using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Storage;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.App.Workflows;

internal sealed record WorkflowTraceStepRecord(
    string StepId,
    string Kind,
    string Status,
    int Attempts,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    long DurationMilliseconds,
    string? Message);

internal sealed record WorkflowTraceRecord(
    Guid TraceId,
    string WorkflowId,
    string WorkflowName,
    int SchemaVersion,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    bool Succeeded,
    bool DryRun,
    IReadOnlyList<WorkflowTraceStepRecord> Steps,
    Guid? AssetId = null,
    string? WorkflowFingerprint = null,
    Guid? ResumedFromTraceId = null,
    IReadOnlyList<string>? ResumeCompletedSideEffectStepIds = null);

internal sealed class WorkflowTraceStore
{
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WorkflowTraceStore(AppPaths paths) => _paths = paths;

    public async Task<IReadOnlyList<WorkflowTraceRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await LoadCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task AppendAsync(
        CaptureWorkflow workflow,
        WorkflowExecutionResult result,
        Guid? assetId = null,
        Guid? resumedFromTraceId = null,
        IReadOnlyCollection<string>? resumeCompletedSideEffectStepIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(result);
        await AppendRecordAsync(CreateRecord(workflow, result, assetId, resumedFromTraceId, resumeCompletedSideEffectStepIds), cancellationToken);
    }

    public async Task AppendFailureAsync(
        CaptureWorkflow workflow,
        bool dryRun,
        Guid? assetId = null,
        Guid? resumedFromTraceId = null,
        IReadOnlyCollection<string>? resumeCompletedSideEffectStepIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var now = DateTimeOffset.UtcNow;
        var record = new WorkflowTraceRecord(
            Guid.NewGuid(), workflow.Id, workflow.Name, workflow.SchemaVersion,
            now, now, Succeeded: false, DryRun: dryRun, Steps: [],
            AssetId: assetId, WorkflowFingerprint: Magic.Capture.Core.Workflows.WorkflowFingerprint.Compute(workflow), ResumedFromTraceId: resumedFromTraceId,
            ResumeCompletedSideEffectStepIds: NormalizeResumeStepIds(resumeCompletedSideEffectStepIds));
        await AppendRecordAsync(record, cancellationToken);
    }

    private async Task AppendRecordAsync(WorkflowTraceRecord record, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var traces = (await LoadCoreAsync(cancellationToken)).ToList();
            traces.Add(record);
            var retained = traces
                .OrderByDescending(trace => trace.StartedUtc)
                .ThenByDescending(trace => trace.TraceId)
                .Take(WorkflowRuntimePolicy.MaximumTraceEntries)
                .ToArray();
            await AtomicJsonFile.WriteAsync(
                _paths.WorkflowTracesFile,
                retained,
                cancellationToken,
                LocalConfigurationLimits.MaximumWorkflowTraceJsonBytes);
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_paths.WorkflowTracesFile)) File.Delete(_paths.WorkflowTracesFile);
            var backup = _paths.WorkflowTracesFile + ".bak";
            if (File.Exists(backup)) File.Delete(backup);
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<WorkflowTraceRecord>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        var traces = await AtomicJsonFile.ReadAsync<List<WorkflowTraceRecord>>(
            _paths.WorkflowTracesFile,
            cancellationToken,
            LocalConfigurationLimits.MaximumWorkflowTraceJsonBytes) ?? [];
        LocalConfigurationLimits.ValidateCount(traces.Count, WorkflowRuntimePolicy.MaximumTraceEntries, "Workflow traces");
        foreach (var trace in traces) Validate(trace);
        return traces.OrderByDescending(trace => trace.StartedUtc).ThenByDescending(trace => trace.TraceId).ToArray();
    }

    private static WorkflowTraceRecord CreateRecord(CaptureWorkflow workflow, WorkflowExecutionResult result, Guid? assetId, Guid? resumedFromTraceId, IReadOnlyCollection<string>? resumeCompletedSideEffectStepIds)
    {
        var steps = result.Steps.Select(step => new WorkflowTraceStepRecord(
            step.StepId,
            step.Kind.ToString(),
            step.Status.ToString(),
            step.Attempts,
            step.StartedUtc,
            step.FinishedUtc,
            Math.Max(0, (long)step.Duration.TotalMilliseconds),
            SafeTraceMessage(step))).ToArray();
        return new WorkflowTraceRecord(
            Guid.NewGuid(), workflow.Id, workflow.Name, workflow.SchemaVersion,
            result.StartedUtc, result.FinishedUtc, result.Succeeded, result.DryRun, steps,
            AssetId: assetId, WorkflowFingerprint: Magic.Capture.Core.Workflows.WorkflowFingerprint.Compute(workflow), ResumedFromTraceId: resumedFromTraceId,
            ResumeCompletedSideEffectStepIds: NormalizeResumeStepIds(resumeCompletedSideEffectStepIds));
    }

    private static string? SafeTraceMessage(WorkflowStepResult step)
    {
        if (step.Status == WorkflowStepStatus.Failed)
            return "Step failed; sensitive diagnostic payload is intentionally not persisted.";
        if (step.Status == WorkflowStepStatus.WouldRun)
            return "Dry-run suppressed this action.";
        if (step.Status == WorkflowStepStatus.Skipped)
            return "Step skipped.";
        return null;
    }

    private static IReadOnlyList<string>? NormalizeResumeStepIds(IReadOnlyCollection<string>? stepIds)
    {
        if (stepIds is null || stepIds.Count == 0) return null;
        return stepIds.Where(stepId => !string.IsNullOrWhiteSpace(stepId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(stepId => stepId, StringComparer.Ordinal)
            .ToArray();
    }

    private static void Validate(WorkflowTraceRecord trace)
    {
        if (trace.TraceId == Guid.Empty) throw new InvalidDataException("Workflow trace id is invalid.");
        if (string.IsNullOrWhiteSpace(trace.WorkflowId) || trace.WorkflowId.Length > 96) throw new InvalidDataException("Workflow trace workflow id is invalid.");
        if (string.IsNullOrWhiteSpace(trace.WorkflowName) || trace.WorkflowName.Length > 120) throw new InvalidDataException("Workflow trace workflow name is invalid.");
        if (trace.SchemaVersion is < 1 or > 5) throw new InvalidDataException("Workflow trace schema is invalid.");
        if (trace.FinishedUtc < trace.StartedUtc) throw new InvalidDataException("Workflow trace timestamps are invalid.");
        if (trace.AssetId == Guid.Empty) throw new InvalidDataException("Workflow trace asset id is invalid.");
        if (trace.ResumedFromTraceId == Guid.Empty || trace.ResumedFromTraceId == trace.TraceId) throw new InvalidDataException("Workflow trace resume ancestry is invalid.");
        if (trace.ResumeCompletedSideEffectStepIds is { Count: > 64 }) throw new InvalidDataException("Workflow trace resume side-effect set is too large.");
        if (trace.ResumeCompletedSideEffectStepIds is not null)
        {
            var uniqueResumeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var stepId in trace.ResumeCompletedSideEffectStepIds)
            {
                if (string.IsNullOrWhiteSpace(stepId) || stepId.Length > 96 || !uniqueResumeIds.Add(stepId))
                    throw new InvalidDataException("Workflow trace resume side-effect ids are invalid.");
            }
        }
        if (trace.WorkflowFingerprint is not null && !System.Text.RegularExpressions.Regex.IsMatch(trace.WorkflowFingerprint, "^[0-9a-f]{64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidDataException("Workflow trace fingerprint is invalid.");
        if (trace.Steps is null || trace.Steps.Count > 64) throw new InvalidDataException("Workflow trace contains an invalid step count.");
        foreach (var step in trace.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.StepId) || step.StepId.Length > 96) throw new InvalidDataException("Workflow trace step id is invalid.");
            if (string.IsNullOrWhiteSpace(step.Kind) || step.Kind.Length > 64) throw new InvalidDataException("Workflow trace step kind is invalid.");
            if (string.IsNullOrWhiteSpace(step.Status) || step.Status.Length > 32) throw new InvalidDataException("Workflow trace step status is invalid.");
            if (step.Attempts is < 0 or > 5 || step.FinishedUtc < step.StartedUtc || step.DurationMilliseconds < 0)
                throw new InvalidDataException("Workflow trace step timing is invalid.");
            if (step.Message is { Length: > WorkflowRuntimePolicy.MaximumTraceMessageLength })
                throw new InvalidDataException("Workflow trace message is too long.");
        }
    }
}
