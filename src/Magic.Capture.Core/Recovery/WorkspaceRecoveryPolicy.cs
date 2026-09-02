namespace Magic.Capture.Core.Recovery;

public enum WorkspaceRecoveryKind
{
    Documentation,
    VideoEdit
}

public sealed record WorkspaceRecoveryJournal(
    int SchemaVersion,
    WorkspaceRecoveryKind Kind,
    Guid SessionId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string SnapshotFileName,
    long DirtyRevision,
    string? DisplayName = null);

public sealed record WorkspaceRecoveryCandidate(WorkspaceRecoveryJournal Journal);

public sealed record WorkspaceRecoveryValidationResult(bool IsValid, string? Error = null);

public static class WorkspaceRecoveryPolicy
{
    public const int CurrentJournalSchemaVersion = 1;
    public const int MaximumActiveSessions = 8;
    public const long MaximumJournalBytes = 64L * 1024;
    public const int MaximumDisplayNameLength = 260;
    public static readonly TimeSpan MaximumAge = TimeSpan.FromDays(14);
    public static readonly TimeSpan MaximumFutureClockSkew = TimeSpan.FromMinutes(5);

    public static WorkspaceRecoveryValidationResult Validate(WorkspaceRecoveryJournal? journal, DateTimeOffset nowUtc)
    {
        if (journal is null) return new(false, "Recovery journal is required.");
        if (journal.SchemaVersion != CurrentJournalSchemaVersion) return new(false, "Recovery journal schema is unsupported.");
        if (!Enum.IsDefined(typeof(WorkspaceRecoveryKind), journal.Kind)) return new(false, "Recovery kind is unsupported.");
        if (journal.SessionId == Guid.Empty) return new(false, "Recovery session id is required.");
        if (journal.DirtyRevision <= 0) return new(false, "Recovery revision must be positive.");
        if (journal.CreatedUtc == default || journal.UpdatedUtc == default || journal.UpdatedUtc < journal.CreatedUtc)
            return new(false, "Recovery timestamps are invalid.");
        if (journal.UpdatedUtc > nowUtc + MaximumFutureClockSkew)
            return new(false, "Recovery timestamp is too far in the future.");
        if (nowUtc - journal.UpdatedUtc > MaximumAge)
            return new(false, "Recovery snapshot has expired.");
        if (!IsSafeSnapshotFileName(journal.Kind, journal.SnapshotFileName))
            return new(false, "Recovery snapshot file name is unsafe.");
        if (!string.Equals(
                journal.SnapshotFileName,
                BuildSnapshotFileName(journal.Kind, journal.SessionId, journal.DirtyRevision),
                StringComparison.OrdinalIgnoreCase))
            return new(false, "Recovery snapshot does not belong to this session revision.");
        if (journal.DisplayName is { Length: > MaximumDisplayNameLength })
            return new(false, "Recovery display name is too long.");
        return new(true);
    }

    public static IReadOnlyList<WorkspaceRecoveryCandidate> SelectCandidates(
        IEnumerable<WorkspaceRecoveryJournal?> journals,
        WorkspaceRecoveryKind kind,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(journals);
        if (!Enum.IsDefined(typeof(WorkspaceRecoveryKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));

        return journals
            .Select(journal => new { Journal = journal, Validation = Validate(journal, nowUtc) })
            .Where(item => item.Journal is not null && item.Journal.Kind == kind && item.Validation.IsValid)
            .GroupBy(item => item.Journal!.SessionId)
            .Select(group => new WorkspaceRecoveryCandidate(group
                .OrderByDescending(item => item.Journal!.UpdatedUtc)
                .ThenByDescending(item => item.Journal!.DirtyRevision)
                .First().Journal!))
            .OrderByDescending(candidate => candidate.Journal.UpdatedUtc)
            .ThenByDescending(candidate => candidate.Journal.DirtyRevision)
            .Take(MaximumActiveSessions)
            .ToArray();
    }

    public static string BuildSnapshotFileName(WorkspaceRecoveryKind kind, Guid sessionId, long dirtyRevision)
    {
        if (!Enum.IsDefined(typeof(WorkspaceRecoveryKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (sessionId == Guid.Empty) throw new ArgumentException("Recovery session id is required.", nameof(sessionId));
        if (dirtyRevision <= 0) throw new ArgumentOutOfRangeException(nameof(dirtyRevision));
        return $"{sessionId:N}-{dirtyRevision:D20}{ExtensionFor(kind)}";
    }

    public static bool IsSafeSnapshotFileName(WorkspaceRecoveryKind kind, string? snapshotFileName)
    {
        if (!Enum.IsDefined(typeof(WorkspaceRecoveryKind), kind)) return false;
        if (string.IsNullOrWhiteSpace(snapshotFileName)) return false;
        if (snapshotFileName.Contains('/') || snapshotFileName.Contains('\\')) return false;
        if (!string.Equals(Path.GetFileName(snapshotFileName), snapshotFileName, StringComparison.Ordinal)) return false;
        if (!snapshotFileName.EndsWith(ExtensionFor(kind), StringComparison.OrdinalIgnoreCase)) return false;

        var extension = ExtensionFor(kind);
        var stem = snapshotFileName[..^extension.Length];
        if (stem.Length != 53 || stem[32] != '-') return false;
        if (!Guid.TryParseExact(stem[..32], "N", out _)) return false;
        return long.TryParse(
                   stem.AsSpan(33),
                   System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var revision)
               && revision > 0;
    }

    public static string ExtensionFor(WorkspaceRecoveryKind kind) => kind switch
    {
        WorkspaceRecoveryKind.Documentation => ".magicdoc",
        WorkspaceRecoveryKind.VideoEdit => ".magicclip",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
