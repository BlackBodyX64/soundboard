using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using TikTokSoundboard.Models;

namespace TikTokSoundboard;

public partial class SettingsDialog : Window
{
    public SoundPad Pad { get; private set; }
    public bool Saved { get; private set; }
    public bool Cleared { get; private set; }

    public SettingsDialog(SoundPad pad, string accentColorHex)
    {
        InitializeComponent();

        Pad = new SoundPad
        {
            Key = pad.Key,
            SoundPath = pad.SoundPath,
            SoundName = pad.SoundName,
            Volume = pad.Volume
        };

        TitleText.Text = $"⚙ ตั้งค่าปุ่ม [ {pad.Key} ]";
        FilePathBox.Text = pad.SoundPath;
        DisplayNameBox.Text = pad.SoundName;
        VolumeSlider.Value = pad.Volume;
        VolumeLabel.Text = $"{pad.Volume}%";
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "เลือกไฟล์เสียง",
            Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.flac;*.aac;*.wma|All Files|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            FilePathBox.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(DisplayNameBox.Text))
            {
                DisplayNameBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeLabel != null)
            VolumeLabel.Text = $"{(int)e.NewValue}%";
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        Pad.SoundPath = FilePathBox.Text.Trim();
        Pad.SoundName = DisplayNameBox.Text.Trim();
        if (string.IsNullOrEmpty(Pad.SoundName) && !string.IsNullOrEmpty(Pad.SoundPath))
            Pad.SoundName = Path.GetFileNameWithoutExtension(Pad.SoundPath);
        Pad.Volume = (int)VolumeSlider.Value;

        Saved = true;
        DialogResult = true;
        Close();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        Pad.SoundPath = "";
        Pad.SoundName = "";
        Pad.Volume = 100;

        Cleared = true;
        DialogResult = true;
        Close();
    }
}
