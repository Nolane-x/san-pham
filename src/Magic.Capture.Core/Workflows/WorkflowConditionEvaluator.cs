using System.Globalization;

namespace Magic.Capture.Core.Workflows;

public static class WorkflowConditionEvaluator
{
    public static bool Evaluate(string? expression, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (string.IsNullOrWhiteSpace(expression)) return true;

        foreach (var rawClause in expression.Split("&&", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!EvaluateClause(rawClause, values)) return false;
        }
        return true;
    }

    private static bool EvaluateClause(string clause, IReadOnlyDictionary<string, object?> values)
    {
        var parts = clause.Split(':', 3, StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;
        var op = parts[0].ToLowerInvariant();
        var key = parts[1];
        var exists = TryGet(values, key, out var value) && value is not null;

        return op switch
        {
            "exists" => exists,
            "not-exists" => !exists,
            "equals" when parts.Length == 3 => exists && string.Equals(ToText(value), parts[2], StringComparison.OrdinalIgnoreCase),
            "contains" when parts.Length == 3 => exists && ToText(value).Contains(parts[2], StringComparison.OrdinalIgnoreCase),
            "gt" when parts.Length == 3 => CompareNumber(value, parts[2], (a, b) => a > b),
            "gte" when parts.Length == 3 => CompareNumber(value, parts[2], (a, b) => a >= b),
            "lt" when parts.Length == 3 => CompareNumber(value, parts[2], (a, b) => a < b),
            "lte" when parts.Length == 3 => CompareNumber(value, parts[2], (a, b) => a <= b),
            _ => false
        };
    }

    private static bool TryGet(IReadOnlyDictionary<string, object?> values, string key, out object? value)
    {
        if (values.TryGetValue(key, out value)) return true;
        foreach (var pair in values)
        {
            if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
            value = pair.Value;
            return true;
        }
        value = null;
        return false;
    }

    private static string ToText(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

    private static bool CompareNumber(object? value, string expected, Func<double, double, bool> compare) =>
        double.TryParse(ToText(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var actual) &&
        double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var target) &&
        compare(actual, target);
}
