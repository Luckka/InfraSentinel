using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Models;
using IAEngine.Core.Git;
using InfraSentinel.Core.Architecture;
using InfraSentinel.Core.Adapters;
using InfraSentinel.Core.Integration;
using InfraSentinel.Core.Observability;
using InfraSentinel.Core.Resilience;
using InfraSentinel.Core.Security;
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
    public string SecurityInvariantArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "security-invariant-gate.json");
    public string ObservabilityEvidenceArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "observability-evidence-gate.json");
    public string ProjectAdapterArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "project-adapter-boundary.json");

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

    public SecurityInvariantValidationRunner CreateSecurityRunner(SecurityInvariantFixture fixture)
        => new(new SecurityInvariantValidator(), fixture, SecurityInvariantArtifactPath);

    public ObservabilityEvidenceAggregator CreateEvidenceAggregator(
        string executionId,
        string milestoneId,
        IReadOnlyList<string> taskIds,
        string fixtureId,
        string fixtureVersion,
        string branch)
        => new(executionId, ProjectId, milestoneId, taskIds, fixtureId, fixtureVersion, EngineRevision, branch);

    public ObservabilityEvidenceWriter CreateEvidenceWriter()
        => new(ObservabilityEvidenceArtifactPath);

    public IProjectAdapterSelector CreateProjectAdapterSelector()
        => new ProjectAdapterSelector([new DotNetProjectAdapter(), new NodeProjectAdapter()]);

    public ProjectAdapterArtifactWriter CreateProjectAdapterArtifactWriter()
        => new(ProjectAdapterArtifactPath);

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
        if (string.Equals(milestoneId, "sentinel-observability-evidence-gate", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new RoadmapMilestoneDefinition
            {
                Id = "sentinel-observability-evidence-gate",
                Title = "InfraSentinel observability and evidence gate",
                Tasks =
                [
                    new RoadmapTaskDefinition { Id = "OBS-001", Title = "Initialize evidence context", Description = "Initialize the local evidence aggregator and fixture metadata." },
                    new RoadmapTaskDefinition { Id = "OBS-002", Title = "Record deterministic validation evidence", Description = "Record validator, rules, findings and severities.", DependsOn = ["OBS-001"] },
                    new RoadmapTaskDefinition { Id = "OBS-003", Title = "Record retry and remediation evidence", Description = "Record bounded retry and remediation events.", DependsOn = ["OBS-002"] },
                    new RoadmapTaskDefinition { Id = "OBS-004", Title = "Record review evidence", Description = "Record review outcome and findings.", DependsOn = ["OBS-003"] },
                    new RoadmapTaskDefinition { Id = "OBS-005", Title = "Validate evidence completeness", Description = "Validate that the evidence is explainable and reproducible.", DependsOn = ["OBS-004"] },
                    new RoadmapTaskDefinition { Id = "OBS-006", Title = "Execute evidence checkpoint", Description = "Persist evidence and create a semantic checkpoint only after approval.", DependsOn = ["OBS-005"] }
                ]
            });
        }

        if (string.Equals(milestoneId, "project-adapter-boundary-validation", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new RoadmapMilestoneDefinition
            {
                Id = "project-adapter-boundary-validation",
                Title = "InfraSentinel project adapter boundary validation",
                Tasks =
                [
                    new RoadmapTaskDefinition { Id = "ADAPTER-001", Title = "Define project adapter contract", Description = "Load a local workspace through the adapter boundary." },
                    new RoadmapTaskDefinition { Id = "ADAPTER-002", Title = "Create technology-neutral project model", Description = "Produce a neutral model without technology-specific Engine types.", DependsOn = ["ADAPTER-001"] },
                    new RoadmapTaskDefinition { Id = "ADAPTER-003", Title = "Implement .NET project adapter", Description = "Analyze supported .NET manifests without executing project commands.", DependsOn = ["ADAPTER-002"] },
                    new RoadmapTaskDefinition { Id = "ADAPTER-004", Title = "Implement Node.js project adapter", Description = "Analyze supported Node manifests without executing project scripts.", DependsOn = ["ADAPTER-003"] },
                    new RoadmapTaskDefinition { Id = "ADAPTER-005", Title = "Validate adapter selection and isolation", Description = "Verify unsupported workspaces and path/security boundaries.", DependsOn = ["ADAPTER-004"] },
                    new RoadmapTaskDefinition { Id = "ADAPTER-006", Title = "Integrate adapters with EngineHost", Description = "Execute adapter validation through the existing EngineHost.", DependsOn = ["ADAPTER-005"] },
                    new RoadmapTaskDefinition { Id = "ADAPTER-007", Title = "Produce adapter evidence artifact", Description = "Persist project-adapter-boundary.json under the Sentinel run directory.", DependsOn = ["ADAPTER-006"] },
                    new RoadmapTaskDefinition { Id = "ADAPTER-008", Title = "Validate approval and checkpoint behavior", Description = "Require approval before a local checkpoint.", DependsOn = ["ADAPTER-007"] }
                ]
            });
        }

        if (string.Equals(milestoneId, "sentinel-security-invariant-gate", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new RoadmapMilestoneDefinition
            {
                Id = "sentinel-security-invariant-gate",
                Title = "InfraSentinel security invariant gate",
                Tasks =
                [
                    new RoadmapTaskDefinition { Id = "SECURITY-001", Title = "Define security invariant domain model", Description = "Load the local synthetic security fixture." },
                    new RoadmapTaskDefinition { Id = "SECURITY-002", Title = "Implement deterministic security validator", Description = "Evaluate security invariants without external services.", DependsOn = ["SECURITY-001"] },
                    new RoadmapTaskDefinition { Id = "SECURITY-003", Title = "Implement validation runner and artifact", Description = "Persist security-invariant-gate.json in the Sentinel run directory.", DependsOn = ["SECURITY-002"] },
                    new RoadmapTaskDefinition { Id = "SECURITY-004", Title = "Integrate with EngineHost", Description = "Execute the security validation through the EngineHost workflow.", DependsOn = ["SECURITY-003"] },
                    new RoadmapTaskDefinition { Id = "SECURITY-005", Title = "Validate approval and HumanRequired scenarios", Description = "Verify approval boundaries and critical finding handling.", DependsOn = ["SECURITY-004"] },
                    new RoadmapTaskDefinition { Id = "SECURITY-006", Title = "Execute engine-controlled checkpoint", Description = "Request a semantic checkpoint only after all gates pass.", DependsOn = ["SECURITY-005"] }
                ]
            });
        }

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
