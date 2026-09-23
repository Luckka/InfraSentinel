using InfraSentinel.Core;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class BootstrapMarkerTests
{
    [Fact]
    public void BootstrapIdentifiesProjectAndLocalEngineIntegration()
    {
        Assert.Equal("InfraSentinel", BootstrapMarker.ProjectName);
        Assert.Equal("Local IAEngine host integration", BootstrapMarker.RuntimeIntegrationStatus);
    }
}
