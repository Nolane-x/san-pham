using Magic.Capture.Core.VideoEditing;

namespace Magic.Capture.Core.Tests;

public sealed class VideoEditPolicyTests
{
    [Fact]
    public void Trim_KeepsRequestedSourceRange()
    {
        var segment = new VideoEditSegment("a", TimeSpan.Zero, TimeSpan.FromSeconds(10));
        var trimmed = VideoEditRules.Trim(segment, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(2), trimmed.SourceStart);
        Assert.Equal(TimeSpan.FromSeconds(8), trimmed.SourceEnd);
        Assert.Equal(TimeSpan.FromSeconds(6), trimmed.Duration);
    }

    [Fact]
    public void CutOut_MiddleIntervalReturnsTwoSegments()
    {
        var segment = new VideoEditSegment("a", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), Volume: 0.75);
        var result = VideoEditRules.CutOut(segment, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(7));

        Assert.Equal(2, result.Count);
        Assert.Equal((TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(13)), (result[0].SourceStart, result[0].SourceEnd));
        Assert.Equal((TimeSpan.FromSeconds(17), TimeSpan.FromSeconds(20)), (result[1].SourceStart, result[1].SourceEnd));
        Assert.All(result, x => Assert.Equal(0.75, x.Volume));
    }

    [Fact]
    public void CutOut_RejectsEmptyOrReversedRange()
    {
        var segment = new VideoEditSegment("a", TimeSpan.Zero, TimeSpan.FromSeconds(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoEditRules.CutOut(segment, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoEditRules.CutOut(segment, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(2)));
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(5.0, 2.0)]
    public void NormalizeVolume_ClampsToTwoHundredPercent(double value, double expected) =>
        Assert.Equal(expected, VideoEditRules.NormalizeVolume(value));

    [Fact]
    public void NormalizeCrop_StaysInsideUnitCanvas()
    {
        var crop = VideoEditRules.NormalizeCrop(new VideoEditCrop(-0.3, 0.8, 2.0, 0.9));
        Assert.InRange(crop.X, 0, 1);
        Assert.InRange(crop.Y, 0, 1);
        Assert.True(crop.Width > 0 && crop.Height > 0);
        Assert.True(crop.X + crop.Width <= 1.0 + 1e-9);
        Assert.True(crop.Y + crop.Height <= 1.0 + 1e-9);
    }

    [Theory]
    [InlineData(1919, 1918)]
    [InlineData(1920, 1920)]
    [InlineData(1, 2)]
    [InlineData(99999, 16384)]
    public void NormalizeOutputDimension_IsEvenAndBounded(int input, int expected) =>
        Assert.Equal(expected, VideoEditRules.NormalizeOutputDimension(input));

    [Fact]
    public void ValidateProject_RejectsUnknownSourceAndCountOverflow()
    {
        var source = new VideoEditSource("a", @"C:\video.mp4", TimeSpan.FromSeconds(10), 1920, 1080);
        var project = new VideoEditProject(
            Sources: [source],
            Segments: [new VideoEditSegment("missing", TimeSpan.Zero, TimeSpan.FromSeconds(1))],
            OutputWidth: 1920,
            OutputHeight: 1080);

        var errors = VideoEditRules.ValidateProject(project);
        Assert.Contains(errors, x => x.Contains("unknown source", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProjectSchema_FutureVersionIsReadOnly()
    {
        Assert.True(VideoEditProjectSchema.CanWrite(VideoEditProjectSchema.CurrentVersion));
        Assert.False(VideoEditProjectSchema.CanWrite(VideoEditProjectSchema.CurrentVersion + 1));
        Assert.True(VideoEditProjectSchema.CanRead(VideoEditProjectSchema.CurrentVersion + 1));
    }

    [Fact]
    public void ContactSheetPlan_BoundsFrameCountAndPixelBudget()
    {
        var plan = VideoContactSheetPlan.Create(TimeSpan.FromMinutes(2), 1000, 640, 360);
        Assert.Equal(VideoContactSheetPlan.MaximumFrames, plan.FrameCount);
        Assert.True(plan.CanvasWidth > 0 && plan.CanvasHeight > 0);
        Assert.True(plan.RequiredBgraBytes <= VideoContactSheetPlan.MaximumBgraBytes);
        Assert.Equal(plan.FrameCount, plan.Timestamps.Count);
    }

    [Fact]
    public void TimelineDuration_SumsSegmentDurations()
    {
        var segments = new[]
        {
            new VideoEditSegment("a", TimeSpan.Zero, TimeSpan.FromSeconds(3)),
            new VideoEditSegment("b", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5))
        };
        Assert.Equal(TimeSpan.FromSeconds(7), VideoEditRules.TimelineDuration(segments));
    }
}

public sealed class VideoEditAdvancedPolicyTests
{
    [Fact]
    public void ProjectMigration_UpgradesSchemaOneToCurrent()
    {
        var source = new VideoEditSource("a", @"C:\video.mp4", TimeSpan.FromSeconds(10), 1920, 1080);
        var legacy = new VideoEditProject(
            Sources: [source],
            Segments: [new VideoEditSegment("a", TimeSpan.Zero, TimeSpan.FromSeconds(10))],
            OutputWidth: 1920,
            OutputHeight: 1080,
            SchemaVersion: 1);

        var upgraded = VideoEditProjectMigration.UpgradeToCurrent(legacy);

        Assert.Equal(VideoEditProjectSchema.CurrentVersion, upgraded.SchemaVersion);
        Assert.True(VideoEditProjectSchema.CanWrite(upgraded.SchemaVersion));
        Assert.Empty(upgraded.Overlays ?? []);
    }

    [Fact]
    public void TitleCard_DurationContributesToTimeline()
    {
        var title = new VideoEditTitleCard("Chapter one", TimeSpan.FromSeconds(2));
        var segment = new VideoEditSegment(string.Empty, TimeSpan.Zero, TimeSpan.Zero, TitleCard: title);

        Assert.True(segment.IsTitleCard);
        Assert.Equal(TimeSpan.FromSeconds(2), segment.Duration);
        Assert.Equal(TimeSpan.FromSeconds(2), VideoEditRules.TimelineDuration([segment]));
    }

    [Fact]
    public void ValidateProject_RejectsOverlayOutsideTimelineAndTooManyKeyframes()
    {
        var source = new VideoEditSource("a", @"C:\video.mp4", TimeSpan.FromSeconds(10), 1920, 1080);
        var keyframes = Enumerable.Range(0, VideoEditRules.MaximumTrackingKeyframes + 1)
            .Select(i => new VideoEditOverlayKeyframe(TimeSpan.FromMilliseconds(i * 10), new VideoEditCrop(0.1, 0.1, 0.2, 0.2)))
            .ToArray();
        var overlay = new VideoEditOverlay(
            "redact",
            VideoEditOverlayKind.Redaction,
            TimeSpan.FromSeconds(9),
            TimeSpan.FromSeconds(2),
            new VideoEditCrop(0.1, 0.1, 0.2, 0.2),
            Keyframes: keyframes);
        var project = new VideoEditProject(
            [source],
            [new VideoEditSegment("a", TimeSpan.Zero, TimeSpan.FromSeconds(10))],
            1920,
            1080,
            Overlays: [overlay]);

        var errors = VideoEditRules.ValidateProject(project);

        Assert.Contains(errors, x => x.Contains("timeline", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, x => x.Contains("keyframe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TemplateTracker_FollowsSyntheticMovingSquare()
    {
        const int width = 40;
        const int height = 30;
        var previous = new byte[width * height * 4];
        var current = new byte[width * height * 4];
        PaintSquare(previous, width, 10, 8, 8, 8);
        PaintSquare(current, width, 13, 10, 8, 8);

        var start = new VideoEditCrop(10d / width, 8d / height, 8d / width, 8d / height);
        var result = VideoEditTemplateTracker.TrackNext(previous, current, width, height, start, searchRadiusPixels: 6, sampleStep: 1);

        Assert.True(result.IsConfident);
        Assert.InRange(result.Bounds.X, 12.5 / width, 13.5 / width);
        Assert.InRange(result.Bounds.Y, 9.5 / height, 10.5 / height);
    }

    private static void PaintSquare(byte[] bgra, int width, int x, int y, int w, int h)
    {
        for (var py = y; py < y + h; py++)
        for (var px = x; px < x + w; px++)
        {
            var offset = (py * width + px) * 4;
            bgra[offset] = 255;
            bgra[offset + 1] = 255;
            bgra[offset + 2] = 255;
            bgra[offset + 3] = 255;
        }
    }
}

public sealed class VideoEdit46PolicyTests
{
    [Fact]
    public void PlaybackRate_ChangesRenderedDurationWithoutChangingSourceDuration()
    {
        var segment = new VideoEditSegment("a", TimeSpan.Zero, TimeSpan.FromSeconds(10), PlaybackRate: 2.0);
        Assert.Equal(TimeSpan.FromSeconds(10), segment.Duration);
        Assert.Equal(TimeSpan.FromSeconds(5), segment.RenderedDuration);
    }

    [Fact]
    public void TimelineMap_MapsOutputTimeBackIntoSourceTimeline()
    {
        var segments = new[]
        {
            new VideoEditSegment("a", TimeSpan.Zero, TimeSpan.FromSeconds(8), PlaybackRate: 2.0),
            new VideoEditSegment("b", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6), PlaybackRate: 0.5)
        };
        var mapped = VideoEditTimelineMap.MapOutputToBaseTimeline(segments, TimeSpan.FromSeconds(6));
        Assert.Equal(1, mapped.SegmentIndex);
        Assert.Equal(TimeSpan.FromSeconds(1), mapped.OffsetInSegment);
        Assert.Equal(TimeSpan.FromSeconds(9), mapped.BaseTimelinePosition);
    }

    [Fact]
    public void Keyframes_InterpolateZoomAndPanLinearly()
    {
        var effect = new VideoEditFrameEffect(
            "zoom", VideoEditFrameEffectKind.ZoomPan, TimeSpan.Zero, TimeSpan.FromSeconds(10),
            [
                new VideoEditFrameKeyframe(TimeSpan.Zero, 1.0, 0.5, 0.5),
                new VideoEditFrameKeyframe(TimeSpan.FromSeconds(10), 2.0, 0.75, 0.25)
            ]);
        var value = VideoEditFrameEffectPolicy.Evaluate(effect, TimeSpan.FromSeconds(5));
        Assert.Equal(1.5, value.Primary, 6);
        Assert.Equal(0.625, value.X, 6);
        Assert.Equal(0.375, value.Y, 6);
    }

    [Fact]
    public void PixelEffects_BlurPixelateAndZoomStayBounded()
    {
        var pixels = new byte[4 * 4 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)i; pixels[i + 1] = 100; pixels[i + 2] = 200; pixels[i + 3] = 255; }
        VideoEditBgraEffects.ApplyPixelateInPlace(pixels, 4, 4, 2);
        VideoEditBgraEffects.ApplyGaussianBlurInPlace(pixels, 4, 4, 2);
        VideoEditBgraEffects.ApplyZoomPanInPlace(pixels, 4, 4, 2.0, 0.5, 0.5);
        Assert.All(Enumerable.Range(0, 16), i => Assert.Equal(255, pixels[i * 4 + 3]));
    }

    [Fact]
    public void ProjectMigration_UpgradesSchemaTwoToCurrent()
    {
        var source = new VideoEditSource("a", @"C:\video.mp4", TimeSpan.FromSeconds(5), 1280, 720);
        var project = new VideoEditProject([source], [new VideoEditSegment("a", TimeSpan.Zero, TimeSpan.FromSeconds(5))], 1280, 720, SchemaVersion: 2);
        var upgraded = VideoEditProjectMigration.UpgradeToCurrent(project);
        Assert.Equal(VideoEditProjectSchema.CurrentVersion, upgraded.SchemaVersion);
        Assert.Equal(30, upgraded.OutputFramesPerSecond);
        Assert.Empty(upgraded.FrameEffects ?? []);
    }
}

public sealed class VideoEdit47PolicyTests
{
    [Theory]
    [InlineData(VideoEditEasingKind.Linear, 0.25, 0.25)]
    [InlineData(VideoEditEasingKind.EaseIn, 0.5, 0.25)]
    [InlineData(VideoEditEasingKind.EaseOut, 0.5, 0.75)]
    [InlineData(VideoEditEasingKind.EaseInOut, 0.25, 0.125)]
    [InlineData(VideoEditEasingKind.Hold, 0.75, 0.0)]
    public void Easing_IsDeterministicAndBounded(VideoEditEasingKind kind, double input, double expected)
    {
        Assert.Equal(expected, VideoEditEasing.Apply(kind, input), 6);
        Assert.InRange(VideoEditEasing.Apply(kind, input), 0.0, 1.0);
    }

    [Fact]
    public void OverlayAnimation_InterpolatesBoundsAndOpacityUsingEasing()
    {
        var overlay = new VideoEditOverlay(
            "o", VideoEditOverlayKind.Rectangle, TimeSpan.Zero, TimeSpan.FromSeconds(2),
            new VideoEditCrop(0.1, 0.1, 0.2, 0.2),
            Opacity: 0.4,
            Keyframes:
            [
                new VideoEditOverlayKeyframe(TimeSpan.Zero, new VideoEditCrop(0.1, 0.1, 0.2, 0.2), 0.4, VideoEditEasingKind.EaseIn),
                new VideoEditOverlayKeyframe(TimeSpan.FromSeconds(2), new VideoEditCrop(0.5, 0.5, 0.4, 0.4), 1.0)
            ]);

        var value = VideoEditOverlayAnimationPolicy.Evaluate(overlay, TimeSpan.FromSeconds(1));
        Assert.Equal(0.2, value.Bounds.X, 6);
        Assert.Equal(0.2, value.Bounds.Y, 6);
        Assert.Equal(0.25, value.Bounds.Width, 6);
        Assert.Equal(0.55, value.Opacity, 6);
    }

    [Fact]
    public void AudioEnvelope_FadeAndDuckRemainWithinTwoHundredPercent()
    {
        var envelope = VideoEditAudioEnvelope.CreateFadeAndDuck(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            duckStart: TimeSpan.FromSeconds(3),
            duckEnd: TimeSpan.FromSeconds(7),
            duckGain: 0.25);

        Assert.Equal(0.0, VideoEditAudioEnvelopePolicy.Evaluate(envelope, TimeSpan.Zero), 6);
        Assert.Equal(1.0, VideoEditAudioEnvelopePolicy.Evaluate(envelope, TimeSpan.FromSeconds(2)), 6);
        Assert.Equal(0.25, VideoEditAudioEnvelopePolicy.Evaluate(envelope, TimeSpan.FromSeconds(5)), 6);
        Assert.Equal(0.0, VideoEditAudioEnvelopePolicy.Evaluate(envelope, TimeSpan.FromSeconds(10)), 6);
    }

    [Fact]
    public void TextStyle_NormalizesFontAndDecorationFields()
    {
        var style = VideoEditTextStyle.Normalize(new VideoEditTextStyle(
            FontFamily: "  Segoe UI  ",
            Weight: 9999,
            Italic: true,
            Underline: true,
            HorizontalAlignment: VideoEditTextAlignment.Right,
            ShadowArgb: 0xAA000000u,
            ShadowOffset: 99,
            OutlineWidth: 99));

        Assert.Equal("Segoe UI", style.FontFamily);
        Assert.Equal(900, style.Weight);
        Assert.True(style.Italic);
        Assert.True(style.Underline);
        Assert.Equal(VideoEditTextAlignment.Right, style.HorizontalAlignment);
        Assert.Equal(VideoEditTextStyle.MaximumShadowOffset, style.ShadowOffset);
        Assert.Equal(VideoEditTextStyle.MaximumOutlineWidth, style.OutlineWidth);
    }

    [Fact]
    public void FrameEffect_EasingControlsInterpolation()
    {
        var effect = new VideoEditFrameEffect(
            "zoom", VideoEditFrameEffectKind.ZoomPan, TimeSpan.Zero, TimeSpan.FromSeconds(2),
            [
                new VideoEditFrameKeyframe(TimeSpan.Zero, 1.0, 0.5, 0.5, VideoEditEasingKind.Hold),
                new VideoEditFrameKeyframe(TimeSpan.FromSeconds(2), 3.0, 0.8, 0.2)
            ]);

        var value = VideoEditFrameEffectPolicy.Evaluate(effect, TimeSpan.FromSeconds(1));
        Assert.Equal(1.0, value.Primary, 6);
        Assert.Equal(0.5, value.X, 6);
        Assert.Equal(0.5, value.Y, 6);
    }

    [Fact]
    public void ProjectMigration_UpgradesSchemaThreeToFour()
    {
        var source = new VideoEditSource("a", @"C:\video.mp4", TimeSpan.FromSeconds(5), 1280, 720);
        var legacy = new VideoEditProject(
            [source],
            [new VideoEditSegment("a", TimeSpan.Zero, TimeSpan.FromSeconds(5))],
            1280,
            720,
            SchemaVersion: 3);

        var upgraded = VideoEditProjectMigration.UpgradeToCurrent(legacy);
        Assert.Equal(4, upgraded.SchemaVersion);
        Assert.Empty(upgraded.Segments[0].AudioEnvelope?.Keyframes ?? []);
    }
}

public sealed class VideoEdit47TimelineRegressionTests
{
    [Fact]
    public void TimelineMap_ExposesOutputLocalOffsetForEnvelopeAtTwoTimesSpeed()
    {
        var segment = new VideoEditSegment("a", TimeSpan.Zero, TimeSpan.FromSeconds(10), PlaybackRate: 2.0);
        var mapped = VideoEditTimelineMap.MapOutputToBaseTimeline([segment], TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(4), mapped.OffsetInSegment);
        Assert.Equal(TimeSpan.FromSeconds(2), mapped.OutputOffsetInSegment);
    }

    [Fact]
    public void AudioEnvelope_RequiresAdvancedRenderEvenAtNormalPlaybackRate()
    {
        var source = new VideoEditSource("a", @"C:\video.mp4", TimeSpan.FromSeconds(4), 1280, 720);
        var envelope = new VideoEditAudioEnvelope([
            new VideoEditAudioKeyframe(TimeSpan.Zero, 0.0),
            new VideoEditAudioKeyframe(TimeSpan.FromSeconds(1), 1.0)
        ]);
        var project = new VideoEditProject(
            [source],
            [new VideoEditSegment("a", TimeSpan.Zero, TimeSpan.FromSeconds(4), AudioEnvelope: envelope)],
            1280, 720);
        Assert.True(VideoEditFrameEffectPolicy.RequiresAdvancedRender(project));
    }

    [Fact]
    public void AnimatedOverlayPieces_AreBounded()
    {
        var overlay = new VideoEditOverlay(
            "o", VideoEditOverlayKind.Text, TimeSpan.Zero, TimeSpan.FromMinutes(10), new VideoEditCrop(0, 0, 0.3, 0.2),
            Text: "Hello",
            Keyframes: [
                new VideoEditOverlayKeyframe(TimeSpan.Zero, new VideoEditCrop(0, 0, 0.3, 0.2)),
                new VideoEditOverlayKeyframe(TimeSpan.FromMinutes(10), new VideoEditCrop(0.7, 0.8, 0.3, 0.2))
            ]);
        var pieces = VideoEditOverlayAnimationPolicy.BuildPieces(overlay, 60, 200);
        Assert.Equal(200, pieces.Count);
    }
}
