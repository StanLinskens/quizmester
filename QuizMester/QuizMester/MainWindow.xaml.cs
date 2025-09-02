using System.ComponentModel.Design;
using System.Windows;
using System.Windows.Controls;

namespace QuizMester
{
    public partial class MainWindow : Window
    {
        private readonly AuthService authService = new AuthService();

        Grid currentGrid;

        public MainWindow()
        {
            InitializeComponent();

            currentGrid = LoginScreen;

            LoginButton.Click += LoginButton_Click;
            RegisterButton.Click += RegisterButton_Click;
            AdminLoginButton.Click += AdminLoginButton_Click;
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
                MessageBox.Show("Login successful! 🎉");
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
            changeGrid(LoginScreen);
        }

        private void AdminLoginButton_Click_1(object sender, RoutedEventArgs e)
        {
            changeGrid(AdminScreen);
        }

        private void AdminLogoutButton_Click_1(object sender, RoutedEventArgs e)
        {
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

        private void SkipQuestionButton_Click(object sender, RoutedEventArgs e)
        {
            // skip question
        }

        private void QuitQuizButton_Click(object sender, RoutedEventArgs e)
        {
            changeGrid(GameBoardScreen);
        }

        private void ScoreboardButton_Click(object sender, RoutedEventArgs e)
        {
            changeGrid(ScoreboardScreen);
        }
    }
}
