using System.Text.Json;
using Magic.Capture.App.Imaging;
using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Documentation;
using Magic.Capture.Core.Recovery;

namespace Magic.Capture.App.Documentation;

internal sealed record DocumentationRecoveryItem(
    WorkspaceRecoveryJournal Journal,
    string JournalPath,
    string SnapshotPath);

internal sealed class DocumentationRecoveryStore
{
    private const int MaximumJournalFilesToInspect = 64;
    private static readonly TimeSpan StaleTempAge = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly DocumentationProjectStore _projects;
    private readonly LocalLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DocumentationRecoveryStore(AppPaths paths, DocumentationProjectStore projects, LocalLog log)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task SaveAsync(
        Guid sessionId,
        DocumentationProject project,
        IReadOnlyDictionary<string, byte[]> images,
        byte[]? logoPng,
        long dirtyRevision,
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Recovery session id is required.", nameof(sessionId));
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(images);
        if (dirtyRevision <= 0) throw new ArgumentOutOfRangeException(nameof(dirtyRevision));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_paths.DocumentationRecoveryRoot);
            var kind = WorkspaceRecoveryKind.Documentation;
            var snapshotFileName = WorkspaceRecoveryPolicy.BuildSnapshotFileName(kind, sessionId, dirtyRevision);
            var snapshotPath = Path.Combine(_paths.DocumentationRecoveryRoot, snapshotFileName);
            var journalPath = Path.Combine(_paths.DocumentationRecoveryRoot, $"{sessionId:N}.json");
            var now = DateTimeOffset.UtcNow;
            var created = now;
            string? previousSnapshotFileName = null;

            var existing = await TryReadJournalAsync(journalPath, cancellationToken);
            if (existing is not null && existing.SessionId == sessionId && existing.Kind == kind && WorkspaceRecoveryPolicy.Validate(existing, now).IsValid)
            {
                if (dirtyRevision < existing.DirtyRevision)
                    throw new InvalidOperationException("A newer documentation recovery revision is already stored for this session.");
                created = existing.CreatedUtc;
                previousSnapshotFileName = existing.SnapshotFileName;
            }

            // Snapshot first, journal pointer second. A crash therefore leaves at least one
            // complete pair instead of a journal that points at a partially written package.
            await _projects.SaveAsync(snapshotPath, project, images, logoPng, cancellationToken);

            var journal = new WorkspaceRecoveryJournal(
                WorkspaceRecoveryPolicy.CurrentJournalSchemaVersion,
                kind,
                sessionId,
                created,
                now,
                snapshotFileName,
                dirtyRevision,
                NormalizeDisplayName(displayName));
            var validation = WorkspaceRecoveryPolicy.Validate(journal, now);
            if (!validation.IsValid) throw new InvalidDataException(validation.Error ?? "Documentation recovery journal is invalid.");

            var bytes = JsonSerializer.SerializeToUtf8Bytes(journal, JsonOptions);
            if (bytes.LongLength <= 0 || bytes.LongLength > WorkspaceRecoveryPolicy.MaximumJournalBytes)
                throw new InvalidDataException("Documentation recovery journal exceeds the safe size limit.");

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
                TryDeleteFile(Path.Combine(_paths.DocumentationRecoveryRoot, previousSnapshotFileName));

            await PruneCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<DocumentationRecoveryItem>> ListAsync(CancellationToken cancellationToken = default)
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

    public async Task<DocumentationProjectPackage> LoadAsync(DocumentationRecoveryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var validation = WorkspaceRecoveryPolicy.Validate(item.Journal, now);
            if (!validation.IsValid || item.Journal.Kind != WorkspaceRecoveryKind.Documentation)
                throw new InvalidDataException(validation.Error ?? "Documentation recovery journal is invalid.");
            EnsureExpectedPaths(item);

            var currentJournal = await TryReadJournalAsync(item.JournalPath, cancellationToken);
            var currentValidation = WorkspaceRecoveryPolicy.Validate(currentJournal, now);
            if (currentJournal is null || currentJournal.Kind != WorkspaceRecoveryKind.Documentation || !currentValidation.IsValid || currentJournal != item.Journal)
                throw new InvalidDataException("Documentation recovery journal changed after it was discovered.");

            DocumentationArchivePolicy.ValidateArchiveLength(new FileInfo(item.SnapshotPath).Length);
            return await _projects.LoadAsync(item.SnapshotPath, cancellationToken);
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
        try { DeleteCore(sessionId); }
        finally { _gate.Release(); }
    }

    public async Task PruneAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { await PruneCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task PruneCoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.DocumentationRecoveryRoot);
        var now = DateTimeOffset.UtcNow;
        PruneStaleTempFiles(now);
        var journals = new List<(WorkspaceRecoveryJournal Journal, string JournalPath)>();

        foreach (var journalPath in Directory.EnumerateFiles(_paths.DocumentationRecoveryRoot, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumJournalFilesToInspect))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var journal = await TryReadJournalAsync(journalPath, cancellationToken);
                var validation = WorkspaceRecoveryPolicy.Validate(journal, now);
                if (journal is null || journal.Kind != WorkspaceRecoveryKind.Documentation || !validation.IsValid)
                {
                    RemoveJournalAndSafeSnapshot(journalPath, journal);
                    continue;
                }

                var expectedJournal = Path.Combine(_paths.DocumentationRecoveryRoot, $"{journal.SessionId:N}.json");
                if (!SamePath(journalPath, expectedJournal))
                {
                    RemoveJournalAndSafeSnapshot(journalPath, journal);
                    continue;
                }

                var snapshotPath = Path.Combine(_paths.DocumentationRecoveryRoot, journal.SnapshotFileName);
                if (!File.Exists(snapshotPath))
                {
                    TryDeleteFile(journalPath);
                    continue;
                }
                DocumentationArchivePolicy.ValidateArchiveLength(new FileInfo(snapshotPath).Length);
                journals.Add((journal, journalPath));
            }
            catch (Exception ex) when (IsExpectedRecoveryFailure(ex))
            {
                _log.Error("DocumentationRecoveryPrune", ex);
                TryDeleteFile(journalPath);
            }
        }

        var selected = WorkspaceRecoveryPolicy.SelectCandidates(journals.Select(item => item.Journal), WorkspaceRecoveryKind.Documentation, now);
        var keep = selected.Select(item => item.Journal.SessionId).ToHashSet();
        foreach (var item in journals)
            if (!keep.Contains(item.Journal.SessionId)) RemoveJournalAndSafeSnapshot(item.JournalPath, item.Journal);

        var knownSnapshots = selected.Select(item => item.Journal.SnapshotFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshotPath in Directory.EnumerateFiles(_paths.DocumentationRecoveryRoot, "*.magicdoc", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumJournalFilesToInspect))
            if (!knownSnapshots.Contains(Path.GetFileName(snapshotPath))) TryDeleteFile(snapshotPath);
    }

    private async Task<IReadOnlyList<DocumentationRecoveryItem>> ReadCandidatesCoreAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var bySession = new Dictionary<Guid, (WorkspaceRecoveryJournal Journal, string JournalPath)>();
        foreach (var journalPath in Directory.EnumerateFiles(_paths.DocumentationRecoveryRoot, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumJournalFilesToInspect))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var journal = await TryReadJournalAsync(journalPath, cancellationToken);
                if (journal is null || journal.Kind != WorkspaceRecoveryKind.Documentation || !WorkspaceRecoveryPolicy.Validate(journal, now).IsValid) continue;
                bySession[journal.SessionId] = (journal, journalPath);
            }
            catch (Exception ex) when (IsExpectedRecoveryFailure(ex)) { _log.Error("DocumentationRecoveryList", ex); }
        }

        var selected = WorkspaceRecoveryPolicy.SelectCandidates(bySession.Values.Select(item => item.Journal), WorkspaceRecoveryKind.Documentation, now);
        var result = new List<DocumentationRecoveryItem>(selected.Count);
        foreach (var candidate in selected)
        {
            if (!bySession.TryGetValue(candidate.Journal.SessionId, out var stored)) continue;
            var snapshotPath = Path.Combine(_paths.DocumentationRecoveryRoot, candidate.Journal.SnapshotFileName);
            if (File.Exists(snapshotPath)) result.Add(new DocumentationRecoveryItem(candidate.Journal, stored.JournalPath, snapshotPath));
        }
        return result;
    }

    private async Task<WorkspaceRecoveryJournal?> TryReadJournalAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > WorkspaceRecoveryPolicy.MaximumJournalBytes) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, useAsync: true);
        var bytes = await BoundedStreamReader.ReadExactAsync(stream, info.Length, WorkspaceRecoveryPolicy.MaximumJournalBytes, cancellationToken);
        return JsonSerializer.Deserialize<WorkspaceRecoveryJournal>(bytes, JsonOptions);
    }

    private void EnsureExpectedPaths(DocumentationRecoveryItem item)
    {
        var expectedJournal = Path.Combine(_paths.DocumentationRecoveryRoot, $"{item.Journal.SessionId:N}.json");
        var expectedSnapshot = Path.Combine(_paths.DocumentationRecoveryRoot, item.Journal.SnapshotFileName);
        if (!SamePath(item.JournalPath, expectedJournal) || !SamePath(item.SnapshotPath, expectedSnapshot))
            throw new InvalidDataException("Documentation recovery item paths are inconsistent with the journal.");
    }

    private void DeleteCore(Guid sessionId)
    {
        Directory.CreateDirectory(_paths.DocumentationRecoveryRoot);
        var stem = sessionId.ToString("N");
        TryDeleteFile(Path.Combine(_paths.DocumentationRecoveryRoot, stem + ".json"));
        foreach (var snapshotPath in Directory.EnumerateFiles(_paths.DocumentationRecoveryRoot, stem + "-*.magicdoc", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumJournalFilesToInspect))
            TryDeleteFile(snapshotPath);
    }

    private void RemoveJournalAndSafeSnapshot(string journalPath, WorkspaceRecoveryJournal? journal)
    {
        TryDeleteFile(journalPath);
        if (journal is not null && CanDeleteJournalSnapshot(journalPath, journal))
            TryDeleteFile(Path.Combine(_paths.DocumentationRecoveryRoot, journal.SnapshotFileName));
    }

    private bool CanDeleteJournalSnapshot(string journalPath, WorkspaceRecoveryJournal journal)
    {
        if (journal.Kind != WorkspaceRecoveryKind.Documentation) return false;
        if (!WorkspaceRecoveryPolicy.IsSafeSnapshotFileName(journal.Kind, journal.SnapshotFileName)) return false;
        if (!string.Equals(journal.SnapshotFileName, WorkspaceRecoveryPolicy.BuildSnapshotFileName(journal.Kind, journal.SessionId, journal.DirtyRevision), StringComparison.OrdinalIgnoreCase)) return false;
        return SamePath(journalPath, Path.Combine(_paths.DocumentationRecoveryRoot, $"{journal.SessionId:N}.json"));
    }

    private void PruneStaleTempFiles(DateTimeOffset now)
    {
        foreach (var tempPath in Directory.EnumerateFiles(_paths.DocumentationRecoveryRoot, "*.tmp-*", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumJournalFilesToInspect))
        {
            try
            {
                var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(tempPath), TimeSpan.Zero);
                if (now - lastWrite >= StaleTempAge) TryDeleteFile(tempPath);
            }
            catch (Exception ex) when (IsExpectedRecoveryFailure(ex)) { _log.Error("DocumentationRecoveryTempPrune", ex); }
        }
    }

    private void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (IsExpectedRecoveryFailure(ex)) { _log.Error("DocumentationRecoveryDelete", ex); }
    }

    private static string? NormalizeDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var name = Path.GetFileName(value.Trim());
        return name.Length <= WorkspaceRecoveryPolicy.MaximumDisplayNameLength ? name : name[..WorkspaceRecoveryPolicy.MaximumDisplayNameLength];
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedRecoveryFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException or System.Security.SecurityException;
}
