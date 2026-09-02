using System.Runtime.InteropServices;

namespace Magic.Capture.Core.Platform;

public static class FatalExceptionPolicy
{
    public static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or SEHException;
}
