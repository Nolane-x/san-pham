using Magic.Capture.App.Imaging;
using Magic.Capture.App.Platform;
using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Imaging;

namespace Magic.Capture.App.Capture;

internal sealed record TwoDimensionalScrollCaptureOptions(
    int Rows = 2,
    int Columns = 2,
    int SettleMilliseconds = 180,
    int HorizontalWheelDelta = 720,
    int VerticalWheelDelta = -720,
    double MinimumChangedPixelPercent = 1.0,
    int SampleEveryPixels = 8,
    int DynamicProbeMilliseconds = 70,
    double DynamicChangedPixelPercent = 4.0,
    int DynamicSettleRetries = 1);

internal sealed record TwoDimensionalScrollCaptureProgress(
    int CapturedTiles,
    int TotalTiles,
    int Row,
    int Column,
    string Phase,
    double LastChangedPixelPercent = 0);

internal sealed record TwoDimensionalScrollCaptureResult(
    byte[] PngBytes,
    int Rows,
    int Columns,
    int TileCount,
    IReadOnlyList<int> HorizontalSeamOverlaps,
    IReadOnlyList<int> VerticalSeamOverlaps,
    bool DynamicContentDetected = false);

internal sealed class TwoDimensionalScrollCaptureService
{
    private readonly ScreenCaptureService _screen;
    private readonly GridImageStitcher _stitcher;
    private readonly InputSynthesisService _input;

    public TwoDimensionalScrollCaptureService(ScreenCaptureService screen, GridImageStitcher stitcher, InputSynthesisService input)
    {
        _screen = screen;
        _stitcher = stitcher;
        _input = input;
    }

    public async Task<TwoDimensionalScrollCaptureResult> CaptureAsync(
        PixelRect bounds,
        TwoDimensionalScrollCaptureOptions? options = null,
        IProgress<TwoDimensionalScrollCaptureProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (bounds.IsEmpty) throw new ArgumentException("2D scrolling capture region must not be empty.", nameof(bounds));
        options ??= new TwoDimensionalScrollCaptureOptions();
        var settle = Math.Clamp(options.SettleMilliseconds, 50, 2_000);
        var horizontalDelta = Math.Clamp(options.HorizontalWheelDelta, -NativeConstants.WheelDelta * 20, NativeConstants.WheelDelta * 20);
        var verticalDelta = Math.Clamp(options.VerticalWheelDelta, -NativeConstants.WheelDelta * 20, NativeConstants.WheelDelta * 20);
        if (options.Columns > 1 && horizontalDelta == 0) horizontalDelta = NativeConstants.WheelDelta * 6;
        if (options.Rows > 1 && verticalDelta == 0) verticalDelta = -NativeConstants.WheelDelta * 6;
        var plan = ScrollCaptureGridPlan.Create(options.Rows, options.Columns, horizontalDelta, verticalDelta);
        var minimumChanged = Math.Clamp(options.MinimumChangedPixelPercent, 0.1, 100);
        var sampleEvery = Math.Clamp(options.SampleEveryPixels, 1, 256);
        var dynamicProbe = Math.Clamp(options.DynamicProbeMilliseconds, 0, 500);
        var dynamicThreshold = Math.Clamp(options.DynamicChangedPixelPercent, 0.1, 50);
        var dynamicRetries = Math.Clamp(options.DynamicSettleRetries, 0, 3);
        var tiles = new List<byte[]>(plan.Tiles.Count);
        var originalCursor = _input.GetCursorPosition();
        var netHorizontal = 0;
        var netVertical = 0;
        var dynamicContentDetected = false;
        byte[]? previousPixels = null;

        try
        {
            _input.SetCursorPosition(bounds.Center);
            await Task.Delay(120, cancellationToken);
            foreach (var tile in plan.Tiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (tile.MoveBeforeCapture != ScrollVector.None)
                {
                    _input.Scroll(tile.MoveBeforeCapture);
                    netHorizontal = checked(netHorizontal + tile.MoveBeforeCapture.HorizontalWheelDelta);
                    netVertical = checked(netVertical + tile.MoveBeforeCapture.VerticalWheelDelta);
                    await Task.Delay(settle, cancellationToken);
                }

                progress?.Report(new TwoDimensionalScrollCaptureProgress(tiles.Count, plan.Tiles.Count, tile.Row, tile.Column, "Capturing tile"));
                var settledTile = await CaptureSettledTileAsync(
                    bounds, tile, dynamicProbe, dynamicThreshold, dynamicRetries, sampleEvery, progress, tiles.Count, plan.Tiles.Count, cancellationToken);
                dynamicContentDetected |= settledTile.DynamicDetected;
                var asset = settledTile.Asset;
                var pixels = settledTile.Pixels;
                if (asset.Width != bounds.Width || asset.Height != bounds.Height)
                    throw new InvalidOperationException("2D scrolling capture returned a tile with unexpected physical-pixel dimensions.");
                if (previousPixels is not null)
                {
                    var changed = FrameDifference.SampledChangedPercent(previousPixels, pixels, sampleEvery, 8);
                    if (changed < minimumChanged)
                        throw new InvalidOperationException($"The target did not visibly scroll before tile {tile.Row + 1},{tile.Column + 1}. Try a different region or larger scroll step.");
                }
                tiles.Add(asset.PngBytes);
                previousPixels = pixels;
                progress?.Report(new TwoDimensionalScrollCaptureProgress(tiles.Count, plan.Tiles.Count, tile.Row, tile.Column, "Captured tile"));
            }

            progress?.Report(new TwoDimensionalScrollCaptureProgress(tiles.Count, plan.Tiles.Count, options.Rows - 1, options.Columns - 1, "Stitching grid"));
            var stitched = await Task.Run(() => _stitcher.Stitch(tiles, options.Rows, options.Columns), cancellationToken);
            return new TwoDimensionalScrollCaptureResult(
                stitched.PngBytes, options.Rows, options.Columns, tiles.Count,
                stitched.HorizontalSeamOverlaps, stitched.VerticalSeamOverlaps, dynamicContentDetected);
        }
        finally
        {
            try
            {
                if (netHorizontal != 0 || netVertical != 0)
                    _input.Scroll(new ScrollVector(checked(-netHorizontal), checked(-netVertical)));
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (ArgumentOutOfRangeException) { }
            try { _input.SetCursorPosition(new PixelPoint(originalCursor.X, originalCursor.Y)); }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }

    private async Task<SettledTile> CaptureSettledTileAsync(
        PixelRect bounds,
        ScrollCaptureTile tile,
        int probeMilliseconds,
        double dynamicThreshold,
        int retryLimit,
        int sampleEvery,
        IProgress<TwoDimensionalScrollCaptureProgress>? progress,
        int capturedTiles,
        int totalTiles,
        CancellationToken cancellationToken)
    {
        var asset = _screen.Capture(bounds, CaptureSourceKind.Region, $"2D Scrolling Capture · {tile.Row + 1},{tile.Column + 1}", includeCursor: false);
        var pixels = ReadPixels(asset.PngBytes);
        if (probeMilliseconds <= 0) return new SettledTile(asset, pixels, false);

        var dynamicDetected = false;
        for (var attempt = 0; attempt <= retryLimit; attempt++)
        {
            await Task.Delay(probeMilliseconds, cancellationToken);
            var probe = _screen.Capture(bounds, CaptureSourceKind.Region, $"2D Scrolling Capture · {tile.Row + 1},{tile.Column + 1}", includeCursor: false);
            var probePixels = ReadPixels(probe.PngBytes);
            var changed = FrameDifference.SampledChangedPercent(pixels, probePixels, Math.Max(sampleEvery, 8), 8);
            asset = probe;
            pixels = probePixels;
            if (changed <= dynamicThreshold) return new SettledTile(asset, pixels, dynamicDetected);
            dynamicDetected = true;
            progress?.Report(new TwoDimensionalScrollCaptureProgress(
                capturedTiles, totalTiles, tile.Row, tile.Column, "Waiting for tile to settle", changed));
        }
        return new SettledTile(asset, pixels, dynamicDetected);
    }

    private static byte[] ReadPixels(byte[] pngBytes)
    {
        using var bitmap = BitmapCodec.Decode(pngBytes);
        return BitmapPixelBuffer.ReadBgra(bitmap);
    }

    private sealed record SettledTile(CaptureAsset Asset, byte[] Pixels, bool DynamicDetected);
}
