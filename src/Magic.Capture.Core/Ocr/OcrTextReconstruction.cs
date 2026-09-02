using System.Text;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Ocr;

public enum OcrTextReconstructionMode { Plain, Layout, Code }

public static class OcrTextReconstruction
{
    public const int MaximumOutputCharacters = 1_000_000;
    public const int MaximumLineCharacters = 4_096;
    private const int MaximumLines = OcrSpatialIndex.MaximumLines;
    private const int MaximumCharacterWidthSamples = 1_024;

    public static string Build(OcrDocument? document, OcrTextReconstructionMode mode)
    {
        if (document is null) return string.Empty;
        return mode switch
        {
            OcrTextReconstructionMode.Layout => BuildLayout(document),
            OcrTextReconstructionMode.Code => BuildCode(document),
            _ => Bound(document.Text)
        };
    }

    private static string BuildLayout(OcrDocument document)
    {
        var lines = ValidLines(document).ToArray();
        if (lines.Length == 0) return Bound(document.Text);
        var medianHeight = Median(lines.Select(line => Math.Max(1, line.Bounds.Height)));
        var builder = new StringBuilder(Math.Min(MaximumOutputCharacters, Math.Max(256, document.Text.Length + lines.Length * 2)));
        OcrLine? previous = null;
        foreach (var line in lines)
        {
            if (previous is not null)
            {
                var gap = Math.Max(0, line.Bounds.Y - previous.Bounds.Bottom);
                AppendNewLine(builder);
                if (gap >= Math.Max(10, medianHeight * 1.35)) AppendNewLine(builder);
            }
            AppendBounded(builder, line.Text.Trim(), MaximumLineCharacters);
            previous = line;
            if (builder.Length >= MaximumOutputCharacters) break;
        }
        return builder.ToString().TrimEnd();
    }

    private static string BuildCode(OcrDocument document)
    {
        var lines = ValidLines(document).ToArray();
        if (lines.Length == 0) return Bound(document.Text);
        var characterWidth = InferCharacterWidth(lines);
        var leftOrigin = lines.SelectMany(line => line.Words)
            .Where(word => !word.Bounds.IsEmpty && !string.IsNullOrWhiteSpace(word.Text))
            .Select(word => word.Bounds.X)
            .DefaultIfEmpty(lines.Min(line => line.Bounds.X))
            .Min();
        var builder = new StringBuilder(Math.Min(MaximumOutputCharacters, Math.Max(256, document.Text.Length + lines.Length * 8)));

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (lineIndex != 0) AppendNewLine(builder);
            var words = lines[lineIndex].Words
                .Where(word => !word.Bounds.IsEmpty && !string.IsNullOrWhiteSpace(word.Text))
                .OrderBy(word => word.Bounds.X)
                .Take(OcrSpatialIndex.MaximumWords)
                .ToArray();
            if (words.Length == 0)
            {
                AppendBounded(builder, lines[lineIndex].Text.Trim(), MaximumLineCharacters);
                continue;
            }

            var lineStart = builder.Length;
            foreach (var word in words)
            {
                var text = NormalizeInline(word.Text);
                if (text.Length == 0) continue;
                var desiredColumn = Math.Max(0, (int)Math.Round((word.Bounds.X - leftOrigin) / characterWidth));
                var currentColumn = builder.Length - lineStart;
                if (currentColumn > 0 && desiredColumn <= currentColumn) desiredColumn = currentColumn + 1;
                var spaces = Math.Clamp(desiredColumn - currentColumn, 0, MaximumLineCharacters - currentColumn);
                if (spaces > 0) builder.Append(' ', spaces);
                var remaining = MaximumLineCharacters - (builder.Length - lineStart);
                if (remaining <= 0) break;
                AppendBounded(builder, text, remaining);
                if (builder.Length >= MaximumOutputCharacters) break;
            }
            if (builder.Length >= MaximumOutputCharacters) break;
        }
        return builder.ToString().TrimEnd();
    }

    private static IEnumerable<OcrLine> ValidLines(OcrDocument document) => document.Lines
        .Take(MaximumLines)
        .Where(line => !line.Bounds.IsEmpty && !string.IsNullOrWhiteSpace(line.Text))
        .OrderBy(line => line.Bounds.Y)
        .ThenBy(line => line.Bounds.X);

    private static double InferCharacterWidth(IReadOnlyList<OcrLine> lines)
    {
        var samples = lines
            .SelectMany(line => line.Words)
            .Where(word => !word.Bounds.IsEmpty && !string.IsNullOrWhiteSpace(word.Text))
            .Select(word => word.Bounds.Width / (double)Math.Max(1, NormalizeInline(word.Text).Length))
            .Where(width => double.IsFinite(width) && width is >= 2 and <= 80)
            .Take(MaximumCharacterWidthSamples)
            .Order()
            .ToArray();
        if (samples.Length == 0) return 8d;
        var middle = samples.Length / 2;
        return Math.Clamp(samples.Length % 2 == 0 ? (samples[middle - 1] + samples[middle]) / 2d : samples[middle], 3d, 40d);
    }

    private static int Median(IEnumerable<int> values)
    {
        var sorted = values.Take(MaximumLines).Order().ToArray();
        if (sorted.Length == 0) return 1;
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }

    private static string NormalizeInline(string value) => string.Join(' ', value
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Bound(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= MaximumOutputCharacters ? value : value[..MaximumOutputCharacters];
    }

    private static void AppendBounded(StringBuilder builder, string value, int perLineLimit)
    {
        if (builder.Length >= MaximumOutputCharacters || perLineLimit <= 0 || value.Length == 0) return;
        var count = Math.Min(value.Length, Math.Min(perLineLimit, MaximumOutputCharacters - builder.Length));
        builder.Append(value, 0, count);
    }

    private static void AppendNewLine(StringBuilder builder)
    {
        if (builder.Length + 2 <= MaximumOutputCharacters) builder.Append("\r\n");
    }
}
