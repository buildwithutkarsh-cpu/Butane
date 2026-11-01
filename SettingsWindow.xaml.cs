using System.Windows;
using System.Windows.Controls;

namespace Butane;

public partial class SettingsWindow : Window
{
    internal BrowserSettings? SavedSettings { get; private set; }

    internal SettingsWindow(BrowserSettings settings)
    {
        InitializeComponent();
        BlockTrackersCheckBox.IsChecked = settings.BlockTrackers;
        ClearCookiesOnExitCheckBox.IsChecked = settings.ClearCookiesOnExit;

        TrackingPreventionComboBox.SelectedItem = TrackingPreventionComboBox.Items
            .OfType<ComboBoxItem>()
            .First(item => string.Equals(item.Content?.ToString(), settings.TrackingPreventionLevel,
                StringComparison.Ordinal));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = new BrowserSettings
        {
            BlockTrackers = BlockTrackersCheckBox.IsChecked == true,
            ClearCookiesOnExit = ClearCookiesOnExitCheckBox.IsChecked == true,
            TrackingPreventionLevel = ((ComboBoxItem)TrackingPreventionComboBox.SelectedItem).Content.ToString()!
        };

        try
        {
            BrowserSettingsStore.Save(settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not save browser settings.\n\n" + ex.Message,
                "Butane settings", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SavedSettings = settings;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
