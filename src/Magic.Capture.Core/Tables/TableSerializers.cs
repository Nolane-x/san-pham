using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Magic.Capture.Core.Tables;

public enum TableNumberLocaleMode
{
    Preserve,
    Invariant,
    CurrentCulture
}

public sealed record TableDelimitedOptions(
    char Delimiter = ',',
    TableNumberLocaleMode NumberLocale = TableNumberLocaleMode.Preserve,
    bool ExcelSafeText = false);

public static class TableSerializers
{
    public const int MaximumRows = 10_000;
    public const int MaximumColumns = 512;
    public const int MaximumCells = 200_000;
    public const int MaximumCellCharacters = 100_000;
    public const int MaximumInputCharacters = 500_000;
    public const int MaximumOutputCharacters = 2_000_000;

    public static string ToCsv(DetectedTable table) =>
        ToDelimited(table, new TableDelimitedOptions(','));

    public static string ToTsv(DetectedTable table) =>
        ToDelimited(table, new TableDelimitedOptions('\t'));

    public static string ToExcelFriendlyTsv(DetectedTable table, CultureInfo? culture = null) =>
        ToDelimited(table, new TableDelimitedOptions('\t', TableNumberLocaleMode.CurrentCulture, true), culture);

    public static string ToDelimited(DetectedTable table, TableDelimitedOptions options, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Delimiter is '\r' or '\n' or '"')
            throw new ArgumentOutOfRangeException(nameof(options), "Delimiter cannot be a quote or line break.");

        ValidateInput(table);
        culture ??= CultureInfo.CurrentCulture;
        var builder = NewBuilder(table);
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            if (rowIndex > 0) AppendChecked(builder, "\r\n");
            var row = NormalizeRow(table.Rows[rowIndex], table.ColumnCount);
            for (var column = 0; column < row.Count; column++)
            {
                if (column > 0) AppendChecked(builder, options.Delimiter);
                var value = FormatDelimitedValue(row[column], options, culture);
                AppendChecked(builder, EscapeDelimited(value, options.Delimiter));
            }
        }
        return builder.ToString();
    }

    public static string ToMarkdown(DetectedTable table)
    {
        ValidateInput(table);
        if (table.Rows.Count == 0) return string.Empty;
        var builder = NewBuilder(table);
        AppendMarkdownRow(builder, table.Rows[0], table.ColumnCount);
        AppendChecked(builder, '|');
        for (var i = 0; i < table.ColumnCount; i++) AppendChecked(builder, " --- |");
        AppendChecked(builder, Environment.NewLine);
        foreach (var row in table.Rows.Skip(1)) AppendMarkdownRow(builder, row, table.ColumnCount);
        return builder.ToString().TrimEnd();
    }

    public static string ToHtml(DetectedTable table)
    {
        ValidateInput(table);
        var builder = NewBuilder(table);
        AppendChecked(builder, "<table>\n");
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var tag = rowIndex == 0 ? "th" : "td";
            AppendChecked(builder, "  <tr>");
            foreach (var value in NormalizeRow(table.Rows[rowIndex], table.ColumnCount))
            {
                AppendChecked(builder, "<" + tag + ">");
                AppendChecked(builder, WebUtility.HtmlEncode(value));
                AppendChecked(builder, "</" + tag + ">");
            }
            AppendChecked(builder, "</tr>\n");
        }
        AppendChecked(builder, "</table>");
        return builder.ToString();
    }

    public static string ToJson(DetectedTable table)
    {
        ValidateInput(table);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(table.Rows.Select(row => NormalizeRow(row, table.ColumnCount)).ToArray());
        if (bytes.Length > MaximumOutputCharacters)
            throw new InvalidOperationException($"Table output exceeds the supported {MaximumOutputCharacters:N0}-character budget.");
        return Encoding.UTF8.GetString(bytes);
    }

    private static string FormatDelimitedValue(string value, TableDelimitedOptions options, CultureInfo culture)
    {
        var normalized = value ?? string.Empty;
        if (options.NumberLocale != TableNumberLocaleMode.Preserve)
            normalized = ConvertNumericLocale(normalized, options.NumberLocale, culture);

        if (options.ExcelSafeText && IsFormulaLikeText(normalized, culture))
            normalized = "'" + normalized;
        return normalized;
    }

    private static string ConvertNumericLocale(string value, TableNumberLocaleMode mode, CultureInfo culture)
    {
        var kind = TableCellInference.Classify(value, culture);
        if (kind is not (TableCellKind.Integer or TableCellKind.Decimal or TableCellKind.Percent or TableCellKind.Currency))
            return value;

        var prefix = string.Empty;
        var suffix = string.Empty;
        var numeric = value.Trim();
        if (kind == TableCellKind.Percent)
        {
            suffix = "%";
            numeric = numeric.TrimEnd().TrimEnd('%').TrimEnd();
        }
        else if (kind == TableCellKind.Currency)
        {
            var stripped = TableCellInference.StripCurrency(numeric);
            var start = numeric.IndexOf(stripped, StringComparison.Ordinal);
            if (start >= 0)
            {
                prefix = numeric[..start];
                suffix = numeric[(start + stripped.Length)..] + suffix;
                numeric = stripped;
            }
        }

        if (!TableCellInference.TryParseDecimal(numeric, culture, out var number)) return value;
        var target = mode == TableNumberLocaleMode.Invariant ? CultureInfo.InvariantCulture : culture;
        var formatted = kind == TableCellKind.Integer
            ? decimal.Truncate(number).ToString("0", target)
            : number.ToString("0.############################", target);
        return prefix + formatted + suffix;
    }

    private static bool IsFormulaLikeText(string value, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var first = value.TrimStart()[0];
        if (first is not ('=' or '+' or '-' or '@')) return false;
        var kind = TableCellInference.Classify(value, culture);
        return kind == TableCellKind.Text;
    }

    private static string EscapeDelimited(string value, char delimiter)
    {
        if (value.Contains('"')) value = value.Replace("\"", "\"\"");
        return value.Contains(delimiter) || value.IndexOfAny(['"', '\r', '\n']) >= 0 ? $"\"{value}\"" : value;
    }

    private static void AppendMarkdownRow(StringBuilder builder, IReadOnlyList<string> row, int count)
    {
        AppendChecked(builder, '|');
        foreach (var value in NormalizeRow(row, count))
        {
            var escaped = NormalizeSingleLine(value).Replace("|", "\\|");
            AppendChecked(builder, " " + escaped + " |");
        }
        AppendChecked(builder, Environment.NewLine);
    }

    private static string NormalizeSingleLine(string value) => value.Replace("\r", " ").Replace("\n", " ");

    private static IReadOnlyList<string> NormalizeRow(IReadOnlyList<string> row, int count)
    {
        if (row.Count == count) return row;
        var normalized = new string[count];
        for (var i = 0; i < count; i++) normalized[i] = i < row.Count ? row[i] : string.Empty;
        return normalized;
    }

    private static StringBuilder NewBuilder(DetectedTable table) =>
        new(Math.Min(MaximumOutputCharacters, Math.Max(256, Math.Min(MaximumInputCharacters, table.Rows.Sum(row => row.Sum(cell => Math.Min(cell?.Length ?? 0, MaximumCellCharacters)))) + table.Rows.Count * 4)));

    private static void ValidateInput(DetectedTable table)
    {
        if (table.ColumnCount < 0 || table.ColumnCount > MaximumColumns || (table.Rows.Count > 0 && table.ColumnCount == 0))
            throw new InvalidOperationException($"Table column count must be between 1 and {MaximumColumns:N0} when rows are present.");
        if (table.Rows.Count > MaximumRows)
            throw new InvalidOperationException($"Table row count exceeds the supported limit of {MaximumRows:N0}.");

        long cells = 0;
        long characters = 0;
        foreach (var row in table.Rows)
        {
            if (row.Count > MaximumColumns || row.Count > table.ColumnCount)
                throw new InvalidOperationException("Table row contains more cells than the declared column count.");
            cells += row.Count;
            if (cells > MaximumCells)
                throw new InvalidOperationException($"Table contains more than {MaximumCells:N0} cells.");
            foreach (var cell in row)
            {
                var length = cell?.Length ?? 0;
                if (length > MaximumCellCharacters)
                    throw new InvalidOperationException($"A table cell exceeds the supported {MaximumCellCharacters:N0}-character limit.");
                characters += length;
                if (characters > MaximumInputCharacters)
                    throw new InvalidOperationException($"Table text exceeds the supported {MaximumInputCharacters:N0}-character budget.");
            }
        }
    }

    private static void AppendChecked(StringBuilder builder, char value)
    {
        if (builder.Length >= MaximumOutputCharacters) ThrowOutputTooLarge();
        builder.Append(value);
    }

    private static void AppendChecked(StringBuilder builder, string value)
    {
        if (value.Length > MaximumOutputCharacters - builder.Length) ThrowOutputTooLarge();
        builder.Append(value);
    }

    private static void ThrowOutputTooLarge() =>
        throw new InvalidOperationException($"Table output exceeds the supported {MaximumOutputCharacters:N0}-character budget.");
}
