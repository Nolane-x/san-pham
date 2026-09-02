using Magic.Capture.Core.VideoEditing;

namespace Magic.Capture.Core.Tests;

public sealed class VideoEditExportPolicyTests
{
    [Theory]
    [InlineData(VideoEditAudioFormat.Wav, ".wav")]
    [InlineData(VideoEditAudioFormat.Mp3, ".mp3")]
    [InlineData(VideoEditAudioFormat.M4a, ".m4a")]
    public void AudioFormat_UsesExpectedExtension(VideoEditAudioFormat format, string expected) =>
        Assert.Equal(expected, VideoEditExportPolicy.Extension(format));

    [Theory]
    [InlineData(VideoEditVideoFormat.H264Mp4, ".mp4")]
    [InlineData(VideoEditVideoFormat.HevcMp4, ".mp4")]
    [InlineData(VideoEditVideoFormat.Wmv, ".wmv")]
    public void VideoFormat_UsesExpectedExtension(VideoEditVideoFormat format, string expected) =>
        Assert.Equal(expected, VideoEditExportPolicy.Extension(format));

    [Fact]
    public void ValidateOutputPath_RejectsWrongExtension()
    {
        Assert.Throws<ArgumentException>(() => VideoEditExportPolicy.ValidateOutputPath(@"C:\out.mp3", VideoEditAudioFormat.Wav));
        Assert.Throws<ArgumentException>(() => VideoEditExportPolicy.ValidateOutputPath(@"C:\out.wmv", VideoEditVideoFormat.H264Mp4));
    }
}
