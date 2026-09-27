namespace InfraSentinel.Core.Cloud;

public sealed record McpToolCallEvidence(string ToolName, IReadOnlyDictionary<string, string> Arguments, bool Allowed, string Reason);

public interface IMcpReadOnlyToolClient
{
    IReadOnlyList<McpToolCallEvidence> Calls { get; }
    Task<IReadOnlyList<string>> ListResourcesAsync(IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken = default);
    Task<string> GetResourceConfigurationAsync(string resourceId, CancellationToken cancellationToken = default);
    Task<string> GetSecurityPostureAsync(string resourceId, CancellationToken cancellationToken = default);
}

public sealed class SyntheticMcpReadOnlyToolClient(IReadOnlyDictionary<string, string> responses) : IMcpReadOnlyToolClient
{
    private readonly List<McpToolCallEvidence> calls = [];
    public IReadOnlyList<McpToolCallEvidence> Calls => calls;

    public Task<IReadOnlyList<string>> ListResourcesAsync(IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record("list_resources", arguments);
        return Task.FromResult<IReadOnlyList<string>>(responses.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    public Task<string> GetResourceConfigurationAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record("get_resource_configuration", new Dictionary<string, string> { ["resourceId"] = resourceId });
        return Task.FromResult(responses.TryGetValue(resourceId, out var value) ? value : "Unknown");
    }

    public Task<string> GetSecurityPostureAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record("get_security_posture", new Dictionary<string, string> { ["resourceId"] = resourceId });
        return Task.FromResult(responses.TryGetValue(resourceId, out var value) ? value : "Unknown");
    }

    public Task<string> InvokeAsync(string toolName, IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (toolName is not ("list_resources" or "get_resource_configuration" or "get_security_posture"))
        {
            Record(toolName, arguments, false, "Tool is not part of the read-only allow-list.");
            throw new InvalidOperationException("Only approved read-only tools are allowed.");
        }
        Record(toolName, arguments);
        return Task.FromResult("synthetic");
    }

    private void Record(string name, IReadOnlyDictionary<string, string> arguments, bool allowed = true, string reason = "Allowed read-only tool.")
    {
        var safe = arguments.ToDictionary(item => item.Key, item => ContainsSecret(item.Value) ? "[REDACTED]" : item.Value, StringComparer.Ordinal);
        calls.Add(new(name, safe, allowed, reason));
    }

    private static bool ContainsSecret(string value)
        => value.Contains("secret", StringComparison.OrdinalIgnoreCase) || value.Contains("token", StringComparison.OrdinalIgnoreCase) || value.Contains("password", StringComparison.OrdinalIgnoreCase);
}
