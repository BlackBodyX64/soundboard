using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NAudio.Wave;

namespace TikTokSoundboard.Controls;

/// <summary>
/// Renders an audio waveform with draggable start/end trim handles.
/// </summary>
public class WaveformViewer : Canvas
{
    // ── Dependency Properties ────────────────────────────────────────────────

    public static readonly DependencyProperty StartRatioProperty =
        DependencyProperty.Register(nameof(StartRatio), typeof(double), typeof(WaveformViewer),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnRatioChanged));

    public static readonly DependencyProperty EndRatioProperty =
        DependencyProperty.Register(nameof(EndRatio), typeof(double), typeof(WaveformViewer),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, OnRatioChanged));

    /// <summary>Start handle position as fraction of total duration [0..1].</summary>
    public double StartRatio
    {
        get => (double)GetValue(StartRatioProperty);
        set => SetValue(StartRatioProperty, Math.Clamp(value, 0.0, EndRatio - 0.01));
    }

    /// <summary>End handle position as fraction of total duration [0..1].</summary>
    public double EndRatio
    {
        get => (double)GetValue(EndRatioProperty);
        set => SetValue(EndRatioProperty, Math.Clamp(value, StartRatio + 0.01, 1.0));
    }

    public static readonly DependencyProperty PlaybackRatioProperty =
        DependencyProperty.Register(nameof(PlaybackRatio), typeof(double), typeof(WaveformViewer),
            new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender, OnRatioChanged));

    /// <summary>Current playback position as fraction of total duration [0..1]. -1 means not playing.</summary>
    public double PlaybackRatio
    {
        get => (double)GetValue(PlaybackRatioProperty);
        set => SetValue(PlaybackRatioProperty, value);
    }

    // ── Events ───────────────────────────────────────────────────────────────
    public event Action<double, double>? TrimChanged;

    // ── Waveform data ────────────────────────────────────────────────────────
    private float[] _peaks = Array.Empty<float>();
    private double _fileDuration;   // seconds

    // ── Drag state ───────────────────────────────────────────────────────────
    private enum DragTarget { None, Start, End, Region }
    private DragTarget _dragging = DragTarget.None;
    private double _dragStartX;
    private double _dragStartRatio;
    private double _dragEndRatio;

    // ── Colors / style ───────────────────────────────────────────────────────
    private static readonly SolidColorBrush _waveFill      = new(Color.FromRgb(0x2d, 0x6a, 0x9f));
    private static readonly SolidColorBrush _waveActive    = new(Color.FromRgb(0x58, 0xa6, 0xff));
    private static readonly SolidColorBrush _waveInactive  = new(Color.FromArgb(0x44, 0x8b, 0x94, 0x9e));
    private static readonly SolidColorBrush _regionBg      = new(Color.FromArgb(0x33, 0x58, 0xa6, 0xff));
    private static readonly SolidColorBrush _handleColor   = new(Color.FromRgb(0xff, 0xd7, 0x00));
    private static readonly SolidColorBrush _handleShadow  = new(Color.FromArgb(0x88, 0x00, 0x00, 0x00));

    private const double HandleWidth = 10;
    private const double HandleRadius = 5;

    public WaveformViewer()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x10, 0x18));
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseLeave += (_, _) => { if (_dragging != DragTarget.None) StopDrag(); };
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Load and decode audio from <paramref name="filePath"/> asynchronously,
    /// then build peaks for rendering.
    /// </summary>
    public async Task LoadFileAsync(string filePath)
    {
        _peaks = Array.Empty<float>();
        _fileDuration = 0;
        InvalidateVisual();

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        try
        {
            var (peaks, duration) = await Task.Run(() => BuildPeaks(filePath));
            _peaks = peaks;
            _fileDuration = duration;
            InvalidateVisual();
        }
        catch
        {
            _peaks = Array.Empty<float>();
            InvalidateVisual();
        }
    }

    public double FileDuration => _fileDuration;

    // ── Peaks builder ─────────────────────────────────────────────────────────

    private static (float[] peaks, double duration) BuildPeaks(string filePath)
    {
        const int targetBins = 800;

        using var reader = new MediaFoundationReader(filePath);
        double duration = reader.TotalTime.TotalSeconds;

        var provider = reader.ToSampleProvider();
        if (provider.WaveFormat.Channels > 1)
            provider = new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(provider);

        var all = new List<float>(1_000_000);
        var buf = new float[4096];
        int read;
        while ((read = provider.Read(buf, 0, buf.Length)) > 0)
            for (int i = 0; i < read; i++)
                all.Add(Math.Abs(buf[i]));

        if (all.Count == 0)
            return (Array.Empty<float>(), duration);

        int bins = Math.Min(targetBins, all.Count);
        float[] peaks = new float[bins];
        int samplesPerBin = all.Count / bins;

        for (int b = 0; b < bins; b++)
        {
            float max = 0;
            int start = b * samplesPerBin;
            int end = Math.Min(start + samplesPerBin, all.Count);
            for (int i = start; i < end; i++)
                if (all[i] > max) max = all[i];
            peaks[b] = max;
        }

        return (peaks, duration);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double mid = h / 2.0;
        double startX = StartRatio * w;
        double endX   = EndRatio   * w;

        // Background
        dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));

        // Inactive overlay (left of start)
        dc.DrawRectangle(_waveInactive, null, new Rect(0, 0, startX, h));
        // Active region highlight
        dc.DrawRectangle(_regionBg,     null, new Rect(startX, 0, endX - startX, h));
        // Inactive overlay (right of end)
        dc.DrawRectangle(_waveInactive, null, new Rect(endX, 0, w - endX, h));

        // Waveform bars
        if (_peaks.Length > 0)
        {
            double binW = w / _peaks.Length;
            for (int i = 0; i < _peaks.Length; i++)
            {
                double x = i * binW;
                double barH = _peaks[i] * mid * 0.92;
                bool active = x >= startX && x <= endX;
                var brush = active ? _waveActive : _waveFill;

                // top half
                dc.DrawRectangle(brush, null, new Rect(x, mid - barH, Math.Max(binW - 0.5, 0.5), barH));
                // bottom half (mirrored)
                dc.DrawRectangle(brush, null, new Rect(x, mid, Math.Max(binW - 0.5, 0.5), barH));
            }
        }
        else
        {
            // No file loaded — show placeholder text
            var ft = new FormattedText("ยังไม่ได้เลือกไฟล์เสียง",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                13, new SolidColorBrush(Color.FromRgb(0x48, 0x4f, 0x58)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(w / 2 - ft.Width / 2, mid - ft.Height / 2));
        }

        // Center line
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x44, 0xff, 0xff, 0xff)), 0.5),
                    new Point(0, mid), new Point(w, mid));

        // Playback line
        double playRatio = PlaybackRatio;
        if (playRatio >= 0 && playRatio <= 1.0)
        {
            double playX = playRatio * w;
            var playPen = new Pen(new SolidColorBrush(Color.FromRgb(0xff, 0x4d, 0x4d)), 2);
            dc.DrawLine(playPen, new Point(playX, 0), new Point(playX, h));
        }

        // Handles
        DrawHandle(dc, startX, h, true);
        DrawHandle(dc, endX,   h, false);
    }

    private static void DrawHandle(DrawingContext dc, double x, double h, bool isStart)
    {
        // Shadow
        dc.DrawRectangle(_handleShadow, null, new Rect(x + (isStart ? -HandleWidth - 1 : 1), 0, HandleWidth + 2, h));

        // Handle body
        double hx = isStart ? x - HandleWidth : x;
        dc.DrawRectangle(_handleColor, null, new Rect(hx, 0, HandleWidth, h));

        // Grip lines
        var gripPen = new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00)), 1);
        for (int i = 0; i < 3; i++)
        {
            double gy = h * (0.3 + i * 0.2);
            dc.DrawLine(gripPen, new Point(hx + 2, gy), new Point(hx + HandleWidth - 2, gy));
        }

        // Arrow triangle
        double cx = hx + HandleWidth / 2;
        double ty = h / 2 - 6;
        var arrowBrush = new SolidColorBrush(Color.FromArgb(0xcc, 0x00, 0x00, 0x00));
        StreamGeometry tri = new();
        using (var sg = tri.Open())
        {
            if (isStart)
            {
                sg.BeginFigure(new Point(cx - 3, ty + 6), true, true);
                sg.LineTo(new Point(cx + 3, ty), false, false);
                sg.LineTo(new Point(cx + 3, ty + 12), false, false);
            }
            else
            {
                sg.BeginFigure(new Point(cx + 3, ty + 6), true, true);
                sg.LineTo(new Point(cx - 3, ty), false, false);
                sg.LineTo(new Point(cx - 3, ty + 12), false, false);
            }
        }
        dc.DrawGeometry(arrowBrush, null, tri);
    }

    // ── Drag interactions ─────────────────────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        double x = e.GetPosition(this).X;
        double w = ActualWidth;
        if (w <= 0) return;

        double startX = StartRatio * w;
        double endX   = EndRatio   * w;
        double ratio  = x / w;

        _dragStartX     = x;
        _dragStartRatio = StartRatio;
        _dragEndRatio   = EndRatio;

        if (Math.Abs(x - startX) <= HandleWidth + 4)
            _dragging = DragTarget.Start;
        else if (Math.Abs(x - endX) <= HandleWidth + 4)
            _dragging = DragTarget.End;
        else if (ratio > StartRatio && ratio < EndRatio)
            _dragging = DragTarget.Region;
        else
            _dragging = DragTarget.None;

        if (_dragging != DragTarget.None)
            CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging == DragTarget.None) return;

        double x = e.GetPosition(this).X;
        double w = ActualWidth;
        if (w <= 0) return;

        double ratio = x / w;
        double delta = (x - _dragStartX) / w;

        switch (_dragging)
        {
            case DragTarget.Start:
                StartRatio = Math.Clamp(ratio, 0.0, EndRatio - 0.01);
                break;
            case DragTarget.End:
                EndRatio = Math.Clamp(ratio, StartRatio + 0.01, 1.0);
                break;
            case DragTarget.Region:
                double span = _dragEndRatio - _dragStartRatio;
                double newStart = Math.Clamp(_dragStartRatio + delta, 0.0, 1.0 - span);
                SetValue(StartRatioProperty, newStart);
                SetValue(EndRatioProperty,   newStart + span);
                break;
        }

        TrimChanged?.Invoke(StartRatio, EndRatio);
        InvalidateVisual();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e) => StopDrag();

    private void StopDrag()
    {
        _dragging = DragTarget.None;
        ReleaseMouseCapture();
        TrimChanged?.Invoke(StartRatio, EndRatio);
        InvalidateVisual();
    }

    // ── Layout ────────────────────────────────────────────────────────────────
    protected override Size MeasureOverride(Size constraint) => constraint;
    protected override Size ArrangeOverride(Size arrangeBounds) => arrangeBounds;

    private static void OnRatioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WaveformViewer)d).InvalidateVisual();
}
