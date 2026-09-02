using System.Globalization;

namespace Magic.Capture.Core.Workflows;

public static class WorkflowRuntimePolicy
{
    public const int MaximumParameters = 24;
    public const int MaximumParameterPromptLength = 240;
    public const int MaximumParameterValueLength = 4_096;
    public const int MaximumParameterChoices = 24;
    public const int MaximumChoiceLength = 160;
    public const int MaximumDelayMilliseconds = 60_000;
    public const int MaximumSubworkflowDepth = 4;
    public const int MaximumBatchAssets = 500;
    public const int MaximumTraceEntries = 100;
    public const int MaximumTraceMessageLength = 1_024;
    public const int MaximumLoopImages = 32;

    public static int ParseDelayMilliseconds(string? value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds) ||
            milliseconds < 0 || milliseconds > MaximumDelayMilliseconds)
            throw new InvalidDataException($"Workflow delay must be an integer between 0 and {MaximumDelayMilliseconds:N0} ms.");
        return milliseconds;
    }

    public static bool CanEnterSubworkflow(IReadOnlyList<string>? callStack, string workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId)) return false;
        callStack ??= [];
        return callStack.Count < MaximumSubworkflowDepth && !callStack.Contains(workflowId, StringComparer.Ordinal);
    }

    public static bool RequiresSchemaV5(WorkflowStepKind kind) => kind == WorkflowStepKind.ForEachImage;

    public static bool RequiresSchemaV4(WorkflowStepKind kind) => kind is
        WorkflowStepKind.PromptText or
        WorkflowStepKind.PromptChoice or
        WorkflowStepKind.Confirm or
        WorkflowStepKind.Delay or
        WorkflowStepKind.RunWorkflow;

    public static bool ParseLoopContinueOnError(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null || !options.TryGetValue("continueOnError", out var raw) || string.IsNullOrWhiteSpace(raw)) return false;
        if (!bool.TryParse(raw, out var value)) throw new InvalidDataException("ForEachImage option 'continueOnError' must be true or false.");
        return value;
    }

    public static bool IsResumeSkippableSideEffect(WorkflowStepKind kind) => kind is
        WorkflowStepKind.CopyImage or
        WorkflowStepKind.CopyText or
        WorkflowStepKind.SaveImage or
        WorkflowStepKind.PinImage or
        WorkflowStepKind.OpenEditor;

    public static bool IsResumeNonReplayable(WorkflowStepKind kind) => kind is
        WorkflowStepKind.RunMagicAction or
        WorkflowStepKind.CustomHttpDestination or
        WorkflowStepKind.RunLocalAction or
        WorkflowStepKind.RunWorkflow or
        WorkflowStepKind.ForEachImage;

    public static bool IsSideEffecting(WorkflowStepKind kind) => kind is
        WorkflowStepKind.CopyImage or
        WorkflowStepKind.CopyText or
        WorkflowStepKind.SaveImage or
        WorkflowStepKind.PinImage or
        WorkflowStepKind.OpenEditor or
        WorkflowStepKind.CustomHttpDestination or
        WorkflowStepKind.RunMagicAction or
        WorkflowStepKind.RunLocalAction;

    public static bool IsInteractive(WorkflowStepKind kind) => kind is
        WorkflowStepKind.PromptText or WorkflowStepKind.PromptChoice or WorkflowStepKind.Confirm;

    public static IReadOnlyList<string> ParseChoices(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null || !options.TryGetValue("choices", out var raw) || string.IsNullOrWhiteSpace(raw)) return [];
        return raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
