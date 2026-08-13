// =====================================================================================================
//  ColumnExpressionEngine.cs - the port of n_cst_dwsvc_columnexp.sru, the largest in-scope legacy
//  object at EXACTLY 2,435 lines, and the behavioural core behind cross-service contract C-04
//  (dataservices.v1.ColumnExpressionService).
//
//  ORACLE:        ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru   (2,435 lines)
//  SPECIFICATION: docs/n_cst_dwsvc_columnexp.md                                          (166 lines)
//  BASE:          ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru             (864 lines)
//  HOST:          ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru               (616 lines)
//
//  Every bare `:Lnnn` locator in this file is a line in the ORACLE. Locators into any other file are
//  written with that file's name. All of those files are READ-ONLY (constraint C-C): they are the only
//  specification that exists for this behaviour, and nothing here may be justified by anything else.
//
// -----------------------------------------------------------------------------------------------------
//  1. THE `@` SIGIL - A THIRD SIGIL THE PUBLISHED SPECIFICATION NEVER MENTIONS
// -----------------------------------------------------------------------------------------------------
//  docs/n_cst_dwsvc_columnexp.md documents exactly two sigils, `$` and `$$`. It never mentions `@`.
//  The oracle declares it as a first-class scanner constant - `constant string MACRO_CONTEXT = "@"`
//  [:L1253] - and documents the grammar only in its own internal comment block [:L1220-L1222]:
//
//        *上下文数据引用('@'开头)
//              @$$变量名  - 只支持动态展开        ("context macro - dynamic expansion only")
//              @列名      - 直接引用列名          ("context column - referenced directly")
//
//  The grammar the scanner actually implements [:L1292-L1300] is three-valued, not two:
//
//        @name    -> context COLUMN read.  isCtx=1 isMacro=0.  Resolved by reading the named column
//                    out of the CONTEXT DataWindow's row [:L2185].
//        @$name   -> context macro, STATIC. REJECTED outright at :L1417 with E_INVALID_ARGUMENT.
//        @$$name  -> context macro, DYNAMIC. Resolved against the context service at :L2182.
//
//  Resolution flows through the `(ctx_row, ctx_dwo, ctx_expsvc)` triple that every preprocess call
//  threads [:L2148 signature, consumed at :L2180-L2186]. `vardata.isctx` is that triple's selector; an
//  implementation built from the published specification alone would drop the whole branch and the
//  field would read as vestigial.
//
// -----------------------------------------------------------------------------------------------------
//  2. THE INTERNAL MODEL IS THREE ORTHOGONAL BITS; THE WIRE MODEL IS FIVE VALUES
// -----------------------------------------------------------------------------------------------------
//  Internally a reference carries three independent bits, and the scanner sets them separately:
//
//        isCtx     `@` seen            [:L1296]
//        isMacro   `$` seen            [:L1289 for `$`, :L1297 for `@$`]
//        dynamic   the sigil doubled   [:L1290 for `$$`, :L1299 for `@$$`]
//
//  C-04's `ExpansionMode` is the PROJECTION of those bits onto five wire values, and the projection is
//  not this file's to invent - `ExpansionSigils.ProjectVariableReference()` and
//  `ExpansionSigils.ProjectFunctionReference(dispatch)` in ExpressionVariableEnvironment.cs own it, and
//  `MacroInvoker.SigilsFor`/`TryClassify` own the function half. This file keeps all three bits, and
//  exposes `SigilsFor(VariableReference)` so the projection is reachable and testable at the boundary
//  without any consumer re-deriving it from the token text.
//
//  Note which combinations SURVIVE parsing. A static variable reference is substituted away and is
//  never added to `vars[]` - the append at :L1477-L1483 is the ELSE of `if bIsMacro and Not bDD`
//  [:L1415]. So every stored macro reference is dynamic, and `vars[]` after a successful parse holds
//  only bare context columns and dynamic references. That is exactly why the wire payload must carry
//  the UNEXPANDED source text alongside the reference list: the static bindings are no longer visible
//  in either.
//
// -----------------------------------------------------------------------------------------------------
//  3. `$` VERSUS `$$` - THE FILE'S HARDEST OBLIGATION (AAP G4)
// -----------------------------------------------------------------------------------------------------
//  STATIC (`$name`) substitutes the variable's VALUE EXPRESSION at the moment the expression is set, so
//  later mutation cannot reach it. DYNAMIC (`$$name`) keeps a REFERENCE resolved at calculation time,
//  so mutation propagates. The specification's worked pair is the acceptance test: with
//  `AddVarExp("本月读数","5")`, `AddVar("上月读数",0)` and `AddVarExp("num1","$上月读数 + $本月读数")`,
//  a later `SetVar("上月读数",1)` plus `Calc(1)` still yields 5 [md:L37-L54]; the identical sequence
//  written `"$$上月读数 + $本月读数"` yields 6 [md:L56-L73].
//
//  THE MECHANISM, and the reason the distinction "does not survive naive serialization": the static
//  branch REWRITES THE EXPRESSION IN PLACE [:L1435]. After the rewrite the variable's name is GONE from
//  the stored text, which is precisely why no later assignment can reach it - not a rule enforced
//  somewhere, but a consequence of the text no longer mentioning the variable. Transmitting the
//  rewritten text would therefore make a static binding indistinguishable from a literal, and
//  transmitting only the reference list would strip a dynamic binding of its resolution environment.
//  C-04 consequently carries ALL THREE of the unexpanded source expression, the bind-time reference
//  snapshot and the live variable environment, plus the expansion mode PER REFERENCE.
//
//  MODE IS PER REFERENCE, NEVER PER EXPRESSION. `$$a + $b` mixes both in one expression [md:L68] and
//  `$FormatPrice(n2, $精度)` nests a static reference inside a macro argument list [md:L117].
//
//  THE PARENTHESIS WRAP AT :L1434 IS MANDATORY AND IS THE SINGLE MOST CONSEQUENTIAL LINE IN THE STATIC
//  PATH. `sVarExp = "(" + sVarExp + ")"` runs before the rewrite. Omit it and a multi-term variable
//  substituted into a larger expression silently changes operator precedence: a variable whose value
//  expression is `1 + 2`, substituted into `$v * 3`, yields 9 with the wrap and 7 without. Nothing else
//  in the engine would report the difference. The same wrap is applied to every dynamic value [:L2213]
//  and to every macro return [:L2310], for the same reason.
//
//  THE REWRITE USES THE FIVE-ARGUMENT KEYWORD FORM of the framework's ReplaceAll
//  [:L1435, and again at :L2214/:L2311], whose whole-token semantics are what stop a short variable
//  name matching inside a longer one - `$a` must not match inside `$ab`.
//
// -----------------------------------------------------------------------------------------------------
//  4. THE EMPTY STRING IS AN ABORT SENTINEL, NOT AN EMPTY VALUE
// -----------------------------------------------------------------------------------------------------
//  Four independent sites establish it: `if sExp = "" then return false` [:L745] aborts a calculation
//  after preprocessing; `if sVal = "" then return ""` [:L2183] and [:L2186] propagate the abort out of
//  context resolution; and `case else return ""` [:L2358] is how an unknown column type refuses to
//  produce a value. `""` therefore means ABORT THIS CALCULATION, and it must be propagated upward
//  rather than written into a column. The sibling Validators/ValueToExpression.cs is under a hard
//  invariant never to return `""` precisely so that this channel stays unambiguous, and that invariant
//  is load-bearing for the calculation chain below.
//
//  `"?"` and `"!"` are a DIFFERENT pair: they are the DataWindow expression evaluator's own failure
//  markers, and they raise a reported error rather than a silent abort [:L760-L763, :L2231, :L2391].
//
// -----------------------------------------------------------------------------------------------------
//  5. `CLC_*` IS A FIFTH NUMERIC ALPHABET
// -----------------------------------------------------------------------------------------------------
//  Domain/ItemChangeProtocol.cs already warns that four alphabets in this system share the numerals 1
//  and 2: `RetCode.PREVENT`, `VetoResult.PreventOnce`/`PreventDeep`, `EventBroker.OnException`'s 1/2,
//  and the `{0,1,2,3}` item-change alphabet. `CLC_UNKNOWN=0 / CLC_YES=1 / CLC_NO=2` [:L113-L115] is a
//  FIFTH, and it is the most dangerous of them to conflate because 0 reads as success in the return-code
//  algebra while here it means "not yet determined". It is modelled as its own type (ColumnCalcFlag)
//  and is never mapped onto RetCode. "Not yet determined" and "determined not to calculate" are
//  distinct states: the engine lazily promotes UNKNOWN to YES or NO on first use [:L218-L221,
//  :L234-L239], so collapsing them would leave a column that arrived UNKNOWN never evaluated at all.
//
// -----------------------------------------------------------------------------------------------------
//  6. POWERSCRIPT SEMANTICS PRESERVED DELIBERATELY
// -----------------------------------------------------------------------------------------------------
//  ONE-BASED ARRAYS (AAP section 0.8.6 R9). PowerBuilder arrays are one-based and `UpperBound` returns
//  the LAST VALID INDEX, not a length. The oracle relies on that in two idioms this file reproduces
//  through OneBasedList<T> rather than by hand-translating each loop: grow-on-demand
//  `if colID > UpperBound(ColDatas) then ColDatas[colID].flag = CLC_UNKNOWN` [:L218-L219, :L234-L235],
//  which extends the array to `colID` by writing past its end, and append
//  `ColDatas[colID].Indexes[UpperBound(...) + 1] = value` [:L277, :L285]. A silent off-by-one in either
//  is indistinguishable from a behavioural regression, so no loop bound below is ever "tidied".
//
//  `FOR` BOUNDS ARE EVALUATED ONCE. PowerScript computes a FOR statement's start, stop and step a
//  single time, before the loop is entered. The scanner's `for nPos = 1 to nLen` [:L1266] therefore
//  keeps its ORIGINAL bound even though a static substitution updates `nLen` at :L1436, while the final
//  `exp = Left(sExp,nLen - 1)` [:L1509] uses the UPDATED value. The observable consequence is that a
//  static substitution which LENGTHENS the expression can leave the new tail unscanned, so a macro
//  positioned after it is not registered. This is reproduced, not corrected (C-B): the specification's
//  own examples place the dynamic reference first and are unaffected, and "fixing" the bound would
//  change which references a stored expression carries.
//
//  STRUCTURES ARE VALUE TYPES. `VARDATA varData` [:L1249] is ONE local reused for every token, copied
//  into the list by value at :L1482. VariableReference and FunctionReference are immutable records here,
//  so a fresh instance is built per token and the aliasing that a mutable class would introduce cannot
//  arise. `columnexpdata` and `columndata`, by contrast, are mutated in place through array-element
//  assignment at dozens of sites, so those two are mutable classes.
//
//  NULL PROPAGATES THROUGH CONCATENATION IN POWERSCRIPT, AND DOES NOT IN C#. The oracle repeatedly
//  builds a literal by concatenation and then null-checks the RESULT - `"'" + val + "'"` followed by
//  `if IsNull(sVal)` [dwvaluetoexp.srf:L47-L50] - which works only because PowerScript poisons the whole
//  concatenation with null. In C# `"'" + null + "'"` is `"''"` and the guard never fires. Every arm
//  below that reproduces that idiom therefore tests the INPUT for null BEFORE formatting, and the typed
//  conversion helpers at the end of this file are written the same way.
//
// -----------------------------------------------------------------------------------------------------
//  7. WHY THE CALCULATION CHAIN IS ASYNCHRONOUS AND THE REGISTRATION CHAIN IS NOT
// -----------------------------------------------------------------------------------------------------
//  Parsing never evaluates anything. The static branch substitutes a variable's value EXPRESSION - text
//  for text [:L1429] - so `ParseExp` and everything that only registers state stays synchronous.
//
//  Calculation is different. A macro function's implementation belongs to the APPLICATION, not to this
//  service: the oracle raises `#DataWindow.Event OnColumnExpInvokeMethod(...)` [:L2263, :L2287] and the
//  legacy specification shows the application implementing the `choose case name` switch [md:L120-L128].
//  Across the new boundary that inverts into C-04's `InvokeMethodChannel` stream, so obtaining a macro
//  value is a network round trip. An in-process call could not fail in transit and a network call can,
//  which is why the calculation chain returns ValueTask and threads a CancellationToken. Blocking on it
//  instead would be a deadlock hazard in the ASP.NET Core host, and it is not an optimisation to avoid:
//  handling a failure mode that decomposition itself creates is required BY the transition.
//
//  IExpressionServiceHost IS SYNCHRONOUS AND THAT IS DELIBERATE. Its four members are the cross-object
//  calls the oracle makes on a FOREIGN or CONTEXT service [:L2182, :L2185, :L2200, :L2385], and
//  ExpressionSession.cs defines them synchronously. Both DataWindows in such a call are co-resident in
//  one session inside one DataServices instance - AAP section 0.6.2.3 BLOCKS anything else - so the call
//  is in-process in the target design too. This engine therefore never routes its own cross-DataWindow
//  work through the synchronous members: it resolves the peer through the session and, when that peer is
//  a ColumnExpressionEngine (which it is for the whole real topology), awaits the peer's asynchronous
//  entry point directly. The synchronous members remain fully implemented for any other consumer of the
//  interface, and they refuse to start a channel invocation they could not await, returning the abort
//  sentinel of section 4 instead. That refusal is a defined narrowing in exactly the sense AAP section
//  0.6.2.3 sanctions - narrow with a defined error, never widen with a guess - and it is unreachable
//  from this engine's own paths.
//
// -----------------------------------------------------------------------------------------------------
//  8. DEFECTS REPRODUCED VERBATIM (C-B) - each is annotated again at its point of reproduction
// -----------------------------------------------------------------------------------------------------
//   a. `","` appears TWICE in the token terminator set [:L1301].
//   b. Three `"~n"` skips are commented out in the oracle [:L1376, :L1494, :L1498] and stay inert here.
//   c. `event onenable`'s body is entirely commented out [:L2428-L2432] and it simply returns 0.
//   d. `_of_calcitem` is declared `public` despite its private-convention underscore [:L142].
//   e. The empty-variable-value arm returns FAILED where its neighbours return E_INVALID_ARGUMENT
//      [:L1432 against :L1418/:L1423/:L1427].
//   f. All 28 reported messages are hardcoded Chinese and do NOT route through the localization
//      library, unlike the equivalent messages in the dwsvc, rowselect and contextmenu services which
//      do. ParseErrorFormatter.cs hard-wires `Localized = false` for every one of them.
//   g. The redraw-suppression threshold is the literal 200 [:L223, :L225].
//   h. `_of_calcitem` clears the duplicate group's dirty flags with the LOOP COUNTER instead of the
//      group member - `ColExpDatas[nIndex].dirty = false` [:L697] - where `of_calcempty` uses the
//      correct `ColExpDatas[ColExpDatas[nIndex].dupExps[nIndex2]]` [:L2059].
//   i. The expression-cache error message formats `ex.text` from the OUTER catch variable while the
//      inner one is `ex2` [:L739 against the catch at :L703].
//   j. The scanner re-arms macro detection on a space INSIDE a quoted literal [:L1498], so a `$` inside
//      quotes can start a macro after a space. It emerges from transliterating the state machine.
//   k. `SetExp` compares the incoming RAW text against the stored POST-REWRITE text [:L1655], so
//      re-setting the same source text always re-parses once a static reference has been expanded.
//   l. The generated compute name carries a DOUBLE underscore, because DWOSUFFIX already starts with
//      one [:L1886].
//   m. Preprocessing writes a memoised index back through a `readonly` array parameter [:L2195], with
//      the observable consequence that the memo can go stale after a variable is redefined.
//   n. The static rewrite is GLOBAL while the scanner is quote-aware [:L1435 against :L1267-L1284], so a
//      variable name that also appears inside a quoted literal is substituted there too.
//   o. `of_setcacheable` REPORTS a failed compute creation at :L1888 and then FALLS THROUGH - there is no
//      `return` before :L1895 sets `cacheable` and :L1897 answers OK - so the success code is a lie until
//      the handle probe at :L707-L713 re-creates the object on the next calculation.
//
// -----------------------------------------------------------------------------------------------------
//  9. BOUNDARIES
// -----------------------------------------------------------------------------------------------------
//  This is the HEADLESS half only (C-D): no UI type, no DPI or pixel conversion, no font measurement,
//  no window geometry, no Win32, no popup-menu rendering, no MessageBox - all 28 dialog sites become
//  structured errors through ParseErrorFormatter.cs. No package is added (AAP section 0.5.3): no
//  expression parser, no SQL parser, no pinyin package, no validation framework. No P/Invoke, and none
//  is needed - this legacy library declares ZERO PBNI native objects, so the whole DataWindow service
//  layer ports as pure logic and the only difficulty is behavioural fidelity. No secret appears here in
//  any form. The item-change gate is EXTERNAL: `se_cst_dw.sru:L43` declares `EID_ITEMCHANGE` with the
//  note that disabling it stops column-expression calculation, and :L187 short-circuits before
//  :L313-L314 ever reaches this engine, so the gate suppresses the CALL IN and Domain/EventGate.cs owns
//  the mask. This file does not re-test it.
// =====================================================================================================

using System.Collections.Immutable;
using System.Globalization;

using Microsoft.Extensions.Logging;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Validators;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Expressions;

/// <summary>
/// The tri-state column calculation cache - the port of <c>CLC_UNKNOWN</c>, <c>CLC_YES</c> and
/// <c>CLC_NO</c> [n_cst_dwsvc_columnexp.sru:L113-L115], and the counterpart of
/// <c>dataservices.v1.ColumnData.Types.CalcFlag</c>.
/// </summary>
/// <remarks>
/// <para>
/// A FIFTH NUMERIC ALPHABET, MODELLED AS ITS OWN TYPE. See section 5 of this file's header: four other
/// alphabets in this system already share the numerals 1 and 2, and 0 reads as SUCCESS in the
/// return-code algebra while here it means NOT YET DETERMINED. It is never mapped onto
/// <see cref="RetCode"/>.
/// </para>
/// <para>
/// THE MEMBER SPELLINGS ARE THE ORACLE'S, DELIBERATELY (AAP section 0.4.5.3). They travel on serialized
/// C-04 payloads, in log records and in characterization recordings, so a rename to C# convention would
/// silently invalidate every stored comparison. The root <c>.editorconfig</c> scopes the
/// naming-analyzer suppressions to this exact file for that reason.
/// </para>
/// </remarks>
public enum ColumnCalcFlag : long
{
    /// <summary>
    /// <c>CLC_UNKNOWN = 0</c> [:L113] - this column's contribution has not been determined yet. It is
    /// promoted to <see cref="CLC_NO"/> or <see cref="CLC_YES"/> on first use [:L238-L293].
    /// </summary>
    CLC_UNKNOWN = 0,

    /// <summary>
    /// <c>CLC_YES = 1</c> [:L114] - this column drives at least one expression or variable, and the
    /// reverse index on <see cref="ColumnCalcData"/> names them.
    /// </summary>
    CLC_YES = 1,

    /// <summary>
    /// <c>CLC_NO = 2</c> [:L115] - determined that this column drives nothing. Distinct from
    /// <see cref="CLC_UNKNOWN"/>: the entry point at :L221 skips the column entirely on NO but
    /// evaluates the reverse index on UNKNOWN.
    /// </summary>
    CLC_NO = 2,
}

/// <summary>
/// A PowerScript array, one-based, with <see cref="UpperBound"/> answering the LAST VALID INDEX rather
/// than a length - the single translation seam through which every ported index in this file passes.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <remarks>
/// <para>
/// AAP section 0.8.6 R9 names one-based-to-zero-based translation the most dangerous mechanical hazard
/// in the refactor, because a silent off-by-one is indistinguishable from a behavioural regression. The
/// mitigation it prescribes is a centralised one-based helper rather than an audit of each loop, and
/// this is it. Two oracle idioms depend on the semantics being exact:
/// </para>
/// <para>
/// GROW ON DEMAND. <c>if colID &gt; UpperBound(ColDatas) then ColDatas[colID].flag = CLC_UNKNOWN</c>
/// [:L218-L219, :L234-L235] writes PAST the end of the array, which in PowerScript extends it to
/// <c>colID</c> and default-initialises everything in between. <see cref="EnsureUpperBound"/> is that
/// behaviour, and the default element comes from the factory supplied at construction because a
/// PowerScript structure array grows with zeroed structures, not with nulls.
/// </para>
/// <para>
/// APPEND. <c>ColDatas[colID].Indexes[UpperBound(...) + 1] = value</c> [:L277, :L285] is the idiom for
/// "add one at the end"; <see cref="Append"/> is exactly that and nothing more.
/// </para>
/// </remarks>
public sealed class OneBasedList<T>
{
    private readonly List<T> _items;
    private readonly Func<T> _defaultFactory;

    /// <summary>
    /// Creates an empty array whose growth fills new slots from <paramref name="defaultFactory"/>.
    /// </summary>
    /// <param name="defaultFactory">
    /// Produces the value a PowerScript array would default-initialise a new slot to: zero for a
    /// numeric element, a zeroed structure for a structure element.
    /// </param>
    public OneBasedList(Func<T> defaultFactory)
    {
        ArgumentNullException.ThrowIfNull(defaultFactory);

        _items = [];
        _defaultFactory = defaultFactory;
    }

    /// <summary>
    /// Creates an array pre-populated from <paramref name="items"/>, preserving their order.
    /// </summary>
    /// <param name="defaultFactory">As documented on the other constructor.</param>
    /// <param name="items">The initial contents, in order, becoming indexes 1..n.</param>
    public OneBasedList(Func<T> defaultFactory, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(defaultFactory);
        ArgumentNullException.ThrowIfNull(items);

        _items = [.. items];
        _defaultFactory = defaultFactory;
    }

    /// <summary>
    /// <c>UpperBound(array)</c> - the LAST VALID INDEX, and 0 when the array is empty. This is not a
    /// length, and reading it as one is the off-by-one R9 warns about.
    /// </summary>
    public int UpperBound => _items.Count;

    /// <summary>
    /// The one-based element accessor. Reading outside 1..<see cref="UpperBound"/> throws;
    /// PowerScript would raise its own bounds error, so a silent default would be less faithful, not
    /// more. Writing at <see cref="UpperBound"/> + 1 or beyond GROWS the array, exactly as the oracle's
    /// grow-on-demand idiom relies on.
    /// </summary>
    /// <param name="index">The one-based index.</param>
    public T this[int index]
    {
        get
        {
            if (index < 1 || index > _items.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    "PowerScript arrays are one-based and UpperBound answers the last valid index, so "
                        + "the readable range here is 1.."
                        + _items.Count.ToString(CultureInfo.InvariantCulture)
                        + ". Growing on demand is a WRITE-side behaviour [n_cst_dwsvc_columnexp.sru:"
                        + "L218-L219]; a read past the end would mask the off-by-one that AAP section "
                        + "0.8.6 R9 warns about.");
            }

            return _items[index - 1];
        }

        set
        {
            if (index < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    "PowerScript array indexes start at 1.");
            }

            EnsureUpperBound(index);
            _items[index - 1] = value;
        }
    }

    /// <summary>
    /// Extends the array so that <paramref name="upperBound"/> becomes a valid index, filling every new
    /// slot from the default factory. A no-op when the array is already at least that long - the oracle
    /// guards it the same way with <c>if colID &gt; UpperBound(ColDatas)</c> [:L218].
    /// </summary>
    /// <param name="upperBound">The one-based index that must become valid.</param>
    public void EnsureUpperBound(int upperBound)
    {
        while (_items.Count < upperBound)
        {
            _items.Add(_defaultFactory());
        }
    }

    /// <summary>
    /// <c>array[UpperBound(array) + 1] = value</c> [:L277, :L285] - append one element at the end.
    /// </summary>
    /// <param name="value">The element to append.</param>
    /// <returns>The one-based index the element landed at.</returns>
    public int Append(T value)
    {
        _items.Add(value);
        return _items.Count;
    }

    /// <summary>
    /// Empties the array. The oracle expresses this by assigning a fresh empty local over the field -
    /// <c>ColDatas = emptyColDatas</c> [:L1135], <c>ColExpDatas = emptyColExpDatas</c> [:L385] - and
    /// this is that assignment without the intermediate.
    /// </summary>
    public void Clear() => _items.Clear();

    /// <summary>
    /// Replaces the whole contents in one step, preserving order. Used where the oracle rebuilds an
    /// array into a local and then assigns it over the field, as removal does at :L474-L481.
    /// </summary>
    /// <param name="items">The new contents, becoming indexes 1..n.</param>
    public void Assign(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.Clear();
        _items.AddRange(items);
    }

    /// <summary>
    /// The contents in index order, for enumeration and for projection onto a C-04 payload. Snapshotted
    /// so that a consumer iterating it cannot be disturbed by a calculation mutating the array.
    /// </summary>
    public ImmutableArray<T> Items => [.. _items];

    /// <summary>
    /// True when <paramref name="index"/> is inside 1..<see cref="UpperBound"/>. Every public entry
    /// point below performs this test explicitly rather than relying on an exception, because the oracle
    /// answers a bounds violation with <see cref="RetCode.E_OUT_OF_BOUND"/> [:L463, :L918, :L1653] and
    /// not with a fault.
    /// </summary>
    /// <param name="index">The one-based index to test.</param>
    public bool IsValidIndex(int index) => index >= 1 && index <= _items.Count;

    /// <summary>
    /// Linear search over the one-based range, answering the one-based index or 0 when absent. 0 is the
    /// oracle's own "not found" answer [:L1000, :L1136], so it is preserved rather than becoming -1.
    /// </summary>
    /// <param name="predicate">The match test.</param>
    /// <param name="reverse">
    /// When true the scan runs from <see cref="UpperBound"/> down to 1. Both lookup helpers in the
    /// oracle scan in reverse - <c>for nIndex = UpperBound(...) to 1 step -1</c> [:L996, :L1132] - which
    /// means the LAST matching entry wins, and that is observable when a column carries duplicate
    /// expressions.
    /// </param>
    public int IndexOf(Func<T, bool> predicate, bool reverse = false)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        if (reverse)
        {
            for (int index = _items.Count; index >= 1; index--)
            {
                if (predicate(_items[index - 1]))
                {
                    return index;
                }
            }

            return 0;
        }

        for (int index = 1; index <= _items.Count; index++)
        {
            if (predicate(_items[index - 1]))
            {
                return index;
            }
        }

        return 0;
    }
}


/// <summary>
/// One bound column expression - the port of <c>columnexpdata</c>
/// [n_cst_dwsvc_columnexp.sru:L22-L42], EXACTLY 19 fields, and the counterpart of
/// <c>dataservices.v1.ColumnExpData</c>.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS A MUTABLE CLASS AND NOT A RECORD. The oracle's structure is a value type, but it is
/// mutated in place through array-element assignment at dozens of sites - <c>.dirty</c> [:L396],
/// <c>.exp</c> [:L1659], <c>.vars</c>/<c>.fns</c> [:L1657], <c>.alwaysCalc</c> [:L524],
/// <c>.relativeColIDs</c> [:L583], <c>.cacheable</c> [:L1895], <c>.computeName</c> [:L1886],
/// <c>.empty</c> [:L1987-L2015], <c>.dwo</c> [:L709] and <c>.dupExps</c> [:L491, :L1575]. Modelling it
/// as an immutable record would turn each of those into a whole-structure rebuild and would make the
/// reverse-dependency graph read as a series of copies rather than as the shared mutable state it is.
/// The value-copy points that DO matter are handled explicitly: the parser's per-token locals are
/// immutable records (see section 6 of the header), and removal rebuilds the list rather than aliasing
/// it [:L474-L481].
/// </para>
/// <para>
/// FIELD ORDER AND FIELD COUNT ARE BOTH PART OF THE CONTRACT. There are 19 fields and no more; a
/// nineteen-field check belongs in the test suite because C-04's <c>ColumnExpData</c> is generated from
/// the same census and a silently added field would desynchronise the two.
/// </para>
/// </remarks>
public sealed class ColumnExpressionData
{
    /// <summary>
    /// <c>string name</c> [:L23] - the bound column's name, always stored LOWER-CASED AND TRIMMED
    /// because :L1538 normalises it before use and :L1131 normalises the lookup key the same way.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// <c>long id</c> [:L24] - the column's ONE-BASED DataWindow ordinal, read from
    /// <c>Describe(colname + ".ID")</c> [:L1541]. Zero is the oracle's "no such column" answer and is
    /// rejected at registration [:L1542].
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// <c>dwobject dwo</c> [:L25] - the column's DataWindow object. A live pointer at source; here the
    /// host's object handle. It is re-resolved whenever it goes stale [:L699-L710], which is the
    /// oracle's own recovery path after the DataWindow object model is replaced.
    /// </summary>
    public IDataWindowObject? Dwo { get; set; }

    /// <summary>
    /// <c>long coltype</c> [:L26] - one of the <c>COL_TYPE_*</c> values from
    /// <c>n_cst_dwsvc.sru:L26-L32</c>, mirrored on <see cref="DataWindowServiceBase"/>. It selects the
    /// typed read/write arm in every calculation [:L717-L740, :L766-L854].
    /// </summary>
    public long ColType { get; set; } = DataWindowServiceBase.COL_TYPE_UNKNOWN;

    /// <summary>
    /// <c>string exp</c> [:L27] - THE STORED EXPRESSION, AFTER THE PARSER'S IN-PLACE REWRITE.
    /// </summary>
    /// <remarks>
    /// This is the single most important field in the structure and the one most easily misread. The
    /// parser rewrites its expression argument in place, substituting every STATIC reference away
    /// [:L1435], and what lands here is the result of that rewrite - so a static binding is no longer
    /// visible in it, which is exactly why later mutation of the variable cannot reach it. C-04
    /// additionally carries the UNEXPANDED source text, because this field alone cannot distinguish a
    /// static binding from a literal. See <see cref="ColumnExpressionEngine.GetSourceExp"/>.
    /// </remarks>
    public string Exp { get; set; } = string.Empty;

    /// <summary>
    /// <c>vardata vars[]</c> [:L28] - the variable references the parser retained. After a successful
    /// parse this holds ONLY bare context columns and DYNAMIC references; static references were
    /// substituted away and never appended [:L1415 against :L1477-L1483].
    /// </summary>
    public OneBasedList<VariableReference> Vars { get; } = new(static () => new VariableReference());

    /// <summary>
    /// <c>funcdata fns[]</c> [:L29] - the macro function references, in the order the parser resolved
    /// them. Preprocessing walks this list BACKWARDS [:L2226], and the order is observable because an
    /// outer call's argument text is rewritten by every inner call that precedes it [:L2312-L2320].
    /// </summary>
    public OneBasedList<FunctionReference> Fns { get; } = new(static () => new FunctionReference());

    /// <summary>
    /// <c>long relativecolids[]</c> [:L30] - the columns whose change triggers this expression, by
    /// one-based column id [:L583]. Non-empty means the expression is NOT triggered by an unrelated
    /// column, which is what <c>bHasRelative</c> tests at :L248.
    /// </summary>
    public OneBasedList<long> RelativeColIds { get; } = new(static () => 0L);

    /// <summary>
    /// <c>long relativeinputcolids[]</c> [:L31] - the columns whose change triggers this expression ONLY
    /// when the change came from user input [:L631]. The <c>frominput</c> discrimination at :L301-L315
    /// is what separates this from <see cref="RelativeColIds"/>, and the legacy specification states the
    /// distinction directly [docs/n_cst_dwsvc_columnexp.md:L162-L163].
    /// </summary>
    public OneBasedList<long> RelativeInputColIds { get; } = new(static () => 0L);

    /// <summary>
    /// <c>long dupexps[]</c> [:L32] - THE MULTIPLE-EXPRESSIONS-PER-COLUMN MECHANISM.
    /// </summary>
    /// <remarks>
    /// The legacy specification lists it as a headline capability: one column may carry several
    /// expressions, and which one calculates is selected by which column changed
    /// [docs/n_cst_dwsvc_columnexp.md:L8]. The group list is assigned at :L491 (on removal) and :L1575
    /// (on addition), and EVERY MEMBER OF A GROUP RECEIVES THE SAME LIST minus itself - the inner loop at
    /// :L1571-L1574 skips only the member being written. It is counted for the dirty-clearing walk at
    /// :L694 and consumed at :L2023 and :L2059.
    /// </remarks>
    public OneBasedList<long> DupExps { get; } = new(static () => 0L);

    /// <summary>
    /// <c>boolean emptystringisnull</c> [:L33] - true when the column declares <c>NilIsNull</c> on its
    /// edit, dddw or ddlb style [:L1546-L1552]. It turns an empty calculated value into NULL for a
    /// string column [:L831] and it participates in the trace's <c>"(null)"</c> selection [:L758].
    /// </summary>
    public bool EmptyStringIsNull { get; set; }

    /// <summary>
    /// <c>boolean hasmacro</c> [:L34] - <c>UpperBound(vars) &gt; 0 or UpperBound(fns) &gt; 0</c>
    /// [:L1557]. It gates preprocessing [:L743] and it makes an expression ineligible for the compute
    /// cache [:L705, :L1881].
    /// </summary>
    public bool HasMacro { get; set; }

    /// <summary>
    /// <c>boolean alwayscalc</c> [:L35] - recalculate on ANY column change, bypassing the relative-column
    /// tests [:L244-L245].
    /// </summary>
    public bool AlwaysCalc { get; set; }

    /// <summary>
    /// <c>boolean triggerevent</c> [:L36] - raise the host's ItemChanged event before writing the
    /// calculated value, and ABANDON THE WRITE when it answers non-zero [:L783-L785 and the five
    /// sibling arms]. That veto is why this flag cannot be treated as diagnostic.
    /// </summary>
    public bool TriggerEvent { get; set; }

    /// <summary>
    /// <c>boolean recursive</c> [:L37] - permit this expression to appear more than once on the
    /// calculation stack. When false, the stack scan at :L682-L690 refuses re-entry.
    /// </summary>
    public bool Recursive { get; set; }

    /// <summary>
    /// <c>boolean dirty</c> [:L38] - the row-calculation pass marker. Set for every expression at the
    /// start of a row pass [:L392-L398, :L1984] and cleared as each is calculated [:L693].
    /// </summary>
    public bool Dirty { get; set; }

    /// <summary>
    /// <c>boolean empty</c> [:L39] - set by the empty-only pass to record whether the bound column
    /// currently holds no value [:L1985-L2016]. It gates <c>_of_calcitem</c> at :L680 while
    /// <c>_bRowCalcingEmpty</c> is set.
    /// </summary>
    public bool Empty { get; set; }

    /// <summary>
    /// <c>boolean cacheable</c> [:L40] - when set, the service creates a hidden DataWindow compute object
    /// carrying this expression and reads the value from it instead of evaluating [:L705, :L717-L740].
    /// Mutually exclusive with <see cref="HasMacro"/> [:L1881].
    /// </summary>
    public bool Cacheable { get; set; }

    /// <summary>
    /// <c>string computename</c> [:L41] - the generated compute object's name,
    /// <c>name + "_" + id + "_" + DWOSUFFIX</c> [:L1886]. NOTE THE DOUBLE UNDERSCORE: DWOSUFFIX already
    /// begins with one, so the name reads <c>n1_1__columnexp</c>. Reproduced exactly (C-B), because it is
    /// observable in the DataWindow syntax and on the C-04 payload.
    /// </summary>
    public string ComputeName { get; set; } = string.Empty;
}

/// <summary>
/// The reverse dependency index for one column - the port of <c>columndata</c>
/// [n_cst_dwsvc_columnexp.sru:L44-L48], three fields, and the counterpart of
/// <c>dataservices.v1.ColumnData</c>.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE GRAPH THAT MAKES DIRTY PROPAGATION WORK, and it runs in the opposite direction to the
/// expression list: from a CHANGED COLUMN to the expressions and variables that depend on it. It is
/// built lazily and then cached - the first change to a column walks every expression and every global
/// variable [:L238-L293] and records the answer, and subsequent changes read the cache. Discarding the
/// cache is how every mutating entry point invalidates the graph [:L1133-L1136], and it is discarded
/// wholesale rather than per column because a single new expression can add edges anywhere.
/// </para>
/// <para>
/// A consumer cannot derive this from the expression text: doing so would mean reimplementing the
/// parser AND the reference-matching scanner. That is why C-04 transmits it rather than recomputing it.
/// </para>
/// </remarks>
public sealed class ColumnCalcData
{
    /// <summary>
    /// <c>long flag</c> [:L45] - the tri-state cache state for this column. See
    /// <see cref="ColumnCalcFlag"/>; the field is typed rather than left as a raw number so it cannot be
    /// confused with a return code.
    /// </summary>
    public ColumnCalcFlag Flag { get; set; } = ColumnCalcFlag.CLC_UNKNOWN;

    /// <summary>
    /// <c>integer indexes[]</c> [:L46] - the one-based indexes of every EXPRESSION that recalculates
    /// when this column changes, appended at :L277 and walked at :L298-L317. It also receives the
    /// indirect edges that variable dependency contributes [:L288].
    /// </summary>
    public OneBasedList<int> Indexes { get; } = new(static () => 0);

    /// <summary>
    /// <c>integer varindexes[]</c> [:L47] - the one-based indexes of every GLOBAL VARIABLE whose value
    /// depends on this column AND which is linked to at least one foreign service [:L284-L287]. The link
    /// requirement is the whole point: an unlinked variable needs no notification because its dependent
    /// expressions were already collected into <see cref="Indexes"/>, whereas a linked one must fan the
    /// change out to the peer services at :L319-L326.
    /// </summary>
    public OneBasedList<int> VarIndexes { get; } = new(static () => 0);
}

/// <summary>
/// The DataWindow syntax-mutation seam - the only member of <c>#DataWindow</c> that the expression
/// cache needs and that <see cref="DataWindowServiceHost"/> does not expose.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS INTERFACE EXISTS HERE. The compute cache is a genuine part of the oracle's surface - C-04
/// carries it as <c>FLAG_CACHEABLE</c> - and it is implemented by generating a DataWindow compute object
/// at run time through <c>#DataWindow.Modify(syntax)</c> [:L1948] and destroying it through
/// <c>Modify("Destroy " + name)</c> [:L381, :L471, :L1663, :L1891]. The host contract this project
/// depends on exposes <c>Describe</c> but no <c>Modify</c>, so rather than dropping a contracted feature
/// or reaching outside the declared dependency set, the mutation is expressed as a one-member seam that
/// the engine PROBES THE HOST FOR: a host that implements it drives the real syntax, and one that does
/// not leaves the cache unavailable through the oracle's OWN failure path - <c>_of_createcompute</c>
/// returns the syntax plus an error string [:L1949-L1953], which is what the reported messages at :L713
/// and :L1888 already exist to carry. Nothing is silently skipped and nothing is guessed.
/// </para>
/// <para>
/// The cache is off by default in the oracle - <c>cacheable</c> defaults to false and only an explicit
/// <c>of_setcacheable(index, true)</c> [:L1857] turns it on - so the default calculation path is
/// unaffected either way.
/// </para>
/// </remarks>
public interface IDataWindowSyntaxMutator
{
    /// <summary>
    /// <c>#DataWindow.Modify(syntax)</c> - applies a DataWindow syntax fragment.
    /// </summary>
    /// <param name="syntax">The syntax fragment, exactly as the oracle composes it.</param>
    /// <returns>
    /// The empty string on success, or the DataWindow's error text. THE EMPTY STRING IS SUCCESS HERE,
    /// which inverts this file's usual convention and is the oracle's own contract [:L1949, :L145 of
    /// n_cst_thread_task_sqlupdate.sru for the same idiom elsewhere in the estate].
    /// </returns>
    string Modify(string syntax);
}


/// <summary>
/// The bind-time binding of one expression - the three-part payload C-04's
/// <c>dataservices.v1.ExpressionBinding</c> requires, and the reason a static reference survives
/// serialization at all.
/// </summary>
/// <param name="UnexpandedExp">
/// The expression EXACTLY AS SUPPLIED, before the parser's in-place rewrite [:L1435]. Without it a
/// static binding is indistinguishable from a literal, because the rewrite removed the variable's name
/// from the stored text.
/// </param>
/// <param name="BindTimeSnapshot">
/// Variable name to the variable's value EXPRESSION at the moment this expression was set, for every
/// macro variable the parse encountered - static and dynamic alike. For a static reference this is the
/// text that was actually baked in; for a dynamic one it records what the value WAS, which is what makes
/// the pair comparable on the wire.
/// </param>
/// <param name="Vars">The retained references [:L1510] - bare context columns and dynamic macros only.</param>
/// <param name="Fns">The retained macro function references [:L1511].</param>
/// <remarks>
/// DELIBERATELY NOT A TWENTIETH FIELD ON <see cref="ColumnExpressionData"/>. That structure is exactly 19
/// fields [:L22-L42] and the count is checked by the test suite against the same census C-04's
/// <c>ColumnExpData</c> was generated from. The unexpanded text has no field in the oracle because the
/// oracle never needed one - it had no boundary to cross - so it is carried alongside rather than
/// smuggled in.
/// </remarks>
public sealed record ExpressionBindingSnapshot(
    string UnexpandedExp,
    ImmutableDictionary<string, string> BindTimeSnapshot,
    ImmutableArray<VariableReference> Vars,
    ImmutableArray<FunctionReference> Fns);

/// <summary>
/// The DataWindow column-expression extension service - the port of <c>n_cst_dwsvc_columnexp</c>
/// [ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru, 2,435 lines] and the behaviour
/// behind contract C-04.
/// </summary>
/// <remarks>
/// <para>
/// Read this file's header banner first. It documents the undocumented <c>@</c> sigil, the
/// three-orthogonal-bit reference model and its projection onto C-04's five expansion modes, the
/// mandatory parenthesis wrap, the empty string as an abort sentinel, the <c>CLC_*</c> alphabet, the
/// PowerScript semantics preserved on purpose, why the calculation chain is asynchronous, and the
/// fifteen defects reproduced verbatim.
/// </para>
/// <para>
/// EVERY COLLABORATOR IS INJECTED AND EVERY ONE IS OPTIONAL, so the whole engine is exercisable with no
/// live DataWindow and no gRPC host - which constraint C-H requires, since this is by far the largest
/// file in the service and it dominates the per-service coverage gate. What the engine cannot do without
/// is a host, and it acquires that the way every DataWindow service does: through
/// <see cref="OnInit(DataWindowServiceHost)"/>, raised by <c>se_cst_dw</c> during its own construction
/// [se_cst_dw.sru:L576-L580].
/// </para>
/// <para>
/// THREAD AFFINITY. The oracle is a single-threaded PowerBuilder object whose state is a set of plain
/// arrays with no synchronisation of any kind, and its calculation paths re-enter each other through
/// events. This port keeps that model rather than inventing locking that would change ordering: one
/// engine instance belongs to one logical DataWindow and must be driven by one caller at a time, which
/// is exactly the discipline <see cref="ExpressionSession.Acquire"/> exists to enforce for the session
/// that owns it. Concurrent calls against one instance are a caller error, not a case to serialise
/// silently.
/// </para>
/// </remarks>
public sealed class ColumnExpressionEngine : DataWindowServiceBase, IExpressionServiceHost
{
    /// <summary>
    /// <c>constant string DWOSUFFIX = "_columnexp"</c> [:L95] - the suffix every generated compute
    /// object's name carries, and observable in <see cref="ColumnExpressionData.ComputeName"/>. It
    /// begins with an underscore, which is why the composed name shows a double one [:L1886].
    /// </summary>
    public const string DWOSUFFIX = "_columnexp";

    /// <summary>
    /// The abort sentinel of section 4 of the header - <c>""</c> means ABORT THIS CALCULATION and is
    /// never a legitimate value [:L745, :L2183, :L2186, :L2358].
    /// </summary>
    public const string AbortSentinel = "";

    /// <summary>
    /// <c>"?"</c> - the DataWindow evaluator's "value could not be determined" marker. Distinct from
    /// <see cref="AbortSentinel"/>: it is REPORTED [:L760-L763, :L2391].
    /// </summary>
    public const string UndeterminedValueSentinel = "?";

    /// <summary>
    /// <c>"!"</c> - the DataWindow evaluator's "expression is invalid" marker [:L760, :L2231, :L2391].
    /// </summary>
    public const string InvalidExpressionSentinel = "!";

    /// <summary>
    /// The object name that addresses the DataWindow itself rather than one of its columns - what
    /// <c>#DataWindow.Object.DataWindow</c> resolves to, and the object the four-argument
    /// <c>_of_calcvarexpvalue</c> arity reports against [:L2399].
    /// </summary>
    public const string DataWindowLevelObjectName = "datawindow";

    /// <summary>
    /// The error text <see cref="ModifySyntax"/> answers when no syntax mutator is available at all.
    /// </summary>
    /// <remarks>
    /// IT IS NON-EMPTY ON PURPOSE, because the empty string is <c>Modify</c>'s SUCCESS answer [:L1949]. A
    /// missing mutator must not read as a successful mutation, or the caller would believe a compute object
    /// exists that does not - and would then read a cached value from nothing. The text is deliberately not a
    /// catalogued message: it has no legacy counterpart, so it is diagnostic rather than reported.
    /// </remarks>
    public const string SyntaxMutationUnavailable =
        "The DataWindow host does not support syntax mutation, so the expression cache is unavailable.";

    /// <summary>
    /// How many raised errors <see cref="Errors"/> retains before discarding the oldest.
    /// </summary>
    /// <remarks>
    /// The oracle retains none - each dialog is shown and forgotten - so any bound is additive. This one is
    /// sized to hold every distinct site plus a margin, so a caller diagnosing a single calculation sees the
    /// whole cascade rather than a truncation of it.
    /// </remarks>
    public const int MaxRetainedErrors = 64;

    /// <summary>
    /// <c>";"</c> [:L1251] - the trailer the scanner appends before scanning so that a token at the end
    /// of the expression is terminated by something. EVERY REPORTED CARET POSITION IS AN INDEX INTO THE
    /// TRAILER-EXTENDED STRING, and those positions are passed to
    /// <see cref="ParseErrorFormatter"/> unchanged.
    /// </summary>
    public const string TRAILER = ";";

    /// <summary>
    /// <c>"$"</c> [:L1252] - the macro sigil. Doubled means dynamic [:L1290].
    /// </summary>
    public const string MACRO_FLAG = "$";

    /// <summary>
    /// <c>"@"</c> [:L1253] - THE CONTEXT SIGIL, documented only inside the oracle's own comment block
    /// [:L1220-L1222] and nowhere in the published specification. See section 1 of the header.
    /// </summary>
    public const string MACRO_CONTEXT = "@";

    /// <summary>
    /// <c>MACRO_PHASE_NONE = 0</c> [:L1254] - the scanner is inside a token that cannot start a macro.
    /// </summary>
    public const long MACRO_PHASE_NONE = 0;

    /// <summary>
    /// <c>MACRO_PHASE_FIND = 1</c> [:L1255] - the scanner is at a position where a sigil would start a
    /// macro. This is the initial state [:L1263].
    /// </summary>
    public const long MACRO_PHASE_FIND = 1;

    /// <summary>
    /// <c>MACRO_PHASE_PARSE = 2</c> [:L1256] - a sigil has been seen and the scanner is accumulating the
    /// token until a terminator.
    /// </summary>
    public const long MACRO_PHASE_PARSE = 2;

    /// <summary>
    /// The token terminator set [:L1301], IN THE ORACLE'S OWN ORDER AND WITH ITS OWN DUPLICATE.
    /// </summary>
    /// <remarks>
    /// <c>","</c> APPEARS TWICE, at positions 10 and 18 of the oracle's <c>choose case</c> list. In a
    /// PowerScript <c>choose case</c> a duplicated value is harmless - the first arm wins and the second
    /// is unreachable - so it is a blemish rather than a bug, and it is reproduced rather than tidied
    /// (C-B). Removing it would be a silent edit to the one artefact that IS the specification, and
    /// keeping it costs a set membership test nothing. <see cref="TRAILER"/> is the final member, which
    /// is what makes the appended sentinel terminate the last token.
    /// </remarks>
    public static readonly ImmutableArray<char> TokenTerminators =
    [
        '=', '+', '-', '*', '/', '\\', '>', '<', '^', ',', '(', ')', '[', ']', '{', '}', ':',
        ',', // <- the duplicate, preserved verbatim from :L1301 (C-B)
        '?', '!', '&', '|',
        ';', // TRAILER
    ];

    /// <summary>
    /// The word delimiters a global variable NAME may not contain [:L1614]. A superset of
    /// <see cref="TokenTerminators"/>: it additionally rejects whitespace, both quote characters and the
    /// period, and it does NOT contain a duplicate.
    /// </summary>
    public static readonly ImmutableArray<char> VariableNameDelimiters =
    [
        ' ', '\t', '\n', '\r', '\'', '"', '+', '-', '*', '/', '\\', '>', '<', '=', '(', ')', '{',
        '}', '[', ']', ',', '.', ':', ';', '?', '!', '&', '|',
    ];

    /// <summary>
    /// The word delimiters the column-reference scanner splits on [:L1840]. Distinct again from both
    /// sets above: it includes whitespace and the period but NOT the quote characters, because quoting is
    /// handled by its own arm [:L1830-L1839].
    /// </summary>
    private static readonly ImmutableArray<char> ReferenceScanDelimiters =
    [
        ' ', '\t', '\n', '\r', '+', '-', '*', '/', '\\', '>', '<', '=', '(', ')', '{', '}', '[',
        ']', ',', '.', ':', ';', '?', '!', '&', '|',
    ];

    // -------------------------------------------------------------------------------------------------
    //  State. The oracle's private instance variables [:L100-L110], one for one and in the same order.
    // -------------------------------------------------------------------------------------------------

    /// <summary><c>COLUMNDATA ColDatas[]</c> [:L101] - the reverse dependency index, by column id.</summary>
    private readonly OneBasedList<ColumnCalcData> _colDatas = new(static () => new ColumnCalcData());

    /// <summary><c>COLUMNEXPDATA ColExpDatas[]</c> [:L102] - the bound expressions, in registration order.</summary>
    private readonly OneBasedList<ColumnExpressionData> _colExpDatas =
        new(static () => new ColumnExpressionData());

    /// <summary>
    /// The bind-time bindings, keyed by the expression entry's own identity so that the removal
    /// re-indexing at :L474-L481 cannot desynchronise them. See <see cref="ExpressionBindingSnapshot"/>
    /// for why they are held beside the structure rather than inside it.
    /// </summary>
    private readonly Dictionary<ColumnExpressionData, ExpressionBindingSnapshot> _expBindings = [];

    /// <summary>
    /// The bind-time bindings of the global variable expressions, keyed by variable name. Names are
    /// unique [:L1606], case-sensitive [:L997] and never rewritten, so the name is a stable key.
    /// </summary>
    private readonly Dictionary<string, ExpressionBindingSnapshot> _varBindings = [];

    /// <summary>
    /// Placeholder token to the value it stands for, for every value this engine has bound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ENGINE-LIFETIME AND APPEND-ONLY, WHICH IS WHAT MAKES A PLACEHOLDER RESOLVABLE WHENEVER IT IS
    /// REACHED.</b> A statically expanded placeholder is baked into an expression's executable text and is
    /// evaluated on every later calculation of that expression, so a per-call table would resolve the first
    /// calculation and fail every subsequent one. Entries are small - one boxed scalar each - and are
    /// bounded by the number of typed variable binds the caller performs.
    /// </para>
    /// <para>
    /// THE ORDINAL IS A MONOTONIC COUNTER RATHER THAN A HASH OR A CLOCK, so a characterization run over the
    /// same sequence of calls produces the same tokens (AAP 0.6.7).
    /// </para>
    /// </remarks>
    private readonly Dictionary<string, ExpressionValue> _boundValues = new(StringComparer.Ordinal);

    /// <summary>
    /// Placeholder token to the literal the oracle renders for its value - the OBSERVABLE half.
    /// </summary>
    /// <remarks>
    /// <b>THE TRACE AND EVERY OTHER REPORTED TEXT ARE RENDERED THROUGH THIS.</b> The trace payload is a
    /// C-04 event [<c>oncolumnexptrace</c>, <c>se_cst_dw.sru:L32</c>] carrying the PREPROCESSED expression,
    /// which the oracle produces with the value spliced in - so a placeholder reaching a trace consumer
    /// would be an observable change and would corrupt every characterization recording that compares one.
    /// Keeping the rendered fragment beside the value is what lets the reported text be derived from the
    /// executed one rather than composed a second time.
    /// </remarks>
    private readonly Dictionary<string, string> _boundFragments = new(StringComparer.Ordinal);

    /// <summary>
    /// Variable name to the TYPED value it was bound from, for variables set through a typed overload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PRESENCE HERE IS WHAT DISTINGUISHES A VALUE FROM AN EXPRESSION, AND THE DISTINCTION IS THE WHOLE
    /// FIX.</b> <c>of_addvar(name, value)</c> is documented as taking a VALUE, so a caller may legitimately
    /// forward untrusted text through it; <c>of_addvarexp(name, exp)</c> is documented as taking an
    /// EXPRESSION, so its argument is syntax the caller composed deliberately. Only the former is bound.
    /// Binding the latter would be wrong twice over: it is not a scalar, and treating deliberate syntax as
    /// data would break the API the caller chose.
    /// </para>
    /// <para>
    /// AN ENTRY IS REMOVED WHEN THE SAME NAME IS LATER GIVEN AN EXPRESSION, because the variable has ceased
    /// to be a bound value - and a stale entry would bind the OLD value into a new expression's executable
    /// text while the observable text carried the new one.
    /// </para>
    /// </remarks>
    private readonly Dictionary<string, ExpressionValue> _boundVarValues = new(StringComparer.Ordinal);

    private int _boundValueOrdinal;

    /// <summary>
    /// Whether a typed bind is currently delegating to the expression path.
    /// </summary>
    /// <remarks>
    /// <b>THE EXPRESSION PATH CLEARS A STALE BINDING AND THE TYPED PATH MUST NOT HAVE ITS OWN CLEARED.</b>
    /// The typed overloads are sugar over the expression ones - which is the oracle's own shape, since
    /// <c>of_addvar</c> renders and forwards [<c>:L1094</c>] - so the two meet at one method that cannot
    /// otherwise tell which of them the caller invoked. A field rather than a parameter keeps the public
    /// signature of <c>of_addvarexp</c> exactly the oracle's.
    /// </remarks>
    private bool _boundVarValueAssignmentInFlight;

    /// <summary>
    /// <c>GLOBALVARDATA GlobalVars[]</c> [:L103] - the global variable table. Held as the immutable
    /// environment type the sibling files publish, so a replacement is one field assignment and a reader
    /// mid-calculation cannot observe a half-updated table.
    /// </summary>
    private ExpressionVariableEnvironment _globalVars = ExpressionVariableEnvironment.Empty;

    /// <summary>
    /// <c>boolean _bRowCalcing</c> [:L105] - set for the duration of a whole-row pass. It changes
    /// <c>_of_calcitem</c>'s re-entry rule from a stack scan to the dirty flag [:L663-L679], suppresses
    /// the per-item changed event [:L860] and makes both row entry points refuse to nest [:L1160].
    /// </summary>
    private bool _rowCalcing;

    /// <summary>
    /// <c>boolean _bRowCalcingEmpty</c> [:L106] - set for the duration of an empty-only pass, which
    /// restricts calculation to expressions whose bound column is currently empty [:L680].
    /// </summary>
    private bool _rowCalcingEmpty;

    /// <summary>
    /// <c>long _nCachedID</c> [:L108] - a monotonically increasing discriminator so two compute objects
    /// generated for the same column cannot collide [:L1885-L1886]. It is never reset, so a name is never
    /// reused within the life of the service.
    /// </summary>
    private long _cachedId;

    private readonly ColumnExpressionOptions _options;
    private readonly ExpressionSession _session;
    private readonly bool _ownsSession;
    private readonly DataWindowHandle _handle;
    private readonly ExpressionCalcStack _calcStack;
    private readonly MacroInvoker? _macroInvoker;
    private readonly IDataWindowSyntaxMutator? _syntaxMutator;
    private readonly ILogger? _logger;
    private DataWindowExpressionEvaluator? _evaluator;

    /// <summary>
    /// Guards <see cref="_errors"/>. The dialog the oracle raised was implicitly serialized by the message
    /// pump; a service has no such serialization, and two concurrent calculations on one engine would
    /// otherwise corrupt the list.
    /// </summary>
    private readonly object _errorLock = new();

    /// <summary>The retained error history - see <see cref="Errors"/> for why it is bounded.</summary>
    private readonly List<ExpressionParseError> _errors = [];

    /// <summary>The total raised count, including errors dropped past <see cref="MaxRetainedErrors"/>.</summary>
    private long _raisedErrorCount;

    /// <summary>
    /// Creates an engine. Every collaborator is optional; see the class remarks for why.
    /// </summary>
    /// <param name="options">
    /// The preserved legacy defaults - <c>Trace</c> off [:L98],
    /// <c>RedrawSuppressionRowThreshold</c> 200 [:L223] and <c>CalcStackInitialCapacity</c> 20 [:L2422].
    /// When omitted, the option type's own defaults apply, and they are those three values.
    /// </param>
    /// <param name="evaluator">
    /// The DataWindow expression evaluator. When omitted it is constructed over the host during
    /// <see cref="OnInit(DataWindowServiceHost)"/>, because the oracle's <c>_of_Evaluate</c> is a member
    /// of the service base and therefore cannot exist before the host does.
    /// </param>
    /// <param name="macroInvoker">
    /// The channel onto C-04's <c>InvokeMethodChannel</c>, which is how a macro function reaches the
    /// application that implements it [:L2263, :L2287]. When omitted, a macro function invocation is
    /// answered the way the oracle answers an unhandled event - see
    /// <see cref="InvokeMacroAsync"/>.
    /// </param>
    /// <param name="session">
    /// The expression session that scopes cross-DataWindow references. When omitted the engine creates
    /// and owns a private one, in which case it is the only DataWindow in it and a foreign reference to
    /// any other is BLOCKED by the session exactly as AAP section 0.6.2.3 requires.
    /// </param>
    /// <param name="syntaxMutator">
    /// The compute-cache seam. When omitted, the host is probed for it; when the host does not implement
    /// it either, the cache reports the oracle's own creation failure rather than pretending to work.
    /// </param>
    /// <param name="traceSink">
    /// Where trace records go. Honoured only when the engine owns its session; a caller-supplied session
    /// already has its own sink and this engine must not redirect it.
    /// </param>
    /// <param name="timeProvider">Clock, seamed for the determinism the characterization model needs.</param>
    /// <param name="logger">Diagnostics. Never receives expression text; see the note on
    /// <see cref="LogChannelUnavailable"/>.</param>
    public ColumnExpressionEngine(
        ColumnExpressionOptions? options = null,
        DataWindowExpressionEvaluator? evaluator = null,
        MacroInvoker? macroInvoker = null,
        ExpressionSession? session = null,
        IDataWindowSyntaxMutator? syntaxMutator = null,
        IExpressionTraceSink? traceSink = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null)
    {
        _options = options ?? new ColumnExpressionOptions();
        _evaluator = evaluator;
        _macroInvoker = macroInvoker;
        _syntaxMutator = syntaxMutator;
        _logger = logger;

        if (session is null)
        {
            // An owned session is constructed with tracing ENABLED at the transport level so that this
            // engine's own #Trace flag stays the single gate, which is what the oracle has: :L751 tests
            // #Trace and nothing else, and of_settrace [:L2409] can flip it at any time after
            // construction. A session built with Trace off would silently swallow a trace that :L751
            // decided to emit.
            ColumnExpressionOptions sessionOptions = new()
            {
                Trace = true,
                RedrawSuppressionRowThreshold = _options.RedrawSuppressionRowThreshold,
                CalcStackInitialCapacity = _options.CalcStackInitialCapacity,
                PageResolution = _options.PageResolution,
                PageRowsPerPage = _options.PageRowsPerPage,
            };

            _session = new ExpressionSession(
                Guid.NewGuid().ToString("N"),
                lifetime: null,
                columnExpression: sessionOptions,
                timeProvider: timeProvider,
                traceSink: traceSink,
                logger: logger);
            _ownsSession = true;
        }
        else
        {
            _session = session;
            _ownsSession = false;
        }

        // :L2421-L2422 - the calculation stack is created in the constructor and reserved for 20 entries.
        // The session owns it here, because the stack is per DataWindow and the session is what knows
        // which DataWindows are co-resident. Registration is also what mints this engine's handle, and
        // the handle is what every cross-DataWindow and trace call is keyed by.
        _handle = _session.Register(this);
        _calcStack = _session.CalcStackFor(_handle)
            ?? throw new InvalidOperationException(
                "The expression session accepted this engine's registration but produced no calculation "
                + "stack for the resulting handle. The stack is not optional: recursion detection "
                + "[n_cst_dwsvc_columnexp.sru:L682-L690] and the trace call stack [:L751-L758] both read "
                + "it, so continuing without one would silently disable both.");

        // :L98 declares `privatewrite boolean #Trace` with NO initializer, so PowerBuilder's default is
        // false. The option carries that default forward and lets deployment override it.
        Trace = _options.Trace;
    }

    /// <summary>
    /// <c>privatewrite boolean #Trace</c> [:L98] - public to read, private to write, with
    /// <see cref="SetTrace"/> [:L2409] as the only mutator. When set, every calculated item emits a trace
    /// carrying the call stack, the preprocessed expression and the value [:L751-L758].
    /// </summary>
    public bool Trace { get; private set; }

    /// <summary>
    /// The expression session this engine is registered in, owned or supplied. Cross-DataWindow
    /// references resolve through it, and it is the transport for traces.
    /// </summary>
    public ExpressionSession Session => _session;

    /// <summary>
    /// True when this engine created <see cref="Session"/> itself because none was supplied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OWNERSHIP DETERMINES WHO MAY CLOSE THE SESSION, AND THE ENGINE NEVER DOES. A supplied session belongs
    /// to whoever supplied it - in the service that is the session registry, which sweeps it on idle expiry -
    /// so closing it here would tear down a session other engines are still registered in. A private session,
    /// by contrast, has no registry entry, no timer and no unmanaged resource: it is in-memory state reachable
    /// only from this engine, so it is reclaimed with the engine and there is nothing to close. Either way the
    /// engine has no disposal obligation, which is why it is not disposable.
    /// </para>
    /// <para>
    /// A PRIVATE SESSION CANNOT RESOLVE A CROSS-DATAWINDOW REFERENCE TO ANY PEER, because no peer is
    /// registered in it - every foreign or context reference answers the session's BLOCKED code, exactly as
    /// AAP section 0.6.2.3 requires for a reference that spans sessions. That is the observable difference
    /// this property exposes, and it is why a caller that needs cross-DataWindow expansion must supply a
    /// shared session rather than letting each engine make its own.
    /// </para>
    /// </remarks>
    public bool OwnsSession => _ownsSession;

    /// <summary>
    /// This engine's handle within <see cref="Session"/> - the session-scoped identity that replaces the
    /// in-process object pointer <c>foreignvardata.expsvc</c> [:L82] carried, which is the one thing in
    /// this contract that could not be serialized (AAP section 0.6.2.3).
    /// </summary>
    public DataWindowHandle Handle => _handle;

    /// <summary>
    /// The expression evaluator, available once <see cref="OnInit(DataWindowServiceHost)"/> has run.
    /// </summary>
    /// <remarks>
    /// It already registers <c>dwValueToExp</c> and the five typed null producers in its own constructor,
    /// so the obligation at :L2390 - where <c>dwValueToExp</c> is called from INSIDE an expression string
    /// - is discharged there and is deliberately not duplicated here.
    /// </remarks>
    public DataWindowExpressionEvaluator? Evaluator => _evaluator;

    /// <summary>The number of bound expressions - <c>UpperBound(ColExpDatas)</c>.</summary>
    public int ExpressionCount => _colExpDatas.UpperBound;

    /// <summary>The global variable table, live. <c>ExpressionBinding.live_environment</c> on C-04.</summary>
    public ExpressionVariableEnvironment Variables => _globalVars;

    /// <summary>
    /// A snapshot of the bound expressions in index order, for projection onto C-04's
    /// <c>ColumnExpData</c> list.
    /// </summary>
    public ImmutableArray<ColumnExpressionData> Expressions => _colExpDatas.Items;

    /// <summary>
    /// The reverse dependency index in column-id order, for projection onto C-04's <c>ColumnData</c>
    /// list. Sparse by construction: an id that has never changed has no entry, which is
    /// <see cref="ColumnCalcFlag.CLC_UNKNOWN"/> by absence.
    /// </summary>
    public ImmutableArray<ColumnCalcData> ColumnCalcStates => _colDatas.Items;

    /// <summary>
    /// True while a whole-row pass is running - <c>_bRowCalcing</c> [:L105]. Exposed because it changes
    /// the meaning of a <see cref="RetCode.FAILED"/> from the calculation entry points [:L1160].
    /// </summary>
    public bool IsRowCalculating => _rowCalcing;

    /// <summary>True while an empty-only pass is running - <c>_bRowCalcingEmpty</c> [:L106].</summary>
    public bool IsEmptyRowCalculating => _rowCalcingEmpty;

    /// <summary>
    /// <c>event oninit(se_cst_dw dw)</c> [n_cst_dwsvc.sru:L85] - attach to the host, then complete the
    /// parts of construction that need one.
    /// </summary>
    /// <param name="dw">The attaching host.</param>
    public override void OnInit(DataWindowServiceHost dw)
    {
        base.OnInit(dw);

        // The oracle's _of_Evaluate is a member of the service base and reads #DataWindow directly
        // [n_cst_dwsvc.sru:L217], so an evaluator cannot exist before the host does. One supplied by the
        // caller is left alone: a test that built it over the same host has already made the choice.
        _evaluator ??= new DataWindowExpressionEvaluator(dw);
    }

    /// <summary>
    /// <c>event onenable</c> [:L2428-L2433] - ALWAYS ALLOWS, AND CHANGES NO SUBSCRIPTION.
    /// </summary>
    /// <param name="enabled">The requested state.</param>
    /// <returns>0 - which <c>of_setenabled</c> reads as "not vetoed" [n_cst_dwsvc.sru:L90].</returns>
    /// <remarks>
    /// THE ORACLE'S BODY IS ENTIRELY COMMENTED OUT AND IS CARRIED HERE AS INERT (C-B). What it would have
    /// done, verbatim from :L2428-L2432:
    /// <code>
    /// /*if enabled then
    ///     #DataWindow.of_On("!" + #DataWindow.EVT_ITEMCHANGED,this,"onItemChanged")
    /// else
    ///     #DataWindow.of_Off(this)
    /// end if*/
    /// </code>
    /// So the subscribe/unsubscribe pair is DORMANT: enabling or disabling the service does not attach or
    /// detach anything from the host's event broker, and the host reaches this engine by calling it
    /// directly [se_cst_dw.sru:L313-L314] rather than through a subscription. Reviving the block would
    /// double-dispatch every item change - the silent correction C-B forbids. It returns 0
    /// unconditionally, so enable is never vetoed.
    /// </remarks>
    protected override long OnEnable(bool enabled) => 0;

    /// <summary>
    /// <c>of_settrace(readonly boolean trace)</c> [:L2409-L2411] - the only mutator of
    /// <see cref="Trace"/>.
    /// </summary>
    /// <param name="trace">The requested state.</param>
    /// <returns><see cref="RetCode.OK"/>, unconditionally [:L2410].</returns>
    public long SetTrace(bool trace)
    {
        Trace = trace;

        // THE TRANSPORT GATE IS RAISED, NEVER LOWERED, so that #Trace stays the SINGLE gate the oracle
        // has. A session created by this engine already carries the flag [see the constructor], but a
        // SUPPLIED session may not - and a session with delivery switched off would silently swallow a
        // trace that :L751 decided to emit, turning of_settrace into a no-op with no diagnostic anywhere.
        // Raising it is safe because delivery is per record and gated again by each engine's own #Trace;
        // LOWERING it would not be, because the session is shared and a peer may still be tracing. So this
        // is deliberately monotone: no engine can silence another, and none can be silenced by one.
        if (trace)
        {
            _session.TraceEnabled = true;
        }

        return RetCode.OK;
    }

    /// <summary>
    /// The bind-time binding of one expression, or <see langword="null"/> when the index is out of range.
    /// This is C-04's <c>ExpressionBinding</c> for that expression.
    /// </summary>
    /// <param name="index">The one-based expression index.</param>
    public ExpressionBindingSnapshot? GetBinding(int index) =>
        _colExpDatas.IsValidIndex(index) && _expBindings.TryGetValue(_colExpDatas[index], out var binding)
            ? binding
            : null;

    /// <summary>
    /// The bind-time binding of one global variable expression, or <see langword="null"/> when the name
    /// is not a locally defined variable.
    /// </summary>
    /// <param name="name">The variable name, case-sensitive [:L997].</param>
    public ExpressionBindingSnapshot? GetVariableBinding(string? name) =>
        name is not null && _varBindings.TryGetValue(name, out var binding) ? binding : null;

    /// <summary>
    /// The UNEXPANDED source text of one expression - the field C-04 must transmit alongside
    /// <see cref="ColumnExpressionData.Exp"/>, and the empty string when the index is out of range.
    /// </summary>
    /// <param name="index">The one-based expression index.</param>
    public string GetSourceExp(int index) => GetBinding(index)?.UnexpandedExp ?? string.Empty;

    /// <summary>
    /// The three-bit sigil model of one retained variable reference, ready for
    /// <see cref="ExpansionSigils.ProjectVariableReference"/> to fold onto C-04's
    /// <c>ExpansionMode</c>. See section 2 of the header: the bits are kept internally and projected only
    /// at the boundary.
    /// </summary>
    /// <param name="reference">The reference to describe.</param>
    /// <remarks>
    /// The dynamic bit is NOT a field of <c>vardata</c> [:L50-L56]; it is recoverable from
    /// <see cref="VariableReference.FullName"/>, which the scanner captured WITH ITS SIGILS [:L1412], by
    /// skipping an optional <c>@</c> and testing for a doubled <c>$</c>. For a reference that SURVIVED
    /// parsing the answer is always dynamic, because the static branch substitutes and never appends
    /// [:L1415 against :L1477]; deriving it rather than asserting it keeps the projection correct for a
    /// reference rebuilt from a wire payload too.
    /// </remarks>
    public static ExpansionSigils SigilsFor(VariableReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return new ExpansionSigils(
            IsCtx: reference.IsCtx,
            IsMacro: reference.IsMacro,
            IsDynamic: reference.IsMacro && HasDoubledSigil(reference.FullName));
    }

    private static bool HasDoubledSigil(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
        {
            return false;
        }

        // :L1297-L1300 - the context sigil precedes the macro sigil, and the doubling test is applied
        // after the '@' has been stepped over.
        int offset = fullName[0] == '@' ? 1 : 0;

        return fullName.Length > offset + 1
            && fullName[offset] == '$'
            && fullName[offset + 1] == '$';
    }


    // =================================================================================================
    //  EXPRESSION REGISTRATION - of_addexp, of_setexp, of_getexp, of_remove, of_removeall
    // =================================================================================================

    /// <summary>
    /// <c>of_addexp(string colname, string exp)</c> [:L1516-L1582] - bind a new expression to a column.
    /// </summary>
    /// <param name="colname">The column name; normalised to lower case and trimmed [:L1538].</param>
    /// <param name="exp">The expression source. Parsed, and REWRITTEN before it is stored.</param>
    /// <returns>
    /// THE NEW ONE-BASED INDEX ON SUCCESS - not a return code [:L1581]. The oracle's signature is
    /// <c>public function integer</c> and its own caller tests <c>if rtCode &gt; 0</c> [:L1699], so a
    /// negative answer is a <see cref="RetCode"/> and a positive one is an index. Preserved exactly,
    /// because the index is how every other entry point addresses the expression afterwards.
    /// </returns>
    /// <remarks>
    /// A COLUMN MAY CARRY SEVERAL EXPRESSIONS. This method always appends; it never replaces. That is the
    /// documented multiple-expressions-per-column capability
    /// [docs/n_cst_dwsvc_columnexp.md:L8], and the duplicate group is rebuilt at the end so every member
    /// knows its siblings.
    /// </remarks>
    public int AddExp(string? colname, string? exp)
    {
        // :L1538 - the name is lower-cased and trimmed, and that normalised form is what is stored and
        // what every lookup compares against [:L1131].
        string columnName = (colname ?? string.Empty).Trim().ToLowerInvariant();
        string expression = exp ?? string.Empty;

        // :L1539 - both empty tests are on the NORMALISED name and the RAW expression.
        if (columnName.Length == 0 || expression.Length == 0)
        {
            return (int)RetCode.E_INVALID_ARGUMENT;
        }

        DataWindowServiceHost host = RequireHost();
        ColumnExpressionData expData = new();

        // :L1541-L1542 - Describe answers "!" or "?" for an unknown property, and Long of either is 0,
        // which is the oracle's own "no such column" test. DescribeLong reproduces that coercion.
        expData.Id = DescribeLong(host, columnName + ".ID");
        if (expData.Id == 0)
        {
            return (int)RetCode.E_INVALID_ARGUMENT;
        }

        expData.Name = columnName;
        expData.Dwo = GetDataWindowObject(columnName);
        expData.ColType = GetColumnType(columnName);

        // :L1546-L1552 - three edit styles can declare that an empty string means null, and the oracle
        // tests them in this order with an else-if chain, so the first "yes" wins and the rest are not
        // consulted. The flag stays false when none of the three says yes.
        if (string.Equals(host.Describe(columnName + ".edit.NilIsNull"), "yes", StringComparison.Ordinal)
            || string.Equals(
                host.Describe(columnName + ".dddw.NilIsNull"), "yes", StringComparison.Ordinal)
            || string.Equals(
                host.Describe(columnName + ".ddlb.NilIsNull"), "yes", StringComparison.Ordinal))
        {
            expData.EmptyStringIsNull = true;
        }

        // :L1554 - IsFailed, not "not OK". The distinction matters: the parse routine answers
        // RetCode.FAILED from one arm [:L1432] and E_INVALID_ARGUMENT from the others, and IsFailed is
        // true for both while it is FALSE for a cancellation, so the predicate is doing real work here.
        // The oracle then flattens every parse failure to E_INVALID_ARGUMENT regardless of which arm
        // raised it - the specific code reaches the caller through the structured error, not through this
        // return value.
        ParseOutcome parse = ParseExp(expression);
        if (Predicates.IsFailed(parse.ReturnCode))
        {
            return (int)RetCode.E_INVALID_ARGUMENT;
        }

        // :L1556-L1557 - the REWRITTEN text is stored, and hasMacro is derived from what the parse
        // retained rather than from the source text.
        expData.Exp = parse.Expression;
        expData.Vars.Assign(parse.Vars);
        expData.Fns.Assign(parse.Fns);
        expData.HasMacro = expData.Vars.UpperBound > 0 || expData.Fns.UpperBound > 0;

        // :L1559-L1560.
        int index = _colExpDatas.Append(expData);
        _expBindings[expData] = parse.ToBinding(expression);

        RebuildDuplicateGroups(columnName, descending: true);

        // :L1579 - the reverse dependency index is discarded wholesale, because one new expression can
        // add an edge from any column.
        ResetColumns();

        return index;
    }

    /// <summary>
    /// <c>of_setexp(readonly integer index, string exp, readonly boolean recalc)</c> [:L1632-L1689] -
    /// replace a bound expression, optionally recalculating every row.
    /// </summary>
    /// <param name="index">The one-based expression index.</param>
    /// <param name="exp">The new expression source.</param>
    /// <param name="recalc">When true, recalculate this expression on every row [:L1678-L1686].</param>
    /// <param name="cancellationToken">Cancels the recalculation pass.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/>, <see cref="RetCode.E_OUT_OF_BOUND"/> or
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/>.
    /// </returns>
    /// <remarks>
    /// THE NO-CHANGE SHORT-CIRCUIT COMPARES THE INCOMING RAW TEXT AGAINST THE STORED REWRITTEN TEXT
    /// [:L1655], and that is reproduced rather than corrected (C-B, defect k). Once an expression has had
    /// a static reference expanded, the stored text no longer resembles the source, so re-setting the
    /// identical source string never matches and always re-parses. That is observable and it is load
    /// bearing in the other direction too: it is what lets a caller re-bind after changing a variable's
    /// value in order to pick up a NEW static snapshot.
    /// </remarks>
    public async ValueTask<long> SetExpAsync(
        int index,
        string? exp,
        bool recalc,
        CancellationToken cancellationToken = default)
    {
        if (!_colExpDatas.IsValidIndex(index))
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        string expression = exp ?? string.Empty;
        if (expression.Length == 0)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        ColumnExpressionData data = _colExpDatas[index];

        // :L1655 - see the remarks. Ordinal comparison, because the oracle compares PowerScript strings
        // and an expression is not culture-sensitive text.
        if (string.Equals(data.Exp, expression, StringComparison.Ordinal))
        {
            return RetCode.OK;
        }

        // :L1657 - on failure the oracle returns BEFORE assigning vars and fns, so the previous
        // references survive intact. ParseOutcome makes that explicit: nothing is written back until the
        // parse has succeeded.
        ParseOutcome parse = ParseExp(expression);
        if (Predicates.IsFailed(parse.ReturnCode))
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        data.Vars.Assign(parse.Vars);
        data.Fns.Assign(parse.Fns);
        data.Exp = parse.Expression;
        data.HasMacro = data.Vars.UpperBound > 0 || data.Fns.UpperBound > 0;
        _expBindings[data] = parse.ToBinding(expression);

        // :L1661-L1674 - the compute cache has to follow the expression. Three arms, in the oracle's
        // order: a macro makes the expression ineligible so the compute is destroyed outright; an
        // existing compute is re-pointed at the new expression; a missing one is created.
        if (data.Cacheable)
        {
            if (data.HasMacro)
            {
                ModifySyntax("Destroy " + data.ComputeName);
            }
            else
            {
                string errInfo;
                if (HasDataWindowObject(data.ComputeName))
                {
                    errInfo = ModifySyntax(
                        data.ComputeName + ".expression=\"" + data.Exp + "\"");
                }
                else
                {
                    CreateCompute(data.ComputeName, data.Exp, out errInfo);
                }

                // :L1670-L1672 - reported and then IGNORED. The oracle shows the dialog and carries on to
                // ResetColumns and to the recalculation, so a cache that failed to update does not fail
                // the call. Reproduced: the structured error is raised and the method continues.
                if (errInfo.Length != 0)
                {
                    RaiseError(
                        ParseErrorFormatter.CreatePlainError(
                            ExpressionErrorSite.UpdateExpressionCacheFailed,
                            data.Name,
                            data.Exp,
                            errInfo));
                }
            }
        }

        ResetColumns();

        // :L1678-L1686 - three conditions, all required. Note it does NOT set _bRowCalcing, so each item
        // calculation goes through the stack-based re-entry rule rather than the dirty flag.
        if (Enabled && !_rowCalcing && recalc)
        {
            long rowCount = RequireHost().RowCount();
            if (rowCount > 0)
            {
                for (long row = 1; row <= rowCount; row++)
                {
                    await _of_calcitem(row, index, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_setexp(readonly string colname, readonly string exp, readonly boolean recalc)</c>
    /// [:L1691-L1702] - set by column name, ADDING the expression when the column has none.
    /// </summary>
    /// <param name="colname">The column name.</param>
    /// <param name="exp">The new expression source.</param>
    /// <param name="recalc">When true, recalculate this expression on every row.</param>
    /// <param name="cancellationToken">Cancels the recalculation pass.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> when it set or added successfully, otherwise the failure code.
    /// </returns>
    /// <remarks>
    /// THE ADD PATH NORMALISES A POSITIVE INDEX TO <see cref="RetCode.OK"/> [:L1699] - which is why this
    /// overload answers a return code while <see cref="AddExp"/> answers an index. Two consequences worth
    /// stating: a caller of this overload cannot learn the new index, and the add path ignores
    /// <paramref name="recalc"/> entirely because <see cref="AddExp"/> takes no such argument.
    /// </remarks>
    public async ValueTask<long> SetExpAsync(
        string? colname,
        string? exp,
        bool recalc,
        CancellationToken cancellationToken = default)
    {
        int index = FindExpIndex(colname);
        if (index > 0)
        {
            return await SetExpAsync(index, exp, recalc, cancellationToken).ConfigureAwait(false);
        }

        long rtCode = AddExp(colname, exp);
        return rtCode > 0 ? RetCode.OK : rtCode;
    }

    /// <summary>
    /// <c>of_setexp(readonly integer index, readonly string exp)</c> [:L1704] - the two-argument arity,
    /// which does NOT recalculate.
    /// </summary>
    /// <param name="index">The one-based expression index.</param>
    /// <param name="exp">The new expression source.</param>
    /// <param name="cancellationToken">Threaded through for signature symmetry with the other arities.</param>
    public ValueTask<long> SetExpAsync(
        int index,
        string? exp,
        CancellationToken cancellationToken = default) =>
        SetExpAsync(index, exp, false, cancellationToken);

    /// <summary>
    /// <c>of_setexp(readonly string colname, readonly string exp)</c> [:L1707] - the two-argument arity
    /// by name, which does NOT recalculate.
    /// </summary>
    /// <param name="colname">The column name.</param>
    /// <param name="exp">The new expression source.</param>
    /// <param name="cancellationToken">Threaded through for signature symmetry.</param>
    public ValueTask<long> SetExpAsync(
        string? colname,
        string? exp,
        CancellationToken cancellationToken = default) =>
        SetExpAsync(colname, exp, false, cancellationToken);

    /// <summary>
    /// <c>of_getexp(readonly integer index)</c> [:L419-L439] - the STORED, REWRITTEN expression, or the
    /// empty string when the index is out of range [:L436].
    /// </summary>
    /// <param name="index">The one-based expression index.</param>
    /// <remarks>
    /// This answers <see cref="ColumnExpressionData.Exp"/>, so a static reference is already gone from
    /// it. <see cref="GetSourceExp"/> answers the text the caller supplied. Both are on C-04 and they are
    /// not interchangeable - see section 3 of the header.
    /// </remarks>
    public string GetExp(int index) =>
        _colExpDatas.IsValidIndex(index) ? _colExpDatas[index].Exp : string.Empty;

    /// <summary>
    /// <c>of_getexp(string colname)</c> [:L400] - by column name. An unknown name resolves to index 0,
    /// which the index overload answers with the empty string.
    /// </summary>
    /// <param name="colname">The column name.</param>
    public string GetExp(string? colname) => GetExp(FindExpIndex(colname));

    /// <summary>
    /// <c>of_remove(readonly integer index)</c> [:L441-L498] - unbind one expression.
    /// </summary>
    /// <param name="index">The one-based expression index.</param>
    /// <returns><see cref="RetCode.OK"/> or <see cref="RetCode.E_OUT_OF_BOUND"/> [:L463].</returns>
    /// <remarks>
    /// REMOVAL RE-INDEXES EVERYTHING AFTER THE REMOVED ENTRY, which is why the duplicate groups are
    /// rebuilt from scratch afterwards [:L483-L493] and why the bind-time snapshots here are keyed by
    /// entry identity rather than by index.
    /// </remarks>
    public long Remove(int index)
    {
        if (!_colExpDatas.IsValidIndex(index))
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        // :L465 - the name is captured BEFORE the rebuild, because it is the key the duplicate group is
        // recomputed from.
        string name = _colExpDatas[index].Name;
        ColumnExpressionData removed = _colExpDatas[index];

        // :L467-L479 - the list is rebuilt without the removed entry, and the removed entry's compute
        // object is destroyed on the way past. The oracle collects the surviving duplicate indexes during
        // the same walk, in ASCENDING order, and against the NEW numbering [:L476].
        List<ColumnExpressionData> survivors = [];
        for (int scan = 1; scan <= _colExpDatas.UpperBound; scan++)
        {
            ColumnExpressionData entry = _colExpDatas[scan];

            if (scan == index)
            {
                if (entry.Cacheable)
                {
                    ModifySyntax("Destroy " + entry.ComputeName);
                }

                continue;
            }

            survivors.Add(entry);
        }

        _colExpDatas.Assign(survivors);
        _expBindings.Remove(removed);

        RebuildDuplicateGroups(name, descending: false);

        ResetColumns();

        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_remove(readonly string colname)</c> [:L500] - by column name. An unknown name resolves to
    /// index 0 and the index overload answers <see cref="RetCode.E_OUT_OF_BOUND"/>.
    /// </summary>
    /// <param name="colname">The column name.</param>
    /// <remarks>
    /// WHEN A COLUMN CARRIES SEVERAL EXPRESSIONS THIS REMOVES EXACTLY ONE - the LAST, because the lookup
    /// scans in reverse [:L1132]. Reproduced; a caller clearing a duplicate group must call this once per
    /// member.
    /// </remarks>
    public long Remove(string? colname) => Remove(FindExpIndex(colname));

    /// <summary>
    /// <c>of_removeall()</c> [:L359-L390] - unbind every expression.
    /// </summary>
    /// <returns><see cref="RetCode.OK"/> [:L389].</returns>
    /// <remarks>
    /// THE GLOBAL VARIABLE TABLE IS NOT TOUCHED. The oracle clears <c>ColExpDatas</c> and resets the
    /// column index, and leaves <c>GlobalVars</c> exactly as it was [:L378-L387] - so variables defined
    /// before a wholesale removal are still defined afterwards, and a subsequent static expansion still
    /// finds them. Reproduced verbatim.
    /// </remarks>
    public long RemoveAll()
    {
        // :L378-L383 - every cached compute object is destroyed first, one Modify per cacheable entry.
        for (int index = 1; index <= _colExpDatas.UpperBound; index++)
        {
            ColumnExpressionData entry = _colExpDatas[index];
            if (entry.Cacheable)
            {
                ModifySyntax("Destroy " + entry.ComputeName);
            }
        }

        _colExpDatas.Clear();
        _expBindings.Clear();

        ResetColumns();

        return RetCode.OK;
    }

    /// <summary>
    /// <c>_of_findexpindex(string colname)</c> [:L1129-L1137] - the one-based index of the expression
    /// bound to a column, or 0 when there is none [:L1136].
    /// </summary>
    /// <param name="colname">The column name; normalised the same way registration normalises it.</param>
    /// <remarks>
    /// THE SCAN RUNS IN REVERSE [:L1132], so when a column carries several expressions the LAST
    /// registered one wins. That is observable through every by-name entry point, and it is the reason
    /// the by-name overloads cannot address an arbitrary member of a duplicate group.
    /// </remarks>
    public int FindExpIndex(string? colname)
    {
        string columnName = (colname ?? string.Empty).Trim().ToLowerInvariant();

        return _colExpDatas.IndexOf(
            entry => string.Equals(entry.Name, columnName, StringComparison.Ordinal),
            reverse: true);
    }

    /// <summary>
    /// <c>_of_resetcolumns()</c> [:L1133-L1136] - discard the whole reverse dependency index.
    /// </summary>
    /// <remarks>
    /// WHOLESALE, NOT PER COLUMN, AND THAT IS DELIBERATE IN THE ORACLE. Every structural mutation calls
    /// it - registration [:L1579], removal [:L495], relative-column changes [:L585, :L633], the
    /// always-calculate flag [:L526], variable definition [:L1627] and foreign-variable linking [:L2143] -
    /// because a single new edge can invalidate any column's cached answer. The cost is that the next
    /// change to each column re-walks every expression once [:L238-L293].
    /// </remarks>
    private void ResetColumns() => _colDatas.Clear();

    /// <summary>
    /// <c>_of_makedirty()</c> [:L392-L398] - mark every expression as needing calculation in the current
    /// row pass.
    /// </summary>
    private void MakeDirty()
    {
        for (int index = 1; index <= _colExpDatas.UpperBound; index++)
        {
            _colExpDatas[index].Dirty = true;
        }
    }

    /// <summary>
    /// Rebuilds the duplicate groups for every expression bound to <paramref name="columnName"/>.
    /// </summary>
    /// <param name="columnName">The normalised column name whose group changed.</param>
    /// <param name="descending">
    /// True to collect members from the highest index down, as registration does [:L1562]; false to
    /// collect them from the lowest up, as removal does [:L468]. THE TWO SITES GENUINELY DIFFER and the
    /// difference is observable in <see cref="ColumnExpressionData.DupExps"/>' ordering, so it is a
    /// parameter rather than a normalised single order.
    /// </param>
    /// <remarks>
    /// <para>
    /// EVERY MEMBER RECEIVES THE SAME GROUP MINUS ITSELF [:L1569-L1576, :L485-L492]: the inner loop skips
    /// only the member currently being written, so a group of three leaves each member holding the other
    /// two.
    /// </para>
    /// <para>
    /// PRESERVED DEFECT: THE REBUILD IS GATED ON <c>nCount &gt; 1</c> [:L484, :L1568], so when a removal
    /// leaves exactly ONE expression on the column, that survivor KEEPS ITS STALE GROUP LIST pointing at
    /// an index that no longer belongs to the group. Nothing clears it. The consequence is confined to
    /// dirty-flag clearing [:L694-L697, :L2023] rather than to value selection, which is why it has
    /// survived; it is reproduced rather than corrected (C-B).
    /// </para>
    /// </remarks>
    private void RebuildDuplicateGroups(string columnName, bool descending)
    {
        List<long> all = [];

        if (descending)
        {
            for (int scan = _colExpDatas.UpperBound; scan >= 1; scan--)
            {
                if (string.Equals(_colExpDatas[scan].Name, columnName, StringComparison.Ordinal))
                {
                    all.Add(scan);
                }
            }
        }
        else
        {
            for (int scan = 1; scan <= _colExpDatas.UpperBound; scan++)
            {
                if (string.Equals(_colExpDatas[scan].Name, columnName, StringComparison.Ordinal))
                {
                    all.Add(scan);
                }
            }
        }

        // The gate that leaves a lone survivor's list stale - see the remarks.
        if (all.Count <= 1)
        {
            return;
        }

        foreach (long member in all)
        {
            List<long> group = [];
            foreach (long candidate in all)
            {
                if (candidate == member)
                {
                    continue;
                }

                group.Add(candidate);
            }

            _colExpDatas[(int)member].DupExps.Assign(group);
        }
    }


    // =================================================================================================
    //  TRIGGER CONDITIONS AND FLAGS - the four flags C-04 exposes as FLAG_* on SetExpressionFlag,
    //  plus the relative-column setters, each in both addressing modes and both arities.
    // =================================================================================================

    /// <summary>
    /// <c>of_setrelativecolumns(readonly integer index, readonly string relativecols[])</c>
    /// [:L552-L587] - the columns whose change triggers this expression.
    /// </summary>
    /// <param name="index">The one-based expression index.</param>
    /// <param name="relativecols">The triggering column names. An empty array clears the list.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/>, <see cref="RetCode.E_OUT_OF_BOUND"/> for a bad index [:L554], or
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> for a name the DataWindow does not know [:L559].
    /// </returns>
    /// <remarks>
    /// THE VALIDATION IS ALL-OR-NOTHING. Every name is resolved to a column id BEFORE anything is stored,
    /// and the first unresolvable name returns immediately [:L557-L561], leaving the previous list intact.
    /// Reproduced, because a partially applied trigger list would silently change which columns
    /// recalculate.
    /// </remarks>
    public long SetRelativeColumns(int index, IReadOnlyList<string>? relativecols)
    {
        if (!_colExpDatas.IsValidIndex(index))
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        if (!TryResolveColumnIds(relativecols, out List<long> columnIds))
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        _colExpDatas[index].RelativeColIds.Assign(columnIds);

        ResetColumns();

        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_setrelativecolumns(readonly string colname, readonly string relativecols[])</c> [:L589].
    /// </summary>
    /// <param name="colname">The bound column's name.</param>
    /// <param name="relativecols">The triggering column names.</param>
    public long SetRelativeColumns(string? colname, IReadOnlyList<string>? relativecols) =>
        SetRelativeColumns(FindExpIndex(colname), relativecols);

    /// <summary>
    /// <c>of_setrelativecolumn(readonly integer index, readonly string relativecol)</c> [:L592-L599] -
    /// the singular arity.
    /// </summary>
    /// <param name="index">The one-based expression index.</param>
    /// <param name="relativecol">
    /// The single triggering column name. THE EMPTY STRING CLEARS THE LIST rather than adding an empty
    /// entry, because the oracle only writes element 1 when the name is non-empty [:L594-L596].
    /// </param>
    public long SetRelativeColumn(int index, string? relativecol) =>
        SetRelativeColumns(index, SingleOrEmpty(relativecol));

    /// <summary>
    /// <c>of_setrelativecolumn(readonly string colname, readonly string relativecol)</c> [:L403-L409].
    /// </summary>
    /// <param name="colname">The bound column's name.</param>
    /// <param name="relativecol">The single triggering column name, or the empty string to clear.</param>
    public long SetRelativeColumn(string? colname, string? relativecol) =>
        SetRelativeColumns(FindExpIndex(colname), SingleOrEmpty(relativecol));

    /// <summary>
    /// <c>of_setrelativeinputcolumns(readonly integer index, readonly string relativecols[])</c>
    /// [:L600-L635] - the columns whose change triggers this expression ONLY WHEN THE USER TYPED IT.
    /// </summary>
    /// <param name="index">The one-based expression index.</param>
    /// <param name="relativecols">The triggering column names.</param>
    /// <returns>As <see cref="SetRelativeColumns(int, IReadOnlyList{string})"/>.</returns>
    /// <remarks>
    /// The body is identical to the non-input setter except for the field it writes [:L611 against
    /// :L583]; the difference in BEHAVIOUR lives entirely in the dispatch filter at :L301-L315, which
    /// consults the <c>frominput</c> flag the host passes. The legacy specification states the
    /// distinction directly [docs/n_cst_dwsvc_columnexp.md:L162-L163].
    /// </remarks>
    public long SetRelativeInputColumns(int index, IReadOnlyList<string>? relativecols)
    {
        if (!_colExpDatas.IsValidIndex(index))
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        if (!TryResolveColumnIds(relativecols, out List<long> columnIds))
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        _colExpDatas[index].RelativeInputColIds.Assign(columnIds);

        ResetColumns();

        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_setrelativeinputcolumns(readonly string colname, readonly string relativecols[])</c> [:L637].
    /// </summary>
    /// <param name="colname">The bound column's name.</param>
    /// <param name="relativecols">The triggering column names.</param>
    public long SetRelativeInputColumns(string? colname, IReadOnlyList<string>? relativecols) =>
        SetRelativeInputColumns(FindExpIndex(colname), relativecols);

    /// <summary>
    /// <c>of_setrelativeinputcolumn(readonly integer index, readonly string relativecol)</c> [:L640-L647].
    /// </summary>
    /// <param name="index">The one-based expression index.</param>
    /// <param name="relativecol">The single triggering column name, or the empty string to clear.</param>
    public long SetRelativeInputColumn(int index, string? relativecol) =>
        SetRelativeInputColumns(index, SingleOrEmpty(relativecol));

    /// <summary>
    /// <c>of_setrelativeinputcolumn(readonly string colname, readonly string relativecol)</c> [:L411-L417].
    /// </summary>
    /// <param name="colname">The bound column's name.</param>
    /// <param name="relativecol">The single triggering column name, or the empty string to clear.</param>
    public long SetRelativeInputColumn(string? colname, string? relativecol) =>
        SetRelativeInputColumns(FindExpIndex(colname), SingleOrEmpty(relativecol));

    /// <summary>
    /// <c>of_setalwayscalc(readonly integer index, readonly boolean alwayscalc)</c> [:L503-L529] -
    /// recalculate on ANY column change.
    /// </summary>
    /// <param name="index">The one-based expression index.</param>
    /// <param name="alwayscalc">The requested state.</param>
    /// <returns><see cref="RetCode.OK"/> or <see cref="RetCode.E_OUT_OF_BOUND"/>.</returns>
    /// <remarks>
    /// THIS FLAG DISCARDS THE REVERSE INDEX AND THE OTHER THREE DO NOT [:L526 against :L921, :L968,
    /// :L1895]. That asymmetry is correct rather than an oversight: always-calculate changes which
    /// expressions a column drives, so the cached graph becomes wrong, whereas recursive, trigger-event and
    /// cacheable change only what happens once an expression has already been selected. The no-change
    /// short-circuit [:L522] is what keeps a redundant set from throwing the graph away.
    /// </remarks>
    public long SetAlwaysCalc(int index, bool alwayscalc)
    {
        if (!_colExpDatas.IsValidIndex(index))
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        ColumnExpressionData data = _colExpDatas[index];
        if (data.AlwaysCalc == alwayscalc)
        {
            return RetCode.OK;
        }

        data.AlwaysCalc = alwayscalc;

        ResetColumns();

        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_setalwayscalc(readonly string colname, readonly boolean alwayscalc)</c> [:L531-L546].
    /// </summary>
    /// <param name="colname">The bound column's name.</param>
    /// <param name="alwayscalc">The requested state.</param>
    public long SetAlwaysCalc(string? colname, bool alwayscalc) =>
        SetAlwaysCalc(FindExpIndex(colname), alwayscalc);

    /// <summary>
    /// <c>of_setrecursive(readonly integer index, readonly boolean recursive)</c> [:L900-L924] - permit
    /// this expression to re-enter itself.
    /// </summary>
    /// <param name="index">The one-based expression index.</param>
    /// <param name="recursive">The requested state.</param>
    /// <returns><see cref="RetCode.OK"/> or <see cref="RetCode.E_OUT_OF_BOUND"/>.</returns>
    /// <remarks>
    /// No reverse-index reset - see the remarks on <see cref="SetAlwaysCalc(int, bool)"/>. The flag is
    /// read by the stack scan at :L683, so it only matters once the expression is already being
    /// calculated.
    /// </remarks>
    public long SetRecursive(int index, bool recursive)
    {
        if (!_colExpDatas.IsValidIndex(index))
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        ColumnExpressionData data = _colExpDatas[index];
        if (data.Recursive == recursive)
        {
            return RetCode.OK;
        }

        data.Recursive = recursive;

        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_setrecursive(readonly string colname, readonly boolean recursive)</c> [:L926-L945].
    /// </summary>
    /// <param name="colname">The bound column's name.</param>
    /// <param name="recursive">The requested state.</param>
    public long SetRecursive(string? colname, bool recursive) =>
        SetRecursive(FindExpIndex(colname), recursive);

    /// <summary>
    /// <c>of_settriggerevent(readonly integer index, readonly boolean btrigger)</c> [:L947-L971] - raise
    /// the host's ItemChanged event before writing a calculated value, and honour its veto.
    /// </summary>
    /// <param name="index">The one-based expression index.</param>
    /// <param name="btrigger">The requested state.</param>
    /// <returns><see cref="RetCode.OK"/> or <see cref="RetCode.E_OUT_OF_BOUND"/>.</returns>
    /// <remarks>
    /// NOT A DIAGNOSTIC FLAG. When set, a non-zero answer from the host's ItemChanged event ABANDONS THE
    /// WRITE [:L783-L785 and the five sibling arms], so the calculated value is discarded and the column
    /// keeps its old one. Turning it on therefore changes results, not just notifications.
    /// </remarks>
    public long SetTriggerEvent(int index, bool btrigger)
    {
        if (!_colExpDatas.IsValidIndex(index))
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        ColumnExpressionData data = _colExpDatas[index];
        if (data.TriggerEvent == btrigger)
        {
            return RetCode.OK;
        }

        data.TriggerEvent = btrigger;

        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_settriggerevent(readonly string colname, readonly boolean btrigger)</c> [:L973-L992].
    /// </summary>
    /// <param name="colname">The bound column's name.</param>
    /// <param name="btrigger">The requested state.</param>
    public long SetTriggerEvent(string? colname, bool btrigger) =>
        SetTriggerEvent(FindExpIndex(colname), btrigger);

    /// <summary>
    /// <c>of_setcacheable(readonly integer index, readonly boolean cache)</c> [:L1857-L1898] - back this
    /// expression with a generated DataWindow compute object instead of evaluating it each time.
    /// </summary>
    /// <param name="index">The one-based expression index.</param>
    /// <param name="cache">The requested state.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/>, <see cref="RetCode.E_OUT_OF_BOUND"/> for a bad index, or
    /// <see cref="RetCode.E_NO_SUPPORT"/> when the expression contains a macro [:L1883].
    /// </returns>
    /// <remarks>
    /// <para>
    /// A MACRO MAKES AN EXPRESSION UNCACHEABLE, and the refusal is a hard one: a compute object is
    /// evaluated by the DataWindow engine itself, which knows nothing about this service's variables or
    /// its application callbacks, so a cached macro expression could not be expanded at all.
    /// </para>
    /// <para>
    /// PRESERVED BEHAVIOUR: A FAILED CREATION STILL TURNS THE FLAG ON. :L1887-L1889 reports the failure
    /// and then falls through to :L1895, so the expression is marked cacheable with no compute object
    /// behind it. That is not left broken by accident - the calculation path notices the missing object
    /// and creates it on the next pass [:L706-L712] - and reproducing it is what keeps that recovery path
    /// reachable.
    /// </para>
    /// <para>
    /// THE GENERATED NAME CARRIES A DOUBLE UNDERSCORE [:L1886], because <see cref="DWOSUFFIX"/> already
    /// begins with one. It is observable in <see cref="ColumnExpressionData.ComputeName"/> and on C-04, so
    /// it is reproduced exactly (C-B, defect l).
    /// </para>
    /// </remarks>
    public long SetCacheable(int index, bool cache)
    {
        if (!_colExpDatas.IsValidIndex(index))
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        ColumnExpressionData data = _colExpDatas[index];
        if (data.Cacheable == cache)
        {
            return RetCode.OK;
        }

        if (cache)
        {
            if (data.HasMacro)
            {
                RaiseError(
                    ParseErrorFormatter.CreatePlainError(
                        ExpressionErrorSite.CreateExpressionCacheMacroExpansionUnsupported,
                        data.Name));
                return RetCode.E_NO_SUPPORT;
            }

            // :L1885-L1886 - the discriminator increments FIRST and is never reset, so no two compute
            // objects generated by this service can collide even across repeated enable/disable cycles.
            _cachedId++;
            data.ComputeName = data.Name
                + "_"
                + _cachedId.ToString(CultureInfo.InvariantCulture)
                + "_"
                + DWOSUFFIX;

            if (!CreateCompute(data.ComputeName, data.Exp, out string errInfo))
            {
                RaiseError(
                    ParseErrorFormatter.CreatePlainError(
                        ExpressionErrorSite.CreateExpressionCacheFailedOnCacheEnable,
                        data.Name,
                        data.Exp,
                        errInfo));

                // NO RETURN HERE - see the remarks. The oracle falls through and sets the flag.
            }
        }
        else
        {
            ModifySyntax("Destroy " + data.ComputeName);
            data.ComputeName = string.Empty;
        }

        data.Cacheable = cache;

        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_setcacheable(readonly string colname, readonly boolean cache)</c> [:L1900-L1919].
    /// </summary>
    /// <param name="colname">The bound column's name.</param>
    /// <param name="cache">The requested state.</param>
    public long SetCacheable(string? colname, bool cache) =>
        SetCacheable(FindExpIndex(colname), cache);

    /// <summary>
    /// Resolves every name to a one-based column id, exactly as :L557-L561 does, answering false on the
    /// first name the DataWindow does not know.
    /// </summary>
    /// <param name="columnNames">The names to resolve; null or empty yields an empty list.</param>
    /// <param name="columnIds">The resolved ids, in order.</param>
    private bool TryResolveColumnIds(
        IReadOnlyList<string>? columnNames,
        out List<long> columnIds)
    {
        columnIds = [];

        if (columnNames is null || columnNames.Count == 0)
        {
            return true;
        }

        DataWindowServiceHost host = RequireHost();

        for (int i = 0; i < columnNames.Count; i++)
        {
            long columnId = DescribeLong(host, columnNames[i] + ".ID");
            if (columnId == 0)
            {
                return false;
            }

            columnIds.Add(columnId);
        }

        return true;
    }

    /// <summary>
    /// The singular arity's array construction [:L594-L596, :L405-L407]: a one-element array for a
    /// non-empty name, and an EMPTY array for the empty string.
    /// </summary>
    /// <param name="value">The candidate name.</param>
    private static IReadOnlyList<string> SingleOrEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? [] : [value];


    // =================================================================================================
    //  GLOBAL VARIABLES - of_addvar (7 typed overloads), of_setvar (7 types x 3 arities),
    //  of_addvarexp, of_setvarexp (3 arities), of_getvarexp, of_addforeignvar.
    // =================================================================================================
    //
    //  EVERY of_addvar OVERLOAD IS A ONE-LINE ADAPTER OVER of_addvarexp [:L1091-L1110], and every
    //  of_setvar overload is the same adapter over of_setvarexp [:L1112-L1130, :L1713-L1731,
    //  :L1779-L1798]. A variable HAS NO TYPED STORAGE: it is stored as the TEXT of a DataWindow
    //  expression, and the typed entry points exist only to render a value into that text. That is why
    //  the seven types are seven overloads rather than a discriminated value, and why the rendering forms
    //  matter - `Time('...')`, `'...'`, `DateTime('...')`, `Date('...')` and the bare numeric - are
    //  spliced verbatim into expressions the DataWindow engine must then parse.
    //
    //  THE SIX NON-BOOLEAN RENDERINGS ARE CHARACTER-IDENTICAL TO Validators/ValueToExpression.cs, which
    //  is the port of dwvaluetoexp.srf and carries the same six forms [dwvaluetoexp.srf:L41-L69]. They
    //  are routed through it rather than re-concatenated here, so the two cannot drift. It differs from
    //  the oracle's raw concatenation in exactly one respect, and in the port's favour: a null input
    //  yields a typed null literal such as `dwNvlNumber()` where PowerScript's `"'" + null + "'"` would
    //  poison the whole string with null and store an unusable expression. That is a narrowing to a
    //  defined value rather than a widening to a guess, and it is unobservable for every non-null input.
    //
    //  THE BOOLEAN OVERLOAD IS THE EXCEPTION, and deliberately so: dwvaluetoexp.srf has NO boolean
    //  overload at all, so the oracle renders it with a bare `String(value)` [:L1109] which yields
    //  "true"/"false" - NOT the `1=1`/`1=0` form that a macro RETURN of boolean is rendered with
    //  [:L2270-L2274]. The asymmetry is real, it is observable, and it is reproduced.

    /// <summary>
    /// <c>of_addvar(readonly string name, readonly time value)</c> [:L1091].
    /// </summary>
    /// <param name="name">The variable name, case-sensitive.</param>
    /// <param name="value">The value, rendered as <c>Time('...')</c>.</param>
    public long AddVar(string? name, TimeOnly? value) =>
        AddVarBound(
            name,
            ValueToExpression.Convert(value),
            ExpressionValue.FromTime(value));

    /// <summary>
    /// <c>of_addvar(readonly string name, readonly string value)</c> [:L1094].
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">
    /// The value, rendered as <c>'...'</c>. THE ORACLE DOES NOT ESCAPE THE VALUE [:L1094], so an embedded
    /// apostrophe produces an expression the DataWindow engine will reject. Reproduced (C-B): escaping it
    /// would change the generated expression text, which is exactly what parity is measured on.
    /// </param>
    public long AddVar(string? name, string? value) =>
        AddVarBound(
            name,
            ValueToExpression.Convert(value),
            ExpressionValue.FromString(value));

    /// <summary>
    /// <c>of_addvar(readonly string name, readonly long value)</c> [:L1097].
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value, rendered as a bare numeral.</param>
    public long AddVar(string? name, long? value) =>
        AddVarBound(
            name,
            ValueToExpression.Convert(value),
            ExpressionValue.FromLong(value));

    /// <summary>
    /// <c>of_addvar(readonly string name, readonly double value)</c> [:L1100].
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value, rendered as a bare numeral.</param>
    public long AddVar(string? name, double? value) =>
        AddVarBound(
            name,
            ValueToExpression.Convert(value),
            ExpressionValue.FromDouble(value));

    /// <summary>
    /// <c>of_addvar(readonly string name, readonly datetime value)</c> [:L1103].
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value, rendered as <c>DateTime('...')</c>.</param>
    public long AddVar(string? name, DateTime? value) =>
        AddVarBound(
            name,
            ValueToExpression.Convert(value),
            ExpressionValue.FromDateTime(value));

    /// <summary>
    /// <c>of_addvar(readonly string name, readonly date value)</c> [:L1106].
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value, rendered as <c>Date('...')</c>.</param>
    public long AddVar(string? name, DateOnly? value) =>
        AddVarBound(
            name,
            ValueToExpression.Convert(value),
            ExpressionValue.FromDate(value));

    /// <summary>
    /// <c>of_addvar(readonly string name, readonly boolean value)</c> [:L1109] - the one overload with no
    /// <c>dwvaluetoexp</c> counterpart. See the section banner: it renders "true"/"false", not
    /// <c>1=1</c>/<c>1=0</c>.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">
    /// The value. NOT NULLABLE, because there is no boolean null literal to render: PowerScript's
    /// <c>String(null)</c> would poison the expression with null and store something unusable, and
    /// inventing a form would be a widening. A caller needing a nullable boolean stores the numeric or
    /// string rendering it wants.
    /// </param>
    public long AddVar(string? name, bool value) =>
        AddVarBound(name, FormatBoolean(value), ExpressionValue.FromBoolean(value));

    /// <summary>
    /// <c>of_addvarexp(readonly string name, string exp)</c> [:L1584-L1630] - define a variable whose
    /// value IS a DataWindow expression, which may itself reference other variables.
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.OK"/>, <see cref="RetCode.E_INVALID_ARGUMENT"/> for an empty name, an empty
    /// expression, a name containing a delimiter or an unparseable expression, or
    /// <see cref="RetCode.FAILED"/> for a duplicate definition [:L1608].
    /// </returns>
    /// <remarks>
    /// THE ORDER OF THE FOUR CHECKS IS OBSERVABLE and is preserved: empty first [:L1605], then DUPLICATE
    /// [:L1606-L1609], then the name's characters [:L1611-L1617], then the parse [:L1619]. A duplicate
    /// name containing a delimiter therefore reports the duplicate, not the delimiter - and it is the only
    /// one of the four that reports a message at all.
    /// </remarks>
    /// <summary>
    /// Drops any typed binding a variable name still carries, unless a typed bind is what is running.
    /// </summary>
    /// <param name="variableName">The variable name.</param>
    /// <remarks>
    /// <b>AN EXPRESSION-VALUED VARIABLE IS NOT A BOUND VALUE.</b> A caller that first sets a variable from a
    /// typed value and later redefines it with an EXPRESSION has stopped supplying data and started
    /// supplying syntax; leaving the old binding in place would bind the STALE value into the new
    /// expression's executable text while its observable text carried the new expression. The drop happens
    /// BEFORE the parse, because the parse is what reads the table.
    /// </remarks>
    private void DropStaleBinding(string variableName)
    {
        if (!_boundVarValueAssignmentInFlight && variableName.Length > 0)
        {
            _ = _boundVarValues.Remove(variableName);
        }
    }

    /// <summary>
    /// Mints a placeholder for a value and records it, so a name is never emitted without its value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="renderedFragment">The rendered parity text for the fragment.</param>
    /// <returns>The placeholder token.</returns>
    /// <remarks>
    /// MINTING AND RECORDING ARE ONE OPERATION. The Persistence update carrier learned the alternative the
    /// hard way: a mint-without-record helper there advanced no counter, so every value bound to one name.
    /// </remarks>
    private string MintBoundValue(in ExpressionValue value, string renderedFragment)
    {
        string placeholder = DataWindowExpressionEvaluator.BoundValuePlaceholder(_boundValueOrdinal++);

        _boundValues[placeholder] = value;
        _boundFragments[placeholder] = renderedFragment;

        return placeholder;
    }

    /// <summary>
    /// Renders an executable text back to the observable one by substituting each placeholder's fragment.
    /// </summary>
    /// <param name="text">The executable text.</param>
    /// <returns>The observable text, byte-identical to what the oracle would have produced.</returns>
    /// <remarks>
    /// <para>
    /// <b>DERIVED RATHER THAN COMPOSED A SECOND TIME, WHICH IS WHY THE TWO CANNOT DISAGREE.</b> Composing
    /// the observable text separately would mean preprocessing twice - and preprocessing INVOKES MACROS, so
    /// a second pass would call back into the client a second time. Substituting fragments into the text
    /// that was actually executed has no side effects and is exact.
    /// </para>
    /// <para>
    /// <b>A SINGLE LEFT-TO-RIGHT SCAN, NOT A LOOP OF REPLACEMENTS.</b> <c>:pfwVal1</c> is a PREFIX of
    /// <c>:pfwVal10</c>, so successive replacements corrupt their own output once ten values have been
    /// bound - the same defect the drop-down search's renumbering had and for the same reason. The digit run
    /// is read greedily, which is what makes <c>:pfwVal10</c> one token.
    /// </para>
    /// </remarks>
    private string RenderBoundText(string text)
    {
        const string Prefix = DataWindowExpressionEvaluator.BoundValuePlaceholderPrefix;

        int at = text.IndexOf(Prefix, StringComparison.Ordinal);

        if (at < 0 || _boundFragments.Count == 0)
        {
            return text;
        }

        System.Text.StringBuilder rendered = new(text.Length);
        int copied = 0;

        while (at >= 0)
        {
            int scan = at + Prefix.Length;

            while (scan < text.Length && char.IsAsciiDigit(text[scan]))
            {
                scan++;
            }

            if (scan == at + Prefix.Length)
            {
                at = text.IndexOf(Prefix, scan, StringComparison.Ordinal);

                continue;
            }

            _ = rendered.Append(text, copied, at - copied);

            // AN UNKNOWN TOKEN IS LEFT STANDING RATHER THAN ERASED, so a disagreement between the text and
            // the table is visible in the reported expression instead of silently deleting part of it.
            _ = rendered.Append(
                _boundFragments.TryGetValue(text[at..scan], out string? fragment)
                    ? fragment
                    : text[at..scan]);

            copied = scan;
            at = scan >= text.Length ? -1 : text.IndexOf(Prefix, scan, StringComparison.Ordinal);
        }

        _ = rendered.Append(text, copied, text.Length - copied);

        return rendered.ToString();
    }

    /// <summary>
    /// Records a variable's TYPED value and then defines it through the expression path.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="rendered">The literal the oracle renders for the value - the OBSERVABLE half.</param>
    /// <param name="value">The value itself - the EXECUTABLE half.</param>
    /// <returns>Whatever <see cref="AddVarExp(string?, string?)"/> answers.</returns>
    /// <remarks>
    /// THE ORDER MATTERS: the value is recorded BEFORE the parse, because the parse is what substitutes a
    /// static reference and therefore what needs to know the variable is bound. Recording afterwards would
    /// leave the first expression that referenced it carrying the rendered literal in its executable text.
    /// </remarks>
    private long AddVarBound(string? name, string rendered, in ExpressionValue value)
    {
        if (name is { Length: > 0 })
        {
            _boundVarValues[name] = value;
        }

        _boundVarValueAssignmentInFlight = true;

        long rtCode;

        try
        {
            rtCode = AddVarExp(name, rendered);
        }
        finally
        {
            _boundVarValueAssignmentInFlight = false;
        }

        if (name is { Length: > 0 } && Predicates.IsFailed(rtCode))
        {
            // A REFUSED DEFINITION LEAVES NO BINDING BEHIND. Otherwise a later of_addvarexp under the same
            // name would find a stale value and bind it into an expression that never carried it.
            _ = _boundVarValues.Remove(name);
        }

        return rtCode;
    }

    /// <summary>
    /// Records a variable's TYPED value and then redefines it through the expression path.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="rendered">The literal the oracle renders for the value.</param>
    /// <param name="value">The value itself.</param>
    /// <param name="recalc">Whether to raise the variable-changed event.</param>
    /// <param name="force">The event's <c>forcecalc</c> argument.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    /// <returns>Whatever <see cref="SetVarExpAsync(string?, string?, bool, bool, CancellationToken)"/> answers.</returns>
    private ValueTask<long> SetVarBoundAsync(
        string? name,
        string rendered,
        in ExpressionValue value,
        bool recalc,
        bool force,
        CancellationToken cancellationToken)
    {
        if (name is { Length: > 0 })
        {
            _boundVarValues[name] = value;
        }

        _boundVarValueAssignmentInFlight = true;

        try
        {
            return SetVarExpAsync(name, rendered, recalc, force, cancellationToken);
        }
        finally
        {
            // SAFE DESPITE THE await INSIDE THE CALLEE. SetVarExpAsync's own removal of a stale binding
            // happens synchronously before its first suspension point - it is inside AddVarExp or inside the
            // parse - so the flag has already been read by the time this finally runs.
            _boundVarValueAssignmentInFlight = false;
        }
    }

    public long AddVarExp(string? name, string? exp)
    {
        string variableName = name ?? string.Empty;
        string expression = exp ?? string.Empty;

        if (variableName.Length == 0 || expression.Length == 0)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        if (FindVarIndex(variableName) > 0)
        {
            RaiseError(
                ParseErrorFormatter.CreatePlainError(
                    ExpressionErrorSite.DuplicateVariableDefinition,
                    variableName));
            return RetCode.FAILED;
        }

        // :L1611-L1617 - a name is scanned character by character and the FIRST delimiter rejects it.
        foreach (char candidate in variableName)
        {
            if (VariableNameDelimiters.Contains(candidate))
            {
                return RetCode.E_INVALID_ARGUMENT;
            }
        }

        DropStaleBinding(variableName);

        ParseOutcome parse = ParseExp(expression);
        if (Predicates.IsFailed(parse.ReturnCode))
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // :L1621-L1625 - varType is left at its default, which is VAR_LOCAL [:L117].
        GlobalVariable variable = new()
        {
            Name = variableName,
            VarType = ExpressionVariableEnvironment.VAR_LOCAL,
            Local = new LocalVariableDefinition
            {
                Exp = parse.Expression,
                Vars = [.. parse.Vars],
                Fns = [.. parse.Fns],
                HasMacro = parse.Vars.Count > 0 || parse.Fns.Count > 0,
            },
        };

        _globalVars = _globalVars.Append(variable);
        _varBindings[variableName] = parse.ToBinding(expression);

        ResetColumns();

        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_setvarexp(readonly string name, string exp, readonly boolean recalc, readonly boolean
    /// force)</c> [:L1734-L1774] - redefine a variable's expression, optionally recalculating what depends
    /// on it.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="exp">The new value expression.</param>
    /// <param name="recalc">When true, raise the variable-changed event [:L1769-L1771].</param>
    /// <param name="force">
    /// Passed to that event as its <c>forcecalc</c> argument. It decides whether expressions that trigger
    /// only on user input are recalculated too [:L345-L347].
    /// </param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/>; whatever <see cref="AddVarExp"/> answers when the name is new [:L1757];
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> for an empty or unparseable expression; or
    /// <see cref="RetCode.E_NO_SUPPORT"/> for a FOREIGN variable [:L1759].
    /// </returns>
    /// <remarks>
    /// <para>
    /// AN UNKNOWN NAME IS AN ADD, NOT AN ERROR [:L1757], and the add path ignores both
    /// <paramref name="recalc"/> and <paramref name="force"/> because <see cref="AddVarExp"/> takes
    /// neither - a brand-new variable has nothing depending on it yet.
    /// </para>
    /// <para>
    /// A FOREIGN VARIABLE IS READ-ONLY HERE. It has no local expression to replace: its value comes from
    /// the peer service that owns it [:L2384-L2385], so redefining it locally would silently detach the
    /// two.
    /// </para>
    /// </remarks>
    public async ValueTask<long> SetVarExpAsync(
        string? name,
        string? exp,
        bool recalc,
        bool force,
        CancellationToken cancellationToken = default)
    {
        string variableName = name ?? string.Empty;
        string expression = exp ?? string.Empty;

        int index = FindVarIndex(variableName);
        if (index == 0)
        {
            return AddVarExp(variableName, expression);
        }

        if (expression.Length == 0)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        GlobalVariable variable = _globalVars[index];

        if (variable.VarType == ExpressionVariableEnvironment.VAR_FOREIGN)
        {
            return RetCode.E_NO_SUPPORT;
        }

        // :L1760 - the same raw-against-rewritten comparison as SetExp, with the same consequence: once a
        // static reference has been expanded the stored text no longer matches the source, so setting the
        // identical source again re-parses and takes a FRESH static snapshot. That is the documented way
        // to re-bake a static expansion [docs/n_cst_dwsvc_columnexp.md:L159].
        if (string.Equals(variable.Local.Exp, expression, StringComparison.Ordinal))
        {
            return RetCode.OK;
        }

        DropStaleBinding(variableName);

        ParseOutcome parse = ParseExp(expression);
        if (Predicates.IsFailed(parse.ReturnCode))
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        _globalVars = _globalVars.Replace(
            index,
            variable with
            {
                Local = new LocalVariableDefinition
                {
                    Exp = parse.Expression,
                    Vars = [.. parse.Vars],
                    Fns = [.. parse.Fns],
                    HasMacro = parse.Vars.Count > 0 || parse.Fns.Count > 0,
                },
            });
        _varBindings[variableName] = parse.ToBinding(expression);

        ResetColumns();

        if (recalc)
        {
            await OnVarChangedAsync(index, force, cancellationToken).ConfigureAwait(false);
        }

        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_setvarexp(readonly string name, readonly string exp, readonly boolean recalc)</c> [:L1776] -
    /// three-argument arity; <c>force</c> defaults to false.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="exp">The new value expression.</param>
    /// <param name="recalc">When true, raise the variable-changed event.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    public ValueTask<long> SetVarExpAsync(
        string? name,
        string? exp,
        bool recalc,
        CancellationToken cancellationToken = default) =>
        SetVarExpAsync(name, exp, recalc, false, cancellationToken);

    /// <summary>
    /// <c>of_setvarexp(readonly string name, readonly string exp)</c> [:L1710] - two-argument arity;
    /// neither recalculates.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="exp">The new value expression.</param>
    /// <param name="cancellationToken">Threaded through for signature symmetry.</param>
    public ValueTask<long> SetVarExpAsync(
        string? name,
        string? exp,
        CancellationToken cancellationToken = default) =>
        SetVarExpAsync(name, exp, false, false, cancellationToken);

    /// <summary>
    /// <c>of_setvar(readonly string name, readonly time value, readonly boolean recalc, readonly boolean
    /// force)</c> [:L1779].
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="recalc">When true, recalculate what depends on the variable.</param>
    /// <param name="force">Whether input-triggered expressions recalculate too.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        TimeOnly? value,
        bool recalc,
        bool force,
        CancellationToken cancellationToken = default) =>
        SetVarBoundAsync(
            name,
            ValueToExpression.Convert(value),
            ExpressionValue.FromTime(value),
            recalc,
            force,
            cancellationToken);

    /// <summary>
    /// <c>of_setvar(readonly string name, readonly string value, readonly boolean recalc, readonly boolean
    /// force)</c> [:L1797].
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value, rendered unescaped as <c>'...'</c>.</param>
    /// <param name="recalc">When true, recalculate what depends on the variable.</param>
    /// <param name="force">Whether input-triggered expressions recalculate too.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        string? value,
        bool recalc,
        bool force,
        CancellationToken cancellationToken = default) =>
        SetVarBoundAsync(
            name,
            ValueToExpression.Convert(value),
            ExpressionValue.FromString(value),
            recalc,
            force,
            cancellationToken);

    /// <summary>
    /// <c>of_setvar(readonly string name, readonly long value, readonly boolean recalc, readonly boolean
    /// force)</c> [:L1794].
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="recalc">When true, recalculate what depends on the variable.</param>
    /// <param name="force">Whether input-triggered expressions recalculate too.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        long? value,
        bool recalc,
        bool force,
        CancellationToken cancellationToken = default) =>
        SetVarBoundAsync(
            name,
            ValueToExpression.Convert(value),
            ExpressionValue.FromLong(value),
            recalc,
            force,
            cancellationToken);

    /// <summary>
    /// <c>of_setvar(readonly string name, readonly double value, readonly boolean recalc, readonly boolean
    /// force)</c> [:L1791].
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="recalc">When true, recalculate what depends on the variable.</param>
    /// <param name="force">Whether input-triggered expressions recalculate too.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        double? value,
        bool recalc,
        bool force,
        CancellationToken cancellationToken = default) =>
        SetVarBoundAsync(
            name,
            ValueToExpression.Convert(value),
            ExpressionValue.FromDouble(value),
            recalc,
            force,
            cancellationToken);

    /// <summary>
    /// <c>of_setvar(readonly string name, readonly datetime value, readonly boolean recalc, readonly
    /// boolean force)</c> [:L1788].
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="recalc">When true, recalculate what depends on the variable.</param>
    /// <param name="force">Whether input-triggered expressions recalculate too.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        DateTime? value,
        bool recalc,
        bool force,
        CancellationToken cancellationToken = default) =>
        SetVarBoundAsync(
            name,
            ValueToExpression.Convert(value),
            ExpressionValue.FromDateTime(value),
            recalc,
            force,
            cancellationToken);

    /// <summary>
    /// <c>of_setvar(readonly string name, readonly date value, readonly boolean recalc, readonly boolean
    /// force)</c> [:L1785].
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="recalc">When true, recalculate what depends on the variable.</param>
    /// <param name="force">Whether input-triggered expressions recalculate too.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        DateOnly? value,
        bool recalc,
        bool force,
        CancellationToken cancellationToken = default) =>
        SetVarBoundAsync(
            name,
            ValueToExpression.Convert(value),
            ExpressionValue.FromDate(value),
            recalc,
            force,
            cancellationToken);

    /// <summary>
    /// <c>of_setvar(readonly string name, readonly boolean value, readonly boolean recalc, readonly
    /// boolean force)</c> [:L1782].
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value, rendered "true"/"false" - see the section banner.</param>
    /// <param name="recalc">When true, recalculate what depends on the variable.</param>
    /// <param name="force">Whether input-triggered expressions recalculate too.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        bool value,
        bool recalc,
        bool force,
        CancellationToken cancellationToken = default) =>
        SetVarBoundAsync(
            name,
            FormatBoolean(value),
            ExpressionValue.FromBoolean(value),
            recalc,
            force,
            cancellationToken);

    /// <summary><c>of_setvar(name, time value, recalc)</c> [:L1713].</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="recalc">When true, recalculate what depends on the variable.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        TimeOnly? value,
        bool recalc,
        CancellationToken cancellationToken = default) =>
        SetVarAsync(name, value, recalc, false, cancellationToken);

    /// <summary><c>of_setvar(name, string value, recalc)</c> [:L1716].</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="recalc">When true, recalculate what depends on the variable.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        string? value,
        bool recalc,
        CancellationToken cancellationToken = default) =>
        SetVarAsync(name, value, recalc, false, cancellationToken);

    /// <summary><c>of_setvar(name, long value, recalc)</c> [:L1719].</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="recalc">When true, recalculate what depends on the variable.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        long? value,
        bool recalc,
        CancellationToken cancellationToken = default) =>
        SetVarAsync(name, value, recalc, false, cancellationToken);

    /// <summary><c>of_setvar(name, double value, recalc)</c> [:L1722].</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="recalc">When true, recalculate what depends on the variable.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        double? value,
        bool recalc,
        CancellationToken cancellationToken = default) =>
        SetVarAsync(name, value, recalc, false, cancellationToken);

    /// <summary><c>of_setvar(name, datetime value, recalc)</c> [:L1725].</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="recalc">When true, recalculate what depends on the variable.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        DateTime? value,
        bool recalc,
        CancellationToken cancellationToken = default) =>
        SetVarAsync(name, value, recalc, false, cancellationToken);

    /// <summary><c>of_setvar(name, date value, recalc)</c> [:L1728].</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="recalc">When true, recalculate what depends on the variable.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        DateOnly? value,
        bool recalc,
        CancellationToken cancellationToken = default) =>
        SetVarAsync(name, value, recalc, false, cancellationToken);

    /// <summary><c>of_setvar(name, boolean value, recalc)</c> [:L1731].</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="recalc">When true, recalculate what depends on the variable.</param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        bool value,
        bool recalc,
        CancellationToken cancellationToken = default) =>
        SetVarAsync(name, value, recalc, false, cancellationToken);

    /// <summary>
    /// <c>of_setvar(readonly string name, readonly time value)</c> [:L1112] - the one-argument arity,
    /// which does NOT recalculate.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">Threaded through for signature symmetry.</param>
    /// <remarks>
    /// ALL SEVEN ONE-ARGUMENT ARITIES PASS recalc=false AND force=false [:L1112-L1130], so setting a
    /// variable through them changes nothing on screen until something else triggers a calculation. That is
    /// the oracle's default and it is preserved: the documented way to see the change is
    /// <c>of_SetVar</c> followed by <c>of_Calc</c> [docs/n_cst_dwsvc_columnexp.md:L51-L53].
    /// </remarks>
    public ValueTask<long> SetVarAsync(
        string? name,
        TimeOnly? value,
        CancellationToken cancellationToken = default) =>
        SetVarAsync(name, value, false, false, cancellationToken);

    /// <summary><c>of_setvar(readonly string name, readonly string value)</c> [:L1115].</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">Threaded through for signature symmetry.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        string? value,
        CancellationToken cancellationToken = default) =>
        SetVarAsync(name, value, false, false, cancellationToken);

    /// <summary><c>of_setvar(readonly string name, readonly long value)</c> [:L1118].</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">Threaded through for signature symmetry.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        long? value,
        CancellationToken cancellationToken = default) =>
        SetVarAsync(name, value, false, false, cancellationToken);

    /// <summary><c>of_setvar(readonly string name, readonly double value)</c> [:L1121].</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">Threaded through for signature symmetry.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        double? value,
        CancellationToken cancellationToken = default) =>
        SetVarAsync(name, value, false, false, cancellationToken);

    /// <summary><c>of_setvar(readonly string name, readonly datetime value)</c> [:L1124].</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">Threaded through for signature symmetry.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        DateTime? value,
        CancellationToken cancellationToken = default) =>
        SetVarAsync(name, value, false, false, cancellationToken);

    /// <summary><c>of_setvar(readonly string name, readonly date value)</c> [:L1127].</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">Threaded through for signature symmetry.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        DateOnly? value,
        CancellationToken cancellationToken = default) =>
        SetVarAsync(name, value, false, false, cancellationToken);

    /// <summary><c>of_setvar(readonly string name, readonly boolean value)</c> [:L1130].</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">Threaded through for signature symmetry.</param>
    public ValueTask<long> SetVarAsync(
        string? name,
        bool value,
        CancellationToken cancellationToken = default) =>
        SetVarAsync(name, value, false, false, cancellationToken);

    /// <summary>
    /// <c>of_getvarexp(string name)</c> [:L1066-L1089] - a variable's stored value expression, or the
    /// empty string when the name is unknown [:L1086].
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <remarks>
    /// FOR A FOREIGN VARIABLE THIS ANSWERS THE EMPTY STRING, because the oracle reads
    /// <c>GlobalVars[index].local.exp</c> unconditionally [:L1088] and a foreign entry's local half was
    /// never filled. Not an error path - just an empty answer - and reproduced as such.
    /// </remarks>
    public string GetVarExp(string? name)
    {
        int index = FindVarIndex(name);
        return index < 1 ? string.Empty : _globalVars[index].Local.Exp;
    }

    /// <summary>
    /// <c>of_addforeignvar(readonly string name, readonly se_cst_dw dw)</c> [:L2097-L2146] - reference a
    /// variable that another DataWindow's expression service owns.
    /// </summary>
    /// <param name="name">
    /// The variable's name. IT MUST ALREADY EXIST IN THE PEER and it is looked up THERE [:L2131]; the
    /// local entry is a reference, not a copy.
    /// </param>
    /// <param name="peer">
    /// The peer service. The oracle takes the peer's HOST and reaches its service through
    /// <c>dw.ColumnExp</c> [:L2126]; taking the service directly is the same edge with the indirection
    /// removed, and it is what makes the co-residency requirement checkable.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/>; <see cref="RetCode.E_INVALID_ARGUMENT"/> for an empty name [:L2119];
    /// <see cref="RetCode.E_INVALID_OBJECT"/> for an invalid peer [:L2120];
    /// <see cref="RetCode.FAILED"/> for a duplicate local definition [:L2123];
    /// <see cref="RetCode.E_VAR_NOT_FOUND"/> when the peer does not define the name [:L2134]; or the
    /// session's BLOCKED code when the peer is not co-resident.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THIS IS WHERE AAP SECTION 0.6.2.3'S HARD LIMIT LIVES. The oracle stores a LIVE OBJECT POINTER -
    /// <c>varData.foreign.expSvc = expSvc</c> [:L2130] - and a pointer cannot be serialized. The port
    /// stores a SESSION-SCOPED HANDLE instead, and the session refuses to resolve a handle that is not
    /// co-resident, returning a defined BLOCKED error rather than a wrong value. Co-residency is checked
    /// HERE as well as at resolution time, so a cross-session link is refused when it is created rather
    /// than silently accepted and then failing on every calculation.
    /// </para>
    /// <para>
    /// THE LINK IS BIDIRECTIONAL AND THE BACK-LINK IS WHAT MAKES PROPAGATION WORK [:L2139-L2141]: the
    /// PEER records that this engine's variable number N mirrors its own variable, so when the peer's
    /// variable changes it can fan the notification back [:L353-L356, :L322-L325]. Without the back-link
    /// the reference would resolve but would never update.
    /// </para>
    /// </remarks>
    public long AddForeignVar(string? name, ColumnExpressionEngine? peer)
    {
        string variableName = name ?? string.Empty;

        if (variableName.Length == 0)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // :L2120 - IsValidObject on the host. A null peer is the port's equivalent of an invalid object.
        if (!Predicates.IsValidObject(peer))
        {
            return RetCode.E_INVALID_OBJECT;
        }

        if (FindVarIndex(variableName) > 0)
        {
            RaiseError(
                ParseErrorFormatter.CreatePlainError(
                    ExpressionErrorSite.DuplicateForeignVariableDefinition,
                    variableName));
            return RetCode.FAILED;
        }

        // :L2126-L2135 - reach the peer, look the name up INSIDE it, and fail with the peer's own error
        // when it is absent. The session performs all three, and it additionally enforces the co-residency
        // the in-process pointer never needed.
        ForeignVariableResolution resolution =
            _session.ResolveForeignVariable(peer!.Handle, variableName);

        if (!resolution.IsResolved)
        {
            if (resolution.Error is { } error)
            {
                RaiseError(error);
            }

            return resolution.ReturnCode;
        }

        // :L2128-L2130 - the local entry is FOREIGN and carries the peer's handle plus the index the
        // lookup answered.
        GlobalVariable variable = new()
        {
            Name = variableName,
            VarType = ExpressionVariableEnvironment.VAR_FOREIGN,
            Foreign = new ForeignVariableReference(resolution.Index, resolution.Handle.Value),
        };

        _globalVars = _globalVars.Append(variable);

        // :L2139-L2141 - the back-link, recorded on the PEER against the peer's own variable, pointing at
        // the index this engine's new entry just landed at.
        peer.AddForeignLink(
            resolution.Index,
            new ForeignVariableReference(_globalVars.UpperBound, _handle.Value));

        ResetColumns();

        return RetCode.OK;
    }

    /// <summary>
    /// <c>_of_findvarindex(readonly string name)</c> [:L994-L1001] - the one-based index of a global
    /// variable, or 0 when it is not defined [:L1000].
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <remarks>
    /// CASE-SENSITIVE AND REVERSE-SCANNING [:L996-L998]. Case sensitivity is stated in the oracle's own
    /// documentation for the definition entry points [:L1587], and the reverse scan means that if a
    /// duplicate ever did exist the later one would win - which the duplicate check at :L1606 is there to
    /// prevent. This is a member of <see cref="IExpressionServiceHost"/> because a peer service calls it
    /// across the DataWindow boundary [:L2131].
    /// </remarks>
    public int FindVarIndex(string? name) => _globalVars.IndexOf(name);

    /// <summary>
    /// Records a back-link on this engine's variable - the peer half of
    /// <see cref="AddForeignVar"/> [:L2139-L2141].
    /// </summary>
    /// <param name="index">The one-based index of THIS engine's variable being mirrored.</param>
    /// <param name="link">The mirroring engine's handle and its own variable index.</param>
    /// <remarks>
    /// Internal rather than public: the oracle reaches straight into the peer's private array, which no
    /// external caller can be allowed to do, and the ONLY legitimate caller is
    /// <see cref="AddForeignVar"/> on the other engine. An out-of-range index is ignored rather than
    /// throwing, because the index came from the peer's own lookup a moment earlier and a race there would
    /// mean the variable was removed in between - which the oracle would answer by writing into a
    /// reallocated array, not by failing.
    /// </remarks>
    internal void AddForeignLink(int index, ForeignVariableReference link)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (!_globalVars.IsValidIndex(index))
        {
            return;
        }

        GlobalVariable variable = _globalVars[index];
        _globalVars = _globalVars.Replace(index, variable with { Links = [.. variable.Links, link] });
    }


    // =================================================================================================
    //  THE PARSER - _of_parseexp [:L1211-L1514]
    // =================================================================================================
    //
    //  This is a LITERAL TRANSLITERATION of the oracle's character-at-a-time state machine, and that is a
    //  deliberate implementation strategy rather than a shortage of imagination. The machine has three
    //  phases, two nested scanners, quote tracking on both, a cursor that MOVES BACKWARDS AND FORWARDS
    //  under substitution, and several emergent behaviours that no specification records - including the
    //  quote-state hole at :L1498 and the FOR-bound consequence in section 6 of the header. Rewriting it
    //  as a tokeniser plus grammar would produce something cleaner that behaved differently in ways no
    //  test would obviously catch, and behavioural fidelity is the ONE property this port is measured on.
    //  Transliterating it means the defects are reproduced BY CONSTRUCTION instead of by enumeration.
    //
    //  THE ORACLE'S OWN GRAMMAR COMMENT [:L1215-L1224], which is the only place the `@` sigil is
    //  documented at all:
    //
    //      *$变量名(静态展开)/$$变量名(动态展开)
    //      *$函数名({表达式参数列表})/$$内置函数名({表达式参数列表})
    //      *内置函数：
    //            expression $$('变量名') - 获取变量
    //            any $$Invoke('函数名'[,参数1[,参数2[,...]]]) - 调用函数
    //      *上下文数据引用('@'开头)
    //            @$$变量名 - 只支持动态展开
    //            @列名     - 直接引用列名
    //      *变量和函数名称区分大小写
    //      *该函数不校验表达式的合法性!
    //
    //  The last line matters: THE PARSER DOES NOT VALIDATE THE EXPRESSION. It finds and expands macros and
    //  nothing else. A syntactically broken expression parses successfully here and fails later, at
    //  evaluation, with the evaluator's own `"!"` marker. Adding validation would move errors earlier and
    //  change which code a caller sees.

    /// <summary>
    /// The result of one parse - what <c>_of_parseexp(ref string exp, ref vardata vars[], ref funcdata
    /// fns[])</c> [:L171, :L1211] communicates through its three arguments and its return value.
    /// </summary>
    /// <remarks>
    /// A RESULT OBJECT RATHER THAN THREE <c>ref</c> PARAMETERS, and that is required for fidelity rather
    /// than style. The oracle assigns <c>vars</c> and <c>fns</c> ONLY at :L1510-L1511, after every failure
    /// arm has already returned, so a failed parse leaves the caller's reference lists UNTOUCHED - which
    /// <see cref="SetExpAsync(int, string, bool, CancellationToken)"/> depends on to keep a working
    /// expression working after a bad edit. Writing them back only on success makes that guarantee
    /// structural instead of a discipline the caller has to remember.
    /// </remarks>
    private sealed record ParseOutcome
    {
        /// <summary>The parse result - <see cref="RetCode.OK"/>, or a failure code.</summary>
        public required long ReturnCode { get; init; }

        /// <summary>
        /// The REWRITTEN expression, trailer stripped [:L1509]. Meaningful only when
        /// <see cref="ReturnCode"/> is OK; on failure it holds whatever the scan had produced when it gave
        /// up, which the oracle also leaves in its by-value argument and which no caller reads.
        /// </summary>
        public required string Expression { get; init; }

        /// <summary>The retained variable references [:L1510].</summary>
        public required List<VariableReference> Vars { get; init; }

        /// <summary>The retained macro function references [:L1511].</summary>
        public required List<FunctionReference> Fns { get; init; }

        /// <summary>
        /// Variable name to its value expression at the moment of this parse, for every macro variable the
        /// scan touched. This is C-04's bind-time snapshot; see
        /// <see cref="ExpressionBindingSnapshot"/>.
        /// </summary>
        public required Dictionary<string, string> BindTimeSnapshot { get; init; }

        /// <summary>The structured error, when one was raised.</summary>
        public ExpressionParseError? Error { get; init; }

        /// <summary>Builds the C-04 binding payload by pairing this outcome with its source text.</summary>
        /// <param name="unexpandedExp">The expression exactly as the caller supplied it.</param>
        public ExpressionBindingSnapshot ToBinding(string unexpandedExp) =>
            new(
                unexpandedExp,
                BindTimeSnapshot.ToImmutableDictionary(StringComparer.Ordinal),
                [.. Vars],
                [.. Fns]);
    }

    /// <summary>
    /// <c>_of_parseexp</c> [:L1211-L1514] - find every macro reference, expand the static ones in place,
    /// and retain the dynamic ones.
    /// </summary>
    /// <param name="exp">The expression source. Never mutated; the rewrite is returned.</param>
    /// <returns>The outcome; see <see cref="ParseOutcome"/>.</returns>
    private ParseOutcome ParseExp(string exp)
    {
        List<VariableReference> varDatas = [];
        List<FunctionReference> fnDatas = [];
        Dictionary<string, string> bindTime = new(StringComparer.Ordinal);

        // :L1258-L1260 - THE EARLY OUT. Neither sigil anywhere means there is nothing to find, and the
        // expression is returned untouched with empty reference lists. Note the shape: two nested tests
        // rather than an `or`, which is equivalent here and is preserved as written.
        if (!exp.Contains(MACRO_FLAG, StringComparison.Ordinal)
            && !exp.Contains(MACRO_CONTEXT, StringComparison.Ordinal))
        {
            return new ParseOutcome
            {
                ReturnCode = RetCode.OK,
                Expression = exp,
                Vars = varDatas,
                Fns = fnDatas,
                BindTimeSnapshot = bindTime,
            };
        }

        // :L1262 - the trailer. Every caret position reported below is an index into THIS string, and the
        // trailer is what terminates a token sitting at the very end of the expression.
        string sExp = exp + TRAILER;
        long nMacFlag = MACRO_PHASE_FIND;
        int nLen = Len(sExp);

        // SECTION 6 OF THE HEADER: PowerScript evaluates a FOR bound ONCE. The scan therefore keeps this
        // bound even when a substitution changes nLen at step 7 of the static path, while :L1509 uses the
        // UPDATED nLen. Both behaviours are reproduced, and the divergence between them is real.
        int loopEnd = nLen;

        bool bQuoted = false;
        string sQuote = string.Empty;
        int nQuotePos = 0;
        bool bDD = false;
        bool bIsCtx = false;
        bool bIsMacro = true;
        int nMacPos = 0;

        // :L1245-L1247 - the inner scanner's cursor state is declared at FUNCTION scope in the oracle, not
        // per function, so it carries over from one function macro to the next. That carry-over is benign
        // because :L1382 refuses to accept a function whose scan did not end cleanly, which forces both
        // back to their neutral values; declaring them here rather than inside the branch keeps that
        // relationship visible instead of hiding it behind a reset the oracle does not perform.
        int nFnArgPos = 0;
        bool bFnQuoted = false;
        string sFnQuote = string.Empty;

        for (int nPos = 1; nPos <= loopEnd; nPos++)
        {
            // Mid past the end answers the empty string in PowerScript, which falls through to the default
            // arm below and cannot match any sigil or terminator. That is reachable: a substitution that
            // SHORTENS the expression leaves the tail of the captured bound pointing past the new end.
            string sChar = Mid(sExp, nPos, 1);

            if (sChar is "'" or "\"")
            {
                // :L1269-L1283 - both quote characters toggle, but only a matching one closes: sQuote
                // records which opened, so a double quote inside a single-quoted literal is just text.
                if (string.Equals(sQuote, sChar, StringComparison.Ordinal) || sQuote.Length == 0)
                {
                    bQuoted = !bQuoted;
                    if (bQuoted)
                    {
                        sQuote = sChar;

                        // :L1274 - the OPENING position, kept for the unclosed-quote error, which reports
                        // the quote rather than the token midpoint every other caret site uses.
                        nQuotePos = nPos;
                    }
                    else
                    {
                        sQuote = string.Empty;
                    }
                }

                if (bQuoted)
                {
                    nMacFlag = MACRO_PHASE_NONE;
                }
                else if (nMacFlag == MACRO_PHASE_NONE)
                {
                    nMacFlag = MACRO_PHASE_FIND;
                }

                continue;
            }

            if (string.Equals(sChar, MACRO_FLAG, StringComparison.Ordinal))
            {
                // :L1284-L1291. THERE IS NO bQuoted TEST HERE, and it is not needed for the ordinary case:
                // opening a quote drives the phase to NONE, and this arm only acts in FIND. It IS needed
                // for the case defect (j) describes - a space inside the literal has driven the phase back
                // to FIND at :L1498 - and its absence is exactly why that hole exists.
                if (nMacFlag != MACRO_PHASE_FIND)
                {
                    continue;
                }

                nMacFlag = MACRO_PHASE_PARSE;
                nMacPos = nPos;
                bIsCtx = false;
                bIsMacro = true;

                // :L1290-L1291 - the doubling test looks one character ahead and CONSUMES it, which is
                // what makes `$$` one token rather than two.
                bDD = string.Equals(Mid(sExp, nPos + 1, 1), MACRO_FLAG, StringComparison.Ordinal);
                if (bDD)
                {
                    nPos++;
                }

                continue;
            }

            if (string.Equals(sChar, MACRO_CONTEXT, StringComparison.Ordinal))
            {
                // :L1292-L1300 - the context sigil, and the reason `@`, `@$` and `@$$` are three distinct
                // tokens: the macro test consumes a `$` if there is one, and the doubling test then looks
                // ahead AGAIN from the new position.
                if (nMacFlag != MACRO_PHASE_FIND)
                {
                    continue;
                }

                nMacFlag = MACRO_PHASE_PARSE;
                nMacPos = nPos;
                bIsCtx = true;

                bIsMacro = string.Equals(Mid(sExp, nPos + 1, 1), MACRO_FLAG, StringComparison.Ordinal);
                if (bIsMacro)
                {
                    nPos++;
                }

                bDD = string.Equals(Mid(sExp, nPos + 1, 1), MACRO_FLAG, StringComparison.Ordinal);
                if (bDD)
                {
                    nPos++;
                }

                continue;
            }

            if (sChar.Length == 1 && TokenTerminators.Contains(sChar[0]))
            {
                // :L1301-L1302 - a terminator inside a quoted literal is text. Note the ordering: this test
                // is what stops a comma inside `'a,b'` splitting anything.
                if (bQuoted)
                {
                    continue;
                }

                if (nMacFlag == MACRO_PHASE_NONE)
                {
                    nMacFlag = MACRO_PHASE_FIND;
                    continue;
                }

                if (nMacFlag != MACRO_PHASE_PARSE)
                {
                    continue;
                }

                // The token that started at nMacPos ends here. An opening parenthesis makes it a FUNCTION
                // macro; anything else makes it a VARIABLE macro.
                if (string.Equals(sChar, "(", StringComparison.Ordinal))
                {
                    FunctionScanResult function = ScanFunctionMacro(
                        sExp,
                        nPos,
                        nMacPos,
                        nLen,
                        bDD,
                        bIsCtx,
                        fnDatas,
                        ref nFnArgPos,
                        ref bFnQuoted,
                        ref sFnQuote);

                    if (function.Error is { } functionError)
                    {
                        return Failed(functionError, sExp, varDatas, fnDatas, bindTime);
                    }
                }
                else
                {
                    VariableScanResult variable = ScanVariableMacro(
                        ref sExp,
                        ref nPos,
                        ref nLen,
                        nMacPos,
                        bDD,
                        bIsCtx,
                        bIsMacro,
                        varDatas,
                        fnDatas,
                        bindTime);

                    if (variable.Error is { } variableError)
                    {
                        return Failed(variableError, sExp, varDatas, fnDatas, bindTime);
                    }
                }

                // :L1490 - whichever branch ran, the scanner is looking for the next macro again.
                nMacFlag = MACRO_PHASE_FIND;

                continue;
            }

            // :L1492-L1501 - the default arm, and the whole of the FIND/NONE oscillation. A macro can only
            // start at a position the scanner considers "clean", and only whitespace makes a position clean
            // again. The commented-out newline tests are the oracle's own and stay inert (C-B, defect b):
            //
            //      if sChar <> " " and sChar <> "~t" /*and sChar <> "~n"*/ then     [:L1494]
            //      if sChar = " " or sChar = "~t" /*or sChar = "~n"*/ then          [:L1498]
            //
            // The consequence of leaving the newline out is real: a macro at the start of a line inside a
            // multi-line expression is NOT recognised, because the newline never returns the phase to FIND.
            // Reviving either test would make previously inert macros suddenly expand.
            if (nMacFlag == MACRO_PHASE_FIND)
            {
                if (!string.Equals(sChar, " ", StringComparison.Ordinal)
                    && !string.Equals(sChar, "\t", StringComparison.Ordinal))
                {
                    nMacFlag = MACRO_PHASE_NONE;
                }
            }
            else if (nMacFlag == MACRO_PHASE_NONE)
            {
                // DEFECT (j) IS REPRODUCED HERE. This arm does not test bQuoted, so a space INSIDE a
                // quoted literal returns the phase to FIND and a `$` later in that same literal starts a
                // macro. `'a $b'` is the smallest case. Guarding it would be the silent correction C-B
                // forbids, and it would change which expressions carry references.
                if (string.Equals(sChar, " ", StringComparison.Ordinal)
                    || string.Equals(sChar, "\t", StringComparison.Ordinal))
                {
                    nMacFlag = MACRO_PHASE_FIND;
                }
            }
        }

        // :L1504-L1507 - an unterminated quote, reported at the OPENING quote's position. This is the one
        // caret-bearing site whose position is not the token midpoint, and ParseErrorFormatter models that
        // with its own CaretPositionConvention.OpeningQuote.
        if (bQuoted)
        {
            return Failed(
                ParseErrorFormatter.CreateParseError(
                    ExpressionErrorSite.VariableMacroQuoteNotClosed,
                    sExp,
                    nQuotePos),
                sExp,
                varDatas,
                fnDatas,
                bindTime);
        }

        // :L1509-L1511 - the trailer is stripped using the CURRENT nLen, and the two reference lists are
        // published. Note that this is the only point at which they are published at all.
        return new ParseOutcome
        {
            ReturnCode = RetCode.OK,
            Expression = Left(sExp, nLen - 1),
            Vars = varDatas,
            Fns = fnDatas,
            BindTimeSnapshot = bindTime,
        };
    }

    /// <summary>Builds a failed outcome, reporting the error on the way past.</summary>
    /// <param name="error">The structured error, already formatted by <see cref="ParseErrorFormatter"/>.</param>
    /// <param name="expression">The partially rewritten expression; no caller reads it on failure.</param>
    /// <param name="vars">The references collected so far, which the caller will discard.</param>
    /// <param name="fns">The function references collected so far.</param>
    /// <param name="bindTime">The bind-time snapshot collected so far.</param>
    private ParseOutcome Failed(
        ExpressionParseError error,
        string expression,
        List<VariableReference> vars,
        List<FunctionReference> fns,
        Dictionary<string, string> bindTime)
    {
        RaiseError(error);

        return new ParseOutcome
        {
            // THE PER-SITE CODE, NOT A BLANKET ONE. Every caret-bearing site in the catalogue carries its
            // own return code, and one of them differs: the empty-variable-value arm answers
            // RetCode.FAILED [:L1432] where its three immediate neighbours answer E_INVALID_ARGUMENT
            // [:L1418, :L1423, :L1427]. That asymmetry is defect (e) and it survives because the code is
            // taken from the descriptor rather than hard-coded here. The public entry points then flatten
            // it to E_INVALID_ARGUMENT exactly as :L1554 does, so both the flattened code AND the specific
            // one are observable - the first through the return value, the second through the error.
            ReturnCode = error.ReturnCode ?? RetCode.E_INVALID_ARGUMENT,
            Expression = expression,
            Vars = vars,
            Fns = fns,
            BindTimeSnapshot = bindTime,
            Error = error,
        };
    }

    /// <summary>The outcome of scanning one function-macro token.</summary>
    /// <param name="Error">The structured error, or null on success.</param>
    private readonly record struct FunctionScanResult(ExpressionParseError? Error);

    /// <summary>The outcome of scanning one variable-macro token.</summary>
    /// <param name="Error">The structured error, or null on success.</param>
    private readonly record struct VariableScanResult(ExpressionParseError? Error);

    /// <summary>
    /// The function-macro branch [:L1306-L1402] - bracket-counting argument extraction with quote
    /// tracking, followed by the built-in name and arity validation.
    /// </summary>
    /// <param name="sExp">The trailer-extended expression. NOT rewritten by this branch.</param>
    /// <param name="nPos">The position of the opening parenthesis.</param>
    /// <param name="nMacPos">The position the token started at.</param>
    /// <param name="nLen">The current expression length, the inner scan's bound.</param>
    /// <param name="bDD">Whether the sigil was doubled - which becomes <c>funcdata.builtin</c> [:L1311].</param>
    /// <param name="bIsCtx">Whether the token carried the context sigil.</param>
    /// <param name="fnDatas">The accumulating function reference list.</param>
    /// <param name="nFnArgPos">The inner cursor; see the note at its declaration.</param>
    /// <param name="bFnQuoted">The inner quote state.</param>
    /// <param name="sFnQuote">Which quote character opened the inner literal.</param>
    /// <remarks>
    /// <para>
    /// THE ARGUMENTS ARE EXTRACTED AS TEXT, NOT AS VALUES, and they may be arbitrary expressions: the scan
    /// counts brackets and tracks quotes precisely so that a nested call or a quoted comma does not split
    /// an argument. The legacy specification's own example passes a STATIC VARIABLE as an argument -
    /// <c>$FormatPrice(n2, $精度)</c> [docs/n_cst_dwsvc_columnexp.md:L117] - which is why an argument's text
    /// is later rewritten by variable substitution at :L1470-L1473.
    /// </para>
    /// <para>
    /// <c>funcdata.builtin</c> IS MISNAMED IN THE ORACLE AND THE MEANING IS PRESERVED (C-B). It is
    /// assigned straight from the doubled-sigil test [:L1311], so it means "written with <c>$$</c>", NOT
    /// "a DataWindow built-in function". It is what routes dispatch to the sentinel branch.
    /// </para>
    /// </remarks>
    private static FunctionScanResult ScanFunctionMacro(
        string sExp,
        int nPos,
        int nMacPos,
        int nLen,
        bool bDD,
        bool bIsCtx,
        List<FunctionReference> fnDatas,
        ref int nFnArgPos,
        ref bool bFnQuoted,
        ref string sFnQuote)
    {
        // :L1307-L1310 - a context FUNCTION macro is refused outright, before the argument list is even
        // looked at. There is no context-function form in the grammar: `@` addresses a variable or a column
        // on the context DataWindow, never a callback.
        if (bIsCtx)
        {
            return new FunctionScanResult(
                ParseErrorFormatter.CreateParseError(
                    ExpressionErrorSite.FunctionMacroContextReferenceUnsupported,
                    sExp,
                    CaretMidpoint(nMacPos, nPos)));
        }

        bool builtIn = bDD;

        // :L1312-L1316 - the name, taken from offset +2 for the doubled form and +1 for the single one, so
        // that the sigils are excluded and the name alone remains.
        string name = builtIn
            ? PbTrim(Mid(sExp, nMacPos + 2, nPos - nMacPos - 2))
            : PbTrim(Mid(sExp, nMacPos + 1, nPos - nMacPos - 1));

        // :L1317 - the argument list starts empty for every function.
        List<string> args = [];
        string fullName = string.Empty;
        bool captured = false;

        // :L1318 - the bracket counter IS reset per function, unlike nFnArgPos. Preserved as written.
        int nFnBrCnt = 0;

        for (int nFnPos = nPos + 1; nFnPos <= nLen; nFnPos++)
        {
            string sFnWord = Mid(sExp, nFnPos, 1);

            if (sFnWord is "'" or "\"")
            {
                // :L1322-L1333 - the same matching-quote rule as the outer scanner, plus the argument-start
                // capture: a literal is the beginning of an argument if nothing else was.
                if (string.Equals(sFnQuote, sFnWord, StringComparison.Ordinal) || sFnQuote.Length == 0)
                {
                    bFnQuoted = !bFnQuoted;
                    if (bFnQuoted)
                    {
                        sFnQuote = sFnWord;
                        if (nFnArgPos <= 0)
                        {
                            nFnArgPos = nFnPos;
                        }
                    }
                    else
                    {
                        sFnQuote = string.Empty;
                    }
                }

                continue;
            }

            if (string.Equals(sFnWord, "(", StringComparison.Ordinal))
            {
                // :L1334-L1339 - a nested call deepens the count AND can start an argument.
                if (bFnQuoted)
                {
                    continue;
                }

                nFnBrCnt++;
                if (nFnArgPos <= 0)
                {
                    nFnArgPos = nFnPos;
                }

                continue;
            }

            if (string.Equals(sFnWord, ")", StringComparison.Ordinal))
            {
                if (bFnQuoted)
                {
                    continue;
                }

                nFnBrCnt--;

                // :L1343 - MINUS ONE, not zero: the opening parenthesis was consumed by the outer scanner,
                // so the count starts at 0 and the closing one takes it negative. That is what identifies
                // THIS function's closing bracket rather than a nested one's.
                if (nFnBrCnt != -1)
                {
                    continue;
                }

                if (nFnArgPos > 0)
                {
                    // :L1345 - RightTrim only. Leading whitespace was already skipped by the default arm,
                    // so trimming both ends would be redundant; more importantly, LeftTrim would also
                    // remove a leading tab that the skip deliberately allowed through when it was not at
                    // the start.
                    args.Add(PbRightTrim(Mid(sExp, nFnArgPos, nFnPos - nFnArgPos)));
                    nFnArgPos = 0;
                }
                else if (nFnArgPos == -1)
                {
                    // :L1347-L1349 - a trailing comma. The scan ABANDONS the function without recording it,
                    // and the well-formedness test below then fails because nFnArgPos is still -1. That
                    // two-step is how `$f(a,)` is rejected.
                    break;
                }

                // :L1350 - the full token INCLUDING both sigils and the whole argument list, which is the
                // ReplaceAll key preprocessing will substitute on [:L2311].
                fullName = Mid(sExp, nMacPos, nFnPos - nMacPos + 1);
                captured = true;
                break;
            }

            if (string.Equals(sFnWord, ",", StringComparison.Ordinal))
            {
                // :L1366-L1373 - a comma separates arguments only at depth zero and outside quotes.
                if (bFnQuoted || nFnBrCnt > 0)
                {
                    continue;
                }

                if (nFnArgPos > 0)
                {
                    args.Add(PbRightTrim(Mid(sExp, nFnArgPos, nFnPos - nFnArgPos)));

                    // MINUS ONE IS A STATE, NOT AN ERROR: it means "an argument has just ended and the next
                    // has not started". The closing-bracket arm reads it to detect a trailing comma.
                    nFnArgPos = -1;
                }
                else
                {
                    // :L1372 - a comma with no argument before it, so `$f(,a)`. Abandoned the same way.
                    break;
                }

                continue;
            }

            // :L1374-L1379 - the first non-blank character begins an argument. The newline skip is
            // commented out in the oracle and stays inert (C-B, defect b):
            //
            //      if sFnWord <> " " and sFnWord <> "~t" /*and sFnWord <> "~n"*/ then     [:L1376]
            //
            // So a newline COUNTS as argument text here, and an argument written across two lines keeps its
            // newline in the extracted text - which the evaluator then has to cope with.
            if (nFnArgPos <= 0)
            {
                if (!string.Equals(sFnWord, " ", StringComparison.Ordinal)
                    && !string.Equals(sFnWord, "\t", StringComparison.Ordinal))
                {
                    nFnArgPos = nFnPos;
                }
            }
        }

        // :L1382-L1385 - THE WELL-FORMEDNESS TEST, three conditions and all three required: an unclosed
        // literal, a bracket count that never reached this function's closing bracket, and a cursor left
        // mid-argument. Reaching the end of the expression without a closing bracket fails the second.
        if (bFnQuoted || nFnBrCnt != -1 || nFnArgPos != 0)
        {
            return new FunctionScanResult(
                ParseErrorFormatter.CreateParseError(
                    ExpressionErrorSite.FunctionMacroInvalidArgumentList,
                    sExp,
                    CaretMidpoint(nMacPos, nPos)));
        }

        FunctionReference reference = new()
        {
            Name = name,
            FullName = fullName,
            Args = [.. args],
            Builtin = builtIn,
        };

        // :L1386-L1401 - the built-in validation, and ONLY for the doubled form. MacroInvoker.TryClassify
        // is that switch: FUNC_VAR demands exactly one argument and reports with the FULL NAME [:L1390],
        // FUNC_INVOKE demands at least one and reports with the BARE NAME [:L1395], and any other name is
        // undefined [:L1399]. The two different message arguments are not a slip to normalise - they are
        // what the catalogue records - and delegating keeps this file from owning a second copy of the
        // sentinel semantics.
        if (builtIn
            && !MacroInvoker.TryClassify(
                reference,
                MacroInvoker.SigilsFor(reference),
                sExp,
                CaretMidpoint(nMacPos, nPos),
                out _,
                out ExpressionParseError? classifyError))
        {
            return new FunctionScanResult(classifyError!);
        }

        // The token was abandoned by the trailing-comma path, so there is nothing to record. Unreachable in
        // practice because the well-formedness test above rejects that case first; kept because the
        // oracle's control flow admits it and a silent null FullName would be worse than an explicit skip.
        if (!captured)
        {
            return default;
        }

        AppendOrRotateFunction(fnDatas, reference);

        return default;
    }

    /// <summary>
    /// The function-reference dedupe [:L1351-L1363] - append when new, and MOVE TO THE END when already
    /// present.
    /// </summary>
    /// <param name="fnDatas">The accumulating list.</param>
    /// <param name="reference">The reference to record.</param>
    /// <remarks>
    /// THE ROTATION IS NOT A NO-OP AND IT IS NOT DEDUPLICATION HYGIENE. Preprocessing walks this list
    /// BACKWARDS [:L2226] and each substitution rewrites the tokens of every function still ahead of it
    /// [:L2312-L2320], so position determines evaluation order. Moving a repeated token to the end makes
    /// its LAST occurrence the one evaluated first. Sorting or normalising this list would change results.
    /// Note also that the vars list uses a DIFFERENT strategy - append-if-absent with no rotation
    /// [:L1477-L1483] - and the difference is preserved.
    /// </remarks>
    private static void AppendOrRotateFunction(
        List<FunctionReference> fnDatas,
        FunctionReference reference)
    {
        int found = 0;
        for (int index = 1; index <= fnDatas.Count; index++)
        {
            if (string.Equals(
                fnDatas[index - 1].FullName,
                reference.FullName,
                StringComparison.Ordinal))
            {
                found = index;
                break;
            }
        }

        if (found == 0)
        {
            fnDatas.Add(reference);
            return;
        }

        // :L1358-L1362 - shift everything after the match one place left, then write the reference into the
        // slot the shift vacated at the end.
        for (int index = found; index < fnDatas.Count; index++)
        {
            fnDatas[index - 1] = fnDatas[index];
        }

        fnDatas[fnDatas.Count - 1] = reference;
    }


    /// <summary>
    /// The variable-macro branch [:L1403-L1489] - THE `$` VERSUS `$$` DECISION, and the eight-step static
    /// expansion that makes the two behave differently.
    /// </summary>
    /// <param name="sExp">
    /// The trailer-extended expression. REWRITTEN IN PLACE by a static expansion, which is the whole
    /// mechanism - see section 3 of the header.
    /// </param>
    /// <param name="nPos">The terminator's position, RE-ANCHORED by a static expansion [:L1437].</param>
    /// <param name="nLen">The expression length, UPDATED by a static expansion [:L1436].</param>
    /// <param name="nMacPos">The position the token started at.</param>
    /// <param name="bDD">Whether the sigil was doubled, i.e. whether the reference is dynamic.</param>
    /// <param name="bIsCtx">Whether the token carried the context sigil.</param>
    /// <param name="bIsMacro">Whether the token carried a macro sigil at all.</param>
    /// <param name="varDatas">The accumulating variable reference list.</param>
    /// <param name="fnDatas">
    /// The accumulating function reference list. A static expansion REWRITES ITS TOKENS TOO [:L1466-L1475],
    /// because a function's argument list can contain the variable being substituted.
    /// </param>
    /// <param name="bindTime">The bind-time snapshot being accumulated.</param>
    private VariableScanResult ScanVariableMacro(
        ref string sExp,
        ref int nPos,
        ref int nLen,
        int nMacPos,
        bool bDD,
        bool bIsCtx,
        bool bIsMacro,
        List<VariableReference> varDatas,
        List<FunctionReference> fnDatas,
        Dictionary<string, string> bindTime)
    {
        // :L1404 - a token with nothing between the sigil and the terminator. `$;` or `$ + 1` reach here.
        if (nPos - nMacPos - 1 <= 0)
        {
            return new VariableScanResult(
                ParseErrorFormatter.CreateParseError(
                    ExpressionErrorSite.VariableMacroInvalidName,
                    sExp,
                    CaretMidpoint(nMacPos, nPos)));
        }

        // :L1405-L1411 - THE TEMPORARY SHIFT. For an `@$name` or `@$$name` token the name starts one
        // character later than the sigil arithmetic below assumes, so nMacPos is nudged forward for the
        // extraction and nudged straight back afterwards. The shift MUST be undone before fullName is
        // taken, because fullName has to include the `@`.
        int namePos = nMacPos;
        if (bIsCtx && bIsMacro)
        {
            namePos++;
        }

        string name = bDD
            ? PbTrim(Mid(sExp, namePos + 2, nPos - namePos - 2))
            : PbTrim(Mid(sExp, namePos + 1, nPos - namePos - 1));

        // :L1412 - THE FULL TOKEN, SIGILS INCLUDED, and the exact substring the rewrite replaces. RightTrim
        // only: the token cannot have leading whitespace because a sigil started it.
        string fullName = PbRightTrim(Mid(sExp, nMacPos, nPos - nMacPos));

        VariableReference varData = new(
            Name: name,
            FullName: fullName,
            Index: 0,
            IsCtx: bIsCtx,
            IsMacro: bIsMacro);

        // :L1415 - THE FORK. A macro sigil that is NOT doubled is static; everything else - a doubled
        // sigil, or no macro sigil at all - is retained as a reference.
        if (!bIsMacro || bDD)
        {
            // ---------------------------------------------------------------------------------------------
            //  DYNAMIC (or bare context) - :L1476-L1484. NO SUBSTITUTION HAPPENS.
            //
            //  The token stays in the expression text and the reference is retained, so resolution happens
            //  at CALCULATION time against the live variable table [:L2189-L2200]. That is the entire
            //  difference from the static branch, and it is why a later of_SetVar propagates: the stored
            //  text still names the variable.
            //
            //  Dedupe is BY FULL NAME and there is NO ROTATION - unlike the function list. Two occurrences
            //  of `$$a` in one expression are one reference, and one ReplaceAll at :L2214 substitutes both.
            // ---------------------------------------------------------------------------------------------
            bool present = false;
            for (int index = 1; index <= varDatas.Count; index++)
            {
                if (string.Equals(
                    varDatas[index - 1].FullName,
                    varData.FullName,
                    StringComparison.Ordinal))
                {
                    present = true;
                    break;
                }
            }

            if (!present)
            {
                varDatas.Add(varData);
            }

            RecordBindTime(bindTime, name);

            return default;
        }

        // -------------------------------------------------------------------------------------------------
        //  STATIC - :L1415-L1475. The eight steps, in the oracle's order, none of them reorderable.
        // -------------------------------------------------------------------------------------------------

        // STEP 1 [:L1416-L1419] - a context variable cannot be expanded statically. The oracle's own
        // documentation says so - "@$$变量名 - 只支持动态展开" [:L1221] - and this is where it is enforced.
        // NOTE THE MESSAGE SAYS 解析函数宏失败 ("function macro") although this is a VARIABLE: the oracle
        // copied the neighbouring function-macro message. Preserved verbatim in the catalogue (C-B).
        if (bIsCtx)
        {
            return new VariableScanResult(
                ParseErrorFormatter.CreateParseError(
                    ExpressionErrorSite.FunctionMacroContextVariableStaticExpansionUnsupported,
                    sExp,
                    CaretMidpoint(nMacPos, nPos),
                    varData.Name));
        }

        // STEP 2 [:L1420-L1424] - the variable must already be defined. A static expansion needs a value
        // NOW, so a forward reference cannot work; a dynamic one can, because it resolves later.
        int nVarIdx = FindVarIndex(varData.Name);
        if (nVarIdx == 0)
        {
            return new VariableScanResult(
                ParseErrorFormatter.CreateParseError(
                    ExpressionErrorSite.VariableMacroUndefined,
                    sExp,
                    CaretMidpoint(nMacPos, nPos),
                    varData.Name));
        }

        GlobalVariable variable = _globalVars[nVarIdx];

        // STEP 3 [:L1425-L1428] - a FOREIGN variable cannot be expanded statically either: its value lives
        // in another service and is read through it at calculation time. The legacy specification states the
        // requirement from the other direction - a foreign variable "需要使用动态展开"
        // [docs/n_cst_dwsvc_columnexp.md:L34].
        if (variable.VarType == ExpressionVariableEnvironment.VAR_FOREIGN)
        {
            return new VariableScanResult(
                ParseErrorFormatter.CreateParseError(
                    ExpressionErrorSite.VariableMacroForeignStaticExpansionUnsupported,
                    sExp,
                    CaretMidpoint(nMacPos, nPos),
                    varData.Name));
        }

        // STEP 4 [:L1429-L1433] - THE ONE ARM THAT ANSWERS RetCode.FAILED. Its three neighbours answer
        // E_INVALID_ARGUMENT, and the asymmetry is defect (e): a defined variable with no value is treated
        // as a failure rather than as a bad argument. The code comes from the catalogue's descriptor, so it
        // cannot drift out of this arm by accident.
        string sVarExp = variable.Local.Exp;
        if (sVarExp.Length == 0)
        {
            return new VariableScanResult(
                ParseErrorFormatter.CreateParseError(
                    ExpressionErrorSite.VariableMacroValueInvalid,
                    sExp,
                    CaretMidpoint(nMacPos, nPos),
                    varData.Name));
        }

        RecordBindTime(bindTime, varData.Name);

        // STEP 5 [:L1434] - THE PARENTHESIS WRAP, and the single most consequential line in this branch.
        // Without it a multi-term value silently changes the precedence of the expression it lands in: a
        // variable whose value is `1 + 2` substituted into `$v * 3` yields 9 with the wrap and 7 without,
        // and nothing else in the engine would notice. See section 3 of the header.
        sVarExp = "(" + sVarExp + ")";

        // STEP 6 [:L1435] - THE REWRITE, and the moment the static binding becomes permanent. The
        // five-argument keyword form is required: its whole-token semantics are what stop `$a` matching
        // inside `$ab`. AFTER THIS LINE THE VARIABLE'S NAME IS GONE FROM THE TEXT, which is precisely why
        // no later of_SetVar can reach it.
        //
        // AND THE REWRITE IS GLOBAL WHILE THE SCANNER IS QUOTE-AWARE, WHICH IS ASYMMETRIC ON PURPOSE (C-B).
        // The scan skips a sigil inside a quoted literal, but this replacement does not know about quotes,
        // so `'$v is ' + string($v)` becomes `'(3) is ' + string((3))` - the occurrence inside the literal
        // is substituted too. Reproduced rather than corrected: making the rewrite quote-aware would change
        // the stored text of every expression that mentions a variable name inside a string.
        // ==============================================================================================
        //  A DOCUMENTED RESIDUAL: STATIC EXPANSION SUBSTITUTES THE RENDERED VALUE AND IS NOT BOUND
        //  ----------------------------------------------------------------------------------------------
        //  The dynamic path binds a typed value - see PreprocessExpAsync's ordinary case - and this one
        //  cannot, for three reasons that are behavioural rather than incidental. Each was established by
        //  building the placeholder form and observing what it broke.
        //
        //   1. THE REFERENCE LISTS ARE CAPTURED AGAINST THIS TEXT. A function token's FullName and Args are
        //      taken from sExp AFTER this rewrite [:L1449-L1464], so `$FormatPrice(n2, $精度)` is stored with
        //      the expanded argument. A placeholder-bearing text would no longer contain the stored token, so
        //      the function pass at :L2226 would find nothing to substitute and the macro would never be
        //      invoked.
        //   2. THE SCAN RE-ANCHORS ON THE SUBSTITUTED TEXT'S LENGTH at step 7 below, and the divergence
        //      between that updated length and the loop bound captured before the loop is real, documented,
        //      observable behaviour (section 6 of this file's header). A placeholder of a different length
        //      from the literal it replaces moves the cursor differently and changes WHICH later macros the
        //      scan finds - a behavioural change dressed as a security fix.
        //   3. THE REWRITE IS DELIBERATELY QUOTE-UNAWARE (see the note below), so a value also lands INSIDE
        //      string literals, where it is DATA and must remain text. `'$v is ' + string($v)` yields
        //      `'(3) is ' + string((3))`; a placeholder in the literal is not lexed as a placeholder and
        //      would appear verbatim in the result.
        //
        //  WHAT THE RESIDUAL DOES AND DOES NOT COST. It does not grant an authenticated C-04 caller any
        //  capability it lacks: of_addvarexp and of_addexp accept ARBITRARY EXPRESSION SYNTAX by contract,
        //  so the same principal can already submit an expression directly. What remains is a
        //  confused-deputy risk for a caller that forwards untrusted third-party text through of_addvar's
        //  VALUE parameter and then references it STATICALLY. Such a caller should reference it dynamically
        //  (`$$name`), which binds - and that is the documented mitigation rather than a silent gap.
        // ==============================================================================================
        sExp = Text.ReplaceAll(sExp, varData.FullName, sVarExp, true, true);

        // STEP 7 [:L1436-L1437] - RE-ANCHOR. The expression just changed length, so the cursor is moved to
        // the end of the substituted text: forward when the value was longer than the token, backward when
        // it was shorter. nLen is updated here and is read again at :L1509, but the SCAN's bound was
        // captured before the loop and does not move - section 6 of the header.
        nLen = Len(sExp);
        nPos += Len(sVarExp) - (nPos - nMacPos);

        // STEP 8a [:L1438-L1465] - a substituted variable may itself contain macros, and those references
        // now belong to THIS expression because their text does. The two merges use DIFFERENT rules and both
        // are preserved.
        if (variable.Local.HasMacro)
        {
            // :L1439-L1448 - the variable merge compares WHOLE STRUCTURES, not names: `if varDatas[nIndex] =
            // GlobalVars[nVarIdx].local.vars[nGVarIdx]`. Record equality is exactly that comparison, field
            // for field, which is why VariableReference is a record. Two references differing only in their
            // memoised Index are correctly unequal, because 0 and a resolved index are different binding
            // states.
            foreach (VariableReference inherited in variable.Local.Vars)
            {
                bool present = false;
                for (int index = 1; index <= varDatas.Count; index++)
                {
                    if (varDatas[index - 1] == inherited)
                    {
                        present = true;
                        break;
                    }
                }

                if (!present)
                {
                    varDatas.Add(inherited);
                }
            }

            // :L1449-L1464 - the function merge compares FULL NAMES and rotates a match to the end, exactly
            // as the scanner's own append does. See AppendOrRotateFunction for why the position matters.
            foreach (FunctionReference inherited in variable.Local.Fns)
            {
                AppendOrRotateFunction(fnDatas, inherited);
            }
        }

        // STEP 8b [:L1466-L1475] - AND THIS RUNS WHETHER OR NOT THE VARIABLE HAD MACROS, because it is
        // outside the guard above. Every function reference collected so far whose token CONTAINS the
        // substituted variable has that token rewritten, along with each of its arguments - otherwise the
        // function's FullName would no longer match the expression text and the substitution at :L2311
        // would find nothing. This is what makes `$FormatPrice(n2, $精度)` work
        // [docs/n_cst_dwsvc_columnexp.md:L117].
        for (int index = 0; index < fnDatas.Count; index++)
        {
            FunctionReference fn = fnDatas[index];

            if (Pos(fn.FullName, varData.FullName) <= 0)
            {
                continue;
            }

            List<string> rewrittenArgs = new(fn.Args.Length);
            foreach (string arg in fn.Args)
            {
                rewrittenArgs.Add(Text.ReplaceAll(arg, varData.FullName, sVarExp, true, true));
            }

            fnDatas[index] = fn with
            {
                FullName = Text.ReplaceAll(fn.FullName, varData.FullName, sVarExp, true, true),
                Args = [.. rewrittenArgs],
            };
        }

        return default;
    }

    /// <summary>
    /// Records what a variable's value expression WAS at parse time, for C-04's bind-time snapshot.
    /// </summary>
    /// <param name="bindTime">The accumulating snapshot.</param>
    /// <param name="name">The variable name the token referenced.</param>
    /// <remarks>
    /// CAPTURED FOR STATIC AND DYNAMIC REFERENCES ALIKE. For a static reference this is the text that was
    /// actually baked into the expression, so it explains a result that no longer depends on the variable.
    /// For a dynamic one it records the value the binding started from, which is what makes a
    /// legacy-versus-port comparison of the same workflow meaningful. An undefined or foreign name records
    /// nothing: there is no local expression to snapshot, and a placeholder would be a guess.
    /// </remarks>
    private void RecordBindTime(Dictionary<string, string> bindTime, string name)
    {
        if (name.Length == 0)
        {
            return;
        }

        int index = FindVarIndex(name);
        if (index == 0)
        {
            return;
        }

        GlobalVariable variable = _globalVars[index];
        if (variable.VarType != ExpressionVariableEnvironment.VAR_LOCAL)
        {
            return;
        }

        bindTime[name] = variable.Local.Exp;
    }

    /// <summary>
    /// The caret position every caret-bearing parse site uses except the unclosed quote -
    /// <c>nMacPos + (nPos - nMacPos) / 2</c> [:L1308 and ten more].
    /// </summary>
    /// <param name="macroStart">The position the token started at.</param>
    /// <param name="terminator">The position of the terminator that ended it.</param>
    /// <remarks>
    /// INTEGER DIVISION, DELIBERATELY. PowerScript's <c>/</c> on two longs assigned into a long truncates,
    /// so the caret lands at or just before the token's midpoint, never after it. C#'s integer division
    /// truncates identically for the non-negative values this can produce, and the caret position is
    /// rendered into the reported message [:L2403] so an off-by-one would be visible in every recorded
    /// error.
    /// </remarks>
    private static long CaretMidpoint(int macroStart, int terminator) =>
        macroStart + ((terminator - macroStart) / 2);

    // =================================================================================================
    //  POWERSCRIPT STRING PRIMITIVES
    // =================================================================================================
    //
    //  The scanner's arithmetic is one-based and its primitives are PowerScript's, which differ from the
    //  BCL's in ways that matter here. They are reproduced rather than approximated, because every caret
    //  position, every extracted name and every substitution offset is computed from them.

    /// <summary>
    /// <c>Len(string)</c> - the character count.
    /// </summary>
    /// <param name="value">The string.</param>
    /// <remarks>
    /// CHARACTERS, NOT BYTES. PowerBuilder 2021 is a Unicode build, so <c>Len</c> counts characters while
    /// <c>LenA</c> counts bytes - and the oracle uses BOTH, for different purposes: the scanner is character
    /// based, while the caret renderer measures with <c>LenA</c> so that a marker under a double-byte
    /// character lines up in a fixed-pitch dialog [:L2403]. That second one belongs to
    /// <see cref="ParseErrorFormatter.LenA"/> and is not duplicated here.
    /// </remarks>
    private static int Len(string? value) => value?.Length ?? 0;

    /// <summary>
    /// <c>Mid(string, start, length)</c> - one-based extraction that ANSWERS THE EMPTY STRING rather than
    /// throwing when the request falls outside the string, and clamps a length that runs past the end.
    /// </summary>
    /// <param name="value">The string.</param>
    /// <param name="start">The one-based start position.</param>
    /// <param name="length">The requested length.</param>
    /// <remarks>
    /// THE OUT-OF-RANGE BEHAVIOUR IS LOAD BEARING, not defensive padding. The scanner reads one character
    /// ahead to test for a doubled sigil [:L1290] and reads past the end when a substitution shortened the
    /// expression, and in both cases PowerScript's answer is the empty string, which falls through to the
    /// default arm and changes nothing. A throwing Substring would turn a normal scan into a fault.
    /// </remarks>
    private static string Mid(string? value, int start, int length)
    {
        if (value is null || start < 1 || length <= 0 || start > value.Length)
        {
            return string.Empty;
        }

        int available = value.Length - start + 1;
        return value.Substring(start - 1, Math.Min(length, available));
    }

    /// <summary>
    /// <c>Left(string, n)</c> - the first <paramref name="length"/> characters, the whole string when
    /// <paramref name="length"/> exceeds it, and the empty string for a non-positive length.
    /// </summary>
    /// <param name="value">The string.</param>
    /// <param name="length">How many characters to take.</param>
    private static string Left(string? value, int length)
    {
        if (value is null || length <= 0)
        {
            return string.Empty;
        }

        return length >= value.Length ? value : value[..length];
    }

    /// <summary>
    /// <c>Pos(string, substring)</c> - the ONE-BASED position of the first occurrence, or 0 when absent.
    /// </summary>
    /// <param name="value">The string to search.</param>
    /// <param name="search">The substring to find.</param>
    /// <remarks>
    /// ZERO MEANS ABSENT, and the oracle relies on that at :L1468 - <c>if Pos(...) &gt; 0</c> - so the
    /// answer must not be the BCL's -1. Ordinal comparison, because an expression is not culture-sensitive
    /// text and a culture-aware search could match where PowerScript would not.
    /// </remarks>
    private static int Pos(string? value, string? search)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(search))
        {
            return 0;
        }

        return value.IndexOf(search, StringComparison.Ordinal) + 1;
    }

    /// <summary>
    /// <c>Trim(string)</c> - removes leading and trailing SPACES ONLY.
    /// </summary>
    /// <param name="value">The string.</param>
    /// <remarks>
    /// NOT <c>string.Trim()</c>, WHICH WOULD ALSO REMOVE TABS AND NEWLINES. PowerScript's Trim family is
    /// documented as removing spaces, and the difference is observable here: the argument scanner skips a
    /// tab EXPLICITLY and separately [:L1376], which would be redundant if Trim already removed them, and a
    /// variable name written <c>$a\t</c> keeps its tab and therefore fails the case-sensitive lookup at
    /// :L997. Using the BCL's Trim would silently make that name resolve.
    /// </remarks>
    private static string PbTrim(string? value) => value?.Trim(' ') ?? string.Empty;

    /// <summary>
    /// <c>RightTrim(string)</c> - removes trailing SPACES ONLY. See <see cref="PbTrim"/>.
    /// </summary>
    /// <param name="value">The string.</param>
    private static string PbRightTrim(string? value) => value?.TrimEnd(' ') ?? string.Empty;

    /// <summary>
    /// <c>String(boolean)</c> - "true" or "false", which is what the boolean <c>of_addvar</c> overload
    /// splices into an expression [:L1109].
    /// </summary>
    /// <param name="value">The value.</param>
    /// <remarks>
    /// LOWER CASE, AND NOT <c>1=1</c>/<c>1=0</c>. PowerScript's <c>String</c> of a boolean yields the words,
    /// and .NET's <c>bool.ToString()</c> yields "True"/"False" with a capital - which is why this is written
    /// out rather than delegated. The <c>1=1</c> form appears only where a macro RETURN of boolean is
    /// rendered [:L2270-L2274]; see the section banner above the of_addvar overloads.
    /// </remarks>
    private static string FormatBoolean(bool value) => value ? "true" : "false";


    // =================================================================================================
    //  THE THREE EVENTS [:L86-L88] - and the reverse dependency index they build and consume
    // =================================================================================================

    /// <summary>
    /// <c>event onitemchanged(long row, dwobject dwo)</c> [:L86, :L211-L227] - the host's entry point,
    /// raised after a column's value changed BY USER INPUT.
    /// </summary>
    /// <param name="row">The affected row.</param>
    /// <param name="dwo">The changed column.</param>
    /// <param name="cancellationToken">Cancels the calculation cascade.</param>
    /// <remarks>
    /// <para>
    /// THE ITEM-CHANGE GATE IS UPSTREAM AND THIS METHOD MUST NOT RE-TEST IT. <c>se_cst_dw.sru:L43</c>
    /// declares <c>EID_ITEMCHANGE</c> with the note that disabling it stops column-expression calculation,
    /// and :L187 short-circuits before :L313-L314 ever reaches here - so the gate suppresses the CALL,
    /// not the work. Domain/EventGate.cs owns the mask.
    /// </para>
    /// <para>
    /// IT ALWAYS PASSES <c>frominput = true</c> [:L224], and that is the whole difference between this
    /// entry point and a programmatic calculation: it is the only path on which an input-triggered
    /// expression fires.
    /// </para>
    /// <para>
    /// THE <c>CLC_NO</c> SHORT-CIRCUIT AT :L221 IS THE POINT OF THE CACHE. A column already determined to
    /// drive nothing does not even read the row count, so a DataWindow with many columns and few
    /// expressions costs almost nothing per keystroke after the first change to each column.
    /// </para>
    /// </remarks>
    public async ValueTask OnItemChangedAsync(
        long row,
        IDataWindowObject dwo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        // :L214 - the disabled guard on this event RETURNS VOID. Four other entry points answer
        // RetCode.FAILED for the same condition [:L1158, :L1201, :L1976, :L2087]; an event has no return
        // value to carry a code, so the asymmetry is structural rather than a choice.
        if (!Enabled)
        {
            return;
        }

        // :L216-L217 - the id and the name both come off the DataWindow object, not from a lookup.
        int nColId = (int)(dwo.ColumnId() ?? 0);
        string sColName = dwo.Name;

        // :L218-L220 - grow on demand. The assignment is redundant because PowerScript zero-fills and
        // CLC_UNKNOWN is 0, and it is reproduced anyway: it is the line that documents the intent.
        if (nColId > _colDatas.UpperBound)
        {
            _colDatas.EnsureUpperBound(nColId);
            _colDatas[nColId].Flag = ColumnCalcFlag.CLC_UNKNOWN;
        }

        if (_colDatas[nColId].Flag == ColumnCalcFlag.CLC_NO)
        {
            return;
        }

        DataWindowServiceHost host = RequireHost();
        long nRowCnt = host.RowCount();

        // :L223 and :L225 - THE LITERAL 200, a matched pair around the cascade. It is surfaced as
        // ColumnExpression:RedrawSuppressionRowThreshold with 200 as its preserved default, so deployment
        // can tune it without the number moving in source. Suppressing redraw is not cosmetic: without it
        // a cascade over thousands of rows repaints per SetItem.
        bool suppressRedraw = nRowCnt > _options.RedrawSuppressionRowThreshold;
        if (suppressRedraw)
        {
            host.SetRedraw(false);
        }

        try
        {
            await OnDoItemChangedAsync(row, sColName, nColId, true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // THE try/finally IS AN ADDITION AND IT IS REQUIRED BY THE TRANSITION, not a behavioural
            // change. The oracle's two SetRedraw calls sit either side of a synchronous, non-throwing
            // event dispatch. Here the cascade can fail in transit - a macro invocation is a network round
            // trip now - and leaving a DataWindow with redraw switched off after a fault would be a defect
            // the legacy could not have had. Restoring it changes nothing on the success path.
            if (suppressRedraw)
            {
                host.SetRedraw(true);
            }
        }
    }

    /// <summary>
    /// <c>event ondoitemchanged(long row, string colname, long colid, boolean frominput)</c>
    /// [:L87, :L229-L328] - build the reverse dependency index for the changed column if it is not already
    /// known, then calculate everything that depends on it.
    /// </summary>
    /// <param name="row">The affected row.</param>
    /// <param name="colname">The changed column's name.</param>
    /// <param name="colid">The changed column's one-based id.</param>
    /// <param name="frominput">
    /// True when the change came from user input. It is what makes an input-triggered expression fire, and
    /// its absence is what suppresses one - see the filter at :L301-L315.
    /// </param>
    /// <param name="cancellationToken">Cancels the calculation cascade.</param>
    /// <remarks>
    /// THIS METHOD IS BOTH HALVES OF THE GRAPH: it builds the reverse index lazily on the first change to a
    /// column and then walks it. The build is the expensive part - every expression and every variable is
    /// examined - and it happens exactly once per column until something invalidates the cache.
    /// </remarks>
    public async ValueTask OnDoItemChangedAsync(
        long row,
        string? colname,
        long colid,
        bool frominput,
        CancellationToken cancellationToken = default)
    {
        DataWindowServiceHost host = RequireHost();
        string columnName = colname ?? string.Empty;
        int nColId = (int)colid;

        // :L232 - a row guard, and note there is NO #Enabled guard on this event: it is reachable only from
        // onitemchanged, which has one, and from the calculation path, which has its own.
        if (row < 1 || row > host.RowCount())
        {
            return;
        }

        // :L234-L236 - grow on demand again, because this event is also raised directly by :L343.
        if (nColId > _colDatas.UpperBound)
        {
            _colDatas.EnsureUpperBound(nColId);
            _colDatas[nColId].Flag = ColumnCalcFlag.CLC_UNKNOWN;
        }

        ColumnCalcData columnData = _colDatas[nColId];

        if (columnData.Flag == ColumnCalcFlag.CLC_UNKNOWN)
        {
            // :L239 - PESSIMISTIC FIRST. The flag is set to NO before the walk and only promoted to YES by
            // a match, so a column that matches nothing ends up cached as NO and is never walked again.
            columnData.Flag = ColumnCalcFlag.CLC_NO;

            // ------------------------------------------------------------------------------------------
            //  :L240-L279 - which EXPRESSIONS depend on this column. Four tests in strict order, and the
            //  order is what gives each its meaning.
            // ------------------------------------------------------------------------------------------
            int expressionCount = _colExpDatas.UpperBound;
            for (int nIndex = 1; nIndex <= expressionCount; nIndex++)
            {
                ColumnExpressionData entry = _colExpDatas[nIndex];
                bool bMatched = false;
                bool bHasRelative = false;

                if (entry.AlwaysCalc)
                {
                    // :L244-L245 - always-calculate short-circuits every other test.
                    bMatched = true;
                }
                else
                {
                    // :L247-L256 - an explicit trigger list.
                    int c = entry.RelativeColIds.UpperBound;
                    if (c > 0)
                    {
                        bHasRelative = true;
                        for (int i = 1; i <= c; i++)
                        {
                            if (entry.RelativeColIds[i] == colid)
                            {
                                bMatched = true;
                                break;
                            }
                        }
                    }

                    // :L257-L268 - then the input-only trigger list. NOTE THAT THIS PASS DOES NOT CARE
                    // WHETHER THE CHANGE CAME FROM INPUT: it only records the dependency. Whether the
                    // expression actually fires is decided later, at :L301-L315, because the reverse index
                    // is cached across changes that differ in their frominput flag.
                    if (!bMatched)
                    {
                        c = entry.RelativeInputColIds.UpperBound;
                        if (c > 0)
                        {
                            bHasRelative = true;
                            for (int i = 1; i <= c; i++)
                            {
                                if (entry.RelativeInputColIds[i] == colid)
                                {
                                    bMatched = true;
                                    break;
                                }
                            }
                        }
                    }

                    // :L269-L273 - AND ONLY WITH NO TRIGGER LIST AT ALL does the expression's own text get
                    // scanned for a reference to the column. That is the crucial asymmetry: declaring ANY
                    // trigger list turns textual dependency detection OFF for that expression, so an
                    // expression that names the column but lists a different one does NOT recalculate.
                    if (!bMatched && !bHasRelative && HasRef(entry.Exp, columnName))
                    {
                        bMatched = true;
                    }
                }

                if (bMatched)
                {
                    // :L276-L277 - promote, and append the expression index. The append idiom is the
                    // one-based one from section 6 of the header.
                    columnData.Flag = ColumnCalcFlag.CLC_YES;
                    columnData.Indexes.Append(nIndex);
                }
            }

            // ------------------------------------------------------------------------------------------
            //  :L280-L292 - which VARIABLES depend on this column, which is a second and different kind of
            //  edge: a variable's own value expression can name a column, so changing the column changes
            //  the variable, which changes every expression that references the variable.
            // ------------------------------------------------------------------------------------------
            int variableCount = _globalVars.UpperBound;
            for (int nIndex = 1; nIndex <= variableCount; nIndex++)
            {
                GlobalVariable variable = _globalVars[nIndex];

                // :L282 - a foreign variable has no local expression to scan; its value comes from the peer.
                if (variable.VarType == ExpressionVariableEnvironment.VAR_FOREIGN)
                {
                    continue;
                }

                if (!HasRef(variable.Local.Exp, columnName))
                {
                    continue;
                }

                // :L284-L287 - THE LINK TEST IS NOT AN OPTIMISATION. Only a variable that is MIRRORED BY A
                // PEER is recorded in varIndexes, because varIndexes exists solely to fan the change out
                // across the DataWindow boundary [:L319-L326]. An unlinked variable needs no entry: the
                // expressions that depend on it were already collected by the call below.
                if (variable.Links.Length > 0)
                {
                    columnData.VarIndexes.Append(nIndex);
                    columnData.Flag = ColumnCalcFlag.CLC_YES;
                }

                // :L288-L290 - and the indirect edges. excludeRels = TRUE here, so an expression with its
                // own explicit trigger list is NOT pulled in by a variable dependency - consistent with the
                // asymmetry noted above.
                if (FindVarDepends(nIndex, columnData.Indexes, true) > 0)
                {
                    columnData.Flag = ColumnCalcFlag.CLC_YES;
                }
            }
        }

        if (columnData.Flag != ColumnCalcFlag.CLC_YES)
        {
            return;
        }

        // ----------------------------------------------------------------------------------------------
        //  :L295-L327 - walk the index and calculate.
        // ----------------------------------------------------------------------------------------------

        // :L296-L297 - THE CHANGED COLUMN IS PUSHED ONTO THE CALCULATION STACK BEFORE ANYTHING RUNS. That
        // is what makes recursion detection work across an event cascade rather than only within one
        // expression: an expression that writes back to this column finds the column already on the stack
        // and refuses [:L682-L690]. The captured depth is what the matching pop uses [:L318].
        int stackFrame = _calcStack.Push(columnName);

        try
        {
            int indexCount = columnData.Indexes.UpperBound;
            for (int nIndex = 1; nIndex <= indexCount; nIndex++)
            {
                int n = columnData.Indexes[nIndex];
                ColumnExpressionData entry = _colExpDatas[n];

                // :L301-L315 - THE INPUT FILTER, and its logic is inverted from what it looks like. When an
                // expression has an input-only trigger list, bMatched ends up true if this column is IN
                // that list - or unconditionally true when the expression has no OTHER way of being
                // triggered - and then `if bMatched and Not fromInput then continue` SKIPS it. So bMatched
                // here means "this column reaches the expression only through the input list", and a
                // programmatic change therefore must not fire it.
                int c = entry.RelativeInputColIds.UpperBound;
                if (c > 0)
                {
                    bool bMatched;

                    if (entry.AlwaysCalc || entry.RelativeColIds.UpperBound > 0)
                    {
                        // The expression has another trigger route, so it matters whether THIS column is in
                        // the input list.
                        bMatched = false;
                        for (int i = 1; i <= c; i++)
                        {
                            if (entry.RelativeInputColIds[i] == colid)
                            {
                                bMatched = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // :L311-L313 - the input list is the ONLY route, so reaching here at all means it
                        // came that way.
                        bMatched = true;
                    }

                    if (bMatched && !frominput)
                    {
                        continue;
                    }
                }

                await _of_calcitem(row, n, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // :L318 - popped by the captured depth, not by "pop the top". See the try/finally note on
            // OnItemChangedAsync: an unbalanced stack after a transit failure would poison recursion
            // detection for every later calculation, which is a strictly worse outcome than the oracle's.
            _calcStack.Pop(stackFrame);
        }

        // :L319-L326 - fan the change out to every peer that mirrors a dependent variable. forcecalc is
        // hard-coded TRUE [:L324], so a peer recalculates its input-triggered expressions too: from the
        // peer's point of view this is not a local input event and it has no other way to know the change
        // is real.
        int varIndexCount = columnData.VarIndexes.UpperBound;
        for (int nIndex = 1; nIndex <= varIndexCount; nIndex++)
        {
            int i = columnData.VarIndexes[nIndex];
            if (!_globalVars.IsValidIndex(i))
            {
                continue;
            }

            foreach (ForeignVariableReference link in _globalVars[i].Links)
            {
                await NotifyLinkedPeerAsync(link, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// <c>event onvarchanged(integer index, boolean forcecalc)</c> [:L88, :L330-L357] - a global variable's
    /// value changed, so recalculate every expression that depends on it, on every row.
    /// </summary>
    /// <param name="index">The one-based variable index.</param>
    /// <param name="forcecalc">
    /// When false, expressions that trigger only on user input are SKIPPED [:L345-L347]. That is the
    /// <c>force</c> argument <c>of_setvar</c> threads through.
    /// </param>
    /// <param name="cancellationToken">Cancels the recalculation.</param>
    /// <remarks>
    /// <para>
    /// THE DIRTY FLAG IS RESET PER ROW, INSIDE THE ROW LOOP [:L343]. That is not redundant: an expression
    /// calculated on row 1 must be calculated again on row 2, and the dirty flag is the only thing that
    /// would otherwise stop it. Note it does NOT set <c>_bRowCalcing</c>, so each item calculation still
    /// uses the stack-based re-entry rule rather than the dirty-based one - the flags are set here purely
    /// so that a nested pass triggered from inside the cascade sees them.
    /// </para>
    /// <para>
    /// THE PEER FAN-OUT AT :L353-L356 RUNS EVEN WHEN NOTHING LOCAL DEPENDED ON THE VARIABLE, because it is
    /// outside the <c>if nCount &gt; 0</c> block. A variable that only peers use still propagates.
    /// </para>
    /// </remarks>
    public async ValueTask OnVarChangedAsync(
        int index,
        bool forcecalc,
        CancellationToken cancellationToken = default)
    {
        // :L334 - void return again; see the note on OnItemChangedAsync.
        if (!Enabled)
        {
            return;
        }

        // :L335 - A ROW PASS IN PROGRESS SUPPRESSES THIS ENTIRELY. Without it, a variable written from
        // inside a calculation would restart the whole cascade from the top.
        if (_rowCalcing)
        {
            return;
        }

        long nRowCnt = RequireHost().RowCount();
        if (nRowCnt <= 0)
        {
            return;
        }

        OneBasedList<int> nColExps = new(static () => 0);
        int nCount = FindVarDepends(index, nColExps, false);

        if (nCount > 0)
        {
            for (long nRow = 1; nRow <= nRowCnt; nRow++)
            {
                MakeDirty();

                for (int nIndex = 1; nIndex <= nCount; nIndex++)
                {
                    int expressionIndex = nColExps[nIndex];

                    // :L345-L347 - without force, an input-triggered expression is skipped. A variable
                    // change is not user input into the bound column, so firing it would be wrong unless
                    // the caller explicitly asked.
                    if (!forcecalc
                        && _colExpDatas[expressionIndex].RelativeInputColIds.UpperBound > 0)
                    {
                        continue;
                    }

                    await _of_calcitem(nRow, expressionIndex, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        // :L353-L356 - the peer fan-out, unconditional. forcecalc is hard-coded TRUE for the peer.
        if (_globalVars.IsValidIndex(index))
        {
            foreach (ForeignVariableReference link in _globalVars[index].Links)
            {
                await NotifyLinkedPeerAsync(link, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Raises the variable-changed event on a peer service - the port of
    /// <c>GlobalVars[i].links[n].expSvc.Event OnVarChanged(GlobalVars[i].links[n].index, true)</c>
    /// [:L324, :L355].
    /// </summary>
    /// <param name="link">The back-link: the peer's handle and the peer's own variable index.</param>
    /// <param name="cancellationToken">Cancels the peer's recalculation.</param>
    /// <remarks>
    /// <para>
    /// <c>forcecalc</c> IS TRUE AT BOTH ORACLE SITES and is therefore not a parameter here.
    /// </para>
    /// <para>
    /// THE PEER IS REACHED THROUGH THE SESSION, NOT THROUGH A STORED POINTER. When it resolves to another
    /// engine the asynchronous entry point is awaited directly, which is what keeps a macro invocation
    /// inside the peer's own cascade awaitable - see section 7 of the header. A handle that no longer
    /// resolves is SILENTLY IGNORED: the oracle's pointer could not dangle, so it has no behaviour to
    /// reproduce here, and turning a stale link into a failure would break a cascade over an unrelated
    /// peer. It is logged instead.
    /// </para>
    /// </remarks>
    private async ValueTask NotifyLinkedPeerAsync(
        ForeignVariableReference link,
        CancellationToken cancellationToken)
    {
        DataWindowHandle handle = DataWindowHandle.From(link.Handle);

        if (!_session.TryGetHost(handle, out IExpressionServiceHost? peer) || peer is null)
        {
            LogChannelUnavailable("a linked peer expression service is no longer registered");
            return;
        }

        if (peer is ColumnExpressionEngine engine)
        {
            await engine.OnVarChangedAsync(link.Index, true, cancellationToken).ConfigureAwait(false);
            return;
        }

        // A non-engine implementation of the host interface cannot receive the event, because the interface
        // does not carry it - it carries only the four cross-object VALUE calls [:L2182, :L2185, :L2200,
        // :L2385]. Recorded rather than swallowed, so a topology that produced one is diagnosable.
        LogChannelUnavailable(
            "a linked peer is not a column-expression engine and cannot receive OnVarChanged");
    }

    /// <summary>
    /// <c>_of_findvardepends(readonly integer index, ref integer colexps[], readonly boolean
    /// excluderels)</c> [:L1003-L1064] - collect every expression that depends on a variable, directly or
    /// through another variable.
    /// </summary>
    /// <param name="index">The one-based variable index.</param>
    /// <param name="colExps">
    /// The accumulating expression-index list. APPENDED TO, NOT REPLACED, because the caller at :L288 passes
    /// the reverse index it is building and expects both kinds of edge to land in one list.
    /// </param>
    /// <param name="excludeRels">
    /// When true, an expression that declares its own trigger list is skipped [:L1029-L1033]. The reverse
    /// index build passes true; the variable-changed event passes false.
    /// </param>
    /// <returns>The list's upper bound afterwards [:L1063].</returns>
    /// <remarks>
    /// <para>
    /// THE TWO EARLY-OUTS ARE HEURISTICS AND BOTH ARE PRESERVED. The first [:L1025] returns immediately when
    /// the list is already as long as the expression list, on the assumption that it must therefore contain
    /// everything - which is only true if the entries are distinct, and they are, because of the containment
    /// test. The second [:L1042] stops as soon as an append lands at the last possible position. Both are
    /// reproduced because they change how many expressions the caller ends up walking.
    /// </para>
    /// <para>
    /// A TERMINATION GUARD IS ADDED HERE, AND IT IS THE ONLY ADDED GUARD IN THIS FILE. Two variables that
    /// reference each other - <c>a</c> holding <c>$$b</c> and <c>b</c> holding <c>$$a</c> - make the
    /// recursion at :L1056 unbounded, and PowerScript answers that with a stack overflow. In an in-process
    /// library that crashed the caller's own application; in a shared service reachable over C-04 it is a
    /// caller-triggerable process kill, which is a hazard decomposition created rather than one the legacy
    /// had. The guard is a visited set, and it changes no observable answer: revisiting a variable can only
    /// re-append expressions the containment test already rejects, and the position the second early-out
    /// fires at is unchanged. Cycles are the only inputs whose behaviour differs, and for those the oracle
    /// produced no answer at all.
    /// </para>
    /// </remarks>
    private int FindVarDepends(int index, OneBasedList<int> colExps, bool excludeRels) =>
        FindVarDepends(index, colExps, excludeRels, []);

    private int FindVarDepends(
        int index,
        OneBasedList<int> colExps,
        bool excludeRels,
        HashSet<int> visited)
    {
        if (!visited.Add(index))
        {
            // See the remarks: the cycle guard. The oracle has no equivalent line and needs none, because it
            // never returns from this case.
            return colExps.UpperBound;
        }

        if (!_globalVars.IsValidIndex(index))
        {
            // The oracle indexes GlobalVars unguarded [:L1036] and would raise a PowerBuilder runtime error,
            // which the framework converts into an assert failure and a HALT
                // [ws_objects/pfw.pbl.src/pfw.sra:L111-L144 - the framework application, not the
                // same-named packager object at ws_objects/pfw.pack.pbl.src/pfw.sra]. The
            // fail-fast posture is preserved by the environment's own indexer throwing; this test exists only
            // for the recursive call below, whose index always came from a live enumeration.
            return colExps.UpperBound;
        }

        string variableName = _globalVars[index].Name;
        int nCount = _colExpDatas.UpperBound;

        // :L1025 - the first heuristic early-out.
        if (nCount == colExps.UpperBound)
        {
            return nCount;
        }

        // :L1027-L1046 - expressions that reference the variable BY NAME. Note it matches on the reference's
        // name, so a static expansion - which removed the name from the text and never recorded a reference
        // - correctly does NOT match. That is the same distinction as everywhere else in this file.
        for (int nIndex = 1; nIndex <= nCount; nIndex++)
        {
            ColumnExpressionData entry = _colExpDatas[nIndex];

            if (!entry.HasMacro)
            {
                continue;
            }

            if (excludeRels
                && (entry.RelativeColIds.UpperBound > 0 || entry.RelativeInputColIds.UpperBound > 0))
            {
                continue;
            }

            int nVarCnt = entry.Vars.UpperBound;
            for (int nVarIndex = 1; nVarIndex <= nVarCnt; nVarIndex++)
            {
                if (!string.Equals(entry.Vars[nVarIndex].Name, variableName, StringComparison.Ordinal))
                {
                    continue;
                }

                // :L1037-L1041 - find the expression's existing slot, or land one past the end. PowerScript
                // leaves the loop variable at c+1 when the scan completes, and C# leaves it at the same
                // place, so the write below either overwrites the same value harmlessly or appends.
                int c = colExps.UpperBound;
                int i;
                for (i = 1; i <= c; i++)
                {
                    if (colExps[i] == nIndex)
                    {
                        break;
                    }
                }

                colExps[i] = nIndex;

                // :L1042 - the second heuristic early-out.
                if (i == nCount)
                {
                    return nCount;
                }

                break;
            }
        }

        // :L1048-L1061 - and then variables that reference THIS variable, recursively. A change to a
        // variable therefore reaches expressions that only mention it at two removes.
        int variableCount = _globalVars.UpperBound;
        for (int nIndex = 1; nIndex <= variableCount; nIndex++)
        {
            if (nIndex == index)
            {
                continue;
            }

            GlobalVariable candidate = _globalVars[nIndex];

            if (candidate.VarType == ExpressionVariableEnvironment.VAR_FOREIGN
                || !candidate.Local.HasMacro)
            {
                continue;
            }

            foreach (VariableReference reference in candidate.Local.Vars)
            {
                if (!string.Equals(reference.Name, variableName, StringComparison.Ordinal))
                {
                    continue;
                }

                FindVarDepends(nIndex, colExps, excludeRels, visited);
                break;
            }

            // :L1060 - stop as soon as everything is collected.
            if (colExps.UpperBound == _colExpDatas.UpperBound)
            {
                break;
            }
        }

        return colExps.UpperBound;
    }

    /// <summary>
    /// <c>_of_hasref(string exp, readonly string colname)</c> [:L1800-L1855] - does an expression reference
    /// a column by name?
    /// </summary>
    /// <param name="exp">The expression text.</param>
    /// <param name="colname">The column name to look for.</param>
    /// <remarks>
    /// <para>
    /// A THIRD SCANNER, WITH ITS OWN DELIMITER SET AND ITS OWN QUOTE RULE, and none of the three is
    /// interchangeable. This one lower-cases the expression [:L1823] but NOT the column name, so the caller
    /// must pass an already-normalised name - which registration guarantees [:L1538] and which is why the
    /// name is stored lower-cased.
    /// </para>
    /// <para>
    /// THE QUOTE HANDLING DIFFERS FROM THE MACRO SCANNER'S IN A WAY THAT MATTERS: this one sets the word
    /// start to 0 when it sees a quote [:L1838] AND skips quoted characters entirely [:L1841, :L1847], so a
    /// column name inside a string literal is never matched. The macro scanner's equivalent has the hole
    /// described in defect (j); this one does not.
    /// </para>
    /// <para>
    /// WHOLE WORDS ONLY. A word is compared only when a delimiter ends it, so a column called <c>n1</c> is
    /// not found inside <c>n10</c>. The trailer is what lets a name at the very end of the expression be
    /// compared at all [:L1823].
    /// </para>
    /// </remarks>
    private static bool HasRef(string? exp, string? colname)
    {
        string columnName = colname ?? string.Empty;
        string text = (exp ?? string.Empty).ToLowerInvariant() + TRAILER;
        int nLen = Len(text);
        int nPosBegin = 1;
        bool bQuoted = false;
        string sQuote = string.Empty;

        for (int nPos = 1; nPos <= nLen; nPos++)
        {
            string sWord = Mid(text, nPos, 1);

            if (sWord is "'" or "\"")
            {
                if (string.Equals(sQuote, sWord, StringComparison.Ordinal) || sQuote.Length == 0)
                {
                    bQuoted = !bQuoted;
                    sQuote = bQuoted ? sWord : string.Empty;

                    // :L1838 - INSIDE the matching-quote test, so an unmatched quote character does not
                    // reset the word start. That is a real distinction from the toggle above it.
                    nPosBegin = 0;
                }

                continue;
            }

            if (sWord.Length == 1 && ReferenceScanDelimiters.Contains(sWord[0]))
            {
                if (bQuoted)
                {
                    continue;
                }

                if (nPosBegin > 0)
                {
                    if (string.Equals(
                        Mid(text, nPosBegin, nPos - nPosBegin),
                        columnName,
                        StringComparison.Ordinal))
                    {
                        return true;
                    }

                    nPosBegin = 0;
                }

                continue;
            }

            if (bQuoted)
            {
                continue;
            }

            if (nPosBegin == 0)
            {
                nPosBegin = nPos;
            }
        }

        return false;
    }


    // =================================================================================================
    //  CALCULATION - _of_calcitem [:L679-L865], of_calc, of_calcall, of_calcempty
    // =================================================================================================

    /// <summary>
    /// <c>public function boolean _of_calcitem(readonly long row, readonly integer index)</c>
    /// [:L142 declaration, :L679-L865 body] - calculate ONE cell and write the result.
    /// </summary>
    /// <param name="row">The one-based row.</param>
    /// <param name="index">The one-based expression index.</param>
    /// <param name="cancellationToken">Cancels a macro invocation mid-calculation.</param>
    /// <returns>
    /// True when a value was written, false in every other case - including "the value did not change",
    /// which is not distinguished from "the calculation refused".
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE NAME AND THE VISIBILITY ARE BOTH THE ORACLE'S, DELIBERATELY (C-B, defect d; AAP section 0.6.2.4
    /// calls it "an anomaly to carry rather than tidy"). The oracle declares it <c>public</c> despite the
    /// leading underscore its own convention reserves for private members, and C-04 exposes it as a
    /// <c>CalcItem</c> RPC - so it is genuinely part of the surface and renaming it would break both the
    /// contract and every characterization recording that names it. The root <c>.editorconfig</c> scopes the
    /// naming-analyzer suppression to this file for exactly this member and the SCREAMING_SNAKE constants.
    /// </para>
    /// <para>
    /// IT RETURNS bool AND NOT A RetCode, which is also the oracle's choice, and false is heavily
    /// overloaded: not dirty, recursion refused, the wrong emptiness for the current pass, an unknown column
    /// type, an aborted preprocess, an evaluator sentinel, an unchanged value, or a vetoing ItemChanged
    /// handler. Only the caller at :L2021 reads it at all, and it reads it as "did anything happen".
    /// </para>
    /// </remarks>
    public async ValueTask<bool> _of_calcitem(
        long row,
        int index,
        CancellationToken cancellationToken = default)
    {
        if (!_colExpDatas.IsValidIndex(index))
        {
            // The oracle indexes unguarded and would fault. Every internal caller passes an index it took
            // from a live enumeration, so this arm is reachable only from the C-04 CalcItem RPC, where a
            // caller-supplied index must not be able to fault the service.
            return false;
        }

        ColumnExpressionData data = _colExpDatas[index];
        DataWindowServiceHost host = RequireHost();

        // :L680 - during an empty-only pass, an expression whose column already holds a value is skipped.
        if (_rowCalcingEmpty && !data.Empty)
        {
            return false;
        }

        if (!_rowCalcing)
        {
            // :L682-L691 - OUTSIDE a row pass, re-entry is refused by scanning the calculation stack for
            // this expression's own column name. That is what stops `n1 = n2` and `n2 = n1` looping
            // forever, and the recursive flag is the opt-out.
            if (!data.Recursive)
            {
                int nCount = _calcStack.Depth;
                for (int nIndex = 1; nIndex <= nCount; nIndex++)
                {
                    if (string.Equals(_calcStack.GetAt(nIndex), data.Name, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }
        }
        else
        {
            // :L693-L698 - INSIDE a row pass the rule is completely different: the dirty flag is the
            // one-shot token, so each expression calculates at most once per row regardless of how many
            // columns reach it.
            if (!data.Dirty)
            {
                return false;
            }

            data.Dirty = false;

            // PRESERVED DEFECT (h) - :L694-L697 clears the dirty flag of the expression at the LOOP
            // COUNTER's index rather than the one the duplicate group names:
            //
            //      nCount = UpperBound(ColExpDatas[index].dupExps)
            //      for nIndex = 1 to nCount
            //          ColExpDatas[nIndex].dirty = false        <- nIndex, not dupExps[nIndex]
            //      next
            //
            // The correct form appears at :L2059 in of_calcempty, which proves this one is a slip rather
            // than a convention. The consequence is that calculating a member of a duplicate group clears
            // the dirty flags of the FIRST n expressions in registration order, so some expressions can be
            // skipped for the rest of the row pass and some group members are not suppressed at all. It is
            // reproduced exactly, loop bound and index expression alike (C-B).
            int dupCount = data.DupExps.UpperBound;
            for (int nIndex = 1; nIndex <= dupCount; nIndex++)
            {
                if (_colExpDatas.IsValidIndex(nIndex))
                {
                    _colExpDatas[nIndex].Dirty = false;
                }
            }
        }

        // :L699-L703 - the DataWindow object handle is probed by touching a property inside a try/catch.
        // A replaced DataWindow object model invalidates every cached handle, and this is the oracle's
        // detection: there is no validity API, so it provokes the failure and catches it.
        bool bDwoValid = false;
        try
        {
            _ = data.Dwo?.Name ?? throw new InvalidOperationException(
                "The cached DataWindow object handle is unset.");
            bDwoValid = true;
        }
        catch (Exception exception) when (IsObjectModelFault(exception))
        {
            // Swallowed, exactly as `catch(throwable ex)` [:L703] swallows it. The recovery is below.
            // THE BREADTH IS THE ORACLE'S: `throwable` is PowerBuilder's root type, so its catch takes
            // everything, and the host contract deliberately does not fix which exception a missing
            // object raises - only that it MUST raise. Narrowing to a chosen type here would turn a
            // host's implementation choice into a crash on a path the oracle recovers from.
            _ = exception;
        }

        // :L705 - the cache is used only when it is switched on AND the expression has no macro, because a
        // compute object is evaluated by the DataWindow engine, which knows nothing about this service's
        // variables or its application callbacks.
        bool bUseCache = data.Cacheable && !data.HasMacro;

        if (!bDwoValid)
        {
            // :L707-L713 - re-resolve, and re-create the compute object if it went with the object model.
            data.Dwo = GetDataWindowObject(data.Name);

            if (bUseCache
                && !HasDataWindowObject(data.ComputeName)
                && !CreateCompute(data.ComputeName, data.Exp, out string createError))
            {
                RaiseError(
                    ParseErrorFormatter.CreatePlainError(
                        ExpressionErrorSite.CreateExpressionCacheFailed,
                        data.Name,
                        data.Exp,
                        createError));
                return false;
            }
        }

        string? sVal;
        string sExp = data.Exp;

        if (bUseCache)
        {
            // :L717-L740 - read the value the DataWindow engine already computed. The typed read is
            // selected by the BOUND column's type, not the compute's, and five of the six render through
            // String() before the write arms parse them back.
            try
            {
                if (!TryReadComputedValue(data, row, out sVal))
                {
                    // :L735-L736 - `case else return false`: an unknown column type has no read arm.
                    return false;
                }
            }
            catch (Exception exception) when (IsObjectModelFault(exception))
            {
                // :L738-L740. THE BREADTH IS THE ORACLE'S, for the same reason as the handle probe above:
                // :L738 is `catch(throwable ex2)`, PowerBuilder's ROOT type, so it takes everything a read
                // through a replaced object model can raise. The host contract fixes only that an unknown
                // object MUST raise, never which exception, so narrowing to a chosen list here would turn a
                // host's implementation choice into a crash on a path the oracle recovers from.
                //
                // PRESERVED DEFECT (i): the oracle's catch names `ex2` but the message formats
                // `ex.text` - the variable from the OUTER catch at :L703, which is populated only if THAT
                // try also failed. So the reported text is usually empty rather than the actual cache
                // error. The catalogue records `ex.text` as the argument source and the argument passed
                // here is therefore the outer condition's text, which is empty whenever the handle probe
                // succeeded. Reproduced, with the real exception routed to the log so the information is
                // not lost - logging is a channel the oracle did not have and cannot conflict with it.
                _logger?.LogDebug(
                    exception,
                    "Expression cache read failed for a bound column; the reported message reproduces the "
                        + "oracle's empty outer-exception text (n_cst_dwsvc_columnexp.sru:L739).");

                RaiseError(
                    ParseErrorFormatter.CreatePlainError(
                        ExpressionErrorSite.ExpressionCacheError,
                        data.Name,
                        data.Exp,
                        string.Empty));
                return false;
            }
        }
        else
        {
            if (data.HasMacro)
            {
                // :L743-L745 - preprocess, and ABORT on the empty string. Both context arguments are THIS
                // row and THIS column's object, and the context service is `this` - so a `@name` reference
                // in a column expression addresses this very DataWindow unless a peer supplied the context.
                sExp = await PreprocessExpAsync(
                        row,
                        data.Dwo,
                        data.Exp,
                        data.Vars,
                        data.Fns,
                        new ExpressionContext(row, data.Dwo, _handle),
                        allowChannelInvocation: true,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (sExp.Length == 0)
                {
                    return false;
                }
            }

            // :L749 - the DataWindow's own expression evaluator, reached through Describe("Evaluate(...)")
            // in the oracle [n_cst_dwsvc.sru:L217].
            sVal = EvaluateExpression(sExp, row);

            // :L751-L759 - THE TRACE. Gated on #Trace and nothing else.
            if (Trace)
            {
                // :L758 - the "(null)" sentinel, selected by Text.Iif. IT MUST BE THE METHOD AND NOT `?:`:
                // the oracle calls a FUNCTION, so both arms are evaluated before the choice is made, and
                // using a conditional expression here would change nothing today but would diverge the
                // moment either arm acquired a side effect. Two conditions select it - an empty value on a
                // column that treats empty as null, or an empty value on any non-string column - and a
                // string column without NilIsNull therefore traces "" rather than "(null)".
                string traced = Text.Iif(
                    sVal.Length == 0
                        && (data.EmptyStringIsNull
                            || data.ColType != DataWindowServiceBase.COL_TYPE_STRING),
                    "(null)",
                    sVal) ?? string.Empty;

                // :L753-L757 - the call stack is the stack frames joined with ">" and then the DataWindow
                // OBJECT'S name appended, so THE PAYLOAD HAS NO TRAILING ">". The session builds it from
                // the same stack this engine pushed onto, which is why the terminal name is passed in
                // rather than assumed.
                // THE OBSERVABLE TEXT, RENDERED FROM THE EXECUTED ONE. A dynamically expanded bound value
                // reaches the evaluator as a placeholder; the trace is a C-04 event a client consumes and a
                // characterization recording compares, so it carries the expression the oracle would have
                // produced - the value spliced in, unescaped, exactly as at :L2213.
                _session.EmitTrace(_handle, row, data.Dwo, RenderBoundText(sExp), traced);
            }

            // :L760-L763 - the evaluator's two failure markers. DISTINCT FROM THE EMPTY STRING: these are
            // reported, that one is silent.
            if (string.Equals(sVal, UndeterminedValueSentinel, StringComparison.Ordinal)
                || string.Equals(sVal, InvalidExpressionSentinel, StringComparison.Ordinal))
            {
                RaiseError(
                    ParseErrorFormatter.CreatePlainError(
                        ExpressionErrorSite.ExpressionError,
                        data.Name,
                        sExp));
                return false;
            }
        }

        // :L766-L854 - THE SIX TYPED WRITE ARMS. Each one: coerce the text, read the current value, refuse
        // if unchanged, optionally ask the host's ItemChanged handler for permission, then write.
        if (!await TryWriteCalculatedValueAsync(host, data, row, sVal, cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        // :L856-L858 - the host's own post-write notification, with THIS SERVICE DISABLED AROUND IT. The
        // suppression is what stops the notification re-entering this engine: the host's handler is free to
        // touch the DataWindow, and any item change it causes finds the service disabled and returns at
        // :L214. Note it force-enables afterwards rather than restoring the previous state, which is
        // harmless because reaching here at all required the service to be enabled.
        Enabled = false;
        try
        {
            host.OnDoItemChanged(row, data.Dwo!);
        }
        finally
        {
            Enabled = true;
        }

        // :L860-L862 - and finally the cascade: this expression just changed its own column, so anything
        // depending on THAT column must recalculate. frominput is FALSE, because a calculation is not user
        // input - which is exactly what stops an input-triggered expression firing off a calculated change.
        // Suppressed during a row pass, where the dirty flags already sequence everything.
        if (!_rowCalcing)
        {
            await OnDoItemChangedAsync(row, data.Name, data.Id, false, cancellationToken)
                .ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// The six typed write arms of <c>_of_calcitem</c> [:L766-L854], factored out so the calculation body
    /// stays readable. The behaviour is unchanged, arm for arm.
    /// </summary>
    /// <param name="host">The DataWindow host.</param>
    /// <param name="data">The expression whose value is being written.</param>
    /// <param name="row">The one-based row.</param>
    /// <param name="value">The evaluated text.</param>
    /// <param name="cancellationToken">Unused by the write itself; carried for symmetry with the caller.</param>
    /// <returns>True when a value was written; false for an unchanged value, a veto or an unknown type.</returns>
    /// <remarks>
    /// <para>
    /// THE EMPTY STRING BECOMES NULL FOR FIVE OF THE SIX TYPES, unconditionally [:L768, :L781, :L809,
    /// :L823, :L837]. The string arm is the exception: it nulls an empty value ONLY when the column declares
    /// NilIsNull [:L830-L832], so a plain string column stores the empty string.
    /// </para>
    /// <para>
    /// THE TWO EQUALITY ARMS COLLAPSE INTO ONE IN C# AND THE RESULT IS IDENTICAL. The oracle tests
    /// <c>if new = old then return false</c> and then <c>elseif IsNull(new) and IsNull(old) then return
    /// false</c>, because PowerScript's <c>=</c> answers NULL - not true - when either side is null, so the
    /// second arm is what catches two nulls. C#'s lifted <c>==</c> answers TRUE for two nulls, so the first
    /// arm catches them instead. Both paths return false and write nothing, so the observable behaviour is
    /// the same; the arms are kept separate below anyway, so the oracle's structure stays legible.
    /// </para>
    /// <para>
    /// THE VETO IS REAL. When trigger-event is set, a non-zero answer from the host's ItemChanged handler
    /// abandons the write and the column keeps its old value.
    /// </para>
    /// </remarks>
    private async ValueTask<bool> TryWriteCalculatedValueAsync(
        DataWindowServiceHost host,
        ColumnExpressionData data,
        long row,
        string? value,
        CancellationToken cancellationToken)
    {
        // The value is only ever read synchronously below; the await keeps the signature honest for the
        // ItemChanged veto, which a host may implement asynchronously in a future phase.
        await ValueTask.CompletedTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // SetNull(sVal) [:L768 and the four siblings]. Modelled as a nullable local rather than by mutating
        // the argument, so the null cannot leak into the trace that already happened.
        string? text = value;
        bool emptyIsNull = text is { Length: 0 };

        if (data.ColType == DataWindowServiceBase.COL_TYPE_DECIMAL)
        {
            if (emptyIsNull)
            {
                text = null;
            }

            decimal? newValue = PbDec(text);
            decimal? oldValue = AsDecimal(ReadRawItem(row, data));

            if (newValue == oldValue)
            {
                return false;
            }

            if (data.TriggerEvent && host.ItemChanged(row, data.Dwo!, text!) != 0)
            {
                return false;
            }

            host.SetItem(row, data.Id, newValue);
            return true;
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_INTEGER)
        {
            if (emptyIsNull)
            {
                text = null;
            }

            // :L783 - Long(), so a fractional evaluated value is TRUNCATED on an integer column.
            long? newValue = PbLong(text);
            long? oldValue = AsLong(ReadRawItem(row, data));

            if (newValue == oldValue)
            {
                return false;
            }

            if (data.TriggerEvent && host.ItemChanged(row, data.Dwo!, text!) != 0)
            {
                return false;
            }

            host.SetItem(row, data.Id, newValue);
            return true;
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_STRING)
        {
            // :L830-L832 - the conditional null, and the only arm where emptiness is not automatically null.
            if (emptyIsNull && data.EmptyStringIsNull)
            {
                text = null;
            }

            string? oldValue = AsString(ReadRawItem(row, data));

            if (string.Equals(text, oldValue, StringComparison.Ordinal))
            {
                return false;
            }

            if (data.TriggerEvent && host.ItemChanged(row, data.Dwo!, text!) != 0)
            {
                return false;
            }

            host.SetItem(row, data.Id, text);
            return true;
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_DATETIME)
        {
            if (emptyIsNull)
            {
                text = null;
            }

            DateTime? newValue = PbDateTime(text);
            DateTime? oldValue = AsDateTime(ReadRawItem(row, data));

            if (newValue == oldValue)
            {
                return false;
            }

            if (data.TriggerEvent && host.ItemChanged(row, data.Dwo!, text!) != 0)
            {
                return false;
            }

            host.SetItem(row, data.Id, newValue);
            return true;
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_DATE)
        {
            if (emptyIsNull)
            {
                text = null;
            }

            DateOnly? newValue = PbDate(text);
            DateOnly? oldValue = AsDate(ReadRawItem(row, data));

            if (newValue == oldValue)
            {
                return false;
            }

            if (data.TriggerEvent && host.ItemChanged(row, data.Dwo!, text!) != 0)
            {
                return false;
            }

            host.SetItem(row, data.Id, newValue);
            return true;
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_TIME)
        {
            if (emptyIsNull)
            {
                text = null;
            }

            TimeOnly? newValue = PbTime(text);
            TimeOnly? oldValue = AsTime(ReadRawItem(row, data));

            if (newValue == oldValue)
            {
                return false;
            }

            if (data.TriggerEvent && host.ItemChanged(row, data.Dwo!, text!) != 0)
            {
                return false;
            }

            host.SetItem(row, data.Id, newValue);
            return true;
        }

        // :L852-L853 - `case else return false`. An unknown or unsupported column type writes nothing, and
        // notably does NOT report an error: it is how the service ignores a column it cannot type.
        return false;
    }

    /// <summary>
    /// <c>of_calc(readonly long row, readonly integer index)</c> [:L348-L373] - calculate ONE expression on
    /// one row.
    /// </summary>
    /// <param name="row">The one-based row.</param>
    /// <param name="index">The one-based expression index.</param>
    /// <param name="cancellationToken">Cancels a macro invocation.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> - even when the calculation wrote nothing [:L372]; the boolean answer of
    /// <c>_of_calcitem</c> is DISCARDED here. Or <see cref="RetCode.E_INVALID_ARGUMENT"/> for a bad row,
    /// <see cref="RetCode.E_OUT_OF_BOUND"/> for a bad index, or <see cref="RetCode.FAILED"/> while a row
    /// pass is running.
    /// </returns>
    /// <remarks>
    /// NOTE THE ROW GUARD USES E_INVALID_ARGUMENT [:L366] WHILE THE ROW-WIDE ENTRY POINT USES
    /// E_OUT_OF_RANGE FOR THE SAME CONDITION [:L1159]. Two codes for one kind of mistake, and both are
    /// preserved: a caller distinguishing them is reading a real difference in the oracle.
    /// </remarks>
    public async ValueTask<long> CalcAsync(
        long row,
        int index,
        CancellationToken cancellationToken = default)
    {
        if (row <= 0 || row > RequireHost().RowCount())
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        if (!_colExpDatas.IsValidIndex(index))
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        // :L368 - a row pass owns the dirty flags, so a single-item calculation inside one would corrupt
        // the sequencing.
        if (_rowCalcing)
        {
            return RetCode.FAILED;
        }

        await _of_calcitem(row, index, cancellationToken).ConfigureAwait(false);

        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_calc(readonly long row, readonly string colname)</c> [:L375] - by column name.
    /// </summary>
    /// <param name="row">The one-based row.</param>
    /// <param name="colname">The bound column's name.</param>
    /// <param name="cancellationToken">Cancels a macro invocation.</param>
    /// <remarks>
    /// AN UNKNOWN NAME RESOLVES TO INDEX 0 and therefore answers <see cref="RetCode.E_OUT_OF_BOUND"/> - but
    /// only AFTER the row guard has passed, so a bad row on an unknown column reports the row.
    /// </remarks>
    public ValueTask<long> CalcAsync(
        long row,
        string? colname,
        CancellationToken cancellationToken = default) =>
        CalcAsync(row, FindExpIndex(colname), cancellationToken);

    /// <summary>
    /// <c>of_calc(readonly long row, readonly boolean force)</c> [:L1138-L1177] - calculate EVERY expression
    /// on one row.
    /// </summary>
    /// <param name="row">The one-based row.</param>
    /// <param name="force">
    /// When false, expressions that trigger only on user input are SKIPPED [:L1168-L1170].
    /// </param>
    /// <param name="cancellationToken">Cancels a macro invocation.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/>; <see cref="RetCode.FAILED"/> when the service is disabled [:L1158] or a row
    /// pass is already running [:L1160]; or <see cref="RetCode.E_OUT_OF_RANGE"/> for a bad row [:L1159].
    /// </returns>
    /// <remarks>
    /// <para>
    /// A DISABLED SERVICE ANSWERS <see cref="RetCode.FAILED"/> - not PREVENT, not E_NO_SUPPORT [:L1158].
    /// The same code appears at :L1201, :L1976 and :L2087, so all four row-level entry points agree.
    /// </para>
    /// <para>
    /// THIS IS THE PASS THAT SETS <c>_bRowCalcing</c>, and setting it changes <c>_of_calcitem</c>'s entire
    /// re-entry model from a stack scan to the dirty flag [:L682 against :L692]. The dirty flags are raised
    /// once, before the loop [:L1164], so each expression calculates at most once no matter how many
    /// cascades reach it.
    /// </para>
    /// <para>
    /// THE try/finally AROUND THE FLAG IS AN ADDITION FOR THE SAME REASON THE REDRAW ONE IS: the oracle's
    /// clear at :L1174 follows a synchronous loop that cannot fail in transit, and leaving the flag set
    /// after a fault would deadlock every later calculation against the <c>_bRowCalcing</c> guards.
    /// </para>
    /// </remarks>
    public async ValueTask<long> CalcAsync(
        long row,
        bool force,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return RetCode.FAILED;
        }

        if (row < 1 || row > RequireHost().RowCount())
        {
            return RetCode.E_OUT_OF_RANGE;
        }

        if (_rowCalcing)
        {
            return RetCode.FAILED;
        }

        _rowCalcing = true;

        try
        {
            MakeDirty();

            int nCount = _colExpDatas.UpperBound;
            for (int nIndex = 1; nIndex <= nCount; nIndex++)
            {
                if (!force && _colExpDatas[nIndex].RelativeInputColIds.UpperBound > 0)
                {
                    continue;
                }

                await _of_calcitem(row, nIndex, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _rowCalcing = false;
        }

        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_calc(readonly long row)</c> [:L1179] - the one-argument arity, which does NOT force.
    /// </summary>
    /// <param name="row">The one-based row.</param>
    /// <param name="cancellationToken">Cancels a macro invocation.</param>
    /// <remarks>
    /// THIS IS THE ENTRY POINT THE LEGACY SPECIFICATION'S WORKED $/$$ EXAMPLES USE -
    /// <c>dw_1.ColumnExp.of_Calc(1)</c> [docs/n_cst_dwsvc_columnexp.md:L53, L72] - so it is the one the two
    /// golden parity assertions drive.
    /// </remarks>
    public ValueTask<long> CalcAsync(long row, CancellationToken cancellationToken = default) =>
        CalcAsync(row, false, cancellationToken);

    /// <summary>
    /// <c>of_calcall(readonly boolean force)</c> [:L1182-L1209] - calculate every expression on every row.
    /// </summary>
    /// <param name="force">Passed to each row pass.</param>
    /// <param name="cancellationToken">Cancels a macro invocation.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/>, or <see cref="RetCode.FAILED"/> when the service is disabled [:L1201].
    /// </returns>
    /// <remarks>
    /// EACH ROW'S RESULT IS DISCARDED [:L1205], so a row that failed does not stop the pass and does not
    /// change the answer. Reproduced: a caller cannot learn which rows calculated, only that the pass ran.
    /// </remarks>
    public async ValueTask<long> CalcAllAsync(
        bool force,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return RetCode.FAILED;
        }

        long nRowCnt = RequireHost().RowCount();
        for (long nRow = 1; nRow <= nRowCnt; nRow++)
        {
            await CalcAsync(nRow, force, cancellationToken).ConfigureAwait(false);
        }

        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_calcall()</c> [:L378] - the no-argument arity, which does NOT force.
    /// </summary>
    /// <param name="cancellationToken">Cancels a macro invocation.</param>
    public ValueTask<long> CalcAllAsync(CancellationToken cancellationToken = default) =>
        CalcAllAsync(false, cancellationToken);

    /// <summary>
    /// <c>of_calcempty(readonly long row)</c> [:L1956-L2066] - calculate only the expressions whose bound
    /// column is currently EMPTY.
    /// </summary>
    /// <param name="row">The one-based row.</param>
    /// <param name="cancellationToken">Cancels a macro invocation.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/>; <see cref="RetCode.FAILED"/> when the service is disabled [:L1976] or an
    /// empty-only pass is already running [:L1978]; or <see cref="RetCode.E_OUT_OF_RANGE"/> for a bad row.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE "FILL IN THE BLANKS" PASS, used after a retrieve to populate calculated columns that
    /// arrived empty without disturbing ones the database already filled. It runs in two phases: first it
    /// records emptiness for EVERY expression [:L1982-L2017], then it calculates [:L2019-L2061]. The two
    /// phases cannot be merged, because calculating one column can fill another and the recorded emptiness
    /// must be the state BEFORE the pass.
    /// </para>
    /// <para>
    /// EMPTINESS IS TYPE-DEPENDENT AND THE ORACLE IS INCONSISTENT ABOUT IT ON PURPOSE. Numeric columns test
    /// IsNull only, so a zero is NOT empty; string columns test the empty string, so both null and "" are
    /// empty; the three temporal types compare against the INVALID-VALUE sentinel that PowerScript's
    /// conversion of an empty string produces, so 1900-01-01 counts as empty. The commented-out
    /// <c>IsEmptyOrNull</c> alternatives at :L1987 and :L1989 show the author considered unifying them and
    /// did not; they stay commented (C-B).
    /// </para>
    /// <para>
    /// THE SECOND PHASE'S DUPLICATE-GROUP CLEARING IS THE CORRECT FORM [:L2059] - the one defect (h) gets
    /// wrong in <c>_of_calcitem</c>. It also re-reads emptiness AFTER calculating [:L2025-L2056] and only
    /// suppresses the group when the value actually landed, so a duplicate that produced nothing leaves its
    /// siblings free to try.
    /// </para>
    /// </remarks>
    public async ValueTask<long> CalcEmptyAsync(
        long row,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return RetCode.FAILED;
        }

        DataWindowServiceHost host = RequireHost();

        if (row < 1 || row > host.RowCount())
        {
            return RetCode.E_OUT_OF_RANGE;
        }

        if (_rowCalcingEmpty)
        {
            return RetCode.FAILED;
        }

        _rowCalcingEmpty = true;

        try
        {
            int nCount = _colExpDatas.UpperBound;

            // :L1982-L2017 - phase one: dirty everything and record emptiness.
            for (int nIndex = 1; nIndex <= nCount; nIndex++)
            {
                ColumnExpressionData entry = _colExpDatas[nIndex];
                entry.Dirty = true;
                entry.Empty = IsBoundColumnEmpty(entry, row);
            }

            // :L2019-L2061 - phase two.
            for (int nIndex = 1; nIndex <= nCount; nIndex++)
            {
                ColumnExpressionData entry = _colExpDatas[nIndex];

                if (!entry.Empty || !entry.Dirty)
                {
                    continue;
                }

                if (!await _of_calcitem(row, nIndex, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                // :L2022 - the last expression has no siblings left to suppress.
                if (nIndex == nCount)
                {
                    break;
                }

                int nCount2 = entry.DupExps.UpperBound;
                if (nCount2 == 0)
                {
                    continue;
                }

                // :L2025-L2057 - re-read emptiness. A calculation that wrote nothing usable leaves the
                // column empty, and in that case the siblings must still be allowed to run.
                if (IsBoundColumnEmpty(entry, row))
                {
                    continue;
                }

                // :L2058-L2060 - THE CORRECT INDEX EXPRESSION, unlike :L697.
                for (int nIndex2 = 1; nIndex2 <= nCount2; nIndex2++)
                {
                    int member = (int)entry.DupExps[nIndex2];
                    if (_colExpDatas.IsValidIndex(member))
                    {
                        _colExpDatas[member].Dirty = false;
                    }
                }
            }
        }
        finally
        {
            _rowCalcingEmpty = false;
        }

        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_calcempty()</c> [:L2068-L2095] - the empty-only pass over every row.
    /// </summary>
    /// <param name="cancellationToken">Cancels a macro invocation.</param>
    /// <returns><see cref="RetCode.OK"/>, or <see cref="RetCode.FAILED"/> when disabled [:L2087].</returns>
    public async ValueTask<long> CalcEmptyAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return RetCode.FAILED;
        }

        long nRowCnt = RequireHost().RowCount();
        for (long nRow = 1; nRow <= nRowCnt; nRow++)
        {
            await CalcEmptyAsync(nRow, cancellationToken).ConfigureAwait(false);
        }

        return RetCode.OK;
    }

    /// <summary>
    /// The emptiness test of <c>of_calcempty</c>, shared by both of its phases [:L1985-L2016 and
    /// :L2025-L2056, which are the same switch written twice].
    /// </summary>
    /// <param name="data">The expression whose bound column is tested.</param>
    /// <param name="row">The one-based row.</param>
    /// <returns>True when the column holds no value by that type's own definition of "no value".</returns>
    /// <remarks>
    /// THE DEFAULT ARM DIFFERS BETWEEN THE TWO PHASES IN THE ORACLE: phase one answers FALSE for an unknown
    /// column type [:L2015] and phase two answers TRUE [:L2055]. The difference is unobservable, because an
    /// expression on an unknown type is filtered out by phase one's answer before phase two can read it -
    /// <c>if Not ColExpDatas[nIndex].empty then continue</c> [:L2020] - so phase two's arm is dead code.
    /// FALSE is used here, which is the reachable one; the divergence is recorded rather than reproduced
    /// because reproducing an unreachable difference would require two methods that can never disagree.
    /// </remarks>
    private bool IsBoundColumnEmpty(ColumnExpressionData data, long row)
    {
        object? raw = ReadRawItem(row, data);

        if (data.ColType == DataWindowServiceBase.COL_TYPE_DECIMAL)
        {
            return AsDecimal(raw) is null;
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_INTEGER)
        {
            return AsLong(raw) is null;
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_STRING)
        {
            // :L1991-L1995 - the empty string counts, and so does null, because PowerScript's `<> ""` on a
            // null answers NULL and the else branch is taken.
            return string.IsNullOrEmpty(AsString(raw));
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_DATETIME)
        {
            // :L1997 - compared against DateTime(""), which is PowerScript's invalid-datetime sentinel.
            DateTime? current = AsDateTime(raw);
            return current is null || current == InvalidDateTimeSentinel;
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_DATE)
        {
            DateOnly? current = AsDate(raw);
            return current is null || current == InvalidDateSentinel;
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_TIME)
        {
            TimeOnly? current = AsTime(raw);
            return current is null || current == InvalidTimeSentinel;
        }

        return false;
    }


    // =================================================================================================
    //  PREPROCESSING - _of_preprocessexp [:L2148-L2324]
    // =================================================================================================
    //
    //  This is where a DYNAMIC reference finally becomes a value, and it runs on EVERY calculation of an
    //  expression that has one - which is the whole cost, and the whole point, of dynamic expansion.
    //
    //  TWO PASSES, IN THIS ORDER AND NOT THE OTHER. Variables are substituted first [:L2179-L2224] and
    //  functions second [:L2226-L2321], because a function's ARGUMENTS may contain variable references that
    //  must be resolved before the arguments are evaluated - which is what makes
    //  `$FormatPrice(n2, $精度)` work [docs/n_cst_dwsvc_columnexp.md:L117]. Each pass also rewrites the
    //  tokens of everything the other pass still has to do, so the two are coupled through the token text
    //  rather than being independent.
    //
    //  THE FUNCTION PASS RUNS BACKWARDS [:L2226]. A nested call's token is a SUBSTRING of its enclosing
    //  call's token, so the inner one must be substituted first and the outer one's stored token must then
    //  be rewritten to match [:L2312-L2320]. Walking forwards would leave the outer token unmatchable.

    /// <summary>
    /// <c>_of_preprocessexp</c> [:L2148-L2324] - resolve every dynamic reference and every macro function in
    /// one expression, producing the text the evaluator will be handed.
    /// </summary>
    /// <param name="row">The row being calculated.</param>
    /// <param name="dwo">The column being calculated; its name appears in every reported message.</param>
    /// <param name="exp">The stored expression text.</param>
    /// <param name="vars">
    /// The retained variable references. WRITTEN BACK TO: a resolved global index is memoised into the
    /// caller's own list, reproducing the oracle's assignment through a <c>readonly</c> array parameter
    /// [:L2195] and its consequence - see the remarks.
    /// </param>
    /// <param name="fns">
    /// The retained function references. NOT written back: the oracle passes this one BY VALUE [:L2148,
    /// which omits <c>readonly</c> and so takes a copy], and the substitution rewrites tokens destructively,
    /// so the rewrites must not survive the call.
    /// </param>
    /// <param name="context">The <c>(ctx_row, ctx_dwo, ctx_expsvc)</c> triple, threaded through unchanged.</param>
    /// <param name="allowChannelInvocation">
    /// False on the synchronous <see cref="IExpressionServiceHost"/> path, where a macro invocation cannot be
    /// awaited. See section 7 of the header.
    /// </param>
    /// <param name="cancellationToken">Cancels a macro invocation.</param>
    /// <returns>The preprocessed expression, or <see cref="AbortSentinel"/>.</returns>
    /// <remarks>
    /// THE MEMOISED INDEX CAN GO STALE, AND THAT IS PRESERVED (C-B, defect m). :L2195 caches the resolved
    /// global index onto the reference so later calculations skip the lookup. Nothing invalidates it: a
    /// variable removed and redefined shifts the table, and the memo then points at whatever now occupies
    /// that slot. The oracle has no removal entry point for variables, which is why it has never bitten - but
    /// the cache write is real and observable, so it is reproduced rather than dropped.
    /// </remarks>
    private async ValueTask<string> PreprocessExpAsync(
        long row,
        IDataWindowObject? dwo,
        string exp,
        OneBasedList<VariableReference> vars,
        OneBasedList<FunctionReference> fns,
        ExpressionContext context,
        bool allowChannelInvocation,
        CancellationToken cancellationToken)
    {
        string result = exp;
        string dwoName = dwo?.Name ?? string.Empty;

        // :L2148 - the by-value copy of the function list. Every rewrite below lands here and is discarded
        // when the call returns, which is why the same expression preprocesses identically next time.
        List<FunctionReference> localFns = [.. fns.Items];

        // :L2176-L2177 - both counts are taken ONCE, before either pass.
        int nFnCnt = localFns.Count;
        int nVarCount = vars.UpperBound;

        for (int nVarIdx = 1; nVarIdx <= nVarCount; nVarIdx++)
        {
            VariableReference reference = vars[nVarIdx];
            string sVal;

            if (reference.IsCtx)
            {
                if (reference.IsMacro)
                {
                    // :L2181-L2183 - a context MACRO: the context service computes its own variable, with
                    // THIS engine and THIS row becoming the context for that computation. The nesting
                    // inverts, which is what lets two DataWindows reference each other's variables without
                    // either owning the other.
                    sVal = await CalcContextVariableValueAsync(
                            reference,
                            context,
                            row,
                            dwo,
                            allowChannelInvocation,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    // :L2184-L2186 - a context COLUMN: read the named column out of the context row. No
                    // variable table is involved at all; `@name` names a column, not a variable.
                    sVal = ContextGetItemExpValue(reference, context);
                }

                if (sVal.Length == 0)
                {
                    return AbortSentinel;
                }
            }
            else
            {
                int nGVarIdx;

                if (reference.Index == 0)
                {
                    // :L2189-L2195 - resolve and memoise. See the remarks on the staleness this introduces.
                    nGVarIdx = FindVarIndex(reference.Name);
                    if (nGVarIdx == 0)
                    {
                        RaiseError(
                            ParseErrorFormatter.CreatePlainError(
                                ExpressionErrorSite.PreprocessVariableUndefined,
                                dwoName,
                                reference.Name));
                        return AbortSentinel;
                    }

                    reference = reference with { Index = nGVarIdx };
                    vars[nVarIdx] = reference;
                }
                else
                {
                    nGVarIdx = reference.Index;
                }

                if (!_globalVars.IsValidIndex(nGVarIdx))
                {
                    // Reachable only through a stale memo - see the remarks. The oracle would index past the
                    // table and fault; refusing the calculation keeps a caller-visible defect from becoming
                    // a caller-triggerable process kill, and the abort is the same answer every other
                    // resolution failure in this method gives.
                    LogChannelUnavailable(
                        "a memoised global variable index no longer addresses the variable table");
                    return AbortSentinel;
                }

                GlobalVariable variable = _globalVars[nGVarIdx];

                if (variable.VarType == ExpressionVariableEnvironment.VAR_FOREIGN)
                {
                    // :L2199-L2201 - a foreign variable is computed by the service that owns it.
                    sVal = await CalcForeignVariableValueAsync(
                            variable.Foreign,
                            row,
                            dwo,
                            allowChannelInvocation,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (sVal.Length == 0)
                    {
                        return AbortSentinel;
                    }
                }
                else if (variable.Local.HasMacro)
                {
                    // :L2202-L2204 - a variable whose own value expression contains macros is preprocessed
                    // recursively, WITH THE SAME CONTEXT TRIPLE. That is what makes `@` inside a variable
                    // address the original context rather than the variable's owner.
                    sVal = await PreprocessExpAsync(
                            row,
                            dwo,
                            variable.Local.Exp,
                            new OneBasedList<VariableReference>(
                                static () => new VariableReference(),
                                variable.Local.Vars),
                            new OneBasedList<FunctionReference>(
                                static () => new FunctionReference(),
                                variable.Local.Fns),
                            context,
                            allowChannelInvocation,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (sVal.Length == 0)
                    {
                        return AbortSentinel;
                    }
                }
                else
                {
                    // :L2205-L2211 - the ordinary case: the stored text IS the value.
                    //
                    // A VARIABLE BOUND FROM A TYPED VALUE SUBSTITUTES ITS PLACEHOLDER INSTEAD, which is what
                    // closes the DYNAMIC half of the same hole the static path closes at step 6 of ParseExp.
                    // Unlike that path this method tracks NO positions - every rewrite below is a whole-token
                    // ReplaceAll and every test is a containment test - so a placeholder of a different length
                    // from the literal is behaviourally inert here and no parallel string is needed.
                    //
                    // The empty-value refusal is still measured on the STORED text, because that is what the
                    // oracle measures [:L2206] and a bound value's rendered literal is never empty.
                    sVal = _boundVarValues.TryGetValue(reference.Name, out ExpressionValue boundDynamic)
                        && variable.Local.Exp.Length > 0
                            ? MintBoundValue(boundDynamic, variable.Local.Exp)
                            : variable.Local.Exp;

                    if (variable.Local.Exp.Length == 0)
                    {
                        RaiseError(
                            ParseErrorFormatter.CreatePlainError(
                                ExpressionErrorSite.PreprocessVariableValueInvalid,
                                dwoName,
                                reference.Name));
                        return AbortSentinel;
                    }
                }
            }

            // :L2213 - THE SAME PARENTHESIS WRAP AS THE STATIC PATH, and mandatory for the same reason: a
            // multi-term value substituted into a larger expression must not change its precedence.
            sVal = "(" + sVal + ")";

            // :L2214 - and the same five-argument keyword ReplaceAll, so whole-token semantics hold here too.
            result = Text.ReplaceAll(result, reference.FullName, sVal, true, true);

            // :L2215-L2223 - rewrite every function token and argument that mentions this variable, so the
            // function pass below can still find its own tokens in the rewritten text.
            for (int nFnIdx = 0; nFnIdx < nFnCnt; nFnIdx++)
            {
                FunctionReference fn = localFns[nFnIdx];

                if (Pos(fn.FullName, reference.FullName) <= 0)
                {
                    continue;
                }

                List<string> rewritten = new(fn.Args.Length);
                foreach (string arg in fn.Args)
                {
                    rewritten.Add(Text.ReplaceAll(arg, reference.FullName, sVal, true, true));
                }

                localFns[nFnIdx] = fn with
                {
                    FullName = Text.ReplaceAll(fn.FullName, reference.FullName, sVal, true, true),
                    Args = [.. rewritten],
                };
            }
        }

        // :L2226 - BACKWARDS. See the section banner.
        for (int nFnIdx = nFnCnt; nFnIdx >= 1; nFnIdx--)
        {
            FunctionReference fn = localFns[nFnIdx - 1];

            // :L2227-L2236 - every argument is EVALUATED first, so what crosses to the application is a
            // value and never an expression. An argument that fails evaluation aborts the whole calculation.
            List<string> sArgs = new(fn.Args.Length);
            foreach (string arg in fn.Args)
            {
                string argValue = EvaluateExpression(arg, row);

                if (string.Equals(argValue, InvalidExpressionSentinel, StringComparison.Ordinal)
                    || string.Equals(argValue, UndeterminedValueSentinel, StringComparison.Ordinal))
                {
                    RaiseError(
                        ParseErrorFormatter.CreatePlainError(
                            ExpressionErrorSite.PreprocessFunctionArgumentEvaluationFailed,
                            dwoName,
                            fn.FullName,
                            arg));
                    return AbortSentinel;
                }

                sArgs.Add(argValue);
            }

            // :L2237-L2309 - the dispatch switch. MacroInvoker owns the classification, so the three-way
            // split between a variable lookup, a dynamic invoke and a direct application call lives in one
            // place rather than being restated here.
            MacroDispatchPlan plan = MacroInvoker.Plan(
                fn,
                MacroArgumentList.FromEvaluated(sArgs));

            string rendered;

            switch (plan.Kind)
            {
                case MacroDispatchKind.Rejected:
                    RaiseError(plan.Error!);
                    return AbortSentinel;

                case MacroDispatchKind.VariableLookup:
                {
                    // :L2239-L2257 - the FUNC_VAR sentinel: `$$('name')`, where the variable's NAME is
                    // itself computed. THE CHANNEL IS NOT INVOLVED - this is entirely local work - which is
                    // why reading FUNC_VAR = "" as an inert placeholder is the natural and wrong reading.
                    string resolved = await ResolveVariableByNameAsync(
                            plan.VariableName,
                            dwoName,
                            row,
                            dwo,
                            context,
                            allowChannelInvocation,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (resolved.Length == 0)
                    {
                        return AbortSentinel;
                    }

                    rendered = resolved;
                    break;
                }

                case MacroDispatchKind.ClientCallback:
                {
                    // :L2258-L2308 - both the dynamic-invoke and the direct-call arms end here, because both
                    // raise OnColumnExpInvokeMethod on the application. Across the new boundary that is
                    // C-04's InvokeMethodChannel.
                    string? invoked = await InvokeMacroAsync(
                            plan,
                            row,
                            dwo,
                            allowChannelInvocation,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (invoked is null)
                    {
                        return AbortSentinel;
                    }

                    rendered = invoked;
                    break;
                }

                default:
                    throw new InvalidOperationException(
                        "A macro dispatch plan is a rejection, a variable lookup or a client callback; the "
                            + "oracle's switch has exactly those three arms "
                            + "(n_cst_dwsvc_columnexp.sru:L2237-L2308).");
            }

            // :L2310-L2311 - one wrap and one substitution, shared by every arm. The wrap is applied HERE
            // and not inside the rendering, which is why the invoker's already-wrapped rendering is
            // deliberately not used.
            string sVal = "(" + rendered + ")";
            result = Text.ReplaceAll(result, fn.FullName, sVal, true, true);

            // :L2312-L2320 - rewrite the tokens of the functions still to come. Only the ones BEFORE this
            // index, because the ones after it are already done - which is the other half of why the walk
            // runs backwards.
            for (int nIndex = 0; nIndex < nFnIdx - 1; nIndex++)
            {
                FunctionReference outer = localFns[nIndex];

                if (Pos(outer.FullName, fn.FullName) <= 0)
                {
                    continue;
                }

                List<string> rewritten = new(outer.Args.Length);
                foreach (string arg in outer.Args)
                {
                    rewritten.Add(Text.ReplaceAll(arg, fn.FullName, sVal, true, true));
                }

                localFns[nIndex] = outer with
                {
                    FullName = Text.ReplaceAll(outer.FullName, fn.FullName, sVal, true, true),
                    Args = [.. rewritten],
                };
            }
        }

        return result;
    }

    /// <summary>
    /// The <c>FUNC_VAR</c> arm's variable resolution [:L2239-L2257] - identical in shape to the ordinary
    /// variable arm, but reached by NAME rather than by reference.
    /// </summary>
    /// <param name="variableName">The evaluated first argument, which IS the variable name.</param>
    /// <param name="dwoName">The calculated column's name, for the reported messages.</param>
    /// <param name="row">The row being calculated.</param>
    /// <param name="dwo">The column being calculated.</param>
    /// <param name="context">The context triple.</param>
    /// <param name="allowChannelInvocation">See <see cref="PreprocessExpAsync"/>.</param>
    /// <param name="cancellationToken">Cancels a macro invocation.</param>
    /// <returns>The variable's value expression, or <see cref="AbortSentinel"/>.</returns>
    /// <remarks>
    /// THIS IS C-04'S <c>EXPANSION_MODE_DYNAMIC_INDIRECT</c>. The name arrives already evaluated, so it may
    /// have been produced by an arbitrary DataWindow expression - the legacy specification's own example
    /// selects between two variables by comparing two columns,
    /// <c>of_SetExp("n1","$$(if(n2 &gt; n3 ,'num1','num2'))")</c>
    /// [docs/n_cst_dwsvc_columnexp.md:L92] - which is exactly why the binding cannot be resolved at parse
    /// time and why the mode exists at all.
    /// </remarks>
    private async ValueTask<string> ResolveVariableByNameAsync(
        string variableName,
        string dwoName,
        long row,
        IDataWindowObject? dwo,
        ExpressionContext context,
        bool allowChannelInvocation,
        CancellationToken cancellationToken)
    {
        int nGVarIdx = FindVarIndex(variableName);
        if (nGVarIdx == 0)
        {
            RaiseError(
                ParseErrorFormatter.CreatePlainError(
                    ExpressionErrorSite.PreprocessMacroVariableUndefined,
                    dwoName,
                    variableName));
            return AbortSentinel;
        }

        GlobalVariable variable = _globalVars[nGVarIdx];

        if (variable.VarType == ExpressionVariableEnvironment.VAR_FOREIGN)
        {
            return await CalcForeignVariableValueAsync(
                    variable.Foreign,
                    row,
                    dwo,
                    allowChannelInvocation,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (variable.Local.HasMacro)
        {
            return await PreprocessExpAsync(
                    row,
                    dwo,
                    variable.Local.Exp,
                    new OneBasedList<VariableReference>(
                        static () => new VariableReference(),
                        variable.Local.Vars),
                    new OneBasedList<FunctionReference>(
                        static () => new FunctionReference(),
                        variable.Local.Fns),
                    context,
                    allowChannelInvocation,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (variable.Local.Exp.Length == 0)
        {
            RaiseError(
                ParseErrorFormatter.CreatePlainError(
                    ExpressionErrorSite.PreprocessMacroVariableValueInvalid,
                    dwoName,
                    variableName));
            return AbortSentinel;
        }

        return variable.Local.Exp;
    }

    /// <summary>
    /// Raises <c>OnColumnExpInvokeMethod</c> on the application [:L2263, :L2287] - across the boundary, that
    /// is C-04's <c>InvokeMethodChannel</c>.
    /// </summary>
    /// <param name="plan">The classified dispatch.</param>
    /// <param name="row">The row being calculated.</param>
    /// <param name="dwo">The column being calculated.</param>
    /// <param name="allowChannelInvocation">False on the synchronous host-interface path.</param>
    /// <param name="cancellationToken">Cancels the round trip.</param>
    /// <returns>The rendered value, UNWRAPPED, or null to abort.</returns>
    /// <remarks>
    /// <para>
    /// THE STREAM IS INVERTED AND THAT IS STRUCTURAL, NOT STYLISTIC. The legacy expects the APPLICATION to
    /// implement the macro switch [docs/n_cst_dwsvc_columnexp.md:L120-L128], so across a service boundary
    /// DataServices must call BACK into its client. MacroInvoker owns that inversion, the correlation and the
    /// timeout; this method owns only the oracle's decision about what to do with the answer.
    /// </para>
    /// <para>
    /// NO INVOKER MEANS AN UNHANDLED EVENT, WHICH IS A REAL LEGACY STATE, NOT A GAP. A PowerBuilder event
    /// with no script returns an uninitialised <c>any</c>, whose class matches none of the seven arms at
    /// :L2264-L2284, so the oracle reports "返回值无效" and aborts. An absent channel produces exactly that
    /// outcome by exactly that route, so the engine behaves the same way whether the application declined to
    /// implement the macro or never connected.
    /// </para>
    /// <para>
    /// THE RENDERING IS THE INVOKER'S AND THE SEVEN CLASS ARMS ARE NOT RESTATED HERE. Note among them that a
    /// boolean renders as <c>1=1</c>/<c>1=0</c> [:L2270-L2274] rather than as a word - the opposite of the
    /// boolean <c>of_addvar</c> overload, and an asymmetry both sides preserve.
    /// </para>
    /// </remarks>
    private async ValueTask<string?> InvokeMacroAsync(
        MacroDispatchPlan plan,
        long row,
        IDataWindowObject? dwo,
        bool allowChannelInvocation,
        CancellationToken cancellationToken)
    {
        if (dwo is null)
        {
            LogChannelUnavailable("the calculated column has no DataWindow object handle");
            return null;
        }

        if (_macroInvoker is null || !allowChannelInvocation)
        {
            LogChannelUnavailable(
                _macroInvoker is null
                    ? "no macro invocation channel is configured"
                    : "the synchronous host-interface path cannot await a macro invocation");
            return null;
        }

        MacroInvocationResult result = await _macroInvoker
            .InvokeAsync(
                row,
                dwo,
                plan,
                _session.SessionId,
                _handle.Value,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Aborts)
        {
            if (result.Error is { } error)
            {
                RaiseError(error);
            }

            return null;
        }

        // The UNWRAPPED rendering: the single wrap happens at the shared site above, mirroring :L2310.
        return result.Rendered ?? string.Empty;
    }

    // =================================================================================================
    //  CROSS-DATAWINDOW RESOLUTION - and the IExpressionServiceHost implementation
    // =================================================================================================

    /// <summary>
    /// <c>_of_calcvarexpvalue(readonly long row, readonly dwobject dwo, readonly integer index, ...)</c>
    /// [:L2362-L2397], asynchronous - compute a global variable's VALUE, as an expression literal.
    /// </summary>
    /// <param name="row">The row to compute against.</param>
    /// <param name="dwo">The column context for reported messages.</param>
    /// <param name="index">The one-based variable index.</param>
    /// <param name="context">The context triple to thread into preprocessing.</param>
    /// <param name="allowChannelInvocation">See <see cref="PreprocessExpAsync"/>.</param>
    /// <param name="cancellationToken">Cancels a macro invocation.</param>
    /// <returns>An expression literal, always a quoted string form, or <see cref="AbortSentinel"/>.</returns>
    /// <remarks>
    /// <para>
    /// THE RESULT IS ALWAYS A STRING LITERAL, and the oracle's own comment says so [:L2365 "始终为字符串类型:
    /// 'value'"]. That is because it evaluates <c>dwValueToExp(...)</c> AROUND the preprocessed expression
    /// [:L2390] - a function called from INSIDE an expression string - so whatever the variable's type, the
    /// answer comes back as a literal the caller can splice anywhere.
    /// </para>
    /// <para>
    /// AN ABORTED PREPROCESS IS NOT CHECKED HERE, DELIBERATELY. :L2388 stores the preprocessed text and
    /// :L2390 evaluates <c>dwValueToExp(</c> plus that text plus <c>)</c> without testing for the empty
    /// string first, so an aborted preprocess produces <c>dwValueToExp()</c> - a zero-argument call that the
    /// evaluator answers with its invalid marker, which :L2391 then reports. The abort therefore surfaces as
    /// a REPORTED expression error rather than as a silent refusal, and that is the observable difference an
    /// added guard would erase.
    /// </para>
    /// </remarks>
    private async ValueTask<string> CalcVarExpValueAsync(
        long row,
        IDataWindowObject? dwo,
        int index,
        ExpressionContext context,
        bool allowChannelInvocation,
        CancellationToken cancellationToken)
    {
        if (!_globalVars.IsValidIndex(index))
        {
            // Reached when a context lookup answered 0, which the oracle forwards unchecked [:L2182] and
            // PowerBuilder answers with a runtime fault. Refusing keeps a `@$$undefined` reference from
            // killing a shared service; the abort is the same answer the surrounding arms give.
            LogChannelUnavailable("a cross-DataWindow variable index does not address the variable table");
            return AbortSentinel;
        }

        GlobalVariable variable = _globalVars[index];

        // :L2384-L2386 - a foreign variable delegates again, one hop further out. The chain terminates
        // because a foreign entry's peer is always a different service and a cycle would have been refused
        // when the link was created.
        if (variable.VarType == ExpressionVariableEnvironment.VAR_FOREIGN)
        {
            return await CalcForeignVariableValueAsync(
                    variable.Foreign,
                    row,
                    dwo,
                    allowChannelInvocation,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        string sExp = await PreprocessExpAsync(
                row,
                dwo,
                variable.Local.Exp,
                new OneBasedList<VariableReference>(
                    static () => new VariableReference(),
                    variable.Local.Vars),
                new OneBasedList<FunctionReference>(
                    static () => new FunctionReference(),
                    variable.Local.Fns),
                context,
                allowChannelInvocation,
                cancellationToken)
            .ConfigureAwait(false);

        // :L2390 - dwValueToExp called from inside the expression. The evaluator registers that function in
        // its own constructor, along with the five typed null producers its output can name, so the
        // obligation is already discharged there and is not duplicated here.
        string sVal = EvaluateExpression(ValueToExpression.FunctionName + "(" + sExp + ")", row);

        if (string.Equals(sVal, UndeterminedValueSentinel, StringComparison.Ordinal)
            || string.Equals(sVal, InvalidExpressionSentinel, StringComparison.Ordinal))
        {
            RaiseError(
                ParseErrorFormatter.CreatePlainError(
                    ExpressionErrorSite.VariableExpressionError,
                    variable.Name,
                    sExp));
            return AbortSentinel;
        }

        return sVal;
    }

    /// <summary>
    /// <c>_of_calcvarexpvalue(readonly integer index, readonly long ctx_row, ...)</c> [:L2399],
    /// asynchronous - the four-argument arity, which computes against THIS DataWindow's CURRENT row.
    /// </summary>
    /// <param name="index">The one-based variable index.</param>
    /// <param name="context">The caller's row, column and handle, which become the context.</param>
    /// <param name="allowChannelInvocation">See <see cref="PreprocessExpAsync"/>.</param>
    /// <param name="cancellationToken">Cancels a macro invocation.</param>
    /// <remarks>
    /// THE CURRENT ROW, NOT THE CALLER'S ROW [:L2399]. A peer asking this service for a variable's value gets
    /// it computed against whatever row this DataWindow happens to be sitting on, which is what makes a
    /// cross-DataWindow variable behave like a single scalar rather than like a row-aligned column. The
    /// column context is the DataWindow-level object rather than a column, which is what
    /// <c>#DataWindow.Object.DataWindow</c> resolves to.
    /// </remarks>
    private ValueTask<string> CalcVarExpValueAsync(
        int index,
        ExpressionContext context,
        bool allowChannelInvocation,
        CancellationToken cancellationToken)
    {
        DataWindowServiceHost host = RequireHost();

        IDataWindowObject? dwo;
        try
        {
            dwo = host.GetObjectAttribute(DataWindowLevelObjectName);
        }
        catch (Exception exception) when (IsObjectModelFault(exception))
        {
            // A host with no DataWindow-level object cannot supply the context object the oracle passes.
            // Continuing without it costs only the column name in a reported message, so it is preferable to
            // failing a calculation over a diagnostic detail.
            _logger?.LogDebug(
                exception,
                "The host did not resolve its DataWindow-level object; cross-DataWindow variable messages "
                    + "will carry no column name (n_cst_dwsvc_columnexp.sru:L2399).");
            dwo = null;
        }

        return CalcVarExpValueAsync(
            host.GetRow(),
            dwo,
            index,
            context,
            allowChannelInvocation,
            cancellationToken);
    }

    /// <summary>
    /// Computes a FOREIGN variable's value through the service that owns it - the port of
    /// <c>GlobalVars[...].foreign.expSvc._of_CalcVarExpValue(foreign.index, row, dwo, this)</c> [:L2200,
    /// :L2246, :L2385].
    /// </summary>
    /// <param name="foreign">The peer's handle and the peer's own variable index.</param>
    /// <param name="callerRow">This engine's row, which becomes the peer's context row.</param>
    /// <param name="callerDwo">This engine's column, which becomes the peer's context column.</param>
    /// <param name="allowChannelInvocation">See <see cref="PreprocessExpAsync"/>.</param>
    /// <param name="cancellationToken">Cancels a macro invocation inside the peer.</param>
    /// <remarks>
    /// THE ASYNCHRONOUS ENTRY POINT IS PREFERRED WHENEVER THE PEER IS AN ENGINE, which for the real topology
    /// is always: that is what keeps a macro invocation inside the peer's own preprocessing awaitable
    /// instead of forcing it down the synchronous interface. A non-engine implementation of
    /// <see cref="IExpressionServiceHost"/> - a test double, in practice - is reached through the session's
    /// synchronous helper, which also carries the BLOCKED reporting.
    /// </remarks>
    private async ValueTask<string> CalcForeignVariableValueAsync(
        ForeignVariableReference foreign,
        long callerRow,
        IDataWindowObject? callerDwo,
        bool allowChannelInvocation,
        CancellationToken cancellationToken)
    {
        ForeignVariableResolution resolution = _session.ResolveForeignVariable(foreign);

        if (!resolution.IsResolved)
        {
            if (resolution.Error is { } error)
            {
                RaiseError(error);
            }

            return AbortSentinel;
        }

        ExpressionContext callerContext = new(callerRow, callerDwo, _handle);

        if (resolution.Host is ColumnExpressionEngine peer)
        {
            return await peer
                .CalcVarExpValueAsync(
                    resolution.Index,
                    callerContext,
                    allowChannelInvocation,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        ExpressionValueResult value =
            _session.CalcForeignVariableValue(foreign, callerRow, callerDwo, this);

        if (value.Error is { } valueError)
        {
            RaiseError(valueError);
        }

        return value.Value;
    }

    /// <summary>
    /// Computes a CONTEXT variable's value through the context service [:L2181-L2183].
    /// </summary>
    /// <param name="reference">The <c>@$$name</c> reference.</param>
    /// <param name="context">The context triple.</param>
    /// <param name="callerRow">This engine's row, which becomes the context service's context row.</param>
    /// <param name="callerDwo">This engine's column.</param>
    /// <param name="allowChannelInvocation">See <see cref="PreprocessExpAsync"/>.</param>
    /// <param name="cancellationToken">Cancels a macro invocation.</param>
    /// <remarks>
    /// THE INDEX IS LOOKED UP INSIDE THE CONTEXT SERVICE AND FORWARDED UNCHECKED [:L2182], which is the
    /// oracle's own behaviour: <c>ctx_expSvc._of_FindVarIndex(...)</c> answers 0 for an undefined name and 0
    /// is passed straight on. The receiving end refuses it rather than faulting - see
    /// <see cref="CalcVarExpValueAsync(long, IDataWindowObject, int, ExpressionContext, bool,
    /// CancellationToken)"/>.
    /// </remarks>
    private async ValueTask<string> CalcContextVariableValueAsync(
        VariableReference reference,
        ExpressionContext context,
        long callerRow,
        IDataWindowObject? callerDwo,
        bool allowChannelInvocation,
        CancellationToken cancellationToken)
    {
        ForeignVariableResolution resolution = _session.ResolveContextHost(context);

        if (!resolution.IsResolved)
        {
            if (resolution.Error is { } error)
            {
                RaiseError(error);
            }

            return AbortSentinel;
        }

        if (resolution.Host is ColumnExpressionEngine contextEngine)
        {
            int contextIndex = contextEngine.FindVarIndex(reference.Name);

            return await contextEngine
                .CalcVarExpValueAsync(
                    context.Row,
                    context.Dwo,
                    contextIndex,
                    new ExpressionContext(callerRow, callerDwo, _handle),
                    allowChannelInvocation,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        ExpressionValueResult value =
            _session.CalcContextVariableValue(reference, context, callerRow, callerDwo, this);

        if (value.Error is { } valueError)
        {
            RaiseError(valueError);
        }

        return value.Value;
    }

    /// <summary>
    /// Reads a column out of the CONTEXT DataWindow's row - the port of
    /// <c>ctx_expsvc._of_GetItemExpValue(ctx_row, vars[nVarIdx].name)</c> [:L2185], which is what a bare
    /// <c>@name</c> reference resolves to.
    /// </summary>
    /// <param name="reference">The <c>@name</c> reference; its name is a COLUMN name here, not a variable.</param>
    /// <param name="context">The context triple.</param>
    private string ContextGetItemExpValue(VariableReference reference, ExpressionContext context)
    {
        ForeignVariableResolution resolution = _session.ResolveContextHost(context);

        if (!resolution.IsResolved)
        {
            if (resolution.Error is { } error)
            {
                RaiseError(error);
            }

            return AbortSentinel;
        }

        return resolution.Host!.GetItemExpValue(context.Row, reference.Name);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>_of_calcvarexpvalue</c>'s six-argument arity [:L2362], SYNCHRONOUS - see section 7 of the header
    /// for why this exists alongside the asynchronous core and why this engine never calls it on itself. It
    /// refuses to start a macro invocation it could not await, so a variable whose expression contains a
    /// macro FUNCTION answers <see cref="AbortSentinel"/> on this path only.
    /// </remarks>
    public string CalcVarExpValue(
        long row,
        IDataWindowObject? dwo,
        int index,
        long ctxRow,
        IDataWindowObject? ctxDwo,
        IExpressionServiceHost? ctxExpSvc) =>
        CompleteSynchronously(
            CalcVarExpValueAsync(
                row,
                dwo,
                index,
                ContextFor(ctxRow, ctxDwo, ctxExpSvc),
                allowChannelInvocation: false,
                CancellationToken.None));

    /// <inheritdoc/>
    /// <remarks><c>_of_calcvarexpvalue</c>'s four-argument arity [:L2399], SYNCHRONOUS.</remarks>
    public string CalcVarExpValue(
        int index,
        long ctxRow,
        IDataWindowObject? ctxDwo,
        IExpressionServiceHost? ctxExpSvc) =>
        CompleteSynchronously(
            CalcVarExpValueAsync(
                index,
                ContextFor(ctxRow, ctxDwo, ctxExpSvc),
                allowChannelInvocation: false,
                CancellationToken.None));

    /// <inheritdoc/>
    /// <remarks>
    /// <c>_of_getitemexpvalue(readonly long row, readonly string colname)</c> [:L2326-L2360] - read one cell
    /// and render it as an EXPRESSION LITERAL, which is what makes the result spliceable into an expression.
    /// FULLY SYNCHRONOUS in the oracle too: it evaluates nothing and invokes nothing.
    /// <para>
    /// AN UNKNOWN COLUMN TYPE ANSWERS THE EMPTY STRING [:L2358], which is the abort sentinel - so a bare
    /// <c>@name</c> against a column this service cannot type aborts the whole calculation rather than
    /// substituting nothing.
    /// </para>
    /// </remarks>
    public string GetItemExpValue(long row, string? columnName)
    {
        string column = columnName ?? string.Empty;
        long colType = GetColumnType(column);

        // The six typed arms [:L2344-L2357]. Each renders through the dwvaluetoexp port, whose hard
        // invariant is that it never answers the empty string - which is precisely what keeps a legitimate
        // value from being mistaken for the abort sentinel this method also uses.
        if (colType == DataWindowServiceBase.COL_TYPE_DECIMAL)
        {
            return ValueToExpression.Convert(RequireHost().GetItemDecimal(row, column));
        }

        if (colType == DataWindowServiceBase.COL_TYPE_INTEGER)
        {
            return ValueToExpression.Convert(RequireHost().GetItemNumber(row, column));
        }

        if (colType == DataWindowServiceBase.COL_TYPE_STRING)
        {
            return ValueToExpression.Convert(RequireHost().GetItemString(row, column));
        }

        // The three temporal reads go through the value buffer, because the host contract exposes by-name
        // typed getters for the other three only. Reading `dwo.Primary[row]` is the path the sibling
        // evaluator uses for the same reason, so the two agree on how a raw cell is obtained.
        if (colType == DataWindowServiceBase.COL_TYPE_DATETIME)
        {
            return ValueToExpression.Convert(AsDateTime(ReadRawByName(column, row)));
        }

        if (colType == DataWindowServiceBase.COL_TYPE_DATE)
        {
            return ValueToExpression.Convert(AsDate(ReadRawByName(column, row)));
        }

        if (colType == DataWindowServiceBase.COL_TYPE_TIME)
        {
            return ValueToExpression.Convert(AsTime(ReadRawByName(column, row)));
        }

        return AbortSentinel;
    }

    /// <summary>
    /// Rebuilds the context triple from the synchronous interface's three loose arguments.
    /// </summary>
    /// <param name="ctxRow">The context row.</param>
    /// <param name="ctxDwo">The context column.</param>
    /// <param name="ctxExpSvc">The context service, whose HANDLE is what the port carries.</param>
    /// <remarks>
    /// A context service that is not registered in this session yields an EMPTY handle, and the session then
    /// answers every resolution against it with its BLOCKED code - which is the correct outcome for a peer
    /// outside the session, and the same one AAP section 0.6.2.3 requires everywhere else.
    /// </remarks>
    private ExpressionContext ContextFor(
        long ctxRow,
        IDataWindowObject? ctxDwo,
        IExpressionServiceHost? ctxExpSvc)
    {
        DataWindowHandle handle = ctxExpSvc switch
        {
            ColumnExpressionEngine engine => engine.Handle,
            null => DataWindowHandle.None,
            _ => HandleOf(ctxExpSvc),
        };

        return new ExpressionContext(ctxRow, ctxDwo, handle);
    }

    private DataWindowHandle HandleOf(IExpressionServiceHost host)
    {
        foreach (DataWindowHandle candidate in _session.Handles)
        {
            if (_session.TryGetHost(candidate, out IExpressionServiceHost? registered)
                && ReferenceEquals(registered, host))
            {
                return candidate;
            }
        }

        return DataWindowHandle.None;
    }

    /// <summary>
    /// Completes an asynchronous result that CANNOT have suspended, for the synchronous
    /// <see cref="IExpressionServiceHost"/> members.
    /// </summary>
    /// <param name="pending">The result, started with channel invocation disabled.</param>
    /// <remarks>
    /// THIS IS NOT SYNC-OVER-ASYNC AND IT NEVER BLOCKS. Every await point inside the preprocessing core is a
    /// macro channel invocation, and these callers pass <c>allowChannelInvocation: false</c>, which refuses
    /// the invocation before the channel is touched - so the returned result is always already complete. The
    /// assertion below states that invariant rather than assuming it: if a future edit introduced a new
    /// suspension point, this fails loudly at the seam instead of deadlocking a request thread.
    /// </remarks>
    private static string CompleteSynchronously(ValueTask<string> pending)
    {
        if (!pending.IsCompleted)
        {
            throw new InvalidOperationException(
                "A synchronous IExpressionServiceHost member started work that suspended. Every suspension "
                + "point in the preprocessing core is a macro channel invocation, and this path disables "
                + "them, so reaching here means a new asynchronous dependency was introduced without a "
                + "synchronous refusal path. See section 7 of ColumnExpressionEngine.cs's header.");
        }

        return pending.GetAwaiter().GetResult();
    }


    // =================================================================================================
    //  ERROR REPORTING - the 28 MessageBox sites, and nothing else
    // =================================================================================================
    //
    //  THERE IS NO MessageBox IN THIS FILE AND THERE CANNOT BE ONE (C-D). Every one of the oracle's 28 dialog
    //  sites becomes a structured error carrying the original text, the substitution arguments, the severity
    //  and - for the 11 caret-bearing ones - the expression and the caret position. ParseErrorFormatter owns
    //  the catalogue; this engine owns only the decision to raise.
    //
    //  AND NONE OF THEM IS LOCALIZED (C-B). The dwsvc, rowselect and contextmenu services route their
    //  messages through I18N; these 28 are hardcoded Chinese and do not. That inconsistency is legacy
    //  behaviour, it is reproduced, and the catalogue hard-wires `Localized = false` on every site so a later
    //  edit cannot regress it silently. This file therefore takes NO dependency on the Localization library
    //  at all - which is itself the assertion, because a missing reference cannot be circumvented.

    /// <summary>
    /// The most recently raised error, or <see langword="null"/> if none has been raised since the last
    /// <see cref="ClearErrors"/>.
    /// </summary>
    /// <remarks>
    /// THIS IS THE REPLACEMENT FOR THE DIALOG'S ONLY OBSERVABLE EFFECT. A PowerBuilder <c>MessageBox</c> is
    /// synchronous and terminal: the user sees it and the function returns false. Across a service boundary
    /// the caller needs the same fact in a form it can transmit, which is what C-04's error detail carries.
    /// </remarks>
    public ExpressionParseError? LastError { get; private set; }

    /// <summary>
    /// Every error raised since the last <see cref="ClearErrors"/>, in the order they were raised, capped at
    /// <see cref="MaxRetainedErrors"/>.
    /// </summary>
    /// <remarks>
    /// THE CAP IS DELIBERATE AND IS NOT A BEHAVIOUR CHANGE. The oracle retains nothing - each dialog is shown
    /// and forgotten - so retaining a bounded history adds information the legacy did not have without
    /// removing any it did. An unbounded list, by contrast, would let a caller that ignores failures grow the
    /// service's memory without limit, which is a denial-of-service the in-process legacy could not have had
    /// because a dialog blocked the thread that produced it.
    /// </remarks>
    public ImmutableArray<ExpressionParseError> Errors
    {
        get
        {
            lock (_errorLock)
            {
                return [.. _errors];
            }
        }
    }

    /// <summary>
    /// The number of errors raised since the last <see cref="ClearErrors"/>, INCLUDING any beyond
    /// <see cref="MaxRetainedErrors"/> that were not retained.
    /// </summary>
    public long RaisedErrorCount => Interlocked.Read(ref _raisedErrorCount);

    /// <summary>
    /// Discards <see cref="LastError"/> and <see cref="Errors"/>. Callers that map one request onto one
    /// calculation use this to scope the errors they report to that request.
    /// </summary>
    public void ClearErrors()
    {
        lock (_errorLock)
        {
            _errors.Clear();
        }

        LastError = null;
        Interlocked.Exchange(ref _raisedErrorCount, 0);
    }

    /// <summary>
    /// Raises one structured error - the replacement for one <c>MessageBox</c> call.
    /// </summary>
    /// <param name="error">The error, already formatted by <see cref="ParseErrorFormatter"/>.</param>
    /// <remarks>
    /// THE LOG RECORD IS AT WARNING AND CARRIES THE SITE'S LEGACY LINE. That is the monitoring hook the
    /// oracle had no equivalent for, and the line number is included because it is the only stable identifier
    /// shared between a production log record, the error catalogue and the legacy source - which is the same
    /// reason the site enumeration is valued by line number rather than declared in sequence.
    /// </remarks>
    private void RaiseError(ExpressionParseError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        LastError = error;
        Interlocked.Increment(ref _raisedErrorCount);

        lock (_errorLock)
        {
            if (_errors.Count >= MaxRetainedErrors)
            {
                // Drop the OLDEST, so the most recent failures - the ones a caller is diagnosing - survive.
                _errors.RemoveAt(0);
            }

            _errors.Add(error);
        }

        _logger?.LogWarning(
            "Column expression error at legacy site {LegacySite} (n_cst_dwsvc_columnexp.sru:L{LegacyLine}): "
                + "{ErrorText}",
            error.Site,
            error.LegacyLine,
            error.Text);
    }

    /// <summary>
    /// Records that a capability the oracle had in-process is unreachable here, and that the calculation is
    /// therefore aborting.
    /// </summary>
    /// <param name="reason">What was unavailable, phrased to complete "because ...".</param>
    /// <remarks>
    /// THIS IS NOT AN ERROR SITE AND MUST NOT BECOME ONE. Every condition routed here has NO legacy
    /// counterpart - the oracle could not fail to reach its own event handler, its own object model or its own
    /// variable table - so there is no catalogued message to raise and inventing one would put text into a
    /// caller's error report that no legacy behaviour ever produced. The abort sentinel is the observable
    /// answer; the log record is how an operator finds out why.
    /// </remarks>
    private void LogChannelUnavailable(string reason)
    {
        _logger?.LogWarning(
            "A column expression calculation aborted because {Reason}. The empty-string abort sentinel is "
                + "propagating to the caller (n_cst_dwsvc_columnexp.sru:L2148-L2324).",
            reason);
    }

    // =================================================================================================
    //  HOST ACCESS - Describe, Modify, and the compute object
    // =================================================================================================

    /// <summary>
    /// The expression evaluator, guaranteed non-null.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="OnInit(DataWindowServiceHost)"/> has not run and no evaluator was supplied to the
    /// constructor.
    /// </exception>
    /// <remarks>
    /// FAIL-FAST, NOT A NULL-COALESCING FALLBACK (AAP 0.1.4). A calculation with no evaluator cannot produce
    /// a value, and substituting one would put a fabricated number into a column. The oracle's equivalent is
    /// structural - <c>_of_Evaluate</c> is a member of the service base, so it cannot exist before the host
    /// does - and the assertion states that same precondition explicitly.
    /// </remarks>
    /// <summary>
    /// <c>_of_Evaluate(exp, row)</c> [n_cst_dwsvc.sru:L217] - evaluate one expression against one row.
    /// </summary>
    /// <param name="exp">The expression text. A BARE expression, never a delimited payload.</param>
    /// <param name="row">The row to evaluate against.</param>
    /// <returns>The value as text, or one of the two evaluator failure markers.</returns>
    /// <remarks>
    /// <para>
    /// WHY THIS WRAPPER EXISTS, AND WHY IT IS NOT A CONVENIENCE. The oracle's <c>_of_Evaluate</c> is
    /// <c>Describe("Evaluate(~"" + exp + "~"," + row + ")")</c>, which splices the expression inside
    /// DOUBLE quotes. That splice makes the expression unambiguously BARE: whatever quoting it contains
    /// is its own, not a delimiter.
    /// </para>
    /// <para>
    /// The evaluator, by contrast, serves seven call sites across two files and normalises its payload
    /// first, because six of those sites arrive from <c>n_cst_dwsvc.sru</c> and
    /// <c>n_cst_dwsvc_contextmenu.sru</c> with the expression wrapped in quotes that ARE a delimiter. Its
    /// own remarks record the one shape those two readings cannot be told apart on - a payload that is
    /// EXACTLY one complete single-quoted literal, such as <c>'num1'</c> - and resolve it in favour of the
    /// delimiter, "because that is the shape all six legacy call sites produce". This engine is the
    /// SEVENTH site, so that justification does not reach it, and the same remarks prescribe the remedy
    /// for a caller in this position: splice the literal into a larger expression, exactly as :L2213 and
    /// :L2310 do with <c>sVal = "(" + sVal + ")"</c>.
    /// </para>
    /// <para>
    /// THE SHAPE THIS AFFECTS IS THE SPECIFICATION'S HEADLINE FORM, WHICH IS WHY IT MATTERS.
    /// <c>$$('变量名字符串')</c> [docs/n_cst_dwsvc_columnexp.md section 4] evaluates its argument
    /// <c>'变量名字符串'</c> at :L2230, raw and unwrapped. Under the delimiter reading the quotes vanish
    /// and what is left is a bare identifier that names no column, so the argument answers the invalid
    /// marker and the whole calculation aborts - where the oracle answers the variable's name. Wrapping
    /// only that one ambiguous shape restores the oracle's answer and leaves the other six sites, and
    /// every other shape, exactly as they were.
    /// </para>
    /// <para>
    /// PARENTHESISING NEVER CHANGES A VALUE, which is what makes this safe rather than a compensating
    /// hack: a parenthesised DataWindow expression evaluates identically to the same expression bare. And
    /// the wrap is invisible to callers - every reported message carries the RAW expression text, because
    /// the wrap is applied here and nowhere else.
    /// </para>
    /// </remarks>
    private string EvaluateExpression(string? exp, long row)
    {
        string trimmed = (exp ?? string.Empty).Trim();

        if (IsAmbiguouslyDelimitedLiteral(trimmed))
        {
            trimmed = "(" + trimmed + ")";
        }

        // THE BOUND TABLE TRAVELS WITH EVERY EVALUATION. Passing it unconditionally rather than only when the
        // text looks like it needs it is what keeps a placeholder from ever reaching the lexer unresolved -
        // and an unresolved placeholder is a lexical error there rather than a silent empty value, so the
        // failure would be loud but the expression would still be wrong.
        return RequireEvaluator().Evaluate(trimmed, row, _boundValues);
    }

    /// <summary>
    /// Is this payload the one shape a delimiter reading and a bare reading cannot be told apart on -
    /// exactly one complete single-quoted literal?
    /// </summary>
    /// <param name="trimmed">The trimmed expression text.</param>
    /// <remarks>
    /// THE TEST DELEGATES TO THE EVALUATOR'S OWN NORMALISER rather than re-scanning the literal here, so
    /// there is exactly one implementation of where a literal closes. <c>'a' + 'b'</c> also begins and
    /// ends with a quote but is NOT this shape: its first literal closes early, so the normaliser returns
    /// it unchanged and the comparison below fails. That distinction is the whole reason the check cannot
    /// be a first-and-last-character comparison.
    /// </remarks>
    private static bool IsAmbiguouslyDelimitedLiteral(string trimmed)
    {
        if (trimmed.Length < 2 || trimmed[0] != '\'' || trimmed[^1] != '\'')
        {
            return false;
        }

        string normalised = DataWindowExpressionEvaluator.NormaliseExpressionPayload(trimmed);

        return string.Equals(normalised, trimmed[1..^1].Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Is this exception a DataWindow object-model fault that the oracle's <c>catch(throwable)</c> would
    /// have swallowed?
    /// </summary>
    /// <param name="exception">The exception raised while probing or reading the object model.</param>
    /// <remarks>
    /// <para>
    /// THE ORACLE CATCHES EVERYTHING AND THAT IS DELIBERATE ON ITS PART. <c>catch(throwable ex)</c>
    /// [:L703, :L738] takes PowerBuilder's ROOT type, because there is no validity API for a DataWindow
    /// object handle: the only way to learn that a handle went stale is to touch it and be told. The host
    /// contract preserves that shape - <c>GetObjectAttribute</c> is documented to THROW for an unknown
    /// name and explicitly must not answer null - but it deliberately does not fix WHICH exception, so a
    /// port that caught a chosen type would crash on a conforming host that chose another.
    /// </para>
    /// <para>
    /// TWO CLASSES ARE EXCLUDED, AND BOTH FOR THE SAME REASON: they are not object-model faults and
    /// swallowing them would hide a real failure. Cancellation must reach the caller, or a cancelled
    /// request would silently complete with a fabricated value; and the two conditions that indicate the
    /// process itself is unsound - out of memory and a stack overflow, which cannot be caught at all -
    /// must never be reinterpreted as a stale handle. That is the fail-fast posture AAP section 0.1.4
    /// requires be preserved as fail-fast rather than softened.
    /// </para>
    /// </remarks>
    private static bool IsObjectModelFault(Exception exception) =>
        exception is not OperationCanceledException and not OutOfMemoryException;

    private DataWindowExpressionEvaluator RequireEvaluator() =>
        _evaluator
        ?? throw new InvalidOperationException(
            "The column expression engine has no evaluator. Either supply one to the constructor or call "
            + "OnInit(host) first; the oracle's _of_Evaluate is a member of the service base and therefore "
            + "cannot exist before the host does (n_cst_dwsvc.sru:L217).");

    /// <summary>
    /// <c>_of_hasdwobject</c> [n_cst_dwsvc.sru:L483] - does the DataWindow's object model contain an object
    /// with this name?
    /// </summary>
    /// <param name="dwoName">The object name to probe for.</param>
    /// <remarks>
    /// THE TEST IS THE ORACLE'S, VERBATIM: <c>Lower(Describe(name + ".Name")) = Lower(name)</c>. A missing
    /// object makes <c>Describe</c> answer its own failure marker rather than throwing, and folding both
    /// sides is what makes the comparison answer false for that marker as well as for a different object -
    /// so the case-insensitive comparison is load-bearing and not a convenience.
    /// </remarks>
    private bool HasDataWindowObject(string dwoName)
    {
        if (string.IsNullOrEmpty(dwoName))
        {
            return false;
        }

        string described;
        try
        {
            described = RequireHost().Describe(dwoName + ".Name");
        }
        catch (Exception exception) when (IsObjectModelFault(exception))
        {
            // A host that raises rather than answering the failure marker is the same condition: no such
            // object. The oracle cannot reach this arm because Describe never throws for it.
            _logger?.LogDebug(
                exception,
                "Describe(\"{Property}\") faulted while probing for a DataWindow object; treated as absent "
                    + "(n_cst_dwsvc.sru:L483).",
                dwoName + ".Name");
            return false;
        }

        return string.Equals(described, dwoName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>#DataWindow.Modify(syntax)</c> - applies a syntax fragment through the injected mutator, or
    /// through the host when it happens to be one.
    /// </summary>
    /// <param name="syntax">The syntax fragment.</param>
    /// <returns>The empty string on SUCCESS, or the DataWindow's error text.</returns>
    /// <remarks>
    /// <para>
    /// THE RETURN CONVENTION IS INVERTED RELATIVE TO THE REST OF THIS FILE and that is the oracle's, not a
    /// slip: <c>Modify</c> answers an error STRING, so empty means it worked [:L1949]. The same idiom appears
    /// in the estate's update path [n_cst_thread_task_sqlupdate.sru:L145].
    /// </para>
    /// <para>
    /// WHY A LOCAL SEAM RATHER THAN A HOST MEMBER. <c>DataWindowServiceHost</c> exposes no <c>Modify</c>,
    /// because syntax mutation belongs to the presentational half of the DataWindow that Phase 1 does not
    /// ship (C-D). The compute-object cache is the one place headless logic needs it, so the capability is
    /// declared here as a narrow optional interface: a host that implements it is used, and a host that does
    /// not simply cannot cache - which degrades to the uncached path rather than to a failure, exactly as an
    /// object model without the compute object already does.
    /// </para>
    /// </remarks>
    private string ModifySyntax(string syntax)
    {
        if (_syntaxMutator is { } mutator)
        {
            return mutator.Modify(syntax);
        }

        if (RequireHost() is IDataWindowSyntaxMutator hostMutator)
        {
            return hostMutator.Modify(syntax);
        }

        _logger?.LogDebug(
            "No DataWindow syntax mutator is available, so the expression cache cannot be created or "
                + "destroyed; calculation falls back to the uncached path "
                + "(n_cst_dwsvc_columnexp.sru:L1943-L1951).");

        return SyntaxMutationUnavailable;
    }

    /// <summary>
    /// <c>_of_createcompute(readonly string name, readonly string exp, ref string errinfo)</c>
    /// [:L1921-L1954] - create the computed field that caches one expression's value.
    /// </summary>
    /// <param name="name">The compute object's name - <c>columnexpdata.computename</c>.</param>
    /// <param name="exp">The expression, spliced into the syntax as-is.</param>
    /// <param name="errInfo">
    /// On failure, the SYNTAX FOLLOWED BY the error text [:L1950-L1952] - the oracle prepends the whole
    /// fragment so the reported message carries what was attempted, not only why it was refused.
    /// </param>
    /// <returns>True when the object was created.</returns>
    /// <remarks>
    /// <para>
    /// THE SYNTAX IS BYTE-FOR-BYTE THE ORACLE'S [:L1943-L1948], INCLUDING THE PROPERTIES THAT LOOK POINTLESS.
    /// Height and width are zero and <c>visible</c> is nonetheless "1"; <c>enabled</c> is "0"; the font is
    /// Arial at height -10 with weight 400. None of it affects the computed VALUE, which is the only thing
    /// this file reads - but the fragment is observable through <c>Describe</c> and through any DataWindow
    /// syntax export, so changing it would change output a characterization recording captures. It is
    /// reproduced verbatim for that reason and not because the values matter individually.
    /// </para>
    /// <para>
    /// THE EXPRESSION IS SPLICED WITHOUT ESCAPING, WHICH IS THE ORACLE'S BEHAVIOUR (C-B). An expression
    /// containing a double quote terminates the <c>expression="..."</c> attribute early and the whole
    /// fragment is then refused by the DataWindow - which is precisely why this method reports the syntax
    /// alongside the error. Escaping it would change which expressions can be cached, so it is left alone and
    /// the failure is reported instead.
    /// </para>
    /// <para>
    /// THE TRAILING NEWLINE IS PART OF THE FRAGMENT [:L1948 ends with <c>~n</c>]. It is preserved.
    /// </para>
    /// </remarks>
    private bool CreateCompute(string name, string exp, out string errInfo)
    {
        // :L1943-L1948 - one fragment, assembled in the oracle's own order.
        string sSyntax =
            "create compute(band=detail alignment=\"1\" expression=\"" + exp + "\" border=\"0\" "
            + "color=\"0\" x=\"0\" y=\"0\" height=\"0\" width=\"0\" "
            + "html.valueishtml=\"0\" name=" + name + " visible=\"1\" enabled=\"0\" font.face=\"Arial\" "
            + "font.height=\"-10\" font.weight=\"400\" font.family=\"0\" font.pitch=\"2\" "
            + "font.charset=\"0\" background.mode=\"1\" background.color=\"0\")\n";

        // :L1950 - the mutator answers the error text, empty meaning success.
        errInfo = ModifySyntax(sSyntax);

        // :L1951-L1953 - prepend the syntax when it failed.
        if (errInfo.Length != 0)
        {
            errInfo = sSyntax + errInfo;
        }

        // :L1955 - success IS the empty error text.
        return errInfo.Length == 0;
    }

    /// <summary>
    /// <c>Long(#DataWindow.Describe(property))</c> - read a numeric DataWindow property, coercing the two
    /// failure markers to zero the way PowerScript's <c>Long</c> does.
    /// </summary>
    /// <param name="host">The host to describe against.</param>
    /// <param name="property">The property expression, for example <c>"salary.ID"</c>.</param>
    /// <returns>The value, or 0 when the property is absent, undetermined or not numeric.</returns>
    /// <remarks>
    /// ZERO IS THE ORACLE'S "NO SUCH COLUMN" ANSWER AND THE CALLERS TEST FOR IT [:L1285, :L2102 and the
    /// key-column resolution at :L118-L122 of the estate's update path]. <c>Describe</c> answers "!" for an
    /// invalid property and "?" for an undetermined one, and <c>Long</c> answers 0 for both because neither
    /// is a valid PowerScript number - so the coercion is what makes a missing column indistinguishable from
    /// a column whose identifier is zero, which no column has.
    /// </remarks>
    private static long DescribeLong(DataWindowServiceHost host, string property)
    {
        string described;
        try
        {
            described = host.Describe(property);
        }
        catch (Exception exception) when (IsObjectModelFault(exception))
        {
            // A throwing host is the same condition as the failure marker: no usable value.
            _ = exception;
            return 0;
        }

        return PbLong(described) ?? 0;
    }

    // =================================================================================================
    //  THE CACHED READ - _of_calcitem's six typed arms over the COMPUTE object [:L720-L737]
    // =================================================================================================

    /// <summary>
    /// Reads the value the DataWindow engine already computed into the cache object [:L720-L737].
    /// </summary>
    /// <param name="data">The bound expression, whose <c>ComputeName</c> is read and whose
    /// <c>ColType</c> selects the arm.</param>
    /// <param name="row">The row to read.</param>
    /// <param name="sVal">The value, rendered as text.</param>
    /// <returns>
    /// False for an unknown column type - the oracle's <c>case else return false</c> [:L735-L736].
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE ARM IS SELECTED BY THE BOUND COLUMN'S TYPE, NOT THE COMPUTE'S. The compute object's own type is
    /// whatever the DataWindow inferred from the expression, and reading it with the wrong typed getter is
    /// how a numeric expression on a string column still lands as text. Preserved exactly.
    /// </para>
    /// <para>
    /// FIVE OF THE SIX GO THROUGH <c>String()</c> AND THE WRITE ARMS PARSE THEM BACK. That round trip is
    /// observable twice - the text is the <c>data</c> argument of the <c>ItemChanged</c> veto [:L779] - so
    /// the rendering must be the inverse of the parse, which is why both halves live in this file as a
    /// matched pair rather than borrowing an unrelated formatter.
    /// </para>
    /// <para>
    /// THE THREE TEMPORAL ARMS READ THE VALUE BUFFER because the host contract publishes by-name typed
    /// getters for decimal, number and string only. The sibling evaluator resolves the same gap the same way,
    /// so the two agree on how a raw cell is obtained and a fixture that satisfies one satisfies the other.
    /// </para>
    /// </remarks>
    private bool TryReadComputedValue(ColumnExpressionData data, long row, out string? sVal)
    {
        DataWindowServiceHost host = RequireHost();
        string compute = data.ComputeName;

        if (data.ColType == DataWindowServiceBase.COL_TYPE_DECIMAL)
        {
            // :L722 - String(GetItemDecimal(row, computeName))
            sVal = PbString(host.GetItemDecimal(row, compute));
            return true;
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_INTEGER)
        {
            // :L724 - String(GetItemNumber(row, computeName))
            sVal = PbString(host.GetItemNumber(row, compute));
            return true;
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_STRING)
        {
            // :L726 - NO String() wrapper: the value is already text, and a null stays null.
            sVal = host.GetItemString(row, compute);
            return true;
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_DATETIME)
        {
            // :L728 - String(GetItemDateTime(row, computeName))
            sVal = PbString(AsDateTime(ReadRawByName(compute, row)));
            return true;
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_DATE)
        {
            // :L730 - String(GetItemDate(row, computeName))
            sVal = PbString(AsDate(ReadRawByName(compute, row)));
            return true;
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_TIME)
        {
            // :L732 - String(GetItemTime(row, computeName))
            sVal = PbString(AsTime(ReadRawByName(compute, row)));
            return true;
        }

        // :L735-L736 - `case else return false`.
        sVal = null;
        return false;
    }

    /// <summary>
    /// Reads the BOUND column's current value, for the equality tests the write arms perform before writing
    /// [:L772, :L786, :L801, :L815, :L829, :L843].
    /// </summary>
    /// <param name="row">The row to read.</param>
    /// <param name="data">The bound expression; its <c>ColType</c> selects the read and its <c>Name</c>
    /// identifies the column.</param>
    /// <remarks>
    /// THE ORACLE READS BY IDENTIFIER AND THIS READS BY NAME. They address the same column - the identifier
    /// was resolved FROM the name at bind time [:L1285] - and the host contract's by-name typed getters are
    /// what is published, so the name is the available spelling of the same access. Where no typed getter
    /// exists the value buffer is read instead, as in <see cref="TryReadComputedValue"/>.
    /// </remarks>
    private object? ReadRawItem(long row, ColumnExpressionData data)
    {
        DataWindowServiceHost host = RequireHost();
        string column = data.Name;

        if (data.ColType == DataWindowServiceBase.COL_TYPE_DECIMAL)
        {
            return host.GetItemDecimal(row, column);
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_INTEGER)
        {
            return host.GetItemNumber(row, column);
        }

        if (data.ColType == DataWindowServiceBase.COL_TYPE_STRING)
        {
            return host.GetItemString(row, column);
        }

        return ReadRawByName(column, row);
    }

    /// <summary>
    /// Reads one cell straight out of the primary value buffer.
    /// </summary>
    /// <param name="column">The column or compute object name.</param>
    /// <param name="row">The one-based row.</param>
    /// <returns>The raw value, or <see langword="null"/> when the object or the row is unreachable.</returns>
    /// <remarks>
    /// A NULL ANSWER MEANS "NULL OR UNREADABLE", AND THE CALLERS CANNOT TELL THE DIFFERENCE - which is
    /// correct for both, because every caller's next act is a null test that treats an unreadable cell the
    /// same way the oracle treats a null one. The oracle cannot reach the unreadable case: its typed getters
    /// answer null for a bad row rather than raising.
    /// </remarks>
    private object? ReadRawByName(string column, long row)
    {
        if (string.IsNullOrEmpty(column))
        {
            return null;
        }

        try
        {
            return RequireHost().GetObjectAttribute(column).Primary[row];
        }
        catch (Exception exception) when (IsObjectModelFault(exception))
        {
            _logger?.LogDebug(
                exception,
                "The value buffer for {Column} could not be read at row {Row}; treated as null "
                    + "(n_cst_dwsvc_columnexp.sru:L720-L737).",
                column,
                row);
            return null;
        }
    }

    // =================================================================================================
    //  POWERSCRIPT VALUE CONVERSION - the String()/Dec()/Long()/DateTime()/Date()/Time() pair
    // =================================================================================================
    //
    //  THESE ARE A MATCHED PAIR AND MUST STAY ONE. The cached read renders with the String() half and the
    //  write arm parses with the other, so any asymmetry between them is a value that changes merely by
    //  passing through the cache - a divergence between the cached and uncached paths that no test of either
    //  path alone would catch.
    //
    //  CULTURE IS INVARIANT EVERYWHERE, DELIBERATELY, AND THIS IS A DOCUMENTED DEVIATION (C-K). PowerBuilder's
    //  String() and Date() honour the desktop's regional settings, so the legacy's rendering varied with the
    //  machine it ran on. A service cannot: the same request must produce the same expression text in every
    //  container, and the characterization model's one hard prerequisite is repeatability with
    //  non-deterministic values masked from BOTH sides (AAP 0.6.7). Locale-varying text is exactly such a
    //  value, and pinning it to the invariant culture is what makes the paired recordings comparable at all.
    //  The legacy's own deployment was single-locale - pfw.sra hardcodes its language [pfw.sra:L94] - so
    //  nothing observable is lost.
    //
    //  THE FAILURE ANSWERS ARE PowerScript's, NOT .NET's. Dec and Long answer 0 for text that is not a
    //  number; Date, Time and DateTime answer their epoch sentinels. None of them throws, and none of them
    //  answers null unless the ARGUMENT was null - which is why every one of these helpers takes a nullable
    //  and short-circuits on null before doing anything else.

    /// <summary>
    /// <c>DateTime("")</c> - PowerScript's invalid-datetime answer, and the value <c>of_calcempty</c> compares
    /// against [:L1997].
    /// </summary>
    public static DateTime InvalidDateTimeSentinel { get; } =
        new(1900, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary><c>Date("")</c> - PowerScript's invalid-date answer [:L2003].</summary>
    public static DateOnly InvalidDateSentinel { get; } = new(1900, 1, 1);

    /// <summary><c>Time("")</c> - PowerScript's invalid-time answer [:L2009].</summary>
    public static TimeOnly InvalidTimeSentinel { get; } = new(0, 0, 0);

    /// <summary><c>String(decimal)</c>.</summary>
    /// <param name="value">The value; null renders as the empty string, which the caller then nulls again.</param>
    private static string PbString(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary><c>String(number)</c> - the general form, which drops a trailing ".0" as PowerScript does.</summary>
    /// <param name="value">The value.</param>
    private static string PbString(double? value) =>
        value?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary><c>String(datetime)</c>, at full microsecond precision.</summary>
    /// <param name="value">The value.</param>
    /// <remarks>
    /// FULL PRECISION IS RETAINED BECAUSE THE ROUND TRIP DEMANDS IT. Whether PowerBuilder's own default
    /// rendering keeps the fractional second cannot be determined from this repository, and the choice is
    /// observable only through this pair - so it is resolved in favour of preserving information, which keeps
    /// the cached and uncached paths identical. Discarding it would make a cached datetime differ from an
    /// uncached one in the sub-second digits alone.
    /// </remarks>
    private static string PbString(DateTime? value) =>
        value?.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary><c>String(date)</c>.</summary>
    /// <param name="value">The value.</param>
    private static string PbString(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary><c>String(time)</c>, at full microsecond precision - see <see cref="PbString(DateTime?)"/>.</summary>
    /// <param name="value">The value.</param>
    private static string PbString(TimeOnly? value) =>
        value?.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary><c>Dec(string)</c> [:L770] - null in, null out; unparseable in, ZERO out.</summary>
    /// <param name="text">The text.</param>
    private static decimal? PbDec(string? text)
    {
        if (text is null)
        {
            return null;
        }

        return decimal.TryParse(
            text.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out decimal parsed)
            ? parsed
            : 0m;
    }

    /// <summary>
    /// <c>Long(string)</c> [:L784] - null in, null out; unparseable in, ZERO out.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <remarks>
    /// A FRACTIONAL VALUE TRUNCATES TOWARDS ZERO, WHICH IS PowerScript's ROUNDING AND NOT .NET's. The .NET
    /// conversion from decimal to long rounds to nearest; PowerScript's <c>Long</c> of a non-integral number
    /// discards the fraction. The write arm feeds this the rendering of a number column, so a computed
    /// average landing on an integer column takes this path routinely - it is not an edge case.
    /// </remarks>
    private static long? PbLong(string? text)
    {
        if (text is null)
        {
            return null;
        }

        string trimmed = text.Trim();

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole))
        {
            return whole;
        }

        if (decimal.TryParse(
                trimmed,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out decimal fractional)
            && fractional >= long.MinValue
            && fractional <= long.MaxValue)
        {
            return (long)decimal.Truncate(fractional);
        }

        return 0L;
    }

    /// <summary>
    /// <c>DateTime(string)</c> [:L813] - null in, null out; unparseable in,
    /// <see cref="InvalidDateTimeSentinel"/> out.
    /// </summary>
    /// <param name="text">The text.</param>
    private static DateTime? PbDateTime(string? text)
    {
        if (text is null)
        {
            return null;
        }

        return DateTime.TryParse(
            text.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime parsed)
            ? parsed
            : InvalidDateTimeSentinel;
    }

    /// <summary>
    /// <c>Date(string)</c> [:L827] - null in, null out; unparseable in, <see cref="InvalidDateSentinel"/> out.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <remarks>
    /// A FULL DATETIME PARSES AND ITS TIME IS DISCARDED, which is what PowerScript's <c>Date</c> of a
    /// datetime-shaped string does. The date form is tried first so a bare date never pays for the wider
    /// parse.
    /// </remarks>
    private static DateOnly? PbDate(string? text)
    {
        if (text is null)
        {
            return null;
        }

        string trimmed = text.Trim();

        if (DateOnly.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly parsed))
        {
            return parsed;
        }

        if (DateTime.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime whole))
        {
            return DateOnly.FromDateTime(whole);
        }

        return InvalidDateSentinel;
    }

    /// <summary>
    /// <c>Time(string)</c> [:L841] - null in, null out; unparseable in, <see cref="InvalidTimeSentinel"/> out.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <remarks>
    /// A FULL DATETIME PARSES AND ITS DATE IS DISCARDED, mirroring <see cref="PbDate"/>.
    /// </remarks>
    private static TimeOnly? PbTime(string? text)
    {
        if (text is null)
        {
            return null;
        }

        string trimmed = text.Trim();

        if (TimeOnly.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly parsed))
        {
            return parsed;
        }

        if (DateTime.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime whole))
        {
            return TimeOnly.FromDateTime(whole);
        }

        return InvalidTimeSentinel;
    }

    // =================================================================================================
    //  RAW BUFFER COERCION - typing an `any` the way PowerBuilder's typed getters do
    // =================================================================================================
    //
    //  The value buffer answers `object?`, which is the `any` the oracle never had to handle because it
    //  called a TYPED getter. These six put the type back on, and each accepts every representation its
    //  own caller can actually hand it - a DataWindow cell holding a date may arrive as DateOnly from one
    //  provider and as DateTime from another, and neither is wrong.
    //
    //  THE BREADTH IS NOT UNIFORM ACROSS THE SIX, BECAUSE THE TWO READ ROUTES ARE NOT. `ReadRawItem` reads
    //  a decimal, integer or string column through the HOST'S OWN TYPED GETTER, exactly as the oracle does
    //  [:L771, :L785, :L800], so those three see only what `decimal?`, `double?` and `string?` can carry.
    //  Only the temporal arms fall through to `ReadRawByName`, which answers a genuinely untyped buffer
    //  cell - so only those three need to span representations. Giving the first three a breadth their
    //  input cannot reach would be unreachable code masquerading as robustness, and it would also hide the
    //  coupling: if `ReadRawItem` ever stopped typing a column through the host, the narrow form here is
    //  where that change has to be answered.
    //
    //  A REPRESENTATION THAT CANNOT BE THE COLUMN'S TYPE ANSWERS NULL, NOT AN EXCEPTION. That is what the
    //  typed getters do: GetItemDecimal on a cell holding something unconvertible answers null rather than
    //  raising, and the callers' next act is a null test either way.

    /// <summary>Types the answer of <c>GetItemDecimal</c> as <c>decimal</c>.</summary>
    /// <param name="raw">The cell as <see cref="ReadRawItem"/> answered it - null or a boxed decimal.</param>
    private static decimal? AsDecimal(object? raw) => raw as decimal?;

    /// <summary>
    /// Types the answer of <c>GetItemNumber</c> as <c>long</c>, TRUNCATING towards zero.
    /// </summary>
    /// <param name="raw">The cell as <see cref="ReadRawItem"/> answered it - null or a boxed double.</param>
    /// <remarks>
    /// THE TRUNCATION IS THE POINT AND IT IS NOT INCIDENTAL. The host's numeric getter answers a floating
    /// value while an integer column holds an integral one, and the write arm compares against
    /// <c>Long(sVal)</c> [:L783] - PowerScript's <c>Long</c>, which truncates rather than rounding. Reading
    /// the old value with .NET's round-to-nearest would report a change at exactly one half where the
    /// oracle reports none.
    /// </remarks>
    private static long? AsLong(object? raw) => raw is double value ? ToLongOrNull(value) : null;

    /// <summary>Types the answer of <c>GetItemString</c> as <c>string</c>.</summary>
    /// <param name="raw">The cell as <see cref="ReadRawItem"/> answered it - null or a string.</param>
    private static string? AsString(object? raw) => raw as string;

    /// <summary>Types a raw cell as <c>datetime</c>, as <c>GetItemDateTime</c> does.</summary>
    /// <param name="raw">The raw cell.</param>
    private static DateTime? AsDateTime(object? raw) => raw switch
    {
        null => null,
        DateTime value => value,
        DateOnly value => value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
        DateTimeOffset value => value.DateTime,
        string value => PbDateTime(value),
        _ => null,
    };

    /// <summary>Types a raw cell as <c>date</c>, as <c>GetItemDate</c> does.</summary>
    /// <param name="raw">The raw cell.</param>
    private static DateOnly? AsDate(object? raw) => raw switch
    {
        null => null,
        DateOnly value => value,
        DateTime value => DateOnly.FromDateTime(value),
        DateTimeOffset value => DateOnly.FromDateTime(value.DateTime),
        string value => PbDate(value),
        _ => null,
    };

    /// <summary>Types a raw cell as <c>time</c>, as <c>GetItemTime</c> does.</summary>
    /// <param name="raw">The raw cell.</param>
    /// <remarks>
    /// A <see cref="TimeSpan"/> OUTSIDE ONE DAY ANSWERS NULL rather than wrapping. PowerScript's <c>time</c>
    /// is a time of day and has no representation for "26 hours", so wrapping would fabricate a value the
    /// legacy could not hold.
    /// </remarks>
    private static TimeOnly? AsTime(object? raw) => raw switch
    {
        null => null,
        TimeOnly value => value,
        DateTime value => TimeOnly.FromDateTime(value),
        DateTimeOffset value => TimeOnly.FromDateTime(value.DateTime),
        TimeSpan value =>
            value >= TimeSpan.Zero && value < TimeSpan.FromDays(1) ? TimeOnly.FromTimeSpan(value) : null,
        string value => PbTime(value),
        _ => null,
    };

    /// <summary>Truncates a floating value to <c>long</c> the way PowerScript's <c>Long</c> does.</summary>
    /// <param name="value">The value.</param>
    /// <remarks>
    /// AN UNREPRESENTABLE VALUE IS A NULL, NOT AN EXCEPTION. An integer column cannot hold infinity, cannot
    /// hold not-a-number and cannot hold a magnitude past its own range, so such a cell is ABSENT rather
    /// than wrong - and absent is what <c>of_calcempty</c> exists to fill [:L1987-L1989].
    /// </remarks>
    private static long? ToLongOrNull(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < long.MinValue || value > long.MaxValue)
        {
            return null;
        }

        return (long)Math.Truncate(value);
    }
}
