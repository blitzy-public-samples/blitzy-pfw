// ==================================================================================================
//  ItemChangeAlphabetTests - THE ITEM-CHANGE ALPHABET IS ITS OWN DOMAIN AND IS NEVER RetCode
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   dataservices.v1.ItemChangeResult   [Proto/dataservices.v1.proto:L686-L710]
//  FOIL      common.v1.RetCode.Value            [Proto/common.v1.proto:L324-L388]
//  ORACLE    ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru - `event ondwnitemchange`
//            [:L182-L254] and its consumer `event ondwnitemvalidationerror` [:L322-L385]
//            ws_objects/pfw.shared.pbl.src/retcode.sru - the return-code catalogue [:L39-L45]
//            Read as specification only, NEVER at runtime (constraint C-C): every legacy locator in
//            this file is a citation in a comment, and nothing here opens a legacy path.
//
//  WHY THIS FILE EXISTS - ONE MISTAKE, AND IT IS A PLAUSIBLE ONE
//  ------------------------------------------------------------------------------------------------
//  Conflating this four-value alphabet with the return-code algebra. That is not a strawman: both are
//  small sets of small integers returned from PowerScript event handlers in the same object graph, both
//  travel on the same three .proto files, and BOTH CONTAIN THE NUMERAL 1. A reviewer who saw
//  `item_change_ret_code` typed as `common.v1.RetCode.Value` would very likely wave it through - the
//  legacy field is even SPELLED `_nItemChangeRetCode` [se_cst_dw.sru:L96], which reads like a return
//  code and is not one.
//
//  The consequence of getting it wrong is silent and total. `IsSucceeded` tests `>= 0`
//  [issucceeded.srf:L11-L13], so under the return-code algebra ALL FOUR of 0, 1, 2 and 3 classify as
//  "succeeded" - a rejected edit, a restored buffer and an unmoved caret would every one of them read
//  as success, and no test that only checked classification would notice. Meanwhile 1 would arrive at a
//  reader as `PREVENT` and 2 as no return code at all.
//
//  AAP 0.6.1.5 states the requirement in one line: the alphabet "must be modelled as its own
//  enumeration on the wire, never mapped onto the return-code algebra". This file is what makes that a
//  build failure rather than a review comment.
//
//  ---- THE NUMERAL 1 MEANS THREE UNRELATED THINGS IN THIS SYSTEM -----------------------------------
//  Naming all three here, once, so that a later agent who notices the overlap does not "unify" them:
//
//    THIS alphabet          1 = return 1 to RAISE OnDwnItemValidationError [se_cst_dw.sru:L210, :L212]
//                           2 = do not re-apply the edit text over the buffer          [:L219, :L250]
//    the broker's veto      1 = PREVENT_ONCE, 2 = PREVENT_DEEP
//                                     [ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L111-L112]
//    the return-code algebra 1 = PREVENT, -1 = FAILED, -2 = CANCELED / CANCELLED
//                                     [ws_objects/pfw.shared.pbl.src/retcode.sru:L42-L45]
//
//  Three alphabets, two shared digits, zero shared meanings. They are modelled as three separate wire
//  enumerations - `ItemChangeResult`, `Veto.Result` and `common.v1.RetCode.Value` - and the assertions
//  below pin the separation of the first from the third, which is the pair that actually collides in
//  the item-change path.
//
//  WHAT THIS FILE ASSERTS, AND WHAT IT DELIBERATELY DOES NOT
//  ------------------------------------------------------------------------------------------------
//  SHAPE ONLY (constraint C-A). Every assertion reads a descriptor. Nothing here constructs a message,
//  runs a dispatch, drives the item-change state machine or executes any legacy behaviour: the machine
//  is DataServices' to implement and DataServices' tests to exercise, and a test project for the
//  published boundary that reproduced it would be the shared-behaviour back door the architecture
//  forbids. The question here is narrower and answerable from the contract alone: DOES THE BOUNDARY
//  DECLARE THIS ALPHABET AS A DOMAIN OF ITS OWN, WITH THE LEGACY NUMBERS, AND KEEP IT APART FROM THE
//  RETURN-CODE ALGEBRA.
//
//  ALL FOUR VALUES ARE ASSERTED, INCLUDING THE TWO THAT LOOK REDUNDANT (constraint C-B). Both 1 and 3
//  end up raising the validation-error event, and 3 is rewritten to 1 [se_cst_dw.sru:L225] - so a fresh
//  reader is tempted to conclude the wire needs only three values. It needs four. THE REWRITE HAPPENS
//  IN THE HANDLER, NOT ON THE WIRE: a handler RETURNS 3 and the event YIELDS 1, and the difference
//  stays observable afterwards because the validation-error handler restores value and status for
//  results 1 and 3 only when the stashed code was NOT 3 [:L369]. A wire that could not carry a 3 could
//  not express that guard. Nothing here asserts a tidier three-value alphabet, and nothing corrects the
//  rewrite.
//
//  ---- THE CASE-1 READING, STATED SO THIS FILE IS NOT MISREAD ---------------------------------------
//  AAP 0.6.1.5 describes `case 1` as "falling through" to `case 2`. Read against the oracle, that is a
//  C-family reading: PowerScript CHOOSE CASE does not fall through, and [:L212] is an arm with no
//  statements, so a result of 1 returns exactly as the semantic handler produced it, with no restore
//  and no coercion - which is what the source's own comment at [:L210] describes, since it is
//  OnDwnItemValidationError that performs the restore [:L369-L379]. dataservices.v1.proto:L657-L685 and
//  docs/CONTRACTS.md 6.5 both record the point with its evidence.
//
//  IT CHANGES NOTHING HERE, and that is worth saying explicitly: the wire alphabet is {0,1,2,3} under
//  EITHER reading, so every assertion in this file is reading-independent. Whether a restore happens
//  before the validation-error event fires is a behavioural question for DataServices, adjudicated
//  against the behavioural oracle - not a shape question, and therefore not this file's to settle.
//
//  DOCUMENTATION LIVES ON THE CONTRACT, AND THE DESCRIPTOR KEEPS ONLY THE NAMES
//  ------------------------------------------------------------------------------------------------
//  A .proto records prose in leading comments, which reach a descriptor through `source_code_info`.
//  MEASURED ON THIS BUILD RATHER THAN ASSUMED: `DataservicesV1Reflection.Descriptor.ToProto()
//  .SourceCodeInfo` is null, because Grpc.Tools does not embed source information in the descriptor it
//  serialises into the generated reflection holder. So the comments at dataservices.v1.proto:L646-L710
//  are unreachable at runtime, and no assertion here can read them. Rather than invent a mechanism for
//  reading them, this file asserts the documentation carrier that DOES survive into the descriptor -
//  THE VALUE NAMES - and that is not a consolation prize: a name like
//  `ITEM_CHANGE_RESULT_RESTORE_AND_REJECT_TEXT` is what appears in a JSON payload, in a log record and
//  in a characterization recording, so it is the only self-description a consumer of the wire ever
//  actually sees. The prose behind each name lives in Proto/dataservices.v1.proto:L646-L710 and in
//  docs/CONTRACTS.md 6.5, which are cited from the assertions that depend on them.
//
//  The REST projection is NOT asserted, because it does not carry this alphabet: `ITEM_CHANGE_RESULT_`
//  appears zero times in OpenApi/gateway.v1.yaml. That is correct rather than a gap - the item-change
//  chain rides `EventChain`, which is bidirectional-stream-only and has no REST projection. The
//  `EID_ITEMCHANGE` occurrences in that document are the event-GATE bitmask [se_cst_dw.sru:L43], a
//  fourth and entirely separate small-integer alphabet.
//
//  PURITY AND SECRETS
//  ------------------------------------------------------------------------------------------------
//  No I/O of any kind, no clock, no randomness, no environment variable, no network, no mutable static
//  state. The descriptor graph is immutable and process-wide, so every fact here is safe under parallel
//  collections and repeatable by construction - which is the one hard prerequisite of the Golden-Master
//  approach this repository adopts (AAP 0.6.7). No credential, key or token appears in this file, and
//  none is needed (constraint C-F).
//
//  EVERY NUMERIC LITERAL IN THIS FILE IS 0, 1, 2 OR 3, and each carries its oracle locator. The counts
//  the assertions compare against are DERIVED from the tables below rather than written as literals, so
//  a row added to a table cannot leave a count assertion silently out of date.
//
//  RULES POSITION
//  ------------------------------------------------------------------------------------------------
//  review_rules returns exactly "No user rules provided.", so NO user-specified rule governs this file.
//  Its absence is not licence: the enterprise-standard baseline of AAP 0.7.2 applies in its place -
//  nullable enabled and warnings as errors inherited from Directory.Build.props and never relaxed, no
//  NoWarn and no #pragma anywhere, and versioned contracts as the only cross-service coupling. The
//  binding non-rule constraints of AAP 0.7.3 that govern this file are C-A, C-B, C-C, C-F, C-H and C-K,
//  and each is cited above at the point where it applies.
// ==================================================================================================

using Google.Protobuf.Reflection;
using Xunit;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Pins <c>dataservices.v1.ItemChangeResult</c> as a four-value domain of its own, numbered exactly as
/// the legacy <c>ondwnitemchange</c> dispatch numbers its arms, and held apart from
/// <c>common.v1.RetCode.Value</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one mistake being prevented.</b> Modelling the item-change alphabet onto the return-code
/// algebra. The two share the numeral 1 and share nothing else: in the algebra 1 is <c>PREVENT</c> and
/// −1 is <c>FAILED</c> [<c>retcode.sru:L42-L43</c>], while here 1 is the value that raises
/// <c>OnDwnItemValidationError</c> [<c>se_cst_dw.sru:L210</c>] and 2 means "the buffer has already been
/// written, do not re-apply the edit text" [<c>:L250</c>]. Because <c>IsSucceeded</c> tests
/// <c>&gt;= 0</c>, all four item-change values would classify as successes if they were carried as
/// return codes.
/// </para>
/// <para>
/// <b>Semantic documentation is not in the descriptor.</b> Leading comments reach a descriptor only
/// through <c>source_code_info</c>, which Grpc.Tools does not embed - measured on this build:
/// <c>ToProto().SourceCodeInfo</c> is <see langword="null"/>. The prose therefore lives in
/// <c>Proto/dataservices.v1.proto:L646-L710</c> and in <c>docs/CONTRACTS.md</c> 6.5, and the on-wire
/// carrier this file can and does assert is the set of value <i>names</i>, which is what appears in
/// payloads, logs and characterization recordings.
/// </para>
/// <para>
/// <b>Shape only.</b> No assertion here executes the item-change state machine; the dispatch, the
/// rewrites and the restore guards belong to DataServices and to its own tests.
/// </para>
/// </remarks>
public sealed class ItemChangeAlphabetTests
{
    // ==============================================================================================
    //  THE TWO DESCRIPTORS UNDER COMPARISON
    //
    //  Addressed through ContractDescriptors so that a miss is a diagnosable assertion failure naming
    //  the closest candidates, rather than a NullReferenceException.
    //
    //  THE TWO SPELLINGS ARE NOT INTERCHANGEABLE, and the difference is load-bearing:
    //    * "ItemChangeResult" is a SIMPLE name and resolves unambiguously - it is declared once in the
    //      whole published boundary.
    //    * "common.v1.RetCode.Value" MUST be fully qualified. The simple name `Value` matches THREE
    //      enums (RetCode.Value, XmlParseStatus.Value, SqliteResultCode.Value), and ContractDescriptors
    //      reports ambiguity as an error rather than returning a first match - correctly, since a foil
    //      silently resolved to the wrong enum would make every disjointness assertion below vacuous.
    // ==============================================================================================

    /// <summary>The item-change alphabet: <c>dataservices.v1.ItemChangeResult</c>.</summary>
    private const string AlphabetEnumName = "ItemChangeResult";

    /// <summary>The foil: <c>common.v1.RetCode.Value</c>, fully qualified because `Value` is ambiguous.</summary>
    private const string ReturnCodeEnumName = "common.v1.RetCode.Value";

    /// <summary>
    /// The prefix every item-change value name carries, and the mechanism by which a recorded value can
    /// be attributed to THIS alphabet rather than to one of the other three that share its digits.
    /// </summary>
    private const string AlphabetValuePrefix = "ITEM_CHANGE_RESULT_";

    /// <summary>
    /// The suffix of the one optional proto3 zero sentinel this contract would be allowed to add.
    /// </summary>
    /// <remarks>
    /// The contract does not currently declare one, and does not need one: zero is a REAL legacy value
    /// here - the event-gate early-out returns it [<c>se_cst_dw.sru:L187</c>] and the default arm
    /// catches it [<c>:L226</c>] - so the zero slot is spent on legacy meaning rather than on a
    /// synthetic name for "absent". The tolerance exists so that adding a sentinel later is a
    /// reviewable change rather than an automatic failure; adding anything else is a failure.
    /// </remarks>
    private const string ZeroSentinelSuffix = "_UNSPECIFIED";

    /// <summary>The three files of the published boundary, as the descriptor for each is reached.</summary>
    private const string AlphabetFileName = "dataservices.v1.proto";

    /// <summary>The file the return-code algebra is declared in.</summary>
    private const string ReturnCodeFileName = "common.v1.proto";

    /// <summary>
    /// The legacy dispatch numbers, and the ONLY numbers this alphabet may declare.
    /// </summary>
    /// <remarks>
    /// From the <c>choose case rtCode</c> at <c>se_cst_dw.sru:L211</c>: <c>case 1</c> [<c>:L212</c>],
    /// <c>case 2</c> [<c>:L213</c>], <c>case 3</c> [<c>:L223</c>] and <c>case else</c> [<c>:L226</c>],
    /// which the gate early-out reaches with 0 [<c>:L187</c>]. Every count assertion in this file is
    /// derived from this array's length rather than from a literal, so the two cannot drift apart.
    /// </remarks>
    private static readonly int[] LegacyDispatchNumbers =
    [
        0, // `case else` [se_cst_dw.sru:L226], and the gate early-out's `return 0` [:L187]
        1, // `case 1`    [se_cst_dw.sru:L212] - the empty arm
        2, // `case 2`    [se_cst_dw.sru:L213] - the conditional restore
        3, // `case 3`    [se_cst_dw.sru:L223] - keep value, no focus move
    ];

    private static EnumDescriptor Alphabet => ContractDescriptors.RequireEnum(AlphabetEnumName);

    private static EnumDescriptor ReturnCode => ContractDescriptors.RequireEnum(ReturnCodeEnumName);

    /// <summary>
    /// The values of <see cref="Alphabet"/> that carry legacy meaning, i.e. every value that is not the
    /// optional proto3 zero sentinel.
    /// </summary>
    private static IReadOnlyList<EnumValueDescriptor> MeaningfulValues() =>
        Alphabet.Values.Where(static value => !IsZeroSentinel(value)).ToArray();

    /// <summary>
    /// Whether <paramref name="value"/> is the idiomatic proto3 zero sentinel rather than a legacy
    /// value.
    /// </summary>
    /// <remarks>
    /// BOTH halves are required. A value named <c>..._UNSPECIFIED</c> with a non-zero number is not a
    /// sentinel at all, it is a fifth domain value wearing a sentinel's name, and it must fail rather
    /// than be excused by this predicate.
    /// </remarks>
    private static bool IsZeroSentinel(EnumValueDescriptor value) =>
        value.Number == LegacyDispatchNumbers[0]
        && value.Name.EndsWith(ZeroSentinelSuffix, StringComparison.Ordinal);

    // ==============================================================================================
    //  TABLE 1 - THE FOUR LEGACY VALUES
    //
    //  One row per dispatch arm: the proto name, the legacy number, and what the arm OBSERVABLY does
    //  with its locator. The third column is not decoration - it is passed into every failure message,
    //  so a broken row reports which legacy behaviour lost its wire representation rather than just
    //  which string did not match.
    // ==============================================================================================

    /// <summary>
    /// The four values of the alphabet: proto name, legacy dispatch number, and the observable legacy
    /// behaviour with its oracle locator.
    /// </summary>
    public static TheoryData<string, int, string> TheFourLegacyValues() => new()
    {
        {
            "ITEM_CHANGE_RESULT_DEFAULT",
            0,
            "the `case else` arm [se_cst_dw.sru:L226-L250]: coerce the edit text by the first five "
                + "characters of the column type [:L231-L244], fire OnDoItemChanged [:L247], then "
                + "FORCIBLY REWRITE the result to 2 [:L250] so the runtime will not re-apply the edit "
                + "text over the buffer. 0 is also what the event-gate early-out returns [:L187], and "
                + "`case else` catches ANY value outside {1,2,3} rather than 0 alone"
        },
        {
            "ITEM_CHANGE_RESULT_TRIGGER_VALIDATION_ERROR",
            1,
            "the EMPTY arm [se_cst_dw.sru:L212]: no statement runs, so value and item status are left "
                + "exactly as they are and 1 is returned unchanged. Returning 1 is how "
                + "OnDwnItemValidationError is raised - the source says so at [:L210] - and it is that "
                + "handler which pre-sets its own result from the stash [:L338-L340] and performs the "
                + "restore [:L369-L379]"
        },
        {
            "ITEM_CHANGE_RESULT_RESTORE_AND_REJECT_TEXT",
            2,
            "restore the original value and item status [se_cst_dw.sru:L219-L220], but ONLY IF the "
                + "earlier equality test held [:L216], because the handler may already have written the "
                + "buffer and it must not be overwritten. 2 is also the value the default arm rewrites "
                + "to [:L250]"
        },
        {
            "ITEM_CHANGE_RESULT_KEEP_VALUE_NO_FOCUS_MOVE",
            3,
            "keep the value, do not move focus, and REWRITE the result to 1 [se_cst_dw.sru:L223-L225]. "
                + "The rewrite is in the handler and not on the wire: 3 is what a handler RETURNS, 1 is "
                + "what the event YIELDS, and the difference stays observable because the "
                + "validation-error handler restores only when the stashed code was NOT 3 [:L369]"
        },
    };

    /// <summary>
    /// The four values again, paired with the meaning token their name must carry after the shared
    /// <c>ITEM_CHANGE_RESULT_</c> prefix.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="TheFourLegacyValues"/> deliberately. That table asserts the NUMBERING;
    /// this one asserts the NAMING, which is the only documentation the descriptor retains once
    /// <c>source_code_info</c> is absent. Splitting them means a failure says which of the two
    /// properties broke.
    /// </remarks>
    public static TheoryData<string, string, string> TheFourMeaningTokens() => new()
    {
        {
            "ITEM_CHANGE_RESULT_DEFAULT",
            "DEFAULT",
            "the `case else` arm [se_cst_dw.sru:L226] - the default, which is also where 0 lands"
        },
        {
            "ITEM_CHANGE_RESULT_TRIGGER_VALIDATION_ERROR",
            "TRIGGER_VALIDATION_ERROR",
            "returning 1 raises OnDwnItemValidationError [se_cst_dw.sru:L210, :L212]"
        },
        {
            "ITEM_CHANGE_RESULT_RESTORE_AND_REJECT_TEXT",
            "RESTORE_AND_REJECT_TEXT",
            "restore value and status [se_cst_dw.sru:L219-L220] and refuse the edit text [:L250]"
        },
        {
            "ITEM_CHANGE_RESULT_KEEP_VALUE_NO_FOCUS_MOVE",
            "KEEP_VALUE_NO_FOCUS_MOVE",
            "keep the value and do not switch focus [se_cst_dw.sru:L223-L224]"
        },
    };

    /// <summary>
    /// Every field of the published boundary that carries an item-change OUTCOME: the declaring message,
    /// the proto field name, and why that field exists with its locators.
    /// </summary>
    /// <remarks>
    /// This table is the positive form of the requirement - each of these is asserted to be typed as the
    /// alphabet. The negative form, that no OTHER outcome-named field exists carrying a return code or a
    /// boolean instead, is asserted by
    /// <see cref="NoOtherItemChangeOutcomeFieldExistsAndNoneIsTypedAsAReturnCodeOrABoolean"/>, which
    /// derives its expected count from this table so the two cannot disagree.
    /// </remarks>
    public static TheoryData<string, string, string> TheItemChangeOutcomeFields() => new()
    {
        {
            "ValidationSessionState",
            "item_change_ret_code",
            "THE STASH - `_nItemChangeRetCode` [se_cst_dw.sru:L96], written by the item-change handler "
                + "[:L195] and read-and-CLEARED by the validation-error handler [:L331-L332]. Its "
                + "legacy name reads like a return code and is not one, which is exactly why it is "
                + "typed as the alphabet [dataservices.v1.proto:L630]"
        },
        {
            "DwnItemValidationErrorEvent",
            "stashed_item_change_ret_code",
            "the stash as it stood BEFORE the read-and-clear at [se_cst_dw.sru:L331-L332], carried "
                + "because the pre-set [:L338-L340] and the restore guard [:L369] both read it and it "
                + "is unrecoverable afterwards [dataservices.v1.proto:L1271]"
        },
        {
            "EventResult",
            "item_change_result",
            "the value the event YIELDS after the legacy's own rewrites - 3 becomes 1 [se_cst_dw.sru:"
                + "L225] and the default arm becomes 2 [:L250]. Applies to the item-change, "
                + "do-item-change and validation-error events only [dataservices.v1.proto:L1453]"
        },
    };

    /// <summary>
    /// The vocabulary of the OTHER alphabets that share these digits: one row per word that would betray
    /// a value borrowed from the return-code algebra or from the broker's veto, with where that word
    /// legitimately lives.
    /// </summary>
    /// <remarks>
    /// Fragments rather than whole names, because the danger is a value merely SPELLED like a prevention
    /// - <c>PREVENT</c>, <c>PREVENT_ONCE</c> and <c>ITEM_CHANGE_RESULT_PREVENT</c> would all be the same
    /// mistake and only the first is caught by comparing whole names. One row per word rather than one
    /// assertion over a list, so a failure names the borrowed word directly.
    /// </remarks>
    public static TheoryData<string, string> TheForeignAlphabetWords() => new()
    {
        {
            "PREVENT",
            "belongs to TWO other alphabets at once - RetCode.PREVENT = 1 [retcode.sru:L42] and the "
                + "broker's PREVENT_ONCE = 1 / PREVENT_DEEP = 2 [n_cst_eventful.sru:L111-L112]"
        },
        {
            "CANCEL",
            "belongs to the return-code algebra - CANCELED / CANCELLED = -2 [retcode.sru:L44-L45], the "
                + "value that is neither succeeded nor failed"
        },
        {
            "FAILED",
            "belongs to the return-code algebra - FAILED = -1 [retcode.sru:L43]. No item-change arm "
                + "signals failure at all: the alphabet describes what to do with the edit, not whether "
                + "something went wrong"
        },
    };

    // ==============================================================================================
    //  FIELD-NAME VOCABULARY - how an "item-change OUTCOME" field is recognised
    //
    //  Recognition is BY NAME rather than by an exception list, and that choice is what makes the
    //  negative assertion durable: a field added later that carries an item-change outcome will be
    //  caught because of what it is CALLED, without anyone remembering to extend a list here.
    //
    //  The one item-change-named field that is legitimately a boolean is excluded by this vocabulary
    //  rather than by a special case - `in_item_change` carries no outcome token, because it is a
    //  re-entrancy GUARD (`_bDoItemChange` [se_cst_dw.sru:L92]) and not a result. It gets its own
    //  assertion below so that its absence from this set reads as a decision rather than an oversight.
    // ==============================================================================================

    /// <summary>The fragment that marks a field as belonging to the item-change path.</summary>
    private const string ItemChangeNameFragment = "item_change";

    /// <summary>
    /// The fragments that mark a field as carrying an OUTCOME rather than an input, a payload or a
    /// re-entrancy guard.
    /// </summary>
    private static readonly string[] OutcomeNameFragments =
    [
        "ret_code",  // the legacy spelling, `_nItemChangeRetCode` [se_cst_dw.sru:L96]
        "result",    // the contract's own spelling
        "outcome",   // guarded against pre-emptively; no field uses it today
        "code",      // catches any `..._code` spelling the two above would miss
    ];

    /// <summary>Every field declared anywhere in <c>dataservices.v1.proto</c>, nested messages included.</summary>
    /// <remarks>
    /// Restricted to the one file on purpose. The alphabet is declared there, and a field in
    /// <c>persistence.v1</c> or <c>common.v1</c> naming an item-change outcome would be a scoping
    /// question rather than a typing one - none exists, and inventing an assertion about one would
    /// obscure what this file is for.
    /// </remarks>
    private static IEnumerable<FieldDescriptor> DataServicesFields() =>
        ContractDescriptors.AllMessages()
            .Where(static message =>
                string.Equals(message.File.Name, AlphabetFileName, StringComparison.Ordinal))
            .SelectMany(static message => message.Fields.InDeclarationOrder());

    /// <summary>
    /// Whether <paramref name="field"/>'s proto name says it carries an item-change outcome.
    /// </summary>
    private static bool IsItemChangeOutcomeName(FieldDescriptor field) =>
        field.Name.Contains(ItemChangeNameFragment, StringComparison.Ordinal)
        && OutcomeNameFragments.Any(fragment =>
            field.Name.Contains(fragment, StringComparison.Ordinal));

    /// <summary>Renders a field for a failure message as <c>message.field : type</c>.</summary>
    private static string Describe(FieldDescriptor field)
    {
        string type = field.FieldType switch
        {
            FieldType.Enum => field.EnumType.FullName,
            FieldType.Message or FieldType.Group => field.MessageType.FullName,
            _ => field.FieldType.ToString(),
        };

        return $"{field.ContainingType.FullName}.{field.Name} : {type}";
    }

    // ==============================================================================================
    //  PART 1 - THE ALPHABET IS ITS OWN ENUM, CARRYING THE FOUR LEGACY NUMBERS AND NOTHING ELSE
    // ==============================================================================================

    /// <summary>
    /// Each of the four legacy dispatch arms is declared exactly once, under its own name, with its own
    /// number.
    /// </summary>
    /// <param name="valueName">The proto spelling, as authored - not the PascalCased C# projection.</param>
    /// <param name="legacyNumber">The number the legacy <c>choose case</c> arm answers to.</param>
    /// <param name="legacyBehaviour">
    /// What that arm observably does, with its oracle locator. Reported in every failure message so a
    /// break names the lost behaviour rather than only the mismatched string.
    /// </param>
    [Theory]
    [MemberData(nameof(TheFourLegacyValues))]
    public void EachLegacyDispatchNumberIsDeclaredOnceUnderItsOwnName(
        string valueName,
        int legacyNumber,
        string legacyBehaviour)
    {
        EnumValueDescriptor[] declarations = Alphabet.Values
            .Where(value => string.Equals(value.Name, valueName, StringComparison.Ordinal))
            .ToArray();

        // DECLARED EXACTLY ONCE. Zero declarations means the arm has no wire representation at all;
        // two would mean the same name carries two numbers, which is the silent-divergence shape the
        // whole contract-test folder exists to prevent.
        Assert.True(
            declarations.Length == 1,
            $"{Alphabet.FullName} declares '{valueName}' {declarations.Length} times, expected exactly "
                + $"once. It carries {legacyNumber}, which is {legacyBehaviour}. Declared values: "
                + $"{string.Join(", ", Alphabet.Values.Select(value => $"{value.Name}={value.Number}"))}.");

        Assert.Equal(legacyNumber, declarations[0].Number);

        // THE NUMBER IS THE CONTRACT, NOT THE ORDINAL. Asserted through the descriptor rather than the
        // generated C# enum because protoc PascalCases the projection, so the CLR member reads
        // `ItemChangeResultDefault` and can never confirm the authored SCREAMING_SNAKE spelling that
        // appears in a JSON payload, a log record or a characterization recording (AAP 0.4.5.3).
        Assert.Equal(
            $"{Alphabet.FullName}.{valueName}",
            declarations[0].FullName);
    }

    /// <summary>
    /// The alphabet declares no number outside the legacy four, and no fifth value beyond an optional
    /// proto3 zero sentinel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both layouts are accepted, and this assertion is written to allow either.</b> proto3 requires a
    /// zero first member, and here zero is a REAL legacy value - the event-gate early-out returns it
    /// [<c>se_cst_dw.sru:L187</c>] and <c>case else</c> catches it [<c>:L226</c>] - so the contract may
    /// legitimately either spend the zero slot on that legacy meaning, or add a separate
    /// <c>..._UNSPECIFIED = 0</c> alias beside it. What is not acceptable is any number outside
    /// <c>{0,1,2,3}</c>, or a second alias of any kind.
    /// </para>
    /// <para>
    /// <b>The layout actually found on this contract</b> is the first: four values, zero carried by
    /// <c>ITEM_CHANGE_RESULT_DEFAULT</c>, no sentinel and no <c>allow_alias</c>. The tolerance above
    /// exists so that adding a sentinel later is a reviewable change rather than an automatic failure.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheAlphabetDeclaresNoNumberOutsideTheLegacyFourAndNoFifthValue()
    {
        string[] outsideTheLegacySet = Alphabet.Values
            .Where(value => !LegacyDispatchNumbers.Contains(value.Number))
            .Select(value => $"{value.Name}={value.Number}")
            .ToArray();

        // A NUMBER OUTSIDE {0,1,2,3} IS A NEW DOMAIN VALUE, and the legacy dispatch has no arm for it:
        // `case else` would swallow it and rewrite it to 2 [se_cst_dw.sru:L226-L250], so it could never
        // round-trip. The failure message names the offender.
        Assert.True(
            outsideTheLegacySet.Length == 0,
            $"{Alphabet.FullName} declares {outsideTheLegacySet.Length} value(s) outside the legacy "
                + $"dispatch set {{{string.Join(",", LegacyDispatchNumbers)}}} "
                + $"[se_cst_dw.sru:L211-L251]: {string.Join(", ", outsideTheLegacySet)}.");

        // ALL FOUR ARE PRESENT. The set has to be complete as well as unpolluted, or an arm silently
        // loses its representation - and 1 and 3 are the ones at risk, because a reader who knows 3 is
        // rewritten to 1 [:L225] is tempted to drop one of them.
        int[] declaredNumbers = Alphabet.Values
            .Select(value => value.Number)
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal(LegacyDispatchNumbers.Order().ToArray(), declaredNumbers);

        // AT MOST ONE ADDITIONAL VALUE, AND ONLY AS THE ZERO SENTINEL.
        EnumValueDescriptor[] sentinels = Alphabet.Values.Where(IsZeroSentinel).ToArray();

        Assert.True(
            sentinels.Length <= 1,
            $"{Alphabet.FullName} declares {sentinels.Length} zero sentinels; at most one is "
                + $"permitted: {string.Join(", ", sentinels.Select(value => value.Name))}.");

        // The expected count is DERIVED from the legacy table plus the optional sentinel, never written
        // as a literal, so adding a row to the table cannot leave this count stale.
        int permittedCount = LegacyDispatchNumbers.Length + sentinels.Length;

        Assert.True(
            Alphabet.Values.Count == permittedCount,
            $"{Alphabet.FullName} declares {Alphabet.Values.Count} values; expected {permittedCount} "
                + $"({LegacyDispatchNumbers.Length} legacy dispatch arms"
                + $"{(sentinels.Length == 0 ? " and no zero sentinel" : " plus one zero sentinel")}). "
                + $"Declared: {string.Join(", ", Alphabet.Values.Select(value => $"{value.Name}={value.Number}"))}.");
    }

    /// <summary>
    /// The four numbers are distinct: unlike the return-code algebra, this alphabet has no evidenced
    /// aliasing.
    /// </summary>
    /// <remarks>
    /// The contrast is asserted too, and it matters. <c>common.v1.RetCode.Value</c> genuinely does alias
    /// - <c>OK</c>, <c>SUCCESS</c> and <c>ALLOW</c> are three spellings of zero and <c>CANCELED</c> and
    /// <c>CANCELLED</c> two spellings of −2 [<c>retcode.sru:L39-L45</c>] - so it carries
    /// <c>option allow_alias</c>. Showing that the foil aliases while the alphabet does not proves this
    /// assertion is discriminating rather than trivially true of every enum in the boundary.
    /// </remarks>
    [Fact]
    public void TheFourNumbersAreDistinctBecauseThisAlphabetHasNoEvidencedAliasing()
    {
        IReadOnlyList<EnumValueDescriptor> meaningful = MeaningfulValues();

        string[] aliased = meaningful
            .GroupBy(value => value.Number)
            .Where(group => group.Count() > 1)
            .Select(group =>
                $"{group.Key} = {string.Join(" / ", group.Select(value => value.Name))}")
            .ToArray();

        // NO ALIAS IS EVIDENCED IN THE ORACLE. The legacy arms are literal digits in a `choose case`
        // [se_cst_dw.sru:L211-L251]; there is no second spelling of any of them to preserve. An alias
        // here would be an invention, and it would make a recorded value ambiguous to read back.
        Assert.True(
            aliased.Length == 0,
            $"{Alphabet.FullName} aliases {aliased.Length} number(s), which the oracle does not "
                + $"evidence: {string.Join("; ", aliased)}.");

        Assert.Equal(LegacyDispatchNumbers.Length, meaningful.Select(value => value.Number).Distinct().Count());

        // THE CONTRAST, so this assertion cannot be mistaken for a property every enum here has.
        int returnCodeNames = ReturnCode.Values.Count;
        int returnCodeNumbers = ReturnCode.Values.Select(value => value.Number).Distinct().Count();

        Assert.True(
            returnCodeNumbers < returnCodeNames,
            $"{ReturnCode.FullName} was expected to alias - OK/SUCCESS/ALLOW share 0 and "
                + "CANCELED/CANCELLED share -2 [retcode.sru:L39-L45] - but it declares "
                + $"{returnCodeNames} names over {returnCodeNumbers} distinct numbers. If the foil has "
                + "stopped aliasing, the distinctness assertion above no longer discriminates and this "
                + "test needs rewriting rather than deleting.");
    }

    /// <summary>
    /// The zero slot carries legacy meaning rather than only a synthetic "absent" name.
    /// </summary>
    /// <remarks>
    /// True under both permitted layouts, because zero is a legacy value in its own right here: the
    /// event-gate early-out returns it [<c>se_cst_dw.sru:L187</c>] and <c>case else</c> - which catches
    /// any value outside <c>{1,2,3}</c>, zero included - coerces, fires the changed event and rewrites
    /// the result to 2 [<c>:L226-L250</c>]. A contract whose zero slot held ONLY a sentinel would have
    /// nowhere to put that arm.
    /// </remarks>
    [Fact]
    public void TheZeroSlotCarriesLegacyMeaningRatherThanOnlyASyntheticSentinel()
    {
        EnumValueDescriptor[] atZero = Alphabet.Values
            .Where(value => value.Number == LegacyDispatchNumbers[0])
            .ToArray();

        Assert.NotEmpty(atZero);

        EnumValueDescriptor[] carryingMeaning = atZero.Where(value => !IsZeroSentinel(value)).ToArray();

        Assert.True(
            carryingMeaning.Length == 1,
            $"{Alphabet.FullName} must give the zero slot exactly one legacy-meaning name for the "
                + "`case else` arm [se_cst_dw.sru:L226-L250], beside at most one optional sentinel. "
                + $"Found {carryingMeaning.Length}: {string.Join(", ", atZero.Select(value => value.Name))}.");
    }

    // ==============================================================================================
    //  PART 2 - IT IS NOT THE RETURN-CODE ALGEBRA
    //
    //  Every assertion in this part is written so that it CANNOT pass if the alphabet were mapped onto
    //  RetCode. Read them back with that hypothesis in mind: two identical descriptors would fail the
    //  non-identity assertion, a reused enum would fail the file and scope assertions, and a value
    //  spelled PREVENT would fail both the disjointness and the fragment assertions.
    // ==============================================================================================

    /// <summary>
    /// The alphabet and the return-code algebra are two different enumerations, in two different files,
    /// at two different scopes - the contract did not reuse <c>RetCode</c> for this purpose.
    /// </summary>
    [Fact]
    public void TheAlphabetAndTheReturnCodeAlgebraAreTwoDifferentEnumerations()
    {
        EnumDescriptor alphabet = Alphabet;
        EnumDescriptor returnCode = ReturnCode;

        // TWO DISTINCT DESCRIPTOR INSTANCES. Reference inequality is the strongest available statement
        // that the contract did not simply point the item-change fields at the existing enum: descriptors
        // are canonical per declaration, so two declarations can never be the same object.
        Assert.NotSame(alphabet, returnCode);
        Assert.NotEqual(returnCode.FullName, alphabet.FullName);

        // THE EXACT IDENTITIES BEING COMPARED, asserted rather than assumed, so a lookup that silently
        // resolved to some other enum could not make the inequality above pass vacuously.
        Assert.Equal($"dataservices.v1.{AlphabetEnumName}", alphabet.FullName);
        Assert.Equal(ReturnCodeEnumName, returnCode.FullName);

        // DIFFERENT FILES. The alphabet belongs to the DataWindow event surface; the algebra is shared
        // vocabulary. Declaring the alphabet in common.v1 would invite exactly the conflation this file
        // guards against, by putting the two side by side in one namespace.
        Assert.Equal(AlphabetFileName, alphabet.File.Name);
        Assert.Equal(ReturnCodeFileName, returnCode.File.Name);

        // DIFFERENT SCOPES, and the asymmetry is deliberate on both sides. The alphabet is declared at
        // FILE scope because it stands alone. The algebra is NESTED inside a wrapper message, which is
        // what lets three sibling code spaces - RetCode.Value, XmlParseStatus.Value and
        // SqliteResultCode.Value - keep colliding names apart. Asserting the shapes differ is a further
        // independent way of saying one is not the other.
        Assert.Null(alphabet.ContainingType);
        Assert.NotNull(returnCode.ContainingType);
        Assert.Equal("RetCode", returnCode.ContainingType.Name);
    }

    /// <summary>
    /// No item-change value name is declared anywhere else in the published boundary - and in particular
    /// not by the return-code algebra.
    /// </summary>
    /// <param name="valueName">The item-change value's proto spelling.</param>
    /// <param name="legacyNumber">Its legacy dispatch number, re-asserted on the single declaration site.</param>
    /// <param name="legacyBehaviour">The behaviour it encodes, reported in any failure message.</param>
    /// <remarks>
    /// Swept across all three files rather than only against the foil. A name declared twice anywhere in
    /// the boundary makes a recorded identifier ambiguous to read back, and the item-change names are
    /// exactly the ones a reader is most likely to mis-attribute.
    /// </remarks>
    [Theory]
    [MemberData(nameof(TheFourLegacyValues))]
    public void NoItemChangeValueNameIsDeclaredAnywhereElseInThePublishedBoundary(
        string valueName,
        int legacyNumber,
        string legacyBehaviour)
    {
        IReadOnlyList<EnumValueDescriptor> sites = ContractDescriptors.FindEnumValuesNamed(valueName);

        Assert.True(
            sites.Count == 1,
            $"'{valueName}' is declared at {sites.Count} site(s) across the published boundary, expected "
                + $"exactly one. It means: {legacyBehaviour}. Sites: "
                + $"{string.Join(", ", sites.Select(site => site.FullName))}.");

        // THE ONE SITE IS THIS ALPHABET'S. Reference identity against the descriptor the alphabet itself
        // hands back, which is the strongest available statement of membership - `EnumValueDescriptor`
        // exposes no back-pointer to its declaring enum, so the assertion is made from the enum's side.
        Assert.Same(Alphabet.FindValueByName(valueName), sites[0]);
        Assert.Equal(legacyNumber, sites[0].Number);

        // AND THE FOIL DOES NOT KNOW THE NAME. Stated separately from the sweep because this is the
        // specific conflation being guarded against: a return-code algebra that had grown an
        // ITEM_CHANGE_RESULT_* member would mean the two domains had been merged after all.
        Assert.Null(ReturnCode.FindValueByName(valueName));
    }

    /// <summary>The two alphabets share no value name, in either direction.</summary>
    /// <remarks>
    /// Both directions are checked because the merge could have been performed from either end: the
    /// alphabet could have borrowed <c>PREVENT</c>, or the algebra could have absorbed the item-change
    /// names. Either would be the same defect.
    /// </remarks>
    [Fact]
    public void TheTwoAlphabetsShareNoValueNameInEitherDirection()
    {
        HashSet<string> alphabetNames = Alphabet.Values
            .Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> returnCodeNames = ReturnCode.Values
            .Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);

        string[] shared = alphabetNames.Intersect(returnCodeNames, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Assert.True(
            shared.Length == 0,
            $"{Alphabet.FullName} and {ReturnCode.FullName} share {shared.Length} value name(s), which "
                + "would make a recorded identifier unattributable to one alphabet or the other: "
                + $"{string.Join(", ", shared)}.");

        // NON-VACUITY. Both sets must be non-empty, or an emptied lookup would make the intersection
        // trivially empty and the assertion above meaningless.
        Assert.NotEmpty(alphabetNames);
        Assert.NotEmpty(returnCodeNames);
    }

    /// <summary>
    /// No item-change value borrows a word from another alphabet that shares its digits.
    /// </summary>
    /// <param name="foreignWord">
    /// The word, checked as a name FRAGMENT rather than as a whole name because a value merely spelled
    /// like a prevention is the same mistake as one named exactly <c>PREVENT</c>.
    /// </param>
    /// <param name="whereItBelongs">Where the word legitimately lives, reported in any failure message.</param>
    [Theory]
    [MemberData(nameof(TheForeignAlphabetWords))]
    public void NoItemChangeValueBorrowsAWordFromAnotherAlphabet(string foreignWord, string whereItBelongs)
    {
        string[] offenders = Alphabet.Values
            .Where(value => value.Name.Contains(foreignWord, StringComparison.Ordinal))
            .Select(value => value.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"{Alphabet.FullName} borrows '{foreignWord}', which {whereItBelongs}. Offending value(s): "
                + $"{string.Join(", ", offenders)}.");

        // NON-VACUITY, ROW BY ROW, and it is the whole point of checking words rather than whole names:
        // each word really is findable in the foil, so finding none of them here is a fact about the
        // alphabet rather than about the search term being unmatchable anywhere.
        Assert.Contains(
            ReturnCode.Values,
            value => value.Name.Contains(foreignWord, StringComparison.Ordinal));
    }

    /// <summary>
    /// The numeral 1 is bound to the validation-error trigger here and to <c>PREVENT</c> there - one
    /// digit, two unrelated meanings, and the reason this whole suite exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A third alphabet shares the digit as well: the broker's veto spells 1 as <c>PREVENT_ONCE</c> and 2
    /// as <c>PREVENT_DEEP</c> [<c>n_cst_eventful.sru:L111-L112</c>], and a fourth shares 1 too - the
    /// event gate's <c>EID_ROWFOCUSCHANGE = 1</c> [<c>se_cst_dw.sru:L41</c>]. All four are modelled as
    /// separate wire domains. Nothing in this system may treat "1" as a value that can be moved between
    /// them.
    /// </para>
    /// <para>
    /// This is the assertion that would fail loudest under the conflation: if the item-change fields were
    /// carried as return codes, the value at 1 would be named <c>PREVENT</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheNumeralOneIsBoundToTheValidationErrorTriggerAndNotToPrevent()
    {
        const int one = 1; // `case 1` [se_cst_dw.sru:L212]; PREVENT [retcode.sru:L42]

        EnumValueDescriptor itemChangeOne = Assert.Single(
            Alphabet.Values,
            value => value.Number == one);

        Assert.Equal("ITEM_CHANGE_RESULT_TRIGGER_VALIDATION_ERROR", itemChangeOne.Name);

        // THE SAME DIGIT IN THE FOIL. Sole rather than single-of-many is not asserted for the algebra,
        // because it aliases by design; what matters is that its 1 is PREVENT and that this is a
        // different word from the one above.
        string[] returnCodeOne = ReturnCode.Values
            .Where(value => value.Number == one)
            .Select(value => value.Name)
            .ToArray();

        Assert.Contains("PREVENT", returnCodeOne);

        Assert.DoesNotContain(itemChangeOne.Name, returnCodeOne);
    }

    // ==============================================================================================
    //  PART 3 - EVERY FIELD THAT CARRIES AN ITEM-CHANGE OUTCOME IS TYPED AS THE ALPHABET
    //
    //  Declaring the enum correctly and then carrying its values in an int32, a bool or a RetCode field
    //  would defeat the entire point, so the declaration is not enough on its own: the TYPING of the
    //  fields is where the separation is either honoured or lost.
    // ==============================================================================================

    /// <summary>
    /// Each field that carries an item-change outcome is typed as the alphabet - not as a return code,
    /// not as a boolean, not as a bare integer.
    /// </summary>
    /// <param name="messageName">The declaring message's proto name.</param>
    /// <param name="fieldName">The field's proto name, in its authored <c>snake_case</c> spelling.</param>
    /// <param name="why">Why the field exists, with its locators; reported in any failure message.</param>
    [Theory]
    [MemberData(nameof(TheItemChangeOutcomeFields))]
    public void EveryFieldCarryingAnItemChangeOutcomeIsTypedAsTheAlphabet(
        string messageName,
        string fieldName,
        string why)
    {
        MessageDescriptor message = ContractDescriptors.RequireMessage(messageName);
        FieldDescriptor field = ContractDescriptors.RequireField(message, fieldName);

        // AN ENUM FIELD, FIRST. A bare int32 would carry the numbers and lose the domain, which is the
        // quietest possible form of the conflation: it type-checks, it round-trips, and it tells a reader
        // nothing about which alphabet the number belongs to.
        Assert.True(
            field.FieldType == FieldType.Enum,
            $"{Describe(field)} must be typed as {Alphabet.FullName} - it carries {why}.");

        // AND THE ENUM MUST BE THIS ONE. Reference identity, so a same-shaped copy declared elsewhere
        // would not satisfy it.
        Assert.Same(Alphabet, field.EnumType);

        // SINGULAR, NOT REPEATED. Each of these fields holds one outcome: the stash holds one code
        // [se_cst_dw.sru:L195], and one event yields one result [:L253].
        Assert.False(field.IsRepeated);
        Assert.False(field.IsMap);
    }

    /// <summary>
    /// No other item-change-outcome field exists in <c>dataservices.v1</c>, and none of them is typed as
    /// a return code or as a boolean.
    /// </summary>
    /// <remarks>
    /// The expected set is derived from <see cref="TheItemChangeOutcomeFields"/>, so a field added later
    /// under an outcome-shaped name fails here until it is either typed as the alphabet and added to that
    /// table, or renamed because it is not an outcome after all. Both outcomes are reviews; neither is a
    /// silent pass.
    /// </remarks>
    [Fact]
    public void NoOtherItemChangeOutcomeFieldExistsAndNoneIsTypedAsAReturnCodeOrABoolean()
    {
        FieldDescriptor[] outcomeFields = DataServicesFields().Where(IsItemChangeOutcomeName).ToArray();

        // NOT TYPED AS A RETURN CODE. Reported before the count so that a mistyped field is diagnosed as
        // a mistyping rather than as an unexpected extra.
        string[] carriedAsReturnCodes = outcomeFields
            .Where(field => field.FieldType == FieldType.Enum && ReferenceEquals(field.EnumType, ReturnCode))
            .Select(Describe)
            .ToArray();

        Assert.True(
            carriedAsReturnCodes.Length == 0,
            $"{carriedAsReturnCodes.Length} item-change-outcome field(s) are typed as "
                + $"{ReturnCode.FullName}. The two alphabets share only digits: 1 is PREVENT there "
                + "[retcode.sru:L42] and the validation-error trigger here [se_cst_dw.sru:L210]. "
                + $"Offenders: {string.Join(", ", carriedAsReturnCodes)}.");

        // NOT TYPED AS A BOOLEAN EITHER. A boolean could carry at most two of the four arms, so it would
        // silently merge the other two - and the pair it would merge (1 and 3) is precisely the pair the
        // validation-error handler's restore guard has to tell apart [se_cst_dw.sru:L369].
        string[] carriedAsBooleans = outcomeFields
            .Where(field => field.FieldType == FieldType.Bool)
            .Select(Describe)
            .ToArray();

        Assert.True(
            carriedAsBooleans.Length == 0,
            $"{carriedAsBooleans.Length} item-change-outcome field(s) are typed as bool, which cannot "
                + $"carry a four-value alphabet [se_cst_dw.sru:L211-L251]: "
                + $"{string.Join(", ", carriedAsBooleans)}.");

        // EVERY ONE IS THE ALPHABET, and the set is exactly the one the table enumerates.
        string[] notTheAlphabet = outcomeFields
            .Where(field => field.FieldType != FieldType.Enum || !ReferenceEquals(field.EnumType, Alphabet))
            .Select(Describe)
            .ToArray();

        Assert.True(
            notTheAlphabet.Length == 0,
            $"{notTheAlphabet.Length} item-change-outcome field(s) are not typed as "
                + $"{Alphabet.FullName}: {string.Join(", ", notTheAlphabet)}.");

        Assert.Equal(TheItemChangeOutcomeFields().Count, outcomeFields.Length);

        // NON-VACUITY IN TWO DIRECTIONS.
        //
        // First, the scan must actually reach fields: a broken enumerator would find no outcome fields
        // and pass every assertion above.
        Assert.NotEmpty(DataServicesFields());

        // Second, the return-code enum IS used elsewhere in this same file - OpenValidationSessionResponse
        // and CloseValidationSessionResponse both carry a `ret_code` typed as it. So its absence from the
        // three fields above is a deliberate choice made where the alternative was right at hand, not an
        // accident of the type being unavailable.
        Assert.Contains(
            DataServicesFields(),
            field => field.FieldType == FieldType.Enum && ReferenceEquals(field.EnumType, ReturnCode));
    }

    /// <summary>
    /// The one item-change-named boolean in the contract is the re-entrancy guard, and it is a boolean by
    /// design rather than by mistake.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ValidationSessionState.in_item_change</c> reproduces <c>_bDoItemChange</c>
    /// [<c>se_cst_dw.sru:L92</c>], which the handler saves, sets and restores around the semantic call
    /// [<c>:L192-L196</c>]. It records WHERE EXECUTION IS, not what an event decided, and its one
    /// observable effect is on the kill-focus path: the deferred accept is queued only when the flag is
    /// clear [<c>:L387-L393</c>]. Two states are all it has, so a boolean is exactly right.
    /// </para>
    /// <para>
    /// Asserted explicitly so that its exclusion from the outcome sweep above reads as a decision with a
    /// reason, and so that nobody "fixes" it into the alphabet on the strength of its name.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOnlyItemChangeNamedBooleanIsTheReentrancyGuardAndItIsBooleanByDesign()
    {
        MessageDescriptor sessionState = ContractDescriptors.RequireMessage("ValidationSessionState");
        FieldDescriptor guard = ContractDescriptors.RequireField(sessionState, "in_item_change");

        Assert.Equal(FieldType.Bool, guard.FieldType);
        Assert.False(IsItemChangeOutcomeName(guard));

        // IT IS THE ONLY ONE. Any other item-change-named boolean would be either a second guard - which
        // the oracle does not have, since `_bDwnItemValidationError` [:L94] is named for the
        // validation-error handler rather than for item change - or an outcome that had been flattened
        // into a flag.
        string[] itemChangeBooleans = DataServicesFields()
            .Where(field =>
                field.FieldType == FieldType.Bool
                && field.Name.Contains(ItemChangeNameFragment, StringComparison.Ordinal))
            .Select(Describe)
            .ToArray();

        Assert.Equal([Describe(guard)], itemChangeBooleans);
    }

    // ==============================================================================================
    //  PART 4 - THE SEMANTICS ARE ON THE CONTRACT, NOT IN FOLKLORE
    //
    //  MEASURED, NOT ASSUMED: this build's descriptor carries no source information -
    //  `DataservicesV1Reflection.Descriptor.ToProto().SourceCodeInfo` is null, because Grpc.Tools does
    //  not embed it - so the prose at dataservices.v1.proto:L646-L710 is unreachable at runtime and no
    //  assertion can read it. Reading the .proto text off disk would be a different mechanism rather
    //  than the same one, and it would put file I/O into a suite whose repeatability is its whole value,
    //  so it is deliberately not done.
    //
    //  WHAT SURVIVES INTO THE DESCRIPTOR IS THE NAMES, and they are not a consolation prize. The name is
    //  what a JSON payload carries, what a log record prints and what a characterization recording
    //  compares - so it is the only self-description a consumer of this wire ever actually sees. The
    //  assertions below make each name carry its meaning, which is what turns the naming from a style
    //  choice into a documented property of the contract.
    //
    //  THE PROSE ITSELF lives in Proto/dataservices.v1.proto:L646-L710 (the four arms, each with its
    //  se_cst_dw.sru locator) and in docs/CONTRACTS.md 6.5 ("The item-change alphabet is its own
    //  enumeration"). Both are cited from the XML documentation on the tests that depend on them.
    //
    //  THE REST PROJECTION IS NOT ASSERTED, and the reason is that it does not carry this alphabet:
    //  `ITEM_CHANGE_RESULT_` appears nowhere in OpenApi/gateway.v1.yaml. The item-change chain rides
    //  `EventChain`, which is bidirectional-stream-only and therefore has no REST form; the
    //  `EID_ITEMCHANGE` occurrences in that document belong to the event-GATE bitmask
    //  [se_cst_dw.sru:L43], a different alphabet again. So the optional projection row is skipped
    //  rather than faked.
    // ==============================================================================================

    /// <summary>
    /// Each value's name carries its legacy meaning, because the descriptor keeps no comments and the
    /// name is therefore the only documentation that reaches the wire.
    /// </summary>
    /// <param name="valueName">The full proto spelling of the value.</param>
    /// <param name="meaningToken">
    /// The meaning-bearing remainder of the name after the shared <c>ITEM_CHANGE_RESULT_</c> prefix.
    /// </param>
    /// <param name="locator">The oracle locator for that meaning, reported in any failure message.</param>
    /// <remarks>
    /// The prose behind each token is in <c>Proto/dataservices.v1.proto:L646-L710</c> and in
    /// <c>docs/CONTRACTS.md</c> 6.5. This test pins the part of it that a runtime consumer can see.
    /// </remarks>
    [Theory]
    [MemberData(nameof(TheFourMeaningTokens))]
    public void EachValueNameCarriesItsLegacyMeaningBecauseTheDescriptorKeepsNoComments(
        string valueName,
        string meaningToken,
        string locator)
    {
        EnumValueDescriptor value = Assert.Single(
            Alphabet.Values,
            candidate => string.Equals(candidate.Name, valueName, StringComparison.Ordinal));

        Assert.StartsWith(AlphabetValuePrefix, value.Name, StringComparison.Ordinal);

        // THE NAME IS EXACTLY PREFIX + MEANING. Asserted as equality rather than as "contains", so a
        // name that had acquired extra decoration - or lost the meaning while keeping the prefix - fails
        // rather than passing on a substring match.
        Assert.Equal(AlphabetValuePrefix + meaningToken, value.Name);

        Assert.True(
            meaningToken.Length > 0,
            $"'{valueName}' must carry a meaning after the '{AlphabetValuePrefix}' prefix - {locator}.");
    }

    /// <summary>
    /// Every value name carries the domain prefix, and no return-code name does - so a recorded
    /// identifier can always be attributed to the alphabet it came from.
    /// </summary>
    /// <remarks>
    /// This is the property that makes the shared digits survivable in practice. A recording holding the
    /// bare number 1 is ambiguous between four alphabets; a recording holding
    /// <c>ITEM_CHANGE_RESULT_TRIGGER_VALIDATION_ERROR</c> is not ambiguous at all. Preserving that
    /// property is why the JSON projection of these enums matters as much as the numeric one.
    /// </remarks>
    [Fact]
    public void EveryValueNameCarriesTheDomainPrefixAndNoReturnCodeNameDoes()
    {
        string[] withoutThePrefix = Alphabet.Values
            .Where(value => !value.Name.StartsWith(AlphabetValuePrefix, StringComparison.Ordinal))
            .Select(value => value.Name)
            .ToArray();

        Assert.True(
            withoutThePrefix.Length == 0,
            $"{withoutThePrefix.Length} value(s) of {Alphabet.FullName} do not carry the "
                + $"'{AlphabetValuePrefix}' prefix, so a recorded identifier could not be attributed to "
                + $"this alphabet: {string.Join(", ", withoutThePrefix)}.");

        // AND THE PREFIX IS EXCLUSIVE TO IT. A return-code member wearing this prefix would re-open the
        // conflation from the other side.
        string[] foilWithThePrefix = ReturnCode.Values
            .Where(value => value.Name.StartsWith(AlphabetValuePrefix, StringComparison.Ordinal))
            .Select(value => value.Name)
            .ToArray();

        Assert.True(
            foilWithThePrefix.Length == 0,
            $"{ReturnCode.FullName} declares {foilWithThePrefix.Length} value(s) carrying the "
                + $"item-change prefix: {string.Join(", ", foilWithThePrefix)}.");

        // NON-VACUITY: the alphabet must have values for the prefix sweep to mean anything.
        Assert.Equal(LegacyDispatchNumbers.Length, MeaningfulValues().Count);
    }
}
