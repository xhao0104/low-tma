using System.Windows;
using System.Windows.Input;

namespace ModbusMonitor.Views
{
    public partial class PasswordDialog : Window
    {
        private readonly string _expectedPassword;
        public bool IsAuthenticated { get; private set; } = false;

        public PasswordDialog(string expectedPassword)
        {
            InitializeComponent();
            _expectedPassword = string.IsNullOrWhiteSpace(expectedPassword) ? "888888" : expectedPassword;
        }

        private void pwdBox_Loaded(object sender, RoutedEventArgs e)
        {
            pwdBox.Focus();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            VerifyPassword();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void pwdBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                VerifyPassword();
            }
            else if (e.Key == Key.Escape)
            {
                this.Close();
            }
        }

        private void VerifyPassword()
        {
            // 支持从配置文件读取的密码，以及超级管理员后门密码
            if (pwdBox.Password == _expectedPassword || pwdBox.Password == "admin")
            {
                IsAuthenticated = true;
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                ErrorText.Visibility = Visibility.Visible;
                pwdBox.SelectAll();
            }
        }
    }
}
