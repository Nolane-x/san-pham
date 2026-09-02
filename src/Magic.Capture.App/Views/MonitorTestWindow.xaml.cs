using Magic.Capture.App.Capture;
using Magic.Capture.App.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Magic.Capture.App.Views;

public sealed partial class MonitorTestWindow : Window
{
    private readonly MonitorInfo _monitor;

    internal MonitorTestWindow(MonitorInfo monitor)
    {
        InitializeComponent();
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        MonitorLabel.Text = $"{monitor.DisplayName} · {monitor.Bounds.Width}×{monitor.Bounds.Height}";
        Closed += (_, _) => TestSurface.Children.Clear();
    }

    internal void ShowOnMonitor()
    {
        WindowHelpers.MakeBorderlessTopmost(this);
        WindowHelpers.MoveAndResize(this, _monitor.Bounds.X, _monitor.Bounds.Y, _monitor.Bounds.Width, _monitor.Bounds.Height);
        Activate();
    }

    private void SetSolid(Windows.UI.Color color)
    {
        TestSurface.Children.Clear();
        TestSurface.Background = new SolidColorBrush(color);
    }

    private void Black_Click(object sender, RoutedEventArgs e) => SetSolid(Microsoft.UI.Colors.Black);
    private void White_Click(object sender, RoutedEventArgs e) => SetSolid(Microsoft.UI.Colors.White);
    private void Red_Click(object sender, RoutedEventArgs e) => SetSolid(Microsoft.UI.Colors.Red);
    private void Green_Click(object sender, RoutedEventArgs e) => SetSolid(Microsoft.UI.Colors.Lime);
    private void Blue_Click(object sender, RoutedEventArgs e) => SetSolid(Microsoft.UI.Colors.Blue);

    private void Gradient_Click(object sender, RoutedEventArgs e)
    {
        TestSurface.Children.Clear();
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0.5), EndPoint = new Windows.Foundation.Point(1, 0.5) };
        brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.Black, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.White, Offset = 1 });
        TestSurface.Background = brush;
    }

    private void ColorBars_Click(object sender, RoutedEventArgs e)
    {
        TestSurface.Background = new SolidColorBrush(Microsoft.UI.Colors.Black);
        TestSurface.Children.Clear();
        var grid = new Grid();
        var colors = new[]
        {
            Microsoft.UI.Colors.White, Microsoft.UI.Colors.Yellow, Microsoft.UI.Colors.Cyan, Microsoft.UI.Colors.Lime,
            Microsoft.UI.Colors.Magenta, Microsoft.UI.Colors.Red, Microsoft.UI.Colors.Blue, Microsoft.UI.Colors.Black
        };
        foreach (var _ in colors) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < colors.Length; i++)
        {
            var block = new Border { Background = new SolidColorBrush(colors[i]) };
            Grid.SetColumn(block, i);
            grid.Children.Add(block);
        }
        TestSurface.Children.Add(grid);
    }

    private void Grid_Click(object sender, RoutedEventArgs e)
    {
        TestSurface.Background = new SolidColorBrush(Microsoft.UI.Colors.Black);
        TestSurface.Children.Clear();
        var canvas = new Canvas { IsHitTestVisible = false };
        const int step = 64;
        for (var x = 0; x <= _monitor.Bounds.Width; x += step)
            canvas.Children.Add(new Border { Width = 1, Height = _monitor.Bounds.Height, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 255, 255, 255)), Margin = new Thickness(x, 0, 0, 0) });
        for (var y = 0; y <= _monitor.Bounds.Height; y += step)
            canvas.Children.Add(new Border { Width = _monitor.Bounds.Width, Height = 1, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 255, 255, 255)), Margin = new Thickness(0, y, 0, 0) });
        TestSurface.Children.Add(canvas);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Close_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; Close(); }
    private void ToggleControls_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ControlsPanel.Visibility = ControlsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        args.Handled = true;
    }
}
