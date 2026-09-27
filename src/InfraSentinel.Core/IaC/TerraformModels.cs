namespace InfraSentinel.Core.IaC;

public enum IaCFindingSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum IaCAnalysisStatus
{
    Analyzed,
    HumanRequired,
    Invalid,
    Failed
}

public sealed record IaCResource(
    string LogicalId,
    string ResourceType,
    string Kind,
    IReadOnlyList<string> Dependencies,
    bool PublicExposure,
    IReadOnlyList<int> Ports,
    bool? EncryptionConfigured,
    bool? LoggingConfigured,
    bool? BackupConfigured,
    bool Critical,
    bool PlaintextSecret,
    string? Owner,
    string? Environment,
    IReadOnlyList<string> Permissions,
    string File,
    int Line,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Limitations);

public sealed record IaCVariable(string Name, string File, int Line, bool Sensitive);
public sealed record IaCOutput(string Name, string File, int Line);
public sealed record IaCModule(string Name, string? Source, string File, int Line, bool External);
public sealed record IaCDataSource(string ResourceType, string Name, string File, int Line);

public sealed record NeutralIaCModel(
    string ProjectId,
    string ProjectType,
    string AnalysisVersion,
    IReadOnlyList<IaCResource> Resources,
    IReadOnlyList<IaCVariable> Variables,
    IReadOnlyList<IaCOutput> Outputs,
    IReadOnlyList<IaCModule> Modules,
    IReadOnlyList<IaCDataSource> DataSources,
    IReadOnlyList<string> AnalyzedFiles,
    IReadOnlyList<string> IgnoredFiles,
    IReadOnlyList<string> Limitations);

public sealed record IaCFinding(
    string RuleId,
    string Resource,
    IaCFindingSeverity Severity,
    string Explanation,
    IReadOnlyList<string> Evidence,
    string File,
    int? Line,
    string RemediationGuidance,
    double Confidence,
    string? Limitation,
    bool RequiresHumanApproval);

public sealed record TerraformAnalysis(
    IaCAnalysisStatus Status,
    string AdapterId,
    string AdapterVersion,
    NeutralIaCModel? Model,
    IReadOnlyList<IaCFinding> Findings,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Limitations,
    string Reason);

public sealed record TerraformEvaluation(
    string ExecutionId,
    NeutralIaCModel Model,
    IReadOnlyList<string> RulesEvaluated,
    IReadOnlyList<string> RulesPassed,
    IReadOnlyList<IaCFinding> Findings,
    IaCAnalysisStatus Status,
    bool RequiresHumanApproval,
    string Justification)
{
    public bool Approved => Status == IaCAnalysisStatus.Analyzed && Findings.Count == 0;
}
