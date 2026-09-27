using System.Text.RegularExpressions;
using InfraSentinel.Core.Adapters;

namespace InfraSentinel.Core.IaC;

public sealed partial class TerraformProjectAdapter : IProjectAdapter
{
    public string AdapterId => "terraform";
    public string AdapterVersion => "1.0-subset";
    public string ProjectType => "terraform";

    public bool CanHandle(ProjectAdapterRequest request)
        => Directory.Exists(request.WorkspaceRoot)
            && Directory.EnumerateFiles(request.WorkspaceRoot, "*.tf", SearchOption.AllDirectories).Any();

    public async Task<ProjectAdapterAnalysis> AnalyzeAsync(ProjectAdapterRequest request, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Options.EffectiveTimeout);
        try
        {
            var result = await AnalyzeCoreAsync(request, timeout.Token);
            var adapterFindings = result.Findings.Select(finding => new ProjectAdapterFinding(
                finding.RuleId, finding.Severity.ToString(), finding.Explanation, finding.Evidence, finding.RequiresHumanApproval)).ToArray();
            var adapterStatus = result.Status == IaCAnalysisStatus.Analyzed
                ? ProjectAdapterStatus.Analyzed
                : result.Status == IaCAnalysisStatus.HumanRequired ? ProjectAdapterStatus.Blocked : ProjectAdapterStatus.Failed;
            return new ProjectAdapterAnalysis(adapterStatus, AdapterId, AdapterVersion, ProjectType, null, result.Evidence, result.Limitations, adapterFindings, result.Reason)
            {
                DomainModel = result.Model
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ToProjectAdapter(Failed("Terraform analysis timed out.", "IAC-TIMEOUT"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return ToProjectAdapter(Failed("Terraform workspace could not be analyzed safely.", "IAC-READ-FAILED", exception.Message));
        }
    }

    private static async Task<TerraformAnalysis> AnalyzeCoreAsync(ProjectAdapterRequest request, CancellationToken cancellationToken)
    {
        var ignored = new List<string>();
        var reader = new ProjectAdapterFileReader(request);
        var files = reader.FindFiles(["*.tf"], ignored).Order(StringComparer.Ordinal).ToArray();
        if (files.Length == 0) return Failed("No Terraform files were found.", "IAC-MANIFEST-MISSING");
        var resources = new List<IaCResource>();
        var variables = new List<IaCVariable>();
        var outputs = new List<IaCOutput>();
        var modules = new List<IaCModule>();
        var dataSources = new List<IaCDataSource>();
        var limitations = new HashSet<string>(StringComparer.Ordinal)
        {
            "Only a documented Terraform subset is parsed; full HCL expression evaluation is not supported.",
            "Unknown values and external module contents are not inferred."
        };
        var findings = new List<IaCFinding>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ParseFile(reader.ReadText(file), reader.ToLogicalPath(file), resources, variables, outputs, modules, dataSources, findings, limitations);
        }
        var model = new NeutralIaCModel(request.ProjectId, "terraform", "1.0-subset",
            resources.OrderBy(resource => resource.File, StringComparer.Ordinal).ThenBy(resource => resource.Line).ThenBy(resource => resource.LogicalId, StringComparer.Ordinal).ToArray(),
            variables.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray(), outputs.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray(),
            modules.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray(), dataSources.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray(),
            files.Select(reader.ToLogicalPath).Order(StringComparer.Ordinal).ToArray(), ignored.Order(StringComparer.Ordinal).ToArray(), limitations.Order(StringComparer.Ordinal).ToArray());
        await Task.CompletedTask;
        var status = findings.Any(finding => finding.Severity == IaCFindingSeverity.Critical) ? IaCAnalysisStatus.HumanRequired : IaCAnalysisStatus.Analyzed;
        return new(status, "terraform", "1.0-subset", model, findings, [.. model.AnalyzedFiles, "terraform-files-read"], model.Limitations,
            findings.Count == 0 ? "Terraform subset analyzed successfully." : "The Terraform subset contains findings requiring validation.");
    }

    private static void ParseFile(string text, string file, List<IaCResource> resources, List<IaCVariable> variables, List<IaCOutput> outputs, List<IaCModule> modules, List<IaCDataSource> dataSources, List<IaCFinding> findings, HashSet<string> limitations)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var index = 0;
        while (index < lines.Length)
        {
            var line = lines[index];
            var match = BlockPattern().Match(line);
            if (!match.Success) { index++; continue; }
            var kind = match.Groups[1].Value;
            var type = match.Groups[2].Value;
            var name = match.Groups[3].Value;
            var start = index;
            var depth = Count(line, '{') - Count(line, '}');
            var block = new List<string> { line };
            while (depth > 0 && ++index < lines.Length)
            {
                block.Add(lines[index]);
                depth += Count(lines[index], '{') - Count(lines[index], '}');
            }
            if (depth != 0) throw new InvalidOperationException($"Unbalanced Terraform block at {file}:{start + 1}.");
            var content = string.Join('\n', block);
            if (content.Contains("for_each", StringComparison.Ordinal) || content.Contains("dynamic ", StringComparison.Ordinal))
                limitations.Add($"Dynamic expressions were not evaluated at {file}:{start + 1}.");
            switch (kind)
            {
                case "resource": resources.Add(ParseResource(type, name, block, file, start + 1, limitations)); break;
                case "data": dataSources.Add(new(type, name, file, start + 1)); break;
                case "variable": variables.Add(new(name, file, start + 1, HasTrue(block, "sensitive"))); break;
                case "output": outputs.Add(new(name, file, start + 1)); break;
                case "module": modules.Add(new(name, Attribute(block, "source"), file, start + 1, IsExternal(Attribute(block, "source")))); break;
            }
            index++;
        }
        if (lines.Any(line => line.Contains("${", StringComparison.Ordinal) || line.Contains("unknown", StringComparison.OrdinalIgnoreCase)))
            limitations.Add($"Unknown or interpolated values were not resolved in {file}.");
    }

    private static IaCResource ParseResource(string type, string name, IReadOnlyList<string> lines, string file, int line, HashSet<string> limitations)
    {
        var ports = lines.SelectMany(line => IntAttribute(line, "from_port").Concat(IntAttribute(line, "to_port"))).Distinct().Order().ToArray();
        var publicExposure = lines.Any(line => line.Contains("0.0.0.0/0", StringComparison.Ordinal) || HasTrue([line], "public") || HasValue([line], "exposure", "public"));
        var permissions = lines.Where(line => line.Contains("*", StringComparison.Ordinal) && (line.Contains("actions", StringComparison.OrdinalIgnoreCase) || line.Contains("permissions", StringComparison.OrdinalIgnoreCase))).Select(line => line.Trim()).ToArray();
        var deps = lines.Where(line => line.Contains("depends_on", StringComparison.Ordinal)).SelectMany(line => QuotedValues(line)).Order(StringComparer.Ordinal).ToArray();
        var evidence = lines.Where(line => line.Contains("public", StringComparison.OrdinalIgnoreCase) || line.Contains("encrypt", StringComparison.OrdinalIgnoreCase) || line.Contains("logging", StringComparison.OrdinalIgnoreCase) || line.Contains("backup", StringComparison.OrdinalIgnoreCase) || line.Contains("owner", StringComparison.OrdinalIgnoreCase) || line.Contains("port", StringComparison.OrdinalIgnoreCase) || line.Contains("action", StringComparison.OrdinalIgnoreCase) || line.Contains("secret", StringComparison.OrdinalIgnoreCase) || line.Contains("password", StringComparison.OrdinalIgnoreCase)).Select(line => line.Trim()).Take(20).ToArray();
        var plaintextSecret = lines.Any(line => line.Contains("secret", StringComparison.OrdinalIgnoreCase) || line.Contains("password", StringComparison.OrdinalIgnoreCase)) && lines.Any(line => line.Contains('=') && line.Contains('"'));
        IReadOnlyList<string> limitation = lines.Any(line => line.Contains("for_each", StringComparison.Ordinal)) ? ["for_each expression was not evaluated."] : [];
        if (limitation.Count > 0) foreach (var item in limitation) limitations.Add($"{item} at {file}:{line}");
        return new(name, type, "resource", deps, publicExposure, ports,
            OptionalTrue(lines, "encryption", "encrypt"), OptionalTrue(lines, "logging", "log"), OptionalTrue(lines, "backup", "retention"),
            HasTrue(lines, "critical") || type.Contains("security_group", StringComparison.OrdinalIgnoreCase), plaintextSecret, Attribute(lines, "owner"), Attribute(lines, "environment"), permissions, file, line, evidence, limitation);
    }

    private static bool? OptionalTrue(IReadOnlyList<string> lines, params string[] names)
    {
        var matching = lines.Where(line => names.Any(name => line.Contains(name, StringComparison.OrdinalIgnoreCase))).ToArray();
        return matching.Length == 0 ? null : matching.Any(line => HasTrue([line], names[0]) || line.Contains("enabled = true", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasTrue(IReadOnlyList<string> lines, string name) => lines.Any(line => Regex.IsMatch(line, $"\\b{Regex.Escape(name)}\\s*=\\s*true\\b", RegexOptions.IgnoreCase));
    private static bool HasValue(IReadOnlyList<string> lines, string name, string value) => lines.Any(line => line.Contains(name, StringComparison.OrdinalIgnoreCase) && line.Contains(value, StringComparison.OrdinalIgnoreCase));
    private static string? Attribute(IReadOnlyList<string> lines, string name) => lines.Select(line => Regex.Match(line, $"\\b{Regex.Escape(name)}\\s*=\\s*\\\"([^\\\"]*)\\\"", RegexOptions.IgnoreCase)).FirstOrDefault(match => match.Success)?.Groups[1].Value;
    private static IEnumerable<int> IntAttribute(string line, string name) { var match = Regex.Match(line, $"\\b{name}\\s*=\\s*(\\d+)", RegexOptions.IgnoreCase); return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? [value] : []; }
    private static IEnumerable<string> QuotedValues(string line) => Regex.Matches(line, "\\\"([^\\\"]+)\\\"").Select(match => match.Groups[1].Value);
    private static bool IsExternal(string? source) => source is not null && (source.StartsWith("git::", StringComparison.OrdinalIgnoreCase) || source.Contains("/", StringComparison.Ordinal));
    private static int Count(string value, char character) => value.Count(item => item == character);
    private static TerraformAnalysis Failed(string reason, string ruleId, string? detail = null)
        => new(IaCAnalysisStatus.Failed, "terraform", "1.0-subset", null,
            [new(ruleId, "", IaCFindingSeverity.High, reason, detail is null ? [] : [detail], "", null, "Correct the Terraform syntax or workspace boundary.", 1.0, null, true)],
            [], [reason], reason);

    private static ProjectAdapterAnalysis ToProjectAdapter(TerraformAnalysis result)
        => new(ProjectAdapterStatus.Failed, "terraform", "1.0-subset", "terraform", null, result.Evidence, result.Limitations,
            result.Findings.Select(finding => new ProjectAdapterFinding(finding.RuleId, finding.Severity.ToString(), finding.Explanation, finding.Evidence, finding.RequiresHumanApproval)).ToArray(), result.Reason)
        {
            DomainModel = result.Model
        };

    [GeneratedRegex("^\\s*(resource|data|variable|output|module)\\s+\\\"([^\\\"]+)\\\"(?:\\s+\\\"([^\\\"]+)\\\")?")]
    private static partial Regex BlockPattern();
}
