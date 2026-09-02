using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Ai;
using Magic.Capture.Core.Storage;

namespace Magic.Capture.App.Ai.Provider;

internal sealed class AiProviderProfileStore
{
    private readonly string _path;
    private bool _writeEnabled;

    public AiProviderProfileStore(AppPaths paths) => _path = paths.AiProvidersFile;

    public async Task<AiProviderProfileState> LoadAsync(CancellationToken cancellationToken = default)
    {
        _writeEnabled = false;
        var state = await AtomicJsonFile.ReadAsync<AiProviderProfileState>(
            _path, cancellationToken, LocalConfigurationLimits.MaximumAiProviderJsonBytes) ?? AiProviderProfileState.Empty;
        state = ValidateState(state);
        _writeEnabled = true;
        return state;
    }

    public Task SaveAsync(AiProviderProfileState state, CancellationToken cancellationToken = default)
    {
        if (!_writeEnabled) throw new InvalidOperationException("AI provider storage is not safely loaded; reload it before saving.");
        state = ValidateState(state);
        return AtomicJsonFile.WriteAsync(_path, state, cancellationToken, LocalConfigurationLimits.MaximumAiProviderJsonBytes);
    }

    private static AiProviderProfileState ValidateState(AiProviderProfileState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Profiles is null || state.Privacy is null) throw new InvalidDataException("AI provider state is incomplete.");
        LocalConfigurationLimits.ValidateCount(state.Profiles.Count, LocalConfigurationLimits.MaximumAiProviderProfiles, "AI provider profiles");
        if (!Enum.IsDefined(state.Privacy.RoutingMode)) throw new InvalidDataException("AI routing mode is invalid.");

        var ids = new HashSet<Guid>();
        foreach (var profile in state.Profiles)
        {
            if (profile is null) throw new InvalidDataException("AI provider state contains a null profile.");
            if (profile.Id == Guid.Empty || !ids.Add(profile.Id)) throw new InvalidDataException("AI provider ids must be non-empty and unique.");
            if (string.IsNullOrWhiteSpace(profile.DisplayName) || profile.DisplayName.Length > 120) throw new InvalidDataException("AI provider display name is invalid.");
            if (string.IsNullOrWhiteSpace(profile.BaseUri) || profile.BaseUri.Length > 2_048 || !AiEndpointPolicy.TryValidate(profile.BaseUri, out _))
                throw new InvalidDataException("AI provider endpoint is invalid.");
            if (string.IsNullOrWhiteSpace(profile.ModelId) || profile.ModelId.Length > 256) throw new InvalidDataException("AI provider model id is invalid.");
            if (profile.SecretId?.Length > 256) throw new InvalidDataException("AI provider secret id is too long.");
            if (profile.TimeoutSeconds is < 10 or > 600) throw new InvalidDataException("AI provider timeout is outside the supported range.");
            if (!Enum.IsDefined(profile.Kind) || !Enum.IsDefined(profile.ContextSize) || !Enum.IsDefined(profile.VisionQuality))
                throw new InvalidDataException("AI provider enum value is invalid.");
        }
        if (state.ActiveProfileId is { } active && !ids.Contains(active))
            throw new InvalidDataException("Active AI provider id does not exist in the profile list.");
        return state with { Profiles = state.Profiles.ToArray() };
    }
}
