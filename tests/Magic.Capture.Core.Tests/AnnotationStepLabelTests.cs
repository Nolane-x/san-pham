using Magic.Capture.Core.Annotation;

namespace Magic.Capture.Core.Tests;

public sealed class AnnotationStepLabelTests
{
    [Theory]
    [InlineData(1, "1")]
    [InlineData(12, "12")]
    public void FormatsNumericSteps(int index, string expected) => Assert.Equal(expected, AnnotationStepLabels.Number(index));

    [Theory]
    [InlineData(1, "A")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    public void FormatsAlphabeticSteps(int index, string expected) => Assert.Equal(expected, AnnotationStepLabels.Alpha(index));

    [Theory]
    [InlineData(1, "I")]
    [InlineData(4, "IV")]
    [InlineData(19, "XIX")]
    public void FormatsRomanSteps(int index, string expected) => Assert.Equal(expected, AnnotationStepLabels.Roman(index));
}
