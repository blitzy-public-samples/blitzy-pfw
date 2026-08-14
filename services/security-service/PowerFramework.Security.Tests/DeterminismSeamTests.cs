// ==============================================================================================
//  DeterminismSeamTests - the suite that EXERCISES this service's determinism seam
//  --------------------------------------------------------------------------------------------
//  docs/PARITY.md §5.1 registers four determinism seams for the whole refactor and puts THREE of
//  them in this one service, as a single row: "GUID generation, random string generation, random
//  blob generation", described there as "The primary non-determinism sources in the in-scope
//  estate". The same section states plainly that "A seam that is documented but not injected has
//  no effect on a single test" and that "No implemented seam here has been executed."
//
//  THIS FILE IS THE EXECUTION. It is the difference between a seam that is present and a seam
//  that is proven, and it discharges two separate obligations that are easy to conflate:
//
//    1. SUBSTITUTION, NOT LUCK. Every value asserted here is an EXACT value produced with a
//       deterministic double installed. "Two generated GUIDs differ" is not a determinism
//       assertion - it is a coin flip that happens to land the same way every run, and it would
//       still pass against a provider that had no seam at all. Nothing of that shape appears
//       below.
//    2. THE SEAM EXISTS, IS INJECTABLE, AND IS TOTAL. The structural half. If a later change made
//       one member of RandomProvider reach a static randomness API directly, every exact-value
//       assertion for that member would break here - which is the whole point, because the
//       characterization model would otherwise become silently non-repeatable. Golden-master
//       comparison has exactly one hard prerequisite, repeatability, with non-deterministic values
//       masked from BOTH the master and the candidate; a value drawn from a process-wide source
//       cannot be masked on either side.
//
//  ==============================================================================================
//  WHERE THE SEAM LIVES, AND WHY IT EXISTS AT ALL
//  ==============================================================================================
//  The seam is `IEntropySource`, declared in
//  services/security-service/PowerFramework.Security/Crypto/RandomProvider.cs alongside its
//  production implementation `CryptographicEntropySource`; that folder admits exactly seven files,
//  so the abstraction sits beside the only provider that draws from it rather than in a file of its
//  own. `Program.cs` registers both it and `RandomProvider` in the composition root, and
//  `SecurityAppFactory` replaces the registration with a deterministic double.
//
//  ITS PROVENANCE IS A LEGACY IDIOM THAT HAS NO C# ANALOGUE, and that is worth stating because it
//  explains why an injectable source is the natural port rather than an invention:
//
//      ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L75 declares `global n_crypto n_crypto` - a
//      global auto-instance whose NAME SHADOWS ITS OWN TYPE NAME - and both global-function
//      wrappers reach it through `if Not IsValid(n_crypto) then n_crypto = Create n_crypto`
//      [randomstring.srf:L14, guid.srf:L14], lazily constructing that global on first use.
//
//  Dependency injection replaces the shadowing global, the validity check and the lazy
//  construction all at once: the instance's lifetime belongs to the composition root, so there is
//  nothing to test for validity and nothing to construct on demand. The seam is therefore what the
//  legacy's own global-plus-lazy-guard idiom BECOMES, not an extra layer added on top of it - and
//  because the replacement is a constructor parameter rather than a static, it is substitutable,
//  which the global never was.
//
//  ==============================================================================================
//  THE ORACLE, AND THE HONEST LIMIT ON WHAT THESE ASSERTIONS PROVE
//  ==============================================================================================
//  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L8 declares the legacy class
//  `native "pfw.dll"`, so ALL FIVE generator bodies live inside a closed binary with no C++ source
//  anywhere in this repository. There is no crypto oracle window in the legacy test library
//  either. Consequently the following are NOT derivable from source and are asserted here as the
//  port's own DOCUMENTED, CHARACTERIZED-OR-ASSUMED behaviour rather than as proven legacy parity:
//
//      * the three character-class alphabets - which characters each flag bit selects;
//      * the case of the GUID's hexadecimal digits;
//      * the behaviour of a zero-length request;
//      * the service-level maximum length, which is a deliberate narrowing this port adds.
//
//  What IS derivable from source, and is therefore asserted as parity rather than as assumption,
//  is the pair of DEFAULT FLAG COMBINATIONS - because the two global-function wrappers hardcode
//  them in PowerScript that can be read:
//
//      randomstring.srf:L11  returns RandomString(size, Enums.CRYPTO_RNDSTRING_NUMBER
//                                                     + Enums.CRYPTO_RNDSTRING_ALPHABET)
//      guid.srf:L11          returns Guid(Enums.CRYPTO_GUID_INCLUDE_BRACKET
//                                       + Enums.CRYPTO_GUID_INCLUDE_SEPARATOR)
//
//  and because ws_objects/pfw.shared.pbl.src/enums.sru NAMES both of those sums `DEFAULT`
//  [:L957 and :L962]. Two independent statements of the same value, both readable.
//
//  EVERY ws_objects/** PATH CITED IN THIS FILE IS A LOCATOR IN A COMMENT AND NOTHING MORE. No test
//  below opens, reads, parses or fixture-loads any legacy path, and no legacy file is edited,
//  moved or reformatted by this suite. The legacy tree is read-only and is the behavioural oracle.
//
//  ==============================================================================================
//  THE DOUBLE, AND WHY THE EXPECTED VALUES LOOK THE WAY THEY DO
//  ==============================================================================================
//  The deterministic double is `DeterministicEntropySource`, declared once in
//  SecurityAppFactory.cs and CONSUMED here. It is not redeclared: a second copy in this file would
//  be a second definition able to drift from the one the rest of this folder - and the factory's
//  own DI replacement - relies on.
//
//  Its sequence is an ARITHMETIC RAMP: the byte at sequence position p is (seed + p) truncated to
//  eight bits, the position spans calls rather than restarting at each one, and `Reset` returns to
//  the start. Every expectation below is a hand-checkable consequence of that one rule, which is
//  why the expected values read as runs rather than as noise:
//
//      a blob is the ramp itself                       0, 1, 2, 3, ...
//      a random string is the ALPHABET'S OWN PREFIX    "0123456789ABCDEFGHIJ"
//      a GUID is the octets 00..0F with six bits set   {00010203-0405-4607-8809-0a0b0c0d0e0f}
//
//  A SEED IS AN OFFSET INTO THE SAME RAMP, WHICH IS HOW A WINDOW IS AIMED. Seed 10 lands the first
//  drawn byte on the letters of the digits-plus-letters alphabet; seed 62 lands it on the symbols
//  of the all-three alphabet. That is what lets a row prove a character class CONTRIBUTES when its
//  flag is set, using a twelve-character expectation instead of a ninety-four-character one.
//
//  THE RAMP IS OBVIOUSLY SYNTHETIC, AND THAT IS A REQUIREMENT RATHER THAN A CONVENIENCE. This file
//  drives a randomness surface, so it is exactly the file where a "realistic-looking" seed would be
//  tempting - and a seed copied from any of the repository's known hardcoded-secret sites (for
//  instance the shared configuration key at ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru:L22)
//  would be a leaked credential no matter how small its role in the test. Nothing of the kind
//  appears here: the only literals below are an ascending byte run, ascending alphabet prefixes,
//  and the GUID text those produce. No key, no initialization vector, no passphrase, no
//  certificate, no PEM block, and no value taken from any site listed in docs/SECRETS.md.
//
//  ==============================================================================================
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//  ==============================================================================================
//  * IT DOES NOT RE-TEST THE CRYPTOGRAPHIC MATRICES OR THE WEAK DEFAULTS. Hashing, keyed hashing,
//    symmetric and RSA behaviour belong to CryptoParityTests.cs, and the annotated weak defaults -
//    ECB, PKCS#1, no key-derivation function, no authenticated encryption, 1024-bit RSA - belong to
//    LegacyDefaultsTests.cs. Coverage here is additive: the determinism dimension of the three
//    generator surfaces, and their two preserved flag defaults, which are what the seam register
//    names.
//  * IT DECLARES NO CONSTANT OF THE PRESERVED SCREAMING_SNAKE FAMILY. The root .editorconfig
//    scopes its CA1707 and IDE1006 suppressions to the individual files that genuinely declare a
//    preserved identifier, and NO TEST FILE IS AMONG THEM - so a declaration here would be a build
//    error under TreatWarningsAsErrors rather than a style note. Every flag value below is an
//    `Enums.`-qualified REFERENCE into PowerFramework.Shared.Kernel, or a
//    `LegacyDefaults.`-qualified one; references are unaffected by those rules, which report
//    declarations only.
//  * IT ADDS NO PACKAGE. There is no mocking library and no clock-testing package in
//    Directory.Packages.props, deliberately, and this file needs neither: the doubles are
//    hand-authored in SecurityAppFactory.cs and the assertions are xunit.v3's own.
//  * IT CONTAINS NOTHING FOR A DEFERRED SERVICE. No design-system, document, integration or
//    scripting concern appears here in any form.
//  * IT ASSERTS NO PERFORMANCE PROPERTY. No throughput, latency or availability target is
//    published anywhere in this refactor, so none is claimed. Where a size is bounded below it is
//    bounded to keep the suite's own allocation honest, never as a measured budget.
//
//  ==============================================================================================
//  GOVERNING RULES
//  ==============================================================================================
//  `review_rules` returns exactly one line: "No user rules provided." NO USER-SPECIFIED RULE
//  GOVERNS THIS FILE, and that absence is recorded rather than treated as latitude. This file is
//  held instead to the enterprise-standard baseline the migration plan states: nullable reference
//  types on with warnings as errors, table-driven matrices expressed as theories with member data,
//  no secret in a fixture, and every disposable disposed. The binding constraints it answers to
//  are the plan's own, and each is discharged above or in the region noted:
//
//      C-B  preserved behaviour, no improvement    the two flag defaults asserted as CORRECT
//      C-C  the legacy tree is read-only oracle    locators in comments only, never opened
//      C-D  nothing for a deferred service         no such reference exists here
//      C-F  no secret in a fixture                 the ramp, and the sweep noted above
//      C-H  80% line coverage per service          this file's share is RandomProvider.cs in full
//      C-K  document every decision                the characterized-or-assumed list above
// ==============================================================================================

using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

using PowerFramework.Security.Crypto;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Proves that the GUID, random-string and random-blob generators of this service draw every byte
/// through an injected, substitutable seam, and that a deterministic double installed in that seam
/// makes each of them produce an exact, repeatable value.
/// </summary>
/// <remarks>
/// <para>
/// See this file's header for the seam's provenance, the honest limit on what these assertions
/// prove, and the arithmetic rule every expected value below follows from.
/// </para>
/// <para>
/// TWO WAYS OF INSTALLING THE DOUBLE APPEAR BELOW, AND BOTH EARN THEIR PLACE. The exact-value
/// theories construct <see cref="RandomProvider"/> DIRECTLY with the double, because they are
/// asserting the provider's own behaviour and booting a web host to do it would add a second
/// failure mode to a first-order assertion. The injectability proofs go through
/// <see cref="SecurityAppFactory"/>, because the claim there is about the CONTAINER - that the
/// composition root registers the seam, that exactly one registration survives replacement, and
/// that the provider the container hands out is the one observing the double - and no amount of
/// direct construction can establish any of that.
/// </para>
/// </remarks>
public sealed class DeterminismSeamTests
{
    // ==========================================================================================
    //  Sizes and seeds. Every one is chosen so that its expected value is short enough to read.
    // ==========================================================================================

    /// <summary>
    /// The character count used by the random-string default assertions: twenty, which reaches ten
    /// digits and ten letters and so shows both halves of the preserved default alphabet in one
    /// value.
    /// </summary>
    private const uint DefaultStringSize = 20;

    /// <summary>
    /// The character count used by the alphabet-selection theory: twelve, which is long enough to
    /// show a class being drawn from and short enough that the expectation stays legible.
    /// </summary>
    private const uint AlphabetProbeSize = 12;

    /// <summary>
    /// The byte count used by the seam-totality and reads-the-seam proofs: thirty-two.
    /// </summary>
    /// <remarks>
    /// Thirty-two bytes is what bounds the reads-the-seam proof's false-failure probability at
    /// 2^-256 - see
    /// <see cref="TheExactValueExpectations_ReadTheSubstitutedSeamRatherThanRealRandomness"/>.
    /// </remarks>
    private const uint SeamProofBlobSize = 32;

    /// <summary>
    /// A seed that offsets the ramp far enough to change every drawn byte, used to show that an
    /// output depends on the bytes drawn rather than on the length requested.
    /// </summary>
    private const byte OffsetSeed = 0x10;

    /// <summary>
    /// The seed that lands the first drawn byte on the LETTERS of the digits-plus-letters
    /// alphabet, whose digits occupy the first ten positions.
    /// </summary>
    private const byte LetterWindowSeed = 10;

    /// <summary>
    /// The seed that lands the first drawn byte on the SYMBOLS of the all-three alphabet, whose
    /// digits and letters occupy the first sixty-two positions.
    /// </summary>
    /// <remarks>
    /// This row is the one that proves the symbol class CONTRIBUTES when its flag is set. Without a
    /// seed offset the same proof would need an expectation long enough to run past the letters,
    /// and a ninety-four-character literal is both unreadable and the kind of long alphanumeric
    /// run a secret scan has to stop and consider.
    /// </remarks>
    private const byte SymbolWindowSeed = 62;

    /// <summary>The octet count a single GUID draws, per RFC 4122 section 4.1.2.</summary>
    private const int GuidDrawInOctets = 16;

    /// <summary>The number of members <see cref="RandomProvider"/> publishes.</summary>
    /// <remarks>
    /// Five from the native declarations at n_crypto.sru:L14-L18 and two from the global-function
    /// wrappers at randomstring.srf:L11 and guid.srf:L11.
    /// </remarks>
    private const int PublishedMemberCount = 7;

    /// <summary>The number of group separators the canonical GUID text form carries.</summary>
    private const int CanonicalGuidSeparatorCount = 4;

    /// <summary>The number of distinct values one byte can take, used to wrap the expected ramp.</summary>
    private const uint ByteValueSpan = 256;

    /// <summary>The lowest printable ASCII character, used by the symbol-class rule.</summary>
    private const char FirstPrintableAscii = '!';

    /// <summary>The highest printable ASCII character, used by the symbol-class rule.</summary>
    private const char LastPrintableAscii = '~';

    // ==========================================================================================
    //  The expected values. Each is a hand-checkable consequence of the ramp - see the header.
    // ==========================================================================================

    /// <summary>
    /// A twenty-character random string under the preserved default flags from an unoffset ramp:
    /// the alphabet's own first twenty characters, ten digits then ten letters.
    /// </summary>
    private const string ExpectedDefaultString = "0123456789ABCDEFGHIJ";

    /// <summary>
    /// The same call from a ramp offset by <see cref="OffsetSeed"/>: twenty characters, none of
    /// them shared with <see cref="ExpectedDefaultString"/>.
    /// </summary>
    private const string ExpectedOffsetSeedString = "GHIJKLMNOPQRSTUVWXYZ";

    /// <summary>
    /// The bare GUID form from an unoffset ramp: octets 00 through 0F with the version nibble
    /// forced to 4 and the variant bits forced to the RFC 4122 pattern, hence <c>46</c> at the
    /// seventh octet and <c>88</c> at the ninth.
    /// </summary>
    private const string ExpectedBareGuid = "000102030405460788090a0b0c0d0e0f";

    /// <summary>
    /// The bracketed-without-separators GUID form - the one combination .NET publishes no standard
    /// format specifier for, so the port composes it by hand.
    /// </summary>
    private const string ExpectedBracketedBareGuid = "{000102030405460788090a0b0c0d0e0f}";

    /// <summary>The separated-without-brackets GUID form.</summary>
    private const string ExpectedSeparatedGuid = "00010203-0405-4607-8809-0a0b0c0d0e0f";

    /// <summary>
    /// The PRESERVED DEFAULT GUID form: bracketed AND separated, per guid.srf:L11 and
    /// enums.sru:L962.
    /// </summary>
    private const string ExpectedBracketedSeparatedGuid =
        "{00010203-0405-4607-8809-0a0b0c0d0e0f}";

    /// <summary>The default GUID form from a ramp offset by <see cref="OffsetSeed"/>.</summary>
    private const string ExpectedOffsetSeedGuid = "{10111213-1415-4617-9819-1a1b1c1d1e1f}";

    // ==========================================================================================
    //  Member data. Table-driven matrices, per the migration plan's stated test shape.
    // ==========================================================================================

    /// <summary>
    /// The alphabet-selection matrix: a flag value, the ramp offset that aims the draw at the class
    /// under examination, the character count, and the EXACT string the pair produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seven rows covering the five combinations the seam register calls for - each class alone, the
    /// preserved default, and all three together - plus two aimed rows that prove a class is
    /// genuinely reachable inside a union rather than merely permitted by it.
    /// </para>
    /// <para>
    /// EVERY FLAG VALUE IS AN <c>Enums.</c> REFERENCE, never a literal and never a local
    /// redeclaration. The all-three value is composed as a sum for the same reason the legacy
    /// composes its own defaults as sums: the sum states which classes are being asked for, where a
    /// bare <c>7</c> would state only the arithmetic.
    /// </para>
    /// <para>
    /// Rows 4 and 7 agree, and the agreement is informative rather than redundant: the preserved
    /// default and the all-three combination share their first twelve characters because both
    /// alphabets begin with the digits and then the letters. Row 6 is what separates them, by aiming
    /// the draw past both.
    /// </para>
    /// <para>
    /// Every size here is well below the point at which the unbiased-draw loop would reject a byte
    /// and resample, so each row's expectation is the alphabet's own run and needs no reasoning
    /// about rejection to check by hand.
    /// </para>
    /// </remarks>
    public static TheoryData<uint, byte, uint, string> AlphabetSelectionRows =>
        new()
        {
            // Digits alone [enums.sru:L954]. Twelve characters from a ten-character alphabet, so
            // the run wraps back to its start - which is itself worth pinning.
            { Enums.CRYPTO_RNDSTRING_NUMBER, 0, AlphabetProbeSize, "012345678901" },

            // Letters alone [enums.sru:L955]. Both cases, upper first.
            { Enums.CRYPTO_RNDSTRING_ALPHABET, 0, AlphabetProbeSize, "ABCDEFGHIJKL" },

            // Symbols alone [enums.sru:L956]: printable ASCII that is neither letter nor digit.
            { Enums.CRYPTO_RNDSTRING_SYMBOL, 0, AlphabetProbeSize, "!\"#$%&'()*+," },

            // The PRESERVED DEFAULT [enums.sru:L957, randomstring.srf:L11] - digits then letters.
            { Enums.CRYPTO_RNDSTRING_DEFAULT, 0, AlphabetProbeSize, "0123456789AB" },

            // The default again, aimed past its digits: the letters half is reachable.
            { Enums.CRYPTO_RNDSTRING_DEFAULT, LetterWindowSeed, AlphabetProbeSize, "ABCDEFGHIJKL" },

            // All three, aimed past digits and letters: the symbol class CONTRIBUTES when selected.
            {
                Enums.CRYPTO_RNDSTRING_NUMBER
                    + Enums.CRYPTO_RNDSTRING_ALPHABET
                    + Enums.CRYPTO_RNDSTRING_SYMBOL,
                SymbolWindowSeed,
                AlphabetProbeSize,
                "!\"#$%&'()*+,"
            },

            // All three, unoffset: the union still begins where the default begins.
            {
                Enums.CRYPTO_RNDSTRING_NUMBER
                    + Enums.CRYPTO_RNDSTRING_ALPHABET
                    + Enums.CRYPTO_RNDSTRING_SYMBOL,
                0,
                AlphabetProbeSize,
                "0123456789AB"
            },
        };

    /// <summary>
    /// The GUID formatting matrix: all four combinations of the two flag bits, with the exact text
    /// each produces from an unoffset ramp.
    /// </summary>
    /// <remarks>
    /// Four rows because two additive bits admit exactly four states, and all four are legal: the
    /// flags carry no validation, so neither bit is required. The bracketed-without-separators row
    /// is the one .NET has no standard format specifier for, which makes it the row most likely to
    /// regress silently if the hand composition were ever replaced by a specifier lookup.
    /// </remarks>
    public static TheoryData<uint, string> GuidFormattingRows =>
        new()
        {
            // Neither bit: the bare thirty-two-digit form.
            { 0, ExpectedBareGuid },

            // Brackets only [enums.sru:L960] - composed by hand; no .NET specifier produces it.
            { Enums.CRYPTO_GUID_INCLUDE_BRACKET, ExpectedBracketedBareGuid },

            // Separators only [enums.sru:L961].
            { Enums.CRYPTO_GUID_INCLUDE_SEPARATOR, ExpectedSeparatedGuid },

            // The PRESERVED DEFAULT: both [enums.sru:L962, guid.srf:L11].
            { Enums.CRYPTO_GUID_DEFAULT, ExpectedBracketedSeparatedGuid },
        };

    /// <summary>
    /// The blob-ramp matrix: a ramp offset and a requested byte count.
    /// </summary>
    /// <remarks>
    /// The two hundred and fifty-sixth and two hundred and fifty-seventh rows are the ones that
    /// matter beyond mere length: the ramp is an eight-bit value, so a request that reaches 256
    /// bytes exercises the truncation and a request past it exercises the wrap back to the start of
    /// the byte range. A provider that silently widened its accumulator would still return the
    /// right LENGTH and the wrong bytes, and only these two rows would notice.
    /// </remarks>
    public static TheoryData<byte, uint> BlobRampRows =>
        new()
        {
            { 0, 1 },
            { 0, 8 },
            { 0, DefaultStringSize },
            { 0, ByteValueSpan - 1 },
            { 0, ByteValueSpan },
            { 0, ByteValueSpan + 1 },
            { OffsetSeed, 8 },
            { OffsetSeed, ByteValueSpan },
        };

    // ==========================================================================================
    //  Helpers. Each restates the DOUBLE'S contract or an INDEPENDENT rule - never the provider's
    //  own logic, which would make an assertion agree with the code under test by construction.
    // ==========================================================================================

    /// <summary>
    /// The byte sequence <see cref="DeterministicEntropySource"/> yields for a fresh source: the
    /// ramp <c>(seed + position)</c> truncated to eight bits.
    /// </summary>
    /// <param name="seed">The offset the source was constructed with.</param>
    /// <param name="length">The number of bytes to project.</param>
    /// <returns>The expected bytes, in order.</returns>
    /// <remarks>
    /// This restates the DOUBLE'S documented sequence, which lives in this same test assembly, and
    /// nothing about the provider under test. Computing it rather than writing out a 256-byte
    /// literal keeps the rule visible and keeps a long byte literal out of a file that a secret
    /// scan has to read.
    /// </remarks>
    private static byte[] ExpectedRamp(byte seed, uint length)
    {
        byte[] ramp = new byte[length];
        for (uint position = 0; position < length; position++)
        {
            ramp[position] = (byte)((seed + position) % ByteValueSpan);
        }

        return ramp;
    }

    /// <summary>
    /// Whether a character belongs to at least one of the character classes a flag value selects.
    /// </summary>
    /// <param name="character">The character to classify.</param>
    /// <param name="flags">The additive flag value under examination.</param>
    /// <returns><see langword="true"/> when the character is permitted by the selection.</returns>
    /// <remarks>
    /// AN INDEPENDENT RULE, NOT A TRANSCRIPTION OF THE PROVIDER'S TABLE. The provider composes its
    /// alphabets from character RANGES; this predicate classifies with the framework's own ASCII
    /// category tests. Two different routes to the same claim is what makes the membership
    /// assertion worth making at all - a helper that rebuilt the provider's table would agree with
    /// it no matter what either of them did.
    /// </remarks>
    private static bool IsPermittedByFlags(char character, uint flags)
    {
        if ((flags & Enums.CRYPTO_RNDSTRING_NUMBER) != 0 && char.IsAsciiDigit(character))
        {
            return true;
        }

        if ((flags & Enums.CRYPTO_RNDSTRING_ALPHABET) != 0 && char.IsAsciiLetter(character))
        {
            return true;
        }

        return (flags & Enums.CRYPTO_RNDSTRING_SYMBOL) != 0 && IsPrintableAsciiSymbol(character);
    }

    /// <summary>
    /// Whether a character is printable ASCII and is neither a letter nor a digit.
    /// </summary>
    /// <param name="character">The character to classify.</param>
    /// <returns><see langword="true"/> when the character belongs to the symbol class.</returns>
    private static bool IsPrintableAsciiSymbol(char character) =>
        character >= FirstPrintableAscii
        && character <= LastPrintableAscii
        && !char.IsAsciiLetterOrDigit(character);

    /// <summary>
    /// Renders a member's signature as text, for the width census.
    /// </summary>
    /// <param name="member">The member to render.</param>
    /// <returns>The return type, name and parameter types, as in <c>String GenGuid(UInt32)</c>.</returns>
    private static string DescribeSignature(MethodInfo member) =>
        member.ReturnType.Name
        + " "
        + member.Name
        + "("
        + string.Join(",", member.GetParameters().Select(parameter => parameter.ParameterType.Name))
        + ")";

    /// <summary>
    /// The members <see cref="RandomProvider"/> declares, excluding compiler-generated accessors.
    /// </summary>
    /// <returns>The declared public instance members.</returns>
    private static MethodInfo[] PublishedMembers() =>
        [.. typeof(RandomProvider)
            .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
            .Where(member => !member.IsSpecialName)];

    // ==========================================================================================
    //  THE STRUCTURAL HALF - the seam exists, is registered, and is injectable
    // ==========================================================================================

    /// <summary>
    /// The composition root registers the entropy seam, and a test host replaces it with exactly
    /// one deterministic double.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CLAIM HERE IS ABOUT THE CONTAINER, WHICH IS WHY IT NEEDS A HOST. Direct construction can
    /// show that <see cref="RandomProvider"/> ACCEPTS a source; only resolution from the running
    /// application can show that the composition root SUPPLIES one and that a test can put its own
    /// in that place. docs/PARITY.md §5.2 draws exactly this line: "A seam is an injected
    /// dependency, not a comment and not a naming convention."
    /// </para>
    /// <para>
    /// EXACTLY ONE REGISTRATION MUST SURVIVE, and that is the substantive half of the assertion.
    /// Replacing the descriptor is not the same as adding a second one: with two registrations for
    /// the same service the resolved instance is decided by registration order, so a suite could
    /// pass or fail on which descriptor the container happened to pick last, and the double might
    /// or might not be the one in use. Counting the registrations is what rules that out.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheEntropySeam_IsRegisteredByTheCompositionRootAndReplaceableByExactlyOneDouble()
    {
        using SecurityAppFactory factory = new();

        IEntropySource resolved = factory.Services.GetRequiredService<IEntropySource>();

        // The registration the test host installed is the one the container hands out - not merely
        // a source of the right shape, but this exact instance, so a test can steer it afterwards.
        Assert.Same(factory.Entropy, resolved);
        Assert.IsType<DeterministicEntropySource>(resolved);

        // And it is the only one. See the remarks: a second descriptor would make resolution
        // order-dependent and the double's installation unprovable.
        Assert.Same(resolved, Assert.Single(factory.Services.GetServices<IEntropySource>()));
    }

    /// <summary>
    /// The provider the container hands out draws through the substituted seam, so every value it
    /// produces inside the running application is deterministic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the DI-level form of the bypass detector. The provider under assertion was
    /// constructed by the container from the application's own registration rather than by this
    /// test, so if the composition root ever supplied a differently-configured provider - or if the
    /// seam registration were added rather than replaced - the exact expectations below would fail
    /// here while still passing in the direct-construction theories. Both forms are needed.
    /// </para>
    /// <para>
    /// The counter is reset immediately before each draw rather than assumed to be at zero: the
    /// host has started by this point, and a suite that depended on nothing else in the application
    /// having drawn a byte would be asserting something about startup instead of about the seam.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheProviderResolvedFromTheContainer_DrawsThroughTheSubstitutedSeam()
    {
        using SecurityAppFactory factory = new();

        RandomProvider provider = factory.Services.GetRequiredService<RandomProvider>();

        factory.Entropy.Reset();
        Assert.Equal(ExpectedRamp(0, SeamProofBlobSize), provider.GenRandomBlob(SeamProofBlobSize));

        factory.Entropy.Reset();
        Assert.Equal(ExpectedDefaultString, provider.GenRandomString(DefaultStringSize));

        factory.Entropy.Reset();
        Assert.Equal(ExpectedBracketedSeparatedGuid, provider.GenGuid());
    }

    /// <summary>
    /// All three generators reflect the substituted byte pattern, so the seam is TOTAL rather than
    /// partial.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONE ASSERTION THAT WOULD CATCH A PARTIAL SEAM. A provider in which two of the three
    /// generators drew through the injected source and the third called a static randomness API
    /// would satisfy every per-member test written against the first two, and would quietly make
    /// any recorded workflow containing the third incomparable forever. Asserting all three against
    /// one installed double, in one test, is what makes the totality claim rather than three
    /// separate partial ones.
    /// </para>
    /// <para>
    /// The draw accounting beside each value is the second half of the detector: a member that
    /// ignored the source would leave the counters untouched, and a member that drew from the source
    /// AND then discarded the result in favour of a static call would move them while still
    /// returning the wrong value. The pair covers both shapes.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryGeneratorReflectsTheSubstitutedPattern_SoTheSeamIsTotal()
    {
        DeterministicEntropySource entropy = new();
        RandomProvider provider = new(entropy);

        Assert.Equal(
            ExpectedRamp(entropy.Seed, SeamProofBlobSize),
            provider.GenRandomBlob(SeamProofBlobSize));
        Assert.Equal(1L, entropy.FillCount);
        Assert.Equal((long)SeamProofBlobSize, entropy.BytesDrawn);

        entropy.Reset();

        Assert.Equal(ExpectedDefaultString, provider.GenRandomString(DefaultStringSize));
        Assert.Equal(1L, entropy.FillCount);
        Assert.Equal((long)DefaultStringSize, entropy.BytesDrawn);

        entropy.Reset();

        Assert.Equal(ExpectedBracketedSeparatedGuid, provider.GenGuid());
        Assert.Equal(1L, entropy.FillCount);
        Assert.Equal((long)GuidDrawInOctets, entropy.BytesDrawn);
    }

    /// <summary>
    /// Each generated value is a function of the BYTES DRAWN, not merely of the length asked for.
    /// </summary>
    /// <remarks>
    /// The complement of the test above. A provider that returned a constant of the right shape -
    /// or that derived its output from the requested length alone - would pass an exact-value
    /// assertion made against a single source, because the expectation would have been written from
    /// that same constant. Offsetting the ramp changes every drawn byte and must change every
    /// output, and BOTH outputs are pinned exactly rather than merely compared with each other.
    /// </remarks>
    [Fact]
    public void EachGeneratedValue_IsAFunctionOfTheDrawnBytesRatherThanOfTheRequestedLength()
    {
        DeterministicEntropySource unoffset = new();
        DeterministicEntropySource offset = new(OffsetSeed);

        RandomProvider fromUnoffset = new(unoffset);
        RandomProvider fromOffset = new(offset);

        Assert.Equal(ExpectedDefaultString, fromUnoffset.GenRandomString(DefaultStringSize));
        Assert.Equal(ExpectedOffsetSeedString, fromOffset.GenRandomString(DefaultStringSize));

        unoffset.Reset();
        offset.Reset();

        Assert.Equal(ExpectedBracketedSeparatedGuid, fromUnoffset.GenGuid());
        Assert.Equal(ExpectedOffsetSeedGuid, fromOffset.GenGuid());

        unoffset.Reset();
        offset.Reset();

        Assert.Equal(ExpectedRamp(0, 8), fromUnoffset.GenRandomBlob(8));
        Assert.Equal(ExpectedRamp(OffsetSeed, 8), fromOffset.GenRandomBlob(8));
    }

    /// <summary>
    /// The same call made twice against the same fixed source returns the identical value, which is
    /// the repeatability property the characterization model rests on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ramp advances across calls on purpose - a source that returned the same bytes to two
    /// consecutive draws would not behave like a real one, and would let an unbiased-draw loop spin
    /// - so the way to make two calls agree is to return the sequence to its start between them.
    /// That is what <see cref="DeterministicEntropySource.Reset"/> is for, and this test is the one
    /// that exercises it for each of the three generators.
    /// </para>
    /// <para>
    /// Each pair is asserted equal to the other AND equal to its written expectation. Equality
    /// alone would also hold for a provider that returned a constant, so the second assertion is
    /// what gives the first its meaning. docs/PARITY.md §5.3 makes the wider point that determinism
    /// is a property of the PAIR of recordings rather than of a run; this test establishes the
    /// prerequisite on the target side, which is that a run can be reproduced at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSameCallTwiceAgainstAResetSource_ReturnsTheIdenticalValue()
    {
        DeterministicEntropySource entropy = new();
        RandomProvider provider = new(entropy);

        byte[] firstBlob = provider.GenRandomBlob(SeamProofBlobSize);
        entropy.Reset();
        byte[] secondBlob = provider.GenRandomBlob(SeamProofBlobSize);

        Assert.Equal(firstBlob, secondBlob);
        Assert.Equal(ExpectedRamp(entropy.Seed, SeamProofBlobSize), secondBlob);

        entropy.Reset();
        string firstString = provider.GenRandomString(DefaultStringSize);
        entropy.Reset();
        string secondString = provider.GenRandomString(DefaultStringSize);

        Assert.Equal(firstString, secondString);
        Assert.Equal(ExpectedDefaultString, secondString);

        entropy.Reset();
        string firstGuid = provider.GenGuid();
        entropy.Reset();
        string secondGuid = provider.GenGuid();

        Assert.Equal(firstGuid, secondGuid);
        Assert.Equal(ExpectedBracketedSeparatedGuid, secondGuid);
    }

    /// <summary>
    /// The exact-value expectations in this file read the substituted seam rather than happening to
    /// agree with real randomness.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TEST THAT VALIDATES THE OTHER TESTS. Every expectation above would be worthless if it
    /// also held against the production source, because it would then be asserting nothing about
    /// substitution. So the same call is made twice: once through the double, where the ramp is
    /// required, and once through <see cref="CryptographicEntropySource"/>, where the ramp must NOT
    /// appear.
    /// </para>
    /// <para>
    /// THIS IS NOT A COIN FLIP, AND THE NUMBER IS WORTH WRITING DOWN. Thirty-two bytes drawn from a
    /// cryptographically secure generator reproduce a specified sequence with probability 2^-256.
    /// That is not a small risk to be tolerated; it is a quantity with no operational meaning, far
    /// below the probability of a hardware fault corrupting the comparison itself. The positive
    /// assertion is exact and unconditional; only the negative one is probabilistic, and it is
    /// bounded here rather than left implicit.
    /// </para>
    /// <para>
    /// The production source is also asserted to honour the seam's own contract - a full buffer of
    /// exactly the requested length - so this test covers the production end of the seam as
    /// behaviour rather than merely as a type that exists.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheExactValueExpectations_ReadTheSubstitutedSeamRatherThanRealRandomness()
    {
        byte[] expected = ExpectedRamp(0, SeamProofBlobSize);

        RandomProvider substituted = new(new DeterministicEntropySource());
        Assert.Equal(expected, substituted.GenRandomBlob(SeamProofBlobSize));

        RandomProvider production = new(new CryptographicEntropySource());
        byte[] drawn = production.GenRandomBlob(SeamProofBlobSize);

        // The production source honours the contract: the whole buffer, exactly as long as asked.
        Assert.Equal((int)SeamProofBlobSize, drawn.Length);

        // And it does not reproduce the double's sequence, so the expectation above is a statement
        // about the installed double and not about the provider's shape. See the remarks for the
        // 2^-256 bound.
        Assert.NotEqual(expected, drawn);
    }

    // ==========================================================================================
    //  EXACT DETERMINISM, PER GENERATOR - the random blob
    // ==========================================================================================

    /// <summary>
    /// A random blob is the drawn bytes verbatim and is exactly as long as was asked for.
    /// </summary>
    /// <param name="seed">The ramp offset the source is constructed with.</param>
    /// <param name="size">The requested byte count.</param>
    /// <remarks>
    /// <para>
    /// The blob is the seam's output with nothing applied to it - no encoding, no truncation, no
    /// padding - so it is the generator whose determinism is most directly readable, and the one
    /// against which the double's own contract is established for the two that follow.
    /// </para>
    /// <para>
    /// THE ROWS AT AND PAST 256 BYTES ARE THE INTERESTING ONES. The ramp is an eight-bit value, so a
    /// request reaching 256 bytes exercises its truncation and a request past it exercises the wrap
    /// back to the start of the byte range. A provider that widened the accumulator, or a double
    /// whose sequence stopped wrapping, would return the right LENGTH and the wrong bytes - a
    /// difference only these rows detect.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(BlobRampRows))]
    public void GenRandomBlob_ReturnsTheDrawnRampAndExactlyTheRequestedLength(byte seed, uint size)
    {
        DeterministicEntropySource entropy = new(seed);

        byte[] blob = new RandomProvider(entropy).GenRandomBlob(size);

        Assert.Equal((int)size, blob.Length);
        Assert.Equal(ExpectedRamp(seed, size), blob);
        Assert.Equal(1L, entropy.FillCount);
        Assert.Equal((long)size, entropy.BytesDrawn);
    }

    /// <summary>
    /// The largest length this service will generate is accepted, drawn in one request, and returned
    /// in full.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE INCLUSIVE EDGE OF THE PUBLISHED CAP. <see cref="RandomProvider.MaximumRequestedLength"/>
    /// is a service-level narrowing this port ADDS: the legacy parameter admits any 32-bit unsigned
    /// value, and in process that cost the caller only its own memory, whereas here a few dozen
    /// bytes of request can ask for gigabytes from the sole token issuer. The cap is part of the
    /// published contract rather than a per-deployment setting, so the boundary is asserted to be
    /// inclusive - a cap that silently rejected the value it advertises would make the contract's
    /// stated maximum untrue.
    /// </para>
    /// <para>
    /// The bytes are spot-checked at both ends rather than compared as a whole, because a one-mebibyte
    /// expectation would double this test's allocation to prove nothing the ramp theory above has not
    /// already proved. The final byte is 255 because the cap is an exact multiple of 256, which makes
    /// it a meaningful check rather than an arbitrary one.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLargestPermittedLength_IsAcceptedAndDrawnThroughTheSeamInOneRequest()
    {
        DeterministicEntropySource entropy = new();

        byte[] blob = new RandomProvider(entropy).GenRandomBlob(RandomProvider.MaximumRequestedLength);

        Assert.Equal((int)RandomProvider.MaximumRequestedLength, blob.Length);
        Assert.Equal(1L, entropy.FillCount);
        Assert.Equal((long)RandomProvider.MaximumRequestedLength, entropy.BytesDrawn);

        // The ramp's first byte, and its last: the cap is a whole number of 256-byte cycles, so the
        // final byte is the top of the byte range.
        Assert.Equal(0, blob[0]);
        Assert.Equal(byte.MaxValue, blob[^1]);
    }

    // ==========================================================================================
    //  EXACT DETERMINISM, PER GENERATOR - the random string and its character classes
    // ==========================================================================================

    /// <summary>
    /// Each flag combination yields the exact string the ramp selects from its alphabet, and every
    /// character produced belongs to a class the flags select.
    /// </summary>
    /// <param name="flags">The additive character-class selection.</param>
    /// <param name="seed">The ramp offset that aims the draw at the class under examination.</param>
    /// <param name="size">The requested character count.</param>
    /// <param name="expected">The exact string the pair produces.</param>
    /// <remarks>
    /// <para>
    /// TWO INDEPENDENT ASSERTIONS PER ROW, DELIBERATELY. The exact-value assertion is the
    /// determinism claim: the same flags and the same bytes must always produce this string. The
    /// membership assertion is the ALPHABET claim, and it is checked against a rule - the framework's
    /// own ASCII category tests - rather than against a transcription of the provider's table, so
    /// the two routes can genuinely disagree. An expectation derived from the code under test cannot.
    /// </para>
    /// <para>
    /// CHARACTERIZED OR ASSUMED, NOT PROVEN PARITY. Which characters each flag bit selects is
    /// unobservable from this repository: enums.sru:L954-L956 names three CLASSES and the closed
    /// binary holds the members of each. So these rows assert the port's documented choice - the ten
    /// digits, both cases of the letters, and every printable ASCII character that is neither -
    /// against which the behavioural oracle would be the only authority. The alternatives the port
    /// records for itself are an upper-only or lower-only letter class and a shorter "safe" symbol
    /// subset; nothing in the repository settles between them.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AlphabetSelectionRows))]
    public void EachFlagCombination_YieldsTheExactStringTheRampSelects(
        uint flags,
        byte seed,
        uint size,
        string expected)
    {
        DeterministicEntropySource entropy = new(seed);

        string drawn = new RandomProvider(entropy).GenRandomString(size, flags);

        Assert.Equal(expected, drawn);
        Assert.Equal((int)size, drawn.Length);

        foreach (char character in drawn)
        {
            Assert.True(
                IsPermittedByFlags(character, flags),
                "A drawn character fell outside the classes the flag value selects: " + character);
        }
    }

    /// <summary>
    /// The one-argument random-string forms apply the preserved legacy default, whose alphabet
    /// EXCLUDES the symbol class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A PRESERVED DEFAULT ASSERTED AS CORRECT, NOT AS AN OVERSIGHT. The default is the digits plus
    /// the letters and nothing else, and the repository states that twice independently:
    /// enums.sru:L957 names that sum <c>DEFAULT</c>, and randomstring.srf:L11 - the one-argument
    /// global function - spells the same sum out rather than deferring to the named constant. A
    /// narrowed alphabet reduces the entropy per character of every string this surface produces, so
    /// a caller needing a given strength must ask for more characters; that consequence is inherited
    /// along with the default, and correcting it here would be a behaviour change dressed as a fix.
    /// </para>
    /// <para>
    /// THREE SPELLINGS MUST AGREE, AND THEY ARE THE THREE A CALLER CAN REACH. The flagless overload
    /// [n_crypto.sru:L15], the global-function wrapper [randomstring.srf:L11], and the two-argument
    /// overload [n_crypto.sru:L16] passed the named default. Byte-identical output from all three is
    /// what makes them interchangeable, and is what would catch them drifting apart.
    /// </para>
    /// <para>
    /// The arithmetic is pinned first: the default is 3, the sum of the number and alphabet bits, and
    /// NOT 7. Stating that explicitly is what stops the assertions below from silently becoming
    /// vacuous if the constant were ever redefined.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOneArgumentRandomStringForms_ApplyTheSymbolExcludedPreservedDefault()
    {
        // enums.sru:L957 - the default is number + alphabet, and the symbol bit is not in it.
        Assert.Equal(
            Enums.CRYPTO_RNDSTRING_NUMBER + Enums.CRYPTO_RNDSTRING_ALPHABET,
            Enums.CRYPTO_RNDSTRING_DEFAULT);
        Assert.NotEqual(
            Enums.CRYPTO_RNDSTRING_NUMBER
                + Enums.CRYPTO_RNDSTRING_ALPHABET
                + Enums.CRYPTO_RNDSTRING_SYMBOL,
            Enums.CRYPTO_RNDSTRING_DEFAULT);
        Assert.Equal(0u, Enums.CRYPTO_RNDSTRING_DEFAULT & Enums.CRYPTO_RNDSTRING_SYMBOL);

        // The provider reaches that value through the annotated catalogue, and the two agree.
        Assert.Equal(Enums.CRYPTO_RNDSTRING_DEFAULT, LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT);

        DeterministicEntropySource entropy = new();
        RandomProvider provider = new(entropy);

        string flagless = provider.GenRandomString(DefaultStringSize);
        entropy.Reset();
        string wrapper = provider.RandomString(DefaultStringSize);
        entropy.Reset();
        string explicitlyDefaulted =
            provider.GenRandomString(DefaultStringSize, Enums.CRYPTO_RNDSTRING_DEFAULT);

        Assert.Equal(ExpectedDefaultString, flagless);
        Assert.Equal(ExpectedDefaultString, wrapper);
        Assert.Equal(ExpectedDefaultString, explicitlyDefaulted);

        // Exactly as many characters as were asked for. Implied by the equalities above, asserted
        // anyway because the length is a contract in its own right: a form that returned fewer
        // characters than requested would hand a caller weaker material than it believes it has.
        Assert.Equal((int)DefaultStringSize, flagless.Length);
        Assert.Equal((int)DefaultStringSize, wrapper.Length);

        // The substantive content of the default: no symbol character can appear under it.
        foreach (char character in flagless)
        {
            Assert.False(
                IsPrintableAsciiSymbol(character),
                "The preserved default alphabet excludes symbols, but one was produced: "
                    + character);
        }
    }

    // ==========================================================================================
    //  EXACT DETERMINISM, PER GENERATOR - the identifier and its four text shapes
    // ==========================================================================================

    /// <summary>
    /// Each combination of the two identifier flag bits yields the exact text shape it selects.
    /// </summary>
    /// <param name="flags">The additive formatting selection.</param>
    /// <param name="expected">The exact text the unoffset ramp produces under it.</param>
    /// <remarks>
    /// <para>
    /// Four rows because two additive bits admit four states and all four are legal - the flags
    /// carry no validation, so neither bit is required. Beside the exact value, the shape is checked
    /// against the flags as a RULE: brackets present exactly when the bracket bit is set, and either
    /// four group separators or none.
    /// </para>
    /// <para>
    /// CHARACTERIZED OR ASSUMED: THE CASE OF THE HEXADECIMAL DIGITS. The expectations are lowercase,
    /// which is what the framework's own identifier formatting emits; the platform API the legacy
    /// most likely called renders them uppercase. Nothing in this repository settles which the closed
    /// binary produced, so this is the port's documented choice and not measured parity - and it is
    /// called out because it affects every stored value while being invisible to any test that checks
    /// only the shape.
    /// </para>
    /// <para>
    /// The version and variant bits are visible in every expectation: the seventh octet reads
    /// <c>46</c> rather than <c>06</c> and the ninth reads <c>88</c> rather than <c>08</c>, because
    /// six of the 128 drawn bits are forced to the RFC 4122 version-4 and variant patterns. The other
    /// 122 are the ramp, which is why the rest of each expectation counts up.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(GuidFormattingRows))]
    public void EachIdentifierFlagCombination_YieldsTheExactTextShape(uint flags, string expected)
    {
        DeterministicEntropySource entropy = new();

        string identifier = new RandomProvider(entropy).GenGuid(flags);

        Assert.Equal(expected, identifier);

        // Sixteen octets, drawn once, whatever the formatting: the format is applied to the value,
        // never used to decide how much entropy to draw.
        Assert.Equal(1L, entropy.FillCount);
        Assert.Equal((long)GuidDrawInOctets, entropy.BytesDrawn);

        bool bracketed = (flags & Enums.CRYPTO_GUID_INCLUDE_BRACKET) != 0;
        bool separated = (flags & Enums.CRYPTO_GUID_INCLUDE_SEPARATOR) != 0;

        Assert.Equal(bracketed, identifier.StartsWith('{'));
        Assert.Equal(bracketed, identifier.EndsWith('}'));
        Assert.Equal(
            separated ? CanonicalGuidSeparatorCount : 0,
            identifier.Count(character => character == '-'));
    }

    /// <summary>
    /// The no-argument identifier forms apply the preserved legacy default, which is BOTH bracketed
    /// and separated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A PRESERVED DEFAULT ASSERTED AS CORRECT. The default carries the surrounding braces AND the
    /// group separators, stated twice independently: enums.sru:L962 names that sum <c>DEFAULT</c>,
    /// and guid.srf:L11 - the no-argument global function - composes the same sum explicitly. The
    /// bracketed, separated spelling travels in stored values and log records, so the bare form is
    /// not the "cleaner" default waiting to be adopted; changing it would alter text that existing
    /// data already contains.
    /// </para>
    /// <para>
    /// THREE SPELLINGS MUST AGREE: the flagless overload [n_crypto.sru:L17], the global-function
    /// wrapper [guid.srf:L11], and the flagged overload [n_crypto.sru:L18] passed the named default.
    /// </para>
    /// <para>
    /// One consequence of the default worth pinning, because a consumer meets it immediately: the
    /// bracketed, separated shape is the one the framework's own parser accepts, so a value produced
    /// by these forms round-trips. That is asserted here rather than assumed, since the bracketed
    /// shape WITHOUT separators - a legal combination of the same two bits - does not.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheNoArgumentIdentifierForms_ApplyTheBracketedAndSeparatedPreservedDefault()
    {
        // enums.sru:L962 - the default is bracket + separator, and neither bit is optional in it.
        Assert.Equal(
            Enums.CRYPTO_GUID_INCLUDE_BRACKET + Enums.CRYPTO_GUID_INCLUDE_SEPARATOR,
            Enums.CRYPTO_GUID_DEFAULT);
        Assert.Equal(
            Enums.CRYPTO_GUID_INCLUDE_BRACKET,
            Enums.CRYPTO_GUID_DEFAULT & Enums.CRYPTO_GUID_INCLUDE_BRACKET);
        Assert.Equal(
            Enums.CRYPTO_GUID_INCLUDE_SEPARATOR,
            Enums.CRYPTO_GUID_DEFAULT & Enums.CRYPTO_GUID_INCLUDE_SEPARATOR);

        // The provider reaches that value through the annotated catalogue, and the two agree.
        Assert.Equal(Enums.CRYPTO_GUID_DEFAULT, LegacyDefaults.GUID_FLAGS_DEFAULT);

        DeterministicEntropySource entropy = new();
        RandomProvider provider = new(entropy);

        string flagless = provider.GenGuid();
        entropy.Reset();
        string wrapper = provider.NewGuid();
        entropy.Reset();
        string explicitlyDefaulted = provider.GenGuid(Enums.CRYPTO_GUID_DEFAULT);

        Assert.Equal(ExpectedBracketedSeparatedGuid, flagless);
        Assert.Equal(ExpectedBracketedSeparatedGuid, wrapper);
        Assert.Equal(ExpectedBracketedSeparatedGuid, explicitlyDefaulted);

        // The substantive content of the default: brackets AND separators, both present.
        Assert.StartsWith("{", flagless, StringComparison.Ordinal);
        Assert.EndsWith("}", flagless, StringComparison.Ordinal);
        Assert.Equal(CanonicalGuidSeparatorCount, flagless.Count(character => character == '-'));

        // And the default shape round-trips through the framework's parser.
        Assert.True(Guid.TryParse(flagless, out Guid parsed));
        Assert.Equal(parsed, Guid.Parse(ExpectedSeparatedGuid));
    }

    // ==========================================================================================
    //  TYPE WIDTH - the mapping a literal reading of the legacy signatures gets wrong
    // ==========================================================================================

    /// <summary>
    /// Every scalar on the generator surface is the 32-bit unsigned type, and no wider or narrower
    /// one appears anywhere on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// POWERBUILDER AND C# USE THE SAME WORDS FOR DIFFERENT WIDTHS, which makes this the one
    /// signature property that a careful reader still gets wrong. PowerBuilder <c>ulong</c> and
    /// <c>unsignedlong</c> are 32-BIT unsigned, so <c>readonly ulong size</c> and
    /// <c>readonly ulong flags</c> at n_crypto.sru:L14-L18 are C# <c>uint</c> - never C#
    /// <c>ulong</c>, which is 64-bit, and never <c>int</c>, which is signed and halves the domain.
    /// The two legacy files even spell the same type differently, <c>unsignedlong</c> at
    /// randomstring.srf:L7 against <c>ulong</c> at guid.srf:L8, which is cosmetic and not a width
    /// difference.
    /// </para>
    /// <para>
    /// THE CENSUS IS THE DETECTOR, AND IT INCLUDES RETURN TYPES. A widening to <c>ulong</c> would
    /// keep every behavioural test passing - every value this suite passes fits in 32 bits - while
    /// silently admitting a domain the legacy never had. A narrowing to <c>int</c> would keep them
    /// passing too, while rejecting the upper half of the legacy domain. Only the declared types
    /// distinguish those, so they are asserted directly, as a whole set rather than one at a time.
    /// </para>
    /// <para>
    /// Seven signatures: five from the native declarations at n_crypto.sru:L14-L18, two from the
    /// global-function wrappers at randomstring.srf:L11 and guid.srf:L11. A blob returns bytes and
    /// every other member returns text, exactly as the legacy declares.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryScalarOnTheGeneratorSurface_IsTheThirtyTwoBitUnsignedType()
    {
        string[] expected =
        [
            "Byte[] GenRandomBlob(UInt32)",
            "String GenGuid()",
            "String GenGuid(UInt32)",
            "String GenRandomString(UInt32)",
            "String GenRandomString(UInt32,UInt32)",
            "String NewGuid()",
            "String RandomString(UInt32)",
        ];

        MethodInfo[] members = PublishedMembers();

        Assert.Equal(PublishedMemberCount, members.Length);

        string[] observed =
            [.. members.Select(DescribeSignature).OrderBy(signature => signature, StringComparer.Ordinal)];

        Assert.Equal(expected, observed);

        // Stated negatively as well, so that a NEW member added with the wrong width is caught by
        // this rule even before anyone updates the census above.
        Type[] wrongWidths =
            [typeof(int), typeof(long), typeof(ulong), typeof(short), typeof(ushort), typeof(byte)];

        Assert.All(
            members.SelectMany(member => member.GetParameters()),
            parameter => Assert.DoesNotContain(parameter.ParameterType, wrongWidths));
    }

    /// <summary>
    /// The top of the flag domain is accepted and masked rather than rejected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE BOUNDARY ROW THAT PROVES THE DOMAIN IS THE FULL 32 BITS. Passing the largest value the
    /// type can hold shows the parameter really is unsigned and really is that wide: a signed
    /// parameter could not carry it at all, and a guarded one would refuse it.
    /// </para>
    /// <para>
    /// It also pins the no-validation ruling at its extreme. The flags are an additive bitmask whose
    /// treatment of an unrecognised bit is unobservable in the closed binary, so the port ignores
    /// bits outside the defined ones rather than rejecting a value on a guess - narrowing the legacy
    /// contract on an assumption would be the larger change. At the top of the domain every
    /// undefined bit is set at once, so the masked selection is the whole of each family and the
    /// result must equal the call made with just those bits.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTopOfTheFlagDomain_IsAcceptedAndMaskedRatherThanRejected()
    {
        DeterministicEntropySource entropy = new();
        RandomProvider provider = new(entropy);

        // Every random-string bit set: the selection masks down to all three defined classes.
        string sprawling = provider.GenRandomString(AlphabetProbeSize, uint.MaxValue);
        entropy.Reset();
        string defined = provider.GenRandomString(
            AlphabetProbeSize,
            Enums.CRYPTO_RNDSTRING_NUMBER
                + Enums.CRYPTO_RNDSTRING_ALPHABET
                + Enums.CRYPTO_RNDSTRING_SYMBOL);

        Assert.Equal(defined, sprawling);
        Assert.Equal("0123456789AB", sprawling);

        // Every identifier bit set: the selection masks down to the two defined ones, which is the
        // same value the preserved default carries.
        entropy.Reset();
        string sprawlingIdentifier = provider.GenGuid(uint.MaxValue);

        Assert.Equal(ExpectedBracketedSeparatedGuid, sprawlingIdentifier);
    }

    /// <summary>
    /// A length above the published cap is refused before anything is allocated and before the seam
    /// is touched.
    /// </summary>
    /// <param name="size">The refused length.</param>
    /// <remarks>
    /// <para>
    /// THE TOP OF THE SIZE DOMAIN IS ASSERTED AS A REFUSAL RATHER THAN EXERCISED AS A REQUEST.
    /// <c>uint.MaxValue</c> characters would be a four-gigabyte allocation for a blob and a
    /// twelve-gigabyte one for a string, so a test that tried it would be measuring the host's
    /// memory rather than the provider's contract. The boundary is therefore established the only
    /// way it usefully can be: the request is named as refused, with the offending parameter
    /// identified.
    /// </para>
    /// <para>
    /// AND IT IS REFUSED BEFORE THE SEAM IS TOUCHED, which is what the draw counter establishes and
    /// is the substantive addition here. A guard placed after the allocation, or after a first draw,
    /// would still throw the same exception while having already done the work the guard exists to
    /// prevent - and against a real entropy source it would have consumed real entropy to do it.
    /// Zero draws is the assertion that distinguishes the two orderings.
    /// </para>
    /// <para>
    /// The refusal is a DELIBERATE NARROWING and is published as one: the legacy accepted any 32-bit
    /// length, but in process the caller and the allocation were the same program, so an
    /// over-large request harmed only its author. Across a network any authenticated caller can
    /// trigger it from a few dozen bytes of request, against the service that is the system's sole
    /// token issuer. The contract is narrowed with a defined error rather than widened with a hope,
    /// and the length is never quietly truncated to the cap - silently returning less material than
    /// was asked for is the one outcome a caller cannot detect.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(uint.MaxValue)]
    [InlineData(RandomProvider.MaximumRequestedLength + 1)]
    public void ALengthAboveThePublishedCap_IsRefusedBeforeAnythingIsAllocatedOrDrawn(uint size)
    {
        DeterministicEntropySource entropy = new();
        RandomProvider provider = new(entropy);

        ArgumentOutOfRangeException blobFailure =
            Assert.Throws<ArgumentOutOfRangeException>(() => provider.GenRandomBlob(size));

        Assert.Equal("size", blobFailure.ParamName);

        ArgumentOutOfRangeException stringFailure = Assert.Throws<ArgumentOutOfRangeException>(
            () => provider.GenRandomString(size, Enums.CRYPTO_RNDSTRING_DEFAULT));

        Assert.Equal("size", stringFailure.ParamName);

        // Neither refusal reached the seam. See the remarks: this is what separates a guard placed
        // before the work from one placed after it.
        Assert.Equal(0L, entropy.FillCount);
        Assert.Equal(0L, entropy.BytesDrawn);
    }

    // ==========================================================================================
    //  ZERO AND EDGE SIZES - characterized or assumed, because no oracle exists
    // ==========================================================================================

    /// <summary>
    /// A zero-length request returns an empty result and does not touch the seam.
    /// </summary>
    /// <param name="flags">The character-class selection the string form is asked for.</param>
    /// <remarks>
    /// <para>
    /// CHARACTERIZED OR ASSUMED, AND SAID SO. The legacy declares no minimum length and its bodies
    /// are inside the closed binary [n_crypto.sru:L8], so whether it returned nothing, threw, or did
    /// something else entirely for a zero-length request is UNOBSERVABLE from this repository. The
    /// port returns an empty result, on the reasoning that a request for no bytes is well defined
    /// and that rejecting it would narrow the legacy contract on a guess - the same reasoning applied
    /// to unrecognised flag bits. This test pins that decision so it cannot drift silently; it does
    /// not claim the closed binary agreed, and only the behavioural oracle could settle that.
    /// </para>
    /// <para>
    /// THE ZERO-DRAW ASSERTION IS THE PART THAT IS ABOUT THE SEAM. Returning empty is the visible
    /// behaviour; doing so WITHOUT drawing is the seam-level property, and it matters beyond
    /// tidiness. The double's sequence position is shared across calls, so a member that drew a
    /// block it then discarded would advance the position and change what a LATER call produced -
    /// turning a zero-length request into an invisible dependency between two unrelated draws, and
    /// making a recording depend on how many empty requests preceded it.
    /// </para>
    /// <para>
    /// The flagless string form is covered alongside the flagged one because it delegates, and a
    /// delegation that inserted work before forwarding would show up here and nowhere else.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ZeroLengthFlagRows))]
    public void AZeroLengthRequest_ReturnsEmptyWithoutTouchingTheSeam(uint flags)
    {
        DeterministicEntropySource entropy = new();
        RandomProvider provider = new(entropy);

        Assert.Empty(provider.GenRandomBlob(0));
        Assert.Empty(provider.GenRandomString(0, flags));
        Assert.Empty(provider.GenRandomString(0));
        Assert.Empty(provider.RandomString(0));

        Assert.Equal(0L, entropy.FillCount);
        Assert.Equal(0L, entropy.BytesDrawn);
    }

    /// <summary>
    /// The flag values the zero-length theory is exercised across: each defined class alone, the
    /// preserved default, all three together, and a value selecting none.
    /// </summary>
    /// <remarks>
    /// A zero length must win over the flags in every case, including the one where the flags select
    /// no class at all - the two empty arms are independent, and a provider that handled only one of
    /// them would still pass a single-row test.
    /// </remarks>
    public static TheoryData<uint> ZeroLengthFlagRows =>
        new()
        {
            0,
            Enums.CRYPTO_RNDSTRING_NUMBER,
            Enums.CRYPTO_RNDSTRING_ALPHABET,
            Enums.CRYPTO_RNDSTRING_SYMBOL,
            Enums.CRYPTO_RNDSTRING_DEFAULT,
            Enums.CRYPTO_RNDSTRING_NUMBER
                + Enums.CRYPTO_RNDSTRING_ALPHABET
                + Enums.CRYPTO_RNDSTRING_SYMBOL,
        };
}
