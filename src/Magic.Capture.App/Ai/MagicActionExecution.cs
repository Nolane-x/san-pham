using Magic.Capture.App.Capture;
using Magic.Capture.Core.Ai;
using Magic.Capture.Core.ScreenGraph;

namespace Magic.Capture.App.Ai;

internal sealed record MagicActionExecutionRequest(
    CaptureAsset Primary,
    MagicActionDefinition Action,
    string? UserQuestion,
    IReadOnlyList<CaptureAsset> Context);

internal sealed record MagicActionExecutionResult(
    AiActionResult Result,
    IReadOnlyList<ResolvedEvidence> Evidence,
    ScreenGraphDocument Graph,
    AiPayloadSummary Payload,
    string ProviderName,
    string ModelId,
    bool ProviderIsLocal,
    bool FromCache = false);
internal sealed record MagicActionExecutionPreview(
    string ProviderName,
    string ModelId,
    bool ProviderIsLocal,
    AiPayloadSummary Payload,
    AiRoutingMode RoutingMode);

