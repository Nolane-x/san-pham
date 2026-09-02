namespace Magic.Capture.Core.Imaging;

public sealed record TranslationAlignmentResult(
    int OffsetX,
    int OffsetY,
    double MeanAbsoluteError,
    long ComparedSamples)
{
    public int EvaluatedOffsetCount { get; init; }
}

public static class TranslationAlignment
{
    public static TranslationAlignmentResult FindBestBgra(
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second,
        int width,
        int height,
        int maxOffset = 24,
        int sampleStep = 4,
        CancellationToken cancellationToken = default)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var expected = checked(width * height * 4);
        if (first.Length != expected || second.Length != expected)
            throw new ArgumentException("BGRA buffer lengths must match width × height × 4.");

        cancellationToken.ThrowIfCancellationRequested();
        maxOffset = Math.Clamp(maxOffset, 0, 64);
        sampleStep = Math.Clamp(sampleStep, 1, 16);

        // Keep the amount of pixel work per candidate bounded on large screenshots. Tiny images
        // preserve the caller's requested sampling granularity for exact/unit-test behavior.
        var pixelCount = checked((long)width * height);
        var adaptiveMinimumStep = pixelCount <= 250_000
            ? 1
            : (int)Math.Ceiling(Math.Sqrt(pixelCount / 250_000d));
        var fineSampleStep = Math.Clamp(Math.Max(sampleStep, adaptiveMinimumStep), 1, 32);

        var best = new TranslationAlignmentResult(0, 0, double.PositiveInfinity, 0);
        var bestDistance = int.MaxValue;
        var evaluated = 0;

        static void Consider(
            ReadOnlySpan<byte> first,
            ReadOnlySpan<byte> second,
            int width,
            int height,
            int maxOffset,
            long pixelCount,
            CancellationToken cancellationToken,
            int offsetX,
            int offsetY,
            int evaluationSampleStep,
            ref TranslationAlignmentResult best,
            ref int bestDistance,
            ref int evaluated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (offsetX < -maxOffset || offsetX > maxOffset || offsetY < -maxOffset || offsetY > maxOffset) return;

            var xStart = Math.Max(0, offsetX);
            var yStart = Math.Max(0, offsetY);
            var xEnd = Math.Min(width, width + offsetX);
            var yEnd = Math.Min(height, height + offsetY);
            if (xEnd <= xStart || yEnd <= yStart) return;

            // Never let a candidate win merely by shifting difficult content out of the overlap.
            // UI auto-align searches only small translations, so retaining at least 70% of the
            // original canvas is both safe for real screenshots and robust for small images.
            var overlapPixels = checked((long)(xEnd - xStart) * (yEnd - yStart));
            if (overlapPixels * 100 < pixelCount * 70) return;

            long samples = 0;
            long sum = 0;
            var phaseCount = evaluationSampleStep > 1 ? 2 : 1;
            for (var phaseIndex = 0; phaseIndex < phaseCount; phaseIndex++)
            {
                var phase = phaseIndex == 0 ? 0 : evaluationSampleStep / 2;
                for (var y = yStart + phase; y < yEnd; y += evaluationSampleStep)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sourceY = y - offsetY;
                    for (var x = xStart + phase; x < xEnd; x += evaluationSampleStep)
                    {
                        var sourceX = x - offsetX;
                        var a = (y * width + x) * 4;
                        var b = (sourceY * width + sourceX) * 4;
                        sum += Math.Abs(first[a] - second[b]);
                        sum += Math.Abs(first[a + 1] - second[b + 1]);
                        sum += Math.Abs(first[a + 2] - second[b + 2]);
                        samples++;
                    }
                }
            }
            if (samples == 0) return;

            evaluated++;
            var error = sum / (samples * 3d);
            var distance = Math.Abs(offsetX) + Math.Abs(offsetY);
            if (error < best.MeanAbsoluteError - 1e-9 ||
                (Math.Abs(error - best.MeanAbsoluteError) <= 1e-9 && distance < bestDistance))
            {
                best = new TranslationAlignmentResult(offsetX, offsetY, error, samples)
                {
                    EvaluatedOffsetCount = evaluated
                };
                bestDistance = distance;
            }
        }

        if (maxOffset == 0)
        {
            Consider(first, second, width, height, maxOffset, pixelCount, cancellationToken,
                0, 0, fineSampleStep, ref best, ref bestDistance, ref evaluated);
            return best with { EvaluatedOffsetCount = evaluated };
        }

        // Small searches are cheap enough to remain exhaustive. Larger searches use a coarse
        // full-range pass, then progressively refine around the current best candidate. For the
        // UI's ±32 px search this reduces 4,225 full candidate evaluations to a few hundred.
        var searchStep = 1;
        while (searchStep * 8 < maxOffset) searchStep *= 2;

        var firstPass = true;
        while (searchStep >= 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var centerX = firstPass ? 0 : best.OffsetX;
            var centerY = firstPass ? 0 : best.OffsetY;
            var radius = firstPass ? maxOffset : Math.Min(maxOffset, searchStep * 2);
            var axisX = BuildAxisCandidates(centerX, radius, searchStep, maxOffset);
            var axisY = BuildAxisCandidates(centerY, radius, searchStep, maxOffset);
            var stageSampleStep = searchStep == 1
                ? fineSampleStep
                : Math.Clamp(fineSampleStep * 2, fineSampleStep, 32);

            foreach (var offsetY in axisY)
            foreach (var offsetX in axisX)
                Consider(first, second, width, height, maxOffset, pixelCount, cancellationToken,
                    offsetX, offsetY, stageSampleStep, ref best, ref bestDistance, ref evaluated);

            firstPass = false;
            if (searchStep == 1) break;
            searchStep /= 2;
        }

        return best.ComparedSamples == 0
            ? new TranslationAlignmentResult(0, 0, 0, 0) { EvaluatedOffsetCount = evaluated }
            : best with { EvaluatedOffsetCount = evaluated };
    }

    private static IReadOnlyList<int> BuildAxisCandidates(int center, int radius, int step, int maxOffset)
    {
        var min = Math.Max(-maxOffset, center - radius);
        var max = Math.Min(maxOffset, center + radius);
        var values = new SortedSet<int>();
        for (var value = min; value <= max; value = checked(value + step))
        {
            values.Add(value);
            if (value > max - step) break;
        }
        values.Add(min);
        values.Add(max);
        values.Add(Math.Clamp(center, -maxOffset, maxOffset));
        if (min <= 0 && max >= 0) values.Add(0);
        return values.ToArray();
    }
}
