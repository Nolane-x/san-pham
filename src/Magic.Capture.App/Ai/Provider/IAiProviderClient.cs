namespace Magic.Capture.App.Ai.Provider;

internal interface IAiProviderClient
{
    AiProviderProfile Profile { get; }
    Task<AiProviderProbe> ProbeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default);
    Task<AiProviderResponse> GenerateAsync(AiProviderRequest request, CancellationToken cancellationToken = default);
}
