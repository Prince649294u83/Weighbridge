using WeighBridge.Core.Configuration;

namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Decodes raw numeric frame payloads into normalized numeric weights according to the active indicator profile.
/// </summary>
public interface IWeightDecoder
{
    /// <summary>
    /// Decodes the raw frame into a normalized weight value.
    /// </summary>
    /// <param name="frame">The parsed frame containing the raw numeric payload string.</param>
    /// <param name="options">The decoding profile options.</param>
    /// <param name="decodedWeight">The resulting decoded weight in target units if successful.</param>
    /// <returns>True if decoding succeeds and all invariants are met; otherwise false.</returns>
    bool TryDecode(ParsedWeightFrame frame, WeightDecodeOptions options, out decimal decodedWeight);
}
