   using System.ComponentModel;
   using System.Windows.Media;
   using Microsoft.Web.WebView2.Wpf;

namespace Butane;

// One browser tab: its own WebView2 plus the text shown in the sidebar.
public class BrowserTab : INotifyPropertyChanged
{
    private string _title = "";
    private bool _isSleeping;
    private bool _isPinned;
    private bool _isMuted;
    private bool _isPlayingAudio;
    private ImageSource? _favicon;

    public bool IsPinned
    {
        get => _isPinned;
        set { if (_isPinned == value) return; _isPinned = value; Notify(nameof(IsPinned)); Notify(nameof(Display)); }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set { if (_isMuted == value) return; _isMuted = value; Notify(nameof(IsMuted)); Notify(nameof(Display)); }
    }

    public bool IsPlayingAudio
    {
        get => _isPlayingAudio;
        set { if (_isPlayingAudio == value) return; _isPlayingAudio = value; Notify(nameof(IsPlayingAudio)); Notify(nameof(Display)); }
    }

    public ImageSource? Favicon
    {
        get => _favicon;
        set { _favicon = value; Notify(nameof(Favicon)); }
    }

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public WebView2 View { get; } = new()
    {
        // Dark background so a new tab never flashes white.
        DefaultBackgroundColor = System.Drawing.Color.FromArgb(17, 17, 17)
    };

    public string Status { get; set; } = "READY";

    // Timestamp taken when a navigation starts (used for the load-time readout).
    public long LoadStart { get; set; }

    public bool ShouldBeSuspended { get; set; }

    public bool SuspensionUpdateRunning { get; set; }

    public int OpenPopupCount { get; set; }

    public BrowserTab? PopupOpener { get; set; }

    public bool IsSleeping
    {
        get => _isSleeping;
        set
        {
            if (_isSleeping == value) return;
            _isSleeping = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSleeping)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
        }
    }

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
    public string Display =>
        (IsPinned ? "[PIN] " : "") +
        (string.IsNullOrWhiteSpace(_title) ? "NEW TAB" : _title.ToUpperInvariant()) +
        (IsMuted ? " [MUTED]" : IsPlayingAudio ? " [AUDIO]" : "") +
        (IsSleeping ? " [SLEEPING]" : "");

    public event PropertyChangedEventHandler? PropertyChanged;
}
