using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TikTokSoundboard.Services;

/// <summary>
/// Manages audio playback using NAudio. Supports multiple simultaneous sounds.
/// </summary>
public class AudioService : IDisposable
{
    private readonly WaveOutEvent _outputDevice;
    private readonly MixingSampleProvider _mixer;
    private readonly Dictionary<string, ActiveSound> _activeSounds = new();
    private float _masterVolume = 0.8f;

    private class ActiveSound
    {
        public required WaveStream Reader { get; init; }
        public required VolumeSampleProvider VolumeProvider { get; init; }
        public required ISampleProvider SampleProvider { get; init; }
        public required System.Timers.Timer PlaybackTimer { get; init; }
    }

    public AudioService()
    {
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2))
        {
            ReadFully = true
        };

        _outputDevice = new WaveOutEvent
        {
            DesiredLatency = 100,
            NumberOfBuffers = 3
        };
        _outputDevice.Init(_mixer);
        _outputDevice.Play();
    }

    public float MasterVolume
    {
        get => _masterVolume;
        set => _masterVolume = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// Play a sound file. Returns true on success.
    /// </summary>
    public bool PlaySound(string key, string filePath, int volume, Action? onCompleted = null)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            // Stop existing sound for this key if still playing
            StopSound(key);

            // Use MediaFoundationReader instead of AudioFileReader to avoid ACM errors
            var reader = new MediaFoundationReader(filePath);

            // Convert to IEEE Float SampleProvider
            ISampleProvider sampleProvider = reader.ToSampleProvider();

            // Convert to stereo 44100 if needed
            if (sampleProvider.WaveFormat.SampleRate != 44100)
            {
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 44100);
            }
            if (sampleProvider.WaveFormat.Channels == 1)
            {
                sampleProvider = new MonoToStereoSampleProvider(sampleProvider);
            }

            var volumeProvider = new VolumeSampleProvider(sampleProvider)
            {
                Volume = (volume / 100f) * _masterVolume
            };

            // Use a timer to poll for completion
            var timer = new System.Timers.Timer(100);

            // Wrap to detect end of playback
            var notifying = new NotifyingSampleProvider(volumeProvider);
            var activeSound = new ActiveSound
            {
                Reader = reader,
                VolumeProvider = volumeProvider,
                SampleProvider = notifying,
                PlaybackTimer = timer
            };

            _activeSounds[key] = activeSound;

            timer.Elapsed += (s, e) =>
            {
                try
                {
                    if (reader.Position >= reader.Length)
                    {
                        timer.Stop();
                        timer.Dispose();
                        StopSound(key);
                        onCompleted?.Invoke();
                    }
                }
                catch
                {
                    // Reader might have been disposed by StopSound
                    timer.Stop();
                    timer.Dispose();
                }
            };
            timer.Start();

            _mixer.AddMixerInput(notifying);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AudioService.PlaySound error: {ex.Message}");
            return false;
        }
    }

    public void StopSound(string key)
    {
        if (_activeSounds.TryGetValue(key, out var active))
        {
            try
            {
                active.PlaybackTimer.Stop();
                active.PlaybackTimer.Dispose();
                _mixer.RemoveMixerInput(active.SampleProvider);
                active.Reader.Dispose();
            }
            catch { }
            _activeSounds.Remove(key);
        }
    }

    public void StopAll()
    {
        foreach (var key in _activeSounds.Keys.ToList())
        {
            StopSound(key);
        }
    }

    public bool IsPlaying(string key)
    {
        return _activeSounds.ContainsKey(key);
    }

    public void Dispose()
    {
        StopAll();
        _outputDevice.Stop();
        _outputDevice.Dispose();
    }
}
