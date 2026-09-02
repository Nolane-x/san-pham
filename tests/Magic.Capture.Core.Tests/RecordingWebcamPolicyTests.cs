using Magic.Capture.Core.Recording;

namespace Magic.Capture.Core.Tests;

public sealed class RecordingWebcamPolicyTests
{
    [Fact]
    public void Normalize_ClampsWebcamOptions()
    {
        var normalized = RecordingRules.Normalize(new RecordingOptions(
            IncludeWebcam: true,
            WebcamDeviceId: " camera-1 ",
            WebcamXPercent: -4,
            WebcamYPercent: 145,
            WebcamWidthPercent: 99,
            WebcamOpacityPercent: 3,
            WebcamBorderPixels: 99));

        Assert.True(normalized.IncludeWebcam);
        Assert.Equal("camera-1", normalized.WebcamDeviceId);
        Assert.Equal(0, normalized.WebcamXPercent);
        Assert.Equal(100, normalized.WebcamYPercent);
        Assert.Equal(50, normalized.WebcamWidthPercent);
        Assert.Equal(20, normalized.WebcamOpacityPercent);
        Assert.Equal(12, normalized.WebcamBorderPixels);
    }

    [Fact]
    public void Normalize_RejectsOversizedWebcamDeviceId()
    {
        var id = new string('x', RecordingWebcamPolicy.MaximumDeviceIdLength + 1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecordingRules.Normalize(new RecordingOptions(IncludeWebcam: true, WebcamDeviceId: id)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 0)]
    [InlineData(0, 100)]
    [InlineData(100, 100)]
    public void ComputeOverlayRect_AlwaysStaysInsideOutput(int xPercent, int yPercent)
    {
        var rect = RecordingWebcamPolicy.ComputeOverlayRect(
            outputWidth: 1920,
            outputHeight: 1080,
            cameraWidth: 1280,
            cameraHeight: 720,
            xPercent,
            yPercent,
            widthPercent: 25);

        Assert.True(rect.Width > 0 && rect.Height > 0);
        Assert.InRange(rect.X, 0, 1920 - rect.Width);
        Assert.InRange(rect.Y, 0, 1080 - rect.Height);
        Assert.True(rect.Right <= 1920);
        Assert.True(rect.Bottom <= 1080);
    }

    [Fact]
    public void Compositor_CircleMaskLeavesCornerUntouched()
    {
        var canvas = SolidBgra(8, 8, b: 10, g: 20, r: 30, a: 255);
        var camera = SolidBgra(4, 4, b: 100, g: 110, r: 120, a: 255);
        var rect = new WebcamOverlayRect(2, 2, 4, 4);

        BgraWebcamCompositor.CompositeInPlace(
            canvas, 8, 8,
            camera, 4, 4,
            rect,
            WebcamOverlayShape.Circle,
            mirror: false,
            opacityPercent: 100,
            borderPixels: 0);

        AssertPixel(canvas, 8, 2, 2, 10, 20, 30, 255);
        AssertPixel(canvas, 8, 4, 4, 100, 110, 120, 255);
    }

    [Fact]
    public void Compositor_MirrorFlipsSourceHorizontally()
    {
        var canvas = SolidBgra(2, 1, 0, 0, 0, 255);
        var camera = new byte[]
        {
            1, 2, 3, 255,
            10, 20, 30, 255
        };

        BgraWebcamCompositor.CompositeInPlace(
            canvas, 2, 1,
            camera, 2, 1,
            new WebcamOverlayRect(0, 0, 2, 1),
            WebcamOverlayShape.Rectangle,
            mirror: true,
            opacityPercent: 100,
            borderPixels: 0);

        AssertPixel(canvas, 2, 0, 0, 10, 20, 30, 255);
        AssertPixel(canvas, 2, 1, 0, 1, 2, 3, 255);
    }

    [Fact]
    public void Compositor_OpacityBlendsWithCanvas()
    {
        var canvas = SolidBgra(1, 1, 0, 0, 0, 255);
        var camera = SolidBgra(1, 1, 200, 100, 50, 255);

        BgraWebcamCompositor.CompositeInPlace(
            canvas, 1, 1,
            camera, 1, 1,
            new WebcamOverlayRect(0, 0, 1, 1),
            WebcamOverlayShape.Rectangle,
            mirror: false,
            opacityPercent: 50,
            borderPixels: 0);

        Assert.InRange(canvas[0], (byte)99, (byte)101);
        Assert.InRange(canvas[1], (byte)49, (byte)51);
        Assert.InRange(canvas[2], (byte)24, (byte)26);
        Assert.Equal(255, canvas[3]);
    }

    private static byte[] SolidBgra(int width, int height, byte b, byte g, byte r, byte a)
    {
        var bytes = new byte[checked(width * height * 4)];
        for (var i = 0; i < width * height; i++)
        {
            var o = i * 4;
            bytes[o] = b;
            bytes[o + 1] = g;
            bytes[o + 2] = r;
            bytes[o + 3] = a;
        }
        return bytes;
    }

    private static void AssertPixel(byte[] bytes, int width, int x, int y, byte b, byte g, byte r, byte a)
    {
        var o = checked((y * width + x) * 4);
        Assert.Equal(b, bytes[o]);
        Assert.Equal(g, bytes[o + 1]);
        Assert.Equal(r, bytes[o + 2]);
        Assert.Equal(a, bytes[o + 3]);
    }
}
