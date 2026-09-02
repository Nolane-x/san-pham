using System.Globalization;
using Magic.Capture.Core.Geometry;
using Magic.Capture.Core.Tables;

namespace Magic.Capture.Core.Tests;

public sealed class TableCellInferenceTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData("42", TableCellKind.Integer)]
    [InlineData("1,234.50", TableCellKind.Decimal)]
    [InlineData("2026-08-24", TableCellKind.Date)]
    [InlineData("$19.99", TableCellKind.Currency)]
    [InlineData("12.5%", TableCellKind.Percent)]
    [InlineData("hello", TableCellKind.Text)]
    [InlineData("", TableCellKind.Empty)]
    public void ClassifiesCommonCellKinds(string value, TableCellKind expected)
    {
        Assert.Equal(expected, TableCellInference.Classify(value, EnUs));
    }

    [Fact]
    public void DetectsHeaderFromTypedColumns()
    {
        var table = Table(
            ["Product", "Qty", "Price"],
            ["Mouse", "2", "$19.99"],
            ["Keyboard", "1", "$79.00"]);

        var schema = TableCellInference.Infer(table, EnUs);

        Assert.True(schema.HasHeader);
        Assert.Equal(TableCellKind.Text, schema.Columns[0].DominantKind);
        Assert.Equal(TableCellKind.Integer, schema.Columns[1].DominantKind);
        Assert.Equal(TableCellKind.Currency, schema.Columns[2].DominantKind);
    }

    [Fact]
    public void FlagsTypeOutlierOnlyWhenColumnIsStable()
    {
        var table = Table(
            ["Name", "Qty"],
            ["A", "1"],
            ["B", "2"],
            ["C", "oops"],
            ["D", "4"]);

        var schema = TableCellInference.Infer(table, EnUs);

        var anomaly = Assert.Single(schema.Anomalies);
        Assert.Equal(3, anomaly.RowIndex);
        Assert.Equal(1, anomaly.ColumnIndex);
        Assert.Equal(TableCellKind.Text, anomaly.ActualKind);
        Assert.Equal(TableCellKind.Integer, anomaly.ExpectedKind);
    }

    [Fact]
    public void AnomaliesAreBounded()
    {
        var rows = new List<IReadOnlyList<string>> { new[] { "Qty" } };
        for (var i = 0; i < 300; i++) rows.Add(new[] { i < 220 ? i.ToString(CultureInfo.InvariantCulture) : "bad" });
        var table = new DetectedTable(rows, 1, rows.Count, .9, PixelRect.Empty);

        var schema = TableCellInference.Infer(table, EnUs);

        Assert.True(schema.Anomalies.Count <= TableCellInference.MaximumAnomalies);
    }

    private static DetectedTable Table(params string[][] rows) =>
        new(rows, rows.Max(row => row.Length), rows.Length, .9, PixelRect.Empty);
}
