// ==================================================================================================
//  ValidationSession - THE FOUR PIECES OF CROSS-EVENT STATE, AND THE PROTOCOL THAT CONSUMES THEM
//  ------------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru  (616 lines)
//                   :L88-L96    the four private instance fields this file owns
//                   :L192-L196  the four-step re-entrancy dance around the item-change handler
//                   :L322-L385  `ondwnitemvalidationerror`, the stash's consumer
//                   :L387-L390  `ondwnkillfocus`, which QUEUES the deferred accept
//                   :L537-L558  `_of_postaccepttext`, the queued continuation's body
//                 ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru
//                   :L17        CAT_DWSVC, the category the two localized strings use
//                 ws_objects/pfw.ui.pbl.src/i18n.srf
//                   :L17-L18    the silent-passthrough translation facade
//                 ws_objects/pfw.shared.pbl.src/retcode.sru
//                   the return-code algebra, used ONLY for the boundary-created lifecycle results
//
//  ORACLE STATUS  Every file above is READ ONLY and is the behavioural oracle for parity testing,
//                 never an edit target (constraint C-C). It is also the ONLY specification: nothing
//                 else in the tree can adjudicate a behavioural question here, which is why every
//                 claim below carries the ws_objects/** locator it came from (AAP 0.1.4).
//
//                 se_cst_dw.sru declares ZERO PBNI native bindings - `grep -cE 'native |external
//                 function'` over it returns 0 - so this is a pure logic port with no native
//                 substitution problem anywhere in it.
//
//  WHY THIS FILE EXISTS
//  ------------------------------------------------------------------------------------------------
//  se_cst_dw.sru:L88-L96 declares four private fields that the event chain reads and writes BETWEEN
//  events, verbatim and with the oracle's own comments:
//
//      //禁用的事件                                                    "the disabled events"
//      long _nDisabledEvent                                                              [:L88-L89]
//
//      //标志当前正在调用OnDoItemChange                 "currently inside OnDoItemChange"
//      boolean _bDoItemChange                                                            [:L91-L92]
//      //标志当前正在调用OnDwnItemValidationError       "currently inside OnDwnItemValidationError"
//      boolean _bDwnItemValidationError                                                  [:L93-L94]
//      //当ItemError事件由ItemChanged触发时使用的ItemChanged返回值
//                       "the ItemChanged return value used when ItemError was triggered BY
//                        ItemChanged"
//      long _nItemChangeRetCode                                                          [:L95-L96]
//
//  In the oracle these are instance fields of one visual control, so their lifetime is the control's
//  and no identifier is needed to find them. A STATELESS REQUEST BOUNDARY HAS NOWHERE TO PUT THEM
//  (AAP 0.6.1.3), so they become fields of a server-held session correlated by an identifier and
//  opened and closed by dedicated calls - which is why
//  shared/PowerFramework.Contracts/Proto/dataservices.v1.proto already declares
//  OpenValidationSession and CloseValidationSession on contract C-03 and already carries
//  ValidationSessionState as a message.
//
//  THE FOURTH FIELD IS WHY THE CHAIN IS STRICTLY SYNCHRONOUS. `ondwnitemvalidationerror` does not
//  merely read the stash - it READS AND CLEARS it [:L331-L332] and then PRE-SETS ITS OWN RESULT from
//  it [:L338-L340], and later SUPPRESSES ITS OWN RESTORE when the stashed value was 3 [:L369]. One
//  event's behaviour is therefore a function of the previous event's return value, so AAP 0.6.1.4
//  assigns this capability area ordering pattern (b): strictly synchronous, NO REORDERING PERMITTED.
//  The monotonic sequence numbers the stream carries exist FOR DETECTION ONLY - an out-of-order
//  arrival is a hard error and never a reorder opportunity.
//
//  WHAT THIS FILE OWNS, AND WHAT IT DELIBERATELY DOES NOT
//  ------------------------------------------------------------------------------------------------
//    * It OWNS the four field VALUES, the validation-error protocol at :L322-L385, and the queued
//      continuation that replaces the oracle's `Post` at :L389.
//    * It does NOT own the event-gate OPERATIONS. Domain/EventGate.cs owns those, and every read and
//      write of the mask here routes through EventGate.IsEventDisabled, EventGate.DisableEvent and
//      EventGate.EnableEvent. The gate owns the operations; the session owns the value.
//    * It does NOT own the item-change dispatch. Domain/ItemChangeProtocol.cs owns :L182-L253 and
//      reaches this session's three item-change fields through IItemChangeSessionState, which that
//      file declares because it is the consumer.
//    * It does NOT reach into Expressions/, Services/, Grpc/ or Endpoints/. Those consume this file;
//      the dependency runs one way only.
//
//  DECISION LOG                                                                      (constraint C-K)
//  ------------------------------------------------------------------------------------------------
//  D-1  THE STASH IS STORED AS `long`, NOT AS ItemChangeResult - and this deliberately differs from
//       one line of this file's brief. IItemChangeSessionState.ItemChangeRetCode is declared `long`
//       by its consumer, Domain/ItemChangeProtocol.cs, for a reason recorded there: the oracle
//       assigns the semantic handler's return value to the field UNCLASSIFIED at :L195, before the
//       `choose case` has looked at it, so a handler returning -1 or 42 stashes -1 or 42. Those
//       values are indistinguishable from 0 as far as the DISPATCH is concerned, but they are not
//       indistinguishable in the stash, which :L338 and :L369 compare against 1 and 3 NUMERICALLY.
//       Narrowing the storage to the enum would discard information the oracle keeps. The typed view
//       the brief asks for is published as StashedItemChangeResult, computed through
//       ItemChangeProtocol.Classify, so both requirements are met without losing a value.
//  D-2  THE MASK IS `uint`, MATCHING Domain/EventGate.cs EXACTLY. PowerBuilder `ulong` is 32-bit, so
//       the bit primitives backing the gate are declared over C# `uint`; widening here would fail to
//       compile against EventGate.IsEventDisabled. The wire carries the mask as int64 on contract
//       C-03, and ValidationSessionSnapshot.DisabledEventMask performs that one widening at the
//       reporting edge rather than in the field.
//  D-3  EVERY TYPE HERE IS `internal`, AND THAT IS FORCED RATHER THAN CHOSEN.
//       ValidationSessionSnapshot surfaces Domain/ItemChangeProtocol.cs's `internal enum
//       ItemChangeResult`, and a public signature over an internal type is CS0053. It also matches
//       the two sibling Domain files and the intent recorded in
//       PowerFramework.DataServices.csproj, whose InternalsVisibleTo item exists precisely so the
//       session state can be driven from the test project without being made public (constraint
//       C-H).
//  D-4  THE DIALOG AT :L357 BECOMES A STRUCTURED ERROR, AND ONLY THE DELIVERY CHANNEL CHANGES
//       (AAP 0.3.4, 0.2.1.3 Correction 5). ValidationStructuredError carries the exact text, the
//       localization category, the Sprintf substitution arguments and the severity the oracle passes
//       - title, body and StopSign! are three fields, not one. NO rendering, positioning, DPI
//       conversion, font measurement or Win32 call appears anywhere in this file: dialog
//       PRESENTATION is DesignSystem's deferred half, reserved under /v1/design/** (constraint C-D).
//  D-5  THERE IS NO CARET POSITION HERE. Caret-bearing errors are parse errors and belong to
//       Expressions/ParseErrorFormatter.cs. Inventing a caret field on this path would publish a
//       coordinate the oracle never produces.
//  D-6  THE WIN32 MESSAGE PUMP IS A DELIBERATE NON-PORT (AAP 0.6.5). :L389 reads
//       `Post _of_PostAcceptText()`, which hands the call to the Win32 message queue so that it runs
//       after the current event returns; a headless Linux container has no message pump. It becomes
//       an explicitly queued continuation owned by this session and DRAINED BY THE HOST - never by a
//       timer, a background task or a thread-pool work item - which is the same discipline
//       Shared.Eventful's EventBroker.Post follows. The re-check at :L553 is why the deferral cannot
//       simply be run inline: it is deliberately observed AFTER the event completes.
//  D-7  SESSION LIFETIME AND EXPIRY ARE NET-NEW OBLIGATIONS CREATED BY THE BOUNDARY. The oracle's
//       fields die with the control, so there is no legacy number to reproduce and no legacy
//       behaviour to preserve. Both values therefore come from configuration
//       (DataServices:Sessions:ValidationSession) rather than from a literal (constraint C-F), and
//       every clock read goes through an injected TimeProvider so a characterization test can
//       substitute a deterministic double (AAP 0.6.7).
//  D-8  THE STORE IS PER-INSTANCE AND IN-MEMORY. No distributed cache, no database, no shared
//       backing store and no storage provider of any kind (constraints C-E and C-J). A session is
//       meaningful only within the correlated stream that opened it.
//  D-9  THE CORRELATION IDENTIFIER IS AN OPAQUE HANDLE AND IS NOT A CREDENTIAL (constraint C-G).
//       Possession of one is never treated as proof of identity; authentication is enforced at the
//       Grpc/ and Endpoints/ layer by the stock JwtBearer handler. Identifiers are drawn from
//       System.Security.Cryptography.RandomNumberGenerator so they are neither guessable nor
//       enumerable, and none is ever derived from caller-supplied data.
//  D-10 Shared.Diagnostics IS DELIBERATELY NOT IMPORTED. Assertions.AssertFailed is annotated
//       [DoesNotReturn] and is not conditional on a debug build, so it terminates in Release.
//       Asserting on this path would convert ordinary legacy data outcomes - a null handler return,
//       an empty validation message, a row deleted under a dialog - into process faults the oracle
//       never raises, which constraint C-B forbids. The fail-fast posture AAP 0.6.7 requires belongs
//       to startup validation, which Configuration/DataServicesOptions.cs already performs.
//  D-11 LOCATOR CORRECTION, RELIED ON THROUGHOUT AND INDEPENDENTLY VERIFIED HERE. AAP 0.1.4 and
//       0.2.1.3 Correction 5 both cite the I18N(CAT_DWSVC, ...) sites in this object as ":L357,
//       :L368". Measured: `grep -n 'I18N('` over se_cst_dw.sru returns EXACTLY :L355 and :L357.
//       `grep -n 'MessageBox'` returns :L286, :L357 and :L368 - of which :L286 sits inside the
//       commented-out block spanning :L280-L290 and :L368 is a comment ABOUT MessageBox's posted
//       message rather than a call. :L357 is therefore the ONLY live dialog in all 616 lines, and
//       the correct pair is :L355 / :L357. Shared.Localization's Categories.cs cites the same pair.
//  D-12 A NULL FROM THE LOCALIZATION FACADE FALLS BACK TO THE UNTRANSLATED SOURCE. I18n.I18N is
//       declared `string?` and returns its argument unchanged when no provider is installed, so it
//       cannot answer null for a non-null argument through any shipped provider - all three assign
//       only a non-empty lookup result to the `ref` parameter and otherwise leave it alone
//       [n_cst_i18n_en.sru, n_cst_i18n_chs.sru, n_cst_i18n_cht.sru]. A THIRD-PARTY provider could
//       null it, and the oracle would then propagate that null into its dialog, because PowerScript
//       `null + "!"` is null. That outcome is not representable on a non-nullable text field, so the
//       port substitutes THE UNTRANSLATED SOURCE STRING - the nearest observable equivalent, and
//       exactly what the no-provider case already yields - rather than an empty string, a marker or
//       an exception. Two coalesces exist for this, one for the fallback body and one for the title,
//       and neither is reachable through any provider in this repository.
//
//  ONE-BASED INDEXING AUDIT                                                          (AAP 0.4.5.4)
//  ------------------------------------------------------------------------------------------------
//  AAP 0.4.5.4 names one-based translation "the single most dangerous mechanical hazard in this
//  refactor". Every index-bearing expression in this file is listed, with its resolution:
//
//    1. `row` is a ONE-BASED DataWindow row number on every path and is NEVER used as a .NET index.
//       It is passed through to host members that are themselves declared over one-based rows, and
//       compared against host.RowCount(), which IS the last valid row number [:L369].
//    2. `Mid(sErrMsg,2,Len(sErrMsg) - 2)` at :L352 is one-based, so its .NET equivalent starts at
//       offset 1, not 2. It is routed through MidOneBased below rather than written inline, so the
//       conversion exists in exactly one place and is tested directly.
//    3. `Long(dwo.ID)` is a COLUMN identifier, not an index, and null is a real state for it that is
//       never defaulted to zero - column zero is a real column position, so a default would address
//       the wrong cell silently. DataWindowObjectExtensions.ColumnId returns long? and every
//       addressing site below is guarded on HasValue, reproducing PowerScript's own outcome:
//       `Long(null)` is null, and every addressing call would receive that null and write nothing.
//    4. There is no array iteration in this file at all, and therefore no upper-bound idiom and no
//       reverse traversal to get backwards.
//
//  NULL SEMANTICS                                                                    (AAP 0.4.5.4)
//  ------------------------------------------------------------------------------------------------
//  Null is never collapsed to zero implicitly. It is collapsed EXACTLY ONCE, explicitly, at :L344,
//  because the oracle does it there - `if IsNull(rtCode) then rtCode = 0` - and the coercion is
//  reported on the outcome so a reader can tell "the handler returned zero" from "no handler ran".
//  The restore test at :L372 additionally depends on null being a distinguishable state: both-null
//  counts as EQUAL, and one-sided null does not.
//
//  NO PERFORMANCE OBJECTIVE IS ASSERTED ANYWHERE IN THIS FILE                          (AAP 0.8.5)
//  ------------------------------------------------------------------------------------------------
//  The repository publishes no service-level agreement, no latency budget, no throughput target and
//  no availability commitment. Every structure below is chosen for correctness and testability, and
//  the two bounds that exist - an idle timeout and a concurrent-session ceiling - are admission and
//  reclamation policy, never a claim about anything this service achieves.
// ==================================================================================================

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;

using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
using ItemStatus = PowerFramework.Contracts.Common.V1.ItemStatus;

namespace PowerFramework.DataServices.Domain;

/// <summary>
/// The PowerBuilder <c>Icon</c> domain, as the legacy dialogs use it - the in-process peer of
/// <c>enum Severity</c> in <c>shared/PowerFramework.Contracts/Proto/dataservices.v1.proto</c>.
/// </summary>
/// <remarks>
/// <para>
/// CARRIED BECAUSE THE SEVERITY IS OBSERVABLE OUTPUT. A structured error replaces a dialog, and the
/// dialog's icon is part of what the legacy showed: <c>se_cst_dw.sru:L357</c> raises its message with
/// <c>StopSign!</c>, and preserving that is the difference between reproducing the legacy's error
/// ergonomics and merely reporting that something failed.
/// </para>
/// <para>
/// DECLARED HERE RATHER THAN REUSED, AND THE DUPLICATION IS FORCED BY THE DEPENDENCY DIRECTION.
/// <c>Expressions/ParseErrorFormatter.cs</c> declares an identical five-value peer for the
/// column-expression engine's own errors, but <c>Expressions/</c> depends on <c>Domain/</c> and not
/// the other way round, so this namespace cannot reach that declaration. Both project onto the single
/// wire enum at the gRPC boundary, and the member values are identical on purpose so that projection
/// is a cast rather than a lookup.
/// </para>
/// <para>
/// <see cref="Unspecified"/> exists because the wire enum needs a zero member, not because the
/// legacy has an unspecified icon. No path in this file produces it.
/// </para>
/// </remarks>
internal enum DialogSeverity
{
    /// <summary>No icon was stated. Present so the wire enum is a legal proto3 enum.</summary>
    Unspecified = 0,

    /// <summary><c>None!</c> - a dialog with no icon.</summary>
    None = 1,

    /// <summary><c>Information!</c></summary>
    Information = 2,

    /// <summary><c>Question!</c></summary>
    Question = 3,

    /// <summary><c>Exclamation!</c></summary>
    Exclamation = 4,

    /// <summary>
    /// <c>StopSign!</c> - the severity <c>se_cst_dw.sru:L357</c> passes, and the only value any path
    /// in this file produces.
    /// </summary>
    StopSign = 5,
}

/// <summary>
/// A legacy dialog, re-expressed as a machine-readable result - the port of
/// <c>MessageBox(I18N(ne_cst_i18n.CAT_DWSVC,"错误"),sErrMsg,StopSign!)</c>
/// [<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L357</c>].
/// </summary>
/// <remarks>
/// <para>
/// ONLY THE DELIVERY CHANNEL CHANGES (AAP 0.3.4, 0.2.1.3 Correction 5). The text, the localization
/// category, the substitution arguments and the severity are all preserved, and the title is
/// preserved as its own field because the oracle localizes it separately from the body on the same
/// line. Field for field this mirrors <c>dataservices.v1.StructuredError</c>, so the gRPC layer
/// projects it without deciding anything.
/// </para>
/// <para>
/// THERE IS NO CARET POSITION, DELIBERATELY (see decision D-5 in this file's header). The caret is a
/// property of an expression parse failure, and those belong to
/// <c>Expressions/ParseErrorFormatter.cs</c>.
/// </para>
/// <para>
/// <see cref="Localized"/> IS ALWAYS <see langword="true"/> ON THIS PATH, AND THE FLAG STILL EXISTS.
/// It reports a legacy inconsistency rather than resolving one: the two messages on the
/// validation-error path DO route through the localization category
/// [<c>:L355</c>, <c>:L357</c>], while all 28 in the column-expression engine do NOT. A consumer can
/// tell translatable text from untranslatable text without any message changing, and routing the
/// untranslated ones through localization would be exactly the silent correction constraint C-B
/// forbids.
/// </para>
/// </remarks>
internal sealed record ValidationStructuredError
{
    /// <summary>
    /// The untranslated dialog TITLE exactly as <c>se_cst_dw.sru:L357</c> spells it. Held as a
    /// constant so the lookup key travels by identifier rather than being retyped at a call site,
    /// where a single character's difference would silently defeat the lookup and fall through to
    /// the localization layer's silent passthrough.
    /// </summary>
    internal const string LegacyTitleSource = "错误";

    /// <summary>
    /// The untranslated FALLBACK BODY exactly as <c>se_cst_dw.sru:L355</c> spells it, used when the
    /// column's own validation message is empty or is the DataWindow's <c>"?"</c> placeholder.
    /// </summary>
    internal const string LegacyFallbackTextSource = "输入了无效的值";

    /// <summary>
    /// The <c>"!"</c> the oracle CONCATENATES ONTO THE TRANSLATED fallback at <c>:L355</c> - never
    /// onto the column's own message, and never before translation. The order matters: translate,
    /// then append.
    /// </summary>
    internal const string LegacyFallbackSuffix = "!";

    /// <summary>
    /// The DataWindow's own placeholder for "this column has no validation message", tested at
    /// <c>se_cst_dw.sru:L354</c> alongside the empty string.
    /// </summary>
    internal const string NoValidationMessagePlaceholder = "?";

    /// <summary>
    /// The line in the oracle this error stands in for. Reported rather than inferred so a
    /// characterization recording can be traced back to the statement that produced it.
    /// </summary>
    internal const int LegacyLine = 357;

    /// <summary>
    /// The dialog title - <c>I18N(CAT_DWSVC,"错误")</c> at <c>se_cst_dw.sru:L357</c>, AFTER
    /// translation.
    /// </summary>
    internal required string Title { get; init; }

    /// <summary>
    /// The message body as the legacy would have shown it: AFTER any translation the legacy itself
    /// performed and AFTER substitution. Observable output, reproduced verbatim.
    /// </summary>
    internal required string Text { get; init; }

    /// <summary>
    /// Whether <see cref="Text"/> was produced through the localization layer. See the type remarks:
    /// this REPORTS a legacy inconsistency, it does not resolve one.
    /// </summary>
    internal required bool Localized { get; init; }

    /// <summary>
    /// The localization category, carried as its numeric value.
    /// <see cref="Categories.CAT_DWSVC"/> is the only value this file produces, and it is a COMPUTED
    /// OFFSET - <c>Enums.I18N_CAT_CUSTOM + 2</c> [<c>ne_cst_i18n.sru:L17</c>] - never the literal it
    /// evaluates to.
    /// </summary>
    internal required long LocalizationCategory { get; init; }

    /// <summary>
    /// The substitution arguments, in order, BEFORE substitution - carried alongside the substituted
    /// <see cref="Text"/> so a consumer can re-render the message in another locale or another format
    /// without parsing the rendered string back apart.
    /// </summary>
    /// <remarks>
    /// EMPTY ON THE ONE LIVE PATH IN THIS FILE, AND THAT IS THE ORACLE'S DOING. Neither
    /// <c>:L355</c> nor <c>:L357</c> substitutes anything: the fallback is a translated literal with
    /// <c>"!"</c> concatenated onto it, and the title is a translated literal. The field is populated
    /// - and <see cref="Formatting.Sprintf"/> is applied - only when a caller supplies arguments.
    /// </remarks>
    internal required ImmutableArray<string> FormatArguments { get; init; }

    /// <summary>The dialog icon. <see cref="DialogSeverity.StopSign"/> on the one live path.</summary>
    internal required DialogSeverity Severity { get; init; }

    /// <summary>
    /// The framework return code the operation produced, when it produced one - <see langword="null"/>
    /// on this path, because <c>:L357</c> is a dialog and not an operation, and the code the event
    /// ultimately returns belongs to the item-change alphabet rather than to the return-code algebra.
    /// </summary>
    /// <remarks>
    /// A consumer must branch on the specific value and never on a two-way success test:
    /// <see cref="RetCode"/> documents the tri-state hole in full - <c>PREVENT</c> reads as a success,
    /// and <c>CANCELLED</c> and null are NEITHER succeeded nor failed.
    /// </remarks>
    internal long? ReturnCode { get; init; }

    /// <summary>
    /// Builds a structured error, applying <see cref="Formatting.Sprintf"/> to
    /// <paramref name="template"/> ONLY when <paramref name="formatArguments"/> is non-empty.
    /// </summary>
    /// <param name="title">The dialog title, already translated.</param>
    /// <param name="template">
    /// The message body. When there are no arguments this is the final text and is used VERBATIM.
    /// </param>
    /// <param name="severity">The dialog icon.</param>
    /// <param name="localizationCategory">The category the text was translated under.</param>
    /// <param name="localized">Whether the text came through the localization layer.</param>
    /// <param name="formatArguments">The substitution arguments, in order, before substitution.</param>
    /// <param name="returnCode">The framework return code, when the operation produced one.</param>
    /// <returns>The structured error.</returns>
    /// <remarks>
    /// <para>
    /// THE EMPTY-ARGUMENT SHORT CIRCUIT IS A PARITY REQUIREMENT, NOT AN OPTIMISATION. Sprintf's
    /// grammar is <c>{</c>index<c>,</c>alignment<c>:</c>mask<c>}</c>, so a brace in the message text
    /// is meaningful to it. The oracle performs no substitution at <c>:L355</c> or <c>:L357</c>, so
    /// running the text through Sprintf there could reinterpret a brace that a translation
    /// legitimately contains and change observable output - which constraint C-B forbids. Passing the
    /// text through untouched when there is nothing to substitute is what the oracle does.
    /// </para>
    /// <para>
    /// Both branches are reachable and both are tested: the validation-error path supplies no
    /// arguments, and a caller that supplies them gets the substituted rendering.
    /// </para>
    /// </remarks>
    internal static ValidationStructuredError Create(
        string title,
        string template,
        DialogSeverity severity,
        long localizationCategory,
        bool localized,
        ImmutableArray<string> formatArguments,
        long? returnCode = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(template);

        ImmutableArray<string> arguments =
            formatArguments.IsDefault ? ImmutableArray<string>.Empty : formatArguments;

        string text = arguments.IsEmpty
            ? template
            : Formatting.Sprintf(template, [.. arguments]);

        return new ValidationStructuredError
        {
            Title = title,
            Text = text,
            Localized = localized,
            LocalizationCategory = localizationCategory,
            FormatArguments = arguments,
            Severity = severity,
            ReturnCode = returnCode,
        };
    }

    /// <summary>
    /// Compares by VALUE, including an element-wise comparison of
    /// <see cref="FormatArguments"/>.
    /// </summary>
    /// <param name="other">The error to compare against.</param>
    /// <returns><see langword="true"/> when every member is equal.</returns>
    /// <remarks>
    /// The compiler-generated equality would compare <see cref="ImmutableArray{T}"/> through
    /// <see cref="EqualityComparer{T}.Default"/>, which compares the UNDERLYING ARRAY REFERENCE - so
    /// two errors carrying identical argument lists would report unequal. A characterization
    /// comparison is exactly the case that would break on, so equality is written out.
    /// </remarks>
    public bool Equals(ValidationStructuredError? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return string.Equals(Title, other.Title, StringComparison.Ordinal)
            && string.Equals(Text, other.Text, StringComparison.Ordinal)
            && Localized == other.Localized
            && LocalizationCategory == other.LocalizationCategory
            && Severity == other.Severity
            && ReturnCode == other.ReturnCode
            && FormatArguments.AsSpan().SequenceEqual(other.FormatArguments.AsSpan());
    }

    /// <summary>Hashes the same members <see cref="Equals(ValidationStructuredError)"/> compares.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        HashCode hash = default;

        hash.Add(Title, StringComparer.Ordinal);
        hash.Add(Text, StringComparer.Ordinal);
        hash.Add(Localized);
        hash.Add(LocalizationCategory);
        hash.Add(Severity);
        hash.Add(ReturnCode);

        foreach (string argument in FormatArguments)
        {
            hash.Add(argument, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// The four cross-event fields, materialized - the in-process peer of
/// <c>message ValidationSessionState</c> in
/// <c>shared/PowerFramework.Contracts/Proto/dataservices.v1.proto</c>.
/// </summary>
/// <param name="DisabledEventMask">
/// <c>_nDisabledEvent</c> [<c>se_cst_dw.sru:L88-L89</c>], WIDENED TO <see langword="long"/> FOR THE
/// WIRE. The field itself is <see langword="uint"/> because PowerBuilder <c>ulong</c> is 32-bit and
/// <see cref="EventGate"/> is declared over that width; this is the single place the widening happens,
/// and it happens at the reporting edge rather than in the field (decision D-2).
/// </param>
/// <param name="InItemChange">
/// <c>_bDoItemChange</c> [<c>:L91-L92</c>] - execution is currently inside the semantic item-change
/// handler. Observable because it GATES THE DEFERRED ACCEPT on kill-focus [<c>:L387-L390</c>].
/// </param>
/// <param name="InItemValidationError">
/// <c>_bDwnItemValidationError</c> [<c>:L93-L94</c>] - execution is currently inside the
/// validation-error handler. Observable because it makes that handler RETURN 1 IMMEDIATELY on re-entry
/// [<c>:L327</c>].
/// </param>
/// <param name="ItemChangeRetCode">
/// <c>_nItemChangeRetCode</c> [<c>:L95-L96</c>] CLASSIFIED - the stash, reported through the
/// item-change alphabet because that is what it holds.
/// </param>
/// <param name="RawItemChangeRetCode">
/// The same stash UNCLASSIFIED. Carried alongside the typed view because the oracle assigns the
/// handler's return value to the field before any dispatch has looked at it [<c>:L195</c>], so a
/// handler returning <c>-1</c> or <c>42</c> stashes that number; classification alone would report all
/// of them as <see cref="ItemChangeResult.Default"/> and lose the distinction (decision D-1).
/// </param>
/// <param name="DeferredAcceptPending">
/// Whether a deferred accept-text continuation is outstanding on this session. NET-NEW: the oracle
/// achieves the deferral with <c>Post</c> [<c>:L389</c>], and a posted message has no representation,
/// whereas a queued continuation has to be observable or a consumer cannot tell that work is still
/// outstanding (decision D-6).
/// </param>
/// <remarks>
/// A SNAPSHOT, NOT A VIEW. It is taken by value at one instant so that a consumer can observe the
/// intermediate state the NEXT event will consume - which is the whole point of carrying it on every
/// synchronous event result rather than making it a separate call.
/// </remarks>
internal readonly record struct ValidationSessionSnapshot(
    long DisabledEventMask,
    bool InItemChange,
    bool InItemValidationError,
    ItemChangeResult ItemChangeRetCode,
    long RawItemChangeRetCode,
    bool DeferredAcceptPending);

/// <summary>
/// What one invocation of the validation-error protocol did - the reportable product of
/// <c>ondwnitemvalidationerror</c> [<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L322-L385</c>].
/// </summary>
/// <remarks>
/// <para>
/// EVERY BRANCH OF THAT ROUTINE IS CONTRACT, so every branch is reported. The oracle returns a single
/// number from three different places - <c>1</c> from the re-entrancy guard at <c>:L327</c>, <c>3</c>
/// from the empty-data arm at <c>:L362</c>, and the accumulated result at <c>:L384</c> - and those
/// three are behaviourally different despite two of them being able to produce the same digit. A
/// consumer that saw only the number could not tell a re-entrant no-op from a real result, which is
/// why <see cref="ReEntered"/> exists.
/// </para>
/// <para>
/// THE DEFAULTS ARE THE ORACLE'S OWN. Where a member is not set on a path, its default is the value
/// the oracle leaves behind: PowerScript numeric locals default to zero and its string locals to the
/// empty string, so <see cref="ValidationMessage"/> is empty when no message was read and
/// <see cref="Error"/> is null when no dialog was raised.
/// </para>
/// </remarks>
internal sealed record ValidationErrorOutcome
{
    /// <summary>
    /// Whether the re-entrancy guard at <c>se_cst_dw.sru:L327</c> fired - <see langword="true"/> when
    /// the handler was already executing and returned <c>1</c> IMMEDIATELY.
    /// </summary>
    /// <remarks>
    /// ON THIS PATH NOTHING ELSE HAPPENS. The oracle's <c>return 1</c> precedes the flag assignment,
    /// the consume-and-clear, both snapshots, the handler raise and the restore - and it does NOT
    /// clear the flag, because the outer invocation that set it is still running and will clear it at
    /// <c>:L382</c>. Every other member of this record therefore holds its default on this path, and
    /// that is the assertion a parity test makes.
    /// </remarks>
    internal required bool ReEntered { get; init; }

    /// <summary>
    /// The number the event returned, verbatim - <c>1</c> from <c>:L327</c>, <c>3</c> from
    /// <c>:L362</c>, or the accumulated <c>rtCode</c> from <c>:L384</c>.
    /// </summary>
    /// <remarks>
    /// A characterization recording compares THIS, because it is what the legacy handler produced.
    /// <see cref="Result"/> is how a consumer should reason; this is what a recording compares.
    /// </remarks>
    internal required long RawResult { get; init; }

    /// <summary>
    /// <see cref="RawResult"/> classified through the item-change alphabet.
    /// </summary>
    /// <remarks>
    /// COMPUTED, NEVER STORED, so the two can never disagree. Classification is total: a value outside
    /// <c>{1, 2, 3}</c> reports as <see cref="ItemChangeResult.Default"/>, which is exactly what the
    /// oracle's <c>case else</c> does with it.
    /// </remarks>
    internal ItemChangeResult Result => ItemChangeProtocol.Classify(RawResult);

    /// <summary>
    /// The stash as it stood when this event fired, BEFORE <c>:L332</c> cleared it, unclassified.
    /// </summary>
    /// <remarks>
    /// Carried because <c>:L338</c> and <c>:L369</c> both read it and after <c>:L332</c> it is gone -
    /// a consumer that wanted to know what drove the pre-set could not ask afterwards. Zero on the
    /// re-entrant path, where the oracle never reaches the read.
    /// </remarks>
    internal long StashedRawItemChangeRetCode { get; init; }

    /// <summary>
    /// <see cref="StashedRawItemChangeRetCode"/> classified. Computed for the same reason
    /// <see cref="Result"/> is.
    /// </summary>
    internal ItemChangeResult StashedItemChangeResult =>
        ItemChangeProtocol.Classify(StashedRawItemChangeRetCode);

    /// <summary>
    /// Whether the result was PRE-SET to <c>1</c> from the stash at <c>se_cst_dw.sru:L338-L340</c>,
    /// because the stashed code was <c>1</c> or <c>3</c>.
    /// </summary>
    /// <remarks>
    /// This is the mechanism by which this handler's behaviour is a function of the PREVIOUS event's
    /// return value, and therefore the reason AAP 0.6.1.4 assigns this chain strictly synchronous
    /// ordering with no reordering permitted.
    /// </remarks>
    internal bool PreSetFromStash { get; init; }

    /// <summary>
    /// Whether the semantic <c>ItemError</c> event was raised [<c>:L343</c>]. False when the pre-set at
    /// <c>:L338-L340</c> had already moved the result off zero, because <c>:L342</c> guards the raise
    /// on the result still being zero.
    /// </summary>
    internal bool ItemErrorRaised { get; init; }

    /// <summary>
    /// Whether that handler returned <see langword="null"/> and was therefore coerced to zero at
    /// <c>:L344</c>.
    /// </summary>
    /// <remarks>
    /// REPORTED BECAUSE THE COERCION MERGES TWO DISTINCT INPUTS. "The handler returned zero" and "no
    /// handler ran" are the same outcome downstream, but only because <c>:L344</c> makes them the same;
    /// this flag keeps the distinction visible without changing the outcome. The coercion is the
    /// oracle's, not a defensive habit - AAP 0.4.5.4 forbids collapsing null to zero IMPLICITLY, and
    /// this is the one place the legacy does it explicitly.
    /// </remarks>
    internal bool ItemErrorReturnedNull { get; init; }

    /// <summary>
    /// Whether the EMPTY-DATA arm at <c>se_cst_dw.sru:L359-L363</c> was taken - the path that clears
    /// the guard itself and returns <c>3</c>, bypassing the tail entirely.
    /// </summary>
    /// <remarks>
    /// The oracle's comment at <c>:L360</c> states the intent: refuse to record an empty value and
    /// re-check at save time instead. When this is <see langword="true"/>,
    /// <see cref="ValueRestored"/> is necessarily <see langword="false"/> and
    /// <see cref="RowStillExists"/> is necessarily null, because the early return means the restore
    /// block never runs.
    /// </remarks>
    internal bool EmptyData { get; init; }

    /// <summary>
    /// The column's validation message AFTER the outer-two-character strip of <c>:L351-L353</c> and
    /// AFTER the empty-or-<c>"?"</c> fallback of <c>:L354-L356</c>.
    /// </summary>
    /// <remarks>
    /// BOTH TRANSFORMATIONS ARE OBSERVABLE OUTPUT, so this carries the FINAL text rather than the raw
    /// property. Empty on every path that never reached <c>:L350</c>.
    /// </remarks>
    internal string ValidationMessage { get; init; } = string.Empty;

    /// <summary>
    /// Whether the localized fallback of <c>:L354-L356</c> replaced the column's own message, because
    /// that message was empty or was the DataWindow's <c>"?"</c> placeholder.
    /// </summary>
    internal bool ValidationMessageFellBack { get; init; }

    /// <summary>
    /// The structured error standing in for the dialog at <c>:L357</c>, or <see langword="null"/> when
    /// no dialog was raised.
    /// </summary>
    internal ValidationStructuredError? Error { get; init; }

    /// <summary>
    /// Whether the value-and-status restore at <c>se_cst_dw.sru:L375-L376</c> actually executed.
    /// </summary>
    /// <remarks>
    /// It requires FOUR conditions together: the result is <c>1</c> or <c>3</c> [<c>:L367</c>], the row
    /// still exists [<c>:L369</c>], the stashed code was NOT <c>3</c> [<c>:L369</c>], and the buffer
    /// value has not moved [<c>:L372</c>]. A fifth is imposed by the port rather than the oracle and
    /// reaches the same outcome: the column identifier must be resolvable, because
    /// <c>Long(dwo.ID)</c> being null makes PowerScript's own addressing calls write nothing.
    /// </remarks>
    internal bool ValueRestored { get; init; }

    /// <summary>
    /// The outcome of the <c>row &lt;= RowCount()</c> test at <c>:L369</c>, or <see langword="null"/>
    /// when the tail block was never reached.
    /// </summary>
    /// <remarks>
    /// THE TEST IS DEFENSIVE AND MUST SURVIVE. The oracle's comment at <c>:L368</c> explains it: the
    /// dialog's own posted message may have DELETED THE ROW while the dialog was up. Dropping the test
    /// would write to a row that no longer exists. It is reported because a parity test has to be able
    /// to assert that the guard was evaluated and refused, rather than merely that no restore
    /// happened.
    /// </remarks>
    internal bool? RowStillExists { get; init; }

    /// <summary>
    /// The value snapshot taken at <c>se_cst_dw.sru:L334</c>, before anything ran - the value the
    /// restore writes back.
    /// </summary>
    /// <remarks>
    /// <c>any</c> maps to <c>object?</c> (AAP 0.4.5.2), and a null cell is PRESERVED AS NULL because
    /// the equality test at <c>:L372</c> has an arm that depends on it.
    /// </remarks>
    internal object? OriginalValue { get; init; }

    /// <summary>
    /// The item status snapshot taken at <c>:L335</c>, at the same instant as the value, because the
    /// restore writes both back together [<c>:L375-L376</c>].
    /// </summary>
    /// <remarks>
    /// Restoring a value without its status would leave the row claiming a modification state it no
    /// longer has. <see cref="ItemStatus.NotModified"/> when the column identifier could not be
    /// resolved, which is what PowerBuilder yields for an item it cannot address.
    /// </remarks>
    internal ItemStatus OriginalStatus { get; init; }

    /// <summary>
    /// <c>Long(dwo.ID)</c> resolved once, or <see langword="null"/> when the identifier itself is null.
    /// </summary>
    /// <remarks>
    /// NULL IS A REAL STATE AND IS NEVER DEFAULTED TO ZERO: column zero is a real column position, so
    /// a default would address the wrong cell silently (AAP 0.4.5.4).
    /// </remarks>
    internal long? ColumnId { get; init; }

    /// <summary>
    /// The session state as it stood when this invocation returned.
    /// </summary>
    /// <remarks>
    /// Carried because the stash and both guards are MUTATED BY this event, and a consumer that had to
    /// re-read them with a separate call could not observe the intermediate value the next event will
    /// consume.
    /// </remarks>
    internal ValidationSessionSnapshot State { get; init; }
}

/// <summary>
/// What draining the deferred accept-text continuation did - the reportable product of
/// <c>_of_postaccepttext</c>
/// [<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L537-L558</c>].
/// </summary>
/// <remarks>
/// The oracle's subroutine returns nothing, so every member here is boundary-created reporting rather
/// than a ported value. It exists because the continuation runs AFTER the event that queued it has
/// already answered, so its effects would otherwise be unobservable to the caller that queued it
/// (decision D-6).
/// </remarks>
internal sealed record DeferredAcceptOutcome
{
    /// <summary>
    /// The outcome of <c>if GetFocus() &lt;&gt; this then</c> at <c>se_cst_dw.sru:L553</c> -
    /// <see langword="true"/> when focus had genuinely left the host.
    /// </summary>
    /// <remarks>
    /// AN IDENTITY COMPARISON AGAINST THE HOST, NOT A FOCUS TEST, which is why
    /// <c>DataWindowServiceHost.GetFocusedObject</c> returns <c>object?</c> rather than a boolean.
    /// When this is <see langword="false"/> the whole body is skipped, which is the case the re-check
    /// exists to catch: focus came back before the continuation ran.
    /// </remarks>
    internal required bool FocusHadLeftHost { get; init; }

    /// <summary>
    /// What <c>AcceptText()</c> returned at <c>:L554</c>, or <see langword="null"/> when it was never
    /// attempted because focus had not left the host.
    /// </summary>
    internal int? AcceptTextResult { get; init; }

    /// <summary>
    /// Whether <c>SetFocus()</c> ran at <c>:L555</c> - which happens ON THE FAILURE CODE
    /// <c>-1</c> ALONE.
    /// </summary>
    /// <remarks>
    /// The oracle tests <c>AcceptText() = -1</c>, not "not 1" and not "less than or equal to zero", so
    /// any other value - including <c>0</c> and including an unexpected value - leaves focus where it
    /// is. Broadening the test would restore focus in cases the legacy does not.
    /// </remarks>
    internal bool FocusRestored { get; init; }

    /// <summary>
    /// What <c>SetFocus()</c> returned, or <see langword="null"/> when it was never called. The oracle
    /// discards this value; it is reported so a test can assert the call happened rather than inferring
    /// it.
    /// </summary>
    internal int? SetFocusResult { get; init; }
}

/// <summary>
/// The four pieces of cross-event state
/// [<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L88-L96</c>], held for one correlated
/// stream, together with the validation-error protocol that consumes them
/// [<c>:L322-L385</c>] and the queued continuation that replaces the oracle's <c>Post</c>
/// [<c>:L387-L390</c>, <c>:L537-L558</c>].
/// </summary>
/// <remarks>
/// <para>
/// SCOPED TO ONE DATAWINDOW. In the oracle all four fields are instance fields of one control, so
/// sharing a session across DataWindows would make the stash readable by the wrong chain - which is
/// why <c>OpenValidationSessionRequest</c> carries a DataWindow handle.
/// </para>
/// <para>
/// THE FOUR LEGACY FIELDS ARE UNSYNCHRONISED, DELIBERATELY. The item-change and validation-error group
/// is assigned strictly synchronous ordering (AAP 0.6.1.4), so exactly one event at a time touches
/// them, and the oracle itself takes no lock. Two of them are written and read back within a single
/// invocation and one is written for a LATER event to consume, so an implementation that serialised,
/// cached, clamped or validated on the way through would break the protocol rather than harden it.
/// The three BOUNDARY-CREATED fields - last-accessed time, open state and the pending-continuation
/// flag - are guarded, because the registry's expiry sweep can reach them from another thread and none
/// of them has a legacy counterpart whose semantics a lock could disturb.
/// </para>
/// <para>
/// THE TWO BOOLEANS ARE RE-ENTRANCY FLAGS, NOT LOCKS. Neither is a mutex, a semaphore or a
/// <see cref="Lock"/>. <c>_bDoItemChange</c> is SAVED, SET AND RESTORED around the handler call
/// [<c>:L192-L196</c>], so a nested invocation restores <see langword="true"/> rather than
/// <see langword="false"/>; <see cref="EnterItemChange"/> is that dance, written once.
/// </para>
/// </remarks>
internal sealed class ValidationSession : IItemChangeSessionState
{
    // ----------------------------------------------------------------------------------------------
    //  THE FOUR LEGACY FIELDS                                            se_cst_dw.sru:L88-L96
    //  --------------------------------------------------------------------------------------------
    //  Named idiomatically, with the legacy identifier on each so a reader can move between this file
    //  and the oracle without a lookup table. Plain field storage, in the oracle's own declaration
    //  order.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>long _nDisabledEvent</c> [<c>se_cst_dw.sru:L88-L89</c>] - the composable mask of suppressed
    /// events. THE SESSION OWNS THE VALUE; <see cref="EventGate"/> owns the operations.
    /// </summary>
    private uint _disabledEvent;

    /// <summary>
    /// <c>boolean _bDoItemChange</c> [<c>:L91-L92</c>] - execution is currently inside
    /// <c>OnDoItemChange</c>.
    /// </summary>
    private bool _doItemChange;

    /// <summary>
    /// <c>boolean _bDwnItemValidationError</c> [<c>:L93-L94</c>] - execution is currently inside
    /// <c>OnDwnItemValidationError</c>. Cleared at TWO SEPARATE SITES that must not be consolidated:
    /// <c>:L361</c> on the empty-data path and <c>:L382</c> on the normal path.
    /// </summary>
    private bool _inItemValidationError;

    /// <summary>
    /// <c>long _nItemChangeRetCode</c> [<c>:L95-L96</c>] - THE STASH, held UNCLASSIFIED for the
    /// validation-error event to read and clear (decision D-1).
    /// </summary>
    private long _itemChangeRetCode;

    // ----------------------------------------------------------------------------------------------
    //  THE BOUNDARY-CREATED FIELDS                                                  NO LEGACY SOURCE
    //  --------------------------------------------------------------------------------------------
    //  None of these exists in the oracle, and each is here because the network boundary created an
    //  obligation the oracle never had (decisions D-6, D-7 and D-8).
    // ----------------------------------------------------------------------------------------------

    /// <summary>Guards the three boundary-created mutable fields below, and nothing else.</summary>
    private readonly Lock _gate = new();

    private readonly TimeProvider _timeProvider;

    private readonly I18n _i18n;

    private DateTimeOffset _lastAccessedAt;

    private bool _isOpen = true;

    private bool _deferredAcceptPending;

    /// <summary>
    /// Creates an open session with all four legacy fields at the values a freshly constructed control
    /// would hold.
    /// </summary>
    /// <param name="sessionId">
    /// The correlation identifier. An OPAQUE HANDLE and never a credential (decision D-9): this type
    /// never treats possession of one as proof of identity, and authentication is enforced at the
    /// <c>Grpc/</c> and <c>Endpoints/</c> layer by the stock bearer handler.
    /// </param>
    /// <param name="dataWindowHandle">
    /// The DataWindow this session is bound to, from <c>OpenValidationSessionRequest</c>. The empty
    /// string is accepted and means the caller did not name one; a session is still scoped to whatever
    /// stream opened it.
    /// </param>
    /// <param name="initialDisabledEventMask">
    /// The initial gate mask. ZERO DISABLES NOTHING, matching a freshly constructed control, and it is
    /// the default for exactly that reason.
    /// </param>
    /// <param name="lifetime">
    /// The configured idle timeout, from <c>DataServices:Sessions:ValidationSession</c>. Never a
    /// literal (constraint C-F). When omitted the options type's own defaults apply, which is what
    /// makes this constructor usable from a test without a configuration root.
    /// </param>
    /// <param name="i18n">
    /// The localization facade, INJECTED AS AN INSTANCE and never read from a static mutable slot
    /// (AAP 0.4.5.1). When omitted a facade with NO PROVIDER INSTALLED is used, which is not a stub -
    /// it is precisely the state the oracle's own <c>if IsValid(n_cst_i18n)</c> guard describes
    /// [<c>i18n.srf:L17</c>], and it yields the SILENT PASSTHROUGH the legacy yields: the text comes
    /// back unchanged, nothing is thrown, nothing is logged and nothing is marked untranslated.
    /// </param>
    /// <param name="timeProvider">
    /// The clock seam. Every clock read in this type goes through it so a characterization test can
    /// substitute a deterministic double, because non-deterministic values must be masked from BOTH the
    /// master and the candidate recording (AAP 0.6.7).
    /// </param>
    internal ValidationSession(
        string sessionId,
        string dataWindowHandle = "",
        uint initialDisabledEventMask = 0u,
        SessionLifetimeOptions? lifetime = null,
        I18n? i18n = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(dataWindowHandle);

        SessionLifetimeOptions effectiveLifetime = lifetime ?? new SessionLifetimeOptions();

        SessionId = sessionId;
        DataWindowHandle = dataWindowHandle;
        IdleTimeout = effectiveLifetime.IdleTimeout;

        // The mask is the ONE legacy field a caller may seed, because the oracle's own
        // of_disableevent [:L110] can be called before any event fires. The other three start at the
        // values PowerScript gives a fresh instance: false, false and zero.
        _disabledEvent = initialDisabledEventMask;

        _i18n = i18n ?? new I18n();
        _timeProvider = timeProvider ?? TimeProvider.System;

        CreatedAt = _timeProvider.GetUtcNow();
        _lastAccessedAt = CreatedAt;
    }

    /// <summary>The correlation identifier. Opaque, and not a credential (decision D-9).</summary>
    internal string SessionId { get; }

    /// <summary>The DataWindow this session is bound to, or the empty string when none was named.</summary>
    internal string DataWindowHandle { get; }

    /// <summary>
    /// The configured idle timeout. A non-positive value means this session never expires, which
    /// matches the sibling expression session and is unreachable through configuration because startup
    /// validation refuses a non-positive value.
    /// </summary>
    internal TimeSpan IdleTimeout { get; }

    /// <summary>When the session was opened, read through the injected clock.</summary>
    internal DateTimeOffset CreatedAt { get; }

    /// <summary>The last time <see cref="Touch"/> recorded activity, read through the injected clock.</summary>
    internal DateTimeOffset LastAccessedAt
    {
        get
        {
            lock (_gate)
            {
                return _lastAccessedAt;
            }
        }
    }

    /// <summary>Whether the session is still open. Closing is terminal and idempotent.</summary>
    internal bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _isOpen;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// READ-ONLY THROUGH THE INTERFACE, DELIBERATELY. The item-change protocol only ever TESTS the
    /// mask [<c>se_cst_dw.sru:L187</c>]; the two mutators are <see cref="DisableEvent"/> and
    /// <see cref="EnableEvent"/> on this type, which are driven by <c>of_disableevent</c>
    /// [<c>:L110</c>] and <c>of_enableevent</c> [<c>:L111</c>]. A setter here would invite the
    /// protocol to gate its own events, which the oracle never does.
    /// </remarks>
    public uint DisabledEvent => _disabledEvent;

    /// <inheritdoc />
    public bool DoItemChange
    {
        get => _doItemChange;
        set => _doItemChange = value;
    }

    /// <inheritdoc />
    public long ItemChangeRetCode
    {
        get => _itemChangeRetCode;
        set => _itemChangeRetCode = value;
    }

    /// <summary>
    /// The stash CLASSIFIED - the typed view of <see cref="ItemChangeRetCode"/>.
    /// </summary>
    /// <remarks>
    /// Computed rather than stored so the two can never disagree, and published alongside the raw value
    /// rather than instead of it (decision D-1). Note that <c>3</c> survives here: the item-change
    /// dispatch rewrites its own RESULT from <c>3</c> to <c>1</c> at <c>:L225</c>, but the stash was
    /// written at <c>:L195</c> with the PRE-REWRITE value, which is what lets <c>:L338</c> and
    /// <c>:L369</c> still distinguish the two.
    /// </remarks>
    internal ItemChangeResult StashedItemChangeResult => ItemChangeProtocol.Classify(_itemChangeRetCode);

    /// <summary>
    /// <c>boolean _bDwnItemValidationError</c> [<c>se_cst_dw.sru:L93-L94</c>] - whether execution is
    /// currently inside the validation-error handler.
    /// </summary>
    /// <remarks>
    /// NO PUBLIC SETTER, AND THAT IS THE POINT. The flag is owned end to end by
    /// <see cref="OnDwnItemValidationError"/>, which is the only routine in the oracle that reads or
    /// writes it. Its re-entrant branch is therefore reachable only the way the oracle reaches it -
    /// from inside a handler raised by an invocation already in flight - which is exactly how it is
    /// tested.
    /// </remarks>
    internal bool InItemValidationError => _inItemValidationError;

    /// <summary>
    /// Whether a deferred accept-text continuation is outstanding. NET-NEW (decision D-6).
    /// </summary>
    internal bool DeferredAcceptPending
    {
        get
        {
            lock (_gate)
            {
                return _deferredAcceptPending;
            }
        }
    }

    /// <summary>
    /// Tests one event against the gate - the port of <c>of_iseventdisabled</c>
    /// [<c>se_cst_dw.sru:L469-L487</c>, body <c>:L486</c>].
    /// </summary>
    /// <param name="evt">
    /// A bitwise combination of <see cref="EventGate.EID_ROWFOCUSCHANGE"/>,
    /// <see cref="EventGate.EID_ITEMFOCUSCHANGE"/> and <see cref="EventGate.EID_ITEMCHANGE"/>.
    /// </param>
    /// <returns><see langword="true"/> when the event is DISABLED.</returns>
    /// <remarks>
    /// Routed through <see cref="EventGate"/> rather than testing the field here, because the gate owns
    /// the operations and the session owns the value. No guard precedes the test in the oracle either.
    /// </remarks>
    internal bool IsEventDisabled(uint evt) => EventGate.IsEventDisabled(_disabledEvent, evt);

    /// <summary>
    /// Disables one or more events - the port of <c>of_disableevent</c>
    /// [<c>se_cst_dw.sru:L489-L511</c>].
    /// </summary>
    /// <param name="evt">The bits to set. Zero is refused.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/>, or <see cref="RetCode.E_INVALID_ARGUMENT"/> for a zero argument
    /// [<c>:L506</c>].
    /// </returns>
    /// <remarks>
    /// The mask is passed BY REFERENCE so the rejection path leaves it provably untouched, exactly as
    /// the oracle's early return does.
    /// </remarks>
    internal long DisableEvent(uint evt) => EventGate.DisableEvent(ref _disabledEvent, evt);

    /// <summary>
    /// Enables one or more events - the port of <c>of_enableevent</c>
    /// [<c>se_cst_dw.sru:L513-L535</c>].
    /// </summary>
    /// <param name="evt">The bits to clear. Zero is refused.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/>, or <see cref="RetCode.E_INVALID_ARGUMENT"/> for a zero argument
    /// [<c>:L530</c>].
    /// </returns>
    /// <remarks>
    /// Returns <see langword="int"/> and not <see langword="long"/>, because the oracle declares
    /// <c>of_enableevent</c> as <c>integer</c> while declaring its mirror image
    /// <c>of_disableevent</c> as <c>long</c>. The asymmetry is the oracle's and is carried rather than
    /// tidied.
    /// </remarks>
    internal int EnableEvent(uint evt) => EventGate.EnableEvent(ref _disabledEvent, evt);

    /// <summary>
    /// Records activity, so an actively used session does not expire.
    /// </summary>
    /// <remarks>
    /// NET-NEW (decision D-7). A closed session is never touched, so a close followed by a stray touch
    /// cannot resurrect its expiry clock.
    /// </remarks>
    internal void Touch()
    {
        lock (_gate)
        {
            if (_isOpen)
            {
                _lastAccessedAt = _timeProvider.GetUtcNow();
            }
        }
    }

    /// <summary>
    /// Whether the session has been idle for longer than <see cref="IdleTimeout"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the session is open and has expired. A closed session reports
    /// <see langword="false"/>, because it has already been released and cannot expire twice.
    /// </returns>
    /// <remarks>
    /// NET-NEW (decision D-7), and driven ENTIRELY BY THE INJECTED CLOCK so expiry is assertable
    /// deterministically with no real waiting.
    /// </remarks>
    internal bool HasExpired()
    {
        if (IdleTimeout <= TimeSpan.Zero)
        {
            return false;
        }

        lock (_gate)
        {
            return _isOpen && _timeProvider.GetUtcNow() - _lastAccessedAt > IdleTimeout;
        }
    }

    /// <summary>
    /// Releases the session's state deterministically.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when this call closed an open session, <see langword="false"/> when it
    /// was already closed. IDEMPOTENT: a caller that cannot safely retry a close would leak sessions on
    /// any transport hiccup, and <c>CloseValidationSessionResponse.was_open</c> exists to report which
    /// it was WITHOUT changing the return code.
    /// </returns>
    /// <remarks>
    /// <para>
    /// NET-NEW (decision D-7). In the oracle the four fields simply die with the control; a server-held
    /// session needs a defined end.
    /// </para>
    /// <para>
    /// THE FOUR LEGACY FIELDS ARE NOT RESET HERE. Closing reports the state at the moment of closure -
    /// <c>CloseValidationSessionResponse.final_state</c> carries it precisely so a caller can observe an
    /// outstanding continuation or a still-set re-entrancy guard rather than discarding that fact.
    /// Zeroing them would destroy the evidence the response exists to deliver. What closure releases is
    /// the SESSION, and the registry drops its only reference immediately afterwards.
    /// </para>
    /// </remarks>
    internal bool Close()
    {
        lock (_gate)
        {
            if (!_isOpen)
            {
                return false;
            }

            _isOpen = false;

            return true;
        }
    }

    /// <summary>Takes a snapshot of all five reportable state fields at one instant.</summary>
    /// <returns>The snapshot.</returns>
    internal ValidationSessionSnapshot CaptureState()
    {
        bool pending;

        lock (_gate)
        {
            pending = _deferredAcceptPending;
        }

        return new ValidationSessionSnapshot(
            // The one widening from the field's uint to the wire's int64 (decision D-2).
            DisabledEventMask: _disabledEvent,
            InItemChange: _doItemChange,
            InItemValidationError: _inItemValidationError,
            ItemChangeRetCode: ItemChangeProtocol.Classify(_itemChangeRetCode),
            RawItemChangeRetCode: _itemChangeRetCode,
            DeferredAcceptPending: pending);
    }

    /// <summary>
    /// Enters the item-change handler, reproducing the SAVE-SET-RESTORE dance of
    /// <c>se_cst_dw.sru:L192-L196</c>. Disposing the returned scope performs the RESTORE.
    /// </summary>
    /// <returns>A scope that restores the SAVED value of the flag when disposed.</returns>
    /// <remarks>
    /// <para>
    /// THE RESTORE IS NOT A CLEAR, AND THAT DISTINCTION IS LOAD BEARING. <c>:L192</c> saves the current
    /// value into a local, <c>:L193</c> sets it, and <c>:L196</c> assigns THE SAVED VALUE back. The
    /// handler raised in between may itself cause a nested item change, so writing
    /// <see langword="false"/> at <c>:L196</c> would clear an outer invocation's flag while that outer
    /// invocation was still running. Encapsulating the dance is what makes writing
    /// <see langword="false"/> impossible at a call site.
    /// </para>
    /// <para>
    /// The flag ESCAPES the item-change routine: <c>ondwnkillfocus</c> queues its deferred accept only
    /// when the flag is clear [<c>:L387-L390</c>], so leaving it set would suppress that continuation
    /// for the remainder of the session.
    /// </para>
    /// <para>
    /// <c>Domain/ItemChangeProtocol.cs</c> performs the same four steps itself through
    /// <see cref="IItemChangeSessionState.DoItemChange"/>, because it also has to place the STASH
    /// between the set and the restore [<c>:L195</c>]. This scope serves the other call sites that
    /// enter the handler without stashing.
    /// </para>
    /// </remarks>
    internal ItemChangeScope EnterItemChange()
    {
        // :L192  bDoItemChange = _bDoItemChange   -- save
        ItemChangeScope scope = new(this, _doItemChange);

        // :L193  _bDoItemChange = true            -- set
        _doItemChange = true;

        return scope;
    }

    /// <summary>
    /// The re-entrancy scope <see cref="EnterItemChange"/> returns - the port of the save and restore
    /// halves of <c>se_cst_dw.sru:L192-L196</c>.
    /// </summary>
    /// <remarks>
    /// A <see langword="struct"/> so entering the handler allocates nothing, and
    /// <see langword="readonly"/> so the saved value cannot be altered before it is restored. It is NOT
    /// a lock of any kind: disposing it restores a boolean, it does not release anything.
    /// </remarks>
    internal readonly struct ItemChangeScope : IDisposable
    {
        private readonly ValidationSession? _session;

        private readonly bool _saved;

        internal ItemChangeScope(ValidationSession session, bool saved)
        {
            _session = session;
            _saved = saved;
        }

        /// <summary>
        /// Performs <c>:L196  _bDoItemChange = bDoItemChange</c> - the RESTORE, never a clear.
        /// </summary>
        /// <remarks>
        /// A default-constructed scope holds no session and restores nothing, so a scope that was never
        /// obtained from <see cref="EnterItemChange"/> cannot write to a session it does not have.
        /// Written as a null-conditional assignment - a C# 14 form - so the guard and the restore are
        /// one statement and cannot drift apart.
        /// </remarks>
        public void Dispose() => _session?._doItemChange = _saved;
    }

    // ==============================================================================================
    //  THE VALIDATION-ERROR PROTOCOL                                     se_cst_dw.sru:L322-L385
    //  --------------------------------------------------------------------------------------------
    //  The eight observable steps, in the oracle's own order. Every one of them is load bearing and
    //  none may be reordered, merged or "simplified":
    //
    //    1. RE-ENTRANCY: return 1 IMMEDIATELY if already inside, without clearing        [:L327]
    //    2. Set the guard                                                               [:L329]
    //    3. CONSUME AND CLEAR the stash - both steps, in that order                     [:L331-L332]
    //    4. Snapshot the value and the item status                                      [:L334-L335]
    //    5. PRE-SET the result to 1 when the stashed code was 1 or 3                     [:L338-L340]
    //    6. Otherwise raise ItemError, coercing a NULL RETURN TO 0                       [:L342-L345]
    //    7. On a zero result: with NON-EMPTY data read ValidationMsg, STRIP THE OUTER TWO
    //       CHARACTERS, fall back to a localized string when the message is empty or a single
    //       question mark, produce a stop-sign error and set the result to 1             [:L348-L358]
    //       - with EMPTY data, CLEAR THE GUARD AND RETURN 3, bypassing the tail          [:L359-L363]
    //    8. Restore value and status for results 1 and 3 only when the row STILL EXISTS and the
    //       stashed code was NOT 3, and only if the value has not moved                 [:L366-L380]
    //       then clear the guard [:L382] and return the result [:L384]
    // ==============================================================================================

    /// <summary>
    /// Runs the validation-error protocol - the port of <c>event ondwnitemvalidationerror</c>
    /// [<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L322-L385</c>].
    /// </summary>
    /// <param name="host">The DataWindow the event fired on.</param>
    /// <param name="row">The ONE-BASED row whose entry was rejected.</param>
    /// <param name="dwo">The column whose entry was rejected.</param>
    /// <param name="data">
    /// The offending edit text. EMPTINESS IS A BRANCH, NOT AN EDGE CASE: empty data takes step 7's
    /// second arm and yields <c>3</c>, non-empty data takes the first and yields <c>1</c>.
    /// </param>
    /// <returns>What the invocation did, including the number the oracle would have returned.</returns>
    /// <remarks>
    /// <para>
    /// STRICTLY SYNCHRONOUS BY ASSIGNMENT, AND THE REASON IS IN THIS ROUTINE. It reads and clears the
    /// code stashed by the preceding item-change event and pre-sets its own result from it, so
    /// reordering the group is not merely undesirable - it is semantically impossible. Sequence numbers
    /// on this group are for DETECTION ONLY (AAP 0.6.1.4).
    /// </para>
    /// <para>
    /// THE DIALOG BECOMES A STRUCTURED ERROR AND NOTHING ELSE CHANGES (decision D-4). The result is
    /// reported on <see cref="ValidationErrorOutcome.Error"/> with the exact text, the localization
    /// category, the substitution arguments and the severity; no rendering, positioning or measurement
    /// happens here or anywhere in this file.
    /// </para>
    /// </remarks>
    internal ValidationErrorOutcome OnDwnItemValidationError(
        DataWindowServiceHost host,
        long row,
        IDataWindowObject dwo,
        string data)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(dwo);

        // The oracle's `data` arrives from the raw pbm_dwnitemvalidationerror event, where PowerBuilder
        // supplies a string rather than a null. Rejecting null matches Domain/ItemChangeProtocol.cs and
        // keeps the emptiness test at :L348 a genuine two-way branch rather than a three-way one.
        ArgumentNullException.ThrowIfNull(data);

        // ------------------------------------------------------------------------------------------
        // :L327  if _bDwnItemValidationError then return 1
        //
        // FIRST, AND NOTHING ELSE RUNS. No guard is set, the stash is NOT consumed, no snapshot is
        // taken, ItemError is NOT raised and no restore is attempted - and critically the flag is NOT
        // CLEARED, because the outer invocation that set it is still executing and will clear it at
        // :L382. A port that cleared here would let the outer invocation's own tail run against a
        // flag it no longer owns.
        // ------------------------------------------------------------------------------------------
        if (_inItemValidationError)
        {
            return new ValidationErrorOutcome
            {
                ReEntered = true,
                RawResult = (long)ItemChangeResult.TriggerValidationError,
                State = CaptureState(),
            };
        }

        // :L329  _bDwnItemValidationError = true
        _inItemValidationError = true;

        // ------------------------------------------------------------------------------------------
        // :L331  nItemChangeRetCode = _nItemChangeRetCode
        // :L332  _nItemChangeRetCode = 0
        //
        // CONSUME AND CLEAR, BOTH STEPS, IN THIS ORDER. The clear is what makes the stash SINGLE USE:
        // a second validation-error event with no intervening item change reads zero and therefore
        // takes the ItemError path rather than the pre-set path. The value is read into a local
        // because every later test - :L338 and :L369 - must see the CONSUMED value and not the
        // cleared field.
        // ------------------------------------------------------------------------------------------
        long nItemChangeRetCode = _itemChangeRetCode;
        _itemChangeRetCode = 0L;

        // `Long(dwo.ID)` - RESOLVED ONCE, exactly as Domain/ItemChangeProtocol.cs resolves it. The
        // oracle re-evaluates it at :L335, :L375 and :L376; `dwo` is a handle to one DataWindow object
        // for the duration of one event, so its identifier cannot change between those sites. Null is
        // a REAL STATE and is not defaulted to zero: `Long(null)` is null in PowerScript, and every
        // addressing call below would receive that null and write nothing, which the HasValue guards
        // reproduce exactly rather than substituting an identifier or raising an error.
        long? columnId = dwo.ColumnId();

        // :L334  aOrgValue = dwo.Primary[row]
        // The `any` snapshot. A null cell is preserved as null, because the equality arm at :L372
        // depends on both-null being distinguishable from one-sided null.
        object? aOrgValue = dwo.Primary[row];

        // :L335  orgStatus = GetItemStatus(row,Long(dwo.ID),Primary!)
        // Taken at the same instant as the value, because :L375-L376 restore BOTH together and a value
        // restored without its status would leave the row claiming a modification state it no longer
        // has. With no resolvable column the read cannot be addressed, and NotModified is what
        // PowerBuilder yields for an item it cannot resolve - so the degenerate path degrades as the
        // oracle does rather than throwing.
        ItemStatus orgStatus = columnId.HasValue
            ? host.GetItemStatus(row, columnId.Value, DwBuffer.Primary)
            : ItemStatus.NotModified;

        // :L322  `long rtCode,nItemChangeRetCode` - PowerScript numeric locals default to zero, and
        // THAT ZERO IS LOAD BEARING: it is what the guards at :L342 and :L347 test against.
        long rtCode = 0L;

        // ------------------------------------------------------------------------------------------
        // :L337  //检查是否由ItemChanged触发            "check whether ItemChanged triggered this"
        // :L338  if nItemChangeRetCode = 1 or nItemChangeRetCode = 3 then
        // :L339      rtCode = 1
        //
        // THE PRE-SET. This is the mechanism by which this handler's behaviour is a function of the
        // PREVIOUS event's return value, and therefore the reason AAP 0.6.1.4 assigns this chain
        // strictly synchronous ordering. Note the comparison is NUMERIC and against the CONSUMED
        // value: a stashed 3 reaches here even though the item-change event rewrote its own result
        // from 3 to 1 at :L225, because the stash was written at :L195 BEFORE that rewrite.
        // ------------------------------------------------------------------------------------------
        bool preSetFromStash = false;

        if (nItemChangeRetCode == (long)ItemChangeResult.TriggerValidationError
            || nItemChangeRetCode == (long)ItemChangeResult.KeepValueNoFocusMove)
        {
            rtCode = (long)ItemChangeResult.TriggerValidationError;
            preSetFromStash = true;
        }

        bool itemErrorRaised = false;
        bool itemErrorReturnedNull = false;

        // ------------------------------------------------------------------------------------------
        // :L342  if rtCode = 0 then
        // :L343      rtCode = Event ItemError(row,dwo,data)
        // :L344      if IsNull(rtCode) then rtCode = 0
        //
        // The raise happens ONLY when the pre-set did not fire, so a stashed 1 or 3 suppresses the
        // semantic event entirely. The null coercion at :L344 IS THE ORACLE'S, not a defensive habit:
        // AAP 0.4.5.4 forbids collapsing null to zero implicitly, and this is the one site where the
        // legacy does it explicitly. DataWindowServiceHost.ItemError is the only nullable event return
        // among the eleven for exactly this reason, and it deliberately does NOT perform the coercion
        // itself, so that this line stays visible in the port.
        // ------------------------------------------------------------------------------------------
        if (rtCode == 0L)
        {
            long? handlerResult = host.ItemError(row, dwo, data);
            itemErrorRaised = true;

            if (handlerResult is null)
            {
                itemErrorReturnedNull = true;
                rtCode = 0L;
            }
            else
            {
                rtCode = handlerResult.Value;
            }
        }

        string validationMessage = string.Empty;
        bool validationMessageFellBack = false;
        ValidationStructuredError? error = null;

        // ------------------------------------------------------------------------------------------
        // :L347  if rtCode = 0 then
        // :L348      if data <> "" /*or String(aOrgValue) <> ""*/ then
        //
        // The commented-out disjunct is carried across INERT and is not revived (constraint C-B):
        // reviving it would take the message branch for an empty entry over a non-empty original
        // value, which the oracle does not do.
        // ------------------------------------------------------------------------------------------
        if (rtCode == 0L)
        {
            if (data.Length != 0)
            {
                // :L349  //如果当前列有校验消息则显示该消息,同时阻止修改和焦点切换
                //        "if the column has a validation message, show it and block both the edit and
                //         the focus change"
                // :L350  sErrMsg = Describe(dwo.Name+".ValidationMsg")
                //
                // The property is read RAW. DataWindowServiceHost.Describe deliberately returns the
                // quoted text exactly as the DataWindow reports it and does not helpfully unquote it,
                // because the strip below is what unquotes it and the strip is observable.
                string sErrMsg = host.Describe(dwo.Name + ".ValidationMsg");

                // ----------------------------------------------------------------------------------
                // :L351  if Len(sErrMsg) > 2 then
                // :L352      sErrMsg = Mid(sErrMsg,2,Len(sErrMsg) - 2)
                //
                // DROPS THE FIRST *AND* THE LAST CHARACTER, because the DataWindow stores the message
                // as a QUOTED LITERAL. PowerScript `Mid` is one-based, so starting at 2 skips the
                // opening quote and a length of Len-2 stops before the closing one.
                //
                // THE GUARD BOUNDARY IS `> 2` AND IS REPRODUCED EXACTLY. A message of length 0, 1 or
                // 2 is left UNTOUCHED - so a bare pair of quotes survives as a bare pair of quotes
                // and is then caught by neither arm of the fallback test below, which is the oracle's
                // behaviour and not an oversight to correct. At length 3 exactly one character
                // survives. An off-by-one here silently corrupts every validation message in the
                // application, which is why the conversion goes through MidOneBased rather than being
                // written inline.
                // ----------------------------------------------------------------------------------
                if (sErrMsg.Length > 2)
                {
                    sErrMsg = MidOneBased(sErrMsg, 2, sErrMsg.Length - 2);
                }

                // ----------------------------------------------------------------------------------
                // :L354  if sErrMsg = "" or sErrMsg = "?" then
                // :L355      sErrMsg = I18N(ne_cst_i18n.CAT_DWSVC,"输入了无效的值") + "!"
                //
                // `"?"` is the DataWindow's OWN PLACEHOLDER for "no message", which is why it is
                // tested alongside the empty string rather than being shown to a user.
                //
                // THE ORDER IS TRANSLATE THEN APPEND. The `"!"` is concatenated onto the TRANSLATED
                // fallback, never onto the column's own message and never before translation, so a
                // provider that rewrites the fallback still gets its exclamation mark and a column
                // that supplied its own message never gains one.
                //
                // I18N here is the injected facade, and its SILENT PASSTHROUGH is preserved: with no
                // provider installed the text comes back unchanged, nothing is thrown, nothing is
                // logged and nothing is marked untranslated [i18n.srf:L17-L18]. The provider's own
                // return value - 1 for handled, 0 for not - is discarded by the facade, exactly as
                // the oracle discards it.
                //
                // THE COALESCE IS DECISION D-12, NOT A HABIT. No provider in this repository can
                // make the facade answer null for a non-null argument, but a third-party one could
                // null the `ref` parameter, and the oracle would then propagate that null into its
                // dialog because PowerScript `null + "!"` is null. That is not representable on a
                // non-nullable field, so the untranslated source stands in - the same string the
                // no-provider case already yields.
                // ----------------------------------------------------------------------------------
                if (sErrMsg.Length == 0
                    || string.Equals(
                        sErrMsg,
                        ValidationStructuredError.NoValidationMessagePlaceholder,
                        StringComparison.Ordinal))
                {
                    sErrMsg = (_i18n.I18N(
                                   Categories.CAT_DWSVC,
                                   ValidationStructuredError.LegacyFallbackTextSource)
                               ?? ValidationStructuredError.LegacyFallbackTextSource)
                        + ValidationStructuredError.LegacyFallbackSuffix;

                    validationMessageFellBack = true;
                }

                validationMessage = sErrMsg;

                // ----------------------------------------------------------------------------------
                // :L357  MessageBox(I18N(ne_cst_i18n.CAT_DWSVC,"错误"),sErrMsg,StopSign!)
                //
                // THE ONLY LIVE DIALOG IN ALL 616 LINES OF THE ORACLE (decision D-11), and it becomes
                // a structured error. TITLE, BODY AND SEVERITY ARE THREE FIELDS, NOT ONE: the oracle
                // localizes the title separately from the body on this very line, so collapsing them
                // would lose a translated string. No arguments are supplied because the oracle
                // substitutes nothing here, which is what keeps the body verbatim.
                // ----------------------------------------------------------------------------------
                string title = _i18n.I18N(
                                   Categories.CAT_DWSVC,
                                   ValidationStructuredError.LegacyTitleSource)
                               ?? ValidationStructuredError.LegacyTitleSource;

                error = ValidationStructuredError.Create(
                    title,
                    sErrMsg,
                    DialogSeverity.StopSign,
                    Categories.CAT_DWSVC,
                    localized: true,
                    ImmutableArray<string>.Empty);

                // :L358  rtCode = 1
                rtCode = (long)ItemChangeResult.TriggerValidationError;
            }
            else
            {
                // ----------------------------------------------------------------------------------
                // :L359  else
                // :L360      //拒绝录入空值，保存时再检测提示
                //            "refuse to record an empty value; check and report at save time instead"
                // :L361      _bDwnItemValidationError = false
                // :L362      return 3
                //
                // THE SUBTLE ARM. It CLEARS THE GUARD FIRST AND THEN RETURNS, so the tail restore
                // block never runs and the flag is cleared HERE rather than at :L382. There are
                // therefore TWO flag-clearing sites and they must stay separate (constraint C-B):
                // unifying this early return with the normal exit would run the tail for a result of
                // 3, and the ordering of clear-then-return is itself the behaviour.
                //
                // Note also which value this is: 3 is returned RAW here. The item-change dispatch
                // rewrites its own 3 into a 1 at :L225, but this routine has no such rewrite.
                // ----------------------------------------------------------------------------------
                _inItemValidationError = false;

                return new ValidationErrorOutcome
                {
                    ReEntered = false,
                    RawResult = (long)ItemChangeResult.KeepValueNoFocusMove,
                    StashedRawItemChangeRetCode = nItemChangeRetCode,
                    PreSetFromStash = preSetFromStash,
                    ItemErrorRaised = itemErrorRaised,
                    ItemErrorReturnedNull = itemErrorReturnedNull,
                    EmptyData = true,
                    OriginalValue = aOrgValue,
                    OriginalStatus = orgStatus,
                    ColumnId = columnId,
                    State = CaptureState(),
                };
            }
        }

        bool? rowStillExists = null;
        bool valueRestored = false;

        // ------------------------------------------------------------------------------------------
        // :L366  choose case rtCode
        // :L367      case 1,3
        //
        // THE MULTI-VALUE ARM. Both 1 and 3 restore, and there is NO `case else` at :L380 - any other
        // result simply leaves the construct with nothing done, which is why no default arm appears
        // below. A result of 3 can only arrive here from a handler that returned it at :L343, because
        // the empty-data arm returned early and the pre-set produces 1.
        // ------------------------------------------------------------------------------------------
        switch (rtCode)
        {
            case (long)ItemChangeResult.TriggerValidationError:
            case (long)ItemChangeResult.KeepValueNoFocusMove:
            {
                // ----------------------------------------------------------------------------------
                // :L368  //*此时有可能行被删除，所以需要检查（由MessageBox处理的Post消息）
                //        "the row may have been deleted by now, so it has to be checked (by the
                //         posted message MessageBox processed)"
                // :L369  if row <= RowCount() and nItemChangeRetCode <> 3 then
                //
                // TWO CONDITIONS TOGETHER, AND BOTH SURVIVE. The first is DEFENSIVE for the reason
                // the oracle states: putting up a dialog pumps messages, and one of them may have
                // deleted the row. Dropping it would write to a row that no longer exists. Note it is
                // an upper bound only - the oracle does not test `row >= 1` - and that is reproduced
                // rather than tightened. RowCount() IS the last valid row number, because DataWindow
                // rows are one-based.
                //
                // The second suppresses the restore precisely when the stashed code was 3, which is
                // the oracle's way of honouring :L224's "keep the value, do not switch focus": a 3
                // that became a 1 is still distinguishable here.
                // ----------------------------------------------------------------------------------
                bool rowExists = row <= host.RowCount();
                rowStillExists = rowExists;

                if (rowExists && nItemChangeRetCode != (long)ItemChangeResult.KeepValueNoFocusMove)
                {
                    // ------------------------------------------------------------------------------
                    // :L370  //还原当前显示的不可编辑下拉值
                    //        "restore the currently displayed non-editable drop-down value"
                    // :L371  //*缓冲区的值可能已经被改变,防止覆盖
                    //        "the buffer value may already have been changed; prevent overwriting it"
                    // :L372  if aOrgValue = dwo.Primary[row] or (IsNull(aOrgValue) and IsNull(dwo.Primary[row])) then
                    //
                    // THE SAME THREE-ARM EQUALITY THE ITEM-CHANGE PROTOCOL USES at :L198-L202 -
                    // equal, or BOTH NULL - so it is reused from Domain/ItemChangeProtocol.cs rather
                    // than written twice. One-sided null is NOT equal, and that asymmetry is the
                    // guard's whole purpose: it means the handler changed the cell and the snapshot
                    // must not be written back over it.
                    //
                    // The current reading is taken FRESH here rather than reusing the one from :L334,
                    // because everything between the two lines may have changed it.
                    // ------------------------------------------------------------------------------
                    if (ItemChangeProtocol.IsBufferValueEqual(aOrgValue, dwo.Primary[row]))
                    {
                        // :L373  //if Describe(dwo.Name + ".DDDW.AllowEdit") = "no" or &
                        // :L374  //   Describe(dwo.Name + ".DDLB.AllowEdit") = "no" then //DDDW/DDLB
                        //
                        // Carried across INERT (constraint C-B). The oracle's own comment at :L370
                        // says the restore is FOR non-editable drop-downs, yet the check that would
                        // narrow it to them is commented out - so the restore currently applies to
                        // every column. Reviving the guard would stop restoring on ordinary columns,
                        // which is a behaviour change dressed as a fix.
                        if (columnId.HasValue)
                        {
                            // :L375  SetItem(row,Long(dwo.ID),aOrgValue)
                            host.SetItem(row, columnId.Value, aOrgValue);

                            // :L376  SetItemStatus(row,Long(dwo.ID),Primary!,orgStatus)
                            host.SetItemStatus(row, columnId.Value, DwBuffer.Primary, orgStatus);

                            valueRestored = true;
                        }

                        // :L377  //end if
                        //
                        // The closing half of the commented-out drop-down guard, carried for the same
                        // reason. Both host calls return a legacy integer code that the oracle
                        // DISCARDS, so neither is examined here either.
                    }
                }

                break;
            }
        }

        // :L382  _bDwnItemValidationError = false
        //
        // The SECOND of the two clearing sites. It runs on every path that did not return early, and
        // it is deliberately not shared with :L361.
        _inItemValidationError = false;

        // :L384  return rtCode
        return new ValidationErrorOutcome
        {
            ReEntered = false,
            RawResult = rtCode,
            StashedRawItemChangeRetCode = nItemChangeRetCode,
            PreSetFromStash = preSetFromStash,
            ItemErrorRaised = itemErrorRaised,
            ItemErrorReturnedNull = itemErrorReturnedNull,
            EmptyData = false,
            ValidationMessage = validationMessage,
            ValidationMessageFellBack = validationMessageFellBack,
            Error = error,
            ValueRestored = valueRestored,
            RowStillExists = rowStillExists,
            OriginalValue = aOrgValue,
            OriginalStatus = orgStatus,
            ColumnId = columnId,
            State = CaptureState(),
        };
    }

    // ==============================================================================================
    //  THE POSTED CONTINUATION                            se_cst_dw.sru:L387-L390 and :L537-L558
    //  --------------------------------------------------------------------------------------------
    //  :L387  event ondwnkillfocus;//*应用修改            "apply the edit"
    //  :L388  if Not _bDoItemChange then
    //  :L389      Post _of_PostAcceptText()
    //  :L390  end if
    //
    //  `Post` places the call on the WIN32 MESSAGE QUEUE so that it runs after the current event
    //  returns. A headless Linux container has no message pump, so AAP 0.6.5 records the pump as a
    //  DELIBERATE NON-PORT and AAP 0.4.5.4 requires the posted call to become an explicitly queued
    //  continuation (decision D-6). Two properties of the original must survive:
    //
    //    * THE GUARD. It is queued only when the item-change flag is CLEAR, so an accept never runs
    //      while an item change is in flight.
    //    * THE DEFERRAL ITSELF. :L553's re-check is deliberately observed AFTER the event completes,
    //      so the continuation cannot be inlined into the kill-focus handler - inlining it would test
    //      focus at a moment the oracle never tests it.
    //
    //  THE QUEUE IS DRAINED BY THE HOST, never by a timer, a background task or a thread-pool work
    //  item - the same discipline Shared.Eventful's EventBroker.Post follows. Nothing in this file
    //  schedules anything.
    // ==============================================================================================

    /// <summary>
    /// The code <c>AcceptText()</c> returns on failure, and the ONLY value that restores focus
    /// [<c>se_cst_dw.sru:L554</c>].
    /// </summary>
    /// <remarks>
    /// Named because the oracle tests it as a bare literal, and because the test is EQUALITY against
    /// <c>-1</c> rather than "not success": broadening it to "not 1" or "less than or equal to zero"
    /// would restore focus in cases the legacy leaves alone.
    /// </remarks>
    internal const int AcceptTextFailure = -1;

    /// <summary>
    /// Queues the deferred accept-text continuation - the port of
    /// <c>if Not _bDoItemChange then Post _of_PostAcceptText()</c>
    /// [<c>se_cst_dw.sru:L388-L390</c>].
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the continuation was queued. <see langword="false"/> MEANS THE
    /// ITEM-CHANGE FLAG WAS SET, which is a meaningful observation rather than a missing one - it is
    /// what <c>DwnKillFocusEvent.deferred_accept_queued</c> reports.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE FLAG IS THE ONLY GUARD, exactly as at <c>:L388</c>. Queueing is idempotent in effect: a
    /// second call while one is already outstanding leaves exactly one continuation queued, which
    /// matches a message queue coalescing nothing but a single deferred accept being all the oracle
    /// ever has outstanding for one control.
    /// </para>
    /// <para>
    /// A pending continuation on a session that is then closed is REPORTED rather than silently
    /// dropped: <c>CloseValidationSessionResponse.final_state</c> carries
    /// <see cref="DeferredAcceptPending"/> precisely so a caller can observe outstanding work.
    /// </para>
    /// </remarks>
    internal bool TryQueueDeferredAccept()
    {
        // :L388  if Not _bDoItemChange then
        if (_doItemChange)
        {
            return false;
        }

        // :L389  Post _of_PostAcceptText()
        lock (_gate)
        {
            _deferredAcceptPending = true;
        }

        return true;
    }

    /// <summary>
    /// Runs the queued continuation, if one is outstanding - the port of
    /// <c>_of_postaccepttext</c> [<c>se_cst_dw.sru:L537-L558</c>, body <c>:L553-L557</c>].
    /// </summary>
    /// <param name="host">The DataWindow the continuation was queued against.</param>
    /// <returns>
    /// What the continuation did, or <see langword="null"/> when nothing was queued - which is not an
    /// error and is how a host distinguishes "drained" from "nothing to drain".
    /// </returns>
    /// <remarks>
    /// <para>
    /// CALLED BY THE HOST, AFTER the event that queued it has answered (decision D-6). The queue is
    /// cleared BEFORE the body runs, so a continuation is executed at most once even if the host drains
    /// twice, and so a failure inside the body cannot leave the same work queued forever.
    /// </para>
    /// <para>
    /// A CLOSED SESSION DRAINS NOTHING. That is boundary-created: the oracle's control cannot be closed
    /// while its own posted message is in flight, whereas a server-held session can be.
    /// </para>
    /// </remarks>
    internal DeferredAcceptOutcome? DrainDeferredAccept(DataWindowServiceHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        lock (_gate)
        {
            if (!_deferredAcceptPending || !_isOpen)
            {
                return null;
            }

            _deferredAcceptPending = false;
        }

        // ------------------------------------------------------------------------------------------
        // :L553  if GetFocus() <> this then
        //
        // AN IDENTITY COMPARISON AGAINST THE HOST, not a focus test - which is why
        // DataWindowServiceHost.GetFocusedObject returns `object?`. Nothing holding focus reads as
        // "focus has left", because PowerScript's GetFocus() yields an object that is not `this` in
        // that case too. When focus HAS come back to the host the whole body is skipped, and that is
        // precisely the case the deferral exists to catch.
        // ------------------------------------------------------------------------------------------
        if (!ReferenceEquals(host.GetFocusedObject(), host))
        {
            // :L554  if AcceptText() = -1 then
            int acceptTextResult = host.AcceptText();

            if (acceptTextResult == AcceptTextFailure)
            {
                // :L555  SetFocus()
                //
                // Focus goes back to the DataWindow so the invalid entry can be corrected. The oracle
                // discards the returned code; it is reported here so a test can assert the call
                // happened rather than inferring it from a side effect.
                int setFocusResult = host.SetFocus();

                return new DeferredAcceptOutcome
                {
                    FocusHadLeftHost = true,
                    AcceptTextResult = acceptTextResult,
                    FocusRestored = true,
                    SetFocusResult = setFocusResult,
                };
            }

            return new DeferredAcceptOutcome
            {
                FocusHadLeftHost = true,
                AcceptTextResult = acceptTextResult,
                FocusRestored = false,
            };
        }

        // :L557  end if - the body did not run, and the oracle does nothing else.
        return new DeferredAcceptOutcome
        {
            FocusHadLeftHost = false,
        };
    }

    /// <summary>
    /// The centralized ONE-BASED substring helper AAP 0.4.5.4 requires - the port of PowerScript
    /// <c>Mid(value, start, length)</c>, whose single use in this file is
    /// <c>Mid(sErrMsg,2,Len(sErrMsg) - 2)</c> at <c>se_cst_dw.sru:L352</c>.
    /// </summary>
    /// <param name="value">The string to take a substring of.</param>
    /// <param name="oneBasedStart">
    /// The ONE-BASED position of the first character to take. The first character of the string is
    /// <c>1</c>, not <c>0</c>.
    /// </param>
    /// <param name="length">How many characters to take.</param>
    /// <returns>
    /// The substring, or the EMPTY STRING when <paramref name="oneBasedStart"/> is past the end of the
    /// string or is less than <c>1</c>, or when <paramref name="length"/> is not positive. The result is
    /// truncated at the end of the string rather than throwing when the requested length overruns.
    /// </returns>
    /// <remarks>
    /// <para>
    /// WHY THIS IS A NAMED HELPER RATHER THAN AN INLINE Substring CALL. AAP 0.4.5.4 calls one-based
    /// translation "the single most dangerous mechanical hazard in this refactor" and requires the
    /// arithmetic to be centralized or individually audited. Centralizing it puts the
    /// <c>oneBasedStart - 1</c> conversion in exactly one place and makes the boundary directly
    /// testable, rather than leaving an off-by-one that would corrupt every validation message while
    /// still producing a plausible-looking string.
    /// </para>
    /// <para>
    /// THE GUARDS ARE PowerScript's, NOT DEFENSIVE ADDITIONS. PowerBuilder's <c>Mid</c> answers the
    /// empty string for a start position past the end and clamps a length that overruns; it does not
    /// raise. Reproducing that is what makes this a port rather than a re-specification, and it is why
    /// the helper is safe to reuse at any future one-based site.
    /// </para>
    /// <para>
    /// WIDTH. The oracle's <c>Len</c> returns <c>long</c> while this takes <see langword="int"/>,
    /// because a string length is an array measurement and .NET string indices are <see langword="int"/>.
    /// No reachable value approaches the limit. Both count UTF-16 code units, so a surrogate pair
    /// counts as two in the oracle and two here.
    /// </para>
    /// </remarks>
    internal static string MidOneBased(string value, int oneBasedStart, int length)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (oneBasedStart < 1 || length <= 0)
        {
            return string.Empty;
        }

        // The ONE conversion from a one-based position to a zero-based offset in this file.
        int offset = oneBasedStart - 1;

        if (offset >= value.Length)
        {
            return string.Empty;
        }

        int available = value.Length - offset;

        return value.Substring(offset, length < available ? length : available);
    }
}

/// <summary>
/// The result of opening a validation session - the in-process peer of
/// <c>OpenValidationSessionResponse</c>.
/// </summary>
/// <param name="Session">The session, or <see langword="null"/> when the open was refused.</param>
/// <param name="ReturnCode">
/// <see cref="RetCode.OK"/> on success, and otherwise a DEFINED code: never a silent failure and never
/// a session that was almost created.
/// </param>
internal sealed record ValidationSessionOpenResult(ValidationSession? Session, long ReturnCode)
{
    /// <summary>Whether a session was actually opened.</summary>
    /// <remarks>
    /// Tests both members rather than either alone, and deliberately does NOT use a two-way success
    /// predicate: <see cref="RetCode"/> has a tri-state hole in which <c>PREVENT</c> reads as a success
    /// and <c>CANCELLED</c> is neither, so equality against <see cref="RetCode.OK"/> is the only safe
    /// test here.
    /// </remarks>
    internal bool IsOpened => Session is not null && ReturnCode == RetCode.OK;
}

/// <summary>
/// The result of closing a validation session - the in-process peer of
/// <c>CloseValidationSessionResponse</c>.
/// </summary>
/// <param name="ReturnCode">
/// The outcome. THE SAME WHETHER THE SESSION WAS OPEN OR ALREADY GONE, because closure is idempotent.
/// </param>
/// <param name="WasOpen">
/// <see langword="false"/> when the session had already been closed or had expired. INFORMATIONAL ONLY:
/// a false value is NOT an error.
/// </param>
/// <param name="FinalState">
/// The state at the moment of closure, when the session was still open - carried so a caller can
/// observe an outstanding deferred continuation or a still-set re-entrancy guard rather than discarding
/// that fact. All-default when the session was already gone.
/// </param>
/// <remarks>
/// IDEMPOTENCE IS A CONTRACT OBLIGATION RATHER THAN AN IMPLEMENTATION NICETY (decision D-7). In the
/// oracle the state simply dies with the control; a server-held session needs a defined end, and a
/// caller that could not safely retry a close would leak sessions on any transport hiccup.
/// </remarks>
internal sealed record ValidationSessionCloseResult(
    long ReturnCode,
    bool WasOpen,
    ValidationSessionSnapshot FinalState);

/// <summary>
/// The result of looking a validation session up by its correlation identifier.
/// </summary>
/// <param name="Session">The session, or <see langword="null"/> when it could not be resolved.</param>
/// <param name="ReturnCode">
/// <see cref="RetCode.OK"/> when resolved; <see cref="RetCode.E_INVALID_ARGUMENT"/> for a missing or
/// blank identifier; <see cref="RetCode.E_INVALID_HANDLE"/> when no such session was ever registered;
/// <see cref="RetCode.E_NOT_EXISTS"/> when it was registered but has since been closed or has expired.
/// </param>
/// <remarks>
/// <para>
/// AN UNKNOWN IDENTIFIER IS A DEFINED ERROR AND NEVER A NEW SESSION. Silently creating one would hand
/// the caller four fields at their defaults while the chain it belongs to had already moved on, so the
/// stash would read zero and the validation-error handler would take the wrong branch - a wrong answer
/// rather than an error.
/// </para>
/// <para>
/// THE THREE CODES ARE DISTINGUISHED ON PURPOSE, and they follow the convention the sibling expression
/// session registry already established: a handle that was never valid is a different fault from one
/// that has expired, and only the second is worth retrying with a fresh open.
/// </para>
/// </remarks>
internal sealed record ValidationSessionResolution(ValidationSession? Session, long ReturnCode)
{
    /// <summary>Whether a live session was resolved.</summary>
    internal bool IsResolved => Session is not null && ReturnCode == RetCode.OK;
}

/// <summary>
/// The per-instance, in-memory store of open validation sessions, keyed by correlation identifier -
/// the boundary-created home for the four cross-event fields of
/// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L88-L96</c>.
/// </summary>
/// <remarks>
/// <para>
/// NO LEGACY EQUIVALENT EXISTS. In the oracle the four fields are instance fields of a control, so
/// there is nothing to look up and nothing to expire. This type exists solely because a stateless
/// request boundary has nowhere to put them (decisions D-7 and D-8).
/// </para>
/// <para>
/// PER-INSTANCE AND IN-MEMORY, WITH NO SHARED BACKING STORE. There is no distributed cache, no
/// database, no storage provider and no SQL anywhere in this file (constraints C-E and C-J). A session
/// is meaningful only within the correlated stream that opened it, so replicating it would buy nothing
/// and would introduce a consistency question the strictly synchronous event chain must not have.
/// </para>
/// <para>
/// EVERY CLOCK READ GOES THROUGH THE INJECTED <see cref="TimeProvider"/>, and both bounds come from
/// configuration rather than from a literal (constraints C-F and AAP 0.6.7). Neither bound is a
/// performance target: the idle timeout decides when abandoned state is reclaimed, and the ceiling
/// decides when a further session is refused outright (AAP 0.8.5).
/// </para>
/// </remarks>
internal sealed class ValidationSessionRegistry
{
    /// <summary>
    /// How many hexadecimal characters a generated correlation identifier has - 32, which is 16 bytes
    /// of cryptographically strong randomness.
    /// </summary>
    /// <remarks>
    /// SIZED FOR OPACITY, NOT FOR ANYTHING ELSE (decision D-9). Constraint C-G requires that possession
    /// of an identifier never stand in for authorization, and that an identifier be neither guessable
    /// nor enumerable; 128 bits of randomness makes enumeration infeasible, while the authorization half
    /// is enforced at the <c>Grpc/</c> and <c>Endpoints/</c> layer by the stock bearer handler. No
    /// identifier is ever derived from caller-supplied data.
    /// </remarks>
    internal const int GeneratedSessionIdLength = 32;

    private readonly ConcurrentDictionary<string, ValidationSession> _sessions =
        new(StringComparer.Ordinal);

    private readonly SessionLifetimeOptions _lifetime;

    private readonly I18n _i18n;

    private readonly TimeProvider _timeProvider;

    private int _openCount;

    /// <summary>
    /// Creates a registry from bound configuration - the constructor the service container uses.
    /// </summary>
    /// <param name="options">
    /// The bound options. THE LIFETIME LIVES AT <c>DataServices:Sessions:ValidationSession</c>, nested
    /// under a <c>Sessions</c> group; <c>DataServices:ValidationSession</c> would bind nothing and leave
    /// the compiled-in defaults in place.
    /// </param>
    /// <param name="i18n">
    /// The localization facade, passed to every session this registry opens. Optional so a test can run
    /// with no provider installed, which is the SILENT PASSTHROUGH state the oracle itself describes.
    /// </param>
    /// <param name="timeProvider">The clock seam.</param>
    internal ValidationSessionRegistry(
        IOptions<DataServicesOptions> options,
        I18n? i18n = null,
        TimeProvider? timeProvider = null)
        : this(GetValue(options), i18n, timeProvider)
    {
    }

    /// <summary>
    /// Creates a registry from an options instance - the constructor a test uses, and the one the
    /// container-facing overload delegates to.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="i18n">The localization facade.</param>
    /// <param name="timeProvider">The clock seam.</param>
    internal ValidationSessionRegistry(
        DataServicesOptions options,
        I18n? i18n = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _lifetime = options.Sessions?.ValidationSession ?? new SessionLifetimeOptions();
        _i18n = i18n ?? new I18n();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The configured idle timeout every session this registry opens is given.</summary>
    internal TimeSpan IdleTimeout => _lifetime.IdleTimeout;

    /// <summary>
    /// How many sessions may be open at once. An ADMISSION BOUND, not a target: it decides when a
    /// further session is refused outright, a decision the oracle never had to make because its
    /// sessions were objects in the caller's own address space.
    /// </summary>
    internal int MaxConcurrentSessions => _lifetime.MaxConcurrentSessions;

    /// <summary>How many sessions are currently open.</summary>
    internal int Count => Volatile.Read(ref _openCount);

    /// <summary>
    /// Opens a session under a freshly generated correlation identifier.
    /// </summary>
    /// <param name="dataWindowHandle">
    /// The DataWindow this session is bound to, from <c>OpenValidationSessionRequest</c>. A session is
    /// scoped to ONE DataWindow because the four fields are instance fields of one control in the
    /// oracle, so sharing one across DataWindows would make the stash readable by the wrong chain.
    /// </param>
    /// <param name="initialDisabledEventMask">
    /// The initial gate mask. Zero disables nothing, matching a freshly constructed control. Typed
    /// <see langword="uint"/> because that is the width <see cref="EventGate"/> operates on; the wire
    /// carries it as <c>int64</c> and that conversion belongs at the gRPC boundary (decision D-2).
    /// </param>
    /// <returns>The opened session, or a defined refusal.</returns>
    internal ValidationSessionOpenResult Open(
        string dataWindowHandle = "",
        uint initialDisabledEventMask = 0u) =>
        OpenWithId(GenerateSessionId(), dataWindowHandle, initialDisabledEventMask);

    /// <summary>
    /// Opens a session under a caller-supplied correlation identifier - the entry point a
    /// deterministic test uses.
    /// </summary>
    /// <param name="sessionId">The identifier. Must be non-blank and must not already be in use.</param>
    /// <param name="dataWindowHandle">The DataWindow this session is bound to.</param>
    /// <param name="initialDisabledEventMask">The initial gate mask.</param>
    /// <returns>
    /// The opened session, or a defined refusal: <see cref="RetCode.E_INVALID_ARGUMENT"/> for a blank
    /// identifier, <see cref="RetCode.FAILED"/> for one already in use, and
    /// <see cref="RetCode.E_BUSY"/> when the concurrent-session ceiling is reached and nothing could be
    /// reclaimed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// AN IDENTIFIER ALREADY IN USE IS REFUSED RATHER THAN REPLACED. Replacing would silently detach a
    /// live chain from its own stash.
    /// </para>
    /// <para>
    /// THE SLOT IS RESERVED BEFORE THE SESSION IS BUILT, so two concurrent opens cannot both pass the
    /// ceiling, and the reservation is given back if the insert then loses a race. The reclamation sweep
    /// runs only when the ceiling has actually been reached, so the ordinary path never pays for it.
    /// </para>
    /// <para>
    /// NAMED RATHER THAN OVERLOADED, DELIBERATELY. The sibling expression-session registry publishes
    /// this pair as <c>Open()</c> and <c>Open(string)</c>, which is unambiguous only because its
    /// generating overload takes no parameters at all. Here both entry points would begin with a
    /// <see langword="string"/> followed by defaulted parameters, so a single-argument call would be
    /// AMBIGUOUS (CS0121) and, worse, a two-argument call would silently bind to whichever overload the
    /// compiler preferred - passing a DataWindow handle where a session identifier was meant. A distinct
    /// name removes the class of mistake rather than documenting it.
    /// </para>
    /// </remarks>
    internal ValidationSessionOpenResult OpenWithId(
        string sessionId,
        string dataWindowHandle = "",
        uint initialDisabledEventMask = 0u)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new ValidationSessionOpenResult(null, RetCode.E_INVALID_ARGUMENT);
        }

        if (dataWindowHandle is null)
        {
            return new ValidationSessionOpenResult(null, RetCode.E_INVALID_ARGUMENT);
        }

        if (_sessions.ContainsKey(sessionId))
        {
            return new ValidationSessionOpenResult(null, RetCode.FAILED);
        }

        int ceiling = MaxConcurrentSessions;

        if (ceiling <= 0)
        {
            // A non-positive ceiling admits nothing. Unreachable through configuration, because startup
            // validation refuses a non-positive value; refused here rather than ignored so the two
            // cannot disagree.
            return new ValidationSessionOpenResult(null, RetCode.E_BUSY);
        }

        while (true)
        {
            int current = Volatile.Read(ref _openCount);

            if (current >= ceiling)
            {
                // Reclaim anything abandoned before refusing - and only then.
                if (SweepExpired() == 0)
                {
                    return new ValidationSessionOpenResult(null, RetCode.E_BUSY);
                }

                continue;
            }

            if (Interlocked.CompareExchange(ref _openCount, current + 1, current) == current)
            {
                break;
            }
        }

        ValidationSession session = new(
            sessionId,
            dataWindowHandle,
            initialDisabledEventMask,
            _lifetime,
            _i18n,
            _timeProvider);

        if (!_sessions.TryAdd(sessionId, session))
        {
            // Lost a race with another open using the same identifier. Give the reserved slot back
            // rather than leaking it, and refuse exactly as the pre-check would have.
            Interlocked.Decrement(ref _openCount);

            return new ValidationSessionOpenResult(null, RetCode.FAILED);
        }

        return new ValidationSessionOpenResult(session, RetCode.OK);
    }

    /// <summary>
    /// Resolves a live session by identifier, reporting a DEFINED code for every failure.
    /// </summary>
    /// <param name="sessionId">The correlation identifier.</param>
    /// <returns>The resolution. Never a silently created session.</returns>
    /// <remarks>
    /// An expired session found here is CLOSED AND RELEASED on the way out, so expiry is enforced at the
    /// point of use as well as by the sweep - a caller cannot revive one by holding its identifier.
    /// </remarks>
    internal ValidationSessionResolution Resolve(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new ValidationSessionResolution(null, RetCode.E_INVALID_ARGUMENT);
        }

        if (!_sessions.TryGetValue(sessionId, out ValidationSession? found))
        {
            return new ValidationSessionResolution(null, RetCode.E_INVALID_HANDLE);
        }

        if (!found.IsOpen || found.HasExpired())
        {
            Close(sessionId);

            return new ValidationSessionResolution(null, RetCode.E_NOT_EXISTS);
        }

        found.Touch();

        return new ValidationSessionResolution(found, RetCode.OK);
    }

    /// <summary>
    /// Resolves a live session by identifier in the try-pattern shape, for call sites that only need to
    /// know whether it is there.
    /// </summary>
    /// <param name="sessionId">The correlation identifier.</param>
    /// <param name="session">The resolved session, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a live session was resolved.</returns>
    internal bool TryGet(string? sessionId, out ValidationSession? session)
    {
        ValidationSessionResolution resolution = Resolve(sessionId);
        session = resolution.Session;

        return resolution.IsResolved;
    }

    /// <summary>
    /// Closes a session and releases its state - IDEMPOTENTLY.
    /// </summary>
    /// <param name="sessionId">The correlation identifier.</param>
    /// <returns>
    /// The close result. <see cref="ValidationSessionCloseResult.ReturnCode"/> is
    /// <see cref="RetCode.OK"/> whether or not the session was still there, and
    /// <see cref="ValidationSessionCloseResult.WasOpen"/> reports which it was; only a missing or blank
    /// identifier yields <see cref="RetCode.E_INVALID_ARGUMENT"/>, because that is a malformed request
    /// rather than an already-closed session.
    /// </returns>
    internal ValidationSessionCloseResult Close(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new ValidationSessionCloseResult(
                RetCode.E_INVALID_ARGUMENT,
                WasOpen: false,
                FinalState: default);
        }

        if (!_sessions.TryRemove(sessionId, out ValidationSession? removed))
        {
            return new ValidationSessionCloseResult(RetCode.OK, WasOpen: false, FinalState: default);
        }

        Interlocked.Decrement(ref _openCount);

        // Captured BEFORE the close, because the response carries the state at the moment of closure so
        // a caller can still observe an outstanding continuation or a set re-entrancy guard.
        ValidationSessionSnapshot finalState = removed.CaptureState();

        // The session's own close is idempotent, so its answer is the authoritative `was_open`: it is
        // false when the session had already been closed directly rather than through this registry.
        bool wasOpen = removed.Close();

        return new ValidationSessionCloseResult(RetCode.OK, wasOpen, finalState);
    }

    /// <summary>
    /// Closes every session that has been idle longer than the configured timeout.
    /// </summary>
    /// <returns>How many sessions were closed.</returns>
    /// <remarks>
    /// NET-NEW (decision D-7): an abandoned session must expire, because a client that never closes its
    /// own must not retain it forever. Driven entirely by the injected clock, so a test asserts expiry
    /// deterministically with no real waiting.
    /// </remarks>
    internal int SweepExpired()
    {
        int closed = 0;

        foreach (KeyValuePair<string, ValidationSession> entry in _sessions)
        {
            if (entry.Value.HasExpired() && Close(entry.Key).WasOpen)
            {
                closed++;
            }
        }

        return closed;
    }

    /// <summary>
    /// Closes every session, expired or not - for host shutdown.
    /// </summary>
    /// <returns>How many sessions were closed.</returns>
    internal int CloseAll()
    {
        int closed = 0;

        foreach (KeyValuePair<string, ValidationSession> entry in _sessions)
        {
            if (Close(entry.Key).WasOpen)
            {
                closed++;
            }
        }

        return closed;
    }

    /// <summary>
    /// Generates an opaque, non-enumerable correlation identifier.
    /// </summary>
    /// <returns>A lower-case hexadecimal identifier of <see cref="GeneratedSessionIdLength"/> characters.</returns>
    /// <remarks>
    /// Drawn from <see cref="RandomNumberGenerator"/> rather than from a counter, a timestamp, a hash of
    /// the request or a general-purpose pseudo-random source, so the identifier carries no structure a
    /// caller could walk and encodes nothing about the session it names (decision D-9).
    /// </remarks>
    internal static string GenerateSessionId() =>
        RandomNumberGenerator.GetHexString(GeneratedSessionIdLength, lowercase: true);

    private static DataServicesOptions GetValue(IOptions<DataServicesOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Value ?? new DataServicesOptions();
    }
}
