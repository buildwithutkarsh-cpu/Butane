using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace Butane;

public partial class MainWindow : Window
{
    private const string SearchUrl = "https://duckduckgo.com/?q=";
    private const string CloudflareDnsUrl = "https://cloudflare-dns.com/dns-query";

    private static Task<CoreWebView2Environment> CreateBrowserEnvironmentAsync()
    {
        var options = new CoreWebView2EnvironmentOptions(
            $"--dns-over-https-mode=secure --dns-over-https-templates={CloudflareDnsUrl}");
        return CoreWebView2Environment.CreateAsync(null, null, options);
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
          <p>FAST. PRIVATE.</p>
          <p>TYPE IN THE BAR ABOVE TO START</p>
        </body>
        </html>
        """;

    private readonly ObservableCollection<BrowserTab> _tabs = new();
    private readonly Task<CoreWebView2Environment> _browserEnvironment = CreateBrowserEnvironmentAsync();
    private BrowserTab? _current;
    private int _blocked;
    private bool _blockTrackers = true;

    public MainWindow()
    {
        InitializeComponent();
        TabList.ItemsSource = _tabs;
        Loaded += (_, _) => _ = AddTabAsync(null);
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

    private async Task AddTabAsync(string? url)
    {
        var tab = new BrowserTab();
        WebHost.Children.Add(tab.View);
        _tabs.Add(tab);
        SelectTab(tab); // shows this tab's view before the engine starts

        try
        {
            var environment = await _browserEnvironment;
            if (!_tabs.Contains(tab)) return;
            await tab.View.EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex)
        {
            if (!_tabs.Contains(tab)) return;

            MessageBox.Show(
                "Could not start the browser engine (WebView2).\n\n" + ex.Message,
                "Butane", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!_tabs.Contains(tab)) return;

        var core = tab.View.CoreWebView2;

        core.SourceChanged += (_, _) =>
        {
            if (tab == _current) AddressBar.Text = DisplayUrl(core.Source);
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
                SetStatus(tab, $"LOADED IN {Stopwatch.GetElapsedTime(tab.LoadStart).TotalSeconds:0.00}S");
            else SetStatus(tab, "FAILED TO LOAD");
            if (tab == _current) UpdateNavButtons();
        };

        // Links that want a new window open in a new tab.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            _ = AddTabAsync(e.Uri);
        };

        // Tracker blocking.
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;

        if (url == null) core.NavigateToString(NewTabHtml);
        else core.Navigate(url);
    }

    private void SelectTab(BrowserTab tab, bool focusView = false)
    {
        if (!_tabs.Contains(tab)) return;

        _current = tab;
        foreach (var t in _tabs)
            t.View.Visibility = t == tab ? Visibility.Visible : Visibility.Collapsed;

        TabList.SelectedItem = tab;
        AddressBar.Text = DisplayUrl(tab.View.CoreWebView2?.Source);
        StatusText.Text = tab.Status;
        UpdateWindowTitle();
        UpdateNavButtons();
        if (focusView) tab.View.Focus();
    }

    private void CloseTab(BrowserTab tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0) return;

        bool wasCurrent = tab == _current;
        if (wasCurrent) _current = null;

        _tabs.Remove(tab);
        WebHost.Children.Remove(tab.View);
        tab.View.Dispose();

        if (_tabs.Count == 0)
        {
            _current = null;
            _ = AddTabAsync(null);
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

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BrowserTab tab }) CloseTab(tab);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

        switch (e.Key)
        {
            case Key.T:
                OpenNewTab();
                e.Handled = true;
                break;
            case Key.W:
                if (_current != null) CloseTab(_current);
                e.Handled = true;
                break;
            case Key.Tab:
            case Key.PageDown:
                CycleTabs((Keyboard.Modifiers & ModifierKeys.Shift) == 0 ? 1 : -1);
                e.Handled = true;
                break;
            case Key.PageUp:
                CycleTabs(-1);
                e.Handled = true;
                break;
            case Key.L:
                AddressBar.Focus();
                AddressBar.SelectAll();
                e.Handled = true;
                break;
            case >= Key.D1 and <= Key.D8:
                SelectTabAtIndex((int)e.Key - (int)Key.D1);
                e.Handled = true;
                break;
            case Key.D9:
                if (_tabs.Count > 0) SelectTab(_tabs[^1]);
                e.Handled = true;
                break;
        }
    }

    private void OpenNewTab()
    {
        _ = AddTabAsync(null);
        AddressBar.Focus();
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
        _blockTrackers = ShieldToggle.IsChecked == true;
        ShieldState.Text = _blockTrackers ? "ON" : "OFF";
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
        Title = string.IsNullOrWhiteSpace(_current?.Title) ? "Butane" : _current!.Title + " - Butane";
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
        if (e.Key != Key.Enter) return;

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

        return SearchUrl + Uri.EscapeDataString(input);
    }
}