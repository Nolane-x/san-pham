using System.Text.Json;
using Magic.Capture.Core.Settings;
using Magic.Capture.Core.Storage;

namespace Magic.Capture.App.Persistence;

internal sealed record SettingsLoadResult(AppSettings Settings, bool UsedFallback, string? Warning);

internal sealed class SettingsStore
{
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _persistenceHealthy;

    public SettingsStore(AppPaths paths) => _paths = paths;

    public bool IsPersistenceHealthy => _persistenceHealthy;

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                var futureSchema = await ProbeFutureSchemaAsync(cancellationToken);
                if (futureSchema is not null)
                {
                    _persistenceHealthy = false;
                    return Fallback($"Settings were written by a newer Magic Capture Desktop persistence schema ({futureSchema}). Safe defaults are being used in read-only recovery mode; the existing settings file will not be overwritten.");
                }

                var loaded = await AtomicJsonFile.ReadAsync<AppSettings>(
                    _paths.SettingsFile, cancellationToken, LocalConfigurationLimits.MaximumSettingsJsonBytes);
                _persistenceHealthy = true;
                return new SettingsLoadResult(AppSettingsRules.NormalizeForRuntime(loaded), false, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidDataException)
            {
                _persistenceHealthy = false;
                return Fallback("Settings could not be recovered from the local file or its backup. Safe defaults are being used for this session; automatic settings writes are disabled until you explicitly reset settings storage.");
            }
            catch (IOException)
            {
                _persistenceHealthy = false;
                return Fallback("Settings are temporarily unavailable because the local file could not be read. Safe defaults are being used for this session; automatic settings writes are disabled to protect the existing file.");
            }
            catch (UnauthorizedAccessException)
            {
                _persistenceHealthy = false;
                return Fallback("Settings are temporarily unavailable because Windows denied access to the local settings file. Safe defaults are being used for this session; automatic settings writes are disabled to protect the existing file.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }


    public async Task<AppSettings> LoadStrictAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var futureSchema = await ProbeFutureSchemaAsync(cancellationToken);
            if (futureSchema is not null)
                throw new InvalidDataException($"Settings were written by a newer Magic Capture Desktop persistence schema ({futureSchema}).");
            var loaded = await AtomicJsonFile.ReadAsync<AppSettings>(
                _paths.SettingsFile, cancellationToken, LocalConfigurationLimits.MaximumSettingsJsonBytes)
                ?? throw new InvalidDataException("Settings file is missing.");
            var normalized = AppSettingsRules.NormalizeForRuntime(loaded);
            _persistenceHealthy = true;
            return normalized;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsurePersistenceHealthy();
            await AtomicJsonFile.WriteAsync(_paths.SettingsFile, AppSettingsRules.NormalizeForRuntime(settings), cancellationToken,
                LocalConfigurationLimits.MaximumSettingsJsonBytes);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TrySaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_persistenceHealthy) return false;
            try
            {
                await AtomicJsonFile.WriteAsync(_paths.SettingsFile, AppSettingsRules.NormalizeForRuntime(settings), cancellationToken,
                    LocalConfigurationLimits.MaximumSettingsJsonBytes);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreserveForRecovery(_paths.SettingsFile);
            PreserveForRecovery(_paths.SettingsFile + ".bak");
            await AtomicJsonFile.WriteAsync(_paths.SettingsFile, AppSettingsRules.NormalizeForRuntime(settings), cancellationToken,
                LocalConfigurationLimits.MaximumSettingsJsonBytes);
            _persistenceHealthy = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<int?> ProbeFutureSchemaAsync(CancellationToken cancellationToken)
    {
        foreach (var path in new[] { _paths.SettingsFile, _paths.SettingsFile + ".bak" })
        {
            if (!File.Exists(path)) continue;
            var schema = await TryProbeSchemaAsync(path, cancellationToken);
            if (schema is > AppSettingsRules.CurrentPersistenceSchemaVersion) return schema;
            if (path == _paths.SettingsFile && schema is not null) return null;
        }
        return null;
    }

    private static async Task<int?> TryProbeSchemaAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length <= 0 || stream.Length > LocalConfigurationLimits.MaximumSettingsJsonBytes) return null;
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, "persistenceSchemaVersion", StringComparison.OrdinalIgnoreCase)) continue;
                return property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var version) ? version : null;
            }
            return 0;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    private void EnsurePersistenceHealthy()
    {
        if (!_persistenceHealthy)
            throw new InvalidOperationException("Settings storage is in recovery mode. The existing settings file will not be overwritten until you explicitly reset settings storage.");
    }

    private static void PreserveForRecovery(string path)
    {
        if (!File.Exists(path)) return;
        var recoveryPath = $"{path}.recovery-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        File.Move(path, recoveryPath, overwrite: false);
    }

    private static SettingsLoadResult Fallback(string warning) =>
        new(AppSettingsRules.NormalizeForRuntime(new AppSettings()), true, warning);
}
