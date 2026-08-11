// ==================================================================================================
//  ItemChangeProtocolTests - PARITY MATRIX FAMILY 2: THE {0,1,2,3} ITEM-CHANGE ALPHABET
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST
//      services/dataservices-service/PowerFramework.DataServices/Domain/ItemChangeProtocol.cs
//
//  BEHAVIOURAL ORACLE - READ ONLY, NEVER AN EDIT TARGET (constraint C-C)
//      ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru
//        :L182-L254   `ondwnitemchange`, the routine under test, in full
//        :L187        the EID_ITEMCHANGE gate and its `return 0`
//        :L189-L190   the value snapshot and the item-status snapshot
//        :L192-L196   the four-step re-entrancy dance, and the stash at :L195
//        :L198-L202   the equality test, including the explicit null-and-null arm
//        :L204-L208   the re-read of the buffer and the nested `OnDwnChanging` raise
//        :L211-L251   the OUTER dispatch - FOUR arms
//        :L231-L244   the INNER coercion dispatch - SIX arms and NO default
//        :L253        `return rtCode`
//        :L256-L293   `ondoitemchange`, whose only live statement is :L292 and whose byte-length
//                     validation at :L280-L290 is COMMENTED OUT in the oracle
//        :L41-L43     EID_ROWFOCUSCHANGE = 1, EID_ITEMFOCUSCHANGE = 2, EID_ITEMCHANGE = 4
//        :L89, :L92, :L96   the three cross-event fields this routine reads and writes
//      ws_objects/pfw.tests.pbl.src/dw_sqlite.srd
//        :L8-L13      the six REAL column types of the primary fixture - number, char(100),
//                     number, char(200), decimal(2), date - whose `Left(_,5)` tokens are
//                     numbe, char(, numbe, char(, decim, date
//        :L14         updatewhere=1 updatekeyinplace=no, which is why this fixture is the
//                     golden-master for the whole retrieval/validation/update triple
//      ws_objects/pfw.shared.pbl.src/retcode.sru
//        :L40-L45     SUCCESS = 0, ALLOW = 0, PREVENT = 1, FAILED = -1, CANCELED = CANCELLED = -2
//
//  ================================================================================================
//  USER RULES: NONE
//  ================================================================================================
//  `review_rules` returns exactly "No user rules provided." No user-specified rule governs this
//  file, none is invented here, and no assertion below exists because a coding guideline demanded
//  it. The enterprise-standard baseline of AAP 0.7.2 applies in their place, and the binding
//  constraints are the non-rule constraints of AAP 0.7.3, which this file honours as follows:
//
//    C-B / G2  PRESERVE BEHAVIOUR EXACTLY, REPLICATING DOCUMENTED DEFECTS RATHER THAN CORRECTING
//              THEM. This is the dominant constraint here and it inverts the usual instinct: every
//              arm below is asserted AS THE ORACLE BEHAVES, including the arms that look like bugs.
//              A test that encoded the "sensible" outcome would force the implementation to violate
//              C-B, so where intuition and the oracle disagree the oracle wins and the disagreement
//              is named in a comment rather than quietly resolved.
//    C-C       The legacy tree is read only and is the oracle. Nothing under ws_objects/** is
//              written, moved or reformatted by this file; it is read and cited.
//    C-K       Every arm's expectation carries its exact line locator.
//    C-H       The alphabet and all six coercion arms are covered, plus both sub-cases of the one
//              arm that branches internally.
//
//  ================================================================================================
//  NAMING: THIS FILE DECLARES NO SCREAMING_SNAKE IDENTIFIER
//  ================================================================================================
//  The repository-root .editorconfig carries a section keyed to
//  `.../PowerFramework.DataServices/Domain/ItemChangeProtocol.cs` that sets CA1707 and IDE1006 to
//  none so preserved legacy constant spellings may be declared there. THERE IS NO SUCH SECTION FOR
//  ANY TEST-PROJECT PATH, so no preserved spelling may be DECLARED here. Every SCREAMING_SNAKE name
//  that appears below - `EventGate.EID_ITEMCHANGE`, `RetCode.PREVENT`, `RetCode.CANCELLED` - is a
//  USAGE of a symbol declared elsewhere under a matching suppression, and CA1707 reports
//  declarations only. Enum members are referenced by name rather than by literal throughout, which
//  is the other half of the same requirement.
//
//  ================================================================================================
//  HOW THESE TESTS ARE BUILT, AND WHY
//  ================================================================================================
//  * PLAIN XUNIT ASSERTIONS ONLY. There is deliberately no mocking library and no fluent assertion
//    library anywhere in the dependency inventory (AAP 0.5.3), so the collaborators below are hand
//    written test doubles and every expectation is a plain `Assert`.
//  * TABLE DRIVEN AS THEORIES WITH MEMBER DATA (AAP 0.6.7), one row per arm.
//  * EVERY ROW PINS EXACTLY ONE VALUE. There is no assertion anywhere below that accepts either of
//    two outcomes: an "either 1 or 2" expectation would pass against both the oracle's behaviour and
//    its opposite, which is the one thing a parity matrix must never do.
//  * THEORY PARAMETERS ARE PRIMITIVES, NOT THE INTERNAL ENUM. `ItemChangeResult` is `internal` by
//    design - the service's published surface is contract C-03 - and a `public static TheoryData<>`
//    over an internal type would not compile. Rows therefore carry the raw numerals the oracle's
//    `choose case` compares, which is also what makes the generated test names legible, and each
//    row's body maps the numeral onto the named member so no literal is asserted against a literal.
//  * ONE INTERLEAVED CALL LOG. Both doubles record into the SAME `DataWindowCallLog` the fake host
//    records into, so raises and host calls appear in one ordered sequence and the ordering claims
//    of :L189-L190 and :L204-L208 can be asserted positionally rather than inferred.
//  * BUFFER MUTATION BY A HANDLER IS UNRECORDED ON PURPOSE. `FakeDataWindowHost.SetBufferValue` and
//    `SetBufferItemStatus` are arrangement, not contract calls, so a handler that mutates the buffer
//    mid-protocol leaves the log clean and every `SetItem`/`SetItemStatus` record in it is
//    attributable to the routine under test.
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;

using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Validators;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;

using Xunit;

using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
using ItemStatus = PowerFramework.Contracts.Common.V1.ItemStatus;

// The wire half of the matched pair. Aliased rather than imported by namespace because
// `PowerFramework.Contracts.DataServices.V1.ItemChangeResult` and
// `PowerFramework.DataServices.Domain.ItemChangeResult` are the same SIMPLE name, and the collision
// is the very thing Section 1 asserts agreement across - so the two must stay separately nameable.
using WireItemChangeResult = PowerFramework.Contracts.DataServices.V1.ItemChangeResult;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Parity matrix for the item-change micro-protocol of
/// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L182-L253</c>, ported to
/// <c>Domain/ItemChangeProtocol.cs</c>.
/// </summary>
/// <remarks>
/// Sections map one-to-one onto the oracle: the alphabet itself, the ordered prologue, the equality
/// test, the inequality re-read, the four-arm outer dispatch, the six-arm inner coercion, and the
/// dormant commented validation that must stay dormant.
/// </remarks>
public sealed class ItemChangeProtocolTests
{
    // ==============================================================================================
    //  SHARED VOCABULARY
    //  --------------------------------------------------------------------------------------------
    //  The call-log member names the fake host and the two doubles below record under. Declared once
    //  so an assertion cannot drift from the recording, and PascalCase because this file may declare
    //  no SCREAMING_SNAKE identifier (see the header).
    // ==============================================================================================

    /// <summary><c>Event OnDoItemChange(row,dwo,data)</c> - the raise at <c>se_cst_dw.sru:L194</c>.</summary>
    private const string DoItemChangeRaise = "Event OnDoItemChange";

    /// <summary><c>Event OnDwnChanging(row,dwo,data)</c> - the NESTED raise at <c>:L207</c>.</summary>
    private const string ChangingRaise = "Event OnDwnChanging";

    /// <summary><c>Event OnDoItemChanged(row,dwo)</c> - the raise at <c>:L247</c>.</summary>
    private const string ChangedRaise = "Event OnDoItemChanged";

    /// <summary><c>Event ItemChanged(row,dwo,data)</c> - the semantic event <c>:L292</c> delegates to.</summary>
    private const string ItemChangedRaise = "Event ItemChanged";

    /// <summary>
    /// The signature the RESTORE writes under. <c>:L219</c> passes the snapshot as PowerScript
    /// <c>any</c>, which ports to <c>object?</c>, so the restore is the only path that reaches the
    /// <c>SetItem(long, long, object?)</c> overload - and that makes a restore distinguishable in the
    /// log from a coercion, which always reaches one of the six TYPED overloads.
    /// </summary>
    private const string RestoreSetItem = "SetItem(object?)";

    /// <summary>The member name every <c>SetItem</c> overload records under.</summary>
    private const string SetItemMember = "SetItem";

    /// <summary><c>SetItemStatus(row,Long(dwo.ID),Primary!,orgStatus)</c> - the restore at <c>:L220</c>.</summary>
    private const string SetItemStatusMember = "SetItemStatus";

    /// <summary><c>GetItemStatus(row,Long(dwo.ID),Primary!)</c> - the snapshot read at <c>:L190</c>.</summary>
    private const string GetItemStatusMember = "GetItemStatus";

    /// <summary>Row-data sentinel for the theory rows that expect NO <c>SetItem</c> call at all.</summary>
    private const string NoSetItemExpected = "(no SetItem call)";

    /// <summary>The one row every test below operates on. One based, exactly as the oracle counts.</summary>
    private const long FixtureRow = 1L;

    /// <summary>
    /// The dialog text of the DORMANT commented validation at <c>se_cst_dw.sru:L286</c>, carried here
    /// only so Section 7 can assert it is never produced. Reproducing the string in a negative
    /// assertion is the opposite of reviving the path.
    /// </summary>
    private const string DormantOverLengthMessagePrefix = "超出最大允许的长度";

    // ==============================================================================================
    //  THE TWO COLLABORATOR DOUBLES
    //  --------------------------------------------------------------------------------------------
    //  `Domain/ItemChangeProtocol.cs` declares both seams itself and takes them as arguments, so the
    //  whole routine is drivable with no live DataWindow, no gRPC channel and no session registry.
    //  Both doubles are PRIVATE NESTED TYPES: they implement `internal` interfaces, and a private
    //  nested type is more restrictive than internal, so nothing is leaked out of the assembly.
    // ==============================================================================================

    /// <summary>
    /// The session-state double - the port of the three legacy instance fields
    /// (<c>se_cst_dw.sru:L89</c>, <c>:L92</c>, <c>:L96</c>) as plain, honest field storage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PLAIN STORAGE ON PURPOSE. Two of the three members are written and read back inside a single
    /// invocation and the third is written for a LATER event to consume, so a double that clamped,
    /// validated or recomputed on the way through would break the protocol rather than harden it. The
    /// real implementer, <c>Domain/ValidationSession.cs</c>, is driven directly in Section 2 as well,
    /// so this double never becomes the only witness.
    /// </para>
    /// </remarks>
    private sealed class RecordingSessionState : IItemChangeSessionState
    {
        /// <summary>
        /// The composable suppression mask - the port of <c>long _nDisabledEvent</c>
        /// (<c>se_cst_dw.sru:L89</c>). Zero suppresses nothing, so the gate at <c>:L187</c> falls
        /// through and the rest of the routine becomes reachable.
        /// </summary>
        public uint DisabledEvent { get; init; }

        /// <summary>
        /// The re-entrancy flag - the port of <c>boolean _bDoItemChange</c>
        /// (<c>se_cst_dw.sru:L92</c>). Settable because <c>:L193</c> sets it and <c>:L196</c> puts the
        /// SAVED value back, and initialisable because the whole point of <c>:L196</c> is that the
        /// previous value may already have been <see langword="true"/>.
        /// </summary>
        public bool DoItemChange { get; set; }

        /// <summary>
        /// The stash - the port of <c>long _nItemChangeRetCode</c> (<c>se_cst_dw.sru:L96</c>). Holds
        /// the RAW, UNCLASSIFIED handler code written at <c>:L195</c>, which is why it is
        /// <see langword="long"/> rather than the enum: a handler returning <c>42</c> stashes
        /// <c>42</c>, and the validation-error event compares the stash against <c>1</c> and <c>3</c>
        /// numerically.
        /// </summary>
        public long ItemChangeRetCode { get; set; }
    }

    /// <summary>
    /// The event-sink double for the three events <c>ondwnitemchange</c> raises -
    /// <c>:L194</c>, <c>:L207</c> and <c>:L247</c>.
    /// </summary>
    /// <remarks>
    /// Records every raise into the shared <see cref="DataWindowCallLog"/> so the raise order and the
    /// host-call order interleave in one sequence. The semantic handler is supplied as a delegate
    /// because several arms need it to mutate the buffer, to observe the re-entrancy flag, or to
    /// re-enter the protocol.
    /// </remarks>
    private sealed class RecordingEventSink : IItemChangeEventSink
    {
        private readonly DataWindowCallLog _log;
        private readonly Func<long, IDataWindowObject, string?, long> _onDoItemChange;

        /// <summary>Creates a sink whose semantic handler returns <paramref name="handlerResult"/> unconditionally.</summary>
        internal RecordingEventSink(DataWindowCallLog log, long handlerResult)
            : this(log, (_, _, _) => handlerResult)
        {
        }

        /// <summary>Creates a sink whose semantic handler is <paramref name="onDoItemChange"/>.</summary>
        internal RecordingEventSink(
            DataWindowCallLog log,
            Func<long, IDataWindowObject, string?, long> onDoItemChange)
        {
            _log = log;
            _onDoItemChange = onDoItemChange;
        }

        /// <summary>How many times <c>:L194</c> raised the semantic handler. Always exactly one on a non-gated path.</summary>
        internal int DoItemChangeRaiseCount { get; private set; }

        /// <summary>How many times <c>:L207</c> raised the nested changing event. Zero unless the buffer value changed.</summary>
        internal int ChangingRaiseCount { get; private set; }

        /// <summary>How many times <c>:L247</c> raised the changed event. Only the <c>case else</c> arm reaches it.</summary>
        internal int ChangedRaiseCount { get; private set; }

        /// <summary>The <c>data</c> the semantic handler was given - the incoming edit text, unaltered.</summary>
        internal string? LastDoItemChangeData { get; private set; }

        /// <summary>
        /// The <c>data</c> the nested changing event was given. This is the RE-READ buffer value from
        /// <c>:L206</c>, not the incoming edit text, and it may legitimately be
        /// <see langword="null"/> because PowerScript's <c>String(null)</c> yields null.
        /// </summary>
        internal string? LastChangingData { get; private set; }

        /// <summary>
        /// What the nested changing event returns. The oracle DISCARDS it - <c>:L207</c> is a bare
        /// statement, unlike every other raise in that object - so this exists to prove the discard
        /// rather than to influence anything.
        /// </summary>
        internal long ChangingResult { get; init; }

        /// <inheritdoc />
        public long OnDoItemChange(long row, IDataWindowObject dwo, string? data)
        {
            DoItemChangeRaiseCount++;
            LastDoItemChangeData = data;
            _log.Record(DoItemChangeRaise, row, dwo, data);
            return _onDoItemChange(row, dwo, data);
        }

        /// <inheritdoc />
        public long OnDwnChanging(long row, IDataWindowObject dwo, string? data)
        {
            ChangingRaiseCount++;
            LastChangingData = data;
            _log.Record(ChangingRaise, row, dwo, data);
            return ChangingResult;
        }

        /// <inheritdoc />
        public void OnDoItemChanged(long row, IDataWindowObject dwo)
        {
            ChangedRaiseCount++;
            _log.Record(ChangedRaise, row, dwo);
        }
    }

    /// <summary>
    /// A sink whose semantic handler body is EXACTLY <c>se_cst_dw.sru:L292</c> -
    /// <c>return Event ItemChanged(row,dwo,data)</c> - and nothing else.
    /// </summary>
    /// <remarks>
    /// Used by Section 7 to assert that the live <c>ondoitemchange</c> body delegates and returns,
    /// carrying across none of the commented byte-length validation at <c>:L280-L290</c> and none of
    /// the dead local assignment at <c>:L278</c>.
    /// </remarks>
    private sealed class SemanticDelegatingSink : IItemChangeEventSink
    {
        private readonly FakeDataWindowHost _host;

        internal SemanticDelegatingSink(FakeDataWindowHost host)
        {
            _host = host;
        }

        /// <summary>How many times <c>:L247</c> raised the changed event.</summary>
        internal int ChangedRaiseCount { get; private set; }

        /// <summary>How many times <c>:L207</c> raised the nested changing event.</summary>
        internal int ChangingRaiseCount { get; private set; }

        /// <summary>
        /// The whole of the live handler: delegate to the semantic event and return its result.
        /// No length test, no truncation, no dialog, no inspection of <c>ColType</c>.
        /// </summary>
        public long OnDoItemChange(long row, IDataWindowObject dwo, string? data)
        {
            return _host.ItemChanged(row, dwo, data);
        }

        /// <inheritdoc />
        public long OnDwnChanging(long row, IDataWindowObject dwo, string? data)
        {
            ChangingRaiseCount++;
            return RetCode.OK;
        }

        /// <inheritdoc />
        public void OnDoItemChanged(long row, IDataWindowObject dwo)
        {
            ChangedRaiseCount++;
        }
    }


    // ==============================================================================================
    //  ARRANGEMENT
    //  --------------------------------------------------------------------------------------------
    //  One single-column, single-row DataWindow, because the routine under test addresses exactly one
    //  cell: `row` and `dwo` arrive from the event and are passed through unchanged. There is no loop
    //  anywhere in :L182-L253, so no multi-row arrangement adds coverage - and `row` stays ONE BASED
    //  throughout, exactly as the oracle counts and as AAP 0.4.5.4 requires.
    // ==============================================================================================

    /// <summary>One arranged cell: the fake host, the column object addressed, and its identifier.</summary>
    private sealed class ProtocolFixture
    {
        internal ProtocolFixture(FakeDataWindowHost host, FakeDataWindowObject dwo, long columnId)
        {
            Host = host;
            Dwo = dwo;
            ColumnId = columnId;
        }

        /// <summary>The fake DataWindow the protocol reads and writes through.</summary>
        internal FakeDataWindowHost Host { get; }

        /// <summary>The column the change is addressed to - the oracle's <c>dwo</c>.</summary>
        internal FakeDataWindowObject Dwo { get; }

        /// <summary>The resolved column identifier - the oracle's <c>Long(dwo.ID)</c>.</summary>
        internal long ColumnId { get; }

        /// <summary>The interleaved log of contract calls and event raises.</summary>
        internal DataWindowCallLog Log => Host.CallLog;

        /// <summary>
        /// The primary-buffer cell value. Reads and writes here are ARRANGEMENT and are NOT recorded,
        /// which is what lets a handler mutate the buffer mid-protocol while leaving every
        /// <c>SetItem</c> record in the log attributable to the routine under test.
        /// </summary>
        internal object? BufferValue
        {
            get => Host.GetBufferValue(DwBuffer.Primary, FixtureRow, ColumnId);
            set => Host.SetBufferValue(DwBuffer.Primary, FixtureRow, ColumnId, value);
        }

        /// <summary>The primary-buffer item status. Also arrangement, also unrecorded.</summary>
        internal ItemStatus BufferStatus
        {
            get => Host.GetBufferItemStatus(DwBuffer.Primary, FixtureRow, ColumnId);
            set => Host.SetBufferItemStatus(DwBuffer.Primary, FixtureRow, ColumnId, value);
        }
    }

    /// <summary>
    /// Arranges one column of <paramref name="colType"/> holding <paramref name="seedValue"/> with
    /// item status <paramref name="seedStatus"/>.
    /// </summary>
    /// <remarks>
    /// The column type string is stored RAW, because <c>:L231</c> reads <c>dwo.ColType</c> and takes
    /// its first five characters, and <c>:L282-L284</c> - the dormant path - parses the declared width
    /// back out of the SAME string. A pre-parsed type would make both unrepresentable.
    /// </remarks>
    private static ProtocolFixture Arrange(
        string colType,
        object? seedValue = null,
        ItemStatus seedStatus = ItemStatus.NotModified)
    {
        FakeDataWindowHost host = new();
        host.AddColumn("subject", colType);
        _ = host.AddRow();

        FakeDataWindowObject dwo = host.DwObject("subject");
        long columnId = host.Column("subject").Id;

        ProtocolFixture fixture = new(host, dwo, columnId)
        {
            BufferValue = seedValue,
            BufferStatus = seedStatus,
        };

        // Arrangement writes must not be visible to the assertions, so the log starts empty even
        // though seeding happened. `SetBufferValue`/`SetBufferItemStatus` are unrecorded anyway; the
        // clear is belt and braces against a future change to the fake.
        host.CallLog.Clear();
        return fixture;
    }

    /// <summary>
    /// Every <c>SetItem</c> record in <paramref name="log"/>, in call order, across all seven
    /// overloads.
    /// </summary>
    /// <remarks>
    /// Filtering on the member name rather than on a formatted signature is what keeps the RESTORE
    /// (<c>SetItem(object?)</c>, <c>:L219</c>) and a COERCION (one of six typed overloads,
    /// <c>:L233-L243</c>) in the same collection while still telling them apart by
    /// <see cref="DataWindowCallRecord.Signature"/>. Their being distinguishable is what makes "no
    /// restore happened" assertable rather than merely plausible.
    /// </remarks>
    private static IReadOnlyList<DataWindowCallRecord> SetItemCalls(DataWindowCallLog log)
    {
        List<DataWindowCallRecord> calls = [];
        foreach (DataWindowCallRecord record in log.Records)
        {
            if (string.Equals(record.Member, SetItemMember, StringComparison.Ordinal))
            {
                calls.Add(record);
            }
        }

        return calls;
    }

    /// <summary>
    /// Renders a stored buffer value culture invariantly, so a theory row can state the expected
    /// stored value as one readable string.
    /// </summary>
    /// <remarks>
    /// Invariant on every branch, deliberately: a coercion outcome that varied with the host locale
    /// would make a parity recording incomparable between two machines, and the five validators are
    /// invariant for exactly the same reason.
    /// </remarks>
    private static string RenderInvariant(object? value)
    {
        return value switch
        {
            null => "(null)",
            string text => text,
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            long integer => integer.ToString(CultureInfo.InvariantCulture),
            DateTime timestamp => timestamp.ToString(
                DateTimeValidator.ExpressionValueFormat,
                CultureInfo.InvariantCulture),
            DateOnly date => date.ToString(DateValidator.CanonicalValueFormat, CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString(TimeValidator.CanonicalFormat, CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "(null)",
        };
    }

    /// <summary>The CLR type name a theory row states, or the null sentinel.</summary>
    private static string ClrTypeName(object? value) => value is null ? "(null)" : value.GetType().Name;

    /// <summary>
    /// The one and only record in <paramref name="log"/> whose member name is
    /// <paramref name="member"/>, asserting along the way that there is exactly one.
    /// </summary>
    /// <remarks>
    /// Filtering here rather than through a predicate overload keeps the assertion's failure message
    /// specific to the member being looked for, and keeps "exactly one call" and "these arguments"
    /// as one statement instead of two that can drift apart.
    /// </remarks>
    private static DataWindowCallRecord SingleRecord(DataWindowCallLog log, string member)
    {
        List<DataWindowCallRecord> matches = [];
        foreach (DataWindowCallRecord record in log.Records)
        {
            if (string.Equals(record.Member, member, StringComparison.Ordinal))
            {
                matches.Add(record);
            }
        }

        Assert.Single(matches);
        return matches[0];
    }

    /// <summary>
    /// The ANSI byte length PowerScript's <c>LenA</c> measures - one byte per ASCII character and two
    /// per Chinese character, which is exactly what the dormant message at <c>se_cst_dw.sru:L286</c>
    /// says it counts.
    /// </summary>
    /// <remarks>
    /// Present ONLY so Section 7 can state machine-checkably that its inputs really do exceed the
    /// declared column width. The routine under test contains no equivalent, which is the point:
    /// <c>:L285</c>'s <c>LenA</c> comparison lives inside the commented block.
    /// </remarks>
    private static int AnsiByteCount(string text)
    {
        int total = 0;
        foreach (char character in text)
        {
            total += character > 0x7F ? 2 : 1;
        }

        return total;
    }

    /// <summary>
    /// Maps one of the four alphabet numerals onto its NAMED member, independently of the code under
    /// test.
    /// </summary>
    /// <remarks>
    /// Deliberately a hand-written switch rather than a call to
    /// <see cref="ItemChangeProtocol.Classify"/>. Deriving a theory row's expectation from the routine
    /// being tested would make every row tautological - it would agree with the implementation however
    /// the implementation behaved. This exists so rows can carry legible numerals (which is what the
    /// oracle's <c>choose case</c> compares, and what keeps the generated test names readable) while
    /// every assertion still names the member.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="numeral"/> is outside <c>{0, 1, 2, 3}</c>. The alphabet has exactly four values
    /// [<c>se_cst_dw.sru:L211-L251</c>], so a fifth here is a defect in the test data, not an input to
    /// be absorbed.
    /// </exception>
    private static ItemChangeResult NamedMember(int numeral)
    {
        return numeral switch
        {
            0 => ItemChangeResult.Default,
            1 => ItemChangeResult.TriggerValidationError,
            2 => ItemChangeResult.RestoreAndRejectText,
            3 => ItemChangeResult.KeepValueNoFocusMove,
            _ => throw new ArgumentOutOfRangeException(
                nameof(numeral),
                numeral,
                "The item-change alphabet has exactly four values: 0, 1, 2 and 3."),
        };
    }


    // ==============================================================================================
    //  SECTION 1 - THE ALPHABET IS ITS OWN ENUMERATION
    //  --------------------------------------------------------------------------------------------
    //  AAP 0.6.1.5: the item-change return alphabet "must be modelled as its own enumeration on the
    //  wire, never mapped onto the return-code algebra." That is not a stylistic preference. FOUR
    //  small-integer alphabets coexist in this system and three of them use 1 to mean something:
    //
    //      ItemChangeResult (HERE)  0 default, 1 trigger validation error, 2 restore and reject
    //                               text, 3 keep value and do not move focus
    //      RetCode                  0 OK/SUCCESS/ALLOW, 1 PREVENT, -1 FAILED, -2 CANCELED/CANCELLED
    //      VetoResult               0 continue, 1 prevent once, 2 prevent deep
    //      EventBroker.OnException  1 prevent, 2 continue  <-- 2 means the OPPOSITE of VetoResult's 2
    //
    //  Conflating any two produces a plausible-looking wrong answer rather than an error, which is
    //  precisely why it needs a test rather than a comment.
    // ==============================================================================================

    /// <summary>
    /// The alphabet has exactly four values and they are exactly <c>0</c>, <c>1</c>, <c>2</c> and
    /// <c>3</c> [<c>se_cst_dw.sru:L211-L251</c>], all distinct, none aliased.
    /// </summary>
    [Fact]
    public void TheAlphabetHasExactlyFourDistinctValuesNumberedZeroToThree()
    {
        ItemChangeResult[] members = Enum.GetValues<ItemChangeResult>();

        // FOUR arms in the oracle's `choose case`: `case 1` [:L212], `case 2` [:L213], `case 3`
        // [:L223] and `case else` [:L226]. A fifth member here would name a state the oracle has no
        // arm for.
        Assert.Equal(4, members.Length);

        // The numerals are load-bearing: they travel in serialized payloads on contract C-03, in log
        // records, and in characterization recordings, so the VALUES are pinned, not just the names.
        Assert.Equal(0, (int)ItemChangeResult.Default);                 // se_cst_dw.sru:L226 `case else`
        Assert.Equal(1, (int)ItemChangeResult.TriggerValidationError);  // se_cst_dw.sru:L212 `case 1`
        Assert.Equal(2, (int)ItemChangeResult.RestoreAndRejectText);    // se_cst_dw.sru:L213 `case 2`
        Assert.Equal(3, (int)ItemChangeResult.KeepValueNoFocusMove);    // se_cst_dw.sru:L223 `case 3`

        // Distinct, and therefore not silently collapsed onto one another. `case 3` REWRITES its code
        // to 1 at :L225, and a port that expressed that rewrite by ALIASING the two members would
        // make the pre-rewrite stash at :L195 unrepresentable - see oddity O-3 and Section 2.
        HashSet<int> numerals = [];
        foreach (ItemChangeResult member in members)
        {
            Assert.True(numerals.Add((int)member), "Two alphabet members share one numeral.");
        }

        Assert.Equal(4, numerals.Count);
    }

    /// <summary>
    /// The numerals of this alphabet COINCIDE with <c>RetCode</c>'s and with <c>VetoResult</c>'s while
    /// the meanings do not - which is what makes conflating them silent rather than loud.
    /// </summary>
    [Fact]
    public void TheAlphabetCollidesNumericallyWithRetCodeAndVetoResultAndIsNeitherOfThem()
    {
        // The collision is REAL, not hypothetical: 1 is `PREVENT` in the return-code algebra
        // [retcode.sru:L42] and "trigger OnDwnItemValidationError" here [se_cst_dw.sru:L210 comment].
        Assert.Equal(RetCode.PREVENT, (long)ItemChangeResult.TriggerValidationError);

        // And again at 2: a DEEP prevention in the broker's tri-valued veto versus "the buffer has
        // already been written, do not re-apply the edit text" here [se_cst_dw.sru:L248-L250].
        Assert.Equal((int)VetoResult.PreventDeep, (int)ItemChangeResult.RestoreAndRejectText);
        Assert.Equal((int)VetoResult.PreventOnce, (int)ItemChangeResult.TriggerValidationError);

        // Yet the TYPES are distinct, so the compiler refuses the interchange the numerals invite.
        Assert.NotEqual(typeof(ItemChangeResult), typeof(VetoResult));
        Assert.NotEqual(typeof(ItemChangeResult), typeof(WireItemChangeResult));

        // The decisive semantic difference, and the reason a numeric alias would be a defect rather
        // than a shortcut: the two NEGATIVE codes that are the return-code algebra's failure and
        // cancellation land in this alphabet's DEFAULT arm [se_cst_dw.sru:L226], which coerces, raises
        // the changed event and forces the result to 2. They are not errors here at all.
        Assert.Equal(ItemChangeResult.Default, ItemChangeProtocol.Classify(RetCode.FAILED));
        Assert.Equal(ItemChangeResult.Default, ItemChangeProtocol.Classify(RetCode.CANCELLED));
        Assert.Equal(ItemChangeResult.Default, ItemChangeProtocol.Classify(RetCode.CANCELED));

        // `OK`/`SUCCESS`/`ALLOW` are all 0 [retcode.sru:L40-L41] and 0 is this alphabet's default
        // member, so THAT numeral does agree - which is exactly why the disagreements above matter.
        Assert.Equal(ItemChangeResult.Default, ItemChangeProtocol.Classify(RetCode.OK));

        // Its own storage, too: `int`, whereas every `RetCode` constant is declared `long`.
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(ItemChangeResult)));
    }

    /// <summary>
    /// Raw handler codes classify per <c>se_cst_dw.sru:L211-L251</c>: <c>1</c>, <c>2</c> and <c>3</c>
    /// select their own arm and EVERYTHING ELSE reaches <c>case else</c>.
    /// </summary>
    /// <param name="rawCode">The unclassified value the semantic handler returned.</param>
    /// <param name="expectedNumeral">The single alphabet numeral it must classify to.</param>
    [Theory]
    [MemberData(nameof(ClassificationRows))]
    public void EveryRawHandlerCodeClassifiesOntoExactlyOneArm(long rawCode, int expectedNumeral)
    {
        Assert.Equal(NamedMember(expectedNumeral), ItemChangeProtocol.Classify(rawCode));
    }

    /// <summary>
    /// The classification matrix. <c>case else</c> [<c>se_cst_dw.sru:L226</c>] is an open arm, so the
    /// rows deliberately include values far outside the alphabet in both directions.
    /// </summary>
    public static TheoryData<long, int> ClassificationRows =>
        new()
        {
            { 1L, 1 },                     // se_cst_dw.sru:L212 `case 1`
            { 2L, 2 },                     // se_cst_dw.sru:L213 `case 2`
            { 3L, 3 },                     // se_cst_dw.sru:L223 `case 3`
            { 0L, 0 },                     // se_cst_dw.sru:L226 `case else`
            { 4L, 0 },                     // se_cst_dw.sru:L226 - one past the alphabet
            { 99L, 0 },                    // se_cst_dw.sru:L226
            { -1L, 0 },                    // se_cst_dw.sru:L226 - RetCode.FAILED is NOT a failure here
            { -2L, 0 },                    // se_cst_dw.sru:L226 - RetCode.CANCELLED likewise
            { long.MaxValue, 0 },          // se_cst_dw.sru:L226 - the field is `long`, so this is reachable
            { long.MinValue, 0 },          // se_cst_dw.sru:L226
        };

    /// <summary>
    /// The in-process alphabet and the wire alphabet agree VALUE FOR VALUE and NAME FOR NAME.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserted rather than enforced, because <c>PowerFramework.Contracts.csproj</c> declares no
    /// <c>ProjectReference</c> by design - versioned contracts are the only permitted cross-service
    /// coupling (constraint C-A) - so nothing makes the two enumerations agree at compile time. This
    /// test is that enforcement.
    /// </para>
    /// <para>
    /// Compared against the GENERATED type from <c>PowerFramework.Contracts.DataServices.V1</c>, never
    /// against retyped literals: a literal would agree with itself even if the protocol definition
    /// changed underneath.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDomainAlphabetAgreesWithTheGeneratedWireAlphabet()
    {
        string[] domainNames = Enum.GetNames<ItemChangeResult>();
        string[] wireNames = Enum.GetNames<WireItemChangeResult>();

        Assert.Equal(domainNames.Length, wireNames.Length);
        Assert.Equal<IEnumerable<string>>(domainNames, wireNames);

        ItemChangeResult[] domainMembers = Enum.GetValues<ItemChangeResult>();
        WireItemChangeResult[] wireMembers = Enum.GetValues<WireItemChangeResult>();

        List<int> domainNumerals = [];
        foreach (ItemChangeResult member in domainMembers)
        {
            domainNumerals.Add((int)member);
        }

        List<int> wireNumerals = [];
        foreach (WireItemChangeResult member in wireMembers)
        {
            wireNumerals.Add((int)member);
        }

        Assert.Equal<IEnumerable<int>>(domainNumerals, wireNumerals);
    }

    /// <summary>
    /// Member by member, the domain alphabet and the wire alphabet carry the same numeral under the
    /// same name.
    /// </summary>
    /// <param name="memberName">The member name both enumerations must declare.</param>
    /// <param name="expectedNumeral">The single numeral both must give it.</param>
    [Theory]
    [MemberData(nameof(WireAgreementRows))]
    public void EachAlphabetMemberMatchesItsWireCounterpart(string memberName, int expectedNumeral)
    {
        ItemChangeResult domainMember = Enum.Parse<ItemChangeResult>(memberName);
        WireItemChangeResult wireMember = Enum.Parse<WireItemChangeResult>(memberName);

        Assert.Equal(expectedNumeral, (int)domainMember);
        Assert.Equal(expectedNumeral, (int)wireMember);
        Assert.Equal(NamedMember(expectedNumeral), domainMember);
    }

    /// <summary>
    /// The four members, paired with the protocol-definition constant each is generated from
    /// [<c>shared/PowerFramework.Contracts/Proto/dataservices.v1.proto</c>, <c>enum
    /// ItemChangeResult</c>].
    /// </summary>
    public static TheoryData<string, int> WireAgreementRows =>
        new()
        {
            { nameof(ItemChangeResult.Default), 0 },                 // ITEM_CHANGE_RESULT_DEFAULT
            { nameof(ItemChangeResult.TriggerValidationError), 1 },  // ITEM_CHANGE_RESULT_TRIGGER_VALIDATION_ERROR
            { nameof(ItemChangeResult.RestoreAndRejectText), 2 },    // ITEM_CHANGE_RESULT_RESTORE_AND_REJECT_TEXT
            { nameof(ItemChangeResult.KeepValueNoFocusMove), 3 },    // ITEM_CHANGE_RESULT_KEEP_VALUE_NO_FOCUS_MOVE
        };

    // ==============================================================================================
    //  SECTION 2 - THE PROLOGUE: GATE, SNAPSHOT, RE-ENTRANCY, STASH
    //  --------------------------------------------------------------------------------------------
    //  se_cst_dw.sru:L187-L196, in this order and no other:
    //
    //      :L187  if BitTest(_nDisabledEvent,EID_ITEMCHANGE) then return 0
    //      :L189  aOrgValue = dwo.Primary[row]
    //      :L190  orgStatus = GetItemStatus(row,Long(dwo.ID),Primary!)
    //      :L192  bDoItemChange = _bDoItemChange          <-- SAVE
    //      :L193  _bDoItemChange = true                   <-- SET
    //      :L194  rtCode = Event OnDoItemChange(row,dwo,data)
    //      :L195  _nItemChangeRetCode = rtCode            <-- STASH, unclassified
    //      :L196  _bDoItemChange = bDoItemChange          <-- RESTORE, not clear
    //
    //  Nothing here may be reordered or hoisted. The status snapshot must be taken BEFORE the handler
    //  runs, the equality test that follows compares a reading taken AFTER it, and the stash must be
    //  written before the dispatch can rewrite the code.
    // ==============================================================================================

    /// <summary>
    /// With <c>EID_ITEMCHANGE</c> suppressed the routine returns <c>0</c> and NOTHING ELSE HAPPENS AT
    /// ALL [<c>se_cst_dw.sru:L187</c>].
    /// </summary>
    /// <remarks>
    /// The zero is not an error code. It is the continue value of the prevent convention the whole
    /// chain uses, identical to the three sibling guards at <c>:L124</c>, <c>:L130</c> and
    /// <c>:L176</c>. This gate is also the mechanism behind the oracle's own warning at <c>:L43</c>
    /// that disabling item-change stops column-expression calculation: the early return never reaches
    /// the <c>:L247</c> raise whose handler drives the expression service.
    /// </remarks>
    [Fact]
    public void TheDisabledItemChangeGateReturnsDefaultAndDoesNothingElse()
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice", ItemStatus.DataModified);

        // Reads are recorded too, so "nothing happened" can be asserted as an EMPTY log rather than
        // merely as an absence of writes. Without this, a stray GetItemStatus would go unnoticed.
        fixture.Host.RecordsReads = true;

        RecordingSessionState session = new()
        {
            DisabledEvent = EventGate.EID_ITEMCHANGE,

            // Both pre-set to non-default values so that "untouched" is distinguishable from "reset".
            DoItemChange = true,
            ItemChangeRetCode = 7L,
        };
        RecordingEventSink sink = new(fixture.Log, RetCode.OK);

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "Bob");

        Assert.Equal(NamedMember(0), result);                       // :L187 `return 0`
        Assert.Equal(0, fixture.Log.Count);                         // no snapshot, no raise, no write
        Assert.Equal(0, sink.DoItemChangeRaiseCount);               // :L194 never reached
        Assert.Equal(0, sink.ChangingRaiseCount);                   // :L207 never reached
        Assert.Equal(0, sink.ChangedRaiseCount);                    // :L247 never reached
        Assert.Equal(7L, session.ItemChangeRetCode);                // :L195 never reached - stash intact
        Assert.True(session.DoItemChange);                          // :L193/:L196 never reached - flag intact
        Assert.Equal("Alice", fixture.BufferValue);                 // buffer untouched
        Assert.Equal(ItemStatus.DataModified, fixture.BufferStatus); // status untouched
    }

    /// <summary>
    /// The gate tests ONE bit of a composable mask [<c>se_cst_dw.sru:L41-L43</c>, <c>:L187</c>]: only
    /// masks carrying <c>EID_ITEMCHANGE</c> suppress the routine.
    /// </summary>
    /// <param name="disabledEventMask">The whole suppression mask under test.</param>
    /// <param name="expectGated">Whether the routine must short-circuit at <c>:L187</c>.</param>
    [Theory]
    [MemberData(nameof(GateMaskRows))]
    public void OnlyMasksCarryingTheItemChangeBitSuppressTheRoutine(uint disabledEventMask, bool expectGated)
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice");
        RecordingSessionState session = new() { DisabledEvent = disabledEventMask };
        RecordingEventSink sink = new(fixture.Log, RetCode.OK);

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "Alice");

        if (expectGated)
        {
            Assert.Equal(NamedMember(0), result);          // :L187
            Assert.Equal(0, sink.DoItemChangeRaiseCount);
        }
        else
        {
            // Not gated, so the handler ran, `case else` was taken and :L250 forced the result to 2.
            Assert.Equal(NamedMember(2), result);          // :L250
            Assert.Equal(1, sink.DoItemChangeRaiseCount);  // :L194
        }
    }

    /// <summary>
    /// The eight masks reachable from the three declared event bits
    /// [<c>se_cst_dw.sru:L41-L43</c>: row-focus 1, item-focus 2, item-change 4].
    /// </summary>
    public static TheoryData<uint, bool> GateMaskRows =>
        new()
        {
            { 0u, false },                                        // nothing suppressed
            { EventGate.EID_ROWFOCUSCHANGE, false },              // 1 - a DIFFERENT event's bit
            { EventGate.EID_ITEMFOCUSCHANGE, false },             // 2 - likewise
            { EventGate.EID_ROWFOCUSCHANGE | EventGate.EID_ITEMFOCUSCHANGE, false },   // 3
            { EventGate.EID_ITEMCHANGE, true },                   // 4 - se_cst_dw.sru:L187 fires
            { EventGate.EID_ITEMCHANGE | EventGate.EID_ROWFOCUSCHANGE, true },         // 5
            { EventGate.EID_ITEMCHANGE | EventGate.EID_ITEMFOCUSCHANGE, true },        // 6
            {
                EventGate.EID_ITEMCHANGE
                    | EventGate.EID_ITEMFOCUSCHANGE
                    | EventGate.EID_ROWFOCUSCHANGE,
                true
            },                                                    // 7 - every declared bit
        };

    /// <summary>
    /// The ITEM-STATUS snapshot is taken at <c>:L190</c>, BEFORE the handler runs at <c>:L194</c>, so
    /// the restore at <c>:L220</c> writes back the PRE-handler status even when the handler changed it.
    /// </summary>
    [Fact]
    public void TheItemStatusSnapshotIsTakenBeforeTheHandlerRuns()
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice", ItemStatus.DataModified);
        RecordingSessionState session = new();

        // The handler leaves the VALUE alone - so :L198 finds them equal and :L216's guard passes -
        // but changes the STATUS. If the snapshot were taken after the handler, `NewModified` would be
        // restored; if before, `DataModified` is.
        RecordingEventSink sink = new(
            fixture.Log,
            (_, _, _) =>
            {
                fixture.BufferStatus = ItemStatus.NewModified;
                return 2L;
            });

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "Alice");

        Assert.Equal(NamedMember(2), result);                                    // :L213 arm, :L253
        DataWindowCallRecord statusRestore = SingleRecord(fixture.Log, SetItemStatusMember);

        // SetItemStatus(row, Long(dwo.ID), Primary!, orgStatus) - argument order is :L220's exactly.
        Assert.Equal(FixtureRow, Assert.IsType<long>(statusRestore.Arguments[0]));
        Assert.Equal(fixture.ColumnId, Assert.IsType<long>(statusRestore.Arguments[1]));
        Assert.Equal(DwBuffer.Primary, Assert.IsType<DwBuffer>(statusRestore.Arguments[2]));
        Assert.Equal(ItemStatus.DataModified, Assert.IsType<ItemStatus>(statusRestore.Arguments[3]));

        // And the buffer really ends up back at the snapshot, not at what the handler left behind.
        Assert.Equal(ItemStatus.DataModified, fixture.BufferStatus);
    }

    /// <summary>
    /// The VALUE snapshot is taken at <c>:L189</c>, BEFORE the handler runs, which is what lets the
    /// equality test at <c>:L198</c> detect a change the handler itself made.
    /// </summary>
    /// <remarks>
    /// If the snapshot were taken after the handler, both readings would be the handler's new value,
    /// <c>bEqual</c> would be <see langword="true"/>, and <c>:L216</c> would overwrite the handler's
    /// write with the stale one - which is exactly the overwrite the oracle's comment at <c>:L215</c>
    /// says the guard exists to prevent. So "no restore happened" is the observable proof of ordering.
    /// </remarks>
    [Fact]
    public void TheValueSnapshotIsTakenBeforeTheHandlerRuns()
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice", ItemStatus.DataModified);
        RecordingSessionState session = new();
        RecordingEventSink sink = new(
            fixture.Log,
            (_, _, _) =>
            {
                fixture.BufferValue = "HandlerWrote";
                return 2L;
            });

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "Alice");

        Assert.Equal(NamedMember(2), result);                       // :L213 arm still, :L253
        Assert.Empty(SetItemCalls(fixture.Log));                    // :L216 guard blocked the restore
        Assert.Equal(0, fixture.Log.CountOf(SetItemStatusMember));  // :L220 likewise never ran
        Assert.Equal("HandlerWrote", fixture.BufferValue);          // the handler's write survives
    }

    /// <summary>
    /// The snapshot read at <c>:L190</c> is recorded BEFORE the raise at <c>:L194</c> in the
    /// interleaved call log - the ordering asserted positionally rather than inferred.
    /// </summary>
    [Fact]
    public void TheSnapshotIsRecordedAheadOfTheSemanticRaise()
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice");
        fixture.Host.RecordsReads = true;

        RecordingSessionState session = new();
        RecordingEventSink sink = new(fixture.Log, RetCode.OK);

        _ = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "Alice");

        // :L190 then :L194 then :L233 (the char coercion) then :L247. Exactly this order, with the
        // whole sequence pinned so a hoisted snapshot or a reordered raise fails here rather than
        // somewhere downstream.
        string[] expectedOrder =
        [
            GetItemStatusMember,   // :L190
            DoItemChangeRaise,     // :L194
            SetItemMember,         // :L233 - `case "char","char("` inside `case else`
            ChangedRaise,          // :L247
        ];

        Assert.Equal<IEnumerable<string>>(expectedOrder, fixture.Log.Members);
        Assert.Equal(0, fixture.Log.IndexOf(GetItemStatusMember));
        Assert.Equal(1, fixture.Log.IndexOf(DoItemChangeRaise));
    }

    /// <summary>
    /// A column whose identifier cannot be resolved degrades rather than throwing: the status snapshot
    /// reads <see cref="ItemStatus.NotModified"/> [<c>:L190</c>], and neither the restore
    /// [<c>:L219-L220</c>] nor the coercion [<c>:L233-L243</c>] can be addressed - while every event
    /// still fires and the result is still forced.
    /// </summary>
    /// <param name="handlerCode">The code selecting the arm.</param>
    /// <param name="expectedNumeral">The single result that arm produces.</param>
    /// <param name="expectedChangedRaises">The single number of times <c>:L247</c> fires for that arm.</param>
    /// <remarks>
    /// <para>
    /// The oracle writes <c>Long(dwo.ID)</c> at every addressing site - <c>:L190</c>, <c>:L219</c>,
    /// <c>:L220</c> and each of <c>:L233-L243</c>. PowerScript's <c>Long</c> of a null yields null, and
    /// a DataWindow given an unresolvable item reference reports it as unmodified rather than raising,
    /// so the degenerate path degrades exactly where the oracle degrades. Throwing here instead would
    /// introduce an error path the legacy does not have (constraint C-B).
    /// </para>
    /// <para>
    /// Reachable in practice: <c>dwo</c> arrives from the event, and a computed field, a text object or
    /// a graph object carries no column identifier at all. This state is therefore pinned rather than
    /// treated as impossible.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnresolvableColumnRows))]
    public void AnUnresolvableColumnIdentifierDegradesWithoutWritingOrThrowing(
        long handlerCode,
        int expectedNumeral,
        int expectedChangedRaises)
    {
        // A `char(` column, so the coercion arm WOULD have matched had the identifier resolved - which
        // is what makes "nothing was written" attributable to the identifier and not to the type.
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice", ItemStatus.DataModified);
        fixture.Host.RecordsReads = true;

        // The buffer still addresses the real column, so the value snapshot and the equality test are
        // unaffected; only the identifier used for addressing the WRITES is gone.
        fixture.Dwo.ID = null;
        Assert.Null(fixture.Dwo.ColumnId());

        RecordingSessionState session = new();
        RecordingEventSink sink = new(fixture.Log, handlerCode);

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "Alice");

        Assert.Equal(NamedMember(expectedNumeral), result);          // :L253, unchanged by the degradation

        // :L190 could not be addressed, so it was not attempted at all.
        Assert.Equal(0, fixture.Log.CountOf(GetItemStatusMember));

        // Neither write site could be addressed - :L216 and :L229 both need the identifier.
        Assert.Empty(SetItemCalls(fixture.Log));
        Assert.Equal(0, fixture.Log.CountOf(SetItemStatusMember));

        // The cell and its status are therefore left exactly as they were.
        Assert.Equal("Alice", fixture.BufferValue);
        Assert.Equal(ItemStatus.DataModified, fixture.BufferStatus);

        // But the prologue and the events are untouched by the degradation: :L194 and :L195 ran, and
        // :L247 still fired for `case else` because it sits outside the `if bEqual` block.
        Assert.Equal(1, sink.DoItemChangeRaiseCount);
        Assert.Equal(handlerCode, session.ItemChangeRetCode);
        Assert.Equal(expectedChangedRaises, sink.ChangedRaiseCount);
    }

    /// <summary>
    /// One row per arm, so the degradation is asserted for the two arms that address the buffer
    /// (<c>:L213</c> and <c>:L226</c>) as well as the two that never do.
    /// </summary>
    public static TheoryData<long, int, int> UnresolvableColumnRows =>
        new()
        {
            // handler, result, changed raises
            { 1L, 1, 0 },   // :L212 - never addressed the buffer anyway
            { 2L, 2, 0 },   // :L213 - :L216's second condition is what stops the restore
            { 3L, 1, 0 },   // :L225 - never addressed the buffer anyway
            { 0L, 2, 1 },   // :L226 - :L229's second condition stops the coercion, :L247 still fires
        };

    /// <summary>
    /// The re-entrancy flag is SET for the duration of the handler [<c>:L193</c>] and then RESTORED TO
    /// ITS PREVIOUS VALUE [<c>:L196</c>] - never cleared.
    /// </summary>
    /// <param name="alreadyInItemChange">
    /// Whether an outer invocation was already in progress. The <see langword="true"/> row is the one
    /// that fails against a port that assigned <see langword="false"/> at <c>:L196</c>.
    /// </param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheReEntrancyFlagIsRestoredToItsPreviousValueRatherThanCleared(bool alreadyInItemChange)
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice");
        RecordingSessionState session = new() { DoItemChange = alreadyInItemChange };

        bool observedInsideHandler = false;
        RecordingEventSink sink = new(
            fixture.Log,
            (_, _, _) =>
            {
                observedInsideHandler = session.DoItemChange;
                return RetCode.OK;
            });

        _ = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "Alice");

        Assert.True(observedInsideHandler);                          // :L193 - true while inside
        Assert.Equal(alreadyInItemChange, session.DoItemChange);     // :L196 - the SAVED value, restored
    }

    /// <summary>
    /// A NESTED item change leaves the outer invocation's flag intact [<c>:L192</c>, <c>:L196</c>].
    /// </summary>
    /// <remarks>
    /// This is the case the save-and-restore exists for, and the reason a port must not write
    /// <see langword="false"/> at <c>:L196</c>. The flag escapes the routine:
    /// <c>ondwnkillfocus</c> queues its deferred accept-text continuation only while the flag is CLEAR
    /// [<c>:L387-L390</c>], so an inner invocation that cleared it would let that continuation run in
    /// the middle of an outer item change.
    /// </remarks>
    [Fact]
    public void ANestedItemChangeLeavesTheOuterReEntrancyFlagIntact()
    {
        // An unmatched column type, so neither invocation's `case else` writes anything and the outer
        // equality test at :L198 stays true. The subject here is the flag, nothing else.
        ProtocolFixture fixture = Arrange("blob", "Alice");
        RecordingSessionState session = new();

        bool observedByInnerHandler = false;
        bool observedAfterInnerReturned = false;
        bool observedByOuterHandler = false;

        RecordingEventSink inner = new(
            fixture.Log,
            (_, _, _) =>
            {
                observedByInnerHandler = session.DoItemChange;   // :L193 set again, nested
                return RetCode.OK;
            });

        ItemChangeResult nestedResult = default;

        RecordingEventSink outer = new(
            fixture.Log,
            (row, dwo, _) =>
            {
                observedByOuterHandler = session.DoItemChange;   // :L193 set by the outer call

                nestedResult = ItemChangeProtocol.OnDwnItemChange(
                    fixture.Host,
                    session,
                    inner,
                    row,
                    dwo,
                    "nested");

                // THE POINT: the inner :L196 put back the value it saved at :L192, which was TRUE.
                observedAfterInnerReturned = session.DoItemChange;
                return 1L;
            });

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            outer,
            FixtureRow,
            fixture.Dwo,
            "outer");

        Assert.Equal(NamedMember(1), result);          // :L212 empty arm, :L253 returns 1 unchanged
        Assert.Equal(NamedMember(2), nestedResult);    // the nested run took :L226 and :L250 forced 2
        Assert.True(observedByOuterHandler);           // :L193, outer
        Assert.True(observedByInnerHandler);           // :L193, inner
        Assert.True(observedAfterInnerReturned);       // :L196, inner - restored to TRUE, not cleared
        Assert.False(session.DoItemChange);            // :L196, outer - back to its own pre-call value
        Assert.Equal(1, inner.DoItemChangeRaiseCount);
        Assert.Equal(1, outer.DoItemChangeRaiseCount);

        // The INNER run wrote the stash last, so the outer's :L195 value is what survives - and the
        // outer wrote it AFTER the nested call returned, because :L194 is where the nesting happens.
        Assert.Equal(1L, session.ItemChangeRetCode);
    }

    /// <summary>
    /// The stash at <c>:L195</c> is observable on the session, holds the RAW handler code, and is
    /// written before any rewrite - so it DIVERGES from the returned value.
    /// </summary>
    /// <param name="handlerCode">What the semantic handler at <c>:L194</c> returned.</param>
    /// <param name="expectedReturnedNumeral">The single value the routine returns for it.</param>
    [Theory]
    [MemberData(nameof(StashRows))]
    public void TheStashKeepsTheRawPreRewriteHandlerCode(long handlerCode, int expectedReturnedNumeral)
    {
        ProtocolFixture fixture = Arrange("blob", "Alice");
        RecordingSessionState session = new() { ItemChangeRetCode = long.MinValue };
        RecordingEventSink sink = new(fixture.Log, handlerCode);

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "Alice");

        Assert.Equal(NamedMember(expectedReturnedNumeral), result);   // :L253
        Assert.Equal(handlerCode, session.ItemChangeRetCode);         // :L195 - raw and unclassified
    }

    /// <summary>
    /// Stash rows. The <c>3</c> row is the one that matters: <c>:L225</c> rewrites the RESULT to
    /// <c>1</c> while <c>:L195</c> has already stashed <c>3</c>, so the validation-error event still
    /// sees <c>3</c> and suppresses its own restore [<c>:L369</c>] while this routine's caller sees
    /// <c>1</c>.
    /// </summary>
    public static TheoryData<long, int> StashRows =>
        new()
        {
            { 1L, 1 },      // :L195 stashes 1, :L212 returns 1
            { 2L, 2 },      // :L195 stashes 2, :L213 arm returns 2
            { 3L, 1 },      // :L195 stashes 3, :L225 REWRITES the return to 1 - stash and return diverge
            { 0L, 2 },      // :L195 stashes 0, :L250 forces the return to 2
            { 42L, 2 },     // :L195 stashes 42 verbatim - narrowing to the enum would lose it
            { -7L, 2 },     // :L195 stashes -7 verbatim
        };

    /// <summary>
    /// The real <c>Domain/ValidationSession.cs</c> - not just the double - carries the stash, and its
    /// own classified view of it agrees with the raw value.
    /// </summary>
    /// <remarks>
    /// Driven against the production implementer so the double can never become the only witness to
    /// the stash contract. The session is the port of the four cross-event fields at
    /// <c>se_cst_dw.sru:L89-L96</c>, materialised as explicit state because a stateless request
    /// boundary has nowhere to put them.
    /// </remarks>
    [Fact]
    public void TheStashIsObservableOnTheRealValidationSession()
    {
        ProtocolFixture fixture = Arrange("blob", "Alice");
        ValidationSession session = new("itemchange-protocol-parity");
        RecordingEventSink sink = new(fixture.Log, 3L);

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "Alice");

        Assert.Equal(NamedMember(1), result);                                    // :L225 rewrite, :L253
        Assert.Equal(3L, session.ItemChangeRetCode);                             // :L195 pre-rewrite
        Assert.Equal(NamedMember(3), session.StashedItemChangeResult);            // the same value, classified
        Assert.False(session.DoItemChange);                                      // :L196 restored
        Assert.Equal(3L, session.CaptureState().RawItemChangeRetCode);            // :L195 via the snapshot
        Assert.Equal(NamedMember(3), session.CaptureState().ItemChangeRetCode);
        Assert.False(session.CaptureState().InItemChange);                        // :L196
    }

    /// <summary>
    /// The routine fails fast on a null collaborator rather than continuing - the posture this refactor
    /// preserves throughout (AAP 0.1.4).
    /// </summary>
    /// <remarks>
    /// None of these can fire for any input the oracle can produce; they are structural guards on
    /// parameters the signature already declares non-nullable, so they add no error path to any
    /// reachable legacy behaviour.
    /// </remarks>
    [Fact]
    public void EveryCollaboratorIsGuardedAgainstNull()
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice");
        RecordingSessionState session = new();
        RecordingEventSink sink = new(fixture.Log, RetCode.OK);

        // Block-bodied lambdas with a discard, so each one binds unambiguously to the `Action`
        // overload of `Assert.Throws` rather than relying on delegate overload resolution.
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = ItemChangeProtocol.OnDwnItemChange(null!, session, sink, FixtureRow, fixture.Dwo, "Alice");
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = ItemChangeProtocol.OnDwnItemChange(fixture.Host, null!, sink, FixtureRow, fixture.Dwo, "Alice");
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = ItemChangeProtocol.OnDwnItemChange(fixture.Host, session, null!, FixtureRow, fixture.Dwo, "Alice");
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = ItemChangeProtocol.OnDwnItemChange(fixture.Host, session, sink, FixtureRow, null!, "Alice");
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = ItemChangeProtocol.OnDwnItemChange(fixture.Host, session, sink, FixtureRow, fixture.Dwo, null!);
        });

        // A rejected call is a call that did nothing: no snapshot, no raise, no write, no stash.
        Assert.Equal(0, fixture.Log.Count);
        Assert.Equal(0, sink.DoItemChangeRaiseCount);
        Assert.Equal(0L, session.ItemChangeRetCode);
    }


    // ==============================================================================================
    //  SECTION 3 - THE EQUALITY TEST, INCLUDING THE NULL-AND-NULL ARM
    //  --------------------------------------------------------------------------------------------
    //      :L198  if aOrgValue = dwo.Primary[row] then
    //      :L199      bEqual = true
    //      :L200  elseif IsNull(aOrgValue) and IsNull(dwo.Primary[row]) then
    //      :L201      bEqual = true
    //      :L202  end if
    //
    //  The second arm is not redundant. PowerScript's `=` against a null operand yields NULL, and `if`
    //  treats a null condition as false, so `null = null` does NOT take :L199 - which is exactly why
    //  the author wrote :L200. The consequence for a port is that ONE-SIDED null must fall through to
    //  "not equal" while TWO-SIDED null must be equal, and that asymmetry is the whole content of this
    //  section. `bEqual` gates BOTH the restore at :L216 AND the coercion at :L229, so flipping it
    //  silently changes what two different arms do.
    // ==============================================================================================

    /// <summary>
    /// The equality predicate reproduces <c>se_cst_dw.sru:L198-L202</c> arm for arm.
    /// </summary>
    /// <param name="original">The <c>:L189</c> snapshot, or <see langword="null"/>.</param>
    /// <param name="current">The post-handler buffer reading, or <see langword="null"/>.</param>
    /// <param name="expectedEqual">The single outcome <c>bEqual</c> must hold.</param>
    [Theory]
    [MemberData(nameof(EqualityRows))]
    public void TheEqualityTestReproducesBothArmsAndTheOneSidedNullGap(
        string? original,
        string? current,
        bool expectedEqual)
    {
        Assert.Equal(expectedEqual, ItemChangeProtocol.IsBufferValueEqual(original, current));
    }

    /// <summary>The five arms of <c>se_cst_dw.sru:L198-L202</c>, one row each.</summary>
    public static TheoryData<string?, string?, bool> EqualityRows =>
        new()
        {
            { "Alice", "Alice", true },   // :L198-L199 - equal non-null values
            { "Alice", "Bob", false },    // :L198 fails, :L200 fails - not equal
            { null, null, true },         // :L200-L201 - THE EXPLICIT SECOND ARM
            { null, "Bob", false },       // :L200 fails on the second operand
            { "Alice", null, false },     // :L200 fails on the first operand
            { "", "", true },             // empty is a VALUE, not a null - :L198-L199
            { "Alice", "alice", false },  // PowerScript comparison is case sensitive
            { " Alice", "Alice", false }, // and whitespace sensitive
        };

    /// <summary>
    /// A null buffer value is NEVER collapsed to zero or to the empty string.
    /// </summary>
    /// <remarks>
    /// AAP 0.4.5.4 forbids the collapse for the return-code algebra, where it converts "neither
    /// succeeded nor failed" into "succeeded". The identical hazard applies here and is arguably worse,
    /// because the damage is silent in BOTH directions: collapsing null to the empty string would make
    /// a cleared cell compare equal to an empty one and let <c>:L216</c> overwrite the clearing;
    /// collapsing it to zero would do the same for a numeric column.
    /// </remarks>
    [Fact]
    public void ANullBufferValueIsNeverCollapsedToZeroOrToEmpty()
    {
        // Two nulls ARE equal - :L200-L201.
        Assert.True(ItemChangeProtocol.IsBufferValueEqual(null, null));

        // But a null is equal to nothing else, in either operand position - :L198 and :L200 both fail.
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(null, string.Empty));
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(string.Empty, null));
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(null, 0L));
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(0L, null));
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(null, 0m));
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(0m, null));
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(null, false));
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(false, null));

        // And zero is not the empty string either, so no pair of "falsy" values is quietly conflated.
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(0L, string.Empty));
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(string.Empty, 0L));
    }

    /// <summary>
    /// Typed, boxed buffer values compare by value within one type - the ordinary case, since every
    /// comparison the routine makes is the SAME cell read twice from the same buffer.
    /// </summary>
    [Fact]
    public void TypedBufferValuesCompareByValueWithinOneType()
    {
        Assert.True(ItemChangeProtocol.IsBufferValueEqual(42L, 42L));
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(42L, 43L));

        Assert.True(ItemChangeProtocol.IsBufferValueEqual(1234.75m, 1234.75m));
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(1234.75m, 1234.76m));

        DateOnly birth = new(2020, 2, 29);
        Assert.True(ItemChangeProtocol.IsBufferValueEqual(birth, new DateOnly(2020, 2, 29)));
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(birth, new DateOnly(2020, 3, 1)));

        DateTime timestamp = new(2020, 2, 29, 10, 30, 0, DateTimeKind.Unspecified);
        Assert.True(ItemChangeProtocol.IsBufferValueEqual(
            timestamp,
            new DateTime(2020, 2, 29, 10, 30, 0, DateTimeKind.Unspecified)));

        // The time component participates, which is the reason the datetime and date arms of the
        // coercion switch must stay separate - see Section 6.
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(
            timestamp,
            new DateTime(2020, 2, 29, 10, 30, 1, DateTimeKind.Unspecified)));

        Assert.True(ItemChangeProtocol.IsBufferValueEqual(new TimeOnly(10, 20, 30), new TimeOnly(10, 20, 30)));
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(new TimeOnly(10, 20, 30), new TimeOnly(10, 20, 31)));
    }

    /// <summary>
    /// RECORDED DIFFERENCE, NOT A PARITY CLAIM: the ported comparison is TYPE EXACT across boxed
    /// numeric types, where PowerScript's <c>any</c> comparison coerces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>:L198</c> compares two PowerScript <c>any</c> values, and PowerScript would treat the integer
    /// <c>1</c> and the decimal <c>1</c> as equal. <c>object.Equals</c> does not. This test pins the
    /// OBSERVED behaviour of the port rather than asserting parity, because the two disagree.
    /// </para>
    /// <para>
    /// WHY IT IS RECORDED HERE RATHER THAN "FIXED". The routine only ever compares the SAME cell read
    /// twice from the SAME buffer [<c>:L189</c> then <c>:L198</c>], so on its own path both readings
    /// carry the same CLR type and the difference is unreachable. It becomes reachable only if a
    /// semantic handler writes a differently-typed boxed value into the cell, which no in-scope handler
    /// does. Changing the comparison to coerce would be a behaviour change made on a guess, which
    /// constraint C-B forbids; a paired legacy/target characterization recording is the arbiter (AAP
    /// 0.6.7), and this test is what makes the current answer visible to whoever runs it.
    /// </para>
    /// </remarks>
    [Fact]
    public void BoxedNumericTypeIdentityParticipatesAndIsRecordedForOracleVerification()
    {
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(1L, 1m));
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(1m, 1L));
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(1L, 1));
        Assert.False(ItemChangeProtocol.IsBufferValueEqual(1L, "1"));
    }

    /// <summary>
    /// End to end: the comparison is made against the buffer AS IT STANDS AFTER THE HANDLER RAN, so a
    /// handler that mutates the cell changes the outcome of <c>:L198</c> and therefore what the arm at
    /// <c>:L213</c> does.
    /// </summary>
    /// <param name="seeded">The value the cell holds before the routine starts.</param>
    /// <param name="writtenByHandler">The value the semantic handler writes into the cell.</param>
    /// <param name="expectRestore">
    /// Whether <c>:L216</c>'s guard passes and the restore at <c>:L219-L220</c> therefore runs.
    /// </param>
    [Theory]
    [MemberData(nameof(PostHandlerComparisonRows))]
    public void TheComparisonUsesThePostHandlerBufferReading(
        string? seeded,
        string? writtenByHandler,
        bool expectRestore)
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), seeded, ItemStatus.DataModified);
        RecordingSessionState session = new();

        // Returns 2 so the run lands on :L213, the one arm whose behaviour is gated purely on bEqual.
        RecordingEventSink sink = new(
            fixture.Log,
            (_, _, _) =>
            {
                fixture.BufferValue = writtenByHandler;
                return 2L;
            });

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "irrelevant edit text");

        // The arm's RESULT is 2 either way [:L253]; only the restore differs. That is precisely why
        // the restore has to be observed rather than inferred from the return value.
        Assert.Equal(NamedMember(2), result);

        if (expectRestore)
        {
            DataWindowCallRecord restore = SingleRecord(fixture.Log, SetItemMember);
            Assert.Equal(RestoreSetItem, restore.Signature);          // :L219 passes `any`
            Assert.Equal(seeded, restore.Arguments[2]);               // the :L189 snapshot
            Assert.Equal(1, fixture.Log.CountOf(SetItemStatusMember)); // :L220
            Assert.Equal(seeded, fixture.BufferValue);
        }
        else
        {
            Assert.Empty(SetItemCalls(fixture.Log));                   // :L216 guard blocked it
            Assert.Equal(0, fixture.Log.CountOf(SetItemStatusMember));
            Assert.Equal(writtenByHandler, fixture.BufferValue);
        }
    }

    /// <summary>
    /// Post-handler comparison rows. The third and fourth are the load-bearing ones: <c>null</c> to
    /// <c>null</c> is equal by <c>:L200-L201</c>, while a one-sided <c>null</c> is not.
    /// </summary>
    public static TheoryData<string?, string?, bool> PostHandlerComparisonRows =>
        new()
        {
            { "Alice", "Alice", true },    // handler rewrote the same value - :L198-L199, restore runs
            { "Alice", "Bob", false },     // handler changed it - :L216 blocks the restore
            { null, null, true },          // :L200-L201 - both null, restore runs
            { "Alice", null, false },      // handler CLEARED the cell - no restore, the clearing stands
            { null, "Bob", false },        // handler filled an empty cell - no restore
            { "Alice", "", false },        // empty is not null and not "Alice"
        };


    // ==============================================================================================
    //  SECTION 4 - THE INEQUALITY PATH: RE-READ, THEN THE NESTED RAISE
    //  --------------------------------------------------------------------------------------------
    //      :L204  if Not bEqual then
    //      :L205      //取修改后的值                      "take the modified value"
    //      :L206      data = String(dwo.Primary[row])     <-- RE-READ, and it REASSIGNS `data`
    //      :L207      Event OnDwnChanging(row,dwo,data)   <-- NESTED, and its result is DISCARDED
    //      :L208  end if
    //
    //  Two details a port loses if it is not careful. First, :L206 REASSIGNS the event's own `data`
    //  parameter, so anything downstream that reads `data` sees the re-read value and not the incoming
    //  edit text - which is unobservable in practice only because :L229's guard and :L204's condition
    //  are mutually exclusive, and this section asserts that mutual exclusion rather than assuming it.
    //  Second, :L207 is a BARE STATEMENT: every other raise in this object is consumed by the
    //  `= 1 then return 1` prevent convention, and this one is not. Acting on its value would add a
    //  prevention path the oracle does not have.
    // ==============================================================================================

    /// <summary>
    /// When the buffer value changed across the handler, the routine RE-READS the cell and raises the
    /// nested changing event with the re-read value - not with the incoming edit text
    /// [<c>se_cst_dw.sru:L206-L207</c>].
    /// </summary>
    [Fact]
    public void AChangedBufferIsReReadAndTheNestedChangingEventCarriesTheReReadValue()
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice");
        RecordingSessionState session = new();
        RecordingEventSink sink = new(
            fixture.Log,
            (_, _, _) =>
            {
                fixture.BufferValue = "WrittenByHandler";
                return RetCode.OK;
            });

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "IncomingEditText");

        // The semantic handler still saw the INCOMING edit text at :L194 - the re-read happens after.
        Assert.Equal("IncomingEditText", sink.LastDoItemChangeData);

        // :L207 raised exactly once, carrying the :L206 re-read rather than the edit text.
        Assert.Equal(1, sink.ChangingRaiseCount);
        Assert.Equal("WrittenByHandler", sink.LastChangingData);

        // :L229's guard failed, so `case else` wrote nothing at all - the mutual exclusion between
        // :L204 and :L229 that keeps the :L206 reassignment unobservable.
        Assert.Empty(SetItemCalls(fixture.Log));

        // :L247 still fired and :L250 still forced the result, both OUTSIDE the `if bEqual` block.
        Assert.Equal(1, sink.ChangedRaiseCount);
        Assert.Equal(NamedMember(2), result);
    }

    /// <summary>
    /// When the buffer value did NOT change, neither the re-read nor the nested raise happens
    /// [<c>se_cst_dw.sru:L204</c>].
    /// </summary>
    [Fact]
    public void AnUnchangedBufferSkipsBothTheReReadAndTheNestedRaise()
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice");
        RecordingSessionState session = new();
        RecordingEventSink sink = new(fixture.Log, RetCode.OK);

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "Alice");

        Assert.Equal(0, sink.ChangingRaiseCount);                  // :L204 guard held
        Assert.Null(sink.LastChangingData);
        Assert.Equal(0, fixture.Log.CountOf(ChangingRaise));

        // And because the value was unchanged, :L229's guard PASSED and the coercion ran instead.
        DataWindowCallRecord coercion = SingleRecord(fixture.Log, SetItemMember);
        Assert.Equal("SetItem(string?)", coercion.Signature);      // :L233
        Assert.Equal(NamedMember(2), result);                      // :L250
    }

    /// <summary>
    /// The re-read of a CLEARED cell yields <see langword="null"/>, never the empty string
    /// [<c>se_cst_dw.sru:L206</c>: PowerScript's <c>String(null)</c> is null].
    /// </summary>
    [Fact]
    public void TheReReadOfAClearedCellCarriesNullRatherThanEmpty()
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice");
        RecordingSessionState session = new();
        RecordingEventSink sink = new(
            fixture.Log,
            (_, _, _) =>
            {
                fixture.BufferValue = null;
                return RetCode.OK;
            });

        _ = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "IncomingEditText");

        Assert.Equal(1, sink.ChangingRaiseCount);   // :L207 - one-sided null is NOT equal, so it fires
        Assert.Null(sink.LastChangingData);         // :L206 - and the null survives as null
    }

    /// <summary>
    /// The re-read renders EVERY buffer type the way <c>se_cst_dw.sru:L206</c>'s
    /// <c>String(dwo.Primary[row])</c> does, culture invariantly.
    /// </summary>
    /// <param name="writtenByHandler">The typed value the handler puts in the cell.</param>
    /// <param name="expectedRendering">The single text the nested raise must carry.</param>
    /// <remarks>
    /// <para>
    /// A real DataWindow cell holds a typed value, not text - the primary fixture alone has a decimal
    /// salary, two numeric columns and a date [<c>dw_sqlite.srd:L8-L13</c>] - so the string arm is the
    /// exception rather than the rule and every other arm is genuinely reachable at <c>:L206</c>.
    /// </para>
    /// <para>
    /// INVARIANT ON EVERY ARM, deliberately. A host locale with a comma decimal mark or a
    /// non-Gregorian default calendar would otherwise change the text handed to the nested event, and a
    /// characterization recording taken on one machine would stop matching one taken on another.
    /// </para>
    /// <para>
    /// The temporal arms render in the SAME shapes their own coercion arms accept
    /// [<c>:L239</c>, <c>:L241</c>, <c>:L243</c>], so a value that makes the round trip through
    /// <c>:L206</c> and back through the coercion switch survives it.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReReadRenderingRows))]
    public void TheReReadRendersEveryBufferTypeInvariantly(object writtenByHandler, string expectedRendering)
    {
        // An unmatched column type, so `case else`'s coercion cannot write over the handler's value and
        // the only thing under test is what :L206 produced.
        ProtocolFixture fixture = Arrange("blob");
        RecordingSessionState session = new();
        RecordingEventSink sink = new(
            fixture.Log,
            (_, _, _) =>
            {
                fixture.BufferValue = writtenByHandler;
                return RetCode.OK;
            });

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "IncomingEditText");

        // The cell started null and ended non-null, so :L200's both-null arm fails and :L204 fires.
        Assert.Equal(1, sink.ChangingRaiseCount);
        Assert.Equal(expectedRendering, sink.LastChangingData);

        // And nothing was written, because :L244 has no arm for "blob" and :L229's guard failed anyway.
        Assert.Empty(SetItemCalls(fixture.Log));
        Assert.Equal(NamedMember(2), result);   // :L250
    }

    /// <summary>
    /// One row per buffer type a PowerScript <c>any</c> can carry through <c>:L206</c>.
    /// </summary>
    public static TheoryData<object, string> ReReadRenderingRows =>
        new()
        {
            { "AlreadyText", "AlreadyText" },                               // returned unchanged
            { 42L, "42" },                                                  // long
            { 7, "7" },                                                     // int
            { 1234.75m, "1234.75" },                                        // decimal - dw_sqlite.srd:L12
            { 0.5d, "0.5" },                                                // double
            { new DateTime(2020, 2, 29, 10, 30, 45), "2020-02-29 10:30:45" }, // the :L239 shape
            { new DateOnly(2020, 2, 29), "2020-02-29" },                    // the :L241 shape - :L13 birth
            { new TimeOnly(10, 20, 30), "10:20:30" },                       // the :L243 shape
            { true, "true" },                                               // lowercase, as PowerScript
            { false, "false" },
            { new byte[] { 1, 2, 3 }, "System.Byte[]" },                    // a blob cell has no text form
        };

    /// <summary>
    /// The re-read and the nested raise happen BEFORE the dispatch, and therefore for EVERY arm
    /// [<c>se_cst_dw.sru:L204-L208</c> precede <c>:L211</c>].
    /// </summary>
    /// <param name="handlerCode">The code the semantic handler returns, selecting the arm.</param>
    /// <param name="expectedNumeral">The single result that arm produces.</param>
    [Theory]
    [MemberData(nameof(NestedRaisePrecedesEveryArmRows))]
    public void TheNestedRaiseHappensBeforeEveryArmOfTheDispatch(long handlerCode, int expectedNumeral)
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice");
        RecordingSessionState session = new();
        RecordingEventSink sink = new(
            fixture.Log,
            (_, _, _) =>
            {
                fixture.BufferValue = "WrittenByHandler";
                return handlerCode;
            });

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "IncomingEditText");

        Assert.Equal(NamedMember(expectedNumeral), result);
        Assert.Equal(1, sink.ChangingRaiseCount);                // :L207, on every arm
        Assert.Equal("WrittenByHandler", sink.LastChangingData); // :L206, on every arm

        // Ordering, positionally: the semantic raise, then the nested raise, then whatever the arm did.
        Assert.Equal(0, fixture.Log.IndexOf(DoItemChangeRaise));  // :L194
        Assert.Equal(1, fixture.Log.IndexOf(ChangingRaise));      // :L207
    }

    /// <summary>
    /// One row per arm of <c>se_cst_dw.sru:L211-L251</c>, all with an UNEQUAL buffer so the
    /// <c>:L204</c> block runs first in every one.
    /// </summary>
    public static TheoryData<long, int> NestedRaisePrecedesEveryArmRows =>
        new()
        {
            { 1L, 1 },   // :L212 empty arm
            { 2L, 2 },   // :L213 arm, whose restore is blocked by :L216 on this data
            { 3L, 1 },   // :L223-L225 rewrite
            { 0L, 2 },   // :L226 `case else`, whose coercion is blocked by :L229 on this data
        };

    /// <summary>
    /// The nested changing event's return value is DISCARDED [<c>se_cst_dw.sru:L207</c> is a bare
    /// statement].
    /// </summary>
    /// <param name="changingResult">What the nested handler returns.</param>
    /// <remarks>
    /// The value <c>1</c> is the interesting row: everywhere else in <c>se_cst_dw</c> a <c>1</c> from a
    /// raise means "prevent" and is returned straight back to the runtime. Here it means nothing at
    /// all, and a port that honoured it would introduce a prevention path the oracle does not have.
    /// </remarks>
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(-1L)]
    public void TheNestedChangingEventResultIsDiscarded(long changingResult)
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice");
        RecordingSessionState session = new();
        RecordingEventSink sink = new(
            fixture.Log,
            (_, _, _) =>
            {
                fixture.BufferValue = "WrittenByHandler";
                return RetCode.OK;
            })
        {
            ChangingResult = changingResult,
        };

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "IncomingEditText");

        // The same outcome for every possible nested result: :L250's forced 2, reached through
        // `case else` because the SEMANTIC handler returned 0.
        Assert.Equal(NamedMember(2), result);
        Assert.Equal(1, sink.ChangingRaiseCount);
        Assert.Equal(1, sink.ChangedRaiseCount);
        Assert.Equal(RetCode.OK, session.ItemChangeRetCode);   // :L195 stashed the SEMANTIC code only
    }


    // ==============================================================================================
    //  SECTION 5 - THE FOUR-ARM DISPATCH: ASSERT THE LEGACY, NOT THE INTUITIVE
    //  --------------------------------------------------------------------------------------------
    //      :L211  choose case rtCode
    //      :L212      case 1                                <-- EMPTY BODY
    //      :L213      case 2
    //      :L216          if bEqual then
    //      :L219              SetItem(row,Long(dwo.ID),aOrgValue)
    //      :L220              SetItemStatus(row,Long(dwo.ID),Primary!,orgStatus)
    //      :L222          end if
    //      :L223      case 3
    //      :L225          rtCode = 1                        <-- REWRITE
    //      :L226      case else
    //      :L229          if bEqual then ... coercion ... end if
    //      :L247          Event OnDoItemChanged(row,dwo)
    //      :L250          rtCode = 2                        <-- FORCED, unconditionally
    //      :L251  end choose
    //      :L253  return rtCode
    //
    //  ============================================================================================
    //  THE `case 1` QUESTION, NAMED RATHER THAN GLOSSED (constraint C-K)
    //  ============================================================================================
    //  AAP 0.6.1.5 and 0.4.2.5 describe this dispatch as one in which "case 1 falls through to case
    //  2". TAKEN LITERALLY THAT IS INCONSISTENT WITH POWERSCRIPT, and the source at :L211-L222 is the
    //  authority (AAP 0.1.4 makes the legacy source the only specification). The evidence:
    //
    //    1. :L212 is `case 1` with an EMPTY body; :L213 is `case 2` and carries the restore.
    //    2. PowerScript `CHOOSE CASE` has Select-Case semantics: arms are EXCLUSIVE and do not fall
    //       through the way a C `switch` does. An arm with no statements executes nothing.
    //    3. AUTHORIAL INTENT, FROM THIS SAME OBJECT: the author uses the MULTI-VALUE form `case 1,3`
    //       at :L367 inside `ondwnitemvalidationerror`. Had 1 and 2 been meant to share a body here,
    //       it would have been written `case 1,2`, exactly as :L367 was written.
    //    4. BEHAVIOURAL CONSEQUENCE: returning 1 is what TRIGGERS `ondwnitemvalidationerror` [:L210
    //       comment], and that handler performs the restore itself [:L369-L379]. A fall-through would
    //       restore twice.
    //
    //  So the assertion below is that a handler result of 1 restores NOTHING and returns 1. It is NOT
    //  weakened to accommodate the AAP's wording, and the disagreement is named here so a reviewer can
    //  see it was considered rather than missed. The distinction is observable - whether value and
    //  status are restored before the validation-error event runs - so a paired legacy/target
    //  characterization recording is the final arbiter (AAP 0.6.7).
    // ==============================================================================================

    /// <summary>
    /// <c>case 1</c> is an EMPTY arm [<c>se_cst_dw.sru:L212</c>]: nothing runs, nothing is restored,
    /// no event is raised, and the <c>1</c> is returned unchanged.
    /// </summary>
    /// <param name="bufferUnchanged">
    /// Whether the handler leaves the cell alone. Asserted BOTH ways, because the empty arm has no
    /// <c>bEqual</c> guard to be gated by - a fall-through into <c>:L216</c> would behave differently
    /// between these two rows, and this arm must not.
    /// </param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArmOneIsEmptyAndRestoresNothing(bool bufferUnchanged)
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice", ItemStatus.DataModified);
        RecordingSessionState session = new();
        RecordingEventSink sink = new(
            fixture.Log,
            (_, _, _) =>
            {
                if (!bufferUnchanged)
                {
                    fixture.BufferValue = "WrittenByHandler";
                }

                return 1L;
            });

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "IncomingEditText");

        // :L212 then :L253. The 1 is returned exactly as the handler produced it.
        Assert.Equal(NamedMember(1), result);

        // NO restore. This is the assertion the "falls through" reading would break.
        Assert.Empty(SetItemCalls(fixture.Log));
        Assert.Equal(0, fixture.Log.CountOf(SetItemStatusMember));

        // NO changed event either - :L247 belongs to `case else` alone.
        Assert.Equal(0, sink.ChangedRaiseCount);
        Assert.Equal(0, fixture.Log.CountOf(ChangedRaise));

        // The buffer is left exactly as the handler left it, and the status untouched.
        Assert.Equal(bufferUnchanged ? "Alice" : "WrittenByHandler", fixture.BufferValue);
        Assert.Equal(ItemStatus.DataModified, fixture.BufferStatus);

        // And the stash carries the 1 forward for `ondwnitemvalidationerror` to consume [:L195].
        Assert.Equal(1L, session.ItemChangeRetCode);
    }

    /// <summary>
    /// <c>case 2</c> restores the snapshot pair ONLY when the value was unchanged
    /// [<c>se_cst_dw.sru:L213-L222</c>], and returns <c>2</c> either way.
    /// </summary>
    [Fact]
    public void ArmTwoRestoresTheSnapshotPairWhenTheValueWasUnchanged()
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice", ItemStatus.NewModified);
        RecordingSessionState session = new();
        RecordingEventSink sink = new(fixture.Log, 2L);

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "IncomingEditText");

        Assert.Equal(NamedMember(2), result);                          // :L253

        DataWindowCallRecord valueRestore = SingleRecord(fixture.Log, SetItemMember);
        Assert.Equal(RestoreSetItem, valueRestore.Signature);          // :L219 - the `any` overload
        Assert.Equal(FixtureRow, Assert.IsType<long>(valueRestore.Arguments[0]));
        Assert.Equal(fixture.ColumnId, Assert.IsType<long>(valueRestore.Arguments[1]));
        Assert.Equal("Alice", valueRestore.Arguments[2]);              // the :L189 snapshot

        DataWindowCallRecord statusRestore = SingleRecord(fixture.Log, SetItemStatusMember);
        Assert.Equal(ItemStatus.NewModified, Assert.IsType<ItemStatus>(statusRestore.Arguments[3])); // :L220

        // The restore is a VALUE restore, so it must NOT go through a type coercion: `NewModified`
        // survives and no typed SetItem overload was reached.
        Assert.Equal(ItemStatus.NewModified, fixture.BufferStatus);
        Assert.Equal("Alice", fixture.BufferValue);

        // :L247 is not in this arm.
        Assert.Equal(0, sink.ChangedRaiseCount);

        // Ordering: value first, then status - :L219 precedes :L220.
        Assert.True(fixture.Log.IndexOf(RestoreSetItem) < fixture.Log.IndexOf(SetItemStatusMember));
    }

    /// <summary>
    /// <c>case 2</c> restores NOTHING when the buffer already changed - the oracle's own comment at
    /// <c>se_cst_dw.sru:L215</c> says the guard exists to prevent overwriting it.
    /// </summary>
    [Fact]
    public void ArmTwoRestoresNothingWhenTheBufferAlreadyChanged()
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice", ItemStatus.NewModified);
        RecordingSessionState session = new();
        RecordingEventSink sink = new(
            fixture.Log,
            (_, _, _) =>
            {
                fixture.BufferValue = "WrittenByHandler";
                return 2L;
            });

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "IncomingEditText");

        Assert.Equal(NamedMember(2), result);                        // :L253 - the SAME result as above
        Assert.Empty(SetItemCalls(fixture.Log));                     // :L216 guard failed
        Assert.Equal(0, fixture.Log.CountOf(SetItemStatusMember));   // :L220 not reached
        Assert.Equal("WrittenByHandler", fixture.BufferValue);       // the handler's write is preserved
        Assert.Equal(0, sink.ChangedRaiseCount);
    }

    /// <summary>
    /// <c>case 3</c> keeps the value, moves no focus, and REWRITES the result to <c>1</c>
    /// [<c>se_cst_dw.sru:L223-L225</c>] - so <c>3</c> is a value a handler returns and never a value
    /// this routine returns.
    /// </summary>
    /// <param name="bufferUnchanged">Asserted both ways: this arm has no <c>bEqual</c> guard either.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArmThreeKeepsTheValueAndRewritesTheResultToOne(bool bufferUnchanged)
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice", ItemStatus.DataModified);
        RecordingSessionState session = new();
        RecordingEventSink sink = new(
            fixture.Log,
            (_, _, _) =>
            {
                if (!bufferUnchanged)
                {
                    fixture.BufferValue = "WrittenByHandler";
                }

                return 3L;
            });

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "IncomingEditText");

        // :L225 - the rewrite. NOT KeepValueNoFocusMove.
        Assert.Equal(NamedMember(1), result);
        Assert.NotEqual(NamedMember(3), result);

        // "保留值" - the value is KEPT, so nothing is restored and nothing is coerced.
        Assert.Empty(SetItemCalls(fixture.Log));
        Assert.Equal(0, fixture.Log.CountOf(SetItemStatusMember));
        Assert.Equal(bufferUnchanged ? "Alice" : "WrittenByHandler", fixture.BufferValue);

        // :L247 is not in this arm.
        Assert.Equal(0, sink.ChangedRaiseCount);

        // ODDITY O-3: the stash keeps the PRE-rewrite 3 [:L195], so `ondwnitemvalidationerror` still
        // sees 3 and suppresses its own restore [:L369] while this routine's caller sees 1.
        Assert.Equal(3L, session.ItemChangeRetCode);
    }

    /// <summary>
    /// <c>case else</c> FORCIBLY returns <c>2</c> [<c>se_cst_dw.sru:L250</c>] regardless of what the
    /// handler returned, and raises the changed event [<c>:L247</c>] on the way.
    /// </summary>
    /// <param name="handlerCode">
    /// The discarded handler code. Several distinct values, so it is unmistakable that the value is
    /// thrown away rather than coincidentally equal to 2.
    /// </param>
    /// <remarks>
    /// The oracle explains itself at <c>:L248-L249</c>: returning 2 REFUSES the DataWindow's
    /// re-application of the current edit text over the buffer, because the item-changed handler may
    /// already have written the buffer and re-applying the text would overwrite that.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ForcedTwoRows))]
    public void ArmElseForciblyReturnsTwoWhateverTheHandlerReturned(long handlerCode)
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice");
        RecordingSessionState session = new();
        RecordingEventSink sink = new(fixture.Log, handlerCode);

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "Alice");

        Assert.Equal(NamedMember(2), result);                   // :L250, unconditionally
        Assert.Equal(1, sink.ChangedRaiseCount);                // :L247
        Assert.Equal(handlerCode, session.ItemChangeRetCode);   // :L195 kept the ORIGINAL code

        // The coercion ran because the value was unchanged, and it is a TYPED overload - so the log
        // distinguishes it from a restore even though both write to the same cell.
        DataWindowCallRecord coercion = SingleRecord(fixture.Log, SetItemMember);
        Assert.Equal("SetItem(string?)", coercion.Signature);   // :L233
        Assert.NotEqual(RestoreSetItem, coercion.Signature);
    }

    /// <summary>
    /// Codes that reach <c>case else</c> [<c>se_cst_dw.sru:L226</c>]. Zero, one past the alphabet, a
    /// large positive, and both negatives that mean something specific in the return-code algebra.
    /// </summary>
    public static TheoryData<long> ForcedTwoRows =>
        new()
        {
            0L,                // RetCode.OK - the ordinary case
            4L,                // one past the alphabet
            99L,               // arbitrary positive
            -1L,               // RetCode.FAILED - NOT a failure in this alphabet
            -2L,               // RetCode.CANCELLED - likewise
            long.MaxValue,     // the stash field is `long`, so this is reachable
            long.MinValue,
        };

    /// <summary>
    /// <c>OnDoItemChanged</c> is raised in the <c>case else</c> arm and NOWHERE ELSE
    /// [<c>se_cst_dw.sru:L247</c>].
    /// </summary>
    /// <param name="handlerCode">The code selecting the arm.</param>
    /// <param name="expectedChangedRaises">The single raise count that arm produces.</param>
    /// <remarks>
    /// This is the coupling the oracle warns about at <c>:L43</c>: the changed handler drives the
    /// column-expression service [<c>:L313-L315</c>], so an arm that does not reach <c>:L247</c> also
    /// does not recalculate any bound expression. Widening the raise to the other three arms would make
    /// expressions recalculate where the oracle leaves them stale.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ChangedRaiseRows))]
    public void TheChangedEventIsRaisedByTheDefaultArmAlone(long handlerCode, int expectedChangedRaises)
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice");
        RecordingSessionState session = new();
        RecordingEventSink sink = new(fixture.Log, handlerCode);

        _ = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "Alice");

        Assert.Equal(expectedChangedRaises, sink.ChangedRaiseCount);
        Assert.Equal(expectedChangedRaises, fixture.Log.CountOf(ChangedRaise));
    }

    /// <summary>One row per arm: only <c>case else</c> reaches <c>se_cst_dw.sru:L247</c>.</summary>
    public static TheoryData<long, int> ChangedRaiseRows =>
        new()
        {
            { 1L, 0 },   // :L212 - empty arm, no raise
            { 2L, 0 },   // :L213 - restore only, no raise
            { 3L, 0 },   // :L223 - rewrite only, no raise
            { 0L, 1 },   // :L226 - the ONE arm that raises, at :L247
            { 7L, 1 },   // :L226 again, via an unnamed code
        };

    /// <summary>
    /// The complete outer dispatch as one matrix: arm, returned value, whether a restore happened,
    /// whether a coercion happened, and how many times the changed event fired.
    /// </summary>
    /// <param name="handlerCode">The code the semantic handler returns.</param>
    /// <param name="mutateBuffer">Whether the handler also changes the cell, flipping <c>bEqual</c>.</param>
    /// <param name="expectedNumeral">The single alphabet numeral returned.</param>
    /// <param name="expectRestore">Whether <c>:L219-L220</c> ran.</param>
    /// <param name="expectCoercion">Whether <c>:L231-L244</c> wrote through a typed overload.</param>
    /// <param name="expectedChangedRaises">How many times <c>:L247</c> fired.</param>
    [Theory]
    [MemberData(nameof(OuterDispatchMatrix))]
    public void TheOuterDispatchMatrixHoldsArmByArm(
        long handlerCode,
        bool mutateBuffer,
        int expectedNumeral,
        bool expectRestore,
        bool expectCoercion,
        int expectedChangedRaises)
    {
        ProtocolFixture fixture = Arrange(FakeColumnType.CharOf(100), "Alice", ItemStatus.DataModified);
        RecordingSessionState session = new();
        RecordingEventSink sink = new(
            fixture.Log,
            (_, _, _) =>
            {
                if (mutateBuffer)
                {
                    fixture.BufferValue = "WrittenByHandler";
                }

                return handlerCode;
            });

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "Alice");

        Assert.Equal(NamedMember(expectedNumeral), result);
        Assert.Equal(expectedChangedRaises, sink.ChangedRaiseCount);
        Assert.Equal(expectRestore ? 1 : 0, fixture.Log.CountOf(RestoreSetItem));
        Assert.Equal(expectRestore ? 1 : 0, fixture.Log.CountOf(SetItemStatusMember));
        Assert.Equal(expectCoercion ? 1 : 0, fixture.Log.CountOf("SetItem(string?)"));

        // Restore and coercion are mutually exclusive: they live in different arms.
        Assert.False(expectRestore && expectCoercion);
    }

    /// <summary>
    /// The outer dispatch matrix. Each arm appears twice - once with the buffer unchanged and once with
    /// it changed - because <c>bEqual</c> gates <c>:L216</c> and <c>:L229</c> but gates neither
    /// <c>:L212</c> nor <c>:L225</c> nor <c>:L247</c> nor <c>:L250</c>.
    /// </summary>
    public static TheoryData<long, bool, int, bool, bool, int> OuterDispatchMatrix =>
        new()
        {
            // handler, mutate, result, restore, coercion, changed raises
            { 1L, false, 1, false, false, 0 },   // :L212 empty arm
            { 1L, true, 1, false, false, 0 },    // :L212 - and no bEqual sensitivity
            { 2L, false, 2, true, false, 0 },    // :L213 with :L216 satisfied - :L219 and :L220 run
            { 2L, true, 2, false, false, 0 },    // :L213 with :L216 failing - :L215's anti-overwrite
            { 3L, false, 1, false, false, 0 },   // :L225 rewrite
            { 3L, true, 1, false, false, 0 },    // :L225 - and no bEqual sensitivity
            { 0L, false, 2, false, true, 1 },    // :L226 with :L229 satisfied - :L233, :L247, :L250
            { 0L, true, 2, false, false, 1 },    // :L226 with :L229 failing - :L247 and :L250 still run
            { 42L, false, 2, false, true, 1 },   // :L226 via an unnamed code
            { 42L, true, 2, false, false, 1 },
        };


    // ==============================================================================================
    //  SECTION 6 - THE SIX-ARM COERCION TABLE (THE HIGHEST-RISK MATRIX)
    //  --------------------------------------------------------------------------------------------
    //      :L230  //*此处不能调用AcceptText来应用数据 ...   "AcceptText cannot be used here, because a
    //                                                        control like a CheckBox has no [Text] to
    //                                                        accept"
    //      :L231  choose case Left(dwo.ColType,5)
    //      :L232      case "char","char("   -> SetItem(row,Long(dwo.ID),data)            <-- NO conversion
    //      :L234      case "decim","real","numbe" -> SetItem(..., Dec(data))
    //      :L236      case "long","ulong"   -> SetItem(..., Long(data))
    //      :L238      case "datet"          -> SetItem(..., DateTime(data))
    //      :L240      case "date"           -> SetItem(..., Date(data))
    //      :L242      case "time"           -> SetItem(..., Time(data))
    //      :L244  end choose                                                             <-- NO `case else`
    //
    //  FOUR PROPERTIES MAKE THIS THE MOST DANGEROUS TABLE IN THE PORT:
    //
    //    (a) SIX ARMS AND NO DEFAULT. An unmatched column type writes NOTHING - not a default, not an
    //        error, not the raw text. Adding a `case else` would start writing cells the oracle leaves
    //        untouched.
    //    (b) `Left(colType,5)` IS COMPARED FOR EQUALITY, NOT AS A PREFIX. `choose case` compares
    //        equality, so `datetime` truncates to "datet" and matches :L238 ONLY. A `StartsWith` port
    //        would send every datetime column down :L240's date arm and silently truncate the time.
    //        The same trap catches "chars", "longlong", "numeric" and "timestamp", none of which
    //        matches any arm.
    //    (c) `Left(s,5)` IS NOT `Substring(0,5)`. PowerScript's `Left` returns the whole string when it
    //        is shorter than five characters; `Substring` would throw. That is why "char", "real",
    //        "long", "date" and "time" - all four-or-five characters - work at all.
    //    (d) THE `numbe` ARM COERCES TO DECIMAL, NOT TO INTEGER. `n_cst_dwsvc.sru:L503-L518`'s
    //        `_of_convertcoltype` classifies "numbe" as INTEGER, so the two switches in this library
    //        DISAGREE. This one is authoritative for coercion, and "harmonising" them would truncate
    //        the fractional part of every `number` column - including `salary`'s siblings in the
    //        primary fixture.
    //
    //  The primary fixture ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L13 exercises three of the six
    //  arms (char( twice, numbe twice, decim, date); the remaining arms are driven with synthetic types.
    // ==============================================================================================

    /// <summary>
    /// Every arm of <c>se_cst_dw.sru:L231-L244</c>, plus the unmatched types that reach no arm at all.
    /// </summary>
    /// <param name="colType">The raw <c>dwo.ColType</c> string.</param>
    /// <param name="editText">The edit text the routine coerces.</param>
    /// <param name="expectedSignature">
    /// The single <c>SetItem</c> overload the owning arm reaches, or <see cref="NoSetItemExpected"/>.
    /// </param>
    /// <param name="expectedClrTypeName">The CLR type the coerced value is stored as.</param>
    /// <param name="expectedRendering">The stored value, rendered culture invariantly.</param>
    [Theory]
    [MemberData(nameof(CoercionMatrix))]
    public void TheSixArmCoercionTableHoldsArmByArm(
        string colType,
        string editText,
        string expectedSignature,
        string expectedClrTypeName,
        string expectedRendering)
    {
        // Seeded with the edit text itself so :L198 finds the values equal and :L229's guard passes -
        // which is the only way the coercion becomes reachable at all.
        ProtocolFixture fixture = Arrange(colType, editText);
        RecordingSessionState session = new();

        // RetCode.OK is outside {1,2,3}, so dispatch lands on `case else` [:L226].
        RecordingEventSink sink = new(fixture.Log, RetCode.OK);

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            editText);

        // :L250 forces 2 and :L247 raises the changed event on EVERY row, matched or not, because both
        // sit OUTSIDE the `if bEqual` block that contains the coercion.
        Assert.Equal(NamedMember(2), result);
        Assert.Equal(1, sink.ChangedRaiseCount);

        IReadOnlyList<DataWindowCallRecord> writes = SetItemCalls(fixture.Log);

        if (string.Equals(expectedSignature, NoSetItemExpected, StringComparison.Ordinal))
        {
            // PROPERTY (a): :L244 has no `case else`, so an unmatched type writes nothing whatsoever,
            // and the cell is left holding exactly what it held before the routine ran.
            Assert.Empty(writes);
            Assert.Equal(editText, Assert.IsType<string>(fixture.BufferValue));
            Assert.Equal(NoSetItemExpected, expectedClrTypeName);
            Assert.Equal(NoSetItemExpected, expectedRendering);
        }
        else
        {
            DataWindowCallRecord write = Assert.Single(writes);
            Assert.Equal(expectedSignature, write.Signature);
            Assert.Equal(FixtureRow, Assert.IsType<long>(write.Arguments[0]));
            Assert.Equal(fixture.ColumnId, Assert.IsType<long>(write.Arguments[1]));

            // The coercion is never the `any` overload - that one belongs to the restore at :L219.
            Assert.NotEqual(RestoreSetItem, write.Signature);

            object? stored = fixture.BufferValue;
            Assert.Equal(expectedClrTypeName, ClrTypeName(stored));
            Assert.Equal(expectedRendering, RenderInvariant(stored));
        }
    }

    /// <summary>
    /// The coercion matrix. Rows marked FIXTURE use a column type taken verbatim from
    /// <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L13</c>; the rest are synthetic types for the
    /// arms the fixture does not exercise, plus the near-miss types that a <c>StartsWith</c> port would
    /// wrongly claim.
    /// </summary>
    public static TheoryData<string, string, string, string, string> CoercionMatrix
    {
        get
        {
            TheoryData<string, string, string, string, string> rows = [];
            foreach (CoercionExpectation expectation in CoercionExpectations)
            {
                rows.Add(
                    expectation.ColType,
                    expectation.EditText,
                    expectation.Signature,
                    expectation.ClrTypeName,
                    expectation.Rendering);
            }

            return rows;
        }
    }

    /// <summary>One row of the coercion matrix, named so the self audit in Section 8 can read it.</summary>
    /// <param name="ColType">The raw <c>dwo.ColType</c> string, exactly as a DataWindow reports it.</param>
    /// <param name="EditText">The edit text the arm coerces.</param>
    /// <param name="Signature">
    /// The <c>SetItem</c> overload the owning arm reaches, or <see cref="NoSetItemExpected"/> when
    /// <c>se_cst_dw.sru:L244</c>'s missing <c>case else</c> means no arm claims the type.
    /// </param>
    /// <param name="ClrTypeName">The CLR type the coerced value is stored as.</param>
    /// <param name="Rendering">The stored value, rendered culture invariantly.</param>
    private readonly record struct CoercionExpectation(
        string ColType,
        string EditText,
        string Signature,
        string ClrTypeName,
        string Rendering);

    /// <summary>
    /// The coercion expectations, held as a typed table so both the theory and the Section 8 coverage
    /// audit read the SAME rows. Two independent copies of this table could disagree, and the copy the
    /// audit read would be the one that looked complete.
    /// </summary>
    private static readonly CoercionExpectation[] CoercionExpectations =
    [
        // ---- :L232 `case "char","char("` - the ONLY arm with no conversion function at all ---------
        new("char", "MiXeD  ", "SetItem(string?)", "String", "MiXeD  "),
        new("char(100)", "Alice", "SetItem(string?)", "String", "Alice"),            // FIXTURE :L9 name
        new("char(200)", "  1 Long Road  ", "SetItem(string?)", "String", "  1 Long Road  "), // FIXTURE :L11
        new("char(1)", "overlong", "SetItem(string?)", "String", "overlong"),        // no width check - Section 7

        // ---- :L234 `case "decim","real","numbe"` -> Dec(data) -------------------------------------
        new("decimal(2)", "1234.75", "SetItem(decimal?)", "Decimal", "1234.75"),     // FIXTURE :L12 salary
        new("real", "-0.5", "SetItem(decimal?)", "Decimal", "-0.5"),
        new("number", "42", "SetItem(decimal?)", "Decimal", "42"),                   // FIXTURE :L8 id / :L10 age
        new("number", "42.75", "SetItem(decimal?)", "Decimal", "42.75"),             // PROPERTY (d): NOT truncated

        // ---- :L236 `case "long","ulong"` -> Long(data) --------------------------------------------
        new("long", "2147483648", "SetItem(long?)", "Int64", "2147483648"),
        new("ulong", "4294967295", "SetItem(long?)", "Int64", "4294967295"),

        // ---- :L238 `case "datet"` -> DateTime(data) ----------------------------------------------
        new("datetime", "2020-02-29 10:30:00", "SetItem(DateTime?)", "DateTime", "2020-02-29 10:30:00"),

        // ---- :L240 `case "date"` -> Date(data) ---------------------------------------------------
        new("date", "2020-02-29", "SetItem(DateOnly?)", "DateOnly", "2020-02-29"),    // FIXTURE :L13 birth

        // ---- :L242 `case "time"` -> Time(data) ---------------------------------------------------
        new("time", "10:20:30", "SetItem(TimeOnly?)", "TimeOnly", "10:20:30"),

        // ---- NO ARM AT ALL: :L244 has no `case else` ---------------------------------------------
        // PROPERTY (b). Every one of these would be wrongly claimed by a `StartsWith` port.
        new("chars", "x", NoSetItemExpected, NoSetItemExpected, NoSetItemExpected),        // not "char"
        new("character", "x", NoSetItemExpected, NoSetItemExpected, NoSetItemExpected),    // "chara"
        new("longlong", "1", NoSetItemExpected, NoSetItemExpected, NoSetItemExpected),     // "longl"
        new("numeric", "1", NoSetItemExpected, NoSetItemExpected, NoSetItemExpected),      // "numer"
        new("timestamp", "10:20:30", NoSetItemExpected, NoSetItemExpected, NoSetItemExpected), // "times"
        new("dates", "2020-02-29", NoSetItemExpected, NoSetItemExpected, NoSetItemExpected),   // "dates"
        new("blob", "x", NoSetItemExpected, NoSetItemExpected, NoSetItemExpected),
        new("", "x", NoSetItemExpected, NoSetItemExpected, NoSetItemExpected),             // Left("",5) is ""
        new("CHAR", "x", NoSetItemExpected, NoSetItemExpected, NoSetItemExpected),         // case sensitive
        new("Date", "2020-02-29", NoSetItemExpected, NoSetItemExpected, NoSetItemExpected),
    ];

    /// <summary>
    /// THE CRITICAL NEGATIVE PAIR: <c>datetime</c> takes the <c>"datet"</c> arm [<c>:L238</c>] and
    /// <c>date</c> takes the <c>"date"</c> arm [<c>:L240</c>], asserted together so neither can be
    /// broken independently.
    /// </summary>
    /// <param name="colType">Either <c>datetime</c> or <c>date</c>.</param>
    /// <param name="editText">Text valid for that arm.</param>
    /// <param name="expectedSignature">The overload the correct arm reaches.</param>
    /// <param name="expectedClrTypeName">The CLR type the correct arm stores.</param>
    /// <param name="expectedRendering">The stored value, rendered culture invariantly.</param>
    /// <remarks>
    /// The concrete regression a <c>StartsWith</c> port causes is a SILENT DATA LOSS rather than a
    /// failure: <c>"datetime".StartsWith("date")</c> is true, so every datetime column would take
    /// <c>:L240</c>, store a <see cref="DateOnly"/>, and drop the time component with no error anywhere.
    /// A row-count assertion, a "did it write" assertion and a "did it return 2" assertion all pass
    /// through that bug unharmed, which is why the pair is pinned by CLR type and by rendered value.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DateFamilyDisambiguation))]
    public void TheDateFamilyArmsAreMatchedByExactEqualityAndNotByPrefix(
        string colType,
        string editText,
        string expectedSignature,
        string expectedClrTypeName,
        string expectedRendering)
    {
        ProtocolFixture fixture = Arrange(colType, editText);
        RecordingSessionState session = new();
        RecordingEventSink sink = new(fixture.Log, RetCode.OK);

        _ = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            editText);

        DataWindowCallRecord write = Assert.Single(SetItemCalls(fixture.Log));
        Assert.Equal(expectedSignature, write.Signature);
        Assert.Equal(expectedClrTypeName, ClrTypeName(fixture.BufferValue));
        Assert.Equal(expectedRendering, RenderInvariant(fixture.BufferValue));
    }

    /// <summary>The two date-family rows, in one theory so they stand or fall together.</summary>
    public static TheoryData<string, string, string, string, string> DateFamilyDisambiguation =>
        new()
        {
            // :L238 - "datetime" truncates to "datet", so the TIME SURVIVES.
            { "datetime", "2020-02-29 10:30:45", "SetItem(DateTime?)", "DateTime", "2020-02-29 10:30:45" },

            // :L240 - "date" is four characters, so Left(_,5) returns it unchanged and it matches here.
            { "date", "2020-02-29", "SetItem(DateOnly?)", "DateOnly", "2020-02-29" },
        };

    /// <summary>
    /// A <c>datetime</c> column keeps its full time component - the exact value a prefix-matching port
    /// would silently truncate [<c>se_cst_dw.sru:L238-L241</c>].
    /// </summary>
    [Fact]
    public void ADateTimeColumnKeepsItsTimeComponent()
    {
        ProtocolFixture fixture = Arrange("datetime", "2020-02-29 10:30:45");
        RecordingSessionState session = new();
        RecordingEventSink sink = new(fixture.Log, RetCode.OK);

        _ = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "2020-02-29 10:30:45");

        DateTime stored = Assert.IsType<DateTime>(fixture.BufferValue);
        Assert.Equal(new DateTime(2020, 2, 29, 10, 30, 45, DateTimeKind.Unspecified), stored);
        Assert.Equal(10, stored.Hour);
        Assert.Equal(30, stored.Minute);
        Assert.Equal(45, stored.Second);

        // And the two arms really are owned by different validators, tested at the predicate level so
        // the ownership cannot drift even if the protocol's dispatch is rewritten.
        Assert.True(DateTimeValidator.MatchesColumnType("datetime"));   // :L238
        Assert.False(DateValidator.MatchesColumnType("datetime"));      // NOT :L240 - the StartsWith trap
        Assert.True(DateValidator.MatchesColumnType("date"));           // :L240
        Assert.False(DateTimeValidator.MatchesColumnType("date"));      // NOT :L238
    }

    /// <summary>
    /// The protocol and the five validators agree on the token sets, so the table cannot drift into two
    /// versions [<c>se_cst_dw.sru:L232-L243</c>].
    /// </summary>
    [Fact]
    public void TheValidatorsPublishExactlyTheOraclesTokenSets()
    {
        string[] charTokens = ["char", "char("];                 // :L232
        string[] decimalTokens = ["decim", "real", "numbe"];      // :L234
        string[] longTokens = ["long", "ulong"];                 // :L236
        string[] dateTimeTokens = ["datet"];                     // :L238
        string[] dateTokens = ["date"];                          // :L240
        string[] timeTokens = ["time"];                          // :L242

        Assert.Equal<IEnumerable<string>>(charTokens, StringValidator.ColTypeTokens);
        Assert.Equal<IEnumerable<string>>(decimalTokens, NumberValidator.DecimalCoercionColumnTypePrefixes);
        Assert.Equal<IEnumerable<string>>(longTokens, NumberValidator.LongCoercionColumnTypePrefixes);
        Assert.Equal<IEnumerable<string>>(dateTimeTokens, DateTimeValidator.ColumnTypePrefixes);
        Assert.Equal<IEnumerable<string>>(dateTokens, DateValidator.ColumnTypePrefixes);
        Assert.Equal<IEnumerable<string>>(timeTokens, TimeValidator.ColumnTypeTokens);

        // Ten tokens across six arms, every one of them at most five characters long - which is what
        // makes `Left(colType,5)` able to match them at all.
        int total = charTokens.Length
            + decimalTokens.Length
            + longTokens.Length
            + dateTimeTokens.Length
            + dateTokens.Length
            + timeTokens.Length;
        Assert.Equal(10, total);

        // The named constants agree with the literals above, so a rename cannot silently change a token.
        Assert.Equal("char", StringValidator.ColTypeTokenChar);
        Assert.Equal("char(", StringValidator.ColTypeTokenParameterizedChar);
        Assert.Equal("datet", DateTimeValidator.ColumnTypePrefix);
        Assert.Equal("date", DateValidator.ColumnTypePrefix);
        Assert.Equal("time", TimeValidator.ColumnTypeToken);

        // And the five-character truncation length is the same everywhere.
        Assert.Equal(5, StringValidator.ColTypePrefixLength);
        Assert.Equal(5, NumberValidator.ColumnTypePrefixLength);
        Assert.Equal(5, DateValidator.ColumnTypePrefixLength);
    }

    /// <summary>
    /// Each of the ten tokens is claimed by EXACTLY ONE arm, so the six arms partition rather than
    /// overlap [<c>se_cst_dw.sru:L231-L244</c>: <c>choose case</c> arms are exclusive].
    /// </summary>
    /// <param name="token">One of the ten arm tokens, or a near miss that no arm may claim.</param>
    /// <param name="expectedOwners">The single number of validators that must claim it.</param>
    [Theory]
    [MemberData(nameof(TokenOwnershipRows))]
    public void EachColumnTypeTokenIsClaimedByExactlyOneArm(string token, int expectedOwners)
    {
        int owners = 0;
        if (StringValidator.OwnsColType(token))
        {
            owners++;
        }

        if (NumberValidator.IsDecimalCoercionColumnType(token))
        {
            owners++;
        }

        if (NumberValidator.IsLongCoercionColumnType(token))
        {
            owners++;
        }

        if (DateTimeValidator.MatchesColumnType(token))
        {
            owners++;
        }

        if (DateValidator.MatchesColumnType(token))
        {
            owners++;
        }

        if (TimeValidator.MatchesColumnType(token))
        {
            owners++;
        }

        Assert.Equal(expectedOwners, owners);
    }

    /// <summary>
    /// The ten owned tokens, the six real fixture column types, and the near misses no arm may claim.
    /// </summary>
    public static TheoryData<string, int> TokenOwnershipRows =>
        new()
        {
            // The ten tokens of :L232-L243, each owned exactly once.
            { "char", 1 },
            { "char(", 1 },
            { "decim", 1 },
            { "real", 1 },
            { "numbe", 1 },
            { "long", 1 },
            { "ulong", 1 },
            { "datet", 1 },
            { "date", 1 },
            { "time", 1 },

            // The six real fixture column types [dw_sqlite.srd:L8-L13], each owned exactly once.
            { "number", 1 },
            { "char(100)", 1 },
            { "char(200)", 1 },
            { "decimal(2)", 1 },
            { "datetime", 1 },

            // Near misses. Every one is owned by NOBODY, which is what :L244's missing `case else`
            // means and what a `StartsWith` port would get wrong.
            { "chars", 0 },
            { "character", 0 },
            { "longlong", 0 },
            { "numeric", 0 },
            { "timestamp", 0 },
            { "dates", 0 },
            { "blob", 0 },
            { "", 0 },
            { "CHAR", 0 },
            { "Number", 0 },
        };

    /// <summary>
    /// The coercions are LEGACY FAITHFUL AND UNCHECKED: uncoercible text is written silently, with no
    /// exception, no error result and no reporting of any kind [<c>se_cst_dw.sru:L233-L243</c>].
    /// </summary>
    /// <param name="colType">The column type selecting the arm.</param>
    /// <param name="editText">Text the arm's conversion cannot parse.</param>
    /// <param name="expectedSignature">The overload the arm reaches anyway.</param>
    /// <param name="expectedClrTypeName">
    /// What lands in the cell. Five of the six arms yield the typed null; the time arm alone yields a
    /// non-null fallback, which is the ported implementation's recorded choice for
    /// <c>Time()</c> of unparseable text.
    /// </param>
    /// <remarks>
    /// The oracle passes each conversion's result straight into <c>SetItem</c> without inspecting it -
    /// there is no <c>IsDate</c>, no <c>IsNumber</c> and no failure branch anywhere in
    /// <c>:L231-L244</c>. A port that threw on unparseable input would introduce an error path the
    /// legacy does not have, and one that skipped the write would leave the cell holding the stale
    /// value where the oracle clears it. Both are behaviour changes, so neither is made.
    /// </remarks>
    [Theory]
    [MemberData(nameof(UncoercibleRows))]
    public void UncoercibleEditTextIsWrittenSilentlyByTheOwningArm(
        string colType,
        string editText,
        string expectedSignature,
        string expectedClrTypeName)
    {
        ProtocolFixture fixture = Arrange(colType, editText);
        RecordingSessionState session = new();
        RecordingEventSink sink = new(fixture.Log, RetCode.OK);

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            editText);

        // No exception, and the ordinary `case else` outcome: coerce, raise, force 2.
        Assert.Equal(NamedMember(2), result);
        Assert.Equal(1, sink.ChangedRaiseCount);

        DataWindowCallRecord write = Assert.Single(SetItemCalls(fixture.Log));
        Assert.Equal(expectedSignature, write.Signature);
        Assert.Equal(expectedClrTypeName, ClrTypeName(fixture.BufferValue));
    }

    /// <summary>
    /// Uncoercible rows, one per converting arm. The <c>char</c> arm is absent by construction: it
    /// applies no conversion at all [<c>:L233</c>], so no text is uncoercible for it.
    /// </summary>
    public static TheoryData<string, string, string, string> UncoercibleRows =>
        new()
        {
            { "number", "not a number", "SetItem(decimal?)", "(null)" },          // :L235 Dec("...")
            { "decimal(2)", "1e400", "SetItem(decimal?)", "(null)" },             // :L235 - overflows
            { "long", "12.34", "SetItem(long?)", "(null)" },                      // :L237 Long("12.34")
            { "datetime", "not a datetime", "SetItem(DateTime?)", "(null)" },     // :L239 DateTime("...")
            { "date", "not a date", "SetItem(DateOnly?)", "(null)" },             // :L241 Date("...")
            { "time", "not a time", "SetItem(TimeOnly?)", "TimeOnly" },           // :L243 Time("...")
        };

    /// <summary>
    /// The time arm's uncoercible outcome is the validator's published fallback, pinned by value so it
    /// cannot drift silently [<c>se_cst_dw.sru:L243</c>].
    /// </summary>
    /// <remarks>
    /// Recorded rather than asserted as proven parity: what PowerScript's <c>Time()</c> yields for
    /// unparseable text is not stated anywhere in the repository, so the ported value is a
    /// verify-against-oracle item. Pinning it here is what makes a future correction a deliberate,
    /// visible change rather than an accident.
    /// </remarks>
    [Fact]
    public void TheTimeArmsUncoercibleFallbackIsPinnedToTheValidatorsPublishedValue()
    {
        ProtocolFixture fixture = Arrange("time", "not a time");
        RecordingSessionState session = new();
        RecordingEventSink sink = new(fixture.Log, RetCode.OK);

        _ = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "not a time");

        Assert.Equal(TimeValidator.UncoercibleFallback, Assert.IsType<TimeOnly>(fixture.BufferValue));
    }

    /// <summary>
    /// <c>Left(colType,5)</c> is a LENGTH-TOLERANT truncation, not <c>Substring(0,5)</c>: a shorter
    /// column type comes back whole rather than throwing [<c>se_cst_dw.sru:L231</c>].
    /// </summary>
    /// <param name="colType">The raw column type.</param>
    /// <param name="expectedPrefix">The single truncation result.</param>
    [Theory]
    [MemberData(nameof(TruncationRows))]
    public void TheColumnTypeTruncationIsLengthTolerant(string colType, string expectedPrefix)
    {
        // Asserted through three independent implementations of the same `Left(_,5)` so none can drift:
        // the string arm's, the number arm's, and the fake's own helper.
        Assert.Equal(expectedPrefix, StringValidator.ColTypePrefix(colType));
        Assert.Equal(expectedPrefix, NumberValidator.ColumnTypePrefix(colType));
        Assert.Equal(expectedPrefix, DateValidator.TruncateColumnType(colType));
        Assert.Equal(expectedPrefix, FakeColumnType.CoercionPrefix(colType));
    }

    /// <summary>
    /// Truncation rows, including the four-character tokens that only work because <c>Left</c> tolerates
    /// a short subject, and the empty string.
    /// </summary>
    public static TheoryData<string, string> TruncationRows =>
        new()
        {
            { "char(100)", "char(" },       // dw_sqlite.srd:L9
            { "char(200)", "char(" },       // dw_sqlite.srd:L11
            { "decimal(2)", "decim" },      // dw_sqlite.srd:L12
            { "number", "numbe" },          // dw_sqlite.srd:L8, :L10
            { "date", "date" },             // dw_sqlite.srd:L13 - FOUR characters, returned whole
            { "datetime", "datet" },
            { "char", "char" },             // four characters
            { "real", "real" },             // four characters
            { "long", "long" },             // four characters
            { "time", "time" },             // four characters
            { "ulong", "ulong" },           // exactly five
            { "", "" },                     // Left("",5) is ""
        };


    // ==============================================================================================
    //  SECTION 7 - THE DORMANT COMMENTED CHECK MUST STAY DORMANT
    //  --------------------------------------------------------------------------------------------
    //  `ondoitemchange` [se_cst_dw.sru:L256-L293] is the semantic handler this protocol raises at
    //  :L194. Its executable body is ONE STATEMENT, :L292 `return Event ItemChanged(row,dwo,data)`.
    //  Everything from :L280 to :L290 is inside a `/* ... */` comment in the oracle:
    //
    //      /*if data <> "" then
    //          //检查字符串输入长度是否合法       "check whether the input string length is legal"
    //          sProp = dwo.ColType
    //          if Left(sProp,5) = "char(" then
    //              nDBLimit = Long(Mid(sProp,6,Len(sProp) - 6))
    //              if LenA(data) > nDBLimit then
    //                  MessageBoxEx("超出最大允许的长度，...个字节(每汉字占2个字节)!",StopSign!)
    //                  return 2
    //              end if
    //          end if
    //      end if*/
    //
    //  AAP 0.6.1.5 requires it be "carried across as commented and inert, not revived", and reviving it
    //  would be exactly the silent correction constraint C-B forbids. It is worth being precise about
    //  WHY the difference is observable, because the naive reading is that both paths return 2:
    //
    //      DORMANT PATH, if revived: the SEMANTIC HANDLER returns 2, so the outer dispatch takes
    //          `case 2` [:L213] - which RESTORES the original value through the `any` overload and
    //          does NOT raise the changed event at :L247.
    //      LIVE PATH: the semantic handler returns whatever `ItemChanged` returned - 0 by default - so
    //          the dispatch takes `case else` [:L226], COERCES the full untruncated text through a
    //          TYPED overload, RAISES the changed event, and only then forces the result to 2.
    //
    //  So the three discriminators are: which SetItem overload was reached, whether the changed event
    //  fired, and what the cell ends up holding. All three are asserted below. Also asserted: the
    //  dialog text never appears anywhere, because a structured-error port of a COMMENTED dialog would
    //  be the same revival wearing a different coat.
    // ==============================================================================================

    /// <summary>
    /// Over-length text for a <c>char(n)</c> column is accepted IN FULL: the dormant byte-length
    /// validation at <c>se_cst_dw.sru:L280-L290</c> is inert.
    /// </summary>
    /// <param name="colType">A parameterized <c>char</c> column type, whose width <c>:L284</c> would parse.</param>
    /// <param name="declaredWidth">The width declared in <paramref name="colType"/>.</param>
    /// <param name="editText">Text whose ANSI byte length exceeds <paramref name="declaredWidth"/>.</param>
    [Theory]
    [MemberData(nameof(OverLengthRows))]
    public void TheCommentedByteLengthValidationIsInert(string colType, int declaredWidth, string editText)
    {
        // The premise of the row, stated machine-checkably: `LenA(data) > nDBLimit` at :L285 WOULD have
        // been true. Without this the test could pass for the trivial reason that the input fits.
        Assert.True(
            AnsiByteCount(editText) > declaredWidth,
            "This row must supply text that the dormant check would have rejected.");

        ProtocolFixture fixture = Arrange(colType, editText);
        RecordingSessionState session = new();

        // The semantic handler returns 0 - what `ItemChanged` returns by default - which is what the
        // LIVE :L292 body produces. A revived :L287 would have returned 2 instead.
        RecordingEventSink sink = new(fixture.Log, RetCode.OK);

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            editText);

        // The result IS 2 - but reached through :L250's forced rewrite, not through :L287.
        Assert.Equal(NamedMember(2), result);

        // DISCRIMINATOR 1: the TYPED string overload of :L233, never the `any` overload of :L219.
        DataWindowCallRecord write = Assert.Single(SetItemCalls(fixture.Log));
        Assert.Equal("SetItem(string?)", write.Signature);
        Assert.NotEqual(RestoreSetItem, write.Signature);
        Assert.Equal(0, fixture.Log.CountOf(SetItemStatusMember));

        // DISCRIMINATOR 2: :L247 fired, which `case 2` never does.
        Assert.Equal(1, sink.ChangedRaiseCount);

        // DISCRIMINATOR 3: the FULL text landed in the cell - not truncated to `declaredWidth`, not
        // rejected, not replaced. :L233 applies no conversion and no width check whatsoever.
        Assert.Equal(editText, Assert.IsType<string>(fixture.BufferValue));
        Assert.Equal(editText, write.Arguments[2]);
        Assert.True(editText.Length > 0);

        // And the stash carries the handler's own 0 forward, not the dormant path's 2 [:L195].
        Assert.Equal(RetCode.OK, session.ItemChangeRetCode);

        // The dialog text of :L286 appears NOWHERE - not as a message, not as a structured error, not
        // as an argument. A revival dressed as a structured error would be caught here.
        foreach (string description in fixture.Log.Descriptions)
        {
            Assert.DoesNotContain(DormantOverLengthMessagePrefix, description);
        }
    }

    /// <summary>
    /// Over-length rows. The Chinese row matters specifically because <c>:L285</c> measures with
    /// <c>LenA</c> - ANSI BYTES, two per Chinese character, exactly as <c>:L286</c>'s own message text
    /// says - so it exceeds its declared width on bytes while being short in characters.
    /// </summary>
    public static TheoryData<string, int, string> OverLengthRows =>
        new()
        {
            { "char(4)", 4, "abcdefghij" },                  // 10 ASCII bytes into 4
            { "char(2)", 2, "汉字" },                         // 2 characters, 4 ANSI bytes, into 2
            { "char(1)", 1, "汉" },                           // 1 character, 2 ANSI bytes, into 1
            { "char(100)", 100, new string('x', 250) },       // dw_sqlite.srd:L9 name, far over
            { "char(200)", 200, new string('y', 500) },       // dw_sqlite.srd:L11 address, far over
        };

    /// <summary>
    /// The unparameterized <c>char</c> arm has no width to parse at all, so the dormant check could not
    /// have applied to it even if revived [<c>se_cst_dw.sru:L283</c> tests for <c>"char("</c>
    /// specifically].
    /// </summary>
    [Fact]
    public void TheUnparameterizedCharArmHasNoWidthForTheDormantCheckToHaveUsed()
    {
        string editText = new('z', 4096);
        ProtocolFixture fixture = Arrange("char", editText);
        RecordingSessionState session = new();
        RecordingEventSink sink = new(fixture.Log, RetCode.OK);

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            editText);

        Assert.Equal(NamedMember(2), result);                                    // :L250
        DataWindowCallRecord write = Assert.Single(SetItemCalls(fixture.Log));
        Assert.Equal("SetItem(string?)", write.Signature);                       // :L233
        Assert.Equal(4096, Assert.IsType<string>(fixture.BufferValue).Length);   // nothing truncated
        Assert.Equal(1, sink.ChangedRaiseCount);                                 // :L247
    }

    /// <summary>
    /// The live <c>ondoitemchange</c> body does ONLY what <c>se_cst_dw.sru:L292</c> does: delegate to
    /// the semantic <c>ItemChanged</c> event and return its result.
    /// </summary>
    /// <param name="itemChangedResult">What the semantic <c>ItemChanged</c> event returns.</param>
    /// <param name="expectedNumeral">The single alphabet numeral the protocol therefore produces.</param>
    /// <remarks>
    /// Driven through <see cref="SemanticDelegatingSink"/>, whose whole handler body is that one
    /// delegation, so the assertion is that the protocol dispatches on the delegated value ALONE - no
    /// length test, no truncation, no dialog, and no second consultation of anything.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SemanticDelegationRows))]
    public void TheLiveSemanticHandlerOnlyDelegatesToItemChangedAndReturnsItsResult(
        long itemChangedResult,
        int expectedNumeral)
    {
        ProtocolFixture fixture = Arrange("char(4)", "abcdefghij");
        RecordingSessionState session = new();
        SemanticDelegatingSink sink = new(fixture.Host);

        // The ONLY thing that decides the outcome is what the semantic event returns.
        fixture.Host.ItemChangedHandler = (_, _, _) => itemChangedResult;

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            "abcdefghij");

        Assert.Equal(NamedMember(expectedNumeral), result);

        // :L292 raised the semantic event exactly once, with the ORIGINAL data - so nothing inspected
        // it, trimmed it, truncated it or re-raised on a modified copy.
        DataWindowCallRecord raised = SingleRecord(fixture.Log, ItemChangedRaise);
        Assert.Equal(ItemChangedRaise, raised.Signature);
        Assert.Equal(FixtureRow, Assert.IsType<long>(raised.Arguments[0]));
        Assert.Equal("abcdefghij", raised.Arguments[2]);
        Assert.Equal(1, fixture.Log.CountOf(ItemChangedRaise));

        // And the delegated value is what got stashed at :L195, unclassified and unmodified.
        Assert.Equal(itemChangedResult, session.ItemChangeRetCode);
    }

    /// <summary>
    /// Semantic delegation rows: every arm of the outer dispatch, reached purely by what
    /// <c>ItemChanged</c> returned.
    /// </summary>
    public static TheoryData<long, int> SemanticDelegationRows =>
        new()
        {
            { 0L, 2 },     // :L292 returns 0 -> :L226 `case else` -> :L250 forces 2
            { 1L, 1 },     // :L292 returns 1 -> :L212 empty arm -> 1 unchanged
            { 2L, 2 },     // :L292 returns 2 -> :L213 restore arm -> 2
            { 3L, 1 },     // :L292 returns 3 -> :L225 rewrite -> 1
            { -1L, 2 },    // :L292 returns RetCode.FAILED -> :L226 -> :L250
            { 77L, 2 },    // :L292 returns an unnamed code -> :L226 -> :L250
        };

    /// <summary>
    /// Over-length text routed through the LIVE delegating handler still takes <c>case else</c>, which
    /// is the same conclusion as <see cref="TheCommentedByteLengthValidationIsInert"/> reached through
    /// the real delegation rather than through a fixed handler code.
    /// </summary>
    [Fact]
    public void OverLengthTextRoutedThroughTheLiveHandlerStillTakesTheDefaultArm()
    {
        const string editText = "abcdefghij";
        ProtocolFixture fixture = Arrange("char(4)", editText);
        RecordingSessionState session = new();
        SemanticDelegatingSink sink = new(fixture.Host);

        // No handler installed, so `DataWindowServiceHost.ItemChanged` returns its own default - the
        // continue value of the prevent convention.
        Assert.Null(fixture.Host.ItemChangedHandler);

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(
            fixture.Host,
            session,
            sink,
            FixtureRow,
            fixture.Dwo,
            editText);

        Assert.Equal(RetCode.OK, session.ItemChangeRetCode);                    // :L292 delegated 0
        Assert.Equal(NamedMember(2), result);                                  // :L226 then :L250
        Assert.Equal(1, sink.ChangedRaiseCount);                               // :L247
        Assert.Equal(0, sink.ChangingRaiseCount);                              // :L204 - value unchanged
        Assert.Equal(0, fixture.Log.CountOf(RestoreSetItem));                  // NOT :L219
        Assert.Equal(0, fixture.Log.CountOf(SetItemStatusMember));             // NOT :L220
        Assert.Equal(editText, Assert.IsType<string>(fixture.BufferValue));    // full text, untruncated
    }

    // ==============================================================================================
    //  SECTION 8 - SELF AUDIT
    //  --------------------------------------------------------------------------------------------
    //  Properties of the SUITE rather than of the subject, asserted because the brief requires them and
    //  because a silent regression in a test suite is worse than one in production code: the suite
    //  keeps passing while it stops checking anything.
    // ==============================================================================================

    /// <summary>
    /// Every matrix in this file is non-empty and states one expectation per row, so no arm is covered
    /// by an empty theory that would pass vacuously.
    /// </summary>
    /// <remarks>
    /// A <c>MemberData</c> source that silently became empty is the classic way a parity matrix stops
    /// testing anything without failing, and it is invisible in a green run. Counting the rows here is
    /// what makes that visible. The counts are exact rather than lower bounds so that DELETING a row is
    /// as loud as adding one.
    /// </remarks>
    [Fact]
    public void EveryMatrixInThisFileIsPopulated()
    {
        Assert.Equal(10, ClassificationRows.Count);                  // Section 1
        Assert.Equal(4, WireAgreementRows.Count);                    // Section 1 - one per member
        Assert.Equal(8, GateMaskRows.Count);                         // Section 2 - eight masks
        Assert.Equal(4, UnresolvableColumnRows.Count);               // Section 2 - one per arm
        Assert.Equal(6, StashRows.Count);                            // Section 2
        Assert.Equal(8, EqualityRows.Count);                         // Section 3
        Assert.Equal(6, PostHandlerComparisonRows.Count);            // Section 3
        Assert.Equal(4, NestedRaisePrecedesEveryArmRows.Count);      // Section 4 - one per arm
        Assert.Equal(11, ReReadRenderingRows.Count);                 // Section 4 - one per buffer type
        Assert.Equal(7, ForcedTwoRows.Count);                        // Section 5
        Assert.Equal(5, ChangedRaiseRows.Count);                     // Section 5
        Assert.Equal(10, OuterDispatchMatrix.Count);                 // Section 5 - each arm, both ways
        Assert.Equal(23, CoercionMatrix.Count);                      // Section 6
        Assert.Equal(2, DateFamilyDisambiguation.Count);             // Section 6 - the critical pair
        Assert.Equal(25, TokenOwnershipRows.Count);                  // Section 6
        Assert.Equal(6, UncoercibleRows.Count);                      // Section 6
        Assert.Equal(12, TruncationRows.Count);                      // Section 6
        Assert.Equal(5, OverLengthRows.Count);                       // Section 7
        Assert.Equal(6, SemanticDelegationRows.Count);               // Section 7
    }

    /// <summary>
    /// All six arms of <c>se_cst_dw.sru:L231-L244</c> are exercised by the coercion matrix, and so is
    /// the no-arm outcome of <c>:L244</c> - the coverage claim asserted rather than assumed
    /// (constraint C-H).
    /// </summary>
    [Fact]
    public void TheCoercionMatrixExercisesAllSixArmsAndTheNoArmOutcome()
    {
        HashSet<string> reachedSignatures = new(StringComparer.Ordinal);
        int noArmRows = 0;

        foreach (CoercionExpectation expectation in CoercionExpectations)
        {
            if (string.Equals(expectation.Signature, NoSetItemExpected, StringComparison.Ordinal))
            {
                noArmRows++;
            }
            else
            {
                _ = reachedSignatures.Add(expectation.Signature);
            }
        }

        // Six arms, six DISTINCT overloads - one per arm, and no arm sharing another's overload.
        string[] expectedOverloads =
        [
            "SetItem(string?)",     // :L233 - char, char(
            "SetItem(decimal?)",    // :L235 - decim, real, numbe
            "SetItem(long?)",       // :L237 - long, ulong
            "SetItem(DateTime?)",   // :L239 - datet
            "SetItem(DateOnly?)",   // :L241 - date
            "SetItem(TimeOnly?)",   // :L243 - time
        ];

        Assert.Equal(expectedOverloads.Length, reachedSignatures.Count);
        foreach (string overload in expectedOverloads)
        {
            Assert.Contains(overload, reachedSignatures);
        }

        // And the missing `case else` of :L244 is covered too, several times over.
        Assert.True(noArmRows >= 6, "The no-arm outcome of se_cst_dw.sru:L244 must be exercised.");
    }
}
