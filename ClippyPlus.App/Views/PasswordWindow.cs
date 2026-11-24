using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ClippyPlus.App.Services;

namespace ClippyPlus.App.Views;

public sealed class PasswordWindow : Window
{
    private static PasswordWindow? _activeWindow;
    private readonly TextBox _passwordTextBox;
    private readonly TextBox? _confirmPasswordTextBox;
    private readonly TextBlock _errorTextBlock;
    private readonly bool _isCreatingPassword;
    private readonly Func<string, bool>? _tryUnlock;

    private PasswordWindow(bool isCreatingPassword, bool verifyCurrent = false, Func<string, bool>? tryUnlock = null)
    {
        _isCreatingPassword = isCreatingPassword;
        _tryUnlock = tryUnlock;
        Title = LocalizationService.T(isCreatingPassword ? "Security.CreateTitle" : "Security.UnlockTitle");
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Topmost = true;
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
        cancelButton.Click += (_, _) => Close();

        var inputPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = LocalizationService.T(verifyCurrent ? "Security.CurrentPasswordMessage" : isCreatingPassword ? "Security.CreateMessage" : "Security.UnlockMessage"),
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

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
            else if (e.Key is Key.Enter or Key.Return && e.Source is not Button)
            {
                e.Handled = true;
                Submit();
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Opened += (_, _) =>
        {
            _activeWindow = this;
            BringToFront();
        };
        Closed += (_, _) =>
        {
            if (_activeWindow == this) _activeWindow = null;
        };
    }

    public static void ActivateCurrent() => _activeWindow?.BringToFront();

    private void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Topmost = false;
        Topmost = true;
        Activate();
        _passwordTextBox.Focus();
    }

    public static Task<string?> ShowCurrentAsync(Window owner)
    {
        return new PasswordWindow(false, verifyCurrent: true).ShowDialog<string?>(owner);
    }

    public static Task<string?> ShowCreateAsync(Window owner)
    {
        return new PasswordWindow(isCreatingPassword: true).ShowDialog<string?>(owner);
    }

    public static Task<bool> ShowUnlockAsync(Window owner, Func<string, bool> tryUnlock)
    {
        return new PasswordWindow(isCreatingPassword: false, tryUnlock: tryUnlock).ShowDialog<bool>(owner);
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

        if (_tryUnlock is { } tryUnlock)
        {
            SubmitUnlock(password, tryUnlock);
        }
        else
        {
            Close(password);
        }
    }

    private void SubmitUnlock(string password, Func<string, bool> tryUnlock)
    {
        _passwordTextBox.Text = string.Empty;
        string? error;
        try
        {
            error = tryUnlock(password) ? null : LocalizationService.T("Security.InvalidPassword");
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (error is null)
        {
            Close(true);
            return;
        }

        ShowError(error);
        _passwordTextBox.Focus();
    }

    private void ShowError(string message)
    {
        _errorTextBlock.Text = message;
        _errorTextBlock.IsVisible = true;
    }
}
