using Magic.Capture.Core.Recovery;

namespace Magic.Capture.Core.Tests;

public sealed class WorkspaceRecoveryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    private static WorkspaceRecoveryJournal Journal(
        WorkspaceRecoveryKind kind = WorkspaceRecoveryKind.Documentation,
        Guid? sessionId = null,
        DateTimeOffset? updatedUtc = null,
        long revision = 1,
        string? displayName = "Guide")
    {
        var id = sessionId ?? Guid.NewGuid();
        var updated = updatedUtc ?? Now;
        return new WorkspaceRecoveryJournal(
            WorkspaceRecoveryPolicy.CurrentJournalSchemaVersion,
            kind,
            id,
            updated - TimeSpan.FromMinutes(2),
            updated,
            WorkspaceRecoveryPolicy.BuildSnapshotFileName(kind, id, revision),
            revision,
            displayName);
    }

    [Theory]
    [InlineData(WorkspaceRecoveryKind.Documentation, ".magicdoc")]
    [InlineData(WorkspaceRecoveryKind.VideoEdit, ".magicclip")]
    public void Snapshot_name_is_kind_scoped_and_safe(WorkspaceRecoveryKind kind, string extension)
    {
        var id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var name = WorkspaceRecoveryPolicy.BuildSnapshotFileName(kind, id, 42);

        Assert.EndsWith(extension, name, StringComparison.OrdinalIgnoreCase);
        Assert.True(WorkspaceRecoveryPolicy.IsSafeSnapshotFileName(kind, name));
        Assert.False(WorkspaceRecoveryPolicy.IsSafeSnapshotFileName(kind, "../" + name));
        Assert.False(WorkspaceRecoveryPolicy.IsSafeSnapshotFileName(
            kind == WorkspaceRecoveryKind.Documentation ? WorkspaceRecoveryKind.VideoEdit : WorkspaceRecoveryKind.Documentation,
            name));
    }

    [Fact]
    public void Validation_rejects_wrong_revision_kind_and_oversized_display_name()
    {
        var journal = Journal();
        Assert.True(WorkspaceRecoveryPolicy.Validate(journal, Now).IsValid);

        Assert.False(WorkspaceRecoveryPolicy.Validate(
            journal with { SnapshotFileName = WorkspaceRecoveryPolicy.BuildSnapshotFileName(journal.Kind, journal.SessionId, 2) }, Now).IsValid);
        Assert.False(WorkspaceRecoveryPolicy.Validate(
            journal with { SnapshotFileName = WorkspaceRecoveryPolicy.BuildSnapshotFileName(WorkspaceRecoveryKind.VideoEdit, journal.SessionId, journal.DirtyRevision) }, Now).IsValid);
        Assert.False(WorkspaceRecoveryPolicy.Validate(
            journal with { DisplayName = new string('x', WorkspaceRecoveryPolicy.MaximumDisplayNameLength + 1) }, Now).IsValid);
    }

    [Fact]
    public void Validation_enforces_age_and_future_clock_skew_boundaries()
    {
        Assert.True(WorkspaceRecoveryPolicy.Validate(Journal(updatedUtc: Now - WorkspaceRecoveryPolicy.MaximumAge), Now).IsValid);
        Assert.False(WorkspaceRecoveryPolicy.Validate(Journal(updatedUtc: Now - WorkspaceRecoveryPolicy.MaximumAge - TimeSpan.FromSeconds(1)), Now).IsValid);
        Assert.True(WorkspaceRecoveryPolicy.Validate(Journal(updatedUtc: Now + WorkspaceRecoveryPolicy.MaximumFutureClockSkew), Now).IsValid);
        Assert.False(WorkspaceRecoveryPolicy.Validate(Journal(updatedUtc: Now + WorkspaceRecoveryPolicy.MaximumFutureClockSkew + TimeSpan.FromSeconds(1)), Now).IsValid);
    }

    [Fact]
    public void Candidate_selection_is_kind_filtered_newest_first_deduplicated_and_bounded()
    {
        var duplicate = Guid.NewGuid();
        var journals = Enumerable.Range(0, WorkspaceRecoveryPolicy.MaximumActiveSessions + 4)
            .Select(i => Journal(
                WorkspaceRecoveryKind.Documentation,
                i == 0 ? duplicate : Guid.NewGuid(),
                Now - TimeSpan.FromMinutes(i),
                i + 1))
            .ToList();
        journals.Add(Journal(WorkspaceRecoveryKind.Documentation, duplicate, Now + TimeSpan.FromSeconds(10), 99));
        journals.Add(Journal(WorkspaceRecoveryKind.VideoEdit, updatedUtc: Now + TimeSpan.FromSeconds(20)));

        var selected = WorkspaceRecoveryPolicy.SelectCandidates(journals, WorkspaceRecoveryKind.Documentation, Now);

        Assert.Equal(WorkspaceRecoveryPolicy.MaximumActiveSessions, selected.Count);
        Assert.All(selected, candidate => Assert.Equal(WorkspaceRecoveryKind.Documentation, candidate.Journal.Kind));
        Assert.Equal(duplicate, selected[0].Journal.SessionId);
        Assert.Equal(99, selected[0].Journal.DirtyRevision);
        Assert.Equal(selected.Count, selected.Select(candidate => candidate.Journal.SessionId).Distinct().Count());
        Assert.True(selected.Zip(selected.Skip(1), (left, right) => left.Journal.UpdatedUtc >= right.Journal.UpdatedUtc).All(value => value));
    }
}
