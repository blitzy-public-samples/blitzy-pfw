// ==================================================================================================
//  UpdateRowValidatorTests - THE VALIDATOR THIRD OF THE TRIPLE, ON THE WRITE PATH
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   services/dataservices-service/PowerFramework.DataServices/Validators/
//                UpdateRowValidator.cs
//
//  WHAT THIS SUITE EXISTS FOR
//  ------------------------------------------------------------------------------------------------
//  The five ported dwnvl* validators were reachable from the expression engine, the context-menu
//  model and the item-change protocol, and from NOTHING ON THE WRITE PATH. An update therefore
//  carried whatever the caller sent all the way to the provider, so a value the DataWindow's own
//  declared type cannot hold - `age = "not-a-number"` against `type=number` - was answered as a
//  502 database error naming a constraint, or worse stored as text nothing could read back.
//  The validator closes that gap by running the ported coercions BEFORE any statement is generated.
//
//  ORACLE ANCHORS, ALL READ ONLY (C-C)
//  ------------------------------------------------------------------------------------------------
//   ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14
//       the six columns and their declared types - id number/key/identity, name char(100),
//       age number, address char(200), salary decimal(2), birth date
//   ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L232-L243
//       the manual coercion switch on Left(dwo.ColType,5) that the representability test is
//       aligned to, and which has NO `case else`
//   ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L347-L358
//       the dialog the invalid-value refusal reproduces: I18N(CAT_DWSVC,"输入了无效的值") + "!" for
//       the body, I18N(CAT_DWSVC,"错误") for the title, StopSign!, no substitution arguments
//   ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L1541-L1542
//       the Describe(name + ".ID") idiom and its non-positive-means-no-such-column reading
//
//  C-B - THREE THINGS THIS SUITE ASSERTS ARE **NOT** VALIDATED
//  ------------------------------------------------------------------------------------------------
//  Each is a preserved property rather than an omission, and each has a case below that pins it so a
//  later change cannot quietly add the check:
//    * WIDTH. char(200) over a CHAR(50) column is a PRESERVED defect (AAP 0.6.4), so a 300-character
//      address is accepted here and refused - if at all - by the engine.
//    * NULLABILITY. The DataWindow definition declares no required flag on ANY column; NOT NULL lives
//      only in the DDL [w_test_sqlite.srw:L463-L469]. This layer cannot know it and does not guess.
//    * ORIGINAL VALUES. An original is what the caller READ BACK from this service; validating it
//      would refuse a payload assembled correctly from our own answer.
//
//  AAP 0.8.5 - NO PERFORMANCE OR VOLUME CLAIM. Every case uses the smallest payload that makes its
//  behaviour reachable. Nothing here sizes a workload and no assertion is made about elapsed time.
// ==================================================================================================

using System.Collections.Immutable;
using System.Globalization;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Validators;
using PowerFramework.Shared.Localization;
using Xunit;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Holds <see cref="UpdateRowValidator"/> to the definition it validates against and to the oracle's
/// own refusal.
/// </summary>
public sealed class UpdateRowValidatorTests
{
    /// <summary>The one-based ordinals of the evidenced fixture, in declaration order.</summary>
    private const long IdColumn = 1L;
    private const long NameColumn = 2L;
    private const long AgeColumn = 3L;
    private const long AddressColumn = 4L;
    private const long SalaryColumn = 5L;
    private const long BirthColumn = 6L;

    // ==============================================================================================
    //  1. ADDRESSING - A COLUMN REFERENCE THAT MISDIRECTS A VALUE
    // ==============================================================================================

    /// <summary>
    /// A name this DataWindow does not carry is refused, and the refusal names it.
    /// </summary>
    /// <param name="columnName">The name the request carried.</param>
    /// <remarks>
    /// <para>
    /// THE LAST ROW IS THE ONE THAT MATTERS MOST. <c>total_salary</c> is a real object on this
    /// definition - the footer compute [<c>dw_sqlite.srd:L27</c>] - and it is NOT a column: it consumes
    /// no column number, so its <c>.ID</c> answers a non-positive value and the oracle's own reading of
    /// that answer is "no such column" [<c>n_cst_dwsvc_columnexp.sru:L1541</c>]. A validator that tested
    /// only for the unresolvable sentinel would accept it and then write a value to whichever column the
    /// ordinal happened to name.
    /// </para>
    /// <para>
    /// THE LEADING-SPACE ROW IS NOT PEDANTRY. Name resolution is case-INSENSITIVE, as PowerBuilder's own
    /// is (pinned separately below), so a reader could reasonably expect it to be forgiving about
    /// surrounding space too. It is not, and it should not be: a name is an identifier the caller either
    /// has or does not have, and trimming it here would accept a payload no other layer accepts.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("no_such_column")]
    [InlineData("total_salary")]
    [InlineData(" age")]
    [InlineData("age ")]
    public void ANameThisDataWindowDoesNotCarryIsRefused(string columnName)
    {
        ImmutableArray<UpdateRowValidationFailure> failures = Validate(
            Row(1L, Column(columnName, AgeColumn, new AnyValue { DoubleValue = 30d })));

        UpdateRowValidationFailure failure = Assert.Single(failures);

        Assert.Equal(UpdateRowValidationKind.NoSuchColumn, failure.Kind);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, failure.ReturnCode);
        Assert.Equal(columnName, failure.ColumnName);
        Assert.Equal(AgeColumn, failure.ColumnId);

        // NO TYPE IS REPORTED, because a column this DataWindow does not carry has no declared type and
        // reporting one would be an invention.
        Assert.Equal(string.Empty, failure.ColumnType);

        // THE ADDRESS TRAVELS AS DATA AND ALSO IN THE ARGUMENTS, so a consumer can either read the
        // fields or re-render the template in its own locale.
        Assert.Equal([columnName, AgeColumn.ToString(CultureInfo.InvariantCulture)],
            failure.Error.FormatArguments);
        Assert.Equal(UpdateRowValidator.UnknownColumnTitle, failure.Error.Title);

        // ⚠ THE WHOLE RENDERED SENTENCE, NOT MERELY THAT THE NAME APPEARS IN IT. A containment assertion
        // passed while the sentence read "named column '' at ordinal no_such_column": the ported Sprintf
        // numbers its placeholders from ONE and renders an explicit {0} as the empty string, so a
        // zero-based template silently dropped its first argument and shifted the second into its place.
        // Nothing threw and the name was still present, which is exactly why containment was not enough.
        Assert.Equal(
            $"The request named column '{columnName}' at ordinal {AgeColumn}, which this DataWindow does "
            + "not carry at that ordinal. A column is addressed by its one-based ordinal, and the name "
            + "must be the name that ordinal carries; no value was applied.",
            failure.Error.Text);

        // A DEFINED ERROR CLAIMS NO ORACLE. It has no translation table behind it, so it claims no
        // category and does not report itself localized - saying otherwise would tell a characterization
        // comparison that a legacy table answered for this text.
        Assert.False(failure.Error.Localized);
        Assert.Equal(0L, failure.Error.LocalizationCategory);
    }

    /// <summary>
    /// A column name resolves CASE-INSENSITIVELY, which is PowerBuilder's own behaviour.
    /// </summary>
    /// <param name="columnName">The spelling the caller used.</param>
    /// <remarks>
    /// PowerScript identifiers and DataWindow object names are case-insensitive, so <c>AGE</c> and
    /// <c>Age</c> address the column the definition spells <c>age</c> [<c>dw_sqlite.srd:L10</c>]. Pinned
    /// as a POSITIVE rather than left implicit, because a validator that resolved ordinally would refuse
    /// these three - turning a payload the oracle accepts into a 400, which is the opposite of the fault
    /// this validator exists to fix.
    /// </remarks>
    [Theory]
    [InlineData("age")]
    [InlineData("AGE")]
    [InlineData("Age")]
    public void AColumnNameResolvesCaseInsensitivelyAsPowerBuilderDoes(string columnName)
    {
        Assert.Empty(Validate(
            Row(1L, Column(columnName, AgeColumn, new AnyValue { DoubleValue = 30d }))));
    }

    /// <summary>
    /// ⚠ TWO REAL IDENTIFIERS THAT DISAGREE ARE REFUSED, because the receiving codec is positional.
    /// </summary>
    /// <remarks>
    /// This is the fault that MISDIRECTS rather than loses: the value lands in the column the ORDINAL
    /// addresses while the caller believes it wrote to the column the NAME addresses. Left alone, a
    /// caller sending <c>{name:"age", columnId:2}</c> writes an age into the name column and is told the
    /// update succeeded.
    /// </remarks>
    [Fact]
    public void ARealNamePairedWithAForeignOrdinalIsRefusedAsADisagreement()
    {
        UpdateRowValidationFailure failure = Assert.Single(Validate(
            Row(1L, Column("age", NameColumn, new AnyValue { DoubleValue = 30d }))));

        Assert.Equal(UpdateRowValidationKind.OrdinalDisagreesWithName, failure.Kind);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, failure.ReturnCode);
        Assert.Equal("age", failure.ColumnName);
        Assert.Equal(NameColumn, failure.ColumnId);
    }

    /// <summary>
    /// A name paired with its OWN ordinal, and a name paired with no ordinal at all, both resolve.
    /// </summary>
    /// <param name="columnId">The ordinal sent beside the name.</param>
    /// <remarks>
    /// ORDINAL ZERO IS "NOT STATED" RATHER THAN "COLUMN ZERO". The contract marks the name optional and
    /// the ordinal authoritative, and a caller that addresses by name alone leaves the ordinal at its
    /// proto default - refusing that would refuse the by-name form the REST projection uses.
    /// </remarks>
    [Theory]
    [InlineData(AgeColumn)]
    [InlineData(0L)]
    public void ANameThatAgreesWithItsOrdinalOrStatesNoneResolves(long columnId)
    {
        Assert.Empty(Validate(Row(1L, Column("age", columnId, new AnyValue { DoubleValue = 30d }))));
    }

    /// <summary>
    /// A POSITIONAL ROW - no name at all - is validated by its ordinal alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A row projected from a carrier changeset carries ordinals only, so an empty name is the legacy's
    /// own serialization shape rather than a malformed request
    /// [<c>common.v1.ColumnValue.column_name</c>]. Refusing it would refuse the changeset path entirely.
    /// </para>
    /// <para>
    /// BUT THE VALUE IS STILL CHECKED, which the second half asserts: the ordinal is mapped back to its
    /// declared type through <c>DataWindow.Objects</c>, so a positional row gets the same value test a
    /// named one does. Without that, a caller could bypass validation simply by omitting names.
    /// </para>
    /// </remarks>
    [Fact]
    public void APositionalRowIsValidatedByOrdinalAloneAndItsValueIsStillChecked()
    {
        Assert.Empty(Validate(
            Row(1L, Column(string.Empty, AgeColumn, new AnyValue { DoubleValue = 30d }))));

        UpdateRowValidationFailure failure = Assert.Single(Validate(
            Row(1L, Column(string.Empty, AgeColumn, new AnyValue { StringValue = "not-a-number" }))));

        Assert.Equal(UpdateRowValidationKind.InvalidValue, failure.Kind);
        Assert.Equal(string.Empty, failure.ColumnName);
        Assert.Equal(AgeColumn, failure.ColumnId);
        Assert.Equal("number", failure.ColumnType);
    }

    /// <summary>
    /// An ordinal outside the definition is left to the receiving codec rather than refused twice.
    /// </summary>
    /// <remarks>
    /// <c>Buffers/ChangesetCodec.TryReadRow</c> already refuses an out-of-range ordinal on its own
    /// authority. A second test here would create a second place for one rule and a second answer to
    /// reconcile, so this layer reports NOTHING for such a reference - asserted so the silence is
    /// deliberate and legible rather than an oversight.
    /// </remarks>
    [Fact]
    public void AnOrdinalOutsideTheDefinitionIsNotThisLayersRefusal()
    {
        Assert.Empty(Validate(
            Row(1L, Column(string.Empty, 99L, new AnyValue { StringValue = "anything" }))));
    }

    // ==============================================================================================
    //  2. VALUES - THE FINDING ITSELF
    // ==============================================================================================

    /// <summary>
    /// 🔴 TEXT THE DECLARED TYPE CANNOT HOLD IS REFUSED WITH THE ORACLE'S OWN DIALOG.
    /// </summary>
    /// <param name="columnName">The column.</param>
    /// <param name="columnId">Its ordinal.</param>
    /// <param name="text">The text the caller sent.</param>
    /// <param name="declaredType">The type the definition declares for it.</param>
    /// <remarks>
    /// These four rows are the finding: each of them previously reached the provider. The two numeric
    /// rows were the ones observed answering a 502 naming a database constraint; the two temporal rows
    /// are the same fault in the family whose coercion genuinely throws.
    /// </remarks>
    [Theory]
    [InlineData("age", AgeColumn, "not-a-number", "number")]
    [InlineData("salary", SalaryColumn, "abc", "decimal(2)")]
    [InlineData("birth", BirthColumn, "31/02/1984", "date")]
    [InlineData("birth", BirthColumn, "yesterday", "date")]
    public void TextTheDeclaredTypeCannotHoldIsRefusedWithTheOraclesOwnDialog(
        string columnName,
        long columnId,
        string text,
        string declaredType)
    {
        UpdateRowValidationFailure failure = Assert.Single(Validate(
            Row(1L, Column(columnName, columnId, new AnyValue { StringValue = text }))));

        Assert.Equal(UpdateRowValidationKind.InvalidValue, failure.Kind);

        // E_INVALID_DATA AND NOT E_INVALID_ARGUMENT: the request is well formed and the DATA is not,
        // and this is the code the ported reporting coercions themselves answer.
        Assert.Equal(RetCode.E_INVALID_DATA, failure.ReturnCode);
        Assert.Equal(columnName, failure.ColumnName);
        Assert.Equal(columnId, failure.ColumnId);
        Assert.Equal(declaredType, failure.ColumnType);

        // THE ORACLE'S DIALOG, FIELD BY FIELD [se_cst_dw.sru:L355-L358]. The body is the translated
        // source with "!" appended - translate THEN append, never the reverse - the title is looked up
        // separately, the severity is StopSign, the category is CAT_DWSVC, and there are NO substitution
        // arguments because the oracle substitutes none here.
        Assert.Equal("输入了无效的值!", failure.Error.Text);
        Assert.Equal("错误", failure.Error.Title);
        Assert.Equal(DialogSeverity.StopSign, failure.Error.Severity);
        Assert.Equal(Categories.CAT_DWSVC, failure.Error.LocalizationCategory);
        Assert.True(failure.Error.Localized);
        Assert.Empty(failure.Error.FormatArguments);

        // AND THE REJECTED VALUE IS NOWHERE IN THE MESSAGE. A refusal that echoed the offending text
        // would put caller data into a log line and into every consumer's error surface.
        Assert.DoesNotContain(text, failure.Error.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Text that genuinely IS the declared type is accepted.
    /// </summary>
    /// <param name="columnName">The column.</param>
    /// <param name="columnId">Its ordinal.</param>
    /// <param name="text">The text.</param>
    /// <remarks>
    /// THE CONTROLS THAT STOP THE VALIDATOR BECOMING A BLANKET REFUSAL. Every row here previously
    /// travelled to the provider and must continue to; a validator that refused any of them would break
    /// the ordinary path while closing the finding. The leading- and trailing-space rows are deliberate:
    /// the numeric coercion styles allow surrounding whitespace around a number even though the name
    /// resolver allows none around an identifier, and those two answers are measured rather than assumed.
    /// </remarks>
    [Theory]
    [InlineData("age", AgeColumn, "30")]
    [InlineData("age", AgeColumn, "30.5")]
    [InlineData("age", AgeColumn, " 30 ")]
    [InlineData("salary", SalaryColumn, "1250.75")]
    [InlineData("salary", SalaryColumn, "-1")]
    [InlineData("salary", SalaryColumn, "1e3")]
    [InlineData("birth", BirthColumn, "1984-02-29")]
    [InlineData("name", NameColumn, "")]
    public void TextThatIsTheDeclaredTypeIsAccepted(string columnName, long columnId, string text)
    {
        Assert.Empty(Validate(Row(1L, Column(columnName, columnId, new AnyValue { StringValue = text }))));
    }

    /// <summary>
    /// ⚠ BLANK TEXT IS REFUSED BY THE NUMERIC ARM AND ACCEPTED BY THE DATE ARM, and that disagreement is
    /// the legacy's own.
    /// </summary>
    /// <param name="columnName">The column.</param>
    /// <param name="columnId">Its ordinal.</param>
    /// <param name="text">The blank text.</param>
    /// <param name="accepted">Whether this column accepts it.</param>
    /// <remarks>
    /// <para>
    /// <b>MEASURED, NOT DESIGNED, AND PRESERVED UNDER C-B.</b> The five ported validators handle blank
    /// input four different ways - <c>DateValidator.TryCoerce</c> short-circuits on
    /// <see cref="string.IsNullOrWhiteSpace"/> and reports absence, while <c>NumberValidator</c>'s
    /// reporting coercions have no such branch and report invalid data. That inconsistency is the
    /// legacy's and is not harmonised here; a validator that "tidied" it would change behaviour on a path
    /// the finding never asked about.
    /// </para>
    /// <para>
    /// AND IT IS THE RIGHT ANSWER FOR THE NUMERIC ARM ANYWAY. The contract is explicit that
    /// <c>is_null = true</c> and <c>string_value = ""</c> ARE DIFFERENT VALUES and that a producer must
    /// never substitute one for the other, because collapsing them would widen the optimistic-concurrency
    /// WHERE clause and overwrite a row that should have conflicted [<c>common.v1.AnyValue</c>]. Empty
    /// text is therefore text that is not a number, and clearing a numeric cell is spelled with the null
    /// arm - which the null case above asserts is always accepted.
    /// </para>
    /// <para>
    /// <c>datetime</c> AND <c>time</c> ARE NOT PINNED HERE, because neither catalogue definition declares
    /// a column of either type and inventing one would fabricate a fixture. Their own blank-input answers
    /// are pinned directly on the validators in <c>ValidatorParityTests</c>.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("age", AgeColumn, "", false)]
    [InlineData("age", AgeColumn, "   ", false)]
    [InlineData("salary", SalaryColumn, "", false)]
    [InlineData("birth", BirthColumn, "", true)]
    [InlineData("birth", BirthColumn, "   ", true)]
    public void BlankTextIsRefusedByTheNumericArmAndAcceptedByTheDateArm(
        string columnName,
        long columnId,
        string text,
        bool accepted)
    {
        ImmutableArray<UpdateRowValidationFailure> failures =
            Validate(Row(1L, Column(columnName, columnId, new AnyValue { StringValue = text })));

        if (accepted)
        {
            Assert.Empty(failures);

            return;
        }

        UpdateRowValidationFailure failure = Assert.Single(failures);

        Assert.Equal(UpdateRowValidationKind.InvalidValue, failure.Kind);
        Assert.Equal(RetCode.E_INVALID_DATA, failure.ReturnCode);
    }

    /// <summary>
    /// A typed arm fits its own family and is refused outside it.
    /// </summary>
    /// <param name="columnName">The column.</param>
    /// <param name="columnId">Its ordinal.</param>
    /// <param name="value">The typed value.</param>
    /// <param name="accepted">Whether the column can hold it.</param>
    /// <remarks>
    /// <para>
    /// A NUMBER IS NOT A DATE AND A DATE IS NOT A NUMBER. The legacy's date coercion takes edit TEXT
    /// [<c>se_cst_dw.sru:L240-L241</c>], so there is no reading under which an <c>int64</c> is a date -
    /// accepting one would mean choosing an epoch, which is a guess (AAP 0.1.5 forbids widening with
    /// one).
    /// </para>
    /// <para>
    /// A BOOLEAN AND A BLOB ARE REFUSED IN BOTH FAMILIES, because the legacy has no coercion from either
    /// and there is therefore no behaviour to preserve.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(TypedArms))]
    public void ATypedArmFitsItsOwnFamilyOnly(
        string columnName,
        long columnId,
        AnyValue value,
        bool accepted)
    {
        ImmutableArray<UpdateRowValidationFailure> failures =
            Validate(Row(1L, Column(columnName, columnId, value)));

        if (accepted)
        {
            Assert.Empty(failures);

            return;
        }

        Assert.Equal(UpdateRowValidationKind.InvalidValue, Assert.Single(failures).Kind);
    }

    /// <summary>The typed-arm matrix.</summary>
    /// <returns>Column, ordinal, value and whether it is accepted.</returns>
    public static TheoryData<string, long, AnyValue, bool> TypedArms() => new()
    {
        // The four numeric arms into a numeric column.
        { "age", AgeColumn, new AnyValue { Int64Value = 30L }, true },
        { "age", AgeColumn, new AnyValue { Uint64Value = 30UL }, true },
        { "age", AgeColumn, new AnyValue { DoubleValue = 30.5d }, true },
        { "salary", SalaryColumn, new AnyValue { DecimalValue = new DecimalValue { Value = "1250.75" } }, true },

        // The same four into a temporal column.
        { "birth", BirthColumn, new AnyValue { Int64Value = 30L }, false },
        { "birth", BirthColumn, new AnyValue { DoubleValue = 30d }, false },

        // A date into its own column, and into a numeric one.
        { "birth", BirthColumn, Date(1984, 2, 29), true },
        { "age", AgeColumn, Date(1984, 2, 29), false },

        // A datetime is NOT a date: accepting it would either truncate the time the caller sent or
        // widen the column, and both store something other than what arrived.
        { "birth", BirthColumn, Midnight(1984, 2, 29), false },

        // Neither family has a coercion from a boolean or a blob.
        { "age", AgeColumn, new AnyValue { BoolValue = true }, false },
        { "birth", BirthColumn, new AnyValue { BoolValue = false }, false },
        { "age", AgeColumn, new AnyValue { BlobValue = Google.Protobuf.ByteString.CopyFrom(1, 2) }, false },
    };

    /// <summary>
    /// NULL IS ALWAYS ACCEPTED, in every spelling the contract allows.
    /// </summary>
    /// <remarks>
    /// <b>C-B, AND THE REASON IS EVIDENCE RATHER THAN CAUTION.</b> Whether a column may be null is a
    /// SCHEMA fact: the DDL declares <c>NAME TEXT NOT NULL</c> and <c>AGE INT NOT NULL</c>
    /// [<c>w_test_sqlite.srw:L463-L469</c>] while the DataWindow definition declares no required flag on
    /// any column [<c>dw_sqlite.srd:L8-L13</c>]. This layer sees only the definition, so refusing null
    /// here would be inventing a constraint it cannot know - the engine's own refusal is what carries it,
    /// classified as a caller fault by <c>ConflictDetector.IsCallerConstraintViolation</c>.
    /// </remarks>
    [Fact]
    public void NullIsAcceptedInEverySpellingTheContractAllows()
    {
        Assert.Empty(Validate(Row(
            1L,
            Column("name", NameColumn, new AnyValue { IsNull = true }),
            Column("age", AgeColumn, new AnyValue { IsNull = true }),
            Column("birth", BirthColumn, new AnyValue()),
            Column("salary", SalaryColumn, value: null))));
    }

    /// <summary>
    /// A <c>char</c> column takes any scalar, and its declared WIDTH is not enforced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PowerBuilder's coercion to string is total, so there is no text a char column refuses.
    /// </para>
    /// <para>
    /// <b>THE WIDTH ROW IS A PRESERVED DEFECT AND MUST STAY ACCEPTED (AAP 0.6.4).</b> The definition
    /// declares <c>address char(200)</c> over an <c>ADDRESS CHAR(50)</c> column, and that disagreement is
    /// one of the three the migration preserves rather than reconciles. Enforcing either number here
    /// would pick a side and change behaviour.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACharColumnTakesAnyScalarAndItsWidthIsNotEnforced()
    {
        Assert.Empty(Validate(Row(
            1L,
            Column("name", NameColumn, new AnyValue { StringValue = "Paul" }),
            Column("address", AddressColumn, new AnyValue { Int64Value = 42L }),
            Column("name", NameColumn, new AnyValue { BoolValue = true }))));

        Assert.Empty(Validate(Row(
            1L,
            Column("address", AddressColumn, new AnyValue { StringValue = new string('x', 300) }))));
    }

    // ==============================================================================================
    //  3. WHAT THE VALIDATOR REPORTS ACROSS A WHOLE PAYLOAD
    // ==============================================================================================

    /// <summary>
    /// EVERY FAULT IS COLLECTED, in request order, across rows and buffers.
    /// </summary>
    /// <remarks>
    /// Stopping at the first fault turns one correction into as many round trips as there are faults.
    /// The ORDER is asserted too, because a consumer rendering these beside its own input needs them to
    /// line up with the payload it sent.
    /// </remarks>
    [Fact]
    public void EveryFaultIsCollectedInRequestOrder()
    {
        ImmutableArray<UpdateRowValidationFailure> failures = Validate(
            Row(
                1L,
                Column("age", AgeColumn, new AnyValue { StringValue = "not-a-number" }),
                Column("name", NameColumn, new AnyValue { StringValue = "Paul" }),
                Column("no_such_column", 0L, new AnyValue { StringValue = "x" })),
            Row(
                7L,
                Column("salary", SalaryColumn, new AnyValue { StringValue = "abc" }),
                Column("age", NameColumn, new AnyValue { DoubleValue = 1d }),
                DwBuffer.Filter));

        Assert.Equal(4, failures.Length);

        Assert.Equal((1L, "age", UpdateRowValidationKind.InvalidValue, DwBuffer.Primary),
            (failures[0].Row, failures[0].ColumnName, failures[0].Kind, failures[0].Buffer));
        Assert.Equal((1L, "no_such_column", UpdateRowValidationKind.NoSuchColumn, DwBuffer.Primary),
            (failures[1].Row, failures[1].ColumnName, failures[1].Kind, failures[1].Buffer));
        Assert.Equal((7L, "salary", UpdateRowValidationKind.InvalidValue, DwBuffer.Filter),
            (failures[2].Row, failures[2].ColumnName, failures[2].Kind, failures[2].Buffer));
        Assert.Equal((7L, "age", UpdateRowValidationKind.OrdinalDisagreesWithName, DwBuffer.Filter),
            (failures[3].Row, failures[3].ColumnName, failures[3].Kind, failures[3].Buffer));
    }

    /// <summary>
    /// ⚠ ORIGINAL VALUES ARE NEVER VALIDATED, even when they hold something no coercion accepts.
    /// </summary>
    /// <remarks>
    /// An original describes what STORAGE held, not what the caller is writing - it is compared and
    /// never applied. And storage genuinely can hold such a value: SQLite keeps text an INTEGER-affinity
    /// column cannot convert AS TEXT, so a retrieval answers it back (see the read-back guard in
    /// <c>Data/DataWindowRuntime.ReadValue</c>). Validating it would refuse a payload assembled
    /// correctly from this service's own answer, which is the one payload that must always be accepted.
    /// </remarks>
    [Fact]
    public void AnOriginalValueIsNeverValidatedEvenWhenNoCoercionAcceptsIt()
    {
        DataWindowRow row = Row(
            1L,
            Column("age", AgeColumn, new AnyValue { DoubleValue = 31d }));

        row.OriginalValues.Add(new ColumnValue
        {
            ColumnName = "age",
            ColumnId = AgeColumn,
            Value = new AnyValue { StringValue = "not-a-number" },
        });

        // AND AN ORIGINAL NAMING A COLUMN THAT DOES NOT EXIST IS EQUALLY UNTOUCHED, so the exemption is
        // the whole collection rather than only its value arm.
        row.OriginalValues.Add(new ColumnValue
        {
            ColumnName = "no_such_column",
            ColumnId = 99L,
            Value = new AnyValue { StringValue = "x" },
        });

        Assert.Empty(Validate(row));
    }

    /// <summary>
    /// A well-formed six-column payload is accepted, in both the named and the positional form.
    /// </summary>
    /// <remarks>
    /// THE END-TO-END CONTROL. Every other case here asserts a refusal; without this one a validator that
    /// refused everything would pass the whole suite. The values are the oracle's own first seeded row
    /// [<c>w_test_sqlite.srw:L381-L388</c>].
    /// </remarks>
    [Fact]
    public void TheEvidencedSixColumnPayloadIsAcceptedNamedAndPositional()
    {
        Assert.Empty(Validate(Row(
            1L,
            Column("id", IdColumn, new AnyValue { DoubleValue = 1d }),
            Column("name", NameColumn, new AnyValue { StringValue = "Paul" }),
            Column("age", AgeColumn, new AnyValue { DoubleValue = 32d }),
            Column("address", AddressColumn, new AnyValue { StringValue = "California" }),
            Column("salary", SalaryColumn, new AnyValue { DecimalValue = new DecimalValue { Value = "20000.00" } }),
            Column("birth", BirthColumn, Date(1999, 5, 8)))));

        Assert.Empty(Validate(Row(
            1L,
            Column(string.Empty, IdColumn, new AnyValue { DoubleValue = 1d }),
            Column(string.Empty, NameColumn, new AnyValue { StringValue = "Paul" }),
            Column(string.Empty, AgeColumn, new AnyValue { DoubleValue = 32d }),
            Column(string.Empty, AddressColumn, new AnyValue { StringValue = "California" }),
            Column(string.Empty, SalaryColumn, new AnyValue { DecimalValue = new DecimalValue { Value = "20000.00" } }),
            Column(string.Empty, BirthColumn, Date(1999, 5, 8)))));
    }

    /// <summary>
    /// An empty payload is accepted rather than refused, and neither argument may be null.
    /// </summary>
    /// <remarks>
    /// An update carrying no row is a caller question the UPDATE path answers - the carrier refuses a row
    /// with nothing assignable with its own code - and pre-empting it here would move one refusal into
    /// two places. The null guards are asserted because this validator is constructed per request and a
    /// missing collaborator must fail at the boundary rather than inside a loop.
    /// </remarks>
    [Fact]
    public void AnEmptyPayloadIsAcceptedAndNeitherArgumentMayBeNull()
    {
        Assert.Empty(new UpdateRowValidator(new I18n()).Validate(Host(), []));
        Assert.Empty(Validate(Row(1L)));

        _ = Assert.Throws<ArgumentNullException>(
            () => new UpdateRowValidator(new I18n()).Validate(null!, []));
        _ = Assert.Throws<ArgumentNullException>(
            () => new UpdateRowValidator(new I18n()).Validate(Host(), null!));
        _ = Assert.Throws<ArgumentNullException>(() => new UpdateRowValidator(null!));
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>The evidenced fixture's host, built through the shipped factory.</summary>
    /// <returns>A host over <c>dw_sqlite</c>.</returns>
    private static DataWindowServiceHost Host()
    {
        DataWindowServiceHost? host =
            new HeadlessDataWindowHostFactory(new DataWindowCatalogue())
                .Create(DataWindowCatalogue.SqliteFixtureName);

        Assert.NotNull(host);

        return host!;
    }

    /// <summary>Validates rows against the evidenced fixture with no localization provider installed.</summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The failures.</returns>
    /// <remarks>
    /// NO PROVIDER IS INSTALLED, which is the oracle's own uninstalled state: the localization facade's
    /// SILENT PASSTHROUGH returns the untranslated source unchanged, throwing nothing and logging nothing
    /// [<c>i18n.srf:L17-L18</c>]. That is why the expected texts above are the Chinese sources.
    /// </remarks>
    private static ImmutableArray<UpdateRowValidationFailure> Validate(params DataWindowRow[] rows) =>
        new UpdateRowValidator(new I18n()).Validate(Host(), rows);

    /// <summary>Builds a row.</summary>
    /// <param name="row">The one-based row ordinal.</param>
    /// <param name="columns">Its columns, in request order.</param>
    /// <returns>The row, in the Primary buffer.</returns>
    private static DataWindowRow Row(long row, params ColumnValue[] columns)
    {
        DataWindowRow built = new() { Buffer = DwBuffer.Primary, Row = row };
        built.Columns.AddRange(columns);

        return built;
    }

    /// <summary>Builds a row in a named buffer, with the buffer LAST so it reads at the call site.</summary>
    /// <param name="row">The one-based row ordinal.</param>
    /// <param name="first">Its first column.</param>
    /// <param name="second">Its second column.</param>
    /// <param name="buffer">The buffer.</param>
    /// <returns>The row.</returns>
    private static DataWindowRow Row(long row, ColumnValue first, ColumnValue second, DwBuffer buffer)
    {
        DataWindowRow built = new() { Buffer = buffer, Row = row };
        built.Columns.AddRange([first, second]);

        return built;
    }

    /// <summary>Builds a column reference.</summary>
    /// <param name="name">The name; may be empty for a positional reference.</param>
    /// <param name="columnId">The one-based ordinal, or zero for "not stated".</param>
    /// <param name="value">The value; may be null.</param>
    /// <returns>The reference.</returns>
    private static ColumnValue Column(string name, long columnId, AnyValue? value)
    {
        ColumnValue column = new() { ColumnName = name, ColumnId = columnId };

        if (value is not null)
        {
            column.Value = value;
        }

        return column;
    }

    /// <summary>A date-arm value.</summary>
    /// <param name="year">The year.</param>
    /// <param name="month">The month.</param>
    /// <param name="day">The day.</param>
    /// <returns>The value.</returns>
    private static AnyValue Date(int year, int month, int day) => new()
    {
        // CANONICAL ISO 8601 "yyyy-MM-dd", ZERO-PADDED, which is the only form common.v1.DateValue
        // accepts - the temporal messages carry canonical TEXT rather than component fields so that two
        // encodings of one date are byte-identical for a golden-master comparison.
        DateValue = new DateValue
        {
            Value = new DateOnly(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        },
    };

    /// <summary>A datetime-arm value at midnight.</summary>
    /// <param name="year">The year.</param>
    /// <param name="month">The month.</param>
    /// <param name="day">The day.</param>
    /// <returns>The value.</returns>
    private static AnyValue Midnight(int year, int month, int day) => new()
    {
        // "yyyy-MM-ddTHH:mm:ss", with the fractional part omitted because it is zero - the canonical form
        // common.v1.DateTimeValue declares, unzoned to match a legacy that has no time-zone concept.
        DatetimeValue = new DateTimeValue
        {
            Value = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified)
                .ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        },
    };
}
