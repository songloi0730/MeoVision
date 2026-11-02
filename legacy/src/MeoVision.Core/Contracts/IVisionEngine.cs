using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeoVision.Core.Contracts;

public interface IVisionEngine : IDisposable
{
    string EngineName { get; }

    bool IsJobLoaded { get; }

    Task LoadRecipeAsync(VisionRecipe recipe, CancellationToken ct = default);

    Task<VisionResult> InspectAsync(VisionJobRequest request, CancellationToken ct = default);

    Task TeachPatternAsync(VisionTrainRegion trainRegion, CancellationToken ct = default);

    event EventHandler<VisionResult>? InspectionCompleted;
}
