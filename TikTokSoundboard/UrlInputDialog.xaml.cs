using System.Windows;
using System.Windows.Input;
using TikTokSoundboard.Services;

namespace TikTokSoundboard;

public partial class UrlInputDialog : Window
{
    public string Url { get; private set; } = "";

    public UrlInputDialog()
    {
        InitializeComponent();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnUrlBoxFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(UrlBox.Text) && Clipboard.ContainsText())
            {
                var clip = Clipboard.GetText().Trim();
                if (YtDlpService.IsSupportedUrl(clip))
                {
                    UrlBox.Text = clip;
                    UrlBox.SelectAll();
                }
            }
        }
        catch { }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Confirm();
        else if (e.Key == Key.Escape) Cancel();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => Confirm();
    private void OnCancelClick(object sender, RoutedEventArgs e)  => Cancel();

    private void Confirm()
    {
        Url = UrlBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel()
    {
        DialogResult = false;
        Close();
    }
}
