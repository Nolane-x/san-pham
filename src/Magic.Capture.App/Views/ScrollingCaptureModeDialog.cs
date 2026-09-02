using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Magic.Capture.App.Views;

internal enum ScrollingCaptureMode
{
    Vertical,
    Horizontal,
    Grid2D
}

internal sealed record ScrollingCaptureModeSelection(ScrollingCaptureMode Mode, int Rows, int Columns);

internal static class ScrollingCaptureModeDialog
{
    public static async Task<ScrollingCaptureModeSelection?> ShowAsync(XamlRoot xamlRoot)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        var mode = new ComboBox
        {
            Header = "Capture mode",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedIndex = 0,
            ItemsSource = new[] { "Vertical", "Horizontal", "2D grid" }
        };
        var rows = new NumberBox
        {
            Header = "Rows",
            Minimum = 2,
            Maximum = 8,
            Value = 2,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            IsEnabled = false
        };
        var columns = new NumberBox
        {
            Header = "Columns",
            Minimum = 2,
            Maximum = 8,
            Value = 2,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            IsEnabled = false
        };
        mode.SelectionChanged += (_, _) =>
        {
            var grid = mode.SelectedIndex == 2;
            rows.IsEnabled = grid;
            columns.IsEnabled = grid;
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "Choose how Magic Capture should follow the selected scrolling region. 2D mode is bounded to an 8×8 / 64-tile safety grid.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(mode);
        panel.Children.Add(rows);
        panel.Children.Add(columns);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Scrolling capture",
            Content = panel,
            PrimaryButtonText = "Select region",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        var selected = mode.SelectedIndex switch
        {
            1 => ScrollingCaptureMode.Horizontal,
            2 => ScrollingCaptureMode.Grid2D,
            _ => ScrollingCaptureMode.Vertical
        };
        return new ScrollingCaptureModeSelection(
            selected,
            BoundGridValue(rows.Value),
            BoundGridValue(columns.Value));
    }

    private static int BoundGridValue(double value) =>
        double.IsFinite(value) ? Math.Clamp((int)Math.Round(value), 2, 8) : 2;
}
