using InfraSentinel.Core.Validation;
using Xunit;

namespace InfraSentinel.Core.Tests;

public sealed class InfrastructureValidatorTests
{
    [Fact]
    public void SafeFixturePassesDeterministically()
    {
        var fixture = new SyntheticInfrastructureFixture(
            [new("network-1", "network")],
            ["minimum-policy"],
            ["minimum-policy"]);
        var validator = new DeterministicInfrastructureValidator();

        var first = validator.Validate(fixture);
        var second = validator.Validate(fixture);

        Assert.True(first.Passed);
        Assert.Empty(first.Findings);
        Assert.Equal(first.Passed, second.Passed);
        Assert.Equal(
            first.Findings.Select(finding => (finding.RuleId, finding.ResourceId, finding.Severity, finding.Explanation)),
            second.Findings.Select(finding => (finding.RuleId, finding.ResourceId, finding.Severity, finding.Explanation)));
    }

    [Fact]
    public void ExplicitlyUnsafeFixtureReturnsExplainableHighFinding()
    {
        var fixture = new SyntheticInfrastructureFixture(
            [new("network-1", "network", ExplicitlyInsecure: true)],
            ["minimum-policy"],
            []);

        var result = new DeterministicInfrastructureValidator().Validate(fixture);

        Assert.False(result.Passed);
        Assert.Contains(result.Findings, finding => finding.RuleId == "explicit-insecure-configuration"
            && finding.Severity == InfrastructureFindingSeverity.High
            && finding.Explanation.Contains("insecure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Findings, finding => finding.RuleId == "required-policy-present");
    }

    [Fact]
    public void InvalidDependencyReturnsStructuredFinding()
    {
        var fixture = new SyntheticInfrastructureFixture(
            [new("service-1", "service", DependsOn: "missing-1")],
            [],
            []);

        var result = new DeterministicInfrastructureValidator().Validate(fixture);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("dependency-must-exist", finding.RuleId);
        Assert.Equal("service-1", finding.ResourceId);
        Assert.Equal(InfrastructureFindingSeverity.High, finding.Severity);
    }
}
