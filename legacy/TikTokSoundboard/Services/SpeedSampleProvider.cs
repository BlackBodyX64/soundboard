using NAudio.Wave;

namespace TikTokSoundboard.Services;

/// <summary>
/// Wraps an ISampleProvider and changes playback speed using linear interpolation.
/// Pitch changes along with speed (chipmunk / slow-mo effect).
/// Speed 1.0 = normal. 2.0 = twice as fast. 0.5 = half speed.
/// </summary>
public class SpeedSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float _speed = 1f;
    private float[] _sourceBuffer = Array.Empty<float>();

    public SpeedSampleProvider(ISampleProvider source, float speed)
    {
        _source = source;
        _speed = speed;
    }

    public float Speed
    {
        get => _speed;
        set => _speed = Math.Clamp(value, 0.25f, 4f);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        if (_speed <= 0.001f)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }

        int channels = WaveFormat.Channels;
        int framesRequested = count / channels;
        if (framesRequested == 0) return 0;

        // Source frames needed for the requested output frames
        int sourceFramesNeeded = (int)Math.Ceiling((framesRequested - 1) * _speed) + 2;
        int sourceSamplesNeeded = sourceFramesNeeded * channels;

        if (_sourceBuffer.Length < sourceSamplesNeeded)
            _sourceBuffer = new float[sourceSamplesNeeded];

        int sourceRead = _source.Read(_sourceBuffer, 0, sourceSamplesNeeded);
        int sourceFramesAvailable = sourceRead / channels;

        if (sourceFramesAvailable <= 1)
            return Math.Max(0, sourceFramesAvailable - 1) * channels;

        int framesToWrite = Math.Min(framesRequested,
            (int)Math.Ceiling((sourceFramesAvailable - 1) / _speed));
        if (framesToWrite < 1) return 0;

        for (int i = 0; i < framesToWrite; i++)
        {
            float sourceIndex = i * _speed;
            int index0 = (int)sourceIndex;
            int index1 = index0 + 1;
            float frac = sourceIndex - index0;

            for (int ch = 0; ch < channels; ch++)
            {
                float s0 = _sourceBuffer[index0 * channels + ch];
                float s1 = _sourceBuffer[index1 * channels + ch];
                buffer[offset + i * channels + ch] = s0 + (s1 - s0) * frac;
            }
        }

        return framesToWrite * channels;
    }
}
