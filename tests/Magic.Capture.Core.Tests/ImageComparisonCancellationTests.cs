using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Tests;

public sealed class ImageComparisonCancellationTests
{
    [Fact]
    public void Difference_HonorsPreCanceledToken()
    {
        var first = new byte[4 * 16];
        var second = new byte[first.Length];
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => ImageDifference.AnalyzeBgra(first, second, cancellationToken: cts.Token));
    }

    [Fact]
    public void Metrics_HonorPreCanceledToken()
    {
        var first = new byte[4 * 16];
        var second = new byte[first.Length];
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => ImageComparisonMetrics.CalculateBgra(first, second, cts.Token));
    }

    [Fact]
    public void TranslationAlignment_HonorsPreCanceledToken()
    {
        var first = new byte[4 * 4 * 4];
        var second = new byte[first.Length];
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => TranslationAlignment.FindBestBgra(first, second, 4, 4, cancellationToken: cts.Token));
    }
}
