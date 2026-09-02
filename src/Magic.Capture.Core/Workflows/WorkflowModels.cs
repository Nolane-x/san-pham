using Magic.Capture.Core.Commerce;

namespace Magic.Capture.Core.Workflows;

public enum WorkflowStepKind
{
    CopyImage,
    CopyText,
    SaveImage,
    PinImage,
    OpenEditor,
    RunOcr,
    ExtractTable,
    ScanBarcode,
    ExtractSignals,
    BeautifyImage,
    StripMetadata,
    ComputeHashes,
    ExportText,
    CustomHttpDestination,
    RunMagicAction,
    RunLocalAction,
    PromptText,
    PromptChoice,
    Confirm,
    Delay,
    RunWorkflow,
    ForEachImage
}

public enum WorkflowParameterKind
{
    Text,
    Choice,
    Boolean
}

public sealed record WorkflowParameterDefinition(
    string Name,
    string Prompt,
    WorkflowParameterKind Kind,
    bool Required = false,
    string? DefaultValue = null,
    IReadOnlyList<string>? Choices = null);

public sealed record WorkflowStep(
    string Id,
    WorkflowStepKind Kind,
    bool Required = true,
    string? Argument = null,
    string? OutputKey = null,
    IReadOnlyDictionary<string, string>? Options = null,
    string? Condition = null,
    int MaxAttempts = 1,
    int RetryDelayMilliseconds = 0,
    int TimeoutMilliseconds = 0,
    bool? IsEnabled = null);

public sealed record CaptureWorkflow(
    string Id,
    string Name,
    string Description,
    ProductTier RequiredTier,
    IReadOnlyList<WorkflowStep> Steps,
    int SchemaVersion = 1,
    bool IsBuiltIn = false,
    IReadOnlyDictionary<string, string>? Variables = null,
    IReadOnlyList<WorkflowParameterDefinition>? Parameters = null);

public sealed record WorkflowValidationResult(bool IsValid, IReadOnlyList<string> Errors);
