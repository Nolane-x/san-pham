using Magic.Capture.Core.Settings;

namespace Magic.Capture.Core.Tests;

public sealed class AppSettingsSchemaTests
{
    [Fact]
    public void Runtime_normalization_always_emits_current_schema()
    {
        var legacy = new AppSettings { PersistenceSchemaVersion = 0 };
        var normalized = AppSettingsRules.NormalizeForRuntime(legacy);
        Assert.Equal(AppSettingsRules.CurrentPersistenceSchemaVersion, normalized.PersistenceSchemaVersion);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void Schema_support_is_forward_fail_closed(int schema, bool supported)
    {
        Assert.Equal(supported, AppSettingsRules.IsPersistenceSchemaSupported(schema));
    }
}
