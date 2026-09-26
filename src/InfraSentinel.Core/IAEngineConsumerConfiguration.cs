using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Models;
using IAEngine.Core.Git;
using InfraSentinel.Core.Architecture;
using InfraSentinel.Core.Integration;
using InfraSentinel.Core.Resilience;
using RoadmapMilestoneDefinition = OnlineOs.AiOrchestrator.Roadmap.MilestoneDefinition;
using RoadmapTaskDefinition = OnlineOs.AiOrchestrator.Roadmap.RoadmapTaskDefinition;
using MilestoneRuntimeStatus = OnlineOs.AiOrchestrator.Roadmap.MilestoneRuntimeStatus;

namespace InfraSentinel.Core;

/// <summary>
/// InfraSentinel-owned configuration for the local Engine host proof.
/// It declares names and storage boundaries but does not implement Engine behavior.
/// </summary>
public sealed record IAEngineConsumerConfiguration(string WorkspaceRoot)
{
    public const string ProjectId = "infra-sentinel";
    public const string EngineRevision = "a7b387e";
    public string RunsDirectory => ".ai-runs-infrasentinel";
    public string StateDirectory => ".ai-state-infrasentinel";
    public string ArchitectureDefenseArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "architecture-defense.json");
    public string ResilienceContractArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "resilience-contract.json");

    public InfraSentinelGitCheckpointCoordinator CreateCheckpointCoordinator(IGitService git, IProcessRunner processes)
        => new(git, processes, RunsDirectory);

    public InfraSentinelCheckpointRequestSource CreateCheckpointRequestSource(
        string branch,
        IReadOnlyList<string> expectedFiles,
        string commitMessage)
        => new(WorkspaceRoot, branch, expectedFiles, commitMessage);

    public ArchitectureDefenseValidationRunner CreateArchitectureDefenseRunner(ArchitectureDecisionFixture fixture)
        => new(new ArchitectureDefenseValidator(), fixture, ArchitectureDefenseArtifactPath);

    public ResilienceValidationRunner CreateResilienceRunner(ResilienceContractFixture fixture)
        => new(new ResilienceContractValidator(), fixture, ResilienceContractArtifactPath);

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

public sealed class InfraSentinelCheckpointRequestSource(
    string workspaceRoot,
    string branch,
    IReadOnlyList<string> expectedFiles,
    string commitMessage) : IGitCheckpointRequestSource
{
    public GitCheckpointRequest CreateForTask(RunRecord run)
        => Create(
            GitCheckpointScope.Task,
            run.MilestoneId,
            run.MilestoneTaskId ?? run.Task.Id,
            run.State == WorkflowState.Approved,
            run.State == WorkflowState.Approved,
            run.State == WorkflowState.Approved,
            run.State == WorkflowState.Approved,
            false);

    public GitCheckpointRequest CreateForMilestone(string milestoneId, EngineMilestoneExecutionResult result)
        => Create(
            GitCheckpointScope.Milestone,
            milestoneId,
            milestoneId,
            result.Status == MilestoneRuntimeStatus.Approved,
            result.Status == MilestoneRuntimeStatus.Approved,
            result.Status == MilestoneRuntimeStatus.Approved,
            result.Status == MilestoneRuntimeStatus.Approved,
            result.Status == MilestoneRuntimeStatus.Approved);

    private GitCheckpointRequest Create(
        GitCheckpointScope scope,
        string? milestoneId,
        string taskId,
        bool validationPassed,
        bool reviewPassed,
        bool remediationCompleted,
        bool taskCompleted,
        bool milestoneCompleted)
        => new(
            IAEngineConsumerConfiguration.ProjectId,
            workspaceRoot,
            scope,
            milestoneId,
            taskId,
            branch,
            commitMessage,
            GitCheckpointState.Approved,
            validationPassed,
            reviewPassed,
            remediationCompleted,
            taskCompleted,
            milestoneCompleted,
            true,
            true,
            true,
            expectedFiles.Count > 0,
            expectedFiles,
            expectedFiles,
            true,
            true,
            false,
            false);
}

public sealed class InfraSentinelMilestoneSource : IEngineMilestoneSource
{
    public Task<RoadmapMilestoneDefinition> LoadMilestoneAsync(string milestoneId, CancellationToken cancellationToken = default)
    {
        if (string.Equals(milestoneId, "sentinel-engine-controlled-checkpoint", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new RoadmapMilestoneDefinition
            {
                Id = "sentinel-engine-controlled-checkpoint",
                Title = "InfraSentinel engine-controlled checkpoint",
                Tasks =
                [
                    new RoadmapTaskDefinition { Id = "CHECKPOINT-001", Title = "Load Sentinel workspace", Description = "Load the local Sentinel workspace and checkpoint fixture." },
                    new RoadmapTaskDefinition { Id = "CHECKPOINT-002", Title = "Execute deterministic validation", Description = "Run the configured deterministic Sentinel validation.", DependsOn = ["CHECKPOINT-001"] },
                    new RoadmapTaskDefinition { Id = "CHECKPOINT-003", Title = "Execute review decision", Description = "Execute the local review decision.", DependsOn = ["CHECKPOINT-002"] },
                    new RoadmapTaskDefinition { Id = "CHECKPOINT-004", Title = "Evaluate generic checkpoint policy", Description = "Evaluate IAEngine's generic checkpoint policy.", DependsOn = ["CHECKPOINT-003"] },
                    new RoadmapTaskDefinition { Id = "CHECKPOINT-005", Title = "Create semantic commit when allowed", Description = "Create a local semantic commit only after all gates pass.", DependsOn = ["CHECKPOINT-004"] },
                    new RoadmapTaskDefinition { Id = "CHECKPOINT-006", Title = "Persist checkpoint result", Description = "Persist the explainable checkpoint artifact.", DependsOn = ["CHECKPOINT-005"] }
                ]
            });
        }

        if (string.Equals(milestoneId, "resilience-contract-validation", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new RoadmapMilestoneDefinition
            {
                Id = "resilience-contract-validation",
                Title = "InfraSentinel resilience contract validation",
                Tasks =
                [
                    new RoadmapTaskDefinition { Id = "RESILIENCE-001", Title = "Load resilience contract fixture", Description = "Load one local synthetic resilience contract fixture." },
                    new RoadmapTaskDefinition { Id = "RESILIENCE-002", Title = "Validate timeout and retry policy", Description = "Validate finite timeout, retry classification and bounded backoff.", DependsOn = ["RESILIENCE-001"] },
                    new RoadmapTaskDefinition { Id = "RESILIENCE-003", Title = "Validate idempotency and recovery", Description = "Validate repeat safety and recovery behavior.", DependsOn = ["RESILIENCE-002"] },
                    new RoadmapTaskDefinition { Id = "RESILIENCE-004", Title = "Validate observability and failure routing", Description = "Validate signals, error classification and failure destinations.", DependsOn = ["RESILIENCE-003"] },
                    new RoadmapTaskDefinition { Id = "RESILIENCE-005", Title = "Produce explainable resilience result", Description = "Persist resilience-contract.json under the InfraSentinel run directory.", DependsOn = ["RESILIENCE-004"] }
                ]
            });
        }

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
