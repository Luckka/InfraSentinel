using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Models;
using IAEngine.Core.Git;
using InfraSentinel.Core.Architecture;
using InfraSentinel.Core.Adapters;
using InfraSentinel.Core.Cloud;
using InfraSentinel.Core.Integration;
using InfraSentinel.Core.IaC;
using InfraSentinel.Core.Findings;
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
    public const string EngineRevision = "501d0b9";
    public string RunsDirectory => ".ai-runs-infrasentinel";
    public string StateDirectory => ".ai-state-infrasentinel";
    public string ArchitectureDefenseArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "architecture-defense.json");
    public string ResilienceContractArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "resilience-contract.json");
    public string SecurityInvariantArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "security-invariant-gate.json");
    public string ObservabilityEvidenceArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "observability-evidence-gate.json");
    public string ProjectAdapterArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "project-adapter-boundary.json");
    public string TerraformStaticAnalysisArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "terraform-static-analysis.json");
    public string CloudObservationArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "cloud-observation.json");
    public string CloudProviderSafetyArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "cloud-provider-safety-boundary.json");
    public string CloudFindingsArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "cloud-findings-gate.json");
    public string FindingCatalogArtifactPath => Path.Combine(WorkspaceRoot, RunsDirectory, "finding-catalog.json");

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
        => new ProjectAdapterSelector([new DotNetProjectAdapter(), new NodeProjectAdapter(), new TerraformProjectAdapter()]);

    public ProjectAdapterArtifactWriter CreateProjectAdapterArtifactWriter()
        => new(ProjectAdapterArtifactPath);

    public TerraformAnalysisArtifactWriter CreateTerraformArtifactWriter()
        => new(TerraformStaticAnalysisArtifactPath);

    public CloudObservationArtifactWriter CreateCloudObservationArtifactWriter()
        => new(CloudObservationArtifactPath);

    public CloudProviderSafetyArtifactWriter CreateCloudProviderSafetyArtifactWriter()
        => new(CloudProviderSafetyArtifactPath);

    public CloudFindingsEvidenceWriter CreateCloudFindingsEvidenceWriter()
        => new(CloudFindingsArtifactPath);

    public FindingCatalog CreateFindingCatalog(string executionId, string milestoneId)
        => new(executionId, ProjectId, milestoneId);

    public FindingCatalogWriter CreateFindingCatalogWriter()
        => new(FindingCatalogArtifactPath);

    public CloudFindingsGate CreateCloudFindingsGate(IReadOnlySet<string>? authorizedRegions = null)
        => new(authorizedRegions ?? new HashSet<string>(["us-east-1"], StringComparer.Ordinal));

    public AwsReadOnlyConfiguration CreateAwsReadOnlyConfiguration()
        => AwsReadOnlyConfiguration.FromEnvironment();

    public ICloudObservationProvider CreateAwsReadOnlyProvider(
        AwsReadOnlyConfiguration configuration,
        IAwsReadOnlyClient? client = null)
    {
        var policy = new AwsReadOnlyPolicy();
        var sdkClient = client ?? new AwsSdkReadOnlyClient(policy);
        return new AwsReadOnlyObservationProvider(configuration, sdkClient, new AwsIdentityVerifier(sdkClient), policy, new AwsReadOnlySnapshotNormalizer());
    }

    public ICloudObservationProvider CreateConfiguredCloudObservationProvider(
        AwsReadOnlyConfiguration configuration,
        IAwsReadOnlyClient? client = null)
    {
        if (configuration.LiveAwsEnabled && !configuration.DryRun)
            return CreateAwsReadOnlyProvider(configuration, client);
        return new SyntheticCloudObservationProvider(new CloudInfrastructureSnapshot(
            "offline-empty",
            "m18-1",
            "synthetic-offline",
            "offline",
            configuration.AuthorizedRegions.FirstOrDefault() ?? "us-east-1",
            "unknown",
            DateTimeOffset.UnixEpoch,
            CloudObservationSourceType.Synthetic,
            [], [], [], [], [], [], [], [], [],
            CloudCollectionStatus.Collected));
    }

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

        if (string.Equals(milestoneId, "terraform-static-analysis-validation", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new RoadmapMilestoneDefinition
            {
                Id = "terraform-static-analysis-validation",
                Title = "InfraSentinel Terraform static analysis validation",
                Tasks =
                [
                    new RoadmapTaskDefinition { Id = "IAC-001", Title = "Define neutral IaC model", Description = "Load the local Terraform fixture through the neutral IaC boundary." },
                    new RoadmapTaskDefinition { Id = "IAC-002", Title = "Implement Terraform adapter selection", Description = "Select exactly one local Terraform adapter.", DependsOn = ["IAC-001"] },
                    new RoadmapTaskDefinition { Id = "IAC-003", Title = "Implement deterministic Terraform analysis", Description = "Parse the documented Terraform subset without executing Terraform.", DependsOn = ["IAC-002"] },
                    new RoadmapTaskDefinition { Id = "IAC-004", Title = "Add security and resilience IaC rules", Description = "Evaluate declared infrastructure invariants.", DependsOn = ["IAC-003"] },
                    new RoadmapTaskDefinition { Id = "IAC-005", Title = "Validate safe Terraform fixture", Description = "Validate the safe synthetic fixture.", DependsOn = ["IAC-004"] },
                    new RoadmapTaskDefinition { Id = "IAC-006", Title = "Validate insecure and critical fixtures", Description = "Validate blocked and human-required synthetic fixtures.", DependsOn = ["IAC-005"] },
                    new RoadmapTaskDefinition { Id = "IAC-007", Title = "Integrate with EngineHost", Description = "Execute IaC analysis through the existing EngineHost.", DependsOn = ["IAC-006"] },
                    new RoadmapTaskDefinition { Id = "IAC-008", Title = "Persist evidence artifact", Description = "Persist terraform-static-analysis.json under the Sentinel run directory.", DependsOn = ["IAC-007"] },
                    new RoadmapTaskDefinition { Id = "IAC-009", Title = "Validate approval and checkpoint blocking", Description = "Require explicit approval and preserve checkpoint boundaries.", DependsOn = ["IAC-008"] }
                ]
            });
        }

        if (string.Equals(milestoneId, "read-only-cloud-observation-validation", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new RoadmapMilestoneDefinition
            {
                Id = "read-only-cloud-observation-validation",
                Title = "InfraSentinel cloud observation contract",
                Tasks =
                [
                    new RoadmapTaskDefinition { Id = "CLOUD-001", Title = "Define cloud observation contracts", Description = "Load the local observation request and scope." },
                    new RoadmapTaskDefinition { Id = "CLOUD-002", Title = "Create neutral cloud snapshot model", Description = "Represent the synthetic cloud snapshot without credentials.", DependsOn = ["CLOUD-001"] },
                    new RoadmapTaskDefinition { Id = "CLOUD-003", Title = "Implement synthetic observation provider", Description = "Collect a controlled local snapshot without external calls.", DependsOn = ["CLOUD-002"] },
                    new RoadmapTaskDefinition { Id = "CLOUD-004", Title = "Define MCP read-only boundary", Description = "Use only the approved conceptual read-only tool set.", DependsOn = ["CLOUD-003"] },
                    new RoadmapTaskDefinition { Id = "CLOUD-005", Title = "Implement synthetic MCP client", Description = "Record deterministic synthetic tool evidence.", DependsOn = ["CLOUD-004"] },
                    new RoadmapTaskDefinition { Id = "CLOUD-006", Title = "Compare IaC model with cloud snapshot", Description = "Evaluate declared versus observed state.", DependsOn = ["CLOUD-005"] },
                    new RoadmapTaskDefinition { Id = "CLOUD-007", Title = "Validate safe and divergent snapshots", Description = "Evaluate compatible and correctable synthetic snapshots.", DependsOn = ["CLOUD-006"] },
                    new RoadmapTaskDefinition { Id = "CLOUD-008", Title = "Validate critical and unknown snapshots", Description = "Preserve human-required and unknown outcomes.", DependsOn = ["CLOUD-007"] },
                    new RoadmapTaskDefinition { Id = "CLOUD-009", Title = "Integrate with EngineHost", Description = "Execute observation through the existing EngineHost.", DependsOn = ["CLOUD-008"] },
                    new RoadmapTaskDefinition { Id = "CLOUD-010", Title = "Persist observation evidence", Description = "Persist cloud-observation.json under the Sentinel run directory.", DependsOn = ["CLOUD-009"] },
                    new RoadmapTaskDefinition { Id = "CLOUD-011", Title = "Validate approval and checkpoint blocking", Description = "Require approval and preserve local checkpoint boundaries.", DependsOn = ["CLOUD-010"] }
                ]
            });
        }

        if (string.Equals(milestoneId, "cloud-provider-safety-boundary", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new RoadmapMilestoneDefinition
            {
                Id = "cloud-provider-safety-boundary",
                Title = "InfraSentinel cloud provider safety boundary",
                Tasks =
                [
                    new RoadmapTaskDefinition { Id = "CLOUD-BOUNDARY-001", Title = "Define provider operation contract", Description = "Define the local provider operation and scope contract." },
                    new RoadmapTaskDefinition { Id = "CLOUD-BOUNDARY-002", Title = "Validate read-only allowlist", Description = "Evaluate explicitly allowed read-only operations.", DependsOn = ["CLOUD-BOUNDARY-001"] },
                    new RoadmapTaskDefinition { Id = "CLOUD-BOUNDARY-003", Title = "Reject mutating and unknown operations", Description = "Reject mutation and unknown operation names by default.", DependsOn = ["CLOUD-BOUNDARY-002"] },
                    new RoadmapTaskDefinition { Id = "CLOUD-BOUNDARY-004", Title = "Validate credential and scope safety", Description = "Validate credential references and account and region scope.", DependsOn = ["CLOUD-BOUNDARY-003"] },
                    new RoadmapTaskDefinition { Id = "CLOUD-BOUNDARY-005", Title = "Produce deterministic safety artifact", Description = "Persist cloud-provider-safety-boundary.json without secrets.", DependsOn = ["CLOUD-BOUNDARY-004"] },
                    new RoadmapTaskDefinition { Id = "CLOUD-BOUNDARY-006", Title = "Execute through EngineHost and checkpoint locally", Description = "Execute the offline provider through the EngineHost approval and checkpoint workflow.", DependsOn = ["CLOUD-BOUNDARY-005"] }
                ]
            });
        }

        if (string.Equals(milestoneId, "cloud-security-findings-gate", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new RoadmapMilestoneDefinition
            {
                Id = "cloud-security-findings-gate",
                Title = "InfraSentinel cloud security findings gate",
                Tasks =
                [
                    new RoadmapTaskDefinition { Id = "FINDINGS-001", Title = "Load normalized AWS snapshot", Description = "Load the sanitized neutral M18 cloud snapshot." },
                    new RoadmapTaskDefinition { Id = "FINDINGS-002", Title = "Evaluate security rules", Description = "Evaluate public exposure, encryption, ownership, classification, identity, and region rules.", DependsOn = ["FINDINGS-001"] },
                    new RoadmapTaskDefinition { Id = "FINDINGS-003", Title = "Evaluate resilience rules", Description = "Evaluate backup, recovery, redundancy, dependency, retention, and availability evidence.", DependsOn = ["FINDINGS-002"] },
                    new RoadmapTaskDefinition { Id = "FINDINGS-004", Title = "Evaluate observability rules", Description = "Evaluate logging, metrics, alarms, traceability, and operational evidence.", DependsOn = ["FINDINGS-003"] },
                    new RoadmapTaskDefinition { Id = "FINDINGS-005", Title = "Evaluate cost and governance rules", Description = "Evaluate potentially paid services, tags, owners, purposes, and region policy.", DependsOn = ["FINDINGS-004"] },
                    new RoadmapTaskDefinition { Id = "FINDINGS-006", Title = "Aggregate deterministic findings", Description = "Sort rule evaluations and findings into a stable result.", DependsOn = ["FINDINGS-005"] },
                    new RoadmapTaskDefinition { Id = "FINDINGS-007", Title = "Execute independent review", Description = "Use the existing EngineHost review boundary.", DependsOn = ["FINDINGS-006"] },
                    new RoadmapTaskDefinition { Id = "FINDINGS-008", Title = "Apply human approval boundary", Description = "Require explicit approval for critical, high, unknown, or blocked outcomes.", DependsOn = ["FINDINGS-007"] },
                    new RoadmapTaskDefinition { Id = "FINDINGS-009", Title = "Persist evidence artifact", Description = "Write cloud-findings-gate.json without secrets or payloads.", DependsOn = ["FINDINGS-008"] },
                    new RoadmapTaskDefinition { Id = "FINDINGS-010", Title = "Validate semantic local checkpoint", Description = "Use the existing local checkpoint coordinator only after approval.", DependsOn = ["FINDINGS-009"] }
                ]
            });
        }

        if (string.Equals(milestoneId, "real-aws-read-only-observation", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new RoadmapMilestoneDefinition
            {
                Id = "real-aws-read-only-observation",
                Title = "InfraSentinel real AWS read-only observation",
                Tasks =
                [
                    new RoadmapTaskDefinition { Id = "AWS-001", Title = "Validate personal AWS identity and scope", Description = "Validate the explicit personal profile, account, identity, and authorized regions." },
                    new RoadmapTaskDefinition { Id = "AWS-002", Title = "Execute read-only provider observation", Description = "Observe only the AWS SDK read-only allowlist with bounded timeout and retry.", DependsOn = ["AWS-001"] },
                    new RoadmapTaskDefinition { Id = "AWS-003", Title = "Normalize cloud resources into neutral snapshot", Description = "Convert sanitized provider responses into domain-neutral resources.", DependsOn = ["AWS-002"] },
                    new RoadmapTaskDefinition { Id = "AWS-004", Title = "Compare IaC expectations with AWS snapshot", Description = "Reuse the deterministic IaC/cloud comparator.", DependsOn = ["AWS-003"] },
                    new RoadmapTaskDefinition { Id = "AWS-005", Title = "Produce deterministic evidence artifact", Description = "Persist sanitized evidence under .ai-runs-infrasentinel/.", DependsOn = ["AWS-004"] },
                    new RoadmapTaskDefinition { Id = "AWS-006", Title = "Execute review and approval boundary", Description = "Use EngineHost review and explicit human approval.", DependsOn = ["AWS-005"] },
                    new RoadmapTaskDefinition { Id = "AWS-007", Title = "Validate semantic local checkpoint", Description = "Allow the existing local checkpoint only after approval.", DependsOn = ["AWS-006"] }
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
