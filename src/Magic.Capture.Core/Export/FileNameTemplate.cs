namespace Magic.Capture.Core.Export;

public static class FileNameTemplate
{
    public static string Render(string? template, DateTimeOffset timestamp, int counter)
    {
        var value = string.IsNullOrWhiteSpace(template)
            ? "Magic Capture Desktop_{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}"
            : template;

        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{date}"] = timestamp.ToString("yyyy-MM-dd"),
            ["{time}"] = timestamp.ToString("HH-mm-ss"),
            ["{yyyy}"] = timestamp.ToString("yyyy"),
            ["{MM}"] = timestamp.ToString("MM"),
            ["{dd}"] = timestamp.ToString("dd"),
            ["{HH}"] = timestamp.ToString("HH"),
            ["{mm}"] = timestamp.ToString("mm"),
            ["{ss}"] = timestamp.ToString("ss"),
            ["{counter}"] = counter.ToString()
        };

        foreach (var pair in replacements) value = value.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        return SafeFileName.Sanitize(value);
    }
}
