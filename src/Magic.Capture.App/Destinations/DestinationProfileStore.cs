using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Destinations;
using Magic.Capture.Core.Storage;

namespace Magic.Capture.App.Destinations;

internal sealed class DestinationProfileStore
{
    private readonly AppPaths _paths;
    private bool _writeEnabled;

    public DestinationProfileStore(AppPaths paths) => _paths = paths;

    public async Task<IReadOnlyList<CustomHttpDestination>> LoadAsync(CancellationToken cancellationToken = default)
    {
        _writeEnabled = false;
        var profiles = await AtomicJsonFile.ReadAsync<List<CustomHttpDestination>>(
            _paths.DestinationsFile, cancellationToken, LocalConfigurationLimits.MaximumDestinationJsonBytes) ?? [];
        LocalConfigurationLimits.ValidateCount(profiles.Count, LocalConfigurationLimits.MaximumDestinations, "Destinations");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in profiles)
        {
            if (profile is null) throw new InvalidDataException("Destination storage contains a null profile.");
            var validation = DestinationValidator.Validate(profile);
            if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
            if (!ids.Add(profile.Id)) throw new InvalidDataException($"Duplicate destination id: {profile.Id}");
        }
        _writeEnabled = true;
        return profiles.ToArray();
    }

    public async Task SaveAsync(IEnumerable<CustomHttpDestination> profiles, CancellationToken cancellationToken = default)
    {
        if (!_writeEnabled) throw new InvalidOperationException("Destination storage is not safely loaded; reload it before saving.");
        ArgumentNullException.ThrowIfNull(profiles);
        var array = profiles.Take(LocalConfigurationLimits.MaximumDestinations + 1).ToArray();
        LocalConfigurationLimits.ValidateCount(array.Length, LocalConfigurationLimits.MaximumDestinations, "Destinations");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in array)
        {
            if (profile is null) throw new InvalidDataException("Destination storage cannot contain null profiles.");
            var validation = DestinationValidator.Validate(profile);
            if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));
            if (!ids.Add(profile.Id)) throw new InvalidDataException($"Duplicate destination id: {profile.Id}");
        }
        await AtomicJsonFile.WriteAsync(_paths.DestinationsFile, array, cancellationToken, LocalConfigurationLimits.MaximumDestinationJsonBytes);
    }
}
