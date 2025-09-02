using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

namespace QuizMester
{
    public class AuthService
    {
        private readonly string connectionString =
            @"Data Source=localhost\sqlexpress;Initial Catalog=QuizmesterDatabase;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";

        // 🔐 Hash passwords with SHA256
        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in bytes)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // 🟢 User login
        public bool Login(string username, string password, bool isAdmin = false)
        {
            string hash = HashPassword(password);

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT COUNT(*) FROM Users WHERE Username=@u AND PasswordHash=@p AND IsAdmin=@a";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@u", username);
                    cmd.Parameters.AddWithValue("@p", hash);
                    cmd.Parameters.AddWithValue("@a", isAdmin ? 1 : 0);

                    int count = (int)cmd.ExecuteScalar();
                    return count > 0;
                }
            }
        }

        // 🆕 Register
        public bool Register(string username, string password)
        {
            string hash = HashPassword(password);

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                // Check if username exists
                string checkQuery = "SELECT COUNT(*) FROM Users WHERE Username=@u";
                using (SqlCommand checkCmd = new SqlCommand(checkQuery, conn))
                {
                    checkCmd.Parameters.AddWithValue("@u", username);
                    int exists = (int)checkCmd.ExecuteScalar();
                    if (exists > 0)
                        return false;
                }

                // Insert new user
                string insertQuery = "INSERT INTO Users (Username, PasswordHash) VALUES (@u, @p)";
                using (SqlCommand insertCmd = new SqlCommand(insertQuery, conn))
                {
                    insertCmd.Parameters.AddWithValue("@u", username);
                    insertCmd.Parameters.AddWithValue("@p", hash);
                    insertCmd.ExecuteNonQuery();
                }
            }

            return true;
        }
    }
}
