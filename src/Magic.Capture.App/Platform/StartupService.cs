using Windows.ApplicationModel;
using Magic.Capture.Core.Platform;

namespace Magic.Capture.App.Platform;

internal sealed record StartupState(bool IsAvailable, bool IsEnabled, string Description);

internal sealed class StartupService
{
    public const string TaskId = "Magic.Capture.Desktop.Startup";

    public async Task<StartupState> GetStateAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            return task.State switch
            {
                StartupTaskState.Enabled => new StartupState(true, true, "Starts with Windows."),
                StartupTaskState.EnabledByPolicy => new StartupState(true, true, "Enabled by Windows policy."),
                StartupTaskState.DisabledByPolicy => new StartupState(true, false, "Disabled by Windows policy."),
                StartupTaskState.DisabledByUser => new StartupState(true, false, "Disabled by the user in Windows Startup settings."),
                _ => new StartupState(true, false, "Does not start with Windows.")
            };
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            return new StartupState(false, false, ex.Message);
        }
    }

    public async Task<StartupState> SetEnabledAsync(bool enabled)
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (enabled)
            {
                if (task.State == StartupTaskState.Disabled)
                    await task.RequestEnableAsync();
            }
            else if (task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
            {
                task.Disable();
            }
            return await GetStateAsync();
        }
        catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
        {
            return new StartupState(false, false, ex.Message);
        }
    }
}
