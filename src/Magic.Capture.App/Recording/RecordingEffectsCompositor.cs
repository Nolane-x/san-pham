using Magic.Capture.Core.Recording;

namespace Magic.Capture.App.Recording;

internal static class RecordingEffectsCompositor
{
    private static readonly Dictionary<char, byte[]> Glyphs = BuildGlyphs();

    public static void ApplyZoomInPlace(
        RecordingFramePixels frame,
        RecordingInputSnapshot input,
        RecordingOptions options,
        int sourceWidth,
        int sourceHeight)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(input);
        options = RecordingRules.Normalize(options);
        if (!options.LiveZoom || !input.ZoomActive) return;

        var focus = ScalePoint(input.Cursor, sourceWidth, sourceHeight, frame.Width, frame.Height);
        if (!Inside(focus, frame.Width, frame.Height)) focus = new RecordingPoint(frame.Width / 2, frame.Height / 2);
        var source = RecordingEffectsPolicy.ComputeZoomSourceRect(frame.Width, frame.Height, focus, options.ZoomPercent);
        var original = frame.BgraBytes.ToArray();
        for (var y = 0; y < frame.Height; y++)
        {
            var sy = source.Y + Math.Min(source.Height - 1, (int)((long)y * source.Height / frame.Height));
            for (var x = 0; x < frame.Width; x++)
            {
                var sx = source.X + Math.Min(source.Width - 1, (int)((long)x * source.Width / frame.Width));
                var src = checked((sy * frame.Width + sx) * 4);
                var dst = checked((y * frame.Width + x) * 4);
                frame.BgraBytes[dst] = original[src];
                frame.BgraBytes[dst + 1] = original[src + 1];
                frame.BgraBytes[dst + 2] = original[src + 2];
                frame.BgraBytes[dst + 3] = original[src + 3];
            }
        }
    }

    public static void ApplyOverlaysInPlace(
        RecordingFramePixels frame,
        RecordingInputSnapshot input,
        RecordingOptions options,
        int sourceWidth,
        int sourceHeight,
        TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(input);
        options = RecordingRules.Normalize(options);

        var zoomRect = GetZoomRect(frame, input, options, sourceWidth, sourceHeight);
        RecordingPoint Map(RecordingPoint point)
        {
            var basePoint = ScalePoint(point, sourceWidth, sourceHeight, frame.Width, frame.Height);
            if (zoomRect is not { } z) return basePoint;
            return new RecordingPoint(
                checked((int)Math.Round((basePoint.X - z.X) * (double)frame.Width / z.Width)),
                checked((int)Math.Round((basePoint.Y - z.Y) * (double)frame.Height / z.Height)));
        }

        var cursor = Map(input.Cursor);
        if (options.CursorHighlight && Inside(cursor, frame.Width, frame.Height))
        {
            FillCircle(frame.BgraBytes, frame.Width, frame.Height, cursor.X, cursor.Y, 22, 0, 210, 255, 58);
            DrawRing(frame.BgraBytes, frame.Width, frame.Height, cursor.X, cursor.Y, 22, 2, 0, 210, 255, 220);
        }

        if (options.ClickVisualization)
        {
            foreach (var click in input.Clicks)
            {
                if (!RecordingEffectsPolicy.TryGetRippleProgress(now, click.Timestamp, out var progress)) continue;
                var p = Map(click.Point);
                if (!Inside(p, frame.Width, frame.Height)) continue;
                var radius = 18 + (int)Math.Round(progress * 34);
                var alpha = (byte)Math.Clamp((int)Math.Round(230 * (1.0 - progress)), 20, 230);
                if (click.Button == RecordingMouseButton.Left)
                    DrawRing(frame.BgraBytes, frame.Width, frame.Height, p.X, p.Y, radius, 4, 40, 220, 255, alpha);
                else
                    DrawRing(frame.BgraBytes, frame.Width, frame.Height, p.X, p.Y, radius, 4, 80, 80, 255, alpha);
            }
        }

        if (options.DrawWhileRecording)
        {
            foreach (var stroke in input.Strokes)
            {
                for (var i = 1; i < stroke.Points.Count; i++)
                {
                    var a = Map(stroke.Points[i - 1]);
                    var b = Map(stroke.Points[i]);
                    DrawLine(frame.BgraBytes, frame.Width, frame.Height, a, b, 3, 40, 80, 255, 235);
                }
            }
        }

        if (options.SafeKeyOverlay && input.Key is { } key && now - key.Timestamp < RecordingEffectsPolicy.KeyOverlayLifetime)
            DrawKeyBadge(frame.BgraBytes, frame.Width, frame.Height, key.Label);
    }

    private static RecordingRect? GetZoomRect(RecordingFramePixels frame, RecordingInputSnapshot input, RecordingOptions options, int sourceWidth, int sourceHeight)
    {
        if (!options.LiveZoom || !input.ZoomActive) return null;
        var focus = ScalePoint(input.Cursor, sourceWidth, sourceHeight, frame.Width, frame.Height);
        if (!Inside(focus, frame.Width, frame.Height)) focus = new RecordingPoint(frame.Width / 2, frame.Height / 2);
        return RecordingEffectsPolicy.ComputeZoomSourceRect(frame.Width, frame.Height, focus, options.ZoomPercent);
    }

    private static RecordingPoint ScalePoint(RecordingPoint point, int sourceWidth, int sourceHeight, int outputWidth, int outputHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0) return point;
        return new RecordingPoint(
            checked((int)Math.Round(point.X * (double)outputWidth / sourceWidth)),
            checked((int)Math.Round(point.Y * (double)outputHeight / sourceHeight)));
    }

    private static bool Inside(RecordingPoint point, int width, int height) =>
        point.X >= 0 && point.Y >= 0 && point.X < width && point.Y < height;

    private static void DrawLine(byte[] pixels, int width, int height, RecordingPoint a, RecordingPoint b, int radius, byte bgrB, byte bgrG, byte bgrR, byte alpha)
    {
        var x0 = a.X; var y0 = a.Y; var x1 = b.X; var y1 = b.Y;
        var dx = Math.Abs(x1 - x0); var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0); var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        while (true)
        {
            FillCircle(pixels, width, height, x0, y0, radius, bgrB, bgrG, bgrR, alpha);
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * error;
            if (e2 >= dy) { error += dy; x0 += sx; }
            if (e2 <= dx) { error += dx; y0 += sy; }
        }
    }

    private static void DrawRing(byte[] pixels, int width, int height, int cx, int cy, int radius, int thickness, byte b, byte g, byte r, byte alpha)
    {
        var inner = Math.Max(0, radius - thickness);
        var r2 = radius * radius;
        var i2 = inner * inner;
        var left = Math.Max(0, cx - radius); var right = Math.Min(width - 1, cx + radius);
        var top = Math.Max(0, cy - radius); var bottom = Math.Min(height - 1, cy + radius);
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
        {
            var dx = x - cx; var dy = y - cy; var d = dx * dx + dy * dy;
            if (d <= r2 && d >= i2) Blend(pixels, width, x, y, b, g, r, alpha);
        }
    }

    private static void FillCircle(byte[] pixels, int width, int height, int cx, int cy, int radius, byte b, byte g, byte r, byte alpha)
    {
        var r2 = radius * radius;
        var left = Math.Max(0, cx - radius); var right = Math.Min(width - 1, cx + radius);
        var top = Math.Max(0, cy - radius); var bottom = Math.Min(height - 1, cy + radius);
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
        {
            var dx = x - cx; var dy = y - cy;
            if (dx * dx + dy * dy <= r2) Blend(pixels, width, x, y, b, g, r, alpha);
        }
    }

    private static void DrawKeyBadge(byte[] pixels, int width, int height, string label)
    {
        var text = label.ToUpperInvariant();
        const int scale = 3;
        var textWidth = text.Length * 6 * scale;
        var boxWidth = Math.Min(width - 8, textWidth + 24);
        var boxHeight = 7 * scale + 18;
        if (boxWidth <= 8 || height <= boxHeight + 8) return;
        var left = (width - boxWidth) / 2;
        var top = height - boxHeight - 24;
        FillRect(pixels, width, height, left, top, boxWidth, boxHeight, 20, 20, 20, 190);
        DrawRect(pixels, width, height, left, top, boxWidth, boxHeight, 1, 255, 255, 255, 180);
        var x = left + 12;
        var y = top + 9;
        foreach (var ch in text)
        {
            DrawGlyph(pixels, width, height, ch, x, y, scale);
            x += 6 * scale;
            if (x + 5 * scale >= left + boxWidth) break;
        }
    }

    private static void DrawGlyph(byte[] pixels, int width, int height, char ch, int left, int top, int scale)
    {
        if (!Glyphs.TryGetValue(ch, out var rows)) rows = Glyphs['?'];
        for (var y = 0; y < 7; y++)
        for (var x = 0; x < 5; x++)
        {
            if ((rows[y] & (1 << (4 - x))) == 0) continue;
            FillRect(pixels, width, height, left + x * scale, top + y * scale, scale, scale, 245, 245, 245, 255);
        }
    }

    private static void FillRect(byte[] pixels, int width, int height, int x, int y, int w, int h, byte b, byte g, byte r, byte alpha)
    {
        var right = Math.Min(width, x + w); var bottom = Math.Min(height, y + h);
        for (var py = Math.Max(0, y); py < bottom; py++)
        for (var px = Math.Max(0, x); px < right; px++) Blend(pixels, width, px, py, b, g, r, alpha);
    }

    private static void DrawRect(byte[] pixels, int width, int height, int x, int y, int w, int h, int t, byte b, byte g, byte r, byte alpha)
    {
        FillRect(pixels, width, height, x, y, w, t, b, g, r, alpha);
        FillRect(pixels, width, height, x, y + h - t, w, t, b, g, r, alpha);
        FillRect(pixels, width, height, x, y, t, h, b, g, r, alpha);
        FillRect(pixels, width, height, x + w - t, y, t, h, b, g, r, alpha);
    }

    private static void Blend(byte[] pixels, int width, int x, int y, byte b, byte g, byte r, byte alpha)
    {
        var o = checked((y * width + x) * 4);
        var inv = 255 - alpha;
        pixels[o] = (byte)((b * alpha + pixels[o] * inv + 127) / 255);
        pixels[o + 1] = (byte)((g * alpha + pixels[o + 1] * inv + 127) / 255);
        pixels[o + 2] = (byte)((r * alpha + pixels[o + 2] * inv + 127) / 255);
        pixels[o + 3] = 255;
    }

    private static Dictionary<char, byte[]> BuildGlyphs()
    {
        var source = new Dictionary<char, string>
        {
            ['A']="01110,10001,10001,11111,10001,10001,10001", ['B']="11110,10001,10001,11110,10001,10001,11110",
            ['C']="01111,10000,10000,10000,10000,10000,01111", ['D']="11110,10001,10001,10001,10001,10001,11110",
            ['E']="11111,10000,10000,11110,10000,10000,11111", ['F']="11111,10000,10000,11110,10000,10000,10000",
            ['G']="01111,10000,10000,10111,10001,10001,01111", ['H']="10001,10001,10001,11111,10001,10001,10001",
            ['I']="11111,00100,00100,00100,00100,00100,11111", ['J']="00111,00010,00010,00010,10010,10010,01100",
            ['K']="10001,10010,10100,11000,10100,10010,10001", ['L']="10000,10000,10000,10000,10000,10000,11111",
            ['M']="10001,11011,10101,10101,10001,10001,10001", ['N']="10001,11001,10101,10011,10001,10001,10001",
            ['O']="01110,10001,10001,10001,10001,10001,01110", ['P']="11110,10001,10001,11110,10000,10000,10000",
            ['Q']="01110,10001,10001,10001,10101,10010,01101", ['R']="11110,10001,10001,11110,10100,10010,10001",
            ['S']="01111,10000,10000,01110,00001,00001,11110", ['T']="11111,00100,00100,00100,00100,00100,00100",
            ['U']="10001,10001,10001,10001,10001,10001,01110", ['V']="10001,10001,10001,10001,10001,01010,00100",
            ['W']="10001,10001,10001,10101,10101,10101,01010", ['X']="10001,10001,01010,00100,01010,10001,10001",
            ['Y']="10001,10001,01010,00100,00100,00100,00100", ['Z']="11111,00001,00010,00100,01000,10000,11111",
            ['0']="01110,10001,10011,10101,11001,10001,01110", ['1']="00100,01100,00100,00100,00100,00100,01110",
            ['2']="01110,10001,00001,00010,00100,01000,11111", ['3']="11110,00001,00001,01110,00001,00001,11110",
            ['4']="00010,00110,01010,10010,11111,00010,00010", ['5']="11111,10000,10000,11110,00001,00001,11110",
            ['6']="01110,10000,10000,11110,10001,10001,01110", ['7']="11111,00001,00010,00100,01000,01000,01000",
            ['8']="01110,10001,10001,01110,10001,10001,01110", ['9']="01110,10001,10001,01111,00001,00001,01110",
            ['+']="00000,00100,00100,11111,00100,00100,00000", [' ']="00000,00000,00000,00000,00000,00000,00000",
            ['?']="01110,10001,00001,00010,00100,00000,00100"
        };
        return source.ToDictionary(pair => pair.Key, pair => pair.Value.Split(',').Select(row => Convert.ToByte(row, 2)).ToArray());
    }
}
