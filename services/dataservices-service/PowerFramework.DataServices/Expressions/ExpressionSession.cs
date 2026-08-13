// ==================================================================================================
// ExpressionSession.cs
// ==================================================================================================
//
// WHAT THIS FILE IS
//   The one place in the whole refactor where a legacy capability CANNOT be carried across the new
//   service boundary unchanged, and the place that says so out loud instead of guessing.
//
//   Three concerns live here and nothing else does:
//
//     1. THE CROSS-DATAWINDOW SCOPE. The session-scoped handle that replaces the in-process object
//        pointer `foreignvardata.expsvc`, the registry that maps a handle to a registered expression
//        service, and the DEFINED ERROR returned when a reference reaches outside that scope.
//
//     2. THE CALCULATION / RECURSION STACK. A faithful re-expression of the engine-private
//        `n_vector _vecCalcStack` [n_cst_dwsvc_columnexp.sru:L110], whose ORDERING is directly
//        observable on cross-service contract C-04's expression-trace payload.
//
//     3. THE CONTEXT TRIPLE. The `(ctx_row, ctx_dwo, ctx_expsvc)` triple that the undocumented `@`
//        sigil resolves against, modelled as a first-class scoped concept because it is the same
//        concern as a foreign reference - "resolve against a service that is not me".
//
// ==================================================================================================
// C-K : THE ONE NARROWING, STATED IN FULL - WHAT THE LEGACY DID, WHY IT CANNOT CROSS, WHAT IS
//       SUPPORTED, WHAT IS BLOCKED, AND WHEN THAT WAS DECIDED
// ==================================================================================================
//
//   WHAT THE LEGACY DID. `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru`
//   declares, at [:L80-L83]:
//
//       type foreignvardata from structure
//           integer                 index
//           n_cst_dwsvc_columnexp   expsvc
//       end type
//
//   The second field is typed AS THE ENGINE CLASS ITSELF. It is not a name, not an identifier and not
//   a handle: it is a LIVE OBJECT POINTER to another DataWindow's expression service instance, and it
//   is dereferenced as one - synchronously, into a PRIVATE method of that other instance - at two
//   independent sites, so the dereference is structural rather than incidental:
//
//       GlobalVars[nGVarIdx].foreign.expSvc._of_CalcVarExpValue(...)   [:L2199-L2200]  preprocessing
//       GlobalVars[index].foreign.expSvc._of_CalcVarExpValue(...)      [:L2384-L2385]  variable value
//
//   `of_addforeignvar(name, dw)` [:L2097] populates it from a live control [:L2126, :L2130] and then
//   writes a BACK-POINTER into the other instance's own table - `expSvc.GlobalVars[...].links[n].expSvc
//   = this` [:L2141] - so the coupling is bidirectional and both directions are pointers.
//   `globalvardata.links[]` [:L70] is an array of the same structure, so every element of it carries
//   the same pointer.
//
//   WHY IT CANNOT CROSS THE BOUNDARY. A pointer is an address in one process's address space. It has
//   no serialized form, so there is no wire representation to define; and the call through it is a
//   synchronous read of another object's private state, which no message can stand in for. Widening
//   the contract - shipping a network address, a URL or a process identifier in the field's place -
//   would recreate the cross-instance reference the narrowing exists to forbid, and would turn a
//   guaranteed-live in-process read into a call that can fail in transit, be answered by a different
//   process, or be answered from a different point in time.
//
//   WHAT IS SUPPORTED. A cross-DataWindow variable resolves through a SESSION-SCOPED DATAWINDOW
//   HANDLE, allocated and owned by an ExpressionSession, and it resolves ONLY when both
//   DataWindows are CO-RESIDENT IN THE SAME SESSION INSIDE ONE DataServices INSTANCE. Inside one
//   session the dereference is still an in-process call through an interface reference, so the
//   supported case loses nothing at all: it is the same synchronous read, reached through a handle
//   lookup rather than through a stored pointer.
//
//   WHAT IS BLOCKED. A reference whose handle is not registered in the resolving session - because it
//   belongs to another session, or to another service instance, or because the session that scoped it
//   has been closed or has expired - is BLOCKED. It returns a defined error that NAMES THE UNREACHABLE
//   HANDLE, carries `ExpressionErrorCategory.ForeignReferenceBlocked`, and yields NO VALUE. It is
//   never resolved to a substituted value, a default, an empty string treated as data, or a stale
//   reading.
//
//   WHY A DEFINED ERROR RATHER THAN A BEST EFFORT. Approximating a pointer dereference across a
//   network produces a result that is WRONG IN A WAY NO TEST WOULD OBVIOUSLY CATCH: the expression
//   evaluates, returns a number of the right type in the right range, and is silently stale. A loud
//   failure is worse ergonomics and better engineering, and it is the only outcome that a
//   characterization comparison can adjudicate.
//
//   WHEN THIS WAS DECIDED. BEFORE IMPLEMENTATION, NOT DURING IT. The narrowing was surfaced during
//   discovery by reading the structure definition at [:L82], reviewed, and recorded in three places
//   that already exist in this repository and can be diffed against this file: the published contract
//   (`ForeignVarRef` in shared/PowerFramework.Contracts/Proto/dataservices.v1.proto, whose
//   `resolvable` field exists for exactly this), the error vocabulary
//   (`ExpressionErrorCategory.ForeignReferenceBlocked`, which is documented there as having NO legacy
//   locator because the legacy cannot produce it), and the structure port
//   (`ForeignVariableReference(int Index, string Handle)` in ExpressionVariableEnvironment.cs, whose
//   two fields match the legacy's two). This file is where that reviewed decision is ENFORCED.
//
// ==================================================================================================
// THE LEGACY'S OWN RULES CORROBORATE THE DESIGN - THEY WERE NOT BENT TO FIT IT
// ==================================================================================================
//
//   A FOREIGN VARIABLE CANNOT BE STATICALLY EXPANDED, and that is the legacy's rule, not a consequence
//   of the narrowing. `docs/n_cst_dwsvc_columnexp.md:L34` introduces the feature with a worked example
//   whose comment says dynamic expansion must be used, and the parser enforces it outright at
//   [:L1425-L1428] - a `VAR_FOREIGN` variable reached by the single-sigil static form is rejected with
//   `RetCode.E_INVALID_ARGUMENT`.
//
//   That matters as EVIDENCE. Static expansion substitutes a value at bind time and never reads it
//   again; the legacy refuses it for foreign variables precisely because a foreign value must be read
//   AT CALCULATION TIME. So the original already insisted on a live read through the pointer at the
//   moment of calculation - which is exactly what a live handle inside one session provides, and
//   exactly what nothing outside one session can provide. The session-scoped-handle design runs with
//   the grain of the original rather than across it.
//
//   THE SAME APPLIES TO THE CONTEXT SIGIL. A context variable cannot be statically expanded either
//   [:L1417], and a context-scoped FUNCTION macro is unsupported outright [:L1308]. Both rejections
//   are reproduced here, verbatim in text and in return code, because both are legacy behaviour and
//   because both are about resolving against a service that is not the local one.
//
// ==================================================================================================
// THE UNDOCUMENTED FOURTH SIGIL - `@` / `MACRO_CONTEXT`
// ==================================================================================================
//
//   `constant string MACRO_CONTEXT = "@"` [:L1253] appears in NO specification - not in
//   docs/n_cst_dwsvc_columnexp.md, which documents only `$` and `$$`, and not in the plan. It is real,
//   it is scanned as a first-class sigil alongside `$` [:L1292-L1300], and it sets `vardata.isCtx`
//   [:L1413]. It composes: `@name`, `@$name` and `@$$name` are all reachable, because `bIsMacro` is
//   taken from the character after `@` and `bDD` from the character after that.
//
//   What `isCtx` DOES is resolve the reference against `ctx_expsvc` instead of the local service
//   [:L2180-L2187]:
//
//       if vars[nVarIdx].isCtx then
//           if vars[nVarIdx].isMacro then
//               sVal = ctx_expSvc._of_CalcVarExpValue(ctx_row, ctx_dwo,
//                          ctx_expSvc._of_FindVarIndex(vars[nVarIdx].name), row, dwo, this)
//           else
//               sVal = ctx_expsvc._of_GetItemExpValue(ctx_row, vars[nVarIdx].name)
//           end if
//
//   So the context path has the SAME shape as the foreign path - a call into a service that is not the
//   local one, through a reference the local service was handed - and therefore the same problem at a
//   boundary and the same solution. Scoping the session around foreign variables ALONE would leave
//   the context path homeless, so the triple is modelled here as
//   ExpressionContext and resolved through the same registry.
//
//   NOTE WHICH TRIPLE IS WHICH AT [:L2182]. The FIRST pair `(ctx_row, ctx_dwo)` is where the value is
//   computed; the SECOND triple `(row, dwo, this)` is the CALLER handing ITSELF down as the context of
//   the nested call. The parameter order is preserved exactly, because reversing it would compute the
//   right expression against the wrong row.
//
// ==================================================================================================
// THE CALCULATION STACK - ONE-BASED, AND OBSERVABLE ON THE WIRE
// ==================================================================================================
//
//   Ownership moves, behaviour does not. The legacy holds the vector as an ENGINE-PRIVATE field
//   [:L110] created and reserved in the constructor [:L2421-L2422] and destroyed in the destructor
//   [:L2425]. On a stateless request boundary a per-engine field has nowhere to live between calls, so
//   the plan assigns the stack to the session. This file therefore owns ONE STACK PER REGISTERED
//   HANDLE - not one per session - so that the legacy's "each engine has its own stack" property
//   survives the move, and so that the reservation and the deterministic release stay paired with the
//   registration they belong to.
//
//   THE THREE OBSERVABLE BEHAVIOURS, TRANSCRIBED:
//
//     PUSH / POP BY CAPTURED INDEX  [:L296-L297, :L318]
//         _vecCalcStack.Append(colName)
//         k = _vecCalcStack.Count()
//         ... _of_CalcItem(row, n) for each dependent expression, WHICH RECURSES ...
//         _vecCalcStack.RemoveAt(k)
//     The pop names the index captured immediately after the push. It is NOT "remove the last
//     element", and the difference is observable: the recursion between them can leave the count
//     changed, and a remove-last would then discard the wrong frame. Reproduced exactly.
//
//     RECURSION DETECTION  [:L684-L686]
//         nCount = _vecCalcStack.Count()
//         for nIndex = 1 to nCount
//             if _vecCalcStack.GetAt(nIndex) = ColExpDatas[index].name then return false
//     A one-based INCLUSIVE scan, forward, returning on the FIRST match. It is a NAME SCAN and not a
//     depth check: the reservation of 20 is a capacity hint and imposes no maximum depth.
//
//     THE TRACE PAYLOAD  [:L752-L757]
//         for nIndex = 1 to nCount
//             sCallStack += _vecCalcStack.GetAt(nIndex) + ">"
//         next
//         sCallStack += ColExpDatas[index].dwo.name
//     Every frame is followed by `>`, then the CURRENT column's own name is appended - SO THE FINAL
//     SEGMENT CARRIES NO TRAILING `>`. A two-deep stack reads "outer>inner>current". That string is
//     handed straight to the trace event at [:L758] and travels on contract C-04's TraceChannel, so
//     an extra or missing delimiter changes every stored recording.
//
//   ONE-BASED TRANSLATION IS THE REFACTOR'S MOST DANGEROUS MECHANICAL HAZARD, and this file is one of
//   the places it bites. Every index here is one-based and is routed through
//   `PowerFramework.Shared.Containers.Vector`, which centralises the conversion in a single private
//   helper of its own. No loop bound in this file was "tidied": the inclusive `1 .. Count()` form is
//   the oracle's own and is preserved literally.
//
//   TRACE ORDERING IS PATTERN (a), SEQUENCING TOKEN. The trace is pure diagnostics and
//   fire-and-forget, so it carries a monotonic token for detection and MUST NOT block a calculation -
//   unlike macro invocation, which is strictly synchronous because the calculation cannot proceed
//   without the answer. ExpressionSession.EmitTrace therefore hands the record to a sink
//   and swallows anything the sink throws.
//
// ==================================================================================================
// WHY THE HOST INTERFACE IS DECLARED HERE - A DELIBERATE ARCHITECTURAL DECISION
// ==================================================================================================
//
//   In the legacy, `foreignvardata.expsvc` points at ANOTHER INSTANCE OF THE ENGINE CLASS while the
//   engine itself owns the stack. Ported naively that is a mutual dependency: the session would need
//   the concrete engine type to hold a reference to it, and the engine would need the session to hold
//   its stack. That is both a design smell and a generation-order cycle.
//
//   The edge is therefore made ONE-DIRECTIONAL - SESSION <- ENGINE:
//
//     * IExpressionServiceHost is declared HERE, exposing EXACTLY the members that
//       foreign and context resolution actually invoke, derived from the four call sites [:L2131],
//       [:L2182], [:L2185] and [:L2385-L2386] and from nowhere else. No speculative member was added.
//     * `ColumnExpressionEngine.cs` IMPLEMENTS that interface and REGISTERS ITSELF with the session.
//     * THIS FILE DOES NOT REFERENCE `ColumnExpressionEngine` AND MUST NEVER ACQUIRE A REFERENCE TO
//       IT. Needing the concrete type would mean the interface is mis-scoped; the correct repair is
//       to widen the interface, never to import the engine.
//
// ==================================================================================================
// WHAT THIS FILE IS NOT
// ==================================================================================================
//
//   NOT `Domain/ValidationSession.cs`. That is a different session type holding the four pieces of
//   cross-event item-change state from `se_cst_dw.sru:L89-L96` - the disabled-event mask, the two
//   re-entrancy flags, and the item-changed result stashed for the validation-error event. Two
//   distinct concepts, two distinct types, no shared state. This session scopes cross-DataWindow
//   handles and owns the calculation stack.
//
//   NOT A UI COMPONENT (C-D). A "DataWindow handle" here is an OPAQUE IDENTIFIER and never a window
//   handle. There is no dialog, no message box, no DPI conversion, no font measurement and no `win32`
//   interop anywhere in this file, and no type from a deferred capability area is referenced.
//
//   NOT A SECRET HOLDER (C-F). CONFIRMED BY INSPECTION: no key, token, password, connection string,
//   certificate or credential-shaped literal appears in this file. Session identifiers and DataWindow
//   handles are correlation values, not credentials - they authorise nothing and are meaningless
//   outside one live session - and every configuration value read here arrives through the options
//   pattern from `Configuration/DataServicesOptions.cs`.
//
//   NOT A NEW DEPENDENCY (AAP 0.5.3). Everything used here is the base class library plus the six
//   shared projects this service already references. No package was added.
//
//   NOT DISPOSABLE, DELIBERATELY. The legacy releases the vector in its DESTRUCTOR [:L2425], which is
//   a deterministic release tied to the owning object's lifetime. The boundary equivalent is the
//   explicit open/close pair the contract already publishes - `OpenExpressionSession` and
//   `CloseExpressionSession` - so release is spelled ExpressionSession.Close and
//   ExpressionSessionRegistry.Close. Adding `IDisposable` on top would give a
//   server-held, id-correlated resource a second, competing release path whose ownership no caller
//   could see, and a `using` block around it would close a session that another in-flight request
//   still names.
//
// ==================================================================================================

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Authorization;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.Shared.Diagnostics;
using PowerFramework.Shared.Containers;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Expressions;

/// <summary>
/// The abstraction that replaces the live object pointer <c>foreignvardata.expsvc</c>
/// [n_cst_dwsvc_columnexp.sru:L82] - one DataWindow's column-expression service, as seen by another
/// one resolving a cross-DataWindow reference through it.
/// </summary>
/// <remarks>
/// <para>
/// THIS INTERFACE IS DECLARED IN THIS FILE ON PURPOSE, AND THE REASON IS ARCHITECTURAL. The legacy
/// field is typed as the engine class itself, so a naive port makes the session and the engine
/// mutually dependent. Declaring the abstraction on the SESSION side makes the edge one-directional -
/// <c>ColumnExpressionEngine</c> implements this and registers itself with an
/// <see cref="ExpressionSession"/>, and this file never names the engine. See the file header.
/// </para>
/// <para>
/// THE MEMBER SET IS DERIVED FROM FOUR CALL SITES AND NOTHING ELSE. Every member below is invoked
/// through the stored pointer, or through the context reference, somewhere in the oracle; no member
/// was added speculatively, and the two <c>CalcVarExpValue</c> arities are both present because BOTH
/// are used - the six-argument form on the context path and the four-argument form on the foreign
/// path. The legacy declares all four as <c>private</c> and reaches them through the pointer anyway,
/// which PowerScript permits between instances of the same class; C# does not, so they are surfaced
/// on an interface. That is a visibility change forced by the language and it is not a widening: no
/// member here is reachable by anything the legacy could not already reach.
/// </para>
/// <para>
/// A NOTE ON FAILURE VALUES, because they are not exceptions. Both <c>CalcVarExpValue</c> arities and
/// <see cref="GetItemExpValue"/> answer THE EMPTY STRING ON FAILURE, and every legacy caller tests
/// exactly that - <c>if sVal = "" then return ""</c> [:L2183, :L2186, :L2201]. Implementations must
/// preserve it: an empty answer is a failure and is never a successful empty value.
/// <see cref="ExpressionValueResult.Succeeded"/> applies that same test.
/// </para>
/// </remarks>
public interface IExpressionServiceHost
{
    /// <summary>
    /// Resolves a global-variable name to its one-based index in this service's own variable table -
    /// <c>_of_FindVarIndex</c> [n_cst_dwsvc_columnexp.sru:L994-L1001].
    /// </summary>
    /// <param name="name">The variable name. Matching is ordinal and case-sensitive.</param>
    /// <returns>
    /// The one-based index, or <c>0</c> when the name is not defined. ZERO IS THE LEGACY'S OWN
    /// "not found" answer [:L1000] and callers test it as such [:L1421, :L2132, :L2191], so
    /// <c>0</c> is returned rather than <c>-1</c> and rather than an exception being thrown.
    /// </returns>
    /// <remarks>
    /// Invoked across the boundary of one service by another at TWO sites: when a foreign variable is
    /// bound, <c>varData.foreign.index = expSvc._of_FindVarIndex(name)</c> [:L2131], and when a
    /// context macro variable is resolved, <c>ctx_expSvc._of_FindVarIndex(...)</c> [:L2182]. The
    /// oracle's scan runs BACKWARDS so that a duplicated name resolves to its LAST definition;
    /// implementations must preserve that, and
    /// <see cref="ExpressionVariableEnvironment.IndexOf(string?)"/> already does.
    /// </remarks>
    int FindVarIndex(string? name);

    /// <summary>
    /// Calculates a global variable's value in THIS service, at an explicit row and DataWindow
    /// object, with the caller handing its own position down as the context triple - the six-argument
    /// <c>_of_CalcVarExpValue</c> [n_cst_dwsvc_columnexp.sru:L2362].
    /// </summary>
    /// <param name="row">
    /// <c>row</c> - the ONE-BASED row ordinal AT WHICH THE VALUE IS COMPUTED, in this service's own
    /// DataWindow.
    /// </param>
    /// <param name="dwo">
    /// <c>dwo</c> - the DataWindow object the value is computed against. May be <see langword="null"/>
    /// where the legacy passes an unset <c>dwobject</c>.
    /// </param>
    /// <param name="index">
    /// <c>index</c> - the ONE-BASED index of the variable in THIS service's table. PASSED THROUGH
    /// UNCHECKED, exactly as [:L2182] passes whatever <see cref="FindVarIndex"/> returned; see the
    /// remarks on <see cref="ExpressionSession.CalcContextVariableValue"/> for what a zero means.
    /// </param>
    /// <param name="ctxRow"><c>ctx_row</c> - the CALLER's row, handed down as context.</param>
    /// <param name="ctxDwo"><c>ctx_dwo</c> - the CALLER's DataWindow object, handed down as context.</param>
    /// <param name="ctxExpSvc">
    /// <c>ctx_expsvc</c> - the CALLER ITSELF, handed down as context. [:L2182] passes <c>this</c>
    /// here, so a nested reference inside the computed variable resolves back against the caller.
    /// </param>
    /// <returns>The value in expression form, or the empty string on failure.</returns>
    /// <remarks>
    /// THE PARAMETER ORDER IS THE ORACLE'S AND MUST NOT BE REARRANGED. The first pair is where the
    /// value is computed; the last triple is the caller describing itself. Swapping them computes the
    /// right expression against the wrong row, which yields a plausible number and no diagnostic.
    /// </remarks>
    string CalcVarExpValue(
        long row,
        IDataWindowObject? dwo,
        int index,
        long ctxRow,
        IDataWindowObject? ctxDwo,
        IExpressionServiceHost? ctxExpSvc);

    /// <summary>
    /// Calculates a global variable's value in THIS service at ITS OWN current row and DataWindow
    /// object - the four-argument <c>_of_CalcVarExpValue</c>
    /// [n_cst_dwsvc_columnexp.sru:L2399-L2400], which delegates to the six-argument arity after
    /// substituting <c>#DataWindow.GetRow()</c> and <c>#DataWindow.Object.DataWindow</c>.
    /// </summary>
    /// <param name="index">The ONE-BASED index of the variable in THIS service's table.</param>
    /// <param name="ctxRow"><c>ctx_row</c> - the CALLER's row, handed down as context.</param>
    /// <param name="ctxDwo"><c>ctx_dwo</c> - the CALLER's DataWindow object, handed down as context.</param>
    /// <param name="ctxExpSvc"><c>ctx_expsvc</c> - the CALLER ITSELF, handed down as context.</param>
    /// <returns>The value in expression form, or the empty string on failure.</returns>
    /// <remarks>
    /// THIS IS THE ARITY THE FOREIGN DEREFERENCE USES, at both of its sites - [:L2200] on the
    /// preprocessing path and [:L2385] on the variable-value path - each passing
    /// <c>(foreign.index, row, dwo, this)</c>. It is a distinct member rather than a defaulted
    /// overload because the substitution it performs is not "the same call with defaults": the row it
    /// reads is the FOREIGN service's current row, which the caller does not know and must not guess.
    /// </remarks>
    string CalcVarExpValue(
        int index,
        long ctxRow,
        IDataWindowObject? ctxDwo,
        IExpressionServiceHost? ctxExpSvc);

    /// <summary>
    /// Reads a COLUMN's current value from THIS service's DataWindow, rendered in expression form -
    /// <c>_of_GetItemExpValue</c> [n_cst_dwsvc_columnexp.sru:L2326].
    /// </summary>
    /// <param name="row">The ONE-BASED row ordinal to read.</param>
    /// <param name="columnName">The column name.</param>
    /// <returns>
    /// The value in expression form, or the empty string when the column's type matched no arm of the
    /// oracle's conversion switch [:L2357-L2358] - which callers treat as a failure [:L2186].
    /// </returns>
    /// <remarks>
    /// The NON-MACRO half of the context path: <c>@name</c> - a bare `@` with no `$` after it - reads a
    /// COLUMN of the context DataWindow rather than one of its variables [:L2185]. That is the only
    /// site this member is reached from across a service, and it is why the interface carries it.
    /// </remarks>
    string GetItemExpValue(long row, string? columnName);
}

/// <summary>
/// An OPAQUE, SESSION-SCOPED identifier for one DataWindow's expression service - the serializable
/// value that crosses the wire in place of the pointer <c>foreignvardata.expsvc</c>
/// [n_cst_dwsvc_columnexp.sru:L82], and the wire counterpart of
/// <c>dataservices.v1.ForeignVarRef.foreign_datawindow_handle</c>.
/// </summary>
/// <remarks>
/// <para>
/// IT IS AN IDENTIFIER AND NOTHING ELSE (C-D). It is NOT a window handle, NOT an <c>HWND</c>, NOT a
/// network address, NOT a URL and NOT a process identifier. Making it any of those would recreate the
/// cross-instance reference the narrowing exists to forbid, and the first of them would drag a
/// deferred capability area into this service.
/// </para>
/// <para>
/// IT IS NOT A CREDENTIAL EITHER (C-F). A handle authorises nothing: it is meaningless outside the one
/// live session that allocated it, and presenting one proves nothing about the presenter. Authorisation
/// is the stock bearer-token handler's job on every one of this service's endpoints.
/// </para>
/// <para>
/// EQUALITY IS ORDINAL, AND EMPTY IS NORMALISED TO ABSENT. The constructor folds
/// <see langword="null"/> and the empty string to the same internal state, so
/// <c>default(DataWindowHandle)</c>, <c>new DataWindowHandle(null)</c> and
/// <c>new DataWindowHandle("")</c> are all equal and all <see cref="IsEmpty"/>. Without that
/// normalisation two spellings of "no handle" would compare unequal and a lookup would silently
/// depend on which one a caller happened to construct.
/// </para>
/// </remarks>
public readonly record struct DataWindowHandle
{
    /// <summary>
    /// The handle text, or <see langword="null"/> for the absent handle. Normalised in the
    /// constructor so that the empty string is stored as <see langword="null"/>.
    /// </summary>
    private readonly string? _value;

    /// <summary>
    /// Initialises a handle from its text.
    /// </summary>
    /// <param name="value">
    /// The opaque handle text. <see langword="null"/> and the empty string are equivalent and both
    /// produce <see cref="None"/>.
    /// </param>
    public DataWindowHandle(string? value) =>
        _value = string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// The absent handle - the value a <c>ForeignVarRef</c> carries when no DataWindow is named.
    /// </summary>
    public static DataWindowHandle None => default;

    /// <summary>
    /// The handle text, never <see langword="null"/>. <see cref="string.Empty"/> for
    /// <see cref="None"/>.
    /// </summary>
    public string Value => _value ?? string.Empty;

    /// <summary>
    /// Whether this is the absent handle. A reference naming an empty handle can never be resolved
    /// and is refused with <see cref="RetCode.E_INVALID_ARGUMENT"/> rather than being reported as
    /// BLOCKED, because nothing was named to block.
    /// </summary>
    public bool IsEmpty => _value is null;

    /// <summary>
    /// Creates a handle from its text. Provided as a named alternative for callers holding a
    /// <see cref="string"/> straight off the wire.
    /// </summary>
    /// <param name="value">The opaque handle text.</param>
    /// <returns>The handle.</returns>
    public static DataWindowHandle From(string? value) => new(value);

    /// <summary>
    /// Returns <see cref="Value"/>, so a handle formats identically to its wire representation.
    /// </summary>
    /// <returns>The handle text, or the empty string for <see cref="None"/>.</returns>
    public override string ToString() => Value;
}


/// <summary>
/// The context triple <c>(ctx_row, ctx_dwo, ctx_expsvc)</c> that the engine threads through every
/// preprocessing call [n_cst_dwsvc_columnexp.sru:L2148], with the third member expressed as a
/// session-scoped <see cref="DataWindowHandle"/> instead of a live service pointer.
/// </summary>
/// <param name="Row">
/// <c>ctx_row</c> - the ONE-BASED row ordinal in the CONTEXT DataWindow at which a context reference is
/// read. Zero means "no row", which is what an unset context carries.
/// </param>
/// <param name="Dwo">
/// <c>ctx_dwo</c> - the context DataWindow object. Carried because the oracle carries it: [:L2182]
/// forwards it verbatim into the six-argument value call. May be <see langword="null"/>.
/// </param>
/// <param name="Handle">
/// <c>ctx_expsvc</c> - THE ONE SUBSTITUTION. The context service is named by a handle the resolving
/// session can look up, rather than held as a pointer. <see cref="DataWindowHandle.None"/> means no
/// context was supplied.
/// </param>
/// <remarks>
/// <para>
/// WHY A HANDLE HERE TOO, RATHER THAN AN <see cref="IExpressionServiceHost"/> REFERENCE. The context
/// path is the same concern as the foreign path - a call into a service that is not the local one - so
/// it is subject to the same narrowing and must be refused in the same way when it reaches outside the
/// session. Carrying a live reference in this record would let a caller smuggle a service in past the
/// co-residency check, which is exactly the hole the narrowing exists to close. Resolution therefore
/// goes through <see cref="ExpressionSession.ResolveContextHost"/> and can be BLOCKED.
/// </para>
/// <para>
/// THE ORDINARY CASE IS THE LOCAL SERVICE, NOT AN ABSENT ONE. Where an expression contains no `@`
/// reference the oracle still passes a full triple - <c>_of_PreprocessExp(row, dwo, ..., row, dwo,
/// this)</c> [:L744] - naming ITSELF as the context. So a caller with nothing special to say supplies
/// its OWN row, object and handle rather than <see cref="None"/>, and every context reference then
/// resolves locally. <see cref="None"/> is reserved for "no context is available at all", which a
/// context reference cannot be satisfied from.
/// </para>
/// <para>
/// THE `@` SIGIL THAT PUTS THIS RECORD TO WORK IS UNDOCUMENTED - `MACRO_CONTEXT` [:L1253]. See the file
/// header; it is real, it composes with `$` and `$$`, and it is why the session scopes context
/// references and not only foreign ones.
/// </para>
/// </remarks>
public readonly record struct ExpressionContext(
    long Row,
    IDataWindowObject? Dwo,
    DataWindowHandle Handle)
{
    /// <summary>
    /// No context at all - row zero, no object, no handle. A context reference cannot be resolved
    /// against it and is refused with <see cref="RetCode.E_INVALID_ARGUMENT"/>.
    /// </summary>
    public static ExpressionContext None => default;

    /// <summary>
    /// Whether a context service was named. False for <see cref="None"/>.
    /// </summary>
    public bool IsSpecified => !Handle.IsEmpty;
}

/// <summary>
/// The outcome of resolving a CROSS-DATAWINDOW reference - a foreign variable or a context reference -
/// through a session-scoped handle.
/// </summary>
/// <remarks>
/// <para>
/// THE WHOLE POINT OF THIS ENUMERATION IS THAT BLOCKED IS DISTINGUISHABLE FROM NOT FOUND. Those two
/// are entirely different facts and a caller must be able to act on them differently: a name that does
/// not exist in a REACHABLE service is the legacy's own error [:L2133] and is a bug in the caller's
/// expression, whereas a reference to an UNREACHABLE service is this refactor's one narrowing and is a
/// fact about topology. Collapsing them - into one boolean, or into "the value was empty" - would
/// hide the narrowing behind an error the legacy already had, which is precisely the silent outcome the
/// plan forbids.
/// </para>
/// <para>
/// TWO MEMBERS ARE THE NARROWING and both carry
/// <see cref="ExpressionErrorCategory.ForeignReferenceBlocked"/>, so "is this BLOCKED?" is answered by
/// the category and by <see cref="ForeignVariableResolution.IsBlocked"/> rather than by matching two
/// separate return codes. They are separate members, and answer separate return codes, only so that a
/// diagnostic can say WHICH kind of unreachable it was.
/// </para>
/// </remarks>
public enum CrossDataWindowStatus
{
    /// <summary>
    /// The handle names a service that is CO-RESIDENT in this session, the session is open, and the
    /// reference was dereferenced exactly as the legacy dereferences its pointer. THE ONLY VALUE THAT
    /// MAY CARRY A VALUE.
    /// </summary>
    Resolved = 0,

    /// <summary>
    /// The handle resolved and the service is reachable, but THE NAME IS NOT DEFINED IN IT -
    /// <c>expSvc._of_FindVarIndex(name)</c> answered a non-positive index
    /// [n_cst_dwsvc_columnexp.sru:L2132]. This is the legacy's own error, reported with the legacy's
    /// own text and its <see cref="RetCode.E_VAR_NOT_FOUND"/>, and IT IS NOT BLOCKED: the topology was
    /// fine and the expression was wrong.
    /// </summary>
    VariableNotFound = 1,

    /// <summary>
    /// BLOCKED - THE NARROWING. The handle is not registered in this session, so the DataWindow it
    /// names is not co-resident: it belongs to another session, or to another DataServices instance, or
    /// it was never registered at all. Answers <see cref="RetCode.E_INVALID_HANDLE"/> and NEVER a
    /// value.
    /// </summary>
    BlockedHandleNotCoResident = 2,

    /// <summary>
    /// BLOCKED - THE NARROWING. The session that scoped the handle has been closed or has expired, so
    /// its registrations were released [n_cst_dwsvc_columnexp.sru:L2425 is the legacy's equivalent
    /// release]. Answers <see cref="RetCode.E_NOT_EXISTS"/> and NEVER a value - and specifically never
    /// a reading from state left over from before the close, which is the failure mode this member
    /// exists to make impossible.
    /// </summary>
    BlockedSessionClosed = 3,

    /// <summary>
    /// The handle resolved, the service was reached, and THE CALL INTO IT THREW. Answers
    /// <see cref="RetCode.E_INTERNAL_ERROR"/> and no value.
    /// </summary>
    /// <remarks>
    /// A BOUNDARY-LEVEL TRANSLATION, AND THE ONE OUTCOME WITH NO LEGACY COUNTERPART OTHER THAN A
    /// CRASH. In-process an engine fault propagates up the PowerBuilder call stack and is handled, or
    /// not, by the application; the framework's own posture on a structural fault is to terminate
    /// [ws_objects/pfw.pbl.src/pfw.sra:L111-L144]. Across a service boundary an escaping exception
    /// from a host callback would tear down the stream that OTHER calculations are also using, so it is
    /// caught at the call site and reported here. <see cref="ExpressionValueResult.Succeeded"/> is
    /// false, so nothing downstream can mistake it for an answer.
    /// </remarks>
    /// <remarks>
    /// THE FAULT IS NEITHER SWALLOWED NOR PUT ON THE WIRE (CWE-209). The reached host is arbitrary
    /// application code this service does not own, and its exception message can carry a file path, a
    /// connection string fragment, a SQL statement or a configuration key. So the error a caller receives
    /// carries FIXED TEXT plus a correlation identifier, and the exception itself is logged server-side
    /// against that identifier - see <see cref="ExpressionSession.Faulted"/>. The diagnostic is kept in
    /// full; it simply is not delivered to the caller.
    /// </remarks>
    HostFaulted = 4,

    /// <summary>
    /// THE REFERENCE ITSELF IS MALFORMED - no handle was named, no variable name was supplied, or a
    /// stored reference carries a non-positive index. NOT BLOCKED, because nothing reachable was named
    /// in the first place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// REPRODUCES THE ORACLE'S TWO ARGUMENT GUARDS, INCLUDING THE FACT THAT THEY REPORT NOTHING.
    /// <c>of_addforeignvar</c> opens with <c>if name = "" then return RetCode.E_INVALID_ARGUMENT</c>
    /// [n_cst_dwsvc_columnexp.sru:L2119] and <c>if Not IsValidObject(dw) then return
    /// RetCode.E_INVALID_OBJECT</c> [:L2120] - two DIFFERENT codes, and NEITHER raises a dialog, unlike
    /// the three failures below them. So a result carrying this status answers a return code with
    /// <see cref="ForeignVariableResolution.Error"/> left <see langword="null"/>, which is the oracle's
    /// own shape and not an omission.
    /// </para>
    /// <para>
    /// An empty handle is mapped to the <c>IsValidObject</c> guard because that is what it corresponds
    /// to: in-process the check is "is there an object to call", and a handle naming nothing is the same
    /// fact expressed as a value. It is deliberately NOT reported as
    /// <see cref="BlockedHandleNotCoResident"/> - a caller that supplied no handle has a bug in its
    /// request, not a topology it needs to hear about.
    /// </para>
    /// </remarks>
    InvalidReference = 5,
}


/// <summary>
/// The result of BINDING a cross-DataWindow reference - resolving a handle to a co-resident
/// <see cref="IExpressionServiceHost"/> and, where a name was supplied, resolving that name to an index
/// inside it.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE BINDING STEP, NOT THE CALCULATION STEP. It reproduces what <c>of_addforeignvar</c> does
/// at [n_cst_dwsvc_columnexp.sru:L2126-L2135] - take the other DataWindow's service, look the name up
/// in it, and refuse if the lookup fails - and what the two dereference sites do before they call
/// [:L2199, :L2384]. The value itself is produced by <see cref="ExpressionValueResult"/>.
/// </para>
/// <para>
/// <see cref="Host"/> IS NON-NULL ONLY WHEN <see cref="Status"/> IS
/// <see cref="CrossDataWindowStatus.Resolved"/>. That is the invariant the whole narrowing rests on: a
/// BLOCKED reference hands back nothing to call, so there is no path by which a caller can dereference
/// an unreachable service even by mistake.
/// </para>
/// </remarks>
public sealed record ForeignVariableResolution
{
    /// <summary>The outcome. See <see cref="CrossDataWindowStatus"/>.</summary>
    public required CrossDataWindowStatus Status { get; init; }

    /// <summary>
    /// The handle that was resolved, echoed back so a diagnostic can name it. Present on every
    /// outcome, including the BLOCKED ones - naming the unreachable handle is part of the defined
    /// error.
    /// </summary>
    public required DataWindowHandle Handle { get; init; }

    /// <summary>
    /// The ONE-BASED index of the variable inside the foreign service's own table, or <c>0</c> when it
    /// was not resolved. Corresponds to <c>foreignvardata.index</c> [:L81] and to
    /// <c>dataservices.v1.ForeignVarRef.index</c>.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// The co-resident service, or <see langword="null"/>. NON-NULL ONLY FOR
    /// <see cref="CrossDataWindowStatus.Resolved"/>.
    /// </summary>
    public required IExpressionServiceHost? Host { get; init; }

    /// <summary>
    /// The defined error, or <see langword="null"/> for <see cref="CrossDataWindowStatus.Resolved"/>.
    /// Its <see cref="ExpressionParseError.Category"/> is
    /// <see cref="ExpressionErrorCategory.ForeignReferenceBlocked"/> for both BLOCKED outcomes and
    /// <see cref="ExpressionErrorCategory.UndefinedName"/> for the legacy's own not-found.
    /// </summary>
    public required ExpressionParseError? Error { get; init; }

    /// <summary>
    /// The return code, from <see cref="RetCode"/>. <see cref="RetCode.OK"/> when resolved.
    /// </summary>
    public required long ReturnCode { get; init; }

    /// <summary>
    /// Whether the reference bound to a callable service.
    /// </summary>
    /// <remarks>
    /// Tests <see cref="Host"/> as well as <see cref="Status"/> deliberately. The two can only
    /// disagree if a result was assembled by hand, and in that case the safe reading is "not
    /// resolved".
    /// </remarks>
    public bool IsResolved => Status == CrossDataWindowStatus.Resolved && Host is not null;

    /// <summary>
    /// Whether this is THE NARROWING'S DEFINED ERROR - the reference reached outside the session.
    /// </summary>
    /// <remarks>
    /// DELIBERATELY FALSE FOR <see cref="CrossDataWindowStatus.VariableNotFound"/>. A name that does
    /// not exist in a reachable service is the legacy's own error and is not a boundary limitation;
    /// conflating the two is exactly what this property exists to prevent.
    /// </remarks>
    public bool IsBlocked =>
        Status is CrossDataWindowStatus.BlockedHandleNotCoResident
            or CrossDataWindowStatus.BlockedSessionClosed;

    /// <summary>
    /// Projects this result onto the structure the expression tables store - the port of
    /// <c>foreignvardata</c> [n_cst_dwsvc_columnexp.sru:L80-L83].
    /// </summary>
    /// <returns>
    /// A reference carrying <see cref="Index"/> and <see cref="Handle"/>. The BLOCKED-ness is
    /// deliberately NOT carried on it: whether a handle is currently resolvable is SESSION STATE
    /// rather than structure, which is why <c>ForeignVarRef.resolvable</c> is a separate wire field
    /// fed from <see cref="IsResolved"/> and why this record keeps the legacy's field count at two.
    /// </returns>
    public ForeignVariableReference ToReference() => new(Index, Handle.Value);
}

/// <summary>
/// The result of CALCULATING a cross-DataWindow reference's value - the outcome of the dereference the
/// legacy performs through its stored pointer at [n_cst_dwsvc_columnexp.sru:L2200] and [:L2385], and of
/// the context read at [:L2182] and [:L2185].
/// </summary>
/// <remarks>
/// <para>
/// AN EMPTY VALUE IS A FAILURE, AND THAT IS THE LEGACY'S OWN CONVENTION RATHER THAN A CHOICE MADE HERE.
/// <c>_of_CalcVarExpValue</c> and <c>_of_GetItemExpValue</c> both return <c>string</c>, so their failure
/// value is the empty string, and every caller tests exactly that - <c>if sVal = "" then return ""</c>
/// [:L2183, :L2186, :L2201, :L2204]. <see cref="Succeeded"/> applies that same test, so a reproduced
/// failure and a reproduced success are distinguished the same way the oracle distinguishes them. The
/// consequence a consumer must know: there is NO representation for "the foreign variable's value is
/// legitimately the empty string", because the legacy has none either.
/// </para>
/// <para>
/// A BLOCKED RESULT NEVER CARRIES A VALUE. <see cref="Value"/> is <see cref="string.Empty"/> for every
/// outcome other than <see cref="CrossDataWindowStatus.Resolved"/>, and that is asserted by test. It is
/// the single most important property of this type: a substituted value, a default or a stale reading
/// would be wrong in a way that evaluates cleanly and reports nothing.
/// </para>
/// </remarks>
public sealed record ExpressionValueResult
{
    /// <summary>The outcome of resolving the reference. See <see cref="CrossDataWindowStatus"/>.</summary>
    public required CrossDataWindowStatus Status { get; init; }

    /// <summary>
    /// The value IN EXPRESSION FORM, exactly as the oracle's <c>_of_CalcVarExpValue</c> returns it -
    /// never <see langword="null"/>, and <see cref="string.Empty"/> whenever the reference did not
    /// resolve or the host reported failure.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// The handle the reference named, echoed back so a diagnostic can name it.
    /// </summary>
    public required DataWindowHandle Handle { get; init; }

    /// <summary>
    /// The defined error, or <see langword="null"/> on success.
    /// </summary>
    public required ExpressionParseError? Error { get; init; }

    /// <summary>
    /// The return code, from <see cref="RetCode"/>. <see cref="RetCode.OK"/> on success and
    /// <see cref="RetCode.FAILED"/> when the reference resolved but the host answered the empty
    /// string - which is the oracle's own failure signal and carries no code of its own there.
    /// </summary>
    public required long ReturnCode { get; init; }

    /// <summary>
    /// Whether a usable value was produced: the reference resolved AND the host answered something
    /// other than the empty string. This is the oracle's own test, reproduced.
    /// </summary>
    public bool Succeeded => Status == CrossDataWindowStatus.Resolved && Value.Length > 0;

    /// <summary>
    /// Whether this is THE NARROWING'S DEFINED ERROR. False for
    /// <see cref="CrossDataWindowStatus.VariableNotFound"/>, which is the legacy's own error.
    /// </summary>
    public bool IsBlocked =>
        Status is CrossDataWindowStatus.BlockedHandleNotCoResident
            or CrossDataWindowStatus.BlockedSessionClosed;
}


/// <summary>
/// The column-expression calculation and recursion stack - the port of the engine-private
/// <c>n_vector _vecCalcStack</c> [n_cst_dwsvc_columnexp.sru:L110], wrapping
/// <see cref="PowerFramework.Shared.Containers.Vector"/>.
/// </summary>
/// <remarks>
/// <para>
/// ONE STACK PER REGISTERED HANDLE, NOT ONE PER SESSION. In the legacy each engine instance owns its
/// own vector, created in its constructor [:L2421] and released in its destructor [:L2425]. The plan
/// moves ownership to the session because a per-engine field has nowhere to live across a stateless
/// request boundary; keying the stacks by handle preserves the legacy's one-stack-per-DataWindow
/// property while still making the whole set of them the session's to release. See
/// <see cref="ExpressionSession.CalcStackFor"/>.
/// </para>
/// <para>
/// EVERY INDEX IS ONE-BASED, AND THAT IS PROVEN FROM THE ORACLE RATHER THAN ASSUMED. [:L296-L297] pushes
/// and then takes <c>Count()</c> AS THE INDEX OF THE ELEMENT JUST PUSHED, with no adjustment; under
/// zero-based indexing the matching <c>RemoveAt</c> at [:L318] would address one past the end and the
/// stack would grow without bound. The conversion to the underlying zero-based storage happens in
/// exactly one private helper inside <see cref="PowerFramework.Shared.Containers.Vector"/>, and no loop
/// bound in this type was adjusted from the oracle's inclusive <c>1 .. Count()</c> form.
/// </para>
/// <para>
/// THE ORDERING IS OBSERVABLE ON THE WIRE. <see cref="BuildCallStack"/> produces the exact string the
/// trace event carries at [:L752-L758], which travels on contract C-04's <c>TraceChannel</c> as
/// <c>TraceRecord.stack</c>. A reversed or zero-based walk would corrupt a published payload while
/// every frame count still matched, so the two shapes are pinned by golden-string tests.
/// </para>
/// <para>
/// NOT THREAD-SAFE, DELIBERATELY AND FAITHFULLY. The legacy engine is main-thread-affine and a
/// calculation is a single depth-first walk, so a stack is used by ONE calculation at a time.
/// <see cref="Push"/> handing back an index that <see cref="Pop"/> later names is inherently a
/// two-step protocol that no internal lock could make atomic, so claiming safety here would be false
/// rather than merely unnecessary. <see cref="ExpressionSession"/> IS safe for concurrent registration
/// and resolution; the stack it hands out must be driven by one calculation.
/// </para>
/// </remarks>
public sealed class ExpressionCalcStack
{
    /// <summary>
    /// The legacy's own reservation - <c>_vecCalcStack.Reserve(20)</c>
    /// [n_cst_dwsvc_columnexp.sru:L2422].
    /// </summary>
    /// <remarks>
    /// A CAPACITY HINT AND NOT A DEPTH LIMIT. The recursion guard is a NAME SCAN
    /// (<see cref="ContainsFrame"/>, from [:L684-L686]) and never a depth comparison, so the stack
    /// grows past this freely. The published contract states the same on
    /// <c>dataservices.v1.TraceRecord.depth</c> so that no consumer infers a maximum of 20 from it.
    /// </remarks>
    public const int DefaultInitialCapacity = 20;

    /// <summary>
    /// The one-based storage. Substituted for <c>n_vector</c>, which is a native binding with no
    /// PowerScript body anywhere in the repository.
    /// </summary>
    private readonly Vector _frames = new();

    /// <summary>
    /// Initialises the stack and reserves its initial capacity, reproducing the two statements of the
    /// legacy constructor [n_cst_dwsvc_columnexp.sru:L2421-L2422].
    /// </summary>
    /// <param name="initialCapacity">
    /// The capacity to reserve. Defaults to <see cref="DefaultInitialCapacity"/>, the legacy's value.
    /// Supplied from <c>DataServices:ColumnExpression:CalcStackInitialCapacity</c> in the ordinary
    /// case.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="initialCapacity"/> is zero or negative. A reserved capacity is a size, and the
    /// options validator refuses a non-positive one for the same reason; this guard covers direct
    /// construction, which bypasses that validator.
    /// </exception>
    public ExpressionCalcStack(int initialCapacity = DefaultInitialCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);

        InitialCapacity = initialCapacity;
        _frames.Reserve((ulong)initialCapacity);
    }

    /// <summary>
    /// The capacity this stack was constructed with.
    /// </summary>
    public int InitialCapacity { get; }

    /// <summary>
    /// The current depth - <c>_vecCalcStack.Count()</c>, which the trace record publishes verbatim as
    /// <c>dataservices.v1.TraceRecord.depth</c>.
    /// </summary>
    public int Depth => ToOneBasedCount(_frames.Count());

    /// <summary>
    /// The allocated capacity - <c>maxsize()</c> [n_vector.sru:L13]. Exposed so a test can assert that
    /// the legacy's reservation actually happened, which is otherwise invisible.
    /// </summary>
    public ulong Capacity => _frames.MaxSize();

    /// <summary>
    /// Pushes a frame and returns the index to pop it by - the two statements at
    /// <c>_vecCalcStack.Append(colName)</c> / <c>k = _vecCalcStack.Count()</c>
    /// [n_cst_dwsvc_columnexp.sru:L296-L297].
    /// </summary>
    /// <param name="columnName">
    /// The column name entering the calculation. Stored as supplied, including
    /// <see langword="null"/>: the oracle appends whatever it holds and its own reader tolerates a
    /// null the same way.
    /// </param>
    /// <returns>
    /// THE CAPTURED INDEX - the one-based position of the frame just pushed. HOLD IT AND HAND IT BACK
    /// TO <see cref="Pop"/>; see that method for why re-deriving it later is not equivalent.
    /// </returns>
    public int Push(string? columnName)
    {
        _frames.Append(columnName);

        // Read AFTER the append, exactly as [:L297] does. Under one-based indexing this IS the index
        // of the element just appended.
        return ToOneBasedCount(_frames.Count());
    }

    /// <summary>
    /// Pops the frame at a PREVIOUSLY CAPTURED index - <c>_vecCalcStack.RemoveAt(k)</c>
    /// [n_cst_dwsvc_columnexp.sru:L318].
    /// </summary>
    /// <param name="capturedIndex">
    /// The value <see cref="Push"/> returned for the frame being removed.
    /// </param>
    /// <remarks>
    /// <para>
    /// THIS IS NOT "REMOVE THE LAST ELEMENT", AND THE DIFFERENCE IS BEHAVIOURAL. Between the push at
    /// [:L296] and the pop at [:L318] the oracle runs <c>_of_CalcItem(row, n)</c> for every dependent
    /// expression [:L299-L317], and that call RECURSES back into the same push/pop pair. If a nested
    /// walk leaves the count changed - because a frame it pushed was popped by a different captured
    /// index, or because it did not unwind cleanly - then remove-last discards a DIFFERENT frame than
    /// the one this call owns, and the corruption shows up later as a wrong recursion verdict or a
    /// wrong trace string rather than as an error here. The captured-index form is reproduced exactly.
    /// </para>
    /// <para>
    /// An index of zero or less, or one past the current depth, is a NO-OP. That is the underlying
    /// container's tolerance and PowerBuilder's, and it keeps an unwind path from throwing while it is
    /// already unwinding.
    /// </para>
    /// </remarks>
    public void Pop(int capturedIndex)
    {
        if (capturedIndex <= 0)
        {
            return;
        }

        _frames.RemoveAt((ulong)capturedIndex);
    }

    /// <summary>
    /// Reads the frame at a one-based index - <c>_vecCalcStack.GetAt(nIndex)</c>
    /// [n_cst_dwsvc_columnexp.sru:L686, :L755].
    /// </summary>
    /// <param name="oneBasedIndex">The one-based position, from <c>1</c> to <see cref="Depth"/>.</param>
    /// <returns>
    /// The frame, or <see langword="null"/> when the index is outside <c>1 .. Depth</c>. Out of range
    /// answers null rather than throwing, matching the container and matching PowerBuilder's own
    /// tolerance on this call.
    /// </returns>
    public string? GetAt(int oneBasedIndex)
    {
        if (oneBasedIndex <= 0)
        {
            return null;
        }

        return _frames.GetAt((ulong)oneBasedIndex) as string;
    }

    /// <summary>
    /// Finds the depth at which a name already appears on the stack - the recursion guard's scan
    /// [n_cst_dwsvc_columnexp.sru:L684-L686].
    /// </summary>
    /// <param name="name">The expression's own name, as the oracle compares it.</param>
    /// <returns>
    /// The ONE-BASED depth of the first match, or <c>0</c> when the name is not on the stack.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE LOOP IS THE ORACLE'S: <c>nCount = Count()</c> hoisted first, then
    /// <c>for nIndex = 1 to nCount</c> - forward, INCLUSIVE of both ends - returning on the FIRST
    /// match. So the answer is the SHALLOWEST occurrence, which is the one that makes the reference
    /// recursive.
    /// </para>
    /// <para>
    /// A <see langword="null"/> name matches nothing, and neither does a null frame. That is not
    /// defensive coding: PowerScript's <c>=</c> yields NULL when either operand is NULL, and an
    /// <c>if</c> on NULL does not take its branch, so the oracle's guard cannot fire on a null either
    /// side. Comparison is ordinal and case-sensitive, as PowerScript string equality is.
    /// </para>
    /// </remarks>
    public int IndexOfFrame(string? name)
    {
        if (name is null)
        {
            return 0;
        }

        int count = Depth;
        for (int index = 1; index <= count; index++)
        {
            if (_frames.GetAt((ulong)index) is string frame
                && string.Equals(frame, name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }

    /// <summary>
    /// Whether a name is already on the stack, which is how the oracle decides an expression would
    /// recurse [n_cst_dwsvc_columnexp.sru:L684-L688].
    /// </summary>
    /// <param name="name">The expression's own name.</param>
    /// <returns><see langword="true"/> when the name appears at any depth.</returns>
    /// <remarks>
    /// A NAME SCAN AND NOT A DEPTH CHECK. The oracle imposes no maximum depth at all; recursion is
    /// detected by repetition, and an expression flagged <c>recursive</c> [:L683] skips this scan
    /// entirely so that a deliberately self-referential expression still calculates.
    /// </remarks>
    public bool ContainsFrame(string? name) => IndexOfFrame(name) != 0;

    /// <summary>
    /// The frames, innermost LAST, in the order the oracle walks them.
    /// </summary>
    /// <remarks>
    /// The unflattened counterpart of <see cref="BuildCallStack"/>, published as
    /// <c>dataservices.v1.TraceRecord.stack_frames</c>. It is carried IN ADDITION to the flattened
    /// string rather than instead of it, because the flattened form is ambiguous - a column name
    /// containing <c>&gt;</c> would split into two frames on parsing - and the flattened form is what a
    /// characterization recording compares. A null frame is projected as the empty string.
    /// </remarks>
    public ImmutableArray<string> Frames
    {
        get
        {
            int count = Depth;
            if (count == 0)
            {
                return [];
            }

            ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>(count);
            for (int index = 1; index <= count; index++)
            {
                builder.Add(_frames.GetAt((ulong)index) as string ?? string.Empty);
            }

            return builder.MoveToImmutable();
        }
    }

    /// <summary>
    /// Builds the trace call-stack string BYTE FOR BYTE as the oracle builds it
    /// [n_cst_dwsvc_columnexp.sru:L752-L757].
    /// </summary>
    /// <param name="terminalName">
    /// <c>ColExpDatas[index].dwo.name</c> - the name of the column being calculated, appended LAST.
    /// A null or empty value is appended as nothing, which leaves a trailing <c>&gt;</c> exactly as the
    /// oracle would when its object's name is empty.
    /// </param>
    /// <returns>
    /// Each frame followed by <c>&gt;</c>, then <paramref name="terminalName"/> - so a two-deep stack
    /// with a current column reads <c>outer&gt;inner&gt;current</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE FINAL SEGMENT CARRIES NO TRAILING DELIMITER, and that is the detail a casual implementation
    /// gets wrong. The oracle appends <c>+ "&gt;"</c> INSIDE the loop over the stack and then appends
    /// the current object's name OUTSIDE it, so the delimiter count is exactly the frame count. This
    /// string is handed to the trace event at [:L758] and travels as
    /// <c>dataservices.v1.TraceRecord.stack</c>, so one extra or one missing <c>&gt;</c> invalidates
    /// every stored recording while looking entirely plausible.
    /// </para>
    /// <para>
    /// An EMPTY stack yields just <paramref name="terminalName"/> with no delimiter at all, which is
    /// what the oracle produces when the loop body never runs.
    /// </para>
    /// </remarks>
    public string BuildCallStack(string? terminalName)
    {
        int count = Depth;

        StringBuilder builder = new();
        for (int index = 1; index <= count; index++)
        {
            builder
                .Append(_frames.GetAt((ulong)index) as string ?? string.Empty)
                .Append('>');
        }

        builder.Append(terminalName ?? string.Empty);

        return builder.ToString();
    }

    /// <summary>
    /// Discards every frame - the release the legacy performs by destroying the vector in its
    /// destructor [n_cst_dwsvc_columnexp.sru:L2425].
    /// </summary>
    /// <remarks>
    /// Called by <see cref="ExpressionSession.Close"/> so that closing a session leaves nothing
    /// behind for a later reading to find. The reserved capacity is NOT released, because the
    /// container's clear does not release capacity and because a cleared stack that is used again in
    /// the same request should not have to re-reserve.
    /// </remarks>
    public void Clear() => _frames.Purge();

    /// <summary>
    /// Narrows the container's <see cref="ulong"/> count to the <see cref="int"/> the contract's depth
    /// field and every one-based loop bound in this type use.
    /// </summary>
    /// <param name="count">The container's count.</param>
    /// <returns>The count, saturated at <see cref="int.MaxValue"/>.</returns>
    /// <remarks>
    /// Saturating rather than wrapping. A stack this deep is unreachable in practice, but an unchecked
    /// cast would turn it into a NEGATIVE depth, which would silently disable every
    /// <c>1 .. count</c> loop in this type - the recursion guard included - and produce an empty trace
    /// instead of an error.
    /// </remarks>
    private static int ToOneBasedCount(ulong count) =>
        count > int.MaxValue ? int.MaxValue : (int)count;
}


/// <summary>
/// One expression-trace record - the payload of <c>OnColumnExpTrace</c>
/// [n_cst_dwsvc_columnexp.sru:L758] and the in-process shape of
/// <c>dataservices.v1.TraceRecord</c>.
/// </summary>
/// <remarks>
/// <para>
/// PURE DIAGNOSTICS, ORDERING PATTERN (a). The trace is fire-and-forget, so
/// <see cref="SequenceNumber"/> exists FOR DETECTION AND REORDERING rather than for enforcement - the
/// opposite of macro invocation, where an out-of-order arrival is a hard error because the calculation
/// cannot proceed without the answer.
/// </para>
/// <para>
/// EMISSION IS GATED, and a silent channel is not the same as a disabled one. The oracle emits only
/// when <c>#Trace</c> is set [:L752, set by <c>of_settrace</c> at :L2409-L2411]; the port reads the same
/// switch from <c>DataServices:ColumnExpression:Trace</c>. That is why the published service state
/// exposes the flag: without it a subscriber cannot tell "nothing is being calculated" from "tracing is
/// off".
/// </para>
/// </remarks>
public sealed record ExpressionTraceRecord
{
    /// <summary>
    /// A monotonic, per-session sequence number. Detection only - see the type's remarks.
    /// </summary>
    public required long SequenceNumber { get; init; }

    /// <summary>The session the calculation ran in.</summary>
    public required string SessionId { get; init; }

    /// <summary>The DataWindow whose expression was calculated.</summary>
    public required DataWindowHandle Handle { get; init; }

    /// <summary>
    /// <c>row</c> - the ONE-BASED row ordinal the expression was calculated for.
    /// </summary>
    public required long Row { get; init; }

    /// <summary>
    /// <c>ColExpDatas[index].dwo.name</c> - the column being calculated, which is also the FINAL
    /// segment of <see cref="Stack"/>.
    /// </summary>
    public required string ColumnName { get; init; }

    /// <summary>
    /// The flattened call stack, byte-exact as the oracle flattens it - each frame followed by
    /// <c>&gt;</c>, then <see cref="ColumnName"/> with NO trailing delimiter
    /// [n_cst_dwsvc_columnexp.sru:L753-L757]. Compared verbatim by characterization recordings.
    /// </summary>
    public required string Stack { get; init; }

    /// <summary>
    /// The same stack unflattened, innermost last. Carried in addition to <see cref="Stack"/> because
    /// the flattened form is ambiguous when a column name contains <c>&gt;</c>; a consumer should read
    /// this one and compare the other.
    /// </summary>
    public required ImmutableArray<string> StackFrames { get; init; }

    /// <summary>
    /// <c>sExp</c> - the expression AS EVALUATED, after macro preprocessing rather than as authored
    /// [n_cst_dwsvc_columnexp.sru:L744, :L747].
    /// </summary>
    public required string Expression { get; init; }

    /// <summary>
    /// <c>sVal</c> - the value, rendered as the oracle renders it for the trace.
    /// </summary>
    /// <remarks>
    /// THE ORACLE CONFLATES TWO CASES HERE AND THE CONFLATION IS PRESERVED (C-B). [:L758] substitutes
    /// the literal text <c>(null)</c> when the value is empty AND the expression either treats the
    /// empty string as null or is not a string column - so a genuinely empty string value and a null
    /// value are indistinguishable in the trace. That is legacy behaviour and is reproduced by whatever
    /// supplies this member, which is the engine: this record carries the rendered text and does not
    /// re-derive it.
    /// </remarks>
    public required string Value { get; init; }

    /// <summary>
    /// <c>_vecCalcStack.Count()</c> at emission - the recursion depth. NOT bounded by the stack's
    /// reserved capacity; see <see cref="ExpressionCalcStack.DefaultInitialCapacity"/>.
    /// </summary>
    public required int Depth { get; init; }

    /// <summary>
    /// When the record was produced, from the session's injected <see cref="TimeProvider"/> so that a
    /// characterization run can substitute a deterministic clock.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Compares two records by VALUE, comparing <see cref="StackFrames"/> element by element.
    /// </summary>
    /// <param name="other">The record to compare against.</param>
    /// <returns><see langword="true"/> when every member is equal.</returns>
    /// <remarks>
    /// WRITTEN OUT BECAUSE THE GENERATED VERSION IS WRONG FOR THIS TYPE.
    /// <see cref="ImmutableArray{T}"/> implements equality as REFERENCE equality of its underlying
    /// array, so the compiler's <c>Equals</c> would report two records built from identical inputs as
    /// different - and that would fail in exactly the place this type serves, a characterization suite
    /// comparing a recorded trace against a reproduced one. ADDING A MEMBER TO THIS RECORD MEANS ADDING
    /// IT HERE AND TO <see cref="GetHashCode"/>.
    /// </remarks>
    public bool Equals(ExpressionTraceRecord? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return SequenceNumber == other.SequenceNumber
            && string.Equals(SessionId, other.SessionId, StringComparison.Ordinal)
            && Handle == other.Handle
            && Row == other.Row
            && string.Equals(ColumnName, other.ColumnName, StringComparison.Ordinal)
            && string.Equals(Stack, other.Stack, StringComparison.Ordinal)
            && StackFrames.AsSpan().SequenceEqual(other.StackFrames.AsSpan())
            && string.Equals(Expression, other.Expression, StringComparison.Ordinal)
            && string.Equals(Value, other.Value, StringComparison.Ordinal)
            && Depth == other.Depth
            && Timestamp == other.Timestamp;
    }

    /// <summary>
    /// A hash consistent with <see cref="Equals(ExpressionTraceRecord?)"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    /// <remarks>
    /// <see cref="StackFrames"/> contributes its LENGTH rather than its contents, which is a valid hash
    /// for a value-equal comparison and keeps the cost bounded on a deep stack. <see cref="Stack"/>
    /// already discriminates the contents.
    /// </remarks>
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(SequenceNumber);
        hash.Add(SessionId, StringComparer.Ordinal);
        hash.Add(Handle);
        hash.Add(Row);
        hash.Add(ColumnName, StringComparer.Ordinal);
        hash.Add(Stack, StringComparer.Ordinal);
        hash.Add(StackFrames.Length);
        hash.Add(Expression, StringComparer.Ordinal);
        hash.Add(Value, StringComparer.Ordinal);
        hash.Add(Depth);
        hash.Add(Timestamp);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Where <see cref="ExpressionSession.EmitTrace"/> hands a completed
/// <see cref="ExpressionTraceRecord"/> - the seam between the engine and contract C-04's
/// <c>TraceChannel</c>.
/// </summary>
/// <remarks>
/// <para>
/// AN IMPLEMENTATION MUST ENQUEUE AND RETURN PROMPTLY. The trace is ordering pattern (a),
/// fire-and-forget, and A TRACE MUST NEVER BLOCK A CALCULATION: the calculation has already produced
/// its value by the time this is called, so anything this method waits on is pure added latency on a
/// path that owes the caller nothing. Do not perform a blocking network write here; hand the record to
/// a channel and let the stream writer drain it.
/// </para>
/// <para>
/// AN IMPLEMENTATION SHOULD NOT THROW, AND IF IT DOES IT IS ABSORBED.
/// <see cref="ExpressionSession.EmitTrace"/> catches everything this method raises, because a
/// diagnostic sink failing must not fail the calculation that produced the diagnostic. That absorption
/// is why this seam is an interface rather than a delegate on the hot path: it is substitutable in
/// tests, so both the emitting and the throwing paths are reachable without a gRPC host.
/// </para>
/// <para>
/// THE SEAM IS ALSO A REDACTION BOUNDARY. A trace record carries EXPRESSION TEXT AND A COMPUTED VALUE,
/// both of which may be caller-supplied data, which is precisely why tracing is off by default in
/// configuration. An implementation that forwards records into a log is responsible for the redaction
/// that implies.
/// </para>
/// </remarks>
public interface IExpressionTraceSink
{
    /// <summary>
    /// Accepts one trace record.
    /// </summary>
    /// <param name="record">The record. Never <see langword="null"/>.</param>
    void Emit(ExpressionTraceRecord record);
}


/// <summary>
/// A server-held expression session: the scope inside which a cross-DataWindow reference resolves, and
/// the owner of one <see cref="ExpressionCalcStack"/> per registered DataWindow.
/// </summary>
/// <remarks>
/// <para>
/// THE SESSION EXISTS FOR EXACTLY ONE REASON - to give <c>foreignvardata.expsvc</c>
/// [n_cst_dwsvc_columnexp.sru:L82] something serializable to be. The published contract says the same
/// on <c>ForeignVarRef</c>. Everything else it holds is here because it is scoped to the same lifetime,
/// not because the session is a general-purpose bag of state.
/// </para>
/// <para>
/// NOT <c>Domain/ValidationSession.cs</c>. That type holds the four pieces of cross-event item-change
/// state from <c>se_cst_dw.sru:L89-L96</c>. This one holds handles and stacks. They share nothing.
/// </para>
/// <para>
/// THREAD SAFETY. Registration, handle resolution, lifetime and closure are all safe for concurrent
/// use - a session is server-held and several in-flight calls may name it at once. The
/// <see cref="ExpressionCalcStack"/> a session hands out is NOT, and says so: a calculation is a single
/// depth-first walk in the legacy and must remain one here.
/// </para>
/// <para>
/// THERE IS NO UNREGISTER, DELIBERATELY. The legacy publishes no operation that removes one foreign
/// link: <c>of_addforeignvar</c> [:L2097] adds, and release happens wholesale when the engine is
/// destroyed [:L2425]. Release here is therefore <see cref="Close"/> and nothing finer, which also
/// means a handle cannot be quietly invalidated underneath a reference that already resolved it.
/// </para>
/// </remarks>
public sealed class ExpressionSession
{
    /// <summary>
    /// Guards <see cref="_hosts"/>, <see cref="_order"/>, <see cref="_isOpen"/> and the access stamp.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>Handle text to registration. Ordinal keys - a handle is opaque and case-sensitive.</summary>
    private readonly Dictionary<string, HostRegistration> _hosts = new(StringComparer.Ordinal);

    /// <summary>
    /// Registration order, kept explicitly because the published open response returns the allocated
    /// handles "in the order requested" and dictionary order is not a documented guarantee.
    /// </summary>
    private readonly List<DataWindowHandle> _order = [];

    /// <summary>
    /// The clock seam. Injected so a characterization run can substitute a deterministic clock.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The trace seam, or <see langword="null"/> when nothing is subscribed.</summary>
    private readonly IExpressionTraceSink? _traceSink;

    /// <summary>
    /// The server-side sink for host-fault detail, or <see langword="null"/> when none was supplied.
    /// </summary>
    /// <remarks>
    /// THE ONLY PLACE AN EXCEPTION'S OWN TEXT MAY GO (see <see cref="Faulted"/>). It is optional so that
    /// every constructor stays usable from a plain unit test, and a null logger is not a licence to put
    /// the detail on the wire instead - the wire payload is the same either way, and the detail is simply
    /// lost when nobody is listening.
    /// </remarks>
    private readonly ILogger? _logger;

    /// <summary>Monotonic source of the per-session part of an allocated handle.</summary>
    private long _handleOrdinal;

    /// <summary>
    /// Monotonic source of the per-session part of a host-fault correlation identifier.
    /// </summary>
    /// <remarks>
    /// DELIBERATELY AN ORDINAL AND NOT A GUID OR A TIMESTAMP. The identifier appears in a wire payload
    /// that a paired characterization recording compares byte for byte, so a random or clock-derived
    /// value would have to be masked on both sides. An ordinal scoped to a session identifier the caller
    /// chose is reproducible by construction.
    /// </remarks>
    private long _faultOrdinal;

    /// <summary>Monotonic source of <see cref="ExpressionTraceRecord.SequenceNumber"/>.</summary>
    private long _traceSequence;

    /// <summary>How many records have been handed to the sink.</summary>
    private long _traceDeliveryAttempted;

    /// <summary>How many the sink accepted without throwing.</summary>
    private long _traceDeliveryDelivered;

    /// <summary>How many the sink threw on, and therefore how many diagnostics were lost.</summary>
    private long _traceDeliveryFailed;

    /// <summary>
    /// The type name of the most recent sink failure, or <see langword="null"/> when there has been none.
    /// </summary>
    /// <remarks>
    /// THE TYPE ONLY, NEVER THE MESSAGE. A sink is supplied from outside this type and its exception
    /// message is arbitrary content that can carry a path, a URL or a credential; this field is readable
    /// by anything holding the session, so admitting a message here would publish that content (C-F). A
    /// type name is authored by whoever wrote the throwing code and identifies the fault without quoting
    /// it - the same allowlist reasoning the ingress applies to an ordinary request fault.
    /// </remarks>
    private string? _lastTraceDeliveryFailureType;

    /// <summary>Whether the session is open. Cleared exactly once, by <see cref="Close"/>.</summary>
    private bool _isOpen = true;

    /// <summary>When the session was last touched, for idle expiry.</summary>
    private DateTimeOffset _lastAccessedAt;

    /// <summary>
    /// Opens a session.
    /// </summary>
    /// <param name="sessionId">
    /// The correlation identifier the contract's <c>session_id</c> carries. Supplied by the caller
    /// rather than generated here so that the identifier the client is told about and the identifier the
    /// session knows itself by are the same value, and so a test can pin it.
    /// </param>
    /// <param name="lifetime">
    /// The idle timeout, from <c>DataServices:Sessions:ExpressionSession</c>. When omitted, defaults
    /// are used - the same defaults the options type declares.
    /// </param>
    /// <param name="columnExpression">
    /// The engine settings, from <c>DataServices:ColumnExpression</c>. Supplies the calculation stack's
    /// reserved capacity and the initial state of <see cref="TraceEnabled"/>. When omitted, defaults are
    /// used.
    /// </param>
    /// <param name="timeProvider">
    /// The clock. Defaults to <see cref="TimeProvider.System"/>. INJECTED BECAUSE EVERY CLOCK READ IS A
    /// DETERMINISM SEAM: idle expiry and the trace timestamp are both non-deterministic values that a
    /// paired characterization recording has to mask on BOTH sides, and substituting the clock is how
    /// the expiry path is reachable in a test without waiting.
    /// </param>
    /// <param name="traceSink">The trace seam. When <see langword="null"/>, nothing is emitted.</param>
    /// <param name="logger">
    /// Where a reached service's own exception is recorded. OPTIONAL AND TRAILING so every existing
    /// construction site stays source-compatible, and so a unit test can construct a session without a
    /// logging container. When omitted the fault detail is simply not recorded anywhere - it is never
    /// substituted onto the wire payload instead. See <see cref="Faulted"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="sessionId"/> is null, empty or whitespace.
    /// </exception>
    public ExpressionSession(
        string sessionId,
        SessionLifetimeOptions? lifetime = null,
        ColumnExpressionOptions? columnExpression = null,
        TimeProvider? timeProvider = null,
        IExpressionTraceSink? traceSink = null,
        ILogger? logger = null,
        string owner = SessionPrincipalResolver.Unattributed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(owner);

        SessionLifetimeOptions effectiveLifetime = lifetime ?? new SessionLifetimeOptions();
        ColumnExpressionOptions effectiveEngine = columnExpression ?? new ColumnExpressionOptions();

        SessionId = sessionId;
        Owner = owner.Length == 0 ? SessionPrincipalResolver.Unattributed : owner;
        IdleTimeout = effectiveLifetime.IdleTimeout;
        CalcStackInitialCapacity = effectiveEngine.CalcStackInitialCapacity > 0
            ? effectiveEngine.CalcStackInitialCapacity
            : ExpressionCalcStack.DefaultInitialCapacity;
        TraceEnabled = effectiveEngine.Trace;

        _timeProvider = timeProvider ?? TimeProvider.System;
        _traceSink = traceSink;
        _logger = logger;

        CreatedAt = _timeProvider.GetUtcNow();
        _lastAccessedAt = CreatedAt;
    }

    /// <summary>
    /// The correlation identifier - <c>session_id</c> on the contract's open, close and channel
    /// messages.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// The authenticated caller this session belongs to, and the only caller its registry will resolve or
    /// close it for.
    /// </summary>
    /// <remarks>
    /// READ ONLY BY THE REGISTRY, AND NEVER RENDERED. The correlation identifier is unguessable, which
    /// bounds discovery of a session but not use of one: an identifier that leaks would otherwise be a
    /// bearer credential for every DataWindow host this session scopes and every expression bound into
    /// them - including the cross-DataWindow variables the session exists to make resolvable. This field
    /// is what the registry compares so possession alone is not authorization (CWE-639, CWE-863). It is
    /// not on the contract and appears in no response.
    /// </remarks>
    public string Owner { get; }

    /// <summary>
    /// How long the session may sit unused before <see cref="HasExpired"/> reports it stale.
    /// </summary>
    /// <remarks>
    /// A NON-POSITIVE VALUE MEANS "NEVER EXPIRES", and it can only arrive through direct construction:
    /// the options validator refuses one, because a zero idle timeout would expire a session the instant
    /// it opened. Reading it as "never" rather than "immediately" is the safe direction - the alternative
    /// would make every session in a misconfigured deployment BLOCKED on its first use, which looks
    /// exactly like the narrowing firing and would send an operator hunting the wrong fault.
    /// </remarks>
    public TimeSpan IdleTimeout { get; }

    /// <summary>
    /// The capacity each <see cref="ExpressionCalcStack"/> reserves, from
    /// <c>DataServices:ColumnExpression:CalcStackInitialCapacity</c> - the legacy's own 20
    /// [n_cst_dwsvc_columnexp.sru:L2422] unless a deployment overrides it.
    /// </summary>
    public int CalcStackInitialCapacity { get; }

    /// <summary>
    /// The session-scoped mirror of the engine's <c>#Trace</c> flag [n_cst_dwsvc_columnexp.sru:L98],
    /// initialised from <c>DataServices:ColumnExpression:Trace</c> and settable exactly as
    /// <c>of_settrace</c> sets it [:L2409-L2411]. When false, <see cref="EmitTrace"/> produces nothing.
    /// </summary>
    /// <remarks>
    /// SETTABLE ON PURPOSE, AND IT IS NOT THE SAME SWITCH AS A CHANNEL SUBSCRIPTION. Subscribing to the
    /// trace channel and enabling the engine's trace are two separate acts, which the published contract
    /// states on <c>TraceChannelRequest.subscribe</c>; a subscriber on a session whose trace is off
    /// correctly receives nothing.
    /// </remarks>
    public bool TraceEnabled { get; set; }

    /// <summary>
    /// How the trace sink has been behaving: how many records were handed to it, how many it accepted, how
    /// many it threw on, and the type of the most recent failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS EXISTS. The trace is fire-and-forget by design - it is ordering pattern (a), and a
    /// diagnostic sink is not permitted to break the thing it is observing - so
    /// <see cref="EmitTrace"/> absorbs whatever the sink throws and the calculation still succeeds. That
    /// much is correct. What absorbing must NOT also do is DISCARD the failure: a sink that
    /// throws on every record loses every trace, and if nothing reports it, nothing anywhere says the
    /// diagnostic channel has stopped working. An invisibly failing diagnostic is worse than a disabled
    /// one, because it looks enabled.
    /// </para>
    /// <para>
    /// So delivery is reported INDEPENDENTLY of the calculation. A caller reads
    /// <see cref="ExpressionTraceDeliveryReport.Failed"/> to learn that records are being lost and
    /// <see cref="ExpressionTraceDeliveryReport.LastFailureType"/> to learn what kind of fault is losing
    /// them, without a single calculation result changing.
    /// </para>
    /// <para>
    /// A COUNTER RATHER THAN A LOG, DELIBERATELY. This type takes no logger and is not going to: it is a
    /// pure domain type whose only injected dependencies are a clock and a sink, which is what makes every
    /// behaviour on it assertable with no host. The report is the seam through which a host surfaces this
    /// on its own operator channel, and a counter additionally answers the question a log answers badly -
    /// "is this still happening, and how often" - without any sampling or retention policy.
    /// </para>
    /// <para>
    /// The counters are cumulative for the life of the session and are never reset. Attempted counts only
    /// records actually handed to a sink, so with no sink registered, or with tracing off, all four members
    /// stay at their initial values.
    /// </para>
    /// </remarks>
    public ExpressionTraceDeliveryReport TraceDelivery => new(
        Attempted: Volatile.Read(ref _traceDeliveryAttempted),
        Delivered: Volatile.Read(ref _traceDeliveryDelivered),
        Failed: Volatile.Read(ref _traceDeliveryFailed),
        LastFailureType: Volatile.Read(ref _lastTraceDeliveryFailureType));

    /// <summary>When the session was opened, from the injected clock.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// When the session was last touched. Advanced by <see cref="Touch"/> and by every successful
    /// resolution.
    /// </summary>
    public DateTimeOffset LastAccessedAt
    {
        get
        {
            lock (_gate)
            {
                return _lastAccessedAt;
            }
        }
    }

    /// <summary>
    /// Whether the session is still open. Once false, EVERY cross-DataWindow reference into it is
    /// BLOCKED with <see cref="CrossDataWindowStatus.BlockedSessionClosed"/> - never answered from
    /// state left over from before the close.
    /// </summary>
    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _isOpen;
            }
        }
    }

    /// <summary>How many DataWindows are co-resident in this session.</summary>
    public int HandleCount
    {
        get
        {
            lock (_gate)
            {
                return _order.Count;
            }
        }
    }

    /// <summary>
    /// The co-resident handles, IN REGISTRATION ORDER - the value the contract's open response returns
    /// as <c>datawindow_handles</c>, and the only handles a foreign reference in this session may name.
    /// </summary>
    public ImmutableArray<DataWindowHandle> Handles
    {
        get
        {
            lock (_gate)
            {
                return [.. _order];
            }
        }
    }

    /// <summary>
    /// Registers a DataWindow's expression service under a NEWLY ALLOCATED session-scoped handle.
    /// </summary>
    /// <param name="host">
    /// The service to make co-resident. Implemented by the column-expression engine.
    /// </param>
    /// <returns>
    /// The allocated handle, or <see cref="DataWindowHandle.None"/> when the session is already closed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="host"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE ALLOCATED VALUE IS SESSION-QUALIFIED, WHICH IS WHAT MAKES THE NARROWING ENFORCEABLE. It is
    /// built from this session's identifier and a monotonic ordinal, so no two sessions can ever mint
    /// the same handle and a handle carried in from elsewhere therefore MISSES this session's registry
    /// and is BLOCKED rather than colliding with a local registration and resolving to the wrong
    /// DataWindow. Nothing parses the handle to reach that verdict - detection is by lookup, and the
    /// value stays opaque to every consumer.
    /// </para>
    /// <para>
    /// Deterministic by construction, so a characterization run needs no handle-generation seam.
    /// </para>
    /// </remarks>
    public DataWindowHandle Register(IExpressionServiceHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        lock (_gate)
        {
            if (!_isOpen)
            {
                return DataWindowHandle.None;
            }

            _handleOrdinal++;
            DataWindowHandle handle = new(
                SessionId + "/" + _handleOrdinal.ToString(CultureInfo.InvariantCulture));

            AddRegistrationUnderLock(handle, host);

            return handle;
        }
    }

    /// <summary>
    /// Registers a DataWindow's expression service under a CALLER-SUPPLIED handle, for a caller that
    /// already has an identifier for the DataWindow and needs the session to adopt it.
    /// </summary>
    /// <param name="handle">
    /// The handle to register under. Must not be <see cref="DataWindowHandle.None"/>.
    /// </param>
    /// <param name="host">The service to make co-resident.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success; <see cref="RetCode.E_INVALID_ARGUMENT"/> when
    /// <paramref name="handle"/> is empty; <see cref="RetCode.FAILED"/> when the handle is already
    /// registered, which is the code the oracle answers for a duplicate definition
    /// [n_cst_dwsvc_columnexp.sru:L2123]; <see cref="RetCode.E_NOT_EXISTS"/> when the session is closed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="host"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// A DUPLICATE IS REFUSED RATHER THAN OVERWRITTEN, which mirrors <c>of_addforeignvar</c> refusing a
    /// name that already resolves [:L2121-L2124]. Silently replacing a registration would repoint every
    /// reference that had already bound to that handle - a live-pointer swap under references that
    /// cannot see it, which is the precise failure this whole file exists to make impossible.
    /// </remarks>
    public long Register(DataWindowHandle handle, IExpressionServiceHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (handle.IsEmpty)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        lock (_gate)
        {
            if (!_isOpen)
            {
                return RetCode.E_NOT_EXISTS;
            }

            if (_hosts.ContainsKey(handle.Value))
            {
                return RetCode.FAILED;
            }

            AddRegistrationUnderLock(handle, host);

            return RetCode.OK;
        }
    }

    /// <summary>
    /// Whether a handle names a DataWindow that is co-resident in this OPEN session - the co-residency
    /// test the whole narrowing turns on.
    /// </summary>
    /// <param name="handle">The handle to test.</param>
    /// <returns>
    /// <see langword="true"/> only when the session is open and the handle is registered in it.
    /// </returns>
    public bool IsCoResident(DataWindowHandle handle)
    {
        if (handle.IsEmpty)
        {
            return false;
        }

        lock (_gate)
        {
            return _isOpen && _hosts.ContainsKey(handle.Value);
        }
    }

    /// <summary>
    /// Resolves a handle to its co-resident service.
    /// </summary>
    /// <param name="handle">The handle to resolve.</param>
    /// <param name="host">
    /// The service on success; <see langword="null"/> otherwise. NEVER SET FROM A CLOSED SESSION.
    /// </param>
    /// <returns><see langword="true"/> when the handle resolved.</returns>
    /// <remarks>
    /// The primitive every resolution in this type is built on. It deliberately answers a bare boolean
    /// and leaves the DEFINED ERROR to the callers that have an expression and a caret to report it
    /// against; use <see cref="ResolveForeignVariable(ForeignVariableReference)"/> or
    /// <see cref="ResolveContextHost"/> when a structured outcome is wanted.
    /// </remarks>
    public bool TryGetHost(DataWindowHandle handle, out IExpressionServiceHost? host)
    {
        host = null;

        if (handle.IsEmpty)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_isOpen || !_hosts.TryGetValue(handle.Value, out HostRegistration? registration))
            {
                return false;
            }

            host = registration.Host;
            return true;
        }
    }

    /// <summary>
    /// The calculation and recursion stack belonging to one co-resident DataWindow - the port of that
    /// engine's private <c>_vecCalcStack</c> [n_cst_dwsvc_columnexp.sru:L110].
    /// </summary>
    /// <param name="handle">The handle whose stack is wanted.</param>
    /// <returns>
    /// The stack, or <see langword="null"/> when the handle is not co-resident or the session is
    /// closed. A closed session hands back nothing, so a late caller cannot append to, read from, or
    /// build a trace out of a stack that was supposed to have been released.
    /// </returns>
    /// <remarks>
    /// The stack was created and reserved when the handle was registered, so the reservation at
    /// [:L2422] is paired with a registration exactly as the legacy's is paired with a constructor. The
    /// SAME instance is returned on every call for a given handle: a calculation's push must be visible
    /// to the recursion guard and the trace builder that run inside it.
    /// </remarks>
    public ExpressionCalcStack? CalcStackFor(DataWindowHandle handle)
    {
        if (handle.IsEmpty)
        {
            return null;
        }

        lock (_gate)
        {
            return _isOpen && _hosts.TryGetValue(handle.Value, out HostRegistration? registration)
                ? registration.CalcStack
                : null;
        }
    }

    /// <summary>
    /// Advances the idle stamp, marking the session in use.
    /// </summary>
    /// <remarks>
    /// Called by the registry on every successful lookup and by every resolution that reaches a host, so
    /// a session in active use never expires underneath the calculation using it. A no-op once closed -
    /// touching a closed session must not resurrect it.
    /// </remarks>
    public void Touch()
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
    /// Whether the session has been idle longer than <see cref="IdleTimeout"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the session is open and stale. A CLOSED SESSION IS NOT "EXPIRED": it
    /// is already gone, and reporting it as expired would invite a second release.
    /// </returns>
    /// <remarks>
    /// A METHOD RATHER THAN A PROPERTY BECAUSE IT READS THE CLOCK. Two evaluations can legitimately
    /// disagree, and a property reads as a stored fact.
    /// </remarks>
    public bool HasExpired()
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
    /// Validates that the session may be used and records the activity - ALL UNDER ONE ACQUISITION OF THE
    /// GATE.
    /// </summary>
    /// <returns>
    /// <see cref="ExpressionSessionAcquisition.Acquired"/> when the session was open, unexpired and has
    /// now had its activity recorded; <see cref="ExpressionSessionAcquisition.Closed"/> when it had
    /// already been closed; <see cref="ExpressionSessionAcquisition.Expired"/> when it had gone idle past
    /// its timeout, in which case IT IS CLOSED BEFORE THIS RETURNS.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THREE SEPARATE CALLS COULD NOT BE MADE ATOMIC BY ORDERING THEM. Asking <see cref="IsOpen"/>, then
    /// <see cref="HasExpired"/>, then <see cref="Touch"/> takes and releases the gate three times, and a
    /// close landing in either interval produced an outcome no single state of the session justified: the
    /// checks passed, the touch silently did nothing, and the caller received a CLOSED session reported as
    /// a successful lookup. On THIS session type the consequence is sharper than a stale read - closing
    /// clears every host registration and every calculation stack, so the caller would then resolve
    /// handles against emptied state rather than being told the session had gone.
    /// </para>
    /// <para>
    /// Under one acquisition there is no interval. A concurrent <see cref="Close"/> either precedes the
    /// acquisition, which is refused, or follows it, and the next acquisition is refused - both outcomes
    /// the session's own state justifies, and neither is "success with a closed session".
    /// </para>
    /// <para>
    /// Expiry is decided AND acted on together, so no second observer can look at a session this method
    /// found stale and conclude anything different. The registry still removes it and releases its slot.
    /// </para>
    /// </remarks>
    public ExpressionSessionAcquisition Acquire()
    {
        lock (_gate)
        {
            if (!_isOpen)
            {
                return ExpressionSessionAcquisition.Closed;
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();

            if (IdleTimeout > TimeSpan.Zero && now - _lastAccessedAt > IdleTimeout)
            {
                // Terminal under this lock, and it releases exactly what Close() releases - the stacks and
                // the registrations - so an expired session cannot be reached through a handle afterwards.
                foreach (HostRegistration registration in _hosts.Values)
                {
                    registration.CalcStack.Clear();
                }

                _hosts.Clear();
                _order.Clear();
                _isOpen = false;

                return ExpressionSessionAcquisition.Expired;
            }

            _lastAccessedAt = now;

            return ExpressionSessionAcquisition.Acquired;
        }
    }

    /// <summary>
    /// Closes the session and releases every registration DETERMINISTICALLY - the boundary equivalent of
    /// the legacy destroying its vector in its destructor [n_cst_dwsvc_columnexp.sru:L2425].
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when this call closed an open session; <see langword="false"/> when it was
    /// already closed. That is <c>CloseExpressionSessionResponse.was_open</c>, and it makes the call
    /// IDEMPOTENT so a caller that cannot safely retry a close does not leak a session on a transport
    /// hiccup.
    /// </returns>
    /// <remarks>
    /// <para>
    /// EVERY REGISTRATION GOES, AND EVERY STACK IS CLEARED. Leaving either behind would keep a foreign
    /// engine reachable through a handle whose session no longer exists - a leak that presents as
    /// working software, because the reference would keep resolving to progressively staler state. After
    /// this call every handle that named this session resolves to
    /// <see cref="CrossDataWindowStatus.BlockedSessionClosed"/>.
    /// </para>
    /// <para>
    /// The stacks are cleared before the registrations are dropped so that any reference still holding a
    /// stack instance sees it empty rather than merely unreachable.
    /// </para>
    /// </remarks>
    public bool Close()
    {
        lock (_gate)
        {
            if (!_isOpen)
            {
                return false;
            }

            foreach (HostRegistration registration in _hosts.Values)
            {
                registration.CalcStack.Clear();
            }

            _hosts.Clear();
            _order.Clear();
            _isOpen = false;

            return true;
        }
    }

    /// <summary>
    /// BINDS a foreign variable BY NAME - the port of <c>of_addforeignvar</c>'s resolution step
    /// [n_cst_dwsvc_columnexp.sru:L2119-L2135], with the live control replaced by a session-scoped
    /// handle.
    /// </summary>
    /// <param name="handle">
    /// The co-resident DataWindow the variable is defined in. Replaces <c>dw.ColumnExp</c> [:L2126].
    /// </param>
    /// <param name="name">The variable's name, as it is defined in THAT DataWindow.</param>
    /// <returns>
    /// A resolution carrying the service and the one-based index on success, or the defined error for
    /// each of the ways it can fail. <see cref="ForeignVariableResolution.Host"/> is non-null ONLY on
    /// success.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE ORACLE'S ORDER IS PRESERVED, GUARD FOR GUARD: the empty-name check [:L2119], then the
    /// invalid-object check [:L2120] - here the co-residency check, which is where the narrowing lands -
    /// then the lookup <c>expSvc._of_FindVarIndex(name)</c> [:L2131], then the refusal of a non-positive
    /// result with the oracle's own text and its <see cref="RetCode.E_VAR_NOT_FOUND"/>
    /// [:L2132-L2135].
    /// </para>
    /// <para>
    /// ONE ORACLE GUARD IS DELIBERATELY ABSENT, AND IT BELONGS SOMEWHERE ELSE. [:L2121-L2124] refuses a
    /// name that is ALREADY DEFINED IN THE CALLING service's own table. The session has no local
    /// variable table - the engine owns that, as <c>ExpressionVariableEnvironment</c> - so the duplicate
    /// check is the engine's to perform before it calls this. Performing it here would require the
    /// session to know the caller's variables, which is precisely the coupling the interface split
    /// exists to avoid.
    /// </para>
    /// <para>
    /// THE BACK-LINK IS ALSO THE ENGINE'S. [:L2139-L2141] writes a reverse pointer into the foreign
    /// service's own <c>links[]</c> array so that a change to the variable can notify its dependants.
    /// That is a mutation of the foreign engine's table, reached through the resolved
    /// <see cref="ForeignVariableResolution.Host"/>, and it is recorded here only so that a reader does
    /// not conclude the link was forgotten.
    /// </para>
    /// </remarks>
    public ForeignVariableResolution ResolveForeignVariable(DataWindowHandle handle, string? name)
    {
        // [:L2119] - the empty-name guard, which answers a code and reports nothing.
        if (string.IsNullOrEmpty(name))
        {
            return Malformed(handle, RetCode.E_INVALID_ARGUMENT);
        }

        // [:L2120] - the "is there anything to call" guard. A handle naming nothing fails it.
        if (handle.IsEmpty)
        {
            return Malformed(handle, RetCode.E_INVALID_OBJECT);
        }

        ForeignVariableResolution scope = ResolveHandle(handle, index: 0);
        if (!scope.IsResolved)
        {
            return scope;
        }

        IExpressionServiceHost host = scope.Host!;

        // [:L2131] - the lookup happens INSIDE the foreign service, against ITS table.
        int resolvedIndex;
        try
        {
            resolvedIndex = host.FindVarIndex(name);
        }
        catch (Exception exception)
        {
            return Faulted(handle, exception);
        }

        // [:L2132-L2135] - a non-positive index is the oracle's own error, with its own text and its own
        // return code. NOT the narrowing: the service was reachable and the name simply is not in it.
        if (resolvedIndex <= 0)
        {
            ExpressionParseError error = ParseErrorFormatter.CreatePlainError(
                ExpressionErrorSite.ForeignVariableUndefined,
                name);

            return new ForeignVariableResolution
            {
                Status = CrossDataWindowStatus.VariableNotFound,
                Handle = handle,
                Index = 0,
                Host = null,
                Error = error,
                ReturnCode = error.ReturnCode ?? RetCode.E_VAR_NOT_FOUND,
            };
        }

        Touch();

        return new ForeignVariableResolution
        {
            Status = CrossDataWindowStatus.Resolved,
            Handle = handle,
            Index = resolvedIndex,
            Host = host,
            Error = null,
            ReturnCode = RetCode.OK,
        };
    }

    /// <summary>
    /// BINDS an already-stored foreign reference - the step the two dereference sites perform before they
    /// call through the pointer [n_cst_dwsvc_columnexp.sru:L2199-L2200, :L2384-L2385].
    /// </summary>
    /// <param name="reference">
    /// The stored reference - the port of <c>foreignvardata</c> [:L80-L83], carrying the index inside the
    /// foreign service and the handle naming it.
    /// </param>
    /// <returns>
    /// A resolution carrying the service and the reference's index on success; otherwise the defined
    /// error. BLOCKED when the handle is not co-resident or the session has closed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="reference"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// NO NAME IS LOOKED UP HERE, AND THAT IS FAITHFUL. The oracle resolved the name once when the
    /// reference was bound and stored the resulting index; the dereference sites use that index directly
    /// and never re-resolve. A non-positive stored index is therefore a structurally impossible state -
    /// <see cref="ResolveForeignVariable(DataWindowHandle, string?)"/> refuses to produce one - and is
    /// refused as a malformed reference rather than repaired by a fresh lookup, because a repair would
    /// silently bind to whatever that name resolves to NOW.
    /// </remarks>
    public ForeignVariableResolution ResolveForeignVariable(ForeignVariableReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        DataWindowHandle handle = DataWindowHandle.From(reference.Handle);

        if (handle.IsEmpty)
        {
            return Malformed(handle, RetCode.E_INVALID_OBJECT);
        }

        if (reference.Index <= 0)
        {
            return Malformed(handle, RetCode.E_INVALID_ARGUMENT);
        }

        return ResolveHandle(handle, reference.Index);
    }

    /// <summary>
    /// DEREFERENCES a foreign reference and returns the variable's value - the port of the two identical
    /// calls <c>foreign.expSvc._of_CalcVarExpValue(foreign.index, row, dwo, this)</c>
    /// [n_cst_dwsvc_columnexp.sru:L2200] and [:L2385].
    /// </summary>
    /// <param name="reference">The stored foreign reference.</param>
    /// <param name="callerRow">
    /// <c>row</c> - THE CALLING DataWindow's row, handed down as the context triple's row. Not the
    /// foreign DataWindow's row: the four-argument arity reads that one for itself, which is exactly why
    /// it exists.
    /// </param>
    /// <param name="callerDwo"><c>dwo</c> - the CALLING DataWindow's object, handed down as context.</param>
    /// <param name="caller">
    /// <c>this</c> - THE CALLER ITSELF, handed down as context so that a nested reference inside the
    /// foreign variable's own expression resolves back against the caller.
    /// </param>
    /// <returns>
    /// The value on success. On any failure, <see cref="ExpressionValueResult.Value"/> is
    /// <see cref="string.Empty"/> and <see cref="ExpressionValueResult.Succeeded"/> is false - A BLOCKED
    /// REFERENCE NEVER CARRIES A VALUE.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="reference"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// AN EMPTY ANSWER FROM A REACHED SERVICE IS A FAILURE, NOT AN EMPTY VALUE, and that is the oracle's
    /// convention: both call sites follow the call with <c>if sVal = "" then return ""</c> [:L2201], so an
    /// empty string aborts the whole preprocessing pass. The result therefore reports
    /// <see cref="RetCode.FAILED"/> with <see cref="CrossDataWindowStatus.Resolved"/> - the reference
    /// resolved, the service answered, and the answer was a failure - which keeps that case
    /// distinguishable from the narrowing.
    /// </para>
    /// <para>
    /// A FOREIGN VALUE IS ALWAYS READ NOW, NEVER AT BIND TIME. The oracle refuses to expand a foreign
    /// variable statically [:L1425-L1428] and its own specification says dynamic expansion is required
    /// [docs/n_cst_dwsvc_columnexp.md:L34]. That is why this method exists at all: the value has to be
    /// fetched at calculation time, through a live reference, which is what co-residency provides and
    /// what nothing outside the session can.
    /// </para>
    /// </remarks>
    public ExpressionValueResult CalcForeignVariableValue(
        ForeignVariableReference reference,
        long callerRow,
        IDataWindowObject? callerDwo,
        IExpressionServiceHost? caller)
    {
        ArgumentNullException.ThrowIfNull(reference);

        ForeignVariableResolution resolution = ResolveForeignVariable(reference);
        if (!resolution.IsResolved)
        {
            return NoValue(resolution);
        }

        string value;
        try
        {
            // The four-argument arity, argument for argument as [:L2200] and [:L2385] pass them.
            value = resolution.Host!.CalcVarExpValue(
                resolution.Index,
                callerRow,
                callerDwo,
                caller);
        }
        catch (Exception exception)
        {
            return NoValue(Faulted(resolution.Handle, exception));
        }

        Touch();

        return Answered(resolution.Handle, value);
    }

    /// <summary>
    /// Resolves the CONTEXT service named by a context triple - the substitution for
    /// <c>ctx_expsvc</c> [n_cst_dwsvc_columnexp.sru:L2148].
    /// </summary>
    /// <param name="context">The triple. Its handle names the context service.</param>
    /// <returns>
    /// A resolution whose <see cref="ForeignVariableResolution.Host"/> is the context service on
    /// success, with <see cref="ForeignVariableResolution.Index"/> zero because no name has been resolved
    /// yet; otherwise the defined error, BLOCKED where the handle reaches outside the session.
    /// </returns>
    /// <remarks>
    /// THE CONTEXT PATH IS SUBJECT TO THE SAME NARROWING AS THE FOREIGN PATH, and that is the reason this
    /// method exists rather than the triple simply carrying a service reference. Both are "resolve
    /// against a service that is not me", so both must be refused the same way when they reach outside
    /// the session - otherwise the undocumented `@` sigil [:L1253] would be a hole straight through the
    /// co-residency check.
    /// </remarks>
    public ForeignVariableResolution ResolveContextHost(ExpressionContext context)
    {
        if (!context.IsSpecified)
        {
            return Malformed(context.Handle, RetCode.E_INVALID_ARGUMENT);
        }

        return ResolveHandle(context.Handle, index: 0);
    }

    /// <summary>
    /// Resolves a CONTEXT variable reference - the `@` sigil's two arms
    /// [n_cst_dwsvc_columnexp.sru:L2180-L2187].
    /// </summary>
    /// <param name="variable">
    /// The reference, whose <see cref="VariableReference.IsCtx"/> must be set. Its
    /// <see cref="VariableReference.IsMacro"/> selects the arm: set means `@$name` or `@$$name` and reads
    /// a VARIABLE of the context service; clear means a bare `@name` and reads a COLUMN of it.
    /// </param>
    /// <param name="context">
    /// The context triple. <see cref="ExpressionContext.Row"/> is where the read happens.
    /// </param>
    /// <param name="callerRow"><c>row</c> - the CALLER's row, handed down as the nested context.</param>
    /// <param name="callerDwo"><c>dwo</c> - the CALLER's object, handed down as the nested context.</param>
    /// <param name="caller"><c>this</c> - the CALLER itself, handed down as the nested context.</param>
    /// <returns>
    /// The value on success; otherwise a result carrying <see cref="string.Empty"/> and the defined
    /// error.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="variable"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE TWO ARMS, TRANSCRIBED. When the reference is a macro the oracle calls
    /// <c>ctx_expSvc._of_CalcVarExpValue(ctx_row, ctx_dwo, ctx_expSvc._of_FindVarIndex(name), row, dwo,
    /// this)</c> [:L2182]; otherwise it calls <c>ctx_expsvc._of_GetItemExpValue(ctx_row, name)</c>
    /// [:L2185]. Both are followed by <c>if sVal = "" then return ""</c>, so an empty answer aborts the
    /// pass. NOTE WHICH POSITION IS WHICH: the context's row and object say WHERE to read, and the
    /// caller's row, object and self are handed down as the context OF THE NESTED CALL. The order is the
    /// oracle's and is not rearranged.
    /// </para>
    /// <para>
    /// THE INDEX IS PASSED THROUGH UNCHECKED, EXACTLY AS THE ORACLE PASSES IT, AND THAT IS A PRESERVED
    /// DEFECT. [:L2182] feeds <c>_of_FindVarIndex</c>'s answer straight into the value call with no
    /// test, so a context variable that does not exist in the context service arrives there as
    /// <c>0</c> - and <c>GlobalVars[0]</c> is an array-boundary fault in PowerBuilder. The
    /// non-context arm at [:L2189-L2194] DOES test the same lookup and reports it properly, so the
    /// omission is an inconsistency in the oracle rather than a deliberate design, and it is recorded
    /// here rather than repaired: testing the index here would change which of the two arms reports a
    /// missing name, and that is observable.
    /// </para>
    /// <para>
    /// WHAT DOES CHANGE IS THE SHAPE OF THE FAULT, AND ONLY BECAUSE IT MUST. In-process the fault
    /// propagates as a PowerBuilder runtime error; across a service boundary an escaping exception would
    /// tear down a stream shared with other calculations, so it is caught and reported as
    /// <see cref="CrossDataWindowStatus.HostFaulted"/> - with fixed text and a correlation identifier
    /// rather than the exception's own text, which is recorded server-side instead (CWE-209, see
    /// <see cref="Faulted"/>). The value is still absent and the caller still fails - the outcome is
    /// preserved, the delivery channel is not.
    /// </para>
    /// <para>
    /// A CONTEXT REFERENCE CANNOT BE STATICALLY EXPANDED [:L1417] AND A CONTEXT FUNCTION MACRO IS NOT
    /// SUPPORTED AT ALL [:L1308]. Both are the parser's refusals and are reproduced by
    /// <see cref="RejectContextVariableStaticExpansion"/> and
    /// <see cref="RejectContextFunctionMacro"/>; this method therefore only ever sees the dynamic
    /// variable forms, which is why it has two arms and not four.
    /// </para>
    /// </remarks>
    public ExpressionValueResult CalcContextVariableValue(
        VariableReference variable,
        ExpressionContext context,
        long callerRow,
        IDataWindowObject? callerDwo,
        IExpressionServiceHost? caller)
    {
        ArgumentNullException.ThrowIfNull(variable);

        // [:L2180] gates this whole path on `isCtx`. A non-context reference resolves against the LOCAL
        // table [:L2188-L2211], which is the engine's own business and not the session's.
        if (!variable.IsCtx)
        {
            return NoValue(Malformed(context.Handle, RetCode.E_INVALID_ARGUMENT));
        }

        ForeignVariableResolution resolution = ResolveContextHost(context);
        if (!resolution.IsResolved)
        {
            return NoValue(resolution);
        }

        IExpressionServiceHost host = resolution.Host!;

        string value;
        try
        {
            if (variable.IsMacro)
            {
                // [:L2182]. The inner lookup answers 0 for an undefined name and is forwarded unchecked,
                // exactly as the oracle forwards it - see the remarks.
                int contextIndex = host.FindVarIndex(variable.Name);

                value = host.CalcVarExpValue(
                    context.Row,
                    context.Dwo,
                    contextIndex,
                    callerRow,
                    callerDwo,
                    caller);
            }
            else
            {
                // [:L2185] - a bare `@name` reads a COLUMN of the context DataWindow, not a variable.
                value = host.GetItemExpValue(context.Row, variable.Name);
            }
        }
        catch (Exception exception)
        {
            return NoValue(Faulted(context.Handle, exception));
        }

        Touch();

        return Answered(context.Handle, value);
    }

    /// <summary>
    /// Builds and emits one trace record for a completed calculation - the port of the trace block
    /// [n_cst_dwsvc_columnexp.sru:L752-L758].
    /// </summary>
    /// <param name="handle">The DataWindow whose expression was calculated. Supplies the stack.</param>
    /// <param name="row"><c>row</c> - the ONE-BASED row the expression was calculated for.</param>
    /// <param name="dwo">
    /// <c>ColExpDatas[index].dwo</c> - the column being calculated. Its
    /// <see cref="IDataWindowObject.Name"/> becomes the FINAL, UNDELIMITED segment of the call stack.
    /// </param>
    /// <param name="expression">
    /// <c>sExp</c> - the expression AS EVALUATED, after macro preprocessing.
    /// </param>
    /// <param name="value">
    /// <c>sVal</c> - the value, ALREADY RENDERED BY THE CALLER including the oracle's <c>(null)</c>
    /// substitution [:L758]. That conflation is legacy behaviour and belongs to the engine, which knows
    /// the column's type and its empty-string-is-null flag; re-deriving it here would need both.
    /// </param>
    /// <returns>
    /// The record, or <see langword="null"/> when nothing was emitted - tracing is off, or the handle is
    /// not co-resident in this open session.
    /// </returns>
    /// <remarks>
    /// <para>
    /// EMISSION NEVER BLOCKS AND NEVER FAILS A CALCULATION. The trace is ordering pattern (a),
    /// fire-and-forget, so anything the sink throws is absorbed here: a diagnostic sink is not permitted
    /// to break the thing it is observing. The record is still returned, so a caller that wants to
    /// forward or assert on it does not depend on the sink at all.
    /// </para>
    /// <para>
    /// THE SEQUENCE NUMBER IS FOR DETECTION, NOT ENFORCEMENT. It is per-session and monotonic, so a
    /// consumer can spot and reorder out-of-order delivery - which pure diagnostics may do, unlike macro
    /// invocation, where an out-of-order arrival is a hard error.
    /// </para>
    /// <para>
    /// GATED THE WAY THE ORACLE GATES IT. [:L752] wraps the whole block in <c>if #Trace then</c>, so
    /// with tracing off the stack is not even walked. <see cref="TraceEnabled"/> is checked first here
    /// for the same reason, and the sequence number is not consumed by a suppressed record.
    /// </para>
    /// </remarks>
    public ExpressionTraceRecord? EmitTrace(
        DataWindowHandle handle,
        long row,
        IDataWindowObject? dwo,
        string? expression,
        string? value)
    {
        // [:L752] - with the trace off, nothing is built and nothing is walked.
        if (!TraceEnabled)
        {
            return null;
        }

        ExpressionCalcStack? stack = CalcStackFor(handle);
        if (stack is null)
        {
            return null;
        }

        string columnName = dwo?.Name ?? string.Empty;

        ExpressionTraceRecord record = new()
        {
            SequenceNumber = Interlocked.Increment(ref _traceSequence),
            SessionId = SessionId,
            Handle = handle,
            Row = row,
            ColumnName = columnName,

            // [:L753-L757]. The terminal segment carries no trailing delimiter.
            Stack = stack.BuildCallStack(columnName),
            StackFrames = stack.Frames,
            Expression = expression ?? string.Empty,
            Value = value ?? string.Empty,

            // [:L297] - the depth is the stack's own count, unbounded by the reservation.
            Depth = stack.Depth,
            Timestamp = _timeProvider.GetUtcNow(),
        };

        IExpressionTraceSink? sink = _traceSink;
        if (sink is not null)
        {
            Interlocked.Increment(ref _traceDeliveryAttempted);

            try
            {
                sink.Emit(record);
                Interlocked.Increment(ref _traceDeliveryDelivered);
            }
            catch (Exception exception)
            {
                // ABSORBED BUT NOT DISCARDED - the two are different things, and only the
                // first is intended. The calculation has already produced its value, so a failing
                // diagnostic sink must not turn a successful calculation into a failed request; the record
                // is returned regardless. But a sink that throws on every record silently loses EVERY
                // trace, and a diagnostic channel that fails invisibly is worse than one that is switched
                // off, because it looks switched on.
                //
                // So the failure is COUNTED and its exception TYPE is retained on
                // TraceDelivery. Only the type: a sink's exception message is arbitrary upstream content
                // and can carry a path, a URL or a credential, and this record is readable by anything
                // holding the session (C-F).
                Interlocked.Increment(ref _traceDeliveryFailed);
                Interlocked.Exchange(
                    ref _lastTraceDeliveryFailureType,
                    exception.GetType().FullName ?? exception.GetType().Name);
            }
        }

        return record;
    }

    /// <summary>
    /// Reproduces the parser's refusal to expand a FOREIGN variable statically -
    /// <c>"解析变量宏失败!~n外部变量[" + varData.name + "],不支持静态展开"</c>
    /// [n_cst_dwsvc_columnexp.sru:L1425-L1428].
    /// </summary>
    /// <param name="syntax">
    /// The expression being parsed. THE TRAILER-EXTENDED TEXT, as every caret-bearing site passes it.
    /// </param>
    /// <param name="caretPosition">The one-based caret position within <paramref name="syntax"/>.</param>
    /// <param name="variableName">
    /// <c>varData.name</c> - the offending variable, substituted into the message.
    /// </param>
    /// <returns>
    /// The error, carrying <see cref="RetCode.E_INVALID_ARGUMENT"/> as
    /// <see cref="ExpressionParseError.ReturnCode"/>.
    /// </returns>
    /// <remarks>
    /// EXPOSED FROM THIS FILE BECAUSE IT IS EVIDENCE FOR THE DESIGN, NOT ONLY A PARSER DETAIL. Static
    /// expansion substitutes a value once at bind time and never reads it again; the oracle refuses it
    /// for foreign variables precisely because a foreign value must be read AT CALCULATION TIME, and its
    /// own specification says dynamic expansion is required [docs/n_cst_dwsvc_columnexp.md:L34]. The
    /// legacy therefore already insisted on the live read that a session-scoped handle provides and that
    /// nothing outside a session can. The text and the return code both come from the oracle-sourced
    /// catalogue, so neither can drift from the transcription.
    /// </remarks>
    public static ExpressionParseError RejectForeignStaticExpansion(
        string? syntax,
        long caretPosition,
        string? variableName)
    {
        return ParseErrorFormatter.CreateParseError(
            ExpressionErrorSite.VariableMacroForeignStaticExpansionUnsupported,
            syntax,
            caretPosition,
            variableName);
    }

    /// <summary>
    /// Reproduces the parser's refusal to expand a CONTEXT variable statically -
    /// <c>"解析函数宏失败!~n上下文变量[" + varData.name + "],不支持静态展开"</c>
    /// [n_cst_dwsvc_columnexp.sru:L1416-L1419].
    /// </summary>
    /// <param name="syntax">The trailer-extended expression being parsed.</param>
    /// <param name="caretPosition">The one-based caret position within <paramref name="syntax"/>.</param>
    /// <param name="variableName"><c>varData.name</c> - the offending variable.</param>
    /// <returns>The error, carrying <see cref="RetCode.E_INVALID_ARGUMENT"/>.</returns>
    /// <remarks>
    /// The context counterpart of <see cref="RejectForeignStaticExpansion"/> and it is checked FIRST:
    /// [:L1416] tests <c>bIsCtx</c> before the variable is even looked up, so a statically expanded
    /// context reference is refused whether or not the name exists. Note that the message says
    /// "function macro" even though the failure is a VARIABLE macro - the oracle reuses the wrong prefix
    /// there, and that wording is preserved verbatim rather than corrected.
    /// </remarks>
    public static ExpressionParseError RejectContextVariableStaticExpansion(
        string? syntax,
        long caretPosition,
        string? variableName)
    {
        return ParseErrorFormatter.CreateParseError(
            ExpressionErrorSite.FunctionMacroContextVariableStaticExpansionUnsupported,
            syntax,
            caretPosition,
            variableName);
    }

    /// <summary>
    /// Reproduces the parser's refusal of a CONTEXT-SCOPED FUNCTION MACRO -
    /// <c>"解析函数宏失败!~n不支持引用上下文的函数宏"</c> [n_cst_dwsvc_columnexp.sru:L1306-L1310].
    /// </summary>
    /// <param name="syntax">The trailer-extended expression being parsed.</param>
    /// <param name="caretPosition">The one-based caret position within <paramref name="syntax"/>.</param>
    /// <returns>The error, carrying <see cref="RetCode.E_INVALID_ARGUMENT"/>.</returns>
    /// <remarks>
    /// A DELIBERATE LEGACY RESTRICTION RATHER THAN A GAP, and the reason it is enforced at the opening
    /// parenthesis [:L1306] is that the parenthesis is what makes the reference a FUNCTION rather than a
    /// variable. It takes no message arguments - the text is fixed - which is why this overload has one
    /// fewer parameter than the other two. Preserved because contract C-04 must refuse `@name(...)` for
    /// the same reason the oracle does, and there is nothing across a boundary that would make it any
    /// more expressible.
    /// </remarks>
    public static ExpressionParseError RejectContextFunctionMacro(string? syntax, long caretPosition)
    {
        return ParseErrorFormatter.CreateParseError(
            ExpressionErrorSite.FunctionMacroContextReferenceUnsupported,
            syntax,
            caretPosition);
    }

    /// <summary>
    /// Builds THE NARROWING'S DEFINED ERROR - the error a cross-DataWindow reference gets when it
    /// reaches outside the session that must contain it.
    /// </summary>
    /// <param name="handle">
    /// The unreachable handle. NAMED IN THE MESSAGE; that is part of the contract.
    /// </param>
    /// <param name="sessionId">The session the reference was resolved in.</param>
    /// <param name="status">
    /// Which kind of unreachable - <see cref="CrossDataWindowStatus.BlockedHandleNotCoResident"/> or
    /// <see cref="CrossDataWindowStatus.BlockedSessionClosed"/>.
    /// </param>
    /// <returns>
    /// An error whose <see cref="ExpressionParseError.Category"/> is
    /// <see cref="ExpressionErrorCategory.ForeignReferenceBlocked"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="status"/> is not one of the two BLOCKED values. Guarded rather than defaulted: a
    /// non-blocked status arriving here would produce an error claiming a narrowing that did not happen,
    /// and a silently mislabelled error is worse than a loud programming fault.
    /// </exception>
    /// <remarks>
    /// <para>
    /// IT IS CONSTRUCTED RATHER THAN FETCHED FROM THE CATALOGUE, AND IT HAS TO BE. Every one of the 28
    /// catalogued sites is a transcription of a legacy <c>MessageBox</c>; this one HAS NO LEGACY LOCATOR
    /// because the legacy cannot produce it - in-process the pointer at [:L82] always dereferences. The
    /// vocabulary the catalogue exposes for exactly this is
    /// <see cref="ExpressionErrorCategory.ForeignReferenceBlocked"/>, which is documented there as having
    /// no oracle line. <see cref="ExpressionParseError.Site"/> is therefore
    /// <see cref="ExpressionErrorSite.Unspecified"/> and NOT a borrowed line number, so nothing can
    /// mistake this for migrated text.
    /// </para>
    /// <para>
    /// THE MESSAGE IS IN ENGLISH, DELIBERATELY, AND THAT IS THE OPPOSITE OF THE RULE FOR THE OTHER 28.
    /// Those are hardcoded Chinese and are reproduced verbatim, untranslated, because changing them
    /// would change observable output. This one has no oracle counterpart at all, so writing it in
    /// Chinese would make an authored string indistinguishable from a transcribed one in a
    /// characterization diff - the one place the distinction matters most. It is still marked
    /// <see cref="ExpressionParseError.Localized"/> false, because it does not route through the
    /// localization layer either.
    /// </para>
    /// <para>
    /// IT CARRIES NO CARET. The failure is not a parse failure - the expression is syntactically fine
    /// and the topology is not - so the family is a plain message and no position is invented. It is
    /// also, by construction, incapable of carrying a value: it is an error and nothing else.
    /// </para>
    /// </remarks>
    public static ExpressionParseError CreateBlockedError(
        DataWindowHandle handle,
        string? sessionId,
        CrossDataWindowStatus status)
    {
        string template = status switch
        {
            CrossDataWindowStatus.BlockedHandleNotCoResident => BlockedNotCoResidentTemplate,
            CrossDataWindowStatus.BlockedSessionClosed => BlockedSessionClosedTemplate,
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Only "
                    + nameof(CrossDataWindowStatus.BlockedHandleNotCoResident)
                    + " and "
                    + nameof(CrossDataWindowStatus.BlockedSessionClosed)
                    + " are BLOCKED outcomes. Every other status is either a success or one of the "
                    + "oracle's own failures, and must not be reported as this refactor's narrowing."),
        };

        ImmutableArray<string> arguments = [handle.Value, sessionId ?? string.Empty];

        return new ExpressionParseError
        {
            // No legacy line exists for this error, and none is borrowed.
            Site = ExpressionErrorSite.Unspecified,
            Family = ExpressionErrorFamily.PlainMessage,
            Category = ExpressionErrorCategory.ForeignReferenceBlocked,

            // The severity every failure in this object uses, so a consumer's handling is uniform.
            Severity = ExpressionErrorSeverity.StopSign,
            Title = ExpressionErrorCatalog.LegacyTitle,
            Text = Formatting.Sprintf(template, [.. arguments]),

            // Not localized, exactly like the 28 oracle messages - though for a different reason: this
            // text has no oracle counterpart to preserve.
            Localized = false,
            LocalizationCategory = 0,
            FormatTemplate = template,
            FormatArguments = arguments,
            ReturnCode = status == CrossDataWindowStatus.BlockedSessionClosed
                ? RetCode.E_NOT_EXISTS
                : RetCode.E_INVALID_HANDLE,

            // A plain message carries no expression, no position, no convention and no marker.
            Expression = null,
            CaretPosition = null,
            CaretConvention = CaretPositionConvention.Unspecified,
            RenderedMarker = null,
        };
    }

    /// <summary>
    /// The BLOCKED message for a handle that is not co-resident. Authored, not transcribed - see
    /// <see cref="CreateBlockedError"/>.
    /// </summary>
    private const string BlockedNotCoResidentTemplate =
        "Cross-DataWindow expression reference BLOCKED: DataWindow handle [{1}] is not co-resident in "
        + "expression session [{2}]. A foreign or context reference resolves only while both "
        + "DataWindows are registered in the same expression session inside one DataServices instance; "
        + "a reference spanning sessions or service instances is refused rather than answered with a "
        + "substituted or stale value.";

    /// <summary>
    /// The BLOCKED message for a session that has been closed or has expired. Authored, not
    /// transcribed - see <see cref="CreateBlockedError"/>.
    /// </summary>
    private const string BlockedSessionClosedTemplate =
        "Cross-DataWindow expression reference BLOCKED: expression session [{2}] is closed, so DataWindow "
        + "handle [{1}] is no longer resolvable. Closing a session releases every registration it "
        + "scoped; the reference is refused rather than answered from state left over from before the "
        + "close.";

    /// <summary>
    /// The message carried when a reached service's own call threw - see
    /// <see cref="CrossDataWindowStatus.HostFaulted"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FIXED TEXT WITH NO EXCEPTION DETAIL IN IT (CWE-209). The three substitutions are the handle, the
    /// session identifier and a correlation identifier this service minted - all three values the caller
    /// already holds or was just given. The exception's type name and message are deliberately absent:
    /// they are host-internal and can carry a file path, a connection string fragment, a SQL statement, a
    /// configuration key or any other text the throwing code chose, and an authenticated caller is not
    /// thereby entitled to it.
    /// </para>
    /// <para>
    /// THE DIAGNOSTIC IS NOT DISCARDED, IT IS NARROWED AND MOVED. <see cref="Faulted"/> records the fault's
    /// TYPE CHAIN server-side against the same correlation identifier this message carries, so an operator
    /// joins the two in one lookup while neither channel carries the fault's own text. The exception object
    /// itself reaches neither: a host holds this expression's variable values, so its message is caller data
    /// wherever it came from, and a log record is a different trust domain from this process.
    /// </para>
    /// </remarks>
    /// <summary>The separator between links of a described fault chain, outermost towards innermost.</summary>
    /// <remarks>
    /// FORWARDED RATHER THAN DECLARED. This walk was written twice - here and in Persistence's status
    /// interceptor - with the same three values, while other sites in both services still attached the
    /// exception object itself. Three independent copies of one security control drift; the walk therefore
    /// moved to <see cref="ExceptionChain"/> and these names forward to it.
    /// </remarks>
    private const string FaultChainSeparator = ExceptionChain.Separator;

    /// <summary>The marker appended when a fault chain is deeper than the bound below.</summary>
    private const string FaultChainTruncationMarker = ExceptionChain.TruncationMarker;

    /// <summary>
    /// How many links of a fault chain are described before truncation.
    /// </summary>
    /// <remarks>
    /// BOUNDED BECAUSE A CHAIN CAN BE CYCLIC. Nothing prevents an exception from being its own ancestor
    /// through aggregation, and an unbounded walk over one would build a string until the process ran out of
    /// memory - while handling a fault, which is the worst moment for a second one.
    /// </remarks>
    private const int MaximumDescribedFaultDepth = ExceptionChain.MaximumDepth;

    private const string HostFaultedTemplate =
        "Cross-DataWindow expression reference FAILED: the expression service behind DataWindow handle "
        + "[{1}] in expression session [{2}] raised an internal error. The handle resolved and the "
        + "service was reached, so this is NOT the cross-session narrowing; the fault is reported "
        + "instead of being allowed to escape and abort calculations sharing the same stream. The fault "
        + "detail is recorded server-side under correlation id [{3}]; quote that identifier to have it "
        + "looked up.";

    /// <summary>
    /// Resolves a handle to a co-resident service, producing the DEFINED ERROR for each way that can
    /// fail. The single point at which the narrowing is enforced.
    /// </summary>
    /// <param name="handle">The handle. Already known to be non-empty.</param>
    /// <param name="index">The index to echo on a successful resolution; zero when none applies.</param>
    /// <returns>The resolution.</returns>
    /// <remarks>
    /// THE ORDER OF THE TWO BLOCKED CHECKS MATTERS FOR THE DIAGNOSTIC, NOT FOR THE VERDICT. A closed
    /// session has already dropped its registrations, so the lookup would miss either way and the
    /// reference is BLOCKED either way; testing closure FIRST is what makes the message say "the session
    /// closed" rather than "that DataWindow was never here", which are very different things for whoever
    /// has to act on it.
    /// </remarks>
    private ForeignVariableResolution ResolveHandle(DataWindowHandle handle, int index)
    {
        bool isOpen;
        HostRegistration? registration;

        lock (_gate)
        {
            isOpen = _isOpen;
            _ = _hosts.TryGetValue(handle.Value, out registration);
        }

        if (!isOpen)
        {
            return Blocked(handle, CrossDataWindowStatus.BlockedSessionClosed);
        }

        if (registration is null)
        {
            return Blocked(handle, CrossDataWindowStatus.BlockedHandleNotCoResident);
        }

        return new ForeignVariableResolution
        {
            Status = CrossDataWindowStatus.Resolved,
            Handle = handle,
            Index = index,
            Host = registration.Host,
            Error = null,
            ReturnCode = RetCode.OK,
        };
    }

    /// <summary>
    /// A BLOCKED resolution - the narrowing's outcome, carrying no service and therefore no way to
    /// dereference anything.
    /// </summary>
    /// <param name="handle">The unreachable handle.</param>
    /// <param name="status">Which kind of unreachable.</param>
    /// <returns>The resolution.</returns>
    private ForeignVariableResolution Blocked(DataWindowHandle handle, CrossDataWindowStatus status)
    {
        ExpressionParseError error = CreateBlockedError(handle, SessionId, status);

        return new ForeignVariableResolution
        {
            Status = status,
            Handle = handle,
            Index = 0,
            Host = null,
            Error = error,
            ReturnCode = error.ReturnCode ?? RetCode.E_INVALID_HANDLE,
        };
    }

    /// <summary>
    /// A malformed-reference resolution: a return code and NO message, which is the shape the oracle's
    /// own two argument guards have [n_cst_dwsvc_columnexp.sru:L2119-L2120].
    /// </summary>
    /// <param name="handle">The handle as supplied, echoed back.</param>
    /// <param name="returnCode">
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> for a missing name or a non-positive stored index;
    /// <see cref="RetCode.E_INVALID_OBJECT"/> for a missing handle.
    /// </param>
    /// <returns>The resolution.</returns>
    private static ForeignVariableResolution Malformed(DataWindowHandle handle, long returnCode)
    {
        return new ForeignVariableResolution
        {
            Status = CrossDataWindowStatus.InvalidReference,
            Handle = handle,
            Index = 0,
            Host = null,
            Error = null,
            ReturnCode = returnCode,
        };
    }

    /// <summary>
    /// A host-faulted resolution: the handle resolved, the call threw, and the fault is reported with
    /// FIXED SAFE TEXT while the exception itself is recorded server-side under a correlation id.
    /// </summary>
    /// <param name="handle">The handle that did resolve.</param>
    /// <param name="exception">The exception the host raised.</param>
    /// <returns>The resolution.</returns>
    /// <remarks>
    /// <para>
    /// WHY THE EXCEPTION DOES NOT TRAVEL (CWE-209). This error reaches an authenticated caller through
    /// two wire fields - the rendered <c>text</c> and the structured <c>format_arguments</c> - and the
    /// host that threw is arbitrary application code reached across a session boundary. Its exception
    /// message can contain a file path, a connection string, a SQL fragment, a configuration key or a
    /// stack-shaped type name, none of which the caller asked for and none of which this service can
    /// vet. Carrying it "rather than swallowing it" puts the fidelity of a diagnostic ahead of the
    /// boundary, and the two are not actually in tension: the diagnostic is kept, just not HERE.
    /// </para>
    /// <para>
    /// THE CORRELATION ID IS DETERMINISTIC, WHICH IS WHY IT CAN BE ON THE WIRE AT ALL. It is the session
    /// identifier the caller chose plus a monotonic per-session ordinal - no GUID, no clock read - so a
    /// paired characterization recording compares it directly instead of masking it. The same identifier
    /// goes into the log record and into the payload, and that is the only thing joining them.
    /// </para>
    /// <para>
    /// BOTH FIELDS ARE SANITISED, NOT JUST THE RENDERED ONE. <c>FormatArguments</c> is what a client
    /// re-renders from, so leaving the type and message there while cleaning the text moves
    /// the leak rather than closing it.
    /// </para>
    /// </remarks>
    private ForeignVariableResolution Faulted(DataWindowHandle handle, Exception exception)
    {
        string faultId = NextFaultId();

        // THE EXCEPTION OBJECT IS NOT PASSED TO THE LOGGER, AND THE REASONING THAT WOULD PUT IT HERE IS
        // ONE STEP SHORT. Keeping the detail out of the wire and in the log is right about the wire and
        // wrong about the log: the host that threw is arbitrary code holding this expression's variable
        // VALUES, so its message can carry a caller's data, an expression fragment, a path or a
        // configuration key - and an attached exception is rendered in full by every provider, message
        // chain and stack together. A log record is a different trust domain from this process, and the
        // rule the estate applies to every other arbitrary fault applies here too.
        //
        // WHAT IS RECORDED INSTEAD IS ALLOWLISTED AND STILL LOCATES THE FAULT. The exception's TYPE CHAIN
        // names which failure occurred and where it came from - every name in it belongs to this codebase,
        // the framework or a package, and none can carry a value a caller supplied. Together with the
        // deterministic correlation id, the handle and the session it identifies the occurrence precisely;
        // what it withholds is the content the fault happened to be holding.
        _logger?.LogError(
            "Cross-DataWindow expression host faulted. Correlation id {FaultId}, DataWindow handle "
                + "{Handle}, expression session {SessionId}, fault types {FaultTypes}. The caller received "
                + "fixed text carrying this correlation id and no exception detail.",
            faultId,
            LogSafeText.Render(handle.Value),
            LogSafeText.Render(SessionId),
            DescribeExceptionTypes(exception));

        ImmutableArray<string> arguments =
        [
            handle.Value,
            SessionId,
            faultId,
        ];

        ExpressionParseError error = new()
        {
            Site = ExpressionErrorSite.Unspecified,
            Family = ExpressionErrorFamily.PlainMessage,

            // NOT the BLOCKED category. The service was reachable, so labelling this as the narrowing
            // would send a reader looking for a topology problem that does not exist.
            Category = ExpressionErrorCategory.Expression,
            Severity = ExpressionErrorSeverity.StopSign,
            Title = ExpressionErrorCatalog.LegacyTitle,
            Text = Formatting.Sprintf(HostFaultedTemplate, [.. arguments]),
            Localized = false,
            LocalizationCategory = 0,
            FormatTemplate = HostFaultedTemplate,
            FormatArguments = arguments,
            ReturnCode = RetCode.E_INTERNAL_ERROR,
            Expression = null,
            CaretPosition = null,
            CaretConvention = CaretPositionConvention.Unspecified,
            RenderedMarker = null,
        };

        return new ForeignVariableResolution
        {
            Status = CrossDataWindowStatus.HostFaulted,
            Handle = handle,
            Index = 0,
            Host = null,
            Error = error,
            ReturnCode = RetCode.E_INTERNAL_ERROR,
        };
    }

    /// <summary>
    /// Mints the next host-fault correlation identifier for this session.
    /// </summary>
    /// <returns>
    /// <see cref="SessionId"/>, then <c>"/fault/"</c>, then a monotonic per-session ordinal starting at
    /// one - for example <c>"session-7/fault/1"</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE SHAPE IS PART OF THE CONTRACT, because the identifier travels on the wire and an operator has
    /// to be able to grep for it. It mirrors the allocated-handle shape (<c>sessionId/ordinal</c>) with an
    /// interposed segment, so a fault id can never be mistaken for a handle even though both are opaque.
    /// </para>
    /// <para>
    /// <see cref="Interlocked.Increment(ref long)"/> RATHER THAN THE SESSION LOCK. A fault can be minted
    /// from a resolution path that already holds no lock and from one that does, so taking
    /// <c>_gate</c> here would make the ordering of the two a correctness question. An interlocked
    /// increment is sufficient - uniqueness is all that is required of the ordinal, not any relationship
    /// to the handle sequence.
    /// </para>
    /// <para>
    /// NO CLOCK AND NO RANDOMNESS, deliberately. See <c>_faultOrdinal</c>: this value is compared byte
    /// for byte by a paired characterization recording, so it has to be reproducible from the same
    /// sequence of calls under the same caller-chosen session identifier.
    /// </para>
    /// </remarks>
    private string NextFaultId()
    {
        long ordinal = Interlocked.Increment(ref _faultOrdinal);

        return SessionId + "/fault/" + ordinal.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Names the types in an exception chain, outermost first, without reading any message.
    /// </summary>
    /// <param name="error">The fault to describe.</param>
    /// <returns>The namespace-qualified type names joined outermost-first.</returns>
    /// <remarks>
    /// A TYPE NAME IS ALLOWLISTED CONTENT AND A MESSAGE IS NOT. Every name this produces belongs to this
    /// codebase, the framework or a package, so none of them can carry an expression fragment, a variable
    /// value, a path or a configuration key - which is exactly what an arbitrary host's message can. The
    /// chain is walked because a host fault is habitually wrapped and the type that actually failed is the
    /// innermost one, and the walk is BOUNDED because nothing prevents a chain from being cyclic through
    /// aggregation; a truncation marker is appended so a shortened chain is never mistaken for a complete
    /// one.
    /// </remarks>
    private static string DescribeExceptionTypes(Exception error) =>
        ExceptionChain.DescribeTypes(error);


    /// <summary>
    /// Projects a failed resolution onto a value result. THE VALUE IS ALWAYS
    /// <see cref="string.Empty"/> - this is the single funnel through which every non-success reaches a
    /// caller, which is what makes "a BLOCKED reference never carries a value" a property of the type
    /// rather than a habit of its call sites.
    /// </summary>
    /// <param name="resolution">The failed resolution.</param>
    /// <returns>A value result carrying no value.</returns>
    private static ExpressionValueResult NoValue(ForeignVariableResolution resolution)
    {
        return new ExpressionValueResult
        {
            Status = resolution.Status,
            Value = string.Empty,
            Handle = resolution.Handle,
            Error = resolution.Error,
            ReturnCode = resolution.ReturnCode,
        };
    }

    /// <summary>
    /// Wraps an answer a reached service actually gave, applying the oracle's own success test.
    /// </summary>
    /// <param name="handle">The handle that resolved.</param>
    /// <param name="value">The service's answer. A null is normalised to the empty string.</param>
    /// <returns>
    /// A resolved result. <see cref="RetCode.OK"/> when the answer is non-empty and
    /// <see cref="RetCode.FAILED"/> when it is empty, because <c>if sVal = "" then return ""</c>
    /// [n_cst_dwsvc_columnexp.sru:L2201] makes an empty answer a failure that aborts the whole
    /// preprocessing pass. No error accompanies it: the oracle raises none at that point either - the
    /// service it called has already reported whatever went wrong.
    /// </returns>
    private static ExpressionValueResult Answered(DataWindowHandle handle, string? value)
    {
        string answer = value ?? string.Empty;

        return new ExpressionValueResult
        {
            Status = CrossDataWindowStatus.Resolved,
            Value = answer,
            Handle = handle,
            Error = null,
            ReturnCode = answer.Length > 0 ? RetCode.OK : RetCode.FAILED,
        };
    }

    /// <summary>
    /// Adds a registration and its freshly reserved stack. The caller holds <see cref="_gate"/>, and has
    /// already established that the session is open and the handle is free.
    /// </summary>
    /// <param name="handle">The handle to register under.</param>
    /// <param name="host">The service being registered.</param>
    private void AddRegistrationUnderLock(DataWindowHandle handle, IExpressionServiceHost host)
    {
        _hosts[handle.Value] = new HostRegistration(
            host,
            new ExpressionCalcStack(CalcStackInitialCapacity));
        _order.Add(handle);
        _lastAccessedAt = _timeProvider.GetUtcNow();
    }

    /// <summary>
    /// One co-resident DataWindow: the service to call, and the calculation stack that belongs to it.
    /// </summary>
    /// <param name="Host">The registered expression service.</param>
    /// <param name="CalcStack">That service's own calculation and recursion stack.</param>
    private sealed record HostRegistration(IExpressionServiceHost Host, ExpressionCalcStack CalcStack);
}


/// <summary>
/// The outcome of asking the registry to open a session - the in-process shape of
/// <c>dataservices.v1.OpenExpressionSessionResponse</c>.
/// </summary>
/// <remarks>
/// A RESULT RATHER THAN AN EXCEPTION, because none of these outcomes is exceptional: an admission
/// refusal and a duplicate identifier are both ordinary answers a caller acts on, and the whole
/// framework this ports from communicates failure through return codes rather than by throwing.
/// </remarks>
/// <summary>
/// The outcome of one atomic attempt to take an expression session into use.
/// </summary>
/// <remarks>
/// Three outcomes rather than a boolean, because the two refusals have different consequences for the
/// registry: a session that was merely closed has already been accounted for, whereas one this attempt
/// found EXPIRED has just been closed by the attempt itself and its registry slot still needs releasing.
/// Collapsing them would either leak a slot or double-release one.
/// </remarks>
public enum ExpressionSessionAcquisition
{
    /// <summary>
    /// The session was open and unexpired, and its activity stamp has been advanced. The only outcome on
    /// which a caller may use the session.
    /// </summary>
    Acquired = 0,

    /// <summary>The session had already been closed. The attempt changed nothing.</summary>
    Closed = 1,

    /// <summary>
    /// The session had gone idle past its timeout. It was CLOSED by the attempt - registrations dropped
    /// and stacks cleared - under the same lock that decided it.
    /// </summary>
    Expired = 2,
}

/// <summary>
/// How the expression trace sink has been behaving, reported independently of any calculation result.
/// </summary>
/// <param name="Attempted">
/// How many records have been handed to a sink. Zero when no sink is registered or tracing is off.
/// </param>
/// <param name="Delivered">How many the sink accepted without throwing.</param>
/// <param name="Failed">
/// How many the sink threw on, and therefore HOW MANY DIAGNOSTIC RECORDS WERE LOST. A non-zero value
/// means the trace is incomplete even though every calculation succeeded.
/// </param>
/// <param name="LastFailureType">
/// The type name of the most recent sink failure, or <see langword="null"/> when there has been none.
/// THE TYPE ONLY: a sink's exception message is arbitrary content that can carry a path, a URL or a
/// credential, and this report is readable by anything holding the session (C-F).
/// </param>
/// <remarks>
/// A sink failure never changes a calculation - the trace is fire-and-forget and must not break the thing
/// it observes - so it needs somewhere else to be visible. This is that somewhere. Reading
/// <paramref name="Failed"/> is how a caller learns the diagnostic channel has stopped working, which
/// nothing else in this service reports.
/// </remarks>
public sealed record ExpressionTraceDeliveryReport(
    long Attempted,
    long Delivered,
    long Failed,
    string? LastFailureType);

public sealed record ExpressionSessionOpenResult(ExpressionSession? Session, long ReturnCode)
{
    /// <summary>
    /// Whether a session was opened. Tests <see cref="Session"/> as well as
    /// <see cref="ReturnCode"/>, so the two cannot disagree in a caller's favour.
    /// </summary>
    public bool IsOpened => Session is not null && ReturnCode == RetCode.OK;
}

/// <summary>
/// The store of live <see cref="ExpressionSession"/> instances, keyed by the correlation identifier the
/// contract carries - the server side of <c>OpenExpressionSession</c> and
/// <c>CloseExpressionSession</c>.
/// </summary>
/// <remarks>
/// <para>
/// WHY IT EXISTS. A session is server-held state correlated by an identifier, and anything server-held
/// and correlated needs an owner that can enumerate it: to enforce the concurrent-session ceiling, to
/// expire a session a client never closed, and - the reason that matters most here - to guarantee that
/// A HANDLE MINTED BY ONE SESSION IS NEVER RESOLVABLE THROUGH ANOTHER. That guarantee is what makes the
/// narrowing enforceable rather than merely documented, and it is why the cross-session BLOCKED path is
/// reachable in a test with two sessions and no network at all.
/// </para>
/// <para>
/// BOTH LIMITS COME FROM CONFIGURATION, AND NEITHER IS A LEGACY NUMBER.
/// <c>DataServices:Sessions:ExpressionSession:IdleTimeout</c> and <c>:MaxConcurrentSessions</c> exist
/// BECAUSE OF THE BOUNDARY: in the legacy the cross-DataWindow references were pointers inside an object
/// whose lifetime was the hosting control's, so there was no session to bound and no ceiling to set.
/// They are starting points for a deployment rather than reproductions of anything, and the
/// configuration document says so.
/// </para>
/// <para>
/// THREAD-SAFE. Sessions are opened, looked up, expired and closed concurrently by independent gRPC
/// calls, so the store is a concurrent dictionary and every mutation is an atomic operation on it. The
/// ceiling is enforced with a compare-and-retry loop rather than a lock, so an open never blocks a
/// lookup.
/// </para>
/// <para>
/// NOT <see cref="IDisposable"/>, for the reason given in the file header: release is the explicit
/// close the contract publishes. <see cref="CloseAll"/> is the shutdown path.
/// </para>
/// </remarks>
public sealed class ExpressionSessionRegistry
{
    /// <summary>
    /// Identifier to session. Ordinal keys - a session identifier is an opaque correlation value.
    /// </summary>
    private readonly ConcurrentDictionary<string, ExpressionSession> _sessions =
        new(StringComparer.Ordinal);

    /// <summary>The engine settings every session it opens is built with.</summary>
    private readonly ColumnExpressionOptions _columnExpression;

    /// <summary>The lifetime settings every session it opens is built with.</summary>
    private readonly SessionLifetimeOptions _lifetime;

    /// <summary>
    /// The clock seam, shared with every session so that one substitution covers them all.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The trace seam, shared with every session.</summary>
    private readonly IExpressionTraceSink? _traceSink;

    /// <summary>
    /// The host-fault log sink, shared with every session it opens.
    /// </summary>
    /// <remarks>
    /// Shared rather than per-session so that one registration covers every session's faults, and so the
    /// category a deployment sees is stable across sessions. See <see cref="ExpressionSession.Faulted"/>
    /// for what is recorded through it and what is deliberately kept off the wire.
    /// </remarks>
    private readonly ILogger? _logger;

    /// <summary>
    /// Who a session is attributed to at open, and who a later call is compared against.
    /// </summary>
    private readonly SessionPrincipalResolver _principals;

    /// <summary>
    /// How many sessions are open, tracked separately so the ceiling needs no enumeration.
    /// </summary>
    private int _openCount;

    /// <summary>
    /// Creates the registry from bound options - the constructor dependency injection resolves.
    /// </summary>
    /// <param name="options">The service options. Only two of its groups are read.</param>
    /// <param name="timeProvider">The clock. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="traceSink">The trace seam. When <see langword="null"/>, nothing is emitted.</param>
    /// <param name="logger">
    /// Where every session's host-fault detail is recorded. Optional and trailing, so the container
    /// supplies it when one is configured and a unit test need not.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> is <see langword="null"/>, or its value is.
    /// </exception>
    public ExpressionSessionRegistry(
        IOptions<DataServicesOptions> options,
        TimeProvider? timeProvider = null,
        IExpressionTraceSink? traceSink = null,
        ILogger<ExpressionSessionRegistry>? logger = null,
        SessionPrincipalResolver? principals = null)
        : this(GetValue(options), timeProvider, traceSink, logger, principals)
    {
    }

    /// <summary>
    /// Creates the registry from an options instance directly, for a caller that already holds one.
    /// </summary>
    /// <param name="options">The service options.</param>
    /// <param name="timeProvider">The clock. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="traceSink">The trace seam.</param>
    /// <param name="logger">The logger, or <see langword="null"/> to record nothing.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// PRESENT SO EVERY PATH IS REACHABLE WITHOUT A HOST. The coverage gate is measured per service, and
    /// the paths that most need covering here - the cross-session BLOCKED refusal, the closed-session
    /// refusal, and idle expiry - are precisely the ones that are awkward to reach through a container.
    /// This overload plus the injected clock make all three reachable from a plain unit test.
    /// </remarks>
    public ExpressionSessionRegistry(
        DataServicesOptions options,
        TimeProvider? timeProvider = null,
        IExpressionTraceSink? traceSink = null,
        ILogger? logger = null,
        SessionPrincipalResolver? principals = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _columnExpression = options.ColumnExpression ?? new ColumnExpressionOptions();
        _lifetime = options.Sessions?.ExpressionSession ?? new SessionLifetimeOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _traceSink = traceSink;
        _logger = logger;
        _principals = principals ?? new SessionPrincipalResolver();
    }

    /// <summary>
    /// The idle timeout applied to every session opened here, from
    /// <c>DataServices:Sessions:ExpressionSession:IdleTimeout</c>.
    /// </summary>
    public TimeSpan IdleTimeout => _lifetime.IdleTimeout;

    /// <summary>
    /// The concurrent-session ceiling, from
    /// <c>DataServices:Sessions:ExpressionSession:MaxConcurrentSessions</c>.
    /// </summary>
    /// <remarks>
    /// AN ADMISSION BOUND AND NOT A TARGET. It decides when a further session is refused outright - a
    /// decision the legacy never had to make, because its sessions were objects in the caller's own
    /// address space. A non-positive value refuses every session, which is the reading the configuration
    /// document already states and which the options validator refuses to allow into a deployment.
    /// </remarks>
    public int MaxConcurrentSessions => _lifetime.MaxConcurrentSessions;

    /// <summary>How many sessions are currently open.</summary>
    public int Count => Volatile.Read(ref _openCount);

    /// <summary>
    /// Opens a session under a generated identifier.
    /// </summary>
    /// <returns>The result. See <see cref="ExpressionSessionOpenResult"/>.</returns>
    /// <remarks>
    /// The identifier is a GUID in its compact form. That is a NON-DETERMINISTIC value, so a
    /// characterization run supplies its own identifier through
    /// <see cref="Open(string)"/> instead of masking this one - the seam is the parameter, which is
    /// cheaper and more exact than a generator abstraction.
    /// </remarks>
    public ExpressionSessionOpenResult Open() => Open(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Opens a session under a caller-supplied identifier.
    /// </summary>
    /// <param name="sessionId">
    /// The correlation identifier. Must be non-blank and not already in use.
    /// </param>
    /// <returns>The result. See <see cref="ExpressionSessionOpenResult"/>.</returns>
    /// <remarks>
    /// <para>
    /// A DUPLICATE IDENTIFIER IS REFUSED RATHER THAN REUSED. Handing back an existing session would give
    /// two unrelated callers one scope, and co-residency IS the security-relevant property here: they
    /// would each be able to resolve the other's handles, which is exactly the cross-boundary reference
    /// this design exists to prevent.
    /// </para>
    /// <para>
    /// AN EXPIRED SESSION IS SWEPT BEFORE THE CEILING IS TESTED, so a client that abandons sessions
    /// cannot lock a service out with state nobody is using. The sweep runs only when the ceiling would
    /// otherwise refuse, which keeps the ordinary open path free of it.
    /// </para>
    /// </remarks>
    public ExpressionSessionOpenResult Open(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new ExpressionSessionOpenResult(null, RetCode.E_INVALID_ARGUMENT);
        }

        if (_sessions.ContainsKey(sessionId))
        {
            return new ExpressionSessionOpenResult(null, RetCode.FAILED);
        }

        int ceiling = MaxConcurrentSessions;
        if (ceiling <= 0)
        {
            return new ExpressionSessionOpenResult(null, RetCode.E_BUSY);
        }

        // Reserve a slot first, so two concurrent opens cannot both pass the ceiling. Retried rather
        // than locked: the loop only spins while another open is committing.
        while (true)
        {
            int current = Volatile.Read(ref _openCount);
            if (current >= ceiling)
            {
                // Reclaim anything abandoned before refusing - and only then, so the ordinary path never
                // pays for the sweep.
                if (SweepExpired() == 0)
                {
                    return new ExpressionSessionOpenResult(null, RetCode.E_BUSY);
                }

                continue;
            }

            if (Interlocked.CompareExchange(ref _openCount, current + 1, current) == current)
            {
                break;
            }
        }

        // ATTRIBUTED AT OPEN, because there is no later moment at which the opening caller is still
        // knowable. Resolution and close both compare against this value, so the stamp and the comparison
        // read the same source and cannot disagree about who a caller is.
        ExpressionSession session = new(
            sessionId,
            _lifetime,
            _columnExpression,
            _timeProvider,
            _traceSink,
            _logger,
            _principals.Resolve());

        if (!_sessions.TryAdd(sessionId, session))
        {
            // Lost a race with another open using the same identifier. Give the reserved slot back
            // rather than leaking it, and refuse exactly as the pre-check would have.
            Interlocked.Decrement(ref _openCount);
            return new ExpressionSessionOpenResult(null, RetCode.FAILED);
        }

        return new ExpressionSessionOpenResult(session, RetCode.OK);
    }

    /// <summary>
    /// Looks a session up and marks it in use.
    /// </summary>
    /// <param name="sessionId">The correlation identifier.</param>
    /// <param name="session">The session on success; <see langword="null"/> otherwise.</param>
    /// <returns>
    /// <see langword="true"/> when an OPEN, UNEXPIRED session was found.
    /// </returns>
    /// <remarks>
    /// <para>
    /// AN EXPIRED SESSION IS CLOSED HERE RATHER THAN RETURNED, which is the property that makes idle
    /// expiry safe: a handle whose session has gone stale must be BLOCKED, and returning the session so
    /// the caller could resolve through it would answer from exactly the state the expiry was supposed to
    /// release. A successful lookup touches the session, so a session in active use never expires
    /// underneath the calculation using it.
    /// </para>
    /// <para>
    /// THE VALIDATION IS ONE ATOMIC STEP, not three. <see cref="ExpressionSession.Acquire"/> tests open
    /// state, tests expiry and records activity under a SINGLE acquisition of the session's gate. Asking
    /// separately took the gate three times, and a close landing in either interval returned a CLOSED
    /// session as a successful lookup - which on this session type means the caller then resolves handles
    /// against state the close has already emptied, instead of being told the session is gone.
    /// </para>
    /// </remarks>
    public bool TryGet(string? sessionId, out ExpressionSession? session)
    {
        session = null;

        if (string.IsNullOrEmpty(sessionId))
        {
            return false;
        }

        if (!_sessions.TryGetValue(sessionId, out ExpressionSession? found))
        {
            return false;
        }

        if (!_principals.IsCaller(found.Owner))
        {
            // Not this caller's session, and answered exactly as an unknown identifier is - the same
            // `false`, so nothing distinguishes "not yours" from "no such session" and this member is not
            // an oracle for which sessions exist. WITHOUT touching it: the check sits ABOVE `Acquire`
            // because acquisition RECORDS ACTIVITY, so checking afterwards would let a leaked identifier
            // hold another caller's session open past its idle window while every call using it failed.
            return false;
        }

        if (found.Acquire() != ExpressionSessionAcquisition.Acquired)
        {
            // Closed, or expired-and-now-closed. The registry drops it and gives the slot back either
            // way; its close is idempotent, so doing so for an already-closed session is safe. Reached
            // through the UNCHECKED close because ownership is already established one line above, and
            // because this close is the registry's own housekeeping rather than a caller's request.
            CloseUnchecked(sessionId);
            return false;
        }

        session = found;

        return true;
    }

    /// <summary>
    /// Closes a session and releases every registration it scoped.
    /// </summary>
    /// <param name="sessionId">The correlation identifier.</param>
    /// <returns>
    /// <see langword="true"/> when this call closed an open session - <c>was_open</c> on the contract's
    /// close response. <see langword="false"/> when it was already closed or was never known, which
    /// makes the call IDEMPOTENT so a retried close cannot leak or double-count.
    /// </returns>
    /// <remarks>
    /// <b>OWNER-CHECKED, AND A FOREIGN CLOSE IS ANSWERED AS AN ALREADY-CLOSED ONE.</b> Closing is the more
    /// damaging half of what a leaked identifier enables: it releases every DataWindow host the session
    /// scoped, so every handle minted from it stops resolving and the owning caller's open calculation is
    /// BLOCKED mid-flight. That is a denial of service against another caller rather than a disclosure
    /// (CWE-862). The refusal is spelled as the idempotent <see langword="false"/> an unknown identifier
    /// answers, so it tells an unauthorised caller nothing, and the session is neither removed nor touched.
    /// </remarks>
    public bool Close(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return false;
        }

        if (_sessions.TryGetValue(sessionId, out ExpressionSession? existing)
            && !_principals.IsCaller(existing.Owner))
        {
            return false;
        }

        return CloseUnchecked(sessionId);
    }

    /// <summary>
    /// Closes a session without comparing its owner - the registry's own maintenance path.
    /// </summary>
    /// <param name="sessionId">The correlation identifier.</param>
    /// <returns><see langword="true"/> when this call closed an open session.</returns>
    /// <remarks>
    /// <para>
    /// <b>NAMED SO ITS ABSENCE OF A CHECK IS AUDITABLE.</b> Its three callers - the expired-session drop
    /// inside <see cref="TryGet"/>, the idle <see cref="SweepExpired"/> and the shutdown
    /// <see cref="CloseAll"/> - run on the SERVICE's behalf inside no request, so there is no caller to
    /// compare against.
    /// </para>
    /// <para>
    /// <b>CHECKING HERE WOULD CONVERT THE CEILING INTO A LEAK.</b> Outside a request the resolver reports
    /// the unattributed sentinel, so an ownership test would refuse every session opened by a real caller:
    /// the sweep would collect nothing, abandoned sessions would stay pinned for the life of the process,
    /// and the concurrent-session ceiling would stay permanently reached. That is the denial of service the
    /// ceiling exists to prevent, arriving by the one route that looks like extra safety.
    /// </para>
    /// </remarks>
    private bool CloseUnchecked(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return false;
        }

        if (!_sessions.TryRemove(sessionId, out ExpressionSession? removed))
        {
            return false;
        }

        Interlocked.Decrement(ref _openCount);

        // The session's own close is idempotent, so its answer is the authoritative `was_open`: it is
        // false when the session had already been closed directly rather than through the registry.
        return removed.Close();
    }

    /// <summary>
    /// Closes every session that has been idle longer than <see cref="IdleTimeout"/>.
    /// </summary>
    /// <returns>How many sessions were closed.</returns>
    /// <remarks>
    /// Callable from a background sweep as well as from <see cref="Open(string)"/>'s admission path. It
    /// enumerates a snapshot, so a session opened while it runs is simply not considered rather than
    /// disturbing the enumeration.
    /// </remarks>
    public int SweepExpired()
    {
        int closed = 0;

        foreach (KeyValuePair<string, ExpressionSession> entry in _sessions)
        {
            if (entry.Value.HasExpired() && CloseUnchecked(entry.Key))
            {
                closed++;
            }
        }

        return closed;
    }

    /// <summary>
    /// Closes every session - the shutdown path.
    /// </summary>
    /// <returns>How many sessions were closed.</returns>
    /// <remarks>
    /// Deterministic release of everything the registry scoped, which is the boundary equivalent of the
    /// legacy destroying its state when its engine goes away
    /// [n_cst_dwsvc_columnexp.sru:L2425]. After this call every handle any of these sessions minted
    /// resolves to <see cref="CrossDataWindowStatus.BlockedSessionClosed"/>.
    /// </remarks>
    public int CloseAll()
    {
        int closed = 0;

        foreach (KeyValuePair<string, ExpressionSession> entry in _sessions)
        {
            if (CloseUnchecked(entry.Key))
            {
                closed++;
            }
        }

        return closed;
    }

    /// <summary>
    /// Unwraps a bound options instance, failing loudly rather than substituting defaults.
    /// </summary>
    /// <param name="options">The options accessor.</param>
    /// <returns>The bound value.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> or its value is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// FAIL-FAST, MATCHING THE FRAMEWORK'S OWN POSTURE. A missing configuration binding is a structural
    /// fault, and the legacy's response to one is to terminate rather than to continue degraded
    /// [ws_objects/pfw.pbl.src/pfw.sra:L111-L144]. Quietly substituting defaults here would start a
    /// service whose session ceiling and idle timeout are not the ones its deployment specified.
    /// </remarks>
    private static DataServicesOptions GetValue(IOptions<DataServicesOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Value);

        return options.Value;
    }
}
