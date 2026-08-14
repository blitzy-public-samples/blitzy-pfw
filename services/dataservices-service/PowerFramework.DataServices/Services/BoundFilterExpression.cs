// =====================================================================================================
//  BoundFilterExpression - A DATAWINDOW FILTER IN TWO FORMS: THE ONE THE ORACLE RENDERS AND THE ONE
//                          THAT IS EXECUTED
//  ---------------------------------------------------------------------------------------------------
//  ORACLE  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L313-L344
//          (of_getfilter - five unescaped splice sites)
//          ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L389-L390
//          (child.SetFilter / child.Filter - the execution)
//
//  WHY THIS TYPE EXISTS, STATED ONCE SO EVERY CONSUMER CAN READ IT HERE
//  -------------------------------------------------------------------
//  The legacy drop-down search builds its filter by CONCATENATION and interpolates the user's typed text
//  into it with no escaping of any kind [:L319, :L323, :L331, :L334]. AAP 0.6.4 names :L323 explicitly as
//  a filter-injection site and requires the DEFECT BE DOCUMENTED RATHER THAN SILENTLY CHANGED, and
//  constraint C-B forbids correcting it - so the rendered text this type carries as ObservableText is the
//  oracle's, defects and all, down to the dangling " OR " that a display-bit-clear filter type begins with.
//
//  AND THAT RENDERED TEXT MUST NOT BE WHAT IS EXECUTED. A search term carrying a quote, a parenthesis or
//  the word OR does not merely break the expression - it CHANGES ITS STRUCTURE, which is CWE-94 on an
//  authenticated but not necessarily trusted input. The two obligations are only in tension if ONE string
//  has to serve both purposes, so it does not: this type carries both, and each consumer takes the one it
//  needs.
//
//      ParameterizedText   - the expression with every caller-derived value replaced by a placeholder.
//                            Structurally fixed: no input can add a clause, close a quote or introduce an
//                            operator, because no input reaches the text at all. THIS IS WHAT EXECUTES.
//      Parameters          - the values the placeholders stand for, each carrying BOTH its typed value
//                            and the exact fragment the oracle spliced for it.
//      ObservableText      - DERIVED from the two above rather than composed separately, which is what
//                            makes it byte-exact against the oracle BY CONSTRUCTION. Reported on the wire
//                            as DropDownSearchState.filter_expression, which dataservices.v1.proto
//                            documents as "compatibility and diagnostic data", and recorded by the
//                            characterization suite. NEVER EXECUTED while Bindable is true.
//
//  ONE COMPOSITION, TWO PROJECTIONS - AND THAT IS THE WHOLE DESIGN. Composing the two texts independently
//  would let them drift, and a drift between "what we executed" and "what we reported" is the worst
//  possible defect here because each half looks correct on its own. Deriving one from the other makes
//  drift unrepresentable.
//
//  THIS IS THE SAME TWO-FORM DEVICE Persistence USES FOR SQL, DELIBERATELY. SqlBoundStatement carries an
//  observable statement and a parameterized statement side by side for exactly this reason, and AAP 0.7.2
//  states the rule generally: parameterized execution even where the legacy interpolates, WITH THE
//  OBSERVABLE STATEMENT PRESERVED. One shape for both problems means a reader who has understood one has
//  understood the other.
//
//  NOTE WHAT AAP 0.7.2 DOES NOT REACH. These are DataWindow FILTER expressions, not SQL, so the
//  parameterised-SQL requirement does not apply here of its own force; the reasoning above is the filter
//  path's own. The distinction is worth carrying because a later reader who conflates the two would go
//  looking for a database that is not there.
//
//  BINDABILITY IS A FACT ABOUT THE EXPRESSION, NOT A PREFERENCE
//  ------------------------------------------------------------
//  One legacy path produces text that CANNOT be bound: of_getfilter raises
//  OnDDSGetFilter(row, dwo, data, ref sFilter) unconditionally at [:L342] with a `ref string`
//  out-parameter, and an application is entitled to REWRITE the filter through it. What comes back is
//  arbitrary syntax the application composed deliberately, with no record of which of its characters came
//  from where - so no placeholder form of it can be derived. Such an expression is marked NOT BINDABLE and
//  its text is what executes, because refusing it would break a documented extension point.
//
//  That is a narrowing and it is recorded rather than hidden, in the same spirit as AAP 0.6.2.3's
//  cross-session foreign-variable narrowing: the boundary is named at the point it takes effect. The
//  narrowing does NOT reach the hazard this type exists for - a CALLER's search term is bound on every
//  path, and only an APPLICATION's own deliberate rewrite passes through as syntax.
// =====================================================================================================

using System.Globalization;

namespace PowerFramework.DataServices.Services;

/// <summary>
/// One caller-derived value carried as a bound literal rather than as expression syntax.
/// </summary>
/// <param name="Placeholder">
/// The token standing for the value in <see cref="BoundFilterExpression.ParameterizedText"/>.
/// </param>
/// <param name="Value">
/// The typed value. A <see cref="string"/> for the two <c>LIKE</c> clauses and the pinyin clause, and a
/// <see cref="double"/> for the numeric equality clause - which is the type the oracle's own
/// <c>IsNumber</c> guard admits [<c>n_cst_dwsvc_dropdownsearch.sru:L333</c>].
/// </param>
/// <param name="ObservableLiteral">
/// The exact fragment the oracle spliced into its expression for this value - <c>'%abc%'</c> WITH its
/// quotes for a textual clause, and the caller's raw digits UNQUOTED for the numeric one.
/// </param>
/// <remarks>
/// <para>
/// THE VALUE IS CARRIED AS ITS OWN TYPE AND IS NEVER ONLY PRE-RENDERED. Rendering it to text and keeping
/// nothing else would reintroduce the very quoting question binding exists to remove, and would lose the
/// numeric/textual distinction the oracle's type-directed data clause draws [<c>:L329-L336</c>].
/// </para>
/// <para>
/// <b>AND THE OBSERVABLE FRAGMENT IS CARRIED SEPARATELY BECAUSE IT IS NOT DERIVABLE FROM THE VALUE.</b>
/// The numeric clause splices the caller's RAW ARGUMENT [<c>:L334</c>], so an input of <c>32.50</c>
/// appears as <c>32.50</c> while its bound double would render as <c>32.5</c>, and an input of
/// <c>&#32;32&#32;</c> keeps its spaces. Re-deriving the fragment would therefore change the observable
/// expression, which constraint C-B forbids.
/// </para>
/// </remarks>
public sealed record BoundFilterLiteral(string Placeholder, object? Value, string ObservableLiteral);

/// <summary>
/// A DataWindow filter expression in both its executable and its rendered form.
/// </summary>
/// <param name="ParameterizedText">
/// The expression with every caller-derived value replaced by its placeholder. Executed.
/// </param>
/// <param name="Parameters">The bound values, in placeholder order.</param>
/// <param name="Bindable">
/// Whether this expression is CERTIFIED free of unbound caller-derived values. <see langword="false"/> for
/// text an application composed itself, whose provenance this code cannot vouch for - see this file's
/// header.
/// <para>
/// <b>IT IS A CERTIFICATION, NOT AN EXECUTION SWITCH.</b> An executor ALWAYS runs
/// <paramref name="ParameterizedText"/> with <paramref name="Parameters"/>, whatever this flag says: for an
/// uncertified expression the parameterized text simply IS the text, with no parameters beside it, so
/// executing it is executing the text. Reading this flag as "fall back to the rendered form" would
/// re-interpolate a conjunction's BOUND half - which is the hole this whole file exists to close, reopened
/// at the one place hardest to notice.
/// </para>
/// </param>
public sealed record BoundFilterExpression(
    string ParameterizedText,
    IReadOnlyList<BoundFilterLiteral> Parameters,
    bool Bindable)
{
    /// <summary>
    /// The placeholder prefix. No DataWindow filter operator or function name begins with a colon, and the
    /// remainder is deliberately unlike any column name a definition would carry.
    /// </summary>
    public const string PlaceholderPrefix = ":pfwArg";

    /// <summary>The empty filter, which is what CLEARS rather than what matches nothing.</summary>
    public static BoundFilterExpression Empty { get; } = new(string.Empty, [], true);

    /// <summary>
    /// The expression as the oracle renders it, with its interpolated literals and its preserved defects.
    /// </summary>
    /// <remarks>
    /// <b>DERIVED, NOT STORED, AND THAT IS THE POINT.</b> Substituting each literal's observable fragment
    /// back into the parameterized text reproduces the oracle's own string byte for byte, and does so BY
    /// CONSTRUCTION rather than by two compositions happening to agree. This is reported and recorded; it
    /// is executed only when <see cref="Bindable"/> is <see langword="false"/>, in which case the two forms
    /// are the same string anyway.
    /// </remarks>
    public string ObservableText
    {
        get
        {
            if (Parameters.Count == 0)
            {
                return ParameterizedText;
            }

            Dictionary<string, string> fragments = new(StringComparer.Ordinal);

            foreach (BoundFilterLiteral literal in Parameters)
            {
                fragments[literal.Placeholder] = literal.ObservableLiteral;
            }

            return Rewrite(
                ParameterizedText,
                placeholder => fragments.TryGetValue(placeholder, out string? fragment)
                    ? fragment

                    // AN UNKNOWN PLACEHOLDER IS LEFT STANDING RATHER THAN ERASED. It can only mean the text
                    // and the parameter list disagree, and leaving the token visible makes that obvious in
                    // the reported expression instead of silently deleting part of a predicate.
                    : placeholder);
        }
    }

    /// <summary>
    /// Rewrites every placeholder in a parameterized expression, in ONE left-to-right pass.
    /// </summary>
    /// <param name="text">The parameterized text.</param>
    /// <param name="replacement">Maps a placeholder token to what replaces it.</param>
    /// <returns>The rewritten text.</returns>
    /// <remarks>
    /// <para>
    /// <b>A SCAN RATHER THAN SUCCESSIVE <see cref="string.Replace(string, string, StringComparison)"/>
    /// CALLS, AND THE DIFFERENCE IS A DEFECT THIS CODE ALREADY HAD.</b> <c>:pfwArg1</c> is a PREFIX of
    /// <c>:pfwArg10</c>, so a loop of replacements corrupts its own output: replacing <c>:pfwArg1</c> after
    /// <c>:pfwArg10</c> has been written rewrites the first eight characters of that token and leaves a
    /// stray <c>0</c> standing in the expression. Ordering the loop by descending placeholder length fixes
    /// the SOURCE side of that hazard and NOT the OUTPUT side - a replacement value may itself contain a
    /// token a later iteration matches - so ordering is not a fix at all, merely a narrower failure.
    /// </para>
    /// <para>
    /// A single pass cannot re-enter its own output, so the whole class of hazard is gone rather than
    /// mitigated. The digit run is read GREEDILY, which is what makes <c>:pfwArg10</c> one token and not
    /// <c>:pfwArg1</c> followed by a literal zero.
    /// </para>
    /// </remarks>
    private static string Rewrite(string text, Func<string, string> replacement)
    {
        int at = text.IndexOf(PlaceholderPrefix, StringComparison.Ordinal);

        if (at < 0)
        {
            return text;
        }

        System.Text.StringBuilder rewritten = new(text.Length);
        int copied = 0;

        while (at >= 0)
        {
            int digits = at + PlaceholderPrefix.Length;

            while (digits < text.Length && char.IsAsciiDigit(text[digits]))
            {
                digits++;
            }

            if (digits == at + PlaceholderPrefix.Length)
            {
                // The prefix with no digit after it is not a placeholder. Copied through and stepped past,
                // so a scan cannot loop on it.
                at = text.IndexOf(PlaceholderPrefix, digits, StringComparison.Ordinal);

                continue;
            }

            _ = rewritten.Append(text, copied, at - copied);
            _ = rewritten.Append(replacement(text[at..digits]));
            copied = digits;

            at = digits >= text.Length
                ? -1
                : text.IndexOf(PlaceholderPrefix, digits, StringComparison.Ordinal);
        }

        _ = rewritten.Append(text, copied, text.Length - copied);

        return rewritten.ToString();
    }

    /// <summary>Whether the expression carries no text at all.</summary>
    public bool IsEmpty => ParameterizedText.Length == 0;

    /// <summary>
    /// Wraps text whose provenance is unknown, so it can travel the same path WITHOUT claiming to be bound.
    /// </summary>
    /// <param name="text">The expression text.</param>
    /// <returns>The unbindable expression.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <b>THE HONEST ANSWER FOR TEXT THIS CODE DID NOT COMPOSE.</b> Both forms are the same string, the
    /// parameter list is empty and <see cref="Bindable"/> is <see langword="false"/> - which says exactly
    /// what is true: nothing was extracted from this text, so nothing can be certified about it. Executing
    /// it is still correct and still what an executor does, because executing a parameterized text with no
    /// parameters is executing the text. What the flag prevents is a downstream check REPORTING this
    /// expression as verified.
    /// </remarks>
    public static BoundFilterExpression Unbindable(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new BoundFilterExpression(text, [], false);
    }

    /// <summary>
    /// Renumbers one expression's placeholders onto a running offset, for an OR-composition of several.
    /// </summary>
    /// <param name="offset">The zero-based position the first of this expression's parameters takes.</param>
    /// <returns>The parameterized text and the renumbered literals.</returns>
    /// <remarks>
    /// EXPOSED SO THAT A MULTI-CLAUSE COMPOSITION USES THIS ONE SCAN RATHER THAN REPEATING IT. The C-03
    /// boundary ORs one clause per search term, and a second hand-written renumbering loop there would be a
    /// second chance to reintroduce the prefix defect <see cref="Rewrite"/> documents.
    /// </remarks>
    public (string Text, IReadOnlyList<BoundFilterLiteral> Literals) Renumber(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        if (Parameters.Count == 0)
        {
            return (ParameterizedText, []);
        }

        Dictionary<string, string> shift = new(StringComparer.Ordinal);
        List<BoundFilterLiteral> moved = [];

        for (int index = 0; index < Parameters.Count; index++)
        {
            BoundFilterLiteral literal = Parameters[index];
            string placeholder = Placeholder(offset + index);

            shift[literal.Placeholder] = placeholder;
            moved.Add(literal with { Placeholder = placeholder });
        }

        return (
            Rewrite(
                ParameterizedText,
                placeholder => shift.TryGetValue(placeholder, out string? target) ? target : placeholder),
            moved);
    }

    /// <summary>Builds the placeholder for a zero-based parameter position.</summary>
    /// <param name="position">The zero-based position.</param>
    /// <returns>The placeholder token.</returns>
    public static string Placeholder(int position) =>
        PlaceholderPrefix + position.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Conjoins an original filter ahead of a user filter, as the oracle's composition arm does, keeping
    /// the bindings of BOTH.
    /// </summary>
    /// <param name="original">
    /// The filter already in force on the child - normally its own pre-existing one, read back through
    /// <c>Describe</c> [<c>n_cst_dwsvc_dropdownsearch.sru:L216-L217</c>, <c>:L445</c>] and therefore
    /// unbindable text, but bindable when a caller supplied it as a composed search.
    /// </param>
    /// <param name="user">The user-derived half.</param>
    /// <returns>The conjoined expression.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE THREE ARMS ARE THE ORACLE'S AND ARE NOT SYMMETRICAL [<c>:L372-L380</c>]: a non-empty user filter
    /// with an original present is conjoined as <c>"(" + org + ") AND (" + filter + ")"</c>; a non-empty
    /// user filter with no original is used alone; and an EMPTY user filter RESTORES the original rather
    /// than clearing the child. That third arm is what makes the reset path at <c>:L158</c> and <c>:L180</c>
    /// a restore rather than a wipe.
    /// </para>
    /// <para>
    /// <b>CERTIFICATION IS CONJUNCTIVE; THE BOUND HALF'S BINDINGS ARE NOT LOST WITH IT.</b> The result is
    /// certified only when BOTH halves are, because a conjunction containing one half of unknown provenance
    /// cannot be vouched for as a whole. But the certification is only a REPORT: the conjoined parameterized
    /// text still carries the bound half's placeholders and the merged parameter list still carries its
    /// values, so executing the result still binds every value that was bound before. That distinction is
    /// what keeps the re-apply path - a user's bound search conjoined onto an application-supplied original
    /// filter [<c>:L451</c>] - from silently reverting to interpolation.
    /// </para>
    /// <para>
    /// <b>THE USER HALF'S PLACEHOLDERS ARE RENUMBERED THROUGH A SINGLE SCAN.</b> Each half numbers its own
    /// parameters from zero, so the two collide the moment they are conjoined. The rewrite is one
    /// left-to-right pass for the reason <see cref="Rewrite"/> gives: successive replacements corrupt their
    /// own output once ten parameters exist, because <c>:pfwArg1</c> is a prefix of <c>:pfwArg10</c>.
    /// </para>
    /// </remarks>
    public static BoundFilterExpression Conjoin(BoundFilterExpression original, BoundFilterExpression user)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(user);

        if (user.IsEmpty)
        {
            // :L379  AN EMPTY USER FILTER RESTORES THE ORIGINAL, it does not clear the child.
            return original;
        }

        if (original.IsEmpty)
        {
            // :L375-L377 not taken - the user half stands alone, bindings and numbering intact.
            return user;
        }

        int offset = original.Parameters.Count;

        Dictionary<string, string> shift = new(StringComparer.Ordinal);
        List<BoundFilterLiteral> combined = [.. original.Parameters];

        for (int index = 0; index < user.Parameters.Count; index++)
        {
            BoundFilterLiteral literal = user.Parameters[index];
            string moved = Placeholder(offset + index);

            shift[literal.Placeholder] = moved;
            combined.Add(literal with { Placeholder = moved });
        }

        return new BoundFilterExpression(
            "("
                + original.ParameterizedText
                + ") AND ("
                + Rewrite(
                    user.ParameterizedText,
                    placeholder => shift.TryGetValue(placeholder, out string? moved) ? moved : placeholder)
                + ")",
            combined,
            original.Bindable && user.Bindable);
    }
}
