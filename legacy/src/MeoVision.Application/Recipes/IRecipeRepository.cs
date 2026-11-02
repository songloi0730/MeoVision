using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core.Contracts;

namespace MeoVision.Application.Recipes;

public interface IRecipeRepository
{
    Task<IReadOnlyList<VisionRecipe>> GetAllAsync(CancellationToken ct = default);

    Task<VisionRecipe> GetAsync(string recipeId, CancellationToken ct = default);
}
