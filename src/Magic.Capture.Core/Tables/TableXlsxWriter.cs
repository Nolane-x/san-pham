using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Magic.Capture.Core.Tables;

public static class TableXlsxWriter
{
    public const long MaximumOutputBytes = 64L * 1024 * 1024;
    private static readonly DateTimeOffset FixedTimestamp = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
    private const string SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string OfficeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static byte[] Write(EditableTableDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteContentTypes(archive);
            WriteRootRelationships(archive);
            WriteWorkbook(archive);
            WriteWorkbookRelationships(archive);
            WriteWorksheet(archive, document);
        }
        if (stream.Length > MaximumOutputBytes)
            throw new InvalidOperationException($"XLSX output exceeds the supported {MaximumOutputBytes / (1024 * 1024):N0} MB limit.");
        return stream.ToArray();
    }

    private static void WriteContentTypes(ZipArchive archive) => WriteXml(archive, "[Content_Types].xml", writer =>
    {
        writer.WriteStartElement("Types", ContentTypeNs);
        WriteType(writer, "Default", "Extension", "rels", "application/vnd.openxmlformats-package.relationships+xml");
        WriteType(writer, "Default", "Extension", "xml", "application/xml");
        WriteType(writer, "Override", "PartName", "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        WriteType(writer, "Override", "PartName", "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        writer.WriteEndElement();
    });

    private static void WriteType(XmlWriter writer, string element, string keyName, string keyValue, string contentType)
    {
        writer.WriteStartElement(element, ContentTypeNs);
        writer.WriteAttributeString(keyName, keyValue);
        writer.WriteAttributeString("ContentType", contentType);
        writer.WriteEndElement();
    }

    private static void WriteRootRelationships(ZipArchive archive) => WriteXml(archive, "_rels/.rels", writer =>
    {
        writer.WriteStartElement("Relationships", PackageRelNs);
        writer.WriteStartElement("Relationship", PackageRelNs);
        writer.WriteAttributeString("Id", "rId1");
        writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
        writer.WriteAttributeString("Target", "xl/workbook.xml");
        writer.WriteEndElement();
        writer.WriteEndElement();
    });

    private static void WriteWorkbook(ZipArchive archive) => WriteXml(archive, "xl/workbook.xml", writer =>
    {
        writer.WriteStartElement("workbook", SpreadsheetNs);
        writer.WriteAttributeString("xmlns", "r", "http://www.w3.org/2000/xmlns/", OfficeRelNs);
        writer.WriteStartElement("sheets", SpreadsheetNs);
        writer.WriteStartElement("sheet", SpreadsheetNs);
        writer.WriteAttributeString("name", "Table");
        writer.WriteAttributeString("sheetId", "1");
        writer.WriteAttributeString("r", "id", OfficeRelNs, "rId1");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    });

    private static void WriteWorkbookRelationships(ZipArchive archive) => WriteXml(archive, "xl/_rels/workbook.xml.rels", writer =>
    {
        writer.WriteStartElement("Relationships", PackageRelNs);
        writer.WriteStartElement("Relationship", PackageRelNs);
        writer.WriteAttributeString("Id", "rId1");
        writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet");
        writer.WriteAttributeString("Target", "worksheets/sheet1.xml");
        writer.WriteEndElement();
        writer.WriteEndElement();
    });

    private static void WriteWorksheet(ZipArchive archive, EditableTableDocument document) => WriteXml(archive, "xl/worksheets/sheet1.xml", writer =>
    {
        writer.WriteStartElement("worksheet", SpreadsheetNs);
        writer.WriteStartElement("sheetData", SpreadsheetNs);
        for (var row = 0; row < document.RowCount; row++)
        {
            writer.WriteStartElement("row", SpreadsheetNs);
            writer.WriteAttributeString("r", (row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (var column = 0; column < document.ColumnCount; column++)
            {
                var merge = document.MergeAt(row, column);
                if (merge is not null && (merge.AnchorRow != row || merge.AnchorColumn != column)) continue;
                var value = document.CellAt(row, column);
                if (value.Length == 0 && merge is null) continue;
                writer.WriteStartElement("c", SpreadsheetNs);
                writer.WriteAttributeString("r", CellReference(row, column));
                writer.WriteAttributeString("t", "inlineStr");
                writer.WriteStartElement("is", SpreadsheetNs);
                writer.WriteStartElement("t", SpreadsheetNs);
                writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                writer.WriteString(value);
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();

        if (document.Merges.Count > 0)
        {
            writer.WriteStartElement("mergeCells", SpreadsheetNs);
            writer.WriteAttributeString("count", document.Merges.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var merge in document.Merges)
            {
                writer.WriteStartElement("mergeCell", SpreadsheetNs);
                writer.WriteAttributeString("ref", RangeReference(merge.Range));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    });

    private static string RangeReference(TableCellRange range) =>
        $"{CellReference(range.StartRow, range.StartColumn)}:{CellReference(range.EndRow, range.EndColumn)}";

    private static string CellReference(int row, int column)
    {
        var value = column + 1;
        Span<char> buffer = stackalloc char[8];
        var position = buffer.Length;
        while (value > 0)
        {
            value--;
            buffer[--position] = (char)('A' + value % 26);
            value /= 26;
        }
        return new string(buffer[position..]) + (row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void WriteXml(ZipArchive archive, string path, Action<XmlWriter> write)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTimestamp;
        using var entryStream = entry.Open();
        using var writer = XmlWriter.Create(entryStream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            CloseOutput = false,
            CheckCharacters = true
        });
        writer.WriteStartDocument();
        write(writer);
        writer.WriteEndDocument();
    }
}
