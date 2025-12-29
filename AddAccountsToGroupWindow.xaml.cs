using Lvchaxs_ZH.Models;
using Lvchaxs_ZH.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Lvchaxs_ZH
{
    public partial class AddAccountsToGroupWindow : Window
    {
        private bool _isClosing = false;
        private readonly int _groupId;
        private AccountGroup _currentGroup;
        private readonly ObservableCollection<SelectableAccount> _displayAccounts = new();
        private string _currentSortColumn = "";
        private bool _isSortAscending = true;

        public List<int> SelectedAccountIds { get; private set; }
        public delegate void ShowToastDelegate(string title, string message, bool isSuccess = true);
        public event ShowToastDelegate ShowToastRequested;

        public AddAccountsToGroupWindow(int groupId)
        {
            InitializeComponent();
            _groupId = groupId;
            SelectedAccountIds = new List<int>();
            InitializeData();
            Loaded += Window_Loaded;
        }

        private void InitializeData()
        {
            LoadAccounts();
            UpdateSelectionInfo();
        }

        private void LoadAccounts()
        {
            _displayAccounts.Clear();

            var allAccounts = AccountDataService.LoadAccounts();
            var groups = GroupDataService.LoadGroups();
            _currentGroup = groups.FirstOrDefault(g => g.Id == _groupId);
            var existingAccountIds = _currentGroup?.AccountIds ?? new List<int>();

            foreach (var account in allAccounts.Where(a => !existingAccountIds.Contains(a.Id)))
            {
                var selectableAccount = new SelectableAccount
                {
                    Id = account.Id,
                    Uid = account.Uid,
                    Level = account.Level,
                    Nickname = account.Nickname,
                    IsSelected = false,
                    CreatedTime = account.CreatedTime
                };

                selectableAccount.PropertyChanged += SelectableAccount_PropertyChanged;
                _displayAccounts.Add(selectableAccount);
            }

            accountsItemsControl.ItemsSource = _displayAccounts;
            noAccountsMessage.Visibility = _displayAccounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateSelectionInfo()
        {
            UpdateSelectionCount();
            UpdateGroupStats();
        }

        private void UpdateSelectionCount()
        {
            int selectedCount = _displayAccounts.Count(a => a.IsSelected);
            selectionCountText.Text = $"已选择 {selectedCount} 个账号";
        }

        private void UpdateGroupStats()
        {
            if (_currentGroup != null)
            {
                int currentInGroup = _currentGroup.AccountIds.Count;
                int totalAccounts = AccountDataService.LoadAccounts().Count;
                int remainingAccounts = totalAccounts - currentInGroup;
                groupStatsText.Text = $"当前分组已有 {currentInGroup} 个账号 • 剩余 {remainingAccounts} 个账号可移入";
            }
        }

        private void ColumnHeader_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left &&
                sender is Border border &&
                border.Tag is string columnName)
            {
                if (_currentSortColumn == columnName)
                    _isSortAscending = !_isSortAscending;
                else
                {
                    _currentSortColumn = columnName;
                    _isSortAscending = true;
                }

                ApplySort();
                UpdateSortArrows();
                e.Handled = true;
            }
        }

        private void ApplySort()
        {
            IEnumerable<SelectableAccount> sortedAccounts = _displayAccounts;

            switch (_currentSortColumn)
            {
                case "Uid":
                    sortedAccounts = _isSortAscending
                        ? sortedAccounts.OrderBy(a => a.Uid)
                        : sortedAccounts.OrderByDescending(a => a.Uid);
                    break;
                case "Level":
                    sortedAccounts = _isSortAscending
                        ? sortedAccounts.OrderBy(a => a.Level)
                        : sortedAccounts.OrderByDescending(a => a.Level);
                    break;
                case "Nickname":
                    sortedAccounts = _isSortAscending
                        ? sortedAccounts.OrderBy(a => a.Nickname)
                        : sortedAccounts.OrderByDescending(a => a.Nickname);
                    break;
            }

            var sortedList = sortedAccounts.ToList();
            _displayAccounts.Clear();
            foreach (var account in sortedList)
                _displayAccounts.Add(account);
        }

        private void UpdateSortArrows()
        {
            ResetAllArrows();

            var arrowTuples = new[]
            {
                ("Uid", uidAscArrow, uidDescArrow),
                ("Level", levelAscArrow, levelDescArrow),
                ("Nickname", nicknameAscArrow, nicknameDescArrow)
            };

            foreach (var (column, ascArrow, descArrow) in arrowTuples)
            {
                if (_currentSortColumn == column)
                {
                    var arrow = _isSortAscending ? ascArrow : descArrow;
                    arrow.Style = (Style)FindResource("ActiveSortArrowStyle");
                    break;
                }
            }
        }

        private void ResetAllArrows()
        {
            var arrows = new[] { uidAscArrow, uidDescArrow, levelAscArrow, levelDescArrow, nicknameAscArrow, nicknameDescArrow };
            foreach (var arrow in arrows)
                arrow.Style = (Style)FindResource("SortArrowStyle");
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PlayAnimation("FadeInAnimation", () =>
            {
                mainBorder.Opacity = 1;
                var transform = mainBorder.RenderTransform as ScaleTransform;
                transform.ScaleX = transform.ScaleY = 1;
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

        private bool ValidateSelection()
        {
            SelectedAccountIds = _displayAccounts
                .Where(a => a.IsSelected)
                .Select(a => a.Id)
                .ToList();

            if (SelectedAccountIds.Count == 0)
            {
                ShowToast("请选择要移入分组的账号", false);
                return false;
            }

            return true;
        }

        private void ShowToast(string message, bool isSuccess = true)
        {
            ShowToastRequested?.Invoke(isSuccess ? "操作成功" : "操作失败", message, isSuccess);
        }

        private bool UpdateGroupAccounts(List<int> accountIds)
        {
            try
            {
                var groups = GroupDataService.LoadGroups();
                var currentGroup = groups.FirstOrDefault(g => g.Id == _groupId);

                if (currentGroup == null)
                    return false;

                var now = DateTime.Now;
                foreach (var accountId in accountIds)
                {
                    if (!currentGroup.AccountIds.Contains(accountId))
                    {
                        currentGroup.AccountIds.Add(accountId);
                        currentGroup.AccountAddTimes ??= new Dictionary<int, DateTime>();
                        currentGroup.AccountAddTimes[accountId] = now;
                    }
                }

                return GroupDataService.SaveGroup(currentGroup);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新分组账号失败: {ex.Message}");
                return false;
            }
        }

        private void SelectableAccount_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectableAccount.IsSelected))
                UpdateSelectionCount();
        }

        private void AccountItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left &&
                e.ClickCount == 1 &&
                sender is FrameworkElement element &&
                element.DataContext is SelectableAccount account)
            {
                account.IsSelected = !account.IsSelected;
                e.Handled = true;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseWithAnimation(false);
        private void CancelButton_Click(object sender, RoutedEventArgs e) => CloseWithAnimation(false);

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateSelection()) return;

            bool success = UpdateGroupAccounts(SelectedAccountIds);

            if (success)
                CloseWithAnimation(true);
            else
                ShowToast("移入账号失败", false);
        }
    }

    public class SelectableAccount : INotifyPropertyChanged
    {
        private int _id;
        private string _uid = "";
        private int _level;
        private string _nickname = "";
        private bool _isSelected;
        private DateTime _createdTime;

        public int Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Uid
        {
            get => _uid;
            set { _uid = value; OnPropertyChanged(); }
        }

        public int Level
        {
            get => _level;
            set { _level = value; OnPropertyChanged(); }
        }

        public string Nickname
        {
            get => _nickname;
            set { _nickname = value; OnPropertyChanged(); }
        }

        public DateTime CreatedTime
        {
            get => _createdTime;
            set { _createdTime = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public int WorldLevel
        {
            get
            {
                int[] thresholds = { 20, 25, 30, 35, 40, 45, 50, 55, 58 };
                for (int i = thresholds.Length - 1; i >= 0; i--)
                {
                    if (_level >= thresholds[i])
                        return i + 1;
                }
                return 0;
            }
        }

        public string DisplayCreatedTime => _createdTime.ToString("yyyy-MM-dd HH:mm");

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}