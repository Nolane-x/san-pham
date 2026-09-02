namespace Magic.Capture.Core.Storage;

public static class LocalPathGuard
{
    public static string ResolveWithinRoot(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Root path is required.", nameof(root));
        if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidDataException("Stored relative path is empty.");
        if (Path.IsPathRooted(relativePath)) throw new InvalidDataException("Stored path must be relative to the application data root.");

        var rootFull = Path.GetFullPath(root);
        var rootPrefix = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(rootFull, relativePath));
        if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Stored path escapes the application data root.");
        return candidate;
    }

    public static bool IsWithinRoot(string root, string relativePath)
    {
        try
        {
            _ = ResolveWithinRoot(root, relativePath);
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidDataException) { return false; }
        catch (NotSupportedException) { return false; }
    }
}
