using Magic.Capture.Core.Ai;

namespace Magic.Capture.Core.Tests;

public sealed class AiModelListPolicyTests
{
    [Fact]
    public void Accept_TrimsAndAcceptsReasonableModelId()
    {
        Assert.True(AiModelListPolicy.Accept("  qwen3:8b  ", out var normalized));
        Assert.Equal("qwen3:8b", normalized);
    }

    [Fact]
    public void Accept_RejectsOversizedAndControlCharacterIds()
    {
        Assert.False(AiModelListPolicy.Accept(new string('x', AiModelListPolicy.MaximumModelIdCharacters + 1), out _));
        Assert.False(AiModelListPolicy.Accept("model\nname", out _));
    }
}
