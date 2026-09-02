using Magic.Capture.Core.Recording;

namespace Magic.Capture.Core.Tests;

public sealed class RecordingEffectsPolicyTests
{
    [Fact]
    public void Normalize_ClampsEffectOptionsAndPreservesOutputFormat()
    {
        var normalized = RecordingRules.Normalize(new RecordingOptions(
            OutputFormat: RecordingOutputFormat.Gif,
            CursorHighlight: true,
            ClickVisualization: true,
            SafeKeyOverlay: true,
            DrawWhileRecording: true,
            LiveZoom: true,
            ZoomPercent: 999));

        Assert.Equal(RecordingOutputFormat.Gif, normalized.OutputFormat);
        Assert.Equal(300, normalized.ZoomPercent);
        Assert.True(RecordingEffectsPolicy.HasAnyEffect(normalized));
    }

    [Theory]
    [InlineData(0x41u, false, false, false, false, null)]
    [InlineData(0x41u, true, false, false, false, "Ctrl+A")]
    [InlineData(0x70u, false, false, false, false, "F1")]
    [InlineData(0x25u, false, false, false, false, "Left")]
    [InlineData(0x5Au, true, true, false, false, "Ctrl+Alt+Z")]
    public void SafeKeyFormatter_DoesNotRetainPlainTyping(uint key, bool ctrl, bool alt, bool shift, bool win, string? expected)
    {
        Assert.Equal(expected, RecordingSafeKeyFormatter.Format(key, ctrl, alt, shift, win));
    }

    [Fact]
    public void RipplePolicy_ExpiresAtBoundedLifetime()
    {
        var start = TimeSpan.FromSeconds(2);
        Assert.True(RecordingEffectsPolicy.TryGetRippleProgress(start + TimeSpan.FromMilliseconds(100), start, out var progress));
        Assert.InRange(progress, 0.0, 1.0);
        Assert.False(RecordingEffectsPolicy.TryGetRippleProgress(start + TimeSpan.FromSeconds(2), start, out _));
    }

    [Fact]
    public void MapDesktopPointToTarget_UsesPhysicalTargetBounds()
    {
        var point = RecordingEffectsPolicy.MapDesktopPointToTarget(120, 230, new RecordingRect(100, 200, 400, 300));
        Assert.Equal(new RecordingPoint(20, 30), point);
    }

    [Fact]
    public void ComputeZoomSourceRect_StaysInsideFrame()
    {
        var rect = RecordingEffectsPolicy.ComputeZoomSourceRect(1920, 1080, new RecordingPoint(1910, 1070), 200);
        Assert.Equal(960, rect.Width);
        Assert.Equal(540, rect.Height);
        Assert.InRange(rect.X, 0, 960);
        Assert.InRange(rect.Y, 0, 540);
        Assert.True(rect.Right <= 1920);
        Assert.True(rect.Bottom <= 1080);
    }

    [Fact]
    public void StrokePolicy_BoundsPointCount()
    {
        var points = Enumerable.Range(0, 10_000).Select(i => new RecordingPoint(i, i)).ToArray();
        var bounded = RecordingEffectsPolicy.BoundStroke(points);
        Assert.True(bounded.Count <= RecordingEffectsPolicy.MaximumStrokePoints);
        Assert.Equal(points[^1], bounded[^1]);
    }
}

public sealed class RecordingAudioOnly46PolicyTests
{
    [Fact]
    public void M4a_IsAudioOnlyAndRequiresAnAudioSource()
    {
        var ok = RecordingRules.Normalize(new RecordingOptions(OutputFormat: RecordingOutputFormat.M4a, IncludeMicrophone: true));
        RecordingOutputPolicy.ValidateCompatibility(ok);
        Assert.True(RecordingOutputPolicy.IsAudioOnly(ok.OutputFormat));
        Assert.Equal(".m4a", RecordingOutputPolicy.Extension(ok.OutputFormat));
        Assert.Throws<InvalidOperationException>(() => RecordingOutputPolicy.ValidateCompatibility(new RecordingOptions(OutputFormat: RecordingOutputFormat.M4a)));
    }

    [Fact]
    public void M4a_RejectsVisualOnlyFeatures()
    {
        var options = new RecordingOptions(OutputFormat: RecordingOutputFormat.M4a, IncludeSystemAudio: true, IncludeWebcam: true);
        Assert.Throws<InvalidOperationException>(() => RecordingOutputPolicy.ValidateCompatibility(options));
    }
}
