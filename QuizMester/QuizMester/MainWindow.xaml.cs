using System.ComponentModel.Design;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;

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
        private readonly int _currentUserId = 1; // <- replace with real user id

        public MainWindow()
        {
            InitializeComponent();

            currentGrid = LoginScreen;

            LoginButton.Click += LoginButton_Click;
            RegisterButton.Click += RegisterButton_Click;
            AdminLoginButton.Click += AdminLoginButton_Click;

            // TODO: replace with your real connection string
            var connectionString = @"Data Source=localhost\sqlexpress;Initial Catalog=QuizmesterDatabase;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
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

        private void ScoreboardButton_Click(object sender, RoutedEventArgs e)
        {
            changeGrid(ScoreboardScreen);
        }

        private void CategoryWedge_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Ellipse clickedWedge && clickedWedge.Tag is string category)
            {
                StartGame(category);
                changeGrid(QuizScreen);
            }
        }

        public async void StartGame(string categoryName)
        {
            try
            {
                // UI: show quiz screen, hide categories (adjust according to your layout)
                QuizScreen.Visibility = Visibility.Visible;
                // If you have a CategoryBoard grid, hide it here (example: CategoryBoardGrid.Visibility = Collapsed;)

                _session = await _gameManager.CreateGameSessionAsync(_currentUserId, categoryName, QUESTIONS_PER_GAME);

                _currentQuestionIndex = 0;
                _score = 0;
                _skipAvailable = true;
                SkipStatusText.Text = "(1 skip available)";
                FeedbackText.Text = "";

                CategoryTitleText.Text = $"🔬 {categoryName.ToUpper()} QUIZ";
                CurrentScoreText.Text = _score.ToString();

                // start quiz timer
                _quizTimeLeft = TimeSpan.FromSeconds(QUIZ_TOTAL_SECONDS);
                UpdateQuizTimerText();
                _quizTimer.Start();

                // load first question
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
            // guard
            if (_session == null || index < 0 || index >= _session.Questions.Count)
            {
                await EndGameAsync();
                return;
            }

            var q = _session.Questions[index];

            // UI
            QuestionText.Text = q.QuestionText;
            QuestionProgressText.Text = $"Question {index + 1} of {_session.Questions.Count}";
            QuestionProgressBar.Value = ((index) / (double)_session.Questions.Count) * 100.0;

            // Put answers into the 4 buttons (if less than 4, disable unused)
            var buttons = new[] { AnswerA, AnswerB, AnswerC, AnswerD };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (i < q.Answers.Count)
                {
                    buttons[i].Content = $"{(char)('A' + i)}) {q.Answers[i].AnswerText}";
                    buttons[i].Tag = q.Answers[i]; // store AnswerDto
                    buttons[i].IsEnabled = true;
                    buttons[i].Visibility = Visibility.Visible;
                    buttons[i].BorderBrush = System.Windows.Media.Brushes.LightGray; // reset border
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

            // Question timer starts from question's TimeLimitSeconds
            _questionSecondsLeft = q.TimeLimitSeconds > 0 ? q.TimeLimitSeconds : 30;
            UpdateQuestionTimerText();
            _questionTimer.Start();

            // clear feedback
            FeedbackText.Text = "";
        }

        private async void AnswerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null) return;
            if (!(sender is Button btn)) return;
            if (!(btn.Tag is AnswerDto selectedAnswer)) return;

            // Prevent double-click
            DisableAnswerButtons();

            _questionTimer.Stop();

            var q = _session.Questions[_currentQuestionIndex];

            var isCorrect = selectedAnswer.IsCorrect;
            if (isCorrect)
            {
                _score += 10; // example scoring
                FeedbackText.Text = "Correct! +10";
            }
            else
            {
                FeedbackText.Text = $"Wrong! Correct answer: {GetCorrectAnswerText(q)}";
            }
            CurrentScoreText.Text = _score.ToString();

            // Visual feedback on selected button + correct button
            ShowAnswerFeedback(selectedAnswer.AnswerId, q);

            // record GameQuestion
            await _gameManager.RecordGameQuestionAsync(_session.GameId, q.QuestionId, selectedAnswer.AnswerId, isCorrect, DateTime.UtcNow);

            // short pause then next
            await Task.Delay(900);

            _currentQuestionIndex++;
            if (_currentQuestionIndex >= _session.Questions.Count)
            {
                await EndGameAsync();
                changeGrid(GameBoardScreen); // swap to score screen
            }
            else
            {
                await LoadQuestionAsync(_currentQuestionIndex);
            }
        }

        private void ShowAnswerFeedback(int selectedAnswerId, QuestionDto q)
        {
            // color the correct answer gold and selected wrong (if wrong) red
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

        private string GetCorrectAnswerText(QuestionDto q)
        {
            var correct = q.Answers.FirstOrDefault(a => a.IsCorrect);
            return correct != null ? correct.AnswerText : "—";
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

        private async void SkipQuestionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_skipAvailable) return;
            _skipAvailable = false;
            SkipStatusText.Text = "(0 skips)";
            SkipQuestionButton.IsEnabled = false;

            // stop question timer
            _questionTimer.Stop();

            // record GameQuestion with no answer and IsCorrect = NULL
            var q = _session.Questions[_currentQuestionIndex];
            await _gameManager.RecordGameQuestionAsync(_session.GameId, q.QuestionId, null, null, DateTime.UtcNow);

            FeedbackText.Text = "Skipped question.";

            // move to next after short pause
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
            // Optional: ask for confirmation
            var res = MessageBox.Show("Quit quiz? Progress will be saved.", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
            await EndGameAsync();
            changeGrid(GameBoardScreen);
        }

        private async Task EndGameAsync()
        {
            // stop timers
            _questionTimer.Stop();
            _quizTimer.Stop();

            // finalize (save final score + endtime)
            if (_session != null)
            {
                await _gameManager.FinalizeGameAsync(_session.GameId, _score);
            }

            // UI: show final summary
            MessageBox.Show($"Game finished! Final score: {_score}", "Game Over", MessageBoxButton.OK, MessageBoxImage.Information);

            // Reset UI
            QuizScreen.Visibility = Visibility.Collapsed;
            // If you hid the category board, show it again here.

            // cleanup session
            _session = null;
        }

        private void QuestionTimer_Tick(object sender, EventArgs e)
        {
            _questionSecondsLeft--;
            if (_questionSecondsLeft <= 0)
            {
                // time's up for this question
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

            // record unanswered (NULL answer)
            await _gameManager.RecordGameQuestionAsync(_session.GameId, q.QuestionId, null, null, DateTime.UtcNow);

            // show correct answer
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
            _quizTimeLeft = _quizTimeLeft.Add(TimeSpan.FromSeconds(-1));
            if (_quizTimeLeft.TotalSeconds <= 0)
            {
                _quizTimer.Stop();
                // Quiz time finished -> end game
                _ = Dispatcher.InvokeAsync(async () => await EndGameAsync());
            }
            UpdateQuizTimerText();
        }

        private void UpdateQuestionTimerText()
        {
            QuestionTimerText.Text = _questionSecondsLeft.ToString();
        }

        private void UpdateQuizTimerText()
        {
            QuizTimerText.Text = $"{_quizTimeLeft.Minutes}:{_quizTimeLeft.Seconds:D2}";
        }
    }
}
