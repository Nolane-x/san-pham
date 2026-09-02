using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Storage;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.App.Workflows;

internal sealed class WorkflowTriggerHistoryStore
{
    public const int MaximumEntries = 200;
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WorkflowTriggerHistoryStore(AppPaths paths) => _paths = paths;

    public async Task<IReadOnlyList<WorkflowTriggerHistoryEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await LoadCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task AppendAsync(WorkflowTrigger trigger, WorkflowTriggerRunStatus status, string reasonCode, DateTimeOffset startedUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        reasonCode = NormalizeReasonCode(reasonCode);
        var completedUtc = DateTimeOffset.UtcNow;
        var entry = new WorkflowTriggerHistoryEntry(Guid.NewGuid().ToString("N"), trigger.Id, trigger.Name, trigger.Kind, status, reasonCode, startedUtc, completedUtc);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await LoadCoreAsync(cancellationToken)).ToList();
            items.Add(entry);
            var retained = items.OrderByDescending(item => item.StartedUtc).ThenByDescending(item => item.Id, StringComparer.Ordinal).Take(MaximumEntries).ToArray();
            await AtomicJsonFile.WriteAsync(_paths.WorkflowTriggerHistoryFile, retained, cancellationToken, LocalConfigurationLimits.MaximumWorkflowTriggerHistoryJsonBytes);
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var path in new[] { _paths.WorkflowTriggerHistoryFile, _paths.WorkflowTriggerHistoryFile + ".bak" })
            {
                if (!File.Exists(path)) continue;
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<WorkflowTriggerHistoryEntry>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        var items = await AtomicJsonFile.ReadAsync<List<WorkflowTriggerHistoryEntry>>(_paths.WorkflowTriggerHistoryFile, cancellationToken, LocalConfigurationLimits.MaximumWorkflowTriggerHistoryJsonBytes) ?? [];
        LocalConfigurationLimits.ValidateCount(items.Count, MaximumEntries, "Workflow trigger history");
        foreach (var item in items) Validate(item);
        return items.OrderByDescending(item => item.StartedUtc).ThenByDescending(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    private static string NormalizeReasonCode(string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode)) return "unspecified";
        var value = new string(reasonCode.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray());
        return value.Length <= 64 ? value : value[..64];
    }

    private static void Validate(WorkflowTriggerHistoryEntry item)
    {
        if (string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 64) throw new InvalidDataException("Trigger history id is invalid.");
        if (!WorkflowTriggerPolicy.IsSafeIdentifier(item.TriggerId)) throw new InvalidDataException("Trigger history trigger id is invalid.");
        if (string.IsNullOrWhiteSpace(item.TriggerName) || item.TriggerName.Length > WorkflowTriggerPolicy.MaximumNameLength) throw new InvalidDataException("Trigger history name is invalid.");
        if (string.IsNullOrWhiteSpace(item.ReasonCode) || item.ReasonCode.Length > 64) throw new InvalidDataException("Trigger history reason code is invalid.");
        if (item.CompletedUtc < item.StartedUtc) throw new InvalidDataException("Trigger history timestamps are invalid.");
    }
}
