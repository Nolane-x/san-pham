using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Recording;

public enum WebcamOverlayShape
{
    Rectangle,
    Rounded,
    Circle
}

public readonly record struct WebcamOverlayRect(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
}

public static class RecordingWebcamPolicy
{
    public const int MinimumWidthPercent = 10;
    public const int MaximumWidthPercent = 50;
    public const int MinimumOpacityPercent = 20;
    public const int MaximumOpacityPercent = 100;
    public const int MaximumBorderPixels = 12;
    public const int MaximumDeviceIdLength = 1024;
    public static readonly TimeSpan WarmUpTimeout = TimeSpan.FromSeconds(5);

    public static string? NormalizeDeviceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > MaximumDeviceIdLength)
            throw new ArgumentOutOfRangeException(nameof(value), $"Webcam device id cannot exceed {MaximumDeviceIdLength} characters.");
        return trimmed;
    }

    public static WebcamOverlayRect ComputeOverlayRect(
        int outputWidth,
        int outputHeight,
        int cameraWidth,
        int cameraHeight,
        int xPercent,
        int yPercent,
        int widthPercent,
        WebcamOverlayShape shape = WebcamOverlayShape.Rounded)
    {
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(outputWidth, outputHeight);
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(cameraWidth, cameraHeight);

        var widthPct = Math.Clamp(widthPercent, MinimumWidthPercent, MaximumWidthPercent);
        var width = Math.Clamp((int)Math.Round(outputWidth * (widthPct / 100d)), 1, outputWidth);
        var height = shape == WebcamOverlayShape.Circle
            ? width
            : Math.Max(1, (int)Math.Round(width * (cameraHeight / (double)cameraWidth)));

        var maxHeight = Math.Max(1, outputHeight / 2);
        if (height > maxHeight)
        {
            height = maxHeight;
            width = Math.Max(1, (int)Math.Round(height * (cameraWidth / (double)cameraHeight)));
            width = Math.Min(width, outputWidth);
        }

        var x = (int)Math.Round((outputWidth - width) * (Math.Clamp(xPercent, 0, 100) / 100d));
        var y = (int)Math.Round((outputHeight - height) * (Math.Clamp(yPercent, 0, 100) / 100d));
        x = Math.Clamp(x, 0, outputWidth - width);
        y = Math.Clamp(y, 0, outputHeight - height);
        return new WebcamOverlayRect(x, y, width, height);
    }
}

public static class BgraWebcamCompositor
{
    public static void CompositeInPlace(
        byte[] canvas,
        int canvasWidth,
        int canvasHeight,
        byte[] camera,
        int cameraWidth,
        int cameraHeight,
        WebcamOverlayRect destination,
        WebcamOverlayShape shape,
        bool mirror,
        int opacityPercent,
        int borderPixels)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(camera);
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(canvasWidth, canvasHeight);
        ImageWorkloadLimits.ValidatePixelProcessingDimensions(cameraWidth, cameraHeight);
        ValidateBuffer(canvas, canvasWidth, canvasHeight, nameof(canvas));
        ValidateBuffer(camera, cameraWidth, cameraHeight, nameof(camera));

        if (destination.Width <= 0 || destination.Height <= 0 || destination.X < 0 || destination.Y < 0 ||
            destination.Right > canvasWidth || destination.Bottom > canvasHeight)
            throw new ArgumentOutOfRangeException(nameof(destination), "Webcam overlay must be fully inside the recording canvas.");

        var opacity = Math.Clamp(opacityPercent, RecordingWebcamPolicy.MinimumOpacityPercent, RecordingWebcamPolicy.MaximumOpacityPercent) / 100d;
        var border = Math.Clamp(borderPixels, 0, RecordingWebcamPolicy.MaximumBorderPixels);
        var destinationAspect = destination.Width / (double)destination.Height;
        var sourceAspect = cameraWidth / (double)cameraHeight;

        double cropX = 0, cropY = 0, cropWidth = cameraWidth, cropHeight = cameraHeight;
        if (sourceAspect > destinationAspect)
        {
            cropWidth = cameraHeight * destinationAspect;
            cropX = (cameraWidth - cropWidth) / 2d;
        }
        else if (sourceAspect < destinationAspect)
        {
            cropHeight = cameraWidth / destinationAspect;
            cropY = (cameraHeight - cropHeight) / 2d;
        }

        for (var dy = 0; dy < destination.Height; dy++)
        {
            for (var dx = 0; dx < destination.Width; dx++)
            {
                if (!InsideMask(dx, dy, destination.Width, destination.Height, shape, inset: 0)) continue;

                var isBorder = border > 0 && !InsideMask(dx, dy, destination.Width, destination.Height, shape, inset: border);
                byte b, g, r, a;
                if (isBorder)
                {
                    b = g = r = a = 255;
                }
                else
                {
                    var u = destination.Width == 1 ? 0.5 : (dx + 0.5) / destination.Width;
                    var v = destination.Height == 1 ? 0.5 : (dy + 0.5) / destination.Height;
                    if (mirror) u = 1d - u;
                    var sx = cropX + u * cropWidth - 0.5;
                    var sy = cropY + v * cropHeight - 0.5;
                    SampleBilinear(camera, cameraWidth, cameraHeight, sx, sy, out b, out g, out r, out a);
                }

                var alpha = opacity * (a / 255d);
                var outputX = destination.X + dx;
                var outputY = destination.Y + dy;
                var o = checked((outputY * canvasWidth + outputX) * 4);
                canvas[o] = Blend(canvas[o], b, alpha);
                canvas[o + 1] = Blend(canvas[o + 1], g, alpha);
                canvas[o + 2] = Blend(canvas[o + 2], r, alpha);
                canvas[o + 3] = 255;
            }
        }
    }

    private static bool InsideMask(int x, int y, int width, int height, WebcamOverlayShape shape, int inset)
    {
        var left = inset;
        var top = inset;
        var right = width - 1 - inset;
        var bottom = height - 1 - inset;
        if (left > right || top > bottom) return false;
        if (x < left || x > right || y < top || y > bottom) return false;
        if (shape == WebcamOverlayShape.Rectangle) return true;

        var innerWidth = right - left + 1;
        var innerHeight = bottom - top + 1;
        var cx = (left + right + 1) / 2d;
        var cy = (top + bottom + 1) / 2d;
        if (shape == WebcamOverlayShape.Circle)
        {
            var rx = innerWidth / 2d;
            var ry = innerHeight / 2d;
            if (rx <= 0 || ry <= 0) return false;
            var nx = (x + 0.5 - cx) / rx;
            var ny = (y + 0.5 - cy) / ry;
            return nx * nx + ny * ny <= 1d;
        }

        var radius = Math.Max(1d, Math.Min(innerWidth, innerHeight) * 0.18d);
        if (x >= left + radius && x <= right - radius) return true;
        if (y >= top + radius && y <= bottom - radius) return true;
        var cornerX = x < cx ? left + radius : right - radius;
        var cornerY = y < cy ? top + radius : bottom - radius;
        var dx = x + 0.5 - cornerX;
        var dy = y + 0.5 - cornerY;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static void SampleBilinear(byte[] pixels, int width, int height, double x, double y, out byte b, out byte g, out byte r, out byte a)
    {
        x = Math.Clamp(x, 0d, width - 1d);
        y = Math.Clamp(y, 0d, height - 1d);
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var x1 = Math.Min(width - 1, x0 + 1);
        var y1 = Math.Min(height - 1, y0 + 1);
        var fx = x - x0;
        var fy = y - y0;
        b = InterpolateChannel(pixels, width, x0, y0, x1, y1, fx, fy, 0);
        g = InterpolateChannel(pixels, width, x0, y0, x1, y1, fx, fy, 1);
        r = InterpolateChannel(pixels, width, x0, y0, x1, y1, fx, fy, 2);
        a = InterpolateChannel(pixels, width, x0, y0, x1, y1, fx, fy, 3);
    }

    private static byte InterpolateChannel(byte[] pixels, int width, int x0, int y0, int x1, int y1, double fx, double fy, int channel)
    {
        var p00 = pixels[checked((y0 * width + x0) * 4 + channel)];
        var p10 = pixels[checked((y0 * width + x1) * 4 + channel)];
        var p01 = pixels[checked((y1 * width + x0) * 4 + channel)];
        var p11 = pixels[checked((y1 * width + x1) * 4 + channel)];
        var top = p00 + (p10 - p00) * fx;
        var bottom = p01 + (p11 - p01) * fx;
        return (byte)Math.Clamp((int)Math.Round(top + (bottom - top) * fy), 0, 255);
    }

    private static byte Blend(byte destination, byte source, double alpha) =>
        (byte)Math.Clamp((int)Math.Round(destination + (source - destination) * alpha), 0, 255);

    private static void ValidateBuffer(byte[] bytes, int width, int height, string name)
    {
        var required = checked((long)width * height * 4L);
        if (required > int.MaxValue || bytes.LongLength != required)
            throw new ArgumentException("BGRA8 buffer length does not match dimensions.", name);
    }
}
