using System.Text.Json;
using IAEngine.Core.Git;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;

namespace InfraSentinel.Core.Integration;

public sealed record InfraSentinelCheckpointArtifact(
    string TaskId,
    string? MilestoneId,
    bool ValidationPassed,
    bool ReviewPassed,
    GitCheckpointDecision Decision,
    GitCheckpointResult Result,
    bool HumanApprovalProvided,
    bool PushPerformed,
    bool MergePerformed,
    DateTimeOffset Timestamp);

/// <summary>
/// InfraSentinel's concrete implementation of IAEngine's generic checkpoint
/// contract. It reads Git state through IGitService and uses the generic process
/// runner only for the local add/commit operations permitted by the contract.
/// </summary>
public sealed class InfraSentinelGitCheckpointCoordinator(
    IGitService git,
    IProcessRunner processes,
    string artifactRoot,
    Func<string, bool>? branchAuthorizer = null)
    : IGitCheckpointCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private Func<string, bool> BranchAuthorizer { get; } = branchAuthorizer ?? (branch => branch.StartsWith("feature/", StringComparison.Ordinal));

    public async Task<GitCheckpointDecision> EvaluateAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var live = await ReadLiveRequestAsync(request, cancellationToken);
        var decision = GitCheckpointPolicy.Evaluate(live);
        await PersistAsync(live, decision, GitCheckpointResult.Blocked(decision.Reason, live), cancellationToken);
        return decision;
    }

    public async Task<GitCheckpointResult> CommitAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var live = await ReadLiveRequestAsync(request, cancellationToken);
        var decision = GitCheckpointPolicy.Evaluate(live);
        if (decision.Status != GitCheckpointDecisionStatus.Allowed || !decision.CommitAllowed)
        {
            var blocked = GitCheckpointResult.Blocked(decision.Reason, live);
            await PersistAsync(live, decision, blocked, cancellationToken);
            return blocked;
        }

        var safeFiles = live.ChangedFiles.Select(file => ValidateRelativeFile(live.WorkspaceRoot, file)).ToArray();
        var add = await processes.RunAsync(new ProcessSpec("git", ["add", "--", .. safeFiles], live.WorkspaceRoot, Timeout: TimeSpan.FromSeconds(30)), cancellationToken);
        if (!add.Succeeded)
            return await PersistFailureAsync(live, decision, $"git add failed: {add.StandardError.Trim()}", cancellationToken);

        var commit = await processes.RunAsync(new ProcessSpec("git", ["commit", "-m", live.ProposedCommitMessage], live.WorkspaceRoot, Timeout: TimeSpan.FromSeconds(30)), cancellationToken);
        if (!commit.Succeeded)
            return await PersistFailureAsync(live, decision, $"git commit failed: {commit.StandardError.Trim()}", cancellationToken);

        var sha = await git.GetCommitAsync(cancellationToken);
        var status = await git.GetStatusAsync(cancellationToken);
        var result = new GitCheckpointResult(
            true,
            sha,
            live.ProposedCommitMessage,
            live.CurrentBranch,
            safeFiles,
            DateTimeOffset.UtcNow,
            null,
            !string.IsNullOrWhiteSpace(status),
            false,
            false);
        await PersistAsync(live, decision, result, cancellationToken);
        return result;
    }

    private async Task<GitCheckpointRequest> ReadLiveRequestAsync(GitCheckpointRequest request, CancellationToken cancellationToken)
    {
        var root = await git.GetRootAsync(cancellationToken);
        var branch = await git.GetBranchAsync(cancellationToken);
        var status = await git.GetStatusAsync(cancellationToken);
        var diff = await git.GetDiffAsync(cancellationToken);
        var diffCheck = await processes.RunAsync(new ProcessSpec("git", ["diff", "--check", "--", "."], root, Timeout: TimeSpan.FromSeconds(30)), cancellationToken);
        var changedFiles = ParseChangedFiles(status);
        var workspaceExpected = string.Equals(
            await ResolvePathAsync(root, cancellationToken),
            await ResolvePathAsync(request.WorkspaceRoot, cancellationToken),
            StringComparison.Ordinal);
        var branchAuthorized = request.BranchAuthorized
            && string.Equals(branch, request.CurrentBranch, StringComparison.Ordinal)
            && BranchAuthorizer(branch);

        return request with
        {
            WorkspaceRoot = root,
            CurrentBranch = branch,
            ChangedFiles = changedFiles,
            HasChanges = changedFiles.Count > 0,
            DiffCheckPassed = diffCheck.Succeeded,
            WorkspaceExpected = workspaceExpected,
            BranchAuthorized = branchAuthorized,
            HasConflicts = status.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(IsConflict),
            SecretsDetected = request.SecretsDetected || ContainsSecretPattern(diff)
        };
    }

    private async Task<GitCheckpointResult> PersistFailureAsync(
        GitCheckpointRequest request,
        GitCheckpointDecision decision,
        string reason,
        CancellationToken cancellationToken)
    {
        var result = GitCheckpointResult.Blocked(reason, request);
        await PersistAsync(request, decision, result, cancellationToken);
        return result;
    }

    private async Task PersistAsync(
        GitCheckpointRequest request,
        GitCheckpointDecision decision,
        GitCheckpointResult result,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(request.WorkspaceRoot, artifactRoot, "checkpoint");
        Directory.CreateDirectory(directory);
        var artifact = new InfraSentinelCheckpointArtifact(
            request.TaskId,
            request.MilestoneId,
            request.ValidationPassed,
            request.ReviewPassed,
            decision,
            result,
            request.HumanApprovalProvided,
            false,
            false,
            result.Timestamp);
        await File.WriteAllTextAsync(Path.Combine(directory, Sanitize(request.TaskId) + ".json"), JsonSerializer.Serialize(artifact, JsonOptions), cancellationToken);
    }

    private static IReadOnlyList<string> ParseChangedFiles(string status)
        => status.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Length > 2)
            .Select(line => line.Length > 2 && line[2] == ' ' ? line[3..].Trim() : line[2..].Trim())
            .Select(path => path.Contains(" -> ", StringComparison.Ordinal) ? path[(path.LastIndexOf(" -> ", StringComparison.Ordinal) + 4)..] : path)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsConflict(string line)
        => line.Length >= 2 && line[..2] is "UU" or "AA" or "DD" or "AU" or "UD" or "UA" or "DU";

    private static bool ContainsSecretPattern(string diff)
        => diff.Contains("BEGIN PRIVATE KEY", StringComparison.OrdinalIgnoreCase)
            || diff.Contains("aws_secret_access_key", StringComparison.OrdinalIgnoreCase)
            || diff.Contains("password=", StringComparison.OrdinalIgnoreCase)
            || diff.Contains("token=", StringComparison.OrdinalIgnoreCase);

    private static string ValidateRelativeFile(string root, string file)
    {
        if (Path.IsPathRooted(file)) throw new InvalidOperationException($"Checkpoint file must be relative: {file}");
        var full = Path.GetFullPath(Path.Combine(root, file));
        var boundary = new WorkspaceBoundary(root);
        return boundary.Contains(full) ? file : throw new InvalidOperationException($"Checkpoint file escapes the workspace: {file}");
    }

    private static string Sanitize(string value)
        => string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));

    private async Task<string> ResolvePathAsync(string path, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows()) return Path.GetFullPath(path);
        var result = await processes.RunAsync(new ProcessSpec("realpath", [path], Directory.GetCurrentDirectory(), Timeout: TimeSpan.FromSeconds(10)), cancellationToken);
        return result.Succeeded ? result.StandardOutput.Trim() : Path.GetFullPath(path);
    }
}
