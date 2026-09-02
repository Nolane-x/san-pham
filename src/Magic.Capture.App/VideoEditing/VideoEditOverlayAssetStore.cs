using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using Magic.Capture.App.Persistence;
using Magic.Capture.Core.VideoEditing;
using Windows.Storage;

namespace Magic.Capture.App.VideoEditing;

internal sealed class VideoEditOverlayAssetStore
{
    public const int MaximumCacheFiles = 256;
    public const long MaximumCacheBytes = 64L * 1024 * 1024;
    public const int MaximumRasterDimension = 2048;

    private readonly AppPaths _paths;
    private readonly LocalLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public VideoEditOverlayAssetStore(AppPaths paths, LocalLog log)
    {
        _paths = paths;
        _log = log;
    }

    public async Task<StorageFile> GetOrCreateAsync(
        VideoEditOverlay overlay,
        int outputWidth,
        int outputHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        cancellationToken.ThrowIfCancellationRequested();
        if (overlay.Kind == VideoEditOverlayKind.Redaction)
            throw new InvalidOperationException("Solid redaction overlays do not require raster assets.");

        var width = Math.Clamp((int)Math.Round(overlay.Bounds.Width * outputWidth), 32, MaximumRasterDimension);
        var height = Math.Clamp((int)Math.Round(overlay.Bounds.Height * outputHeight), 32, MaximumRasterDimension);
        var key = BuildKey(overlay, width, height);
        var finalPath = Path.Combine(_paths.VideoEditOverlayCacheRoot, key + ".png");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(finalPath) || new FileInfo(finalPath).Length == 0)
            {
                var tempPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    RenderOverlayPng(overlay, width, height, tempPath);
                    cancellationToken.ThrowIfCancellationRequested();
                    var info = new FileInfo(tempPath);
                    if (!info.Exists || info.Length == 0) throw new InvalidDataException("Overlay rasterizer produced an empty PNG.");
                    File.Move(tempPath, finalPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tempPath)) DeleteBestEffort(tempPath);
                }
            }
            File.SetLastWriteTimeUtc(finalPath, DateTime.UtcNow);
            PruneCacheBestEffort();
        }
        finally
        {
            _gate.Release();
        }

        return await StorageFile.GetFileFromPathAsync(finalPath);
    }

    private static string BuildKey(VideoEditOverlay overlay, int width, int height)
    {
        var style = VideoEditTextStyle.Normalize(overlay.TextStyle);
        var raw = string.Join('|',
            overlay.Kind,
            overlay.Text,
            overlay.FillArgb.ToString("X8"),
            overlay.StrokeArgb.ToString("X8"),
            overlay.StrokeWidth.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            overlay.FontScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            style.FontFamily, style.Weight, style.Italic, style.Underline, style.HorizontalAlignment,
            style.ShadowArgb.ToString("X8"), style.ShadowOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            style.OutlineArgb.ToString("X8"), style.OutlineWidth.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            width,
            height);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static void RenderOverlayPng(VideoEditOverlay overlay, int width, int height, string path)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var fill = FromArgb(overlay.FillArgb);
        var stroke = FromArgb(overlay.StrokeArgb);
        var strokeWidth = Math.Clamp((float)overlay.StrokeWidth, 0f, 32f);
        var inset = Math.Max(1f, strokeWidth / 2f + 1f);
        var rect = new RectangleF(inset, inset, Math.Max(1f, width - inset * 2), Math.Max(1f, height - inset * 2));

        switch (overlay.Kind)
        {
            case VideoEditOverlayKind.Text:
                DrawText(graphics, overlay, rect, fill);
                break;
            case VideoEditOverlayKind.Rectangle:
                using (var brush = new SolidBrush(fill)) graphics.FillRectangle(brush, rect);
                if (strokeWidth > 0) using (var pen = new Pen(stroke, strokeWidth)) graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                break;
            case VideoEditOverlayKind.Ellipse:
                using (var brush = new SolidBrush(fill)) graphics.FillEllipse(brush, rect);
                if (strokeWidth > 0) using (var pen = new Pen(stroke, strokeWidth)) graphics.DrawEllipse(pen, rect);
                break;
            case VideoEditOverlayKind.Arrow:
                using (var pen = new Pen(stroke, Math.Max(2f, strokeWidth)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.ArrowAnchor;
                    graphics.DrawLine(pen, rect.Left, rect.Bottom, rect.Right, rect.Top);
                }
                break;
            default:
                throw new InvalidOperationException($"Overlay kind {overlay.Kind} does not use raster assets.");
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static void DrawText(Graphics graphics, VideoEditOverlay overlay, RectangleF rect, Color color)
    {
        var textStyle = VideoEditTextStyle.Normalize(overlay.TextStyle);
        var size = Math.Clamp((float)(overlay.FontScale * rect.Height * 4.0), 10f, Math.Max(12f, rect.Height * 0.9f));
        var fontStyle = FontStyle.Regular;
        if (textStyle.Weight >= 600) fontStyle |= FontStyle.Bold;
        if (textStyle.Italic) fontStyle |= FontStyle.Italic;
        if (textStyle.Underline) fontStyle |= FontStyle.Underline;
        using var font = CreateFont(textStyle.FontFamily, size, fontStyle);
        using var format = new StringFormat
        {
            Alignment = textStyle.HorizontalAlignment switch
            {
                VideoEditTextAlignment.Left => StringAlignment.Near,
                VideoEditTextAlignment.Right => StringAlignment.Far,
                _ => StringAlignment.Center
            },
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisWord,
            FormatFlags = StringFormatFlags.LineLimit
        };

        if ((textStyle.ShadowArgb >> 24) != 0 && textStyle.ShadowOffset > 0)
        {
            using var shadow = new SolidBrush(FromArgb(textStyle.ShadowArgb));
            var shifted = rect;
            shifted.Offset((float)textStyle.ShadowOffset, (float)textStyle.ShadowOffset);
            graphics.DrawString(overlay.Text, font, shadow, shifted, format);
        }

        if (textStyle.OutlineWidth > 0 && (textStyle.OutlineArgb >> 24) != 0)
        {
            using var outline = new SolidBrush(FromArgb(textStyle.OutlineArgb));
            var radius = Math.Clamp((float)textStyle.OutlineWidth, 1f, 12f);
            foreach (var (dx, dy) in new[] { (-1f, 0f), (1f, 0f), (0f, -1f), (0f, 1f), (-0.7f, -0.7f), (0.7f, -0.7f), (-0.7f, 0.7f), (0.7f, 0.7f) })
            {
                var shifted = rect;
                shifted.Offset(dx * radius, dy * radius);
                graphics.DrawString(overlay.Text, font, outline, shifted, format);
            }
        }

        using var brush = new SolidBrush(color);
        graphics.DrawString(overlay.Text, font, brush, rect, format);
    }

    private static Font CreateFont(string family, float size, FontStyle style)
    {
        try { return new Font(family, size, style, GraphicsUnit.Pixel); }
        catch (ArgumentException) { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Pixel); }
    }

    private void PruneCacheBestEffort()
    {
        try
        {
            var files = new DirectoryInfo(_paths.VideoEditOverlayCacheRoot)
                .EnumerateFiles("*.png", SearchOption.TopDirectoryOnly)
                .OrderByDescending(x => x.LastWriteTimeUtc)
                .ToList();
            long total = files.Sum(x => x.Length);
            for (var i = files.Count - 1; i >= 0 && (i + 1 > MaximumCacheFiles || total > MaximumCacheBytes); i--)
            {
                var file = files[i];
                total -= file.Length;
                DeleteBestEffort(file.FullName);
                files.RemoveAt(i);
            }
        }
        catch (IOException ex) { _log.Error("VideoEdit.OverlayCache", ex); }
        catch (UnauthorizedAccessException ex) { _log.Error("VideoEdit.OverlayCache", ex); }
    }

    private static Color FromArgb(uint value) => Color.FromArgb(
        unchecked((byte)(value >> 24)),
        unchecked((byte)(value >> 16)),
        unchecked((byte)(value >> 8)),
        unchecked((byte)value));

    private static void DeleteBestEffort(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
