using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ULT;

public partial class MainWindow : Window
{
    private readonly string ToolName = AppVersion.Full;
    private readonly List<FileTabState> _tabs = new();
    private FileTabState _activeTab;
    private FileTabState _pendingSelectionRestoreTab;
    private ScrollViewer _dataGridScrollViewer;

    private ScrollViewer DataGridScrollViewer
    {
        get
        {
            if (_dataGridScrollViewer == null)
            {
                DataGridMain.ApplyTemplate();
                _dataGridScrollViewer = FindScrollViewer(DataGridMain);
            }
            return _dataGridScrollViewer;
        }
    }
    private bool _suppressTabSwitch = false;

    private readonly struct TabSwitchSuppressionScope : IDisposable
    {
        private readonly MainWindow _owner;

        public TabSwitchSuppressionScope(MainWindow owner)
        {
            _owner = owner;
            _owner._suppressTabSwitch = true;
        }

        public void Dispose() => _owner._suppressTabSwitch = false;
    }

    private TabSwitchSuppressionScope SuppressTabSwitch() => new(this);
    private bool _suppressGridSelectionSync = false;
    private GridSelectionSyncSuppressionScope SuppressGridSelectionSync() => new(this);
    private TabItem _plusTabItem;
    private bool _ignoreNextPlusTabSelection = false;
    private Grid _tabScrollLeftBtn;
    private Grid _tabScrollRightBtn;
    private bool _suppressFilterComboBox = false;
    private FileTreeManager _fileTreeManager;
    private bool _fileTreeVisible = false;
    private double _fileTreeLastWidth = 120;
    private readonly RecentItemsManager<string> _recentFilesManager;
    private readonly RecentItemsManager<string> _recentFoldersManager;
    private readonly RecentItemsManager<string> _recentSearchesManager;
    private CancellationTokenSource _statsCts;
    private ReplaceWindow replaceWindow;
    private GlobalSearchWindow _globalSearchWindow;
    private GlobalStatsWindow _globalStatsWindow;
    private GlossaryWindow _glossaryWindow;
    private GlobalFilterWindow _globalFilterWindow;
    private readonly DiscordPresenceManager _discord = new();
    private System.Windows.Threading.DispatcherTimer _searchNotFoundTimer;
    private string _spellContextMenuSelectedText = "";
    private bool _isClosing = false;
    private string _lastGlobalSearchQuery = "";
    private string _lastReplaceFindText = "";
    private string _lastReplaceText = "";

    private string Tab_FilePath { get => _activeTab?.FilePath ?? ""; set { if (_activeTab != null) _activeTab.FilePath = value; } }
    private string Tab_FileType { get => _activeTab?.FileType ?? ""; set { if (_activeTab != null) _activeTab.FileType = value; } }
    private bool Tab_HasChanges { get => _activeTab?.HasUnsavedChanges ?? false; set { if (_activeTab != null) _activeTab.HasUnsavedChanges = value; } }
    private UndoRedoManager Tab_Undo => _activeTab?.UndoRedo;
    private SearchManager Tab_Search => _activeTab?.SearchManager;
    private GridRowFilter Tab_Filter { get => _activeTab?.ActiveGridFilter ?? GridRowFilter.None; set { if (_activeTab != null) _activeTab.ActiveGridFilter = value; } }
    private ICollectionView Tab_View => _activeTab?.DataGridView;
    private ObservableCollection<DataGridItem> Tab_Rows => _activeTab?.DataRows;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);

    private string _editingTranslationBefore
    {
        get => _activeTab?.EditingTranslationBefore;
        set { if (_activeTab != null) _activeTab.EditingTranslationBefore = value; }
    }
    private bool _searchWasFilteredSearch
    {
        get => _activeTab?.SearchWasFilteredSearch ?? false;
        set { if (_activeTab != null) _activeTab.SearchWasFilteredSearch = value; }
    }

    public MainWindow()
    {
        InitializeComponent();
        ThemeMenuItem.Header = App.IsDarkTheme() ? "Світла тема" : "Темна тема";
        _recentFilesManager = RecentManagers.ForFiles();
        UpdateRecentFilesMenu();
        _recentFoldersManager = RecentManagers.ForFolders();
        UpdateRecentFoldersMenu();
        _recentSearchesManager = RecentManagers.ForSearches();
        LoadSearchHistory();
        UpdateLoadStatusesVisibility();
        CreateNewTab();
        AddPlusTab();
        InitTabScrollButtons();

        if (App.StartupFilePaths.Count > 0)
        {
            Loaded += async (s, e) =>
            {
                var versionSession = new FolderVersionSession();
                var firstTab = _activeTab;

                await LoadFileIntoTab(firstTab, App.StartupFilePaths[0], versionSession, isBatchLoad: true);

                if (App.StartupDirectFilePaths.Contains(App.StartupFilePaths[0]))
                    _recentFilesManager.Add(App.StartupFilePaths[0]);

                var lastTab = firstTab;

                for (int i = 1; i < App.StartupFilePaths.Count; i++)
                {
                    if (versionSession.Cancelled)
                        break;

                    lastTab = CreateNewTab(activate: false);
                    await LoadFileIntoTab(lastTab, App.StartupFilePaths[i], versionSession, isBatchLoad: true);

                    if (App.StartupDirectFilePaths.Contains(App.StartupFilePaths[i]))
                        _recentFilesManager.Add(App.StartupFilePaths[i]);
                }

                _fileTreeManager?.FinalizeBatchAdd();
                var tabToActivate = !string.IsNullOrEmpty(lastTab.FilePath) ? lastTab : firstTab;
                int idx = _tabs.IndexOf(tabToActivate);

                if (idx >= 0)
                {
                    using (SuppressTabSwitch())
                        FileTabs.SelectedIndex = idx;

                    ActivateTab(tabToActivate);
                    if (FileTabs.Items[idx] is TabItem ti)
                        ti.Focus();
                }

                foreach (var folder in App.StartupFolderPaths)
                    _recentFoldersManager.Add(folder);

                UpdateRecentFilesMenu();
                UpdateRecentFoldersMenu();
            };
        }

        Closing += MainWindow_Closing;
        Closing += (s, e) => _discord?.Dispose();
        Loaded += (s, e) => InitFileTree();
        SpellCheckService.Initialize(AppDomain.CurrentDomain.BaseDirectory);
        ApplyOriginalTextBoxSettings();
        EditTextBox.TextArea.TextView.LineTransformers.Add(new TagVariableHighlighter());
        EditTextBox.LostKeyboardFocus += (s, e) => FlushEditTextBox();
        ApplyEditTextBoxSettings();
        GlossaryManager.Instance.Load();
        TranslationMemoryManager.Instance.LoadAll();
        SubscribeTranslationMemoryEvents();
    }

    private static string N(int n) => n.ToString("N0", new System.Globalization.CultureInfo("uk-UA"));

    public List<FileTabState> GetAllTabs()
    {
        return _tabs.ToList();
    }

    private async void SafeExecute(Func<Task> asyncAction, string errorTitle = "Помилка")
    {
        try
        {
            await asyncAction();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, errorTitle, MessageBoxButton.OK);
        }
    }

    #region Tab Management
    private FileTabState CreateNewTab(bool activate = true)
    {
        var tab = new FileTabState();
        tab.InitCollectionView();
        _tabs.Add(tab);

        var tabItem = new TabItem
        {
            DataContext = tab,
            Tag = tab
        };
        tabItem.Header = tab.Header;
        tabItem.AddHandler(Button.ClickEvent, new RoutedEventHandler(TabCloseButton_Click));

        tab.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FileTabState.Header))
            {
                tabItem.Header = tab.Header;
            }
            else if (e.PropertyName == nameof(FileTabState.FilePath))
            {
                tabItem.ToolTip = string.IsNullOrEmpty(tab.FilePath) ? null : tab.FilePath;
            }
            else if (e.PropertyName == nameof(FileTabState.HasUnsavedChanges))
            {
                _fileTreeManager?.UpdateUnsavedFlag(tab.FilePath, tab.HasUnsavedChanges);

                if (tab == _activeTab && !string.IsNullOrEmpty(tab.FilePath))
                {
                    var (openFiles, totalRows) = GetOpenFilesStats();
                    _discord.SetFileState(tab.FilePath, tab.DataRows.Count, totalRows, openFiles, tab.HasUnsavedChanges);
                }
            }
        };

        var ctxMenu = new ContextMenu();

        var closeOthersItem = new MenuItem { Header = "Закрити всі, окрім цієї" };
        closeOthersItem.Click += async (s, e) => await CloseAllTabsExcept(tab);

        var closeAllItem = new MenuItem { Header = "Закрити всі вкладки" };
        closeAllItem.Click += async (s, e) => await CloseAllTabs();

        var openFolderItem = new MenuItem { Header = "Відкрити шлях до файлу" };
        openFolderItem.Click += (s, e) =>
        {
            if (!string.IsNullOrEmpty(tab.FilePath) && System.IO.File.Exists(tab.FilePath))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{tab.FilePath}\"");
        };

        var copyPathItem = new MenuItem { Header = "Копіювати шлях" };
        copyPathItem.Click += (s, e) =>
        {
            if (!string.IsNullOrEmpty(tab.FilePath))
                Clipboard.SetText(tab.FilePath);
        };

        ctxMenu.Items.Add(closeOthersItem);
        ctxMenu.Items.Add(closeAllItem);
        ctxMenu.Items.Add(new Separator { Style = (Style)FindResource("ThinMenuSeparator") });
        ctxMenu.Items.Add(openFolderItem);
        ctxMenu.Items.Add(copyPathItem);

        ctxMenu.Opened += (s, e) =>
        {
            bool hasFile = !string.IsNullOrEmpty(tab.FilePath);
            openFolderItem.IsEnabled = hasFile;
            copyPathItem.IsEnabled = hasFile;
        };

        tabItem.ContextMenu = ctxMenu;

        using (SuppressTabSwitch())
        {
            if (_plusTabItem != null && FileTabs.Items.Count > 0 && FileTabs.Items[^1] == _plusTabItem)
                FileTabs.Items.Insert(FileTabs.Items.Count - 1, tabItem);
            else
                FileTabs.Items.Add(tabItem);
        }

        if (activate)
        {
            using (SuppressTabSwitch())
                FileTabs.SelectedItem = tabItem;

            ActivateTab(tab);
        }

        return tab;
    }

    private (int openFiles, int totalRows) GetOpenFilesStats()
        => (_tabs.Count(t => !string.IsNullOrEmpty(t.FilePath)), _tabs.Sum(t => t.DataRows.Count));

    private void SelectAndActivateTab(FileTabState tab)
    {
        int idx = _tabs.IndexOf(tab);
        if (idx < 0 || idx >= FileTabs.Items.Count) return;

        using (SuppressTabSwitch())
            FileTabs.SelectedIndex = idx;

        ActivateTab(tab);
    }

    private async Task<bool> CloseTabsWithConfirmation(List<FileTabState> tabsToClose)
    {
        foreach (var tab in tabsToClose.Where(t => !t.HasUnsavedChanges).ToList())
        {
            if (_tabs.Contains(tab))
                RemoveTab(tab);
        }

        foreach (var tab in tabsToClose.Where(t => t.HasUnsavedChanges).ToList())
        {
            if (!_tabs.Contains(tab))
                continue;

            SelectAndActivateTab(tab);

            string fileName = string.IsNullOrEmpty(tab.FilePath) ? "новий файл" : Path.GetFileName(tab.FilePath);
            var result = MessageBox.Show(this, $"Є незбережені зміни у {fileName}.\nЗберегти перед закриттям?", "Підтвердження", MessageBoxButton.YesNoCancel);

            if (result == MessageBoxResult.Cancel)
                return false;

            if (result == MessageBoxResult.Yes)
            {
                await SaveTabForCloseAsync(tab);

                if (!tab.HasUnsavedChanges)
                    RemoveTab(tab);
            }
            else if (result == MessageBoxResult.No)
            {
                RemoveTab(tab);
            }
        }

        return true;
    }

    private async Task CloseAllTabs()
    {
        _globalSearchWindow?.Close();
        await CloseTabsWithConfirmation(_tabs.ToList());
    }

    private async Task CloseAllTabsExcept(FileTabState keepTab)
    {
        _globalSearchWindow?.Close();

        bool completed = await CloseTabsWithConfirmation(_tabs.Where(t => t != keepTab).ToList());

        if (!completed)
            return;

        if (_tabs.Contains(keepTab))
            SelectAndActivateTab(keepTab);
    }

    private void AddPlusTab()
    {
        _plusTabItem = new TabItem
        {
            Tag = "__plus__",
            Width = 26,
            Padding = new Thickness(0),
            Header = new TextBlock
            {
                Text = "+",
                FontSize = 15,
                FontWeight = FontWeights.Normal,
                Margin = new Thickness(0, -3.5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Width = 26
            }
        };

        _plusTabItem.PreviewMouseLeftButtonDown += (s, e) =>
        {
            bool alreadyOnEmptyTab = _activeTab != null
                && string.IsNullOrEmpty(_activeTab.FilePath)
                && !_activeTab.HasUnsavedChanges;

            if (alreadyOnEmptyTab)
                e.Handled = true;
        };

        _plusTabItem.MouseLeftButtonUp += (s, e) =>
        {
            _ignoreNextPlusTabSelection = true;
            FindOrCreateEmptyTab(activate: true);
            e.Handled = true;
        };

        FileTabs.Items.Add(_plusTabItem);
        _plusTabItem.Style = FindResource(typeof(TabItem)) as Style;
    }

    private void UpdateColumnHeaders(string origLabel, string transLabel)
    {
        ColOriginal.Header = string.IsNullOrWhiteSpace(origLabel) ? "Оригінал" : $"Оригінал ({origLabel})";
        ColTranslation.Header = string.IsNullOrWhiteSpace(transLabel) ? "Переклад" : $"Переклад ({transLabel})";
    }

    private static ScrollViewer FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    private void ActivateTab(FileTabState tab)
    {
        if (_activeTab != null && _activeTab != tab)
        {
            SaveTabUIState(_activeTab);

            if (_activeTab.StatsDirty)
                _ = CalculateOriginalStatsAsync(_activeTab);
        }

        _activeTab = tab;

        using (SuppressGridSelectionSync())
        {
            DataGridMain.ItemsSource = tab.DataRows;
            UpdateColumnHeaders(tab.OrigLangLabel, tab.TransLangLabel);
            DataGridMain.SelectionMode = DataGridSelectionMode.Extended;
            DataContext = tab.SearchManager;
            SearchResultsList.ItemsSource = tab.SearchManager.SearchResults;
            SearchComboBox.Text = tab.SearchManager.SearchQuery;
            SearchModeToggle.IsChecked = tab.SearchManager.IsIdMode;
            SearchModeToggle.Content = tab.SearchManager.IsIdMode ? "ID" : "Текст";

            SearchResultsPanel.Visibility = tab.SearchPanelVisible
                ? Visibility.Visible
                : Visibility.Collapsed;

            SearchStatusLabel.Content = tab.SearchStatusText;

            LoadTranslationIntoEditor(null);
            EditTextBox.IsEnabled = false;

            bool hasFile = !string.IsNullOrEmpty(tab.FilePath);
            bool hasRows = tab.DataRows.Count > 0;

            ControlsMode(hasFile && hasRows);
            UpdateUndoRedoMenuItems();
            UpdateGridFilterUI();

            if (hasFile && hasRows)
            {
                MainContentGrid.Visibility = Visibility.Visible;
                WelcomePanel.Visibility = Visibility.Collapsed;
                ApplyGridFilterToTab(tab, tab.ActiveGridFilter);
                SaveOverwriteMenuItem.IsEnabled = tab.HasUnsavedChanges && hasRows;
                SaveAsMenuItem.IsEnabled = hasRows;
                OperationsMenuItem.IsEnabled = hasRows;
                ManageLanguagesMenuItem.IsEnabled = hasRows && tab.SupportsLanguageManager;
                StatsPanel.Visibility = Visibility.Visible;
                UpdateStatsUI();
            }
            else
            {
                MainContentGrid.Visibility = Visibility.Collapsed;
                WelcomePanel.Visibility = Visibility.Visible;
            }

            Title = tab.WindowTitle(ToolName);
        }

        _pendingSelectionRestoreTab = tab;

        if (tab.SelectedRowIndex >= 0 && tab.SelectedRowIndex < tab.DataRows.Count)
        {
            var item = tab.DataRows[tab.SelectedRowIndex];
            DataGridMain.SelectedItem = item;
            LoadTranslationIntoEditor(item);
            EditTextBox.IsEnabled = true;
        }
        else
        {
            DataGridMain.SelectedItem = null;
            DataGridMain.UnselectAllCells();
        }

        if (DataGridScrollViewer != null)
        {
            DataGridScrollViewer.ScrollToVerticalOffset(tab.ScrollOffset);
            DataGridScrollViewer.UpdateLayout();
        }
        else if (tab.SelectedRowIndex >= 0 && tab.SelectedRowIndex < tab.DataRows.Count)
        {
            DataGridMain.ScrollIntoView(tab.DataRows[tab.SelectedRowIndex]);
        }

        _fileTreeManager?.SetActiveFile(tab.FilePath);

        Dispatcher.BeginInvoke(() =>
        {
            if (_activeTab != tab)
                return;

            var (openFiles, totalRows) = GetOpenFilesStats();

            if (!string.IsNullOrEmpty(tab.FilePath))
                _discord.SetFileState(tab.FilePath, tab.DataRows.Count, totalRows, openFiles, tab.HasUnsavedChanges);
            else
                _discord.SetIdle();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void SaveTabUIState(FileTabState tab)
    {
        if (tab == null) return;

        FlushEditTextBox();

        if (DataGridMain.SelectedItem is DataGridItem editingItem && tab.EditingTranslationBefore != null)
        {
            string currentTranslation = editingItem.Translation ?? "";
            if (currentTranslation != tab.EditingTranslationBefore)
            {
                tab.UndoRedo.Push(new UndoRedoAction
                {
                    Description = "Редагування рядка",
                    Changes = new List<TranslationSnapshot>
                {
                    new(editingItem.Index, tab.EditingTranslationBefore, currentTranslation)
                }
                });

                UpdateUndoRedoMenuItems();
                tab.HasUnsavedChanges = true;
                tab.StatsDirty = true;
                SaveOverwriteMenuItem.IsEnabled = true;
            }
            tab.EditingTranslationBefore = null;
        }

        if (DataGridMain.SelectedItem is DataGridItem sel)
            tab.SelectedRowIndex = sel.Index;
        else
            tab.SelectedRowIndex = -1;

        if (DataGridScrollViewer != null)
            tab.ScrollOffset = DataGridScrollViewer.VerticalOffset;

        tab.SearchPanelVisible = SearchResultsPanel.Visibility == Visibility.Visible;
        tab.SearchStatusText = SearchStatusLabel.Content?.ToString() ?? "";
    }

    private void FileTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTabSwitch) return;

        if (FileTabs.SelectedItem == _plusTabItem)
        {
            if (_ignoreNextPlusTabSelection)
            {
                _ignoreNextPlusTabSelection = false;
                return;
            }

            using (SuppressTabSwitch())
            {
                if (_activeTab != null)
                {
                    int prevIndex = _tabs.IndexOf(_activeTab);
                    if (prevIndex >= 0)
                        FileTabs.SelectedIndex = prevIndex;
                }
            }
            return;
        }

        if (FileTabs.SelectedItem is TabItem tabItem && tabItem.Tag is FileTabState tab)
        {
            ActivateTab(tab);
        }
    }

    private void FileTabs_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scroller = FindVisualChild<ScrollViewer>(FileTabs);
        if (scroller == null) return;
        scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private void ScrollTabIntoView(int tabIndex)
    {
        var scroller = FindVisualChild<ScrollViewer>(FileTabs);
        if (scroller == null) return;

        var tabItem = FileTabs.ItemContainerGenerator.ContainerFromIndex(tabIndex) as TabItem;
        if (tabItem == null) return;

        var transform = tabItem.TransformToAncestor(scroller);
        var tabLeft = transform.Transform(new Point(0, 0)).X + scroller.HorizontalOffset;
        var tabRight = tabLeft + tabItem.ActualWidth;

        if (tabLeft < scroller.HorizontalOffset)
            scroller.ScrollToHorizontalOffset(tabLeft);
        else if (tabRight > scroller.HorizontalOffset + scroller.ViewportWidth)
            scroller.ScrollToHorizontalOffset(tabRight - scroller.ViewportWidth);
    }

    private async Task<bool> CloseTab(FileTabState tab)
    {
        if (tab == null)
            return true;

        if (tab.HasUnsavedChanges)
        {
            SelectAndActivateTab(tab);

            string fileName = string.IsNullOrEmpty(tab.FilePath)
                ? "новий файл"
                : Path.GetFileName(tab.FilePath);

            var result = MessageBox.Show(this, $"Є незбережені зміни у {fileName}.\nЗберегти перед закриттям?", "Підтвердження", MessageBoxButton.YesNoCancel);

            if (result == MessageBoxResult.Cancel)
                return false;

            if (result == MessageBoxResult.Yes)
            {
                await SaveTabForCloseAsync(tab);

                if (tab.HasUnsavedChanges)
                    return false;
            }
        }

        RemoveTab(tab);
        return true;
    }

    private void RemoveTab(FileTabState tab)
    {
        int idx = _tabs.IndexOf(tab);
        if (idx < 0) return;

        bool wasActive = tab == _activeTab;

        if (wasActive)
        {
            DataGridMain.ItemsSource = null;
            DataGridMain.SelectedItem = null;
            LoadTranslationIntoEditor(null);
            SearchResultsList.ItemsSource = null;
            DataContext = null;
            _activeTab = null;
        }

        if (_statsCts != null)
        {
            var ctsToCancel = _statsCts;
            _statsCts = null;
            ctsToCancel.Cancel();
            ctsToCancel.Dispose();
        }

        if (_fileTreeManager != null && !string.IsNullOrEmpty(tab.FilePath))
            _fileTreeManager.RemoveFile(tab.FilePath);

        using (SuppressTabSwitch())
        {
            FileTabs.Items.RemoveAt(idx);
            _tabs.RemoveAt(idx);
        }

        tab.Dispose();

        if (_tabs.Count == 0)
        {
            CreateNewTab();
        }
        else if (wasActive)
        {
            int newIdx = Math.Min(idx, _tabs.Count - 1);
            SelectAndActivateTab(_tabs[newIdx]);
        }
        else
        {
            int activeIdx = _tabs.IndexOf(_activeTab);
            using (SuppressTabSwitch())
                FileTabs.SelectedIndex = activeIdx;
        }
    }

    private FileTabState FindTabByPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;
        return _tabs.FirstOrDefault(t =>
            string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
    }

    private bool SwitchToExistingTab(string filePath)
    {
        var existing = FindTabByPath(filePath);

        if (existing == null)
            return false;

        int idx = _tabs.IndexOf(existing);

        if (existing == _activeTab)
        {
            ScrollTabIntoView(idx);
            return true;
        }

        using (SuppressTabSwitch())
            FileTabs.SelectedIndex = idx;

        ScrollTabIntoView(idx);
        ActivateTab(existing);
        return true;
    }

    private async void TabCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is TabItem tabItem && tabItem.Tag is FileTabState tab)
        {
            e.Handled = true;
            await CloseTab(tab);
        }
    }

    private async void CloseCurrentTab_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab != null)
            await CloseTab(_activeTab);
    }

    private FileTabState FindOrCreateEmptyTab(bool activate = true)
    {
        var emptyTab = _tabs.FirstOrDefault(t =>
            string.IsNullOrEmpty(t.FilePath) && !t.HasUnsavedChanges);

        if (emptyTab != null)
        {
            if (activate)
                SelectAndActivateTab(emptyTab);

            return emptyTab;
        }

        return CreateNewTab(activate: activate);
    }

    private void InitTabScrollButtons()
    {
        FileTabs.Loaded += (_, __) =>
        {
            _tabScrollLeftBtn = FindVisualChildByName<Grid>(FileTabs, "TabScrollLeftBtn");
            _tabScrollRightBtn = FindVisualChildByName<Grid>(FileTabs, "TabScrollRightBtn");
            UpdateTabScrollArrows();
        };
    }

    private void TabStripScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateTabScrollArrows();
    }

    private void TabPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTabScrollArrows();
    }

    private void UpdateTabScrollArrows()
    {
        var scroller = FindVisualChild<ScrollViewer>(FileTabs);
        if (scroller == null) return;

        _tabScrollLeftBtn ??= FindVisualChildByName<Grid>(FileTabs, "TabScrollLeftBtn");
        _tabScrollRightBtn ??= FindVisualChildByName<Grid>(FileTabs, "TabScrollRightBtn");

        bool canScrollLeft = scroller.HorizontalOffset > 0.5;
        bool canScrollRight = scroller.HorizontalOffset < scroller.ScrollableWidth - 0.5;

        if (_tabScrollLeftBtn != null)
            _tabScrollLeftBtn.Visibility = canScrollLeft ? Visibility.Visible : Visibility.Collapsed;
        if (_tabScrollRightBtn != null)
            _tabScrollRightBtn.Visibility = canScrollRight ? Visibility.Visible : Visibility.Collapsed;
    }

    private static T FindVisualChildByName<T>(DependencyObject parent, string name)
        where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T fe && fe.Name == name) return fe;
            var found = FindVisualChildByName<T>(child, name);
            if (found != null) return found;
        }
        return null;
    }
    #endregion

    #region File Tree
    private void InitFileTree()
    {
        _fileTreeManager = new FileTreeManager(FileTreeView);
        _fileTreeManager.FileNodeClicked += OnFileTreeNodeClicked;

        _fileTreeManager.CloseFolder += async folderPath =>
        {
            var toClose = _tabs
                .Where(t => Path.GetDirectoryName(t.FilePath)
                                .Equals(folderPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var tab in toClose)
                await CloseTab(tab);
        };

        _fileTreeManager.CloseFolderKeepOnly += async folderPath =>
        {
            var toClose = _tabs
                .Where(t => !Path.GetDirectoryName(t.FilePath)
                                 .Equals(folderPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var tab in toClose)
                await CloseTab(tab);
        };

        _fileTreeManager.CloseFile += async filePath =>
        {
            var tab = _tabs.FirstOrDefault(t => t.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            if (tab != null)
                await CloseTab(tab);
        };

        _fileTreeManager.CloseAll += async () =>
        {
            foreach (var tab in _tabs.ToList())
                await CloseTab(tab);
        };

        foreach (var tab in _tabs)
        {
            if (!string.IsNullOrEmpty(tab.FilePath))
                _fileTreeManager.AddFile(tab.FilePath);
        }

        if (_activeTab != null)
            _fileTreeManager.SetActiveFile(_activeTab.FilePath);
    }

    private void OnFileTreeNodeClicked(string filePath)
    {
        if (_suppressTabSwitch) return;
        if (_activeTab?.FilePath == filePath) return;

        if (_loadSemaphore.CurrentCount == 0) return;

        SwitchToExistingTab(filePath);
    }

    private void ToggleFileTree_Click(object sender, RoutedEventArgs e)
    {
        _fileTreeVisible = !_fileTreeVisible;

        if (_fileTreeVisible)
        {
            FileTreeColumn.Width = new GridLength(_fileTreeLastWidth);
            FileTreeColumn.MinWidth = 80;
            FileTreePanelGrid.Visibility = Visibility.Visible;
        }
        else
        {
            _fileTreeLastWidth = FileTreeColumn.ActualWidth > 0
                ? FileTreeColumn.ActualWidth
                : _fileTreeLastWidth;

            FileTreeColumn.Width = new GridLength(0);
            FileTreeColumn.MinWidth = 0;
            FileTreePanelGrid.Visibility = Visibility.Collapsed;
        }
    }
    #endregion

    #region Opening Files
    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        SafeExecute(async () =>
        {
            var ofd = new OpenFileDialog
            {
                Filter = "JSON файли|*.json|Всі файли|*.*",
                Title = "Відкрити JSON файли локалізації",
                Multiselect = true
            };

            if (ofd.ShowDialog() != true) return;

            var toOpen = new List<string>();
            foreach (var f in ofd.FileNames.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (!SwitchToExistingTab(f))
                    toOpen.Add(f);
            }

            if (toOpen.Count == 0) return;

            FileTabState firstTarget = string.IsNullOrEmpty(_activeTab?.FilePath)
                ? _activeTab
                : FindOrCreateEmptyTab(activate: false);

            var versionSession = new FolderVersionSession { IsMultiFile = toOpen.Count > 1 };

            await LoadFileIntoTab(firstTarget, toOpen[0], versionSession, isBatchLoad: true);
            _recentFilesManager.Add(toOpen[0]);

            var lastTarget = firstTarget;

            for (int i = 1; i < toOpen.Count; i++)
            {
                if (versionSession.Cancelled) break;
                lastTarget = CreateNewTab(activate: false);
                await LoadFileIntoTab(lastTarget, toOpen[i], versionSession, isBatchLoad: true);
                _recentFilesManager.Add(toOpen[i]);
            }

            _fileTreeManager?.FinalizeBatchAdd();

            if (!string.IsNullOrEmpty(lastTarget.FilePath))
            {
                int idx = _tabs.IndexOf(lastTarget);
                using (SuppressTabSwitch())
                    FileTabs.SelectedIndex = idx;

                ActivateTab(lastTarget);
            }

            UpdateRecentFilesMenu();
        }, "Помилка відкриття файлу");
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        SafeExecute(async () =>
        {
            var ofd = new OpenFolderDialog
            {
                Title = "Вибрати теку з JSON файлами",
                Multiselect = false
            };

            if (ofd.ShowDialog() != true) return;

            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json" };
            var files = Directory.GetFiles(ofd.FolderName, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f)
                .ToList();

            if (files.Count == 0)
            {
                MessageBox.Show(this, "У вибраній теці не знайдено JSON файлів.", "Нічого не знайдено", MessageBoxButton.OK);
                return;
            }

            var filesToOpen = files.Where(f => FindTabByPath(f) == null).ToList();

            if (filesToOpen.Count == 0) return;

            if (filesToOpen.Count > 20)
            {
                var confirm = MessageBox.Show(this, $"Знайдено {filesToOpen.Count} {PluralizationHelper.GetFilesWord(filesToOpen.Count)}. Відкрити всі?", "Підтвердження", MessageBoxButton.YesNo);
                if (confirm != MessageBoxResult.Yes) return;
            }

            _recentFoldersManager.Add(ofd.FolderName);

            var versionSession = new FolderVersionSession { IsMultiFile = filesToOpen.Count > 1 };

            FileTabState firstTarget = string.IsNullOrEmpty(_activeTab?.FilePath)
                ? _activeTab
                : FindOrCreateEmptyTab(activate: false);

            await LoadFileIntoTab(firstTarget, filesToOpen[0], versionSession, isBatchLoad: true);

            var lastTarget = firstTarget;

            for (int i = 1; i < filesToOpen.Count; i++)
            {
                if (versionSession.Cancelled) break;
                lastTarget = CreateNewTab(activate: false);
                await LoadFileIntoTab(lastTarget, filesToOpen[i], versionSession, isBatchLoad: true);
            }

            _fileTreeManager?.FinalizeBatchAdd();

            if (!string.IsNullOrEmpty(lastTarget.FilePath))
            {
                int idx = _tabs.IndexOf(lastTarget);
                using (SuppressTabSwitch())
                    FileTabs.SelectedIndex = idx;

                ActivateTab(lastTarget);
            }

            UpdateRecentFoldersMenu();
        }, "Помилка відкриття теки");
    }

    private void ShowLoadWelcomeScreen()
    {
        WelcomePanel.Visibility = Visibility.Visible;
        MainContentGrid.Visibility = Visibility.Collapsed;
    }

    private void FailLoadAndShowError(string message, string title = "Помилка")
    {
        CloseFromState();
        MessageBox.Show(this, message, title, MessageBoxButton.OK);
        ShowLoadWelcomeScreen();
    }

    private void FinalizeSuccessfulLoad(FileTabState tab, string filePath, bool isBatchLoad)
    {
        tab.FilePath = filePath;
        tab.HasUnsavedChanges = false;
        _fileTreeManager?.AddFile(filePath, deferSort: isBatchLoad);

        if (!isBatchLoad)
            _fileTreeManager?.SetActiveFile(filePath);

        DataGridMain.ItemsSource = tab.DataRows;
        DataContext = tab.SearchManager;
        SearchResultsList.ItemsSource = tab.SearchManager.SearchResults;
        DataGridMain.SelectedItem = null;
        DataGridMain.UnselectAllCells();

        if (tab.DataRows.Count > 0 && DataGridScrollViewer != null)
        {
            DataGridScrollViewer.ScrollToVerticalOffset(0);
            DataGridScrollViewer.UpdateLayout();
        }

        ControlsMode(true);
        MainContentGrid.Visibility = Visibility.Visible;
        WelcomePanel.Visibility = Visibility.Collapsed;
        CloseFromState();
        Title = tab.WindowTitle(ToolName);
        RefreshGlossaryHighlights(tab);
        RefreshTranslationMemoryHighlights(tab);

        if (isBatchLoad)
            return;

        int openFiles = _tabs.Count(t => !string.IsNullOrEmpty(t.FilePath));
        int totalRows = _tabs.Sum(t => t.DataRows.Count);
        _discord.SetFileState(tab.FilePath, tab.DataRows.Count, totalRows, openFiles, false);
    }

    internal async Task LoadFileIntoTab(FileTabState tab, string filePath, FolderVersionSession folderSession = null, bool isBatchLoad = false)
    {
        await _loadSemaphore.WaitAsync();

        try
        {
            int idx = _tabs.IndexOf(tab);

            if (idx >= 0 && idx < FileTabs.Items.Count)
            {
                if (_activeTab != null && _activeTab != tab)
                    SaveTabUIState(_activeTab);

                if (!isBatchLoad)
                {
                    using (SuppressTabSwitch())
                        FileTabs.SelectedIndex = idx;
                }
                _activeTab = tab;
            }

            tab.Reset();
            ControlsMode(false);
            SearchResultsPanel.Visibility = Visibility.Collapsed;
            SearchStatusLabel.Content = "";
            SearchComboBox.Text = tab.SearchManager.SearchQuery;
            SearchResultsList.ItemsSource = null;
            SearchModeToggle.IsChecked = tab.SearchManager.IsIdMode;
            SearchModeToggle.Content = tab.SearchManager.IsIdMode ? "ID" : "Текст";
            WelcomePanel.Visibility = Visibility.Collapsed;
            MainContentGrid.Visibility = Visibility.Collapsed;

            try
            {
                StatusMessage("Відкриття файлу...");

                if (!filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    FailLoadAndShowError($"Непідтримуваний тип файлу: {Path.GetExtension(filePath)}\nULT підтримує лише JSON файли.");
                    return;
                }

                JToken jsonData;
                JsonFormatType format;
                try
                {
                    jsonData = await Task.Run(() => JToken.Parse(File.ReadAllText(filePath)));
                    format = JsonFormatDetector.DetectFormat(jsonData);
                }
                catch (Exception ex)
                {
                    FailLoadAndShowError(ex.Message, "Не вдалося прочитати файл JSON");
                    return;
                }

                if (format == JsonFormatType.Unknown)
                {
                    FailLoadAndShowError($"Файл: {Path.GetFileName(filePath)}\nНевідома структура JSON.\nПричини:\n  — Ви відкриваєте TextAsset замість MonoBehaviour.\n  — JSON файл пошкоджено.\n  — Ваш формат поки не підтримується.\nПередайте автору інструменту файл на аналіз.");
                    return;
                }

                tab.JsonData = jsonData;
                tab.CurrentFormat = format;
                tab.FileType = ".json";
                tab.FilePath = filePath;
                var loadResult = await LoadJsonIntoTab(tab, filePath, folderSession);

                if (loadResult != JsonLoadResult.Success)
                {
                    if (loadResult == JsonLoadResult.Failed)
                    {
                        FailLoadAndShowError($"Непідтримуваний тип файлу: {Path.GetExtension(filePath)}");
                    }
                    else
                    {
                        CloseFromState();
                        ShowLoadWelcomeScreen();
                    }
                    return;
                }

                FinalizeSuccessfulLoad(tab, filePath, isBatchLoad);
            }
            catch (Exception ex)
            {
                FailLoadAndShowError(ex.Message, "Не вдалося завантажити файл");
            }
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    private enum JsonLoadResult { Success, UserCancelled, Failed }

    private async Task<JsonLoadResult> LoadJsonIntoTab(FileTabState tab, string filePath, FolderVersionSession folderSession = null)
    {
        var format = tab.CurrentFormat;
        var jsonData = tab.JsonData;

        if (JsonFormatDetector.IsArrayFormat(format) && JsonImportHelper.NeedsLanguageSelection(format))
        {
            var langsArray = JsonImportHelper.GetLanguagesArray(jsonData, format);

            if (langsArray == null || !langsArray.Any())
            {
                MessageBox.Show(this, $"Файл: {Path.GetFileName(filePath)}\nНе вдалося визначити мови у форматі JSON.", "Помилка", MessageBoxButton.OK);
                return JsonLoadResult.Failed;
            }

            var rawLabels = langsArray.Select((t, i) =>
                t is JObject obj
                    ? (obj["Name"]?.ToString() ?? obj["Code"]?.ToString() ?? obj.Properties().FirstOrDefault()?.Value?.ToString() ?? $"Мова {i + 1}")
                    : (t?.ToString() ?? $"Мова {i + 1}")
            ).ToList();

            var names = rawLabels.Select((label, i) => $"{i}: {label}").ToList();
            bool isBatch = folderSession != null && folderSession.IsMultiFile;

            if (isBatch && folderSession.IsLanguageSetForFormat(format))
            {
                tab.OrigIndex = folderSession.GetOrigIndexForFormat(format);
                tab.LangIndex = folderSession.GetLangIndexForFormat(format);
            }
            else
            {
                var dialog = new IndexSelectionWindow("Індекс для стовпця Оригінал:", "Індекс для стовпця Переклад:", names, isBatch)
                {
                    Owner = IsLoaded ? this : null,
                    WindowStartupLocation = IsLoaded ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
                };

                if (dialog.ShowDialog() != true)
                {
                    if (folderSession != null)
                        folderSession.Cancelled = true;

                    return JsonLoadResult.UserCancelled;
                }

                tab.OrigIndex = dialog.SelectedOrigIndex;
                tab.LangIndex = dialog.SelectedTransIndex;

                if (isBatch && dialog.ApplyToAll)
                    folderSession.SetLanguageForFormat(format, tab.OrigIndex, tab.LangIndex);
            }

            tab.OrigLangLabel = tab.OrigIndex >= 0 && tab.OrigIndex < rawLabels.Count ? rawLabels[tab.OrigIndex] : null;
            tab.TransLangLabel = tab.LangIndex >= 0 && tab.LangIndex < rawLabels.Count ? rawLabels[tab.LangIndex] : null;
        }
        else
        {
            tab.OrigLangLabel = null;
            tab.TransLangLabel = null;
        }

        BaseFormatHandler handler;
        if (JsonFormatDetector.IsArrayFormat(format))
        {
            var h = new ArrayFormatHandler();
            h.SetFormatType(format);
            h.SetJsonData(jsonData);
            h.SetLanguageIndices(tab.OrigIndex, tab.LangIndex);
            handler = h;
        }
        else
        {
            var h = new NonArrayFormatHandler();
            h.SetFormatType(format);
            h.SetJsonData(jsonData);
            handler = h;
        }

        StatusMessage("Завантаження тексту...");
        var loadedRows = await Task.Run(handler.LoadTerms);
        tab.FormatHandler = handler;
        tab.DataRows.Clear();
        int idx = 0;

        foreach (var row in loadedRows)
        {
            row.Index = idx++;
            row.OriginalStringData = new List<string> { row.ID ?? "", row.Text ?? "" };
            tab.DataRows.Add(row);
        }

        bool supportsLangManager =
            format == JsonFormatType.TermsArray ||
            format == JsonFormatType.ItemsArray ||
            format == JsonFormatType.LinesArray ||
            format == JsonFormatType.LanguageKeysArray ||
            format == JsonFormatType.NamedDataArray ||
            format == JsonFormatType.GameStringsArray ||
            format == JsonFormatType.LocalizationData;

        tab.SupportsLanguageManager = supportsLangManager;
        tab.InitCollectionView();
        AutoLoadStatusesIfExists(tab);
        await CalculateOriginalStatsAsync(tab);
        OperationsMenuItem.IsEnabled = tab.DataRows.Count > 0;
        ManageLanguagesMenuItem.IsEnabled = tab.DataRows.Count > 0 && supportsLangManager;
        UpdateColumnHeaders(tab.OrigLangLabel, tab.TransLangLabel);
        return JsonLoadResult.Success;
    }

    private void AutoLoadStatusesIfExists(FileTabState tab)
    {
        if (tab == null || tab.DataRows.Count == 0)
            return;

        string statusPath = RowStatusManager.GetStatusFilePath(tab.FilePath);

        if (!File.Exists(statusPath))
            return;

        try
        {
            RowStatusManager.LoadFromFile(statusPath, tab.DataRows, useIndexKeys: RowStatusManager.ShouldUseIndexKeys(tab.CurrentFormat));
            tab.HadStatusFileOnLoad = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{statusPath}\n{ex}", "Помилка завантаження статусів", MessageBoxButton.OK);
        }
    }

    internal class FolderVersionSession
    {
        public bool Cancelled { get; set; } = false;
        public bool IsMultiFile { get; set; } = false;

        private readonly Dictionary<string, (int orig, int lang)> _indexByFormat = new();

        public bool IsLanguageSetForFormat(JsonFormatType format)
            => _indexByFormat.ContainsKey(format.ToString());

        public int GetOrigIndexForFormat(JsonFormatType format)
            => _indexByFormat.TryGetValue(format.ToString(), out var v) ? v.orig : 0;

        public int GetLangIndexForFormat(JsonFormatType format)
            => _indexByFormat.TryGetValue(format.ToString(), out var v) ? v.lang : 1;

        public void SetLanguageForFormat(JsonFormatType format, int orig, int lang)
            => _indexByFormat[format.ToString()] = (orig, lang);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        SafeExecute(async () => await SaveAsync(_activeTab), "Помилка збереження");
    }

    private async Task SaveAsync(FileTabState tab = null)
    {
        tab ??= _activeTab;
        if (tab?.FormatHandler == null) return;

        var sfd = new SaveFileDialog
        {
            Title = "Збереження файлу",
            FileName = Path.GetFileNameWithoutExtension(tab.FilePath) + "_N",
            Filter = "JSON файли|*.json|Всі файли|*.*"
        };

        if (sfd.ShowDialog() == true)
        {
            StatusMessage("Збереження...");
            try
            {
                await SaveJsonTab(tab, sfd.FileName);
                tab.HasUnsavedChanges = false;
                tab.FilePath = sfd.FileName;
                Title = tab.WindowTitle(ToolName);
                CloseFromState();
                MessageBox.Show(this, "Файл збережено.", "Готово", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                CloseFromState();
                MessageBox.Show(this, $"{ex.Message}", "Не вдалося зберегти файл", MessageBoxButton.OK);
            }
        }
    }

    private void SaveOverwrite_Click(object sender, RoutedEventArgs e)
    {
        SafeExecute(async () => await SaveOverwriteAsync(_activeTab), "Помилка збереження");
    }

    private async Task SaveOverwriteAsync(FileTabState tab = null)
    {
        tab ??= _activeTab;

        if (tab?.FormatHandler == null || string.IsNullOrEmpty(tab.FilePath))
            return;

        StatusMessage("Збереження...");
        try
        {
            await SaveJsonTab(tab, tab.FilePath);
            tab.HasUnsavedChanges = false;
            Title = tab.WindowTitle(ToolName);
            SaveOverwriteMenuItem.IsEnabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $":{ex.Message}", "Не вдалося зберегти файл", MessageBoxButton.OK);
        }
        finally
        {
            CloseFromState();
        }
    }

    private static void RecordTranslationsForTab(FileTabState tab)
    {
        foreach (var row in tab.DataRows)
        {
            if (!row.IsModified)
                continue;

            if (row.Translation == row.Text)
                continue;

            if (!TranslationMemoryManager.IsMeaningfulTranslation(row.Translation))
                continue;

            TranslationMemoryManager.Instance.RecordTranslation(row.Text, row.Translation);
        }
    }

    private async Task SaveJsonTab(FileTabState tab, string outputPath)
    {
        FlushEditTextBox();

        var snapshot = tab.DataRows.ToList();

        RecordTranslationsForTab(tab);

        await Task.Run(() =>
        {
            tab.FormatHandler.SetOutputPath(outputPath);
            tab.FormatHandler.SaveTerms(snapshot);

            var json = tab.FormatHandler.GetJsonData();
            File.WriteAllText(outputPath, json.ToString(Newtonsoft.Json.Formatting.Indented));
        });

        if (SettingsManager.CreateStatusFiles || tab.HadStatusFileOnLoad)
            RowStatusManager.Save(outputPath, tab.DataRows, useIndexKeys: RowStatusManager.ShouldUseIndexKeys(tab.CurrentFormat));

        await Task.Run(() => TranslationMemoryManager.Instance.SaveDirty());

        foreach (var t in _tabs)
            RefreshTranslationMemoryHighlights(t);
    }

    private async void SaveAll_Click(object sender, RoutedEventArgs e)
    {
        var dirtyTabs = _tabs.Where(t => t.HasUnsavedChanges && t.FormatHandler != null).ToList();

        if (dirtyTabs.Count == 0)
        {
            MessageBox.Show(this, "Немає файлів із незбереженими змінами.", "Зберегти все", MessageBoxButton.OK);
            return;
        }

        const int maxNamesShown = 5;
        var names = string.Join("\n", dirtyTabs.Take(maxNamesShown).Select(t => " " + Path.GetFileName(t.FilePath)));
        if (dirtyTabs.Count > maxNamesShown)
            names += $"\n  … і ще {dirtyTabs.Count - maxNamesShown} {PluralizationHelper.GetFilesWord(dirtyTabs.Count - maxNamesShown)}";

        var confirm = MessageBox.Show(this, $"Буде перезаписано {dirtyTabs.Count} {PluralizationHelper.GetFilesWord(dirtyTabs.Count)}:\n{names}\n\nПродовжити?", "Зберегти все", MessageBoxButton.YesNo);
        if (confirm != MessageBoxResult.Yes)
            return;

        int saved = 0, failed = 0;
        foreach (var tab in dirtyTabs)
        {
            try
            {
                StatusMessage($"Збереження {Path.GetFileName(tab.FilePath)}...");
                await SaveJsonTab(tab, tab.FilePath);
                tab.HasUnsavedChanges = false;
                saved++;
            }
            catch
            {
                failed++;
            }
        }

        CloseFromState();
        Title = _activeTab?.WindowTitle(ToolName) ?? ToolName;

        string msg = $"Збережено {saved} {PluralizationHelper.GetFilesWord(saved)}.";

        if (failed > 0)
            msg += $"\nПомилка при збереженні {failed} {PluralizationHelper.GetFilesWord(failed)}.";

        MessageBox.Show(this, msg, "Зберегти все", MessageBoxButton.OK);
    }

    private void SaveAllAs_Click(object sender, RoutedEventArgs e)
    {
        SafeExecute(async () =>
        {
            var tabsToSave = _tabs.Where(t => t.FormatHandler != null && !string.IsNullOrEmpty(t.FilePath)).ToList();

            if (tabsToSave.Count == 0)
            {
                MessageBox.Show(this, "Немає відкритих файлів для збереження.", "Зберегти все як", MessageBoxButton.OK);
                return;
            }

            int saved = 0, skipped = 0, failed = 0;

            foreach (var tab in tabsToSave)
            {
                var sfd = new SaveFileDialog
                {
                    Title = $"Зберегти як — {Path.GetFileName(tab.FilePath)}",
                    FileName = Path.GetFileNameWithoutExtension(tab.FilePath) + "_N",
                    Filter = "JSON файли|*.json|Всі файли|*.*"
                };

                if (sfd.ShowDialog() != true) { skipped++; continue; }

                try
                {
                    StatusMessage($"Збереження {Path.GetFileName(sfd.FileName)}...");
                    await SaveJsonTab(tab, sfd.FileName);
                    tab.HasUnsavedChanges = false;
                    tab.FilePath = sfd.FileName;
                    saved++;
                }
                catch
                {
                    failed++;
                }
            }

            CloseFromState();
            Title = _activeTab?.WindowTitle(ToolName) ?? ToolName;

            string msg = $"Збережено {saved} {PluralizationHelper.GetFilesWord(saved)}.";
            if (skipped > 0) msg += $"\nПропущено {skipped} {PluralizationHelper.GetFilesWord(skipped)}.";
            if (failed > 0) msg += $"\nПомилка при збереженні {failed} {PluralizationHelper.GetFilesWord(failed)}.";
            MessageBox.Show(this, msg, "Зберегти все як", MessageBoxButton.OK);
        }, "Помилка збереження");
    }

    private async Task<bool> SaveTabForCloseAsync(FileTabState tab)
    {
        if (tab?.FormatHandler == null)
			return true;

        string filePath = tab.FilePath;

        if (string.IsNullOrEmpty(filePath))
        {
            var sfd = new SaveFileDialog
            {
                Title = "Збереження файлу",
                FileName = tab.Header,
                Filter = "JSON файли|*.json|Всі файли|*.*"
            };

            if (sfd.ShowDialog() != true)
                return false;

            filePath = sfd.FileName;
        }

        StatusMessage("Збереження...");

        try
        {
            await SaveJsonTab(tab, filePath);
            tab.HasUnsavedChanges = false;
            tab.FilePath = filePath;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{ex.Message}", "Не вдалося зберегти файл", MessageBoxButton.OK);
            return false;
        }
        finally
        {
            CloseFromState();
        }
    }
    #endregion

    #region DataGrid Editing
    private readonly struct GridSelectionSyncSuppressionScope : IDisposable
    {
        private readonly MainWindow _owner;

        public GridSelectionSyncSuppressionScope(MainWindow owner)
        {
            _owner = owner;
            _owner._suppressGridSelectionSync = true;
        }

        public void Dispose() => _owner._suppressGridSelectionSync = false;
    }

    private void CopyID_Click(object sender, RoutedEventArgs e)
    {
        var items = DataGridMain.SelectedItems.OfType<DataGridItem>().ToList();
        if (items.Count == 0) return;
        Clipboard.SetText(string.Join("\n", items.Select(i => i.ID ?? "")));
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        var items = DataGridMain.SelectedItems.OfType<DataGridItem>().ToList();
        if (items.Count == 0) return;
        Clipboard.SetText(string.Join("\n", items.Select(i => i.Text ?? "")));
    }

    private void CopyTranslation_Click(object sender, RoutedEventArgs e)
    {
        var items = DataGridMain.SelectedItems.OfType<DataGridItem>().ToList();
        if (items.Count == 0) return;
        Clipboard.SetText(string.Join("\n", items.Select(i => i.Translation ?? "")));
    }

    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressGridSelectionSync) return;

        SafeExecute(async () =>
        {
            if (_activeTab == null) return;

            if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is DataGridItem previousItem)
            {
                string currentTranslation = previousItem.Translation ?? "";

                if (_editingTranslationBefore != null && currentTranslation != _editingTranslationBefore)
                {
                    await CalculateOriginalStatsAsync(_activeTab);
                    MarkAsModified();

                    Tab_Undo.Push(new UndoRedoAction
                    {
                        Description = "Редагування рядка",
                        Changes = new List<TranslationSnapshot>
                    {
                        new(previousItem.Index, _editingTranslationBefore, currentTranslation)
                    }
                    });
                    UpdateUndoRedoMenuItems();
                }

                _editingTranslationBefore = null;
            }

            if (DataGridMain.SelectedItem is DataGridItem item)
            {
                LoadTranslationIntoEditor(item);
                EditTextBox.IsEnabled = true;
                _editingTranslationBefore = item.Translation;
                _activeTab.SelectedRowIndex = item.Index;
                UpdateSpellErrorPanel(item);
                UpdateCommonErrorsPanel(item);
            }
            else
            {
                LoadTranslationIntoEditor(null);
                EditTextBox.IsEnabled = false;
                _editingTranslationBefore = null;
                _activeTab.SelectedRowIndex = -1;
                UpdateSpellErrorPanel(null);
                UpdateCommonErrorsPanel(null);
            }
        }, "Помилка при зміні виділення");
    }

    private void RestoreOriginal_Click(object sender, RoutedEventArgs e)
    {
        SafeExecute(async () =>
        {
            if (_activeTab == null) return;

            var selectedItems = DataGridMain.SelectedItems.Cast<DataGridItem>().ToList();
            if (selectedItems.Count == 0) return;

            var changes = new List<TranslationSnapshot>();

            foreach (var item in selectedItems)
            {
                bool translationChanged = item.Translation != item.Text;
                bool statusChanged = item.Status != RowStatus.None;
                if (!translationChanged && !statusChanged) continue;

                string oldValue = item.Translation ?? "";
                string newValue = item.Text ?? "";

                if (translationChanged)
                {
                    changes.Add(new TranslationSnapshot(item.Index, oldValue, newValue));
                    item.Translation = newValue;
                    item.IsModified = false;

                    if (item.OriginalStringData != null && item.OriginalStringData.Count > 1)
                        item.OriginalStringData[1] = newValue;

                    SyncEditorIfSelected(item);
                }

                item.Status = RowStatus.None;
            }

            if (changes.Count > 0)
            {
                Tab_Undo.Push(new UndoRedoAction { Description = "Відновити оригінал (вибрані)", Changes = changes });
                UpdateUndoRedoMenuItems();
                await CalculateOriginalStatsAsync(_activeTab);
                MarkAsModified();
            }
        }, "Помилка при відновленні оригіналу");
    }

    private void RestoreAllOriginal_Click(object sender, RoutedEventArgs e)
    {
        SafeExecute(async () =>
        {
            if (_activeTab == null || Tab_Rows.Count == 0) return;

            int modifiedCount = Tab_Rows.Count(r => r.IsModified || r.Status != RowStatus.None);

            var result = MessageBox.Show(this, $"Ви дійсно хочете повернути оригінальний текст у всі рядки?\nБуде змінено {modifiedCount} {PluralizationHelper.GetCellsWord(modifiedCount)}.", "Підтвердження", MessageBoxButton.YesNo);

            if (result != MessageBoxResult.Yes) return;

            var changes = new List<TranslationSnapshot>();
            int restoredCount = 0;

            foreach (var item in Tab_Rows)
            {
                bool translationChanged = item.Translation != item.Text;
                bool statusChanged = item.Status != RowStatus.None;
                if (!translationChanged && !statusChanged) continue;

                if (translationChanged)
                {
                    string oldValue = item.Translation ?? "";
                    string newValue = item.Text ?? "";
                    changes.Add(new TranslationSnapshot(item.Index, oldValue, newValue));
                    item.Translation = newValue;
                    item.IsModified = false;
                    SyncEditorIfSelected(item);
                }

                item.Status = RowStatus.None;
                restoredCount++;
            }

            if (restoredCount > 0)
            {
                Tab_Undo.Push(new UndoRedoAction { Description = "Відновити оригінал (всі)", Changes = changes });
                UpdateUndoRedoMenuItems();
                await CalculateOriginalStatsAsync(_activeTab);
                MarkAsModified();
                DataGridMain.Items.Refresh();
                MessageBox.Show(this, $"Відновлено оригінальний текст у {PluralizationHelper.GetCellsWordReplace(restoredCount)}.", "Готово", MessageBoxButton.OK);
            }
        }, "Помилка при відновленні оригіналу");
    }

    private void MarkAsModified()
    {
        if (_activeTab == null)
            return;

        _activeTab.HasUnsavedChanges = true;
        SaveOverwriteMenuItem.IsEnabled = true;
        Title = _activeTab.WindowTitle(ToolName);
        _activeTab.StatsDirty = true;
    }

    private void CheckIfHasChanges()
    {
        if (_activeTab == null)
            return;

        if (Tab_Rows.Any(r => r.IsModified || r.IsNew))
            _activeTab.HasUnsavedChanges = true;

        SaveOverwriteMenuItem.IsEnabled = _activeTab.HasUnsavedChanges && Tab_Rows.Count > 0;
        Title = _activeTab.WindowTitle(ToolName);
    }

    private void PasteClipboardToSelectedRows()
    {
        if (_activeTab == null || DataGridMain.SelectedItems.Count == 0)
            return;

        string clipText = Clipboard.GetText();
        if (string.IsNullOrEmpty(clipText))
            return;

        var selectedItems = DataGridMain.SelectedItems
            .OfType<DataGridItem>()
            .OrderBy(i => i.Index)
            .ToList();

        if (selectedItems.Count == 0)
            return;

        var clipLines = clipText.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        bool multiPaste = clipLines.Count == selectedItems.Count && clipLines.Count > 1;

        _editingTranslationBefore = null;

        var snapshots = new List<TranslationSnapshot>();

        for (int i = 0; i < selectedItems.Count; i++)
        {
            var item = selectedItems[i];
            string newValue = multiPaste
                ? clipLines[i]
                : BaseFormatHandler.ConvertNewlinesToMarkers(clipText);

            if (item.Translation == newValue)
                continue;

            snapshots.Add(new TranslationSnapshot(item.Index, item.Translation, newValue));
            item.Translation = newValue;
        }

        if (snapshots.Count == 0)
            return;

        if (DataGridMain.SelectedItem is DataGridItem currentItem)
            SyncEditorIfSelected(currentItem);

        Tab_Undo.Push(new UndoRedoAction
        {
            Description = snapshots.Count == 1
                ? "Вставлено переклад"
                : $"Вставлено переклад у {snapshots.Count} рядків",
            Changes = snapshots
        });

        Tab_HasChanges = true;
        SaveOverwriteMenuItem.IsEnabled = true;
        UpdateUndoRedoMenuItems();
        _ = CalculateOriginalStatsAsync(_activeTab);
    }

    private void DataGridContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        int count = DataGridMain.SelectedItems.Count;
        PasteToGridMenuItem.Header = count > 1 ? "Вставити у комірки" : "Вставити";
        PasteToGridMenuItem.IsEnabled = Clipboard.ContainsText();
        AddToGlossaryMenuItem.Visibility = Visibility.Collapsed;
        GlossarySuggestionsMenuItem.Items.Clear();
        GlossarySuggestionsMenuItem.Visibility = Visibility.Collapsed;
        TranslationMemorySuggestionsMenuItem.Items.Clear();
        TranslationMemorySuggestionsMenuItem.Visibility = Visibility.Collapsed;

        if (DataGridMain.SelectedItem is not DataGridItem row)
            return;

        bool hasTranslation = !string.IsNullOrWhiteSpace(row.Translation) && row.Translation != row.Text;
        AddToGlossaryMenuItem.Visibility = hasTranslation ? Visibility.Visible : Visibility.Collapsed;
        const int maxSuggestions = 10;

        if (row.HasTranslationMemoryMatch)
        {
            var tmMatches = TranslationMemoryManager.Instance.FindMatches(row.Text);

            foreach (var match in tmMatches.Take(maxSuggestions))
            {
                string full = $"«{match.Translation}» ({match.Memory.Name})";

                var item = new MenuItem
                {
                    Header = TruncateForMenu(full),
                    ToolTip = full,
                    Tag = (row, match.Translation)
                };

                item.Click += TranslationMemorySuggestion_Click;
                TranslationMemorySuggestionsMenuItem.Items.Add(item);
            }

            if (tmMatches.Count > maxSuggestions)
            {
                var moreItem = new MenuItem { Header = $"… ще {tmMatches.Count - maxSuggestions}" };

                foreach (var match in tmMatches.Skip(maxSuggestions))
                {
                    string full = $"«{match.Translation}» ({match.Memory.Name})";

                    var subItem = new MenuItem
                    {
                        Header = TruncateForMenu(full),
                        ToolTip = full,
                        Tag = (row, match.Translation)
                    };

                    subItem.Click += TranslationMemorySuggestion_Click;
                    moreItem.Items.Add(subItem);
                }

                TranslationMemorySuggestionsMenuItem.Items.Add(moreItem);
            }

            TranslationMemorySuggestionsMenuItem.Visibility = tmMatches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!row.HasGlossaryMatch)
            return;

        var matches = GlossaryManager.Instance.FindMatches(row.Text);

        if (matches.Count == 0)
            return;

        int glossaryItemsAdded = 0;
        int glossaryItemsTotal = 0;
        MenuItem moreGlossaryItem = null;

        foreach (var match in matches)
        {
            foreach (var translation in match.Entry.Translations)
            {
                glossaryItemsTotal++;
                string full = $"«{match.MatchedText}» → «{translation}»";

                var item = new MenuItem
                {
                    Header = TruncateForMenu(full),
                    ToolTip = full,
                    Tag = (row, match.MatchedText, translation)
                };

                item.Click += GlossarySuggestion_Click;

                if (glossaryItemsAdded < maxSuggestions)
                {
                    GlossarySuggestionsMenuItem.Items.Add(item);
                    glossaryItemsAdded++;
                }
                else
                {
                    moreGlossaryItem ??= new MenuItem { Header = "" };
                    moreGlossaryItem.Items.Add(item);
                }
            }
        }

        if (moreGlossaryItem != null)
        {
            moreGlossaryItem.Header = $"… ще {glossaryItemsTotal - maxSuggestions}";
            GlossarySuggestionsMenuItem.Items.Add(moreGlossaryItem);
        }

        GlossarySuggestionsMenuItem.Visibility = Visibility.Visible;
    }

    private static string TruncateForMenu(string text, int maxLength = 70)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "…";
    }

    private void PasteToGrid_Click(object sender, RoutedEventArgs e)
    {
        PasteClipboardToSelectedRows();
    }

    private void LoadTranslationIntoEditor(DataGridItem item)
    {
        EditTextBox.Text = item?.Translation ?? "";
        EditTextBox.SelectionLength = 0;
    }

    private void SyncEditorIfSelected(DataGridItem item)
    {
        if (item != null && DataGridMain.SelectedItem == item)
        {
            _editingTranslationBefore = item.Translation;
            LoadTranslationIntoEditor(item);
        }
    }

    private void EditTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            e.Handled = true;
        }
    }
    #endregion

    #region CSV Import/Export
    private void ExportAllToCSV_Click(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Filter = "CSV файл|*.csv",
            Title = "Експорт усіх рядків у CSV",
            FileName = Path.GetFileNameWithoutExtension(Tab_FilePath) + ".csv"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                CSVHelper.ExportToCSV(sfd.FileName, Tab_Rows.ToList());
                MessageBox.Show(this, "Експортування завершено.", "Експорт усіх рядків у CSV", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{ex.Message}", "Помилка", MessageBoxButton.OK);
            }
        }
    }

    private void ExportSelectedToCSV_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = DataGridMain.SelectedItems.Cast<DataGridItem>().OrderBy(i => i.Index).ToList();

        var sfd = new SaveFileDialog
        {
            Filter = "CSV файл|*.csv",
            Title = "Експорт виділених рядків у CSV",
            FileName = Path.GetFileNameWithoutExtension(Tab_FilePath) + "_select.csv"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                CSVHelper.ExportToCSV(sfd.FileName, selectedItems);
                MessageBox.Show(this, $"Експортовано {selectedItems.Count} {PluralizationHelper.GetRowsWord(selectedItems.Count)}.", "Експорт виділених рядків у CSV", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{ex.Message}", "Помилка", MessageBoxButton.OK);
            }
        }
    }

    private void BatchExportToCSV_Click(object sender, RoutedEventArgs e)
    {
        var tabsWithData = _tabs
            .Where(t => !string.IsNullOrEmpty(t.FilePath) && t.DataRows.Count > 0)
            .ToList();

        var ofd = new OpenFolderDialog { Title = "Вибрати теку для пакетного експорту CSV" };

        if (ofd.ShowDialog() != true)
            return;

        try
        {
            var input = tabsWithData.Select(t => (t.FilePath, t.DataRows.ToList())).ToList();
            var (exported, failed) = CSVHelper.ExportToCSVBatch(ofd.FolderName, input);
            string msg = $"Експортовано {exported} {PluralizationHelper.GetFilesWord(exported)}.";

            if (failed.Count > 0)
                msg += $"\nПомилка при експорті:\n{string.Join("\n", failed.Select(f => "  — " + f))}";

            MessageBox.Show(this, msg, "Пакетний експорт CSV", MessageBoxButton.OK);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Помилка", MessageBoxButton.OK);
        }
    }

    private async void ImportCSVByRowOrder_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "CSV файл|*.csv",
            Title = "Імпорт CSV за порядком рядків"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                var before = SnapshotTranslations();
                var csvData = CSVHelper.ImportFromCSV(ofd.FileName);
                int changedCount = CSVHelper.ImportByRowOrder(Tab_Rows.ToList(), csvData);

                if (changedCount > 0)
                {
                    Tab_Undo.Push(BuildUndoAction("Імпорт CSV за порядком рядків", before));
                    UpdateUndoRedoMenuItems();
                }

                await CalculateOriginalStatsAsync(_activeTab);
                CheckIfHasChanges();
                DataGridMain.Items.Refresh();
                SyncEditorIfSelected(DataGridMain.SelectedItem as DataGridItem);
                MessageBox.Show(this, $"Змінено {changedCount} {PluralizationHelper.GetCellsWord(changedCount)}", "Імпорт CSV за порядком рядків", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{ex.Message}", "Помилка", MessageBoxButton.OK);
            }
        }
    }

    private async void ImportCSVByID_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "CSV файл|*.csv",
            Title = "Імпорт CSV за ID"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                var before = SnapshotTranslations();
                var csvData = CSVHelper.ImportFromCSV(ofd.FileName);
                int changedCount = CSVHelper.ImportByID(Tab_Rows.ToList(), csvData);

                if (changedCount > 0)
                {
                    Tab_Undo.Push(BuildUndoAction("Імпорт CSV за ID", before));
                    UpdateUndoRedoMenuItems();
                }

                await CalculateOriginalStatsAsync(_activeTab);
                CheckIfHasChanges();
                DataGridMain.Items.Refresh();
                SyncEditorIfSelected(DataGridMain.SelectedItem as DataGridItem);
                MessageBox.Show(this, $"Змінено {changedCount} {PluralizationHelper.GetCellsWord(changedCount)}", "Імпорт CSV за ID", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{ex.Message}", "Помилка", MessageBoxButton.OK);
            }
        }
    }

    private async void ImportCSVByOriginal_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Файл CSV|*.csv",
            Title = "Імпорт CSV за оригінальним текстом"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                var before = SnapshotTranslations();
                var csvData = CSVHelper.ImportFromCSV(ofd.FileName);
                int changedCount = CSVHelper.ImportByOriginalText(Tab_Rows.ToList(), csvData);

                if (changedCount > 0)
                {
                    Tab_Undo.Push(BuildUndoAction("Імпорт CSV за оригінальним текстом", before));
                    UpdateUndoRedoMenuItems();
                }

                await CalculateOriginalStatsAsync(_activeTab);
                CheckIfHasChanges();
                DataGridMain.Items.Refresh();
                SyncEditorIfSelected(DataGridMain.SelectedItem as DataGridItem);
                MessageBox.Show(this, $"Змінено {changedCount} {PluralizationHelper.GetCellsWord(changedCount)}", "Імпорт CSV за оригінальним текстом", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{ex.Message}", "Помилка", MessageBoxButton.OK);
            }
        }
    }

    private async void ImportCSVByIDAndOriginal_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "CSV файл|*.csv",
            Title = "Імпорт CSV за ID та оригінальним текстом"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                var before = SnapshotTranslations();
                var csvData = CSVHelper.ImportFromCSV(ofd.FileName);
                int changedCount = CSVHelper.ImportByIDAndOriginalText(Tab_Rows.ToList(), csvData);

                if (changedCount > 0)
                {
                    Tab_Undo.Push(BuildUndoAction("Імпорт CSV за ID та оригінальним текстом", before));
                    UpdateUndoRedoMenuItems();
                }

                await CalculateOriginalStatsAsync(_activeTab);
                CheckIfHasChanges();
                DataGridMain.Items.Refresh();
                SyncEditorIfSelected(DataGridMain.SelectedItem as DataGridItem);
                MessageBox.Show(this, $"Змінено {changedCount} {PluralizationHelper.GetCellsWord(changedCount)}", "Імпорт CSV за ID та ориг. текстом", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{ex.Message}", "Помилка", MessageBoxButton.OK);
            }
        }
    }

    private async void BatchImportCSVByRowOrder_Click(object sender, RoutedEventArgs e)
    {
        await BatchImportFromFolder("Виберіть теку з CSV файлами", "Пакетний імпорт CSV за порядком рядків", (filePath, folder, rows) => CSVHelper.ImportByRowOrderFromFolder(filePath, folder, rows));
    }

    private async void BatchImportCSVByID_Click(object sender, RoutedEventArgs e)
    {
        await BatchImportFromFolder("Виберіть теку з CSV файлами", "Пакетний імпорт CSV за ID", (filePath, folder, rows) => CSVHelper.ImportByIDFromFolder(filePath, folder, rows));
    }

    private async void BatchImportCSVByOriginalText_Click(object sender, RoutedEventArgs e)
    {
        await BatchImportFromFolder("Виберіть теку з CSV файлами", "Пакетний імпорт CSV за оригінальним текстом", (filePath, folder, rows) => CSVHelper.ImportByOriginalTextFromFolder(filePath, folder, rows));
    }

    private async void BatchImportCSVByIDAndOriginalText_Click(object sender, RoutedEventArgs e)
    {
        await BatchImportFromFolder("Виберіть теку з CSV файлами", "Пакетний імпорт CSV за ID та ориг. текстом", (filePath, folder, rows) => CSVHelper.ImportByIDAndOriginalTextFromFolder(filePath, folder, rows));
    }

    private async Task BatchImportFromFolder(string folderDialogTitle, string operationTitle, Func<string, string, List<DataGridItem>, int> importFunc)
    {
        var tabsWithData = _tabs
            .Where(t => !string.IsNullOrEmpty(t.FilePath) && t.DataRows.Count > 0)
            .ToList();

        if (tabsWithData.Count == 0)
            return;

        var ofd = new OpenFolderDialog { Title = folderDialogTitle, Multiselect = false };

        if (ofd.ShowDialog() != true)
            return;

        string folderPath = ofd.FolderName;
        StatusMessage($"{operationTitle}...");
        int totalChanged = 0;
        int notFoundCount = 0;
        var errors = new List<string>();

        foreach (var tab in tabsWithData)
        {
            try
            {
                var before = SnapshotTranslationsFor(tab);
                var rows = tab.DataRows;
                string filePath = tab.FilePath;
                bool isActive = tab == _activeTab;

                int result = isActive
                    ? importFunc(filePath, folderPath, rows.ToList())
                    : await Task.Run(() => importFunc(filePath, folderPath, rows.ToList()));

                if (result == -1)
                {
                    notFoundCount++;
                    continue;
                }

                if (result > 0)
                {
                    tab.UndoRedo.Push(BuildUndoActionFor(tab, operationTitle, before));
                    tab.HasUnsavedChanges = true;
                    totalChanged += result;

                    if (isActive)
                    {
                        UpdateUndoRedoMenuItems();
                        CheckIfHasChanges();
                        DataGridMain.Items.Refresh();
                        SyncEditorIfSelected(DataGridMain.SelectedItem as DataGridItem);
                    }
                    else
                    {
                        _ = CalculateOriginalStatsAsync(tab);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(tab.FilePath)}: {ex.Message}");
            }
        }

        if (_activeTab != null && tabsWithData.Contains(_activeTab))
            await CalculateOriginalStatsAsync(_activeTab);

        CloseFromState();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Оброблено {tabsWithData.Count} {PluralizationHelper.GetTabsWord(tabsWithData.Count)}");
        sb.AppendLine($"Загалом змінено {totalChanged} {PluralizationHelper.GetRowsWord(totalChanged)}");

        if (notFoundCount > 0)
            sb.AppendLine($"Не знайдено відповідний файл для {notFoundCount} {PluralizationHelper.GetTabsWord(notFoundCount)}");

        if (errors.Count > 0)
            sb.AppendLine("Помилки:\n" + string.Join("\n", errors));

        MessageBox.Show(this, sb.ToString(), operationTitle, MessageBoxButton.OK);
    }
    #endregion

    #region TXT Import/Export
    private void ExportSimpleToTXT_Click(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Filter = "Текстовий файл|*.txt",
            Title = "Експорт лише тексту у TXT",
            FileName = Path.GetFileNameWithoutExtension(Tab_FilePath) + ".txt"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                TxtHelper.ExportSimple(sfd.FileName, Tab_Rows.ToList());
                MessageBox.Show(this, "Експортування завершено.", "Експорт лише тексту у TXT", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{ex.Message}", "Помилка", MessageBoxButton.OK);
            }
        }
    }

    private void ExportSelectedWithIDToTXT_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = DataGridMain.SelectedItems.Cast<DataGridItem>().OrderBy(i => i.Index).ToList();

        if (selectedItems.Count == 0)
        {
            MessageBox.Show(this, "Виділіть рядки для експорту.", "Попередження", MessageBoxButton.OK);
            return;
        }

        var sfd = new SaveFileDialog
        {
            Filter = "Текстовий файл|*.txt",
            Title = "Експорт виділених рядків з ID у TXT",
            FileName = Path.GetFileNameWithoutExtension(Tab_FilePath) + "_selected.txt"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                TxtHelper.ExportSelectedWithID(sfd.FileName, selectedItems);
                MessageBox.Show(this, $"Експортовано {selectedItems.Count} {PluralizationHelper.GetRowsWord(selectedItems.Count)}.", "Експорт виділених рядків з ID у TXT", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{ex.Message}", "Помилка", MessageBoxButton.OK);
            }
        }
    }

    private void ExportWithIDFullToTXT_Click(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Filter = "Текстовий файл|*.txt",
            Title = "Експорт з ID у TXT",
            FileName = Path.GetFileNameWithoutExtension(Tab_FilePath) + "_full.txt"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                TxtHelper.ExportWithIDFull(sfd.FileName, Tab_Rows.ToList());
                MessageBox.Show(this, "Експортування завершено.", "Експорт з ID у TXT", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{ex.Message}", "Помилка", MessageBoxButton.OK);
            }
        }
    }

    private void BatchExportWithIDToTXT_Click(object sender, RoutedEventArgs e)
    {
        var tabsWithData = _tabs
            .Where(t => !string.IsNullOrEmpty(t.FilePath) && t.DataRows.Count > 0)
            .ToList();

        var ofd = new OpenFolderDialog
        {
            Title = "Виберіть теку для пакетного експорту TXT з ID"
        };

        if (ofd.ShowDialog() != true) return;

        try
        {
            var input = tabsWithData.Select(t => (t.FilePath, t.DataRows.ToList())).ToList();
            var (exported, failed) = TxtHelper.BatchExportWithID(ofd.FolderName, input);
            string msg = $"Експортовано {exported} {PluralizationHelper.GetFilesWord(exported)}.";

            if (failed.Count > 0)
                msg += $"\nПомилка при експорті:\n{string.Join("\n", failed.Select(f => "  — " + f))}";

            MessageBox.Show(this, msg, "Пакетний експорт TXT з ID", MessageBoxButton.OK);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Помилка", MessageBoxButton.OK);
        }
    }

    private async void ImportTXTByRowOrder_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Текстовий файл|*.txt",
            Title = "Імпорт TXT за порядком рядків"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                var before = SnapshotTranslations();
                int changedCount = TxtHelper.ImportByRowOrder(Tab_Rows.ToList(), ofd.FileName);

                if (changedCount > 0)
                {
                    Tab_Undo.Push(BuildUndoAction("Імпорт TXT за порядком рядків", before));
                    UpdateUndoRedoMenuItems();
                }

                await CalculateOriginalStatsAsync(_activeTab);
                CheckIfHasChanges();
                DataGridMain.Items.Refresh();
                SyncEditorIfSelected(DataGridMain.SelectedItem as DataGridItem);
                MessageBox.Show(this, $"Змінено {changedCount} {PluralizationHelper.GetCellsWord(changedCount)}", "Імпорт TXT за порядком рядків", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{ex.Message}", "Помилка", MessageBoxButton.OK);
            }
        }
    }

    private async void ImportTXTByID_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Текстовий файл|*.txt",
            Title = "Імпорт TXT за ID"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                var before = SnapshotTranslations();
                int changedCount = TxtHelper.ImportByID(Tab_Rows.ToList(), ofd.FileName);

                if (changedCount > 0)
                {
                    Tab_Undo.Push(BuildUndoAction("Імпорт TXT за ID", before));
                    UpdateUndoRedoMenuItems();
                }

                await CalculateOriginalStatsAsync(_activeTab);
                CheckIfHasChanges();
                DataGridMain.Items.Refresh();
                SyncEditorIfSelected(DataGridMain.SelectedItem as DataGridItem);
                MessageBox.Show(this, $"Змінено {changedCount} {PluralizationHelper.GetCellsWord(changedCount)}", "Імпорт TXT за ID", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{ex.Message}", "Помилка", MessageBoxButton.OK);
            }
        }
    }

    private async void BatchImportTXTByID_Click(object sender, RoutedEventArgs e)
    {
        await BatchImportFromFolder("Виберіть теку з TXT файлами для пакетного імпорту за ID", "Пакетний імпорт TXT за ID", (filePath, folder, rows) => TxtHelper.ImportByIDFromFolder(filePath, folder, rows));
    }
    #endregion

    #region JSON Import
    private bool TryPickImportLanguageIndex(JToken importData, JsonFormatType format, string prompt, out int selectedIndex, out bool applyToAll, out string selectedLabel, bool isBatchOpen = false)
    {
        selectedIndex = 0;
        applyToAll = false;
        selectedLabel = null;

        var langsArray = JsonImportHelper.GetLanguagesArray(importData, format);
        if (langsArray == null || !langsArray.Any())
        {
            MessageBox.Show(this, "Не вдалося визначити мови у файлі імпорту.", "Помилка", MessageBoxButton.OK);
            return false;
        }

        var labels = langsArray.Select((t, i) => t is JObject obj
                ? (obj["Name"]?.ToString() ?? obj["Code"]?.ToString() ?? obj.Properties().FirstOrDefault()?.Value?.ToString() ?? $"Мова {i + 1}")
                : (t?.ToString() ?? $"Мова {i + 1}"))
            .ToList();

        var names = labels.Select((label, i) => $"{i}: {label}").ToList();

        var dialog = new IndexSelectionWindow(prompt, names, isBatchOpen)
        {
            Owner = IsLoaded ? this : null,
            WindowStartupLocation = IsLoaded ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
        };

        if (dialog.ShowDialog() != true) return false;

        selectedIndex = dialog.SelectedIndex;
        applyToAll = dialog.ApplyToAll;
        selectedLabel = selectedIndex >= 0 && selectedIndex < labels.Count ? labels[selectedIndex] : null;
        return true;
    }

    private async void ImportFromJson_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab == null || Tab_Rows.Count == 0) return;

        var ofd = new OpenFileDialog { Filter = "JSON файли|*.json|Всі файли|*.*", Title = "Імпорт перекладу з JSON" };
        if (ofd.ShowDialog() != true) return;

        string path = ofd.FileName;
        JToken importData;
        JsonFormatType format;

        try
        {
            (importData, format) = await Task.Run(() => JsonImportHelper.PrepareJsonImport(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{ex.Message}", "Не вдалося прочитати файл", MessageBoxButton.OK);
            return;
        }

        if (format == JsonFormatType.Unknown)
        {
            MessageBox.Show(this, "Не вдалося визначити структуру JSON-файлу.", "Помилка", MessageBoxButton.OK);
            return;
        }

        int importLangIndex = 0;

        if (JsonFormatDetector.IsArrayFormat(format) && JsonImportHelper.NeedsLanguageSelection(format))
        {
            if (!TryPickImportLanguageIndex(importData, format, "Індекс мови для імпорту перекладу:", out importLangIndex, out _, out _))
                return;
        }

        StatusMessage("Імпорт перекладу з JSON...");
        try
        {
            var before = SnapshotTranslations();
            var rows = Tab_Rows;

            var (changedCount, updated) = await Task.Run(() => JsonImportHelper.ImportFromJson(rows, importData, format, importLangIndex));
            JsonImportHelper.ApplyImportedTranslations(rows, updated);

            if (changedCount > 0)
            {
                Tab_Undo.Push(BuildUndoAction("Імпорт перекладу з JSON", before));
                UpdateUndoRedoMenuItems();
            }

            await CalculateOriginalStatsAsync(_activeTab);
            CheckIfHasChanges();
            DataGridMain.Items.Refresh();
            SyncEditorIfSelected(DataGridMain.SelectedItem as DataGridItem);
            CloseFromState();
            MessageBox.Show(this, $"Змінено {changedCount} {PluralizationHelper.GetCellsWord(changedCount)}", "Імпорт перекладу з JSON", MessageBoxButton.OK);
        }
        catch (Exception ex)
        {
            CloseFromState();
            MessageBox.Show(this, $"{ex.Message}", "Помилка", MessageBoxButton.OK);
        }
    }

    private async void ImportFromJsonFolder_Click(object sender, RoutedEventArgs e)
    {
        var jsonTabs = _tabs.Where(t => !string.IsNullOrEmpty(t.FilePath) && t.FileType == ".json").ToList();
        if (jsonTabs.Count == 0) return;

        var ofd = new OpenFolderDialog { Title = "Виберіть теку з JSON файлами для пакетного імпорту перекладу", Multiselect = false };
        if (ofd.ShowDialog() != true) return;

        var candidates = Directory.GetFiles(ofd.FolderName, "*.json", SearchOption.AllDirectories);
        var plan = JsonImportHelper.BuildBatchImportPlan(jsonTabs.Select(t => t.FilePath), candidates)
            .ToDictionary(p => p.tabPath, p => p.importFile);

        StatusMessage("Пакетний імпорт перекладу з JSON...");
        int totalChanged = 0, notFoundCount = 0;
        var errors = new List<string>();
        var langIndexByFormat = new Dictionary<JsonFormatType, int>();
        bool activeTabChanged = false;

        foreach (var tab in jsonTabs)
        {
            try
            {
                if (!plan.TryGetValue(tab.FilePath, out var importFile)) { notFoundCount++; continue; }

                var (importData, format) = await Task.Run(() => JsonImportHelper.PrepareJsonImport(importFile));
                if (format == JsonFormatType.Unknown) { notFoundCount++; continue; }

                int importLangIndex = 0;

                if (JsonFormatDetector.IsArrayFormat(format) && JsonImportHelper.NeedsLanguageSelection(format))
                {
                    if (!langIndexByFormat.TryGetValue(format, out importLangIndex))
                    {
                        if (!TryPickImportLanguageIndex(importData, format, "Індекс мови для імпорту перекладу:", out importLangIndex, out bool applyToAll, out _, isBatchOpen: true))
                            continue;

                        if (applyToAll)
                            langIndexByFormat[format] = importLangIndex;
                    }
                }

                var before = SnapshotTranslationsFor(tab);
                var rows = tab.DataRows;

                var (result, updated) = await Task.Run(() => JsonImportHelper.ImportFromJson(rows, importData, format, importLangIndex));

                if (result > 0)
                {
                    JsonImportHelper.ApplyImportedTranslations(rows, updated);
                    tab.UndoRedo.Push(BuildUndoActionFor(tab, "Пакетний імпорт перекладу з JSON", before));
                    tab.HasUnsavedChanges = true;
                    totalChanged += result;
                    if (tab == _activeTab) activeTabChanged = true;
                }
            }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(tab.FilePath)}: {ex.Message}"); }
        }

        if (activeTabChanged)
        {
            await CalculateOriginalStatsAsync(_activeTab);
            CheckIfHasChanges();
            DataGridMain.Items.Refresh();
            SyncEditorIfSelected(DataGridMain.SelectedItem as DataGridItem);
        }

        CloseFromState();
        string msg = $"Змінено рядків: {totalChanged}";
        if (notFoundCount > 0) msg += $"\nФайлів не знайдено: {notFoundCount}";
        if (errors.Count > 0) msg += $"\nПомилок: {errors.Count}\n" + string.Join("\n", errors.Take(5));
        MessageBox.Show(this, msg, "Пакетний імпорт перекладу з JSON", MessageBoxButton.OK);
    }

    private async void ImportOriginalFromJson_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab == null || Tab_Rows.Count == 0)
            return;

        var warn = MessageBox.Show(this, "Ця операція не підтримує запис в історію змін (Undo/Redo).\nІсторія змін для поточного файлу буде очищена, попередні дії неможливо буде скасувати.\nПродовжити?", "Імпорт з JSON у стовпчик Оригінал", MessageBoxButton.YesNo);

        if (warn != MessageBoxResult.Yes)
            return;

        var ofd = new OpenFileDialog { Filter = "JSON файли|*.json|Всі файли|*.*", Title = "Імпорт з JSON у стовпчик Оригінал" };

        if (ofd.ShowDialog() != true)
            return;

        string path = ofd.FileName;
        JToken importData;
        JsonFormatType format;

        try
        {
            (importData, format) = await Task.Run(() => JsonImportHelper.PrepareJsonImport(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{ex.Message}", "Не вдалося прочитати файл", MessageBoxButton.OK);
            return;
        }

        if (format == JsonFormatType.Unknown)
        {
            MessageBox.Show(this, "Не вдалося визначити структуру JSON-файлу.", "Помилка", MessageBoxButton.OK);
            return;
        }

        int importLangIndex = 0;
        string selectedLabel = null;

        if (JsonFormatDetector.IsArrayFormat(format) && JsonImportHelper.NeedsLanguageSelection(format))
        {
            if (!TryPickImportLanguageIndex(importData, format, "Індекс мови для імпорту у стовпчик Оригінал:", out importLangIndex, out _, out selectedLabel))
                return;
        }

        StatusMessage("Імпорт з JSON у стовпчик Оригінал...");
        try
        {
            var rows = Tab_Rows;

            var (changedCount, updated) = await Task.Run(() => JsonImportHelper.ImportOriginalFromJson(rows, importData, format, importLangIndex));
            JsonImportHelper.ApplyImportedOriginals(rows, updated);

            Tab_Undo?.Clear();
            UpdateUndoRedoMenuItems();

            if (Tab_Filter != GridRowFilter.None)
                ApplyGridFilter(GridRowFilter.None);

            if (selectedLabel != null)
            {
                _activeTab.OrigLangLabel = selectedLabel;
                UpdateColumnHeaders(_activeTab.OrigLangLabel, _activeTab.TransLangLabel);
            }

            await CalculateOriginalStatsAsync(_activeTab);
            CheckIfHasChanges();
            DataGridMain.Items.Refresh();
            CloseFromState();
            MessageBox.Show(this, $"Змінено {changedCount} {PluralizationHelper.GetCellsWord(changedCount)}", "Імпорт з JSON у стовпчик Оригінал", MessageBoxButton.OK);
        }
        catch (Exception ex)
        {
            CloseFromState();
            MessageBox.Show(this, $"{ex.Message}", "Імпорт з JSON у стовпчик Оригінал", MessageBoxButton.OK);
        }
    }

    private async void ImportOriginalFromJsonFolder_Click(object sender, RoutedEventArgs e)
    {
        var jsonTabs = _tabs.Where(t => !string.IsNullOrEmpty(t.FilePath) && t.FileType == ".json").ToList();

        if (jsonTabs.Count == 0)
            return;

        var warn = MessageBox.Show(this, "Ця операція не підтримує запис в історію змін (Undo/Redo).\nІсторія змін для всіх зачеплених файлів буде очищена, попередні дії неможливо буде скасувати.\nПродовжити?", "Пакетний імпорт з JSON у стовпчик Оригінал", MessageBoxButton.YesNo);

        if (warn != MessageBoxResult.Yes)
            return;

        var ofd = new OpenFolderDialog { Title = "Виберіть теку з JSON файлами для пакетного імпорту у стовпчик Оригінал", Multiselect = false };

        if (ofd.ShowDialog() != true)
            return;

        var candidates = Directory.GetFiles(ofd.FolderName, "*.json", SearchOption.AllDirectories);
        var plan = JsonImportHelper.BuildBatchImportPlan(jsonTabs.Select(t => t.FilePath), candidates)
            .ToDictionary(p => p.tabPath, p => p.importFile);

        StatusMessage("Пакетний імпорт з JSON у стовпчик Оригінал...");
        int totalChanged = 0, notFoundCount = 0;
        var errors = new List<string>();
        var langIndexByFormat = new Dictionary<JsonFormatType, int>();
        var langLabelByFormat = new Dictionary<JsonFormatType, string>();
        bool activeTabChanged = false;

        foreach (var tab in jsonTabs)
        {
            try
            {
                if (!plan.TryGetValue(tab.FilePath, out var importFile)) { notFoundCount++; continue; }

                var (importData, format) = await Task.Run(() => JsonImportHelper.PrepareJsonImport(importFile));
                if (format == JsonFormatType.Unknown) { notFoundCount++; continue; }

                int importLangIndex = 0;
                string pickedLabel = null;

                if (JsonFormatDetector.IsArrayFormat(format) && JsonImportHelper.NeedsLanguageSelection(format))
                {
                    if (langIndexByFormat.TryGetValue(format, out importLangIndex))
                    {
                        pickedLabel = langLabelByFormat.GetValueOrDefault(format);
                    }
                    else
                    {
                        if (!TryPickImportLanguageIndex(importData, format, "Індекс мови для імпорту у стовпчик Оригінал:", out importLangIndex, out bool applyToAll, out pickedLabel, isBatchOpen: true))
                            continue;

                        if (applyToAll)
                        {
                            langIndexByFormat[format] = importLangIndex;
                            langLabelByFormat[format] = pickedLabel;
                        }
                    }
                }

                var rows = tab.DataRows;
                var (result, updated) = await Task.Run(() => JsonImportHelper.ImportOriginalFromJson(rows, importData, format, importLangIndex));

                if (result > 0)
                {
                    JsonImportHelper.ApplyImportedOriginals(rows, updated);
                    tab.UndoRedo?.Clear();

                    if (tab.ActiveGridFilter != GridRowFilter.None)
                    {
                        tab.ActiveGridFilter = GridRowFilter.None;
                        tab.DataGridView.SortDescriptions.Clear();
                        tab.DataGridView.Filter = null;
                    }

                    if (pickedLabel != null)
                        tab.OrigLangLabel = pickedLabel;

                    totalChanged += result;
                    if (tab == _activeTab) activeTabChanged = true;
                }
            }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(tab.FilePath)}: {ex.Message}"); }
        }

        if (activeTabChanged)
        {
            UpdateUndoRedoMenuItems();
            UpdateGridFilterUI();
            UpdateColumnHeaders(_activeTab.OrigLangLabel, _activeTab.TransLangLabel);
            await CalculateOriginalStatsAsync(_activeTab);
            CheckIfHasChanges();
            DataGridMain.Items.Refresh();
        }

        CloseFromState();
        string msg = $"Змінено рядків: {totalChanged}";
        if (notFoundCount > 0) msg += $"\nФайлів не знайдено: {notFoundCount}";
        if (errors.Count > 0) msg += $"\nПомилок: {errors.Count}\n" + string.Join("\n", errors.Take(5));
        MessageBox.Show(this, msg, "Пакетний імпорт з JSON у стовпчик Оригінал", MessageBoxButton.OK);
    }
    #endregion

    #region Language Manager
    private async void ManageLanguages_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.FormatHandler == null || string.IsNullOrEmpty(_activeTab.FilePath))
            return;

        if (_activeTab.HasUnsavedChanges)
        {
            var result = MessageBox.Show(this, "Операції з мовами потребують перезавантаження файлу, і всі незбережені переклади будуть втрачені.\nЗберегти зміни перед тим, як продовжити?", "Незбережені зміни", MessageBoxButton.YesNoCancel);

            if (result == MessageBoxResult.Cancel)
                return;

            if (result == MessageBoxResult.Yes)
            {
                await SaveOverwriteAsync(_activeTab);

                if (_activeTab.HasUnsavedChanges)
                    return;
            }
        }

        var win = new LanguageManagerWindow { Owner = this };
        win.PreloadFile(_activeTab.FilePath, _activeTab.JsonData);
        win.ShowDialog();

        if (win.FileWasModified)
            await LoadFileIntoTab(_activeTab, _activeTab.FilePath);
    }
    #endregion

    #region Replace
    private void OpenReplaceWindow()
    {
        if (replaceWindow == null || !replaceWindow.IsLoaded)
        {
            replaceWindow = new ReplaceWindow();
            replaceWindow.ReplaceRequested += ReplaceWindow_ReplaceRequested;
            replaceWindow.Closed += (s, e) =>
            {
                var w = (ReplaceWindow)s;
                _lastReplaceFindText = w.FindText;
                _lastReplaceText = w.ReplaceText;
                replaceWindow = null;
                this.Activate();
            };
            replaceWindow.Owner = this;
            replaceWindow.Show();
            replaceWindow.Initialize(_recentSearchesManager, _lastReplaceFindText, _lastReplaceText);
        }
        else
        {
            replaceWindow.Activate();
            replaceWindow.Initialize(_recentSearchesManager, null, null);
        }

        int tabsWithData = _tabs.Count(t => t.DataRows?.Count > 0);
        replaceWindow.SetMultiTabMode(tabsWithData > 1);
    }

    private async void ReplaceWindow_ReplaceRequested(object sender, EventArgs e)
    {
        if (replaceWindow == null) return;

        string findText = replaceWindow.FindText;
        string replaceText = replaceWindow.ReplaceText;
        bool wholeWord = replaceWindow.WholeWord;
        bool caseSensitive = replaceWindow.CaseSensitive;
        bool exactMatch = replaceWindow.ExactMatch;
        bool allTabs = replaceWindow.AllTabs;

        if (allTabs)
            await ExecuteReplaceAllTabs(findText, replaceText, wholeWord, caseSensitive, exactMatch);
        else
            await ExecuteReplaceSingleTab(findText, replaceText, wholeWord, caseSensitive, exactMatch);
    }

    private async Task ExecuteReplaceSingleTab(string findText, string replaceText, bool wholeWord, bool caseSensitive, bool exactMatch)
    {
        if (_activeTab == null || Tab_Rows == null)
            return;

        bool hasFilter = Tab_Filter != GridRowFilter.None;
        IEnumerable<DataGridItem> targets = hasFilter
            ? Tab_Rows.Where(MatchesGridFilter)
            : (IEnumerable<DataGridItem>)Tab_Rows;

        var before = SnapshotTranslations();
        int replacedCount = ApplyReplaceToRows(targets, findText, replaceText, wholeWord, caseSensitive, exactMatch);

        if (replacedCount == 0)
        {
            replaceWindow?.ShowStatus("Нічого не знайдено");
            return;
        }

        string scope = hasFilter ? " (лише у поточному фільтрі)" : "";
        replaceWindow?.ShowStatus($"Замінено у {PluralizationHelper.GetCellsWordReplace(replacedCount)}{scope}");
        Tab_Undo.Push(BuildUndoAction($"«{findText}»", before));
        UpdateUndoRedoMenuItems();
        await CalculateOriginalStatsAsync(_activeTab);
        CheckIfHasChanges();
        DataGridMain.Items.Refresh();
        SyncEditorIfSelected(DataGridMain.SelectedItem as DataGridItem);
    }

    private async Task ExecuteReplaceAllTabs(string findText, string replaceText, bool wholeWord, bool caseSensitive, bool exactMatch)
    {
        int totalReplaced = 0;
        int tabsAffected = 0;
        int filteredTabsCount = 0;

        var tabsToProcess = _tabs.Where(t => t.DataRows?.Count > 0).ToList();

        foreach (var tab in tabsToProcess)
        {
            bool hasFilter = tab.ActiveGridFilter != GridRowFilter.None;
            IEnumerable<DataGridItem> targets = hasFilter
                ? tab.DataRows.Where(item => MatchesGridFilterFor(tab.ActiveGridFilter, item))
                : (IEnumerable<DataGridItem>)tab.DataRows;

            var before = SnapshotTranslationsFor(tab);
            int replacedCount = ApplyReplaceToRows(
                targets, findText, replaceText, wholeWord, caseSensitive, exactMatch);

            if (replacedCount > 0)
            {
                tab.UndoRedo.Push(BuildUndoActionFor(tab, $"«{findText}»", before));
                tab.HasUnsavedChanges = tab.DataRows.Any(r => r.IsModified);
                totalReplaced += replacedCount;
                tabsAffected++;
                if (hasFilter) filteredTabsCount++;

                if (tab != _activeTab)
                    _ = CalculateOriginalStatsAsync(tab);
            }
        }

        if (_activeTab != null)
        {
            UpdateUndoRedoMenuItems();
            CheckIfHasChanges();
            DataGridMain.Items.Refresh();
            await CalculateOriginalStatsAsync(_activeTab);
            SyncEditorIfSelected(DataGridMain.SelectedItem as DataGridItem);
        }

        if (totalReplaced == 0)
        {
            replaceWindow?.ShowStatus("Нічого не знайдено");
            return;
        }

        SaveAllMenuItem.IsEnabled = _tabs.Any(t => t.HasUnsavedChanges);
        string status = $"Замінено у {PluralizationHelper.GetCellsWordReplace(totalReplaced)} у {tabsAffected} {PluralizationHelper.GetTabsWordLocative(tabsAffected)}";

        if (filteredTabsCount > 0)
            status += $", з них {filteredTabsCount} з увімкненим фільтром";

        replaceWindow?.ShowStatus(status);
    }

    private static int ApplyReplaceToRows(IEnumerable<DataGridItem> rows, string findText, string replaceText, bool wholeWord, bool caseSensitive, bool exactMatch)
    {
        int count = 0;
        foreach (var item in rows)
        {
            if (string.IsNullOrEmpty(item.Translation)) continue;
            string orig = item.Translation;
            string next = ReplaceInText(orig, findText, replaceText, wholeWord, caseSensitive, exactMatch);
            if (!string.Equals(orig, next, StringComparison.Ordinal))
            {
                item.Translation = next;
                count++;
            }
        }
        return count;
    }

    private static string ReplaceInText(string text, string findText, string replaceText, bool wholeWord, bool caseSensitive, bool exactMatch)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(findText))
            return text;

        StringComparison cmp = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (exactMatch)
            return string.Equals(text, findText, cmp) ? replaceText : text;

        if (!wholeWord)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            int index = 0;
            while (true)
            {
                int found = text.IndexOf(findText, index, cmp);
                if (found < 0)
                {
                    sb.Append(text, index, text.Length - index);
                    break;
                }
                sb.Append(text, index, found - index);
                sb.Append(replaceText);
                index = found + findText.Length;
            }
            return sb.ToString();
        }
        else
        {
            var sb = new System.Text.StringBuilder(text.Length);
            int index = 0;
            while (true)
            {
                int found = text.IndexOf(findText, index, cmp);
                if (found < 0)
                {
                    sb.Append(text, index, text.Length - index);
                    break;
                }

                bool wordStart = found == 0 || !char.IsLetterOrDigit(text[found - 1]);
                bool wordEnd = found + findText.Length >= text.Length
                                 || !char.IsLetterOrDigit(text[found + findText.Length]);

                if (wordStart && wordEnd)
                {
                    sb.Append(text, index, found - index);
                    sb.Append(replaceText);
                    index = found + findText.Length;
                }
                else
                {
                    sb.Append(text, index, found - index + 1);
                    index = found + 1;
                }
            }
            return sb.ToString();
        }
    }

    private void Replace_Click(object sender, RoutedEventArgs e) => OpenReplaceWindow();
    #endregion

    #region Drag & Drop
    private void Window_DragEnter(object sender, DragEventArgs e)
        => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
        var validExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json" };
        var allFiles = CollectFilesFromDropped(dropped, validExtensions);
        var validFiles = allFiles
            .Where(f => FindTabByPath(f) == null)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (validFiles.Count == 0)
            return;

        if (validFiles.Count > 20)
        {
            var confirm = MessageBox.Show(this, $"Знайдено {validFiles.Count} {PluralizationHelper.GetFilesWord(validFiles.Count)}. Відкрити всі?", "Підтвердження", MessageBoxButton.YesNo);

            if (confirm != MessageBoxResult.Yes)
                return;
        }

        foreach (var path in dropped.Where(Directory.Exists))
            _recentFoldersManager.Add(path);

        var versionSession = new FolderVersionSession { IsMultiFile = validFiles.Count > 1 };

        FileTabState firstTarget = string.IsNullOrEmpty(_activeTab?.FilePath)
            ? _activeTab
            : FindOrCreateEmptyTab(activate: false);

        var droppedFilesDirectly = new HashSet<string>(dropped.Where(File.Exists), StringComparer.OrdinalIgnoreCase);

        await LoadFileIntoTab(firstTarget, validFiles[0], versionSession, isBatchLoad: true);
        if (droppedFilesDirectly.Contains(validFiles[0]))
            _recentFilesManager.Add(validFiles[0]);

        var lastTarget = firstTarget;

        for (int i = 1; i < validFiles.Count; i++)
        {
            if (versionSession.Cancelled)
                break;

            lastTarget = CreateNewTab(activate: false);
            await LoadFileIntoTab(lastTarget, validFiles[i], versionSession, isBatchLoad: true);

            if (droppedFilesDirectly.Contains(validFiles[i]))
                _recentFilesManager.Add(validFiles[i]);
        }

        _fileTreeManager?.FinalizeBatchAdd();
        UpdateRecentFilesMenu();
        UpdateRecentFoldersMenu();

        var tabToActivate = string.IsNullOrEmpty(lastTarget.FilePath) ? firstTarget : lastTarget;
        int idx = _tabs.IndexOf(tabToActivate);

        if (idx >= 0)
        {
            using (SuppressTabSwitch())
                FileTabs.SelectedIndex = idx;

            ActivateTab(tabToActivate);

            if (FileTabs.Items[idx] is TabItem newTabItem)
                newTabItem.Focus();
        }
    }

    private static List<string> CollectFilesFromDropped(string[] paths, HashSet<string> extensions)
    {
        var result = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                var folderFiles = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => extensions.Contains(Path.GetExtension(f)))
                    .OrderBy(f => f);
                result.AddRange(folderFiles);
            }
            else if (File.Exists(path) && extensions.Contains(Path.GetExtension(path)))
            {
                result.Add(path);
            }
        }
        return result;
    }
    #endregion

    #region Undo/Redo
    private Dictionary<int, string> SnapshotTranslations()
    {
        var snap = new Dictionary<int, string>(Tab_Rows?.Count ?? 0);
        if (Tab_Rows != null)
            foreach (var row in Tab_Rows) snap[row.Index] = row.Translation ?? "";
        return snap;
    }

    private UndoRedoAction BuildUndoAction(string description, Dictionary<int, string> before)
    {
        var changes = new List<TranslationSnapshot>();
        if (Tab_Rows != null)
            foreach (var row in Tab_Rows)
                if (before.TryGetValue(row.Index, out var oldVal) && (row.Translation ?? "") != oldVal)
                    changes.Add(new TranslationSnapshot(row.Index, oldVal, row.Translation ?? ""));
        return new UndoRedoAction { Description = description, Changes = changes };
    }

    private async Task ApplyUndoRedo(UndoRedoAction action, bool isUndo)
    {
        if (Tab_Rows == null)
            return;

        foreach (var change in action.Changes)
        {
            var row = Tab_Rows.FirstOrDefault(r => r.Index == change.RowIndex);

            if (row == null)
                continue;

            string targetValue = isUndo ? change.OldTranslation : change.NewTranslation;

            if ((row.Translation ?? "") == targetValue)
                continue;

            row.Translation = targetValue;
            row.IsModified = targetValue != (row.Text ?? "");

            if (row.OriginalStringData?.Count > 1)
                row.OriginalStringData[1] = targetValue;

            SyncEditorIfSelected(row);
        }

        await CalculateOriginalStatsAsync(_activeTab);
        CheckIfHasChanges();
        DataGridMain.Items.Refresh();
        UpdateUndoRedoMenuItems();
        UpdateGridFilterUI();
    }

    private void UpdateUndoRedoMenuItems()
    {
        UndoMenuItem.IsEnabled = Tab_Undo?.CanUndo ?? false;
        RedoMenuItem.IsEnabled = Tab_Undo?.CanRedo ?? false;
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        var action = Tab_Undo?.Undo();
        if (action != null) await ApplyUndoRedo(action, isUndo: true);
    }

    private async void Redo_Click(object sender, RoutedEventArgs e)
    {
        var action = Tab_Undo?.Redo();
        if (action != null) await ApplyUndoRedo(action, isUndo: false);
    }

    private static Dictionary<int, string> SnapshotTranslationsFor(FileTabState tab)
    {
        var snap = new Dictionary<int, string>(tab.DataRows?.Count ?? 0);
        if (tab.DataRows != null)
            foreach (var row in tab.DataRows) snap[row.Index] = row.Translation ?? "";
        return snap;
    }

    private static UndoRedoAction BuildUndoActionFor(FileTabState tab, string description, Dictionary<int, string> before)
    {
        var changes = new List<TranslationSnapshot>();
        if (tab.DataRows != null)
            foreach (var row in tab.DataRows)
                if (before.TryGetValue(row.Index, out var oldVal) && (row.Translation ?? "") != oldVal)
                    changes.Add(new TranslationSnapshot(row.Index, oldVal, row.Translation ?? ""));
        return new UndoRedoAction { Description = description, Changes = changes };
    }
    #endregion

    #region Grid Filtering
    private bool MatchesGridFilter(DataGridItem item) => MatchesGridFilterFor(Tab_Filter, item);

    private static bool MatchesGridFilterFor(GridRowFilter filter, DataGridItem item) => filter switch
    {
        GridRowFilter.None => true,
        GridRowFilter.Untranslated => (item.Translation ?? "") == (item.Text ?? ""),
        GridRowFilter.Modified => item.IsModified,
        GridRowFilter.NeedsReview => item.Status == RowStatus.NeedsReview,
        GridRowFilter.Approved => item.Status == RowStatus.Approved,
        GridRowFilter.HideApproved => item.Status != RowStatus.Approved,
        GridRowFilter.GroupByID => true,
        GridRowFilter.GroupByText => true,
        GridRowFilter.GroupByTranslation => true,
        GridRowFilter.SortByOriginalWordCount => true,
        GridRowFilter.SortByTranslationWordCount => true,
        GridRowFilter.SpellCheck => SpellCheckService.HasSpellingErrors(item.Translation),
        GridRowFilter.Glossary => item.HasGlossaryMatch,
        GridRowFilter.TranslationMemory => item.HasTranslationMemoryMatch,
        GridRowFilter.TranslationMemoryUnmodified => item.HasTranslationMemoryMatch && !item.IsModified,
        GridRowFilter.CommonErrors => CommonErrorChecker.HasCommonErrors(item.Text, item.Translation),
        _ => true
    };

    private bool GridRowFilterPredicate(object obj)
        => obj is DataGridItem item && MatchesGridFilter(item);

    private void ApplyGridFilter(GridRowFilter filter)
    {
        if (_activeTab == null) return;
        ApplyGridFilterToTab(_activeTab, filter);
        UpdateGridFilterUI();
        UpdateStatsUI();
    }

    private void ApplyGridFilterToTab(FileTabState tab, GridRowFilter filter)
    {
        if (tab?.DataGridView == null) return;

        tab.ActiveGridFilter = filter;

        using (tab.DataGridView.DeferRefresh())
        {
            tab.DataGridView.SortDescriptions.Clear();

            switch (filter)
            {
                case GridRowFilter.None:
                    tab.DataGridView.Filter = null;
                    break;
                case GridRowFilter.GroupByID:
                    tab.DataGridView.Filter = null;
                    tab.DataGridView.SortDescriptions.Add(new SortDescription(nameof(DataGridItem.ID), ListSortDirection.Ascending));
                    break;
                case GridRowFilter.GroupByText:
                    tab.DataGridView.Filter = null;
                    tab.DataGridView.SortDescriptions.Add(new SortDescription(nameof(DataGridItem.Text), ListSortDirection.Ascending));
                    break;
                case GridRowFilter.GroupByTranslation:
                    tab.DataGridView.Filter = null;
                    tab.DataGridView.SortDescriptions.Add(new SortDescription(nameof(DataGridItem.Translation), ListSortDirection.Ascending));
                    break;
                case GridRowFilter.SortByOriginalWordCount:
                    tab.DataGridView.Filter = null;
                    tab.DataGridView.SortDescriptions.Add(new SortDescription(nameof(DataGridItem.OriginalWordCount), ListSortDirection.Descending));
                    break;
                case GridRowFilter.SortByTranslationWordCount:
                    tab.DataGridView.Filter = null;
                    tab.DataGridView.SortDescriptions.Add(new SortDescription(nameof(DataGridItem.TranslationWordCount), ListSortDirection.Descending));
                    break;
                default:
                    tab.DataGridView.Filter = obj => obj is DataGridItem item && MatchesGridFilterFor(filter, item);
                    break;
            }
        }
    }

    private void UpdateGridFilterUI()
    {
        if (_activeTab == null) return;

        _suppressFilterComboBox = true;
        FilterComboBox.SelectedIndex = Tab_Filter switch
        {
            GridRowFilter.Modified => 1,
            GridRowFilter.NeedsReview => 2,
            GridRowFilter.Approved => 3,
            GridRowFilter.HideApproved => 4,
            GridRowFilter.Untranslated => 5,
            GridRowFilter.GroupByID => 6,
            GridRowFilter.GroupByText => 7,
            GridRowFilter.GroupByTranslation => 8,
            GridRowFilter.SortByOriginalWordCount => 9,
            GridRowFilter.SortByTranslationWordCount => 10,
            GridRowFilter.SpellCheck => 11,
            GridRowFilter.Glossary => 12,
            GridRowFilter.TranslationMemory => 13,
            GridRowFilter.TranslationMemoryUnmodified => 14,
            GridRowFilter.CommonErrors => 15,
            _ => 0
        };

        _suppressFilterComboBox = false;
    }

    private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterComboBox || _activeTab == null) return;
        if (FilterComboBox.SelectedItem is not ComboBoxItem item) return;

        var filter = item.Tag.ToString() switch
        {
            "Modified" => GridRowFilter.Modified,
            "NeedsReview" => GridRowFilter.NeedsReview,
            "Approved" => GridRowFilter.Approved,
            "HideApproved" => GridRowFilter.HideApproved,
            "Untranslated" => GridRowFilter.Untranslated,
            "GroupByID" => GridRowFilter.GroupByID,
            "GroupByText" => GridRowFilter.GroupByText,
            "GroupByTranslation" => GridRowFilter.GroupByTranslation,
            "SortByOriginalWordCount" => GridRowFilter.SortByOriginalWordCount,
            "SortByTranslationWordCount" => GridRowFilter.SortByTranslationWordCount,
            "SpellCheck" => GridRowFilter.SpellCheck,
            "Glossary" => GridRowFilter.Glossary,
            "TranslationMemory" => GridRowFilter.TranslationMemory,
            "TranslationMemoryUnmodified" => GridRowFilter.TranslationMemoryUnmodified,
            "CommonErrors" => GridRowFilter.CommonErrors,
            _ => GridRowFilter.None
        };

        if (filter == GridRowFilter.SpellCheck && !SpellCheckService.TryLoadDictionary())
        {
            MessageBox.Show(this, "Словники uk_UA.dic та uk_UA.aff не знайдено.\nПрочитайте документацію у пункті меню ? -> Про програму.", "Перевірка орфографії", MessageBoxButton.OK);
            _suppressFilterComboBox = true;
            FilterComboBox.SelectedIndex = 0;
            _suppressFilterComboBox = false;
            return;
        }

        ApplyGridFilter(filter);
    }

    private static bool IsSortOnlyFilter(GridRowFilter filter) => filter switch
    {
        GridRowFilter.None => true,
        GridRowFilter.GroupByID => true,
        GridRowFilter.GroupByText => true,
        GridRowFilter.GroupByTranslation => true,
        GridRowFilter.SortByOriginalWordCount => true,
        GridRowFilter.SortByTranslationWordCount => true,
        _ => false
    };

    private int GetVisibleRowsCount()
    {
        if (_activeTab == null || Tab_View == null) return -1;

        if (!IsSortOnlyFilter(Tab_Filter))
            return (Tab_View as IList)?.Count ?? Tab_View.Cast<DataGridItem>().Count();

        return -1;
    }

    private void OpenGlobalFilterWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_globalFilterWindow != null)
        {
            _globalFilterWindow.Activate();
            return;
        }

        _globalFilterWindow = new GlobalFilterWindow { Owner = this };
        _globalFilterWindow.Closed += (s, _) => _globalFilterWindow = null;
        _globalFilterWindow.Show();
    }

    public void ApplyGlobalGridFilter(GridRowFilter filter)
    {
        if (filter == GridRowFilter.SpellCheck && !SpellCheckService.TryLoadDictionary())
        {
            MessageBox.Show(this, "Словники uk_UA.dic та uk_UA.aff не знайдено.\nПрочитайте документацію у пункті меню ? -> Про програму.", "Перевірка орфографії", MessageBoxButton.OK);
            return;
        }

        foreach (var tab in _tabs)
            ApplyGridFilterToTab(tab, filter);

        UpdateGridFilterUI();
        UpdateStatsUI();
    }
    #endregion

    #region Spell Check UI
    private void UpdateErrorsPanelVisibility()
    {
        ErrorsPanel.Visibility = (SpellErrorPanel.Visibility == Visibility.Visible
                                || CommonErrorsPanel.Visibility == Visibility.Visible)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateSpellErrorPanel(DataGridItem item)
    {
        if (!SpellCheckService.IsAvailable || item == null)
        {
            SpellErrorPanel.Visibility = Visibility.Collapsed;
            UpdateErrorsPanelVisibility();
            return;
        }

        var errors = SpellCheckService.GetMisspelledWords(item.Translation)
                                      .Distinct()
                                      .ToList();

        SpellErrorsBlock.Text = string.Join(", ", errors);
        SpellErrorPanel.Visibility = errors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateErrorsPanelVisibility();
    }

    private void UpdateCommonErrorsPanel(DataGridItem item)
        {
        if (item == null)
        {
            CommonErrorsPanel.Visibility = Visibility.Collapsed;
            UpdateErrorsPanelVisibility();
            return;
        }

        var errors = CommonErrorChecker.GetCommonErrors(item.Text, item.Translation);

        CommonErrorsBlock.Text = string.Join(". ", errors);
        CommonErrorsPanel.Visibility = errors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateErrorsPanelVisibility();
    }

    private void EditTextBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _spellContextMenuSelectedText = EditTextBox.SelectedText.Trim();
    }

    private void EditTextBoxContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        bool show = SpellCheckService.IsAvailable && !string.IsNullOrWhiteSpace(_spellContextMenuSelectedText);
        AddToSpellIgnoreMenuItem.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        if (show)
            AddToSpellIgnoreMenuItem.Header = $"Додати «{_spellContextMenuSelectedText}» до виключень";

        AddToGlossaryMenuItemEditBox.Header = string.IsNullOrWhiteSpace(_spellContextMenuSelectedText)
            ? "Додати до глосарію"
            : $"Додати «{_spellContextMenuSelectedText}» до глосарію";

        EditFontSize8MenuItem.FontWeight = SettingsManager.EditTextFontSize == 8 ? FontWeights.Bold : FontWeights.Normal;
        EditFontSize9MenuItem.FontWeight = SettingsManager.EditTextFontSize == 9 ? FontWeights.Bold : FontWeights.Normal;
        EditFontSize10MenuItem.FontWeight = SettingsManager.EditTextFontSize == 10 ? FontWeights.Bold : FontWeights.Normal;
        EditFontSize11MenuItem.FontWeight = SettingsManager.EditTextFontSize == 11 ? FontWeights.Bold : FontWeights.Normal;
        EditFontSize12MenuItem.FontWeight = SettingsManager.EditTextFontSize == 12 ? FontWeights.Bold : FontWeights.Normal;
        EditFontSize13MenuItem.FontWeight = SettingsManager.EditTextFontSize == 13 ? FontWeights.Bold : FontWeights.Normal;
        EditFontSize14MenuItem.FontWeight = SettingsManager.EditTextFontSize == 14 ? FontWeights.Bold : FontWeights.Normal;
    }

    private void EditFontSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string tag && int.TryParse(tag, out var size))
        {
            SettingsManager.SetEditTextFontSize(size);
            ApplyEditTextBoxSettings();
        }
    }

    private void AddToSpellIgnore_Click(object sender, RoutedEventArgs e)
    {
        string word = _spellContextMenuSelectedText;
        if (string.IsNullOrWhiteSpace(word)) return;

        SpellCheckService.AddIgnoredWord(word);

        if (DataGridMain.SelectedItem is DataGridItem item)
            UpdateSpellErrorPanel(item);

        if (Tab_Filter == GridRowFilter.SpellCheck)
        {
            Tab_View?.Refresh();
            UpdateStatsUI();
        }
    }

    #endregion

    #region Row Status
    private async void SetStatusForSelected(RowStatus status)
    {
        var selected = DataGridMain.SelectedItems.Cast<DataGridItem>().ToList();
        if (selected.Count == 0) return;
        foreach (var item in selected) item.Status = status;
        MarkAsModified();
        await CalculateOriginalStatsAsync(_activeTab);
    }

    private void StatusNeedsReview_Click(object sender, RoutedEventArgs e) => SetStatusForSelected(RowStatus.NeedsReview);
    private void StatusApproved_Click(object sender, RoutedEventArgs e) => SetStatusForSelected(RowStatus.Approved);
    private void StatusClear_Click(object sender, RoutedEventArgs e) => SetStatusForSelected(RowStatus.None);

    private async void StatusIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not DataGridItem clickedItem)
            return;

        RowStatus oldStatus = clickedItem.Status;

        RowStatus newStatus = oldStatus switch
        {
            RowStatus.NeedsReview => RowStatus.Approved,
            RowStatus.Approved => RowStatus.NeedsReview,
            _ => oldStatus
        };

        if (newStatus == oldStatus)
            return;

        var selectedItems = DataGridMain.SelectedItems.Cast<DataGridItem>().ToList();
        List<DataGridItem> targets;

        if (selectedItems.Count > 1 && selectedItems.Contains(clickedItem))
        {
            targets = selectedItems.Where(i => i.Status == oldStatus).ToList();
        }
        else
        {
            targets = new List<DataGridItem> { clickedItem };
        }

        foreach (var item in targets)
            item.Status = newStatus;

        MarkAsModified();
        await CalculateOriginalStatsAsync(_activeTab);
    }

    private void LoadStatuses_Click(object sender, RoutedEventArgs e)
    {
        if (Tab_Rows == null || Tab_Rows.Count == 0)
            return;

        var ofd = new OpenFileDialog { Filter = "Файл статусів|*.STATUS", Title = "Завантажити статуси" };

        if (ofd.ShowDialog() != true)
            return;

        SafeExecute(async () =>
        {
            bool useIndexKeys = RowStatusManager.ShouldUseIndexKeys(_activeTab.CurrentFormat);
            RowStatusManager.LoadFromFile(ofd.FileName, Tab_Rows, useIndexKeys);
            DataGridMain.Items.Refresh();
            UpdateGridFilterUI();
            CheckIfHasChanges();
            await CalculateOriginalStatsAsync(_activeTab);
        }, "Помилка завантаження статусів");
    }
    #endregion

    #region Recent Files
    private void UpdateRecentFilesMenu()
    {
        RecentFilesMenuItem.Items.Clear();

        if (_recentFilesManager.Items.Count == 0)
        {
            RecentFilesMenuItem.Items.Add(new MenuItem { Header = "(порожньо)", IsEnabled = false });
        }
        else
        {
            foreach (var file in _recentFilesManager.Items)
            {
                var menuItem = new MenuItem
                {
                    Header = Path.GetFileName(file).Replace("_", "__"),
                    ToolTip = file,
                    Tag = file
                };

                menuItem.Click += RecentFileMenuItem_Click;
                RecentFilesMenuItem.Items.Add(menuItem);
            }
        }
    }

    private void RecentFileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is string filePath)
        {
            if (File.Exists(filePath))
            {
                if (SwitchToExistingTab(filePath))
                    return;

                FileTabState target = string.IsNullOrEmpty(_activeTab?.FilePath)
                    ? _activeTab
                    : FindOrCreateEmptyTab(activate: true);

                SafeExecute(async () =>
                {
                    await LoadFileIntoTab(target, filePath);
                    _recentFilesManager.Add(filePath);
                    UpdateRecentFilesMenu();
                }, "Помилка відкриття файлу");
            }
            else
            {
                MessageBox.Show(this, $"Файл не знайдено:\n{filePath}", "Помилка", MessageBoxButton.OK);
                _recentFilesManager.Remove(filePath);
                UpdateRecentFilesMenu();
            }
        }
    }

    private void ClearRecentFiles_Click(object sender, RoutedEventArgs e)
    {
        _recentFilesManager.Clear();
        UpdateRecentFilesMenu();
    }
    #endregion

    #region Recent Folders
    private void UpdateRecentFoldersMenu()
    {
        RecentFoldersMenuItem.Items.Clear();

        if (_recentFoldersManager.Items.Count == 0)
        {
            RecentFoldersMenuItem.Items.Add(new MenuItem { Header = "(порожньо)", IsEnabled = false });
        }
        else
        {
            foreach (var folder in _recentFoldersManager.Items)
            {
                var menuItem = new MenuItem
                {
                    Header = GetShortPath(folder, 3).Replace("_", "__"),
                    ToolTip = folder,
                    Tag = folder
                };

                menuItem.Click += RecentFolderMenuItem_Click;
                RecentFoldersMenuItem.Items.Add(menuItem);
            }
        }
    }

    private void RecentFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string folderPath)
            return;

        if (!Directory.Exists(folderPath))
        {
            MessageBox.Show(this, $"{folderPath}", "Теку не знайдено", MessageBoxButton.OK);
            _recentFoldersManager.Remove(folderPath);
            UpdateRecentFoldersMenu();
            return;
        }

        SafeExecute(async () =>
        {
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json" };
            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f)
                .ToList();

            if (files.Count == 0)
            {
                MessageBox.Show(this, "У вибраній теці не знайдено файлів .json.", "Нічого не знайдено", MessageBoxButton.OK);
                return;
            }

            var filesToOpen = files.Where(f => FindTabByPath(f) == null).ToList();

            if (filesToOpen.Count == 0)
                return;

            if (filesToOpen.Count > 20)
            {
                var confirm = MessageBox.Show(this, $"Знайдено {filesToOpen.Count} {PluralizationHelper.GetFilesWord(filesToOpen.Count)}. Відкрити всі?", "Підтвердження", MessageBoxButton.YesNo);

                if (confirm != MessageBoxResult.Yes)
                    return;
            }

            _recentFoldersManager.Add(folderPath);
            var versionSession = new FolderVersionSession { IsMultiFile = filesToOpen.Count > 1 };

            FileTabState firstTarget = string.IsNullOrEmpty(_activeTab?.FilePath)
                ? _activeTab
                : FindOrCreateEmptyTab(activate: false);

            await LoadFileIntoTab(firstTarget, filesToOpen[0], versionSession, isBatchLoad: true);
            var lastTarget = firstTarget;

            for (int i = 1; i < filesToOpen.Count; i++)
            {
                if (versionSession.Cancelled) break;
                lastTarget = CreateNewTab(activate: false);
                await LoadFileIntoTab(lastTarget, filesToOpen[i], versionSession, isBatchLoad: true);
            }

            _fileTreeManager?.FinalizeBatchAdd();

            if (!string.IsNullOrEmpty(lastTarget.FilePath))
            {
                int idx = _tabs.IndexOf(lastTarget);

                using (SuppressTabSwitch())
                    FileTabs.SelectedIndex = idx;

                ActivateTab(lastTarget);
            }

            UpdateRecentFoldersMenu();
        }, "Помилка відкриття теки");
    }
    #endregion

    #region Search
    private void TxtClear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is TextBox tb)
        {
            tb.Clear();
            tb.Focus();
        }
    }

    private void SearchComboBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) SearchText(); }
    private void SearchButton_Click(object sender, RoutedEventArgs e) => SearchText();

    private void SearchModeToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (Tab_Search != null)
            Tab_Search.IsIdMode = true;

        SearchModeToggle.Content = "ID";
    }

    private void SearchModeToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (Tab_Search != null)
            Tab_Search.IsIdMode = false;

        SearchModeToggle.Content = "Текст";
    }

    private void ChkMutualExclusive_Changed(object sender, RoutedEventArgs e)
    {
        if (WholeWordCheckBox.IsChecked == true)
            ExactMatchCheckBox.IsEnabled = false;
        else if (ExactMatchCheckBox.IsChecked == true)
            WholeWordCheckBox.IsEnabled = false;
        else
        {
            WholeWordCheckBox.IsEnabled = true;
            ExactMatchCheckBox.IsEnabled = true;
        }
    }

    private void SearchText()
    {
        if (_activeTab == null || Tab_Rows == null)
            return;
        var query = SearchComboBox.Text;

        if (string.IsNullOrEmpty(query) || query == " ")
            return;

        _recentSearchesManager.Add(query);
        RefreshSearchHistory();
        Tab_Search.SearchQuery = query;

        Tab_Search.SearchComparison = CaseSensitiveCheckBox.IsChecked == true
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        bool searchById = Tab_Search.SearchMode == SearchMode.ID;
        bool wholeWord = WholeWordCheckBox.IsChecked == true;
        bool exactMatch = ExactMatchCheckBox.IsChecked == true;
        Tab_Search.WasWholeWord = wholeWord;
        Tab_Search.WasExactMatch = exactMatch;
        var matches = new List<int>();
        Tab_Search.OriginalMatchColumnTypes.Clear();
        _searchWasFilteredSearch = Tab_Filter != GridRowFilter.None;

        for (int i = 0; i < Tab_Rows.Count; i++)
        {
            var entry = Tab_Rows[i];

            if (_searchWasFilteredSearch && !MatchesGridFilter(entry))
                continue;

            bool isMatch;
            bool foundInText = false, foundInTranslation = false;

            if (searchById)
            {
                if (exactMatch) isMatch = entry.ID?.Equals(query, Tab_Search.SearchComparison) == true;
                else if (wholeWord) isMatch = IsWholeWordMatch(entry.ID ?? "", query, Tab_Search.SearchComparison);
                else isMatch = entry.ID?.IndexOf(query, Tab_Search.SearchComparison) >= 0;
            }
            else
            {
                if (exactMatch)
                {
                    foundInText = entry.Text?.Equals(query, Tab_Search.SearchComparison) == true;
                    foundInTranslation = entry.Translation?.Equals(query, Tab_Search.SearchComparison) == true;
                }
                else if (wholeWord)
                {
                    foundInText = IsWholeWordMatch(entry.Text ?? "", query, Tab_Search.SearchComparison);
                    foundInTranslation = IsWholeWordMatch(entry.Translation ?? "", query, Tab_Search.SearchComparison);
                }
                else
                {
                    foundInText = entry.Text?.IndexOf(query, Tab_Search.SearchComparison) >= 0;
                    foundInTranslation = entry.Translation?.IndexOf(query, Tab_Search.SearchComparison) >= 0;
                }
                isMatch = foundInText || foundInTranslation;
            }

            if (isMatch)
            {
                matches.Add(i);
                if (!searchById)
                {
                    string ct = (foundInText && foundInTranslation) ? "both"
                              : foundInTranslation ? "translation"
                              : "original";
                    Tab_Search.OriginalMatchColumnTypes[i] = ct;
                }
            }
        }

        Tab_Search.OriginalMatchIndices = matches.ToArray();
        Tab_Search.MatchIndices = Tab_Search.OriginalMatchIndices;

        if (Tab_Search.MatchIndices.Length > 0)
        {
            Tab_Search.CurrentMatch = 0;
            PopulateSearchResults(query, Tab_Search.SearchComparison);
            ShowSearchPanel(); HighlightMatch();
            ToggleSearchResultsButton.IsEnabled = true;
        }
        else
        {
            ShowSearchNotFound(); HideSearchPanel();
            ToggleSearchResultsButton.IsEnabled = false;
        }

        UpdateFilterPanelVisibility();
    }

    private static bool IsWholeWordMatch(string text, string searchText, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(text)) return
                false;

        int index = 0;

        while ((index = text.IndexOf(searchText, index, comparison)) != -1)
        {
            bool isWordStart = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            bool isWordEnd = index + searchText.Length >= text.Length || !char.IsLetterOrDigit(text[index + searchText.Length]);

            if (isWordStart && isWordEnd)
                return true;

            index++;
        }

        return false;
    }

    private void PopulateSearchResults(string query, StringComparison comparison)
    {
        if (Tab_Search == null)
            return;

        Tab_Search.CurrentSearchFilter = SearchFilter.All;
        UpdateFilterButtonStyles();
        SearchResultsList.ItemsSource = null;
        Tab_Search.SearchResults.Clear();

        foreach (var index in Tab_Search.MatchIndices)
        {
            var entry = Tab_Rows[index];
            Tab_Search.SearchResults.Add(CreateSearchResultItem(entry, index, query, comparison));
        }

        SearchResultsList.ItemsSource = Tab_Search.SearchResults;
    }

    private SearchResultItem CreateSearchResultItem(DataGridItem entry, int index, string query, StringComparison comparison)
    {
        bool searchById = Tab_Search?.SearchMode == SearchMode.ID;
        string columnType = "";

        if (!searchById)
        {
            Tab_Search.OriginalMatchColumnTypes.TryGetValue(index, out string savedCt);
            columnType = savedCt switch
            {
                "both" => " — В обох",
                "translation" => " — У перекладі",
                "original" => " — В оригіналі",
                _ => ""
            };
        }

        return new SearchResultItem
        {
            Number = entry.Index,
            Term = entry.ID ?? "",
            Preview = GetSearchPreview(entry, query, comparison),
            TermIndex = index,
            SearchQuery = query,
            Comparison = comparison,
            ColumnType = columnType,
            ShowTerm = !searchById
        };
    }

    private string GetSearchPreview(DataGridItem entry, string query, StringComparison comparison)
    {
        string text = Tab_Search?.SearchMode == SearchMode.ID
            ? (entry.ID ?? "")
            : (Tab_Search.OriginalMatchColumnTypes.TryGetValue(entry.Index, out string ct) && ct != "original"
                ? entry.Translation
                : (entry.Text ?? ""));

        const int maxLength = 125;
        int queryIndex = text.IndexOf(query, comparison);

        if (queryIndex >= 0 && text.Length > maxLength)
        {
            int start = Math.Max(0, queryIndex - 30);
            int end = Math.Min(text.Length, start + maxLength);
            if (end == text.Length && end - start < maxLength) start = Math.Max(0, end - maxLength);

            string preview = text[start..end];
            if (start > 0) preview = "…" + preview;
            if (end < text.Length) preview += "…";
            return preview;
        }

        return text.Length > maxLength ? $"{text[..maxLength]}…" : text;
    }

    private void ShowSearchPanel() => SearchResultsPanel.Visibility = Visibility.Visible;
    private void HideSearchPanel() => SearchResultsPanel.Visibility = Visibility.Collapsed;

    private void ShowSearchNotFound()
    {
        SearchStatusLabel.Content = "";

        if (_searchNotFoundTimer != null)
        {
            _searchNotFoundTimer.Stop();
            _searchNotFoundTimer.Tick -= OnSearchNotFoundTick;
            _searchNotFoundTimer = null;
        }

        SearchComboBox.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(128, 255, 100, 100));

        _searchNotFoundTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };

        _searchNotFoundTimer.Tick += OnSearchNotFoundTick;
        _searchNotFoundTimer.Start();
    }

    private void OnSearchNotFoundTick(object sender, EventArgs e)
    {
        if (_searchNotFoundTimer == null || !IsLoaded)
        {
            if (_searchNotFoundTimer != null)
            {
                _searchNotFoundTimer.Stop();
                _searchNotFoundTimer = null;
            }
            return;
        }

        SearchComboBox.ClearValue(ComboBox.BackgroundProperty);

        if (SearchComboBox.Template?.FindName("PART_EditableTextBox", SearchComboBox) is TextBox eb)
            eb.CaretIndex = eb.Text.Length;

        _searchNotFoundTimer.Stop();
        _searchNotFoundTimer = null;
    }

    private void ToggleSearchResultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (SearchResultsPanel.Visibility == Visibility.Visible)
            HideSearchPanel();
        else
            ShowSearchPanel();
    }

    private void HighlightMatch()
    {
        if (Tab_Search == null || Tab_Search.CurrentMatch < 0 || Tab_Search.CurrentMatch >= Tab_Search.MatchIndices.Length)
            return;

        var rowIndex = Tab_Search.MatchIndices[Tab_Search.CurrentMatch];
        var targetItem = Tab_Rows[rowIndex];

        bool filteredAway = Tab_Filter != GridRowFilter.None && !(Tab_View?.Contains(targetItem) ?? true);

        if (filteredAway && !_searchWasFilteredSearch)
            ApplyGridFilter(GridRowFilter.None);

        SearchStatusLabel.Content = $"{Tab_Search.CurrentMatch + 1}/{Tab_Search.MatchIndices.Length}";

        if (_activeTab != null)
            _activeTab.SearchStatusText = SearchStatusLabel.Content?.ToString() ?? "";

        DataGridMain.SelectedItem = targetItem;
        DataGridMain.ScrollIntoView(targetItem);

        if (SearchResultsPanel.Visibility == Visibility.Visible)
        {
            for (int i = 0; i < Tab_Search.SearchResults.Count; i++)
            {
                if (Tab_Search.SearchResults[i].TermIndex == rowIndex)
                {
                    SearchResultsList.SelectedIndex = i;
                    SearchResultsList.ScrollIntoView(Tab_Search.SearchResults[i]);
                    break;
                }
            }
        }
    }

    private void SearchResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tab_Search == null) return;
        if (SearchResultsList.SelectedItem is SearchResultItem item)
        {
            for (int i = 0; i < Tab_Search.MatchIndices.Length; i++)
            {
                if (Tab_Search.MatchIndices[i] == item.TermIndex)
                {
                    Tab_Search.CurrentMatch = i;
                    HighlightMatch();
                    break;
                }
            }
        }
    }

    private void FilterBothButton_Click(object sender, RoutedEventArgs e) => ApplySearchFilter(SearchFilter.Both);
    private void FilterOriginalButton_Click(object sender, RoutedEventArgs e) => ApplySearchFilter(SearchFilter.Original);
    private void FilterTranslationButton_Click(object sender, RoutedEventArgs e) => ApplySearchFilter(SearchFilter.Translation);
    private void FilterResetButton_Click(object sender, RoutedEventArgs e) => ApplySearchFilter(SearchFilter.All);

    private void ApplySearchFilter(SearchFilter filter)
    {
        if (Tab_Search == null)
            return;

        Tab_Search.CurrentSearchFilter = filter;
        UpdateFilterButtonStyles();
        var filteredResults = GetFilteredSearchResults();
        Tab_Search.SearchResults.Clear();
        var filteredIndices = new List<int>();

        foreach (var item in filteredResults)
        {
            Tab_Search.SearchResults.Add(item);
            filteredIndices.Add(item.TermIndex);
        }

        Tab_Search.MatchIndices = filteredIndices.ToArray();

        if (Tab_Search.MatchIndices.Length > 0)
        {
            Tab_Search.CurrentMatch = 0;

            if (SearchResultsList.Items.Count > 0)
                SearchResultsList.SelectedIndex = 0;
            HighlightMatch();
        }
        else
        {
            Tab_Search.CurrentMatch = -1;
            SearchStatusLabel.Content = "";

            if (_activeTab != null)
                _activeTab.SearchStatusText = "";
        }

        ToggleSearchResultsButton.IsEnabled = Tab_Search.OriginalMatchIndices.Length > 0;
    }

    private List<SearchResultItem> GetFilteredSearchResults()
    {
        if (Tab_Search == null || Tab_Rows == null)
            return new();

        var query = SearchComboBox.Text;
        var comparison = Tab_Search.SearchComparison;
        bool searchById = Tab_Search.SearchMode == SearchMode.ID;
        var allResults = new List<SearchResultItem>();

        foreach (var index in Tab_Search.OriginalMatchIndices)
        {
            var entry = Tab_Rows[index];

            if (searchById)
            {
                if (Tab_Search.CurrentSearchFilter == SearchFilter.All)
                    allResults.Add(CreateSearchResultItem(entry, index, query, comparison));
            }
            else
            {
                Tab_Search.OriginalMatchColumnTypes.TryGetValue(index, out string savedColumnType);

                bool shouldInclude = Tab_Search.CurrentSearchFilter switch
                {
                    SearchFilter.All => true,
                    SearchFilter.Both => savedColumnType == "both",
                    SearchFilter.Original => savedColumnType == "original",
                    SearchFilter.Translation => savedColumnType == "translation",
                    _ => false
                };

                if (shouldInclude)
                    allResults.Add(CreateSearchResultItem(entry, index, query, comparison));
            }
        }

        return allResults;
    }

    private void UpdateFilterButtonStyles()
    {
        FilterBothButton.FontWeight = FontWeights.Normal;
        FilterOriginalButton.FontWeight = FontWeights.Normal;
        FilterTranslationButton.FontWeight = FontWeights.Normal;
        FilterResetButton.FontWeight = FontWeights.Normal;

        if (Tab_Search == null)
            return;

        switch (Tab_Search.CurrentSearchFilter)
        {
            case SearchFilter.All: FilterResetButton.FontWeight = FontWeights.Bold; break;
            case SearchFilter.Both: FilterBothButton.FontWeight = FontWeights.Bold; break;
            case SearchFilter.Original: FilterOriginalButton.FontWeight = FontWeights.Bold; break;
            case SearchFilter.Translation: FilterTranslationButton.FontWeight = FontWeights.Bold; break;
        }
    }

    private void UpdateFilterPanelVisibility()
    {
        bool isIdMode = SearchModeToggle.IsChecked == true;
        FilterPanel.Visibility = isIdMode ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SearchComboBox_DropDownOpened(object sender, EventArgs e)
    {
        RefreshSearchHistory();
    }

    private void LoadSearchHistory() => RefreshSearchHistory();

    private void RefreshSearchHistory()
    {
        string currentText = SearchComboBox.Text;
        SearchComboBox.ItemsSource = null;
        SearchComboBox.ItemsSource = _recentSearchesManager.GetAll();
        SearchComboBox.Text = currentText;
    }

    private void SearchResultsList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Tab_Search == null)
            return;

        var item = SearchResultsList.SelectedItem as SearchResultItem;

        if (item == null)
            return;

        for (int i = 0; i < Tab_Search.MatchIndices.Length; i++)
        {
            if (Tab_Search.MatchIndices[i] == item.TermIndex)
            {
                Tab_Search.CurrentMatch = i;
                HighlightMatch();
                break;
            }
        }
    }
    #endregion

    #region Global Search
    private void GlobalSearch_Click(object sender, RoutedEventArgs e)
    => OpenGlobalSearchWindow();

    private void OpenGlobalSearchWindow()
    {
        if (_globalSearchWindow != null && _globalSearchWindow.IsLoaded)
        {
            if (_globalSearchWindow.WindowState == WindowState.Minimized)
                _globalSearchWindow.WindowState = WindowState.Normal;
            _globalSearchWindow.Activate();
            return;
        }

        _globalSearchWindow = new GlobalSearchWindow(
            getTabsFunc: () => _tabs.AsReadOnly(),
            navigateToRowAction: NavigateToRowInTab,
            recentSearchesManager: _recentSearchesManager,
            initialQuery: _lastGlobalSearchQuery)
        {
            Owner = this
        };

        _globalSearchWindow.Closed += (s, e) =>
        {
            _lastGlobalSearchQuery = _globalSearchWindow.LastQuery;
            this.Activate();
        };
        _globalSearchWindow.Show();
    }

    private void NavigateToRowInTab(FileTabState tab, int rowIndex)
    {
        if (tab == null) return;

        int tabIdx = _tabs.IndexOf(tab);
        if (tabIdx < 0) return;

        FlushEditTextBox();

        if (_activeTab != tab)
        {
            using (SuppressTabSwitch())
                FileTabs.SelectedIndex = tabIdx;

            ActivateTab(tab);
        }

        var targetRow = tab.DataRows.FirstOrDefault(r => r.Index == rowIndex);
        if (targetRow == null) return;

        if (tab.ActiveGridFilter != GridRowFilter.None
            && tab.DataGridView != null
            && !tab.DataGridView.Contains(targetRow))
        {
            ApplyGridFilter(GridRowFilter.None);
        }

        DataGridMain.SelectedItem = targetRow;
        DataGridMain.ScrollIntoView(targetRow);
        DataGridMain.Focus();
    }

    private void FlushEditTextBox()
    {
        if (DataGridMain.SelectedItem is DataGridItem item)
            item.Translation = EditTextBox.Text;
    }
    #endregion

    #region Glossary
    private void ContextMenu_AddToGlossary_Click(object sender, RoutedEventArgs e)
    {
        if (DataGridMain.SelectedItem is not DataGridItem row) return;

        string original = row.Text;
        string translation = row.Translation;

        if (string.IsNullOrWhiteSpace(original)) return;

        GlossaryManager.Instance.AddOrUpdate(original, translation);

        foreach (var t in _tabs)
            RefreshGlossaryHighlights(t);
    }

    private void OpenGlossaryWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_glossaryWindow != null)
        {
            _glossaryWindow.Activate();
            return;
        }

        _glossaryWindow = new GlossaryWindow { Owner = this };
        _glossaryWindow.Closed += (s, _) =>
        {
            _glossaryWindow = null;
            if (_activeTab != null)
                RefreshGlossaryHighlights(_activeTab);
        };
        _glossaryWindow.Show();
    }

    private static async void RefreshGlossaryHighlights(FileTabState tab)
    {
        var oldCts = tab.GlossaryCts;
        tab.GlossaryCts = new CancellationTokenSource();
        var token = tab.GlossaryCts.Token;

        oldCts?.Cancel();
        oldCts?.Dispose();

        var rows = tab.DataRows.ToList();
        var entries = GlossaryManager.Instance.Entries.ToList();

        var results = await Task.Run(() =>
        {
            var dict = new Dictionary<DataGridItem, bool>(rows.Count);
            foreach (var row in rows)
            {
                if (token.IsCancellationRequested) return null;
                dict[row] = HasAnyMatchSnapshot(row.Text, entries);
            }
            return dict;
        }, token);

        if (results == null || token.IsCancellationRequested) return;

        foreach (var (row, val) in results)
            row.HasGlossaryMatch = val;
    }

    private static bool HasAnyMatchSnapshot(string text, List<GlossaryEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Original)) continue;
            if (entry.GetMatchRegex().IsMatch(text))
                return true;
        }
        return false;
    }

    private void GlossarySuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        if (item.Tag is not (DataGridItem row, string matchedText, string translation)) return;

        string source = string.IsNullOrEmpty(row.Translation) || row.Translation == row.Text
            ? row.Text
            : row.Translation;

        string newText = GlossaryManager.ReplaceWholeWord(source, matchedText, translation);
        if (newText == row.Translation) return;

        string oldTranslation = row.Translation ?? "";
        row.Translation = newText;
        SyncEditorIfSelected(row);

        var tab = _activeTab;
        if (tab == null) return;

        tab.UndoRedo.Push(new UndoRedoAction
        {
            Description = $"Глосарій: «{matchedText}» → {translation}",
            Changes = [new TranslationSnapshot(row.Index, oldTranslation, newText)]
        });
        UpdateUndoRedoMenuItems();
        tab.HasUnsavedChanges = true;
        RefreshGlossaryHighlights(tab);
    }

    private void AddToGlossary_Click(object sender, RoutedEventArgs e)
    {
        if (DataGridMain.SelectedItem is not DataGridItem item)
            return;

        if (string.IsNullOrWhiteSpace(item.Text))
            return;

        string suggestedTranslation = !string.IsNullOrWhiteSpace(_spellContextMenuSelectedText)
            ? _spellContextMenuSelectedText
            : (item.Translation ?? "");

        var dlg = new AddToGlossaryDialog(item.Text, suggestedTranslation) { Owner = this };

        if (dlg.ShowDialog() != true)
            return;

        bool existed = GlossaryManager.Instance.AddOrUpdate(dlg.ResultOriginal, dlg.ResultTranslation);

        foreach (var t in _tabs)
            RefreshGlossaryHighlights(t);
    }
    #endregion

    #region Translation Memory
    private void TranslationMemorySuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item)
            return;

        if (item.Tag is not (DataGridItem row, string translation))
            return;

        if (translation == row.Translation)
            return;

        string oldTranslation = row.Translation ?? "";
        row.Translation = translation;
        SyncEditorIfSelected(row);
        var tab = _activeTab;

        if (tab == null)
            return;

        tab.UndoRedo.Push(new UndoRedoAction
        {
            Description = $"Пам’ять перекладів: «{translation}»",
            Changes = [new TranslationSnapshot(row.Index, oldTranslation, translation)]
        });

        UpdateUndoRedoMenuItems();
        tab.HasUnsavedChanges = true;
    }

    private static async void RefreshTranslationMemoryHighlights(FileTabState tab)
    {
        var oldCts = tab.TranslationMemoryCts;
        tab.TranslationMemoryCts = new CancellationTokenSource();
        var token = tab.TranslationMemoryCts.Token;

        oldCts?.Cancel();
        oldCts?.Dispose();

        var rows = tab.DataRows.ToList();
        var activeMemories = TranslationMemoryManager.Instance.Memories.Where(m => m.IsActive).ToList();

        var results = await Task.Run(() =>
        {
            var dict = new Dictionary<DataGridItem, bool>(rows.Count);
            foreach (var row in rows)
            {
                if (token.IsCancellationRequested) return null;
                dict[row] = activeMemories.Any(m => m.HasMatch(row.Text));
            }
            return dict;
        }, token);

        if (results == null || token.IsCancellationRequested) return;

        foreach (var (row, val) in results)
            row.HasTranslationMemoryMatch = val;
    }

    public void RefreshAfterBulkTranslationApply(IEnumerable<FileTabState> tabs)
    {
        foreach (var tab in tabs)
            _ = CalculateOriginalStatsAsync(tab);

        if (_activeTab != null && tabs.Contains(_activeTab))
        {
            DataGridMain.Items.Refresh();
            UpdateUndoRedoMenuItems();
        }
    }


    private void SubscribeTranslationMemoryEvents()
    {
        foreach (var m in TranslationMemoryManager.Instance.Memories)
            m.PropertyChanged += TranslationMemory_PropertyChanged;

        TranslationMemoryManager.Instance.Memories.CollectionChanged += TranslationMemories_CollectionChanged;
    }

    private void TranslationMemories_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (TranslationMemory m in e.OldItems)
                m.PropertyChanged -= TranslationMemory_PropertyChanged;

        if (e.NewItems != null)
            foreach (TranslationMemory m in e.NewItems)
                m.PropertyChanged += TranslationMemory_PropertyChanged;

        RefreshTitleForWriteTargetChange();
    }

    private void TranslationMemory_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranslationMemory.IsWriteTarget) || e.PropertyName == nameof(TranslationMemory.IsActive))
            RefreshTitleForWriteTargetChange();
    }

    private void RefreshTitleForWriteTargetChange()
    {
        Title = _activeTab?.WindowTitle(ToolName) ?? ToolName;
    }
    #endregion

    #region Original Text Box Settings
    private void ApplyOriginalTextBoxSettings()
    {
        OriginalTextBox.HorizontalContentAlignment = SettingsManager.OriginalTextAlignment switch
        {
            "Left" => HorizontalAlignment.Left,
            "Right" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
        OriginalTextBox.FontSize = SettingsManager.OriginalTextFontSize;
    }

    private void ApplyEditTextBoxSettings()
    {
        EditTextBox.FontSize = SettingsManager.EditTextFontSize;
    }

    private void OriginalTextBoxContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        AlignLeftMenuItem.FontWeight = SettingsManager.OriginalTextAlignment == "Left" ? FontWeights.Bold : FontWeights.Normal; 
        AlignCenterMenuItem.FontWeight = SettingsManager.OriginalTextAlignment == "Center" ? FontWeights.Bold : FontWeights.Normal;
        AlignRightMenuItem.FontWeight = SettingsManager.OriginalTextAlignment == "Right" ? FontWeights.Bold : FontWeights.Normal;
        FontSize8MenuItem.FontWeight = SettingsManager.OriginalTextFontSize == 8 ? FontWeights.Bold : FontWeights.Normal; 
        FontSize9MenuItem.FontWeight = SettingsManager.OriginalTextFontSize == 9 ? FontWeights.Bold : FontWeights.Normal;
        FontSize10MenuItem.FontWeight = SettingsManager.OriginalTextFontSize == 10 ? FontWeights.Bold : FontWeights.Normal;
        FontSize11MenuItem.FontWeight = SettingsManager.OriginalTextFontSize == 11 ? FontWeights.Bold : FontWeights.Normal;
        FontSize12MenuItem.FontWeight = SettingsManager.OriginalTextFontSize == 12 ? FontWeights.Bold : FontWeights.Normal;
        FontSize13MenuItem.FontWeight = SettingsManager.OriginalTextFontSize == 13 ? FontWeights.Bold : FontWeights.Normal;
        FontSize14MenuItem.FontWeight = SettingsManager.OriginalTextFontSize == 14 ? FontWeights.Bold : FontWeights.Normal;
    }

    private void OriginalTextAlign_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string tag)
        {
            SettingsManager.SetOriginalTextAlignment(tag);
            ApplyOriginalTextBoxSettings();
        }
    }

    private void OriginalFontSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string tag && int.TryParse(tag, out var size))
        {
            SettingsManager.SetOriginalTextFontSize(size);
            ApplyOriginalTextBoxSettings();
        }
    }
    #endregion

    #region Statistics
    private async Task CalculateOriginalStatsAsync(FileTabState tab)
    {
        if (tab == null)
            return;

        int rowCount = tab.DataRows.Count;
        StatsPanel.Visibility = rowCount == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (rowCount == 0)
            return;

        CancellationToken ct;

        if (tab == _activeTab)
        {
            _statsCts?.Cancel();
            _statsCts?.Dispose();
            _statsCts = new CancellationTokenSource();
            ct = _statsCts.Token;
        }
        else
        {
            ct = CancellationToken.None;
        }

        try
        {
            var result = await StatisticsManager.CalculateStatsAsync(tab.DataRows, ct);

            tab.CachedTotalWords = result.TotalWords;
            tab.CachedTranslatedRows = result.TranslatedRows;
            tab.CachedTranslatedWords = result.TranslatedWords;
            tab.CachedApprovedRows = result.ApprovedRows;
            tab.CachedApprovedWords = result.ApprovedWords;
            tab.StatsDirty = false;

            if (tab == _activeTab)
            {
                TotalRowsBlock.Text = N(rowCount);
                TotalWordsBlock.Text = N(result.TotalWords);
                UpdateStatsUI();
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    private void UpdateStatsUI()
    {
        if (_activeTab == null)
            return;

        int totalRows = _activeTab.DataRows.Count;
        int visibleRows = GetVisibleRowsCount();
        bool showFiltered = visibleRows >= 0 && visibleRows != totalRows;

        if (showFiltered)
            TotalRowsBlock.Text = $"{N(visibleRows)} / {N(totalRows)}";
        else
            TotalRowsBlock.Text = N(totalRows);

        TotalWordsBlock.Text = N(_activeTab.CachedTotalWords);

        double rowsPercent = totalRows > 0 ? _activeTab.CachedTranslatedRows * 100.0 / totalRows : 0;
        TranslatedRowsBlock.Text = N(_activeTab.CachedTranslatedRows);
        TranslatedRowsPercentBlock.Text = $" ({rowsPercent:F2}%)";

        double wordsPercent = _activeTab.CachedTotalWords > 0 ? _activeTab.CachedTranslatedWords * 100.0 / _activeTab.CachedTotalWords : 0;
        TranslatedWordsBlock.Text = N(_activeTab.CachedTranslatedWords);
        TranslatedWordsPercentBlock.Text = $" ({wordsPercent:F2}%)";

        double approvedPct = _activeTab.CachedTranslatedWords > 0
            ? _activeTab.CachedApprovedWords * 100.0 / _activeTab.CachedTranslatedWords
            : 0;

        ApprovedWordsBlock.Text = N(_activeTab.CachedApprovedWords);
        ApprovedWordsPercentBlock.Text = $" ({approvedPct:F2}%)";
    }

    private void GlobalStats_Click(object sender, RoutedEventArgs e)
    {
        var openTabs = _tabs.Where(t => !string.IsNullOrEmpty(t.FilePath) && t.DataRows.Count > 0).ToList();

        if (openTabs.Count == 0)
        {
            MessageBox.Show(this, "Немає відкритих файлів.", "Загальна статистика", MessageBoxButton.OK);
            return;
        }

        if (_globalStatsWindow != null && _globalStatsWindow.IsLoaded)
        {
            if (_globalStatsWindow.WindowState == WindowState.Minimized)
                _globalStatsWindow.WindowState = WindowState.Normal;
            _globalStatsWindow.Refresh(openTabs);
            _globalStatsWindow.Activate();
            return;
        }

        _globalStatsWindow = new GlobalStatsWindow(openTabs) { Owner = this };
        _globalStatsWindow.Closed += (s, e) => _globalStatsWindow = null;
        _globalStatsWindow.Show();
    }
    #endregion

    #region Helpers
    private void ControlsMode(bool enabled)
    {
        bool hasRows = Tab_Rows?.Count > 0;

        SaveOverwriteMenuItem.IsEnabled = enabled && hasRows && Tab_HasChanges;
        SaveAsMenuItem.IsEnabled = enabled && hasRows;
        SaveAllMenuItem.IsEnabled = _tabs.Any(t => t.HasUnsavedChanges && t.FormatHandler != null);
        SaveAllAsMenuItem.IsEnabled = _tabs.Any(t => t.FormatHandler != null && !string.IsNullOrEmpty(t.FilePath));
        OperationsMenuItem.IsEnabled = enabled && hasRows;

        SearchModeToggle.IsEnabled = enabled;
        SearchComboBox.IsEnabled = enabled;
        SearchButton.IsEnabled = enabled;
        WholeWordCheckBox.IsEnabled = enabled;
        CaseSensitiveCheckBox.IsEnabled = enabled;
        ExactMatchCheckBox.IsEnabled = enabled;
        FilterComboBox.IsEnabled = enabled;
        ToggleSearchResultsButton.IsEnabled = enabled && (Tab_Search?.MatchIndices.Length > 0);
        DataGridMain.ContextMenu.IsEnabled = enabled && hasRows;
    }

    private void StatusMessage(string message)
    {
        StatusTitle.Text = message;
        StatusBlock.Visibility = Visibility.Visible;
    }

    private void CloseFromState()
    {
        StatusBlock.Visibility = Visibility.Collapsed;
    }

    private static string GetShortPath(string fullPath, int folderDepth = 5)
    {
        var parts = fullPath.Replace('\\', '/').Split('/');
        int take = Math.Min(folderDepth + 1, parts.Length);
        var slice = parts.Skip(parts.Length - take).ToArray();
        string shortPath = string.Join(Path.DirectorySeparatorChar.ToString(), slice);
        return parts.Length > take ? "...​" + Path.DirectorySeparatorChar + shortPath : shortPath;
    }
    #endregion

    #region Other
    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        => new AboutWindow { Owner = this }.ShowDialog();

    private void MenuJsonDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab == null || _activeTab.JsonData == null)
        {
            MessageBox.Show(this, "Спочатку відкрийте JSON-файл.", "Інформація", MessageBoxButton.OK);
            return;
        }

        MessageBox.Show(this, _activeTab.CurrentFormat.ToString(), "Кейс JSON", MessageBoxButton.OK);
    }

    private async void MainWindow_Closing(object sender, CancelEventArgs e)
    {
        if (_isClosing)
            return;

        e.Cancel = true;

        try
        {
            if (_activeTab != null)
                SaveTabUIState(_activeTab);

            var dirtyTabs = _tabs.Where(t => t.HasUnsavedChanges).ToList();

            if (dirtyTabs.Count == 0)
            {
                _isClosing = true;
                await CloseWindowAsync();
                return;
            }

            if (dirtyTabs.Count == 1)
            {
                var tab = dirtyTabs[0];
                string fileName = string.IsNullOrEmpty(tab.FilePath) ? "новий файл" : Path.GetFileName(tab.FilePath);

                var result = MessageBox.Show(this, $"Є незбережені зміни у {fileName}.\nЗберегти перед закриттям?", "Попередження", MessageBoxButton.YesNoCancel);

                if (result == MessageBoxResult.Cancel)
                    return;

                if (result == MessageBoxResult.Yes)
                {
                    await SaveTabForCloseAsync(tab);

                    if (!tab.HasUnsavedChanges)
                    {
                        _isClosing = true;
                        await CloseWindowAsync();
                    }
                }
                else if (result == MessageBoxResult.No)
                {
                    _isClosing = true;
                    await CloseWindowAsync();
                }
            }
            else
            {
                const int maxShown = 5;

                var shownTabs = dirtyTabs.Take(maxShown).ToList();
                int remaining = dirtyTabs.Count - shownTabs.Count;

                string names = string.Join("\n", shownTabs.Select(t => "— " + (string.IsNullOrEmpty(t.FilePath) ? "новий файл" : Path.GetFileName(t.FilePath))));

                if (remaining > 0)
                    names += $"\n  … і ще {remaining}";

                var result = MessageBox.Show(this, $"Є незбережені зміни у {dirtyTabs.Count} вкладках:\n{names}\nЗберегти всі перед закриттям?", "Попередження", MessageBoxButton.YesNoCancel);

                if (result == MessageBoxResult.Cancel)
                    return;

                if (result == MessageBoxResult.Yes)
                {
                    foreach (var tab in dirtyTabs)
                        await SaveTabForCloseAsync(tab);

                    if (_tabs.All(t => !t.HasUnsavedChanges))
                    {
                        _isClosing = true;
                        await CloseWindowAsync();
                    }
                }
                else if (result == MessageBoxResult.No)
                {
                    _isClosing = true;
                    await CloseWindowAsync();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{ex}");
            _isClosing = true;
            try
            {
                await CloseWindowAsync();
            }
            catch (Exception ex2)
            {
                System.Diagnostics.Debug.WriteLine($"[Closing] Не вдалося закрити вікно: {ex2}");
                Environment.Exit(0);
            }
        }
    }

    private async Task CloseWindowAsync()
    {
        await CleanupResourcesAsync();
        await Task.Yield();
        Close();
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (_isClosing) return;
        _isClosing = true;

#pragma warning disable CS4014
        Task.Run(() =>
        {
            Thread.Sleep(2000);
            Environment.Exit(0);
        });
#pragma warning restore CS4014
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        bool focusInTextInput = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
            || Keyboard.FocusedElement is ICSharpCode.AvalonEdit.Editing.TextArea
            || SearchComboBox.IsDropDownOpen;

        if (focusInTextInput)
            return;

        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (DataGridMain.SelectedItems.Count > 0 && DataGridMain.CurrentCell.Column != null)
            {
                string columnPath = null;

                if (DataGridMain.CurrentCell.Column is DataGridBoundColumn boundColumn)
                {
                    var binding = boundColumn.Binding as System.Windows.Data.Binding;
                    columnPath = binding?.Path.Path;
                }

                if (columnPath != null)
                {
                    var items = DataGridMain.SelectedItems.OfType<DataGridItem>().ToList();
                    var values = items.Select(item => columnPath switch
                    {
                        "Index" => item.Index.ToString(),
                        "ID" => item.ID ?? "",
                        "Text" => item.Text ?? "",
                        "Translation" => item.Translation ?? "",
                        _ => ""
                    }).ToList();

                    if (values.Count > 0)
                    {
                        Clipboard.SetText(string.Join("\n", values));
                        e.Handled = true;
                    }
                }
            }
        }
        else if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (DataGridMain.SelectedItems.Count > 0 && Clipboard.ContainsText())
            {
                PasteClipboardToSelectedRows();
                e.Handled = true;
            }
        }
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (Tab_Rows?.Count > 0) SearchComboBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (Tab_Rows?.Count > 0) OpenReplaceWindow();
            e.Handled = true;
        }
        else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenGlossaryWindow_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            OpenGlobalSearchWindow();
            e.Handled = true;
        }
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenFile_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.O && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            OpenFolder_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (SaveOverwriteMenuItem.IsEnabled) SaveOverwrite_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (SaveAllMenuItem.IsEnabled) SaveAll_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_activeTab != null) await CloseTab(_activeTab);
            e.Handled = true;
        }
        else if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FindOrCreateEmptyTab(activate: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!EditTextBox.IsKeyboardFocusWithin && UndoMenuItem.IsEnabled)
            {
                Undo_Click(sender, new RoutedEventArgs());
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (!EditTextBox.IsKeyboardFocusWithin && RedoMenuItem.IsEnabled)
            {
                Redo_Click(sender, new RoutedEventArgs());
            }
            e.Handled = true;
        }
    }

    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        bool isDark = !App.IsDarkTheme();
        App.ApplyTheme(isDark);
        ThemeMenuItem.Header = isDark ? "Світла тема" : "Темна тема";
        EditTextBox.TextArea.TextView.Redraw();
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow { Owner = this }.ShowDialog();

        foreach (var t in _tabs)
            RefreshTranslationMemoryHighlights(t);

        UpdateLoadStatusesVisibility();

        if (SettingsManager.DiscordPresenceEnabled)
        {
            int openFiles = _tabs.Count(t => !string.IsNullOrEmpty(t.FilePath));
            int totalRows = _tabs.Sum(t => t.DataRows.Count);

            if (_activeTab != null && !string.IsNullOrEmpty(_activeTab.FilePath))
                _discord.SetFileState(_activeTab.FilePath, _activeTab.DataRows.Count, totalRows, openFiles, _activeTab.HasUnsavedChanges);
            else
                _discord.SetIdle();
        }
        else
        {
            _discord.Clear();
        }
    }

    private void UpdateLoadStatusesVisibility()
    {
        LoadStatusesMenuItem.Visibility = SettingsManager.CreateStatusFiles
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
    #endregion

    #region Cleanup
    private async Task CleanupResourcesAsync()
    {
        if (_searchNotFoundTimer != null)
        {
            _searchNotFoundTimer.Stop();

            var waitCount = 0;
            while (_searchNotFoundTimer.IsEnabled && waitCount < 50)
            {
                await Task.Delay(10);
                waitCount++;
            }

            _searchNotFoundTimer = null;
        }

        if (_statsCts != null)
        {
            try
            {
                _statsCts.Cancel();
            }
            catch { }

            _statsCts.Dispose();
            _statsCts = null;
        }

        if (replaceWindow != null)
        {
            replaceWindow.Close();
            replaceWindow = null;
        }

        if (_globalSearchWindow != null)
        {
            _globalSearchWindow.Close();
            _globalSearchWindow = null;
        }

        if (_glossaryWindow != null)
        {
            _glossaryWindow.Close();
            _glossaryWindow = null;
        }

        if (_globalStatsWindow != null)
        {
            _globalStatsWindow.Close();
            _globalStatsWindow = null;
        }

        if (_globalFilterWindow != null)
        {
            _globalFilterWindow.Close();
            _globalFilterWindow = null;
        }

        _discord?.Dispose();

        foreach (var tab in _tabs.ToList())
        {
            tab.Dispose();
        }

        _tabs.Clear();
    }
    #endregion
}