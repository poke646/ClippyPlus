using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClippyPlus.App.Services;

namespace ClippyPlus.App.Views;

public sealed class PasswordWindow : Window
{
    private readonly TextBox _passwordTextBox;
    private readonly TextBox? _confirmPasswordTextBox;
    private readonly TextBlock _errorTextBlock;
    private readonly bool _isCreatingPassword;

    private PasswordWindow(bool isCreatingPassword)
    {
        _isCreatingPassword = isCreatingPassword;
        Title = LocalizationService.T(isCreatingPassword ? "Security.CreateTitle" : "Security.UnlockTitle");
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Icon = AppIcon.CreateWindowIcon();

        _passwordTextBox = new TextBox
        {
            PasswordChar = '*',
            PlaceholderText = LocalizationService.T("Security.Password"),
        };

        _confirmPasswordTextBox = isCreatingPassword
            ? new TextBox
            {
                PasswordChar = '*',
                PlaceholderText = LocalizationService.T("Security.ConfirmPassword"),
            }
            : null;

        _errorTextBlock = new TextBlock
        {
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        var okButton = new Button
        {
            Content = LocalizationService.T(isCreatingPassword ? "Security.SetPassword" : "Security.Unlock"),
            MinWidth = 90,
        };
        okButton.Click += (_, _) => Submit();

        var cancelButton = new Button
        {
            Content = LocalizationService.T("Settings.Cancel"),
            MinWidth = 90,
        };
        cancelButton.Click += (_, _) => Close(null);

        var inputPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = LocalizationService.T(isCreatingPassword ? "Security.CreateMessage" : "Security.UnlockMessage"),
                    TextWrapping = TextWrapping.Wrap,
                },
                _passwordTextBox,
            },
        };

        if (_confirmPasswordTextBox is not null)
        {
            inputPanel.Children.Add(_confirmPasswordTextBox);
        }

        inputPanel.Children.Add(_errorTextBlock);

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Spacing = 14,
            Children =
            {
                inputPanel,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancelButton, okButton },
                },
            },
        };

        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return)
            {
                Submit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };
        Opened += (_, _) => _passwordTextBox.Focus();
    }

    public static Task<string?> ShowCreateAsync(Window owner)
    {
        return new PasswordWindow(isCreatingPassword: true).ShowDialog<string?>(owner);
    }

    public static Task<string?> ShowUnlockAsync(Window owner)
    {
        return new PasswordWindow(isCreatingPassword: false).ShowDialog<string?>(owner);
    }

    private void Submit()
    {
        var password = _passwordTextBox.Text ?? string.Empty;
        if (_isCreatingPassword)
        {
            if (password.Length < 8)
            {
                ShowError(LocalizationService.T("Security.PasswordTooShort"));
                return;
            }

            if (password != (_confirmPasswordTextBox?.Text ?? string.Empty))
            {
                ShowError(LocalizationService.T("Security.PasswordsDoNotMatch"));
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(password))
        {
            ShowError(LocalizationService.T("Security.PasswordRequired"));
            return;
        }

        Close(password);
    }

    private void ShowError(string message)
    {
        _errorTextBlock.Text = message;
        _errorTextBlock.IsVisible = true;
    }
}
