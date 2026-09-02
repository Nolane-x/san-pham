namespace Magic.Capture.Core.LocalActions;

public enum LocalActionOutputMode
{
    None,
    StdoutText,
    OutputFileAuto,
    OutputFileText,
    OutputFileImage
}

public sealed record LocalActionProfile(
    string Id,
    string Name,
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    LocalActionOutputMode OutputMode = LocalActionOutputMode.StdoutText,
    string OutputFileExtension = ".out",
    string? WorkingDirectory = null,
    int TimeoutMilliseconds = 30_000,
    int MaxStdoutBytes = 1 * 1024 * 1024,
    int MaxStderrBytes = 256 * 1024,
    int MaxOutputFileBytes = 16 * 1024 * 1024,
    bool Enabled = true,
    int SchemaVersion = 1);

public sealed record LocalActionValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public sealed record LocalActionApproval(
    string ExecutablePath,
    string Sha256,
    DateTimeOffset ApprovedUtc,
    int SchemaVersion = 1);

public static class LocalActionProfileValidator
{
    public const int MaximumArguments = 64;
    public const int MaximumArgumentLength = 8_192;
    public const int MaximumPathLength = 32_767;
    public const int MaximumTimeoutMilliseconds = 10 * 60 * 1_000;
    public const int MaximumCapturedStreamBytes = 8 * 1024 * 1024;
    public const int MaximumOutputFileBytes = 64 * 1024 * 1024;

    public static LocalActionValidationResult Validate(LocalActionProfile? profile)
    {
        var errors = new List<string>();
        if (profile is null) return new(false, ["Local Action profile is required."]);

        if (string.IsNullOrWhiteSpace(profile.Id) || profile.Id.Length > 96)
            errors.Add("Local Action id is invalid.");
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 120)
            errors.Add("Local Action name is invalid.");
        if (profile.SchemaVersion != 1)
            errors.Add("Unsupported Local Action schema version.");
        if (!profile.Enabled && string.IsNullOrWhiteSpace(profile.ExecutablePath))
            errors.Add("Disabled Local Actions still require an executable path so they can be safely edited and re-enabled.");

        ValidateExecutablePath(profile.ExecutablePath, errors);
        ValidateWorkingDirectory(profile.WorkingDirectory, errors);

        if (profile.Arguments is null)
        {
            errors.Add("Local Action arguments are required.");
        }
        else
        {
            if (profile.Arguments.Count > MaximumArguments)
                errors.Add($"Local Action cannot contain more than {MaximumArguments} arguments.");
            foreach (var argument in profile.Arguments.Take(MaximumArguments + 1))
            {
                if (argument is null) errors.Add("Local Action arguments cannot contain null values.");
                else if (argument.Length > MaximumArgumentLength) errors.Add("A Local Action argument is too long.");
            }
        }

        if (!Enum.IsDefined(profile.OutputMode))
            errors.Add("Local Action output mode is invalid.");
        if (!IsSafeExtension(profile.OutputFileExtension))
            errors.Add("Output file extension must be a short extension such as .png or .txt and cannot contain path separators.");
        if (profile.TimeoutMilliseconds is < 100 or > MaximumTimeoutMilliseconds)
            errors.Add($"Local Action timeout must be between 100 and {MaximumTimeoutMilliseconds} ms.");
        if (profile.MaxStdoutBytes is < 0 or > MaximumCapturedStreamBytes)
            errors.Add("Local Action stdout limit is outside the supported range.");
        if (profile.MaxStderrBytes is < 0 or > MaximumCapturedStreamBytes)
            errors.Add("Local Action stderr limit is outside the supported range.");
        if (profile.MaxOutputFileBytes is < 0 or > MaximumOutputFileBytes)
            errors.Add("Local Action output-file limit is outside the supported range.");
        if (profile.OutputMode == LocalActionOutputMode.StdoutText && profile.MaxStdoutBytes < 1)
            errors.Add("StdoutText mode requires a positive stdout limit.");
        if ((profile.OutputMode is LocalActionOutputMode.OutputFileAuto or LocalActionOutputMode.OutputFileText or LocalActionOutputMode.OutputFileImage) && profile.MaxOutputFileBytes < 1)
            errors.Add("Output-file modes require a positive output-file limit.");

        return new(errors.Count == 0, errors);
    }

    public static bool IsAllowedExecutableExtension(string? path)
    {
        var extension = Path.GetExtension(path ?? string.Empty);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".com", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSafeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension) || extension.Length is < 2 or > 16 || extension[0] != '.') return false;
        return extension.AsSpan(1).ToArray().All(ch => char.IsAsciiLetterOrDigit(ch));
    }

    private static void ValidateExecutablePath(string? path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumPathLength)
        {
            errors.Add("Local Action executable path is invalid.");
            return;
        }
        if (!IsFullyQualifiedWindowsPath(path))
            errors.Add("Local Action executable path must be fully qualified.");
        if (!IsAllowedExecutableExtension(path))
            errors.Add("Local Actions may directly launch only .exe or .com programs. Use an explicitly approved interpreter executable for scripts.");
    }

    private static void ValidateWorkingDirectory(string? path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (path.Length > MaximumPathLength || !IsFullyQualifiedWindowsPath(path))
            errors.Add("Local Action working directory must be a fully qualified path.");
    }

    private static bool IsFullyQualifiedWindowsPath(string path)
    {
        if (path.StartsWith("\\\\", StringComparison.Ordinal)) return path.Length > 4;
        return path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
    }
}

public static class LocalActionApprovalPolicy
{
    public static bool IsValid(LocalActionApproval? approval)
    {
        if (approval is null || approval.SchemaVersion != 1) return false;
        if (string.IsNullOrWhiteSpace(approval.ExecutablePath) || approval.ExecutablePath.Length > LocalActionProfileValidator.MaximumPathLength) return false;
        return approval.Sha256 is { Length: 64 } && approval.Sha256.All(Uri.IsHexDigit);
    }
}
