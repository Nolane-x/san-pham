namespace Magic.Capture.Core.Ai;

[Flags]
public enum AiCapability
{
    None = 0,
    TextInput = 1 << 0,
    VisionInput = 1 << 1,
    MultipleImages = 1 << 2,
    StructuredJson = 1 << 3,
    JsonSchema = 1 << 4,
    Streaming = 1 << 5,
    ToolCalling = 1 << 6,
    Reasoning = 1 << 7,
    LocalEndpoint = 1 << 8
}

public enum AiContextSizeClass { Small, Medium, Large }
public enum AiVisionQuality { None, Basic, Strong }

public sealed record AiModelProfile(string ModelId, AiCapability Capabilities, AiContextSizeClass ContextSize, AiVisionQuality VisionQuality)
{
    public bool Has(AiCapability capability) => (Capabilities & capability) == capability;
}
