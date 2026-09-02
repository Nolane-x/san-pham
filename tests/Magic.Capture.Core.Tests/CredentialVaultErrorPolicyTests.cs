using System.Runtime.InteropServices;
using Magic.Capture.Core.Platform;

namespace Magic.Capture.Core.Tests;

public sealed class CredentialVaultErrorPolicyTests
{
    [Fact]
    public void IsElementNotFound_RecognizesWin32ElementNotFoundHResult()
    {
        var exception = new COMException("Element not found", CredentialVaultErrorPolicy.ElementNotFoundHResult);
        Assert.True(CredentialVaultErrorPolicy.IsElementNotFound(exception));
    }

    [Fact]
    public void IsElementNotFound_DoesNotHideOtherVaultFailures()
    {
        var exception = new COMException("Access denied", unchecked((int)0x80070005));
        Assert.False(CredentialVaultErrorPolicy.IsElementNotFound(exception));
    }
}
