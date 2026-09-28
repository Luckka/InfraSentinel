using System.Text.Json;
using System.Text.Json.Serialization;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Models;

namespace InfraSentinel.Core.Cloud;

public enum CloudProviderOperationCategory { List, Describe, Get, Mutation, Unknown }
public enum CloudProviderSafetyStatus { Allowed, Blocked, Disabled, HumanRequired, TimedOut }

public sealed record CloudCredentialReference(string Provider, string ReferenceName);

public sealed record CloudProviderScope(
    string AccountAlias,
    IReadOnlyList<string> AllowedAccounts,
    string Region,
    IReadOnlyList<string> AllowedRegions);

public sealed record CloudProviderObservationRequest(
    string ProjectId,
    string ProviderName,
    string OperationName,
    CloudProviderScope Scope,
    bool ReadOnlyMode,
    bool ProviderEnabled = false,
    bool DryRun = true,
    bool AllowGetOperations = false,
    CloudCredentialReference? CredentialReference = null,
    string? CredentialMaterial = null,
    TimeSpan? Timeout = null,
    int MaxAttempts = 1);

public sealed record CredentialSafetyResult(bool Safe, string Reason);

public sealed record CloudProviderSafetyDecision(
    CloudProviderSafetyStatus Status,
    CloudProviderOperationCategory OperationCategory,
    string Reason,
    IReadOnlyList<string> Evidence,
    CredentialSafetyResult CredentialSafety,
    bool RealCallAttempted,
    bool MutationAttempted,
    bool RequiresHumanApproval)
{
    public bool Allowed => Status == CloudProviderSafetyStatus.Allowed;
}

public sealed record CloudProviderObservationResult(
    CloudProviderSafetyDecision Decision,
    int Attempts,
    IReadOnlyList<string> Limitations);

public interface ICloudProviderSafetyBoundary
{
    Task<CloudProviderObservationResult> ObserveAsync(
        CloudProviderObservationRequest request,
        CancellationToken cancellationToken = default);
}

public static class CloudProviderSafetyPolicy
{
    private static readonly string[] MutationPrefixes = ["Create", "Put", "Update", "Delete", "Terminate", "Run", "Start", "Stop", "Modify", "Attach", "Detach", "Associate", "Disassociate"];

    public static CloudProviderSafetyDecision Evaluate(CloudProviderObservationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var credential = ValidateCredentials(request);
        if (!credential.Safe)
            return Blocked(CloudProviderSafetyStatus.Blocked, "Credential material was supplied directly to the domain; only references are accepted.", request, credential, "credential-material-rejected");
        if (string.IsNullOrWhiteSpace(request.ProjectId) || string.IsNullOrWhiteSpace(request.ProviderName) || string.IsNullOrWhiteSpace(request.OperationName))
            return Blocked(CloudProviderSafetyStatus.Blocked, "Provider request identity is incomplete.", request, credential, "request-identity-incomplete");
        if (!request.ReadOnlyMode)
            return Blocked(CloudProviderSafetyStatus.Blocked, "Read-only mode is not explicitly enabled.", request, credential, "read-only-disabled");
        if (!request.Scope.AllowedAccounts.Contains(request.Scope.AccountAlias, StringComparer.Ordinal))
            return Blocked(CloudProviderSafetyStatus.Blocked, "Account alias is outside the authorized observation scope.", request, credential, "account-out-of-scope");
        if (!request.Scope.AllowedRegions.Contains(request.Scope.Region, StringComparer.Ordinal))
            return Blocked(CloudProviderSafetyStatus.Blocked, "Region is outside the authorized observation scope.", request, credential, "region-out-of-scope");

        var category = Categorize(request.OperationName);
        if (category == CloudProviderOperationCategory.Mutation)
            return Blocked(CloudProviderSafetyStatus.Blocked, "Mutating provider operations are prohibited by the observation boundary.", request, credential, "mutation-denied", category, mutation: true);
        if (category == CloudProviderOperationCategory.Unknown)
            return Blocked(CloudProviderSafetyStatus.Blocked, "Unknown provider operations are denied by the allowlist.", request, credential, "operation-unknown", category);
        if (category == CloudProviderOperationCategory.Get && !request.AllowGetOperations)
            return Blocked(CloudProviderSafetyStatus.Blocked, "Get operations require explicit read-only authorization.", request, credential, "get-not-explicitly-authorized", category);
        if (!request.ProviderEnabled && !request.DryRun)
            return Blocked(CloudProviderSafetyStatus.Disabled, "The cloud provider is disabled; real observation was not attempted.", request, credential, "provider-disabled", category);
        if (request.ProviderEnabled && !request.DryRun)
            return new(CloudProviderSafetyStatus.HumanRequired, category, "Real provider observation requires explicit human authorization and a separate controlled rollout.", ["real-call-not-executed"], credential, false, false, true);
        return new(CloudProviderSafetyStatus.Allowed, category, "Offline read-only provider decision approved; no cloud call was executed.", ["offline", "read-only", "dry-run"], credential, false, false, false);
    }

    public static CloudProviderOperationCategory Categorize(string operationName)
    {
        if (string.IsNullOrWhiteSpace(operationName)) return CloudProviderOperationCategory.Unknown;
        if (MutationPrefixes.Any(prefix => operationName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return CloudProviderOperationCategory.Mutation;
        if (operationName.StartsWith("Describe", StringComparison.OrdinalIgnoreCase)) return CloudProviderOperationCategory.Describe;
        if (operationName.StartsWith("List", StringComparison.OrdinalIgnoreCase)) return CloudProviderOperationCategory.List;
        if (operationName.StartsWith("Get", StringComparison.OrdinalIgnoreCase)) return CloudProviderOperationCategory.Get;
        return CloudProviderOperationCategory.Unknown;
    }

    public static CredentialSafetyResult ValidateCredentials(CloudProviderObservationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.CredentialMaterial)) return new(false, "Credential material is not accepted by the domain.");
        if (request.CredentialReference is null) return new(true, "No credential content was supplied; offline observation needs no credentials.");
        if (string.IsNullOrWhiteSpace(request.CredentialReference.Provider) || string.IsNullOrWhiteSpace(request.CredentialReference.ReferenceName))
            return new(false, "Credential reference is incomplete.");
        return new(true, "Only a non-secret credential reference was supplied.");
    }

    private static CloudProviderSafetyDecision Blocked(CloudProviderSafetyStatus status, string reason, CloudProviderObservationRequest request, CredentialSafetyResult credential, string evidence, CloudProviderOperationCategory category = CloudProviderOperationCategory.Unknown, bool mutation = false)
        => new(status, category == CloudProviderOperationCategory.Unknown ? Categorize(request.OperationName) : category, reason, [evidence], credential, false, mutation, false);
}

public sealed class OfflineAwsReadOnlyProvider(bool providerEnabled = false, bool simulateTimeout = false) : ICloudProviderSafetyBoundary
{
    public string ProviderName => "aws-read-only-offline";

    public async Task<CloudProviderObservationResult> ObserveAsync(CloudProviderObservationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var effective = request with { ProviderName = ProviderName, ProviderEnabled = request.ProviderEnabled && providerEnabled };
        if (simulateTimeout)
        {
            var timeout = effective.Timeout ?? TimeSpan.FromMilliseconds(1);
            try { await Task.Delay(timeout, cancellationToken); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            if (!cancellationToken.IsCancellationRequested)
            {
                var decision = new CloudProviderSafetyDecision(CloudProviderSafetyStatus.TimedOut, CloudProviderSafetyPolicy.Categorize(request.OperationName), "Offline provider simulation timed out; no call was executed.", ["timeout", "real-call-not-executed"], CloudProviderSafetyPolicy.ValidateCredentials(effective), false, false, true);
                return new(decision, 1, ["Provider timeout was simulated locally."]);
            }
        }
        var decisionResult = CloudProviderSafetyPolicy.Evaluate(effective);
        return new(decisionResult, 1, decisionResult.Allowed ? ["No AWS SDK or network call exists in this provider."] : [decisionResult.Reason]);
    }
}

public sealed record CloudProviderSafetyArtifact(
    string ExecutionId,
    string MilestoneId,
    string TaskId,
    string ProviderName,
    string RequestedOperation,
    CloudProviderOperationCategory OperationCategory,
    CloudProviderSafetyStatus Status,
    string AllowlistDecision,
    string AccountScopeDecision,
    string RegionScopeDecision,
    CredentialSafetyResult CredentialSafety,
    bool RealCallAttempted,
    bool MutationAttempted,
    int Attempts,
    IReadOnlyList<string> Limitations,
    bool HumanApprovalRequired,
    bool ApprovalProvided,
    bool CheckpointAllowed,
    string? CommitSha,
    bool PushPerformed,
    bool MergePerformed,
    DateTimeOffset Timestamp,
    string DeterministicResult);

public sealed class CloudProviderSafetyArtifactWriter(string artifactPath)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    public Task WriteAsync(CloudProviderSafetyArtifact artifact, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(artifactPath) ?? throw new ArgumentException("Artifact path must include a directory.", nameof(artifactPath));
        Directory.CreateDirectory(directory);
        return File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(artifact, Options), cancellationToken);
    }
}

public sealed class CloudProviderSafetyValidationRunner(
    ICloudProviderSafetyBoundary provider,
    CloudProviderObservationRequest request,
    string artifactPath,
    string executionId = "m17-execution",
    string milestoneId = "cloud-provider-safety-boundary",
    string taskId = "CLOUD-BOUNDARY-005") : IValidationRunner
{
    public async Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var maxAttempts = Math.Clamp(request.MaxAttempts, 1, 3);
        CloudProviderObservationResult result = await provider.ObserveAsync(request, cancellationToken);
        var attempts = 1;
        while (attempts < maxAttempts && result.Decision.Status is CloudProviderSafetyStatus.TimedOut or CloudProviderSafetyStatus.Blocked)
        {
            if (result.Decision.Status == CloudProviderSafetyStatus.Blocked) break;
            result = await provider.ObserveAsync(request, cancellationToken);
            attempts++;
        }
        var category = result.Decision.OperationCategory;
        var artifact = new CloudProviderSafetyArtifact(
            executionId, milestoneId, taskId, "aws-read-only-offline", request.OperationName, category, result.Decision.Status,
            result.Decision.Allowed ? "Allowed" : "Blocked", result.Decision.Evidence.Contains("account-out-of-scope") ? "Blocked" : "Allowed", result.Decision.Evidence.Contains("region-out-of-scope") ? "Blocked" : "Allowed",
            result.Decision.CredentialSafety, result.Decision.RealCallAttempted, result.Decision.MutationAttempted, attempts, result.Limitations,
            result.Decision.RequiresHumanApproval, false, false, null, false, false, DateTimeOffset.UnixEpoch,
            $"{result.Decision.Status}:{result.Decision.Reason}:attempts={attempts}");
        await new CloudProviderSafetyArtifactWriter(artifactPath).WriteAsync(artifact, cancellationToken);
        var passed = result.Decision.Allowed;
        return [new ValidationResult("CloudProviderSafety", true, new ProcessResult("infrasentinel-cloud-provider-safety", passed ? 0 : 1, result.Decision.Reason, "", TimeSpan.Zero), passed ? ValidationStatus.Pass : ValidationStatus.Fail, passed ? null : result.Decision.Reason)];
    }
}
