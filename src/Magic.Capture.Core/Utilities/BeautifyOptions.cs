namespace Magic.Capture.Core.Utilities;

public sealed record BeautifyOptions(
    int Padding = 32,
    int CornerRadius = 16,
    int ShadowBlur = 24,
    int BorderWidth = 0,
    double ShadowOpacity = 0.25,
    string Background = "#F3F3F3",
    string BorderColor = "#00000000",
    string ShadowColor = "#000000")
{
    public BeautifyOptions Normalize() => this with
    {
        Padding = Math.Clamp(Padding, 0, 1024),
        CornerRadius = Math.Clamp(CornerRadius, 0, 512),
        ShadowBlur = Math.Clamp(ShadowBlur, 0, 256),
        BorderWidth = Math.Clamp(BorderWidth, 0, 128),
        ShadowOpacity = Math.Clamp(ShadowOpacity, 0, 1),
        Background = NormalizeColor(Background, "#F3F3F3"),
        BorderColor = NormalizeColor(BorderColor, "#00000000"),
        ShadowColor = NormalizeColor(ShadowColor, "#000000")
    };

    private static string NormalizeColor(string? color, string fallback)
    {
        if (string.IsNullOrWhiteSpace(color)) return fallback;
        var value = color.Trim();
        if (!value.StartsWith('#')) value = "#" + value;
        return value.Length is 7 or 9 ? value.ToUpperInvariant() : fallback;
    }
}
