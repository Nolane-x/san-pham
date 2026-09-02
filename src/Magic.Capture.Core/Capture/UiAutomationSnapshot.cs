using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.ScreenGraph;

namespace Magic.Capture.Core.Capture;

public sealed record UiAutomationSnapshotNode(
    string StableKey,
    string? ParentStableKey,
    string ControlType,
    string? Name,
    string? AutomationId,
    string? Value,
    bool? IsEnabled,
    bool? IsChecked,
    bool? IsSelected,
    bool? HasKeyboardFocus,
    PixelRect DesktopBounds,
    string? AccessKey,
    string? AcceleratorKey,
    int ProcessId,
    string? ProcessName,
    string? WindowTitle,
    int WindowZOrder,
    int Depth,
    bool? IsPassword = null);

public sealed record UiAutomationSnapshot(
    DateTimeOffset CapturedUtc,
    IReadOnlyList<UiAutomationSnapshotNode> Nodes,
    bool WasTruncated,
    string? Diagnostic = null)
{
    public static UiAutomationSnapshot Empty { get; } = new(DateTimeOffset.UnixEpoch, [], false);
}

public sealed record UiAutomationSnapTarget(
    string StableKey,
    PixelRect Bounds,
    string Label,
    string ControlType,
    int WindowZOrder,
    int Depth);

public static class UiAutomationSnapshotRules
{
    public const int MaximumNodes = 384;
    public const int MaximumDepth = 10;
    public const int MaximumTopLevelWindows = 12;
    public const int MaximumSnapTargets = MaximumNodes + 128;
    public const int MaximumStableKeyLength = 256;
    public const int MaximumTextLength = 512;
    public const int MaximumValueLength = 1_024;
    private const int MaximumInputNodesScanned = MaximumNodes * 4;

    public static UiAutomationSnapshot Normalize(
        IEnumerable<UiAutomationSnapshotNode>? source,
        bool wasTruncated,
        string? diagnostic = null,
        DateTimeOffset? capturedUtc = null)
    {
        var nodes = new List<UiAutomationSnapshotNode>(MaximumNodes);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var scanned = 0;
        var truncated = wasTruncated;

        foreach (var candidate in source ?? [])
        {
            scanned++;
            if (scanned > MaximumInputNodesScanned)
            {
                truncated = true;
                break;
            }

            if (candidate.DesktopBounds.IsEmpty) continue;
            var stableKey = NormalizeRequired(candidate.StableKey, MaximumStableKeyLength);
            if (stableKey is null || !keys.Add(stableKey)) continue;

            nodes.Add(candidate with
            {
                StableKey = stableKey,
                ParentStableKey = NormalizeOptional(candidate.ParentStableKey, MaximumStableKeyLength),
                ControlType = NormalizeRequired(candidate.ControlType, 120) ?? "Custom",
                Name = NormalizeOptional(candidate.Name, MaximumTextLength),
                AutomationId = NormalizeOptional(candidate.AutomationId, MaximumTextLength),
                Value = candidate.IsPassword == true ? null : NormalizeOptional(candidate.Value, MaximumValueLength),
                AccessKey = NormalizeOptional(candidate.AccessKey, MaximumTextLength),
                AcceleratorKey = NormalizeOptional(candidate.AcceleratorKey, MaximumTextLength),
                ProcessId = Math.Max(0, candidate.ProcessId),
                ProcessName = NormalizeOptional(candidate.ProcessName, 260),
                WindowTitle = NormalizeOptional(candidate.WindowTitle, MaximumTextLength),
                WindowZOrder = Math.Clamp(candidate.WindowZOrder, 0, 255),
                Depth = Math.Clamp(candidate.Depth, 0, MaximumDepth)
            });

            if (nodes.Count == MaximumNodes)
            {
                truncated = true;
                break;
            }
        }

        var acceptedKeys = nodes.Select(node => node.StableKey).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            var parent = nodes[i].ParentStableKey;
            if (parent is not null && (!acceptedKeys.Contains(parent) || parent == nodes[i].StableKey))
                nodes[i] = nodes[i] with { ParentStableKey = null };
        }

        return new UiAutomationSnapshot(
            capturedUtc ?? DateTimeOffset.UtcNow,
            nodes,
            truncated,
            NormalizeOptional(diagnostic, 1_024));
    }

    public static UiAutomationSnapshotNode? FindSnapTarget(UiAutomationSnapshot snapshot, PixelPoint desktopPoint)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Nodes
            .Where(node => node.DesktopBounds.Contains(desktopPoint))
            .OrderBy(node => node.WindowZOrder)
            .ThenByDescending(node => node.Depth)
            .ThenBy(node => node.DesktopBounds.Area)
            .ThenBy(node => node.StableKey, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static UiAutomationSnapTarget? FindSnapTarget(IReadOnlyList<UiAutomationSnapTarget> targets, PixelPoint point)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return targets
            .Where(target => target.Bounds.Contains(point))
            .OrderBy(target => target.WindowZOrder)
            .ThenByDescending(target => target.Depth)
            .ThenBy(target => target.Bounds.Area)
            .ThenBy(target => target.StableKey, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static IReadOnlyList<UiAutomationSnapTarget> ProjectSnapTargets(UiAutomationSnapshot snapshot, PixelRect monitorDesktopBounds)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (monitorDesktopBounds.IsEmpty) return [];

        var targets = new List<UiAutomationSnapTarget>(Math.Min(snapshot.Nodes.Count, MaximumNodes));
        foreach (var node in snapshot.Nodes)
        {
            var clipped = node.DesktopBounds.Intersect(monitorDesktopBounds);
            if (clipped.Width < 2 || clipped.Height < 2) continue;
            targets.Add(new UiAutomationSnapTarget(
                node.StableKey,
                new PixelRect(clipped.X - monitorDesktopBounds.X, clipped.Y - monitorDesktopBounds.Y, clipped.Width, clipped.Height),
                BuildLabel(node),
                node.ControlType,
                node.WindowZOrder,
                node.Depth));
        }
        return targets;
    }

    public static IReadOnlyList<ScreenUiAutomationNode> ProjectForCapture(
        UiAutomationSnapshot snapshot,
        PixelRect monitorDesktopBounds,
        CaptureSelectionGeometry selection)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selection);
        if (monitorDesktopBounds.IsEmpty || selection.Bounds.IsEmpty || snapshot.Nodes.Count == 0) return [];

        var captureDesktopBounds = new PixelRect(
            checked(monitorDesktopBounds.X + selection.Bounds.X),
            checked(monitorDesktopBounds.Y + selection.Bounds.Y),
            selection.Bounds.Width,
            selection.Bounds.Height);

        var byKey = snapshot.Nodes.ToDictionary(node => node.StableKey, StringComparer.Ordinal);
        var accepted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in snapshot.Nodes)
        {
            if (node.DesktopBounds.Intersect(captureDesktopBounds).IsEmpty) continue;
            var centerOnMonitor = new PixelPoint(
                node.DesktopBounds.Center.X - monitorDesktopBounds.X,
                node.DesktopBounds.Center.Y - monitorDesktopBounds.Y);
            if (!CaptureSelectionGeometryRules.ContainsPoint(selection, centerOnMonitor)) continue;
            accepted.Add(node.StableKey);
        }

        // Keep ancestors of accepted controls so ScreenGraph retains meaningful UI hierarchy.
        foreach (var key in accepted.ToArray())
        {
            var current = byKey[key];
            var hops = 0;
            while (current.ParentStableKey is { } parentKey && hops++ < MaximumDepth && byKey.TryGetValue(parentKey, out var parent))
            {
                if (!parent.DesktopBounds.Intersect(captureDesktopBounds).IsEmpty) accepted.Add(parentKey);
                current = parent;
            }
        }

        var result = new List<ScreenUiAutomationNode>(accepted.Count);
        foreach (var node in snapshot.Nodes)
        {
            if (!accepted.Contains(node.StableKey)) continue;
            var clipped = node.DesktopBounds.Intersect(captureDesktopBounds);
            if (clipped.IsEmpty) continue;
            var parentKey = node.ParentStableKey is { } parent && accepted.Contains(parent) ? parent : null;
            result.Add(new ScreenUiAutomationNode(
                node.StableKey,
                node.ControlType,
                node.Name,
                node.AutomationId,
                node.Value,
                node.IsEnabled,
                node.IsChecked,
                node.IsSelected,
                node.HasKeyboardFocus,
                new PixelRect(clipped.X - captureDesktopBounds.X, clipped.Y - captureDesktopBounds.Y, clipped.Width, clipped.Height),
                parentKey,
                node.AccessKey,
                node.ProcessName,
                node.WindowTitle,
                node.ProcessId,
                node.AcceleratorKey,
                node.IsPassword));
        }
        return result;
    }

    private static string BuildLabel(UiAutomationSnapshotNode node)
    {
        var name = NormalizeOptional(node.Name, 120);
        return string.IsNullOrWhiteSpace(name) ? node.ControlType : $"{node.ControlType} · {name}";
    }

    private static string? NormalizeRequired(string? value, int maximumLength) =>
        NormalizeOptional(value, maximumLength);

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var span = value.AsSpan().Trim();
        var builder = new System.Text.StringBuilder(Math.Min(span.Length, maximumLength));
        foreach (var character in span)
        {
            if (builder.Length == maximumLength) break;
            if (!char.IsControl(character) || character is '\t' or '\n') builder.Append(character);
        }
        var normalized = builder.ToString().Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
