namespace Magic.Capture.Core.Utilities;

public static class Base64ClipboardPolicy
{
    public const int MaximumOutputCharacters = 16 * 1024 * 1024;

    public static long ComputeBase64CharacterCount(long sourceBytes)
    {
        if (sourceBytes < 0) throw new ArgumentOutOfRangeException(nameof(sourceBytes));
        return checked(((sourceBytes + 2L) / 3L) * 4L);
    }

    public static void ValidateSourceLength(long sourceBytes, int prefixCharacters = 0)
    {
        if (sourceBytes < 0) throw new ArgumentOutOfRangeException(nameof(sourceBytes));
        if (prefixCharacters < 0) throw new ArgumentOutOfRangeException(nameof(prefixCharacters));
        var outputCharacters = checked(ComputeBase64CharacterCount(sourceBytes) + prefixCharacters);
        if (outputCharacters > MaximumOutputCharacters)
            throw new InvalidDataException($"Base64 clipboard output would exceed the safe {MaximumOutputCharacters / (1024 * 1024)} million-character limit. Copy the image file or path instead.");
    }
}
