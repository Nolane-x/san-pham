using System.Text.Json;
using Magic.Capture.App.Imaging;
using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Recovery;
using Magic.Capture.Core.VideoEditing;

namespace Magic.Capture.App.VideoEditing;

internal sealed record VideoEditRecoveryItem(
    WorkspaceRecoveryJournal Journal,
    string JournalPath,
    string SnapshotPath);

internal sealed class VideoEditRecoveryStore
{
    private const int MaximumJournalFilesToInspect = 64;
    private static readonly TimeSpan StaleTempAge = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly VideoEditProjectStore _projects;
    private readonly LocalLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public VideoEditRecoveryStore(AppPaths paths, VideoEditProjectStore projects, LocalLog log)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task SaveAsync(
        Guid sessionId,
        VideoEditProject project,
        long dirtyRevision,
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Recovery session id is required.", nameof(sessionId));
        ArgumentNullException.ThrowIfNull(project);
        if (dirtyRevision <= 0) throw new ArgumentOutOfRangeException(nameof(dirtyRevision));
        if (!VideoEditProjectSchema.CanWrite(project.SchemaVersion))
            throw new InvalidOperationException("Future-schema clip projects are read-only and cannot be autosaved.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_paths.VideoEditRecoveryRoot);
            var kind = WorkspaceRecoveryKind.VideoEdit;
            var snapshotFileName = WorkspaceRecoveryPolicy.BuildSnapshotFileName(kind, sessionId, dirtyRevision);
            var snapshotPath = Path.Combine(_paths.VideoEditRecoveryRoot, snapshotFileName);
            var journalPath = Path.Combine(_paths.VideoEditRecoveryRoot, $"{sessionId:N}.json");
            var now = DateTimeOffset.UtcNow;
            var created = now;
            string? previousSnapshotFileName = null;

            var existing = await TryReadJournalAsync(journalPath, cancellationToken);
            if (existing is not null && existing.SessionId == sessionId && existing.Kind == kind && WorkspaceRecoveryPolicy.Validate(existing, now).IsValid)
            {
                if (dirtyRevision < existing.DirtyRevision)
                    throw new InvalidOperationException("A newer video-edit recovery revision is already stored for this session.");
                created = existing.CreatedUtc;
                previousSnapshotFileName = existing.SnapshotFileName;
            }

            await _projects.SaveAsync(project, snapshotPath, cancellationToken);

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
            if (!validation.IsValid) throw new InvalidDataException(validation.Error ?? "Video-edit recovery journal is invalid.");

            var bytes = JsonSerializer.SerializeToUtf8Bytes(journal, JsonOptions);
            if (bytes.LongLength <= 0 || bytes.LongLength > WorkspaceRecoveryPolicy.MaximumJournalBytes)
                throw new InvalidDataException("Video-edit recovery journal exceeds the safe size limit.");

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
                TryDeleteFile(Path.Combine(_paths.VideoEditRecoveryRoot, previousSnapshotFileName));

            await PruneCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<VideoEditRecoveryItem>> ListAsync(CancellationToken cancellationToken = default)
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

    public async Task<VideoEditProjectLoadResult> LoadAsync(VideoEditRecoveryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var validation = WorkspaceRecoveryPolicy.Validate(item.Journal, now);
            if (!validation.IsValid || item.Journal.Kind != WorkspaceRecoveryKind.VideoEdit)
                throw new InvalidDataException(validation.Error ?? "Video-edit recovery journal is invalid.");
            EnsureExpectedPaths(item);

            var currentJournal = await TryReadJournalAsync(item.JournalPath, cancellationToken);
            var currentValidation = WorkspaceRecoveryPolicy.Validate(currentJournal, now);
            if (currentJournal is null || currentJournal.Kind != WorkspaceRecoveryKind.VideoEdit || !currentValidation.IsValid || currentJournal != item.Journal)
                throw new InvalidDataException("Video-edit recovery journal changed after it was discovered.");

            var info = new FileInfo(item.SnapshotPath);
            if (!info.Exists || info.Length <= 0 || info.Length > VideoEditProjectStore.MaximumProjectBytes)
                throw new InvalidDataException("Video-edit recovery snapshot exceeds the safe project size limit.");
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
        Directory.CreateDirectory(_paths.VideoEditRecoveryRoot);
        var now = DateTimeOffset.UtcNow;
        PruneStaleTempFiles(now);
        var journals = new List<(WorkspaceRecoveryJournal Journal, string JournalPath)>();

        foreach (var journalPath in Directory.EnumerateFiles(_paths.VideoEditRecoveryRoot, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumJournalFilesToInspect))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var journal = await TryReadJournalAsync(journalPath, cancellationToken);
                var validation = WorkspaceRecoveryPolicy.Validate(journal, now);
                if (journal is null || journal.Kind != WorkspaceRecoveryKind.VideoEdit || !validation.IsValid)
                {
                    RemoveJournalAndSafeSnapshot(journalPath, journal);
                    continue;
                }

                var expectedJournal = Path.Combine(_paths.VideoEditRecoveryRoot, $"{journal.SessionId:N}.json");
                if (!SamePath(journalPath, expectedJournal))
                {
                    RemoveJournalAndSafeSnapshot(journalPath, journal);
                    continue;
                }

                var snapshotPath = Path.Combine(_paths.VideoEditRecoveryRoot, journal.SnapshotFileName);
                var info = new FileInfo(snapshotPath);
                if (!info.Exists || info.Length <= 0 || info.Length > VideoEditProjectStore.MaximumProjectBytes)
                {
                    RemoveJournalAndSafeSnapshot(journalPath, journal);
                    continue;
                }
                journals.Add((journal, journalPath));
            }
            catch (Exception ex) when (IsExpectedRecoveryFailure(ex))
            {
                _log.Error("VideoEditRecoveryPrune", ex);
                TryDeleteFile(journalPath);
            }
        }

        var selected = WorkspaceRecoveryPolicy.SelectCandidates(journals.Select(item => item.Journal), WorkspaceRecoveryKind.VideoEdit, now);
        var keep = selected.Select(item => item.Journal.SessionId).ToHashSet();
        foreach (var item in journals)
            if (!keep.Contains(item.Journal.SessionId)) RemoveJournalAndSafeSnapshot(item.JournalPath, item.Journal);

        var knownSnapshots = selected.Select(item => item.Journal.SnapshotFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshotPath in Directory.EnumerateFiles(_paths.VideoEditRecoveryRoot, "*.magicclip", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumJournalFilesToInspect))
            if (!knownSnapshots.Contains(Path.GetFileName(snapshotPath))) TryDeleteFile(snapshotPath);
    }

    private async Task<IReadOnlyList<VideoEditRecoveryItem>> ReadCandidatesCoreAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var bySession = new Dictionary<Guid, (WorkspaceRecoveryJournal Journal, string JournalPath)>();
        foreach (var journalPath in Directory.EnumerateFiles(_paths.VideoEditRecoveryRoot, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumJournalFilesToInspect))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var journal = await TryReadJournalAsync(journalPath, cancellationToken);
                if (journal is null || journal.Kind != WorkspaceRecoveryKind.VideoEdit || !WorkspaceRecoveryPolicy.Validate(journal, now).IsValid) continue;
                bySession[journal.SessionId] = (journal, journalPath);
            }
            catch (Exception ex) when (IsExpectedRecoveryFailure(ex)) { _log.Error("VideoEditRecoveryList", ex); }
        }

        var selected = WorkspaceRecoveryPolicy.SelectCandidates(bySession.Values.Select(item => item.Journal), WorkspaceRecoveryKind.VideoEdit, now);
        var result = new List<VideoEditRecoveryItem>(selected.Count);
        foreach (var candidate in selected)
        {
            if (!bySession.TryGetValue(candidate.Journal.SessionId, out var stored)) continue;
            var snapshotPath = Path.Combine(_paths.VideoEditRecoveryRoot, candidate.Journal.SnapshotFileName);
            if (File.Exists(snapshotPath)) result.Add(new VideoEditRecoveryItem(candidate.Journal, stored.JournalPath, snapshotPath));
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

    private void EnsureExpectedPaths(VideoEditRecoveryItem item)
    {
        var expectedJournal = Path.Combine(_paths.VideoEditRecoveryRoot, $"{item.Journal.SessionId:N}.json");
        var expectedSnapshot = Path.Combine(_paths.VideoEditRecoveryRoot, item.Journal.SnapshotFileName);
        if (!SamePath(item.JournalPath, expectedJournal) || !SamePath(item.SnapshotPath, expectedSnapshot))
            throw new InvalidDataException("Video-edit recovery item paths are inconsistent with the journal.");
    }

    private void DeleteCore(Guid sessionId)
    {
        Directory.CreateDirectory(_paths.VideoEditRecoveryRoot);
        var stem = sessionId.ToString("N");
        TryDeleteFile(Path.Combine(_paths.VideoEditRecoveryRoot, stem + ".json"));
        foreach (var snapshotPath in Directory.EnumerateFiles(_paths.VideoEditRecoveryRoot, stem + "-*.magicclip", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumJournalFilesToInspect))
            TryDeleteFile(snapshotPath);
    }

    private void RemoveJournalAndSafeSnapshot(string journalPath, WorkspaceRecoveryJournal? journal)
    {
        TryDeleteFile(journalPath);
        if (journal is not null && CanDeleteJournalSnapshot(journalPath, journal))
            TryDeleteFile(Path.Combine(_paths.VideoEditRecoveryRoot, journal.SnapshotFileName));
    }

    private bool CanDeleteJournalSnapshot(string journalPath, WorkspaceRecoveryJournal journal)
    {
        if (journal.Kind != WorkspaceRecoveryKind.VideoEdit) return false;
        if (!WorkspaceRecoveryPolicy.IsSafeSnapshotFileName(journal.Kind, journal.SnapshotFileName)) return false;
        if (!string.Equals(journal.SnapshotFileName, WorkspaceRecoveryPolicy.BuildSnapshotFileName(journal.Kind, journal.SessionId, journal.DirtyRevision), StringComparison.OrdinalIgnoreCase)) return false;
        return SamePath(journalPath, Path.Combine(_paths.VideoEditRecoveryRoot, $"{journal.SessionId:N}.json"));
    }

    private void PruneStaleTempFiles(DateTimeOffset now)
    {
        foreach (var tempPath in Directory.EnumerateFiles(_paths.VideoEditRecoveryRoot, "*.tmp-*", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumJournalFilesToInspect))
        {
            try
            {
                var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(tempPath), TimeSpan.Zero);
                if (now - lastWrite >= StaleTempAge) TryDeleteFile(tempPath);
            }
            catch (Exception ex) when (IsExpectedRecoveryFailure(ex)) { _log.Error("VideoEditRecoveryTempPrune", ex); }
        }
    }

    private void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (IsExpectedRecoveryFailure(ex)) { _log.Error("VideoEditRecoveryDelete", ex); }
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
