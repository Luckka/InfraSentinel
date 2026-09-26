using System.Text.Json;
using System.Xml.Linq;
using System.Xml;

namespace InfraSentinel.Core.Adapters;

public sealed class DotNetProjectAdapter : IProjectAdapter
{
    public string AdapterId => "dotnet";
    public string AdapterVersion => "1.0";
    public string ProjectType => "dotnet";

    public bool CanHandle(ProjectAdapterRequest request)
        => Directory.Exists(request.WorkspaceRoot)
            && (File.Exists(Path.Combine(request.WorkspaceRoot, "global.json"))
                || Directory.EnumerateFiles(request.WorkspaceRoot, "*.sln", SearchOption.TopDirectoryOnly).Any()
                || Directory.EnumerateFiles(request.WorkspaceRoot, "*.csproj", SearchOption.AllDirectories).Any());

    public async Task<ProjectAdapterAnalysis> AnalyzeAsync(ProjectAdapterRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Options.EffectiveTimeout);
        try
        {
            return await AnalyzeCoreAsync(request, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("Adapter analysis timed out.", "ADAPTER-TIMEOUT");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or XmlException)
        {
            return Failed("Workspace could not be analyzed safely.", "ADAPTER-READ-FAILED", exception.Message);
        }
    }

    private async Task<ProjectAdapterAnalysis> AnalyzeCoreAsync(ProjectAdapterRequest request, CancellationToken cancellationToken)
    {
        var ignored = new List<string>();
        var reader = new ProjectAdapterFileReader(request);
        var files = reader.FindFiles(["*.sln", "*.csproj", "appsettings.example.json", "global.json"], ignored);
        if (files.Count == 0) return Failed("No supported .NET manifest was found.", "DOTNET-MANIFEST-MISSING");

        var components = new List<ProjectComponent>();
        var dependencies = new List<ProjectDependency>();
        var versions = new List<string>();
        var encrypted = new List<string>();
        var logging = new List<string>();
        var findings = new List<ProjectAdapterFinding>();
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var logical = reader.ToLogicalPath(file);
            var text = reader.ReadText(file);
            if (ContainsSecret(text))
                findings.Add(new("ADAPTER-SECRET-CONTENT", "Critical", "Potential secret content was detected and was not persisted.", [logical], true));
            if (file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
                var projectName = Path.GetFileNameWithoutExtension(file);
                components.Add(new(logical, "project", projectName));
                foreach (var package in document.Descendants().Where(node => node.Name.LocalName == "PackageReference"))
                    dependencies.Add(new(package.Attribute("Include")?.Value ?? "unknown", package.Attribute("Version")?.Value, logical));
                var target = document.Descendants().FirstOrDefault(node => node.Name.LocalName == "TargetFramework")?.Value;
                if (!string.IsNullOrWhiteSpace(target)) versions.Add(target);
            }
            else if (file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                components.Add(new(logical, "solution", Path.GetFileNameWithoutExtension(file)));
            else if (file.EndsWith("appsettings.example.json", StringComparison.OrdinalIgnoreCase))
            {
                using var json = JsonDocument.Parse(text);
                if (json.RootElement.TryGetProperty("Logging", out _)) logging.Add(logical);
                if (json.RootElement.TryGetProperty("Encryption", out _)) encrypted.Add(logical);
            }
            else if (file.EndsWith("global.json", StringComparison.OrdinalIgnoreCase))
            {
                using var json = JsonDocument.Parse(text);
                if (json.RootElement.TryGetProperty("sdk", out var sdk) && sdk.TryGetProperty("version", out var version)) versions.Add(version.GetString() ?? "unknown");
            }
        }

        var limitations = new[]
        {
            "The adapter reads manifests only; it does not restore, build, execute, or inspect compiled output.",
            "Runtime endpoints, infrastructure resources, and owners are not inferred from source code."
        };
        var model = new NeutralProjectModel(
            request.ProjectId, ProjectType, versions.Order(StringComparer.Ordinal).FirstOrDefault(), "/",
            components.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            dependencies.OrderBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.Version, StringComparer.Ordinal).ToArray(),
            [], [], [], [], [], encrypted.Order(StringComparer.Ordinal).ToArray(), logging.Order(StringComparer.Ordinal).ToArray(), [],
            files.Select(reader.ToLogicalPath).Order(StringComparer.Ordinal).ToArray(), ignored.Order(StringComparer.Ordinal).ToArray(), limitations);
        await Task.CompletedTask;
        return new(ProjectAdapterStatus.Analyzed, AdapterId, AdapterVersion, ProjectType, model,
            [.. model.AnalyzedFiles, "dotnet-manifest-read"], limitations, findings,
            findings.Count == 0 ? "Supported .NET manifests analyzed read-only." : "Potential secret content requires review.");
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
