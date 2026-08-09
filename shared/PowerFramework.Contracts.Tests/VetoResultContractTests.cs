// ==================================================================================================
//  VetoResultContractTests - THE BROKER VETO IS TRI-VALUED ON THE WIRE, AND NO BOOLEAN CARRIES IT
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   dataservices.v1.Veto.Result                      - the wire alphabet under test
//            every field of every message in common.v1, dataservices.v1 and persistence.v1
//                                                             - swept for a flattened veto
//  ORACLE    ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru   - the broker's own protocol
//            ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru    - the MISLEADING comments
//            ws_objects/pfw.shared.pbl.src/retcode.sru                   - the OTHER alphabet
//            All three are read as specification only, never at runtime (constraint C-C). Every
//            locator below appears as a citation in a comment or in a failure message and nowhere
//            else - this file opens no legacy file, and performs no I/O of any kind.
//
//  WHICH ALPHABET THIS FILE PINS, AND THE THREE IT DELIBERATELY DOES NOT
//  ------------------------------------------------------------------------------------------------
//  FOUR UNRELATED NUMERIC ALPHABETS IN THIS SYSTEM SHARE THE NUMERALS 1 AND 2, and three of the four
//  are declared inside the SAME PowerScript object within thirteen lines of each other. They are
//  listed here in full because merging any two of them is a one-line mistake with silent
//  consequences, and because a later agent reading only one of the source files would have no way to
//  know the others exist.
//
//      (a) THE BROKER VETO - what this file pins, and nothing else.
//          PREVENT_ONCE = 1  [n_cst_eventful.sru:L111]
//          PREVENT_DEEP = 2  [n_cst_eventful.sru:L112]
//          continue     = 0  - not a declared constant in the legacy at all; it is the ZERO held in
//                             the broker's private `long _nPrevent` [:L85], written on entry to every
//                             dispatch level [:L813] and tested as `if _nPrevent <> 0 then exit`
//                             [:L822]. "No veto in effect" is therefore a REAL STATE with a real
//                             value, not an absence of information.
//
//      (b) THE BROKER'S OnException HOOK - a DIFFERENT alphabet, and INVERTED against (a).
//          `choose case Event OnException(name,ex)` [n_cst_eventful.sru:L889-L895]:
//              case 1 -> //Prevent   exit the dispatch
//              case 2 -> //Continue  clear the exception flag and carry on
//              anything else -> falls through to `throw ex` [:L902], i.e. rethrow
//          NOTE WHAT 2 MEANS IN EACH: in (a) it is the STRONGEST prevention; in (b) it is
//          CONTINUATION; in (c) below it is "capture everything". Modelling (a) with (b) would not
//          merely blur a distinction - it would invert the meaning of the numeral 2.
//
//      (c) THE EXCEPTION CAPTURE FLAGS - a THIRD alphabet on 0, 1 and 2, in the same object again.
//          `constant long CAP_UNHANDLED = 0` / `CAP_HANDLED = 1` / `CAP_ALL = 2`
//          [n_cst_eventful.sru:L100-L102]. It selects WHICH exceptions a subscription captures and
//          has nothing to do with prevention at all. It earns a place in this list for a practical
//          reason rather than a taxonomic one: it is why the boolean sweep below matches `handled` as
//          an EXACT field name and never as a substring - the boundary legitimately carries an
//          `unhandled` bool for capture state, and a substring rule over `handled` would fail on it.
//
//      (d) THE RETURN-CODE ALGEBRA - PREVENT = 1 [retcode.sru:L42], tested through
//          Predicates.IsPrevented, and asserted numerically by the sibling RetCodeAgreementTests.
//          It is the alphabet the framework's own hooks return: `of_prevent` itself returns
//          RetCode.FAILED or RetCode.OK [n_cst_eventful.sru:L1293, :L1299] while SETTING an (a) value,
//          so the two appear inside the same seven lines of PowerScript and still mean different
//          things.
//          Alphabet (d) is also what the two other vetoable outcomes on this boundary carry - see the
//          inventory in Section 3 - which is faithful rather than a shortfall, because those legacy
//          hooks genuinely return RetCode values and never touch `_nPrevent`.
//
//  WHY A TEST IS NEEDED AT ALL: THE SOURCE COMMENTS SAY BINARY, AND THEY ARE PERSUASIVE
//  ------------------------------------------------------------------------------------------------
//  Every vetoable topic constant in se_cst_dw.sru carries the inline comment `//0:continue,1:prevent`
//  - eight sites, at :L46, :L51, :L56, :L59, :L62, :L65, :L68 and :L71, all agreeing, all describing a
//  BOOLEAN. An implementer who took the shape from those comments would model a bool and would be
//  wrong, and the eight-fold agreement is exactly what would make the mistake feel safe.
//
//  The comments are not a defect to correct (constraint C-B). They are accurate about what a handler
//  on THOSE PARTICULAR topics is expected to return. They are simply not a statement about what the
//  broker can carry, and the broker's own protocol is the authority on that.
//
//  WHAT FLATTENING WOULD ACTUALLY COST - THE MECHANISM, NOT THE SLOGAN
//  ------------------------------------------------------------------------------------------------
//  The difference between the two preventions is not a shade of severity, it is the UNWIND rule, and
//  it lives at n_cst_eventful.sru:L954-L958:
//
//      if nPrevent <> 0 then                                   // an OUTER level had already vetoed
//          _nPrevent = nPrevent                                // restore it; it outranks this level
//      elseif _nPrevent = PREVENT_ONCE or _nDeep = 0 then
//          _nPrevent = 0                                       // PREVENT_ONCE CLEARS on unwind
//      end if                                                  // PREVENT_DEEP SURVIVES the unwind
//
//  So PREVENT_ONCE aborts the current dispatch level and evaporates as that level unwinds, while
//  PREVENT_DEEP aborts the whole nested chain and is still in force when control returns to the outer
//  level - it is cleared only once `_nDeep` is back to zero. A boolean has one bit and cannot express
//  which of the two a handler asked for.
//
//  AND THE LOSS WOULD BE SILENT. No exception, no error, no failing status: just a nested handler that
//  runs when the legacy would have stopped it. That is why this suite asserts THREE distinct states
//  rather than "prevention is representable", and why it separately sweeps the entire boundary for a
//  boolean that has quietly taken a veto's job.
//
//  WHY THE EXPECTED NUMBERS ARE WRITTEN HERE AND NOT READ FROM THE MANAGED ENUM  (constraint C-A)
//  ------------------------------------------------------------------------------------------------
//  PowerFramework.Shared.Eventful/VetoResult.cs holds the in-process form of this same alphabet, and
//  this project deliberately does not reference it. Reading the expectations from that enum would make
//  the test a tautology - it would prove the two transcriptions agree with each other while proving
//  nothing about either one's agreement with the legacy. The numbers below are transcribed from the
//  PowerScript locators, and Section 5 asserts mechanically that no Eventful reference has crept in.
//
//  Equally, nothing here instantiates a broker or dispatches anything. This file reasons over
//  DESCRIPTORS - the shape of the published boundary - which is the only thing a contract test can
//  legitimately claim to be about.
//
//  DISCIPLINE
//  ------------------------------------------------------------------------------------------------
//  Pure: descriptor reflection and in-memory assembly metadata only. No clock, no randomness, no
//  configuration read, no network, no file or directory access, no sleep, no mutable static state.
//  Repeatability is the one hard prerequisite of the Golden-Master approach this repository adopts
//  (AAP 0.6.7).
//
//  Two things that look like exceptions and are not. `FileDescriptor.Name` reads the DECLARING .proto
//  file's name out of compiled-in descriptor metadata; it opens nothing. `Environment.NewLine` is a
//  platform constant used only to lay out a multi-line failure MESSAGE, so it can never influence a
//  verdict.
//
//  THE ONLY LITERAL NUMBERS IN THIS FILE ARE 0, 1 AND 2, each declared once as a named constant with
//  its PowerScript locator attached. Cardinalities are DERIVED from the expected alphabet rather than
//  written down, so adding a state to the expectation cannot leave a stale count behind.
//
//  No secret of any kind appears here, and nothing in this subject needs one (constraint C-F).
//
//  RULES POSITION
//  ------------------------------------------------------------------------------------------------
//  review_rules returns exactly "No user rules provided.", so NO user-specified rule governs this
//  file. Its absence is not licence: the enterprise-standard baseline of AAP 0.7.2 applies in its
//  place - nullable enabled and warnings as errors inherited from Directory.Build.props and never
//  relaxed, no NoWarn, no #pragma, and no per-project analyser suppression.
// ==================================================================================================

using System.Reflection;
using Google.Protobuf.Reflection;
using Xunit;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Pins the tri-valued broker veto on the published boundary: three distinct states carrying the
/// legacy numeric values, and no boolean field anywhere carrying a veto outcome.
/// </summary>
/// <remarks>
/// <para>
/// The alphabet under test is the event broker's prevention protocol -
/// <c>PREVENT_ONCE = 1</c> and <c>PREVENT_DEEP = 2</c> from
/// <c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L111-L112</c>, plus the continue state
/// the broker expresses as the zero in its private <c>_nPrevent</c> field (<c>:L85</c>, written at
/// <c>:L813</c>, tested at <c>:L822</c>). It is NOT the <c>OnException</c> hook's alphabet
/// (<c>:L889-L895</c>, where 2 means continue) and it is NOT the return-code algebra's
/// <c>PREVENT = 1</c> (<c>ws_objects/pfw.shared.pbl.src/retcode.sru:L42</c>). The file-level comment
/// sets all three side by side.
/// </para>
/// <para>
/// Every assertion reasons over Protobuf descriptors reached through
/// <see cref="ContractDescriptors"/>. Nothing is instantiated, nothing is dispatched, and no
/// reference to <c>PowerFramework.Shared.Eventful</c> exists or may be added - see
/// <see cref="TheExpectedNumbersComeFromTheLegacyLocatorsAndNotFromTheManagedEnum"/>.
/// </para>
/// </remarks>
public sealed class VetoResultContractTests
{
    // ==============================================================================================
    //  THE THREE NUMBERS. THE ONLY NUMERIC LITERALS IN THIS FILE.
    //
    //  Each is transcribed from a PowerScript locator, not from the managed enum and not from the
    //  .proto text, so that a change on either side of the wire is caught rather than mirrored.
    // ==============================================================================================

    /// <summary>
    /// The continue state: no veto in effect, dispatch proceeds.
    /// </summary>
    /// <remarks>
    /// The legacy declares no constant for this. It is the zero held in the broker's private
    /// <c>long _nPrevent</c> [n_cst_eventful.sru:L85], written on entry to every dispatch level
    /// [<c>:L813</c>] and tested as <c>if _nPrevent &lt;&gt; 0 then exit</c> [<c>:L822</c>]. Being
    /// tested against zero rather than against a named constant is exactly why it is a real state
    /// with a real value rather than an absence of information.
    /// </remarks>
    private const int ContinueNumber = 0;

    /// <summary>
    /// Stop this dispatch level. Clears as the level unwinds [n_cst_eventful.sru:L956-L957].
    /// </summary>
    /// <remarks><c>constant long PREVENT_ONCE = 1</c> [n_cst_eventful.sru:L111], assigned at
    /// <c>:L1297</c>.</remarks>
    private const int PreventOnceNumber = 1;

    /// <summary>
    /// Stop the whole nested chain. Survives the unwind [n_cst_eventful.sru:L956-L957].
    /// </summary>
    /// <remarks><c>constant long PREVENT_DEEP = 2</c> [n_cst_eventful.sru:L112], assigned at
    /// <c>:L1295</c>.</remarks>
    private const int PreventDeepNumber = 2;

    /// <summary>
    /// The RETURN-CODE algebra's prevention - alphabet (c), which is NOT the broker veto.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Constant Long PREVENT = 1</c> [ws_objects/pfw.shared.pbl.src/retcode.sru:L42].
    /// </para>
    /// <para>
    /// <b>It is deliberately a second constant with the same value as
    /// <see cref="PreventOnceNumber"/>, and that duplication is the point.</b> The two alphabets share
    /// the numeral 1 and share nothing else: different enum, different declaring file, different value
    /// name, different semantics. Collapsing the two constants into one would be the first step
    /// towards collapsing the two alphabets, which is what
    /// <see cref="TheReturnCodePreventionSharesTheNumeralOneWithPreventOnceButNotTheAlphabet"/>
    /// exists to prevent.
    /// </para>
    /// </remarks>
    private const int ReturnCodePreventNumber = 1;

    // ==============================================================================================
    //  DECLARED NAMES. Read off the contract during discovery rather than guessed, and asserted
    //  below rather than assumed - RequireEnum and RequireMessage fail with a diagnosable message
    //  naming the closest candidates if any of these ever stops resolving.
    // ==============================================================================================

    /// <summary>The veto alphabet: an enum nested inside its own carrier message.</summary>
    private const string VetoEnumFullName = "dataservices.v1.Veto.Result";

    /// <summary>The carrier message the alphabet is nested in.</summary>
    private const string VetoCarrierMessageFullName = "dataservices.v1.Veto";

    /// <summary>The protocol definition that declares the veto alphabet.</summary>
    private const string VetoProtocolFileName = "dataservices.v1.proto";

    /// <summary>The return-code alphabet - alphabet (c), and a different enum entirely.</summary>
    private const string ReturnCodeEnumFullName = "common.v1.RetCode.Value";

    /// <summary>The shared-vocabulary protocol definition that declares the return-code alphabet.</summary>
    private const string SharedVocabularyFileName = "common.v1.proto";

    /// <summary>The proto spelling of the continue state.</summary>
    private const string ContinueStateName = "CONTINUE";

    /// <summary>The proto spelling of prevent-once, preserved from the legacy (AAP 0.4.5.3).</summary>
    private const string PreventOnceStateName = "PREVENT_ONCE";

    /// <summary>The proto spelling of prevent-deep, preserved from the legacy (AAP 0.4.5.3).</summary>
    private const string PreventDeepStateName = "PREVENT_DEEP";

    /// <summary>The return-code algebra's prevention value name - present in the OTHER enum only.</summary>
    private const string ReturnCodePreventName = "PREVENT";

    /// <summary>
    /// Suffix of the proto3 zero sentinel some enums on this boundary use instead of a semantic zero.
    /// </summary>
    /// <remarks>
    /// Both layouts are tolerated by this suite - see
    /// <see cref="TheZeroMemberIsTheContinueStateItselfRatherThanAnUnspecifiedSentinel"/> - because
    /// which one a contract picks is a modelling choice, whereas the EXISTENCE of a distinguishable
    /// continue state is a legacy fact.
    /// </remarks>
    private const string UnspecifiedSentinelSuffix = "_UNSPECIFIED";

    // ==============================================================================================
    //  THE VETO-CARRYING SURFACE, AS THE CONTRACT ACTUALLY DECLARES IT
    // ==============================================================================================

    /// <summary>
    /// The message that carries the two veto outcomes of one event occurrence - contract C-03.
    /// </summary>
    private const string EventResultMessageFullName = "dataservices.v1.EventResult";

    /// <summary>Contract C-03's service.</summary>
    private const string DataWindowServiceFullName = "dataservices.v1.DataWindowService";

    /// <summary>
    /// The bidirectional stream the veto outcomes travel on, in both directions.
    /// </summary>
    private const string EventChainMethodName = "EventChain";

    /// <summary>
    /// The SEMANTIC handler's edge of the two-stage veto - <c>se_cst_dw.sru:L115</c>.
    /// </summary>
    private const string SemanticVetoFieldName = "semantic_veto";

    /// <summary>
    /// The BROKER's edge of the two-stage veto - <c>se_cst_dw.sru:L116</c> and <c>:L120</c>.
    /// </summary>
    private const string BrokerVetoFieldName = "broker_veto";

    // ==============================================================================================
    //  THE EXPECTED ALPHABET, IN ONE PLACE
    //
    //  Every cardinality assertion below is derived from this dictionary rather than written as a
    //  number, so a state added here cannot leave a stale count behind in an assertion.
    // ==============================================================================================

    /// <summary>
    /// The three semantic states of the broker veto, keyed by their preserved proto spelling.
    /// </summary>
    /// <remarks>
    /// Ordinal comparer, because a case-insensitive key would let <c>prevent_once</c> satisfy an
    /// assertion about <c>PREVENT_ONCE</c> - and the SCREAMING_SNAKE spelling is itself part of what
    /// AAP 0.4.5.3 requires to be preserved, since these names travel in serialized payloads, log
    /// records and characterization recordings.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, int> LegacyAlphabet =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [ContinueStateName] = ContinueNumber,
            [PreventOnceStateName] = PreventOnceNumber,
            [PreventDeepStateName] = PreventDeepNumber,
        };

    // ==============================================================================================
    //  THE BOOLEAN-SWEEP VOCABULARY - THREE EXPLICIT TIERS, WITH THE EXCLUSIONS NAMED
    //
    //  A vocabulary sweep is only worth as much as its word list, so the list is stated here in full
    //  rather than buried in a predicate, and every deliberate OMISSION is named with the real field
    //  that motivated it. TheVetoVocabularyRecognisesAVetoNamedField turns the whole list into
    //  executable rows, so it cannot silently become vacuous.
    // ==============================================================================================

    /// <summary>
    /// TIER 1 - substrings that mean prevention and nothing else anywhere in this system.
    /// </summary>
    /// <remarks>
    /// A bool field whose name contains either token is a flattened veto with no further qualification
    /// needed. <c>veto</c> is the contract's own word for the concept and <c>prevent</c> is the
    /// legacy's (<c>PREVENT_ONCE</c>, <c>PREVENT_DEEP</c>, <c>of_prevent</c>,
    /// <c>_nPrevent</c>). Between them they catch every realistic spelling of the regression -
    /// <c>vetoed</c>, <c>is_veto</c>, <c>prevented</c>, <c>prevention</c>, <c>prevent_deep</c>,
    /// <c>dispatch_prevented</c>.
    /// </remarks>
    private static readonly IReadOnlyList<string> UnconditionalVetoTokens = ["veto", "prevent"];

    /// <summary>
    /// TIER 2, first half - verbs that describe stopping something.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matched ONLY in combination with a Tier 2 noun, and the compounding is deliberate rather than
    /// timid. Every bare head here means something else somewhere on this boundary, and a sweep that
    /// failed on a legitimate field would be "fixed" by a later agent weakening the sweep - the worst
    /// available outcome. Named counterexamples, all real:
    /// </para>
    /// <para>
    /// <c>reject</c> - <c>dataservices.v1.MacroResult.rejected</c> is a bool, and it is not a veto: it
    /// reports that a macro's RETURN TYPE matched no arm, including the <c>unsignedinteger</c> case
    /// created by a preserved legacy typo.
    /// </para>
    /// <para>
    /// <c>cancel</c> - <c>CANCELED</c>/<c>CANCELLED</c> is a RETURN CODE
    /// [ws_objects/pfw.shared.pbl.src/retcode.sru:L44-L45], alphabet (c), and it is the value a cleanly
    /// vetoed update legitimately yields [n_cst_thread_task_sqlupdate.sru:L195-L202]. A field carrying
    /// it is doing its job.
    /// </para>
    /// <para>
    /// <c>block</c>, <c>stop</c>, <c>skip</c> - the query layer speaks of chunks and blocks, and
    /// <c>OrderedDispatchReport</c> reports which of three steps ran; none of those is a veto.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyList<string> DispatchStoppingVerbs =
    [
        "cancel", "canceled", "cancelled",
        "stop", "stopped",
        "halt", "halted",
        "suppress", "suppressed",
        "abort", "aborted",
        "block", "blocked",
        "deny", "denied",
        "refuse", "refused",
        "reject", "rejected",
        "skip", "skipped",
        "kill", "killed",
        "shortcircuit", "short_circuit",
        "swallow", "swallowed",
    ];

    /// <summary>
    /// TIER 2, second half - nouns naming the thing a veto stops.
    /// </summary>
    /// <remarks>
    /// Matched only in combination with a Tier 2 verb. Bare heads are excluded for the same reason:
    /// <c>dataservices.v1.OrderedDispatchReport.broker_trigger_ran</c> and its three siblings are
    /// bools naming the broker and the handlers without carrying a veto, and
    /// <c>dataservices.v1.ColumnExpData.trigger_event</c> is a bool naming an event without
    /// carrying one either.
    /// </remarks>
    private static readonly IReadOnlyList<string> DispatchNouns =
    [
        "dispatch", "chain", "trigger", "triggering",
        "event", "events", "handler", "handlers",
        "propagation", "subscriber", "subscribers", "broker",
    ];

    /// <summary>
    /// TIER 3 - exact field names that express the veto WITHOUT using either Tier 1 word.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the shapes a flattening would most plausibly take once someone had decided a bool
    /// would do. Three groups:
    /// </para>
    /// <para>
    /// GROUP ONE - the "prevention with a depth flag" shape, which the legacy's own
    /// <c>of_prevent(readonly boolean deep)</c> signature [n_cst_eventful.sru:L1276] makes tempting and
    /// which is a trap: that boolean is an ARGUMENT selecting which of two values to write into
    /// <c>_nPrevent</c> [<c>:L1294-L1298</c>], not the outcome the broker then reports.
    /// </para>
    /// <para>
    /// GROUP TWO - the inverse spelling, where the bool means "may we carry on".
    /// </para>
    /// <para>
    /// GROUP THREE - THE .NET-IDIOMATIC FLATTENING, and the most likely of the three to actually
    /// happen. Routed events in WPF and WinForms stop propagating when a handler sets
    /// <c>e.Handled = true</c>, so a developer porting a dispatch chain into .NET has a strong habit
    /// pushing them towards <c>bool handled</c> - and it would read as perfectly idiomatic while
    /// destroying the once-versus-deep distinction. <c>suppressed</c>, <c>aborted</c>, <c>stopped</c>,
    /// <c>blocked</c> and <c>short_circuited</c> are the same instinct in different words.
    /// </para>
    /// <para>
    /// <b>Exact names rather than substrings, and that precision is load-bearing for group three.</b>
    /// The boundary legitimately carries <c>dataservices.v1.*.unhandled</c> for exception-CAPTURE state -
    /// alphabet (c), <c>CAP_UNHANDLED</c>/<c>CAP_HANDLED</c>/<c>CAP_ALL</c> at
    /// [n_cst_eventful.sru:L100-L102] - and a substring rule over <c>handled</c> would fail on it.
    /// Exact matching keeps <c>handled</c> caught and <c>unhandled</c> untouched. For the same reason
    /// <c>cancelled</c> and <c>canceled</c> are deliberately ABSENT: <c>CANCELED</c>/<c>CANCELLED</c> is
    /// a return code [retcode.sru:L44-L45] and the value a cleanly vetoed update legitimately yields
    /// [n_cst_thread_task_sqlupdate.sru:L195-L202], so a bool reporting one is doing its job.
    /// </para>
    /// <para>
    /// No field on this boundary bears any of these names today, which is precisely the state this tier
    /// keeps.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyList<string> FlattenedVetoFieldNames =
    [
        // Group one - prevention with a depth flag.
        "deep", "is_deep", "deep_flag", "deep_only", "once", "is_once", "once_only",

        // Group two - the inverse spelling.
        "continue", "continues", "should_continue", "may_continue", "can_continue",
        "proceed", "should_proceed", "keep_going", "carry_on",

        // Group three - the .NET routed-event idiom and its synonyms.
        "handled", "is_handled", "was_handled",
        "suppressed", "is_suppressed",
        "aborted", "is_aborted",
        "stopped", "is_stopped",
        "blocked", "is_blocked",
        "shortcircuited", "short_circuited",
    ];


    // ==============================================================================================
    //  DESCRIPTOR ACCESSORS
    //
    //  Properties rather than fields, so that a lookup failure surfaces inside the test that needed
    //  the descriptor - with that test's name in the report - instead of during static construction,
    //  where xunit would report a TypeInitializationException for the whole class and say nothing
    //  about which assertion was being made.
    // ==============================================================================================

    /// <summary>The veto alphabet under test, resolved by its fully-qualified proto name.</summary>
    private static EnumDescriptor VetoAlphabet => ContractDescriptors.RequireEnum(VetoEnumFullName);

    /// <summary>The return-code alphabet - alphabet (c), kept deliberately at arm's length.</summary>
    private static EnumDescriptor ReturnCodeAlphabet =>
        ContractDescriptors.RequireEnum(ReturnCodeEnumFullName);

    /// <summary>
    /// The veto alphabet's SEMANTIC states - every declared value that is not a proto3 zero sentinel.
    /// </summary>
    /// <remarks>
    /// The sentinel is filtered rather than ignored so that both permitted layouts reduce to the same
    /// three-state question. Under the continue-at-zero layout this returns every declared value;
    /// under a sentinel layout it returns every value except the sentinel. Either way the count that
    /// matters is the count of states a consumer can act on.
    /// </remarks>
    private static IReadOnlyList<EnumValueDescriptor> SemanticVetoStates =>
        [.. VetoAlphabet.Values.Where(static value => !IsUnspecifiedSentinel(value))];

    /// <summary>
    /// Every field on the whole published boundary whose declared type is the veto alphabet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Walks <see cref="ContractDescriptors.AllFields()"/>, which covers <c>common.v1</c>,
    /// <c>dataservices.v1</c> and <c>persistence.v1</c> including messages nested at any depth.
    /// </para>
    /// <para>
    /// The lambda parameter is named <c>candidate</c> rather than the obvious <c>field</c> because
    /// <c>field</c> is a contextual keyword inside a property accessor from C# 14 onwards, where it
    /// binds to a synthesized backing field (CS9258/CS9273). The escaped <c>@field</c> would compile
    /// and would read as though the escape were hiding something.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<FieldDescriptor> FieldsTypedWithTheVetoAlphabet =>
        [.. ContractDescriptors.AllFields().Where(static candidate =>
            candidate.FieldType == FieldType.Enum
            && string.Equals(candidate.EnumType.FullName, VetoEnumFullName, StringComparison.Ordinal))];

    // ==============================================================================================
    //  THE VOCABULARY MATCHER
    // ==============================================================================================

    /// <summary>
    /// True when <paramref name="valueName"/> is a proto3 zero sentinel rather than a semantic state.
    /// </summary>
    /// <param name="valueName">An enum value descriptor from the veto alphabet.</param>
    private static bool IsUnspecifiedSentinel(EnumValueDescriptor valueName) =>
        valueName.Number == ContinueNumber
        && valueName.Name.EndsWith(UnspecifiedSentinelSuffix, StringComparison.Ordinal);

    /// <summary>
    /// Decides whether a field NAME reads as a veto or dispatch-prevention outcome, and says which
    /// vocabulary term made it so.
    /// </summary>
    /// <param name="fieldName">
    /// The field's proto spelling - the authored <c>snake_case</c> form. Matched case-insensitively,
    /// because a regression could arrive spelled <c>Prevented</c> or <c>PREVENTED</c> just as easily
    /// as <c>prevented</c>, and the sweep is about MEANING rather than about identity here.
    /// </param>
    /// <param name="matchedTerm">
    /// The vocabulary term responsible, so a failure message can be audited without re-reading the
    /// three tier lists. Empty when nothing matched.
    /// </param>
    /// <returns><see langword="true"/> when the name reads as a veto outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="fieldName"/> is null, empty or whitespace.</exception>
    private static bool ReadsAsAVetoOutcome(string fieldName, out string matchedTerm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        // TIER 1 - unconditional. These two words mean prevention and nothing else.
        foreach (string token in UnconditionalVetoTokens)
        {
            if (fieldName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                matchedTerm = token;
                return true;
            }
        }

        // TIER 3 - exact names that express the veto without either Tier 1 word. Checked before
        // Tier 2 because it is a cheap exact comparison and because its matches are unambiguous.
        foreach (string flattened in FlattenedVetoFieldNames)
        {
            if (string.Equals(fieldName, flattened, StringComparison.OrdinalIgnoreCase))
            {
                matchedTerm = flattened;
                return true;
            }
        }

        // TIER 2 - a stopping verb AND a dispatch noun, in either order. The conjunction is what
        // keeps `rejected`, `broker_trigger_ran` and `trigger_event` out while still catching
        // `cancel_dispatch`, `dispatch_cancelled`, `stop_chain` and `event_suppressed`.
        foreach (string verb in DispatchStoppingVerbs)
        {
            if (!fieldName.Contains(verb, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string noun in DispatchNouns)
            {
                if (fieldName.Contains(noun, StringComparison.OrdinalIgnoreCase))
                {
                    matchedTerm = $"{verb}+{noun}";
                    return true;
                }
            }
        }

        matchedTerm = string.Empty;
        return false;
    }

    // ==============================================================================================
    //  THEORY DATA - one provider per invariant, so a row that does not match its consumer is a
    //  compile error rather than a runtime surprise.
    // ==============================================================================================

    /// <summary>
    /// The three semantic states: preserved proto spelling, legacy number, and the locator the number
    /// is transcribed from.
    /// </summary>
    /// <returns>Three rows, one per state of the tri-valued alphabet.</returns>
    public static TheoryData<string, int, string> TriValuedAlphabetRows()
    {
        TheoryData<string, int, string> rows = [];

        // The continue state has no legacy CONSTANT, so its locators are the three sites that prove
        // the zero is a value the broker reads and writes rather than an unset field.
        rows.Add(
            ContinueStateName,
            ContinueNumber,
            "n_cst_eventful.sru:L85 (long _nPrevent), :L813 (cleared on entry), :L822 (tested <> 0)");

        rows.Add(
            PreventOnceStateName,
            PreventOnceNumber,
            "n_cst_eventful.sru:L111 (constant long PREVENT_ONCE = 1), assigned at :L1297");

        rows.Add(
            PreventDeepStateName,
            PreventDeepNumber,
            "n_cst_eventful.sru:L112 (constant long PREVENT_DEEP = 2), assigned at :L1295");

        return rows;
    }

    /// <summary>
    /// The two preventions, addressed BY NUMBER, with the token whose presence proves the once-versus-
    /// deep distinction survived into the spelling.
    /// </summary>
    /// <remarks>
    /// Addressed by number rather than by name on purpose. The numbers are the legacy's authority and
    /// cannot be renegotiated; the SPELLING is the contract's own choice (AAP 0.4.5.3 asks for the
    /// legacy identifiers to be preserved, and today they are exactly preserved), so this provider
    /// asserts the weaker, durable property: whatever the two states are called, they must still say
    /// which is shallow and which is deep. A contract that renamed both to <c>PREVENT</c> would pass a
    /// naive spelling check and fail this one.
    /// </remarks>
    /// <returns>Two rows, one per prevention depth.</returns>
    public static TheoryData<int, string, string, string> PreventionSpellingRows()
    {
        TheoryData<int, string, string, string> rows = [];

        rows.Add(
            PreventOnceNumber,
            "ONCE",
            PreventOnceStateName,
            "n_cst_eventful.sru:L111; cleared on unwind at :L956-L957");

        rows.Add(
            PreventDeepNumber,
            "DEEP",
            PreventDeepStateName,
            "n_cst_eventful.sru:L112; SURVIVES the unwind at :L956-L957");

        return rows;
    }

    /// <summary>The three state names, for the single-declaration-site assertion.</summary>
    /// <returns>Three rows, one per state name.</returns>
    public static TheoryData<string> VetoStateNameRows()
    {
        TheoryData<string> rows = [];

        foreach (string name in LegacyAlphabet.Keys)
        {
            rows.Add(name);
        }

        return rows;
    }

    /// <summary>
    /// One inventoried veto carrier: the declaring message, the field, and the PowerScript locator the
    /// outcome comes from.
    /// </summary>
    /// <param name="MessageFullName">Fully-qualified proto name of the declaring message.</param>
    /// <param name="FieldName">The field's authored <c>snake_case</c> proto name.</param>
    /// <param name="Locator">
    /// The contract identifier and the PowerScript site, satisfying constraint C-K for this row.
    /// </param>
    /// <remarks>
    /// A record rather than a raw tuple or a <c>TheoryData</c> row, because two different tests consume
    /// this inventory - the per-row typing theory and the closed-set assertion - and a
    /// <c>TheoryData</c> row is not addressable by position in xunit.v3. One strongly-typed list keeps
    /// them from drifting apart.
    /// </remarks>
    private sealed record VetoCarrier(string MessageFullName, string FieldName, string Locator);

    /// <summary>
    /// THE INVENTORY of every field on this boundary that genuinely carries a broker-veto outcome.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two rows, and both are the two edges of the SAME two-stage veto rather than two unrelated
    /// outcomes: <c>ondwnrbuttondown</c> consults the semantic handler and THEN the broker, returning 1
    /// if either says so [se_cst_dw.sru:L115-L117], while <c>ondwnrbuttonup</c> consults the broker
    /// alone [<c>:L120-L121</c>]. Reporting one combined verdict would tell a consumer that the
    /// dispatch stopped without telling it which edge stopped it, and the two edges have different
    /// subscribers and different remedies.
    /// </para>
    /// <para>
    /// This list is also what
    /// <see cref="TheVetoAlphabetIsCarriedByExactlyTheInventoriedFieldsAndNoOthers"/> compares the
    /// contract against, which is deliberate: a THIRD carrier appearing without a row here is a
    /// finding either way - either the inventory went stale, or an alphabet was mixed in somewhere it
    /// does not belong. The failure message says which of those to check.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyList<VetoCarrier> VetoCarrierInventory =
    [
        new VetoCarrier(
            EventResultMessageFullName,
            SemanticVetoFieldName,
            "C-03: the semantic handler's edge - se_cst_dw.sru:L115"),
        new VetoCarrier(
            EventResultMessageFullName,
            BrokerVetoFieldName,
            "C-03: the broker's edge - se_cst_dw.sru:L116 and :L120"),
    ];

    /// <summary>
    /// The inventoried veto carriers as theory rows.
    /// </summary>
    /// <returns>One row per veto-carrying field.</returns>
    public static TheoryData<string, string, string> VetoCarryingFieldRows()
    {
        TheoryData<string, string, string> rows = [];

        foreach (VetoCarrier carrier in VetoCarrierInventory)
        {
            rows.Add(carrier.MessageFullName, carrier.FieldName, carrier.Locator);
        }

        return rows;
    }


    /// <summary>
    /// Candidate field names and whether the sweep must recognise each as a veto outcome.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This provider is the sweep's own regression test, and it is why the sweep in
    /// <see cref="NoBooleanFieldOnThePublishedBoundaryCarriesAVetoOutcome"/> cannot silently become
    /// vacuous.</b> A vocabulary sweep that matched nothing would pass forever while proving nothing,
    /// and the failure mode is invisible: the assertion still reads as if it were doing work. So the
    /// positive rows below are the realistic regression spelled every way it plausibly arrives, and
    /// the negative rows are REAL field names from this boundary that must keep passing.
    /// </para>
    /// <para>
    /// This is also strictly better than proving the point by temporarily adding a <c>bool prevented</c>
    /// to a <c>.proto</c> and watching the sweep fail: a temporary edit proves it once, on one
    /// developer's machine, and then disappears. These rows prove it on every build.
    /// </para>
    /// </remarks>
    /// <returns>One row per candidate name.</returns>
    public static TheoryData<string, bool, string> VocabularyRecognitionRows()
    {
        TheoryData<string, bool, string> rows = [];

        // ---- MUST BE RECOGNISED: the flattening, spelled the ways it plausibly arrives ----
        // Tier 1, the two words themselves and their inflections.
        rows.Add("veto", true, "Tier 1: the contract's own word for the concept");
        rows.Add("vetoed", true, "Tier 1: past participle of the same word");
        rows.Add("is_veto", true, "Tier 1: the predicate spelling a bool invites");
        rows.Add("prevent", true, "Tier 1: the legacy's word - n_cst_eventful.sru:L111-L112");
        rows.Add("prevented", true, "Tier 1: THE most likely regression spelling");
        rows.Add("is_prevented", true, "Tier 1: predicate spelling, mirrors Predicates.IsPrevented");
        rows.Add("prevention", true, "Tier 1: noun spelling");
        rows.Add("prevent_deep", true, "Tier 1: prevent-deep demoted to a flag");
        rows.Add("prevent_once", true, "Tier 1: prevent-once demoted to a flag");
        rows.Add("dispatch_prevented", true, "Tier 1: qualified, still caught by the bare token");
        rows.Add("semantic_veto", true, "Tier 1: the real field name - proves the sweep sees it");
        rows.Add("broker_veto", true, "Tier 1: the real field name - proves the sweep sees it");

        // Tier 3, the veto expressed without either Tier 1 word.
        rows.Add("deep", true, "Tier 3: the 'prevention with a depth flag' shape");
        rows.Add("is_deep", true, "Tier 3: same shape, predicate spelling");
        rows.Add("continue", true, "Tier 3: the inverse spelling - 'may we carry on'");
        rows.Add("should_continue", true, "Tier 3: same, in the imperative");
        rows.Add("proceed", true, "Tier 3: synonym of the inverse spelling");
        rows.Add(
            "handled",
            true,
            "Tier 3: THE .NET-idiomatic flattening - WPF and WinForms routed events stop propagating "
                + "on e.Handled = true, so this is the spelling habit most likely to produce the "
                + "regression, and it would look entirely idiomatic while losing once-versus-deep");
        rows.Add("is_handled", true, "Tier 3: the same idiom, predicate spelling");
        rows.Add("was_handled", true, "Tier 3: the same idiom, past tense");
        rows.Add("suppressed", true, "Tier 3: standalone verdict spelling, no Tier 2 noun present");
        rows.Add("aborted", true, "Tier 3: standalone verdict spelling");
        rows.Add("stopped", true, "Tier 3: standalone verdict spelling");
        rows.Add("blocked", true, "Tier 3: standalone verdict spelling");
        rows.Add("short_circuited", true, "Tier 3: standalone verdict spelling");

        // Tier 2, a stopping verb combined with a dispatch noun, in either order.
        rows.Add("cancel_dispatch", true, "Tier 2: cancel+dispatch");
        rows.Add("dispatch_cancelled", true, "Tier 2: dispatch+cancelled, reversed order");
        rows.Add("stop_chain", true, "Tier 2: stop+chain");
        rows.Add("event_suppressed", true, "Tier 2: suppress+event");
        rows.Add("handler_aborted", true, "Tier 2: abort+handler");
        rows.Add("broker_skipped", true, "Tier 2: skip+broker");

        // ---- MUST NOT BE RECOGNISED: real fields on this boundary, each doing a different job ----
        // Every name below is declared somewhere in the three protocol definitions. A sweep that
        // failed on one of them would be weakened by the next reader, which is why the exclusions are
        // asserted rather than merely intended.
        rows.Add("rejected", false, "dataservices.v1.MacroResult: a macro RETURN TYPE was refused");
        rows.Add(
            "gated_out",
            false,
            "dataservices.v1.OrderedDispatchReport: the event gate's early-out, which the contract "
                + "states reports CONTINUE and not prevention - se_cst_dw.sru:L124, :L130, :L176, :L187");
        rows.Add("broker_trigger_ran", false, "OrderedDispatchReport: a noun with no stopping verb");
        rows.Add("semantic_handler_ran", false, "OrderedDispatchReport: likewise");
        rows.Add("column_expression_handler_ran", false, "OrderedDispatchReport: likewise");
        rows.Add("target_became_invalid", false, "OrderedDispatchReport: the IsValid guard, not a veto");
        rows.Add("trigger_event", false, "dataservices.v1.ColumnExpData: a noun pair, no stopping verb");
        rows.Add("changed", false, "dataservices.v1.SetEnabledResponse: separates the two OK outcomes");
        rows.Add("enabled", false, "the #Enabled gate STATE, not the vetoable outcome of setting it");
        rows.Add("committed", false, "persistence.v1: transaction outcome, alphabet (c) territory");
        rows.Add("auto_rollback", false, "persistence.v1: a transaction policy, not a dispatch veto");
        rows.Add("failed", false, "persistence.v1: SQLCode < 0 - n_cst_thread_trans.sru:L337");
        rows.Add("succeeded", false, "the legacy's own bare boolean return, faithfully a bool");
        rows.Add("subscribe", false, "dataservices.v1: a subscription request, not an outcome");
        rows.Add(
            "unhandled",
            false,
            "dataservices.v1: exception CAPTURE state - alphabet (c), CAP_UNHANDLED/CAP_HANDLED/CAP_ALL "
                + "at n_cst_eventful.sru:L100-L102. This row is why Tier 3 matches 'handled' as an "
                + "EXACT name: a substring rule would catch this legitimate field too");
        rows.Add(
            "cancelled",
            false,
            "CANCELLED is a RETURN CODE [retcode.sru:L45] and the value a cleanly vetoed update yields "
                + "[n_cst_thread_task_sqlupdate.sru:L195-L202] - a bool reporting one is doing its job");
        rows.Add("canceled", false, "the legacy's second spelling of the same code [retcode.sru:L44]");
        rows.Add("deferred_accept_pending", false, "dataservices.v1: queued-continuation state");
        rows.Add("in_item_change", false, "dataservices.v1: a re-entrancy guard - se_cst_dw.sru:L89-L96");
        rows.Add("clear", false, "dataservices.v1: a request flag");
        rows.Add("final", false, "dataservices.v1: a stream-chunk marker");
        rows.Add("paged", false, "persistence.v1: a query mode");
        rows.Add("is_null", false, "common.v1.AnyValue: the null discriminator");

        return rows;
    }

    /// <summary>
    /// The other two vetoable outcomes on this boundary, which carry alphabet (c) rather than the
    /// broker veto - and must not be flattened to a boolean either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Recorded here rather than treated as a shortfall, because the modelling is faithful.</b> The
    /// plan expects a veto outcome in three places. Only C-03's event chain carries the broker's
    /// <c>Veto.Result</c>, and that is correct: the other two legacy hooks return RETURN CODES and
    /// never touch <c>_nPrevent</c>, so routing them through the broker's alphabet would be the
    /// mirror-image mistake to the one this file exists to prevent.
    /// </para>
    /// <para>
    /// C-04 <c>SetEnabledResponse</c> - <c>of_setenabled</c> [n_cst_dwsvc.sru:L89-L95] has three
    /// outcomes and not two: unchanged returns <c>RetCode.OK</c> [<c>:L89</c>], a handler returning 1
    /// from <c>onenable</c> refuses the change and returns <c>RetCode.FAILED</c> [<c>:L90</c>], and an
    /// applied change returns <c>RetCode.OK</c> [<c>:L92-L94</c>]. A bool could not tell "refused" from
    /// "applied", which is exactly the flattening in a different alphabet.
    /// </para>
    /// <para>
    /// C-06 <c>OperationStatus</c>, reached from <c>UpdateResponse.status</c> - the before-update hook's
    /// veto means TWO DIFFERENT THINGS [n_cst_thread_task_sqlupdate.sru:L195-L202]: combined with a
    /// transaction failure it yields a database error, while a clean veto yields
    /// <c>RetCode.CANCELLED</c>. And CANCELLED is NEITHER succeeded nor failed in the legacy algebra
    /// [isfailed.srf:L11-L13], so it cannot be folded into a boolean at all.
    /// </para>
    /// </remarks>
    /// <returns>One row per vetoable return-code outcome.</returns>
    public static TheoryData<string, string, string> VetoableReturnCodeOutcomeRows()
    {
        TheoryData<string, string, string> rows = [];

        rows.Add(
            "dataservices.v1.SetEnabledResponse",
            "ret_code",
            "C-04 #Enabled setter - n_cst_dwsvc.sru:L89-L95, vetoable event at :L10");

        rows.Add(
            "persistence.v1.OperationStatus",
            "ret_code",
            "C-06 update hooks - n_cst_thread_task_sqlupdate.sru:L195-L202");

        return rows;
    }

    /// <summary>
    /// The two alphabets that must stay apart, as descriptor names plus the locator of each one's
    /// prevention value.
    /// </summary>
    /// <returns>Two rows: the broker veto and the return-code algebra.</returns>
    public static TheoryData<string, string, string> AlphabetIdentityRows()
    {
        TheoryData<string, string, string> rows = [];

        rows.Add(
            VetoEnumFullName,
            VetoProtocolFileName,
            "alphabet (a): the broker veto - n_cst_eventful.sru:L111-L112");

        rows.Add(
            ReturnCodeEnumFullName,
            SharedVocabularyFileName,
            "alphabet (c): the return-code algebra - retcode.sru:L42");

        return rows;
    }

    // ==============================================================================================
    //  SECTION 1 - WHERE THE ALPHABET LIVES
    // ==============================================================================================

    /// <summary>
    /// The veto alphabet is a real enum on the published boundary, nested inside its own carrier
    /// message, and reachable by the fully-qualified name this file uses everywhere else.
    /// </summary>
    /// <remarks>
    /// Asserted rather than assumed because every other test in this file addresses the alphabet by
    /// that name. If the contract renamed or relocated it, this is the test that says so in one line
    /// instead of eighteen tests failing with a lookup error each.
    /// </remarks>
    [Fact]
    public void TheVetoAlphabetIsDeclaredAsAnEnumNestedInItsOwnCarrierMessage()
    {
        EnumDescriptor veto = VetoAlphabet;

        Assert.Equal(VetoEnumFullName, veto.FullName);

        // Nested in a carrier message rather than declared at file scope. That is not decoration:
        // proto enum values share their ENCLOSING scope's namespace, so a file-scope enum called
        // `Result` with a value called `CONTINUE` would occupy `dataservices.v1.CONTINUE` and collide
        // with any other file-scope enum wanting the same word. Nesting is what lets the legacy
        // spellings be preserved verbatim (AAP 0.4.5.3) without a prefix.
        Assert.NotNull(veto.ContainingType);
        Assert.Equal(VetoCarrierMessageFullName, veto.ContainingType.FullName);
    }

    /// <summary>
    /// Each of the two alphabets is declared in exactly the protocol definition that owns it.
    /// </summary>
    /// <remarks>
    /// The broker veto belongs to <c>dataservices.v1</c> because the broker's dispatch chain is
    /// DataServices' concern; the return-code algebra belongs to <c>common.v1</c> because every service
    /// speaks it. Two alphabets sharing one declaring file would be the first sign that they were on
    /// their way to being merged.
    /// </remarks>
    /// <param name="enumFullName">The alphabet's fully-qualified proto name.</param>
    /// <param name="expectedFileName">The protocol definition expected to declare it.</param>
    /// <param name="locator">Which alphabet this row is, and its PowerScript locator.</param>
    [Theory]
    [MemberData(nameof(AlphabetIdentityRows))]
    public void EachAlphabetIsDeclaredInTheProtocolDefinitionThatOwnsIt(
        string enumFullName,
        string expectedFileName,
        string locator)
    {
        EnumDescriptor alphabet = ContractDescriptors.RequireEnum(enumFullName);

        Assert.Equal(expectedFileName, alphabet.File.Name);
        Assert.False(
            string.IsNullOrWhiteSpace(locator),
            $"Row for '{enumFullName}' carries no locator; constraint C-K requires every row to cite "
                + "the PowerScript site its expectation comes from.");
    }


    // ==============================================================================================
    //  SECTION 2 - THREE STATES, WITH THE LEGACY NUMBERS
    // ==============================================================================================

    /// <summary>
    /// Each of the three states is declared with the exact number its PowerScript locator gives.
    /// </summary>
    /// <remarks>
    /// The numbers are not free. They are the values the broker itself writes into <c>_nPrevent</c>
    /// and compares against, and they travel in characterization recordings, so a renumbering would
    /// invalidate every stored comparison as surely as a rename would (AAP 0.4.5.3).
    /// </remarks>
    /// <param name="stateName">The preserved proto spelling.</param>
    /// <param name="expectedNumber">The legacy number.</param>
    /// <param name="locator">The PowerScript site the number is transcribed from.</param>
    [Theory]
    [MemberData(nameof(TriValuedAlphabetRows))]
    public void TheVetoAlphabetDeclaresEachLegacyStateWithItsLegacyNumber(
        string stateName,
        int expectedNumber,
        string locator)
    {
        EnumDescriptor veto = VetoAlphabet;

        EnumValueDescriptor? declared = veto.Values
            .FirstOrDefault(value => string.Equals(value.Name, stateName, StringComparison.Ordinal));

        Assert.True(
            declared is not null,
            $"{VetoEnumFullName} declares no state named '{stateName}'. The broker veto is TRI-VALUED "
                + $"and this state comes from [{locator}]. Declared states are: "
                + $"{DescribeStates(veto)}. Dropping a state - or renaming it so this lookup misses - "
                + "is the flattening constraint C-B forbids: a deep prevention would silently become a "
                + "shallow one and the nested chain would resume after the vetoing handler.");

        Assert.Equal(expectedNumber, declared!.Number);
    }

    /// <summary>
    /// The alphabet declares exactly three semantic states - no fewer, and no fourth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The expected count is derived from <see cref="LegacyAlphabet"/> rather than written as a
    /// literal, so this assertion cannot go stale independently of the expectation it enforces.
    /// </para>
    /// <para>
    /// Both halves matter. A MISSING state is the flattening. An EXTRA one is a different failure with
    /// the same root cause - somebody needed a distinction the three states already make and added a
    /// fourth rather than finding it, which leaves two ways to say the same thing on the wire and no
    /// rule about which consumers should honour.
    /// </para>
    /// <para>
    /// The failure message NAMES the missing and the unexpected states individually, because "expected
    /// 3, got 2" would leave the reader to work out which of the three vanished - and the whole point
    /// of the tri-valued alphabet is that the three are not interchangeable.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheVetoAlphabetDeclaresExactlyThreeSemanticStatesAndNoFourth()
    {
        IReadOnlyList<EnumValueDescriptor> semantic = SemanticVetoStates;

        IReadOnlyList<string> declaredNames =
            [.. semantic.Select(static value => value.Name)];

        IReadOnlyList<string> missing =
            [.. LegacyAlphabet.Keys.Where(expected => !declaredNames.Contains(expected, StringComparer.Ordinal))];

        IReadOnlyList<string> unexpected =
            [.. declaredNames.Where(static declared => !LegacyAlphabet.ContainsKey(declared))];

        // `is []` rather than a comparison against zero: an emptiness check is not a numeric fact, and
        // borrowing one of the three veto numbers to express it would make the discipline that keeps
        // this file's only literals to 0, 1 and 2 read as an accident rather than a rule.
        Assert.True(
            missing is [],
            $"{VetoEnumFullName} is MISSING {missing.Count} of the {LegacyAlphabet.Count} semantic "
                + $"states of the broker veto: {string.Join(", ", missing)}. "
                + $"{DescribeMissingStateConsequence(missing)} "
                + $"Declared states are: {DescribeStates(VetoAlphabet)}. "
                + "Oracle: n_cst_eventful.sru:L111-L112 for the two preventions, and :L85 with :L813 "
                + "and :L822 for the continue state the legacy holds as the zero in _nPrevent.");

        Assert.True(
            unexpected is [],
            $"{VetoEnumFullName} declares {unexpected.Count} state(s) the broker veto does not have: "
                + $"{string.Join(", ", unexpected)}. The alphabet is exactly three states "
                + "[n_cst_eventful.sru:L111-L112 plus the zero at :L85]. If a new distinction is "
                + "genuinely needed, it belongs in a field beside the veto - the way "
                + "OrderedDispatchReport.gated_out records a gated early-out WITHOUT pretending to be "
                + "a fourth veto state - and not inside this alphabet.");

        Assert.Equal(LegacyAlphabet.Count, semantic.Count);
    }

    /// <summary>
    /// The three numbers are distinct, so no state can be mistaken for another on the wire.
    /// </summary>
    /// <remarks>
    /// Protobuf permits aliasing - two value names sharing one number under <c>option allow_alias</c> -
    /// and <c>common.v1.RetCode.Value</c> genuinely uses it, because the legacy really does declare
    /// <c>OK</c>, <c>SUCCESS</c> and <c>ALLOW</c> all as 0 [retcode.sru:L39-L41]. Here an alias would be
    /// the flattening wearing three names: <c>PREVENT_DEEP</c> aliased onto <c>PREVENT_ONCE</c> would
    /// serialize identically and no consumer could tell which the handler asked for.
    /// </remarks>
    [Fact]
    public void TheThreeVetoNumbersAreDistinctSoNoStateCanBeMistakenForAnother()
    {
        IReadOnlyList<EnumValueDescriptor> semantic = SemanticVetoStates;

        int distinctNumbers = semantic
            .Select(static value => value.Number)
            .Distinct()
            .Count();

        Assert.Equal(semantic.Count, distinctNumbers);

        // And the distinct numbers are exactly the legacy's three, not merely three of something.
        Assert.Equal(
            LegacyAlphabet.Values.Order(),
            semantic.Select(static value => value.Number).Order());
    }

    /// <summary>
    /// The proto3 zero member is the continue state itself rather than an information-free sentinel -
    /// and if a future contract chooses the sentinel layout instead, a distinguishable continue state
    /// must still exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Layout found in this repository: CONTINUE = 0, the semantic-zero layout.</b>
    /// <c>dataservices.v1.Veto.Result</c> declares <c>CONTINUE = 0</c>, <c>PREVENT_ONCE = 1</c> and
    /// <c>PREVENT_DEEP = 2</c> with no <c>*_UNSPECIFIED</c> member, which is the right choice here and
    /// for a specific reason: the usual argument for a sentinel is that proto3 cannot distinguish an
    /// unset scalar from a legitimate zero, so zero must mean "no information". THE BROKER VETO HAS NO
    /// SUCH PROBLEM. Its zero is not the absence of an answer, it IS the answer - the value written
    /// into <c>_nPrevent</c> on entry to every dispatch level [n_cst_eventful.sru:L813] and the value
    /// the loop guard tests for [<c>:L822</c>]. Making it a sentinel would take the one state the
    /// legacy is most explicit about and render it as "unknown".
    /// </para>
    /// <para>
    /// The sentinel layout is nonetheless tolerated, because whether to carry one is a wire-modelling
    /// choice while the EXISTENCE of a distinguishable continue state is a legacy fact. Under that
    /// layout this test requires the continue state to be present as its own member, so
    /// "no veto in effect" never degrades into "we do not know".
    /// </para>
    /// </remarks>
    [Fact]
    public void TheZeroMemberIsTheContinueStateItselfRatherThanAnUnspecifiedSentinel()
    {
        EnumDescriptor veto = VetoAlphabet;

        IReadOnlyList<EnumValueDescriptor> zeroMembers =
            [.. veto.Values.Where(static value => value.Number == ContinueNumber)];

        EnumValueDescriptor zero = Assert.Single(zeroMembers);

        bool sentinelLayout = IsUnspecifiedSentinel(zero);

        if (sentinelLayout)
        {
            // The contract chose the sentinel layout. A continue state must still exist and still be
            // distinguishable from both preventions, because "no veto in effect" is a real legacy
            // state that a consumer has to be able to act on.
            EnumValueDescriptor? continueState = veto.Values.FirstOrDefault(value =>
                string.Equals(value.Name, ContinueStateName, StringComparison.Ordinal));

            Assert.True(
                continueState is not null,
                $"{VetoEnumFullName} uses the '{UnspecifiedSentinelSuffix}' sentinel layout "
                    + $"('{zero.Name}' = {ContinueNumber}) but declares no separate continue state. "
                    + "A sentinel says 'no information'; the legacy's zero says 'no veto in effect' "
                    + "[n_cst_eventful.sru:L85, :L813, :L822]. Those are different facts and a "
                    + "consumer deciding whether to keep dispatching needs the second one. Declared "
                    + $"states are: {DescribeStates(veto)}.");

            Assert.NotEqual(PreventOnceNumber, continueState!.Number);
            Assert.NotEqual(PreventDeepNumber, continueState.Number);
        }
        else
        {
            // The layout this repository actually declares.
            Assert.Equal(ContinueStateName, zero.Name);
        }
    }

    /// <summary>
    /// Whatever the two preventions are called, the spelling still says which is shallow and which is
    /// deep.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two assertions of different strengths, on purpose. The DURABLE one is that the depth token
    /// survives in the name, addressed by the legacy NUMBER, which cannot be renegotiated - a contract
    /// that renamed both states to <c>PREVENT</c> would keep two distinct numbers and still have
    /// destroyed the human-readable distinction, and this catches that. The RECORDING one is that today
    /// the spellings are exactly the legacy identifiers, which is what AAP 0.4.5.3 asks for; it is
    /// asserted so a deliberate future rename shows up as a decision to make rather than a drift.
    /// </para>
    /// <para>
    /// Matching the depth token case-insensitively, because a contract that spelled the states
    /// <c>PreventOnce</c> and <c>PreventDeep</c> would have chosen a different convention rather than
    /// lost the distinction, and only the distinction is the invariant here.
    /// </para>
    /// </remarks>
    /// <param name="legacyNumber">The prevention's legacy number - the authority for identity.</param>
    /// <param name="depthToken">The token whose presence proves the depth distinction survived.</param>
    /// <param name="preservedSpelling">The legacy identifier, preserved verbatim today.</param>
    /// <param name="locator">The PowerScript site, including the unwind behaviour that differs.</param>
    [Theory]
    [MemberData(nameof(PreventionSpellingRows))]
    public void TheTwoPreventionSpellingsKeepTheOnceVersusDeepDistinctionRecognisable(
        int legacyNumber,
        string depthToken,
        string preservedSpelling,
        string locator)
    {
        EnumDescriptor veto = VetoAlphabet;

        EnumValueDescriptor? byNumber = veto.Values
            .FirstOrDefault(value => value.Number == legacyNumber);

        Assert.True(
            byNumber is not null,
            $"{VetoEnumFullName} declares no state numbered {legacyNumber}, which is the prevention at "
                + $"[{locator}]. Declared states are: {DescribeStates(veto)}.");

        // THE DURABLE ASSERTION: the name still says which depth this is.
        Assert.True(
            byNumber!.Name.Contains(depthToken, StringComparison.OrdinalIgnoreCase),
            $"{VetoEnumFullName} numbers {legacyNumber} as '{byNumber.Name}', whose spelling does not "
                + $"contain '{depthToken}'. The two preventions differ in ONE behaviour and it is the "
                + "unwind rule [n_cst_eventful.sru:L956-L957]: prevent-once clears as its dispatch "
                + "level unwinds, prevent-deep survives it. A name that does not distinguish them "
                + "invites a caller to treat them as interchangeable severities, which they are not.");

        // THE RECORDING ASSERTION: today the legacy identifier is preserved verbatim (AAP 0.4.5.3).
        Assert.Equal(preservedSpelling, byNumber.Name);
    }

    /// <summary>
    /// Each veto state name is declared exactly once across the whole published boundary, and that one
    /// declaration is in the veto alphabet.
    /// </summary>
    /// <remarks>
    /// A second declaration site would be the silent-divergence failure AAP 0.6.1 warns about in its
    /// most literal form: two enums each internally consistent, disagreeing with each other about what
    /// <c>PREVENT_DEEP</c> means, and a consumer resolving whichever one its own generated code
    /// happened to reference.
    /// </remarks>
    /// <param name="stateName">The state name to count declaration sites for.</param>
    [Theory]
    [MemberData(nameof(VetoStateNameRows))]
    public void EachVetoStateNameIsDeclaredExactlyOnceInTheWholeBoundary(string stateName)
    {
        IReadOnlyList<EnumValueDescriptor> sites = ContractDescriptors.FindEnumValuesNamed(stateName);

        EnumValueDescriptor site = Assert.Single(sites);

        Assert.Equal(VetoEnumFullName, site.EnumDescriptor.FullName);
    }

    /// <summary>
    /// On this alphabet the numeral 2 means DEEPER prevention and never continuation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the guard against merging alphabet (a) with alphabet (b). The broker has a second
    /// numeric protocol of its own - the <c>OnException</c> hook at
    /// <c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L889-L895</c> - where
    /// <c>case 1</c> is prevent, <c>case 2</c> is CONTINUE, and anything else rethrows [<c>:L902</c>].
    /// The two protocols live in the same object, share the numerals 1 and 2, and disagree completely
    /// about what 2 means.
    /// </para>
    /// <para>
    /// So an implementer who reached for "the broker's numbers" without noticing which of the two they
    /// had would not blur a distinction - they would INVERT it, turning the strongest prevention into
    /// permission to carry on. Asserting the polarity of 2 explicitly is cheap; discovering the
    /// inversion from a nested handler that ran when it should not have is not.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheNumeralTwoMeansDeeperPreventionAndNeverContinuation()
    {
        EnumDescriptor veto = VetoAlphabet;

        EnumValueDescriptor two = Assert.Single(
            veto.Values,
            static value => value.Number == PreventDeepNumber);

        Assert.Equal(PreventDeepStateName, two.Name);

        // Belt and braces on the polarity, phrased so the failure explains the confusion it catches.
        Assert.False(
            string.Equals(two.Name, ContinueStateName, StringComparison.Ordinal),
            $"{VetoEnumFullName} numbers {PreventDeepNumber} as the continue state. That is the "
                + "OnException hook's alphabet [n_cst_eventful.sru:L889-L895], not the veto's "
                + "[:L111-L112]. The two share the numerals and invert their meanings.");

        // And the continue state is at zero, not at two.
        EnumValueDescriptor? continueState = veto.Values.FirstOrDefault(value =>
            string.Equals(value.Name, ContinueStateName, StringComparison.Ordinal));

        if (continueState is not null)
        {
            Assert.Equal(ContinueNumber, continueState.Number);
        }
    }


    // ==============================================================================================
    //  SECTION 3 - NO BOOLEAN CARRIES VETO MEANING
    // ==============================================================================================

    /// <summary>
    /// No boolean field anywhere on the published boundary carries a veto or dispatch-prevention
    /// outcome.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Scope: every field of every message in all three protocol definitions</b>, walked through
    /// <see cref="ContractDescriptors.AllFields()"/>, which recurses messages nested at any depth. The
    /// folder requirement asks for <c>dataservices.v1</c> and <c>persistence.v1</c>; <c>common.v1</c>
    /// is swept too because it is the file BOTH others import, so a flattened veto placed there would
    /// reach every service at once - the widest possible blast radius and the last place anyone would
    /// look.
    /// </para>
    /// <para>
    /// Repeated and map fields are included rather than filtered: a <c>repeated bool prevented</c> is
    /// the same mistake made per-row, and arguably a worse one because it looks like a bulk result.
    /// </para>
    /// <para>
    /// The failure message names every offender with the vocabulary term that caught it and the type it
    /// should have had, so the remedy is one read away rather than a re-derivation.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoBooleanFieldOnThePublishedBoundaryCarriesAVetoOutcome()
    {
        List<string> offenders = [];

        foreach (FieldDescriptor field in ContractDescriptors.AllFields())
        {
            if (field.FieldType != FieldType.Bool)
            {
                continue;
            }

            if (ReadsAsAVetoOutcome(field.Name, out string matchedTerm))
            {
                offenders.Add(
                    $"{field.ContainingType.FullName}.{field.Name} (bool; matched '{matchedTerm}')");
            }
        }

        Assert.True(
            offenders is [],
            $"{offenders.Count} boolean field(s) on the published boundary read as a veto outcome:"
                + $"{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", offenders)}"
                + $"{Environment.NewLine}The broker veto is TRI-VALUED - PREVENT_ONCE = 1 and "
                + "PREVENT_DEEP = 2 [n_cst_eventful.sru:L111-L112] plus the continue state the broker "
                + "holds as the zero in _nPrevent [:L85, :L813, :L822]. A bool has one bit and cannot "
                + "say which prevention a handler asked for, and the two are not severities: "
                + "prevent-once clears as its dispatch level unwinds while prevent-deep survives it "
                + "[:L956-L957]. FLATTENING IS SILENT - no exception, no error, just a nested handler "
                + $"that runs when the legacy would have stopped it. Type each field above as "
                + $"{VetoEnumFullName} instead. If a listed field genuinely is not a veto, do NOT "
                + "weaken the vocabulary: rename the field, and add it to "
                + $"{nameof(VocabularyRecognitionRows)} as a negative row so the exclusion is "
                + "recorded and asserted rather than assumed.");
    }

    /// <summary>
    /// The vocabulary the sweep uses actually recognises a veto-named field, and actually leaves the
    /// boundary's real non-veto booleans alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is what stops <see cref="NoBooleanFieldOnThePublishedBoundaryCarriesAVetoOutcome"/> from
    /// being vacuously green.</b> A sweep whose word list matched nothing would pass forever and prove
    /// nothing, and there would be no outward sign - the assertion still reads as though it were doing
    /// work. The positive rows are the realistic regression spelled the ways it plausibly arrives; the
    /// negative rows are real field names from these three files that must keep passing.
    /// </para>
    /// <para>
    /// The negative rows are the load-bearing half in practice. A vocabulary that produced a false
    /// failure would be "fixed" by the next reader deleting a term, and the sweep would quietly lose
    /// its teeth - so every exclusion this file makes is asserted here with the reason attached, rather
    /// than left as an untested judgement.
    /// </para>
    /// </remarks>
    /// <param name="candidateFieldName">A field name to run the matcher over.</param>
    /// <param name="expectedToBeRecognised">Whether the matcher must treat it as a veto outcome.</param>
    /// <param name="reason">Why this row is what it is - carried into the failure message.</param>
    [Theory]
    [MemberData(nameof(VocabularyRecognitionRows))]
    public void TheVetoVocabularyRecognisesAVetoNamedField(
        string candidateFieldName,
        bool expectedToBeRecognised,
        string reason)
    {
        bool recognised = ReadsAsAVetoOutcome(candidateFieldName, out string matchedTerm);

        if (expectedToBeRecognised)
        {
            Assert.True(
                recognised,
                $"The veto vocabulary does NOT recognise '{candidateFieldName}' ({reason}). A boolean "
                    + "field with that name would therefore pass the sweep, and a flattened veto would "
                    + "reach the wire unnoticed. Widen the tier that should have caught it: "
                    + $"{nameof(UnconditionalVetoTokens)}, {nameof(DispatchStoppingVerbs)} paired with "
                    + $"{nameof(DispatchNouns)}, or {nameof(FlattenedVetoFieldNames)}.");

            Assert.False(string.IsNullOrEmpty(matchedTerm));
        }
        else
        {
            Assert.False(
                recognised,
                $"The veto vocabulary wrongly flags '{candidateFieldName}' via '{matchedTerm}' - but "
                    + $"{reason}. A false positive is not harmless: the next reader will remove the "
                    + "offending term to get a green build, and the sweep will silently stop catching "
                    + "the real regression. Narrow the term instead - a Tier 2 verb only ever matches "
                    + "in combination with a Tier 2 noun for exactly this reason.");
        }
    }

    /// <summary>
    /// Every field inventoried as carrying a veto outcome is typed with the veto alphabet, as a single
    /// value rather than a collection.
    /// </summary>
    /// <param name="messageFullName">The declaring message.</param>
    /// <param name="fieldName">The field's authored proto name.</param>
    /// <param name="locator">Which edge of the two-stage veto this is, and its PowerScript site.</param>
    [Theory]
    [MemberData(nameof(VetoCarryingFieldRows))]
    public void EveryFieldThatCarriesAVetoOutcomeIsTypedWithTheVetoAlphabet(
        string messageFullName,
        string fieldName,
        string locator)
    {
        MessageDescriptor message = ContractDescriptors.RequireMessage(messageFullName);
        FieldDescriptor field = ContractDescriptors.RequireField(message, fieldName);

        Assert.Equal(FieldType.Enum, field.FieldType);

        Assert.Equal(VetoEnumFullName, field.EnumType.FullName);

        // A single value, not a collection. Each edge of the two-stage veto reports one verdict per
        // event occurrence [se_cst_dw.sru:L115-L117 for both edges, :L120-L121 for the broker alone];
        // a repeated field would leave a consumer to decide how to combine several, which is precisely
        // the combining the two separate fields exist to avoid.
        Assert.False(
            field.IsRepeated,
            $"{messageFullName}.{fieldName} is repeated. It reports ONE verdict for one edge of one "
                + $"event occurrence [{locator}].");
    }

    /// <summary>
    /// The veto alphabet is carried by exactly the inventoried fields and by nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The closing half of the inventory. <see cref="VetoCarryingFieldRows"/> asserts that everything
    /// listed is correctly typed; this asserts that nothing else on the boundary is typed with the veto
    /// alphabet at all. Together they make the veto's reach a reviewed fact rather than a side effect
    /// of wherever the type happened to be convenient.
    /// </para>
    /// <para>
    /// That matters because of what the veto is. It is the BROKER's protocol, and the broker's dispatch
    /// chain is the only thing that can be prevented once or prevented deeply - depth is meaningless
    /// outside a nested dispatch. A veto-typed field on, say, a query response would be borrowing an
    /// alphabet for an outcome that has no nesting to be deep about.
    /// </para>
    /// <para>
    /// A new carrier is therefore a finding either way, and the failure message says which of the two
    /// to check: the inventory went stale, or the alphabet got borrowed.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheVetoAlphabetIsCarriedByExactlyTheInventoriedFieldsAndNoOthers()
    {
        IReadOnlyList<string> inventoried =
            [.. VetoCarrierInventory
                .Select(static carrier => $"{carrier.MessageFullName}.{carrier.FieldName}")
                .Order(StringComparer.Ordinal)];

        IReadOnlyList<string> declared =
            [.. FieldsTypedWithTheVetoAlphabet
                .Select(static field => $"{field.ContainingType.FullName}.{field.Name}")
                .Order(StringComparer.Ordinal)];

        IReadOnlyList<string> unlisted =
            [.. declared.Where(site => !inventoried.Contains(site, StringComparer.Ordinal))];

        IReadOnlyList<string> vanished =
            [.. inventoried.Where(site => !declared.Contains(site, StringComparer.Ordinal))];

        Assert.True(
            unlisted is [],
            $"{unlisted.Count} field(s) are typed with {VetoEnumFullName} but are not in this file's "
                + $"inventory: {string.Join(", ", unlisted)}. Either the inventory in "
                + $"{nameof(VetoCarryingFieldRows)} has gone stale - in which case add a row with the "
                + "PowerScript locator the new outcome comes from - or the broker's alphabet has been "
                + "borrowed for an outcome that is not a dispatch veto, in which case it belongs on "
                + "its own alphabet: prevent-ONCE versus prevent-DEEP is a statement about nested "
                + "dispatch depth [n_cst_eventful.sru:L956-L957] and means nothing outside one.");

        Assert.True(
            vanished is [],
            $"{vanished.Count} inventoried veto carrier(s) no longer exist on the boundary: "
                + $"{string.Join(", ", vanished)}. If a veto outcome was removed from the wire, the "
                + "prevention it reported has nowhere to go and the dispatch chain will resume after a "
                + "vetoing handler.");
    }

    /// <summary>
    /// The veto outcomes travel on C-03's bidirectional event-chain stream, in both directions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Grounds the inventory in the contract rather than in a message name. Reaching the veto through
    /// the SERVICE proves the outcome is actually transmitted: a correctly typed field on a message no
    /// method references would satisfy every other assertion in this file and carry nothing.
    /// </para>
    /// <para>
    /// Bidirectional streaming, both halves asserted, because the legacy chain runs both ways. Server
    /// to client the result is the outcome of a notification the client sent; client to server it is
    /// the client's answer to a handler invocation the server sent - the inverted half the legacy
    /// forces, since the application owns the semantic handlers. A prevention can originate on either
    /// side, so the veto has to be carriable on either.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheEventChainIsTheBidirectionalStreamOnWhichTheVetoOutcomeTravels()
    {
        ServiceDescriptor dataWindowService =
            ContractDescriptors.RequireService(DataWindowServiceFullName);

        MethodDescriptor eventChain =
            ContractDescriptors.RequireMethod(dataWindowService, EventChainMethodName);

        Assert.True(
            eventChain.IsClientStreaming,
            $"{DataWindowServiceFullName}.{EventChainMethodName} is not client-streaming. The chain "
                + "carries the client's answers to handler invocations, so the client end must stream.");

        Assert.True(
            eventChain.IsServerStreaming,
            $"{DataWindowServiceFullName}.{EventChainMethodName} is not server-streaming. The chain "
                + "carries dispatch outcomes and handler invocations to the client, so the server end "
                + "must stream.");

        AssertReachesTheVetoCarrier(eventChain.InputType, "request");
        AssertReachesTheVetoCarrier(eventChain.OutputType, "response");
    }

    /// <summary>
    /// The other two vetoable outcomes are enum-typed return codes, not booleans.
    /// </summary>
    /// <remarks>
    /// See <see cref="VetoableReturnCodeOutcomeRows"/> for why these carry alphabet (c) rather than the
    /// broker veto, and why that is faithful rather than a shortfall. What this test protects is the
    /// property both alphabets share: a vetoable outcome with three or more distinguishable results
    /// cannot be a bool. C-04's setter has three [n_cst_dwsvc.sru:L89-L95] and C-06's hook has two that
    /// are both non-boolean [n_cst_thread_task_sqlupdate.sru:L195-L202], one of which is CANCELLED -
    /// neither succeeded nor failed in the legacy algebra [isfailed.srf:L11-L13].
    /// </remarks>
    /// <param name="messageFullName">The declaring message.</param>
    /// <param name="fieldName">The outcome field's authored proto name.</param>
    /// <param name="locator">Which contract this is, and its PowerScript site.</param>
    [Theory]
    [MemberData(nameof(VetoableReturnCodeOutcomeRows))]
    public void TheOtherTwoVetoableOutcomesAreEnumTypedReturnCodesAndNotBooleans(
        string messageFullName,
        string fieldName,
        string locator)
    {
        MessageDescriptor message = ContractDescriptors.RequireMessage(messageFullName);
        FieldDescriptor field = ContractDescriptors.RequireField(message, fieldName);

        Assert.NotEqual(FieldType.Bool, field.FieldType);

        Assert.Equal(FieldType.Enum, field.FieldType);

        Assert.Equal(ReturnCodeEnumFullName, field.EnumType.FullName);

        Assert.False(
            string.IsNullOrWhiteSpace(locator),
            $"Row for {messageFullName}.{fieldName} carries no locator; constraint C-K requires the "
                + "PowerScript site to be cited.");
    }


    // ==============================================================================================
    //  SECTION 4 - THE ALPHABETS STAY APART
    // ==============================================================================================

    /// <summary>
    /// The veto alphabet is not the return-code alphabet.
    /// </summary>
    /// <remarks>
    /// Two distinct enum descriptors in two distinct files. Stated as its own assertion because the
    /// merge is easy to reach for: both alphabets answer a question about prevention, both are small,
    /// and one of them already lives in the shared vocabulary every service imports. Reusing it for the
    /// veto would look like tidying and would cost the once-versus-deep distinction outright, since the
    /// return-code algebra has exactly one prevention value [retcode.sru:L42].
    /// </remarks>
    [Fact]
    public void TheVetoAlphabetIsNotTheReturnCodeAlphabet()
    {
        EnumDescriptor veto = VetoAlphabet;
        EnumDescriptor returnCode = ReturnCodeAlphabet;

        Assert.NotSame(veto, returnCode);
        Assert.NotEqual(veto.FullName, returnCode.FullName);
        Assert.NotEqual(veto.File.Name, returnCode.File.Name);
    }

    /// <summary>
    /// The two alphabets share no value name, so no identifier can be resolved against the wrong one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Name collision is the practical failure mode rather than a theoretical one. Both alphabets are
    /// spelled in SCREAMING_SNAKE because both preserve legacy identifiers verbatim (AAP 0.4.5.3), both
    /// are about prevention, and their generated C# members sit in namespaces a single <c>using</c>
    /// apart. A shared value name would mean a reader - and a search - could not tell which alphabet a
    /// citation in a log record or a characterization recording belonged to.
    /// </para>
    /// <para>
    /// The disjointness is inherited from the legacy rather than invented: <c>retcode.sru</c> declares
    /// <c>PREVENT</c> and nothing named <c>PREVENT_ONCE</c>, <c>PREVENT_DEEP</c> or <c>CONTINUE</c>,
    /// while the broker declares the latter two and nothing named <c>PREVENT</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheVetoAlphabetSharesNoValueNameWithTheReturnCodeAlphabet()
    {
        IReadOnlyList<string> vetoNames =
            [.. VetoAlphabet.Values.Select(static value => value.Name)];

        IReadOnlyList<string> returnCodeNames =
            [.. ReturnCodeAlphabet.Values.Select(static value => value.Name)];

        IReadOnlyList<string> collisions =
            [.. vetoNames.Intersect(returnCodeNames, StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        Assert.True(
            collisions is [],
            $"{VetoEnumFullName} and {ReturnCodeEnumFullName} both declare: "
                + $"{string.Join(", ", collisions)}. These are two different alphabets - the broker's "
                + "prevention protocol [n_cst_eventful.sru:L111-L112] and the return-code algebra "
                + "[retcode.sru:L42] - and the legacy keeps their identifiers disjoint. A shared name "
                + "makes a value in a log record or a characterization recording ambiguous as to which "
                + "alphabet produced it.");
    }

    /// <summary>
    /// The return-code prevention shares the numeral 1 with prevent-once and shares nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sharpest available statement of the separation, and the reason this file declares
    /// <see cref="ReturnCodePreventNumber"/> as a second constant with the same value as
    /// <see cref="PreventOnceNumber"/>. Both really are 1 - <c>Constant Long PREVENT = 1</c>
    /// [retcode.sru:L42] and <c>constant long PREVENT_ONCE = 1</c> [n_cst_eventful.sru:L111] - and the
    /// coincidence is exactly what makes the merge tempting and the merge's consequences invisible.
    /// </para>
    /// <para>
    /// What the coincidence does NOT license: reading a 1 without knowing which alphabet it came from.
    /// A 1 on the return-code algebra means a prevention that <c>IsSucceeded</c> classifies as a
    /// SUCCESS [issucceeded.srf:L11-L13]. A 1 on the veto means stop this dispatch level, and clear
    /// when it unwinds. Nothing about the first tells you anything about the second.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReturnCodePreventionSharesTheNumeralOneWithPreventOnceButNotTheAlphabet()
    {
        // The shared numeral, from both sides.
        EnumValueDescriptor returnCodePrevent = Assert.Single(
            ReturnCodeAlphabet.Values,
            static value => string.Equals(value.Name, ReturnCodePreventName, StringComparison.Ordinal));

        Assert.Equal(ReturnCodePreventNumber, returnCodePrevent.Number);

        EnumValueDescriptor preventOnce = Assert.Single(
            VetoAlphabet.Values,
            static value => string.Equals(value.Name, PreventOnceStateName, StringComparison.Ordinal));

        Assert.Equal(PreventOnceNumber, preventOnce.Number);

        // The numeral is shared; the alphabet is not. `PREVENT` is declared in the return-code
        // alphabet and NOWHERE in the veto, and the veto's prevention names are declared nowhere in
        // the return-code alphabet - which the collision test above proves as a set property and this
        // proves for the one name that would actually get confused.
        Assert.DoesNotContain(
            ReturnCodePreventName,
            VetoAlphabet.Values.Select(static value => value.Name),
            StringComparer.Ordinal);

        Assert.NotEqual(returnCodePrevent.EnumDescriptor.FullName, preventOnce.EnumDescriptor.FullName);
    }

    // ==============================================================================================
    //  SECTION 5 - PROVENANCE OF THE EXPECTED NUMBERS  (constraint C-A)
    // ==============================================================================================

    /// <summary>
    /// This assembly does not use <c>PowerFramework.Shared.Eventful</c>, so the expected numbers
    /// cannot have come from the managed enum.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is worth asserting rather than trusting.</b> The in-process form of this same
    /// alphabet exists in <c>shared/PowerFramework.Shared.Eventful/VetoResult.cs</c>, and reading the
    /// expectations from it would turn every assertion in this file into a tautology: it would prove
    /// the two transcriptions agree with each other while proving nothing about either one's agreement
    /// with <c>n_cst_eventful.sru</c>. Two transcriptions that drifted together would pass.
    /// </para>
    /// <para>
    /// <see cref="Assembly.GetReferencedAssemblies()"/> is exactly the right instrument here, and its
    /// well-known limitation is why. The compiler emits an assembly reference only for an assembly the
    /// code actually USES, so an unused project reference would not appear - and an unused reference is
    /// precisely one that cannot have supplied a number to an expectation. What this catches is a
    /// reference that is being used, which is the only kind that could.
    /// </para>
    /// <para>
    /// It is also pure: in-memory metadata already loaded for this test run, no file read and no
    /// assembly load.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheExpectedNumbersComeFromTheLegacyLocatorsAndNotFromTheManagedEnum()
    {
        const string eventfulAssemblyName = "PowerFramework.Shared.Eventful";

        Assembly thisAssembly = typeof(VetoResultContractTests).Assembly;

        IReadOnlyList<string> referenced =
            [.. thisAssembly
                .GetReferencedAssemblies()
                .Select(static reference => reference.Name ?? string.Empty)];

        Assert.DoesNotContain(eventfulAssemblyName, referenced, StringComparer.Ordinal);
    }

    // ==============================================================================================
    //  PRIVATE MESSAGE HELPERS
    //
    //  Failure text only. Nothing here participates in an assertion's verdict; it exists so that a
    //  failure names the state that went missing instead of leaving the reader to diff two counts.
    // ==============================================================================================

    /// <summary>
    /// Renders an enum's declared values as <c>NAME=number</c>, in declaration order, for a failure
    /// message.
    /// </summary>
    /// <param name="alphabet">The enum to describe.</param>
    /// <returns>A comma-separated rendering, or a stated empty marker when the enum has no values.</returns>
    private static string DescribeStates(EnumDescriptor alphabet)
    {
        IReadOnlyList<string> rendered =
            [.. alphabet.Values.Select(static value => $"{value.Name}={value.Number}")];

        return rendered is [] ? "(none declared)" : string.Join(", ", rendered);
    }

    /// <summary>
    /// Explains, per missing state, exactly which behaviour is lost - so the failure reads as a
    /// consequence rather than as a count.
    /// </summary>
    /// <param name="missing">The state names the alphabet failed to declare.</param>
    /// <returns>A sentence naming the lost behaviour for each missing state.</returns>
    private static string DescribeMissingStateConsequence(IReadOnlyList<string> missing)
    {
        List<string> consequences = [];

        foreach (string state in missing)
        {
            // Ordinal comparison: these are the preserved SCREAMING_SNAKE spellings, and a
            // case-insensitive match here would quietly accept a renamed state.
            if (string.Equals(state, PreventOnceStateName, StringComparison.Ordinal))
            {
                consequences.Add(
                    "Without PREVENT_ONCE there is no way to stop ONLY the current dispatch level, so "
                        + "a handler that meant to veto one level either vetoes nothing or vetoes the "
                        + "whole chain [n_cst_eventful.sru:L1297, cleared at :L956-L957].");
            }
            else if (string.Equals(state, PreventDeepStateName, StringComparison.Ordinal))
            {
                consequences.Add(
                    "Without PREVENT_DEEP a deep prevention silently becomes a shallow one: the chain "
                        + "resumes after the vetoing handler and the nested events the caller intended "
                        + "to stop all fire [n_cst_eventful.sru:L1295, survives the unwind at "
                        + ":L956-L957].");
            }
            else if (string.Equals(state, ContinueStateName, StringComparison.Ordinal))
            {
                consequences.Add(
                    "Without a continue state 'no veto in effect' has no representation, and a consumer "
                        + "cannot tell it from 'we do not know'. The legacy is explicit that this is a "
                        + "value: the broker writes it on entry to every dispatch level "
                        + "[n_cst_eventful.sru:L813] and tests for it [:L822].");
            }
            else
            {
                consequences.Add(
                    $"'{state}' is expected by this file's alphabet but its consequence is not "
                        + "documented here; add it to DescribeMissingStateConsequence with the "
                        + "PowerScript locator so the failure stays self-explaining.");
            }
        }

        return consequences is [] ? string.Empty : string.Join(" ", consequences);
    }

    /// <summary>
    /// Asserts that <paramref name="message"/> reaches the veto-carrying message through one of its own
    /// fields.
    /// </summary>
    /// <param name="message">The event-chain request or response message.</param>
    /// <param name="direction">
    /// Which side of the stream this is, for the failure message - <c>"request"</c> or
    /// <c>"response"</c>.
    /// </param>
    /// <remarks>
    /// One level of fields is enough and is what the contract declares: the veto carrier is an arm of
    /// the payload <c>oneof</c> on both the request and the response, so it is a direct field of each.
    /// Recursing further would let a distant, unrelated reference satisfy the assertion.
    /// </remarks>
    private static void AssertReachesTheVetoCarrier(MessageDescriptor message, string direction)
    {
        bool reaches = message.Fields
            .InDeclarationOrder()
            .Any(static field =>
                field.FieldType == FieldType.Message
                && string.Equals(
                    field.MessageType.FullName,
                    EventResultMessageFullName,
                    StringComparison.Ordinal));

        Assert.True(
            reaches,
            $"The event chain's {direction} message {message.FullName} carries no "
                + $"{EventResultMessageFullName} field, so the veto outcome cannot travel in that "
                + "direction. A prevention can originate on either side of this stream: the server "
                + "reports the outcome of a notification the client sent, and the client answers a "
                + "handler invocation the server sent [se_cst_dw.sru:L115-L117].");
    }
}
