using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Storage;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.App.Workflows;

internal sealed class WorkflowTriggerStore
{
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WorkflowTriggerStore(AppPaths paths) => _paths = paths;

    public event EventHandler? Changed;

    public async Task<IReadOnlyList<WorkflowTrigger>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await LoadCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(WorkflowTrigger trigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        var single = WorkflowTriggerPolicy.Validate(trigger);
        if (!single.IsValid) throw new InvalidDataException(string.Join(" ", single.Errors));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = (await LoadCoreAsync(cancellationToken)).ToList();
            var index = existing.FindIndex(item => string.Equals(item.Id, trigger.Id, StringComparison.Ordinal));
            if (index >= 0) existing[index] = trigger;
            else existing.Add(trigger);
            var validation = WorkflowTriggerPolicy.ValidateSet(existing);
            if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
            await AtomicJsonFile.WriteAsync(_paths.WorkflowTriggersFile, existing, cancellationToken, LocalConfigurationLimits.MaximumWorkflowTriggerJsonBytes);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<int> DisableDanglingAsync(
        IReadOnlySet<string> validWorkflowIds,
        IReadOnlySet<string> validCaptureProfileIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validWorkflowIds);
        ArgumentNullException.ThrowIfNull(validCaptureProfileIds);
        var changed = 0;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = (await LoadCoreAsync(cancellationToken)).ToList();
            for (var index = 0; index < existing.Count; index++)
            {
                var trigger = existing[index];
                if (!trigger.Enabled) continue;
                if (validWorkflowIds.Contains(trigger.WorkflowId) && validCaptureProfileIds.Contains(trigger.CaptureProfileId)) continue;
                existing[index] = trigger with { Enabled = false };
                changed++;
            }
            if (changed > 0)
                await AtomicJsonFile.WriteAsync(_paths.WorkflowTriggersFile, existing, cancellationToken, LocalConfigurationLimits.MaximumWorkflowTriggerJsonBytes);
        }
        finally { _gate.Release(); }
        if (changed > 0) Changed?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!WorkflowTriggerPolicy.IsSafeIdentifier(id)) throw new ArgumentException("Trigger id is invalid.", nameof(id));
        var changed = false;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = (await LoadCoreAsync(cancellationToken)).ToList();
            changed = existing.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal)) > 0;
            if (changed)
                await AtomicJsonFile.WriteAsync(_paths.WorkflowTriggersFile, existing, cancellationToken, LocalConfigurationLimits.MaximumWorkflowTriggerJsonBytes);
        }
        finally { _gate.Release(); }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<IReadOnlyList<WorkflowTrigger>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        var items = await AtomicJsonFile.ReadAsync<List<WorkflowTrigger>>(_paths.WorkflowTriggersFile, cancellationToken, LocalConfigurationLimits.MaximumWorkflowTriggerJsonBytes) ?? [];
        var validation = WorkflowTriggerPolicy.ValidateSet(items);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        return items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }
}
