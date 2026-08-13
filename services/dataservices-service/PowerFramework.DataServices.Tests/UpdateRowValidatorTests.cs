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
    /// These four rows are the ones an unvalidated path lets reach the provider. The two numeric
    /// rows are the pair observed answering a 502 naming a database constraint; the two temporal rows
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
    /// THE CONTROLS THAT STOP THE VALIDATOR BECOMING A BLANKET REFUSAL. Every row here travels
    /// to the provider and must continue to; a validator that refused any of them would break
    /// the ordinary path while closing the hole. The leading- and trailing-space rows are deliberate:
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

    /// <summary>
    /// 🔴 A BLOB IS NOT A SCALAR AND A <c>char</c> COLUMN REFUSES IT, which "any scalar" above did not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CHAR ARM USED TO ACCEPT EVERY ARM WITHOUT LOOKING, and a blob was the one that mattered. There
    /// is NO legacy coercion from <c>blob</c> to <c>string</c> - PowerScript requires an explicit codec
    /// call, which is a different operation with a different result - so accepting one here invents
    /// behaviour the oracle does not have. What actually happened downstream was worse than an invention:
    /// the bytes were bound to a character column verbatim, so a write-then-read answered a value the
    /// caller never sent, with nothing in the response saying so.
    /// </para>
    /// <para>
    /// BOTH CHAR COLUMNS ARE DRIVEN, because they are declared with different widths
    /// [<c>dw_sqlite.srd:L9</c>, <c>:L11</c>] and the refusal must be about the TYPE rather than about
    /// either width - the width is a preserved defect and is still not enforced, which the case above
    /// pins.
    /// </para>
    /// </remarks>
    [Fact]
    public void ABlobIsRefusedByACharColumn()
    {
        foreach ((string name, long ordinal) in ((string, long)[])[("name", NameColumn), ("address", AddressColumn)])
        {
            UpdateRowValidationFailure failure = Assert.Single(Validate(Row(
                1L,
                Column(name, ordinal, new AnyValue
                {
                    BlobValue = Google.Protobuf.ByteString.CopyFrom(0x00, 0x01, 0xFF),
                }))));

            Assert.Equal(UpdateRowValidationKind.InvalidValue, failure.Kind);
            Assert.Equal(RetCode.E_INVALID_DATA, failure.ReturnCode);
            Assert.Equal(name, failure.ColumnName);
        }

        // AND A STRING IS STILL ACCEPTED BY THE SAME COLUMN, so the refusal is about the arm and not
        // about the column.
        Assert.Empty(Validate(Row(
            1L,
            Column("name", NameColumn, new AnyValue { StringValue = "Paul" }))));
    }

    /// <summary>
    /// 🔴 A <c>NaN</c> IS REFUSED BY EVERY NUMERIC COLUMN - AND THE TWO INFINITIES ARE NOT, because the
    /// published contract accepts them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS CASE USED TO REFUSE ALL THREE, AND THAT WAS THE DEFECT IT NOW GUARDS AGAINST.</b>
    /// <c>common.v1.AnyValue.double_value</c> states the rule and Persistence implements exactly it: NaN is
    /// answered with a defined refusal before a statement is generated, while both infinities bind and
    /// store. Refusing an infinity HERE made acceptance depend on the route a value travelled - the same
    /// <c>double_value</c> was admitted on a direct C-06 update and rejected through this service - which
    /// is precisely the hop-dependent semantics a shared published contract exists to prevent.
    /// </para>
    /// <para>
    /// <b>THE NaN REFUSAL IS NOT SYMMETRY WITH THE INFINITIES; IT IS THE PROVIDER'S OWN DISTINCTION,
    /// MEASURED.</b> Binding <c>double.NaN</c> raises a provider FAULT rather than a database error, so it
    /// would escape the update walk as an undiagnosed <c>Internal</c> and could half-apply a multi-row
    /// payload; refusing it at this seam turns that into <c>E_INVALID_DATA</c> per column. A deliberate
    /// contract narrowing, and the legacy has no NaN at all to preserve - PowerScript has no literal for
    /// one.
    /// </para>
    /// <para>
    /// The three columns are the fixture's own numeric ones, and all three are <c>Dec()</c> columns
    /// [<c>dw_sqlite.srd:L8, L10, L12</c>]. An infinity into an INTEGRAL column is a different question and
    /// is answered by the out-of-domain arm, which
    /// <see cref="AnIntegralColumnRefusesAFractionalOrOutOfDomainValue"/> asserts.
    /// </para>
    /// </remarks>
    [Fact]
    public void ANotANumberDoubleIsRefusedByANumericColumnAndTheInfinitiesAreNot()
    {
        foreach ((string name, long ordinal) in ((string, long)[])
            [("id", IdColumn), ("age", AgeColumn), ("salary", SalaryColumn)])
        {
            UpdateRowValidationFailure failure = Assert.Single(Validate(Row(
                1L,
                Column(name, ordinal, new AnyValue { DoubleValue = double.NaN }))));

            Assert.Equal(UpdateRowValidationKind.InvalidValue, failure.Kind);
            Assert.Equal(RetCode.E_INVALID_DATA, failure.ReturnCode);

            // AND BOTH INFINITIES TRAVEL, on the very same column, so the refusal above is about NaN
            // rather than about the arm or the column.
            Assert.Empty(Validate(Row(
                1L,
                Column(name, ordinal, new AnyValue { DoubleValue = double.PositiveInfinity }))));

            Assert.Empty(Validate(Row(
                1L,
                Column(name, ordinal, new AnyValue { DoubleValue = double.NegativeInfinity }))));
        }

        // THE NEGATIVE CONTROL FOR THE SAME COLUMNS: a finite double is accepted, including a fractional
        // one, because `number` and `decimal(n)` are Dec() columns and hold fractions perfectly well.
        Assert.Empty(Validate(Row(
            1L,
            Column("age", AgeColumn, new AnyValue { DoubleValue = 32.5d }),
            Column("salary", SalaryColumn, new AnyValue { DoubleValue = -1250.75d }))));
    }

    /// <summary>
    /// 🔴 A DECIMAL ARM WHOSE CANONICAL TEXT DOES NOT PARSE IS REFUSED HERE, not left to the codec.
    /// </summary>
    /// <param name="text">The decimal arm's text.</param>
    /// <remarks>
    /// <c>common.v1.DecimalValue</c> carries canonical TEXT rather than a numeric field, so an unparseable
    /// one is possible on the wire. Refusing it here answers a PER-COLUMN fault; leaving it to the
    /// receiving codec answers a generic invalid-payload code for the whole request, which tells a caller
    /// nothing about which column to correct.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("1,250.75")]
    [InlineData("12 34")]
    public void AnUnparseableDecimalArmIsRefused(string text)
    {
        UpdateRowValidationFailure failure = Assert.Single(Validate(Row(
            1L,
            Column("salary", SalaryColumn, new AnyValue
            {
                DecimalValue = new DecimalValue { Value = text },
            }))));

        Assert.Equal(UpdateRowValidationKind.InvalidValue, failure.Kind);

        // AND THE CANONICAL SPELLINGS ARE ACCEPTED, including a negative and an exponent, so the refusal
        // is about parseability rather than about a narrow format.
        Assert.Empty(Validate(Row(
            1L,
            Column("salary", SalaryColumn, new AnyValue { DecimalValue = new DecimalValue { Value = "-1250.75" } }),
            Column("age", AgeColumn, new AnyValue { DecimalValue = new DecimalValue { Value = "3E2" } }))));
    }

    /// <summary>
    /// 🔴 AN INTEGRAL COLUMN REFUSES A VALUE ITS OWN <c>Long()</c> COERCION WOULD SILENTLY TRUNCATE OR
    /// WRAP, and the identical values are accepted by a <c>Dec()</c> column.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE SUBTYPE DECIDES, WHICH IS WHY THE FAMILY TEST WAS NOT ENOUGH.</b> The legacy dispatches
    /// <c>long</c> and <c>ulong</c> columns to <c>Long()</c> [<c>se_cst_dw.sru:L236-L237</c>], and
    /// <c>Long()</c> TRUNCATES rather than rounds - so admitting 3.7 stores 3, a value the caller did not
    /// send, arrived at silently. A negative value for <c>ulong</c> is outside the type's domain outright,
    /// and a <c>uint64</c> past <see cref="long.MaxValue"/> cannot be held by the signed carrier at all.
    /// </para>
    /// <para>
    /// <b>THE HOST IS SYNTHETIC AND SAYS SO, BECAUSE THE ORACLE HAS NO SUCH COLUMN.</b> The one updatable
    /// DataWindow in the estate declares <c>number</c> and <c>decimal(2)</c> and nothing integral
    /// [<c>dw_sqlite.srd:L8-L13</c>], so these arms are unreachable through it - and adding an integral
    /// column to the transcribed catalogue to reach them would misrepresent the oracle (C-C). A test-only
    /// definition is the honest way in: the TYPE TOKENS are the oracle's own, read from
    /// <c>NumberValidator.LongCoercionColumnTypePrefixes</c>, and only the definition carrying them is
    /// this file's.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnIntegralColumnRefusesAFractionalOrOutOfDomainValue()
    {
        FakeDataWindowHost host = new();
        _ = host.AddColumn("signed", FakeColumnType.Long);
        _ = host.AddColumn("unsigned", FakeColumnType.ULong);
        _ = host.AddColumn("fractional", FakeColumnType.DecimalOf(2));

        const long Signed = 1L;
        const long Unsigned = 2L;
        const long Fractional = 3L;

        // A FRACTION INTO EITHER INTEGRAL COLUMN, spelled as a double and as a decimal, because the two
        // arms reach the same rule by different routes.
        foreach (AnyValue fraction in (AnyValue[])
            [
                new AnyValue { DoubleValue = 3.7d },
                new AnyValue { DecimalValue = new DecimalValue { Value = "3.7" } },
            ])
        {
            Assert.Equal(
                UpdateRowValidationKind.InvalidValue,
                Assert.Single(Validate(host, Row(1L, Column("signed", Signed, fraction)))).Kind);
            Assert.Equal(
                UpdateRowValidationKind.InvalidValue,
                Assert.Single(Validate(host, Row(1L, Column("unsigned", Unsigned, fraction)))).Kind);

            // 🔴 AND THE SAME VALUE IS FINE FOR A Dec() COLUMN, which is what makes this a subtype
            // decision rather than a blanket refusal of fractions.
            Assert.Empty(Validate(host, Row(1L, Column("fractional", Fractional, fraction))));
        }

        // A NEGATIVE FOR ulong, in all three spellings that can express one.
        foreach (AnyValue negative in (AnyValue[])
            [
                new AnyValue { Int64Value = -1L },
                new AnyValue { DoubleValue = -1d },
                new AnyValue { DecimalValue = new DecimalValue { Value = "-1" } },
            ])
        {
            Assert.Equal(
                UpdateRowValidationKind.InvalidValue,
                Assert.Single(Validate(host, Row(1L, Column("unsigned", Unsigned, negative)))).Kind);

            // The SIGNED column holds it, so the refusal is the domain and not the sign.
            Assert.Empty(Validate(host, Row(1L, Column("signed", Signed, negative))));
        }

        // A uint64 PAST long.MaxValue: refused by the signed column, held by the unsigned one.
        AnyValue beyond = new() { Uint64Value = (ulong)long.MaxValue + 1UL };

        Assert.Equal(
            UpdateRowValidationKind.InvalidValue,
            Assert.Single(Validate(host, Row(1L, Column("signed", Signed, beyond)))).Kind);
        Assert.Empty(Validate(host, Row(1L, Column("unsigned", Unsigned, beyond))));

        // A double magnitude past the signed domain is refused for the same reason.
        Assert.Equal(
            UpdateRowValidationKind.InvalidValue,
            Assert.Single(Validate(host, Row(
                1L,
                Column("signed", Signed, new AnyValue { DoubleValue = 1e19d })))).Kind);

        // 🔴 AND SO IS EITHER INFINITY, BY THE SAME DOMAIN TEST RATHER THAN BY A FAMILY RULE. The published
        // contract accepts both on `double_value` and Persistence stores them, so this validator does not
        // refuse them as a class - but no infinity is representable as a `long` or a `ulong`, so an
        // INTEGRAL column refuses one exactly as it refuses 1e19. The paired assertion that a Dec() column
        // ACCEPTS them is what makes this a subtype decision rather than a reinstated blanket refusal.
        foreach (AnyValue unbounded in (AnyValue[])
            [
                new AnyValue { DoubleValue = double.PositiveInfinity },
                new AnyValue { DoubleValue = double.NegativeInfinity },
            ])
        {
            Assert.Equal(
                UpdateRowValidationKind.InvalidValue,
                Assert.Single(Validate(host, Row(1L, Column("signed", Signed, unbounded)))).Kind);

            Assert.Equal(
                UpdateRowValidationKind.InvalidValue,
                Assert.Single(Validate(host, Row(1L, Column("unsigned", Unsigned, unbounded)))).Kind);

            Assert.Empty(Validate(host, Row(1L, Column("fractional", Fractional, unbounded))));
        }

        // AND WHOLE VALUES IN DOMAIN ARE ACCEPTED BY BOTH, so nothing above is a refusal of integers.
        Assert.Empty(Validate(
            host,
            Row(
                1L,
                Column("signed", Signed, new AnyValue { Int64Value = long.MinValue }),
                Column("unsigned", Unsigned, new AnyValue { Uint64Value = ulong.MaxValue }),
                Column("signed", Signed, new AnyValue { DoubleValue = -4d }),
                Column("unsigned", Unsigned, new AnyValue { DecimalValue = new DecimalValue { Value = "4.000" } }))));
    }

    /// <summary>
    /// 🔴 EVERY PUBLISHED VALUE ARM HAS A STATED DECISION, so an arm added to the contract cannot be
    /// admitted unvalidated by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE DEFAULT ARM USED TO ACCEPT, AND THAT IS THE FINDING RESTATED AS A RULE.</b> Accepting an
    /// unrecognised arm reads as generous on a READ path; on a WRITE path it inverts into precisely the
    /// defect this validator exists to prevent, because an arm the code cannot reason about is exactly an
    /// arm it cannot say is representable - and the value would then be bound to a column of a type
    /// nothing had checked it against.
    /// </para>
    /// <para>
    /// <b>ASSERTED AS SET EQUALITY AGAINST THE GENERATED ONE-OF, WHICH IS WHAT MAKES IT A GUARD RATHER
    /// THAN A SNAPSHOT.</b> Every value of <c>AnyValue.KindOneofCase</c> must appear in the table below, so
    /// widening <c>common.v1.AnyValue</c> fails this case until an author states the new arm's decision
    /// here - and the decision itself is asserted by running the validator, not merely recorded.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryPublishedValueArmHasAStatedDecisionForANumericColumn()
    {
        // The decision for `age`, declared `number` [dw_sqlite.srd:L10] - a Dec() column, so it takes the
        // four numeric arms and nothing else. `None` is the unset state and reads as "no value supplied",
        // which is accepted for the same reason an explicit null is.
        Dictionary<AnyValue.KindOneofCase, (AnyValue Value, bool Accepted)> decisions = new()
        {
            [AnyValue.KindOneofCase.None] = (new AnyValue(), true),
            [AnyValue.KindOneofCase.IsNull] = (new AnyValue { IsNull = true }, true),
            [AnyValue.KindOneofCase.Int64Value] = (new AnyValue { Int64Value = 30L }, true),
            [AnyValue.KindOneofCase.Uint64Value] = (new AnyValue { Uint64Value = 30UL }, true),
            [AnyValue.KindOneofCase.DoubleValue] = (new AnyValue { DoubleValue = 30d }, true),
            [AnyValue.KindOneofCase.DecimalValue] =
                (new AnyValue { DecimalValue = new DecimalValue { Value = "30" } }, true),
            [AnyValue.KindOneofCase.StringValue] = (new AnyValue { StringValue = "30" }, true),
            [AnyValue.KindOneofCase.BoolValue] = (new AnyValue { BoolValue = true }, false),
            [AnyValue.KindOneofCase.BlobValue] =
                (new AnyValue { BlobValue = Google.Protobuf.ByteString.CopyFrom(1, 2) }, false),
            [AnyValue.KindOneofCase.DateValue] = (Date(1999, 5, 8), false),
            [AnyValue.KindOneofCase.TimeValue] =
                (new AnyValue { TimeValue = new TimeValue { Value = "12:00:00" } }, false),
            [AnyValue.KindOneofCase.DatetimeValue] = (Midnight(1999, 5, 8), false),
        };

        // THE GUARD. A new arm on the published message lands here first.
        Assert.Equal(
            Enum.GetValues<AnyValue.KindOneofCase>().Order().ToArray(),
            decisions.Keys.Order().ToArray());

        foreach ((AnyValue.KindOneofCase arm, (AnyValue value, bool accepted)) in decisions)
        {
            // The arm the builder actually produced, so a table row cannot claim an arm it did not set.
            Assert.Equal(arm, value.KindCase);

            ImmutableArray<UpdateRowValidationFailure> failures =
                Validate(Row(1L, Column("age", AgeColumn, value)));

            if (accepted)
            {
                Assert.Empty(failures);

                continue;
            }

            Assert.Equal(UpdateRowValidationKind.InvalidValue, Assert.Single(failures).Kind);
        }
    }

    /// <summary>
    /// The three type disagreements between the DataWindow and the DDL are PRESERVED, because this layer
    /// validates against the definition and never against storage.
    /// </summary>
    /// <remarks>
    /// <b>C-B AND AAP 0.6.4, PINNED AS ONE CASE SO THE INTENT IS LEGIBLE.</b> The definition declares
    /// <c>address char(200)</c> over an <c>ADDRESS CHAR(50)</c> column, <c>salary decimal(2)</c> over a
    /// <c>SALARY REAL</c>, and <c>birth date</c> over a <c>BIRTH TEXT</c>
    /// [<c>dw_sqlite.srd:L11-L13</c> against <c>w_test_sqlite.srw:L463-L469</c>]. All three are defects the
    /// migration reproduces rather than reconciles, so a value that fits the DECLARED type is accepted here
    /// whatever storage would do with it - and a validator that consulted the DDL would pick a side and
    /// change behaviour.
    /// </remarks>
    [Fact]
    public void TheThreeDeclaredVersusStoredTypeMismatchesAreNotThisLayersRefusal()
    {
        Assert.Empty(Validate(Row(
            1L,

            // 200 declared characters into a 50-character column: accepted, width unenforced either way.
            Column("address", AddressColumn, new AnyValue { StringValue = new string('x', 200) }),

            // Two-place decimal into a REAL: accepted as the DECIMAL arm the definition declares.
            Column("salary", SalaryColumn, new AnyValue { DecimalValue = new DecimalValue { Value = "20000.00" } }),

            // A date into a TEXT column: accepted as the DATE arm the definition declares. The reverse -
            // a datetime into the same column - is still refused, because that is a DEFINITION mismatch
            // rather than a storage one.
            Column("birth", BirthColumn, Date(1999, 5, 8)))));

        Assert.Equal(
            UpdateRowValidationKind.InvalidValue,
            Assert.Single(Validate(Row(1L, Column("birth", BirthColumn, Midnight(1999, 5, 8))))).Kind);
    }

    // ==============================================================================================
    //  2b. THE CONCURRENCY BASELINE - A COLUMN THAT STATES NO ORIGINAL
    //  --------------------------------------------------------------------------------------------
    //  `updatewhere=1` builds the generated statement's where clause from the ORIGINAL value of every
    //  column the row carries [dw_sqlite.srd:L14, AAP 0.6.3.2], so a column with no stated original
    //  leaves the receiving codec with no baseline. Substituting the caller's own current value - which
    //  is what happened before this check existed - produces a predicate built from the values being
    //  WRITTEN: an ordinary update compares a row against itself and can never detect a lost race, and
    //  an update whose KEY changed addresses the row named by the NEW key. Both report success.
    //
    //  THE SCOPE IS THE PREDICATE AND NOT THE ROW SHAPE, which the cases below pin from both sides.
    // ==============================================================================================

    /// <summary>
    /// 🔴 A MODIFIED ROW THAT STATES NO ORIGINAL FOR A COLUMN IS REFUSED, and the refusal says what to
    /// send.
    /// </summary>
    [Fact]
    public void AModifiedRowThatStatesNoOriginalForAColumnIsRefused()
    {
        ImmutableArray<UpdateRowValidationFailure> failures = Validate(Row(
            1L,
            ItemStatus.DataModified,
            [
                Column("age", AgeColumn, new AnyValue { DoubleValue = 33d }),
                Column("name", NameColumn, new AnyValue { StringValue = "Paul" }),
            ],
            [Column("name", NameColumn, new AnyValue { StringValue = "Paul" })]));

        UpdateRowValidationFailure failure = Assert.Single(failures);

        Assert.Equal(UpdateRowValidationKind.MissingOriginalValue, failure.Kind);

        // THE STRUCTURAL CODE, because the payload's SHAPE is wrong rather than its value: a caller must
        // fix what it sends before its values mean anything.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, failure.ReturnCode);

        Assert.Equal("age", failure.ColumnName);
        Assert.Equal(AgeColumn, failure.ColumnId);
        Assert.Equal(DwBuffer.Primary, failure.Buffer);
        Assert.Equal(1L, failure.Row);

        // THE DECLARED TYPE IS REPORTED, unlike an unknown-column refusal - the column exists, so there is
        // a real type to report and a caller can see which column it is being asked about.
        Assert.Equal("number", failure.ColumnType);

        Assert.Equal(UpdateRowValidator.MissingOriginalTitle, failure.Error.Title);
        Assert.Equal(
            [failure.ColumnName, AgeColumn.ToString(CultureInfo.InvariantCulture)],
            failure.Error.FormatArguments);

        // ⚠ THE WHOLE RENDERED SENTENCE, for the same reason the unknown-column case asserts it: the
        // ported Sprintf numbers placeholders from ONE, so a zero-based template renders the ordinal where
        // the name belongs and nothing where the ordinal does, and a containment assertion would pass.
        Assert.Equal(
            $"Column 'age' at ordinal {AgeColumn} carries no original value. This row generates a "
            + "statement whose where clause is built from the ORIGINAL value of every column the row "
            + "carries, so each one must appear in original_values - echo the value the retrieval answered "
            + "for it. The requirement covers deleted rows and rows stamped DataModified; an inserted or "
            + "unchanged row needs no baseline. Nothing was applied.",
            failure.Error.Text);

        // A DEFINED ERROR CLAIMS NO ORACLE - no translation table stands behind this sentence, so it
        // reports itself untranslated and claims no category.
        Assert.False(failure.Error.Localized);
        Assert.Equal(0L, failure.Error.LocalizationCategory);

        // AND THE COMPLETE PAYLOAD IS ACCEPTED, which is the control: the requirement is one field per
        // column, and the retrieval the caller read the row from answered every one of them.
        Assert.Empty(Validate(Row(
            1L,
            ItemStatus.DataModified,
            [
                Column("age", AgeColumn, new AnyValue { DoubleValue = 33d }),
                Column("name", NameColumn, new AnyValue { StringValue = "Paul" }),
            ],
            [
                Column("age", AgeColumn, new AnyValue { DoubleValue = 32d }),
                Column("name", NameColumn, new AnyValue { StringValue = "Paul" }),
            ])));
    }

    /// <summary>
    /// A DELETED row is held to the same requirement, because a <c>DELETE</c> carries a where clause too.
    /// </summary>
    /// <param name="status">The row status the delete-buffer row carries.</param>
    /// <remarks>
    /// EVERY ROW OF THE <c>Delete!</c> BUFFER GENERATES A STATEMENT, whatever its status - membership in
    /// that buffer IS the pending delete, and Persistence's delete walk is deliberately not filtered by
    /// "modified" because an ordinary retrieved-then-deleted row sits there as <c>NotModified!</c>
    /// [<c>Tasks/SqlUpdateCarrier.ApplyUpdate</c>, <c>ItemStatusMachine.IsDeleteCountable</c>]. So the
    /// theory drives the statuses that would otherwise be exempt in a modifiable buffer.
    /// </remarks>
    [Theory]
    [InlineData(ItemStatus.NotModified)]
    [InlineData(ItemStatus.DataModified)]
    [InlineData(ItemStatus.New)]
    [InlineData(ItemStatus.NewModified)]
    public void ADeletedRowIsHeldToTheSameBaselineRequirement(ItemStatus status)
    {
        UpdateRowValidationFailure failure = Assert.Single(Validate(Row(
            1L,
            status,
            [Column("age", AgeColumn, new AnyValue { DoubleValue = 33d })],
            originals: null,
            DwBuffer.Delete)));

        Assert.Equal(UpdateRowValidationKind.MissingOriginalValue, failure.Kind);
        Assert.Equal(DwBuffer.Delete, failure.Buffer);

        // WITH THE BASELINE STATED IT IS ACCEPTED, so the requirement is satisfiable in the delete buffer
        // exactly as it is elsewhere.
        Assert.Empty(Validate(Row(
            1L,
            status,
            [Column("age", AgeColumn, new AnyValue { DoubleValue = 33d })],
            [Column("age", AgeColumn, new AnyValue { DoubleValue = 33d })],
            DwBuffer.Delete)));
    }

    /// <summary>
    /// A row that generates no where clause is NOT asked for a baseline, in either of the two shapes.
    /// </summary>
    /// <param name="status">The row status.</param>
    /// <param name="buffer">The modifiable buffer the row is sent in.</param>
    /// <remarks>
    /// <para>
    /// <b>THE OTHER HALF OF THE SCOPE, AND IT IS WHAT KEEPS THE CHECK FROM REFUSING CONFORMING
    /// PAYLOADS.</b> A <c>New!</c>/<c>NewModified!</c> row generates an <c>INSERT</c>, which has no where
    /// clause and no prior state to describe; a <c>NotModified!</c> row in a modifiable buffer generates no
    /// statement at all [<c>Tasks/SqlUpdateCarrier.ApplyUpdate</c>, the closing comment of the row walk].
    /// Asking either for an original would refuse payloads that are correct, which is a different defect
    /// rather than a safer one.
    /// </para>
    /// <para>
    /// BOTH MODIFIABLE BUFFERS ARE DRIVEN, because the update walk visits <c>Primary!</c> and
    /// <c>Filter!</c> alike and a rule written against one buffer would silently exempt the other.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(ItemStatus.New, DwBuffer.Primary)]
    [InlineData(ItemStatus.NewModified, DwBuffer.Primary)]
    [InlineData(ItemStatus.NotModified, DwBuffer.Primary)]
    [InlineData(ItemStatus.New, DwBuffer.Filter)]
    [InlineData(ItemStatus.NewModified, DwBuffer.Filter)]
    [InlineData(ItemStatus.NotModified, DwBuffer.Filter)]
    public void ARowThatGeneratesNoWhereClauseIsNotAskedForABaseline(ItemStatus status, DwBuffer buffer)
    {
        Assert.Empty(Validate(Row(
            1L,
            status,
            [
                Column("age", AgeColumn, new AnyValue { DoubleValue = 33d }),
                Column("name", NameColumn, new AnyValue { StringValue = "Paul" }),
            ],
            originals: null,
            buffer)));

        // AND ITS VALUES ARE STILL CHECKED, so the exemption is the baseline requirement alone.
        Assert.Equal(
            UpdateRowValidationKind.InvalidValue,
            Assert.Single(Validate(Row(
                1L,
                status,
                [Column("age", AgeColumn, new AnyValue { StringValue = "not-a-number" })],
                originals: null,
                buffer))).Kind);
    }

    /// <summary>
    /// A modified column can fail BOTH questions, and both are reported.
    /// </summary>
    /// <remarks>
    /// THE TWO FAULTS ARE INDEPENDENT AND A CALLER CORRECTING A PAYLOAD NEEDS BOTH. A column may be missing
    /// its baseline AND carry a value its declared type cannot hold; reporting only the first would send
    /// the caller back for a second round trip to discover the second. The ORDER is asserted too, because
    /// it is the order the payload was walked in.
    /// </remarks>
    [Fact]
    public void AColumnThatFailsBothQuestionsReportsBoth()
    {
        ImmutableArray<UpdateRowValidationFailure> failures = Validate(Row(
            1L,
            ItemStatus.DataModified,
            [Column("age", AgeColumn, new AnyValue { StringValue = "not-a-number" })],
            originals: null));

        Assert.Equal(2, failures.Length);

        Assert.Equal(UpdateRowValidationKind.MissingOriginalValue, failures[0].Kind);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, failures[0].ReturnCode);

        Assert.Equal(UpdateRowValidationKind.InvalidValue, failures[1].Kind);
        Assert.Equal(RetCode.E_INVALID_DATA, failures[1].ReturnCode);

        // AND AN UNRESOLVABLE COLUMN REPORTS ONLY THE ADDRESSING FAULT, because a column that does not
        // exist has neither a baseline nor a type to judge a value against.
        UpdateRowValidationFailure unresolvable = Assert.Single(Validate(Row(
            1L,
            ItemStatus.DataModified,
            [Column("no_such_column", AgeColumn, new AnyValue { StringValue = "x" })],
            originals: null)));

        Assert.Equal(UpdateRowValidationKind.NoSuchColumn, unresolvable.Kind);
    }

    /// <summary>
    /// An original that names a column the row does not carry does NOT satisfy the requirement for a
    /// column it does.
    /// </summary>
    /// <remarks>
    /// THE MATCH IS PER COLUMN AND BY ORDINAL, which is the only reading that makes the baseline usable:
    /// the predicate reads the original of a SPECIFIC column, so an original for a different one is not a
    /// baseline for this one. Counting originals instead - "as many as there are columns" - would accept a
    /// payload that stated the same column twice, or six originals for the wrong six columns.
    /// </remarks>
    [Fact]
    public void AnOriginalForADifferentColumnDoesNotSatisfyTheRequirement()
    {
        UpdateRowValidationFailure failure = Assert.Single(Validate(Row(
            1L,
            ItemStatus.DataModified,
            [Column("age", AgeColumn, new AnyValue { DoubleValue = 33d })],
            [Column("name", NameColumn, new AnyValue { StringValue = "Paul" })])));

        Assert.Equal(UpdateRowValidationKind.MissingOriginalValue, failure.Kind);
        Assert.Equal("age", failure.ColumnName);

        // AND AN ORIGINAL IS MATCHED BY ORDINAL RATHER THAN BY NAME, because the receiving codec is
        // positional [common.v1.ColumnValue.column_name] - a positional row carries no name at all, so a
        // name-keyed match would refuse the legacy's own serialization shape.
        Assert.Empty(Validate(Row(
            1L,
            ItemStatus.DataModified,
            [Column("age", AgeColumn, new AnyValue { DoubleValue = 33d })],
            [Column(string.Empty, AgeColumn, new AnyValue { DoubleValue = 32d })])));
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

    /// <summary>Validates rows against a SPECIFIC host, for the arms the evidenced fixture cannot reach.</summary>
    /// <param name="host">The host to validate against.</param>
    /// <param name="rows">The rows.</param>
    /// <returns>The failures.</returns>
    /// <remarks>
    /// USED ONLY WHERE THE ORACLE HAS NO SUCH COLUMN. The one updatable DataWindow in the estate declares
    /// no integral column at all [<c>dw_sqlite.srd:L8-L13</c>], so the <c>Long()</c> arms of the coercion
    /// table are unreachable through it - and adding one to the transcribed catalogue to reach them would
    /// misrepresent the oracle (C-C). Every other case in this file goes through <see cref="Host"/>.
    /// </remarks>
    private static ImmutableArray<UpdateRowValidationFailure> Validate(
        DataWindowServiceHost host,
        params DataWindowRow[] rows) =>
        new UpdateRowValidator(new I18n()).Validate(host, rows);

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

    /// <summary>Builds a row that declares an item status and, optionally, its original values.</summary>
    /// <param name="row">The one-based row ordinal.</param>
    /// <param name="status">The row's item status, which decides whether a baseline is required of it.</param>
    /// <param name="columns">Its current values, in request order.</param>
    /// <param name="originals">Its original values, or <see langword="null"/> for none.</param>
    /// <param name="buffer">The buffer it is sent in.</param>
    /// <returns>The row.</returns>
    /// <remarks>
    /// SEPARATE FROM THE TWO OVERLOADS ABOVE RATHER THAN REPLACING THEM. Those leave the status at the
    /// contract's own default - <c>ITEM_STATUS_NOT_MODIFIED</c> is field number zero - which is what the
    /// value cases want: a row that generates no statement is exempt from the baseline requirement, so its
    /// failures are about values alone and nothing else is in the way.
    /// </remarks>
    private static DataWindowRow Row(
        long row,
        ItemStatus status,
        ColumnValue[] columns,
        ColumnValue[]? originals = null,
        DwBuffer buffer = DwBuffer.Primary)
    {
        DataWindowRow built = new() { Buffer = buffer, Row = row, ItemStatus = status };
        built.Columns.AddRange(columns);
        built.OriginalValues.AddRange(originals ?? []);

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
