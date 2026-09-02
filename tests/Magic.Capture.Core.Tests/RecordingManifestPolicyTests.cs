using Magic.Capture.Core.Recording;

namespace Magic.Capture.Core.Tests;

public sealed class RecordingManifestPolicyTests
{
    [Theory]
    [InlineData(RecordingSessionState.Preparing, true)]
    [InlineData(RecordingSessionState.Recording, true)]
    [InlineData(RecordingSessionState.Paused, true)]
    [InlineData(RecordingSessionState.Finalizing, true)]
    [InlineData(RecordingSessionState.Completed, false)]
    [InlineData(RecordingSessionState.Failed, false)]
    public void Unfinished_OnlyIncludesRecoverableLifecycleStates(RecordingSessionState state, bool expected)
    {
        Assert.Equal(expected, RecordingManifestPolicy.IsUnfinished(state));
    }

    [Fact]
    public void FutureSchema_IsReadOnly()
    {
        Assert.False(RecordingManifestPolicy.CanWriteSchema(RecordingManifestPolicy.CurrentSchemaVersion + 1));
        Assert.True(RecordingManifestPolicy.CanWriteSchema(RecordingManifestPolicy.CurrentSchemaVersion));
        Assert.True(RecordingManifestPolicy.CanReadSchema(RecordingManifestPolicy.CurrentSchemaVersion + 1));
    }

    [Fact]
    public void AudioOnlyJournalSchema_IsVersion5AndKeepsLegacyReadable()
    {
        Assert.Equal(5, RecordingManifestPolicy.CurrentSchemaVersion);
        Assert.True(RecordingManifestPolicy.CanReadSchema(1));
        Assert.True(RecordingManifestPolicy.CanWriteSchema(1));
        Assert.True(RecordingManifestPolicy.CanWriteSchema(2));
        Assert.True(RecordingManifestPolicy.CanWriteSchema(3));
        Assert.True(RecordingManifestPolicy.CanWriteSchema(4));
        Assert.True(RecordingManifestPolicy.CanWriteSchema(5));
    }
}
