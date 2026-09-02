using Magic.Capture.App.Capture;
using Magic.Capture.App.Commerce;
using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Commerce;
using Magic.Capture.Core.Platform;
using Magic.Capture.Core.Settings;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.App.Workflows;

internal sealed class WorkflowTriggerRunner
{
    private sealed class TriggerRuntimeState
    {
        public DateTimeOffset? LastCompletedUtc { get; set; }
        public DateTimeOffset? SuspendedUntilUtc { get; set; }
        public Queue<DateTimeOffset> RecentRuns { get; } = new();
    }

    private readonly WorkflowTriggerStore _triggers;
    private readonly WorkflowTriggerHistoryStore _history;
    private readonly WorkflowStore _workflows;
    private readonly EntitlementService _entitlements;
    private readonly Func<AppSettings> _settings;
    private readonly Func<CaptureProfile, CaptureWorkflow, CancellationToken, Task> _runCaptureProfileForAutomationAsync;
    private readonly LocalLog _log;
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly Dictionary<string, TriggerRuntimeState> _runtime = new(StringComparer.Ordinal);

    public WorkflowTriggerRunner(
        WorkflowTriggerStore triggers,
        WorkflowTriggerHistoryStore history,
        WorkflowStore workflows,
        EntitlementService entitlements,
        Func<AppSettings> settings,
        Func<CaptureProfile, CaptureWorkflow, CancellationToken, Task> runCaptureProfileForAutomationAsync,
        LocalLog log)
    {
        _triggers = triggers;
        _history = history;
        _workflows = workflows;
        _entitlements = entitlements;
        _settings = settings;
        _runCaptureProfileForAutomationAsync = runCaptureProfileForAutomationAsync;
        _log = log;
    }

    public async Task RunAsync(
        string triggerId,
        WorkflowTriggerKind? expectedKind = null,
        string reasonCode = "event",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerId);
        await _executionGate.WaitAsync(cancellationToken);
        try
        {
            var trigger = (await _triggers.LoadAsync(cancellationToken))
                .FirstOrDefault(item => string.Equals(item.Id, triggerId, StringComparison.Ordinal));
            if (trigger is null) return;

            var startedUtc = DateTimeOffset.UtcNow;
            if (!trigger.Enabled) { await RecordBestEffortAsync(trigger, WorkflowTriggerRunStatus.Suppressed, "disabled", startedUtc, cancellationToken); return; }
            if (expectedKind is { } kind && trigger.Kind != kind) { await RecordBestEffortAsync(trigger, WorkflowTriggerRunStatus.Suppressed, "trigger_kind_mismatch", startedUtc, cancellationToken); return; }
            if (!_entitlements.CanUse(ProductFeature.AdvancedWorkflows)) { await RecordBestEffortAsync(trigger, WorkflowTriggerRunStatus.Suppressed, "feature_not_entitled", startedUtc, cancellationToken); return; }

            var state = GetState(trigger.Id);
            if (state.SuspendedUntilUtc is { } suspendedUntil && startedUtc < suspendedUntil)
            {
                await RecordBestEffortAsync(trigger, WorkflowTriggerRunStatus.Suppressed, "circuit_breaker", startedUtc, cancellationToken);
                return;
            }
            if (state.LastCompletedUtc is { } lastCompleted && startedUtc - lastCompleted < TimeSpan.FromSeconds(trigger.CooldownSeconds))
            {
                await RecordBestEffortAsync(trigger, WorkflowTriggerRunStatus.Suppressed, "cooldown", startedUtc, cancellationToken);
                return;
            }

            while (state.RecentRuns.Count > 0 && startedUtc - state.RecentRuns.Peek() > WorkflowTriggerPolicy.CircuitBreakerWindow)
                state.RecentRuns.Dequeue();
            if (state.RecentRuns.Count >= WorkflowTriggerPolicy.CircuitBreakerMaximumRuns)
            {
                state.SuspendedUntilUtc = startedUtc + WorkflowTriggerPolicy.CircuitBreakerSuspension;
                await RecordBestEffortAsync(trigger, WorkflowTriggerRunStatus.Suppressed, "circuit_breaker", startedUtc, cancellationToken);
                return;
            }

            // Count every accepted attempt, including preflight failures, so broken/deleted
            // workflow/profile references cannot create an unbounded metadata/error storm.
            state.RecentRuns.Enqueue(startedUtc);

            var workflow = (await _workflows.LoadAsync(cancellationToken))
                .FirstOrDefault(item => string.Equals(item.Id, trigger.WorkflowId, StringComparison.Ordinal));
            if (workflow is null)
            {
                state.LastCompletedUtc = DateTimeOffset.UtcNow;
                await RecordBestEffortAsync(trigger, WorkflowTriggerRunStatus.Failed, "workflow_missing", startedUtc, cancellationToken);
                return;
            }
            if (_entitlements.Current.Tier < workflow.RequiredTier)
            {
                state.LastCompletedUtc = DateTimeOffset.UtcNow;
                await RecordBestEffortAsync(trigger, WorkflowTriggerRunStatus.Suppressed, "workflow_tier", startedUtc, cancellationToken);
                return;
            }

            var profile = _settings().CaptureProfiles.FirstOrDefault(item => string.Equals(item.Id, trigger.CaptureProfileId, StringComparison.Ordinal));
            if (profile is null)
            {
                state.LastCompletedUtc = DateTimeOffset.UtcNow;
                await RecordBestEffortAsync(trigger, WorkflowTriggerRunStatus.Failed, "profile_missing", startedUtc, cancellationToken);
                return;
            }
            if (!WorkflowTriggerPolicy.IsCaptureProfileUnattendedSafe(profile))
            {
                state.LastCompletedUtc = DateTimeOffset.UtcNow;
                await RecordBestEffortAsync(trigger, WorkflowTriggerRunStatus.Suppressed, "profile_interactive", startedUtc, cancellationToken);
                return;
            }

            try
            {
                await _runCaptureProfileForAutomationAsync(profile with { WorkflowId = workflow.Id }, workflow, cancellationToken);
                state.LastCompletedUtc = DateTimeOffset.UtcNow;
                await RecordBestEffortAsync(trigger, WorkflowTriggerRunStatus.Succeeded, NormalizeEventReason(reasonCode), startedUtc, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                state.LastCompletedUtc = DateTimeOffset.UtcNow;
                throw;
            }
            catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
            {
                state.LastCompletedUtc = DateTimeOffset.UtcNow;
                _log.Error("WorkflowTriggerRun", ex);
                await RecordBestEffortAsync(trigger, WorkflowTriggerRunStatus.Failed, "execution_failed", startedUtc, cancellationToken);
            }
        }
        finally { _executionGate.Release(); }
    }

    private TriggerRuntimeState GetState(string triggerId)
    {
        if (_runtime.TryGetValue(triggerId, out var state)) return state;
        state = new TriggerRuntimeState();
        _runtime[triggerId] = state;
        return state;
    }

    private async Task RecordBestEffortAsync(
        WorkflowTrigger trigger,
        WorkflowTriggerRunStatus status,
        string reasonCode,
        DateTimeOffset startedUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await _history.AppendAsync(trigger, status, reasonCode, startedUtc, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            _log.Error("WorkflowTriggerHistory", ex);
        }
    }

    private static string NormalizeEventReason(string reasonCode) => reasonCode switch
    {
        "schedule" or "file_change" or "clipboard_change" or "foreground_window" or "process_start" or "hotkey" or "test" => reasonCode,
        _ => "event"
    };
}
