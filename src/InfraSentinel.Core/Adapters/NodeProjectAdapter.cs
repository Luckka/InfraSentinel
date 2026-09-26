using System.Text.Json;

namespace InfraSentinel.Core.Adapters;

public sealed class NodeProjectAdapter : IProjectAdapter
{
    public string AdapterId => "node";
    public string AdapterVersion => "1.0";
    public string ProjectType => "node";

    public bool CanHandle(ProjectAdapterRequest request)
        => Directory.Exists(request.WorkspaceRoot) && File.Exists(Path.Combine(request.WorkspaceRoot, "package.json"));

    public async Task<ProjectAdapterAnalysis> AnalyzeAsync(ProjectAdapterRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Options.EffectiveTimeout);
        try
        {
            var reader = new ProjectAdapterFileReader(request);
            var ignored = new List<string>();
            var files = reader.FindFiles(["package.json", "*.config.json"], ignored);
            if (!files.Any(file => Path.GetFileName(file).Equals("package.json", StringComparison.OrdinalIgnoreCase)))
                return Failed("package.json was not found.", "NODE-MANIFEST-MISSING");
            var components = new List<ProjectComponent>();
            var dependencies = new List<ProjectDependency>();
            var findings = new List<ProjectAdapterFinding>();
            string? version = null;
            foreach (var file in files.Order(StringComparer.Ordinal))
            {
                timeout.Token.ThrowIfCancellationRequested();
                var logical = reader.ToLogicalPath(file);
                var text = reader.ReadText(file);
                if (ContainsSecret(text)) findings.Add(new("ADAPTER-SECRET-CONTENT", "Critical", "Potential secret content was detected and was not persisted.", [logical], true));
                using var json = JsonDocument.Parse(text);
                if (Path.GetFileName(file).Equals("package.json", StringComparison.OrdinalIgnoreCase))
                {
                    var root = json.RootElement;
                    var name = root.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "unnamed" : "unnamed";
                    version = root.TryGetProperty("version", out var versionValue) ? versionValue.GetString() : null;
                    components.Add(new(logical, "package", name));
                    AddDependencies(root, "dependencies", logical, dependencies);
                    AddDependencies(root, "devDependencies", logical, dependencies);
                }
                else components.Add(new(logical, "configuration", Path.GetFileName(file)));
            }
            var limitations = new[]
            {
                "The adapter reads package manifests only; it does not install dependencies, run scripts, or execute JavaScript.",
                "Framework, infrastructure, and runtime behavior are not inferred from package metadata alone."
            };
            var model = new NeutralProjectModel(request.ProjectId, ProjectType, version, "/",
                components.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
                dependencies.OrderBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.Version, StringComparer.Ordinal).ToArray(),
                [], [], [], [], [], [], [], [],
                files.Select(reader.ToLogicalPath).Order(StringComparer.Ordinal).ToArray(), ignored.Order(StringComparer.Ordinal).ToArray(), limitations);
            await Task.CompletedTask;
            return new(ProjectAdapterStatus.Analyzed, AdapterId, AdapterVersion, ProjectType, model,
                [.. model.AnalyzedFiles, "node-manifest-read"], limitations, findings,
                findings.Count == 0 ? "Supported Node manifest analyzed read-only." : "Potential secret content requires review.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("Adapter analysis timed out.", "ADAPTER-TIMEOUT");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            return Failed("Workspace could not be analyzed safely.", "ADAPTER-READ-FAILED", exception.Message);
        }
    }

    private static void AddDependencies(JsonElement root, string property, string source, List<ProjectDependency> output)
    {
        if (!root.TryGetProperty(property, out var dependencies) || dependencies.ValueKind != JsonValueKind.Object) return;
        foreach (var dependency in dependencies.EnumerateObject()) output.Add(new(dependency.Name, dependency.Value.GetString(), source));
    }

    private static bool ContainsSecret(string content)
        => content.Contains("BEGIN PRIVATE KEY", StringComparison.OrdinalIgnoreCase)
            || content.Contains("aws_secret_access_key", StringComparison.OrdinalIgnoreCase)
            || content.Contains("password=", StringComparison.OrdinalIgnoreCase)
            || content.Contains("token=", StringComparison.OrdinalIgnoreCase)
            || content.Contains("client_secret", StringComparison.OrdinalIgnoreCase);

    private ProjectAdapterAnalysis Failed(string reason, string ruleId, string? detail = null)
        => new(ProjectAdapterStatus.Failed, AdapterId, AdapterVersion, ProjectType, null, [], [reason],
            [new(ruleId, "High", reason, detail is null ? [] : [detail], true)], reason);
}
