using Magic.Capture.Core.Ai;

namespace Magic.Capture.App.Ai.Provider;

internal sealed record AiImageAttachment(string MimeType, byte[] Bytes, string Label)
{
    public string ToDataUrl() => $"data:{MimeType};base64,{Convert.ToBase64String(Bytes)}";
}

internal sealed record AiProviderRequest(string Prompt, MagicActionOutputKind OutputKind, IReadOnlyList<AiImageAttachment> Images);
internal sealed record AiProviderResponse(string Text, int? InputTokens = null, int? OutputTokens = null);
internal sealed record AiProviderProbe(bool Success, string Message);

internal sealed class AiProviderException : Exception
{
    public AiProviderException(string provider, string message, int? statusCode = null) : base($"{provider}: {message}") => StatusCode = statusCode;
    public int? StatusCode { get; }
}
