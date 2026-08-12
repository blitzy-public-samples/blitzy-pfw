// ==================================================================================================
//  UpdateRowValidator.cs - THE VALIDATOR THIRD OF THE TRIPLE, ON THE WRITE PATH
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  C-03's Update runs this over every row it was handed BEFORE it tells Persistence anything. It
//  answers two questions the boundary newly permits a caller to get wrong, and it answers nothing
//  else:
//
//    1. Does every named column exist on this DataWindow, at the ordinal the request paired with it?
//    2. Can the value the request carried be represented in the type the definition declares?
//
//  WHY THIS FILE EXISTS AT ALL
//  The AAP ports five `dwnvl*` validators as "the Validator third of the triple, one per column type,
//  aligned to the coercion switch at se_cst_dw.sru:L228-L243" (AAP 0.4.2.5). They were ported
//  faithfully - each with its own reporting counterpart, added expressly "for the callers that must
//  answer a client across one of the newly created service boundaries" - and NOTHING ON THE WRITE PATH
//  CALLED THEM. The measured consequence: `age = "not-a-number"` was stored as TEXT in an
//  `AGE INT NOT NULL` column and read back as `0`; `salary = "abc"` was stored as TEXT in a `REAL`
//  column; `birth = "31/02/1984"`, a date that does not exist, was stored as text; and a column name no
//  definition carries was accepted and its value applied to whichever ordinal travelled beside it.
//  Every one answered HTTP 200. A write-then-read that returns a different value than it was given is
//  the worst class of data defect, because nothing in the response says anything went wrong.
//
//  ================ WHAT THIS FILE DELIBERATELY DOES *NOT* VALIDATE (constraint C-B) ===============
//
//  Three things a validator "should" check are left alone on purpose, and each is a preserved legacy
//  behaviour rather than an omission:
//
//    * COLUMN WIDTH. `address` is declared char(200) while the only DDL in the repository declares
//      CHAR(50) [ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469]. That disagreement is a
//      PRESERVED DEFECT recorded in AAP 0.6.4 and transcribed in Domain/DataWindowCatalogue.cs.
//      Enforcing either number would be a behavioural change; enforcing the narrower one would refuse
//      payloads the legacy accepts.
//    * NULLABILITY. `NAME TEXT NOT NULL` and `AGE INT NOT NULL` live in the DDL, not in the DataWindow
//      definition - the `.srd` declares no required flag on any column [dw_sqlite.srd:L8-L13] - so this
//      layer has no authority to say a column is required. A NOT NULL violation is classified where the
//      schema is known, which is Persistence, and this file must not guess at it from a column name.
//    * THE DECLARED-VERSUS-STORED TYPE MISMATCHES. `salary` is decimal(2) against a REAL column and
//      `birth` is date against a TEXT column. Both are preserved defects for the same reason as the
//      width, and both are why the check below is "can the DECLARED type represent this value" rather
//      than "does this value match the storage type".
//
//  ================ WHAT A LEGACY TYPE REFUSAL LOOKS LIKE, AND WHY IT IS NOT INVENTED ==============
//
//  The oracle already has this message. When a column rejects a value, se_cst_dw.sru reads the column's
//  own validation message and, when there is none, falls back to
//      sErrMsg = I18N(ne_cst_i18n.CAT_DWSVC,"输入了无效的值") + "!"          [:L355]
//      MessageBox(I18N(ne_cst_i18n.CAT_DWSVC,"错误"), sErrMsg, StopSign!)   [:L357]
//  - "an invalid value was entered", titled "error", stop-sign severity, BOTH strings localized through
//  CAT_DWSVC. That is precisely the refusal this file reproduces, through the same constants
//  Domain/ValidationSession.cs already holds, so the two paths cannot drift apart. Nothing about the
//  message is new; only the delivery channel is.
//
//  The UNKNOWN-COLUMN refusal has no oracle, and is marked as such: in process a column name is
//  resolved when the DataWindow is compiled, so a name that does not exist is not a runtime condition
//  the legacy can reach. It is therefore a DEFINED ERROR (AAP 0.1.5 - narrow with a defined error,
//  never widen with a guess), it reports `localized = false`, and it claims no localization category,
//  because claiming one would tell a characterization comparison that a translation table answered when
//  none did.
//
//  ================ THE COLUMN IS RESOLVED THE ORACLE'S OWN WAY ==================================
//
//  `Describe(name + ".ID")` and `Describe(name + ".ColType")`, which is exactly how the expansion
//  engine resolves a column - `Long(#DataWindow.Describe(colname + ".ID"))`
//  [n_cst_dwsvc_columnexp.sru:L1541-L1542] - and the oracle reads a non-positive answer there as "no
//  such column". Going through Describe rather than through the concrete definition keeps this file
//  written against the abstract host (AAP 0.2.1.3 Correction 3) and means a deployment that registers a
//  further definition is validated by the same code with no change here.
//
//  RULES POSITION
//  No user rules were provided for this project. The binding constraints applied here are C-B (no
//  behaviour improvement beyond what the transition requires - hence the three deliberate non-checks
//  above), C-K (every boundary decision documented at the point it is made) and AAP 0.1.5.
// ==================================================================================================

using System.Collections.Immutable;
using System.Globalization;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.DataServices.Domain;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

// The kernel's return-code catalogue, not the generated wire wrapper of the same simple name. Both
// namespaces above declare a `RetCode`, and this file speaks the in-process algebra - the same
// direction the sibling validators bind it.
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.DataServices.Validators;

/// <summary>
/// Which of the two questions a row failed.
/// </summary>
internal enum UpdateRowValidationKind
{
    /// <summary>The name resolves to no column of this DataWindow at all.</summary>
    NoSuchColumn,

    /// <summary>
    /// The name resolves, but to a different ordinal than the one sent beside it.
    /// </summary>
    /// <remarks>
    /// A DISTINCT MEMBER RATHER THAN A VARIANT OF THE ABOVE, because the two say different things to an
    /// operator reading the log: the first is a name that does not exist, the second is two real
    /// identifiers that disagree. Both MISDIRECT a value rather than losing it - the receiving codec is
    /// positional, so the value lands in whichever column the ORDINAL addresses while the caller
    /// believes it wrote to the column the NAME addresses - which is why both are refused and why both
    /// carry the same caller-facing text and the same code.
    /// </remarks>
    OrdinalDisagreesWithName,

    /// <summary>
    /// The value cannot be represented in the type the definition declares for that column.
    /// </summary>
    InvalidValue,
}

/// <summary>
/// One rejected column of one row, addressed the way the caller addressed it.
/// </summary>
/// <param name="Buffer">The buffer the row was sent in, echoed from the request.</param>
/// <param name="Row">The one-based row ordinal, echoed from the request.</param>
/// <param name="ColumnName">The column name the request carried; may be empty.</param>
/// <param name="ColumnId">The one-based column ordinal the request carried.</param>
/// <param name="ColumnType">
/// The type the definition declares, spelled as the definition spells it. Empty when the named column
/// does not exist - there is no type to report for a column this DataWindow does not have.
/// </param>
/// <param name="Kind">Which question failed.</param>
/// <param name="ReturnCode">
/// The framework code this single failure carries. <c>E_INVALID_ARGUMENT</c> for an unknown column,
/// because the REQUEST is malformed; <c>E_INVALID_DATA</c> for a value the declared type cannot hold,
/// which is the code the ported reporting coercions themselves answer.
/// </param>
/// <param name="Error">The refusal, as the legacy would have shown it.</param>
internal sealed record UpdateRowValidationFailure(
    DwBuffer Buffer,
    long Row,
    string ColumnName,
    long ColumnId,
    string ColumnType,
    UpdateRowValidationKind Kind,
    long ReturnCode,
    ValidationStructuredError Error);

/// <summary>
/// Validates the rows of an update before any statement is generated.
/// </summary>
/// <remarks>
/// <para>
/// STATELESS AND SAFE TO SHARE. It holds only the localization facade, which is itself a singleton, and
/// it mutates nothing on the host it is handed - every question it asks is a <c>Describe</c>.
/// </para>
/// <para>
/// IT NEVER THROWS FOR ANY PAYLOAD. A validator that faulted on malformed input would replace a legible
/// 400 with a 500, which is the shape this whole finding is about.
/// </para>
/// </remarks>
internal sealed class UpdateRowValidator
{
    /// <summary>
    /// The property expression suffix that answers a column's ordinal, exactly as the oracle spells it
    /// [<c>n_cst_dwsvc_columnexp.sru:L1541-L1542</c>].
    /// </summary>
    private const string IdPropertySuffix = ".ID";

    /// <summary>The property expression suffix that answers a column's declared type.</summary>
    private const string ColTypePropertySuffix = ".ColType";

    /// <summary>
    /// PowerBuilder's answer for a property expression it cannot resolve. Tested rather than assumed,
    /// because an unresolvable name answers this and NOT the empty string - the two are different
    /// answers and the host preserves the difference.
    /// </summary>
    private const string DescribeFailureSentinel = "!";

    /// <summary>
    /// The untranslated text of the unknown-column refusal. A DEFINED ERROR with no oracle, so the text
    /// is FIXED and carries only values the caller already sent - a column name and an ordinal - never
    /// an exception's own detail (CWE-209).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="Formatting.Sprintf"/> template, and the arguments travel beside the rendered text so
    /// a consumer can re-render it.
    /// </para>
    /// <para>
    /// <b>THE INDICES ARE ONE-BASED, BECAUSE THE PORTED SPRINTF IS.</b> The legacy corpus numbers its
    /// placeholders from <c>{1}</c> and <c>{0}</c> appears nowhere in it, so the port treats an explicit
    /// <c>{0}</c> as out of range and renders it as the EMPTY STRING rather than throwing
    /// [<c>Formatting.Sprintf</c> - CHOICE 2]. A zero-based template therefore does not fail loudly: it
    /// silently drops its first argument and shifts every other one down, which is precisely what this
    /// sentence did before - it rendered the column NAME where the ordinal belongs and nothing at all
    /// where the name belongs. Measured at runtime, not inferred.
    /// </para>
    /// </remarks>
    internal const string UnknownColumnTemplate =
        "The request named column '{1}' at ordinal {2}, which this DataWindow does not carry at that "
        + "ordinal. A column is addressed by its one-based ordinal, and the name must be the name that "
        + "ordinal carries; no value was applied.";

    /// <summary>The title the unknown-column refusal carries.</summary>
    /// <remarks>
    /// NOT the oracle's localized 错误, deliberately: this refusal is not one of the oracle's dialogs,
    /// and borrowing its translated title would claim a provenance it does not have.
    /// </remarks>
    internal const string UnknownColumnTitle = "Invalid column reference";

    /// <summary>The localization facade, whose silent passthrough is preserved.</summary>
    private readonly I18n _localization;

    /// <summary>Initializes the validator.</summary>
    /// <param name="localization">
    /// The localization facade. Its SILENT PASSTHROUGH is the legacy behaviour and is relied on: with no
    /// provider installed the untranslated source comes back unchanged, nothing is thrown and nothing is
    /// logged [<c>i18n.srf:L17-L18</c>].
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="localization"/> is null.</exception>
    internal UpdateRowValidator(I18n localization)
    {
        ArgumentNullException.ThrowIfNull(localization);

        _localization = localization;
    }

    /// <summary>
    /// Validates every column of every row.
    /// </summary>
    /// <param name="host">The DataWindow the update names, for its definition.</param>
    /// <param name="rows">The rows the request carried, in request order.</param>
    /// <returns>
    /// The failures, in request order - rows in the order sent and each row's columns in the order sent.
    /// Empty when nothing was rejected.
    /// </returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <remarks>
    /// <para>
    /// EVERY FAULT IS COLLECTED RATHER THAN THE FIRST. A caller correcting a payload needs all of them;
    /// stopping at the first turns one correction into as many round trips as there are faults.
    /// </para>
    /// <para>
    /// ORIGINAL VALUES ARE NOT VALIDATED, and that is deliberate. An original is a value the caller
    /// READ BACK from a retrieval - it describes what storage held, not what the caller is writing - so
    /// refusing it would refuse a payload assembled correctly from this service's own answer. It is
    /// compared, never applied.
    /// </para>
    /// </remarks>
    internal ImmutableArray<UpdateRowValidationFailure> Validate(
        DataWindowServiceHost host,
        IReadOnlyList<DataWindowRow> rows)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(rows);

        ImmutableArray<UpdateRowValidationFailure>.Builder failures =
            ImmutableArray.CreateBuilder<UpdateRowValidationFailure>();

        foreach (DataWindowRow row in rows)
        {
            foreach (ColumnValue column in row.Columns)
            {
                if (ResolveColumn(host, column, out string columnType) is { } unresolvable)
                {
                    failures.Add(BuildUnknownColumn(row, column, unresolvable));
                    continue;
                }

                if (!IsRepresentable(column.Value, columnType))
                {
                    failures.Add(BuildInvalidValue(row, column, columnType));
                }
            }
        }

        return failures.ToImmutable();
    }

    /// <summary>
    /// Resolves a column reference against the host's own definition.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="column">The reference the request carried.</param>
    /// <param name="columnType">The declared type on success; the empty string otherwise.</param>
    /// <returns>
    /// <see langword="null"/> when the reference resolves, or the kind of unresolvable reference it is.
    /// </returns>
    /// <remarks>
    /// <para>
    /// AN EMPTY NAME IS NOT A FAULT, because the contract says so: a row projected from a carrier
    /// changeset is POSITIONAL and carries ordinals only, so <c>column_id</c> is the authoritative
    /// identifier there [<c>common.v1.ColumnValue.column_name</c>]. Such a row is validated by ordinal
    /// alone, and refusing it would refuse the legacy's own serialization shape.
    /// </para>
    /// <para>
    /// AN OUT-OF-RANGE ORDINAL IS LEFT TO THE RECEIVING CODEC, which already refuses one on its own
    /// authority [<c>Buffers/ChangesetCodec.TryReadRow</c>]. Duplicating that test here would create a
    /// second place for the same rule and a second answer to reconcile.
    /// </para>
    /// </remarks>
    private static UpdateRowValidationKind? ResolveColumn(
        DataWindowServiceHost host,
        ColumnValue column,
        out string columnType)
    {
        columnType = string.Empty;

        if (column.ColumnName.Length == 0)
        {
            // Positional row: the ordinal is authoritative and there is no name to check. The declared
            // type is still read where the ordinal names a real column, so the value check below still
            // applies.
            columnType = DescribeColumnTypeByOrdinal(host, column.ColumnId);

            return null;
        }

        string described = host.Describe(column.ColumnName + IdPropertySuffix);

        if (!long.TryParse(
                described,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long resolved)
            || resolved <= 0L)
        {
            // No such object, or an object that is not a column - a text object answers "0" and the
            // oracle reads a non-positive answer as "no such column" [n_cst_dwsvc_columnexp.sru:L1541].
            return UpdateRowValidationKind.NoSuchColumn;
        }

        if (column.ColumnId != 0L && column.ColumnId != resolved)
        {
            // The name is real and the ordinal is real, and they name DIFFERENT columns. The receiving
            // codec is positional, so leaving this alone writes the value to the column the ORDINAL
            // names while the caller believes it wrote to the column the NAME names.
            return UpdateRowValidationKind.OrdinalDisagreesWithName;
        }

        string type = host.Describe(column.ColumnName + ColTypePropertySuffix);

        columnType = string.Equals(type, DescribeFailureSentinel, StringComparison.Ordinal)
            ? string.Empty
            : type;

        return null;
    }

    /// <summary>
    /// Reads the declared type of the column at an ordinal, for a positional row.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="columnId">The one-based ordinal.</param>
    /// <returns>The declared type, or the empty string when the ordinal names no column.</returns>
    /// <remarks>
    /// The ordinal is mapped to a name through <c>DataWindow.Objects</c>, which answers the definition's
    /// objects in DECLARATION ORDER - the same order column numbers are assigned in - so a name that
    /// answers the requested ordinal for its own <c>.ID</c> is that ordinal's column. Reading it back
    /// through <c>.ID</c> rather than counting is what keeps text objects, which consume no column
    /// number, from shifting the mapping.
    /// </remarks>
    private static string DescribeColumnTypeByOrdinal(DataWindowServiceHost host, long columnId)
    {
        if (columnId <= 0L)
        {
            return string.Empty;
        }

        string objects = host.Describe("DataWindow.Objects");

        if (string.Equals(objects, DescribeFailureSentinel, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        foreach (string name in objects.Split('\t', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!long.TryParse(
                    host.Describe(name + IdPropertySuffix),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long ordinal)
                || ordinal != columnId)
            {
                continue;
            }

            string type = host.Describe(name + ColTypePropertySuffix);

            return string.Equals(type, DescribeFailureSentinel, StringComparison.Ordinal)
                ? string.Empty
                : type;
        }

        return string.Empty;
    }

    /// <summary>
    /// Whether a value can be represented in a declared column type.
    /// </summary>
    /// <param name="value">The value the request carried; may be null.</param>
    /// <param name="columnType">The declared type, or the empty string when it is not known.</param>
    /// <returns><see langword="true"/> when the value is acceptable for that column.</returns>
    /// <remarks>
    /// <para>
    /// <b>AN UNKNOWN COLUMN TYPE IS ACCEPTED, NOT REFUSED.</b> A definition this service does not
    /// recognise the type of is not a caller fault, and refusing on it would make a registered
    /// definition unusable for a reason the caller cannot address. The same reasoning covers a type
    /// outside the oracle's own dispatch set: the switch at <c>se_cst_dw.sru:L232-L243</c> has no
    /// <c>case else</c>, so a type it does not name is left alone there too.
    /// </para>
    /// <para>
    /// <b>NULL IS ALWAYS ACCEPTED HERE.</b> Whether a column may be null is a SCHEMA fact, and the
    /// DataWindow definition carries no required flag on any column - see the file header. Refusing null
    /// here would be this layer inventing a constraint it cannot know.
    /// </para>
    /// <para>
    /// <b>TEXT IS THE ARM THE FINDING IS ABOUT</b>, and it is checked through the ported validators'
    /// own reporting coercions - which exist for exactly this caller and are documented as forbidden on
    /// the item-change path, where reporting an error the legacy cannot produce would breach C-B.
    /// </para>
    /// </remarks>
    private static bool IsRepresentable(AnyValue? value, string columnType)
    {
        if (value is null || value.KindCase == AnyValue.KindOneofCase.None || value.IsNull)
        {
            return true;
        }

        if (columnType.Length == 0)
        {
            return true;
        }

        if (StringValidator.OwnsColType(columnType))
        {
            // A char column takes any scalar: PowerBuilder's own coercion to string is total, and the
            // declared WIDTH is deliberately not enforced (see the file header - preserved defect).
            return true;
        }

        bool numeric = NumberValidator.IsDecimalCoercionColumnType(columnType)
            || NumberValidator.IsLongCoercionColumnType(columnType);

        // The temporal tests are ordered datetime-before-date because the legacy's five-character
        // truncation makes "datet" and "date" two different arms and "datetime" would otherwise match
        // the shorter token first [se_cst_dw.sru:L238-L241].
        bool dateTime = DateTimeValidator.MatchesColumnType(columnType);
        bool date = !dateTime && DateValidator.MatchesColumnType(columnType);
        bool time = TimeValidator.MatchesColumnType(columnType);

        if (!numeric && !dateTime && !date && !time)
        {
            return true;
        }

        return value.KindCase switch
        {
            AnyValue.KindOneofCase.StringValue => IsRepresentableText(
                value.StringValue,
                numeric,
                dateTime,
                date,
                time,
                columnType),

            // A numeric arm fits a numeric column and nothing else. A number sent for a `date` column is
            // a categorical mismatch: the legacy's date coercion takes edit TEXT, and there is no
            // reading under which an int64 is a date.
            AnyValue.KindOneofCase.Int64Value
                or AnyValue.KindOneofCase.Uint64Value
                or AnyValue.KindOneofCase.DoubleValue
                or AnyValue.KindOneofCase.DecimalValue => numeric,

            // Each temporal arm fits its own column type. datetime into a `date` column and back are
            // refused rather than silently truncated or widened, because either would store a value the
            // caller did not send.
            AnyValue.KindOneofCase.DateValue => date,
            AnyValue.KindOneofCase.TimeValue => time,
            AnyValue.KindOneofCase.DatetimeValue => dateTime,

            // A boolean or a blob in a numeric or temporal column. The legacy has no coercion from
            // either, so there is no behaviour to preserve and nothing to guess at.
            AnyValue.KindOneofCase.BoolValue or AnyValue.KindOneofCase.BlobValue => false,

            // IsNull and None are handled above; the switch is exhaustive over the remaining arms and
            // this arm exists so a NEW arm added to the contract is accepted rather than silently
            // refused - widening a published message must not retroactively invalidate payloads.
            _ => true,
        };
    }

    /// <summary>
    /// Whether text coerces to the declared type, through the ported reporting coercions.
    /// </summary>
    /// <param name="text">The text the request carried.</param>
    /// <param name="numeric">Whether the column is one of the numeric coercion types.</param>
    /// <param name="dateTime">Whether the column is <c>datetime</c>.</param>
    /// <param name="date">Whether the column is <c>date</c>.</param>
    /// <param name="time">Whether the column is <c>time</c>.</param>
    /// <param name="columnType">The declared type, for the decimal-versus-long distinction.</param>
    /// <returns><see langword="true"/> when the text coerces.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE FOUR ARMS DISAGREE ABOUT BLANK TEXT, AND THE DISAGREEMENT IS PRESERVED (C-B).</b> Measured
    /// against the ported validators rather than assumed: <see cref="DateValidator.TryCoerce"/>
    /// short-circuits on <see cref="string.IsNullOrWhiteSpace"/> and reports ABSENCE, so an empty or
    /// whitespace-only edit is accepted for a <c>date</c> column; <see cref="NumberValidator"/>'s two
    /// reporting coercions have no such branch and report INVALID DATA; <c>DateTimeValidator</c> and
    /// <c>TimeValidator</c> short-circuit on null only. That is the legacy's own four-way inconsistency,
    /// which the validators' suite pins directly, and harmonising it here would change behaviour on a
    /// path this validator was not added to touch.
    /// </para>
    /// <para>
    /// AND THE NUMERIC ANSWER IS THE CORRECT ONE INDEPENDENTLY. <c>common.v1.AnyValue</c> states that
    /// <c>is_null = true</c> and <c>string_value = ""</c> ARE DIFFERENT VALUES and that a producer must
    /// never substitute one for the other, because collapsing them widens the optimistic-concurrency
    /// where-clause and overwrites a row that should have conflicted. Empty text is therefore text that
    /// is not a number; clearing a numeric cell is spelled with the null arm, which
    /// <see cref="IsRepresentable"/> always accepts.
    /// </para>
    /// </remarks>
    private static bool IsRepresentableText(
        string text,
        bool numeric,
        bool dateTime,
        bool date,
        bool time,
        string columnType)
    {
        if (numeric)
        {
            return NumberValidator.IsLongCoercionColumnType(columnType)
                ? Predicates.IsSucceeded(NumberValidator.TryCoerceToLong(text, out _))
                : Predicates.IsSucceeded(NumberValidator.TryCoerceToDecimal(text, out _));
        }

        if (dateTime)
        {
            return Predicates.IsSucceeded(DateTimeValidator.TryCoerce(text, out _));
        }

        if (date)
        {
            return Predicates.IsSucceeded(DateValidator.TryCoerce(text, out _));
        }

        return !time || Predicates.IsSucceeded(TimeValidator.TryCoerce(text, out _));
    }

    /// <summary>Builds the unknown-column refusal.</summary>
    /// <param name="row">The row the reference was in.</param>
    /// <param name="column">The reference.</param>
    /// <param name="kind">Which shape of unresolvable reference it was.</param>
    /// <returns>The failure.</returns>
    /// <remarks>
    /// THE TWO SHAPES SHARE ONE CALLER-FACING MESSAGE ON PURPOSE, because the corrective action is
    /// identical - send the name that ordinal carries - and two near-identical strings would be two
    /// things to translate for one fault. The distinction survives on
    /// <see cref="UpdateRowValidationFailure.Kind"/>, which is what the service's own diagnostic reads.
    /// </remarks>
    private UpdateRowValidationFailure BuildUnknownColumn(
        DataWindowRow row,
        ColumnValue column,
        UpdateRowValidationKind kind)
    {
        ImmutableArray<string> arguments =
        [
            column.ColumnName,
            column.ColumnId.ToString(CultureInfo.InvariantCulture),
        ];

        return new UpdateRowValidationFailure(
            row.Buffer,
            row.Row,
            column.ColumnName,
            column.ColumnId,
            ColumnType: string.Empty,
            kind,
            RetCode.E_INVALID_ARGUMENT,
            ValidationStructuredError.Create(
                UnknownColumnTitle,
                UnknownColumnTemplate,
                DialogSeverity.StopSign,

                // NO CATEGORY IS CLAIMED. This refusal has no oracle and therefore no translation
                // table answered for it; reporting CAT_DWSVC would tell a characterization comparison
                // that one did. Zero is "none", matching StructuredError.category's own contract.
                localizationCategory: 0L,
                localized: false,
                arguments,
                RetCode.E_INVALID_ARGUMENT));
    }

    /// <summary>Builds the invalid-value refusal, which is the oracle's own dialog.</summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The reference.</param>
    /// <param name="columnType">The declared type.</param>
    /// <returns>The failure.</returns>
    /// <remarks>
    /// <para>
    /// TRANSLATE THEN APPEND, WHICH IS THE ORDER THE ORACLE USES. <c>:L355</c> reads
    /// <c>I18N(CAT_DWSVC,"输入了无效的值") + "!"</c> - the exclamation mark is concatenated onto the
    /// TRANSLATED text, never onto the key and never before the lookup. Reversing the two would look up
    /// a key that no table carries and fall through the localization layer's silent passthrough.
    /// </para>
    /// <para>
    /// THE TITLE IS LOOKED UP SEPARATELY, because <c>:L357</c> localizes it separately on the same line.
    /// Collapsing title and body into one lookup would lose a translated string.
    /// </para>
    /// <para>
    /// NO SUBSTITUTION ARGUMENTS, because the oracle substitutes none here - and the empty-argument
    /// short circuit in <see cref="ValidationStructuredError.Create"/> is what keeps a brace in a
    /// translation from being reinterpreted by Sprintf. The column identity travels in this failure's
    /// own address fields instead, which is where a consumer can read it without parsing prose.
    /// </para>
    /// </remarks>
    private UpdateRowValidationFailure BuildInvalidValue(
        DataWindowRow row,
        ColumnValue column,
        string columnType)
    {
        string text = (_localization.I18N(
                           Categories.CAT_DWSVC,
                           ValidationStructuredError.LegacyFallbackTextSource)
                       ?? ValidationStructuredError.LegacyFallbackTextSource)
            + ValidationStructuredError.LegacyFallbackSuffix;

        string title = _localization.I18N(
                           Categories.CAT_DWSVC,
                           ValidationStructuredError.LegacyTitleSource)
                       ?? ValidationStructuredError.LegacyTitleSource;

        return new UpdateRowValidationFailure(
            row.Buffer,
            row.Row,
            column.ColumnName,
            column.ColumnId,
            columnType,
            UpdateRowValidationKind.InvalidValue,
            RetCode.E_INVALID_DATA,
            ValidationStructuredError.Create(
                title,
                text,
                DialogSeverity.StopSign,
                Categories.CAT_DWSVC,
                localized: true,
                ImmutableArray<string>.Empty,
                RetCode.E_INVALID_DATA));
    }
}
