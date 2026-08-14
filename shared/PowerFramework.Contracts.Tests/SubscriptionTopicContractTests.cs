// ==================================================================================================
//  SubscriptionTopicContractTests - TOPIC IDENTITY TRAVELS DECOMPOSED
//  ------------------------------------------------------------------------------------------------
//  SUBJECT     Proto/dataservices.v1.proto, message `BrokerTopic`
//  AUTHORITY   Agent Action Plan 0.6.1.2 (one legacy string fuses three encodings) and
//              0.3.4 (the fused form is reconstituted ONLY at the compatibility edge)
//  ORACLE      ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru      - the twelve topics
//              ws_objects/pfw.thread.pbl.src/n_cst_threading.sru             - the lifetime namespace
//              ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru     - the broker's grammar
//              Read as specification only, never at run time (constraint C-C).
//
//  THE ONE CLAIM THIS FILE EXISTS TO KEEP
//  ------------------------------------------------------------------------------------------------
//  The subscription message carries SEPARATE `sequence`, `name` and `lifetime` fields, and the fused
//  legacy string is NOT its primary representation.
//
//  WHY THE LEGACY STRING CANNOT SIMPLY BE TRANSMITTED
//  ------------------------------------------------------------------------------------------------
//  One string is doing three jobs at once, and only one of them is visible to a naive reader.
//
//   1. ORDERING, baked into the spelling. se_cst_dw.sru declares twelve broker topics at L47-L76 and
//      exactly TWO of them carry a leading digit: EVT_ITEMCHANGED is "0-itemchanged" [:L54] and
//      EVT_EDITCHANGED is "1-editchanged" [:L57]. The other ten carry none. Those digits are not
//      decoration - the broker keeps its subscription registry in ASCENDING LEXICAL ORDER OF THE
//      SUBSCRIPTION NAME. Its insertion scan at n_cst_eventful.sru:L407-L422 places a new subscription
//      before the first existing entry whose name compares GREATER (`Events[nIndex].name >
//      newEvent.name`), breaking ties by priority, and maintains the first/last name bounds at
//      :L439-L440. ASCII digits sort before letters, so THE DISPATCH ORDER IS A FUNCTION OF THE
//      STRING'S SPELLING.
//
//   2. LIFETIME, as a trailing namespace. `.^persistent` is appended to the same string at
//      n_cst_threading.sru:L596 - and only when the caller supplied no namespace of their own, per the
//      guard at :L594 (`if Pos(name,".") > 0 then` return the name untouched). At :L544 the bare form
//      `of_Off(".^persistent")` is passed with no logical name at all. This third encoding is the one
//      most easily missed.
//
//   3. CAPTURE MODE AND EXPLICIT PRIORITY, in the general case, from the symbol constants at
//      n_cst_eventful.sru:L91-L98 consumed by the subscribe parser at :L339-L381.
//
//  Transmit the fused string as-is and the ordering becomes INVISIBLE: a consumer cannot tell that
//  "0-itemchanged" dispatches before "1-editchanged" for a reason, and reads the "0-" as decoration.
//  Parse it at the far end and the contract has acquired an UNDOCUMENTED GRAMMAR that every consumer
//  must reimplement identically or diverge. So the wire carries the parts as separate fields.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT FORBID (C-B - replicate, never correct)
//  ------------------------------------------------------------------------------------------------
//  The fused legacy spelling is PRESERVED, not abolished. AAP 0.3.4 has the contract "reconstitute the
//  fused legacy form only at the compatibility edge", so a field carrying the legacy string is
//  LEGITIMATE and is not asserted away here - a compatibility edge has to replay the exact string
//  legacy-shaped code expects, and a characterization recording keyed on the string still has to
//  match. What is forbidden is that string being the PRIMARY or the ONLY representation. Every
//  assertion below is written to that distinction: it passes for a contract carrying the decomposed
//  triple PLUS an identified compatibility field, and fails for one carrying only a fused string. An
//  over-strict "no string topic field may exist" assertion would contradict the plan, so none is made.
//
//  SCOPE (C-A - shape only)
//  ------------------------------------------------------------------------------------------------
//  Descriptor reflection and pure string arithmetic. No broker, no dispatch, no subscription
//  behaviour, and no reference to PowerFramework.Shared.Eventful - this project references only
//  PowerFramework.Contracts and PowerFramework.Shared.Kernel, so that boundary is structural rather
//  than a matter of discipline. Nothing here is a Golden-Master parity test: the legacy has no wire
//  format at all, so what is established is that the SHAPE the legacy semantics demand is present on
//  the wire, with every requirement traced to its locator (C-K).
//
//  A LIMITATION, STATED RATHER THAN QUIETLY DROPPED
//  ------------------------------------------------------------------------------------------------
//  `FieldDescriptor.Declaration` - the accessor for a field's proto leading comments - is a public
//  API and compiles, but it is NULL for every field here, because Grpc.Tools does not embed
//  SourceCodeInfo in the generated C# descriptor. The proto documentation therefore cannot be asserted
//  from the descriptor graph. The claim "`name` is the UNPREFIXED logical name" is consequently made
//  STRUCTURALLY instead of from prose, by three independent mechanical assertions, and the limitation
//  itself is asserted so that it starts failing usefully if source info is ever emitted. See
//  SubscriptionTopicContractTests.TheDescriptorGraphDoesNotSurfaceProtoComments.
// ==================================================================================================

using Google.Protobuf.Reflection;
using Xunit;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Contract-conformance suite for the decomposed event-broker subscription identity published by
/// <c>Proto/dataservices.v1.proto</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every test resolves its subject through <see cref="ContractDescriptors"/>, which searches every
/// published file's messages AND their nested messages recursively, so the suite is indifferent to
/// where the declaration was organised in the <c>.proto</c> text. Field names, numbers and types are
/// transcribed from the contract as authored rather than guessed, and each row cites the legacy
/// locator that makes it a requirement rather than a preference.
/// </para>
/// <para>
/// <b>Purity.</b> Descriptor reflection plus string arithmetic over constants transcribed from the
/// oracle. No clock, no randomness, no environment variable, no network, no file I/O and no secret
/// literal. The descriptor graph is immutable and process-wide, so every test here is safe under
/// parallel collections.
/// </para>
/// </remarks>
public sealed class SubscriptionTopicContractTests
{
    // ==============================================================================================
    //  THE CONTRACT'S OWN SPELLINGS
    //
    //  Held as constants so that a rename in the .proto surfaces as a single diagnosable lookup
    //  failure naming the message or field, rather than as a scatter of unrelated assertion failures.
    //  Every one of these was read out of Proto/dataservices.v1.proto and then verified against the
    //  generated descriptor; none is a guess.
    // ==============================================================================================

    /// <summary>The message that carries subscription identity. <c>dataservices.v1.proto</c> L356.</summary>
    private const string TopicMessageFullName = "dataservices.v1.BrokerTopic";

    /// <summary>The ordering encoding, extracted from the spelling as a number.</summary>
    private const string SequenceField = "sequence";

    /// <summary>
    /// Whether the legacy spelling actually carried a leading ordering prefix.
    /// </summary>
    /// <remarks>
    /// Load-bearing rather than decorative. The ten unprefixed topics and "0-itemchanged" ALL report a
    /// sequence of 0, so the numeric field alone cannot tell "no prefix" from "a prefix of zero" - and
    /// the difference is real, because a lexical sort places "0-itemchanged" before "clicked"
    /// [n_cst_eventful.sru:L407-L422] whereas an absent prefix makes no ordering statement at all.
    /// </remarks>
    private const string SequencePresenceField = "has_sequence_prefix";

    /// <summary>The logical identity, with the ordering prefix and the lifetime namespace removed.</summary>
    private const string LogicalNameField = "name";

    /// <summary>Classification of the logical name against the twelve framework topics.</summary>
    private const string WellKnownField = "well_known";

    /// <summary>The lifetime encoding, as a closed vocabulary.</summary>
    private const string LifetimeField = "lifetime";

    /// <summary>The namespace as arbitrary text, declared proto3 <c>optional</c>.</summary>
    private const string NamespaceField = "namespace";

    /// <summary>The nested enum that closes the lifetime vocabulary.</summary>
    private const string LifetimeEnumName = "Lifetime";

    /// <summary>The nested enum that classifies the logical name.</summary>
    private const string WellKnownEnumName = "WellKnownName";

    /// <summary>Prefix every well-known topic value carries in the contract's enum.</summary>
    private const string WellKnownValuePrefix = "EVT_";

    /// <summary>
    /// The character separating the ordering prefix from the logical name in the fused spelling.
    /// </summary>
    /// <remarks>
    /// Transcribed from se_cst_dw.sru:L54 and :L57 - "0-itemchanged" and "1-editchanged". Note it
    /// collides with <c>SYMBOL_PREPEND</c> ("-") of the broker's own parser
    /// [n_cst_eventful.sru:L91], which consumes a LEADING "-" as "prepend within equal priority"
    /// [:L342-L344]. In these two topics the "-" is interior rather than leading, so the parser never
    /// sees it as a symbol - which is precisely why the digits survive into the registry and end up
    /// governing the lexical sort.
    /// </remarks>
    private const char SequencePrefixSeparator = '-';

    /// <summary>The lifetime the framework's bulk unsubscribe SPARES. [n_cst_threading.sru:L544,L596]</summary>
    private const string PersistentLifetimeValue = "LIFETIME_PERSISTENT";

    /// <summary>The legacy default lifetime, swept away by the bulk unsubscribe.</summary>
    private const string TransientLifetimeValue = "LIFETIME_TRANSIENT";

    // ==============================================================================================
    //  ROW TABLES
    //
    //  One row per field and per negative assertion, per the table-driven discipline AAP 0.6.7 asks
    //  for. Each row carries its own oracle locator and the reason the requirement exists, and both
    //  travel into the failure message so a red test reads as an explanation rather than as a diff.
    // ==============================================================================================

    /// <summary>
    /// The three decomposed encodings plus the presence companion that makes the ordering encoding
    /// honest, with the type and field number the contract declares for each.
    /// </summary>
    private static readonly (string FieldName, FieldType DeclaredType, int FieldNumber, string Locator, string Why)[]
        DecomposedFields =
    [
        (SequenceField, FieldType.Int32, 1, "se_cst_dw.sru:L54,L57",
            "the leading digit of \"0-itemchanged\" and \"1-editchanged\" is dispatch order, and a number "
            + "is what a consumer can sort on without re-parsing the spelling"),
        (SequencePresenceField, FieldType.Bool, 2, "se_cst_dw.sru:L47-L76",
            "ten of the twelve topics carry no prefix at all, which is not the same statement as a prefix "
            + "of zero"),
        (LogicalNameField, FieldType.String, 3, "se_cst_dw.sru:L54",
            "\"itemchanged\" is the authoritative identity; the \"0-\" prefix and any \".^persistent\" "
            + "suffix are stripped out of it"),
        (LifetimeField, FieldType.Enum, 5, "n_cst_threading.sru:L544,L596",
            "\".^persistent\" is a trailing NAMESPACE fused into the same string, and its vocabulary is "
            + "closed"),
    ];

    /// <summary>
    /// The negative half: the two encodings whose stringly-typed form would re-fuse the grammar this
    /// contract exists to take apart.
    /// </summary>
    private static readonly (string FieldName, FieldType ForbiddenType, string Why)[] FusedFormRegressions =
    [
        (SequenceField, FieldType.String,
            "a string ordering prefix leaves the dispatch order embedded in text, exactly where the "
            + "legacy left it - and the broker's lexical insertion scan [n_cst_eventful.sru:L407-L422] "
            + "is then invisible to every consumer"),
        (LifetimeField, FieldType.String,
            "a string lifetime re-opens the vocabulary the enum closes, and the legacy suffix carries a "
            + "negation [n_cst_eventful.sru:L1012-L1015] that a bare string silently swallows"),
    ];

    /// <summary>The closed lifetime vocabulary, value for value.</summary>
    private static readonly (string ValueName, int Number, string Locator, string Why)[] LifetimeValues =
    [
        (TransientLifetimeValue, 0, "n_cst_threading.sru:L593-L597",
            "no lifetime namespace, or any namespace other than \"persistent\" - the LEGACY DEFAULT, "
            + "which is why it takes the proto3 zero"),
        (PersistentLifetimeValue, 1, "n_cst_threading.sru:L544,L596",
            "the namespace is ordinally \"persistent\", so the framework's bulk unsubscribe SPARES it"),
    ];

    /// <summary>
    /// The two topics whose legacy spelling fuses an ordering prefix into the name, with the ordinal
    /// and the unprefixed logical name the decomposition must yield.
    /// </summary>
    private static readonly (string FusedSpelling, int Sequence, string LogicalName, string WellKnownValue, string Locator)[]
        PrefixedTopics =
    [
        ("0-itemchanged", 0, "itemchanged", "EVT_ITEMCHANGED", "se_cst_dw.sru:L54"),
        ("1-editchanged", 1, "editchanged", "EVT_EDITCHANGED", "se_cst_dw.sru:L57"),
    ];

    /// <summary>
    /// The ten topics that carry no ordering prefix, which is the whole reason a presence flag has to
    /// travel beside the ordinal.
    /// </summary>
    private static readonly (string FusedSpelling, string WellKnownValue, string Locator)[] UnprefixedTopics =
    [
        ("rowfocuschanging", "EVT_ROWFOCUSCHANGING", "se_cst_dw.sru:L47"),
        ("rowfocuschanged", "EVT_ROWFOCUSCHANGED", "se_cst_dw.sru:L49"),
        ("itemfocuschanged", "EVT_ITEMFOCUSCHANGED", "se_cst_dw.sru:L52"),
        ("clicked", "EVT_CLICKED", "se_cst_dw.sru:L60"),
        ("doubleclicked", "EVT_DOUBLECLICKED", "se_cst_dw.sru:L63"),
        ("lbuttonup", "EVT_LBUTTONUP", "se_cst_dw.sru:L66"),
        ("rbuttondown", "EVT_RBUTTONDOWN", "se_cst_dw.sru:L69"),
        ("rbuttonup", "EVT_RBUTTONUP", "se_cst_dw.sru:L72"),
        ("getfocus", "EVT_GETFOCUS", "se_cst_dw.sru:L74"),
        ("losefocus", "EVT_LOSEFOCUS", "se_cst_dw.sru:L76"),
    ];

    /// <summary>
    /// Every unordered pair drawn from the decomposed triple. One row per pair, because "not in a
    /// <c>oneof</c> together" is a property of a PAIR and asserting it over the whole set at once
    /// would report the failure without naming which two collided.
    /// </summary>
    private static readonly (string First, string Second)[] DecomposedFieldPairs =
    [
        (SequenceField, LogicalNameField),
        (LogicalNameField, LifetimeField),
        (SequenceField, LifetimeField),
    ];

    /// <summary>
    /// Every protobuf scalar an ordinal may legitimately be declared as. Anything outside this set
    /// leaves the ordering unsortable.
    /// </summary>
    /// <remarks>
    /// Deliberately broader than the single type the contract happens to declare today. The
    /// requirement is that the ordering travel as a NUMBER; widening <c>int32</c> to <c>int64</c> would
    /// satisfy it and must not be reported as a regression, while turning it into a string must be.
    /// </remarks>
    private static readonly FieldType[] IntegralFieldTypes =
    [
        FieldType.Int32,
        FieldType.Int64,
        FieldType.SInt32,
        FieldType.SInt64,
        FieldType.UInt32,
        FieldType.UInt64,
        FieldType.Fixed32,
        FieldType.Fixed64,
        FieldType.SFixed32,
        FieldType.SFixed64,
    ];

    /// <summary>
    /// The string-typed fields of the topic message that are DECOMPOSED PARTS rather than fused forms.
    /// </summary>
    /// <remarks>
    /// <c>name</c> is the logical identity with the prefix and the namespace removed; <c>namespace</c>
    /// is the namespace on its own, with the <c>^</c> negation split off into its own boolean
    /// [n_cst_eventful.sru:L1012-L1015]. Neither carries the fused spelling, so neither has to be
    /// marked as a compatibility representation.
    /// </remarks>
    private static readonly string[] DecomposedStringParts = [LogicalNameField, NamespaceField];

    /// <summary>
    /// Substrings that make a field's name READ as a compatibility representation of the fused legacy
    /// spelling.
    /// </summary>
    /// <remarks>
    /// The list is intentionally a vocabulary rather than an exact name. AAP 0.3.4 permits the fused
    /// form to travel at the compatibility edge; what it must never be is INDISTINGUISHABLE from the
    /// decomposed identity a consumer switches on. Naming is the only mechanism a descriptor offers
    /// for that distinction, since the proto comments are not observable - see the file header.
    /// </remarks>
    private static readonly string[] CompatibilityNameMarkers = ["legacy", "compat", "fused", "raw"];

    /// <summary>
    /// Substrings that mark a field as carrying subscription identity, used by the boundary-wide
    /// sweeps.
    /// </summary>
    private static readonly string[] TopicBearingNameMarkers = ["topic", "subscription"];

    // ==============================================================================================
    //  RESOLUTION HELPERS
    //
    //  Everything goes through ContractDescriptors so that a lookup failure names the message or field
    //  it could not find and lists the candidates, which is the difference between "the contract
    //  changed" and "a test broke".
    // ==============================================================================================

    /// <summary>The subscription-identity message, resolved from the published descriptor graph.</summary>
    /// <remarks>
    /// Addressed by FULL name rather than by simple name. <see cref="ContractDescriptors.RequireMessage"/>
    /// also accepts a simple name and a dot-boundary suffix, but the fully-qualified form is the only
    /// spelling that cannot become ambiguous if a same-named message is later declared in another
    /// package - and simple-name collisions genuinely exist in this contract set.
    /// </remarks>
    private static MessageDescriptor TopicMessage => ContractDescriptors.RequireMessage(TopicMessageFullName);

    /// <summary>Resolves a field of the subscription-identity message by its proto spelling.</summary>
    /// <param name="fieldName">The <c>snake_case</c> name as authored, compared ordinally.</param>
    private static FieldDescriptor TopicField(string fieldName) =>
        ContractDescriptors.RequireField(TopicMessage, fieldName);

    /// <summary>Resolves a nested enum of the subscription-identity message by its proto spelling.</summary>
    /// <param name="enumName">The nested enum's simple name, compared ordinally.</param>
    /// <remarks>
    /// Searched among the message's OWN nested declarations rather than through the file-wide lookup,
    /// because co-location with the message is itself part of what makes the vocabulary closed: a
    /// consumer reading the message sees the whole alphabet without following a reference.
    /// </remarks>
    private static EnumDescriptor TopicEnum(string enumName)
    {
        EnumDescriptor? nested = TopicMessage.EnumTypes
            .FirstOrDefault(candidate => string.Equals(candidate.Name, enumName, StringComparison.Ordinal));

        Assert.True(
            nested is not null,
            $"Message '{TopicMessageFullName}' declares no nested enum '{enumName}'. It declares: "
                + $"{string.Join(", ", TopicMessage.EnumTypes.Select(static candidate => candidate.Name))}.");

        return nested!;
    }

    /// <summary>
    /// Whether <paramref name="fieldName"/> reads as carrying subscription identity.
    /// </summary>
    /// <param name="fieldName">A field's proto name.</param>
    /// <remarks>
    /// Matched case-insensitively against <see cref="TopicBearingNameMarkers"/> so that
    /// <c>topic</c>, <c>broker_topic</c> and <c>subscription_topic</c> are all caught. Naming is the
    /// only handle a sweep has on intent, and a field that means "topic" and is not named for it is a
    /// readability problem this suite cannot and does not try to police.
    /// </remarks>
    private static bool IsTopicBearingName(string fieldName) =>
        TopicBearingNameMarkers.Any(marker => fieldName.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether <paramref name="fieldName"/> identifies itself as a compatibility representation.
    /// </summary>
    /// <param name="fieldName">A field's proto name.</param>
    private static bool IsCompatibilityName(string fieldName) =>
        CompatibilityNameMarkers.Any(marker => fieldName.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether <paramref name="field"/> carries the decomposed topic message itself.</summary>
    /// <param name="field">Any field of any published message.</param>
    private static bool CarriesDecomposedTopic(FieldDescriptor field) =>
        field.FieldType == FieldType.Message
        && field.MessageType is not null
        && string.Equals(field.MessageType.FullName, TopicMessageFullName, StringComparison.Ordinal);

    /// <summary>
    /// Whether a field declared as <paramref name="fieldType"/> can represent the integer
    /// <paramref name="value"/> without loss.
    /// </summary>
    /// <param name="fieldType">The type the contract declares for the field.</param>
    /// <param name="value">The ordinal the oracle evidences.</param>
    /// <remarks>
    /// The <c>_ =&gt; false</c> arm is the load-bearing one: a non-numeric declaration cannot represent
    /// an ordinal AS an ordinal at all, which is exactly the regression the ordinal rows guard against.
    /// This is not a tautology over the declared type - it is evaluated against whatever the contract
    /// currently declares.
    /// </remarks>
    private static bool CanRepresent(FieldType fieldType, long value) => fieldType switch
    {
        FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => value is >= int.MinValue and <= int.MaxValue,
        FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 => true,
        FieldType.UInt32 or FieldType.Fixed32 => value is >= 0 and <= uint.MaxValue,
        FieldType.UInt64 or FieldType.Fixed64 => value >= 0,
        _ => false,
    };

    /// <summary>
    /// Every message in the published boundary, other than the subscription-identity message itself,
    /// that declares at least one field reading as subscription identity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Walks <see cref="ContractDescriptors.AllMessages()"/>, which covers all three published files
    /// and recurses into nested messages, so a topic carried by a message nested inside an event body
    /// is seen too. Synthetic <c>map</c> entry messages are included by that enumerator and are simply
    /// never topic-bearing, so no filter is needed for them.
    /// </para>
    /// <para>
    /// Today this yields exactly one message - <c>dataservices.v1.EventResult</c>, whose <c>topic</c>
    /// field is the broker edge's topic on the event chain. It is computed rather than hard-coded so
    /// that a topic added to a second message is swept automatically instead of silently escaping.
    /// </para>
    /// </remarks>
    private static IEnumerable<MessageDescriptor> TopicBearingMessages() =>
        ContractDescriptors.AllMessages()
            .Where(static message =>
                !string.Equals(message.FullName, TopicMessageFullName, StringComparison.Ordinal)
                && message.Fields.InDeclarationOrder().Any(static field =>
                    IsTopicBearingName(field.Name) || CarriesDecomposedTopic(field)));

    // ==============================================================================================
    //  MEMBER DATA PROVIDERS
    //
    //  Each projects one table above into a strongly typed TheoryData so the compiler checks each row
    //  against the theory's parameter list rather than deferring a mismatch to a run-time cast. Every
    //  row carries only serializable values - strings, ints and protobuf field-type enum members - so
    //  a single failing row can be re-run on its own.
    // ==============================================================================================

    /// <summary>Field name, declared type, field number, locator and rationale.</summary>
    /// <returns>Four rows: the decomposed triple plus the ordering-presence companion.</returns>
    public static TheoryData<string, FieldType, int, string, string> DecomposedFieldRows()
    {
        TheoryData<string, FieldType, int, string, string> rows = [];
        foreach ((string fieldName, FieldType declaredType, int fieldNumber, string locator, string why) in DecomposedFields)
        {
            rows.Add(fieldName, declaredType, fieldNumber, locator, why);
        }

        return rows;
    }

    /// <summary>Field name, the type it must NOT be declared as, and why.</summary>
    /// <returns>Two rows: the ordering and lifetime encodings.</returns>
    public static TheoryData<string, FieldType, string> FusedFormRegressionRows()
    {
        TheoryData<string, FieldType, string> rows = [];
        foreach ((string fieldName, FieldType forbiddenType, string why) in FusedFormRegressions)
        {
            rows.Add(fieldName, forbiddenType, why);
        }

        return rows;
    }

    /// <summary>Enum value name, number, locator and rationale.</summary>
    /// <returns>Two rows, in the contract's declaration order. [n_cst_threading.sru:L544,L596]</returns>
    public static TheoryData<string, int, string, string> LifetimeValueRows()
    {
        TheoryData<string, int, string, string> rows = [];
        foreach ((string valueName, int number, string locator, string why) in LifetimeValues)
        {
            rows.Add(valueName, number, locator, why);
        }

        return rows;
    }

    /// <summary>An unordered pair of decomposed field names.</summary>
    /// <returns>Three rows, one per pair drawn from the triple.</returns>
    public static TheoryData<string, string> DecomposedFieldPairRows()
    {
        TheoryData<string, string> rows = [];
        foreach ((string first, string second) in DecomposedFieldPairs)
        {
            rows.Add(first, second);
        }

        return rows;
    }

    /// <summary>Fused spelling, ordinal, unprefixed logical name, classifier value and locator.</summary>
    /// <returns>Two rows: the only two topics whose spelling carries an ordering prefix.</returns>
    public static TheoryData<string, int, string, string, string> PrefixedTopicRows()
    {
        TheoryData<string, int, string, string, string> rows = [];
        foreach ((string spelling, int sequence, string logicalName, string wellKnown, string locator) in PrefixedTopics)
        {
            rows.Add(spelling, sequence, logicalName, wellKnown, locator);
        }

        return rows;
    }

    /// <summary>Fused spelling, classifier value and locator.</summary>
    /// <returns>Ten rows: every topic that carries no ordering prefix. [se_cst_dw.sru:L47-L76]</returns>
    public static TheoryData<string, string, string> UnprefixedTopicRows()
    {
        TheoryData<string, string, string> rows = [];
        foreach ((string spelling, string wellKnown, string locator) in UnprefixedTopics)
        {
            rows.Add(spelling, wellKnown, locator);
        }

        return rows;
    }

    /// <summary>The full name of each message that carries subscription identity.</summary>
    /// <returns>
    /// One row per topic-bearing message other than the identity message itself; one row today.
    /// </returns>
    public static TheoryData<string> TopicBearingMessageRows()
    {
        TheoryData<string> rows = [];
        foreach (MessageDescriptor message in TopicBearingMessages())
        {
            rows.Add(message.FullName);
        }

        return rows;
    }

    /// <summary>The name of each string-typed field the subscription-identity message declares.</summary>
    /// <returns>
    /// One row per string field; three today - the logical name, the namespace and the fused
    /// compatibility spelling.
    /// </returns>
    public static TheoryData<string> TopicMessageStringFieldRows()
    {
        TheoryData<string> rows = [];
        foreach (FieldDescriptor field in TopicMessage.Fields.InDeclarationOrder())
        {
            if (field.FieldType == FieldType.String)
            {
                rows.Add(field.Name);
            }
        }

        return rows;
    }

    /// <summary>Resolves an enum value by its proto spelling, failing diagnosably when absent.</summary>
    /// <param name="vocabulary">The enum to search.</param>
    /// <param name="valueName">The SCREAMING_SNAKE spelling as authored, compared ordinally.</param>
    /// <remarks>
    /// The proto spelling is used rather than the generated CLR member, because protoc PascalCases the
    /// C# projection - <c>LIFETIME_PERSISTENT</c> becomes <c>Persistent</c> - and AAP 0.4.5.3 requires
    /// the legacy spellings to survive verbatim into serialized payloads, log records and
    /// characterization recordings. Only the descriptor can confirm the original.
    /// </remarks>
    private static EnumValueDescriptor RequireEnumValue(EnumDescriptor vocabulary, string valueName)
    {
        EnumValueDescriptor? value = vocabulary.Values
            .FirstOrDefault(candidate => string.Equals(candidate.Name, valueName, StringComparison.Ordinal));

        Assert.True(
            value is not null,
            $"Enum '{vocabulary.FullName}' declares no value '{valueName}'. It declares: "
                + $"{string.Join(", ", vocabulary.Values.Select(static candidate => candidate.Name))}.");

        return value!;
    }

    // ==============================================================================================
    //  PHASE 1 - THE THREE DECOMPOSED ENCODINGS EXIST, AS THREE SEPARATELY TYPED FIELDS
    // ==============================================================================================

    /// <summary>
    /// The subscription-identity message is published by the DataServices contract and is reachable
    /// through the descriptor graph, nested declarations included.
    /// </summary>
    /// <remarks>
    /// The anchor for every other test in the file. It is resolved through
    /// <see cref="ContractDescriptors"/> rather than from the generated CLR type so that the assertion
    /// is about the CONTRACT - the artifact both ends of a call agree on - rather than about one
    /// language's projection of it. Membership in
    /// <see cref="ContractDescriptors.AllMessages()"/> is asserted explicitly because that enumerator
    /// is what recurses nested types, and a topic declared inside another message must still be found.
    /// </remarks>
    [Fact]
    public void TheSubscriptionIdentityMessageIsPublishedInTheDataServicesContract()
    {
        MessageDescriptor message = TopicMessage;

        Assert.Equal(TopicMessageFullName, message.FullName);
        Assert.Equal(ContractDescriptors.DataServices.Name, message.File.Name);
        Assert.Equal(ContractDescriptors.DataServices.Package, message.File.Package);

        // NOT A SYNTHETIC MAP ENTRY. `AllMessages()` deliberately surfaces the `...Entry` messages
        // protoc synthesises for a `map<K,V>` field rather than filtering them out, so a test that
        // means "a message as AUTHORED" says so itself.
        Assert.False(
            message.IsMapEntry,
            $"'{TopicMessageFullName}' resolved to a synthetic map-entry message rather than to the "
                + "authored subscription-identity message.");

        Assert.Contains(message, ContractDescriptors.AllMessages());

        // Three encodings cannot live on fewer than three fields.
        FieldDescriptor[] fields = message.Fields.InDeclarationOrder().ToArray();
        Assert.True(
            fields.Length >= DecomposedFields.Length,
            $"'{TopicMessageFullName}' declares {fields.Length} field(s); the decomposition needs at "
                + $"least {DecomposedFields.Length}. Declared: "
                + $"{string.Join(", ", fields.Select(static field => field.Name))}.");
    }

    /// <summary>
    /// Each decomposed encoding is declared with the type and the field number its legacy evidence
    /// requires.
    /// </summary>
    /// <param name="fieldName">The field's proto spelling, transcribed from the contract.</param>
    /// <param name="declaredType">The protobuf type the encoding requires.</param>
    /// <param name="fieldNumber">The wire number the contract assigns it.</param>
    /// <param name="locator">The oracle locator that makes the row a requirement.</param>
    /// <param name="why">Why that type, in the oracle's terms.</param>
    /// <remarks>
    /// <para>
    /// The TYPE is the substance of the row, not the name. A contract could declare three fields called
    /// <c>sequence</c>, <c>name</c> and <c>lifetime</c> and still have fused the grammar back together
    /// by making all three strings - so asserting names alone would pass for exactly the shape this
    /// suite exists to reject.
    /// </para>
    /// <para>
    /// The field NUMBER is asserted because it is the wire identity: renumbering is a breaking change
    /// for every already-deployed consumer, and it is the one property of a field that cannot be
    /// recovered from a payload once it has been changed.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DecomposedFieldRows))]
    public void EachDecomposedEncodingIsDeclaredWithTheTypeAndNumberItsEvidenceRequires(
        string fieldName,
        FieldType declaredType,
        int fieldNumber,
        string locator,
        string why)
    {
        FieldDescriptor field = TopicField(fieldName);

        Assert.True(
            field.FieldType == declaredType,
            $"'{TopicMessageFullName}.{fieldName}' is declared {field.FieldType} but the evidence at "
                + $"[{locator}] requires {declaredType}: {why}");

        Assert.True(
            field.FieldNumber == fieldNumber,
            $"'{TopicMessageFullName}.{fieldName}' carries wire number {field.FieldNumber}; the "
                + $"published contract assigns it {fieldNumber}. Renumbering breaks every deployed "
                + $"consumer. [{locator}]");

        // A SINGLE-VALUED ENCODING. One subscription has one ordering position, one logical name and
        // one lifetime; a repeated or map-shaped field would say otherwise.
        Assert.False(field.IsRepeated, $"'{TopicMessageFullName}.{fieldName}' must not be repeated.");
        Assert.False(field.IsMap, $"'{TopicMessageFullName}.{fieldName}' must not be a map.");
    }

    /// <summary>
    /// The ordering encoding travels as a NUMBER, so a consumer can sort on it without re-parsing the
    /// legacy spelling. [se_cst_dw.sru:L54,L57; n_cst_eventful.sru:L407-L422]
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the assertion that makes the ordering visible. The legacy prefix IS an ordinal - the
    /// broker's insertion scan compares subscription NAMES lexically
    /// (<c>Events[nIndex].name &gt; newEvent.name</c>) and ASCII digits sort before letters, so the
    /// leading "0" and "1" of "0-itemchanged" and "1-editchanged" are dispatch order expressed as text.
    /// Carry that as a string on the wire and the ordering is still hidden inside a spelling with an
    /// undocumented grammar; carry it as a number and it becomes data.
    /// </para>
    /// <para>
    /// The accepted set is every protobuf integral type rather than the single one the contract
    /// declares today, so widening <c>int32</c> to <c>int64</c> is not reported as a regression while
    /// turning it into text is.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOrderingEncodingTravelsAsANumberSoItIsSortableWithoutReparsingTheSpelling()
    {
        FieldDescriptor sequence = TopicField(SequenceField);

        Assert.Contains(sequence.FieldType, IntegralFieldTypes);

        Assert.True(
            sequence.FieldType != FieldType.String,
            $"'{TopicMessageFullName}.{SequenceField}' is declared as a string, which leaves the "
                + "dispatch order embedded in text exactly where the legacy left it "
                + "[n_cst_eventful.sru:L407-L422].");

        // BOTH EVIDENCED ORDINALS ARE REPRESENTABLE IN THE DECLARED TYPE. Zero is the interesting one:
        // it is the item-changed topic's real ordering position [se_cst_dw.sru:L54], and it is also the
        // proto3 default, which is why the presence companion has to travel beside it.
        foreach ((_, int ordinal, _, _, _) in PrefixedTopics)
        {
            Assert.True(
                CanRepresent(sequence.FieldType, ordinal),
                $"'{TopicMessageFullName}.{SequenceField}' is declared {sequence.FieldType}, which "
                    + $"cannot represent the evidenced ordinal {ordinal}.");
        }

        // The companion that distinguishes "no prefix" from "a prefix of zero".
        FieldDescriptor presence = TopicField(SequencePresenceField);
        Assert.Equal(FieldType.Bool, presence.FieldType);
    }

    /// <summary>
    /// No decomposed encoding is allowed to collapse back into a string, which would re-fuse the
    /// grammar this contract exists to take apart.
    /// </summary>
    /// <param name="fieldName">The field the row guards.</param>
    /// <param name="forbiddenType">The type that would re-fuse the encoding.</param>
    /// <param name="why">What is lost when it does, in the oracle's terms.</param>
    /// <remarks>
    /// The negative half of the type assertions, kept as its own theory so that a failure reads as
    /// "this encoding was re-fused" rather than as "a type changed". These two rows are the encodings
    /// whose stringly-typed form is genuinely tempting, because the legacy carried both AS text.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FusedFormRegressionRows))]
    public void NoDecomposedEncodingIsAllowedToCollapseBackIntoAString(
        string fieldName,
        FieldType forbiddenType,
        string why)
    {
        FieldDescriptor field = TopicField(fieldName);

        Assert.True(
            field.FieldType != forbiddenType,
            $"'{TopicMessageFullName}.{fieldName}' is declared {forbiddenType}, which re-fuses the "
                + $"encoding: {why}");
    }

    /// <summary>
    /// The lifetime encoding is a CLOSED vocabulary - a nested enum - rather than free-form text.
    /// [n_cst_threading.sru:L544,L596]
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy lifetime is the trailing namespace <c>.^persistent</c> appended to the subscription
    /// string. As text it is open: any spelling at all is accepted and only the framework's own
    /// convention gives "persistent" its meaning. An enum is what makes the vocabulary closed, so a
    /// consumer switching on lifetime has a total set of cases rather than a string it must guess at.
    /// </para>
    /// <para>
    /// The enum is required to be NESTED IN the identity message. Co-location is part of what makes the
    /// vocabulary legible: a consumer reading the message sees the whole alphabet without following a
    /// reference to a file-level declaration that may be shared with something unrelated.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLifetimeEncodingIsAClosedNestedEnumRatherThanAFreeFormString()
    {
        FieldDescriptor lifetime = TopicField(LifetimeField);

        Assert.Equal(FieldType.Enum, lifetime.FieldType);

        EnumDescriptor vocabulary = TopicEnum(LifetimeEnumName);

        Assert.True(
            lifetime.EnumType is { } bound
                && string.Equals(bound.FullName, vocabulary.FullName, StringComparison.Ordinal),
            $"'{TopicMessageFullName}.{LifetimeField}' is not bound to the nested "
                + $"'{LifetimeEnumName}' vocabulary.");

        Assert.True(
            vocabulary.ContainingType is { } owner
                && string.Equals(owner.FullName, TopicMessageFullName, StringComparison.Ordinal),
            $"Enum '{vocabulary.FullName}' is not nested inside '{TopicMessageFullName}'.");

        Assert.True(
            vocabulary.Values.Count >= LifetimeValues.Length,
            $"Enum '{vocabulary.FullName}' declares {vocabulary.Values.Count} member(s); the evidenced "
                + $"vocabulary needs at least {LifetimeValues.Length}.");
    }

    /// <summary>
    /// The lifetime vocabulary declares each evidenced member, with the number the contract assigns it,
    /// exactly once in the whole published boundary.
    /// </summary>
    /// <param name="valueName">The member's SCREAMING_SNAKE proto spelling.</param>
    /// <param name="number">The number the contract assigns it.</param>
    /// <param name="locator">The oracle locator evidencing the member.</param>
    /// <param name="why">What the member means, in the oracle's terms.</param>
    /// <remarks>
    /// <para>
    /// The persistent row is the one the folder requirement names: the lifetime evidenced at
    /// <c>n_cst_threading.sru:L544</c>, where <c>of_Off(".^persistent")</c> is passed with no logical
    /// name at all, and at <c>:L596</c>, where the same suffix is appended to a named subscription -
    /// but only when the caller supplied no namespace of their own, per the guard at <c>:L594</c>.
    /// </para>
    /// <para>
    /// UNIQUENESS ACROSS THE BOUNDARY is asserted through
    /// <see cref="ContractDescriptors.FindEnumValuesNamed"/>, which returns every declaration site
    /// rather than a first match. A lifetime member declared in two enums with two different numbers
    /// would be a silent divergence of exactly the kind AAP 0.6.1 warns about - each side internally
    /// consistent, the two disagreeing with each other - and only the full set of sites can express it.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(LifetimeValueRows))]
    public void TheLifetimeVocabularyDeclaresEachEvidencedMemberWithItsNumber(
        string valueName,
        int number,
        string locator,
        string why)
    {
        IReadOnlyList<EnumValueDescriptor> sites = ContractDescriptors.FindEnumValuesNamed(valueName);

        Assert.True(
            sites.Count == 1,
            $"Lifetime member '{valueName}' has {sites.Count} declaration site(s) in the published "
                + $"boundary and must have exactly one. Sites: "
                + $"{string.Join(", ", sites.Select(static site => site.FullName))}. [{locator}] {why}");

        EnumDescriptor vocabulary = TopicEnum(LifetimeEnumName);
        EnumValueDescriptor declared = RequireEnumValue(vocabulary, valueName);

        Assert.Same(sites[0], declared);
        Assert.Equal(number, declared.Number);
    }

    /// <summary>
    /// Persistent is NOT the protobuf default, because the legacy default lifetime is transient.
    /// [n_cst_threading.sru:L593-L597]
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which member takes zero is a behavioural statement, not a numbering detail. proto3 omits a
    /// default-valued field from the encoding entirely, so the zero member is what a consumer sees when
    /// the producer said nothing - and the legacy's silence means TRANSIENT: a subscription registered
    /// with no namespace is swept away by the framework's bulk unsubscribe. Numbering persistent as zero
    /// would make every unset payload claim the subscription survives, which is the exact inverse.
    /// </para>
    /// <para>
    /// This is also why the vocabulary carries no synthetic "unspecified" member: the legacy has no such
    /// state, and inventing one would displace a real legacy value from zero.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePersistentLifetimeIsNotTheProtoDefaultBecauseTheLegacyDefaultIsTransient()
    {
        EnumDescriptor vocabulary = TopicEnum(LifetimeEnumName);

        EnumValueDescriptor persistent = RequireEnumValue(vocabulary, PersistentLifetimeValue);
        EnumValueDescriptor transient = RequireEnumValue(vocabulary, TransientLifetimeValue);

        Assert.True(
            persistent.Number != 0,
            $"'{PersistentLifetimeValue}' is numbered 0, so an unset lifetime would claim the "
                + "subscription survives the bulk unsubscribe. The legacy default is the opposite "
                + "[n_cst_threading.sru:L593-L597].");

        Assert.Equal(0, transient.Number);
    }

    /// <summary>
    /// The three encodings are three DISTINCT fields with three distinct wire numbers - not three views
    /// of one value.
    /// </summary>
    /// <remarks>
    /// The whole decomposition rests on this. Three aliases onto one field would satisfy every
    /// name-and-type assertion above while still carrying one fused value - which is exactly the legacy
    /// shape, where one string held ordering [se_cst_dw.sru:L54,L57], identity [:L47-L76] and lifetime
    /// [n_cst_threading.sru:L596] at the same time. Identity, spelling and wire number are therefore all
    /// checked for distinctness rather than just presence.
    /// </remarks>
    [Fact]
    public void TheThreeDecomposedEncodingsAreThreeDistinctFieldsWithThreeDistinctNumbers()
    {
        FieldDescriptor sequence = TopicField(SequenceField);
        FieldDescriptor logical = TopicField(LogicalNameField);
        FieldDescriptor lifetime = TopicField(LifetimeField);

        Assert.NotSame(sequence, logical);
        Assert.NotSame(logical, lifetime);
        Assert.NotSame(sequence, lifetime);

        int[] numbers = [sequence.FieldNumber, logical.FieldNumber, lifetime.FieldNumber];
        Assert.Equal(3, numbers.Distinct().Count());

        string[] names = [sequence.Name, logical.Name, lifetime.Name];
        Assert.Equal(3, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// No two decomposed encodings share a <c>oneof</c>, because a <c>oneof</c> would make them
    /// mutually exclusive and so defeat the decomposition entirely.
    /// </summary>
    /// <param name="firstFieldName">One decomposed field.</param>
    /// <param name="secondFieldName">Another decomposed field.</param>
    /// <remarks>
    /// <para>
    /// A <c>oneof</c> permits at most one of its members to be set. Placing any two of ordering, name
    /// and lifetime in one would mean a topic could report its position OR its identity but never both -
    /// which is strictly worse than the fused string, since the fused string at least carried all three
    /// at once: <c>"0-itemchanged"</c> [se_cst_dw.sru:L54] with <c>".^persistent"</c> appended
    /// [n_cst_threading.sru:L596] carries ordering, identity AND lifetime simultaneously.
    /// </para>
    /// <para>
    /// A SYNTHETIC oneof is explicitly tolerated and must not be confused with an authored one. proto3
    /// <c>optional</c> makes protoc synthesise a single-member oneof whose only purpose is to carry
    /// explicit presence; it constrains nothing. The assertion therefore rejects only a SHARED oneof and
    /// an AUTHORED one - see
    /// <see cref="TheOnlyOneofTouchingTheIdentityMessageIsTheSyntheticKindProtoThreeOptionalCreates"/>.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DecomposedFieldPairRows))]
    public void NoTwoDecomposedEncodingsShareAOneof(string firstFieldName, string secondFieldName)
    {
        FieldDescriptor first = TopicField(firstFieldName);
        FieldDescriptor second = TopicField(secondFieldName);

        Assert.True(
            first.FieldNumber != second.FieldNumber,
            $"'{firstFieldName}' and '{secondFieldName}' share wire number {first.FieldNumber}.");

        OneofDescriptor? firstOneof = first.ContainingOneof;
        OneofDescriptor? secondOneof = second.ContainingOneof;

        bool shareAOneof = firstOneof is not null
            && secondOneof is not null
            && string.Equals(firstOneof.FullName, secondOneof.FullName, StringComparison.Ordinal);

        Assert.False(
            shareAOneof,
            $"'{firstFieldName}' and '{secondFieldName}' both belong to oneof "
                + $"'{firstOneof?.FullName}', so at most one of them can ever be set. The three "
                + "encodings are independent and must all travel together.");

        foreach (OneofDescriptor? containing in new[] { firstOneof, secondOneof })
        {
            if (containing is null)
            {
                continue;
            }

            Assert.True(
                containing.IsSynthetic,
                $"A decomposed encoding belongs to the AUTHORED oneof '{containing.FullName}'. Only "
                    + "the synthetic single-member oneof that proto3 `optional` creates is permitted.");
        }
    }

    /// <summary>
    /// The only <c>oneof</c> touching the identity message is the synthetic single-member kind proto3
    /// <c>optional</c> creates, and it constrains nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recorded as its own test because the synthetic oneof is a trap for a reader tidying the
    /// assertions above: <c>optional string namespace</c> genuinely does appear in
    /// <see cref="MessageDescriptor.Oneofs"/>, under the field name with a leading underscore, and an
    /// assertion phrased as "no field is in a oneof" would fail on it for entirely the wrong reason.
    /// </para>
    /// <para>
    /// The namespace field is <c>optional</c> deliberately, and the presence tracking is behavioural:
    /// ABSENT AND EMPTY ARE DIFFERENT STATES. In the filter grammar a string carrying no <c>.</c> at all
    /// sets <c>bNoNamespace</c> and the namespace test is skipped entirely, so the filter matches every
    /// namespace [n_cst_eventful.sru:L1000-L1006], whereas the subscribe grammar rejects an EMPTY
    /// namespace after the dot outright [:L379]. Collapsing absent into empty would silently change
    /// which subscriptions a filter matches.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOnlyOneofTouchingTheIdentityMessageIsTheSyntheticKindProtoThreeOptionalCreates()
    {
        MessageDescriptor message = TopicMessage;
        string[] decomposedTriple = [SequenceField, LogicalNameField, LifetimeField];

        foreach (OneofDescriptor oneof in message.Oneofs)
        {
            string[] members = oneof.Fields.Select(static field => field.Name).ToArray();

            if (oneof.IsSynthetic)
            {
                Assert.True(
                    members.Length == 1,
                    $"Synthetic oneof '{oneof.FullName}' has {members.Length} members; proto3 "
                        + "`optional` synthesises exactly one.");
                continue;
            }

            foreach (string member in members)
            {
                Assert.False(
                    decomposedTriple.Contains(member, StringComparer.Ordinal),
                    $"Authored oneof '{oneof.FullName}' contains decomposed encoding '{member}', "
                        + "making it mutually exclusive with its siblings.");
            }
        }

        // The namespace field's presence tracking is the reason the synthetic oneof exists at all.
        FieldDescriptor namespaceField = TopicField(NamespaceField);
        Assert.True(
            namespaceField.HasPresence,
            $"'{TopicMessageFullName}.{NamespaceField}' must track explicit presence: an ABSENT "
                + "namespace criterion matches every namespace [n_cst_eventful.sru:L1000-L1006] while "
                + "an EMPTY one is rejected outright [:L379].");

        if (namespaceField.ContainingOneof is { } namespacePresence)
        {
            Assert.True(
                namespacePresence.IsSynthetic,
                $"'{NamespaceField}' belongs to the authored oneof '{namespacePresence.FullName}'.");
        }
    }

    // ==============================================================================================
    //  PHASE 2 - THE FUSED STRING IS NOT THE PRIMARY REPRESENTATION
    //
    //  Read the four tests below together. They are deliberately two-directional: they PASS for a
    //  contract carrying the decomposed triple plus an identified compatibility field, and FAIL for one
    //  carrying only a fused string. Either direction alone would be mis-stated - forbidding the fused
    //  form outright would contradict AAP 0.3.4, and merely requiring the triple would tolerate a fused
    //  string sitting beside it, unlabelled and indistinguishable from the real identity.
    // ==============================================================================================

    /// <summary>
    /// The subscription identity is not reducible to a single fused string: two of its three encodings
    /// are not text at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mechanical statement of the folder requirement. Strip every string-typed field from the
    /// message and BOTH the ordering and the lifetime encodings survive, because one is a number and the
    /// other an enum. A contract whose topic representation was one string could not pass this, and
    /// neither could one that renamed its way to three string fields.
    /// </para>
    /// <para>
    /// The string-typed encoding - the logical name - is asserted to BE a string, because it genuinely
    /// is one: a subscription name is free-form and is not restricted to the twelve the framework
    /// declares [se_cst_dw.sru:L47-L76]. The point is not that strings are forbidden; it is that the
    /// identity cannot be carried by text ALONE.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSubscriptionIdentityIsNotReducibleToASingleFusedString()
    {
        FieldDescriptor[] fields = TopicMessage.Fields.InDeclarationOrder().ToArray();

        FieldDescriptor sequence = TopicField(SequenceField);
        FieldDescriptor logical = TopicField(LogicalNameField);
        FieldDescriptor lifetime = TopicField(LifetimeField);

        Assert.NotEqual(FieldType.String, sequence.FieldType);
        Assert.NotEqual(FieldType.String, lifetime.FieldType);
        Assert.Equal(FieldType.String, logical.FieldType);

        int stringFieldCount = fields.Count(static field => field.FieldType == FieldType.String);
        Assert.True(
            stringFieldCount < fields.Length,
            $"Every one of the {fields.Length} field(s) of '{TopicMessageFullName}' is a string, so the "
                + "identity is expressible as text alone - which is the fused legacy form wearing more "
                + "field names.");

        // The two non-text encodings survive the strip, which is what "not reducible to one string"
        // means when stated as a property rather than as a hope.
        FieldDescriptor[] surviving = fields
            .Where(static field => field.FieldType != FieldType.String)
            .ToArray();

        Assert.Contains(surviving, field => field.FieldNumber == sequence.FieldNumber);
        Assert.Contains(surviving, field => field.FieldNumber == lifetime.FieldNumber);
    }

    /// <summary>
    /// The decomposed message is the form actually carried wherever a topic travels, and no message
    /// anywhere in the published boundary carries an unidentified fused topic string instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A decomposed message that nothing references would satisfy every assertion above while the event
    /// chain went on fusing topics somewhere else, so the carriers are swept for rather than assumed.
    /// Today the single carrier is <c>dataservices.v1.EventResult.topic</c> - the broker edge's topic on
    /// the event chain - but the sweep is computed, so a second carrier is covered automatically.
    /// </para>
    /// <para>
    /// The negative half is the sharper of the two: any field whose NAME reads as subscription identity
    /// and whose TYPE is a bare string, without a name marking it as a compatibility representation, is
    /// a fused topic travelling as the real thing. That is the shape the folder requirement forbids, and
    /// it is asserted across all three published files rather than only where a topic is expected.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDecomposedFormIsTheOneActuallyCarriedWhereverATopicTravels()
    {
        FieldDescriptor[] carriers = ContractDescriptors.AllFields()
            .Where(CarriesDecomposedTopic)
            .ToArray();

        Assert.True(
            carriers.Length > 0,
            $"No field in the published boundary is typed '{TopicMessageFullName}'. The decomposition "
                + "is declared but unused, which means topic identity is travelling in some other form.");

        Assert.All(
            carriers,
            static carrier => Assert.True(
                IsTopicBearingName(carrier.Name),
                $"Field '{carrier.FullName}' carries the decomposed topic but is not named for it, so a "
                    + "sweep looking for topic identity by name cannot find it."));

        FieldDescriptor[] unidentifiedFusedStrings = ContractDescriptors.AllFields()
            .Where(static field => field.FieldType == FieldType.String
                && IsTopicBearingName(field.Name)
                && !IsCompatibilityName(field.Name))
            .ToArray();

        Assert.True(
            unidentifiedFusedStrings.Length == 0,
            "These fields carry subscription identity as a bare string with nothing marking them as a "
                + "compatibility representation, so a consumer cannot tell the fused spelling from the "
                + "decomposed identity: "
                + $"{string.Join(", ", unidentifiedFusedStrings.Select(static field => field.FullName))}.");
    }

    /// <summary>
    /// Every message that carries a topic carries the DECOMPOSED form, and any fused string beside it is
    /// identified as a compatibility representation.
    /// </summary>
    /// <param name="messageFullName">The topic-bearing message under examination.</param>
    /// <remarks>
    /// <para>
    /// The per-message half of the sweep, so a failure names the message rather than the boundary. Rows
    /// are computed from <see cref="ContractDescriptors.AllMessages()"/>, which recurses nested types, so
    /// a topic carried by a message nested inside an event body is examined on the same terms as a
    /// top-level one.
    /// </para>
    /// <para>
    /// The identity message itself is excluded from the rows and is covered by
    /// <see cref="TheSubscriptionIdentityIsNotReducibleToASingleFusedString"/> and
    /// <see cref="EveryStringFieldOnTheIdentityMessageIsADecomposedPartOrAnIdentifiedCompatibilityForm"/>,
    /// which state the same property in the terms appropriate to it.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(TopicBearingMessageRows))]
    public void EveryMessageCarryingATopicCarriesTheDecomposedFormRatherThanAFusedString(string messageFullName)
    {
        MessageDescriptor message = ContractDescriptors.RequireMessage(messageFullName);

        FieldDescriptor[] topicBearing = message.Fields.InDeclarationOrder()
            .Where(static field => IsTopicBearingName(field.Name) || CarriesDecomposedTopic(field))
            .ToArray();

        Assert.True(
            topicBearing.Length > 0,
            $"'{messageFullName}' was selected as topic-bearing but declares no topic-bearing field; the "
                + "row provider and this assertion disagree.");

        Assert.True(
            topicBearing.Any(CarriesDecomposedTopic),
            $"'{messageFullName}' carries subscription identity but not as '{TopicMessageFullName}'. Its "
                + "topic-bearing fields are: "
                + $"{string.Join(", ", topicBearing.Select(static field => $"{field.Name}:{field.FieldType}"))}.");

        foreach (FieldDescriptor fusedCandidate in topicBearing.Where(static field => field.FieldType == FieldType.String))
        {
            Assert.True(
                IsCompatibilityName(fusedCandidate.Name),
                $"'{messageFullName}.{fusedCandidate.Name}' carries subscription identity as a bare "
                    + "string and is not named as a compatibility representation, so it is "
                    + "indistinguishable from the decomposed identity a consumer switches on.");
        }
    }

    /// <summary>
    /// Every string field on the identity message is either a DECOMPOSED PART or a field whose name
    /// identifies it as a compatibility representation of the fused legacy spelling.
    /// </summary>
    /// <param name="fieldName">The string field under examination.</param>
    /// <remarks>
    /// <para>
    /// THE PRECISE STATEMENT OF C-B FOR THIS CONTRACT (AAP 0.3.4). The fused spelling is permitted to
    /// travel - a compatibility edge has to replay the exact string legacy-shaped code expects, and a
    /// characterization recording keyed on the string still has to match - but it must be
    /// DISTINGUISHABLE from the decomposed identity. Since the proto comments are not observable from
    /// the descriptor graph (see the file header), the field NAME is the only handle a mechanical
    /// assertion has on that distinction.
    /// </para>
    /// <para>
    /// The two legitimate decomposed parts are the logical name and the namespace. Neither carries the
    /// fused spelling: the name has the ordering prefix and the lifetime namespace removed, and the
    /// namespace has its <c>^</c> negation split into its own boolean [n_cst_eventful.sru:L1012-L1015].
    /// A part is additionally required NOT to be named as a compatibility form, so a decomposed field
    /// cannot masquerade as the fused one either.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(TopicMessageStringFieldRows))]
    public void EveryStringFieldOnTheIdentityMessageIsADecomposedPartOrAnIdentifiedCompatibilityForm(string fieldName)
    {
        FieldDescriptor field = TopicField(fieldName);
        Assert.Equal(FieldType.String, field.FieldType);

        bool isDecomposedPart = DecomposedStringParts.Contains(fieldName, StringComparer.Ordinal);
        bool isCompatibilityForm = IsCompatibilityName(fieldName);

        Assert.True(
            isDecomposedPart || isCompatibilityForm,
            $"'{TopicMessageFullName}.{fieldName}' is a string that is neither one of the known "
                + $"decomposed parts ({string.Join(", ", DecomposedStringParts)}) nor named as a "
                + $"compatibility representation (one of: {string.Join(", ", CompatibilityNameMarkers)}). "
                + "An unlabelled string on this message is a fused topic travelling as the real "
                + "identity.");

        Assert.False(
            isDecomposedPart && isCompatibilityForm,
            $"'{TopicMessageFullName}.{fieldName}' is a decomposed part whose name also reads as a "
                + "compatibility representation, which makes the two indistinguishable.");
    }

    /// <summary>
    /// The fused legacy spelling travels only as an ADDITIVE compatibility field, beside the decomposed
    /// triple rather than instead of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CONDITIONAL BY DESIGN, and that is not a weakness in the assertion. AAP 0.3.4 permits the fused
    /// form at the compatibility edge; it does not require it. A contract carrying the decomposed triple
    /// and NO compatibility field is equally acceptable, so this test asserts the triple
    /// unconditionally and then constrains whatever compatibility fields happen to exist - which
    /// <see cref="Assert.All{T}(IEnumerable{T}, System.Action{T})"/> expresses exactly, since it passes
    /// on an empty set.
    /// </para>
    /// <para>
    /// The published contract does carry one today - the fused spelling, verbatim, so that
    /// "0-itemchanged", ".^persistent" and "%evt.myns" can all be replayed losslessly. It is the only
    /// field that survives a namespace or a negation the decomposed fields could not express between
    /// them, which is why its presence is a design decision rather than a leftover.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFusedLegacySpellingTravelsOnlyAsAnAdditiveCompatibilityFieldBesideTheDecomposedTriple()
    {
        // ADDITIVE means the triple is there regardless. Asserted first and unconditionally, because
        // that is the half of the claim a compatibility field must not be allowed to substitute for.
        foreach ((string fieldName, FieldType declaredType, _, _, _) in DecomposedFields)
        {
            Assert.Equal(declaredType, TopicField(fieldName).FieldType);
        }

        FieldDescriptor[] compatibilityForms = TopicMessage.Fields.InDeclarationOrder()
            .Where(static field => field.FieldType == FieldType.String && IsCompatibilityName(field.Name))
            .ToArray();

        Assert.All(compatibilityForms, static form =>
        {
            Assert.False(
                form.IsRepeated,
                $"Compatibility field '{form.FullName}' is repeated; the fused spelling of one "
                    + "subscription is one string.");

            Assert.False(
                DecomposedStringParts.Contains(form.Name, StringComparer.Ordinal),
                $"'{form.FullName}' is simultaneously a decomposed part and a compatibility form.");
        });
    }

    /// <summary>
    /// Each order-prefixed legacy spelling decomposes into exactly the ordinal and the UNPREFIXED
    /// logical name the two decomposed fields carry - and composing them back reconstitutes the spelling
    /// byte for byte.
    /// </summary>
    /// <param name="fusedSpelling">The legacy constant's value, transcribed from the oracle.</param>
    /// <param name="ordinal">The dispatch position its leading digit encodes.</param>
    /// <param name="logicalName">The identity left once the prefix is removed.</param>
    /// <param name="wellKnownValue">The contract's classifier member for the topic.</param>
    /// <param name="locator">The oracle locator for the constant.</param>
    /// <remarks>
    /// <para>
    /// THE ROW THAT PROVES THE KEY INSIGHT. Only two of the twelve topics are spelled with a leading
    /// digit, and those digits are dispatch order rather than part of the name - the broker sorts
    /// subscriptions lexically by name [n_cst_eventful.sru:L407-L422], and ASCII digits sort before
    /// letters. The decomposition is asserted in both directions: the spelling really does split into
    /// (ordinal, separator, logical name), and re-composing the two decomposed values reproduces the
    /// legacy spelling exactly, which is what makes the compatibility field's round trip meaningful.
    /// </para>
    /// <para>
    /// The classifier is checked because it is the third independent witness that the decomposed name is
    /// the UNPREFIXED one: the contract's well-known member for "0-itemchanged" spells the identity
    /// without the digit. Together with the split and the re-composition, this stands in for the prose
    /// assertion the descriptor cannot supply - see the file header.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PrefixedTopicRows))]
    public void EachOrderPrefixedSpellingDecomposesIntoTheOrdinalAndTheUnprefixedLogicalName(
        string fusedSpelling,
        int ordinal,
        string logicalName,
        string wellKnownValue,
        string locator)
    {
        Assert.True(
            fusedSpelling.Length > 2,
            $"The spelling '{fusedSpelling}' at [{locator}] is too short to carry a prefix and a name.");

        Assert.True(
            char.IsAsciiDigit(fusedSpelling[0]),
            $"The spelling '{fusedSpelling}' at [{locator}] was transcribed as order-prefixed but does "
                + "not begin with a digit.");

        Assert.Equal(ordinal, fusedSpelling[0] - '0');
        Assert.Equal(SequencePrefixSeparator, fusedSpelling[1]);
        Assert.Equal(logicalName, fusedSpelling[2..]);

        // THE LEGACY ENCODES THE ORDINAL AS EXACTLY ONE ASCII DIGIT, which is why the lexical sort works
        // at all: a two-digit prefix would sort "10-" before "2-" and the ordering would stop matching the
        // numbers [n_cst_eventful.sru:L407-L422].
        Assert.InRange(ordinal, 0, 9);

        // Composing the two decomposed values reproduces the fused legacy spelling byte for byte, which
        // is the property the compatibility field of AAP 0.3.4 depends on. Composed from the DIGIT rather
        // than by formatting the number, so the assertion is culture-independent by construction.
        Assert.Equal(fusedSpelling, $"{(char)('0' + ordinal)}{SequencePrefixSeparator}{logicalName}");

        // The ordinal is representable in the type the contract actually declares for it.
        FieldDescriptor sequence = TopicField(SequenceField);
        Assert.True(
            CanRepresent(sequence.FieldType, ordinal),
            $"'{TopicMessageFullName}.{SequenceField}' is declared {sequence.FieldType} and cannot "
                + $"represent the ordinal {ordinal} evidenced at [{locator}].");

        // The decomposed name is the UNPREFIXED one, witnessed by the contract's own classifier.
        AssertClassifierCarriesTheUnprefixedName(wellKnownValue, logicalName, locator);
    }

    /// <summary>
    /// Each of the ten unprefixed spellings carries no ordinal at all, which is exactly why a presence
    /// flag has to travel beside the ordering encoding.
    /// </summary>
    /// <param name="fusedSpelling">The legacy constant's value, transcribed from the oracle.</param>
    /// <param name="wellKnownValue">The contract's classifier member for the topic.</param>
    /// <param name="locator">The oracle locator for the constant.</param>
    /// <remarks>
    /// These ten rows are what turn <c>has_sequence_prefix</c> from a curiosity into a requirement. An
    /// unprefixed topic and "0-itemchanged" both report an ordinal of zero, and proto3 omits a zero from
    /// the encoding entirely, so the numeric field alone cannot distinguish "made no ordering statement"
    /// from "claimed position zero" - and the difference is observable, because a lexical sort places
    /// "0-itemchanged" before "clicked" [n_cst_eventful.sru:L407-L422] while an absent prefix orders
    /// purely by the name's own letters.
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnprefixedTopicRows))]
    public void EachUnprefixedSpellingCarriesNoOrdinalWhichIsWhyThePresenceFlagHasToTravel(
        string fusedSpelling,
        string wellKnownValue,
        string locator)
    {
        Assert.False(string.IsNullOrEmpty(fusedSpelling));

        Assert.False(
            char.IsAsciiDigit(fusedSpelling[0]),
            $"The spelling '{fusedSpelling}' at [{locator}] was transcribed as unprefixed but begins "
                + "with a digit.");

        Assert.False(
            fusedSpelling.Contains(SequencePrefixSeparator, StringComparison.Ordinal),
            $"The spelling '{fusedSpelling}' at [{locator}] carries the ordering separator "
                + $"'{SequencePrefixSeparator}', so it is not unprefixed after all.");

        // The presence flag is what records the absence, and it has to be a boolean to do it.
        FieldDescriptor presence = TopicField(SequencePresenceField);
        Assert.True(
            presence.FieldType == FieldType.Bool,
            $"'{TopicMessageFullName}.{SequencePresenceField}' is declared {presence.FieldType}; only a "
                + $"boolean can record that '{fusedSpelling}' [{locator}] carried no prefix as distinct "
                + "from carrying a prefix of zero.");

        AssertClassifierCarriesTheUnprefixedName(wellKnownValue, fusedSpelling, locator);
    }

    /// <summary>
    /// Asserts that the contract's well-known classifier member spells the topic's UNPREFIXED logical
    /// name.
    /// </summary>
    /// <param name="wellKnownValue">The classifier member's proto spelling.</param>
    /// <param name="logicalName">The unprefixed identity the member must spell.</param>
    /// <param name="locator">The oracle locator, for the failure message.</param>
    /// <remarks>
    /// Shared by both topic theories so the two say the same thing about the classifier. The comparison
    /// is case-insensitive on purpose and on one axis only: the legacy constants are SCREAMING_SNAKE
    /// while their values are lower-case, and preserving both spellings verbatim is required by AAP
    /// 0.4.5.3 - so casing is the one difference that must be tolerated, and every other character must
    /// match exactly.
    /// </remarks>
    private static void AssertClassifierCarriesTheUnprefixedName(
        string wellKnownValue,
        string logicalName,
        string locator)
    {
        Assert.StartsWith(WellKnownValuePrefix, wellKnownValue, StringComparison.Ordinal);

        EnumValueDescriptor classifier = RequireEnumValue(TopicEnum(WellKnownEnumName), wellKnownValue);

        Assert.Equal(
            logicalName,
            classifier.Name[WellKnownValuePrefix.Length..],
            StringComparer.OrdinalIgnoreCase);

        Assert.True(
            classifier.Number != 0,
            $"Classifier '{wellKnownValue}' is numbered 0, which the contract reserves for "
                + $"\"not one of the twelve - an application-defined topic\". The topic at [{locator}] "
                + "is one of the twelve the framework declares.");
    }

    // ==============================================================================================
    //  THE LIMITATION, ASSERTED RATHER THAN QUIETLY DROPPED
    // ==============================================================================================

    /// <summary>
    /// Records mechanically that the descriptor graph does not surface proto comments, which is why the
    /// "the name is the unprefixed logical name" claim is asserted structurally rather than from prose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>FieldDescriptor.Declaration</c> is the accessor for a field's leading proto comments,
    /// and it is populated only when the embedded <c>FileDescriptorProto</c> carries
    /// <c>SourceCodeInfo</c>. Grpc.Tools does not emit it for the C# projection, so the property is null
    /// for every field here and the contract's own documentation is unreachable from a test.
    /// </para>
    /// <para>
    /// Exactly one of the two branches below runs, and which one is a property of the TOOLCHAIN rather
    /// than of the contract. The first is live today and asserts the structural substitutes. The second
    /// becomes live if source info is ever emitted - at which point the prose claim becomes assertable
    /// and is asserted, rather than being left as a comment nobody re-reads.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDescriptorGraphDoesNotSurfaceProtoComments()
    {
        FieldDescriptor sequence = TopicField(SequenceField);
        FieldDescriptor logical = TopicField(LogicalNameField);
        FieldDescriptor lifetime = TopicField(LifetimeField);

        FieldDescriptor[] triple = [sequence, logical, lifetime];
        FieldDescriptor[] documented = triple
            .Where(static field => field.Declaration is not null)
            .ToArray();

        if (documented.Length == 0)
        {
            // LIVE TODAY. The claim is carried by the three structural witnesses instead: the logical
            // name is text, the ordering is a number, and the lifetime is a closed enum - which
            // together say that the name cannot be carrying the prefix or the namespace, because both
            // of those travel in fields of their own.
            Assert.Equal(FieldType.String, logical.FieldType);
            Assert.Contains(sequence.FieldType, IntegralFieldTypes);
            Assert.Equal(FieldType.Enum, lifetime.FieldType);
            return;
        }

        // Reached only once the toolchain begins embedding SourceCodeInfo.
        Assert.All(documented, static field => Assert.True(
            field.Declaration is { } declaration
                && declaration.LeadingComments.Contains("prefix", StringComparison.OrdinalIgnoreCase),
            $"Field '{field.FullName}' now exposes proto documentation, so it must state that the "
                + "ordering prefix travels in a field of its own rather than inside the name."));
    }
}
