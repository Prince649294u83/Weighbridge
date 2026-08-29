namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Intermediate representation of an extracted frame before indicator-specific measurement decoding.
/// Preserves the exact raw numeric character string and leading zeros.
/// </summary>
public sealed record ParsedWeightFrame(
    string RawNumericPayload,
    string Unit,
    bool? ExplicitStability,
    string RawFrame);
