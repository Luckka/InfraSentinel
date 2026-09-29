namespace InfraSentinel.Core.Cloud;

public sealed class AwsReadOnlyObservationProvider(
    AwsReadOnlyConfiguration configuration,
    IAwsReadOnlyClient client,
    IAwsIdentityVerifier identityVerifier,
    IAwsReadOnlyPolicy policy,
    ICloudSnapshotNormalizer normalizer) : ICloudObservationProvider
{
    public string ProviderId => "aws-read-only";
    public CloudObservationSourceType SourceType => CloudObservationSourceType.External;

    public async Task<CloudObservationResult> GetSnapshotAsync(CloudObservationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!policy.IsProfileAllowed(configuration.ProfileName))
            return Blocked("profile-out-of-scope", "The configured AWS profile is not authorized.");
        if (!configuration.LiveAwsEnabled || configuration.DryRun)
            return Blocked("live-aws-disabled", "Live AWS observation is disabled unless INFRA_SENTINEL_ENABLE_LIVE_AWS=true and dry-run is false.");
        if (string.IsNullOrWhiteSpace(configuration.AuthorizedAccountId))
            return Blocked("account-id-missing", "An authorized AWS account id is required.");
        if (!policy.IsRegionAllowed(request.LogicalRegion, configuration.AuthorizedRegions))
            return Blocked("region-out-of-scope", "The requested region is outside the authorized region allowlist.");
        if (policy.ContainsCredentialMaterial([configuration.ProfileName, configuration.AuthorizedAccountId, request.ProjectId, request.Environment]))
            return Blocked("credential-material-rejected", "Credential-like material was detected in observation inputs.");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(MinimumTimeout(request.Timeout, configuration.Timeout));
        try
        {
            var identity = await identityVerifier.VerifyAsync(configuration, timeoutSource.Token);
            if (!policy.IsAccountAllowed(identity.AccountId, configuration.AuthorizedAccountId))
                return Blocked("account-out-of-scope", "STS identity did not match the authorized account.");
            if (!identity.Arn.EndsWith("/infra-sentinel", StringComparison.Ordinal))
                return Blocked("identity-out-of-scope", "STS identity did not match the authorized infra-sentinel user.");

            var result = await client.ObserveAsync(configuration.ProfileName, configuration.AuthorizedRegions, configuration.EnabledServices, MinimumTimeout(request.Timeout, configuration.Timeout), Math.Clamp(configuration.MaxAttempts, 1, 3), timeoutSource.Token);
            var snapshot = normalizer.Normalize(identity, configuration, result);
            var metadata = new CloudObservationMetadata(
                $"aws-account-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity.AccountId))).ToLowerInvariant()[..12]}",
                configuration.AuthorizedRegions.Order(StringComparer.Ordinal).ToArray(),
                configuration.EnabledServices.Select(service => service.ToString()).Order(StringComparer.Ordinal).ToArray(),
                result.Operations.OrderBy(operation => operation.Region, StringComparer.Ordinal).ThenBy(operation => operation.Operation, StringComparer.Ordinal).Select(operation => new CloudObservedOperation(operation.Operation, operation.Region, operation.Outcome, operation.ResourceCount)).ToArray(),
                result.Failures.Order(StringComparer.Ordinal).ToArray(),
                result.Retries,
                result.TimedOut);
            var limitations = result.Limitations.Concat(result.Failures).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            return new(CloudCollectionStatus.Collected, snapshot, limitations, "AWS read-only snapshot collected.", false, metadata);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(CloudCollectionStatus.TimedOut, null, ["aws-observation-timeout"], "AWS read-only observation timed out.", false,
                new CloudObservationMetadata("unknown", configuration.AuthorizedRegions, [], [], ["aws-observation-timeout"], 0, true));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException exception) when (exception.Message == "sanitized-response-rejected")
        {
            return new(CloudCollectionStatus.Failed, null, ["sanitized-response-rejected"], "AWS response was rejected because it could not be sanitized.", false,
                new CloudObservationMetadata("unknown", configuration.AuthorizedRegions, [], [], ["sanitized-response-rejected"], 0, false));
        }
        catch (Exception exception)
        {
            var reason = exception.GetType().Name.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase)
                ? "aws-access-denied"
                : "aws-observation-failed";
            return new(CloudCollectionStatus.Failed, null, [reason], "AWS read-only observation failed with a sanitized error.", false,
                new CloudObservationMetadata("unknown", configuration.AuthorizedRegions, [], [], [reason], 0, false));
        }
    }

    private CloudObservationResult Blocked(string limitation, string reason)
        => new(CloudCollectionStatus.Failed, null, [limitation], reason, false,
            new CloudObservationMetadata("unknown", configuration.AuthorizedRegions, [], [], [limitation], 0, false));

    private static TimeSpan MinimumTimeout(TimeSpan requestTimeout, TimeSpan configuredTimeout)
        => requestTimeout <= TimeSpan.Zero ? configuredTimeout : requestTimeout <= configuredTimeout ? requestTimeout : configuredTimeout;
}

public sealed class AwsIdentityVerifier(IAwsReadOnlyClient client) : IAwsIdentityVerifier
{
    public async Task<AwsIdentityObservation> VerifyAsync(AwsReadOnlyConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(configuration.ProfileName, AwsReadOnlyConfiguration.RequiredProfile, StringComparison.Ordinal))
            throw new InvalidOperationException("AWS profile is outside the authorized scope.");
        return await client.GetIdentityAsync(configuration.ProfileName, cancellationToken);
    }
}
