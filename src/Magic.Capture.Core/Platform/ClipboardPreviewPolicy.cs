namespace Magic.Capture.Core.Platform;

public static class ClipboardPreviewPolicy
{
    public const int MaximumTextPreviewCharacters = 16_000;

    public static int BoundedCharacterCount(nuint byteLength, int maximumCharacters = MaximumTextPreviewCharacters)
    {
        if (maximumCharacters <= 0 || maximumCharacters > MaximumTextPreviewCharacters)
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters));

        var availableCharacters = byteLength / sizeof(char);
        if (availableCharacters == 0) return 0;
        return (int)Math.Min((nuint)maximumCharacters, availableCharacters);
    }
}
