using System.IO;
using System.Text.Json;
using ConverterPlus.Models;

namespace ConverterPlus.Services;

internal static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ConverterPlus", "settings.json");
    private static readonly string InstallWelcomeMarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ConverterPlus", "onboarding.pending");

    public static bool ConsumeInstallWelcomeMarker(out bool isReinstall)
    {
        isReinstall = false;
        try
        {
            if (!File.Exists(InstallWelcomeMarkerPath))
                return false;

            isReinstall = string.Equals(
                File.ReadAllText(InstallWelcomeMarkerPath).Trim(), "reinstall", StringComparison.OrdinalIgnoreCase);
            File.Delete(InstallWelcomeMarkerPath);
            return true;
        }
        catch
        {
            isReinstall = false;
            return false;
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
        }
        catch
        {
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
