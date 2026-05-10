using System.Collections.ObjectModel;
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
    
    // Downloader State
    private readonly YtDlpService _ytDlp = new();
    private CancellationTokenSource? _downloadCts;
    private readonly ObservableCollection<DownloadedItem> _downloadedItems = new();
    private Point? _dragStartPoint;

    public MainWindow()
    {
        InitializeComponent();

        DownloadsList.ItemsSource = _downloadedItems;

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

        var success = _audio.PlaySound(key, pad.SoundPath, pad.Volume,
            pad.StartTime, pad.EndTime, () =>
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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            OnStopAllClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
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

    // ==================== Downloader ====================
    private void OnMainUrlBoxFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(MainUrlBox.Text) && Clipboard.ContainsText())
            {
                var clipText = Clipboard.GetText().Trim();
                if (YtDlpService.IsSupportedUrl(clipText))
                {
                    MainUrlBox.Text = clipText;
                    MainUrlBox.SelectAll();
                }
            }
        }
        catch { }
    }

    private async void OnMainDownloadClick(object sender, RoutedEventArgs e)
    {
        var url = MainUrlBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(url) || !YtDlpService.IsSupportedUrl(url))
        {
            SetMainStatus("⚠️ URL ไม่ถูกต้อง (รองรับ Youtube/Tiktok)", "#f0883e");
            return;
        }

        if (_downloadCts != null)
        {
            // Cancel current download
            _downloadCts.Cancel();
            _downloadCts = null;
            return;
        }

        MainDownloadBtn.Content = "❌";
        MainDownloadBtn.Background = new SolidColorBrush(Color.FromRgb(0xda, 0x36, 0x33));
        MainUrlBox.IsEnabled = false;
        MainDownloadProgress.Visibility = Visibility.Visible;
        MainDownloadStatus.Visibility = Visibility.Visible;

        _downloadCts = new CancellationTokenSource();
        var progress = new Progress<DownloadProgressInfo>(info =>
        {
            Dispatcher.Invoke(() => SetMainStatus(info.Message, "#58a6ff", info.Percentage));
        });

        try
        {
            SetMainStatus("กำลังดึงข้อมูล...", "#58a6ff", 0);
            var title = await _ytDlp.GetTitleAsync(url, _downloadCts.Token);
            if (!string.IsNullOrEmpty(title))
                SetMainStatus($"กำลังดาวน์โหลด: {title}", "#58a6ff", 0);

            var downloadDir = string.IsNullOrWhiteSpace(_config.DownloadDirectory) 
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : _config.DownloadDirectory;

            var filePath = await _ytDlp.DownloadAudioAsync(url, downloadDir, "mp3", progress, _downloadCts.Token);

            if (!string.IsNullOrEmpty(filePath))
            {
                SetMainStatus("✅ ดาวน์โหลดสำเร็จ!", "#3fb950", 100);
                var item = new DownloadedItem 
                { 
                    Name = !string.IsNullOrEmpty(title) ? title : Path.GetFileNameWithoutExtension(filePath), 
                    Path = filePath 
                };
                _downloadedItems.Insert(0, item);
                MainUrlBox.Text = "";
            }
            else
            {
                SetMainStatus("❌ ดาวน์โหลดไม่สำเร็จ", "#f85149", 0);
            }
        }
        catch (OperationCanceledException)
        {
            SetMainStatus("🚫 ยกเลิกการดาวน์โหลดแล้ว", "#8b949e", 0);
        }
        catch (Exception ex)
        {
            SetMainStatus($"❌ ข้อผิดพลาด: {ex.Message}", "#f85149", 0);
        }
        finally
        {
            ResetMainDownloader();
        }
    }

    private void SetMainStatus(string msg, string colorHex, double percentage = -1)
    {
        MainDownloadStatus.Text = msg;
        var color = (Color)ColorConverter.ConvertFromString(colorHex);
        MainDownloadStatus.Foreground = new SolidColorBrush(color);
        MainDownloadStatus.Visibility = Visibility.Visible;
        
        if (percentage >= 0)
        {
            MainDownloadProgress.IsIndeterminate = false;
            MainDownloadProgress.Value = percentage;
        }
        else
        {
            MainDownloadProgress.IsIndeterminate = true;
        }
    }

    private void ResetMainDownloader()
    {
        MainDownloadBtn.Content = "📥";
        MainDownloadBtn.Background = new SolidColorBrush(Color.FromRgb(0x23, 0x86, 0x36));
        MainUrlBox.IsEnabled = true;
        MainDownloadProgress.Visibility = Visibility.Collapsed;
        _downloadCts = null;
    }

    private void OnChooseDownloadFolderClick(object sender, RoutedEventArgs e)
    {
        // Use Win32 OpenFolderDialog if available (.NET 8 WPF)
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "เลือกโฟลเดอร์สำหรับเก็บไฟล์เสียงที่ดาวน์โหลด",
            InitialDirectory = string.IsNullOrWhiteSpace(_config.DownloadDirectory) 
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : _config.DownloadDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            _config.DownloadDirectory = dialog.FolderName;
            ConfigService.Save(_config);
            SetMainStatus($"บันทึกที่: {dialog.FolderName}", "#3fb950");
        }
    }

    // ==================== Drag & Drop ====================
    private void OnDownloadListMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _dragStartPoint = e.GetPosition(null);
        }
    }

    private void OnDownloadListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _dragStartPoint.HasValue)
        {
            Vector diff = _dragStartPoint.Value - e.GetPosition(null);
            
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                // Find the ListBoxItem
                var listBox = sender as ListBox;
                var listViewItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
                
                if (listViewItem != null && listViewItem.DataContext is DownloadedItem item)
                {
                    var data = new DataObject(DataFormats.FileDrop, new string[] { item.Path });
                    DragDrop.DoDragDrop(listViewItem, data, DragDropEffects.Copy);
                }
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        do
        {
            if (current is T ancestor)
            {
                return ancestor;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        while (current != null);
        return null;
    }
}

public class DownloadedItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}