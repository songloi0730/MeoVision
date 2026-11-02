using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MeoVision.Core.Contracts;
using MeoVision.Core.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeoVision.Engine.Simulated.Tests;

public class SimulatedVisionEngineTests
{
    [Fact]
    public async Task LoadRecipeAsync_Should_Throw_VisionRecipeException_WhenSampleFolderMissing()
    {
        SimulatedVisionEngine engine = new(NullLogger<SimulatedVisionEngine>.Instance);
        string missingFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        VisionRecipe recipe = TestRecipes.Default(missingFolder);

        Func<Task> act = () => engine.LoadRecipeAsync(recipe);

        await act.Should().ThrowAsync<VisionRecipeException>();
    }

    [Fact]
    public async Task LoadRecipeAsync_Should_Succeed_WhenSampleFolderExists()
    {
        SimulatedVisionEngine engine = new(NullLogger<SimulatedVisionEngine>.Instance);
        VisionRecipe recipe = TestRecipes.Default(Path.GetTempPath());

        await engine.LoadRecipeAsync(recipe);

        engine.IsJobLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task InspectAsync_Should_Throw_WhenRecipeNotLoaded()
    {
        SimulatedVisionEngine engine = new(NullLogger<SimulatedVisionEngine>.Instance);
        VisionJobRequest request = VisionJobRequest.CreateNew();

        Func<Task> act = () => engine.InspectAsync(request);

        await act.Should().ThrowAsync<VisionEngineException>();
    }

    [Fact]
    public async Task InspectAsync_Should_VaryScoreAcrossMultipleCalls()
    {
        SimulatedVisionEngine engine = new(NullLogger<SimulatedVisionEngine>.Instance, new Random(1234));
        VisionRecipe recipe = TestRecipes.Default(Path.GetTempPath());
        await engine.LoadRecipeAsync(recipe);

        List<double> scores = new();
        for (int i = 0; i < 25; i++)
        {
            VisionJobRequest request = VisionJobRequest.CreateNew();
            VisionResult result = await engine.InspectAsync(request);
            scores.Add(result.Score);
            result.Image?.Dispose();
        }

        scores.Distinct().Count().Should().BeGreaterThan(1);
    }
}
