namespace Magic.Capture.Core.Ai;

public enum MagicActionVisionMode { None, Optional, Required }
public enum MagicActionOutputKind { PlainText, Markdown, StructuredJson }

public sealed record MagicActionDefinition(
    string Id,
    string Name,
    string Category,
    string SystemInstruction,
    string UserInstruction,
    AiCapability MinimumCapabilities,
    AiCapability PreferredCapabilities,
    MagicActionVisionMode VisionMode,
    MagicActionOutputKind OutputKind,
    bool RequiresEvidence,
    bool SupportsContextStack,
    bool IsBuiltIn,
    int SchemaVersion = 1);

public sealed record MagicActionValidationResult(bool IsValid, IReadOnlyList<string> Errors);
