// ==================================================================================================
//  SensitiveValueAssertions - assertions about live credential material that never render it
//  ------------------------------------------------------------------------------------------------
//  WHY THIS FILE EXISTS
//
//  Security is the sole JWT issuer, so its test suite is the one place in the repository where LIVE
//  credential material is routinely produced: a minted access token is a working credential for as
//  long as it is valid, and a signing secret generated inside a test row is real key material for the
//  duration of that row. Several rows must make a statement ABOUT one of those values - that two
//  issuances are byte-identical, that a rendering does not carry the token, that a failure message
//  does not echo a secret - and the obvious way to write each of them hands the value straight to an
//  xUnit assertion overload.
//
//  THAT IS THE DEFECT, AND IT IS A DISCLOSURE DEFECT RATHER THAN A CORRECTNESS ONE (CWE-532). The
//  assertions themselves are right. But `Assert.Equal(a, b)` renders BOTH operands into its failure
//  message, `Assert.NotEqual(a, b)` renders the value when the two agree, and
//  `Assert.DoesNotContain(needle, haystack)` renders both the needle and the haystack. A failure
//  therefore writes the credential into the test output, which travels into the CI log, the test
//  report artifact and any dashboard that ingests it - none of which are credential stores, and all
//  of which are retained far longer than the token is valid. Constraint C-F of the Agent Action Plan
//  (Section 0.7.3) is explicit that no secret may appear in source or in output, and that only
//  variable NAMES belong in a message.
//
//  THE TWO SHAPES THIS FILE PROVIDES, AND WHY BOTH ARE NEEDED
//
//    * EQUALITY BY FINGERPRINT. Byte-identity between two credentials is a real and load-bearing
//      claim - it is how the fixed-clock determinism rows prove the signature scheme is deterministic
//      and that the issuer stamps nothing of its own into the payload. Comparing SHA-256 fingerprints
//      preserves the claim exactly (equal inputs give equal digests, and a differing input gives a
//      differing digest for any realistic purpose) while making the rendered operands one-way. A
//      failure still says something useful - two unequal hexadecimal digests - and says nothing that
//      can be replayed.
//
//    * CONTAINMENT AS A BOOLEAN. For a "must not carry" guard there is nothing a rendered value adds:
//      the useful diagnostic is WHICH guard tripped, and that is prose. So the containment is
//      evaluated first and the assertion is made on the resulting boolean with fixed, value-free
//      wording. Where the surrounding row also needs a POSITIVE containment check against the same
//      haystack - a fault message that must name a configuration key, for instance - the haystack is
//      redacted before it can be rendered, which keeps that failure diagnosable without reintroducing
//      the disclosure through the other assertion in the pair.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//
//    * It does not touch rows whose operand is a SYNTHETIC test constant. A placeholder such as a
//      hand-written three-segment string or a test-local password literal cannot be replayed against
//      anything, so routing it through here would cost the row its diagnostic - the actual and
//      expected values - and buy no confidentiality at all. The distinction that matters is whether
//      the value was PRODUCED BY THE SYSTEM UNDER TEST from real key material.
//    * It does not wrap assertions that never render a sensitive operand. `Assert.NotEmpty` on a
//      credential fails only when the credential is empty, and a row that compares parsed CLAIMS
//      rather than the compact serialization renders claims. Wrapping either would be ceremony.
//    * It introduces no package. The fingerprint is BCL SHA-256, consistent with this project's
//      standing rule that the base class library covers the whole cryptographic surface.
// ==================================================================================================

using System.Security.Cryptography;
using System.Text;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Assertions and helpers for making statements about live credential material without rendering it.
/// </summary>
/// <remarks>
/// Every member here is deliberately small and non-generic. The point is not abstraction - it is that
/// the fingerprint idiom be IDENTICAL at every site that compares two credentials, because two rows
/// fingerprinting differently would be comparing different things while appearing to compare the same
/// thing.
/// </remarks>
internal static class SensitiveValueAssertions
{
    /// <summary>The stand-in written in place of a redacted occurrence.</summary>
    /// <remarks>
    /// Deliberately conspicuous and deliberately fixed-length regardless of what it replaced, so a
    /// redacted rendering leaks neither the value nor its length.
    /// </remarks>
    internal const string RedactionMarker = "[REDACTED]";

    /// <summary>
    /// Produces a one-way fingerprint of a sensitive value, safe to render in a failure message.
    /// </summary>
    /// <param name="value">The sensitive value.</param>
    /// <returns>The uppercase hexadecimal SHA-256 digest of the value's UTF-8 encoding.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    /// <remarks>
    /// <para>
    /// EMPTY IS REFUSED RATHER THAN FINGERPRINTED. Every caller here is comparing credentials that the
    /// service produced, and an empty one is a failure of the row's own premise: two empty values would
    /// fingerprint identically and an equality row would pass having proved nothing. Refusing makes that
    /// premise a checked precondition instead of a silent pass.
    /// </para>
    /// <para>
    /// SHA-256 rather than a shorter digest, and hexadecimal rather than base64, because the digest's
    /// only jobs are to be one-way and to be legible in a diff of two failure messages.
    /// </para>
    /// </remarks>
    internal static string Fingerprint(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>
    /// Reports whether one text carries a sensitive value, without either one being rendered.
    /// </summary>
    /// <param name="text">The text to search - a rendering, a response body or a fault message.</param>
    /// <param name="sensitive">The sensitive value that must or must not appear.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> contains <paramref name="sensitive"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sensitive"/> is empty.</exception>
    /// <remarks>
    /// <para>
    /// AN EMPTY NEEDLE IS REFUSED, and this guard is the whole reason the helper exists rather than the
    /// call site writing <c>text.Contains(...)</c> itself. Every string contains the empty string, so a
    /// "must not carry" row whose needle turned out empty would fail for a reason that has nothing to do
    /// with disclosure, and a "must carry" row would pass without checking anything. Refusing turns
    /// either accident into an immediate, legible failure.
    /// </para>
    /// <para>
    /// The comparison is ordinal. A credential is bytes rather than prose, so a culture-sensitive or
    /// case-insensitive search would be both slower and wrong.
    /// </para>
    /// </remarks>
    internal static bool Carries(string text, string sensitive)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(sensitive);

        return text.Contains(sensitive, StringComparison.Ordinal);
    }

    /// <summary>
    /// Replaces every occurrence of each sensitive value in a text with <see cref="RedactionMarker"/>.
    /// </summary>
    /// <param name="text">The text to redact.</param>
    /// <param name="sensitive">The sensitive values to remove.</param>
    /// <returns>The redacted text, safe to render.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// FOR THE POSITIVE HALF OF A PAIR. A row that asserts a fault message NAMES a configuration key and
    /// DOES NOT name the value behind it has two assertions over one haystack; the second is safe as a
    /// boolean, but the first genuinely wants to show the message when the key is missing. Redacting
    /// first satisfies both: the failure shows the message, and the message cannot carry the value even
    /// if the very defect under test is present.
    /// </para>
    /// <para>
    /// An empty or null entry in <paramref name="sensitive"/> is skipped rather than refused, because
    /// this helper is also used where one half of a configured pair is deliberately unset - replacing
    /// the empty string would rewrite the entire text into markers.
    /// </para>
    /// </remarks>
    internal static string Redact(string text, params string[] sensitive)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(sensitive);

        string redacted = text;

        foreach (string value in sensitive)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            redacted = redacted.Replace(value, RedactionMarker, StringComparison.Ordinal);
        }

        return redacted;
    }
}
