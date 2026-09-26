namespace InfraSentinel.Core.Observability;

public sealed class ObservabilityEvidenceWriter(string artifactPath)
{
    public async Task WriteAsync(ObservabilityEvidenceAggregator aggregator, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregator);
        var directory = Path.GetDirectoryName(artifactPath);
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Evidence artifact path must include a directory.", nameof(artifactPath));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(artifactPath, aggregator.SerializeDeterministically(), cancellationToken);
    }
}
