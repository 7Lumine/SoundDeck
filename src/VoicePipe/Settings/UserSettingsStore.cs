using System.IO;
using System.Text.Json;

namespace VoicePipe.Settings;

public static class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SoundDeck");

    private static string LegacyAppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoicePipe");

    public static string SettingsPath { get; } = Path.Combine(
        AppDataDirectory,
        "settings.json");

    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                TryMigrateLegacySettings();
            }

            if (!File.Exists(SettingsPath))
            {
                return new UserSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    private static void TryMigrateLegacySettings()
    {
        try
        {
            var legacyPath = Path.Combine(LegacyAppDataDirectory, "settings.json");
            if (!File.Exists(legacyPath))
            {
                return;
            }

            Directory.CreateDirectory(AppDataDirectory);
            File.Copy(legacyPath, SettingsPath, overwrite: false);
        }
        catch
        {
            // Legacy settings migration is best-effort.
        }
    }

    public static void Save(UserSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Settings persistence should never interrupt audio routing.
        }
    }
}
