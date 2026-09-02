using Magic.Capture.App.Imaging;
using Magic.Capture.App.Platform;
using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Capture;

internal sealed record AutomaticScrollCaptureOptions(
    int MaxFrames = 32,
    int SettleMilliseconds = 180,
    int WheelDelta = -720,
    double EndChangedPixelPercent = 1.0,
    int EndStableSamples = 2,
    int SampleEveryPixels = 8,
    int DynamicProbeMilliseconds = 70,
    double DynamicChangedPixelPercent = 4.0,
    int DynamicSettleRetries = 1,
    int AlignmentRetries = 2,
    int MinimumStickyBandRows = 12,
    double MaximumStickyBandRatio = 0.22,
    ScrollAxis Axis = ScrollAxis.Vertical,
    int HorizontalWheelDelta = 720);

internal sealed record AutomaticScrollCaptureProgress(int FrameCount, double LastChangedPixelPercent, string Phase);
internal sealed record AutomaticScrollCaptureResult(
    byte[] PngBytes,
    int FrameCount,
    bool EndDetected,
    IReadOnlyList<StitchPairResult> Pairs,
    bool DynamicContentDetected = false,
    int AlignmentRetries = 0,
    int StickyTopRowsRemoved = 0,
    int StickyBottomRowsRemoved = 0,
    ScrollAxis Axis = ScrollAxis.Vertical,
    IReadOnlyList<HorizontalStitchPairResult>? HorizontalPairs = null);

internal sealed class AutomaticScrollCaptureService
{
    private readonly ScreenCaptureService _screen;
    private readonly VerticalImageStitcher _stitcher;
    private readonly HorizontalImageStitcher _horizontalStitcher;
    private readonly InputSynthesisService _input;

    public AutomaticScrollCaptureService(ScreenCaptureService screen, VerticalImageStitcher stitcher, HorizontalImageStitcher horizontalStitcher, InputSynthesisService input)
    {
        _screen = screen;
        _stitcher = stitcher;
        _horizontalStitcher = horizontalStitcher;
        _input = input;
    }

    public Task<AutomaticScrollCaptureResult> CaptureAsync(
        PixelRect bounds,
        AutomaticScrollCaptureOptions? options = null,
        IProgress<AutomaticScrollCaptureProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AutomaticScrollCaptureOptions();
        return options.Axis == ScrollAxis.Horizontal
            ? CaptureHorizontalAsync(bounds, options, progress, cancellationToken)
            : CaptureVerticalAsync(bounds, options, progress, cancellationToken);
    }

    private async Task<AutomaticScrollCaptureResult> CaptureVerticalAsync(
        PixelRect bounds,
        AutomaticScrollCaptureOptions? options = null,
        IProgress<AutomaticScrollCaptureProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (bounds.IsEmpty) throw new ArgumentException("Scrolling capture region must not be empty.", nameof(bounds));
        options ??= new AutomaticScrollCaptureOptions();
        var maxFrames = Math.Clamp(options.MaxFrames, 2, 64);
        var settle = Math.Clamp(options.SettleMilliseconds, 50, 2_000);
        var stableTarget = Math.Clamp(options.EndStableSamples, 1, 5);
        var endChangedPixelPercent = Math.Clamp(options.EndChangedPixelPercent, 0, 100);
        var sampleEvery = Math.Clamp(options.SampleEveryPixels, 1, 256);
        var dynamicProbe = Math.Clamp(options.DynamicProbeMilliseconds, 0, 500);
        var dynamicThreshold = Math.Clamp(options.DynamicChangedPixelPercent, 0.1, 50);
        var dynamicRetries = Math.Clamp(options.DynamicSettleRetries, 0, 3);
        var alignmentRetryLimit = Math.Clamp(options.AlignmentRetries, 0, 3);
        var wheelDelta = Math.Clamp(options.WheelDelta, -NativeConstants.WheelDelta * 20, NativeConstants.WheelDelta * 20);
        if (wheelDelta == 0) wheelDelta = -NativeConstants.WheelDelta * 6;

        var frames = new List<byte[]>(Math.Min(maxFrames, 16));
        var trims = new List<StitchFrameTrim>(Math.Min(maxFrames, 16));
        var originalCursor = _input.GetCursorPosition();
        var endDetected = false;
        var dynamicContentDetected = false;
        var alignmentRetriesUsed = 0;

        var stickyOptions = new StableEdgeBandOptions(
            MaximumBandRatio: Math.Clamp(options.MaximumStickyBandRatio, 0.05, 0.40),
            MinimumBandRows: Math.Clamp(options.MinimumStickyBandRows, 2, Math.Max(2, bounds.Height / 3)),
            MaximumRowChangedPercent: 4,
            MinimumGlobalChangedPercent: 8,
            SampleEveryColumns: Math.Clamp(sampleEvery, 1, 32),
            ChannelThreshold: 8);

        try
        {
            _input.SetCursorPosition(bounds.Center);
            await Task.Delay(120, cancellationToken);
            var first = _screen.Capture(bounds, CaptureSourceKind.Region, "Automatic Scrolling Capture", includeCursor: false);
            frames.Add(first.PngBytes);
            trims.Add(new StitchFrameTrim());
            var acceptedPixels = ReadPixels(first.PngBytes);
            var lastObservedPixels = acceptedPixels;
            progress?.Report(new AutomaticScrollCaptureProgress(1, 100, "Capturing"));
            var stableSamples = 0;

            for (var frameIndex = 1; frameIndex < maxFrames; frameIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _input.ScrollVertical(wheelDelta);
                await Task.Delay(settle, cancellationToken);

                var settled = await CaptureSettledFrameAsync(
                    bounds, dynamicProbe, dynamicThreshold, dynamicRetries, sampleEvery,
                    progress, frames.Count, cancellationToken);
                dynamicContentDetected |= settled.DynamicDetected;
                var capture = settled.Asset;
                var currentPixels = settled.Pixels;

                var changed = FrameDifference.SampledChangedPercent(lastObservedPixels, currentPixels, sampleEvery, 8);
                if (changed <= endChangedPixelPercent)
                {
                    lastObservedPixels = currentPixels;
                    stableSamples++;
                    progress?.Report(new AutomaticScrollCaptureProgress(frames.Count, changed, "Checking end"));
                    if (stableSamples >= stableTarget)
                    {
                        endDetected = true;
                        break;
                    }
                    continue;
                }

                stableSamples = 0;
                var pair = AnalyzePair(acceptedPixels, currentPixels, bounds.Width, bounds.Height, trims[^1], stickyOptions, frames[^1], capture.PngBytes);

                var becameNearDuplicate = false;
                if (pair.Match is null)
                {
                    for (var attempt = 0; attempt < alignmentRetryLimit && pair.Match is null; attempt++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var divisor = 1 << (attempt + 1);
                        var correctionDelta = -(wheelDelta / divisor);
                        if (correctionDelta == 0) break;

                        alignmentRetriesUsed++;
                        progress?.Report(new AutomaticScrollCaptureProgress(frames.Count, changed, $"Alignment retry {attempt + 1}"));
                        _input.ScrollVertical(correctionDelta);
                        await Task.Delay(settle, cancellationToken);
                        settled = await CaptureSettledFrameAsync(
                            bounds, dynamicProbe, dynamicThreshold, dynamicRetries, sampleEvery,
                            progress, frames.Count, cancellationToken);
                        dynamicContentDetected |= settled.DynamicDetected;
                        capture = settled.Asset;
                        currentPixels = settled.Pixels;

                        var changedFromAccepted = FrameDifference.SampledChangedPercent(acceptedPixels, currentPixels, sampleEvery, 8);
                        if (changedFromAccepted <= endChangedPixelPercent)
                        {
                            becameNearDuplicate = true;
                            break;
                        }
                        pair = AnalyzePair(acceptedPixels, currentPixels, bounds.Width, bounds.Height, trims[^1], stickyOptions, frames[^1], capture.PngBytes);
                    }
                }

                if (becameNearDuplicate)
                {
                    lastObservedPixels = currentPixels;
                    progress?.Report(new AutomaticScrollCaptureProgress(frames.Count, 0, "Alignment corrected"));
                    continue;
                }
                if (pair.Match is null)
                {
                    var dynamicHint = dynamicContentDetected ? " Dynamic or animated content was detected; pause it and retry." : string.Empty;
                    throw new InvalidOperationException($"Automatic scrolling could not align the next frame after {alignmentRetryLimit} bounded correction attempt(s). Try a smaller region or scroll step.{dynamicHint}");
                }

                // Sticky chrome is kept only at the outside edges of the final image: the first frame
                // keeps its header, the final frame keeps its footer, and repeated bands are trimmed.
                trims[^1] = pair.UpperTrim;
                trims.Add(pair.LowerTrim);
                frames.Add(capture.PngBytes);
                acceptedPixels = currentPixels;
                lastObservedPixels = currentPixels;
                progress?.Report(new AutomaticScrollCaptureProgress(frames.Count, changed, pair.HasStickyBands ? "Capturing · sticky chrome removed" : "Capturing"));
            }

            if (frames.Count == 1)
                return new AutomaticScrollCaptureResult(frames[0], 1, endDetected, [], dynamicContentDetected, alignmentRetriesUsed);

            progress?.Report(new AutomaticScrollCaptureProgress(frames.Count, 0, "Stitching"));
            var stitched = await Task.Run(() => _stitcher.Stitch(frames, trims: trims), cancellationToken);
            var stickyTopRows = trims.Skip(1).Sum(trim => trim.TopRows);
            var stickyBottomRows = trims.Take(Math.Max(0, trims.Count - 1)).Sum(trim => trim.BottomRows);
            return new AutomaticScrollCaptureResult(
                stitched.PngBytes,
                frames.Count,
                endDetected,
                stitched.Pairs,
                dynamicContentDetected,
                alignmentRetriesUsed,
                stickyTopRows,
                stickyBottomRows);
        }
        finally
        {
            try { _input.SetCursorPosition(new PixelPoint(originalCursor.X, originalCursor.Y)); }
            catch (System.ComponentModel.Win32Exception) { /* Cursor restoration is best-effort and must not hide the capture result. */ }
        }
    }

    private async Task<AutomaticScrollCaptureResult> CaptureHorizontalAsync(
        PixelRect bounds,
        AutomaticScrollCaptureOptions options,
        IProgress<AutomaticScrollCaptureProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (bounds.IsEmpty) throw new ArgumentException("Scrolling capture region must not be empty.", nameof(bounds));
        var maxFrames = Math.Clamp(options.MaxFrames, 2, 64);
        var settle = Math.Clamp(options.SettleMilliseconds, 50, 2_000);
        var stableTarget = Math.Clamp(options.EndStableSamples, 1, 5);
        var endChangedPixelPercent = Math.Clamp(options.EndChangedPixelPercent, 0, 100);
        var sampleEvery = Math.Clamp(options.SampleEveryPixels, 1, 256);
        var dynamicProbe = Math.Clamp(options.DynamicProbeMilliseconds, 0, 500);
        var dynamicThreshold = Math.Clamp(options.DynamicChangedPixelPercent, 0.1, 50);
        var dynamicRetries = Math.Clamp(options.DynamicSettleRetries, 0, 3);
        var alignmentRetryLimit = Math.Clamp(options.AlignmentRetries, 0, 3);
        var wheelDelta = Math.Clamp(options.HorizontalWheelDelta, -NativeConstants.WheelDelta * 20, NativeConstants.WheelDelta * 20);
        if (wheelDelta == 0) wheelDelta = NativeConstants.WheelDelta * 6;

        var frames = new List<byte[]>(Math.Min(maxFrames, 16));
        var originalCursor = _input.GetCursorPosition();
        var endDetected = false;
        var dynamicContentDetected = false;
        var alignmentRetriesUsed = 0;
        try
        {
            _input.SetCursorPosition(bounds.Center);
            await Task.Delay(120, cancellationToken);
            var first = _screen.Capture(bounds, CaptureSourceKind.Region, "Automatic Horizontal Scrolling Capture", includeCursor: false);
            frames.Add(first.PngBytes);
            var acceptedPixels = ReadPixels(first.PngBytes);
            var lastObservedPixels = acceptedPixels;
            var stableSamples = 0;
            progress?.Report(new AutomaticScrollCaptureProgress(1, 100, "Capturing horizontal"));

            for (var frameIndex = 1; frameIndex < maxFrames; frameIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _input.ScrollHorizontal(wheelDelta);
                await Task.Delay(settle, cancellationToken);
                var settled = await CaptureSettledFrameAsync(bounds, dynamicProbe, dynamicThreshold, dynamicRetries, sampleEvery, progress, frames.Count, cancellationToken);
                dynamicContentDetected |= settled.DynamicDetected;
                var capture = settled.Asset;
                var currentPixels = settled.Pixels;
                var changed = FrameDifference.SampledChangedPercent(lastObservedPixels, currentPixels, sampleEvery, 8);
                if (changed <= endChangedPixelPercent)
                {
                    lastObservedPixels = currentPixels;
                    stableSamples++;
                    progress?.Report(new AutomaticScrollCaptureProgress(frames.Count, changed, "Checking horizontal end"));
                    if (stableSamples >= stableTarget) { endDetected = true; break; }
                    continue;
                }

                stableSamples = 0;
                var match = _horizontalStitcher.FindPairOverlap(frames[^1], capture.PngBytes);
                var becameNearDuplicate = false;
                for (var attempt = 0; attempt < alignmentRetryLimit && match is null; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var divisor = 1 << (attempt + 1);
                    var correctionDelta = -(wheelDelta / divisor);
                    if (correctionDelta == 0) break;
                    alignmentRetriesUsed++;
                    progress?.Report(new AutomaticScrollCaptureProgress(frames.Count, changed, $"Horizontal alignment retry {attempt + 1}"));
                    _input.ScrollHorizontal(correctionDelta);
                    await Task.Delay(settle, cancellationToken);
                    settled = await CaptureSettledFrameAsync(bounds, dynamicProbe, dynamicThreshold, dynamicRetries, sampleEvery, progress, frames.Count, cancellationToken);
                    dynamicContentDetected |= settled.DynamicDetected;
                    capture = settled.Asset;
                    currentPixels = settled.Pixels;
                    if (FrameDifference.SampledChangedPercent(acceptedPixels, currentPixels, sampleEvery, 8) <= endChangedPixelPercent)
                    {
                        becameNearDuplicate = true;
                        break;
                    }
                    match = _horizontalStitcher.FindPairOverlap(frames[^1], capture.PngBytes);
                }

                if (becameNearDuplicate)
                {
                    lastObservedPixels = currentPixels;
                    progress?.Report(new AutomaticScrollCaptureProgress(frames.Count, 0, "Horizontal alignment corrected"));
                    continue;
                }
                if (match is null)
                    throw new InvalidOperationException($"Automatic horizontal scrolling could not align the next frame after {alignmentRetryLimit} bounded correction attempt(s). Try a smaller region or horizontal scroll step.");

                frames.Add(capture.PngBytes);
                acceptedPixels = currentPixels;
                lastObservedPixels = currentPixels;
                progress?.Report(new AutomaticScrollCaptureProgress(frames.Count, changed, "Capturing horizontal"));
            }

            if (frames.Count == 1)
                return new AutomaticScrollCaptureResult(frames[0], 1, endDetected, [], dynamicContentDetected, alignmentRetriesUsed, Axis: ScrollAxis.Horizontal);

            progress?.Report(new AutomaticScrollCaptureProgress(frames.Count, 0, "Stitching horizontal"));
            var stitched = await Task.Run(() => _horizontalStitcher.Stitch(frames), cancellationToken);
            return new AutomaticScrollCaptureResult(stitched.PngBytes, frames.Count, endDetected, [], dynamicContentDetected, alignmentRetriesUsed, Axis: ScrollAxis.Horizontal, HorizontalPairs: stitched.Pairs);
        }
        finally
        {
            try { _input.SetCursorPosition(new PixelPoint(originalCursor.X, originalCursor.Y)); }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }

    private PairAnalysis AnalyzePair(
        byte[] acceptedPixels,
        byte[] currentPixels,
        int width,
        int height,
        StitchFrameTrim currentUpperTrim,
        StableEdgeBandOptions stickyOptions,
        byte[] upperPng,
        byte[] lowerPng)
    {
        var bands = StableEdgeBandDetector.Detect(acceptedPixels, currentPixels, width, height, stickyOptions);
        var upperTrim = currentUpperTrim with { BottomRows = bands.BottomRows };
        var lowerTrim = new StitchFrameTrim(bands.TopRows, 0);
        var match = _stitcher.FindPairOverlap(upperPng, lowerPng, upperTrim, lowerTrim);

        if (match is null && (bands.TopRows > 0 || bands.BottomRows > 0))
        {
            // A conservative fallback prevents a false sticky-band heuristic from breaking a capture.
            upperTrim = currentUpperTrim with { BottomRows = 0 };
            lowerTrim = new StitchFrameTrim();
            match = _stitcher.FindPairOverlap(upperPng, lowerPng, upperTrim, lowerTrim);
        }
        return new PairAnalysis(match, upperTrim, lowerTrim, bands.TopRows > 0 || bands.BottomRows > 0);
    }

    private async Task<SettledFrame> CaptureSettledFrameAsync(
        PixelRect bounds,
        int probeMilliseconds,
        double dynamicThreshold,
        int retryLimit,
        int sampleEvery,
        IProgress<AutomaticScrollCaptureProgress>? progress,
        int frameCount,
        CancellationToken cancellationToken)
    {
        var asset = _screen.Capture(bounds, CaptureSourceKind.Region, "Automatic Scrolling Capture", includeCursor: false);
        var pixels = ReadPixels(asset.PngBytes);
        if (probeMilliseconds <= 0) return new SettledFrame(asset, pixels, false);

        var dynamicDetected = false;
        for (var attempt = 0; attempt <= retryLimit; attempt++)
        {
            await Task.Delay(probeMilliseconds, cancellationToken);
            var probe = _screen.Capture(bounds, CaptureSourceKind.Region, "Automatic Scrolling Capture", includeCursor: false);
            var probePixels = ReadPixels(probe.PngBytes);
            var changedWhileSettling = FrameDifference.SampledChangedPercent(pixels, probePixels, Math.Max(sampleEvery, 8), 8);
            asset = probe;
            pixels = probePixels;
            if (changedWhileSettling <= dynamicThreshold) return new SettledFrame(asset, pixels, dynamicDetected);

            dynamicDetected = true;
            progress?.Report(new AutomaticScrollCaptureProgress(frameCount, changedWhileSettling, "Waiting for dynamic content"));
        }
        return new SettledFrame(asset, pixels, dynamicDetected);
    }

    private static byte[] ReadPixels(byte[] pngBytes)
    {
        using var bitmap = BitmapCodec.Decode(pngBytes);
        return BitmapPixelBuffer.ReadBgra(bitmap);
    }

    private sealed record SettledFrame(CaptureAsset Asset, byte[] Pixels, bool DynamicDetected);
    private sealed record PairAnalysis(OverlapMatch? Match, StitchFrameTrim UpperTrim, StitchFrameTrim LowerTrim, bool HasStickyBands);
}
