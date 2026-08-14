// ==============================================================================================
//  EncodingProviderTests - the characterization suite that pins
//  PowerFramework.Security.Crypto.EncodingProvider
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/security-service/PowerFramework.Security/Crypto/EncodingProvider.cs
//  BEHAVIOURAL ORACLE ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L11-L13   the three declarations
//                     ws_objects/pfw.crypto.pbl.src/base64encode.srf       the four wrappers
//                     ws_objects/pfw.crypto.pbl.src/base64decode.srf
//                     ws_objects/pfw.crypto.pbl.src/hexencode.srf
//                     ws_objects/pfw.crypto.pbl.src/hexdecode.srf
//                     ws_objects/pfw.shared.pbl.src/enums.sru:L924-L925    the two encodings
//                     ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L419,L434
//                     all READ ONLY per constraint C-C - read as specification, never edited
//
//  WHAT THIS SUITE CAN AND CANNOT BE EVIDENCE OF (finding DP-7)
//  --------------------------------------------------------------------------------------------
//  n_crypto is a PBNI class over the closed pfw.dll: the .sru declares the prototypes and the
//  implementation is unreadable. Two OBSERVABLES are therefore genuinely open, and the production
//  file records them as decisions rather than measurements:
//
//    DECISION E1 - the hexadecimal LETTER CASE. Upper case is adopted. If a paired legacy
//                  recording shows lower case, the single-line correction is documented in the
//                  production file. The assertions below pin UPPER CASE so that the current
//                  behaviour cannot drift silently before that capture exists - they do NOT claim
//                  the native agrees.
//    DECISION E2 - the Base64 SHAPE: standard alphabet, WITH padding, no line breaks. Corroborated
//                  by an in-repository consumer at
//                  ws_objects/pfw.net.http.ext.pbl.src/n_cst_alipay.sru:L145, which is evidence
//                  but not proof.
//    DECISION E3 - the boolean that BlobReverse returns. This port has no false arm, because the
//                  only failure the legacy boolean could have carried is a native-interface handle
//                  fault with no managed analogue.
//
//  Everything else here IS traceable: the seven-member census, the two-value encoding domain, the
//  delegation of each wrapper to the dispatch member, and the direction of each operation are all
//  readable in the oracle exports cited above.
//
//  THE TOLERANCE ASYMMETRY IS THE MOST VALUABLE THING PINNED BELOW
//  --------------------------------------------------------------------------------------------
//  Base64 decoding IGNORES white space while hexadecimal decoding REJECTS it, and hexadecimal
//  decoding accepts any letter case while Base64 does not accept the URL-safe alphabet. None of
//  that is behaviour added by the port - all four are properties of the chosen primitives - and
//  none of it is symmetric, so it is exactly the kind of thing a later "harmonise the two
//  directions" change would break while every round-trip test still passed. The white-space
//  tolerance is load-bearing: it is what lets encoded text survive the trip through a text control
//  that the oracle's own caller performs [u_cst_tabpage_utility_crypto.sru:L419, :L434].
//
//  C-F SELF-AUDIT: every payload here is a short synthetic byte sequence or an obviously invented
//  string. No key, credential, token, password, certificate or connection string appears - which
//  matters in this file specifically, because this type sees whatever a caller is encoding, and the
//  production type deliberately holds no logger for the same reason.
//
// ==============================================================================================

using System.Reflection;

using PowerFramework.Security.Crypto;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Characterization tests for the blob-to-text surface: both directions of both encodings, the
/// four global-function wrappers, the in-place buffer reversal and the uniform failure shapes.
/// </summary>
public sealed class EncodingProviderTests
{
    /// <summary>The system under test. Stateless, so a fresh instance per test costs nothing.</summary>
    private readonly EncodingProvider _encoding = new();

    // ==========================================================================================
    //  The census and the encoding domain
    // ==========================================================================================

    /// <summary>
    /// The type publishes exactly seven members: three from the native declarations and four from
    /// the global wrappers.
    /// </summary>
    /// <remarks>
    /// <c>n_crypto.sru:L11-L13</c> declares <c>StringToBlob</c>, <c>BlobToString</c> and
    /// <c>BlobReverse</c>; <c>base64encode.srf</c>, <c>base64decode.srf</c>, <c>hexencode.srf</c> and
    /// <c>hexdecode.srf</c> supply the four wrappers. Seven is the whole census: no third encoding, no
    /// URL-safe variant, no convenience overload and no Try-shaped alternative is offered, the last
    /// because it would silence input the legacy surface rejected.
    /// </remarks>
    [Fact]
    public void TheType_PublishesExactlyTheSevenMembersTheOracleDeclares()
    {
        string[] members = typeof(EncodingProvider)
            .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Base64Decode", "Base64Encode", "BlobReverse", "BlobToString",
                "HexDecode", "HexEncode", "StringToBlob",
            ],
            members);

        // No Try-shaped alternative, and no static entry point.
        Assert.DoesNotContain("TryStringToBlob", members);
        Assert.Empty(
            typeof(EncodingProvider)
                .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Static));
    }

    /// <summary>
    /// The published encoding set is exactly two values, Base64 as 0 and hexadecimal as 1.
    /// </summary>
    /// <remarks>
    /// Measured at <c>enums.sru:L924-L925</c>. Asserted against literals rather than against the
    /// constants themselves, because comparing a constant to itself would pass for any value.
    /// </remarks>
    [Fact]
    public void ThePublishedEncodings_AreBase64AsZeroAndHexadecimalAsOne()
    {
        Assert.Equal(0L, Enums.CRYPTO_ENCODING_BASE64);
        Assert.Equal(1L, Enums.CRYPTO_ENCODING_HEX);
    }

    /// <summary>
    /// The type is stateless and thread-safe: no field, no cache and no static mutable state.
    /// </summary>
    /// <remarks>
    /// What makes it safe to register with any container lifetime and to share across concurrent
    /// requests. The ordinary parameterless constructor is declared explicitly in the production file
    /// so the shape is unambiguous, and it deliberately does not reproduce the legacy's
    /// lazy-construction guard [<c>base64encode.srf:L10</c>] - a container-managed instance has
    /// nothing to check before use.
    /// </remarks>
    [Fact]
    public void TheType_IsStatelessAndSealedWithAnOrdinaryConstructor()
    {
        Type type = typeof(EncodingProvider);

        Assert.True(type.IsSealed);
        Assert.True(type.IsPublic);
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.DoesNotContain(
            type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            field => !field.IsLiteral && !field.IsInitOnly);

        ConstructorInfo[] constructors = type.GetConstructors();
        Assert.Single(constructors);
        Assert.Empty(constructors[0].GetParameters());
    }

    /// <summary>
    /// This type is NOT <c>System.Text.EncodingProvider</c>, which is an unrelated Base Class Library
    /// type of the same simple name.
    /// </summary>
    /// <remarks>
    /// The collision is real and is documented in the production header: a file importing both
    /// <c>System.Text</c> and this namespace cannot use the simple name and fails with CS0104, so the
    /// character encoding has to be qualified at its use site or aliased. Asserted so the hazard is
    /// discoverable from the test suite as well as from the header.
    /// </remarks>
    [Fact]
    public void TheType_IsNotTheBaseClassLibraryTypeOfTheSameSimpleName()
    {
        Assert.Equal("PowerFramework.Security.Crypto", typeof(EncodingProvider).Namespace);
        Assert.NotEqual(typeof(System.Text.EncodingProvider), typeof(EncodingProvider));
        Assert.False(typeof(System.Text.EncodingProvider).IsAssignableFrom(typeof(EncodingProvider)));
    }

    // ==========================================================================================
    //  The encode direction
    // ==========================================================================================

    /// <summary>
    /// Base64 encoding emits the standard padded alphabet.
    /// </summary>
    /// <remarks>
    /// DECISION E2. The padding is the part worth asserting on every length residue: a one-byte
    /// payload pads with two characters, a two-byte payload with one and a three-byte payload with
    /// none, so the three cases together prove the padding is emitted rather than stripped.
    /// </remarks>
    [Theory]
    [InlineData(new byte[0], "")]
    [InlineData(new byte[] { 0x00 }, "AA==")]
    [InlineData(new byte[] { 0xFF }, "/w==")]
    [InlineData(new byte[] { 0x41, 0x42 }, "QUI=")]
    [InlineData(new byte[] { 0x01, 0x02, 0x03 }, "AQID")]
    [InlineData(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, "3q2+7w==")]
    public void Base64Encode_EmitsTheStandardPaddedAlphabet(byte[] data, string expected) =>
        Assert.Equal(expected, _encoding.Base64Encode(data));

    /// <summary>
    /// Base64 encoding inserts NO line break, however long the payload.
    /// </summary>
    /// <remarks>
    /// DECISION E2's second half. The primitive's formatting options default to none, so the result is
    /// single-line - but a change to the line-breaking variant would still round-trip through the
    /// matching decoder, so only an explicit assertion catches it. A payload well past the 76-character
    /// wrapping width of the classic MIME convention is used deliberately.
    /// </remarks>
    [Fact]
    public void Base64Encode_NeverInsertsALineBreakEvenForALongPayload()
    {
        byte[] data = new byte[512];
        for (int index = 0; index < data.Length; index++)
        {
            data[index] = (byte)index;
        }

        string encoded = _encoding.Base64Encode(data);

        Assert.DoesNotContain('\n', encoded);
        Assert.DoesNotContain('\r', encoded);
        Assert.Equal(encoded.Length, encoded.TrimEnd().Length);
    }

    /// <summary>
    /// Hexadecimal encoding emits UPPER CASE with no separator, two digits per byte.
    /// </summary>
    /// <remarks>
    /// DECISION E1, and the one observable in this file that a paired legacy recording would settle. If
    /// a capture shows lower case, the correction is a single call change in the production file and
    /// nothing else anywhere - which is precisely why the letter case is asserted in one place rather
    /// than assumed in twenty. The changelog entry that introduced this function and its inverse
    /// describes them only as hexadecimal encoding and decoding of a blob, specifying no case
    /// [<c>logfile.md:L931</c>].
    /// </remarks>
    [Theory]
    [InlineData(new byte[0], "")]
    [InlineData(new byte[] { 0x00 }, "00")]
    [InlineData(new byte[] { 0xFF }, "FF")]
    [InlineData(new byte[] { 0x0A, 0xBC }, "0ABC")]
    [InlineData(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, "DEADBEEF")]
    public void HexEncode_EmitsUpperCaseWithNoSeparator(byte[] data, string expected)
    {
        string encoded = _encoding.HexEncode(data);

        Assert.Equal(expected, encoded);
        Assert.Equal(encoded.ToUpperInvariant(), encoded);
        Assert.Equal(data.Length * 2, encoded.Length);
    }

    /// <summary>
    /// An EMPTY payload is valid in both encodings and yields an empty string.
    /// </summary>
    /// <remarks>
    /// Not an error and not a special case: encoding nothing produces nothing. Asserted through the
    /// dispatch member as well as the wrappers so that neither path grows a guard the other lacks.
    /// </remarks>
    [Fact]
    public void AnEmptyPayload_EncodesToAnEmptyStringInBothEncodings()
    {
        Assert.Equal(string.Empty, _encoding.BlobToString([], Enums.CRYPTO_ENCODING_BASE64));
        Assert.Equal(string.Empty, _encoding.BlobToString([], Enums.CRYPTO_ENCODING_HEX));
        Assert.Equal(string.Empty, _encoding.Base64Encode([]));
        Assert.Equal(string.Empty, _encoding.HexEncode([]));
    }

    // ==========================================================================================
    //  The decode direction, and the tolerance asymmetry
    // ==========================================================================================

    /// <summary>
    /// Base64 decoding accepts well-formed text and IGNORES white space anywhere in it.
    /// </summary>
    /// <remarks>
    /// A property of the primitive rather than behaviour added here, and load-bearing: it is what lets
    /// encoded text survive a round trip through a text control, which is exactly what the oracle's own
    /// caller does [<c>u_cst_tabpage_utility_crypto.sru:L419</c>, <c>:L434</c>]. Spaces, newlines and
    /// tabs are each asserted separately because they are separate characters and a hand-written
    /// stripper would plausibly handle only the first.
    /// </remarks>
    [Theory]
    [InlineData("AQID")]
    [InlineData(" AQID ")]
    [InlineData(" A Q I D ")]
    [InlineData("AQ\nID")]
    [InlineData("AQ\r\nID")]
    [InlineData("AQ\tID")]
    public void Base64Decode_IgnoresWhiteSpaceAnywhereInTheText(string encoded) =>
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, _encoding.Base64Decode(encoded));

    /// <summary>
    /// Base64 decoding REJECTS unpadded text, the URL-safe alphabet, a foreign character and a bad
    /// length residue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MEASURED, and two of the four are worth knowing before writing a caller. Unpadded Base64 - the
    /// form many web APIs emit - is rejected, so a caller receiving such text must pad it. The URL-safe
    /// alphabet, which substitutes <c>-</c> and <c>_</c>, is likewise rejected rather than silently
    /// decoded, which is consistent with the production file offering no URL-safe variant.
    /// </para>
    /// <para>
    /// A malformed payload raises <see cref="FormatException"/> from the primitive itself, PROPAGATED
    /// UNCHANGED rather than caught and rewrapped, so its message and stack survive.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("AQI")]
    [InlineData("A")]
    [InlineData("-_8=")]
    [InlineData("AQ!D")]
    [InlineData("====")]
    public void Base64Decode_RejectsUnpaddedUrlSafeAndOtherwiseMalformedText(string encoded)
    {
        Action decode = () => _ = _encoding.Base64Decode(encoded);

        Assert.Throws<FormatException>(decode);
    }

    /// <summary>
    /// Hexadecimal decoding accepts upper, lower and mixed case.
    /// </summary>
    /// <remarks>
    /// The case tolerance is what makes DECISION E1 SAFE IN BOTH DIRECTIONS: whichever case a paired
    /// recording eventually settles for the encode direction, this method already accepts it, so no
    /// round trip can fail on casing.
    /// </remarks>
    [Theory]
    [InlineData("DEADBEEF")]
    [InlineData("deadbeef")]
    [InlineData("DeAdBeEf")]
    [InlineData("dEaDbEeF")]
    public void HexDecode_AcceptsAnyLetterCase(string encoded) =>
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, _encoding.HexDecode(encoded));

    /// <summary>
    /// Hexadecimal decoding REJECTS white space, an odd digit count, a prefix and a non-hexadecimal
    /// character.
    /// </summary>
    /// <remarks>
    /// THE ASYMMETRY WITH BASE64, ASSERTED. Base64 ignores white space and hexadecimal does not, and
    /// adding a white-space stripper here would be inventing a tolerance the legacy gives no evidence
    /// of. The <c>0x</c> prefix case is included because it is the spelling a developer is most likely
    /// to try, and because rejecting it is a decision a reader should be able to discover.
    /// </remarks>
    [Theory]
    [InlineData("ABC")]
    [InlineData("DE AD")]
    [InlineData(" DEAD")]
    [InlineData("DEAD\n")]
    [InlineData("0xDEAD")]
    [InlineData("GG")]
    [InlineData("DEADBEE")]
    public void HexDecode_RejectsWhiteSpaceOddLengthPrefixesAndNonHexadecimalCharacters(string encoded)
    {
        Action decode = () => _ = _encoding.HexDecode(encoded);

        Assert.Throws<FormatException>(decode);
    }

    /// <summary>
    /// An EMPTY string is valid in both encodings and yields an empty array.
    /// </summary>
    [Fact]
    public void AnEmptyString_DecodesToAnEmptyArrayInBothEncodings()
    {
        Assert.Empty(_encoding.StringToBlob(string.Empty, Enums.CRYPTO_ENCODING_BASE64));
        Assert.Empty(_encoding.StringToBlob(string.Empty, Enums.CRYPTO_ENCODING_HEX));
        Assert.Empty(_encoding.Base64Decode(string.Empty));
        Assert.Empty(_encoding.HexDecode(string.Empty));
    }

    // ==========================================================================================
    //  Round trips
    // ==========================================================================================

    /// <summary>
    /// Both encodings round-trip any byte sequence exactly, at every length residue.
    /// </summary>
    /// <remarks>
    /// The inverse relationship the oracle fixes by declaring the two members as a pair
    /// [<c>n_crypto.sru:L11-L12</c>]. Lengths 0 through 8 cover every Base64 padding residue twice
    /// over, and the all-bytes case exercises every one of the 256 byte values so that no value is
    /// mapped wrongly.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(256)]
    public void BothEncodings_RoundTripAnyByteSequenceExactly(int length)
    {
        byte[] data = new byte[length];
        for (int index = 0; index < length; index++)
        {
            data[index] = (byte)index;
        }

        Assert.Equal(data, _encoding.Base64Decode(_encoding.Base64Encode(data)));
        Assert.Equal(data, _encoding.HexDecode(_encoding.HexEncode(data)));
    }

    /// <summary>
    /// The two encodings are not interchangeable: text produced by one is not accepted by the other,
    /// except where both alphabets happen to admit it.
    /// </summary>
    /// <remarks>
    /// Worth pinning because a caller that passed the wrong encoding constant would otherwise get a
    /// plausible-looking wrong answer. Hexadecimal text IS valid Base64 whenever its length is a
    /// multiple of four - the digits and letters are all in the Base64 alphabet - so the DECODED BYTES
    /// are compared rather than merely asserting that one throws. That is the failure mode this test
    /// exists for: silent misinterpretation rather than an exception.
    /// </remarks>
    [Fact]
    public void TheTwoEncodings_ProduceDifferentBytesForTheSameText()
    {
        byte[] data = [0xDE, 0xAD, 0xBE, 0xEF];
        string asHex = _encoding.HexEncode(data);

        Assert.Equal("DEADBEEF", asHex);

        // Eight hexadecimal digits are also well-formed Base64 - and decode to something else entirely.
        byte[] misread = _encoding.Base64Decode(asHex);

        Assert.NotEqual(data, misread);
        Assert.Equal(data, _encoding.HexDecode(asHex));
    }

    // ==========================================================================================
    //  Dispatch and delegation
    // ==========================================================================================

    /// <summary>
    /// Each of the four wrappers delegates to the dispatch member with its own encoding constant, and
    /// holds no conversion logic of its own.
    /// </summary>
    /// <remarks>
    /// Mirrors the legacy exactly: each wrapper's entire body is a single call - for instance
    /// <c>return n_crypto.BlobToString(data, Enums.CRYPTO_ENCODING_BASE64)</c>
    /// [<c>base64encode.srf:L10-L11</c>]. That single dispatch point is what makes DECISION E1 and
    /// DECISION E2 decidable in ONE place, so the agreement asserted here is the property that keeps
    /// them there.
    /// </remarks>
    [Fact]
    public void TheFourWrappers_DelegateToTheDispatchMemberWithTheirOwnEncoding()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05];

        Assert.Equal(_encoding.BlobToString(data, Enums.CRYPTO_ENCODING_BASE64), _encoding.Base64Encode(data));
        Assert.Equal(_encoding.BlobToString(data, Enums.CRYPTO_ENCODING_HEX), _encoding.HexEncode(data));

        string base64 = _encoding.Base64Encode(data);
        string hex = _encoding.HexEncode(data);

        Assert.Equal(_encoding.StringToBlob(base64, Enums.CRYPTO_ENCODING_BASE64), _encoding.Base64Decode(base64));
        Assert.Equal(_encoding.StringToBlob(hex, Enums.CRYPTO_ENCODING_HEX), _encoding.HexDecode(hex));
    }

    /// <summary>
    /// An encoding outside the published pair is rejected in BOTH directions, with the same failure
    /// shape and the same message.
    /// </summary>
    /// <remarks>
    /// One validation point, one wording. The screen goes through the shared catalogue rather than
    /// local literals, which is what keeps this type's accepted set identical to every sibling
    /// provider's - and is why no encoding constant is declared in the production file. The offending
    /// value is carried on the exception so a caller can log which constant it passed without the
    /// provider logging anything itself.
    /// </remarks>
    [Theory]
    [InlineData(-1L)]
    [InlineData(2L)]
    [InlineData(3L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void AnUnpublishedEncoding_IsRejectedIdenticallyInBothDirections(long encoding)
    {
        Action encode = () => _ = _encoding.BlobToString([0x01], encoding);
        Action decode = () => _ = _encoding.StringToBlob("AQ==", encoding);

        ArgumentOutOfRangeException fromEncode = Assert.Throws<ArgumentOutOfRangeException>(encode);
        ArgumentOutOfRangeException fromDecode = Assert.Throws<ArgumentOutOfRangeException>(decode);

        Assert.Equal("encoding", fromEncode.ParamName);
        Assert.Equal("encoding", fromDecode.ParamName);
        Assert.Equal(encoding, fromEncode.ActualValue);
        Assert.Equal(encoding, fromDecode.ActualValue);
        Assert.Equal(fromEncode.Message, fromDecode.Message);
    }

    /// <summary>
    /// A null payload is a caller defect in every member, reported as an argument-null failure naming
    /// the data parameter.
    /// </summary>
    /// <remarks>
    /// The uniform null policy of all seven members, asserted across all of them rather than sampled -
    /// a single member that returned an empty result for null instead would be a silent divergence,
    /// because an empty result is a legal outcome for an empty input.
    /// </remarks>
    [Fact]
    public void ANullPayload_IsRejectedByEveryMember()
    {
        byte[]? nullBlob = null;
        string? nullText = null;

        Action[] callsWithNullBlob =
        [
            () => _ = _encoding.BlobToString(nullBlob!, Enums.CRYPTO_ENCODING_BASE64),
            () => _ = _encoding.BlobToString(nullBlob!, Enums.CRYPTO_ENCODING_HEX),
            () => _ = _encoding.Base64Encode(nullBlob!),
            () => _ = _encoding.HexEncode(nullBlob!),
        ];

        Action[] callsWithNullText =
        [
            () => _ = _encoding.StringToBlob(nullText!, Enums.CRYPTO_ENCODING_BASE64),
            () => _ = _encoding.StringToBlob(nullText!, Enums.CRYPTO_ENCODING_HEX),
            () => _ = _encoding.Base64Decode(nullText!),
            () => _ = _encoding.HexDecode(nullText!),
        ];

        foreach (Action call in callsWithNullBlob.Concat(callsWithNullText))
        {
            ArgumentNullException failure = Assert.Throws<ArgumentNullException>(call);
            Assert.Equal("data", failure.ParamName);
        }

        // And the reversal member, whose parameter is by reference.
        byte[]? nullBuffer = null;
        ArgumentNullException fromReverse =
            Assert.Throws<ArgumentNullException>(() => _encoding.BlobReverse(ref nullBuffer!));
        Assert.Equal("data", fromReverse.ParamName);
    }

    /// <summary>
    /// The null check precedes the encoding check, so a call that is wrong in both ways reports the
    /// null.
    /// </summary>
    /// <remarks>
    /// Ordering matters for diagnosis rather than for correctness: a null payload is the more
    /// fundamental defect, and reporting the encoding first would send a caller looking at the wrong
    /// argument. Asserted because the ordering is invisible in any test that supplies one bad argument
    /// at a time.
    /// </remarks>
    [Fact]
    public void TheNullCheck_PrecedesTheEncodingCheck()
    {
        byte[]? nullBlob = null;

        Assert.Throws<ArgumentNullException>(() => _ = _encoding.BlobToString(nullBlob!, 99L));
        Assert.Throws<ArgumentNullException>(() => _ = _encoding.StringToBlob(null!, 99L));
    }

    // ==========================================================================================
    //  BlobReverse
    // ==========================================================================================

    /// <summary>
    /// The reversal is performed IN PLACE, on the caller's own array, and reports success at every
    /// length.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DECISION E3. The <c>ref</c> is retained because the legacy declares it
    /// [<c>n_crypto.sru:L13</c>] - it is part of the observable contract, and it leaves room for an
    /// implementation that assigns a fresh array. This one does not need that room, and the
    /// same-reference assertion is what records that: the call allocates nothing, so a caller holding
    /// another reference to the buffer sees the reversal too.
    /// </para>
    /// <para>
    /// Every length is deliberate. Zero and one byte are each already their own reverse and are left
    /// untouched, reported as SUCCESS rather than as a failed no-op. An even length swaps in pairs; an
    /// odd length leaves the exact middle byte in place, which needs no special handling.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(64)]
    public void BlobReverse_ReversesInPlaceAndReportsSuccessAtEveryLength(int length)
    {
        byte[] buffer = new byte[length];
        for (int index = 0; index < length; index++)
        {
            buffer[index] = (byte)(index + 1);
        }

        byte[] original = (byte[])buffer.Clone();
        byte[] aliased = buffer;

        Assert.True(_encoding.BlobReverse(ref buffer));

        Assert.Same(aliased, buffer);
        Assert.Equal(original.Reverse().ToArray(), buffer);
        Assert.Equal(original.Reverse().ToArray(), aliased);
    }

    /// <summary>
    /// The reversal is its own inverse at every length.
    /// </summary>
    /// <remarks>
    /// Asserted separately from the reversal itself because an implementation that reversed all but the
    /// last byte would pass a single-reversal test on a palindromic buffer and fail this one. The
    /// buffers here are deliberately non-palindromic.
    /// </remarks>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(17)]
    public void BlobReverse_IsItsOwnInverse(int length)
    {
        byte[] buffer = new byte[length];
        for (int index = 0; index < length; index++)
        {
            buffer[index] = (byte)(index + 1);
        }

        byte[] original = (byte[])buffer.Clone();

        Assert.True(_encoding.BlobReverse(ref buffer));
        Assert.NotEqual(original, buffer);
        Assert.True(_encoding.BlobReverse(ref buffer));
        Assert.Equal(original, buffer);
    }

    /// <summary>
    /// The reversal returns <see langword="true"/> in every case that returns at all - there is no
    /// false arm.
    /// </summary>
    /// <remarks>
    /// DECISION E3 stated as a property rather than as a case. True means the postcondition holds: the
    /// buffer now contains the reverse of what it contained on entry. The only failure the legacy
    /// boolean could have carried is a native-interface handle fault, which has no managed analogue, so
    /// reporting false for an empty buffer - the plausible alternative - was rejected: an empty buffer
    /// IS its own reverse, so the postcondition holds and false would be a lie.
    /// </remarks>
    [Fact]
    public void BlobReverse_HasNoFalseArm()
    {
        foreach (int length in new[] { 0, 1, 2, 3, 100 })
        {
            byte[] buffer = new byte[length];

            Assert.True(_encoding.BlobReverse(ref buffer));
        }
    }

    /// <summary>
    /// The reversal composes with the two encodings, which is how the legacy's own byte-order
    /// conversions are expressed.
    /// </summary>
    /// <remarks>
    /// The oracle's caller encodes a blob it has just reversed rather than asking this type to do both,
    /// so the composition is the caller's step. Asserted here to record that the two operations are
    /// independent and that neither implicitly performs the other - a provider that reversed on encode
    /// would pass every other test in this file.
    /// </remarks>
    [Fact]
    public void BlobReverse_ComposesWithBothEncodingsWithoutEitherImplyingTheOther()
    {
        byte[] buffer = [0x01, 0x02, 0x03, 0x04];

        Assert.Equal("01020304", _encoding.HexEncode(buffer));

        Assert.True(_encoding.BlobReverse(ref buffer));

        Assert.Equal("04030201", _encoding.HexEncode(buffer));
        Assert.Equal(buffer, _encoding.Base64Decode(_encoding.Base64Encode(buffer)));
    }
}
