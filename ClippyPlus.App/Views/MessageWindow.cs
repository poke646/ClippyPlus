using Avalonia.Controls;
using Avalonia.Layout;
using ClippyPlus.App.Services;

namespace ClippyPlus.App.Views;

public sealed class MessageWindow : Window
{
    private MessageWindow(string title, string message, bool showCancel)
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
            MinWidth = 84,
        };
        okButton.Click += (_, _) =>
        {
            if (showCancel)
            {
                Close(true);
            }
            else
            {
                Close();
            }
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (showCancel)
        {
            var cancelButton = new Button
            {
                Content = LocalizationService.T("Settings.Cancel"),
                MinWidth = 84,
            };
            cancelButton.Click += (_, _) => Close(false);
            buttons.Children.Add(cancelButton);
        }

        buttons.Children.Add(okButton);

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
                buttons,
            },
        };
    }

    public static Task ShowAsync(string title, string message, Window? owner)
    {
        var window = new MessageWindow(title, message, showCancel: false);
        if (owner is not null)
        {
            return window.ShowDialog(owner);
        }

        var completion = new TaskCompletionSource();
        window.Closed += (_, _) => completion.SetResult();
        window.Show();
        return completion.Task;
    }

    public static async Task<bool> ConfirmAsync(string title, string message, Window? owner)
    {
        var window = new MessageWindow(title, message, showCancel: true);
        if (owner is not null)
        {
            return await window.ShowDialog<bool>(owner);
        }

        var completion = new TaskCompletionSource<bool>();
        window.Closed += (_, _) => completion.TrySetResult(false);
        window.Show();
        return await completion.Task;
    }
}
