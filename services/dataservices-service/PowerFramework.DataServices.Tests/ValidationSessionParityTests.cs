// ==================================================================================================
//  ValidationSessionParityTests - the parity matrix for Domain/ValidationSession.cs
//  ------------------------------------------------------------------------------------------------
//  BEHAVIOURAL ORACLE   ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru  (616 lines)
//                         :L88-L96    the four cross-event state fields
//                         :L192-L196  the item-change re-entrancy dance
//                         :L322-L385  ondwnitemvalidationerror, step by step
//                         :L388-L390  the guarded Post of the deferred accept
//                         :L537-L558  _of_postaccepttext, the continuation body
//                       ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru:L17  CAT_DWSVC
//                       ws_objects/pfw.ui.pbl.src/i18n.srf:L17-L18  the silent passthrough
//
//  The legacy tree is READ ONLY and is never an edit target (constraint C-C); it is the oracle these
//  tests characterize. Every assertion below states what the legacy ACTUALLY does, including where
//  that is a defect - a test that asserted the corrected behaviour would be asserting a regression.
//
//  SHAPE. Table-driven parity matrices expressed as theories with inline data (AAP 0.6.7), driving
//  every branch through three injected seams - the host, the clock and the localization facade - so
//  no live DataWindow, no real waiting and no ambient state is involved anywhere (constraint C-H).
// ==================================================================================================
using System.Collections.Immutable;

using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

using Xunit;

// The three domain types below are ALIASED RATHER THAN IMPORTED PLAIN, because
// PowerFramework.Contracts.DataServices.V1 publishes a type of the same name for each of them - the
// in-process half and the wire half of a matched pair. An unaliased reference is CS0104, and the
// alias makes each use say which half it means.
using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
using EventGate = PowerFramework.DataServices.Domain.EventGate;
using ItemChangeResult = PowerFramework.DataServices.Domain.ItemChangeResult;
using ItemStatus = PowerFramework.Contracts.Common.V1.ItemStatus;
using WireItemChangeResult = PowerFramework.Contracts.DataServices.V1.ItemChangeResult;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// A deterministic clock double for the injected <see cref="TimeProvider"/> seam. AAP 0.6.7 requires
/// every clock read to be substitutable so a non-deterministic value is masked from BOTH the master
/// and the candidate recording; expiry is therefore asserted with no real waiting anywhere.
/// </summary>
internal sealed class ValidationSessionTestClock : TimeProvider
{
    private DateTimeOffset _now = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}

/// <summary>
/// A localization provider double. It translates by table, counts its invocations, and can assign
/// <see langword="null"/> to the <c>ref</c> parameter - the one input that makes the facade answer null
/// for a non-null argument, which is the case decision D-12 in the subject file exists to absorb.
/// </summary>
internal sealed class ValidationSessionTestI18nProvider : II18nProvider
{
    private readonly Dictionary<string, string?> _table = new(StringComparer.Ordinal);

    public long Calls { get; private set; }

    public ValidationSessionTestI18nProvider Add(string source, string? translated)
    {
        _table[source] = translated;
        return this;
    }

    public long OnTranslate(long source, long category, ref string? text)
    {
        Calls++;

        if (text is not null && _table.TryGetValue(text, out string? translated))
        {
            text = translated;
            return 1;
        }

        return 0;
    }
}

public sealed class ValidationSessionParityTests
{
    private const string Column = "name";

    private static FakeDataWindowHost NewHost(object? cellValue = null, string colType = "char(50)")
    {
        FakeDataWindowHost host = new();
        host.AddColumn(Column, colType);
        host.AddRow(cellValue);
        host.RecordsReads = true;
        return host;
    }

    private static ValidationSession NewSession(
        I18n? i18n = null,
        TimeProvider? clock = null,
        uint mask = 0u) =>
        new("adhoc-session", "dw-1", mask, new SessionLifetimeOptions(), i18n, clock);

    // ==============================================================================================
    //  STEP 1 - THE RE-ENTRANCY GUARD                                       se_cst_dw.sru:L327
    // ==============================================================================================

    [Fact]
    public void ReEntrantCall_ReturnsOneAndNothingElseHappens()
    {
        FakeDataWindowHost host = NewHost("original");
        ValidationSession session = NewSession();
        ValidationErrorOutcome? inner = null;
        bool flagSeenSetInside = false;

        host.ItemErrorHandler = (row, dwo, data) =>
        {
            flagSeenSetInside = session.InItemValidationError;
            inner = session.OnDwnItemValidationError(host, row, dwo, data);
            return 0L;
        };

        ValidationErrorOutcome outer =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.True(flagSeenSetInside);
        Assert.NotNull(inner);
        Assert.True(inner!.ReEntered);
        Assert.Equal(1L, inner.RawResult);
        Assert.Equal(ItemChangeResult.TriggerValidationError, inner.Result);
        Assert.False(inner.ItemErrorRaised);
        Assert.False(inner.ValueRestored);
        Assert.Null(inner.RowStillExists);
        Assert.Equal(string.Empty, inner.ValidationMessage);
        Assert.Null(inner.Error);
        Assert.Equal(0L, inner.StashedRawItemChangeRetCode);
        Assert.Null(inner.ColumnId);
        Assert.Equal(ItemStatus.NotModified, inner.OriginalStatus);

        // :L327 does NOT clear the flag - the outer invocation still owns it.
        Assert.True(inner.State.InItemValidationError);

        // The inner call took NO snapshot: only the outer one read the item status.
        Assert.Equal(1, host.CallLog.CountOf("GetItemStatus"));

        // The outer call ran to completion and cleared the flag at :L382.
        Assert.False(outer.ReEntered);
        Assert.False(session.InItemValidationError);
    }

    // ==============================================================================================
    //  STEP 3 - CONSUME AND CLEAR                                     se_cst_dw.sru:L331-L332
    // ==============================================================================================

    [Fact]
    public void ConsumeAndClear_MakesTheStashSingleUse()
    {
        FakeDataWindowHost host = NewHost("original");
        ValidationSession session = NewSession();
        session.ItemChangeRetCode = 1L;

        ValidationErrorOutcome first =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.True(first.PreSetFromStash);
        Assert.False(first.ItemErrorRaised);
        Assert.Equal(1L, first.StashedRawItemChangeRetCode);
        Assert.Equal(0L, session.ItemChangeRetCode);
        Assert.Equal(0L, first.State.RawItemChangeRetCode);

        ValidationErrorOutcome second =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.False(second.PreSetFromStash);
        Assert.True(second.ItemErrorRaised);
        Assert.Equal(0L, second.StashedRawItemChangeRetCode);
    }

    // ==============================================================================================
    //  STEPS 5 AND 6 - THE PRE-SET AND THE ItemError RAISE          se_cst_dw.sru:L338-L345
    // ==============================================================================================

    [Theory]
    [InlineData(1L)]
    [InlineData(3L)]
    public void StashedOneOrThree_PreSetsOneAndSkipsItemError(long stashed)
    {
        FakeDataWindowHost host = NewHost("original");
        ValidationSession session = NewSession();
        session.ItemChangeRetCode = stashed;
        bool raised = false;
        host.ItemErrorHandler = (_, _, _) =>
        {
            raised = true;
            return 0L;
        };

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.True(outcome.PreSetFromStash);
        Assert.False(raised);
        Assert.False(outcome.ItemErrorRaised);
        Assert.Equal(1L, outcome.RawResult);
        Assert.Null(outcome.Error);
        Assert.Equal(stashed, outcome.StashedRawItemChangeRetCode);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(2L)]
    [InlineData(-1L)]
    [InlineData(42L)]
    public void StashedOutsideOneAndThree_RaisesItemError(long stashed)
    {
        FakeDataWindowHost host = NewHost("original");
        ValidationSession session = NewSession();
        session.ItemChangeRetCode = stashed;

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.False(outcome.PreSetFromStash);
        Assert.True(outcome.ItemErrorRaised);

        // D-1: the raw stash survives verbatim, even for a value outside the alphabet.
        Assert.Equal(stashed, outcome.StashedRawItemChangeRetCode);
        Assert.Equal(ItemChangeProtocol.Classify(stashed), outcome.StashedItemChangeResult);
    }

    [Fact]
    public void ItemErrorReturningNull_IsCoercedToZeroAndReachesTheMessageBranch()
    {
        FakeDataWindowHost host = NewHost("original");
        host.SetValidationMessage(Column, "'Bad'");
        ValidationSession session = NewSession();
        host.ItemErrorHandler = (_, _, _) => null;

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.True(outcome.ItemErrorRaised);
        Assert.True(outcome.ItemErrorReturnedNull);
        Assert.NotNull(outcome.Error);
        Assert.Equal(1L, outcome.RawResult);
    }

    [Fact]
    public void ItemErrorReturningNonZero_SkipsTheMessageBranchEntirely()
    {
        FakeDataWindowHost host = NewHost("original");
        host.SetValidationMessage(Column, "'Bad'");
        ValidationSession session = NewSession();
        host.ItemErrorHandler = (_, _, _) => 2L;

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.True(outcome.ItemErrorRaised);
        Assert.False(outcome.ItemErrorReturnedNull);
        Assert.Null(outcome.Error);
        Assert.Equal(string.Empty, outcome.ValidationMessage);
        Assert.Equal(2L, outcome.RawResult);

        // 2 is outside {1,3}, so the tail arm never ran.
        Assert.Null(outcome.RowStillExists);
        Assert.False(outcome.ValueRestored);
    }

    // ==============================================================================================
    //  STEP 7 - THE STRIP, THE FALLBACK AND THE DIALOG               se_cst_dw.sru:L348-L358
    // ==============================================================================================

    [Theory]
    // length 0 - the strip is skipped and the fallback fires.
    [InlineData("", "输入了无效的值!", true)]
    // length 1 - untouched, and it is exactly the DataWindow's "no message" placeholder.
    [InlineData("?", "输入了无效的值!", true)]
    // length 1 - untouched, and used as-is.
    [InlineData("x", "x", false)]
    // length 2 - UNTOUCHED, because the guard is `> 2`. A bare pair of quotes survives verbatim.
    [InlineData("''", "''", false)]
    // length 3 - exactly one character survives, and BOTH the first and the last are dropped.
    [InlineData("'a'", "a", false)]
    // a normal quoted message.
    [InlineData("'Bad value'", "Bad value", false)]
    [InlineData("\"输入无效\"", "输入无效", false)]
    public void ValidationMessageStripBoundary(string described, string expected, bool expectFallback)
    {
        FakeDataWindowHost host = NewHost("original");
        host.SetValidationMessage(Column, described);
        ValidationSession session = NewSession();

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.Equal(expected, outcome.ValidationMessage);
        Assert.Equal(expectFallback, outcome.ValidationMessageFellBack);
        Assert.NotNull(outcome.Error);
        Assert.Equal(expected, outcome.Error!.Text);
        Assert.Equal(1L, outcome.RawResult);
    }

    [Fact]
    public void StructuredError_CarriesTextCategoryArgumentsAndSeverity()
    {
        FakeDataWindowHost host = NewHost("original");
        host.SetValidationMessage(Column, "'Bad value'");
        ValidationSession session = NewSession();

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        ValidationStructuredError error = Assert.IsType<ValidationStructuredError>(outcome.Error);

        Assert.Equal("Bad value", error.Text);
        Assert.Equal(ValidationStructuredError.LegacyTitleSource, error.Title);
        Assert.True(error.Localized);
        Assert.Equal(Categories.CAT_DWSVC, error.LocalizationCategory);
        Assert.Empty(error.FormatArguments);
        Assert.Equal(DialogSeverity.StopSign, error.Severity);
        Assert.Null(error.ReturnCode);
        Assert.Equal(357, ValidationStructuredError.LegacyLine);

        // The severity value agrees with the wire enum member for member.
        Assert.Equal((int)Severity.StopSign, (int)error.Severity);
    }

    [Fact]
    public void SilentPassthroughLocalization_LeavesTheTextUnchanged()
    {
        FakeDataWindowHost host = NewHost("original");
        host.SetValidationMessage(Column, "");
        ValidationSession session = NewSession(new I18n());

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.Equal(
            ValidationStructuredError.LegacyFallbackTextSource
                + ValidationStructuredError.LegacyFallbackSuffix,
            outcome.ValidationMessage);
        Assert.Equal(ValidationStructuredError.LegacyTitleSource, outcome.Error!.Title);
    }

    [Fact]
    public void InstalledProvider_TranslatesBothStringsAndTheSuffixIsAppendedAfterTranslation()
    {
        FakeDataWindowHost host = NewHost("original");
        host.SetValidationMessage(Column, "");

        ValidationSessionTestI18nProvider provider = new ValidationSessionTestI18nProvider()
            .Add(ValidationStructuredError.LegacyFallbackTextSource, "An invalid value was entered")
            .Add(ValidationStructuredError.LegacyTitleSource, "Error");

        I18n i18n = new();
        Assert.Equal(RetCode.OK, i18n.I18N(provider));

        ValidationSession session = NewSession(i18n);

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.Equal("An invalid value was entered!", outcome.ValidationMessage);
        Assert.Equal("Error", outcome.Error!.Title);
        Assert.Equal(2L, provider.Calls);
    }

    [Fact]
    public void ProviderNullingTheRefParameter_FallsBackToTheUntranslatedSource()
    {
        FakeDataWindowHost host = NewHost("original");
        host.SetValidationMessage(Column, "");

        ValidationSessionTestI18nProvider provider = new ValidationSessionTestI18nProvider()
            .Add(ValidationStructuredError.LegacyFallbackTextSource, null)
            .Add(ValidationStructuredError.LegacyTitleSource, null);

        I18n i18n = new();
        i18n.I18N(provider);

        ValidationSession session = NewSession(i18n);

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.Equal(
            ValidationStructuredError.LegacyFallbackTextSource
                + ValidationStructuredError.LegacyFallbackSuffix,
            outcome.ValidationMessage);
        Assert.Equal(ValidationStructuredError.LegacyTitleSource, outcome.Error!.Title);
    }

    // ==============================================================================================
    //  STEP 7's SECOND ARM - EMPTY DATA                              se_cst_dw.sru:L359-L363
    // ==============================================================================================

    [Fact]
    public void EmptyData_ReturnsThreeClearsTheGuardAndSkipsTheTail()
    {
        FakeDataWindowHost host = NewHost("original");
        host.SetValidationMessage(Column, "'Bad value'");
        ValidationSession session = NewSession();

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), string.Empty);

        Assert.Equal(3L, outcome.RawResult);
        Assert.Equal(ItemChangeResult.KeepValueNoFocusMove, outcome.Result);
        Assert.True(outcome.EmptyData);
        Assert.False(session.InItemValidationError);
        Assert.False(outcome.State.InItemValidationError);

        // The tail never ran.
        Assert.Null(outcome.RowStillExists);
        Assert.False(outcome.ValueRestored);
        Assert.False(host.CallLog.Contains("SetItem(object?)"));
        Assert.False(host.CallLog.Contains("SetItemStatus"));

        // :L350 was never reached either.
        Assert.Equal(string.Empty, outcome.ValidationMessage);
        Assert.Null(outcome.Error);
    }

    // ==============================================================================================
    //  STEP 8 - THE TAIL RESTORE TRUTH TABLE                         se_cst_dw.sru:L366-L380
    // ==============================================================================================

    [Fact]
    public void TailRestore_HappensForResultOneWhenRowExistsStashNotThreeAndValueUnmoved()
    {
        FakeDataWindowHost host = NewHost("original");
        host.SetValidationMessage(Column, "'Bad value'");
        host.SetBufferItemStatus(DwBuffer.Primary, 1L, 1L, ItemStatus.DataModified);
        ValidationSession session = NewSession();

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.Equal(1L, outcome.RawResult);
        Assert.True(outcome.RowStillExists);
        Assert.True(outcome.ValueRestored);
        Assert.True(host.CallLog.Contains("SetItem(object?)"));
        Assert.True(host.CallLog.Contains("SetItemStatus"));
        Assert.Equal("original", outcome.OriginalValue);
        Assert.Equal(ItemStatus.DataModified, outcome.OriginalStatus);
        Assert.Equal(1L, outcome.ColumnId);
    }

    [Fact]
    public void TailRestore_HappensForResultThreeReturnedByTheHandler()
    {
        FakeDataWindowHost host = NewHost("original");
        ValidationSession session = NewSession();
        host.ItemErrorHandler = (_, _, _) => 3L;

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.Equal(3L, outcome.RawResult);
        Assert.False(outcome.EmptyData);
        Assert.True(outcome.RowStillExists);
        Assert.True(outcome.ValueRestored);
    }

    [Fact]
    public void TailRestore_IsSuppressedWhenTheStashedCodeWasThree()
    {
        FakeDataWindowHost host = NewHost("original");
        ValidationSession session = NewSession();
        session.ItemChangeRetCode = 3L;

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.Equal(1L, outcome.RawResult);
        Assert.True(outcome.PreSetFromStash);
        Assert.True(outcome.RowStillExists);
        Assert.False(outcome.ValueRestored);
        Assert.False(host.CallLog.Contains("SetItem(object?)"));
    }

    [Fact]
    public void TailRestore_IsSuppressedWhenTheRowWasDeletedUnderTheDialog()
    {
        FakeDataWindowHost host = NewHost("original");
        host.SetValidationMessage(Column, "'Bad value'");
        ValidationSession session = NewSession();
        host.ItemErrorHandler = (row, _, _) =>
        {
            Assert.True(host.SimulateRowDeletedByPostedMessage(row));
            return 0L;
        };

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.Equal(1L, outcome.RawResult);
        Assert.False(outcome.RowStillExists);
        Assert.False(outcome.ValueRestored);
        Assert.False(host.CallLog.Contains("SetItem(object?)"));
        Assert.False(session.InItemValidationError);
    }

    [Fact]
    public void TailRestore_IsSuppressedWhenTheBufferValueMoved()
    {
        FakeDataWindowHost host = NewHost("original");
        host.SetValidationMessage(Column, "'Bad value'");
        ValidationSession session = NewSession();
        host.ItemErrorHandler = (_, _, _) =>
        {
            host.SetBufferValue(DwBuffer.Primary, 1L, 1L, "changed by the handler");
            return 0L;
        };

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.True(outcome.RowStillExists);
        Assert.False(outcome.ValueRestored);
        Assert.False(host.CallLog.Contains("SetItem(object?)"));
    }

    [Fact]
    public void TailRestore_HappensWhenBothTheSnapshotAndTheCurrentValueAreNull()
    {
        FakeDataWindowHost host = NewHost(null);
        host.SetValidationMessage(Column, "'Bad value'");
        ValidationSession session = NewSession();

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.Null(outcome.OriginalValue);
        Assert.True(outcome.ValueRestored);
    }

    [Fact]
    public void TailRestore_IsSuppressedWhenOnlyOneSideIsNull()
    {
        FakeDataWindowHost host = NewHost(null);
        host.SetValidationMessage(Column, "'Bad value'");
        ValidationSession session = NewSession();
        host.ItemErrorHandler = (_, _, _) =>
        {
            host.SetBufferValue(DwBuffer.Primary, 1L, 1L, "now populated");
            return 0L;
        };

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad");

        Assert.False(outcome.ValueRestored);
    }

    [Fact]
    public void UnresolvableColumnIdentifier_TakesNoSnapshotAndWritesNothing()
    {
        FakeDataWindowHost host = NewHost("original");
        host.SetValidationMessage(Column, "'Bad value'");
        FakeDataWindowObject dwo = host.DwObject(Column);
        dwo.ID = null;
        ValidationSession session = NewSession();

        ValidationErrorOutcome outcome =
            session.OnDwnItemValidationError(host, 1L, dwo, "bad");

        Assert.Null(outcome.ColumnId);
        Assert.Equal(ItemStatus.NotModified, outcome.OriginalStatus);
        Assert.True(outcome.RowStillExists);
        Assert.False(outcome.ValueRestored);
        Assert.False(host.CallLog.Contains("SetItem(object?)"));
        Assert.False(host.CallLog.Contains("GetItemStatus"));
    }

    [Fact]
    public void ArgumentGuards_RejectNulls()
    {
        FakeDataWindowHost host = NewHost("original");
        ValidationSession session = NewSession();
        FakeDataWindowObject dwo = host.DwObject(Column);

        Assert.Throws<ArgumentNullException>(
            () => session.OnDwnItemValidationError(null!, 1L, dwo, "bad"));
        Assert.Throws<ArgumentNullException>(
            () => session.OnDwnItemValidationError(host, 1L, null!, "bad"));
        Assert.Throws<ArgumentNullException>(
            () => session.OnDwnItemValidationError(host, 1L, dwo, null!));
        Assert.Throws<ArgumentNullException>(() => session.DrainDeferredAccept(null!));
    }

    // ==============================================================================================
    //  THE FOUR FIELDS, THE GATE AND THE RE-ENTRANCY SCOPE
    // ==============================================================================================

    [Fact]
    public void FreshSession_HoldsTheFourFieldsAtAFreshControlsValues()
    {
        ValidationSession session = NewSession();

        Assert.Equal(0u, session.DisabledEvent);
        Assert.False(session.DoItemChange);
        Assert.False(session.InItemValidationError);
        Assert.Equal(0L, session.ItemChangeRetCode);
        Assert.Equal(ItemChangeResult.Default, session.StashedItemChangeResult);
        Assert.False(session.DeferredAcceptPending);
        Assert.True(session.IsOpen);
        Assert.Equal("adhoc-session", session.SessionId);
        Assert.Equal("dw-1", session.DataWindowHandle);
    }

    [Fact]
    public void Gate_RoutesThroughEventGateAndRejectsZeroWithoutTouchingTheMask()
    {
        ValidationSession session = NewSession();

        Assert.False(session.IsEventDisabled(EventGate.EID_ITEMCHANGE));
        Assert.Equal(RetCode.OK, session.DisableEvent(EventGate.EID_ITEMCHANGE));
        Assert.True(session.IsEventDisabled(EventGate.EID_ITEMCHANGE));
        Assert.Equal(EventGate.EID_ITEMCHANGE, session.DisabledEvent);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, session.DisableEvent(0u));
        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, session.EnableEvent(0u));
        Assert.Equal(EventGate.EID_ITEMCHANGE, session.DisabledEvent);

        Assert.Equal((int)RetCode.OK, session.EnableEvent(EventGate.EID_ITEMCHANGE));
        Assert.Equal(0u, session.DisabledEvent);
    }

    [Fact]
    public void SeededMask_IsCarriedOntoTheSession()
    {
        ValidationSession session = NewSession(mask: EventGate.EID_ROWFOCUSCHANGE | EventGate.EID_ITEMCHANGE);

        Assert.True(session.IsEventDisabled(EventGate.EID_ROWFOCUSCHANGE));
        Assert.True(session.IsEventDisabled(EventGate.EID_ITEMCHANGE));
        Assert.False(session.IsEventDisabled(EventGate.EID_ITEMFOCUSCHANGE));
        Assert.Equal(5L, session.CaptureState().DisabledEventMask);
    }

    [Fact]
    public void EnterItemChange_RestoresTheSavedValueAndNeverClears()
    {
        ValidationSession session = NewSession();

        using (session.EnterItemChange())
        {
            Assert.True(session.DoItemChange);

            using (session.EnterItemChange())
            {
                Assert.True(session.DoItemChange);
            }

            // A NESTED scope restores TRUE, not false - se_cst_dw.sru:L196.
            Assert.True(session.DoItemChange);
        }

        Assert.False(session.DoItemChange);
    }

    [Fact]
    public void DefaultItemChangeScope_RestoresNothing()
    {
        ValidationSession session = NewSession();
        session.DoItemChange = true;

        ValidationSession.ItemChangeScope scope = default;
        scope.Dispose();

        Assert.True(session.DoItemChange);
    }

    // ==============================================================================================
    //  THE POSTED CONTINUATION                       se_cst_dw.sru:L388-L390 and :L553-L557
    // ==============================================================================================

    [Fact]
    public void QueueDeferredAccept_OnlyWhenTheItemChangeFlagIsClear()
    {
        ValidationSession session = NewSession();

        session.DoItemChange = true;
        Assert.False(session.TryQueueDeferredAccept());
        Assert.False(session.DeferredAcceptPending);

        session.DoItemChange = false;
        Assert.True(session.TryQueueDeferredAccept());
        Assert.True(session.DeferredAcceptPending);
    }

    [Fact]
    public void DrainDeferredAccept_ReturnsNullWhenNothingIsQueued()
    {
        FakeDataWindowHost host = NewHost();
        ValidationSession session = NewSession();

        Assert.Null(session.DrainDeferredAccept(host));
    }

    [Fact]
    public void DrainDeferredAccept_SkipsTheBodyWhenFocusIsStillOnTheHost()
    {
        FakeDataWindowHost host = NewHost();
        host.FocusedObject = host;
        ValidationSession session = NewSession();
        Assert.True(session.TryQueueDeferredAccept());

        DeferredAcceptOutcome outcome = Assert.IsType<DeferredAcceptOutcome>(
            session.DrainDeferredAccept(host));

        Assert.False(outcome.FocusHadLeftHost);
        Assert.Null(outcome.AcceptTextResult);
        Assert.False(outcome.FocusRestored);
        Assert.Null(outcome.SetFocusResult);
        Assert.False(host.CallLog.Contains("AcceptText"));
        Assert.False(session.DeferredAcceptPending);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(1, false)]
    [InlineData(0, false)]
    [InlineData(-2, false)]
    public void DrainDeferredAccept_RestoresFocusOnlyOnMinusOne(int acceptTextResult, bool expectSetFocus)
    {
        FakeDataWindowHost host = NewHost();
        host.FocusedObject = null;
        host.AcceptTextResult = acceptTextResult;
        ValidationSession session = NewSession();
        Assert.True(session.TryQueueDeferredAccept());

        DeferredAcceptOutcome outcome = Assert.IsType<DeferredAcceptOutcome>(
            session.DrainDeferredAccept(host));

        Assert.True(outcome.FocusHadLeftHost);
        Assert.Equal(acceptTextResult, outcome.AcceptTextResult);
        Assert.Equal(expectSetFocus, outcome.FocusRestored);
        Assert.Equal(expectSetFocus, host.CallLog.Contains("SetFocus"));
        Assert.Equal(expectSetFocus ? host.SetFocusResult : null, outcome.SetFocusResult);
        Assert.Equal(ValidationSession.AcceptTextFailure, -1);
    }

    [Fact]
    public void DrainDeferredAccept_RunsAtMostOnce()
    {
        FakeDataWindowHost host = NewHost();
        host.FocusedObject = null;
        ValidationSession session = NewSession();
        Assert.True(session.TryQueueDeferredAccept());

        Assert.NotNull(session.DrainDeferredAccept(host));
        Assert.Null(session.DrainDeferredAccept(host));
        Assert.Equal(1, host.CallLog.CountOf("AcceptText"));
    }

    [Fact]
    public void DrainDeferredAccept_DrainsNothingOnAClosedSession()
    {
        FakeDataWindowHost host = NewHost();
        host.FocusedObject = null;
        ValidationSession session = NewSession();
        Assert.True(session.TryQueueDeferredAccept());
        Assert.True(session.Close());

        Assert.Null(session.DrainDeferredAccept(host));
        Assert.False(host.CallLog.Contains("AcceptText"));
    }

    // ==============================================================================================
    //  THE ONE-BASED HELPER                                              se_cst_dw.sru:L352
    // ==============================================================================================

    [Theory]
    [InlineData("'abc'", 2, 3, "abc")]
    [InlineData("abcde", 1, 5, "abcde")]
    [InlineData("abcde", 5, 1, "e")]
    [InlineData("abcde", 6, 1, "")]
    [InlineData("abcde", 0, 3, "")]
    [InlineData("abcde", -4, 3, "")]
    [InlineData("abcde", 2, 0, "")]
    [InlineData("abcde", 2, -1, "")]
    [InlineData("abcde", 3, 99, "cde")]
    [InlineData("", 1, 1, "")]
    public void MidOneBased_ReproducesPowerScriptMid(
        string value,
        int oneBasedStart,
        int length,
        string expected) =>
        Assert.Equal(expected, ValidationSession.MidOneBased(value, oneBasedStart, length));

    [Fact]
    public void MidOneBased_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => ValidationSession.MidOneBased(null!, 1, 1));

    // ==============================================================================================
    //  THE STRUCTURED-ERROR FACTORY AND ITS EQUALITY
    // ==============================================================================================

    [Fact]
    public void Create_SubstitutesThroughSprintfOnlyWhenArgumentsArePresent()
    {
        ValidationStructuredError substituted = ValidationStructuredError.Create(
            "Title",
            "value {1} rejected",
            DialogSeverity.Exclamation,
            Categories.CAT_MSGBOX,
            localized: false,
            ["42"]);

        Assert.Equal(Formatting.Sprintf("value {1} rejected", "42"), substituted.Text);
        Assert.Equal(["42"], substituted.FormatArguments);

        ValidationStructuredError verbatim = ValidationStructuredError.Create(
            "Title",
            "value {1} rejected",
            DialogSeverity.Exclamation,
            Categories.CAT_MSGBOX,
            localized: false,
            ImmutableArray<string>.Empty);

        Assert.Equal("value {1} rejected", verbatim.Text);
        Assert.Empty(verbatim.FormatArguments);
    }

    [Fact]
    public void Create_NormalisesADefaultArgumentArrayAndRejectsNulls()
    {
        ValidationStructuredError error = ValidationStructuredError.Create(
            "Title",
            "body",
            DialogSeverity.None,
            0L,
            localized: false,
            default,
            returnCode: RetCode.E_INVALID_DATA);

        Assert.Empty(error.FormatArguments);
        Assert.Equal(RetCode.E_INVALID_DATA, error.ReturnCode);

        Assert.Throws<ArgumentNullException>(() => ValidationStructuredError.Create(
            null!, "body", DialogSeverity.None, 0L, false, ImmutableArray<string>.Empty));
        Assert.Throws<ArgumentNullException>(() => ValidationStructuredError.Create(
            "Title", null!, DialogSeverity.None, 0L, false, ImmutableArray<string>.Empty));
    }

    [Fact]
    public void StructuredError_InequalityCoversEveryMember()
    {
        ValidationStructuredError baseline = ValidationStructuredError.Create(
            "T", "b", DialogSeverity.StopSign, 7L, true, ["x"], returnCode: 1L);

        Assert.NotEqual(
            baseline,
            ValidationStructuredError.Create("OTHER", "b", DialogSeverity.StopSign, 7L, true, ["x"], 1L));
        Assert.NotEqual(
            baseline,
            ValidationStructuredError.Create("T", "OTHER", DialogSeverity.StopSign, 7L, true, ["x"], 1L));
        Assert.NotEqual(
            baseline,
            ValidationStructuredError.Create("T", "b", DialogSeverity.StopSign, 7L, false, ["x"], 1L));
        Assert.NotEqual(
            baseline,
            ValidationStructuredError.Create("T", "b", DialogSeverity.StopSign, 8L, true, ["x"], 1L));
        Assert.NotEqual(
            baseline,
            ValidationStructuredError.Create("T", "b", DialogSeverity.None, 7L, true, ["x"], 1L));
        Assert.NotEqual(
            baseline,
            ValidationStructuredError.Create("T", "b", DialogSeverity.StopSign, 7L, true, ["x"], 2L));
        Assert.NotEqual(
            baseline,
            ValidationStructuredError.Create("T", "b", DialogSeverity.StopSign, 7L, true, ["x", "y"], 1L));
        Assert.NotEqual(
            baseline,
            ValidationStructuredError.Create(
                "T", "b", DialogSeverity.StopSign, 7L, true, ImmutableArray<string>.Empty, 1L));
        Assert.False(baseline.Equals((object?)"not an error"));
    }

    [Fact]
    public void Registry_ConcurrentOpensOfOneIdentifierYieldExactlyOneSession()
    {
        ValidationSessionRegistry registry = NewRegistry();
        int opened = 0;
        int refused = 0;

        Parallel.For(0, 64, _ =>
        {
            ValidationSessionOpenResult result = registry.OpenWithId("contended");

            if (result.IsOpened)
            {
                Interlocked.Increment(ref opened);
            }
            else
            {
                Assert.Equal(RetCode.FAILED, result.ReturnCode);
                Interlocked.Increment(ref refused);
            }
        });

        Assert.Equal(1, opened);
        Assert.Equal(63, refused);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void StructuredError_ComparesArgumentListsElementWise()
    {
        ValidationStructuredError a = ValidationStructuredError.Create(
            "T", "b", DialogSeverity.StopSign, 7L, true, ["x", "y"]);
        ValidationStructuredError b = ValidationStructuredError.Create(
            "T", "b", DialogSeverity.StopSign, 7L, true, ["x", "y"]);
        ValidationStructuredError c = ValidationStructuredError.Create(
            "T", "b", DialogSeverity.StopSign, 7L, true, ["x", "z"]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
        Assert.True(a.Equals(a));
        Assert.False(a.Equals(null));
    }

    // ==============================================================================================
    //  THE REGISTRY - IDENTITY, LIFETIME AND ISOLATION
    // ==============================================================================================

    private static ValidationSessionRegistry NewRegistry(
        TimeSpan? idleTimeout = null,
        int maxConcurrent = 100,
        TimeProvider? clock = null)
    {
        DataServicesOptions options = new();
        options.Sessions.ValidationSession.IdleTimeout = idleTimeout ?? TimeSpan.FromMinutes(5);
        options.Sessions.ValidationSession.MaxConcurrentSessions = maxConcurrent;

        return new ValidationSessionRegistry(options, i18n: null, timeProvider: clock);
    }

    [Fact]
    public void Registry_OpenYieldsAFreshSessionWithAnOpaqueIdentifier()
    {
        ValidationSessionRegistry registry = NewRegistry();

        ValidationSessionOpenResult opened = registry.Open("dw-1", EventGate.EID_ITEMCHANGE);

        Assert.True(opened.IsOpened);
        Assert.Equal(RetCode.OK, opened.ReturnCode);
        Assert.Equal(1, registry.Count);
        Assert.Equal(TimeSpan.FromMinutes(5), registry.IdleTimeout);
        Assert.Equal(100, registry.MaxConcurrentSessions);

        ValidationSession session = opened.Session!;
        Assert.Equal(ValidationSessionRegistry.GeneratedSessionIdLength, session.SessionId.Length);
        Assert.All(session.SessionId, c => Assert.Contains(c, "0123456789abcdef"));
        Assert.Equal("dw-1", session.DataWindowHandle);
        Assert.True(session.IsEventDisabled(EventGate.EID_ITEMCHANGE));
        Assert.False(session.DoItemChange);
        Assert.False(session.InItemValidationError);
        Assert.Equal(0L, session.ItemChangeRetCode);

        Assert.NotEqual(
            ValidationSessionRegistry.GenerateSessionId(),
            ValidationSessionRegistry.GenerateSessionId());
    }

    [Fact]
    public void Registry_RefusesABlankIdentifierADuplicateAndANonPositiveCeiling()
    {
        ValidationSessionRegistry registry = NewRegistry();

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, registry.OpenWithId("  ").ReturnCode);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, registry.OpenWithId("id", null!).ReturnCode);

        Assert.True(registry.OpenWithId("id-1").IsOpened);
        ValidationSessionOpenResult duplicate = registry.OpenWithId("id-1");
        Assert.False(duplicate.IsOpened);
        Assert.Equal(RetCode.FAILED, duplicate.ReturnCode);

        ValidationSessionRegistry refuses = NewRegistry(maxConcurrent: 0);
        Assert.Equal(RetCode.E_BUSY, refuses.Open().ReturnCode);
    }

    [Fact]
    public void Registry_RefusesOnceTheCeilingIsReachedAndReclaimsExpiredSessionsFirst()
    {
        ValidationSessionTestClock clock = new();
        ValidationSessionRegistry registry =
            NewRegistry(TimeSpan.FromMinutes(1), maxConcurrent: 1, clock: clock);

        Assert.True(registry.OpenWithId("first").IsOpened);
        Assert.Equal(RetCode.E_BUSY, registry.OpenWithId("second").ReturnCode);

        clock.Advance(TimeSpan.FromMinutes(2));

        // The sweep now reclaims the abandoned session and the open succeeds.
        Assert.True(registry.OpenWithId("third").IsOpened);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Registry_ResolveReportsADefinedErrorAndNeverCreatesASession()
    {
        ValidationSessionRegistry registry = NewRegistry();

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, registry.Resolve(null).ReturnCode);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, registry.Resolve("   ").ReturnCode);
        Assert.Equal(RetCode.E_INVALID_HANDLE, registry.Resolve("never-registered").ReturnCode);
        Assert.Equal(0, registry.Count);

        Assert.True(registry.OpenWithId("id-1").IsOpened);
        Assert.True(registry.Resolve("id-1").IsResolved);
        Assert.True(registry.TryGet("id-1", out ValidationSession? live));
        Assert.NotNull(live);

        registry.Close("id-1");
        Assert.Equal(RetCode.E_INVALID_HANDLE, registry.Resolve("id-1").ReturnCode);
        Assert.False(registry.TryGet("id-1", out ValidationSession? gone));
        Assert.Null(gone);
    }

    [Fact]
    public void Registry_ResolveReportsNotExistsForASessionClosedDirectly()
    {
        ValidationSessionRegistry registry = NewRegistry();
        ValidationSessionOpenResult opened = registry.OpenWithId("id-1");
        Assert.True(opened.Session!.Close());

        Assert.Equal(RetCode.E_NOT_EXISTS, registry.Resolve("id-1").ReturnCode);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Registry_CloseIsIdempotentAndCarriesTheFinalState()
    {
        ValidationSessionRegistry registry = NewRegistry();
        ValidationSessionOpenResult opened = registry.OpenWithId("id-1", "dw-1", EventGate.EID_ITEMCHANGE);
        ValidationSession session = opened.Session!;
        session.ItemChangeRetCode = 3L;
        Assert.True(session.TryQueueDeferredAccept());

        ValidationSessionCloseResult first = registry.Close("id-1");
        Assert.Equal(RetCode.OK, first.ReturnCode);
        Assert.True(first.WasOpen);
        Assert.Equal(3L, first.FinalState.RawItemChangeRetCode);
        Assert.Equal(ItemChangeResult.KeepValueNoFocusMove, first.FinalState.ItemChangeRetCode);
        Assert.True(first.FinalState.DeferredAcceptPending);
        Assert.Equal((long)EventGate.EID_ITEMCHANGE, first.FinalState.DisabledEventMask);
        Assert.False(session.IsOpen);

        ValidationSessionCloseResult second = registry.Close("id-1");
        Assert.Equal(RetCode.OK, second.ReturnCode);
        Assert.False(second.WasOpen);
        Assert.Equal(default, second.FinalState);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, registry.Close(null).ReturnCode);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Registry_ExpiryIsDrivenEntirelyByTheInjectedClock()
    {
        ValidationSessionTestClock clock = new();
        ValidationSessionRegistry registry =
            NewRegistry(TimeSpan.FromMinutes(5), clock: clock);
        ValidationSession session = registry.OpenWithId("id-1").Session!;

        Assert.False(session.HasExpired());

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.False(session.HasExpired());

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(session.HasExpired());

        // Touch resets the idle clock.
        session.Touch();
        Assert.False(session.HasExpired());
        Assert.Equal(clock.GetUtcNow(), session.LastAccessedAt);

        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(1, registry.SweepExpired());
        Assert.Equal(0, registry.Count);
        Assert.False(session.IsOpen);
        Assert.False(session.HasExpired());

        // A closed session is never touched, so its clock cannot be revived.
        DateTimeOffset lastAccessed = session.LastAccessedAt;
        clock.Advance(TimeSpan.FromMinutes(1));
        session.Touch();
        Assert.Equal(lastAccessed, session.LastAccessedAt);
    }

    [Fact]
    public void Registry_NonPositiveIdleTimeoutNeverExpires()
    {
        ValidationSessionTestClock clock = new();
        ValidationSessionRegistry registry = NewRegistry(TimeSpan.Zero, clock: clock);
        ValidationSession session = registry.OpenWithId("id-1").Session!;

        clock.Advance(TimeSpan.FromDays(30));

        Assert.False(session.HasExpired());
        Assert.Equal(0, registry.SweepExpired());
        Assert.Equal(0, registry.SweepExpired());
    }

    [Fact]
    public void Registry_CloseAllReleasesEverySession()
    {
        ValidationSessionRegistry registry = NewRegistry();
        Assert.True(registry.OpenWithId("a").IsOpened);
        Assert.True(registry.OpenWithId("b").IsOpened);
        Assert.True(registry.OpenWithId("c").IsOpened);

        Assert.Equal(3, registry.CloseAll());
        Assert.Equal(0, registry.Count);
        Assert.Equal(0, registry.CloseAll());
    }

    [Fact]
    public void Registry_BindsTheLifetimeFromTheSessionsGroupAndTheOptionsOverload()
    {
        DataServicesOptions options = new();
        options.Sessions.ValidationSession.IdleTimeout = TimeSpan.FromMinutes(9);
        options.Sessions.ValidationSession.MaxConcurrentSessions = 7;

        ValidationSessionRegistry registry = new(
            Microsoft.Extensions.Options.Options.Create(options));

        Assert.Equal(TimeSpan.FromMinutes(9), registry.IdleTimeout);
        Assert.Equal(7, registry.MaxConcurrentSessions);
        Assert.Equal(TimeSpan.FromMinutes(9), registry.OpenWithId("id-1").Session!.IdleTimeout);

        Assert.Throws<ArgumentNullException>(() => new ValidationSessionRegistry((DataServicesOptions)null!));
        Assert.Throws<ArgumentNullException>(
            () => new ValidationSessionRegistry(
                (Microsoft.Extensions.Options.IOptions<DataServicesOptions>)null!));
    }

    [Fact]
    public void ConstructionDefaults_AreExercisedWhenEveryOptionalArgumentIsOmitted()
    {
        // The session's own defaults: no lifetime, no I18n, no clock.
        ValidationSession bare = new("bare-id");

        Assert.Equal(new SessionLifetimeOptions().IdleTimeout, bare.IdleTimeout);
        Assert.Equal(string.Empty, bare.DataWindowHandle);
        Assert.True(bare.IsOpen);
        Assert.NotEqual(default, bare.CreatedAt);

        // A misconfigured options graph with no Sessions group at all.
        DataServicesOptions headless = new() { Sessions = null! };
        ValidationSessionRegistry fallback = new(headless, new I18n(), TimeProvider.System);

        Assert.Equal(new SessionLifetimeOptions().IdleTimeout, fallback.IdleTimeout);
        Assert.Equal(new SessionLifetimeOptions().MaxConcurrentSessions, fallback.MaxConcurrentSessions);
        Assert.True(fallback.Open().IsOpened);

        // An IOptions implementation whose Value is null.
        ValidationSessionRegistry fromNullValue = new(new NullValuedDataServicesOptions());

        Assert.Equal(new SessionLifetimeOptions().IdleTimeout, fromNullValue.IdleTimeout);
        Assert.True(fromNullValue.Open().IsOpened);
    }

    private sealed class NullValuedDataServicesOptions : Microsoft.Extensions.Options.IOptions<DataServicesOptions>
    {
        public DataServicesOptions Value => null!;
    }

    [Fact]
    public void Session_RejectsABlankIdentifierAndANullHandle()
    {
        Assert.Throws<ArgumentException>(() => new ValidationSession("  "));
        Assert.Throws<ArgumentNullException>(() => new ValidationSession("id", null!));
    }

    // ==============================================================================================
    //  SNAPSHOT AGREEMENT WITH dataservices.v1.proto - THE ONLY GUARD, BECAUSE THE CONTRACTS
    //  PROJECT DECLARES NO ProjectReference BY DESIGN.
    // ==============================================================================================

    [Fact]
    public void Snapshot_AgreesFieldForFieldWithTheWireValidationSessionState()
    {
        ValidationSession session = NewSession(mask: EventGate.EID_ITEMFOCUSCHANGE);
        session.DoItemChange = true;
        session.ItemChangeRetCode = 2L;
        Assert.False(session.TryQueueDeferredAccept());

        ValidationSessionSnapshot snapshot = session.CaptureState();

        ValidationSessionState wire = new()
        {
            DisabledEventMask = snapshot.DisabledEventMask,
            InItemChange = snapshot.InItemChange,
            InItemValidationError = snapshot.InItemValidationError,
            ItemChangeRetCode = (WireItemChangeResult)snapshot.ItemChangeRetCode,
            DeferredAcceptPending = snapshot.DeferredAcceptPending,
        };

        Assert.Equal(2L, wire.DisabledEventMask);
        Assert.True(wire.InItemChange);
        Assert.False(wire.InItemValidationError);
        Assert.Equal(WireItemChangeResult.RestoreAndRejectText, wire.ItemChangeRetCode);
        Assert.False(wire.DeferredAcceptPending);
        Assert.Equal(2L, snapshot.RawItemChangeRetCode);
    }

    [Fact]
    public void ItemChangeAlphabet_MatchesTheWireEnumNameForNameAndValueForValue()
    {
        (ItemChangeResult Domain, WireItemChangeResult Wire)[] pairs =
        [
            (ItemChangeResult.Default, WireItemChangeResult.Default),
            (ItemChangeResult.TriggerValidationError, WireItemChangeResult.TriggerValidationError),
            (ItemChangeResult.RestoreAndRejectText, WireItemChangeResult.RestoreAndRejectText),
            (ItemChangeResult.KeepValueNoFocusMove, WireItemChangeResult.KeepValueNoFocusMove),
        ];

        foreach ((ItemChangeResult domain, WireItemChangeResult wire) in pairs)
        {
            Assert.Equal((long)domain, (long)wire);
            Assert.Equal(domain.ToString(), wire.ToString());
        }

        Assert.Equal(4, pairs.Length);
    }

    [Fact]
    public void DialogSeverity_MatchesTheWireSeverityNameForNameAndValueForValue()
    {
        (DialogSeverity Domain, Severity Wire)[] pairs =
        [
            (DialogSeverity.Unspecified, Severity.Unspecified),
            (DialogSeverity.None, Severity.None),
            (DialogSeverity.Information, Severity.Information),
            (DialogSeverity.Question, Severity.Question),
            (DialogSeverity.Exclamation, Severity.Exclamation),
            (DialogSeverity.StopSign, Severity.StopSign),
        ];

        foreach ((DialogSeverity domain, Severity wire) in pairs)
        {
            Assert.Equal((int)domain, (int)wire);
            Assert.Equal(domain.ToString(), wire.ToString());
        }

        Assert.Equal(6, pairs.Length);
    }

    [Fact]
    public void StructuredError_ProjectsOntoTheWireStructuredErrorFieldForField()
    {
        FakeDataWindowHost host = NewHost("original");
        host.SetValidationMessage(Column, "'Bad value'");
        ValidationSession session = NewSession();

        ValidationStructuredError error = session
            .OnDwnItemValidationError(host, 1L, host.DwObject(Column), "bad")
            .Error!;

        StructuredError wire = new()
        {
            Text = error.Text,
            Localized = error.Localized,
            Category = error.LocalizationCategory,
            Severity = (Severity)error.Severity,
            Title = error.Title,
        };
        wire.FormatArgs.AddRange(error.FormatArguments);

        Assert.Equal("Bad value", wire.Text);
        Assert.True(wire.Localized);
        Assert.Equal(Categories.CAT_DWSVC, wire.Category);
        Assert.Equal(Severity.StopSign, wire.Severity);
        Assert.Equal(ValidationStructuredError.LegacyTitleSource, wire.Title);
        Assert.Empty(wire.FormatArgs);
    }
}
