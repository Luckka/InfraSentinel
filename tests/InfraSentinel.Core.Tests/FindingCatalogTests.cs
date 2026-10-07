using System.Text.Json;
using InfraSentinel.Core;
using InfraSentinel.Core.Cloud;
using InfraSentinel.Core.Findings;
using InfraSentinel.Core.IaC;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class FindingCatalogTests
{
    [Fact]
    public void CatalogNormalizesSourcesOrdersEntriesAndDeduplicatesKeys()
    {
        var catalog = new FindingCatalog("execution-1", "infra-sentinel", "m21-finding-catalog");
        var cloudFinding = new CloudFinding(
            "SECURITY-ADMIN-PORT",
            new("i-1", "ec2-instance", "us-east-1"),
            CloudFindingSeverity.Critical,
            "Administrative port is public",
            "Administrative port is publicly open.",
            [new("snapshot", "adminPortOpen=true")],
            null,
            new("Restrict administrative access."),
            CloudFindingConfidence.High,
            CloudFindingEvaluationStatus.Finding,
            "cloud-key",
            true);

        catalog.AddCloud("cloud", [cloudFinding, cloudFinding]);
        catalog.AddTerraform("terraform", [new IaCFinding(
            "IAC-PLAINTEXT-SECRET", "secret", IaCFindingSeverity.Critical,
            "Plaintext secret", ["main.tf:2"], "main.tf", 2,
            "Remove the literal.", 0.95, null, true)]);

        var artifact = catalog.BuildArtifact();

        Assert.Equal("HumanRequired", artifact.Status);
        Assert.Equal(2, artifact.FindingCount);
        Assert.Equal(2, artifact.HumanApprovalCount);
        Assert.Equal(["cloud", "terraform"], artifact.Sources);
        Assert.Equal("cloud", artifact.Findings[0].Source);
        Assert.Equal("SECURITY-ADMIN-PORT", artifact.Findings[0].RuleId);
    }

    [Fact]
    public async Task WriterPersistsDeterministicCatalogUnderSentinelRunDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"infrasentinel-finding-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var configuration = new IAEngineConsumerConfiguration(root);
        var first = configuration.CreateFindingCatalog("execution-1", "m21-finding-catalog");
        var second = configuration.CreateFindingCatalog("execution-1", "m21-finding-catalog");
        var finding = new IaCFinding("IAC-OWNER", "db", IaCFindingSeverity.High, "Owner missing", ["main.tf:1"], "main.tf", 1, "Assign an owner.", 0.9, null, true);
        first.AddTerraform("terraform", [finding]);
        second.AddTerraform("terraform", [finding]);

        await configuration.CreateFindingCatalogWriter().WriteAsync(first);
        var firstJson = await File.ReadAllTextAsync(configuration.FindingCatalogArtifactPath);
        await configuration.CreateFindingCatalogWriter().WriteAsync(second);
        var secondJson = await File.ReadAllTextAsync(configuration.FindingCatalogArtifactPath);

        Assert.Equal(firstJson, secondJson);
        Assert.NotNull(JsonDocument.Parse(secondJson));
            Assert.Contains("finding-catalog.json", configuration.FindingCatalogArtifactPath, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
