using System.Globalization;
using System.Text;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Documentation;

public static class DocumentationPolicy
{
    public const int MaximumSteps = 512;
    public const int MaximumProjectTitleLength = 256;
    public const int MaximumSubtitleLength = 512;
    public const int MaximumStepTitleLength = 512;
    public const int MaximumStepDescriptionLength = 16_384;
    public const int MaximumSectionLength = 256;
    public const int MaximumHeaderFooterLength = 2_048;
    public const int MaximumStableKeyLength = 256;
    public const int MaximumControlTextLength = 512;
    public const int MaximumProcessNameLength = 260;
    public const int MaximumImageKeyLength = 260;
    public const int DefaultCaptureWidth = 960;
    public const int DefaultCaptureHeight = 640;
    public const int MaximumCaptureWidth = 1_920;
    public const int MaximumCaptureHeight = 1_200;
    public const int MaximumLogoWidth = 1_024;
    public const int MaximumLogoHeight = 512;
    public const int MinimumCaptureWidth = 160;
    public const int MinimumCaptureHeight = 120;
    public const int TargetPadding = 48;
    public const int DuplicateClickMilliseconds = 180;
    public const int DuplicateClickDistancePixels = 8;

    private static readonly HashSet<string> SafeSingleKeyGestures = new(StringComparer.OrdinalIgnoreCase)
    {
        "Enter", "Escape", "Esc", "Tab", "Backspace", "Delete", "Insert",
        "Home", "End", "PageUp", "PageDown", "Page Up", "Page Down",
        "Left", "Right", "Up", "Down",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24"
    };

    public static DocumentationProject Normalize(DocumentationProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var steps = (project.Steps ?? [])
            .Where(step => step is not null)
            .Take(MaximumSteps)
            .Select(NormalizeStep)
            .ToArray();
        var now = project.ModifiedUtc == default ? DateTimeOffset.UtcNow : project.ModifiedUtc;
        return project with
        {
            SchemaVersion = DocumentationProject.CurrentSchemaVersion,
            ProjectId = project.ProjectId == Guid.Empty ? Guid.NewGuid() : project.ProjectId,
            CreatedUtc = project.CreatedUtc == default ? now : project.CreatedUtc,
            ModifiedUtc = now,
            Title = NormalizeRequired(project.Title, MaximumProjectTitleLength) ?? "Untitled guide",
            Subtitle = NormalizeOptional(project.Subtitle, MaximumSubtitleLength),
            Header = NormalizeOptional(project.Header, MaximumHeaderFooterLength),
            Footer = NormalizeOptional(project.Footer, MaximumHeaderFooterLength),
            LogoImageKey = string.Equals(NormalizeOptional(project.LogoImageKey, MaximumImageKeyLength), "logo.png", StringComparison.Ordinal) ? "logo.png" : null,
            Template = DocumentationTemplateCatalog.NormalizeId(project.Template),
            Steps = steps
        };
    }

    public static DocumentationCapturePlan PlanCapture(
        PixelRect monitorBounds,
        PixelPoint desktopPoint,
        DocumentationTargetEvidence? target)
    {
        if (monitorBounds.IsEmpty) throw new ArgumentOutOfRangeException(nameof(monitorBounds));
        if (!monitorBounds.Contains(desktopPoint)) throw new ArgumentOutOfRangeException(nameof(desktopPoint));

        PixelRect desired;
        DocumentationTargetEvidence? safeTarget = null;
        if (target is not null && !target.DesktopBounds.IsEmpty && target.DesktopBounds.Contains(desktopPoint))
        {
            safeTarget = NormalizeTarget(target);
            var padded = Inflate(target.DesktopBounds, TargetPadding);
            desired = EnsureMinimumSize(padded, desktopPoint, MinimumCaptureWidth, MinimumCaptureHeight);
        }
        else
        {
            desired = Centered(desktopPoint, DefaultCaptureWidth, DefaultCaptureHeight);
        }

        desired = LimitMaximumSize(desired, desktopPoint, MaximumCaptureWidth, MaximumCaptureHeight);
        desired = ClampToBounds(desired, monitorBounds);
        desired = EnsureMinimumWithinBounds(desired, desktopPoint, monitorBounds, MinimumCaptureWidth, MinimumCaptureHeight);

        var local = new PixelPoint(desktopPoint.X - desired.X, desktopPoint.Y - desired.Y);
        return new DocumentationCapturePlan(desired, local, safeTarget);
    }

    public static bool ShouldCoalesce(DocumentationClickEvent previous, DocumentationClickEvent current)
    {
        if (previous.Button != current.Button) return false;
        var delta = current.TimestampUtc - previous.TimestampUtc;
        if (delta < TimeSpan.Zero || delta.TotalMilliseconds > DuplicateClickMilliseconds) return false;
        var dx = current.DesktopPoint.X - previous.DesktopPoint.X;
        var dy = current.DesktopPoint.Y - previous.DesktopPoint.Y;
        return (long)dx * dx + (long)dy * dy <= (long)DuplicateClickDistancePixels * DuplicateClickDistancePixels;
    }

    public static string GenerateDescription(DocumentationTargetEvidence? target)
    {
        if (target is null) return "Click the highlighted area.";
        var type = NormalizeOptional(target.ControlType, 120) ?? "control";
        var typeLower = type.ToLowerInvariant();
        var name = NormalizeOptional(target.Name, 160);
        var quoted = name is null ? null : $"“{name}”";

        if (quoted is not null)
        {
            if (typeLower.Contains("checkbox", StringComparison.Ordinal) ||
                typeLower.Contains("toggle", StringComparison.Ordinal) ||
                typeLower.Contains("switch", StringComparison.Ordinal))
                return $"Toggle {quoted}.";
            if (typeLower.Contains("edit", StringComparison.Ordinal) ||
                typeLower.Contains("combobox", StringComparison.Ordinal) ||
                typeLower.Contains("combo box", StringComparison.Ordinal))
                return $"Select or edit {quoted}.";
            if (typeLower.Contains("menuitem", StringComparison.Ordinal) ||
                typeLower.Contains("menu item", StringComparison.Ordinal))
                return $"Choose {quoted}.";
            return $"Click {quoted}.";
        }

        return $"Click the {HumanizeControlType(type)}.";
    }

    public static string GenerateProjectTitle(DocumentationTargetEvidence? target)
    {
        var window = NormalizeOptional(target?.WindowTitle, 180);
        if (window is not null) return NormalizeRequired($"Guide to {window}", MaximumProjectTitleLength)!;
        var process = NormalizeOptional(target?.ProcessName, 120);
        if (process is not null) return NormalizeRequired($"{process} guide", MaximumProjectTitleLength)!;
        return "Magic Capture guide";
    }

    public static DocumentationProject MoveStep(DocumentationProject project, string stepId, int delta)
    {
        ArgumentNullException.ThrowIfNull(project);
        var steps = project.Steps.ToList();
        var index = FindStepIndex(steps, stepId);
        if (index < 0 || delta == 0) return project;
        var target = Math.Clamp(index + delta, 0, steps.Count - 1);
        if (target == index) return project;
        var item = steps[index];
        steps.RemoveAt(index);
        steps.Insert(target, item);
        return Touch(project with { Steps = steps.ToArray() });
    }

    public static DocumentationProject RemoveStep(DocumentationProject project, string stepId)
    {
        ArgumentNullException.ThrowIfNull(project);
        var steps = project.Steps.Where(step => !string.Equals(step.Id, stepId, StringComparison.Ordinal)).ToArray();
        return steps.Length == project.Steps.Count ? project : Touch(project with { Steps = steps });
    }

    public static DocumentationProject DuplicateStep(DocumentationProject project, string stepId, string newId)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Steps.Count >= MaximumSteps) throw new InvalidOperationException($"Documentation projects support at most {MaximumSteps} steps.");
        var normalizedId = NormalizeRequired(newId, 128) ?? throw new ArgumentException("A duplicate step id is required.", nameof(newId));
        if (project.Steps.Any(step => string.Equals(step.Id, normalizedId, StringComparison.Ordinal)))
            throw new ArgumentException("Duplicate step id already exists.", nameof(newId));
        var steps = project.Steps.ToList();
        var index = FindStepIndex(steps, stepId);
        if (index < 0) return project;
        var source = steps[index];
        steps.Insert(index + 1, source with { Id = normalizedId, CapturedUtc = DateTimeOffset.UtcNow });
        return Touch(project with { Steps = steps.ToArray() });
    }

    public static DocumentationProject MergeSteps(DocumentationProject project, string firstId, string secondId)
    {
        ArgumentNullException.ThrowIfNull(project);
        var steps = project.Steps.ToList();
        var firstIndex = FindStepIndex(steps, firstId);
        var secondIndex = FindStepIndex(steps, secondId);
        if (firstIndex < 0 || secondIndex < 0 || firstIndex == secondIndex) return project;
        if (Math.Abs(firstIndex - secondIndex) != 1)
            throw new InvalidOperationException("Only adjacent documentation steps can be merged.");
        if (secondIndex < firstIndex) (firstIndex, secondIndex) = (secondIndex, firstIndex);

        var first = steps[firstIndex];
        var second = steps[secondIndex];
        var merged = first with
        {
            CapturedUtc = second.CapturedUtc,
            ImageKey = second.ImageKey,
            Width = second.Width,
            Height = second.Height,
            Target = second.Target,
            ClickPoint = second.ClickPoint,
            MouseButton = second.MouseButton,
            SafeKeyGesture = second.SafeKeyGesture,
            Title = FirstNonEmpty(first.Title, second.Title),
            Description = JoinParagraphs(first.Description, second.Description),
            Section = FirstNonEmpty(first.Section, second.Section)
        };
        steps[firstIndex] = NormalizeStep(merged);
        steps.RemoveAt(secondIndex);
        return Touch(project with { Steps = steps.ToArray() });
    }

    public static bool IsSafeKeyboardGesture(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return false;
        var normalized = label.Trim();
        if (normalized.Length > 80) return false;
        if (SafeSingleKeyGestures.Contains(normalized)) return true;
        if (normalized.Equals("Space", StringComparison.OrdinalIgnoreCase)) return false;

        var parts = normalized.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Length > 5) return false;
        var hasCommandModifier = false;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                parts[i].Equals("Control", StringComparison.OrdinalIgnoreCase) ||
                parts[i].Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
                parts[i].Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                parts[i].Equals("Windows", StringComparison.OrdinalIgnoreCase))
                hasCommandModifier = true;
            else if (!parts[i].Equals("Shift", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        if (!hasCommandModifier && !parts[0].Equals("Shift", StringComparison.OrdinalIgnoreCase)) return false;

        var key = parts[^1];
        if (SafeSingleKeyGestures.Contains(key)) return true;
        if (key.Length == 1 && char.IsLetterOrDigit(key[0])) return hasCommandModifier;
        return key.Equals("Space", StringComparison.OrdinalIgnoreCase) && hasCommandModifier;
    }

    public static DocumentationStep NormalizeStep(DocumentationStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var target = step.Target is null ? null : NormalizeTarget(step.Target);
        var safeGesture = target?.IsPassword == true ? null : NormalizeOptional(step.SafeKeyGesture, 80);
        if (!IsSafeKeyboardGesture(safeGesture)) safeGesture = null;
        var width = Math.Clamp(step.Width, 1, MaximumCaptureWidth);
        var height = Math.Clamp(step.Height, 1, MaximumCaptureHeight);
        PixelPoint? click = step.ClickPoint;
        if (click is { } point)
            click = new PixelPoint(Math.Clamp(point.X, 0, width - 1), Math.Clamp(point.Y, 0, height - 1));

        return step with
        {
            Id = NormalizeRequired(step.Id, 128) ?? Guid.NewGuid().ToString("N"),
            CapturedUtc = step.CapturedUtc == default ? DateTimeOffset.UtcNow : step.CapturedUtc,
            ImageKey = NormalizeRequired(step.ImageKey, MaximumImageKeyLength) ?? throw new InvalidDataException("Documentation step image key is required."),
            Width = width,
            Height = height,
            Target = target,
            ClickPoint = click,
            SafeKeyGesture = safeGesture,
            Title = NormalizeOptional(step.Title, MaximumStepTitleLength),
            Description = NormalizeOptional(step.Description, MaximumStepDescriptionLength),
            Section = NormalizeOptional(step.Section, MaximumSectionLength)
        };
    }

    private static DocumentationTargetEvidence NormalizeTarget(DocumentationTargetEvidence target) => target with
    {
        StableKey = NormalizeRequired(target.StableKey, MaximumStableKeyLength) ?? "unknown",
        ControlType = NormalizeRequired(target.ControlType, 120) ?? "Custom",
        Name = NormalizeOptional(target.Name, MaximumControlTextLength),
        AutomationId = NormalizeOptional(target.AutomationId, MaximumControlTextLength),
        ProcessName = NormalizeOptional(target.ProcessName, MaximumProcessNameLength),
        WindowTitle = NormalizeOptional(target.WindowTitle, MaximumControlTextLength),
        ProcessId = Math.Max(0, target.ProcessId)
    };

    private static int FindStepIndex(IReadOnlyList<DocumentationStep> steps, string stepId)
    {
        for (var i = 0; i < steps.Count; i++)
            if (string.Equals(steps[i].Id, stepId, StringComparison.Ordinal)) return i;
        return -1;
    }

    private static DocumentationProject Touch(DocumentationProject project) =>
        Normalize(project with { ModifiedUtc = DateTimeOffset.UtcNow });

    private static PixelRect Inflate(PixelRect rect, int amount)
    {
        if (rect.IsEmpty) return rect;
        var left = (long)rect.X - amount;
        var top = (long)rect.Y - amount;
        var right = (long)rect.Right + amount;
        var bottom = (long)rect.Bottom + amount;
        return SafeRect(left, top, right - left, bottom - top);
    }

    private static PixelRect Centered(PixelPoint point, int width, int height) =>
        new(point.X - width / 2, point.Y - height / 2, width, height);

    private static PixelRect EnsureMinimumSize(PixelRect rect, PixelPoint anchor, int minWidth, int minHeight)
    {
        var width = Math.Max(rect.Width, minWidth);
        var height = Math.Max(rect.Height, minHeight);
        if (width == rect.Width && height == rect.Height) return rect;
        return Centered(anchor, width, height);
    }

    private static PixelRect LimitMaximumSize(PixelRect rect, PixelPoint anchor, int maxWidth, int maxHeight)
    {
        if (rect.Width <= maxWidth && rect.Height <= maxHeight) return rect;
        return Centered(anchor, Math.Min(rect.Width, maxWidth), Math.Min(rect.Height, maxHeight));
    }

    private static PixelRect ClampToBounds(PixelRect rect, PixelRect bounds)
    {
        var width = Math.Min(Math.Max(1, rect.Width), bounds.Width);
        var height = Math.Min(Math.Max(1, rect.Height), bounds.Height);
        var x = Math.Clamp(rect.X, bounds.X, bounds.Right - width);
        var y = Math.Clamp(rect.Y, bounds.Y, bounds.Bottom - height);
        return new PixelRect(x, y, width, height);
    }

    private static PixelRect EnsureMinimumWithinBounds(PixelRect rect, PixelPoint anchor, PixelRect bounds, int minWidth, int minHeight)
    {
        var width = Math.Min(bounds.Width, Math.Max(rect.Width, minWidth));
        var height = Math.Min(bounds.Height, Math.Max(rect.Height, minHeight));
        if (width == rect.Width && height == rect.Height) return rect;
        return ClampToBounds(Centered(anchor, width, height), bounds);
    }

    private static PixelRect SafeRect(long x, long y, long width, long height)
    {
        var safeX = (int)Math.Clamp(x, int.MinValue, int.MaxValue);
        var safeY = (int)Math.Clamp(y, int.MinValue, int.MaxValue);
        var safeWidth = (int)Math.Clamp(width, 1, int.MaxValue);
        var safeHeight = (int)Math.Clamp(height, 1, int.MaxValue);
        return new PixelRect(safeX, safeY, safeWidth, safeHeight);
    }

    private static string HumanizeControlType(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (i > 0 && char.IsUpper(c) && char.IsLower(value[i - 1])) builder.Append(' ');
            builder.Append(char.ToLowerInvariant(c));
        }
        var normalized = builder.ToString().Trim();
        return normalized.Length == 0 ? "control" : normalized;
    }

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : !string.IsNullOrWhiteSpace(second) ? second : null;

    private static string? JoinParagraphs(string? first, string? second)
    {
        first = NormalizeOptional(first, MaximumStepDescriptionLength);
        second = NormalizeOptional(second, MaximumStepDescriptionLength);
        if (first is null) return second;
        if (second is null) return first;
        return NormalizeOptional(first + "\n\n" + second, MaximumStepDescriptionLength);
    }

    private static string? NormalizeRequired(string? value, int maximumLength) => NormalizeOptional(value, maximumLength);

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var span = value.AsSpan().Trim();
        var builder = new StringBuilder(Math.Min(span.Length, maximumLength));
        foreach (var c in span)
        {
            if (builder.Length == maximumLength) break;
            if (!char.IsControl(c) || c is '\t' or '\n') builder.Append(c);
        }
        var normalized = builder.ToString().Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
