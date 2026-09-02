using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Tests;

public sealed class ImageComparisonMetricsTests
{
    [Fact]
    public void Identical_images_have_zero_mse_infinite_psnr_and_ssim_one()
    {
        byte[] pixels = [0, 10, 20, 30, 40, 50];
        var result = ImageComparisonMetrics.Calculate(pixels, pixels);
        Assert.Equal(0, result.MeanSquaredError);
        Assert.True(double.IsPositiveInfinity(result.PeakSignalToNoiseRatio));
        Assert.Equal(1, result.StructuralSimilarity, 12);
    }

    [Fact]
    public void Different_images_report_error_and_lower_similarity()
    {
        byte[] a = [0, 0, 0, 0];
        byte[] b = [255, 255, 255, 255];
        var result = ImageComparisonMetrics.Calculate(a, b);
        Assert.Equal(65025, result.MeanSquaredError);
        Assert.True(result.StructuralSimilarity < .01);
    }

    [Fact]
    public void Invalid_buffers_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ImageComparisonMetrics.Calculate([1], [1, 2]));
        Assert.Throws<ArgumentException>(() => ImageComparisonMetrics.Calculate([], []));
    }

    [Fact]
    public void Bgra_metrics_ignore_alpha_channel_without_allocating_rgb_copy()
    {
        byte[] a = [0, 0, 0, 0, 10, 20, 30, 255];
        byte[] b = [0, 0, 0, 255, 10, 20, 30, 0];
        var result = ImageComparisonMetrics.CalculateBgra(a, b);
        Assert.Equal(0, result.MeanSquaredError);
        Assert.True(double.IsPositiveInfinity(result.PeakSignalToNoiseRatio));
        Assert.Equal(1, result.StructuralSimilarity, 12);
    }
}
