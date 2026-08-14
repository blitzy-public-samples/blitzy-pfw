// ==================================================================================================
//  SecretScreen - the structural half of "no recording may carry a secret"
//  ------------------------------------------------------------------------------------------------
//  characterization/README.md section 6.2 rules that no recording may capture, echo or store key
//  material, a certificate, a password or a token, and the crypto workflow restates it as a
//  store-wide rule for every recording taken under it. A rule stated twice in prose is still a rule
//  nobody executes, so the driver holds every sensitive value it was given and does two things with
//  them: it REPLACES each with the placeholder name it came from, and then it ASSERTS that none
//  survives anywhere in the finished recording. The second half is what makes it a gate: a plan that
//  captured a member nobody expected to be sensitive fails the run rather than publishing it.
//
//  A KEY REFERENCE IS TREATED AS CREDENTIAL-EQUIVALENT, which is not obvious and was a finding in its
//  own right: an opaque reference names key material the service will USE on the presenter's behalf,
//  so it is exactly as reusable as the material for as long as it is registered. Recordings therefore
//  carry `${keyRef}` and never the reference an operator passed.
// ==================================================================================================

using System.Text.Json.Nodes;

namespace PowerFramework.Characterization.Capture;

/// <summary>Replaces sensitive values with their placeholder names and proves none survives.</summary>
internal sealed class SecretScreen
{
    /// <summary>Sensitive value to placeholder, longest first so a substring cannot mask a superstring.</summary>
    private readonly List<KeyValuePair<string, string>> _replacements = [];

    /// <summary>Registers a value that must never appear in a recording.</summary>
    /// <param name="value">The sensitive value, or <see langword="null"/> to register nothing.</param>
    /// <param name="placeholder">The text to write in its place.</param>
    internal void Register(string? value, string placeholder)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        // A one- or two-character "secret" would rewrite half the recording. A real reference, token or
        // credential is longer than this, and the bound keeps a degenerate value from corrupting output
        // rather than protecting anything.
        if (value.Length < 4)
        {
            return;
        }

        _replacements.Add(new KeyValuePair<string, string>(value, placeholder));
        _replacements.Sort(static (left, right) => right.Key.Length.CompareTo(left.Key.Length));
    }

    /// <summary>Rewrites every registered value out of a string.</summary>
    /// <param name="text">The text to screen.</param>
    /// <returns>The screened text.</returns>
    internal string Screen(string text)
    {
        string screened = text;

        foreach ((string value, string placeholder) in _replacements)
        {
            screened = screened.Replace(value, placeholder, StringComparison.Ordinal);
        }

        return screened;
    }

    /// <summary>Rewrites every registered value out of a JSON tree, in place.</summary>
    /// <param name="node">The node to screen.</param>
    internal void Screen(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject members:
                foreach (string name in members.Select(static member => member.Key).ToArray())
                {
                    JsonNode? value = members[name];

                    if (value is JsonValue && value.GetValueKind() == System.Text.Json.JsonValueKind.String)
                    {
                        members[name] = JsonValue.Create(Screen(value.GetValue<string>()));
                    }
                    else
                    {
                        Screen(value);
                    }
                }

                break;

            case JsonArray items:
                for (int index = 0; index < items.Count; index++)
                {
                    JsonNode? value = items[index];

                    if (value is JsonValue && value.GetValueKind() == System.Text.Json.JsonValueKind.String)
                    {
                        items[index] = JsonValue.Create(Screen(value.GetValue<string>()));
                    }
                    else
                    {
                        Screen(value);
                    }
                }

                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Proves no registered value survives in the finished text.
    /// </summary>
    /// <param name="text">The serialized recording.</param>
    /// <param name="artifact">The artifact name, for the failure message.</param>
    /// <exception cref="CaptureFailure">A sensitive value reached the recording.</exception>
    /// <remarks>
    /// THE FAILURE NAMES THE PLACEHOLDER, NEVER THE VALUE. A message quoting what leaked would leak it
    /// again into a build log, which is the same mistake one layer out.
    /// </remarks>
    internal void Prove(string text, string artifact)
    {
        foreach ((string value, string placeholder) in _replacements)
        {
            if (text.Contains(value, StringComparison.Ordinal))
            {
                throw new CaptureFailure(
                    $"The recording '{artifact}' carries the value registered as {placeholder}. "
                    + "characterization/README.md section 6.2 forbids any recording holding key material, "
                    + "a certificate, a password or a token, and a key REFERENCE is credential-equivalent "
                    + "because it names material the service will use on the presenter's behalf. Nothing "
                    + "was written. The value is deliberately not quoted here.");
            }
        }
    }
}
