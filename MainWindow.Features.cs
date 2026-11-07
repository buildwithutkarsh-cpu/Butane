using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace Butane;

// Chrome/Edge-style features: find, zoom, print, fullscreen, reopen tab, bookmarks bar,
// address suggestions, tab menu + drag reorder + pinning + mute, session restore, reader mode,
// dev tools, screenshots, import/export bookmarks, clear data, main menu, global shortcuts.
public partial class MainWindow
{
    private static readonly HttpClient SuggestHttp = new() { Timeout = TimeSpan.FromSeconds(3) };
    private static string SessionFile => Path.Combine(BrowserDataFolder, "session.json");
    private static bool _sessionHandled;

    private readonly Stack<string> _closedTabs = new();
    private CancellationTokenSource? _suggestCts;
    private bool _isFullscreen;
    private WindowState _stateBeforeFullscreen = WindowState.Normal;
    private WindowStyle _styleBeforeFullscreen = WindowStyle.SingleBorderWindow;
    private Point _dragStart;
    private BrowserTab? _dragTab;

    private sealed record Suggestion(string Display, string Target);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    // ---------- Per-tab hooks (called from AddTabAsync) ----------

    private void HookTabFeatures(BrowserTab tab, CoreWebView2 core)
    {
        // Chromium's built-in password manager and autofill (stored in the normal profile only).
        core.Settings.IsPasswordAutosaveEnabled = !_isPrivate && _settings.SavePasswords;
        core.Settings.IsGeneralAutofillEnabled = !_isPrivate && _settings.Autofill;

        core.StatusBarTextChanged += (_, _) =>
        {
            if (tab != _current) return;
            StatusText.Text = string.IsNullOrEmpty(core.StatusBarText) ? tab.Status : core.StatusBarText;
        };

        core.FaviconChanged += async (_, _) =>
        {
            try
            {
                using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
                if (stream == null) { tab.Favicon = null; return; }
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                tab.Favicon = bitmap;
            }
            catch
            {
                // Tab closed or icon unreadable; keep the previous icon.
            }
        };

        core.IsDocumentPlayingAudioChanged += (_, _) => tab.IsPlayingAudio = core.IsDocumentPlayingAudio;
        core.ContainsFullScreenElementChanged += (_, _) => SetFullscreen(core.ContainsFullScreenElement);

        // Keyboard shortcuts keep working while a web page has focus: a small script forwards them.
        _ = core.AddScriptToExecuteOnDocumentCreatedAsync(ShortcutScript);
        core.WebMessageReceived += (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("shortcut", out JsonElement shortcut) ||
                    root.GetProperty("key").GetString() is not { } name ||
                    KeyFromName(name) is not { } key)
                    return;

                HandleShortcut(
                    key,
                    root.GetProperty("ctrl").GetBoolean(),
                    root.GetProperty("shift").GetBoolean(),
                    root.GetProperty("alt").GetBoolean());
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
            {
                // Not one of our messages.
            }
        };
    }

    private const string ShortcutScript = """
        (function () {
          var letters = 'nwtldfpuhjrob';
          window.addEventListener('keydown', function (e) {
            var k = e.key, lk = k.length === 1 ? k.toLowerCase() : k;
            var ctrl = e.ctrlKey || e.metaKey, alt = e.altKey, shift = e.shiftKey, send = false;
            if (k === 'F5' || k === 'F11' || k === 'F12') send = true;
            else if (ctrl && !alt) {
              send = k === 'Tab' || k === 'PageDown' || k === 'PageUp' ||
                     (lk.length === 1 && (letters.indexOf(lk) >= 0 || '0123456789+=-_'.indexOf(lk) >= 0));
            } else if (alt && !ctrl) {
              send = k === 'ArrowLeft' || k === 'ArrowRight' || k === 'Home';
            }
            if (!send) return;
            e.preventDefault();
            e.stopPropagation();
            window.chrome.webview.postMessage({ shortcut: true, key: k, ctrl: ctrl, shift: shift, alt: alt });
          }, true);
        })();
        """;

    private static Key? KeyFromName(string name)
    {
        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z') return Key.A + (c - 'A');
            if (c is >= '0' and <= '9') return Key.D0 + (c - '0');
            return c switch
            {
                '+' or '=' => Key.OemPlus,
                '-' or '_' => Key.OemMinus,
                _ => null
            };
        }

        return name switch
        {
            "Tab" => Key.Tab,
            "PageDown" => Key.PageDown,
            "PageUp" => Key.PageUp,
            "F5" => Key.F5,
            "F11" => Key.F11,
            "F12" => Key.F12,
            "ArrowLeft" => Key.Left,
            "ArrowRight" => Key.Right,
            "Home" => Key.Home,
            _ => null
        };
    }

    // ---------- Keyboard shortcuts ----------

    private bool HandleShortcut(Key key, bool ctrl, bool shift, bool alt)
    {
        if (alt && !ctrl)
        {
            switch (key)
            {
                case Key.Left: BackButton_Click(this, new RoutedEventArgs()); return true;
                case Key.Right: ForwardButton_Click(this, new RoutedEventArgs()); return true;
                case Key.Home: GoHome(); return true;
            }
            return false;
        }

        if (!ctrl)
        {
            switch (key)
            {
                case Key.F5: _current?.View.CoreWebView2?.Reload(); return true;
                case Key.F11: SetFullscreen(!_isFullscreen); return true;
                case Key.F12: _current?.View.CoreWebView2?.OpenDevToolsWindow(); return true;
                case Key.Escape:
                    if (shift) { _current?.View.CoreWebView2?.OpenTaskManagerWindow(); return true; }
                    if (FindBar.Visibility == Visibility.Visible) { CloseFind(); return true; }
                    if (SuggestPopup.IsOpen) { CloseSuggestions(); return true; }
                    if (_isFullscreen) { SetFullscreen(false); return true; }
                    return false;
            }
            return false;
        }

        switch (key)
        {
            case Key.N: OpenNewWindow(shift); return true;
            case Key.T:
                if (shift) ReopenClosedTab(); else OpenNewTab();
                return true;
            case Key.W:
                // Deferred: closing a tab from inside its own key event would dispose the view mid-callback.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (shift) Close();
                    else if (_current != null) CloseTab(_current);
                }));
                return true;
            case Key.Tab:
            case Key.PageDown:
                CycleTabs(shift ? -1 : 1); return true;
            case Key.PageUp: CycleTabs(-1); return true;
            case Key.L:
                AddressBar.Focus();
                AddressBar.SelectAll();
                return true;
            case Key.D: ToggleBookmark(); return true;
            case >= Key.D1 and <= Key.D8: SelectTabAtIndex((int)key - (int)Key.D1); return true;
            case Key.D9:
                if (_tabs.Count > 0) SelectTab(_tabs[^1]);
                return true;
            case Key.F: ShowFind(); return true;
            case Key.P: _current?.View.CoreWebView2?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); return true;
            case Key.U: ViewSource(); return true;
            case Key.H: if (!_isPrivate) OpenLibrary(1); return true;
            case Key.J: OpenLibrary(2); return true;
            case Key.R: _current?.View.CoreWebView2?.Reload(); return true;
            case Key.OemPlus:
            case Key.Add: ChangeZoom(0.1); return true;
            case Key.OemMinus:
            case Key.Subtract: ChangeZoom(-0.1); return true;
            case Key.D0:
            case Key.NumPad0: ChangeZoom(0); return true;
            case Key.O:
                if (shift && !_isPrivate) { OpenLibrary(0); return true; }
                return false;
            case Key.B:
                if (shift) { ToggleBookmarksBar(); return true; }
                return false;
            case Key.Delete:
                if (shift) { ClearBrowsingData(); return true; }
                return false;
        }

        return false;
    }

    // ---------- Home, zoom, print, capture ----------

    private string? HomeUrl() =>
        string.IsNullOrWhiteSpace(_settings.HomePage) ? null : ToUrl(_settings.HomePage);

    private void GoHome()
    {
        var core = _current?.View.CoreWebView2;
        if (core == null) return;
        string? home = _isPrivate ? null : HomeUrl();
        if (home != null) core.Navigate(home);
        else core.NavigateToString(_isPrivate ? PrivateNewTabHtml : NewTabHtml);
    }

    private void ChangeZoom(double delta)
    {
        if (_current == null) return;
        double zoom = delta == 0
            ? 1.0
            : Math.Clamp(Math.Round((_current.View.ZoomFactor + delta) * 100) / 100, 0.25, 5.0);
        _current.View.ZoomFactor = zoom;
        SetStatus(_current, $"ZOOM {zoom * 100:0}%");
    }

    private async void SavePageAsPdf()
    {
        var core = _current?.View.CoreWebView2;
        if (core == null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF document|*.pdf",
            FileName = SafeFileName(core.DocumentTitle, "page") + ".pdf"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            await core.PrintToPdfAsync(dialog.FileName);
            SetStatus(_current!, "SAVED PDF");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not save the PDF.\n\n" + ex.Message, "Butane", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void CaptureScreenshot()
    {
        var core = _current?.View.CoreWebView2;
        if (core == null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG image|*.png",
            FileName = SafeFileName(core.DocumentTitle, "screenshot") + ".png"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            await using var file = File.Create(dialog.FileName);
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
            SetStatus(_current!, "SCREENSHOT SAVED");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not save the screenshot.\n\n" + ex.Message, "Butane", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string SafeFileName(string? name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 80 ? name[..80] : name;
    }

    // ---------- Dev tools, source, reader mode ----------

    private void ViewSource()
    {
        string? url = _current?.View.CoreWebView2?.Source;
        if (IsWeb(url)) StartAddTab("view-source:" + url);
    }

    private const string ReaderScript = """
        (function () {
          var root = document.querySelector('article') || document.querySelector('main') ||
                     document.querySelector('[role=main]');
          if (!root) {
            var best = null, max = 0;
            document.querySelectorAll('div,section').forEach(function (el) {
              var total = 0;
              el.querySelectorAll(':scope > p').forEach(function (p) { total += p.innerText.length; });
              if (total > max) { max = total; best = el; }
            });
            root = best || document.body;
          }
          var title = document.title;
          var nodes = root.querySelectorAll('h1,h2,h3,h4,p,li,blockquote,pre,img');
          var out = document.createElement('div');
          out.className = 'reader';
          var h = document.createElement('h1');
          h.textContent = title;
          out.appendChild(h);
          nodes.forEach(function (n) {
            var tag = n.tagName.toLowerCase();
            if (tag === 'img') {
              if (n.src && n.naturalWidth > 200) {
                var img = document.createElement('img');
                img.src = n.src;
                out.appendChild(img);
              }
              return;
            }
            var text = (n.innerText || '').trim();
            if (!text) return;
            var el = document.createElement(tag === 'li' ? 'p' : tag);
            el.textContent = (tag === 'li' ? '\u2022 ' : '') + text;
            out.appendChild(el);
          });
          document.head.innerHTML = '<meta charset="utf-8"><style>' +
            'body{background:#111;color:#e6e6e6;font:19px/1.7 Georgia,serif;margin:0}' +
            '.reader{max-width:720px;margin:0 auto;padding:48px 24px}' +
            'h1,h2,h3,h4{font-family:Consolas,monospace;color:#fff;line-height:1.25}' +
            'img{max-width:100%;height:auto}blockquote{border-left:3px solid #555;margin-left:0;padding-left:16px;color:#bbb}' +
            'pre{background:#1b1b1b;padding:12px;overflow:auto}' +
            '</style>';
          document.body.removeAttribute('style');
          document.body.className = '';
          document.body.innerHTML = '';
          document.body.appendChild(out);
          window.scrollTo(0, 0);
        })();
        """;

    private async void EnterReaderMode()
    {
        var core = _current?.View.CoreWebView2;
        if (core == null || !IsWeb(core.Source)) return;
        try { await core.ExecuteScriptAsync(ReaderScript); }
        catch { /* page blocked scripting; nothing to do */ }
    }

    // ---------- Fullscreen ----------

    private void SetFullscreen(bool on)
    {
        if (on == _isFullscreen) return;
        _isFullscreen = on;

        if (on)
        {
            _stateBeforeFullscreen = WindowState;
            _styleBeforeFullscreen = WindowStyle;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
            SidebarPanel.Visibility = Visibility.Collapsed;
            TopPanel.Visibility = Visibility.Collapsed;
            BookmarksBarPanel.Visibility = Visibility.Collapsed;
            FindBar.Visibility = Visibility.Collapsed;
            BottomPanel.Visibility = Visibility.Collapsed;
            SidebarColumn.Width = new GridLength(0);
            TopRow.Height = new GridLength(0);
            BottomRow.Height = new GridLength(0);
        }
        else
        {
            WindowStyle = _styleBeforeFullscreen;
            WindowState = _stateBeforeFullscreen;
            SidebarPanel.Visibility = Visibility.Visible;
            TopPanel.Visibility = Visibility.Visible;
            BottomPanel.Visibility = Visibility.Visible;
            SidebarColumn.Width = new GridLength(260);
            TopRow.Height = new GridLength(96);
            BottomRow.Height = new GridLength(40);
            ApplyChromeSettings();
        }

        // Pages that requested element fullscreen need to be told when the user leaves with F11/Esc.
        if (!on && _current?.View.CoreWebView2?.ContainsFullScreenElement == true)
            _ = _current.View.CoreWebView2.ExecuteScriptAsync("document.exitFullscreen && document.exitFullscreen()");
    }

    // ---------- Find in page ----------

    private void ShowFind()
    {
        FindBar.Visibility = Visibility.Visible;
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        FindResult.Text = "";
        _ = _current?.View.CoreWebView2?.ExecuteScriptAsync("window.getSelection().removeAllRanges()");
        _current?.View.Focus();
    }

    private async Task FindAsync(bool backwards, bool fromStart)
    {
        var core = _current?.View.CoreWebView2;
        string text = FindBox.Text;
        if (core == null || text.Length == 0)
        {
            FindResult.Text = "";
            return;
        }

        string quoted = JsonSerializer.Serialize(text);
        string caseSensitive = FindCase.IsChecked == true ? "true" : "false";
        string back = backwards ? "true" : "false";
        string reset = fromStart ? "window.getSelection().removeAllRanges();" : "";
        try
        {
            string result = await core.ExecuteScriptAsync(
                $"(function(){{{reset}return window.find({quoted},{caseSensitive},{back},true,false,false,false);}})()");
            FindResult.Text = result == "true" ? "" : "NOT FOUND";
        }
        catch
        {
            FindResult.Text = "";
        }
    }

    private void FindBox_TextChanged(object sender, RoutedEventArgs e) => _ = FindAsync(false, true);

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = FindAsync((Keyboard.Modifiers & ModifierKeys.Shift) != 0, false);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => _ = FindAsync(false, false);
    private void FindPrev_Click(object sender, RoutedEventArgs e) => _ = FindAsync(true, false);
    private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFind();

    // ---------- Closed tabs and session ----------

    private void RememberClosedTab(BrowserTab tab)
    {
        if (_isClosing) return;
        string? url = tab.View.CoreWebView2?.Source;
        if (IsWeb(url)) _closedTabs.Push(url!);
    }

    private void ReopenClosedTab()
    {
        if (_closedTabs.Count > 0) StartAddTab(_closedTabs.Pop());
    }

    private void StartInitialTabs()
    {
        if (!_isPrivate && !_sessionHandled)
        {
            _sessionHandled = true;
            if (_settings.RestoreSession)
            {
                var urls = LoadSession();
                if (urls.Count > 0)
                {
                    foreach (string url in urls) StartAddTab(url);
                    return;
                }
            }
        }

        string? home = _isPrivate ? null : HomeUrl();
        StartAddTab(home);
    }

    private static List<string> LoadSession()
    {
        try
        {
            if (!File.Exists(SessionFile)) return new List<string>();
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(SessionFile))?
                .Where(u => IsWeb(u)).ToList() ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static void SaveSession()
    {
        if (!_settings.RestoreSession) return;
        try
        {
            var urls = NormalWindows
                .SelectMany(w => w._tabs)
                .Select(t => t.View.CoreWebView2?.Source)
                .Where(u => IsWeb(u))
                .Select(u => u!)
                .ToList();
            Directory.CreateDirectory(BrowserDataFolder);
            File.WriteAllText(SessionFile, JsonSerializer.Serialize(urls));
        }
        catch
        {
            // Session saving is best effort.
        }
    }

    // ---------- Chrome settings: bookmarks bar ----------

    private void ApplyChromeSettings()
    {
        if (BookmarksBarPanel == null) return;
        if (!_isPrivate && BookmarksBar.ItemsSource == null)
            BookmarksBar.ItemsSource = BrowserLibraryStore.Bookmarks;

        BookmarksBarPanel.Visibility = !_isPrivate && !_isFullscreen && _settings.ShowBookmarksBar
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ToggleBookmarksBar()
    {
        if (_isPrivate) return;
        _settings.ShowBookmarksBar = !_settings.ShowBookmarksBar;
        try { BrowserSettingsStore.Save(_settings); }
        catch { /* shown for this session even if saving fails */ }
        foreach (var window in Application.Current.Windows.OfType<MainWindow>())
            window.ApplyChromeSettings();
    }

    private void BookmarkBarItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BookmarkEntry bookmark }) NavigateTo(bookmark.Url);
    }

    private void BookmarkBarOpenNewTab_Click(object sender, RoutedEventArgs e)
    {
        if (TargetOfMenu(sender)?.DataContext is BookmarkEntry bookmark) StartAddTab(bookmark.Url);
    }

    private void BookmarkBarRemove_Click(object sender, RoutedEventArgs e)
    {
        if (TargetOfMenu(sender)?.DataContext is not BookmarkEntry bookmark) return;
        BrowserLibraryStore.Bookmarks.Remove(bookmark);
        try { SaveLibraryData(); }
        catch (Exception ex)
        {
            MessageBox.Show("Could not save bookmarks.\n\n" + ex.Message, "Butane", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static FrameworkElement? TargetOfMenu(object sender) =>
        (sender as MenuItem)?.Parent is ContextMenu { PlacementTarget: FrameworkElement target } ? target : null;

    // ---------- Security indicator ----------

    private void UpdateSecurityIcon()
    {
        if (SecurityIcon == null) return;
        string? source = _current?.View.CoreWebView2?.Source;
        if (!IsWeb(source))
        {
            SecurityIcon.Text = "\uE721";
            SecurityIcon.Foreground = Brushes.White;
            SecurityIcon.ToolTip = null;
            return;
        }

        bool https = source!.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        SecurityIcon.Text = https ? "\uE72E" : "\uE785";
        SecurityIcon.Foreground = https ? Brushes.White : new SolidColorBrush(Color.FromRgb(255, 170, 60));
        SecurityIcon.ToolTip = https ? "Connection is secure (HTTPS)" : "Not secure (HTTP)";
    }

    // ---------- Address bar suggestions ----------

    private void CloseSuggestions()
    {
        _suggestCts?.Cancel();
        SuggestPopup.IsOpen = false;
        SuggestList.ItemsSource = null;
    }

    private void ShowSuggestions(List<Suggestion> items)
    {
        if (items.Count == 0) { CloseSuggestions(); return; }
        SuggestBorder.Width = Math.Max(360, AddressBar.ActualWidth);
        SuggestList.ItemsSource = items;
        SuggestList.SelectedIndex = -1;
        SuggestPopup.IsOpen = true;
    }

    private async void AddressBar_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!AddressBar.IsKeyboardFocused) return;

        string text = AddressBar.Text.Trim();
        if (text.Length == 0) { CloseSuggestions(); return; }

        var items = new List<Suggestion>();
        if (!_isPrivate)
        {
            foreach (var b in BrowserLibraryStore.Bookmarks
                         .Where(b => Matches(b.Title, text) || Matches(b.Url, text)).Take(3))
                items.Add(new Suggestion($"BOOKMARK  {b.Title}  |  {b.Url}", b.Url));

            foreach (var h in BrowserLibraryStore.History
                         .Where(h => Matches(h.Title, text) || Matches(h.Url, text))
                         .GroupBy(h => h.Url).Select(g => g.First()).Take(4))
                items.Add(new Suggestion($"HISTORY  {h.Title}  |  {h.Url}", h.Url));
        }

        string typedUrl = ToUrl(text);
        bool looksLikeAddress = typedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                                !typedUrl.StartsWith(_settings.GetSearchUrl(), StringComparison.OrdinalIgnoreCase);
        items.Add(new Suggestion(looksLikeAddress ? $"OPEN  {text}" : $"SEARCH  {text}", typedUrl));
        ShowSuggestions(items);

        if (!_settings.SearchSuggestions || text.Length < 2 || looksLikeAddress) return;

        _suggestCts?.Cancel();
        var cts = _suggestCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(180, cts.Token);
            var remote = await FetchSearchSuggestionsAsync(text, cts.Token);
            if (cts.IsCancellationRequested || remote.Count == 0 || AddressBar.Text.Trim() != text) return;

            foreach (string phrase in remote.Take(5))
                items.Add(new Suggestion($"SEARCH  {phrase}", _settings.GetSearchUrl() + Uri.EscapeDataString(phrase)));
            ShowSuggestions(items);
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or JsonException)
        {
            // Suggestions are optional; ignore network failures and cancellations.
        }
    }

    private static bool Matches(string? value, string text) =>
        value != null && value.Contains(text, StringComparison.OrdinalIgnoreCase);

    private async Task<List<string>> FetchSearchSuggestionsAsync(string query, CancellationToken token)
    {
        string encoded = Uri.EscapeDataString(query);
        string url = _settings.SearchEngine switch
        {
            "Google" => $"https://suggestqueries.google.com/complete/search?client=firefox&q={encoded}",
            "Bing" => $"https://api.bing.com/osjson.aspx?query={encoded}",
            _ => $"https://duckduckgo.com/ac/?q={encoded}"
        };

        string json = await SuggestHttp.GetStringAsync(url, token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var phrases = new List<string>();
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return phrases;

        var first = root[0];
        if (first.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in root.EnumerateArray())
                if (item.TryGetProperty("phrase", out var phrase) && phrase.GetString() is { } s)
                    phrases.Add(s);
        }
        else if (root.GetArrayLength() > 1 && root[1].ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root[1].EnumerateArray())
                if (item.GetString() is { } s)
                    phrases.Add(s);
        }

        return phrases;
    }

    // Returns true when the key was consumed by the suggestion list.
    private bool HandleSuggestKeys(KeyEventArgs e)
    {
        if (!SuggestPopup.IsOpen) return false;

        int count = SuggestList.Items.Count;
        switch (e.Key)
        {
            case Key.Down:
                SuggestList.SelectedIndex = Math.Min(count - 1, SuggestList.SelectedIndex + 1);
                e.Handled = true;
                return true;
            case Key.Up:
                SuggestList.SelectedIndex = Math.Max(-1, SuggestList.SelectedIndex - 1);
                e.Handled = true;
                return true;
            case Key.Escape:
                CloseSuggestions();
                e.Handled = true;
                return true;
            case Key.Enter when SuggestList.SelectedItem is Suggestion suggestion:
                ChooseSuggestion(suggestion);
                e.Handled = true;
                return true;
        }

        return false;
    }

    private void ChooseSuggestion(Suggestion suggestion)
    {
        CloseSuggestions();
        var tab = _current;
        var core = tab?.View.CoreWebView2;
        if (tab == null || core == null) return;
        core.Navigate(suggestion.Target);
        tab.View.Focus();
    }

    private void SuggestList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            var item = ItemsControl.ContainerFromElement(SuggestList, source) as ListBoxItem;
            if (item?.DataContext is Suggestion suggestion) ChooseSuggestion(suggestion);
        }
    }

    private void AddressBar_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CloseSuggestions();

    private void Window_Deactivated(object? sender, EventArgs e) => CloseSuggestions();

    // ---------- Tab context menu, pinning, mute, drag reorder ----------

    private static BrowserTab? TabOfMenu(object sender) => TargetOfMenu(sender)?.DataContext as BrowserTab;

    private void TabMenu_Pin_Click(object sender, RoutedEventArgs e)
    {
        if (TabOfMenu(sender) is not { } tab) return;
        tab.IsPinned = !tab.IsPinned;

        int pinnedOthers = _tabs.Count(t => t.IsPinned && t != tab);
        int from = _tabs.IndexOf(tab);
        int to = tab.IsPinned ? pinnedOthers : _tabs.Count - 1;
        if (from != to && from >= 0) _tabs.Move(from, to);
        if (_current != null) TabList.SelectedItem = _current;
    }

    private void TabMenu_Mute_Click(object sender, RoutedEventArgs e)
    {
        if (TabOfMenu(sender) is not { } tab || tab.View.CoreWebView2 is not { } core) return;
        core.IsMuted = !core.IsMuted;
        tab.IsMuted = core.IsMuted;
    }

    private void TabMenu_Reload_Click(object sender, RoutedEventArgs e) =>
        TabOfMenu(sender)?.View.CoreWebView2?.Reload();

    private void TabMenu_Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (TabOfMenu(sender) is not { } tab) return;
        string? url = tab.View.CoreWebView2?.Source;
        StartAddTab(IsWeb(url) ? url : null);
    }

    private void TabMenu_CloseOthers_Click(object sender, RoutedEventArgs e)
    {
        if (TabOfMenu(sender) is not { } tab) return;
        foreach (var other in _tabs.Where(t => t != tab && !t.IsPinned).ToList()) CloseTab(other);
    }

    private void TabMenu_CloseBelow_Click(object sender, RoutedEventArgs e)
    {
        if (TabOfMenu(sender) is not { } tab) return;
        int index = _tabs.IndexOf(tab);
        foreach (var other in _tabs.Skip(index + 1).Where(t => !t.IsPinned).ToList()) CloseTab(other);
    }

    private void TabMenu_Close_Click(object sender, RoutedEventArgs e)
    {
        if (TabOfMenu(sender) is { } tab) CloseTab(tab);
    }

    private static BrowserTab? TabAt(object? source)
    {
        var d = source as DependencyObject;
        while (d != null && d is not ListBoxItem)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        return (d as ListBoxItem)?.DataContext as BrowserTab;
    }

    private void TabList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragTab = TabAt(e.OriginalSource);
    }

    private void TabList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragTab == null) return;

        var delta = _dragStart - e.GetPosition(null);
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var tab = _dragTab;
        _dragTab = null;
        DragDrop.DoDragDrop(TabList, tab, DragDropEffects.Move);
    }

    private void TabList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(BrowserTab)) is not BrowserTab source) return;
        var target = TabAt(e.OriginalSource);
        if (target == null || target == source || source.IsPinned != target.IsPinned) return;

        int from = _tabs.IndexOf(source);
        int to = _tabs.IndexOf(target);
        if (from < 0 || to < 0) return;

        _tabs.Move(from, to);
        if (_current != null) TabList.SelectedItem = _current;
    }

    // ---------- Bookmark import / export ----------

    private void ImportBookmarks()
    {
        if (_isPrivate) return;

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var sources = new (string Name, string Path)[]
        {
            ("Chrome", System.IO.Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Bookmarks")),
            ("Edge", System.IO.Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Bookmarks")),
            ("Brave", System.IO.Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Bookmarks"))
        };
        var found = sources.Where(s => File.Exists(s.Path)).ToList();

        var files = new List<string>();
        if (found.Count > 0)
        {
            var answer = MessageBox.Show(
                $"Import bookmarks from {string.Join(", ", found.Select(f => f.Name))}?\n\n" +
                "Choose No to pick a Chromium Bookmarks file yourself.",
                "Import bookmarks", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;
            if (answer == MessageBoxResult.Yes) files.AddRange(found.Select(f => f.Path));
        }

        if (files.Count == 0)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Select a Chromium Bookmarks file" };
            if (dialog.ShowDialog(this) != true) return;
            files.Add(dialog.FileName);
        }

        int added = 0;
        var existing = new HashSet<string>(
            BrowserLibraryStore.Bookmarks.Select(b => b.Url), StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (!doc.RootElement.TryGetProperty("roots", out var roots)) continue;
                foreach (var root in roots.EnumerateObject())
                    if (root.Value.ValueKind == JsonValueKind.Object)
                        added += ImportBookmarkNode(root.Value, existing);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                MessageBox.Show($"Could not read {file}.\n\n{ex.Message}", "Import bookmarks",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        try { SaveLibraryData(); }
        catch (Exception ex)
        {
            MessageBox.Show("Could not save bookmarks.\n\n" + ex.Message, "Butane", MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        MessageBox.Show($"Imported {added} bookmark(s).", "Import bookmarks", MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static int ImportBookmarkNode(JsonElement node, HashSet<string> existing)
    {
        int added = 0;
        string type = node.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        if (type == "url" && node.TryGetProperty("url", out var u) && u.GetString() is { } url && IsWeb(url))
        {
            if (existing.Add(url))
            {
                string name = node.TryGetProperty("name", out var n) ? n.GetString() ?? url : url;
                BrowserLibraryStore.Bookmarks.Add(new BookmarkEntry { Title = name, Url = url });
                added++;
            }
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray())
                added += ImportBookmarkNode(child, existing);

        return added;
    }

    private void ExportBookmarks()
    {
        if (_isPrivate) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Bookmarks HTML|*.html",
            FileName = "butane-bookmarks.html"
        };
        if (dialog.ShowDialog(this) != true) return;

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE NETSCAPE-Bookmark-file-1>");
        sb.AppendLine("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">");
        sb.AppendLine("<TITLE>Bookmarks</TITLE><H1>Bookmarks</H1><DL><p>");
        foreach (var b in BrowserLibraryStore.Bookmarks)
            sb.AppendLine($"<DT><A HREF=\"{WebUtility.HtmlEncode(b.Url)}\">{WebUtility.HtmlEncode(b.Title)}</A>");
        sb.AppendLine("</DL><p>");

        try { File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8); }
        catch (Exception ex)
        {
            MessageBox.Show("Could not export bookmarks.\n\n" + ex.Message, "Butane", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ---------- Clear browsing data ----------

    private async void ClearBrowsingData()
    {
        var answer = MessageBox.Show(
            "Clear cookies, cache, site data and browsing history?\n\nBookmarks and downloads are kept.",
            "Clear browsing data", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            var core = _current?.View.CoreWebView2;
            if (core != null) await core.Profile.ClearBrowsingDataAsync();
            if (!_isPrivate)
            {
                BrowserLibraryStore.History.Clear();
                SaveLibraryData();
            }

            if (_current != null) SetStatus(_current, "BROWSING DATA CLEARED");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not clear browsing data.\n\n" + ex.Message, "Butane", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ---------- Main menu ----------

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B)),
            Foreground = Brushes.White,
            PlacementTarget = MenuButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        void Add(string header, Action action, string? gesture = null, bool enabled = true)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        void Separator() => menu.Items.Add(new System.Windows.Controls.Separator());

        Add("New tab", OpenNewTab, "Ctrl+T");
        Add("New window", () => OpenNewWindow(false), "Ctrl+N");
        Add("New private window", () => OpenNewWindow(true), "Ctrl+Shift+N");
        Add("Reopen closed tab", ReopenClosedTab, "Ctrl+Shift+T", _closedTabs.Count > 0);
        Add("Home", GoHome, "Alt+Home");
        Separator();
        Add("Bookmarks", () => OpenLibrary(0), "Ctrl+Shift+O", !_isPrivate);
        Add("History", () => OpenLibrary(1), "Ctrl+H", !_isPrivate);
        Add("Downloads", () => OpenLibrary(2), "Ctrl+J");
        Add(_settings.ShowBookmarksBar ? "Hide bookmarks bar" : "Show bookmarks bar", ToggleBookmarksBar,
            "Ctrl+Shift+B", !_isPrivate);
        Add("Import bookmarks (Chrome / Edge / Brave)", ImportBookmarks, null, !_isPrivate);
        Add("Export bookmarks", ExportBookmarks, null, !_isPrivate);
        Separator();
        Add("Find in page", ShowFind, "Ctrl+F");
        Add("Zoom in", () => ChangeZoom(0.1), "Ctrl++");
        Add("Zoom out", () => ChangeZoom(-0.1), "Ctrl+-");
        Add("Reset zoom", () => ChangeZoom(0), "Ctrl+0");
        Add("Reader mode", EnterReaderMode);
        Add("Full screen", () => SetFullscreen(!_isFullscreen), "F11");
        Separator();
        Add("Print...", () => _current?.View.CoreWebView2?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser), "Ctrl+P");
        Add("Save page as PDF...", SavePageAsPdf);
        Add("Save screenshot...", CaptureScreenshot);
        Separator();
        Add("View page source", ViewSource, "Ctrl+U");
        Add("Developer tools", () => _current?.View.CoreWebView2?.OpenDevToolsWindow(), "F12");
        Add("Task manager", () => _current?.View.CoreWebView2?.OpenTaskManagerWindow(), "Shift+Esc");
        Separator();
        Add("Clear browsing data...", ClearBrowsingData, "Ctrl+Shift+Del");
        Add("Settings", () => SettingsButton_Click(this, new RoutedEventArgs()));

        menu.IsOpen = true;
    }
}
