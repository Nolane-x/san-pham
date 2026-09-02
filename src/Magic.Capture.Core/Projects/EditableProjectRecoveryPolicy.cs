namespace Magic.Capture.Core.Projects;

public sealed record EditableProjectRecoveryJournal(
    int SchemaVersion,
    Guid SessionId,
    Guid ProjectId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string SnapshotFileName,
    int Width,
    int Height,
    long DirtyRevision,
    string? OriginalProjectDisplayName = null);

public sealed record EditableProjectRecoveryCandidate(EditableProjectRecoveryJournal Journal);

public sealed record EditableProjectRecoveryValidationResult(bool IsValid, string? Error = null);

public static class EditableProjectRecoveryPolicy
{
    public const int CurrentJournalSchemaVersion = 1;
    public const int MaximumActiveSessions = 8;
    public const long MaximumJournalBytes = 64L * 1024;
    public static readonly TimeSpan MaximumAge = TimeSpan.FromDays(14);
    public static readonly TimeSpan MaximumFutureClockSkew = TimeSpan.FromMinutes(5);
    public const int MaximumDisplayNameLength = 260;

    public static EditableProjectRecoveryValidationResult Validate(EditableProjectRecoveryJournal? journal, DateTimeOffset nowUtc)
    {
        if (journal is null) return new(false, "Recovery journal is required.");
        if (journal.SchemaVersion != CurrentJournalSchemaVersion) return new(false, "Recovery journal schema is unsupported.");
        if (journal.SessionId == Guid.Empty) return new(false, "Recovery session id is required.");
        if (journal.ProjectId == Guid.Empty) return new(false, "Recovery project id is required.");
        if (journal.DirtyRevision <= 0) return new(false, "Recovery revision must be positive.");
        if (journal.Width <= 0 || journal.Height <= 0 || journal.Width > 200_000 || journal.Height > 200_000)
            return new(false, "Recovery dimensions are invalid.");
        if ((long)journal.Width * journal.Height > EditableProjectValidator.MaxPixelCount)
            return new(false, "Recovery pixel area exceeds the supported limit.");
        if (journal.CreatedUtc == default || journal.UpdatedUtc == default || journal.UpdatedUtc < journal.CreatedUtc)
            return new(false, "Recovery timestamps are invalid.");
        if (journal.UpdatedUtc > nowUtc + MaximumFutureClockSkew)
            return new(false, "Recovery timestamp is too far in the future.");
        if (nowUtc - journal.UpdatedUtc > MaximumAge)
            return new(false, "Recovery snapshot has expired.");
        if (!IsSafeSnapshotFileName(journal.SnapshotFileName))
            return new(false, "Recovery snapshot file name is unsafe.");
        if (!string.Equals(
                journal.SnapshotFileName,
                BuildSnapshotFileName(journal.SessionId, journal.DirtyRevision),
                StringComparison.OrdinalIgnoreCase))
            return new(false, "Recovery snapshot does not belong to this session revision.");
        if (journal.OriginalProjectDisplayName is { Length: > MaximumDisplayNameLength })
            return new(false, "Recovery project display name is too long.");
        return new(true);
    }

    public static IReadOnlyList<EditableProjectRecoveryCandidate> SelectCandidates(
        IEnumerable<EditableProjectRecoveryJournal?> journals,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(journals);
        return journals
            .Select(journal => new { Journal = journal, Validation = Validate(journal, nowUtc) })
            .Where(item => item.Journal is not null && item.Validation.IsValid)
            .GroupBy(item => item.Journal!.SessionId)
            .Select(group => new EditableProjectRecoveryCandidate(group
                .OrderByDescending(item => item.Journal!.DirtyRevision)
                .ThenByDescending(item => item.Journal!.UpdatedUtc)
                .First().Journal!))
            .OrderByDescending(candidate => candidate.Journal.UpdatedUtc)
            .Take(MaximumActiveSessions)
            .ToArray();
    }

    public static string BuildSnapshotFileName(Guid sessionId, long dirtyRevision)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Recovery session id is required.", nameof(sessionId));
        if (dirtyRevision <= 0) throw new ArgumentOutOfRangeException(nameof(dirtyRevision));
        return $"{sessionId:N}-{dirtyRevision:D20}.magiccapture";
    }

    public static bool IsSafeSnapshotFileName(string? snapshotFileName)
    {
        if (string.IsNullOrWhiteSpace(snapshotFileName)) return false;
        if (snapshotFileName.Contains('/') || snapshotFileName.Contains('\\')) return false;
        if (!string.Equals(Path.GetFileName(snapshotFileName), snapshotFileName, StringComparison.Ordinal)) return false;
        if (!snapshotFileName.EndsWith(".magiccapture", StringComparison.OrdinalIgnoreCase)) return false;

        var stem = Path.GetFileNameWithoutExtension(snapshotFileName);
        if (stem.Length != 53 || stem[32] != '-') return false;
        if (!Guid.TryParseExact(stem[..32], "N", out _)) return false;
        return long.TryParse(
                   stem.AsSpan(33),
                   System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var revision)
               && revision > 0;
    }
}
