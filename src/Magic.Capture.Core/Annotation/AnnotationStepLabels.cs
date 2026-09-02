namespace Magic.Capture.Core.Annotation;

public static class AnnotationStepLabels
{
    public static string Number(int index) => Math.Clamp(index, 1, 9999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string Alpha(int index)
    {
        index = Math.Clamp(index, 1, 18278);
        Span<char> chars = stackalloc char[4];
        var pos = chars.Length;
        while (index > 0)
        {
            index--;
            chars[--pos] = (char)('A' + index % 26);
            index /= 26;
        }
        return new string(chars[pos..]);
    }

    public static string Roman(int index)
    {
        index = Math.Clamp(index, 1, 3999);
        var values = new (int Value, string Symbol)[]
        {
            (1000,"M"),(900,"CM"),(500,"D"),(400,"CD"),(100,"C"),(90,"XC"),(50,"L"),(40,"XL"),
            (10,"X"),(9,"IX"),(5,"V"),(4,"IV"),(1,"I")
        };
        var builder = new System.Text.StringBuilder();
        foreach (var (value, symbol) in values)
            while (index >= value) { builder.Append(symbol); index -= value; }
        return builder.ToString();
    }
}
