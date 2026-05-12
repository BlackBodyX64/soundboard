using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TikTokSoundboard.Controls;

public partial class KeyPickerDialog : Window
{
    public string? SelectedKey { get; private set; }

    public KeyPickerDialog()
    {
        InitializeComponent();
        BuildKeyboard();
    }

    private void BuildKeyboard()
    {
        // Definitions mirrored from MainWindow to keep it simple
        string[] fKeys = { "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12" };
        string[][] kbRows = {
            new[] { "1","2","3","4","5","6","7","8","9","0" },
            new[] { "Q","W","E","R","T","Y","U","I","O","P" },
            new[] { "A","S","D","F","G","H","J","K","L" },
            new[] { "Z","X","C","V","B","N","M" },
        };
        string[][] numpadRows = {
            new[] { "Num7","Num8","Num9" },
            new[] { "Num4","Num5","Num6" },
            new[] { "Num1","Num2","Num3" },
            new[] { "Num0","Num.","Num+" },
        };

        foreach (var k in fKeys)
            FKeyPanel.Children.Add(CreateKeyButton(k));

        foreach (var row in kbRows)
        {
            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 2, 0, 2) };
            foreach (var k in row)
                rowPanel.Children.Add(CreateKeyButton(k));
            KbRowsPanel.Children.Add(rowPanel);
        }

        foreach (var row in numpadRows)
        {
            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 2, 0, 2) };
            foreach (var k in row)
            {
                var display = k.Replace("Num", "");
                rowPanel.Children.Add(CreateKeyButton(k, display));
            }
            NumpadPanel.Children.Add(rowPanel);
        }
    }

    private Button CreateKeyButton(string keyId, string? displayText = null)
    {
        var btn = new Button
        {
            Content = displayText ?? keyId,
            Width = 32,
            Height = 32,
            Margin = new System.Windows.Thickness(2),
            Background = new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3d)),
            Foreground = Brushes.White,
            BorderThickness = new System.Windows.Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x48, 0x4f, 0x58)),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btn.Click += (s, e) =>
        {
            SelectedKey = keyId;
            DialogResult = true;
        };
        return btn;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        this.Close();
    }
}
