using Magic.Capture.Core.Ai;

namespace Magic.Capture.Core.Tests;

public sealed class AiCacheMaintenancePolicyTests
{
    [Fact]
    public void Invalid_filename_key_or_oversize_entry_is_deleted()
    {
        var now = DateTimeOffset.Parse("2026-08-24T10:00:00Z");
        Assert.Equal(AiCacheMaintenanceDecision.DeleteInvalidName,
            AiCacheMaintenancePolicy.Decide("bad.json", new string('a', 64), now, now, 100, TimeSpan.FromDays(14)));
        Assert.Equal(AiCacheMaintenanceDecision.DeleteKeyMismatch,
            AiCacheMaintenancePolicy.Decide(new string('a', 64) + ".json", new string('b', 64), now, now, 100, TimeSpan.FromDays(14)));
        Assert.Equal(AiCacheMaintenanceDecision.DeleteOversize,
            AiCacheMaintenancePolicy.Decide(new string('a', 64) + ".json", new string('a', 64), now, now,
                AiCacheMaintenancePolicy.MaximumEntryJsonBytes + 1, TimeSpan.FromDays(14)));
    }

    [Fact]
    public void Expired_and_future_entries_are_deleted()
    {
        var now = DateTimeOffset.Parse("2026-08-24T10:00:00Z");
        var name = new string('a', 64) + ".json";
        var key = new string('a', 64);
        Assert.Equal(AiCacheMaintenanceDecision.DeleteExpired,
            AiCacheMaintenancePolicy.Decide(name, key, now.AddDays(-15), now, 100, TimeSpan.FromDays(14)));
        Assert.Equal(AiCacheMaintenanceDecision.DeleteFutureTimestamp,
            AiCacheMaintenancePolicy.Decide(name, key, now.AddMinutes(6), now, 100, TimeSpan.FromDays(14)));
        Assert.Equal(AiCacheMaintenanceDecision.Keep,
            AiCacheMaintenancePolicy.Decide(name, key, now.AddHours(-1), now, 100, TimeSpan.FromDays(14)));
    }
}
