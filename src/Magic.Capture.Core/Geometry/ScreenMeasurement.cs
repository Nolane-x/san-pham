namespace Magic.Capture.Core.Geometry;

public sealed record ScreenMeasurementResult(int DeltaX, int DeltaY, double DistancePixels, double AngleDegrees, double Inches, double Centimeters);

public static class ScreenMeasurement
{
    public static double CalibrateDpi(double pixelLength, double physicalInches)
    {
        if (!double.IsFinite(pixelLength) || !double.IsFinite(physicalInches) || pixelLength <= 0 || physicalInches <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelLength), "Pixel length and physical length must both be finite and positive.");
        var dpi = pixelLength / physicalInches;
        if (dpi is < 10 or > 2_000)
            throw new ArgumentOutOfRangeException(nameof(physicalInches), "Calibrated DPI must be between 10 and 2000.");
        return dpi;
    }

    public static ScreenMeasurementResult Measure(PixelPoint start, PixelPoint end, double dpi)
    {
        dpi = double.IsFinite(dpi) ? Math.Clamp(dpi, 10, 2_000) : 96;
        var dx = end.X - start.X; var dy = end.Y - start.Y;
        var distance = Math.Sqrt((double)dx * dx + (double)dy * dy);
        var angle = Math.Atan2(dy, dx) * 180d / Math.PI;
        if (angle < 0) angle += 360;
        var inches = distance / dpi;
        return new(dx, dy, distance, angle, inches, inches * 2.54);
    }
}
