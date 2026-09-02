namespace Magic.Capture.Core.Ai;

public static class MagicActionValidator
{
    public static MagicActionValidationResult Validate(MagicActionDefinition? action)
    {
        var errors = new List<string>();
        if (action is null) return new MagicActionValidationResult(false, ["Action is required."]);
        if (action.SchemaVersion != 1) errors.Add("Unsupported schema version.");
        if (string.IsNullOrWhiteSpace(action.Id) || action.Id.Length > 96) errors.Add("Action id is invalid.");
        if (string.IsNullOrWhiteSpace(action.Name) || action.Name.Length > 120) errors.Add("Action name is invalid.");
        if (string.IsNullOrWhiteSpace(action.Category) || action.Category.Length > 120) errors.Add("Action category is invalid.");
        if ((action.SystemInstruction ?? string.Empty).Length > 8_000 || (action.UserInstruction ?? string.Empty).Length > 12_000) errors.Add("Action instructions are too large.");
        if (!Enum.IsDefined(action.VisionMode)) errors.Add("Action vision mode is invalid.");
        if (!Enum.IsDefined(action.OutputKind)) errors.Add("Action output kind is invalid.");
        if (!string.IsNullOrWhiteSpace(action.Id) && !string.IsNullOrWhiteSpace(action.UserInstruction) &&
            (action.Id.Contains("script", StringComparison.OrdinalIgnoreCase) ||
             action.UserInstruction.Contains("powershell", StringComparison.OrdinalIgnoreCase) && action.Category.Equals("Executable", StringComparison.OrdinalIgnoreCase)))
            errors.Add("Executable custom actions are not supported.");
        return new MagicActionValidationResult(errors.Count == 0, errors);
    }
}
