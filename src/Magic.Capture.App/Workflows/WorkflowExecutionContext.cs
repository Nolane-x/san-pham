using Magic.Capture.App.Ai;
using Magic.Capture.App.Capture;
using Magic.Capture.App.LocalActions;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.App.Workflows;

internal sealed record WorkflowExecutionContext(
    CaptureAsset Asset,
    Func<CaptureAsset, CancellationToken, Task>? SaveImageAsync = null,
    Action<CaptureAsset>? PinImage = null,
    Action<CaptureAsset>? OpenEditor = null,
    IReadOnlyList<CaptureAsset>? AiContext = null,
    Func<MagicActionExecutionRequest, MagicActionExecutionPreview, CancellationToken, Task<bool>>? ConfirmCloudMagicActionAsync = null,
    IReadOnlyDictionary<string, string>? InitialVariables = null,
    Func<LocalActionApprovalRequest, CancellationToken, Task<bool>>? ConfirmLocalActionApprovalAsync = null,
    Func<string, string?, CancellationToken, Task<string?>>? PromptTextAsync = null,
    Func<string, IReadOnlyList<string>, string?, CancellationToken, Task<string?>>? PromptChoiceAsync = null,
    Func<string, CancellationToken, Task<bool?>>? ConfirmStepAsync = null,
    Func<string, CancellationToken, Task<CaptureWorkflow?>>? ResolveWorkflowAsync = null,
    bool DryRun = false,
    IReadOnlyList<string>? WorkflowCallStack = null,
    IReadOnlyList<CaptureAsset>? LoopAssets = null,
    bool IsResume = false,
    IReadOnlySet<string>? ResumeCompletedSideEffectStepIds = null);

internal enum WorkflowStepStatus
{
    Succeeded,
    Failed,
    Skipped,
    WouldRun
}

internal sealed record WorkflowStepResult(
    string StepId,
    WorkflowStepKind Kind,
    WorkflowStepStatus Status,
    string? Message,
    string? OutputKey,
    int Attempts,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc)
{
    public bool Succeeded => Status != WorkflowStepStatus.Failed;
    public bool WasSkipped => Status is WorkflowStepStatus.Skipped or WorkflowStepStatus.WouldRun;
    public TimeSpan Duration => FinishedUtc >= StartedUtc ? FinishedUtc - StartedUtc : TimeSpan.Zero;
}

internal sealed record WorkflowLoopSummary(int Requested, int Succeeded, int Failed);

internal sealed record WorkflowExecutionResult(
    bool Succeeded,
    IReadOnlyList<WorkflowStepResult> Steps,
    IReadOnlyDictionary<string, object?> Values,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    bool DryRun);
