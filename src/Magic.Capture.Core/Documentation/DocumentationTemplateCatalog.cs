namespace Magic.Capture.Core.Documentation;

public sealed record DocumentationTemplateProfile(
    string Id,
    string DisplayName,
    int CardWidth,
    int MinimumCardWidth,
    int OuterPadding,
    int ImagePadding,
    int HeaderHeight,
    int DescriptionHeight,
    int FooterHeight,
    int TitleFontPixels,
    int BodyFontPixels,
    int PageWidthTwips,
    int PageHeightTwips,
    int PageMarginTwips,
    string CssClass);

public static class DocumentationTemplateCatalog
{
    public const string DefaultId = "clean";

    private static readonly DocumentationTemplateProfile[] Profiles =
    [
        new(
            "clean", "Clean", 1440, 720, 36, 16, 92, 96, 34, 17, 15,
            12240, 15840, 720, "template-clean"),
        new(
            "compact", "Compact", 1180, 640, 24, 12, 74, 72, 28, 16, 14,
            12240, 15840, 540, "template-compact"),
        new(
            "presentation", "Presentation", 1600, 900, 54, 22, 108, 112, 40, 20, 17,
            15840, 12240, 720, "template-presentation"),
        new(
            "print", "Print", 1240, 720, 42, 16, 92, 94, 32, 17, 15,
            12240, 15840, 900, "template-print")
    ];

    public static IReadOnlyList<DocumentationTemplateProfile> All => Profiles;

    public static string NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return DefaultId;
        var candidate = id.Trim();
        foreach (var profile in Profiles)
            if (string.Equals(profile.Id, candidate, StringComparison.OrdinalIgnoreCase)) return profile.Id;
        return DefaultId;
    }

    public static DocumentationTemplateProfile Get(string? id)
    {
        var normalized = NormalizeId(id);
        foreach (var profile in Profiles)
            if (string.Equals(profile.Id, normalized, StringComparison.Ordinal)) return profile;
        return Profiles[0];
    }
}
