# First deterministic InfraSentinel validator

## Rule set

`DeterministicInfrastructureValidator` evaluates a synthetic fixture only. It
does not access a filesystem, network, cloud provider or external endpoint.

Rules:

| Rule | Severity | Meaning |
|---|---|---|
| `resource-identification-required` | High | Every resource must have an identifier |
| `resource-identification-unique` | High | Identifiers must be unique |
| `explicit-insecure-configuration` | High | A fixture may explicitly mark a resource as insecure |
| `dependency-must-exist` | High | Every declared dependency must identify a resource in the fixture |
| `required-policy-present` | High | Every required policy must appear in configured policies |

## Input

```json
{
  "resources": [
    { "id": "network-1", "kind": "network", "explicitlyInsecure": false }
  ],
  "requiredPolicies": ["minimum-policy"],
  "configuredPolicies": ["minimum-policy"]
}
```

## Output

```json
{
  "passed": false,
  "findings": [
    {
      "ruleId": "explicit-insecure-configuration",
      "resourceId": "network-1",
      "severity": "High",
      "explanation": "The fixture explicitly marks this resource configuration as insecure."
    }
  ]
}
```

The `InfrastructureValidationRunner` adapts this report to the IAEngine
`IValidationRunner` contract. The Engine persists it in the consumer's
`.ai-runs-infrasentinel` artifacts; the rule and finding types remain owned by
InfraSentinel.

## Flow through EngineHost

```text
synthetic fixture
  → DeterministicInfrastructureValidator
  → InfrastructureValidationRunner
  → EngineHost composition
  → Orchestrator validation stage
  → validation-N.json artifact
  → Approved or HUMAN_REQUIRED
```

## Learning Checkpoint

1. The validator resolves whether a local infrastructure fixture satisfies explicit declarative safety prerequisites.
2. The rule belongs to InfraSentinel because it expresses infrastructure/security domain meaning, not workflow mechanics.
3. The Engine executes it through the generic `IValidationRunner` capability.
4. The structured report is serialized into the validation artifact and remains available in the consumer's run directory.
5. Production security scope, severity policy, remediation authority and any real infrastructure access still require human authority.

## Limitations

This is a synthetic local validator. It is not an AWS scanner, a penetration test,
an exploit detector or a production security policy engine.
