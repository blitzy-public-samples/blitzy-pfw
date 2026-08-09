// ==============================================================================================
//  RandomProviderTests - the characterization suite that pins
//  PowerFramework.Security.Crypto.RandomProvider, its entropy seam and both entropy sources
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/security-service/PowerFramework.Security/Crypto/RandomProvider.cs
//  BEHAVIOURAL ORACLE ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L14-L18   the five declarations
//                     ws_objects/pfw.crypto.pbl.src/randomstring.srf       the two wrappers
//                     ws_objects/pfw.crypto.pbl.src/guid.srf
//                     ws_objects/pfw.shared.pbl.src/enums.sru:L953-L962    the flags and defaults
//                     ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L652,L667,L682
//                     all READ ONLY per constraint C-C - read as specification, never edited
//
//  WHY THIS SUITE EXISTS AT ALL, AND WHAT IT CAN BE EVIDENCE OF (finding DP-7)
//  --------------------------------------------------------------------------------------------
//  A random-value generator is normally close to untestable, and this one is fully deterministic -
//  because every random byte it produces is drawn through an INJECTED SEAM, IEntropySource. That
//  design exists precisely so that a workflow whose output contains a generated value can be
//  masked and compared against a legacy recording; the refactor plan names these three members as
//  the primary non-determinism sources for characterization. Substituting the seam is therefore not
//  a testing trick, it is the feature.
//
//  Four of this file's behaviours are DECIDED RATHER THAN MEASURED, because the implementation
//  lives inside the closed pfw.dll and no C++ source exists in this repository. Every assertion
//  touching one says so:
//
//    DECISION D-R1 - the DEFAULTS the no-flag overloads apply. Inferred, not read, from two
//                    independent pieces of in-repository evidence: enums.sru NAMES the combination
//                    DEFAULT [L957, L962], and the global wrapper composes the identical
//                    combination when it has no flags to pass [randomstring.srf:L11, guid.srf:L11].
//    DECISION D-R2 - the three CHARACTER-CLASS ALPHABETS, and the empty-selection arm. The flags
//                    are named for classes, not for characters; the sets themselves are inside the
//                    binary.
//    DECISION D-R3 - the GUID octet source, the RFC 4122 bit forcing, the one text shape .NET has
//                    no specifier for, and the LETTER CASE of the hexadecimal digits.
//    DECISION D-R4 - that a generated blob is returned RAW and never encoded. This is the one of
//                    the four that the repository DOES evidence directly, at
//                    u_cst_tabpage_utility_crypto.sru:L652.
//
//  Two further behaviours are implementation choices with no legacy analogue at all, and are
//  asserted so they cannot regress silently: the UNBIASED rejection sampling, and the BOUNDED
//  resampling loop that reports a degenerate seam rather than hanging.
//
//  THE ASSERTIONS THAT WOULD BE WORTHLESS IF WRITTEN LOOSELY
//  --------------------------------------------------------------------------------------------
//  "The result has the requested length and contains only permitted characters" passes for a
//  generator that returns the same character every time, for one that is biased, and for one that
//  ignores its flags. Every case below therefore pins an EXACT string or an EXACT byte sequence
//  against a known entropy sequence, which is only possible because of the seam. The rejection-
//  sampling boundary cases are the sharpest of these: they choose a constant byte that sits one
//  side or the other of the acceptance window for a particular alphabet length, so they fail if the
//  window arithmetic changes by one.
//
//  C-F SELF-AUDIT: no key, credential, token, password or certificate appears. The byte sequences
//  here are counting ramps and constants, and the alphabets are algorithm data rather than secrets.
//  No generated value is asserted to be secret-quality, because that is not a property a unit test
//  can establish - the production seam draws from the operating system's generator and the
//  substituted one deliberately does not.
//
//  RULES POSITION: review_rules returns exactly one line, "No user rules provided.", so no
//  user-specified rule governs this file and none is invented. The enterprise-standard baseline
//  applies instead: deterministic, no I/O, no clock, no shared mutable state, every test
//  independent. No performance property is asserted, because the repository publishes none.
// ==============================================================================================

using System.Reflection;

using PowerFramework.Security.Crypto;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

/// <summary>
/// A deterministic entropy source that repeats a fixed byte sequence, so that every value the
/// provider derives from it is an exact, known function of that sequence.
/// </summary>
/// <remarks>
/// This is the substituted end of the determinism seam the production file exists to offer. It also
/// COUNTS its invocations, which is how the block-drawing behaviour is observed - a provider that
/// drew one byte per character would produce the same strings and a different call count.
/// </remarks>
internal sealed class SequencedEntropySource(params byte[] sequence) : IEntropySource
{
    private int _position;

    /// <summary>The number of times <see cref="Fill"/> has been called.</summary>
    internal int FillCount { get; private set; }

    /// <summary>The total number of bytes requested across every call.</summary>
    internal int BytesRequested { get; private set; }

    public void Fill(Span<byte> destination)
    {
        FillCount++;
        BytesRequested += destination.Length;

        for (int index = 0; index < destination.Length; index++)
        {
            destination[index] = sequence[_position % sequence.Length];
            _position++;
        }
    }
}

/// <summary>
/// A degenerate entropy source that returns one constant byte, used to drive the rejection-sampling
/// boundary and the bounded-resampling failure.
/// </summary>
internal sealed class ConstantEntropySource(byte value) : IEntropySource
{
    public void Fill(Span<byte> destination) => destination.Fill(value);
}

/// <summary>
/// Characterization tests for the random blob, random string and GUID surface, driven through the
/// injected entropy seam so that every expected value is exact.
/// </summary>
public sealed class RandomProviderTests
{
    /// <summary>A byte ramp 0, 1, 2 ... 255, so a drawn byte equals its own index.</summary>
    private static byte[] Ramp()
    {
        byte[] ramp = new byte[256];
        for (int index = 0; index < ramp.Length; index++)
        {
            ramp[index] = (byte)index;
        }

        return ramp;
    }

    /// <summary>A provider drawing from a ramp, which makes a drawn character equal to its own index.</summary>
    private static RandomProvider FromRamp() => new(new SequencedEntropySource(Ramp()));

    /// <summary>Sixteen distinct octets 0x10 .. 0x1F, for the GUID cases.</summary>
    private static byte[] GuidOctets()
    {
        byte[] octets = new byte[16];
        for (int index = 0; index < octets.Length; index++)
        {
            octets[index] = (byte)(0x10 + index);
        }

        return octets;
    }

    // ==========================================================================================
    //  The seam
    // ==========================================================================================

    /// <summary>
    /// The provider takes its entropy source as a required constructor dependency: there is no
    /// parameterless constructor and no defaulted parameter.
    /// </summary>
    /// <remarks>
    /// Either would make the UNSUBSTITUTED path the default one, which is precisely how a test seam
    /// stops being usable - a caller that forgot the argument would silently get a real generator and
    /// every recording taken through it would be uncomparable. Asserted structurally rather than by
    /// behaviour, because the failure is an API shape rather than a wrong value.
    /// </remarks>
    [Fact]
    public void TheProvider_RequiresItsEntropySourceRatherThanDefaultingOne()
    {
        ConstructorInfo[] constructors = typeof(RandomProvider).GetConstructors();

        Assert.Single(constructors);

        ParameterInfo[] parameters = constructors[0].GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(IEntropySource), parameters[0].ParameterType);
        Assert.False(parameters[0].HasDefaultValue);

        ArgumentNullException failure =
            Assert.Throws<ArgumentNullException>(() => new RandomProvider(null!));
        Assert.Equal("entropy", failure.ParamName);
    }

    /// <summary>
    /// The production entropy source implements the seam and draws real entropy.
    /// </summary>
    /// <remarks>
    /// The one place in the production file that touches the platform generator, which is what makes
    /// it the only part of this surface a test cannot pin exactly. Two independent draws differing is
    /// the strongest statement available here, and it is deliberately NOT dressed up as a randomness
    /// or entropy-quality assertion - that is not a property a unit test can establish. An empty
    /// request is accepted rather than rejected, matching the provider's own zero-length arms.
    /// </remarks>
    [Fact]
    public void TheProductionEntropySource_ImplementsTheSeamAndProducesVaryingBytes()
    {
        CryptographicEntropySource source = new();

        Assert.IsAssignableFrom<IEntropySource>(source);

        byte[] first = new byte[32];
        byte[] second = new byte[32];
        source.Fill(first);
        source.Fill(second);

        Assert.NotEqual(first, second);

        // An empty request is legal and does nothing.
        source.Fill(Span<byte>.Empty);
    }

    // ==========================================================================================
    //  GenRandomBlob - n_crypto.sru:L14
    // ==========================================================================================

    /// <summary>
    /// A generated blob is the drawn bytes VERBATIM, in order, and is never encoded.
    /// </summary>
    /// <remarks>
    /// DECISION D-R4, and the one of the four decisions the repository evidences DIRECTLY: the
    /// oracle's own demo wraps this call in the caller-driven encoder,
    /// <c>BlobToString(GenRandomBlob(10), Enums.CRYPTO_ENCODING_HEX)</c>
    /// [<c>u_cst_tabpage_utility_crypto.sru:L652</c>]. Pre-encoding here would double-encode that call
    /// site, so the member takes no dependency on the encoding provider at all. The sequence repeats
    /// deliberately - seven bytes from a three-byte source - so that both the ORDER and the wrap-around
    /// are visible.
    /// </remarks>
    [Fact]
    public void GenRandomBlob_ReturnsTheDrawnBytesVerbatimAndUnencoded()
    {
        RandomProvider provider = new(new SequencedEntropySource(0x07, 0x08, 0x09));

        byte[] blob = provider.GenRandomBlob(7);

        Assert.Equal([0x07, 0x08, 0x09, 0x07, 0x08, 0x09, 0x07], blob);
    }

    /// <summary>
    /// A blob of the requested length is produced for every length, and a ZERO length yields an empty
    /// array rather than throwing.
    /// </summary>
    /// <remarks>
    /// The legacy declares no minimum, and a request for no bytes is well defined; rejecting it would
    /// narrow the legacy contract on a guess. The parameter is the 32-bit unsigned PowerBuilder
    /// <c>ulong</c>, hence <see cref="uint"/>.
    /// </remarks>
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(16u)]
    [InlineData(1024u)]
    public void GenRandomBlob_ProducesExactlyTheRequestedLength(uint size)
    {
        byte[] blob = FromRamp().GenRandomBlob(size);

        Assert.Equal((int)size, blob.Length);
    }

    /// <summary>
    /// A length beyond what this platform can allocate as an array is a DEFINED argument error rather
    /// than an allocation failure from deep inside the runtime.
    /// </summary>
    /// <remarks>
    /// The legacy parameter admits lengths above <see cref="int.MaxValue"/>, which cannot be satisfied
    /// on this platform at all. The guard converts what would otherwise surface as an
    /// <see cref="OutOfMemoryException"/> into a named argument error at the boundary. It is not a
    /// policy limit: no minimum is imposed and no satisfiable length is rejected, which the
    /// requested-length cases above establish.
    /// </remarks>
    [Fact]
    public void GenRandomBlob_ReportsAnUnsatisfiableLengthAsAnArgumentError()
    {
        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => FromRamp().GenRandomBlob(uint.MaxValue));

        Assert.Equal("size", failure.ParamName);
    }

    /// <summary>
    /// Each call returns a FRESH array, so a caller mutating one result cannot affect another.
    /// </summary>
    /// <remarks>
    /// The returned array IS the generated material, so the production code deliberately does not zero
    /// it - the caller owns its lifetime and is the only party that can know when it is spent. That
    /// makes freshness load-bearing rather than incidental: a shared buffer would let one caller's
    /// zeroing blank another's key material.
    /// </remarks>
    [Fact]
    public void GenRandomBlob_ReturnsAFreshArrayEachTime()
    {
        RandomProvider provider = FromRamp();

        byte[] first = provider.GenRandomBlob(8);
        byte[] second = provider.GenRandomBlob(8);

        Assert.NotSame(first, second);

        Array.Clear(first);
        Assert.NotEqual(first, second);
    }

    // ==========================================================================================
    //  The three character-class alphabets - DECISION D-R2
    // ==========================================================================================

    /// <summary>
    /// The three flag bits are 1, 2 and 4, and the named default is the first two of them - symbols
    /// EXCLUDED.
    /// </summary>
    /// <remarks>
    /// Measured at <c>enums.sru:L953-L957</c>, asserted against literals so that no constant is
    /// compared to itself. The default is DECISION D-R1: <c>enums.sru</c> names that combination
    /// <c>DEFAULT</c> and the global wrapper composes the identical pair when it has no flags to pass
    /// [<c>randomstring.srf:L11</c>]. The exclusion of symbols is the substantive part - it NARROWS the
    /// alphabet and therefore reduces entropy per character, which is annotated in the production
    /// defaults catalogue as a weak-by-modern-standards default preserved rather than corrected.
    /// </remarks>
    [Fact]
    public void TheThreeFlagBits_AndTheNamedDefaultThatExcludesSymbols()
    {
        Assert.Equal(1u, Enums.CRYPTO_RNDSTRING_NUMBER);
        Assert.Equal(2u, Enums.CRYPTO_RNDSTRING_ALPHABET);
        Assert.Equal(4u, Enums.CRYPTO_RNDSTRING_SYMBOL);
        Assert.Equal(3u, Enums.CRYPTO_RNDSTRING_DEFAULT);
        Assert.Equal(3u, LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT);

        Assert.Equal(
            0u, LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT & Enums.CRYPTO_RNDSTRING_SYMBOL);
    }

    /// <summary>
    /// Each flag combination draws from exactly the character set DECISION D-R2 adopts, and from no
    /// other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three classes and the eight combinations, pinned exhaustively. A ramp source drawing 2000
    /// characters visits every position of every alphabet many times over, so the DISTINCT set observed
    /// is the alphabet itself rather than a sample of it. The counts are the substantive assertion:
    /// </para>
    /// <list type="bullet">
    /// <item>digits alone - 10 characters, unambiguous;</item>
    /// <item>letters alone - 52, BOTH CASES. There is no case sub-flag anywhere in the constant block,
    /// so a reading admitting only one case would have to invent a reason to prefer it. The
    /// alternatives are upper-only and lower-only, and a paired recording would settle which the
    /// closed binary uses;</item>
    /// <item>symbols alone - 32, every printable ASCII character that is neither a letter nor a digit.
    /// The complete self-describing complement of the other two classes, so it needs no arbitrary
    /// inclusion. The alternative is a shorter "safe" subset omitting quotes or shell metacharacters,
    /// which would be a judgement about downstream consumers that nothing in the repository
    /// authorises;</item>
    /// <item>and the four unions, up to all 94 printable ASCII characters.</item>
    /// </list>
    /// </remarks>
    [Theory]
    [InlineData(1u, 10, "0123456789")]
    [InlineData(2u, 52, "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz")]
    [InlineData(4u, 32, "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~")]
    [InlineData(3u, 62, "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz")]
    [InlineData(5u, 42, "!\"#$%&'()*+,-./0123456789:;<=>?@[\\]^_`{|}~")]
    public void EachFlagCombination_DrawsFromExactlyItsOwnAlphabet(
        uint flags,
        int expectedSize,
        string expectedOrdered)
    {
        string drawn = FromRamp().GenRandomString(2000, flags);

        string distinct = new(drawn.Distinct().OrderBy(character => character).ToArray());

        Assert.Equal(expectedSize, distinct.Length);
        Assert.Equal(expectedOrdered, distinct);
    }

    /// <summary>
    /// The widest combinations draw from 84 and from all 94 printable ASCII characters.
    /// </summary>
    /// <remarks>
    /// Separated from the theory above only because the 94-character expectation is long enough to
    /// obscure the cases beside it. The all-classes combination is the one the oracle's demo exercises
    /// directly, with <c>GenRandomString(50, NUMBER + ALPHABET + SYMBOL)</c>
    /// [<c>u_cst_tabpage_utility_crypto.sru:L667</c>], which makes it the combination with the most
    /// direct evidence behind it.
    /// </remarks>
    [Fact]
    public void TheWidestCombinations_CoverEightyFourAndAllNinetyFourPrintableCharacters()
    {
        string lettersAndSymbols = new(
            FromRamp().GenRandomString(4000, 6u).Distinct().OrderBy(character => character).ToArray());
        Assert.Equal(84, lettersAndSymbols.Length);
        Assert.DoesNotContain('0', lettersAndSymbols);

        string everything = new(
            FromRamp()
                .GenRandomString(
                    4000,
                    Enums.CRYPTO_RNDSTRING_NUMBER
                    + Enums.CRYPTO_RNDSTRING_ALPHABET
                    + Enums.CRYPTO_RNDSTRING_SYMBOL)
                .Distinct()
                .OrderBy(character => character)
                .ToArray());

        Assert.Equal(94, everything.Length);
        Assert.Equal('!', everything[0]);
        Assert.Equal('~', everything[^1]);

        // Every printable ASCII character and nothing else - no space, no control character.
        Assert.Equal(
            string.Concat(Enumerable.Range('!', '~' - '!' + 1).Select(code => (char)code)), everything);
    }

    /// <summary>
    /// The alphabet is composed in a FIXED order - digits, then letters, then symbols - so a given
    /// flag value draws the same character for the same byte across runs and processes.
    /// </summary>
    /// <remarks>
    /// The order has no effect on the DISTRIBUTION of a draw, because every character of the composed
    /// alphabet is equally likely. It is fixed so that the alphabet for a given flag value is STABLE,
    /// which a characterization recording depends on: a recording taken today has to reproduce
    /// tomorrow, and a composition order that varied - by hash-set iteration order, say - would break
    /// every stored comparison while every distribution property still held.
    /// </remarks>
    [Fact]
    public void TheAlphabetOrder_IsDigitsThenLettersThenSymbolsAndIsStable()
    {
        // A ramp source draws index 0, 1, 2 ... so the emitted string IS the alphabet's prefix.
        Assert.Equal("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcd", FromRamp().GenRandomString(40, 3u));
        Assert.Equal("0123456789!\"#$%&'()*", FromRamp().GenRandomString(20, 5u));
        Assert.Equal("ABCDEFGHIJ", FromRamp().GenRandomString(10, 6u));
        Assert.Equal("0123456789", FromRamp().GenRandomString(10, 7u));

        // And it is identical on a second, independent provider.
        Assert.Equal(FromRamp().GenRandomString(40, 3u), FromRamp().GenRandomString(40, 3u));
    }

    /// <summary>
    /// A flag value selecting NO character class yields an empty string, and undefined bits are IGNORED
    /// rather than rejected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DECISION D-R2's empty arm, and the no-validation ruling it rests on. How the closed binary
    /// treats a zero or unrecognised value is unobservable, so imposing validation on a caller-supplied
    /// flag would narrow the legacy contract on a guess. The alternative - silently substituting the
    /// default alphabet - was rejected as the WORSE guess, because it produces a plausible value rather
    /// than an obviously empty one.
    /// </para>
    /// <para>
    /// Note that <c>8</c> and <c>0x80000000</c> select nothing even though they are non-zero, because
    /// only the three defined bits survive the mask.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0u)]
    [InlineData(8u)]
    [InlineData(16u)]
    [InlineData(0x80000000u)]
    [InlineData(0xFFFFFFF8u)]
    public void AFlagValueSelectingNoClass_YieldsAnEmptyStringWithoutThrowing(uint flags)
    {
        Assert.Equal(string.Empty, FromRamp().GenRandomString(40, flags));
    }

    /// <summary>
    /// An undefined bit combined with a defined one is masked away, leaving the defined selection
    /// intact.
    /// </summary>
    /// <remarks>
    /// The other half of the no-validation ruling: an unrecognised bit neither rejects the call nor
    /// changes the alphabet. Asserted by equality with the same call made without the extra bit, so the
    /// masking is proven rather than merely surviving.
    /// </remarks>
    [Theory]
    [InlineData(1u, 9u)]
    [InlineData(3u, 3u + 8u)]
    [InlineData(7u, 0xFFFFFFFFu)]
    public void AnUndefinedBit_IsMaskedAwayWithoutChangingTheSelection(uint defined, uint withNoise)
    {
        Assert.Equal(FromRamp().GenRandomString(50, defined), FromRamp().GenRandomString(50, withNoise));
    }

    /// <summary>
    /// A ZERO length yields an empty string whatever the flags.
    /// </summary>
    /// <remarks>
    /// The second of the two independent empty arms - a zero length is a well-defined request for
    /// nothing, and an empty alphabet leaves nothing to draw. Neither is an error, and the two are
    /// separate conditions rather than one.
    /// </remarks>
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(7u)]
    public void AZeroLength_YieldsAnEmptyStringWhateverTheFlags(uint flags)
    {
        Assert.Equal(string.Empty, FromRamp().GenRandomString(0, flags));
    }

    // ==========================================================================================
    //  Determinism, block drawing and the unbiased draw
    // ==========================================================================================

    /// <summary>
    /// The same entropy sequence always produces the same string, which is what makes a generated
    /// value maskable in a paired recording.
    /// </summary>
    /// <remarks>
    /// The whole purpose of the seam. Asserted with an EXACT expected string rather than by comparing
    /// two providers, because comparing two providers would also pass for an implementation that
    /// ignored its entropy entirely and returned a constant.
    /// </remarks>
    [Fact]
    public void TheSameEntropySequence_AlwaysProducesTheSameString()
    {
        RandomProvider first = new(new SequencedEntropySource(1, 2, 3, 4, 5));
        RandomProvider second = new(new SequencedEntropySource(1, 2, 3, 4, 5));

        // The default alphabet is digits then letters, so bytes 1..5 select '1'..'5'.
        Assert.Equal("12345123451234512345", first.GenRandomString(20));
        Assert.Equal(first.GenRandomString(20), second.GenRandomString(20));
    }

    /// <summary>
    /// Bytes are drawn in BLOCKS sized to the characters still needed, not one at a time.
    /// </summary>
    /// <remarks>
    /// Keeps the number of calls into the seam proportional to the RESULT rather than to the number of
    /// draws, while remaining fully deterministic for a deterministic source. Asserted through the call
    /// count on the substituted source, which is the only way to observe it: a per-character
    /// implementation would produce the identical string from a ramp source.
    /// </remarks>
    [Fact]
    public void TheDraw_RequestsBytesInBlocksRatherThanOneAtATime()
    {
        SequencedEntropySource source = new(Ramp());
        RandomProvider provider = new(source);

        string drawn = provider.GenRandomString(50, 7u);

        Assert.Equal(50, drawn.Length);
        Assert.Equal(1, source.FillCount);
        Assert.Equal(50, source.BytesRequested);
    }

    /// <summary>
    /// THE DRAW IS UNBIASED: a byte at or above the acceptance window is DISCARDED and redrawn rather
    /// than folded in by a modulo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Taking a random byte modulo the alphabet length is the obvious approach and is subtly wrong
    /// whenever the length does not divide 256: the first <c>256 mod n</c> characters would each be
    /// drawn once more often than the rest, skewing the distribution towards the digits, which sort
    /// first. The acceptance window is the largest multiple of the alphabet length that fits in a byte.
    /// </para>
    /// <para>
    /// The boundary is pinned EXACTLY, one side at a time, using a constant source. For the ten digits
    /// the window is 250: a source returning 249 succeeds and selects <c>'9'</c> because 249 modulo 10
    /// is 9, while a source returning 250 is rejected on every draw. A one-off change to the window
    /// arithmetic flips one of these two cases.
    /// </para>
    /// <para>
    /// The bias this removes is UNOBSERVABLE from the repository - what the closed binary does is
    /// unknown - so this is an implementation detail chosen for correctness rather than a behavioural
    /// change, and it is recorded because "we chose the unbiased method" is exactly the kind of
    /// decision that looks like an accident when it is not written down.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDraw_DiscardsBytesOutsideTheUnbiasedAcceptanceWindow()
    {
        // Ten digits: the window is 250, so 249 is the last accepted byte.
        RandomProvider accepted = new(new ConstantEntropySource(249));
        Assert.Equal("9999", accepted.GenRandomString(4, Enums.CRYPTO_RNDSTRING_NUMBER));

        // 250 is the first rejected byte, so no draw ever completes.
        Action rejected = () =>
            _ = new RandomProvider(new ConstantEntropySource(250))
                .GenRandomString(1, Enums.CRYPTO_RNDSTRING_NUMBER);
        Assert.Throws<InvalidOperationException>(rejected);

        // Thirty-two symbols divide 256 exactly, so the window is the whole byte range and NOTHING is
        // ever discarded - which is why 255 succeeds here and fails for the 94-character alphabet.
        RandomProvider symbols = new(new ConstantEntropySource(255));
        Assert.Equal("~~~", symbols.GenRandomString(3, Enums.CRYPTO_RNDSTRING_SYMBOL));

        // Ninety-four characters: the window is 188, so 255 is rejected on every draw.
        Action tooWide = () =>
            _ = new RandomProvider(new ConstantEntropySource(255)).GenRandomString(1, 7u);
        Assert.Throws<InvalidOperationException>(tooWide);
    }

    /// <summary>
    /// A DEGENERATE entropy source is reported as a defined failure rather than hanging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rejection sampling is unbounded in principle: a source returning only values outside the window
    /// would loop forever. The narrowest window any of the seven non-empty alphabets produces is 188 of
    /// 256 values, so a genuine generator clears each round with probability of at least roughly 0.73
    /// and exhausting the permitted rounds is far beyond negligible.
    /// </para>
    /// <para>
    /// A bounded loop with a defined error is nevertheless the correct shape BECAUSE the source is
    /// injected: a substituted implementation returning a constant byte is entirely possible, and a
    /// service that hangs is worse in every way than one that reports the fault. The message is asserted
    /// to name the seam and, just as importantly, to reveal NOTHING about any drawn value.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADegenerateEntropySource_IsReportedRatherThanLoopingForever()
    {
        Action draw = () => _ = new RandomProvider(new ConstantEntropySource(255)).GenRandomString(8, 7u);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(draw);

        Assert.Contains("IEntropySource", failure.Message, StringComparison.Ordinal);
        Assert.Contains("resampling rounds", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("255", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unsatisfiable string length is the same defined argument error as for a blob.
    /// </summary>
    [Fact]
    public void GenRandomString_ReportsAnUnsatisfiableLengthAsAnArgumentError()
    {
        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => FromRamp().GenRandomString(uint.MaxValue, 1u));

        Assert.Equal("size", failure.ParamName);
    }

    /// <summary>
    /// The no-flag overload applies the preserved default, and the global wrapper agrees with it.
    /// </summary>
    /// <remarks>
    /// DECISION D-R1. The wrapper composes its classes EXPLICITLY as number plus alphabet
    /// [<c>randomstring.srf:L11</c>] rather than deferring to the named default constant, and the two
    /// are numerically identical by construction [<c>enums.sru:L957</c>] - so agreement is the expected
    /// result and the assertion is what would catch them drifting apart. Both are asserted to exclude
    /// symbols, which is the substantive content of the default.
    /// </remarks>
    [Fact]
    public void TheNoFlagOverloadAndTheGlobalWrapper_BothApplyTheDigitsAndLettersDefault()
    {
        Assert.Equal("0123456789AB", FromRamp().GenRandomString(12));
        Assert.Equal(FromRamp().GenRandomString(12), FromRamp().RandomString(12));
        Assert.Equal(
            FromRamp().GenRandomString(30, LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT),
            FromRamp().RandomString(30));

        // Symbols are excluded from both.
        string drawn = FromRamp().RandomString(2000);
        Assert.DoesNotContain(drawn, character => !char.IsAsciiLetterOrDigit(character));
    }

    // ==========================================================================================
    //  GUID - DECISION D-R3
    // ==========================================================================================

    /// <summary>
    /// The two GUID flag bits are 1 and 2, and the named default is both of them.
    /// </summary>
    /// <remarks>
    /// Measured at <c>enums.sru:L962</c>. DECISION D-R1 again: <c>enums.sru</c> names the combination
    /// <c>DEFAULT</c> and the global wrapper composes the identical pair when it has no flags to pass
    /// [<c>guid.srf:L11</c>].
    /// </remarks>
    [Fact]
    public void TheTwoGuidFlagBits_AndTheirNamedDefault()
    {
        Assert.Equal(1u, Enums.CRYPTO_GUID_INCLUDE_BRACKET);
        Assert.Equal(2u, Enums.CRYPTO_GUID_INCLUDE_SEPARATOR);
        Assert.Equal(3u, Enums.CRYPTO_GUID_DEFAULT);
        Assert.Equal(3u, LegacyDefaults.GUID_FLAGS_DEFAULT);
    }

    /// <summary>
    /// The four text shapes are produced by the two flag bits, and the shape .NET has no standard
    /// specifier for is composed by hand.
    /// </summary>
    /// <remarks>
    /// DECISION D-R3, PART THREE. .NET covers three of the four - <c>B</c> is bracketed AND hyphenated,
    /// <c>D</c> is hyphenated alone, <c>N</c> is bare - and there is NO standard specifier for brackets
    /// WITHOUT hyphens, which is exactly what the bracket flag alone asks for. Reaching for <c>P</c>
    /// instead would emit PARENTHESES, a different character and a silent defect, so the braces are
    /// asserted as characters rather than the shape being asserted only by its length.
    /// </remarks>
    [Theory]
    [InlineData(0u, "101112131415461798191a1b1c1d1e1f", 32)]
    [InlineData(1u, "{101112131415461798191a1b1c1d1e1f}", 34)]
    [InlineData(2u, "10111213-1415-4617-9819-1a1b1c1d1e1f", 36)]
    [InlineData(3u, "{10111213-1415-4617-9819-1a1b1c1d1e1f}", 38)]
    public void TheTwoGuidFlagBits_ProduceTheFourTextShapes(uint flags, string expected, int expectedLength)
    {
        RandomProvider provider = new(new SequencedEntropySource(GuidOctets()));

        string rendered = provider.GenGuid(flags);

        Assert.Equal(expected, rendered);
        Assert.Equal(expectedLength, rendered.Length);
        Assert.DoesNotContain('(', rendered);
        Assert.DoesNotContain(')', rendered);
    }

    /// <summary>
    /// Undefined GUID flag bits are IGNORED, so any value above the two defined bits renders as the
    /// combination those bits select.
    /// </summary>
    /// <remarks>
    /// MEASURED, and the mirror of the random-string ruling: bits outside the two known ones neither
    /// reject the call nor change the shape. The consequence is worth stating plainly because it is easy
    /// to assume otherwise - <c>4</c> renders as <c>0</c>, <c>7</c> renders as <c>3</c>, and
    /// <see cref="uint.MaxValue"/> renders as <c>3</c>.
    /// </remarks>
    [Theory]
    [InlineData(4u, 0u)]
    [InlineData(5u, 1u)]
    [InlineData(6u, 2u)]
    [InlineData(7u, 3u)]
    [InlineData(0xFFFFFFFFu, 3u)]
    [InlineData(0x80000000u, 0u)]
    public void UndefinedGuidFlagBits_AreIgnored(uint noisy, uint equivalent)
    {
        RandomProvider withNoise = new(new SequencedEntropySource(GuidOctets()));
        RandomProvider withoutNoise = new(new SequencedEntropySource(GuidOctets()));

        Assert.Equal(withoutNoise.GenGuid(equivalent), withNoise.GenGuid(noisy));
    }

    /// <summary>
    /// The sixteen octets come from the SEAM, and the RFC 4122 version and variant bits are then
    /// forced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DECISION D-R3, PARTS ONE AND TWO. <see cref="Guid.NewGuid"/> is the idiomatic call and is
    /// rejected because it is STATIC: a test could not make it return a chosen value, so any workflow
    /// whose output contained a generated GUID could never be masked and therefore never compared
    /// against a legacy recording. Drawing the octets through the seam costs a few lines and buys the
    /// whole parity model - and this test is only writable because of it.
    /// </para>
    /// <para>
    /// Six of the 128 bits are overwritten: the four-bit version field becomes 4, meaning "generated
    /// from random numbers", and the two-bit variant field becomes the RFC 4122 pattern. The remaining
    /// 122 bits are the drawn ones, which the assertion checks octet by octet. The reason is behaviour
    /// preservation rather than standards compliance for its own sake: the legacy almost certainly
    /// delegated to the platform GUID API, which produces version-4 variant-1 values, so leaving those
    /// bits random would emit text distinguishable from every GUID the legacy ever produced.
    /// </para>
    /// <para>
    /// The octets are read BIG-ENDIAN so that octet six and octet eight line up with the thirteenth and
    /// seventeenth digits of the text form. A little-endian reading would scatter them across other
    /// digits and the two assignments would be quietly meaningless - which is why the round trip below
    /// reads them back the same way.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheGuidOctetsComeFromTheSeam_WithOnlyTheVersionAndVariantBitsForced()
    {
        byte[] drawn = GuidOctets();
        RandomProvider provider = new(new SequencedEntropySource(drawn));

        Guid parsed = Guid.Parse(provider.GenGuid(Enums.CRYPTO_GUID_INCLUDE_SEPARATOR));
        byte[] roundTripped = parsed.ToByteArray(bigEndian: true);

        // Octet 6 carries the version nibble: 0x16 becomes 0x46. Octet 8 carries the variant bits:
        // 0x18 becomes 0x98. Every other octet is the drawn byte, unchanged.
        Assert.Equal(
            [
                0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x46, 0x17,
                0x98, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F,
            ],
            roundTripped);

        Assert.Equal(4, roundTripped[6] >> 4);
        Assert.Equal(0b10, roundTripped[8] >> 6);

        // The low nibble of octet 6 and the low six bits of octet 8 are still the drawn bits.
        Assert.Equal(drawn[6] & 0x0F, roundTripped[6] & 0x0F);
        Assert.Equal(drawn[8] & 0x3F, roundTripped[8] & 0x3F);
    }

    /// <summary>
    /// The version and variant bits are forced whatever the drawn octets are, including all zeroes and
    /// all ones.
    /// </summary>
    /// <remarks>
    /// The two extremes are the cases where "clear then set" and "set only" diverge: an all-ones source
    /// proves the version nibble is CLEARED before the marker is applied, and an all-zeroes source
    /// proves the marker is applied at all. Either mistake alone produces valid-looking GUIDs.
    /// </remarks>
    [Fact]
    public void TheVersionAndVariantBits_AreForcedForEveryPossibleDrawnOctet()
    {
        RandomProvider zeroes = new(new ConstantEntropySource(0x00));
        Assert.Equal(
            "00000000-0000-4000-8000-000000000000",
            zeroes.GenGuid(Enums.CRYPTO_GUID_INCLUDE_SEPARATOR));

        RandomProvider ones = new(new ConstantEntropySource(0xFF));
        Assert.Equal(
            "ffffffff-ffff-4fff-bfff-ffffffffffff",
            ones.GenGuid(Enums.CRYPTO_GUID_INCLUDE_SEPARATOR));
    }

    /// <summary>
    /// The hexadecimal digits are LOWER CASE.
    /// </summary>
    /// <remarks>
    /// DECISION D-R3, PART FOUR, and a genuine OPEN OBSERVABLE. .NET's GUID formatting emits lower
    /// case; the Windows GUID-to-string API renders UPPER case, and the legacy ran on Windows - so a
    /// paired recording would settle it. Called out rather than quietly assumed because it affects every
    /// stored value and every recorded comparison while being invisible to any test that only checks the
    /// shape. This assertion is the marker that would have to change if a capture disagreed.
    /// </remarks>
    [Fact]
    public void TheGuidHexadecimalDigits_AreLowerCase()
    {
        RandomProvider provider = new(new SequencedEntropySource(GuidOctets()));

        string rendered = provider.GenGuid(0u);

        Assert.Equal(rendered.ToLowerInvariant(), rendered);
        Assert.Contains('a', rendered);
        Assert.DoesNotContain('A', rendered);
    }

    /// <summary>
    /// Sixteen octets are drawn per GUID, in a single request.
    /// </summary>
    /// <remarks>
    /// The buffer is stack-allocated in the production code, so it never reaches the heap and cannot be
    /// moved by a collector or left behind for the next allocation to read. Asserted through the call
    /// count and byte total, which is the only observable evidence that exactly one GUID's worth of
    /// entropy is consumed.
    /// </remarks>
    [Fact]
    public void EachGuid_DrawsExactlySixteenOctetsInOneRequest()
    {
        SequencedEntropySource source = new(GuidOctets());
        RandomProvider provider = new(source);

        _ = provider.GenGuid(3u);

        Assert.Equal(1, source.FillCount);
        Assert.Equal(16, source.BytesRequested);
    }

    /// <summary>
    /// The no-flag overload applies the preserved default, and the global wrapper agrees with it.
    /// </summary>
    /// <remarks>
    /// DECISION D-R1. As with the random string, the wrapper composes its flags EXPLICITLY as bracket
    /// plus separator [<c>guid.srf:L11</c>] rather than deferring to the named default, and the two are
    /// the identical value [<c>enums.sru:L962</c>]. The member is named <c>NewGuid</c> rather than
    /// <c>Guid</c> deliberately: a member named <c>Guid</c> would read as a reference to
    /// <see cref="System.Guid"/> at every call site.
    /// </remarks>
    [Fact]
    public void TheNoFlagGuidOverloadAndTheGlobalWrapper_BothApplyTheBracketedHyphenatedDefault()
    {
        RandomProvider viaDefault = new(new SequencedEntropySource(GuidOctets()));
        RandomProvider viaWrapper = new(new SequencedEntropySource(GuidOctets()));
        RandomProvider viaFlags = new(new SequencedEntropySource(GuidOctets()));

        const string expected = "{10111213-1415-4617-9819-1a1b1c1d1e1f}";

        Assert.Equal(expected, viaDefault.GenGuid());
        Assert.Equal(expected, viaWrapper.NewGuid());
        Assert.Equal(expected, viaFlags.GenGuid(LegacyDefaults.GUID_FLAGS_DEFAULT));
    }

    /// <summary>
    /// Three of the four shapes parse back to the same GUID.
    /// </summary>
    /// <remarks>
    /// A shape no parser accepted would look right in a log and fail on the way back in, so each shape's
    /// parseability is a property worth knowing. Three of the four are parseable, and the fourth is
    /// characterized separately below.
    /// </remarks>
    [Theory]
    [InlineData(0u)]
    [InlineData(2u)]
    [InlineData(3u)]
    public void TheParseableShapes_ParseBackToTheSameGuid(uint flags)
    {
        RandomProvider provider = new(new SequencedEntropySource(GuidOctets()));
        RandomProvider reference = new(new SequencedEntropySource(GuidOctets()));

        Guid parsed = Guid.Parse(provider.GenGuid(flags));
        Guid canonical = Guid.Parse(reference.GenGuid(Enums.CRYPTO_GUID_INCLUDE_SEPARATOR));

        Assert.Equal(canonical, parsed);
    }

    /// <summary>
    /// MEASURED CONSEQUENCE OF DECISION D-R3: the bracketed-WITHOUT-hyphens shape cannot be parsed back
    /// by .NET at all - not by <see cref="Guid.Parse(string)"/> and not by any standard format
    /// specifier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the same shape .NET has no specifier to PRODUCE, and it turns out to be one .NET cannot
    /// CONSUME either. <see cref="Guid.Parse(string)"/> sees the opening brace and commits to the
    /// <c>X</c> layout - <c>{0x...,0x...,{0x..,..}}</c> - then fails on the missing <c>0x</c> prefix; and
    /// <see cref="Guid.TryParseExact(string,string,out Guid)"/> rejects it under every one of
    /// <c>N</c>, <c>D</c>, <c>B</c>, <c>P</c> and <c>X</c>. The hyphenated bracketed form parses, and so
    /// do both unbracketed forms, so this one shape is alone in being write-only.
    /// </para>
    /// <para>
    /// NOT A DEFECT TO FIX. The shape is exactly what the bracket flag WITHOUT the separator flag asks
    /// for [<c>enums.sru:L962</c>], and the legacy surface offers that combination
    /// [<c>n_crypto.sru:L18</c>], so emitting something else - parentheses, or hyphens the caller did
    /// not ask for - would be a behaviour change. What a CONSUMER must do is strip the braces before
    /// parsing, which the second half of this test demonstrates so the workaround is discoverable from
    /// the suite rather than rediscovered at a call site.
    /// </para>
    /// <para>
    /// Recorded here because it is invisible to every other assertion in this file: the shape's text is
    /// asserted exactly elsewhere, and text equality says nothing about whether anything can read it
    /// back.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheBracketedShapeWithoutHyphens_CannotBeParsedBackByDotNet()
    {
        RandomProvider provider = new(new SequencedEntropySource(GuidOctets()));

        string bracketedBare = provider.GenGuid(Enums.CRYPTO_GUID_INCLUDE_BRACKET);

        Assert.Equal("{101112131415461798191a1b1c1d1e1f}", bracketedBare);
        Assert.False(Guid.TryParse(bracketedBare, out _));

        foreach (string specifier in new[] { "N", "D", "B", "P", "X" })
        {
            Assert.False(
                Guid.TryParseExact(bracketedBare, specifier, out _),
                $"Format specifier {specifier} unexpectedly accepted the bracketed bare shape.");
        }

        // The workaround a consumer needs: strip the braces, then parse.
        RandomProvider reference = new(new SequencedEntropySource(GuidOctets()));
        Guid canonical = Guid.Parse(reference.GenGuid(Enums.CRYPTO_GUID_INCLUDE_SEPARATOR));

        Assert.Equal(canonical, Guid.Parse(bracketedBare.Trim('{', '}')));
    }

    // ==========================================================================================
    //  The census
    // ==========================================================================================

    /// <summary>
    /// The type publishes exactly seven members: five from the native declarations and two from the
    /// global wrappers.
    /// </summary>
    /// <remarks>
    /// <c>n_crypto.sru:L14-L18</c> declares <c>GenRandomBlob</c>, both <c>GenRandomString</c>
    /// overloads and both <c>GenGUID</c> overloads; <c>randomstring.srf</c> and <c>guid.srf</c> supply
    /// the two wrappers. SEVEN RATHER THAN EIGHT is deliberate and is worth the assertion: each
    /// wrapper's two-argument sibling adds only a lazy-initialization guard and the native call, both
    /// of which constructor injection and the flagged overloads already cover
    /// [<c>randomstring.srf:L14-L15</c>, <c>guid.srf:L14-L15</c>].
    /// </remarks>
    [Fact]
    public void TheType_PublishesExactlyTheSevenMembersTheOracleImplies()
    {
        MethodInfo[] members = typeof(RandomProvider)
            .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
            .Where(method => !method.IsSpecialName)
            .ToArray();

        Assert.Equal(7, members.Length);
        Assert.Single(members, method => method.Name == "GenRandomBlob");
        Assert.Equal(2, members.Count(method => method.Name == "GenRandomString"));
        Assert.Equal(2, members.Count(method => method.Name == "GenGuid"));
        Assert.Single(members, method => method.Name == "RandomString");
        Assert.Single(members, method => method.Name == "NewGuid");

        // The parameter widths follow the 32-bit unsigned PowerBuilder types.
        Assert.All(
            members.SelectMany(method => method.GetParameters()),
            parameter => Assert.Equal(typeof(uint), parameter.ParameterType));

        // Sealed, and the alphabet table is the only static state - immutable, and read-only.
        Assert.True(typeof(RandomProvider).IsSealed);
        Assert.DoesNotContain(
            typeof(RandomProvider)
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            field => !field.IsLiteral && !field.IsInitOnly);
    }
}
