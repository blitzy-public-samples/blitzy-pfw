// ==============================================================================================
// I18n.cs
// The framework's translation entry point: install a provider, then translate through it - or,
// with no provider installed, hand every string straight back untouched.
// ==============================================================================================
//
// WHAT THIS FILE IS
// Three overloads over about a dozen lines of executable code, ported from a PowerScript global
// function object whose entire source is twenty-four lines. The size is misleading twice over.
// Two of the three overloads carry the SILENT-PASSTHROUGH localization fallback, which AAP §0.1.4
// lists among the error-ergonomics behaviours that "must all survive the migration intact"; and
// the third overload is not a translator at all - it is the installer that decides which provider
// the other two dispatch to, and it is the seam the composition root drives.
//
// AUTHORITATIVE SOURCE, QUOTED IN FULL
//     ws_objects/pfw.ui.pbl.src/i18n.srf
//
// The forward prototypes fix the published surface at exactly three overloads and no more
// [:L6-L10]:
//
//     global function long i18n (n_cst_i18n n)                                              L7
//     global function string i18n (readonly long category,string text)                      L8
//     global function string i18n (readonly long source,readonly long category,string text) L9
//
// And the three bodies are three one-liners with a return apiece [:L12-L23]:
//
//     global function long i18n (n_cst_i18n n);if Not IsValid(n) then return RetCode.E_INVALID_OBJECT
//     n_cst_i18n = n
//     return RetCode.OK
//     end function
//
//     global function string i18n (readonly long category,string text);if IsValid(n_cst_i18n) then n_cst_i18n.Event OnTranslate(Enums.I18N_SRC_PFW,category,ref text)
//     return text
//     end function
//
//     global function string i18n (readonly long source,readonly long category,string text);if IsValid(n_cst_i18n) then n_cst_i18n.Event OnTranslate(source,category,ref text)
//     return text
//     end function
//
// Everything this file does is in those nine lines. Everything below explains why reproducing them
// faithfully in C# takes more thought than transcribing them.
//
// THE SLOT THEY SHARE
// `n_cst_i18n` in the two translating bodies is not a type name being used as a value by accident:
// ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru:L11 declares `global n_cst_i18n n_cst_i18n`, a global
// variable whose name is identical to its own type name. That variable is the installed-provider
// slot - the installer assigns it [i18n.srf:L13] and both translators test it [:L17, :L21]. It is
// one of the two shadowing collisions AAP §0.4.5.1 names, and resolving it is DECISION 1.
//
// ----------------------------------------------------------------------------------------------
// DECISION 1  The slot is an INSTANCE FIELD of an instantiable class, never a static mutable
// ----------------------------------------------------------------------------------------------
// AAP §0.4.5.1 rules that "where a legacy global auto-instance shadows its own type name, the type
// keeps the descriptive .NET name and the instance becomes an INJECTED DEPENDENCY rather than a
// global". The type kept the descriptive name in II18nProvider.cs; the instance is this class's
// `_provider` field.
//
// There is an apparent tension with AAP §0.4.5.2, whose object-kind table maps a `*.srf` global
// function to "a static method on a role-named static class", and its three worked examples are
// Predicates.IsSucceeded, Bits.BitAnd and Formatting.Sprintf. Every one of those is a PURE
// function of its arguments. This one is not: it READS AND WRITES global mutable state, which is
// the whole point of the installer overload. The more specific ruling therefore governs, and this
// class is instantiable and registrable as a singleton rather than static. Three consequences,
// each load-bearing:
//
//   * Test isolation, which AAP §0.7.3 C-H depends on. The sibling PowerFramework.Shared
//     .Localization.Tests project runs xunit collections in parallel. With a static slot, a test
//     that installs a fake would be observable by every other test in the assembly and the suite
//     would be order-dependent and flaky - and a flaky suite cannot hold a coverage gate. With an
//     instance field, two tests construct two facades and neither can see the other's provider.
//   * Gateway installs through ordinary DI. The legacy composition root selects one of three
//     providers from a locale token and installs it [ws_objects/pfw.pbl.src/pfw.sra:L94-L103];
//     Gateway's Configuration/GatewayOptions.cs preserves that token's hardcoded "en" default and
//     makes it overridable (AAP §0.4.2.4). A singleton registration plus a constructor parameter
//     is how that reaches a service; an ambient static would put it out of the container's reach.
//   * Nothing in this project declares a `Current`, `Default` or `Instance` holder of
//     II18nProvider, and II18nProvider.cs records that absence as a decision too. This file is
//     where the absence would otherwise have appeared, so it is restated here: there is no static
//     mutable state in this file, and none may be added.
//
// ----------------------------------------------------------------------------------------------
// DECISION 2  The members are spelled `I18N` inside a class named `I18n`
// ----------------------------------------------------------------------------------------------
// The class name is fixed: Categories.cs:L240 already records that "the entry point that
// dispatches to whichever provider is installed is I18n". The members cannot share that spelling
// - C# forbids a member named identically to its enclosing type (CS0542) - so a second spelling
// was needed, and `I18N` is not a compromise but the more faithful of the two. PowerScript is
// case-insensitive and every call site in the estate writes the all-capitals form: `I18N(locale)`
// at pfw.sra:L103, and `I18N(ne_cst_i18n.CAT_DWSVC, ...)` at both
// ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L355 and :L357. A reader comparing
// ported code against the oracle sees the same token in both.
//
// Naming analyzers are not an obstacle and were checked rather than assumed: the repository-root
// .editorconfig sets its three naming rules to `suggestion`, and no project sets
// EnforceCodeStyleInBuild, so IDE1006 cannot gate the build. CA1707 would report an underscore,
// and this file declares no identifier containing one. Note the corollary carefully, because it
// constrains future edits to this file: the .editorconfig's CA1707/IDE1006 suppression bands are
// scoped to exact file paths and THIS PATH IS NOT AMONG THEM. No SCREAMING_SNAKE constant may be
// declared here; Kernel's are CONSUMED, which needs no suppression, and any new preserved-spelling
// constant of this library belongs in Categories.cs.
//
// ----------------------------------------------------------------------------------------------
// DECISION 3  SILENT PASSTHROUGH is the headline behaviour, and it is absolute
// ----------------------------------------------------------------------------------------------
// Both translating bodies invoke the provider ONLY when one is installed, and then return the text
// UNCONDITIONALLY [i18n.srf:L17-L18, :L21-L22]. There is no else branch, no diagnostic and no
// marker. With no provider installed the text comes back exactly as it went in, and the caller
// cannot tell that from a successful translation to an identical string.
//
// That is not a fallback bolted onto the design; with localization absent it is the ONLY
// behaviour. It also composes with the sibling reader's unchecked load - I18nResourceReader
// reproduces the legacy's ignored `LoadFile` result [n_cst_i18n_en.sru:L158-L159], so a missing
// pfw.i18n.xml makes every lookup miss - which means an entirely unconfigured system returns every
// string untouched and reports nothing at all. AAP §0.7.3 C-B forbids improving that, and it is
// worth naming exactly what "improving" would mean here, because every item on the list is
// something a well-meaning engineer would add:
//
//     no throw when no provider is installed          no throw when the provider returns 0
//     no log, trace or metric of a miss, at any level no returning null for a missed lookup
//     no sentinel, marker, prefix or suffix           no out parameter or flag reporting a miss
//     no "was it translated" result record            no fallback chain to a second provider
//
// The mechanical statement of the rule, checkable by reading the code: `return text` is reached on
// EVERY path through both translating overloads, and neither overload contains a `throw`.
//
// ----------------------------------------------------------------------------------------------
// DECISION 4  The provider's return value is discarded, visibly and on purpose  (AAP §0.7.3 C-K)
// ----------------------------------------------------------------------------------------------
// II18nProvider.OnTranslate returns a long: 1 means handled, 0 means not handled, and the legacy
// documents that alphabet in the doc block all three providers carry - `返回1代表已处理`
// [ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru:L23]. The value is real and every
// provider produces it. This facade THROWS IT AWAY, because the legacy invokes the event as a bare
// statement and returns the text regardless [i18n.srf:L17-L18, :L21-L22].
//
// It is written as a discard assignment - `_ = provider.OnTranslate(...)` - so that the discard is
// a visible act rather than an accident of a bare call, and this comment is the C-K record of it at
// its point of reproduction. Reproducing it correctly means the translation is taken from the `ref`
// argument and NOT from the return code. Two consequences worth spelling out:
//
//   * A provider that answers 0 must be indistinguishable from no provider at all, so a 0 must
//     never become an exception, a log line or an untranslated marker (DECISION 3).
//   * A provider that mutates `text` and STILL answers 0 has its mutation honoured, because the
//     facade never consults the code. That is not a contradiction to be tidied away; it is what
//     "the ref argument is the channel" means, and the sibling test project asserts it.
//
// ----------------------------------------------------------------------------------------------
// DECISION 5  Nullability: `string?` text, `string?` return, `II18nProvider?` installer parameter
// ----------------------------------------------------------------------------------------------
// Every annotation here was forced by an authority rather than chosen by taste, and each avoids a
// null-forgiving `!` operator, of which this file contains none.
//
//   * `text` is `string?`. II18nProvider.OnTranslate takes `ref string?`, and C# requires a `ref`
//     argument's type to match EXACTLY, so a non-nullable `string` parameter here could only be
//     passed as `ref text!`. II18nProvider.cs states the obligation directly: "I18n.cs must
//     declare its own `text` parameter as `string?`, so that it can pass `ref text` with no
//     null-forgiving operator anywhere."
//   * The return type is `string?` because it returns that same variable. AAP §0.4.5.4 forbids
//     collapsing PowerBuilder's null semantics for value types, and the same reasoning applies to
//     a null string: the DataWindow handlers that reach this entry point carry explicit
//     null-and-null comparison arms [se_cst_dw.sru:L200, :L372], so a null string is a real value
//     in this estate and must remain expressible.
//   * A null `text` still reaches the provider. The legacy guard tests the PROVIDER's validity and
//     never the text [i18n.srf:L17, :L21], so short-circuiting on a null text would be a new
//     behaviour. II18nProvider.cs binds the other half of that bargain: no provider may throw on a
//     null text; "not handled" is the correct answer to a null lookup key.
//   * The installer parameter is `II18nProvider?`. The legacy tests `Not IsValid(n)` and returns an
//     error code for the invalid case [:L12], which means an invalid argument is EXPECTED INPUT and
//     part of the contract rather than a caller defect. A non-nullable parameter would make
//     `I18N(null)` raise CS8625 at the call site, so the very case the oracle handles could not be
//     exercised without a suppression - and this repository treats warnings as errors.
//
// ----------------------------------------------------------------------------------------------
// DECISION 6  `in` on source and category, plain by-value on text
// ----------------------------------------------------------------------------------------------
// AAP §0.4.5.2 maps a `readonly` parameter to an `in` parameter and PowerBuilder `long` to C#
// `long`, and both are applied literally against the prototypes at i18n.srf:L8-L9: `category` is
// `readonly` in both translating overloads and `source` is `readonly` in the three-argument one,
// so all three are `in long`. `text` is NOT `readonly` in either prototype - it is the one
// parameter the oracle leaves writable, because it is the channel a translation travels back
// through - so it stays a plain by-value parameter that is handed onward by `ref`. Marking it `in`
// would make the file uncompilable at the `ref` argument, which is a fair sign of how load-bearing
// the distinction is. Note that `in` on a 64-bit integer buys nothing at runtime; it is here
// because it is the faithful rendering of `readonly`, and II18nProvider.cs deliberately does NOT
// carry it, because the legacy EVENT declares bare `long` parameters [n_cst_i18n.sru:L9].
//
// ----------------------------------------------------------------------------------------------
// DECISION 7  The validity test is `is not null`, and Predicates.IsValidObject is NOT used
// ----------------------------------------------------------------------------------------------
// The oracle gates on the PowerBuilder intrinsic `IsValid`, which reports whether a reference is
// live - created and not yet `Destroy`ed. Kernel already records the substitution reasoning for
// that intrinsic on Predicates.IsValidObject: ".NET has no destroy-and-dangle state: a reference is
// either null or points at an object the garbage collector guarantees is alive, so the exact
// managed equivalent of 'created and not yet destroyed' is 'not null'". This file applies that
// reasoning but deliberately does not call that member, for two independent reasons:
//
//   * Predicates.cs is not among this file's declared dependencies (II18nProvider.cs, this
//     library's project file, Kernel's RetCode.cs and Kernel's Enums.cs), and inventing an import
//     outside that set is exactly what the refactor's dependency discipline forbids.
//   * `IsValidObject(object? value)` carries no [NotNullWhen(true)] annotation, so the compiler
//     learns nothing from a true result. Gating the dereference on it would require `provider!`,
//     reintroducing the null-forgiving operator DECISION 5 exists to avoid.
//
// `is not null` is behaviourally identical to what that member computes - its body is
// `value is not null` - so nothing is lost, and the reference comparison is allocation-free and
// cannot itself throw.
//
// ----------------------------------------------------------------------------------------------
// DECISION 8  Two overloads, one shared body, and NOT an optional parameter
// ----------------------------------------------------------------------------------------------
// The two-argument overload delegates to the three-argument one passing Enums.I18N_SRC_PFW. That
// is faithful rather than merely tidy: the oracle's two-argument body hardcodes exactly that
// constant as the source [i18n.srf:L17], so the delegation reproduces the value and eliminates a
// duplicated body in which the two could later drift.
//
// Collapsing them into ONE method with a defaulted parameter was rejected. The oracle publishes two
// distinct overloads [:L8-L9] and callers select between them - se_cst_dw.sru:L355 and :L357 both
// call the two-argument form - and a defaulted parameter is a different published surface with a
// different metadata shape, a different overload-resolution story, and a default value baked into
// every caller's compiled call site rather than resolved here.
//
// ----------------------------------------------------------------------------------------------
// DECISION 9  Install ordering, and the guards that are deliberately absent
// ----------------------------------------------------------------------------------------------
// The invalid case returns BEFORE any assignment [i18n.srf:L12 precedes :L13], so a failed install
// leaves whatever was installed before it intact. That ordering is observable - install a provider,
// then fail an install, then translate, and the first provider still answers - so it is preserved
// exactly and the sibling test project asserts it.
//
// Equally deliberate is what the oracle does NOT do, none of which may be added:
//
//   * No already-installed guard. A second install simply replaces the slot; there is no
//     "already installed" error and no idempotence check.
//   * No uninstall, reset or dispose. Once a provider is installed the slot is never emptied, so
//     the slot's lifetime is null -> provider -> provider' and never back to null. No public
//     member here can clear it.
//   * No property or accessor exposing the installed provider, and no IsInstalled probe. The
//     oracle publishes three functions and no fourth thing; a caller learns what is installed by
//     translating, which is also how the tests observe it.
//
// ----------------------------------------------------------------------------------------------
// DECISION 10  Concurrency: no lock, no volatile, no Interlocked - and why that is correct here
// ----------------------------------------------------------------------------------------------
// This class is registrable as a singleton, so in a service its translating overloads will be
// entered concurrently while the oracle's original was a single-threaded desktop global installed
// once during the application's open event [pfw.sra:L103]. Adding synchronization anyway was
// considered and rejected, because it would be unobservable ceremony rather than safety:
//
//   * A reference-typed field is read and written atomically under the CLI memory model, so no
//     reader can observe a torn or partially published reference.
//   * The slot only ever moves forward (DECISION 9), so a concurrent install cannot turn a
//     non-null observation back into null.
//   * The translating overloads copy the field into a local ONCE and then test and dereference the
//     LOCAL. That is not a micro-optimisation: it is what makes the null test and the call refer to
//     the same instance, so an install racing with a translation can only mean the translation used
//     the older provider - never a NullReferenceException, and never two different providers within
//     one call.
//
// The intended pattern - install during startup, translate while serving - additionally gives the
// host's own startup barrier a happens-before edge for free. A `lock` would serialise every
// translation in the process for no behavioural gain, which is a change the oracle cannot express.
//
// ----------------------------------------------------------------------------------------------
// DELIBERATELY ABSENT, each for a stated reason, so nobody "completes" this file by adding one
// ----------------------------------------------------------------------------------------------
//   * Any call to Sprintf, and any formatting at all. This is worth stating explicitly because
//     Kernel's Formatting.cs summarises this folder as one where "the I18n path formats translated
//     text through Sprintf". Checked against the oracle: it does not. i18n.srf never calls Sprintf.
//     Sprintf appears on BOTH SIDES of this entry point - the two XPath providers use it to build a
//     query, and CALL SITES such as ws_objects/pfw.datawindow.services.pbl.src/
//     n_cst_dwsvc_rowselect.sru:L239 use it to format a result they already translated - and in
//     neither case inside this file. Adding a formatting dependency here would be a new capability.
//   * Any logging, ILogger, Console, trace, metric or diagnostic sink. Forbidden twice over: as a
//     dependency the §0.7.2 baseline does not want in a pure shared library, and as behaviour,
//     because logging a miss is precisely what DECISION 3 forbids.
//   * Any `throw`, including argument validation on the installer. The installer's answer to an
//     invalid argument is a return CODE, which is the whole content of i18n.srf:L12; converting it
//     to an exception would change a documented result into a control-flow break.
//   * Any interface over this facade. Consumers substitute behaviour by installing a fake PROVIDER,
//     which is the seam the oracle itself publishes and which II18nProvider already types. A second
//     abstraction over three lines of dispatch would be surface with no legacy counterpart.
//   * Any async or Task-returning form. The oracle TRIGGERS the event rather than posting it, so
//     the mutation is visible on the very next line; an async form would break that sequence.
//   * Any CultureInfo, locale property, resource manager or fallback chain. The locale is not data
//     on this facade - it is WHICH provider was installed [pfw.sra:L95-L102] - and a fallback chain
//     is a capability the oracle does not have.
//   * Any file, network or environment access. This library performs exactly one narrow read, in
//     I18nResourceReader.cs, and this file performs none.
//
// DEPENDENCY SURFACE - one import, three constants, and nothing else
// `using PowerFramework.Shared.Kernel;` is the only import in the file, and the executable code
// reaches exactly three names through it: RetCode.E_INVALID_OBJECT and RetCode.OK
// [retcode.sru:L48, :L39] and Enums.I18N_SRC_PFW [enums.sru:L115]. All three are referenced BY
// IDENTIFIER and never by their literal values -5, 0 and 0, because AAP §0.4.5.3 preserves those
// spellings precisely so they stay legible in log records and in characterization recordings.
//
// Two names appear without an import because they are siblings in this same namespace, so no
// import exists for them to appear in: II18nProvider, which the field and the installer signature
// use, and Categories, which appears in DOCUMENTATION ONLY and in no line of code - it is named
// where a reader needs to know which values a category parameter takes.
//
// Nothing else is reachable from here at all: no package (the project file declares none and this
// file needs none), no other shared library, nothing under services/, and nothing belonging to the
// four deferred services. Notably absent is Kernel's Predicates, for the two reasons DECISION 7
// records; it is discussed in prose above and imported nowhere.
//
// THE LEGACY TREE IS READ-ONLY AND SHARES THIS WORKING DIRECTORY  (AAP §0.7.3 C-C)
// Every `:Lnnn` locator above and below points into ws_objects/, which is the behavioural oracle
// for parity testing: read as specification, never edited, moved or reformatted, and never a build
// input. i18n.srf is the sole specification for this file, and every line number quoted here was
// verified against the file on disk rather than carried forward from a summary - which is how the
// Formatting.cs claim above came to be corrected instead of copied.
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
