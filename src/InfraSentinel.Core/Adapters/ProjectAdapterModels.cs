namespace InfraSentinel.Core.Adapters;

public enum ProjectAdapterStatus
{
    Analyzed,
    Unsupported,
    InvalidWorkspace,
    Blocked,
    Failed
}

public sealed record ProjectAdapterOptions(
    int MaxFiles = 100,
    long MaxFileBytes = 512 * 1024,
    TimeSpan? Timeout = null,
    IReadOnlyList<string>? ExcludedDirectoryNames = null)
{
    public TimeSpan EffectiveTimeout => Timeout ?? TimeSpan.FromSeconds(5);

    public IReadOnlyList<string> EffectiveExcludedDirectoryNames => ExcludedDirectoryNames ??
    [
        ".git", ".svn", ".hg", "node_modules", "bin", "obj", ".terraform",
        ".ai-runs", ".ai-state", ".ai-runs-infrasentinel", ".ai-state-infrasentinel",
        ".env", ".secrets", "secrets"
    ];
}

public sealed record ProjectAdapterRequest(
    string ProjectId,
    string WorkspaceRoot,
    ProjectAdapterOptions Options);

public sealed record ProjectComponent(string Id, string Kind, string Name);

public sealed record ProjectDependency(string Name, string? Version, string Source);

public sealed record NeutralProjectModel(
    string ProjectId,
    string ProjectType,
    string? ProjectVersion,
    string LogicalWorkspaceRoot,
    IReadOnlyList<ProjectComponent> Components,
    IReadOnlyList<ProjectDependency> Dependencies,
    IReadOnlyList<string> Endpoints,
    IReadOnlyList<string> QueuesOrTopics,
    IReadOnlyList<string> Databases,
    IReadOnlyList<string> Storage,
    IReadOnlyList<string> PublicResources,
    IReadOnlyList<string> EncryptionConfigurations,
    IReadOnlyList<string> LoggingConfigurations,
    IReadOnlyList<string> Owners,
    IReadOnlyList<string> AnalyzedFiles,
    IReadOnlyList<string> IgnoredFiles,
    IReadOnlyList<string> Limitations);

public sealed record ProjectAdapterFinding(
    string RuleId,
    string Severity,
    string Explanation,
    IReadOnlyList<string> Evidence,
    bool RequiresHumanApproval);

public sealed record ProjectAdapterAnalysis(
    ProjectAdapterStatus Status,
    string AdapterId,
    string AdapterVersion,
    string ProjectType,
    NeutralProjectModel? Model,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<ProjectAdapterFinding> Findings,
    string Reason);

public interface IProjectAdapter
{
    string AdapterId { get; }
    string AdapterVersion { get; }
    string ProjectType { get; }
    bool CanHandle(ProjectAdapterRequest request);
    Task<ProjectAdapterAnalysis> AnalyzeAsync(ProjectAdapterRequest request, CancellationToken cancellationToken = default);
}

public interface IProjectAdapterSelector
{
    Task<IProjectAdapter?> SelectAsync(ProjectAdapterRequest request, CancellationToken cancellationToken = default);
}

public sealed class ProjectAdapterSelector(IEnumerable<IProjectAdapter> adapters) : IProjectAdapterSelector
{
    private readonly IReadOnlyList<IProjectAdapter> adapters = adapters.ToArray();

    public Task<IProjectAdapter?> SelectAsync(ProjectAdapterRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        if (!Directory.Exists(request.WorkspaceRoot)) return Task.FromResult<IProjectAdapter?>(null);
        var matches = adapters.Where(adapter => adapter.CanHandle(request)).ToArray();
        return Task.FromResult(matches.Length == 1 ? matches[0] : null);
    }
}
