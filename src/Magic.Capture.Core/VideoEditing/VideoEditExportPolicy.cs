namespace Magic.Capture.Core.VideoEditing;

public enum VideoEditAudioFormat
{
    Wav,
    Mp3,
    M4a
}

public enum VideoEditVideoFormat
{
    H264Mp4,
    HevcMp4,
    Wmv
}

public static class VideoEditExportPolicy
{
    public static string Extension(VideoEditAudioFormat format) => format switch
    {
        VideoEditAudioFormat.Wav => ".wav",
        VideoEditAudioFormat.Mp3 => ".mp3",
        VideoEditAudioFormat.M4a => ".m4a",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static string Extension(VideoEditVideoFormat format) => format switch
    {
        VideoEditVideoFormat.H264Mp4 => ".mp4",
        VideoEditVideoFormat.HevcMp4 => ".mp4",
        VideoEditVideoFormat.Wmv => ".wmv",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static void ValidateOutputPath(string path, VideoEditAudioFormat format)
    {
        ValidatePath(path, Extension(format), "audio");
    }

    public static void ValidateOutputPath(string path, VideoEditVideoFormat format)
    {
        ValidatePath(path, Extension(format), "video");
    }

    private static void ValidatePath(string path, string extension, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException($"{kind} output path must be fully qualified.", nameof(path));
        if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{kind} output path must end with '{extension}'.", nameof(path));
    }
}
