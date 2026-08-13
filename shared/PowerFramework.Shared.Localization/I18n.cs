// ==============================================================================================
// I18n.cs
// The framework's translation entry point: install a provider, then translate through it - or,
// with no provider installed, hand every string straight back untouched.
//
// Source of record: ws_objects/pfw.ui.pbl.src/i18n.srf, a twenty-four-line global function object
// publishing exactly three overloads and no more [:L6-L10] - an INSTALLER taking a provider and
// answering RetCode.E_INVALID_OBJECT or RetCode.OK [:L12-L14], and two TRANSLATORS that invoke the
// installed provider only when one is installed and return the text unconditionally either way
// [:L17-L18, :L21-L22]. The two-argument translator hardcodes Enums.I18N_SRC_PFW as its source
// [:L17]. The slot all three share is `global n_cst_i18n n_cst_i18n` [n_cst_i18n.sru:L11] - a global
// whose name shadows its own type name, one of the two collisions AAP 0.4.5.1 names.
//
// About a dozen lines of executable code carry two behaviours the AAP requires intact, so the
// reasoning is recorded rather than left to be reconstructed.
// ==============================================================================================
//
// DECISION 1  The slot is an INSTANCE FIELD of an instantiable class, never a static mutable.
// AAP 0.4.5.1: the TYPE keeps the descriptive .NET name (II18nProvider) and the INSTANCE becomes an
// injected dependency rather than a global. The apparent tension with AAP 0.4.5.2 - which maps a
// `*.srf` to a static method on a role-named static class - resolves on specificity: its three
// worked examples (Predicates.IsSucceeded, Bits.BitAnd, Formatting.Sprintf) are PURE functions,
// while this one reads and writes global mutable state, which is the installer's whole point. Three
// load-bearing consequences: the sibling test project runs collections in parallel, so a static slot
// would make one test's fake visible to every other and the suite order-dependent - and a flaky
// suite cannot hold the AAP 0.7.3 C-H coverage gate; Gateway installs through ordinary DI, selecting
// a provider from the locale token whose hardcoded "en" default it preserves and makes overridable
// [pfw.sra:L94-L103, AAP 0.4.2.4], which an ambient static would put out of the container's reach;
// and nothing in this project declares a `Current`, `Default` or `Instance` holder of II18nProvider,
// which is a decision recorded in both files. There is no static mutable state here, and none may be
// added.
//
// DECISION 2  The members are spelled `I18N` inside a class named `I18n`. The class name is fixed by
// Categories.cs; C# forbids a member named identically to its enclosing type (CS0542), so a second
// spelling was needed - and `I18N` is the more faithful one, because PowerScript is case-insensitive
// and every call site in the estate writes the all-capitals form: `I18N(locale)` at pfw.sra:L103 and
// `I18N(ne_cst_i18n.CAT_DWSVC, ...)` at se_cst_dw.sru:L355 and :L357. A reader diffing against the
// oracle sees one token in both. Naming analyzers were checked rather than assumed: the root
// .editorconfig sets its naming rules to `suggestion` and no project sets EnforceCodeStyleInBuild,
// so IDE1006 cannot gate the build, and this file declares no underscore for CA1707 to report. The
// corollary constrains future edits: the .editorconfig's suppression bands are scoped to exact paths
// and THIS PATH IS NOT AMONG THEM, so no SCREAMING_SNAKE constant may be declared here - Kernel's
// are only CONSUMED, and any new preserved-spelling constant of this library belongs in Categories.cs.
//
// DECISION 3  SILENT PASSTHROUGH is the headline behaviour, and it is absolute. Both translating
// bodies invoke the provider ONLY when one is installed and then return the text UNCONDITIONALLY
// [i18n.srf:L17-L18, :L21-L22]: no else branch, no diagnostic, no marker. With no provider installed
// the text comes back exactly as it went in, and the caller cannot tell that from a successful
// translation to an identical string. It is not a fallback bolted on - with localization absent it is
// the ONLY behaviour - and it composes with the sibling reader's unchecked load, which reproduces the
// legacy's ignored `LoadFile` result [n_cst_i18n_en.sru:L158-L159], so an entirely unconfigured
// system returns every string untouched and reports nothing at all. AAP 0.7.3 C-B forbids improving
// that, and every item on this list is something a well-meaning engineer would add:
//
//     no throw when no provider is installed          no throw when the provider returns 0
//     no log, trace or metric of a miss, at any level no returning null for a missed lookup
//     no sentinel, marker, prefix or suffix           no out parameter or flag reporting a miss
//     no "was it translated" result record            no fallback chain to a second provider
//
// The mechanical statement, checkable by reading the code: `return text` is reached on EVERY path
// through both translating overloads, and neither contains a `throw`.
//
// DECISION 4  The provider's return value is discarded, visibly and on purpose (AAP 0.7.3 C-K).
// II18nProvider.OnTranslate returns a long - 1 handled, 0 not handled, documented in the doc block
// all three providers carry [n_cst_i18n_chs.sru:L23]. Every provider produces it and this facade
// THROWS IT AWAY, because the legacy invokes the event as a bare statement and returns the text
// regardless. It is written as `_ = provider.OnTranslate(...)` so the discard is a visible act rather
// than an accident of a bare call. Reproducing it correctly means the translation is taken from the
// `ref` argument and NOT from the return code, which has two consequences: a provider answering 0
// must be indistinguishable from no provider at all (DECISION 3), and a provider that mutates `text`
// and STILL answers 0 has its mutation honoured, because the facade never consults the code. That is
// not a contradiction to tidy away - it is what "the ref argument is the channel" means, and the
// sibling test project asserts it.
//
// DECISION 5  Nullability: `string?` text, `string?` return, `II18nProvider?` installer parameter.
// Each annotation is forced by an authority, and the file contains no null-forgiving `!`.
// II18nProvider.OnTranslate takes `ref string?` and C# requires a `ref` argument's type to match
// EXACTLY, so a non-nullable `string` here could only be passed as `ref text!`; the return type is
// that same variable, and AAP 0.4.5.4 forbids collapsing null, which is a real value in this estate
// because the DataWindow handlers reaching this entry point carry explicit null-and-null comparison
// arms [se_cst_dw.sru:L200, :L372]. A null `text` still reaches the provider: the legacy guard tests
// the PROVIDER's validity and never the text [i18n.srf:L17, :L21], so short-circuiting on a null
// text would be new behaviour - and II18nProvider binds the other half, that no provider throws on
// one. The installer parameter is `II18nProvider?` because the legacy tests `Not IsValid(n)` and
// returns an error code [:L12], making an invalid argument EXPECTED INPUT rather than a caller
// defect; a non-nullable parameter would raise CS8625 at `I18N(null)`, so the very case the oracle
// handles could not be exercised without a suppression, and warnings are errors here.
//
// DECISION 6  `in` on source and category, plain by-value on text. AAP 0.4.5.2 maps `readonly` to
// `in` and PowerBuilder `long` to C# `long`, applied literally against the prototypes
// [i18n.srf:L8-L9]. `text` is NOT `readonly` in either prototype - it is the one parameter the oracle
// leaves writable, because it is the channel a translation travels back through - so it stays plain
// by-value and is handed onward by `ref`; marking it `in` makes the file uncompilable at that
// argument, which is a fair measure of how load-bearing the distinction is. `in` on a 64-bit integer
// buys nothing at runtime; it is here as the faithful rendering of `readonly`, and II18nProvider
// deliberately does NOT carry it because the legacy EVENT declares bare `long` [n_cst_i18n.sru:L9].
//
// DECISION 7  The validity test is `is not null`, and Predicates.IsValidObject is NOT used. The
// oracle gates on PowerBuilder's `IsValid`, which reports whether a reference is created and not yet
// `Destroy`ed; Kernel records the substitution reasoning on Predicates.IsValidObject - .NET has no
// destroy-and-dangle state, so the exact managed equivalent is "not null". This file applies that
// reasoning without calling that member, for two independent reasons: Predicates.cs is not among
// this file's declared dependencies, and inventing an import outside that set is what the refactor's
// dependency discipline forbids; and `IsValidObject(object?)` carries no [NotNullWhen(true)], so the
// compiler learns nothing from a true result and the dereference would need `provider!`,
// reintroducing the operator DECISION 5 exists to avoid. `is not null` is what that member computes
// anyway - its body is `value is not null` - so nothing is lost.
//
// DECISION 8  Two overloads, one shared body, and NOT an optional parameter. The two-argument
// overload delegates to the three-argument one passing Enums.I18N_SRC_PFW, reproducing the constant
// the oracle hardcodes [i18n.srf:L17] and eliminating a duplicated body the two could later drift
// apart in. Collapsing them into one method with a defaulted parameter was rejected: the oracle
// publishes two distinct overloads [:L8-L9], callers select between them (se_cst_dw.sru:L355 and
// :L357 both call the two-argument form), and a defaulted parameter is a different published surface
// with a different metadata shape, a different overload-resolution story, and a default baked into
// every caller's compiled call site rather than resolved here.
//
// DECISION 9  Install ordering, and the guards that are deliberately absent. The invalid case returns
// BEFORE any assignment [i18n.srf:L12 precedes :L13], so a failed install leaves whatever was
// installed before it intact - observable by installing a provider, failing an install, then
// translating, and the sibling test project asserts it. Equally deliberate is what the oracle does
// NOT do, none of which may be added: no already-installed guard, so a second install simply
// replaces the slot; no uninstall, reset or dispose, so the slot's lifetime is null -> provider ->
// provider' and never back to null; and no property, accessor or IsInstalled probe, because the
// oracle publishes three functions and no fourth thing - a caller learns what is installed by
// translating, which is also how the tests observe it.
//
// DECISION 10  Concurrency: no lock, no volatile, no Interlocked - and that is correct here. This
// class is registrable as a singleton, so its translating overloads will be entered concurrently
// where the oracle's original was a single-threaded desktop global installed once during the open
// event [pfw.sra:L103]. Synchronization was considered and rejected as unobservable ceremony: a
// reference-typed field is read and written atomically under the CLI memory model, so no reader can
// observe a torn reference; the slot only ever moves forward (DECISION 9), so a concurrent install
// cannot turn a non-null observation back into null; and the translating overloads copy the field
// into a local ONCE and then test and dereference the LOCAL, which is what makes the null test and
// the call refer to the same instance - so an install racing a translation can only mean the older
// provider was used, never a NullReferenceException and never two providers within one call. The
// intended pattern, install during startup and translate while serving, additionally gives the
// host's own startup barrier a happens-before edge for free. A `lock` would serialise every
// translation in the process for no behavioural gain, which is a change the oracle cannot express.
//
// DELIBERATELY ABSENT, each for a stated reason, so nobody "completes" this file by adding one:
//   * Any call to Sprintf, and any formatting at all - worth stating because Kernel's Formatting.cs
//     summarises this folder as one where "the I18n path formats translated text through Sprintf".
//     Checked against the oracle: it does not. Sprintf appears on BOTH SIDES of this entry point -
//     the two XPath providers build a query with it, and call sites such as
//     n_cst_dwsvc_rowselect.sru:L239 format a result they already translated - and in neither case
//     inside this file. A formatting dependency here would be new capability.
//   * Any logging, ILogger, Console, trace, metric or diagnostic sink. Forbidden twice: as a
//     dependency the AAP 0.7.2 baseline does not want in a pure shared library, and as behaviour,
//     because logging a miss is precisely what DECISION 3 forbids.
//   * Any `throw`, including argument validation on the installer. Its answer to an invalid argument
//     is a return CODE, which is the whole content of i18n.srf:L12.
//   * Any interface over this facade. Consumers substitute behaviour by installing a fake PROVIDER,
//     the seam the oracle itself publishes and II18nProvider already types.
//   * Any async or Task-returning form. The oracle TRIGGERS the event rather than posting it, so the
//     mutation is visible on the very next line; an async form would break that sequence.
//   * Any CultureInfo, locale property, resource manager or fallback chain. The locale is not data on
//     this facade - it is WHICH provider was installed [pfw.sra:L95-L102].
//   * Any file, network or environment access. This library performs exactly one narrow read, in
//     I18nResourceReader.cs, and this file performs none.
//
// DEPENDENCY SURFACE. `using PowerFramework.Shared.Kernel;` is the only import, and the executable
// code reaches exactly three names through it: RetCode.E_INVALID_OBJECT and RetCode.OK
// [retcode.sru:L48, :L39] and Enums.I18N_SRC_PFW [enums.sru:L115] - all BY IDENTIFIER and never by
// their literal values -5, 0 and 0, because AAP 0.4.5.3 preserves those spellings so they stay
// legible in log records and characterization recordings. II18nProvider and Categories need no
// import, being siblings in this namespace; Categories appears in DOCUMENTATION ONLY. Nothing else
// is reachable: no package, no other shared library, nothing under services/, and nothing belonging
// to the four deferred services - notably not Kernel's Predicates, for the reasons in DECISION 7.
//
//  LOCATOR CONVENTION: every bare `pfw.sra:L...` in this file means ws_objects/pfw.pbl.src/pfw.sra,
//  the framework application - never the same-named packager object at
//  ws_objects/pfw.pack.pbl.src/pfw.sra (AAP 0.8.6 R7).
// ==============================================================================================

using PowerFramework.Shared.Kernel;

namespace PowerFramework.Shared.Localization;

/// <summary>
/// The framework's localization entry point: installs the provider that translations dispatch to,
/// and translates text through it. Ported from the three overloads of
/// <c>ws_objects/pfw.ui.pbl.src/i18n.srf</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three members, because the oracle publishes exactly three.</b> Its forward prototypes
/// [i18n.srf:L7-L9] declare one installer taking a provider and returning a status code, and two
/// translators returning text - one that supplies <see cref="Enums.I18N_SRC_PFW"/> as the source
/// and one that takes the source explicitly. Nothing else is published: there is no uninstall, no
/// accessor for the installed provider, no capability probe and no formatting helper, and this
/// file's header records why each of those absences is a decision rather than an omission.
/// </para>
/// <para>
/// <b>Silent passthrough is the behaviour to know about.</b> Both translating overloads invoke the
/// installed provider only when there is one, and then return the text unconditionally
/// [i18n.srf:L17-L18, :L21-L22]. With no provider installed - or with one that reports the text as
/// unhandled - the text comes back exactly as it went in. Neither overload throws, logs, returns
/// <see langword="null"/> for a miss, or marks the text in any way, and AAP §0.1.4 lists that
/// fallback among the behaviours that must survive this migration intact.
/// </para>
/// <para>
/// <b>Instantiable, and holding the provider in an instance field.</b> The oracle keeps the
/// installed provider in a global variable that shadows its own type name
/// [<c>ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru</c>:L11]; following AAP §0.4.5.1 that global
/// becomes an injected dependency, so this class is registered as a singleton and injected rather
/// than reached statically. There is no static mutable state here and no ambient
/// <c>Current</c>/<c>Default</c>/<c>Instance</c> holder anywhere in this library, which is what
/// lets two parallel tests install two different providers without either seeing the other's.
/// </para>
/// <para>
/// <b>Thread safety.</b> Installing is a single reference assignment and translating reads the
/// field once into a local before testing and using it, so concurrent translation is safe without
/// a lock: a translation racing with an install observes either the previous provider or the new
/// one, never a torn reference and never <see langword="null"/> once something has been installed.
/// The intended pattern is nonetheless the oracle's - install during startup [<c>pfw.sra</c>:L103],
/// translate while serving.
/// </para>
/// </remarks>
/// <example>
/// The composition root selects a provider from a locale token and installs it, mirroring
/// <c>ws_objects/pfw.pbl.src/pfw.sra</c>:L94-L103; call sites then translate through the same
/// facade, mirroring <c>se_cst_dw.sru</c>:L355 and :L357:
/// <code>
/// I18n i18n = new I18n();
/// long installed = i18n.I18N(new EnglishProvider());   // RetCode.OK
///
/// string? caption = i18n.I18N(Categories.CAT_DWSVC, "错误");
/// </code>
/// With no provider installed the same call returns <c>"错误"</c> unchanged, and reports nothing.
/// </example>
public sealed class I18n
{
    /// <summary>
    /// The installed provider, or <see langword="null"/> when none has been installed yet. Ports
    /// the global slot declared at <c>ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru</c>:L11,
    /// <c>global n_cst_i18n n_cst_i18n</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An INSTANCE field rather than a static one, per AAP §0.4.5.1: the shadowed global becomes
    /// an injected dependency, so the facade that owns it is instantiated and injected.
    /// DECISION 1 in this file's header records the two consequences that make the choice
    /// load-bearing - parallel test isolation, and installability through the container from
    /// Gateway's locale option.
    /// </para>
    /// <para>
    /// It is deliberately writable rather than <c>readonly</c>, because installation is a published
    /// operation [i18n.srf:L13] and repeat installation simply replaces it. It moves only from
    /// <see langword="null"/> to a provider and then to a later provider: no member here sets it
    /// back to <see langword="null"/>, because the oracle publishes no uninstall.
    /// </para>
    /// </remarks>
    private II18nProvider? _provider;

    // ------------------------------------------------------------------------------------------
    //  OVERLOAD 1 OF 3 - THE INSTALLER                                          i18n.srf:L12-L15
    // ------------------------------------------------------------------------------------------
    //  Declared first because the oracle declares it first [i18n.srf:L7]. It is the only overload
    //  that is not a translator, and the only one that writes the slot.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Installs <paramref name="provider"/> as the provider both translating overloads dispatch to,
    /// replacing any provider installed before it. Ports
    /// <c>global function long i18n (n_cst_i18n n)</c>
    /// [<c>ws_objects/pfw.ui.pbl.src/i18n.srf</c>:L12-L15].
    /// </summary>
    /// <param name="provider">
    /// The provider to install, or <see langword="null"/>. The oracle's parameter is spelled
    /// <c>n</c> and typed as the BASE provider type [i18n.srf:L12], which is what lets the
    /// composition root install any of the three concrete locale providers through one
    /// base-typed local [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L89, :L97, :L99, :L101]; the .NET
    /// equivalent is this interface-typed parameter, renamed to say what it is.
    /// <para>
    /// <see langword="null"/> is legal input, not a caller defect: the oracle's first act is
    /// <c>if Not IsValid(n) then return RetCode.E_INVALID_OBJECT</c>, so an invalid provider has a
    /// defined RESULT rather than an exception. Nothing is thrown for it.
    /// </para>
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/> (<c>0</c>) when the provider was installed [i18n.srf:L14], or
    /// <see cref="RetCode.E_INVALID_OBJECT"/> (<c>-5</c>) when
    /// <paramref name="provider"/> is <see langword="null"/> and nothing was installed
    /// [i18n.srf:L12]. Both are the kernel constants, referenced by identifier per AAP §0.4.5.3
    /// rather than as the literals <c>0</c> and <c>-5</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>A failed install does not clear the slot.</b> The rejection returns BEFORE the assignment
    /// [i18n.srf:L12 precedes :L13], so whatever was installed previously keeps answering. That
    /// ordering is observable and is preserved exactly.
    /// </para>
    /// <para>
    /// <b>Repeat installation overwrites, without complaint.</b> The oracle has no
    /// already-installed guard and none is added, so the last successful install wins. There is
    /// likewise no uninstall: this member cannot be used to return the slot to
    /// <see langword="null"/>.
    /// </para>
    /// <para>
    /// The return value is a plain <see cref="long"/> status code and NOT part of the tri-state
    /// algebra <c>Predicates</c> governs; callers that want that reading may pass it to
    /// <c>Predicates.IsSucceeded</c> themselves, exactly as they could in the oracle.
    /// </para>
    /// </remarks>
    public long I18N(II18nProvider? provider)
    {
        // i18n.srf:L12 - `if Not IsValid(n) then return RetCode.E_INVALID_OBJECT`
        //
        // `is not null` is the faithful rendering of the PowerBuilder `IsValid` intrinsic: .NET has
        // no destroy-and-dangle state, so "created and not yet destroyed" is exactly "not null".
        // DECISION 7 in this file's header records why Kernel's Predicates.IsValidObject - which
        // computes precisely this comparison - is not called here.
        //
        // This test comes FIRST and returns, which is what leaves an earlier provider installed
        // when an install fails. Do not merge it into the assignment below.
        if (provider is null)
        {
            return RetCode.E_INVALID_OBJECT;
        }

        // i18n.srf:L13 - `n_cst_i18n = n`
        //
        // The ported global slot. No guard against an existing provider, because the oracle has
        // none: a second install replaces the first (DECISION 9).
        _provider = provider;

        // i18n.srf:L14 - `return RetCode.OK`
        return RetCode.OK;
    }

    // ------------------------------------------------------------------------------------------
    //  OVERLOAD 2 OF 3 - TRANSLATE, FRAMEWORK SOURCE                            i18n.srf:L17-L19
    // ------------------------------------------------------------------------------------------
    //  The only translating overload the estate calls. Measured, not assumed: every translating
    //  call site under ws_objects/ passes two arguments and none passes three. Representative
    //  sites, one per category, are se_cst_dw.sru:L355 and :L357 and
    //  n_cst_dwsvc_contextmenu.sru:L243 for CAT_DWSVC, messageboxex.srf:L184-L190 for CAT_MSGBOX,
    //  and n_cst_window_titlebar.sru:L376 for Enums.I18N_CAT_WINDOW.
    //
    //  Its whole difference from overload 3 is that it supplies Enums.I18N_SRC_PFW as the source,
    //  so it delegates rather than duplicating the dispatch (DECISION 8).
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Translates <paramref name="text"/> for the given category through the installed provider,
    /// using the framework's own source <see cref="Enums.I18N_SRC_PFW"/>, and returns the result -
    /// or returns <paramref name="text"/> unchanged when no provider is installed. Ports
    /// <c>global function string i18n (readonly long category,string text)</c>
    /// [<c>ws_objects/pfw.ui.pbl.src/i18n.srf</c>:L17-L19].
    /// </summary>
    /// <param name="category">
    /// The resource category the lookup searches, <c>in</c> because the oracle's parameter is
    /// <c>readonly</c> [i18n.srf:L8] and AAP §0.4.5.2 maps <c>readonly</c> to <c>in</c>. Values
    /// come from <see cref="Enums.I18N_CAT_WINDOW"/> through <see cref="Enums.I18N_CAT_CUSTOM"/>
    /// and from <see cref="Categories.CAT_MSGBOX"/> and <see cref="Categories.CAT_DWSVC"/>.
    /// </param>
    /// <param name="text">
    /// The text to translate. It is both the lookup key and the channel the translation comes back
    /// through, so it is passed to the provider by <c>ref</c> and the possibly-rewritten value is
    /// what this method returns. It may be <see langword="null"/>, and a <see langword="null"/> is
    /// passed to the provider unchanged rather than short-circuited, because the oracle's guard
    /// tests the provider and never the text [i18n.srf:L17].
    /// </param>
    /// <returns>
    /// The translated text, or <paramref name="text"/> exactly as supplied when no provider is
    /// installed or the installed provider did not handle it. Never a marker, never a sentinel and
    /// never <see langword="null"/> unless <paramref name="text"/> itself was
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Silent passthrough.</b> This method cannot fail. It does not throw, does not log and does
    /// not signal a miss in any way; an untranslated string is returned identical to the input, and
    /// is indistinguishable from a translation that happened to produce the same string
    /// [i18n.srf:L17-L18].
    /// </para>
    /// <para>
    /// <b>Equivalent to the three-argument overload with the framework source.</b> It delegates
    /// there, because the oracle hardcodes exactly <see cref="Enums.I18N_SRC_PFW"/> as its source
    /// [i18n.srf:L17]. The two are deliberately kept as separate overloads rather than merged
    /// behind a defaulted parameter, which would publish a different surface (DECISION 8).
    /// </para>
    /// </remarks>
    public string? I18N(in long category, string? text)
    {
        // i18n.srf:L17-L18 - the whole body is
        //     `if IsValid(n_cst_i18n) then n_cst_i18n.Event OnTranslate(Enums.I18N_SRC_PFW,category,ref text)`
        //     `return text`
        //
        // which is overload 3 with the source fixed to Enums.I18N_SRC_PFW. Delegating reproduces
        // that constant by identifier (AAP §0.4.5.3) and keeps ONE copy of the dispatch, so the
        // guard, the discard and the unconditional return cannot drift between the two overloads.
        return I18N(Enums.I18N_SRC_PFW, category, text);
    }

    // ------------------------------------------------------------------------------------------
    //  OVERLOAD 3 OF 3 - TRANSLATE, EXPLICIT SOURCE                             i18n.srf:L21-L23
    // ------------------------------------------------------------------------------------------
    //  The one place in this file where the provider is actually called, and therefore the one
    //  place the silent passthrough (DECISION 3) and the discarded return (DECISION 4) are
    //  reproduced. Overload 2 reaches the provider only through here.
    //
    //  It has NO caller in the estate - the sweep behind overload 2's banner found not one
    //  three-argument call site - yet it is published here because the oracle publishes it at
    //  i18n.srf:L9 and because Enums.I18N_SRC_CUSTOM [enums.sru:L116] exists precisely so a caller
    //  outside the framework can name its own source. Dropping it would narrow the surface the
    //  oracle offers; that it is currently unreached is a fact about the estate, not licence to
    //  remove it.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Translates <paramref name="text"/> for the given source and category through the installed
    /// provider and returns the result - or returns <paramref name="text"/> unchanged when no
    /// provider is installed. Ports
    /// <c>global function string i18n (readonly long source,readonly long category,string text)</c>
    /// [<c>ws_objects/pfw.ui.pbl.src/i18n.srf</c>:L21-L23].
    /// </summary>
    /// <param name="source">
    /// The originating subsystem, <c>in</c> because the oracle's parameter is <c>readonly</c>
    /// [i18n.srf:L9]. <see cref="Enums.I18N_SRC_PFW"/> is the framework's own source and
    /// <see cref="Enums.I18N_SRC_CUSTOM"/> the open extension point; all three shipped providers
    /// answer only for the former and report anything else as unhandled, which this method then
    /// passes through untouched.
    /// </param>
    /// <param name="category">
    /// The resource category the lookup searches, <c>in</c> for the same reason [i18n.srf:L9].
    /// </param>
    /// <param name="text">
    /// The text to translate: both the lookup key and the channel the translation returns through,
    /// handed to the provider by <c>ref</c>. May be <see langword="null"/>, and a
    /// <see langword="null"/> still reaches the provider because the oracle's guard tests only the
    /// provider [i18n.srf:L21].
    /// </param>
    /// <returns>
    /// The translated text, or <paramref name="text"/> exactly as supplied when no provider is
    /// installed or the installed provider did not handle it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Silent passthrough, and the provider's answer is discarded.</b>
    /// <c>II18nProvider.OnTranslate</c> returns <c>1</c> for handled and <c>0</c> for not handled,
    /// and this method ignores it entirely - the oracle invokes the event as a bare statement and
    /// returns the text regardless [i18n.srf:L21-L22]. The translation is therefore taken from the
    /// <c>ref</c> argument alone: a provider that rewrites the text while answering <c>0</c> still
    /// has its rewrite returned, and a provider that answers <c>1</c> without touching the text
    /// returns the original. A <c>0</c> is not an error, is not logged and does not mark the text.
    /// </para>
    /// <para>
    /// <b>Thread safety.</b> The slot is read once into a local and both the test and the call use
    /// that local, so an install racing with a translation can only mean this call used the
    /// previous provider - never a <see cref="NullReferenceException"/>, and never two providers
    /// within one call.
    /// </para>
    /// </remarks>
    public string? I18N(in long source, in long category, string? text)
    {
        // Read the slot ONCE. Both the guard below and the call that follows use this local, which
        // is what makes them refer to the same provider even if another thread installs a different
        // one in between (DECISION 10). It is also what lets the compiler's null-state analysis
        // prove the dereference safe, so no null-forgiving operator is needed anywhere here.
        II18nProvider? provider = _provider;

        // i18n.srf:L21 - `if IsValid(n_cst_i18n) then n_cst_i18n.Event OnTranslate(source,category,ref text)`
        //
        // The guard tests the PROVIDER and nothing else: not the text, not the source, not the
        // category. There is no `else` in the oracle and there is none here - that absence IS the
        // silent-passthrough fallback (DECISION 3).
        if (provider is not null)
        {
            // The discard is deliberate and is written to be seen (AAP §0.7.3 C-K, DECISION 4).
            // OnTranslate's `long` result - 1 handled, 0 not handled [n_cst_i18n_chs.sru:L23] - is
            // thrown away because the oracle throws it away: it invokes the event as a bare
            // statement. Do NOT branch on it, log it, or surface it; the translation arrives
            // through the `ref` argument, which is why `text` is passed by reference here and
            // returned below whatever the provider answered.
            _ = provider.OnTranslate(source, category, ref text);
        }

        // i18n.srf:L22 - `return text`
        //
        // Unconditional, and the only exit from this method: reached identically when no provider
        // is installed, when the provider handled the text, and when it did not. With no provider
        // the value returned is the argument exactly as supplied - unchanged, unwrapped and
        // unannotated.
        return text;
    }
}
