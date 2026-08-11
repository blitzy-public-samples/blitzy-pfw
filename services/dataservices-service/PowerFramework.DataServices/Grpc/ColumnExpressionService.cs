// ==================================================================================================
//  ColumnExpressionService - the server implementation of contract C-04
//  `dataservices.v1.ColumnExpressionService`.
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS. A TRANSPORT AND SESSION FACADE, and nothing else. Every behaviour it exposes
//  lives in Expressions/: the engine (ColumnExpressionEngine.cs), the `Describe("Evaluate(...)")`
//  substitute (DataWindowExpressionEvaluator.cs), the seven-type variable union
//  (ExpressionVariableEnvironment.cs), the handle registry and calculation stack
//  (ExpressionSession.cs), the macro dispatch rules (MacroInvoker.cs), the 28 error sites
//  (ParseErrorFormatter.cs) and the pinyin BLOCKED path (PinyinFirstLetterMatcher.cs). NOTHING here
//  parses an expression, evaluates one, or decides what a macro means. If a behavioural question can
//  be asked of this file, the answer is in one of those seven.
//
//  ITS BEHAVIOURAL SOURCE is `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru`
//  at 2,435 lines - the largest in-scope legacy object - plus its own authoritative specification
//  `docs/n_cst_dwsvc_columnexp.md`. Both are READ-ONLY (C-C): they are the behavioural oracle, and
//  every behavioural assertion below carries its `ws_objects/**` or `docs/**` locator because
//  nothing else in the repository can adjudicate it.
//
//  ============================ WHY C-04 IS SEPARATE FROM C-03 (C-K) ============================
//  The expansion engine versions INDEPENDENTLY of the DataWindow service (AAP 0.4.3). The two are
//  coupled in the legacy only because both are in-process members of one control; across a boundary
//  the coupling would force the 22-event chain and the expansion grammar to move in lockstep, and
//  the grammar is the faster-moving of the two. Concretely, THIS FILE DOES NOT REFERENCE
//  Grpc/DataWindowService.cs AND MUST NOT: the one real coupling between them - disabling
//  EID_ITEMCHANGE also suppresses column-expression evaluation [se_cst_dw.sru:L43] - is mediated by
//  Domain/EventGate.cs and the engine, never by a call from one gRPC service class to the other.
//  Introducing that edge would misrepresent the contract split it exists to express.
//
//  ============================ WHY EACH STREAM IS INVERTED (C-K) ===============================
//  `InvokeMethodChannel` - THE LEGACY EXPECTS THE *APPLICATION* TO IMPLEMENT THE MACRO SWITCH. Its
//  own specification says so: the function is defined in the DataWindow's
//  `OnColumnExpInvokeMethod` event [docs/n_cst_dwsvc_columnexp.md:L106], declared
//  `(long row, dwobject dwo, string name, string args[]) -> any` [se_cst_dw.sru:L14], and the
//  documented handler is a `choose case name` returning the computed value [docs:L124-L127 direct,
//  :L146-L151 dynamic]. The engine ASKS; it does not own the implementations. The source confirms
//  the inversion twice: [:L2263] for the dynamic form and [:L2287] for the direct one. So across a
//  boundary DataServices MUST CALL BACK INTO ITS CLIENT mid-calculation. It is STRICTLY SYNCHRONOUS
//  (AAP 0.6.1.4 pattern (b), ORDERING_DISCIPLINE_SYNCHRONOUS): the calculation cannot proceed
//  without the value, so there is no reordering and no speculative continuation. NOTE what this is
//  NOT: `$$Invoke` looks like it needs `n_scriptinvoker`, which is a deferred ScriptBridge object
//  (C-D). It does not. The macro switch is the client's, and C# has native variadic support, so no
//  ScriptBridge coupling is acquired here or anywhere.
//
//  `TraceChannel` - the trace is EMITTED BY THE ENGINE, so it flows server to client with no
//  request to answer. It is FIRE-AND-FORGET and therefore pattern (a),
//  ORDERING_DISCIPLINE_SEQUENCED: pure diagnostics, so a consumer may reorder on the token rather
//  than fail. Emission is gated on `#Trace` [:L98, set by `of_settrace` :L2409-L2411], which is why
//  GetServiceState publishes that flag - a silent channel and a disabled channel are otherwise
//  indistinguishable.
//
//  ==================== WHY THE PAYLOAD CARRIES THREE COMPONENTS (C-K) =========================
//  `ExpressionBinding` transmits ALL THREE of the unexpanded source text, the bind-time snapshot and
//  the live environment. This is mechanical, not defensive. Static expansion [`$name`, docs:L37-L54]
//  substitutes the variable's value AT THE MOMENT THE EXPRESSION IS SET - the parser rewrites the
//  expression IN PLACE [:L1435], so the variable name is GONE from the stored text and no later
//  assignment can reach it; the worked example is permanently 5. Dynamic expansion [`$$name`,
//  docs:L56-L73] keeps the reference, whose index points into the live global-variable table, so
//  mutation propagates; the identical example yields 6. Therefore: transmitting an
//  already-expanded string makes a STATIC BINDING INDISTINGUISHABLE FROM A LITERAL and strips a
//  DYNAMIC BINDING OF ITS RESOLUTION ENVIRONMENT. One component cannot carry two facts.
//
//  AND THE MODE IS PER-REFERENCE, NEVER PER-EXPRESSION. The specification's own example mixes both
//  inside one string: `"$$上月读数 + $本月读数"` [docs:L68] is one dynamic reference and one static
//  reference together. A per-expression mode field would be structurally incapable of representing
//  it, so every `VarData` carries its own `expansion_mode` across the five evidenced modes: static
//  `$name`; dynamic `$$name`; dynamic-indirect `$$('name')` [docs:L92]; macro-direct `$Func(args)`
//  [docs:L110]; macro-dynamic `$$Invoke($var, args)` [docs:L134]. The environment is a DISCRIMINATED
//  UNION over seven scalar types (`VarValue`), never a stringly-typed map, because the coercion
//  behaviour depends on the type.
//
//  ============= WHY CROSS-SESSION FOREIGN VARIABLES ARE BLOCKED, NOT APPROXIMATED (C-K) ========
//  `foreignvardata` is `{ integer index; n_cst_dwsvc_columnexp expsvc }` [:L80-L83]: `expsvc` is a
//  LIVE IN-PROCESS POINTER to another DataWindow's expression service, and `globalvardata.links[]`
//  [:L70] is an array of the same. A pointer cannot be serialized. Compounding it, the
//  specification states a foreign variable REQUIRES DYNAMIC EXPANSION (需要使用动态展开)
//  [docs:L29-L35] - `of_AddForeignVar('name', dw_src)` then `of_AddVarExp('local', '$$name')` - so
//  the limitation bites precisely on the path that must resolve at calculation time.
//
//  The resolution AAP 0.6.2.3 mandates, and the one this file implements: a cross-DataWindow
//  variable resolves through a SESSION-SCOPED DATAWINDOW HANDLE, and is supported ONLY when both
//  DataWindows are co-resident in the same expression session inside one DataServices instance. A
//  reference spanning sessions or service instances is BLOCKED AND RETURNS A DEFINED ERROR
//  (`ExpressionError.Category.CATEGORY_FOREIGN_REFERENCE_BLOCKED`). It is never a silently wrong
//  value and never an approximation of a pointer dereference across a network. THIS IS A
//  DELIBERATE, DOCUMENTED NARROWING OF THE LEGACY CONTRACT, surfaced before implementation rather
//  than discovered during it, because the alternative produces results that are wrong in a way no
//  test would obviously catch.
//
//  ======================== THE LEGACY SURFACE IS CARRIED, NOT TIDIED (C-B) =====================
//  Four anomalies are preserved deliberately. `of_addexp` returns the new expression's INDEX rather
//  than a return code, and its return type is `integer` not `long` [:L172] - hence
//  `AddExpressionResponse.index` distinct from `ret_code`. `_of_calcitem` is declared PUBLIC despite
//  its private-convention underscore prefix and answers `boolean` rather than a code [:L142]; the
//  underscore travels as descriptor metadata `(common.v1.legacy_name) = "_of_calcitem"` because a
//  protobuf identifier cannot begin with one, so it is neither silently dropped nor falsely claimed
//  to have survived verbatim. `of_setenabled`'s veto maps to `RetCode.FAILED` (-1) and NOT to
//  `PREVENT` (+1) [n_cst_dwsvc.sru:L90] - see SetEnabled. And the engine's own `ondoitemchanged`
//  takes `(row, colname, colid, frominput)` [:L87], which is NOT the shape of `se_cst_dw`'s
//  `ondoitemchanged(row, dwo)` [se_cst_dw.sru:L26]; conflating the two would corrupt the contract.
//  No convenience overload, no added validation and no expression-cache optimisation the legacy
//  lacks appears anywhere below.
//
//  ============================== BOUNDARY AND SAFETY POSTURE ===================================
//  AUTHENTICATED FROM THE OUTSET (C-G). `Program.cs` applies `RequireAuthorization()` at
//  `MapGrpcService<ColumnExpressionService>()`. There is no `[AllowAnonymous]` here and no
//  per-method escape, and that matters more than usual because both inverted streams are LONG-LIVED
//  CALLBACK CHANNELS: the caller's identity is established by the framework's bearer handler before
//  a channel opens and is NEVER re-derived from stream payload content. Where a duplex stream needs
//  to name the session and DataWindow it serves and the client-to-server message cannot carry them,
//  the values are read from CALL METADATA - see ColumnExpressionChannelHeaders.
//
//  NO WIRE SHAPE IS DEFINED HERE (C-A). shared/PowerFramework.Contracts owns all `.proto`
//  compilation through one `<Protobuf GrpcServices="Both">` item; this folder contains NO `.proto`
//  and the project declares NO `<Protobuf>` item, because re-declaring double-generates every type
//  (verified: 244 CS0436 diagnostics). `Grpc.AspNetCore.Server.Reflection` is deliberately absent
//  (AAP 0.5.3). Nothing here references a type from `PowerFramework.Gateway.*`,
//  `PowerFramework.Persistence.*` or `PowerFramework.Security.*`; the published contract is the
//  sole cross-service coupling. No storage provider, connection or DbContext (C-E). No key, token
//  or credential literal (C-F). No project, container, test or placeholder for DesignSystem,
//  Documents, Integration or ScriptBridge (C-D). `/health` and `/v1/ping` live in the sibling
//  `Endpoints/` folder and are deliberately not defined here (C-L); this service is gRPC on 5102.
//
//  FAIL-FAST, NEVER GRACEFUL DEGRADATION (AAP 0.1.4, 0.6.7). A structurally impossible state - an
//  unknown or expired session, a handle that is not co-resident, a blocked cross-instance foreign
//  reference, an out-of-order arrival on the synchronous macro channel - is a hard, defined error.
//  Nothing here recovers on a best-effort basis, substitutes a default, or reports success it did
//  not achieve.
//
//  ONE-BASED INDEXING IS THE SHARPEST HAZARD IN THIS REFACTOR (AAP 0.8.6 R9). PowerBuilder arrays
//  are one-based and `UpperBound` answers the LAST VALID INDEX, not a length. Every expression
//  index, variable index, relative-column position, macro `args[]` position and calculation-stack
//  position that crosses this boundary is one-based on the legacy side, and each site below states
//  its basis. A silent off-by-one here is indistinguishable from a behavioural regression, so the
//  translation is concentrated in OneBasedList<T> and MacroArgumentList rather than repeated inline;
//  the one place a basis genuinely changes - protobuf `repeated` fields are zero-based - is called
//  out where it happens.
//
//  TESTABILITY AND DETERMINISM (C-H). Every collaborator is constructor-injected, there is NO
//  STATIC MUTABLE STATE anywhere in this file, and both inverted streams are drivable by a test
//  double: MacroInvocationRouter and ExpressionTraceBroker are ordinary injected objects, so
//  PowerFramework.DataServices.Tests can exercise the macro and trace paths without a real client.
//  The clock arrives as a TimeProvider so a characterization recording is reproducible; no clock is
//  read, no GUID minted and no ordering left to chance except through an injected seam.
// ==================================================================================================

using System.Collections.Immutable;
using System.Globalization;
using System.Threading.Channels;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.Shared.Kernel;

// `PowerFramework.Contracts.DataServices.V1` publishes a generated `EventGate` MESSAGE (C-03's event
// bitmask projection) and this project publishes `Domain.EventGate`, the internal static helper that
// owns the EID_* constants. An explicit alias beats a using-directive import, so the name below is
// unambiguously the Domain one - which is the gate this file must read [se_cst_dw.sru:L41-L43].
using EventGate = PowerFramework.DataServices.Domain.EventGate;

// The generated server base. The alias exists because this class and the generated static container
// share the name `ColumnExpressionService`, so `global::` is used to make the base unambiguous
// regardless of which namespace a reader resolves first.
using GeneratedColumnExpressionServiceBase =
    global::PowerFramework.Contracts.DataServices.V1.ColumnExpressionService.ColumnExpressionServiceBase;

// `PowerFramework.Contracts.Common.V1.RetCode` is a generated MESSAGE whose nested `Types.Value` is
// the wire enum; `PowerFramework.Shared.Kernel.RetCode` is the ported constant catalogue
// [retcode.sru]. Importing both namespaces would make every bare `RetCode` ambiguous (CS0104), so
// `Common.V1` is reached through aliases only and bare `RetCode` always means the Kernel constants.
using WireAnyValue = global::PowerFramework.Contracts.Common.V1.AnyValue;
using WireDateTimeValue = global::PowerFramework.Contracts.Common.V1.DateTimeValue;
using WireDateValue = global::PowerFramework.Contracts.Common.V1.DateValue;
using WireRetCode = global::PowerFramework.Contracts.Common.V1.RetCode.Types.Value;
using WireTimeValue = global::PowerFramework.Contracts.Common.V1.TimeValue;

namespace PowerFramework.DataServices.Grpc;

/// <summary>
/// Supplies the <see cref="DataWindowServiceHost"/> that an expression engine attaches to, for one
/// named DataWindow - the composition seam this service layer owns.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS RATHER THAN A CONCRETE HOST. <c>se_cst_dw</c> derives from
/// <c>se_cst_datawindow</c> [n_cst_dwsvc_columnexp.sru's sibling, se_cst_dw.sru:L4, :L10], which lives
/// in the DEFERRED DesignSystem library. AAP 0.2.1.3 Correction 3 resolves that structural inheritance
/// edge by having DataServices define its own abstract host contract carrying only the members the
/// service layer actually consumes, and recording the legacy parent as REFERENCE-only. That abstract
/// contract is <see cref="DataWindowServiceHost"/>; BINDING IT TO SOMETHING CONCRETE IS A COMPOSITION
/// CONCERN, NOT A BEHAVIOURAL ONE, so it arrives through this interface.
/// </para>
/// <para>
/// AND WHY THE C-04 LAYER OWNS IT. <c>Program.cs</c> registers the page resolver but deliberately
/// registers no evaluator, because <c>DataWindowExpressionEvaluator</c> binds to a host, and a host is
/// PER-SESSION STATE THAT THIS SERVICE LAYER CREATES AND OWNS. Registering an evaluator there would
/// mean inventing a host lifetime the contract has not defined.
/// </para>
/// <para>
/// FAIL-FAST, NOT OPTIONAL. This is a REQUIRED dependency of
/// <see cref="ColumnExpressionService"/>: an engine cannot answer <c>Describe</c> or
/// <c>RowCount</c> without a host, so a C-04 service composed without a factory is structurally
/// faulty rather than partially functional. AAP 0.1.4 requires that posture stay fail-fast rather
/// than soften into graceful degradation, and a service that answered
/// <c>E_NO_IMPLEMENTATION</c> to every call would be exactly the softening it forbids.
/// </para>
/// </remarks>
public interface IDataWindowHostFactory
{
    /// <summary>
    /// Creates the host for one DataWindow named by the caller.
    /// </summary>
    /// <param name="dataWindowName">
    /// The caller's own name for the DataWindow, taken verbatim from
    /// <c>OpenExpressionSessionRequest.datawindow_handles</c>. It is NOT a session handle: handles are
    /// minted by the session when the engine registers, and are returned to the caller in the open
    /// response in the order requested.
    /// </param>
    /// <returns>
    /// The host, or <see langword="null"/> when this factory cannot serve the name. A null answer
    /// FAILS THE WHOLE OPEN rather than yielding a session with a hole in it - see
    /// <see cref="ColumnExpressionService.OpenExpressionSession"/>.
    /// </returns>
    DataWindowServiceHost? Create(string dataWindowName);
}

/// <summary>
/// The call-metadata header names that identify which session and DataWindow a duplex channel serves.
/// </summary>
/// <remarks>
/// <para>
/// WHY METADATA AND NOT THE STREAM PAYLOAD. <c>InvokeMethodChannel</c> is inverted, so the
/// client-to-server message is <c>InvokeMethodResponse</c> - which carries an <c>invocation_id</c>, a
/// result, an unhandled flag and an error, AND NO SESSION OR HANDLE. There is nothing in the payload
/// to identify the channel with, and C-G additionally requires that a long-lived channel's identity be
/// established before it opens rather than re-derived from what flows over it. gRPC call metadata is
/// the mechanism that satisfies both: it is present on the initial request headers, before the first
/// payload, and it is covered by the same authenticated call as everything else.
/// </para>
/// <para>
/// <c>TraceChannel</c> needs neither header: <c>TraceChannelRequest</c> carries <c>session_id</c>,
/// <c>datawindow_handle</c> and <c>subscribe</c>, so that channel is driven entirely by its request
/// stream. The asymmetry is the contract's, not this file's.
/// </para>
/// <para>
/// Header names are lower-case because gRPC normalises metadata keys to lower-case, and a lookup with
/// a mixed-case key would silently miss.
/// </para>
/// </remarks>
public static class ColumnExpressionChannelHeaders
{
    /// <summary>Names the expression session a duplex channel serves.</summary>
    public const string SessionId = "pfw-expression-session-id";

    /// <summary>
    /// Names the session-scoped DataWindow handle a duplex channel serves - the value the open
    /// response returned, not the caller's own DataWindow name.
    /// </summary>
    public const string DataWindowHandle = "pfw-expression-datawindow-handle";

    /// <summary>
    /// Reads one header, treating absent and blank identically.
    /// </summary>
    /// <param name="headers">The call's request headers.</param>
    /// <param name="key">One of the constants on this class.</param>
    /// <returns>The trimmed value, or the empty string when absent or blank.</returns>
    public static string Read(Metadata? headers, string key)
    {
        if (headers is null || string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        string? value = headers.GetValue(key);

        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}

/// <summary>
/// Routes macro invocations from an expression engine out to the client that is servicing
/// <c>InvokeMethodChannel</c> for the DataWindow being calculated - the transport half of INVERTED
/// STREAM 1.
/// </summary>
/// <remarks>
/// <para>
/// WHY ONE ROUTER PER SESSION RATHER THAN ONE CHANNEL PER ENGINE. <c>ColumnExpressionEngine</c> mints
/// its handle INSIDE its own constructor, by registering itself with the session, and that constructor
/// is also where its <see cref="MacroInvoker"/> is supplied. So at the moment a channel must be handed
/// to an engine, THE HANDLE THAT WOULD KEY IT DOES NOT EXIST YET. The resolution is to key channels by
/// SESSION and route each invocation on <see cref="MacroInvocation.DataWindowHandle"/>, which the
/// invoker fills in from the engine that raised it. One indirection, no chicken-and-egg, and no
/// mutable handle field that could be read before it is written.
/// </para>
/// <para>
/// STRICTLY SYNCHRONOUS, WITH NO SLACK (AAP 0.6.1.4 pattern (b)). At most ONE invocation may be
/// outstanding per DataWindow, because a calculation is a single depth-first walk: a nested macro
/// argument is preprocessed BEFORE the call that contains it [:L2216-L2220], so nesting is sequential
/// rather than concurrent. Every departure from that discipline is a hard error, never a buffering
/// opportunity: a second concurrent invocation, an answer naming a different invocation, and a client
/// that disconnects mid-invocation all raise
/// <see cref="MacroProtocolViolationException"/>. Letting a value computed for one macro be
/// substituted into another's expression is silent data corruption, which is the one outcome worse
/// than failing loudly.
/// </para>
/// <para>
/// NO CLIENT ATTACHED IS NOT AN ERROR HERE, AND THAT IS DELIBERATE. It answers
/// <see cref="MacroInvocationResponse.Unhandled"/>, which <see cref="MacroInvoker"/> turns into the
/// legacy's own refusal - the <c>case else</c> arm at [:L2282] for the dynamic form and [:L2306] for
/// the direct one, reached in-process whenever an unimplemented event returned a value matching no
/// accepted class. So an unserviced channel produces EXACTLY the oracle's outcome rather than a new
/// one, and no fallback macro implementation is invented (there is nothing to invent one from).
/// </para>
/// <para>
/// THREAD SAFETY. Attachment, detachment and routing are safe for concurrent use, because several
/// in-flight calls may name one session. Writes to a single response stream are serialised, because
/// gRPC forbids concurrent writes to one stream.
/// </para>
/// </remarks>
public sealed class MacroInvocationRouter
{
    private readonly Dictionary<string, MacroChannelRegistration> _attached =
        new(StringComparer.Ordinal);

    private readonly Lock _gate = new();
    private readonly ILogger? _logger;

    /// <summary>Creates a router.</summary>
    /// <param name="logger">
    /// Diagnostics. NEVER receives expression text, macro arguments or returned values: an expression
    /// can embed user data, and a log record is not the place for it.
    /// </param>
    public MacroInvocationRouter(ILogger<MacroInvocationRouter>? logger = null) => _logger = logger;

    /// <summary>The number of DataWindows currently being serviced by a client.</summary>
    public int AttachedCount
    {
        get
        {
            lock (_gate)
            {
                return _attached.Count;
            }
        }
    }

    /// <summary>
    /// The channel to hand to an engine's <see cref="MacroInvoker"/>.
    /// </summary>
    /// <param name="sessionId">The session the engine belongs to.</param>
    /// <returns>A channel that routes on the invocation's own DataWindow handle.</returns>
    /// <remarks>
    /// Cheap and stateless: the returned object holds the session identifier and a reference to this
    /// router, and resolves the attached client at invocation time rather than at creation time. That
    /// is what lets a client attach and detach freely underneath a long-lived engine.
    /// </remarks>
    public IMacroInvocationChannel ChannelFor(string sessionId) =>
        new SessionScopedChannel(this, sessionId ?? string.Empty);

    /// <summary>
    /// Registers the client that will service macro invocations for one DataWindow.
    /// </summary>
    /// <param name="sessionId">The session named in call metadata.</param>
    /// <param name="dataWindowHandle">The session-scoped handle named in call metadata.</param>
    /// <param name="responseStream">The server-to-client stream that carries the questions.</param>
    /// <returns>
    /// The registration. Disposing it detaches the client and faults any invocation still waiting,
    /// because a client that disconnects mid-invocation has answered nothing.
    /// </returns>
    /// <exception cref="ArgumentException">Either identifier is blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// A client is already servicing this DataWindow. TWO SERVICERS IS REFUSED RATHER THAN THE SECOND
    /// SILENTLY WINNING: a replacement would leave the first client's outstanding question unanswered
    /// forever while the engine waited on a stream nobody is reading.
    /// </exception>
    public MacroChannelRegistration Attach(
        string sessionId,
        string dataWindowHandle,
        IServerStreamWriter<InvokeMethodRequest> responseStream)
    {
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataWindowHandle);

        MacroChannelRegistration registration =
            new(this, sessionId, dataWindowHandle, responseStream);

        string key = BuildKey(sessionId, dataWindowHandle);

        lock (_gate)
        {
            if (_attached.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    "A macro invocation channel is already attached for DataWindow '"
                        + dataWindowHandle + "' in expression session '" + sessionId
                        + "'. Macro invocation is strictly synchronous, so a second servicer would "
                        + "leave the first client's outstanding invocation permanently unanswered.");
            }

            _attached.Add(key, registration);
        }

        _logger?.LogDebug(
            "Macro invocation channel attached for session {SessionId} DataWindow {Handle}.",
            sessionId,
            dataWindowHandle);

        return registration;
    }

    /// <summary>
    /// Detaches one registration, if it is still the current one for its DataWindow.
    /// </summary>
    /// <param name="registration">The registration being disposed.</param>
    internal void Detach(MacroChannelRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        string key = BuildKey(registration.SessionId, registration.DataWindowHandle);

        lock (_gate)
        {
            // Reference equality, so a stale disposal cannot evict a newer client that legitimately
            // attached after this one had already gone.
            if (_attached.TryGetValue(key, out MacroChannelRegistration? current)
                && ReferenceEquals(current, registration))
            {
                _attached.Remove(key);
            }
        }

        _logger?.LogDebug(
            "Macro invocation channel detached for session {SessionId} DataWindow {Handle}.",
            registration.SessionId,
            registration.DataWindowHandle);
    }

    /// <summary>
    /// Resolves the client servicing one DataWindow, or <see langword="null"/> when none is.
    /// </summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="dataWindowHandle">The handle.</param>
    /// <returns>The registration, or <see langword="null"/>.</returns>
    internal MacroChannelRegistration? Find(string sessionId, string dataWindowHandle)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(dataWindowHandle))
        {
            return null;
        }

        lock (_gate)
        {
            return _attached.GetValueOrDefault(BuildKey(sessionId, dataWindowHandle));
        }
    }

    // A single-string key rather than a tuple, because both components are opaque identifiers and a
    // separator that cannot occur in a minted handle keeps the mapping injective. Session-minted
    // handles are of the form "<sessionId>/<ordinal>" [ExpressionSession.Register], so the newline is
    // chosen deliberately: it appears in neither component.
    private static string BuildKey(string sessionId, string dataWindowHandle) =>
        sessionId + "\n" + dataWindowHandle;

    /// <summary>
    /// The channel an engine's <see cref="MacroInvoker"/> holds: session-scoped, routing each
    /// invocation on the handle the invoker stamps onto it.
    /// </summary>
    private sealed class SessionScopedChannel(MacroInvocationRouter router, string sessionId)
        : IMacroInvocationChannel
    {
        public ValueTask<MacroInvocationResponse> InvokeAsync(
            MacroInvocation invocation,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(invocation);

            // The handle the invoker stamped on, which is the engine's own. An engine constructed
            // outside a service session carries an empty handle and therefore has no client - which
            // answers Unhandled below, the same outcome as an unserviced channel.
            string handle = invocation.DataWindowHandle ?? string.Empty;

            MacroChannelRegistration? registration = router.Find(sessionId, handle);

            if (registration is null)
            {
                // [:L2282] / [:L2306] - the oracle's own refusal for a value that matched no accepted
                // class, which is what an unimplemented OnColumnExpInvokeMethod produced in-process.
                return ValueTask.FromResult(
                    new MacroInvocationResponse
                    {
                        InvocationId = invocation.InvocationId,
                        Sequence = invocation.Sequence,
                        Unhandled = true,
                    });
            }

            return registration.AskAsync(invocation, cancellationToken);
        }
    }
}

/// <summary>
/// One client's servicing of <c>InvokeMethodChannel</c> for one DataWindow: the rendezvous between the
/// engine asking a question and the client answering it.
/// </summary>
/// <remarks>
/// <para>
/// A SINGLE PENDING SLOT, NOT A QUEUE, because the discipline is strictly synchronous. Every way of
/// breaking that discipline is detected here and raised as
/// <see cref="MacroProtocolViolationException"/>: a second invocation while one is outstanding, an
/// answer that arrives when nothing was asked, and disposal while a question is unanswered. The
/// mismatched-identifier case is deliberately NOT detected here - the answer is handed up so that
/// <see cref="MacroInvoker"/> can raise the violation with the full expected-versus-actual detail and
/// its own late-versus-out-of-order discrimination.
/// </para>
/// <para>
/// CANCELLATION IS HONOURED AND TIMEOUT IS NOT IMPOSED. The wait ends when the client answers, when the
/// call is cancelled, or when the channel is torn down - and on nothing else. No invocation timeout is
/// invented, because the contract states the consequence plainly: A CLIENT THAT DOES NOT SERVICE THIS
/// CHANNEL STALLS ITS OWN CALCULATIONS, and there is no fallback macro implementation to fall back to.
/// The bound is the RPC's own cancellation token, which is the caller's to set.
/// </para>
/// </remarks>
public sealed class MacroChannelRegistration : IDisposable
{
    private readonly MacroInvocationRouter _router;
    private readonly IServerStreamWriter<InvokeMethodRequest> _responseStream;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Lock _pendingGate = new();

    private PendingInvocation? _pending;
    private bool _disposed;

    internal MacroChannelRegistration(
        MacroInvocationRouter router,
        string sessionId,
        string dataWindowHandle,
        IServerStreamWriter<InvokeMethodRequest> responseStream)
    {
        _router = router;
        SessionId = sessionId;
        DataWindowHandle = dataWindowHandle;
        _responseStream = responseStream;
    }

    /// <summary>The expression session this channel serves.</summary>
    public string SessionId { get; }

    /// <summary>The session-scoped DataWindow handle this channel serves.</summary>
    public string DataWindowHandle { get; }

    /// <summary>Whether an invocation is currently awaiting an answer.</summary>
    public bool HasOutstandingInvocation
    {
        get
        {
            lock (_pendingGate)
            {
                return _pending is not null;
            }
        }
    }

    /// <summary>
    /// Asks the client to evaluate one macro and waits for its answer.
    /// </summary>
    /// <param name="invocation">The macro, its name already resolved and its arguments already shifted.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The client's answer, never <see langword="null"/>.</returns>
    /// <exception cref="MacroProtocolViolationException">
    /// An invocation was already outstanding, or the channel was torn down before an answer arrived.
    /// </exception>
    internal async ValueTask<MacroInvocationResponse> AskAsync(
        MacroInvocation invocation,
        CancellationToken cancellationToken)
    {
        PendingInvocation pending = new(invocation.InvocationId);

        lock (_pendingGate)
        {
            if (_disposed)
            {
                throw new MacroProtocolViolationException(
                    "The macro invocation channel for DataWindow '" + DataWindowHandle
                        + "' has been torn down, so invocation '" + invocation.InvocationId
                        + "' cannot be asked.",
                    invocation.InvocationId,
                    invocationId: null,
                    late: false);
            }

            if (_pending is { } outstanding)
            {
                throw new MacroProtocolViolationException(
                    "Invocation '" + invocation.InvocationId
                        + "' was issued while '" + outstanding.InvocationId
                        + "' was still outstanding on the same DataWindow. Macro invocation is "
                        + "strictly synchronous: a calculation is one depth-first walk, so a nested "
                        + "macro argument is preprocessed BEFORE the call containing it and never "
                        + "alongside it.",
                    outstanding.InvocationId,
                    invocation.InvocationId,
                    late: false);
            }

            _pending = pending;
        }

        try
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // gRPC forbids concurrent writes to one stream. The single-slot discipline already
                // serialises them; the gate makes that independent of the discipline holding.
                await _responseStream
                    .WriteAsync(ExpressionWireProjection.ToWire(invocation, SessionId))
                    .ConfigureAwait(false);
            }
            finally
            {
                _ = _writeGate.Release();
            }

            using CancellationTokenRegistration registration = cancellationToken.Register(
                static state => ((PendingInvocation)state!).Cancel(),
                pending);

            return await pending.Completion.ConfigureAwait(false);
        }
        finally
        {
            lock (_pendingGate)
            {
                if (ReferenceEquals(_pending, pending))
                {
                    _pending = null;
                }
            }
        }
    }

    /// <summary>
    /// Delivers one answer read from the client's stream.
    /// </summary>
    /// <param name="answer">The message the client sent.</param>
    /// <exception cref="MacroProtocolViolationException">
    /// Nothing was outstanding. An UNSOLICITED ANSWER IS A HARD ERROR, not something to discard: it
    /// means the client's notion of what is outstanding has diverged from the server's, and the next
    /// answer it sends could be substituted into the wrong expression.
    /// </exception>
    internal void Deliver(InvokeMethodResponse answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        PendingInvocation? pending;

        lock (_pendingGate)
        {
            pending = _pending;
        }

        if (pending is null)
        {
            throw new MacroProtocolViolationException(
                "The macro invocation channel for DataWindow '" + DataWindowHandle
                    + "' answered invocation '" + answer.InvocationId
                    + "' when none was outstanding.",
                expectedInvocationId: null,
                answer.InvocationId,
                late: true);
        }

        // The identifier is deliberately NOT compared here - MacroInvoker.ValidateCorrelation raises the
        // out-of-order and late violations with the full detail, and duplicating the test here would put
        // two authorities on one rule.
        pending.Complete(ExpressionWireProjection.FromWire(answer));
    }

    /// <summary>
    /// Detaches this channel and fails any invocation still waiting.
    /// </summary>
    public void Dispose()
    {
        PendingInvocation? pending;

        lock (_pendingGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pending = _pending;
            _pending = null;
        }

        _router.Detach(this);

        // "Answered nothing at all" is one of the violations the synchronous discipline names, and a
        // client that walks away mid-invocation has done exactly that. Faulting the wait is what stops
        // the calculation from hanging on a stream nobody is reading any more.
        pending?.Fault(
            new MacroProtocolViolationException(
                "The macro invocation channel for DataWindow '" + DataWindowHandle
                    + "' closed while invocation '" + pending.InvocationId
                    + "' was outstanding, so the macro was never answered.",
                pending.InvocationId,
                invocationId: null,
                late: false));

        _writeGate.Dispose();
    }

    /// <summary>One outstanding question and the completion the reader loop resolves it through.</summary>
    private sealed class PendingInvocation(string invocationId)
    {
        private readonly TaskCompletionSource<MacroInvocationResponse> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal string InvocationId { get; } = invocationId;

        internal Task<MacroInvocationResponse> Completion => _completion.Task;

        internal void Complete(MacroInvocationResponse response) =>
            _completion.TrySetResult(response);

        internal void Fault(Exception exception) => _completion.TrySetException(exception);

        // MacroInvoker translates the resulting OperationCanceledException into ForCancelled or
        // ForTimedOut, so cancellation must surface as cancellation rather than as a fabricated answer.
        internal void Cancel() => _completion.TrySetCanceled();
    }
}

/// <summary>
/// The projections between this service's in-process types and the C-04 wire messages.
/// </summary>
/// <remarks>
/// <para>
/// EVERY CONVERSION IN ONE PLACE, DELIBERATELY. The mapping is where a refactor of this shape loses
/// fidelity - a dropped array, a collapsed null, an index whose basis changed silently - so it is
/// concentrated here rather than spread across twenty-seven RPC bodies, and each narrowing is stated
/// at the member that performs it rather than in a summary somewhere else.
/// </para>
/// <para>
/// PURE AND STATIC, holding no state of any kind, so it is directly testable and so nothing in it can
/// make a recording irreproducible.
/// </para>
/// </remarks>
internal static class ExpressionWireProjection
{
    /// <summary>
    /// Projects a ported <see cref="RetCode"/> constant onto the wire enum.
    /// </summary>
    /// <param name="code">A value from <see cref="RetCode"/>.</param>
    /// <returns>The wire value.</returns>
    /// <remarks>
    /// A CAST IS CORRECT HERE AND IS NOT A SHORTCUT. <c>common.v1.RetCode.Value</c> is generated from a
    /// proto enum whose members cite <c>retcode.sru</c> line by line, so the two catalogues carry
    /// IDENTICAL NUMERIC VALUES by construction; the whole point of preserving the identifiers
    /// (AAP 0.4.5.3) is that the numbers travel unchanged. Every ported constant fits in
    /// <see cref="int"/> - the widest magnitude is <c>UNKNOWN = -4000</c> - and protobuf permits an
    /// unrecognised numeric value to travel as itself, so a code this file has never seen still arrives
    /// intact rather than being folded onto a default.
    /// </remarks>
    internal static WireRetCode ToWireRetCode(long code) => (WireRetCode)(int)code;

    /// <summary>Builds the grammar sentinels, read from the types that own them.</summary>
    /// <returns>The sentinel block for <c>GetExpressionState</c>.</returns>
    /// <remarks>
    /// READ, NEVER RESTATED. Every value comes from the constant that defines it, so the wire cannot
    /// drift from the engine: <c>FUNC_VAR = ""</c> and <c>FUNC_INVOKE = "Invoke"</c> [:L120-L121],
    /// <c>DWOSUFFIX = "_columnexp"</c> [:L95], and the two grammar sigils. The SCREAMING_SNAKE
    /// spellings are preserved on those constants because they appear in serialized payloads, log
    /// records and characterization recordings (AAP 0.4.5.3).
    /// </remarks>
    internal static ExpressionSentinels Sentinels() =>
        new()
        {
            FuncVar = MacroSentinels.FUNC_VAR,
            FuncInvoke = MacroSentinels.FUNC_INVOKE,
            DwoSuffix = ColumnExpressionEngine.DWOSUFFIX,
            MacroFlag = ColumnExpressionEngine.MACRO_FLAG,
            MacroContext = ColumnExpressionEngine.MACRO_CONTEXT,
        };

    /// <summary>
    /// Projects a DataWindow column object onto its wire reference.
    /// </summary>
    /// <param name="dwo">The object, or <see langword="null"/>.</param>
    /// <returns>The reference; an empty one when <paramref name="dwo"/> is <see langword="null"/>.</returns>
    /// <remarks>
    /// <c>dwobject dwo</c> [:L25] is a live object at source. <c>ID</c> is the ONE-BASED DataWindow
    /// ordinal and is read through the ported <c>ColumnId()</c> projection rather than cast here, because
    /// the oracle's identifier arrives as an <c>any</c> and the coercion is that projection's job.
    /// <c>col_type_prefix</c> is left unspecified: it belongs to the item-change coercion switch
    /// [se_cst_dw.sru:L228-L243], which is C-03's, and inferring it here would put a second authority on
    /// that switch.
    /// </remarks>
    internal static DwObjectRef ToWire(IDataWindowObject? dwo) =>
        dwo is null
            ? new DwObjectRef()
            : new DwObjectRef
            {
                Name = dwo.Name ?? string.Empty,
                Id = dwo.ColumnId() ?? 0L,
                ColType = dwo.ColType ?? string.Empty,
            };

    /// <summary>
    /// Projects a macro invocation onto the question that travels to the client.
    /// </summary>
    /// <param name="invocation">The invocation the invoker built.</param>
    /// <param name="sessionId">The session, which the channel knows and the invocation may not.</param>
    /// <returns>The wire message.</returns>
    /// <remarks>
    /// <para>
    /// THE ARGUMENT LIST CHANGES BASIS HERE, AND THIS IS THE ONLY PLACE IT DOES.
    /// <see cref="MacroArgumentList"/> is ONE-BASED, faithfully to
    /// <c>args[1]</c> being the first argument in the documented handler
    /// [docs/n_cst_dwsvc_columnexp.md:L126]; a protobuf <c>repeated</c> field is ZERO-BASED. The
    /// conversion is a straight enumeration in order, so wire element 0 IS legacy <c>args[1]</c>, and
    /// the contract states that basis on the field itself so a client cannot mistake it.
    /// </para>
    /// <para>
    /// THE NAME IS ALREADY RESOLVED AND THE LIST IS ALREADY SHIFTED for both forms. For
    /// <c>$$Invoke</c> the legacy takes the function name from <c>sArgs[1]</c> and renumbers the rest
    /// down by one [:L2259-L2262]; the dispatch plan did that before this point, so a client's handler
    /// sees the ACTUAL arguments at the same positions whichever form invoked it.
    /// </para>
    /// </remarks>
    internal static InvokeMethodRequest ToWire(MacroInvocation invocation, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        InvokeMethodRequest request = new()
        {
            SessionId = sessionId ?? string.Empty,
            DatawindowHandle = invocation.DataWindowHandle ?? string.Empty,
            InvocationId = invocation.InvocationId,
            Row = invocation.Row,
            Dwo = ToWire(invocation.Dwo),
            Name = invocation.Name,
            Form = invocation.Form,
            Builtin = invocation.Builtin,
        };

        // One-based 1..UpperBound in order, landing at zero-based 0..n-1 on the wire.
        for (int oneBasedIndex = 1; oneBasedIndex <= invocation.Arguments.UpperBound; oneBasedIndex++)
        {
            request.Args.Add(invocation.Arguments[oneBasedIndex]);
        }

        return request;
    }

    /// <summary>
    /// Projects the client's answer onto the invoker's response type.
    /// </summary>
    /// <param name="answer">The message the client sent.</param>
    /// <returns>The response the invoker validates and renders.</returns>
    /// <remarks>
    /// <para>
    /// A CLIENT-SIDE REJECTION AND AN UNRENDERABLE VALUE REACH THE SAME PLACE, WHICH IS THE ORACLE'S
    /// PLACE. <c>MacroResult.rejected</c> means the handler ran and produced something unusable; it is
    /// projected as a null value so that <c>MacroInvoker.TryRender</c> refuses it through the very arm
    /// the legacy refuses it through - <c>case else</c> at [:L2282] for the dynamic form and [:L2306]
    /// for the direct one. Inventing a distinct outcome would add a failure mode the oracle does not
    /// have.
    /// </para>
    /// <para>
    /// NO SEQUENCE IS ECHOED, because <c>InvokeMethodResponse</c> carries none. The invoker treats an
    /// absent echo as "not asserted" and validates the invocation identifier instead, which is the
    /// correlation the contract actually requires.
    /// </para>
    /// </remarks>
    internal static MacroInvocationResponse FromWire(InvokeMethodResponse answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        bool rejected = answer.Result is { Rejected: true };

        return new MacroInvocationResponse
        {
            InvocationId = answer.InvocationId,
            Unhandled = answer.Unhandled,
            Value = rejected ? null : FromWire(answer.Result?.Value),
        };
    }

    /// <summary>
    /// Projects an <c>any</c> value off the wire.
    /// </summary>
    /// <param name="value">The wire value, or <see langword="null"/>.</param>
    /// <returns>
    /// The CLR value, whose runtime type is one <c>MacroInvoker.ProjectClassName</c> recognises.
    /// </returns>
    /// <remarks>
    /// <para>
    /// AN EXPLICIT NULL AND AN UNSET ONEOF BOTH YIELD NULL, AND THAT IS NOT A COLLAPSE. The contract
    /// distinguishes them - <c>is_null</c> is a positive statement, an unset <c>kind</c> means nothing
    /// was supplied - but PowerScript's own <c>ClassName(aVal)</c> cannot tell them apart either: a
    /// null <c>any</c> carries no type, so both reach the same <c>case else</c> refusal at [:L2282] and
    /// [:L2306]. Preserving a distinction the oracle does not observe would be the invention, not the
    /// fidelity.
    /// </para>
    /// <para>
    /// A BLOB IS PROJECTED FAITHFULLY AND THEN REFUSED, which is correct: <c>blob</c> is not among the
    /// twelve accepted return classes [:L2265-L2281], so it must reach the refusal AS a blob rather
    /// than be dropped before the switch that names the offending type.
    /// </para>
    /// </remarks>
    internal static object? FromWire(WireAnyValue? value) =>
        value?.KindCase switch
        {
            null => null,
            WireAnyValue.KindOneofCase.None => null,
            WireAnyValue.KindOneofCase.IsNull => null,
            WireAnyValue.KindOneofCase.BoolValue => value.BoolValue,
            WireAnyValue.KindOneofCase.Int64Value => value.Int64Value,
            WireAnyValue.KindOneofCase.Uint64Value => value.Uint64Value,
            WireAnyValue.KindOneofCase.DoubleValue => value.DoubleValue,
            WireAnyValue.KindOneofCase.DecimalValue => ParseDecimal(value.DecimalValue?.Value),
            WireAnyValue.KindOneofCase.DateValue => ParseDate(value.DateValue?.Value),
            WireAnyValue.KindOneofCase.TimeValue => ParseTime(value.TimeValue?.Value),
            WireAnyValue.KindOneofCase.DatetimeValue => ParseDateTime(value.DatetimeValue?.Value),
            WireAnyValue.KindOneofCase.StringValue => value.StringValue,
            WireAnyValue.KindOneofCase.BlobValue => value.BlobValue.ToByteArray(),
            _ => null,
        };

    /// <summary>
    /// Projects a CLR value onto the wire <c>any</c>.
    /// </summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <returns>The wire value; <c>is_null</c> set when <paramref name="value"/> is null.</returns>
    /// <remarks>
    /// NULL BECOMES <c>is_null = true</c>, NEVER AN UNSET ONEOF AND NEVER THE EMPTY STRING. The
    /// contract is explicit that <c>is_null = true</c> and <c>string_value = ""</c> are different
    /// values, and the standing rule is never to collapse null to a zero-equivalent, because doing so
    /// converts "neither succeeded nor failed" into "succeeded".
    /// </remarks>
    internal static WireAnyValue ToWire(object? value) =>
        value switch
        {
            null => new WireAnyValue { IsNull = true },
            bool boolean => new WireAnyValue { BoolValue = boolean },
            string text => new WireAnyValue { StringValue = text },
            short integer => new WireAnyValue { Int64Value = integer },
            int integer => new WireAnyValue { Int64Value = integer },
            long integer => new WireAnyValue { Int64Value = integer },
            ushort unsigned => new WireAnyValue { Uint64Value = unsigned },
            uint unsigned => new WireAnyValue { Uint64Value = unsigned },
            ulong unsigned => new WireAnyValue { Uint64Value = unsigned },
            float real => new WireAnyValue { DoubleValue = real },
            double real => new WireAnyValue { DoubleValue = real },
            decimal number => new WireAnyValue
            {
                DecimalValue = new global::PowerFramework.Contracts.Common.V1.DecimalValue
                {
                    Value = number.ToString(CultureInfo.InvariantCulture),
                },
            },
            DateTime moment => new WireAnyValue { DatetimeValue = ToWire(moment) },
            DateOnly day => new WireAnyValue { DateValue = ToWire(day) },
            TimeOnly clock => new WireAnyValue { TimeValue = ToWire(clock) },
            byte[] blob => new WireAnyValue { BlobValue = ByteString.CopyFrom(blob) },

            // Anything else is a type the oracle's twelve accepted classes do not name, so it travels as
            // its own rendering and is refused downstream by name rather than being silently dropped.
            _ => new WireAnyValue
            {
                StringValue = System.Convert.ToString(value, CultureInfo.InvariantCulture)
                    ?? string.Empty,
            },
        };

    // ==============================================================================================
    //  THE THREE TEMPORAL MESSAGES
    //  --------------------------------------------------------------------------------------------
    //  THREE MESSAGES AND NOT ONE TIMESTAMP, because the legacy has three distinct types - `date`,
    //  `time` and `datetime` - and NO TIME-ZONE CONCEPT AT ALL. The canonical forms are the
    //  contract's own: "yyyy-MM-dd"; "HH:mm:ss" with a fractional part present only when non-zero;
    //  and "yyyy-MM-ddTHH:mm:ss" with the same fractional rule, UNZONED. Emission is always
    //  canonical. Parsing additionally accepts the space-separated datetime form, because that is
    //  what PowerScript's own `String(datetime)` produces and a client echoing an oracle recording
    //  would otherwise be refused for a difference the oracle does not make.
    // ==============================================================================================

    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm:ss";
    private const string TimeFractionFormat = "HH:mm:ss.ffffff";
    private const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss";
    private const string DateTimeFractionFormat = "yyyy-MM-ddTHH:mm:ss.ffffff";

    private static readonly string[] TimeFormats = [TimeFormat, TimeFractionFormat, "HH:mm"];

    private static readonly string[] DateTimeFormats =
    [
        DateTimeFormat,
        DateTimeFractionFormat,
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.ffffff",
        DateFormat,
    ];

    /// <summary>Renders a date in the contract's canonical form.</summary>
    /// <param name="value">The date.</param>
    /// <returns>The wire message.</returns>
    internal static WireDateValue ToWire(DateOnly value) =>
        new() { Value = value.ToString(DateFormat, CultureInfo.InvariantCulture) };

    /// <summary>Renders a time of day, including a fractional part only when it is non-zero.</summary>
    /// <param name="value">The time.</param>
    /// <returns>The wire message.</returns>
    internal static WireTimeValue ToWire(TimeOnly value) =>
        new()
        {
            Value = value.Ticks % TimeSpan.TicksPerSecond == 0
                ? value.ToString(TimeFormat, CultureInfo.InvariantCulture)
                : value.ToString(TimeFractionFormat, CultureInfo.InvariantCulture),
        };

    /// <summary>Renders a date and time, unzoned, with a fractional part only when non-zero.</summary>
    /// <param name="value">The moment.</param>
    /// <returns>The wire message.</returns>
    internal static WireDateTimeValue ToWire(DateTime value) =>
        new()
        {
            Value = value.Ticks % TimeSpan.TicksPerSecond == 0
                ? value.ToString(DateTimeFormat, CultureInfo.InvariantCulture)
                : value.ToString(DateTimeFractionFormat, CultureInfo.InvariantCulture),
        };

    /// <summary>Parses a canonical date, answering null when the text is absent or malformed.</summary>
    /// <param name="value">The wire text.</param>
    /// <returns>The date, or <see langword="null"/>.</returns>
    /// <remarks>
    /// NULL MEANS "NOT A DATE", AND THE CALLER MUST DECIDE WHAT THAT IS. It is never turned into a
    /// default date here: <c>DateValidator.InvalidDateSentinel</c> exists for the oracle's own coercion
    /// failure and is that path's business, not this one's.
    /// </remarks>
    internal static DateOnly? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateOnly.TryParseExact(
                value,
                DateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly parsed)
                ? parsed
                : null;

    /// <summary>Parses a canonical time of day.</summary>
    /// <param name="value">The wire text.</param>
    /// <returns>The time, or <see langword="null"/>.</returns>
    internal static TimeOnly? ParseTime(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : TimeOnly.TryParseExact(
                value,
                TimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly parsed)
                ? parsed
                : null;

    /// <summary>Parses a canonical, unzoned date and time.</summary>
    /// <param name="value">The wire text.</param>
    /// <returns>The moment, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <see cref="DateTimeStyles.None"/> keeps the value unzoned and unconverted, which matches a
    /// legacy that has no time-zone concept: adopting the host's zone would invent information the
    /// oracle does not have.
    /// </remarks>
    internal static DateTime? ParseDateTime(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateTime.TryParseExact(
                value,
                DateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed)
                ? parsed
                : null;

    /// <summary>Parses an invariant-culture decimal.</summary>
    /// <param name="value">The wire text.</param>
    /// <returns>The number, or <see langword="null"/>.</returns>
    internal static decimal? ParseDecimal(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : decimal.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out decimal parsed)
                ? parsed
                : null;

    // ==============================================================================================
    //  THE 28 ERROR SITES, AS STRUCTURED RESULTS
    //  --------------------------------------------------------------------------------------------
    //  ONLY THE DELIVERY CHANNEL CHANGES. `n_cst_dwsvc_columnexp.sru` contains 28 LIVE MessageBox
    //  sites and zero commented-out ones. Each becomes a structured error preserving the message
    //  text, the title, the severity (StopSign!) and - for a parse failure - the expression plus the
    //  caret position and the legacy's fully rendered marker.
    //
    //  THE INCONSISTENCY IS PRESERVED, NOT HARMONIZED (C-B). All 28 are HARDCODED CHINESE carrying
    //  the literal title "错误", and NOT ONE of them routes through I18N - unlike the dwsvc,
    //  rowselect and contextmenu messages, which do go through I18n/Categories.CAT_DWSVC. That
    //  asymmetry is legacy behaviour. Nothing here localizes, and `localized` therefore arrives false
    //  from the formatter for every one of them; "fixing" the inconsistency would be exactly the
    //  silent correction of a legacy defect that C-B forbids.
    //
    //  TWO CARET CONVENTIONS TRAVEL, BOTH OF THEM. Ten sites pass the integer-divided midpoint
    //  `nMacPos + (nPos - nMacPos) / 2` [:L1308, :L1383, :L1390, :L1395, :L1399, :L1417, :L1422,
    //  :L1426, :L1431, :L1486] and the unclosed-quote site passes `nQuotePos` [:L1505]. The position
    //  is ONE-BASED and measured in the legacy's own terms, and the marker is rendered by
    //  ParseErrorFormatter in DBCS byte width (LenA) rather than UTF-16 char count - so nothing is
    //  recomputed here. The rendered marker is carried as the formatter produced it.
    // ==============================================================================================

    /// <summary>
    /// Projects a structured expression error onto the wire.
    /// </summary>
    /// <param name="error">The error, or <see langword="null"/>.</param>
    /// <returns>The wire error, or <see langword="null"/> when there was none.</returns>
    /// <remarks>
    /// <c>ret_code</c> IS SET ONLY WHEN THE LEGACY SITE PRODUCED ONE. Most of the 28 sites answer
    /// <c>false</c> or the empty string rather than a framework code, and
    /// <c>common.v1.RetCode.Value</c> has no explicit presence - so an unset field means "no code",
    /// which is why a consumer branches on the error's presence and on the specific code, never on a
    /// two-way success test over a defaulted zero.
    /// </remarks>
    internal static ExpressionError? ToWire(ExpressionParseError? error)
    {
        if (error is null)
        {
            return null;
        }

        StructuredError structured = new()
        {
            Text = error.Text,

            // Passed through, never forced: the formatter reports what the oracle did, and for these
            // 28 sites what it did was not localize.
            Localized = error.Localized,
            Category = error.LocalizationCategory,
            Severity = ToWire(error.Severity),
            Title = error.Title,
        };

        if (error.ReturnCode is { } code)
        {
            structured.RetCode = ToWireRetCode(code);
        }

        structured.FormatArgs.AddRange(error.FormatArguments);

        return new ExpressionError
        {
            Error = structured,
            Expression = error.Expression ?? string.Empty,

            // ONE-BASED, in the legacy's own units. Absent means the site is a plain message with no
            // position at all, which is a real distinction: a plain message has no caret to place.
            CaretPosition = error.CaretPosition ?? 0L,
            RenderedMarker = error.RenderedMarker ?? string.Empty,
            Category = ToWire(error.Category),
            CaughtExceptionText = error.CaughtExceptionText ?? string.Empty,
        };
    }

    /// <summary>Projects the dialog severity onto the wire enum.</summary>
    /// <param name="severity">The formatter's severity.</param>
    /// <returns>The wire severity.</returns>
    /// <remarks>
    /// An explicit switch rather than a cast. The two enumerations are numbered identically on
    /// purpose, and writing the mapping out is what keeps that a checked fact rather than a silent
    /// assumption if either gains a member.
    /// </remarks>
    internal static Severity ToWire(ExpressionErrorSeverity severity) =>
        severity switch
        {
            ExpressionErrorSeverity.None => Severity.None,
            ExpressionErrorSeverity.Information => Severity.Information,
            ExpressionErrorSeverity.Question => Severity.Question,
            ExpressionErrorSeverity.Exclamation => Severity.Exclamation,
            ExpressionErrorSeverity.StopSign => Severity.StopSign,
            _ => Severity.Unspecified,
        };

    /// <summary>Projects the error category onto the wire enum.</summary>
    /// <param name="category">The formatter's category.</param>
    /// <returns>The wire category.</returns>
    internal static ExpressionError.Types.Category ToWire(ExpressionErrorCategory category) =>
        category switch
        {
            ExpressionErrorCategory.CacheCreation => ExpressionError.Types.Category.CacheCreation,
            ExpressionErrorCategory.CacheError => ExpressionError.Types.Category.CacheError,
            ExpressionErrorCategory.CacheUpdate => ExpressionError.Types.Category.CacheUpdate,
            ExpressionErrorCategory.Expression => ExpressionError.Types.Category.Expression,
            ExpressionErrorCategory.Parse => ExpressionError.Types.Category.Parse,
            ExpressionErrorCategory.Preprocess => ExpressionError.Types.Category.Preprocess,
            ExpressionErrorCategory.UndefinedName => ExpressionError.Types.Category.UndefinedName,
            ExpressionErrorCategory.InvalidValue => ExpressionError.Types.Category.InvalidValue,
            ExpressionErrorCategory.MacroUnsupported =>
                ExpressionError.Types.Category.MacroUnsupported,
            ExpressionErrorCategory.DuplicateDefinition =>
                ExpressionError.Types.Category.DuplicateDefinition,
            ExpressionErrorCategory.ForeignReferenceBlocked =>
                ExpressionError.Types.Category.ForeignReferenceBlocked,
            _ => ExpressionError.Types.Category.Unspecified,
        };

    // ==============================================================================================
    //  THE SEVEN LEGACY STRUCTURES [:L22-L83]
    //  --------------------------------------------------------------------------------------------
    //  THEY ARE THE CONTRACT, so every field travels. Two of them are the ones a summary loses:
    //  `columnexpdata.dupexps[]` [:L32] is the mechanism behind MULTIPLE EXPRESSIONS PER COLUMN
    //  selected by which column changed, and `columndata.indexes[]` / `varindexes[]` [:L46-L47] form
    //  the REVERSE DEPENDENCY INDEX from a column to the expressions and variables depending on it -
    //  the graph that makes dirty-propagation work. Neither may be dropped as an implementation
    //  detail, and neither is.
    //
    //  EVERY ONE OF THESE ARRAYS IS ONE-BASED AT SOURCE and arrives here as a OneBasedList<T>, whose
    //  `Items` projection preserves order 1..UpperBound. A protobuf `repeated` field is zero-based,
    //  so element 0 on the wire is legacy element 1 throughout - a basis change, not a reordering,
    //  and the contract states it on the fields that carry an index rather than a value.
    // ==============================================================================================

    /// <summary>Projects one bound expression onto the wire.</summary>
    /// <param name="data">The engine's expression record.</param>
    /// <returns>The wire message.</returns>
    /// <remarks>
    /// <c>exp</c> carries the STORED text, which is the parser's IN-PLACE REWRITE [:L1435] and
    /// therefore has every static reference already substituted away. That is exactly why
    /// <see cref="ToWire(ExpressionBindingSnapshot, ExpressionVariableEnvironment)"/> exists and why
    /// <c>GetExpressionState</c> can carry bindings alongside this: the stored text alone cannot tell a
    /// static binding from a literal.
    /// </remarks>
    internal static ColumnExpData ToWire(ColumnExpressionData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        ColumnExpData projected = new()
        {
            Name = data.Name,

            // ONE-BASED DataWindow column ordinal, read from Describe(colname + ".ID") [:L1541].
            Id = data.Id,
            Dwo = ToWire(data.Dwo),
            ColType = ToWireColType(data.ColType),
            Exp = data.Exp,
            EmptyStringIsNull = data.EmptyStringIsNull,
            HasMacro = data.HasMacro,
            AlwaysCalc = data.AlwaysCalc,
            TriggerEvent = data.TriggerEvent,
            Recursive = data.Recursive,
            Dirty = data.Dirty,
            Empty = data.Empty,
            Cacheable = data.Cacheable,
            ComputeName = data.ComputeName,
        };

        foreach (VariableReference variable in data.Vars.Items)
        {
            projected.Vars.Add(ToWire(variable));
        }

        foreach (FunctionReference function in data.Fns.Items)
        {
            projected.Fns.Add(ToWire(function));
        }

        projected.RelativeColIds.AddRange(data.RelativeColIds.Items);
        projected.RelativeInputColIds.AddRange(data.RelativeInputColIds.Items);

        // [:L32] The other expression indexes bound to the same column, selected by which column
        // changed. Dropping this would silently reduce multi-expression columns to their first.
        projected.DupExps.AddRange(data.DupExps.Items);

        return projected;
    }

    /// <summary>Projects the per-column calculation state and its reverse dependency index.</summary>
    /// <param name="data">The engine's column record.</param>
    /// <returns>The wire message.</returns>
    internal static ColumnData ToWire(ColumnCalcData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        ColumnData projected = new() { Flag = ToWire(data.Flag) };

        // [:L46-L47] ONE-BASED expression and variable indexes. This IS the dirty-propagation graph.
        projected.Indexes.AddRange(data.Indexes.Items);
        projected.VarIndexes.AddRange(data.VarIndexes.Items);

        return projected;
    }

    /// <summary>Projects the tri-state calculation flag [:L113-L115].</summary>
    /// <param name="flag">The engine's flag.</param>
    /// <returns>The wire flag.</returns>
    /// <remarks>
    /// TRI-STATE AND NOT BOOLEAN. <c>CLC_UNKNOWN</c> is not "no": the entry point at [:L221] SKIPS a
    /// column entirely on <c>CLC_NO</c> but EVALUATES the reverse index on <c>CLC_UNKNOWN</c>, so
    /// collapsing the two would change which columns are walked.
    /// </remarks>
    internal static ColumnData.Types.CalcFlag ToWire(ColumnCalcFlag flag) =>
        flag switch
        {
            ColumnCalcFlag.CLC_YES => ColumnData.Types.CalcFlag.ClcYes,
            ColumnCalcFlag.CLC_NO => ColumnData.Types.CalcFlag.ClcNo,
            _ => ColumnData.Types.CalcFlag.ClcUnknown,
        };

    /// <summary>Projects a column type [n_cst_dwsvc.sru:L26-L32].</summary>
    /// <param name="colType">The engine's numeric column type.</param>
    /// <returns>The wire type.</returns>
    internal static ColumnExpData.Types.ColType ToWireColType(long colType) =>
        colType switch
        {
            DataWindowServiceBase.COL_TYPE_STRING => ColumnExpData.Types.ColType.String,
            DataWindowServiceBase.COL_TYPE_INTEGER => ColumnExpData.Types.ColType.Integer,
            DataWindowServiceBase.COL_TYPE_DECIMAL => ColumnExpData.Types.ColType.Decimal,
            DataWindowServiceBase.COL_TYPE_DATETIME => ColumnExpData.Types.ColType.Datetime,
            DataWindowServiceBase.COL_TYPE_DATE => ColumnExpData.Types.ColType.Date,
            DataWindowServiceBase.COL_TYPE_TIME => ColumnExpData.Types.ColType.Time,
            _ => ColumnExpData.Types.ColType.Unknown,
        };

    /// <summary>
    /// Projects one variable reference, TAGGED WITH ITS OWN EXPANSION MODE.
    /// </summary>
    /// <param name="reference">The reference the parser recorded.</param>
    /// <returns>The wire message.</returns>
    /// <remarks>
    /// <para>
    /// THE MODE IS PER-REFERENCE AND THIS IS WHERE THAT IS ENFORCED. The specification's own example
    /// mixes both timings in ONE expression - <c>"$$上月读数 + $本月读数"</c> [docs:L68] - so a
    /// per-expression mode field could not represent it. The mode is derived from the reference's own
    /// sigils, which the engine reads off <c>fullname</c> [:L1297-L1300], and the projection is the
    /// environment's own: single sigil is STATIC (substituted at bind time and gone from the stored
    /// text [:L1435]), doubled sigil is DYNAMIC (an index into the live table, so mutation propagates
    /// [:L2189-L2200]).
    /// </para>
    /// <para>
    /// WHY ONLY THREE OF THE FIVE MODES APPEAR ON A VarData, and it is not an omission. A variable
    /// reference can only be static or dynamic, or carry no macro sigil at all
    /// (<c>EXPANSION_MODE_UNSPECIFIED</c> - the bare reference and the context column read at
    /// [:L2185]). The other two evidenced modes belong to FUNCTION references and travel on
    /// <c>InvokeMethodRequest.form</c> instead: dynamic-indirect is <c>$$('name')</c>, which the
    /// parser records as a FUNCTION whose name is the <c>FUNC_VAR</c> sentinel [:L120], and
    /// macro-direct and macro-dynamic are <c>$Func(args)</c> and <c>$$Invoke(...)</c>. All five modes
    /// are representable on the contract; which of them can decorate a variable as opposed to a
    /// function is the grammar's answer, not a simplification of it.
    /// </para>
    /// <para>
    /// <c>is_ctx</c> travels separately because the context bit does NOT alter the timing - a
    /// context-scoped variable is still static or dynamic - and because static expansion of a
    /// context-scoped variable is REFUSED outright by the legacy [:L1415-L1419], a restriction that is
    /// reported as a rejection rather than as a mode.
    /// </para>
    /// </remarks>
    internal static VarData ToWire(VariableReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return new VarData
        {
            Name = reference.Name,
            FullName = reference.FullName,

            // ONE-BASED index into the global variable table [:L53].
            Index = reference.Index,
            IsCtx = reference.IsCtx,
            IsMacro = reference.IsMacro,
            ExpansionMode = ColumnExpressionEngine
                .SigilsFor(reference)
                .ProjectVariableReference()
                .Mode,
        };
    }

    /// <summary>Projects one function reference [:L58-L63].</summary>
    /// <param name="reference">The reference the parser recorded.</param>
    /// <returns>The wire message.</returns>
    /// <remarks>
    /// <c>builtin</c> IS CARRIED WITH ITS MISLEADING NAME INTACT (C-B). It means "written with
    /// <c>$$</c>", which is what discriminates a built-in dispatch from a client macro - <c>$$Name(…)</c>
    /// against <c>$Func(…)</c> - and it is the flag the macro classifier reads. Renaming it to
    /// something clearer would break every characterization recording that carries it.
    /// </remarks>
    internal static FuncData ToWire(FunctionReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        FuncData projected = new()
        {
            Name = reference.Name,
            FullName = reference.FullName,
            Builtin = reference.Builtin,
        };

        // ONE-BASED at source [:L61]; zero-based here, order preserved.
        projected.Args.AddRange(reference.Args);

        return projected;
    }

    /// <summary>Projects one global variable table entry [:L65-L71].</summary>
    /// <param name="variable">The entry.</param>
    /// <param name="isResolvable">
    /// Whether a foreign entry's handle currently resolves in the session that will receive this
    /// message. Ignored for a local entry.
    /// </param>
    /// <returns>The wire message.</returns>
    internal static GlobalVarData ToWire(GlobalVariable variable, bool isResolvable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        GlobalVarData projected = new()
        {
            Name = variable.Name,

            // [:L117-L118] `constant long VAR_LOCAL = 0` and `VAR_FOREIGN = 1` - a CLOSED two-member
            // domain, so both preserved identifiers are named here rather than one of them being left
            // implicit in an else branch. The contract's own enumeration carries the same two spellings
            // as its wire names, which is what keeps a characterization recording comparable.
            VarType = variable.VarType switch
            {
                ExpressionVariableEnvironment.VAR_FOREIGN => GlobalVarData.Types.VarType.VarForeign,
                ExpressionVariableEnvironment.VAR_LOCAL => GlobalVarData.Types.VarType.VarLocal,

                // The oracle stores a long and declares only the two values above. Anything else is a
                // corrupted entry, and reporting it as LOCAL is the conservative reading: a local
                // variable resolves against THIS service's own table and therefore cannot manufacture a
                // cross-DataWindow reference the session never admitted.
                _ => GlobalVarData.Types.VarType.VarLocal,
            },
            Local = ToWire(variable.Local),
            Foreign = ToWire(variable.Foreign, isResolvable),
        };

        // [:L70] Every element is a reference into another DataWindow's table. Their resolvability is
        // reported alongside the same session test that decided the primary entry's.
        foreach (ForeignVariableReference link in variable.Links)
        {
            projected.Links.Add(ToWire(link, isResolvable));
        }

        return projected;
    }

    /// <summary>Projects a local variable definition [:L73-L78].</summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The wire message.</returns>
    /// <remarks>
    /// <c>value</c> IS DELIBERATELY LEFT UNSET, AND THIS IS FIDELITY RATHER THAN A GAP. The legacy
    /// stores a value variable AS A RENDERED LITERAL IN ITS EXPRESSION TEXT - every
    /// <c>of_addvar</c> overload [:L153-L159] renders its argument through <c>dwValueToExp</c> and calls
    /// <c>of_addvarexp</c> - so THERE IS NO TYPED VALUE LEFT IN THE ENGINE'S STATE TO PROJECT.
    /// <c>exp</c> is authoritative for both value and expression variables, exactly as it is at source.
    /// Reconstructing a type by parsing the literal back would invent information the oracle discarded,
    /// and the contract's own note reserves the field for a producer that genuinely has it.
    /// </remarks>
    internal static LocalVarData ToWire(LocalVariableDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        LocalVarData projected = new()
        {
            Exp = definition.Exp,
            HasMacro = definition.HasMacro,
        };

        // [:L75-L76] RECURSIVE BY DESIGN: a variable's value may itself be an expression carrying its
        // own variable and function references, so these two lists are not decoration.
        foreach (VariableReference variable in definition.Vars)
        {
            projected.Vars.Add(ToWire(variable));
        }

        foreach (FunctionReference function in definition.Fns)
        {
            projected.Fns.Add(ToWire(function));
        }

        return projected;
    }

    /// <summary>
    /// Projects a foreign variable reference - the replacement for the unserializable pointer.
    /// </summary>
    /// <param name="reference">The reference.</param>
    /// <param name="isResolvable">Whether the handle resolves in the receiving session.</param>
    /// <returns>The wire message.</returns>
    /// <remarks>
    /// <c>foreignvardata.expsvc</c> [:L82] is a LIVE POINTER to another instance of the same service
    /// class and is dereferenced synchronously at [:L2199-L2200] and [:L2384-L2385]. It is replaced by
    /// an OPAQUE SESSION-SCOPED HANDLE - not a URL, not an address, not a process identifier, because
    /// making it any of those would recreate the cross-instance reference the narrowing exists to
    /// forbid. <c>resolvable = false</c> means the reference is BLOCKED, and the accompanying error
    /// names the handle.
    /// </remarks>
    internal static ForeignVarRef ToWire(ForeignVariableReference reference, bool isResolvable)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return new ForeignVarRef
        {
            // ONE-BASED index WITHIN THE FOREIGN SERVICE's table [:L81], not within this one's.
            Index = reference.Index,
            ForeignDatawindowHandle = reference.Handle,
            Resolvable = isResolvable && !string.IsNullOrEmpty(reference.Handle),
        };
    }

    /// <summary>
    /// Projects the THREE-PART binding: unexpanded source text, bind-time snapshot, live environment.
    /// </summary>
    /// <param name="snapshot">The engine's binding record for one expression or variable expression.</param>
    /// <param name="environment">The engine's LIVE global variable table.</param>
    /// <returns>The wire message.</returns>
    /// <remarks>
    /// <para>
    /// ALL THREE COMPONENTS OR NONE OF THEM. Transmitting only the stored, already-rewritten text makes
    /// a STATIC BINDING INDISTINGUISHABLE FROM A LITERAL, because the parser substituted the variable
    /// away in place [:L1435] and the name is simply not there any more. Transmitting only the text and
    /// the snapshot strips a DYNAMIC BINDING of the environment it must resolve against at calculation
    /// time. The observable difference is the specification's 5 against 6 [docs:L37-L73], and it is
    /// unrecoverable from one component.
    /// </para>
    /// <para>
    /// THE SNAPSHOT'S VALUES ARE THE RENDERED TEXT THE PARSER SUBSTITUTED, which is why they travel as
    /// <c>string_value</c>. That is not a flattening of the seven-type union: it is what a static
    /// substitution IS at source - the parser writes rendered text into the expression - so a typed arm
    /// here would claim a type the substitution never had. The LIVE side carries the same rendering
    /// because the engine stores a value variable as a literal in its expression text; see
    /// <see cref="ToWire(LocalVariableDefinition)"/>.
    /// </para>
    /// </remarks>
    internal static ExpressionBinding ToWire(
        ExpressionBindingSnapshot snapshot,
        ExpressionVariableEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(environment);

        ExpressionBinding projected = new() { UnexpandedExp = snapshot.UnexpandedExp };

        foreach (KeyValuePair<string, string> bound in snapshot.BindTimeSnapshot)
        {
            projected.BindTimeSnapshot[bound.Key] = new VarValue { StringValue = bound.Value };
        }

        foreach (VariableReference variable in snapshot.Vars)
        {
            projected.Vars.Add(ToWire(variable));
        }

        foreach (FunctionReference function in snapshot.Fns)
        {
            projected.Fns.Add(ToWire(function));
        }

        // One-based 1..UpperBound over the live table, so a dynamic reference's index lands on the
        // entry it names.
        for (int index = 1; index <= environment.UpperBound; index++)
        {
            GlobalVariable variable = environment[index];

            projected.GlobalVars.Add(ToWire(variable, isResolvable: false));

            // Only a LOCAL entry has a value this side of the boundary. A foreign entry's value lives in
            // another DataWindow's table and is reached through the session, never copied into a
            // snapshot, because copying it would be the stale reading the narrowing exists to prevent.
            if (variable.VarType != ExpressionVariableEnvironment.VAR_FOREIGN
                && !string.IsNullOrEmpty(variable.Name))
            {
                projected.LiveEnvironment[variable.Name] =
                    new VarValue { StringValue = variable.Local.Exp };
            }
        }

        return projected;
    }

    /// <summary>Projects one trace record onto the wire.</summary>
    /// <param name="record">The record the session emitted.</param>
    /// <param name="dwo">The column the expression is bound to, resolved by the subscriber.</param>
    /// <returns>The wire message.</returns>
    /// <remarks>
    /// <para>
    /// THE CALL STACK IS CARRIED AS THE ORACLE BUILT IT, BYTE FOR BYTE [:L752-L757]: each frame
    /// followed by <c>&gt;</c>, then the current column's name appended with NO trailing delimiter, so
    /// the delimiter count equals the frame count and an empty stack yields just the column name. The
    /// frames also travel separately, because the flattened form cannot be split back apart safely - a
    /// column name containing <c>&gt;</c> would parse as two frames.
    /// </para>
    /// <para>
    /// <c>value</c> ALREADY CARRIES THE <c>"(null)"</c> SENTINEL WHERE THE ORACLE SUBSTITUTES IT
    /// [:L758]: the literal string <c>(null)</c> replaces the value when it is empty AND either the
    /// expression's <c>emptystringisnull</c> flag is set OR the column type is not string. That
    /// compound condition is the engine's to evaluate and is not re-derived here; both limbs are
    /// observable, so a second evaluation would be a second authority.
    /// </para>
    /// <para>
    /// <c>expr</c> IS THE PREPROCESSED EXPRESSION, not the source text - [:L758] passes <c>sExp</c>,
    /// which is the macro-expanded form when the expression has macros [:L744] and the stored form
    /// otherwise [:L747].
    /// </para>
    /// </remarks>
    internal static TraceRecord ToWire(ExpressionTraceRecord record, DwObjectRef dwo)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(dwo);

        TraceRecord projected = new()
        {
            // FOR DETECTION AND REORDERING, NOT FOR ORDERING GUARANTEES. The trace is pattern (a):
            // pure diagnostics, fire-and-forget, so a consumer may reorder on this token rather than
            // fail on a gap.
            Token = new SequencingToken
            {
                Sequence = record.SequenceNumber,
                Discipline = OrderingDiscipline.Sequenced,
            },

            // ONE-BASED row ordinal.
            Row = record.Row,
            Dwo = dwo,
            Stack = record.Stack,
            Expr = record.Expression,
            Value = record.Value,
            Depth = record.Depth,
        };

        projected.StackFrames.AddRange(record.StackFrames);

        return projected;
    }
}

/// <summary>
/// Fans expression trace records out to the clients subscribed on <c>TraceChannel</c> - the transport
/// half of INVERTED STREAM 2.
/// </summary>
/// <remarks>
/// <para>
/// WHY A BROKER AND NOT A DIRECT WRITE. <see cref="IExpressionTraceSink"/> is supplied to the SESSION
/// REGISTRY, so one sink serves every session in the process, while a subscriber names one session and
/// one DataWindow. <see cref="ExpressionTraceRecord"/> carries both, so routing is a lookup on values
/// the record already has - nothing is parsed and nothing is inferred.
/// </para>
/// <para>
/// THE SAME INSTANCE MUST REACH THE REGISTRY AND THIS SERVICE. A trace is emitted through the sink the
/// session was BUILT with; a second broker instance would receive nothing at all, and a subscriber on
/// it would see a silent channel that looks exactly like a disabled one. Registering it once as a
/// singleton is what makes the two the same object.
/// </para>
/// <para>
/// EMISSION NEVER BLOCKS AND NEVER THROWS. <see cref="Emit"/> is called from inside a calculation
/// [:L751-L758], and the trace is FIRE-AND-FORGET pattern (a) - so a slow or absent consumer must not
/// stall the engine and must not fail it either. Each subscriber therefore holds a BOUNDED channel that
/// DROPS ITS OLDEST record when full, and the drop is counted rather than hidden: diagnostics that
/// silently lose records while claiming completeness are worse than diagnostics that admit the gap, and
/// the sequencing token on every record makes a gap visible to the consumer as well.
/// </para>
/// <para>
/// SUBSCRIBING IS NOT THE SAME ACT AS ENABLING. Emission is gated on the engine's own <c>#Trace</c>
/// [:L98, set by <c>of_settrace</c> :L2409-L2411], and this broker does not touch that flag. A
/// subscriber on a service whose <c>#Trace</c> is clear correctly receives nothing, which is why
/// <c>GetServiceState</c> publishes the flag.
/// </para>
/// </remarks>
public sealed class ExpressionTraceBroker : IExpressionTraceSink
{
    /// <summary>
    /// How many records one subscriber may fall behind by before the oldest is dropped.
    /// </summary>
    /// <remarks>
    /// A FIXED CONSTANT RATHER THAN A SETTING, DELIBERATELY. <c>DataServices:ColumnExpression</c>
    /// publishes <c>Trace</c>, <c>RedrawSuppressionRowThreshold</c> and
    /// <c>CalcStackInitialCapacity</c> and nothing else, and inventing a fourth key would put a
    /// configuration surface in the repository that no requirement asked for.
    /// </remarks>
    public const int SubscriberCapacity = 1024;

    private readonly Dictionary<string, List<ExpressionTraceSubscription>> _subscribers =
        new(StringComparer.Ordinal);

    private readonly Lock _gate = new();
    private readonly ILogger? _logger;

    private long _dropped;

    /// <summary>Creates a broker.</summary>
    /// <param name="logger">
    /// Diagnostics. NEVER receives a trace record: a trace carries expression text and calculated
    /// values, which are the caller's data and not this process's to log.
    /// </param>
    public ExpressionTraceBroker(ILogger<ExpressionTraceBroker>? logger = null) => _logger = logger;

    /// <summary>The number of records dropped because a subscriber was too far behind.</summary>
    public long DroppedRecordCount => Interlocked.Read(ref _dropped);

    /// <summary>The number of live subscriptions.</summary>
    public int SubscriptionCount
    {
        get
        {
            lock (_gate)
            {
                int total = 0;

                foreach (List<ExpressionTraceSubscription> topic in _subscribers.Values)
                {
                    total += topic.Count;
                }

                return total;
            }
        }
    }

    /// <summary>
    /// Accepts one trace record from a session and hands it to every subscriber of that DataWindow.
    /// </summary>
    /// <param name="record">The record. Never <see langword="null"/>.</param>
    public void Emit(ExpressionTraceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        ExpressionTraceSubscription[] targets;

        lock (_gate)
        {
            if (!_subscribers.TryGetValue(
                    BuildKey(record.SessionId, record.Handle.Value),
                    out List<ExpressionTraceSubscription>? topic)
                || topic.Count == 0)
            {
                return;
            }

            // Copied under the lock so the write below happens outside it: a channel write must never
            // run while the subscriber map is held, or a subscriber disposing concurrently would
            // serialise against every emission in the process.
            targets = [.. topic];
        }

        foreach (ExpressionTraceSubscription target in targets)
        {
            if (!target.TryPublish(record))
            {
                _ = Interlocked.Increment(ref _dropped);
            }
        }
    }

    /// <summary>
    /// Subscribes to the trace of one DataWindow within one session.
    /// </summary>
    /// <param name="sessionId">The expression session.</param>
    /// <param name="dataWindowHandle">The session-scoped handle.</param>
    /// <returns>The subscription. Dispose it to unsubscribe.</returns>
    /// <exception cref="ArgumentException">Either identifier is blank.</exception>
    public ExpressionTraceSubscription Subscribe(string sessionId, string dataWindowHandle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataWindowHandle);

        ExpressionTraceSubscription subscription = new(this, sessionId, dataWindowHandle);

        lock (_gate)
        {
            string key = BuildKey(sessionId, dataWindowHandle);

            if (!_subscribers.TryGetValue(key, out List<ExpressionTraceSubscription>? topic))
            {
                topic = [];
                _subscribers[key] = topic;
            }

            topic.Add(subscription);
        }

        _logger?.LogDebug(
            "Trace subscription opened for session {SessionId} DataWindow {Handle}.",
            sessionId,
            dataWindowHandle);

        return subscription;
    }

    /// <summary>Removes one subscription.</summary>
    /// <param name="subscription">The subscription being disposed.</param>
    internal void Unsubscribe(ExpressionTraceSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        lock (_gate)
        {
            string key = BuildKey(subscription.SessionId, subscription.DataWindowHandle);

            if (_subscribers.TryGetValue(key, out List<ExpressionTraceSubscription>? topic))
            {
                _ = topic.Remove(subscription);

                if (topic.Count == 0)
                {
                    _ = _subscribers.Remove(key);
                }
            }
        }

        _logger?.LogDebug(
            "Trace subscription closed for session {SessionId} DataWindow {Handle}.",
            subscription.SessionId,
            subscription.DataWindowHandle);
    }

    // The newline separator appears in neither component: a session identifier is a hex string and a
    // minted handle is "<sessionId>/<ordinal>".
    private static string BuildKey(string? sessionId, string? dataWindowHandle) =>
        (sessionId ?? string.Empty) + "\n" + (dataWindowHandle ?? string.Empty);
}

/// <summary>
/// One client's subscription to the expression trace of one DataWindow.
/// </summary>
/// <remarks>
/// BOUNDED AND OLDEST-DROPPING, because emission happens inside a calculation and must not block. A
/// consumer that keeps up sees every record; one that does not sees the most recent
/// <see cref="ExpressionTraceBroker.SubscriberCapacity"/> and can detect what it missed from the
/// sequencing token, which is exactly what ordering pattern (a) is for.
/// </remarks>
public sealed class ExpressionTraceSubscription : IDisposable
{
    private readonly ExpressionTraceBroker _broker;
    private readonly Channel<ExpressionTraceRecord> _records;
    private readonly Lock _gate = new();

    private bool _disposed;

    internal ExpressionTraceSubscription(
        ExpressionTraceBroker broker,
        string sessionId,
        string dataWindowHandle)
    {
        _broker = broker;
        SessionId = sessionId;
        DataWindowHandle = dataWindowHandle;

        _records = Channel.CreateBounded<ExpressionTraceRecord>(
            new BoundedChannelOptions(ExpressionTraceBroker.SubscriberCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    /// <summary>The session this subscription observes.</summary>
    public string SessionId { get; }

    /// <summary>The DataWindow handle this subscription observes.</summary>
    public string DataWindowHandle { get; }

    /// <summary>The records, in emission order.</summary>
    public ChannelReader<ExpressionTraceRecord> Records => _records.Reader;

    /// <summary>
    /// Offers one record without blocking.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <returns>
    /// <see langword="true"/> when it was accepted; <see langword="false"/> when the subscription is
    /// already closed, which the broker counts as a drop.
    /// </returns>
    internal bool TryPublish(ExpressionTraceRecord record) => _records.Writer.TryWrite(record);

    /// <summary>Unsubscribes and completes the reader so a pump can finish.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _broker.Unsubscribe(this);

        _ = _records.Writer.TryComplete();
    }
}

/// <summary>
/// Raises the three events the expression engine declares on itself [:L86-L88] and publishes each to
/// the clients observing <c>EventStream</c>.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS THE RAISING POINT AND NOT JUST AN OBSERVER. In the legacy the host raises the engine's
/// item-changed event directly - <c>if ColumnExp.#Enabled then ColumnExp.Event OnItemChanged(row,dwo)</c>
/// [se_cst_dw.sru:L313-L314] - and that call is reachable only through <c>ondwnitemchange</c>, WHICH IS
/// GATED [se_cst_dw.sru:L182]. Putting the raise and the gate together here is what keeps the coupling
/// intact across the boundary while leaving both gRPC service classes independent of each other: the
/// item-change to column-expression edge runs through <c>Domain/EventGate.cs</c> and the engine, never
/// through a call from C-03's service class to C-04's.
/// </para>
/// <para>
/// THE GATE, EXACTLY WHERE THE ORACLE PUTS IT AND NOWHERE ELSE. <c>EID_ITEMCHANGE = 4</c> carries the
/// legacy's own note that disabling it means column-expression calculation will not be triggered
/// [se_cst_dw.sru:L43], and the mechanism is the early return at [se_cst_dw.sru:L182] - NOT a test
/// inside <c>of_calc</c>. So <see cref="RaiseItemChangedAsync"/> consults the gate and the calculation
/// entry points deliberately DO NOT: adding a gate test to <c>of_calc</c> would be a behaviour change
/// dressed as consistency, which C-B forbids.
/// </para>
/// <para>
/// WHAT IT DOES NOT OBSERVE, STATED RATHER THAN IMPLIED. The engine raises two of these three events on
/// ITSELF during its own work - do-item-changed from inside item-changed [:L224] and from the item
/// calculation [:L862], and var-changed from inside <c>of_setvar</c> [:L1770] and the foreign-link walk
/// [:L324, :L355]. Those internal raises are in-process recursion of the same handlers and are NOT
/// separately published, because publishing them would need a notification seam inside a file this one
/// does not own. What <c>EventStream</c> therefore carries is every event raised THROUGH this relay,
/// which is every event the host raises - the same set a legacy subscriber attached at the host would
/// have seen.
/// </para>
/// <para>
/// SEQUENCING TOKENS ARE FOR DETECTION. These are notifications, so a monotonic per-DataWindow sequence
/// is sufficient and the discipline is ORDERING_DISCIPLINE_SEQUENCED. Delivery is bounded and
/// oldest-dropping for the same reason the trace is: raising an event must not stall a calculation.
/// </para>
/// </remarks>
public sealed class ColumnExpressionEventRelay
{
    /// <summary>How many events one subscriber may fall behind by before the oldest is dropped.</summary>
    public const int SubscriberCapacity = 512;

    private readonly Dictionary<string, Topic> _topics = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly ILogger? _logger;

    /// <summary>Creates a relay.</summary>
    /// <param name="logger">Diagnostics. Never receives an event payload.</param>
    public ColumnExpressionEventRelay(ColumnExpressionEventRelayLogger? logger = null) =>
        _logger = logger?.Logger;

    /// <summary>The number of live subscriptions.</summary>
    public int SubscriptionCount
    {
        get
        {
            lock (_gate)
            {
                int total = 0;

                foreach (Topic topic in _topics.Values)
                {
                    total += topic.Subscribers.Count;
                }

                return total;
            }
        }
    }

    /// <summary>
    /// The engine's <c>onitemchanged(long row, dwobject dwo)</c> [:L86], raised the way the host raises
    /// it.
    /// </summary>
    /// <param name="engine">The engine bound to the DataWindow whose item changed.</param>
    /// <param name="row">The ONE-BASED row ordinal.</param>
    /// <param name="dwo">The column object that changed.</param>
    /// <param name="cancellationToken">Cancels the calculation the event drives.</param>
    /// <returns>A task that completes when the engine has finished reacting.</returns>
    /// <remarks>
    /// TWO GUARDS, BOTH OF THEM THE ORACLE'S. The gate test reproduces [se_cst_dw.sru:L182] and the
    /// enablement test reproduces [se_cst_dw.sru:L313]; the engine additionally guards itself at
    /// [:L214]. When either guard refuses, NOTHING is published - because the legacy did not raise the
    /// event at all, so no subscriber could have seen it.
    /// </remarks>
    public async ValueTask RaiseItemChangedAsync(
        ColumnExpressionEngine engine,
        long row,
        IDataWindowObject dwo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(dwo);

        // se_cst_dw.sru:L182 - `if BitTest(_nDisabledEvent,EID_ITEMCHANGE) then return 0`. The raw event
        // never reaches OnDoItemChange, so :L313's call into this service is never made, so NO
        // EXPRESSION IS CALCULATED [se_cst_dw.sru:L43].
        if (engine.DataWindow is { } host && host.IsEventDisabled(EventGate.EID_ITEMCHANGE))
        {
            return;
        }

        // se_cst_dw.sru:L313 - the host tests the service's own #Enabled before calling it.
        if (!engine.Enabled)
        {
            return;
        }

        await engine.OnItemChangedAsync(row, dwo, cancellationToken).ConfigureAwait(false);

        Publish(
            engine,
            new ColumnExpEvent
            {
                ItemChanged = new ColumnExpEvent.Types.ItemChanged
                {
                    Row = row,
                    Dwo = ExpressionWireProjection.ToWire(dwo),
                },
            },
            dwo.Name);
    }

    /// <summary>
    /// The engine's
    /// <c>ondoitemchanged(long row, string colname, long colid, boolean frominput)</c> [:L87].
    /// </summary>
    /// <param name="engine">The engine.</param>
    /// <param name="row">The ONE-BASED row ordinal.</param>
    /// <param name="columnName">The column's name.</param>
    /// <param name="columnId">The column's ONE-BASED DataWindow ordinal.</param>
    /// <param name="fromInput">
    /// Whether the change came from user input, which selects the relative-input column set rather than
    /// the relative column set.
    /// </param>
    /// <param name="cancellationToken">Cancels the calculation the event drives.</param>
    /// <returns>A task that completes when the engine has finished reacting.</returns>
    /// <remarks>
    /// THIS SHAPE IS NOT <c>se_cst_dw</c>'S. The host declares
    /// <c>ondoitemchanged(long row, dwobject dwo)</c> [se_cst_dw.sru:L26] while the ENGINE declares
    /// <c>(row, colname, colid, frominput)</c> [:L87]. They are different events with the same name in
    /// two different classes, and conflating them would corrupt the contract - which is why C-04 carries
    /// its own message rather than reusing C-03's. NO GATE TEST HERE: the legacy reaches this event from
    /// the gated item-change path AND from the ungated item calculation [:L862], so gating a direct
    /// raise would suppress a call the oracle makes.
    /// </remarks>
    public async ValueTask RaiseDoItemChangedAsync(
        ColumnExpressionEngine engine,
        long row,
        string? columnName,
        long columnId,
        bool fromInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);

        await engine
            .OnDoItemChangedAsync(row, columnName, columnId, fromInput, cancellationToken)
            .ConfigureAwait(false);

        Publish(
            engine,
            new ColumnExpEvent
            {
                DoItemChanged = new ColumnExpEvent.Types.DoItemChanged
                {
                    Row = row,
                    ColName = columnName ?? string.Empty,
                    ColId = columnId,
                    FromInput = fromInput,
                },
            },
            columnName);
    }

    /// <summary>
    /// The engine's <c>onvarchanged(integer index, boolean forcecalc)</c> [:L88].
    /// </summary>
    /// <param name="engine">The engine.</param>
    /// <param name="index">The ONE-BASED index into the global variable table.</param>
    /// <param name="forceCalc">Whether the recalculation it drives ignores the dirty state.</param>
    /// <param name="cancellationToken">Cancels the calculation the event drives.</param>
    /// <returns>A task that completes when the engine has finished reacting.</returns>
    /// <remarks>
    /// A VARIABLE HAS NO COLUMN, so this event carries none and is delivered to every subscriber of the
    /// DataWindow regardless of any column filter - see <see cref="Subscribe"/>.
    /// </remarks>
    public async ValueTask RaiseVarChangedAsync(
        ColumnExpressionEngine engine,
        int index,
        bool forceCalc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);

        await engine.OnVarChangedAsync(index, forceCalc, cancellationToken).ConfigureAwait(false);

        Publish(
            engine,
            new ColumnExpEvent
            {
                VarChanged = new ColumnExpEvent.Types.VarChanged
                {
                    Index = index,
                    ForceCalc = forceCalc,
                },
            },
            columnName: null);
    }

    /// <summary>
    /// Subscribes to the events of one DataWindow within one session.
    /// </summary>
    /// <param name="sessionId">The expression session.</param>
    /// <param name="dataWindowHandle">The session-scoped handle.</param>
    /// <param name="columnName">
    /// Restricts delivery to events naming this column, or null/blank for all of them. An event that
    /// names no column - var-changed - is delivered either way, because a filter on a column cannot
    /// exclude something that has none without silently hiding it.
    /// </param>
    /// <returns>The subscription. Dispose it to unsubscribe.</returns>
    /// <exception cref="ArgumentException">The session or handle is blank.</exception>
    public ColumnExpressionEventSubscription Subscribe(
        string sessionId,
        string dataWindowHandle,
        string? columnName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataWindowHandle);

        ColumnExpressionEventSubscription subscription =
            new(this, sessionId, dataWindowHandle, columnName);

        lock (_gate)
        {
            string key = BuildKey(sessionId, dataWindowHandle);

            if (!_topics.TryGetValue(key, out Topic? topic))
            {
                topic = new Topic();
                _topics[key] = topic;
            }

            topic.Subscribers.Add(subscription);
        }

        _logger?.LogDebug(
            "Column-expression event subscription opened for session {SessionId} DataWindow {Handle}.",
            sessionId,
            dataWindowHandle);

        return subscription;
    }

    /// <summary>Removes one subscription.</summary>
    /// <param name="subscription">The subscription being disposed.</param>
    internal void Unsubscribe(ColumnExpressionEventSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        lock (_gate)
        {
            string key = BuildKey(subscription.SessionId, subscription.DataWindowHandle);

            if (_topics.TryGetValue(key, out Topic? topic))
            {
                _ = topic.Subscribers.Remove(subscription);

                if (topic.Subscribers.Count == 0)
                {
                    _ = _topics.Remove(key);
                }
            }
        }
    }

    private void Publish(ColumnExpressionEngine engine, ColumnExpEvent body, string? columnName)
    {
        ColumnExpressionEventSubscription[] targets;
        long sequence;

        lock (_gate)
        {
            if (!_topics.TryGetValue(
                    BuildKey(engine.Session.SessionId, engine.Handle.Value),
                    out Topic? topic)
                || topic.Subscribers.Count == 0)
            {
                return;
            }

            // Assigned under the lock so that two concurrent raises cannot hand the same number to two
            // events, which would defeat the detection the token exists for.
            sequence = ++topic.Sequence;
            targets = [.. topic.Subscribers];
        }

        EventStreamResponse response = new()
        {
            Event = body,
            Sequence = sequence,
            Discipline = OrderingDiscipline.Sequenced,
        };

        foreach (ColumnExpressionEventSubscription target in targets)
        {
            target.TryPublish(response, columnName);
        }
    }

    private static string BuildKey(string? sessionId, string? dataWindowHandle) =>
        (sessionId ?? string.Empty) + "\n" + (dataWindowHandle ?? string.Empty);

    private sealed class Topic
    {
        internal List<ColumnExpressionEventSubscription> Subscribers { get; } = [];

        internal long Sequence { get; set; }
    }
}

/// <summary>
/// A typed carrier for the relay's optional logger.
/// </summary>
/// <remarks>
/// WHY A WRAPPER RATHER THAN <c>ILogger&lt;ColumnExpressionEventRelay&gt;</c> DIRECTLY. The relay is
/// constructed both by the container and, in tests, by hand with no arguments at all. A wrapper with a
/// nullable logger keeps the parameterless construction meaningful while still letting the container
/// supply a categorised logger, and it does so without a second constructor whose overload resolution a
/// reader would have to work out.
/// </remarks>
/// <param name="logger">The logger, or null.</param>
public sealed class ColumnExpressionEventRelayLogger(ILogger<ColumnExpressionEventRelay>? logger)
{
    /// <summary>The wrapped logger.</summary>
    public ILogger? Logger { get; } = logger;
}

/// <summary>
/// One client's subscription to the column-expression events of one DataWindow.
/// </summary>
/// <remarks>
/// Bounded and oldest-dropping for the same reason the trace subscription is: an event is raised inside
/// a calculation, and a slow consumer must not stall one.
/// </remarks>
public sealed class ColumnExpressionEventSubscription : IDisposable
{
    private readonly ColumnExpressionEventRelay _relay;
    private readonly Channel<EventStreamResponse> _events;
    private readonly string _columnFilter;
    private readonly Lock _gate = new();

    private bool _disposed;

    internal ColumnExpressionEventSubscription(
        ColumnExpressionEventRelay relay,
        string sessionId,
        string dataWindowHandle,
        string? columnName)
    {
        _relay = relay;
        SessionId = sessionId;
        DataWindowHandle = dataWindowHandle;
        _columnFilter = columnName?.Trim() ?? string.Empty;

        _events = Channel.CreateBounded<EventStreamResponse>(
            new BoundedChannelOptions(ColumnExpressionEventRelay.SubscriberCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    /// <summary>The session this subscription observes.</summary>
    public string SessionId { get; }

    /// <summary>The DataWindow handle this subscription observes.</summary>
    public string DataWindowHandle { get; }

    /// <summary>The column filter, or the empty string when unfiltered.</summary>
    public string ColumnFilter => _columnFilter;

    /// <summary>The events, in publication order.</summary>
    public ChannelReader<EventStreamResponse> Events => _events.Reader;

    /// <summary>Offers one event without blocking, honouring the column filter.</summary>
    /// <param name="response">The event.</param>
    /// <param name="columnName">The column the event names, or null when it names none.</param>
    /// <returns><see langword="true"/> when the event was accepted.</returns>
    internal bool TryPublish(EventStreamResponse response, string? columnName)
    {
        if (_columnFilter.Length > 0
            && columnName is not null
            && !string.Equals(columnName.Trim(), _columnFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _events.Writer.TryWrite(response);
    }

    /// <summary>Unsubscribes and completes the reader so a pump can finish.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _relay.Unsubscribe(this);

        _ = _events.Writer.TryComplete();
    }
}

/// <summary>
/// Contract C-04 <c>dataservices.v1.ColumnExpressionService</c> - the column-expression engine's
/// published surface.
/// </summary>
/// <remarks>
/// <para>
/// A TRANSPORT AND SESSION FACADE. Every member below resolves a session and a DataWindow handle, calls
/// the engine, and projects the answer. None of them parses, evaluates or decides anything: see the file
/// header for the full division of responsibility and for the four documented rationales this contract
/// requires (the C-03 split, the two stream inversions, the three-component payload, and the blocked
/// cross-session foreign reference).
/// </para>
/// <para>
/// WHERE A FAILURE GOES, AND WHY THE TWO KINDS ARE DIFFERENT (C-K). A UNARY call carries its outcome in
/// its own <c>ret_code</c> field, because the contract gives every response one and because the legacy's
/// tri-state algebra must survive the trip - <c>PREVENT</c> reads as a success, and <c>CANCELLED</c> and
/// null are neither succeeded nor failed, so collapsing an outcome onto a two-state transport status
/// would destroy exactly the distinction <c>retcode.sru</c> exists to make. A STREAMING call has no
/// response body to carry a code before its stream opens, so a precondition failure there is an
/// <see cref="RpcException"/>. Nothing is reported twice and nothing is reported in both ways.
/// </para>
/// <para>
/// NO STATIC MUTABLE STATE, and every collaborator injected (C-H). The session registry, the macro
/// router, the trace broker and the event relay are all ordinary objects a test can substitute, which is
/// what makes both inverted streams drivable without a real client.
/// </para>
/// </remarks>
public sealed class ColumnExpressionService : GeneratedColumnExpressionServiceBase
{
    private readonly ExpressionSessionRegistry _sessions;
    private readonly IDataWindowHostFactory _hostFactory;
    private readonly MacroInvocationRouter _macroRouter;
    private readonly ExpressionTraceBroker _traceBroker;
    private readonly ColumnExpressionEventRelay _eventRelay;
    private readonly IExpressionPageResolver _pageResolver;
    private readonly PinyinFirstLetterMatcher _pinyinMatcher;
    private readonly ColumnExpressionOptions _columnExpression;

    /// <summary>
    /// The backstop bound on one macro invocation, from
    /// <c>DataServices:ColumnExpression:MacroInvocationTimeout</c>. See the remarks on
    /// <see cref="CreateEngine"/> for why it is a backstop rather than a budget, and the option's own
    /// remarks for why its default is the expression session's idle lifetime.
    /// </summary>
    private readonly TimeSpan _macroInvocationBackstop;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ColumnExpressionService>? _logger;

    /// <summary>
    /// Creates the service.
    /// </summary>
    /// <param name="options">
    /// The service options. <c>DataServices:ColumnExpression</c> supplies <c>Trace</c> - which
    /// reproduces <c>privatewrite boolean #Trace</c> having no initializer at source [:L98] and lets
    /// deployment override the resulting false - along with <c>RedrawSuppressionRowThreshold</c> and
    /// <c>CalcStackInitialCapacity</c>, whose default of 20 is the oracle's own <c>Reserve(20)</c>
    /// [:L2421-L2422].
    /// </param>
    /// <param name="sessions">
    /// The expression session registry. It owns session identity, the idle sweep and the concurrency
    /// ceiling, and it is where <c>DataServices:Sessions:ExpressionSession</c> takes effect.
    /// </param>
    /// <param name="hostFactory">
    /// Binds a caller's DataWindow name to a host. REQUIRED: an engine cannot answer <c>Describe</c> or
    /// <c>RowCount</c> without one, so a service composed without a factory is structurally faulty
    /// rather than partly working, and AAP 0.1.4 requires that fail fast rather than degrade.
    /// </param>
    /// <param name="macroRouter">Routes INVERTED STREAM 1. See the file header.</param>
    /// <param name="traceBroker">
    /// Fans out INVERTED STREAM 2. IT MUST BE THE SAME INSTANCE THE SESSION REGISTRY WAS BUILT WITH: a
    /// trace is emitted through the sink the session holds, so a second instance would receive nothing
    /// and its subscribers would see a silent channel indistinguishable from a disabled one.
    /// </param>
    /// <param name="eventRelay">Raises and publishes the engine's own three events [:L86-L88].</param>
    /// <param name="pageResolver">
    /// Resolves <c>for page</c> for the evaluator. REQUIRED, because the evaluator's class default
    /// REFUSES <c>for page</c> - a deliberate narrowing that is the right default for code which has
    /// stated nothing about pagination and the wrong one for a configured, running service. The single
    /// statement of pagination is <c>DataServices:ColumnExpression:PageResolution</c>, turned into a
    /// resolver once at composition.
    /// </param>
    /// <param name="pinyinMatcher">
    /// The pinyin first-letter matcher. Defaults to
    /// <see cref="PinyinFirstLetterMatcher.Blocked"/>, WHICH IS THE HONEST DEFAULT AND NOT A STUB: the
    /// lookup table exists only inside the closed <c>pfw.dll</c>, so a matcher that has not been
    /// characterized against the behavioural oracle reports BLOCKED rather than approximating. An
    /// approximation would return subtly different result sets - a regression a characterization suite
    /// would catch and a unit test would not.
    /// </param>
    /// <param name="timeProvider">
    /// The clock, seamed so a characterization recording is reproducible. Defaults to the system clock.
    /// </param>
    /// <param name="logger">Diagnostics. Never receives expression text or calculated values.</param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public ColumnExpressionService(
        IOptions<DataServicesOptions> options,
        ExpressionSessionRegistry sessions,
        IDataWindowHostFactory hostFactory,
        MacroInvocationRouter macroRouter,
        ExpressionTraceBroker traceBroker,
        ColumnExpressionEventRelay eventRelay,
        IExpressionPageResolver pageResolver,
        PinyinFirstLetterMatcher? pinyinMatcher = null,
        TimeProvider? timeProvider = null,
        ILogger<ColumnExpressionService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _hostFactory = hostFactory ?? throw new ArgumentNullException(nameof(hostFactory));
        _macroRouter = macroRouter ?? throw new ArgumentNullException(nameof(macroRouter));
        _traceBroker = traceBroker ?? throw new ArgumentNullException(nameof(traceBroker));
        _eventRelay = eventRelay ?? throw new ArgumentNullException(nameof(eventRelay));
        _pageResolver = pageResolver ?? throw new ArgumentNullException(nameof(pageResolver));
        _pinyinMatcher = pinyinMatcher ?? PinyinFirstLetterMatcher.Blocked;
        _columnExpression = options.Value.ColumnExpression;
        _macroInvocationBackstop = options.Value.ColumnExpression.MacroInvocationTimeout;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    // =================================================================================================
    //  THE SESSION - the scope that replaces `foreignvardata.expsvc` [:L80-L83]
    // =================================================================================================

    /// <summary>
    /// Opens an expression session and makes the named DataWindows co-resident in it.
    /// </summary>
    /// <param name="request">The DataWindows to make co-resident.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The session identifier and the ALLOCATED handles, in the order requested.</returns>
    /// <remarks>
    /// <para>
    /// THE HANDLES ARE ALLOCATED HERE, NOT ADOPTED FROM THE REQUEST. The request names DataWindows in
    /// the caller's own terms; each engine mints its handle by registering with the session, and the
    /// minted values come back in request order. They are session-qualified by construction, which is
    /// what makes the narrowing enforceable: a handle carried in from another session MISSES this
    /// session's registry and is BLOCKED rather than colliding with a local registration and resolving
    /// to the wrong DataWindow.
    /// </para>
    /// <para>
    /// A PARTIAL OPEN IS NOT A SESSION - it is a session with a hole in it, which would BLOCK a foreign
    /// reference that the caller had every reason to expect to resolve. So the first DataWindow that
    /// cannot be bound closes the whole session and answers a code. That is the fail-fast posture, and
    /// the alternative would be graceful degradation of exactly the guarantee the session exists to
    /// provide.
    /// </para>
    /// <para>
    /// NOTHING IS ENABLED HERE. <c>#Enabled</c> is <c>protectedwrite boolean</c> with no initializer
    /// [n_cst_dwsvc.sru:L38], so a PowerBuilder service is INERT until <c>of_setenabled</c> turns it on.
    /// Auto-enabling would be a behaviour change (C-B); the caller uses <c>SetEnabled</c>, which is on
    /// the contract for exactly that reason.
    /// </para>
    /// </remarks>
    public override Task<OpenExpressionSessionResponse> OpenExpressionSession(
        OpenExpressionSessionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        ExpressionSessionOpenResult opened = _sessions.Open();

        if (!opened.IsOpened || opened.Session is null)
        {
            return Task.FromResult(
                new OpenExpressionSessionResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(opened.ReturnCode),
                });
        }

        ExpressionSession session = opened.Session;
        OpenExpressionSessionResponse response = new() { SessionId = session.SessionId };

        foreach (string dataWindowName in request.DatawindowHandles)
        {
            DataWindowServiceHost? host = _hostFactory.Create(dataWindowName ?? string.Empty);

            if (host is null)
            {
                _ = _sessions.Close(session.SessionId);

                _logger?.LogWarning(
                    "No DataWindow host could be bound for the requested name, so expression session "
                        + "{SessionId} was closed rather than opened with a hole in it.",
                    session.SessionId);

                return Task.FromResult(
                    new OpenExpressionSessionResponse
                    {
                        RetCode = ExpressionWireProjection.ToWireRetCode(RetCode.E_OBJECT_NOT_FOUND),
                    });
            }

            ColumnExpressionEngine engine = CreateEngine(session, host);

            if (engine.Handle.IsEmpty)
            {
                // Registration answers DataWindowHandle.None only for a session that closed underneath
                // the open - a race with the idle sweep. There is nothing to recover: an engine with no
                // handle is unreachable and every reference to it would be BLOCKED.
                _ = _sessions.Close(session.SessionId);

                return Task.FromResult(
                    new OpenExpressionSessionResponse
                    {
                        RetCode = ExpressionWireProjection.ToWireRetCode(RetCode.E_NOT_EXISTS),
                    });
            }

            response.DatawindowHandles.Add(engine.Handle.Value);
        }

        response.RetCode = ExpressionWireProjection.ToWireRetCode(RetCode.OK);

        return Task.FromResult(response);
    }

    /// <summary>
    /// Closes an expression session, releasing its handles and calculation stacks.
    /// </summary>
    /// <param name="request">The session to close.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome, and whether the session had still been open.</returns>
    /// <remarks>
    /// IDEMPOTENT, AND <c>was_open</c> IS THE DIFFERENCE. Closing an already-closed session is not an
    /// error - the registry's close is idempotent so that an expired entry cannot linger - but the two
    /// cases are distinguishable, because a caller that believed it held a live session should be able
    /// to learn that it did not. Closure is WHOLESALE by design: the legacy publishes no operation that
    /// removes one foreign link, so a handle cannot be quietly invalidated underneath a reference that
    /// has already resolved it.
    /// </remarks>
    public override Task<CloseExpressionSessionResponse> CloseExpressionSession(
        CloseExpressionSessionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            return Task.FromResult(
                new CloseExpressionSessionResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT),
                });
        }

        bool wasOpen = _sessions.Close(request.SessionId);

        return Task.FromResult(
            new CloseExpressionSessionResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(RetCode.OK),
                WasOpen = wasOpen,
            });
    }

    // =================================================================================================
    //  EXPRESSIONS - of_addexp [:L172], of_setexp x4 [:L174-L177], of_getexp x2 [:L127, :L130],
    //  of_remove x2 [:L131-L132], of_removeall [:L125]
    // =================================================================================================

    /// <summary>
    /// <c>of_addexp(string colname, string exp)</c> [:L172] - binds an expression to a column.
    /// </summary>
    /// <param name="request">The column and the expression.</param>
    /// <param name="context">The call context.</param>
    /// <returns>THE NEW EXPRESSION'S INDEX, plus a return code and any error.</returns>
    /// <remarks>
    /// IT RETURNS AN INDEX, NOT A RETURN CODE, AND ITS TYPE IS <c>integer</c> RATHER THAN <c>long</c>
    /// [:L172]. Both anomalies are carried (C-B), which is why <c>index</c> is a field distinct from
    /// <c>ret_code</c>: a caller reads the index to address the expression later, and reads the code to
    /// learn whether the bind succeeded. The index is ONE-BASED. A non-positive index means the bind
    /// failed, and the code says why.
    /// </remarks>
    public override Task<AddExpressionResponse> AddExpression(
        AddExpressionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return Task.FromResult(
                new AddExpressionResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
                });
        }

        long baseline = engine.RaisedErrorCount;
        int index = engine.AddExp(request.ColumnName, request.Exp);

        return Task.FromResult(
            new AddExpressionResponse
            {
                Index = index,
                RetCode = ExpressionWireProjection.ToWireRetCode(
                    index > 0 ? RetCode.OK : RetCode.FAILED),
                Error = ErrorRaisedSince(engine, baseline),
            });
    }

    /// <summary>
    /// <c>of_setexp</c> in all four arities [:L174-L177] - replaces a bound expression.
    /// </summary>
    /// <param name="request">The target, the expression, and optionally the recalculate flag.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// FOUR LEGACY OVERLOADS, TWO WIRE DISTINCTIONS. Addressing is carried because an index and a name
    /// are NOT the same request - the name form performs a lookup that can fail
    /// [<c>_of_findexpindex</c> :L141] and the index form does not - while arity is collapsed onto the
    /// explicit presence of <c>recalc</c>, which is the same information rather than less: absent means
    /// the caller used the shorter overload.
    /// </remarks>
    public override async Task<SetExpressionResponse> SetExpression(
        SetExpressionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return new SetExpressionResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
            };
        }

        long baseline = engine.RaisedErrorCount;
        CancellationToken cancellationToken = context.CancellationToken;
        long code;

        switch (request.Target?.AddressingCase)
        {
            case ColumnRef.AddressingOneofCase.Index:
                code = request.HasRecalc
                    ? await engine
                        .SetExpAsync(
                            request.Target.Index,
                            request.Exp,
                            request.Recalc,
                            cancellationToken)
                        .ConfigureAwait(false)
                    : await engine
                        .SetExpAsync(request.Target.Index, request.Exp, cancellationToken)
                        .ConfigureAwait(false);
                break;

            case ColumnRef.AddressingOneofCase.ColumnName:
                code = request.HasRecalc
                    ? await engine
                        .SetExpAsync(
                            request.Target.ColumnName,
                            request.Exp,
                            request.Recalc,
                            cancellationToken)
                        .ConfigureAwait(false)
                    : await engine
                        .SetExpAsync(request.Target.ColumnName, request.Exp, cancellationToken)
                        .ConfigureAwait(false);
                break;

            default:
                // Neither arm set. Exactly one is required, so this is malformed rather than a default.
                code = RetCode.E_INVALID_ARGUMENT;
                break;
        }

        return new SetExpressionResponse
        {
            RetCode = ExpressionWireProjection.ToWireRetCode(code),
            Error = ErrorRaisedSince(engine, baseline),
        };
    }

    /// <summary>
    /// <c>of_getexp</c> by name [:L127] or by index [:L130], WITH THE THREE-PART BINDING.
    /// </summary>
    /// <param name="request">The target.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The stored expression and its binding.</returns>
    /// <remarks>
    /// <c>exp</c> IS THE STORED TEXT AND THE BINDING IS WHY THAT IS NOT ENOUGH. The parser rewrote the
    /// expression in place when it was set [:L1435], so every static reference has already been
    /// substituted away and the stored text ALONE CANNOT DISTINGUISH A STATIC BINDING FROM A LITERAL.
    /// The binding carries the unexpanded source, the bind-time snapshot and the live environment; see
    /// the file header for why all three are required rather than merely useful.
    /// </remarks>
    public override Task<GetExpressionResponse> GetExpression(
        GetExpressionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return Task.FromResult(
                new GetExpressionResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
                });
        }

        int index;
        string exp;

        switch (request.Target?.AddressingCase)
        {
            case ColumnRef.AddressingOneofCase.Index:
                index = request.Target.Index;
                exp = engine.GetExp(index);
                break;

            case ColumnRef.AddressingOneofCase.ColumnName:
                // The name form resolves through the lookup the legacy uses [:L141], so an unknown name
                // yields index 0 and the empty expression rather than an exception.
                index = engine.FindExpIndex(request.Target.ColumnName);
                exp = engine.GetExp(index);
                break;

            default:
                return Task.FromResult(
                    new GetExpressionResponse
                    {
                        RetCode = ExpressionWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT),
                    });
        }

        GetExpressionResponse response = new()
        {
            Exp = exp,
            RetCode = ExpressionWireProjection.ToWireRetCode(
                index > 0 ? RetCode.OK : RetCode.E_NOT_EXISTS),
        };

        if (engine.GetBinding(index) is { } snapshot)
        {
            response.Binding = ExpressionWireProjection.ToWire(snapshot, engine.Variables);
        }

        return Task.FromResult(response);
    }

    /// <summary>
    /// <c>of_remove</c> by index [:L131] or by name [:L132] - unbinds one expression.
    /// </summary>
    /// <param name="request">The target.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome.</returns>
    public override Task<RemoveExpressionResponse> RemoveExpression(
        RemoveExpressionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return Task.FromResult(
                new RemoveExpressionResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
                });
        }

        long code = request.Target?.AddressingCase switch
        {
            ColumnRef.AddressingOneofCase.Index => engine.Remove(request.Target.Index),
            ColumnRef.AddressingOneofCase.ColumnName => engine.Remove(request.Target.ColumnName),
            _ => RetCode.E_INVALID_ARGUMENT,
        };

        return Task.FromResult(
            new RemoveExpressionResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(code),
            });
    }

    /// <summary>
    /// <c>of_removeall()</c> [:L125] - unbinds every expression.
    /// </summary>
    /// <param name="request">The session and DataWindow.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome.</returns>
    public override Task<RemoveAllExpressionsResponse> RemoveAllExpressions(
        RemoveAllExpressionsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        long code = resolution.Engine is { } engine
            ? engine.RemoveAll()
            : resolution.ReturnCode;

        return Task.FromResult(
            new RemoveAllExpressionsResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(code),
            });
    }

    // =================================================================================================
    //  VARIABLES - of_addvar across SEVEN TYPED OVERLOADS [:L153-L159] and of_setvar across
    //  SEVEN TYPES x THREE ARITIES [:L160-L166, :L179-L185, :L188-L194]
    // =================================================================================================

    /// <summary>
    /// <c>of_addvar</c> [:L153-L159] - defines a typed variable.
    /// </summary>
    /// <param name="request">The name and the typed value.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// <para>
    /// SEVEN OVERLOADS, ONE PER TYPE, IN THE LEGACY'S OWN DECLARATION ORDER: time, string, long, double,
    /// datetime, date, boolean. Note <c>double</c> and NOT <c>decimal</c> [:L156]. The wire selects the
    /// overload by which arm of the <c>VarValue</c> union is set, and the union is exactly why this is a
    /// discriminated union rather than a stringly-typed map: the type selects the coercion, so collapsing
    /// the seven to their renderings would make the coercion unreproducible.
    /// </para>
    /// <para>
    /// NO <c>index</c> IS RETURNED, AND THE CONTRACT RESERVES THE FIELD RATHER THAN PRETENDING. The
    /// legacy answers a <c>long</c> return code here, not an index - unlike <c>of_addexp</c> - so a
    /// field claiming otherwise would be an invention.
    /// </para>
    /// <para>
    /// AN UNSET UNION IS REFUSED. It means "no value supplied", which the contract deliberately keeps
    /// distinct from an explicit null, and the legacy has no untyped overload to route it to. The value
    /// is NEVER coerced to zero or to the empty string to make the call succeed: that would convert
    /// "nothing" into a datum, which is the collapse the standing null rule forbids. A TYPED NULL is
    /// likewise not expressible on this contract - <c>VarValue</c> has no null arm - and that is a
    /// documented narrowing rather than an oversight.
    /// </para>
    /// </remarks>
    public override Task<AddVariableResponse> AddVariable(
        AddVariableRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return Task.FromResult(
                new AddVariableResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
                });
        }

        long baseline = engine.RaisedErrorCount;
        long code = DispatchAddVar(engine, request.Name, request.Value);

        return Task.FromResult(
            new AddVariableResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(code),
                Error = ErrorRaisedSince(engine, baseline),
            });
    }

    /// <summary>
    /// <c>of_setvar</c> across seven types and three arities [:L160-L166, :L179-L185, :L188-L194].
    /// </summary>
    /// <param name="request">The name, the typed value, and optionally recalculate and force.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// TWENTY-ONE LEGACY MEMBERS, ONE MESSAGE. The type comes from the union arm and the arity from the
    /// explicit presence of the two flags: neither is the <c>(value)</c> overload, <c>recalc</c> alone is
    /// <c>(value, recalc)</c>, and both together are <c>(value, recalc, force)</c>. FORCE WITHOUT RECALC
    /// IS MALFORMED rather than tolerated, because the legacy declares no such overload and silently
    /// choosing one for the caller would pick a behaviour they did not ask for.
    /// </remarks>
    public override async Task<SetVariableResponse> SetVariable(
        SetVariableRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return new SetVariableResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
            };
        }

        long baseline = engine.RaisedErrorCount;
        long code = await DispatchSetVarAsync(engine, request, context.CancellationToken)
            .ConfigureAwait(false);

        return new SetVariableResponse
        {
            RetCode = ExpressionWireProjection.ToWireRetCode(code),
            Error = ErrorRaisedSince(engine, baseline),
        };
    }

    // =================================================================================================
    //  EXPRESSION VARIABLES - of_addvarexp [:L173], of_setvarexp x3 [:L178, :L186, :L187],
    //  of_getvarexp [:L152]
    // =================================================================================================

    /// <summary>
    /// <c>of_addvarexp(readonly string name, string exp)</c> [:L173] - defines a variable whose value is
    /// itself an expression.
    /// </summary>
    /// <param name="request">The name and the expression.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// RECURSIVE BY DESIGN. The expression may reference other variables and macro functions
    /// [:L75-L76], which is what makes the seven-structure model a graph rather than a table, and it is
    /// the entry point the specification pairs with a foreign variable:
    /// <c>of_AddForeignVar('name', dw_src)</c> then <c>of_AddVarExp('local', '$$name')</c>
    /// [docs:L33-L34], where the doubled sigil is REQUIRED because a static substitution would read the
    /// foreign value once and never see it change.
    /// </remarks>
    public override Task<AddVariableExpressionResponse> AddVariableExpression(
        AddVariableExpressionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return Task.FromResult(
                new AddVariableExpressionResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
                });
        }

        long baseline = engine.RaisedErrorCount;
        long code = engine.AddVarExp(request.Name, request.Exp);

        return Task.FromResult(
            new AddVariableExpressionResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(code),
                Error = ErrorRaisedSince(engine, baseline),
            });
    }

    /// <summary>
    /// <c>of_setvarexp</c> in three arities [:L178, :L186, :L187].
    /// </summary>
    /// <param name="request">The name, the expression, and optionally recalculate and force.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// The arity rule is the one <see cref="SetVariable"/> uses, including force-without-recalc being
    /// malformed: the legacy's three overloads are <c>(name, exp)</c>, <c>(name, exp, recalc)</c> and
    /// <c>(name, exp, recalc, force)</c>, and there is no fourth.
    /// </remarks>
    public override async Task<SetVariableExpressionResponse> SetVariableExpression(
        SetVariableExpressionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return new SetVariableExpressionResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
            };
        }

        long baseline = engine.RaisedErrorCount;
        CancellationToken cancellationToken = context.CancellationToken;
        long code;

        if (request.HasForce && !request.HasRecalc)
        {
            code = RetCode.E_INVALID_ARGUMENT;
        }
        else if (request.HasForce)
        {
            code = await engine
                .SetVarExpAsync(
                    request.Name,
                    request.Exp,
                    request.Recalc,
                    request.Force,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (request.HasRecalc)
        {
            code = await engine
                .SetVarExpAsync(request.Name, request.Exp, request.Recalc, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            code = await engine
                .SetVarExpAsync(request.Name, request.Exp, cancellationToken)
                .ConfigureAwait(false);
        }

        return new SetVariableExpressionResponse
        {
            RetCode = ExpressionWireProjection.ToWireRetCode(code),
            Error = ErrorRaisedSince(engine, baseline),
        };
    }

    /// <summary>
    /// <c>of_getvarexp(string name)</c> [:L152] - reads a variable's expression and its definition.
    /// </summary>
    /// <param name="request">The variable name.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The expression and the local definition.</returns>
    public override Task<GetVariableExpressionResponse> GetVariableExpression(
        GetVariableExpressionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return Task.FromResult(
                new GetVariableExpressionResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
                });
        }

        // ONE-BASED index into the global variable table; 0 means the name is not defined.
        int index = engine.FindVarIndex(request.Name);

        GetVariableExpressionResponse response = new()
        {
            Exp = engine.GetVarExp(request.Name),
            RetCode = ExpressionWireProjection.ToWireRetCode(
                index > 0 ? RetCode.OK : RetCode.E_VAR_NOT_FOUND),
        };

        if (index > 0)
        {
            response.Local = ExpressionWireProjection.ToWire(engine.Variables[index].Local);
        }

        return Task.FromResult(response);
    }

    // =================================================================================================
    //  THE FOREIGN VARIABLE - of_addforeignvar [:L201], AND THE ONE HARD LIMIT OF THIS CONTRACT
    // =================================================================================================

    /// <summary>
    /// <c>of_addforeignvar(readonly string name, readonly se_cst_dw dw)</c> [:L201] - references a
    /// variable that belongs to ANOTHER DataWindow's expression service.
    /// </summary>
    /// <param name="request">The local name and the foreign DataWindow's session handle.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome, with a BLOCKED error when the reference cannot be honoured.</returns>
    /// <remarks>
    /// <para>
    /// THE LEGACY TAKES A LIVE CONTROL; THIS TAKES A SESSION-SCOPED HANDLE. <c>foreignvardata.expsvc</c>
    /// [:L82] is typed as another live instance of the same service class and is DEREFERENCED as a
    /// pointer at two distinct sites - the variable-value path [:L2384-L2385] and the
    /// expression-preprocessing path [:L2199-L2200] - each a synchronous call through the stored pointer
    /// into a private method of the other instance. A pointer cannot be serialized, so the handle
    /// replaces it.
    /// </para>
    /// <para>
    /// CO-RESIDENCY IS THE WHOLE TEST, AND FAILING IT IS BLOCKED RATHER THAN BEST-EFFORT. The reference
    /// is honoured only when the named DataWindow is registered in THE SAME SESSION inside THIS service
    /// instance. Anything else - a handle from another session, a handle from another instance, a
    /// session that has since closed - returns a DEFINED ERROR THAT NAMES THE UNREACHABLE HANDLE, and is
    /// never resolved to a substituted value, a default, an empty string or a stale reading.
    /// Approximating the dereference would yield an expression that evaluates, returns a number of the
    /// right type in the right range, and is silently wrong; a defined error is worse ergonomics and
    /// better engineering. This is the ONLY place in the contract where the narrowing principle has to
    /// be applied, and it is documented (C-K) rather than discovered.
    /// </para>
    /// <para>
    /// The specification pairs this entry point with dynamic expansion - <c>of_AddForeignVar</c> then
    /// <c>of_AddVarExp('local', '$$name')</c>, annotated "must use dynamic expansion" [docs:L33-L34] -
    /// so the limitation lands exactly on the combination the legacy supports, which is why it is stated
    /// here rather than left to be met at calculation time.
    /// </para>
    /// </remarks>
    public override Task<AddForeignVariableResponse> AddForeignVariable(
        AddForeignVariableRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine || resolution.Session is not { } session)
        {
            return Task.FromResult(
                new AddForeignVariableResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
                });
        }

        DataWindowHandle foreignHandle = DataWindowHandle.From(request.ForeignDatawindowHandle);

        if (foreignHandle.IsEmpty)
        {
            return Task.FromResult(
                new AddForeignVariableResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT),
                });
        }

        // The co-residency test. A miss is BLOCKED, and the error names the handle.
        if (!session.TryGetHost(foreignHandle, out IExpressionServiceHost? foreignHost)
            || foreignHost is not ColumnExpressionEngine peer)
        {
            ExpressionParseError blocked = ExpressionSession.CreateBlockedError(
                foreignHandle,
                session.SessionId,
                CrossDataWindowStatus.BlockedHandleNotCoResident);

            _logger?.LogWarning(
                "A foreign variable reference in expression session {SessionId} named a DataWindow that "
                    + "is not co-resident, so it was BLOCKED rather than approximated.",
                session.SessionId);

            return Task.FromResult(
                new AddForeignVariableResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(
                        blocked.ReturnCode ?? RetCode.E_INVALID_HANDLE),
                    Error = ExpressionWireProjection.ToWire(blocked),
                });
        }

        long baseline = engine.RaisedErrorCount;
        long code = engine.AddForeignVar(request.Name, peer);

        return Task.FromResult(
            new AddForeignVariableResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(code),
                Error = ErrorRaisedSince(engine, baseline),
            });
    }

    // =================================================================================================
    //  RELATIVE COLUMNS - EIGHT legacy entry points [:L128-L129, :L135-L140]
    // =================================================================================================

    /// <summary>
    /// The eight relative-column setters: singular and plural, by index and by name, for both the
    /// change-triggered and the INPUT-triggered sets.
    /// </summary>
    /// <param name="request">The target, the column list, and the two discriminators.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// <para>
    /// TWO SETS, NOT ONE, AND THE DIFFERENCE IS OBSERVABLE. The relative set triggers a recalculation
    /// whenever the named column's value changes; the INPUT set triggers only when the user typed it -
    /// the specification's own annotation, "only when the user inputs n1" [docs:L162-L163]. The
    /// discriminator on the wire is <c>input_only</c>, and it maps onto the two legacy families.
    /// </para>
    /// <para>
    /// SINGULAR IS NOT PLURAL-WITH-ONE-ELEMENT, so <c>plural</c> is carried rather than inferred from the
    /// list length: the legacy declares both families, a caller that used the singular form said so, and
    /// a singular request carrying zero or several columns is MALFORMED rather than quietly reinterpreted
    /// as the plural form.
    /// </para>
    /// </remarks>
    public override Task<SetRelativeColumnsResponse> SetRelativeColumns(
        SetRelativeColumnsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return Task.FromResult(
                new SetRelativeColumnsResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
                });
        }

        ImmutableArray<string> columns = [.. request.RelativeColumns];

        if (!request.Plural && columns.Length != 1)
        {
            return Task.FromResult(
                new SetRelativeColumnsResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT),
                });
        }

        long code = request.Target?.AddressingCase switch
        {
            ColumnRef.AddressingOneofCase.Index => request.Plural
                ? request.InputOnly
                    ? engine.SetRelativeInputColumns(request.Target.Index, columns)
                    : engine.SetRelativeColumns(request.Target.Index, columns)
                : request.InputOnly
                    ? engine.SetRelativeInputColumn(request.Target.Index, columns[0])
                    : engine.SetRelativeColumn(request.Target.Index, columns[0]),

            ColumnRef.AddressingOneofCase.ColumnName => request.Plural
                ? request.InputOnly
                    ? engine.SetRelativeInputColumns(request.Target.ColumnName, columns)
                    : engine.SetRelativeColumns(request.Target.ColumnName, columns)
                : request.InputOnly
                    ? engine.SetRelativeInputColumn(request.Target.ColumnName, columns[0])
                    : engine.SetRelativeColumn(request.Target.ColumnName, columns[0]),

            _ => RetCode.E_INVALID_ARGUMENT,
        };

        return Task.FromResult(
            new SetRelativeColumnsResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(code),
            });
    }

    // =================================================================================================
    //  PER-EXPRESSION FLAGS - always-calculate [:L133-L134], recursive [:L146-L147],
    //  trigger-event [:L148-L149] AND cacheable [:L196-L197]
    // =================================================================================================

    /// <summary>
    /// The four per-expression flags, each addressable by index or by name.
    /// </summary>
    /// <param name="request">The target, the flag, and the state.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// THE CACHEABLE FLAG IS DECLARED IN THE SOURCE AND ABSENT FROM THE DOCUMENTED NINE [:L196-L197],
    /// which is why the surface is taken from the prototype block rather than from the specification's
    /// prose - the specification closes by saying it is incomplete [docs:L166]. All four flags are per
    /// expression, not per service, and each addressing form is carried because the name form resolves
    /// through a lookup that can fail.
    /// </remarks>
    public override Task<SetExpressionFlagResponse> SetExpressionFlag(
        SetExpressionFlagRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return Task.FromResult(
                new SetExpressionFlagResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
                });
        }

        bool enabled = request.Enabled;

        long code = request.Target?.AddressingCase switch
        {
            ColumnRef.AddressingOneofCase.Index => request.Flag switch
            {
                SetExpressionFlagRequest.Types.Flag.AlwaysCalc =>
                    engine.SetAlwaysCalc(request.Target.Index, enabled),
                SetExpressionFlagRequest.Types.Flag.Recursive =>
                    engine.SetRecursive(request.Target.Index, enabled),
                SetExpressionFlagRequest.Types.Flag.TriggerEvent =>
                    engine.SetTriggerEvent(request.Target.Index, enabled),
                SetExpressionFlagRequest.Types.Flag.Cacheable =>
                    engine.SetCacheable(request.Target.Index, enabled),
                _ => RetCode.E_INVALID_ARGUMENT,
            },

            ColumnRef.AddressingOneofCase.ColumnName => request.Flag switch
            {
                SetExpressionFlagRequest.Types.Flag.AlwaysCalc =>
                    engine.SetAlwaysCalc(request.Target.ColumnName, enabled),
                SetExpressionFlagRequest.Types.Flag.Recursive =>
                    engine.SetRecursive(request.Target.ColumnName, enabled),
                SetExpressionFlagRequest.Types.Flag.TriggerEvent =>
                    engine.SetTriggerEvent(request.Target.ColumnName, enabled),
                SetExpressionFlagRequest.Types.Flag.Cacheable =>
                    engine.SetCacheable(request.Target.ColumnName, enabled),
                _ => RetCode.E_INVALID_ARGUMENT,
            },

            _ => RetCode.E_INVALID_ARGUMENT,
        };

        return Task.FromResult(
            new SetExpressionFlagResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(code),
            });
    }

    // =================================================================================================
    //  CALCULATION - of_calc x4 [:L143, :L144, :L168, :L169], of_calcall x2 [:L145, :L170],
    //  of_calcempty x2 [:L199-L200], _of_calcitem [:L142]
    //  -----------------------------------------------------------------------------------------------
    //  NO GATE TEST HERE, DELIBERATELY. `EID_ITEMCHANGE` suppresses column-expression calculation by
    //  short-circuiting the RAW ITEM-CHANGE EVENT [se_cst_dw.sru:L182], so the engine is never reached;
    //  `of_calc` itself tests only `#Enabled` [:L1158]. Adding a gate test to these four entry points
    //  would suppress calculations the oracle performs - a behaviour change dressed as consistency,
    //  which C-B forbids. The gate lives on ColumnExpressionEventRelay, which is where the oracle puts
    //  it.
    // =================================================================================================

    /// <summary>
    /// <c>of_calc</c> in all four arities [:L143, :L144, :L168, :L169].
    /// </summary>
    /// <param name="request">The row, optionally a target, and optionally the force flag.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome and, for a single-target request, the resulting value.</returns>
    /// <remarks>
    /// <para>
    /// AN OVERLOAD COLLISION THE WIRE RESOLVES. At source
    /// <c>of_calc(readonly long row, readonly integer index)</c> and
    /// <c>of_calc(readonly long row, readonly boolean force)</c> are distinguished ONLY BY THE SECOND
    /// ARGUMENT'S TYPE; here they are distinguished by WHICH FIELD IS SET. A request setting both is
    /// therefore malformed rather than merely redundant, and is refused instead of having one of the two
    /// meanings chosen for it.
    /// </para>
    /// <para>
    /// <c>results</c> IS POPULATED ONLY FOR A SINGLE-TARGET REQUEST, AND THAT IS PRECISION RATHER THAN
    /// PARSIMONY. The contract defines it as one entry PER EXPRESSION EVALUATED. The engine - faithfully
    /// to a legacy that writes calculated values into the DataWindow and returns a bare code - does not
    /// report which of its expressions it evaluated, so for the whole-row form there is no truthful set
    /// to send: enumerating every bound expression would claim evaluations that may not have happened.
    /// An empty list is the honest answer, and the values are read from the DataWindow exactly as the
    /// legacy caller reads them.
    /// </para>
    /// </remarks>
    public override async Task<CalcResponse> Calc(CalcRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return new CalcResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
            };
        }

        bool hasTarget = request.Target is { AddressingCase: not ColumnRef.AddressingOneofCase.None };

        if (hasTarget && request.HasForce)
        {
            return new CalcResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT),
            };
        }

        long baseline = engine.RaisedErrorCount;
        CancellationToken cancellationToken = context.CancellationToken;
        long code;

        // ONE-BASED expression index, resolved before the call so the single result can name it.
        int index = 0;

        switch (request.Target?.AddressingCase)
        {
            case ColumnRef.AddressingOneofCase.Index:
                index = request.Target.Index;
                code = await engine
                    .CalcAsync(request.Row, index, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case ColumnRef.AddressingOneofCase.ColumnName:
                index = engine.FindExpIndex(request.Target.ColumnName);
                code = await engine
                    .CalcAsync(request.Row, request.Target.ColumnName, cancellationToken)
                    .ConfigureAwait(false);
                break;

            default:
                code = request.HasForce
                    ? await engine
                        .CalcAsync(request.Row, request.Force, cancellationToken)
                        .ConfigureAwait(false)
                    : await engine.CalcAsync(request.Row, cancellationToken).ConfigureAwait(false);
                break;
        }

        CalcResponse response = new()
        {
            RetCode = ExpressionWireProjection.ToWireRetCode(code),
            Error = ErrorRaisedSince(engine, baseline),
        };

        if (index > 0 && BuildCalcResult(engine, index, request.Row) is { } result)
        {
            response.Results.Add(result);
        }

        return response;
    }

    /// <summary>
    /// <c>of_calcall()</c> [:L145] and <c>of_calcall(readonly boolean force)</c> [:L170].
    /// </summary>
    /// <param name="request">Optionally the force flag.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// <c>results</c> is empty for the reason given on <see cref="Calc"/>: the engine does not report
    /// which expressions it evaluated, and claiming a set it did not report would be worse than sending
    /// none.
    /// </remarks>
    public override async Task<CalcAllResponse> CalcAll(
        CalcAllRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return new CalcAllResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
            };
        }

        long baseline = engine.RaisedErrorCount;

        long code = request.HasForce
            ? await engine
                .CalcAllAsync(request.Force, context.CancellationToken)
                .ConfigureAwait(false)
            : await engine.CalcAllAsync(context.CancellationToken).ConfigureAwait(false);

        return new CalcAllResponse
        {
            RetCode = ExpressionWireProjection.ToWireRetCode(code),
            Error = ErrorRaisedSince(engine, baseline),
        };
    }

    /// <summary>
    /// <c>of_calcempty(readonly long row)</c> [:L199] and <c>of_calcempty()</c> [:L200].
    /// </summary>
    /// <param name="request">Optionally the row; absent covers every row.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// DECLARED IN THE SOURCE AND ABSENT FROM THE DOCUMENTED NINE, like the cacheable flag. It
    /// calculates only the expressions currently marked empty, which is precisely why <c>results</c>
    /// cannot be filled by enumeration: the engine knows which those were and does not say.
    /// </remarks>
    public override async Task<CalcEmptyResponse> CalcEmpty(
        CalcEmptyRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return new CalcEmptyResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
            };
        }

        long baseline = engine.RaisedErrorCount;

        long code = request.HasRow
            ? await engine
                .CalcEmptyAsync(request.Row, context.CancellationToken)
                .ConfigureAwait(false)
            : await engine.CalcEmptyAsync(context.CancellationToken).ConfigureAwait(false);

        return new CalcEmptyResponse
        {
            RetCode = ExpressionWireProjection.ToWireRetCode(code),
            Error = ErrorRaisedSince(engine, baseline),
        };
    }

    /// <summary>
    /// <c>_of_calcitem(readonly long row, readonly integer index)</c> [:L142] - calculate ONE expression.
    /// </summary>
    /// <param name="request">The row and the ONE-BASED expression index.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The legacy's bare boolean, the resulting value, and a transport-level code.</returns>
    /// <remarks>
    /// <para>
    /// TWO ANOMALIES, BOTH CARRIED (C-B). It is declared PUBLIC despite the private-convention underscore
    /// prefix - every other <c>_of_*</c> member in the object is private or protected - and it answers
    /// <c>boolean</c> rather than a <c>RetCode</c>, so IT CANNOT REPORT WHY IT FAILED, only that it did.
    /// That is a real expressive limit of the legacy API and it is reproduced: the primary result here is
    /// a bool.
    /// </para>
    /// <para>
    /// THE NAME COULD NOT BE THE RPC IDENTIFIER AND IS NOT CLAIMED TO BE. A protobuf identifier must
    /// begin with a letter, so the RPC is <c>CalcItem</c> and the legacy spelling travels as
    /// machine-readable metadata on the method - <c>option (common.v1.legacy_name) = "_of_calcitem"</c>.
    /// A comparator reads it off the descriptor rather than inferring it, so the underscore is neither
    /// silently dropped nor falsely asserted to have survived.
    /// </para>
    /// <para>
    /// <c>ret_code</c> CARRIES TRANSPORT-LEVEL OUTCOMES ONLY - a missing session, a malformed index -
    /// which are conditions the in-process legacy could not have had. It is deliberately NOT used to
    /// smuggle a richer calculation verdict than the boolean, because that would widen the contract.
    /// </para>
    /// </remarks>
    public override async Task<CalcItemResponse> CalcItem(
        CalcItemRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return new CalcItemResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
            };
        }

        if (request.Index <= 0)
        {
            // Transport-level, and therefore the one thing ret_code is for here. There is no by-name
            // overload of this entry point, so an index is the only addressing it has.
            return new CalcItemResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT),
            };
        }

        long baseline = engine.RaisedErrorCount;

        bool succeeded = await engine
            ._of_calcitem(request.Row, request.Index, context.CancellationToken)
            .ConfigureAwait(false);

        CalcItemResponse response = new()
        {
            Succeeded = succeeded,
            RetCode = ExpressionWireProjection.ToWireRetCode(RetCode.OK),
            Error = ErrorRaisedSince(engine, baseline),
        };

        if (BuildCalcResult(engine, request.Index, request.Row) is { } result)
        {
            response.Result = result;
        }

        return response;
    }

    // =================================================================================================
    //  THE TWO GATES - #Enabled [n_cst_dwsvc.sru:L38] and #Trace [:L98]
    // =================================================================================================

    /// <summary>
    /// <c>of_setenabled(readonly boolean benabled)</c> [n_cst_dwsvc.sru:L89-L95] - THE VETOABLE GATE.
    /// </summary>
    /// <param name="request">The requested state.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome, whether anything changed, and the state after the call.</returns>
    /// <remarks>
    /// <para>
    /// *** A VETO MAPS TO <c>RetCode.FAILED</c> (-1), NOT TO <c>PREVENT</c> (+1). *** The oracle is
    /// <c>if Event OnEnable(bEnabled) = 1 then return RetCode.FAILED</c> [n_cst_dwsvc.sru:L90]: THE
    /// HANDLER SIGNALS WITH THE NUMERAL 1 AND THE FUNCTION TRANSLATES THAT SIGNAL INTO -1. A reasonable
    /// author would map a veto onto <c>PREVENT</c>, and that would be wrong here in a way that FLIPS
    /// every caller's branch - <c>Predicates.IsSucceeded</c> is <c>&gt;= 0</c>, so it reads
    /// <c>PREVENT</c> as a SUCCESS while <c>FAILED</c> is a failure. So for a vetoed change
    /// <c>IsPrevented</c> is FALSE and <c>IsFailed</c> is TRUE, and a caller testing for prevention will
    /// not see the veto. That is a legacy quirk, not an implementation error, and it is preserved
    /// exactly (C-B). The numeral 1 carries five distinct and non-interchangeable meanings in this
    /// refactor; this is one of them.
    /// </para>
    /// <para>
    /// THREE OUTCOMES, NOT TWO, WHICH IS WHY <c>changed</c> EXISTS. The idempotent early-out at
    /// [n_cst_dwsvc.sru:L89] returns success WITHOUT RAISING THE HOOK AT ALL, so "already in that state"
    /// and "changed" are both OK and only <c>changed</c> separates them. The early-out is observable and
    /// must not be simplified away: a derived service cannot rely on being asked about a no-op change.
    /// </para>
    /// <para>
    /// WHY A CONSUMER NEEDS THIS BEYOND INTROSPECTION: C-03's item-changed sequence invokes this service
    /// ONLY WHEN <c>#Enabled</c> is set [se_cst_dw.sru:L313-L315], so a consumer seeing no recalculation
    /// must be able to tell "disabled" from "nothing to do". Together with the <c>EID_ITEMCHANGE</c>
    /// coupling those are the two independent ways recalculation can be silently off.
    /// </para>
    /// </remarks>
    public override Task<SetEnabledResponse> SetEnabled(
        SetEnabledRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return Task.FromResult(
                new SetEnabledResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
                });
        }

        // Captured BEFORE the call, because [:L89] returns without touching anything when they match.
        bool wasDifferent = engine.Enabled != request.Enabled;

        long code = engine.SetEnabled(request.Enabled);

        return Task.FromResult(
            new SetEnabledResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(code),

                // True only when the state genuinely moved: a veto answers FAILED and changes nothing,
                // and a matching request answers OK and changes nothing.
                Changed = wasDifferent && code == RetCode.OK,
                Enabled = engine.Enabled,
            });
    }

    /// <summary>
    /// <c>of_settrace(readonly boolean trace)</c> [:L208, implemented :L2409-L2411] - the NON-vetoable
    /// gate.
    /// </summary>
    /// <param name="request">The requested state.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome and the state after the call.</returns>
    /// <remarks>
    /// NOT VETOABLE, UNLIKE <c>#Enabled</c>: it assigns and returns OK unconditionally [:L2409-L2410].
    /// The asymmetry between the two gate setters is the legacy's and is preserved. Setting it does NOT
    /// subscribe anybody to <c>TraceChannel</c>, and subscribing does not set it - they are two different
    /// acts, which is why both flags are published together by <see cref="GetServiceState"/>.
    /// </remarks>
    public override Task<SetTraceResponse> SetTrace(
        SetTraceRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return Task.FromResult(
                new SetTraceResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
                });
        }

        long code = engine.SetTrace(request.Trace);

        return Task.FromResult(
            new SetTraceResponse
            {
                RetCode = ExpressionWireProjection.ToWireRetCode(code),
                Trace = engine.Trace,
            });
    }

    /// <summary>
    /// Both gate states together, because a consumer diagnosing a missing recalculation needs both.
    /// </summary>
    /// <param name="request">The session and DataWindow.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The two flags.</returns>
    public override Task<GetServiceStateResponse> GetServiceState(
        GetServiceStateRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return Task.FromResult(
                new GetServiceStateResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
                });
        }

        return Task.FromResult(
            new GetServiceStateResponse
            {
                Enabled = engine.Enabled,
                Trace = engine.Trace,
                RetCode = ExpressionWireProjection.ToWireRetCode(RetCode.OK),
            });
    }

    // =================================================================================================
    //  THE ENGINE-STATE SNAPSHOT - the operation that makes the seven structures [:L22-L83] reachable
    // =================================================================================================

    /// <summary>
    /// Reads the expression table, the reverse dependency index, the grammar sentinels and - on request -
    /// the global variable table and the three-part bindings.
    /// </summary>
    /// <param name="request">The session, DataWindow, an optional column filter, and the binding switch.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The snapshot.</returns>
    /// <remarks>
    /// <para>
    /// READ-ONLY BY DESIGN. State is mutated through the typed operations above and never by posting a
    /// snapshot back, so there is no path by which a caller could write a structure the parser is
    /// responsible for maintaining.
    /// </para>
    /// <para>
    /// THE COLUMN MAP IS KEYED BY THE ONE-BASED DATAWINDOW ORDINAL. The engine holds the reverse index in
    /// a one-based array whose position IS the column id [:L218-L219], so position <c>n</c> becomes key
    /// <c>n</c> - and a column that has never changed simply has no entry, which is
    /// <c>CLC_UNKNOWN</c> BY ABSENCE rather than by a stored zero. That sparseness is the engine's own
    /// [:L234-L235] and is preserved rather than densified.
    /// </para>
    /// <para>
    /// BINDINGS ARE OPTIONAL BECAUSE THEY ARE EXPENSIVE, NOT BECAUSE THEY ARE SECONDARY. When asked for,
    /// one is emitted per expression in index order, each carrying all three components; see the file
    /// header for why fewer than three is not a smaller answer but a wrong one.
    /// </para>
    /// </remarks>
    public override Task<GetExpressionStateResponse> GetExpressionState(
        GetExpressionStateRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is not { } engine)
        {
            return Task.FromResult(
                new GetExpressionStateResponse
                {
                    RetCode = ExpressionWireProjection.ToWireRetCode(resolution.ReturnCode),
                });
        }

        GetExpressionStateResponse response = new()
        {
            Sentinels = ExpressionWireProjection.Sentinels(),
            RetCode = ExpressionWireProjection.ToWireRetCode(RetCode.OK),
        };

        string filter = request.ColumnName?.Trim() ?? string.Empty;
        ImmutableArray<ColumnExpressionData> expressions = engine.Expressions;

        for (int position = 0; position < expressions.Length; position++)
        {
            ColumnExpressionData data = expressions[position];

            if (filter.Length > 0
                && !string.Equals(data.Name, filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            response.Expressions.Add(ExpressionWireProjection.ToWire(data));

            if (!request.IncludeBindings)
            {
                continue;
            }

            // The engine's array is zero-based in memory and ONE-BASED in the contract, so the binding
            // for wire element `position` is fetched with `position + 1`.
            if (engine.GetBinding(position + 1) is { } snapshot)
            {
                response.Bindings.Add(
                    ExpressionWireProjection.ToWire(snapshot, engine.Variables));
            }
        }

        ImmutableArray<ColumnCalcData> columnStates = engine.ColumnCalcStates;

        for (int position = 0; position < columnStates.Length; position++)
        {
            // Position + 1 IS the one-based DataWindow column ordinal.
            response.Columns[position + 1] = ExpressionWireProjection.ToWire(columnStates[position]);
        }

        ExpressionVariableEnvironment variables = engine.Variables;

        for (int index = 1; index <= variables.UpperBound; index++)
        {
            GlobalVariable variable = variables[index];

            // Resolvability is the session's answer, not a guess: a foreign entry is resolvable only
            // while its handle is co-resident in this very session.
            bool resolvable = variable.VarType == ExpressionVariableEnvironment.VAR_FOREIGN
                && resolution.Session is { } session
                && session.IsCoResident(DataWindowHandle.From(variable.Foreign.Handle));

            response.GlobalVars.Add(ExpressionWireProjection.ToWire(variable, resolvable));
        }

        return Task.FromResult(response);
    }

    // =================================================================================================
    //  THE THREE STREAMS
    // =================================================================================================

    /// <summary>
    /// Server stream of the three events the engine declares on itself [:L86-L88].
    /// </summary>
    /// <param name="request">The session, DataWindow and optional column filter.</param>
    /// <param name="responseStream">The stream to write events to.</param>
    /// <param name="context">The call context.</param>
    /// <returns>A task that completes when the client disconnects or the call is cancelled.</returns>
    /// <remarks>
    /// A SERVER STREAM AND NOT AN INVERTED ONE: these are notifications the engine EMITS, not questions
    /// it asks, so there is nothing to answer. Sequencing tokens are FOR DETECTION - the discipline is
    /// <c>ORDERING_DISCIPLINE_SEQUENCED</c>, pattern (a) - and a consumer may reorder on the token rather
    /// than fail on a gap. Delivery is bounded and oldest-dropping so that observing events cannot stall
    /// a calculation; see <see cref="ColumnExpressionEventRelay"/> for what the relay does and does not
    /// observe.
    /// </remarks>
    public override async Task EventStream(
        EventStreamRequest request,
        IServerStreamWriter<EventStreamResponse> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        EngineResolution resolution = Resolve(request.SessionId, request.DatawindowHandle);

        if (resolution.Engine is null)
        {
            // A stream has no response body to carry a code before it opens, so a precondition failure
            // here is a status rather than a ret_code. See the class remarks.
            throw BuildRpcException(resolution.ReturnCode, "EventStream");
        }

        using ColumnExpressionEventSubscription subscription = _eventRelay.Subscribe(
            request.SessionId,
            request.DatawindowHandle,
            request.ColumnName);

        try
        {
            await foreach (EventStreamResponse response in subscription
                .Events
                .ReadAllAsync(context.CancellationToken)
                .ConfigureAwait(false))
            {
                await responseStream.WriteAsync(response).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // The client went away or the host is shutting down. Ending the stream is the whole of the
            // correct response; a status would describe the client's own action back to it.
        }
    }

    /// <summary>
    /// INVERTED STREAM 1 - macro invocation. The server asks; the client answers.
    /// </summary>
    /// <param name="requestStream">The client's answers, correlated by invocation identifier.</param>
    /// <param name="responseStream">The server's questions.</param>
    /// <param name="context">The call context, whose metadata names the session and DataWindow.</param>
    /// <returns>A task that completes when the client closes the stream or the call is cancelled.</returns>
    /// <remarks>
    /// <para>
    /// THE IDENTITY OF THIS CHANNEL COMES FROM CALL METADATA, NOT FROM THE STREAM. The client-to-server
    /// message carries only an invocation identifier, a result, an unhandled flag and an error - there is
    /// nothing in it to name a session or a DataWindow with. C-G additionally requires that a long-lived
    /// channel's identity be established BEFORE it opens rather than re-derived from what flows over it,
    /// and metadata satisfies both: it arrives on the initial request headers, on the same authenticated
    /// call. The two header names are published on
    /// <see cref="ColumnExpressionChannelHeaders"/>.
    /// </para>
    /// <para>
    /// STRICTLY SYNCHRONOUS, AND EVERY BREACH IS A HARD ERROR. A second servicer for one DataWindow, an
    /// answer with nothing outstanding, an answer naming a different invocation, and a disconnect while a
    /// question is unanswered are all refused rather than absorbed - because letting a value computed for
    /// one macro be substituted into another's expression is silent data corruption. That is the
    /// fail-fast posture the AAP requires be preserved AS fail-fast.
    /// </para>
    /// <para>
    /// A CLIENT THAT DOES NOT SERVICE THIS CHANNEL STALLS ITS OWN CALCULATIONS, which the contract states
    /// outright. No fallback macro implementation is invented, because there is nothing to invent one
    /// from: a default value, a zero or an empty string would substitute a WRONG answer for a MISSING
    /// one.
    /// </para>
    /// </remarks>
    public override async Task InvokeMethodChannel(
        IAsyncStreamReader<InvokeMethodResponse> requestStream,
        IServerStreamWriter<InvokeMethodRequest> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(requestStream);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        string sessionId = ColumnExpressionChannelHeaders.Read(
            context.RequestHeaders,
            ColumnExpressionChannelHeaders.SessionId);

        string dataWindowHandle = ColumnExpressionChannelHeaders.Read(
            context.RequestHeaders,
            ColumnExpressionChannelHeaders.DataWindowHandle);

        if (sessionId.Length == 0 || dataWindowHandle.Length == 0)
        {
            throw new RpcException(
                new Status(
                    StatusCode.InvalidArgument,
                    "InvokeMethodChannel requires the '"
                        + ColumnExpressionChannelHeaders.SessionId
                        + "' and '"
                        + ColumnExpressionChannelHeaders.DataWindowHandle
                        + "' call metadata headers. The client-to-server message carries no session or "
                        + "handle, so the channel cannot be identified from its payload."));
        }

        EngineResolution resolution = Resolve(sessionId, dataWindowHandle);

        if (resolution.Engine is null)
        {
            throw BuildRpcException(resolution.ReturnCode, "InvokeMethodChannel");
        }

        MacroChannelRegistration registration;

        try
        {
            registration = _macroRouter.Attach(sessionId, dataWindowHandle, responseStream);
        }
        catch (InvalidOperationException alreadyAttached)
        {
            throw new RpcException(
                new Status(StatusCode.AlreadyExists, alreadyAttached.Message, alreadyAttached));
        }

        using (registration)
        {
            try
            {
                await foreach (InvokeMethodResponse answer in requestStream
                    .ReadAllAsync(context.CancellationToken)
                    .ConfigureAwait(false))
                {
                    registration.Deliver(answer);
                }
            }
            catch (OperationCanceledException)
                when (context.CancellationToken.IsCancellationRequested)
            {
                // Cancelled or disconnected. Disposal below fails any outstanding invocation, which is
                // the only thing the engine needs to learn from it.
            }
            catch (MacroProtocolViolationException violation)
            {
                _logger?.LogError(
                    violation,
                    "The macro invocation channel for expression session {SessionId} broke the "
                        + "synchronous discipline, so the channel was failed rather than resynchronised.",
                    sessionId);

                throw new RpcException(
                    new Status(StatusCode.FailedPrecondition, violation.Message, violation));
            }
        }
    }

    /// <summary>
    /// INVERTED STREAM 2 - the expression trace, gated on <c>#Trace</c> [:L98].
    /// </summary>
    /// <param name="requestStream">Subscribe and unsubscribe messages, each naming a DataWindow.</param>
    /// <param name="responseStream">The trace records.</param>
    /// <param name="context">The call context.</param>
    /// <returns>A task that completes when the client closes the stream or the call is cancelled.</returns>
    /// <remarks>
    /// <para>
    /// DRIVEN BY ITS REQUEST STREAM, unlike the macro channel: <c>TraceChannelRequest</c> carries the
    /// session, the handle and a subscribe flag, so no metadata is needed and one channel may observe
    /// several DataWindows. The asymmetry between the two streams is the contract's, not this file's.
    /// </para>
    /// <para>
    /// SUBSCRIBING IS NOT ENABLING. Emission is gated on the engine's own <c>#Trace</c>, which this call
    /// does not touch - so a subscriber on a service whose flag is clear correctly receives nothing, and
    /// <see cref="GetServiceState"/> exists so that a silent channel can be told apart from a disabled
    /// one.
    /// </para>
    /// <para>
    /// FIRE-AND-FORGET, pattern (a). Records carry a sequencing token so a consumer can detect a gap, and
    /// a subscriber that falls behind loses its OLDEST records rather than stalling the calculation that
    /// produced them.
    /// </para>
    /// </remarks>
    public override async Task TraceChannel(
        IAsyncStreamReader<TraceChannelRequest> requestStream,
        IServerStreamWriter<TraceRecord> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(requestStream);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        Dictionary<string, TracePump> pumps = new(StringComparer.Ordinal);

        // gRPC forbids concurrent writes to one stream, and several DataWindows may be pumped onto this
        // one, so every write goes through a single gate.
        using SemaphoreSlim writeGate = new(1, 1);

        try
        {
            await foreach (TraceChannelRequest message in requestStream
                .ReadAllAsync(context.CancellationToken)
                .ConfigureAwait(false))
            {
                string key = message.SessionId + "\n" + message.DatawindowHandle;

                if (message.Subscribe)
                {
                    if (pumps.ContainsKey(key))
                    {
                        continue;
                    }

                    EngineResolution resolution =
                        Resolve(message.SessionId, message.DatawindowHandle);

                    if (resolution.Engine is not { } engine)
                    {
                        throw BuildRpcException(resolution.ReturnCode, "TraceChannel");
                    }

                    ExpressionTraceSubscription subscription = _traceBroker.Subscribe(
                        message.SessionId,
                        message.DatawindowHandle);

                    pumps[key] = new TracePump(
                        subscription,
                        PumpTraceAsync(
                            subscription,
                            engine,
                            responseStream,
                            writeGate,
                            context.CancellationToken));
                }
                else if (pumps.Remove(key, out TracePump? pump))
                {
                    await pump.StopAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Cancelled or disconnected; the pumps are stopped below either way.
        }
        finally
        {
            foreach (TracePump pump in pumps.Values)
            {
                await pump.StopAsync().ConfigureAwait(false);
            }
        }
    }

    // =================================================================================================
    //  RESOLUTION, COMPOSITION AND DISPATCH - the private half
    // =================================================================================================

    /// <summary>
    /// Resolves a session identifier and a DataWindow handle to the engine they name.
    /// </summary>
    /// <param name="sessionId">The session identifier from the request.</param>
    /// <param name="dataWindowHandle">The session-scoped handle from the request.</param>
    /// <returns>The engine and its session, or the code that says why not.</returns>
    /// <remarks>
    /// <para>
    /// THE CODES FOLLOW THE SIBLING SESSION LAYER'S CONVENTION so that a caller sees one vocabulary
    /// across C-03 and C-04: a blank identifier is <c>E_INVALID_ARGUMENT</c>; a session that is unknown,
    /// closed or idle-expired is <c>E_NOT_EXISTS</c>, because the registry has already collapsed those
    /// three into one fact - the session named is not usable - and reporting a distinction it does not
    /// keep would be an invention; a handle that is not co-resident is <c>E_INVALID_HANDLE</c>, the same
    /// code the session's own BLOCKED error carries.
    /// </para>
    /// <para>
    /// RESOLUTION IS ALSO A TOUCH. The registry's own lookup renews the idle timer, so a session in
    /// active use does not expire underneath its user.
    /// </para>
    /// </remarks>
    private EngineResolution Resolve(string? sessionId, string? dataWindowHandle)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(dataWindowHandle))
        {
            return new EngineResolution(null, null, RetCode.E_INVALID_ARGUMENT);
        }

        if (!_sessions.TryGet(sessionId, out ExpressionSession? session) || session is null)
        {
            return new EngineResolution(null, null, RetCode.E_NOT_EXISTS);
        }

        DataWindowHandle handle = DataWindowHandle.From(dataWindowHandle);

        if (!session.TryGetHost(handle, out IExpressionServiceHost? host))
        {
            return new EngineResolution(null, session, RetCode.E_INVALID_HANDLE);
        }

        if (host is not ColumnExpressionEngine engine)
        {
            // Structurally impossible through this service, which registers nothing else. Reported rather
            // than assumed away, because a silently ignored impossible state is how an invariant stops
            // being one.
            return new EngineResolution(null, session, RetCode.E_INVALID_OBJECT);
        }

        return new EngineResolution(engine, session, RetCode.OK);
    }

    /// <summary>
    /// Builds the engine for one DataWindow inside one session, wired to both inverted streams.
    /// </summary>
    /// <param name="session">The session the engine joins, and which mints its handle.</param>
    /// <param name="host">The DataWindow the engine attaches to.</param>
    /// <returns>The engine, registered and attached.</returns>
    /// <remarks>
    /// <para>
    /// THE ORDER IS FORCED, NOT CHOSEN. The constructor registers the engine with the session, and THAT
    /// REGISTRATION IS WHAT MINTS THE HANDLE - so the handle does not exist until construction returns.
    /// This is why the macro channel is keyed by SESSION and routes on the handle the invoker stamps onto
    /// each invocation: a channel keyed by handle could not be built in time to be passed in.
    /// </para>
    /// <para>
    /// THE SESSION IS SUPPLIED RATHER THAN OWNED, WHICH IS THE WHOLE POINT. An engine that builds its own
    /// private session is the only DataWindow in it, so every foreign or context reference from it is
    /// BLOCKED - correct for a standalone engine and useless for a service. Passing the shared session is
    /// what makes co-residency, and therefore cross-DataWindow expansion, possible at all.
    /// </para>
    /// <para>
    /// NO TRACE SINK IS PASSED, DELIBERATELY: the engine honours one only when it OWNS its session, and
    /// this session already carries the broker it was built with. Passing one here would be silently
    /// ignored, which is worse than not passing it.
    /// </para>
    /// <para>
    /// THE INVOCATION TIMEOUT IS A BACKSTOP, NOT A BUDGET, AND IT IS DERIVED RATHER THAN INVENTED. The
    /// ordinary bound on a macro invocation is the calculating call's own cancellation - which is real
    /// now that every shipped client attaches a gRPC deadline, so an abandoned calculation is cancelled
    /// and the invocation with it. What that does NOT cover is a caller that attaches no deadline at
    /// all: this service cannot require one, and against such a caller an unserviced macro channel
    /// would hold the session, its engines and the calculating call open indefinitely. The backstop is
    /// therefore <c>DataServices:ColumnExpression:MacroInvocationTimeout</c>, whose default IS the
    /// expression session's own idle lifetime: an invocation still outstanding past the point at which
    /// the session it belongs to would have been reclaimed is holding something the service's own policy
    /// had already given up on.
    /// </para>
    /// <para>
    /// THE ORIGINAL OBJECTION IS PRESERVED, NOT OVERRULED. A timeout invented here would turn a
    /// client's omission into a fabricated macro result, and that is still true - which is why this
    /// value is an order of magnitude away from any plausible invocation and why an elapsed timeout is
    /// reported as its own defined outcome rather than as a value. A properly deadlined client never
    /// reaches it; a client with no deadline reaches it instead of stalling the session forever.
    /// </para>
    /// </remarks>
    private ColumnExpressionEngine CreateEngine(
        ExpressionSession session,
        DataWindowServiceHost host)
    {
        DataWindowExpressionEvaluator evaluator = new(host, _pinyinMatcher, _pageResolver);

        MacroInvoker macroInvoker = new(
            _macroRouter.ChannelFor(session.SessionId),
            _macroInvocationBackstop,
            _timeProvider);

        ColumnExpressionEngine engine = new(
            _columnExpression,
            evaluator,
            macroInvoker,
            session,
            syntaxMutator: null,
            traceSink: null,
            _timeProvider,
            _logger);

        // n_cst_dwsvc.sru:L85-L86 - attach the host and lift its broker off it, in that order, in one
        // call. The evaluator supplied above is left alone by the engine's override.
        engine.OnInit(host);

        return engine;
    }

    /// <summary>
    /// Answers the error the engine raised during the call just made, and nothing older.
    /// </summary>
    /// <param name="engine">The engine.</param>
    /// <param name="baseline">The engine's raised-error count captured immediately before the call.</param>
    /// <returns>The wire error, or <see langword="null"/> when the call raised none.</returns>
    /// <remarks>
    /// THE COUNT IS THE TEST, NOT THE PRESENCE OF A LAST ERROR. The engine retains its most recent error
    /// indefinitely, so a successful call would otherwise appear to have failed by inheriting an error
    /// from a previous one - a false attribution that is entirely plausible on inspection and completely
    /// wrong.
    /// </remarks>
    private static ExpressionError? ErrorRaisedSince(ColumnExpressionEngine engine, long baseline) =>
        engine.RaisedErrorCount > baseline
            ? ExpressionWireProjection.ToWire(engine.LastError)
            : null;

    /// <summary>
    /// Turns a resolution code into the status a streaming call must fail with.
    /// </summary>
    /// <param name="code">The resolution code.</param>
    /// <param name="operation">The RPC name, for the message.</param>
    /// <returns>The exception to throw.</returns>
    /// <remarks>
    /// ONLY STREAMING CALLS USE THIS. A unary call carries its outcome in its own <c>ret_code</c>, which
    /// preserves the tri-state algebra a transport status cannot express; a stream has no body to put a
    /// code in before it opens. The mapping is deliberately narrow, and an unmapped code becomes
    /// <c>FailedPrecondition</c> rather than <c>Unknown</c>, because every code that reaches here is a
    /// statement about the session or handle the caller supplied.
    /// </remarks>
    private static RpcException BuildRpcException(long code, string operation)
    {
        StatusCode status = code switch
        {
            RetCode.E_INVALID_ARGUMENT => StatusCode.InvalidArgument,
            RetCode.E_NOT_EXISTS => StatusCode.NotFound,
            RetCode.E_OBJECT_NOT_FOUND => StatusCode.NotFound,
            RetCode.E_BUSY => StatusCode.ResourceExhausted,
            RetCode.E_NO_IMPLEMENTATION => StatusCode.Unimplemented,
            RetCode.E_NO_SUPPORT => StatusCode.Unimplemented,
            _ => StatusCode.FailedPrecondition,
        };

        return new RpcException(
            new Status(
                status,
                operation
                    + " could not resolve the expression session and DataWindow handle it was given. "
                    + "RetCode "
                    + code.ToString(CultureInfo.InvariantCulture)
                    + " ("
                    + Formatting.FormatRetCode(code)
                    + ")."));
    }

    /// <summary>
    /// Builds the single result for a calculation that named exactly one expression.
    /// </summary>
    /// <param name="engine">The engine.</param>
    /// <param name="index">The ONE-BASED expression index.</param>
    /// <param name="row">The ONE-BASED row ordinal.</param>
    /// <returns>The result, or <see langword="null"/> when it cannot be read truthfully.</returns>
    /// <remarks>
    /// <para>
    /// ONE-BASED IN, ZERO-BASED OUT, ONCE. The engine publishes its expressions as an ordinary array, so
    /// the one-based contract index becomes <c>index - 1</c> here - the only place that arithmetic
    /// happens on this path.
    /// </para>
    /// <para>
    /// <c>evaluated_exp</c> AND <c>from_cache</c> ARE LEFT UNSET BECAUSE THE ENGINE DOES NOT PUBLISH
    /// THEM, and a plausible guess at either would be worse than their absence: the preprocessed text is
    /// built inside the calculation and discarded, and whether a value was served from the compute cache
    /// is not reported. Nothing here reconstructs them.
    /// </para>
    /// <para>
    /// THE VALUE IS READ BACK THROUGH THE HOST, TYPED WHERE THE ABSTRACT HOST CONTRACT ALLOWS. It
    /// publishes three reads - string, decimal and number - and only the string read is defined for every
    /// column type, so a string, integer or decimal column is reported in its own type and the three
    /// temporal types are reported as the DataWindow's own rendering. The declared type always travels
    /// separately on <c>ColumnExpData.col_type</c>, so nothing is lost and nothing is guessed.
    /// </para>
    /// </remarks>
    private static CalcResult? BuildCalcResult(ColumnExpressionEngine engine, int index, long row)
    {
        ImmutableArray<ColumnExpressionData> expressions = engine.Expressions;

        if (index < 1 || index > expressions.Length)
        {
            return null;
        }

        ColumnExpressionData data = expressions[index - 1];

        if (engine.DataWindow is not { } host || row < 1 || row > host.RowCount())
        {
            // Outside the buffer there is no value to report. Checked positively rather than discovered
            // by catching whatever the host raises.
            return null;
        }

        return new CalcResult
        {
            ColumnName = data.Name,
            ColumnId = data.Id,
            Row = row,
            Value = ReadItemValue(host, data, row),
        };
    }

    /// <summary>Reads one calculated item back through the host.</summary>
    /// <param name="host">The DataWindow.</param>
    /// <param name="data">The expression whose column is being read.</param>
    /// <param name="row">The ONE-BASED row ordinal.</param>
    /// <returns>The value, with <c>is_null</c> set when the item is null.</returns>
    private static WireAnyValue ReadItemValue(
        DataWindowServiceHost host,
        ColumnExpressionData data,
        long row)
    {
        switch (data.ColType)
        {
            case DataWindowServiceBase.COL_TYPE_INTEGER:
                // An integer column's decimal read is exact, so the narrowing cast cannot lose a digit.
                return host.GetItemDecimal(row, data.Name) is { } integral
                    ? new WireAnyValue { Int64Value = (long)integral }
                    : new WireAnyValue { IsNull = true };

            case DataWindowServiceBase.COL_TYPE_DECIMAL:
                return host.GetItemDecimal(row, data.Name) is { } number
                    ? ExpressionWireProjection.ToWire(number)
                    : new WireAnyValue { IsNull = true };

            default:
                // String and the three temporal types. NULL IS NOT THE EMPTY STRING: the contract states
                // that is_null and string_value = "" are different values, so the two are kept apart here
                // rather than folded together for convenience.
                return host.GetItemString(row, data.Name) is { } text
                    ? new WireAnyValue { StringValue = text }
                    : new WireAnyValue { IsNull = true };
        }
    }

    /// <summary>
    /// Resolves the column reference a trace record names, enriching it from the engine when it can.
    /// </summary>
    /// <param name="engine">The engine that emitted the trace.</param>
    /// <param name="columnName">The column name carried on the record.</param>
    /// <returns>The wire reference.</returns>
    /// <remarks>
    /// The oracle's trace event passes the whole DataWindow object [:L758] while the record retains its
    /// name, so the identifier and column type are recovered from the engine's own expression table where
    /// a match exists and left unset where it does not. Nothing is fabricated to fill the message.
    /// </remarks>
    private static DwObjectRef BuildDwObjectRef(ColumnExpressionEngine engine, string columnName)
    {
        foreach (ColumnExpressionData data in engine.Expressions)
        {
            if (string.Equals(data.Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return data.Dwo is not null
                    ? ExpressionWireProjection.ToWire(data.Dwo)
                    : new DwObjectRef { Name = columnName, Id = data.Id };
            }
        }

        return new DwObjectRef { Name = columnName ?? string.Empty };
    }

    /// <summary>Pumps one DataWindow's trace onto the shared response stream.</summary>
    /// <param name="subscription">The subscription to drain.</param>
    /// <param name="engine">The engine, for column enrichment.</param>
    /// <param name="responseStream">The shared stream.</param>
    /// <param name="writeGate">Serialises writes across every pump on this stream.</param>
    /// <param name="cancellationToken">Ends the pump when the call ends.</param>
    /// <returns>A task that completes when the subscription is disposed or the call ends.</returns>
    private async Task PumpTraceAsync(
        ExpressionTraceSubscription subscription,
        ColumnExpressionEngine engine,
        IServerStreamWriter<TraceRecord> responseStream,
        SemaphoreSlim writeGate,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ExpressionTraceRecord record in subscription
                .Records
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                TraceRecord projected = ExpressionWireProjection.ToWire(
                    record,
                    BuildDwObjectRef(engine, record.ColumnName));

                await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    await responseStream.WriteAsync(projected).ConfigureAwait(false);
                }
                finally
                {
                    _ = writeGate.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Unsubscribed or the call ended. Ending the pump is the whole of the correct response; the
            // trace is fire-and-forget, so an undelivered record is a diagnostic gap and never a fault.
        }
        catch (RpcException transportFault)
        {
            // The client stopped reading. Logged at debug because a consumer abandoning a diagnostic
            // stream is ordinary, and rethrowing would turn it into a service error.
            _logger?.LogDebug(
                transportFault,
                "The trace channel for DataWindow {Handle} stopped accepting records.",
                subscription.DataWindowHandle);
        }
    }

    /// <summary>
    /// <c>of_addvar</c> [:L153-L159] - selects the typed overload from the union arm.
    /// </summary>
    /// <param name="engine">The engine.</param>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The typed value.</param>
    /// <returns>The engine's return code, or <c>E_INVALID_ARGUMENT</c> for a malformed request.</returns>
    /// <remarks>
    /// A BLANK OR UNPARSEABLE TEMPORAL VALUE IS MALFORMED, NOT NULL. The canonical forms are non-empty,
    /// so an empty one carries no information about whether the caller meant null or made a mistake -
    /// and guessing "null" would put a value into the table that the caller never sent.
    /// </remarks>
    private static long DispatchAddVar(ColumnExpressionEngine engine, string name, VarValue? value)
    {
        switch (value?.KindCase)
        {
            case VarValue.KindOneofCase.TimeValue:
                return ExpressionWireProjection.ParseTime(value.TimeValue?.Value) is { } time
                    ? engine.AddVar(name, time)
                    : RetCode.E_INVALID_ARGUMENT;

            case VarValue.KindOneofCase.StringValue:
                return engine.AddVar(name, value.StringValue);

            case VarValue.KindOneofCase.LongValue:
                return engine.AddVar(name, value.LongValue);

            // `double`, NOT `decimal` [:L156]. The legacy declares no decimal variable overload.
            case VarValue.KindOneofCase.DoubleValue:
                return engine.AddVar(name, value.DoubleValue);

            case VarValue.KindOneofCase.DatetimeValue:
                return ExpressionWireProjection.ParseDateTime(value.DatetimeValue?.Value)
                        is { } moment
                    ? engine.AddVar(name, moment)
                    : RetCode.E_INVALID_ARGUMENT;

            case VarValue.KindOneofCase.DateValue:
                return ExpressionWireProjection.ParseDate(value.DateValue?.Value) is { } day
                    ? engine.AddVar(name, day)
                    : RetCode.E_INVALID_ARGUMENT;

            // The one non-nullable overload, matching `readonly boolean value` [:L159].
            case VarValue.KindOneofCase.BoolValue:
                return engine.AddVar(name, value.BoolValue);

            default:
                return RetCode.E_INVALID_ARGUMENT;
        }
    }

    /// <summary>
    /// <c>of_setvar</c> across seven types and three arities [:L160-L166, :L179-L185, :L188-L194].
    /// </summary>
    /// <param name="engine">The engine.</param>
    /// <param name="request">The request carrying the name, value and the two optional flags.</param>
    /// <param name="cancellationToken">Cancels the recalculation the set may drive.</param>
    /// <returns>The engine's return code, or <c>E_INVALID_ARGUMENT</c> for a malformed request.</returns>
    private static async ValueTask<long> DispatchSetVarAsync(
        ColumnExpressionEngine engine,
        SetVariableRequest request,
        CancellationToken cancellationToken)
    {
        VarValue? value = request.Value;
        string name = request.Name;
        bool hasRecalc = request.HasRecalc;
        bool hasForce = request.HasForce;

        // The legacy declares (value), (value, recalc) and (value, recalc, force) and no fourth shape, so
        // force alone names no overload and is refused rather than promoted.
        if (hasForce && !hasRecalc)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        bool recalc = request.Recalc;
        bool force = request.Force;

        switch (value?.KindCase)
        {
            case VarValue.KindOneofCase.TimeValue:
            {
                if (ExpressionWireProjection.ParseTime(value.TimeValue?.Value) is not { } time)
                {
                    return RetCode.E_INVALID_ARGUMENT;
                }

                return hasForce
                    ? await engine
                        .SetVarAsync(name, time, recalc, force, cancellationToken)
                        .ConfigureAwait(false)
                    : hasRecalc
                        ? await engine
                            .SetVarAsync(name, time, recalc, cancellationToken)
                            .ConfigureAwait(false)
                        : await engine
                            .SetVarAsync(name, time, cancellationToken)
                            .ConfigureAwait(false);
            }

            case VarValue.KindOneofCase.StringValue:
            {
                string text = value.StringValue;

                return hasForce
                    ? await engine
                        .SetVarAsync(name, text, recalc, force, cancellationToken)
                        .ConfigureAwait(false)
                    : hasRecalc
                        ? await engine
                            .SetVarAsync(name, text, recalc, cancellationToken)
                            .ConfigureAwait(false)
                        : await engine
                            .SetVarAsync(name, text, cancellationToken)
                            .ConfigureAwait(false);
            }

            case VarValue.KindOneofCase.LongValue:
            {
                long integral = value.LongValue;

                return hasForce
                    ? await engine
                        .SetVarAsync(name, integral, recalc, force, cancellationToken)
                        .ConfigureAwait(false)
                    : hasRecalc
                        ? await engine
                            .SetVarAsync(name, integral, recalc, cancellationToken)
                            .ConfigureAwait(false)
                        : await engine
                            .SetVarAsync(name, integral, cancellationToken)
                            .ConfigureAwait(false);
            }

            case VarValue.KindOneofCase.DoubleValue:
            {
                double real = value.DoubleValue;

                return hasForce
                    ? await engine
                        .SetVarAsync(name, real, recalc, force, cancellationToken)
                        .ConfigureAwait(false)
                    : hasRecalc
                        ? await engine
                            .SetVarAsync(name, real, recalc, cancellationToken)
                            .ConfigureAwait(false)
                        : await engine
                            .SetVarAsync(name, real, cancellationToken)
                            .ConfigureAwait(false);
            }

            case VarValue.KindOneofCase.DatetimeValue:
            {
                if (ExpressionWireProjection.ParseDateTime(value.DatetimeValue?.Value)
                    is not { } moment)
                {
                    return RetCode.E_INVALID_ARGUMENT;
                }

                return hasForce
                    ? await engine
                        .SetVarAsync(name, moment, recalc, force, cancellationToken)
                        .ConfigureAwait(false)
                    : hasRecalc
                        ? await engine
                            .SetVarAsync(name, moment, recalc, cancellationToken)
                            .ConfigureAwait(false)
                        : await engine
                            .SetVarAsync(name, moment, cancellationToken)
                            .ConfigureAwait(false);
            }

            case VarValue.KindOneofCase.DateValue:
            {
                if (ExpressionWireProjection.ParseDate(value.DateValue?.Value) is not { } day)
                {
                    return RetCode.E_INVALID_ARGUMENT;
                }

                return hasForce
                    ? await engine
                        .SetVarAsync(name, day, recalc, force, cancellationToken)
                        .ConfigureAwait(false)
                    : hasRecalc
                        ? await engine
                            .SetVarAsync(name, day, recalc, cancellationToken)
                            .ConfigureAwait(false)
                        : await engine
                            .SetVarAsync(name, day, cancellationToken)
                            .ConfigureAwait(false);
            }

            case VarValue.KindOneofCase.BoolValue:
            {
                bool boolean = value.BoolValue;

                return hasForce
                    ? await engine
                        .SetVarAsync(name, boolean, recalc, force, cancellationToken)
                        .ConfigureAwait(false)
                    : hasRecalc
                        ? await engine
                            .SetVarAsync(name, boolean, recalc, cancellationToken)
                            .ConfigureAwait(false)
                        : await engine
                            .SetVarAsync(name, boolean, cancellationToken)
                            .ConfigureAwait(false);
            }

            default:
                return RetCode.E_INVALID_ARGUMENT;
        }
    }

    /// <summary>The outcome of resolving a session identifier and a DataWindow handle.</summary>
    /// <param name="Engine">The engine, or null when resolution failed.</param>
    /// <param name="Session">The session, when it at least resolved.</param>
    /// <param name="ReturnCode">The code to report.</param>
    private readonly record struct EngineResolution(
        ColumnExpressionEngine? Engine,
        ExpressionSession? Session,
        long ReturnCode);

    /// <summary>One DataWindow's trace pump on a shared <c>TraceChannel</c>.</summary>
    /// <param name="Subscription">The subscription being drained.</param>
    /// <param name="Pump">The pump task.</param>
    private sealed record TracePump(ExpressionTraceSubscription Subscription, Task Pump)
    {
        /// <summary>Unsubscribes and waits for the pump to drain.</summary>
        /// <returns>A task that completes when the pump has finished.</returns>
        internal async Task StopAsync()
        {
            // Disposal completes the channel, which is what lets the pump's `await foreach` finish
            // normally instead of being torn down mid-write.
            Subscription.Dispose();

            try
            {
                await Pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The call ended before the pump drained. Cancellation IS the expected end of a
                // fire-and-forget diagnostic stream, so there is nothing to report.
            }
        }
    }
}
