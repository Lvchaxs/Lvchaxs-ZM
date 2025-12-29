using Lvchaxs_ZH.GenshinImpact;
using Lvchaxs_ZH.Models;
using Lvchaxs_ZH.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Lvchaxs_ZH
{
    public static class NativeClipboardHelper
    {
        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll")]
        private static extern bool EmptyClipboard();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr lstrcpy(IntPtr dest, string src);

        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint GMEM_ZEROINIT = 0x0040;

        public static bool SafeSetText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            bool result = false;
            IntPtr hGlobal = IntPtr.Zero;

            try
            {
                for (int i = 0; i < 3; i++)
                {
                    if (OpenClipboard(IntPtr.Zero))
                    {
                        try
                        {
                            EmptyClipboard();
                            uint sizeInBytes = (uint)((text.Length + 1) * 2);
                            hGlobal = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (UIntPtr)sizeInBytes);

                            if (hGlobal == IntPtr.Zero)
                                return false;

                            IntPtr lockedPtr = GlobalLock(hGlobal);
                            if (lockedPtr == IntPtr.Zero)
                                return false;

                            lstrcpy(lockedPtr, text);
                            GlobalUnlock(lockedPtr);

                            if (SetClipboardData(CF_UNICODETEXT, hGlobal) != IntPtr.Zero)
                            {
                                result = true;
                                hGlobal = IntPtr.Zero;
                            }
                        }
                        finally
                        {
                            CloseClipboard();
                        }
                        break;
                    }
                    Thread.Sleep(10);
                }
            }
            catch
            {
                result = false;
            }
            finally
            {
                if (hGlobal != IntPtr.Zero)
                {
                    GlobalFree(hGlobal);
                }
            }

            return result;
        }
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region 字段和属性
        private ObservableCollection<Account> _accounts = new();
        private List<Account> _allAccounts = new();
        private List<AccountGroup> _allGroups = new();
        private List<int> _selectedAccountIds = new();
        private DispatcherTimer _toastTimer;
        private string _currentSortColumn = "";
        private ListSortDirection _currentSortDirection = ListSortDirection.Ascending;
        private int _selectedGroupId = 0;
        private bool _isAddToGroupMode = false;
        private int _currentGroupIdForAdding = 0;
        private List<int> _pinnedAccountOrder = new();
        private List<int> _pinnedGroupOrder = new();

        // 登录相关字段
        private bool _isLoginInProgress = false;
        private DispatcherTimer _statusCheckTimer;
        private CancellationTokenSource _loginCancellationTokenSource;
        private Task _loginTask;
        private string _gamePath = "";
        private const string GAME_PATH_FILE = "game_path.json";

        private enum DisplayMode { AccountList, NoData, GroupManagement }
        private DisplayMode _currentDisplayMode = DisplayMode.AccountList;
        private Window _currentModalWindow;
        private bool _isModalWindowOpen = false;

        public static readonly DependencyProperty SelectedGroupIdProperty =
            DependencyProperty.Register("SelectedGroupId", typeof(int), typeof(MainWindow),
                new PropertyMetadata(0, OnSelectedGroupIdChanged));

        public int SelectedGroupId
        {
            get => (int)GetValue(SelectedGroupIdProperty);
            set => SetValue(SelectedGroupIdProperty, value);
        }

        public bool IsInGroupMode => _selectedGroupId > 0;

        public event PropertyChangedEventHandler PropertyChanged;
        #endregion

        #region 构造函数和初始化
        public MainWindow()
        {
            InitializeComponent();
            InitializeData();
            this.Loaded += MainWindow_Loaded;

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _toastTimer.Tick += (s, e) => HideToast();

            _statusCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusCheckTimer.Tick += StatusCheckTimer_Tick;
        }

        private void InitializeData()
        {
            LoadGamePath();
            LoadPinnedOrders();
            LoadAllData();
        }

        private void LoadAllData()
        {
            _allAccounts = AccountDataService.LoadAccounts();
            _allGroups = GroupDataService.LoadGroups();
            UpdateAccountsList();
            UpdateWatermarkVisibility();
            UpdateGroupInfoDisplay();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AdjustWindowSizeAndPosition();
            ReloadData();
        }

        private void ReloadData()
        {
            LoadAllData();
            
            if (_allAccounts.Count == 0)
            {
                SwitchToDisplayMode(DisplayMode.NoData);
            }
            else
            {
                SwitchToDisplayMode(DisplayMode.AccountList);
                Dispatcher.BeginInvoke(() =>
                {
                    UpdateMarkColumnVisibility();
                    UpdatePinColumnVisibility();
                }, DispatcherPriority.Render);
            }
        }
        #endregion

        #region 通用辅助方法
        private void AdjustWindowSizeAndPosition()
        {
            double screenWidth = SystemParameters.WorkArea.Width;
            double screenHeight = SystemParameters.WorkArea.Height;

            double widthRatio = 1500.0 / 3840.0;
            double heightRatio = 900.0 / 2160.0;

            this.Width = Math.Clamp(screenWidth * widthRatio, 1000, 1920);
            this.Height = Math.Clamp(screenHeight * heightRatio, 610, 1080);
            this.Left = (screenWidth - this.Width) / 2;
            this.Top = (screenHeight - this.Height) / 2;
        }

        private void UpdateWatermarkVisibility()
        {
            watermarkText.Visibility = string.IsNullOrWhiteSpace(searchTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private T FindVisualChild<T>(DependencyObject parent, string childName = null) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && (childName == null || (element is FrameworkElement fe && fe.Name == childName)))
                    return element;

                var result = FindVisualChild<T>(child, childName);
                if (result != null) return result;
            }
            return null;
        }

        private T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null && !(child is T))
                child = VisualTreeHelper.GetParent(child);
            return child as T;
        }
        #endregion

        #region 账号列表管理
        private void UpdateAccountsList(string searchTerm = "")
        {
            _accounts.Clear();
            List<Account> filteredAccounts = GetFilteredAccounts(searchTerm);

            if (!string.IsNullOrEmpty(_currentSortColumn))
                filteredAccounts = SortAccounts(filteredAccounts, _currentSortColumn, _currentSortDirection);

            foreach (var account in filteredAccounts)
                _accounts.Add(account);

            accountsDataGrid.ItemsSource = _accounts;
            UpdateUI();
        }

        private List<Account> GetFilteredAccounts(string searchTerm)
        {
            if (_selectedGroupId > 0)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                return group != null ? ProcessGroupAccounts(group, searchTerm) : new List<Account>();
            }
            else
            {
                return ProcessMainAccounts(searchTerm);
            }
        }

        private List<Account> ProcessGroupAccounts(AccountGroup group, string searchTerm)
        {
            var filteredAccounts = _allAccounts
                .Where(a => group.AccountIds.Contains(a.Id))
                .ToList();

            foreach (var account in filteredAccounts)
                account.IsMarked = group.IsAccountMarked(account.Id);

            if (!string.IsNullOrWhiteSpace(searchTerm))
                filteredAccounts = FilterAccounts(filteredAccounts, searchTerm);

            return SortGroupAccounts(filteredAccounts, group);
        }

        private List<Account> SortGroupAccounts(List<Account> accounts, AccountGroup group)
        {
            var markedAccounts = accounts.Where(a => a.IsMarked).ToList();
            var unmarkedAccounts = accounts.Where(a => !a.IsMarked).ToList();

            var sortedUnmarked = unmarkedAccounts
                .OrderBy(a => group.AccountAddTimes?.ContainsKey(a.Id) == true ? 
                    group.AccountAddTimes[a.Id] : a.CreatedTime)
                .ToList();

            var sortedMarked = markedAccounts
                .OrderBy(a => group.MarkedTimes?.ContainsKey(a.Id) == true ? 
                    group.MarkedTimes[a.Id] : a.CreatedTime)
                .ToList();

            var result = new List<Account>();
            result.AddRange(sortedUnmarked);
            result.AddRange(sortedMarked);
            return result;
        }

        private List<Account> ProcessMainAccounts(string searchTerm)
        {
            var filteredAccounts = string.IsNullOrWhiteSpace(searchTerm) 
                ? _allAccounts 
                : FilterAccounts(_allAccounts, searchTerm);

            foreach (var account in filteredAccounts)
                account.IsMarked = false;

            return SortMainAccounts(filteredAccounts);
        }

        private List<Account> SortMainAccounts(List<Account> accounts)
        {
            var pinnedAccounts = accounts.Where(a => a.IsPinned)
                .OrderBy(a => a.PinnedTime)
                .ToList();
            
            var unpinnedAccounts = accounts.Where(a => !a.IsPinned)
                .OrderBy(a => a.CreatedTime)
                .ToList();

            var result = new List<Account>();
            result.AddRange(pinnedAccounts);
            result.AddRange(unpinnedAccounts);
            return result;
        }

        private List<Account> FilterAccounts(List<Account> accounts, string searchTerm)
        {
            return accounts.Where(a =>
                a.Uid.StartsWith(searchTerm) ||
                (a.Nickname != null && a.Nickname.StartsWith(searchTerm, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }
        #endregion

        #region UI状态管理
        private void UpdateUI()
        {
            UpdateUIState();
            UpdateGroupInfoDisplay();
            UpdateWatermarkVisibility();
            UpdateColumnVisibility();
        }

        private void UpdateColumnVisibility()
        {
            UpdateMarkColumnVisibility();
            UpdatePinColumnVisibility();
        }

        private void UpdateMarkColumnVisibility()
        {
            if (accountsDataGrid.Columns.Count > 0 && accountsDataGrid.Columns[0].Header?.ToString()?.Contains("标记") == true)
            {
                bool shouldBeVisible = (_selectedGroupId > 0 && _currentDisplayMode == DisplayMode.AccountList);
                accountsDataGrid.Columns[0].Visibility = shouldBeVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdatePinColumnVisibility()
        {
            int pinColumnIndex = 1;
            if (accountsDataGrid.Columns.Count > pinColumnIndex &&
                accountsDataGrid.Columns[pinColumnIndex].Header?.ToString()?.Contains("置顶") == true)
            {
                bool shouldBeVisible = (_selectedGroupId == 0 && _currentDisplayMode == DisplayMode.AccountList);
                accountsDataGrid.Columns[pinColumnIndex].Visibility = shouldBeVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateUIState()
        {
            if (_currentDisplayMode == DisplayMode.AccountList && _accounts.Count == 0)
            {
                bool hasSearchText = !string.IsNullOrWhiteSpace(searchTextBox.Text);
                bool hasAccountsInTotal = _selectedGroupId > 0
                    ? _allGroups.Any(g => g.Id == _selectedGroupId && g.AccountIds.Count > 0)
                    : _allAccounts.Count > 0;

                if ((hasSearchText && hasAccountsInTotal) || (!hasSearchText && !hasAccountsInTotal))
                    SwitchToDisplayMode(DisplayMode.NoData);
            }
            else if (_currentDisplayMode == DisplayMode.NoData && _accounts.Count > 0)
            {
                SwitchToDisplayMode(DisplayMode.AccountList);
            }
        }

        private void SwitchToDisplayMode(DisplayMode mode)
        {
            _currentDisplayMode = mode;

            noDataMessage.Visibility = Visibility.Collapsed;
            accountsDataGrid.Visibility = Visibility.Collapsed;
            groupManagementMessage.Visibility = Visibility.Collapsed;

            UpdateNoDataMessage();
            UpdateNoDataBackButton();
            UpdateButtonVisibility();

            switch (mode)
            {
                case DisplayMode.AccountList:
                    noDataMessage.Visibility = _accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    accountsDataGrid.Visibility = _accounts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case DisplayMode.NoData:
                    noDataMessage.Visibility = Visibility.Visible;
                    break;
                case DisplayMode.GroupManagement:
                    groupManagementMessage.Visibility = Visibility.Visible;
                    SortGroups();
                    UpdateGroupsList();
                    HideAddToGroupButton();
                    break;
            }

            UpdateGroupInfoDisplay();
            UpdateColumnVisibility();
        }
        #endregion

        #region 分组信息显示
        private void UpdateGroupInfoDisplay()
        {
            currentGroupLabel.Visibility = Visibility.Visible;

            if (_currentDisplayMode == DisplayMode.GroupManagement)
                UpdateGroupManagementInfo();
            else if (_selectedGroupId > 0)
                UpdateSelectedGroupInfo();
            else
                UpdateMainAccountListInfo();
        }

        private void UpdateGroupManagementInfo()
        {
            leftText.Text = $"分组列表：共计{_allGroups.Count}个分组";
            groupSeparator.Visibility = Visibility.Collapsed;
            rightText.Text = "";
        }

        private void UpdateSelectedGroupInfo()
        {
            var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
            if (group != null)
                UpdateGroupInfoForSelectedGroup(group);
        }

        private void UpdateMainAccountListInfo()
        {
            leftText.Text = $"账号列表：共计{_allAccounts.Count}个账号";
            groupSeparator.Visibility = Visibility.Visible;
            groupSeparator.Text = "•";
            rightText.Text = $"{_allGroups.Count}个分组";
        }

        private void UpdateGroupInfoForSelectedGroup(AccountGroup group)
        {
            var groupAccounts = _allAccounts.Where(a => group.AccountIds.Contains(a.Id)).ToList();
            int totalAccountCount = groupAccounts.Count;
            int markedCount = group.MarkedTimes?.Count ?? 0;
            int unmarkedCount = totalAccountCount - markedCount;

            leftText.Text = $"分组：{group.Name}";
            groupSeparator.Visibility = Visibility.Visible;
            groupSeparator.Text = "•";

            string displayText = $"{totalAccountCount}个账号";
            if (markedCount > 0) displayText += $" • 已标记{markedCount}";
            if (unmarkedCount > 0) displayText += $" • 未标记{unmarkedCount}";

            rightText.Text = displayText;
        }
        #endregion

        #region 分组管理
        private void SortGroups()
        {
            if (_allGroups != null && _allGroups.Count > 0)
            {
                _allGroups = _allGroups
                    .OrderByDescending(g => g.IsPinned)
                    .ThenBy(g => g.IsPinned ? g.PinnedTime : g.CreatedTime)
                    .ToList();
                UpdateGroupsList();
            }
        }

        private void UpdateGroupsList()
        {
            var groupsItemsControl = FindVisualChild<ItemsControl>(groupManagementMessage, "groupsItemsControl");
            if (groupsItemsControl == null) return;

            if (_allGroups.Count > 0)
            {
                var groupsWithDisplayText = _allGroups.Select(CreateGroupDisplayObject).ToList();
                groupsItemsControl.ItemsSource = groupsWithDisplayText;
                UpdateGroupsListUI(groupsItemsControl);
                ShowGroupsListContainer(true);
            }
            else
            {
                groupsItemsControl.ItemsSource = null;
                ShowGroupsListContainer(false);
            }
        }

        private object CreateGroupDisplayObject(AccountGroup group)
        {
            var groupAccounts = _allAccounts.Where(a => group.AccountIds.Contains(a.Id)).ToList();
            int totalAccountCount = groupAccounts.Count;
            int markedCount = group.MarkedTimes?.Count ?? 0;

            string displayText = $"包含 {totalAccountCount} 个账号";
            if (markedCount > 0)
                displayText += $" • 已标记 {markedCount} • 未标记 {totalAccountCount - markedCount}";

            string displayTagNote = string.IsNullOrWhiteSpace(group.TagNote) ? string.Empty : $"• {group.TagNote}";

            return new
            {
                Id = group.Id,
                Name = group.Name,
                TagNote = displayTagNote,
                AccountIds = group.AccountIds,
                AccountCount = totalAccountCount,
                DisplayText = displayText,
                HasAccounts = totalAccountCount > 0,
                HasMarks = markedCount > 0,
                MarkedCount = markedCount,
                IsPinned = group.IsPinned,
                CreatedTime = group.CreatedTime,
                PinnedTime = group.PinnedTime
            };
        }

        private void UpdateGroupsListUI(ItemsControl groupsItemsControl)
        {
            groupsItemsControl.InvalidateVisual();
            groupsItemsControl.UpdateLayout();

            Dispatcher.BeginInvoke(() =>
            {
                foreach (var item in groupsItemsControl.Items)
                {
                    var container = groupsItemsControl.ItemContainerGenerator.ContainerFromItem(item);
                    if (container != null)
                    {
                        var clearMarksButton = FindVisualChild<Button>(container, "clearMarksButton");
                        if (clearMarksButton != null)
                        {
                            var propertyInfo = item.GetType().GetProperty("HasMarks");
                            if (propertyInfo != null)
                            {
                                bool hasMarks = (bool)propertyInfo.GetValue(item);
                                clearMarksButton.Visibility = hasMarks ? Visibility.Visible : Visibility.Collapsed;
                            }
                        }
                    }
                }
            }, DispatcherPriority.Render);
        }

        private void ShowGroupsListContainer(bool show)
        {
            var noGroupMessage = FindVisualChild<Border>(groupManagementMessage, "noGroupMessage");
            if (noGroupMessage != null)
                noGroupMessage.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        }
        #endregion

        #region 标记管理
        private void ToggleAccountMarkInGroup(int accountId, int groupId)
        {
            var group = _allGroups.FirstOrDefault(g => g.Id == groupId);
            if (group != null)
            {
                if (group.IsAccountMarked(accountId))
                    group.UnmarkAccount(accountId);
                else
                    group.MarkAccount(accountId);

                GroupDataService.SaveGroup(group);

                var account = _allAccounts.FirstOrDefault(a => a.Id == accountId);
                if (account != null)
                    account.IsMarked = group.IsAccountMarked(accountId);
            }
        }

        private void ClearAllMarksInGroup(int groupId)
        {
            var group = _allGroups.FirstOrDefault(g => g.Id == groupId);
            if (group != null && group.MarkedTimes?.Count > 0)
            {
                group.MarkedTimes.Clear();
                GroupDataService.SaveGroup(group);

                foreach (var account in _allAccounts.Where(a => group.AccountIds.Contains(a.Id)))
                    account.IsMarked = false;
            }
        }
        #endregion

        #region 排序功能
        private List<Account> SortAccounts(List<Account> accounts, string sortBy, ListSortDirection direction)
        {
            return _selectedGroupId == 0 
                ? SortMainAccounts(accounts, sortBy, direction) 
                : SortGroupAccounts(accounts, sortBy, direction);
        }

        private List<Account> SortMainAccounts(List<Account> accounts, string sortBy, ListSortDirection direction)
        {
            var pinnedAccounts = accounts.Where(a => a.IsPinned).ToList();
            var unpinnedAccounts = accounts.Where(a => !a.IsPinned).ToList();

            if (sortBy == "IsPinned")
                return direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.IsPinned).ToList()
                    : accounts.OrderByDescending(a => a.IsPinned).ToList();

            var sortedPinned = SortAccountList(pinnedAccounts, sortBy, direction);
            var sortedUnpinned = SortAccountList(unpinnedAccounts, sortBy, direction);

            var result = new List<Account>();
            result.AddRange(sortedPinned);
            result.AddRange(sortedUnpinned);
            return result;
        }

        private List<Account> SortGroupAccounts(List<Account> accounts, string sortBy, ListSortDirection direction)
        {
            var markedAccounts = accounts.Where(a => a.IsMarked).ToList();
            var unmarkedAccounts = accounts.Where(a => !a.IsMarked).ToList();

            if (sortBy == "IsMarked")
                return direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.IsMarked).ToList()
                    : accounts.OrderByDescending(a => a.IsMarked).ToList();

            var sortedMarked = SortAccountList(markedAccounts, sortBy, direction);
            var sortedUnmarked = SortAccountList(unmarkedAccounts, sortBy, direction);

            var result = new List<Account>();
            result.AddRange(sortedUnmarked);
            result.AddRange(sortedMarked);
            return result;
        }

        private List<Account> SortAccountList(List<Account> accounts, string sortBy, ListSortDirection direction)
        {
            return sortBy switch
            {
                "Uid" => direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.Uid).ToList()
                    : accounts.OrderByDescending(a => a.Uid).ToList(),
                "Level" => direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.Level).ThenBy(a => a.WorldLevel).ToList()
                    : accounts.OrderByDescending(a => a.Level).ThenByDescending(a => a.WorldLevel).ToList(),
                "Nickname" => direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.Nickname).ToList()
                    : accounts.OrderByDescending(a => a.Nickname).ToList(),
                "Username" => direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.Username).ToList()
                    : accounts.OrderByDescending(a => a.Username).ToList(),
                "IsMarked" => direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.IsMarked).ToList()
                    : accounts.OrderByDescending(a => a.IsMarked).ToList(),
                "IsPinned" => direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.IsPinned).ToList()
                    : accounts.OrderByDescending(a => a.IsPinned).ToList(),
                "Id" => direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.Id).ToList()
                    : accounts.OrderByDescending(a => a.Id).ToList(),
                _ => accounts
            };
        }

        private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            var column = e.Column;
            var sortMemberPath = column.SortMemberPath;

            if (string.IsNullOrEmpty(sortMemberPath))
                return;

            if (_currentSortColumn == sortMemberPath)
            {
                if (_currentSortDirection == ListSortDirection.Ascending)
                {
                    _currentSortDirection = ListSortDirection.Descending;
                    UpdateSortArrows(sortMemberPath, false);
                    column.SortDirection = ListSortDirection.Descending;
                }
                else if (_currentSortDirection == ListSortDirection.Descending)
                {
                    _currentSortColumn = "";
                    _currentSortDirection = ListSortDirection.Ascending;
                    ResetAllSortArrowColors();
                    ClearDataGridSortIndicators();
                }
            }
            else
            {
                _currentSortColumn = sortMemberPath;
                _currentSortDirection = ListSortDirection.Ascending;
                UpdateSortArrows(sortMemberPath, true);
                column.SortDirection = ListSortDirection.Ascending;
            }
            UpdateAccountsList(searchTextBox.Text);
        }

        private void UpdateSortArrows(string currentColumn, bool isAscending)
        {
            ResetAllSortArrowColors();

            string arrowName = isAscending
                ? $"{currentColumn.ToLower()}AscArrow"
                : $"{currentColumn.ToLower()}DescArrow";

            var arrow = FindVisualChild<Path>(accountsDataGrid, arrowName);
            if (arrow != null)
                arrow.Fill = new SolidColorBrush(Color.FromArgb(0xFF, 0x1A, 0x56, 0xDB));
        }

        private void ResetAllSortArrowColors()
        {
            var arrowNames = new[] {
                "uidAscArrow", "uidDescArrow",
                "levelAscArrow", "levelDescArrow",
                "nicknameAscArrow", "nicknameDescArrow",
                "usernameAscArrow", "usernameDescArrow"
            };

            foreach (var arrowName in arrowNames)
            {
                var arrow = FindVisualChild<Path>(accountsDataGrid, arrowName);
                if (arrow != null)
                    arrow.Fill = new SolidColorBrush(Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF));
            }
        }

        private void ClearDataGridSortIndicators()
        {
            foreach (var column in accountsDataGrid.Columns)
                column.SortDirection = null;
        }
        #endregion

        #region 按钮管理
        private void UpdateNoDataMessage()
        {
            var noDataIcon = FindVisualChild<TextBlock>(noDataMessage, "noDataIcon");
            var noDataText = FindVisualChild<TextBlock>(noDataMessage, "noDataText");
            var noDataSubText = FindVisualChild<TextBlock>(noDataMessage, "noDataSubText");

            if (noDataIcon == null || noDataText == null || noDataSubText == null) return;

            bool hasSearchText = !string.IsNullOrWhiteSpace(searchTextBox.Text);
            bool hasAccountsInCurrentView = _selectedGroupId > 0
                ? _allGroups.Any(g => g.Id == _selectedGroupId && g.AccountIds.Count > 0)
                : _allAccounts.Count > 0;

            noDataIcon.Text = "📋";
            noDataText.Text = "当前没有任何账号";
            noDataSubText.Text = "点击右上角'添加账号'按钮添加第一个账号";

            if (_selectedGroupId > 0 && !hasSearchText && !hasAccountsInCurrentView)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                if (group != null)
                {
                    noDataIcon.Text = "📂";
                    noDataText.Text = $"「{group.Name}」分组中没有账号";
                    noDataSubText.Text = $"伴生Lvchaxs\n点击右上角 移入账号 按钮选择账号移入「{group.Name}」分组中";
                }
            }
            else if (hasSearchText && _allAccounts.Count > 0)
            {
                noDataIcon.Text = "🔍";
                noDataText.Text = "未找到相关账号";
                noDataSubText.Text = $"伴生Lvchaxs\n未找到与「{searchTextBox.Text}」相关的账号";
            }
        }

        private void UpdateNoDataBackButton()
        {
            if (noDataBackButton != null)
            {
                bool hasSearchText = !string.IsNullOrWhiteSpace(searchTextBox.Text);
                bool hasAccountsInTotal = _allAccounts.Count > 0;

                noDataBackButton.Visibility = (hasSearchText && hasAccountsInTotal)
                    ? Visibility.Visible : Visibility.Collapsed;

                noDataBackButton.Content = _selectedGroupId > 0 ? "返回当前分组" : "返回账号列表";
            }
        }

        private void UpdateButtonVisibility()
        {
            bool isMainAccountList = (_currentDisplayMode == DisplayMode.AccountList && _selectedGroupId == 0);
            bool isGroupManagement = (_currentDisplayMode == DisplayMode.GroupManagement);
            bool isInGroup = (_selectedGroupId > 0 && (_currentDisplayMode == DisplayMode.AccountList ||
                             (_currentDisplayMode == DisplayMode.NoData && string.IsNullOrWhiteSpace(searchTextBox.Text))));
            bool isSearchNoData = (_currentDisplayMode == DisplayMode.NoData && !string.IsNullOrWhiteSpace(searchTextBox.Text));

            if (isMainAccountList)
                ShowMainAccountListButtons();
            else if (isGroupManagement)
                ShowGroupManagementButtons();
            else if (isInGroup || _isAddToGroupMode)
                ShowInGroupButtons();
            else if (isSearchNoData)
                HideAllButtons();
            else if (_currentDisplayMode == DisplayMode.NoData && _allAccounts.Count == 0)
                ShowMainAccountListButtons();
            else
                ShowDefaultButtons();
        }

        private void ShowMainAccountListButtons()
        {
            addAccountButton.Visibility = Visibility.Visible;
            backToAccountListButton.Visibility = Visibility.Collapsed;
            addGroupButton.Visibility = Visibility.Collapsed;
            groupManagementButton.Visibility = Visibility.Visible;
            
            groupManagementButton.Click -= GroupManagementButton_Click;
            groupManagementButton.Click -= ReturnToGroupList_Click;
            groupManagementButton.Click += GroupManagementButton_Click;
            
            if (_isAddToGroupMode) HideAddToGroupButton();
        }

        private void ShowGroupManagementButtons()
        {
            if (!_isAddToGroupMode) addGroupButton.Visibility = Visibility.Visible;
            backToAccountListButton.Visibility = Visibility.Visible;
            addAccountButton.Visibility = Visibility.Collapsed;
            groupManagementButton.Visibility = Visibility.Collapsed;
        }

        private void ShowInGroupButtons()
        {
            addAccountButton.Visibility = Visibility.Collapsed;
            backToAccountListButton.Visibility = Visibility.Visible;
            groupManagementButton.Visibility = Visibility.Visible;
            
            groupManagementButton.Click -= GroupManagementButton_Click;
            groupManagementButton.Click -= ReturnToGroupList_Click;
            groupManagementButton.Click += ReturnToGroupList_Click;
        }

        private void HideAllButtons()
        {
            addAccountButton.Visibility = Visibility.Collapsed;
            backToAccountListButton.Visibility = Visibility.Collapsed;
            addGroupButton.Visibility = Visibility.Collapsed;
            groupManagementButton.Visibility = Visibility.Collapsed;
        }

        private void ShowDefaultButtons()
        {
            addAccountButton.Visibility = Visibility.Collapsed;
            backToAccountListButton.Visibility = Visibility.Visible;
            addGroupButton.Visibility = Visibility.Collapsed;
            groupManagementButton.Visibility = Visibility.Collapsed;
        }

        private void ShowAddToGroupButton(AccountGroup group)
        {
            if (addGroupButton != null)
            {
                _isAddToGroupMode = true;
                _currentGroupIdForAdding = group.Id;
                
                var stackPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                stackPanel.Children.Add(new TextBlock { Text = "+", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 6, 0) });
                stackPanel.Children.Add(new TextBlock { Text = "移入账号", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                
                addGroupButton.Content = stackPanel;
                addGroupButton.Tag = group;
                
                addGroupButton.Click -= CreateGroupButton_Click;
                addGroupButton.Click -= OnAddGroupButtonClick;
                addGroupButton.Click += OnAddGroupButtonClick;
                addGroupButton.Visibility = Visibility.Visible;
            }
        }

        private void HideAddToGroupButton()
        {
            if (addGroupButton != null)
            {
                _isAddToGroupMode = false;
                _currentGroupIdForAdding = 0;
                
                var stackPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                stackPanel.Children.Add(new TextBlock { Text = "+", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 6, 0) });
                stackPanel.Children.Add(new TextBlock { Text = "添加分组", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                
                addGroupButton.Content = stackPanel;
                addGroupButton.Tag = null;
                
                addGroupButton.Click -= OnAddGroupButtonClick;
                addGroupButton.Click -= CreateGroupButton_Click;
                addGroupButton.Click += CreateGroupButton_Click;
                
                UpdateButtonVisibility();
            }
        }
        #endregion

        #region 游戏路径管理
        private void LoadGamePath()
        {
            try
            {
                string filePath = System.IO.Path.Combine(
                    System.AppDomain.CurrentDomain.BaseDirectory,
                    GAME_PATH_FILE);

                if (System.IO.File.Exists(filePath))
                {
                    string json = System.IO.File.ReadAllText(filePath);
                    using var document = System.Text.Json.JsonDocument.Parse(json);

                    if (document.RootElement.TryGetProperty("GamePath", out var pathElement))
                    {
                        _gamePath = pathElement.GetString();
                        UpdateGamePathDisplay();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载游戏路径失败: {ex.Message}");
                _gamePath = "";
            }
        }

        private void SaveGamePath()
        {
            try
            {
                var data = new { GamePath = _gamePath, LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
                string json = System.Text.Json.JsonSerializer.Serialize(data);
                string filePath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, GAME_PATH_FILE);
                System.IO.File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存游戏路径失败: {ex.Message}");
            }
        }

        private void UpdateGamePathDisplay()
        {
            if (gamePathText != null)
            {
                if (string.IsNullOrEmpty(_gamePath))
                {
                    gamePathText.Text = "未设置游戏路径";
                    gamePathText.Foreground = Brushes.Gray;
                }
                else if (System.IO.File.Exists(_gamePath))
                {
                    gamePathText.Text = _gamePath;
                    gamePathText.Foreground = Brushes.Black;
                }
                else
                {
                    gamePathText.Text = "路径不存在: " + System.IO.Path.GetFileName(_gamePath);
                    gamePathText.Foreground = Brushes.Red;
                }
            }
        }
        #endregion

        #region 事件处理程序
        #region 搜索框事件
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool hasText = !string.IsNullOrEmpty(searchTextBox.Text);
            clearSearchButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
            watermarkText.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
            UpdateAccountsList(searchTextBox.Text);

            if (!hasText && _selectedGroupId > 0)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                if (group != null)
                    ShowAddToGroupButton(group);
                UpdateButtonVisibility();
            }

            UpdateWatermarkVisibility();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            bool hadFocus = searchTextBox.IsFocused;
            searchTextBox.Text = string.Empty;
            clearSearchButton.Visibility = Visibility.Collapsed;
            watermarkText.Visibility = Visibility.Visible;

            if (hadFocus)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    searchTextBox.Focus();
                    Keyboard.Focus(searchTextBox);
                }, DispatcherPriority.Render);
            }

            UpdateWatermarkVisibility();
        }

        private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            searchBox.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x1A, 0x56, 0xDB));
            UpdateWatermarkVisibility();
        }

        private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            searchBox.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xDD, 0xDD, 0xDD));
            UpdateWatermarkVisibility();
        }
        #endregion

        #region 窗口事件
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as FrameworkElement;
            if (source != null && source.Name != "searchTextBox" && source.Name != "searchBox")
                FocusManager.SetFocusedElement(this, this);
        }
        #endregion

        #region 标记按钮事件
        private void MarkButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int accountId && _selectedGroupId > 0)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                if (group != null)
                {
                    var account = _allAccounts.FirstOrDefault(a => a.Id == accountId);
                    if (account != null)
                    {
                        ToggleAccountMarkInGroup(accountId, _selectedGroupId);
                        bool isNowMarked = group.IsAccountMarked(accountId);
                        string action = isNowMarked ? "已标记" : "已取消标记";
                        ShowToast($"{action}·账号「{account.Nickname}」在分组「{group.Name}」中", true);

                        UpdateAccountsList(searchTextBox.Text);
                        UpdateGroupInfoDisplay();
                    }
                }
            }
            else if (_selectedGroupId == 0)
            {
                ShowToast("提示", "标记功能仅在分组内可用", false);
            }
        }
        #endregion

        #region 置顶功能
        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int accountId && _selectedGroupId == 0)
            {
                var account = _allAccounts.FirstOrDefault(a => a.Id == accountId);
                if (account != null)
                {
                    account.SetPinned(!account.IsPinned);

                    if (account.IsPinned)
                    {
                        if (!_pinnedAccountOrder.Contains(accountId))
                            _pinnedAccountOrder.Add(accountId);
                    }
                    else
                    {
                        _pinnedAccountOrder.Remove(accountId);
                    }

                    SavePinnedOrders();
                    bool success = AccountDataService.SaveAccount(account);

                    string message = account.IsPinned
                        ? $"账号「{account.Nickname}」已置顶"
                        : $"账号「{account.Nickname}」已取消置顶";

                    ShowToast(account.IsPinned ? "已置顶" : "已取消置顶", message);

                    _allAccounts = AccountDataService.LoadAccounts();
                    UpdateAccountsList(searchTextBox.Text);
                }
            }
            else if (_selectedGroupId > 0)
            {
                ShowToast("提示", "置顶功能仅在主账号列表可用", false);
            }
        }

        private void GroupPinButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int groupId)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == groupId);
                if (group != null)
                {
                    group.SetPinned(!group.IsPinned);

                    if (group.IsPinned)
                    {
                        if (!_pinnedGroupOrder.Contains(groupId))
                            _pinnedGroupOrder.Add(groupId);
                    }
                    else
                    {
                        _pinnedGroupOrder.Remove(groupId);
                    }

                    SavePinnedOrders();
                    bool success = GroupDataService.SaveGroup(group);

                    string action = group.IsPinned ? "已置顶" : "已取消置顶";
                    string message = group.IsPinned
                        ? $"分组「{group.Name}」已置顶"
                        : $"分组「{group.Name}」已取消置顶";

                    ShowToast(action, message);

                    _allGroups = GroupDataService.LoadGroups();
                    SortGroups();

                    if (_currentDisplayMode == DisplayMode.GroupManagement)
                        UpdateGroupsList();
                }
            }
        }
        #endregion

        #region 账号管理事件
        private void AddAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDisplayMode != DisplayMode.AccountList)
                SwitchToDisplayMode(DisplayMode.AccountList);
            ShowAccountModal();
        }

        private void EditAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                var account = _allAccounts.FirstOrDefault(a => a.Id == id);
                if (account != null)
                    ShowAccountModal(account);
            }
        }

        private void DeleteAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                var account = _allAccounts.FirstOrDefault(a => a.Id == id);
                if (account != null)
                    ShowDeleteModal(account);
            }
        }

        private void RemoveFromGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int accountId)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                if (group != null)
                {
                    var account = _allAccounts.FirstOrDefault(a => a.Id == accountId);
                    if (account != null)
                    {
                        _selectedAccountIds = new List<int> { accountId };
                        ShowRemoveFromGroupModal(group, account);
                    }
                }
            }
        }
        #endregion

        #region 分组管理事件
        private void GroupManagementButton_Click(object sender, RoutedEventArgs e)
        {
            _allGroups = GroupDataService.LoadGroups();
            SortGroups();
            SwitchToDisplayMode(DisplayMode.GroupManagement);
            Dispatcher.BeginInvoke(() =>
            {
                UpdateGroupsList();
                UpdateGroupInfoDisplay();
            }, DispatcherPriority.Render);
        }

        private void CreateGroupButton_Click(object sender, RoutedEventArgs e)
        {
            ShowGroupModal();
        }

        private void EditGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == id);
                if (group != null)
                    ShowGroupModal(group);
            }
        }

        private void OpenGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == id);
                if (group != null)
                {
                    _selectedGroupId = id;
                    SelectedGroupId = id;
                    UpdateAccountsList();
                    SwitchToDisplayMode(DisplayMode.AccountList);
                    UpdateGroupInfoDisplay();
                    ShowAddToGroupButton(group);
                    UpdateButtonVisibility();
                    UpdateMarkColumnVisibility();
                }
            }
        }

        private void DeleteGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == id);
                if (group != null)
                    ShowDeleteGroupModal(group);
            }
        }
        #endregion

        #region 返回按钮事件
        private void NoDataBackButton_Click(object sender, RoutedEventArgs e)
        {
            searchTextBox.Text = string.Empty;
            clearSearchButton.Visibility = Visibility.Collapsed;
            watermarkText.Visibility = Visibility.Visible;

            if (_selectedGroupId > 0)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                if (group != null)
                    ShowAddToGroupButton(group);
            }

            UpdateButtonVisibility();
        }

        private void BackToAccountListButton_Click(object sender, RoutedEventArgs e)
        {
            bool hasSearchText = !string.IsNullOrWhiteSpace(searchTextBox.Text);
            bool hasAccountsInTotal = _allAccounts.Count > 0;

            if (hasSearchText && hasAccountsInTotal)
            {
                searchTextBox.Text = string.Empty;
                clearSearchButton.Visibility = Visibility.Collapsed;
                watermarkText.Visibility = Visibility.Visible;
            }

            _selectedGroupId = 0;
            SelectedGroupId = 0;
            UpdateAccountsList(searchTextBox.Text);

            if (_currentDisplayMode == DisplayMode.GroupManagement)
                SortGroups();

            SwitchToDisplayMode(DisplayMode.AccountList);

            if (_isAddToGroupMode)
                HideAddToGroupButton();

            UpdateButtonVisibility();
            UpdateWatermarkVisibility();
            UpdateMarkColumnVisibility();
        }

        private void ReturnToGroupList_Click(object sender, RoutedEventArgs e)
        {
            SwitchToDisplayMode(DisplayMode.GroupManagement);
        }
        #endregion

        #region DataGrid 事件
        private void DataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            e.Cancel = true;
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 处理选择改变的逻辑
        }

        private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            if (sender is DataGrid dataGrid)
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(dataGrid);
                if (scrollViewer != null)
                {
                    double scrollOffset = e.Delta > 0 ? -1 : 1;
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollOffset);
                }
            }
        }
        #endregion

        #region 密码操作
        private void TogglePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                var account = _accounts.FirstOrDefault(a => a.Id == id);
                if (account != null)
                    account.IsPasswordVisible = !account.IsPasswordVisible;
            }
        }
        #endregion

        #region 复制操作
        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                try
                {
                    var row = FindVisualParent<DataGridRow>(button);
                    if (row != null && row.DataContext is Account account)
                    {
                        var cell = FindVisualParent<DataGridCell>(button);
                        if (cell != null)
                        {
                            int columnIndex = cell.Column.DisplayIndex;
                            string textToCopy = "";
                            string contentType = "";

                            if (columnIndex == 2) // UID列
                            {
                                textToCopy = account.Uid;
                                contentType = "UID";
                            }
                            else if (columnIndex == 5) // 账号列
                            {
                                textToCopy = account.Username;
                                contentType = "账号";
                            }
                            else if (columnIndex == 6) // 密码列
                            {
                                textToCopy = account.Password;
                                contentType = "密码";
                            }

                            if (!string.IsNullOrEmpty(textToCopy))
                            {
                                NativeClipboardHelper.SafeSetText(textToCopy);
                                string message = GetCopyMessage(textToCopy, contentType);
                                ShowToast(message, true);
                            }
                        }
                    }
                }
                catch
                {
                    ShowToast("复制失败", false);
                }
            }
        }

        private string GetCopyMessage(string copiedText, string contentType)
        {
            switch (contentType)
            {
                case "UID":
                    return $"UID {copiedText} 已复制到剪贴板";
                case "账号":
                    return GetAccountCopyMessage(copiedText);
                case "密码":
                    string maskedPassword = new string('●', Math.Min(copiedText.Length, 6));
                    return $"密码 {maskedPassword} 已复制到剪贴板";
                default:
                    return $"{copiedText} 已复制到剪贴板";
            }
        }

        private string GetAccountCopyMessage(string copiedText)
        {
            if (copiedText.Length == 11 && copiedText.All(char.IsDigit))
            {
                string maskedAccount = $"{copiedText.Substring(0, 3)}●●●●{copiedText.Substring(7)}";
                return $"账号 {maskedAccount} 已复制到剪贴板";
            }
            else if (copiedText.Contains("@"))
            {
                var atIndex = copiedText.IndexOf('@');
                if (atIndex > 0)
                {
                    var prefix = copiedText.Substring(0, Math.Min(3, atIndex));
                    var suffix = copiedText.Substring(atIndex);
                    return $"账号 {prefix}●●●●{suffix} 已复制到剪贴板";
                }
            }
            else if (copiedText.Length > 8)
            {
                string maskedAccount = $"{copiedText.Substring(0, 4)}●●●●{copiedText.Substring(copiedText.Length - 4)}";
                return $"账号 {maskedAccount} 已复制到剪贴板";
            }
            return $"账号 {copiedText} 已复制到剪贴板";
        }
        #endregion

        #region 模态框通用操作
        private void ShowModal(Border modal)
        {
            modalOverlay.Visibility = Visibility.Visible;
            modal.Visibility = Visibility.Visible;

            modal.InvalidateVisual();
            modal.UpdateLayout();

            var storyboard = modal.Resources["FadeInAnimation"] as Storyboard;
            if (storyboard != null)
            {
                storyboard.Stop();
                Storyboard.SetTarget(storyboard, modal);
                storyboard.Begin();
            }
            else
            {
                modal.Opacity = 1;
                var transform = modal.RenderTransform as ScaleTransform;
                if (transform != null)
                {
                    transform.ScaleX = 1;
                    transform.ScaleY = 1;
                }
            }
        }

        private async void HideModal(Border modal)
        {
            var storyboard = modal.Resources["FadeOutAnimation"] as Storyboard;
            if (storyboard != null)
            {
                storyboard.Stop();
                Storyboard.SetTarget(storyboard, modal);
                storyboard.Begin();
                await Task.Delay(150);
            }

            modal.Visibility = Visibility.Collapsed;
            modalOverlay.Visibility = Visibility.Collapsed;

            modal.Opacity = 0;
            var transform = modal.RenderTransform as ScaleTransform;
            if (transform != null)
            {
                transform.ScaleX = 0.95;
                transform.ScaleY = 0.95;
            }
        }

        private void OnAddGroupButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && _isAddToGroupMode && button.Tag is AccountGroup group)
                ShowAddAccountsToGroupModal(group);
        }
        #endregion

        #region 模态框特定操作
        private void ShowDeleteModal(Account account)
        {
            if (deleteConfirmText != null)
                deleteConfirmText.Text = $"确定要删除账号「{account.Nickname}」吗？此操作无法撤销。";

            ShowModal(deleteModal);
            _selectedAccountIds = new List<int> { account.Id };
        }

        private void ShowDeleteGroupModal(AccountGroup group)
        {
            if (deleteGroupConfirmText != null)
                deleteGroupConfirmText.Text = $"确定要删除分组「{group.Name}」吗？此操作无法撤销。";

            ShowModal(deleteGroupModal);
            _selectedGroupId = group.Id;
        }

        private void ShowRemoveFromGroupModal(AccountGroup group, Account account)
        {
            if (removeFromGroupConfirmText != null)
                removeFromGroupConfirmText.Text = $"确定要从分组「{group.Name}」中移出账号「{account.Nickname}」吗？";

            ShowModal(removeFromGroupModal);
        }

        private void HideDeleteModal() => HideModal(deleteModal);
        private void HideDeleteGroupModal() => HideModal(deleteGroupModal);
        private void HideRemoveFromGroupModal() => HideModal(removeFromGroupModal);
        #endregion

        #region Toast 通知
        private void ShowToast(string title, string message, bool isSuccess = true)
        {
            toastText.Text = $"{title} • {message}";
            UpdateToastIcon(isSuccess);
            ShowToastNotification();
        }

        private void ShowToast(string message, bool isSuccess = true)
        {
            toastText.Text = message;
            UpdateToastIcon(isSuccess);
            ShowToastNotification();
        }

        private void UpdateToastIcon(bool isSuccess)
        {
            toastIcon.Text = isSuccess ? "✔" : "⚠️";
            toastIcon.Foreground = isSuccess ? Brushes.Green : Brushes.Red;
        }

        private void ShowToastNotification()
        {
            StartShowAnimation();
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void StartShowAnimation()
        {
            var showStoryboard = toastContainer.FindResource("ShowToastAnimation") as Storyboard;
            showStoryboard?.Begin();
        }

        private async void HideToast()
        {
            var hideStoryboard = toastContainer.FindResource("HideToastAnimation") as Storyboard;
            if (hideStoryboard != null)
            {
                hideStoryboard.Begin();
                await Task.Delay(500);
            }

            toastContainer.Visibility = Visibility.Collapsed;
            _toastTimer.Stop();
        }

        private void CloseToastButton_Click(object sender, RoutedEventArgs e) => HideToast();
        #endregion

        #region 确认操作事件
        private void ConfirmDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAccountIds.Count > 0)
            {
                bool success = AccountDataService.DeleteAccounts(_selectedAccountIds);

                if (success)
                {
                    _allAccounts.RemoveAll(a => _selectedAccountIds.Contains(a.Id));
                    UpdateAccountsList(searchTextBox.Text);

                    string message = _selectedAccountIds.Count == 1
                        ? "账号已成功删除"
                        : $"已成功删除 {_selectedAccountIds.Count} 个账号";

                    ShowToast("操作成功", message);
                }
                else
                {
                    ShowToast("操作失败", "删除账号失败", false);
                }

                HideDeleteModal();
            }
        }

        private void ConfirmDeleteGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroupId > 0)
            {
                bool success = GroupDataService.DeleteGroup(_selectedGroupId);

                if (success)
                {
                    _allGroups.RemoveAll(g => g.Id == _selectedGroupId);
                    SortGroups();
                    UpdateGroupInfoDisplay();

                    if (!_allGroups.Any())
                    {
                        _selectedGroupId = 0;
                        SelectedGroupId = 0;
                        if (_currentDisplayMode == DisplayMode.AccountList)
                        {
                            UpdateAccountsList(searchTextBox.Text);
                            SwitchToDisplayMode(DisplayMode.AccountList);
                        }
                    }
                    else if (_currentDisplayMode == DisplayMode.AccountList)
                    {
                        _selectedGroupId = 0;
                        SelectedGroupId = 0;
                        UpdateAccountsList(searchTextBox.Text);
                        SwitchToDisplayMode(DisplayMode.AccountList);
                    }

                    ShowToast("操作成功", "分组已成功删除");
                }
                else
                {
                    ShowToast("操作失败", "删除分组失败", false);
                }
                
                HideDeleteGroupModal();

                if (_currentDisplayMode == DisplayMode.GroupManagement)
                {
                    _allGroups = GroupDataService.LoadGroups();
                    SortGroups();
                    UpdateGroupInfoDisplay();
                    var groupsItemsControl = FindVisualChild<ItemsControl>(groupManagementMessage, "groupsItemsControl");
                    if (groupsItemsControl != null)
                    {
                        groupsItemsControl.ItemsSource = null;
                        UpdateGroupsList();
                    }
                    ShowGroupsListContainer(_allGroups.Count > 0);
                }
            }
        }

        private void ConfirmRemoveFromGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAccountIds.Count > 0)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                if (group != null)
                {
                    var account = _allAccounts.FirstOrDefault(a => a.Id == _selectedAccountIds[0]);
                    if (account != null && group.AccountIds.Contains(_selectedAccountIds[0]))
                    {
                        group.RemoveAccount(_selectedAccountIds[0]);
                        bool success = GroupDataService.SaveGroup(group);

                        if (success)
                        {
                            UpdateAccountListAfterRemove(group);
                            ShowToast("操作成功", $"已从分组「{group.Name}」中移出账号「{account.Nickname}」");
                        }
                        else
                        {
                            ShowToast("操作失败", "移出账号失败", false);
                        }
                    }
                    else
                    {
                        ShowToast("操作失败", "账号不在该分组中", false);
                    }
                }

                HideRemoveFromGroupModal();
            }
        }

        private void UpdateAccountListAfterRemove(AccountGroup group)
        {
            var groupIndex = _allGroups.FindIndex(g => g.Id == group.Id);
            if (groupIndex >= 0)
                _allGroups[groupIndex] = group;

            UpdateAccountsList(searchTextBox.Text);
            UpdateGroupInfoDisplay();
        }
        #endregion

        #region 窗口模态框方法
        private void ShowModalWindow(Window window)
        {
            if (_isModalWindowOpen) return;

            _currentModalWindow = window;
            _isModalWindowOpen = true;

            modalOverlay.Visibility = Visibility.Visible;

            window.Owner = this;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            window.Closed += ModalWindow_Closed;
            window.ShowDialog();
        }

        private void ModalWindow_Closed(object sender, EventArgs e)
        {
            if (sender is Window window)
            {
                window.Closed -= ModalWindow_Closed;
                _currentModalWindow = null;
                _isModalWindowOpen = false;
                modalOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowAccountModal(Account account = null)
        {
            var window = new AddEditAccountWindow(account);

            window.ShowToastRequested += (title, message, isSuccess) => ShowToast(title, message, isSuccess);

            window.Closed += (s, e) =>
            {
                if (window.DialogResult == true)
                    HandleAccountModalResult(window, account);
            };

            ShowModalWindow(window);
        }

        private void HandleAccountModalResult(AddEditAccountWindow window, Account originalAccount)
        {
            bool success;
            string message;

            if (originalAccount != null)
            {
                success = AccountDataService.SaveAccount(window.Account);
                message = success ? "账号已成功更新" : "更新账号失败";

                if (success)
                {
                    var index = _allAccounts.FindIndex(a => a.Id == originalAccount.Id);
                    if (index >= 0)
                        _allAccounts[index] = window.Account;
                    _allAccounts = AccountDataService.LoadAccounts();
                }
            }
            else
            {
                window.Account.Id = AccountDataService.GetNextId();
                success = AccountDataService.SaveAccount(window.Account);
                message = success ? "账号已成功添加" : "添加账号失败";

                if (success)
                    _allAccounts = AccountDataService.LoadAccounts();
            }

            if (success)
            {
                UpdateAccountsList(searchTextBox.Text);
                SwitchToDisplayMode(_allAccounts.Count > 0 ? DisplayMode.AccountList : DisplayMode.NoData);
                ShowToast("操作成功", message);
            }
            else
            {
                ShowToast("操作失败", message, false);
            }
        }

        private void ShowGroupModal(AccountGroup group = null)
        {
            var window = new AddEditGroupWindow(group);

            window.ShowToastRequested += (title, message, isSuccess) => ShowToast(title, message, isSuccess);

            window.Closed += (s, e) =>
            {
                if (window.DialogResult == true)
                    HandleGroupModalResult(window, group);
            };

            ShowModalWindow(window);
        }

        private void HandleGroupModalResult(AddEditGroupWindow window, AccountGroup originalGroup)
        {
            bool success;
            string message;

            if (originalGroup != null)
            {
                success = GroupDataService.SaveGroup(window.Group);
                message = success ? "分组已成功更新" : "更新分组失败";

                if (success)
                {
                    var index = _allGroups.FindIndex(g => g.Id == originalGroup.Id);
                    if (index >= 0)
                    {
                        _allGroups[index].Name = window.Group.Name;
                        _allGroups[index].TagNote = window.Group.TagNote;
                        _allGroups[index].AccountIds = window.Group.AccountIds;
                        _allGroups[index].AccountAddTimes = window.Group.AccountAddTimes;
                        _allGroups[index].MarkedTimes = window.Group.MarkedTimes;
                        _allGroups[index].IsPinned = window.Group.IsPinned;
                        _allGroups[index].PinnedTime = window.Group.PinnedTime;
                    }
                    SortGroups();
                }
            }
            else
            {
                window.Group.Id = GroupDataService.GetNextId();
                success = GroupDataService.SaveGroup(window.Group);
                message = success ? "分组已成功添加" : "添加分组失败";

                if (success)
                {
                    _allGroups.Add(window.Group);
                    SortGroups();
                }
            }

            if (success)
            {
                _allGroups = GroupDataService.LoadGroups();
                SortGroups();
                UpdateGroupInfoDisplay();
                ShowToast("操作成功", message);

                if (_currentDisplayMode == DisplayMode.GroupManagement)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        ShowGroupsListContainer(true);
                        UpdateGroupsList();
                    }, DispatcherPriority.Render);
                }
            }
            else
            {
                ShowToast("操作失败", message, false);
            }
        }

        private void ShowAddAccountsToGroupModal(AccountGroup group)
        {
            var window = new AddAccountsToGroupWindow(group.Id);

            window.ShowToastRequested += (title, message, isSuccess) => ShowToast(title, message, isSuccess);

            window.Closed += (s, e) =>
            {
                if (window.DialogResult == true)
                    HandleAddAccountsToGroupResult(window);
            };

            ShowModalWindow(window);
        }

        private void HandleAddAccountsToGroupResult(AddAccountsToGroupWindow window)
        {
            _allGroups = GroupDataService.LoadGroups();
            var currentGroup = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);

            if (currentGroup != null)
            {
                _accounts.Clear();
                var groupAccounts = _allAccounts
                    .Where(a => currentGroup.AccountIds.Contains(a.Id))
                    .ToList();

                foreach (var account in groupAccounts)
                    account.IsMarked = currentGroup.IsAccountMarked(account.Id);

                var sortedAccounts = groupAccounts
                    .OrderBy(a => currentGroup.AccountAddTimes?.ContainsKey(a.Id) == true 
                        ? currentGroup.AccountAddTimes[a.Id] 
                        : a.CreatedTime)
                    .ToList();

                foreach (var account in sortedAccounts)
                    _accounts.Add(account);

                accountsDataGrid.ItemsSource = null;
                accountsDataGrid.ItemsSource = _accounts;
                UpdateUIState();
                UpdateGroupInfoDisplay();
                SwitchToDisplayMode(groupAccounts.Count == 0 ? DisplayMode.NoData : DisplayMode.AccountList);

                if (window.SelectedAccountIds.Count > 0)
                    ShowToast("操作成功", $"已成功将 {window.SelectedAccountIds.Count} 个账号移入分组");
            }
        }
        #endregion

        #region 登录相关
        private void LoginAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                var account = _allAccounts.FirstOrDefault(a => a.Id == id);
                if (account != null)
                    ShowLoginModal(account);
            }
        }

        private void ShowLoginModal(Account account)
        {
            if (loginConfirmText != null)
                loginConfirmText.Text = $"确定要登录账号「{account.Nickname}」到游戏中吗？";

            _selectedAccountIds = new List<int> { account.Id };

            // 重置UI状态
            loginModalTitle.Text = "确认登录";
            confirmLoginButton.Content = "确认登录";
            confirmLoginButton.IsEnabled = false;
            confirmLoginButton.Background = Brushes.LightGray;
            cancelLoginButton.IsEnabled = true;
            closeLoginModalButton.IsEnabled = true;
            adminStatusText.Text = "检查中...";
            adminStatusText.Foreground = Brushes.Orange;
            gameStatusText.Text = "检查中...";
            gameStatusText.Foreground = Brushes.Orange;

            ShowModal(loginModal);

            // 立即执行一次状态检查
            UpdateStatusInfo();

            // 开始状态检查定时器
            _statusCheckTimer.Start();
        }

        private async void ConfirmLoginButton_Click(object sender, RoutedEventArgs e)
        {
            string currentButtonText = GetButtonContentText(confirmLoginButton);

            if (currentButtonText == "登录完成")
            {
                HideLoginModal();
                return;
            }

            if (currentButtonText == "停止登录")
            {
                await StopLoginOperation();
                return;
            }

            if (!confirmLoginButton.IsEnabled)
            {
                ShowToast("状态未就绪", "请等待管理员权限和游戏状态检查完成", false);
                return;
            }

            // 检查管理员权限
            bool hasAdmin = false;
            try
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    hasAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                hasAdmin = false;
            }

            if (!hasAdmin)
            {
                ShowToast("权限不足", "需要管理员权限才能执行自动登录", false);
                confirmLoginButton.IsEnabled = false;
                confirmLoginButton.Background = Brushes.LightGray;
                return;
            }

            if (_selectedAccountIds.Count > 0)
            {
                var account = _allAccounts.FirstOrDefault(a => a.Id == _selectedAccountIds[0]);
                if (account != null)
                    StartLoginOperation(account);
            }
        }

        private string GetButtonContentText(Button button)
        {
            if (button.Content == null)
                return string.Empty;

            if (button.Content is string text)
                return text;

            if (button.Content is StackPanel stackPanel)
            {
                foreach (var child in stackPanel.Children)
                {
                    if (child is TextBlock textBlock)
                        return textBlock.Text;
                }
            }

            return string.Empty;
        }

        private void StartLoginOperation(Account account)
        {
            _isLoginInProgress = true;
            _loginCancellationTokenSource = new CancellationTokenSource();

            // 订阅登录完成事件
            GenshinWindowActivator.LoginCompleted += OnGenshinLoginCompleted;

            // 更新UI状态
            loginModalTitle.Text = "登录中...";
            confirmLoginButton.Content = "停止登录";
            cancelLoginButton.IsEnabled = false;
            closeLoginModalButton.IsEnabled = false;

            // 显示Toast
            ShowToast("正在登录", $"正在激活原神窗口...");

            // 启动登录任务
            _loginTask = Task.Run(() => ExecuteLoginAsync(account, _loginCancellationTokenSource.Token));
        }

        private async Task ExecuteLoginAsync(Account account, CancellationToken cancellationToken)
        {
            try
            {
                // 执行登录
                GenshinWindowActivator.ExecuteLogin(account.Username, account.Password);

                // 创建一个TaskCompletionSource来等待登录完成事件
                var loginCompletionSource = new TaskCompletionSource<bool>();

                // 临时事件处理器
                Action<bool> loginCompletedHandler = null;
                loginCompletedHandler = (success) => {
                    GenshinWindowActivator.LoginCompleted -= loginCompletedHandler;
                    loginCompletionSource.TrySetResult(success);
                };

                GenshinWindowActivator.LoginCompleted += loginCompletedHandler;

                // 等待登录完成
                bool success = await loginCompletionSource.Task;

                Dispatcher.Invoke(() => {
                    if (success)
                    {
                        UpdateLoginCompleteUI("登录完成", true);
                        ShowToast("登录成功", "账号已成功登录到游戏中");
                    }
                    else
                    {
                        UpdateLoginCompleteUI("登录失败", false);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // 登录被取消
                Dispatcher.Invoke(() => UpdateLoginCompleteUI("登录已取消", false));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => UpdateLoginCompleteUI($"登录失败: {ex.Message}", false));
            }
            finally
            {
                // 确保取消订阅事件
                GenshinWindowActivator.LoginCompleted -= OnGenshinLoginCompleted;
            }
        }

        private void OnGenshinLoginCompleted(bool success)
        {
            Dispatcher.Invoke(() => {
                if (success)
                {
                    UpdateLoginCompleteUI("登录完成", true);
                    ShowToast("登录成功", "账号已成功登录到游戏中");
                }
                else
                {
                    UpdateLoginCompleteUI("登录失败", false);
                }
            });
        }

        private void UpdateLoginCompleteUI(string status, bool isSuccess)
        {
            _isLoginInProgress = false;

            // 更新按钮文本和状态
            confirmLoginButton.Content = status == "登录完成" ? "登录完成" : "确认登录";

            // 如果登录完成，按钮可以点击关闭窗口
            if (status == "登录完成")
            {
                confirmLoginButton.IsEnabled = true;
                confirmLoginButton.Background = Brushes.Green;
                cancelLoginButton.IsEnabled = true;
                closeLoginModalButton.IsEnabled = true;
            }
            else if (status == "登录失败" || status == "登录已取消")
            {
                confirmLoginButton.Content = "确认登录";
                confirmLoginButton.IsEnabled = true;
                confirmLoginButton.Background = (SolidColorBrush)FindResource("PrimaryBlueBrush");
                cancelLoginButton.IsEnabled = true;
                closeLoginModalButton.IsEnabled = true;

                // 登录失败后重新开始状态检查
                _statusCheckTimer.Start();
            }

            // 更新标题
            loginModalTitle.Text = status;
            loginModalTitle.Foreground = isSuccess ? Brushes.Green : Brushes.Red;
        }

        private async Task StopLoginOperation()
        {
            if (_isLoginInProgress && _loginCancellationTokenSource != null)
            {
                // 取消订阅事件
                GenshinWindowActivator.LoginCompleted -= OnGenshinLoginCompleted;

                _loginCancellationTokenSource.Cancel();

                // 等待任务结束
                try
                {
                    await Task.WhenAny(_loginTask, Task.Delay(2000));
                }
                catch { }

                // 停止自动检测
                GenshinWindowActivator.StopAutoDetect();

                // 更新UI
                _isLoginInProgress = false;

                Dispatcher.Invoke(() => {
                    confirmLoginButton.Content = "确认登录";
                    confirmLoginButton.Background = (SolidColorBrush)FindResource("PrimaryBlueBrush");
                    confirmLoginButton.IsEnabled = true;
                    cancelLoginButton.IsEnabled = true;
                    closeLoginModalButton.IsEnabled = true;
                    loginModalTitle.Text = "登录已停止";
                    loginModalTitle.Foreground = Brushes.Red;

                    // 登录停止后重新开始状态检查
                    _statusCheckTimer.Start();
                });
            }
        }

        private void HideLoginModal()
        {
            // 停止状态检查定时器
            _statusCheckTimer.Stop();

            // 停止任何正在进行的登录操作
            if (_isLoginInProgress && _loginCancellationTokenSource != null)
            {
                _loginCancellationTokenSource.Cancel();
                GenshinWindowActivator.StopAutoDetect();
            }

            _isLoginInProgress = false;

            // 重置按钮样式
            confirmLoginButton.Content = "确认登录";
            confirmLoginButton.Background = (SolidColorBrush)FindResource("PrimaryBlueBrush");
            confirmLoginButton.IsEnabled = false;

            cancelLoginButton.IsEnabled = true;
            closeLoginModalButton.IsEnabled = true;

            loginModalTitle.Text = "确认登录";
            loginModalTitle.Foreground = Brushes.Black;

            // 调用HideModal来关闭模态框
            HideModal(loginModal);
            _selectedAccountIds.Clear();
        }
        #endregion

        #region 状态检查
        private void StatusCheckTimer_Tick(object sender, EventArgs e)
        {
            if (loginModal.Visibility == Visibility.Visible)
                UpdateStatusInfo();
        }

        private void UpdateStatusInfo()
        {
            bool adminStatusOk = CheckAdminStatus();
            bool gameStatusOk = CheckGameStatus();

            // 更新按钮状态
            Dispatcher.BeginInvoke(() =>
            {
                if (adminStatusOk && gameStatusOk)
                {
                    if (confirmLoginButton.Content.ToString() == "确认登录" && !confirmLoginButton.IsEnabled)
                    {
                        confirmLoginButton.IsEnabled = true;
                        confirmLoginButton.Background = (SolidColorBrush)FindResource("PrimaryBlueBrush");
                    }
                }
                else if (!adminStatusOk)
                {
                    confirmLoginButton.IsEnabled = false;
                    confirmLoginButton.Background = Brushes.LightGray;

                    if (confirmLoginButton.Content.ToString() == "确认登录")
                        confirmLoginButton.Content = "确认登录";
                }
                else if (!gameStatusOk)
                {
                    if (confirmLoginButton.Content.ToString() == "确认登录" && !confirmLoginButton.IsEnabled)
                    {
                        confirmLoginButton.IsEnabled = true;
                        confirmLoginButton.Background = (SolidColorBrush)FindResource("PrimaryBlueBrush");
                    }
                }
            }, DispatcherPriority.Background);
        }

        private bool CheckAdminStatus()
        {
            try
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    bool isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                    
                    adminStatusText.Text = isAdmin ? "已是管理员" : "未获取管理员权限 无法执行登录";
                    adminStatusText.Foreground = isAdmin ? Brushes.Green : Brushes.Red;
                    return isAdmin;
                }
            }
            catch
            {
                adminStatusText.Text = "未知";
                adminStatusText.Foreground = Brushes.Red;
                return false;
            }
        }

        private bool CheckGameStatus()
        {
            try
            {
                IntPtr hWnd = GenshinWindowActivator.FindWindow("UnityWndClass", null);
                bool isGameRunning = hWnd != IntPtr.Zero;
                
                gameStatusText.Text = isGameRunning ? "已准备就绪" : "未启动，使用路径启动游戏或手动启动游戏";
                gameStatusText.Foreground = isGameRunning ? Brushes.Green : Brushes.Red;
                return isGameRunning;
            }
            catch
            {
                gameStatusText.Text = "未知";
                gameStatusText.Foreground = Brushes.Red;
                return false;
            }
        }
        #endregion

        #region 置顶顺序管理
        private void SavePinnedOrders()
        {
            try
            {
                var orders = new { AccountOrder = _pinnedAccountOrder, GroupOrder = _pinnedGroupOrder };
                string json = System.Text.Json.JsonSerializer.Serialize(orders);
                string filePath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "pinned_orders.json");
                System.IO.File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存置顶顺序失败: {ex.Message}");
            }
        }

        private void LoadPinnedOrders()
        {
            try
            {
                string filePath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "pinned_orders.json");

                if (System.IO.File.Exists(filePath))
                {
                    string json = System.IO.File.ReadAllText(filePath);
                    using var document = System.Text.Json.JsonDocument.Parse(json);
                    var root = document.RootElement;

                    if (root.TryGetProperty("AccountOrder", out var accountOrderElement))
                        _pinnedAccountOrder = accountOrderElement.EnumerateArray().Select(x => x.GetInt32()).ToList();

                    if (root.TryGetProperty("GroupOrder", out var groupOrderElement))
                        _pinnedGroupOrder = groupOrderElement.EnumerateArray().Select(x => x.GetInt32()).ToList();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载置顶顺序失败: {ex.Message}");
                _pinnedAccountOrder = new List<int>();
                _pinnedGroupOrder = new List<int>();
            }
        }
        #endregion

        #region 清除标记相关
        private void ClearMarksButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int groupId)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == groupId);
                if (group != null)
                {
                    int markedCount = group.MarkedTimes?.Count ?? 0;
                    if (markedCount > 0)
                        ShowClearMarksModal(group, markedCount);
                    else
                        ShowToast("提示", "该分组中没有已标记的账号", false);
                }
            }
        }

        private void ShowClearMarksModal(AccountGroup group, int markedCount)
        {
            _selectedGroupId = group.Id;

            if (clearMarksConfirmText != null)
                clearMarksConfirmText.Text = $"确定要清除分组「{group.Name}」中的所有标记吗？";
            if (clearMarksExtraText != null)
                clearMarksExtraText.Text = $"该分组中共有 {markedCount} 个已标记账号，清除后将全部恢复为未标记状态";
            
            ShowModal(clearMarksModal);
        }

        private void ConfirmClearMarksButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroupId > 0)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                if (group != null && group.MarkedTimes?.Count > 0)
                {
                    int markedCount = group.MarkedTimes.Count;
                    ClearAllMarksInGroup(_selectedGroupId);

                    UpdateGroupsList();
                    UpdateGroupInfoDisplay();

                    if (_currentDisplayMode == DisplayMode.AccountList)
                        UpdateAccountsList(searchTextBox.Text);

                    ShowToast("操作成功", $"已清除分组「{group.Name}」中的 {markedCount} 个标记");
                }
                else
                {
                    ShowToast("提示", "该分组中没有已标记的账号", false);
                }

                HideModal(clearMarksModal);
            }
        }

        private void HideClearMarksModal()
        {
            HideModal(clearMarksModal);
            _selectedGroupId = 0;
        }
        #endregion

        #region 模态框按钮事件
        private void CloseDeleteModalButton_Click(object sender, RoutedEventArgs e) => HideDeleteModal();
        private void CancelDeleteButton_Click(object sender, RoutedEventArgs e) => HideDeleteModal();
        private void CloseDeleteGroupModalButton_Click(object sender, RoutedEventArgs e) => HideDeleteGroupModal();
        private void CancelDeleteGroupButton_Click(object sender, RoutedEventArgs e) => HideDeleteGroupModal();
        private void CloseRemoveFromGroupModalButton_Click(object sender, RoutedEventArgs e) => HideRemoveFromGroupModal();
        private void CancelRemoveFromGroupButton_Click(object sender, RoutedEventArgs e) => HideRemoveFromGroupModal();
        private void CloseLoginModalButton_Click(object sender, RoutedEventArgs e) => HideLoginModal();
        private void CancelLoginButton_Click(object sender, RoutedEventArgs e) => HideLoginModal();
        private void BrowseGamePathButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "原神游戏程序|YuanShen.exe;GenshinImpact.exe|所有文件|*.*",
                    Title = "选择原神游戏程序",
                    InitialDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
                    Multiselect = false
                };

                if (!string.IsNullOrEmpty(_gamePath) && System.IO.File.Exists(_gamePath))
                    openFileDialog.InitialDirectory = System.IO.Path.GetDirectoryName(_gamePath);

                if (openFileDialog.ShowDialog() == true)
                {
                    string selectedPath = openFileDialog.FileName;
                    string fileName = System.IO.Path.GetFileName(selectedPath).ToLower();
                    
                    if (fileName != "yuanshen.exe" && fileName != "genshinimpact.exe")
                    {
                        MessageBox.Show("请选择正确的原神游戏程序 (YuanShen.exe 或 GenshinImpact.exe)", "路径错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    _gamePath = selectedPath;
                    UpdateGamePathDisplay();
                    SaveGamePath();

                    ShowToast("游戏路径已保存", $"已设置游戏路径: {System.IO.Path.GetFileName(_gamePath)}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"选择游戏路径失败: {ex.Message}");
                ShowToast("错误", $"选择游戏路径失败: {ex.Message}", false);
            }
        }
        private void CloseClearMarksModalButton_Click(object sender, RoutedEventArgs e) => HideClearMarksModal();
        private void CancelClearMarksButton_Click(object sender, RoutedEventArgs e) => HideClearMarksModal();
        #endregion

        #region PropertyChanged 支持
        private static void OnSelectedGroupIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MainWindow window)
            {
                window._selectedGroupId = (int)e.NewValue;
                window.OnPropertyChanged(nameof(window.IsInGroupMode));
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
        #endregion 
    }
}