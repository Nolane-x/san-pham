using Magic.Capture.Core.Storage;

namespace Magic.Capture.Core.Tests;

public sealed class LocalConfigurationLimitsTests
{
    [Fact]
    public void ValidateCount_AcceptsLimitAndRejectsOverflow()
    {
        LocalConfigurationLimits.ValidateCount(LocalConfigurationLimits.MaximumDestinations,
            LocalConfigurationLimits.MaximumDestinations, "Destinations");
        Assert.Throws<InvalidDataException>(() => LocalConfigurationLimits.ValidateCount(
            LocalConfigurationLimits.MaximumDestinations + 1,
            LocalConfigurationLimits.MaximumDestinations,
            "Destinations"));
    }

    [Fact]
    public void ConfigurationBudgetsRemainSmallAndBounded()
    {
        Assert.InRange(LocalConfigurationLimits.MaximumWorkflowJsonBytes, 1, 4L * 1024 * 1024);
        Assert.InRange(LocalConfigurationLimits.MaximumMagicActionJsonBytes, 1, 8L * 1024 * 1024);
        Assert.InRange(LocalConfigurationLimits.MaximumAiProviderProfiles, 1, 64);
    }
}
