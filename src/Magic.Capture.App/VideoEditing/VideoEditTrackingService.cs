using Magic.Capture.Core.VideoEditing;

namespace Magic.Capture.App.VideoEditing;

internal sealed record VideoEditTrackingSummary(
    int KeyframeCount,
    TimeSpan CoveredDuration,
    bool LostTarget,
    double LastMeanAbsoluteError);

internal sealed record VideoEditTrackingUpdate(
    VideoEditProject Project,
    VideoEditTrackingSummary Summary);

internal sealed class VideoEditTrackingService
{
    private static readonly TimeSpan MinimumSampleInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaximumSampleInterval = TimeSpan.FromSeconds(2);
    private const int MaximumTrackingWidth = 960;

    private readonly VideoEditCompositionService _composition;
    private readonly VideoEditThumbnailService _thumbnails;

    public VideoEditTrackingService(VideoEditCompositionService composition, VideoEditThumbnailService thumbnails)
    {
        _composition = composition;
        _thumbnails = thumbnails;
    }

    public async Task<VideoEditTrackingUpdate> TrackRedactionAsync(
        VideoEditProject project,
        string overlayId,
        TimeSpan sampleInterval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(overlayId);
        if (!VideoEditProjectSchema.CanWrite(project.SchemaVersion))
            throw new InvalidOperationException("Future-schema clip projects are read-only and cannot be tracked.");

        var overlays = project.OverlayItems.ToArray();
        var index = Array.FindIndex(overlays, x => string.Equals(x.Id, overlayId, StringComparison.Ordinal));
        if (index < 0) throw new InvalidOperationException("The selected overlay no longer exists.");
        var overlay = overlays[index];
        if (overlay.Kind != VideoEditOverlayKind.Redaction)
            throw new InvalidOperationException("Automatic tracking is available only for redaction overlays.");
        if (overlay.Duration <= TimeSpan.Zero) throw new InvalidDataException("Redaction duration must be positive.");

        var requestedInterval = sampleInterval < MinimumSampleInterval ? MinimumSampleInterval :
            sampleInterval > MaximumSampleInterval ? MaximumSampleInterval : sampleInterval;
        var trackingDuration = overlay.Duration > VideoEditRules.MaximumTrackingDuration
            ? VideoEditRules.MaximumTrackingDuration
            : overlay.Duration;
        var interval = EnsureBoundedSampleCount(trackingDuration, requestedInterval);
        var dimensions = ChooseTrackingDimensions(project.OutputWidth, project.OutputHeight);
        var composition = await _composition.BuildCompositionAsync(project, cancellationToken, includeOverlays: false);

        var keyframes = new List<VideoEditOverlayKeyframe>(VideoEditRules.MaximumTrackingKeyframes)
        {
            new(TimeSpan.Zero, overlay.Bounds)
        };
        var currentBounds = overlay.Bounds;
        var previous = await _thumbnails.SampleFrameBgraAsync(composition, overlay.Start, dimensions.Width, dimensions.Height, cancellationToken);
        var lost = false;
        var lastError = 0.0;
        var covered = TimeSpan.Zero;

        for (var offset = interval; offset <= trackingDuration && keyframes.Count < VideoEditRules.MaximumTrackingKeyframes; offset += interval)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await _thumbnails.SampleFrameBgraAsync(composition, overlay.Start + offset, dimensions.Width, dimensions.Height, cancellationToken);
            if (current.Width != previous.Width || current.Height != previous.Height)
                throw new InvalidDataException("Tracking thumbnail dimensions changed during sampling.");

            var searchRadius = Math.Clamp((int)Math.Round(Math.Max(current.Width, current.Height) * 0.05), 8, 64);
            var result = VideoEditTemplateTracker.TrackNext(
                previous.Bytes,
                current.Bytes,
                current.Width,
                current.Height,
                currentBounds,
                searchRadiusPixels: searchRadius,
                sampleStep: 3);
            lastError = result.MeanAbsoluteError;
            if (!result.IsConfident)
            {
                lost = true;
                break;
            }

            currentBounds = result.Bounds;
            keyframes.Add(new VideoEditOverlayKeyframe(offset, currentBounds));
            covered = offset;
            previous = current;
        }

        if (keyframes.Count < 2)
            throw new InvalidOperationException("The redaction tracker could not confidently follow the selected region beyond the first sample.");

        overlays[index] = overlay with { Keyframes = keyframes.ToArray() };
        var updated = project with { Overlays = overlays };
        var errors = VideoEditRules.ValidateProject(updated);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
        return new VideoEditTrackingUpdate(updated, new VideoEditTrackingSummary(keyframes.Count, covered, lost, lastError));
    }

    private static TimeSpan EnsureBoundedSampleCount(TimeSpan duration, TimeSpan interval)
    {
        if (duration <= TimeSpan.Zero) return interval;
        var maximumIntervals = VideoEditRules.MaximumTrackingKeyframes - 1L;
        var requestedCount = checked(duration.Ticks / Math.Max(1L, interval.Ticks) + 1L);
        if (requestedCount <= VideoEditRules.MaximumTrackingKeyframes) return interval;
        var ticks = checked((duration.Ticks + maximumIntervals - 1L) / maximumIntervals);
        return TimeSpan.FromTicks(Math.Max(MinimumSampleInterval.Ticks, ticks));
    }

    private static (int Width, int Height) ChooseTrackingDimensions(int outputWidth, int outputHeight)
    {
        var maxSide = Math.Max(outputWidth, outputHeight);
        var scale = maxSide <= MaximumTrackingWidth ? 1.0 : (double)MaximumTrackingWidth / maxSide;
        var width = Math.Clamp((int)Math.Round(outputWidth * scale), 64, MaximumTrackingWidth);
        var height = Math.Clamp((int)Math.Round(outputHeight * scale), 36, MaximumTrackingWidth);
        return (width, height);
    }
}
