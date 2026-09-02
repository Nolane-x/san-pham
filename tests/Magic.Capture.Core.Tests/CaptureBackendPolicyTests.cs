using Magic.Capture.Core.Capture;

namespace Magic.Capture.Core.Tests;

public sealed class CaptureBackendPolicyTests
{
    private static readonly CaptureBackendAvailability AllAvailable = new(
        WindowsGraphicsCapture: true,
        DesktopDuplication: true,
        Gdi: true);

    [Fact]
    public void Window_UsesWgcThenGdi()
    {
        var candidates = CaptureBackendPolicy.BuildCandidates(
            CaptureTargetKind.Window,
            includeCursor: false,
            AllAvailable);

        Assert.Equal(
            [CaptureBackendKind.WindowsGraphicsCapture, CaptureBackendKind.Gdi],
            candidates);
    }

    [Fact]
    public void Monitor_UsesWgcThenDesktopDuplicationThenGdi()
    {
        var candidates = CaptureBackendPolicy.BuildCandidates(
            CaptureTargetKind.Monitor,
            includeCursor: false,
            AllAvailable);

        Assert.Equal(
            [CaptureBackendKind.WindowsGraphicsCapture, CaptureBackendKind.DesktopDuplication, CaptureBackendKind.Gdi],
            candidates);
    }

    [Fact]
    public void SingleMonitorRegion_WithCursor_SkipsDesktopDuplication()
    {
        var candidates = CaptureBackendPolicy.BuildCandidates(
            CaptureTargetKind.RegionSingleMonitor,
            includeCursor: true,
            AllAvailable);

        Assert.Equal(
            [CaptureBackendKind.WindowsGraphicsCapture, CaptureBackendKind.Gdi],
            candidates);
    }

    [Theory]
    [InlineData(CaptureTargetKind.RegionCrossMonitor)]
    [InlineData(CaptureTargetKind.VirtualDesktop)]
    public void CrossOutputTargets_AreGdiOnlyIn39(CaptureTargetKind target)
    {
        var candidates = CaptureBackendPolicy.BuildCandidates(target, includeCursor: false, AllAvailable);

        Assert.Equal([CaptureBackendKind.Gdi], candidates);
    }

    [Fact]
    public void UnavailableBackends_AreFilteredWithoutRemovingGdiFallback()
    {
        var availability = new CaptureBackendAvailability(
            WindowsGraphicsCapture: false,
            DesktopDuplication: true,
            Gdi: true);

        var candidates = CaptureBackendPolicy.BuildCandidates(
            CaptureTargetKind.Monitor,
            includeCursor: false,
            availability);

        Assert.Equal([CaptureBackendKind.DesktopDuplication, CaptureBackendKind.Gdi], candidates);
    }

    [Fact]
    public void PreferredAvailableBackend_MovesToFrontButPreservesFallbacks()
    {
        var candidates = CaptureBackendPolicy.BuildCandidates(
            CaptureTargetKind.Monitor,
            includeCursor: false,
            AllAvailable,
            CaptureBackendPreference.DesktopDuplication);

        Assert.Equal(
            [CaptureBackendKind.DesktopDuplication, CaptureBackendKind.WindowsGraphicsCapture, CaptureBackendKind.Gdi],
            candidates);
    }

    [Fact]
    public void InapplicablePreference_DoesNotBypassCorrectnessRules()
    {
        var candidates = CaptureBackendPolicy.BuildCandidates(
            CaptureTargetKind.Window,
            includeCursor: false,
            AllAvailable,
            CaptureBackendPreference.DesktopDuplication);

        Assert.Equal([CaptureBackendKind.WindowsGraphicsCapture, CaptureBackendKind.Gdi], candidates);
    }

    [Theory]
    [InlineData(CaptureBackendFailureKind.AccessLost, 0, true)]
    [InlineData(CaptureBackendFailureKind.DeviceRemoved, 0, true)]
    [InlineData(CaptureBackendFailureKind.DeviceReset, 0, true)]
    [InlineData(CaptureBackendFailureKind.AccessLost, 1, false)]
    [InlineData(CaptureBackendFailureKind.Timeout, 0, false)]
    [InlineData(CaptureBackendFailureKind.AccessDenied, 0, false)]
    [InlineData(CaptureBackendFailureKind.Unsupported, 0, false)]
    [InlineData(CaptureBackendFailureKind.Permanent, 0, false)]
    public void DesktopDuplicationRecovery_IsBounded(
        CaptureBackendFailureKind failure,
        int rebuildsUsed,
        bool expected)
    {
        Assert.Equal(expected, CaptureBackendRecoveryPolicy.ShouldRebuildDesktopDuplication(failure, rebuildsUsed));
    }

    [Theory]
    [InlineData(1L, true, true)]
    [InlineData(1L, false, false)]
    [InlineData(0L, true, false)]
    [InlineData(0L, false, false)]
    public void DesktopDuplicationCursorExclusion_RequiresVisibleSeparatePointerMetadata(
        long lastMouseUpdateTime,
        bool separatePointerVisible,
        bool expected)
    {
        Assert.Equal(expected, DesktopDuplicationCursorPolicy.CanGuaranteeCursorExcluded(
            lastMouseUpdateTime,
            separatePointerVisible));
    }

    [Fact]
    public void Cancellation_StopsFallback()
    {
        Assert.False(CaptureBackendRecoveryPolicy.ShouldFallback(CaptureBackendFailureKind.Cancelled));
        Assert.True(CaptureBackendRecoveryPolicy.ShouldFallback(CaptureBackendFailureKind.Timeout));
    }
}
