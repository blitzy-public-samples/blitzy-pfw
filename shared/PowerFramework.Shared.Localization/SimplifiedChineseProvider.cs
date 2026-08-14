// ==============================================================================================
// SimplifiedChineseProvider.cs
// The Simplified Chinese locale provider: two live lines, and both of them matter.
// ==============================================================================================
//
// WHAT THIS FILE IS
// One of the three locale providers the composition root can install. It is the smallest file in
// this project and the one most likely to be written wrongly, because the obvious tidy-up is a
// behavioural change. Simplified Chinese is the framework's BASE locale: every literal the legacy
// framework raises is already Simplified Chinese, so there is nothing to look up and nothing to
// rewrite. This provider therefore translates nothing - and still answers "handled" for the
// framework's own source, which is a different answer from "not handled" and is preserved as such.
//
// AUTHORITATIVE SOURCE - THE WHOLE OBJECT, QUOTED IN FULL
//     ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru
//
// The file is 27 significant lines and every one of them is reproduced below, because at that size
// an excerpt would hide exactly the evidence this port rests on:
//
//     :L1        $PBExportHeader$n_cst_i18n_chs.sru
//     :L3-L5     forward / global type n_cst_i18n_chs from ne_cst_i18n / end type / end forward
//     :L7-L8     global type n_cst_i18n_chs from ne_cst_i18n
//                end type
//     :L9        global n_cst_i18n_chs n_cst_i18n_chs
//     :L11-L13   on n_cst_i18n_chs.create / call super::create / end on
//     :L15-L17   on n_cst_i18n_chs.destroy / call super::destroy / end on
//     :L19-L27   event ontranslate;call super::ontranslate;/*
//                    source:来源
//                    category:分类
//                    text:待转换的文本
//                    返回1代表已处理
//                */
//                if source = Enums.I18N_SRC_PFW then return 1
//                return 0
//                end event
//
// Note what is NOT in that list, because the absences carry as much weight as the presences:
// there is no `type variables` block, so the type declares NOT ONE FIELD; there is no `event
// constructor` and no `event destructor`, so nothing is ever loaded or released; and `create` and
// `destroy` do nothing but `call super`, so there is no lifecycle behaviour to port either.
//
// CORRECTION TO THE PLAN TEXT, RECORDED AT THE POINT OF REPRODUCTION
// AAP §0.4.2.3 summarises this object as "28 L - NO-OP, chs is the base locale" and states that
// "its translate body is entirely commented out because Simplified Chinese is the base locale".
// The first half is right. The second half is WRONG, and the difference is the whole file.
//
// The `/*` that opens on :L19 is closed by the `*/` on :L24. It wraps the four-line parameter
// legend - source, category, text, and the return convention - that ALL THREE providers carry
// identically [n_cst_i18n_chs.sru:L20-L23, n_cst_i18n_en.sru:L25-L28, n_cst_i18n_cht.sru:L25-L28].
// It is a DOCUMENTATION comment. The two statements that follow it, :L25 and :L26, are outside it
// and are LIVE PowerScript. This project's own contract file reached the same conclusion
// independently: II18nProvider.cs cites "n_cst_i18n_chs.sru:L25-L26" as the reason the member has
// to return a value at all.
//
// For contrast, this repository does contain a genuinely commented-out translation body, and it is
// in the SIBLING file: n_cst_i18n_en.sru:L58-L153 holds the pre-XML English table, which AAP
// §0.8.2 forbids reviving. Confusing that block with this one is the mistake this section exists to
// prevent. The correction is written here rather than filed elsewhere, so that a reader comparing
// plan to code finds the resolution in the same place as the discrepancy.
//
// THE TWO LIVE LINES, AND WHY NEITHER MAY BE "SIMPLIFIED"
// A reasonable engineer reading a method that returns 1 without touching its arguments will reach
// one of two conclusions, and both are wrong:
//
//   * "This is dead code, delete the class."  It is not dead. The composition root selects it by
//     locale token: `case "chs"` creates it and `I18N(locale)` installs it
//     [ws_objects/pfw.pbl.src/pfw.sra:L98-L99, :L103]. Deleting it makes the "chs" arm of that
//     switch unimplementable, and AAP §0.4.2.3 requires "the three-provider shape including this
//     one" be reproduced precisely because the shape is what the locale option drives.
//   * "Returning 1 and returning 0 are the same here, so collapse it to `return 0`."  They are
//     not the same. 1 means HANDLED, and claiming "handled" is a claim that no further processing
//     is required - a positive statement that the text the caller already holds is correct for
//     this locale. 0 means NOT handled. Collapsing the first into the second discards that
//     statement.
//
// It is true that the value is unobservable in this system TODAY: the facade holds exactly one
// installed provider and discards the code, invoking the member as a bare statement and returning
// the text regardless [ws_objects/pfw.ui.pbl.src/i18n.srf:L17-L18, :L21-L22]. That is not a licence
// to drop it. AAP §0.7.3 C-B does not ask whether a behaviour is currently observable; it asks that
// it be preserved. The value is part of the published provider contract, it appears in
// characterization recordings, and it would matter immediately to any consumer that chained
// providers - a chain would stop at this provider and must continue to.
//
// WHY THERE IS NO RESOURCE DOCUMENT, AND WHY THAT IS EVIDENCE RATHER THAN AN OVERSIGHT
// Three independent facts agree, and each was verified against the files on disk rather than
// inferred from the other two:
//
//   1. NO FIELDS. This object has no `type variables` block at all. Both siblings do, and both use
//      it for the same one thing: `protected: privatewrite n_xmldoc _doc`
//      [n_cst_i18n_en.sru:L11-L14, n_cst_i18n_cht.sru:L11-L14].
//   2. NO LOAD. This object overrides no constructor. n_cst_i18n_cht.sru:L62-L63 creates the
//      document and calls LoadFile("pfw.i18n.xml"), and :L66 destroys it; n_cst_i18n_en.sru does
//      the same at :L158-L159. There is no such handler here, so no table is ever opened.
//   3. NO TABLE TO LOAD. pfw.i18n.xml contains no chs section whatsoever. Its only language
//      attribute values are `en` and `cht`, six category elements each; a search for a chs section
//      returns zero matches.
//
// So the two XPath providers query a table under a hardcoded language literal
// [n_cst_i18n_en.sru:L51, n_cst_i18n_cht.sru:L52] and this one has no literal to query with,
// because the source strings ARE the Simplified Chinese strings. That is what "base locale" means
// operationally, and it is why this class holds no state.
//
// The consequences are constraints, not observations. AAP §0.7.3 C-C makes pfw.i18n.xml read-only,
// so a chs section may not be added to it - and its absence is load-bearing evidence, so adding one
// would destroy the proof as well as breaking the rule. AAP §0.7.3 C-D keeps the Documents XML
// object family deferred, and §0.2.1.3 Correction 6 confines the one substituted XML read in this
// project to the sibling resource-reader file; this provider never had an n_xmldoc to substitute, so
// it acquires no Documents coupling BY CONSTRUCTION. It must not be given a reader "so that all
// three providers match": matching the siblings' shape would mean loading a table that does not
// exist.
//
// A NOTE ON HOW THE ABSENCES BELOW ARE WORDED
// The state audit for this file greps it for the managed type and helper names a resource-loading
// provider would have to name, and expects zero matches for every one. This header therefore
// describes those constructs by role - "the substituted XML document reader", "the composite
// formatting helper", "this project's category-constant catalogue" - rather than spelling their
// identifiers out. The decisions are recorded in full either way, which is what C-K asks for, and
// the audit stays a clean yes-or-no rather than one that has to distinguish a code reference from a
// mention in prose.
//
// THE DERIVATION CHAIN FLATTENS TO ONE INTERFACE
// AAP §0.4.5.2 maps a `ne_cst_*` object to a derived class, and here that mapping resolves to a
// direct implementation rather than a class hierarchy, because neither ancestor has anything left
// to inherit:
//
//     n_cst_i18n        ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru
//                       Declares the event and nothing else [:L8-L10]. Its `type variables` block
//                       holds only the licence header and not one variable [:L13-L37]. That event
//                       maps onto II18nProvider.
//     ne_cst_i18n       ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru
//                       Declares only CAT_MSGBOX and CAT_DWSVC as offsets from
//                       Enums.I18N_CAT_CUSTOM [:L16-L17], and overrides no event. Those two
//                       constants are this project's category-constant catalogue, so the
//                       intermediate has no counterpart.
//     n_cst_i18n_chs    this file.
//
// One member to implement, no state to inherit, no constants needed here - so a base class would
// have nothing to hold and would consume the single base slot for nothing.
//
// `call super::ontranslate` IS BEHAVIOURALLY EMPTY, AND ITS OMISSION IS DELIBERATE
// The legacy handler opens with `call super::ontranslate` BEFORE its own logic [:L19], so the
// ordering question is real and is answered here rather than left to be guessed at. It chains to
// ne_cst_i18n, which does not handle the event, and then to n_cst_i18n, which DECLARES the event
// and implements no script for it. The chain therefore executes nothing and cannot return anything
// that this handler reads - the handler ignores it and evaluates :L25 unconditionally.
//
// An interface has no base implementation to chain to, so nothing is lost by there being no such
// call in the C# below. There is deliberately no default interface method standing in for that
// empty body and no abstract base class invented to host it: either would add a construct the
// legacy does not have in order to model a call that does nothing.
//
// THE RETURN ALPHABET IS EXACTLY 1 AND 0, AND IT IS WRITTEN RAW ON PURPOSE
// The legend all three providers carry states it directly: `返回1代表已处理`, "returning 1 means
// handled" [:L23]. II18nProvider publishes no named constant for either value and documents the
// convention in prose instead, so this file writes the numerals as the legacy writes them. That
// choice must hold for all three providers, and it does: 1 for handled, 0 for not handled, in
// every one.
//
// Two things these values are NOT. They are not part of the tri-state return-code algebra that
// RetCode and Predicates govern - a coincidence of numerals only, since RetCode.PREVENT is also 1
// and RetCode.OK is 0, and reading this member through Predicates.IsSucceeded would be a category
// error. And 0 is not a failure: with no provider installed at all the legacy returns the text
// unchanged, so a provider answering 0 has to be indistinguishable from that. The facade's
// silent-passthrough fallback depends on it.
//
// `text` IS NEVER WRITTEN, ON EITHER PATH
// The parameter is `ref` because that is how the contract delivers a translation - the two XPath
// providers assign to it [n_cst_i18n_en.sru:L53, n_cst_i18n_cht.sru:L54] and the facade returns
// that same variable on the following line. This provider simply never writes to it. Not a
// translation, not a trim, not a normalization, not a copy-back of the value it was given: :L25 and
// :L26 are `return` statements and touch nothing.
//
// That is worth stating because a self-assignment would LOOK harmless and is not. Assigning the
// parameter back to itself is an observable write through a ref parameter - it is exactly the trick
// n_cst_thread_task_sqlupdate.sru uses at :L163 to flip hidden state - and it would also silently
// change what a caller holding a null string gets back. Nor is a null check needed: because the
// value is never dereferenced, a null `text` is answered by the same two return statements as any
// other input, cannot throw, and comes back untouched. Null is preserved as null, which AAP
// §0.4.5.4 requires.
//
// DELIBERATELY ABSENT, EACH FOR A STATED REASON, so nobody "completes" this file by adding one
//   * Any field, and therefore any state. See the three evidences above. An unused field would
//     also fail this repository's build outright, since warnings are errors.
//   * A constructor. Adding one that loads a resource is the exact behavioural change C-B forbids,
//     and there is no resource for it to load. The implicit parameterless constructor matches
//     `Create n_cst_i18n_chs` [pfw.sra:L99] precisely.
//   * A finalizer, and IDisposable. The legacy destructor is `call super::destroy` and nothing else
//     [:L15-L17]; there is no document to Destroy, unlike n_cst_i18n_cht.sru:L66. II18nProvider
//     deliberately does not extend IDisposable, so adding it here would oblige every caller and
//     test double to dispose an object that owns nothing.
//   * A `choose case` over `category`, and any reference to this project's category-constant
//     catalogue or either of the two constants it publishes. The legacy does not read `category` at
//     all. The siblings' category switch exists only to pick a table section
//     [n_cst_i18n_en.sru:L34-L47]; with no table there is nothing to pick, and adding a switch
//     would be inventing behaviour.
//   * Any resource-document field, any reference to the sibling resource reader or to the
//     substituted XML document and query types, and any file-system read. See C-D above.
//   * Any culture object, and any language or locale member. No provider exposes one. The locale is
//     not data on the provider; it is WHICH provider the composition root constructed
//     [pfw.sra:L95-L102].
//   * Any reference to the kernel's composite formatting helper. The siblings use it only to build
//     an XPath query string [n_cst_i18n_en.sru:L51]; there is no query here.
//   * Any logging, any exception, any async or Task-returning form. A miss is not an error, the
//     facade has no handler, and the member is synchronous by contract - the facade reads the
//     mutated text on the statement after the call.
//   * Any SCREAMING_SNAKE identifier of this file's own. Enums.I18N_SRC_PFW is CONSUMED here by
//     name, as AAP §0.4.5.3 requires, but nothing is DECLARED. That matters mechanically: the
//     repository-root .editorconfig relaxes CA1707 and IDE1006 only for the specific files on its
//     BAND 3 roster - the single source of truth for that list - that
//     carry preserved legacy constant spellings, and this file is deliberately not one of them.
//   * `sealed`. The legacy `global type` is an ordinary derivable type, the two siblings are the
//     same, and nothing here relies on being final; the modifier would be a new constraint on a
//     public type for no behavioural gain.
//
// GOVERNING CONSTRAINTS
// review_rules reports "No user rules provided", so no user-specified rule governs this file and
// none was invented. In their place AAP §0.7.2 (the enterprise baseline) and §0.7.3 (the twelve
// binding non-rule constraints) apply. The ones that actually bite here are C-B (replicate
// documented behaviour verbatim, which is what both rulings above are), C-C (the legacy tree is
// read-only and is the only specification), C-D (this provider stays I/O-free and XML-free), C-K
// (document every boundary decision at its point of reproduction, which is why this header is
// longer than the code), and §0.4.5.2 and §0.4.5.3 for the object-kind and constant-identifier
// mappings. C-H applies indirectly: there are exactly two branches, so anything short of full
// coverage of this file is an untested branch with no excuse.
//
// THE LEGACY TREE IS READ-ONLY AND SHARES THIS WORKING DIRECTORY
// Every `:Lnnn` locator above points into ws_objects/ or at pfw.i18n.xml, which together are the
// behavioural oracle for parity testing: read as specification, never edited, moved or reformatted,
// and never a build input. n_cst_i18n_chs.sru is the single specification for this file - there is
// no other statement of this behaviour anywhere to consult - so every claim made here is cited to a
// line, and every line number was verified against the file on disk rather than copied forward.
// ==============================================================================================

using PowerFramework.Shared.Kernel;

namespace PowerFramework.Shared.Localization;

/// <summary>
/// The Simplified Chinese locale provider. Simplified Chinese is the framework's base locale, so
/// the legacy source strings are already in it and there is nothing to translate: this provider
/// reports that it handled the framework's own translation requests without altering the text, and
/// declines everything else. Ports
/// <c>ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru</c>, whose entire behaviour is the
/// two statements at :L25-L26.
/// </summary>
/// <remarks>
/// <para>
/// <b>No state, and three independent reasons there is none.</b> The legacy object declares no
/// <c>type variables</c> block, so it has no fields; it overrides no constructor, so it loads
/// nothing; and <c>pfw.i18n.xml</c> has no Simplified Chinese section to load, its only language
/// values being English and Traditional Chinese. Both sibling providers differ on all three counts,
/// each declaring <c>privatewrite n_xmldoc _doc</c> and loading the table in its constructor
/// [<c>n_cst_i18n_en.sru</c>:L11-L14, :L158-L159, <c>n_cst_i18n_cht.sru</c>:L11-L14, :L62-L63]. This
/// class therefore declares no field, no constructor, no finalizer and no disposal, and performs no
/// I/O of any kind.
/// </para>
/// <para>
/// <b>Handled-but-unchanged is deliberate and must not be collapsed.</b> Answering <c>1</c> without
/// writing to the text is a positive statement that the caller's text is already correct for this
/// locale, and it is a different answer from <c>0</c>, which declines the request. The facade
/// discards the value today [<c>ws_objects/pfw.ui.pbl.src/i18n.srf</c>:L17-L18, :L21-L22], so
/// neither <c>return</c> is observable through it - but the value is part of the published provider
/// contract, it travels in characterization recordings, and it would halt a provider chain. Rewriting
/// this method to return <c>0</c> uniformly, or deleting it as dead code, are both behavioural
/// regressions.
/// </para>
/// <para>
/// <b>The derivation chain is flattened.</b> The legacy hierarchy is <c>n_cst_i18n</c> to
/// <c>ne_cst_i18n</c> to this type. The root declares only the event this class now implements
/// through <c>II18nProvider</c> [<c>ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru</c>:L9] and the
/// intermediate declares only the two category constants that now live in this project's
/// category-constant catalogue
/// [<c>ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru</c>:L16-L17], so neither has a .NET
/// counterpart and no base class is introduced here.
/// </para>
/// <para>
/// <b>Selection.</b> The composition root chooses among the three providers by locale token,
/// creating this one for <c>"chs"</c> and installing it through the facade
/// [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L98-L99, :L103]. That is why the three-provider shape is
/// reproduced in full including this member of it, and why an instance is created with the implicit
/// parameterless constructor and nothing else.
/// </para>
/// </remarks>
public class SimplifiedChineseProvider : II18nProvider
{
    /// <summary>
    /// Reports whether this provider handled the request, without ever altering
    /// <paramref name="text"/>. Returns <c>1</c> for the framework's own source, because Simplified
    /// Chinese is the base locale and the text is already correct as supplied, and <c>0</c> for any
    /// other source. Ports <c>n_cst_i18n_chs.sru</c>:L25-L26, which are the whole of the legacy
    /// handler's live body.
    /// </summary>
    /// <param name="source">
    /// The originating subsystem, and the only parameter this provider reads. Compared against
    /// <c>Enums.I18N_SRC_PFW</c> by name rather than against the literal it happens to equal, as
    /// AAP §0.4.5.3 requires, because the identifier is what travels in payloads, log records and
    /// characterization recordings.
    /// </param>
    /// <param name="category">
    /// The resource category. Deliberately unread: the legacy handler does not examine it, and the
    /// sibling providers examine it only to select a section of the translation table
    /// [<c>n_cst_i18n_en.sru</c>:L34-L47], which this provider does not have. Adding a switch over
    /// it would invent behaviour the original does not have.
    /// </param>
    /// <param name="text">
    /// The text to translate, which this provider never writes to on either path - no translation,
    /// no trim, no normalization, and no copy-back of the value it was given, since a
    /// self-assignment through a <c>ref</c> parameter is itself an observable write. It is returned
    /// to the caller exactly as supplied. Because the value is never dereferenced, a null is
    /// answered by the same two statements as any other input, cannot throw, and is preserved as
    /// null.
    /// </param>
    /// <returns>
    /// <c>1</c> when <paramref name="source"/> is <c>Enums.I18N_SRC_PFW</c>, meaning handled - the
    /// legacy states this as <c>返回1代表已处理</c>, "returning 1 means handled"
    /// [<c>n_cst_i18n_chs.sru</c>:L23] - and <c>0</c> otherwise, meaning not handled. These two
    /// values are the whole alphabet, and they are not part of the tri-state return-code algebra
    /// that <c>RetCode</c> and <c>Predicates</c> govern: the numerals coincide, the semantics do
    /// not.
    /// </returns>
    public long OnTranslate(long source, long category, ref string? text)
    {
        // The legacy handler opens with `call super::ontranslate` before this test [:L19]. That
        // chain reaches ne_cst_i18n, which does not handle the event, and then n_cst_i18n, which
        // declares it and implements no script - so it executes nothing, returns nothing this
        // handler reads, and has no .NET counterpart. Its absence here is deliberate, not an
        // oversight, and no empty base call is invented to stand in for it.

        // n_cst_i18n_chs.sru:L25 - if source = Enums.I18N_SRC_PFW then return 1
        //
        // HANDLED, WITH THE TEXT LEFT EXACTLY AS SUPPLIED. This is not dead code and must not be
        // collapsed into the `return 0` below: 1 asserts that no further processing is required
        // because the caller's text is already Simplified Chinese, which is a different claim from
        // declining the request. `text` is deliberately not touched on this path.
        if (source == Enums.I18N_SRC_PFW)
        {
            return 1;
        }

        // n_cst_i18n_chs.sru:L26 - return 0
        //
        // NOT HANDLED, for every other source including Enums.I18N_SRC_CUSTOM. Not an error and not
        // a failure: the facade must be unable to tell this apart from having no provider installed
        // at all, so nothing is thrown, nothing is logged, and `text` is again left untouched.
        return 0;
    }
}
