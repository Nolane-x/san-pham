using Magic.Capture.Core.Color;

namespace Magic.Capture.Core.Tests;

public sealed class ColorValueTests
{
    [Theory]
    [InlineData(255, 0, 0, "#FF0000", "hsl(0, 100%, 50%)")]
    [InlineData(0, 255, 0, "#00FF00", "hsl(120, 100%, 50%)")]
    [InlineData(0, 0, 255, "#0000FF", "hsl(240, 100%, 50%)")]
    [InlineData(255, 255, 255, "#FFFFFF", "hsl(0, 0%, 100%)")]
    [InlineData(0, 0, 0, "#000000", "hsl(0, 0%, 0%)")]
    public void FormatsKnownColors(byte r, byte g, byte b, string hex, string hsl)
    {
        var color = ColorValue.FromRgb(r, g, b);
        Assert.Equal(hex, color.Hex);
        Assert.Equal(hsl, color.Hsl);
    }
}
