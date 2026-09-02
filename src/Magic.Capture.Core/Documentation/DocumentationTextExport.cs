using System.Net;
using System.Text;

namespace Magic.Capture.Core.Documentation;

public sealed record DocumentationContentsEntry(int StepNumber, string Title, string? Section, string Anchor);

public static class DocumentationTextExport
{
    public static IReadOnlyList<DocumentationContentsEntry> BuildContents(DocumentationProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project = DocumentationPolicy.Normalize(project);
        var contents = new List<DocumentationContentsEntry>(project.Steps.Count);
        string? currentSection = null;
        for (var i = 0; i < project.Steps.Count; i++)
        {
            var step = project.Steps[i];
            string? section = null;
            if (!string.IsNullOrWhiteSpace(step.Section) && !string.Equals(currentSection, step.Section, StringComparison.Ordinal))
            {
                currentSection = step.Section;
                section = currentSection;
            }
            var title = string.IsNullOrWhiteSpace(step.Title) ? $"Step {i + 1}" : step.Title!;
            contents.Add(new DocumentationContentsEntry(i + 1, title, section, BuildStepAnchor(i + 1)));
        }
        return contents;
    }

    public static string BuildStepAnchor(int stepNumber)
    {
        if (stepNumber <= 0) throw new ArgumentOutOfRangeException(nameof(stepNumber));
        return $"step-{stepNumber:D3}";
    }

    public static string BuildHtml(DocumentationProject project, Func<string, string> imageHref, string? logoHref = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(imageHref);
        project = DocumentationPolicy.Normalize(project);
        var profile = DocumentationTemplateCatalog.Get(project.Template);
        var contents = BuildContents(project);
        var html = new StringBuilder(Math.Max(6144, project.Steps.Count * 1200));
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.Append("<title>").Append(HtmlEncode(project.Title)).Append("</title>");
        html.Append("<style>")
            .Append("*{box-sizing:border-box}body{font:16px/1.55 system-ui,-apple-system,Segoe UI,sans-serif;margin:0;color:#1f2328;background:#f6f8fa}")
            .Append(".document{max-width:1040px;margin:0 auto;padding:28px 22px 44px;background:white;min-height:100vh}")
            .Append(".template-compact .document{max-width:900px;padding:20px 18px}.template-presentation .document{max-width:1220px;padding:42px 34px}.template-print{background:white}.template-print .document{max-width:900px}")
            .Append(".running-header{font-size:.88rem;color:#656d76;border-bottom:1px solid #d8dee4;padding-bottom:10px;margin-bottom:22px}")
            .Append(".hero{display:grid;grid-template-columns:auto 1fr;gap:18px;align-items:center;margin-bottom:24px}.hero.no-logo{display:block}.logo{max-width:180px;max-height:86px;object-fit:contain}")
            .Append("h1{font-size:2rem;line-height:1.2;margin:0 0 8px}.subtitle{margin:0;color:#57606a}")
            .Append(".toc{border:1px solid #d0d7de;border-radius:12px;padding:16px 20px;margin:22px 0 30px;background:#f6f8fa}.toc h2{margin:0 0 10px}.toc ul{margin:0;padding-left:20px}.toc-section{font-weight:700;margin-top:8px;list-style:none}.toc a{color:#0969da;text-decoration:none}")
            .Append(".section{margin-top:34px}.step{border:1px solid #d0d7de;border-radius:12px;padding:18px;margin:20px 0;break-inside:avoid}.template-compact .step{padding:12px;margin:12px 0}.template-presentation .step{padding:26px;margin:30px 0}")
            .Append(".step img{max-width:100%;height:auto;border-radius:8px;border:1px solid #d8dee4}.meta{color:#656d76;font-size:.9em}.number{display:inline-grid;place-items:center;min-width:32px;height:32px;border-radius:999px;background:#24292f;color:white;font-weight:700;margin-right:8px}.step h2{display:flex;align-items:center}")
            .Append(".document-footer{margin:36px 0 0;padding-top:14px;border-top:1px solid #d8dee4;color:#656d76}@media print{body{background:white}.document{max-width:none;padding:0}.step{break-inside:avoid}.toc{background:white}}")
            .Append("</style></head><body class=\"").Append(profile.CssClass).Append("\"><main class=\"document\">");

        if (!string.IsNullOrWhiteSpace(project.Header))
            html.Append("<div class=\"running-header\">").Append(HtmlEncode(project.Header)).Append("</div>");

        var hasLogo = !string.IsNullOrWhiteSpace(logoHref);
        html.Append("<header class=\"hero").Append(hasLogo ? string.Empty : " no-logo").Append("\">");
        if (hasLogo)
            html.Append("<img class=\"logo\" alt=\"Logo\" src=\"").Append(HtmlAttributeEncode(logoHref!)).Append("\">");
        html.Append("<div><h1>").Append(HtmlEncode(project.Title)).Append("</h1>");
        if (!string.IsNullOrWhiteSpace(project.Subtitle))
            html.Append("<p class=\"subtitle\">").Append(HtmlEncode(project.Subtitle)).Append("</p>");
        html.Append("</div></header>");

        AppendHtmlContents(html, contents);

        string? currentSection = null;
        for (var index = 0; index < project.Steps.Count; index++)
        {
            var step = project.Steps[index];
            if (!string.Equals(currentSection, step.Section, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(step.Section))
            {
                currentSection = step.Section;
                html.Append("<h2 class=\"section\">").Append(HtmlEncode(currentSection)).Append("</h2>");
            }
            html.Append("<article class=\"step\" id=\"").Append(BuildStepAnchor(index + 1)).Append("\"><h2><span class=\"number\">").Append(index + 1).Append("</span>");
            html.Append(HtmlEncode(string.IsNullOrWhiteSpace(step.Title) ? $"Step {index + 1}" : step.Title!)).Append("</h2>");
            if (!string.IsNullOrWhiteSpace(step.Description))
                html.Append("<p>").Append(HtmlEncode(step.Description).Replace("\n", "<br>", StringComparison.Ordinal)).Append("</p>");
            var href = imageHref(step.ImageKey) ?? string.Empty;
            html.Append("<img loading=\"lazy\" alt=\"Step ").Append(index + 1).Append("\" src=\"").Append(HtmlAttributeEncode(href)).Append("\">");
            var source = BuildSourceLabel(step);
            if (source is not null) html.Append("<p class=\"meta\">").Append(HtmlEncode(source)).Append("</p>");
            html.Append("</article>");
        }
        if (!string.IsNullOrWhiteSpace(project.Footer))
            html.Append("<footer class=\"document-footer\">").Append(HtmlEncode(project.Footer)).Append("</footer>");
        html.Append("</main></body></html>");
        return html.ToString();
    }

    public static string BuildMarkdown(DocumentationProject project, Func<string, string> imageHref, string? logoHref = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(imageHref);
        project = DocumentationPolicy.Normalize(project);
        var contents = BuildContents(project);
        var markdown = new StringBuilder(Math.Max(3072, project.Steps.Count * 640));
        if (!string.IsNullOrWhiteSpace(logoHref)) markdown.Append("![Logo](").Append(EscapeMarkdownLink(logoHref)).Append(")\n\n");
        if (!string.IsNullOrWhiteSpace(project.Header)) markdown.Append("> ").Append(MarkdownEscape(project.Header)).Append("\n\n");
        markdown.Append("# ").Append(MarkdownEscape(project.Title)).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(project.Subtitle)) markdown.Append(MarkdownEscape(project.Subtitle)).Append("\n\n");
        AppendMarkdownContents(markdown, contents);

        string? currentSection = null;
        for (var index = 0; index < project.Steps.Count; index++)
        {
            var step = project.Steps[index];
            if (!string.Equals(currentSection, step.Section, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(step.Section))
            {
                currentSection = step.Section;
                markdown.Append("## ").Append(MarkdownEscape(currentSection)).Append("\n\n");
            }
            markdown.Append("<a id=\"").Append(BuildStepAnchor(index + 1)).Append("\"></a>\n\n");
            markdown.Append("### Step ").Append(index + 1).Append(": ")
                .Append(MarkdownEscape(string.IsNullOrWhiteSpace(step.Title) ? $"Step {index + 1}" : step.Title!)).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(step.Description)) markdown.Append(MarkdownEscape(step.Description)).Append("\n\n");
            markdown.Append("![Step ").Append(index + 1).Append("](").Append(EscapeMarkdownLink(imageHref(step.ImageKey))).Append(")\n\n");
            var source = BuildSourceLabel(step);
            if (source is not null) markdown.Append("_Source: ").Append(MarkdownEscape(source)).Append("_\n\n");
        }
        if (!string.IsNullOrWhiteSpace(project.Footer)) markdown.Append("---\n\n").Append(MarkdownEscape(project.Footer)).Append("\n");
        return markdown.ToString();
    }

    public static string BuildSelfContainedHtml(DocumentationProject project, IReadOnlyDictionary<string, byte[]> images, byte[]? logoPng = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(images);
        long total = 0;
        var dataUris = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var step in project.Steps)
        {
            if (!images.TryGetValue(step.ImageKey, out var bytes) || bytes is null || bytes.Length == 0)
                throw new InvalidDataException($"Missing documentation image: {step.ImageKey}");
            DocumentationArchivePolicy.ValidateImageLength(bytes.LongLength);
            total = checked(total + bytes.LongLength);
            if (total > DocumentationArchivePolicy.MaximumTotalImageBytes)
                throw new InvalidDataException("Self-contained tutorial image payload exceeds the project safety limit.");
            dataUris[step.ImageKey] = "data:image/png;base64," + Convert.ToBase64String(bytes);
        }
        string? logoHref = null;
        if (logoPng is { Length: > 0 })
        {
            DocumentationArchivePolicy.ValidateImageLength(logoPng.LongLength);
            total = checked(total + logoPng.LongLength);
            if (total > DocumentationArchivePolicy.MaximumTotalImageBytes)
                throw new InvalidDataException("Self-contained tutorial image payload exceeds the project safety limit.");
            logoHref = "data:image/png;base64," + Convert.ToBase64String(logoPng);
        }
        return BuildHtml(project, key => dataUris[key], logoHref);
    }

    public static string HtmlEncode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    public static string MarkdownEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var builder = new StringBuilder(value.Length + 16);
        foreach (var c in value)
        {
            if (c is '\\' or '`' or '*' or '_' or '{' or '}' or '[' or ']' or '<' or '>' or '(' or ')' or '#' or '+' or '-' or '.' or '!' or '|')
                builder.Append('\\');
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static void AppendHtmlContents(StringBuilder html, IReadOnlyList<DocumentationContentsEntry> contents)
    {
        if (contents.Count == 0) return;
        html.Append("<nav class=\"toc\"><h2>Contents</h2><ul>");
        foreach (var item in contents)
        {
            if (!string.IsNullOrWhiteSpace(item.Section))
                html.Append("<li class=\"toc-section\">").Append(HtmlEncode(item.Section)).Append("</li>");
            html.Append("<li><a href=\"#").Append(item.Anchor).Append("\">").Append(item.StepNumber).Append(". ")
                .Append(HtmlEncode(item.Title)).Append("</a></li>");
        }
        html.Append("</ul></nav>");
    }

    private static void AppendMarkdownContents(StringBuilder markdown, IReadOnlyList<DocumentationContentsEntry> contents)
    {
        if (contents.Count == 0) return;
        markdown.Append("## Contents\n\n");
        foreach (var item in contents)
        {
            if (!string.IsNullOrWhiteSpace(item.Section)) markdown.Append("- **").Append(MarkdownEscape(item.Section)).Append("**\n");
            markdown.Append("  - [").Append(item.StepNumber).Append(". ").Append(MarkdownEscape(item.Title)).Append("](#")
                .Append(item.Anchor).Append(")\n");
        }
        markdown.Append('\n');
    }

    private static string HtmlAttributeEncode(string value) => HtmlEncode(value).Replace("'", "&#39;", StringComparison.Ordinal);

    private static string EscapeMarkdownLink(string? value) => (value ?? string.Empty).Replace(" ", "%20", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal);

    private static string? BuildSourceLabel(DocumentationStep step)
    {
        var target = step.Target;
        if (target is null) return step.SafeKeyGesture;
        var source = !string.IsNullOrWhiteSpace(target.WindowTitle) ? target.WindowTitle : target.ProcessName;
        if (!string.IsNullOrWhiteSpace(step.SafeKeyGesture))
            return string.IsNullOrWhiteSpace(source) ? step.SafeKeyGesture : $"{source} · {step.SafeKeyGesture}";
        return source;
    }
}
