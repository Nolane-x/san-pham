using System.Diagnostics;
using Magic.Capture.Core.Workflows;

namespace Magic.Capture.App.Workflows;

internal sealed class WindowsTaskSchedulerService
{
    private const string TaskPrefix = "Magic Capture Desktop - Workflow - ";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

    public async Task CreateOrUpdateAsync(WorkflowTrigger trigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        var validation = WorkflowTriggerPolicy.Validate(trigger);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        if (trigger.Kind != WorkflowTriggerKind.Schedule || trigger.Schedule is null)
            throw new InvalidOperationException("Only schedule triggers can be registered with Windows Task Scheduler.");

        var days = ToTaskSchedulerDays(trigger.Schedule.Days);
        var taskCommand = $"magiccapture.exe --trigger {trigger.Id}";
        var arguments = new[]
        {
            "/Create", "/F", "/SC", "WEEKLY", "/D", days, "/ST", trigger.Schedule.TimeOfDay,
            "/TN", TaskName(trigger.Id), "/TR", taskCommand, "/RL", "LIMITED", "/IT"
        };
        await RunAsync(arguments, allowMissingTask: false, cancellationToken);
    }

    public Task DeleteAsync(string triggerId, CancellationToken cancellationToken = default)
    {
        if (!WorkflowTriggerPolicy.IsSafeIdentifier(triggerId)) throw new ArgumentException("Trigger id is invalid.", nameof(triggerId));
        return RunAsync(["/Delete", "/F", "/TN", TaskName(triggerId)], allowMissingTask: true, cancellationToken);
    }

    private static string TaskName(string triggerId) => TaskPrefix + triggerId;

    private static string ToTaskSchedulerDays(WorkflowTriggerDays days)
    {
        var values = new List<string>();
        if (days.HasFlag(WorkflowTriggerDays.Monday)) values.Add("MON");
        if (days.HasFlag(WorkflowTriggerDays.Tuesday)) values.Add("TUE");
        if (days.HasFlag(WorkflowTriggerDays.Wednesday)) values.Add("WED");
        if (days.HasFlag(WorkflowTriggerDays.Thursday)) values.Add("THU");
        if (days.HasFlag(WorkflowTriggerDays.Friday)) values.Add("FRI");
        if (days.HasFlag(WorkflowTriggerDays.Saturday)) values.Add("SAT");
        if (days.HasFlag(WorkflowTriggerDays.Sunday)) values.Add("SUN");
        if (values.Count == 0) throw new InvalidDataException("Schedule requires at least one day.");
        return string.Join(',', values);
    }

    private static async Task RunAsync(IReadOnlyList<string> arguments, bool allowMissingTask, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new InvalidOperationException("Windows Task Scheduler command could not be started.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("Windows Task Scheduler command timed out.");
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode == 0) return;
        if (allowMissingTask && (stdout.Contains("cannot find", StringComparison.OrdinalIgnoreCase) || stderr.Contains("cannot find", StringComparison.OrdinalIgnoreCase))) return;
        throw new InvalidOperationException($"Windows Task Scheduler rejected the request (exit {process.ExitCode}).");
    }
}
