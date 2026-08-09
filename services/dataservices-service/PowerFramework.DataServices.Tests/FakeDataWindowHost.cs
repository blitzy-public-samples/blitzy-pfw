// =====================================================================================================
//  FakeDataWindowHost.cs
//  =====================================================================================================
//  SHARED TEST INFRASTRUCTURE FOR THE DATASERVICES DOMAIN SUITES. THIS FILE IS NOT A TEST SUITE.
//
//  It declares no [Fact], no [Theory] and calls no Assert, and its file name deliberately carries no
//  `Tests` suffix so a reader scanning the folder does not mistake it for a suite. Everything here is an
//  in-memory DOUBLE for the DataWindow host contract plus the ordered call log the suites assert
//  against. It is authored BEFORE the suites that consume it because almost every one of them needs it,
//  and its public surface is therefore deliberately stable and obvious.
//
//  WHY THIS FILE EXISTS AT ALL (AAP 0.5.1 + 0.5.3)
//      The dependency inventory contains NO MOCKING LIBRARY. There is no Moq, no NSubstitute and no
//      FakeItEasy anywhere in Directory.Packages.props, and AAP 0.5.3 forbids adding an "obvious"
//      package - a sixth package would additionally fail restore outright, because central package
//      management carries no PackageVersion entry for it. Every test double in this project is
//      therefore hand written, and this is the largest of them. Do not attempt to add a package.
//
//  WHAT IT DOUBLES, AND WHERE THE CONTRACT LIVES
//      services/dataservices-service/PowerFramework.DataServices/Domain/DataWindowServiceHost.cs is the
//      authoritative contract - the abstract stand in for the DEFERRED DesignSystem type
//      `se_cst_datawindow` that AAP 0.2.1.3 Correction 3 requires DataServices to declare for itself.
//      Everything below implements exactly what that file declares, and NOTHING ELSE. The contract was
//      re-read member by member after this file was written; the two places where it diverges from the
//      folder brief's enumerated member list are called out under DIVERGENCES below, and in both the
//      contract wins.
//
//  BEHAVIOURAL ORACLE (constraint C-C: the legacy tree is READ ONLY and is never an edit target)
//      ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru      (616 lines) - the 22 event chain,
//          the item change micro protocol at :L182-L254, the validation error protocol at :L322-L385,
//          and the eleven semantic events the host raises.
//      ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru    (864 lines) - every `Describe`
//          property expression the service layer reads, and the child DataWindow value map path at
//          :L580-L670.
//      ws_objects/pfw.tests.pbl.src/dw_sqlite.srd                    (37 lines)  - THE PRIMARY FIXTURE.
//          The only updatable DataWindow in the whole repository, and therefore the golden master for
//          the retrieval / validation / update triple.
//      ws_objects/pfw.tests.pbl.src/dw_test_dwsvc.srd, dw_test_dwsvc_dddw.srd,
//      dw_test_dwsvc_contextmenu.srd, dw_test_dwsvc_columnexp.srd                - the smaller service
//          fixtures, modelled ONLY as far as a consuming test actually reads them.
//      ws_objects/pfw.ui.controls.ext.pbl.src/se_cst_datawindow.sru               - REFERENCE ONLY. Not
//          one line of it is doubled here, for the reason the contract's own header establishes line by
//          line: it is 100% theming and contributes zero consumed members.
//
//      NOTHING HERE READS THE LEGACY TREE. Every `ws_objects/**` path in this file appears in a COMMENT.
//      Fixture values are TRANSCRIBED as C# literals, each carrying the locator it came from, and no
//      `.srd` file is parsed at build time or at run time. That is not merely convenient: the root
//      .dockerignore excludes ws_objects/ from the build context, so a read would fail in a container
//      and in CI even where it happened to work locally.
//
//  =====================================================================================================
//  THE GOVERNING RULE OF THIS FILE: IT IS DUMB AND FAITHFUL (constraint C-B)
//  =====================================================================================================
//  This double models the legacy DataWindow's OBSERVABLE SURFACE, quirks included. It never validates,
//  coerces, normalises, tidies or "helpfully" rejects anything a real DataWindow would accept, because
//  the code under test relies on the host being permissive. The load-bearing examples:
//
//      * SetItem WITH A VALUE OF A DIFFERENT TYPE THAN THE COLUMN IS RECORDED, NOT REJECTED. The restore
//        path at se_cst_dw.sru:L219 and :L375 writes back a snapshotted `any` without knowing the
//        column's type, and Domain/ItemChangeProtocol.cs depends on that write landing. A double that
//        type checked would make the restore arm unreachable.
//      * NULL IS REPRESENTABLE FOR EVERY COLUMN VALUE, AND `Primary[row]` CAN RETURN NULL. The equality
//        test at se_cst_dw.sru:L198-L202 carries an explicit `IsNull(aOrgValue) and
//        IsNull(dwo.Primary[row])` arm whose entire purpose is to classify null-versus-null as equal,
//        and the tri-state predicate family turns on the same distinction. Null is NEVER collapsed to
//        zero or to the empty string anywhere below (AAP 0.4.5.4 forbids it in terms).
//      * `Primary[row]` IS SETTABLE BY THE TEST AND READABLE AFTER THE CODE UNDER TEST CALLS SetItem.
//        se_cst_dw.sru:L198-L202 compares the snapshot against `dwo.Primary[row]` AFTER the item change
//        handler has run, so that comparison only means something if a handler can mutate the buffer
//        mid flight. The contract's IDataWindowValueBuffer indexer is get only; the concrete buffer here
//        implements it with a get/SET indexer, which is exactly how the mid flight mutation is staged.
//      * ROWS ARE ONE BASED, everywhere, with an explicit conversion helper. AAP 0.4.5.4 names one based
//        to zero based translation the single most dangerous mechanical hazard in this refactor, and a
//        double that was silently zero based would make every consuming test lie about the subject.
//      * THE Describe SENTINELS ARE NOT NORMALISED. `"!"` (invalid expression), `"?"` (value cannot be
//        determined) and `""` are three DISTINCT outcomes that ported callers branch on individually at
//        n_cst_dwsvc.sru:L587, :L590, :L598 and :L599. An unknown key returns `"!"` and NEVER throws.
//      * SUCCESS IS 1, NOT 0, for SetRow, AcceptText, SetRedraw, SetFocus, SetItem, SetItemStatus,
//        Filter and DeleteRow; AcceptText's FAILURE IS -1 [se_cst_dw.sru:L554]. None of them is mapped
//        onto RetCode, whose OK is 0 - conflating the two numbering schemes inverts every success test,
//        so this file references no RetCode constant at all.
//
//  =====================================================================================================
//  DIVERGENCES FROM THE FOLDER BRIEF'S ENUMERATED MEMBER LIST - THE CONTRACT WINS IN BOTH
//  =====================================================================================================
//  DIVERGENCE 1 - THE SIX TYPED GETTERS ARE ON THE CHILD, NOT ON THE HOST. The brief lists
//      GetItemDecimal / GetItemNumber / GetItemString / GetItemDateTime / GetItemDate / GetItemTime
//      without saying which object carries them. The contract's DECISION 2 measured all twelve call
//      sites onto `dwc`, the `datawindowchild` from `#DataWindow.GetChild`
//      (n_cst_dwsvc.sru:L605-L615 and :L621-L631), and records that there is not one
//      `#DataWindow.GetItem*` call anywhere in either ported source. They are therefore implemented on
//      FakeDataWindowChild, addressed BY COLUMN NAME rather than by number, and all six return nullable
//      types because the oracle skips a row on a null result [:L617, :L633].
//  DIVERGENCE 2 - THE FOCUS FUNCTION IS NAMED GetFocusedObject. The brief lists `GetFocus`. The contract's
//      DECISION 3 renamed the PowerScript SYSTEM function used at se_cst_dw.sru:L553 to
//      GetFocusedObject() because it collides with the SEMANTIC EVENT `Event GetFocus()` [:L400], and C#
//      forbids two members differing only in return type. Both appear below under the contract's names.
//
//  =====================================================================================================
//  WHAT IS DELIBERATELY ABSENT, AND WHY EACH ABSENCE IS A DECISION (constraint C-K)
//  =====================================================================================================
//  NO RButtonUp AND NO LButtonUp SEMANTIC EVENT. `ondwnrbuttonup` [se_cst_dw.sru:L120] triggers only the
//      broker and `ondwnlbuttonup` [:L395] does the same. There is no semantic counterpart to either, so
//      inventing one here would fabricate a hook the oracle does not have. That asymmetry is asserted by
//      the event chain suites, so the absence is load bearing rather than incidental.
//  NO WINDOW HANDLE, DPI, FONT, COLOUR, CANVAS, PAINTER OR MENU RENDERING MEMBER (constraint C-D).
//      DesignSystem is a DEFERRED service that must not be implemented even partially and even to stub
//      it out, so no member below names or types any of those concepts. `SetRedraw` is the one
//      borderline-presentational member and it is here only because the CONTRACT declares it abstract -
//      the contract's own remarks record why it stays and why a headless no-op is not a stub. A suite
//      that needs to prove a rendering artifact is absent proves it against the contract, never by
//      adding a member here.
//  NO STORAGE, NO SQL, NO FILE ACCESS (constraint C-E). This file is in memory only: no System.IO, no
//      Microsoft.Data.Sqlite, no Entity Framework Core, no connection and no DDL. Persistence is the
//      only service in the system that holds a storage provider, and these tests provision no database.
//  NO SECRET, KEY, PASSWORD, TOKEN OR CREDENTIAL of any kind (constraint C-F). Nothing in this file is
//      security sensitive and no literal below resembles credential material.
//  NO SCREAMING_SNAKE MEMBER DECLARATION. The root .editorconfig scopes its CA1707 and IDE1006
//      suppressions to the individually named non-test files on its BAND 3 roster - the single source
//      of truth for that list; NO TEST FILE IS SUPPRESSED, and under
//      TreatWarningsAsErrors a naming diagnostic here would be an error rather than a warning. This file
//      may freely REFERENCE a preserved constant such as DataWindowServiceBase.STYLE_GRID, and it
//      declares none of its own.
//  NO PERFORMANCE CLAIM (AAP 0.8.5). The repository publishes no SLA, no latency budget, no throughput
//      target and no availability commitment, so no member below is described as fast or optimised.
//
//  =====================================================================================================
//  THE CALL LOG POLICY, STATED ONCE SO NO SUITE HAS TO GUESS IT
//  =====================================================================================================
//  DataWindowEventChainTests, EventOrderingPatternTests and ValidationErrorEventTests assert SEQUENCES
//  and not just outcomes, so the log exists to make order observable. Three rules govern what reaches it:
//
//      1. EVERY MUTATING CONTRACT MEMBER AND EVERY ONE OF THE ELEVEN EVENT RAISES IS ALWAYS RECORDED.
//         That is the sequence a suite asserts on.
//      2. READS ARE OPT IN, through RecordsReads. Describe alone has 37 measured call sites and would
//         swamp a sequence assertion, so reads stay out of the log by default and a suite that genuinely
//         wants to prove read ordering - a snapshot taken BEFORE a write, say - turns them on
//         explicitly.
//      3. TEST SETUP MUTATORS ARE NEVER RECORDED. AddRow, SetBufferValue and their siblings are how a
//         suite arranges the world; they are not calls the subject made. The one exception is
//         SimulateRowDeletedByPostedMessage, which models a real posted deletion arriving mid flight and
//         is recorded precisely because a suite may need to assert where in the sequence it landed.
//
//  Each SetItem record additionally carries the OVERLOAD'S VALUE TYPE SPELLING, because the coercion
//  table at se_cst_dw.sru:L231-L244 dispatches to a specific overload and proving WHICH arm ran is the
//  whole point of the item change parity matrix. That is also why FakeColumnType publishes `"datetime"`
//  alongside `"date"`: `Left(colType,5)` truncates the former to `"datet"` and the latter to `"date"`,
//  and a StartsWith-versus-exact-equality confusion between those two arms is the single most likely
//  silent defect in the whole coercion table.
//
//  =====================================================================================================
//  SELF CHECKS THE CONSUMING SUITES SHOULD CARRY, AND WHY THEY BELONG THERE RATHER THAN HERE
//  =====================================================================================================
//  This file declares no test of its own, on purpose - it is infrastructure, and a suite embedded in it
//  would be a suite nobody scanning the folder expects to find. The checks below were all EXERCISED AND
//  PASSED against this file during its validation; they are listed so that whichever sibling suite is
//  authored next adopts the ones that matter to it, rather than each suite rediscovering them:
//
//      1. ONE BASED INDEXING, WHICH IS THE MOST IMPORTANT ONE. Seed three rows and assert that
//         RowCount() is 3, that AddRow returned 1, 2 and 3 in order, and that row 1 - not row 0 - holds
//         the first row's values. AAP 0.4.5.4 names one based to zero based translation the single most
//         dangerous mechanical hazard in this refactor, and a fake that were silently zero based would
//         make every other assertion in every other suite quietly wrong.
//      2. NULL NEVER COLLAPSES. Write null through SetItem, read it back through `dwo.Primary[row]`, and
//         assert it is still null rather than 0 or "".
//      3. THE COERCION NEAR MISS. Assert FakeColumnType.CoercionPrefix(FakeColumnType.DateTime) is
//         "datet" and that it differs from CoercionPrefix(FakeColumnType.Date).
//      4. THE MID FLIGHT MUTATION. From inside a scripted ItemChangedHandler, write through
//         DwObject(name).Primary[row], and assert the value the code under test then reads is the
//         mutated one.
//      5. THE UNKNOWN Describe KEY. Assert it answers "!" and does not raise.
//      6. THE OVERLOAD LOG. Call all seven SetItem overloads and assert CallLog.Signatures reports
//         SetItem(string?) through SetItem(object?) in order.
// =====================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using PowerFramework.DataServices.Domain;
using PowerFramework.Shared.Eventful;

// The two published contract enums are ALIASED rather than imported wholesale, and the alias is
// mandatory rather than cosmetic. `PowerFramework.Contracts.Common.V1` also contains a generated
// wrapper message class named RetCode - common.v1.proto nests each legacy constant set as an enum named
// Value inside a thin wrapper message - so a plain namespace import would put a second `RetCode` in
// scope and make every mention of it ambiguous (CS0104). Under TreatWarningsAsErrors that is a build
// failure. Aliasing exactly the two types needed keeps the wrapper message out of scope entirely and
// states the dependency precisely. This mirrors the identical decision in the contract file itself.
using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
using ItemStatus = PowerFramework.Contracts.Common.V1.ItemStatus;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The explicit one-based to zero-based row-number conversion every buffer in this file goes through.
/// </summary>
/// <remarks>
/// <para>
/// PowerBuilder DataWindow rows are ONE BASED, and <c>RowCount()</c> returns the LAST VALID ROW NUMBER
/// rather than a zero-based length - which is why the oracle's loops read
/// <c>for nRow = 1 to nRowCnt</c> [<c>n_cst_dwsvc.sru:L601</c>] and its bounds tests read
/// <c>row &lt;= RowCount()</c> [<c>se_cst_dw.sru:L369</c>] and <c>r &gt; RowCount()</c> [<c>:L421</c>].
/// </para>
/// <para>
/// AAP 0.4.5.4 names one-based to zero-based translation THE SINGLE MOST DANGEROUS MECHANICAL HAZARD in
/// this refactor, because a silent off-by-one is indistinguishable from a behavioural regression. This
/// type exists so the conversion happens in exactly one place, by name, rather than as an inline
/// <c>- 1</c> repeated at every access. A double that was quietly zero-based would make every consuming
/// suite lie about the subject, so the conversion is centralised and the range check is explicit.
/// </para>
/// </remarks>
public static class OneBasedRows
{
    /// <summary>
    /// The row number of the first row, which is <c>1</c> and never <c>0</c>.
    /// </summary>
    public static long FirstRow => 1L;

    /// <summary>
    /// Converts a one-based DataWindow row number into the zero-based list index that backs it.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <returns>The zero-based index, which is <paramref name="row"/> minus one.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="row"/> is below <see cref="FirstRow"/>, or is larger than a list index can
    /// express. Both are programming faults in test setup rather than data conditions, so they fail
    /// loudly instead of silently clamping - a clamp is exactly the silent off-by-one this type exists
    /// to prevent.
    /// </exception>
    public static int ToListIndex(long row)
    {
        if (row < FirstRow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(row),
                row,
                "DataWindow rows are one-based, so the lowest valid row number is 1. A row of 0 means "
                + "'no current row' when GetRow() reports it and must never be converted to an index.");
        }

        if (row > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(row),
                row,
                "The row number exceeds the largest index an in-memory buffer can address.");
        }

        return (int)(row - 1L);
    }

    /// <summary>
    /// Converts a zero-based list index back into the one-based row number that names it.
    /// </summary>
    /// <param name="listIndex">The zero-based index.</param>
    /// <returns>The one-based row number, which is <paramref name="listIndex"/> plus one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="listIndex"/> is negative.</exception>
    public static long ToRowNumber(int listIndex)
    {
        if (listIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(listIndex),
                listIndex,
                "A buffer index is never negative.");
        }

        return listIndex + 1L;
    }

    /// <summary>
    /// Whether <paramref name="row"/> names a row that exists in a buffer holding
    /// <paramref name="rowCount"/> rows.
    /// </summary>
    /// <param name="row">The one-based row number to test.</param>
    /// <param name="rowCount">The number of rows the buffer holds.</param>
    /// <returns>
    /// <see langword="true"/> when <c>1 &lt;= row &lt;= rowCount</c>; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// This is the port of the oracle's own bounds tests, and the upper bound is INCLUSIVE for the reason
    /// stated on this type: <c>RowCount()</c> is the last valid row number. The guard at
    /// <c>se_cst_dw.sru:L369</c> is <c>row &lt;= RowCount()</c>, not <c>row &lt; RowCount()</c>.
    /// </remarks>
    public static bool IsInRange(long row, int rowCount)
    {
        return row >= FirstRow && row <= rowCount;
    }
}

/// <summary>
/// One entry in a <see cref="DataWindowCallLog"/>: the name of the member that was called or the event
/// that was raised, the value-type spelling when the member is an overload set, and the arguments it was
/// handed.
/// </summary>
/// <remarks>
/// <para>
/// IMMUTABLE, AND THE ARGUMENT LIST IS COPIED ON THE WAY IN. A suite reads a log entry after the subject
/// has moved on, so an entry that aliased a caller's array could report a value the subject never
/// actually passed. Copying is what makes the log a record of history rather than a window onto live
/// state.
/// </para>
/// <para>
/// NOTHING IS NORMALISED HERE EITHER. The arguments are stored exactly as received, including nulls,
/// including values whose runtime type does not match the column they were written to, and in the order
/// they were received. The suites assert that the SUBJECT loses no argument and reorders nothing, so a
/// record that tidied its input would be asserting its own behaviour instead of the subject's.
/// </para>
/// </remarks>
public sealed class DataWindowCallRecord
{
    private readonly object?[] _arguments;

    /// <summary>
    /// Creates a record.
    /// </summary>
    /// <param name="member">
    /// The member or event name, spelled exactly as the contract spells it - for example
    /// <c>"SetItem"</c>, <c>"SetItemStatus"</c> or <c>"Event ItemChanged"</c>.
    /// </param>
    /// <param name="valueTypeName">
    /// For a member with several overloads, the declared spelling of the overload's value parameter - for
    /// example <c>"DateTime?"</c>. <see langword="null"/> for every member that has exactly one
    /// signature.
    /// </param>
    /// <param name="arguments">
    /// The salient arguments, in call order. A <see langword="null"/> array is treated as no arguments,
    /// which is distinct from an array holding one null element.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> is <see langword="null"/>.</exception>
    public DataWindowCallRecord(string member, string? valueTypeName, params object?[]? arguments)
    {
        ArgumentNullException.ThrowIfNull(member);

        Member = member;
        ValueTypeName = valueTypeName;
        _arguments = arguments is null ? [] : (object?[])arguments.Clone();
    }

    /// <summary>
    /// The member or event name, spelled as the contract spells it.
    /// </summary>
    public string Member { get; }

    /// <summary>
    /// The overload's value-type spelling, or <see langword="null"/> when the member is not overloaded.
    /// </summary>
    /// <remarks>
    /// Populated only by the seven <c>SetItem</c> overloads. It is the mechanism by which the item-change
    /// parity matrix proves WHICH arm of the coercion table at
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L231-L244</c> ran - most importantly
    /// that a <c>"datetime"</c> column reaches the <c>DateTime?</c> overload through the <c>"datet"</c>
    /// arm and never the <c>DateOnly?</c> overload through the <c>"date"</c> arm.
    /// </remarks>
    public string? ValueTypeName { get; }

    /// <summary>
    /// The arguments, in call order, exactly as received.
    /// </summary>
    public IReadOnlyList<object?> Arguments => _arguments;

    /// <summary>
    /// The member name qualified by its overload, which is the key a sequence assertion compares on -
    /// for example <c>"SetItem(DateTime?)"</c>, or plain <c>"AcceptText"</c> for a member with one
    /// signature.
    /// </summary>
    public string Signature => ValueTypeName is null ? Member : Member + "(" + ValueTypeName + ")";

    /// <summary>
    /// The signature followed by the formatted arguments, for an assertion that cares about the values as
    /// well as the order, and for a readable failure message.
    /// </summary>
    public string Description
    {
        get
        {
            StringBuilder text = new(Signature);
            text.Append('[');

            for (int index = 0; index < _arguments.Length; index++)
            {
                if (index > 0)
                {
                    text.Append(", ");
                }

                text.Append(FormatArgument(_arguments[index]));
            }

            text.Append(']');
            return text.ToString();
        }
    }

    /// <summary>
    /// Renders one argument for <see cref="Description"/>.
    /// </summary>
    /// <param name="value">The argument to render.</param>
    /// <returns>The rendered text.</returns>
    /// <remarks>
    /// <para>
    /// PUBLIC ON PURPOSE, so a suite can compose an expected description with the same formatter the log
    /// uses rather than guessing at it. Every conversion is invariant-culture: a log entry is structural
    /// data, and a culture with a comma decimal separator would otherwise make an expected string
    /// machine-dependent.
    /// </para>
    /// <para>
    /// A <see cref="IDataWindowObject"/> renders as <c>dwo:</c> followed by its name rather than by its
    /// default <see cref="object.ToString"/>, because the name is what identifies a column in the oracle
    /// and a reference hash identifies nothing to a reader.
    /// </para>
    /// </remarks>
    public static string FormatArgument(object? value)
    {
        switch (value)
        {
            case null:
                // Rendered as a word rather than as an empty gap, because "the argument was null" and
                // "the argument was the empty string" are different facts and both occur.
                return "null";

            case string text:
                return "\"" + text + "\"";

            case bool flag:
                return flag ? "true" : "false";

            case IDataWindowObject dwo:
                return "dwo:" + dwo.Name;

            case DateTime moment:
                return moment.ToString("O", CultureInfo.InvariantCulture);

            case DateOnly day:
                return day.ToString("O", CultureInfo.InvariantCulture);

            case TimeOnly timeOfDay:
                return timeOfDay.ToString("O", CultureInfo.InvariantCulture);

            case IFormattable formattable:
                // Covers every numeric type and both contract enums.
                return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;

            default:
                return value.ToString() ?? string.Empty;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Description;
}

/// <summary>
/// The ordered, append-only record of what the code under test did to a <see cref="FakeDataWindowHost"/>.
/// </summary>
/// <remarks>
/// <para>
/// Exposed read-only from the host. The three policy rules that decide what reaches it are stated once,
/// in this file's header, so no suite has to infer them: every mutating contract member and every one of
/// the eleven event raises is always recorded; reads are opt-in through
/// <see cref="FakeDataWindowHost.RecordsReads"/>; and test-setup mutators are never recorded.
/// </para>
/// <para>
/// The projections below exist because different assertions want different granularity.
/// <see cref="Signatures"/> is the usual one - it proves order and overload selection without coupling
/// the assertion to argument formatting - while <see cref="Descriptions"/> proves the values as well.
/// </para>
/// </remarks>
public sealed class DataWindowCallLog
{
    private readonly List<DataWindowCallRecord> _records = [];

    /// <summary>
    /// Every record, in the order it was appended.
    /// </summary>
    public IReadOnlyList<DataWindowCallRecord> Records => _records;

    /// <summary>
    /// The number of records.
    /// </summary>
    public int Count => _records.Count;

    /// <summary>
    /// The member name of every record, in order, ignoring overload selection.
    /// </summary>
    public IReadOnlyList<string> Members
    {
        get
        {
            List<string> members = new(_records.Count);
            foreach (DataWindowCallRecord record in _records)
            {
                members.Add(record.Member);
            }

            return members;
        }
    }

    /// <summary>
    /// The overload-qualified signature of every record, in order. This is the projection a sequence
    /// assertion normally compares against.
    /// </summary>
    public IReadOnlyList<string> Signatures
    {
        get
        {
            List<string> signatures = new(_records.Count);
            foreach (DataWindowCallRecord record in _records)
            {
                signatures.Add(record.Signature);
            }

            return signatures;
        }
    }

    /// <summary>
    /// The full description, arguments included, of every record, in order.
    /// </summary>
    public IReadOnlyList<string> Descriptions
    {
        get
        {
            List<string> descriptions = new(_records.Count);
            foreach (DataWindowCallRecord record in _records)
            {
                descriptions.Add(record.Description);
            }

            return descriptions;
        }
    }

    /// <summary>
    /// Appends a record for a member that has exactly one signature.
    /// </summary>
    /// <param name="member">The member or event name.</param>
    /// <param name="arguments">The salient arguments, in call order.</param>
    public void Record(string member, params object?[]? arguments)
    {
        _records.Add(new DataWindowCallRecord(member, valueTypeName: null, arguments));
    }

    /// <summary>
    /// Appends a record for one overload of an overloaded member.
    /// </summary>
    /// <param name="member">The member name.</param>
    /// <param name="valueTypeName">The declared spelling of the overload's value parameter.</param>
    /// <param name="arguments">The salient arguments, in call order.</param>
    public void RecordOverload(string member, string valueTypeName, params object?[]? arguments)
    {
        ArgumentNullException.ThrowIfNull(valueTypeName);

        _records.Add(new DataWindowCallRecord(member, valueTypeName, arguments));
    }

    /// <summary>
    /// Discards every record.
    /// </summary>
    /// <remarks>
    /// Called by the fixture factories once seeding is complete, so a suite that arranges a fixture and
    /// then acts sees a log containing only what the subject did. A suite may also call it between the
    /// arrange and act phases of a longer scenario.
    /// </remarks>
    public void Clear()
    {
        _records.Clear();
    }

    /// <summary>
    /// The record at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">The zero-based position in the log. The log is not a DataWindow buffer, so it
    /// is indexed the way a .NET list is indexed rather than the way a row is numbered.</param>
    /// <returns>The record.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> does not name a record. The message reports how many records there are,
    /// because a sequence assertion that fails here has almost always mis-predicted the length.
    /// </exception>
    public DataWindowCallRecord RecordAt(int index)
    {
        if (index < 0 || index >= _records.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                "The call log holds "
                    + _records.Count.ToString(CultureInfo.InvariantCulture)
                    + " record(s).");
        }

        return _records[index];
    }

    /// <summary>
    /// The position of the first record whose <see cref="DataWindowCallRecord.Signature"/> is
    /// <paramref name="signature"/>, or <c>-1</c> when there is none.
    /// </summary>
    /// <param name="signature">The overload-qualified signature to find, compared ordinally.</param>
    /// <returns>The zero-based position, or <c>-1</c>.</returns>
    /// <remarks>
    /// Returning <c>-1</c> rather than throwing is what lets a suite assert relative order - that one
    /// call happened before another - in a single readable expression.
    /// </remarks>
    public int IndexOf(string signature)
    {
        for (int index = 0; index < _records.Count; index++)
        {
            if (string.Equals(_records[index].Signature, signature, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Whether any record carries <paramref name="signature"/>.
    /// </summary>
    /// <param name="signature">The overload-qualified signature to look for.</param>
    /// <returns><see langword="true"/> when at least one record matches.</returns>
    public bool Contains(string signature) => IndexOf(signature) >= 0;

    /// <summary>
    /// How many records carry <paramref name="signature"/>.
    /// </summary>
    /// <param name="signature">The overload-qualified signature to count.</param>
    /// <returns>The number of matching records.</returns>
    /// <remarks>
    /// The item-change protocol raises <c>EditChanged</c> a SECOND time from inside itself when the
    /// buffer value turns out to have moved [<c>se_cst_dw.sru:L204-L208</c>], so "how many times" is a
    /// distinct question from "did it at all" and both are asked.
    /// </remarks>
    public int CountOf(string signature)
    {
        int total = 0;
        foreach (DataWindowCallRecord record in _records)
        {
            if (string.Equals(record.Signature, signature, StringComparison.Ordinal))
            {
                total++;
            }
        }

        return total;
    }
}


/// <summary>
/// The legacy <c>ColType</c> strings, spelled exactly as a DataWindow reports them, plus the
/// five-character prefix reduction the item-change coercion table keys on.
/// </summary>
/// <remarks>
/// <para>
/// THE EXACT SPELLING IS THE WHOLE POINT. <c>se_cst_dw.sru:L231</c> switches on
/// <c>Left(dwo.ColType,5)</c> - the FIRST FIVE CHARACTERS - across six arms:
/// <c>"char"</c>/<c>"char("</c>, <c>"decim"</c>/<c>"real"</c>/<c>"numbe"</c>,
/// <c>"long"</c>/<c>"ulong"</c>, <c>"datet"</c>, <c>"date"</c> and <c>"time"</c>. A double that reported
/// a tidied or enumerated column type would destroy that prefix match's input and
/// <c>Domain/ItemChangeProtocol.cs</c> would lose the six-arm dispatch it exists to reproduce.
/// </para>
/// <para>
/// <c>"datetime"</c> IS PUBLISHED SPECIFICALLY SO THE NEAR MISS IS TESTABLE. It truncates to
/// <c>"datet"</c> and <c>"date"</c> truncates to <c>"date"</c>, so the two reach DIFFERENT arms and
/// receive DIFFERENT conversions. Confusing a prefix match for an equality test - or the reverse - is
/// the single most likely silent defect in the whole coercion table, and it is only provable if a suite
/// can drive both spellings. <c>"char"</c> is published for the same reason: it is the unsized near miss
/// of <c>"char(100)"</c> and reaches the same arm by a different route.
/// </para>
/// <para>
/// Every member is a <see cref="string"/> constant or a string-producing method rather than an enum
/// member. An enum would invite a reader to add a value, to renumber, or to parse - and all three break
/// the prefix match (constraint C-B).
/// </para>
/// </remarks>
public static class FakeColumnType
{
    /// <summary>
    /// <c>"number"</c>, the type of <c>id</c> and <c>age</c> in the primary fixture
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8</c>, <c>:L10</c>]. Truncates to
    /// <c>"numbe"</c>.
    /// </summary>
    public const string Number = "number";

    /// <summary>
    /// <c>"real"</c>. Truncates to <c>"real"</c> and shares the decimal arm with
    /// <see cref="Number"/> and <c>decimal(n)</c>.
    /// </summary>
    public const string Real = "real";

    /// <summary>
    /// <c>"long"</c>. Truncates to <c>"long"</c> and reaches the integral arm.
    /// </summary>
    public const string Long = "long";

    /// <summary>
    /// <c>"ulong"</c>. Truncates to <c>"ulong"</c> - exactly five characters - and shares the integral
    /// arm with <see cref="Long"/>.
    /// </summary>
    public const string ULong = "ulong";

    /// <summary>
    /// <c>"char"</c>, the UNSIZED near miss of <c>char(n)</c>. Truncates to <c>"char"</c> and reaches the
    /// text arm by a different route than <see cref="CharOf(int)"/> does.
    /// </summary>
    public const string Char = "char";

    /// <summary>
    /// <c>"date"</c>, the type of <c>birth</c> in the primary fixture
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L13</c>]. Truncates to <c>"date"</c> and reaches
    /// the date arm, which converts to a date-only value.
    /// </summary>
    public const string Date = "date";

    /// <summary>
    /// <c>"datetime"</c>. Truncates to <c>"datet"</c> - NOT to <c>"date"</c> - and reaches the
    /// date-and-time arm. See this type's remarks for why the distinction is published deliberately.
    /// </summary>
    public const string DateTime = "datetime";

    /// <summary>
    /// <c>"time"</c>. Truncates to <c>"time"</c> and reaches the time-of-day arm.
    /// </summary>
    public const string Time = "time";

    /// <summary>
    /// Produces <c>"char(n)"</c> - for example <c>"char(100)"</c> for <c>name</c> and
    /// <c>"char(200)"</c> for <c>address</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L9</c>, <c>:L11</c>].
    /// </summary>
    /// <param name="width">The declared width in characters.</param>
    /// <returns>The column-type string.</returns>
    /// <remarks>
    /// Truncates to <c>"char("</c>, which is a SEPARATE case label from <c>"char"</c> in the coercion
    /// table even though both reach the same arm. The width is also what the commented-out byte-length
    /// check at <c>se_cst_dw.sru:L282-L284</c> parses back out with
    /// <c>Long(Mid(sProp,6,Len(sProp) - 6))</c>, which is a second reason the sized spelling has to be
    /// reproducible exactly. That check is carried across as commented and inert and is NOT revived
    /// (constraint C-B); the spelling is reproduced so a future revival would have the input it needs.
    /// </remarks>
    public static string CharOf(int width)
    {
        return "char(" + width.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Produces <c>"decimal(n)"</c> - for example <c>"decimal(2)"</c> for <c>salary</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L12</c>].
    /// </summary>
    /// <param name="scale">The declared number of decimal places.</param>
    /// <returns>The column-type string.</returns>
    /// <remarks>Truncates to <c>"decim"</c>.</remarks>
    public static string DecimalOf(int scale)
    {
        return "decimal(" + scale.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The port of <c>Left(colType,5)</c> - the five-character reduction the coercion table at
    /// <c>se_cst_dw.sru:L231</c> switches on.
    /// </summary>
    /// <param name="colType">The raw column-type string.</param>
    /// <returns>
    /// The first five characters, or the whole string when it is shorter than five characters.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Published so a suite can state its expectation in the oracle's own terms - asserting that
    /// <c>CoercionPrefix(FakeColumnType.DateTime)</c> is <c>"datet"</c> reads as a statement about the
    /// legacy rather than as a magic string.
    /// </para>
    /// <para>
    /// PowerScript's <c>Left</c> returns the whole string when it is shorter than the requested length
    /// rather than raising or padding, which is why a short type such as <c>"date"</c> or <c>"time"</c>
    /// reduces to itself. An empty column type reduces to the empty string and falls through every arm to
    /// the default - the same outcome the legacy reaches for any unrecognised type. A
    /// <see langword="null"/> argument is treated as the empty string, because PowerScript's <c>Left</c>
    /// of a null yields null and the switch then matches nothing, which is the identical observable
    /// result.
    /// </para>
    /// </remarks>
    public static string CoercionPrefix(string? colType)
    {
        if (string.IsNullOrEmpty(colType))
        {
            return string.Empty;
        }

        return colType.Length <= 5 ? colType : colType.Substring(0, 5);
    }
}

/// <summary>
/// One entry of a column's code table - the <c>display</c> and <c>value</c> pair a DataWindow reports as
/// a single tab-separated string from <c>GetValue</c>.
/// </summary>
/// <param name="Display">
/// The text shown to a user. In the service fixture this is Chinese - the <c>s1</c> column declares
/// <c>values="新建~tNEW/确认~tCFD/审核~tADT/"</c>
/// [<c>ws_objects/pfw.tests.pbl.src/dw_test_dwsvc.srd:L11</c>] - and the checkbox column's entries have
/// an EMPTY display, <c>values="~t1/~t0"</c>
/// [<c>dw_test_dwsvc_contextmenu.srd:L10</c>]. Both are reproduced as they are.
/// </param>
/// <param name="Value">The value stored in the buffer for that display text.</param>
/// <remarks>
/// A pair rather than a dictionary entry because ORDER MATTERS: the oracle walks the code table by index
/// from <c>1</c> upward and stops at the first empty answer
/// [<c>n_cst_dwsvc.sru:L650-L659</c>], so the declared order is part of the observable behaviour.
/// </remarks>
public readonly record struct FakeCodeTableEntry(string Display, string Value);

/// <summary>
/// The declaration of one named object on a fake DataWindow - a column, a computed field or a text
/// object - carrying every property the ported service layer reads through <c>Describe</c>.
/// </summary>
/// <remarks>
/// <para>
/// THE PROPERTY SET IS MEASURED, NOT IMAGINED. Every property below appears in a <c>Describe</c>
/// expression in one of the two ported sources: <c>.Name</c>, <c>.ColType</c>, <c>.Band</c>,
/// <c>.Type</c>, <c>.TabSequence</c>, <c>.Edit.DisplayOnly</c>, <c>.edit.style</c>, <c>.checkbox.on</c>,
/// <c>.checkbox.off</c>, <c>.dddw.name</c>, <c>.dddw.displaycolumn</c>, <c>.dddw.datacolumn</c>
/// [<c>n_cst_dwsvc.sru</c>], and <c>.ValidationMsg</c> [<c>se_cst_dw.sru:L350</c>]. The update-contract
/// flags and the format and edit-mask spellings come from the primary fixture's own declaration and are
/// carried because the update and sort suites read them.
/// </para>
/// <para>
/// A PROPERTY THAT DOES NOT APPLY ANSWERS WITH THE <c>"!"</c> SENTINEL RATHER THAN WITH AN EMPTY STRING,
/// because that is the distinction the oracle branches on: <c>n_cst_dwsvc.sru:L587</c> establishes
/// whether a column is a drop-down DataWindow at all by testing <c>sProp &lt;&gt; "!" and sProp &lt;&gt;
/// "?"</c> against <c>.dddw.name</c>. Answering with an empty string instead would make a plain column
/// look like a misconfigured drop-down.
/// </para>
/// <para>
/// <see cref="ExtraProperties"/> is the escape hatch for anything not enumerated, so a suite can teach
/// the fixture a property without this type growing a member for it.
/// </para>
/// </remarks>
public sealed class FakeDataWindowObjectDefinition
{
    private readonly List<FakeCodeTableEntry> _codeTable = [];
    private readonly Dictionary<string, string> _extraProperties =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Declares an object.
    /// </summary>
    /// <param name="name">The object name, as <c>dwo.Name</c> reports it.</param>
    /// <param name="type">
    /// The object type as <c>Describe(name + ".Type")</c> reports it - <see cref="ColumnType"/>,
    /// <see cref="ComputeType"/> or <see cref="TextType"/>.
    /// </param>
    /// <param name="band">
    /// The band as <c>Describe(name + ".Band")</c> reports it - for example <c>"detail"</c>,
    /// <c>"header"</c>, <c>"footer"</c> or <c>"summary"</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public FakeDataWindowObjectDefinition(string name, string type, string band)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(band);

        Name = name;
        Type = type;
        Band = band;
    }

    /// <summary>
    /// The <c>Describe(name + ".Type")</c> answer for a data column: <c>"column"</c>.
    /// </summary>
    public const string ColumnType = "column";

    /// <summary>
    /// The <c>Describe(name + ".Type")</c> answer for a computed field: <c>"compute"</c>.
    /// </summary>
    public const string ComputeType = "compute";

    /// <summary>
    /// The <c>Describe(name + ".Type")</c> answer for a static text object: <c>"text"</c>.
    /// </summary>
    public const string TextType = "text";

    /// <summary>The object name.</summary>
    public string Name { get; }

    /// <summary>The object type - one of <see cref="ColumnType"/>, <see cref="ComputeType"/> or
    /// <see cref="TextType"/>.</summary>
    public string Type { get; }

    /// <summary>The band the object sits in.</summary>
    public string Band { get; }

    /// <summary>
    /// The column number, which is also what <c>dwo.ID</c> reports. <c>0</c> for an object that is not a
    /// data column, because a computed field and a text object have no column number.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The raw column type, for example <c>"char(100)"</c>. The empty string for an object that is not a
    /// data column, in which case <c>Describe(name + ".ColType")</c> answers <c>"!"</c>.
    /// </summary>
    public string ColType { get; set; } = string.Empty;

    /// <summary>The database column name, from the fixture's <c>dbname=</c> attribute.</summary>
    public string DbName { get; set; } = string.Empty;

    /// <summary>Whether the column is updatable, from <c>update=yes</c>.</summary>
    public bool Update { get; set; }

    /// <summary>Whether the column is part of the update key, from <c>key=yes</c>.</summary>
    public bool Key { get; set; }

    /// <summary>Whether the column is the identity column, from <c>identity=yes</c>.</summary>
    public bool Identity { get; set; }

    /// <summary>
    /// Whether the column takes part in the optimistic-concurrency where-clause, from
    /// <c>updatewhereclause=yes</c>.
    /// </summary>
    /// <remarks>
    /// All six columns of the primary fixture carry it
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L13</c>], which combined with
    /// <c>updatewhere=1</c> is what makes the concurrency check span all six columns' ORIGINAL values.
    /// </remarks>
    public bool UpdateWhereClause { get; set; }

    /// <summary>
    /// The display format, reproduced with its ORIGINAL CASING.
    /// </summary>
    /// <remarks>
    /// THE CASING IS DELIBERATELY INCONSISTENT IN THE ORACLE AND IS NOT NORMALISED HERE. The primary
    /// fixture's six columns declare <c>format="[general]"</c> in LOWER CASE
    /// [<c>dw_sqlite.srd:L21-L26</c>] while its footer computed field declares <c>format="[GENERAL]"</c>
    /// in UPPER CASE [<c>:L27</c>], and the context-menu fixture adds a third spelling,
    /// <c>format="[General]"</c> in mixed case [<c>dw_test_dwsvc_contextmenu.srd</c>]. All three are
    /// carried verbatim: harmonising them would be exactly the silent correction constraint C-B forbids.
    /// </remarks>
    public string Format { get; set; } = string.Empty;

    /// <summary>The tab order, as text, because <c>Describe</c> answers in text.</summary>
    public string TabSequence { get; set; } = "0";

    /// <summary>
    /// The edit style - <c>"edit"</c>, <c>"editmask"</c>, <c>"ddlb"</c>, <c>"dddw"</c>,
    /// <c>"checkbox"</c> or <c>"radiobuttons"</c>.
    /// </summary>
    /// <remarks>
    /// Read at <c>n_cst_dwsvc.sru:L638</c>, where the value <c>"checkbox"</c> selects the checkbox branch
    /// of the column value map and anything else falls through to the code-table branch.
    /// </remarks>
    public string EditStyle { get; set; } = "edit";

    /// <summary>
    /// Whether the column is display-only, as <c>Describe(name + ".Edit.DisplayOnly")</c> answers -
    /// <c>"yes"</c> or <c>"no"</c>.
    /// </summary>
    public string EditDisplayOnly { get; set; } = "no";

    /// <summary>
    /// The edit mask, or <see langword="null"/> when the column has none, in which case
    /// <c>Describe(name + ".editmask.mask")</c> answers <c>"!"</c>.
    /// </summary>
    /// <remarks>
    /// The primary fixture's <c>birth</c> column declares <c>editmask.mask="yyyy-mm-dd"</c>
    /// [<c>dw_sqlite.srd:L26</c>], and the service fixtures' numeric columns declare
    /// <c>editmask.mask="###,##0.00"</c>.
    /// </remarks>
    public string? EditMaskMask { get; set; }

    /// <summary>
    /// The validation message, exactly as <c>Describe(name + ".ValidationMsg")</c> would answer it -
    /// INCLUDING its surrounding quotes when it has them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FOUR SHAPES MUST BE REPRESENTABLE, because between them they drive every branch of the validation
    /// message path at <c>se_cst_dw.sru:L350-L358</c>:
    /// </para>
    /// <para>
    /// 1. A NORMAL QUOTED MESSAGE, longer than two characters, which the oracle then strips the outer two
    /// characters from with <c>Mid(sErrMsg,2,Len(sErrMsg) - 2)</c> [<c>:L352</c>]. The stripping belongs
    /// to <c>Domain/ValidationSession.cs</c>, so this property must hold the QUOTED text and must not
    /// helpfully unquote it.
    /// </para>
    /// <para>
    /// 2. A VALUE OF TWO CHARACTERS OR FEWER, which fails the <c>Len(sErrMsg) &gt; 2</c> guard at
    /// <c>:L351</c> and therefore passes through UNSTRIPPED. Both sides of that guard are reachable only
    /// if a suite can set both shapes.
    /// </para>
    /// <para>
    /// 3. THE EMPTY STRING, which is this property's default and which reaches the fallback at
    /// <c>:L354</c> where a localized message is substituted.
    /// </para>
    /// <para>
    /// 4. THE SINGLE CHARACTER <c>"?"</c>, which reaches the SAME fallback through the second half of
    /// that <c>or</c>. It is a distinct shape rather than a duplicate of the empty string, because it
    /// arrives by a different route: <c>"?"</c> is the DataWindow's own "value cannot be determined"
    /// sentinel.
    /// </para>
    /// </remarks>
    public string ValidationMsg { get; set; } = string.Empty;

    /// <summary>
    /// The expression a computed field evaluates, or <see langword="null"/> for an object that is not a
    /// computed field.
    /// </summary>
    /// <remarks>
    /// The primary fixture's footer computed field is <c>compute_1</c> with the expression
    /// <c>sum(salary for page)</c> [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L27</c>]. That exact
    /// pair is what the expression-evaluator suite needs, and it is a page-scoped aggregate rather than a
    /// plain one - which is precisely the form AAP 0.6.5 records as part of the evaluator's net-new
    /// obligation.
    /// </remarks>
    public string? Expression { get; set; }

    /// <summary>
    /// The value stored when a checkbox column is ticked, or <see langword="null"/> when the column is
    /// not a checkbox.
    /// </summary>
    /// <remarks>
    /// Read at <c>n_cst_dwsvc.sru:L640</c>. The context-menu fixture's <c>n3</c> column declares
    /// <c>checkbox.on="1"</c> and <c>checkbox.off="0"</c>.
    /// </remarks>
    public string? CheckBoxOn { get; set; }

    /// <summary>
    /// The value stored when a checkbox column is cleared, or <see langword="null"/> when the column is
    /// not a checkbox. Read at <c>n_cst_dwsvc.sru:L645</c>.
    /// </summary>
    public string? CheckBoxOff { get; set; }

    /// <summary>
    /// The name of the child DataWindow behind a drop-down DataWindow column, or <see langword="null"/>
    /// when the column is not one - in which case <c>Describe(name + ".dddw.name")</c> answers
    /// <c>"!"</c>, which is the test at <c>n_cst_dwsvc.sru:L587</c>.
    /// </summary>
    public string? DropDownDataWindowName { get; set; }

    /// <summary>
    /// The child column whose value is DISPLAYED, read at <c>n_cst_dwsvc.sru:L592</c>. In the service
    /// fixture this is <c>dsp</c> [<c>dw_test_dwsvc.srd:L12-L13</c>].
    /// </summary>
    public string? DropDownDisplayColumn { get; set; }

    /// <summary>
    /// The child column whose value is STORED, read at <c>n_cst_dwsvc.sru:L593</c>. In the service
    /// fixture this is <c>dat</c>.
    /// </summary>
    public string? DropDownDataColumn { get; set; }

    /// <summary>
    /// Whether a drop-down DataWindow column allows free text - <c>"yes"</c> or <c>"no"</c>.
    /// </summary>
    /// <remarks>
    /// Read only by the COMMENTED-OUT drop-down edit checks at <c>se_cst_dw.sru:L217-L218</c> and
    /// <c>:L373-L374</c>. Those checks are carried across as commented and inert and are NOT revived
    /// (constraint C-B); the property is modelled so a suite can prove the fixture reports what the
    /// oracle's fixture reports - <c>dddw.allowedit=no</c> in the service fixture, later overridden to
    /// <c>yes</c> at run time by the drop-down search oracle
    /// [<c>ws_objects/pfw.tests.pbl.src/w_test_dwsvc_dropdownsearch.srw:L46</c>].
    /// </remarks>
    public string DropDownAllowEdit { get; set; } = "no";

    /// <summary>
    /// Whether a drop-down LIST BOX column allows free text - <c>"yes"</c> or <c>"no"</c>. Distinct from
    /// <see cref="DropDownAllowEdit"/> because the oracle reads the two through different property
    /// expressions, <c>.DDLB.AllowEdit</c> and <c>.DDDW.AllowEdit</c>.
    /// </summary>
    public string DropDownListAllowEdit { get; set; } = "no";

    /// <summary>
    /// The column's code table, in declaration order.
    /// </summary>
    public IReadOnlyList<FakeCodeTableEntry> CodeTable => _codeTable;

    /// <summary>
    /// Appends a code-table entry.
    /// </summary>
    /// <param name="display">The display text.</param>
    /// <param name="value">The stored value.</param>
    /// <returns>This definition, so declarations can be chained.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public FakeDataWindowObjectDefinition AddCodeTableEntry(string display, string value)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(value);

        _codeTable.Add(new FakeCodeTableEntry(display, value));
        return this;
    }

    /// <summary>
    /// Properties taught to this object beyond the enumerated set.
    /// </summary>
    public IReadOnlyDictionary<string, string> ExtraProperties => _extraProperties;

    /// <summary>
    /// Teaches this object one extra property, or replaces it when already present.
    /// </summary>
    /// <param name="property">
    /// The property name WITHOUT the object-name prefix - for example <c>"Height"</c> for the expression
    /// <c>"birth.Height"</c>. Matched case-insensitively, as DataWindow property expressions are.
    /// </param>
    /// <param name="value">The answer <c>Describe</c> should give.</param>
    /// <returns>This definition, so declarations can be chained.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public FakeDataWindowObjectDefinition SetProperty(string property, string value)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);

        _extraProperties[property] = value;
        return this;
    }

    /// <summary>
    /// Resolves one property of this object for <c>Describe</c>.
    /// </summary>
    /// <param name="property">
    /// The property name with the object-name prefix already removed, matched case-insensitively.
    /// </param>
    /// <param name="value">
    /// Receives the answer when the property is known, including when the answer is a sentinel because
    /// the property does not apply to this kind of object.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when this object recognises the property; <see langword="false"/> when it
    /// does not, leaving the caller to answer with the invalid-expression sentinel.
    /// </returns>
    /// <remarks>
    /// The switch lives here rather than on the host so that per-object knowledge stays with the object,
    /// and so a suite that wants to check one column's answers can do so without a host at all. Every
    /// label is one of the measured property expressions listed on this type.
    /// </remarks>
    public bool TryDescribe(string property, out string value)
    {
        ArgumentNullException.ThrowIfNull(property);

        bool isColumn = string.Equals(Type, ColumnType, StringComparison.OrdinalIgnoreCase);
        bool isDropDownDataWindow = DropDownDataWindowName is not null;

        switch (property.ToLowerInvariant())
        {
            case "name":
                value = Name;
                return true;

            case "type":
                value = Type;
                return true;

            case "band":
                value = Band;
                return true;

            case "id":
                value = Id.ToString(CultureInfo.InvariantCulture);
                return true;

            case "coltype":
                // A text object and a computed field have no database type at all, so the property does
                // not apply and the DataWindow answers with the invalid-expression sentinel rather than
                // with an empty type that would fall through the coercion table's default arm.
                value = isColumn ? ColType : FakeDataWindowHost.InvalidExpressionSentinel;
                return true;

            case "dbname":
                value = isColumn ? DbName : FakeDataWindowHost.InvalidExpressionSentinel;
                return true;

            case "format":
                value = Format;
                return true;

            case "tabsequence":
                value = TabSequence;
                return true;

            case "validationmsg":
                value = ValidationMsg;
                return true;

            case "expression":
                value = Expression ?? FakeDataWindowHost.InvalidExpressionSentinel;
                return true;

            case "edit.style":
                value = EditStyle;
                return true;

            case "edit.displayonly":
                value = EditDisplayOnly;
                return true;

            case "editmask.mask":
                value = EditMaskMask ?? FakeDataWindowHost.InvalidExpressionSentinel;
                return true;

            case "checkbox.on":
                value = CheckBoxOn ?? FakeDataWindowHost.InvalidExpressionSentinel;
                return true;

            case "checkbox.off":
                value = CheckBoxOff ?? FakeDataWindowHost.InvalidExpressionSentinel;
                return true;

            case "dddw.name":
                value = DropDownDataWindowName ?? FakeDataWindowHost.InvalidExpressionSentinel;
                return true;

            case "dddw.displaycolumn":
                value = DropDownDisplayColumn ?? FakeDataWindowHost.InvalidExpressionSentinel;
                return true;

            case "dddw.datacolumn":
                value = DropDownDataColumn ?? FakeDataWindowHost.InvalidExpressionSentinel;
                return true;

            case "dddw.allowedit":
                value = isDropDownDataWindow
                    ? DropDownAllowEdit
                    : FakeDataWindowHost.InvalidExpressionSentinel;
                return true;

            case "ddlb.allowedit":
                value = string.Equals(EditStyle, "ddlb", StringComparison.OrdinalIgnoreCase)
                    ? DropDownListAllowEdit
                    : FakeDataWindowHost.InvalidExpressionSentinel;
                return true;

            case "update":
                value = YesOrNo(isColumn && Update);
                return true;

            case "key":
                value = YesOrNo(isColumn && Key);
                return true;

            case "identity":
                value = YesOrNo(isColumn && Identity);
                return true;

            case "updatewhereclause":
                value = YesOrNo(isColumn && UpdateWhereClause);
                return true;

            default:
                // Anything not enumerated falls back to whatever a suite taught this object, and
                // otherwise reports "not recognised" so the caller answers with the invalid-expression
                // sentinel. `value` is still assigned on the false path, because the signature promises a
                // non-null out parameter on every exit and the empty string is the inert choice.
                if (_extraProperties.TryGetValue(property, out string? taught))
                {
                    value = taught;
                    return true;
                }

                value = string.Empty;
                return false;
        }
    }

    /// <summary>
    /// Renders a flag the way a DataWindow renders one: <c>"yes"</c> or <c>"no"</c>, never
    /// <c>"true"</c> or <c>"1"</c>.
    /// </summary>
    private static string YesOrNo(bool flag) => flag ? "yes" : "no";
}


/// <summary>
/// The fake's implementation of the indexed <c>dwo.Primary[row]</c> view, with a SETTER the contract's
/// interface does not declare.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE SETTER EXISTS, WHICH IS THE MOST IMPORTANT SENTENCE IN THIS TYPE.
/// <see cref="IDataWindowValueBuffer"/> declares a GET-ONLY indexer because all seven measured legacy
/// uses are reads. But the equality test at
/// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L198-L202</c> compares a snapshot taken
/// BEFORE the item-change handler ran against <c>dwo.Primary[row]</c> read AFTER it ran, and that
/// comparison only means anything if a handler can mutate the buffer mid-flight. A suite therefore has to
/// be able to write to this view from inside a scripted handler. Implementing the get-only interface
/// member with a get/set indexer is exactly how that is staged: the code under test sees the read-only
/// contract, and the suite sees the setter.
/// </para>
/// <para>
/// TWO MODES, AND THE DIFFERENCE IS DELIBERATE.
/// </para>
/// <para>
/// HOST-BACKED - created by a <see cref="FakeDataWindowHost"/> for one column of one buffer. Reads and
/// writes go straight through to the host's row store, so a write made by the code under test through
/// <c>SetItem</c> is immediately visible here and vice versa. That shared identity is required: a
/// separate copy would let the two views disagree, and the whole point of the equality test above is that
/// they cannot.
/// </para>
/// <para>
/// STANDALONE - created directly by a suite that needs a <see cref="FakeDataWindowObject"/> without a
/// host at all, for instance to hand one column type to the item-change protocol. It keeps its own
/// values, has no notion of how many rows exist, and therefore never reports an out-of-range row.
/// </para>
/// <para>
/// NULL IS A VALUE HERE, NOT AN ABSENCE. A row that has never been written and a row explicitly set to
/// null both read as <see langword="null"/>, exactly as an unassigned DataWindow item does, and null is
/// never substituted with a default (AAP 0.4.5.4).
/// </para>
/// </remarks>
public sealed class FakeDataWindowValueBuffer : IDataWindowValueBuffer
{
    private readonly FakeDataWindowHost? _host;
    private readonly Dictionary<long, object?>? _standaloneValues;

    /// <summary>
    /// Creates a STANDALONE buffer that keeps its own values and is attached to no host.
    /// </summary>
    /// <param name="buffer">
    /// The buffer this view reports itself as, which a suite may still want to be accurate even though
    /// nothing dispatches on it in standalone mode.
    /// </param>
    /// <param name="columnId">The column number this view reports itself as.</param>
    public FakeDataWindowValueBuffer(DwBuffer buffer = DwBuffer.Primary, long columnId = 0L)
    {
        Buffer = buffer;
        ColumnId = columnId;
        _standaloneValues = [];
    }

    /// <summary>
    /// Creates a HOST-BACKED buffer that reads and writes one column of one of the host's buffers.
    /// </summary>
    /// <param name="host">The host whose row store backs this view.</param>
    /// <param name="buffer">The buffer this view addresses.</param>
    /// <param name="columnId">The column number this view addresses.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public FakeDataWindowValueBuffer(FakeDataWindowHost host, DwBuffer buffer, long columnId)
    {
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
        Buffer = buffer;
        ColumnId = columnId;
    }

    /// <summary>
    /// The buffer this view addresses.
    /// </summary>
    /// <remarks>
    /// Always <see cref="DwBuffer.Primary"/> for a view reached through
    /// <see cref="IDataWindowObject.Primary"/>. <see cref="DwBuffer.Delete"/> and
    /// <see cref="DwBuffer.Filter"/> appear in NEITHER ported source - they belong to Persistence's
    /// buffer codecs - so no member of the contract reaches them, and this property carries the whole
    /// published enum only because the enum is published whole.
    /// </remarks>
    public DwBuffer Buffer { get; }

    /// <summary>
    /// The column number this view addresses.
    /// </summary>
    public long ColumnId { get; }

    /// <summary>
    /// Whether this view is backed by a host rather than by its own values.
    /// </summary>
    public bool IsHostBacked => _host is not null;

    /// <summary>
    /// The value held for <paramref name="row"/>, or <see langword="null"/> when the item is null or has
    /// never been written.
    /// </summary>
    /// <param name="row">The ONE-BASED DataWindow row number.</param>
    /// <returns>The value, which may be <see langword="null"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// On a WRITE to a host-backed view when <paramref name="row"/> names a row that does not exist. A
    /// read of a non-existent row answers <see langword="null"/> instead of raising, because the oracle
    /// reads <c>dwo.Primary[row]</c> at <c>se_cst_dw.sru:L334</c> before any bounds guard and a raise
    /// there would turn a normal path into a fault. A WRITE, by contrast, is only ever test setup or a
    /// scripted mid-flight mutation, and silently discarding one would hide a mistake in the suite.
    /// </exception>
    public object? this[long row]
    {
        get
        {
            if (_host is not null)
            {
                return _host.GetBufferValue(Buffer, row, ColumnId);
            }

            return _standaloneValues!.TryGetValue(row, out object? value) ? value : null;
        }

        set
        {
            if (_host is not null)
            {
                _host.SetBufferValue(Buffer, row, ColumnId, value);
                return;
            }

            _standaloneValues![row] = value;
        }
    }
}

/// <summary>
/// The fake <c>dwobject</c> handle - the column, computed field or text object a DataWindow event names
/// as the target of the interaction.
/// </summary>
/// <remarks>
/// <para>
/// EXACTLY THE CONTRACT'S FOUR MEMBERS, PLUS SETTERS AND THE CONCRETE BUFFER.
/// <see cref="IDataWindowObject"/> declares <c>ID</c>, <c>Name</c>, <c>ColType</c> and <c>Primary</c> and
/// nothing else; this type adds no fifth member of its own beyond the definition back-reference. Adding
/// one would fabricate surface the oracle does not have, and would give every suite one more thing to
/// arrange for no behavioural gain.
/// </para>
/// <para>
/// <c>ID</c> IS <c>object?</c> AND IS DELIBERATELY NOT PRE-CONVERTED. All twelve legacy uses wrap it as
/// <c>Long(dwo.ID)</c>, so the conversion is performed at the point of use through
/// <see cref="DataWindowObjectExtensions.ColumnId(IDataWindowObject)"/>. Leaving it untyped here is what
/// lets a suite drive that conversion's own arms - a null identifier, an identifier that arrives as text,
/// and text that does not parse - which is a real class of behaviour rather than a hypothetical one.
/// </para>
/// <para>
/// <c>Primary</c> IS EXPOSED TWICE ON PURPOSE. The strongly-typed property returns the concrete
/// <see cref="FakeDataWindowValueBuffer"/> so a suite can write to it; the explicit interface
/// implementation hands the code under test the same instance through the read-only contract. Two
/// spellings, one object - so a suite's write and the subject's read cannot diverge.
/// </para>
/// </remarks>
public sealed class FakeDataWindowObject : IDataWindowObject
{
    /// <summary>
    /// Creates a STANDALONE handle with its own value buffer, attached to no host.
    /// </summary>
    /// <param name="name">The object name.</param>
    /// <param name="colType">
    /// The raw column type, for example <see cref="FakeColumnType.Number"/> or
    /// <c>FakeColumnType.CharOf(100)</c>. The empty string models an object with no column type, which
    /// truncates to the empty string and falls through every arm of the coercion table to its default -
    /// the same outcome the legacy reaches for any unrecognised type.
    /// </param>
    /// <param name="id">
    /// The identifier, untyped. Defaults to <see langword="null"/> so a suite that has not stated one
    /// gets the null-propagating arm of the identifier conversion rather than a silently invented column
    /// zero.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> or <paramref name="colType"/> is <see langword="null"/>. Both are
    /// non-nullable on the contract, because the oracle concatenates the name with no guard
    /// [<c>se_cst_dw.sru:L350</c>] and applies <c>Left</c> to the column type with no guard
    /// [<c>:L231</c>].
    /// </exception>
    public FakeDataWindowObject(string name, string colType, object? id = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(colType);

        Name = name;
        ColType = colType;
        ID = id;
        Primary = new FakeDataWindowValueBuffer();
    }

    /// <summary>
    /// Creates a HOST-BACKED handle for one declared object of a host.
    /// </summary>
    /// <param name="host">The host that owns the row store.</param>
    /// <param name="definition">The object declaration this handle stands for.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public FakeDataWindowObject(FakeDataWindowHost host, FakeDataWindowObjectDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(definition);

        Definition = definition;
        Name = definition.Name;
        ColType = definition.ColType;

        // Boxed as a long, which is the most common shape a real DataWindow reports, while remaining an
        // `any` on the contract so a suite can replace it with text or with null.
        ID = definition.Id;
        Primary = new FakeDataWindowValueBuffer(host, DwBuffer.Primary, definition.Id);
    }

    /// <summary>
    /// The object identifier, in the untyped form the legacy exposes it in.
    /// </summary>
    public object? ID { get; set; }

    /// <summary>
    /// The object name, as used to build a property expression such as
    /// <c>dwo.Name + ".ValidationMsg"</c>.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The raw column type, kept as text so the five-character prefix match keeps its input.
    /// </summary>
    public string ColType { get; set; }

    /// <summary>
    /// The primary-buffer view, as the concrete type so a suite can write to it.
    /// </summary>
    public FakeDataWindowValueBuffer Primary { get; }

    /// <summary>
    /// The declaration this handle stands for, or <see langword="null"/> for a standalone handle.
    /// </summary>
    /// <remarks>
    /// Exposed so a suite that already holds the handle can reach the column's <c>Describe</c>-visible
    /// properties without going back to the host. It is not part of the contract and the code under test
    /// never sees it.
    /// </remarks>
    public FakeDataWindowObjectDefinition? Definition { get; }

    /// <summary>
    /// The contract's read-only view of <see cref="Primary"/> - the same instance, narrowed.
    /// </summary>
    IDataWindowValueBuffer IDataWindowObject.Primary => Primary;

    /// <inheritdoc/>
    public override string ToString() => "dwo:" + Name;
}


/// <summary>
/// The permissive value conversions the fake buffers use, reproducing PowerBuilder's
/// never-raise conversion posture.
/// </summary>
/// <remarks>
/// <para>
/// EVERY CONVERSION RETURNS <see langword="null"/> RATHER THAN RAISING, and none of them uses a
/// <c>try</c>/<c>catch</c> to get there - each dispatches on the runtime type and falls back to a
/// <c>TryParse</c>. PowerScript's conversions do not throw, so neither do these (constraint C-B), and a
/// swallowed exception would be a place where a real defect could hide.
/// </para>
/// <para>
/// A NULL INPUT ALWAYS YIELDS A NULL OUTPUT. That is contract rather than convenience: the oracle skips a
/// row on a null result [<c>n_cst_dwsvc.sru:L617</c>, <c>:L633</c>], so a converted null must stay a null
/// and must never become a zero or an empty string (AAP 0.4.5.4).
/// </para>
/// <para>
/// Every text conversion is invariant-culture. A DataWindow value map's keys are structural data, and a
/// culture with a comma decimal separator would otherwise make a recorded comparison
/// machine-dependent - a substitution the legacy cannot express, and unobservable for the values these
/// fixtures carry.
/// </para>
/// </remarks>
public static class FakeItemValue
{
    /// <summary>
    /// Converts a stored value to text, as <c>GetItemString</c> would report it.
    /// </summary>
    /// <param name="value">The stored value.</param>
    /// <returns>The text, or <see langword="null"/> when <paramref name="value"/> is null.</returns>
    public static string? AsString(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            DateTime moment => moment.ToString("O", CultureInfo.InvariantCulture),
            DateOnly day => day.ToString("O", CultureInfo.InvariantCulture),
            TimeOnly timeOfDay => timeOfDay.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    /// <summary>
    /// Converts a stored value to a base-ten decimal, as <c>GetItemDecimal</c> would report it.
    /// </summary>
    /// <param name="value">The stored value.</param>
    /// <returns>
    /// The value, or <see langword="null"/> when it is null or cannot be read as a number.
    /// </returns>
    public static decimal? AsDecimal(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case decimal already:
                return already;
            case long integral:
                return integral;
            case int integral:
                return integral;
            case short integral:
                return integral;
            case byte integral:
                return integral;
            case sbyte integral:
                return integral;
            case uint integral:
                return integral;
            case ushort integral:
                return integral;
            case ulong integral:
                return integral;
            case double floating:
                return FromBinaryFloatingPoint(floating);
            case float floating:
                return FromBinaryFloatingPoint(floating);
            case string text:
                return decimal.TryParse(
                    text,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out decimal parsed)
                    ? parsed
                    : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Narrows a binary floating-point value into <see cref="decimal"/> WITHOUT EVER RAISING.
    /// </summary>
    /// <param name="value">The value to narrow.</param>
    /// <returns>
    /// The narrowed value, or <see langword="null"/> when it is not finite or lies outside
    /// <see cref="decimal"/>'s range.
    /// </returns>
    /// <remarks>
    /// THE RANGE TEST IS NOT DEFENSIVE PADDING, IT IS THE ONLY THING KEEPING THIS TYPE'S PROMISE. A plain
    /// <c>(decimal)</c> cast RAISES <see cref="OverflowException"/> for a magnitude a <see cref="double"/>
    /// or a <see cref="float"/> can hold and a <see cref="decimal"/> cannot - <c>1e30</c> and
    /// <c>3.4e38f</c> are both finite and both outside range - so a finiteness check alone is
    /// insufficient. Every conversion in this type is documented as never raising, and PowerScript's own
    /// conversions do not raise either (constraint C-B), so an out-of-range magnitude answers
    /// <see langword="null"/> exactly as unparseable text does.
    /// </remarks>
    private static decimal? FromBinaryFloatingPoint(double value)
    {
        if (!double.IsFinite(value))
        {
            return null;
        }

        if (value > (double)decimal.MaxValue || value < (double)decimal.MinValue)
        {
            return null;
        }

        return (decimal)value;
    }

    /// <summary>
    /// Converts a stored value to a double, as <c>GetItemNumber</c> would report it.
    /// </summary>
    /// <param name="value">The stored value.</param>
    /// <returns>
    /// The value, or <see langword="null"/> when it is null or cannot be read as a number.
    /// </returns>
    /// <remarks>
    /// A <see cref="double"/> and NOT a <see cref="decimal"/>, because PowerBuilder's
    /// <c>GetItemNumber</c> returns a double and the oracle sends decimal columns down the separate
    /// <c>GetItemDecimal</c> path [<c>n_cst_dwsvc.sru:L607</c> versus <c>:L609</c>]. Collapsing the two
    /// would merge two distinct legacy conversions and change the text a value map's keys are built from.
    /// </remarks>
    public static double? AsDouble(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case double already:
                return already;
            case float floating:
                return floating;
            case decimal exact:
                return (double)exact;
            case long integral:
                return integral;
            case int integral:
                return integral;
            case short integral:
                return integral;
            case byte integral:
                return integral;
            case sbyte integral:
                return integral;
            case uint integral:
                return integral;
            case ushort integral:
                return integral;
            case ulong integral:
                return integral;
            case string text:
                return double.TryParse(
                    text,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out double parsed)
                    ? parsed
                    : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Converts a stored value to a date and time, as <c>GetItemDateTime</c> would report it.
    /// </summary>
    /// <param name="value">The stored value.</param>
    /// <returns>The value, or <see langword="null"/> when it is null or unreadable.</returns>
    /// <remarks>
    /// A date-only value converts to midnight of that day, which is what a DataWindow reports when a
    /// <c>date</c> column is read through the date-and-time accessor.
    /// </remarks>
    public static DateTime? AsDateTime(object? value)
    {
        return value switch
        {
            null => null,
            DateTime already => already,
            DateOnly day => day.ToDateTime(TimeOnly.MinValue),
            string text => DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed)
                ? parsed
                : null,
            _ => null,
        };
    }

    /// <summary>
    /// Converts a stored value to a date, as <c>GetItemDate</c> would report it.
    /// </summary>
    /// <param name="value">The stored value.</param>
    /// <returns>The value, or <see langword="null"/> when it is null or unreadable.</returns>
    /// <remarks>
    /// A date-and-time value loses its time of day, which is what a DataWindow does when a
    /// <c>datetime</c> column is read through the date accessor. The mapping is deliberately one-way
    /// lossy rather than rejected, because the legacy accessor is equally lossy.
    /// </remarks>
    public static DateOnly? AsDateOnly(object? value)
    {
        return value switch
        {
            null => null,
            DateOnly already => already,
            DateTime moment => DateOnly.FromDateTime(moment),
            string text => DateOnly.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly parsed)
                ? parsed
                : null,
            _ => null,
        };
    }

    /// <summary>
    /// Converts a stored value to a time of day, as <c>GetItemTime</c> would report it.
    /// </summary>
    /// <param name="value">The stored value.</param>
    /// <returns>The value, or <see langword="null"/> when it is null or unreadable.</returns>
    public static TimeOnly? AsTimeOnly(object? value)
    {
        return value switch
        {
            null => null,
            TimeOnly already => already,
            DateTime moment => TimeOnly.FromDateTime(moment),
            string text => TimeOnly.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly parsed)
                ? parsed
                : null,
            _ => null,
        };
    }
}

/// <summary>
/// The fake <c>datawindowchild</c> - the drop-down DataWindow behind a DDDW column, as obtained from
/// <see cref="FakeDataWindowHost.GetChild(string, ref IDataWindowChild?)"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is where the SIX TYPED GETTERS LIVE, and it is the contract that put them here rather than a
/// preference: all twelve measured call sites are on <c>dwc</c> inside
/// <c>_of_getcolumnvaluemap</c> [<c>n_cst_dwsvc.sru:L605-L615</c>, <c>:L621-L631</c>], and there is not
/// one <c>#DataWindow.GetItem*</c> call anywhere in either ported source. See DIVERGENCE 1 in this file's
/// header.
/// </para>
/// <para>
/// COLUMNS ARE ADDRESSED BY NAME, not by number, because that is how the oracle addresses them: the
/// display and data column names come out of <c>Describe</c> answers
/// [<c>n_cst_dwsvc.sru:L592-L593</c>] and are passed straight through to the getters. The host's item
/// accessors take a numeric column id instead, and that asymmetry is the legacy's, preserved rather than
/// harmonised.
/// </para>
/// <para>
/// NO CALL LOG. This type is a pure value source: it records nothing, because nothing in the ported code
/// mutates a child DataWindow. Adding a log here would suggest an ordering question that does not exist.
/// </para>
/// </remarks>
public sealed class FakeDataWindowChild : IDataWindowChild
{
    private readonly List<FakeDataWindowObjectDefinition> _columns = [];
    private readonly List<Dictionary<string, object?>> _rows = [];
    private readonly Dictionary<string, string> _describeTable =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates an empty child DataWindow.
    /// </summary>
    /// <param name="name">
    /// The child's data-object name, as a DDDW column's <c>dddw.name</c> would report it - for example
    /// <c>"dw_test_dwsvc_dddw"</c>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public FakeDataWindowChild(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
    }

    /// <summary>The child's data-object name.</summary>
    public string Name { get; }

    /// <summary>The child's columns, in declaration order.</summary>
    public IReadOnlyList<FakeDataWindowObjectDefinition> Columns => _columns;

    /// <summary>
    /// Declares a column on the child, giving it the next column number.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <param name="colType">The raw column type, for example <c>FakeColumnType.CharOf(100)</c>.</param>
    /// <returns>The declaration, so a suite can adjust it further.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public FakeDataWindowObjectDefinition AddColumn(string name, string colType)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(colType);

        FakeDataWindowObjectDefinition column = new(
            name,
            FakeDataWindowObjectDefinition.ColumnType,
            band: "detail")
        {
            Id = _columns.Count + 1,
            ColType = colType,
            DbName = name,
        };

        _columns.Add(column);
        return column;
    }

    /// <summary>
    /// Appends a row, taking values positionally in declared column order.
    /// </summary>
    /// <param name="values">
    /// The values, one per column from the first onward. Fewer values than columns leaves the remaining
    /// columns null; more values than columns is a setup fault and raises.
    /// </param>
    /// <returns>The ONE-BASED row number of the new row.</returns>
    /// <exception cref="ArgumentException">
    /// More values were supplied than the child has columns.
    /// </exception>
    /// <remarks>
    /// TEST SETUP, SO IT IS NOT RECORDED ANYWHERE - see the call-log policy in this file's header. It
    /// returns the one-based row number rather than a count so a suite can address the row it just added
    /// without an off-by-one of its own.
    /// </remarks>
    public long AddRow(params object?[]? values)
    {
        object?[] supplied = values ?? [];
        if (supplied.Length > _columns.Count)
        {
            throw new ArgumentException(
                "The child DataWindow '"
                    + Name
                    + "' declares "
                    + _columns.Count.ToString(CultureInfo.InvariantCulture)
                    + " column(s) but "
                    + supplied.Length.ToString(CultureInfo.InvariantCulture)
                    + " value(s) were supplied.",
                nameof(values));
        }

        Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < supplied.Length; index++)
        {
            row[_columns[index].Name] = supplied[index];
        }

        _rows.Add(row);
        return OneBasedRows.ToRowNumber(_rows.Count - 1);
    }

    /// <summary>
    /// Writes one item directly, without going through a contract member.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <param name="value">The value, which may be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="column"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="row"/> does not exist.</exception>
    /// <remarks>Test setup, so it is not recorded.</remarks>
    public void SetItem(long row, string column, object? value)
    {
        ArgumentNullException.ThrowIfNull(column);

        RowAt(row)[column] = value;
    }

    /// <summary>
    /// Teaches the child one <c>Describe</c> answer, or replaces it when already present.
    /// </summary>
    /// <param name="property">The full property expression, matched case-insensitively.</param>
    /// <param name="value">The answer.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Consulted BEFORE the computed answers, so a suite can override any of them - including forcing a
    /// column's <c>.coltype</c> to one of the sentinels, which is how the early-return arms at
    /// <c>n_cst_dwsvc.sru:L598-L599</c> are reached.
    /// </remarks>
    public void SetDescribe(string property, string value)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);

        _describeTable[property] = value;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Answers the taught table first, then the per-column property switch, then
    /// <see cref="FakeDataWindowHost.InvalidExpressionSentinel"/>. It never raises: the oracle branches on
    /// the sentinels [<c>n_cst_dwsvc.sru:L598-L599</c>] and an exception would replace a branch with a
    /// fault.
    /// </remarks>
    public string Describe(string property)
    {
        ArgumentNullException.ThrowIfNull(property);

        if (_describeTable.TryGetValue(property, out string? taught))
        {
            return taught;
        }

        int separator = property.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 || separator >= property.Length - 1)
        {
            return FakeDataWindowHost.InvalidExpressionSentinel;
        }

        string objectName = property.Substring(0, separator);
        string objectProperty = property.Substring(separator + 1);

        foreach (FakeDataWindowObjectDefinition column in _columns)
        {
            if (string.Equals(column.Name, objectName, StringComparison.OrdinalIgnoreCase)
                && column.TryDescribe(objectProperty, out string value))
            {
                return value;
            }
        }

        return FakeDataWindowHost.InvalidExpressionSentinel;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The LAST VALID ROW NUMBER, not a zero-based length, because the oracle's loop at
    /// <c>n_cst_dwsvc.sru:L601</c> iterates <c>1</c> to this value INCLUSIVE.
    /// </remarks>
    public long RowCount() => _rows.Count;

    /// <inheritdoc/>
    public string? GetItemString(long row, string column) =>
        FakeItemValue.AsString(PeekItem(row, column));

    /// <inheritdoc/>
    public decimal? GetItemDecimal(long row, string column) =>
        FakeItemValue.AsDecimal(PeekItem(row, column));

    /// <inheritdoc/>
    public double? GetItemNumber(long row, string column) =>
        FakeItemValue.AsDouble(PeekItem(row, column));

    /// <inheritdoc/>
    public DateTime? GetItemDateTime(long row, string column) =>
        FakeItemValue.AsDateTime(PeekItem(row, column));

    /// <inheritdoc/>
    public DateOnly? GetItemDate(long row, string column) =>
        FakeItemValue.AsDateOnly(PeekItem(row, column));

    /// <inheritdoc/>
    public TimeOnly? GetItemTime(long row, string column) =>
        FakeItemValue.AsTimeOnly(PeekItem(row, column));

    /// <summary>
    /// Reads one item as it is stored, without conversion.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <returns>
    /// The stored value, or <see langword="null"/> when the item is null, the column is unknown, or the
    /// row does not exist.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="column"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A read of a row that does not exist answers <see langword="null"/> rather than raising, matching
    /// the read side of <see cref="FakeDataWindowValueBuffer"/> and for the same reason: the ported code
    /// reads before it bounds-checks.
    /// </remarks>
    public object? PeekItem(long row, string column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (!OneBasedRows.IsInRange(row, _rows.Count))
        {
            return null;
        }

        return _rows[OneBasedRows.ToListIndex(row)].TryGetValue(column, out object? value)
            ? value
            : null;
    }

    /// <summary>
    /// The mutable row store for one existing row.
    /// </summary>
    private Dictionary<string, object?> RowAt(long row)
    {
        if (!OneBasedRows.IsInRange(row, _rows.Count))
        {
            throw new ArgumentOutOfRangeException(
                nameof(row),
                row,
                "The child DataWindow '"
                    + Name
                    + "' holds "
                    + _rows.Count.ToString(CultureInfo.InvariantCulture)
                    + " row(s), numbered from 1.");
        }

        return _rows[OneBasedRows.ToListIndex(row)];
    }
}


/// <summary>
/// The in-memory double for <see cref="DataWindowServiceHost"/>: every member the contract declares,
/// backed by dictionaries, with the eleven semantic events exposed as SETTABLE HANDLERS so a suite can
/// script exact return codes.
/// </summary>
/// <remarks>
/// <para>
/// NOT SEALED, DELIBERATELY. A suite with an unusual need - overriding <see cref="Describe(string)"/>
/// wholesale, say - can derive from this type rather than reimplementing the contract. The
/// <see cref="DescribeOverride"/> hook covers the common case without a subclass, so deriving should be
/// rare, but forbidding it would buy nothing.
/// </para>
/// <para>
/// EVERY OUTCOME IS SCRIPTABLE, AND NONE OF IT IS CLEVER. The result of each fallible member is a plain
/// settable property defaulting to the legacy success value, so a suite states the outcome it wants
/// instead of contriving a state that produces it. The one pairing that deserves attention is
/// <see cref="SetRowResult"/> together with <see cref="SetRowMovesCurrentRow"/>: the contract's DECISION 4
/// records that the oracle calls <c>SetRow(row)</c> and then RE-READS <c>GetRow()</c>, returning prevent
/// when the row still differs [<c>se_cst_dw.sru:L154-L157</c>], so a successful return is NOT evidence
/// the cursor moved. Those two properties are how that exact divergence is staged.
/// </para>
/// <para>
/// WHAT IT DOES NOT DO. It does not filter when <see cref="Filter"/> succeeds, does not sort, does not
/// evaluate an expression, does not validate a value against a column type and does not localize a
/// message. Every one of those belongs to the code under test, and a double that did any of them would be
/// asserting its own behaviour instead of the subject's (constraint C-B). The one exception is row
/// removal on a successful <c>DeleteRowCore</c>, which is switchable through
/// <see cref="DeleteRowCoreRemovesRow"/> and is modelled because the ported override reads
/// <c>RowCount()</c> AFTER calling the base implementation [<c>se_cst_dw.sru:L434</c>, <c>:L441</c>] and
/// would otherwise be untestable.
/// </para>
/// </remarks>
public class FakeDataWindowHost : DataWindowServiceHost
{
    /// <summary>
    /// The DataWindow's "that property expression is invalid" answer: <c>"!"</c>.
    /// </summary>
    /// <remarks>
    /// THE SENTINEL IS CONTRACT AND IS NOT NORMALISED. It is what an unknown <see cref="Describe(string)"/>
    /// key answers with, and ported callers test for it explicitly rather than for an exception or a null -
    /// <c>n_cst_dwsvc.sru:L587</c> uses it to decide whether a column is a drop-down DataWindow at all,
    /// <c>:L598-L599</c> uses it to abandon a value map, and <c>:L850-L851</c> uses it to detect whether
    /// group bands exist. Answering anything else, or raising, would break behaviour the oracle branches
    /// on.
    /// </remarks>
    public const string InvalidExpressionSentinel = "!";

    /// <summary>
    /// The DataWindow's "that value cannot be determined" answer: <c>"?"</c>.
    /// </summary>
    /// <remarks>
    /// A THIRD, DISTINCT OUTCOME - not a synonym for <see cref="InvalidExpressionSentinel"/> and not a
    /// synonym for the empty string. All three are tested separately at
    /// <c>n_cst_dwsvc.sru:L598-L599</c>, and <c>"?"</c> additionally appears as one of the four validation
    /// message shapes at <c>se_cst_dw.sru:L354</c>. Published so a suite can drive that arm by name rather
    /// than by a bare literal.
    /// </remarks>
    public const string UndeterminedValueSentinel = "?";

    /// <summary>
    /// The default object model, which exists only so that <c>Predicates.IsValidObject</c> answers true.
    /// </summary>
    /// <remarks>
    /// The ported predicate is <c>value is not null</c>, so ANY non-null reference is a valid object model
    /// and this one carries no behaviour. It is a single shared instance rather than one per host because
    /// nothing distinguishes two of them.
    /// </remarks>
    private static readonly object DefaultObjectModel = new();

    private readonly List<FakeDataWindowObjectDefinition> _objects = [];

    private readonly Dictionary<string, FakeDataWindowObject> _dwObjects =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<DwBuffer, List<FakeBufferRow>> _buffers;

    private readonly Dictionary<string, string> _describeTable =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, IDataWindowChild> _children =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates an empty host with no objects and no rows.
    /// </summary>
    /// <param name="eventful">
    /// The broker the host publishes as <see cref="Eventful"/>, or <see langword="null"/> to create one.
    /// A suite that needs to observe broker traffic passes its own; the rest let the host make one, which
    /// matches the legacy where <c>se_cst_dw</c> creates its own broker in its constructor
    /// [<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L562</c>].
    /// </param>
    public FakeDataWindowHost(EventBroker? eventful = null)
    {
        Eventful = eventful ?? new EventBroker();
        ObjectModelValue = DefaultObjectModel;

        // All three published buffers exist from the outset. `Delete!` and `Filter!` appear in NEITHER
        // ported source, so no contract member reaches them, but a suite exercising the delete path needs
        // somewhere for a removed row to go and Persistence's own codecs document Filter!'s inverted row
        // order - so the triple is modelled whole rather than pruned to the one the contract touches.
        _buffers = new Dictionary<DwBuffer, List<FakeBufferRow>>
        {
            [DwBuffer.Primary] = [],
            [DwBuffer.Delete] = [],
            [DwBuffer.Filter] = [],
        };
    }

    /// <summary>
    /// The ordered record of what the code under test did to this host.
    /// </summary>
    /// <remarks>
    /// Read-only to a suite in the sense that matters: a suite may clear it, and may read it, but nothing
    /// outside this file appends to it. The three rules deciding what reaches it are in this file's
    /// header.
    /// </remarks>
    public DataWindowCallLog CallLog { get; } = new DataWindowCallLog();

    /// <summary>
    /// Whether READS are recorded in <see cref="CallLog"/> alongside the mutations and event raises.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> by default. <see cref="Describe(string)"/> alone has 37 measured call sites
    /// and would swamp a sequence assertion, so reads stay out of the log unless a suite genuinely needs
    /// to prove read ordering - for instance that the item-status snapshot at <c>se_cst_dw.sru:L190</c>
    /// was taken BEFORE the restoring write at <c>:L220</c>.
    /// </remarks>
    public bool RecordsReads { get; set; }

    /// <inheritdoc/>
    public override EventBroker Eventful { get; }

    /// <summary>
    /// The value <see cref="ObjectModel"/> reports.
    /// </summary>
    /// <remarks>
    /// A SEPARATE SETTABLE PROPERTY BECAUSE C# CANNOT ADD A SETTER IN AN OVERRIDE. The contract declares
    /// <see cref="DataWindowServiceHost.ObjectModel"/> get-only, so the writable half lives here. Setting
    /// it to <see langword="null"/> is how a suite drives the "no valid object model" arm at
    /// <c>n_cst_dwsvc.sru:L119</c>, where <c>_of_getdwobject</c> returns an UNSET handle QUIETLY rather
    /// than raising - a different outcome from asking for a name that does not exist, which raises.
    /// </remarks>
    public object? ObjectModelValue { get; set; }

    /// <inheritdoc/>
    public override object? ObjectModel => ObjectModelValue;

    /// <summary>
    /// A hook consulted BEFORE the taught table and before every computed answer of
    /// <see cref="Describe(string)"/>.
    /// </summary>
    /// <remarks>
    /// Returning <see langword="null"/> means "I do not answer this one" and falls through to the normal
    /// resolution, so a hook can intercept selectively. This is also how an <c>Evaluate(...)</c>
    /// expression is answered: the oracle reaches the DataWindow expression evaluator THROUGH
    /// <c>Describe</c> - for example <c>Describe("Evaluate('LookUpDisplay(" + colName + ")', " + row +
    /// ")")</c> [<c>n_cst_dwsvc.sru:L289</c>] - so an evaluator suite scripts the answers here rather than
    /// through a separate member the contract does not have.
    /// </remarks>
    public Func<string, string?>? DescribeOverride { get; set; }

    /// <summary>
    /// The current row <see cref="GetRow"/> reports. <c>0</c> means there is no current row, which the
    /// oracle relies on when it redirects a delete of row <c>0</c> to the current row
    /// [<c>se_cst_dw.sru:L424-L426</c>].
    /// </summary>
    public long CurrentRow { get; set; }

    /// <summary>
    /// The presentation-style code <c>Describe("DataWindow.Processing")</c> answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DEFAULTS TO <c>"1"</c>, WHICH IS THE FIXTURE'S OWN VALUE AND NOT AN ARBITRARY CHOICE. The primary
    /// fixture declares <c>processing=1</c> [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L3</c>], and
    /// <c>1</c> is the GRID style - it agrees exactly with <c>DataWindowServiceBase.STYLE_GRID</c>, which
    /// is the same numbering read through a different door.
    /// </para>
    /// <para>
    /// It must be settable because <c>ondwnlbuttonclk</c> performs its focusless row move ONLY when this
    /// answers exactly <c>"1"</c> [<c>se_cst_dw.sru:L153</c>], so both sides of that branch are reachable
    /// only if a suite can change it.
    /// </para>
    /// </remarks>
    public string Processing { get; set; } = "1";

    /// <summary>
    /// The answer to <c>Describe("DataWindow.ReadOnly")</c> - <c>"yes"</c> or <c>"no"</c>, read at
    /// <c>n_cst_dwsvc.sru:L311</c> where the service tests it for equality with <c>"no"</c>.
    /// </summary>
    public string ReadOnly { get; set; } = "no";

    /// <summary>
    /// The answer to <c>Describe("DataWindow.Table.Select")</c> - the retrieval statement, from the
    /// fixture's <c>retrieve=</c> attribute.
    /// </summary>
    public string RetrieveStatement { get; set; } = string.Empty;

    /// <summary>
    /// The answer to <c>Describe("DataWindow.Table.UpdateTable")</c>, from the fixture's <c>update=</c>
    /// attribute.
    /// </summary>
    public string UpdateTable { get; set; } = string.Empty;

    /// <summary>
    /// The answer to <c>Describe("DataWindow.Table.UpdateWhere")</c>, from the fixture's
    /// <c>updatewhere=</c> attribute.
    /// </summary>
    /// <remarks>
    /// The primary fixture declares <c>updatewhere=1</c> [<c>dw_sqlite.srd:L14</c>], which is the
    /// "key and updateable columns" concurrency mode - combined with all six columns carrying
    /// <c>updatewhereclause=yes</c> it makes the optimistic-concurrency check span all six columns'
    /// ORIGINAL values. Kept as text because <c>Describe</c> answers in text.
    /// </remarks>
    public string UpdateWhere { get; set; } = "0";

    /// <summary>
    /// The answer to <c>Describe("DataWindow.Table.UpdateKeyInPlace")</c> - <c>"yes"</c> or <c>"no"</c>.
    /// </summary>
    /// <remarks>
    /// The primary fixture declares <c>updatekeyinplace=no</c> [<c>dw_sqlite.srd:L14</c>], which means a
    /// key change is performed as delete-plus-insert rather than in place - and is exactly the setting
    /// that triggers the legacy's own self-assignment workaround on the Persistence side. Because the
    /// fixture sets it, that path is exercised by the fixture rather than being a rare branch.
    /// </remarks>
    public string UpdateKeyInPlace { get; set; } = "yes";

    /// <summary>
    /// The answer to <c>Describe("DataWindow.Table.Sort")</c>, from the fixture's <c>sort=</c> attribute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The primary fixture declares <c>sort="age A salary A "</c> [<c>dw_sqlite.srd:L14</c>] - note the
    /// TRAILING SPACE, which is part of the literal and is carried verbatim rather than trimmed.
    /// </para>
    /// <para>
    /// NAMED FOR THE DESCRIBE PROPERTY IT BACKS, not <c>Sort</c>, because <c>Sort()</c> is now a member of
    /// the host contract itself [<c>n_cst_dwsvc_columnsort.sru:L415</c>] and a property may not share a
    /// name with an inherited method. The two are genuinely different things and the rename makes that
    /// legible: this is the sort expression the DataWindow REPORTS, whereas
    /// <see cref="SetSort(string)"/> sets one and <see cref="Sort"/> applies it. It is deliberately NOT
    /// updated by <see cref="SetSort(string)"/> - see that member's remarks.
    /// </para>
    /// </remarks>
    public string TableSort { get; set; } = string.Empty;

    /// <summary>
    /// The value <see cref="GetFocusedObject"/> reports.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> by default, which means nothing holds focus. The oracle compares it against
    /// the host itself - <c>if GetFocus() &lt;&gt; this then</c> [<c>se_cst_dw.sru:L553</c>] - so a suite
    /// that wants the "focus is already on the DataWindow" arm assigns the host to this property. That is
    /// a reference comparison, which is why the property is typed <c>object?</c> rather than as a boolean.
    /// </remarks>
    public object? FocusedObject { get; set; }

    /// <summary>
    /// The last value <see cref="SetRedraw(bool)"/> was called with, or <see langword="null"/> when it has
    /// not been called.
    /// </summary>
    /// <remarks>
    /// Nullable so that "never called" is distinguishable from "called with false". The only ported call
    /// site passes <see langword="true"/> [<c>se_cst_dw.sru:L442</c>], so a suite asserting the repaint
    /// happened is asserting both that it happened and that it was an enable.
    /// </remarks>
    public bool? RedrawEnabled { get; private set; }

    /// <summary>
    /// The code <see cref="SetRow(long)"/> returns for a row that exists. Defaults to the legacy success
    /// value <c>1</c>.
    /// </summary>
    public int SetRowResult { get; set; } = 1;

    /// <summary>
    /// Whether <see cref="SetRow(long)"/> actually moves <see cref="CurrentRow"/>. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Set it to <see langword="false"/> while leaving <see cref="SetRowResult"/> at <c>1</c> to reproduce
    /// the situation the contract's DECISION 4 exists for: a successful return that did not move the
    /// cursor, which the oracle detects by re-reading <c>GetRow()</c> and answers with prevent
    /// [<c>se_cst_dw.sru:L154-L157</c>].
    /// </remarks>
    public bool SetRowMovesCurrentRow { get; set; } = true;

    /// <summary>
    /// The code <see cref="AcceptText"/> returns. Defaults to <c>1</c>.
    /// </summary>
    /// <remarks>
    /// Set it to <c>-1</c> to drive the failure branch of the deferred accept, which is the only thing the
    /// ported call site tests for: <c>if AcceptText() = -1 then SetFocus()</c>
    /// [<c>se_cst_dw.sru:L554-L556</c>].
    /// </remarks>
    public int AcceptTextResult { get; set; } = 1;

    /// <summary>The code <see cref="SetRedraw(bool)"/> returns. Defaults to <c>1</c>.</summary>
    public int SetRedrawResult { get; set; } = 1;

    /// <summary>The code <see cref="SetSort(string)"/> returns. Defaults to <c>1</c>.</summary>
    public int SetSortResult { get; set; } = 1;

    /// <summary>The code <see cref="Sort"/> returns. Defaults to <c>1</c>.</summary>
    public int SortResult { get; set; } = 1;

    /// <summary>The code <see cref="GroupCalc"/> returns. Defaults to <c>1</c>.</summary>
    public int GroupCalcResult { get; set; } = 1;

    /// <summary>
    /// The expression the last <see cref="SetSort(string)"/> was handed, or <see langword="null"/> when it
    /// has never been called.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> AND THE EMPTY STRING ARE DIFFERENT ANSWERS HERE, deliberately. The empty
    /// string is a legal sort expression meaning "no sort", and the sort service passes it whenever the
    /// user cleared every column and there was no original sort to restore
    /// [<c>n_cst_dwsvc_columnsort.sru:L201-L207</c>]. A test asserting the clear-the-sort path must be
    /// able to tell that from the call never having happened.
    /// </remarks>
    public string? AppliedSort { get; private set; }

    /// <summary>How many times <see cref="Sort"/> has been called.</summary>
    public int SortCallCount { get; private set; }

    /// <summary>How many times <see cref="GroupCalc"/> has been called.</summary>
    public int GroupCalcCallCount { get; private set; }

    /// <summary>
    /// Row number to stable row identifier, backing <see cref="GetRowIDFromRow(long)"/>. An unmapped row
    /// answers <c>0</c>.
    /// </summary>
    public Dictionary<long, long> RowIdsByRow { get; } = [];

    /// <summary>
    /// Stable row identifier to row number, backing <see cref="GetRowFromRowID(long)"/>. Kept SEPARATE
    /// from <see cref="RowIdsByRow"/> rather than inverted from it, because a re-sort is exactly the event
    /// that makes the two disagree - and reproducing that disagreement is the only way the row-identity
    /// round trip at <c>n_cst_dwsvc_columnsort.sru:L408</c> and <c>:L422</c> becomes observable.
    /// </summary>
    public Dictionary<long, long> RowsByRowId { get; } = [];

    /// <summary>The code <see cref="SetFocus"/> returns. Defaults to <c>1</c>.</summary>
    public int SetFocusResult { get; set; } = 1;

    /// <summary>
    /// The code every <c>SetItem</c> overload returns for a row that exists. Defaults to <c>1</c>.
    /// </summary>
    public int SetItemResult { get; set; } = 1;

    /// <summary>
    /// The code <see cref="SetItemStatus(long, long, DwBuffer, ItemStatus)"/> returns for a row that
    /// exists. Defaults to <c>1</c>.
    /// </summary>
    public int SetItemStatusResult { get; set; } = 1;

    /// <summary>
    /// The value <see cref="SelectRow(long, bool)"/> answers. Defaults to <c>1</c>, the DataWindow's
    /// success code.
    /// </summary>
    /// <remarks>
    /// Settable for completeness only. Every ported call site DISCARDS the code, exactly as the oracle
    /// does [<c>n_cst_dwsvc_rowselect.sru:L51</c> and fourteen further sites], so no suite is expected
    /// to depend on it - but a suite that wants to prove the discard can set it to <c>-1</c> and observe
    /// that nothing changes.
    /// </remarks>
    public int SelectRowResult { get; set; } = 1;

    /// <summary>
    /// The code <see cref="DataWindowServiceHost.FilterCore"/> returns. Defaults to <c>1</c>, WHICH IS
    /// SUCCESS - the ported override tests <c>if rtCode = 1</c> [<c>se_cst_dw.sru:L407</c>] and is
    /// deliberately not mapped onto a return code whose success is zero.
    /// </summary>
    public int FilterResult { get; set; } = 1;

    /// <summary>
    /// The code <see cref="DataWindowServiceHost.DeleteRowCore(long)"/> returns for a row that exists.
    /// Defaults to <c>1</c>, which is success [<c>se_cst_dw.sru:L432</c>].
    /// </summary>
    public int DeleteRowResult { get; set; } = 1;

    /// <summary>
    /// Whether a successful <see cref="DataWindowServiceHost.DeleteRowCore(long)"/> actually removes the
    /// row from the primary buffer and moves it to the delete buffer. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Modelled rather than stubbed because the ported override reads <c>RowCount()</c> AFTER calling the
    /// base implementation, twice: once to detect that the DataWindow is now empty
    /// [<c>se_cst_dw.sru:L434</c>] and once to decide whether a repaint is needed
    /// [<c>:L441</c>]. A base delete that changed nothing would make both branches unreachable. Removal
    /// also CLAMPS <see cref="CurrentRow"/> to the new row count, which is what a DataWindow does when the
    /// current row was the last one; a suite that wants to control the cursor itself turns this off.
    /// </remarks>
    public bool DeleteRowCoreRemovesRow { get; set; } = true;

    /// <summary>
    /// Every declared object - columns, computed fields and text objects - in declaration order.
    /// </summary>
    public IReadOnlyList<FakeDataWindowObjectDefinition> Objects => _objects;

    // ==============================================================================================
    //  THE ELEVEN SEMANTIC EVENT HANDLERS
    //  --------------------------------------------------------------------------------------------
    //  Settable delegates rather than fixed behaviour, so a suite scripts the exact code it needs. An
    //  UNSET handler falls through to the CONTRACT'S OWN DEFAULT - 0 for ten of them and null for
    //  ItemError - which is what an unhandled PowerBuilder event yields, so an unscripted host behaves
    //  the way an unscripted DataWindow behaves rather than the way this file happens to feel like.
    //
    //  THERE IS DELIBERATELY NO RButtonUpHandler AND NO LButtonUpHandler. `ondwnrbuttonup`
    //  [se_cst_dw.sru:L120] and `ondwnlbuttonup` [:L395] trigger the broker and nothing else - neither
    //  has a semantic counterpart to script. Their absence is an asymmetry the event-chain suites assert,
    //  so adding either would fabricate a hook the oracle does not have (constraint C-B).
    //
    //  Each raise is recorded BEFORE the handler runs, so a handler that itself calls back into the host -
    //  the mid-flight buffer mutation, or the posted row deletion - produces a log in the order the calls
    //  actually happened.
    // ==============================================================================================

    /// <summary>
    /// Scripts <see cref="RButtonDown(long, long, long, IDataWindowObject)"/>. Parameters are x, y, row
    /// and the object under the pointer; return <c>1</c> to prevent.
    /// </summary>
    public Func<long, long, long, IDataWindowObject, long>? RButtonDownHandler { get; set; }

    /// <summary>
    /// Scripts <see cref="RowFocusChanged(long)"/>. The parameter is the new current row; return <c>1</c>
    /// to prevent.
    /// </summary>
    public Func<long, long>? RowFocusChangedHandler { get; set; }

    /// <summary>
    /// Scripts <see cref="RowFocusChanging(long, long)"/>. Parameters are the row focus is leaving and the
    /// row it is moving to; return <c>1</c> to prevent the move.
    /// </summary>
    public Func<long, long, long>? RowFocusChangingHandler { get; set; }

    /// <summary>
    /// Scripts <see cref="DoubleClicked(long, long, long, IDataWindowObject)"/>.
    /// </summary>
    public Func<long, long, long, IDataWindowObject, long>? DoubleClickedHandler { get; set; }

    /// <summary>
    /// Scripts <see cref="Clicked(long, long, long, IDataWindowObject)"/>.
    /// </summary>
    public Func<long, long, long, IDataWindowObject, long>? ClickedHandler { get; set; }

    /// <summary>
    /// Scripts <see cref="EditChanged(long, IDataWindowObject, string)"/>. Parameters are the row, the
    /// column and the current edit text.
    /// </summary>
    /// <remarks>
    /// This is the one event the item-change protocol re-raises from inside itself, when the buffer value
    /// turns out to have moved [<c>se_cst_dw.sru:L204-L208</c>], so a suite asserting how MANY times it
    /// ran is asking a real question. <see cref="DataWindowCallLog.CountOf(string)"/> answers it.
    /// </remarks>
    public Func<long, IDataWindowObject, string, long>? EditChangedHandler { get; set; }

    /// <summary>
    /// Scripts <see cref="ItemFocusChanged(long, IDataWindowObject)"/>.
    /// </summary>
    public Func<long, IDataWindowObject, long>? ItemFocusChangedHandler { get; set; }

    /// <summary>
    /// Scripts <see cref="ItemChanged(long, IDataWindowObject, string)"/>, whose return value is in the
    /// FOUR-VALUE ITEM-CHANGE ALPHABET <c>{0, 1, 2, 3}</c> and is NOT a return code.
    /// </summary>
    /// <remarks>
    /// The most consequential handler on this double. Its value is propagated verbatim into the
    /// micro-protocol at <c>se_cst_dw.sru:L211-L251</c> and is also stashed for the validation-error event
    /// to consume [<c>:L195</c>, <c>:L338-L340</c>]. A handler that mutates the buffer before returning is
    /// how the equality test at <c>:L198-L202</c> is driven to its unequal arm - write through
    /// <c>DwObject(...).Primary[row]</c> from inside the handler.
    /// </remarks>
    public Func<long, IDataWindowObject, string, long>? ItemChangedHandler { get; set; }

    /// <summary>
    /// Scripts <see cref="ItemError(long, IDataWindowObject, string)"/>, WHICH MAY RETURN
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// THE NULLABLE RETURN IS THE POINT. <c>se_cst_dw.sru:L343</c> assigns the result and <c>:L344</c>
    /// immediately coerces it with <c>if IsNull(rtCode) then rtCode = 0</c>. No other one of the eleven
    /// carries such a guard, so this handler must be able to return null or that ported line is
    /// unreachable. Note that an UNSET handler also yields null, because the contract's default body
    /// returns null - so the arm is reachable both by scripting it and by leaving the handler alone, and a
    /// suite should be explicit about which it means.
    /// </remarks>
    public Func<long, IDataWindowObject, string, long?>? ItemErrorHandler { get; set; }

    /// <summary>
    /// The stand-in for a subscriber of <see cref="DataWindowServiceHost.OnDoItemChange"/>. Answering
    /// anything OTHER THAN <c>0</c> REFUSES the change.
    /// </summary>
    /// <remarks>
    /// The test seam for the refusal path at
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru:L238</c>, whose test is
    /// <c>&lt;&gt; 0</c> - a two-state accept-or-reject read, NOT the four-value item-change alphabet
    /// and NOT the return-code algebra. When unset the inherited no-op answers <c>0</c> and every change
    /// is accepted.
    /// </remarks>
    public Func<long, IDataWindowObject, string, long>? DoItemChangeHandler { get; set; }

    /// <summary>
    /// The stand-in for a subscriber of <see cref="DataWindowServiceHost.OnDoItemChanged"/>.
    /// </summary>
    /// <remarks>
    /// An <see cref="Action"/> rather than a <see cref="Func{T, TResult}"/> because the legacy event is
    /// declared with no <c>type</c> clause [<c>se_cst_dw.sru:L26</c>] and therefore has no code to
    /// return; giving it one would invent a veto the notification does not have.
    /// </remarks>
    public Action<long, IDataWindowObject>? DoItemChangedHandler { get; set; }

    /// <summary>
    /// Scripts <see cref="LoseFocus"/>, whose value the raw handler returns verbatim
    /// [<c>se_cst_dw.sru:L392</c>].
    /// </summary>
    public Func<long>? LoseFocusHandler { get; set; }

    /// <summary>
    /// Scripts <see cref="GetFocus"/>, whose value the raw handler returns verbatim
    /// [<c>se_cst_dw.sru:L400</c>]. NOT to be confused with <see cref="FocusedObject"/>, which is what the
    /// PowerScript system function reports - see DIVERGENCE 2 in this file's header.
    /// </summary>
    public Func<long>? GetFocusHandler { get; set; }

    /// <summary>
    /// One row of one buffer: its column values and its item statuses.
    /// </summary>
    /// <remarks>
    /// The status dictionary is keyed by column number, and KEY <c>0</c> IS THE ROW STATUS rather than a
    /// column's - that is PowerBuilder's own convention for <c>GetItemStatus(row, 0, buffer)</c> and it is
    /// reproduced rather than replaced with a separate field, so a caller that passes column zero gets the
    /// answer the legacy gives.
    /// </remarks>
    private sealed class FakeBufferRow
    {
        public Dictionary<long, object?> Values { get; } = [];

        public Dictionary<long, ItemStatus> Statuses { get; } = [];
    }

    // ==============================================================================================
    //  DECLARATION - the objects the fake DataWindow carries. TEST SETUP, SO NONE OF IT IS RECORDED.
    // ==============================================================================================

    /// <summary>
    /// Declares a data column, giving it the next column number.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <param name="colType">
    /// The raw column type, from <see cref="FakeColumnType"/> - for example
    /// <c>FakeColumnType.CharOf(100)</c>.
    /// </param>
    /// <returns>The declaration, so a suite can set its remaining properties.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The column number counts DATA COLUMNS ONLY, so a text object or a computed field declared in between
    /// does not consume one - which is how the primary fixture's six columns end up numbered 1 to 6
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L21-L26</c>] despite six text objects being declared
    /// before them [<c>:L15-L20</c>].
    /// </remarks>
    public FakeDataWindowObjectDefinition AddColumn(string name, string colType)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(colType);

        long nextId = 0;
        foreach (FakeDataWindowObjectDefinition existing in _objects)
        {
            if (string.Equals(
                    existing.Type,
                    FakeDataWindowObjectDefinition.ColumnType,
                    StringComparison.OrdinalIgnoreCase))
            {
                nextId++;
            }
        }

        FakeDataWindowObjectDefinition column = new(
            name,
            FakeDataWindowObjectDefinition.ColumnType,
            band: "detail")
        {
            Id = nextId + 1,
            ColType = colType,
            DbName = name,
        };

        _objects.Add(column);
        return column;
    }

    /// <summary>
    /// Declares a computed field.
    /// </summary>
    /// <param name="name">The object name, for example <c>"compute_1"</c>.</param>
    /// <param name="band">The band, for example <c>"footer"</c>.</param>
    /// <param name="expression">
    /// The expression, for example <c>"sum(salary for page)"</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L27</c>].
    /// </param>
    /// <returns>The declaration.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public FakeDataWindowObjectDefinition AddComputedField(
        string name,
        string band,
        string expression)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(band);
        ArgumentNullException.ThrowIfNull(expression);

        FakeDataWindowObjectDefinition compute = new(
            name,
            FakeDataWindowObjectDefinition.ComputeType,
            band)
        {
            Expression = expression,
        };

        _objects.Add(compute);
        return compute;
    }

    /// <summary>
    /// Declares a static text object.
    /// </summary>
    /// <param name="name">The object name, for example <c>"id_t"</c>.</param>
    /// <param name="band">The band, for example <c>"header"</c>.</param>
    /// <returns>The declaration.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Text objects are declared because two ported behaviours enumerate ALL objects and then filter by
    /// type and band [<c>n_cst_dwsvc.sru:L693-L712</c>], so a fixture with only columns could not
    /// distinguish a correct filter from one that happened to match everything.
    /// </remarks>
    public FakeDataWindowObjectDefinition AddTextObject(string name, string band)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(band);

        FakeDataWindowObjectDefinition text = new(
            name,
            FakeDataWindowObjectDefinition.TextType,
            band);

        _objects.Add(text);
        return text;
    }

    /// <summary>
    /// The declaration named <paramref name="name"/>, or <see langword="null"/> when there is none.
    /// </summary>
    /// <param name="name">
    /// The object name, or the positional form <c>"#n"</c> that addresses a column by number - the same
    /// spelling <c>_of_getdwobject(colnum)</c> composes [<c>n_cst_dwsvc.sru:L97</c>].
    /// </param>
    /// <returns>The declaration, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public FakeDataWindowObjectDefinition? FindObject(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length > 1 && name[0] == '#')
        {
            // The "#n" positional form. Parsed invariant-culture, because PowerScript's String(long)
            // composes digits with no group separator and a culture that added one would resolve nothing.
            if (long.TryParse(
                    name.AsSpan(1),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long columnNumber))
            {
                return FindColumn(columnNumber);
            }

            return null;
        }

        foreach (FakeDataWindowObjectDefinition candidate in _objects)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The data column whose number is <paramref name="columnId"/>, or <see langword="null"/> when there is
    /// none.
    /// </summary>
    /// <param name="columnId">The one-based column number.</param>
    /// <returns>The declaration, or <see langword="null"/>.</returns>
    public FakeDataWindowObjectDefinition? FindColumn(long columnId)
    {
        foreach (FakeDataWindowObjectDefinition candidate in _objects)
        {
            if (candidate.Id == columnId
                && string.Equals(
                    candidate.Type,
                    FakeDataWindowObjectDefinition.ColumnType,
                    StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The declaration named <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The object name, or the positional <c>"#n"</c> form.</param>
    /// <returns>The declaration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">There is no such object.</exception>
    /// <remarks>
    /// The raising counterpart of <see cref="FindObject(string)"/>, for a suite that is arranging a fixture
    /// it declared itself and wants a typo to fail immediately rather than to surface later as a null.
    /// </remarks>
    public FakeDataWindowObjectDefinition Column(string name)
    {
        return FindObject(name)
            ?? throw new KeyNotFoundException(
                "This fake DataWindow declares no object named '" + name + "'.");
    }

    /// <summary>
    /// Sets one column's validation message, which
    /// <c>Describe(dwo.Name + ".ValidationMsg")</c> then answers with.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="message">
    /// The message EXACTLY as a DataWindow would report it, quotes included. See
    /// <see cref="FakeDataWindowObjectDefinition.ValidationMsg"/> for the four shapes that between them
    /// drive every branch of <c>se_cst_dw.sru:L350-L358</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">There is no such object.</exception>
    public void SetValidationMessage(string column, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Column(column).ValidationMsg = message;
    }

    /// <summary>
    /// Registers, or removes, the child DataWindow that <see cref="GetChild(string, ref IDataWindowChild?)"/>
    /// hands back for <paramref name="column"/>.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="child">The child, or <see langword="null"/> to remove any registration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="column"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Registration is deliberately SEPARATE from declaring the column's <c>dddw.*</c> properties, because
    /// the oracle reads the two through different doors and a real DataWindow can disagree with itself: a
    /// column can name a drop-down DataWindow that fails to materialise, which is precisely why
    /// <c>n_cst_dwsvc.sru:L593</c> discards <c>GetChild</c>'s return code and tests
    /// <c>IsValidObject(dwc)</c> instead. Declaring the properties without registering a child reproduces
    /// that state.
    /// </remarks>
    public void SetChild(string column, IDataWindowChild? child)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (child is null)
        {
            _children.Remove(column);
            return;
        }

        _children[column] = child;
    }

    /// <summary>
    /// Teaches the host one <c>Describe</c> answer, or replaces it when already present.
    /// </summary>
    /// <param name="property">The full property expression, matched case-insensitively.</param>
    /// <param name="value">The answer.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Consulted after <see cref="DescribeOverride"/> and before every computed answer, so it can override
    /// any of them - including forcing a well-known key to a sentinel, which is how the group-band probes
    /// at <c>n_cst_dwsvc.sru:L850-L851</c> are driven to their true arm.
    /// </remarks>
    public void SetDescribe(string property, string value)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);

        _describeTable[property] = value;
    }

    /// <summary>
    /// The <c>dwobject</c> reference for one declared object, as the concrete fake type.
    /// </summary>
    /// <param name="name">The object name, or the positional <c>"#n"</c> form.</param>
    /// <returns>The reference.</returns>
    /// <exception cref="KeyNotFoundException">There is no such object.</exception>
    /// <remarks>
    /// <para>
    /// NAMED FOR THE LEGACY TYPE, AND NOT NAMED "HANDLE", DELIBERATELY. PowerBuilder's <c>#Handle</c> is a
    /// WIN32 WINDOW HANDLE and belongs to the deferred DesignSystem capability, so constraint C-D keeps
    /// that word off every identifier in this file - no member here names or types a window handle, a DPI
    /// conversion, a font, a colour, a canvas, a painter or a menu. What this member returns is a
    /// <c>dwobject</c> reference, which is a column or report object and carries no geometry at all.
    /// </para>
    /// <para>
    /// ONE REFERENCE PER DECLARED OBJECT, CACHED, AND THAT IDENTITY MATTERS. Resolving <c>"age"</c> and
    /// resolving <c>"#3"</c> yield THE SAME instance, so a value a suite writes through one spelling is
    /// visible to code that resolved the other. A fresh reference per call would let the two disagree, and
    /// the item-change equality test at <c>se_cst_dw.sru:L198-L202</c> would then compare two different
    /// objects' views of the same buffer.
    /// </para>
    /// <para>
    /// The reference snapshots the column number at creation, so a suite that intends to renumber a column
    /// should do so BEFORE resolving it.
    /// </para>
    /// </remarks>
    public FakeDataWindowObject DwObject(string name)
    {
        FakeDataWindowObjectDefinition definition = Column(name);

        if (_dwObjects.TryGetValue(definition.Name, out FakeDataWindowObject? cached))
        {
            return cached;
        }

        FakeDataWindowObject resolved = new(this, definition);
        _dwObjects[definition.Name] = resolved;
        return resolved;
    }

    /// <summary>
    /// The <c>dwobject</c> reference for the data column numbered <paramref name="columnId"/>.
    /// </summary>
    /// <param name="columnId">The one-based column number.</param>
    /// <returns>The reference.</returns>
    /// <exception cref="KeyNotFoundException">There is no such column.</exception>
    public FakeDataWindowObject DwObject(long columnId)
    {
        FakeDataWindowObjectDefinition definition = FindColumn(columnId)
            ?? throw new KeyNotFoundException(
                "This fake DataWindow declares no column numbered "
                + columnId.ToString(CultureInfo.InvariantCulture)
                + ".");

        return DwObject(definition.Name);
    }

    // ==============================================================================================
    //  ROW STORE - TEST SETUP, SO NONE OF IT IS RECORDED (with one named exception, below).
    // ==============================================================================================

    /// <summary>
    /// The number of rows in <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The buffer to measure.</param>
    /// <returns>
    /// The row count, which is also the LAST VALID ROW NUMBER because rows are one-based.
    /// </returns>
    public int RowCountOf(DwBuffer buffer) => BufferOf(buffer).Count;

    /// <summary>
    /// Appends a row to the primary buffer, taking values positionally in declared column order.
    /// </summary>
    /// <param name="values">
    /// The values, one per data column from the first onward. Fewer values than columns leaves the
    /// remaining columns NULL - which is a real state and not a gap, so it is left as null rather than
    /// defaulted. More values than columns is a setup fault and raises.
    /// </param>
    /// <returns>The ONE-BASED row number of the new row.</returns>
    /// <exception cref="ArgumentException">
    /// More values were supplied than the DataWindow has data columns.
    /// </exception>
    public long AddRow(params object?[]? values) => AddRowTo(DwBuffer.Primary, values);

    /// <summary>
    /// Appends a row to <paramref name="buffer"/>, taking values positionally in declared column order.
    /// </summary>
    /// <param name="buffer">The buffer to append to.</param>
    /// <param name="values">The values, one per data column from the first onward.</param>
    /// <returns>The ONE-BASED row number of the new row within that buffer.</returns>
    /// <exception cref="ArgumentException">
    /// More values were supplied than the DataWindow has data columns.
    /// </exception>
    public long AddRowTo(DwBuffer buffer, params object?[]? values)
    {
        object?[] supplied = values ?? [];
        List<FakeDataWindowObjectDefinition> columns = DataColumns();

        if (supplied.Length > columns.Count)
        {
            throw new ArgumentException(
                "This fake DataWindow declares "
                    + columns.Count.ToString(CultureInfo.InvariantCulture)
                    + " data column(s) but "
                    + supplied.Length.ToString(CultureInfo.InvariantCulture)
                    + " value(s) were supplied.",
                nameof(values));
        }

        FakeBufferRow row = new();
        for (int index = 0; index < supplied.Length; index++)
        {
            row.Values[columns[index].Id] = supplied[index];
        }

        List<FakeBufferRow> rows = BufferOf(buffer);
        rows.Add(row);
        return OneBasedRows.ToRowNumber(rows.Count - 1);
    }

    /// <summary>
    /// Removes one row from the primary buffer WITHOUT moving it to the delete buffer.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <returns>
    /// <see langword="true"/> when the row existed and was removed; <see langword="false"/> when it did
    /// not exist.
    /// </returns>
    /// <remarks>
    /// Returning a flag rather than raising, because the callers that matter are arranging a scenario in
    /// which the row may already be gone. <see cref="CurrentRow"/> is clamped to the new row count, which
    /// is what a DataWindow does when the current row was the last one.
    /// </remarks>
    public bool RemoveRow(long row) => RemoveRowFrom(DwBuffer.Primary, row);

    /// <summary>
    /// Removes one row from <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The buffer to remove from.</param>
    /// <param name="row">The one-based row number.</param>
    /// <returns><see langword="true"/> when the row existed and was removed.</returns>
    public bool RemoveRowFrom(DwBuffer buffer, long row)
    {
        List<FakeBufferRow> rows = BufferOf(buffer);
        if (!OneBasedRows.IsInRange(row, rows.Count))
        {
            return false;
        }

        rows.RemoveAt(OneBasedRows.ToListIndex(row));

        if (buffer == DwBuffer.Primary && CurrentRow > rows.Count)
        {
            CurrentRow = rows.Count;
        }

        return true;
    }

    /// <summary>
    /// Simulates the row being deleted WHILE A DIALOG WAS UP - the situation the defensive guard at
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L369</c> exists for.
    /// </summary>
    /// <param name="row">The one-based row number to delete.</param>
    /// <returns><see langword="true"/> when the row existed and was removed.</returns>
    /// <remarks>
    /// <para>
    /// WHY THIS MEMBER EXISTS RATHER THAN A SUITE JUST CALLING <see cref="RemoveRow(long)"/>. The legacy
    /// shows a <c>MessageBox</c> at <c>:L357</c> and then, at <c>:L369</c>, re-checks
    /// <c>row &lt;= RowCount()</c> before restoring the value - with an inline comment stating the reason:
    /// the row may have been deleted by the message the dialog pumped. In the port the dialog becomes a
    /// structured error result, so there is no message pump to deliver that deletion; a suite has to
    /// inject it. Calling this from inside a scripted <see cref="ItemErrorHandler"/> is the deterministic
    /// reproduction, and the guard is then genuinely exercised rather than merely present.
    /// </para>
    /// <para>
    /// IT IS THE ONE SETUP MUTATOR THAT IS RECORDED IN <see cref="CallLog"/>, because a suite asserting
    /// this scenario cares WHERE in the sequence the deletion landed - before or after the value snapshot -
    /// and a silent removal would make that unanswerable.
    /// </para>
    /// </remarks>
    public bool SimulateRowDeletedByPostedMessage(long row)
    {
        CallLog.Record("SimulateRowDeletedByPostedMessage", row);
        return RemoveRow(row);
    }

    /// <summary>
    /// Reads one item directly, without going through a contract member and without recording anything.
    /// </summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnId">The column number.</param>
    /// <returns>
    /// The stored value, or <see langword="null"/> when the item is null, has never been written, or the
    /// row does not exist.
    /// </returns>
    /// <remarks>
    /// A read of a non-existent row answers <see langword="null"/> rather than raising, because the oracle
    /// reads <c>dwo.Primary[row]</c> at <c>se_cst_dw.sru:L334</c> BEFORE any bounds guard and a raise there
    /// would turn a normal path into a fault.
    /// </remarks>
    public object? GetBufferValue(DwBuffer buffer, long row, long columnId)
    {
        List<FakeBufferRow> rows = BufferOf(buffer);
        if (!OneBasedRows.IsInRange(row, rows.Count))
        {
            return null;
        }

        return rows[OneBasedRows.ToListIndex(row)].Values.TryGetValue(columnId, out object? value)
            ? value
            : null;
    }

    /// <summary>
    /// Writes one item directly, without going through a contract member and without recording anything.
    /// </summary>
    /// <param name="buffer">The buffer to write.</param>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnId">The column number.</param>
    /// <param name="value">The value, which may be <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="row"/> does not exist. A write is only ever test setup or a scripted mid-flight
    /// mutation, so silently discarding one would hide a mistake in the suite.
    /// </exception>
    /// <remarks>
    /// NO TYPE CHECKING AGAINST THE COLUMN, deliberately. The restore path at <c>se_cst_dw.sru:L219</c>
    /// writes back an <c>any</c> without knowing the column's type, and a double that rejected a mismatch
    /// would make that arm unreachable (constraint C-B).
    /// </remarks>
    public void SetBufferValue(DwBuffer buffer, long row, long columnId, object? value)
    {
        RowAt(buffer, row).Values[columnId] = value;
    }

    /// <summary>
    /// Reads one item status directly, without recording anything.
    /// </summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnId">
    /// The column number, or <c>0</c> for THE ROW'S OWN STATUS - PowerBuilder's column-zero convention,
    /// reproduced rather than replaced.
    /// </param>
    /// <returns>
    /// The status, or <see cref="ItemStatus.NotModified"/> when none was set or the row does not exist.
    /// </returns>
    /// <remarks>
    /// <see cref="ItemStatus.NotModified"/> is the right answer for "never set" rather than a fabricated
    /// one: it is the published enum's zero, it is the state a freshly retrieved row is in, and it is what
    /// an unassigned <c>dwItemStatus</c> reads as.
    /// </remarks>
    public ItemStatus GetBufferItemStatus(DwBuffer buffer, long row, long columnId)
    {
        List<FakeBufferRow> rows = BufferOf(buffer);
        if (!OneBasedRows.IsInRange(row, rows.Count))
        {
            return ItemStatus.NotModified;
        }

        return rows[OneBasedRows.ToListIndex(row)].Statuses.TryGetValue(columnId, out ItemStatus status)
            ? status
            : ItemStatus.NotModified;
    }

    /// <summary>
    /// Writes one item status directly, without recording anything.
    /// </summary>
    /// <param name="buffer">The buffer to write.</param>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnId">The column number, or <c>0</c> for the row's own status.</param>
    /// <param name="status">The status to store.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="row"/> does not exist.</exception>
    public void SetBufferItemStatus(DwBuffer buffer, long row, long columnId, ItemStatus status)
    {
        RowAt(buffer, row).Statuses[columnId] = status;
    }

    // ==============================================================================================
    //  THE CONTRACT MEMBERS
    // ==============================================================================================

    /// <inheritdoc/>
    /// <exception cref="KeyNotFoundException">
    /// There is no object named <paramref name="dwoName"/>. THE THROW IS THE CONTRACT: the legacy carries
    /// its own warning immediately above the call [<c>n_cst_dwsvc.sru:L121</c>] that an exception occurs
    /// when the object does not exist, and the caller
    /// <c>DataWindowServiceBase.GetDataWindowObject(in string)</c> already uses null to mean something
    /// else entirely - that the DataWindow has no valid object model at all [<c>:L119</c>]. Returning null
    /// here would erase a distinction the oracle makes, so this member raises and the exception is allowed
    /// to propagate.
    /// </exception>
    public override IDataWindowObject GetObjectAttribute(string dwoName)
    {
        ArgumentNullException.ThrowIfNull(dwoName);

        RecordRead("GetObjectAttribute", dwoName);
        return DwObject(dwoName);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Resolution order, and each step exists for a reason: <see cref="DescribeOverride"/> first so a suite
    /// can answer anything including an <c>Evaluate(...)</c> expression; then the table taught through
    /// <see cref="SetDescribe(string, string)"/>; then the well-known <c>DataWindow.*</c> keys; then a
    /// per-object property; and finally <see cref="InvalidExpressionSentinel"/>. IT NEVER RAISES, because
    /// ported callers branch on the sentinels and an exception would replace a branch with a fault.
    /// </remarks>
    public override string Describe(string property)
    {
        ArgumentNullException.ThrowIfNull(property);

        RecordRead("Describe", property);

        if (DescribeOverride is not null)
        {
            string? overridden = DescribeOverride(property);
            if (overridden is not null)
            {
                return overridden;
            }
        }

        if (_describeTable.TryGetValue(property, out string? taught))
        {
            return taught;
        }

        // The well-known DataWindow-level keys, each one measured in a ported source.
        if (string.Equals(property, "DataWindow.Processing", StringComparison.OrdinalIgnoreCase))
        {
            return Processing;
        }

        if (string.Equals(property, "DataWindow.ReadOnly", StringComparison.OrdinalIgnoreCase))
        {
            return ReadOnly;
        }

        if (string.Equals(property, "DataWindow.Objects", StringComparison.OrdinalIgnoreCase))
        {
            return ObjectNameList();
        }

        if (string.Equals(property, "DataWindow.Column.Count", StringComparison.OrdinalIgnoreCase))
        {
            return DataColumns().Count.ToString(CultureInfo.InvariantCulture);
        }

        if (string.Equals(property, "DataWindow.Table.Select", StringComparison.OrdinalIgnoreCase))
        {
            return RetrieveStatement;
        }

        if (string.Equals(property, "DataWindow.Table.UpdateTable", StringComparison.OrdinalIgnoreCase))
        {
            return UpdateTable;
        }

        if (string.Equals(property, "DataWindow.Table.UpdateWhere", StringComparison.OrdinalIgnoreCase))
        {
            return UpdateWhere;
        }

        if (string.Equals(
                property,
                "DataWindow.Table.UpdateKeyInPlace",
                StringComparison.OrdinalIgnoreCase))
        {
            return UpdateKeyInPlace;
        }

        if (string.Equals(property, "DataWindow.Table.Sort", StringComparison.OrdinalIgnoreCase))
        {
            return TableSort;
        }

        // A per-object property expression: everything up to the FIRST dot names the object, and the
        // remainder is the property - which is why a multi-part property such as `dddw.displaycolumn`
        // survives the split intact.
        int separator = property.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 || separator >= property.Length - 1)
        {
            return InvalidExpressionSentinel;
        }

        FakeDataWindowObjectDefinition? definition = FindObject(property.Substring(0, separator));
        if (definition is not null
            && definition.TryDescribe(property.Substring(separator + 1), out string value))
        {
            return value;
        }

        // Unknown object, or a property this object does not recognise. Every group-band probe lands here
        // too, which is exactly what makes `Describe("DataWindow.Header.1.Height") <> "!"`
        // [n_cst_dwsvc.sru:L850] answer false for a fixture that declares no group bands.
        return InvalidExpressionSentinel;
    }

    /// <inheritdoc/>
    public override long RowCount()
    {
        RecordRead("RowCount");
        return BufferOf(DwBuffer.Primary).Count;
    }

    /// <inheritdoc/>
    public override long GetRow()
    {
        RecordRead("GetRow");
        return CurrentRow;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <c>-1</c> for a row that does not exist, which is what a DataWindow does. Otherwise it
    /// returns <see cref="SetRowResult"/> and moves <see cref="CurrentRow"/> only when
    /// <see cref="SetRowMovesCurrentRow"/> allows it - see that property for why the two are separable.
    /// </remarks>
    public override int SetRow(long row)
    {
        CallLog.Record("SetRow", row);

        if (!OneBasedRows.IsInRange(row, BufferOf(DwBuffer.Primary).Count))
        {
            return -1;
        }

        if (SetRowMovesCurrentRow)
        {
            CurrentRow = row;
        }

        return SetRowResult;
    }

    /// <inheritdoc/>
    public override int AcceptText()
    {
        CallLog.Record("AcceptText");
        return AcceptTextResult;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A headless no-op that records the call and remembers the flag in <see cref="RedrawEnabled"/>. That
    /// is not a stub: repainting has no observable effect a service response can carry, so there is nothing
    /// for it to do and nothing about it is deferred. The member is on the contract because in-scope logic
    /// genuinely calls it [<c>se_cst_dw.sru:L442</c>], and it carries no geometry, no DPI conversion, no
    /// font and no window handle - so it does not breach constraint C-D.
    /// </remarks>
    public override int SetRedraw(bool enable)
    {
        CallLog.Record("SetRedraw", enable);
        RedrawEnabled = enable;
        return SetRedrawResult;
    }

    // ==============================================================================================
    //  SORT AND ROW IDENTITY                    n_cst_dwsvc_columnsort.sru:L408, L414, L415, L418, L422
    //  --------------------------------------------------------------------------------------------
    //  The five members the sort service's apply path consumes. The row-identifier ROUND TRIP is
    //  modelled rather than faked away: RowIdsByRow and RowsByRowId are two independently settable
    //  maps, so a test can make an identifier resolve to a DIFFERENT row after the sort - which is the
    //  only configuration in which the restore at :L421-L423 is distinguishable from doing nothing.
    //  Every call is recorded, because the ORDER of SetSort, Sort and GroupCalc relative to the
    //  SetRedraw bracket and the event-gate save and restore is itself the ported behaviour.
    // ==============================================================================================

    /// <inheritdoc/>
    /// <remarks>
    /// Answers from <see cref="RowIdsByRow"/> when the row has a mapping and <c>0</c> otherwise, so an
    /// unmapped row reproduces PowerBuilder's non-positive answer for an out-of-range row and the
    /// <c>&gt; 0</c> guard at <c>n_cst_dwsvc_columnsort.sru:L421</c> is exercised in both directions.
    /// </remarks>
    public override long GetRowIDFromRow(long row)
    {
        RecordRead("GetRowIDFromRow", row);
        return RowIdsByRow.TryGetValue(row, out long rowId) ? rowId : 0L;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Answers from <see cref="RowsByRowId"/> when the identifier has a mapping and <c>0</c> otherwise -
    /// the "the row went away" case, which the oracle passes straight into <c>SetRow</c> without
    /// checking [<c>:L422</c>].
    /// </remarks>
    public override long GetRowFromRowID(long rowId)
    {
        RecordRead("GetRowFromRowID", rowId);
        return RowsByRowId.TryGetValue(rowId, out long row) ? row : 0L;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Remembers the expression in <see cref="AppliedSort"/> WITHOUT reordering anything, which is
    /// exactly the split between <c>SetSort</c> [<c>:L414</c>] and <c>Sort</c> [<c>:L415</c>]. It does
    /// NOT update the <c>"DataWindow.Table.Sort"</c> Describe answer: a test controls that separately so
    /// the equal-sort early-out at <c>:L404</c> can be driven independently of what was last set.
    /// </remarks>
    public override int SetSort(string sort)
    {
        CallLog.Record("SetSort", sort);
        AppliedSort = sort;
        return SetSortResult;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A headless no-op that records the call. Reordering a fabricated row set would prove nothing about
    /// the ported logic, whose observable output is the SEQUENCE of host calls and the sort expression
    /// handed to <see cref="SetSort"/>.
    /// </remarks>
    public override int Sort()
    {
        CallLog.Record("Sort");
        SortCallCount++;
        return SortResult;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Records the call and counts it, so a test can assert that group aggregates are recomputed ONLY
    /// when <see cref="Describe"/> reports a group band [<c>:L417</c>].
    /// </remarks>
    public override int GroupCalc()
    {
        CallLog.Record("GroupCalc");
        GroupCalcCallCount++;
        return GroupCalcResult;
    }

    // ==============================================================================================
    //  THE EVENT GATE                                              se_cst_dw.sru:L109-L111, L469-L525
    //  --------------------------------------------------------------------------------------------
    //  The mask VALUE lives on this host because the oracle declares it as a private instance field of
    //  se_cst_dw [:L88-L89]. The three operations delegate to EventGate so the bit arithmetic - and
    //  the zero-argument rejection - exists in exactly one place and is not re-specified by a test
    //  double. DisabledEvent is settable so a test can start the host with an event ALREADY suppressed,
    //  which is the branch in which the sort path must NOT re-enable it [:L424].
    // ==============================================================================================

    /// <summary>
    /// The suppressed-event mask - the port of <c>long _nDisabledEvent</c>
    /// (<c>se_cst_dw.sru:L88-L89</c>). Settable so a test can arrange either polarity of the
    /// save-and-restore at <c>n_cst_dwsvc_columnsort.sru:L409-L412</c> and <c>:L424-L426</c>.
    /// </summary>
    public uint DisabledEvent { get; set; }

    /// <inheritdoc/>
    public override bool IsEventDisabled(uint evt)
    {
        RecordRead("IsEventDisabled", evt);
        return EventGate.IsEventDisabled(DisabledEvent, evt);
    }

    /// <inheritdoc/>
    public override long DisableEvent(uint evt)
    {
        CallLog.Record("DisableEvent", evt);
        uint mask = DisabledEvent;
        long result = EventGate.DisableEvent(ref mask, evt);
        DisabledEvent = mask;
        return result;
    }

    /// <inheritdoc/>
    public override int EnableEvent(uint evt)
    {
        CallLog.Record("EnableEvent", evt);
        uint mask = DisabledEvent;
        int result = EventGate.EnableEvent(ref mask, evt);
        DisabledEvent = mask;
        return result;
    }

    /// <inheritdoc/>
    public override object? GetFocusedObject()
    {
        RecordRead("GetFocusedObject");
        return FocusedObject;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Records the call and assigns this host to <see cref="FocusedObject"/>, so a subsequent
    /// <see cref="GetFocusedObject"/> reports focus where a real DataWindow would - which is what makes
    /// the deferred-accept sequence at <c>se_cst_dw.sru:L553-L557</c> observable end to end.
    /// </remarks>
    public override int SetFocus()
    {
        CallLog.Record("SetFocus");
        FocusedObject = this;
        return SetFocusResult;
    }

    /// <inheritdoc/>
    public override ItemStatus GetItemStatus(long row, long columnId, DwBuffer buffer)
    {
        RecordRead("GetItemStatus", row, columnId, buffer);
        return GetBufferItemStatus(buffer, row, columnId);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <c>-1</c> for a row that does not exist and <see cref="SetItemStatusResult"/> otherwise.
    /// Always recorded, because restoring a status is half of the pairing that makes a rejected item change
    /// leave no trace [<c>se_cst_dw.sru:L190</c> and <c>:L220</c>, <c>:L335</c> and <c>:L376</c>].
    /// </remarks>
    public override int SetItemStatus(long row, long columnId, DwBuffer buffer, ItemStatus status)
    {
        CallLog.Record("SetItemStatus", row, columnId, buffer, status);

        if (!OneBasedRows.IsInRange(row, BufferOf(buffer).Count))
        {
            return -1;
        }

        BufferOf(buffer)[OneBasedRows.ToListIndex(row)].Statuses[columnId] = status;
        return SetItemStatusResult;
    }

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, string? value)
    {
        CallLog.RecordOverload("SetItem", "string?", row, columnId, value);
        return StoreItem(row, columnId, value);
    }

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, decimal? value)
    {
        CallLog.RecordOverload("SetItem", "decimal?", row, columnId, value);
        return StoreItem(row, columnId, value);
    }

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, long? value)
    {
        CallLog.RecordOverload("SetItem", "long?", row, columnId, value);
        return StoreItem(row, columnId, value);
    }

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, DateTime? value)
    {
        CallLog.RecordOverload("SetItem", "DateTime?", row, columnId, value);
        return StoreItem(row, columnId, value);
    }

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, DateOnly? value)
    {
        CallLog.RecordOverload("SetItem", "DateOnly?", row, columnId, value);
        return StoreItem(row, columnId, value);
    }

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, TimeOnly? value)
    {
        CallLog.RecordOverload("SetItem", "TimeOnly?", row, columnId, value);
        return StoreItem(row, columnId, value);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The restore path's overload. The value is stored EXACTLY as handed over, boxed runtime type
    /// included, so a later read through <c>dwo.Primary[row]</c> reports what was restored rather than a
    /// re-coerced approximation of it - and a null restores a null.
    /// </remarks>
    public override int SetItem(long row, long columnId, object? value)
    {
        CallLog.RecordOverload("SetItem", "object?", row, columnId, value);
        return StoreItem(row, columnId, value);
    }

    // ==============================================================================================
    //  ROW SELECTION AND NAME-KEYED ITEM ACCESS
    //  --------------------------------------------------------------------------------------------
    //  These satisfy the members the contract acquired for
    //  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru, which addresses columns BY
    //  NAME - it takes `sColName = dwo.Name` once at :L188 and threads that string through every read
    //  and write - and which is the only in-scope source that touches row selection at all.
    //
    //  SELECTION IS MODELLED AS REAL STATE RATHER THAN RECORDED AND DISCARDED, because the ported
    //  logic READS BACK what it writes: :L80 is `SelectRow(row, Not IsSelected(row))`, a toggle, and
    //  :L224's GetSelectedRow walks the very set that :L63 through :L78 built. A record-only double
    //  would make the toggle answer the same way twice and would make the walk find nothing, so the
    //  set is genuine and the CallLog records the calls IN ADDITION to applying them.
    //
    //  ROW 0 MEANS EVERY ROW, which is the load-bearing part: `SelectRow(0,false)` is the oracle's
    //  clear-the-whole-selection idiom at :L51, :L63, :L90, :L103, :L174 and :L282, and
    //  `SelectRow(GetRow(),true)` at :L176 and :L278 passes 0 when the DataWindow is empty and
    //  therefore selects everything. A double that treated 0 as out of range would leave all eight
    //  sites doing nothing and nothing would report it.
    // ==============================================================================================

    private readonly SortedSet<long> _selectedRows = [];

    /// <summary>
    /// The rows currently selected, in ascending order - the state
    /// <see cref="SelectRow(long, bool)"/> maintains and <see cref="IsSelected(long)"/> and
    /// <see cref="GetSelectedRow(long)"/> read.
    /// </summary>
    /// <remarks>
    /// Exposed so a suite can assert the OUTCOME of a gesture as well as the call sequence that produced
    /// it. Ascending, because <see cref="GetSelectedRow(long)"/> must walk in row order.
    /// </remarks>
    public IReadOnlyCollection<long> SelectedRows => _selectedRows;

    /// <summary>
    /// Arranges an initial selection without recording anything - test setup, not an observed call.
    /// </summary>
    /// <param name="rows">The one-based rows to mark selected. Replaces any existing selection.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Needed because two of the eight preconditions of the range propagation are selection state -
    /// <c>IsSelected(row)</c> at <c>n_cst_dwsvc_rowselect.sru:L193</c> and the walk at <c>:L224</c> - so
    /// a suite has to be able to establish a multi-row selection before the click it is testing rather
    /// than by performing the gestures that would produce one.
    /// </remarks>
    public void ArrangeSelectedRows(params long[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        _selectedRows.Clear();
        foreach (long row in rows)
        {
            _selectedRows.Add(row);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Applies the change to the real selection set AND records the call. Row <c>0</c> applies to every
    /// row of the primary buffer, which is the oracle's whole-selection idiom; a row outside the buffer
    /// is applied verbatim and is not rejected, because a DataWindow answers such a call with its error
    /// code rather than raising and the ported call sites discard the code anyway.
    /// </remarks>
    public override int SelectRow(long row, bool select)
    {
        CallLog.Record("SelectRow", row, select);

        if (row == 0L)
        {
            // Row 0 means EVERY row.
            if (select)
            {
                long rowCount = BufferOf(DwBuffer.Primary).Count;
                for (long candidate = OneBasedRows.FirstRow; candidate <= rowCount; candidate++)
                {
                    _selectedRows.Add(candidate);
                }
            }
            else
            {
                _selectedRows.Clear();
            }

            return SelectRowResult;
        }

        if (select)
        {
            _selectedRows.Add(row);
        }
        else
        {
            _selectedRows.Remove(row);
        }

        return SelectRowResult;
    }

    /// <inheritdoc/>
    public override bool IsSelected(long row)
    {
        RecordRead("IsSelected", row);
        return _selectedRows.Contains(row);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Searches STRICTLY AFTER <paramref name="startRow"/> and answers <c>0</c> when there is no further
    /// selected row - which is what terminates the oracle's walk, since <c>:L224</c> feeds its own answer
    /// back in and <c>:L225</c> exits on a non-positive one. A double that could answer
    /// <paramref name="startRow"/> itself would spin for ever on the first selected row.
    /// </remarks>
    public override long GetSelectedRow(long startRow)
    {
        RecordRead("GetSelectedRow", startRow);

        foreach (long candidate in _selectedRows)
        {
            if (candidate > startRow)
            {
                return candidate;
            }
        }

        return 0L;
    }

    /// <inheritdoc/>
    public override string? GetItemString(long row, string column)
    {
        ArgumentNullException.ThrowIfNull(column);

        RecordRead("GetItemString", row, column);
        return FakeItemValue.AsString(ReadNamedItem(row, column));
    }

    /// <inheritdoc/>
    public override decimal? GetItemDecimal(long row, string column)
    {
        ArgumentNullException.ThrowIfNull(column);

        RecordRead("GetItemDecimal", row, column);
        return FakeItemValue.AsDecimal(ReadNamedItem(row, column));
    }

    /// <inheritdoc/>
    public override double? GetItemNumber(long row, string column)
    {
        ArgumentNullException.ThrowIfNull(column);

        RecordRead("GetItemNumber", row, column);
        return FakeItemValue.AsDouble(ReadNamedItem(row, column));
    }

    /// <inheritdoc/>
    public override int SetItem(long row, string column, string? value)
    {
        ArgumentNullException.ThrowIfNull(column);

        CallLog.RecordOverload("SetItem", "string?", row, column, value);
        return StoreNamedItem(row, column, value);
    }

    /// <inheritdoc/>
    public override int SetItem(long row, string column, decimal? value)
    {
        ArgumentNullException.ThrowIfNull(column);

        CallLog.RecordOverload("SetItem", "decimal?", row, column, value);
        return StoreNamedItem(row, column, value);
    }

    /// <inheritdoc/>
    public override int SetItem(long row, string column, long? value)
    {
        ArgumentNullException.ThrowIfNull(column);

        CallLog.RecordOverload("SetItem", "long?", row, column, value);
        return StoreNamedItem(row, column, value);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Answers <c>display</c> and <c>value</c> joined by a TAB, which is the shape the oracle splits with
    /// <c>Pos(sVal,"~t")</c> [<c>n_cst_dwsvc.sru:L653-L656</c>], and answers THE EMPTY STRING once
    /// <paramref name="index"/> is past the last entry - because that empty answer IS the loop's
    /// termination condition at <c>:L658</c>. An unknown column and a non-positive index answer the empty
    /// string for the same reason: turning either into a raise would convert a normal termination into a
    /// fault.
    /// </remarks>
    public override string GetValue(string column, long index)
    {
        ArgumentNullException.ThrowIfNull(column);

        RecordRead("GetValue", column, index);

        FakeDataWindowObjectDefinition? definition = FindObject(column);
        if (definition is null || index < 1 || index > definition.CodeTable.Count)
        {
            return string.Empty;
        }

        FakeCodeTableEntry entry = definition.CodeTable[OneBasedRows.ToListIndex(index)];
        return entry.Display + "\t" + entry.Value;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Assigns <paramref name="child"/> and answers <c>1</c> when a child is registered for
    /// <paramref name="column"/>; otherwise LEAVES <paramref name="child"/> UNTOUCHED and answers
    /// <c>-1</c>. Leaving it untouched is why the contract declares it <c>ref</c> rather than <c>out</c>:
    /// PowerBuilder leaves the variable unset on failure, and the oracle then establishes validity with
    /// <c>IsValidObject(dwc)</c> [<c>n_cst_dwsvc.sru:L593</c>] while DISCARDING this return code
    /// [<c>:L592</c>].
    /// </remarks>
    public override int GetChild(string column, ref IDataWindowChild? child)
    {
        ArgumentNullException.ThrowIfNull(column);

        RecordRead("GetChild", column);

        if (_children.TryGetValue(column, out IDataWindowChild? registered))
        {
            child = registered;
            return 1;
        }

        return -1;
    }

    /// <inheritdoc/>
    protected override int FilterCore()
    {
        CallLog.Record("FilterCore");
        return FilterResult;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Answers <c>-1</c> for a row that does not exist. Otherwise it answers
    /// <see cref="DeleteRowResult"/> and, when that is the success value <c>1</c> and
    /// <see cref="DeleteRowCoreRemovesRow"/> allows it, moves the row from the primary buffer to the
    /// delete buffer - which is what makes the ported override's post-delete <c>RowCount()</c> reads
    /// meaningful.
    /// </remarks>
    protected override int DeleteRowCore(long row)
    {
        CallLog.Record("DeleteRowCore", row);

        List<FakeBufferRow> primary = BufferOf(DwBuffer.Primary);
        if (!OneBasedRows.IsInRange(row, primary.Count))
        {
            return -1;
        }

        int result = DeleteRowResult;
        if (result == 1 && DeleteRowCoreRemovesRow)
        {
            FakeBufferRow removed = primary[OneBasedRows.ToListIndex(row)];
            primary.RemoveAt(OneBasedRows.ToListIndex(row));
            BufferOf(DwBuffer.Delete).Add(removed);

            if (CurrentRow > primary.Count)
            {
                CurrentRow = primary.Count;
            }
        }

        return result;
    }

    // ==============================================================================================
    //  TWO PUBLIC BRIDGES ONTO THE PROTECTED *Core OPERATIONS
    //  --------------------------------------------------------------------------------------------
    //  Domain/DataWindowServiceHost.cs's DECISION 5 makes Filter and DeleteRow virtual over PROTECTED
    //  abstract *Core operations, so that Domain/DataWindowEventChain.cs's overrides can reach the
    //  base through `base.Filter()` and `base.DeleteRow(nRow)` exactly as the oracle reaches it
    //  through `super::` [se_cst_dw.sru:L406, :L431].
    //
    //  A double for the CHAIN cannot inherit this fake, because the chain is itself a
    //  DataWindowServiceHost and C# has no multiple inheritance - it must COMPOSE one and forward. Every
    //  other member of the contract is public and forwards directly; these two are protected and cannot.
    //  Re-implementing them in the forwarding double would fork the buffer bookkeeping above and let the
    //  two copies drift, which is the one outcome a shared fake exists to prevent. Two additive public
    //  forwarders are therefore the narrow fix: they add no behaviour, change nothing for any existing
    //  test, and keep exactly one implementation of each operation.
    // ==============================================================================================

    /// <summary>
    /// Invokes <see cref="FilterCore"/> on behalf of a composing double.
    /// </summary>
    /// <returns><see cref="FilterResult"/>, having recorded the call.</returns>
    public int InvokeFilterCore() => FilterCore();

    /// <summary>
    /// Invokes <see cref="DeleteRowCore(long)"/> on behalf of a composing double.
    /// </summary>
    /// <param name="row">The one-based row to delete.</param>
    /// <returns>
    /// <c>-1</c> for a row that does not exist, otherwise <see cref="DeleteRowResult"/>, having
    /// recorded the call and moved the row when <see cref="DeleteRowCoreRemovesRow"/> allows it.
    /// </returns>
    public int InvokeDeleteRowCore(long row) => DeleteRowCore(row);

    // ==============================================================================================
    //  THE ELEVEN SEMANTIC EVENTS - recorded, then dispatched to the handler or to the contract default
    // ==============================================================================================

    /// <inheritdoc/>
    public override long RButtonDown(long xpos, long ypos, long row, IDataWindowObject dwo)
    {
        CallLog.Record("Event RButtonDown", xpos, ypos, row, dwo);
        return RButtonDownHandler is null
            ? base.RButtonDown(xpos, ypos, row, dwo)
            : RButtonDownHandler(xpos, ypos, row, dwo);
    }

    /// <inheritdoc/>
    public override long RowFocusChanged(long currentRow)
    {
        CallLog.Record("Event RowFocusChanged", currentRow);
        return RowFocusChangedHandler is null
            ? base.RowFocusChanged(currentRow)
            : RowFocusChangedHandler(currentRow);
    }

    /// <inheritdoc/>
    public override long RowFocusChanging(long currentRow, long newRow)
    {
        CallLog.Record("Event RowFocusChanging", currentRow, newRow);
        return RowFocusChangingHandler is null
            ? base.RowFocusChanging(currentRow, newRow)
            : RowFocusChangingHandler(currentRow, newRow);
    }

    /// <inheritdoc/>
    public override long DoubleClicked(long xpos, long ypos, long row, IDataWindowObject dwo)
    {
        CallLog.Record("Event DoubleClicked", xpos, ypos, row, dwo);
        return DoubleClickedHandler is null
            ? base.DoubleClicked(xpos, ypos, row, dwo)
            : DoubleClickedHandler(xpos, ypos, row, dwo);
    }

    /// <inheritdoc/>
    public override long Clicked(long xpos, long ypos, long row, IDataWindowObject dwo)
    {
        CallLog.Record("Event Clicked", xpos, ypos, row, dwo);
        return ClickedHandler is null
            ? base.Clicked(xpos, ypos, row, dwo)
            : ClickedHandler(xpos, ypos, row, dwo);
    }

    /// <inheritdoc/>
    public override long EditChanged(long row, IDataWindowObject dwo, string data)
    {
        CallLog.Record("Event EditChanged", row, dwo, data);
        return EditChangedHandler is null
            ? base.EditChanged(row, dwo, data)
            : EditChangedHandler(row, dwo, data);
    }

    /// <inheritdoc/>
    public override long ItemFocusChanged(long row, IDataWindowObject dwo)
    {
        CallLog.Record("Event ItemFocusChanged", row, dwo);
        return ItemFocusChangedHandler is null
            ? base.ItemFocusChanged(row, dwo)
            : ItemFocusChangedHandler(row, dwo);
    }

    /// <inheritdoc/>
    public override long ItemChanged(long row, IDataWindowObject dwo, string data)
    {
        CallLog.Record("Event ItemChanged", row, dwo, data);
        return ItemChangedHandler is null
            ? base.ItemChanged(row, dwo, data)
            : ItemChangedHandler(row, dwo, data);
    }

    /// <inheritdoc/>
    public override long OnDoItemChange(long row, IDataWindowObject dwo, string data)
    {
        CallLog.Record("Event OnDoItemChange", row, dwo, data);
        return DoItemChangeHandler is null
            ? base.OnDoItemChange(row, dwo, data)
            : DoItemChangeHandler(row, dwo, data);
    }

    /// <inheritdoc/>
    public override void OnDoItemChanged(long row, IDataWindowObject dwo)
    {
        CallLog.Record("Event OnDoItemChanged", row, dwo);

        if (DoItemChangedHandler is null)
        {
            base.OnDoItemChanged(row, dwo);
        }
        else
        {
            DoItemChangedHandler(row, dwo);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// MAY ANSWER <see langword="null"/>, and the coercion at <c>se_cst_dw.sru:L344</c> is deliberately
    /// NOT performed here - it belongs to the code under test, at the point the oracle performs it.
    /// Defaulting to <c>0</c> in this member would make that ported line unreachable.
    /// </remarks>
    public override long? ItemError(long row, IDataWindowObject dwo, string data)
    {
        CallLog.Record("Event ItemError", row, dwo, data);
        return ItemErrorHandler is null
            ? base.ItemError(row, dwo, data)
            : ItemErrorHandler(row, dwo, data);
    }

    /// <inheritdoc/>
    public override long LoseFocus()
    {
        CallLog.Record("Event LoseFocus");
        return LoseFocusHandler is null ? base.LoseFocus() : LoseFocusHandler();
    }

    /// <inheritdoc/>
    public override long GetFocus()
    {
        CallLog.Record("Event GetFocus");
        return GetFocusHandler is null ? base.GetFocus() : GetFocusHandler();
    }

    // ==============================================================================================
    //  INTERNALS
    // ==============================================================================================

    /// <summary>
    /// Resolves a column NAME to its id and reads the primary-buffer value, or answers
    /// <see langword="null"/> when either the column or the row is unknown.
    /// </summary>
    /// <remarks>
    /// NULL FOR AN UNKNOWN COLUMN OR ROW RATHER THAN A RAISE, because null is an ORDINARY outcome on this
    /// path: the ported comparisons at <c>n_cst_dwsvc_rowselect.sru:L205</c>, <c>:L211</c>, <c>:L217</c>,
    /// <c>:L232</c>, <c>:L234</c> and <c>:L236</c> all treat a null item as "not equal", so a double that
    /// raised would make a legitimate branch unreachable.
    /// </remarks>
    private object? ReadNamedItem(long row, string column)
    {
        FakeDataWindowObjectDefinition? definition = FindObject(column);
        if (definition is null)
        {
            return null;
        }

        List<FakeBufferRow> primary = BufferOf(DwBuffer.Primary);
        if (!OneBasedRows.IsInRange(row, primary.Count))
        {
            return null;
        }

        return primary[OneBasedRows.ToListIndex(row)].Values
            .TryGetValue(definition.Id, out object? value)
            ? value
            : null;
    }

    /// <summary>
    /// Resolves a column NAME to its id and stores the value in the primary buffer.
    /// </summary>
    /// <returns>
    /// <see cref="SetItemResult"/> on success, or <c>-1</c> when the column or row is unknown - the same
    /// failure code <see cref="StoreItem(long, long, object?)"/> answers for an out-of-range row.
    /// </returns>
    private int StoreNamedItem(long row, string column, object? value)
    {
        FakeDataWindowObjectDefinition? definition = FindObject(column);
        return definition is null ? -1 : StoreItem(row, definition.Id, value);
    }

    /// <summary>
    /// The shared write path behind all seven <c>SetItem</c> overloads.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnId">The column number.</param>
    /// <param name="value">The value, boxed as handed over.</param>
    /// <returns><c>-1</c> when the row does not exist; otherwise <see cref="SetItemResult"/>.</returns>
    /// <remarks>
    /// NO VALIDATION OF ANY KIND. The value is not checked against the column's type, is not coerced and
    /// is not rejected, because the code under test relies on the host being permissive - the restore path
    /// at <c>se_cst_dw.sru:L219</c> and <c>:L375</c> writes back an <c>any</c> and must land. The
    /// out-of-range answer of <c>-1</c> is the one thing that is enforced, and it is enforced because a
    /// real DataWindow enforces it.
    /// </remarks>
    private int StoreItem(long row, long columnId, object? value)
    {
        List<FakeBufferRow> primary = BufferOf(DwBuffer.Primary);
        if (!OneBasedRows.IsInRange(row, primary.Count))
        {
            return -1;
        }

        primary[OneBasedRows.ToListIndex(row)].Values[columnId] = value;
        return SetItemResult;
    }

    /// <summary>
    /// Records a READ, but only when <see cref="RecordsReads"/> is on.
    /// </summary>
    private void RecordRead(string member, params object?[]? arguments)
    {
        if (RecordsReads)
        {
            CallLog.Record(member, arguments);
        }
    }

    /// <summary>
    /// The row list behind one buffer.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="buffer"/> is not one of the three published members. A value outside the enum can
    /// only arrive through a cast, which is a programming fault rather than a data condition.
    /// </exception>
    private List<FakeBufferRow> BufferOf(DwBuffer buffer)
    {
        return _buffers.TryGetValue(buffer, out List<FakeBufferRow>? rows)
            ? rows
            : throw new ArgumentOutOfRangeException(
                nameof(buffer),
                buffer,
                "Only Primary, Delete and Filter are published buffers.");
    }

    /// <summary>
    /// One existing row of one buffer, for a write.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The row does not exist.</exception>
    private FakeBufferRow RowAt(DwBuffer buffer, long row)
    {
        List<FakeBufferRow> rows = BufferOf(buffer);
        if (!OneBasedRows.IsInRange(row, rows.Count))
        {
            throw new ArgumentOutOfRangeException(
                nameof(row),
                row,
                "The "
                    + buffer.ToString()
                    + " buffer holds "
                    + rows.Count.ToString(CultureInfo.InvariantCulture)
                    + " row(s), numbered from 1.");
        }

        return rows[OneBasedRows.ToListIndex(row)];
    }

    /// <summary>
    /// The declared data columns, in declaration order.
    /// </summary>
    private List<FakeDataWindowObjectDefinition> DataColumns()
    {
        List<FakeDataWindowObjectDefinition> columns = [];
        foreach (FakeDataWindowObjectDefinition candidate in _objects)
        {
            if (string.Equals(
                    candidate.Type,
                    FakeDataWindowObjectDefinition.ColumnType,
                    StringComparison.OrdinalIgnoreCase))
            {
                columns.Add(candidate);
            }
        }

        return columns;
    }

    /// <summary>
    /// The answer to <c>Describe("DataWindow.Objects")</c>.
    /// </summary>
    /// <returns>Every object name, each FOLLOWED by a tab.</returns>
    /// <remarks>
    /// TAB-TERMINATED RATHER THAN TAB-SEPARATED, AND THAT IS DELIBERATE. The oracle's consumer is a
    /// <c>do while (nPos &gt; 0)</c> loop that finds a tab, takes the text before it, and continues
    /// [<c>n_cst_dwsvc.sru:L697-L712</c>] - so a name not followed by a tab is never processed. Emitting a
    /// trailing tab is what makes every declared object reachable through that loop. A suite that wants to
    /// explore the other shape teaches it through <see cref="SetDescribe(string, string)"/> rather than
    /// changing this.
    /// </remarks>
    private string ObjectNameList()
    {
        StringBuilder names = new();
        foreach (FakeDataWindowObjectDefinition candidate in _objects)
        {
            names.Append(candidate.Name).Append('\t');
        }

        return names.ToString();
    }
}

/// <summary>
/// A concrete, recording double for <see cref="DataWindowServiceBase"/> - the host-facing half of
/// <c>n_cst_dwsvc</c> - so the attachment and enablement protocols are drivable.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS TYPE IS NEEDED AT ALL. <see cref="DataWindowServiceBase"/> is abstract yet declares no
/// abstract member, and three of the members a suite has to exercise are PROTECTED:
/// <c>OnEnable</c>, both <c>GetDataWindowObject</c> overloads and <c>RequireHost</c>. A concrete
/// subclass is the only way to reach them, and it is also the only way to SCRIPT the enablement veto.
/// </para>
/// <para>
/// <see cref="ForceEnabled(bool)"/> IS THE ONE MEMBER THAT LOOKS SUSPICIOUS AND IS NOT. The idempotent
/// early-out at <c>n_cst_dwsvc.sru:L89</c> returns success WITHOUT raising <c>OnEnable</c> when the
/// requested state already matches, so proving that behaviour requires putting the service into a state
/// without going through <c>of_setenabled</c>. The legacy setter is <c>protectedwrite</c>, which the
/// contract reproduces as a protected setter; this member is the derived type exercising exactly that
/// access, which is the access the legacy grants.
/// </para>
/// <para>
/// A VETO MAPS TO FAILURE, NOT TO PREVENTION, AND THAT QUIRK IS THE SERVICE BASE'S OWN. The handler
/// signals with the bare numeral <c>1</c> and <c>SetEnabled</c> translates it into <c>RetCode.FAILED</c>
/// [<c>:L90</c>], so a caller testing for prevention will not see the veto. This double does not
/// interfere with that: <see cref="OnEnableHandler"/> returns the raw signal and the base class performs
/// the translation, exactly as the oracle does.
/// </para>
/// </remarks>
public sealed class FakeDataWindowService : DataWindowServiceBase
{
    /// <summary>
    /// Creates the double.
    /// </summary>
    /// <param name="callLog">
    /// The log to record into, or <see langword="null"/> to keep a private one. A suite that wants the
    /// service's attachment and enablement calls interleaved with the host's own passes the host's
    /// <see cref="FakeDataWindowHost.CallLog"/>.
    /// </param>
    public FakeDataWindowService(DataWindowCallLog? callLog = null)
    {
        CallLog = callLog ?? new DataWindowCallLog();
    }

    /// <summary>
    /// The log this service records into.
    /// </summary>
    public DataWindowCallLog CallLog { get; }

    /// <summary>
    /// Scripts <c>OnEnable</c>. The parameter is the state being requested; RETURN <c>1</c> TO VETO, and
    /// any other value to allow. An unset handler allows the change, which is what a PowerBuilder event
    /// with no script attached does.
    /// </summary>
    public Func<bool, long>? OnEnableHandler { get; set; }

    /// <summary>
    /// Puts this service into a state directly, bypassing <c>SetEnabled</c> and therefore bypassing
    /// <c>OnEnable</c> - see this type's remarks for why that is required rather than convenient.
    /// </summary>
    /// <param name="enabled">The state to adopt.</param>
    public void ForceEnabled(bool enabled)
    {
        Enabled = enabled;
    }

    /// <summary>
    /// Reaches the protected <c>GetDataWindowObject(in string)</c> so a suite can assert its TWO DISTINCT
    /// failure modes.
    /// </summary>
    /// <param name="dwoName">The object name to resolve.</param>
    /// <returns>
    /// The handle, or <see langword="null"/> when the host has no valid object model
    /// [<c>n_cst_dwsvc.sru:L119</c>].
    /// </returns>
    /// <exception cref="InvalidOperationException">This service has not been attached to a host yet.</exception>
    /// <exception cref="KeyNotFoundException">
    /// The host declares no object of that name. The exception is allowed to PROPAGATE rather than being
    /// translated into the null above, because a caller that received null could not tell "the DataWindow
    /// has no object model" from "you asked for a column that is not there", and the legacy can.
    /// </exception>
    public IDataWindowObject? ResolveDataWindowObject(string dwoName)
    {
        CallLog.Record("ResolveDataWindowObject", dwoName);
        return GetDataWindowObject(dwoName);
    }

    /// <summary>
    /// Reaches the protected <c>GetDataWindowObject(in long)</c>, which composes the positional
    /// <c>"#n"</c> name and delegates - the behaviour being the delegation itself
    /// [<c>n_cst_dwsvc.sru:L97</c>].
    /// </summary>
    /// <param name="columnNumber">The one-based column number.</param>
    /// <returns>The handle, or <see langword="null"/> when the host has no valid object model.</returns>
    public IDataWindowObject? ResolveDataWindowObject(long columnNumber)
    {
        CallLog.Record("ResolveDataWindowObject", columnNumber);
        return GetDataWindowObject(columnNumber);
    }

    /// <summary>
    /// Reaches the protected <c>RequireHost</c>, so a suite can assert the fail-fast not-attached fault.
    /// </summary>
    /// <returns>The attached host.</returns>
    /// <exception cref="InvalidOperationException">This service has not been attached to a host yet.</exception>
    public DataWindowServiceHost RequireAttachedHost() => RequireHost();

    /// <inheritdoc/>
    /// <remarks>
    /// Records the attachment and then performs it through the base implementation, which binds the host
    /// and lifts the broker off it in that order - both or neither, with no partially attached state
    /// [<c>n_cst_dwsvc.sru:L85-L86</c>].
    /// </remarks>
    public override void OnInit(DataWindowServiceHost dw)
    {
        CallLog.Record("Event OnInit", dw);
        base.OnInit(dw);
    }

    /// <inheritdoc/>
    protected override long OnEnable(bool enabled)
    {
        CallLog.Record("Event OnEnable", enabled);
        return OnEnableHandler is null ? base.OnEnable(enabled) : OnEnableHandler(enabled);
    }
}

/// <summary>
/// The fixture factories: hosts and child DataWindows seeded from the legacy <c>.srd</c> definitions,
/// TRANSCRIBED AS LITERALS with the locator each value came from.
/// </summary>
/// <remarks>
/// <para>
/// CONSTRAINT C-C IN PRACTICE. The legacy tree is read-only and is the behavioural oracle, so nothing here
/// reads it: every value below is a C# literal carrying a <c>ws_objects/**</c> line locator in a comment.
/// No <c>.srd</c> file is parsed at build time or at run time, which is also what keeps these fixtures
/// working inside a container where the root <c>.dockerignore</c> excludes <c>ws_objects/</c> from the
/// build context entirely.
/// </para>
/// <para>
/// EACH FACTORY CLEARS THE CALL LOG BEFORE HANDING THE HOST BACK, so a suite that arranges a fixture and
/// then acts sees a log containing only what the subject did. Seeding uses the unrecorded setup mutators
/// anyway; the clear is belt and braces against a future factory reaching for a contract member.
/// </para>
/// </remarks>
public static class FakeDataWindowFixtures
{
    /// <summary>
    /// The primary fixture: <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd</c>, the ONLY updatable
    /// DataWindow in the whole repository and therefore the golden master for the retrieval, validation
    /// and update triple.
    /// </summary>
    /// <param name="eventful">An optional broker to publish as the host's own.</param>
    /// <returns>The seeded host, with SIX COLUMNS AND NO ROWS.</returns>
    /// <remarks>
    /// <para>
    /// NO ROWS BY DEFAULT, DELIBERATELY. The oracle's own definition carries no <c>data(...)</c> line - it
    /// retrieves from SQLite - so an empty buffer is the faithful starting state. A suite adds rows with
    /// <see cref="FakeDataWindowHost.AddRow(object?[])"/>, which returns the one-based row number of the
    /// row it added.
    /// </para>
    /// <para>
    /// THE THREE FORMAT SPELLINGS ARE REPRODUCED AS THEY ARE. The six columns declare
    /// <c>format="[general]"</c> in lower case [<c>:L21-L26</c>] while the footer computed field declares
    /// <c>format="[GENERAL]"</c> in upper case [<c>:L27</c>]. Harmonising them would be exactly the silent
    /// correction constraint C-B forbids, and the expression-evaluator suite needs the upper-case pair
    /// exactly as declared.
    /// </para>
    /// </remarks>
    public static FakeDataWindowHost CreateCompanyFixture(EventBroker? eventful = null)
    {
        FakeDataWindowHost host = new(eventful)
        {
            // dw_sqlite.srd:L3 - datawindow(... processing=1 ...). 1 is the GRID presentation style, which
            // is also what makes the focusless row move at se_cst_dw.sru:L152-L158 reachable.
            Processing = "1",

            // dw_sqlite.srd:L14 - retrieve, update table, concurrency mode, key handling and sort. The
            // sort's TRAILING SPACE is part of the literal and is carried verbatim.
            RetrieveStatement = "SELECT * FROM COMPANY",
            UpdateTable = "COMPANY",
            UpdateWhere = "1",
            UpdateKeyInPlace = "no",
            TableSort = "age A salary A ",
        };

        // dw_sqlite.srd:L15-L20 - the six header text objects, declared BEFORE the columns exactly as the
        // definition declares them. They consume no column number.
        host.AddTextObject("id_t", "header");
        host.AddTextObject("name_t", "header");
        host.AddTextObject("age_t", "header");
        host.AddTextObject("address_t", "header");
        host.AddTextObject("salary_t", "header");
        host.AddTextObject("birth_t", "header");

        // dw_sqlite.srd:L8 + :L21 - id, the key AND identity column. Column number 1.
        FakeDataWindowObjectDefinition id = host.AddColumn("id", FakeColumnType.Number);
        id.Update = true;
        id.UpdateWhereClause = true;
        id.Key = true;
        id.Identity = true;
        id.Format = "[general]";
        id.TabSequence = "10";

        // dw_sqlite.srd:L9 + :L22 - name, char(100). Column number 2.
        FakeDataWindowObjectDefinition name = host.AddColumn("name", FakeColumnType.CharOf(100));
        name.Update = true;
        name.UpdateWhereClause = true;
        name.Format = "[general]";
        name.TabSequence = "20";

        // dw_sqlite.srd:L10 + :L23 - age, number. Column number 3.
        FakeDataWindowObjectDefinition age = host.AddColumn("age", FakeColumnType.Number);
        age.Update = true;
        age.UpdateWhereClause = true;
        age.Format = "[general]";
        age.TabSequence = "30";

        // dw_sqlite.srd:L11 + :L24 - address, char(200). Column number 4. Note the DataWindow declares 200
        // characters while the only DDL in the repository declares 50; that mismatch is a documented legacy
        // defect preserved on the Persistence side and is recorded here so the width is not "corrected".
        FakeDataWindowObjectDefinition address = host.AddColumn("address", FakeColumnType.CharOf(200));
        address.Update = true;
        address.UpdateWhereClause = true;
        address.Format = "[general]";
        address.TabSequence = "40";

        // dw_sqlite.srd:L12 + :L25 - salary, decimal(2). Column number 5.
        FakeDataWindowObjectDefinition salary = host.AddColumn("salary", FakeColumnType.DecimalOf(2));
        salary.Update = true;
        salary.UpdateWhereClause = true;
        salary.Format = "[general]";
        salary.TabSequence = "50";

        // dw_sqlite.srd:L13 + :L26 - birth, date, with an edit mask and therefore the editmask edit style.
        // "date" truncates to "date" and reaches a DIFFERENT coercion arm from "datetime", which truncates
        // to "datet" - see FakeColumnType.
        FakeDataWindowObjectDefinition birth = host.AddColumn("birth", FakeColumnType.Date);
        birth.Update = true;
        birth.UpdateWhereClause = true;
        birth.Format = "[general]";
        birth.TabSequence = "60";
        birth.EditStyle = "editmask";
        birth.EditMaskMask = "yyyy-mm-dd";

        // dw_sqlite.srd:L27 - the footer computed field. A PAGE-SCOPED aggregate, and the format is UPPER
        // CASE here while every column's is lower case.
        FakeDataWindowObjectDefinition compute = host.AddComputedField(
            "compute_1",
            "footer",
            "sum(salary for page)");
        compute.Format = "[GENERAL]";

        host.CallLog.Clear();
        return host;
    }

    /// <summary>
    /// The general service fixture: <c>ws_objects/pfw.tests.pbl.src/dw_test_dwsvc.srd</c>, six columns and
    /// nine rows, with a drop-down list-box column and TWO drop-down DataWindow columns.
    /// </summary>
    /// <param name="eventful">An optional broker to publish as the host's own.</param>
    /// <returns>The seeded host.</returns>
    /// <remarks>
    /// Modelled only as far as a consuming suite reads it, per the folder brief. The child DataWindow the
    /// two DDDW columns name is NOT registered here - call
    /// <see cref="AttachDropDownChild(FakeDataWindowHost, string)"/> for that, because a column naming a
    /// child that fails to materialise is a real state the oracle guards against
    /// [<c>n_cst_dwsvc.sru:L592-L593</c>].
    /// </remarks>
    public static FakeDataWindowHost CreateServiceFixture(EventBroker? eventful = null)
    {
        FakeDataWindowHost host = new(eventful)
        {
            // dw_test_dwsvc.srd:L14 - the definition declares a sort and no update table at all.
            TableSort = "n1 A n2 A n3 A ",
        };

        AddNumericServiceColumns(host, "[general]", "[general]", "[general]");

        // dw_test_dwsvc.srd:L11 + the s1 column declaration - a drop-down LIST BOX carrying a code table.
        FakeDataWindowObjectDefinition s1 = host.AddColumn("s1", FakeColumnType.CharOf(100));
        s1.UpdateWhereClause = true;
        s1.Format = "[general]";
        s1.TabSequence = "40";
        s1.EditStyle = "ddlb";
        s1.AddCodeTableEntry("新建", "NEW");
        s1.AddCodeTableEntry("确认", "CFD");
        s1.AddCodeTableEntry("审核", "ADT");

        // dw_test_dwsvc.srd:L12-L13 + the s2 and s3 column declarations - both drop-down DataWindows over
        // dw_test_dwsvc_dddw, displaying dsp and storing dat, with free text disallowed.
        DeclareDropDownColumn(host, "s2", "50");
        DeclareDropDownColumn(host, "s3", "60");

        // dw_test_dwsvc.srd:L15 - nine rows, n1/n2/n3/s1/s2/s3.
        host.AddRow(1.00m, 2.00m, 1.00m, "NEW", "NEW", "NEW");
        host.AddRow(1.00m, 2.00m, 2.00m, "CFD", "CFD", "CFD");
        host.AddRow(1.00m, 2.00m, 3.00m, "ADT", "ADT", "ADT");
        host.AddRow(2.00m, 3.00m, 4.00m, "NEW", "NEW", "NEW");
        host.AddRow(2.00m, 3.00m, 5.00m, "CFD", "CFD", "CFD");
        host.AddRow(2.00m, 3.00m, 6.00m, "ADT", "ADT", "ADT");
        host.AddRow(3.00m, 4.00m, 7.00m, "NEW", "NEW", "NEW");
        host.AddRow(3.00m, 4.00m, 8.00m, "CFD", "CFD", "CFD");
        host.AddRow(3.00m, 4.00m, 9.00m, "ADT", "ADT", "ADT");

        host.CallLog.Clear();
        return host;
    }

    /// <summary>
    /// The context-menu fixture: <c>ws_objects/pfw.tests.pbl.src/dw_test_dwsvc_contextmenu.srd</c>, which
    /// differs from the general service fixture in exactly two ways worth modelling.
    /// </summary>
    /// <param name="eventful">An optional broker to publish as the host's own.</param>
    /// <returns>The seeded host.</returns>
    /// <remarks>
    /// <para>
    /// DIFFERENCE 1 - <c>n3</c> IS A CHECKBOX over a <c>number</c> column, declaring
    /// <c>checkbox.on="1"</c> and <c>checkbox.off="0"</c> plus the matching code table. That is the branch
    /// selector at <c>n_cst_dwsvc.sru:L638</c>, where an edit style of <c>"checkbox"</c> sends the column
    /// value map down a completely different path from the code-table one.
    /// </para>
    /// <para>
    /// DIFFERENCE 2 - A THIRD FORMAT CASING. <c>n1</c> and <c>n2</c> declare
    /// <c>format="###,##0.00"</c> while <c>n3</c> declares <c>format="[General]"</c> in MIXED case -
    /// neither the lower case of the primary fixture's columns nor the upper case of its footer. All three
    /// spellings now exist across these fixtures and none is normalised (constraint C-B).
    /// </para>
    /// </remarks>
    public static FakeDataWindowHost CreateContextMenuFixture(EventBroker? eventful = null)
    {
        FakeDataWindowHost host = new(eventful)
        {
            TableSort = "n1 A n2 A n3 A ",
        };

        // dw_test_dwsvc_contextmenu.srd - n1 and n2 are decimal(2) formatted as numbers; n3 is a `number`
        // checkbox, so it is declared separately below rather than through the shared helper.
        FakeDataWindowObjectDefinition n1 = host.AddColumn("n1", FakeColumnType.DecimalOf(2));
        n1.UpdateWhereClause = true;
        n1.Format = "###,##0.00";
        n1.TabSequence = "10";
        n1.EditStyle = "editmask";
        n1.EditMaskMask = "###,##0.00";

        FakeDataWindowObjectDefinition n2 = host.AddColumn("n2", FakeColumnType.DecimalOf(2));
        n2.UpdateWhereClause = true;
        n2.Format = "###,##0.00";
        n2.TabSequence = "20";
        n2.EditStyle = "editmask";
        n2.EditMaskMask = "###,##0.00";

        FakeDataWindowObjectDefinition n3 = host.AddColumn("n3", FakeColumnType.Number);
        n3.UpdateWhereClause = true;
        n3.Format = "[General]";
        n3.TabSequence = "30";
        n3.EditStyle = "checkbox";
        n3.CheckBoxOn = "1";
        n3.CheckBoxOff = "0";

        // The checkbox column's own code table, values="~t1/~t0" - BOTH DISPLAYS ARE EMPTY, which is
        // reproduced rather than filled in.
        n3.AddCodeTableEntry(string.Empty, "1");
        n3.AddCodeTableEntry(string.Empty, "0");

        FakeDataWindowObjectDefinition s1 = host.AddColumn("s1", FakeColumnType.CharOf(100));
        s1.UpdateWhereClause = true;
        s1.Format = "[general]";
        s1.TabSequence = "40";
        s1.EditStyle = "ddlb";
        s1.AddCodeTableEntry("新建", "NEW");
        s1.AddCodeTableEntry("确认", "CFD");
        s1.AddCodeTableEntry("审核", "ADT");

        DeclareDropDownColumn(host, "s2", "50");
        DeclareDropDownColumn(host, "s3", "60");

        // dw_test_dwsvc_contextmenu.srd - nine rows, with n3 carrying the checkbox's 0/1 rather than a
        // decimal.
        host.AddRow(1.00m, 2.00m, 0L, "NEW", "NEW", "NEW");
        host.AddRow(1.00m, 2.00m, 0L, "CFD", "CFD", "CFD");
        host.AddRow(1.00m, 2.00m, 1L, "ADT", "ADT", "ADT");
        host.AddRow(2.00m, 3.00m, 0L, "NEW", "NEW", "NEW");
        host.AddRow(2.00m, 3.00m, 0L, "CFD", "CFD", "CFD");
        host.AddRow(2.00m, 3.00m, 1L, "ADT", "ADT", "ADT");
        host.AddRow(3.00m, 4.00m, 0L, "NEW", "NEW", "NEW");
        host.AddRow(3.00m, 4.00m, 0L, "CFD", "CFD", "CFD");
        host.AddRow(3.00m, 4.00m, 1L, "ADT", "ADT", "ADT");

        host.CallLog.Clear();
        return host;
    }

    /// <summary>
    /// The column-expression fixture: <c>ws_objects/pfw.tests.pbl.src/dw_test_dwsvc_columnexp.srd</c> -
    /// FIVE columns, all with <c>updatewhereclause=no</c>, and four rows of zeros with NULL text columns.
    /// </summary>
    /// <param name="eventful">An optional broker to publish as the host's own.</param>
    /// <returns>The seeded host.</returns>
    /// <remarks>
    /// THE NULLS ARE THE POINT OF THIS FIXTURE. Its <c>data(...)</c> line seeds <c>s1</c> and <c>s2</c> as
    /// NULL on every row, so it is the fixture that exercises the explicit null-and-null equality arm at
    /// <c>se_cst_dw.sru:L200-L201</c> and the expression engine's own empty-string-is-null handling. The
    /// nulls are seeded as nulls and are never replaced with empty strings (AAP 0.4.5.4).
    /// </remarks>
    public static FakeDataWindowHost CreateColumnExpressionFixture(EventBroker? eventful = null)
    {
        FakeDataWindowHost host = new(eventful)
        {
            TableSort = "n1 A n2 A n3 A ",
        };

        // dw_test_dwsvc_columnexp.srd - n1, n2 and n3 are decimal(2) with updatewhereclause=no, which is
        // the opposite of the general service fixture and is carried as declared.
        for (int position = 1; position <= 3; position++)
        {
            FakeDataWindowObjectDefinition numeric = host.AddColumn(
                "n" + position.ToString(CultureInfo.InvariantCulture),
                FakeColumnType.DecimalOf(2));
            numeric.UpdateWhereClause = false;
            numeric.Format = "[general]";
            numeric.TabSequence = (position * 10).ToString(CultureInfo.InvariantCulture);
            numeric.EditStyle = "editmask";
            numeric.EditMaskMask = "###,##0.00";
        }

        // dw_test_dwsvc_columnexp.srd - s1 and s2 are char(100), also updatewhereclause=no, and this
        // definition declares NO drop-down DataWindow on either of them.
        FakeDataWindowObjectDefinition s1 = host.AddColumn("s1", FakeColumnType.CharOf(100));
        s1.Format = "[general]";
        s1.TabSequence = "40";

        FakeDataWindowObjectDefinition s2 = host.AddColumn("s2", FakeColumnType.CharOf(100));
        s2.Format = "[general]";
        s2.TabSequence = "50";

        // dw_test_dwsvc_columnexp.srd - four rows: three zeros then two nulls, on every row.
        for (int row = 0; row < 4; row++)
        {
            host.AddRow(0m, 0m, 0m, null, null);
        }

        host.CallLog.Clear();
        return host;
    }

    /// <summary>
    /// The child DataWindow behind a drop-down DataWindow column:
    /// <c>ws_objects/pfw.tests.pbl.src/dw_test_dwsvc_dddw.srd</c> - two <c>char(100)</c> columns and three
    /// rows.
    /// </summary>
    /// <returns>The seeded child.</returns>
    /// <remarks>
    /// Its columns are <c>dsp</c> (displayed) and <c>dat</c> (stored)
    /// [<c>dw_test_dwsvc_dddw.srd:L8-L9</c>], and its three rows are the display/value pairs from its
    /// <c>data(...)</c> line [<c>:L11</c>]. That is the entire content a consuming suite reads, so nothing
    /// further is modelled.
    /// </remarks>
    public static FakeDataWindowChild CreateDropDownChildFixture()
    {
        FakeDataWindowChild child = new("dw_test_dwsvc_dddw");

        FakeDataWindowObjectDefinition display = child.AddColumn("dsp", FakeColumnType.CharOf(100));
        display.UpdateWhereClause = true;
        display.Format = "[general]";
        display.TabSequence = "10";

        FakeDataWindowObjectDefinition data = child.AddColumn("dat", FakeColumnType.CharOf(100));
        data.UpdateWhereClause = true;
        data.Format = "[general]";
        data.TabSequence = "20";

        // dw_test_dwsvc_dddw.srd:L11 - data("新建","NEW","确认","CFD","审核","ADT",)
        child.AddRow("新建", "NEW");
        child.AddRow("确认", "CFD");
        child.AddRow("审核", "ADT");

        return child;
    }

    /// <summary>
    /// Registers a freshly seeded <see cref="CreateDropDownChildFixture"/> against one of a host's
    /// drop-down DataWindow columns.
    /// </summary>
    /// <param name="host">The host whose column gains a child.</param>
    /// <param name="column">The column name, for example <c>"s2"</c>.</param>
    /// <returns>The child that was registered, so a suite can adjust or inspect it.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Separate from the fixture factories because registration and declaration are separate in the oracle
    /// too: a column can name a child that never materialises, which is the state
    /// <c>IsValidObject(dwc)</c> guards against at <c>n_cst_dwsvc.sru:L593</c>. Leaving a suite to opt in
    /// keeps both states reachable.
    /// </remarks>
    public static FakeDataWindowChild AttachDropDownChild(FakeDataWindowHost host, string column)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(column);

        FakeDataWindowChild child = CreateDropDownChildFixture();
        host.SetChild(column, child);
        return child;
    }

    /// <summary>
    /// Declares the three numeric columns the service fixtures share - <c>n1</c>, <c>n2</c> and
    /// <c>n3</c>, all <c>decimal(2)</c> with the numeric edit mask.
    /// </summary>
    private static void AddNumericServiceColumns(
        FakeDataWindowHost host,
        string firstFormat,
        string secondFormat,
        string thirdFormat)
    {
        string[] formats = [firstFormat, secondFormat, thirdFormat];

        for (int position = 1; position <= formats.Length; position++)
        {
            FakeDataWindowObjectDefinition numeric = host.AddColumn(
                "n" + position.ToString(CultureInfo.InvariantCulture),
                FakeColumnType.DecimalOf(2));

            numeric.UpdateWhereClause = true;
            numeric.Format = formats[position - 1];
            numeric.TabSequence = (position * 10).ToString(CultureInfo.InvariantCulture);
            numeric.EditStyle = "editmask";

            // The numeric columns of every service fixture declare editmask.mask="###,##0.00".
            numeric.EditMaskMask = "###,##0.00";
        }
    }

    /// <summary>
    /// Declares one drop-down DataWindow column over <c>dw_test_dwsvc_dddw</c>, displaying <c>dsp</c> and
    /// storing <c>dat</c>, with free text disallowed - the shape both <c>s2</c> and <c>s3</c> declare.
    /// </summary>
    private static void DeclareDropDownColumn(
        FakeDataWindowHost host,
        string name,
        string tabSequence)
    {
        FakeDataWindowObjectDefinition column = host.AddColumn(name, FakeColumnType.CharOf(100));

        column.UpdateWhereClause = true;
        column.Format = "[general]";
        column.TabSequence = tabSequence;
        column.EditStyle = "dddw";
        column.DropDownDataWindowName = "dw_test_dwsvc_dddw";
        column.DropDownDisplayColumn = "dsp";
        column.DropDownDataColumn = "dat";

        // dddw.allowedit=no as declared. The drop-down search oracle overrides it to yes at RUN TIME
        // [ws_objects/pfw.tests.pbl.src/w_test_dwsvc_dropdownsearch.srw:L46], which a suite reproduces by
        // assigning the property rather than by changing this declaration.
        column.DropDownAllowEdit = "no";
    }
}
