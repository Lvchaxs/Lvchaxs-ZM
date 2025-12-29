using Lvchaxs_ZH.Models;
using Lvchaxs_ZH.Services;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Lvchaxs_ZH
{
    public partial class AddEditAccountWindow : Window
    {
        public Account Account { get; private set; }
        public bool IsEditMode { get; private set; }
        private bool _isClosing = false;
        private bool _isPasswordVisible = false;

        public delegate void ShowToastDelegate(string title, string message, bool isSuccess = true);
        public event ShowToastDelegate ShowToastRequested;

        public AddEditAccountWindow(Account account = null)
        {
            InitializeComponent();

            if (account == null)
            {
                // 创建新账号
                Account = new Account();
                Account.CreatedTime = DateTime.Now;
            }
            else
            {
                // 编辑现有账号
                Account = account;
            }

            IsEditMode = account != null;

            InitializeWindow();
            UpdateWatermarks();
            UpdatePasswordIcon();

            this.Loaded += Window_Loaded;
        }

        private void InitializeWindow()
        {
            windowTitle.Text = IsEditMode ? "编辑账号" : "添加账号";

            if (IsEditMode)
            {
                uidTextBox.Text = Account.Uid;
                levelTextBox.Text = Account.Level.ToString();
                nicknameTextBox.Text = Account.Nickname;
                usernameTextBox.Text = Account.Username;
                passwordBox.Password = Account.Password;
                passwordTextBox.Text = Account.Password;
            }

            SetPasswordVisibility(false);
            UpdatePasswordStrength(IsEditMode ? Account.Password : "");
        }

        private void SetPasswordVisibility(bool isVisible)
        {
            _isPasswordVisible = isVisible;

            if (isVisible)
            {
                passwordTextBox.Text = passwordBox.Password;
                passwordTextBox.Visibility = Visibility.Visible;
                passwordBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                passwordBox.Password = passwordTextBox.Text;
                passwordBox.Visibility = Visibility.Visible;
                passwordTextBox.Visibility = Visibility.Collapsed;
            }

            UpdatePasswordIcon();
            UpdateWatermarks();
        }

        private void UpdatePasswordIcon()
        {
            eyeOpenIcon.Visibility = _isPasswordVisible ? Visibility.Visible : Visibility.Collapsed;
            eyeClosedIcon.Visibility = _isPasswordVisible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PlayAnimation("FadeInAnimation", () =>
            {
                mainBorder.Opacity = 1;
                (mainBorder.RenderTransform as ScaleTransform)?.SetValue(ScaleTransform.ScaleXProperty, 1.0);
                (mainBorder.RenderTransform as ScaleTransform)?.SetValue(ScaleTransform.ScaleYProperty, 1.0);
            });
        }

        private void PlayAnimation(string animationKey, Action onCompleted = null)
        {
            if (FindResource(animationKey) is Storyboard animation)
            {
                if (onCompleted != null)
                    animation.Completed += (s, args) => onCompleted();
                animation.Begin(mainBorder);
            }
        }

        private void UpdateWatermarks()
        {
            uidWatermarkText.Visibility = string.IsNullOrWhiteSpace(uidTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            levelWatermarkText.Visibility = string.IsNullOrWhiteSpace(levelTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            nicknameWatermarkText.Visibility = string.IsNullOrWhiteSpace(nicknameTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            usernameWatermarkText.Visibility = string.IsNullOrWhiteSpace(usernameTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            passwordWatermarkText.Visibility = string.IsNullOrWhiteSpace(_isPasswordVisible ? passwordTextBox.Text : passwordBox.Password)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdatePasswordStrength(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                passwordStrengthText.Text = "密码强度：";
                passwordStrengthBar.Width = 0;
                passwordStrengthBar.Background = Brushes.Transparent;
                return;
            }

            int strength = CalculatePasswordStrength(password);

            if (strength <= 2)
                SetPasswordStrengthDisplay("弱", Brushes.Red, 100);
            else if (strength <= 4)
                SetPasswordStrengthDisplay("中", Brushes.Orange, 200);
            else
                SetPasswordStrengthDisplay("强", Brushes.Green, 300);
        }

        private int CalculatePasswordStrength(string password)
        {
            int strength = 0;
            if (password.Length >= 8) strength++;
            if (password.Length >= 12) strength++;
            if (Regex.IsMatch(password, @"\d")) strength++;
            if (Regex.IsMatch(password, @"[a-z]")) strength++;
            if (Regex.IsMatch(password, @"[A-Z]")) strength++;
            if (Regex.IsMatch(password, @"[^A-Za-z0-9]")) strength++;
            return strength;
        }

        private void SetPasswordStrengthDisplay(string level, Brush color, double width)
        {
            passwordStrengthText.Text = $"密码强度：{level}";
            passwordStrengthText.Foreground = color;
            passwordStrengthBar.Background = color;
            passwordStrengthBar.Width = width;
        }

        private bool ValidateInput()
        {
            if (!int.TryParse(uidTextBox.Text, out int uid) || uidTextBox.Text.Length != 9)
            {
                ShowToast("UID必须是9位数字", false);
                return false;
            }

            if (!int.TryParse(levelTextBox.Text, out int level) || level < 1 || level > 60)
            {
                ShowToast("冒险等级必须是1-60之间的数字", false);
                return false;
            }

            if (string.IsNullOrWhiteSpace(nicknameTextBox.Text))
            {
                ShowToast("请输入昵称", false);
                return false;
            }

            if (!IsValidUsername(usernameTextBox.Text))
            {
                ShowToast("账号必须是11位手机号或有效的邮箱", false);
                return false;
            }

            string currentPassword = _isPasswordVisible ? passwordTextBox.Text : passwordBox.Password;
            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                ShowToast("请输入密码", false);
                return false;
            }

            return true;
        }

        private void ShowToast(string message, bool isSuccess = true)
        {
            ShowToastRequested?.Invoke(isSuccess ? "输入验证" : "输入错误", message, isSuccess);
        }

        private bool IsValidUsername(string username)
        {
            var phoneRegex = new Regex(@"^1[3-9]\d{9}$");
            var emailRegex = new Regex(@"^[^\s@]+@(qq\.com|163\.com|126\.com|yeah\.net|gmail\.com|outlook\.com|hotmail\.com|live\.com|msn\.com|yahoo\.com|foxmail\.com|sina\.com|sina\.cn|sohu\.com|tom\.com|139\.com|189\.cn|wo\.cn|icloud\.com|me\.com|mac\.com)$", RegexOptions.IgnoreCase);
            return phoneRegex.IsMatch(username) || emailRegex.IsMatch(username);
        }

        private void CloseWithAnimation(bool dialogResult = false)
        {
            if (_isClosing) return;
            _isClosing = true;

            PlayAnimation("FadeOutAnimation", () =>
            {
                DialogResult = dialogResult;
                Close();
            });
        }

        // 事件处理方法
        private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseWithAnimation(false);
        private void CancelButton_Click(object sender, RoutedEventArgs e) => CloseWithAnimation(false);

        private void NumberOnlyTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !char.IsDigit(e.Text, 0);
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdatePasswordStrength(passwordBox.Password);
            if (_isPasswordVisible) passwordTextBox.Text = passwordBox.Password;
            UpdateWatermarks();
        }

        private void PasswordTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isPasswordVisible) passwordBox.Password = passwordTextBox.Text;
            UpdatePasswordStrength(passwordTextBox.Text);
            UpdateWatermarks();
        }

        private void TogglePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            SetPasswordVisibility(!_isPasswordVisible);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput()) return;

            string inputUid = uidTextBox.Text.Trim();
            var allAccounts = AccountDataService.LoadAccounts();
            bool uidExists;

            if (IsEditMode && Account != null)
                uidExists = allAccounts.Any(a => a.Id != Account.Id && a.Uid == inputUid);
            else
                uidExists = allAccounts.Any(a => a.Uid == inputUid);

            if (uidExists)
            {
                ShowToast($"UID「{inputUid}」已存在，无法保存", false);
                return;
            }

            Account.Uid = uidTextBox.Text;
            Account.Level = int.Parse(levelTextBox.Text);
            Account.Nickname = nicknameTextBox.Text;
            Account.Username = usernameTextBox.Text;
            Account.Password = _isPasswordVisible ? passwordTextBox.Text : passwordBox.Password;

            // 如果是新建账号，设置创建时间
            if (!IsEditMode)
            {
                Account.CreatedTime = DateTime.Now;
            }

            CloseWithAnimation(true);
        }

        // 统一的TextChanged处理方法
        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateWatermarks();
        }

        private void UIDTextBox_TextChanged(object sender, TextChangedEventArgs e) => TextBox_TextChanged(sender, e);
        private void LevelTextBox_TextChanged(object sender, TextChangedEventArgs e) => TextBox_TextChanged(sender, e);
        private void NicknameTextBox_TextChanged(object sender, TextChangedEventArgs e) => TextBox_TextChanged(sender, e);
        private void UsernameTextBox_TextChanged(object sender, TextChangedEventArgs e) => TextBox_TextChanged(sender, e);
    }
}