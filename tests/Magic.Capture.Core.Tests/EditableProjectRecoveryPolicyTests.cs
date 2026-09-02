using Magic.Capture.Core.Projects;

namespace Magic.Capture.Core.Tests;

public sealed class EditableProjectRecoveryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 5, 0, 0, TimeSpan.Zero);

    private static EditableProjectRecoveryJournal Journal(Guid? sessionId = null, DateTimeOffset? updatedUtc = null, long revision = 1)
    {
        var id = sessionId ?? Guid.NewGuid();
        var updated = updatedUtc ?? Now.AddMinutes(-1);
        return new(
            EditableProjectRecoveryPolicy.CurrentJournalSchemaVersion,
            id,
            Guid.NewGuid(),
            updated.AddMinutes(-10),
            updated,
            EditableProjectRecoveryPolicy.BuildSnapshotFileName(id, revision),
            1920,
            1080,
            revision,
            "Example.magiccapture");
    }

    [Fact]
    public void AcceptsValidBoundedJournal()
    {
        var id = Guid.NewGuid();
        var result = EditableProjectRecoveryPolicy.Validate(Journal(id), Now);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("../escape.magiccapture")]
    [InlineData("folder\\escape.magiccapture")]
    [InlineData("not-a-guid.magiccapture")]
    [InlineData("0123456789abcdef0123456789abcdef.png")]
    public void RejectsUnsafeSnapshotNames(string snapshotFileName)
    {
        var id = Guid.NewGuid();
        Assert.False(EditableProjectRecoveryPolicy.Validate(Journal(id) with { SnapshotFileName = snapshotFileName }, Now).IsValid);
    }


    [Fact]
    public void RejectsSnapshotBelongingToAnotherSession()
    {
        var journal = Journal();
        var otherSession = Guid.NewGuid();
        journal = journal with { SnapshotFileName = EditableProjectRecoveryPolicy.BuildSnapshotFileName(otherSession, journal.DirtyRevision) };
        Assert.False(EditableProjectRecoveryPolicy.Validate(journal, Now).IsValid);
    }

    [Fact]
    public void SnapshotNameBindsSessionAndDirtyRevision()
    {
        var journal = Journal(revision: 7);
        var expected = $"{journal.SessionId:N}-00000000000000000007.magiccapture";
        Assert.Equal(expected, journal.SnapshotFileName);
        Assert.True(EditableProjectRecoveryPolicy.IsSafeSnapshotFileName(expected));
        Assert.False(EditableProjectRecoveryPolicy.Validate(
            journal with { SnapshotFileName = EditableProjectRecoveryPolicy.BuildSnapshotFileName(journal.SessionId, 8) },
            Now).IsValid);
        Assert.False(EditableProjectRecoveryPolicy.IsSafeSnapshotFileName($"{journal.SessionId:N}.magiccapture"));
    }

    [Fact]
    public void RejectsExpiredAndFarFutureSnapshots()
    {
        var expired = Journal(updatedUtc: Now - EditableProjectRecoveryPolicy.MaximumAge - TimeSpan.FromSeconds(1));
        var future = Journal(updatedUtc: Now + EditableProjectRecoveryPolicy.MaximumFutureClockSkew + TimeSpan.FromSeconds(1));
        Assert.False(EditableProjectRecoveryPolicy.Validate(expired, Now).IsValid);
        Assert.False(EditableProjectRecoveryPolicy.Validate(future, Now).IsValid);
    }

    [Fact]
    public void AcceptsExactExpiryAndFutureSkewBoundaries()
    {
        var expiryBoundary = Journal(updatedUtc: Now - EditableProjectRecoveryPolicy.MaximumAge);
        var skewBoundary = Journal(updatedUtc: Now + EditableProjectRecoveryPolicy.MaximumFutureClockSkew);
        Assert.True(EditableProjectRecoveryPolicy.Validate(expiryBoundary, Now).IsValid);
        Assert.True(EditableProjectRecoveryPolicy.Validate(skewBoundary, Now).IsValid);
    }

    [Fact]
    public void SelectsNewestUniqueSessionsAndCapsResult()
    {
        var journals = Enumerable.Range(0, 12)
            .Select(i =>
            {
                var id = Guid.NewGuid();
                return Journal(id, Now.AddMinutes(-i));
            })
            .ToList();
        journals.Add(journals[0] with
        {
            UpdatedUtc = Now.AddSeconds(-1),
            DirtyRevision = 2,
            SnapshotFileName = EditableProjectRecoveryPolicy.BuildSnapshotFileName(journals[0].SessionId, 2)
        });

        var selected = EditableProjectRecoveryPolicy.SelectCandidates(journals, Now);

        Assert.Equal(EditableProjectRecoveryPolicy.MaximumActiveSessions, selected.Count);
        Assert.Equal(journals[0].SessionId, selected[0].Journal.SessionId);
        Assert.Equal(2, selected[0].Journal.DirtyRevision);
        Assert.Equal(selected.Count, selected.Select(item => item.Journal.SessionId).Distinct().Count());
    }
}
