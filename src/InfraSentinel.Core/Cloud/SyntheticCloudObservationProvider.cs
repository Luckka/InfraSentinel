namespace InfraSentinel.Core.Cloud;

public sealed class SyntheticCloudObservationProvider(CloudInfrastructureSnapshot fixture, bool fail = false, bool timeout = false) : ICloudObservationProvider
{
    public string ProviderId => "synthetic-cloud";
    public CloudObservationSourceType SourceType => CloudObservationSourceType.Synthetic;

    public async Task<CloudObservationResult> GetSnapshotAsync(CloudObservationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(request.Timeout);
        if (timeout)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, timeoutSource.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(CloudCollectionStatus.TimedOut, null, ["Synthetic provider timed out."], "Observation timed out.", true); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (fail) return new(CloudCollectionStatus.Failed, null, ["Synthetic provider failure."], "Observation provider failed.", true);
        if (fixture.SourceType != CloudObservationSourceType.Synthetic) return new(CloudCollectionStatus.Failed, null, ["Fixture source must be Synthetic."], "Provider rejected a non-synthetic fixture.", true);
        if (fixture.Resources.Count > request.MaxResources) return new(CloudCollectionStatus.Failed, null, ["Resource limit exceeded."], "Observation exceeded the configured resource limit.", true);
        if (fixture.Resources.Any(resource => ContainsSecret(resource.Evidence) || ContainsSecret(resource.Policies)))
            return new(CloudCollectionStatus.Failed, null, ["Potential secret content was rejected."], "Synthetic snapshot contained a prohibited secret marker.", true);
        var scoped = request.RequestedResources.Count == 0
            ? fixture.Resources
            : fixture.Resources.Where(resource => request.RequestedResources.Contains(resource.ResourceId, StringComparer.Ordinal)).ToArray();
        var snapshot = fixture with { Resources = scoped.OrderBy(resource => resource.ResourceId, StringComparer.Ordinal).ToArray(), CollectedAt = DateTimeOffset.UnixEpoch };
        return new(CloudCollectionStatus.Collected, snapshot, snapshot.Limitations, "Synthetic snapshot collected.", true);
    }

    private static bool ContainsSecret(IEnumerable<string> values)
        => values.Any(value => value.Contains("secret", StringComparison.OrdinalIgnoreCase) || value.Contains("token", StringComparison.OrdinalIgnoreCase) || value.Contains("password", StringComparison.OrdinalIgnoreCase));
}
