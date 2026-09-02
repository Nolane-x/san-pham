using Magic.Capture.Core.Ai;

namespace Magic.Capture.Core.Tests;

public sealed class ContextStackTests
{
    [Fact]
    public void Stack_preserves_order_and_has_hard_limit()
    {
        var stack = new ContextStack(2);
        var a = new ContextStackItem(Guid.NewGuid(), "A");
        var b = new ContextStackItem(Guid.NewGuid(), "B");
        var c = new ContextStackItem(Guid.NewGuid(), "C");
        Assert.True(stack.TryAdd(a));
        Assert.True(stack.TryAdd(b));
        Assert.False(stack.TryAdd(c));
        Assert.Equal([a, b], stack.Items);
        Assert.True(stack.Remove(a.CaptureId));
        Assert.Single(stack.Items);
    }
}
