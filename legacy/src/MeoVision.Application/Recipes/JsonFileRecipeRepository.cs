using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core;
using MeoVision.Core.Contracts;
using MeoVision.Core.Exceptions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MeoVision.Application.Recipes;

public sealed class JsonFileRecipeRepository : IRecipeRepository
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Ignore
    };

    private readonly string _recipesDirectory;
    private readonly ILogger<JsonFileRecipeRepository> _logger;

    public JsonFileRecipeRepository(MeoVisionHostOptions options, ILogger<JsonFileRecipeRepository> logger)
    {
        Guard.ThrowIfNull(options, nameof(options));
        Guard.ThrowIfNull(logger, nameof(logger));

        _recipesDirectory = Path.IsPathRooted(options.RecipesDirectory)
            ? options.RecipesDirectory
            : Path.Combine(AppContext.BaseDirectory, options.RecipesDirectory);
        _logger = logger;
    }

    public Task<IReadOnlyList<VisionRecipe>> GetAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!Directory.Exists(_recipesDirectory))
        {
            _logger.LogWarning("Thư mục recipe không tồn tại: {Directory}", _recipesDirectory);
            return Task.FromResult<IReadOnlyList<VisionRecipe>>(Array.Empty<VisionRecipe>());
        }

        List<VisionRecipe> recipes = new();
        foreach (string file in Directory.EnumerateFiles(_recipesDirectory, "*.recipe.json"))
        {
            string json = File.ReadAllText(file);
            VisionRecipe? recipe = JsonConvert.DeserializeObject<VisionRecipe>(json, SerializerSettings);
            if (recipe is not null)
            {
                recipes.Add(recipe);
            }
        }

        return Task.FromResult<IReadOnlyList<VisionRecipe>>(recipes);
    }

    public async Task<VisionRecipe> GetAsync(string recipeId, CancellationToken ct = default)
    {
        Guard.ThrowIfNull(recipeId, nameof(recipeId));

        IReadOnlyList<VisionRecipe> all = await GetAllAsync(ct).ConfigureAwait(false);
        VisionRecipe? found = all.FirstOrDefault(r => r.RecipeId == recipeId);
        return found ?? throw new VisionRecipeException($"Không tìm thấy recipe: {recipeId}");
    }
}
