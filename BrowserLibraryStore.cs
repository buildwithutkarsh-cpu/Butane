using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace Butane;

internal sealed class BookmarkEntry
{
    public BookmarkEntry() { }

    public string Title { get; set; } = "";
    public string Url { get; set; } = "";

    [JsonIgnore]
    public string Display => $"{Title}  |  {Url}";
}

internal sealed class HistoryEntry
{
    public HistoryEntry() { }

    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime VisitedAt { get; set; }

    [JsonIgnore]
    public string Display => $"{VisitedAt:g}  |  {Title}  |  {Url}";
}

internal sealed class DownloadEntry : INotifyPropertyChanged
{
    private long _bytesReceived;
    private ulong? _totalBytesToReceive;
    private string _state = "IN PROGRESS";

    public DownloadEntry() { }

    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime StartedAt { get; set; }

    public long BytesReceived
    {
        get => _bytesReceived;
        set
        {
            if (_bytesReceived == value) return;
            _bytesReceived = value;
            NotifyChanged(nameof(Progress));
        }
    }

    public ulong? TotalBytesToReceive
    {
        get => _totalBytesToReceive;
        set
        {
            if (_totalBytesToReceive == value) return;
            _totalBytesToReceive = value;
            NotifyChanged(nameof(Progress));
        }
    }

    public string State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            NotifyChanged(nameof(Display));
        }
    }

    [JsonIgnore]
    public string Progress
    {
        get
        {
            if (TotalBytesToReceive is not > 0) return $"{FormatBytes(Math.Max(0, BytesReceived))} received";
            return $"{FormatBytes(Math.Max(0, BytesReceived))} / {FormatBytes(TotalBytesToReceive.Value)}";
        }
    }

    [JsonIgnore]
    public string Display => $"{State}  |  {FileName}  |  {Progress}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName == nameof(Progress))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
    }

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}

internal static class BrowserLibraryStore
{
    private static readonly string DataFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Butane", "library.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ObservableCollection<BookmarkEntry> Bookmarks { get; } = new();
    public static ObservableCollection<HistoryEntry> History { get; } = new();
    public static ObservableCollection<DownloadEntry> Downloads { get; } = new();

    static BrowserLibraryStore()
    {
        if (!File.Exists(DataFile)) return;

        try
        {
            var data = JsonSerializer.Deserialize<BrowserLibraryData>(File.ReadAllText(DataFile));
            if (data == null) throw new InvalidDataException("The saved browser library is empty.");

            foreach (var bookmark in data.Bookmarks ?? [])
                if (!string.IsNullOrWhiteSpace(bookmark.Url)) Bookmarks.Add(bookmark);
            foreach (var entry in data.History ?? [])
                if (!string.IsNullOrWhiteSpace(entry.Url)) History.Add(entry);
            foreach (var download in data.Downloads ?? [])
            {
                if (string.IsNullOrWhiteSpace(download.FilePath)) continue;
                if (download.State == "IN PROGRESS") download.State = "INTERRUPTED";
                Downloads.Add(download);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            MessageBox.Show(
                "Could not load saved bookmarks, history, and downloads.\n\n" + ex.Message,
                "Butane library", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DataFile)!);
        string temporaryFile = DataFile + ".tmp";
        File.WriteAllText(temporaryFile, JsonSerializer.Serialize(new BrowserLibraryData
        {
            Bookmarks = Bookmarks.ToList(),
            History = History.ToList(),
            Downloads = Downloads.ToList()
        }, JsonOptions));
        File.Move(temporaryFile, DataFile, overwrite: true);
    }

}

internal sealed class BrowserLibraryData
{
    public BrowserLibraryData() { }

    public List<BookmarkEntry>? Bookmarks { get; set; }
    public List<HistoryEntry>? History { get; set; }
    public List<DownloadEntry>? Downloads { get; set; }
}
