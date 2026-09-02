using System.Text.RegularExpressions;

namespace Magic.Capture.Core.Workflows;

public static partial class WorkflowVariables
{
    public const int MaximumVariables = 64;
    public const int MaximumNameLength = 64;
    public const int MaximumValueLength = 16_384;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "asset", "image", "workflow", "filename", "width", "height", "size", "source",
        "ocr", "ocrText", "text", "table", "barcodes", "signals", "metadata", "magic",
        "destination", "stdout", "stderr", "output", "input", "windowTitle"
    };

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    public static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name) && NameRegex().IsMatch(name);
    public static bool IsReserved(string? name) => !string.IsNullOrWhiteSpace(name) && ReservedNames.Contains(name);

    public static IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string>? variables, string scope)
    {
        var errors = new List<string>();
        if (variables is null) return errors;
        if (variables.Count > MaximumVariables) errors.Add($"{scope} cannot contain more than {MaximumVariables} variables.");
        foreach (var (name, value) in variables.Take(MaximumVariables + 1))
        {
            if (!IsValidName(name)) errors.Add($"{scope} contains an invalid variable name.");
            else if (IsReserved(name)) errors.Add($"{scope} variable '{name}' uses a reserved runtime name.");
            if ((value ?? string.Empty).Length > MaximumValueLength) errors.Add($"{scope} variable '{name}' is too long.");
        }
        return errors;
    }
}
