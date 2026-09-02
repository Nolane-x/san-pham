namespace Magic.Capture.Core.Platform;

public static class CredentialVaultErrorPolicy
{
    public const int ElementNotFoundHResult = unchecked((int)0x80070490);

    public static bool IsElementNotFound(Exception? exception) =>
        exception is not null && exception.HResult == ElementNotFoundHResult;
}
