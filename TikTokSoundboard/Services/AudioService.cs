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
    /// startTime / endTime are in seconds; use -1 for start/end of file.
    /// speed is a percentage: 100 = normal, 200 = 2x, 50 = half.
    /// </summary>
    public bool PlaySound(string key, string filePath, int volume,
        double startTime = -1, double endTime = -1, int speed = 100,
        Action? onCompleted = null)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            // Stop existing sound for this key if still playing
            StopSound(key);

            // Use MediaFoundationReader instead of AudioFileReader to avoid ACM errors
            var reader = new MediaFoundationReader(filePath);

            // Seek to start time
            if (startTime > 0)
            {
                var seekPos = TimeSpan.FromSeconds(startTime);
                if (seekPos < reader.TotalTime)
                    reader.CurrentTime = seekPos;
            }

            // Determine end sample position for early-stop
            long endBytes = reader.Length; // default: play to end
            if (endTime > 0)
            {
                var endTs = TimeSpan.FromSeconds(endTime);
                if (endTs > reader.CurrentTime && endTs <= reader.TotalTime)
                {
                    double ratio = endTs.TotalSeconds / reader.TotalTime.TotalSeconds;
                    endBytes = (long)(reader.Length * ratio);
                }
            }

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

            if (speed != 100)
            {
                sampleProvider = new SpeedSampleProvider(sampleProvider, speed / 100f);
            }

            var volumeProvider = new VolumeSampleProvider(sampleProvider)
            {
                Volume = (volume / 100f) * _masterVolume
            };

            // Use a timer to poll for completion / end-time
            var timer = new System.Timers.Timer(50);

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
                    bool finished = reader.Position >= reader.Length || reader.Position >= endBytes;
                    if (finished)
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
