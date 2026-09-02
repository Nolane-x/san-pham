using System.Text;

namespace Magic.Capture.App.Persistence;

internal sealed class LocalLog
{
    private const long MaximumLogBytes = 8L * 1024 * 1024;
    private const int MaximumComponentCharacters = 96;
    private const int MaximumMessageCharacters = 400;

    private readonly AppPaths _paths;
    private readonly object _sync = new();

    public LocalLog(AppPaths paths) => _paths = paths;

    public void Error(string component, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            var safeComponent = Sanitize(component, MaximumComponentCharacters, "Unknown");
            var safeMessage = Sanitize(exception.Message, MaximumMessageCharacters, "(no message)");
            var line = $"{DateTimeOffset.UtcNow:O}\tERROR\t{safeComponent}\t{exception.GetType().Name}\t{safeMessage}{Environment.NewLine}";
            var path = Path.Combine(_paths.LogsRoot, DateTime.UtcNow.ToString("yyyy-MM-dd") + ".log");

            lock (_sync)
            {
                Directory.CreateDirectory(_paths.LogsRoot);
                RotateIfNeeded(path, Encoding.UTF8.GetByteCount(line));
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (IsExpectedLogFailure(ex))
        {
            // Local diagnostics are best-effort. Never turn an ordinary disk/permission problem
            // into a second application failure, but do not swallow fatal memory/native failures.
        }
    }

    private static void RotateIfNeeded(string path, int incomingBytes)
    {
        if (!File.Exists(path)) return;
        var existingBytes = new FileInfo(path).Length;
        if (existingBytes + incomingBytes <= MaximumLogBytes) return;

        var rotated = path + ".1";
        if (File.Exists(rotated)) File.Delete(rotated);
        File.Move(path, rotated, overwrite: false);
    }

    private static string Sanitize(string? value, int maximumCharacters, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value;
        normalized = normalized.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return normalized.Length <= maximumCharacters ? normalized : normalized[..maximumCharacters];
    }

    private static bool IsExpectedLogFailure(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException;
}
