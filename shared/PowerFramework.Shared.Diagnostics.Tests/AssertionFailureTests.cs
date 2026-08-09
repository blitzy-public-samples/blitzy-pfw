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
//  THE SEAM-DRIVEN HALF OF THIS SUITE, AND WHY IT CANNOT BE DONE ANY OTHER WAY
//  ------------------------------------------------------------------------------------------------
//  Sections 1 to 5 exercise the carrier in isolation - what it defaults to, what it stores, what it
//  hands back. Sections 6 to 10 exercise it AS THE PRODUCER FILLS IT, through the internal
//  Assertions.BuildFailure seam, because five of the behaviours this file is responsible for pinning
//  are not properties of the carrier at all: they are properties of the act of filling it.
//
//  A LIVE CAPTURE CANNOT PIN THEM. Assertions.AssertFailed captures the real stack, and inside a test
//  host that stack carries runner frames that differ by runner version and by whether the test is a
//  fact or a theory - so which frame gets blamed, and therefore every one of the four location
//  members, is not reproducible. BuildFailure takes the frames as an argument instead, and
//  LegacyStackFrames supplies them as literals, which is what makes the deep shape, the shallow
//  shape and every parse branch reachable deterministically. Section 9 still drives the LIVE thrower
//  once, because catchability is the one thing the seam cannot demonstrate: the seam returns the
//  failure, it does not throw it.
//
//  TWO DEFECT-SHAPED EXPECTATIONS ARE PINNED HERE DELIBERATELY (C-B)
//  ------------------------------------------------------------------------------------------------
//  Both look like bugs and neither is. Each carries its own deliberate-legacy comment at the point it
//  is asserted, and they are named together here so that a reader who finds one goes looking for the
//  other rather than "fixing" it:
//
//  D-A  THE LOCATION SUFFIX LANDS ON THE DETAIL MEMBER AND NOT ON PAYLOAD FIELD TWO. The producer
//       copies field 2 into Info by VALUE [assert.srf:L35] and only afterwards appends
//       "at Object::Event(Line)" to Info alone [assert.srf:L71]. Field 2 is never re-read, so in the
//       deep shape the two strings differ - Info is strictly longer - and they must not be treated as
//       interchangeable. Section 8 asserts this from the MEMBER side; the payload side is
//       AssertPayloadProtocolTests' subject, and the divergence has to be asserted from BOTH or a
//       regression hides on whichever side is untested.
//
//  D-B  ON A SHALLOW STACK FIVE MEMBERS STAY EMPTY RATHER THAN BEING FILLED WITH PLACEHOLDERS. The
//       whole population block sits behind one gate, `if nCount > 2` [assert.srf:L37], so a stack of
//       two frames or fewer simply never enters it. Section 10 asserts the emptiness. Emitting
//       placeholders instead would be a behaviour change twice over: the members would lie, and the
//       payload would grow from two fields to seven and so would stop matching the only two shapes
//       the consumer accepts [pfw.sra:L116,L119].
//
//  TWO TECHNOLOGY-SPECIFIC DECISIONS THIS SUITE COVERS, NAMED WHERE THEY ARE COVERED (C-K)
//  ------------------------------------------------------------------------------------------------
//  T-A  THE LEGACY MEMBER NAMES CARRY POWERBUILDER'S '#' PREFIX AND IT IS DROPPED. The oracle
//       declares #Info, #WindowMenu, #Object, #ObjectEvent, #Line, #StackTraceInfo and #StackTrace[]
//       [assertionfailed.sru:L18-L24]. '#' is a legal PowerScript identifier character and an illegal
//       C# one, so the prefix cannot be spelled here and is dropped. NOTHING ELSE about any spelling
//       changes, including casing, because these names appear in serialized payloads, log records and
//       characterization recordings where a rename would silently invalidate every stored comparison
//       (AAP 0.4.5.3). Section 4 audits the resulting names against that list.
//
//  T-B  THE TYPE DERIVES FROM A BASE CLASS LIBRARY EXCEPTION, NOT FROM THE LEGACY runtimeerror. The
//       oracle declares `from runtimeerror` [assertionfailed.sru:L4,L8], the PowerBuilder runtime's
//       own throwable root, which has no .NET counterpart to reference - so System.Exception
//       substitutes for it. The consequence that MATTERS is negative and is asserted in section 9:
//       pfwexception ALSO declares `from runtimeerror` [pfwexception.sru:L7], so the two legacy types
//       are SIBLINGS, and only pfwexception overrides setmessage to prefix its message
//       [pfwexception.sru:L23]. Inheriting that prefix here would corrupt payload field 1 and break
//       the consumer's numeric parse [pfw.sra:L117], so non-assignability to PfwException is asserted
//       as parity, not as preference.
//
//  RULES POSITION
//  review_rules returns "No user rules provided." - so NO user rule governs this file, none was
//  invented, and the absence is not read as licence to lower the bar. AAP 0.7.2 (the enterprise
//  baseline) and AAP 0.7.3 (the twelve binding non-rule constraints) apply in their place. Cited
//  inline where each applies: C-B (replicate behaviour and defects), C-H (every member read, both
//  composition paths covered, warning-clean), C-K (document boundary decisions), C-C (ws_objects/**
//  read as specification only, never opened at run time), AAP 0.4.5.3 (identifier spellings survive
//  verbatim), AAP 0.4.5.4 (the one-based to zero-based hazard). No path under services/ is referenced
//  and no deferred capability is named anywhere in this file (C-A, C-D).
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using PowerFramework.Shared.Kernel;
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
    /// Payload field 1 verbatim: the failure number, as a STRING literal. [assert.srf:L22]
    /// </summary>
    /// <remarks>
    /// Spelled here independently of the production code on purpose. The consumer parses it back with
    /// <c>Error.Number = Long(sMessages[1])</c> [pfw.sra:L117], so its textual form is the contract,
    /// and a suite that read the value from the implementation could not detect the implementation
    /// changing it.
    /// </remarks>
    private const string FailureNumberField = "-10000";

    /// <summary>
    /// The bare assertion text that opens payload field 2, before any info and before any suffix.
    /// [assert.srf:L24]
    /// </summary>
    private const string BareAssertionText = "Assertion failed";

    /// <summary>
    /// The literal that opens the location suffix: the word <c>at</c> followed by EXACTLY ONE SPACE.
    /// [assert.srf:L71]
    /// </summary>
    /// <remarks>
    /// Broken out as its own constant because the space is the whole of it. Dropping that one space is
    /// a mutation the suite must catch, and a constant naming the space explicitly is what makes the
    /// expectation auditable against <c>assert.srf:L71</c> character for character.
    /// </remarks>
    private const string LocationSuffixIntroducer = "at ";

    /// <summary>
    /// The separator between the object and the event inside the location suffix: a DOUBLE colon.
    /// [assert.srf:L71]
    /// </summary>
    /// <remarks>
    /// A double colon and not a dot, even though every frame the parse reads uses dots. The producer
    /// writes <c>"::"</c>, so <c>"::"</c> is what the suffix carries; rendering it as a dot would make
    /// the suffix look like a frame and is the second mutation this suite must catch.
    /// </remarks>
    private const string LocationSuffixScopeSeparator = "::";

    /// <summary>
    /// The number of CRLF-delimited fields in the SHALLOW payload shape. [assert.srf:L22-L24]
    /// </summary>
    private const int ShallowFieldCount = 2;

    /// <summary>
    /// The number of CRLF-delimited fields in the DEEP payload shape. [assert.srf:L22-L24,L66-L70]
    /// </summary>
    private const int DeepFieldCount = 7;

    /// <summary>
    /// The zero-based position of payload field 2 once <see cref="Exception.Message"/> is split on
    /// <see cref="FieldDelimiter"/>.
    /// </summary>
    /// <remarks>
    /// Named rather than spelled as a bare <c>1</c> at each use, because <c>sMessages[2]</c>
    /// [assert.srf:L35] is ONE-based and its C# index is one less - exactly the translation AAP 0.4.5.4
    /// names the most dangerous mechanical hazard in this refactor. One named constant is one place for
    /// that subtraction to be checked instead of several places for it to be repeated.
    /// </remarks>
    private const int FieldTwoIndex = 1;

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

    // ==============================================================================================
    //  6. THE SEAM: HOW THE PRODUCER FILLS THE CARRIER, AND THE MATRICES THAT DRIVE IT
    // ==============================================================================================
    //  Everything below reaches PowerFramework.Shared.Diagnostics.Assertions.BuildFailure, the
    //  internal seam that builds a failure from a SUPPLIED frame list instead of from the live stack.
    //  It is reachable because the Diagnostics project declares InternalsVisibleTo for this assembly
    //  and this project pins its AssemblyName to match; that is the only route, and it must never be
    //  replaced with reflection.
    //
    //  ws_objects/** IS READ AS SPECIFICATION AND NOTHING MORE (C-C). Every expectation below is a
    //  literal authored by reading the oracle. No test here opens a file, touches the network or reads
    //  an environment variable, so nothing under ws_objects is a build input or a run-time input.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a DEEP-shape failure around a chosen calling frame.
    /// </summary>
    /// <param name="callerFrame">The frame the producer will select and blame [assert.srf:L38].</param>
    /// <param name="info">The caller's info text; the empty string means none [assert.srf:L25].</param>
    /// <returns>The populated failure, not thrown.</returns>
    /// <remarks>
    /// The frame count handed to the seam is the array's own length, which is what a real capture
    /// reports. The seam takes the count SEPARATELY because the legacy holds it in its own local and a
    /// swallowed capture failure leaves that local at zero independently of the array
    /// [assert.srf:L30-L32]; passing the length is the consistent pair, and no test below fabricates an
    /// inconsistent one.
    /// </remarks>
    private static AssertionFailure BuildDeep(string callerFrame, string info)
    {
        string[] callStack = LegacyStackFrames.Deep(callerFrame);

        return Assertions.BuildFailure(callStack, callStack.Length, info);
    }

    /// <summary>
    /// Builds a SHALLOW-shape failure - a frame count of two or fewer, which fails the population gate
    /// at <c>assert.srf:L37</c>.
    /// </summary>
    /// <param name="frameCount">Zero, one, or the boundary count two.</param>
    /// <param name="info">The caller's info text; the empty string means none.</param>
    /// <returns>The populated failure, not thrown.</returns>
    private static AssertionFailure BuildShallow(int frameCount, string info)
    {
        string[] callStack = LegacyStackFrames.Shallow(frameCount);

        return Assertions.BuildFailure(callStack, frameCount, info);
    }

    /// <summary>
    /// Composes payload field 2 the way the producer does: the bare text, then a BARE LINE FEED and the
    /// info when the info is not the empty string. [assert.srf:L24-L27]
    /// </summary>
    /// <param name="info">The caller's info text.</param>
    /// <returns>The expected field 2, which is also the expected shallow-shape detail text.</returns>
    /// <remarks>
    /// The test is against the EMPTY STRING exactly as the legacy writes it - not a null test and not a
    /// whitespace test - so a whitespace-only info DOES produce a separator and a continuation line.
    /// </remarks>
    private static string ExpectedFieldTwo(string info) =>
        info != string.Empty ? BareAssertionText + IntraFieldSeparator + info : BareAssertionText;

    /// <summary>
    /// Composes the location suffix character for character from <c>assert.srf:L71</c>:
    /// <c>"\n"</c> + <c>"at "</c> + object + <c>"::"</c> + event + <c>"("</c> + line + <c>")"</c>.
    /// </summary>
    /// <param name="objectName">The value of the <c>Object</c> member - NOT <c>WindowMenu</c>.</param>
    /// <param name="objectEvent">The value of the <c>ObjectEvent</c> member.</param>
    /// <param name="line">The value of the <c>Line</c> member.</param>
    /// <returns>The expected suffix, INCLUDING its leading bare line feed.</returns>
    /// <remarks>
    /// <para>
    /// Assembled from named constants rather than from one opaque format string so that each of the
    /// four things a careless port gets wrong is independently visible: the leading separator is a bare
    /// line feed and not CRLF, the introducer carries exactly one trailing space, the scope separator is
    /// a double colon and not a dot, and the line number sits in bare parentheses with no spaces.
    /// </para>
    /// <para>
    /// The line number is rendered with the INVARIANT culture, matching the producer, so no hosting
    /// locale can inject a group separator or a non-ASCII negative sign into a value the consumer
    /// parses numerically [pfw.sra:L123].
    /// </para>
    /// </remarks>
    private static string ExpectedLocationSuffix(string objectName, string objectEvent, long line) =>
        IntraFieldSeparator + LocationSuffixIntroducer + objectName + LocationSuffixScopeSeparator +
        objectEvent + "(" + line.ToString(CultureInfo.InvariantCulture) + ")";

    /// <summary>
    /// The seven frame shapes the parse branches on, each with the four location members it must
    /// produce. [assert.srf:L39-L59]
    /// </summary>
    /// <remarks>
    /// <para>
    /// Columns: the frame text, then the expected <c>WindowMenu</c>, <c>Object</c>, <c>ObjectEvent</c>
    /// and <c>Line</c>. Every expected value was hand-traced character position by character position
    /// against the parse arithmetic; none was read back from the implementation, which is the only way
    /// a parity matrix can detect the implementation being wrong.
    /// </para>
    /// <para>
    /// Rows 1, 2 and 7 have <c>WindowMenu</c> DIFFERENT from <c>Object</c> - the two-or-more-dots
    /// branch [assert.srf:L42-L46]. Rows 3, 4 and 5 have them IDENTICAL, which is assigned outright at
    /// <c>assert.srf:L51</c> and is preserved legacy behaviour, not a duplication defect: the consumer
    /// tests <c>Error.WindowMenu &lt;&gt; Error.Object</c> to decide whether to print its window line
    /// [pfw.sra:L131]. Row 6 has no dot at all, so neither member is ever assigned and both keep the
    /// carrier's empty default - the <c>ObjectEvent</c> and <c>Line</c> steps still run because they sit
    /// OUTSIDE the dot guard.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string, string, long> DeepFrameParseMatrix =>
        new()
        {
            // TWO DOTS: the window and the object differ. Last dot 19, first dot 14.
            { LegacyStackFrames.TwoDotFrame, "w_test_assert", "cb_1", "clicked", 137L },

            // MANY DOTS: the parse looks only at the first and the last, so everything between them -
            // dots included - becomes the object. Faithful arithmetic, not a defect.
            {
                LegacyStackFrames.NamespacedFrame,
                "PowerFramework",
                "Shared.Kernel.Predicates",
                "IsSucceeded",
                12L
            },

            // ONE DOT: the object is assigned the window's value outright [assert.srf:L51].
            {
                LegacyStackFrames.SingleDotFrame,
                "n_cst_thread_trans",
                "n_cst_thread_trans",
                "of_connect",
                42L
            },

            // ONE DOT, from the oracle's own window function - the shape the oracle exercises by
            // default [w_test_assert.srw:L39].
            {
                LegacyStackFrames.WindowFunctionFrame,
                "w_test_assert",
                "w_test_assert",
                "wf_testassert",
                39L
            },

            // DEGRADED: a real release-build shape. Still one dot, so still the equal-members branch,
            // and the line number degrades to zero rather than failing the parse.
            { LegacyStackFrames.DegradedFrame, "<unknown>", "<unknown>", "?", 0L },

            // NO DOT: the whole dot block is skipped, so both members stay EMPTY while the event and
            // the line still parse. The space search restarts at position 1, so the entire leading
            // token becomes the event.
            { LegacyStackFrames.DotlessFrame, "", "", "dotlessframe", 7L },

            // NO SPACE AFTER THE METHOD: the event window computes to a NEGATIVE length and yields the
            // empty string, yet the line number still parses - losing the event does not lose the
            // location. The window and object split is identical to the first row.
            { LegacyStackFrames.NoSpaceAfterMethodFrame, "w_test_assert", "cb_1", "", 137L },
        };

    /// <summary>
    /// The three frame shapes for which <c>WindowMenu</c> and <c>Object</c> DIFFER - the only rows that
    /// can discriminate which of the two the location suffix names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Columns: the frame text, the expected <c>WindowMenu</c>, the expected <c>Object</c>.
    /// </para>
    /// <para>
    /// <b>Why a separate matrix rather than a filter over the full one.</b> On a one-dot frame the two
    /// members hold the same string, so a port that appended <c>WindowMenu</c> instead of
    /// <c>Object</c> would produce a byte-identical suffix and every one-dot row would still pass. Only
    /// these three rows can fail, so they are named as their own matrix to make that reasoning visible
    /// instead of buried in a predicate.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string> SuffixDiscriminatingMatrix =>
        new()
        {
            { LegacyStackFrames.TwoDotFrame, "w_test_assert", "cb_1" },
            { LegacyStackFrames.NamespacedFrame, "PowerFramework", "Shared.Kernel.Predicates" },
            { LegacyStackFrames.NoSpaceAfterMethodFrame, "w_test_assert", "cb_1" },
        };

    /// <summary>
    /// Every frame shape crossed with every info shape, for the assertions that must hold across both
    /// axes at once.
    /// </summary>
    /// <remarks>
    /// Columns: the frame text, the info text, and the expected <c>Object</c>, <c>ObjectEvent</c> and
    /// <c>Line</c>. The three expected values are carried in the row rather than read off the failure,
    /// so the composed suffix is an independent expectation and not a restatement of the implementation.
    /// The info axis is the empty string, the oracle's own info text
    /// [w_test_assert.srw:L42 <c>Assert(num &gt; 0,"Invalid Number!")</c>], and a MULTI-LINE info -
    /// which matters because a multi-line info puts a bare line feed inside field 2 and so proves the
    /// suffix's own leading line feed is not being confused with a field boundary.
    /// </remarks>
    public static TheoryData<string, string, string, string, long> DeepDetailMatrix
    {
        get
        {
            TheoryData<string, string, string, string, long> matrix = [];

            // Frame, expected Object, expected ObjectEvent, expected Line - the three the suffix uses.
            (string Frame, string Object, string Event, long Line)[] frames =
            [
                (LegacyStackFrames.TwoDotFrame, "cb_1", "clicked", 137L),
                (LegacyStackFrames.NamespacedFrame, "Shared.Kernel.Predicates", "IsSucceeded", 12L),
                (LegacyStackFrames.SingleDotFrame, "n_cst_thread_trans", "of_connect", 42L),
                (LegacyStackFrames.WindowFunctionFrame, "w_test_assert", "wf_testassert", 39L),
                (LegacyStackFrames.DegradedFrame, "<unknown>", "?", 0L),
                (LegacyStackFrames.DotlessFrame, "", "dotlessframe", 7L),
                (LegacyStackFrames.NoSpaceAfterMethodFrame, "cb_1", "", 137L),
            ];

            // None of these contains "::" or a line feed followed by "at ", so a suffix-absence
            // assertion over them cannot be satisfied by the info text itself.
            string[] infoTexts =
            [
                string.Empty,
                "Invalid Number!",
                "first line" + IntraFieldSeparator + "second line",
            ];

            foreach ((string frame, string objectName, string objectEvent, long line) in frames)
            {
                foreach (string info in infoTexts)
                {
                    matrix.Add(frame, info, objectName, objectEvent, line);
                }
            }

            return matrix;
        }
    }

    /// <summary>
    /// Every shallow frame count crossed with every info shape. [assert.srf:L37]
    /// </summary>
    /// <remarks>
    /// Columns: the frame count, the info text. The counts are zero - which is what a swallowed capture
    /// failure leaves behind [assert.srf:L30-L32] - then one, then the boundary count
    /// <c>MaxShallowFrameCount</c>, the largest value that still FAILS the gate. Including the boundary
    /// is the point: an implementation that wrote <c>&gt;=</c> instead of <c>&gt;</c> would pass the
    /// zero and one rows and fail only here.
    /// </remarks>
    public static TheoryData<int, string> ShallowShapeMatrix
    {
        get
        {
            TheoryData<int, string> matrix = [];

            int[] frameCounts = [0, 1, LegacyStackFrames.MaxShallowFrameCount];

            string[] infoTexts =
            [
                string.Empty,
                "Invalid Number!",
                "first line" + IntraFieldSeparator + "second line",
            ];

            foreach (int frameCount in frameCounts)
            {
                foreach (string info in infoTexts)
                {
                    matrix.Add(frameCount, info);
                }
            }

            return matrix;
        }
    }

    /// <summary>
    /// A spread of DEEP frame counts, for the trace-width assertions.
    /// </summary>
    /// <remarks>
    /// Starts at <c>MinimumDeepFrameCount</c>, the smallest count that PASSES the gate and therefore
    /// the boundary an off-by-one would break, and runs up through counts wide enough that a trimmed
    /// width of "all but two" is distinguishable from "all", from "one" and from "half".
    /// </remarks>
    public static TheoryData<int> DeepFrameCounts =>
        new() { LegacyStackFrames.MinimumDeepFrameCount, 4, 5, 6, 9, 12 };

    // ==============================================================================================
    //  7. THE SEVEN MEMBERS AS THE PRODUCER FILLS THEM, ON A DEEP STACK
    // ==============================================================================================

    /// <summary>
    /// On a deep stack the four location members carry the values parsed out of the BLAMED frame.
    /// [assert.srf:L38-L59]
    /// </summary>
    /// <param name="callerFrame">The frame the producer will select and blame.</param>
    /// <param name="expectedWindowMenu">The expected <c>WindowMenu</c>.</param>
    /// <param name="expectedObject">The expected <c>Object</c>.</param>
    /// <param name="expectedObjectEvent">The expected <c>ObjectEvent</c>.</param>
    /// <param name="expectedLine">The expected <c>Line</c>.</param>
    /// <remarks>
    /// <para>
    /// Asserted against the MEMBERS, never by re-splitting the message. The four members and payload
    /// fields 3 to 6 are the same values [assert.srf:L66-L69], but the payload's layout is a separate
    /// contract with its own suite, and a member-parity test that reached through the message would fail
    /// for a layout reason and be diagnosed as a parse bug.
    /// </para>
    /// <para>
    /// All four are asserted in one test rather than four because they are produced by one indivisible
    /// pass over one frame: a positional parse whose four searches are chained, each anchored at the
    /// position the previous one found. Splitting them would report four failures for one cause and
    /// would still not isolate it.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeepFrameParseMatrix))]
    public void TheDeepShapeParsesTheBlamedFrameIntoTheFourLocationMembers(
        string callerFrame,
        string expectedWindowMenu,
        string expectedObject,
        string expectedObjectEvent,
        long expectedLine)
    {
        AssertionFailure failure = BuildDeep(callerFrame, string.Empty);

        Assert.Equal(expectedWindowMenu, failure.WindowMenu);
        Assert.Equal(expectedObject, failure.Object);
        Assert.Equal(expectedObjectEvent, failure.ObjectEvent);
        Assert.Equal(expectedLine, failure.Line);
    }

    /// <summary>
    /// The frame the producer blames is the CALLER's, never either of the two innermost framework
    /// frames. [assert.srf:L38]
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole purpose of the <c>nCount - 2</c> selection is that an assertion report names the user's
    /// method rather than the assertion framework's. This asserts that outcome from both directions: the
    /// members match the frame the fixture NAMES as the caller, and no member carries text that could
    /// only have come from the assertion guard or from the payload builder.
    /// </para>
    /// <para>
    /// The caller is taken from <c>LegacyStackFrames.ExpectedCallerFrame</c> rather than by indexing the
    /// array. That is deliberate and AAP 0.4.5.4 is the reason: the one-based selection
    /// <c>sCallStack[nCount - 2]</c> becomes the zero-based index <c>frameCount - 3</c>, and a suite that
    /// recomputed that subtraction could get it wrong in the SAME direction as the code under test and
    /// pass while both were wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheBlamedFrameIsTheCallersAndNeitherInnermostFrameworkFrame()
    {
        string[] callStack = LegacyStackFrames.Deep();

        AssertionFailure failure = Assertions.BuildFailure(callStack, callStack.Length, string.Empty);

        // The fixture's default caller is the two-dot frame, so the window and the object differ.
        AssertionFailure fromNamedCaller =
            BuildDeep(LegacyStackFrames.ExpectedCallerFrame, string.Empty);

        Assert.Equal(fromNamedCaller.WindowMenu, failure.WindowMenu);
        Assert.Equal(fromNamedCaller.Object, failure.Object);
        Assert.Equal(fromNamedCaller.ObjectEvent, failure.ObjectEvent);
        Assert.Equal(fromNamedCaller.Line, failure.Line);

        // Neither framework frame was blamed. Both name a member of Assertions, so if either had been
        // selected the event member would carry that member's name.
        Assert.NotEqual("Assert", failure.ObjectEvent);
        Assert.NotEqual("AssertFailed", failure.ObjectEvent);
        Assert.DoesNotContain("Assertions", failure.Object, StringComparison.Ordinal);
        Assert.DoesNotContain("Assertions", failure.WindowMenu, StringComparison.Ordinal);
    }

    /// <summary>
    /// The stack-trace TEXT member is the trimmed frames joined by a SINGLE bare line feed, and it holds
    /// exactly <c>count - 2</c> lines. [assert.srf:L61-L64]
    /// </summary>
    /// <param name="frameCount">The deep frame count to build at.</param>
    /// <remarks>
    /// <para>
    /// Three separate properties, all of which a wrong join would break differently: the width is
    /// <c>count - 2</c> lines, the separator is a bare line feed, and the separator appears BETWEEN
    /// entries only - never leading and never trailing, which is what the producer's
    /// <c>if nIndex &gt; 1</c> guard buys.
    /// </para>
    /// <para>
    /// The CRLF-absence assertion is the load-bearing one. This text is payload field 7, and field
    /// boundaries are CRLF [assert.srf:L19]; a CRLF separator here would split field 7 into extra
    /// segments at the consumer and collapse the seven-field shape it insists on [pfw.sra:L119]. This is
    /// also why <see cref="Environment.NewLine"/> can never be used for the join - it IS CRLF on one of
    /// the two platforms.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeepFrameCounts))]
    public void TheStackTraceTextIsTheTrimmedFramesJoinedBySingleLineFeeds(int frameCount)
    {
        string[] callStack = LegacyStackFrames.WithDepth(frameCount);

        AssertionFailure failure = Assertions.BuildFailure(callStack, frameCount, string.Empty);

        Assert.Equal(LegacyStackFrames.ExpectedStackTraceInfo(callStack), failure.StackTraceInfo);

        // Exactly count - 2 lines: the trim keeps everything except the two innermost frames.
        Assert.Equal(
            frameCount - LegacyStackFrames.MaxShallowFrameCount,
            failure.StackTraceInfo.Split(IntraFieldSeparator).Length);

        // The separator is a BARE line feed. A CRLF anywhere here would be a field boundary.
        Assert.DoesNotContain(FieldDelimiter, failure.StackTraceInfo, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", failure.StackTraceInfo, StringComparison.Ordinal);

        // Between entries only: no leading and no trailing separator.
        Assert.False(
            failure.StackTraceInfo.StartsWith(IntraFieldSeparator, StringComparison.Ordinal),
            "The frame separator is inserted between entries only, never leading.");
        Assert.False(
            failure.StackTraceInfo.EndsWith(IntraFieldSeparator, StringComparison.Ordinal),
            "The frame separator is inserted between entries only, never trailing.");
    }

    /// <summary>
    /// The frame COLLECTION member holds exactly <c>count - 2</c> entries, outermost first.
    /// [assert.srf:L61-L62]
    /// </summary>
    /// <param name="frameCount">The deep frame count to build at.</param>
    /// <remarks>
    /// <para>
    /// The legacy appends with the upper-bound-plus-one idiom
    /// <c>ex.#StackTrace[UpperBound(ex.#StackTrace) + 1] = sCallStack[nIndex]</c>
    /// [assert.srf:L62] - the one-based append pattern AAP 0.4.5.4 flags as the single most dangerous
    /// mechanical hazard in this refactor. The ported collection must therefore hold
    /// <c>count - 2</c> items, NO MORE AND NO FEWER: one too many means a framework frame leaked in, one
    /// too few means the user's own frame was trimmed away.
    /// </para>
    /// <para>
    /// Note the asymmetry that makes this trap what it is, because both halves of it are asserted in
    /// this file: the SELECTION at <c>assert.srf:L38</c> shifts by one when translated to zero-based
    /// (<c>nCount - 2</c> becomes <c>frameCount - 3</c>) while the TRIM at <c>assert.srf:L61</c> does
    /// NOT, because one is an index and the other is a count. The expected sequence is taken from the
    /// fixture rather than sliced here, so no index arithmetic appears in this test at all.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeepFrameCounts))]
    public void TheFrameCollectionHoldsExactlyCountMinusTwoEntriesOutermostFirst(int frameCount)
    {
        string[] callStack = LegacyStackFrames.WithDepth(frameCount);

        AssertionFailure failure = Assertions.BuildFailure(callStack, frameCount, string.Empty);

        Assert.Equal(frameCount - LegacyStackFrames.MaxShallowFrameCount, failure.StackTrace.Count);

        // Sequence equality, so ORDER is pinned and not merely membership: outermost first.
        Assert.Equal(LegacyStackFrames.ExpectedTrimmedStack(callStack, frameCount), failure.StackTrace);

        // The outermost frame survives at the front, and the two innermost are gone from the back.
        Assert.Equal(LegacyStackFrames.DepthFrame(1), failure.StackTrace[0]);
        Assert.DoesNotContain(LegacyStackFrames.DepthFrame(frameCount), failure.StackTrace);
        Assert.DoesNotContain(LegacyStackFrames.DepthFrame(frameCount - 1), failure.StackTrace);
    }

    /// <summary>
    /// Neither innermost framework frame reaches the collection or the text. [assert.srf:L61]
    /// </summary>
    /// <remarks>
    /// The width assertions above prove that two frames were dropped; this proves WHICH two. Driven from
    /// the fixture chain whose two innermost frames are named after the assertion guard and the payload
    /// builder, so the exclusion is checked by identity rather than by counting.
    /// </remarks>
    [Fact]
    public void NeitherInnermostFrameworkFrameReachesTheTraceOrTheText()
    {
        string[] callStack = LegacyStackFrames.Deep();

        AssertionFailure failure = Assertions.BuildFailure(callStack, callStack.Length, string.Empty);

        Assert.DoesNotContain(LegacyStackFrames.AssertGuardFrame, failure.StackTrace);
        Assert.DoesNotContain(LegacyStackFrames.PayloadBuilderFrame, failure.StackTrace);
        Assert.DoesNotContain(
            LegacyStackFrames.AssertGuardFrame,
            failure.StackTraceInfo,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            LegacyStackFrames.PayloadBuilderFrame,
            failure.StackTraceInfo,
            StringComparison.Ordinal);

        // Everything from the outermost frame down to and including the caller DOES survive, in order.
        Assert.Equal(
            [
                LegacyStackFrames.OutermostFrame,
                LegacyStackFrames.SecondOutermostFrame,
                LegacyStackFrames.ExpectedCallerFrame,
            ],
            failure.StackTrace);
    }

    /// <summary>
    /// The stack-trace text and the frame collection agree: joining the collection reproduces the text
    /// exactly. [assert.srf:L62-L64]
    /// </summary>
    /// <param name="frameCount">The deep frame count to build at.</param>
    /// <remarks>
    /// The producer builds both in ONE pass over the same frames, so they cannot legitimately disagree.
    /// Pinning the agreement is what catches a port that split that pass in two and then let the halves
    /// drift - a refactor which would leave both the width assertions above still passing.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeepFrameCounts))]
    public void TheStackTraceTextIsExactlyTheJoinedFrameCollection(int frameCount)
    {
        string[] callStack = LegacyStackFrames.WithDepth(frameCount);

        AssertionFailure failure = Assertions.BuildFailure(callStack, frameCount, string.Empty);

        Assert.Equal(
            string.Join(LegacyStackFrames.FrameSeparator, failure.StackTrace),
            failure.StackTraceInfo);
    }


    // ==============================================================================================
    //  8. THE DETAIL TEXT, AND THE EXACT LOCATION SUFFIX
    // ==============================================================================================
    //  The suffix is the reason this section exists. It is appended at assert.srf:L71 and it is the one
    //  string in the whole payload that a port can get subtly, silently wrong: a dot instead of the
    //  double colon, a missing space after "at", the window instead of the object, or an append to the
    //  shared field-2 local instead of to the exception's own member. Each of those four is asserted
    //  below, and each is asserted by FULL STRING EQUALITY rather than by a substring probe, because a
    //  substring probe passes on three of the four.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// On a deep stack the detail member is the bare text, then the info when supplied, then the exact
    /// location suffix. [assert.srf:L24-L27,L35,L71]
    /// </summary>
    /// <param name="callerFrame">The frame the producer will blame.</param>
    /// <param name="info">The caller's info text; the empty string means none.</param>
    /// <param name="expectedObject">The <c>Object</c> the suffix must name.</param>
    /// <param name="expectedObjectEvent">The <c>ObjectEvent</c> the suffix must name.</param>
    /// <param name="expectedLine">The <c>Line</c> the suffix must carry.</param>
    /// <remarks>
    /// <para>
    /// FULL STRING EQUALITY, deliberately, and against a value composed entirely from the matrix row and
    /// this file's own constants - nothing is read back off the failure. A substring assertion would
    /// accept a suffix that was also duplicated, prefixed, mis-separated from the info, or appended
    /// twice; only equality pins the whole string.
    /// </para>
    /// <para>
    /// The three components are asserted individually as well, so that a failure says WHICH part moved
    /// rather than only that the string differs. That is worth the extra lines here because the expected
    /// value is long and a raw diff of two long strings is hard to read.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeepDetailMatrix))]
    public void TheDeepDetailTextIsTheBareTextThenTheInfoThenTheExactLocationSuffix(
        string callerFrame,
        string info,
        string expectedObject,
        string expectedObjectEvent,
        long expectedLine)
    {
        AssertionFailure failure = BuildDeep(callerFrame, info);

        string expectedBody = ExpectedFieldTwo(info);
        string expectedSuffix =
            ExpectedLocationSuffix(expectedObject, expectedObjectEvent, expectedLine);

        // The whole string, character for character.
        Assert.Equal(expectedBody + expectedSuffix, failure.Info);

        // And the three components, so a failure localises itself.
        Assert.StartsWith(BareAssertionText, failure.Info, StringComparison.Ordinal);
        Assert.StartsWith(expectedBody, failure.Info, StringComparison.Ordinal);
        Assert.EndsWith(expectedSuffix, failure.Info, StringComparison.Ordinal);
    }

    /// <summary>
    /// The suffix is spelled exactly as <c>assert.srf:L71</c> spells it: one space after <c>at</c>, a
    /// DOUBLE colon between object and event, and the line number in bare parentheses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The matrix-driven test above composes its expectation from the same named constants the helper
    /// uses, which pins the suffix against a mutation of the PRODUCER. This test pins the constants
    /// themselves against a mutation of the SUITE, by asserting one fully spelled-out literal for one
    /// known frame. Without it, editing <c>LocationSuffixScopeSeparator</c> to a dot would silently
    /// re-baseline every expectation in the section and the suite would go green against a broken port.
    /// </para>
    /// <para>
    /// The frame is the two-dot one, so the object and the window differ and the literal below is a
    /// discriminating value in its own right: it names <c>cb_1</c>, the object, and never
    /// <c>w_test_assert</c>, the window.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLocationSuffixIsSpelledExactlyAsTheOracleSpellsIt()
    {
        AssertionFailure failure = BuildDeep(LegacyStackFrames.TwoDotFrame, string.Empty);

        // Written out in full, with no constant and no composition, straight from assert.srf:L71.
        Assert.Equal("Assertion failed\nat cb_1::clicked(137)", failure.Info);

        // The same string, decomposed, so each named constant is independently confirmed to be what the
        // literal above requires it to be.
        Assert.Equal(
            BareAssertionText + ExpectedLocationSuffix("cb_1", "clicked", 137L),
            failure.Info);
        Assert.Contains(IntraFieldSeparator + "at ", failure.Info, StringComparison.Ordinal);
        Assert.Contains("cb_1::clicked", failure.Info, StringComparison.Ordinal);
        Assert.EndsWith("(137)", failure.Info, StringComparison.Ordinal);

        // Not a dot, and not a single colon: the separator between object and event is exactly "::".
        Assert.DoesNotContain("cb_1.clicked", failure.Info, StringComparison.Ordinal);
        Assert.DoesNotContain("cb_1:clicked", failure.Info, StringComparison.Ordinal);

        // The space after "at" is present, so the concatenated form never appears.
        Assert.DoesNotContain("atcb_1", failure.Info, StringComparison.Ordinal);
    }

    /// <summary>
    /// The suffix names the <c>Object</c> member and NOT the <c>WindowMenu</c> member. [assert.srf:L71]
    /// </summary>
    /// <param name="callerFrame">A frame shape for which the two members differ.</param>
    /// <param name="expectedWindowMenu">The expected <c>WindowMenu</c> - which must NOT appear.</param>
    /// <param name="expectedObject">The expected <c>Object</c> - which MUST appear.</param>
    /// <remarks>
    /// <para>
    /// <b>This is the discriminating test of the whole section.</b> The producer writes
    /// <c>ex.#Object</c> at <c>assert.srf:L71</c>, and the two members sit adjacent in the source
    /// [assert.srf:L66-L67], so appending the wrong one is a one-token slip. On a ONE-DOT frame the two
    /// hold the same string [assert.srf:L51], so such a port would emit a byte-identical suffix and
    /// every one-dot row in this file would still pass. Only a frame where they DIFFER can fail, which
    /// is why this theory is driven by the discriminating matrix rather than by the full one.
    /// </para>
    /// <para>
    /// The guard assertion comes first: the row is confirmed to be genuinely discriminating before
    /// anything is concluded from it. A row that silently stopped discriminating - because a fixture was
    /// edited - would otherwise turn this test into a tautology that could never fail.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(SuffixDiscriminatingMatrix))]
    public void TheLocationSuffixNamesTheObjectAndNeverTheWindowMenu(
        string callerFrame,
        string expectedWindowMenu,
        string expectedObject)
    {
        // The row must actually discriminate, or nothing below it means anything.
        Assert.NotEqual(expectedWindowMenu, expectedObject);

        AssertionFailure failure = BuildDeep(callerFrame, string.Empty);

        Assert.Equal(expectedWindowMenu, failure.WindowMenu);
        Assert.Equal(expectedObject, failure.Object);
        Assert.NotEqual(failure.WindowMenu, failure.Object);

        // The object is what the suffix introduces.
        Assert.Contains(
            IntraFieldSeparator + LocationSuffixIntroducer + expectedObject +
                LocationSuffixScopeSeparator,
            failure.Info,
            StringComparison.Ordinal);

        // The window never is.
        Assert.DoesNotContain(
            LocationSuffixIntroducer + expectedWindowMenu + LocationSuffixScopeSeparator,
            failure.Info,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// On a SHALLOW stack the location suffix is ABSENT, and the detail member is the bare text plus the
    /// info and nothing else. [assert.srf:L35,L37-L72]
    /// </summary>
    /// <param name="frameCount">A frame count of two or fewer.</param>
    /// <param name="info">The caller's info text; the empty string means none.</param>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED LEGACY BEHAVIOUR, ASSERTED DELIBERATELY (C-B).</b> The two shapes really do produce
    /// different detail text, and that is not an inconsistency to be smoothed over. The append at
    /// <c>assert.srf:L71</c> sits INSIDE the <c>if nCount &gt; 2</c> block that opens at
    /// <c>assert.srf:L37</c> and closes at <c>:L72</c>, so on a shallow stack it simply never runs -
    /// there is no location to report, because no frame was parsed. Synthesising a placeholder suffix
    /// such as <c>"at ::(0)"</c> would be a behaviour change, and it would also make every shallow
    /// failure claim a source location it never had.
    /// </para>
    /// <para>
    /// Absence is asserted three ways because full equality alone, while sufficient, reads as an
    /// accident: equality against the bare body, then the absence of the scope separator, then the
    /// absence of the introducer. None of the info texts in the matrix contains either token, so no row
    /// can satisfy the absence assertions through its own info text.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ShallowShapeMatrix))]
    public void TheShallowDetailTextCarriesNoLocationSuffix(int frameCount, string info)
    {
        AssertionFailure failure = BuildShallow(frameCount, info);

        Assert.Equal(ExpectedFieldTwo(info), failure.Info);
        Assert.DoesNotContain(LocationSuffixScopeSeparator, failure.Info, StringComparison.Ordinal);
        Assert.DoesNotContain(
            IntraFieldSeparator + LocationSuffixIntroducer,
            failure.Info,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The two shapes differ in exactly the suffix, and in nothing else. [assert.srf:L37-L72]
    /// </summary>
    /// <remarks>
    /// Holding the frame and the info constant and varying ONLY the depth isolates the difference to the
    /// gate. The deep detail text must be the shallow one plus the suffix - no re-wording, no re-ordering
    /// and no second copy of the info - which is the tightest available statement that the gate controls
    /// the suffix and nothing more.
    /// </remarks>
    [Fact]
    public void TheDeepDetailTextIsTheShallowOnePlusTheSuffixAndNothingElse()
    {
        const string Info = "Invalid Number!";

        AssertionFailure shallow = BuildShallow(LegacyStackFrames.MaxShallowFrameCount, Info);
        AssertionFailure deep = BuildDeep(LegacyStackFrames.TwoDotFrame, Info);

        Assert.Equal(
            shallow.Info + ExpectedLocationSuffix("cb_1", "clicked", 137L),
            deep.Info);
        Assert.StartsWith(shallow.Info, deep.Info, StringComparison.Ordinal);
        Assert.True(
            deep.Info.Length > shallow.Info.Length,
            "The deep detail text extends the shallow one; it never replaces it.");
    }

    /// <summary>
    /// The suffix reaches the DETAIL MEMBER and never payload field 2. [assert.srf:L35,L66-L71]
    /// </summary>
    /// <param name="callerFrame">The frame the producer will blame.</param>
    /// <param name="info">The caller's info text; the empty string means none.</param>
    /// <param name="expectedObject">The <c>Object</c> the suffix must name.</param>
    /// <param name="expectedObjectEvent">The <c>ObjectEvent</c> the suffix must name.</param>
    /// <param name="expectedLine">The <c>Line</c> the suffix must carry.</param>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED LEGACY BEHAVIOUR, ASSERTED DELIBERATELY (C-B). THIS IS THE SUBTLEST DIVERGENCE IN
    /// THE FILE.</b> The producer copies field 2 into the detail member at <c>assert.srf:L35</c> - a
    /// VALUE copy, because PowerScript strings assign by value - and then appends the suffix to the
    /// member alone at <c>assert.srf:L71</c>. Field 2 is never re-read after <c>:L35</c>, and the suffix
    /// is appended AFTER fields 3 to 7 have already been written at <c>:L66-L70</c>. So in the deep shape
    /// the member and field 2 are two different strings, and the member is strictly longer.
    /// </para>
    /// <para>
    /// It is observable on both sides, which is why it must be preserved rather than tidied: the oracle
    /// displays the member, WITH the suffix [w_test_assert.srw:L100], while the consumer assigns its
    /// error text from field 2, WITHOUT it [pfw.sra:L118]. Building field 2 from the member instead would
    /// compile, would look tidier, would keep every field count at seven - and would make the consumer
    /// silently report the source location twice in every assertion report.
    /// </para>
    /// <para>
    /// <b>Cross-reference.</b> This asserts the divergence from the MEMBER side. The payload side is
    /// <c>AssertPayloadProtocolTests</c>' subject. Both are required: a regression that corrupted only
    /// one of the two strings would be invisible to whichever side went untested, and this is exactly the
    /// pair where that could happen unnoticed.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeepDetailMatrix))]
    public void TheSuffixReachesTheDetailMemberButNeverPayloadFieldTwo(
        string callerFrame,
        string info,
        string expectedObject,
        string expectedObjectEvent,
        long expectedLine)
    {
        AssertionFailure failure = BuildDeep(callerFrame, info);

        string[] fields = failure.Message.Split(FieldDelimiter);
        Assert.Equal(DeepFieldCount, fields.Length);

        string fieldTwo = fields[FieldTwoIndex];
        string expectedSuffix =
            ExpectedLocationSuffix(expectedObject, expectedObjectEvent, expectedLine);

        // Field 2 is the bare body: it never receives the suffix.
        Assert.Equal(ExpectedFieldTwo(info), fieldTwo);
        Assert.DoesNotContain(LocationSuffixScopeSeparator, fieldTwo, StringComparison.Ordinal);
        Assert.DoesNotContain(
            IntraFieldSeparator + LocationSuffixIntroducer,
            fieldTwo,
            StringComparison.Ordinal);

        // The member is field 2 EXTENDED by the suffix - so the two diverge, and by exactly that much.
        Assert.Equal(fieldTwo + expectedSuffix, failure.Info);
        Assert.NotEqual(fieldTwo, failure.Info);
        Assert.StartsWith(fieldTwo, failure.Info, StringComparison.Ordinal);
        Assert.Equal(fieldTwo.Length + expectedSuffix.Length, failure.Info.Length);
    }

    /// <summary>
    /// On a shallow stack the detail member and payload field 2 are the SAME string, because the append
    /// that separates them never runs. [assert.srf:L35,L37]
    /// </summary>
    /// <param name="frameCount">A frame count of two or fewer.</param>
    /// <param name="info">The caller's info text; the empty string means none.</param>
    /// <remarks>
    /// The other half of the divergence, and the half that proves the divergence is caused by the SUFFIX
    /// rather than by the copy. If the two strings differed here as well, the cause would be something
    /// in the copy at <c>assert.srf:L35</c>; because they agree here and differ only in the deep shape,
    /// the append at <c>:L71</c> is established as the sole cause.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ShallowShapeMatrix))]
    public void TheShallowDetailMemberAndPayloadFieldTwoAreIdentical(int frameCount, string info)
    {
        AssertionFailure failure = BuildShallow(frameCount, info);

        string[] fields = failure.Message.Split(FieldDelimiter);
        Assert.Equal(ShallowFieldCount, fields.Length);

        Assert.Equal(fields[FieldTwoIndex], failure.Info);
    }


    // ==============================================================================================
    //  9. THE MESSAGE, AND THE FRAMEWORK PREFIX THAT MUST NOT BE THERE
    // ==============================================================================================
    //  The assertions here are mostly NEGATIVE, and negative assertions are worth stating plainly
    //  because they are the ones a reader is most likely to mistake for missing coverage.
    //
    //  The failure the section guards against is silent. If this type inherited pfwexception's
    //  message decoration, nothing would throw, no field count would change and no other assertion in
    //  this file would fail - the consumer's `Error.Number = Long(sMessages[1])` [pfw.sra:L117] would
    //  simply start parsing 0 instead of -10000, and every assertion failure in the system would
    //  report the wrong failure code from then on. So the absence of the prefix is asserted directly,
    //  and the contrast is DEMONSTRATED with a real PfwException rather than argued in prose.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The message is the CRLF-joined payload, and its FIRST field is exactly the failure number with
    /// nothing in front of it. [assert.srf:L19,L22,L74-L80]
    /// </summary>
    /// <param name="callerFrame">The frame the producer will blame.</param>
    /// <param name="info">The caller's info text; the empty string means none.</param>
    /// <param name="expectedObject">Unused by the assertions; carried by the shared matrix.</param>
    /// <param name="expectedObjectEvent">Unused by the assertions; carried by the shared matrix.</param>
    /// <param name="expectedLine">Unused by the assertions; carried by the shared matrix.</param>
    /// <remarks>
    /// <para>
    /// Field 1 is asserted by EQUALITY against the whole first segment, not by a starts-with probe. That
    /// distinction is the entire point: a starts-with probe against the segment would still pass if a
    /// prefix ending in a bare line feed were glued to the front, because the CRLF split would keep the
    /// prefix and the number in the same segment. Equality is what rejects that, and it is the same test
    /// the consumer effectively performs when it parses the segment as a number.
    /// </para>
    /// <para>
    /// The last three parameters are unused here and are declared only because the matrix is shared with
    /// the suffix theories. Re-declaring a near-identical matrix to avoid three unused parameters would
    /// create a second place for the frame expectations to drift, which is the worse trade.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeepDetailMatrix))]
    public void TheMessageIsTheCrlfJoinedPayloadWhoseFirstFieldIsExactlyTheFailureNumber(
        string callerFrame,
        string info,
        string expectedObject,
        string expectedObjectEvent,
        long expectedLine)
    {
        _ = expectedObject;
        _ = expectedObjectEvent;
        _ = expectedLine;

        AssertionFailure failure = BuildDeep(callerFrame, info);

        string[] fields = failure.Message.Split(FieldDelimiter);

        Assert.Equal(DeepFieldCount, fields.Length);

        // EXACTLY the magic literal, with no prefix of any kind before it.
        Assert.Equal(FailureNumberField, fields[0]);
        Assert.StartsWith(FailureNumberField + FieldDelimiter, failure.Message, StringComparison.Ordinal);

        // The message really is the join, so re-joining the fields reproduces it.
        Assert.Equal(string.Join(FieldDelimiter, fields), failure.Message);
    }

    /// <summary>
    /// The shallow shape carries the same undecorated field 1, over a two-field payload.
    /// [assert.srf:L22,L74-L80]
    /// </summary>
    /// <param name="frameCount">A frame count of two or fewer.</param>
    /// <param name="info">The caller's info text; the empty string means none.</param>
    /// <remarks>
    /// The message-composition path is covered on BOTH shapes, which C-H requires: the shallow payload is
    /// two fields and the deep one is seven, and there is no shape in between [pfw.sra:L116,L119]. The
    /// field count is asserted here as well as the field content, because a shallow payload that had
    /// grown to seven would be the observable symptom of the population gate having been widened.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ShallowShapeMatrix))]
    public void TheShallowMessageIsTwoFieldsWhoseFirstIsExactlyTheFailureNumber(
        int frameCount,
        string info)
    {
        AssertionFailure failure = BuildShallow(frameCount, info);

        string[] fields = failure.Message.Split(FieldDelimiter);

        Assert.Equal(ShallowFieldCount, fields.Length);
        Assert.Equal(FailureNumberField, fields[0]);
        Assert.Equal(ExpectedFieldTwo(info), fields[FieldTwoIndex]);
        Assert.Equal(FailureNumberField + FieldDelimiter + failure.Info, failure.Message);
    }

    /// <summary>
    /// <see cref="AssertionFailure"/> is NOT assignable to
    /// <see cref="PowerFramework.Shared.Kernel.PfwException"/>, and the two are SIBLINGS.
    /// [assertionfailed.sru:L4,L8; pfwexception.sru:L7,L23]
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PARITY, NOT PREFERENCE - AND TECHNOLOGY DECISION T-B FROM THIS FILE'S HEADER (C-K).</b> In the
    /// legacy both types declare <c>from runtimeerror</c>: <c>assertionfailed</c> at
    /// <c>assertionfailed.sru:L4</c> and <c>:L8</c>, and <c>pfwexception</c> at
    /// <c>pfwexception.sru:L7</c>. Neither derives from the other, so they are siblings under the
    /// PowerBuilder throwable root, and <c>System.Exception</c> substitutes for that root because it has
    /// no .NET counterpart to reference.
    /// </para>
    /// <para>
    /// <b>Why the negative matters so much.</b> Only <c>pfwexception</c> overrides <c>setmessage</c>, and
    /// its override prefixes every message with <c>"PowerFramework Runtime Error"</c> and a single BARE
    /// LINE FEED [pfwexception.sru:L23]. Were that inherited here, the stored payload would begin
    /// <c>"PowerFramework Runtime Error\n-10000\r\n..."</c>. The consumer splits on CRLF
    /// [pfw.sra:L115], and the prefix ends in a line feed rather than a carriage-return pair, so it
    /// would stay GLUED to field 1 - and <c>Error.Number = Long(sMessages[1])</c> [pfw.sra:L117] would
    /// parse 0 instead of -10000. Nothing would throw and no field count would change; the failure code
    /// would just be wrong forever. "Unifying the exception hierarchy" is the obvious-looking change that
    /// causes it, which is why this is asserted rather than left to be inferred from a class declaration.
    /// </para>
    /// <para>
    /// Asserted four ways so that no single refactor can satisfy it accidentally: neither direction of
    /// assignability holds, and each type's IMMEDIATE base is the throwable root itself - which is the
    /// assertion that fails the instant a <c>: PfwException</c> is introduced.
    /// </para>
    /// </remarks>
    [Fact]
    public void AssertionFailureIsNotAssignableToPfwExceptionAndTheTwoAreSiblings()
    {
        // Not a descendant of PfwException - the assertion that breaks the consumer's numeric parse.
        Assert.False(
            typeof(PfwException).IsAssignableFrom(typeof(AssertionFailure)),
            "AssertionFailure must not derive from PfwException: its message prefix would corrupt " +
            "payload field 1 and Long(sMessages[1]) would parse 0 instead of -10000 [pfw.sra:L117].");

        // Nor an ancestor of it, in case the hierarchy were unified the other way round.
        Assert.False(
            typeof(AssertionFailure).IsAssignableFrom(typeof(PfwException)),
            "The two legacy types are siblings under runtimeerror; neither derives from the other.");

        // Siblings: each type's IMMEDIATE base is the throwable root.
        Assert.Equal(typeof(Exception), typeof(AssertionFailure).BaseType);
        Assert.Equal(typeof(Exception), typeof(PfwException).BaseType);
    }

    /// <summary>
    /// The message never carries the framework-error phrase anywhere, in either shape.
    /// [pfwexception.sru:L23]
    /// </summary>
    /// <param name="callerFrame">The frame the producer will blame.</param>
    /// <param name="info">The caller's info text; the empty string means none.</param>
    /// <param name="expectedObject">Unused by the assertions; carried by the shared matrix.</param>
    /// <param name="expectedObjectEvent">Unused by the assertions; carried by the shared matrix.</param>
    /// <param name="expectedLine">Unused by the assertions; carried by the shared matrix.</param>
    /// <remarks>
    /// ANYWHERE, not merely at the front. A decoration inserted mid-payload would corrupt whichever field
    /// it landed in rather than field 1, so the absence is asserted over the whole message and over the
    /// detail member too. The phrase is taken from <c>PfwException.MessagePrefix</c> rather than
    /// re-spelled, so that a future change to the sibling's wording cannot leave this test asserting the
    /// absence of a string nothing produces any more.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeepDetailMatrix))]
    public void TheMessageNeverCarriesTheFrameworkErrorPhrase(
        string callerFrame,
        string info,
        string expectedObject,
        string expectedObjectEvent,
        long expectedLine)
    {
        _ = expectedObject;
        _ = expectedObjectEvent;
        _ = expectedLine;

        AssertionFailure failure = BuildDeep(callerFrame, info);

        Assert.DoesNotContain(PfwException.MessagePrefix, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(PfwException.MessagePrefix, failure.Info, StringComparison.Ordinal);
    }

    /// <summary>
    /// The contrast, demonstrated rather than argued: the SAME payload through
    /// <see cref="PowerFramework.Shared.Kernel.PfwException"/> loses field 1 to the prefix.
    /// [pfwexception.sru:L23; pfw.sra:L115,L117]
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes the negative assertions above load-bearing instead of decorative. It takes the
    /// real payload this type produces, puts it through the sibling type, and shows the concrete damage:
    /// the first CRLF-delimited segment is no longer the failure number, because the prefix ends in a
    /// BARE LINE FEED and therefore does not create a field boundary.
    /// </para>
    /// <para>
    /// It also pins the mechanism, so the diagnosis survives even if the wording changes: the prefix and
    /// its separator are read from the sibling's own published constants, and the separator is asserted
    /// NOT to be the field delimiter. Were the sibling to separate with CRLF instead, the prefix would
    /// become its own field and field 1 would merely SHIFT rather than be corrupted - a different defect
    /// with a different fix, and this assertion is what tells the two apart.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSamePayloadThroughPfwExceptionWouldCorruptFieldOneWhichIsWhyTheBaseTypeDiffers()
    {
        AssertionFailure failure = BuildDeep(LegacyStackFrames.TwoDotFrame, "Invalid Number!");
        string payload = failure.Message;

        // The undecorated carrier: field 1 is exactly the failure number.
        Assert.Equal(FailureNumberField, payload.Split(FieldDelimiter)[0]);

        // The sibling decorates, and the decoration is NOT a field boundary.
        PfwException decorated = new(payload);
        Assert.NotEqual(FieldDelimiter, PfwException.MessageSeparator);
        Assert.StartsWith(
            PfwException.MessagePrefix + PfwException.MessageSeparator,
            decorated.Message,
            StringComparison.Ordinal);

        // So field 1 is no longer the number: the prefix stays glued to it.
        string decoratedFieldOne = decorated.Message.Split(FieldDelimiter)[0];
        Assert.NotEqual(FailureNumberField, decoratedFieldOne);
        Assert.Equal(
            PfwException.MessagePrefix + PfwException.MessageSeparator + FailureNumberField,
            decoratedFieldOne);

        // And that is precisely the value the consumer would try to read as a number [pfw.sra:L117].
        Assert.False(
            long.TryParse(decoratedFieldOne, CultureInfo.InvariantCulture, out _),
            "The decorated field 1 does not parse as a number, which is the silent corruption the " +
            "sibling base type would introduce.");
        Assert.True(
            long.TryParse(
                payload.Split(FieldDelimiter)[0],
                CultureInfo.InvariantCulture,
                out long parsedNumber));
        Assert.Equal(-10000L, parsedNumber);
    }

    /// <summary>
    /// The failure is throwable and catchable as its EXACT type, which is how the oracle catches it.
    /// [assert.srf:L83; w_test_assert.srw:L99]
    /// </summary>
    /// <remarks>
    /// <para>
    /// Driven through the LIVE thrower rather than through the seam, because the seam returns the failure
    /// and does not throw it - catchability is the one property of this type the seam cannot demonstrate.
    /// The live stack inside a test host is deep, so this reaches the deep shape, but nothing here asserts
    /// a parsed member value: which frame a runner blames is not reproducible, and pinning it would make
    /// the suite fragile against a runner upgrade for no gain.
    /// </para>
    /// <para>
    /// <c>Assert.Throws</c> requires an EXACT type match, which is what makes it the right assertion for
    /// this: the oracle's catch names the type by name [w_test_assert.srw:L99], so a port that threw a
    /// base type or a wrapper would break the oracle's catch and must break this test too.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFailureIsThrownAndIsCatchableAsItsExactType()
    {
        const string Info = "Invalid Number!";

        AssertionFailure caught = Assert.Throws<AssertionFailure>(() => Assertions.AssertFailed(Info));

        // The payload arrived intact through the throw.
        Assert.Equal(FailureNumberField, caught.Message.Split(FieldDelimiter)[0]);
        Assert.StartsWith(BareAssertionText, caught.Info, StringComparison.Ordinal);
        Assert.Contains(Info, caught.Info, StringComparison.Ordinal);
        Assert.DoesNotContain(PfwException.MessagePrefix, caught.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The failure is also catchable as a GENERAL exception, with the detail and stack-trace members
    /// readable off it. [n_cst_eventful.sru:L876]
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second of the two legacy catch sites, and it catches by the throwable root rather than by
    /// name: the event broker wraps a subscriber call, catches whatever comes out, and reads the detail
    /// text and the stack-trace text off it. Both catch shapes therefore have to remain expressible, and
    /// they exercise different things - the exact-type catch pins the thrown type, this one pins that the
    /// members survive being reached through a base-typed reference.
    /// </para>
    /// <para>
    /// The general catch is written out rather than expressed with a throws-any helper on purpose: the
    /// legacy site catches a base type and then reads DERIVED members off the result, and only a real
    /// catch-and-narrow reproduces that shape.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFailureIsCatchableAsAGeneralExceptionWithItsMembersStillReadable()
    {
        const string Info = "Invalid Number!";

        string? detail = null;
        string? traceText = null;
        int? traceCount = null;

        try
        {
            Assertions.AssertFailed(Info);
        }
        catch (Exception caught)
        {
            // Caught by the root, then narrowed - exactly the broker's shape.
            AssertionFailure failure = Assert.IsAssignableFrom<AssertionFailure>(caught);
            detail = failure.Info;
            traceText = failure.StackTraceInfo;
            traceCount = failure.StackTrace.Count;
        }

        Assert.NotNull(detail);
        Assert.NotNull(traceText);
        Assert.NotNull(traceCount);

        Assert.StartsWith(BareAssertionText, detail, StringComparison.Ordinal);
        Assert.Contains(Info, detail, StringComparison.Ordinal);

        // A live capture in a test host is deep, so both trace members are populated, and they agree:
        // one line of text per collected frame. Only the RELATIONSHIP is asserted, never the frame
        // contents - which runner frames a real capture carries is not reproducible.
        Assert.NotEmpty(traceText);
        Assert.InRange(traceCount.Value, 1, int.MaxValue);
        Assert.Equal(traceCount.Value, traceText.Split(LegacyStackFrames.FrameSeparator).Length);
        Assert.DoesNotContain(FieldDelimiter, traceText, StringComparison.Ordinal);
    }


    // ==============================================================================================
    //  10. SHALLOW-STACK MEMBER STATE: THE FIVE MEMBERS THAT STAY EMPTY
    // ==============================================================================================
    //  PRESERVED LEGACY BEHAVIOUR THROUGHOUT THIS SECTION (C-B), and it is defect D-B from this file's
    //  header. The population block is guarded by a single comparison, `if nCount > 2`
    //  [assert.srf:L37], and a stack of two frames or fewer never enters it. So five of the seven
    //  members are left exactly as the carrier initialised them.
    //
    //  Nothing is "missing" and nothing needs defending against. Emitting placeholder values instead -
    //  "<unknown>", "?", -1, a synthesised frame - would be a behaviour change TWICE OVER: the members
    //  would assert a source location that was never determined, and fields 3 to 7 would be appended,
    //  taking the payload from two fields to seven. The consumer accepts exactly two or exactly seven
    //  and nothing between [pfw.sra:L116,L119], so a placeholder-filled shallow payload would be
    //  accepted as a DEEP one and its fabricated location would be reported as fact.
    //
    //  A frame count of zero is not a contrived input, either: it is what a swallowed capture failure
    //  leaves behind, because the count lives in its own local and the capture is wrapped in a
    //  try/catch that discards the error [assert.srf:L30-L32].
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// On a shallow stack the four location members are their empty defaults, the line number is zero and
    /// the frame collection is empty. [assert.srf:L37]
    /// </summary>
    /// <param name="frameCount">A frame count of two or fewer - zero, one, or the boundary count.</param>
    /// <param name="info">The caller's info text; the empty string means none.</param>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED LEGACY BEHAVIOUR, ASSERTED DELIBERATELY (C-B).</b> The producer never enters the
    /// population block, so it never assigns any of these. They are asserted as EMPTY and ZERO rather
    /// than merely as "not populated" because empty is the observable contract: the consumer reads the
    /// members straight out and would print whatever it found.
    /// </para>
    /// <para>
    /// Empty rather than null for the four strings, which matters to the producer as much as to the
    /// consumer: the trace text is built with <c>+=</c> [assert.srf:L64] and the suffix is appended with
    /// <c>+=</c> [assert.srf:L71], and a null start would either produce the literal text "null" or
    /// throw - inside the failure path, where an exception would replace the reported assertion with an
    /// unrelated one.
    /// </para>
    /// <para>
    /// The frame collection is asserted to be EMPTY BUT PRESENT. It is get-only and initialised by the
    /// carrier, so a null here would not be a shallow-shape concern at all - it would mean the collection
    /// had been replaced, which the type does not permit.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ShallowShapeMatrix))]
    public void TheShallowShapeLeavesTheFiveUnpopulatedMembersAtTheirDefaults(int frameCount, string info)
    {
        AssertionFailure failure = BuildShallow(frameCount, info);

        Assert.Equal(string.Empty, failure.WindowMenu);
        Assert.Equal(string.Empty, failure.Object);
        Assert.Equal(string.Empty, failure.ObjectEvent);
        Assert.Equal(string.Empty, failure.StackTraceInfo);
        Assert.Equal(0L, failure.Line);

        Assert.NotNull(failure.StackTrace);
        Assert.Empty(failure.StackTrace);
    }

    /// <summary>
    /// Even with five members empty, the detail member is FULLY FORMED, because it is assigned before the
    /// gate. [assert.srf:L35,L37]
    /// </summary>
    /// <param name="frameCount">A frame count of two or fewer.</param>
    /// <param name="info">The caller's info text; the empty string means none.</param>
    /// <remarks>
    /// <para>
    /// The ordering is the whole content of this test. <c>ex.#Info = sMessages[2]</c> sits at
    /// <c>assert.srf:L35</c>, two lines ABOVE the gate at <c>:L37</c>, so it runs unconditionally. A
    /// shallow failure is therefore still fully reportable - it carries the assertion text and the
    /// caller's info - and only the location is missing. Hoisting the assignment inside the gate would
    /// produce a failure whose detail text was empty, which is the one thing the consumer always displays
    /// [pfw.sra:L118].
    /// </para>
    /// <para>
    /// Asserted as full equality, plus non-emptiness stated separately: equality alone would still hold if
    /// both the expectation and the member became empty through some future change to the bare text.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ShallowShapeMatrix))]
    public void TheShallowShapeStillCarriesAFullyFormedDetailText(int frameCount, string info)
    {
        AssertionFailure failure = BuildShallow(frameCount, info);

        Assert.Equal(ExpectedFieldTwo(info), failure.Info);
        Assert.NotEmpty(failure.Info);
        Assert.StartsWith(BareAssertionText, failure.Info, StringComparison.Ordinal);

        // The info, when supplied, follows a BARE LINE FEED - never CRLF, which would split the field.
        if (info != string.Empty)
        {
            Assert.EndsWith(IntraFieldSeparator + info, failure.Info, StringComparison.Ordinal);
            Assert.DoesNotContain(FieldDelimiter, failure.Info, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The boundary is <c>&gt; 2</c> and not <c>&gt;= 2</c>: two frames stay shallow, three go deep.
    /// [assert.srf:L37]
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single comparison at <c>assert.srf:L37</c> is the only gate between the two payload shapes, so
    /// an off-by-one in it would silently move every three-frame failure into the wrong shape. This pins
    /// both sides of the boundary in one place, which is the only way the assertion is meaningful: a test
    /// that checked either side alone would pass against a gate that had been shifted by one.
    /// </para>
    /// <para>
    /// The two counts are adjacent by construction - <c>MaxShallowFrameCount</c> is the largest value that
    /// FAILS the gate and <c>MinimumDeepFrameCount</c> is the smallest that PASSES it - and the fixture's
    /// own constants are used rather than the literals 2 and 3, so the adjacency is asserted rather than
    /// assumed.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheShapeGateAdmitsThreeFramesAndRejectsTwo()
    {
        // The two counts must be adjacent, or the boundary is not being tested at all.
        Assert.Equal(
            LegacyStackFrames.MaxShallowFrameCount + 1,
            LegacyStackFrames.MinimumDeepFrameCount);

        AssertionFailure atBoundary =
            BuildShallow(LegacyStackFrames.MaxShallowFrameCount, string.Empty);

        string[] justOverBoundaryStack =
            LegacyStackFrames.WithDepth(LegacyStackFrames.MinimumDeepFrameCount);
        AssertionFailure justOverBoundary = Assertions.BuildFailure(
            justOverBoundaryStack,
            LegacyStackFrames.MinimumDeepFrameCount,
            string.Empty);

        // Two frames: shallow. Nothing populated, two payload fields.
        Assert.Equal(ShallowFieldCount, atBoundary.Message.Split(FieldDelimiter).Length);
        Assert.Empty(atBoundary.StackTrace);
        Assert.Equal(string.Empty, atBoundary.ObjectEvent);
        Assert.Equal(BareAssertionText, atBoundary.Info);

        // Three frames: deep. Exactly one frame survives the trim, and the suffix appears.
        Assert.Equal(DeepFieldCount, justOverBoundary.Message.Split(FieldDelimiter).Length);
        Assert.Single(justOverBoundary.StackTrace);
        Assert.Equal(LegacyStackFrames.DepthFrame(1), justOverBoundary.StackTrace[0]);
        Assert.Contains(
            IntraFieldSeparator + LocationSuffixIntroducer,
            justOverBoundary.Info,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A completely empty frame list - the swallowed-capture-failure case - still produces a usable, valid
    /// two-field failure. [assert.srf:L30-L32,L37]
    /// </summary>
    /// <remarks>
    /// <para>
    /// The most degraded input the producer can reach, and the one it must handle most gracefully, because
    /// it is already on the failure path: the capture threw, the error was discarded, and the count local
    /// was left at zero. The failure that comes out must still be reportable rather than throwing a second
    /// exception on top of the first.
    /// </para>
    /// <para>
    /// Asserted here as well as through the theory rows because this is the case whose INPUT is degenerate
    /// rather than merely small - an empty array, not a short one - and it is worth a named test that says
    /// so, so that the behaviour is findable by the scenario rather than only by the frame count.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnEmptyFrameListStillProducesAValidTwoFieldFailure()
    {
        AssertionFailure failure = Assertions.BuildFailure([], 0, string.Empty);

        Assert.Equal(FailureNumberField + FieldDelimiter + BareAssertionText, failure.Message);
        Assert.Equal(BareAssertionText, failure.Info);
        Assert.Equal(ShallowFieldCount, failure.Message.Split(FieldDelimiter).Length);

        Assert.Equal(string.Empty, failure.WindowMenu);
        Assert.Equal(string.Empty, failure.Object);
        Assert.Equal(string.Empty, failure.ObjectEvent);
        Assert.Equal(string.Empty, failure.StackTraceInfo);
        Assert.Equal(0L, failure.Line);
        Assert.Empty(failure.StackTrace);
    }


    // ==============================================================================================
    //  11. TECHNOLOGY DECISION T-A, ASSERTED RATHER THAN ONLY DECLARED
    // ==============================================================================================
    //  The header states T-A: the legacy member names carry PowerBuilder's '#' prefix, which is an
    //  illegal C# identifier character and is therefore dropped, and NOTHING ELSE about any spelling
    //  changes. This section turns that statement into an executable check.
    //
    //  WHY IT IS NEEDED ALONGSIDE THE SEVEN-MEMBER AUDIT IN SECTION 4. That audit reads its names from
    //  `nameof(AssertionFailure.Info)` and friends, so it is CIRCULAR with respect to spelling: rename
    //  the member and the expectation renames itself, and the audit stays green. The expectations below
    //  are LITERAL strings transcribed from assertionfailed.sru:L18-L24, so they cannot follow a
    //  rename. That matters because AAP 0.4.5.3 makes these spellings a contract rather than a style
    //  choice: they appear in serialized payloads, in log records and in characterization recordings,
    //  where a rename would silently invalidate every stored comparison.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The seven legacy member names exactly as <c>assertionfailed.sru:L18-L24</c> declares them,
    /// <b>including the PowerBuilder <c>#</c> prefix</b>.
    /// </summary>
    /// <remarks>
    /// Transcribed literals, in declaration order. The prefix is retained HERE, in the expectation, so
    /// that the test below can strip it and compare - which is what makes "the prefix is dropped and
    /// nothing else changes" a checkable statement rather than an assertion of good intent.
    /// </remarks>
    private static readonly string[] LegacyPayloadMemberNames =
    [
        "#Info",
        "#WindowMenu",
        "#Object",
        "#ObjectEvent",
        "#Line",
        "#StackTraceInfo",
        "#StackTrace",
    ];

    /// <summary>
    /// The PowerBuilder identifier prefix that cannot be spelled in C#. [assertionfailed.sru:L18-L24]
    /// </summary>
    private const string LegacyMemberNamePrefix = "#";

    /// <summary>
    /// Every member name is its legacy spelling with the <c>#</c> prefix removed and nothing else
    /// altered. [assertionfailed.sru:L18-L24]
    /// </summary>
    /// <remarks>
    /// <para>
    /// TECHNOLOGY DECISION T-A (C-K). <c>#</c> is a legal PowerScript identifier character and an illegal
    /// C# one, so the prefix could not be carried across even if that were desirable. Every other
    /// character survives, casing included: the legacy already used PascalCase for these members, so -
    /// unlike the preserved SCREAMING_SNAKE constants elsewhere in this refactor - they need no analyzer
    /// suppression, which is fortunate because the repository root <c>.editorconfig</c> scopes its naming
    /// suppressions to the named production files on its BAND 3 roster - the single source of truth for that
    /// list - and none of them is in this project. Under the inherited
    /// <c>TreatWarningsAsErrors</c> a member here that needed one would be an unfixable build error.
    /// </para>
    /// <para>
    /// The declaration ORDER is pinned as well as the spellings, because it is the order fields 3 to 7 are
    /// written in [assert.srf:L66-L70] and the order the consumer assigns them in [pfw.sra:L120-L124].
    /// Two adjacent members of the same type - <c>WindowMenu</c> and <c>Object</c>, both strings - would
    /// otherwise be transposable without any other test in this file noticing.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryMemberNameIsTheLegacySpellingWithOnlyTheHashPrefixDropped()
    {
        Assert.Equal(LegacyPayloadMemberNames.Length, PayloadMemberNames.Length);

        for (int index = 0; index < LegacyPayloadMemberNames.Length; index++)
        {
            string legacyName = LegacyPayloadMemberNames[index];

            // The legacy name really does carry the prefix, or the comparison below proves nothing.
            Assert.StartsWith(LegacyMemberNamePrefix, legacyName, StringComparison.Ordinal);

            string expectedManagedName = legacyName[LegacyMemberNamePrefix.Length..];

            // Same spelling, same casing, same position in the declaration order.
            Assert.Equal(expectedManagedName, PayloadMemberNames[index]);

            // And the member genuinely exists under that name on the type.
            Assert.NotNull(
                typeof(AssertionFailure).GetProperty(
                    expectedManagedName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        }

        // No member carries the prefix, which C# could not compile in the first place.
        Assert.All(
            PayloadMemberNames,
            name => Assert.DoesNotContain(
                LegacyMemberNamePrefix,
                name,
                StringComparison.Ordinal));
    }


}
