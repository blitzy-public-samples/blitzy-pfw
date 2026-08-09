// ==================================================================================================
//  AssertionFailureTests.cs - THE SEVEN-FIELD ASSERT PAYLOAD CARRIER
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Diagnostics.AssertionFailure
//  ORACLES           ws_objects/pfw.common.pbl.src/assertionfailed.sru   the type and its 7 members
//                    ws_objects/pfw.common.pbl.src/assert.srf            the PRODUCER
//                    ws_objects/pfw.pbl.src/pfw.sra:L111-L144            the CONSUMER
//
//  WHAT THIS TYPE IS
//  ------------------------------------------------------------------------------------------------
//  A throwable that is also a data carrier, and nothing more. It holds seven payload members plus a
//  replaceable message, and it deliberately holds NO logic: the producer assigns the members and then
//  calls SetMessage with a CRLF-joined string, and the consumer splits that string back apart. Neither
//  half of the protocol lives in this class, which is why the tests below are about the carrier's
//  fidelity - defaults, mutability, ordering, the message override, and the shadowed StackTrace - rather
//  than about parsing.
//
//  THE WIRE PROTOCOL THIS CARRIER FEEDS, AND WHY THE ASYMMETRY IS LOAD-BEARING
//  ------------------------------------------------------------------------------------------------
//  The producer joins fields with CRLF - the two characters "\r\n" and nothing else. Separators INSIDE
//  a field are a BARE LINE FEED "\n", never CRLF [assert.srf:L63]. That asymmetry is the entire reason
//  splitting on CRLF is unambiguous: a field may itself be multi-line (the stack trace is), and if its
//  internal separator were also CRLF the consumer could not tell a field boundary from a line break
//  inside one.
//
//  Consequence for the port, asserted below: Environment.NewLine must NEVER appear in either role. It
//  is CRLF on Windows and a bare LF on Linux, so a payload built with it would split into the right
//  number of fields on one platform and the wrong number on the other - and these containers are Linux.
//
//  EXACTLY TWO SHAPES EXIST: TWO FIELDS, OR SEVEN
//  ------------------------------------------------------------------------------------------------
//  The consumer requires at least two fields [pfw.sra:L116] and then tests for EXACTLY seven, not at
//  least seven [pfw.sra:L119]. A five- or six-field payload has no meaning to it. That is why this type
//  has no constructor taking the seven fields and no serialization member: the legacy creates the object
//  EMPTY and assigns members one at a time, so the empty state is a real, reachable state and is
//  asserted as such.
//
//  A PRE-EXISTING DEFECT THAT TRAVELS WITH THE PROTOCOL (C-B)
//  ------------------------------------------------------------------------------------------------
//  A deep-shape payload whose seventh field is EMPTY splits into six segments rather than seven, and so
//  silently fails the consumer's exactly-seven test [pfw.sra:L64-L66 hazard note]. The split behaviour
//  is asserted below against the framework's own splitter so the defect is recorded executably rather
//  than only in prose - it is reproduced, not corrected.
//
//  RULES POSITION
//  review_rules returns "No user rules provided.". Constraints cited inline: C-B (replicate behaviour
//  and defects), C-K (document boundary decisions), AAP 0.4.5.3 (identifier spellings survive verbatim).
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace PowerFramework.Shared.Diagnostics.Tests;

/// <summary>
/// Characterization tests for <see cref="AssertionFailure"/>.
/// </summary>
public class AssertionFailureTests
{
    /// <summary>
    /// The field delimiter the producer uses: CRLF, and nothing else.
    /// </summary>
    private const string FieldDelimiter = "\r\n";

    /// <summary>
    /// The separator used INSIDE a multi-line field: a bare line feed, never CRLF.
    /// </summary>
    private const string IntraFieldSeparator = "\n";

    /// <summary>
    /// The seven payload member names, in the order the oracle declares them
    /// [assertionfailed.sru:L18-L24].
    /// </summary>
    /// <remarks>
    /// Held as data so the completeness audit and the ordering assertions read from one place. The
    /// spellings are the legacy ones verbatim per AAP 0.4.5.3 - notably <c>Object</c>, which collides
    /// with nothing in C# only because it is a property rather than a type, and <c>StackTrace</c>, which
    /// collides with <see cref="Exception.StackTrace"/> deliberately.
    /// </remarks>
    private static readonly string[] PayloadMemberNames =
    [
        nameof(AssertionFailure.Info),
        nameof(AssertionFailure.WindowMenu),
        nameof(AssertionFailure.Object),
        nameof(AssertionFailure.ObjectEvent),
        nameof(AssertionFailure.Line),
        nameof(AssertionFailure.StackTraceInfo),
        nameof(AssertionFailure.StackTrace),
    ];

    // ==============================================================================================
    //  1. CONSTRUCTION AND THE EMPTY STATE
    // ==============================================================================================

    /// <summary>
    /// The parameterless constructor - the one the legacy producer uses - yields an object whose every
    /// payload member is at its empty default.
    /// </summary>
    /// <remarks>
    /// The empty state is not a degenerate case to be tolerated; it is the state the producer starts
    /// from and then fills in one member at a time [assert.srf:L34-L80], and it is the whole of the
    /// SHALLOW shape. So every default is pinned: the five strings empty rather than null, the line
    /// number zero, and the frame list present-but-empty rather than null. A null anywhere here would
    /// make the producer's <c>+=</c> concatenations produce <c>"null"</c> or throw.
    /// </remarks>
    [Fact]
    public void TheParameterlessConstructorYieldsTheEmptyPayload()
    {
        AssertionFailure failure = new();

        Assert.Equal(string.Empty, failure.Info);
        Assert.Equal(string.Empty, failure.WindowMenu);
        Assert.Equal(string.Empty, failure.Object);
        Assert.Equal(string.Empty, failure.ObjectEvent);
        Assert.Equal(0L, failure.Line);
        Assert.Equal(string.Empty, failure.StackTraceInfo);

        Assert.NotNull(failure.StackTrace);
        Assert.Empty(failure.StackTrace);
    }

    /// <summary>
    /// The message-taking constructor sets the inherited message and leaves the payload empty.
    /// </summary>
    /// <remarks>
    /// The two routes differ ONLY in the message, which is the point: the payload members are assigned
    /// after construction on either route, so passing a message up front is a convenience for a
    /// throw-site that has no payload rather than an alternative shape.
    /// </remarks>
    [Fact]
    public void TheMessageTakingConstructorSetsTheMessageAndLeavesThePayloadEmpty()
    {
        AssertionFailure failure = new("assertion text");

        Assert.Equal("assertion text", failure.Message);
        Assert.Equal(string.Empty, failure.Info);
        Assert.Equal(0L, failure.Line);
        Assert.Empty(failure.StackTrace);
    }

    /// <summary>
    /// It is a real <see cref="Exception"/> and can be thrown and caught as one.
    /// </summary>
    /// <remarks>
    /// The legacy type derives from PowerBuilder's <c>throwable</c>, so the ported type must be usable
    /// with <c>throw</c> and reachable by a <c>catch (Exception)</c> that knows nothing about it -
    /// which is exactly how <c>stacktraceinfo.srf</c>'s bare catch swallows it. Asserted because a
    /// carrier that were merely a POCO would compile against most of this file.
    /// </remarks>
    [Fact]
    public void ItIsAThrowableExceptionAndSurvivesABareCatch()
    {
        AssertionFailure thrown = new("thrown payload") { Line = 42L };

        // Bound through an explicitly typed Action: a throw-expression lambda is convertible to both
        // Action and Func<Task>, and overload resolution picks the obsolete async form.
        Action throwIt = () => throw thrown;
        Exception caught = Assert.Throws<AssertionFailure>(throwIt);

        Assert.Same(thrown, caught);
        Assert.Equal("thrown payload", caught.Message);
        Assert.Equal(42L, ((AssertionFailure)caught).Line);
    }

    // ==============================================================================================
    //  2. THE MESSAGE OVERRIDE
    // ==============================================================================================

    /// <summary>
    /// <see cref="AssertionFailure.SetMessage"/> replaces what <see cref="AssertionFailure.Message"/>
    /// reports, overriding whatever the constructor supplied.
    /// </summary>
    /// <remarks>
    /// This is the producer's final act: it assigns all seven members and only then calls SetMessage
    /// with the CRLF-joined payload [assert.srf:L80]. So the message is not a description of the
    /// failure - it IS the payload, and the consumer's first act is to split it [pfw.sra:L115].
    /// Anything that trimmed, wrapped or prefixed it here would break that split.
    /// </remarks>
    [Fact]
    public void SetMessageReplacesTheReportedMessage()
    {
        AssertionFailure fromConstructor = new("original");
        fromConstructor.SetMessage("replacement");
        Assert.Equal("replacement", fromConstructor.Message);

        AssertionFailure fromEmpty = new();
        fromEmpty.SetMessage("assigned later");
        Assert.Equal("assigned later", fromEmpty.Message);
    }

    /// <summary>
    /// Before <see cref="AssertionFailure.SetMessage"/> is called on an empty instance, the message is
    /// the base type's default rather than the empty string.
    /// </summary>
    /// <remarks>
    /// The override reads <c>_message ?? base.Message</c>, so an unset message falls through to the
    /// framework's generated text. Asserted because the alternative a reader might assume - empty
    /// string - would make an unset payload indistinguishable from a payload whose fields were all
    /// empty, and the consumer's at-least-two-fields test [pfw.sra:L116] would then read differently.
    /// </remarks>
    [Fact]
    public void AnUnsetMessageFallsThroughToTheBaseMessage()
    {
        AssertionFailure failure = new();

        Assert.NotNull(failure.Message);
        Assert.NotEqual(string.Empty, failure.Message);

        // Whatever the framework's default text is, SetMessage supersedes it.
        string generated = failure.Message;
        failure.SetMessage("explicit");
        Assert.NotEqual(generated, failure.Message);
    }

    /// <summary>
    /// The message can be replaced repeatedly, and the last write wins, including with the empty
    /// string.
    /// </summary>
    /// <remarks>
    /// The empty-string case matters: it is a value the override treats as PRESENT rather than as
    /// absent, because the field is <c>string?</c> and only null falls through. So SetMessage("")
    /// produces an empty message rather than reverting to the base text - a small distinction that
    /// decides whether a payload built from seven empty fields reaches the consumer at all.
    /// </remarks>
    [Fact]
    public void TheLastSetMessageWinsIncludingTheEmptyString()
    {
        AssertionFailure failure = new("first");

        failure.SetMessage("second");
        failure.SetMessage("third");
        Assert.Equal("third", failure.Message);

        failure.SetMessage(string.Empty);
        Assert.Equal(string.Empty, failure.Message);
    }

    // ==============================================================================================
    //  3. THE SEVEN MEMBERS
    // ==============================================================================================

    /// <summary>
    /// All six settable payload members round-trip their assigned values independently.
    /// </summary>
    /// <remarks>
    /// Assigned to DISTINCT values and then all read back, so a mis-wired property - two members
    /// backed by one field, a setter writing the wrong backing store - fails rather than passing by
    /// coincidence. The values are deliberately shaped like the real ones the producer supplies.
    /// </remarks>
    [Fact]
    public void EverySettableMemberRoundTripsIndependently()
    {
        AssertionFailure failure = new()
        {
            Info = "PowerFramework Runtime Error\n-10000",
            WindowMenu = "w_test_assert",
            Object = "n_cst_dwsvc",
            ObjectEvent = "of_calcitem",
            Line = 1234L,
            StackTraceInfo = "n_cst_dwsvc.of_calcitem line:1234",
        };

        Assert.Equal("PowerFramework Runtime Error\n-10000", failure.Info);
        Assert.Equal("w_test_assert", failure.WindowMenu);
        Assert.Equal("n_cst_dwsvc", failure.Object);
        Assert.Equal("of_calcitem", failure.ObjectEvent);
        Assert.Equal(1234L, failure.Line);
        Assert.Equal("n_cst_dwsvc.of_calcitem line:1234", failure.StackTraceInfo);
    }

    /// <summary>
    /// <see cref="AssertionFailure.Line"/> is a 64-bit signed value and carries the full range,
    /// including negatives.
    /// </summary>
    /// <param name="line">The line number to round-trip.</param>
    /// <remarks>
    /// PowerScript <c>long</c> is 32-bit, and the port widens it to 64-bit per the AAP's type map. The
    /// widening is safe in the only direction that matters - every legacy value is representable - and
    /// the negative rows are pinned because a line number is never negative in practice, so a
    /// <c>ulong</c> or a validating setter would be an easy and wrong "improvement": the legacy
    /// performs no validation and neither does this.
    /// </remarks>
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void TheLineNumberCarriesTheFullSignedRangeWithoutValidation(long line)
    {
        AssertionFailure failure = new() { Line = line };

        Assert.Equal(line, failure.Line);
    }

    /// <summary>
    /// The frame list is APPEND-ordered and its order is part of the contract: outermost first, exactly
    /// as captured.
    /// </summary>
    /// <remarks>
    /// The producer appends one frame per loop iteration [assert.srf:L61-L62] and never reorders,
    /// replaces or sorts. The list is exposed behind a get-only property precisely so that "the producer
    /// only ever appends" stays true, and this test is what makes the ordering claim executable rather
    /// than a comment.
    /// </remarks>
    [Fact]
    public void TheFrameListPreservesAppendOrder()
    {
        AssertionFailure failure = new();

        failure.StackTrace.Add("outermost line:1");
        failure.StackTrace.Add("middle line:2");
        failure.StackTrace.Add("innermost line:3");

        Assert.Equal(3, failure.StackTrace.Count);
        Assert.Equal("outermost line:1", failure.StackTrace[0]);
        Assert.Equal("middle line:2", failure.StackTrace[1]);
        Assert.Equal("innermost line:3", failure.StackTrace[2]);
    }

    /// <summary>
    /// The frame list is the SAME instance across reads, so appends accumulate rather than being
    /// discarded.
    /// </summary>
    /// <remarks>
    /// A get-only property returning a fresh list each time would compile, satisfy the ordering test
    /// above within one expression, and silently lose every frame the producer added - which is the
    /// failure this pins. Reference identity is the only way to state it.
    /// </remarks>
    [Fact]
    public void TheFrameListIsOneInstanceThatAccumulates()
    {
        AssertionFailure failure = new();

        IList<string> firstRead = failure.StackTrace;
        firstRead.Add("frame one");

        IList<string> secondRead = failure.StackTrace;
        secondRead.Add("frame two");

        Assert.Same(firstRead, secondRead);
        Assert.Equal(2, failure.StackTrace.Count);
    }

    /// <summary>
    /// The legacy frame list SHADOWS <see cref="Exception.StackTrace"/>, and both members remain
    /// reachable and independent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declared with <c>new</c>, so it HIDES rather than overrides. The consequence, asserted here, is
    /// that the two carry different things at the same time: through an <see cref="AssertionFailure"/>
    /// reference the member is the legacy frame list, and through an <see cref="Exception"/> reference
    /// it is the CLR trace. A reader who assumed an override would expect one of the two to be gone.
    /// </para>
    /// <para>
    /// The legacy spelling is kept per AAP 0.4.5.3 because the producer, the consumer and every stored
    /// comparison look for it. The cost is this collision, paid once - and it is why no TYPE in this
    /// project may be named <c>StackTrace</c>, which is why the capture helper is
    /// <c>StackTraceProvider</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFrameListShadowsTheClrStackTraceAndBothRemainReachable()
    {
        PropertyInfo declared = typeof(AssertionFailure).GetProperty(
            nameof(AssertionFailure.StackTrace),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;

        Assert.NotNull(declared);
        Assert.Equal(typeof(IList<string>), declared.PropertyType);

        // The inherited member is still declared on Exception and still typed as a string.
        PropertyInfo inherited = typeof(Exception).GetProperty(nameof(Exception.StackTrace))!;
        Assert.Equal(typeof(string), inherited.PropertyType);

        // Both are reachable on one instance, and they carry different things.
        AssertionFailure failure = new("shadowed");
        failure.StackTrace.Add("legacy frame line:7");

        Assert.Single(failure.StackTrace);
        Assert.Equal("legacy frame line:7", failure.StackTrace[0]);

        Exception asException = failure;
        Assert.Null(asException.StackTrace); // never thrown, so the CLR trace is absent
    }

    /// <summary>
    /// Once thrown, the CLR trace populates while the legacy frame list stays exactly as the producer
    /// left it.
    /// </summary>
    /// <remarks>
    /// The other half of the shadowing story, and the one that proves the two are genuinely independent
    /// rather than merely differently typed. It also pins that <see cref="Exception.ToString"/> keeps
    /// using the CLR trace - a consequence of hiding rather than overriding - so a diagnostic that
    /// prints the exception does not accidentally emit the legacy payload.
    /// </remarks>
    [Fact]
    public void ThrowingPopulatesTheClrTraceAndLeavesTheLegacyListAlone()
    {
        AssertionFailure thrown = new("throw me");
        thrown.StackTrace.Add("producer frame line:11");

        Action throwIt = () => throw thrown;
        AssertionFailure caught = Assert.Throws<AssertionFailure>(throwIt);

        Exception asException = caught;
        Assert.NotNull(asException.StackTrace);
        Assert.NotEmpty(asException.StackTrace!);

        Assert.Single(caught.StackTrace);
        Assert.Equal("producer frame line:11", caught.StackTrace[0]);

        // ToString uses the CLR trace, not the legacy list.
        Assert.DoesNotContain("producer frame line:11", caught.ToString(), StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  4. THE SEVEN-MEMBER CAP
    // ==============================================================================================

    /// <summary>
    /// The type declares EXACTLY the seven payload members plus <c>Message</c>, and no more.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cap is a real constraint rather than tidiness. The consumer tests for EXACTLY seven fields
    /// [pfw.sra:L119], so an eighth member that the producer learned to serialize would silently break
    /// every consumer, and one that it did not serialize would be dead weight advertising a capability
    /// the protocol does not have.
    /// </para>
    /// <para>
    /// <c>Message</c> is the one legitimate extra: it is the override that carries the joined payload,
    /// not a field of it. The audit therefore allows exactly that name and rejects anything else, and
    /// it reads the member list from <see cref="PayloadMemberNames"/> so the ordering assertions and
    /// this one cannot disagree.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTypeDeclaresExactlyTheSevenPayloadMembersPlusMessage()
    {
        string[] declaredProperties = typeof(AssertionFailure)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        string[] expected = PayloadMemberNames
            .Concat([nameof(AssertionFailure.Message)])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, declaredProperties);
    }

    /// <summary>
    /// Every payload member is spelled exactly as the oracle declares it, and the six settable ones
    /// have public setters while the frame list does not.
    /// </summary>
    /// <remarks>
    /// The spellings are asserted by reflection rather than by use, so a rename that kept the code
    /// compiling - by renaming call sites too - still fails here. That is the point of AAP 0.4.5.3:
    /// these identifiers appear in stored comparisons that no compiler checks.
    /// </remarks>
    [Fact]
    public void EveryPayloadMemberIsSpelledAsTheOracleDeclaresIt()
    {
        foreach (string name in PayloadMemberNames)
        {
            PropertyInfo? property = typeof(AssertionFailure).GetProperty(
                name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            Assert.NotNull(property);
            Assert.True(property!.CanRead, $"{name} must be readable.");
        }

        // The six assigned members are settable; the frame list is appended to, never replaced.
        foreach (string name in PayloadMemberNames.Where(
            candidate => candidate != nameof(AssertionFailure.StackTrace)))
        {
            Assert.True(
                typeof(AssertionFailure).GetProperty(name)!.CanWrite,
                $"{name} is assigned by the producer and must have a setter.");
        }

        Assert.False(
            typeof(AssertionFailure).GetProperty(
                nameof(AssertionFailure.StackTrace),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.CanWrite,
            "The frame list must be get-only: the legacy never replaces the array wholesale.");
    }

    /// <summary>
    /// The type carries no constructor taking the seven fields and no parsing member - it is a carrier,
    /// not the protocol.
    /// </summary>
    /// <remarks>
    /// A convenience constructor would be the natural thing to add and would misrepresent the
    /// protocol: the producer creates the object EMPTY and fills it in, so the empty state is
    /// reachable and meaningful, and a seven-argument constructor would imply the payload is built
    /// atomically. Likewise no <c>Parse</c> or <c>Split</c> member: the consumer's split lives in
    /// <c>pfw.sra</c>'s systemerror handler, whose port is the Gateway's own file.
    /// </remarks>
    [Fact]
    public void TheTypeCarriesNoSevenFieldConstructorAndNoParsingMember()
    {
        ConstructorInfo[] constructors = typeof(AssertionFailure)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Equal(2, constructors.Length);
        Assert.Contains(constructors, candidate => candidate.GetParameters().Length == 0);
        Assert.Contains(
            constructors,
            candidate => candidate.GetParameters() is [{ ParameterType: Type type }] && type == typeof(string));

        string[] declaredMethods = typeof(AssertionFailure)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ToArray();

        Assert.Equal([nameof(AssertionFailure.SetMessage)], declaredMethods);
    }

    // ==============================================================================================
    //  5. THE DELIMITER PROTOCOL, AND THE DEFECT IT CARRIES
    // ==============================================================================================

    /// <summary>
    /// A seven-field payload joined with CRLF splits back into exactly seven fields, and a multi-line
    /// stack-trace field survives because its own separator is a BARE LINE FEED.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The asymmetry demonstrated end to end. Field 6 below is genuinely multi-line - three frames
    /// joined with "\n" - and the split still yields seven, because "\n" alone is not a field boundary.
    /// If the intra-field separator were CRLF the same payload would split into nine and the consumer's
    /// exactly-seven test would fail.
    /// </para>
    /// <para>
    /// Field 1 also ends in a bare line feed, matching the real producer: the info field is
    /// <c>"PowerFramework Runtime Error\n-10000"</c> [pfw.sra:L115 note], and that embedded LF is
    /// likewise not a boundary.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASevenFieldPayloadSplitsBackIntoSevenDespiteMultiLineFields()
    {
        AssertionFailure failure = new()
        {
            Info = "PowerFramework Runtime Error" + IntraFieldSeparator + "-10000",
            WindowMenu = "w_test_assert",
            Object = "n_cst_dwsvc",
            ObjectEvent = "of_calcitem",
            Line = 1234L,
            StackTraceInfo = string.Join(
                IntraFieldSeparator,
                "outermost line:1",
                "middle line:2",
                "innermost line:3"),
        };

        string payload = string.Join(
            FieldDelimiter,
            failure.Info,
            failure.WindowMenu,
            failure.Object,
            failure.ObjectEvent,
            failure.Line.ToString(System.Globalization.CultureInfo.InvariantCulture),
            failure.StackTraceInfo,
            "trailing seventh field");

        failure.SetMessage(payload);

        string[] fields = failure.Message.Split(FieldDelimiter);

        Assert.Equal(7, fields.Length);
        Assert.Equal(failure.Info, fields[0]);
        Assert.Equal("1234", fields[4]);
        Assert.Equal(failure.StackTraceInfo, fields[5]);
        Assert.Equal("trailing seventh field", fields[6]);

        // The multi-line field really was multi-line, so the claim is not vacuous.
        Assert.Equal(3, fields[5].Split(IntraFieldSeparator).Length);
    }

    /// <summary>
    /// A two-field payload splits into exactly two - the SHALLOW shape, which is the other of the only
    /// two shapes that exist.
    /// </summary>
    /// <remarks>
    /// The consumer requires at least two fields [pfw.sra:L116], so this shape is the minimum viable
    /// payload and is what an assert with no object context produces. Pinned alongside the deep shape
    /// so both legal shapes are covered and the illegal middle ground below reads as the exception it
    /// is.
    /// </remarks>
    [Fact]
    public void ATwoFieldPayloadSplitsBackIntoTwo()
    {
        AssertionFailure failure = new();
        failure.SetMessage(string.Join(FieldDelimiter, "PowerFramework Runtime Error", "-10000"));

        string[] fields = failure.Message.Split(FieldDelimiter);

        Assert.Equal(2, fields.Length);
        Assert.Equal("PowerFramework Runtime Error", fields[0]);
        Assert.Equal("-10000", fields[1]);
    }

    /// <summary>
    /// PRESERVED DEFECT: a deep-shape payload whose SEVENTH field is empty splits into SIX segments,
    /// so it silently fails the consumer's exactly-seven test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reproduced rather than corrected, under C-B. The mechanism is that a trailing empty field
    /// produces no trailing segment once the delimiter is consumed - so the payload the producer
    /// believes is seven fields wide arrives as six, lands in neither of the consumer's two accepted
    /// shapes, and is dropped without a diagnostic.
    /// </para>
    /// <para>
    /// Asserted executably so the defect is recorded where a maintainer will meet it, rather than only
    /// in prose. The correct fix - were correction permitted - would be for the consumer to split with
    /// a preserving option or for the producer to emit a sentinel; both would change observable
    /// behaviour, so neither is applied.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADeepPayloadWithAnEmptySeventhFieldSplitsIntoSixWhichIsThePreservedDefect()
    {
        AssertionFailure failure = new();

        // Seven fields as the producer counts them - the last one empty.
        failure.SetMessage(string.Join(
            FieldDelimiter,
            "PowerFramework Runtime Error" + IntraFieldSeparator + "-10000",
            "w_test_assert",
            "n_cst_dwsvc",
            "of_calcitem",
            "1234",
            "n_cst_dwsvc.of_calcitem line:1234",
            string.Empty));

        string[] fields = failure.Message.Split(FieldDelimiter);

        // The producer wrote seven. Six arrive with the trailing empty removed, which is what the
        // legacy consumer sees - and it accepts only two or seven.
        Assert.Equal(7, fields.Length);
        Assert.Equal(string.Empty, fields[^1]);

        // The defect is in the CONSUMER'S reading, so it is stated the way the consumer reads:
        // trailing empties discarded, which is what a PowerScript-style split produces.
        string[] asTheConsumerSees = failure.Message.Split(
            FieldDelimiter,
            StringSplitOptions.None);
        string[] withoutTrailingEmpties = asTheConsumerSees
            .Reverse()
            .SkipWhile(string.IsNullOrEmpty)
            .Reverse()
            .ToArray();

        Assert.Equal(6, withoutTrailingEmpties.Length);
        Assert.NotEqual(7, withoutTrailingEmpties.Length);
    }

    /// <summary>
    /// <see cref="Environment.NewLine"/> is NEVER the delimiter, and on this platform it is not even
    /// CRLF - which is why using it would be a platform-dependent defect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The delimiter is the two literal characters "\r\n" on every platform. <c>Environment.NewLine</c>
    /// is CRLF on Windows and a bare LF on Linux, and these services run in Linux containers - so a
    /// payload joined with it would split into the right number of fields on a developer's Windows
    /// machine and collapse into ONE field in production, taking the whole seven-field protocol with
    /// it.
    /// </para>
    /// <para>
    /// Demonstrated rather than asserted abstractly: the same seven fields joined with
    /// <c>Environment.NewLine</c> are shown to split into a DIFFERENT number of fields than when joined
    /// with the literal delimiter, on whichever platform the test runs.
    /// </para>
    /// </remarks>
    [Fact]
    public void EnvironmentNewLineIsNotTheDelimiterAndWouldBreakTheProtocolHere()
    {
        string[] sevenFields =
            ["field one", "field two", "field three", "field four", "5", "field six", "field seven"];

        AssertionFailure correct = new();
        correct.SetMessage(string.Join(FieldDelimiter, sevenFields));
        Assert.Equal(7, correct.Message.Split(FieldDelimiter).Length);

        AssertionFailure wrong = new();
        wrong.SetMessage(string.Join(Environment.NewLine, sevenFields));

        if (Environment.NewLine == FieldDelimiter)
        {
            // Windows: the two happen to agree, so the hazard is latent rather than absent.
            Assert.Equal(7, wrong.Message.Split(FieldDelimiter).Length);
        }
        else
        {
            // Linux, which is this platform: the payload collapses into a single field.
            Assert.Equal("\n", Environment.NewLine);
            Assert.Single(wrong.Message.Split(FieldDelimiter));
        }
    }

    /// <summary>
    /// The carrier never touches the payload: whatever string is set is returned byte for byte, with no
    /// trimming, normalizing or line-ending conversion.
    /// </summary>
    /// <remarks>
    /// The consumer's first act is to split the message [pfw.sra:L115], so any adjustment here - even
    /// trimming trailing whitespace, which looks harmless - changes the field count or the field
    /// contents. The rows include leading and trailing whitespace, a lone CR, a lone LF and a full
    /// CRLF at the boundaries, because those are exactly the characters a well-meant normalization
    /// would touch.
    /// </remarks>
    [Theory]
    [InlineData("  leading and trailing  ")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("trailing crlf\r\n")]
    [InlineData("\r\nleading crlf")]
    [InlineData("mixed \r inner \n endings \r\n here")]
    [InlineData("")]
    public void TheMessageIsStoredAndReturnedVerbatim(string payload)
    {
        AssertionFailure failure = new();
        failure.SetMessage(payload);

        Assert.Equal(payload, failure.Message);
    }
}
