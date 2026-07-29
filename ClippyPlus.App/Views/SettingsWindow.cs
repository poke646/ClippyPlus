using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClippyPlus.App.Models;
using ClippyPlus.App.Services;

namespace ClippyPlus.App.Views;

public sealed class SettingsWindow : Window
{
    private readonly ComboBox _languageComboBox;
    private readonly TextBox _captureHotkeyTextBox;
    private readonly TextBox _editCaptureHotkeyTextBox;
    private readonly TextBox _clipboardHistoryHotkeyTextBox;
    private readonly CheckBox _hideSharingCheckBox;
    private readonly IClipboardHistoryStore _historyStore;
    private readonly StackPanel _sectionRowsPanel;
    private readonly List<SectionEditorItem> _sections;
    private readonly IReadOnlyList<ClipboardSection> _originalSections;
    private readonly Button _addSectionButton;
    private CaptureTarget? _captureTarget;

    public SettingsWindow(AppSettings settings, IClipboardHistoryStore historyStore)
    {
        _historyStore = historyStore;
        _originalSections = historyStore.GetSections();
        _sections = _originalSections
            .Select(section => new SectionEditorItem(section.Id, section.Name, section.ItemLimit, section.ItemCount))
            .ToList();

        Title = LocalizationService.T("Settings.Title");
        Width = 560;
        Height = 620;
        MinHeight = 480;
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

        _sectionRowsPanel = new StackPanel { Spacing = 8 };
        _addSectionButton = new Button
        {
            Content = LocalizationService.T("Settings.AddSection"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _addSectionButton.Click += async (_, _) =>
        {
            if (_sections.Count >= ClipboardHistoryStore.MaxSections)
            {
                await MessageWindow.ShowAsync(
                    "ClippyPlus",
                    LocalizationService.T("Validation.TooManySections"),
                    this);
                return;
            }

            _sections.Add(new SectionEditorItem(null, CreateUniqueSectionName(), 100, 0));
            RebuildSectionRows();
        };
        RebuildSectionRows();

        var body = new StackPanel
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
                new TextBlock
                {
                    Text = LocalizationService.T("Settings.PinnedSections"),
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Avalonia.Thickness(0, 8, 0, 0),
                },
                new TextBlock
                {
                    Text = LocalizationService.T("Settings.PinnedSectionsHint"),
                    Foreground = Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap,
                },
                _sectionRowsPanel,
                _addSectionButton,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancelButton, saveButton },
                },
            },
        };
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = body,
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

        var currentSections = _historyStore.GetSections();
        var sectionError = ValidateSections(currentSections);
        if (sectionError is not null)
        {
            await MessageWindow.ShowAsync("ClippyPlus", sectionError, this);
            return;
        }

        var retainedIds = _sections.Where(section => section.Id.HasValue).Select(section => section.Id!.Value).ToHashSet();
        var removedNonEmpty = currentSections
            .Where(section => !retainedIds.Contains(section.Id) && section.ItemCount > 0)
            .ToList();
        if (removedNonEmpty.Count > 0)
        {
            var details = string.Join(
                Environment.NewLine,
                removedNonEmpty.Select(section => $"{section.Name}: {section.ItemCount}"));
            var confirmed = await MessageWindow.ConfirmAsync(
                LocalizationService.T("Settings.DeleteSectionsTitle"),
                LocalizationService.Format("Settings.DeleteSectionsWarning", details),
                this);
            if (!confirmed)
            {
                return;
            }
        }

        try
        {
            _historyStore.ApplySections(_sections
                .Select(section => new ClipboardSectionDraft(section.Id, section.Name.Trim(), section.ItemLimit))
                .ToList());
        }
        catch (Exception ex)
        {
            await MessageWindow.ShowAsync("ClippyPlus", ex.Message, this);
            return;
        }

        Saved?.Invoke(this, settings);
        Close();
    }

    private void RebuildSectionRows()
    {
        _sectionRowsPanel.Children.Clear();
        foreach (var section in _sections)
        {
            var nameTextBox = new TextBox
            {
                Text = section.Name,
                MaxLength = ClipboardHistoryStore.MaxSectionNameLength,
                PlaceholderText = LocalizationService.T("Settings.SectionName"),
            };
            nameTextBox.TextChanged += (_, _) => section.Name = nameTextBox.Text ?? string.Empty;

            var limitInput = new NumericUpDown
            {
                Minimum = 1,
                Maximum = ClipboardHistoryStore.MaxSectionItems,
                Value = section.ItemLimit,
                FormatString = "0",
                Width = 82,
            };
            limitInput.ValueChanged += (_, _) => section.ItemLimit = (int)(limitInput.Value ?? 1);

            var deleteButton = new Button
            {
                Content = LocalizationService.T("Settings.RemoveSection"),
            };
            deleteButton.Click += (_, _) =>
            {
                _sections.Remove(section);
                RebuildSectionRows();
            };

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                },
                Children = { nameTextBox, limitInput, deleteButton },
            };
            Grid.SetColumn(limitInput, 1);
            Grid.SetColumn(deleteButton, 2);
            limitInput.Margin = new Avalonia.Thickness(8, 0, 0, 0);
            deleteButton.Margin = new Avalonia.Thickness(8, 0, 0, 0);
            ToolTip.SetTip(
                limitInput,
                LocalizationService.Format("Settings.SectionCount", section.OriginalItemCount));
            _sectionRowsPanel.Children.Add(row);
        }

        _addSectionButton.IsEnabled = _sections.Count < ClipboardHistoryStore.MaxSections;
    }

    private string? ValidateSections(IReadOnlyList<ClipboardSection> currentSections)
    {
        if (_sections.Count > ClipboardHistoryStore.MaxSections)
        {
            return LocalizationService.T("Validation.TooManySections");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in _sections)
        {
            var name = section.Name.Trim();
            if (name.Length == 0 || name.Length > ClipboardHistoryStore.MaxSectionNameLength)
            {
                return LocalizationService.T("Validation.SectionName");
            }

            if (!names.Add(name))
            {
                return LocalizationService.T("Validation.DuplicateSectionName");
            }

            if (section.ItemLimit is < 1 or > ClipboardHistoryStore.MaxSectionItems)
            {
                return LocalizationService.T("Validation.SectionLimit");
            }

            var currentItemCount = section.Id is { } id
                ? currentSections.FirstOrDefault(current => current.Id == id)?.ItemCount ?? section.OriginalItemCount
                : 0;
            if (section.ItemLimit < currentItemCount)
            {
                return LocalizationService.Format(
                    "Validation.SectionLimitBelowCountDetail",
                    name,
                    currentItemCount);
            }
        }

        return null;
    }

    private string CreateUniqueSectionName()
    {
        var baseName = LocalizationService.T("Settings.NewSection");
        var candidate = baseName;
        var suffix = 2;
        while (_sections.Any(section => string.Equals(section.Name.Trim(), candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} {suffix++}";
        }

        return candidate;
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

    private sealed class SectionEditorItem
    {
        public SectionEditorItem(long? id, string name, int itemLimit, int originalItemCount)
        {
            Id = id;
            Name = name;
            ItemLimit = itemLimit;
            OriginalItemCount = originalItemCount;
        }

        public long? Id { get; }

        public string Name { get; set; }

        public int ItemLimit { get; set; }

        public int OriginalItemCount { get; }
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
