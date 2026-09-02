using Magic.Capture.App.Persistence;
using Magic.Capture.Core.LocalActions;
using Magic.Capture.Core.Storage;

namespace Magic.Capture.App.LocalActions;

internal sealed class LocalActionProfileStore
{
    private readonly AppPaths _paths;
    private bool _writeEnabled;

    public LocalActionProfileStore(AppPaths paths) => _paths = paths;

    public async Task<IReadOnlyList<LocalActionProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        _writeEnabled = false;
        var profiles = await AtomicJsonFile.ReadAsync<List<LocalActionProfile>>(
            _paths.LocalActionsFile, cancellationToken, LocalConfigurationLimits.MaximumLocalActionJsonBytes) ?? [];
        LocalConfigurationLimits.ValidateCount(profiles.Count, LocalConfigurationLimits.MaximumLocalActions, "Local Actions");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in profiles)
        {
            if (profile is null) throw new InvalidDataException("Local Action storage contains a null profile.");
            var validation = LocalActionProfileValidator.Validate(profile);
            if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
            if (!ids.Add(profile.Id)) throw new InvalidDataException($"Duplicate Local Action id: {profile.Id}");
        }

        _writeEnabled = true;
        return profiles.ToArray();
    }

    public async Task SaveAsync(IEnumerable<LocalActionProfile> profiles, CancellationToken cancellationToken = default)
    {
        if (!_writeEnabled) throw new InvalidOperationException("Local Action storage is not safely loaded; reload it before saving.");
        ArgumentNullException.ThrowIfNull(profiles);
        var array = profiles.Take(LocalConfigurationLimits.MaximumLocalActions + 1).ToArray();
        LocalConfigurationLimits.ValidateCount(array.Length, LocalConfigurationLimits.MaximumLocalActions, "Local Actions");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in array)
        {
            if (profile is null) throw new InvalidDataException("Local Action storage cannot contain null profiles.");
            var validation = LocalActionProfileValidator.Validate(profile);
            if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));
            if (!ids.Add(profile.Id)) throw new InvalidDataException($"Duplicate Local Action id: {profile.Id}");
        }

        await AtomicJsonFile.WriteAsync(_paths.LocalActionsFile, array, cancellationToken, LocalConfigurationLimits.MaximumLocalActionJsonBytes);
    }
}
