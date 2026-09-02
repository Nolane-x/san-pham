using System.Globalization;

namespace Magic.Capture.Core.Color;

public readonly record struct ColorValue(byte R, byte G, byte B, byte A, double Hue, double Saturation, double Lightness)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public string Rgb => $"rgb({R}, {G}, {B})";
    public string Hsl => $"hsl({Round(Hue)}, {Round(Saturation * 100)}%, {Round(Lightness * 100)}%)";
    public string Hsv
    {
        get
        {
            var max = Math.Max(R, Math.Max(G, B)) / 255d;
            var min = Math.Min(R, Math.Min(G, B)) / 255d;
            var value = max;
            var saturation = max <= 0 ? 0 : (max - min) / max;
            return $"hsv({Round(Hue)}, {Round(saturation * 100)}%, {Round(value * 100)}%)";
        }
    }
    public string Cmyk
    {
        get
        {
            var rd = R / 255d; var gd = G / 255d; var bd = B / 255d;
            var k = 1 - Math.Max(rd, Math.Max(gd, bd));
            var denominator = Math.Max(1e-9, 1 - k);
            var c = (1 - rd - k) / denominator; var m = (1 - gd - k) / denominator; var y = (1 - bd - k) / denominator;
            if (k >= .999999) c = m = y = 0;
            return $"cmyk({Round(c * 100)}%, {Round(m * 100)}%, {Round(y * 100)}%, {Round(k * 100)}%)";
        }
    }
    public string Css => Hex;
    public string CSharp => $"Color.FromArgb({A}, {R}, {G}, {B})";
    public string Cpp => $"RGB({R}, {G}, {B})";

    public static ColorValue FromRgb(byte r, byte g, byte b, byte a = 255)
    {
        var rd = r / 255d;
        var gd = g / 255d;
        var bd = b / 255d;
        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var delta = max - min;
        var lightness = (max + min) / 2d;
        double hue;
        double saturation;

        if (delta == 0)
        {
            hue = 0;
            saturation = 0;
        }
        else
        {
            saturation = delta / (1 - Math.Abs(2 * lightness - 1));
            if (max == rd)
                hue = 60 * (((gd - bd) / delta) % 6);
            else if (max == gd)
                hue = 60 * (((bd - rd) / delta) + 2);
            else
                hue = 60 * (((rd - gd) / delta) + 4);
            if (hue < 0) hue += 360;
        }

        return new ColorValue(r, g, b, a, hue, saturation, lightness);
    }

    public override string ToString() => Hex;

    private static string Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
}
