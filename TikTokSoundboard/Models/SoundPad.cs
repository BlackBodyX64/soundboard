using System.Text.Json.Serialization;

namespace TikTokSoundboard.Models;

public class SoundPad
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("sound_path")]
    public string SoundPath { get; set; } = "";

    [JsonPropertyName("sound_name")]
    public string SoundName { get; set; } = "";

    [JsonPropertyName("volume")]
    public int Volume { get; set; } = 100;

    /// <summary>Playback start time in seconds. -1 = beginning of file.</summary>
    [JsonPropertyName("start_time")]
    public double StartTime { get; set; } = -1;

    /// <summary>Playback end time in seconds. -1 = end of file.</summary>
    [JsonPropertyName("end_time")]
    public double EndTime { get; set; } = -1;

    /// <summary>Playback speed percentage. 100 = normal speed.</summary>
    [JsonPropertyName("speed")]
    public int Speed { get; set; } = 100;
}

public class SoundboardConfig
{
    [JsonPropertyName("master_volume")]
    public int MasterVolume { get; set; } = 80;

    [JsonPropertyName("download_directory")]
    public string DownloadDirectory { get; set; } = "";

    [JsonPropertyName("pads")]
    public List<SoundPad> Pads { get; set; } = new();
}
