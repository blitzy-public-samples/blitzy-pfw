// ==============================================================================================
//  MacroInvoker.cs
//  The macro-invocation protocol for the column-expression engine: the CLIENT-CALLBACK side of
//  contract C-04's InvokeMethodChannel.
//  --------------------------------------------------------------------------------------------
//  PORTED FROM
//      ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L14
//          event type any oncolumnexpinvokemethod ( long row,  dwobject dwo,  string name,
//                                                   string args[] )
//      ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru
//          :L58-L63     the funcdata structure - name, fullname, args[], builtin
//          :L120-L121   the two grammar sentinels FUNC_VAR and FUNC_INVOKE
//          :L1290-L1311 bDD, the DOUBLED sigil, and `fnData.builtIn = bDD`
//          :L1306-L1310 the context function macro, refused outright
//          :L1386-L1401 parse-time built-in name validation and the two arity rules
//          :L2226-L2236 the reverse dispatch loop and argument evaluation
//          :L2237-L2257 the FUNC_VAR arm - VARIABLE resolution, never a client callback
//          :L2258-L2284 the FUNC_INVOKE arm - the DYNAMIC firing at :L2263
//          :L2286-L2308 the default arm - the DIRECT firing at :L2287
//          :L2282, :L2306  the two "invalid return value" refusals
//          :L2310       the parenthesis wrap applied to every macro result
//      docs/n_cst_dwsvc_columnexp.md:L104-L151 (§宏函数)
//          the authoritative prose specification of both invocation forms, with both worked
//          `choose case name` handlers
//
//  ============ WHY THE STREAM IS INVERTED, AND WHY THAT IS STRUCTURAL (C-K) =====================
//  THE LEGACY EXPECTS THE *APPLICATION* TO IMPLEMENT THE MACRO SWITCH. The specification says so in
//  its own words: the function is defined in the DataWindow's `OnColumnExpInvokeMethod` event
//  [docs/n_cst_dwsvc_columnexp.md:L106], and the worked handler is a `choose case name` that returns
//  the computed value [:L124-L127 direct, :L146-L151 dynamic]. The engine does not own the macro
//  implementations; it ASKS FOR THEM, from inside `_of_PreprocessExp`, mid-calculation, at :L2263 and
//  :L2287.
//
//  In process that is an ordinary event trigger, because the application is already listening. Across
//  a network boundary the application is a CLIENT, and a client does not answer questions it was
//  never asked. So DataServices - the SERVER - must issue a request TO ITS CLIENT and block for the
//  answer before the calculation can continue. That inversion is not a stylistic preference: a
//  unidirectional client-speaks-first design cannot express it at all, which is exactly why contract
//  C-04 declares InvokeMethodChannel as a bidirectional stream
//  [shared/PowerFramework.Contracts/Proto/dataservices.v1.proto, section 12].
//
//  A CONSEQUENCE WORTH STATING PLAINLY: A CLIENT THAT DOES NOT SERVICE THIS CHANNEL STALLS ITS OWN
//  CALCULATIONS. There is no fallback macro implementation to fall back to, and inventing one - a
//  zero, an empty string, a default - would substitute a WRONG answer for a MISSING one, which is
//  worse than the stall. Nothing in this file fabricates a macro result.
//
//  ============ ORDERING: PATTERN (b), STRICTLY SYNCHRONOUS, NO REORDERING =======================
//  AAP 0.6.1.4 assigns macro invocation ordering pattern (b) - a synchronous request/response chain
//  with NO reordering permitted - for the blunt reason that "the calculation cannot proceed without
//  the returned value". This file therefore models the call as an awaited round trip with zero
//  reordering tolerance: a response that does not correlate to the invocation still outstanding is a
//  HARD ERROR (<see cref="MacroProtocolViolationException"/>), never an invitation to buffer and
//  reorder. Cancellation and timeout, by contrast, are DEFINED OUTCOMES rather than exceptions,
//  because a network call can fail in transit where the legacy in-process call could not, and
//  handling that failure is required BY the transition (AAP 0.5.3).
//
//  ============ WHAT THIS FILE OWNS, AND WHAT IT DELIBERATELY DOES NOT ===========================
//  IT OWNS THE PROTOCOL, NOT THE TRANSPORT. Every type here is transport-agnostic and the outbound
//  edge is the injectable <see cref="IMacroInvocationChannel"/>, so the engine and the tests can
//  supply an in-process implementation and reach every path with no gRPC host at all (C-H, the 80%
//  per-service line-coverage gate). `Grpc/ColumnExpressionService.cs` - a sibling, not this file -
//  binds the abstraction to the generated `dataservices.v1` stream.
//
//  IT DOES NOT EVALUATE. `_of_Evaluate` [:L2230] belongs to Expressions/DataWindowExpressionEvaluator.cs,
//  and the arguments that reach this file are ALREADY EVALUATED strings, exactly as `sArgs` is at
//  :L2235. What this file does provide is <see cref="MacroInvoker.IsEvaluationFailure"/>, the
//  `sVal = "!" or sVal = "?"` inspection the dispatch loop performs on each evaluated argument
//  [:L2231], because that inspection is part of the macro loop rather than part of evaluation.
//
//  IT DOES NOT RESOLVE VARIABLES. The FUNC_VAR arm [:L2239-L2257] is a variable lookup through
//  `_of_FindVarIndex`, and its two failure sites are the engine's. This file RECOGNISES the form and
//  routes it - see <see cref="MacroDispatchKind.VariableLookup"/> - so that `$$('name')` never
//  reaches the client macro channel.
//
//  IT DOES NOT SCAN. `nMacPos`, `nPos` and the caret arithmetic `nMacPos + (nPos - nMacPos) / 2`
//  belong to the scanner in ColumnExpressionEngine.cs; the caret position arrives here as a
//  parameter, the same way ParseErrorFormatter.CreateParseError takes it.
//
//  ============ PRESERVED IDENTIFIER SPELLINGS AND THE .editorconfig POSITION ====================
//  FUNC_VAR and FUNC_INVOKE keep their legacy SCREAMING_SNAKE spellings and exact values (AAP
//  0.4.5.3): those identifiers travel in serialized payloads - contract C-04 carries them on
//  `ExpressionSentinels` - in log records, and in characterization recordings, so a rename would
//  silently invalidate every stored comparison.
//
//  A REPORTED FINDING, NOT A SILENT ASSUMPTION. The root .editorconfig scopes its CA1707/IDE1006
//  naming suppressions to one section per DECLARING file, and it has NO section for this file. Its
//  ColumnExpressionEngine.cs section states the reason: it expects the sentinels to be DECLARED by
//  the engine and merely CONSUMED here. That expectation cannot be met from this file, because
//  ColumnExpressionEngine.cs is not among this file's declared dependencies and so cannot be
//  imported from it, while this file's own specification requires both constants' spellings and
//  values to be preserved HERE. The constants are therefore declared here and the coverage gap is
//  REPORTED rather than resolved by renaming them or by widening a suppression the .editorconfig
//  header explicitly forbids widening. There is no build impact today: the same .editorconfig
//  records that CA1707 is OFF at the default AnalysisMode and becomes active only at
//  AnalysisMode=Recommended or All, so raising the analysis mode is the event that would need a
//  section added for this path.
//
//  ============ BOUNDARIES ======================================================================
//  C-D  No UI. No dialog, no MessageBox, no window, no DPI, no font, no Win32. The legacy reported
//       every one of these failures through `MessageBox(...,StopSign!)`; each becomes a structured
//       ExpressionParseError instead, and only the DELIVERY CHANNEL changes - the text, the
//       category, the severity, the substitution arguments and the caret position are preserved.
//  C-B  These particular messages are HARDCODED CHINESE THAT DO NOT ROUTE THROUGH I18N, unlike the
//       equivalent messages elsewhere in the DataWindow service layer which do. That inconsistency
//       is legacy behaviour and is reproduced, not harmonised. No macro form the legacy lacks is
//       added; neither arity rule nor the built-in-name restriction is relaxed.
//  C-A  Nothing here reaches into another service. The only cross-service coupling is the published
//       contracts project, from which exactly one type is used: the ExpansionMode enum.
//       There is NO ScriptBridge coupling - AAP 0.2.1.4 establishes that `n_scriptinvoker` is not a
//       real dependency because C# has native variadic support, so "dynamic invocation" here means
//       the legacy's own $$Invoke form and nothing else.
//  AAP 0.5.3  No new package. Everything below is BCL plus the four in-repository dependencies.
// ==============================================================================================

using System.Collections;
using System.Collections.Immutable;
using System.Globalization;

using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Validators;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Expressions;

/// <summary>
/// The two reserved function-macro names the column-expression grammar defines, preserved verbatim.
/// </summary>
/// <remarks>
/// <para>
/// PORTED FROM <c>n_cst_dwsvc_columnexp.sru:L120-L121</c>, where both are declared as
/// <c>constant string</c> on the expression service itself and both are matched BY VALUE in the
/// parser [:L1386-L1401] and again in the dispatcher [:L2237-L2258]. A consumer that guessed a
/// different spelling would silently fail to dispatch, which is why contract C-04 carries both values
/// on its <c>ExpressionSentinels</c> message rather than leaving them implicit.
/// </para>
/// <para>
/// ONLY THE TWO FUNCTION SENTINELS LIVE HERE. The grammar's other three - the compute-object suffix
/// [:L95], the macro flag [:L1252] and the context sigil [:L1253] - are the scanner's, and the
/// scanner is <c>ColumnExpressionEngine.cs</c>. Splitting them this way keeps this file to the
/// invocation protocol and stops it from becoming a second, competing definition of the grammar.
/// </para>
/// </remarks>
public static class MacroSentinels
{
    /// <summary>
    /// The variable-reference sentinel: <b>the empty string</b> [n_cst_dwsvc_columnexp.sru:L120].
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS A LIVE SENTINEL, NOT A PLACEHOLDER, AND THAT IS THE EASIEST THING IN THE WHOLE
    /// GRAMMAR TO MISREAD. An empty function name is how the scanner records the dynamic-indirect
    /// variable form <c>$$('name')</c>: <c>$$(</c> is a doubled sigil immediately followed by an
    /// opening bracket, so the text between the sigil and the bracket is empty and
    /// <c>fnData.name</c> [:L1313] comes out as <c>""</c>. The reference is then held as a FUNCTION
    /// entry whose name happens to be empty rather than as a separate kind of entry.
    /// </para>
    /// <para>
    /// It carries a real arity rule - EXACTLY ONE argument [:L1389] - and a real dispatch arm
    /// [:L2239] that performs a VARIABLE LOOKUP. It is never forwarded to the application's macro
    /// handler, which is why <see cref="MacroDispatchKind.VariableLookup"/> exists as a distinct
    /// routing outcome.
    /// </para>
    /// </remarks>
    public const string FUNC_VAR = "";

    /// <summary>
    /// The dynamic-dispatch sentinel, <c>"Invoke"</c> [n_cst_dwsvc_columnexp.sru:L121].
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matched at [:L1393] during parsing and at [:L2258] during dispatch, and only ever in the
    /// doubled-sigil form: <c>$$Invoke($nameVar, args)</c>
    /// [docs/n_cst_dwsvc_columnexp.md:L134].
    /// </para>
    /// <para>
    /// A LEGACY LIMITATION THIS RECORDS RATHER THAN DEFENDS AGAINST: an application macro literally
    /// named <c>Invoke</c> is unreachable through the doubled form, because the sentinel has taken
    /// the name. The single-sigil direct form <c>$Invoke(...)</c> still reaches the application
    /// normally, because <c>builtin</c> is false there and the name is never compared against these
    /// sentinels at all.
    /// </para>
    /// </remarks>
    public const string FUNC_INVOKE = "Invoke";

    /// <summary>
    /// Tests whether a built-in function macro's name is the variable-reference sentinel.
    /// </summary>
    /// <param name="name">The function name as the scanner recorded it in <c>funcdata.name</c>.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="name"/> is the empty string. A
    /// <see langword="null"/> name answers <see langword="false"/>: PowerScript string array and
    /// structure members initialise to <c>""</c> and never to null, so a null here is a port-side
    /// absence rather than the legacy's empty-name case, and conflating the two would route a missing
    /// name into the variable-lookup arm.
    /// </returns>
    public static bool IsFuncVar(string? name) =>
        name is not null && string.Equals(name, FUNC_VAR, StringComparison.Ordinal);

    /// <summary>
    /// Tests whether a built-in function macro's name is the dynamic-dispatch sentinel.
    /// </summary>
    /// <param name="name">The function name as the scanner recorded it in <c>funcdata.name</c>.</param>
    /// <returns><see langword="true"/> when <paramref name="name"/> is exactly <c>"Invoke"</c>.</returns>
    /// <remarks>
    /// ORDINAL, CASE-SENSITIVE COMPARISON, PRESERVING POWERSCRIPT'S OWN SEMANTICS. PowerScript's
    /// relational operators on strings - and therefore <c>choose case</c> [:L1387] - compare case
    /// sensitively, so <c>$$INVOKE(...)</c> and <c>$$invoke(...)</c> match NEITHER sentinel in the
    /// legacy and are refused as undefined functions [:L1398-L1400]. Making the comparison
    /// case-insensitive here would ACCEPT two spellings the oracle rejects, which is a behaviour
    /// change dressed up as leniency (C-B).
    /// </remarks>
    public static bool IsFuncInvoke(string? name) =>
        string.Equals(name, FUNC_INVOKE, StringComparison.Ordinal);
}

/// <summary>
/// A macro's ALREADY-EVALUATED argument list, addressed the way the legacy addresses it: ONE-BASED.
/// </summary>
/// <remarks>
/// <para>
/// PORTED FROM <c>string sArgs[]</c> [n_cst_dwsvc_columnexp.sru:L2173], filled at [:L2235] from the
/// evaluated sub-expressions and handed to the application handler at [:L2263] and [:L2287]. The
/// specification's own handlers read <c>args[1]</c> and <c>args[2]</c>
/// [docs/n_cst_dwsvc_columnexp.md:L126, L149], so ONE-BASED IS THE PUBLISHED CONTRACT, not an
/// implementation detail of PowerScript.
/// </para>
/// <para>
/// WHY THIS TYPE EXISTS AT ALL. AAP 0.8.6 R9 names one-based-to-zero-based translation the single
/// most dangerous mechanical hazard in the whole refactor, because a silent off-by-one is
/// indistinguishable from a behavioural regression: the call still succeeds, the handler still runs,
/// and it simply receives the wrong value. The AAP's prescribed mitigation is a CENTRALISED one-based
/// indexing helper, and this is it - <see cref="ToZeroBasedIndex"/> is the ONLY place in this file
/// where the two conventions meet, and every other member is expressed in terms of it. Passing a raw
/// array around instead would scatter that conversion across every call site.
/// </para>
/// <para>
/// <see cref="UpperBound"/> DELIBERATELY MIRRORS POWERSCRIPT'S <c>UpperBound</c>, WHICH RETURNS THE
/// LAST VALID INDEX rather than a length. For a one-based list the two happen to have the same
/// numeric value, and that coincidence is exactly what makes the hazard invisible; naming the member
/// after the legacy function keeps the ported arity comparisons - <c>UpperBound(fnData.args) &lt;&gt; 1</c>
/// [:L1389] and <c>&lt; 1</c> [:L1394] - readable as the same comparisons.
/// </para>
/// <para>
/// IMMUTABLE, because a macro's arguments are fixed once evaluated, because an immutable list is safe
/// to share across the concurrent calls a service handles, and because value equality lets a
/// characterization recording compare argument lists directly.
/// </para>
/// <para>
/// IT IS <see cref="IEnumerable{T}"/> AND DELIBERATELY NOT <see cref="IReadOnlyList{T}"/>. That
/// interface's indexer is contractually ZERO-BASED, so implementing it would put a zero-based and a
/// one-based indexer on the same type - precisely the ambiguity AAP 0.8.6 R9 warns produces a silent
/// off-by-one. Enumeration carries no index and so carries no ambiguity, and it is enough for every
/// consumer that only needs to walk the arguments, LINQ included. Anything that needs a position uses
/// <see cref="this[int]"/> and gets the legacy convention, with no second convention available to
/// reach for by accident.
/// </para>
/// </remarks>
public sealed record MacroArgumentList : IEnumerable<string>
{
    private readonly ImmutableArray<string> _values = ImmutableArray<string>.Empty;

    /// <summary>Initialises an empty argument list.</summary>
    /// <remarks>
    /// Public because the type is a record and its <c>with</c> expressions need it; prefer
    /// <see cref="Empty"/> for the empty case so the shared instance is reused.
    /// </remarks>
    public MacroArgumentList()
    {
    }

    /// <summary>
    /// The empty argument list - the port of <c>sEmptyArray</c> [n_cst_dwsvc_columnexp.sru:L2173],
    /// the zero-length array the dispatch loop resets <c>sArgs</c> to on every iteration [:L2227].
    /// </summary>
    public static MacroArgumentList Empty { get; } = new();

    /// <summary>
    /// The arguments in ZERO-BASED storage order, for projecting onto the wire.
    /// </summary>
    /// <remarks>
    /// Contract C-04's <c>InvokeMethodRequest.args</c> is a protobuf repeated field and therefore
    /// zero-based; this is the one member intended for that projection, and it is named for the
    /// convention it hands out so a reader cannot mistake it for the addressable surface.
    /// </remarks>
    public ImmutableArray<string> Values
    {
        get => _values;
        init => _values = value.IsDefault ? ImmutableArray<string>.Empty : value;
    }

    /// <summary>
    /// The last valid ONE-BASED index - the port of <c>UpperBound(args)</c>. Zero when empty.
    /// </summary>
    public int UpperBound => _values.Length;

    /// <summary>How many arguments the list holds.</summary>
    public int Count => _values.Length;

    /// <summary><see langword="true"/> when there are no arguments at all.</summary>
    public bool IsEmpty => _values.IsEmpty;

    /// <summary>
    /// Reads one argument by its ONE-BASED position, the way <c>args[1]</c> reads in the
    /// specification's handler [docs/n_cst_dwsvc_columnexp.md:L126].
    /// </summary>
    /// <param name="oneBasedIndex">The one-based position, from 1 to <see cref="UpperBound"/>.</param>
    /// <returns>The evaluated argument text at that position.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="oneBasedIndex"/> is below 1 or above <see cref="UpperBound"/>.
    /// </exception>
    /// <remarks>
    /// IT THROWS RATHER THAN RETURNING THE EMPTY STRING, and that is deliberate. PowerBuilder raises a
    /// runtime error when a bounded array is read out of range, and every read this file performs is
    /// guarded upstream by a parse-time arity rule [:L1386-L1401], so an out-of-range read here can
    /// only be a CALLER MISTAKE and never data. Answering <c>""</c> instead would let a mistake
    /// masquerade as an empty argument, and an empty argument is legitimate.
    /// </remarks>
    public string this[int oneBasedIndex]
    {
        get
        {
            if (oneBasedIndex < 1 || oneBasedIndex > _values.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(oneBasedIndex),
                    oneBasedIndex,
                    "A macro argument list is ONE-BASED and holds "
                        + _values.Length.ToString(CultureInfo.InvariantCulture)
                        + " argument(s), so the valid positions are 1.."
                        + _values.Length.ToString(CultureInfo.InvariantCulture)
                        + ". The legacy addresses these as args[1], args[2] "
                        + "[docs/n_cst_dwsvc_columnexp.md:L126, L149].");
            }

            return _values[ToZeroBasedIndex(oneBasedIndex)];
        }
    }

    /// <summary>
    /// Reads one argument by its ONE-BASED position without throwing.
    /// </summary>
    /// <param name="oneBasedIndex">The one-based position.</param>
    /// <param name="value">
    /// The evaluated argument text when the position is valid; the empty string otherwise.
    /// </param>
    /// <returns><see langword="true"/> when the position is within 1..<see cref="UpperBound"/>.</returns>
    public bool TryGet(int oneBasedIndex, out string value)
    {
        if (oneBasedIndex < 1 || oneBasedIndex > _values.Length)
        {
            value = string.Empty;

            return false;
        }

        value = _values[ToZeroBasedIndex(oneBasedIndex)];

        return true;
    }

    /// <summary>
    /// Drops the FIRST argument and renumbers the rest down by one - the exact port of the
    /// <c>sArgs2</c> construction at <c>n_cst_dwsvc_columnexp.sru:L2259-L2262</c>.
    /// </summary>
    /// <returns>
    /// The remaining arguments, one-based from 1 again; <see cref="Empty"/> when this list holds one
    /// argument or none.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE CONCRETE MECHANISM OF DYNAMIC DISPATCH, AND GETTING IT WRONG IS SILENT. The legacy
    /// loop is <c>for nFnArgIdx = 2 to nFnArgCnt : sArgs2[nFnArgIdx - 1] = sArgs[nFnArgIdx]</c>, and
    /// the shifted list is what the firing at [:L2263] passes as <c>args</c> while
    /// <c>sArgs[1]</c> is passed separately as the resolved callee NAME. A port that forwarded the
    /// full array instead would hand the callee name to the handler as its first user argument, and
    /// EVERY client macro would receive silently shifted arguments while still returning a plausible
    /// value.
    /// </para>
    /// <para>
    /// The result is what makes both invocation forms POSITIONALLY UNIFORM on the wire, which contract
    /// C-04 states as a deliberate property: a client handler should not have to know which form
    /// invoked it.
    /// </para>
    /// </remarks>
    public MacroArgumentList DropFirst() =>
        _values.Length <= 1
            ? Empty
            : new MacroArgumentList { Values = _values.RemoveAt(0) };

    /// <summary>
    /// Builds a list from evaluated argument texts supplied in ZERO-BASED order.
    /// </summary>
    /// <param name="args">
    /// The evaluated arguments, first argument first. A <see langword="null"/> array yields
    /// <see cref="Empty"/>.
    /// </param>
    /// <returns>The one-based argument list.</returns>
    /// <remarks>
    /// A <see langword="null"/> ELEMENT BECOMES THE EMPTY STRING, which is faithful rather than
    /// lenient: PowerScript initialises string array elements to <c>""</c> and there is no such thing
    /// as a null element of a <c>string[]</c>, so <c>""</c> is precisely what the oracle would hold in
    /// that position.
    /// </remarks>
    public static MacroArgumentList FromEvaluated(params string?[]? args) =>
        args is null || args.Length == 0
            ? Empty
            : FromEvaluated((IEnumerable<string?>)args);

    /// <summary>
    /// Builds a list from evaluated argument texts supplied in ZERO-BASED order.
    /// </summary>
    /// <param name="args">
    /// The evaluated arguments, first argument first. A <see langword="null"/> sequence yields
    /// <see cref="Empty"/>.
    /// </param>
    /// <returns>The one-based argument list.</returns>
    public static MacroArgumentList FromEvaluated(IEnumerable<string?>? args)
    {
        if (args is null)
        {
            return Empty;
        }

        ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>();

        foreach (string? argument in args)
        {
            builder.Add(argument ?? string.Empty);
        }

        return builder.Count == 0
            ? Empty
            : new MacroArgumentList { Values = builder.ToImmutable() };
    }

    /// <summary>
    /// Builds a list from an already-immutable ZERO-BASED array of evaluated argument texts.
    /// </summary>
    /// <param name="args">
    /// The evaluated arguments. A default (uninitialised) array yields <see cref="Empty"/>.
    /// </param>
    /// <returns>The one-based argument list.</returns>
    /// <remarks>
    /// The overload that matches <c>FunctionReference.Args</c>' own storage type, so an argument list
    /// can be carried across from the parsed reference without a copy through an intermediate
    /// sequence.
    /// </remarks>
    public static MacroArgumentList FromEvaluated(ImmutableArray<string> args) =>
        args.IsDefaultOrEmpty
            ? Empty
            : new MacroArgumentList { Values = args };

    /// <summary>
    /// THE ONE PLACE THE TWO INDEX CONVENTIONS MEET: converts a legacy one-based position to a .NET
    /// zero-based offset.
    /// </summary>
    /// <param name="oneBasedIndex">The one-based position.</param>
    /// <returns>The zero-based offset.</returns>
    /// <remarks>
    /// Deliberately trivial and deliberately named. AAP 0.8.6 R9 requires every ported loop to route
    /// its indexing through a centralised helper OR be audited individually; concentrating the
    /// arithmetic in one named member is what makes the audit a one-line check instead of a file-wide
    /// review. It performs NO range validation, because its callers have already established the range
    /// and a second check here would hide which caller failed.
    /// </remarks>
    public static int ToZeroBasedIndex(int oneBasedIndex) => oneBasedIndex - 1;

    /// <summary>
    /// The inverse of <see cref="ToZeroBasedIndex"/>: converts a .NET zero-based offset to the legacy
    /// one-based position.
    /// </summary>
    /// <param name="zeroBasedIndex">The zero-based offset.</param>
    /// <returns>The one-based position.</returns>
    /// <remarks>
    /// Present so that code translating IN THE OTHER DIRECTION - reporting which wire element failed,
    /// for instance - names the conversion instead of writing a bare <c>+ 1</c> that a reader has to
    /// interpret.
    /// </remarks>
    public static int ToOneBasedIndex(int zeroBasedIndex) => zeroBasedIndex + 1;

    /// <summary>Enumerates the arguments in order, first argument first.</summary>
    /// <returns>An enumerator over the evaluated argument texts.</returns>
    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Compares two argument lists element by element.</summary>
    /// <param name="other">The list to compare against.</param>
    /// <returns><see langword="true"/> when both hold the same arguments in the same order.</returns>
    public bool Equals(MacroArgumentList? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null && _values.AsSpan().SequenceEqual(other._values.AsSpan());
    }

    /// <summary>Hashes the arguments in order.</summary>
    /// <returns>A hash consistent with <see cref="Equals(MacroArgumentList?)"/>.</returns>
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(_values.Length);

        foreach (string argument in _values)
        {
            hash.Add(argument);
        }

        return hash.ToHashCode();
    }
}


/// <summary>
/// What the dispatcher must do with one function-macro reference, once its sigils, its name and its
/// arity have been checked.
/// </summary>
/// <remarks>
/// The three members are the three arms of the legacy dispatch at
/// <c>n_cst_dwsvc_columnexp.sru:L2237-L2308</c>, plus the refusal the parser reaches first. Nothing is
/// added: the legacy has exactly one variable arm [:L2239], exactly one client-callback path reached
/// two different ways [:L2263 and :L2287], and a set of refusals that all end in <c>return</c>.
/// </remarks>
public enum MacroDispatchKind
{
    /// <summary>
    /// The reference is REFUSED and no dispatch happens. <see cref="MacroDispatchPlan.Error"/>
    /// carries the structured form of the legacy dialog, including its return code.
    /// </summary>
    Rejected = 0,

    /// <summary>
    /// The macro's implementation belongs to the APPLICATION and must be fetched across the inverted
    /// channel - the firings at <c>n_cst_dwsvc_columnexp.sru:L2263</c> (dynamic) and <c>:L2287</c>
    /// (direct). Both arrive here; they differ only in how
    /// <see cref="MacroDispatchPlan.Name"/> and <see cref="MacroDispatchPlan.Arguments"/> were derived.
    /// </summary>
    ClientCallback = 1,

    /// <summary>
    /// The reference is the <see cref="MacroSentinels.FUNC_VAR"/> form <c>$$('name')</c> and resolves
    /// to a VARIABLE, not to a macro [<c>n_cst_dwsvc_columnexp.sru:L2239-L2257</c>]. IT MUST NOT REACH
    /// THE CLIENT CHANNEL: the legacy never fires the event on this arm, and forwarding it would
    /// invent a callback the application has no handler for.
    /// </summary>
    VariableLookup = 2,
}

/// <summary>
/// The decision the invoker reaches about one function-macro reference: refuse it, resolve it as a
/// variable, or call back into the client - and, in that last case, with exactly which name and which
/// arguments.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE DECISION IS A VALUE RATHER THAN A SEQUENCE OF SIDE EFFECTS. The legacy fuses classification,
/// validation, argument shifting and firing into one <c>choose case</c> [:L2237-L2308], where the
/// name-versus-<c>fullName</c> distinction in the error messages and the <c>sArgs</c>-versus-<c>sArgs2</c>
/// distinction in the firings are three lines apart and easy to transpose. Producing an inspectable
/// plan first makes each of those decisions individually assertable, which is what lets the arity
/// matrix, the argument-shift test and the routing test all run with no channel and no host at all
/// (C-H).
/// </para>
/// <para>
/// A plan is always internally consistent because the only ways to obtain one are the four factory
/// methods below, each of which sets exactly the fields its kind defines and leaves the rest at their
/// documented resting values.
/// </para>
/// </remarks>
public sealed record MacroDispatchPlan
{
    private MacroDispatchPlan()
    {
    }

    /// <summary>What the dispatcher must do.</summary>
    public MacroDispatchKind Kind { get; private init; }

    /// <summary>
    /// Which legacy dispatch arm the reference selected, as classified from
    /// <c>funcdata.builtin</c> and the function name.
    /// </summary>
    /// <remarks>
    /// <see cref="MacroFunctionDispatch.Application"/> is also the resting value on a refusal, because
    /// a refused reference has no arm; read <see cref="Kind"/> first.
    /// </remarks>
    public MacroFunctionDispatch Dispatch { get; private init; }

    /// <summary>
    /// Contract C-04's expansion mode for this reference, for the request's <c>form</c> field.
    /// </summary>
    /// <remarks>
    /// Projected by <c>ExpansionSigils.ProjectFunctionReference</c>, so the mapping - direct to
    /// <c>MacroDirect</c>, <c>$$Invoke</c> to <c>MacroDynamic</c>, <c>$$(...)</c> to
    /// <c>DynamicIndirect</c> - is stated once, in the file that owns the expansion-mode model, rather
    /// than twice. Contract C-04 marks the field diagnostic-only: the arguments are already
    /// normalised, so a handler need not branch on it.
    /// </remarks>
    public ExpansionMode Form { get; private init; }

    /// <summary>
    /// The reference's <c>funcdata.builtin</c> flag, carried through unchanged.
    /// </summary>
    /// <remarks>
    /// THE FLAG'S NAME IS MISLEADING AND IS PRESERVED ANYWAY. It does not mean "the engine implements
    /// this"; it is literally <c>bDD</c>, "written with a DOUBLED sigil"
    /// [<c>n_cst_dwsvc_columnexp.sru:L1290</c> assigned at <c>:L1311</c>]. That single line is the whole
    /// reason <c>$Func(...)</c> and <c>$$Func(...)</c> behave completely differently: the first is an
    /// application macro and the second is checked against the two reserved names and refused if it
    /// matches neither.
    /// </remarks>
    public bool Builtin { get; private init; }

    /// <summary>
    /// The name to invoke on the client. Meaningful only for
    /// <see cref="MacroDispatchKind.ClientCallback"/>; the empty string otherwise.
    /// </summary>
    /// <remarks>
    /// FOR THE DIRECT FORM this is <c>fns[nFnIdx].name</c> [:L2287] - the literal text after the single
    /// <c>$</c>. FOR THE DYNAMIC FORM it is <c>sArgs[1]</c> [:L2263], the RESOLVED callee name: the
    /// client receives the function's actual name and never the sentinel, nor the variable that held
    /// it. The specification's example makes the difference concrete - <c>$$Invoke($单价格式化, n2, $精度)</c>
    /// with <c>单价格式化</c> holding <c>"FormatPrice"</c> arrives at the handler as
    /// <c>name = "FormatPrice"</c> [docs/n_cst_dwsvc_columnexp.md:L139-L149].
    /// </remarks>
    public string Name { get; private init; } = string.Empty;

    /// <summary>
    /// The arguments to forward, one-based. Meaningful only for
    /// <see cref="MacroDispatchKind.ClientCallback"/>; <see cref="MacroArgumentList.Empty"/> otherwise.
    /// </summary>
    /// <remarks>
    /// FOR THE DIRECT FORM this is <c>sArgs</c> whole [:L2287]. FOR THE DYNAMIC FORM it is
    /// <c>sArgs2</c> [:L2259-L2262] - the list WITH THE CALLEE NAME REMOVED and the remainder
    /// renumbered from 1. So <c>args[1]</c> is the handler's first real argument in both forms.
    /// </remarks>
    public MacroArgumentList Arguments { get; private init; } = MacroArgumentList.Empty;

    /// <summary>
    /// The variable name to resolve. Meaningful only for
    /// <see cref="MacroDispatchKind.VariableLookup"/>; the empty string otherwise.
    /// </summary>
    /// <remarks>
    /// This is <c>sArgs[1]</c> as the FUNC_VAR arm reads it [:L2240], already evaluated - the legacy
    /// runs the single argument through the DataWindow evaluator first, so
    /// <c>$$('单价')</c> and <c>$$(some_column)</c> both arrive here as a plain name, which is what
    /// makes the form "dynamic INDIRECT": the name itself is computed at calculation time.
    /// </remarks>
    public string VariableName { get; private init; } = string.Empty;

    /// <summary>
    /// The structured refusal. Non-<see langword="null"/> exactly when
    /// <see cref="Kind"/> is <see cref="MacroDispatchKind.Rejected"/>.
    /// </summary>
    public ExpressionParseError? Error { get; private init; }

    /// <summary><see langword="true"/> when the reference was refused.</summary>
    public bool IsRejected => Kind == MacroDispatchKind.Rejected;

    /// <summary>
    /// Builds the plan for the DIRECT application form <c>$Func(args)</c> [:L2286-L2287].
    /// </summary>
    /// <param name="name">The macro name, from <c>funcdata.name</c>.</param>
    /// <param name="arguments">The evaluated arguments, forwarded whole.</param>
    /// <param name="form">The projected expansion mode, normally <c>MacroDirect</c>.</param>
    /// <returns>A client-callback plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="arguments"/> is <see langword="null"/>.</exception>
    public static MacroDispatchPlan ForDirect(
        string? name,
        MacroArgumentList arguments,
        ExpansionMode form)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return new MacroDispatchPlan
        {
            Kind = MacroDispatchKind.ClientCallback,
            Dispatch = MacroFunctionDispatch.Application,
            Form = form,
            Builtin = false,
            Name = name ?? string.Empty,
            Arguments = arguments,
        };
    }

    /// <summary>
    /// Builds the plan for the DYNAMIC form <c>$$Invoke($nameVar, args)</c> [:L2258-L2263], with the
    /// callee name already split off and the remaining arguments already renumbered.
    /// </summary>
    /// <param name="resolvedName">The resolved callee name - <c>sArgs[1]</c>.</param>
    /// <param name="forwardedArguments">The remaining arguments - <c>sArgs2</c>.</param>
    /// <param name="form">The projected expansion mode, normally <c>MacroDynamic</c>.</param>
    /// <returns>A client-callback plan.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="forwardedArguments"/> is <see langword="null"/>.
    /// </exception>
    public static MacroDispatchPlan ForDynamic(
        string? resolvedName,
        MacroArgumentList forwardedArguments,
        ExpansionMode form)
    {
        ArgumentNullException.ThrowIfNull(forwardedArguments);

        return new MacroDispatchPlan
        {
            Kind = MacroDispatchKind.ClientCallback,
            Dispatch = MacroFunctionDispatch.DynamicInvoke,
            Form = form,
            Builtin = true,
            Name = resolvedName ?? string.Empty,
            Arguments = forwardedArguments,
        };
    }

    /// <summary>
    /// Builds the plan for the <see cref="MacroSentinels.FUNC_VAR"/> form <c>$$('name')</c>
    /// [:L2239-L2257], which routes to variable resolution and NOT to the client.
    /// </summary>
    /// <param name="variableName">The resolved variable name - <c>sArgs[1]</c>.</param>
    /// <param name="form">The projected expansion mode, normally <c>DynamicIndirect</c>.</param>
    /// <returns>A variable-lookup plan.</returns>
    public static MacroDispatchPlan ForVariableLookup(string? variableName, ExpansionMode form) =>
        new()
        {
            Kind = MacroDispatchKind.VariableLookup,
            Dispatch = MacroFunctionDispatch.VariableLookup,
            Form = form,
            Builtin = true,
            VariableName = variableName ?? string.Empty,
        };

    /// <summary>
    /// Builds a refusal carrying the structured form of the legacy dialog the parser raised.
    /// </summary>
    /// <param name="error">The structured error. Its own <c>ReturnCode</c> is the legacy return.</param>
    /// <returns>A rejected plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static MacroDispatchPlan ForRejection(ExpressionParseError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new MacroDispatchPlan
        {
            Kind = MacroDispatchKind.Rejected,
            Error = error,
        };
    }
}


/// <summary>
/// One macro invocation travelling SERVER to CLIENT across the inverted channel - the port of the
/// argument list of <c>#DataWindow.Event OnColumnExpInvokeMethod(row, dwo, name, args)</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORTED FROM <c>se_cst_dw.sru:L14</c>, whose four parameters map one for one:
/// <c>long row</c> to <see cref="Row"/>, <c>dwobject dwo</c> to <see cref="Dwo"/>,
/// <c>string name</c> to <see cref="Name"/> and <c>string args[]</c> to <see cref="Arguments"/>.
/// The remaining members are correlation and diagnostics that a network boundary requires and an
/// in-process event trigger does not.
/// </para>
/// <para>
/// <see cref="Dwo"/> IS THE HOST'S ACCESSOR SURFACE AND NOT A LIVE UI HANDLE (C-D). The legacy
/// <c>dwobject</c> is a pointer into a Windows control; <see cref="IDataWindowObject"/> exposes only
/// the four members the ported call sites actually read, and this file reads exactly one of them -
/// <see cref="IDataWindowObject.Name"/>, which the invalid-return message needs as
/// <c>String(dwo.name)</c> [:L2282, :L2306].
/// </para>
/// </remarks>
public sealed record MacroInvocation
{
    /// <summary>
    /// The correlation identifier. REQUIRED, and required for a concrete reason: one calculation may
    /// invoke several macros, and a macro ARGUMENT may itself contain a macro
    /// [<c>n_cst_dwsvc_columnexp.sru:L2216-L2220</c> rewrites nested references], so answers must be
    /// matched to questions rather than assumed to arrive in issue order.
    /// </summary>
    public required string InvocationId { get; init; }

    /// <summary>
    /// The monotonic issue number, from 1, assigned by the invoker that created this invocation.
    /// </summary>
    /// <remarks>
    /// CARRIED FOR DETECTION ONLY, exactly as contract C-04 says of every sequencing token in this
    /// service: a gap or a reversal is a hard error, never an invitation to reorder. Under ordering
    /// pattern (b) it is redundant with <see cref="InvocationId"/> for correlation and is present so a
    /// recording, a log line or a stalled-channel diagnosis can be read in issue order.
    /// </remarks>
    public required long Sequence { get; init; }

    /// <summary>The ONE-BASED row ordinal being calculated - the legacy <c>row</c> parameter.</summary>
    public required long Row { get; init; }

    /// <summary>The column the expression is bound to - the legacy <c>dwo</c> parameter.</summary>
    public required IDataWindowObject Dwo { get; init; }

    /// <summary>
    /// The macro name the client's <c>choose case name</c> matches on - ALREADY RESOLVED for the
    /// dynamic form.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The arguments, ONE-BASED, already evaluated and - for the dynamic form - already shifted so
    /// that position 1 is the handler's first real argument.
    /// </summary>
    public required MacroArgumentList Arguments { get; init; }

    /// <summary>Which of the two macro forms produced this invocation. Diagnostics only.</summary>
    public ExpansionMode Form { get; init; } = ExpansionMode.Unspecified;

    /// <summary>
    /// The originating reference's <c>funcdata.builtin</c> flag - "written with a doubled sigil".
    /// Carried so a consumer can correlate the invocation with the reference that produced it.
    /// </summary>
    public bool Builtin { get; init; }

    /// <summary>
    /// The expression session this invocation belongs to, or <see langword="null"/> when the invoker
    /// is being driven in process with no session.
    /// </summary>
    /// <remarks>
    /// Session identity is not transport: AAP 0.6.2.3 makes the expression session the thing that
    /// scopes cross-DataWindow variable handles, so it is part of the protocol. It stays nullable so
    /// an in-process caller need not fabricate one.
    /// </remarks>
    public string? SessionId { get; init; }

    /// <summary>
    /// The session-scoped DataWindow handle, or <see langword="null"/> when there is none.
    /// </summary>
    public string? DataWindowHandle { get; init; }
}

/// <summary>
/// One macro answer travelling CLIENT to SERVER across the inverted channel - the port of the
/// <c>any</c> that <c>OnColumnExpInvokeMethod</c> returns.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Value"/> IS <c>object?</c> AND IS NOT NARROWED, per AAP 0.4.5.2's mapping of
/// PowerScript's <c>any</c>. Narrowing it to <see cref="string"/> would break the specification's own
/// worked handler, which returns <c>Round(Double(args[1]), Long(args[2]))</c> - a NUMBER
/// [docs/n_cst_dwsvc_columnexp.md:L126, L149].
/// </para>
/// <para>
/// A DELIBERATE, DOCUMENTED NARROWING LIVES HERE (AAP 0.1.5: narrow with a defined error, never widen
/// with a guess). PowerScript's <c>any</c> can hold a TYPED NULL - a null string is still a string, and
/// <c>ClassName</c> reports <c>"string"</c> for it - whereas C#'s <c>object?</c> cannot: a boxed
/// <c>int?</c> without a value is indistinguishable from any other null. A null
/// <see cref="Value"/> therefore matches no arm of the conversion switch and is reported through the
/// site's own "invalid return value" message. Both the legacy and this port ABORT the calculation in
/// that case - the legacy through a null-propagating rendering that trips its own
/// <c>if sVal = "" then return ""</c> guard [:L2201, :L745] - so the abort is preserved and only the
/// diagnostic differs.
/// </para>
/// </remarks>
public sealed record MacroInvocationResponse
{
    /// <summary>
    /// The correlation identifier being answered. MUST equal the invocation's; a mismatch is a
    /// protocol violation and never a reorder opportunity.
    /// </summary>
    public required string InvocationId { get; init; }

    /// <summary>
    /// The issue number being answered, when the channel echoes it, or <see langword="null"/> when it
    /// does not. Checked against the invocation's when present.
    /// </summary>
    public long? Sequence { get; init; }

    /// <summary>The value the client's handler returned. The legacy <c>any</c>.</summary>
    public object? Value { get; init; }

    /// <summary>
    /// Set when the client has NO HANDLER for this name at all - distinct from a handler that ran and
    /// returned an unusable type.
    /// </summary>
    /// <remarks>
    /// THE LEGACY HAS NO ANALOGUE, AND THE PORT MAPS IT ONTO LEGACY BEHAVIOUR RATHER THAN INVENTING
    /// NEW BEHAVIOUR. In process there is always a handler, even when it falls through its
    /// <c>choose case</c> without returning - and a fall-through yields a value whose class matches no
    /// arm, so the legacy raises "返回值无效" and aborts [:L2282, :L2306]. This flag therefore produces
    /// exactly that outcome, and exists only so a client can say "not mine" explicitly instead of
    /// fabricating a null the engine would then treat as a real answer.
    /// </remarks>
    public bool Unhandled { get; init; }
}

/// <summary>
/// The CLIENT-CALLBACK EDGE of contract C-04's <c>InvokeMethodChannel</c>: the one member through
/// which DataServices, the server, asks its client to execute a macro and waits for the answer.
/// </summary>
/// <remarks>
/// <para>
/// THE INTERFACE IS THE INVERSION, EXPRESSED AS A TYPE. Its direction - server calls client - is the
/// whole reason the wire contract needs a bidirectional stream rather than an ordinary unary call, and
/// stating it as an injectable single-member abstraction has two payoffs: the engine depends on the
/// PROTOCOL rather than on gRPC, and every path in <see cref="MacroInvoker"/> is reachable from an
/// in-process stub with no host, no port and no certificate (C-H).
/// </para>
/// <para>
/// IMPLEMENTATIONS MUST ANSWER, OR FAIL. Returning <see langword="null"/> is a protocol violation.
/// An implementation that cannot service the invocation sets
/// <see cref="MacroInvocationResponse.Unhandled"/>; an implementation whose transport failed should
/// let that failure surface, because there is no defensible substitute for a macro result and
/// inventing one would put a wrong number into a calculation.
/// </para>
/// </remarks>
public interface IMacroInvocationChannel
{
    /// <summary>
    /// Asks the client to execute one macro and waits for its answer.
    /// </summary>
    /// <param name="invocation">The macro to execute, its name already resolved and its arguments already shifted.</param>
    /// <param name="cancellationToken">
    /// Cancels the wait. Honouring it is required: the legacy in-process call could not fail in
    /// transit and this one can, so an unbounded wait would be a failure mode the legacy never had.
    /// </param>
    /// <returns>
    /// The client's answer. Never <see langword="null"/>, and its
    /// <see cref="MacroInvocationResponse.InvocationId"/> must be the one it was asked about.
    /// </returns>
    ValueTask<MacroInvocationResponse> InvokeAsync(
        MacroInvocation invocation,
        CancellationToken cancellationToken);
}

/// <summary>
/// Raised when the macro channel breaks the SYNCHRONOUS, NO-REORDERING discipline that ordering
/// pattern (b) requires: it answered a different invocation, echoed a different sequence number,
/// answered one that had already been retired, or answered nothing at all.
/// </summary>
/// <remarks>
/// <para>
/// A HARD ERROR ON PURPOSE. AAP 0.6.1.4 assigns macro invocation ordering pattern (b) - "strictly
/// synchronous, no reordering" - and states that an out-of-order arrival is a hard error rather than a
/// reorder opportunity. Buffering a mismatched answer and hoping the right one follows would let a
/// value computed for one macro be substituted into another's expression, which is silent data
/// corruption; failing loudly is also the fail-fast posture AAP 0.1.4 requires be preserved as
/// fail-fast rather than softened into graceful degradation.
/// </para>
/// <para>
/// It is deliberately NOT one of <see cref="MacroInvocationOutcome"/>'s members. Cancellation and
/// timeout are outcomes because they are things the environment does; a correlation failure is a
/// BROKEN IMPLEMENTATION, and giving it a result value would invite a caller to carry on past it.
/// </para>
/// </remarks>
public sealed class MacroProtocolViolationException : InvalidOperationException
{
    /// <summary>Initialises the exception with a default message.</summary>
    public MacroProtocolViolationException()
        : base("The macro invocation channel broke the synchronous ordering discipline.")
    {
    }

    /// <summary>Initialises the exception with a message.</summary>
    /// <param name="message">The diagnostic message.</param>
    public MacroProtocolViolationException(string? message)
        : base(message)
    {
    }

    /// <summary>Initialises the exception with a message and an inner cause.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public MacroProtocolViolationException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initialises the exception with the correlation detail that failed.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="expectedInvocationId">The identifier the invoker was waiting on.</param>
    /// <param name="invocationId">The identifier the channel answered with, if any.</param>
    /// <param name="late">
    /// <see langword="true"/> when the answered identifier belongs to an invocation that had already
    /// completed, which distinguishes a LATE answer from a merely unknown one.
    /// </param>
    public MacroProtocolViolationException(
        string? message,
        string? expectedInvocationId,
        string? invocationId,
        bool late)
        : base(message)
    {
        ExpectedInvocationId = expectedInvocationId;
        InvocationId = invocationId;
        Late = late;
    }

    /// <summary>The identifier the invoker was waiting on, when one is known.</summary>
    public string? ExpectedInvocationId { get; }

    /// <summary>The identifier the channel answered with, when one is known.</summary>
    public string? InvocationId { get; }

    /// <summary>
    /// <see langword="true"/> when the answer belongs to an invocation that has already been retired -
    /// a late answer rather than an unrecognised one.
    /// </summary>
    public bool Late { get; }
}


/// <summary>
/// How one macro invocation ended.
/// </summary>
/// <remarks>
/// THREE MEMBERS ARE LEGACY OUTCOMES AND TWO ARE OUTCOMES THE NETWORK BOUNDARY CREATES. The legacy
/// dispatch can only render a value [:L2264-L2308], resolve a variable [:L2239-L2257] or refuse and
/// return the empty string. <see cref="Cancelled"/> and <see cref="TimedOut"/> have no legacy locator
/// because an in-process call cannot be cancelled mid-flight and cannot time out; handling them is
/// required BY the transition rather than being an improvement layered on top of it (AAP 0.5.3), and
/// each carries a defined <c>RetCode</c> so a caller can tell them apart from a refusal.
/// </remarks>
public enum MacroInvocationOutcome
{
    /// <summary>
    /// The client answered with a value whose runtime type matched an arm of the conversion switch, and
    /// <see cref="MacroInvocationResult.Rendered"/> carries the expression fragment.
    /// </summary>
    Rendered = 0,

    /// <summary>
    /// The reference was the <see cref="MacroSentinels.FUNC_VAR"/> variable form, so no client call was
    /// made at all and <see cref="MacroInvocationResult.VariableName"/> names the variable the engine
    /// must resolve.
    /// </summary>
    VariableLookup = 1,

    /// <summary>
    /// The invocation was refused. Either the parser refused the reference, or the client's answer
    /// matched no arm of the conversion switch, or the client had no handler at all.
    /// <see cref="MacroInvocationResult.Error"/> carries the legacy message.
    /// </summary>
    Rejected = 2,

    /// <summary>
    /// The caller's <see cref="CancellationToken"/> was signalled while the invoker was waiting for the
    /// client's answer. Carries <c>RetCode.CANCELLED</c>.
    /// </summary>
    Cancelled = 3,

    /// <summary>
    /// The invoker's own <see cref="MacroInvoker.InvocationTimeout"/> elapsed before the client
    /// answered. Carries <c>RetCode.E_TIME_OUT</c>.
    /// </summary>
    TimedOut = 4,
}

/// <summary>
/// The result of one macro invocation: the rendered expression fragment, the variable to resolve, or
/// the refusal - together with everything a characterization recording needs to compare it.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE RENDERING IS CARRIED ALONGSIDE THE VALUE. The observable behaviour of a macro is not the
/// value the handler returned but the TEXT the engine substituted into the expression: two different
/// values can render identically, while two identical values cannot render differently. The rendering
/// is therefore what a paired legacy/target recording compares, which is why contract C-04's
/// <c>MacroResult</c> carries both fields and why this record does too.
/// </para>
/// <para>
/// <see cref="Aborts"/> IS THE EMPTY-STRING SENTINEL, MADE EXPLICIT. Every refusal in
/// <c>_of_PreprocessExp</c> ends in <c>return ""</c>, and its caller reads that as failure -
/// <c>if sExp = "" then return false</c> [<c>n_cst_dwsvc_columnexp.sru:L745</c>], with the same guard
/// repeated on every nested preprocessing call [:L2183, :L2186, :L2201, :L2204]. A boolean flag says
/// so at the type level, so a caller cannot accidentally treat an aborted invocation as one that
/// legitimately produced nothing.
/// </para>
/// </remarks>
public sealed record MacroInvocationResult
{
    private MacroInvocationResult()
    {
    }

    /// <summary>How the invocation ended.</summary>
    public MacroInvocationOutcome Outcome { get; private init; }

    /// <summary>
    /// The correlation identifier of the invocation, or <see langword="null"/> when no invocation was
    /// issued - which is the case for both a parse-time refusal and a variable lookup.
    /// </summary>
    public string? InvocationId { get; private init; }

    /// <summary>
    /// The monotonic issue number of the invocation, or <see langword="null"/> when none was issued.
    /// </summary>
    public long? Sequence { get; private init; }

    /// <summary>
    /// The value the client returned, unchanged. Only meaningful for
    /// <see cref="MacroInvocationOutcome.Rendered"/> and for a rejection caused by an unusable type,
    /// where it is the very value that was rejected.
    /// </summary>
    public object? Value { get; private init; }

    /// <summary>
    /// The legacy <c>ClassName</c> token the value projected onto, or <see langword="null"/> when it
    /// projected onto none - which is exactly the condition the legacy's <c>case else</c> detects.
    /// </summary>
    public string? ClassNameToken { get; private init; }

    /// <summary>
    /// The expression fragment the legacy substituted, BEFORE the dispatcher's parenthesis wrap -
    /// <c>'text'</c>, <c>42</c>, <c>1=1</c>, <c>DateTime('...')</c>.
    /// </summary>
    /// <remarks>
    /// Unwrapped because the wrap at <c>n_cst_dwsvc_columnexp.sru:L2310</c> sits OUTSIDE the conversion
    /// switch and is applied identically to both forms; keeping the two apart means the switch can be
    /// asserted on its own. <see cref="WrappedRendering"/> is what actually reaches the expression.
    /// </remarks>
    public string? Rendered { get; private init; }

    /// <summary>
    /// The fragment as it reaches the expression - <see cref="Rendered"/> inside parentheses, the port
    /// of <c>sVal = "(" + sVal + ")"</c> [<c>n_cst_dwsvc_columnexp.sru:L2310</c>].
    /// </summary>
    /// <remarks>
    /// THE WRAP IS NOT COSMETIC. It is what stops a returned expression from re-associating with the
    /// operators around it: a macro answering <c>1+2</c> substituted bare into <c>$M() * 3</c> would
    /// evaluate as <c>1+2*3</c>, and wrapped it evaluates as <c>(1+2)*3</c>. Because it is observable,
    /// it is part of what a recording compares.
    /// </remarks>
    public string? WrappedRendering { get; private init; }

    /// <summary>
    /// <see langword="true"/> when the client reported that it has no handler for the name.
    /// </summary>
    public bool Unhandled { get; private init; }

    /// <summary>
    /// The variable to resolve, for <see cref="MacroInvocationOutcome.VariableLookup"/>; the empty
    /// string otherwise.
    /// </summary>
    public string VariableName { get; private init; } = string.Empty;

    /// <summary>
    /// The structured refusal, for <see cref="MacroInvocationOutcome.Rejected"/>; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public ExpressionParseError? Error { get; private init; }

    /// <summary>
    /// The <c>RetCode</c> a caller should surface, or <see langword="null"/> where the legacy returns
    /// only the empty string and carries no code.
    /// </summary>
    /// <remarks>
    /// NULL IS MEANINGFUL AND IS NOT A MISSING VALUE. <c>_of_PreprocessExp</c> returns a STRING, so its
    /// failure value is <c>""</c> and it has no return code to give: both invalid-return sites
    /// [:L2282, :L2306] answer <c>""</c> and nothing else. The parse-time refusals DO carry
    /// <c>RetCode.E_INVALID_ARGUMENT</c> because the scanner is a <c>long</c>-returning function, and
    /// the two transition-created outcomes carry codes of their own.
    /// </remarks>
    public long? ReturnCode { get; private init; }

    /// <summary>
    /// <see langword="true"/> when the calculation must abort - the port of the empty-string sentinel.
    /// </summary>
    /// <remarks>
    /// True for every outcome except <see cref="MacroInvocationOutcome.Rendered"/> and
    /// <see cref="MacroInvocationOutcome.VariableLookup"/>. A variable lookup does not abort: it hands
    /// off to the engine's variable resolution, which has abort conditions of its own [:L2243, :L2255].
    /// </remarks>
    public bool Aborts =>
        Outcome is not (MacroInvocationOutcome.Rendered or MacroInvocationOutcome.VariableLookup);

    /// <summary>
    /// Builds the result for a value the conversion switch accepted.
    /// </summary>
    /// <param name="invocationId">The invocation's correlation identifier.</param>
    /// <param name="sequence">The invocation's issue number.</param>
    /// <param name="value">The value the client returned.</param>
    /// <param name="classNameToken">The legacy <c>ClassName</c> token it projected onto.</param>
    /// <param name="rendered">The unwrapped expression fragment.</param>
    /// <param name="wrappedRendering">The fragment inside parentheses, as substituted.</param>
    /// <returns>A rendered result.</returns>
    public static MacroInvocationResult ForRendered(
        string invocationId,
        long sequence,
        object? value,
        string classNameToken,
        string rendered,
        string wrappedRendering) =>
        new()
        {
            Outcome = MacroInvocationOutcome.Rendered,
            InvocationId = invocationId,
            Sequence = sequence,
            Value = value,
            ClassNameToken = classNameToken,
            Rendered = rendered,
            WrappedRendering = wrappedRendering,
        };

    /// <summary>
    /// Builds the result for the variable form, which issues no invocation at all.
    /// </summary>
    /// <param name="variableName">The variable the engine must resolve.</param>
    /// <returns>A variable-lookup result.</returns>
    public static MacroInvocationResult ForVariableLookup(string? variableName) =>
        new()
        {
            Outcome = MacroInvocationOutcome.VariableLookup,
            VariableName = variableName ?? string.Empty,
        };

    /// <summary>
    /// Builds the result for a refusal.
    /// </summary>
    /// <param name="error">The structured error carrying the legacy message and its return code.</param>
    /// <param name="invocationId">The invocation's identifier, when one was issued.</param>
    /// <param name="sequence">The invocation's issue number, when one was issued.</param>
    /// <param name="value">The rejected value, when the refusal was caused by one.</param>
    /// <param name="unhandled">Whether the client reported having no handler.</param>
    /// <returns>A rejected result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static MacroInvocationResult ForRejected(
        ExpressionParseError error,
        string? invocationId = null,
        long? sequence = null,
        object? value = null,
        bool unhandled = false)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new MacroInvocationResult
        {
            Outcome = MacroInvocationOutcome.Rejected,
            InvocationId = invocationId,
            Sequence = sequence,
            Value = value,
            Unhandled = unhandled,
            Error = error,
            ReturnCode = error.ReturnCode,
        };
    }

    /// <summary>
    /// Builds the result for a caller-cancelled wait - an outcome the network boundary creates.
    /// </summary>
    /// <param name="invocationId">The invocation that was abandoned.</param>
    /// <param name="sequence">Its issue number.</param>
    /// <returns>A cancelled result carrying <c>RetCode.CANCELLED</c>.</returns>
    public static MacroInvocationResult ForCancelled(string invocationId, long sequence) =>
        new()
        {
            Outcome = MacroInvocationOutcome.Cancelled,
            InvocationId = invocationId,
            Sequence = sequence,
            ReturnCode = RetCode.CANCELLED,
        };

    /// <summary>
    /// Builds the result for an invoker-imposed timeout - an outcome the network boundary creates.
    /// </summary>
    /// <param name="invocationId">The invocation that was abandoned.</param>
    /// <param name="sequence">Its issue number.</param>
    /// <returns>A timed-out result carrying <c>RetCode.E_TIME_OUT</c>.</returns>
    public static MacroInvocationResult ForTimedOut(string invocationId, long sequence) =>
        new()
        {
            Outcome = MacroInvocationOutcome.TimedOut,
            InvocationId = invocationId,
            Sequence = sequence,
            ReturnCode = RetCode.E_TIME_OUT,
        };
}


/// <summary>
/// The macro-invocation protocol: classifies a function-macro reference, validates it exactly as the
/// legacy parser does, routes the variable form away from the client, and - for a real macro - calls
/// back into the client across the inverted channel and converts the answer into an expression
/// fragment.
/// </summary>
/// <remarks>
/// <para>
/// PORTED FROM the macro half of <c>_of_PreprocessExp</c>
/// [<c>n_cst_dwsvc_columnexp.sru:L2226-L2321</c>] together with the built-in validation the scanner
/// performs on the way in [<c>:L1306-L1401</c>]. Those two regions are separated by nine hundred lines
/// in the oracle and belong to the same protocol, which is why they are reunited here.
/// </para>
/// <para>
/// THE STATIC HALF IS PURE AND THE INSTANCE HALF OWNS THE CHANNEL. Classification, validation,
/// argument shifting, class-name projection and rendering are static and side-effect free, so they are
/// testable as tables with nothing injected at all. Only <see cref="InvokeAsync"/> needs the channel,
/// the clock and the identifier source, and all three are constructor parameters so a test can make
/// the whole thing deterministic (AAP 0.6.7 names identifier generation and clock reads as the
/// determinism seams a characterization suite must substitute).
/// </para>
/// <para>
/// WHAT IS NOT HERE, RESTATED SO ITS ABSENCE READS AS A BOUNDARY. No expression scanning, no caret
/// arithmetic, no argument evaluation, no variable table, no dirty propagation, no compute-object
/// cache and no expression substitution: all of those are <c>ColumnExpressionEngine.cs</c>'s, and this
/// type is called by it rather than the other way round.
/// </para>
/// </remarks>
public sealed class MacroInvoker
{
    /// <summary>
    /// The abort sentinel: THE EMPTY STRING, which is how every failure in the legacy preprocessor
    /// reports itself.
    /// </summary>
    /// <remarks>
    /// <c>_of_PreprocessExp</c> returns a <c>string</c>, so it has no return code to give and every one
    /// of its refusals ends in <c>return ""</c>. Its callers read that value as failure -
    /// <c>if sExp = "" then return false</c> [<c>n_cst_dwsvc_columnexp.sru:L745</c>] - and the same guard
    /// is repeated on every nested call [<c>:L2183</c>, <c>:L2186</c>, <c>:L2201</c>, <c>:L2204</c>,
    /// <c>:L2247</c>, <c>:L2250</c>]. The constant exists so the sentinel is named at every site that
    /// tests it instead of appearing as a bare <c>""</c> that reads like an ordinary empty value.
    /// </remarks>
    public const string AbortSentinel = "";

    /// <summary>How many retired invocation identifiers are remembered for the late-answer diagnostic.</summary>
    private const int RetiredHistoryLimit = 64;

    private readonly IMacroInvocationChannel _channel;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string> _invocationIdFactory;
    private readonly Lock _gate = new();
    private readonly HashSet<string> _outstanding = new(StringComparer.Ordinal);
    private readonly Queue<string> _retired = new();
    private long _sequence;

    /// <summary>
    /// Creates an invoker over one client-callback channel.
    /// </summary>
    /// <param name="channel">The inverted edge the macro request travels out on.</param>
    /// <param name="invocationTimeout">
    /// How long to wait for the client's answer before answering
    /// <see cref="MacroInvocationOutcome.TimedOut"/>, or <see langword="null"/> to wait only as long as
    /// the caller's <see cref="CancellationToken"/> allows. <see cref="Timeout.InfiniteTimeSpan"/> is
    /// accepted and means the same as <see langword="null"/>.
    /// </param>
    /// <param name="timeProvider">
    /// The clock the timeout is measured on. Defaults to <see cref="TimeProvider.System"/>; a test
    /// supplies a fake so the timeout path is reachable without waiting (AAP 0.6.7).
    /// </param>
    /// <param name="invocationIdFactory">
    /// Produces correlation identifiers. Defaults to a GUID. INJECTABLE BECAUSE IT IS A DETERMINISM
    /// SEAM: AAP 0.6.7 names GUID generation among the values that must be masked from BOTH sides of a
    /// paired characterization recording, and an identifier that appears in a recording cannot be
    /// random if the recordings are to be comparable.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="channel"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="invocationTimeout"/> is zero or negative and is not
    /// <see cref="Timeout.InfiniteTimeSpan"/>. A zero timeout would abandon every invocation before the
    /// client could answer, which is a stalled channel dressed up as a configuration value.
    /// </exception>
    public MacroInvoker(
        IMacroInvocationChannel channel,
        TimeSpan? invocationTimeout = null,
        TimeProvider? timeProvider = null,
        Func<string>? invocationIdFactory = null)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (invocationTimeout is { } timeout
            && timeout != Timeout.InfiniteTimeSpan
            && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(invocationTimeout),
                timeout,
                "A macro invocation timeout must be positive, or Timeout.InfiniteTimeSpan to wait "
                    + "indefinitely. The macro's value is required before the calculation can "
                    + "continue, so abandoning every invocation immediately would stall every "
                    + "expression that contains one.");
        }

        _channel = channel;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _invocationIdFactory = invocationIdFactory ?? DefaultInvocationId;
        InvocationTimeout = invocationTimeout == Timeout.InfiniteTimeSpan ? null : invocationTimeout;
    }

    /// <summary>The inverted edge this invoker calls out on.</summary>
    public IMacroInvocationChannel Channel => _channel;

    /// <summary>
    /// How long the invoker waits for an answer, or <see langword="null"/> when it imposes no limit of
    /// its own.
    /// </summary>
    public TimeSpan? InvocationTimeout { get; }

    /// <summary>How many invocations this invoker has issued - the current value of the sequence.</summary>
    public long IssuedInvocationCount => Interlocked.Read(ref _sequence);

    /// <summary>
    /// How many invocations are awaiting an answer. Zero between calculations; greater than one only
    /// while a nested macro argument is being resolved [<c>n_cst_dwsvc_columnexp.sru:L2216-L2220</c>].
    /// </summary>
    public int OutstandingCount
    {
        get
        {
            lock (_gate)
            {
                return _outstanding.Count;
            }
        }
    }

    /// <summary>
    /// The twelve <c>ClassName</c> tokens the legacy conversion switch accepts, in source order and
    /// INCLUDING ITS MISSPELLING.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read off <c>n_cst_dwsvc_columnexp.sru:L2265-L2280</c> and again, identically, off
    /// <c>:L2289-L2304</c>. Anything not in this list reaches the legacy's <c>case else</c> and is
    /// refused as an invalid return value.
    /// </para>
    /// <para>
    /// A DEFECT PRESERVED AT ITS POINT OF REPRODUCTION (C-B): <c>"usignedinteger"</c> IS MISSPELLED IN
    /// THE ORACLE - the <c>n</c> of "unsigned" is missing - and it is misspelled IDENTICALLY at both
    /// sites, so it is one mistake copied rather than two independent slips. The consequence is
    /// observable: PowerBuilder reports a genuine <c>unsignedinteger</c> as <c>"unsignedinteger"</c>,
    /// which matches no label, so such a return is REFUSED even though every other integral type is
    /// accepted. Correcting the spelling would make a previously failing macro start working, which is
    /// a behaviour change dressed up as a typo fix, so the misspelling is carried and the correct
    /// spelling is deliberately absent.
    /// </para>
    /// </remarks>
    public static ImmutableArray<string> AcceptedReturnClassNames { get; } =
    [
        "string",
        "long",
        "integer",
        "unsignedlong",
        "usignedinteger",
        "double",
        "real",
        "decimal",
        "boolean",
        "datetime",
        "date",
        "time",
    ];

    /// <summary>
    /// The two markers the DataWindow evaluator answers when a sub-expression cannot be evaluated.
    /// </summary>
    /// <remarks>
    /// Tested as <c>if sVal = "!" or sVal = "?"</c> against each evaluated macro ARGUMENT
    /// [<c>n_cst_dwsvc_columnexp.sru:L2231</c>] and against a whole column expression [<c>:L761</c>].
    /// Exposed here because the argument test sits inside the macro dispatch loop, so a caller
    /// preparing an argument list needs it; the evaluation itself is not this file's.
    /// </remarks>
    public static ImmutableArray<string> EvaluationFailureMarkers { get; } = ["!", "?"];

    /// <summary>
    /// Tests a preprocessing result against the abort sentinel.
    /// </summary>
    /// <param name="preprocessed">The value a preprocessing step produced.</param>
    /// <returns><see langword="true"/> when the calculation must abort.</returns>
    /// <remarks>
    /// <see langword="null"/> IS TREATED AS ABORT, AND THE REASON IS THAT IT HAS NO LEGACY COUNTERPART.
    /// <c>_of_PreprocessExp</c> is declared to return a <c>string</c> and always does, so the oracle
    /// never produces a null here; a null in the port is a port-side absence, and admitting it as a
    /// legitimate expression would let a missing value be substituted into a calculation. Aborting is
    /// the conservative reading and matches what the empty string - the value a failure actually
    /// produces - already means.
    /// </remarks>
    public static bool IsAbort(string? preprocessed) => string.IsNullOrEmpty(preprocessed);

    /// <summary>
    /// Tests one evaluated macro argument for the evaluator's failure markers - the port of
    /// <c>if sVal = "!" or sVal = "?"</c> [<c>n_cst_dwsvc_columnexp.sru:L2231</c>].
    /// </summary>
    /// <param name="evaluated">The evaluated argument text.</param>
    /// <returns>
    /// <see langword="true"/> when the text is exactly <c>"!"</c> or exactly <c>"?"</c>.
    /// </returns>
    /// <remarks>
    /// EXACT EQUALITY, NOT A PREFIX OR SUBSTRING TEST. The oracle compares the whole value, so an
    /// argument that legitimately evaluates to <c>"?!"</c> or to <c>"what?"</c> is not a failure. The
    /// caller that finds a failure reports it through
    /// <c>ExpressionErrorSite.PreprocessFunctionArgumentEvaluationFailed</c> [<c>:L2232</c>] and aborts.
    /// </remarks>
    public static bool IsEvaluationFailure(string? evaluated) => evaluated is "!" or "?";

    /// <summary>
    /// Tests a <c>ClassName</c> token against the legacy conversion switch's labels.
    /// </summary>
    /// <param name="classNameToken">The token, as <see cref="ProjectClassName"/> produced it.</param>
    /// <returns>
    /// <see langword="true"/> when the token matches one of
    /// <see cref="AcceptedReturnClassNames"/>. <see langword="null"/> answers
    /// <see langword="false"/>, which is the arm that refuses a return value.
    /// </returns>
    public static bool IsAcceptedClassName(string? classNameToken) =>
        classNameToken is not null
        && AcceptedReturnClassNames.Contains(classNameToken, StringComparer.Ordinal);

    /// <summary>
    /// Wraps a rendered fragment in parentheses - the port of <c>sVal = "(" + sVal + ")"</c>
    /// [<c>n_cst_dwsvc_columnexp.sru:L2310</c>].
    /// </summary>
    /// <param name="rendered">The unwrapped fragment.</param>
    /// <returns>The fragment inside parentheses.</returns>
    /// <remarks>
    /// APPLIED OUTSIDE THE CONVERSION SWITCH IN THE ORACLE, to the result of BOTH forms, which is why
    /// it is a member of its own rather than folded into the rendering. It is load-bearing rather than
    /// cosmetic: it stops a returned expression from re-associating with the operators surrounding the
    /// macro call.
    /// </remarks>
    public static string WrapRendering(string? rendered) => "(" + (rendered ?? string.Empty) + ")";


    /// <summary>
    /// Projects a returned .NET value onto the PowerBuilder <c>ClassName</c> token the legacy switch
    /// would have seen - the port of <c>ClassName(aVal)</c>
    /// [<c>n_cst_dwsvc_columnexp.sru:L2264</c>, <c>:L2288</c>].
    /// </summary>
    /// <param name="value">The value the client's handler returned.</param>
    /// <returns>
    /// The token, or <see langword="null"/> when the value has no PowerBuilder counterpart at all.
    /// Either way, <see cref="IsAcceptedClassName"/> decides whether the switch accepts it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE PROJECTION IS SEPARATE FROM THE ACCEPTANCE TEST ON PURPOSE, and that separation is what
    /// reproduces the misspelling defect as DATA rather than as a special case. A <see cref="ushort"/>
    /// is PowerBuilder's <c>unsignedinteger</c>, so this member answers <c>"unsignedinteger"</c> -
    /// spelled correctly, because that is what PowerBuilder's own <c>ClassName</c> answers - and
    /// <see cref="AcceptedReturnClassNames"/> does not contain that spelling, so the value is refused.
    /// The refusal therefore EMERGES from the same mismatch that causes it in the oracle instead of
    /// being hardcoded here, and the day someone corrects the list the port's behaviour changes in step
    /// with the oracle's.
    /// </para>
    /// <para>
    /// The width mapping follows AAP 0.4.5.2: PowerBuilder's <c>long</c> is 32-bit and its
    /// <c>integer</c> 16-bit, so <see cref="int"/> and <see cref="short"/> map onto them exactly, while
    /// the AAP's own table maps <c>long</c>/<c>unsignedlong</c> onto <see cref="long"/>/<see cref="ulong"/>
    /// and those are honoured too. Every integral width therefore reaches an accepted token except the
    /// one the misspelling excludes.
    /// </para>
    /// <para>
    /// FOUR TOKENS ARE PROJECTED THAT THE SWITCH REFUSES - <c>unsignedinteger</c>, <c>blob</c>,
    /// <c>byte</c> and <c>char</c>. They are real PowerBuilder types a handler could plausibly return,
    /// and naming them makes the refusal diagnosable: the structured error can say which type was
    /// refused rather than only that something was.
    /// </para>
    /// </remarks>
    public static string? ProjectClassName(object? value) => value switch
    {
        // A null carries no type at all in C#. See MacroInvocationResponse.Value for the documented
        // narrowing this creates against PowerScript's typed null.
        null => null,
        string => "string",
        short => "integer",
        int => "long",
        long => "long",
        // Correctly spelled, and therefore REFUSED by the oracle's misspelled label. This is the
        // preserved defect [:L2267, :L2291].
        ushort => "unsignedinteger",
        uint => "unsignedlong",
        ulong => "unsignedlong",
        double => "double",
        float => "real",
        decimal => "decimal",
        bool => "boolean",
        DateTime => "datetime",
        DateOnly => "date",
        TimeOnly => "time",
        byte[] => "blob",
        byte => "byte",
        char => "char",
        _ => null,
    };

    /// <summary>
    /// Converts a returned value into the expression fragment the legacy substitutes - the port of the
    /// conversion switch at <c>n_cst_dwsvc_columnexp.sru:L2264-L2284</c> and, identically,
    /// <c>:L2288-L2308</c>.
    /// </summary>
    /// <param name="value">The value the client's handler returned.</param>
    /// <param name="rendered">
    /// The fragment, UNWRAPPED - see <see cref="WrapRendering"/> for the parenthesis the dispatcher adds
    /// afterwards. The empty string when the value is refused.
    /// </param>
    /// <param name="classNameToken">
    /// The token the value projected onto, whether accepted or not, so a refusal can name the type.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value matched an arm; <see langword="false"/> when it reached the
    /// legacy's <c>case else</c> and must be refused as an invalid return value.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE SIX ARMS, VERBATIM. A string is quoted with SINGLE quotes and NOTHING IS ESCAPED, because the
    /// oracle escapes nothing [<c>:L2266</c>] - a returned value containing an apostrophe produces a
    /// malformed fragment there and produces the same malformed fragment here, which is a preserved
    /// defect and not an oversight. A number renders bare [<c>:L2268</c>]. A boolean renders as the
    /// PREDICATE <c>1=1</c> or <c>1=0</c> [<c>:L2270-L2274</c>], because a DataWindow expression has no
    /// boolean literal to render into. The three temporal types render as calls to the expression
    /// language's own constructors [<c>:L2276-L2280</c>].
    /// </para>
    /// <para>
    /// CULTURE, STATED RATHER THAN LEFT TO THE HOST. PowerBuilder's parameterless <c>String</c> formats
    /// against the machine's regional settings, which a Linux container cannot reproduce and which the
    /// oracle never publishes as behaviour. Every conversion here is invariant, and the temporal
    /// conversions use the canonical forms the expression language's own
    /// <c>DateTime</c>/<c>Date</c>/<c>Time</c> constructors parse - so the VALUE round-trips exactly,
    /// and the rendered TEXT is pinned by this port instead of by the deployment's locale. That is
    /// determinism the containerised target requires, which is also what makes the fragment comparable
    /// between a legacy recording and a target recording at all.
    /// </para>
    /// <para>
    /// AND THE TEMPORAL TEXT IS SINGLE-SOURCED, NOT RESTATED - DECISION 5 APPLIED TO THE FORMAT. This
    /// method carries no temporal format string of its own. It calls
    /// <see cref="DateTimeValidator.FormatExpressionValue(DateTime)"/>,
    /// <see cref="DateValidator.FormatValue(DateOnly)"/> and
    /// <see cref="TimeValidator.Format(TimeOnly)"/> - the same three culture-pinned formatters
    /// <c>Validators/ValueToExpression.cs</c> composes its <c>DateTime('...')</c>,
    /// <c>Date('...')</c> and <c>Time('...')</c> literals around. Both paths render the same legacy
    /// <c>String(aVal)</c> conversion into the same expression language, so a macro result and a
    /// validated cell value MUST produce identical literal text; two independent format strings would
    /// let them drift, and the drift would be invisible until a recording compared them.
    /// </para>
    /// <para>
    /// CONCRETELY, THAT IS WHOLE SECONDS. Those formatters emit <c>yyyy-MM-dd HH:mm:ss</c>,
    /// <c>yyyy-MM-dd</c> and <c>HH:mm:ss</c>. This method previously emitted six fractional digits for
    /// <c>datetime</c> and <c>time</c>, which rendered <c>.000000</c> onto every whole-second value and
    /// disagreed with the validator path on a value both can carry. Fractional precision is an
    /// UNVERIFIED-FROM-REPOSITORY characterization item, recorded once on
    /// <see cref="DateTimeValidator.ExpressionValueFormat"/> and
    /// <see cref="TimeValidator.CanonicalFormat"/> rather than duplicated here: nothing in the tree
    /// shows a formatted temporal literal, so the precision must be pinned against the behavioural
    /// oracle. When it is, changing it on those constants changes this path with it - which is the
    /// point of routing through them.
    /// </para>
    /// </remarks>
    public static bool TryRender(object? value, out string rendered, out string? classNameToken)
    {
        classNameToken = ProjectClassName(value);

        if (!IsAcceptedClassName(classNameToken))
        {
            rendered = string.Empty;

            return false;
        }

        switch (classNameToken)
        {
            case "string":
                // [:L2266] No escaping, exactly as the oracle does none.
                rendered = "'" + (string)value! + "'";

                return true;

            case "long":
            case "integer":
            case "unsignedlong":
            // Unreachable BY CONSTRUCTION, and that is the defect: nothing projects onto the
            // misspelling, so no value can ever take this arm. It is present because the oracle's
            // label list contains it, and removing it would make the port's switch disagree with the
            // switch it reproduces [:L2267, :L2291].
            case "usignedinteger":
            case "double":
            case "real":
            case "decimal":
                // [:L2268] String(aVal), bare.
                rendered = FormatNumber(value!);

                return true;

            case "boolean":
                // [:L2270-L2274] A predicate, because the expression language has no boolean literal.
                rendered = (bool)value! ? "1=1" : "1=0";

                return true;

            case "datetime":
                // [:L2276] The inner text comes from the canonical formatter, NOT from a format
                // string of this file's own - see the CULTURE paragraph in the remarks.
                rendered = "DateTime('"
                    + DateTimeValidator.FormatExpressionValue((DateTime)value!)
                    + "')";

                return true;

            case "date":
                // [:L2278]
                rendered = "Date('"
                    + DateValidator.FormatValue((DateOnly)value!)
                    + "')";

                return true;

            case "time":
                // [:L2280]
                rendered = "Time('"
                    + TimeValidator.Format((TimeOnly)value!)
                    + "')";

                return true;

            default:
                // Unreachable: IsAcceptedClassName has already restricted the token to the twelve
                // labels above. Present so the switch is total and a future thirteenth label cannot
                // silently fall through as an accepted-but-unrendered value.
                rendered = string.Empty;

                return false;
        }
    }

    /// <summary>
    /// Reconstructs the sigil bits for a parsed reference at DISPATCH time, when
    /// <c>funcdata</c> no longer carries them.
    /// </summary>
    /// <param name="reference">The parsed function reference.</param>
    /// <returns>The three sigil bits the scanner had when it recorded this reference.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reference"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// ALL THREE BITS ARE RECOVERABLE, AND NONE IS GUESSED.
    /// <c>IsCtx</c> is <see langword="false"/> BY CONSTRUCTION: the scanner refuses a context function
    /// macro outright [<c>n_cst_dwsvc_columnexp.sru:L1306-L1310</c>], so a <c>funcdata</c> entry with the
    /// context bit set cannot exist to be dispatched.
    /// <c>IsMacro</c> is <see langword="true"/> because a <c>funcdata</c> entry is only ever created
    /// inside the macro-flag arm of the scanner [<c>:L1284-L1291</c>].
    /// <c>IsDynamic</c> is exactly <c>funcdata.builtin</c>, because that field IS <c>bDD</c>, the
    /// doubled-sigil flag [<c>:L1290</c> assigned at <c>:L1311</c>].
    /// </remarks>
    public static ExpansionSigils SigilsFor(FunctionReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return new ExpansionSigils(IsCtx: false, IsMacro: true, IsDynamic: reference.Builtin);
    }

    /// <summary>
    /// Classifies a function-macro reference and applies the legacy parser's validation - the port of
    /// <c>n_cst_dwsvc_columnexp.sru:L1306-L1310</c> and <c>:L1386-L1401</c>.
    /// </summary>
    /// <param name="reference">The parsed reference, the port of <c>funcdata</c>.</param>
    /// <param name="sigils">The sigil bits the scanner recorded for it.</param>
    /// <param name="syntax">
    /// The expression the reference appears in, for the caret-bearing message. Pass the same
    /// trailer-extended text the scanner holds, because <paramref name="caretPosition"/> indexes it.
    /// </param>
    /// <param name="caretPosition">
    /// The one-based caret position the scanner computed under the macro-token-midpoint convention.
    /// </param>
    /// <param name="dispatch">The arm the reference selects, when it is accepted.</param>
    /// <param name="error">The structured refusal, when it is not.</param>
    /// <returns><see langword="true"/> when the reference is accepted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reference"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE ORDER OF THE CHECKS IS THE ORACLE'S ORDER, and it matters. The CONTEXT refusal comes first,
    /// before the built-in flag is even read [<c>:L1307</c> precedes <c>:L1311</c>], so
    /// <c>@$Func(...)</c> and <c>@$$Invoke(...)</c> are both refused with the SAME message whatever
    /// their name or arity would have been. Only then is <c>builtin</c> consulted, and only a built-in
    /// reference has its name validated at all - a single-sigil <c>$Func(...)</c> is never compared
    /// against the sentinels, which is why an application macro may legitimately be called
    /// <c>Invoke</c>.
    /// </para>
    /// <para>
    /// THE ARITY MESSAGES DIFFER IN WHICH FIELD THEY NAME, AND THE DIFFERENCE IS REAL. The
    /// <see cref="MacroSentinels.FUNC_VAR"/> arm formats <c>fnData.fullName</c> [<c>:L1390</c>] - the
    /// whole macro text including its sigils and argument list - while the
    /// <see cref="MacroSentinels.FUNC_INVOKE"/> arm formats <c>fnData.name</c> [<c>:L1395</c>], which for
    /// that arm is the literal word <c>Invoke</c>. The two messages are otherwise character for
    /// character identical, which is exactly why the distinction is easy to lose; it is preserved here
    /// by using two different error sites, so a test can assert which one was raised.
    /// </para>
    /// <para>
    /// THE UNDEFINED-NAME REFUSAL IS WHAT MAKES THE DOUBLED SIGIL A CLOSED SET [<c>:L1398-L1400</c>].
    /// Only <c>$$(...)</c> and <c>$$Invoke(...)</c> are legal dynamic function forms; anything else -
    /// <c>$$Round(...)</c>, <c>$$FormatPrice(...)</c> - is refused. This is not relaxed (C-B).
    /// </para>
    /// </remarks>
    public static bool TryClassify(
        FunctionReference reference,
        ExpansionSigils sigils,
        string? syntax,
        long caretPosition,
        out MacroFunctionDispatch dispatch,
        out ExpressionParseError? error)
    {
        ArgumentNullException.ThrowIfNull(reference);

        dispatch = MacroFunctionDispatch.Application;
        error = null;

        // [:L1306-L1310] A context function macro is refused before the built-in flag is read.
        if (sigils.IsCtx)
        {
            error = ParseErrorFormatter.CreateParseError(
                ExpressionErrorSite.FunctionMacroContextReferenceUnsupported,
                syntax,
                caretPosition);

            return false;
        }

        // [:L2286-L2287] Not a doubled sigil, so the application owns the implementation and the name
        // is never validated against anything.
        if (!reference.Builtin)
        {
            dispatch = MacroFunctionDispatch.Application;

            return true;
        }

        // [:L1388-L1392] FUNC_VAR: EXACTLY one argument, and the message names fullName.
        if (MacroSentinels.IsFuncVar(reference.Name))
        {
            if (reference.Args.Length != 1)
            {
                error = ParseErrorFormatter.CreateParseError(
                    ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByFullName,
                    syntax,
                    caretPosition,
                    reference.FullName);

                return false;
            }

            dispatch = MacroFunctionDispatch.VariableLookup;

            return true;
        }

        // [:L1393-L1397] FUNC_INVOKE: AT LEAST one argument, and the message names name.
        if (MacroSentinels.IsFuncInvoke(reference.Name))
        {
            if (reference.Args.Length < 1)
            {
                error = ParseErrorFormatter.CreateParseError(
                    ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByName,
                    syntax,
                    caretPosition,
                    reference.Name);

                return false;
            }

            dispatch = MacroFunctionDispatch.DynamicInvoke;

            return true;
        }

        // [:L1398-L1400] Any other doubled-sigil name is undefined.
        error = ParseErrorFormatter.CreateParseError(
            ExpressionErrorSite.FunctionMacroUndefined,
            syntax,
            caretPosition,
            reference.Name);

        return false;
    }


    /// <summary>
    /// Produces the dispatch plan for one function-macro reference whose arguments have already been
    /// evaluated - the port of the classification, argument shifting and routing at
    /// <c>n_cst_dwsvc_columnexp.sru:L2237-L2287</c>.
    /// </summary>
    /// <param name="reference">The parsed reference, the port of <c>funcdata</c>.</param>
    /// <param name="sigils">The sigil bits the scanner recorded, or <see cref="SigilsFor"/> at dispatch time.</param>
    /// <param name="evaluatedArguments">
    /// The evaluated arguments, ONE-BASED, in the same order and count as
    /// <c>FunctionReference.Args</c> - the port of <c>sArgs</c> as it stands after the evaluation loop
    /// at <c>:L2229-L2236</c>.
    /// </param>
    /// <param name="syntax">The expression the reference appears in, for a caret-bearing refusal.</param>
    /// <param name="caretPosition">The one-based caret position, computed by the scanner.</param>
    /// <returns>The plan: a refusal, a variable lookup, or a client callback with its name and arguments.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="reference"/> or <paramref name="evaluatedArguments"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="evaluatedArguments"/> does not hold exactly as many arguments as
    /// <paramref name="reference"/> declares.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE COUNT AGREEMENT IS ENFORCED BECAUSE IT IS A CALLER MISTAKE, NOT DATA. The oracle fills
    /// <c>sArgs</c> from <c>fns[nFnIdx].args</c> one for one [<c>:L2228-L2235</c>], so the two lengths
    /// cannot differ there; a difference here means the caller paired the wrong evaluated list with the
    /// wrong reference, and the parse-time arity rules that guarantee <c>args[1]</c> exists would then
    /// be guaranteeing it about the wrong list. Failing loudly is the fail-fast posture AAP 0.1.4
    /// requires be preserved.
    /// </para>
    /// <para>
    /// THE VARIABLE FORM NEVER BECOMES A CLIENT CALLBACK. <c>$$('name')</c> yields
    /// <see cref="MacroDispatchKind.VariableLookup"/> carrying <c>sArgs[1]</c> as the variable name
    /// [<c>:L2239-L2240</c>], and the engine resolves it against its own table. The oracle fires the
    /// application event on the other two arms only, and forwarding this one would invent a callback the
    /// application has no handler for.
    /// </para>
    /// </remarks>
    public static MacroDispatchPlan Plan(
        FunctionReference reference,
        ExpansionSigils sigils,
        MacroArgumentList evaluatedArguments,
        string? syntax,
        long caretPosition)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(evaluatedArguments);

        if (evaluatedArguments.UpperBound != reference.Args.Length)
        {
            throw new ArgumentException(
                "The evaluated argument list holds "
                    + evaluatedArguments.UpperBound.ToString(CultureInfo.InvariantCulture)
                    + " argument(s) but the reference declares "
                    + reference.Args.Length.ToString(CultureInfo.InvariantCulture)
                    + ". The oracle fills sArgs from fns[nFnIdx].args one for one "
                    + "[n_cst_dwsvc_columnexp.sru:L2228-L2235], so the two counts always agree and a "
                    + "mismatch means the wrong evaluated list was paired with this reference.",
                nameof(evaluatedArguments));
        }

        if (!TryClassify(
                reference,
                sigils,
                syntax,
                caretPosition,
                out MacroFunctionDispatch dispatch,
                out ExpressionParseError? error))
        {
            return MacroDispatchPlan.ForRejection(error!);
        }

        ExpansionModeProjection projection = sigils.ProjectFunctionReference(dispatch);

        if (projection.IsRejected)
        {
            // Not reachable through TryClassify, which already refuses the context sigil, and kept as a
            // real refusal rather than an assertion so that the expansion-mode model stays the single
            // authority on which sigil combinations are legal. If it ever adds a rejection, this path
            // reports it with the message the oracle uses for that rejection instead of throwing.
            return MacroDispatchPlan.ForRejection(
                ParseErrorFormatter.CreateParseError(
                    ExpressionErrorSite.FunctionMacroContextReferenceUnsupported,
                    syntax,
                    caretPosition));
        }

        switch (dispatch)
        {
            case MacroFunctionDispatch.Application:
                // [:L2287] The whole evaluated list travels, and the name is the reference's own.
                return MacroDispatchPlan.ForDirect(
                    reference.Name,
                    evaluatedArguments,
                    projection.Mode);

            case MacroFunctionDispatch.VariableLookup:
                // [:L2240] sArgs[1] is a VARIABLE NAME. Arity is exactly one, so there is nothing else.
                return MacroDispatchPlan.ForVariableLookup(evaluatedArguments[1], projection.Mode);

            case MacroFunctionDispatch.DynamicInvoke:
                // [:L2259-L2263] sArgs[1] becomes the callee NAME and the remainder is renumbered.
                return MacroDispatchPlan.ForDynamic(
                    evaluatedArguments[1],
                    evaluatedArguments.DropFirst(),
                    projection.Mode);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(reference),
                    dispatch,
                    "A function macro reference classifies as Application, VariableLookup or "
                        + "DynamicInvoke. Those are the only three arms the oracle's dispatch switch "
                        + "has [n_cst_dwsvc_columnexp.sru:L2237-L2308].");
        }
    }

    /// <summary>
    /// Produces the dispatch plan for a reference at DISPATCH time, reconstructing its sigils with
    /// <see cref="SigilsFor"/>.
    /// </summary>
    /// <param name="reference">The parsed reference.</param>
    /// <param name="evaluatedArguments">The evaluated arguments, one-based.</param>
    /// <param name="syntax">The expression the reference appears in, for a caret-bearing refusal.</param>
    /// <param name="caretPosition">The one-based caret position.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="reference"/> or <paramref name="evaluatedArguments"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The overload the dispatcher uses, because by then the scanner's sigil bits are gone and only
    /// <c>funcdata</c> remains. The parse-time overload takes the sigils explicitly, because at parse
    /// time the context bit is still live and is the first thing checked.
    /// </remarks>
    public static MacroDispatchPlan Plan(
        FunctionReference reference,
        MacroArgumentList evaluatedArguments,
        string? syntax = null,
        long caretPosition = 0) =>
        Plan(reference, SigilsFor(reference), evaluatedArguments, syntax, caretPosition);

    /// <summary>
    /// Carries out one dispatch plan: refuses, resolves a variable, or calls back into the client across
    /// the inverted channel and converts the answer into an expression fragment.
    /// </summary>
    /// <param name="row">The ONE-BASED row being calculated - the legacy <c>row</c> argument.</param>
    /// <param name="dwo">The column the expression is bound to - the legacy <c>dwo</c> argument.</param>
    /// <param name="plan">The plan produced by <see cref="Plan(FunctionReference, MacroArgumentList, string?, long)"/>.</param>
    /// <param name="sessionId">The expression session, when there is one.</param>
    /// <param name="dataWindowHandle">The session-scoped DataWindow handle, when there is one.</param>
    /// <param name="cancellationToken">Cancels the wait for the client's answer.</param>
    /// <returns>
    /// The outcome. <see cref="MacroInvocationResult.Aborts"/> tells the caller whether to propagate the
    /// empty-string sentinel.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="dwo"/> or <paramref name="plan"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="MacroProtocolViolationException">
    /// The channel answered nothing, answered a different invocation, or echoed a different sequence
    /// number. Under ordering pattern (b) that is a hard error and never a reorder opportunity.
    /// </exception>
    /// <remarks>
    /// <para>
    /// NO CHANNEL CALL IS MADE unless the plan is a client callback. A refusal and a variable lookup both
    /// answer immediately, which is what makes the routing assertion possible: a test can hand the
    /// invoker a channel that fails if it is ever touched and prove that <c>$$('name')</c> never reaches
    /// it.
    /// </para>
    /// <para>
    /// EXCEPTIONS THAT ARE NOT CANCELLATION ARE ALLOWED TO PROPAGATE. A transport fault has no legacy
    /// counterpart - an in-process event trigger cannot fail in transit - so there is no behaviour to
    /// preserve, and the only alternatives would be to retry here, which belongs to the channel's own
    /// resilience policy, or to substitute a value, which would put a fabricated number into a
    /// calculation. Failing loudly is the fail-fast posture AAP 0.1.4 requires be preserved as
    /// fail-fast rather than softened into graceful degradation.
    /// </para>
    /// </remarks>
    public async ValueTask<MacroInvocationResult> InvokeAsync(
        long row,
        IDataWindowObject dwo,
        MacroDispatchPlan plan,
        string? sessionId = null,
        string? dataWindowHandle = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dwo);
        ArgumentNullException.ThrowIfNull(plan);

        switch (plan.Kind)
        {
            case MacroDispatchKind.Rejected:
                // The parser already refused this reference; the channel is not involved at all.
                return MacroInvocationResult.ForRejected(plan.Error!);

            case MacroDispatchKind.VariableLookup:
                // [:L2239-L2257] A variable, resolved by the engine. THE CHANNEL IS NOT TOUCHED.
                return MacroInvocationResult.ForVariableLookup(plan.VariableName);

            case MacroDispatchKind.ClientCallback:
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(plan),
                    plan.Kind,
                    "A dispatch plan is a rejection, a variable lookup or a client callback.");
        }

        string invocationId = _invocationIdFactory() ?? DefaultInvocationId();
        long sequence = Interlocked.Increment(ref _sequence);

        MacroInvocation invocation = new()
        {
            InvocationId = invocationId,
            Sequence = sequence,
            Row = row,
            Dwo = dwo,
            Name = plan.Name,
            Arguments = plan.Arguments,
            Form = plan.Form,
            Builtin = plan.Builtin,
            SessionId = sessionId,
            DataWindowHandle = dataWindowHandle,
        };

        RegisterInvocation(invocationId);

        CancellationTokenSource? timeoutSource = null;
        CancellationTokenSource? linkedSource = null;

        try
        {
            CancellationToken effectiveToken = cancellationToken;

            if (InvocationTimeout is { } timeout)
            {
                timeoutSource = new CancellationTokenSource(timeout, _timeProvider);
                linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutSource.Token);
                effectiveToken = linkedSource.Token;
            }

            MacroInvocationResponse response;

            try
            {
                response = await _channel
                    .InvokeAsync(invocation, effectiveToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The caller's intent wins when both fired: a deliberate cancellation is not a timeout.
                return cancellationToken.IsCancellationRequested
                    || timeoutSource is null
                    || !timeoutSource.IsCancellationRequested
                        ? MacroInvocationResult.ForCancelled(invocationId, sequence)
                        : MacroInvocationResult.ForTimedOut(invocationId, sequence);
            }

            ValidateCorrelation(invocation, response);

            if (response.Unhandled)
            {
                // No handler at all. The oracle's equivalent is a `choose case` that falls through, whose
                // value then matches no arm - so this reaches the very same refusal.
                return MacroInvocationResult.ForRejected(
                    BuildReturnValueError(plan, dwo),
                    invocationId,
                    sequence,
                    response.Value,
                    unhandled: true);
            }

            if (!TryRender(response.Value, out string rendered, out string? classNameToken))
            {
                // [:L2282] for the dynamic form, [:L2306] for the direct one.
                return MacroInvocationResult.ForRejected(
                    BuildReturnValueError(plan, dwo),
                    invocationId,
                    sequence,
                    response.Value);
            }

            return MacroInvocationResult.ForRendered(
                invocationId,
                sequence,
                response.Value,
                classNameToken!,
                rendered,
                WrapRendering(rendered));
        }
        finally
        {
            RetireInvocation(invocationId);
            linkedSource?.Dispose();
            timeoutSource?.Dispose();
        }
    }

    /// <summary>
    /// Builds the "invalid return value" refusal for whichever form was invoked.
    /// </summary>
    /// <param name="plan">The plan whose invocation was refused.</param>
    /// <param name="dwo">The column, whose name the message reports.</param>
    /// <returns>The structured error.</returns>
    /// <remarks>
    /// TWO SITES, ONE MESSAGE TEXT, DIFFERENT ARGUMENT PROVENANCE. Both read
    /// <c>"预处理表达式发生错误!~n[" + String(dwo.name) + "]表达式错误:~n函数[...],返回值无效"</c>, but the
    /// dynamic site names <c>sArgs[1]</c> [<c>:L2282</c>] and the direct site names
    /// <c>fns[nFnIdx].name</c> [<c>:L2306</c>]. Because <see cref="MacroDispatchPlan.Name"/> already
    /// holds the resolved callee name for the dynamic form and the reference's own name for the direct
    /// form, one argument serves both - the SITE is what records which line was reached, so a test can
    /// still tell them apart.
    /// <para>
    /// THE MESSAGE IS HARDCODED CHINESE AND IS NOT LOCALIZED, and that is preserved rather than
    /// harmonised (C-B). The equivalent messages elsewhere in the DataWindow service layer DO route
    /// through <c>I18N</c>; these do not, and the structured error records that with
    /// <c>Localized = false</c> and a zero localization category.
    /// </para>
    /// </remarks>
    private static ExpressionParseError BuildReturnValueError(
        MacroDispatchPlan plan,
        IDataWindowObject dwo)
    {
        ExpressionErrorSite site = plan.Dispatch == MacroFunctionDispatch.DynamicInvoke
            ? ExpressionErrorSite.PreprocessMacroReturnValueInvalid
            : ExpressionErrorSite.PreprocessFunctionReturnValueInvalid;

        return ParseErrorFormatter.CreatePlainError(site, dwo.Name, plan.Name);
    }

    /// <summary>
    /// Renders a numeric value the way the oracle's bare <c>String(aVal)</c> does [<c>:L2268</c>].
    /// </summary>
    /// <param name="value">The boxed numeric value, already known to be one of the accepted types.</param>
    /// <returns>The invariant-culture rendering.</returns>
    /// <remarks>
    /// <para>
    /// Invariant throughout, so the fragment does not vary with the deployment's locale - see
    /// <see cref="TryRender"/> for why that is required by the containerised target rather than a
    /// change of behaviour. Floating-point values render round-trippably, which is the choice that
    /// preserves the VALUE the DataWindow expression will re-parse.
    /// </para>
    /// <para>
    /// THERE IS DELIBERATELY NO <see cref="ushort"/> ARM, AND ITS ABSENCE MEASURES THE PRESERVED
    /// DEFECT'S REACH. A <see cref="ushort"/> is PowerBuilder's <c>unsignedinteger</c>, and the oracle's
    /// misspelled label means such a value is refused before any rendering is attempted, so this member
    /// can never be handed one. Writing an arm for it would be code no input can reach; leaving it out
    /// states that consequence where a reader will meet it. The trailing arm keeps the switch total, so
    /// were the label list ever corrected the value would still render correctly rather than throw.
    /// </para>
    /// </remarks>
    private static string FormatNumber(object value) => value switch
    {
        short v => v.ToString(CultureInfo.InvariantCulture),
        int v => v.ToString(CultureInfo.InvariantCulture),
        long v => v.ToString(CultureInfo.InvariantCulture),
        uint v => v.ToString(CultureInfo.InvariantCulture),
        ulong v => v.ToString(CultureInfo.InvariantCulture),
        double v => v.ToString(CultureInfo.InvariantCulture),
        float v => v.ToString(CultureInfo.InvariantCulture),
        decimal v => v.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    /// <summary>The default correlation identifier source.</summary>
    /// <returns>A fresh identifier.</returns>
    private static string DefaultInvocationId() => Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// Records an invocation as outstanding before it is sent.
    /// </summary>
    /// <param name="invocationId">The correlation identifier.</param>
    /// <exception cref="MacroProtocolViolationException">
    /// The identifier is already outstanding, which means the identifier source produced a duplicate.
    /// Two invocations sharing an identifier cannot be told apart, so their answers cannot be correlated
    /// at all.
    /// </exception>
    private void RegisterInvocation(string invocationId)
    {
        lock (_gate)
        {
            if (!_outstanding.Add(invocationId))
            {
                throw new MacroProtocolViolationException(
                    "Macro invocation identifier '" + invocationId + "' is already outstanding. "
                        + "Correlation identifiers must be unique while in flight, because one "
                        + "calculation may invoke several macros and a macro argument may invoke "
                        + "another [n_cst_dwsvc_columnexp.sru:L2216-L2220].",
                    invocationId,
                    invocationId,
                    late: false);
            }
        }
    }

    /// <summary>
    /// Retires an invocation once it has been answered, abandoned or refused.
    /// </summary>
    /// <param name="invocationId">The correlation identifier.</param>
    /// <remarks>
    /// The identifier is remembered in a bounded history so that a LATE answer - one for an invocation
    /// that has already finished - is reported as late rather than as unrecognised. The bound keeps the
    /// history from growing without limit over a long-lived session; an answer older than the bound is
    /// still refused, just with the less specific diagnosis.
    /// </remarks>
    private void RetireInvocation(string invocationId)
    {
        lock (_gate)
        {
            _outstanding.Remove(invocationId);
            _retired.Enqueue(invocationId);

            while (_retired.Count > RetiredHistoryLimit)
            {
                _retired.Dequeue();
            }
        }
    }

    /// <summary>
    /// Checks whether an identifier belongs to an invocation that has already finished.
    /// </summary>
    /// <param name="invocationId">The identifier the channel answered with.</param>
    /// <returns><see langword="true"/> when the identifier is in the retired history.</returns>
    private bool IsRetired(string? invocationId)
    {
        if (invocationId is null)
        {
            return false;
        }

        lock (_gate)
        {
            return _retired.Contains(invocationId, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Enforces the synchronous, no-reordering discipline on one answer.
    /// </summary>
    /// <param name="invocation">The invocation that was sent.</param>
    /// <param name="response">
    /// The answer the channel produced. Typed as nullable even though
    /// <see cref="IMacroInvocationChannel.InvokeAsync"/> promises a value, because the promise is a
    /// compile-time one and a badly behaved implementation can still answer <see langword="null"/> at
    /// run time - and a null answer must be refused rather than dereferenced.
    /// </param>
    /// <exception cref="MacroProtocolViolationException">
    /// The answer is absent, correlates to a different invocation, or echoes a different sequence
    /// number.
    /// </exception>
    /// <remarks>
    /// THREE CHECKS, ALL OF THEM HARD. AAP 0.6.1.4 assigns macro invocation ordering pattern (b), so an
    /// answer that does not correlate is a fault and not something to buffer: substituting a value
    /// computed for one macro into another's expression is silent data corruption, and both expressions
    /// would still produce a plausible result. The sequence check is skipped when the channel does not
    /// echo the number, because echoing it is optional; when it does echo one, it must be the right one.
    /// </remarks>
    private void ValidateCorrelation(MacroInvocation invocation, MacroInvocationResponse? response)
    {
        if (response is null)
        {
            throw new MacroProtocolViolationException(
                "The macro invocation channel answered nothing for invocation '"
                    + invocation.InvocationId + "'. There is no fallback macro implementation to fall "
                    + "back to, and substituting a default would put a fabricated value into the "
                    + "calculation.",
                invocation.InvocationId,
                invocationId: null,
                late: false);
        }

        if (!string.Equals(response.InvocationId, invocation.InvocationId, StringComparison.Ordinal))
        {
            bool late = IsRetired(response.InvocationId);

            throw new MacroProtocolViolationException(
                (late
                    ? "The macro invocation channel answered LATE: '"
                    : "The macro invocation channel answered out of order: '")
                    + response.InvocationId + "' arrived while '" + invocation.InvocationId
                    + "' was outstanding. Macro invocation is ordering pattern (b), strictly "
                    + "synchronous with no reordering permitted, because the calculation cannot "
                    + "proceed without the returned value.",
                invocation.InvocationId,
                response.InvocationId,
                late);
        }

        if (response.Sequence is { } echoed && echoed != invocation.Sequence)
        {
            throw new MacroProtocolViolationException(
                "The macro invocation channel echoed sequence "
                    + echoed.ToString(CultureInfo.InvariantCulture) + " for invocation '"
                    + invocation.InvocationId + "', which was issued as sequence "
                    + invocation.Sequence.ToString(CultureInfo.InvariantCulture)
                    + ". The sequencing token is for DETECTION only; a mismatch is a hard error.",
                invocation.InvocationId,
                response.InvocationId,
                late: false);
        }
    }
}
