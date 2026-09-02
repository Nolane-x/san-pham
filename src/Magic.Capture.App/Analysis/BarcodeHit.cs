using Magic.Capture.Core.Geometry;

namespace Magic.Capture.App.Analysis;

internal sealed record BarcodeHit(string Format, string Text, byte[]? RawBytes, PixelRect? Bounds = null)
{
    public bool IsUri => Uri.TryCreate(Text, UriKind.Absolute, out var uri) &&
                         (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
