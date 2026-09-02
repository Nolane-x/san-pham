using Magic.Capture.Core.Imaging;

namespace Magic.Capture.Core.Tests;

public sealed class ImageEffectPipelineTests
{
    [Fact]
    public void NormalizesEffectParametersAndStepCount()
    {
        var pipeline = new ImageEffectPipeline(Enumerable.Range(0, 100).Select(_ => new ImageEffectStep(ImageEffectKind.Brightness, 999)).ToArray()).Normalize();
        Assert.Equal(32, pipeline.Steps.Count);
        Assert.All(pipeline.Steps, step => Assert.Equal(100, step.Amount));
    }

    [Fact]
    public void BuiltInPresetsAreBoundedAndNonEmpty()
    {
        var presets = ImageEffectPresets.BuiltIn;
        Assert.NotEmpty(presets);
        Assert.All(presets, preset => Assert.InRange(preset.Pipeline.Steps.Count, 1, 32));
    }
    [Fact]
    public void NormalizesAdvancedEffectParameters()
    {
        Assert.Equal(180, new ImageEffectStep(ImageEffectKind.Hue, 999).Normalize().Amount);
        Assert.Equal(-100, new ImageEffectStep(ImageEffectKind.Vibrance, -999).Normalize().Amount);
        var balance = new ImageEffectStep(ImageEffectKind.ColorBalance, 999, -999, double.NaN).Normalize();
        Assert.Equal(100, balance.Amount);
        Assert.Equal(-100, balance.SecondaryAmount);
        Assert.Equal(0, balance.TertiaryAmount);
        Assert.Equal(64, new ImageEffectStep(ImageEffectKind.Mosaic, 999).Normalize().Amount);
        Assert.Equal(5, new ImageEffectStep(ImageEffectKind.Sharpen, 999).Normalize().Amount);
    }

    [Fact]
    public void EffectPackRoundTripsBoundedPortablePipeline()
    {
        var pipeline = new ImageEffectPipeline([
            new(ImageEffectKind.Hue, 30),
            new(ImageEffectKind.ColorBalance, 12, -8, 4),
            new(ImageEffectKind.Sharpen, 2),
        ]);
        var json = ImageEffectPackSerializer.Serialize("Documentation", pipeline);
        var pack = ImageEffectPackSerializer.Deserialize(json);
        Assert.Equal("Documentation", pack.Name);
        Assert.Equal(3, pack.Pipeline.Steps.Count);
        Assert.Equal(ImageEffectKind.ColorBalance, pack.Pipeline.Steps[1].Kind);
        Assert.Equal(-8, pack.Pipeline.Steps[1].SecondaryAmount);
    }

    [Fact]
    public void EffectPackRejectsOversizedInput()
    {
        var json = new string('x', ImageEffectPackSerializer.MaximumJsonBytes + 1);
        Assert.Throws<InvalidDataException>(() => ImageEffectPackSerializer.Deserialize(json));
    }

}
