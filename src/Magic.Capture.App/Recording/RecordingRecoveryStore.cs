using System.Text.Json;
using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Recording;

namespace Magic.Capture.App.Recording;

internal sealed record RecordingSessionManifest(
    int SchemaVersion,
    Guid SessionId,
    RecordingSessionState State,
    RecordingTargetKind TargetKind,
    string TargetSummary,
    string FinalPath,
    string TemporaryPath,
    RecordingOptions Options,
    DateTimeOffset StartedUtc,
    DateTimeOffset UpdatedUtc,
    long FrameCount,
    long ActiveElapsedTicks,
    string? Failure = null,
    long AudioBlockCount = 0);

internal sealed record RecordingRecoveryResult(
    RecordingSessionManifest? Manifest,
    bool IsReadOnly,
    string? Warning);

internal sealed class RecordingRecoveryStore
{
    private const long MaximumJournalBytes = 256L * 1024;
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RecordingRecoveryStore(AppPaths paths) => _paths = paths;

    public async Task SaveAsync(RecordingSessionManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateManifest(manifest);
        if (!RecordingManifestPolicy.CanWriteSchema(manifest.SchemaVersion))
            throw new InvalidOperationException("A newer recording journal schema is in read-only recovery mode.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var futureSchema = await ProbeSchemaAsync(_paths.RecordingJournalFile, cancellationToken);
            if (futureSchema is > RecordingManifestPolicy.CurrentSchemaVersion)
                throw new InvalidOperationException("The existing recording journal was written by a newer Magic Capture Desktop version and will not be overwritten.");
            await AtomicJsonFile.WriteAsync(_paths.RecordingJournalFile, manifest, cancellationToken, MaximumJournalBytes);
        }
        finally { _gate.Release(); }
    }

    public async Task<RecordingRecoveryResult> LoadUnfinishedAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var schema = await ProbeSchemaAsync(_paths.RecordingJournalFile, cancellationToken);
            if (schema is > RecordingManifestPolicy.CurrentSchemaVersion)
                return new RecordingRecoveryResult(null, true,
                    $"An unfinished recording journal uses newer schema {schema}. It was left untouched for recovery by a newer Magic Capture Desktop version.");

            RecordingSessionManifest? manifest;
            try
            {
                manifest = await AtomicJsonFile.ReadAsync<RecordingSessionManifest>(_paths.RecordingJournalFile, cancellationToken, MaximumJournalBytes);
            }
            catch (InvalidDataException ex)
            {
                return new RecordingRecoveryResult(null, true, $"Recording recovery journal is unreadable: {ex.Message}");
            }
            if (manifest is null || !RecordingManifestPolicy.IsUnfinished(manifest.State))
                return new RecordingRecoveryResult(null, false, null);
            ValidateManifest(manifest);
            return new RecordingRecoveryResult(manifest, false, null);
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteBestEffort(_paths.RecordingJournalFile);
            DeleteBestEffort(_paths.RecordingJournalFile + ".bak");
        }
        finally { _gate.Release(); }
    }

    private static void ValidateManifest(RecordingSessionManifest manifest)
    {
        if (manifest.SchemaVersion < 0) throw new InvalidDataException("Recording journal schema is invalid.");
        if (manifest.SessionId == Guid.Empty) throw new InvalidDataException("Recording journal session id is invalid.");
        if (string.IsNullOrWhiteSpace(manifest.TargetSummary) || manifest.TargetSummary.Length > 512)
            throw new InvalidDataException("Recording journal target summary is invalid.");
        if (!Path.IsPathFullyQualified(manifest.FinalPath) || !Path.IsPathFullyQualified(manifest.TemporaryPath))
            throw new InvalidDataException("Recording journal paths must be fully qualified local paths.");
        var normalizedOptions = RecordingRules.Normalize(manifest.Options);
        RecordingOutputPolicy.ValidateCompatibility(normalizedOptions);
        var expectedExtension = RecordingOutputPolicy.Extension(normalizedOptions.OutputFormat);
        var expectedPartial = RecordingOutputPolicy.PartialSuffix(normalizedOptions.OutputFormat);
        if (!manifest.FinalPath.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Recording final output must use the {expectedExtension} extension.");
        if (!manifest.TemporaryPath.EndsWith(expectedPartial, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Recording temporary output must use the {expectedPartial} suffix.");
        var finalDirectory = Path.GetFullPath(Path.GetDirectoryName(manifest.FinalPath) ?? string.Empty);
        var tempDirectory = Path.GetFullPath(Path.GetDirectoryName(manifest.TemporaryPath) ?? string.Empty);
        if (!string.Equals(finalDirectory, tempDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Recording final and temporary files must share a directory for atomic finalization.");
        if (manifest.FrameCount < 0 || manifest.AudioBlockCount < 0 || manifest.ActiveElapsedTicks < 0)
            throw new InvalidDataException("Recording journal counters are invalid.");
    }

    private static async Task<int?> ProbeSchemaAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length <= 0 || stream.Length > MaximumJournalBytes) return null;
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase)) continue;
                return property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value) ? value : null;
            }
            return 0;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static void DeleteBestEffort(string path)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
