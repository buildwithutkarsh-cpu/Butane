using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Butane;

public partial class LibraryWindow : Window
{
    private readonly Action<string> _openUrl;
    private readonly Action _save;
    private readonly Action<DownloadEntry> _cancelDownload;

    internal LibraryWindow(
        ObservableCollection<BookmarkEntry> bookmarks,
        ObservableCollection<HistoryEntry> history,
        ObservableCollection<DownloadEntry> downloads,
        Action<string> openUrl,
        Action save,
        Action<DownloadEntry> cancelDownload,
        bool isPrivate,
        int selectedTab)
    {
        InitializeComponent();
        _openUrl = openUrl;
        _save = save;
        _cancelDownload = cancelDownload;
        BookmarksList.ItemsSource = bookmarks;
        HistoryList.ItemsSource = history;
        DownloadsList.ItemsSource = downloads;

        if (isPrivate)
        {
            BookmarksTab.Visibility = Visibility.Collapsed;
            HistoryTab.Visibility = Visibility.Collapsed;
            selectedTab = 2;
        }
        LibraryTabs.SelectedIndex = selectedTab;
    }

    private void OpenBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (BookmarksList.SelectedItem is BookmarkEntry bookmark) _openUrl(bookmark.Url);
    }

    private void RemoveBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (BookmarksList.SelectedItem is not BookmarkEntry bookmark) return;
        if (BookmarksList.ItemsSource is ObservableCollection<BookmarkEntry> bookmarks)
        {
            int index = bookmarks.IndexOf(bookmark);
            bookmarks.Remove(bookmark);
            SaveChanges(() => bookmarks.Insert(index, bookmark));
        }
    }

    private void OpenHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryEntry entry) _openUrl(entry.Url);
    }

    private void RemoveHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryEntry entry) return;
        if (HistoryList.ItemsSource is ObservableCollection<HistoryEntry> history)
        {
            int index = history.IndexOf(entry);
            history.Remove(entry);
            SaveChanges(() => history.Insert(index, entry));
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.ItemsSource is not ObservableCollection<HistoryEntry> history ||
            history.Count == 0) return;

        if (MessageBox.Show("Clear all browsing history?", "Butane history",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var previousHistory = history.ToList();
        history.Clear();
        SaveChanges(() =>
        {
            foreach (var entry in previousHistory) history.Add(entry);
        });
    }

    private void OpenDownload_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadsList.SelectedItem is not DownloadEntry download) return;
        if (download.State != "COMPLETED")
        {
            MessageBox.Show("This download is not complete yet.", "Butane downloads",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!File.Exists(download.FilePath))
        {
            MessageBox.Show("The downloaded file could not be found.", "Butane downloads",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StartProcess(download.FilePath, "");
    }

    private void ShowDownload_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadsList.SelectedItem is not DownloadEntry download) return;
        string? directory = Path.GetDirectoryName(download.FilePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            MessageBox.Show("The download folder could not be found.", "Butane downloads",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StartProcess("explorer.exe", $"/select,\"{download.FilePath}\"");
    }

    private void RemoveDownload_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadsList.SelectedItem is not DownloadEntry download) return;
        if (DownloadsList.ItemsSource is ObservableCollection<DownloadEntry> downloads)
        {
            int index = downloads.IndexOf(download);
            downloads.Remove(download);
            SaveChanges(() => downloads.Insert(index, download));
        }
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadsList.SelectedItem is DownloadEntry download)
            _cancelDownload(download);
    }

    private void SaveChanges(Action restore)
    {
        try
        {
            _save();
        }
        catch (Exception ex)
        {
            restore();
            MessageBox.Show(
                "Could not save the browser library changes.\n\n" + ex.Message,
                "Butane library", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void StartProcess(string fileName, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                "Could not open the downloaded file or its folder.\n\n" + ex.Message,
                "Butane downloads", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
