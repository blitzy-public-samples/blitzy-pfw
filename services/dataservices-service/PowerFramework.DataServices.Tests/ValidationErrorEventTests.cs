// ==================================================================================================
//  ValidationErrorEventTests - THE `ondwnitemvalidationerror` PARITY MATRIX
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST  services/dataservices-service/PowerFramework.DataServices/Domain/ValidationSession.cs
//                     ValidationSession.OnDwnItemValidationError, and the two result records it
//                     produces - ValidationErrorOutcome and ValidationStructuredError.
//
//  BEHAVIOURAL ORACLE  ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L322-L385, read line
//                      by line. Supporting oracles:
//                        ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru:L16-L17  the two
//                          categories, declared as OFFSETS from Enums.I18N_CAT_CUSTOM
//                        ws_objects/pfw.ui.pbl.src/i18n.srf:L17-L18  the three-overload facade and
//                          its SILENT PASSTHROUGH - `if IsValid(n_cst_i18n) then ...` then
//                          `return text`, so with no provider installed the text comes back
//                          unchanged, nothing is thrown, nothing is logged and nothing is marked
//                          untranslated
//                        pfw.i18n.xml  the resource table the concrete providers read; its English
//                          rows for this path are 错误 -> "Error" [:L40, :L56] and 输入了无效的值 ->
//                          "Invalid input value" [:L57], and its Traditional Chinese rows are
//                          错误 -> "錯誤" [:L115, :L131] and 输入了无效的值 -> "輸入了無效的值" [:L132]
//                        ws_objects/pfw.shared.pbl.src/retcode.sru:L39-L45  the return-code algebra,
//                          referenced here only to prove this path does NOT use it
//                        ws_objects/pfw.common.pbl.src/sprintf.srf  the substitution primitive, whose
//                          20 arity overloads become one `params` method
//
//  THE LEGACY TREE IS READ ONLY AND IS NEVER AN EDIT TARGET (constraint C-C). It is the oracle these
//  tests characterize. Every assertion below states what the legacy ACTUALLY does, including where
//  that is odd, defensive or outright inconsistent - a test that asserted the corrected behaviour
//  would be asserting a regression (constraints C-B and G2).
//
//  WHY THIS FILE EXISTS ALONGSIDE ValidationSessionParityTests.cs
//  ------------------------------------------------------------------------------------------------
//  The sibling file characterizes the same routine as individually named facts, one branch each. This
//  file is the TABLE-DRIVEN FORM AAP 0.6.7 mandates: "table-driven parity matrices expressed as
//  theories with member data". Every test below is a `[Theory]` fed by a `[MemberData]` provider, and
//  every row carries the `se_cst_dw.sru` line it characterizes in a trailing comment. The two shapes
//  answer different questions and neither subsumes the other: a fact pins one branch and reads
//  clearly in a failure report, whereas a matrix pins the COMBINATIONS - the arms that only misbehave
//  together, such as a stashed 3 arriving at a row that was deleted, or a strip that itself produces
//  the DataWindow's "no message" placeholder and so re-enters the fallback.
//
//  THE DIALOG IS A STRUCTURED ERROR, AND ONLY THE DELIVERY CHANNEL CHANGES
//  ------------------------------------------------------------------------------------------------
//  AAP 0.2.1.3 Correction 5 turns `MessageBox(...)` [se_cst_dw.sru:L357] into a machine-readable
//  result carrying the exact message text, the localization category, the Sprintf substitution
//  arguments and the severity. These tests therefore assert THE PAYLOAD AND NEVER A DIALOG: there is
//  no window, no positioning, no DPI conversion, no font measurement and no rendering anywhere in the
//  subject or in this file, and ValidationErrorEventTests.StructuredErrorSurfaceIsClosed proves it by
//  pinning the result's entire property-type closure (constraint C-D).
//
//  DISCIPLINES THIS FILE HOLDS TO
//  ------------------------------------------------------------------------------------------------
//    * PLAIN XUNIT ASSERTIONS AND HAND-WRITTEN DOUBLES ONLY. No mocking framework, no fluent
//      assertion library, no snapshot library. The three seams - the host, the localization facade
//      and the localization provider - are all hand-authored doubles already in this assembly.
//    * MESSAGE TEXT IS COMPARED BYTE FOR BYTE, including the Chinese source keys and the appended
//      "!". Nothing is trimmed, case-folded, culture-normalised or whitespace-collapsed: `Assert.Equal`
//      over two strings is ordinal, which is exactly what a characterization comparison needs.
//    * SCREAMING_SNAKE CONSTANTS ARE REFERENCED, NEVER DECLARED. `Categories.CAT_DWSVC` is read from
//      the localization library, where it is the computed offset `Enums.I18N_CAT_CUSTOM + 2`
//      [ne_cst_i18n.sru:L17]. The number it evaluates to appears nowhere in this file, because
//      hardcoding it would silently survive a change to the base constant.
//    * EVERY LOOKUP KEY AND EVERY LEGACY LITERAL IS REACHED THROUGH THE PRODUCT CONSTANT, via
//      `ValidationStructuredError` and `LegacyMessageKeys`. There is one source of truth per string,
//      so a test's copy cannot drift from the product's.
//    * NO CLOCK, NO RANDOMNESS, NO WAITING AND NO AMBIENT STATE. Nothing here reads the wall clock, so
//      no determinism mask is required (AAP 0.6.7) and no `TimeProvider` double is needed.
//
//  RULES POSITION
//  ------------------------------------------------------------------------------------------------
//  `review_rules` returns "No user rules provided." No user-specified rule governs this file, and
//  none is invented. The enterprise baseline of AAP 0.7.2 applies in their place: nullable reference
//  types on, warnings as errors, no secret in source, and a test project held to exactly the bar of
//  the application it tests.
// ==================================================================================================
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;

using PowerFramework.DataServices.Domain;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

using Xunit;

// THE TWO WIRE TYPES ARE ALIASED RATHER THAN IMPORTED WHOLESALE, and the reason is a genuine name
// clash rather than style. `PowerFramework.Contracts.Common.V1` publishes a `RetCode` message that
// would collide with `PowerFramework.Shared.Kernel.RetCode`, and
// `PowerFramework.Contracts.DataServices.V1` publishes its own `ItemChangeResult` and `EventGate` that
// would collide with the Domain types of the same name. Importing either namespace plain makes those
// references CS0104; naming exactly the three types needed keeps every use unambiguous.
using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
using ItemStatus = PowerFramework.Contracts.Common.V1.ItemStatus;
using WireSeverity = PowerFramework.Contracts.DataServices.V1.Severity;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The four localization arrangements this matrix drives the two localized strings of
/// <c>se_cst_dw.sru:L355</c> and <c>:L357</c> through.
/// </summary>
/// <remarks>
/// <para>
/// DECLARED AT NAMESPACE SCOPE BECAUSE A <c>[MemberData]</c> PROVIDER MUST BE PUBLIC AND STATIC, and a
/// public signature over a less accessible type is CS0053. It follows the same shape as the
/// namespace-scoped enum in <c>TestDoubles.cs</c>.
/// </para>
/// <para>
/// THE FIRST THREE ARRANGEMENTS ARE ALL PASSTHROUGH AND ARE STILL DISTINCT INPUTS.
/// <see cref="NoProvider"/> is the state <c>i18n.srf:L17</c>'s <c>IsValid</c> guard describes - no
/// provider installed at all - which is a real production state, because it is what the framework is in
/// before <c>pfw.sra</c>'s open event installs a locale
/// [<c>ws_objects/pfw.pbl.src/pfw.sra:L95-L103</c>]. <see cref="InstalledButUntaught"/> and
/// <see cref="PassthroughProvider"/> both have a provider that is asked and declines. THE ORACLE CANNOT
/// TELL ANY OF THE THREE APART - all three leave <c>text</c> exactly as found - and that
/// indistinguishability is itself the behaviour under test.
/// </para>
/// </remarks>
public enum ValidationErrorLocalization
{
    /// <summary>
    /// No provider installed. <c>i18n.srf:L17</c> skips the raise entirely and returns the argument, so
    /// both strings stay as the untranslated Chinese source.
    /// </summary>
    NoProvider = 0,

    /// <summary>
    /// A provider is installed but has been taught nothing, so it records the lookup and answers "not
    /// mine" without assigning the <c>ref</c> parameter. Behaviourally identical to
    /// <see cref="NoProvider"/>, and additionally REPORTS which keys it was asked for.
    /// </summary>
    InstalledButUntaught = 1,

    /// <summary>
    /// The dedicated passthrough double, which declines WITHOUT EVEN A SELF-ASSIGNMENT to the
    /// <c>ref</c> parameter. Present because a zero answer must leave the argument exactly as found, and
    /// a double that reassigned it would pass while hiding a port that relied on the reassignment.
    /// </summary>
    PassthroughProvider = 2,

    /// <summary>
    /// A provider taught the ENGLISH rows of <c>pfw.i18n.xml</c> - 错误 -&gt; <c>"Error"</c> and
    /// 输入了无效的值 -&gt; <c>"Invalid input value"</c>.
    /// </summary>
    EnglishResourceTable = 3,

    /// <summary>
    /// A provider taught the TRADITIONAL CHINESE rows of <c>pfw.i18n.xml</c> - 错误 -&gt; <c>"錯誤"</c>
    /// and 输入了无效的值 -&gt; <c>"輸入了無效的值"</c>.
    /// </summary>
    TraditionalChineseResourceTable = 4,
}

/// <summary>
/// The table-driven parity matrix for <c>event ondwnitemvalidationerror</c>
/// [<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L322-L385</c>].
/// </summary>
public sealed class ValidationErrorEventTests
{
    // ==============================================================================================
    //  THE FIXTURE
    //  --------------------------------------------------------------------------------------------
    //  One column on one row, which is everything :L322-L385 addresses: the routine reads and writes
    //  exactly one cell, and every branch it has turns on that cell, on the edit text, on the stash or
    //  on the row count. A wider fixture would add arrangement without adding a branch.
    // ==============================================================================================

    /// <summary>The single column the whole matrix addresses.</summary>
    private const string Column = "name";

    /// <summary>
    /// The identifier <c>FakeDataWindowHost.AddColumn</c> assigns the first DATA column, and therefore
    /// the value <c>Long(dwo.ID)</c> resolves to on every path here [<c>se_cst_dw.sru:L335</c>,
    /// <c>:L375</c>, <c>:L376</c>].
    /// </summary>
    private const long ColumnIdOfFirstColumn = 1L;

    /// <summary>The one-based row the event fires on. DataWindow rows are one-based (AAP 0.4.5.4).</summary>
    private const long Row = 1L;

    /// <summary>
    /// Non-empty edit text, which is what makes <c>:L348</c>'s test true and sends execution down the
    /// message arm rather than the empty-data arm.
    /// </summary>
    private const string OffendingText = "bad";

    /// <summary>The correlation identifier. Opaque, and never a credential.</summary>
    private const string SessionId = "validation-error-matrix";

    /// <summary>The DataWindow handle a session is scoped to.</summary>
    private const string DataWindowHandle = "dw-validation-error";

    /// <summary>A quoted validation message, the shape a real DataWindow reports.</summary>
    private const string QuotedMessage = "'Bad value'";

    /// <summary>That message once <c>:L352</c> has dropped the opening and closing quote.</summary>
    private const string StrippedMessage = "Bad value";

    /// <summary>The value the fixture's one cell starts at.</summary>
    private const string OriginalCellValue = "original";

    /// <summary>
    /// <c>pfw.i18n.xml</c>'s English rendering of the dialog title <c>"错误"</c>
    /// [<c>pfw.i18n.xml:L40</c>, <c>:L56</c>].
    /// </summary>
    private const string EnglishTitle = "Error";

    /// <summary>
    /// <c>pfw.i18n.xml</c>'s English rendering of the fallback body <c>"输入了无效的值"</c>
    /// [<c>pfw.i18n.xml:L57</c>]. The <c>"!"</c> is NOT part of it - see <c>:L355</c>.
    /// </summary>
    private const string EnglishFallbackBody = "Invalid input value";

    /// <summary>
    /// <c>pfw.i18n.xml</c>'s Traditional Chinese rendering of <c>"错误"</c>
    /// [<c>pfw.i18n.xml:L115</c>, <c>:L131</c>].
    /// </summary>
    private const string TraditionalChineseTitle = "錯誤";

    /// <summary>
    /// <c>pfw.i18n.xml</c>'s Traditional Chinese rendering of <c>"输入了无效的值"</c>
    /// [<c>pfw.i18n.xml:L132</c>].
    /// </summary>
    private const string TraditionalChineseFallbackBody = "輸入了無效的值";

    /// <summary>
    /// Builds the one-column, one-row host.
    /// </summary>
    /// <param name="cellValue">
    /// The cell's starting value. <see langword="null"/> models a null cell, which is a DISTINGUISHABLE
    /// state that <c>:L372</c>'s both-null arm depends on and is never collapsed to zero or to the empty
    /// string (AAP 0.4.5.4).
    /// </param>
    /// <param name="validationMsg">
    /// What <c>Describe(dwo.Name + ".ValidationMsg")</c> answers, VERBATIM and quotes included, because
    /// the strip at <c>:L352</c> is what unquotes it and the strip is observable output.
    /// </param>
    /// <param name="status">The cell's starting item status, snapshotted at <c>:L335</c>.</param>
    /// <returns>The host, already recording reads.</returns>
    /// <remarks>
    /// READ RECORDING IS ON, because three of this routine's assertions are about reads that must NOT
    /// happen - the re-entrant path takes no snapshot [<c>:L327</c>], the empty-data path reads no
    /// validation message [<c>:L359-L363</c>], and a non-zero handler result skips the message read
    /// entirely [<c>:L347</c>]. A double that only recorded writes could not express any of them.
    /// </remarks>
    private static FakeDataWindowHost NewHost(
        string? cellValue = OriginalCellValue,
        string validationMsg = QuotedMessage,
        ItemStatus status = ItemStatus.NotModified)
    {
        FakeDataWindowHost host = new();
        host.AddColumn(Column, FakeColumnType.CharOf(50));
        host.AddRow(cellValue);
        host.SetValidationMessage(Column, validationMsg);
        host.SetBufferItemStatus(DwBuffer.Primary, Row, ColumnIdOfFirstColumn, status);
        host.RecordsReads = true;
        return host;
    }

    /// <summary>
    /// Builds a session with the four legacy fields at the values a freshly constructed control holds.
    /// </summary>
    /// <param name="i18n">
    /// The localization facade. When omitted the subject installs one with NO PROVIDER, which is the
    /// silent-passthrough arrangement rather than a stub.
    /// </param>
    /// <param name="stash">
    /// The value to seed <c>_nItemChangeRetCode</c> [<c>:L95-L96</c>] with, standing in for a preceding
    /// item-change event. Seeded through the public setter the item-change protocol itself writes
    /// through, so nothing here reaches past the subject's own surface.
    /// </param>
    /// <returns>The session.</returns>
    /// <remarks>
    /// THE LIFETIME AND THE CLOCK ARE BOTH LEFT AT THEIR DEFAULTS, DELIBERATELY. The subject declares
    /// both parameters optional precisely so a session can be built without a configuration root, and it
    /// substitutes its own options instance when none is supplied - so passing one would add a dependency
    /// on the configuration type while changing nothing observable. Nothing on the validation-error path
    /// reads the clock or consults the idle timeout, so no determinism seam is required here either
    /// (AAP 0.6.7): the whole routine is a pure function of the host, the stash and the edit text.
    /// </remarks>
    private static ValidationSession NewSession(I18n? i18n = null, long stash = 0L)
    {
        ValidationSession session = new(
            SessionId,
            DataWindowHandle,
            initialDisabledEventMask: 0u,
            lifetime: null,
            i18n,
            timeProvider: null);

        session.ItemChangeRetCode = stash;
        return session;
    }

    /// <summary>
    /// Builds the localization facade for one arrangement, together with the provider behind it when
    /// there is one.
    /// </summary>
    /// <param name="mode">The arrangement.</param>
    /// <returns>
    /// The facade, and the provider so a test can inspect the LOOKUP KEYS it was actually asked for -
    /// which is how the "translate then append" ordering of <c>:L355</c> is proved rather than assumed.
    /// </returns>
    /// <remarks>
    /// The two resource tables are taught from <c>pfw.i18n.xml</c>'s own rows rather than from invented
    /// strings, so a row of this matrix is grounded in the shipped resource file and not in a guess about
    /// what a translation might look like.
    /// </remarks>
    private static (I18n Facade, ScriptedI18nProvider? Provider) LocalizationFor(
        ValidationErrorLocalization mode)
    {
        switch (mode)
        {
            case ValidationErrorLocalization.NoProvider:
                return (ScriptedLocalization.WithoutProvider(), null);

            case ValidationErrorLocalization.InstalledButUntaught:
            {
                // Installed and asked, with an empty table, so every lookup misses and `text` is left
                // exactly as found - while the request is still recorded.
                ScriptedI18nProvider provider = new();
                return (ScriptedLocalization.With(provider), provider);
            }

            case ValidationErrorLocalization.PassthroughProvider:
                // Declines without so much as a self-assignment to the `ref` parameter.
                return (ScriptedLocalization.With(new PassthroughI18nProvider()), null);

            case ValidationErrorLocalization.EnglishResourceTable:
            {
                ScriptedI18nProvider provider = new ScriptedI18nProvider()
                    .Teach(LegacyMessageKeys.ValidationErrorTitle, EnglishTitle)
                    .Teach(LegacyMessageKeys.ValidationErrorFallbackText, EnglishFallbackBody);

                return (ScriptedLocalization.With(provider), provider);
            }

            case ValidationErrorLocalization.TraditionalChineseResourceTable:
            {
                ScriptedI18nProvider provider = new ScriptedI18nProvider()
                    .Teach(LegacyMessageKeys.ValidationErrorTitle, TraditionalChineseTitle)
                    .Teach(
                        LegacyMessageKeys.ValidationErrorFallbackText,
                        TraditionalChineseFallbackBody);

                return (ScriptedLocalization.With(provider), provider);
            }

            default:
                // Total by construction: the enum has exactly five members and all five are handled
                // above. The arm exists so the switch is exhaustive to the compiler rather than relying
                // on flow analysis of an enum, whose underlying type admits values no member names.
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unhandled arrangement.");
        }
    }

    /// <summary>
    /// The dialog title <c>:L357</c> produces under one arrangement.
    /// </summary>
    /// <param name="mode">The arrangement.</param>
    /// <returns>The expected title.</returns>
    /// <remarks>
    /// The two passthrough arrangements yield the UNTRANSLATED CHINESE SOURCE, byte for byte. That is
    /// not a degenerate outcome to be tolerated - it is what <c>i18n.srf:L18</c>'s <c>return text</c>
    /// does, and a port that substituted a marker, an empty string or an exception there would be
    /// changing observable output.
    /// </remarks>
    private static string ExpectedTitle(ValidationErrorLocalization mode) => mode switch
    {
        ValidationErrorLocalization.EnglishResourceTable => EnglishTitle,
        ValidationErrorLocalization.TraditionalChineseResourceTable => TraditionalChineseTitle,
        _ => LegacyMessageKeys.ValidationErrorTitle,
    };

    /// <summary>
    /// The fallback body <c>:L355</c> produces under one arrangement, INCLUDING the appended
    /// <c>"!"</c>.
    /// </summary>
    /// <param name="mode">The arrangement.</param>
    /// <returns>The expected body.</returns>
    /// <remarks>
    /// Composed through <see cref="LegacyMessageKeys.ComposeValidationErrorFallback"/> so the ordering
    /// rule lives in one place: the text is TRANSLATED and the <c>"!"</c> is appended AFTERWARDS. The
    /// suffix is therefore never part of the lookup key, which
    /// <see cref="FallbackSuffixIsAppendedAfterTranslationAndIsNotPartOfTheKey"/> proves by inspecting
    /// the keys the provider was actually handed.
    /// </remarks>
    private static string ExpectedFallbackBody(ValidationErrorLocalization mode) =>
        LegacyMessageKeys.ComposeValidationErrorFallback(
            mode switch
            {
                ValidationErrorLocalization.EnglishResourceTable => EnglishFallbackBody,
                ValidationErrorLocalization.TraditionalChineseResourceTable =>
                    TraditionalChineseFallbackBody,
                _ => LegacyMessageKeys.ValidationErrorFallbackText,
            });

    /// <summary>
    /// Counts the reads of <c>Describe(dwo.Name + ".ValidationMsg")</c> [<c>se_cst_dw.sru:L350</c>].
    /// </summary>
    /// <param name="host">The host whose call log to scan.</param>
    /// <returns>How many times that exact property was described.</returns>
    /// <remarks>
    /// Counted by PROPERTY rather than by member, because a suite that merely asserted "no Describe
    /// happened" would break the moment any unrelated property was read, and would pass vacuously if the
    /// subject described a different property instead. Three separate arms depend on this count being
    /// zero: the re-entrant guard, the empty-data early return and a non-zero handler result.
    /// </remarks>
    private static int CountValidationMessageReads(FakeDataWindowHost host)
    {
        int count = 0;

        foreach (DataWindowCallRecord record in host.CallLog.Records)
        {
            if (string.Equals(record.Member, "Describe", StringComparison.Ordinal)
                && record.Arguments.Count == 1
                && record.Arguments[0] is string property
                && string.Equals(property, Column + ".ValidationMsg", StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Installs an <c>ItemError</c> handler [<c>se_cst_dw.sru:L343</c>] and records what it was handed.
    /// </summary>
    /// <param name="host">The host to script.</param>
    /// <param name="result">
    /// What the handler answers. <see langword="null"/> is the input that drives <c>:L344</c>'s
    /// coercion, and it is a real answer rather than an absence.
    /// </param>
    /// <param name="observed">
    /// Receives the arguments the handler saw, so a test can assert the row, the column object and the
    /// edit text were forwarded unchanged.
    /// </param>
    private static void ScriptItemError(
        FakeDataWindowHost host,
        long? result,
        List<(long Row, string ColumnName, string Data)> observed)
    {
        host.ItemErrorHandler = (row, dwo, data) =>
        {
            observed.Add((row, dwo.Name, data));
            return result;
        };
    }

    // ==============================================================================================
    //  PHASE 1 - THE RE-ENTRANCY GUARD AND THE STASH                       se_cst_dw.sru:L327-L335
    // ==============================================================================================

    /// <summary>
    /// The outer arrangements a re-entrant invocation is driven under.
    /// </summary>
    /// <remarks>
    /// EVERY ROW STASHES SOMETHING OUTSIDE <c>{1, 3}</c>, and that is forced rather than chosen: the
    /// pre-set at <c>:L338-L340</c> moves the result off zero for 1 and 3, which makes <c>:L342</c>
    /// false, which means <c>ItemError</c> is never raised - and the handler is the only place from
    /// which the oracle can re-enter this routine at all. A row stashing 1 or 3 could not reach the
    /// re-entrant branch, so it would silently test nothing.
    /// </remarks>
    public static TheoryData<long, string, string> ReEntrancyRows =>
        new()
        {
            // :L327 under the ordinary message arm.
            { 0L, OffendingText, QuotedMessage },
            // :L327 with a stash of 2, which is in the alphabet but is neither 1 nor 3.
            { 2L, OffendingText, "" },
            // :L327 with a stash outside the alphabet entirely, which :L338 compares NUMERICALLY.
            { -1L, OffendingText, LegacyMessageKeys.NoValidationMessagePlaceholder },
            // :L327 with EMPTY data. The raise at :L342 precedes the emptiness test at :L347, so the
            // handler still runs and the re-entrant branch is still reachable on this path.
            { 0L, "", QuotedMessage },
        };

    [Theory]
    [MemberData(nameof(ReEntrancyRows))]
    public void ReEntrantInvocationReturnsOneImmediatelyAndDoesNothingElse(
        long outerStash,
        string data,
        string validationMsg)
    {
        FakeDataWindowHost host = NewHost(validationMsg: validationMsg, status: ItemStatus.DataModified);
        ValidationSession session = NewSession(stash: outerStash);

        ValidationErrorOutcome? inner = null;
        bool flagWasSetWhenReEntered = false;

        // The ONLY door the oracle re-enters through. `InItemValidationError` has no setter, so the
        // re-entrant branch is reachable exactly the way the legacy reaches it - from inside a handler
        // raised by an invocation already in flight - and never by poking the flag.
        host.ItemErrorHandler = (row, dwo, itemData) =>
        {
            flagWasSetWhenReEntered = session.InItemValidationError;
            inner = session.OnDwnItemValidationError(host, row, dwo, itemData);
            return 0L;
        };

        ValidationErrorOutcome outer =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), data);

        // :L329 had already run when the handler was raised.
        Assert.True(flagWasSetWhenReEntered);
        Assert.NotNull(inner);

        // :L327 - the number, and the fact that it came from the guard rather than from a real result.
        Assert.True(inner.ReEntered);
        Assert.Equal((long)ItemChangeResult.TriggerValidationError, inner.RawResult);
        Assert.Equal(ItemChangeResult.TriggerValidationError, inner.Result);

        // NOTHING ELSE RAN. Each of these is a separate statement the oracle's `return 1` precedes.
        Assert.False(inner.PreSetFromStash);
        Assert.False(inner.ItemErrorRaised);
        Assert.False(inner.ItemErrorReturnedNull);
        Assert.False(inner.EmptyData);
        Assert.Equal(string.Empty, inner.ValidationMessage);
        Assert.False(inner.ValidationMessageFellBack);
        Assert.Null(inner.Error);
        Assert.False(inner.ValueRestored);
        Assert.Null(inner.RowStillExists);

        // :L331-L332 were never reached, so the stash was neither read into the outcome nor cleared BY
        // THE INNER CALL. The outer call had already consumed it, which is why the reported value is 0
        // rather than `outerStash`.
        Assert.Equal(0L, inner.StashedRawItemChangeRetCode);
        Assert.Equal(ItemChangeResult.Default, inner.StashedItemChangeResult);

        // :L334-L335 were never reached either, so both snapshot members hold their defaults.
        Assert.Null(inner.OriginalValue);
        Assert.Equal(ItemStatus.NotModified, inner.OriginalStatus);
        Assert.Null(inner.ColumnId);

        // :L327 DOES NOT CLEAR THE FLAG - the outer invocation still owns it and clears it at :L382.
        Assert.True(inner.State.InItemValidationError);

        // The proof that no snapshot was taken: exactly ONE status read happened across both
        // invocations, and it was the outer one's.
        Assert.Equal(1, host.CallLog.CountOf("GetItemStatus"));

        // The outer invocation ran to completion and cleared the flag at :L382.
        Assert.False(outer.ReEntered);
        Assert.False(outer.State.InItemValidationError);
        Assert.False(session.InItemValidationError);
        Assert.Equal(outerStash, outer.StashedRawItemChangeRetCode);
    }

    /// <summary>
    /// Every non-re-entrant exit from the routine, with the number it returns.
    /// </summary>
    /// <remarks>
    /// THERE ARE TWO SEPARATE FLAG-CLEARING SITES AND THEY MUST STAY SEPARATE. <c>:L361</c> clears the
    /// flag and returns 3 WITHOUT running the tail; <c>:L382</c> clears it after the tail has run.
    /// Unifying them would run the tail for a result of 3 that the oracle bypasses, so every row below
    /// asserts the flag is clear afterwards AND that a second invocation still works - which is the
    /// difference between the flag being reported clear and being clear.
    /// </remarks>
    public static TheoryData<long, bool, long?, string, long, bool> FlagClearedOnEveryExitRows =>
        new()
        {
            // :L344 null coerced to 0, then the message arm at :L358 sets 1, then :L382 clears.
            { 0L, false, null, OffendingText, 1L, false },
            // :L343 answers 0 explicitly - same arm, different input.
            { 0L, true, 0L, OffendingText, 1L, false },
            // :L343 answers 2, which skips the message arm and falls through :L366 with no case match.
            { 0L, true, 2L, OffendingText, 2L, false },
            // :L343 answers 3, which reaches :L367's SECOND value and restores.
            { 0L, true, 3L, OffendingText, 3L, false },
            // :L343 answers a value outside the alphabet; :L366 has no `case else`, so nothing happens.
            { 0L, true, -1L, OffendingText, -1L, false },
            { 0L, true, 99L, OffendingText, 99L, false },
            // :L361-L362 THE EARLY RETURN. The flag is cleared HERE, not at :L382.
            { 0L, false, null, "", 3L, true },
            { 0L, true, 0L, "", 3L, true },
            // :L338-L340 pre-set from the stash, which skips :L343 entirely.
            { 1L, false, null, OffendingText, 1L, false },
            { 3L, false, null, OffendingText, 1L, false },
            // A stash of 1 with EMPTY data still pre-sets, so :L347 is false and the early return is
            // NOT taken - the emptiness of the data is only consulted when the result is still zero.
            { 1L, false, null, "", 1L, false },
        };

    [Theory]
    [MemberData(nameof(FlagClearedOnEveryExitRows))]
    public void GuardFlagIsClearedOnEveryExitPath(
        long stash,
        bool installHandler,
        long? handlerResult,
        string data,
        long expectedRawResult,
        bool expectEmptyDataArm)
    {
        FakeDataWindowHost host = NewHost();
        ValidationSession session = NewSession(stash: stash);
        List<(long Row, string ColumnName, string Data)> observed = [];

        if (installHandler)
        {
            ScriptItemError(host, handlerResult, observed);
        }

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), data);

        Assert.Equal(expectedRawResult, outcome.RawResult);
        Assert.Equal(ItemChangeProtocol.Classify(expectedRawResult), outcome.Result);
        Assert.Equal(expectEmptyDataArm, outcome.EmptyData);

        // :L361 and :L382 - the flag is clear on BOTH sites, observed through the session and through
        // the snapshot the outcome carries.
        Assert.False(session.InItemValidationError);
        Assert.False(outcome.State.InItemValidationError);

        // The flag is genuinely clear rather than merely reported clear: a second invocation gets past
        // :L327 and produces a real result instead of the guard's 1.
        ValidationErrorOutcome again =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), data);

        Assert.False(again.ReEntered);
        Assert.False(session.InItemValidationError);
    }

    /// <summary>
    /// The stash values <c>:L331-L332</c> consumes, and whether each drives the pre-set.
    /// </summary>
    /// <remarks>
    /// The values outside <c>{1, 2, 3}</c> matter as much as the ones inside it. The oracle assigns the
    /// item-change handler's return value to the field UNCLASSIFIED at <c>:L195</c>, before any
    /// <c>choose case</c> has looked at it, so a handler answering <c>-1</c> or <c>42</c> stashes
    /// <c>-1</c> or <c>42</c> - and <c>:L338</c> and <c>:L369</c> both compare NUMERICALLY. Narrowing
    /// the stash to the alphabet would discard information the legacy keeps.
    /// </remarks>
    public static TheoryData<long, bool> StashConsumptionRows =>
        new()
        {
            { 1L, true },    // :L338 first disjunct
            { 3L, true },    // :L338 second disjunct
            { 0L, false },   // the value a fresh control holds, and the value :L332 leaves behind
            { 2L, false },   // in the alphabet, but neither 1 nor 3
            { -1L, false },  // outside the alphabet
            { -2L, false },  // numerically CANCELLED in the return-code algebra, which does not apply here
            { 42L, false },  // outside the alphabet, positive
            { 4L, false },   // one past the alphabet's highest member
        };

    [Theory]
    [MemberData(nameof(StashConsumptionRows))]
    public void StashIsConsumedThenClearedAndIsThereforeSingleUse(long stash, bool expectPreSet)
    {
        FakeDataWindowHost host = NewHost();
        ValidationSession session = NewSession(stash: stash);

        // The stash is visible on the session before the event fires.
        Assert.Equal(stash, session.ItemChangeRetCode);
        Assert.Equal(ItemChangeProtocol.Classify(stash), session.StashedItemChangeResult);

        ValidationErrorOutcome first =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), OffendingText);

        // :L331 - CONSUMED. The outcome reports the value as it stood BEFORE the clear, both raw and
        // classified, because after :L332 nothing can ask for it again.
        Assert.Equal(stash, first.StashedRawItemChangeRetCode);
        Assert.Equal(ItemChangeProtocol.Classify(stash), first.StashedItemChangeResult);

        // :L338-L340 - the pre-set is a function of the CONSUMED value.
        Assert.Equal(expectPreSet, first.PreSetFromStash);
        Assert.Equal(!expectPreSet, first.ItemErrorRaised);

        // :L332 - CLEARED, on the field and on the snapshot taken as the invocation returned.
        Assert.Equal(0L, session.ItemChangeRetCode);
        Assert.Equal(0L, first.State.RawItemChangeRetCode);
        Assert.Equal(ItemChangeResult.Default, first.State.ItemChangeRetCode);

        // SINGLE USE. A second validation-error event with no intervening item change reads ZERO, not
        // the original value, and therefore takes the ItemError path even where the first pre-set. This
        // consume-and-clear is precisely why AAP 0.6.1.4 assigns the item-change chain strictly
        // synchronous ordering: this handler's behaviour is a function of the PREVIOUS event's return
        // value, so a reordered delivery would read a stash that belongs to a different edit.
        ValidationErrorOutcome second =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), OffendingText);

        Assert.Equal(0L, second.StashedRawItemChangeRetCode);
        Assert.Equal(ItemChangeResult.Default, second.StashedItemChangeResult);
        Assert.False(second.PreSetFromStash);
        Assert.True(second.ItemErrorRaised);
    }

    /// <summary>
    /// The cell states <c>:L334-L335</c> snapshots.
    /// </summary>
    /// <remarks>
    /// The value and the status are taken at THE SAME INSTANT because <c>:L375-L376</c> write them back
    /// TOGETHER, and a value restored without its status would leave the row claiming a modification
    /// state it no longer has. The null-cell row is not an edge case: <c>:L372</c>'s second arm exists
    /// solely to make both-null count as equal, so null has to survive the snapshot as null.
    /// </remarks>
    public static TheoryData<string?, ItemStatus> SnapshotRows =>
        new()
        {
            { OriginalCellValue, ItemStatus.NotModified },
            { OriginalCellValue, ItemStatus.DataModified },
            { OriginalCellValue, ItemStatus.New },
            { OriginalCellValue, ItemStatus.NewModified },
            { null, ItemStatus.DataModified },       // a null cell, preserved as null
            { "", ItemStatus.NotModified },          // the EMPTY STRING, which is not null
            { "  ", ItemStatus.DataModified },       // whitespace, neither trimmed nor normalised
        };

    [Theory]
    [MemberData(nameof(SnapshotRows))]
    public void ValueAndStatusAreSnapshottedBeforeAnythingRuns(string? cellValue, ItemStatus status)
    {
        FakeDataWindowHost host = NewHost(cellValue, status: status);
        ValidationSession session = NewSession();

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), OffendingText);

        // :L334 - `aOrgValue = dwo.Primary[row]`, with null and the empty string kept distinct.
        Assert.Equal(cellValue, outcome.OriginalValue);

        // :L335 - `orgStatus = GetItemStatus(row,Long(dwo.ID),Primary!)`.
        Assert.Equal(status, outcome.OriginalStatus);

        // `Long(dwo.ID)` resolved once. Never defaulted to zero, because column zero is a real column
        // position and a default would address the wrong cell silently (AAP 0.4.5.4).
        Assert.Equal(ColumnIdOfFirstColumn, outcome.ColumnId);

        // The status read went to the row, the column and the buffer the oracle names - and to exactly
        // one of each.
        int index = host.CallLog.IndexOf("GetItemStatus");
        Assert.True(index >= 0);

        DataWindowCallRecord record = host.CallLog.RecordAt(index);
        Assert.Equal(3, record.Arguments.Count);
        Assert.Equal(Row, record.Arguments[0]);
        Assert.Equal(ColumnIdOfFirstColumn, record.Arguments[1]);
        Assert.Equal(DwBuffer.Primary, record.Arguments[2]);
        Assert.Equal(1, host.CallLog.CountOf("GetItemStatus"));
    }

    /// <summary>
    /// The identifier shapes <c>Long(dwo.ID)</c> is handed at <c>:L335</c>, <c>:L375</c> and
    /// <c>:L376</c>, and what each resolves to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TWO "UNRESOLVABLE-LOOKING" INPUTS ARE BEHAVIOURALLY DIFFERENT, AND THAT DIFFERENCE IS THE
    /// POINT OF THIS MATRIX. PowerScript's <c>Long(null)</c> is null, so a null identifier PROPAGATES
    /// and every addressing call receives that null and writes nothing. PowerScript's
    /// <c>Long("not a number")</c> is <c>0</c>, so unparseable text RESOLVES - to column zero, which is
    /// a real column position - and the addressing calls therefore go ahead against it. Collapsing the
    /// two into one "unresolvable" case would erase a preserved legacy behaviour (constraint C-B), and
    /// treating unparseable text as an error would add a throw the legacy does not have.
    /// </para>
    /// <para>
    /// The integral row exists because <c>IDataWindowObject.ID</c> is <c>object?</c> and is deliberately
    /// not pre-converted, so the conversion's widening arms are reachable from a real event rather than
    /// only from a unit test of the converter.
    /// </para>
    /// </remarks>
    public static TheoryData<string, long?, bool, ItemStatus> ColumnIdentifierRows =>
        new()
        {
            // The ordinary case: the identifier the fixture assigned, and the status it seeded.
            { "as-declared", ColumnIdOfFirstColumn, true, ItemStatus.DataModified },
            // NULL PROPAGATES. No status read, and no write at :L375-L376.
            { "null", null, false, ItemStatus.NotModified },
            // UNPARSEABLE TEXT YIELDS ZERO, so the addressing calls DO happen - against column zero,
            // where nothing was seeded, so the status read answers NotModified.
            { "unparseable-text", 0L, true, ItemStatus.NotModified },
            // Text that DOES parse is honoured, and lands back on the fixture's own column.
            { "numeric-text", ColumnIdOfFirstColumn, true, ItemStatus.DataModified },
            // A narrower integral type widens rather than falling to the default arm.
            { "int32", ColumnIdOfFirstColumn, true, ItemStatus.DataModified },
        };

    [Theory]
    [MemberData(nameof(ColumnIdentifierRows))]
    public void ColumnIdentifierResolutionDecidesWhetherTheCellIsAddressedAtAll(
        string identifierShape,
        long? expectedColumnId,
        bool expectStatusRead,
        ItemStatus expectedStatus)
    {
        FakeDataWindowHost host = NewHost(status: ItemStatus.DataModified);
        FakeDataWindowObject dwo = host.DwObject(Column);

        dwo.ID = identifierShape switch
        {
            "null" => null,
            "unparseable-text" => "not a number",
            "numeric-text" => ColumnIdOfFirstColumn.ToString(CultureInfo.InvariantCulture),
            "int32" => (int)ColumnIdOfFirstColumn,
            _ => dwo.ID,
        };

        ValidationSession session = NewSession();

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, Row, dwo, OffendingText);

        // `Long(dwo.ID)` resolved once, and reported so a consumer can tell an absent identifier from
        // column zero.
        Assert.Equal(expectedColumnId, outcome.ColumnId);

        // :L335 - the read happens only when there is something to address.
        Assert.Equal(expectStatusRead ? 1 : 0, host.CallLog.CountOf("GetItemStatus"));
        Assert.Equal(expectedStatus, outcome.OriginalStatus);

        // :L375-L376 - the writes are gated on the same resolution, so an absent identifier leaves the
        // buffer provably untouched.
        Assert.Equal(expectedColumnId.HasValue, outcome.ValueRestored);
        Assert.Equal(expectedColumnId.HasValue, host.CallLog.Contains("SetItem(object?)"));
        Assert.Equal(expectedColumnId.HasValue, host.CallLog.Contains("SetItemStatus"));

        // :L369's row test ran on every row here, because it does not consult the identifier.
        Assert.True(outcome.RowStillExists);

        // :L350-L358 ran either way: the message arm reads the column by NAME, not by identifier.
        Assert.Equal(StrippedMessage, outcome.ValidationMessage);
        Assert.Equal((long)ItemChangeResult.TriggerValidationError, outcome.RawResult);
    }

    // ==============================================================================================
    //  PHASE 2 - THE PRE-SET FROM THE STASH                                se_cst_dw.sru:L338-L342
    //  --------------------------------------------------------------------------------------------
    //  :L338  if nItemChangeRetCode = 1 or nItemChangeRetCode = 3 then
    //  :L339      rtCode = 1
    //  :L342  if rtCode = 0 then          <- the raise is GATED on the pre-set not having fired
    //
    //  The pre-set does not merely choose a return value; it SUPPRESSES the semantic ItemError event
    //  and, because :L347 tests the same zero, the message arm as well. One stashed digit therefore
    //  silences two whole blocks.
    // ==============================================================================================

    /// <summary>
    /// Every stashed value, with the observable consequences of the pre-set for each.
    /// </summary>
    /// <remarks>
    /// A STASHED 3 REACHES <c>:L338</c> EVEN THOUGH THE ITEM-CHANGE DISPATCH REWRITES ITS OWN 3 INTO A
    /// 1. The stash is written at <c>:L195</c>, BEFORE the <c>choose case</c> at <c>:L211</c> rewrites
    /// the result at <c>:L225</c>, so the pre-rewrite value is what this routine sees - and that is
    /// exactly what lets <c>:L369</c> still distinguish "was a 3" from "was a 1" later on.
    /// </remarks>
    public static TheoryData<long, bool, long> PreSetRows =>
        new()
        {
            // :L338 first disjunct - pre-set to 1, ItemError suppressed, message arm suppressed.
            { 1L, true, 1L },
            // :L338 second disjunct - the same, from the value the item-change case-3 arm stashed.
            { 3L, true, 1L },
            // Not pre-set: the result stays 0, so :L342 raises and :L347 takes the message arm, which
            // sets 1 at :L358. The RESULT MATCHES the pre-set rows while the PATH does not, which is
            // why every row below asserts the path and not only the number.
            { 0L, false, 1L },
            { 2L, false, 1L },
            { -1L, false, 1L },
            { -2L, false, 1L },
            { 4L, false, 1L },
            { 42L, false, 1L },
        };

    [Theory]
    [MemberData(nameof(PreSetRows))]
    public void PreSetFromStashSuppressesBothTheItemErrorRaiseAndTheMessageArm(
        long stash,
        bool expectPreSet,
        long expectedRawResult)
    {
        FakeDataWindowHost host = NewHost();
        ValidationSession session = NewSession(stash: stash);
        List<(long Row, string ColumnName, string Data)> observed = [];

        // Scripted to answer 0 so that, if it were ever raised on a pre-set row, execution would fall
        // into the message arm and the assertions below would catch it. A handler that answered non-zero
        // could mask the raise by producing the same shape the pre-set produces.
        ScriptItemError(host, 0L, observed);

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), OffendingText);

        Assert.Equal(expectPreSet, outcome.PreSetFromStash);
        Assert.Equal(expectedRawResult, outcome.RawResult);
        Assert.Equal(ItemChangeResult.TriggerValidationError, outcome.Result);

        if (expectPreSet)
        {
            // :L342 IS FALSE, so the semantic event is NOT RAISED AT ALL - asserted at the handler
            // itself, not merely on the reported flag, because the flag and the invocation are two
            // different claims.
            Assert.Empty(observed);
            Assert.False(outcome.ItemErrorRaised);
            Assert.False(outcome.ItemErrorReturnedNull);

            // :L347 tests the same zero, so the message arm is skipped too: no property read, no
            // message, no fallback and no structured error.
            Assert.Equal(0, CountValidationMessageReads(host));
            Assert.Equal(string.Empty, outcome.ValidationMessage);
            Assert.False(outcome.ValidationMessageFellBack);
            Assert.Null(outcome.Error);
        }
        else
        {
            // :L343 raised exactly once, and was handed the row, the column and the edit text unchanged.
            (long row, string columnName, string data) = Assert.Single(observed);
            Assert.Equal(Row, row);
            Assert.Equal(Column, columnName);
            Assert.Equal(OffendingText, data);

            Assert.True(outcome.ItemErrorRaised);

            // :L347-L358 ran, because the handler answered 0.
            Assert.Equal(1, CountValidationMessageReads(host));
            Assert.Equal(StrippedMessage, outcome.ValidationMessage);
            Assert.NotNull(outcome.Error);
        }
    }

    // ==============================================================================================
    //  PHASE 3 - THE ItemError RAISE AND THE NULL COERCION                 se_cst_dw.sru:L342-L345
    //  --------------------------------------------------------------------------------------------
    //  :L343  rtCode = Event ItemError(row,dwo,data)
    //  :L344  if IsNull(rtCode) then rtCode = 0
    //
    //  THE COERCION IS THE ORACLE'S, NOT A DEFENSIVE HABIT. AAP 0.4.5.4 forbids collapsing null to zero
    //  IMPLICITLY, and this is the one site in the routine where the legacy does it EXPLICITLY. It is
    //  also the arm most easily lost in translation: a C# port that read the nullable return as "no
    //  result" and skipped ahead would silently drop the message arm for every handler that answers
    //  null - which is every handler that does not override the event at all.
    // ==============================================================================================

    /// <summary>
    /// What the semantic handler answers, and where each answer sends execution.
    /// </summary>
    /// <remarks>
    /// THE FIRST TWO ROWS ARE DIFFERENT INPUTS WITH THE SAME OUTCOME, DELIBERATELY. "No handler
    /// installed" reaches the base implementation, which answers null; "handler installed, answers
    /// null" reaches a handler that chooses to. <c>:L344</c> makes them indistinguishable downstream,
    /// and <see cref="ValidationErrorOutcome.ItemErrorReturnedNull"/> keeps the distinction visible
    /// WITHOUT changing the outcome - which is the whole difference between reporting a coercion and
    /// performing one.
    /// </remarks>
    public static TheoryData<bool, long?, long, bool, bool> ItemErrorAnswerRows =>
        new()
        {
            // :L343 reaches the base event, which answers null -> :L344 coerces -> message arm -> 1.
            { false, null, 1L, true, true },
            // :L343 reaches a handler that answers null -> the same three steps.
            { true, null, 1L, true, true },
            // :L343 answers ZERO explicitly. Same arm as the coerced rows, no coercion reported.
            { true, 0L, 1L, true, false },
            // Every non-zero answer SHORT-CIRCUITS :L347 and flows straight to the dispatch at :L366.
            { true, 1L, 1L, false, false },
            { true, 2L, 2L, false, false },
            { true, 3L, 3L, false, false },
            { true, -1L, -1L, false, false },
            { true, -2L, -2L, false, false },
            { true, 99L, 99L, false, false },
        };

    [Theory]
    [MemberData(nameof(ItemErrorAnswerRows))]
    public void ItemErrorAnswerDecidesTheMessageArmAndNullIsCoercedToZero(
        bool installHandler,
        long? handlerResult,
        long expectedRawResult,
        bool expectMessageArm,
        bool expectNullCoercion)
    {
        FakeDataWindowHost host = NewHost();
        ValidationSession session = NewSession();
        List<(long Row, string ColumnName, string Data)> observed = [];

        if (installHandler)
        {
            ScriptItemError(host, handlerResult, observed);
        }

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), OffendingText);

        // :L343 - raised on every row here, because no row pre-sets.
        Assert.True(outcome.ItemErrorRaised);
        Assert.False(outcome.PreSetFromStash);

        if (installHandler)
        {
            // The three arguments are forwarded verbatim: `(row, dwo, data)`.
            (long row, string columnName, string data) = Assert.Single(observed);
            Assert.Equal(Row, row);
            Assert.Equal(Column, columnName);
            Assert.Equal(OffendingText, data);
        }

        // :L344 - reported, never inferred.
        Assert.Equal(expectNullCoercion, outcome.ItemErrorReturnedNull);

        Assert.Equal(expectedRawResult, outcome.RawResult);
        Assert.Equal(ItemChangeProtocol.Classify(expectedRawResult), outcome.Result);

        if (expectMessageArm)
        {
            // A null answer really does reach :L350: the property is read, the message is produced and
            // the structured error is raised - which is the behaviour a "nullable means skip" port
            // silently loses.
            Assert.Equal(1, CountValidationMessageReads(host));
            Assert.Equal(StrippedMessage, outcome.ValidationMessage);
            Assert.NotNull(outcome.Error);
            Assert.Equal(StrippedMessage, outcome.Error.Text);
        }
        else
        {
            // :L347 is false, so :L350 is never reached and NOTHING about the message is produced.
            Assert.Equal(0, CountValidationMessageReads(host));
            Assert.Equal(string.Empty, outcome.ValidationMessage);
            Assert.False(outcome.ValidationMessageFellBack);
            Assert.Null(outcome.Error);
        }

        // The empty-data arm is not this row's business either way.
        Assert.False(outcome.EmptyData);
    }

    // ==============================================================================================
    //  PHASE 4 - THE MESSAGE ARM                                           se_cst_dw.sru:L347-L358
    //  --------------------------------------------------------------------------------------------
    //  :L347  if rtCode = 0 then
    //  :L348      if data <> "" /*or String(aOrgValue) <> ""*/ then
    //  :L350          sErrMsg = Describe(dwo.Name+".ValidationMsg")
    //  :L351          if Len(sErrMsg) > 2 then
    //  :L352              sErrMsg = Mid(sErrMsg,2,Len(sErrMsg) - 2)
    //  :L354          if sErrMsg = "" or sErrMsg = "?" then
    //  :L355              sErrMsg = I18N(ne_cst_i18n.CAT_DWSVC,"输入了无效的值") + "!"
    //  :L357          MessageBox(I18N(ne_cst_i18n.CAT_DWSVC,"错误"),sErrMsg,StopSign!)
    //  :L358          rtCode = 1
    //
    //  THE COMMENTED-OUT DISJUNCT AT :L348 IS CARRIED ACROSS INERT AND IS NOT REVIVED (constraint C-B).
    //  Reviving `or String(aOrgValue) <> ""` would take the message arm for an EMPTY entry over a
    //  non-empty original value, which the oracle does not do - and which would make the empty-data
    //  early return unreachable for every populated cell.
    // ==============================================================================================

    /// <summary>
    /// The four legacy literals this path carries, each paired with its transcription from the oracle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE ONE PLACE IN THE FILE WHERE THE CHINESE LITERALS ARE WRITTEN OUT, and it exists so
    /// that every other assertion can reference the PRODUCT CONSTANT without the comparison becoming
    /// tautological. The left operand of each row is transcribed from <c>se_cst_dw.sru</c>; the right
    /// operand is read from the shipping constant. They come from different places, so agreement is a
    /// real claim - and a stray character in either is caught here rather than being asserted against
    /// itself everywhere else.
    /// </para>
    /// <para>
    /// The suffix is a SEPARATE constant from the body because <c>:L355</c> appends it OUTSIDE the
    /// localization call. Fusing the two would make the lookup key <c>"输入了无效的值!"</c>, which no
    /// row of <c>pfw.i18n.xml</c> carries, so every translation would silently miss and the passthrough
    /// would look like correct behaviour.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string> LegacyLiteralRows =>
        new()
        {
            {
                "se_cst_dw.sru:L355 - the fallback body, translated then suffixed",
                "输入了无效的值",
                ValidationStructuredError.LegacyFallbackTextSource
            },
            {
                "se_cst_dw.sru:L355 - the suffix, appended OUTSIDE the lookup",
                "!",
                ValidationStructuredError.LegacyFallbackSuffix
            },
            {
                "se_cst_dw.sru:L357 - the dialog title, localized separately from the body",
                "错误",
                ValidationStructuredError.LegacyTitleSource
            },
            {
                "se_cst_dw.sru:L354 - the DataWindow's own \"no message\" placeholder",
                "?",
                ValidationStructuredError.NoValidationMessagePlaceholder
            },
        };

    [Theory]
    [MemberData(nameof(LegacyLiteralRows))]
    public void LegacyLiteralsMatchTheOracleByteForByte(
        string locator,
        string transcribedFromOracle,
        string shippingConstant)
    {
        Assert.False(string.IsNullOrEmpty(locator));

        // Ordinal, and with no trimming, case folding, normalisation or culture involved anywhere.
        Assert.Equal(transcribedFromOracle, shippingConstant);
        Assert.Equal(transcribedFromOracle.Length, shippingConstant.Length);

        // `LegacyMessageKeys` aliases the same constants rather than re-transcribing them, so the test
        // support layer cannot drift from the product either.
        Assert.Equal(
            ValidationStructuredError.LegacyFallbackTextSource,
            LegacyMessageKeys.ValidationErrorFallbackText);
        Assert.Equal(
            ValidationStructuredError.LegacyFallbackSuffix,
            LegacyMessageKeys.ValidationErrorFallbackSuffix);
        Assert.Equal(
            ValidationStructuredError.LegacyTitleSource,
            LegacyMessageKeys.ValidationErrorTitle);
        Assert.Equal(
            ValidationStructuredError.NoValidationMessagePlaceholder,
            LegacyMessageKeys.NoValidationMessagePlaceholder);

        // The line the structured error stands in for, reported rather than inferred so a
        // characterization recording can be traced back to the statement that produced it.
        Assert.Equal(357, ValidationStructuredError.LegacyLine);
    }

    /// <summary>
    /// The outer-two-character strip of <c>:L351-L353</c>, driven across both sides of its
    /// <c>Len(sErrMsg) &gt; 2</c> boundary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE GUARD IS <c>&gt; 2</c> AND THE BOUNDARY IS THE WHOLE POINT. The DataWindow stores a
    /// validation message as a QUOTED LITERAL, so <c>Mid(sErrMsg,2,Len(sErrMsg) - 2)</c> drops the
    /// opening quote and stops before the closing one. At length 3 exactly one character survives; at
    /// length 2, 1 and 0 the message is left UNTOUCHED - so a bare pair of quotes survives AS a bare
    /// pair of quotes and is then caught by neither arm of the fallback test. That is the oracle's
    /// behaviour, not an oversight to correct.
    /// </para>
    /// <para>
    /// TWO ROWS CROSS THE STRIP AND THE FALLBACK TOGETHER, which is the interaction only a matrix
    /// finds: <c>"'?'"</c> is three characters, so it IS stripped, and what the strip PRODUCES is the
    /// DataWindow's own placeholder - which then satisfies <c>:L354</c> and falls back. A test that
    /// exercised the strip and the fallback separately would never reach that composition.
    /// </para>
    /// <para>
    /// THE STRIP CAN NEVER PRODUCE THE EMPTY STRING, and the table shows why: it only runs at length 3
    /// or more, where it always leaves at least one character. So the empty-string arm of <c>:L354</c>
    /// is reachable ONLY from a column that describes nothing at all.
    /// </para>
    /// </remarks>
    public static TheoryData<string, int, string, bool> ValidationMessageStripRows =>
        new()
        {
            // Length 0 - the strip is skipped, and the EMPTY arm of :L354 fires.
            { "", 0, "", true },
            // Length 1 - untouched, and it is exactly the placeholder, so the "?" arm of :L354 fires.
            { LegacyMessageKeys.NoValidationMessagePlaceholder, 1, "?", true },
            // Length 1 - untouched, and used as-is.
            { "x", 1, "x", false },
            // Length 2 - UNTOUCHED. A bare pair of quotes reaches the user verbatim.
            { "''", 2, "''", false },
            { "ab", 2, "ab", false },
            // Length 2 beginning with the placeholder character - NOT equal to "?", so no fallback.
            { "?x", 2, "?x", false },
            // Length 3 - the boundary's far side. Exactly one character survives.
            { "'a'", 3, "a", false },
            // Length 3 whose STRIPPED RESULT is the placeholder - strip then fallback, in that order.
            { "'?'", 3, "?", true },
            // Length 3 whose stripped result is a single SPACE, which is neither empty nor "?" - so no
            // fallback, and the space is not trimmed away.
            { "' '", 3, " ", false },
            // Length 4 - both quotes dropped from a doubled pair.
            { "''''", 4, "''", false },
            // Length 10 - stripped to the inner 8, the canonical case.
            { "'12345678'", 10, "12345678", false },
            // A realistic quoted message.
            { QuotedMessage, 11, StrippedMessage, false },
            // Double-quoted, and non-ASCII: the strip counts CHARACTERS, and nothing is re-encoded.
            { "\"输入无效\"", 6, "输入无效", false },
            // Leading and trailing whitespace INSIDE the quotes survives the strip untouched.
            { "'  padded  '", 12, "  padded  ", false },
        };

    [Theory]
    [MemberData(nameof(ValidationMessageStripRows))]
    public void ValidationMessageIsStrippedOnlyAboveTheTwoCharacterBoundary(
        string described,
        int describedLength,
        string strippedOrUntouched,
        bool expectFallback)
    {
        // Keeps the table honest: the length column is what makes the boundary legible, so it is
        // asserted rather than trusted.
        Assert.Equal(describedLength, described.Length);

        FakeDataWindowHost host = NewHost(validationMsg: described);
        ValidationSession session = NewSession();

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), OffendingText);

        // :L350 - the property was read exactly once, and read RAW.
        Assert.Equal(1, CountValidationMessageReads(host));

        // :L351-L353 - the strip, verified independently against the one-based helper the port routes
        // through, so an off-by-one in the conversion is caught at the conversion rather than only at
        // the message.
        string expectedAfterStrip = described.Length > 2
            ? ValidationSession.MidOneBased(described, 2, described.Length - 2)
            : described;

        Assert.Equal(strippedOrUntouched, expectedAfterStrip);

        // :L354-L356 - the fallback fires on the empty string and on the placeholder, and on nothing
        // else.
        Assert.Equal(expectFallback, outcome.ValidationMessageFellBack);

        string expectedMessage = expectFallback
            ? ExpectedFallbackBody(ValidationErrorLocalization.NoProvider)
            : strippedOrUntouched;

        // Byte for byte, on both the outcome and the structured error that carries it.
        Assert.Equal(expectedMessage, outcome.ValidationMessage);
        Assert.NotNull(outcome.Error);
        Assert.Equal(expectedMessage, outcome.Error.Text);

        // :L358
        Assert.Equal((long)ItemChangeResult.TriggerValidationError, outcome.RawResult);
    }

    /// <summary>
    /// Every localization arrangement crossed with the three message shapes that decide whether the
    /// fallback fires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FIFTEEN ROWS, AND THE CROSS IS WHAT MATTERS. The arrangement decides what the two localized
    /// strings become; the message shape decides whether the FALLBACK string is looked up at all. Only
    /// the cross shows that the column's own message is never localized under ANY arrangement - a
    /// property no single row can establish.
    /// </para>
    /// <para>
    /// THE TWO STRINGS ARE LOCALIZED SEPARATELY ON ONE LINE. <c>:L357</c> passes
    /// <c>I18N(CAT_DWSVC,"错误")</c> as the title and <c>sErrMsg</c> as the body, so title and body are
    /// two fields rather than one; collapsing them would lose a translated string.
    /// </para>
    /// </remarks>
    public static TheoryData<ValidationErrorLocalization, string, bool> LocalizationRows
    {
        get
        {
            TheoryData<ValidationErrorLocalization, string, bool> rows = [];

            ValidationErrorLocalization[] arrangements =
            [
                ValidationErrorLocalization.NoProvider,
                ValidationErrorLocalization.InstalledButUntaught,
                ValidationErrorLocalization.PassthroughProvider,
                ValidationErrorLocalization.EnglishResourceTable,
                ValidationErrorLocalization.TraditionalChineseResourceTable,
            ];

            foreach (ValidationErrorLocalization arrangement in arrangements)
            {
                // :L354 empty arm - the fallback body IS looked up.
                rows.Add(arrangement, "", true);

                // :L354 placeholder arm - likewise.
                rows.Add(arrangement, LegacyMessageKeys.NoValidationMessagePlaceholder, true);

                // The column supplies its own message, so the fallback is NOT looked up and the message
                // is NOT translated - only the title is.
                rows.Add(arrangement, QuotedMessage, false);
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(LocalizationRows))]
    public void StructuredErrorCarriesTheLocalizedPayloadThePathProduces(
        ValidationErrorLocalization mode,
        string described,
        bool expectFallback)
    {
        (I18n facade, ScriptedI18nProvider? provider) = LocalizationFor(mode);
        FakeDataWindowHost host = NewHost(validationMsg: described);
        ValidationSession session = NewSession(facade);

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), OffendingText);

        ValidationStructuredError error = Assert.IsType<ValidationStructuredError>(outcome.Error);

        // :L357 title - translated under this arrangement, or the untranslated Chinese source under any
        // of the three passthrough arrangements.
        Assert.Equal(ExpectedTitle(mode), error.Title);

        // :L355 or the column's own message, byte for byte. Note the second branch: the column's message
        // is IDENTICAL under every arrangement, which is the claim the cross exists to make.
        string expectedBody = expectFallback ? ExpectedFallbackBody(mode) : StrippedMessage;

        Assert.Equal(expectedBody, error.Text);
        Assert.Equal(expectedBody, outcome.ValidationMessage);
        Assert.Equal(expectFallback, outcome.ValidationMessageFellBack);

        // The category is READ FROM THE LOCALIZATION LIBRARY, where it is the computed offset
        // `Enums.I18N_CAT_CUSTOM + 2` [ne_cst_i18n.sru:L17]. The number it evaluates to appears nowhere
        // in this file: hardcoding it would survive a change to the base constant unnoticed.
        Assert.Equal(Categories.CAT_DWSVC, error.LocalizationCategory);

        // :L357 severity - `StopSign!`, and it agrees with the wire enum member for member so the gRPC
        // projection is a cast rather than a lookup.
        Assert.Equal(DialogSeverity.StopSign, error.Severity);
        Assert.Equal((int)WireSeverity.StopSign, (int)error.Severity);

        // NO SUBSTITUTION HAPPENS ON THIS PATH. Neither :L355 nor :L357 has a placeholder, so the
        // argument list is empty and the body is the template verbatim.
        Assert.Empty(error.FormatArguments);

        // :L357 is a DIALOG, not an operation, so there is no framework return code to report - and the
        // code the event ultimately returns belongs to the item-change alphabet, NOT to the return-code
        // algebra. The two coincide numerically at 1, which is exactly why they must not be conflated:
        // read through the algebra, this result would be `RetCode.PREVENT`.
        Assert.Null(error.ReturnCode);
        Assert.Equal(RetCode.PREVENT, outcome.RawResult);
        Assert.Equal(ItemChangeResult.TriggerValidationError, outcome.Result);

        // Reports that the text came through the localization layer. TRUE EVEN UNDER PASSTHROUGH,
        // because the layer WAS consulted; the flag records a legacy inconsistency - these two messages
        // are localized while all 28 in the column-expression engine are not - rather than resolving it.
        Assert.True(error.Localized);

        if (provider is not null)
        {
            // THE LOOKUP KEYS THE PROVIDER WAS ACTUALLY HANDED. The fallback key appears only when the
            // fallback fires, and the column's own message NEVER appears - which is the direct evidence
            // that :L355's lookup is inside the fallback block and the column's message is not localized
            // at all.
            ImmutableArray<string?> keys = provider.RequestedKeys;

            Assert.Contains(LegacyMessageKeys.ValidationErrorTitle, keys);
            Assert.DoesNotContain(StrippedMessage, keys);
            Assert.DoesNotContain(described, keys);

            if (expectFallback)
            {
                Assert.Contains(LegacyMessageKeys.ValidationErrorFallbackText, keys);
                Assert.Equal(2, keys.Length);
            }
            else
            {
                Assert.DoesNotContain(LegacyMessageKeys.ValidationErrorFallbackText, keys);
                Assert.Single(keys);
            }

            // Every lookup went out under the category and source the oracle passes, and the suffix was
            // never part of a key - which proves the ordering at :L355 is TRANSLATE THEN APPEND.
            foreach (I18nRequest request in provider.Requests)
            {
                Assert.Equal(provider.Category, request.Category);
                Assert.Equal(provider.Source, request.Source);
                Assert.NotNull(request.Text);
                Assert.DoesNotContain(ValidationStructuredError.LegacyFallbackSuffix, request.Text);
            }
        }
    }

    /// <summary>
    /// The three passthrough arrangements, crossed with the two shapes that reach the fallback.
    /// </summary>
    /// <remarks>
    /// SEPARATED FROM <see cref="LocalizationRows"/> BECAUSE IT ASSERTS A DIFFERENT KIND OF CLAIM. That
    /// matrix asserts what each arrangement PRODUCES; this one asserts that the three passthrough
    /// arrangements produce THE SAME THING AS EACH OTHER and as the untranslated source - the silent
    /// passthrough of <c>i18n.srf:L17-L18</c>, where the text comes back unchanged, nothing is thrown,
    /// nothing is logged and nothing is marked untranslated.
    /// </remarks>
    public static TheoryData<ValidationErrorLocalization, string> SilentPassthroughRows =>
        new()
        {
            { ValidationErrorLocalization.NoProvider, "" },
            { ValidationErrorLocalization.NoProvider, "?" },
            { ValidationErrorLocalization.InstalledButUntaught, "" },
            { ValidationErrorLocalization.InstalledButUntaught, "?" },
            { ValidationErrorLocalization.PassthroughProvider, "" },
            { ValidationErrorLocalization.PassthroughProvider, "?" },
        };

    [Theory]
    [MemberData(nameof(SilentPassthroughRows))]
    public void SilentPassthroughReturnsTheUntranslatedSourceUnchanged(
        ValidationErrorLocalization mode,
        string described)
    {
        (I18n facade, _) = LocalizationFor(mode);
        FakeDataWindowHost host = NewHost(validationMsg: described);
        ValidationSession session = NewSession(facade);

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), OffendingText);

        ValidationStructuredError error = Assert.IsType<ValidationStructuredError>(outcome.Error);

        // The untranslated Chinese source, byte for byte, composed from the product constants so there
        // is one source of truth - and pinned against the oracle's own characters by
        // LegacyLiteralsMatchTheOracleByteForByte.
        Assert.Equal(LegacyMessageKeys.ValidationErrorTitle, error.Title);
        Assert.Equal(
            LegacyMessageKeys.ValidationErrorFallbackText
                + ValidationStructuredError.LegacyFallbackSuffix,
            error.Text);

        // NOT THROWN, NOT EMPTY, AND NOT MARKED. The three failure modes a port might substitute for a
        // missing translation are each excluded explicitly.
        Assert.NotEmpty(error.Text);
        Assert.NotEmpty(error.Title);
        Assert.True(error.Localized);
        Assert.True(outcome.ValidationMessageFellBack);
        Assert.EndsWith(
            ValidationStructuredError.LegacyFallbackSuffix,
            error.Text,
            StringComparison.Ordinal);

        // The suffix is appended exactly once, outside the lookup.
        Assert.Equal(
            LegacyMessageKeys.ValidationErrorFallbackText.Length
                + ValidationStructuredError.LegacyFallbackSuffix.Length,
            error.Text.Length);
    }

    /// <summary>
    /// The one localization arrangement whose keys prove the ordering of <c>:L355</c>.
    /// </summary>
    /// <remarks>
    /// Two rows, one per translating arrangement. What is asserted is not the translation but the KEY:
    /// the provider must be handed <c>"输入了无效的值"</c> WITHOUT the <c>"!"</c>, because <c>:L355</c>
    /// concatenates the suffix onto the RESULT of the lookup. Were the suffix inside the key, no row of
    /// <c>pfw.i18n.xml</c> would match it, every translation would silently miss, and the passthrough
    /// would masquerade as correct behaviour under every locale.
    /// </remarks>
    public static TheoryData<ValidationErrorLocalization, string, string> TranslateThenAppendRows =>
        new()
        {
            {
                ValidationErrorLocalization.EnglishResourceTable,
                EnglishTitle,
                EnglishFallbackBody
            },
            {
                ValidationErrorLocalization.TraditionalChineseResourceTable,
                TraditionalChineseTitle,
                TraditionalChineseFallbackBody
            },
        };

    [Theory]
    [MemberData(nameof(TranslateThenAppendRows))]
    public void FallbackSuffixIsAppendedAfterTranslationAndIsNotPartOfTheKey(
        ValidationErrorLocalization mode,
        string translatedTitle,
        string translatedFallbackBody)
    {
        (I18n facade, ScriptedI18nProvider? provider) = LocalizationFor(mode);
        Assert.NotNull(provider);

        FakeDataWindowHost host = NewHost(validationMsg: string.Empty);
        ValidationSession session = NewSession(facade);

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), OffendingText);

        ValidationStructuredError error = Assert.IsType<ValidationStructuredError>(outcome.Error);

        // The lookup keys, exactly: the untranslated Chinese, suffix-free, in the oracle's order -
        // the fallback body at :L355 before the title at :L357.
        Assert.Equal(
            [LegacyMessageKeys.ValidationErrorFallbackText, LegacyMessageKeys.ValidationErrorTitle],
            provider.RequestedKeys);

        // TRANSLATE, THEN APPEND. The translated body carries the suffix; the key did not.
        Assert.Equal(translatedFallbackBody + ValidationStructuredError.LegacyFallbackSuffix, error.Text);
        Assert.Equal(translatedTitle, error.Title);

        // And the suffix is genuinely outside: strip it and the translation is left exact.
        Assert.Equal(
            translatedFallbackBody,
            error.Text[..^ValidationStructuredError.LegacyFallbackSuffix.Length]);
    }

    /// <summary>
    /// The substitution rule <see cref="ValidationStructuredError.Create"/> applies, driven from both
    /// sides of its empty-argument short circuit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SHORT CIRCUIT IS A PARITY REQUIREMENT, NOT AN OPTIMISATION, and the last three rows are the
    /// proof. <c>Formatting.Sprintf</c>'s placeholder grammar is
    /// <c>{</c>index<c>,</c>alignment<c>:</c>mask<c>}</c> with ONE-BASED indices - <c>{1}</c> is the
    /// first argument, mirroring PowerScript's own <c>param1</c> - and an index with no argument behind
    /// it renders as EMPTY rather than being left alone. So running a brace-bearing message through
    /// Sprintf with nothing to substitute DELETES the placeholder. The oracle substitutes nothing at
    /// <c>:L355</c> or <c>:L357</c>, so the port must pass the text through untouched; each of those
    /// rows asserts both that it does, and that Sprintf would have changed it.
    /// </para>
    /// <para>
    /// BOTH BRANCHES ARE REACHABLE AND BOTH ARE COVERED. The validation-error path supplies no
    /// arguments, which is why every row of <see cref="LocalizationRows"/> asserts an empty argument
    /// list; a caller that supplies them gets the substituted rendering, which the first rows pin.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, bool, string, bool> SprintfArgumentRows =>
        new()
        {
            // Arguments present: substituted, one-based.
            { "{1} is invalid", "age", true, "age is invalid", true },
            // Alignment is applied to the rendered value; a negative width left-aligns.
            { "{1,-6}|", "age", true, "age   |", true },
            // Arguments present but the template has no placeholder - unchanged, and no throw.
            { "no placeholder here", "age", true, "no placeholder here", false },
            // NO arguments: verbatim, even though the template HAS a placeholder that Sprintf would
            // have deleted.
            { "{1} is invalid", "", false, "{1} is invalid", true },
            { "{1}", "", false, "{1}", true },
            // The two texts the live path actually produces, carried through the no-argument branch.
            { "输入了无效的值!", "", false, "输入了无效的值!", false },
            { "错误", "", false, "错误", false },
        };

    [Theory]
    [MemberData(nameof(SprintfArgumentRows))]
    public void SubstitutionIsAppliedOnlyWhenArgumentsArePresent(
        string template,
        string argument,
        bool anyArguments,
        string expectedText,
        bool templateHasPlaceholder)
    {
        ImmutableArray<string> arguments =
            anyArguments ? [argument] : ImmutableArray<string>.Empty;

        ValidationStructuredError error = ValidationStructuredError.Create(
            ValidationStructuredError.LegacyTitleSource,
            template,
            DialogSeverity.StopSign,
            Categories.CAT_DWSVC,
            localized: true,
            arguments);

        Assert.Equal(expectedText, error.Text);

        // The arguments travel BEFORE substitution alongside the substituted text, so a consumer can
        // re-render in another locale without parsing the rendered string back apart.
        Assert.Equal(arguments, error.FormatArguments);

        if (anyArguments)
        {
            // Routed through the PORTED formatter, so the placeholder grammar is the framework's own
            // rather than this test's guess at it.
            Assert.Equal(Formatting.Sprintf(template, argument), error.Text);
        }
        else
        {
            // Verbatim - not merely equal to the template by coincidence.
            Assert.Same(template, error.Text);

            if (templateHasPlaceholder)
            {
                // AND THE SHORT CIRCUIT IS LOAD BEARING: had Sprintf run, the text would have changed.
                Assert.NotEqual(template, Formatting.Sprintf(template));
            }
        }
    }

    // ==============================================================================================
    //  PHASE 4 (CONTINUED) - THE RESULT CARRIES NO PRESENTATION TYPE            constraint C-D
    //  --------------------------------------------------------------------------------------------
    //  AAP 0.2.1.3 Correction 5 replaces the dialog at :L357 with a structured error and changes ONLY
    //  THE DELIVERY CHANNEL. The corollary is auditable and is audited here: window positioning, DPI
    //  conversion, font measurement and rendering are DesignSystem's deferred half, reserved under
    //  /v1/design/**, so none of them may appear anywhere in the result graph. A property list can be
    //  read; a closure over property TYPES is what actually forecloses the possibility.
    // ==============================================================================================

    /// <summary>
    /// The type names permitted anywhere in the validation-error result graph.
    /// </summary>
    /// <remarks>
    /// A closed set of primitives, enums, one immutable string sequence, <c>object</c> for the untyped
    /// cell value that <c>any</c> maps to, and the two result records themselves. Anything a renderer
    /// would need - a window handle, a device context, a font, a rectangle, a colour - is absent, and
    /// cannot be added without this set being widened deliberately.
    /// </remarks>
    private static ImmutableHashSet<string> PermittedResultTypeNames { get; } =
    [
        "string",
        "bool",
        "bool?",
        "long",
        "long?",
        "object",
        "ImmutableArray<string>",
        "DialogSeverity",
        "ItemChangeResult",
        "ItemStatus",
        "ValidationStructuredError",
        "ValidationSessionSnapshot",
    ];

    /// <summary>
    /// The presentation vocabulary no result type name may contain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>"Dialog"</c> IS DELIBERATELY ABSENT FROM THIS LIST, and the reason is worth stating so nobody
    /// adds it. <c>DialogSeverity</c> names the ICON the oracle passes at <c>:L357</c> - <c>StopSign!</c>
    /// - which is OBSERVABLE OUTPUT and part of what the legacy showed. It is an integral enum with six
    /// members and no geometry, which
    /// <see cref="SeverityIsAnIconEnumAndCarriesNoGeometry"/> pins; it renders nothing and measures
    /// nothing. Excluding the icon would not remove a presentation dependency, it would DISCARD
    /// behaviour.
    /// </para>
    /// <para>
    /// MATCHED AGAINST TYPE NAMES ONLY, ORDINALLY, AND THAT IS A CORRECTNESS REQUIREMENT RATHER THAN A
    /// SHORTCUT. A presentation dependency is carried by a TYPE - a window handle, a device context, a
    /// font, a rectangle - so the type closure is where it can actually be foreclosed; a property called
    /// <c>Draw</c> whose type is <see langword="long"/> carries no presentation dependency at all.
    /// Scanning member names in addition would only produce false positives from compound words, which
    /// is not hypothetical: <c>StashedRawItemChangeRetCode</c> contains <c>"dRaw"</c>. Ordinal matching
    /// is precise here because .NET type names are PascalCase, so a presentation type is spelled
    /// <c>Rect</c> or <c>Font</c> and not <c>rect</c> or <c>font</c>.
    /// </para>
    /// </remarks>
    private static ImmutableArray<string> PresentationVocabulary { get; } =
    [
        "Window", "Control", "Font", "Menu", "Canvas", "Painter", "Bitmap", "Image", "Color",
        "Colour", "Dpi", "Pixel", "Rect", "Point", "Graphics", "Brush", "Cursor", "Screen",
        "Widget", "Visual", "Layout", "Render", "Draw", "Paint", "Tooltip", "Tray", "Win32",
    ];

    /// <summary>
    /// Renders a type the way C# spells it, so a row of the matrix reads as source rather than as
    /// reflection output.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The C# spelling.</returns>
    private static string FriendlyTypeName(Type type)
    {
        Type? underlying = Nullable.GetUnderlyingType(type);

        if (underlying is not null)
        {
            return FriendlyTypeName(underlying) + "?";
        }

        if (type.IsConstructedGenericType)
        {
            string generic = type.Name;
            int tick = generic.IndexOf('`', StringComparison.Ordinal);

            if (tick > 0)
            {
                generic = generic[..tick];
            }

            return generic
                + "<"
                + string.Join(", ", type.GenericTypeArguments.Select(FriendlyTypeName))
                + ">";
        }

        return type.FullName switch
        {
            "System.String" => "string",
            "System.Boolean" => "bool",
            "System.Int64" => "long",
            "System.Int32" => "int",
            "System.UInt32" => "uint",
            "System.Object" => "object",
            _ => type.Name,
        };
    }

    /// <summary>
    /// The properties one result type reports, excluding the record's compiler-generated equality hook.
    /// </summary>
    /// <param name="type">The type to enumerate.</param>
    /// <returns>The properties, in a stable order.</returns>
    /// <remarks>
    /// <see cref="BindingFlags.NonPublic"/> is required because every member of the result graph is
    /// <c>internal</c> - forced, not chosen, because the graph surfaces <c>internal</c> enums and a
    /// public signature over one is CS0053.
    /// </remarks>
    private static IEnumerable<PropertyInfo> ReportedProperties(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(property =>
                !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal);

    /// <summary>
    /// Every property of every type in the validation-error result graph, one row each.
    /// </summary>
    /// <remarks>
    /// DERIVED FROM THE SUBJECT RATHER THAN TRANSCRIBED, which is what makes this matrix a gate instead
    /// of a snapshot: a property added to any of the three types appears here automatically on the next
    /// run and must satisfy the closure, so a presentation type cannot enter the result graph without
    /// this test refusing it.
    /// </remarks>
    public static TheoryData<string, string, string> ResultGraphPropertyRows
    {
        get
        {
            TheoryData<string, string, string> rows = [];

            Type[] graph =
            [
                typeof(ValidationErrorOutcome),
                typeof(ValidationStructuredError),
                typeof(ValidationSessionSnapshot),
            ];

            foreach (Type type in graph)
            {
                foreach (PropertyInfo property in ReportedProperties(type))
                {
                    rows.Add(type.Name, property.Name, FriendlyTypeName(property.PropertyType));
                }
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(ResultGraphPropertyRows))]
    public void ResultGraphCarriesNoPresentationType(
        string declaringTypeName,
        string propertyName,
        string propertyTypeName)
    {
        Assert.False(string.IsNullOrEmpty(declaringTypeName));
        Assert.False(string.IsNullOrEmpty(propertyName));

        // The closure: every reported type is drawn from the permitted set. Written as an explicit
        // predicate rather than `Assert.Contains` so the failure names the offending type, which is the
        // one thing a reviewer needs when this gate trips.
        Assert.True(
            PermittedResultTypeNames.Contains(propertyTypeName),
            $"{declaringTypeName}.{propertyName} reports type '{propertyTypeName}', which is not in the "
                + "permitted result-type closure. If this is a legitimate new reporting field, widen "
                + "PermittedResultTypeNames deliberately; if it is a presentation type, it belongs to "
                + "the deferred DesignSystem half reserved under /v1/design/** (constraint C-D).");

        // And belt-and-braces against the vocabulary itself, so a permitted-set widening that admitted a
        // renderer would still be caught here. Type names only, ordinally - see the remarks on
        // PresentationVocabulary for why a member-name scan would be both weaker and noisier.
        foreach (string token in PresentationVocabulary)
        {
            Assert.DoesNotContain(token, propertyTypeName, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The six icons <c>DialogSeverity</c> names, and the one the live path produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SEVERITY TRAVELS AS AN <see langword="int"/> RATHER THAN AS THE ENUM, and that is forced
    /// rather than chosen. <c>DialogSeverity</c> is <c>internal</c> - it has to be, because the result
    /// graph surfaces <c>internal</c> types and a public signature over one is CS0053 - so an
    /// <c>internal</c> theory parameter on a <c>public</c> test method is CS0051. The row therefore
    /// carries the NUMBER and the MEMBER NAME, and the test casts. The same constraint is recorded as
    /// decision D-3 in the subject file, so this row shape is the constraint showing through rather
    /// than an inconvenience worked around.
    /// </para>
    /// <para>
    /// Both are pinned against the wire enum, because <c>dataservices.v1.Severity</c> is the projection
    /// target and the plan records that projection as a CAST rather than a lookup - which is only true
    /// while the two agree in value AND in name at every position.
    /// </para>
    /// </remarks>
    public static TheoryData<int, string, string> SeverityRows =>
        new()
        {
            { 0, "Unspecified", "the proto3 zero member; no path in the subject produces it" },
            { 1, "None", "None!" },
            { 2, "Information", "Information!" },
            { 3, "Question", "Question!" },
            { 4, "Exclamation", "Exclamation!" },
            { 5, "StopSign", "StopSign! - se_cst_dw.sru:L357, the only value this path produces" },
        };

    [Theory]
    [MemberData(nameof(SeverityRows))]
    public void SeverityIsAnIconEnumAndCarriesNoGeometry(
        int value,
        string expectedMemberName,
        string legacySpelling)
    {
        Assert.False(string.IsNullOrEmpty(legacySpelling));

        DialogSeverity severity = (DialogSeverity)value;

        Assert.Equal(expectedMemberName, Enum.GetName(severity));
        Assert.Equal(value, (int)severity);

        // THE AGREEMENT THAT MAKES THE WIRE PROJECTION A CAST: same name at the same value, on both
        // sides. Numeric agreement alone would let the two drift into naming different icons.
        Assert.Equal(expectedMemberName, Enum.GetName((WireSeverity)value));

        // An integral enum, and nothing else: six members, no property of its own. So it cannot carry a
        // coordinate, a measurement or a handle however it is extended by mistake.
        Assert.True(typeof(DialogSeverity).IsEnum);
        Assert.Equal(typeof(int), typeof(DialogSeverity).GetEnumUnderlyingType());
        Assert.Equal(6, Enum.GetValues<DialogSeverity>().Length);
        Assert.Equal(
            Enum.GetValues<DialogSeverity>().Length,
            Enum.GetValues<WireSeverity>().Length);
        Assert.Empty(
            typeof(DialogSeverity).GetProperties(BindingFlags.Instance | BindingFlags.Public));
    }

    // ==============================================================================================
    //  PHASE 5 - THE EMPTY-DATA EARLY RETURN                               se_cst_dw.sru:L359-L363
    //  --------------------------------------------------------------------------------------------
    //  :L359  else
    //  :L360      //拒绝录入空值，保存时再检测提示
    //             "refuse to record an empty value; check and report at save time instead"
    //  :L361      _bDwnItemValidationError = false
    //  :L362      return 3
    //
    //  THE SUBTLE ARM. It clears the guard FIRST and returns SECOND, so the tail at :L366-L380 never
    //  runs and the flag is cleared HERE rather than at :L382. There are therefore two clearing sites,
    //  and unifying them would run the tail for a result of 3 that the oracle deliberately bypasses.
    //  Note also which value this is: 3 is returned RAW. The item-change dispatch rewrites its own 3
    //  into a 1 at :L225; this routine has no such rewrite, so a 3 leaves here as a 3.
    // ==============================================================================================

    /// <summary>
    /// Every way of arriving at <c>:L347</c> with a zero result and empty edit text.
    /// </summary>
    /// <remarks>
    /// EVERY ROW STASHES SOMETHING OUTSIDE <c>{1, 3}</c> AND ANSWERS ZERO OR NULL, because those are
    /// jointly what leaves the result at zero: a stashed 1 or 3 pre-sets at <c>:L339</c> and a non-zero
    /// handler answer overwrites at <c>:L343</c>, and either makes <c>:L347</c> false so the emptiness of
    /// the data is never consulted at all. That exclusion is itself asserted, by
    /// <see cref="EmptyDataArmIsNotTakenWhenTheResultIsAlreadyNonZero"/>.
    /// </remarks>
    public static TheoryData<long, bool, long?, string> EmptyDataRows =>
        new()
        {
            // :L344's coercion reaches :L347 with zero, and the data is empty.
            { 0L, false, null, QuotedMessage },
            { 0L, true, null, QuotedMessage },
            // An explicit zero from the handler.
            { 0L, true, 0L, QuotedMessage },
            // The validation message the column WOULD have supplied is irrelevant: :L350 is never
            // reached, so none of these three shapes is read.
            { 0L, false, null, "" },
            { 0L, false, null, LegacyMessageKeys.NoValidationMessagePlaceholder },
            // Stashes in and outside the alphabet that are neither 1 nor 3.
            { 2L, false, null, QuotedMessage },
            { -1L, true, 0L, QuotedMessage },
            { -2L, false, null, QuotedMessage },
            { 42L, false, null, QuotedMessage },
        };

    [Theory]
    [MemberData(nameof(EmptyDataRows))]
    public void EmptyDataClearsTheGuardFirstAndReturnsThreeWithoutRunningTheTail(
        long stash,
        bool installHandler,
        long? handlerResult,
        string validationMsg)
    {
        FakeDataWindowHost host = NewHost(
            validationMsg: validationMsg,
            status: ItemStatus.DataModified);

        ValidationSession session = NewSession(stash: stash);
        List<(long Row, string ColumnName, string Data)> observed = [];

        if (installHandler)
        {
            ScriptItemError(host, handlerResult, observed);
        }

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), string.Empty);

        // :L362 - THREE, raw and unrewritten.
        Assert.Equal((long)ItemChangeResult.KeepValueNoFocusMove, outcome.RawResult);
        Assert.Equal(ItemChangeResult.KeepValueNoFocusMove, outcome.Result);
        Assert.True(outcome.EmptyData);

        // :L361 - the guard is cleared BEFORE the return, on both the session and the snapshot.
        Assert.False(session.InItemValidationError);
        Assert.False(outcome.State.InItemValidationError);

        // :L350 WAS NEVER REACHED. No property read, no message, no fallback, no structured error.
        Assert.Equal(0, CountValidationMessageReads(host));
        Assert.Equal(string.Empty, outcome.ValidationMessage);
        Assert.False(outcome.ValidationMessageFellBack);
        Assert.Null(outcome.Error);

        // :L366-L380 WAS NEVER REACHED EITHER. The row test did not even run, which is why
        // RowStillExists is null rather than false - "not evaluated" and "evaluated and refused" are
        // different observations and are reported differently.
        Assert.Null(outcome.RowStillExists);
        Assert.False(outcome.ValueRestored);
        Assert.False(host.CallLog.Contains("SetItem(object?)"));
        Assert.False(host.CallLog.Contains("SetItemStatus"));
        Assert.Equal(0, host.CallLog.CountOf("RowCount"));

        // :L334-L335 DID run, because they precede the arm - so the snapshot is fully populated even
        // though nothing was ever restored from it.
        Assert.Equal(OriginalCellValue, outcome.OriginalValue);
        Assert.Equal(ItemStatus.DataModified, outcome.OriginalStatus);
        Assert.Equal(ColumnIdOfFirstColumn, outcome.ColumnId);

        // :L331 - the stash was still consumed and reported before the arm was taken.
        Assert.Equal(stash, outcome.StashedRawItemChangeRetCode);
        Assert.False(outcome.PreSetFromStash);
        Assert.Equal(installHandler, observed.Count == 1);

        // The cell is untouched: refusing an empty entry leaves the buffer exactly as it was.
        Assert.Equal(
            OriginalCellValue,
            host.GetBufferValue(DwBuffer.Primary, Row, ColumnIdOfFirstColumn));
    }

    /// <summary>
    /// The arrivals at empty edit text that do NOT take the early return.
    /// </summary>
    /// <remarks>
    /// <c>:L347</c> gates the whole emptiness test on the result still being zero, so a pre-set or a
    /// non-zero handler answer makes the edit text's emptiness IRRELEVANT. Without these rows the matrix
    /// would leave open the reading that empty data returns 3 unconditionally, which is the single most
    /// plausible way to get this routine wrong.
    /// </remarks>
    public static TheoryData<long, bool, long?, long> EmptyDataNotTakenRows =>
        new()
        {
            // :L338-L340 pre-set to 1, so :L347 is false and the tail runs normally.
            { 1L, false, null, 1L },
            { 3L, false, null, 1L },
            // :L343 answers non-zero, so :L347 is false for the same reason.
            { 0L, true, 1L, 1L },
            { 0L, true, 2L, 2L },
            { 0L, true, 3L, 3L },
            { 0L, true, -1L, -1L },
        };

    [Theory]
    [MemberData(nameof(EmptyDataNotTakenRows))]
    public void EmptyDataArmIsNotTakenWhenTheResultIsAlreadyNonZero(
        long stash,
        bool installHandler,
        long? handlerResult,
        long expectedRawResult)
    {
        FakeDataWindowHost host = NewHost(status: ItemStatus.DataModified);
        ValidationSession session = NewSession(stash: stash);
        List<(long Row, string ColumnName, string Data)> observed = [];

        if (installHandler)
        {
            ScriptItemError(host, handlerResult, observed);
        }

        // EMPTY edit text, and it makes no difference.
        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), string.Empty);

        Assert.False(outcome.EmptyData);
        Assert.Equal(expectedRawResult, outcome.RawResult);

        // No message either - :L347 is false, so :L350 is out of reach from this direction too.
        Assert.Equal(0, CountValidationMessageReads(host));
        Assert.Null(outcome.Error);

        // The tail RAN, which is the difference that matters: the row test was evaluated.
        if (expectedRawResult is (long)ItemChangeResult.TriggerValidationError
            or (long)ItemChangeResult.KeepValueNoFocusMove)
        {
            Assert.True(outcome.RowStillExists);
        }
        else
        {
            // :L366 has no `case else`, so a result outside {1, 3} leaves the construct with nothing
            // done - and the row test is not reached.
            Assert.Null(outcome.RowStillExists);
        }

        Assert.False(session.InItemValidationError);
    }

    // ==============================================================================================
    //  PHASE 6 - THE TAIL RESTORE TRUTH TABLE                              se_cst_dw.sru:L366-L384
    //  --------------------------------------------------------------------------------------------
    //  :L366  choose case rtCode
    //  :L367      case 1,3                                    <- TWO VALUES, and NO `case else`
    //  :L368          //*此时有可能行被删除，所以需要检查（由MessageBox处理的Post消息）
    //                 "the row may have been deleted by now, so it has to be checked (by the posted
    //                  message MessageBox processed)"
    //  :L369          if row <= RowCount() and nItemChangeRetCode <> 3 then
    //  :L372              if aOrgValue = dwo.Primary[row] or (IsNull(aOrgValue) and IsNull(...)) then
    //  :L375                  SetItem(row,Long(dwo.ID),aOrgValue)
    //  :L376                  SetItemStatus(row,Long(dwo.ID),Primary!,orgStatus)
    //  :L382  _bDwnItemValidationError = false
    //  :L384  return rtCode
    //
    //  THE `row <= RowCount()` TEST IS DEFENSIVE AND MUST SURVIVE THE MIGRATION EVEN THOUGH THE
    //  STRUCTURED-ERROR REPLACEMENT NO LONGER POSTS A MESSAGE. The oracle's own comment at :L368 gives
    //  the reason: putting up a dialog pumps the Win32 message queue, and one of those messages may have
    //  DELETED THE ROW while the dialog was up. A port that reasoned "there is no dialog now, so the row
    //  cannot vanish" and dropped the test would be making a behavioural change: the row can still be
    //  removed by the reached handler itself, which is exactly what the deletion rows below do, and
    //  writing to a row that no longer exists is precisely what the guard prevents. Note too that it is
    //  an UPPER BOUND ONLY - the oracle does not test `row >= 1` - and that is reproduced rather than
    //  tightened, because RowCount() IS the last valid row number for one-based DataWindow rows.
    // ==============================================================================================

    /// <summary>
    /// The full truth table of the tail: the two-value case arm, the two conditions at
    /// <c>:L369</c>, the equality test at <c>:L372</c> and the port's own identifier condition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FIVE CONDITIONS MUST HOLD TOGETHER FOR THE BUFFER TO BE WRITTEN, and each is failed individually
    /// by a row below: the result is 1 or 3 [<c>:L367</c>]; the row still exists [<c>:L369</c>]; the
    /// stashed code was NOT 3 [<c>:L369</c>]; the buffer value has not moved [<c>:L372</c>]; and the
    /// column identifier resolves - the fifth being imposed by the port and reaching the same outcome as
    /// PowerScript's own <c>Long(null)</c> addressing.
    /// </para>
    /// <para>
    /// <c>expectedRowStillExists</c> IS THREE-VALUED ON PURPOSE. <see langword="null"/> means the tail was
    /// never entered because the result matched no case, <see langword="false"/> means the test ran and
    /// refused, and <see langword="true"/> means it ran and passed. Collapsing "not evaluated" into
    /// "false" would make the deletion guard indistinguishable from a result of 2.
    /// </para>
    /// <para>
    /// A RESULT OF 3 CAN ONLY ARRIVE HERE FROM THE HANDLER. The empty-data arm returns its 3 early and
    /// the pre-set produces 1, so <c>:L367</c>'s second value is reachable only when
    /// <c>Event ItemError</c> answers 3 - which is why those rows all install a handler.
    /// </para>
    /// </remarks>
    public static TheoryData<long, bool, long?, bool, bool, bool, long, bool?, bool>
        RestoreTruthTableRows =>
        new()
        {
            // ALL FIVE HOLD. The base event answers null, :L344 coerces, the message arm sets 1.
            { 0L, false, null, false, false, false, 1L, true, true },
            // All five hold, with the handler answering zero explicitly.
            { 0L, true, 0L, false, false, false, 1L, true, true },
            // :L367 FIRST VALUE straight from the handler.
            { 0L, true, 1L, false, false, false, 1L, true, true },
            // :L367 SECOND VALUE - the only way a 3 reaches the tail.
            { 0L, true, 3L, false, false, false, 3L, true, true },
            // :L338-L340 pre-set from a stashed 1. The stash is not 3, so the restore still happens.
            { 1L, false, null, false, false, false, 1L, true, true },
            // :L369 SECOND CONDITION FAILS - a stashed 3 suppresses the restore, which is how the
            // oracle honours :L224's "keep the value, do not switch focus". The row test still ran.
            { 3L, false, null, false, false, false, 1L, true, false },
            // :L369 FIRST CONDITION FAILS - the row was deleted while the handler ran.
            { 0L, true, 0L, true, false, false, 1L, false, false },
            // The same, with the handler answering 3 so the second case value is covered too.
            { 0L, true, 3L, true, false, false, 3L, false, false },
            // :L372 FAILS - the handler moved the buffer value, so the snapshot must NOT be written back
            // over it. This is the "prevent overwriting" comment at :L371, made observable.
            { 0L, true, 0L, false, true, false, 1L, true, false },
            { 0L, true, 3L, false, true, false, 3L, true, false },
            // THE PORT'S FIFTH CONDITION - `Long(dwo.ID)` is null, so nothing is addressable and nothing
            // is written, while :L369 is still evaluated.
            { 0L, false, null, false, false, true, 1L, true, false },
            // :L366 MATCHES NO CASE. There is no `case else` at :L380, so every other result leaves the
            // construct having done nothing - and the row test is never reached.
            { 0L, true, 2L, false, false, false, 2L, null, false },
            { 0L, true, -1L, false, false, false, -1L, null, false },
            { 0L, true, -2L, false, false, false, -2L, null, false },
            { 0L, true, 99L, false, false, false, 99L, null, false },
        };

    [Theory]
    [MemberData(nameof(RestoreTruthTableRows))]
    public void TailRestoreRequiresEveryConditionToHold(
        long stash,
        bool installHandler,
        long? handlerResult,
        bool deleteRowDuringHandler,
        bool moveValueDuringHandler,
        bool nullColumnIdentifier,
        long expectedRawResult,
        bool? expectedRowStillExists,
        bool expectRestore)
    {
        const string MovedValue = "moved by the handler";

        // DataModified rather than NotModified, so a restored status is DISTINGUISHABLE from a status
        // that was simply never written.
        FakeDataWindowHost host = NewHost(status: ItemStatus.DataModified);
        FakeDataWindowObject dwo = host.DwObject(Column);

        if (nullColumnIdentifier)
        {
            dwo.ID = null;
        }

        ValidationSession session = NewSession(stash: stash);

        if (installHandler)
        {
            host.ItemErrorHandler = (row, _, _) =>
            {
                // Both side effects happen INSIDE the reached handler, which is after the snapshot at
                // :L334-L335 and before the tests at :L369 and :L372 - the same window in which the
                // oracle's dialog pumped its messages.
                if (deleteRowDuringHandler)
                {
                    Assert.True(host.SimulateRowDeletedByPostedMessage(row));
                }

                if (moveValueDuringHandler)
                {
                    host.SetBufferValue(DwBuffer.Primary, Row, ColumnIdOfFirstColumn, MovedValue);
                }

                return handlerResult;
            };
        }

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, Row, dwo, OffendingText);

        // :L384
        Assert.Equal(expectedRawResult, outcome.RawResult);
        Assert.Equal(ItemChangeProtocol.Classify(expectedRawResult), outcome.Result);

        // :L369's first condition, three-valued.
        Assert.Equal(expectedRowStillExists, outcome.RowStillExists);

        // :L375-L376
        Assert.Equal(expectRestore, outcome.ValueRestored);
        Assert.Equal(expectRestore, host.CallLog.Contains("SetItem(object?)"));
        Assert.Equal(expectRestore, host.CallLog.Contains("SetItemStatus"));

        // The row test is evaluated exactly when the case arm matched, and not otherwise.
        Assert.Equal(
            expectedRowStillExists.HasValue ? 1 : 0,
            host.CallLog.CountOf("RowCount"));

        if (expectRestore)
        {
            // BOTH WRITES, WITH THE ARGUMENTS THE ORACLE PASSES, and value before status because
            // :L375 precedes :L376.
            int setItemIndex = host.CallLog.IndexOf("SetItem(object?)");
            int setStatusIndex = host.CallLog.IndexOf("SetItemStatus");

            Assert.True(setItemIndex >= 0);
            Assert.True(setStatusIndex > setItemIndex);

            DataWindowCallRecord setItem = host.CallLog.RecordAt(setItemIndex);
            Assert.Equal(3, setItem.Arguments.Count);
            Assert.Equal(Row, setItem.Arguments[0]);
            Assert.Equal(ColumnIdOfFirstColumn, setItem.Arguments[1]);
            Assert.Equal(OriginalCellValue, setItem.Arguments[2]);

            DataWindowCallRecord setStatus = host.CallLog.RecordAt(setStatusIndex);
            Assert.Equal(4, setStatus.Arguments.Count);
            Assert.Equal(Row, setStatus.Arguments[0]);
            Assert.Equal(ColumnIdOfFirstColumn, setStatus.Arguments[1]);
            Assert.Equal(DwBuffer.Primary, setStatus.Arguments[2]);
            Assert.Equal(ItemStatus.DataModified, setStatus.Arguments[3]);

            // And the buffer really holds the snapshot again, value and status together.
            Assert.Equal(
                OriginalCellValue,
                host.GetBufferValue(DwBuffer.Primary, Row, ColumnIdOfFirstColumn));
            Assert.Equal(
                ItemStatus.DataModified,
                host.GetBufferItemStatus(DwBuffer.Primary, Row, ColumnIdOfFirstColumn));
        }
        else if (moveValueDuringHandler)
        {
            // :L372's WHOLE PURPOSE: the handler's value survives, because the snapshot was NOT written
            // back over it. Asserting only "no restore happened" would miss that the point is the
            // handler's data being preserved.
            Assert.Equal(
                MovedValue,
                host.GetBufferValue(DwBuffer.Primary, Row, ColumnIdOfFirstColumn));
        }
        else if (deleteRowDuringHandler)
        {
            // The row really is gone, so :L375 would have addressed a row that no longer exists.
            Assert.Equal(0L, host.RowCount());
        }

        // :L382 on every one of these paths, because none of them returned early.
        Assert.False(session.InItemValidationError);
        Assert.False(outcome.State.InItemValidationError);
        Assert.False(outcome.EmptyData);
        Assert.Equal(stash, outcome.StashedRawItemChangeRetCode);
    }

    /// <summary>
    /// The three-armed equality of <c>:L372</c> - equal, or BOTH NULL.
    /// </summary>
    /// <remarks>
    /// THE ASYMMETRY IS THE GUARD'S PURPOSE. One-sided null is NOT equal, so it means the handler changed
    /// the cell and the snapshot must not be written back over it; both-null IS equal, so an untouched
    /// null cell is restored like any other. The empty string is a THIRD, distinct state - equal to
    /// itself and unequal to null - which is why the last two rows exist: collapsing null and the empty
    /// string would make an emptied cell look untouched and silently overwrite it.
    /// </remarks>
    public static TheoryData<string?, bool, string?, bool> BufferEqualityRows =>
        new()
        {
            // Untouched, populated - equal.
            { OriginalCellValue, false, null, true },
            // Untouched, NULL - :L372's second arm, both null.
            { null, false, null, true },
            // Untouched, EMPTY STRING - equal to itself.
            { "", false, null, true },
            // Rewritten to the SAME value - still equal, so the restore goes ahead.
            { OriginalCellValue, true, OriginalCellValue, true },
            // Genuinely moved - not equal.
            { OriginalCellValue, true, "changed", false },
            // Populated snapshot, cell NULLED by the handler - one-sided null, NOT equal.
            { OriginalCellValue, true, null, false },
            // NULL snapshot, cell POPULATED by the handler - the other one-sided null, NOT equal.
            { null, true, "now populated", false },
            // NULL snapshot rewritten to the EMPTY STRING - distinct states, NOT equal.
            { null, true, "", false },
            // EMPTY-STRING snapshot nulled - the mirror of the row above, and equally unequal.
            { "", true, null, false },
        };

    [Theory]
    [MemberData(nameof(BufferEqualityRows))]
    public void RestoreHappensOnlyWhenTheBufferValueHasNotMoved(
        string? initialValue,
        bool rewriteDuringHandler,
        string? rewrittenValue,
        bool expectRestore)
    {
        FakeDataWindowHost host = NewHost(initialValue, status: ItemStatus.DataModified);
        ValidationSession session = NewSession();

        if (rewriteDuringHandler)
        {
            host.ItemErrorHandler = (_, _, _) =>
            {
                host.SetBufferValue(DwBuffer.Primary, Row, ColumnIdOfFirstColumn, rewrittenValue);
                return 0L;
            };
        }

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), OffendingText);

        // :L334 - the snapshot, with null and the empty string kept apart.
        Assert.Equal(initialValue, outcome.OriginalValue);

        // :L372
        Assert.Equal(expectRestore, outcome.ValueRestored);
        Assert.Equal(expectRestore, host.CallLog.Contains("SetItem(object?)"));

        // Cross-checked against the shared predicate the item-change protocol uses at :L198-L202, so the
        // two sites cannot drift apart in their treatment of null.
        object? currentValue = rewriteDuringHandler ? rewrittenValue : initialValue;
        Assert.Equal(expectRestore, ItemChangeProtocol.IsBufferValueEqual(initialValue, currentValue));

        // Whichever way it went, the cell holds what the oracle leaves behind.
        Assert.Equal(
            expectRestore ? initialValue : currentValue,
            host.GetBufferValue(DwBuffer.Primary, Row, ColumnIdOfFirstColumn));

        Assert.True(outcome.RowStillExists);
        Assert.Equal((long)ItemChangeResult.TriggerValidationError, outcome.RawResult);
    }

    /// <summary>
    /// Every reachable arrival at <c>:L366</c>, asserting the one value that can never get there.
    /// </summary>
    /// <remarks>
    /// A STRUCTURAL PROPERTY WORTH PINNING, BECAUSE IT EXPLAINS AN ABSENCE IN THE ORACLE. Entering
    /// <c>:L347</c> with a zero result guarantees one of two outcomes: the message arm sets 1 at
    /// <c>:L358</c>, or the empty-data arm returns 3 at <c>:L362</c>. So the dispatch at <c>:L366</c> is
    /// NEVER reached with zero, a <c>case 0</c> arm would be dead code, and the oracle not having one is
    /// consistent rather than an omission. A port that added a zero arm "for completeness" would be
    /// adding a branch the legacy cannot execute.
    /// </remarks>
    public static TheoryData<long, bool, long?, string> DispatchArrivalRows =>
        new()
        {
            { 0L, false, null, OffendingText },
            { 0L, false, null, "" },
            { 0L, true, 0L, OffendingText },
            { 0L, true, 0L, "" },
            { 0L, true, 1L, OffendingText },
            { 0L, true, 2L, "" },
            { 0L, true, 3L, OffendingText },
            { 0L, true, -1L, "" },
            { 1L, false, null, OffendingText },
            { 3L, false, null, "" },
            { 2L, false, null, OffendingText },
            { 42L, true, null, "" },
        };

    [Theory]
    [MemberData(nameof(DispatchArrivalRows))]
    public void ResultIsNeverZeroWhichIsWhyTheDispatchHasNoZeroArm(
        long stash,
        bool installHandler,
        long? handlerResult,
        string data)
    {
        FakeDataWindowHost host = NewHost();
        ValidationSession session = NewSession(stash: stash);
        List<(long Row, string ColumnName, string Data)> observed = [];

        if (installHandler)
        {
            ScriptItemError(host, handlerResult, observed);
        }

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, Row, host.DwObject(Column), data);

        // THE STRUCTURAL PROPERTY: the RAW result is never zero, on any arrival.
        Assert.NotEqual(0L, outcome.RawResult);

        // THE CLASSIFICATION IS A DIFFERENT CLAIM, AND CONFLATING THE TWO IS THE TRAP. Classification is
        // TOTAL - anything outside {1, 2, 3} reports as Default, which is exactly what the oracle's
        // `case else` does with it - so a handler answering -1 produces a raw result of -1 whose
        // classification IS Default despite the raw value being non-zero. That is why both are published
        // rather than only the typed view: a consumer must branch on the specific raw value, and a
        // characterization recording compares the raw value, because that is what the legacy returned.
        Assert.Equal(ItemChangeProtocol.Classify(outcome.RawResult), outcome.Result);

        if (outcome.RawResult is not ((long)ItemChangeResult.TriggerValidationError
            or (long)ItemChangeResult.RestoreAndRejectText
            or (long)ItemChangeResult.KeepValueNoFocusMove))
        {
            Assert.Equal(ItemChangeResult.Default, outcome.Result);
            Assert.NotEqual(0L, outcome.RawResult);
        }

        // And the value came from EXACTLY ONE of the three producing sites - the message arm at :L358,
        // the empty-data arm at :L362, or the handler at :L343.
        bool fromEmptyDataArm = outcome.EmptyData;
        bool fromMessageArm = outcome.Error is not null;
        bool fromHandler = !fromEmptyDataArm && !fromMessageArm;

        Assert.Equal(
            1,
            (fromEmptyDataArm ? 1 : 0) + (fromMessageArm ? 1 : 0) + (fromHandler ? 1 : 0));

        // The empty-data arm and the message arm are mutually exclusive by construction, because :L348
        // is a two-way branch on the same edit text.
        Assert.False(fromEmptyDataArm && fromMessageArm);

        Assert.False(session.InItemValidationError);
    }
}
