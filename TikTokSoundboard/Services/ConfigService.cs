using System.IO;
using System.Text.Json;
using TikTokSoundboard.Models;

namespace TikTokSoundboard.Services;

public static class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TikTokSoundboard");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "soundboard_config.json");

    // Legacy path (from Python version, same directory as exe)
    private static readonly string LegacyConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "soundboard_config.json");

    public static SoundboardConfig Load()
    {
        try
        {
            string? json = null;

            if (File.Exists(ConfigPath))
                json = File.ReadAllText(ConfigPath);
            else if (File.Exists(LegacyConfigPath))
                json = File.ReadAllText(LegacyConfigPath);

            if (json != null)
            {
                var config = JsonSerializer.Deserialize<SoundboardConfig>(json);
                if (config != null) return config;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ConfigService.Load error: {ex.Message}");
        }

        return new SoundboardConfig();
    }

    public static void Save(SoundboardConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ConfigService.Save error: {ex.Message}");
        }
    }
}
