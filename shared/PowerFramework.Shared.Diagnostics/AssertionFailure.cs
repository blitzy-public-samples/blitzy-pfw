// ==============================================================================================
//  AssertionFailure - the structured assertion failure payload
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.common.pbl.src/assertionfailed.sru (37 lines)
//  ORACLE STATUS  That .sru is the ONLY specification for this type, and it is READ ONLY: it is
//                 the behavioural oracle for parity testing, never an edit target. Every member
//                 declared below cites the line of it that authorises the member.
//
//  FOUR MORE LEGACY PATHS WERE READ AS SPECIFICATION AND ARE EQUALLY READ ONLY:
//      ws_objects/pfw.common.pbl.src/assert.srf         the only producer. Creates this object
//                                                      empty, fills it, attaches the payload,
//                                                      throws it [assert.srf:L34-L83].
//      ws_objects/pfw.shared.pbl.src/pfwexception.sru   the SIBLING type. Read to establish that
//                                                      it is a sibling and not an ancestor; see
//                                                      the inheritance ruling on the type below.
//      ws_objects/pfw.pbl.src/pfw.sra                   the consumer. Splits the payload back
//                                                      into fields and then halts the process
//                                                      [pfw.sra:L111-L143].
//      ws_objects/pfw.tests.pbl.src/w_test_assert.srw   the behavioural oracle. Proves Info,
//                                                      Object, ObjectEvent and StackTraceInfo
//                                                      are consumed [w_test_assert.srw:L100].
//                                                      REFERENCE only per AAP section 0.2.2.3:
//                                                      never ported and never edited.
//
//  GOVERNING CONSTRAINTS. review_rules reports "No user rules provided", so NO user rule governs
//  this file and none was invented. In their place AAP section 0.7.2 (the enterprise baseline)
//  and section 0.7.3 (the twelve binding non-rule constraints) apply. Three of the twelve bind
//  this file, and each is discharged at the point it applies:
//
//      C-C  the legacy tree is read only and is the only specification. Discharged by the five
//           paths above being read and cited, never written, and by every member below carrying
//           its authorising locator.
//      C-B  no new features, no behaviour improvements, documented defects replicated. Discharged
//           by the deliberate-omissions list below and by the seven-member cap.
//      C-K  document every technology-specific and boundary-specific decision. Discharged by the
//           three substitutions below, the inheritance ruling, and the payload format contract.
//
//  THREE TECHNOLOGY SUBSTITUTIONS ARE MADE, AND ONLY THREE (C-K):
//
//  1. TYPE NAME.   assertionfailed  ->  AssertionFailure.
//     Not cosmetic. assertionfailed.sru:L14 declares `global assertionfailed assertionfailed`, a
//     global auto-instance whose name shadows its own type name. That is the THIRD occurrence of
//     the pattern in the estate, beyond the two AAP section 0.4.5.1 already names, and AAP
//     section 0.5.4.2's collision rule resolves it the same way for all three: the TYPE takes the
//     descriptive .NET name and NO global instance is created. AAP section 0.3.1 fixes the
//     descriptive name as AssertionFailure. Consumers searching the legacy spelling will find it
//     here, which is the whole reason the legacy spelling is recorded rather than discarded.
//
//  2. BASE TYPE.   runtimeerror  ->  System.Exception.
//     assertionfailed.sru:L4 and :L8 both declare `from runtimeerror`, the PowerBuilder runtime's
//     own throwable root. It has no .NET counterpart to reference, so the Base Class Library
//     throwable root substitutes for it directly. The substitution is exact in the one dimension
//     that is observable across it: the object is thrown [assert.srf:L83] and caught by type
//     [w_test_assert.srw:L99], and both survive unchanged.
//
//     The four inherited-property defaults the legacy declares on this type - objectname and
//     class both "assertionfailed", routinename "create", and `integer line = -1`
//     [assertionfailed.sru:L9-L12] - are properties of that inherited runtimeerror object, NOT
//     members of the seven-field payload [assertionfailed.sru:L18-L24]. They are therefore NOT
//     declared here: doing so would breach the seven-member cap C-B imposes, and no in-scope
//     consumer reads them. Read the Line member's own remarks before concluding otherwise; the
//     `-1` in that group is the single most inviting mistake in this file.
//
//  3. MEMBER NAMES.  The leading '#' is dropped. Nothing else about any spelling changes.
//     '#' is a legal PowerScript identifier character and an illegal C# one, so `#Info` cannot be
//     spelled here. Every other character, including casing, is carried over exactly, because
//     these names appear in serialized payloads, in log records and in characterization
//     recordings, where a rename would silently invalidate every stored comparison (AAP section
//     0.4.5.3). PascalCase is what the legacy already used, so unlike the preserved
//     SCREAMING_SNAKE constants elsewhere in this refactor these names need no analyzer
//     suppression - which matters, because the repository root .editorconfig scopes its naming
//     suppressions to ten named files and NOT ONE of them is in this project. With
//     TreatWarningsAsErrors inherited from Directory.Build.props, a member here that needed a
//     suppression would be a build ERROR with no way to grant it. Never introduce one.
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON (C-B). Recorded so the omissions read as
//  decisions rather than oversights, and so nobody "completes" this file by adding one:
//
//      * An eighth public member of any kind. The payload IS the published contract and it is
//        seven fields wide. In particular there is no member carrying the literal "assert": the
//        legacy consumer discriminates on `Error.Object = "assert"` [pfw.sra:L114], the name of
//        the throwing PowerBuilder function object, and the .NET analogue of that test is
//        catching this exception TYPE. Re-exposing the string would widen the contract to no end.
//
//      * A setmessage override that decorates the message. assertionfailed.sru contains no
//        setmessage override at all - the file declares only create and destroy
//        [assertionfailed.sru:L28-L36] - and that ABSENCE is the ported behaviour. See the
//        inheritance ruling on the type.
//
//      * A constructor taking the seven fields. The legacy creates the object EMPTY
//        [assert.srf:L34] and assigns members afterwards, and that order is load-bearing rather
//        than incidental: Info is copied from field 2 at :L35 and only later has the "at ..."
//        suffix appended at :L71, so a single all-fields constructor could not produce a payload
//        whose Info and field 2 differ - which in the deep shape they always do.
//
//      * A constructor taking an inner exception. The legacy has no inner-exception concept, so
//        such a constructor could carry no legacy semantics. It is not required to compile
//        warning-clean either: the standard-exception-constructors analyzer rule is not part of
//        the rule set this repository resolves, verified by building this project to zero
//        warnings without it.
//
//      * ToString, equality members, GetHashCode, ISerializable plumbing and [Serializable].
//        None has a legacy counterpart. A custom ToString would be actively harmful: it would
//        reformat the very payload the consumer parses. Exception.ToString is inherited unchanged
//        and continues to report the CLR stack trace, not the legacy frame list.
//
//      * Validation of any kind. No field is rejected for being empty, and no field is trimmed,
//        normalised or reordered. An empty field 7 is a real legacy hazard, described on the type
//        below, and it is documented there rather than defended against here.
//
//  DEPENDENCIES. Base Class Library only, and every one of them arrives through the
//  ImplicitUsings this project inherits, so the file needs no using directive whatsoever.
//  Deliberately NOT imported: PowerFramework.Shared.Kernel. This project references Kernel, but
//  that reference exists for the return-code predicates that the assertion overloads in Assert.cs
//  guard on. Nothing in THIS file needs a symbol from it, and the inheritance ruling on the type
//  below is the reason it must stay that way.
// ==============================================================================================

namespace PowerFramework.Shared.Diagnostics;

/// <summary>
/// The structured assertion failure payload, ported from
/// <c>ws_objects/pfw.common.pbl.src/assertionfailed.sru</c>.
/// </summary>
/// <remarks>
/// <para>
/// A data carrier that is also the throwable. It holds no logic beyond message storage: the seven
/// members below are filled by its only producer, <c>Assert</c>, which creates it empty
/// [assert.srf:L34], assigns the members, attaches the assembled payload through
/// <see cref="SetMessage"/> [assert.srf:L80] and throws it [assert.srf:L83]. Consumers catch it by
/// type and read the members directly, exactly as the legacy oracle does
/// [w_test_assert.srw:L99-L100].
/// </para>
/// <para>
/// The type is left unsealed because the legacy object is not final, but it introduces no new
/// virtual member of its own: <see cref="Message"/> overrides an inherited member and
/// <see cref="SetMessage"/> is deliberately non-virtual. Nothing in the legacy tree derives from
/// <c>assertionfailed</c>, so the guarantee that the message is stored undecorated cannot be
/// subverted by a descendant.
/// </para>
///
/// <para><b>INHERITANCE RULING - THIS TYPE MUST NOT DERIVE FROM <c>PfwException</c>.</b></para>
/// <para>
/// It derives directly from <see cref="Exception"/>, the substitute for the legacy
/// <c>runtimeerror</c> root. That is a deliberate ruling, not an oversight, and it is recorded
/// here because "unifying the exception hierarchy" is the obvious-looking change that would break
/// the entire seven-field protocol silently.
/// </para>
/// <para>
/// In the legacy the two types are SIBLINGS. <c>assertionfailed</c> declares
/// <c>from runtimeerror</c> [assertionfailed.sru:L8] and <c>pfwexception</c> declares
/// <c>from runtimeerror</c> as well [pfwexception.sru:L7]; neither derives from the other. Only
/// <c>pfwexception</c> overrides <c>setmessage</c>, prefixing every message it carries with
/// <c>"PowerFramework Runtime Error"</c> and a single line feed [pfwexception.sru:L23].
/// <c>assertionfailed</c> has no such override anywhere in its file.
/// </para>
/// <para>
/// Were that decoration inherited here, the stored payload would begin
/// <c>"PowerFramework Runtime Error\n-10000\r\n..."</c>. The consumer splits on <c>"\r\n"</c>
/// [pfw.sra:L115], so the prefix - which ends in a bare line feed, not a carriage-return pair -
/// would stay glued to field 1, and <c>Error.Number = Long(sMessages[1])</c> [pfw.sra:L117] would
/// parse <c>0</c> instead of <c>-10000</c>. Nothing would throw, no field count would change and
/// no assertion would fail; the failure code would simply be wrong from then on. That is why this
/// ruling is stated rather than left to be inferred from the class declaration.
/// </para>
///
/// <para><b>THE PAYLOAD FORMAT CARRIED BY <see cref="Message"/> - THE PUBLISHED CONTRACT.</b></para>
/// <para>
/// <see cref="SetMessage"/> stores one string built by joining fields with a CRLF delimiter
/// [assert.srf:L19,L74-L78]. Any component that unpacks a caught
/// <see cref="AssertionFailure"/> - the Gateway system-error handler above all - reads the format
/// from here, so it is specified exhaustively:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <b>Field delimiter: CRLF, the two characters <c>"\r\n"</c></b>, and nothing else
///     [assert.srf:L19].
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Separators INSIDE a field: a bare line feed <c>"\n"</c></b>, never CRLF. Both places
///     this occurs use the bare form: the info continuation appended to field 2
///     [assert.srf:L26] and the frame separator inside <see cref="StackTraceInfo"/>
///     [assert.srf:L63]. That asymmetry is precisely what makes splitting on CRLF unambiguous,
///     so it is load-bearing rather than stylistic. It follows that
///     <see cref="Environment.NewLine"/> must NEVER be used for either: it is CRLF on Windows and
///     LF elsewhere, so it would corrupt the field structure on one platform or the other
///     whichever role it were used for.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Field 1</b> is the literal string <c>"-10000"</c> [assert.srf:L22]. The consumer reads
///     it as the failure number [pfw.sra:L117].
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Field 2</b> is <c>"Assertion failed"</c>, followed by a bare line feed and the caller's
///     info text when that text is non-empty [assert.srf:L24-L27]. The consumer reads it as the
///     failure text [pfw.sra:L118].
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Fields 3 to 7 exist in the deep shape only</b> and are, in order,
///     <see cref="WindowMenu"/>, <see cref="Object"/>, <see cref="ObjectEvent"/>,
///     <see cref="Line"/> rendered as a decimal string, and <see cref="StackTraceInfo"/>
///     [assert.srf:L66-L70]. The consumer assigns them in that same order [pfw.sra:L120-L124].
///     </description>
///   </item>
/// </list>
/// <para>
/// <b>Exactly two shapes exist: two fields, or seven. Never anything between.</b> The producer
/// emits fields 3 to 7 only inside a single guarded block [assert.srf:L37-L72], so the payload is
/// either the two mandatory fields or all seven. The consumer's tests encode that same
/// expectation: it requires at least two fields [pfw.sra:L116] and then tests for EXACTLY seven,
/// not at least seven [pfw.sra:L119]. A five- or six-field payload has no meaning to it.
/// </para>
/// <para>
/// <b><see cref="Info"/> and field 2 are NOT the same string in the deep shape.</b> Field 2 is
/// copied into <see cref="Info"/> first [assert.srf:L35], and only afterwards is the
/// <c>"at Object::Event(Line)"</c> location suffix appended to <see cref="Info"/> alone
/// [assert.srf:L71]. Field 2 never receives it. So in the deep shape <see cref="Info"/> is
/// strictly longer than field 2, and the two must not be treated as interchangeable.
/// </para>
/// <para>
/// <b>A single-dot calling frame makes <see cref="WindowMenu"/> and <see cref="Object"/>
/// identical.</b> The producer compares the first dot in the frame text with the last: when they
/// differ the two members are parsed from separate spans, but when the frame carries exactly one
/// dot the two are the same position, and <see cref="Object"/> is assigned the value of
/// <see cref="WindowMenu"/> outright [assert.srf:L49-L51]. That equality is what the consumer's
/// <c>Error.WindowMenu &lt;&gt; Error.Object</c> test detects when it suppresses the window line
/// from its rendered report [pfw.sra:L131]. Equal values are therefore normal and meaningful, not
/// a duplication defect.
/// </para>
/// <para>
/// <b>Legacy hazard, documented rather than defended against.</b> The consumer's own splitter
/// appends its trailing segment only when that segment is non-empty [pfw.sra:L64-L66], so a
/// deep-shape payload whose field 7 is empty splits into six segments, not seven, and silently
/// fails the exactly-seven test. That is pre-existing legacy behaviour reachable whenever
/// <see cref="StackTraceInfo"/> is empty while the deep shape is emitted. Per C-B it is preserved
/// and recorded, not corrected here.
/// </para>
/// </remarks>
public class AssertionFailure : Exception
{
    /// <summary>
    /// Backing store for <see cref="Message"/>, written only by <see cref="SetMessage"/>.
    /// </summary>
    /// <remarks>
    /// Nullable so that "never assigned" stays distinguishable from "assigned the empty string".
    /// <see cref="Exception"/> keeps its own message field private and offers no setter, so an
    /// overridable store is the only way to reproduce the legacy's assign-after-construction order
    /// [assert.srf:L34,L80]. This is the sole piece of private state in the type; the seven payload
    /// members are auto-properties.
    /// </remarks>
    private string? _message;

    /// <summary>
    /// Initialises an empty failure record, reproducing <c>Create AssertionFailed</c>
    /// [assert.srf:L34].
    /// </summary>
    /// <remarks>
    /// This is the constructor the legacy producer actually uses. The seven members are assigned
    /// afterwards and the payload is attached last through <see cref="SetMessage"/>, which is the
    /// order the format depends on - see the payload contract on the type.
    /// </remarks>
    public AssertionFailure()
    {
    }

    /// <summary>
    /// Initialises a failure record whose <see cref="Message"/> is <paramref name="message"/>,
    /// stored verbatim.
    /// </summary>
    /// <param name="message">
    /// The payload to carry. Stored exactly as supplied: no prefix, no suffix, no trimming and no
    /// newline normalisation. May be empty; emptiness is not rejected, per C-B.
    /// </param>
    /// <remarks>
    /// A convenience over the empty constructor plus <see cref="SetMessage"/> for the common case
    /// of a payload that is already assembled, and the shape consumers and characterization tests
    /// use to round-trip a known payload. It carries no decoration of any kind - see the
    /// inheritance ruling on the type for why that absence is the ported behaviour.
    /// </remarks>
    public AssertionFailure(string message) : base(message)
    {
    }

    /// <summary>
    /// Gets the CRLF-delimited failure payload, verbatim.
    /// </summary>
    /// <value>
    /// The string most recently passed to <see cref="SetMessage"/>; failing that, the string passed
    /// to <see cref="AssertionFailure(string)"/>; failing that, the base class default for an
    /// exception constructed without a message.
    /// </value>
    /// <remarks>
    /// <para>
    /// Overridden purely to make the legacy's assign-after-construction order expressible. The
    /// legacy sets the message through the <c>setmessage</c> it inherits from <c>runtimeerror</c>,
    /// and it does NOT override that member, so the text reaches the consumer undecorated. This
    /// override introduces no decoration either - the deliberate contrast with
    /// <c>pfwexception</c>'s prefixing override [pfwexception.sru:L23] is the whole point, and the
    /// inheritance ruling on the type explains what breaks if that contrast is lost.
    /// </para>
    /// <para>
    /// The fall-through to the base value keeps the two construction routes indistinguishable to a
    /// reader: constructing with a message and constructing empty then calling
    /// <see cref="SetMessage"/> both yield exactly the string supplied. The consumer's very first
    /// act is to split this value on CRLF [pfw.sra:L115], so any adjustment here - even trimming
    /// trailing whitespace - would shift every field index after the point of adjustment.
    /// </para>
    /// </remarks>
    public override string Message => _message ?? base.Message;

    /// <summary>
    /// Replaces <see cref="Message"/> with <paramref name="newMessage"/>, stored verbatim.
    /// Reproduces the inherited, UNDECORATED <c>setmessage</c> the legacy relies on
    /// [assert.srf:L80].
    /// </summary>
    /// <param name="newMessage">
    /// The assembled payload. Stored exactly as supplied: no prefix, no suffix, no trimming and no
    /// newline normalisation. May be empty.
    /// </param>
    /// <remarks>
    /// Called last by the producer, after all seven members have been assigned [assert.srf:L80],
    /// and repeatable - the most recent call wins, including over a value supplied to
    /// <see cref="AssertionFailure(string)"/>. Deliberately non-virtual: nothing in the legacy tree
    /// derives from <c>assertionfailed</c>, so no descendant may reintroduce the decoration this
    /// type exists to be free of.
    /// </remarks>
    public void SetMessage(string newMessage) => _message = newMessage;

    /// <summary>
    /// Gets or sets the human-readable failure text. Ports <c>string #Info</c>
    /// [assertionfailed.sru:L18].
    /// </summary>
    /// <value>
    /// Field 2 of the payload, plus the <c>"at Object::Event(Line)"</c> location suffix in the deep
    /// shape. Empty string until assigned, never <see langword="null"/>.
    /// </value>
    /// <remarks>
    /// Proven consumed by the oracle [w_test_assert.srw:L100]. Assigned from field 2
    /// [assert.srf:L35] and then extended with the location suffix [assert.srf:L71], which is why
    /// it is NOT interchangeable with field 2 of <see cref="Message"/> - see the payload contract
    /// on the type.
    /// </remarks>
    public string Info { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the window or menu that contains the failing routine. Ports
    /// <c>string #WindowMenu</c> [assertionfailed.sru:L19].
    /// </summary>
    /// <value>
    /// Field 3 of the payload. Empty string until assigned, never <see langword="null"/>, and empty
    /// in the shallow shape because the producer never reaches the assignment there.
    /// </value>
    /// <remarks>
    /// Parsed from the calling frame text [assert.srf:L44,L49]. Equal to <see cref="Object"/>
    /// whenever that text carries exactly one dot [assert.srf:L51]; the consumer uses that equality
    /// to suppress its window line [pfw.sra:L131], so equal values are meaningful rather than
    /// duplicated.
    /// </remarks>
    public string WindowMenu { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the object that owns the failing routine. Ports <c>string #Object</c>
    /// [assertionfailed.sru:L20].
    /// </summary>
    /// <value>
    /// Field 4 of the payload. Empty string until assigned, never <see langword="null"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// Proven consumed by the oracle [w_test_assert.srw:L100]. Parsed from the calling frame text
    /// [assert.srf:L46] or copied from <see cref="WindowMenu"/> in the single-dot case
    /// [assert.srf:L51], and also embedded in <see cref="Info"/>'s location suffix
    /// [assert.srf:L71].
    /// </para>
    /// <para>
    /// The legacy spelling is retained even though it matches the framework type name, because
    /// these names appear in stored characterization comparisons (AAP section 0.4.5.3). It is a
    /// member and not a type, so it cannot be confused with <see cref="System.Object"/> at any use
    /// site; within this type's own body the member wins, which is one reason nothing here
    /// references that type by its unqualified name.
    /// </para>
    /// </remarks>
    public string Object { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the event or function in which the assertion failed. Ports
    /// <c>string #ObjectEvent</c> [assertionfailed.sru:L21].
    /// </summary>
    /// <value>
    /// Field 5 of the payload. Empty string until assigned, never <see langword="null"/>.
    /// </value>
    /// <remarks>
    /// Proven consumed by the oracle [w_test_assert.srw:L100]. Parsed from the calling frame text
    /// [assert.srf:L56] and embedded in <see cref="Info"/>'s location suffix [assert.srf:L71].
    /// </remarks>
    public string ObjectEvent { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source line on which the assertion failed. Ports <c>long #Line</c>
    /// [assertionfailed.sru:L22].
    /// </summary>
    /// <value>
    /// Field 6 of the payload, rendered as a decimal string. <c>0</c> until assigned.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>This member's default is 0, and it must NOT be initialised to -1.</b> The invitation to
    /// do so is real: the same legacy file declares <c>integer line = -1</c> four lines earlier
    /// [assertionfailed.sru:L12]. That declaration is one of the inherited <c>runtimeerror</c>
    /// property defaults [assertionfailed.sru:L9-L12], a DIFFERENT member from this one, which is
    /// declared in the payload block [assertionfailed.sru:L18-L24] with no initialiser at all and
    /// therefore starts at the PowerScript numeric default of 0. The producer overwrites it from a
    /// parse of the calling frame text in every deep-shape payload [assert.srf:L59], so -1 is not
    /// merely wrong here, it is unreachable.
    /// </para>
    /// <para>
    /// The distinction is worth this much text because the sibling port makes the opposite choice
    /// correctly: <c>PfwException.Line</c> maps the inherited <c>runtimeerror</c> property and so
    /// is an <see cref="int"/> defaulting to -1, while this member maps the payload field and so is
    /// a <see cref="long"/> defaulting to 0. Anyone reconciling the two files will meet both and
    /// must not converge them. The width follows AAP section 0.4.5.2, which maps PowerScript
    /// <c>long</c> to <see cref="long"/>.
    /// </para>
    /// </remarks>
    public long Line { get; set; }

    /// <summary>
    /// Gets or sets the captured call stack rendered as one string. Ports
    /// <c>string #StackTraceInfo</c> [assertionfailed.sru:L23].
    /// </summary>
    /// <value>
    /// Field 7 of the payload: the same frames as <see cref="StackTrace"/>, joined by a bare line
    /// feed. Empty string until assigned, never <see langword="null"/>.
    /// </value>
    /// <remarks>
    /// Proven consumed by the oracle [w_test_assert.srw:L100]. Built frame by frame alongside
    /// <see cref="StackTrace"/> [assert.srf:L61-L65], with the separator emitted BEFORE each frame
    /// after the first [assert.srf:L63] - so the value carries no leading and no trailing
    /// separator. The separator is a bare line feed and never CRLF, because CRLF is the field
    /// delimiter and would split this single field into many. When this value is empty while the
    /// deep shape is emitted, the consumer's splitter drops the trailing segment and the payload
    /// presents as six fields rather than seven [pfw.sra:L64-L66]; that hazard is described on the
    /// type and is preserved, not corrected.
    /// </remarks>
    public string StackTraceInfo { get; set; } = string.Empty;

    /// <summary>
    /// Gets the captured call stack as an ordered list of frames, outermost first. Ports
    /// <c>string #StackTrace[]</c> [assertionfailed.sru:L24].
    /// </summary>
    /// <value>
    /// The individual frames, in capture order. Never <see langword="null"/>; empty until frames
    /// are added, which is the whole of the shallow shape.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>This member intentionally shadows <see cref="Exception.StackTrace"/>, and the shadowing
    /// is declared with <c>new</c>.</b> The two carry different things and both remain reachable:
    /// this one is the legacy frame array, while the inherited CLR trace is still available by
    /// accessing the property through an <see cref="Exception"/>-typed reference, and
    /// <see cref="Exception.ToString"/> continues to use the CLR trace because this member hides
    /// rather than overrides. The <c>new</c> keyword is not optional: because
    /// <see cref="Exception.StackTrace"/> is virtual, omitting it produces compiler error CS0114 -
    /// an unconditional error rather than a warning promoted by the inherited
    /// TreatWarningsAsErrors, so the build fails either way.
    /// </para>
    /// <para>
    /// Keeping the legacy spelling is the deliberate trade. It is what the producer, the sibling
    /// test project and every consumer look for, and renaming it would break the AAP section
    /// 0.4.5.3 rule that these identifiers survive verbatim into stored comparisons. The cost is
    /// paid once, here. The same collision is why no TYPE in this project may be named
    /// <c>StackTrace</c> - the sibling capture helper is <c>StackTraceProvider</c> for exactly that
    /// reason - and why any reference to the Base Class Library type must be spelled
    /// <see cref="System.Diagnostics.StackTrace"/> in full.
    /// </para>
    /// <para>
    /// Exposed as a mutable list behind a get-only property, which is the faithful analogue of the
    /// legacy one-based array: the producer appends one frame per iteration
    /// [assert.srf:L61-L62], and append is the only operation it performs. Ordering is therefore
    /// part of the contract. The declared type is the interface rather than the concrete list so
    /// that callers cannot take a dependency on the implementation, and there is no setter because
    /// the legacy never replaces the array wholesale.
    /// </para>
    /// </remarks>
    public new IList<string> StackTrace { get; } = new List<string>();
}
