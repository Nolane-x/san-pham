using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Commerce;

namespace Magic.Capture.App.Commerce;

internal sealed class TrialStateStore
{
    private readonly AppPaths _paths;
    public TrialStateStore(AppPaths paths) => _paths = paths;

    public async Task<TrialState> LoadOrCreateAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var stateFilesExist = File.Exists(_paths.TrialStateFile) || File.Exists(_paths.TrialStateFile + ".bak");
        var existing = await AtomicJsonFile.ReadAsync<TrialState>(_paths.TrialStateFile, cancellationToken);
        if (existing is not null)
        {
            if (!TrialStatePolicy.IsValidPersisted(existing))
                throw new InvalidDataException("Persisted trial state is invalid; refusing to reset the trial automatically.");
            return existing;
        }

        if (stateFilesExist)
            throw new InvalidDataException("Persisted trial state could not be read; refusing to replace it with a new trial.");

        var created = TrialState.Create(nowUtc);
        await SaveAsync(created, cancellationToken);
        return created;
    }

    public Task SaveAsync(TrialState state, CancellationToken cancellationToken = default)
    {
        if (!TrialStatePolicy.IsValidPersisted(state))
            throw new InvalidDataException("Trial state is invalid and cannot be persisted.");
        return AtomicJsonFile.WriteAsync(_paths.TrialStateFile, state, cancellationToken);
    }
}
