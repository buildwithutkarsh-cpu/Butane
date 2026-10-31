using System.ComponentModel;
using Microsoft.Web.WebView2.Wpf;

namespace Butane;

// One browser tab: its own WebView2 plus the text shown in the sidebar.
public class BrowserTab : INotifyPropertyChanged
{
    private string _title = "";

    public WebView2 View { get; } = new()
    {
        // Dark background so a new tab never flashes white.
        DefaultBackgroundColor = System.Drawing.Color.FromArgb(17, 17, 17)
    };

    public string Status { get; set; } = "READY";

    // Timestamp taken when a navigation starts (used for the load-time readout).
    public long LoadStart { get; set; }

    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? "";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
        }
    }

    // What the sidebar shows.
    public string Display => string.IsNullOrWhiteSpace(_title) ? "NEW TAB" : _title.ToUpperInvariant();

    public event PropertyChangedEventHandler? PropertyChanged;
}
