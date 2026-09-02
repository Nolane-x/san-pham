namespace Magic.Capture.Core.VideoEditing;

public readonly record struct VideoEditTimelinePosition(
    int SegmentIndex,
    TimeSpan OffsetInSegment,
    TimeSpan BaseTimelinePosition,
    TimeSpan OutputTimelinePosition,
    TimeSpan OutputOffsetInSegment);

public static class VideoEditTimelineMap
{
    public static VideoEditTimelinePosition MapOutputToBaseTimeline(IReadOnlyList<VideoEditSegment> segments, TimeSpan outputPosition)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0) throw new ArgumentException("Timeline has no segments.", nameof(segments));
        var targetTicks = Math.Max(0L, outputPosition.Ticks);
        long outputCursor = 0;
        long baseCursor = 0;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var baseTicks = Math.Max(0L, segment.Duration.Ticks);
            var renderedTicks = Math.Max(1L, segment.RenderedDuration.Ticks);
            var outputEnd = checked(outputCursor + renderedTicks);
            if (targetTicks < outputEnd || i == segments.Count - 1)
            {
                var outputOffset = Math.Clamp(targetTicks - outputCursor, 0L, Math.Max(0L, renderedTicks - 1L));
                var rate = VideoEditFrameEffectPolicy.NormalizePlaybackRate(segment.PlaybackRate);
                var baseOffset = Math.Min(Math.Max(0L, baseTicks - 1L), checked((long)Math.Round(outputOffset * rate)));
                return new VideoEditTimelinePosition(
                    i,
                    TimeSpan.FromTicks(baseOffset),
                    TimeSpan.FromTicks(checked(baseCursor + baseOffset)),
                    TimeSpan.FromTicks(targetTicks),
                    TimeSpan.FromTicks(outputOffset));
            }
            outputCursor = outputEnd;
            baseCursor = checked(baseCursor + baseTicks);
        }

        throw new InvalidOperationException("Timeline mapping failed.");
    }
}
