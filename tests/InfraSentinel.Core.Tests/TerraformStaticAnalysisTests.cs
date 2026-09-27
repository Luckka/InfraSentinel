using InfraSentinel.Core;
using InfraSentinel.Core.Adapters;
using InfraSentinel.Core.IaC;
using IAEngine.Core.Git;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class TerraformStaticAnalysisTests
{
    [Fact]
    public async Task TerraformAdapterIsSelectedAndSafeFixturePasses()
    {
        using var workspace = FixtureWorkspace(SafeTerraform);
        var selector = new ProjectAdapterSelector([new DotNetProjectAdapter(), new NodeProjectAdapter(), new TerraformProjectAdapter()]);
        var adapter = await selector.SelectAsync(Request(workspace.Root));
        var analysis = await adapter!.AnalyzeAsync(Request(workspace.Root));
        var evaluation = new TerraformStaticAnalysisValidator().Evaluate("safe", (NeutralIaCModel)analysis.DomainModel!);

        Assert.Equal("terraform", adapter.AdapterId);
        Assert.Equal(ProjectAdapterStatus.Analyzed, analysis.Status);
        Assert.True(evaluation.Approved, evaluation.Justification);
        Assert.Contains(evaluation.RulesEvaluated, rule => rule == "IAC-PLAINTEXT-SECRET");
    }

    [Fact]
    public async Task TerraformAnalysisIsDeterministic()
    {
        using var first = FixtureWorkspace(SafeTerraform);
        using var second = FixtureWorkspace(SafeTerraform);
        var adapter = new TerraformProjectAdapter();
        var left = await adapter.AnalyzeAsync(Request(first.Root));
        var right = await adapter.AnalyzeAsync(Request(second.Root));
        var leftEvaluation = new TerraformStaticAnalysisValidator().Evaluate("same", (NeutralIaCModel)left.DomainModel!);
        var rightEvaluation = new TerraformStaticAnalysisValidator().Evaluate("same", (NeutralIaCModel)right.DomainModel!);

        Assert.Equal(leftEvaluation.RulesEvaluated, rightEvaluation.RulesEvaluated);
        Assert.Equal(leftEvaluation.RulesPassed, rightEvaluation.RulesPassed);
        Assert.Equal(leftEvaluation.Findings, rightEvaluation.Findings);
        Assert.Equal(leftEvaluation.Status, rightEvaluation.Status);
    }

    [Fact]
    public async Task CriticalFixtureRequiresHumanApproval()
    {
        using var workspace = FixtureWorkspace(CriticalTerraform);
        var analysis = await new TerraformProjectAdapter().AnalyzeAsync(Request(workspace.Root));
        var evaluation = new TerraformStaticAnalysisValidator().Evaluate("critical", (NeutralIaCModel)analysis.DomainModel!);

        Assert.Equal(IaCAnalysisStatus.HumanRequired, evaluation.Status);
        Assert.Contains(evaluation.Findings, finding => finding.RuleId == "IAC-PUBLIC-SENSITIVE");
        Assert.Contains(evaluation.Findings, finding => finding.RuleId == "IAC-SENSITIVE-PORT");
        Assert.Contains(evaluation.Findings, finding => finding.RuleId == "IAC-IAM-LEAST-PRIVILEGE");
        Assert.Contains(evaluation.Findings, finding => finding.RuleId == "IAC-PLAINTEXT-SECRET");
        Assert.All(evaluation.Findings.Where(finding => finding.Severity == IaCFindingSeverity.Critical), finding => Assert.True(finding.RequiresHumanApproval));
    }

    [Fact]
    public async Task CriticalRunnerPersistsHumanRequiredWithoutCheckpoint()
    {
        using var workspace = FixtureWorkspace(CriticalTerraform);
        var configuration = new IAEngineConsumerConfiguration(workspace.Root);
        var evidence = new TerraformAnalysisEvidence("critical-runner", "infra-sentinel", "terraform-static-analysis-validation", "critical", "1", "feature/m15-terraform-static-analysis");
        var writer = configuration.CreateTerraformArtifactWriter();
        var result = await new TerraformValidationRunner(configuration.CreateProjectAdapterSelector(), Request(workspace.Root), "critical-runner", new TerraformStaticAnalysisValidator(), evidence, writer).RunAsync();
        var artifact = evidence.BuildArtifact();

        Assert.False(result.Single().Passed);
        Assert.Equal("HumanRequired", artifact.Status);
        Assert.Null(artifact.CommitSha);
        Assert.False(artifact.PushPerformed);
        Assert.False(artifact.MergePerformed);
    }

    [Fact]
    public async Task InsecureFixtureCanBeRemediatedByReanalysis()
    {
        using var workspace = FixtureWorkspace(InsecureTerraform);
        var adapter = new TerraformProjectAdapter();
        var first = await adapter.AnalyzeAsync(Request(workspace.Root));
        var evidence = new TerraformAnalysisEvidence("remediation", "infra-sentinel", "terraform-static-analysis-validation", "insecure", "1", "feature/m15-terraform-static-analysis");
        var validator = new TerraformStaticAnalysisValidator();
        var initial = validator.Evaluate("remediation", (NeutralIaCModel)first.DomainModel!);
        evidence.RecordAnalysis(first);
        evidence.RecordEvaluation(initial);
        Assert.NotEmpty(initial.Findings);

        File.WriteAllText(Path.Combine(workspace.Root, "main.tf"), SafeTerraform);
        var corrected = await adapter.AnalyzeAsync(Request(workspace.Root));
        var final = validator.Evaluate("remediation", (NeutralIaCModel)corrected.DomainModel!);
        evidence.RecordAnalysis(corrected);
        evidence.RecordEvaluation(final, retry: true, remediated: true);

        Assert.True(final.Approved, final.Justification);
        Assert.Equal(1, evidence.BuildArtifact().Retries);
        Assert.Equal(1, evidence.BuildArtifact().RemediationCount);
    }

    [Fact]
    public async Task InvalidTerraformFailsDeterministically()
    {
        using var workspace = FixtureWorkspace("resource \"aws_s3_bucket\" \"broken\" {\n  encryption_enabled = true\n");
        var analysis = await new TerraformProjectAdapter().AnalyzeAsync(Request(workspace.Root));

        Assert.Equal(ProjectAdapterStatus.Failed, analysis.Status);
        Assert.Contains(analysis.Findings, finding => finding.RuleId == "IAC-READ-FAILED");
        Assert.DoesNotContain(analysis.Evidence, item => item.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BoundariesProtectTraversalSymlinksAndLimits()
    {
        Assert.False(ProjectAdapterPathPolicy.IsSafeRelativePath("../secret.tf"));
        Assert.False(ProjectAdapterPathPolicy.IsSafeRelativePath("/tmp/secret.tf"));
        using var workspace = FixtureWorkspace(new string('x', 300));
        var result = await new TerraformProjectAdapter().AnalyzeAsync(Request(workspace.Root, new ProjectAdapterOptions(MaxFileBytes: 32)));
        Assert.Equal(ProjectAdapterStatus.Failed, result.Status);
    }

    [Fact]
    public async Task EngineHostRunsTerraformMilestoneAndPersistsApprovedArtifact()
    {
        using var workspace = FixtureWorkspace(SafeTerraform);
        var configuration = new IAEngineConsumerConfiguration(workspace.Root);
        var evidence = new TerraformAnalysisEvidence("m15-execution", "infra-sentinel", "terraform-static-analysis-validation", "safe", "1", "feature/m15-terraform-static-analysis");
        var writer = configuration.CreateTerraformArtifactWriter();
        var validation = new TerraformValidationRunner(configuration.CreateProjectAdapterSelector(), Request(workspace.Root), "m15-execution", new TerraformStaticAnalysisValidator(), evidence, writer);
        var coordinator = new RecordingCoordinator();
        var host = EngineHost.Create(new EngineHostContext
        {
            ProjectId = IAEngineConsumerConfiguration.ProjectId,
            WorkspaceRoot = workspace.Root,
            Options = configuration.CreateOptions(),
            Composition = configuration.Composition,
            Components = new("local-router", "local-implementation", "local-review", "validation"),
            RegisterComponents = builder => builder
                .RegisterProvider<ITaskRouter>("local-router", () => new LocalRouter())
                .RegisterProvider<IImplementationAgent>("local-implementation", () => new LocalImplementation())
                .RegisterProvider<IReviewAgent>("local-review", () => new TerraformReviewAgent(new LocalReview(), evidence))
                .RegisterValidator<IValidationRunner>("local-validator", () => validation)
                .RegisterCapability<IValidationRunner>("validation", () => validation)
                .RegisterPolicy("local-policy", () => new LocalPolicy()),
            RunStore = new RunStore(workspace.Root, configuration.RunsDirectory),
            Git = new LocalGit(workspace.Root),
            MilestoneSource = new InfraSentinelMilestoneSource(),
            MilestoneStateDirectory = configuration.StateDirectory,
            CheckpointCoordinator = new TerraformCheckpointCoordinator(coordinator, evidence, writer),
            CheckpointRequestSource = new TerraformCheckpointRequestSource(configuration.CreateCheckpointRequestSource("feature/m15-terraform-static-analysis", ["main.tf"], "test: validate terraform static analysis"), evidence)
        });

        var awaiting = await host.RunMilestoneAsync("terraform-static-analysis-validation");
        Assert.Equal(OnlineOs.AiOrchestrator.Roadmap.MilestoneRuntimeStatus.CompleteAwaitingApproval, awaiting.Status);
        Assert.Empty(coordinator.Commits);
        var approved = await host.ApproveMilestoneAsync("terraform-static-analysis-validation");

        Assert.Equal(OnlineOs.AiOrchestrator.Roadmap.MilestoneRuntimeStatus.Approved, approved.Status);
        Assert.Single(coordinator.Commits);
        Assert.True(File.Exists(configuration.TerraformStaticAnalysisArtifactPath));
        var artifact = await File.ReadAllTextAsync(configuration.TerraformStaticAnalysisArtifactPath);
        Assert.Contains("terraform-static-analysis-validation", artifact, StringComparison.Ordinal);
        Assert.Contains("m15-sha", artifact, StringComparison.Ordinal);
        Assert.Contains("\"pushPerformed\": false", artifact, StringComparison.Ordinal);
        Assert.Contains("\"mergePerformed\": false", artifact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedWorkspaceProducesBlockedResultWithoutCheckpoint()
    {
        using var workspace = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(workspace.Root, "README.md"), "README only");
        var configuration = new IAEngineConsumerConfiguration(workspace.Root);
        var evidence = new TerraformAnalysisEvidence("unsupported", "infra-sentinel", "terraform-static-analysis-validation", "unsupported", "1", "feature/m15-terraform-static-analysis");
        var writer = configuration.CreateTerraformArtifactWriter();
        var results = await new TerraformValidationRunner(configuration.CreateProjectAdapterSelector(), Request(workspace.Root), "unsupported", new TerraformStaticAnalysisValidator(), evidence, writer).RunAsync();
        var artifact = evidence.BuildArtifact();

        Assert.False(results.Single().Passed);
        Assert.Equal("Blocked", artifact.Status);
        Assert.Null(artifact.CommitSha);
        Assert.False(artifact.PushPerformed);
        Assert.False(artifact.MergePerformed);
    }

    private static ProjectAdapterRequest Request(string root, ProjectAdapterOptions? options = null) => new("terraform-fixture", root, options ?? new());
    private static TemporaryWorkspace FixtureWorkspace(string text) { var workspace = new TemporaryWorkspace(); File.WriteAllText(Path.Combine(workspace.Root, "main.tf"), text); return workspace; }

    private const string SafeTerraform = """
        resource "aws_s3_bucket" "safe" {
          critical = true
          encryption_enabled = true
          logging_enabled = true
          backup_enabled = true
          owner = "platform"
          environment = "production"
        }
        resource "aws_security_group" "safe" {
          critical = true
          encryption_enabled = true
          logging_enabled = true
          backup_enabled = true
          owner = "platform"
          environment = "production"
          from_port = 443
          to_port = 443
          cidr_blocks = ["10.0.0.0/8"]
        }
        resource "aws_iam_policy" "safe" {
          actions = ["read"]
          owner = "platform"
          environment = "production"
        }
        """;

    private const string InsecureTerraform = """
        resource "aws_s3_bucket" "insecure" {
          critical = true
          public = true
          logging_enabled = false
          backup_enabled = false
        }
        """;

    private const string CriticalTerraform = """
        resource "aws_s3_bucket" "critical" {
          critical = true
          public = true
          encryption_enabled = false
          secret = "plaintext-secret"
          owner = "platform"
          environment = "production"
        }
        resource "aws_security_group" "admin" {
          critical = true
          encryption_enabled = true
          logging_enabled = true
          backup_enabled = true
          owner = "platform"
          environment = "production"
          from_port = 22
          to_port = 22
          cidr_blocks = ["0.0.0.0/0"]
        }
        resource "aws_iam_policy" "admin" {
          actions = ["*"]
          owner = "platform"
          environment = "production"
        }
        """;

    private sealed class LocalRouter : ITaskRouter
    {
        public Task<RoutingResult> RouteAsync(DevelopmentTask task, CancellationToken cancellationToken = default) => Task.FromResult(new RoutingResult("synthetic", ["local"], "low", "low", [], [], "local", "local", true));
        public Task<(bool Reachable, bool ModelAvailable, string Detail)> CheckHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult((true, true, "local"));
    }
    private sealed class LocalImplementation : IImplementationAgent
    {
        public Task<ImplementationResult> ImplementAsync(DevelopmentTask task, RoutingResult route, EngineeringProfile engineering, CancellationToken cancellationToken = default) => Task.FromResult(new ImplementationResult(true, "local", []));
        public Task<ImplementationResult> RemediateAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, ReviewResult? review, CancellationToken cancellationToken = default, int contextReductionAttempt = 0) => Task.FromResult(new ImplementationResult(true, "local", []));
    }
    private sealed class LocalReview : IReviewAgent
    {
        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default) => Task.FromResult(new ReviewResult(ReviewDecision.Pass, [], "local review"));
    }
    private sealed record LocalPolicy(string Name = "local-policy") : IProjectPolicyComponent;
    private sealed class LocalGit(string root) : IGitService
    {
        public Task<string> GetRootAsync(CancellationToken cancellationToken = default) => Task.FromResult(root);
        public Task<string> GetBranchAsync(CancellationToken cancellationToken = default) => Task.FromResult("feature/m15-terraform-static-analysis");
        public Task<string> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetDiffAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetCommitAsync(CancellationToken cancellationToken = default) => Task.FromResult("local");
        public Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken cancellationToken = default) => Task.FromResult((true, "local"));
    }
    private sealed class RecordingCoordinator : IGitCheckpointCoordinator
    {
        public List<GitCheckpointRequest> Commits { get; } = [];
        public Task<GitCheckpointDecision> EvaluateAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default) => Task.FromResult(GitCheckpointPolicy.Evaluate(request));
        public Task<GitCheckpointResult> CommitAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default)
        { Commits.Add(request); return Task.FromResult(new GitCheckpointResult(true, "m15-sha", request.ProposedCommitMessage, request.CurrentBranch, request.ExpectedFiles, DateTimeOffset.UtcNow, null, true, false, false)); }
    }
    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("infrasentinel-m15-").FullName;
        public void Dispose() => Directory.Delete(Root, true);
    }
}
