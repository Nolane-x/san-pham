using System.IO.Compression;
using System.Text;
using Magic.Capture.Core.Documentation;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.Core.Tests;

public sealed class DocumentationTextExportTests
{
    [Fact]
    public void BuildHtml_EscapesUserTextAndUsesProvidedImageHref()
    {
        var project = Project("<Guide & Test>", "Use <Save> & continue");

        var html = DocumentationTextExport.BuildHtml(project, key => "images/" + key);

        Assert.Contains("&lt;Guide &amp; Test&gt;", html);
        Assert.Contains("Use &lt;Save&gt; &amp; continue", html);
        Assert.Contains("images/step.png", html);
        Assert.DoesNotContain("<Guide & Test>", html);
    }

    [Fact]
    public void BuildMarkdown_EscapesStructuralPunctuation()
    {
        var project = Project("# Guide", "Choose [Save] *now*");

        var markdown = DocumentationTextExport.BuildMarkdown(project, key => "images/" + key);

        Assert.Contains("# \\# Guide", markdown);
        Assert.Contains("Choose \\[Save\\] \\*now\\*", markdown);
        Assert.Contains("![Step 1](images/step.png)", markdown);
    }

    [Fact]
    public void BuildSelfContainedHtml_EmbedsPngAsDataUri()
    {
        var project = Project("Guide", "Click save");
        var images = new Dictionary<string, byte[]> { ["step.png"] = [1, 2, 3, 4] };

        var html = DocumentationTextExport.BuildSelfContainedHtml(project, images);

        Assert.Contains("data:image/png;base64,AQIDBA==", html);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DocumentationDocxWriter_WritesMinimalOpenXmlPackageWithMedia()
    {
        var project = Project("Guide & Steps", "Click <Save>");
        var images = new Dictionary<string, byte[]> { ["step.png"] = [137, 80, 78, 71, 1, 2, 3, 4] };

        var bytes = DocumentationDocxWriter.Write(project, images);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("_rels/.rels"));
        Assert.NotNull(archive.GetEntry("word/document.xml"));
        Assert.NotNull(archive.GetEntry("word/_rels/document.xml.rels"));
        Assert.NotNull(archive.GetEntry("word/media/image1.png"));
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        var xml = reader.ReadToEnd();
        Assert.Contains("Guide &amp; Steps", xml);
        Assert.Contains("Click &lt;Save&gt;", xml);
    }

    [Fact]
    public void BuildContents_EmitsSectionsOnceAndKeepsStepOrder()
    {
        var first = new DocumentationStep("s1", DateTimeOffset.UnixEpoch, "a.png", 640, 480, null, null, null, null, "Open", "Open it", "Basics");
        var second = first with { Id = "s2", ImageKey = "b.png", Title = "Save" };
        var third = first with { Id = "s3", ImageKey = "c.png", Title = "Share", Section = "Finish" };
        var project = DocumentationProject.Create("Guide", [first, second, third]);

        var contents = DocumentationTextExport.BuildContents(project);

        Assert.Equal([1, 2, 3], contents.Select(item => item.StepNumber).ToArray());
        Assert.Equal("Basics", contents[0].Section);
        Assert.Null(contents[1].Section);
        Assert.Equal("Finish", contents[2].Section);
        Assert.Equal(["step-001", "step-002", "step-003"], contents.Select(item => item.Anchor).ToArray());
    }

    [Fact]
    public void BuildHtml_RendersTemplateHeaderContentsFooterAndLogo()
    {
        var project = Project("Guide", "Click save") with
        {
            Header = "Internal <Guide>",
            Footer = "Page footer",
            LogoImageKey = "logo.png",
            Template = "presentation"
        };

        var html = DocumentationTextExport.BuildHtml(project, key => "images/" + key, "logo.png");

        Assert.Contains("template-presentation", html);
        Assert.Contains("Internal &lt;Guide&gt;", html);
        Assert.Contains("<nav class=\"toc\"", html);
        Assert.Contains("href=\"#step-001\"", html);
        Assert.Contains("id=\"step-001\"", html);
        Assert.Contains("src=\"logo.png\"", html);
        Assert.Contains("Page footer", html);
    }

    [Fact]
    public void BuildMarkdown_RendersHeaderContentsAnchorsAndLogo()
    {
        var project = Project("Guide", "Click save") with { Header = "Internal guide", LogoImageKey = "logo.png" };

        var markdown = DocumentationTextExport.BuildMarkdown(project, key => "images/" + key, "logo.png");

        Assert.Contains("![Logo](logo.png)", markdown);
        Assert.Contains("> Internal guide", markdown);
        Assert.Contains("## Contents", markdown);
        Assert.Contains("[1. Save](#step-001)", markdown);
        Assert.Contains("<a id=\"step-001\"></a>", markdown);
    }

    [Fact]
    public void BuildSelfContainedHtml_EmbedsOptionalLogoAsDataUri()
    {
        var project = Project("Guide", "Click save") with { LogoImageKey = "logo.png" };
        var images = new Dictionary<string, byte[]> { ["step.png"] = [1, 2, 3, 4] };

        var html = DocumentationTextExport.BuildSelfContainedHtml(project, images, [9, 8, 7, 6]);

        Assert.Contains("data:image/png;base64,CQgHBg==", html);
    }

    [Fact]
    public void DocumentationDocxWriter_WritesContentsAndRealHeaderFooterParts()
    {
        var project = Project("Guide", "Click save") with
        {
            Header = "Internal header",
            Footer = "Internal footer",
            Template = "print"
        };
        var images = new Dictionary<string, byte[]> { ["step.png"] = [137, 80, 78, 71, 1, 2, 3, 4] };

        var bytes = DocumentationDocxWriter.Write(project, images);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("word/header1.xml"));
        Assert.NotNull(archive.GetEntry("word/footer1.xml"));
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        var xml = reader.ReadToEnd();
        Assert.Contains("Contents", xml);
        Assert.Contains("Step 1: Save", xml);
        Assert.Contains("w:headerReference", xml);
        Assert.Contains("w:footerReference", xml);
    }

    private static DocumentationProject Project(string title, string description)
    {
        var step = new DocumentationStep(
            "s1", DateTimeOffset.UnixEpoch, "step.png", 640, 480, null,
            new PixelPoint(10, 10), DocumentationMouseButton.Left, null,
            "Save", description, "Basics");
        return DocumentationProject.Create(title, [step]);
    }
}
