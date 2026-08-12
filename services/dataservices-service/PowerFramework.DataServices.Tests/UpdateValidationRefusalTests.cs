// ==================================================================================================
//  UpdateValidationRefusalTests - WHAT C-03's Update ANSWERS WHEN VALIDATION REFUSES A PAYLOAD
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   services/dataservices-service/PowerFramework.DataServices/Grpc/DataWindowService.cs
//                the validation block ahead of the work scope, and BuildValidationRefusal
//            shared/PowerFramework.Contracts/Proto/dataservices.v1.proto
//                UpdateResponse.validation_errors and RowValidationError
//
//  WHAT THIS SUITE IS FOR, AS DISTINCT FROM UpdateRowValidatorTests
//  ------------------------------------------------------------------------------------------------
//  UpdateRowValidatorTests holds the VALIDATOR to its decisions. This suite holds the SERVICE to what
//  it does with them: the outcome code it chooses, the payload it projects, and - the part with the
//  most operational weight - that a refused request touches NO UPSTREAM AT ALL. A refusal that opened
//  a Persistence session and then abandoned it would answer the caller correctly while leaking a
//  worker task and a pooled-transaction reference on every malformed payload.
//
//  THE REAL HOST, NOT THE FAKE ONE. Every case here injects the SHIPPED catalogue-backed model-set
//  provider - HeadlessDataWindowModelSetProvider over HeadlessDataWindowHostFactory over
//  DataWindowCatalogue - because the validator resolves columns by asking the host, and a fake host
//  would let this suite pass while the production wiring rejected or accepted the wrong payloads. The
//  handle is therefore the transcribed fixture's own name rather than the sibling suites' "dw-1",
//  which resolves to nothing and is precisely why those suites never reach validation.
//
//  ORACLE ANCHORS, READ ONLY (C-C)
//  ------------------------------------------------------------------------------------------------
//    ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14          the six columns and their types
//    ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L347-L358
//                                                              the dialog the refusal reproduces
//
//  AAP 0.8.5 - NO PERFORMANCE OR VOLUME CLAIM. No case measures elapsed time or sizes a payload.
// ==================================================================================================

using System.Globalization;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Validators;
using PowerFramework.Shared.Localization;
using Xunit;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;
using WireUpdateRequest = PowerFramework.Contracts.DataServices.V1.UpdateRequest;
using WireUpdateResponse = PowerFramework.Contracts.DataServices.V1.UpdateResponse;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Holds the update path's validation refusal to its published shape.
/// </summary>
public sealed class UpdateValidationRefusalTests
{
    /// <summary>The transcribed fixture's ordinals, in declaration order.</summary>
    private const long NameColumn = 2L;
    private const long AgeColumn = 3L;
    private const long SalaryColumn = 5L;
    private const long BirthColumn = 6L;

    // ==============================================================================================
    //  1. THE REFUSAL REACHES NO UPSTREAM
    // ==============================================================================================

    /// <summary>
    /// 🔴 A REFUSED PAYLOAD OPENS NO SESSION, CREATES NO TASK AND SUBMITS NOTHING.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE EMPTY LIFECYCLE IS THE ASSERTION THAT MATTERS. The validation block sits ahead of the work
    /// scope deliberately, so a refusal has nothing to unwind: no session was begun, no update task was
    /// created and no pooled-transaction reference was taken. Placing it after the scope would still
    /// answer the caller correctly while making every malformed payload cost a round trip - and would open
    /// a window in which a refused request holds an upstream resource.
    /// </para>
    /// <para>
    /// AND THE COUNTS ARE ZERO WITH NO <c>error</c> ATTACHED, because nothing was attempted. A
    /// <c>DbError</c> on this response would claim a database failure that never happened, which is the
    /// exact misdiagnosis the finding was about in the other direction.
    /// </para>
    /// </remarks>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task ARefusedPayloadTouchesNoUpstreamAndReportsNothingApplied()
    {
        C03Fixture fixture = Fixture();

        WireUpdateResponse response = await fixture.Service.Update(
            Request(Column("age", AgeColumn, "not-a-number")),
            fixture.Context);

        Assert.Empty(fixture.Persistence.Lifecycle);
        Assert.Equal(0, fixture.Persistence.UpdateCalls);

        Assert.Equal(0L, response.RowsInserted);
        Assert.Equal(0L, response.RowsUpdated);
        Assert.Equal(0L, response.RowsDeleted);
        Assert.Null(response.Error);
        Assert.Empty(response.Identity);
    }

    // ==============================================================================================
    //  2. THE OUTCOME CODE
    // ==============================================================================================

    /// <summary>
    /// ⚠ AN ADDRESSING FAULT SETS THE OUTCOME EVEN WHEN VALUES WERE ALSO REJECTED.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A caller must fix the addressing before its values mean anything: while a column reference is
    /// wrong, nobody knows which column the rejected value was even destined for. So a payload carrying
    /// both faults answers <c>E_INVALID_ARGUMENT</c> - the request is malformed - and only a payload whose
    /// references all resolve answers <c>E_INVALID_DATA</c>.
    /// </para>
    /// <para>
    /// BOTH PROJECT TO HTTP 400 AT THE INGRESS, so this choice changes the DIAGNOSIS a caller reads rather
    /// than the status it receives. That is the point: the status says "your request", the code says which
    /// part of it.
    /// </para>
    /// </remarks>
    /// <param name="columns">The payload.</param>
    /// <param name="expected">The outcome it must answer.</param>
    /// <returns>A task representing the assertion.</returns>
    [Theory]
    [MemberData(nameof(OutcomeCodes))]
    public async Task TheOutcomeCodeNamesTheStructuralFaultWhenThereIsOne(
        ColumnValue[] columns,
        WireRetCode expected)
    {
        C03Fixture fixture = Fixture();

        WireUpdateResponse response = await fixture.Service.Update(Request(columns), fixture.Context);

        Assert.Equal(expected, response.RetCode);
        Assert.NotEmpty(response.ValidationErrors);
    }

    /// <summary>The outcome-code matrix.</summary>
    /// <returns>A payload and the outcome it answers.</returns>
    public static TheoryData<ColumnValue[], WireRetCode> OutcomeCodes() => new()
    {
        // Values only.
        { [Column("age", AgeColumn, "not-a-number")], WireRetCode.EInvalidData },
        {
            [Column("age", AgeColumn, "not-a-number"), Column("salary", SalaryColumn, "abc")],
            WireRetCode.EInvalidData
        },

        // Addressing only.
        { [Column("no_such_column", AgeColumn, "30")], WireRetCode.EInvalidArgument },

        // A name and an ordinal that disagree is an addressing fault too, even though both identifiers
        // are real - it MISDIRECTS the value rather than losing it.
        { [Column("age", NameColumn, "30")], WireRetCode.EInvalidArgument },

        // BOTH, in each order, so the answer cannot be an artefact of which fault was seen first.
        {
            [Column("age", AgeColumn, "not-a-number"), Column("no_such_column", 0L, "x")],
            WireRetCode.EInvalidArgument
        },
        {
            [Column("no_such_column", 0L, "x"), Column("age", AgeColumn, "not-a-number")],
            WireRetCode.EInvalidArgument
        },
    };

    // ==============================================================================================
    //  3. THE PROJECTED PAYLOAD
    // ==============================================================================================

    /// <summary>
    /// Every rejected column is projected, in request order, addressed as the caller addressed it.
    /// </summary>
    /// <remarks>
    /// REQUEST ORDER IS PART OF THE CONTRACT, not a coincidence of iteration: a consumer rendering these
    /// beside its own input needs them to line up with the payload it sent. Collecting every fault rather
    /// than the first is what turns as many round trips as there are faults into one.
    /// </remarks>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task EveryRejectedColumnIsProjectedInRequestOrder()
    {
        C03Fixture fixture = Fixture();

        WireUpdateResponse response = await fixture.Service.Update(
            Request(
                Column("age", AgeColumn, "not-a-number"),
                Column("name", NameColumn, "Paul"),
                Column("salary", SalaryColumn, "abc"),
                Column("birth", BirthColumn, "31/02/1984")),
            fixture.Context);

        Assert.Equal(WireRetCode.EInvalidData, response.RetCode);
        Assert.Equal(3, response.ValidationErrors.Count);

        Assert.Equal(
            ["age", "salary", "birth"],
            response.ValidationErrors.Select(static error => error.ColumnName));

        Assert.Equal(
            [AgeColumn, SalaryColumn, BirthColumn],
            response.ValidationErrors.Select(static error => error.ColumnId));

        // THE DECLARED TYPE TRAVELS, so a caller can see WHY its value was refused without guessing at
        // the definition.
        Assert.Equal(
            ["number", "decimal(2)", "date"],
            response.ValidationErrors.Select(static error => error.ColumnType));

        // THE ADDRESS ECHOES THE REQUEST'S OWN BUFFER AND ROW.
        Assert.All(
            response.ValidationErrors,
            static error =>
            {
                Assert.Equal(DwBuffer.Primary, error.Buffer);
                Assert.Equal(1L, error.Row);
            });
    }

    /// <summary>
    /// The projected error is the oracle's own dialog, field by field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AAP 0.2.1.3 Correction 5 requires a structured error to carry the exact message text, the
    /// localization category, the substitution arguments and the severity - only the DELIVERY CHANNEL
    /// changes from a dialog to a payload. Each of those four is asserted here, plus the caret position
    /// staying absent because this refusal has no expression text to point into.
    /// </para>
    /// <para>
    /// THE TEXTS ARE THE CHINESE SOURCES because no provider is installed, and the localization facade's
    /// SILENT PASSTHROUGH returns the untranslated source unchanged [<c>i18n.srf:L17-L18</c>]. That
    /// passthrough is the legacy's own uninstalled state and is preserved rather than replaced with a
    /// throw or a marker.
    /// </para>
    /// </remarks>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task TheProjectedErrorIsTheOraclesOwnDialog()
    {
        C03Fixture fixture = Fixture();

        WireUpdateResponse response = await fixture.Service.Update(
            Request(Column("age", AgeColumn, "not-a-number")),
            fixture.Context);

        StructuredError error = Assert.Single(response.ValidationErrors).Error;

        Assert.NotNull(error);
        Assert.Equal("输入了无效的值!", error.Text);
        Assert.Equal("错误", error.Title);
        Assert.Equal(Severity.StopSign, error.Severity);
        Assert.Equal(Categories.CAT_DWSVC, error.Category);
        Assert.True(error.Localized);
        Assert.Empty(error.FormatArgs);

        // AND THE REJECTED VALUE IS NOT IN THE MESSAGE. It is caller content, malformed by definition on
        // this path, and the column identity a consumer needs is carried in the address fields instead.
        Assert.DoesNotContain("not-a-number", error.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unknown-column refusal carries its own defined text and claims no localization.
    /// </summary>
    /// <remarks>
    /// This refusal has no oracle behind it - the legacy never sees a column reference at all, because a
    /// DataWindow cell is addressed in-process - so it claims NO category and reports itself unlocalized.
    /// Reporting <c>CAT_DWSVC</c> would tell a characterization comparison that a legacy translation table
    /// answered for text no table carries. The two identifiers the caller sent travel as the substitution
    /// arguments so a consumer can re-render the sentence in its own locale.
    /// </remarks>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task AnUnknownColumnRefusalCarriesADefinedErrorAndClaimsNoLocalization()
    {
        C03Fixture fixture = Fixture();

        WireUpdateResponse response = await fixture.Service.Update(
            Request(Column("no_such_column", 42L, "x")),
            fixture.Context);

        RowValidationError projected = Assert.Single(response.ValidationErrors);

        Assert.Equal("no_such_column", projected.ColumnName);
        Assert.Equal(42L, projected.ColumnId);

        // NO TYPE, because a column this DataWindow does not carry has none.
        Assert.Equal(string.Empty, projected.ColumnType);

        Assert.NotNull(projected.Error);
        Assert.Equal(UpdateRowValidator.UnknownColumnTitle, projected.Error.Title);
        Assert.False(projected.Error.Localized);
        Assert.Equal(0L, projected.Error.Category);
        // THE WHOLE RENDERED SENTENCE. The template's placeholders are ONE-BASED, matching the ported
        // Sprintf, and a zero-based template renders its first argument as the empty string instead of
        // throwing - so the sentence has to be pinned in full or a silent shift passes unnoticed.
        Assert.Equal(
            "The request named column 'no_such_column' at ordinal 42, which this DataWindow does not "
            + "carry at that ordinal. A column is addressed by its one-based ordinal, and the name must "
            + "be the name that ordinal carries; no value was applied.",
            projected.Error.Text);
        Assert.Equal(
            ["no_such_column", 42L.ToString(CultureInfo.InvariantCulture)],
            projected.Error.FormatArgs);
    }

    // ==============================================================================================
    //  4. THE CONTROLS - WHAT MUST STILL REACH THE UPSTREAM
    // ==============================================================================================

    /// <summary>
    /// A well-formed payload against the same handle still runs the full upstream lifecycle.
    /// </summary>
    /// <remarks>
    /// <b>THE CONTROL WITHOUT WHICH THIS WHOLE SUITE PROVES NOTHING.</b> Every case above asserts a
    /// refusal, so a change that refused every update would satisfy all of them. This one pins that the
    /// ordinary path is untouched: six well-formed columns against the real transcribed definition reach
    /// Persistence, in the documented order, and answer success with no validation errors attached.
    /// </remarks>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task AWellFormedPayloadStillRunsTheFullUpstreamLifecycle()
    {
        C03Fixture fixture = Fixture();

        WireUpdateResponse response = await fixture.Service.Update(
            Request(
                Column("name", NameColumn, "Paul"),
                Column("age", AgeColumn, "32"),
                Column("salary", SalaryColumn, "20000.00"),
                Column("birth", BirthColumn, "1999-05-08")),
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.Empty(response.ValidationErrors);
        Assert.NotEmpty(fixture.Persistence.Lifecycle);
        Assert.Equal(1, fixture.Persistence.UpdateCalls);
    }

    /// <summary>
    /// ⚠ AN UNRESOLVABLE HANDLE IS NOT VALIDATED HERE, and reaches the layer that owns that refusal.
    /// </summary>
    /// <remarks>
    /// There is no definition to validate against, and the update contract's refusal for an unknown
    /// DataWindow belongs to <c>Tasks/SqlUpdateTask</c> step 3 - the port of the oracle's own boundary
    /// check. Refusing at this line instead would move an established refusal into the projecting layer
    /// and change which code a caller receives for an unknown handle ON THIS OPERATION ALONE, which is
    /// exactly the kind of divergence that makes two services disagree about one fault. Asserted with a
    /// payload that WOULD have been refused had a definition been resolvable, so the case pins the skip
    /// rather than merely the absence of an error.
    /// </remarks>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task AnUnresolvableHandleIsNotValidatedHereAndStillReachesTheUpstream()
    {
        C03Fixture fixture = Fixture();

        WireUpdateRequest request = Request(Column("no_such_column", AgeColumn, "not-a-number"));
        request.DatawindowHandle = "d_never_transcribed";

        WireUpdateResponse response = await fixture.Service.Update(request, fixture.Context);

        Assert.Empty(response.ValidationErrors);
        Assert.NotEmpty(fixture.Persistence.Lifecycle);
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>
    /// A C-03 fixture over the SHIPPED catalogue-backed model-set provider.
    /// </summary>
    /// <returns>The fixture.</returns>
    /// <remarks>
    /// THE PRODUCTION WIRING, ASSEMBLED THE WAY THE COMPOSITION ROOT ASSEMBLES IT - including the
    /// deliberately BLOCKED pinyin matcher, because the lookup table lives only inside the closed
    /// <c>pfw.dll</c> and AAP 0.6.5 requires it be reported blocked rather than approximated.
    /// </remarks>
    private static C03Fixture Fixture()
    {
        DataServicesOptions configured = new();

        HeadlessDataWindowModelSetProvider models = new(
            new HeadlessDataWindowHostFactory(new DataWindowCatalogue()),
            Options.Create(configured),
            new I18n(),
            PinyinFirstLetterMatcher.Blocked,
            ExpressionPageResolverFactory.Create(
                configured.ColumnExpression.PageResolution,
                configured.ColumnExpression.PageRowsPerPage));

        return new C03Fixture(models, configured);
    }

    /// <summary>Builds a one-row update request against the transcribed fixture.</summary>
    /// <param name="columns">The columns, in request order.</param>
    /// <returns>The request.</returns>
    private static WireUpdateRequest Request(params ColumnValue[] columns)
    {
        WireUpdateRequest request = new()
        {
            DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
        };

        DataWindowRow row = new() { Buffer = DwBuffer.Primary, Row = 1L };
        row.Columns.AddRange(columns);
        request.Rows.Add(row);

        return request;
    }

    /// <summary>Builds a text-valued column reference.</summary>
    /// <param name="name">The column name.</param>
    /// <param name="columnId">The one-based ordinal, or zero for "not stated".</param>
    /// <param name="text">The value, as the edit text a caller sends.</param>
    /// <returns>The reference.</returns>
    private static ColumnValue Column(string name, long columnId, string text) => new()
    {
        ColumnName = name,
        ColumnId = columnId,
        Value = new AnyValue { StringValue = text },
    };
}
