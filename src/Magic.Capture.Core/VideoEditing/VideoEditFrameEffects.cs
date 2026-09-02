namespace Magic.Capture.Core.VideoEditing;

public enum VideoEditFrameEffectKind
{
    ZoomPan,
    GaussianBlur,
    Pixelate
}

public sealed record VideoEditFrameKeyframe(
    TimeSpan Offset,
    double Primary,
    double X = 0.5,
    double Y = 0.5,
    VideoEditEasingKind Easing = VideoEditEasingKind.Linear);

public sealed record VideoEditFrameEffect(
    string Id,
    VideoEditFrameEffectKind Kind,
    TimeSpan Start,
    TimeSpan Duration,
    IReadOnlyList<VideoEditFrameKeyframe> Keyframes)
{
    public TimeSpan End => Start + Duration;
}

public readonly record struct VideoEditFrameEffectValue(double Primary, double X, double Y);

public static class VideoEditFrameEffectPolicy
{
    public const double MinimumPlaybackRate = 0.25;
    public const double MaximumPlaybackRate = 4.0;
    public const int MaximumFrameEffects = 128;
    public const int MaximumKeyframesPerEffect = 256;
    public const int MaximumBlurRadius = 32;
    public const int MaximumPixelateCell = 64;
    public const double MaximumZoom = 4.0;
    public static readonly TimeSpan MaximumAdvancedRenderDuration = TimeSpan.FromHours(4);
    public const long MaximumAdvancedRenderFrames = 500_000;
    private static readonly int[] SupportedOutputFps = [15, 24, 30, 60];

    public static double NormalizePlaybackRate(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, MinimumPlaybackRate, MaximumPlaybackRate) : 1.0;

    public static int NormalizeOutputFps(int value) =>
        SupportedOutputFps.OrderBy(x => Math.Abs(x - value)).First();

    public static VideoEditFrameEffectValue Evaluate(VideoEditFrameEffect effect, TimeSpan timelinePosition)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (effect.Keyframes.Count == 0) return DefaultValue(effect.Kind);
        var local = timelinePosition - effect.Start;
        if (local <= effect.Keyframes[0].Offset) return Normalize(effect.Kind, effect.Keyframes[0]);
        if (local >= effect.Keyframes[^1].Offset) return Normalize(effect.Kind, effect.Keyframes[^1]);

        for (var i = 1; i < effect.Keyframes.Count; i++)
        {
            var right = effect.Keyframes[i];
            if (local > right.Offset) continue;
            var left = effect.Keyframes[i - 1];
            var spanTicks = right.Offset.Ticks - left.Offset.Ticks;
            var raw = spanTicks <= 0 ? 0.0 : (local.Ticks - left.Offset.Ticks) / (double)spanTicks;
            var t = VideoEditEasing.Apply(left.Easing, raw);
            var interpolated = new VideoEditFrameKeyframe(
                local,
                Lerp(left.Primary, right.Primary, t),
                Lerp(left.X, right.X, t),
                Lerp(left.Y, right.Y, t));
            return Normalize(effect.Kind, interpolated);
        }
        return Normalize(effect.Kind, effect.Keyframes[^1]);
    }

    public static bool RequiresAdvancedRender(VideoEditProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.Segments.Any(x => Math.Abs(NormalizePlaybackRate(x.PlaybackRate) - 1.0) > 1e-9 || (x.AudioEnvelope?.Keyframes.Count ?? 0) > 0)
            || project.FrameEffectItems.Count > 0;
    }

    private static VideoEditFrameEffectValue DefaultValue(VideoEditFrameEffectKind kind) => kind switch
    {
        VideoEditFrameEffectKind.ZoomPan => new(1.0, 0.5, 0.5),
        VideoEditFrameEffectKind.GaussianBlur => new(1.0, 0.5, 0.5),
        VideoEditFrameEffectKind.Pixelate => new(2.0, 0.5, 0.5),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static VideoEditFrameEffectValue Normalize(VideoEditFrameEffectKind kind, VideoEditFrameKeyframe value) => kind switch
    {
        VideoEditFrameEffectKind.ZoomPan => new(
            Math.Clamp(double.IsFinite(value.Primary) ? value.Primary : 1.0, 1.0, MaximumZoom),
            Math.Clamp(double.IsFinite(value.X) ? value.X : 0.5, 0.0, 1.0),
            Math.Clamp(double.IsFinite(value.Y) ? value.Y : 0.5, 0.0, 1.0)),
        VideoEditFrameEffectKind.GaussianBlur => new(
            Math.Clamp(double.IsFinite(value.Primary) ? value.Primary : 1.0, 1.0, MaximumBlurRadius), 0.5, 0.5),
        VideoEditFrameEffectKind.Pixelate => new(
            Math.Clamp(double.IsFinite(value.Primary) ? value.Primary : 2.0, 2.0, MaximumPixelateCell), 0.5, 0.5),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0.0, 1.0);
}

public static class VideoEditBgraEffects
{
    private const long MaximumScratchBytes = 256L * 1024 * 1024;

    public static void ApplyPixelateInPlace(byte[] bgra, int width, int height, int cellSize)
    {
        Validate(bgra, width, height);
        var cell = Math.Clamp(cellSize, 2, VideoEditFrameEffectPolicy.MaximumPixelateCell);
        for (var y = 0; y < height; y += cell)
        for (var x = 0; x < width; x += cell)
        {
            var maxY = Math.Min(height, y + cell);
            var maxX = Math.Min(width, x + cell);
            long b = 0, g = 0, r = 0, count = 0;
            for (var py = y; py < maxY; py++)
            for (var px = x; px < maxX; px++)
            {
                var o = checked((py * width + px) * 4);
                b += bgra[o]; g += bgra[o + 1]; r += bgra[o + 2]; count++;
            }
            var bb = (byte)(b / Math.Max(1, count));
            var gg = (byte)(g / Math.Max(1, count));
            var rr = (byte)(r / Math.Max(1, count));
            for (var py = y; py < maxY; py++)
            for (var px = x; px < maxX; px++)
            {
                var o = checked((py * width + px) * 4);
                bgra[o] = bb; bgra[o + 1] = gg; bgra[o + 2] = rr; bgra[o + 3] = 255;
            }
        }
    }

    public static void ApplyGaussianBlurInPlace(byte[] bgra, int width, int height, int radius)
    {
        Validate(bgra, width, height);
        var r = Math.Clamp(radius, 1, VideoEditFrameEffectPolicy.MaximumBlurRadius);
        var bytes = checked((long)width * height * 4L);
        if (bytes > MaximumScratchBytes) throw new InvalidOperationException("Blur scratch buffer exceeds the bounded allocation budget.");
        var scratch = GC.AllocateUninitializedArray<byte>(checked((int)bytes));
        var sigma = Math.Max(0.5, r / 2.0);
        var kernel = BuildKernel(r, sigma);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            double b = 0, g = 0, red = 0;
            for (var k = -r; k <= r; k++)
            {
                var sx = Math.Clamp(x + k, 0, width - 1);
                var o = checked((y * width + sx) * 4);
                var w = kernel[k + r];
                b += bgra[o] * w; g += bgra[o + 1] * w; red += bgra[o + 2] * w;
            }
            var d = checked((y * width + x) * 4);
            scratch[d] = ToByte(b); scratch[d + 1] = ToByte(g); scratch[d + 2] = ToByte(red); scratch[d + 3] = 255;
        }

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            double b = 0, g = 0, red = 0;
            for (var k = -r; k <= r; k++)
            {
                var sy = Math.Clamp(y + k, 0, height - 1);
                var o = checked((sy * width + x) * 4);
                var w = kernel[k + r];
                b += scratch[o] * w; g += scratch[o + 1] * w; red += scratch[o + 2] * w;
            }
            var d = checked((y * width + x) * 4);
            bgra[d] = ToByte(b); bgra[d + 1] = ToByte(g); bgra[d + 2] = ToByte(red); bgra[d + 3] = 255;
        }
    }

    public static void ApplyZoomPanInPlace(byte[] bgra, int width, int height, double zoom, double centerX, double centerY)
    {
        Validate(bgra, width, height);
        var z = Math.Clamp(double.IsFinite(zoom) ? zoom : 1.0, 1.0, VideoEditFrameEffectPolicy.MaximumZoom);
        if (z <= 1.000001) return;
        var cx = Math.Clamp(double.IsFinite(centerX) ? centerX : 0.5, 0.0, 1.0);
        var cy = Math.Clamp(double.IsFinite(centerY) ? centerY : 0.5, 0.0, 1.0);
        var sourceWidth = Math.Max(1, (int)Math.Round(width / z));
        var sourceHeight = Math.Max(1, (int)Math.Round(height / z));
        var sourceX = Math.Clamp((int)Math.Round(cx * width - sourceWidth / 2.0), 0, width - sourceWidth);
        var sourceY = Math.Clamp((int)Math.Round(cy * height - sourceHeight / 2.0), 0, height - sourceHeight);
        var copy = bgra.ToArray();
        for (var y = 0; y < height; y++)
        {
            var sy = Math.Min(height - 1, sourceY + y * sourceHeight / height);
            for (var x = 0; x < width; x++)
            {
                var sx = Math.Min(width - 1, sourceX + x * sourceWidth / width);
                var src = checked((sy * width + sx) * 4);
                var dst = checked((y * width + x) * 4);
                bgra[dst] = copy[src]; bgra[dst + 1] = copy[src + 1]; bgra[dst + 2] = copy[src + 2]; bgra[dst + 3] = 255;
            }
        }
    }

    private static double[] BuildKernel(int radius, double sigma)
    {
        var kernel = new double[radius * 2 + 1];
        var sum = 0.0;
        for (var i = -radius; i <= radius; i++)
        {
            var value = Math.Exp(-(i * i) / (2.0 * sigma * sigma));
            kernel[i + radius] = value; sum += value;
        }
        for (var i = 0; i < kernel.Length; i++) kernel[i] /= sum;
        return kernel;
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static void Validate(byte[] bgra, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var expected = checked((long)width * height * 4L);
        if (expected != bgra.LongLength) throw new ArgumentException("BGRA buffer length does not match dimensions.", nameof(bgra));
    }
}
