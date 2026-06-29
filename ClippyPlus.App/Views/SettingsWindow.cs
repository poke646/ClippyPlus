using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using ClippyPlus.App.Services;

namespace ClippyPlus.App.Views;

public sealed class SettingsWindow : Window
{
    private readonly ComboBox _languageComboBox;
    private readonly TextBox _captureHotkeyTextBox;
    private readonly TextBox _editCaptureHotkeyTextBox;
    private readonly TextBox _clipboardHistoryHotkeyTextBox;
    private readonly CheckBox _hideSharingCheckBox;
    private CaptureTarget? _captureTarget;

    public SettingsWindow(AppSettings settings)
    {
        Title = LocalizationService.T("Settings.Title");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.CreateWindowIcon();

        _languageComboBox = new ComboBox
        {
            ItemsSource = LocalizationService.SupportedLanguages,
            SelectedItem = LocalizationService.SupportedLanguages.FirstOrDefault(language => language.Code == settings.LanguageCode)
                ?? LocalizationService.SupportedLanguages.First(language => language.Code == LocalizationService.DefaultLanguageCode),
        };
        _captureHotkeyTextBox = new TextBox
        {
            Text = settings.CaptureHotkey,
            IsReadOnly = true,
        };
        _editCaptureHotkeyTextBox = new TextBox
        {
            Text = settings.EditCaptureHotkey,
            IsReadOnly = true,
        };
        _clipboardHistoryHotkeyTextBox = new TextBox
        {
            Text = settings.ClipboardHistoryHotkey,
            IsReadOnly = true,
        };
        _hideSharingCheckBox = new CheckBox
        {
            Content = LocalizationService.T("Settings.HideSharing"),
            IsChecked = settings.HideSharingEnabled,
        };

        var recordCaptureButton = new Button { Content = LocalizationService.T("Settings.Record") };
        recordCaptureButton.Click += (_, _) => BeginCapture(CaptureTarget.ScreenCapture);
        var recordEditCaptureButton = new Button { Content = LocalizationService.T("Settings.Record") };
        recordEditCaptureButton.Click += (_, _) => BeginCapture(CaptureTarget.EditScreenCapture);
        var recordHistoryButton = new Button { Content = LocalizationService.T("Settings.Record") };
        recordHistoryButton.Click += (_, _) => BeginCapture(CaptureTarget.ClipboardHistory);
        var saveButton = new Button { Content = LocalizationService.T("Settings.Save"), MinWidth = 88 };
        saveButton.Click += async (_, _) => await SaveAsync();
        var cancelButton = new Button { Content = LocalizationService.T("Settings.Cancel"), MinWidth = 88 };
        cancelButton.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Spacing = 12,
            Children =
            {
                CreateLanguageRow(_languageComboBox),
                CreateHotkeyRow(LocalizationService.T("Settings.ScreenCapture"), _captureHotkeyTextBox, recordCaptureButton),
                CreateHotkeyRow(LocalizationService.T("Settings.EditScreenCapture"), _editCaptureHotkeyTextBox, recordEditCaptureButton),
                CreateHotkeyRow(LocalizationService.T("Settings.ClipboardHistory"), _clipboardHistoryHotkeyTextBox, recordHistoryButton),
                _hideSharingCheckBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancelButton, saveButton },
                },
            },
        };

        KeyDown += OnKeyDown;
    }

    public event EventHandler<AppSettings>? Saved;

    private static Control CreateLanguageRow(ComboBox comboBox)
    {
        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(150)),
                new ColumnDefinition(GridLength.Star),
            },
            Children =
            {
                new TextBlock
                {
                    Text = LocalizationService.T("Settings.Language"),
                    VerticalAlignment = VerticalAlignment.Center,
                },
                comboBox,
            },
        }.Also(grid =>
        {
            Grid.SetColumn(comboBox, 1);
        });
    }

    private static Control CreateHotkeyRow(string label, TextBox textBox, Button button)
    {
        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(150)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                textBox,
                button,
            },
        }.Also(grid =>
        {
            Grid.SetColumn(textBox, 1);
            Grid.SetColumn(button, 2);
            button.Margin = new Avalonia.Thickness(8, 0, 0, 0);
        });
    }

    private void BeginCapture(CaptureTarget target)
    {
        _captureTarget = target;
        var textBox = target switch
        {
            CaptureTarget.ScreenCapture => _captureHotkeyTextBox,
            CaptureTarget.EditScreenCapture => _editCaptureHotkeyTextBox,
            _ => _clipboardHistoryHotkeyTextBox,
        };
        textBox.Text = LocalizationService.T("Settings.PressShortcut");
        Focus();
    }

    private async Task SaveAsync()
    {
        var settings = new AppSettings
        {
            LanguageCode = (_languageComboBox.SelectedItem as SupportedLanguage)?.Code ?? LocalizationService.DefaultLanguageCode,
            CaptureHotkey = _captureHotkeyTextBox.Text ?? string.Empty,
            EditCaptureHotkey = _editCaptureHotkeyTextBox.Text ?? string.Empty,
            ClipboardHistoryHotkey = _clipboardHistoryHotkeyTextBox.Text ?? string.Empty,
            HideSharingEnabled = _hideSharingCheckBox.IsChecked == true,
        };

        if (!SettingsStore.TryValidate(settings, out _, out _, out _, out var error))
        {
            await MessageWindow.ShowAsync("ClippyPlus", error, this);
            return;
        }

        Saved?.Invoke(this, settings);
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_captureTarget is not { } target)
        {
            return;
        }

        if (IsModifierOnly(e.Key))
        {
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            modifiers |= HotkeyModifiers.Meta;
        }

        if (modifiers == HotkeyModifiers.None)
        {
            return;
        }

        var gesture = new HotkeyGesture(modifiers, ToHotkeyKey(e.Key));
        if (target == CaptureTarget.ScreenCapture)
        {
            _captureHotkeyTextBox.Text = gesture.ToString();
        }
        else if (target == CaptureTarget.EditScreenCapture)
        {
            _editCaptureHotkeyTextBox.Text = gesture.ToString();
        }
        else
        {
            _clipboardHistoryHotkeyTextBox.Text = gesture.ToString();
        }

        _captureTarget = null;
        e.Handled = true;
    }

    private static bool IsModifierOnly(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;
    }

    private static string ToHotkeyKey(Key key)
    {
        return key == Key.Space ? "Space" : HotkeyGesture.NormalizeKey(key.ToString());
    }

    private enum CaptureTarget
    {
        ScreenCapture,
        EditScreenCapture,
        ClipboardHistory,
    }
}

internal static class ControlExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
