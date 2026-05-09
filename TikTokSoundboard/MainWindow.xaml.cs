using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TikTokSoundboard.Controls;
using TikTokSoundboard.Models;
using TikTokSoundboard.Services;

namespace TikTokSoundboard;

public partial class MainWindow : Window
{
    // ==================== Layout Definitions ====================
    private static readonly string[] FKeyRow = { "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12" };

    private static readonly string[][] KbRows = {
        new[] { "1","2","3","4","5","6","7","8","9","0" },
        new[] { "Q","W","E","R","T","Y","U","I","O","P" },
        new[] { "A","S","D","F","G","H","J","K","L" },
        new[] { "Z","X","C","V","B","N","M" },
    };
    private static readonly int[] KbOffsets = { 0, 20, 35, 55 };

    private static readonly string[][] NumpadRows = {
        new[] { "Num7","Num8","Num9" },
        new[] { "Num4","Num5","Num6" },
        new[] { "Num1","Num2","Num3" },
        new[] { "Num0","Num.","Num+" },
    };

    // Color schemes: (base, accent)
    private static readonly Dictionary<string, (Color Base, Color Accent)> SectionColors = new()
    {
        ["fkey"]   = (Color.FromRgb(0xb8,0x86,0x0b), Color.FromRgb(0xff,0xd7,0x00)),
        ["row0"]   = (Color.FromRgb(0xe9,0x45,0x60), Color.FromRgb(0xff,0x6b,0x81)),
        ["row1"]   = (Color.FromRgb(0x0f,0x34,0x60), Color.FromRgb(0x1a,0x6d,0xaa)),
        ["row2"]   = (Color.FromRgb(0x53,0x34,0x83), Color.FromRgb(0x7c,0x4d,0xbd)),
        ["row3"]   = (Color.FromRgb(0xe7,0x6f,0x51), Color.FromRgb(0xf4,0xa2,0x61)),
        ["numpad"] = (Color.FromRgb(0x2d,0x6a,0x4f), Color.FromRgb(0x52,0xb7,0x88)),
    };

    // ==================== State ====================
    private readonly Dictionary<string, SoundPad> _pads = new();
    private readonly Dictionary<string, KeyButton> _keyButtons = new();
    private readonly AudioService _audio;
    private readonly GlobalHotkeyService _hotkeys;
    private SoundboardConfig _config = new();

    public MainWindow()
    {
        InitializeComponent();

        _audio = new AudioService();
        _hotkeys = new GlobalHotkeyService();
        _hotkeys.KeyTriggered += OnHotkeyTriggered;

        InitPads();
        LoadConfig();
        BuildKeyboard();

        _hotkeys.Install();
        UpdateRegisteredHotkeys();

        Closing += (_, _) =>
        {
            SaveConfig();
            _hotkeys.Dispose();
            _audio.Dispose();
        };
    }

    // ==================== Init ====================
    private void InitPads()
    {
        foreach (var k in FKeyRow) _pads[k] = new SoundPad { Key = k };
        foreach (var row in KbRows)
            foreach (var k in row) _pads[k] = new SoundPad { Key = k };
        foreach (var row in NumpadRows)
            foreach (var k in row) _pads[k] = new SoundPad { Key = k };
    }

    private void LoadConfig()
    {
        _config = ConfigService.Load();
        _audio.MasterVolume = _config.MasterVolume / 100f;
        MasterVolumeSlider.Value = _config.MasterVolume;
        MasterVolumeLabel.Text = $"{_config.MasterVolume}%";

        foreach (var pd in _config.Pads)
        {
            if (!string.IsNullOrEmpty(pd.Key) && _pads.ContainsKey(pd.Key))
            {
                _pads[pd.Key] = pd;
            }
        }
    }

    private void SaveConfig()
    {
        _config.MasterVolume = (int)MasterVolumeSlider.Value;
        _config.Pads = _pads.Values.ToList();
        ConfigService.Save(_config);
    }

    // ==================== Build Keyboard UI ====================
    private void BuildKeyboard()
    {
        // F-Keys
        KeyboardPanel.Children.Add(CreateSectionLabel("FUNCTION KEYS"));
        var fRow = CreateKeyRow(0);
        foreach (var k in FKeyRow)
            fRow.Children.Add(CreateKeyButton(k, SectionColors["fkey"], k, 70, 52));
        KeyboardPanel.Children.Add(fRow);

        // Divider
        var divider = new Border
        {
            Height = 1,
            Margin = new Thickness(10, 4, 10, 4),
            Background = new LinearGradientBrush(
                Color.FromArgb(0, 0x21, 0x26, 0x2d),
                Color.FromRgb(0x21, 0x26, 0x2d), 0)
        };
        KeyboardPanel.Children.Add(divider);

        // Main keyboard
        KeyboardPanel.Children.Add(CreateSectionLabel("MAIN KEYBOARD"));
        for (int i = 0; i < KbRows.Length; i++)
        {
            var row = CreateKeyRow(KbOffsets[i]);
            var colors = SectionColors[$"row{i}"];
            foreach (var k in KbRows[i])
                row.Children.Add(CreateKeyButton(k, colors, k));
            KeyboardPanel.Children.Add(row);
        }

        // Numpad
        foreach (var rowKeys in NumpadRows)
        {
            var row = CreateKeyRow(0);
            row.HorizontalAlignment = HorizontalAlignment.Center;
            foreach (var k in rowKeys)
            {
                var display = k.Replace("Num", "");
                row.Children.Add(CreateKeyButton(k, SectionColors["numpad"], display));
            }
            NumpadPanel.Children.Add(row);
        }

        // Refresh all displays
        foreach (var (key, btn) in _keyButtons)
        {
            var pad = _pads[key];
            btn.UpdateDisplay(pad.SoundName, !string.IsNullOrEmpty(pad.SoundPath));
        }
    }

    private static TextBlock CreateSectionLabel(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 11,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Color.FromRgb(0x48, 0x4f, 0x58)),
        Margin = new Thickness(10, 6, 0, 3)
    };

    private static StackPanel CreateKeyRow(int leftOffset) => new()
    {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(leftOffset + 5, 2, 0, 2)
    };

    private KeyButton CreateKeyButton(string key, (Color Base, Color Accent) colors,
        string displayText, double w = 72, double h = 68)
    {
        var btn = new KeyButton
        {
            KeyId = key,
            DisplayText = displayText,
            ButtonWidth = w,
            ButtonHeight = h,
            BaseColor = colors.Base,
            AccentColor = colors.Accent,
        };

        btn.PlayRequested += OnPlayRequested;
        btn.SettingsRequested += OnSettingsRequested;
        btn.FileDropped += OnFileDropped;

        _keyButtons[key] = btn;
        return btn;
    }

    // ==================== Events ====================
    private void OnPlayRequested(string key)
    {
        PlaySound(key);
    }

    private void OnSettingsRequested(string key)
    {
        var pad = _pads[key];
        var colors = GetColorsForKey(key);
        var dlg = new SettingsDialog(pad, "#e94560") { Owner = this };

        if (dlg.ShowDialog() == true)
        {
            _pads[key] = dlg.Pad;
            _keyButtons[key].UpdateDisplay(dlg.Pad.SoundName, !string.IsNullOrEmpty(dlg.Pad.SoundPath));
            SaveConfig();
            UpdateRegisteredHotkeys();
        }
    }

    private void OnFileDropped(string key, string filePath)
    {
        var pad = _pads[key];
        pad.SoundPath = filePath;
        pad.SoundName = Path.GetFileNameWithoutExtension(filePath);
        _keyButtons[key].UpdateDisplay(pad.SoundName, true);
        SaveConfig();
        UpdateRegisteredHotkeys();
    }

    private void OnHotkeyTriggered(string key)
    {
        Dispatcher.Invoke(() => PlaySound(key));
    }

    // ==================== Audio ====================
    private void PlaySound(string key)
    {
        if (!_pads.TryGetValue(key, out var pad) || string.IsNullOrEmpty(pad.SoundPath))
            return;

        if (!File.Exists(pad.SoundPath))
            return;

        _keyButtons[key].SetPlaying(true);

        var success = _audio.PlaySound(key, pad.SoundPath, pad.Volume, () =>
        {
            Dispatcher.Invoke(() => _keyButtons[key].SetPlaying(false));
        });

        if (!success)
            _keyButtons[key].SetPlaying(false);
    }

    // ==================== Hotkeys ====================
    private void UpdateRegisteredHotkeys()
    {
        var keys = _pads.Values
            .Where(p => !string.IsNullOrEmpty(p.SoundPath))
            .Select(p => p.Key);
        _hotkeys.SetRegisteredKeys(keys);
    }

    // ==================== Window Chrome ====================
    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.ClickCount == 2)
                ToggleMaximize();
            else
                DragMove();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    // ==================== Controls ====================
    private void OnStopAllClick(object sender, RoutedEventArgs e)
    {
        _audio.StopAll();
        foreach (var btn in _keyButtons.Values)
            btn.SetPlaying(false);
    }

    private void OnMasterVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MasterVolumeLabel == null || _audio == null) return;
        var vol = (int)e.NewValue;
        MasterVolumeLabel.Text = $"{vol}%";
        _audio.MasterVolume = vol / 100f;
    }

    private void OnHotkeyToggle(object sender, RoutedEventArgs e)
    {
        if (_hotkeys != null && HotkeyToggle != null)
        {
            _hotkeys.IsActive = HotkeyToggle.IsChecked == true;
        }
    }

    // ==================== Helpers ====================
    private (Color Base, Color Accent) GetColorsForKey(string key)
    {
        if (FKeyRow.Contains(key)) return SectionColors["fkey"];
        for (int i = 0; i < KbRows.Length; i++)
            if (KbRows[i].Contains(key)) return SectionColors[$"row{i}"];
        return SectionColors["numpad"];
    }
}