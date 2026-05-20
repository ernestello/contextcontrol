using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Views;

public sealed partial class ThemeSettingsWindow : Window
{
    private bool _fileRulesSectionAttached;

    public ThemeSettingsWindow()
    {
        InitializeComponent();
        WorkbenchThemeResources.Apply(this, "empty");
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        AttachFileRulesSectionSafely();
    }

    public void ApplyTheme(string? themeKey)
    {
        WorkbenchThemeResources.Apply(this, themeKey);
    }

    private void AttachFileRulesSectionSafely()
    {
        if (_fileRulesSectionAttached)
        {
            return;
        }

        try
        {
            AttachFileRulesSection();
        }
        catch (Exception ex)
        {
            _fileRulesSectionAttached = true;
            Console.Error.WriteLine($"Context Control settings file-rules section failed to attach: {ex}");
        }
    }

    private void AttachFileRulesSection()
    {
        var section = BuildFileRulesSection();

        if (Content is ScrollViewer scrollViewer)
        {
            AttachInsideScrollViewer(scrollViewer, section);
            _fileRulesSectionAttached = true;
            return;
        }

        if (Content is Border border)
        {
            AttachInsideBorder(border, section);
            _fileRulesSectionAttached = true;
            return;
        }

        if (Content is Panel panel)
        {
            panel.Children.Add(section);
            _fileRulesSectionAttached = true;
            return;
        }

        if (Content is Control existingContent)
        {
            // Important: detach first. Adding the current Window.Content into a new panel while
            // it is still parented can terminate the settings window with no useful app-level report.
            Content = null;
            Content = WrapExistingContentWithFileRules(existingContent, section);
            _fileRulesSectionAttached = true;
            return;
        }

        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 10,
                Margin = new Thickness(12),
                Children =
                {
                    section
                }
            }
        };
        _fileRulesSectionAttached = true;
    }

    private static void AttachInsideScrollViewer(ScrollViewer scrollViewer, Control section)
    {
        if (scrollViewer.Content is StackPanel stack)
        {
            stack.Children.Add(section);
            return;
        }

        if (scrollViewer.Content is Panel panel)
        {
            panel.Children.Add(section);
            return;
        }

        if (scrollViewer.Content is Control existingContent)
        {
            scrollViewer.Content = null;
            scrollViewer.Content = new StackPanel
            {
                Spacing = 10,
                Margin = new Thickness(12),
                Children =
                {
                    existingContent,
                    section
                }
            };
            return;
        }

        scrollViewer.Content = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(12),
            Children =
            {
                section
            }
        };
    }

    private static void AttachInsideBorder(Border border, Control section)
    {
        if (border.Child is StackPanel stack)
        {
            stack.Children.Add(section);
            return;
        }

        if (border.Child is Panel panel)
        {
            panel.Children.Add(section);
            return;
        }

        if (border.Child is Control existingContent)
        {
            border.Child = null;
            border.Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    existingContent,
                    section
                }
            };
            return;
        }

        border.Child = section;
    }

    private static Control WrapExistingContentWithFileRules(Control existingContent, Control section)
    {
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 10,
                Margin = new Thickness(12),
                Children =
                {
                    existingContent,
                    section
                }
            }
        };
    }

    private static Control BuildFileRulesSection()
    {
        var status = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        };
        status.Bind(TextBlock.TextProperty, new Binding("FileRulesStatus"));

        var path = new TextBlock
        {
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.62
        };
        path.Bind(TextBlock.TextProperty, new Binding("FileRulesPath"));

        var editorGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8,
            MinHeight = 180
        };
        editorGrid.Children.Add(CreateRuleEditor(
            "Allowed file types",
            "SupportedFileTypesText",
            "Extensions that CC may show, count, export, and track."));
        var ignoredFiles = CreateRuleEditor(
            "Unallowed file types",
            "IgnoredFileTypesText",
            "Extensions ignored even when they would otherwise be supported.");
        Grid.SetColumn(ignoredFiles, 1);
        editorGrid.Children.Add(ignoredFiles);

        var ignoredDirectories = CreateRuleEditor(
            "Unallowed folders",
            "IgnoredDirectoriesText",
            "Directory names ignored by the IDE tree and external update watcher.");
        Grid.SetColumn(ignoredDirectories, 2);
        editorGrid.Children.Add(ignoredDirectories);

        var saveButton = new Button
        {
            Content = "Save file rules",
            MinHeight = 30,
            Padding = new Thickness(12, 2),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        saveButton.Bind(Button.CommandProperty, new Binding("SaveFileRulesCommand"));

        var resetButton = new Button
        {
            Content = "Reset defaults",
            MinHeight = 30,
            Padding = new Thickness(12, 2),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        resetButton.Bind(Button.CommandProperty, new Binding("ResetFileRulesCommand"));

        return new Border
        {
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "File rules",
                        FontSize = 14,
                        FontWeight = FontWeight.Bold
                    },
                    new TextBlock
                    {
                        Text = "Configure what files the IDE tree, file history, and external-update tracker can read or must ignore. One entry per line or comma-separated.",
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.72
                    },
                    path,
                    editorGrid,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children =
                        {
                            resetButton,
                            saveButton
                        }
                    },
                    status
                }
            }
        };
    }

    private static Control CreateRuleEditor(string title, string bindingPath, string help)
    {
        var textBox = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 128,
            TextWrapping = TextWrapping.NoWrap,
            Watermark = ".cs, .cpp, .ps1"
        };
        textBox.Bind(TextBox.TextProperty, new Binding(bindingPath) { Mode = BindingMode.TwoWay });

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeight.Bold
        };

        var helpBlock = new TextBlock
        {
            Text = help,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.62
        };
        Grid.SetRow(helpBlock, 1);
        Grid.SetRow(textBox, 2);

        return new Border
        {
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*"),
                RowSpacing = 5,
                Children =
                {
                    titleBlock,
                    helpBlock,
                    textBox
                }
            }
        };
    }
}
