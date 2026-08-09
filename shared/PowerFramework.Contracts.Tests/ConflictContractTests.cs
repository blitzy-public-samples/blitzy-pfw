// ==================================================================================================
//  ConflictContractTests - THE CONFLICT PATH, ASSERTED AS A SHAPE AT BOTH ENDS
//  ------------------------------------------------------------------------------------------------
//  SUBJECTS    Proto/common.v1.proto      : ConflictDetail, ConflictRow, ColumnValue, AnyValue,
//                                           IdentityColumnData, RichErrorTrailer, RichErrorBinding
//              Proto/persistence.v1.proto : UpdateService, TableUpdateContract, PrepareUpdateRequest,
//                                           UpdateResponse, UpdateCounts, OperationStatus
//              OpenApi/gateway.v1.yaml    : the /v1/datawindow/** REST projection and its 409
//
//  ORACLE      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru   (409 lines)
//              ws_objects/pfw.tests.pbl.src/dw_sqlite.srd                          (the only
//                                                                   updatable DataWindow, rel 12.5)
//              ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs                   (5 fields)
//
//  WHAT THIS FILE ESTABLISHES
//  ------------------------------------------------------------------------------------------------
//  Four properties of the published boundary, each of which the legacy forces and none of which a
//  reviewer can confirm by reading a .proto quickly:
//
//    1. The conflict detail carries the CURRENT ROW STATE, structured per column, so a caller can
//       tell WHICH column moved underneath it and construct a retry.
//    2. The update reply exposes the three counts, the identity column, and THE TWO IDENTITY VALUE
//       ARRAYS AS TWO SEPARATE REPEATED FIELDS.
//    3. The table-update contract is REPEATED, because multi-table update from one DataWindow is a
//       real legacy capability rather than a theoretical one.
//    4. The conflict outcome is `Aborted` on the gRPC end and `409` on the REST end, with no
//       documented way to make the write proceed anyway.
//
//  THE KEY INSIGHT, AND THE REASON THIS SUITE EXISTS AT ALL
//  ------------------------------------------------------------------------------------------------
//  THE TWO IDENTITY ARRAYS LOOK LIKE DUPLICATION AND ARE NOT. They are two buffers whose row orders
//  run in OPPOSITE DIRECTIONS: the Primary buffer is collected FORWARD, `for nIndex = 1 to nCount`
//  [n_cst_thread_task_sqlupdate.sru:L228-L233], and the Filter buffer BACKWARD,
//  `for nIndex = nCount to 1 step -1` [:L237], because the filter buffer's row order is INVERTED
//  relative to the data source - the legacy says so in an inline comment immediately above the loop
//  [:L235].
//
//  Merging the two arrays, or "correcting" the backward iteration, produces WRONG IDENTITY VALUES
//  THAT PASS A ROW-COUNT ASSERTION: the same number of values comes back, paired with the wrong
//  rows. That is precisely why the property is pinned here, as a CONTRACT SHAPE, instead of being
//  left to a behavioural test downstream where the failure mode is invisible.
//
//  WHAT THIS FILE IS NOT
//  ------------------------------------------------------------------------------------------------
//  It is a SHAPE test, not a behavioural one, and not a Golden-Master comparison against the
//  PowerBuilder oracle. Nothing here executes an update, opens a connection, or touches a database -
//  AAP 0.2.2.5 forbids provisioning one and this suite needs none (constraint C-A/C-E). Every
//  assertion is either descriptor reflection over the generated Protobuf FileDescriptors or a read
//  of the already-parsed OpenAPI document supplied by the assembly fixture. No clock, no randomness,
//  no environment variable, no network, no I/O of its own.
//
//  GOVERNING CONSTRAINTS - AND THERE ARE NO USER RULES
//  ------------------------------------------------------------------------------------------------
//  `review_rules` returns exactly one line: "No user rules provided." Verified, twice. NO RULE
//  GOVERNS THIS FILE, and its absence is not latitude: the enterprise-standard baseline of AAP 0.7.2
//  applies in its place - nullable enabled, warnings as errors, no suppression added to make a test
//  compile, no secret literal anywhere. The binding constraints are the non-rule ones of AAP 0.7.3:
//
//    C-B  Replicate, never correct. `updatewhere` stays an INTEGER because the legacy field is a
//         `long` [:L15]; the two identity arrays stay TWO arrays because the legacy hands back two
//         and their orders differ [:L243]; the table contract stays REPEATED because the legacy
//         holds an array [:L30]. This suite asserts none of the tidier shapes.
//    C-K  Every row cites its locator, and the reason the Filter array is separate is stated in
//         prose at the assertion that depends on it rather than left to be rediscovered.
//    C-A  Shape only. No update execution, no database, no EF Core, no SQLite.
//    C-H  Nullable and warnings-as-errors inherited from Directory.Build.props, unrelaxed.
//
//  A NOTE ON OVERLAP WITH GatewayContractTests
//  ------------------------------------------------------------------------------------------------
//  That suite owns C-09 as an INGRESS document and asserts, among other things, that exactly one
//  operation carries a 409. This suite owns the CONFLICT PATH end to end, so it starts at the
//  Protobuf side that suite never reaches and then follows the REST `$ref` chain all the way down to
//  the member lists of ConflictDetail and ConflictRow, comparing them field for field against the
//  Protobuf messages they claim to mirror. The overlap is the single hand-off point, deliberately
//  asserted from both sides, because a projection that agreed with neither end would otherwise pass
//  both suites.
// ==================================================================================================

using System.Reflection;
using Google.Protobuf.Reflection;
using Microsoft.OpenApi;
using PowerFramework.Contracts.Common.V1;
using Xunit;
using Xunit.Sdk;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Contract-shape tests for the optimistic-concurrency conflict path: the conflict detail, the update
/// reply's counts and identity round trip, the repeated table-update contract, and the
/// <c>Aborted</c>-to-<c>409</c> status mapping at both ends.
/// </summary>
/// <remarks>
/// The OpenAPI document arrives through a constructor parameter, which is the one sanctioned way to
/// reach it in this folder: <c>OpenApiContractDocuments</c> is registered once for the whole assembly
/// with <c>[assembly: AssemblyFixture(...)]</c> in <c>ContractTestContext.cs</c>, so the two YAML
/// documents are parsed exactly once per run. Adding a class or collection fixture here, or a second
/// registration, would reparse them while looking correct.
/// </remarks>
public sealed class ConflictContractTests(OpenApiContractDocuments documents)
{
    // ==============================================================================================
    //  DESCRIPTOR ANCHORS
    //
    //  FULLY QUALIFIED, WITHOUT EXCEPTION, AND THAT IS A CORRECTNESS REQUIREMENT RATHER THAN A STYLE
    //  CHOICE. `UpdateRequest` and `UpdateResponse` are each declared in BOTH `persistence.v1` and
    //  `dataservices.v1`, and ContractDescriptors treats an ambiguous name as an error that names
    //  every candidate rather than returning a first match. A bare "UpdateResponse" here would
    //  therefore fail the lookup outright - which is the safe failure - but a bare "UpdateCounts",
    //  which happens to be unique today, would silently start resolving to the wrong contract the
    //  day a sibling declared one. Qualifying every name closes both cases at once.
    // ==============================================================================================

    private const string ConflictDetailName = "common.v1.ConflictDetail";
    private const string ConflictRowName = "common.v1.ConflictRow";
    private const string ColumnValueName = "common.v1.ColumnValue";
    private const string AnyValueName = "common.v1.AnyValue";
    private const string IdentityColumnDataName = "common.v1.IdentityColumnData";
    private const string RichErrorTrailerName = "common.v1.RichErrorTrailer";
    private const string RichErrorBindingName = "common.v1.RichErrorBinding";
    private const string TableUpdateContractName = "persistence.v1.TableUpdateContract";
    private const string PrepareUpdateRequestName = "persistence.v1.PrepareUpdateRequest";
    private const string UpdateResponseName = "persistence.v1.UpdateResponse";
    private const string UpdateCountsName = "persistence.v1.UpdateCounts";
    private const string OperationStatusName = "persistence.v1.OperationStatus";
    private const string UpdateServiceName = "persistence.v1.UpdateService";
    private const string UpdateMethodName = "Update";
    private const string PrepareUpdateMethodName = "PrepareUpdate";

    /// <summary>
    /// The canonical numeric gRPC status code for <c>Aborted</c>, which is the canonical mapping to
    /// HTTP 409 and the only status the concurrency contract uses.
    /// </summary>
    private const int AbortedStatusCode = 10;

    /// <summary>The REST route carrying the update projection, and the only one that can conflict.</summary>
    private const string UpdateRoute = "/v1/datawindow/update";

    /// <summary>Prefix of every route this suite treats as part of the DataWindow REST projection.</summary>
    private const string DataWindowRoutePrefix = "/v1/datawindow";

    /// <summary>The media type the problem-details bodies are declared under.</summary>
    private const string ProblemJson = "application/problem+json";

    /// <summary>The shared component response every conflicting operation must reference.</summary>
    private const string ConflictResponseComponent = "Conflict";

    /// <summary>The 409 body schema, a problem-details object specialised with the conflict detail.</summary>
    private const string ConflictProblemSchema = "ConflictProblemDetails";

    /// <summary>The projected conflict-detail schema, mirroring <c>common.v1.ConflictDetail</c>.</summary>
    private const string ConflictDetailSchema = "ConflictDetail";

    /// <summary>The member of the 409 body that carries the conflict detail.</summary>
    private const string ConflictMember = "conflict";

    // ==============================================================================================
    //  LOOKUP HELPERS - thin, so a failing row reports the descriptor problem rather than a
    //  NullReferenceException from inside a LINQ chain. Every one of these delegates to
    //  ContractDescriptors, whose Require* members already fail with a message naming every
    //  candidate they searched.
    // ==============================================================================================

    private static MessageDescriptor Message(string fullName) =>
        ContractDescriptors.RequireMessage(fullName);

    private static FieldDescriptor Field(string messageFullName, string fieldName) =>
        ContractDescriptors.RequireField(Message(messageFullName), fieldName);

    private static MethodDescriptor UpdateServiceMethod(string methodName) =>
        ContractDescriptors.RequireMethod(ContractDescriptors.RequireService(UpdateServiceName), methodName);

    /// <summary>
    /// Parses an expected <see cref="FieldType"/> from its spelling in a theory row.
    /// </summary>
    /// <remarks>
    /// The theory rows carry the expected field type as a STRING rather than as the enum itself, so
    /// every <c>TheoryData</c> type argument stays a primitive the test framework can serialize -
    /// which keeps the rows individually addressable in a test explorer and keeps the xunit analyzers
    /// quiet without a suppression. Parsing is case-SENSITIVE and throws on an unknown spelling, so a
    /// typo in a row fails loudly instead of silently comparing against a default.
    /// </remarks>
    private static FieldType ParseFieldType(string spelling) =>
        Enum.Parse<FieldType>(spelling, ignoreCase: false);

    // ==============================================================================================
    //  SECTION 1 - ConflictDetail CARRIES THE CURRENT ROW STATE
    //  --------------------------------------------------------------------------------------------
    //  AAP 0.6.3.8: on a mismatch the response carries a conflict detail with the CURRENT ROW STATE
    //  so callers can implement an explicit retry-or-surface policy, and THERE IS NO SILENT OVERWRITE
    //  ANYWHERE. A caller told only "409" cannot construct a retry: it would re-send the same stale
    //  original values and receive the same 409 for ever.
    //
    //  AAP 0.6.3.2 is where the per-column requirement comes from, and it is forced by the fixture
    //  rather than chosen. dw_sqlite.srd declares `update="COMPANY" updatewhere=1
    //  updatekeyinplace=no` and marks ALL SIX columns `update=yes updatewhereclause=yes`.
    //  `updatewhere=1` is the "key and updateable columns" concurrency mode, so the generated WHERE
    //  clause carries the key column PLUS THE ORIGINAL VALUE OF EVERY UPDATEABLE COLUMN - all six
    //  columns' original values. Hence the payload must transmit, PER ROW, BOTH the current and the
    //  original value of every marked column, and hence two repeated value sets per row rather than
    //  one.
    // ==============================================================================================

    [Fact]
    public void ConflictDetailIsDeclaredInTheCommonContractBecauseBothSiblingsReferenceIt()
    {
        MessageDescriptor detail = Message(ConflictDetailName);

        Assert.Equal(ConflictDetailName, detail.FullName);

        // IT LIVES IN common.v1 AND NOT IN A SERVICE FILE, AND THE ADMISSION TEST IS "BOTH SIBLINGS
        // REFERENCE IT". C-06's Update produces it and C-03's Update relays it, so a copy in each
        // service file would be free to drift - and this is exactly the payload whose drift is
        // invisible, because two copies would both carry rows plus a table name and only the MEANING
        // would have moved.
        Assert.Same(ContractDescriptors.Common, detail.File);
    }

    /// <summary>
    /// One row per <c>ConflictDetail</c> field: the message's whole declared surface.
    /// </summary>
    public static TheoryData<string, string, bool, string> ConflictDetailFieldRows() => new()
    {
        // fieldName, expected FieldType, expected IsRepeated, locator / reason
        {
            "rows",
            nameof(FieldType.Message),
            true,
            "One Update call submits a whole changeset, so more than one row can conflict in a "
                + "single failed attempt; reporting only the first would send a caller round the "
                + "retry loop once per conflicting row."
        },
        {
            "update_table",
            nameof(FieldType.String),
            false,
            "n_cst_thread_task_sqlupdate.sru:L30 holds TABLEDATA Tables[] as an ARRAY and :L364-L369 "
                + "loops it, so multi-table update from one DataWindow is real and \"which table "
                + "conflicted\" is a genuine question."
        },
        {
            "rows_expected",
            nameof(FieldType.Int64),
            false,
            "Stating expected and matched together keeps \"the row changed\" distinguishable from "
                + "\"the row was deleted\" without a second round trip."
        },
        {
            "rows_matched",
            nameof(FieldType.Int64),
            false,
            "The gap between matched and expected is the size of the mismatch, not merely its "
                + "existence."
        },
    };

    [Theory]
    [MemberData(nameof(ConflictDetailFieldRows))]
    public void ConflictDetailDeclaresTheFieldARetryNeeds(
        string fieldName,
        string expectedFieldType,
        bool expectedIsRepeated,
        string reason)
    {
        FieldDescriptor field = Field(ConflictDetailName, fieldName);

        Assert.Equal(ParseFieldType(expectedFieldType), field.FieldType);
        Assert.Equal(expectedIsRepeated, field.IsRepeated);

        // `reason` is carried into the assertion rather than left as a comment so a failing row
        // reports WHY the field has to exist, not merely that it does not.
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void ConflictDetailRowsAreConflictRowsSoEveryReportedRowCarriesItsOwnState()
    {
        FieldDescriptor rows = Field(ConflictDetailName, "rows");

        Assert.True(rows.IsRepeated);
        Assert.Equal(FieldType.Message, rows.FieldType);
        Assert.Equal(ConflictRowName, rows.MessageType.FullName);
    }

    /// <summary>
    /// The three members that IDENTIFY the conflicting row - which buffer, which row, and what the
    /// server believes the row's status is now.
    /// </summary>
    public static TheoryData<string, string, string> ConflictRowIdentityRows() => new()
    {
        // fieldName, expected FieldType, locator / reason
        {
            "buffer",
            nameof(FieldType.Enum),
            "WHICH BUFFER. dberrordata.srs:L7 carries the offending buffer as a `dwbuffer`, and the "
                + "update path reads all three - Primary! at n_cst_thread_task_sqlupdate.sru:L230 and "
                + "Filter! at :L238. `row` does NOT count the same way across buffers, so a row "
                + "ordinal without its buffer is not addressable."
        },
        {
            "row",
            nameof(FieldType.Int64),
            "WHICH ROW. dberrordata.srs:L8, a `long`, ONE-BASED like every legacy row ordinal; 0 means "
                + "\"no particular row\", which is what n_cst_thread_task_sqlupdate.sru:L190 passes "
                + "for the synthesized \"no updatable table\" error."
        },
        {
            "item_status",
            nameof(FieldType.Enum),
            "THE ROW'S OWN STATUS, read the legacy way with column index ZERO - "
                + "GetItemStatus(nRow, 0, Primary!) at n_cst_thread_task_sqlupdate.sru:L160 and :L230. "
                + "Column 0 is not a real column; it means THE ROW ITSELF."
        },
    };

    [Theory]
    [MemberData(nameof(ConflictRowIdentityRows))]
    public void ConflictRowIdentifiesTheRowItIsReportingOn(
        string fieldName,
        string expectedFieldType,
        string reason)
    {
        FieldDescriptor field = Field(ConflictRowName, fieldName);

        Assert.Equal(ParseFieldType(expectedFieldType), field.FieldType);

        // Singular, not repeated: each ConflictRow describes exactly one row. A repeated identifier
        // would make the row's own identity ambiguous.
        Assert.False(field.IsRepeated);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    /// <summary>
    /// The two value sets, which are the whole diagnostic content of the message.
    /// </summary>
    public static TheoryData<string, string> ConflictRowValueSetRows() => new()
    {
        {
            "current_values",
            "The CURRENT server-side values of the marked columns - the state a retry would be "
                + "rebased onto."
        },
        {
            "original_values",
            "The values the caller believed were current: the ones that formed the failed statement's "
                + "WHERE clause under updatewhere=1 [dw_sqlite.srd table spec]. Comparing these "
                + "against current_values is what tells a caller WHICH column changed underneath it."
        },
    };

    [Theory]
    [MemberData(nameof(ConflictRowValueSetRows))]
    public void ConflictRowCarriesEachValueSetAsAStructuredPerColumnCollection(
        string fieldName,
        string reason)
    {
        FieldDescriptor field = Field(ConflictRowName, fieldName);

        // REPEATED, because the check spans EVERY marked column and not one of them. On the primary
        // fixture that is all six [dw_sqlite.srd: six columns, each `update=yes
        // updatewhereclause=yes`].
        Assert.True(field.IsRepeated);

        // STRUCTURED, NOT AN OPAQUE STRING BLOB. This is the assertion AAP 0.6.3.2 forces: the
        // payload must express, per row, BOTH the current and the original value of EVERY marked
        // column. A single string could carry neither the per-column boundary nor the per-column
        // type, so a caller could not identify which column moved - which is the one thing the 409
        // exists to tell it. The field type is checked explicitly against String as well as for
        // Message, so a future change that flattened this to a serialized blob fails here rather
        // than passing quietly.
        Assert.Equal(FieldType.Message, field.FieldType);
        Assert.NotEqual(FieldType.String, field.FieldType);
        Assert.NotEqual(FieldType.Bytes, field.FieldType);

        Assert.Equal(ColumnValueName, field.MessageType.FullName);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void CurrentAndOriginalValuesAreTwoDistinctFieldsRatherThanOneReusedSlot()
    {
        FieldDescriptor current = Field(ConflictRowName, "current_values");
        FieldDescriptor original = Field(ConflictRowName, "original_values");

        // TWO FIELDS, NOT ONE WITH A DISCRIMINATOR. Both sets travel on the SAME message for the same
        // row, at the same time, so they cannot share a slot and cannot be arms of a oneof: a caller
        // needs to compare them against each other. AAP 0.6.3.2 is explicit that the payload carries
        // both per row.
        Assert.NotEqual(current.FieldNumber, original.FieldNumber);
        Assert.Null(current.ContainingOneof);
        Assert.Null(original.ContainingOneof);

        // Same element type, so the comparison is like-for-like rather than across two shapes.
        Assert.Equal(original.MessageType.FullName, current.MessageType.FullName);
    }

    [Fact]
    public void AColumnValueDecomposesIntoAnIdentifiedColumnAndATypedValue()
    {
        // THE STRUCTURE CLAIM, CHECKED ONE LEVEL DOWN. Asserting that the value sets are message-typed
        // only proves they are not strings; it does not prove the message is a column/value pair. So
        // the pair is asserted here: a column identified BOTH by name and by its one-based ordinal,
        // plus the value itself.
        FieldDescriptor columnName = Field(ColumnValueName, "column_name");
        FieldDescriptor columnId = Field(ColumnValueName, "column_id");
        FieldDescriptor value = Field(ColumnValueName, "value");

        Assert.Equal(FieldType.String, columnName.FieldType);

        // int64, matching every sibling identifier in the boundary. The legacy produces this ordinal
        // as `Long(dwo.ID)` - PowerBuilder `long` - and one-based, which is why 0 is reserved for
        // "the row itself" [n_cst_thread_task_sqlupdate.sru:L160 versus :L162].
        Assert.Equal(FieldType.Int64, columnId.FieldType);

        Assert.Equal(FieldType.Message, value.FieldType);
        Assert.Equal(AnyValueName, value.MessageType.FullName);
    }

    [Fact]
    public void AConflictValueIsADiscriminatedUnionSoNullNeverCollapsesIntoZeroOrEmpty()
    {
        MessageDescriptor anyValue = Message(AnyValueName);

        // A DISCRIMINATED UNION, NOT A STRINGLY-TYPED MAP. The type travels WITH the value, so a
        // string is ONE ARM AMONG MANY rather than the only representation - which is the precise
        // form of "structured, not one opaque string".
        OneofDescriptor kind = Assert.Single(anyValue.Oneofs);

        FieldDescriptor stringArm = Field(AnyValueName, "string_value");
        Assert.Same(kind, stringArm.ContainingOneof);

        // AND THERE IS AN EXPLICIT NULL ARM. This matters to the conflict path specifically: under
        // updatewhere=1 the failed statement compared original values, and NULL AND EMPTY ARE
        // DISTINCT in that comparison. Collapsing them would match rows the legacy would not,
        // silently widening the WHERE clause and overwriting a row that should have conflicted -
        // the exact silent overwrite this whole contract exists to prevent.
        FieldDescriptor nullArm = Field(AnyValueName, "is_null");
        Assert.Same(kind, nullArm.ContainingOneof);

        // More than one non-null arm, or the "union" would be a single type wearing a union's clothes.
        Assert.True(
            anyValue.Fields.InDeclarationOrder().Count(field => field.ContainingOneof == kind) > 2,
            "common.v1.AnyValue must offer several typed arms; a union with one value arm plus a null "
                + "arm would be a nullable scalar, and the legacy parameter it models is typed `any`.");
    }

    // ==============================================================================================
    //  SECTION 1b - ConflictDetail IS REACHABLE FROM THE UPDATE PATH
    //  --------------------------------------------------------------------------------------------
    //  A conflict is reported as the gRPC status ABORTED, and a status carries only a code and a
    //  message - neither can hold ConflictDetail. So the boundary declares exactly ONE retrieval
    //  mechanism, machine-readably: a versioned binary trailer carrying RichErrorTrailer, bound to the
    //  RPC by a custom method option naming the key, the status code and the payload type.
    //
    //  THE DETAIL TRAVELS ON THE STATUS AND NOT AS A RESPONSE FIELD, DELIBERATELY. A conflict is not a
    //  successful call with a bad outcome, and a conflict field on UpdateResponse as well would create
    //  two ways to report one condition - a consumer could then handle one and miss the other.
    // ==============================================================================================

    [Fact]
    public void TheConflictDetailIsCarriedByTheRichErrorTrailerRatherThanInventedPerService()
    {
        FieldDescriptor conflict = Field(RichErrorTrailerName, "conflict");

        Assert.Equal(FieldType.Message, conflict.FieldType);
        Assert.Equal(ConflictDetailName, conflict.MessageType.FullName);

        // ONE KIND OF FAILURE AT A TIME. conflict and db_error are arms of one oneof rather than two
        // independent optional fields, because a response carrying both would leave a client to guess
        // which to act on.
        Assert.NotNull(conflict.ContainingOneof);
        FieldDescriptor dbError = Field(RichErrorTrailerName, "db_error");
        Assert.Same(conflict.ContainingOneof, dbError.ContainingOneof);

        // A CLIENT THAT ONLY WANTS TO CLASSIFY THE FAILURE NEED NOT DECODE THE ONEOF. The reconciled
        // outcome code travels alongside, outside the union.
        FieldDescriptor retCode = Field(RichErrorTrailerName, "ret_code");
        Assert.Null(retCode.ContainingOneof);
    }

    [Fact]
    public void TheUpdateMethodDeclaresHowToRetrieveTheConflictDetailOnItsOwnDescriptor()
    {
        MethodDescriptor update = UpdateServiceMethod(UpdateMethodName);
        MethodOptions? options = update.GetOptions();

        Assert.NotNull(options);
        Assert.True(
            options.HasExtension(CommonV1Extensions.RichError),
            $"persistence.v1.UpdateService/{UpdateMethodName} must declare the (common.v1.rich_error) "
                + "option. Without it a client cannot discover that the method can fail with a "
                + "conflict, nor which trailer to read - and the retry-or-surface policy the "
                + "concurrency contract requires becomes undecidable.");

        RichErrorBinding binding =
            options.GetExtension(CommonV1Extensions.RichError);

        // THE PAYLOAD TYPE IS THE TRAILER, WHICH IS WHAT MAKES ConflictDetail REACHABLE FROM HERE.
        Assert.Equal(RichErrorTrailerName, binding.PayloadType);

        // THE KEY MUST END `-bin`. That is not decoration: gRPC treats a metadata key ending in `-bin`
        // as BINARY and base64-transports it, while a key without the suffix may carry ASCII only - so
        // a protobuf payload under a non-`-bin` key would be corrupted in transit.
        Assert.EndsWith("-bin", binding.TrailerKey, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  SECTION 2 - THE UPDATE REPLY : THE COUNTS, THE IDENTITY COLUMN, AND THE TWO ARRAYS
    //  --------------------------------------------------------------------------------------------
    //  Three callbacks on the legacy worker define this reply, and the reply must expose all three:
    //
    //      OnUpdated(insertedCount, updatedCount, deletedCount)              [:L247]
    //      OnIdentityColumnDataRetrieved(nIdentityColumn,
    //                                   ref nPrimaryIdValues,
    //                                   ref nFilterIdValues)                 [:L243]
    //      ... plus the reconciled outcome, which is neither of the above
    //
    //  THE IDENTITY BLOCK IS CONDITIONAL AT THREE LEVELS, so its absence means "none collected" and is
    //  NOT an error: rows must have been inserted at all [:L215], an identity column must have been
    //  found [:L226], and at least one of the two arrays must be non-empty [:L242-L244].
    // ==============================================================================================

    [Fact]
    public void TheUpdateMethodRepliesWithTheMessageThatCarriesTheRoundTrip()
    {
        MethodDescriptor update = UpdateServiceMethod(UpdateMethodName);

        Assert.Equal(UpdateResponseName, update.OutputType.FullName);

        // UNARY IN BOTH DIRECTIONS. One changeset in, one reconciled outcome out. Streaming either way
        // would imply partial results a caller could act on, and the legacy has no such notion here:
        // the update either reached the value 1 [:L214] or fell through to a database error [:L250].
        Assert.False(update.IsClientStreaming);
        Assert.False(update.IsServerStreaming);
    }

    /// <summary>
    /// One row per count. The legacy fires all three as one callback with three arguments, so they are
    /// one message here rather than three fields scattered on the reply.
    /// </summary>
    public static TheoryData<string> UpdateCountRows() => new()
    {
        "inserted",
        "updated",
        "deleted",
    };

    [Theory]
    [MemberData(nameof(UpdateCountRows))]
    public void TheUpdateReplyCarriesEachRowCountAsItsOwnNumericField(string fieldName)
    {
        FieldDescriptor count = Field(UpdateCountsName, fieldName);

        // NUMERIC, AND int64 SPECIFICALLY. The legacy sources these from of_GetInsertedCount,
        // of_GetUpdatedCount and of_GetDeletedCount [:L247], all PowerBuilder `long`, which the
        // boundary's scalar rule widens to int64. A narrower arm would be a silent narrowing at the one
        // place a caller reconciles what it sent against what landed.
        Assert.Equal(FieldType.Int64, count.FieldType);
        Assert.False(count.IsRepeated);
    }

    [Fact]
    public void TheThreeCountsAreDeliveredTogetherBecauseTheLegacyFiresThemTogether()
    {
        MessageDescriptor counts = Message(UpdateCountsName);

        // EXACTLY THREE, MATCHING THE CALLBACK'S ARITY [:L247]. A fourth field here would be a
        // capability the legacy never reported.
        Assert.Equal(3, counts.Fields.InDeclarationOrder().Count);

        FieldDescriptor onReply = Field(UpdateResponseName, "counts");
        Assert.Equal(FieldType.Message, onReply.FieldType);
        Assert.Equal(UpdateCountsName, onReply.MessageType.FullName);
    }

    /// <summary>
    /// The identity round trip: the discovered column, then the two value arrays.
    /// </summary>
    /// <remarks>
    /// The two arrays are separate rows here for the same reason they are separate fields in the
    /// contract - see <see cref="TheTwoIdentityArraysAreTwoDistinctRepeatedFieldsAndMustNeverBeMerged"/>
    /// for the full statement of why.
    /// </remarks>
    public static TheoryData<string, bool, string> IdentityRoundTripRows() => new()
    {
        // fieldName, expected IsRepeated, locator / reason
        {
            "identity_column_id",
            false,
            "THE COLUMN INDEX, not a name: the legacy passes `nIdentityColumn`, a `long` ORDINAL, as "
                + "the callback's first argument [n_cst_thread_task_sqlupdate.sru:L243]. It is "
                + "discovered at RUN TIME by lowercasing the update table name, appending a dot and "
                + "prefix-matching that against each column's #N.DBName, WITH A FIRST-WINS FALLBACK "
                + "[:L217-L225, fallback at :L221] - so it can differ from the identity column named "
                + "in the request's table contract, and a consumer must READ it rather than assume it."
        },
        {
            "primary_values",
            true,
            "Collected from the PRIMARY buffer FORWARD, `for nIndex = 1 to nCount` [:L228-L233], for "
                + "rows whose ROW status (column index 0) is NewModified! [:L230], read with "
                + "GetItemNumber - hence a numeric array."
        },
        {
            "filter_values",
            true,
            "Collected from the FILTER buffer BACKWARD, `for nIndex = nCount to 1 step -1` [:L237], "
                + "because the filter buffer's row order is INVERTED relative to the data source - the "
                + "legacy states exactly that in an inline comment immediately above the loop [:L235]."
        },
    };

    [Theory]
    [MemberData(nameof(IdentityRoundTripRows))]
    public void TheIdentityRoundTripExposesTheColumnAndItsCollectedValues(
        string fieldName,
        bool expectedIsRepeated,
        string reason)
    {
        FieldDescriptor field = Field(IdentityColumnDataName, fieldName);

        // ALL THREE ARE NUMERIC. The legacy reads the values with GetItemNumber [:L231, :L239] and
        // carries the column as an ordinal, so nothing here is a string.
        Assert.Equal(FieldType.Int64, field.FieldType);
        Assert.Equal(expectedIsRepeated, field.IsRepeated);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void TheTwoIdentityArraysAreTwoDistinctRepeatedFieldsAndMustNeverBeMerged()
    {
        FieldDescriptor primary = Field(IdentityColumnDataName, "primary_values");
        FieldDescriptor filter = Field(IdentityColumnDataName, "filter_values");

        // ==========================================================================================
        //  WHY THESE TWO ARRAYS CANNOT BE MERGED. THIS IS THE ASSERTION THIS WHOLE FILE EXISTS FOR.
        //
        //  The legacy delivers them through ONE callback with TWO `ref` ARRAY OUT-PARAMETERS -
        //      OnIdentityColumnDataRetrieved(nIdentityColumn,
        //                                    ref nPrimaryIdValues,
        //                                    ref nFilterIdValues)                        [:L243]
        //  - because they come from DIFFERENT BUFFERS COLLECTED IN OPPOSITE DIRECTIONS:
        //
        //      Primary buffer, FORWARD:   for nIndex = 1 to nCount             [:L228-L233]
        //      Filter  buffer, BACKWARD:  for nIndex = nCount to 1 step -1     [:L237]
        //
        //  The justification sits in the source immediately above the backward loop [:L235]: THE
        //  FILTER BUFFER'S ROW ORDER IS INVERTED relative to the data source that produced the
        //  changeset.
        //
        //  IT LOOKS LIKE A BUG. IT IS NOT A BUG. Merging the arrays, or "correcting" the backward
        //  iteration, produces WRONG IDENTITY VALUES THAT A ROW-COUNT ASSERTION WOULD NOT CATCH: the
        //  same number of values comes back, paired with the wrong rows. AAP 0.4.5.4 and 0.6.3.6 name
        //  this the single most dangerous line in the refactor for one-based-to-zero-based
        //  translation, and constraint C-B forbids tidying it.
        //
        //  A merged list would also destroy the only evidence a consumer has of which order each set
        //  was collected in, and the relay is order-preserving BY CONTRACT: C-03 forwards C-06's
        //  payload and Gateway forwards C-03's, each element for element in the order received.
        //  Re-sorting either array, deduplicating it, or concatenating the two is a contract
        //  violation, not an optimisation.
        // ==========================================================================================

        // TWO FIELDS.
        Assert.NotEqual(primary.FieldNumber, filter.FieldNumber);
        Assert.NotEqual(filter.Name, primary.Name);

        // BOTH REPEATED - this is the row that fails if either is ever collapsed to a scalar.
        Assert.True(
            primary.IsRepeated,
            "common.v1.IdentityColumnData.primary_values must be repeated: the Primary buffer can "
                + "yield an identity value for every newly-inserted row [:L228-L233].");
        Assert.True(
            filter.IsRepeated,
            "common.v1.IdentityColumnData.filter_values must be repeated AND must remain a SEPARATE "
                + "field from primary_values. The Primary set is collected forward and the Filter set "
                + "backward, because the filter buffer's row order is inverted relative to the source "
                + "[n_cst_thread_task_sqlupdate.sru:L235-L241]. A single merged array would silently "
                + "reorder identity values in a way a row-count assertion could not detect.");

        // NEITHER IS AN ARM OF A ONEOF, so a reply can carry both at once - which it must, since a
        // single Update can insert into both buffers and the legacy fires the callback when EITHER is
        // non-empty [:L242-L244].
        Assert.Null(primary.ContainingOneof);
        Assert.Null(filter.ContainingOneof);
    }

    [Fact]
    public void TheReplyCarriesTheSharedIdentityBlockSoTheRelayCannotDrift()
    {
        FieldDescriptor identity = Field(UpdateResponseName, "identity");

        Assert.Equal(FieldType.Message, identity.FieldType);

        // THE SHARED DEFINITION IN common.v1, NOT A PRIVATE COPY IN persistence.v1. C-03's own
        // UpdateResponse RELAYS this exact payload to Gateway, so there are two consumers and
        // therefore one definition. Two copies would be free to drift in the one way that is
        // invisible: both would carry two int64 arrays and only the ORDER would differ.
        Assert.Equal(IdentityColumnDataName, identity.MessageType.FullName);
        Assert.Same(ContractDescriptors.Common, identity.MessageType.File);
    }

    // ==============================================================================================
    //  SECTION 2b - THE OUTCOME IS A RECONCILED CODE, NEVER A BARE BOOLEAN
    //  --------------------------------------------------------------------------------------------
    //  Two verified behaviours invert what a straightforward port would produce, and a boolean can
    //  express neither:
    //
    //    1. SUCCESS IS THE VALUE 1, NOT THE ZERO OF THE RETURN-CODE ALGEBRA. The gate on the entire
    //       success path is `if rtCode = 1 then` [:L214], with anything else falling through to
    //       E_DB_ERROR [:L249-L250].
    //    2. A CLAIMED SUCCESS IS DEFENSIVELY REWRITTEN INTO A FAILURE:
    //           if TransObject.SQLCode = -1 and rtCode = 1 then rtCode = -1        [:L208-L210]
    //       An implementation that trusts the update call's own return value REPORTS SUCCESS ON A
    //       FAILED UPDATE.
    //
    //  So the reply must be able to express "reported success, then overridden", and it must be able
    //  to distinguish CANCELLED - which the legacy algebra treats as NEITHER succeeded nor failed -
    //  from a database error, because the vetoable before-update hook produces one or the other
    //  depending on the transaction's own failure predicate [:L195-L202]. A boolean has two states and
    //  this contract needs at least four.
    // ==============================================================================================

    [Fact]
    public void TheUpdateReplyReportsAReconciledOutcomeCodeRatherThanASuccessFlag()
    {
        FieldDescriptor status = Field(UpdateResponseName, "status");

        Assert.Equal(FieldType.Message, status.FieldType);
        Assert.Equal(OperationStatusName, status.MessageType.FullName);

        // THE OUTCOME IS AN ENUMERATED CODE, NOT A FLAG.
        FieldDescriptor retCode = Field(OperationStatusName, "ret_code");
        Assert.Equal(FieldType.Enum, retCode.FieldType);
        Assert.Equal("common.v1.RetCode.Value", retCode.EnumType.FullName);

        // AND THE CODE SPACE IS RICH ENOUGH FOR THE ARMS THE LEGACY ACTUALLY PRODUCES. CANCELLED and
        // E_DB_ERROR must both exist, or the veto/failure discrimination at [:L195-L202] would have to
        // be flattened - and CANCELLED cannot be folded into either success or failure, because the
        // legacy predicates classify it as neither.
        string[] declared = retCode.EnumType.Values.Select(static value => value.Name).ToArray();
        Assert.Contains("CANCELLED", declared);
        Assert.Contains("E_DB_ERROR", declared);
    }

    [Fact]
    public void NoFieldOnTheUpdateReplyIsABooleanSoSuccessCannotBeReducedToAFlag()
    {
        MessageDescriptor reply = Message(UpdateResponseName);

        string[] booleans = reply.Fields.InDeclarationOrder()
            .Where(static field => field.FieldType == FieldType.Bool)
            .Select(static field => field.Name)
            .ToArray();

        Assert.Empty(booleans);
    }

    [Fact]
    public void TheConflictIsNotAlsoAFieldOnTheReplySoThereIsExactlyOneWayToReportIt()
    {
        MessageDescriptor reply = Message(UpdateResponseName);

        // THE DETAIL TRAVELS ON THE STATUS, AND ONLY THERE. A conflict is not a successful call with a
        // bad outcome. Giving the reply a conflict field as well would create two ways to report one
        // condition, and a consumer could then handle one and miss the other - which on this
        // particular condition means proceeding as though the write had succeeded.
        FieldDescriptor[] conflictShaped = reply.Fields.InDeclarationOrder()
            .Where(static field =>
                field.FieldType == FieldType.Message
                && string.Equals(field.MessageType.FullName, "common.v1.ConflictDetail", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(conflictShaped);
    }

    // ==============================================================================================
    //  SECTION 3 - PrepareUpdate : THE REPEATED SIX-FIELD TABLE CONTRACT
    //  --------------------------------------------------------------------------------------------
    //  MULTI-TABLE UPDATE FROM ONE DATAWINDOW IS A REAL LEGACY CAPABILITY, NOT A THEORETICAL ONE, and
    //  the evidence is fourfold rather than inferred:
    //
    //    1. The legacy holds `TABLEDATA Tables[]` as an ARRAY                        [:L30]
    //    2. There is an explicit add path taking all six descriptor fields and appending at
    //       UpperBound + 1                                                           [:L46, :L82-L96]
    //    3. There is an explicit multi-table switch                                  [:L51, :L265-L268]
    //    4. With the switch on, the worker LOOPS THE ARRAY IN ARRAY ORDER, calling prepare and then
    //       update once per table, and STOPS AT THE FIRST FAILURE                    [:L364-L369]
    //
    //  AND _of_updateprepare DOES NOT TRUST THE DATAWINDOW'S STATIC DEFINITION: it first turns Update,
    //  Key and Identity OFF on EVERY column by ordinal [:L103-L108] and only then re-enables the ones
    //  the descriptor names [:L111-L129]. A column marked updatable in the carrier's own definition
    //  but absent from the descriptor is therefore NOT updatable for that operation.
    // ==============================================================================================

    [Fact]
    public void PrepareUpdateCarriesTheTableContractAsARepeatedFieldSoMultiTableUpdateSurvives()
    {
        MethodDescriptor prepare = UpdateServiceMethod(PrepareUpdateMethodName);
        Assert.Equal(PrepareUpdateRequestName, prepare.InputType.FullName);

        FieldDescriptor tables = Field(PrepareUpdateRequestName, "tables");

        // THE SINGLE MOST IMPORTANT ROW IN THIS SUITE.
        //
        // If this field were singular, multi-table update - a capability the legacy demonstrably has -
        // would be unreachable across the boundary, and the omission would be invisible because every
        // single-table caller would keep working. That is the failure mode a shape test catches and a
        // behavioural test on the primary fixture does not, since the primary fixture targets one
        // table.
        Assert.True(
            tables.IsRepeated,
            "persistence.v1.PrepareUpdateRequest.tables MUST be repeated. The legacy holds the update "
                + "contract as an ARRAY - `TABLEDATA Tables[]` "
                + "[n_cst_thread_task_sqlupdate.sru:L30] - with an add path appending at UpperBound + 1 "
                + "[:L86] and a worker that loops it in array order, preparing and updating once per "
                + "table [:L364-L369]. A singular field would silently drop multi-table update.");

        Assert.Equal(FieldType.Message, tables.FieldType);
        Assert.Equal(TableUpdateContractName, tables.MessageType.FullName);

        // THE FLAG TRAVELS TOO, because the legacy has one and it changes behaviour: with it OFF the
        // descriptor array is NOT APPLIED AT ALL - _of_updateprepare has exactly one caller, inside the
        // multi-table branch [:L365], while the single-table branch calls the update directly [:L371].
        // That looks like an oversight in the legacy; it is the observable behaviour, so the contract
        // carries the switch rather than "fixing" it into applying the descriptor in both modes (C-B).
        FieldDescriptor multiTable = Field(PrepareUpdateRequestName, "multi_table_update");
        Assert.Equal(FieldType.Bool, multiTable.FieldType);
    }

    /// <summary>
    /// One row per legacy <c>tabledata</c> field, in the structure's own declaration order.
    /// </summary>
    /// <remarks>
    /// The oracle is <c>n_cst_thread_task_sqlupdate.sru:L10-L17</c>, which declares exactly six fields:
    /// <c>string name</c>, <c>string updatablecolumns[]</c>, <c>string keycolumns[]</c>,
    /// <c>string identitycolumn</c>, <c>long updatewhere</c> and <c>boolean updatekeyinplace</c>.
    /// </remarks>
    public static TheoryData<string, string, bool, bool, string> TableUpdateContractFieldRows() => new()
    {
        // fieldName, expected FieldType, expected IsRepeated, expected HasPresence, locator
        {
            "name",
            nameof(FieldType.String),
            false,
            false,
            "`string name` [n_cst_thread_task_sqlupdate.sru:L11]. Rejected when empty, together with an "
                + "empty column list on either side [:L84]."
        },
        {
            "updatablecolumns",
            nameof(FieldType.String),
            true,
            false,
            "`string updatablecolumns[]` [:L12] - an ARRAY. Re-enabled per name after the blanket reset "
                + "[:L111-L114]."
        },
        {
            "keycolumns",
            nameof(FieldType.String),
            true,
            false,
            "`string keycolumns[]` [:L13] - an ARRAY. Each name is resolved through a describe call and "
                + "a non-positive identifier is E_INTERNAL_ERROR naming the offending column "
                + "[:L118-L122]."
        },
        {
            "identitycolumn",
            nameof(FieldType.String),
            false,
            false,
            "`string identitycolumn` [:L14] - a NAME here, unlike the response's ordinal. Empty means "
                + "none and the legacy skips the marking step entirely [:L127-L129]."
        },
        {
            "updatewhere",
            nameof(FieldType.Int64),
            false,
            true,
            "`long updatewhere` [:L15] - AN INTEGER, NOT A BOOLEAN. Applied only when not null "
                + "[:L131-L133]. The fixture's value is 1, the \"key and updateable columns\" mode "
                + "[dw_sqlite.srd table spec]."
        },
        {
            "updatekeyinplace",
            nameof(FieldType.Bool),
            false,
            true,
            "`boolean updatekeyinplace` [:L16]. Applied only when not null [:L135-L141]. The fixture "
                + "sets it to no [dw_sqlite.srd table spec], which triggers the delete-plus-insert path "
                + "and the legacy's own self-assignment workaround [:L151-L167]."
        },
    };

    [Theory]
    [MemberData(nameof(TableUpdateContractFieldRows))]
    public void TheTableUpdateContractMirrorsTheSixLegacyDescriptorFields(
        string fieldName,
        string expectedFieldType,
        bool expectedIsRepeated,
        bool expectedHasPresence,
        string locator)
    {
        FieldDescriptor field = Field(TableUpdateContractName, fieldName);

        Assert.Equal(ParseFieldType(expectedFieldType), field.FieldType);
        Assert.Equal(expectedIsRepeated, field.IsRepeated);

        // PRESENCE IS ASSERTED IN BOTH DIRECTIONS FROM ONE TABLE, WHICH IS WHAT MAKES THE ASSERTION
        // MEAN SOMETHING. The four non-nullable fields are expected to have NO explicit presence and
        // the two nullable ones to have it, so a change that made every field `optional` - or that
        // dropped `optional` from the two that need it - fails a row here either way. An assertion
        // that only ever checked for `true` would pass vacuously if presence became universal.
        Assert.Equal(expectedHasPresence, field.HasPresence);

        Assert.False(string.IsNullOrWhiteSpace(locator));
    }

    [Fact]
    public void TheUpdateWhereSettingIsIntegralAndNotABooleanBecauseTheLegacyFieldIsALong()
    {
        FieldDescriptor updateWhere = Field(TableUpdateContractName, "updatewhere");

        // THE LEGACY FIELD IS `long updatewhere` [n_cst_thread_task_sqlupdate.sru:L15], AND IT IS A
        // MODE RATHER THAN A FLAG. The legacy stringifies it straight into the carrier's setting -
        // `DataWindow.Table.UpdateWhere = '<value>'` [:L132] - so the value space is the DataWindow's
        // own, not two states.
        //
        // The evidenced value is 1 [dw_sqlite.srd table spec: `updatewhere=1`], the "key and updateable
        // columns" mode, which is what makes the concurrency check span all six columns' ORIGINAL
        // values and therefore what forces ConflictRow to carry two value sets per row. A boolean here
        // would erase the distinction between that mode and the others, and constraint C-B forbids
        // narrowing a legacy type to a tidier one.
        Assert.NotEqual(FieldType.Bool, updateWhere.FieldType);
        Assert.Equal(FieldType.Int64, updateWhere.FieldType);

        // BELT AND BRACES ON THE GENERATED PROJECTION, WHICH IS WHAT A CONSUMER ACTUALLY CONSUMES. The
        // descriptor is the contract, but a caller writes against the generated property, so the
        // projection is checked too: `long Updatewhere`, not `bool`.
        PropertyInfo? projected = Message(TableUpdateContractName).ClrType.GetProperty("Updatewhere");

        Assert.NotNull(projected);
        Assert.Equal(typeof(long), projected.PropertyType);
        Assert.NotEqual(typeof(bool), projected.PropertyType);
    }

    /// <summary>
    /// The two settings the legacy applies only when they are not null.
    /// </summary>
    public static TheoryData<string, string> NullableTableSettingRows() => new()
    {
        {
            "updatewhere",
            "Applied under `if Not IsNull(Tables[index].UpdateWhere)` "
                + "[n_cst_thread_task_sqlupdate.sru:L131], so UNSET means \"leave the carrier's own "
                + "setting alone\" and is materially different from any value the caller could send."
        },
        {
            "updatekeyinplace",
            "Applied under `if Not IsNull(Tables[index].UpdateKeyInPlace)` [:L135]. Unset means \"leave "
                + "the carrier's setting alone\"; FALSE means something entirely different - a key "
                + "change becomes DELETE PLUS INSERT, which is the path the legacy's own FIXME "
                + "[:L151-L154] and self-assignment workaround [:L155-L167] exist to serve. The primary "
                + "fixture sets it to no, so that path is EXERCISED, not a rare branch."
        },
    };

    [Theory]
    [MemberData(nameof(NullableTableSettingRows))]
    public void TheNullableTableSettingsCarryExplicitPresenceSoUnsetIsNotTheDefault(
        string fieldName,
        string reason)
    {
        FieldDescriptor field = Field(TableUpdateContractName, fieldName);

        // proto3 `optional` IS the presence mechanism, and HasPresence is how it is observed. Without
        // it, "unset" would be indistinguishable from "explicitly zero" for updatewhere and from
        // "explicitly false" for updatekeyinplace - and for the latter those two mean opposite things:
        // unset leaves the carrier's setting alone, while false actively selects delete-plus-insert.
        Assert.True(
            field.HasPresence,
            $"persistence.v1.TableUpdateContract.{fieldName} must be declared proto3 `optional`. The "
                + "legacy applies it only when it is NOT NULL, so \"unset\" must stay distinguishable "
                + "from \"set to the default\". " + reason);

        // Explicit presence on a scalar in proto3 is implemented as a synthetic one-field oneof, so the
        // field reports a containing oneof that is NOT a real, author-declared union. Asserting the
        // synthetic flag rather than merely "has a oneof" keeps this from being confused with a genuine
        // union arm such as AnyValue's.
        Assert.NotNull(field.ContainingOneof);
        Assert.True(field.ContainingOneof.IsSynthetic);

        // AND THE PROJECTION EXPOSES IT, so a caller can actually act on the distinction rather than
        // merely being promised it: protoc emits Has<Field> and Clear<Field> for a field with explicit
        // presence and emits neither for one without.
        Type clrType = Message(TableUpdateContractName).ClrType;
        string projectedName = field.PropertyName;

        Assert.NotNull(clrType.GetProperty("Has" + projectedName));
        Assert.NotNull(clrType.GetMethod("Clear" + projectedName));
    }

    [Fact]
    public void TheTableUpdateContractDeclaresExactlyTheSixFieldsTheLegacyStructureHas()
    {
        MessageDescriptor contract = Message(TableUpdateContractName);

        string[] declared = contract.Fields.InDeclarationOrder()
            .Select(static field => field.Name)
            .ToArray();

        // FIELD FOR FIELD, IN THE ORACLE'S OWN ORDER [n_cst_thread_task_sqlupdate.sru:L10-L17]. No
        // seventh field, no reordering, no renaming: the legacy spellings are single lower-case tokens
        // and are kept verbatim so the correspondence to the oracle is checkable at a glance, which is
        // the whole point of a mirror (C-B, C-K).
        Assert.Equal(
            ["name", "updatablecolumns", "keycolumns", "identitycolumn", "updatewhere", "updatekeyinplace"],
            declared);
    }

    [Fact]
    public void BothColumnListsAreRepeatedAndTheirEmptyRejectionCodeExistsInTheContract()
    {
        FieldDescriptor updatable = Field(TableUpdateContractName, "updatablecolumns");
        FieldDescriptor keys = Field(TableUpdateContractName, "keycolumns");

        Assert.True(updatable.IsRepeated);
        Assert.True(keys.IsRepeated);
        Assert.Equal(FieldType.String, updatable.FieldType);
        Assert.Equal(FieldType.String, keys.FieldType);

        // THE LEGACY REJECTS EITHER LIST BEING EMPTY AT ADD TIME, BEFORE ANY STATEMENT EXISTS:
        //     if name = "" or UpperBound(updatableColumns) = 0 or UpperBound(keyColumns) = 0
        //         then return RetCode.E_INVALID_ARGUMENT                                    [:L84]
        // A repeated proto3 field cannot express "at least one" in its own type, so the boundary
        // expresses the rejection through the outcome code instead. What IS assertable here, and is
        // asserted, is that the code the legacy returns exists in the contract's code space - so a
        // server can reproduce the guard and a client can recognise it.
        //
        // A DESCRIPTOR CANNOT CARRY THE PROSE, SO THIS IS AS FAR AS THE SHAPE GOES, AND THAT IS SAID
        // PLAINLY RATHER THAN ASSERTED AROUND: the .proto documents the guard in comments on both
        // fields, which review covers and a test cannot. Enforcing the minimum-count rule itself
        // belongs to Persistence's own validation tests, against the same locator.
        EnumDescriptor retCode = ContractDescriptors.RequireEnum("common.v1.RetCode.Value");
        Assert.Contains("E_INVALID_ARGUMENT", retCode.Values.Select(static value => value.Name));

        // The same code also covers the multi-table arm that fires when the array itself is empty
        // [:L359-L363], so one value serves both guards exactly as the legacy uses it for both.
        Assert.Contains("E_INTERNAL_ERROR", retCode.Values.Select(static value => value.Name));
    }

    // ==============================================================================================
    //  SECTION 4 - THE STATUS MAPPING, ASSERTED AT BOTH ENDS
    //  --------------------------------------------------------------------------------------------
    //  AAP 0.1.5: on an `updatewhereclause` mismatch, Persistence and DataServices return gRPC
    //  `Aborted` - the canonical gRPC-to-HTTP 409 mapping - with a structured detail carrying the
    //  current row state, and Gateway's REST projection surfaces it as HTTP 409 with the same payload.
    //
    //  ABORTED RATHER THAN FAILED_PRECONDITION, DELIBERATELY: the operation MAY succeed if the caller
    //  re-reads and retries at a higher level, which is precisely what `Aborted` means and what
    //  `FailedPrecondition` does not.
    //
    //  THE NEGATIVE AT THE END OF THIS SECTION IS THE AUDITABLE FORM OF "NO SILENT OVERWRITE ANYWHERE".
    //  Asserting that a 409 exists proves a conflict CAN be reported. It does not prove a caller cannot
    //  ask for the write to proceed anyway. Only the absence of a bypass proves that, so the absence is
    //  asserted rather than assumed.
    // ==============================================================================================

    // ---- OpenAPI reading helpers -----------------------------------------------------------------
    //
    // Microsoft.OpenApi 2.x models a `$ref` as a dedicated reference type that PROXIES member reads
    // through to its target, so `.Content` and `.Properties` resolve transparently. The reference's own
    // component id, however, lives on the concrete reference type - which is what these two helpers
    // reach, because "the 409 points at THIS component" is exactly the assertion that matters here.

    private static string? ResponseReferenceId(IOpenApiResponse? response) =>
        response is OpenApiResponseReference reference ? reference.Reference.Id : null;

    private static string? SchemaReferenceId(IOpenApiSchema? schema) =>
        schema is OpenApiSchemaReference reference ? reference.Reference.Id : null;

    /// <summary>Every (route, method, operation) triple under <c>/v1/datawindow</c>, flattened.</summary>
    private static IEnumerable<(string Route, HttpMethod Method, OpenApiOperation Operation)> DataWindowOperations(
        OpenApiDocument document)
    {
        foreach ((string route, IOpenApiPathItem pathItem) in document.Paths)
        {
            if (!route.StartsWith(DataWindowRoutePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            // A path item may legitimately declare no operation of its own, so this is skipped rather
            // than null-forgiven.
            if (pathItem.Operations is null)
            {
                continue;
            }

            foreach ((HttpMethod method, OpenApiOperation operation) in pathItem.Operations)
            {
                yield return (route, method, operation);
            }
        }
    }

    /// <summary>
    /// Every parameter name in force for one operation: the path item's shared parameters plus the
    /// operation's own.
    /// </summary>
    /// <remarks>
    /// BOTH LEVELS ARE COLLECTED, WHICH MATTERS FOR THE NEGATIVE ASSERTION. OpenAPI lets a parameter be
    /// declared on the path item and inherited by every operation under it, so a bypass added there
    /// would be invisible to a sweep that read only <see cref="OpenApiOperation.Parameters"/> - and
    /// invisible is exactly what a silent overwrite is.
    /// </remarks>
    private static IEnumerable<string> ParameterNames(
        OpenApiDocument document,
        string route,
        OpenApiOperation operation)
    {
        IOpenApiPathItem pathItem = document.Paths[route];

        IEnumerable<IOpenApiParameter> shared = pathItem.Parameters ?? [];
        IEnumerable<IOpenApiParameter> own = operation.Parameters ?? [];

        return shared.Concat(own)
            .Select(static parameter => parameter.Name)
            .Where(static name => !string.IsNullOrEmpty(name))
            .Select(static name => name!);
    }

    /// <summary>
    /// The one operation that can conflict: <c>POST /v1/datawindow/update</c>.
    /// </summary>
    /// <remarks>
    /// Resolved through a helper rather than indexed inline so that a missing route or a missing POST
    /// fails with a sentence, and so the nullable operation map is checked once instead of being
    /// null-forgiven at every call site. A suppression would have compiled just as well and told a
    /// future reader nothing.
    /// </remarks>
    private static OpenApiOperation UpdateOperation(OpenApiDocument document)
    {
        if (!document.Paths.TryGetValue(UpdateRoute, out IOpenApiPathItem? pathItem))
        {
            throw FailException.ForFailure(
                $"The Gateway contract declares no '{UpdateRoute}' path, so the update projection that "
                + "carries the conflict has nowhere to live.");
        }

        if (pathItem.Operations is null
            || !pathItem.Operations.TryGetValue(HttpMethod.Post, out OpenApiOperation? operation))
        {
            throw FailException.ForFailure(
                $"'{UpdateRoute}' declares no POST operation. The update projection is a POST because it "
                + "carries a changeset body.");
        }

        return operation;
    }

    /// <summary>
    /// The declared response set of one operation, checked rather than null-forgiven.
    /// </summary>
    /// <remarks>
    /// An operation with no responses at all is a document defect worth its own sentence, and reaching
    /// for <c>!</c> here would trade that sentence for a <see cref="NullReferenceException"/> thrown
    /// from inside a LINQ chain several frames away from the cause.
    /// </remarks>
    private static OpenApiResponses Responses(OpenApiOperation operation) =>
        operation.Responses
        ?? throw FailException.ForFailure(
            $"The operation '{operation.OperationId}' declares no responses, so no status mapping can "
            + "be asserted against it.");

    /// <summary>The document's component schema table, checked rather than null-forgiven.</summary>
    private static IDictionary<string, IOpenApiSchema> Schemas(OpenApiDocument document) =>
        document.Components?.Schemas
        ?? throw FailException.ForFailure(
            "The Gateway contract declares no component schemas, so the projected conflict shape "
            + "cannot be resolved.");

    /// <summary>The document's component response table, checked rather than null-forgiven.</summary>
    private static IDictionary<string, IOpenApiResponse> ComponentResponses(OpenApiDocument document) =>
        document.Components?.Responses
        ?? throw FailException.ForFailure(
            "The Gateway contract declares no component responses, so the shared Conflict response "
            + "cannot be resolved.");

    /// <summary>Reads a string-valued specification extension, or null when it is absent.</summary>
    private static string? Extension(OpenApiOperation operation, string name)
    {
        if (operation.Extensions is null
            || !operation.Extensions.TryGetValue(name, out IOpenApiExtension? extension))
        {
            return null;
        }

        return extension is JsonNodeExtension node ? node.Node?.GetValue<string>() : null;
    }

    // ---- The gRPC end ----------------------------------------------------------------------------

    [Fact]
    public void TheGrpcEndDeclaresTheConflictOutcomeAsAbortedAndSaysSoMachineReadably()
    {
        MethodDescriptor update = UpdateServiceMethod(UpdateMethodName);
        MethodOptions options = Assert.IsType<MethodOptions>(update.GetOptions());

        RichErrorBinding binding = options.GetExtension(CommonV1Extensions.RichError);

        // 10 IS THE CANONICAL NUMERIC CODE FOR ABORTED, and it is the canonical mapping to HTTP 409.
        // The binding states it ON THE DESCRIPTOR rather than in prose, so a client discovers the status
        // and the retrieval mechanism together, without reading a comment (C-K).
        Assert.Equal(AbortedStatusCode, binding.GrpcStatusCode);

        // A plain integer rather than an enum mirrored into this boundary, because the canonical code
        // space is gRPC's own and a local copy would be free to drift from it.
        FieldDescriptor statusCode = Field(RichErrorBindingName, "grpc_status_code");
        Assert.Equal(FieldType.Int32, statusCode.FieldType);

        // The trailer key is declared per method, so the descriptor is self-describing even to a client
        // that never reads common.v1.proto.
        Assert.False(string.IsNullOrWhiteSpace(binding.TrailerKey));
        Assert.Equal(RichErrorTrailerName, binding.PayloadType);
    }

    [Fact]
    public void OnlyTheUpdateMethodOfTheUpdateServiceCanProduceAConflict()
    {
        ServiceDescriptor service = ContractDescriptors.RequireService(UpdateServiceName);

        string[] withRichError = service.Methods
            .Where(static method =>
                method.GetOptions() is { } options && options.HasExtension(CommonV1Extensions.RichError))
            .Select(static method => method.Name)
            .ToArray();

        // THE CONFLICT BELONGS TO THE UPDATE AND TO NOTHING ELSE. Task creation, release, reset and
        // prepare cannot conflict: none of them generates a statement. A binding on any of those would
        // mean the mapping had been applied by habit rather than from the semantics.
        Assert.Equal([UpdateMethodName], withRichError);
    }

    // ---- The REST end ----------------------------------------------------------------------------

    [Fact]
    public void TheRestProjectionDeclaresA409OnTheUpdateOperation()
    {
        OpenApiDocument document = documents.Gateway;

        OpenApiOperation update = UpdateOperation(document);

        Assert.True(
            Responses(update).ContainsKey("409"),
            $"POST {UpdateRoute} must declare a 409 response. C-06 returns gRPC `Aborted` on an "
                + "optimistic-concurrency mismatch and 409 is the canonical projection of that status "
                + "(AAP 0.1.5). Mapping it to 500 would tell a caller the failure was the server's and "
                + "not retryable; mapping it to 200 would report a write that did not happen.");

        // AND IT IS THE UPDATE PROJECTION, not some other operation that happens to sit on this route.
        Assert.Equal("dataservices.v1.DataWindowService/Update", Extension(update, "x-grpc-method"));

        // THE 409 RESOLVES TO THE SHARED CONFLICT RESPONSE COMPONENT rather than to a body invented at
        // this one call site, which is what keeps the REST and gRPC ends describing one condition.
        Assert.Equal(ConflictResponseComponent, ResponseReferenceId(Responses(update)["409"]));
    }

    [Fact]
    public void The409BodyCarriesTheConflictDetailShapeRatherThanABareStatus()
    {
        OpenApiDocument document = documents.Gateway;
        IOpenApiResponse conflict = Assert.Contains(ConflictResponseComponent, ComponentResponses(document));

        // A PROBLEM-DETAILS BODY, so a caller gets a machine-readable payload rather than prose.
        Assert.NotNull(conflict.Content);
        OpenApiMediaType media = Assert.Contains(ProblemJson, conflict.Content);

        Assert.Equal(ConflictProblemSchema, SchemaReferenceId(media.Schema));

        // ... WHICH CARRIES THE CONFLICT DETAIL AS A REQUIRED MEMBER. This is the chain that makes the
        // 409 actionable: without it a caller cannot learn the current row state, would re-send the same
        // stale original values, and would receive the same 409 for ever.
        IOpenApiSchema problem = Assert.Contains(ConflictProblemSchema, Schemas(document));
        Assert.NotNull(problem.AllOf);

        // Walked with TryGetValue rather than a LINQ chain plus `!`, so the compiler verifies the null
        // handling instead of being told to trust it.
        List<IOpenApiSchema> conflictMembers = [];
        List<string> requiredMembers = [];

        foreach (IOpenApiSchema part in problem.AllOf)
        {
            if (part.Properties is not null
                && part.Properties.TryGetValue(ConflictMember, out IOpenApiSchema? member))
            {
                conflictMembers.Add(member);
            }

            if (part.Required is not null)
            {
                requiredMembers.AddRange(part.Required);
            }
        }

        IOpenApiSchema conflictMember = Assert.Single(conflictMembers);
        Assert.Equal(ConflictDetailSchema, SchemaReferenceId(conflictMember));
        Assert.Contains(ConflictMember, requiredMembers);
    }

    [Fact]
    public void EveryConflictingRestOperationUsesTheOneConflictShapeSoASecondCannotDiverge()
    {
        OpenApiDocument document = documents.Gateway;

        (string Route, HttpMethod Method, OpenApiOperation Operation)[] conflicting = DataWindowOperations(document)
            .Where(static entry => Responses(entry.Operation).ContainsKey("409"))
            .ToArray();

        // AT LEAST ONE, OR THE MAPPING IS NOT EXPRESSED AT ALL.
        Assert.NotEmpty(conflicting);

        foreach ((string route, HttpMethod method, OpenApiOperation operation) in conflicting)
        {
            Assert.Equal(
                ConflictResponseComponent,
                ResponseReferenceId(Responses(operation)["409"]));

            // A second, inline 409 body on some other DataWindow operation would be the drift this row
            // exists to catch: two shapes for one condition, and a client that decoded one and not the
            // other. The route and method are read into the assertion message so a failure names the
            // offender rather than only the count.
            Assert.True(
                Responses(operation)["409"] is OpenApiResponseReference,
                $"{method} {route} declares a 409 that is not a reference to the shared Conflict "
                    + "response component. One condition must have exactly one shape.");
        }
    }

    /// <summary>
    /// Member-for-member rows pairing the projected REST schema against the Protobuf message it claims
    /// to mirror.
    /// </summary>
    /// <remarks>
    /// WITHOUT THESE ROWS A PROJECTION THAT AGREED WITH NEITHER END WOULD PASS BOTH SUITES: the REST
    /// suite would see a well-formed schema and this file's Protobuf sections would see a well-formed
    /// message, while the two described different payloads. The names differ by convention - protobuf
    /// snake_case against JSON camelCase, which is the canonical protobuf JSON mapping - so the pairing
    /// is stated explicitly rather than derived.
    /// </remarks>
    public static TheoryData<string, string, string> ProjectedConflictMemberRows() => new()
    {
        // openApiSchemaName, jsonMemberName, protoFieldName
        { "ConflictDetail", "rows", "rows" },
        { "ConflictDetail", "updateTable", "update_table" },
        { "ConflictDetail", "rowsExpected", "rows_expected" },
        { "ConflictDetail", "rowsMatched", "rows_matched" },
        { "ConflictRow", "buffer", "buffer" },
        { "ConflictRow", "row", "row" },
        { "ConflictRow", "itemStatus", "item_status" },
        { "ConflictRow", "currentValues", "current_values" },
        { "ConflictRow", "originalValues", "original_values" },
    };

    [Theory]
    [MemberData(nameof(ProjectedConflictMemberRows))]
    public void TheProjectedConflictShapeMirrorsTheProtobufMessageMemberForMember(
        string schemaName,
        string jsonMemberName,
        string protoFieldName)
    {
        OpenApiDocument document = documents.Gateway;

        IOpenApiSchema schema = Assert.Contains(schemaName, Schemas(document));

        Assert.NotNull(schema.Properties);
        Assert.Contains(jsonMemberName, schema.Properties);

        // REQUIRED ON THE REST SIDE, because the payload is only actionable if every member is present:
        // a 409 that omitted the original values would leave a caller unable to see which column moved.
        Assert.NotNull(schema.Required);
        Assert.Contains(jsonMemberName, schema.Required);

        // AND THE PROTOBUF SIDE DECLARES THE COUNTERPART, so neither end can drop a member unilaterally.
        FieldDescriptor field = Field($"common.v1.{schemaName}", protoFieldName);
        Assert.Equal(protoFieldName, field.Name);
    }

    [Fact]
    public void TheProjectedConflictShapeAddsNoMemberTheProtobufMessageDoesNotHave()
    {
        OpenApiDocument document = documents.Gateway;

        foreach (string schemaName in (string[])["ConflictDetail", "ConflictRow"])
        {
            IOpenApiSchema schema = Assert.Contains(schemaName, Schemas(document));
            MessageDescriptor message = Message($"common.v1.{schemaName}");

            // SAME MEMBER COUNT IN BOTH DIRECTIONS. The row-by-row theory above proves every protobuf
            // field is projected; this proves the projection invented nothing extra - in particular no
            // member that would let a caller influence the conflict outcome.
            Assert.NotNull(schema.Properties);
            Assert.Equal(message.Fields.InDeclarationOrder().Count, schema.Properties.Count);

            // additionalProperties: false, so an undeclared member is rejected rather than tolerated. A
            // schema open to extra members would let a "force" flag be smuggled past this suite in a
            // request body without the document ever declaring one.
            Assert.False(schema.AdditionalPropertiesAllowed);
        }
    }

    // ---- The negative: no way to make the write proceed anyway -----------------------------------

    /// <summary>
    /// Substrings that would indicate a conflict-bypass knob if they appeared in a parameter name.
    /// </summary>
    /// <remarks>
    /// SCOPED TO PARAMETER NAMES ON PURPOSE, and that scope is load-bearing rather than incidental. The
    /// document legitimately contains a <c>forceCalc</c> member on a column-expression schema, which
    /// reproduces the legacy "value plus recalculate plus force" arity and has nothing to do with
    /// concurrency. A blanket text search for "force" would flag it and the honest response would then
    /// be to weaken the assertion, so the assertion is aimed precisely instead: at the names a caller
    /// could actually use to ask an update to proceed anyway.
    /// </remarks>
    public static TheoryData<string> ConflictBypassVocabularyRows() => new()
    {
        // The obvious spellings a bypass would take.
        "force",
        "overwrite",
        "ignoreconflict",
        "skipconflict",
        "lastwriterwins",

        // AND THE NON-OBVIOUS ONE, WHICH IS THE POINT OF LISTING IT. A per-request knob named for the
        // concurrency setting itself would be a bypass by another name: `updatewhere` is a MODE, and the
        // legacy applies it from the table descriptor inside PrepareUpdate [:L131-L133], never from the
        // update call. A request-level override would let a caller select a weaker mode for one write
        // and get exactly the silent overwrite the 409 exists to prevent, without the word "force"
        // appearing anywhere.
        "updatewhereclause",
    };

    [Theory]
    [MemberData(nameof(ConflictBypassVocabularyRows))]
    public void NoDataWindowOperationOffersAParameterThatWouldBypassTheConflict(string forbiddenSubstring)
    {
        OpenApiDocument document = documents.Gateway;

        string[] offenders = DataWindowOperations(document)
            .SelectMany(entry => ParameterNames(document, entry.Route, entry.Operation)
                .Where(name => name.Contains(forbiddenSubstring, StringComparison.OrdinalIgnoreCase))
                .Select(name => $"{entry.Method} {entry.Route} -> {name}"))
            .ToArray();

        // THIS NEGATIVE IS THE AUDITABLE FORM OF "NO SILENT OVERWRITE ANYWHERE IN THE SYSTEM".
        //
        // A 409 that a caller could opt out of is not a concurrency control, it is a suggestion. The
        // legacy has no such opt-out: on the failure path the worker rolls the transaction back
        // [n_cst_thread_task_sqlupdate.sru:L395] and there is no argument anywhere in
        // of_addupdatabletable or the update path that would let a caller ignore a mismatch. Adding one
        // at the boundary would be a new capability, which constraint C-B forbids outright.
        Assert.True(
            offenders.Length == 0,
            $"No /v1/datawindow operation may expose a parameter whose name contains "
                + $"'{forbiddenSubstring}', because that would let a caller ask a conflicting write to "
                + $"proceed anyway. Offending parameters: {string.Join("; ", offenders)}.");
    }

    [Fact]
    public void TheUpdateOperationTakesNoParametersAtAllSoThereIsNothingToTuneTheConflictWith()
    {
        OpenApiDocument document = documents.Gateway;
        OpenApiOperation update = UpdateOperation(document);

        // THE STRONGEST AVAILABLE FORM OF THE NEGATIVE FOR THE ONE OPERATION THAT CAN CONFLICT: not
        // "no bypass parameter" but NO PARAMETER WHATSOEVER. The whole request is the changeset in the
        // body, so there is no query or header surface on which a bypass could later be added without
        // this row failing.
        Assert.Empty(ParameterNames(document, UpdateRoute, update));
    }

    [Fact]
    public void TheConflictShapeIsReachableFromThe409AndFromNoOtherStatus()
    {
        OpenApiDocument document = documents.Gateway;
        OpenApiOperation update = UpdateOperation(document);

        // ============ THIS IS THE ROW THAT FAILS IF Aborted IS EVER MAPPED TO 500 OR TO 200 ==========
        // It is not enough that a 409 exists somewhere on the operation: the CONFLICT BODY must hang off
        // the 409 and off nothing else. Were the projection changed to report a conflict as a 500, the
        // Conflict component would move to that status and this row would fail on both halves - the 409
        // would no longer reference it, and a non-409 status would. Were it reported as a 200, the
        // success response would carry it and the same two halves would fail.
        // ==========================================================================================
        string[] statusesCarryingTheConflictShape = Responses(update)
            .Where(static entry => string.Equals(ResponseReferenceId(entry.Value), "Conflict", StringComparison.Ordinal))
            .Select(static entry => entry.Key)
            .ToArray();

        Assert.Equal(["409"], statusesCarryingTheConflictShape);

        // The success status must exist and must NOT be the conflict shape, so "reported success on a
        // conflict" is unrepresentable in this document - the REST mirror of the reply's own refusal to
        // reduce the outcome to a flag.
        IOpenApiResponse success = Assert.Contains("200", Responses(update));
        Assert.NotEqual("Conflict", ResponseReferenceId(success));

        // And across the whole DataWindow projection, the shared Conflict component is used by the 409
        // status and by no other, so no sibling operation can quietly reuse it under a different code.
        string[] statusesAcrossTheProjection = DataWindowOperations(document)
            .SelectMany(static entry => Responses(entry.Operation))
            .Where(static entry => string.Equals(ResponseReferenceId(entry.Value), "Conflict", StringComparison.Ordinal))
            .Select(static entry => entry.Key)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["409"], statusesAcrossTheProjection);
    }
}
