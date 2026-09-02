using Magic.Capture.Core.Recording;
using Windows.Storage.Streams;

namespace Magic.Capture.App.Recording;

internal static class RecordingWebcamCompositor
{
    public static IBuffer Composite(RecordingFramePixels canvas, RecordingWebcamFrame webcam, RecordingOptions options)
    {
        CompositeInPlace(canvas, webcam, options);
        return RecordingFrameDecoder.ToBuffer(canvas.BgraBytes);
    }

    public static void CompositeInPlace(RecordingFramePixels canvas, RecordingWebcamFrame webcam, RecordingOptions options)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(webcam);
        options = RecordingRules.Normalize(options);
        var destination = RecordingWebcamPolicy.ComputeOverlayRect(
            canvas.Width, canvas.Height, webcam.Width, webcam.Height,
            options.WebcamXPercent, options.WebcamYPercent, options.WebcamWidthPercent, options.WebcamShape);

        BgraWebcamCompositor.CompositeInPlace(
            canvas.BgraBytes, canvas.Width, canvas.Height, webcam.BgraBytes, webcam.Width, webcam.Height, destination,
            options.WebcamShape, options.MirrorWebcam, options.WebcamOpacityPercent, options.WebcamBorderPixels);
    }
}
