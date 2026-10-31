using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace Butane;

public partial class MainWindow : Window
{
    private const string HomePage = "https://duckduckgo.com";
    private const string SearchUrl = "https://duckduckgo.com/?q=";

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitBrowserAsync();
    }

    private async Task InitBrowserAsync()
    {
        try
        {
            await Web.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not start the browser engine (WebView2).\n\n" + ex.Message,
                "Butane", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var core = Web.CoreWebView2;

        // Keep the address bar, title and buttons in sync with the page.
        core.SourceChanged += (_, _) => AddressBar.Text = core.Source;
        core.DocumentTitleChanged += (_, _) => Title = string.IsNullOrWhiteSpace(core.DocumentTitle)
            ? "Butane"
            : core.DocumentTitle + " - Butane";
        core.HistoryChanged += (_, _) => UpdateNavButtons();
        core.NavigationCompleted += (_, _) => UpdateNavButtons();

        // Open links that want a new window in this same tab (tabs come in the next step).
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            core.Navigate(e.Uri);
        };

        core.Navigate(HomePage);
    }

    private void UpdateNavButtons()
    {
        var core = Web.CoreWebView2;
        if (core == null) return;
        BackButton.IsEnabled = core.CanGoBack;
        ForwardButton.IsEnabled = core.CanGoForward;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2?.CanGoBack == true) Web.CoreWebView2.GoBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2?.CanGoForward == true) Web.CoreWebView2.GoForward();
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        Web.CoreWebView2?.Reload();
    }

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Web.CoreWebView2?.Navigate(ToUrl(AddressBar.Text));
        Web.Focus();
    }

    // Turns whatever was typed into a URL: full address, bare domain, or a search.
    private static string ToUrl(string input)
    {
        input = input.Trim();
        if (input.Length == 0) return "about:blank";

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return input;

        if (!input.Contains(' ') && input.Contains('.'))
            return "https://" + input;

        return SearchUrl + Uri.EscapeDataString(input);
    }
}
