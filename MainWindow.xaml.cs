using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Butane;

public partial class MainWindow : Window
{
    private const string CloudflareDnsUrl = "https://cloudflare-dns.com/dns-query";
    private static readonly string BrowserDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Butane");
    private static readonly string SiteExceptionsFile = Path.Combine(BrowserDataFolder, "site-exceptions.json");

    private static readonly Lazy<Task<CoreWebView2Environment>> PersistentBrowserEnvironment =
        new(() => CreateBrowserEnvironmentAsync(BrowserDataFolder));
    private static readonly HashSet<string> AllowedSites = new(StringComparer.OrdinalIgnoreCase);
    private static bool _siteExceptionsLoaded;
    private static BrowserSettings _settings = new();
    private static bool _settingsLoaded;
    private static readonly HashSet<MainWindow> NormalWindows = new();

    private readonly bool _isPrivate;
    private readonly string? _privateProfileName;
    private readonly Task<CoreWebView2Environment> _browserEnvironment;
    private readonly HashSet<string> _privateAllowedSites = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Task> _tabInitializations = new();
    private bool _isClosing;
    private bool _privateCleanupComplete;
    private bool _closeAfterCookieCleanup;

    private static Task<CoreWebView2Environment> CreateBrowserEnvironmentAsync(string? userDataFolder)
    {
        if (userDataFolder != null) Directory.CreateDirectory(userDataFolder);

        var options = new CoreWebView2EnvironmentOptions(
            $"--dns-over-https-mode=secure --dns-over-https-templates={CloudflareDnsUrl}");
        return CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
    }

    private void BookmarkButton_Click(object sender, RoutedEventArgs e) => ToggleBookmark();

    private void BookmarksButton_Click(object sender, RoutedEventArgs e) => OpenLibrary(0);
    private void HistoryButton_Click(object sender, RoutedEventArgs e) => OpenLibrary(1);

    private void DownloadsButton_Click(object sender, RoutedEventArgs e) => OpenLibrary(2);

    private void ToggleBookmark()
    {
        string? url = _current?.View.CoreWebView2?.Source;
        if (_isPrivate || !IsWeb(url)) return;

        var existing = BrowserLibraryStore.Bookmarks.FirstOrDefault(
            bookmark => string.Equals(bookmark.Url, url, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            BrowserLibraryStore.Bookmarks.Remove(existing);
        else
            BrowserLibraryStore.Bookmarks.Add(new BookmarkEntry
            {
                Title = string.IsNullOrWhiteSpace(_current?.Title) ? url! : _current!.Title,
                Url = url!
            });

        try
        {
            BrowserLibraryStore.Save();
        }
        catch (Exception ex)
        {
            if (existing != null) BrowserLibraryStore.Bookmarks.Add(existing);
            else BrowserLibraryStore.Bookmarks.Remove(BrowserLibraryStore.Bookmarks.Last());

            MessageBox.Show(
                "Could not save this bookmark.\n\n" + ex.Message,
                "Butane bookmarks", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        UpdateBookmarkButton();
    }

    private void UpdateBookmarkButton()
    {
        UpdateSecurityIcon();
        if (!IsLoaded || SaveBookmarkButton == null) return;
        string? url = _current?.View.CoreWebView2?.Source;
        bool isBookmarked = !_isPrivate && IsWeb(url) &&
            BrowserLibraryStore.Bookmarks.Any(
                bookmark => string.Equals(bookmark.Url, url, StringComparison.OrdinalIgnoreCase));
        SaveBookmarkButton.Content = isBookmarked ? "SAVED" : "SAVE";
        SaveBookmarkButton.IsEnabled = !_isPrivate && IsWeb(url);
    }

    private void Bookmarks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateBookmarkButton();

    private void AddHistoryEntry(string url, string title)
    {
        if (!IsWeb(url)) return;

        var entry = new HistoryEntry
        {
            Title = string.IsNullOrWhiteSpace(title) ? url : title,
            Url = url,
            VisitedAt = DateTime.Now
        };
        BrowserLibraryStore.History.Insert(0, entry);
        try
        {
            BrowserLibraryStore.Save();
        }
        catch (Exception ex)
        {
            BrowserLibraryStore.History.Remove(entry);
            MessageBox.Show(
                "Could not save browsing history.\n\n" + ex.Message,
                "Butane history", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenLibrary(int selectedTab)
    {
        var bookmarks = _isPrivate
            ? new ObservableCollection<BookmarkEntry>()
            : BrowserLibraryStore.Bookmarks;
        var history = _isPrivate
            ? new ObservableCollection<HistoryEntry>()
            : BrowserLibraryStore.History;
        var downloads = _isPrivate ? _privateDownloads : BrowserLibraryStore.Downloads;
        var window = new LibraryWindow(
            bookmarks, history, downloads, NavigateTo, SaveLibraryData, CancelDownload, _isPrivate, selectedTab)
        {
            Owner = this
        };
        window.Show();
        if (!_isPrivate) window.Activate();
    }

    private void NavigateTo(string url)
    {
        var core = _current?.View.CoreWebView2;
        if (core == null) StartAddTab(url);
        else core.Navigate(url);
        Activate();
    }

    private void SaveLibraryData()
    {
        if (!_isPrivate) BrowserLibraryStore.Save();
    }

    private void HandleDownloadStarting(CoreWebView2DownloadStartingEventArgs e)
    {
        var operation = e.DownloadOperation;
        string filePath;
        try
        {
            filePath = GetDownloadFilePath(operation.ResultFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            e.Cancel = true;
            MessageBox.Show(
                "Could not prepare the Downloads folder.\n\n" + ex.Message,
                "Butane downloads", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        e.ResultFilePath = filePath;
        e.Handled = true;

        var entry = new DownloadEntry
        {
            FileName = Path.GetFileName(filePath),
            FilePath = filePath,
            StartedAt = DateTime.Now,
            BytesReceived = operation.BytesReceived,
            TotalBytesToReceive = operation.TotalBytesToReceive,
            State = GetDownloadState(operation.State)
        };
        var downloads = _isPrivate ? _privateDownloads : BrowserLibraryStore.Downloads;
        downloads.Insert(0, entry);
        _activeDownloads[entry] = operation;
        SaveDownloadData();

        operation.BytesReceivedChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            entry.BytesReceived = operation.BytesReceived;
            entry.TotalBytesToReceive = operation.TotalBytesToReceive;
        }));
        operation.StateChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            entry.State = GetDownloadState(operation.State);
            entry.BytesReceived = operation.BytesReceived;
            entry.TotalBytesToReceive = operation.TotalBytesToReceive;
            if (operation.State != CoreWebView2DownloadState.InProgress)
                _activeDownloads.Remove(entry);
            SaveDownloadData();
        }));
    }

    private static string GetDownloadFilePath(string suggestedPath)
    {
        string downloadsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(downloadsFolder))
            downloadsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Directory.CreateDirectory(downloadsFolder);

        string fileName = Path.GetFileName(suggestedPath);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "download";
        string extension = Path.GetExtension(fileName);
        if (extension.Length > 20) extension = "";
        string path = Path.Combine(downloadsFolder, fileName);

        if (path.Length > 240)
            return GetUniqueRandomDownloadPath(downloadsFolder, extension);

        if (File.Exists(path))
        {
            string baseName = Path.GetFileNameWithoutExtension(path);
            int suffix = 1;
            while (true)
            {
                string candidate = Path.Combine(downloadsFolder, $"{baseName} ({suffix++}){extension}");
                if (candidate.Length > 240)
                    return GetUniqueRandomDownloadPath(downloadsFolder, extension);
                if (!File.Exists(candidate)) return candidate;
            }
        }

        return path;
    }

    private static string GetUniqueRandomDownloadPath(string downloadsFolder, string extension)
    {
        while (true)
        {
            string path = Path.Combine(downloadsFolder, Guid.NewGuid().ToString("N") + extension);
            if (path.Length > 240)
                throw new PathTooLongException("The Downloads folder path is too long to save files safely.");
            if (!File.Exists(path)) return path;
        }
    }

    private readonly Dictionary<DownloadEntry, CoreWebView2DownloadOperation> _activeDownloads = new();

    private void CancelDownload(DownloadEntry entry)
    {
        if (!_activeDownloads.TryGetValue(entry, out var operation)) return;
        try
        {
            operation.Cancel();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not cancel this download.\n\n" + ex.Message,
                "Butane downloads", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string GetDownloadState(CoreWebView2DownloadState state) =>
        state switch
        {
            CoreWebView2DownloadState.InProgress => "IN PROGRESS",
            CoreWebView2DownloadState.Completed => "COMPLETED",
            _ => "INTERRUPTED"
        };

    private void SaveDownloadData()
    {
        if (_isPrivate) return;
        try
        {
            BrowserLibraryStore.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not save download details.\n\n" + ex.Message,
                "Butane downloads", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Tiny starter blocklist. We will swap this for real filter lists later.
    private static readonly string[] BlockedHosts =
    {
        "doubleclick.net", "googlesyndication.com", "googleadservices.com",
        "google-analytics.com", "adservice.google.com", "connect.facebook.net",
        "scorecardresearch.com", "hotjar.com", "taboola.com", "outbrain.com",
        "adnxs.com", "criteo.com", "amazon-adsystem.com"
    };

    private const string NewTabHtml = """
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8">
        <title>NEW TAB</title>
        <style>
          html, body { height: 100%; margin: 0; background: #111; color: #fff;
                       font-family: Consolas, monospace; }
          body { display: flex; flex-direction: column; align-items: center; justify-content: center; }
          h1 { font-size: 64px; letter-spacing: 6px; margin: 0 0 14px 0; }
          p { color: #8c8c8c; font-size: 13px; letter-spacing: 3px; margin: 4px; }
        </style>
        </head>
        <body>
          <h1>BUTANE</h1>
          <p>NORMAL REMEMBERS YOU ON SITES YOU VISIT, BUT BLOCKS TRACKERS.</p>
          <p>TYPE IN THE BAR ABOVE TO START</p>
        </body>
        </html>
        """;

    private const string PrivateNewTabHtml = """
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8">
        <title>PRIVATE WINDOW</title>
        <style>
          html, body { height: 100%; margin: 0; background: #111; color: #fff;
                       font-family: Consolas, monospace; }
          body { display: flex; flex-direction: column; align-items: center; justify-content: center; }
          h1 { color: #b58cff; font-size: 64px; letter-spacing: 6px; margin: 0 0 14px 0; }
          p { color: #aaa; font-size: 13px; letter-spacing: 1px; margin: 6px; text-align: center; }
        </style>
        </head>
        <body>
          <h1>PRIVATE</h1>
          <p>PRIVATE REMEMBERS NOTHING. COOKIES, LOGINS, AND CACHE ARE CLEARED WHEN THIS WINDOW CLOSES.</p>
          <p>WEBSITES, YOUR NETWORK, AND YOUR ISP MAY STILL SEE YOUR ACTIVITY.</p>
          <p>TYPE IN THE BAR ABOVE TO START</p>
        </body>
        </html>
        """;

    private readonly ObservableCollection<BrowserTab> _tabs = new();
    private readonly ObservableCollection<DownloadEntry> _privateDownloads = new();
    private BrowserTab? _current;
    private int _blocked;
    private bool _blockTrackers = true;

    public MainWindow()
        : this(false)
    {
    }

    public MainWindow(bool isPrivate)
    {
        EnsureSettingsLoaded();
        _isPrivate = isPrivate;
        if (_isPrivate)
        {
            _privateProfileName = "Private" + Guid.NewGuid().ToString("N");
            _browserEnvironment = PersistentBrowserEnvironment.Value;
        }
        else
        {
            _browserEnvironment = PersistentBrowserEnvironment.Value;
            LoadSiteExceptions();
        }

        InitializeComponent();
        if (!_isPrivate)
        {
            NormalWindows.Add(this);
            BrowserLibraryStore.Bookmarks.CollectionChanged += Bookmarks_CollectionChanged;
        }
        ApplySettingsToWindow();
        if (_isPrivate)
        {
            Title = "PRIVATE WINDOW - Butane";
            Resources["AccentBrush"] = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(181, 140, 255));
            PrivateModeLabel.Visibility = Visibility.Visible;
            BookmarksManagerButton.IsEnabled = false;
            HistoryManagerButton.IsEnabled = false;
        }

        TabList.ItemsSource = _tabs;
        Loaded += (_, _) => StartInitialTabs();
        Deactivated += Window_Deactivated;
        Closing += Window_Closing;
        Closed += (_, _) =>
        {
            NormalWindows.Remove(this);
            if (!_isPrivate)
                BrowserLibraryStore.Bookmarks.CollectionChanged -= Bookmarks_CollectionChanged;
        };
    }

    // Dark title bar to match the interface (Windows 10 20H1+ and Windows 11).
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        int on = 1;
        DwmSetWindowAttribute(handle, 20, ref on, sizeof(int));
    }

    // ---------- Tabs ----------

    private void StartAddTab(string? url)
    {
        if (_isClosing) return;

        var initialization = AddTabAsync(url);
        _tabInitializations.Add(initialization);
        _ = initialization.ContinueWith(
            _ => Dispatcher.BeginInvoke(new Action(() => _tabInitializations.Remove(initialization))),
            TaskScheduler.Default);
    }

    private async Task<bool> AddTabAsync(
        string? url,
        CoreWebView2NewWindowRequestedEventArgs? popupArgs = null,
        BrowserTab? popupOpener = null,
        BrowserTab? preparedTab = null)
    {
        var tab = preparedTab ?? new BrowserTab();
        WebHost.Children.Add(tab.View);
        _tabs.Add(tab);
        SelectTab(tab); // shows this tab's view before the engine starts

        try
        {
            var environment = await _browserEnvironment;
            if (_isClosing || !_tabs.Contains(tab)) return false;
            var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.IsInPrivateModeEnabled = _isPrivate;
            if (_privateProfileName != null) controllerOptions.ProfileName = _privateProfileName;
            await tab.View.EnsureCoreWebView2Async(environment, controllerOptions);
        }
        catch (Exception ex)
        {
            if (_isClosing || !_tabs.Contains(tab)) return false;

            MessageBox.Show(
                "Could not start the browser engine (WebView2).\n\n" + ex.Message,
                "Butane", MessageBoxButton.OK, MessageBoxImage.Error);
            CloseTab(tab);
            return false;
        }

        if (_isClosing || !_tabs.Contains(tab)) return false;

        var core = tab.View.CoreWebView2;
        core.Profile.PreferredTrackingPreventionLevel = _settings.GetTrackingPreventionLevel();
        if (popupArgs != null)
        {
            popupArgs.NewWindow = core;
            tab.PopupOpener = popupOpener;
        }

        core.SourceChanged += (_, _) =>
        {
            if (tab == _current)
            {
                AddressBar.Text = DisplayUrl(core.Source);
                UpdateBookmarkButton();
                UpdateSiteExceptionButton();
            }
        };
        core.DocumentTitleChanged += (_, _) =>
        {
            tab.Title = core.DocumentTitle;
            if (tab == _current) UpdateWindowTitle();
        };
        core.HistoryChanged += (_, _) =>
        {
            if (tab == _current) UpdateNavButtons();
        };
        core.NavigationStarting += (_, _) =>
        {
            tab.LoadStart = Stopwatch.GetTimestamp();
            SetStatus(tab, "LOADING...");
        };
        core.NavigationCompleted += (_, e) =>
        {
            if (!IsWeb(core.Source)) SetStatus(tab, "READY");
            else if (e.IsSuccess)
            {
                SetStatus(tab, $"LOADED IN {Stopwatch.GetElapsedTime(tab.LoadStart).TotalSeconds:0.00}S");
                if (!_isPrivate)
                {
                    AddHistoryEntry(core.Source, core.DocumentTitle);
                    SaveSession();
                }
            }
            else SetStatus(tab, "FAILED TO LOAD");
            if (tab == _current) UpdateNavButtons();
        };
        core.DownloadStarting += (_, e) => HandleDownloadStarting(e);
        HookTabFeatures(tab, core);

        // Keep the new window linked to its opener so window.opener messaging works.
        core.NewWindowRequested += (_, e) =>
        {
            StartAddPopupTab(tab, e);
        };
        core.WindowCloseRequested += (_, _) =>
            Dispatcher.BeginInvoke(new Action(() => CloseTab(tab)));

        // Tracker blocking.
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;

        UpdateTabSuspension(tab, tab != _current);

        if (popupArgs != null) return true;
        if (url == null) core.NavigateToString(_isPrivate ? PrivateNewTabHtml : NewTabHtml);
        else core.Navigate(url);
        return true;
    }

    private void StartAddPopupTab(BrowserTab opener, CoreWebView2NewWindowRequestedEventArgs args)
    {
        if (_isClosing)
        {
            args.Handled = true;
            return;
        }

        opener.OpenPopupCount++;
        UpdateTabSuspension(opener, shouldSuspend: false);
        var popupTab = new BrowserTab { PopupOpener = opener };
        var deferral = args.GetDeferral();
        var initialization = AddPopupTabAsync(opener, popupTab, args, deferral);
        _tabInitializations.Add(initialization);
        _ = initialization.ContinueWith(
            _ => Dispatcher.BeginInvoke(new Action(() => _tabInitializations.Remove(initialization))),
            TaskScheduler.Default);
    }

    private async Task AddPopupTabAsync(
        BrowserTab opener,
        BrowserTab popupTab,
        CoreWebView2NewWindowRequestedEventArgs args,
        CoreWebView2Deferral deferral)
    {
        try
        {
            bool initialized = await AddTabAsync(null, args, opener, popupTab);
            if (!initialized)
            {
                args.Handled = true;
                CloseTab(popupTab);
            }
        }
        catch (Exception ex)
        {
            args.Handled = true;
            CloseTab(popupTab);
            MessageBox.Show(
                "Could not open this popup tab.\n\n" + ex.Message,
                "Butane", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ReleasePopupHold(BrowserTab opener)
    {
        if (opener.OpenPopupCount > 0) opener.OpenPopupCount--;
        if (_tabs.Contains(opener))
            UpdateTabSuspension(opener, opener != _current);
    }

    private void SelectTab(BrowserTab tab, bool focusView = false)
    {
        if (!_tabs.Contains(tab)) return;

        _current = tab;
        foreach (var t in _tabs)
        {
            t.View.Visibility = t == tab ? Visibility.Visible : Visibility.Collapsed;
            UpdateTabSuspension(t, t != tab);
        }

        TabList.SelectedItem = tab;
        AddressBar.Text = DisplayUrl(tab.View.CoreWebView2?.Source);
        UpdateBookmarkButton();
        UpdateSiteExceptionButton();
        StatusText.Text = tab.Status;
        UpdateWindowTitle();
        UpdateNavButtons();
        if (focusView) tab.View.Focus();
    }

    private void UpdateTabSuspension(BrowserTab tab, bool shouldSuspend)
    {
        tab.ShouldBeSuspended = shouldSuspend && tab.OpenPopupCount == 0;
        if (tab.View.CoreWebView2 == null || tab.SuspensionUpdateRunning || _isClosing) return;

        tab.SuspensionUpdateRunning = true;
        _ = UpdateTabSuspensionAsync(tab);
    }

    private async Task UpdateTabSuspensionAsync(BrowserTab tab)
    {
        var core = tab.View.CoreWebView2;
        if (core == null)
        {
            tab.SuspensionUpdateRunning = false;
            return;
        }

        bool transitionFailed = false;
        try
        {
            while (_tabs.Contains(tab) && !_isClosing)
            {
                if (tab.ShouldBeSuspended)
                {
                    if (core.IsSuspended)
                    {
                        tab.IsSleeping = true;
                        break;
                    }

                    bool suspended = await core.TrySuspendAsync();
                    if (!_tabs.Contains(tab) || _isClosing) break;

                    if (!suspended && tab.ShouldBeSuspended)
                    {
                        tab.IsSleeping = false;
                        SetStatus(tab, "COULD NOT SLEEP");
                        transitionFailed = true;
                        break;
                    }

                    tab.IsSleeping = suspended;
                }
                else
                {
                    if (core.IsSuspended) core.Resume();
                    tab.IsSleeping = false;
                }

                if (tab.ShouldBeSuspended == core.IsSuspended) break;
            }
        }
        catch (Exception ex)
        {
            transitionFailed = true;
            if (_tabs.Contains(tab) && !_isClosing)
            {
                tab.IsSleeping = false;
                SetStatus(tab, "TAB SLEEP FAILED");
                MessageBox.Show(
                    "Could not suspend or resume this background tab.\n\n" + ex.Message,
                    "Butane", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            tab.SuspensionUpdateRunning = false;
            if (!transitionFailed && _tabs.Contains(tab) && !_isClosing &&
                tab.ShouldBeSuspended != core.IsSuspended)
                UpdateTabSuspension(tab, tab.ShouldBeSuspended);
        }
    }

    private void CloseTab(BrowserTab tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0) return;

        RememberClosedTab(tab);

        if (tab.PopupOpener is { } opener)
        {
            tab.PopupOpener = null;
            ReleasePopupHold(opener);
        }

        bool wasCurrent = tab == _current;
        if (wasCurrent) _current = null;

        _tabs.Remove(tab);
        WebHost.Children.Remove(tab.View);
        tab.View.Dispose();
        if (!_isPrivate && !_isClosing) SaveSession();

        if (_tabs.Count == 0)
        {
            _current = null;
            if (!_isClosing) StartAddTab(null);
            return;
        }

        if (wasCurrent) SelectTab(_tabs[Math.Min(index, _tabs.Count - 1)]);
    }

    private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabList.SelectedItem is BrowserTab tab && tab != _current) SelectTab(tab, focusView: true);
    }

    private void NewTab_Click(object sender, RoutedEventArgs e)
    {
        OpenNewTab();
    }

    private void NewWindow_Click(object sender, RoutedEventArgs e)
    {
        OpenNewWindow(isPrivate: false);
    }

    private void PrivateWindow_Click(object sender, RoutedEventArgs e)
    {
        OpenNewWindow(isPrivate: true);
    }

    private void SiteException_Click(object sender, RoutedEventArgs e)
    {
        string? host = GetCurrentSiteHost();
        if (host == null) return;

        var allowedSites = _isPrivate ? _privateAllowedSites : AllowedSites;
        string? existingException = FindMatchingSiteException(allowedSites, host);
        if (existingException != null) allowedSites.Remove(existingException);
        else allowedSites.Add(host);

        if (!_isPrivate)
        {
            try
            {
                Directory.CreateDirectory(BrowserDataFolder);
                File.WriteAllText(SiteExceptionsFile, JsonSerializer.Serialize(AllowedSites.OrderBy(site => site)));
            }
            catch (Exception ex)
            {
                if (existingException != null) allowedSites.Add(existingException);
                else allowedSites.Remove(host);

                MessageBox.Show(
                    "Could not save the site exception.\n\n" + ex.Message,
                    "Butane", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        UpdateSiteExceptionButton();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SavedSettings is { } settings)
            ApplySettingsToOpenWindows(settings);
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BrowserTab tab }) CloseTab(tab);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HandleShortcut(
                key,
                (modifiers & ModifierKeys.Control) != 0,
                (modifiers & ModifierKeys.Shift) != 0,
                (modifiers & ModifierKeys.Alt) != 0))
            e.Handled = true;
    }

    private void OpenNewTab()
    {
        StartAddTab(null);
        AddressBar.Focus();
    }

    private void OpenNewWindow(bool isPrivate)
    {
        var window = new MainWindow(isPrivate);
        window.Show();
        window.Activate();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isPrivate && NormalWindows.Count == 1) SaveSession();

        if (!_isPrivate)
        {
            if (_closeAfterCookieCleanup || !_settings.ClearCookiesOnExit || NormalWindows.Count != 1)
                return;

            e.Cancel = true;
            if (_isClosing) return;

            _isClosing = true;
            IsEnabled = false;
            await ClearCookiesAndCloseAsync();
            return;
        }

        if (_privateCleanupComplete) return;

        e.Cancel = true;
        if (_isClosing) return;

        _isClosing = true;
        var cleanupErrors = new List<string>();
        try
        {
            await Task.WhenAll(_tabInitializations.ToArray());
        }
        catch (Exception ex)
        {
            cleanupErrors.Add("Could not finish initializing every tab: " + ex.Message);
        }

        foreach (var tab in _tabs)
        {
            WebHost.Children.Remove(tab.View);
            tab.View.Dispose();
        }
        _tabs.Clear();
        _current = null;

        _privateCleanupComplete = true;
        if (cleanupErrors.Count > 0)
        {
            MessageBox.Show(
                "Private window cleanup reported an error:\n\n" + string.Join("\n\n", cleanupErrors),
                "Butane privacy cleanup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Close();
    }

    private async Task ClearCookiesAndCloseAsync()
    {
        try
        {
            await Task.WhenAll(_tabInitializations.ToArray());
            var core = _tabs.Select(tab => tab.View.CoreWebView2).FirstOrDefault(view => view != null);
            if (core != null)
            {
                await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.Cookies);
            }
            else
            {
                var temporaryView = new WebView2();
                try
                {
                    await temporaryView.EnsureCoreWebView2Async(await _browserEnvironment);
                    await temporaryView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                        CoreWebView2BrowsingDataKinds.Cookies);
                }
                finally
                {
                    temporaryView.Dispose();
                }
            }

            _closeAfterCookieCleanup = true;
            Close();
        }
        catch (Exception ex)
        {
            _isClosing = false;
            IsEnabled = true;
            var closeAnyway = MessageBox.Show(
                "Could not clear cookies before closing. Cookies may remain on disk.\n\n" +
                ex.Message + "\n\nClose anyway?",
                "Butane cookie cleanup", MessageBoxButton.YesNo, MessageBoxImage.Error);
            if (closeAnyway == MessageBoxResult.Yes)
            {
                _closeAfterCookieCleanup = true;
                Close();
            }
        }
    }

    private void CycleTabs(int direction)
    {
        if (_tabs.Count == 0) return;

        int currentIndex = _current == null ? -1 : _tabs.IndexOf(_current);
        int nextIndex = (currentIndex + direction + _tabs.Count) % _tabs.Count;
        SelectTab(_tabs[nextIndex], focusView: true);
    }

    private void SelectTabAtIndex(int index)
    {
        if (index >= 0 && index < _tabs.Count) SelectTab(_tabs[index], focusView: true);
    }

    // ---------- Tracker blocking ----------

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!_blockTrackers) return;
        if (e.ResourceContext == CoreWebView2WebResourceContext.Document) return; // never block the page itself
        if (sender is CoreWebView2 requestCore && IsCurrentSiteAllowed(requestCore)) return;
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)) return;

        string host = uri.Host;
        foreach (var blocked in BlockedHosts)
        {
            if (host == blocked || host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase))
            {
                var core = (CoreWebView2)sender!;
                e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
                _blocked++;
                BlockedCount.Text = _blocked.ToString();
                return;
            }
        }
    }

    private void ShieldToggle_Click(object sender, RoutedEventArgs e)
    {
        var settings = new BrowserSettings
        {
            BlockTrackers = ShieldToggle.IsChecked == true,
            ClearCookiesOnExit = _settings.ClearCookiesOnExit,
            TrackingPreventionLevel = _settings.TrackingPreventionLevel
        };
        try
        {
            BrowserSettingsStore.Save(settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not save the Shield setting.\n\n" + ex.Message,
                "Butane settings", MessageBoxButton.OK, MessageBoxImage.Error);
            ApplySettingsToWindow();
            return;
        }

        ApplySettingsToOpenWindows(settings);
    }

    private static void EnsureSettingsLoaded()
    {
        if (_settingsLoaded) return;
        _settings = BrowserSettingsStore.Load();
        _settingsLoaded = true;
    }

    private static void ApplySettingsToOpenWindows(BrowserSettings settings)
    {
        _settings = settings;
        foreach (var window in Application.Current.Windows.OfType<MainWindow>())
            window.ApplySettingsToWindow();
    }

    private void ApplySettingsToWindow()
    {
        _blockTrackers = _settings.BlockTrackers;
        ShieldToggle.IsChecked = _blockTrackers;
        ShieldState.Text = _blockTrackers ? "ON" : "OFF";
        foreach (var tab in _tabs)
        {
            if (tab.View.CoreWebView2 != null)
                tab.View.CoreWebView2.Profile.PreferredTrackingPreventionLevel =
                    _settings.GetTrackingPreventionLevel();
        }

        ApplyChromeSettings();
    }

    // ---------- Toolbar and address bar ----------

    private void UpdateNavButtons()
    {
        var core = _current?.View.CoreWebView2;
        BackButton.IsEnabled = core?.CanGoBack == true;
        ForwardButton.IsEnabled = core?.CanGoForward == true;
    }

    private void UpdateWindowTitle()
    {
        string title = string.IsNullOrWhiteSpace(_current?.Title) ? "Butane" : _current!.Title + " - Butane";
        Title = _isPrivate ? "PRIVATE WINDOW - " + title : title;
    }

    private string? GetCurrentSiteHost()
    {
        if (!Uri.TryCreate(_current?.View.CoreWebView2?.Source, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        return uri.IdnHost.TrimEnd('.');
    }

    private bool IsCurrentSiteAllowed(CoreWebView2 core)
    {
        if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)) return false;
        var allowedSites = _isPrivate ? _privateAllowedSites : AllowedSites;
        return ContainsSiteOrSubdomain(allowedSites, uri.IdnHost);
    }

    private static bool ContainsSiteOrSubdomain(HashSet<string> sites, string host) =>
        FindMatchingSiteException(sites, host) != null;

    private static string? FindMatchingSiteException(HashSet<string> sites, string host) =>
        sites.Where(site => host.Equals(site, StringComparison.OrdinalIgnoreCase) ||
                            host.EndsWith("." + site, StringComparison.OrdinalIgnoreCase))
             .OrderByDescending(site => site.Length)
             .FirstOrDefault();

    private static void LoadSiteExceptions()
    {
        if (_siteExceptionsLoaded) return;

        _siteExceptionsLoaded = true;
        if (!File.Exists(SiteExceptionsFile)) return;

        try
        {
            var sites = JsonSerializer.Deserialize<string[]>(File.ReadAllText(SiteExceptionsFile)) ?? Array.Empty<string>();
            foreach (var site in sites)
            {
                if (!string.IsNullOrWhiteSpace(site) &&
                    !Uri.CheckHostName(site).Equals(UriHostNameType.Unknown))
                    AllowedSites.Add(site.Trim().TrimEnd('.'));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not load saved site exceptions.\n\n" + ex.Message,
                "Butane", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateSiteExceptionButton()
    {
        if (!IsLoaded) return;

        string? host = GetCurrentSiteHost();
        bool allowed = host != null && ContainsSiteOrSubdomain(
            _isPrivate ? _privateAllowedSites : AllowedSites, host);
        SiteExceptionButton.IsEnabled = host != null;
        SiteExceptionButton.Content = allowed ? "REMOVE SITE EXCEPTION" : "ALLOW CURRENT SITE";
        SiteExceptionButton.Foreground = allowed
            ? System.Windows.Media.Brushes.LightGreen
            : (System.Windows.Media.Brush)FindResource("DimBrush");
    }

    private void SetStatus(BrowserTab tab, string text)
    {
        tab.Status = text;
        if (tab == _current) StatusText.Text = text;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var core = _current?.View.CoreWebView2;
        if (core?.CanGoBack == true) core.GoBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        var core = _current?.View.CoreWebView2;
        if (core?.CanGoForward == true) core.GoForward();
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        _current?.View.CoreWebView2?.Reload();
    }

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (HandleSuggestKeys(e)) return;
        if (e.Key != Key.Enter) return;
        CloseSuggestions();

        var tab = _current;
        var core = tab?.View.CoreWebView2;
        if (tab == null || core == null) return;

        string url = ToUrl(AddressBar.Text);
        if (url.Length == 0) return;

        core.Navigate(url);
        tab.View.Focus();
    }

    private void AddressBar_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => AddressBar.SelectAll()));
    }

    // ---------- Helpers ----------

    private static bool IsWeb(string? url) =>
        url != null && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    // Only real web addresses go in the address bar; the new tab page shows the placeholder.
    private static string DisplayUrl(string? url) => IsWeb(url) ? url! : "";

    // Turns whatever was typed into a URL: full address, bare domain, or a search.
    private static string ToUrl(string input)
    {
        input = input.Trim();
        if (input.Length == 0) return "";

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return input;

        if (!input.Contains(' ') && input.Contains('.'))
            return "https://" + input;

        return _settings.GetSearchUrl() + Uri.EscapeDataString(input);
    }
}