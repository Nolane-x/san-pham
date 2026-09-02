using Magic.Capture.App.Ai;
using Magic.Capture.App.Ai.Provider;
using Magic.Capture.App.Capture;
using Magic.Capture.Core.Ai;
using Magic.Capture.Core.ScreenGraph;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Magic.Capture.App.Views;

public sealed partial class MagicActionWindow : Window
{
    private readonly CaptureAsset _asset;
    private readonly ApplicationServices _services;
    private IReadOnlyList<MagicActionDefinition> _actions = [];
    private MagicActionExecutionResult? _execution;
    private CancellationTokenSource? _runCts;
    private readonly string? _initialActionId;
    private ScreenGraphDocument? _graph;

    internal MagicActionWindow(CaptureAsset asset, ApplicationServices services, string? initialActionId = null)
    {
        InitializeComponent();
        _asset = asset;
        _services = services;
        _initialActionId = initialActionId;
        var appWindow = Platform.WindowHelpers.GetAppWindow(this);
        appWindow.Resize(new Windows.Graphics.SizeInt32(1180, 760));
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await CaptureOverlayWindow.SetImageAsync(SourceImage, _asset.PngBytes);
        var custom = await _services.MagicActionStore.LoadAsync();
        _graph = await _services.ScreenGraph.BuildAsync(_asset, _services.Settings);
        var recommendations = MagicActionRecommender.Recommend(_graph);
        var recommendationScores = recommendations.ToDictionary(x => x.ActionId, x => x.Score, StringComparer.Ordinal);
        _actions = BuiltInMagicActions.All.Concat(custom)
            .OrderByDescending(a => recommendationScores.GetValueOrDefault(a.Id))
            .ThenBy(a => a.Category)
            .ThenBy(a => a.Name)
            .ToArray();
        ActionCombo.ItemsSource = _actions;
        var defaultActionId = _initialActionId ?? recommendations.FirstOrDefault()?.ActionId ?? "general.explain";
        ActionCombo.SelectedItem = _actions.FirstOrDefault(a => a.Id == defaultActionId) ?? _actions.FirstOrDefault();
        if (_initialActionId is null && recommendations.FirstOrDefault() is { } top)
            RecommendationText.Text = $"Recommended without AI: {BuiltInMagicActions.ById(top.ActionId).Name} — {top.Reason}";
        RefreshContext();
        await RefreshProviderBadgeAsync();
    }

    private async Task RefreshProviderBadgeAsync()
    {
        var state = await _services.AiProfiles.LoadAsync();
        var active = state.Profiles.FirstOrDefault(p => p.Id == state.ActiveProfileId);
        ProviderBadge.Text = active is null ? "No provider selected" : $"{active.DisplayName} · {active.ModelId} · {(active.IsLocal ? "LOCAL" : "CLOUD")}";
    }

    private void RefreshContext() => ContextList.ItemsSource = _services.AiContext.Assets.ToArray();

    private void ActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActionCombo.SelectedItem is not MagicActionDefinition action) return;
        QuestionBox.Visibility = action.Id == "general.ask" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (ActionCombo.SelectedItem is not MagicActionDefinition action) return;
        try
        {
            var request = new MagicActionExecutionRequest(_asset, action, QuestionBox.Text, _services.AiContext.Assets);
            var state = await _services.AiProfiles.LoadAsync();
            var preview = await _services.MagicActions.PreviewAsync(request);

            if (!preview.ProviderIsLocal && state.Privacy.ShowPayloadSummaryBeforeCloudAction)
            {
                var guardFindings = AiGuard.Scan(string.Join("\n", (_graph?.Nodes ?? Array.Empty<ScreenGraphNode>()).Select(n => n.Text).Where(t => !string.IsNullOrWhiteSpace(t))));
                var risky = guardFindings.Where(f => f.Severity is AiGuardSeverity.Warning or AiGuardSeverity.Critical).Take(8).ToArray();
                var guardText = risky.Length == 0
                    ? "AI Guard: no obvious secret patterns found in extracted text."
                    : "AI Guard found possible sensitive data:\n" + string.Join("\n", risky.Select(f => $"• {f.Kind} ({f.Severity}) — {f.Preview}"));
                var dialog = new ContentDialog
                {
                    XamlRoot = (Content as FrameworkElement)?.XamlRoot,
                    Title = risky.Length == 0 ? "Send this Magic Action to cloud AI?" : "Review data before sending to cloud AI",
                    Content = $"Provider: {preview.ProviderName}\nModel: {preview.ModelId}\nRouting: {preview.RoutingMode}\nImages: {preview.Payload.ImageCount}\nContext items: {preview.Payload.ContextItemCount}\n\n{guardText}\n\nMagic Capture Desktop sends requests directly to the selected provider; it does not proxy them through a Magic Capture server.",
                    PrimaryButtonText = risky.Any(f => f.Severity == AiGuardSeverity.Critical) ? "Send anyway" : "Run",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            }

            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();
            BusyRing.IsActive = true;
            RunButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            StatusBar.IsOpen = false;
            ResultBox.Text = string.Empty;
            EvidenceCanvas.Children.Clear();

            _execution = await _services.MagicActions.ExecuteAsync(request, _runCts.Token);

            ResultBox.Text = _execution.Result.Markdown;
            EvidenceList.ItemsSource = _execution.Evidence.Select(e => new EvidenceDisplay(e)).ToArray();
            PayloadText.Text = $"{_execution.ProviderName} · {_execution.ModelId} · {_execution.Payload.ImageCount} image(s)" + (_execution.FromCache ? " · cached" : string.Empty);
            ProviderBadge.Text = $"{_execution.ProviderName} · {_execution.ModelId} · {(_execution.ProviderIsLocal ? "LOCAL" : "CLOUD")}";
        }
        catch (OperationCanceledException) { Show("Magic Action cancelled.", InfoBarSeverity.Informational); }
        catch (Exception ex) { Show(ex.Message, InfoBarSeverity.Error); _services.Log.Error("MagicAction", ex); }
        finally
        {
            BusyRing.IsActive = false;
            RunButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _runCts?.Cancel();
    private void CopyResult_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrWhiteSpace(ResultBox.Text)) _services.Clipboard.CopyText(ResultBox.Text); }
    private void CopyJson_Click(object sender, RoutedEventArgs e) { if (_execution?.Result.RawJson is { Length: > 0 } json) _services.Clipboard.CopyText(json); }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current as App)?.ShowMainWindow();
        (Application.Current as App)?.MainWindowShowAiSettings();
    }

    private void ClearContext_Click(object sender, RoutedEventArgs e)
    {
        _services.AiContext.Clear();
        RefreshContext();
    }

    private void EvidenceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        EvidenceCanvas.Children.Clear();
        if (EvidenceList.SelectedItem is not EvidenceDisplay display) return;
        DrawEvidence(display.Evidence);
    }

    private void DrawEvidence(ResolvedEvidence evidence)
    {
        if (evidence.CaptureId != _asset.Id)
        {
            Show("This evidence belongs to a supporting Context Stack capture. Open that capture from the Context Stack to inspect its source pixels.", InfoBarSeverity.Informational);
            return;
        }
        if (ImageHost.ActualWidth <= 0 || ImageHost.ActualHeight <= 0) return;
        var scale = Math.Min(ImageHost.ActualWidth / Math.Max(1, _asset.Width), ImageHost.ActualHeight / Math.Max(1, _asset.Height));
        var renderedWidth = _asset.Width * scale;
        var renderedHeight = _asset.Height * scale;
        var offsetX = (ImageHost.ActualWidth - renderedWidth) / 2;
        var offsetY = (ImageHost.ActualHeight - renderedHeight) / 2;
        var r = evidence.Bounds;
        var rect = new Rectangle
        {
            Width = Math.Max(2, r.Width * scale),
            Height = Math.Max(2, r.Height * scale),
            Stroke = new SolidColorBrush(Microsoft.UI.Colors.Gold),
            StrokeThickness = 3,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(36, 255, 215, 0))
        };
        Canvas.SetLeft(rect, offsetX + r.X * scale);
        Canvas.SetTop(rect, offsetY + r.Y * scale);
        EvidenceCanvas.Children.Add(rect);
    }

    private void Show(string message, InfoBarSeverity severity)
    {
        StatusBar.Title = "Magic";
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private sealed record EvidenceDisplay(ResolvedEvidence Evidence)
    {
        public override string ToString() => $"{Evidence.NodeId} · {Evidence.Kind} · {Evidence.Text}";
    }
}
