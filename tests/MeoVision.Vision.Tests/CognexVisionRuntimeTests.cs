using FluentAssertions;
using MeoVision.Core.Vision;
using MeoVision.Vision.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeoVision.Vision.Tests;

public class CognexVisionRuntimeTests
{
    private readonly CognexVisionRuntime _runtime =
        new(NullLogger<CognexVisionRuntime>.Instance);

    [Fact]
    public void Probe_Reads_Real_Version_And_Vx_Feature_Names()
    {
        VisionRuntimeInfo info = _runtime.Probe(new[] { "VxPatMax", "VxBlob" });

        info.Version.Should().NotBeNullOrWhiteSpace();
        info.LicensedFeatures.Should().Contain("VxPatMax");
        info.AllRequiredFeaturesLicensed.Should().BeTrue();
    }

    [Fact]
    public void Probe_Reports_Names_Without_Vx_Prefix_As_Missing()
    {
        // Tên "PatMax" (thiếu tiền tố Vx) không phải tên tính năng license.
        VisionRuntimeInfo info = _runtime.Probe(new[] { "PatMax" });

        info.MissingFeatures.Should().Equal("PatMax");
    }

    [Fact]
    public void ReleaseHardware_Does_Not_Throw_Without_Cameras()
    {
        _runtime.Invoking(r => r.ReleaseHardware()).Should().NotThrow();
    }
}
