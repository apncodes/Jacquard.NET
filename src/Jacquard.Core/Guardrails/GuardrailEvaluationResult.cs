namespace Jacquard.Core;

public sealed record GuardrailEvaluationResult(
    GuardrailAction Action,
    string? BlockedMessage,
    string? GuardrailId,
    string? GuardrailVersion);
