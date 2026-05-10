using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TikTokSoundboard.Models;
using TikTokSoundboard.Services;

namespace TikTokSoundboard;

public partial class SettingsDialog : Window
{
    public SoundPad Pad { get; private set; }
    public bool Saved   { get; private set; }
    public bool Cleared { get; private set; }

    private double _fileDuration;

    // Preview playback
    private WaveOutEvent?          _previewOut;
    private MediaFoundationReader? _previewReader;

    public SettingsDialog(SoundPad pad, string accentColorHex)
    {
        InitializeComponent();

        Pad = new SoundPad
        {
            Key       = pad.Key,
            SoundPath = pad.SoundPath,
            SoundName = pad.SoundName,
            Volume    = pad.Volume,
            StartTime = pad.StartTime,
            EndTime   = pad.EndTime
        };

        TitleText.Text      = $"⚙ ตั้งค่าปุ่ม [ {pad.Key} ]";
        FilePathBox.Text    = pad.SoundPath;
        DisplayNameBox.Text = pad.SoundName;
        VolumeSlider.Value  = pad.Volume;
        VolumeLabel.Text    = $"{pad.Volume}%";

        Waveform.TrimChanged += OnTrimChanged;

        Loaded += async (_, _) =>
            await LoadWaveformAsync(pad.SoundPath, pad.StartTime, pad.EndTime);

        Closed += (_, _) => StopPreview();
    }

    // ── Waveform loader ───────────────────────────────────────────────────────

    private async Task LoadWaveformAsync(string filePath,
        double startTime = -1, double endTime = -1)
    {
        DurationLabel.Text = "กำลังโหลดไฟล์...";
        ResetTrimBtn.Visibility = Visibility.Collapsed;

        await Waveform.LoadFileAsync(filePath);
        _fileDuration = Waveform.FileDuration;

        if (_fileDuration <= 0)
        {
            DurationLabel.Text  = "— ไม่มีไฟล์ —";
            StartTimeLabel.Text = "0.00 s";
            EndTimeLabel.Text   = "0.00 s";
            PlayStatusLabel.Text = "";
            return;
        }

        // Always show handles at start — restore saved trim or use full range
        double sr = startTime >= 0 && startTime < _fileDuration
            ? startTime / _fileDuration : 0.0;
        double er = endTime   >  0 && endTime   <= _fileDuration
            ? endTime   / _fileDuration : 1.0;

        Waveform.StartRatio = sr;
        Waveform.EndRatio   = er;

        UpdateTimeLabels(sr, er);
        DurationLabel.Text = $"⏱ {_fileDuration:0.00} s";
        ResetTrimBtn.Visibility = (sr > 0.001 || er < 0.999)
            ? Visibility.Visible : Visibility.Collapsed;

        PlayStatusLabel.Text = "ลากจุด ◀ ▶ เพื่อกำหนดช่วงเสียง";
    }

    // ── Trim callbacks ────────────────────────────────────────────────────────

    private void OnTrimChanged(double sr, double er)
    {
        UpdateTimeLabels(sr, er);
        ResetTrimBtn.Visibility = (sr > 0.001 || er < 0.999)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTimeLabels(double sr, double er)
    {
        StartTimeLabel.Text = $"{sr * _fileDuration:0.00} s";
        EndTimeLabel.Text   = $"{er * _fileDuration:0.00} s";
    }

    private void OnResetTrimClick(object sender, RoutedEventArgs e)
    {
        Waveform.StartRatio = 0.0;
        Waveform.EndRatio   = 1.0;
        UpdateTimeLabels(0.0, 1.0);
        ResetTrimBtn.Visibility = Visibility.Collapsed;
        PlayStatusLabel.Text = "";
    }

    // ── Preview playback ──────────────────────────────────────────────────────

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        var path = FilePathBox.Text.Trim();
        if (!File.Exists(path))
        {
            PlayStatusLabel.Text = "⚠️ ไม่พบไฟล์";
            return;
        }

        StopPreview();

        double startSec = Waveform.StartRatio * _fileDuration;
        double endSec   = Waveform.EndRatio   * _fileDuration;

        try
        {
            _previewReader = new MediaFoundationReader(path);

            if (startSec > 0)
                _previewReader.CurrentTime = TimeSpan.FromSeconds(startSec);

            long endBytes = _previewReader.Length;
            if (endSec > 0 && endSec < _fileDuration)
            {
                double ratio = endSec / _fileDuration;
                endBytes = (long)(_previewReader.Length * ratio);
            }

            ISampleProvider sp = _previewReader.ToSampleProvider();
            if (sp.WaveFormat.SampleRate != 44100)
                sp = new WdlResamplingSampleProvider(sp, 44100);
            if (sp.WaveFormat.Channels == 1)
                sp = new MonoToStereoSampleProvider(sp);

            var vol = new VolumeSampleProvider(sp)
            {
                Volume = (float)VolumeSlider.Value / 100f
            };
            var notify = new NotifyingSampleProvider(vol);

            _previewOut = new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 2 };
            _previewOut.Init(notify);
            _previewOut.Play();

            PlayStatusLabel.Text = $"▶ กำลังเล่น  {startSec:0.00}s → {endSec:0.00}s";
            PlayBtn.IsEnabled    = false;

            // Poll for end position
            var timer = new System.Timers.Timer(40); // 40ms for smoother animation
            timer.Elapsed += (_, _) =>
            {
                try
                {
                    var reader = _previewReader;
                    if (reader == null)
                    {
                        timer.Stop(); timer.Dispose();
                        return;
                    }
                    
                    double currentRatio = reader.Position / (double)reader.Length;
                    Dispatcher.InvokeAsync(() =>
                    {
                        Waveform.PlaybackRatio = currentRatio;
                    });

                    if (reader.Position >= endBytes || reader.Position >= reader.Length)
                    {
                        timer.Stop(); timer.Dispose();
                        Dispatcher.InvokeAsync(StopPreview);
                    }
                }
                catch { timer.Stop(); timer.Dispose(); }
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            PlayStatusLabel.Text = $"❌ {ex.Message}";
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e) => StopPreview();

    private void StopPreview()
    {
        _previewOut?.Stop();
        _previewOut?.Dispose();
        _previewOut = null;

        _previewReader?.Dispose();
        _previewReader = null;

        Waveform.PlaybackRatio = -1;
        PlayBtn.IsEnabled    = true;
        PlayStatusLabel.Text = "";
    }

    // ── File picker ───────────────────────────────────────────────────────────

    private async void OnFilePathChanged(object sender, TextChangedEventArgs e)
    {
        var path = FilePathBox.Text.Trim();
        if (File.Exists(path))
            await LoadWaveformAsync(path);
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "เลือกไฟล์เสียง",
            Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.flac;*.aac;*.wma;*.m4a;*.opus|All Files|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            FilePathBox.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(DisplayNameBox.Text))
                DisplayNameBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            await LoadWaveformAsync(dlg.FileName);
        }
    }

    // ── Other ─────────────────────────────────────────────────────────────────

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        StopPreview();
        DialogResult = false;
        Close();
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeLabel != null)
            VolumeLabel.Text = $"{(int)e.NewValue}%";
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        StopPreview();

        Pad.SoundPath = FilePathBox.Text.Trim();
        Pad.SoundName = DisplayNameBox.Text.Trim();
        if (string.IsNullOrEmpty(Pad.SoundName) && !string.IsNullOrEmpty(Pad.SoundPath))
            Pad.SoundName = Path.GetFileNameWithoutExtension(Pad.SoundPath);
        Pad.Volume = (int)VolumeSlider.Value;

        if (_fileDuration > 0)
        {
            double sr = Waveform.StartRatio;
            double er = Waveform.EndRatio;
            Pad.StartTime = sr > 0.001 ? sr * _fileDuration : -1;
            Pad.EndTime   = er < 0.999 ? er * _fileDuration : -1;
        }
        else
        {
            Pad.StartTime = -1;
            Pad.EndTime   = -1;
        }

        Saved = true;
        DialogResult = true;
        Close();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        StopPreview();
        Pad.SoundPath = "";
        Pad.SoundName = "";
        Pad.Volume    = 100;
        Pad.StartTime = -1;
        Pad.EndTime   = -1;

        Cleared = true;
        DialogResult = true;
        Close();
    }
}
