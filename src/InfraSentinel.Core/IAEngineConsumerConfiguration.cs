using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Hosting;
using InfraSentinel.Core.Architecture;
using RoadmapMilestoneDefinition = OnlineOs.AiOrchestrator.Roadmap.MilestoneDefinition;
using RoadmapTaskDefinition = OnlineOs.AiOrchestrator.Roadmap.RoadmapTaskDefinition;

namespace InfraSentinel.Core;

/// <summary>
/// InfraSentinel-owned configuration for the local Engine host proof.
/// It declares names and storage boundaries but does not implement Engine behavior.
/// </summary>
public sealed record IAEngineConsumerConfiguration(string WorkspaceRoot)
{
    public const string ProjectId = "infra-sentinel";
    public const string EngineRevision = "699dfe7";
    public string RunsDirectory => ".ai-runs-infrasentinel";
    public string StateDirectory => ".ai-state-infrasentinel";
    public string ArchitectureDefenseArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "architecture-defense.json");

    public ArchitectureDefenseValidationRunner CreateArchitectureDefenseRunner(ArchitectureDecisionFixture fixture)
        => new(new ArchitectureDefenseValidator(), fixture, ArchitectureDefenseArtifactPath);

    public EngineCompositionPlan Composition => new(
        ProjectId,
        false,
        false,
        false,
        false,
        false,
        [],
        ["local-router", "local-implementation", "local-review"],
        ["local-validator"],
        ["local-policy"],
        ["validation"]);

    public AppOptions CreateOptions(int engineeringRemediationCycles = 5, int validationRemediationCycles = 5) => new()
    {
        Project = new ProjectProfileOptions
        {
            Id = ProjectId,
            WorkspaceRoot = WorkspaceRoot,
            Stack = "dotnet",
            Validators = ["local-validator"],
            Policies = ["local-policy"],
            Composition = new ProjectCompositionOptions
            {
                Providers = ["local-router", "local-implementation", "local-review"],
                Capabilities = ["validation"]
            }
        },
        Orchestrator = new OrchestratorOptions
        {
            RunsDirectory = RunsDirectory,
            ProcessTimeoutSeconds = 30,
            EngineeringRemediationCycles = engineeringRemediationCycles,
            DeterministicValidationRemediationCycles = validationRemediationCycles
        },
        ReviewPolicy = new ReviewPolicyOptions()
    };
}

public sealed class InfraSentinelMilestoneSource : IEngineMilestoneSource
{
    public Task<RoadmapMilestoneDefinition> LoadMilestoneAsync(string milestoneId, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(milestoneId, "sentinel-bootstrap-validation", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(milestoneId, "architecture-defense-validation", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Synthetic milestone '{milestoneId}' was not found.");

            return Task.FromResult(new RoadmapMilestoneDefinition
            {
                Id = "architecture-defense-validation",
                Title = "InfraSentinel architecture defense validation",
                Tasks =
                [
                    new RoadmapTaskDefinition { Id = "ARCH-DEFENSE-001", Title = "Evaluate architecture decision fixture", Description = "Evaluate one local architecture decision using deterministic defense rules." },
                    new RoadmapTaskDefinition { Id = "ARCH-DEFENSE-002", Title = "Persist explainable architecture evidence", Description = "Persist architecture-defense.json under the InfraSentinel run directory.", DependsOn = ["ARCH-DEFENSE-001"] }
                ]
            });
        }

        return Task.FromResult(new RoadmapMilestoneDefinition
        {
            Id = "sentinel-bootstrap-validation",
            Title = "Synthetic InfraSentinel bootstrap validation",
            Tasks =
            [
                new RoadmapTaskDefinition { Id = "SENTINEL-001", Title = "Load synthetic infrastructure fixture", Description = "Load only a local synthetic fixture." },
                new RoadmapTaskDefinition { Id = "SENTINEL-002", Title = "Execute deterministic local validator", Description = "Run a fake deterministic validator.", DependsOn = ["SENTINEL-001"] },
                new RoadmapTaskDefinition { Id = "SENTINEL-003", Title = "Produce explainable result", Description = "Record a local explainable result.", DependsOn = ["SENTINEL-002"] }
            ]
        });
    }
}
