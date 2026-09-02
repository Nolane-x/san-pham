using System.Runtime.InteropServices;
using Magic.Capture.Core.Platform;

namespace Magic.Capture.Core.Tests;

public sealed class FatalExceptionPolicyTests
{
    [Fact]
    public void IsFatal_RecognizesMemoryAndNativeCorruptionFamilies()
    {
        Assert.True(FatalExceptionPolicy.IsFatal(new OutOfMemoryException()));
        Assert.True(FatalExceptionPolicy.IsFatal(new AccessViolationException()));
        Assert.True(FatalExceptionPolicy.IsFatal(new SEHException()));
    }

    [Fact]
    public void IsFatal_DoesNotClassifyOrdinaryIoFailureAsFatal()
    {
        Assert.False(FatalExceptionPolicy.IsFatal(new IOException("disk busy")));
    }
}
