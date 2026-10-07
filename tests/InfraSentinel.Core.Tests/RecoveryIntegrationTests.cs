using IAEngine.Core.Recovery;
using InfraSentinel.Core;

namespace InfraSentinel.Core.Tests;

public sealed class RecoveryIntegrationTests
{
    [Fact]
    public async Task RecoveryStateUsesSentinelNamespaceAndPreservesIdentity()
    {
        var root = Directory.CreateTempSubdirectory("infrasentinel-recovery-");
        try
        {
            var configuration = new IAEngineConsumerConfiguration(root.FullName);
            var key = new ExecutionKey("infra-sentinel", "m22", "task-1", "execution-1");
            var service = configuration.CreateRecoveryService();
            await service.StartAsync(key);
            var result = await service.RecordArtifactAsync(key, new RecoveryArtifactIdentity("finding-1", "finding-catalog.json", "hash"));

            Assert.Single(result.Artifacts);
            Assert.False(Directory.Exists(Path.Combine(root.FullName, configuration.RunsDirectory)));
            Assert.Single(Directory.EnumerateFiles(Path.Combine(root.FullName, configuration.StateDirectory), "*.json", SearchOption.AllDirectories));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task DuplicateCheckpointDoesNotCreateSecondRecord()
    {
        var root = Directory.CreateTempSubdirectory("infrasentinel-recovery-");
        try
        {
            var configuration = new IAEngineConsumerConfiguration(root.FullName);
            var key = new ExecutionKey("infra-sentinel", "m22", "task-1", "execution-1");
            var service = configuration.CreateRecoveryService();
            await service.StartAsync(key);
            var checkpoint = new RecoveryCheckpointIdentity("checkpoint-1", "commit-1");
            await service.RecordCheckpointAsync(key, checkpoint);
            var result = await service.RecordCheckpointAsync(key, checkpoint);

            Assert.Single(result.Checkpoints);
        }
        finally { root.Delete(true); }
    }
}
