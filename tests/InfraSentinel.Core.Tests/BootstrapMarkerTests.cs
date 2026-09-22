using InfraSentinel.Core;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class BootstrapMarkerTests
{
    [Fact]
    public void BootstrapIdentifiesProjectAndDefersEngineIntegration()
    {
        Assert.Equal("InfraSentinel", BootstrapMarker.ProjectName);
        Assert.Equal("Not integrated with IAEngine", BootstrapMarker.RuntimeIntegrationStatus);
    }
}
