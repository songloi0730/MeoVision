using MeoVision.Core.Contracts;

namespace MeoVision.Engine.Simulated.Tests;

internal static class TestRecipes
{
    public static VisionRecipe Default(string sampleImageFolder) => new(
        RecipeId: "test",
        JobName: "TB_Test",
        JobFilePath: @"Jobs\TB_Test.vpp",
        SampleImageFolder: sampleImageFolder,
        MinAcceptScore: 0.75,
        PartWidthMinMm: 24.5,
        PartWidthMaxMm: 25.5,
        ExpectedPadCount: 6,
        MaxRetries: 2,
        CycleTimeoutMs: 2000,
        JobLoadTimeoutMs: 10_000);
}
