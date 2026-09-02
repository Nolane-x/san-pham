namespace Magic.Capture.Core.Color;

public static class ColorContrast
{
    public static double Ratio(ColorValue first, ColorValue second)
    {
        var l1 = RelativeLuminance(first);
        var l2 = RelativeLuminance(second);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static string WcagLabel(double ratio) => ratio >= 7 ? "AAA normal" : ratio >= 4.5 ? "AA normal" : ratio >= 3 ? "AA large" : "Fail";

    private static double RelativeLuminance(ColorValue color) =>
        0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

    private static double Channel(byte value)
    {
        var x = value / 255d;
        return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
    }
}
