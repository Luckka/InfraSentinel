using OnlineOs.AiOrchestrator.Hosting;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class IAEngineCoreBoundaryTests
{
    [Fact]
    public void ConsumerLoadsEngineHostFromCoreAssemblyOnly()
    {
        var engineAssembly = typeof(EngineHost).Assembly;

        Assert.Equal("IAEngine.Core", engineAssembly.GetName().Name);
        Assert.DoesNotContain(
            engineAssembly.GetReferencedAssemblies(),
            assembly => string.Equals(assembly.Name, "IAEngine.OnlineOSAdapter", StringComparison.Ordinal));
    }
}
