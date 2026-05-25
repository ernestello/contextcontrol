using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ContextControl.Workbench.Views;

public sealed partial class MainWindow
{
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

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximizeRestore()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            FitToWorkingArea();
            return;
        }

        FitMaximizedWindowToWorkingArea();
        WindowState = WindowState.Maximized;
    }

    private void OnSettingsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        OpenSettingsWindow();
        e.Handled = true;
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        OpenSettingsWindow();
        e.Handled = true;
    }

    private async void OnEditOpenProjectClick(object? sender, RoutedEventArgs e)
    {
        CloseEditMenuFlyout();
        await PickAndLoadProjectAsync("Open Project");
        e.Handled = true;
    }

    private async void OnEditNewProjectClick(object? sender, RoutedEventArgs e)
    {
        CloseEditMenuFlyout();
        await PickAndLoadProjectAsync("Add Project");
        e.Handled = true;
    }

    private void OnEditSettingsClick(object? sender, RoutedEventArgs e)
    {
        CloseEditMenuFlyout();
        OpenSettingsWindow();
        e.Handled = true;
    }

    private void CloseEditMenuFlyout()
    {
        EditMenuButton.Flyout?.Hide();
    }

    private void OpenSettingsWindow()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (_themeSettingsWindow is null)
        {
            _themeSettingsWindow = new ThemeSettingsWindow();
            _themeSettingsWindow.Closed += (_, _) => _themeSettingsWindow = null;
        }

        _themeSettingsWindow.DataContext = viewModel;
        _themeSettingsWindow.ApplyTheme(viewModel.ThemeKey, viewModel.UiFontFamily, viewModel.CodeFontFamily, viewModel.SkinKey);

        if (!_themeSettingsWindow.IsVisible)
        {
            _themeSettingsWindow.Show(this);
        }

        if (_themeSettingsWindow.WindowState == WindowState.Minimized)
        {
            _themeSettingsWindow.WindowState = WindowState.Normal;
        }

        _themeSettingsWindow.Activate();
    }

    private void OnResizeLeftPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.West, e);
    }

    private void OnResizeRightPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.East, e);
    }

    private void OnResizeTopPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.North, e);
    }

    private void OnResizeBottomPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.South, e);
    }

    private void OnResizeTopLeftPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.NorthWest, e);
    }

    private void OnResizeTopRightPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.NorthEast, e);
    }

    private void OnResizeBottomLeftPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.SouthWest, e);
    }

    private void OnResizeBottomRightPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.SouthEast, e);
    }

    private void BeginWindowResize(WindowEdge edge, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Maximized
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginResizeDrag(edge, e);
        e.Handled = true;
    }

    private void FitToWorkingArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var usableWidth = Math.Max(MinWidth, screen.WorkingArea.Width / screen.Scaling);
        var usableHeight = Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling);
        MaxWidth = usableWidth;
        MaxHeight = usableHeight;

        if (WindowState == WindowState.Maximized)
        {
            return;
        }

        Width = Math.Min(Math.Max(Width, MinWidth), usableWidth);
        Height = Math.Min(Math.Max(Height, MinHeight), usableHeight);

        var widthPixels = (int)Math.Ceiling(Width * screen.Scaling);
        var heightPixels = (int)Math.Ceiling(Height * screen.Scaling);
        var maxX = Math.Max(screen.WorkingArea.X, screen.WorkingArea.X + screen.WorkingArea.Width - widthPixels);
        var maxY = Math.Max(screen.WorkingArea.Y, screen.WorkingArea.Y + screen.WorkingArea.Height - heightPixels);
        Position = new PixelPoint(
            Math.Clamp(Position.X, screen.WorkingArea.X, maxX),
            Math.Clamp(Position.Y, screen.WorkingArea.Y, maxY));
    }

    private void FitMaximizedWindowToWorkingArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        MaxWidth = Math.Max(MinWidth, screen.WorkingArea.Width / screen.Scaling);
        MaxHeight = Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling);
    }
}
