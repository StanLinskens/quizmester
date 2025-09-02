using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;

namespace QuizMester
{
    public class QuestionDto
    {
        public int QuestionId { get; set; }
        public int CategoryId { get; set; }
        public string QuestionText { get; set; }
        public int TimeLimitSeconds { get; set; }
        public List<AnswerDto> Answers { get; set; } = new();
    }

    public class AnswerDto
    {
        public int AnswerId { get; set; }
        public int QuestionId { get; set; }
        public string AnswerText { get; set; }
        public bool IsCorrect { get; set; }
    }

    public class GameSession
    {
        public int GameId { get; set; }
        public int UserId { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; }
        public List<QuestionDto> Questions { get; set; } = new();
    }

    public class GameManager
    {
        private readonly string _connectionString;

        public GameManager(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<int?> GetCategoryIdByNameAsync(string categoryName)
        {
            const string sql = "SELECT CategoryId FROM Categories WHERE Name = @Name";
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Name", categoryName);
            var result = await cmd.ExecuteScalarAsync();
            return result == null ? null : (int?)Convert.ToInt32(result);
        }

        public async Task<GameSession> CreateGameSessionAsync(int userId, string categoryName, int questionCount = 10)
        {
            var catIdNullable = await GetCategoryIdByNameAsync(categoryName);
            if (catIdNullable == null) throw new InvalidOperationException("Category not found: " + categoryName);
            var categoryId = catIdNullable.Value;

            // Create Games row
            int gameId;
            const string insertGameSql = @"INSERT INTO Games (UserId, CategoryId) OUTPUT INSERTED.GameId VALUES (@UserId, @CategoryId)";
            await using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(insertGameSql, conn);
                cmd.Parameters.AddWithValue("@UserId", userId);
                cmd.Parameters.AddWithValue("@CategoryId", categoryId);
                gameId = (int)await cmd.ExecuteScalarAsync();
            }

            // Load random questions for category
            var questions = await GetRandomQuestionsWithAnswersAsync(categoryId, questionCount);

            return new GameSession
            {
                GameId = gameId,
                UserId = userId,
                CategoryId = categoryId,
                CategoryName = categoryName,
                Questions = questions
            };
        }

        private async Task<List<QuestionDto>> GetRandomQuestionsWithAnswersAsync(int categoryId, int count)
        {
            // Note: TOP @count with parameterization for SQL Server requires building SQL (can't parameterize TOP in older servers).
            // We'll use TOP (@count) as a workaround with sp_executesql, but simpler: fetch a bit more and take 'count' in memory.
            const string sql = @"
SELECT q.QuestionId, q.CategoryId, q.QuestionText, q.TimeLimitSeconds
FROM Questions q
WHERE q.CategoryId = @CategoryId
ORDER BY NEWID()"; // RANDOM order

            var list = new List<QuestionDto>();
            await using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                await using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@CategoryId", categoryId);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var q = new QuestionDto
                        {
                            QuestionId = reader.GetInt32(0),
                            CategoryId = reader.GetInt32(1),
                            QuestionText = reader.GetString(2),
                            TimeLimitSeconds = reader.IsDBNull(3) ? 30 : reader.GetInt32(3)
                        };
                        list.Add(q);
                    }
                }

                // Trim to requested count:
                if (list.Count > count) list = list.GetRange(0, count);

                // Load answers for all selected questions in one query
                if (list.Count > 0)
                {
                    var ids = string.Join(",", list.ConvertAll(q => q.QuestionId));
                    var answersSql = $@"
SELECT AnswerId, QuestionId, AnswerText, IsCorrect
FROM Answers
WHERE QuestionId IN ({ids})
ORDER BY AnswerId"; // preserve stable order
                    await using var cmd2 = new SqlCommand(answersSql, conn);
                    await using var reader2 = await cmd2.ExecuteReaderAsync();
                    var answerGroups = new Dictionary<int, List<AnswerDto>>();
                    while (await reader2.ReadAsync())
                    {
                        var a = new AnswerDto
                        {
                            AnswerId = reader2.GetInt32(0),
                            QuestionId = reader2.GetInt32(1),
                            AnswerText = reader2.GetString(2),
                            IsCorrect = reader2.GetBoolean(3)
                        };
                        if (!answerGroups.ContainsKey(a.QuestionId)) answerGroups[a.QuestionId] = new List<AnswerDto>();
                        answerGroups[a.QuestionId].Add(a);
                    }

                    foreach (var q in list)
                    {
                        if (answerGroups.TryGetValue(q.QuestionId, out var answers))
                            q.Answers = answers;
                    }
                }
            }

            return list;
        }

        public async Task RecordGameQuestionAsync(int gameId, int questionId, int? userAnswerId, bool? isCorrect, DateTime? answeredAt)
        {
            const string sql = @"
INSERT INTO GameQuestions (GameId, QuestionId, UserAnswerId, IsCorrect, AnsweredAt)
VALUES (@GameId, @QuestionId, @UserAnswerId, @IsCorrect, @AnsweredAt)";
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@GameId", gameId);
            cmd.Parameters.AddWithValue("@QuestionId", questionId);
            cmd.Parameters.AddWithValue("@UserAnswerId", (object?)userAnswerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsCorrect", (object?)isCorrect ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AnsweredAt", (object?)answeredAt ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task FinalizeGameAsync(int gameId, int finalScore)
        {
            const string sql = @"UPDATE Games SET FinalScore = @FinalScore, EndTime = GETDATE() WHERE GameId = @GameId";
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@FinalScore", finalScore);
            cmd.Parameters.AddWithValue("@GameId", gameId);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
