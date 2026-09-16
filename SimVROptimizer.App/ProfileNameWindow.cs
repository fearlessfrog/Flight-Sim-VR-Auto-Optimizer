using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SimVROptimizer.App;

public sealed class ProfileNameWindow : Window
{
    private readonly TextBox _nameBox;
    public string ProfileName => _nameBox.Text.Trim();

    public ProfileNameWindow(string title, string prompt, string initialName)
    {
        Title = title;
        Width = 430;
        Height = 190;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(12, 18, 20));
        Foreground = Brushes.White;

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = prompt,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(Color.FromRgb(125, 211, 252)),
            TextWrapping = TextWrapping.Wrap
        });
        _nameBox = new TextBox { Text = initialName, Margin = new Thickness(0, 12, 0, 14), Padding = new Thickness(8, 5, 8, 5) };
        root.Children.Add(_nameBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "CANCEL", MinWidth = 90, Padding = new Thickness(10, 5, 10, 5), IsCancel = true };
        var confirm = new Button { Content = "CONFIRM", MinWidth = 90, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        confirm.Click += (_, _) =>
        {
            if (ProfileName.Length == 0) return;
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => { _nameBox.Focus(); _nameBox.SelectAll(); };
    }
}
