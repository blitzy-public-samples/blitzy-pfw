// ==================================================================================================
//  ProtoDescriptorTests - THE EIGHT gRPC CONTRACTS, ASSERTED AGAINST THE GENERATED DESCRIPTORS
//  ------------------------------------------------------------------------------------------------
//  SUBJECT     Proto/common.v1.proto        - shared vocabulary, no service
//              Proto/dataservices.v1.proto  - C-03 DataWindowService, C-04 ColumnExpressionService
//              Proto/persistence.v1.proto   - C-05 Query, C-06 Update, C-07 Command, C-08 Transaction
//
//  WHY ASSERT AGAINST DESCRIPTORS RATHER THAN AGAINST THE .proto TEXT
//  ------------------------------------------------------------------------------------------------
//  A descriptor is what the wire agrees on. The .proto text is its source, and the generated C# is one
//  projection of it, but the descriptor is the artifact both ends of a call resolve against - so a
//  property asserted here is a property of the CONTRACT rather than of a file's contents or of one
//  language's rendering of it.
//
//  This also means these tests exercise the code generation itself. `GrpcServices="Both"` is
//  configured in the project file, and a misconfiguration there produces messages without service
//  stubs - which compiles perfectly and then fails at wire-up. Reaching for a ServiceDescriptor is
//  what detects that.
//
//  WHAT IS AND IS NOT BEING CLAIMED
//  ------------------------------------------------------------------------------------------------
//  These are CONTRACT-CONFORMANCE tests: they assert that the published descriptors carry the
//  structure the legacy behaviour requires, with each requirement traced to its source locator. They
//  are NOT Golden-Master parity tests - no PowerBuilder oracle was executed to produce them, and none
//  could be, because the legacy has no wire format at all. What they establish is that the numeric
//  alphabets, the streaming directions, the veto states and the write-only field treatment which the
//  legacy semantics DEMAND are actually present on the wire, so a downstream implementation cannot
//  quietly lose one.
// ==================================================================================================

using Google.Protobuf.Reflection;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Contracts.Persistence.V1;
using Xunit;

namespace PowerFramework.Contracts.Tests;

public sealed class ProtoDescriptorTests
{
    private static FileDescriptor Common => CommonV1Reflection.Descriptor;

    private static FileDescriptor DataServices => DataservicesV1Reflection.Descriptor;

    private static FileDescriptor Persistence => PersistenceV1Reflection.Descriptor;

    private static FileDescriptor[] AllFiles => [Common, DataServices, Persistence];

    /// <summary>
    /// Finds an enum by name, searching top-level declarations and then every message's nested
    /// declarations.
    /// </summary>
    /// <remarks>
    /// Nesting is a source-organisation choice rather than a wire-visible one, so a test asserting a
    /// numeric alphabet should not have to know whether the enum was declared at file scope or inside
    /// the message that uses it. Searching both means moving an enum for readability does not break
    /// the assertion that its VALUES are correct.
    /// </remarks>
    private static EnumDescriptor FindEnum(FileDescriptor file, string name)
    {
        EnumDescriptor? top = file.EnumTypes.FirstOrDefault(e => e.Name == name);
        if (top is not null)
        {
            return top;
        }

        foreach (MessageDescriptor message in file.MessageTypes)
        {
            EnumDescriptor? nested = message.EnumTypes.FirstOrDefault(e => e.Name == name);
            if (nested is not null)
            {
                return nested;
            }
        }

        Assert.Fail($"Enum '{name}' was not found in {file.Name}.");
        return null!;
    }

    /// <summary>Maps an enum's declared values to (name, number) pairs in declaration order.</summary>
    private static (string Name, int Number)[] ValuesOf(EnumDescriptor descriptor) =>
        descriptor.Values.Select(static value => (value.Name, value.Number)).ToArray();

    private static MessageDescriptor Message(FileDescriptor file, string name)
    {
        MessageDescriptor? found = file.FindTypeByName<MessageDescriptor>(name);
        Assert.True(found is not null, $"Message '{name}' was not found in {file.Name}.");
        return found!;
    }

    // ==============================================================================================
    //  FILE-LEVEL STRUCTURE
    // ==============================================================================================

    [Fact]
    public void TheThreeContractFilesDeclareTheirExpectedPackagesAndNamespaces()
    {
        Assert.Equal("common.v1", Common.Package);
        Assert.Equal("dataservices.v1", DataServices.Package);
        Assert.Equal("persistence.v1", Persistence.Package);

        // THE VERSION IS IN THE PACKAGE NAME, WHICH IS WHAT MAKES INDEPENDENT VERSIONING POSSIBLE.
        //
        // A breaking change becomes `dataservices.v2` alongside `dataservices.v1`, so both can be served
        // at once and a consumer migrates on its own schedule. That is the whole reason C-03 and C-04
        // are separate contracts in the first place: the expansion engine must be able to version
        // without forcing the DataWindow surface to.
        foreach (FileDescriptor file in AllFiles)
        {
            Assert.EndsWith(".v1", file.Package, StringComparison.Ordinal);
        }

        Assert.Equal("PowerFramework.Contracts.Common.V1", Common.GetOptions()!.CsharpNamespace);
        Assert.Equal("PowerFramework.Contracts.DataServices.V1", DataServices.GetOptions()!.CsharpNamespace);
        Assert.Equal("PowerFramework.Contracts.Persistence.V1", Persistence.GetOptions()!.CsharpNamespace);
    }

    [Fact]
    public void CommonDeclaresNoServiceBecauseItIsSharedVocabularyRatherThanABoundary()
    {
        // `common.v1` IS THE SHARED VOCABULARY, NOT A SERVICE.
        //
        // It carries the return-code algebra, the buffer and item-status enumerations and the error and
        // conflict shapes - the types BOTH DataServices and Persistence need in order to describe the
        // same concepts identically. Giving it a service would make it a boundary, and a boundary in
        // the shared layer is exactly the back door the constraint "the only cross-service coupling is
        // the published contract" exists to prevent.
        Assert.Empty(Common.Services);
        Assert.NotEmpty(Common.MessageTypes);
    }

    [Fact]
    public void TheSixServicesAreDeclaredAcrossTheTwoBoundaryFiles()
    {
        Assert.Equal(
            ["ColumnExpressionService", "DataWindowService"],
            DataServices.Services.Select(static service => service.Name)
                .OrderBy(static name => name, StringComparer.Ordinal).ToArray());

        Assert.Equal(
            ["CommandService", "QueryService", "TransactionService", "UpdateService"],
            Persistence.Services.Select(static service => service.Name)
                .OrderBy(static name => name, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void EveryServiceExposesAtLeastOneMethodAndTheGeneratorProducedServiceStubs()
    {
        foreach (FileDescriptor file in AllFiles)
        {
            foreach (ServiceDescriptor service in file.Services)
            {
                Assert.NotEmpty(service.Methods);

                // REACHING THE ServiceDescriptor AT ALL IS THE ASSERTION THAT MATTERS HERE.
                //
                // `GrpcServices="Both"` in the project file is what makes the generator emit service
                // base classes and clients alongside the message types. Set to `None` - or omitted - the
                // messages still generate and everything still compiles, and the failure appears only
                // when something tries to map an endpoint. This is the cheap check for that.
                Assert.NotNull(service.File);
                Assert.Equal(file.Package, service.File.Package);
            }
        }
    }

    // ==============================================================================================
    //  C-03 - THE DATAWINDOW SERVICE AND ITS STREAMING SHAPE
    // ==============================================================================================

    [Fact]
    public void TheDataWindowServiceCarriesTheRetrievalSessionUpdateGateAndHeadlessModelSurface()
    {
        ServiceDescriptor service = DataServices.FindTypeByName<ServiceDescriptor>("DataWindowService")!;

        // C-03 IS SIXTEEN METHODS, AND THE SET IS ASSERTED RATHER THAN THE COUNT.
        //
        // Eight are the core: `Retrieve` and `EventChain` (the two streams), `OpenValidationSession` and
        // `CloseValidationSession` (the four pieces of cross-event state materialised as an explicit
        // correlated session), `Update`, and the three gate operations.
        //
        // The other eight are the READ-AND-APPLY PAIRS OF THE FOUR HEADLESS MODELS - DropDownSearch,
        // ColumnSort, ContextMenu and RowSelect. They belong on C-03 rather than nowhere, and that is
        // the substantive point: three of those four services are irreducibly presentational in the
        // legacy, so each SPLITS into a headless half that ships and a rendering half that does not
        // (Agent Action Plan 0.2.1.3 correction 4, 0.3.5). Without these eight the headless halves
        // would be four compiling declarations with no way to reach them, which is a documented gap
        // dressed up as an implementation.
        //
        // WHAT IS NOT HERE IS THE ASSERTION'S OTHER HALF: no method takes or returns a pixel
        // coordinate, a DPI factor, a font metric, a window handle or an IME state. The rendering
        // halves are the reserved /v1/design/** extension point, and C-D forbids implementing any part
        // of them here.
        Assert.Equal(
            [
                "ApplyColumnSort",
                "ApplyContextMenuModel",
                "ApplyDropDownSearch",
                "ApplyRowSelectStyle",
                "CloseValidationSession",
                "DisableEvent",
                "EnableEvent",
                "EventChain",
                "GetColumnSortState",
                "GetContextMenuModel",
                "GetDropDownSearchState",
                "GetEventGate",
                "GetRowSelectState",
                "OpenValidationSession",
                "Retrieve",
                "Update",
            ],
            service.Methods.Select(static method => method.Name)
                .OrderBy(static name => name, StringComparer.Ordinal).ToArray());

        // AND EXACTLY TWO OF THE SIXTEEN STREAM. The eight headless operations are unary in both
        // directions, which is what makes them projectable over REST at all - Gateway is the sole
        // ingress, so an operation that streamed would be unreachable from outside the cluster.
        Assert.Equal(
            ["EventChain", "Retrieve"],
            service.Methods
                .Where(static method => method.IsClientStreaming || method.IsServerStreaming)
                .Select(static method => method.Name)
                .OrderBy(static name => name, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void RetrieveIsServerStreamingSoResultsArriveProgressively()
    {
        MethodDescriptor retrieve = DataServices
            .FindTypeByName<ServiceDescriptor>("DataWindowService")!
            .FindMethodByName("Retrieve")!;

        // SERVER STREAMING REPRODUCES PROGRESSIVE RECORDSET DELIVERY.
        //
        // The legacy retrieves in chunks rather than materialising a whole result, and the chunk size is
        // a configurable guard rather than an implementation detail. A unary Retrieve would force the
        // entire result into one message and lose the progressive property the legacy has.
        Assert.False(retrieve.IsClientStreaming);
        Assert.True(retrieve.IsServerStreaming);
    }

    [Fact]
    public void EventChainIsBidirectionalBecauseTheOrderedVetoableChainRequiresIt()
    {
        MethodDescriptor chain = DataServices
            .FindTypeByName<ServiceDescriptor>("DataWindowService")!
            .FindMethodByName("EventChain")!;

        // BIDIRECTIONAL IS STRUCTURALLY REQUIRED, NOT A PREFERENCE.
        //
        // se_cst_dw declares 22 events - 13 raw pbm_dwn* events and 9 semantic ones - where each raw
        // event delegates to a semantic one and then to the broker, and a handler's RETURN VALUE can
        // veto what happens next. So the server must send an event and RECEIVE a decision before it can
        // continue, in order, within one logical session. That is a bidirectional stream and nothing
        // less will carry it.
        Assert.True(chain.IsClientStreaming);
        Assert.True(chain.IsServerStreaming);
    }

    [Fact]
    public void UpdateIsUnarySoItsConflictOutcomeIsASingleDefiniteAnswer()
    {
        MethodDescriptor update = DataServices
            .FindTypeByName<ServiceDescriptor>("DataWindowService")!
            .FindMethodByName("Update")!;

        // UNARY, DELIBERATELY.
        //
        // An update either applied or hit an optimistic-concurrency conflict. A streaming update would
        // make "did it conflict?" a question about a sequence rather than a single answer, and the
        // caller's retry-or-surface decision needs one answer.
        Assert.False(update.IsClientStreaming);
        Assert.False(update.IsServerStreaming);
    }

    [Fact]
    public void TheItemChangeAlphabetIsTheLegacyFourValueSetAndNotTheReturnCodeAlgebra()
    {
        EnumDescriptor alphabet = FindEnum(DataServices, "ItemChangeResult");

        // THE {0,1,2,3} ALPHABET, PRESERVED VERBATIM.
        //
        // se_cst_dw.sru:L182-L253 is the most intricate behaviour in the in-scope set, and its return
        // values are NOT the return-code constants. In the legacy dispatch: case 1 FALLS THROUGH to
        // case 2, restoring value and status only if an earlier equality test held; case 3 keeps the
        // value, does not move focus, and REWRITES its own result to 1; and the default arm coerces by
        // the column type's first five characters and then FORCIBLY RETURNS 2 so the runtime will not
        // re-apply the edit text over the buffer.
        //
        // Modelling this as its own enumeration rather than mapping it onto RetCode is essential: in
        // RetCode, 1 is PREVENT and 0 is OK, and `IsSucceeded` tests `>= 0` so all four of these values
        // would read as "succeeded". The two alphabets share digits and mean entirely different things.
        Assert.Equal(
            [
                ("ITEM_CHANGE_RESULT_DEFAULT", 0),
                ("ITEM_CHANGE_RESULT_TRIGGER_VALIDATION_ERROR", 1),
                ("ITEM_CHANGE_RESULT_RESTORE_AND_REJECT_TEXT", 2),
                ("ITEM_CHANGE_RESULT_KEEP_VALUE_NO_FOCUS_MOVE", 3),
            ],
            ValuesOf(alphabet));
    }

    [Fact]
    public void AllTwentyTwoDataWindowEventsAreNamedOnTheWire()
    {
        EnumDescriptor events = FindEnum(DataServices, "EventId");

        // 22 EVENTS PLUS THE PROTO3 ZERO SENTINEL = 23 VALUES.
        //
        // se_cst_dw declares exactly 22: 9 semantic and 13 raw. Carrying all of them individually is
        // what lets a conformance test assert ORDERING - the highest-risk property in the refactor -
        // because an ordering assertion needs to name the events whose order it is asserting.
        Assert.Equal(23, events.Values.Count);

        (string Name, int Number)[] values = ValuesOf(events);
        Assert.Equal(("EVENT_ID_UNSPECIFIED", 0), values[0]);

        // THE NUMBERING IS DENSE AND IN DECLARATION ORDER, so the wire ordinal and the legacy
        // declaration order agree and a recorded event sequence can be read against se_cst_dw directly.
        for (int index = 0; index < values.Length; index++)
        {
            Assert.Equal(index, values[index].Number);
        }

        // The item-change chain - the strictly-synchronous group - must be individually addressable.
        string[] names = values.Select(static value => value.Name).ToArray();
        foreach (string required in (string[])
        [
            "EVENT_ID_ONDWNITEMCHANGE",
            "EVENT_ID_ONDOITEMCHANGE",
            "EVENT_ID_ONITEMCHANGED",
            "EVENT_ID_ONDOITEMCHANGED",
            "EVENT_ID_ONDWNITEMVALIDATIONERROR",
            "EVENT_ID_ONDWNKILLFOCUS",
        ])
        {
            Assert.Contains(required, names);
        }
    }

    [Fact]
    public void TheVetoIsTriValuedAndIsNeverFlattenedToABoolean()
    {
        EnumDescriptor veto = FindEnum(DataServices, "Result");

        // THREE STATES, NOT TWO.
        //
        // The broker's veto is prevent-once (1), prevent-deep (2), or continue - even though the inline
        // handler comments in se_cst_dw suggest a binary convention. Flattening it to a boolean would
        // silently convert a DEEP prevention into a SHALLOW one: the event would stop propagating at
        // this level and then resume at the next, which is a behavioural change that no type error and
        // no round-trip test would catch.
        Assert.Equal(
            [("CONTINUE", 0), ("PREVENT_ONCE", 1), ("PREVENT_DEEP", 2)],
            ValuesOf(veto));
    }

    [Fact]
    public void TheEventGateBitsAreComposableAndKeepTheirLegacyValues()
    {
        EnumDescriptor gate = FindEnum(DataServices, "Bit");

        // 1, 2, 4 - POWERS OF TWO, BECAUSE THEY ARE COMPOSED WITH BITWISE OR.
        //
        // se_cst_dw.sru:L41-L43 declares EID_ROWFOCUSCHANGE=1, EID_ITEMFOCUSCHANGE=2, EID_ITEMCHANGE=4,
        // and the gate guards are bit TESTS. Renumbering these densely would break every stored mask.
        Assert.Equal(
            [
                ("EID_UNSPECIFIED", 0),
                ("EID_ROWFOCUSCHANGE", 1),
                ("EID_ITEMFOCUSCHANGE", 2),
                ("EID_ITEMCHANGE", 4),
            ],
            ValuesOf(gate));
    }

    [Fact]
    public void TheOrderingDisciplineDistinguishesTheTwoPatternsAssignedPerCapabilityArea()
    {
        EnumDescriptor discipline = FindEnum(DataServices, "OrderingDiscipline");

        // THE TWO PATTERNS ARE CARRIED ON THE WIRE, WHICH IS WHAT MAKES THE PER-AREA ASSIGNMENT
        // ENFORCEABLE.
        //
        // Event ordering is the single highest risk in this refactor, and the resolution is to choose
        // per capability area between (a) a sequencing token on every event, so a consumer can DETECT
        // out-of-order delivery, and (b) a strictly synchronous chain where no reordering is permitted
        // at all.
        //
        // The item-change chain is (b) because reordering it is not merely undesirable but semantically
        // impossible: the validation-error handler READS AND CLEARS the code stashed by the preceding
        // item-change event, so its behaviour is a function of the prior event's return value. Focus,
        // mouse and row-focus notifications are (a) - they carry no cross-event state.
        //
        // Under (b), sequence numbers are used FOR DETECTION ONLY: an out-of-order arrival is a hard
        // error, never a reorder opportunity. Naming the discipline explicitly is what stops an
        // implementer helpfully buffering and reordering a chain that must not be reordered.
        Assert.Equal(
            [
                ("ORDERING_DISCIPLINE_UNSPECIFIED", 0),
                ("ORDERING_DISCIPLINE_SYNCHRONOUS", 1),
                ("ORDERING_DISCIPLINE_SEQUENCED", 2),
            ],
            ValuesOf(discipline));
    }

    [Fact]
    public void TheBrokerTopicIsDecomposedIntoItsThreeIndependentEncodings()
    {
        MessageDescriptor topic = Message(DataServices, "BrokerTopic");

        string[] fields = topic.Fields.InDeclarationOrder()
            .Select(static field => field.Name).ToArray();

        // THE LEGACY TOPIC STRING FUSES THREE INDEPENDENT ENCODINGS INTO ONE OPAQUE VALUE.
        //
        // se_cst_dw.sru:L47-L76 declares twelve broker topics, two of which carry an ORDERING PREFIX
        // baked into the string itself - the item-changed topic is spelled with a leading "0-" and the
        // edit-changed topic with a leading "1-" - and the broker's dispatch order derives from the
        // LEXICAL SORT of the subscription name. Separately, n_cst_threading.sru:L544,L596 reveal a
        // `.^persistent` NAMESPACE SUFFIX in the same string.
        //
        // So one string carries ordering, logical identity and lifetime. Transmit it whole and the
        // ordering becomes invisible; parse it at the far end and the contract has an undocumented
        // grammar. The wire therefore carries the three separately and reconstitutes the fused legacy
        // form only at the compatibility edge.
        Assert.Contains("sequence", fields);
        Assert.Contains("name", fields);
        Assert.Contains("lifetime", fields);

        EnumDescriptor lifetime = FindEnum(DataServices, "Lifetime");
        string[] lifetimeNames = ValuesOf(lifetime).Select(static value => value.Name).ToArray();
        Assert.Contains("LIFETIME_TRANSIENT", lifetimeNames);
        Assert.Contains("LIFETIME_PERSISTENT", lifetimeNames);
    }

    // ==============================================================================================
    //  C-04 - THE COLUMN-EXPRESSION SERVICE, ITS INVERTED STREAMS AND ITS EXPANSION MODES
    // ==============================================================================================

    [Fact]
    public void TheColumnExpressionServiceCarriesTheFullSurfaceTheSourceDeclares()
    {
        ServiceDescriptor service =
            DataServices.FindTypeByName<ServiceDescriptor>("ColumnExpressionService")!;

        // THE REAL API IS MUCH LARGER THAN THE LEGACY DOCUMENTATION DESCRIBES.
        //
        // docs/n_cst_dwsvc_columnexp.md describes nine methods. The source declares far more, and the
        // contract must cover what the SOURCE has rather than what the documentation says: expression
        // add/set/get/remove/remove-all, variable add across seven typed overloads, variable set across
        // seven types in three arities each, expression-variable add/set/get, relative-column setters in
        // singular and plural forms by index and by name, three per-expression flags each by index and
        // by name, calculate in four arities, calculate-all in two, and an item entry point.
        //
        // The overload families collapse into single RPCs carrying an explicit selector, because a
        // protobuf message with an optional field expresses "three arities" better than three methods.
        //
        // TWO OF THE TWENTY-SIX ARE NOT OVERLOAD COLLAPSES AND ARE WORTH NAMING. `GetExpressionState`
        // is the engine-state snapshot that makes the seven internal structures of
        // `n_cst_dwsvc_columnexp.sru:L22-L83` REACHABLE rather than merely declared - the expression
        // table, the reverse dependency index, the grammar sentinels and the three-part $ / $$
        // bindings. `EventStream` is the server stream of the three events the engine declares on
        // ITSELF [:L86-L88]: item-changed, do-item-changed with its `frominput` flag, and var-changed
        // with its `forcecalc` flag. Neither is in the nine methods the legacy documentation lists,
        // which is precisely the point of asserting against the SOURCE.
        Assert.Equal(26, service.Methods.Count);

        string[] names = service.Methods.Select(static method => method.Name).ToArray();

        foreach (string required in (string[])
        [
            "OpenExpressionSession", "CloseExpressionSession",
            "AddExpression", "SetExpression", "GetExpression", "RemoveExpression", "RemoveAllExpressions",
            "AddVariable", "SetVariable",
            "AddVariableExpression", "SetVariableExpression", "GetVariableExpression",
            "AddForeignVariable", "SetRelativeColumns", "SetExpressionFlag",
            "Calc", "CalcAll", "CalcEmpty", "CalcItem",
            "SetEnabled", "SetTrace", "GetServiceState", "GetExpressionState",
            "EventStream", "InvokeMethodChannel", "TraceChannel",
        ])
        {
            Assert.Contains(required, names);
        }
    }

    [Fact]
    public void InvokeMethodChannelIsInvertedSoTheServiceCallsBackIntoItsClient()
    {
        MethodDescriptor channel = DataServices
            .FindTypeByName<ServiceDescriptor>("ColumnExpressionService")!
            .FindMethodByName("InvokeMethodChannel")!;

        Assert.True(channel.IsClientStreaming);
        Assert.True(channel.IsServerStreaming);

        // THE INVERSION IS VISIBLE IN THE MESSAGE NAMES, AND IT IS THE POINT.
        //
        // The legacy expects the APPLICATION to implement the macro switch: `oncolumnexpinvokemethod`
        // is an event the host handles, returning `any` over a `string[]` [se_cst_dw.sru:L14]. Across a
        // service boundary that means DataServices must call BACK INTO its client mid-calculation,
        // because the calculation cannot proceed without the returned value.
        //
        // gRPC has no server-initiated call, so the inversion is expressed by inverting the stream: the
        // CLIENT sends RESPONSES and the SERVER sends REQUESTS. That reads backwards on purpose, and
        // asserting it here is what stops someone "fixing" the direction and breaking macro invocation.
        Assert.Equal("dataservices.v1.InvokeMethodResponse", channel.InputType.FullName);
        Assert.Equal("dataservices.v1.InvokeMethodRequest", channel.OutputType.FullName);
    }

    [Fact]
    public void TraceChannelStreamsDiagnosticsWithoutBlockingTheCalculation()
    {
        MethodDescriptor channel = DataServices
            .FindTypeByName<ServiceDescriptor>("ColumnExpressionService")!
            .FindMethodByName("TraceChannel")!;

        Assert.True(channel.IsClientStreaming);
        Assert.True(channel.IsServerStreaming);

        // TRACE IS FIRE-AND-FORGET, WHICH IS WHY IT IS THE SEQUENCED DISCIPLINE RATHER THAN SYNCHRONOUS.
        //
        // `oncolumnexptrace(row, dwo, stack, expr, value)` is pure diagnostics: it carries the call
        // stack the vector container builds [n_cst_dwsvc_columnexp.sru:L110, created at :L2421], and no
        // calculation waits on it. So a sequencing token is sufficient and blocking would be wrong.
        Assert.Equal("dataservices.v1.TraceRecord", channel.OutputType.FullName);
    }

    [Fact]
    public void AllFiveExpansionModesAreRepresentableAndTheStaticDynamicSplitIsNamed()
    {
        EnumDescriptor modes = FindEnum(DataServices, "ExpansionMode");

        // FIVE MODES, BECAUSE THE MODE IS A PER-REFERENCE PROPERTY RATHER THAN A PER-EXPRESSION ONE.
        //
        // The legacy specification documents an example mixing static and dynamic expansion INSIDE A
        // SINGLE EXPRESSION, so a per-expression mode flag would be unable to represent it.
        //
        // The distinction is mechanical. Static expansion (`$name`) substitutes the variable's value at
        // the moment the expression is SET - the parse routine rewrites the expression in place, so the
        // variable name is GONE from the stored text and no later assignment can reach it. Dynamic
        // expansion (`$$name`) leaves an entry whose index points into the live global-variable table,
        // so mutation propagates. The documented worked example fixes a static result at 5 permanently
        // while the dynamic one yields 6.
        //
        // This is precisely the distinction that "does not survive naive serialization": transmitting an
        // already-expanded string makes static bindings indistinguishable from literals and strips
        // dynamic bindings of their resolution environment.
        Assert.Equal(
            [
                ("EXPANSION_MODE_UNSPECIFIED", 0),
                ("EXPANSION_MODE_STATIC", 1),
                ("EXPANSION_MODE_DYNAMIC", 2),
                ("EXPANSION_MODE_DYNAMIC_INDIRECT", 3),
                ("EXPANSION_MODE_MACRO_DIRECT", 4),
                ("EXPANSION_MODE_MACRO_DYNAMIC", 5),
            ],
            ValuesOf(modes));
    }

    [Fact]
    public void TheExpressionPayloadCarriesTheUnexpandedTextAlongsideItsBindings()
    {
        MessageDescriptor data = Message(DataServices, "ColumnExpData");

        string[] fields = data.Fields.InDeclarationOrder()
            .Select(static field => field.Name).ToArray();

        // ALL THREE THINGS MUST TRAVEL, NOT JUST THE EXPRESSION.
        //
        // `columnexpdata` [n_cst_dwsvc_columnexp.sru:L22-L42] carries 19 fields including `exp` - the
        // SOURCE, UNEXPANDED expression text - plus the variable and function reference arrays. The
        // unexpanded text is what makes a static binding re-derivable and a dynamic one resolvable;
        // without it the payload would carry only the rewritten string, and static and literal would be
        // indistinguishable.
        Assert.Contains("exp", fields);
        Assert.Contains("vars", fields);
        Assert.Contains("fns", fields);

        // `dup_exps` IS THE MECHANISM BEHIND MULTIPLE EXPRESSIONS PER COLUMN, selected by WHICH column
        // changed. It looks redundant and is not.
        //
        // NOTE ON SPELLING: field names here are protobuf `snake_case`, not the legacy's run-together
        // PowerScript spelling. That is the one category of identifier this refactor deliberately does
        // NOT preserve verbatim, and the reason is that a field name is not a VALUE: protobuf's own
        // convention governs the wire, the generator maps `dup_exps` to `DupExps` in C# and to
        // `dupExps` in canonical JSON, and fighting that would produce a contract no standard tool
        // renders correctly. Constant VALUES and constant IDENTIFIER SPELLINGS are preserved verbatim -
        // as `SQL_MS_REPLACE`, `EID_ITEMCHANGE` and `CLC_UNKNOWN` in the enums above all show - because
        // those appear in serialized payloads and stored characterization comparisons. A field name
        // appears in neither.
        Assert.Contains("dup_exps", fields);

        // The per-expression behaviour flags, which the flag setter addresses.
        foreach (string flag in (string[])["always_calc", "recursive", "trigger_event", "cacheable"])
        {
            Assert.Contains(flag, fields);
        }

        // AND THE REVERSE DEPENDENCY INPUTS, which are what make dirty-propagation work.
        Assert.Contains("relative_col_ids", fields);
        Assert.Contains("relative_input_col_ids", fields);
    }

    [Fact]
    public void TheCalculationCacheIsTriStateRatherThanBoolean()
    {
        EnumDescriptor cache = FindEnum(DataServices, "CalcFlag");

        // UNKNOWN IS A REAL THIRD STATE, NOT AN ABSENCE.
        //
        // "not yet determined" is different from "determined not to be calculable". The tri-state cache
        // is what stops an expression being evaluated twice within one pass while still allowing a
        // genuinely-uncalculable expression to be distinguished from one nobody has looked at yet - and
        // collapsing it to a boolean would make the first pass and a negative result identical.
        Assert.Equal(
            [("CLC_UNKNOWN", 0), ("CLC_YES", 1), ("CLC_NO", 2)],
            ValuesOf(cache));
    }

    [Fact]
    public void AForeignVariableIsDistinguishedFromALocalOneOnTheWire()
    {
        EnumDescriptor varType = FindEnum(DataServices, "VarType");

        // `globalvardata.vartype` IS local=0 / foreign=1 [n_cst_dwsvc_columnexp.sru:L65-L71].
        //
        // The distinction has to be on the wire because the two resolve completely differently: a local
        // variable resolves within the session, while a foreign one requires a session-scoped DataWindow
        // handle and is subject to the co-residency limit. A payload that did not distinguish them would
        // make the blocked case undetectable until resolution failed.
        Assert.Equal([("VAR_LOCAL", 0), ("VAR_FOREIGN", 1)], ValuesOf(varType));
    }

    [Fact]
    public void ABlockedForeignReferenceHasItsOwnErrorCategory()
    {
        EnumDescriptor category = FindEnum(DataServices, "Category");

        string[] names = ValuesOf(category).Select(static value => value.Name).ToArray();

        // THE NARROWED CONTRACT GETS A NAMED ERROR RATHER THAN A GENERIC FAILURE.
        //
        // `foreignvardata.expsvc` is a live object pointer and cannot be serialized, so cross-session
        // foreign references are BLOCKED. A caller must be able to tell that outcome apart from a parse
        // error or an undefined name, because it is the one failure that says "this is a boundary of the
        // distributed contract" rather than "your input was wrong".
        Assert.Contains("CATEGORY_FOREIGN_REFERENCE_BLOCKED", names);

        // AND THE PARSE-ERROR CATEGORIES SURVIVE ALONGSIDE IT.
        //
        // The 28 live dialog sites in the legacy engine become structured errors preserving the
        // expression text and the caret position. A defect travels with them: those messages are
        // hardcoded Chinese and do NOT route through localization, unlike the equivalent messages
        // elsewhere in the DataWindow service layer which do. That inconsistency is reproduced.
        Assert.Contains("CATEGORY_PARSE", names);
        Assert.Contains("CATEGORY_EXPRESSION", names);
        Assert.Contains("CATEGORY_UNDEFINED_NAME", names);
    }

    // ==============================================================================================
    //  C-05..C-08 - PERSISTENCE
    // ==============================================================================================

    [Fact]
    public void QueryIsServerStreamingToReproduceProgressiveRecordsetDelivery()
    {
        MethodDescriptor query = Persistence
            .FindTypeByName<ServiceDescriptor>("QueryService")!
            .FindMethodByName("Query")!;

        Assert.False(query.IsClientStreaming);
        Assert.True(query.IsServerStreaming);
    }

    [Fact]
    public void TheClauseModificationStylesKeepTheirLegacyValues()
    {
        EnumDescriptor style = FindEnum(Persistence, "SqlModifyStyle");

        // SQL_MS_REPLACE=1, APPEND=2, PREPEND=3 [enums.sru:L718-L720].
        //
        // The identifier spellings are preserved for the same reason as every other constant in this
        // refactor - they appear in stored characterization comparisons. The values matter because the
        // where-clause and order-by setters dispatch on them, and because a zero index or an empty
        // clause must yield E_INVALID_ARGUMENT rather than being treated as one of the three.
        Assert.Equal(
            [
                ("SQL_MS_UNSPECIFIED", 0),
                ("SQL_MS_REPLACE", 1),
                ("SQL_MS_APPEND", 2),
                ("SQL_MS_PREPEND", 3),
            ],
            ValuesOf(style));
    }

    [Fact]
    public void OnlyTheTwoLegacyDatabaseTypesAreDeclaredAndSqliteIsNotAmongThem()
    {
        EnumDescriptor type = FindEnum(Persistence, "DatabaseType");

        // EXACTLY TWO, AND SQLITE IS DELIBERATELY ABSENT.
        //
        // n_cst_thread_trans.sru:L60-L61 declares SQL Server as 0 and Oracle as 1, and SQLITE IS NOT IN
        // THIS ENUMERATION AT ALL. The repository contains two mutually independent storage paths: the
        // SQLite binding, which is the only one with a real evidenced connection and the only DDL
        // anywhere in the repository; and this transaction-object path, which knows only these two.
        //
        // Neither SQL Server nor Oracle has any schema, connection string or DDL anywhere - only these
        // two constants and their two statement generators. So Persistence provisions SQLite ONLY, and
        // these two survive as PAGING-REWRITER STRATEGIES that are pure string transforms, fully
        // testable with no database instance of either kind. Adding SQLITE here would fabricate a
        // database the evidence does not support, which constraint C-E forbids.
        Assert.Equal([("DBT_MSSQL", 0), ("DBT_ORACLE", 1)], ValuesOf(type));
    }

    [Fact]
    public void TheUpdateContractCarriesAPerTableDescriptorBecauseMultiTableUpdateIsReal()
    {
        MessageDescriptor request = Message(Persistence, "PrepareUpdateRequest");

        FieldDescriptor? tables = request.Fields.InDeclarationOrder()
            .FirstOrDefault(static field => field.Name == "tables");

        Assert.True(tables is not null, "PrepareUpdateRequest must carry a repeated table contract.");

        // REPEATED, BECAUSE `Tables[]` IS AN ARRAY IN THE LEGACY.
        //
        // `_of_updateprepare` [n_cst_thread_task_sqlupdate.sru:L98-L145] does not trust the DataWindow's
        // static definition: it RESETS update, key and identity to off on every column and then
        // selectively re-enables from a table descriptor ARRAY, with its own reset and add paths. So
        // multi-table update from one DataWindow is a REAL legacy capability rather than a theoretical
        // one, and a singular descriptor would silently drop it.
        Assert.True(tables!.IsRepeated);
    }

    [Fact]
    public void TheUpdateResponseCarriesTheIdentityRoundTripAndTheThreeCounts()
    {
        MessageDescriptor response = Message(Persistence, "UpdateResponse");

        string[] fields = response.Fields.InDeclarationOrder()
            .Select(static field => field.Name).ToArray();

        // THE RESPONSE COMPOSES THREE NAMED SUB-MESSAGES RATHER THAN FLATTENING EVERYTHING.
        //
        // status / counts / identity. The composition is worth keeping: the counts and the identity
        // round-trip are independently meaningful - a caller may want one and not the other - and
        // flattening eleven scalars into one message would lose which of them belong together.
        Assert.Equal(["status", "counts", "identity"], fields);

        // THE THREE COUNTS.
        MessageDescriptor counts = Message(Persistence, "UpdateCounts");
        string[] countFields = counts.Fields.InDeclarationOrder()
            .Select(static field => field.Name).ToArray();

        foreach (string expected in (string[])["inserted", "updated", "deleted"])
        {
            Assert.Contains(expected, countFields);
        }

        // THE IDENTITY ROUND-TRIP IS PART OF THE CONTRACT.
        //
        // The legacy discovers the identity column by prefix-matching the lower-cased update table name
        // against each column's database name with a first-wins fallback, then collects values for
        // newly-modified rows from the primary buffer FORWARD and from the filter buffer BACKWARD
        // [n_cst_thread_task_sqlupdate.sru:L237] - because the filter buffer's row order is documented
        // as INVERTED relative to the source [:L235].
        //
        // That backward iteration is the most dangerous single line in this refactor for one-based to
        // zero-based translation: it LOOKS like a bug, it is NOT a bug, and "correcting" the direction
        // produces wrong identity values that a row-count assertion would not catch.
        // IdentityColumnData IS DECLARED IN common.v1, NOT IN persistence.v1.
        //
        // The identity round-trip is produced by C-06 and consumed by C-03, so the shape crosses two
        // contracts in two files. Declaring it in the shared vocabulary is what lets both name the same
        // descriptor; a per-file copy would have been two types that serialise alike until one of them
        // gained a field.
        MessageDescriptor identity = Message(Common, "IdentityColumnData");
        string[] identityFields = identity.Fields.InDeclarationOrder()
            .Select(static field => field.Name).ToArray();

        // THE TWO VALUE ARRAYS ARE THE ROUND-TRIP, and both must be present.
        //
        // The legacy fires a callback taking the identity column and TWO `ref` array out-parameters -
        // one collected from the primary buffer forward and one from the filter buffer backward. Two
        // arrays rather than one because they come from different buffers with different orderings, and
        // merging them would destroy the correspondence between a row and its generated key.
        Assert.True(
            identityFields.Count(static name =>
                name.Contains("value", StringComparison.OrdinalIgnoreCase)) >= 2,
            $"IdentityColumnData must carry both identity value arrays; it declares: "
                + $"{string.Join(", ", identityFields)}");
    }

    [Fact]
    public void TheTransactionDescriptorCarriesTheLoginPasswordOnRequestsOnly()
    {
        MessageDescriptor descriptor = Message(Persistence, "TransactionDescriptor");

        string[] fields = descriptor.Fields.InDeclarationOrder()
            .Select(static field => field.Name).ToArray();

        // THE REQUEST-SIDE DESCRIPTOR CARRIES IT, because a connection cannot be opened without it.
        Assert.Contains("logpass", fields);

        FieldDescriptor logpass = descriptor.Fields.InDeclarationOrder()
            .First(static field => field.Name == "logpass");
        Assert.Equal(5, logpass.FieldNumber);
    }

    [Fact]
    public void TheTransactionDescriptorViewHasNoPasswordFieldAndPermanentlyReservesItsSlot()
    {
        MessageDescriptor view = Message(Persistence, "TransactionDescriptorView");

        string[] fields = view.Fields.InDeclarationOrder()
            .Select(static field => field.Name).ToArray();

        // WRITE-ONLY, ENFORCED STRUCTURALLY RATHER THAN BY CONVENTION.
        //
        // `transactiondata.logpass` is a PASSWORD. The legacy copies the whole descriptor - INCLUDING
        // the password - on every transaction data read [n_cst_thread_trans.sru:L402-L419], so a
        // faithful single-message port would echo a password on every query, update and command
        // response, and into every log that recorded one. That is a C-F obligation as much as a design
        // choice.
        //
        // The resolution is two messages rather than one flag: the request-side descriptor carries the
        // password and the RESPONSE-side view HAS NO SUCH FIELD. A field that does not exist cannot be
        // populated by a careless implementation, cannot be logged, and cannot be added back by
        // accident - which a "do not serialize this" comment on a single message could not promise.
        Assert.DoesNotContain("logpass", fields);

        foreach (string field in fields)
        {
            Assert.False(
                field.Contains("pass", StringComparison.OrdinalIgnoreCase)
                    || field.Contains("secret", StringComparison.OrdinalIgnoreCase)
                    || field.Contains("credential", StringComparison.OrdinalIgnoreCase),
                $"TransactionDescriptorView.{field} looks like a credential. The response-side view "
                    + "must carry none.");
        }

        // SLOT 5 CARRIES NO FIELD - THE WIRE FACT, ASSERTED FROM THE DESCRIPTOR.
        //
        // This is the half that matters at runtime. Slot 5 is `logpass` on the request-side descriptor,
        // so a field occupying slot 5 on the view would be wire-compatible with it: an old client would
        // deserialize that field INTO its password slot, and a proxy replaying traffic would move data
        // into a password field. Nothing occupies it.
        Assert.DoesNotContain(5, view.Fields.InDeclarationOrder().Select(static field => field.FieldNumber));

        // AND THE SLOT IS PERMANENTLY RESERVED - THE SOURCE FACT, WHICH IS WHAT PREVENTS REUSE.
        //
        // Reserving the number as well as the name is what makes the guarantee durable rather than
        // incidental: without the reservation a future field could be GIVEN slot 5 and the descriptor
        // assertion above would then pass while the hazard returned. Reserving the name alone would not
        // prevent it either, since the danger is the NUMBER.
        //
        // A `reserved` declaration has no runtime representation that the public descriptor API exposes -
        // it constrains the compiler rather than describing a wire feature - so this half is necessarily
        // asserted against the protocol definition text.
        AssertReservationDeclared("persistence.v1.proto", "TransactionDescriptorView", "5", "logpass");
    }

    /// <summary>
    /// Asserts that a message reserves the given field number and field name in its protocol definition.
    /// </summary>
    /// <remarks>
    /// Skips rather than fails when the protocol definition cannot be located on disk, so the suite
    /// stays runnable from a packaged assembly where only the compiled descriptors are present. The
    /// wire-level half of the guarantee is asserted from the descriptor and does not skip.
    /// </remarks>
    private static void AssertReservationDeclared(
        string protoFileName,
        string messageName,
        string reservedNumber,
        string reservedName)
    {
        string? protoPath = LocateProtoFile(protoFileName);
        Assert.SkipUnless(
            protoPath is not null,
            $"{protoFileName} could not be located on disk from {AppContext.BaseDirectory}, so the "
                + "source-level `reserved` declaration cannot be inspected. The wire-level assertion "
                + "that no field occupies the slot is unaffected and has already run.");

        string text = File.ReadAllText(protoPath!);

        int messageStart = text.IndexOf($"message {messageName} {{", StringComparison.Ordinal);
        Assert.True(messageStart >= 0, $"'message {messageName}' was not found in {protoFileName}.");

        int messageEnd = text.IndexOf("\n}", messageStart, StringComparison.Ordinal);
        Assert.True(messageEnd > messageStart, $"The body of '{messageName}' could not be delimited.");

        string body = text[messageStart..messageEnd];

        Assert.Contains($"reserved {reservedNumber};", body, StringComparison.Ordinal);
        Assert.Contains($"reserved \"{reservedName}\";", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks upward from the test assembly looking for the repository's protocol definition directory.
    /// </summary>
    /// <remarks>
    /// An upward SEARCH rather than a fixed number of parent hops, deliberately: the number of hops
    /// between a test assembly and the repository root is a function of the configuration and target
    /// framework in the output path, so a fixed count silently breaks when either changes. The search
    /// anchors on two markers together - the protocol directory and the root solution file - so it
    /// cannot latch onto a same-named directory somewhere else on the machine.
    /// </remarks>
    private static string? LocateProtoFile(string protoFileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "shared", "PowerFramework.Contracts", "Proto", protoFileName);

            if (File.Exists(candidate)
                && File.Exists(Path.Combine(directory.FullName, "PowerFramework.slnx")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    [Fact]
    public void TheDatabaseErrorCarriesTheStructuredFieldsTheLegacyEventReported()
    {
        // `DbError` LIVES IN common.v1, NOT IN persistence.v1, AND THAT PLACEMENT IS CORRECT.
        //
        // Persistence RAISES a database error and DataServices FORWARDS one, so both files need to
        // describe the same shape. Declaring it twice would let the two drift; declaring it in the
        // shared vocabulary is what keeps a forwarded error byte-identical to the raised one.
        MessageDescriptor error = Message(Common, "DbError");

        string[] fields = error.Fields.InDeclarationOrder()
            .Select(static field => field.Name).ToArray();

        // `dberrordata` IS A LEGACY STRUCTURE AND ITS FIELDS ARE THE CONTRACT.
        //
        // sqldbcode, sqlerrtext, sqlsyntax, the offending buffer and the offending row. A caller
        // handling an update failure needs the buffer and row to point a user at the offending record,
        // so collapsing this to a message string would lose the only fields that make the error
        // actionable.
        Assert.Contains("sqldbcode", fields);
        Assert.Contains("sqlerrtext", fields);

        // AND THE STATEMENT FIELD IS THE ONE THAT MUST BE REDACTED.
        //
        // The legacy `sqlsyntax` carries the COMPLETE generated statement including interpolated
        // literals - and `DisableBind=1` means the runtime does not use bind variables at all
        // [n_cst_thread_task_sqlbase.sru:L128-L129], so those literals are real row data. The legacy
        // logger performs no redaction whatsoever. Gateway's 500 projection commits to redacting it,
        // which GatewayContractTests asserts on the published description.
        Assert.Contains(fields, static name =>
            name.Contains("syntax", StringComparison.OrdinalIgnoreCase)
            || name.Contains("statement", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheConflictRowCarriesBothCurrentAndOriginalValuesAsUpdateWhereOneRequires()
    {
        MessageDescriptor row = Message(Common, "ConflictRow");

        string[] fields = row.Fields.InDeclarationOrder()
            .Select(static field => field.Name).ToArray();

        // BOTH VALUE SETS TRAVEL, AND THIS IS THE SINGLE MOST IMPORTANT SHAPE IN THE UPDATE CONTRACT.
        //
        // `updatewhere=1` is the "key and updateable columns" concurrency mode: the generated statement's
        // where-clause carries the key column PLUS THE ORIGINAL VALUES of every updateable column. The
        // sole updatable DataWindow in the estate marks ALL SIX columns `updatewhereclause=yes`
        // [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14], so the check spans all six originals.
        //
        // This is why a naive rowset is insufficient and why the buffer anti-corruption layer exists: the
        // payload must carry, per row, BOTH the current and the original value of every marked column.
        // Carrying only current values would make the where-clause unreconstructable and the conflict
        // undiagnosable - the caller could see that something differed but not what it had been.
        Assert.Contains("current_values", fields);
        Assert.Contains("original_values", fields);

        // AND THE ROW IDENTIFIES WHICH BUFFER IT CAME FROM.
        //
        // A conflict can arise in the primary buffer or in the delete buffer, and the two mean different
        // things - a row someone else changed versus a row someone else already removed. The Filter
        // buffer matters too, since its ordering is inverted relative to the source.
        Assert.Contains("buffer", fields);
        Assert.Contains("item_status", fields);
        Assert.Contains("row", fields);

        // THE DETAIL SAYS HOW MANY ROWS WERE EXPECTED AND HOW MANY MATCHED.
        //
        // That pair is what makes the conflict actionable rather than merely reported: expected 1 and
        // matched 0 is a concurrency conflict, and it is distinguishable from a statement that matched
        // more rows than intended - which would be a different and more serious defect.
        MessageDescriptor detail = Message(Common, "ConflictDetail");
        string[] detailFields = detail.Fields.InDeclarationOrder()
            .Select(static field => field.Name).ToArray();

        Assert.Contains("rows", detailFields);
        Assert.Contains("rows_expected", detailFields);
        Assert.Contains("rows_matched", detailFields);
        Assert.Contains("update_table", detailFields);
    }

    // ==============================================================================================
    //  SHARED VOCABULARY IN common.v1
    // ==============================================================================================

    [Fact]
    public void TheThreeDataWindowBuffersAreNamedIndividually()
    {
        EnumDescriptor buffer = FindEnum(Common, "DwBuffer");

        // Primary!, Delete!, Filter! - THE LEGACY RESULT CARRIER IS A DATAWINDOW, NOT A ROWSET.
        //
        // `n_cst_thread_task_sqlbase_ds` derives from `datastore`, so the legacy carrier has buffers and
        // item statuses. A naive rowset would silently discard exactly the state the update contract
        // depends on - which is why the buffer anti-corruption layer exists rather than a direct map.
        //
        // The Filter buffer is the one whose row order is INVERTED relative to the source, which is why
        // the identity round-trip iterates it backwards.
        //
        // THE DOMAIN IS EXACTLY THREE MEMBERS AND IT IS NOT SHIFTED BY A SENTINEL.
        //
        // `dwbuffer` in the legacy is a THREE-VALUED enumerated type with no fourth state: there is no
        // "unspecified" buffer, and a request that named one would have nowhere to go. An
        // `UNSPECIFIED = 0` sentinel would therefore have added a state the legacy cannot represent AND
        // pushed `Primary` to 1, so every reader would have had to fold a wire number back onto the
        // legacy alphabet - and the first one that forgot would read Primary as Delete with no error
        // anywhere.
        //
        // Proto3's default for a scalar enum field is 0 and is indistinguishable from an unset field on
        // the wire, so the zero member is load-bearing: it is `PRIMARY`, which is the buffer every
        // legacy call site means when it does not say otherwise. Where "unspecified" is genuinely
        // meaningful the field is declared `optional` instead, which gives explicit presence without
        // spending a domain member on it - see RetrieveChunk.buffer and ColumnValue.item_status.
        Assert.Equal(
            [
                ("DW_BUFFER_PRIMARY", 0),
                ("DW_BUFFER_DELETE", 1),
                ("DW_BUFFER_FILTER", 2),
            ],
            ValuesOf(buffer));
    }

    [Fact]
    public void TheItemStatusMachineDistinguishesNewFromNewModified()
    {
        EnumDescriptor status = FindEnum(Common, "ItemStatus");

        string[] names = ValuesOf(status).Select(static value => value.Name).ToArray();

        // NEW AND NEW-MODIFIED ARE DIFFERENT STATES AND THE DIFFERENCE DRIVES STATEMENT GENERATION.
        //
        // A new-but-unmodified row generates nothing; a new-and-modified row generates an INSERT. The
        // identity round-trip collects values for NEWLY-MODIFIED rows specifically, so collapsing the
        // two would either lose inserts or fabricate them.
        Assert.Contains("ITEM_STATUS_NEW", names);
        Assert.Contains("ITEM_STATUS_NEW_MODIFIED", names);
        Assert.Contains("ITEM_STATUS_DATA_MODIFIED", names);
        Assert.Contains("ITEM_STATUS_NOT_MODIFIED", names);
    }

    [Fact]
    public void EveryProtoThreeEnumStartsAtZeroAsTheLanguageRequires()
    {
        // PROTO3 REQUIRES THE FIRST DECLARED VALUE TO BE ZERO, and it is the default for an unset field.
        //
        // Two of these enums are deliberate exceptions in SPIRIT rather than in form: `CalcFlag` and
        // `VarType` reuse the legacy's own zero value as a meaningful state - CLC_UNKNOWN and VAR_LOCAL
        // - rather than adding an UNSPECIFIED sentinel, because the legacy constants already occupy 0
        // and renumbering them would break stored comparisons. The rule still holds; what differs is
        // whether zero means "unset" or carries legacy meaning.
        foreach (FileDescriptor file in AllFiles)
        {
            IEnumerable<EnumDescriptor> enums = file.EnumTypes
                .Concat(file.MessageTypes.SelectMany(static message => message.EnumTypes));

            foreach (EnumDescriptor descriptor in enums)
            {
                Assert.Equal(0, descriptor.Values[0].Number);
            }
        }
    }

    [Fact]
    public void NoMessageOrFieldNameAnywhereLooksLikeEmbeddedKeyMaterial()
    {
        // C-F SELF-AUDIT ACROSS ALL THREE CONTRACT FILES.
        //
        // `logpass` is the one legitimate password-shaped field in the estate and it is confined to the
        // request-side transaction descriptor, verified above. Anything else with a credential-shaped
        // name would be a new secret channel created by this refactor, which is the opposite of the
        // remediation mandate.
        string[] permitted = ["logpass"];

        foreach (FileDescriptor file in AllFiles)
        {
            foreach (MessageDescriptor message in file.MessageTypes)
            {
                foreach (FieldDescriptor field in message.Fields.InDeclarationOrder())
                {
                    if (permitted.Contains(field.Name, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    foreach (string marker in (string[])["privatekey", "private_key", "signingkey", "signing_key", "apikey", "api_key"])
                    {
                        Assert.False(
                            field.Name.Replace("_", string.Empty)
                                .Contains(marker.Replace("_", string.Empty), StringComparison.OrdinalIgnoreCase),
                            $"{message.FullName}.{field.Name} names key material. Raw key material must "
                                + "never cross the wire; callers pass an opaque key reference that "
                                + "Security resolves against its configured key store.");
                    }
                }
            }
        }
    }

    // ==============================================================================================
    //  CROSS-CONTRACT SCALAR CONSISTENCY, PRESENCE, AND THE FIXED UPDATE PAIR
    //  --------------------------------------------------------------------------------------------
    //  Three guards over properties that are stated in the protocol definitions' comments and were
    //  previously true only by inspection. Each one failed silently before it was asserted: a width
    //  drift produced a working build with one `int` among a family of `long`s, a missing presence bit
    //  made an omitted field indistinguishable from a deliberate zero, and a published pair of flags
    //  offered callers behaviour the oracle does not have. None of the three is reachable through a
    //  serialization round trip, which is why they are asserted against the descriptors directly.
    // ==============================================================================================

    /// <summary>
    /// Every column identifier across the three protocol definitions is <c>int64</c>.
    /// </summary>
    /// <remarks>
    /// The legacy produces a column ordinal as <c>Long(dwo.ID)</c> - PowerBuilder <c>long</c> - at every
    /// site that reads one [<c>se_cst_dw.sru:L190</c>, <c>:L219</c>, <c>:L220</c>, <c>:L233</c>;
    /// <c>n_cst_dwsvc_columnexp.sru:L216</c>], and <c>common.v1.proto</c>'s normative scalar rule maps
    /// legacy <c>long</c> onto <c>int64</c>. One field once diverged to <c>int32</c>, which no compiler
    /// could catch because each file compiles independently: the generated C# simply gave that one field
    /// an <c>int</c> while every identifier beside it got a <c>long</c>. This asserts the family
    /// together, so a future narrowing of any member fails here rather than surfacing as a cast at a
    /// consumer.
    /// </remarks>
    [Fact]
    public void EveryColumnIdentifierFieldIsInt64SoTheFamilyCannotDriftApart()
    {
        // Message names are dotted where the contract nests - `ColumnSortState.ColumnSort` is declared
        // inside `ColumnSortState` - and the path is walked by ResolveMessage below rather than handed to
        // FileDescriptor.FindTypeByName, which returns null for any name containing a dot and would make
        // a nested member silently unverifiable.
        (FileDescriptor File, string Message, string Field)[] identifiers =
        [
            (Common, "ColumnValue", "column_id"),
            (Common, "IdentityColumnData", "identity_column_id"),
            (DataServices, "ColumnSortState.ColumnSort", "column_id"),
            (DataServices, "CalcResult", "column_id"),
            (DataServices, "DwObjectRef", "id"),
        ];

        foreach ((FileDescriptor file, string messageName, string fieldName) in identifiers)
        {
            MessageDescriptor? message = ResolveMessage(file, messageName);

            Assert.True(
                message is not null,
                $"Message '{messageName}' was not found in {file.Name}. The column-identifier family "
                    + "cannot be checked for consistency if one of its members has been renamed.");

            FieldDescriptor? field = message!.FindFieldByName(fieldName);

            Assert.True(
                field is not null,
                $"{messageName}.{fieldName} was not found in {file.Name}. The column-identifier family "
                    + "cannot be checked for consistency if one of its members has been renamed.");

            Assert.Equal(FieldType.Int64, field!.FieldType);
        }
    }

    /// <summary>
    /// Resolves a possibly-nested message by walking a dot-separated descriptor path.
    /// </summary>
    /// <remarks>
    /// <see cref="FileDescriptor.FindTypeByName{T}(string)"/> rejects any name containing a dot outright,
    /// so a nested message cannot be reached through it at all. Walking <see
    /// cref="MessageDescriptor.NestedTypes"/> segment by segment is what makes a nested declaration
    /// assertable, and returning null rather than throwing lets each caller phrase its own failure
    /// message.
    /// </remarks>
    private static MessageDescriptor? ResolveMessage(FileDescriptor file, string path)
    {
        string[] segments = path.Split('.');

        MessageDescriptor? current = file.FindTypeByName<MessageDescriptor>(segments[0]);

        foreach (string segment in segments.Skip(1))
        {
            current = current?.NestedTypes
                .FirstOrDefault(nested => nested.Name.Equals(segment, StringComparison.Ordinal));
        }

        return current;
    }

    /// <summary>
    /// The liveness-cache window has real presence, so absence is distinguishable from explicit zero.
    /// </summary>
    /// <remarks>
    /// The field carries a three-way rule: absent means "use the configured value, which defaults to the
    /// legacy 10000 ms" [<c>n_cst_thread_trans.sru:L198</c>], present and non-positive means "always
    /// probe", and present and positive means "use this window". A plain proto3 <c>int64</c> collapses the
    /// first two - its default is zero and an omitted field is indistinguishable from an explicit zero -
    /// so a caller that simply did not set the field would silently disable the cache the legacy has.
    /// That divergence is observable only through the <c>probed</c> flag on <c>IsConnectedResponse</c>,
    /// which is the signal a caller is least likely to assert on, so the guarantee is asserted here
    /// instead.
    /// </remarks>
    [Fact]
    public void TheLivenessCacheWindowHasExplicitPresenceSoAbsenceIsNotAnExplicitZero()
    {
        FieldDescriptor? window = Message(Persistence, "PoolKeepAliveSettings")
            .FindFieldByName("liveness_cache_window_ms");

        Assert.True(window is not null, "PoolKeepAliveSettings.liveness_cache_window_ms was not found.");
        Assert.Equal(FieldType.Int64, window!.FieldType);

        // THE ASSERTION THAT MATTERS. `HasPresence` is what generates HasLivenessCacheWindowMs, and it is
        // the only way the three-way rule above becomes expressible on the wire.
        Assert.True(
            window.HasPresence,
            "liveness_cache_window_ms must be declared proto3 `optional`. Without presence, an omitted "
                + "field and an explicit zero are the same bytes, and the contract assigns them OPPOSITE "
                + "meanings - the configured 10000 ms window versus never caching at all.");

        // AND ITS SIBLINGS DELIBERATELY DO NOT HAVE PRESENCE, because neither assigns a distinct meaning
        // to absence: keep_alive off is off, and the expire-seconds field carries the legacy's own
        // "non-positive means use the default" convention [n_cst_thread_trans_pool.sru:L78-L79], where
        // absence and zero genuinely coincide. Asserting the negative keeps `optional` a deliberate
        // signal rather than something sprinkled across the message.
        foreach (string sibling in (string[])["keep_alive", "keep_alive_expire_seconds", "transaction_class"])
        {
            FieldDescriptor? field = Message(Persistence, "PoolKeepAliveSettings").FindFieldByName(sibling);
            Assert.True(field is not null, $"PoolKeepAliveSettings.{sibling} was not found.");
            Assert.False(field!.HasPresence);
        }
    }

    /// <summary>
    /// The DataWindow update request publishes no accept-text or reset flag, and reserves both slots.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy update call is a single literal - <c>Data.Update(true,false)</c> at
    /// <c>n_cst_thread_task_sqlupdate.sru:L204</c> - with no argument reaching it from any caller, so
    /// there is no legacy behaviour in which either value differs. Publishing them as plain proto3 bools
    /// offered a choice the oracle does not have, and both wrong answers were silent: an omitted
    /// accept-text defaults to false and would SKIP AcceptText, submitting stale values while reporting
    /// success; and a reset would clear the item statuses and the original-value shadow that
    /// <c>updatewhere=1</c> compares across all six marked columns
    /// [<c>dw_sqlite.srd:L8-L14</c>], removing the ability to detect a conflict at all.
    /// </para>
    /// <para>
    /// Both halves are asserted. The wire fact - that nothing occupies slot 4 or 5 - is what matters at
    /// runtime. The source fact - that both numbers and both names are <c>reserved</c> - is what stops a
    /// later field being GIVEN slot 4, which would make the wire assertion pass while the hazard
    /// returned, because an old client's <c>accept_text</c> byte would deserialize into it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheUpdateRequestPublishesNoUpdateFlagsAndPermanentlyReservesBothSlots()
    {
        MessageDescriptor request = Message(DataServices, "UpdateRequest");

        FieldDescriptor[] fields = [.. request.Fields.InDeclarationOrder()];
        string[] names = [.. fields.Select(static field => field.Name)];

        Assert.DoesNotContain("accept_text", names);
        Assert.DoesNotContain("reset_flags", names);

        // Exactly the three fields that carry data, and nothing else.
        Assert.Equal(["datawindow_handle", "session_id", "rows"], names);

        int[] numbers = [.. fields.Select(static field => field.FieldNumber)];
        Assert.DoesNotContain(4, numbers);
        Assert.DoesNotContain(5, numbers);

        AssertReservationDeclared("dataservices.v1.proto", "UpdateRequest", "4, 5", "accept_text");
        AssertReservationDeclared("dataservices.v1.proto", "UpdateRequest", "4, 5", "reset_flags");
    }
}
