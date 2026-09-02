namespace Magic.Capture.Core.Ai;

public static class AiModelListPolicy
{
    public const int MaximumModels = 512;
    public const int MaximumModelIdCharacters = 256;

    public static bool Accept(string? candidate, out string normalized)
    {
        normalized = (candidate ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Length > MaximumModelIdCharacters)
        {
            normalized = string.Empty;
            return false;
        }

        if (normalized.Any(char.IsControl))
        {
            normalized = string.Empty;
            return false;
        }

        return true;
    }
}
