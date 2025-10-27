using Microsoft.Data.SqlClient;
using System.ComponentModel.Design;
using System.Data;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Diagnostics;
using System.Media;

namespace QuizMester
{
    public partial class MainWindow : Window
    {
        private readonly AuthService authService = new AuthService();

        Grid currentGrid;

        private readonly GameManager _gameManager;
        private GameSession _session;
        private int _currentQuestionIndex;
        private int _score;
        private bool _skipAvailable;
        private readonly DispatcherTimer _questionTimer = new DispatcherTimer();
        private readonly DispatcherTimer _quizTimer = new DispatcherTimer();
        private int _questionSecondsLeft;
        private TimeSpan _quizTimeLeft;
        private const int QUESTIONS_PER_GAME = 10;
        private const int QUIZ_TOTAL_SECONDS = 5 * 60; // 5:00 as in XAML default
        int? _currentUserId = 1; // <- replace with real user id
        private int? _editingQuestionId = null;

        // New fields for features:
        private bool _fiftyFiftyUsed = false;
        private int? _specialQuestionIndex = null;
        private bool _isCurrentSpecialQuestion = false;
        private bool _specialQuizMode = false;
        private int _specialQuizCorrectCount = 0;
        private Stopwatch _specialQuizStopwatch;
        private int _specialQuizPenaltySeconds = 0; // added for wrong-answer penalty in special mode

        string connectionString = @"Data Source=localhost\sqlexpress;Initial Catalog=QuizmesterDatabase;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";

        public MainWindow()
        {
            InitializeComponent();

            currentGrid = LoginScreen;

            LoginButton.Click += LoginButton_Click;
            RegisterButton.Click += RegisterButton_Click;
            AdminLoginButton.Click += AdminLoginButton_Click;

            // TODO: replace with your real connection string
            _gameManager = new GameManager(connectionString);

            // Timer intervals
            _questionTimer.Interval = TimeSpan.FromSeconds(1);
            _questionTimer.Tick += QuestionTimer_Tick;

            _quizTimer.Interval = TimeSpan.FromSeconds(1);
            _quizTimer.Tick += QuizTimer_Tick;

            // Wire answer buttons
            AnswerA.Click += AnswerButton_Click;
            AnswerB.Click += AnswerButton_Click;
            AnswerC.Click += AnswerButton_Click;
            AnswerD.Click += AnswerButton_Click;

            SkipQuestionButton.Click += SkipQuestionButton_Click;
            QuitQuizButton.Click += QuitQuizButton_Click;

            SaveQuestionButton.Click += SaveQuestionButton_Click;
            CancelQuestionButton.Click += CancelQuestionButton_Click;

            // New handlers
            FiftyFiftyButton.Click += FiftyFiftyButton_Click;
            SpecialModeToggle.Checked += (s,e) => SpecialModeToggleChanged(true);
            SpecialModeToggle.Unchecked += (s,e) => SpecialModeToggleChanged(false);

            LoadScoreboard();

            // Load initial data
            LoadUsers();
            LoadQuestions();
        }

        private void SpecialModeToggleChanged(bool isOn)
        {
            // update UI/tooltip as feedback (toggle only affects next game)
            _specialQuizMode = isOn;
            SpecialModeToggle.Content = isOn ? "Special Quiz Mode ✓" : "Special Quiz Mode";
        }

        //admin
        private void LoadUsers()
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                string query = "SELECT UserId, Username, IsAdmin, CreatedAt FROM Users";
                SqlDataAdapter da = new SqlDataAdapter(query, conn);
                DataTable dt = new DataTable();
                da.Fill(dt);
                UsersDataGrid.ItemsSource = dt.DefaultView;
            }
        }

        private void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int userId)
            {
                if (MessageBox.Show($"Delete user with ID {userId}?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    DeleteUser(userId);
                }
            }
        }

        private void DeleteUser(int userId)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                // Step 1: Delete GameQuestions tied to the user’s games
                string deleteGameQuestions = @"
                    DELETE gq
                    FROM GameQuestions gq
                    INNER JOIN Games g ON gq.GameId = g.GameId
                    WHERE g.UserId = @UserId";
                using (SqlCommand cmd = new SqlCommand(deleteGameQuestions, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.ExecuteNonQuery();
                }

                // Step 2: Delete Games for this user
                string deleteGames = "DELETE FROM Games WHERE UserId = @UserId";
                using (SqlCommand cmd = new SqlCommand(deleteGames, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.ExecuteNonQuery();
                }

                // Step 3: Delete the user
                string deleteUser = "DELETE FROM Users WHERE UserId = @UserId";
                using (SqlCommand cmd = new SqlCommand(deleteUser, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.ExecuteNonQuery();
                }
            }

            LoadUsers();
        }

        private void LoadQuestions(string category = null)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                string query = @"
                    SELECT q.QuestionId AS Id, 
                           c.Name AS Category, 
                           q.QuestionText, 
                           q.TimeLimitSeconds
                    FROM Questions q
                    INNER JOIN Categories c ON q.CategoryId = c.CategoryId";

                if (!string.IsNullOrEmpty(category) && category != "All Categories")
                {
                    query += " WHERE c.Name = @Category";
                }

                SqlCommand cmd = new SqlCommand(query, conn);
                if (!string.IsNullOrEmpty(category) && category != "All Categories")
                    cmd.Parameters.AddWithValue("@Category", category);

                SqlDataAdapter da = new SqlDataAdapter(cmd);
                DataTable dt = new DataTable();
                da.Fill(dt);
                QuestionsDataGrid.ItemsSource = dt.DefaultView;
            }
        }

        private void DeleteQuestion(int questionId)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                // Step 1: Remove game references
                string deleteGameQuestions = "DELETE FROM GameQuestions WHERE QuestionId = @Id";
                SqlCommand cmd1 = new SqlCommand(deleteGameQuestions, conn);
                cmd1.Parameters.AddWithValue("@Id", questionId);
                cmd1.ExecuteNonQuery();

                // Step 2: Remove answers
                string deleteAnswers = "DELETE FROM Answers WHERE QuestionId = @Id";
                SqlCommand cmd2 = new SqlCommand(deleteAnswers, conn);
                cmd2.Parameters.AddWithValue("@Id", questionId);
                cmd2.ExecuteNonQuery();

                // Step 3: Remove question
                string deleteQuestion = "DELETE FROM Questions WHERE QuestionId = @Id";
                SqlCommand cmd3 = new SqlCommand(deleteQuestion, conn);
                cmd3.Parameters.AddWithValue("@Id", questionId);
                cmd3.ExecuteNonQuery();
            }

            LoadQuestions();
        }

        private void DeleteQuestion_Click(object sender, RoutedEventArgs e)
        {
            var row = (sender as Button).DataContext as DataRowView;
            int questionId = Convert.ToInt32(row["Id"]);

            if (MessageBox.Show($"Delete question {questionId}?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                DeleteQuestion(questionId);
            }
        }

        private void AddQuestionButton_Click(object sender, RoutedEventArgs e)
        {
            QuestionDialogTitle.Text = "Add New Question";
            _editingQuestionId = null; // Null means new

            // Clear fields
            QuestionCategoryComboBox.SelectedIndex = -1;
            QuestionDifficultyComboBox.SelectedIndex = -1;
            QuestionTextBox.Text = "";
            AnswerATextBox.Text = "";
            AnswerBTextBox.Text = "";
            AnswerCTextBox.Text = "";
            AnswerDTextBox.Text = "";

            // reset fifty/ special flags
            _fiftyFiftyUsed = false;
            _specialQuestionIndex = null;
            _isCurrentSpecialQuestion = false;

            // Use changeGrid to show the overlay so navigation stays consistent
            changeGrid(QuestionDialogOverlay);
        }

        private void EditQuestion_Click(object sender, RoutedEventArgs e)
        {
            // existing edit implementation (unchanged from previous working version)
            var button = (Button)sender;
            int questionId = Convert.ToInt32(button.Tag);

            string qQuery = @"
        SELECT QuestionId, CategoryId, QuestionText, TimeLimitSeconds
        FROM Questions
        WHERE QuestionId = @Id";

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                int categoryId = -1;
                string difficulty = null;
                string questionText = "";
                int timeLimit = 30;

                using (SqlCommand cmd = new SqlCommand(qQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", questionId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            MessageBox.Show($"Question {questionId} not found.", "Edit Question", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        categoryId = reader["CategoryId"] != DBNull.Value ? Convert.ToInt32(reader["CategoryId"]) : -1;
                        questionText = reader["QuestionText"]?.ToString() ?? "";
                        timeLimit = reader["TimeLimitSeconds"] != DBNull.Value ? Convert.ToInt32(reader["TimeLimitSeconds"]) : 30;
                    }
                }

                // map category id to combo box by name
                if (categoryId > 0)
                {
                    using (SqlCommand catCmd = new SqlCommand("SELECT Name FROM Categories WHERE CategoryId = @CategoryId", conn))
                    {
                        catCmd.Parameters.AddWithValue("@CategoryId", categoryId);
                        var catNameObj = catCmd.ExecuteScalar();
                        if (catNameObj != null && catNameObj != DBNull.Value)
                        {
                            string catName = catNameObj.ToString();
                            QuestionCategoryComboBox.SelectedItem = QuestionCategoryComboBox.Items
                                .Cast<ComboBoxItem>()
                                .FirstOrDefault(i => i.Content.ToString() == catName);
                        }
                        else
                        {
                            QuestionCategoryComboBox.SelectedIndex = -1;
                        }
                    }
                }
                else
                {
                    QuestionCategoryComboBox.SelectedIndex = -1;
                }

                QuestionTextBox.Text = questionText;

                // Load answers for this question
                var answers = new List<(int Id, string Text, bool IsCorrect)>();
                using (SqlCommand ansCmd = new SqlCommand("SELECT AnswerId, AnswerText, IsCorrect FROM Answers WHERE QuestionId = @QId ORDER BY AnswerId", conn))
                {
                    ansCmd.Parameters.AddWithValue("@QId", questionId);
                    using (SqlDataReader ar = ansCmd.ExecuteReader())
                    {
                        while (ar.Read())
                        {
                            answers.Add((
                                Id: Convert.ToInt32(ar["AnswerId"]),
                                Text: ar["AnswerText"]?.ToString() ?? string.Empty,
                                IsCorrect: Convert.ToBoolean(ar["IsCorrect"])
                            ));
                        }
                    }
                }

                // Reset UI fields/tags
                AnswerATextBox.Text = AnswerBTextBox.Text = AnswerCTextBox.Text = AnswerDTextBox.Text = "";
                AnswerATextBox.Tag = AnswerBTextBox.Tag = AnswerCTextBox.Tag = AnswerDTextBox.Tag = null;

                // Put correct answer in A (if present), else use first answer as A.
                var correct = answers.FirstOrDefault(a => a.IsCorrect);
                List<(int Id, string Text, bool IsCorrect)> others;
                if (answers.Any(a => a.IsCorrect))
                {
                    AnswerATextBox.Text = correct.Text;
                    AnswerATextBox.Tag = correct.Id;
                    others = answers.Where(a => a.Id != correct.Id).ToList();
                }
                else
                {
                    if (answers.Count > 0)
                    {
                        AnswerATextBox.Text = answers[0].Text;
                        AnswerATextBox.Tag = answers[0].Id;
                        others = answers.Skip(1).ToList();
                    }
                    else
                    {
                        others = new List<(int, string, bool)>();
                    }
                }

                // Fill B, C, D with remaining answers (if any)
                if (others.Count >= 1)
                {
                    AnswerBTextBox.Text = others[0].Text;
                    AnswerBTextBox.Tag = others[0].Id;
                }
                if (others.Count >= 2)
                {
                    AnswerCTextBox.Text = others[1].Text;
                    AnswerCTextBox.Tag = others[1].Id;
                }
                if (others.Count >= 3)
                {
                    AnswerDTextBox.Text = others[2].Text;
                    AnswerDTextBox.Tag = others[2].Id;
                }

                // Open editor overlay
                _editingQuestionId = questionId;
                QuestionDialogTitle.Text = "Edit Question";
                changeGrid(QuestionDialogOverlay);
            }
        }

        private void SaveQuestionButton_Click(object sender, RoutedEventArgs e)
        {
            string categoryName = (QuestionCategoryComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            string question = QuestionTextBox.Text?.Trim() ?? "";
            string answerA = AnswerATextBox.Text?.Trim() ?? "";
            string answerB = AnswerBTextBox.Text?.Trim() ?? "";
            string answerC = AnswerCTextBox.Text?.Trim() ?? "";
            string answerD = AnswerDTextBox.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(question))
            {
                MessageBox.Show("Question text cannot be empty.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    // Resolve CategoryId from selected name (if any)
                    int? categoryId = null;
                    if (!string.IsNullOrEmpty(categoryName))
                    {
                        using (var catCmd = new SqlCommand("SELECT CategoryId FROM Categories WHERE Name = @Name", conn))
                        {
                            catCmd.Parameters.AddWithValue("@Name", categoryName);
                            var obj = catCmd.ExecuteScalar();
                            if (obj != null && obj != DBNull.Value) categoryId = Convert.ToInt32(obj);
                        }
                    }

                    // Insert or update question
                    if (_editingQuestionId == null)
                    {
                        string insertQ = @"
                    INSERT INTO Questions (CategoryId, QuestionText, TimeLimitSeconds)
                    VALUES (@CategoryId, @QuestionText, @TimeLimitSeconds);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";
                        using (var cmd = new SqlCommand(insertQ, conn))
                        {
                            cmd.Parameters.AddWithValue("@CategoryId", categoryId ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@QuestionText", question);
                            cmd.Parameters.AddWithValue("@TimeLimitSeconds", 30);
                            var newQId = cmd.ExecuteScalar();
                            _editingQuestionId = newQId != null && newQId != DBNull.Value ? Convert.ToInt32(newQId) : null;
                        }
                    }
                    else
                    {
                        string updateQ = @"
                    UPDATE Questions
                    SET CategoryId = @CategoryId, QuestionText = @QuestionText
                    WHERE QuestionId = @Id";
                        using (var cmd = new SqlCommand(updateQ, conn))
                        {
                            cmd.Parameters.AddWithValue("@CategoryId", categoryId ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@QuestionText", question);
                            cmd.Parameters.AddWithValue("@Id", _editingQuestionId.Value);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    if (_editingQuestionId == null)
                    {
                        MessageBox.Show("Could not create or locate question id.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Prepare the answers from UI (A is treated as the correct one)
                    var uiAnswers = new[]
                    {
                new { TextBox = AnswerATextBox, Text = answerA, IsCorrect = true },
                new { TextBox = AnswerBTextBox, Text = answerB, IsCorrect = false },
                new { TextBox = AnswerCTextBox, Text = answerC, IsCorrect = false },
                new { TextBox = AnswerDTextBox, Text = answerD, IsCorrect = false }
            };

                    var updatedAnswerIds = new List<int>();

                    foreach (var ui in uiAnswers)
                    {
                        string text = ui.Text?.Trim();
                        if (string.IsNullOrEmpty(text)) continue;

                        if (ui.TextBox.Tag != null && int.TryParse(ui.TextBox.Tag.ToString(), out int existingAnswerId))
                        {
                            // Update existing answer row
                            string upd = "UPDATE Answers SET AnswerText = @AnswerText, IsCorrect = @IsCorrect WHERE AnswerId = @AnswerId";
                            using (var cmd = new SqlCommand(upd, conn))
                            {
                                cmd.Parameters.AddWithValue("@AnswerText", text);
                                cmd.Parameters.AddWithValue("@IsCorrect", ui.IsCorrect);
                                cmd.Parameters.AddWithValue("@AnswerId", existingAnswerId);
                                cmd.ExecuteNonQuery();
                            }
                            updatedAnswerIds.Add(existingAnswerId);
                        }
                        else
                        {
                            // Insert new answer
                            string ins = "INSERT INTO Answers (QuestionId, AnswerText, IsCorrect) VALUES (@QuestionId, @AnswerText, @IsCorrect); SELECT CAST(SCOPE_IDENTITY() AS INT);";
                            using (var cmd = new SqlCommand(ins, conn))
                            {
                                cmd.Parameters.AddWithValue("@QuestionId", _editingQuestionId.Value);
                                cmd.Parameters.AddWithValue("@AnswerText", text);
                                cmd.Parameters.AddWithValue("@IsCorrect", ui.IsCorrect);
                                var newAnsId = cmd.ExecuteScalar();
                                if (newAnsId != null && newAnsId != DBNull.Value) updatedAnswerIds.Add(Convert.ToInt32(newAnsId));
                            }
                        }
                    }

                    // Remove any leftover answers that the admin removed in the editor
                    if (updatedAnswerIds.Count > 0)
                    {
                        var paramNames = updatedAnswerIds.Select((id, idx) => "@id" + idx).ToArray();
                        string deleteSql = $"DELETE FROM Answers WHERE QuestionId = @QuestionId AND AnswerId NOT IN ({string.Join(",", paramNames)})";
                        using (var cmd = new SqlCommand(deleteSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@QuestionId", _editingQuestionId.Value);
                            for (int i = 0; i < updatedAnswerIds.Count; i++)
                                cmd.Parameters.AddWithValue(paramNames[i], updatedAnswerIds[i]);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        // No answers provided -> delete any existing answers (optional behavior)
                        using (var cmd = new SqlCommand("DELETE FROM Answers WHERE QuestionId = @QuestionId", conn))
                        {
                            cmd.Parameters.AddWithValue("@QuestionId", _editingQuestionId.Value);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                // Refresh UI
                _editingQuestionId = null;
                LoadQuestions();
                changeGrid(AdminScreen);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save question: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelQuestionButton_Click(object sender, RoutedEventArgs e)
        {
            // Close editor and return to admin screen
            _editingQuestionId = null;
            changeGrid(AdminScreen);
        }

        private void CategoryFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryFilterComboBox.SelectedItem is ComboBoxItem selected)
            {
                string category = selected.Content.ToString();
                LoadQuestions(category);
            }
        }

        // 🟢 Login
        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string username = LoginUsernameTextBox.Text.Trim();
            string password = LoginPasswordBox.Password.Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter both username and password.");
                return;
            }

            if (authService.Login(username, password, false))
            {
                _currentUserId = authService.GetUserId(username);
                MessageBox.Show($"{username}Login successful! 🎉");
                changeGrid(GameBoardScreen);
            }
            else
            {
                MessageBox.Show("Invalid username or password.");
            }
        }

        // 🆕 Register
        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string username = RegisterUsernameTextBox.Text.Trim();
            string password = RegisterPasswordBox.Password.Trim();
            string confirm = ConfirmPasswordBox.Password.Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Username and password cannot be empty.");
                return;
            }

            if (password != confirm)
            {
                MessageBox.Show("Passwords do not match.");
                return;
            }

            if (authService.Register(username, password))
            {
                MessageBox.Show("Registration successful! 🎉 You can now log in.");
            }
            else
            {
                MessageBox.Show("Username already exists.");
            }
        }

        // 👑 Admin login
        private void AdminLoginButton_Click(object sender, RoutedEventArgs e)
        {
            string username = LoginUsernameTextBox.Text.Trim();
            string password = LoginPasswordBox.Password.Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter both username and password.");
                return;
            }

            if (authService.Login(username, password, true))
            {
                MessageBox.Show("Admin login successful! 👑");
                _currentUserId = authService.GetUserId(username);
                // TODO: Open admin dashboard
            }
            else
            {
                MessageBox.Show("Invalid admin credentials.");
            }
        }

        private void changeGrid(Grid newgrid)
        {
            try
            {
                if (currentGrid != null)
                {
                    currentGrid.IsEnabled = false;
                    currentGrid.Visibility = Visibility.Collapsed;

                    newgrid.IsEnabled = true;
                    newgrid.Visibility = Visibility.Visible;

                    currentGrid = newgrid;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"failed to change grid, {ex}");
            }
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            authService.Logout();
            changeGrid(LoginScreen);
        }

        private void AdminLoginButton_Click_1(object sender, RoutedEventArgs e)
        {
            changeGrid(AdminScreen);
        } 

        private void AdminLogoutButton_Click_1(object sender, RoutedEventArgs e)
        {
            authService.Logout();
            changeGrid(LoginScreen);
        }

        private void ContinueGameButton_Click(object sender, RoutedEventArgs e)
        {
            // play agian
        }

        private void ViewScoreboardButton_Click(object sender, RoutedEventArgs e)
        {
            changeGrid(ScoreboardScreen);
        }

        private void BackToMenuButton_Click(object sender, RoutedEventArgs e)
        {
            changeGrid(GameBoardScreen);
        }

        private void BackToGameButton_Click(object sender, RoutedEventArgs e)
        {
            changeGrid(GameBoardScreen);
        }

        private void ScoreboardButton_Click(object sender, RoutedEventArgs e)
        {
            LoadScoreboard();
            changeGrid(ScoreboardScreen);
        }

        private void CategoryWedge_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Ellipse clickedWedge && clickedWedge.Tag is string category)
            {
                bool startSpecialMode = SpecialModeToggle.IsChecked == true;
                StartGame(category, startSpecialMode);
                changeGrid(QuizScreen);
            }
        }

        public async void StartGame(string categoryName, bool specialMode = false)
        {
            try
            {
                QuizScreen.Visibility = Visibility.Visible;

                _specialQuizMode = specialMode;
                _fiftyFiftyUsed = false;
                _specialQuestionIndex = null;
                _isCurrentSpecialQuestion = false;
                _specialQuizCorrectCount = 0;
                _specialQuizPenaltySeconds = 0;

                if (_specialQuizMode)
                {
                    // Special quiz: random questions across all categories
                    _session = await _gameManager.CreateSpecialGameSessionAsync(_currentUserId.Value, QUESTIONS_PER_GAME);
                    // stopwatch for total time
                    _specialQuizStopwatch = new Stopwatch();
                    _specialQuizStopwatch.Start();
                    // start quiz timer to update elapsed display
                    _quizTimer.Start();
                }
                else
                {
                    _session = await _gameManager.CreateGameSessionAsync(_currentUserId.Value, categoryName, QUESTIONS_PER_GAME);

                    // select special question randomly among first 20
                    if (_session.Questions.Count > 0)
                    {
                        var maxIndex = Math.Min(20, _session.Questions.Count);
                        var rnd = new Random();
                        _specialQuestionIndex = rnd.Next(0, maxIndex);
                    }

                    // start the quiz timer (total) as before
                    _quizTimeLeft = TimeSpan.FromSeconds(QUIZ_TOTAL_SECONDS);
                    UpdateQuizTimerText();
                    _quizTimer.Start();
                }

                _currentQuestionIndex = 0;
                _score = 0;
                _skipAvailable = true;
                SkipStatusText.Text = "(1 skip available)";
                FeedbackText.Text = "";

                CategoryTitleText.Text = $"🔬 {categoryName.ToUpper()} QUIZ";
                CurrentScoreText.Text = _score.ToString();

                await LoadQuestionAsync(_currentQuestionIndex);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start game: " + ex.Message);
                QuizScreen.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadQuestionAsync(int index)
        {
            if (_session == null || index < 0 || index >= _session.Questions.Count)
            {
                await EndGameAsync();
                return;
            }
                    
            var q = _session.Questions[index];

            // UI reset
            QuestionText.Text = q.QuestionText;
            QuestionProgressText.Text = $"Question {index + 1} of {_session.Questions.Count}";
            QuestionProgressBar.Value = ((index) / (double)_session.Questions.Count) * 100.0;

            // Reset question border style
            QuestionBorder.Background = System.Windows.Media.Brushes.White;
            QuestionText.FontSize = 22;
            QuestionText.FontWeight = FontWeights.SemiBold;
            _isCurrentSpecialQuestion = false;

            // put answers
            var buttons = new[] { AnswerA, AnswerB, AnswerC, AnswerD };
            var rnd = new Random();
            var shuffledAnswers = q.Answers.OrderBy(a => rnd.Next()).ToList();

            for (int i = 0; i < buttons.Length; i++)
            {
                if (i < shuffledAnswers.Count)
                {
                    buttons[i].Content = $"{(char)('A' + i)}) {shuffledAnswers[i].AnswerText}";
                    buttons[i].Tag = shuffledAnswers[i];
                    buttons[i].IsEnabled = true;
                    buttons[i].Visibility = Visibility.Visible;
                    buttons[i].BorderBrush = System.Windows.Media.Brushes.LightGray;
                    buttons[i].Background = System.Windows.Media.Brushes.White;
                }
                else
                {
                    buttons[i].Content = "";
                    buttons[i].Tag = null;
                    buttons[i].IsEnabled = false;
                    buttons[i].Visibility = Visibility.Collapsed;
                }
            }

            // per-question timer only for normal mode
            if (!_specialQuizMode)
            {
                _questionSecondsLeft = q.TimeLimitSeconds > 0 ? q.TimeLimitSeconds : 30;
                UpdateQuestionTimerText();
                _questionTimer.Start();
            }
            else
            {
                // no per-question timer in special quiz mode
                QuestionTimerText.Text = "-";
            }

            // Special question decoration (only for normal mode)
            if (!_specialQuizMode && _specialQuestionIndex.HasValue && index == _specialQuestionIndex.Value)
            {
                _isCurrentSpecialQuestion = true;
                QuestionBorder.Background = System.Windows.Media.Brushes.LightGoldenrodYellow;
                QuestionText.FontSize = 24;
                QuestionText.FontWeight = FontWeights.Bold;
                // play a short attention sound
                try { SystemSounds.Asterisk.Play(); } catch { }
            }

            // Reset joker availability for each question (only if you want 50/50 once per game; keep it per game)
            // We leave _fiftyFiftyUsed as-is (single-use per game). To make it per-question, set to false here.

            FeedbackText.Text = "";
            FiftyFiftyButton.IsEnabled = !_fiftyFiftyUsed;
        }

        private async void AnswerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null) return;
            if (!(sender is Button btn)) return;
            if (!(btn.Tag is AnswerDto selectedAnswer)) return;

            DisableAnswerButtons();

            // stop per-question timer for normal mode
            if (!_specialQuizMode)
                _questionTimer.Stop();

            var q = _session.Questions[_currentQuestionIndex];

            var isCorrect = selectedAnswer.IsCorrect;
            if (isCorrect)
            {
                if (_specialQuizMode)
                {
                    _score += 10; // regular scoring in special mode (no bonus)
                    _specialQuizCorrectCount++;
                    FeedbackText.Text = $"Correct! +10 ({_specialQuizCorrectCount}/10)";
                }
                else
                {
                    // normal mode: award extra when special question
                    int points = 10;
                    if (_isCurrentSpecialQuestion)
                    {
                        points += 15; // extra bonus for special
                        FeedbackText.Text = $"Special QUESTION! Correct! +{points}";
                    }
                    else
                    {
                        FeedbackText.Text = $"Correct! +{points}";
                    }
                    _score += points;
                }
            }
            else
            {
                if (_specialQuizMode)
                {
                    // wrong answer in special mode => +5 seconds penalty
                    _specialQuizPenaltySeconds += 5;
                    FeedbackText.Text = "Wrong! +5s penalty";
                }
                else
                {
                    FeedbackText.Text = $"Wrong! Correct answer: {GetCorrectAnswerText(q)}";
                }
            }

            CurrentScoreText.Text = _score.ToString();

            // Show feedback visuals
            ShowAnswerFeedback(selectedAnswer.AnswerId, q);

            // record GameQuestion (note: special mode still records)
            await _gameManager.RecordGameQuestionAsync(_session.GameId, q.QuestionId, selectedAnswer.AnswerId, isCorrect, DateTime.UtcNow);

            // short pause then next
            await Task.Delay(900);

            // reset fifty/ fifty button for next question if per-game; we keep it single-use per game (so don't reset)
            // _fiftyFiftyUsed = false;

            // check end condition for special quiz mode (10 correct answers)
            if (_specialQuizMode)
            {
                if (_specialQuizCorrectCount >= 10)
                {
                    await EndGameAsync();
                    changeGrid(GameBoardScreen);
                    return;
                }
            }

            _currentQuestionIndex++;
            if (_currentQuestionIndex >= _session.Questions.Count)
            {
                await EndGameAsync();
                changeGrid(GameBoardScreen);
            }
            else
            {
                await LoadQuestionAsync(_currentQuestionIndex);
            }
        }

        private void ShowAnswerFeedback(int selectedAnswerId, QuestionDto q)
        {
            var buttons = new[] { AnswerA, AnswerB, AnswerC, AnswerD };
            foreach (var b in buttons)
            {
                if (!(b.Tag is AnswerDto aDto)) continue;
                if (aDto.IsCorrect)
                {
                    b.BorderBrush = System.Windows.Media.Brushes.Gold;
                }
                if (aDto.AnswerId == selectedAnswerId && !aDto.IsCorrect)
                {
                    b.BorderBrush = System.Windows.Media.Brushes.Red;
                }
            }
        }

        private void DisableAnswerButtons()
        {
            AnswerA.IsEnabled = AnswerB.IsEnabled = AnswerC.IsEnabled = AnswerD.IsEnabled = false;
        }

        private void EnableAnswerButtons()
        {
            if (AnswerA.Tag != null) AnswerA.IsEnabled = true;
            if (AnswerB.Tag != null) AnswerB.IsEnabled = true;
            if (AnswerC.Tag != null) AnswerC.IsEnabled = true;
            if (AnswerD.Tag != null) AnswerD.IsEnabled = true;
        }

        // 50/50 Joker implementation (single-use for the whole game)
        private void FiftyFiftyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_fiftyFiftyUsed || _session == null) return;

            var buttons = new[] { AnswerA, AnswerB, AnswerC, AnswerD };

            // Identify correct button and incorrect ones
            Button correctButton = null;
            var incorrectButtons = new List<Button>();
            foreach (var b in buttons)
            {
                if (!(b.Tag is AnswerDto a)) continue;
                if (a.IsCorrect) correctButton = b;
                else incorrectButtons.Add(b);
            }

            if (correctButton == null || incorrectButtons.Count == 0) return;

            // pick one random incorrect to keep
            var rnd = new Random();
            var keepIncorrect = incorrectButtons[rnd.Next(incorrectButtons.Count)];
                
            // disable all except correctButton and keepIncorrect
            foreach (var b in buttons)
            {
                if (b != correctButton && b != keepIncorrect)
                {
                    b.IsEnabled = false;
                    b.Visibility = Visibility.Visible; // keep visible but disabled
                    b.Opacity = 0.6;
                }
            }

            _fiftyFiftyUsed = true;
            FiftyFiftyButton.IsEnabled = false;
            FeedbackText.Text = "50/50 used";
        }

        private async void SkipQuestionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_skipAvailable) return;
            _skipAvailable = false;
            SkipStatusText.Text = "(0 skips)";
            SkipQuestionButton.IsEnabled = false;

            if (!_specialQuizMode)
                _questionTimer.Stop();

            var q = _session.Questions[_currentQuestionIndex];
            await _gameManager.RecordGameQuestionAsync(_session.GameId, q.QuestionId, null, null, DateTime.UtcNow);

            FeedbackText.Text = "Skipped question.";

            await Task.Delay(600);
            _currentQuestionIndex++;
            if (_currentQuestionIndex >= _session.Questions.Count)
            {
                await EndGameAsync();
                changeGrid(GameBoardScreen);
            }
            else
            {
                await LoadQuestionAsync(_currentQuestionIndex);
            }
        }

        private async void QuitQuizButton_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Quit quiz? Progress will be saved.", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
            await EndGameAsync();
            changeGrid(GameBoardScreen);
        }

        private async Task EndGameAsync()
        {
            // stop timers & stopwatch
            _questionTimer.Stop();
            _quizTimer.Stop();
            if (_specialQuizStopwatch != null && _specialQuizStopwatch.IsRunning)
            {
                _specialQuizStopwatch.Stop();
            }

            // finalize (save final score + endtime)
            if (_session != null)
            {
                await _gameManager.FinalizeGameAsync(_session.GameId, _score);
            }

            // If the special mode was used, compute total time with penalties and show that
            if (_specialQuizMode)
            {
                var elapsed = _specialQuizStopwatch?.Elapsed.TotalSeconds ?? 0;
                var totalWithPenalty = TimeSpan.FromSeconds(elapsed + _specialQuizPenaltySeconds);
                MessageBox.Show($"Special Quiz finished! Final score: {_score}\nTime (incl. penalties): {totalWithPenalty:mm\\:ss}", "Game Over", MessageBoxButton.OK, MessageBoxImage.Information);

                // reset special mode state
                _specialQuizMode = false;
                SpecialModeToggle.IsChecked = false;
                SpecialModeToggle.Content = "Special Quiz Mode";
            }
            else
            {
                MessageBox.Show($"Game finished! Final score: {_score}", "Game Over", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            // Reset UI
            QuizScreen.Visibility = Visibility.Collapsed;

            // cleanup session
            _session = null;
            _fiftyFiftyUsed = false;
            _specialQuestionIndex = null;
            _isCurrentSpecialQuestion = false;
        }

        private void QuestionTimer_Tick(object sender, EventArgs e)
        {
            _questionSecondsLeft--;
            if (_questionSecondsLeft <= 0)
            {
                _questionTimer.Stop();
                OnQuestionTimeExpired();
            }
            UpdateQuestionTimerText();
        }

        private async void OnQuestionTimeExpired()
        {
            FeedbackText.Text = "Time's up!";
            DisableAnswerButtons();

            var q = _session.Questions[_currentQuestionIndex];

            await _gameManager.RecordGameQuestionAsync(_session.GameId, q.QuestionId, null, null, DateTime.UtcNow);

            FeedbackText.Text = $"Time's up! Correct: {GetCorrectAnswerText(q)}";

            await Task.Delay(900);

            _currentQuestionIndex++;
            if (_currentQuestionIndex >= _session.Questions.Count)
                await EndGameAsync();
            else
                await LoadQuestionAsync(_currentQuestionIndex);
        }

        private void QuizTimer_Tick(object sender, EventArgs e)
        {
            if (_specialQuizMode)
            {
                // show elapsed + penalty
                var elapsed = _specialQuizStopwatch?.Elapsed.TotalSeconds ?? 0;
                var totalSeconds = elapsed + _specialQuizPenaltySeconds;
                var t = TimeSpan.FromSeconds(totalSeconds);
                QuizTimerText.Text = $"{t.Minutes}:{t.Seconds:D2}";
            }
            else
            {
                _quizTimeLeft = _quizTimeLeft.Add(TimeSpan.FromSeconds(-1));
                if (_quizTimeLeft.TotalSeconds <= 0)
                {
                    _quizTimer.Stop();
                    _ = Dispatcher.InvokeAsync(async () => await EndGameAsync());
                }
                UpdateQuizTimerText();
            }
        }

        private void UpdateQuestionTimerText()
        {
            QuestionTimerText.Text = _questionSecondsLeft.ToString();
        }

        private void UpdateQuizTimerText()
        {
            QuizTimerText.Text = $"{_quizTimeLeft.Minutes}:{_quizTimeLeft.Seconds:D2}";
        }

        private void LoadScoreboard()
        {
            var entries = new List<ScoreboardEntry>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                string query = "SELECT Username, FinalScore, EndTime FROM dbo.Scoreboard";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    int rank = 1;
                    while (reader.Read())
                    {
                        entries.Add(new ScoreboardEntry
                        {
                            Rank = rank++,
                            PlayerName = reader["Username"].ToString(),
                            Score = Convert.ToInt32(reader["FinalScore"]),
                            Date = reader["EndTime"] != DBNull.Value
                               ? Convert.ToDateTime(reader["EndTime"])
                               : (DateTime?)null

                        });
                    }
                }
            }

            ScoreboardDataGrid.ItemsSource = entries;
        }
    }
}