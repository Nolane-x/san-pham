using Magic.Capture.Core.Color;
using Magic.Capture.Core.Geometry;
using Xunit;

namespace Magic.Capture.Core.Tests;

public sealed class DesignToolsTests
{
    [Fact]
    public void Color_formats_and_contrast_are_deterministic()
    {
        var red = ColorValue.FromRgb(255, 0, 0);
        Assert.Contains("hsv", red.Hsv);
        Assert.Contains("cmyk", red.Cmyk);
        Assert.Equal("#FF0000", red.Css);
        Assert.InRange(ColorContrast.Ratio(ColorValue.FromRgb(0,0,0), ColorValue.FromRgb(255,255,255)), 20.9, 21.1);
    }

    [Fact]
    public void Palette_extracts_average_and_dominant_colors()
    {
        var pixels = new byte[4 * 4 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 2] = 255; pixels[i + 3] = 255; }
        for (var i = 0; i < 4 * 4; i += 4) { var p = i * 4; pixels[p + 2] = 0; pixels[p] = 255; }
        var result = ColorPaletteExtractor.ExtractBgra(pixels, 4, 4, 4);
        Assert.Equal(255, result.Dominant.R);
        Assert.NotEmpty(result.Colors);
    }

    [Fact]
    public void Measurement_returns_pixels_angle_and_physical_units()
    {
        var result = ScreenMeasurement.Measure(new PixelPoint(0,0), new PixelPoint(300,400), 100);
        Assert.Equal(500, result.DistancePixels, 6);
        Assert.Equal(5, result.Inches, 6);
        Assert.Equal(12.7, result.Centimeters, 6);
    }
    [Fact]
    public void Measurement_calibrates_dpi_from_known_physical_length()
    {
        Assert.Equal(120, ScreenMeasurement.CalibrateDpi(600, 5), 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenMeasurement.CalibrateDpi(0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenMeasurement.CalibrateDpi(600, 0));
    }

}
