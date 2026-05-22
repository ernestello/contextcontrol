using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Views;

public sealed class FileRuleListEditorWindow : Window
{
    private readonly StackPanel _itemsPanel;
    private readonly TextBox _newEntryBox;
    private readonly List<TextBox> _entryEditors = [];
    private readonly string _focusValue;

    public FileRuleListEditorWindow(
        string title,
        IEnumerable<string> values,
        string watermark,
        string? focusValue = null)
    {
        _focusValue = focusValue ?? "";
        Title = title;
        Width = 520;
        Height = 720;
        MinWidth = 380;
        MinHeight = 420;
        SystemDecorations = SystemDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _itemsPanel = new StackPanel
        {
            Spacing = 1,
            Margin = new Thickness(0, 0, 12, 0)
        };

        _newEntryBox = new TextBox
        {
            Watermark = watermark,
            FontSize = 9.5,
            MinHeight = 20,
            Padding = new Thickness(5, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _newEntryBox.Classes.Add("rule-add");
        _newEntryBox.KeyDown += OnNewEntryKeyDown;

        Content = BuildContent(title);
        foreach (var value in Normalize(values))
        {
            AddEntryEditor(value);
        }

        ApplyTheme("empty");
    }

    public IReadOnlyList<string> Values { get; private set; } = [];

    public void ApplyTheme(string? themeKey)
    {
        WorkbenchThemeResources.Apply(this, themeKey);
        if (Resources.TryGetValue("AppBackgroundBrush", out var brush) && brush is IBrush background)
        {
            Background = background;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitToWorkingArea();

        if (!string.IsNullOrWhiteSpace(_focusValue))
        {
            var editor = _entryEditors.FirstOrDefault(item =>
                string.Equals(item.Text?.Trim(), _focusValue.Trim(), StringComparison.OrdinalIgnoreCase));
            if (editor is not null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    editor.Focus();
                    editor.SelectAll();
                });
                return;
            }
        }

        Dispatcher.UIThread.Post(() => _newEntryBox.Focus());
    }

    private Control BuildContent(string title)
    {
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeight.Bold
        };
        titleBlock.Classes.Add("settings-title");

        var addButton = SmallButton("+");
        addButton.Width = 30;
        addButton.Click += (_, _) => AddPendingEntry();

        var saveButton = CommandButton("Save");
        saveButton.Click += (_, _) =>
        {
            AddPendingEntry();
            Values = Normalize(_entryEditors.Select(editor => editor.Text ?? "")).ToArray();
            Close(true);
        };

        var cancelButton = CommandButton("Cancel");
        cancelButton.Click += (_, _) => Close(false);

        var addRow = BuildAddRow(addButton);
        var listHost = BuildListHost();
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                cancelButton,
                saveButton
            }
        };
        Grid.SetRow(addRow, 1);
        Grid.SetRow(listHost, 2);
        Grid.SetRow(actions, 3);

        var contentPanel = new Border
        {
            Margin = new Thickness(6),
            Padding = new Thickness(7),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
                RowSpacing = 5,
                Children =
                {
                    titleBlock,
                    addRow,
                    listHost,
                    actions
                }
            }
        };
        contentPanel.Classes.Add("settings-panel");
        Grid.SetRow(contentPanel, 1);

        return new Grid
        {
            RowDefinitions = new RowDefinitions("31,*"),
            Children =
            {
                BuildTitleBar(title),
                contentPanel
            }
        };
    }

    private Control BuildTitleBar(string title)
    {
        var titleBar = new Border
        {
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 8,
                Children =
                {
                    BuildTitleIcon(),
                    new TextBlock
                    {
                        Text = title,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    BuildWindowControls()
                }
            }
        };
        titleBar.Classes.Add("custom-titlebar");

        Grid.SetColumn(((Grid)titleBar.Child!).Children[1], 1);
        Grid.SetColumn(((Grid)titleBar.Child!).Children[2], 2);
        ((Grid)titleBar.Child!).Children[1].Classes.Add("window-title");
        titleBar.PointerPressed += OnTitleBarPointerPressed;
        return titleBar;
    }

    private static Control BuildTitleIcon()
    {
        var icon = new Border
        {
            Child = new TextBlock
            {
                Text = "▤",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        icon.Classes.Add("title-icon");
        ((TextBlock)icon.Child!).Classes.Add("title-icon-glyph");
        return icon;
    }

    private Control BuildWindowControls()
    {
        var minimize = WindowButton("-");
        minimize.Click += OnMinimizeClick;

        var maximize = WindowButton("□");
        maximize.Click += OnMaximizeRestoreClick;

        var close = WindowButton("x");
        close.Click += OnCloseClick;

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children =
            {
                minimize,
                maximize,
                close
            }
        };
    }

    private static Button WindowButton(string text)
    {
        var button = new Button
        {
            Content = text,
            Width = 34,
            Height = 26,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Classes.Add("window-control");
        if (string.Equals(text, "x", StringComparison.OrdinalIgnoreCase))
        {
            button.Classes.Add("close");
        }

        return button;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private Control BuildAddRow(Button addButton)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 4
        };
        row.Children.Add(_newEntryBox);
        Grid.SetColumn(addButton, 1);
        row.Children.Add(addButton);
        return row;
    }

    private Control BuildListHost()
    {
        var host = new Border
        {
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _itemsPanel
            }
        };
        host.Classes.Add("rule-list-host");
        return host;
    }

    private void AddEntryEditor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (_entryEditors.Any(editor => string.Equals(editor.Text?.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var editor = new TextBox
        {
            Text = value.Trim(),
            FontSize = 9.5,
            MinHeight = 18,
            Padding = new Thickness(5, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        editor.Classes.Add("rule-add");

        var removeButton = SmallButton("x");
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 3,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                editor,
                removeButton
            }
        };
        Grid.SetColumn(removeButton, 1);

        removeButton.Click += (_, _) =>
        {
            _entryEditors.Remove(editor);
            _itemsPanel.Children.Remove(row);
        };

        _entryEditors.Add(editor);
        _itemsPanel.Children.Add(row);
    }

    private void AddPendingEntry()
    {
        foreach (var value in Split(_newEntryBox.Text ?? ""))
        {
            AddEntryEditor(value);
        }

        _newEntryBox.Text = "";
        _newEntryBox.Focus();
    }

    private void OnNewEntryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        AddPendingEntry();
        e.Handled = true;
    }

    private void FitToWorkingArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var usableWidth = Math.Max(MinWidth, (screen.WorkingArea.Width / screen.Scaling) - 24);
        var usableHeight = Math.Max(MinHeight, (screen.WorkingArea.Height / screen.Scaling) - 24);
        MaxWidth = usableWidth;
        MaxHeight = usableHeight;
        Width = Math.Min(Math.Max(Width, MinWidth), usableWidth);
        Height = usableHeight;

        var widthPixels = (int)Math.Ceiling(Width * screen.Scaling);
        var heightPixels = (int)Math.Ceiling(Height * screen.Scaling);
        var maxX = Math.Max(screen.WorkingArea.X, screen.WorkingArea.X + screen.WorkingArea.Width - widthPixels);
        var maxY = Math.Max(screen.WorkingArea.Y, screen.WorkingArea.Y + screen.WorkingArea.Height - heightPixels);
        Position = new PixelPoint(
            Math.Clamp(Position.X, screen.WorkingArea.X, maxX),
            Math.Clamp(Position.Y, screen.WorkingArea.Y, maxY));
    }

    private static Button SmallButton(string text)
    {
        var button = new Button
        {
            Content = text,
            Width = 17,
            Height = 17,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            FontSize = 8,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Classes.Add(string.Equals(text, "x", StringComparison.OrdinalIgnoreCase) ? "rule-x" : "rule-mini");
        return button;
    }

    private static Button CommandButton(string text)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 24,
            Padding = new Thickness(8, 0),
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Classes.Add("dialog-command");
        if (string.Equals(text, "Save", StringComparison.OrdinalIgnoreCase))
        {
            button.Classes.Add("primary");
        }

        return button;
    }

    private static IEnumerable<string> Normalize(IEnumerable<string> values)
    {
        return values
            .SelectMany(Split)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Split(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(['\r', '\n', ',', ';', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item));
    }
}
