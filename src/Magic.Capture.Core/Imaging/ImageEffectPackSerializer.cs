using System.Text;
using System.Text.Json;

namespace Magic.Capture.Core.Imaging;

public sealed record ImageEffectPack(string Name, ImageEffectPipeline Pipeline);

public static class ImageEffectPackSerializer
{
    public const int SchemaVersion = 1;
    public const int MaximumJsonBytes = 64 * 1024;
    public const int MaximumNameCharacters = 128;

    private sealed record PackDto(int SchemaVersion, string Name, IReadOnlyList<ImageEffectStep> Steps);

    private static readonly JsonSerializerOptions Options = new()
    {
        MaxDepth = 16,
        WriteIndented = true,
    };

    public static string Serialize(string name, ImageEffectPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        var normalizedName = NormalizeName(name);
        var normalized = pipeline.Normalize();
        ValidateSteps(normalized.Steps);
        var json = JsonSerializer.Serialize(new PackDto(SchemaVersion, normalizedName, normalized.Steps), Options);
        if (Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
            throw new InvalidDataException($"Effect pack exceeds {MaximumJsonBytes:N0} UTF-8 bytes.");
        return json;
    }

    public static ImageEffectPack Deserialize(string json)
    {
        if (json is null) throw new ArgumentNullException(nameof(json));
        if (Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
            throw new InvalidDataException($"Effect pack exceeds {MaximumJsonBytes:N0} UTF-8 bytes.");
        PackDto? dto;
        try { dto = JsonSerializer.Deserialize<PackDto>(json, Options); }
        catch (JsonException ex) { throw new InvalidDataException("The effect pack JSON is invalid.", ex); }
        if (dto is null) throw new InvalidDataException("The effect pack is empty.");
        if (dto.SchemaVersion != SchemaVersion) throw new InvalidDataException($"Unsupported effect-pack schema {dto.SchemaVersion}.");
        var name = NormalizeName(dto.Name);
        var rawSteps = dto.Steps ?? Array.Empty<ImageEffectStep>();
        if (rawSteps.Count > 32) throw new InvalidDataException("Effect packs may contain at most 32 steps.");
        ValidateSteps(rawSteps);
        var pipeline = new ImageEffectPipeline(rawSteps).Normalize();
        return new ImageEffectPack(name, pipeline);
    }

    private static string NormalizeName(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0) value = "Effect pack";
        if (value.Length > MaximumNameCharacters) value = value[..MaximumNameCharacters];
        foreach (var ch in value)
            if (char.IsControl(ch)) throw new InvalidDataException("Effect-pack name contains control characters.");
        return value;
    }

    private static void ValidateSteps(IEnumerable<ImageEffectStep> steps)
    {
        foreach (var step in steps)
        {
            if (step is null) throw new InvalidDataException("Effect-pack steps must not be null.");
            if (!Enum.IsDefined(step.Kind)) throw new InvalidDataException($"Unknown image effect kind: {(int)step.Kind}.");
        }
    }
}
