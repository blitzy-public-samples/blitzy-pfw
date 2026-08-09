// ==================================================================================================
//  PinyinFirstLetterMatcher - the headless substitute for pfwPinyinFirstLetterLike.
//
//  ORACLE      ws_objects/pfw.utility.pbl.src/pinyinfirstletterlike.srf   (the two prototypes)
//              ws_objects/pfw.shared.pbl.src/enums.sru:L1144-L1151        (the three [flags] bits)
//              ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L323
//                                                                        (the SOLE call site)
//  READ-ONLY   Every path above is the behavioural oracle under constraint C-C. Never edited.
//
//  THIS FILE CARRIES THE SINGLE GENUINE PARITY RISK OF THE IN-SCOPE SET (plan risk R1). Read this
//  header before changing any matching logic; the design is unusual on purpose and the reasons are
//  recorded here rather than left to be rediscovered (constraint C-K).
//
// --------------------------------------------------------------------------------------------------
//  1. WHAT THE ORACLE ACTUALLY CONTAINS, AND WHAT IT DOES NOT
// --------------------------------------------------------------------------------------------------
//  pinyinfirstletterlike.srf is ELEVEN LINES and is declaration-only. Its type body is EMPTY:
//
//      global type pinyinfirstletterlike from function_object   [:L3]
//      end type                                                 [:L4]
//
//  and the whole of its behaviour is two forward prototypes, each bound straight to the closed
//  native binary:
//
//      global function boolean PinyinFirstLetterLike (readonly string text, readonly string py)
//          system library "pfw.dll" alias for "pfwPinyinFirstLetterLike"                     [:L7]
//      global function boolean PinyinFirstLetterLike (readonly string text, readonly string py,
//          readonly unsignedlong flags)
//          system library "pfw.dll" alias for "pfwPinyinFirstLetterLike"                     [:L8]
//
//  There is NO PowerScript implementation of this function anywhere in the repository, and no C++
//  source for pfw.dll anywhere either. That is the whole of the risk in one sentence.
//
//  NOTE ON THE BINDING MECHANISM, because it explains an earlier miss. This object carries NO
//  `native "pfw.dll"` attribute on its type - unlike most PBNI objects in the same library, and
//  unlike sprintf.srf in pfw.common which does. It binds purely through the two `system library`
//  prototypes, which is the CLASSIC EXTERNAL-PROTOTYPE mechanism rather than the PBNI-class one. A
//  repository sweep that looked only for `native "..."` declarations would not find this file at all.
//
// --------------------------------------------------------------------------------------------------
//  2. THE FLAGS ARE DOCUMENTED. THIS NARROWS THE RISK THE PLAN DESCRIBES.
// --------------------------------------------------------------------------------------------------
//  The plan states in two places - its native-binding substitution matrix and its open risk R1 - that
//  the pinyin flag semantics are UNDOCUMENTED, and docs/PARITY.md repeats it. THAT IS OUT OF DATE.
//  The oracle documents all three bits itself, under its own heading:
//
//      /*--- Pinyin ---*/                                          [enums.sru:L1144]
//      //PinyinFirstLetterLike:[flags]                             [enums.sru:L1146]
//      constant long PY_LIKE_IGNORE_CASE  = 1  // ignore case                            [:L1147]
//      constant long PY_LIKE_IGNORE_WIDTH = 2  // ignore full-width versus half-width    [:L1148]
//      constant long PY_LIKE_FUZZY_SOUND  = 4  // fuzzy pronunciation (l=n, f=h, r=l)    [:L1149]
//
//  So the literal 7 the sole call site passes is fully explained: 7 == 1 | 2 | 4 == all three bits.
//  This file therefore COMPOSES those three named constants and never writes the magic number - see
//  LegacyCallSiteFlags. The constants are consumed from PowerFramework.Shared.Kernel and are
//  deliberately NOT redeclared here, which is also why this file needs no .editorconfig naming
//  suppression: it declares no SCREAMING_SNAKE identifier of its own.
//
//  Because the flags are known, everything the flags GOVERN is implemented concretely and completely
//  in this file: case folding, full-width/half-width folding, and fuzzy-sound equivalence.
//
// --------------------------------------------------------------------------------------------------
//  3. WHAT REMAINS GENUINELY CLOSED - FOUR THINGS, NOT ONE
// --------------------------------------------------------------------------------------------------
//  Knowing the flags does not yield the function. Four inputs to a bit-exact answer live only inside
//  pfw.dll and cannot be derived from anything in this repository:
//
//    (a) THE LOOKUP TABLE. The mapping from a Han character to its pinyin initial(s). No table, no
//        data file and no source for one exists anywhere in the tree.
//
//    (b) THE MATCHING ALGORITHM. "Like" is not a defined relation. Whether the pattern must prefix
//        the derived initial string, appear anywhere within it, or equal it outright is not stated
//        at the call site, in the five legacy documents, or in the changelog. docs/ARCHITECTURE.md
//        records this explicitly: what is unavailable is "the lookup table AND the matching
//        algorithm". A great deal of otherwise careful work treats the table as the only unknown; it
//        is not.
//
//    (c) THE TWO-ARGUMENT OVERLOAD'S EFFECTIVE FLAGS. Both prototypes alias the SAME native export,
//        so the binary - not the PowerScript - decides what the omitted argument becomes. Zero and 7
//        are both plausible and the repository never says. Guessing either would silently change
//        which rows match, so this file BLOCKS the two-argument form until a characterized default is
//        supplied. See PinyinMatchConfiguration.CharacterizedDefaultFlags.
//
//    (d) THE FUZZY-SOUND EQUIVALENCE SET, in two independent respects:
//
//        EXHAUSTIVENESS. The comment gives three pairs. It does not claim to be complete, and
//        Mandarin fuzzy-sound conventions in the wild routinely also carry z/zh, c/ch, s/sh and
//        others. Three examples are not a specification.
//
//        CLOSURE, which is subtler and is easy to miss. The documented pairs are l=n, f=h and r=l,
//        and `l` occurs in TWO of them. Read as an equivalence relation the classes are {l,n,r} and
//        {f,h}, so `n` and `r` match each other. Read as unordered pairs only, l~n and l~r hold but
//        n~r does NOT. The oracle's `=` spelling hints at the first reading and the comment's shape
//        hints at the second; nothing in the repository settles it, and the two readings return
//        different result sets. It is therefore a configured, oracle-settled choice here rather than
//        an assumption - see PinyinFuzzySoundClosure.
//
// --------------------------------------------------------------------------------------------------
//  4. THE RULE THIS FILE OBEYS: CHARACTERIZE, OR REPORT BLOCKED. NEVER APPROXIMATE.
// --------------------------------------------------------------------------------------------------
//  The mandate is absolute in the plan (risk R1) and in docs/PARITY.md, and the reasoning is specific
//  rather than cautious:
//
//      A substitute pinyin table gets most characters right and a minority wrong, so an approximated
//      filter returns SUBTLY DIFFERENT RESULT SETS - mostly correct, occasionally missing a row,
//      occasionally including one. That is a regression that looks like correct behaviour. It passes
//      review, it passes a unit test written against the substitute's own table, and it is found by
//      a user rather than by a suite. A BLOCKED report is a KNOWN gap; an approximation is an
//      UNKNOWN one.
//
//  So this file is built as a complete, exercised implementation of everything that is knowable,
//  wrapped around SEAMS for the four things that are not. Every seam has an unconfigured state, and
//  every unconfigured state produces the same first-class outcome: Unavailable, carrying a reason.
//  Unavailable is never approximated into a match, and never degraded into a non-match.
//
//  THE THREE-WAY DISCIPLINE ON HOW UNAVAILABILITY IS DELIVERED, which is the point of the design:
//
//    * NOT a silent `false`. A caller that cannot tell "this row does not match" from "matching is
//      not available" will filter rows away and report success. TryMatch therefore returns a
//      TRI-STATE outcome, not a bool.
//    * NOT an exception escaping expression evaluation. TryInvokeFromExpression converts the
//      unavailable outcome into a structured ExpressionParseError and returns false; nothing throws
//      out of a filter evaluation.
//    * NOT unobservable either. The two `bool`-returning parity overloads cannot express a third
//      state - their signature is fixed by the oracle - so when they are unavailable they throw
//      PinyinMatchUnavailableException, a defined and documented outcome carrying the full result.
//      Returning false there would be exactly the silent approximation this file exists to prevent.
//
//  DELIBERATE NON-DECISIONS, recorded so nobody "completes" this file by adding one:
//
//    * NO THIRD-PARTY PINYIN PACKAGE. The plan excludes this by name. When the acceptance criterion
//      is bit-exact parity with a closed table, trusting a different table is precisely the wrong
//      trade: it converts a known gap into an unknown one while looking like progress. Adding one
//      would also breach constraint C-B, which forbids behaviour change beyond what the technology
//      transition requires. Cross-check: no pinyin PackageReference exists in this project and no
//      pinyin PackageVersion exists in Directory.Packages.props.
//    * NO P/INVOKE TO pfw.dll. The plan requires the PBNI and native dependency be ELIMINATED
//      outright rather than wrapped for every in-scope capability. The native binaries are read-only
//      artifacts of the legacy tree and the deployment target is Linux containers, so a DllImport
//      would not merely be inelegant, it would not load.
//    * NO ILogger DEPENDENCY. Unavailability must be loggable, and it is: PinyinMatchResult.Diagnostic
//      renders a complete, log-ready sentence naming the reason and the seam that is unconfigured.
//      Taking a logger would add a dependency this file's contract does not list and would make a pure,
//      deterministic matcher awkward to test.
//    * NO UI, NO IME, NO WINDOW GEOMETRY, NO DPI. Constraint C-D. The drop-down search service's
//      presentational half is deferred; this file is the headless matcher only.
//
//  THE ORACLE PROCEDURE for deriving the table, settling the algorithm, settling the two-argument
//  default and settling the fuzzy set lives in docs/PARITY.md (risk R1) alongside the paired-recording
//  model - characterization/recordings/legacy/<workflowId>/ against
//  characterization/recordings/dotnet/<workflowId>/. Recorded data is loaded through the seams below;
//  no recorded data is embedded in this file.
//
// --------------------------------------------------------------------------------------------------
//  5. HOW THIS FILE IS REACHED, WHICH IS NOT HOW IT LOOKS
// --------------------------------------------------------------------------------------------------
//  The sole call site is verified as the only occurrence of the name in the repository, and it does
//  not call the function from PowerScript at all. It BUILDS A STRING:
//
//      sFilter += " OR PinyinFirstLetterLike(" + _editCtx.dddw.dispColName
//                 + ",'" + sData + "',7)"      [n_cst_dwsvc_dropdownsearch.sru:L323]
//
//  That is a DataWindow FILTER EXPRESSION, evaluated later by the DataWindow expression evaluator.
//  The architectural consequence is binding: this matcher is reached through the expression
//  evaluator's FUNCTION DISPATCH, under the expression-callable name PinyinFirstLetterLike - see
//  ExpressionFunctionName and TryInvokeFromExpression. Services/DropDownSearchModel.cs builds the
//  filter TEXT and never calls this file directly. An implementation that exposed only a direct C#
//  API for that model to call would be architecturally wrong and would not reproduce the legacy path.
//
//  Corroboration from the engine side: n_cst_dwsvc_columnexp.sru's funcdata structure carries a
//  `builtin` field [:L62] which is declared and NEVER ASSIGNED anywhere in that object, and its
//  dispatch switch handles only the two sentinels FUNC_VAR = "" [:L120] and FUNC_INVOKE = "Invoke"
//  [:L121]. A built-in filter function like this one is therefore resolved by the DataWindow
//  evaluator, not registered through the macro path.
//
//  WHAT THE INPUTS ACTUALLY LOOK LIKE, from the same call site, because it changes what is correct:
//
//    * `py` ARRIVES ALREADY LOWER-CASED. sData = Lower(data) at [:L316]. This file therefore does NOT
//      lower-case defensively: doing so would mask whether PY_LIKE_IGNORE_CASE is honoured, and with
//      the flag CLEAR it would change the answer.
//    * The clause is reached only when the pinyin bit of the filter type is set [:L321] AND the raw
//      input matches [a-zA-Z] [:L322]. Both guards live in DropDownSearchModel, not here. They are
//      NOT re-applied here - re-guarding would change behaviour for any other caller and would
//      duplicate a decision that belongs upstream.
//    * The clause is OR-ed with the plain display-column LIKE filter [:L319], so this predicate is
//      one term of a compound expression. A parity matrix must vary the other terms too.
//    * The argument is interpolated UNESCAPED. That is a known legacy filter-injection defect,
//      recorded in the plan's storage analysis. It belongs to the filter-CONSTRUCTION site and is
//      documented rather than corrected (constraint C-B). NO escaping is added here; adding it would
//      change the observable generated filter.
//
// --------------------------------------------------------------------------------------------------
//  6. ONE PIECE OF CORROBORATING EVIDENCE, AND THE LIMIT ON HOW IT IS USED
// --------------------------------------------------------------------------------------------------
//  logfile.md:L376 records the function's introduction and says it supports 多音字 - POLYPHONIC
//  CHARACTERS - as well as fuzzy-sound matching. logfile.md:L394 records that it replaced an earlier
//  DDSPYFirstLetterLike in the DataWindow services library.
//
//  THE CHANGELOG IS NOT A SPECIFICATION and is stale - it stops years before the commit history, and
//  the plan's risk R8 is explicit that behaviour is derived from source and never from the changelog.
//  It is used here for exactly one structural purpose and no behavioural one: it establishes that a
//  character may have MORE THAN ONE reading, so the table seam is one-to-MANY. A one-to-one
//  char -> char seam would be structurally incapable of expressing what the oracle does, and that
//  shape has to be right before any recorded data can be loaded into it. WHICH readings each
//  character has remains a characterization output.
// ==================================================================================================

using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Expressions;

/// <summary>
/// The three-valued result of a pinyin first-letter match.
/// </summary>
/// <remarks>
/// <para>
/// THE THIRD STATE IS THE WHOLE POINT. A boolean cannot distinguish "this text does not match the
/// pattern" from "matching is not available in this deployment", and conflating them is the exact
/// failure mode the parity mandate forbids: rows would be filtered away and the operation would
/// report success. <see cref="Unavailable"/> keeps the two apart at the type level.
/// </para>
/// <para>
/// This enumeration has no legacy counterpart, and it cannot have one. The native function returns
/// <c>boolean</c> because in-process it always has its table; it has no way to be unavailable. The
/// third state is created by the decomposition, in the same way that the one narrowing elsewhere in
/// this service is.
/// </para>
/// </remarks>
public enum PinyinMatchOutcome
{
    /// <summary>
    /// No outcome. A guard for the default-initialised value; never produced by this file.
    /// </summary>
    Unspecified = 0,

    /// <summary>The text matched the pattern under the supplied flags.</summary>
    Match = 1,

    /// <summary>
    /// The text did not match the pattern under the supplied flags. A real, trusted negative: the
    /// comparison ran to completion.
    /// </summary>
    NoMatch = 2,

    /// <summary>
    /// The comparison could not be performed, because something the closed native binary owns has
    /// not been characterized and supplied. NEVER a negative - see
    /// <see cref="PinyinMatchUnavailableReason"/> for which seam is missing.
    /// </summary>
    Unavailable = 3,
}

/// <summary>
/// Which unconfigured seam made a match unavailable.
/// </summary>
/// <remarks>
/// <para>
/// Every member names one of the four things that live only inside <c>pfw.dll</c>, so an operator
/// reading a log can tell precisely what characterization work would unblock the filter. That is the
/// difference between a BLOCKED report that is actionable and one that merely says "no".
/// </para>
/// <para>
/// None of these is an error in the ordinary sense. Each is the honest answer to a question the
/// repository cannot answer, and each disappears the moment the corresponding recording exists.
/// </para>
/// </remarks>
public enum PinyinMatchUnavailableReason
{
    /// <summary>
    /// Not unavailable. The value carried by a <see cref="PinyinMatchOutcome.Match"/> or
    /// <see cref="PinyinMatchOutcome.NoMatch"/> result.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// No lookup table is configured, so no character can be reduced to a pinyin initial. This is the
    /// DEFAULT state of a matcher built from <see cref="PinyinMatchConfiguration.Blocked"/>, and it is
    /// the state this refactor ships in until the oracle is exercised. Closed input (a).
    /// </summary>
    LookupTableNotConfigured = 1,

    /// <summary>
    /// A table is configured but the matching relation is not: whether the pattern must prefix, occur
    /// within, or equal the derived initials is not stated anywhere in the repository. Closed input
    /// (b).
    /// </summary>
    MatchStrategyNotCharacterized = 2,

    /// <summary>
    /// The two-argument overload was called and no characterized default flag set has been supplied.
    /// Both prototypes alias the same native export, so the binary decides what the omitted argument
    /// becomes; the repository never says. Closed input (c) - and the reason this file refuses to
    /// guess zero or seven.
    /// </summary>
    DefaultFlagsNotCharacterized = 3,

    /// <summary>
    /// A table is configured but has no reading for a character actually present in the text, so the
    /// derived initials would be incomplete. The comparison is abandoned rather than run against a
    /// partial reduction, because a partial reduction is exactly an approximation.
    /// </summary>
    LookupTableIncomplete = 4,
}

/// <summary>
/// How the pattern is compared against the derived pinyin initials.
/// </summary>
/// <remarks>
/// <para>
/// THE ALGORITHM IS AS CLOSED AS THE TABLE IS, and this enumeration exists to stop that from being
/// forgotten. The oracle's name ends in "Like", which is suggestive but not a definition; the call
/// site wraps its OTHER filter term in <c>LIKE '%...%'</c> explicitly [:L319] but calls this function
/// bare [:L323], so the relation is internal to the binary and no wildcard syntax is visible.
/// docs/ARCHITECTURE.md records the matching algorithm as unavailable alongside the table.
/// </para>
/// <para>
/// The three named members are the candidate readings a characterization run must choose between.
/// They are NOT ranked and no default is nominated: <see cref="NotCharacterized"/> is the zero, so a
/// configuration that forgets to state a strategy is BLOCKED rather than quietly taking whichever
/// member happened to be listed first.
/// </para>
/// </remarks>
public enum PinyinMatchStrategy
{
    /// <summary>
    /// The relation has not been characterized. The zero value, and the shipped state: a match
    /// attempt yields <see cref="PinyinMatchUnavailableReason.MatchStrategyNotCharacterized"/>.
    /// </summary>
    NotCharacterized = 0,

    /// <summary>The initials must START WITH the pattern.</summary>
    Prefix = 1,

    /// <summary>The pattern must occur ANYWHERE within the initials.</summary>
    Substring = 2,

    /// <summary>The initials must EQUAL the pattern.</summary>
    Whole = 3,
}

/// <summary>
/// Whether the documented fuzzy-sound pairs are read as bare pairs or closed into equivalence
/// classes.
/// </summary>
/// <remarks>
/// <para>
/// THIS DISTINCTION IS EASY TO MISS AND IT CHANGES RESULTS. The oracle documents three pairs -
/// <c>l=n</c>, <c>f=h</c>, <c>r=l</c> [enums.sru:L1149] - and the letter <c>l</c> appears in two of
/// them. So the two readings genuinely differ:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="PairwiseOnly"/>: <c>l</c>~<c>n</c> and <c>l</c>~<c>r</c> hold, but <c>n</c>~<c>r</c>
/// does NOT. The relation is symmetric and reflexive but deliberately not transitive.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="TransitiveClosure"/>: the classes are <c>{l,n,r}</c> and <c>{f,h}</c>, so
/// <c>n</c>~<c>r</c> DOES hold.
/// </description>
/// </item>
/// </list>
/// <para>
/// Nothing in the repository settles which the binary implements. The <c>=</c> spelling hints at an
/// equivalence relation; writing three separate pairs rather than one class hints at the opposite.
/// Because the two readings return different row sets, the choice is configuration settled by the
/// oracle rather than an assumption buried in a comparison routine.
/// </para>
/// <para>
/// <see cref="PairwiseOnly"/> is the zero because it is the LITERAL reading of the source - exactly
/// the three pairs as written, nothing added. Defaulting to the closure would be inferring a property
/// the source does not state.
/// </para>
/// </remarks>
public enum PinyinFuzzySoundClosure
{
    /// <summary>
    /// The pairs as literally written, with no transitive inference. The zero value and the default.
    /// </summary>
    PairwiseOnly = 0,

    /// <summary>
    /// The pairs closed transitively into equivalence classes, merging the two that share
    /// <c>l</c>.
    /// </summary>
    TransitiveClosure = 1,
}

/// <summary>
/// Supplies the pinyin initial(s) of a character. THE PRINCIPAL CHARACTERIZATION SEAM.
/// </summary>
/// <remarks>
/// <para>
/// This is closed input (a): the table exists only inside <c>pfw.dll</c>. The interface exists so
/// that oracle-derived data can be loaded when a recording exists, so that the absence of a table is
/// a legitimate configuration rather than a crash, and so that a test can supply a handful of
/// characters without needing the oracle at all.
/// </para>
/// <para>
/// THE CONTRACT IS ONE-TO-MANY, AND THAT SHAPE IS EVIDENCE-DRIVEN RATHER THAN SPECULATIVE.
/// logfile.md:L376 records that the function supports 多音字 - polyphonic characters, a character
/// with more than one reading. The changelog is not a specification and is stale, so it fixes only
/// the SHAPE of this contract and none of its content: a one-to-one <c>char</c> to <c>char</c> seam
/// would be structurally incapable of carrying what the oracle does, and the shape has to be right
/// before any recording can be loaded through it. Which readings a character actually has stays a
/// characterization output.
/// </para>
/// <para>
/// A <see cref="Rune"/> is taken rather than a <see cref="char"/> so that characters outside the
/// basic multilingual plane - the CJK extensions, which arrive as surrogate pairs in UTF-16 - are
/// addressable at all. Note honestly that whether the native binary itself operates per UTF-16 code
/// unit or per code point is ANOTHER unverified detail; a table that only ever answers for BMP runes
/// is perfectly legal here, and the distinction is only observable for supplementary-plane input.
/// </para>
/// <para>
/// Implementations must be thread-safe and side-effect free. A matcher is safe to share across
/// requests only if its table is, and the matcher itself holds no mutable state.
/// </para>
/// </remarks>
public interface IPinyinFirstLetterTable
{
    /// <summary>
    /// A short description of where this table's data came from, for diagnostics and for the parity
    /// record. For example the characterization workflow identifier that produced it.
    /// </summary>
    /// <remarks>
    /// Present so that an Unavailable or a surprising match can be attributed to a specific data
    /// source in a log, rather than to "the table". Never <see langword="null"/>.
    /// </remarks>
    string SourceDescription { get; }

    /// <summary>
    /// Gets every pinyin initial recorded for <paramref name="character"/>.
    /// </summary>
    /// <param name="character">The character to reduce.</param>
    /// <param name="letters">
    /// On success, one or more initials, in the order the recording lists them. Must not be empty on
    /// a <see langword="true"/> return.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when this table has a reading for the character;
    /// <see langword="false"/> when it does not.
    /// </returns>
    /// <remarks>
    /// A <see langword="false"/> return is NOT an error and must not throw - it means "this table
    /// does not cover that character", which the matcher turns into
    /// <see cref="PinyinMatchUnavailableReason.LookupTableIncomplete"/>. That is deliberate: the
    /// alternative would be to substitute the character for itself or to skip it, and both are
    /// approximations of a closed behaviour.
    /// </remarks>
    bool TryGetFirstLetters(Rune character, out ImmutableArray<char> letters);
}

/// <summary>
/// The fuzzy-sound equivalence relation applied when <c>PY_LIKE_FUZZY_SOUND</c> is set.
/// </summary>
/// <remarks>
/// <para>
/// Closed input (d), in its two independent respects. The relation this type carries is DOCUMENTED as
/// to its three pairs [enums.sru:L1149] and UNDOCUMENTED as to whether those three are exhaustive and
/// as to whether they close transitively - see <see cref="ExhaustivenessConfirmed"/> and
/// <see cref="Closure"/>.
/// </para>
/// <para>
/// The relation is applied SYMMETRICALLY at comparison time, between a pattern letter and a candidate
/// initial. Both sides are Latin letters, so there is no need to decide which side is "the pattern" -
/// and deciding would be inventing an asymmetry the source does not describe.
/// </para>
/// </remarks>
public sealed record PinyinFuzzySoundEquivalence
{
    /// <summary>
    /// The three pairs the oracle documents, verbatim and in source order: <c>l=n</c>, <c>f=h</c>,
    /// <c>r=l</c>. [ws_objects/pfw.shared.pbl.src/enums.sru:L1149]
    /// </summary>
    /// <remarks>
    /// Lower case because the oracle writes them lower case, and because the sole call site's pattern
    /// arrives lower-cased [n_cst_dwsvc_dropdownsearch.sru:L316]. Case is handled by
    /// <c>PY_LIKE_IGNORE_CASE</c> and by the folding this type performs before comparing, not by
    /// duplicating the table.
    /// </remarks>
    public static ImmutableArray<PinyinFuzzySoundPair> DocumentedPairs { get; } =
    [
        new PinyinFuzzySoundPair('l', 'n'),
        new PinyinFuzzySoundPair('f', 'h'),
        new PinyinFuzzySoundPair('r', 'l'),
    ];

    /// <summary>
    /// The relation exactly as the oracle documents it: the three pairs, read literally, with
    /// exhaustiveness unconfirmed.
    /// </summary>
    /// <remarks>
    /// The shipped default. Every property is the most conservative reading of the source, so nothing
    /// about this instance asserts more than [enums.sru:L1149] actually says.
    /// </remarks>
    public static PinyinFuzzySoundEquivalence Documented { get; } = new();

    /// <summary>
    /// The pairs in force. Defaults to <see cref="DocumentedPairs"/>.
    /// </summary>
    /// <remarks>
    /// Replaceable so a characterization run that discovers further pairs - the widely used
    /// <c>z</c>/<c>zh</c>, <c>c</c>/<c>ch</c> and <c>s</c>/<c>sh</c> conventions are the obvious
    /// candidates - can supply them without this file guessing that they apply.
    /// </remarks>
    public ImmutableArray<PinyinFuzzySoundPair> Pairs { get; init; } = DocumentedPairs;

    /// <summary>
    /// Whether the pairs are read literally or closed transitively. Defaults to
    /// <see cref="PinyinFuzzySoundClosure.PairwiseOnly"/>, the literal reading.
    /// </summary>
    public PinyinFuzzySoundClosure Closure { get; init; } = PinyinFuzzySoundClosure.PairwiseOnly;

    /// <summary>
    /// Whether <see cref="Pairs"/> has been CONFIRMED complete against the behavioural oracle.
    /// <see langword="false"/> until a characterization run says otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This flag deliberately does NOT gate matching, and that asymmetry is the interesting part. An
    /// unconfirmed pair set still produces real answers, because the three documented pairs are
    /// genuinely documented - refusing to use them would discard evidence the repository actually
    /// contains. What is unknown is only whether there are MORE.
    /// </para>
    /// <para>
    /// It is surfaced instead as an explicit, assertable statement of the open question, so that the
    /// gap is visible in a diagnostic and in the parity record rather than implied by silence. Compare
    /// the table, which does gate matching, because there no evidence exists at all.
    /// </para>
    /// </remarks>
    public bool ExhaustivenessConfirmed { get; init; }

    /// <summary>
    /// Tests whether two letters are fuzzy-sound equivalent under this relation.
    /// </summary>
    /// <param name="first">One letter.</param>
    /// <param name="second">The other letter.</param>
    /// <returns>
    /// <see langword="true"/> when the two are the same letter, or are related by
    /// <see cref="Pairs"/> under <see cref="Closure"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// REFLEXIVE AND SYMMETRIC ALWAYS; TRANSITIVE ONLY UNDER
    /// <see cref="PinyinFuzzySoundClosure.TransitiveClosure"/>. Reflexivity means this method is a
    /// safe replacement for plain equality at every comparison site, so the fuzzy flag changes which
    /// relation is used and never whether one is used.
    /// </para>
    /// <para>
    /// Both arguments are folded to lower case with the invariant culture before comparison. That is
    /// NOT a culture policy: the pairs are recorded lower case, so folding is how a pattern letter is
    /// matched against them at all, and the invariant culture is chosen precisely so that no ambient
    /// culture can alter the answer. Case SENSITIVITY of the overall match is a separate concern
    /// governed by <c>PY_LIKE_IGNORE_CASE</c> and applied by the matcher before it reaches here.
    /// </para>
    /// </remarks>
    public bool AreEquivalent(char first, char second)
    {
        char left = char.ToLowerInvariant(first);
        char right = char.ToLowerInvariant(second);

        if (left == right)
        {
            return true;
        }

        return Closure == PinyinFuzzySoundClosure.TransitiveClosure
            ? AreTransitivelyRelated(left, right)
            : IsDeclaredPair(left, right);
    }

    /// <summary>
    /// Renders the relation as a stable, log-ready string, for diagnostics and the parity record.
    /// </summary>
    /// <returns>
    /// For example <c>l=n,f=h,r=l (pairwise, exhaustiveness unconfirmed)</c>.
    /// </returns>
    public string Describe()
    {
        StringBuilder description = new(64);

        for (int index = 0; index < Pairs.Length; index++)
        {
            if (index > 0)
            {
                _ = description.Append(',');
            }

            _ = description.Append(Pairs[index].First).Append('=').Append(Pairs[index].Second);
        }

        _ = description
            .Append(" (")
            .Append(Closure == PinyinFuzzySoundClosure.TransitiveClosure ? "transitive" : "pairwise")
            .Append(", exhaustiveness ")
            .Append(ExhaustivenessConfirmed ? "confirmed" : "unconfirmed")
            .Append(')');

        return description.ToString();
    }

    /// <summary>
    /// Tests direct membership of a declared pair, in either order.
    /// </summary>
    /// <param name="left">The already-folded first letter.</param>
    /// <param name="right">The already-folded second letter.</param>
    /// <returns><see langword="true"/> when some pair relates the two.</returns>
    private bool IsDeclaredPair(char left, char right)
    {
        foreach (PinyinFuzzySoundPair pair in Pairs)
        {
            char first = char.ToLowerInvariant(pair.First);
            char second = char.ToLowerInvariant(pair.Second);

            if ((first == left && second == right) || (first == right && second == left))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tests membership of the same transitive equivalence class.
    /// </summary>
    /// <param name="left">The already-folded first letter.</param>
    /// <param name="right">The already-folded second letter.</param>
    /// <returns><see langword="true"/> when a chain of declared pairs joins the two.</returns>
    /// <remarks>
    /// A breadth-first walk over the pair graph rather than a precomputed closure, because the pair
    /// set is tiny - three entries as documented - and a walk cannot fall out of step with
    /// <see cref="Pairs"/> if that property is replaced. The visited set bounds the walk, so a pair
    /// set containing a cycle terminates.
    /// </remarks>
    private bool AreTransitivelyRelated(char left, char right)
    {
        HashSet<char> reached = [left];
        Queue<char> pending = new();
        pending.Enqueue(left);

        while (pending.Count > 0)
        {
            char current = pending.Dequeue();

            foreach (PinyinFuzzySoundPair pair in Pairs)
            {
                char first = char.ToLowerInvariant(pair.First);
                char second = char.ToLowerInvariant(pair.Second);

                char? next = first == current ? second : second == current ? first : null;

                if (next is null || !reached.Add(next.Value))
                {
                    continue;
                }

                if (next.Value == right)
                {
                    return true;
                }

                pending.Enqueue(next.Value);
            }
        }

        return false;
    }
}

/// <summary>
/// One documented fuzzy-sound equivalence, such as <c>l=n</c>.
/// </summary>
/// <param name="First">One letter of the pair.</param>
/// <param name="Second">The other letter of the pair.</param>
/// <remarks>
/// UNORDERED in meaning: the oracle writes <c>l=n</c> and <c>r=l</c>, and equality is symmetric, so
/// <see cref="PinyinFuzzySoundEquivalence.AreEquivalent"/> tests both directions. The two positions
/// exist only to preserve the source's own spelling and ordering, which the parity record compares
/// against.
/// </remarks>
public readonly record struct PinyinFuzzySoundPair(char First, char Second);

/// <summary>
/// Everything a <see cref="PinyinFirstLetterMatcher"/> needs that the repository cannot supply.
/// </summary>
/// <remarks>
/// <para>
/// The four closed inputs of this file's header, gathered into one bindable object so that a
/// deployment either has characterization data or provably does not. Each property's unconfigured
/// value maps to one <see cref="PinyinMatchUnavailableReason"/>, so BLOCKED is a legitimate,
/// first-class configuration rather than a failure to configure.
/// </para>
/// <para>
/// A record with <c>init</c> properties rather than a mutable options class, deliberately: a matcher
/// captures its configuration once and must not observe it changing underneath a comparison. The
/// options type that binds this service's settings holds the FLAG default only; the table and the
/// strategy arrive from characterization data, not from application settings.
/// </para>
/// </remarks>
public sealed record PinyinMatchConfiguration
{
    /// <summary>
    /// The BLOCKED configuration: no table, no characterized strategy, no characterized default flag
    /// set. The documented fuzzy relation is present because it is genuinely documented.
    /// </summary>
    /// <remarks>
    /// THE SHIPPED STATE OF THIS REFACTOR, and it is a deliberate deliverable rather than an
    /// oversight. The behavioural oracle has not been exercised in this environment - docs/PARITY.md
    /// records that - so the honest configuration is the one that reports BLOCKED. Every match
    /// attempt through it yields <see cref="PinyinMatchOutcome.Unavailable"/> with a reason naming the
    /// seam, which is precisely the reportable outcome the parity mandate asks for in place of an
    /// approximation.
    /// </remarks>
    public static PinyinMatchConfiguration Blocked { get; } = new();

    /// <summary>
    /// The lookup table, or <see langword="null"/> when none is configured. Closed input (a).
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> is meaningful and is the default: it produces
    /// <see cref="PinyinMatchUnavailableReason.LookupTableNotConfigured"/>. It is NOT normalised to an
    /// empty table, because an empty table and an absent one are different findings - the first says
    /// the recording covered nothing, the second says there is no recording.
    /// </remarks>
    public IPinyinFirstLetterTable? Table { get; init; }

    /// <summary>
    /// How the pattern is compared against the derived initials. Closed input (b). Defaults to
    /// <see cref="PinyinMatchStrategy.NotCharacterized"/>.
    /// </summary>
    public PinyinMatchStrategy Strategy { get; init; } = PinyinMatchStrategy.NotCharacterized;

    /// <summary>
    /// The fuzzy-sound relation used when <c>PY_LIKE_FUZZY_SOUND</c> is set. Closed input (d).
    /// Defaults to <see cref="PinyinFuzzySoundEquivalence.Documented"/>.
    /// </summary>
    /// <remarks>
    /// Non-null by construction. Unlike the table, this has a documented default because
    /// [enums.sru:L1149] genuinely documents three pairs; only their completeness and closure are
    /// open, and those are carried on the relation itself.
    /// </remarks>
    public PinyinFuzzySoundEquivalence FuzzySound { get; init; } =
        PinyinFuzzySoundEquivalence.Documented;

    /// <summary>
    /// The flag set the TWO-ARGUMENT overload behaves as, once characterized, or
    /// <see langword="null"/> while it is unknown. Closed input (c).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE NULL IS THE ENTIRE POINT AND MUST NOT BE GIVEN A DEFAULT. Both legacy prototypes alias the
    /// same native export [pinyinfirstletterlike.srf:L7-L8], so the omitted argument is resolved
    /// inside <c>pfw.dll</c> and the repository never states the value. Zero and seven are both
    /// plausible, they produce different result sets, and choosing either would be exactly the
    /// invisible approximation this file exists to prevent.
    /// </para>
    /// <para>
    /// So while this is <see langword="null"/> the two-argument overload is BLOCKED with
    /// <see cref="PinyinMatchUnavailableReason.DefaultFlagsNotCharacterized"/>, and the
    /// three-argument overload - whose flags the caller states explicitly - is unaffected. Note that
    /// the only in-repository call site passes all three flags explicitly
    /// [n_cst_dwsvc_dropdownsearch.sru:L323], so nothing in the legacy estate depends on this default;
    /// it is unknown, but it is also unexercised.
    /// </para>
    /// <para>
    /// Do NOT initialise this to <see cref="PinyinFirstLetterMatcher.LegacyCallSiteFlags"/>. That
    /// constant is what the CALL SITE passes, not what the OVERLOAD defaults to, and conflating the
    /// two would turn an honest gap into a confident error.
    /// </para>
    /// </remarks>
    public uint? CharacterizedDefaultFlags { get; init; }
}

/// <summary>
/// The complete outcome of one pinyin first-letter match, including everything needed to log it.
/// </summary>
/// <remarks>
/// <para>
/// A struct because a filter predicate is evaluated per row and this type is small, immutable and
/// never stored; a record struct so that value equality makes it directly assertable in a parity
/// matrix.
/// </para>
/// <para>
/// It carries its inputs alongside its answer on purpose. An Unavailable outcome is only actionable
/// if the reader can see which text and which pattern provoked it and under which flags, and a
/// characterization recording compares inputs and outputs together.
/// </para>
/// </remarks>
public readonly record struct PinyinMatchResult
{
    /// <summary>The three-valued answer.</summary>
    public PinyinMatchOutcome Outcome { get; init; }

    /// <summary>
    /// Which seam was unconfigured, when <see cref="Outcome"/> is
    /// <see cref="PinyinMatchOutcome.Unavailable"/>; otherwise
    /// <see cref="PinyinMatchUnavailableReason.Unspecified"/>.
    /// </summary>
    public PinyinMatchUnavailableReason UnavailableReason { get; init; }

    /// <summary>The text that was searched, as supplied.</summary>
    public string Text { get; init; }

    /// <summary>
    /// The pinyin-initial pattern that was searched for, as supplied - already lower-cased when it
    /// arrives from the legacy call path [n_cst_dwsvc_dropdownsearch.sru:L316].
    /// </summary>
    public string Pattern { get; init; }

    /// <summary>The effective flag set the comparison used.</summary>
    public uint Flags { get; init; }

    /// <summary>
    /// The candidate initial strings derived from <see cref="Text"/>, one per combination of
    /// polyphonic readings, or empty when derivation did not complete.
    /// </summary>
    /// <remarks>
    /// Present because it is the single most useful thing to see when a match surprises somebody, and
    /// because a characterization comparison of the derived initials isolates a table fault from a
    /// strategy fault. Empty on any Unavailable outcome.
    /// </remarks>
    public ImmutableArray<string> DerivedInitials { get; init; }

    /// <summary>
    /// A complete, log-ready sentence describing this outcome. Never <see langword="null"/> and never
    /// empty.
    /// </summary>
    /// <remarks>
    /// THIS IS HOW UNAVAILABILITY IS MADE LOGGABLE WITHOUT THIS FILE TAKING A LOGGER. The matcher
    /// stays a pure function, the caller logs at whatever level it considers appropriate, and the text
    /// naming the unconfigured seam is written once here rather than reconstructed at each call site.
    /// </remarks>
    public string Diagnostic { get; init; }

    /// <summary>
    /// <see langword="true"/> only for <see cref="PinyinMatchOutcome.Match"/>.
    /// </summary>
    /// <remarks>
    /// DELIBERATELY NOT A GENERAL-PURPOSE COERCION. It is false for Unavailable as well as for
    /// NoMatch, so it must only be read after <see cref="IsAvailable"/> has been checked - which is
    /// why the parity overloads throw rather than returning this property blindly. Reading it alone is
    /// the exact mistake that turns a BLOCKED filter into a silently wrong one.
    /// </remarks>
    public bool IsMatch => Outcome == PinyinMatchOutcome.Match;

    /// <summary>
    /// <see langword="true"/> when the comparison actually ran, whichever way it came out.
    /// </summary>
    public bool IsAvailable => Outcome is PinyinMatchOutcome.Match or PinyinMatchOutcome.NoMatch;

    /// <summary>
    /// Builds a decided outcome - a genuine match or a genuine non-match.
    /// </summary>
    /// <param name="matched">Whether the comparison succeeded.</param>
    /// <param name="text">The text searched.</param>
    /// <param name="pattern">The pattern searched for.</param>
    /// <param name="flags">The effective flags.</param>
    /// <param name="derivedInitials">The candidate initial strings derived from the text.</param>
    /// <returns>The decided result.</returns>
    /// <remarks>
    /// EVERY <c>Sprintf</c> PLACEHOLDER IN THIS FILE IS ONE-BASED - <c>{1}</c> is the FIRST argument,
    /// not <c>{0}</c>. That is the ported formatter's documented convention, inherited from the
    /// legacy logger's format grammar, and it differs from .NET composite formatting. An index with no
    /// argument renders as the empty string rather than throwing, so a <c>{0}</c> written out of .NET
    /// habit fails SILENTLY: every argument shifts by one position and the last one vanishes. This was
    /// caught here by an ad-hoc diagnostic assertion rather than by the compiler, so do not
    /// "normalise" these templates to zero-based.
    /// </remarks>
    public static PinyinMatchResult Decided(
        bool matched,
        string text,
        string pattern,
        uint flags,
        ImmutableArray<string> derivedInitials) => new()
        {
            Outcome = matched ? PinyinMatchOutcome.Match : PinyinMatchOutcome.NoMatch,
            UnavailableReason = PinyinMatchUnavailableReason.Unspecified,
            Text = text,
            Pattern = pattern,
            Flags = flags,
            DerivedInitials = derivedInitials,
            Diagnostic = Formatting.Sprintf(
                "PinyinFirstLetterLike({1},'{2}',{3}) = {4}. Derived initials: {5}.",
                text,
                pattern,
                flags.ToString(CultureInfo.InvariantCulture),
                matched ? "true" : "false",
                derivedInitials.IsDefaultOrEmpty ? "(none)" : string.Join('|', derivedInitials)),
        };

    /// <summary>
    /// Builds an unavailable outcome, naming the unconfigured seam.
    /// </summary>
    /// <param name="reason">Which seam is missing.</param>
    /// <param name="text">The text searched.</param>
    /// <param name="pattern">The pattern searched for.</param>
    /// <param name="flags">The flags the comparison would have used.</param>
    /// <param name="detail">
    /// A short, specific addition to the diagnostic - the offending character, or the table's source
    /// description. May be <see langword="null"/>.
    /// </param>
    /// <returns>The unavailable result.</returns>
    /// <remarks>
    /// <see cref="DerivedInitials"/> is left empty rather than partially filled. A partial reduction is
    /// an approximation, and publishing one would invite a caller to use it.
    /// </remarks>
    public static PinyinMatchResult Unavailable(
        PinyinMatchUnavailableReason reason,
        string text,
        string pattern,
        uint flags,
        string? detail) => new()
        {
            Outcome = PinyinMatchOutcome.Unavailable,
            UnavailableReason = reason,
            Text = text,
            Pattern = pattern,
            Flags = flags,
            DerivedInitials = [],
            Diagnostic = Formatting.Sprintf(
                "PinyinFirstLetterLike({1},'{2}',{3}) is UNAVAILABLE: {4}{5} This is a reported "
                    + "parity gap, not a non-match; see docs/PARITY.md risk R1 for the oracle "
                    + "procedure that resolves it.",
                text,
                pattern,
                flags.ToString(CultureInfo.InvariantCulture),
                DescribeReason(reason),
                string.IsNullOrEmpty(detail) ? string.Empty : " " + detail),
        };

    /// <summary>
    /// Renders a reason as the sentence fragment the diagnostic embeds.
    /// </summary>
    /// <param name="reason">The reason to describe.</param>
    /// <returns>A complete sentence naming the closed input and what would resolve it.</returns>
    private static string DescribeReason(PinyinMatchUnavailableReason reason) => reason switch
    {
        PinyinMatchUnavailableReason.LookupTableNotConfigured =>
            "no pinyin lookup table is configured, so no character can be reduced to an initial. "
                + "The table exists only inside the closed pfw.dll and must be characterized from "
                + "the behavioural oracle.",
        PinyinMatchUnavailableReason.MatchStrategyNotCharacterized =>
            "the matching relation has not been characterized. Whether the pattern must prefix, "
                + "occur within, or equal the derived initials is not stated anywhere in the "
                + "repository.",
        PinyinMatchUnavailableReason.DefaultFlagsNotCharacterized =>
            "the two-argument overload's effective flag set has not been characterized. Both legacy "
                + "prototypes alias one native export, so pfw.dll decides the omitted argument and "
                + "the repository never states it; neither 0 nor 7 is assumed.",
        PinyinMatchUnavailableReason.LookupTableIncomplete =>
            "the configured pinyin lookup table has no reading for a character present in the text, "
                + "so the derived initials would be incomplete.",
        _ =>
            "the reason was not stated, which is itself a defect in the caller.",
    };
}

/// <summary>
/// Thrown by the two <c>boolean</c>-returning parity overloads when matching is unavailable.
/// </summary>
/// <remarks>
/// <para>
/// A DEFINED OUTCOME, NOT A FAULT, and it exists because of a signature constraint rather than a
/// design preference. The legacy prototypes return <c>boolean</c>
/// [pinyinfirstletterlike.srf:L7-L8] and this port reproduces that signature exactly, so those two
/// methods have nowhere to put a third state. The alternatives were to return <see langword="false"/>
/// - the silent approximation the parity mandate specifically forbids, because it converts "cannot
/// tell" into "does not match" and filters rows away while reporting success - or to throw. Throwing
/// is the only option that keeps the answer honest at that signature.
/// </para>
/// <para>
/// It is therefore expected, documented and catchable, and it carries the whole
/// <see cref="PinyinMatchResult"/> so a handler can log the reason and the inputs without
/// reconstructing them. Callers that would rather branch than catch should use
/// <c>TryMatch</c>, which never throws for unavailability, and the expression path already does
/// exactly that: <c>TryInvokeFromExpression</c> converts unavailability into a structured error, so
/// nothing of this type escapes into DataWindow filter evaluation.
/// </para>
/// <para>
/// Derived from <see cref="InvalidOperationException"/> because the object is validly constructed and
/// the operation is validly requested; it is the CONFIGURATION that does not permit it - which is the
/// standard meaning of that base type.
/// </para>
/// </remarks>
public sealed class PinyinMatchUnavailableException : InvalidOperationException
{
    /// <summary>
    /// Creates the exception from the unavailable result it reports.
    /// </summary>
    /// <param name="result">
    /// The unavailable result. Its <see cref="PinyinMatchResult.Diagnostic"/> becomes the message, so
    /// the exception text and the log text cannot drift apart.
    /// </param>
    public PinyinMatchUnavailableException(PinyinMatchResult result)
        : base(result.Diagnostic) => Result = result;

    /// <summary>
    /// Creates the exception with a message and no result. Present for the standard exception shape.
    /// </summary>
    /// <param name="message">The message.</param>
    public PinyinMatchUnavailableException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with a message and an inner exception. Present for the standard exception
    /// shape.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public PinyinMatchUnavailableException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates the exception with no message. Present for the standard exception shape.
    /// </summary>
    public PinyinMatchUnavailableException()
    {
    }

    /// <summary>
    /// The full result being reported, including the reason, the inputs and the flags.
    /// </summary>
    /// <remarks>
    /// Default-valued on the three standard constructors, whose
    /// <see cref="PinyinMatchResult.Outcome"/> is therefore
    /// <see cref="PinyinMatchOutcome.Unspecified"/>. Only the result-carrying constructor is used by
    /// this file.
    /// </remarks>
    public PinyinMatchResult Result { get; }

    /// <summary>
    /// The reason matching was unavailable, or
    /// <see cref="PinyinMatchUnavailableReason.Unspecified"/> when the exception was built without a
    /// result.
    /// </summary>
    public PinyinMatchUnavailableReason Reason => Result.UnavailableReason;
}

/// <summary>
/// The managed substitute for the native <c>pfwPinyinFirstLetterLike</c> DataWindow filter function.
/// </summary>
/// <remarks>
/// <para>
/// Implements everything the oracle documents - the three <c>PY_LIKE_*</c> flags and the folding and
/// equivalence they govern - and delegates everything the oracle keeps closed to the seams on
/// <see cref="PinyinMatchConfiguration"/>. Read this file's header for the substitute-versus-BLOCKED
/// reasoning; it is the design rationale for every choice below.
/// </para>
/// <para>
/// REACHED THROUGH EXPRESSION DISPATCH, not by a direct call from the drop-down search model. The
/// legacy builds a DataWindow filter string that names this function
/// [n_cst_dwsvc_dropdownsearch.sru:L323], so <see cref="ExpressionFunctionName"/> is the name the
/// expression evaluator registers and <see cref="TryInvokeFromExpression"/> is the entry point it
/// calls.
/// </para>
/// <para>
/// Immutable and stateless once constructed, so a single instance is safe to share across concurrent
/// requests provided the configured <see cref="IPinyinFirstLetterTable"/> is itself thread-safe, which
/// that interface requires.
/// </para>
/// </remarks>
public sealed class PinyinFirstLetterMatcher
{
    /// <summary>
    /// The name this function is callable by inside a DataWindow filter or computed-column expression.
    /// </summary>
    /// <remarks>
    /// Spelled exactly as the legacy emits it into the filter string
    /// [n_cst_dwsvc_dropdownsearch.sru:L323] rather than as the native export's alias
    /// <c>pfwPinyinFirstLetterLike</c> [pinyinfirstletterlike.srf:L7-L8]. The expression evaluator
    /// resolves the PowerScript-visible name, not the binary's, and DataWindow expression function
    /// names are matched case-insensitively - see <see cref="IsExpressionFunctionName"/>.
    /// </remarks>
    public const string ExpressionFunctionName = "PinyinFirstLetterLike";

    /// <summary>
    /// The flag set the one in-repository call site passes: all three bits, that is <c>7</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// COMPOSED FROM THE NAMED CONSTANTS, NEVER WRITTEN AS 7. The oracle documents the bits
    /// [enums.sru:L1147-L1149] and the call site writes the literal
    /// [n_cst_dwsvc_dropdownsearch.sru:L323]; composing them here PROVES the literal rather than
    /// restating it, and is what the shared constant catalogue's own note on this region asks of this
    /// file.
    /// </para>
    /// <para>
    /// The cast is the deliberate 32-bit narrowing. The catalogue declares these as <c>long</c> to
    /// match PowerBuilder's <c>long</c>, but this function's parameter is PowerBuilder
    /// <c>unsignedlong</c>, which is 32 bits wide - so <see cref="uint"/>, never <see cref="ulong"/>.
    /// It is a compile-time constant conversion of the value 7, so nothing is lost and nothing is
    /// checked at run time.
    /// </para>
    /// <para>
    /// THIS IS NOT THE TWO-ARGUMENT OVERLOAD'S DEFAULT. What the call site passes and what the native
    /// binary substitutes for an omitted argument are different unknowns; see
    /// <see cref="PinyinMatchConfiguration.CharacterizedDefaultFlags"/>.
    /// </para>
    /// </remarks>
    public const uint LegacyCallSiteFlags = (uint)(
        Enums.PY_LIKE_IGNORE_CASE | Enums.PY_LIKE_IGNORE_WIDTH | Enums.PY_LIKE_FUZZY_SOUND);

    /// <summary>
    /// The lowest code point of the Unicode halfwidth-and-fullwidth block's fullwidth ASCII range,
    /// <c>U+FF01</c> (fullwidth exclamation mark).
    /// </summary>
    private const int FullWidthAsciiFirst = 0xFF01;

    /// <summary>
    /// The highest code point of that range, <c>U+FF5E</c> (fullwidth tilde).
    /// </summary>
    private const int FullWidthAsciiLast = 0xFF5E;

    /// <summary>
    /// The fixed distance from a fullwidth ASCII form to its halfwidth equivalent.
    /// </summary>
    private const int FullWidthToHalfWidthOffset = 0xFEE0;

    /// <summary>
    /// The ideographic space, <c>U+3000</c>, whose halfwidth equivalent is the ordinary space and
    /// which lies outside the contiguous range above.
    /// </summary>
    private const int IdeographicSpace = 0x3000;

    /// <summary>
    /// A hard ceiling on how many polyphonic reading combinations one text may expand to.
    /// </summary>
    /// <remarks>
    /// Polyphonic characters multiply: a text of <c>n</c> characters each with <c>k</c> readings has
    /// <c>k</c> to the power <c>n</c> candidate initial strings. Without a bound, a long string of
    /// multi-reading characters would exhaust memory inside a per-row filter predicate. The ceiling
    /// converts that into a reported <see cref="PinyinMatchUnavailableReason.LookupTableIncomplete"/>
    /// rather than an outage, which is consistent with the rule that this file never guesses: it
    /// declines instead of truncating the candidate set, because truncating would silently drop
    /// readings and turn a match into a non-match.
    /// </remarks>
    private const int MaximumReadingCombinations = 4096;

    /// <summary>
    /// Creates a matcher over the supplied configuration.
    /// </summary>
    /// <param name="configuration">
    /// The seams. Pass <see cref="PinyinMatchConfiguration.Blocked"/> for the shipped, BLOCKED state.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    public PinyinFirstLetterMatcher(PinyinMatchConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Configuration = configuration;
    }

    /// <summary>
    /// A matcher in the BLOCKED state: every match attempt reports
    /// <see cref="PinyinMatchOutcome.Unavailable"/>.
    /// </summary>
    /// <remarks>
    /// The correct instance to register with the expression evaluator until the behavioural oracle has
    /// been exercised. It is not a null object and not a stub: it answers every call with a specific,
    /// logged reason naming the characterization work that would unblock it.
    /// </remarks>
    public static PinyinFirstLetterMatcher Blocked { get; } = new(PinyinMatchConfiguration.Blocked);

    /// <summary>The seams this matcher was built over. Never null.</summary>
    public PinyinMatchConfiguration Configuration { get; }

    /// <summary>
    /// Whether this matcher can decide a match at all, that is whether a table and a characterized
    /// strategy are both configured.
    /// </summary>
    /// <remarks>
    /// Lets a caller report the gap once at startup instead of discovering it per row, and lets a
    /// health or capability projection state plainly that the pinyin filter is unavailable. It does not
    /// cover the two-argument overload's separate default-flag requirement, which
    /// <see cref="IsDefaultFlagSetCharacterized"/> reports.
    /// </remarks>
    public bool CanMatch =>
        Configuration.Table is not null
        && Configuration.Strategy != PinyinMatchStrategy.NotCharacterized;

    /// <summary>
    /// Whether the two-argument overload's effective flag set has been characterized.
    /// </summary>
    /// <remarks>
    /// Reported separately from <see cref="CanMatch"/> because it blocks only the two-argument form.
    /// The three-argument form states its flags explicitly and is unaffected.
    /// </remarks>
    public bool IsDefaultFlagSetCharacterized => Configuration.CharacterizedDefaultFlags is not null;

    /// <summary>
    /// Tests whether <c>PY_LIKE_IGNORE_CASE</c> is set in a flag mask.
    /// </summary>
    /// <param name="flags">The mask.</param>
    /// <returns><see langword="true"/> when case is to be ignored.</returns>
    /// <remarks>
    /// Uses the ported bit primitive so that flag testing here is the same operation the legacy
    /// performs at its own bit tests, and consumes the constant by name from the shared catalogue
    /// [enums.sru:L1147]. The cast is the same documented 32-bit narrowing as
    /// <see cref="LegacyCallSiteFlags"/>.
    /// </remarks>
    public static bool IgnoresCase(uint flags) =>
        Bits.BitTest(flags, (uint)Enums.PY_LIKE_IGNORE_CASE);

    /// <summary>
    /// Tests whether <c>PY_LIKE_IGNORE_WIDTH</c> is set in a flag mask.
    /// </summary>
    /// <param name="flags">The mask.</param>
    /// <returns>
    /// <see langword="true"/> when fullwidth and halfwidth forms are to be treated alike.
    /// [enums.sru:L1148]
    /// </returns>
    public static bool IgnoresWidth(uint flags) =>
        Bits.BitTest(flags, (uint)Enums.PY_LIKE_IGNORE_WIDTH);

    /// <summary>
    /// Tests whether <c>PY_LIKE_FUZZY_SOUND</c> is set in a flag mask.
    /// </summary>
    /// <param name="flags">The mask.</param>
    /// <returns>
    /// <see langword="true"/> when fuzzy pronunciation equivalence applies. [enums.sru:L1149]
    /// </returns>
    public static bool UsesFuzzySound(uint flags) =>
        Bits.BitTest(flags, (uint)Enums.PY_LIKE_FUZZY_SOUND);

    /// <summary>
    /// Tests whether a name from an expression is this function's name.
    /// </summary>
    /// <param name="name">The function name as it appeared in the expression.</param>
    /// <returns><see langword="true"/> when it names this function.</returns>
    /// <remarks>
    /// Case-insensitive and ordinal. DataWindow expression function names are not case-sensitive, and
    /// ordinal comparison is used rather than a culture-aware one so that no ambient culture can change
    /// which function an expression resolves to.
    /// </remarks>
    public static bool IsExpressionFunctionName(string? name) =>
        string.Equals(name, ExpressionFunctionName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Narrows a flag value carried as a 64-bit integer down to this function's 32-bit parameter.
    /// </summary>
    /// <param name="flags">
    /// The mask as the service's options type and the published contract carry it - <c>long</c> in
    /// configuration and <c>uint64</c> on the wire.
    /// </param>
    /// <returns>The mask as this function's parameter type.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="flags"/> is negative or does not fit in 32 bits.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE ONE PLACE THE WIDTH MISMATCH IS RESOLVED, and it is explicit so that the mismatch cannot be
    /// papered over by an implicit conversion somewhere else. Three widths meet at this boundary and
    /// all three are correct in their own context: the shared constant catalogue declares the bits as
    /// <c>long</c> to match PowerBuilder's <c>long</c>, the published contract carries
    /// <c>uint64</c>, and THIS function's parameter is PowerBuilder <c>unsignedlong</c>, which is 32
    /// bits.
    /// </para>
    /// <para>
    /// It throws rather than truncating, because a value that does not fit was never a legal flag mask
    /// and silently discarding its high bits would change which comparison ran.
    /// </para>
    /// </remarks>
    public static uint ToFlags(long flags)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(flags);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(flags, uint.MaxValue);

        return (uint)flags;
    }

    /// <summary>
    /// Folds one character to its halfwidth equivalent, as <c>PY_LIKE_IGNORE_WIDTH</c> requires.
    /// </summary>
    /// <param name="value">The character to fold.</param>
    /// <returns>
    /// The halfwidth equivalent when <paramref name="value"/> is a fullwidth ASCII form or the
    /// ideographic space; otherwise <paramref name="value"/> unchanged.
    /// </returns>
    /// <remarks>
    /// <para>
    /// DELIBERATELY NOT <see cref="string.Normalize(NormalizationForm)"/> WITH
    /// <see cref="NormalizationForm.FormKC"/>, and the reason is behavioural rather than stylistic.
    /// Compatibility normalisation would fold this range, but it would ALSO rewrite a great deal more -
    /// CJK compatibility ideographs, halfwidth katakana, circled and parenthesised forms, ligatures -
    /// none of which is what 全角/半角 means and none of which the flag asks for. Applying it would
    /// change results for inputs the flag says nothing about, which constraint C-B forbids.
    /// </para>
    /// <para>
    /// So the fold is exactly the fullwidth-to-halfwidth relation and nothing else: the contiguous
    /// <c>U+FF01</c> to <c>U+FF5E</c> range maps onto <c>U+0021</c> to <c>U+007E</c> by a fixed
    /// offset, and the ideographic space maps to the ordinary space. That relation is defined by the
    /// Unicode block itself, so this is a documented mapping rather than an invented one.
    /// </para>
    /// </remarks>
    public static char FoldWidth(char value)
    {
        if (value >= FullWidthAsciiFirst && value <= FullWidthAsciiLast)
        {
            return (char)(value - FullWidthToHalfWidthOffset);
        }

        return value == IdeographicSpace ? ' ' : value;
    }

    // ==============================================================================================
    //  THE TWO PARITY OVERLOADS
    //
    //  These reproduce the oracle's prototype pair exactly [pinyinfirstletterlike.srf:L7-L8]: same
    //  name, same parameter names, same order, `bool` return, and a 32-bit unsigned flag argument.
    //
    //  THEY ARE TWO OVERLOADS AND NOT ONE METHOD WITH AN OPTIONAL PARAMETER. That is a contract, not a
    //  formatting choice, for two reasons. The legacy declares two prototypes, and the arity is
    //  observable: an optional parameter would let a caller omit the argument and silently receive
    //  whatever default this file nominated, whereas the two-argument overload here is BLOCKED
    //  precisely because that default is unknown. Collapsing them would delete the distinction the
    //  BLOCKED state depends on.
    //
    //  `readonly string` maps to a plain by-value `string` rather than `in string`. PowerBuilder's
    //  `readonly` forbids assignment to the parameter, which is exactly what a by-value immutable
    //  reference already guarantees; `in` would add a needless indirection and, on a reference type,
    //  express nothing further.
    // ==============================================================================================

    /// <summary>
    /// Tests whether <paramref name="text"/> matches the pinyin first-letter pattern
    /// <paramref name="py"/>, using the characterized default flag set.
    /// </summary>
    /// <param name="text">The text to search, typically containing Han characters.</param>
    /// <param name="py">
    /// The pinyin-initial pattern. Arrives already lower-cased on the legacy path
    /// [n_cst_dwsvc_dropdownsearch.sru:L316]; this method does NOT lower-case it again.
    /// </param>
    /// <returns><see langword="true"/> when the text matches.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="PinyinMatchUnavailableException">
    /// Matching is unavailable - which for THIS overload includes the case that the effective default
    /// flag set has not been characterized, because both legacy prototypes alias one native export and
    /// the repository never states what the omitted argument becomes. Never returns
    /// <see langword="false"/> for an unavailable comparison.
    /// </exception>
    /// <remarks>
    /// Reproduces <c>PinyinFirstLetterLike(readonly string text, readonly string py)</c>
    /// [pinyinfirstletterlike.srf:L7]. Prefer <see cref="TryMatch(string?, string?)"/> to branch on
    /// availability instead of catching.
    /// </remarks>
    public bool PinyinFirstLetterLike(string text, string py)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(py);

        return Decide(TryMatch(text, py));
    }

    /// <summary>
    /// Tests whether <paramref name="text"/> matches the pinyin first-letter pattern
    /// <paramref name="py"/> under an explicit flag set.
    /// </summary>
    /// <param name="text">The text to search, typically containing Han characters.</param>
    /// <param name="py">
    /// The pinyin-initial pattern. Arrives already lower-cased on the legacy path
    /// [n_cst_dwsvc_dropdownsearch.sru:L316]; this method does NOT lower-case it again.
    /// </param>
    /// <param name="flags">
    /// A bit-or of <c>PY_LIKE_IGNORE_CASE</c>, <c>PY_LIKE_IGNORE_WIDTH</c> and
    /// <c>PY_LIKE_FUZZY_SOUND</c> [enums.sru:L1147-L1149]. The sole legacy call site passes all three,
    /// which <see cref="LegacyCallSiteFlags"/> composes.
    /// </param>
    /// <returns><see langword="true"/> when the text matches.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="text"/> or <paramref name="py"/> is null.
    /// </exception>
    /// <exception cref="PinyinMatchUnavailableException">
    /// Matching is unavailable. Never returns <see langword="false"/> for an unavailable comparison.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces
    /// <c>PinyinFirstLetterLike(readonly string text, readonly string py, readonly unsignedlong flags)</c>
    /// [pinyinfirstletterlike.srf:L8]. The parameter is <see cref="uint"/> because PowerBuilder's
    /// <c>unsignedlong</c> is 32 bits wide - it is NOT <see cref="ulong"/>, whose name coincides with
    /// the PowerBuilder type while its width does not.
    /// </para>
    /// <para>
    /// UNDEFINED BITS ARE NOT REJECTED. Only bits 1, 2 and 4 are documented, and what the binary does
    /// with any other bit is unknown, so a mask carrying one is neither an error nor a reason to
    /// refuse: the undocumented bits are ignored, exactly as an unrecognised bit in a mask
    /// conventionally is. Throwing would invent a validation the oracle does not perform.
    /// </para>
    /// </remarks>
    public bool PinyinFirstLetterLike(string text, string py, uint flags)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(py);

        return Decide(TryMatch(text, py, flags));
    }

    /// <summary>
    /// Attempts a match using the characterized default flag set, reporting availability rather than
    /// throwing.
    /// </summary>
    /// <param name="text">The text to search. Null is treated as empty.</param>
    /// <param name="py">The pinyin-initial pattern. Null is treated as empty.</param>
    /// <returns>
    /// A decided result, or an unavailable result carrying
    /// <see cref="PinyinMatchUnavailableReason.DefaultFlagsNotCharacterized"/> when the two-argument
    /// form's effective flags are still unknown.
    /// </returns>
    /// <remarks>
    /// The non-throwing counterpart of <see cref="PinyinFirstLetterLike(string, string)"/>. Null is
    /// tolerated here, rather than rejected as it is on the parity overloads, because this method's
    /// contract is to report rather than to police: a null reaching a filter predicate should produce a
    /// describable outcome, not an exception from inside expression evaluation.
    /// </remarks>
    public PinyinMatchResult TryMatch(string? text, string? py)
    {
        string subject = text ?? string.Empty;
        string pattern = py ?? string.Empty;

        // THE UNKNOWN DEFAULT IS REFUSED, NOT GUESSED. Reported before any other seam is consulted so
        // that the diagnostic names the arity-specific gap rather than a downstream one, and so the
        // reason cannot be mistaken for a missing table.
        if (Configuration.CharacterizedDefaultFlags is not uint effectiveFlags)
        {
            return PinyinMatchResult.Unavailable(
                PinyinMatchUnavailableReason.DefaultFlagsNotCharacterized,
                subject,
                pattern,
                0u,
                "Supply PinyinMatchConfiguration.CharacterizedDefaultFlags from a characterization "
                    + "recording, or call the three-argument overload with an explicit mask.");
        }

        return TryMatch(subject, pattern, effectiveFlags);
    }

    /// <summary>
    /// Attempts a match under an explicit flag set, reporting availability rather than throwing.
    /// </summary>
    /// <param name="text">The text to search. Null is treated as empty.</param>
    /// <param name="py">The pinyin-initial pattern. Null is treated as empty.</param>
    /// <param name="flags">The flag mask [enums.sru:L1147-L1149].</param>
    /// <returns>A decided result, or an unavailable result naming the unconfigured seam.</returns>
    /// <remarks>
    /// <para>
    /// THE ONLY PLACE A MATCH IS ACTUALLY DECIDED; every other entry point funnels here. The order of
    /// the checks is deliberate: the seams are tested before any work is done, so an unavailable
    /// deployment costs nothing per row and always reports the most specific reason available.
    /// </para>
    /// <para>
    /// Never throws. Availability is data, not an exceptional condition.
    /// </para>
    /// </remarks>
    public PinyinMatchResult TryMatch(string? text, string? py, uint flags)
    {
        string subject = text ?? string.Empty;
        string pattern = py ?? string.Empty;

        if (Configuration.Table is not IPinyinFirstLetterTable table)
        {
            return PinyinMatchResult.Unavailable(
                PinyinMatchUnavailableReason.LookupTableNotConfigured,
                subject,
                pattern,
                flags,
                "Load a characterization-derived table into PinyinMatchConfiguration.Table.");
        }

        if (Configuration.Strategy == PinyinMatchStrategy.NotCharacterized)
        {
            return PinyinMatchResult.Unavailable(
                PinyinMatchUnavailableReason.MatchStrategyNotCharacterized,
                subject,
                pattern,
                flags,
                Formatting.Sprintf(
                    "A table is present (source: {1}) but PinyinMatchConfiguration.Strategy is still "
                        + "NotCharacterized.",
                    table.SourceDescription));
        }

        if (!TryDeriveInitials(
                table, subject, flags, out ImmutableArray<string> candidates, out Rune missing))
        {
            return PinyinMatchResult.Unavailable(
                PinyinMatchUnavailableReason.LookupTableIncomplete,
                subject,
                pattern,
                flags,
                Formatting.Sprintf(
                    "Table '{1}' has no reading for U+{2} ('{3}'), or the text exceeds the {4} "
                        + "polyphonic-combination ceiling.",
                    table.SourceDescription,
                    missing.Value.ToString("X4", CultureInfo.InvariantCulture),
                    missing.ToString(),
                    MaximumReadingCombinations.ToString(CultureInfo.InvariantCulture)));
        }

        bool matched = MatchesAnyCandidate(candidates, pattern, flags);

        return PinyinMatchResult.Decided(matched, subject, pattern, flags, candidates);
    }

    /// <summary>
    /// The DataWindow expression evaluator's entry point for
    /// <c>PinyinFirstLetterLike(column,'pattern',flags)</c>.
    /// </summary>
    /// <param name="arguments">
    /// The evaluated argument list from the expression, in source order: the column's value, the
    /// pattern, and optionally the flag mask. Two or three entries.
    /// </param>
    /// <param name="result">The outcome, decided or unavailable, always populated.</param>
    /// <param name="error">
    /// When the return is <see langword="false"/>, the structured error to surface; otherwise
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the comparison ran - read <see cref="PinyinMatchResult.IsMatch"/>
    /// for the predicate's value. <see langword="false"/> when matching is unavailable.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THIS IS HOW THIS FILE IS ACTUALLY REACHED. The legacy does not call the function from
    /// PowerScript; it splices its name into a DataWindow filter string
    /// [n_cst_dwsvc_dropdownsearch.sru:L323] which the expression evaluator later evaluates. So the
    /// evaluator registers <see cref="ExpressionFunctionName"/> and calls this method, and the
    /// drop-down search model - which only builds the filter TEXT - never calls this class directly.
    /// </para>
    /// <para>
    /// IT DOES NOT THROW FOR UNAVAILABILITY, and that is the requirement this signature exists to
    /// satisfy. An exception escaping a filter predicate would abort the whole retrieval rather than
    /// reporting a gap in one function, so unavailability is converted here into an
    /// <see cref="ExpressionParseError"/> the caller can surface on the published contract. It also
    /// does not return a silent <see langword="false"/> predicate value: the boolean return reports
    /// AVAILABILITY, and the predicate's value lives on <paramref name="result"/> where it cannot be
    /// confused with it.
    /// </para>
    /// <para>
    /// A malformed argument list - wrong arity, or a flag argument that is not a non-negative integer -
    /// is reported the same way rather than thrown, because a filter expression is data assembled by
    /// string concatenation and a caller may legitimately have got it wrong. The flag argument is
    /// accepted from any integral form, since a DataWindow expression's numeric literal has no fixed
    /// managed type.
    /// </para>
    /// </remarks>
    public bool TryInvokeFromExpression(
        IReadOnlyList<object?> arguments,
        out PinyinMatchResult result,
        out ExpressionParseError? error)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count is not (2 or 3))
        {
            result = PinyinMatchResult.Unavailable(
                PinyinMatchUnavailableReason.Unspecified,
                string.Empty,
                string.Empty,
                0u,
                Formatting.Sprintf(
                    "{1} takes 2 or 3 arguments [pinyinfirstletterlike.srf:L7-L8] but the expression "
                        + "supplied {2}.",
                    ExpressionFunctionName,
                    arguments.Count.ToString(CultureInfo.InvariantCulture)));

            error = CreateUnavailableExpressionError(result);
            return false;
        }

        string subject = ToExpressionString(arguments[0]);
        string pattern = ToExpressionString(arguments[1]);

        if (arguments.Count == 2)
        {
            result = TryMatch(subject, pattern);
        }
        else if (TryReadFlagArgument(arguments[2], out uint flags))
        {
            result = TryMatch(subject, pattern, flags);
        }
        else
        {
            result = PinyinMatchResult.Unavailable(
                PinyinMatchUnavailableReason.Unspecified,
                subject,
                pattern,
                0u,
                Formatting.Sprintf(
                    "The third argument must be a non-negative 32-bit flag mask "
                        + "[enums.sru:L1147-L1149] but the expression supplied '{1}'.",
                    arguments[2] ?? "(null)"));
        }

        if (result.IsAvailable)
        {
            error = null;
            return true;
        }

        error = CreateUnavailableExpressionError(result);
        return false;
    }

    /// <summary>
    /// Projects an unavailable result onto this service's structured expression-error surface.
    /// </summary>
    /// <param name="result">The unavailable result.</param>
    /// <returns>The structured error, ready to project onto the published contract.</returns>
    /// <exception cref="ArgumentException"><paramref name="result"/> is not unavailable.</exception>
    /// <remarks>
    /// <para>
    /// CONSTRUCTED DIRECTLY RATHER THAN THROUGH THE 28-SITE FACTORIES, and that is required rather
    /// than merely preferred. <c>ParseErrorFormatter</c>'s factories accept only the 28 dialog sites
    /// the column-expression engine actually raises, and they stamp each site's legacy Chinese message
    /// text. Routing this error through one of them would claim the legacy raised it, which is false:
    /// the native function always has its table in process and has no unavailable state, so no legacy
    /// line can be cited for this condition.
    /// </para>
    /// <para>
    /// Hence <see cref="ExpressionErrorSite.Unspecified"/>, which that enumeration documents as "not
    /// one of the 28 legacy sites". The precedent is exact: the one other refactor-introduced error in
    /// this service - the cross-session foreign-variable narrowing - likewise carries no legacy
    /// locator, and for the same structural reason.
    /// </para>
    /// <para>
    /// The category is <see cref="ExpressionErrorCategory.Expression"/>, the general
    /// expression-evaluation failure, because from the evaluator's position that is exactly what has
    /// happened: a function in the expression could not produce a value. It is deliberately NOT
    /// <see cref="ExpressionErrorCategory.ForeignReferenceBlocked"/>, which is reserved for the
    /// foreign-variable narrowing - reusing it would corrupt that narrowing's diagnostics and conflate
    /// two unrelated gaps. NO NEW CATEGORY IS ADDED: that enumeration mirrors the published contract
    /// name for name and value for value, and adding a member would break the mirror and breach
    /// constraint C-B.
    /// </para>
    /// <para>
    /// <see cref="ExpressionParseError.Localized"/> is false and the localization category is zero,
    /// matching every one of the 28 legacy sites - those messages are hardcoded and do not route
    /// through the localization surface, an inconsistency this refactor preserves rather than
    /// harmonises. The return code is <c>E_NO_IMPLEMENTATION</c>, which is what an operation that
    /// exists but has no available implementation means in the ported code algebra.
    /// </para>
    /// </remarks>
    public static ExpressionParseError CreateUnavailableExpressionError(in PinyinMatchResult result)
    {
        if (result.Outcome != PinyinMatchOutcome.Unavailable)
        {
            throw new ArgumentException(
                "Only an unavailable result becomes a structured error. A decided match or non-match "
                    + "is a VALUE and must be returned to the expression, never reported as a fault.",
                nameof(result));
        }

        const string template = "{1}";

        return new ExpressionParseError
        {
            // No legacy locator, and none is possible - see the remarks above.
            Site = ExpressionErrorSite.Unspecified,
            Family = ExpressionErrorFamily.PlainMessage,
            Category = ExpressionErrorCategory.Expression,
            // The severity of every one of the 28 legacy sites, kept for consistency with them.
            Severity = ExpressionErrorSeverity.StopSign,
            Title = ExpressionErrorCatalog.LegacyTitle,
            Text = result.Diagnostic,
            // Hard-wired false and zero, matching the preserved non-localization of the 28 sites.
            Localized = false,
            LocalizationCategory = 0,
            FormatTemplate = template,
            FormatArguments = [result.Diagnostic],
            ReturnCode = RetCode.E_NO_IMPLEMENTATION,
            // A plain-message error carries no expression, position, convention or marker. All four
            // stay absent rather than being filled with a plausible default.
            Expression = null,
            CaretPosition = null,
            CaretConvention = CaretPositionConvention.Unspecified,
            RenderedMarker = null,
        };
    }

    /// <summary>
    /// Converts a result into the <see cref="bool"/> the parity overloads must return, or throws.
    /// </summary>
    /// <param name="result">The result to reduce.</param>
    /// <returns>Whether the text matched.</returns>
    /// <exception cref="PinyinMatchUnavailableException">The comparison did not run.</exception>
    /// <remarks>
    /// The single place the boolean signature's inability to express a third state is resolved, so the
    /// decision is made once rather than at each overload. Throwing here is what stops an unavailable
    /// comparison from being reported as a non-match.
    /// </remarks>
    private static bool Decide(PinyinMatchResult result) => result.Outcome switch
    {
        PinyinMatchOutcome.Match => true,
        PinyinMatchOutcome.NoMatch => false,
        _ => throw new PinyinMatchUnavailableException(result),
    };

    /// <summary>
    /// Renders an expression argument as the string this function compares.
    /// </summary>
    /// <param name="value">The evaluated argument.</param>
    /// <returns>The argument as text; the empty string when null.</returns>
    /// <remarks>
    /// The invariant culture is used for a formattable value so that no ambient culture can change
    /// which rows a filter selects. A null column value becomes the empty string, which is how a
    /// DataWindow expression treats a null in a string context.
    /// </remarks>
    private static string ToExpressionString(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>
    /// Reads the flag argument from an expression, accepting any integral or textual integer form.
    /// </summary>
    /// <param name="value">The evaluated third argument.</param>
    /// <param name="flags">The mask on success; zero otherwise.</param>
    /// <returns><see langword="true"/> when the value is a legal 32-bit mask.</returns>
    /// <remarks>
    /// A DataWindow expression's numeric literal has no fixed managed type - the evaluator may hand
    /// over any integral width, a decimal, or the digits as text - so the accepted forms are broad
    /// while the accepted RANGE is exactly what the parameter can hold. A negative or oversized value
    /// is refused rather than truncated, for the reason given on <see cref="ToFlags"/>.
    /// </remarks>
    private static bool TryReadFlagArgument(object? value, out uint flags)
    {
        flags = 0u;

        switch (value)
        {
            case null:
                return false;

            case uint direct:
                flags = direct;
                return true;

            case string text:
                return uint.TryParse(
                    text.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out flags);

            case IConvertible convertible
                when value is byte or sbyte or short or ushort or int or long or ulong
                    or float or double or decimal:
                try
                {
                    decimal numeric = convertible.ToDecimal(CultureInfo.InvariantCulture);

                    if (numeric != decimal.Truncate(numeric)
                        || numeric < 0m
                        || numeric > uint.MaxValue)
                    {
                        return false;
                    }

                    flags = (uint)numeric;
                    return true;
                }
                catch (InvalidCastException)
                {
                    // A conversion the type advertised but cannot perform. Reported as a malformed
                    // argument, which is what it is, rather than propagated out of a filter predicate.
                    return false;
                }
                catch (OverflowException)
                {
                    return false;
                }

            default:
                return false;
        }
    }

    /// <summary>
    /// Reduces the text to every candidate pinyin-initial string its readings allow.
    /// </summary>
    /// <param name="table">The configured lookup table.</param>
    /// <param name="text">The text to reduce.</param>
    /// <param name="flags">The flag mask, which governs width folding of the input.</param>
    /// <param name="candidates">
    /// On success, one candidate per combination of polyphonic readings; a single empty candidate for
    /// empty text.
    /// </param>
    /// <param name="missing">
    /// On failure, the character the table has no reading for, or the replacement character when the
    /// failure was the combination ceiling.
    /// </param>
    /// <returns><see langword="true"/> when every character was reduced.</returns>
    /// <remarks>
    /// <para>
    /// THE CARTESIAN EXPANSION IS THE POLYPHONIC-CHARACTER MECHANISM. A character with several readings
    /// contributes several branches, so a text of such characters yields several candidate initial
    /// strings and a match against ANY of them is a match - which is what supporting 多音字 means.
    /// logfile.md:L376 is what establishes that the table is one-to-many; the changelog fixes only that
    /// shape, never the readings (plan risk R8).
    /// </para>
    /// <para>
    /// ITERATION IS BY <see cref="Rune"/>, not by <see cref="char"/>, so a supplementary-plane
    /// character is offered to the table as ONE character rather than as two surrogate halves that
    /// could never match a reading. Whether the native binary does the same is itself unverified, and
    /// the difference is observable only outside the basic multilingual plane.
    /// </para>
    /// <para>
    /// AN UNCOVERED CHARACTER FAILS THE WHOLE DERIVATION rather than being skipped or passed through as
    /// itself. Both of those would be approximations: skipping shortens the initial string and changes
    /// what a prefix or whole match means, and passing through invents a reading. Failing produces a
    /// reported gap instead, which is the rule this file exists to enforce.
    /// </para>
    /// </remarks>
    private static bool TryDeriveInitials(
        IPinyinFirstLetterTable table,
        string text,
        uint flags,
        out ImmutableArray<string> candidates,
        out Rune missing)
    {
        missing = Rune.ReplacementChar;

        // A single empty candidate, not an empty candidate set: empty text HAS a reduction, and it is
        // the empty string. An empty set would mean "no reduction exists", which is a different claim.
        if (text.Length == 0)
        {
            candidates = [string.Empty];
            return true;
        }

        bool foldWidth = IgnoresWidth(flags);
        List<StringBuilder> branches = [new StringBuilder(text.Length)];

        foreach (Rune rune in text.EnumerateRunes())
        {
            Rune subject = foldWidth ? FoldWidthRune(rune) : rune;

            if (!table.TryGetFirstLetters(subject, out ImmutableArray<char> letters)
                || letters.IsDefaultOrEmpty)
            {
                candidates = [];
                missing = subject;
                return false;
            }

            if (branches.Count * letters.Length > MaximumReadingCombinations)
            {
                candidates = [];
                missing = subject;
                return false;
            }

            branches = Extend(branches, letters);
        }

        ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>(branches.Count);

        foreach (StringBuilder branch in branches)
        {
            builder.Add(branch.ToString());
        }

        candidates = builder.ToImmutable();
        return true;
    }

    /// <summary>
    /// Extends every branch by every reading of the next character.
    /// </summary>
    /// <param name="branches">The partial candidates so far.</param>
    /// <param name="letters">The readings of the next character.</param>
    /// <returns>The extended branches.</returns>
    /// <remarks>
    /// The single-reading case is handled in place, without allocating a new list or copying any
    /// builder, because it is overwhelmingly the common one: most characters have exactly one reading,
    /// so the expansion machinery costs nothing on ordinary input.
    /// </remarks>
    private static List<StringBuilder> Extend(
        List<StringBuilder> branches,
        ImmutableArray<char> letters)
    {
        if (letters.Length == 1)
        {
            foreach (StringBuilder branch in branches)
            {
                _ = branch.Append(letters[0]);
            }

            return branches;
        }

        List<StringBuilder> extended = new(branches.Count * letters.Length);

        foreach (StringBuilder branch in branches)
        {
            string prefix = branch.ToString();

            foreach (char letter in letters)
            {
                extended.Add(new StringBuilder(prefix.Length + 1).Append(prefix).Append(letter));
            }
        }

        return extended;
    }

    /// <summary>
    /// Folds a rune to its halfwidth equivalent, for the width-folding flag.
    /// </summary>
    /// <param name="value">The rune to fold.</param>
    /// <returns>
    /// The folded rune, or <paramref name="value"/> unchanged when it is not a fullwidth ASCII form.
    /// </returns>
    /// <remarks>
    /// A thin bridge to <see cref="FoldWidth(char)"/> so the fold is defined in exactly one place.
    /// Every fullwidth ASCII form and the ideographic space lie in the basic multilingual plane, so a
    /// supplementary-plane rune is never affected and is returned untouched.
    /// </remarks>
    private static Rune FoldWidthRune(Rune value)
    {
        if (!value.IsBmp)
        {
            return value;
        }

        char folded = FoldWidth((char)value.Value);

        return folded == value.Value ? value : new Rune(folded);
    }

    /// <summary>
    /// Tests the pattern against every candidate initial string.
    /// </summary>
    /// <param name="candidates">The candidate reductions of the text.</param>
    /// <param name="pattern">The pattern as supplied.</param>
    /// <param name="flags">The flag mask.</param>
    /// <returns><see langword="true"/> when any candidate matches.</returns>
    /// <remarks>
    /// ANY candidate matching is a match, which is the polyphonic rule: a character's several readings
    /// are alternatives, not conjuncts, so a text matches if it matches under some assignment of
    /// readings.
    /// </remarks>
    private bool MatchesAnyCandidate(
        ImmutableArray<string> candidates,
        string pattern,
        uint flags)
    {
        string subject = IgnoresWidth(flags) ? FoldWidth(pattern) : pattern;

        foreach (string candidate in candidates)
        {
            if (Matches(candidate, subject, flags))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Applies the configured strategy to one candidate.
    /// </summary>
    /// <param name="initials">One candidate reduction of the text.</param>
    /// <param name="pattern">The pattern, already width-folded when the flag asks for it.</param>
    /// <param name="flags">The flag mask.</param>
    /// <returns><see langword="true"/> when this candidate matches.</returns>
    /// <remarks>
    /// <para>
    /// The three arms are the candidate readings of "Like" that a characterization run must choose
    /// between; <see cref="PinyinMatchStrategy.NotCharacterized"/> cannot reach here, because
    /// <see cref="TryMatch(string?, string?, uint)"/> reports it unavailable before any comparison is
    /// attempted. The final arm is therefore unreachable for the declared members and exists so that a
    /// member added later fails closed rather than silently matching.
    /// </para>
    /// <para>
    /// AN EMPTY PATTERN IS NOT SPECIAL-CASED. Under a prefix or substring reading every string starts
    /// with and contains the empty string, so an empty pattern matches; under a whole reading it
    /// matches only an empty reduction. That falls out of the relation rather than being decided here,
    /// which is why no separate rule is written for it - and the legacy never reaches this case anyway,
    /// because its call site is guarded by a non-empty test [n_cst_dwsvc_dropdownsearch.sru:L314].
    /// </para>
    /// </remarks>
    private bool Matches(string initials, string pattern, uint flags)
    {
        return Configuration.Strategy switch
        {
            PinyinMatchStrategy.Prefix => IsPrefixAt(initials, pattern, 0, flags),
            PinyinMatchStrategy.Substring => ContainsAt(initials, pattern, flags),
            PinyinMatchStrategy.Whole =>
                initials.Length == pattern.Length && IsPrefixAt(initials, pattern, 0, flags),
            _ => false,
        };
    }

    /// <summary>
    /// Tests whether the pattern occurs at any offset within the candidate.
    /// </summary>
    /// <param name="initials">The candidate reduction.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flag mask.</param>
    /// <returns><see langword="true"/> when the pattern occurs.</returns>
    /// <remarks>
    /// A plain scan rather than <see cref="string.Contains(string, StringComparison)"/>, because the
    /// per-character comparison is not string equality: under the fuzzy-sound flag it is an equivalence
    /// relation, which no <see cref="StringComparison"/> can express. The pattern is short - a user's
    /// typed search term - so the scan's cost is not a concern, and correctness here is not negotiable.
    /// </remarks>
    private bool ContainsAt(string initials, string pattern, uint flags)
    {
        if (pattern.Length == 0)
        {
            return true;
        }

        int last = initials.Length - pattern.Length;

        for (int offset = 0; offset <= last; offset++)
        {
            if (IsPrefixAt(initials, pattern, offset, flags))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tests whether the pattern matches the candidate starting at an offset.
    /// </summary>
    /// <param name="initials">The candidate reduction.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="offset">Where in the candidate to begin.</param>
    /// <param name="flags">The flag mask.</param>
    /// <returns><see langword="true"/> when every pattern character matches.</returns>
    /// <remarks>
    /// THE ONE PLACE THE THREE FLAGS COMBINE, which is why they are applied here rather than by
    /// rewriting either string up front. Width folding has already been applied to both sides; case
    /// folding and fuzzy-sound equivalence are applied per character pair, because fuzzy equivalence is
    /// a relation between two characters and cannot be expressed as a normalisation of either one
    /// alone.
    /// </remarks>
    private bool IsPrefixAt(string initials, string pattern, int offset, uint flags)
    {
        if (offset + pattern.Length > initials.Length)
        {
            return false;
        }

        bool ignoreCase = IgnoresCase(flags);
        bool fuzzy = UsesFuzzySound(flags);

        for (int index = 0; index < pattern.Length; index++)
        {
            char expected = pattern[index];
            char actual = initials[offset + index];

            if (!MatchesCharacter(actual, expected, ignoreCase, fuzzy))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Tests one character pair under the case and fuzzy-sound flags.
    /// </summary>
    /// <param name="actual">The character from the derived initials.</param>
    /// <param name="expected">The character from the pattern.</param>
    /// <param name="ignoreCase">Whether <c>PY_LIKE_IGNORE_CASE</c> is set.</param>
    /// <param name="fuzzy">Whether <c>PY_LIKE_FUZZY_SOUND</c> is set.</param>
    /// <returns><see langword="true"/> when the two characters match.</returns>
    /// <remarks>
    /// <para>
    /// EACH FLAG IS INDEPENDENTLY OBSERVABLE HERE, which is what makes a matrix over all eight
    /// combinations meaningful. With neither flag the test is exact equality. With case only it is
    /// case-insensitive equality. With fuzzy only it is the equivalence relation, which is reflexive
    /// and therefore still matches identical characters. With both, the relation is applied to
    /// already-case-folded characters.
    /// </para>
    /// <para>
    /// FOLDING IS INVARIANT, NOT CULTURE-AWARE, and deliberately so: a filter must select the same rows
    /// regardless of the ambient culture of the thread that happens to evaluate it. This is not an added
    /// culture policy - it is the absence of one, which is what the flag's plain reading requires.
    /// </para>
    /// </remarks>
    private bool MatchesCharacter(char actual, char expected, bool ignoreCase, bool fuzzy)
    {
        char left = ignoreCase ? char.ToLowerInvariant(actual) : actual;
        char right = ignoreCase ? char.ToLowerInvariant(expected) : expected;

        if (left == right)
        {
            return true;
        }

        return fuzzy && Configuration.FuzzySound.AreEquivalent(left, right);
    }

    /// <summary>
    /// Folds every character of a string to its halfwidth equivalent.
    /// </summary>
    /// <param name="value">The string to fold.</param>
    /// <returns>
    /// The folded string, or <paramref name="value"/> itself when nothing needed folding.
    /// </returns>
    /// <remarks>
    /// Returns the original instance when no character changes, so the overwhelmingly common
    /// all-halfwidth pattern - a Latin search term - allocates nothing. Operates per UTF-16 unit rather
    /// than per rune because every foldable form is in the basic multilingual plane, so a surrogate half
    /// can never be mistaken for one.
    /// </remarks>
    private static string FoldWidth(string value)
    {
        int first = -1;

        for (int index = 0; index < value.Length; index++)
        {
            if (FoldWidth(value[index]) != value[index])
            {
                first = index;
                break;
            }
        }

        if (first < 0)
        {
            return value;
        }

        return string.Create(value.Length, (value, first), static (destination, state) =>
        {
            (string source, int from) = state;

            source.AsSpan(0, from).CopyTo(destination);

            for (int index = from; index < source.Length; index++)
            {
                destination[index] = FoldWidth(source[index]);
            }
        });
    }
}
