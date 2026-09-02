using System.Globalization;

namespace Magic.Capture.Core.Tables;

public enum TableCellKind
{
    Empty,
    Text,
    Integer,
    Decimal,
    Date,
    Currency,
    Percent
}

public sealed record TableColumnInference(int Index, TableCellKind DominantKind, double Confidence);

public sealed record TableAnomaly(
    int RowIndex,
    int ColumnIndex,
    string Value,
    TableCellKind ExpectedKind,
    TableCellKind ActualKind);

public sealed record TableSchemaInference(
    bool HasHeader,
    IReadOnlyList<TableColumnInference> Columns,
    IReadOnlyList<TableAnomaly> Anomalies);

public static class TableCellInference
{
    public const int MaximumInspectedCells = 20_000;
    public const int MaximumAnomalies = 256;
    public const int MaximumCellCharacters = 4_096;

    private static readonly char[] CurrencySymbols = ['$','€','£','¥','₫','₹','₩','₽','₺','₴','₪'];

    public static TableCellKind Classify(string? value, CultureInfo? culture = null)
    {
        var text = Normalize(value);
        if (text.Length == 0) return TableCellKind.Empty;
        culture ??= CultureInfo.CurrentCulture;

        if (text.EndsWith('%') && TryParseDecimal(text[..^1], culture, out _))
            return TableCellKind.Percent;

        if (LooksLikeCurrency(text) && TryParseDecimal(StripCurrency(text), culture, out _))
            return TableCellKind.Currency;

        if (LooksLikeDate(text) && TryParseDate(text, culture, out _))
            return TableCellKind.Date;

        if (TryParseInteger(text, culture, out _))
            return TableCellKind.Integer;

        if (TryParseDecimal(text, culture, out _))
            return TableCellKind.Decimal;

        return TableCellKind.Text;
    }

    public static TableSchemaInference Infer(DetectedTable table, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        culture ??= CultureInfo.CurrentCulture;
        if (table.ColumnCount <= 0 || table.Rows.Count == 0)
            return new TableSchemaInference(false, [], []);

        var columns = Math.Min(table.ColumnCount, 512);
        var rows = Math.Min(table.Rows.Count, Math.Max(1, MaximumInspectedCells / columns));
        var hasHeader = DetectHeader(table, rows, columns, culture);
        var dataStart = hasHeader && rows > 1 ? 1 : 0;
        var inferred = new List<TableColumnInference>(columns);

        for (var column = 0; column < columns; column++)
        {
            var kinds = new List<TableCellKind>();
            for (var row = dataStart; row < rows; row++)
            {
                var kind = Classify(CellAt(table, row, column), culture);
                if (kind != TableCellKind.Empty) kinds.Add(kind);
            }

            inferred.Add(InferColumn(column, kinds));
        }

        var anomalies = new List<TableAnomaly>();
        for (var row = dataStart; row < rows && anomalies.Count < MaximumAnomalies; row++)
        {
            for (var column = 0; column < columns && anomalies.Count < MaximumAnomalies; column++)
            {
                var expected = inferred[column];
                if (expected.Confidence < 0.70 || expected.DominantKind is TableCellKind.Empty or TableCellKind.Text)
                    continue;

                var value = CellAt(table, row, column);
                var actual = Classify(value, culture);
                if (actual == TableCellKind.Empty || IsCompatible(expected.DominantKind, actual))
                    continue;

                anomalies.Add(new TableAnomaly(row, column, Normalize(value), expected.DominantKind, actual));
            }
        }

        return new TableSchemaInference(hasHeader, inferred, anomalies);
    }

    internal static bool TryParseDecimal(string value, CultureInfo culture, out decimal number)
    {
        var text = value.Trim();
        var styles = NumberStyles.Number | NumberStyles.AllowLeadingSign;
        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
        var preferInvariant = text.Contains('.')
            && !string.Equals(decimalSeparator, ".", StringComparison.Ordinal)
            && !text.Contains(decimalSeparator, StringComparison.Ordinal);

        if (preferInvariant && decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out number))
            return true;
        if (decimal.TryParse(text, styles, culture, out number))
            return true;
        return decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out number);
    }

    internal static string StripCurrency(string value)
    {
        var text = value.Trim();
        while (text.Length > 0 && (CurrencySymbols.Contains(text[0]) || char.IsWhiteSpace(text[0]))) text = text[1..];
        while (text.Length > 0 && (CurrencySymbols.Contains(text[^1]) || char.IsWhiteSpace(text[^1]))) text = text[..^1];
        return text.Trim();
    }

    private static TableColumnInference InferColumn(int index, IReadOnlyList<TableCellKind> kinds)
    {
        if (kinds.Count == 0) return new TableColumnInference(index, TableCellKind.Empty, 1);
        var numericCount = kinds.Count(kind => kind is TableCellKind.Integer or TableCellKind.Decimal);
        var candidates = kinds
            .Where(kind => kind is not (TableCellKind.Integer or TableCellKind.Decimal))
            .GroupBy(kind => kind)
            .Select(group => (Kind: group.Key, Count: group.Count()))
            .ToList();
        if (numericCount > 0)
        {
            var numericKind = kinds.Contains(TableCellKind.Decimal) ? TableCellKind.Decimal : TableCellKind.Integer;
            candidates.Add((numericKind, numericCount));
        }

        var best = candidates.OrderByDescending(item => item.Count).ThenBy(item => item.Kind).First();
        return new TableColumnInference(index, best.Kind, best.Count / (double)kinds.Count);
    }

    private static bool DetectHeader(DetectedTable table, int rows, int columns, CultureInfo culture)
    {
        if (rows < 2) return false;
        var typedEvidence = 0;
        for (var column = 0; column < columns; column++)
        {
            var firstKind = Classify(CellAt(table, 0, column), culture);
            if (firstKind != TableCellKind.Text) continue;

            var dataKinds = new List<TableCellKind>();
            for (var row = 1; row < rows; row++)
            {
                var kind = Classify(CellAt(table, row, column), culture);
                if (kind != TableCellKind.Empty) dataKinds.Add(kind);
            }
            if (dataKinds.Count == 0) continue;
            var dominant = InferColumn(column, dataKinds);
            if (dominant.Confidence >= 0.60 && dominant.DominantKind is not (TableCellKind.Text or TableCellKind.Empty))
                typedEvidence++;
        }

        if (typedEvidence == 0) return false;
        return typedEvidence >= Math.Max(1, (int)Math.Ceiling(columns * 0.50));
    }

    private static bool LooksLikeCurrency(string value) =>
        value.Any(ch => CurrencySymbols.Contains(ch));

    private static bool LooksLikeDate(string value)
    {
        if (value.Length is < 4 or > 64) return false;
        if (value.Any(char.IsLetter) && value.Any(char.IsDigit)) return true;
        var separators = value.Count(ch => ch is '/' or '-' or '.');
        return separators >= 2 && value.Any(char.IsDigit);
    }

    private static bool TryParseDate(string value, CultureInfo culture, out DateTime date)
    {
        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces;
        if (DateTime.TryParse(value, culture, styles, out date)) return true;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, styles, out date);
    }

    private static bool TryParseInteger(string value, CultureInfo culture, out long number)
    {
        if (LooksLikeFractionalNumber(value, culture))
        {
            number = default;
            return false;
        }

        var styles = NumberStyles.Integer | NumberStyles.AllowThousands;
        if (long.TryParse(value, styles, culture, out number)) return true;
        return long.TryParse(value, styles, CultureInfo.InvariantCulture, out number);
    }

    private static bool LooksLikeFractionalNumber(string value, CultureInfo culture)
    {
        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
        if (!string.IsNullOrEmpty(decimalSeparator) && value.Contains(decimalSeparator, StringComparison.Ordinal))
            return true;

        return !string.Equals(decimalSeparator, ".", StringComparison.Ordinal)
            && value.Contains('.', StringComparison.Ordinal)
            && !value.Contains(decimalSeparator, StringComparison.Ordinal);
    }

    private static bool IsCompatible(TableCellKind expected, TableCellKind actual) =>
        expected == actual || (expected == TableCellKind.Decimal && actual == TableCellKind.Integer);

    private static string CellAt(DetectedTable table, int row, int column) =>
        row >= 0 && row < table.Rows.Count && column >= 0 && column < table.Rows[row].Count
            ? table.Rows[row][column]
            : string.Empty;

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        return trimmed.Length <= MaximumCellCharacters ? trimmed : trimmed[..MaximumCellCharacters];
    }
}
