using Magic.Capture.Core.Workflows;

namespace Magic.Capture.App.Workflows;

internal sealed record WorkflowResumePlan(
    bool IsEligible,
    string Reason,
    Guid? AssetId,
    IReadOnlySet<string> CompletedSafeSideEffectStepIds)
{
    public static WorkflowResumePlan Reject(string reason) => new(false, reason, null, new HashSet<string>(StringComparer.Ordinal));
}

internal static class WorkflowResumePlanner
{
    public static WorkflowResumePlan CreatePlan(CaptureWorkflow workflow, WorkflowTraceRecord trace)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(trace);
        if (trace.Succeeded) return WorkflowResumePlan.Reject("Successful workflows do not need resume.");
        if (trace.DryRun) return WorkflowResumePlan.Reject("Dry-run traces cannot be resumed.");
        if (!string.Equals(trace.WorkflowId, workflow.Id, StringComparison.Ordinal)) return WorkflowResumePlan.Reject("The trace belongs to a different workflow.");
        if (trace.AssetId is null || trace.AssetId == Guid.Empty) return WorkflowResumePlan.Reject("The trace does not reference a recoverable History capture.");
        var fingerprint = WorkflowFingerprint.Compute(workflow);
        if (string.IsNullOrWhiteSpace(trace.WorkflowFingerprint) || !string.Equals(trace.WorkflowFingerprint, fingerprint, StringComparison.Ordinal))
            return WorkflowResumePlan.Reject("The workflow changed after this trace was created.");

        var byId = workflow.Steps.ToDictionary(step => step.Id, StringComparer.Ordinal);
        var safe = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stepId in trace.ResumeCompletedSideEffectStepIds ?? [])
        {
            if (!byId.TryGetValue(stepId, out var priorSafeStep) || !WorkflowRuntimePolicy.IsResumeSkippableSideEffect(priorSafeStep.Kind))
                return WorkflowResumePlan.Reject("The resume side-effect checkpoint no longer matches this workflow.");
            safe.Add(stepId);
        }

        foreach (var stepTrace in trace.Steps)
        {
            if (!byId.TryGetValue(stepTrace.StepId, out var step)) return WorkflowResumePlan.Reject("The workflow step layout changed after this trace was created.");
            if (!string.Equals(stepTrace.Kind, step.Kind.ToString(), StringComparison.Ordinal)) return WorkflowResumePlan.Reject("The workflow step kinds changed after this trace was created.");
            if (string.Equals(stepTrace.Status, WorkflowStepStatus.Failed.ToString(), StringComparison.Ordinal))
            {
                var failedStep = step;
                if (WorkflowRuntimePolicy.IsResumeNonReplayable(failedStep.Kind))
                    return WorkflowResumePlan.Reject($"Resume is unsafe because failed step '{failedStep.Kind}' may have produced non-replayable effects.");
                break;
            }
            if (!string.Equals(stepTrace.Status, WorkflowStepStatus.Succeeded.ToString(), StringComparison.Ordinal)) continue;
            if (WorkflowRuntimePolicy.IsResumeSkippableSideEffect(step.Kind))
            {
                safe.Add(step.Id);
                continue;
            }
            if (WorkflowRuntimePolicy.IsResumeNonReplayable(step.Kind))
                return WorkflowResumePlan.Reject($"Resume is unsafe because '{step.Kind}' already completed before the failure.");
        }

        return new WorkflowResumePlan(true, "Safe replay is available. Interactive inputs may be requested again.", trace.AssetId, safe);
    }
}
