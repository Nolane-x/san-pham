using System.Globalization;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Tables;

namespace Magic.Capture.Core.Tests;

public sealed class TableDelimitedSerializerTests
{
    private static readonly DetectedTable Table = new(
        [["Name", "Amount", "Formula"], ["Alice", "12.50", "=2+2"], ["Bob", "1,234.75", "+SUM(A1:A2)"]],
        3, 3, .9, PixelRect.Empty);

    [Fact]
    public void SemicolonDialectQuotesOnlyWhenRequired()
    {
        var text = TableSerializers.ToDelimited(Table, new TableDelimitedOptions(';'));

        Assert.Contains("Name;Amount;Formula", text);
        Assert.Contains("Alice;12.50;=2+2", text);
    }

    [Fact]
    public void ExcelFriendlyTsvNeutralizesFormulaText()
    {
        var text = TableSerializers.ToExcelFriendlyTsv(Table, CultureInfo.GetCultureInfo("en-US"));

        Assert.Contains("'=" + "2+2", text);
        Assert.Contains("'+SUM(A1:A2)", text);
    }

    [Fact]
    public void InvariantLocaleUsesDotDecimal()
    {
        var table = new DetectedTable([["Value"], ["12,5"]], 1, 2, .9, PixelRect.Empty);
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var text = TableSerializers.ToDelimited(
            table,
            new TableDelimitedOptions(';', TableNumberLocaleMode.Invariant),
            culture);

        Assert.Contains("12.5", text);
    }

    [Fact]
    public void CurrentLocaleUsesCultureDecimalSeparator()
    {
        var table = new DetectedTable([["Value"], ["12.5"]], 1, 2, .9, PixelRect.Empty);
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var text = TableSerializers.ToDelimited(
            table,
            new TableDelimitedOptions(';', TableNumberLocaleMode.CurrentCulture),
            culture);

        Assert.Contains("12,5", text);
    }
}
