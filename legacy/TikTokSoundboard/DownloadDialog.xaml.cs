using System.Windows;
using System.Windows.Input;
using TikTokSoundboard.Services;

namespace TikTokSoundboard;

public partial class DownloadDialog : Window
{
    private readonly YtDlpService _ytDlp = new();
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Path to the downloaded audio file (set on success).
    /// </summary>
    public string? DownloadedFilePath { get; private set; }

    /// <summary>
    /// Title/name of the downloaded audio.
    /// </summary>
    public string? DownloadedTitle { get; private set; }

    public DownloadDialog()
    {
        InitializeComponent();
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        DialogResult = false;
        Close();
    }

    private void OnUrlBoxFocus(object sender, RoutedEventArgs e)
    {
        // Auto-paste from clipboard if it looks like a URL
        try
        {
            if (string.IsNullOrWhiteSpace(UrlBox.Text) && Clipboard.ContainsText())
            {
                var clipText = Clipboard.GetText().Trim();
                if (YtDlpService.IsSupportedUrl(clipText))
                {
                    UrlBox.Text = clipText;
                    UrlBox.SelectAll();
                }
            }
        }
        catch { }
    }

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            SetStatus("⚠️", "กรุณาใส่ URL ก่อน", "#f0883e");
            return;
        }

        if (!YtDlpService.IsSupportedUrl(url))
        {
            SetStatus("⚠️", "URL ไม่ถูกต้อง — รองรับเฉพาะ YouTube และ TikTok", "#f0883e");
            return;
        }

        // Start download
        DownloadBtn.IsEnabled = false;
        UrlBox.IsEnabled = false;
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.IsIndeterminate = true;

        _cts = new CancellationTokenSource();
        var progress = new Progress<DownloadProgressInfo>(info =>
        {
            Dispatcher.Invoke(() => SetStatus("⏳", info.Message, "#58a6ff", info.Percentage));
        });

        try
        {
            // First get title
            SetStatus("⏳", "กำลังดึงข้อมูลวิดีโอ...", "#58a6ff", 0);
            DownloadedTitle = await _ytDlp.GetTitleAsync(url, _cts.Token);

            if (!string.IsNullOrEmpty(DownloadedTitle))
            {
                SetStatus("⏳", $"กำลังดาวน์โหลด: {DownloadedTitle}", "#58a6ff", 0);
            }

            var config = ConfigService.Load();
            var downloadDir = string.IsNullOrWhiteSpace(config.DownloadDirectory) 
                ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TikTokSoundboardDownloads") 
                : config.DownloadDirectory;

            // Download audio
            DownloadedFilePath = await _ytDlp.DownloadAudioAsync(url, downloadDir, "mp3", progress, _cts.Token);

            if (!string.IsNullOrEmpty(DownloadedFilePath))
            {
                SetStatus("✅", $"สำเร็จ! {System.IO.Path.GetFileName(DownloadedFilePath)}", "#3fb950", 100);
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = 100;

                // Auto-close with success after a short delay
                await Task.Delay(800);
                DialogResult = true;
                Close();
            }
            else
            {
                SetStatus("❌", "ดาวน์โหลดไม่สำเร็จ — ลองตรวจสอบ URL อีกครั้ง", "#f85149", 0);
                ResetButtons();
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("🚫", "ยกเลิกการดาวน์โหลด", "#8b949e", 0);
            ResetButtons();
        }
        catch (Exception ex)
        {
            SetStatus("❌", $"เกิดข้อผิดพลาด: {ex.Message}", "#f85149", 0);
            ResetButtons();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            return;
        }

        DialogResult = false;
        Close();
    }

    private void SetStatus(string icon, string message, string colorHex, double percentage = -1)
    {
        StatusIcon.Text = icon;
        StatusText.Text = message;
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(color);
        
        if (percentage >= 0)
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = percentage;
        }
        else
        {
            ProgressBar.IsIndeterminate = true;
        }
    }

    private void ResetButtons()
    {
        DownloadBtn.IsEnabled = true;
        UrlBox.IsEnabled = true;
        ProgressBar.Visibility = Visibility.Collapsed;
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = 0;
    }
}
