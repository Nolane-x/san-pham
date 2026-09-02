using System.Text.Json;
using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Ai;
using Magic.Capture.Core.Storage;

namespace Magic.Capture.App.Ai;

internal sealed class MagicRecipeStore
{
    private const long MaxImportBytes = 512 * 1024;
    private readonly string _path;
    private bool _writeEnabled;

    public MagicRecipeStore(AppPaths paths) => _path = paths.AiRecipesFile;

    public async Task<IReadOnlyList<MagicRecipe>> LoadAsync(CancellationToken cancellationToken = default)
    {
        _writeEnabled = false;
        var recipes = await AtomicJsonFile.ReadAsync<List<MagicRecipe>>(
            _path, cancellationToken, LocalConfigurationLimits.MaximumMagicRecipeJsonBytes) ?? [];
        LocalConfigurationLimits.ValidateCount(recipes.Count, LocalConfigurationLimits.MaximumMagicRecipes, "Magic Recipes");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipe in recipes)
        {
            if (recipe is null) throw new InvalidDataException("Magic Recipe storage contains a null recipe.");
            var validation = MagicRecipeValidator.Validate(recipe);
            if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
            if (!ids.Add(recipe.Id)) throw new InvalidDataException($"Duplicate Magic Recipe id: {recipe.Id}");
        }
        _writeEnabled = true;
        return recipes.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task SaveAsync(IEnumerable<MagicRecipe> recipes, CancellationToken cancellationToken = default)
    {
        if (!_writeEnabled) throw new InvalidOperationException("Magic Recipe storage is not safely loaded; reload it before saving.");
        ArgumentNullException.ThrowIfNull(recipes);
        var valid = recipes.Take(LocalConfigurationLimits.MaximumMagicRecipes + 1).ToArray();
        LocalConfigurationLimits.ValidateCount(valid.Length, LocalConfigurationLimits.MaximumMagicRecipes, "Magic Recipes");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipe in valid)
        {
            if (recipe is null) throw new InvalidDataException("Magic Recipe storage cannot contain null recipes.");
            var validation = MagicRecipeValidator.Validate(recipe);
            if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
            if (!ids.Add(recipe.Id)) throw new InvalidDataException($"Duplicate Magic Recipe id: {recipe.Id}");
        }
        await AtomicJsonFile.WriteAsync(_path, valid, cancellationToken, LocalConfigurationLimits.MaximumMagicRecipeJsonBytes);
    }

    public async Task<MagicRecipe> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > MaxImportBytes) throw new InvalidDataException("Magic Recipe file is missing or too large.");
        await using var stream = info.OpenRead();
        var recipe = await JsonSerializer.DeserializeAsync<MagicRecipe>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Magic Recipe file is invalid.");
        var validation = MagicRecipeValidator.Validate(recipe);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        return recipe;
    }

    public async Task ExportAsync(MagicRecipe recipe, string path, CancellationToken cancellationToken = default)
    {
        var validation = MagicRecipeValidator.Validate(recipe);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, recipe, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
    }
}
