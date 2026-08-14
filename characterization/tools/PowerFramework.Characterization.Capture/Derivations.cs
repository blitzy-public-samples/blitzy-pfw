// ==================================================================================================
//  Derivations - the seven deterministic functions a plan may apply to what a call returned
//  ------------------------------------------------------------------------------------------------
//  WHY DERIVATIONS EXIST AT ALL. Several of a workflow definition's declared outputs are not values a
//  response carries. "The round trip returned the original plaintext" is a comparison. "Identical
//  plaintext blocks encrypt to identical ciphertext blocks" is a property of the ciphertext's structure.
//  "The generator draws from the symbol class" is a property of a sample set. Each is a small, total,
//  deterministic function of values the run already holds, and each is the ONLY honest way to record the
//  output the definition actually asked for.
//
//  WHY THE SET IS CLOSED. An expression language in a plan file would move behaviour into data, where no
//  test reaches it and no reviewer can check it. Seven named functions, each with its own rows in the
//  test project, keeps every plan reviewable as data.
//
//  WHY THIS IS A SEPARATE CLASS FROM THE RUNNER. Everything here takes resolved strings and answers a
//  JSON object - no HTTP, no plan, no credential, no state. That is what makes the seven functions
//  directly unit-testable rather than testable through a seam, and it is why reference resolution
//  deliberately stayed behind in CaptureRunner.
// ==================================================================================================

using System.Text.Json.Nodes;

namespace PowerFramework.Characterization.Capture;

/// <summary>The closed set of deterministic functions a capture plan may apply.</summary>
internal static class Derivations
{
    /// <summary>Applies one derivation to its already-resolved inputs.</summary>
    /// <param name="derivation">The derivation, for its kind, name and settings.</param>
    /// <param name="inputs">The resolved inputs. The first is <c>value</c>; the rest are <c>values</c>.</param>
    /// <param name="other">The resolved second operand, for the comparing derivation.</param>
    /// <param name="context">The owning step's output name, for the failure messages.</param>
    /// <returns>The derived observation.</returns>
    /// <exception cref="CaptureFailure">A setting is missing, or an input is not of the required form.</exception>
    internal static JsonObject Apply(
        PlannedDerivation derivation,
        IReadOnlyList<string> inputs,
        string? other,
        string context)
    {
        ArgumentNullException.ThrowIfNull(derivation);
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.Count == 0)
        {
            throw new CaptureFailure(
                $"Derivation '{derivation.Name}' of '{context}' was given no input to derive from.");
        }

        string value = inputs[0];

        JsonObject derived = new()
        {
            ["kind"] = derivation.Kind,
        };

        if (derivation.Observes is { } observes)
        {
            derived["observes"] = observes;
        }

        switch (derivation.Kind)
        {
            case "equals":
                derived["equal"] = string.Equals(
                    value,
                    other ?? throw new CaptureFailure(
                        $"Derivation '{derivation.Name}' of '{context}' compares, but names no 'other' "
                        + "value to compare against."),
                    StringComparison.Ordinal);
                break;

            case "hexOfBase64":
                byte[] rendered = DecodeBase64(value, derivation, context);
                derived["byteCount"] = rendered.Length;
                derived["hex"] = Convert.ToHexStringLower(rendered);
                break;

            case "byteLengthOfBase64":
                derived["byteCount"] = DecodeBase64(value, derivation, context).Length;
                break;

            case "characterLength":
                derived["characters"] = value.Length;
                break;

            case "hexBlockRepetition":
                BlockRepetition(derivation, context, value, derived);
                break;

            case "characterClasses":
                CharacterClasses(inputs, derived);
                break;

            case "guidFormatting":
                GuidFormatting(value, derived);
                break;

            default:
                throw new CaptureFailure(
                    $"Derivation kind '{derivation.Kind}' passed plan validation but is not implemented. "
                    + "CapturePlan.DerivationKinds and this switch have diverged, which is the one way a "
                    + "plan can name a derivation nothing computes.");
        }

        return derived;
    }

    /// <summary>
    /// Splits a ciphertext into cipher blocks and counts the repetition an unchained mode leaks.
    /// </summary>
    /// <param name="derivation">The derivation.</param>
    /// <param name="context">The owning step, for the message.</param>
    /// <param name="value">The base64 ciphertext.</param>
    /// <param name="derived">The observation being built.</param>
    /// <exception cref="CaptureFailure">The block size is absent or not positive.</exception>
    /// <remarks>
    /// 🔴 THIS DERIVATION IS THE WEAKNESS MADE VISIBLE. Under the electronic-codebook default a plaintext
    /// built from a repeated block encrypts to a ciphertext whose corresponding blocks are identical, so
    /// plaintext structure survives into the ciphertext. The workflow definition requires that be compared
    /// rather than masked: the determinism is the point, and a target that hid it would be a parity
    /// failure rather than a hardening.
    /// </remarks>
    private static void BlockRepetition(
        PlannedDerivation derivation,
        string context,
        string value,
        JsonObject derived)
    {
        if (derivation.BlockBytes <= 0)
        {
            throw new CaptureFailure(
                $"Derivation '{derivation.Name}' of '{context}' counts block repetition but names no "
                + "positive blockBytes. The cipher's block size is a property of the cipher and cannot be "
                + "inferred from the ciphertext.");
        }

        byte[] cipher = DecodeBase64(value, derivation, context);

        List<string> blocks = [];

        for (int offset = 0; offset + derivation.BlockBytes <= cipher.Length; offset += derivation.BlockBytes)
        {
            blocks.Add(Convert.ToHexStringLower(cipher.AsSpan(offset, derivation.BlockBytes)));
        }

        derived["blockBytes"] = derivation.BlockBytes;
        derived["byteCount"] = cipher.Length;
        derived["blockCount"] = blocks.Count;
        derived["distinctBlocks"] = blocks.Distinct(StringComparer.Ordinal).Count();
        derived["maximumBlockRepetition"] = blocks.Count == 0
            ? 0
            : blocks.GroupBy(static block => block, StringComparer.Ordinal).Max(static group => group.Count());
        derived["blocks"] = new JsonArray([.. blocks.Select(static block => (JsonNode)JsonValue.Create(block)!)]);
    }

    /// <summary>Unions the character classes observed across every sample.</summary>
    /// <param name="samples">The resolved samples.</param>
    /// <param name="derived">The observation being built.</param>
    /// <remarks>
    /// 🔴 THE UNION IS THE OBSERVATION, AND THAT WAS MEASURED RATHER THAN REASONED. "Which classes does
    /// this generator draw from" is a property of the generator; "which classes does this one string
    /// exhibit" is a property of the sample. Two runs of a 24-character sample under identical flags
    /// disagreed on whether a digit was present, which is what a golden-master store records as a
    /// difference where no behaviour changed. A class the alphabet does not contain can never be observed,
    /// so unioning across samples only ever adds classes that are genuinely there - which makes the union
    /// sound and a single sample unsound. Both the union and the per-sample figures are written: seeing the
    /// per-sample figures differ is the evidence that the union is the sound comparison rather than a
    /// convenience.
    /// </remarks>
    private static void CharacterClasses(IReadOnlyList<string> samples, JsonObject derived)
    {
        JsonArray perSample = [];

        foreach (string sample in samples)
        {
            perSample.Add(new JsonObject
            {
                ["characters"] = sample.Length,
                ["digits"] = sample.Any(char.IsAsciiDigit),
                ["letters"] = sample.Any(char.IsAsciiLetter),
                ["symbols"] = sample.Any(IsSymbol),
            });
        }

        derived["sampleCount"] = samples.Count;
        derived["charactersObserved"] = samples.Sum(static sample => sample.Length);
        derived["perSample"] = perSample;
        derived["union"] = new JsonObject
        {
            ["digits"] = samples.Any(static sample => sample.Any(char.IsAsciiDigit)),
            ["letters"] = samples.Any(static sample => sample.Any(char.IsAsciiLetter)),
            ["symbols"] = samples.Any(static sample => sample.Any(IsSymbol)),
        };
    }

    /// <summary>Describes an identifier's formatting without revealing the identifier.</summary>
    /// <param name="value">The identifier.</param>
    /// <param name="derived">The observation being built.</param>
    /// <remarks>
    /// The value is a masked seam; the FORMATTING is behaviour. Bracket presence, separator count and
    /// separator positions are what distinguish the four flag combinations from each other, and they are
    /// a non-invertible summary - which is why this derivation is admissible over an opaque value where
    /// a hexadecimal rendering is not.
    /// </remarks>
    private static void GuidFormatting(string value, JsonObject derived)
    {
        derived["characters"] = value.Length;
        derived["bracketed"] = value.StartsWith('{') && value.EndsWith('}');
        derived["separatorCount"] = value.Count(static character => character == '-');
        derived["separatorPositions"] = new JsonArray(
            [.. value.Select(static (character, index) => (character, index))
                .Where(static pair => pair.character == '-')
                .Select(static pair => (JsonNode)JsonValue.Create(pair.index)!)]);
    }

    /// <summary>Whether a character belongs to neither the digit nor the letter class.</summary>
    /// <param name="character">The character.</param>
    /// <returns>True when it is a symbol.</returns>
    private static bool IsSymbol(char character) =>
        !char.IsAsciiLetterOrDigit(character) && !char.IsWhiteSpace(character);

    /// <summary>Decodes a base64 input, failing with a message a plan author can act on.</summary>
    /// <param name="value">The value.</param>
    /// <param name="derivation">The derivation, for the message.</param>
    /// <param name="context">The owning step, for the message.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="CaptureFailure">The value is not base64.</exception>
    private static byte[] DecodeBase64(string value, PlannedDerivation derivation, string context)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            throw new CaptureFailure(
                $"Derivation '{derivation.Name}' of '{context}' expects base64 and its input is not "
                + "base64. On this contract a BLOB payload and every digest are base64; a STRING payload "
                + "is not.");
        }
    }
}
