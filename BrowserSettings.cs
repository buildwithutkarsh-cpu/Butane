using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Butane;

internal sealed class BrowserSettings
{
    public bool BlockTrackers { get; set; } = true;
    public bool ClearCookiesOnExit { get; set; }
    public string TrackingPreventionLevel { get; set; } = "Balanced";

    public CoreWebView2TrackingPreventionLevel GetTrackingPreventionLevel() =>
        TrackingPreventionLevel switch
        {
            "Off" => CoreWebView2TrackingPreventionLevel.None,
            "Basic" => CoreWebView2TrackingPreventionLevel.Basic,
            "Strict" => CoreWebView2TrackingPreventionLevel.Strict,
            _ => CoreWebView2TrackingPreventionLevel.Balanced
        };
}

internal static class BrowserSettingsStore
{
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Butane", "settings.json");

    public static BrowserSettings Load()
    {
        if (!File.Exists(SettingsFile)) return new BrowserSettings();

        try
        {
            var settings = JsonSerializer.Deserialize<BrowserSettings>(File.ReadAllText(SettingsFile));
            if (settings == null ||
                settings.TrackingPreventionLevel is not ("Off" or "Basic" or "Balanced" or "Strict"))
                throw new InvalidDataException("The saved settings contain an unsupported protection level.");

            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            MessageBox.Show(
                "Could not load saved browser settings. Default settings will be used.\n\n" + ex.Message,
                "Butane settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return new BrowserSettings();
        }
    }

    public static void Save(BrowserSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
