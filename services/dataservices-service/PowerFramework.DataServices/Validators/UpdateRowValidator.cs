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

    /// <summary>
    /// The row's originals become a generated where clause and the column states none, so the update
    /// carries no concurrency baseline for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>A DISTINCT MEMBER BECAUSE IT IS A DISTINCT FAULT, AND THE MOST CONSEQUENTIAL ONE THIS
    /// VALIDATOR CAN REPORT.</b> <c>updatewhere=1</c> builds the generated statement's where clause from
    /// the ORIGINAL value of every marked column (AAP 0.6.3.2), so a column with no stated original
    /// leaves the receiving codec with nothing to compare against. Substituting the caller's own current
    /// value - which is what happened before this check existed - produces a predicate built from the
    /// values being written: an ordinary update then compares a row against itself and can never detect a
    /// lost race, and an update whose KEY changed addresses the row named by the NEW key instead of the
    /// row the caller read. Both are silent: the statement succeeds and reports success.
    /// </para>
    /// <para>
    /// <b>REPORTED ONLY FOR THE ROWS WHOSE BASELINE IS ACTUALLY READ, which is the predicate rather than
    /// the row shape.</b> Persistence generates a <c>DELETE ... WHERE</c> for every row of the
    /// <c>Delete!</c> buffer whatever its status, and an <c>UPDATE ... WHERE</c> - or a DELETE plus an
    /// INSERT when the key moved under <c>updatekeyinplace=no</c> - for every <c>DataModified!</c> row in a
    /// modifiable buffer. A <c>New!</c>/<c>NewModified!</c> row generates an <c>INSERT</c>, which has no
    /// where clause and no prior state to describe, and a <c>NotModified!</c> row in a modifiable buffer
    /// generates no statement at all. Asking either for a baseline would refuse conforming payloads while
    /// preventing nothing.
    /// </para>
    /// </remarks>
    MissingOriginalValue,
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

    /// <summary>
    /// The untranslated text of the missing-baseline refusal. A <see cref="Formatting.Sprintf"/> template
    /// on the same terms as <see cref="UnknownColumnTemplate"/>: ONE-BASED placeholders, and the arguments
    /// travel beside the rendered text so a consumer can re-render it.
    /// </summary>
    /// <remarks>
    /// It names WHAT to send rather than only what is missing, because the corrective action is not
    /// guessable from the fault: the caller must echo the value it read for that column into
    /// <c>original_values</c>. It names WHICH rows the requirement covers too, so a caller inserting rows
    /// or echoing unchanged ones does not read it as a demand it cannot satisfy.
    /// </remarks>
    internal const string MissingOriginalTemplate =
        "Column '{1}' at ordinal {2} carries no original value. This row generates a statement whose "
        + "where clause is built from the ORIGINAL value of every column the row carries, so each one must "
        + "appear in original_values - echo the value the retrieval answered for it. The requirement covers "
        + "deleted rows and rows stamped DataModified; an inserted or unchanged row needs no baseline. "
        + "Nothing was applied.";

    /// <summary>The title the missing-baseline refusal carries.</summary>
    /// <remarks>
    /// NOT the oracle's localized 错误, for the same reason the unknown-column title is not: this refusal
    /// is not one of the oracle's dialogs, and borrowing its translated title would claim a provenance it
    /// does not have.
    /// </remarks>
    internal const string MissingOriginalTitle = "Missing concurrency baseline";

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
    /// AN ORIGINAL'S VALUE IS NOT TYPE-CHECKED, and that is deliberate. An original is a value the caller
    /// READ BACK from a retrieval - it describes what storage held, not what the caller is writing - so
    /// refusing it on type grounds would refuse a payload assembled correctly from this service's own
    /// answer. It is compared, never applied.
    /// </para>
    /// <para>
    /// 🔴 <b>ITS PRESENCE, HOWEVER, IS REQUIRED, AND THAT IS THE THIRD QUESTION THIS VALIDATOR ANSWERS.</b>
    /// A row that is not insert-shaped must state the original of every column it carries, because those
    /// originals become the optimistic-concurrency predicate downstream and a caller cannot be a
    /// trustworthy source of a value it is simultaneously overwriting. Persistence refuses the same shape
    /// on its own authority [<c>Buffers/ChangesetCodec.cs</c>
    /// <c>CarrierBaselineTrust.RequiredOnChangedRows</c>]; checking it HERE as well is not duplication for
    /// its own sake - it is what turns a generic invalid-update-data code from the far side of the
    /// boundary into a per-column answer naming the column whose baseline is missing.
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
            // 🔴 THE ROWS WHOSE ORIGINALS BECOME A WHERE CLAUSE, which is what the baseline requirement is
            // scoped to - and the scope is the PREDICATE rather than the row shape. Persistence generates a
            // `DELETE ... WHERE` for every row of the Delete! buffer whatever its status, because
            // membership in that buffer IS the pending delete, and an `UPDATE ... WHERE` - or, when the key
            // moved under updatekeyinplace=no, a DELETE plus an INSERT - for every DataModified! row in a
            // modifiable buffer [Tasks/SqlUpdateCarrier.ApplyUpdate]. A New!/NewModified! row generates an
            // INSERT, which has no where clause and no prior state to describe, and a NotModified! row in a
            // modifiable buffer generates NO STATEMENT AT ALL - so asking either for a baseline would
            // refuse conforming payloads while preventing nothing. Computed once per row because it is a
            // property of the row.
            bool baselineBecomesAPredicate =
                row.Buffer == DwBuffer.Delete || row.ItemStatus == ItemStatus.DataModified;

            HashSet<long> statedOriginals = [];

            if (baselineBecomesAPredicate)
            {
                foreach (ColumnValue original in row.OriginalValues)
                {
                    _ = statedOriginals.Add(original.ColumnId);
                }
            }

            foreach (ColumnValue column in row.Columns)
            {
                if (ResolveColumn(host, column, out string columnType) is { } unresolvable)
                {
                    failures.Add(BuildUnknownColumn(row, column, unresolvable));
                    continue;
                }

                if (baselineBecomesAPredicate && !statedOriginals.Contains(column.ColumnId))
                {
                    // REPORTED AND THE VALUE CHECK IS STILL RUN BELOW, because the two faults are
                    // independent and a caller correcting a payload needs both: a column may be missing
                    // its baseline AND carry a value its column cannot hold.
                    failures.Add(BuildMissingOriginal(row, column, columnType));
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
            // A char column takes any SCALAR: PowerBuilder's own coercion to string is total over the
            // scalar types, and the declared WIDTH is deliberately not enforced (see the file header -
            // preserved defect).
            //
            // 🔴 A BLOB IS NOT A SCALAR AND IS REFUSED. There is no legacy coercion from `blob` to
            // `string` - PowerScript requires an explicit codec call, which is a DIFFERENT operation
            // with a DIFFERENT result - so admitting one here invents behaviour the oracle does not
            // have, and what actually happened downstream was worse than an invention: the bytes were
            // bound to a character column verbatim, so a write-then-read answered a value the caller
            // never sent. The refusal is the same defined error every other unrepresentable value gets.
            //
            // The unknown arm falls THROUGH to the fail-closed switch below rather than being accepted
            // here, so a new wire arm cannot enter a character column unvalidated either.
            return value.KindCase switch
            {
                AnyValue.KindOneofCase.BlobValue => false,
                AnyValue.KindOneofCase.StringValue
                    or AnyValue.KindOneofCase.BoolValue
                    or AnyValue.KindOneofCase.Int64Value
                    or AnyValue.KindOneofCase.Uint64Value
                    or AnyValue.KindOneofCase.DoubleValue
                    or AnyValue.KindOneofCase.DecimalValue
                    or AnyValue.KindOneofCase.DateValue
                    or AnyValue.KindOneofCase.TimeValue
                    or AnyValue.KindOneofCase.DatetimeValue => true,
                _ => false,
            };
        }

        bool integral = NumberValidator.IsLongCoercionColumnType(columnType);
        bool numeric = NumberValidator.IsDecimalCoercionColumnType(columnType) || integral;

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
            //
            // 🔴 AND FITTING THE FAMILY IS NOT ENOUGH - THE SUBTYPE DECIDES. Three refusals live in
            // IsRepresentableNumber and each one was reachable and silent before it existed: a non-finite
            // double, which SQLite stores as NULL so a NaN written to a NOT NULL column either fails at
            // the driver or reads back as an absent value; a fractional value for a `long` column, where
            // the legacy's own coercion is Long(), which TRUNCATES, so the stored value is not the value
            // sent; and a negative value for `ulong`, whose domain begins at zero. Each is a value the
            // DECLARED type cannot hold, which is exactly the question this validator exists to answer.
            AnyValue.KindOneofCase.Int64Value
                or AnyValue.KindOneofCase.Uint64Value
                or AnyValue.KindOneofCase.DoubleValue
                or AnyValue.KindOneofCase.DecimalValue =>
                numeric && IsRepresentableNumber(value, integral, columnType),

            // Each temporal arm fits its own column type. datetime into a `date` column and back are
            // refused rather than silently truncated or widened, because either would store a value the
            // caller did not send.
            AnyValue.KindOneofCase.DateValue => date,
            AnyValue.KindOneofCase.TimeValue => time,
            AnyValue.KindOneofCase.DatetimeValue => dateTime,

            // A boolean or a blob in a numeric or temporal column. The legacy has no coercion from
            // either, so there is no behaviour to preserve and nothing to guess at.
            AnyValue.KindOneofCase.BoolValue or AnyValue.KindOneofCase.BlobValue => false,

            // 🔴 IsNull and None are handled above, so this arm is reachable only for an arm added to
            // `common.v1.AnyValue` AFTER this validator was written - AND IT FAILS CLOSED. It used to
            // accept, on the reasoning that widening a published message must not retroactively
            // invalidate payloads. That reasoning belongs to a READ path; on the WRITE path it inverts
            // into the defect this whole validator exists to prevent, because an arm this code cannot
            // reason about is precisely an arm it cannot say is representable - and the value would then
            // travel on to be bound to a column of a type nothing checked it against. A new arm is
            // taught to this switch in the same change that adds it to the contract, and until then it
            // is refused with a defined error rather than admitted on trust (AAP 0.1.5).
            _ => false,
        };
    }

    /// <summary>
    /// Whether a numeric wire value can be held by the numeric subtype a column declares.
    /// </summary>
    /// <param name="value">The value, known to be one of the four numeric arms.</param>
    /// <param name="integral">
    /// <see langword="true"/> for a column the legacy coerces with <c>Long()</c> - <c>long</c> or
    /// <c>ulong</c> - and <see langword="false"/> for one it coerces with <c>Dec()</c>.
    /// </param>
    /// <param name="columnType">The declared type, for the signed-versus-unsigned distinction.</param>
    /// <returns><see langword="true"/> when the declared subtype can hold the value.</returns>
    /// <remarks>
    /// <para>
    /// <b>THIS IS VALIDATION AND NOT COERCION, WHICH IS THE C-B LINE.</b> Nothing here converts, rounds,
    /// truncates or clamps a value: the ported <c>dwnvl*</c> coercions are the legacy's own and are used
    /// as PREDICATES only, exactly as they already were for the text arm. The legacy's coercing behaviour
    /// belongs to the item-change path, where edit text becomes a buffer value; the update path receives
    /// buffer values already, so coercing one here would CHANGE a value the caller sent rather than
    /// judging it.
    /// </para>
    /// <para>
    /// THE THREE REFUSALS, EACH WITH ITS OWN REASON:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///     <b>NON-FINITE.</b> <c>NaN</c>, <c>+∞</c> and <c>-∞</c> are legal <c>double</c> values and no
    ///     legal DataWindow numeric value: PowerBuilder has no literal for any of them, and the storage
    ///     engine behind this contract records a non-finite REAL as <c>NULL</c>, so accepting one turns a
    ///     write into a null - or into a NOT NULL violation from the driver - with nothing in the
    ///     response saying so.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     <b>FRACTIONAL INTO AN INTEGRAL COLUMN.</b> The legacy coerces a <c>long</c> column with
    ///     <c>Long()</c>, which TRUNCATES rather than rounds, so admitting 3.7 would store 3 - a value
    ///     the caller did not send, arrived at silently. The column type is what makes it a fault: the
    ///     identical value is fine for <c>decim</c>, <c>real</c> or <c>numbe</c>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     <b>OUT OF DOMAIN.</b> A negative value for an <c>ulong</c> column is outside the type's
    ///     domain, and a magnitude beyond <see cref="long"/> for a signed integral column cannot be
    ///     represented at all. Both are refused rather than wrapped.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// A DECIMAL ARM CARRIES CANONICAL TEXT [<c>common.v1.DecimalValue</c>], so an unparseable one is
    /// refused here too rather than being left to fail inside the receiving codec, where the answer would
    /// be a generic invalid-payload code instead of a per-column one.
    /// </para>
    /// </remarks>
    private static bool IsRepresentableNumber(AnyValue value, bool integral, string columnType)
    {
        switch (value.KindCase)
        {
            case AnyValue.KindOneofCase.Int64Value:
                // An int64 fits any numeric column except an unsigned one it is negative for.
                return !(value.Int64Value < 0L && IsUnsignedIntegralColumnType(columnType));

            case AnyValue.KindOneofCase.Uint64Value:
                // A uint64 is non-negative by construction; for a SIGNED integral column it must still
                // fit, because the carrier's own long domain is what will hold it.
                return !integral
                    || IsUnsignedIntegralColumnType(columnType)
                    || value.Uint64Value <= long.MaxValue;

            case AnyValue.KindOneofCase.DoubleValue:
                double number = value.DoubleValue;

                if (!double.IsFinite(number))
                {
                    return false;
                }

                if (!integral)
                {
                    return true;
                }

                return number == Math.Truncate(number)
                    && number >= (IsUnsignedIntegralColumnType(columnType) ? 0d : long.MinValue)
                    && number <= long.MaxValue;

            case AnyValue.KindOneofCase.DecimalValue:
                if (!decimal.TryParse(
                        value.DecimalValue?.Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out decimal exact))
                {
                    return false;
                }

                if (!integral)
                {
                    return true;
                }

                return exact == decimal.Truncate(exact)
                    && exact >= (IsUnsignedIntegralColumnType(columnType) ? 0m : long.MinValue)
                    && exact <= long.MaxValue;

            default:
                // Unreachable: the caller dispatches this method on the four numeric arms alone. Fails
                // closed for the same reason the caller's own default arm does.
                return false;
        }
    }

    /// <summary>
    /// Whether an integral column type is the UNSIGNED one of the pair the legacy's <c>Long()</c> arm
    /// covers.
    /// </summary>
    /// <param name="columnType">The declared type.</param>
    /// <returns><see langword="true"/> for <c>ulong</c>.</returns>
    /// <remarks>
    /// Matched through the same five-character truncation the legacy dispatches on
    /// [<c>se_cst_dw.sru:L236</c>], so <c>"unsigned long"</c> truncates to <c>"unsig"</c> and matches
    /// nothing here exactly as it matches nothing there. The token is read from
    /// <see cref="NumberValidator.LongCoercionColumnTypePrefixes"/> rather than restated, so the pair
    /// cannot drift apart.
    /// </remarks>
    private static bool IsUnsignedIntegralColumnType(string columnType) =>
        string.Equals(
            NumberValidator.ColumnTypePrefix(columnType),
            NumberValidator.LongCoercionColumnTypePrefixes[1],
            StringComparison.Ordinal);

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

    /// <summary>Builds the missing-baseline refusal.</summary>
    /// <param name="row">The row the column was in.</param>
    /// <param name="column">The column that stated no original.</param>
    /// <param name="columnType">The declared type, reported for the operator's benefit.</param>
    /// <returns>The failure.</returns>
    /// <remarks>
    /// <para>
    /// A DEFINED ERROR WITH NO ORACLE, AND IT SAYS SO. In process there is no such condition to reproduce
    /// - the runtime maintains a row's originals itself, so a caller cannot omit one - which makes this a
    /// fault the boundary newly permits and therefore a defined error rather than a translated dialog
    /// (AAP 0.1.5). It reports <c>localized = false</c> and claims NO localization category, because
    /// claiming one would tell a characterization comparison that a translation table answered when none
    /// did. That is the identical posture the unknown-column refusal takes, for the identical reason.
    /// </para>
    /// <para>
    /// THE TEXT CARRIES ONLY WHAT THE CALLER ALREADY SENT - a column name and an ordinal - and never a
    /// value, an exception detail or anything read out of storage (CWE-209, C-F).
    /// </para>
    /// <para>
    /// THE CODE IS <c>E_INVALID_ARGUMENT</c> AND NOT <c>E_INVALID_DATA</c>, because what is wrong is the
    /// SHAPE of the request rather than a value in it: the payload omitted a member the update contract
    /// requires. It is also what makes the row's outcome code the structural one, since
    /// <c>Grpc/DataWindowService.BuildValidationRefusal</c> reports the structural code whenever any
    /// addressing-or-shape fault is present - a caller must fix the shape before its values mean
    /// anything. Both codes project to HTTP 400 at the ingress, so the choice changes the diagnosis a
    /// caller reads rather than the status.
    /// </para>
    /// </remarks>
    private static UpdateRowValidationFailure BuildMissingOriginal(
        DataWindowRow row,
        ColumnValue column,
        string columnType)
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
            columnType,
            UpdateRowValidationKind.MissingOriginalValue,
            RetCode.E_INVALID_ARGUMENT,
            ValidationStructuredError.Create(
                MissingOriginalTitle,
                MissingOriginalTemplate,
                DialogSeverity.StopSign,
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
