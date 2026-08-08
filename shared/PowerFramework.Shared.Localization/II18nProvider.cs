// ==============================================================================================
// II18nProvider.cs
// The installed localization provider's one-member translation contract.
// ==============================================================================================
//
// WHAT THIS FILE IS
// One interface with exactly one member, and deliberately nothing else. It is the contract the
// three locale providers implement and the only thing the I18n facade holds: hand it a source, a
// category and a piece of text, and it either rewrites that text in place and says so, or leaves
// the text alone and says that instead.
//
// Small as it is, it is the fulcrum of this folder. Every other file here either implements this
// member (EnglishProvider.cs, SimplifiedChineseProvider.cs, TraditionalChineseProvider.cs), calls
// it (I18n.cs), or supplies a value for one of its parameters (Categories.cs). Get the signature
// wrong and all five are wrong with it, which is why the reasoning behind every token of it is
// written down below rather than left to be reconstructed.
//
// AUTHORITATIVE SOURCE, AND THE EXACT DECLARATION THIS FILE PORTS
//     ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru:L8-L11
//
//         global type n_cst_i18n from nonvisualobject
//         event type long ontranslate ( long source,  long category,  ref string text )
//         end type
//         global n_cst_i18n n_cst_i18n
//
// That is the whole type. The four lines above are quoted in full rather than excerpted, and the
// source file is only 48 lines long. Everything else in it is inert:
//
//     :L13-L37   a `type variables` block containing ONLY the BSD / four-condition Chinese licence
//                header and NOT ONE VARIABLE, so the type carries no state whatsoever
//     :L39-L47   `create` and `destroy`, which do nothing but `call super` and TriggerEvent the
//                constructor and destructor events, so there is no lifecycle behaviour either
//
// No state, no behaviour, one member. Every decision recorded below follows from those three facts.
//
// WHY AN INTERFACE, WHEN AAP §0.4.5.2 MAPS n_cst_* TO A CLASS
// The object-kind table maps a non-visual custom class to a class, and for the other n_cst_*
// objects in this refactor that is right. This one is the exception, and it is an exception on
// evidence rather than on taste:
//
//   * There is nothing for a base class to hold. No fields (:L13-L37), no methods, no ported
//     constructor body (:L39-L47). A class here would be an empty class, and an empty base class is
//     an interface with extra ceremony that also consumes the single base slot its implementors
//     have.
//   * Three concrete types derive from it, through one intermediate that adds only constants:
//     n_cst_i18n -> ne_cst_i18n -> { n_cst_i18n_en, n_cst_i18n_chs, n_cst_i18n_cht }. The
//     intermediate [ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru:L16-L17] declares
//     CAT_MSGBOX and CAT_DWSVC and nothing else, so it needs no .NET counterpart at all: those two
//     constants live in Categories.cs, and the chain flattens to this interface plus three
//     independent implementing classes.
//   * The legacy already programs against the base type polymorphically. The composition root
//     declares its local as the BASE type and assigns whichever of the three it selected:
//     `n_cst_i18n locale` [ws_objects/pfw.pbl.src/pfw.sra:L89], then a `choose case` over the
//     locale token creating n_cst_i18n_en, _chs or _cht [:L97, :L99, :L101], then `I18N(locale)` to
//     install it [:L103]. A variable of the abstract supertype holding any of its subtypes and
//     dispatching one virtual member IS an interface, written in the only notation PowerBuilder
//     offers.
//
// Keeping it an interface is also what makes the sibling test project's job trivial: a test double
// is a class with one method, with no base to construct and no constructor to satisfy.
//
// THE RETURN VALUE IS PART OF THE CONTRACT - AND THE PLAN'S OWN SUMMARY OF THIS FILE IS WRONG
// AAP §0.4.2.3 summarises this member as "ontranslate(source, category, ref text) - translation
// mutates a `ref string`, it does not return a value", and the neighbouring
// PowerFramework.Shared.Localization.csproj repeats that wording verbatim. The first half is right;
// the second half is not. :L9 declares `event type long ontranslate`, so the member returns a long.
// The correction is recorded HERE, at the point of reproduction, rather than left as a discrepancy
// between plan and code for a later reader to trip over.
//
// It is not a pedantic difference, because one of the three providers consists of nothing else.
// SimplifiedChineseProvider's original is a genuine no-op that NEVER TOUCHES text and communicates
// entirely through its return value
// [ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru:L25-L26]:
//
//         if source = Enums.I18N_SRC_PFW then return 1
//         return 0
//
// Drop the return and that provider becomes literally unrepresentable - an empty method body that
// cannot say what it did. Simplified Chinese is the framework's base locale, so "handled, and the
// text was already correct" is a real answer and a distinct one from "not handled". The alphabet is
// documented in the legacy, in the doc block all three providers carry identically
// [n_cst_i18n_chs.sru:L20-L23, n_cst_i18n_en.sru:L25-L28, n_cst_i18n_cht.sru:L25-L28]:
//
//         source:来源            the originating subsystem
//         category:分类          the resource category
//         text:待转换的文本      the text to be converted
//         返回1代表已处理        "returning 1 means handled"
//
// WHAT THE CALLER DOES WITH IT: NOTHING - AND THAT IS ALSO CONTRACT
// The facade discards it. Both translating overloads of the legacy entry point invoke the event as
// a bare statement and then return the text regardless
// [ws_objects/pfw.ui.pbl.src/i18n.srf:L17, :L21]:
//
//         if IsValid(n_cst_i18n) then n_cst_i18n.Event OnTranslate(Enums.I18N_SRC_PFW,category,ref text)
//         return text
//
// So the return is declared, it is meaningful, every provider produces it - and nothing on the path
// this refactor ports reads it. Both halves belong in the contract. Modelling the return keeps the
// providers faithful and keeps the value reachable from a test and from a characterization
// recording; documenting that the facade ignores it stops a future reader from "improving" I18n.cs
// to branch on it, which would change observable behaviour. In particular a 0 must NOT become an
// exception, a log line, or an untranslated marker: with no provider installed at all the legacy
// simply returns the text unchanged, so a provider answering 0 has to be indistinguishable from
// that. That is the silent-passthrough fallback I18n.cs preserves.
//
// A POWERBUILDER EVENT HAS BECOME A PLAIN METHOD CALL
// AAP §0.4.5.4 names this translation explicitly: a triggered event becomes a method call returning
// a numeric code. `event type long ontranslate` is dispatched by the PowerBuilder runtime through
// the `.Event` syntax visible at i18n.srf:L17 and :L21; here it is an ordinary interface method
// dispatched by the CLR. Two consequences are worth stating because both are easy to assume away:
//
//   * It is SYNCHRONOUS, and it always was. The legacy TRIGGERS the event rather than POSTING it,
//     which is a direct call that completes before the next statement runs - which is precisely why
//     the facade can return `text` on the following line and see the mutation. There is no async
//     variant of this member, and adding one would break that sequence.
//   * `call super::ontranslate` at the head of every provider's handler [n_cst_i18n_chs.sru:L19,
//     n_cst_i18n_en.sru:L24, n_cst_i18n_cht.sru:L24] chains to the base handler, which for THIS
//     type is empty: the base declares the event and implements nothing. An interface has no base
//     implementation to chain to, so nothing is lost, and there is deliberately no default
//     interface method here standing in for that empty body.
//
// THE :L11 GLOBAL AUTO-INSTANCE, AND WHY THIS PROJECT HAS NO `static II18nProvider Current`
// `global n_cst_i18n n_cst_i18n` [:L11] is a global variable whose name is identical to its own
// type name - one of the two shadowing collisions AAP §0.4.5.1 names. It is the installed-provider
// slot: the installer overload assigns to it [i18n.srf:L13] and the two translating overloads read
// it [:L17, :L21].
//
// The resolution §0.4.5.1 prescribes is followed exactly. The TYPE keeps the descriptive .NET name
// (II18nProvider, this file), and the INSTANCE becomes an injected dependency rather than a global.
// The instance is therefore not here: it belongs to I18n.cs, the file that ports i18n.srf's three
// overloads including its installer. Nothing anywhere in this project declares a
// `static II18nProvider Current`, a `Default`, an `Instance`, or any other ambient holder, and that
// absence is a decision rather than an omission. This interface is a pure contract with no registry
// attached, which is also what lets two independent tests install two different fakes without
// either seeing the other's.
//
// WHY `text` IS `ref string?` RATHER THAN `ref string`, AND WHY IT IS `ref` AT ALL
// `ref` is not a stylistic choice: it is how a translation is delivered. The provider REWRITES the
// caller's variable [`text = sTo` at n_cst_i18n_en.sru:L53 and n_cst_i18n_cht.sru:L54] and the
// facade returns that same variable on the next line. A return value, an out parameter or a
// returned tuple would each be a different contract, and each would leave the numeric return code
// with nowhere to go.
//
// The nullable annotation was DECIDED, not defaulted, and it was decided by measurement against
// this repository's own build configuration - `Nullable` enable plus `TreatWarningsAsErrors`, both
// inherited from the root Directory.Build.props:
//
//     ref string?   compiles clean. A facade-shaped caller holding a `string?` passes `ref text`
//                   directly, and an implementor CANNOT dereference text without the compiler
//                   demanding a null check first.
//     ref string    fails. Passing a `string?` variable to it raises CS8601, "Possible null
//                   reference assignment", which this repository treats as an error. The only ways
//                   out are `ref text!` at the call site, which collapses null while telling the
//                   compiler otherwise, or making the whole facade non-nullable, which makes a
//                   PowerBuilder null string unrepresentable. It also lets a provider write
//                   `text.Length` with no diagnostic at all.
//
// AAP §0.4.5.4 forbids collapsing null, and PowerBuilder strings genuinely can be null: the very
// DataWindow handlers that reach CAT_DWSVC at
// ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L355 and :L357 carry an explicit
// null-and-null comparison arm, at :L200 and again at :L372, precisely because a column value may
// be null on either side of a comparison. So the nullable annotation is the only form that is both
// faithful and warning-clean, and the non-nullable form is the one that would have to lie. Two
// obligations follow for the rest of this folder, stated here because this is the file that creates
// them:
//
//   * I18n.cs must declare its own `text` parameter as `string?`, so that it can pass `ref text`
//     with no null-forgiving operator anywhere.
//   * No provider may throw on a null text, and none may quietly substitute the empty string for
//     null in a way that changes what the caller gets back. "Not handled" is the correct answer to
//     a null lookup key; a NullReferenceException is not.
//
// WHY THE OTHER TWO PARAMETERS ARE PLAIN BY-VALUE `long`
//   * `long`, not `int`. AAP §0.4.5.2's type table maps PowerBuilder `long` to C# `long`, and the
//     sibling constant catalogue that supplies these parameters' values already does so:
//     Enums.I18N_SRC_PFW and Enums.I18N_CAT_CUSTOM are declared `public const long`. Narrowing here
//     would force a cast at every call site.
//   * Not an enum. Both spaces are OPEN-ENDED by design. The framework's own two categories are
//     defined as offsets from Enums.I18N_CAT_CUSTOM [ne_cst_i18n.sru:L16-L17], which is an explicit
//     extension point, and Enums.I18N_SRC_CUSTOM is the same extension point for the source. An
//     enum would make a caller-defined category unrepresentable without an unchecked cast, which is
//     worse than an integer that admits to being an integer.
//   * Not `in`. The legacy EVENT declares both as bare `long` [:L9]. It is i18n.srf's FUNCTION
//     parameters that are `readonly` [i18n.srf:L8-L9], so `in` belongs on I18n.cs's signature and
//     not on this one. Copying it here would over-constrain the contract all three providers must
//     implement, for a 64-bit value where `in` buys nothing anyway.
//
// DELIBERATELY ABSENT, EACH FOR A STATED REASON, so nobody "completes" this file by adding one
// The legacy type declares exactly one member, so this interface declares exactly one member. The
// tempting additions, and why each is refused:
//
//   * Install, Current, Default, Instance. Provider installation is i18n.srf's job, not the
//     provider's, and the instance is injected - see the :L11 section above.
//   * Language, Locale, CultureInfo, or any culture property. No provider exposes one. The locale
//     is not data on the provider; it is WHICH provider was constructed, chosen by the composition
//     root's `choose case` [pfw.sra:L95-L102]. The concrete providers then query their table by a
//     hardcoded `@lang='en'` / `@lang='cht'` literal [n_cst_i18n_en.sru:L51,
//     n_cst_i18n_cht.sru:L52], so there is no culture value to surface even if one were wanted.
//   * IDisposable. This type's destructor is `call super::destroy` plus a TriggerEvent [:L44-L47]
//     and holds nothing to release. The two providers that DO own a resource - English and
//     Traditional Chinese load pfw.i18n.xml in their constructor and Destroy it in their destructor
//     [n_cst_i18n_cht.sru:L62-L63, :L66] - are a concrete-class concern, and the mandated .NET
//     substitute is an in-memory XDocument the garbage collector reclaims. Putting IDisposable on
//     the contract would oblige every caller and every test double to dispose something that never
//     needs disposing.
//   * IsAvailable, Supports(category), CanTranslate, or any capability probe. The legacy answers
//     that question by ATTEMPTING the translation and returning 0 when it did not apply: an
//     unmatched category falls straight through the `choose case` leaving the category key empty
//     [n_cst_i18n_en.sru:L34-L47], and an unmatched source returns 0 immediately [:L32]. A probe
//     would be a second, separately-fallible way to ask what the one member already answers.
//   * A "was it translated" out parameter, flag or result record. That is what the return value is.
//   * A default interface method, a static abstract member, or any implementation at all. See the
//     `call super` note above: the base handler is empty and an interface has nothing to chain to.
//     A default body would also hand a provider a way to silently not implement the one member it
//     exists to implement.
//   * Any async or Task-returning form, any logging or ILogger parameter, and any fallback-chain or
//     composite-provider member. All are new capability, which AAP §0.7.3 C-B forbids outright, and
//     the first would break the synchronous mutate-then-read sequence the facade depends on.
//   * Any using directive. This file needs no import at all, not even one the implicit usings
//     already supply. It is a pure leaf: no reference to Kernel, to Categories.cs, or to anything
//     else. That is deliberate - Enums and Categories supply VALUES for these parameters, and a
//     contract must not depend on the catalogue of values that happen to travel through it. Every
//     cross-project name below therefore appears as documentation prose in a `c` element rather
//     than as a resolved cref, which is the same pattern the sibling
//     PowerFramework.Shared.Eventful/VetoResult.cs uses for the same reason.
//   * Any SCREAMING_SNAKE identifier. This file declares no constant, so it needs no entry in the
//     repository-root .editorconfig's scoped naming band, and it deliberately has none. Adding a
//     constant here would silently require one.
//
// THE LEGACY TREE IS READ-ONLY AND SHARES THIS WORKING DIRECTORY
// Every `:Lnnn` locator above and below points into ws_objects/, which is the behavioural oracle for
// parity testing: read as specification, never edited, moved or reformatted, and never a build
// input. n_cst_i18n.sru is the primary and effectively only specification for this file - there is
// no other statement of this contract anywhere to consult - so every claim made here is cited to a
// line, and every line number was verified against the file on disk rather than copied forward.
// ==============================================================================================

namespace PowerFramework.Shared.Localization;

/// <summary>
/// The contract an installed localization provider satisfies: translate one piece of text for one
/// source and one category, in place, and report whether it did. Ported from the single event
/// declared by <c>ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru</c>:L9, which is that object's only
/// member.
/// </summary>
/// <remarks>
/// <para>
/// <b>Exactly one member, because the legacy type has exactly one.</b> The PowerScript original is
/// a <c>nonvisualobject</c> whose entire declaration is the event this interface reproduces
/// [n_cst_i18n.sru:L8-L10]. Its <c>type variables</c> block holds only a licence header and not one
/// variable [:L13-L37], and its <c>create</c> and <c>destroy</c> handlers do nothing but chain to
/// the base and raise the constructor and destructor events [:L39-L47]. There is no state to carry
/// and no lifecycle to model, so there is nothing else for this contract to declare - no
/// installation member, no culture property, no disposal, and no capability probe. The reasoning
/// for each of those omissions is recorded in this file's header rather than left implicit.
/// </para>
/// <para>
/// <b>The three implementors, and the intermediate that vanished.</b> The legacy chain is
/// <c>n_cst_i18n</c> to <c>ne_cst_i18n</c> to the English, Simplified Chinese and Traditional
/// Chinese providers. The intermediate contributes only two constants,
/// <c>CAT_MSGBOX</c> and <c>CAT_DWSVC</c>
/// [<c>ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru</c>:L16-L17], and no behaviour, so it
/// has no .NET counterpart: those constants belong to <c>Categories.cs</c> and the chain flattens
/// to this interface plus three independent implementing classes.
/// </para>
/// <para>
/// <b>How an instance is installed - and where it is not.</b> The legacy composition root declares
/// its local as this abstract supertype, <c>n_cst_i18n locale</c>
/// [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L89], creates one of the three concrete providers from a
/// locale token [:L97, :L99, :L101] and installs it through the facade's installer overload
/// [:L103], which assigns the global slot at
/// <c>ws_objects/pfw.ui.pbl.src/i18n.srf</c>:L13. Following AAP §0.4.5.1, that global slot becomes
/// an injected dependency held by <c>I18n.cs</c> and never an ambient static: this project
/// deliberately declares no <c>Current</c>, <c>Default</c> or <c>Instance</c> holder of this
/// interface anywhere.
/// </para>
/// <para>
/// <b>Implementor obligations, all three of them behavioural rather than structural.</b> A provider
/// must return <c>1</c> when it handled the request and <c>0</c> when it did not; it must deliver a
/// translation only by assigning through the <c>text</c> parameter; and it must tolerate a null
/// <c>text</c> by answering "not handled" rather than throwing. It must not throw to signal an
/// ordinary miss, because the facade discards the return value and has no handler - a thrown
/// exception would escape all the way to the caller of a lookup that the legacy answers by simply
/// leaving the text alone.
/// </para>
/// </remarks>
public interface II18nProvider
{
    /// <summary>
    /// Translate <paramref name="text"/> in place for the given source and category, returning
    /// <c>1</c> if this provider handled the request and <c>0</c> if it did not. Ports
    /// <c>event type long ontranslate ( long source,  long category,  ref string text )</c>
    /// [<c>ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru</c>:L9].
    /// </summary>
    /// <param name="source">
    /// The originating subsystem, documented in the legacy as <c>source:来源</c>
    /// [<c>ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru</c>:L20]. All three shipped
    /// providers translate only for the framework's own source and return <c>0</c> for anything
    /// else - by an equality test at <c>n_cst_i18n_chs.sru</c>:L25 and by an early inequality guard
    /// at <c>n_cst_i18n_en.sru</c>:L32 and <c>n_cst_i18n_cht.sru</c>:L33. Values come from the
    /// kernel constant catalogue, <c>Enums.I18N_SRC_PFW</c> and <c>Enums.I18N_SRC_CUSTOM</c>. It is
    /// a plain <c>long</c> rather than an enum because <c>I18N_SRC_CUSTOM</c> is an open extension
    /// point, and a plain by-value parameter rather than an <c>in</c> parameter because the legacy
    /// event declares it as a bare <c>long</c>; it is the facade's function parameters that are
    /// <c>readonly</c> [<c>ws_objects/pfw.ui.pbl.src/i18n.srf</c>:L8-L9].
    /// </param>
    /// <param name="category">
    /// The resource category, documented in the legacy as <c>category:分类</c>
    /// [<c>n_cst_i18n_chs.sru</c>:L21]. The two XPath providers switch on it to select a table
    /// section and fall straight through when it matches nothing, leaving the section key empty and
    /// so producing <c>0</c> [<c>n_cst_i18n_en.sru</c>:L34-L47]. Values come from
    /// <c>Enums.I18N_CAT_WINDOW</c> through <c>Enums.I18N_CAT_CUSTOM</c> in the kernel catalogue and
    /// from <c>Categories.CAT_MSGBOX</c> and <c>Categories.CAT_DWSVC</c> in this project, the latter
    /// two being offsets from <c>Enums.I18N_CAT_CUSTOM</c>. That offset arrangement is exactly why
    /// this parameter is a <c>long</c> and not an enum: the space is open, and a caller-defined
    /// category has to remain expressible without an unchecked cast.
    /// </param>
    /// <param name="text">
    /// The text to translate, documented in the legacy as <c>text:待转换的文本</c>
    /// [<c>n_cst_i18n_chs.sru</c>:L22]. It is both the input and the output: it is the lookup key
    /// the two XPath providers query with, and it is the channel through which a translation is
    /// delivered - the provider assigns to it [<c>n_cst_i18n_en.sru</c>:L53,
    /// <c>n_cst_i18n_cht.sru</c>:L54] and the facade returns that same variable on the very next
    /// line [<c>i18n.srf</c>:L18, :L22]. A provider that returns <c>0</c> must leave it exactly as
    /// it found it.
    /// <para>
    /// It is annotated nullable deliberately, not incidentally. PowerBuilder strings can be null,
    /// AAP §0.4.5.4 forbids collapsing that null, and a non-nullable annotation here would force
    /// every caller holding a nullable string either to suppress a genuine compiler diagnostic or to
    /// make a null string unrepresentable. Because it is nullable, an implementor cannot dereference
    /// it without the compiler first demanding a null check - which is the mechanism that keeps a
    /// null lookup key answering "not handled" instead of throwing.
    /// </para>
    /// </param>
    /// <returns>
    /// <c>1</c> when this provider handled the request, <c>0</c> when it did not. The legacy states
    /// this as <c>返回1代表已处理</c>, "returning 1 means handled", in the doc block all three
    /// providers carry identically [<c>n_cst_i18n_chs.sru</c>:L23, <c>n_cst_i18n_en.sru</c>:L28,
    /// <c>n_cst_i18n_cht.sru</c>:L28].
    /// <para>
    /// <b>The facade discards this value, and that is contract too.</b> Both translating overloads
    /// of the legacy entry point invoke the event as a bare statement and then return the text
    /// regardless of the answer [<c>i18n.srf</c>:L17-L18, :L21-L22]. It is modelled here because
    /// dropping it would make the Simplified Chinese provider unrepresentable - that provider never
    /// touches the text and communicates only through this value [<c>n_cst_i18n_chs.sru</c>:L25-L26]
    /// - and because a test and a characterization recording can both read it. It must nonetheless
    /// not be acted on by the facade: a <c>0</c> is not an error, is not logged, and does not mark
    /// the text untranslated, because with no provider installed at all the legacy returns the text
    /// unchanged and a provider answering <c>0</c> has to be indistinguishable from that.
    /// </para>
    /// <para>
    /// Values other than <c>1</c> and <c>0</c> have no meaning in this contract. The legacy return
    /// type is a <c>long</c> and is reproduced as one rather than as a boolean, because the width
    /// and the numerals are what travel in recordings; the two documented values are the whole
    /// alphabet, and this member is not part of the tri-state return-code algebra that
    /// <c>RetCode</c> and <c>Predicates</c> govern elsewhere.
    /// </para>
    /// </returns>
    long OnTranslate(long source, long category, ref string? text);
}
