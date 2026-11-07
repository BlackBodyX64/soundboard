using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace TikTokSoundboard.Controls;

public partial class KeyButton : UserControl
{
    // ===== Dependency Properties =====
    public static readonly DependencyProperty KeyIdProperty =
        DependencyProperty.Register(nameof(KeyId), typeof(string), typeof(KeyButton), new PropertyMetadata(""));

    public static readonly DependencyProperty DisplayTextProperty =
        DependencyProperty.Register(nameof(DisplayText), typeof(string), typeof(KeyButton), new PropertyMetadata(""));

    public static readonly DependencyProperty ButtonWidthProperty =
        DependencyProperty.Register(nameof(ButtonWidth), typeof(double), typeof(KeyButton), new PropertyMetadata(72.0));

    public static readonly DependencyProperty ButtonHeightProperty =
        DependencyProperty.Register(nameof(ButtonHeight), typeof(double), typeof(KeyButton), new PropertyMetadata(68.0));

    public string KeyId
    {
        get => (string)GetValue(KeyIdProperty);
        set => SetValue(KeyIdProperty, value);
    }

    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }

    public double ButtonWidth
    {
        get => (double)GetValue(ButtonWidthProperty);
        set => SetValue(ButtonWidthProperty, value);
    }

    public double ButtonHeight
    {
        get => (double)GetValue(ButtonHeightProperty);
        set => SetValue(ButtonHeightProperty, value);
    }

    // ===== Colors =====
    public Color BaseColor { get; set; } = Color.FromRgb(0x21, 0x26, 0x2d);
    public Color AccentColor { get; set; } = Color.FromRgb(0x30, 0x36, 0x3d);

    private bool _hasSound;
    private static readonly HashSet<string> AudioExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".ogg", ".flac", ".aac", ".wma" };

    // ===== Events =====
    public event Action<string>? PlayRequested;
    public event Action<string>? SettingsRequested;
    public event Action<string, string>? FileDropped;

    public KeyButton()
    {
        InitializeComponent();
    }

    public void UpdateDisplay(string soundName, bool hasSound)
    {
        _hasSound = hasSound;

        var name = soundName.Length > 7 ? soundName[..6] + "…" : soundName;
        SoundNameLabel.Text = name;

        if (hasSound)
        {
            OuterBorder.Background = new SolidColorBrush(BaseColor);
            OuterBorder.BorderBrush = new SolidColorBrush(AccentColor);
            KeyLabel.Foreground = Brushes.White;
            SoundNameLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xc9, 0xd1, 0xd9));
            Indicator.Background = new SolidColorBrush(AccentColor);
            IndicatorGlow.Color = AccentColor;
            IndicatorGlow.Opacity = 0.5;
        }
        else
        {
            OuterBorder.Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x1d, 0x24));
            OuterBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2d, 0x33, 0x3b));
            KeyLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x6e, 0x76, 0x81));
            SoundNameLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x48, 0x4f, 0x58));
            Indicator.Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x33, 0x3b));
            IndicatorGlow.Color = Colors.Transparent;
        }
    }

    public void SetPlaying(bool playing)
    {
        Dispatcher.Invoke(() =>
        {
            if (playing)
            {
                OuterBorder.BorderBrush = Brushes.White;
                Indicator.Background = Brushes.White;
                IndicatorGlow.Color = Colors.White;
                IndicatorGlow.Opacity = 1.0;

                // Pulse animation on the glow
                var anim = new DoubleAnimation(1.0, 0.4, TimeSpan.FromSeconds(0.4))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new QuadraticEase()
                };
                IndicatorGlow.BeginAnimation(DropShadowEffect.OpacityProperty, anim);
            }
            else
            {
                // Stop animation
                IndicatorGlow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                UpdateDisplay(SoundNameLabel.Text, _hasSound);
            }
        });
    }

    // ===== Event Handlers =====
    private void OnLeftClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        PlayRequested?.Invoke(KeyId);
    }

    private void OnRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SettingsRequested?.Invoke(KeyId);
        e.Handled = true;
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            OuterBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x58, 0xa6, 0xff));
            OuterBorder.Background = new SolidColorBrush(Color.FromArgb(30, 0x58, 0xa6, 0xff));
        }
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        UpdateDisplay(SoundNameLabel.Text, _hasSound);
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (AudioExts.Contains(ext))
                {
                    FileDropped?.Invoke(KeyId, file);
                    break;
                }
            }
        }
        UpdateDisplay(SoundNameLabel.Text, _hasSound);
        e.Handled = true;
    }
}
