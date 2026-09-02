using System.IO.Compression;
using System.Text;

namespace Magic.Capture.Core.Documentation;

public static class DocumentationDocxWriter
{
    private const long EmusPerTwip = 635L;
    private const long EnglishEmusPerInch = 914400L;

    public static byte[] Write(DocumentationProject project, IReadOnlyDictionary<string, byte[]> images, byte[]? logoPng = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(images);
        project = DocumentationPolicy.Normalize(project);
        if (project.Steps.Count == 0) throw new InvalidDataException("DOCX export requires at least one documentation step.");
        var profile = DocumentationTemplateCatalog.Get(project.Template);

        var orderedImages = new List<(DocumentationStep Step, byte[] Bytes, string RelationshipId, string FileName)>();
        long total = 0;
        var nextRelationship = 1;
        for (var i = 0; i < project.Steps.Count; i++)
        {
            var step = project.Steps[i];
            if (!images.TryGetValue(step.ImageKey, out var bytes) || bytes is null || bytes.Length == 0)
                throw new InvalidDataException($"Missing documentation image: {step.ImageKey}");
            DocumentationArchivePolicy.ValidateImageLength(bytes.LongLength);
            total = checked(total + bytes.LongLength);
            if (total > DocumentationArchivePolicy.MaximumTotalImageBytes)
                throw new InvalidDataException("DOCX image payload exceeds the documentation project safety limit.");
            orderedImages.Add((step, bytes, $"rId{nextRelationship++}", $"image{i + 1}.png"));
        }

        string? logoRelationshipId = null;
        if (logoPng is { Length: > 0 })
        {
            DocumentationArchivePolicy.ValidateImageLength(logoPng.LongLength);
            total = checked(total + logoPng.LongLength);
            if (total > DocumentationArchivePolicy.MaximumTotalImageBytes)
                throw new InvalidDataException("DOCX image payload exceeds the documentation project safety limit.");
            logoRelationshipId = $"rId{nextRelationship++}";
        }
        var headerRelationshipId = string.IsNullOrWhiteSpace(project.Header) ? null : $"rId{nextRelationship++}";
        var footerRelationshipId = string.IsNullOrWhiteSpace(project.Footer) ? null : $"rId{nextRelationship++}";

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(archive, "[Content_Types].xml", ContentTypesXml(headerRelationshipId is not null, footerRelationshipId is not null));
            WriteTextEntry(archive, "_rels/.rels", RootRelationshipsXml());
            WriteTextEntry(archive, "word/document.xml", BuildDocumentXml(project, profile, orderedImages, logoRelationshipId, headerRelationshipId, footerRelationshipId));
            WriteTextEntry(archive, "word/_rels/document.xml.rels", BuildDocumentRelationshipsXml(orderedImages, logoRelationshipId, headerRelationshipId, footerRelationshipId));
            foreach (var image in orderedImages)
            {
                var entry = archive.CreateEntry("word/media/" + image.FileName, CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.Write(image.Bytes, 0, image.Bytes.Length);
            }
            if (logoPng is { Length: > 0 })
            {
                var entry = archive.CreateEntry("word/media/logo.png", CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.Write(logoPng, 0, logoPng.Length);
            }
            if (headerRelationshipId is not null)
                WriteTextEntry(archive, "word/header1.xml", BuildHeaderFooterXml(project.Header!, isHeader: true));
            if (footerRelationshipId is not null)
                WriteTextEntry(archive, "word/footer1.xml", BuildHeaderFooterXml(project.Footer!, isHeader: false));
        }
        return output.ToArray();
    }

    private static string BuildDocumentXml(
        DocumentationProject project,
        DocumentationTemplateProfile profile,
        IReadOnlyList<(DocumentationStep Step, byte[] Bytes, string RelationshipId, string FileName)> images,
        string? logoRelationshipId,
        string? headerRelationshipId,
        string? footerRelationshipId)
    {
        var xml = new StringBuilder(12_000 + project.Steps.Count * 2400);
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
            .Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" ")
            .Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" ")
            .Append("xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" ")
            .Append("xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" ")
            .Append("xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>");

        if (logoRelationshipId is not null)
            AppendImageParagraph(xml, logoRelationshipId, 900, 320, 2L * EnglishEmusPerInch, EnglishEmusPerInch, 9000);
        AppendParagraph(xml, project.Title, bold: true, sizeHalfPoints: profile.Id == "presentation" ? 44 : 36);
        if (!string.IsNullOrWhiteSpace(project.Subtitle)) AppendParagraph(xml, project.Subtitle, bold: false, sizeHalfPoints: 24);

        var contents = DocumentationTextExport.BuildContents(project);
        if (contents.Count > 0)
        {
            AppendParagraph(xml, "Contents", bold: true, sizeHalfPoints: 28);
            foreach (var item in contents)
            {
                if (!string.IsNullOrWhiteSpace(item.Section)) AppendParagraph(xml, item.Section, bold: true, sizeHalfPoints: 22);
                AppendParagraph(xml, $"Step {item.StepNumber}: {item.Title}", bold: false, sizeHalfPoints: 20, leftIndentTwips: 360);
            }
        }

        string? currentSection = null;
        var maxWidthEmu = Math.Max(EnglishEmusPerInch, (profile.PageWidthTwips - profile.PageMarginTwips * 2L) * EmusPerTwip);
        var maxHeightEmu = Math.Max(EnglishEmusPerInch, (profile.PageHeightTwips - profile.PageMarginTwips * 2L - 1440) * EmusPerTwip);
        for (var i = 0; i < images.Count; i++)
        {
            var item = images[i];
            if (!string.Equals(currentSection, item.Step.Section, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(item.Step.Section))
            {
                currentSection = item.Step.Section;
                AppendParagraph(xml, currentSection, bold: true, sizeHalfPoints: 28);
            }
            AppendParagraph(xml, $"Step {i + 1}: {(string.IsNullOrWhiteSpace(item.Step.Title) ? $"Step {i + 1}" : item.Step.Title)}", bold: true, sizeHalfPoints: 26);
            if (!string.IsNullOrWhiteSpace(item.Step.Description)) AppendParagraph(xml, item.Step.Description, bold: false, sizeHalfPoints: 22);
            AppendImageParagraph(xml, item.RelationshipId, item.Step.Width, item.Step.Height, maxWidthEmu, maxHeightEmu, i + 1);
        }

        xml.Append("<w:sectPr>");
        if (headerRelationshipId is not null)
            xml.Append("<w:headerReference w:type=\"default\" r:id=\"").Append(headerRelationshipId).Append("\"/>");
        if (footerRelationshipId is not null)
            xml.Append("<w:footerReference w:type=\"default\" r:id=\"").Append(footerRelationshipId).Append("\"/>");
        xml.Append("<w:pgSz w:w=\"").Append(profile.PageWidthTwips).Append("\" w:h=\"").Append(profile.PageHeightTwips).Append("\"/>")
            .Append("<w:pgMar w:top=\"").Append(profile.PageMarginTwips).Append("\" w:right=\"").Append(profile.PageMarginTwips)
            .Append("\" w:bottom=\"").Append(profile.PageMarginTwips).Append("\" w:left=\"").Append(profile.PageMarginTwips).Append("\"/>")
            .Append("</w:sectPr></w:body></w:document>");
        return xml.ToString();
    }

    private static void AppendParagraph(StringBuilder xml, string? text, bool bold, int sizeHalfPoints, int leftIndentTwips = 0)
    {
        xml.Append("<w:p>");
        if (leftIndentTwips > 0) xml.Append("<w:pPr><w:ind w:left=\"").Append(leftIndentTwips).Append("\"/></w:pPr>");
        xml.Append("<w:r><w:rPr>");
        if (bold) xml.Append("<w:b/>");
        xml.Append("<w:sz w:val=\"").Append(sizeHalfPoints).Append("\"/></w:rPr><w:t xml:space=\"preserve\">")
            .Append(XmlEscape(text ?? string.Empty).Replace("\n", "&#10;", StringComparison.Ordinal))
            .Append("</w:t></w:r></w:p>");
    }

    private static void AppendImageParagraph(
        StringBuilder xml,
        string relationshipId,
        int pixelWidth,
        int pixelHeight,
        long maxWidthEmu,
        long maxHeightEmu,
        int index)
    {
        var safeWidth = Math.Max(1, pixelWidth);
        var safeHeight = Math.Max(1, pixelHeight);
        var widthEmu = maxWidthEmu;
        var heightEmu = checked((long)Math.Round(widthEmu * (safeHeight / (double)safeWidth)));
        if (heightEmu > maxHeightEmu)
        {
            heightEmu = maxHeightEmu;
            widthEmu = checked((long)Math.Round(heightEmu * (safeWidth / (double)safeHeight)));
        }
        widthEmu = Math.Max(1, widthEmu);
        heightEmu = Math.Max(1, heightEmu);
        xml.Append("<w:p><w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">")
            .Append("<wp:extent cx=\"").Append(widthEmu).Append("\" cy=\"").Append(heightEmu).Append("\"/>")
            .Append("<wp:docPr id=\"").Append(index).Append("\" name=\"Image ").Append(index).Append("\"/>")
            .Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>")
            .Append("<pic:nvPicPr><pic:cNvPr id=\"0\" name=\"Documentation image\"/><pic:cNvPicPr/></pic:nvPicPr>")
            .Append("<pic:blipFill><a:blip r:embed=\"").Append(relationshipId).Append("\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>")
            .Append("<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"").Append(widthEmu).Append("\" cy=\"").Append(heightEmu)
            .Append("\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>")
            .Append("</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>");
    }

    private static string BuildDocumentRelationshipsXml(
        IReadOnlyList<(DocumentationStep Step, byte[] Bytes, string RelationshipId, string FileName)> images,
        string? logoRelationshipId,
        string? headerRelationshipId,
        string? footerRelationshipId)
    {
        var xml = new StringBuilder(1400 + images.Count * 180);
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        foreach (var image in images)
            xml.Append("<Relationship Id=\"").Append(image.RelationshipId).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/").Append(image.FileName).Append("\"/>");
        if (logoRelationshipId is not null)
            xml.Append("<Relationship Id=\"").Append(logoRelationshipId).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/logo.png\"/>");
        if (headerRelationshipId is not null)
            xml.Append("<Relationship Id=\"").Append(headerRelationshipId).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>");
        if (footerRelationshipId is not null)
            xml.Append("<Relationship Id=\"").Append(footerRelationshipId).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/>");
        xml.Append("</Relationships>");
        return xml.ToString();
    }

    private static string BuildHeaderFooterXml(string text, bool isHeader)
    {
        var element = isHeader ? "w:hdr" : "w:ftr";
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><" + element +
            " xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:rPr><w:sz w:val=\"18\"/></w:rPr><w:t xml:space=\"preserve\">" +
            XmlEscape(text) + "</w:t></w:r></w:p></" + element + ">";
    }

    private static string ContentTypesXml(bool includeHeader, bool includeFooter)
    {
        var xml = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
            .Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">")
            .Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>")
            .Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>")
            .Append("<Default Extension=\"png\" ContentType=\"image/png\"/>")
            .Append("<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>");
        if (includeHeader)
            xml.Append("<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>");
        if (includeFooter)
            xml.Append("<Override PartName=\"/word/footer1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/>");
        return xml.Append("</Types>").ToString();
    }

    private static string RootRelationshipsXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
        "</Relationships>";

    private static void WriteTextEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: false);
        writer.Write(content);
    }

    private static string XmlEscape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);
}
