using Avalonia.Controls;
using Avalonia.Layout;

namespace ClippyPlus.App.Views;

public sealed class MessageWindow : Window
{
    private MessageWindow(string title, string message)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        MinHeight = 150;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Icon = Services.AppIcon.CreateWindowIcon();

        var okButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 84,
        };
        okButton.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                okButton,
            },
        };
    }

    public static Task ShowAsync(string title, string message, Window? owner)
    {
        var window = new MessageWindow(title, message);
        if (owner is not null)
        {
            return window.ShowDialog(owner);
        }

        var completion = new TaskCompletionSource();
        window.Closed += (_, _) => completion.SetResult();
        window.Show();
        return completion.Task;
    }
}
