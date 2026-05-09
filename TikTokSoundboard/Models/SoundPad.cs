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
}

public class SoundboardConfig
{
    [JsonPropertyName("master_volume")]
    public int MasterVolume { get; set; } = 80;

    [JsonPropertyName("pads")]
    public List<SoundPad> Pads { get; set; } = new();
}
