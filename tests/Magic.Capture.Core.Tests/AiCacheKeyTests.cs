using Magic.Capture.Core.Ai;

namespace Magic.Capture.Core.Tests;

public sealed class AiCacheKeyTests
{
    [Fact]
    public void Same_inputs_produce_same_key()
    {
        var a = AiCacheKey.Create("capture", ["context"], "action", 1, "profile", "model", "prompt=abc;image=xyz");
        var b = AiCacheKey.Create("capture", ["context"], "action", 1, "profile", "model", "prompt=abc;image=xyz");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Prompt_or_payload_strategy_change_changes_key()
    {
        var baseline = AiCacheKey.Create("capture", ["context"], "action", 1, "profile", "model", "prompt=abc;image=xyz");
        var promptChanged = AiCacheKey.Create("capture", ["context"], "action", 1, "profile", "model", "prompt=def;image=xyz");
        var imageChanged = AiCacheKey.Create("capture", ["context"], "action", 1, "profile", "model", "prompt=abc;image=uvw");
        Assert.NotEqual(baseline, promptChanged);
        Assert.NotEqual(baseline, imageChanged);
    }
}
