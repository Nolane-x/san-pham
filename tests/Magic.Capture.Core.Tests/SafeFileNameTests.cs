using Magic.Capture.Core.Export;

namespace Magic.Capture.Core.Tests;

public sealed class SafeFileNameTests
{
    [Fact]
    public void ReplacesWindowsInvalidCharacters()
    {
        Assert.Equal("report_2026_08_23", SafeFileName.Sanitize("report:2026/08*23"));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("prn.txt")]
    [InlineData("LPT1.png")]
    public void PrefixesReservedDeviceNames(string input)
    {
        Assert.StartsWith("_", SafeFileName.Sanitize(input));
    }

    [Fact]
    public void EmptyNameFallsBackToOfficialProductBrandSpacing()
    {
        Assert.Equal("Magic Capture Desktop", SafeFileName.Sanitize("   "));
    }

    [Fact]
    public void TemplateRendersDeterministically()
    {
        var ts = new DateTimeOffset(2026, 8, 23, 18, 12, 5, TimeSpan.FromHours(7));
        var result = FileNameTemplate.Render("Magic_{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}_{counter}", ts, 7);
        Assert.Equal("Magic_2026-08-23_18-12-05_7", result);
    }
}
