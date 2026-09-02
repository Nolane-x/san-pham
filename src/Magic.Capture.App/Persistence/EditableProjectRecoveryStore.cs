using System.Text.Json;
using Magic.Capture.App.Imaging;
using Magic.Capture.Core.Projects;

namespace Magic.Capture.App.Persistence;

internal sealed record EditableProjectRecoveryItem(
    EditableProjectRecoveryJournal Journal,
    string JournalPath,
    string SnapshotPath);

internal sealed class EditableProjectRecoveryStore
{
    private const int MaximumJournalFilesToInspect = 64;
    private static readonly TimeSpan StaleTempAge = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly EditableProjectService _editableProjects;
    private readonly LocalLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EditableProjectRecoveryStore(AppPaths paths, EditableProjectService editableProjects, LocalLog log)
    {
        _paths = paths;
        _editableProjects = editableProjects;
        _log = log;
    }

    public async Task SaveAsync(
        Guid sessionId,
        byte[] basePng,
        EditableProjectManifest manifest,
        long dirtyRevision,
        string? originalProjectDisplayName = null,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Recovery session id is required.", nameof(sessionId));
        ArgumentNullException.ThrowIfNull(basePng);
        ArgumentNullException.ThrowIfNull(manifest);
        if (dirtyRevision <= 0) throw new ArgumentOutOfRangeException(nameof(dirtyRevision));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_paths.EditableProjectRecoveryRoot);
            var snapshotFileName = EditableProjectRecoveryPolicy.BuildSnapshotFileName(sessionId, dirtyRevision);
            var snapshotPath = Path.Combine(_paths.EditableProjectRecoveryRoot, snapshotFileName);
            var journalPath = Path.Combine(_paths.EditableProjectRecoveryRoot, $"{sessionId:N}.json");
            var now = DateTimeOffset.UtcNow;
            var created = now;
            string? previousSnapshotFileName = null;
            var existing = await TryReadJournalAsync(journalPath, cancellationToken);
            if (existing is not null
                && existing.SessionId == sessionId
                && EditableProjectRecoveryPolicy.Validate(existing, now).IsValid)
            {
                if (dirtyRevision < existing.DirtyRevision)
                    throw new InvalidOperationException("A newer recovery revision is already stored for this session.");
                created = existing.CreatedUtc;
                previousSnapshotFileName = existing.SnapshotFileName;
            }

            // The snapshot is immutable per revision. Only after it is fully promoted do we
            // atomically replace the journal pointer, so a crash always leaves either the old
            // complete pair or the new complete pair recoverable.
            await _editableProjects.SaveAsync(snapshotPath, basePng, manifest, cancellationToken);

            var journal = new EditableProjectRecoveryJournal(
                EditableProjectRecoveryPolicy.CurrentJournalSchemaVersion,
                sessionId,
                manifest.ProjectId,
                created,
                now,
                snapshotFileName,
                manifest.Width,
                manifest.Height,
                dirtyRevision,
                NormalizeDisplayName(originalProjectDisplayName));
            var validation = EditableProjectRecoveryPolicy.Validate(journal, now);
            if (!validation.IsValid) throw new InvalidDataException(validation.Error ?? "Recovery journal is invalid.");

            var bytes = JsonSerializer.SerializeToUtf8Bytes(journal, JsonOptions);
            if (bytes.LongLength <= 0 || bytes.LongLength > EditableProjectRecoveryPolicy.MaximumJournalBytes)
                throw new InvalidDataException("Recovery journal exceeds the safe size limit.");

            var tempJournal = journalPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllBytesAsync(tempJournal, bytes, cancellationToken);
                File.Move(tempJournal, journalPath, overwrite: true);
            }
            finally
            {
                TryDeleteFile(tempJournal);
            }

            if (!string.IsNullOrWhiteSpace(previousSnapshotFileName)
                && !string.Equals(previousSnapshotFileName, snapshotFileName, StringComparison.OrdinalIgnoreCase))
                TryDeleteFile(Path.Combine(_paths.EditableProjectRecoveryRoot, previousSnapshotFileName));

            await PruneCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<EditableProjectRecoveryItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await PruneCoreAsync(cancellationToken);
            return await ReadCandidatesCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EditableProjectPackage> LoadAsync(EditableProjectRecoveryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var validation = EditableProjectRecoveryPolicy.Validate(item.Journal, DateTimeOffset.UtcNow);
            if (!validation.IsValid) throw new InvalidDataException(validation.Error ?? "Recovery journal is invalid.");
            EnsureExpectedPaths(item);
            var currentJournal = await TryReadJournalAsync(item.JournalPath, cancellationToken);
            var currentValidation = EditableProjectRecoveryPolicy.Validate(currentJournal, DateTimeOffset.UtcNow);
            if (currentJournal is null || !currentValidation.IsValid || currentJournal != item.Journal)
                throw new InvalidDataException("Recovery journal changed after it was discovered.");

            var info = new FileInfo(item.SnapshotPath);
            EditableProjectArchivePolicy.ValidateArchiveLength(info.Length);
            var package = await _editableProjects.LoadAsync(item.SnapshotPath, cancellationToken);
            if (package.Manifest.ProjectId != item.Journal.ProjectId)
                throw new InvalidDataException("Recovery snapshot project id does not match its journal.");
            if (package.Manifest.Width != item.Journal.Width || package.Manifest.Height != item.Journal.Height)
                throw new InvalidDataException("Recovery snapshot dimensions do not match its journal.");
            return package;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sessionId == Guid.Empty) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            DeleteCore(sessionId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PruneAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await PruneCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PruneCoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.EditableProjectRecoveryRoot);
        var now = DateTimeOffset.UtcNow;
        PruneStaleTempFiles(now);

        var journals = new List<(EditableProjectRecoveryJournal Journal, string JournalPath)>();
        foreach (var journalPath in Directory.EnumerateFiles(_paths.EditableProjectRecoveryRoot, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumJournalFilesToInspect))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var journal = await TryReadJournalAsync(journalPath, cancellationToken);
                var validation = EditableProjectRecoveryPolicy.Validate(journal, now);
                if (journal is null || !validation.IsValid)
                {
                    RemoveJournalAndSafeSnapshot(journalPath, journal);
                    continue;
                }

                var expectedJournal = Path.Combine(_paths.EditableProjectRecoveryRoot, $"{journal.SessionId:N}.json");
                if (!string.Equals(Path.GetFullPath(journalPath), Path.GetFullPath(expectedJournal), StringComparison.OrdinalIgnoreCase))
                {
                    RemoveJournalAndSafeSnapshot(journalPath, journal);
                    continue;
                }

                var snapshotPath = Path.Combine(_paths.EditableProjectRecoveryRoot, journal.SnapshotFileName);
                if (!File.Exists(snapshotPath))
                {
                    TryDeleteFile(journalPath);
                    continue;
                }

                var snapshotLength = new FileInfo(snapshotPath).Length;
                if (snapshotLength <= 0 || snapshotLength > EditableProjectArchivePolicy.MaximumArchiveBytes)
                {
                    RemoveJournalAndSafeSnapshot(journalPath, journal);
                    continue;
                }
                journals.Add((journal, journalPath));
            }
            catch (Exception ex) when (IsExpectedRecoveryFailure(ex))
            {
                _log.Error("EditableProjectRecoveryPrune", ex);
                TryDeleteFile(journalPath);
            }
        }

        var selected = EditableProjectRecoveryPolicy.SelectCandidates(journals.Select(item => item.Journal), now);
        var keep = selected.Select(item => item.Journal.SessionId).ToHashSet();
        foreach (var item in journals)
        {
            if (!keep.Contains(item.Journal.SessionId)) RemoveJournalAndSafeSnapshot(item.JournalPath, item.Journal);
        }

        var knownSnapshots = selected.Select(item => item.Journal.SnapshotFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshotPath in Directory.EnumerateFiles(_paths.EditableProjectRecoveryRoot, "*.magiccapture", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumJournalFilesToInspect))
        {
            if (!knownSnapshots.Contains(Path.GetFileName(snapshotPath))) TryDeleteFile(snapshotPath);
        }
    }

    private async Task<IReadOnlyList<EditableProjectRecoveryItem>> ReadCandidatesCoreAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var bySession = new Dictionary<Guid, (EditableProjectRecoveryJournal Journal, string JournalPath)>();
        foreach (var journalPath in Directory.EnumerateFiles(_paths.EditableProjectRecoveryRoot, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumJournalFilesToInspect))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var journal = await TryReadJournalAsync(journalPath, cancellationToken);
                if (journal is null || !EditableProjectRecoveryPolicy.Validate(journal, now).IsValid) continue;
                bySession[journal.SessionId] = (journal, journalPath);
            }
            catch (Exception ex) when (IsExpectedRecoveryFailure(ex))
            {
                _log.Error("EditableProjectRecoveryList", ex);
            }
        }

        var selected = EditableProjectRecoveryPolicy.SelectCandidates(bySession.Values.Select(item => item.Journal), now);
        var result = new List<EditableProjectRecoveryItem>(selected.Count);
        foreach (var candidate in selected)
        {
            if (!bySession.TryGetValue(candidate.Journal.SessionId, out var stored)) continue;
            var snapshotPath = Path.Combine(_paths.EditableProjectRecoveryRoot, candidate.Journal.SnapshotFileName);
            if (!File.Exists(snapshotPath)) continue;
            result.Add(new EditableProjectRecoveryItem(candidate.Journal, stored.JournalPath, snapshotPath));
        }
        return result;
    }

    private async Task<EditableProjectRecoveryJournal?> TryReadJournalAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > EditableProjectRecoveryPolicy.MaximumJournalBytes) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, useAsync: true);
        var bytes = await BoundedStreamReader.ReadExactAsync(stream, info.Length, EditableProjectRecoveryPolicy.MaximumJournalBytes, cancellationToken);
        return JsonSerializer.Deserialize<EditableProjectRecoveryJournal>(bytes, JsonOptions);
    }

    private void EnsureExpectedPaths(EditableProjectRecoveryItem item)
    {
        var expectedJournal = Path.Combine(_paths.EditableProjectRecoveryRoot, $"{item.Journal.SessionId:N}.json");
        var expectedSnapshot = Path.Combine(_paths.EditableProjectRecoveryRoot, item.Journal.SnapshotFileName);
        if (!string.Equals(Path.GetFullPath(item.JournalPath), Path.GetFullPath(expectedJournal), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFullPath(item.SnapshotPath), Path.GetFullPath(expectedSnapshot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Recovery item paths are inconsistent with the journal.");
    }

    private void DeleteCore(Guid sessionId)
    {
        var stem = sessionId.ToString("N");
        TryDeleteFile(Path.Combine(_paths.EditableProjectRecoveryRoot, stem + ".json"));
        TryDeleteFile(Path.Combine(_paths.EditableProjectRecoveryRoot, stem + ".magiccapture")); // pre-4.10 development cleanup
        foreach (var snapshotPath in Directory.EnumerateFiles(
                     _paths.EditableProjectRecoveryRoot,
                     stem + "-*.magiccapture",
                     SearchOption.TopDirectoryOnly)
                 .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                 .Take(MaximumJournalFilesToInspect))
            TryDeleteFile(snapshotPath);
    }

    private void RemoveJournalAndSafeSnapshot(string journalPath, EditableProjectRecoveryJournal? journal)
    {
        TryDeleteFile(journalPath);
        if (journal is not null && CanDeleteJournalSnapshot(journalPath, journal))
            TryDeleteFile(Path.Combine(_paths.EditableProjectRecoveryRoot, journal.SnapshotFileName));
    }

    private bool CanDeleteJournalSnapshot(string journalPath, EditableProjectRecoveryJournal journal)
    {
        if (!EditableProjectRecoveryPolicy.IsSafeSnapshotFileName(journal.SnapshotFileName)) return false;
        if (!string.Equals(
                journal.SnapshotFileName,
                EditableProjectRecoveryPolicy.BuildSnapshotFileName(journal.SessionId, journal.DirtyRevision),
                StringComparison.OrdinalIgnoreCase)) return false;

        var expectedJournal = Path.Combine(_paths.EditableProjectRecoveryRoot, $"{journal.SessionId:N}.json");
        return string.Equals(
            Path.GetFullPath(journalPath),
            Path.GetFullPath(expectedJournal),
            StringComparison.OrdinalIgnoreCase);
    }

    private void PruneStaleTempFiles(DateTimeOffset now)
    {
        foreach (var tempPath in Directory.EnumerateFiles(_paths.EditableProjectRecoveryRoot, "*.tmp-*", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumJournalFilesToInspect))
        {
            try
            {
                var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(tempPath), TimeSpan.Zero);
                if (now - lastWrite >= StaleTempAge) TryDeleteFile(tempPath);
            }
            catch (Exception ex) when (IsExpectedRecoveryFailure(ex))
            {
                _log.Error("EditableProjectRecoveryTempPrune", ex);
            }
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (IsExpectedRecoveryFailure(ex))
        {
            _log.Error("EditableProjectRecoveryDelete", ex);
        }
    }

    private static string? NormalizeDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var name = Path.GetFileName(value.Trim());
        return name.Length <= EditableProjectRecoveryPolicy.MaximumDisplayNameLength
            ? name
            : name[..EditableProjectRecoveryPolicy.MaximumDisplayNameLength];
    }

    private static bool IsExpectedRecoveryFailure(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or JsonException
            or NotSupportedException
            or System.Security.SecurityException;
}
