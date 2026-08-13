// ==============================================================================================
// II18nProvider.cs
// The installed localization provider's one-member translation contract.
//
// Source of record: ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru:L8-L11. That object declares one
// event - `event type long ontranslate ( long source, long category, ref string text )` [:L9] -
// plus the global auto-instance that shadows its own type name [:L11]. It carries NO state (its
// `type variables` block holds only the licence header and not one variable [:L13-L37]) and NO
// lifecycle behaviour (`create`/`destroy` only chain to the base and raise their events
// [:L39-L47]). One member, no state, no behaviour: every decision below follows from those facts.
// ==============================================================================================
//
// AN INTERFACE, THOUGH AAP 0.4.5.2 MAPS n_cst_* TO A CLASS - the exception is on evidence:
//   * A class here would be an EMPTY class, and it would consume its implementors' single base slot
//     for nothing. There are no fields, no methods and no constructor body to inherit.
//   * The legacy chain n_cst_i18n -> ne_cst_i18n -> { _en, _chs, _cht } flattens cleanly: the
//     intermediate contributes only CAT_MSGBOX and CAT_DWSVC [ne_cst_i18n.sru:L16-L17], which live
//     in Categories.cs, leaving three independent implementors.
//   * The legacy ALREADY programs against the base polymorphically - `n_cst_i18n locale`
//     [ws_objects/pfw.pbl.src/pfw.sra:L89], a `choose case` creating one of the three [:L97, :L99,
//     :L101], then `I18N(locale)` [:L103]. That is an interface in the only notation PowerBuilder
//     offers.
//
// THE RETURN VALUE IS PART OF THE CONTRACT, AND AAP 0.4.2.3'S SUMMARY OF THIS MEMBER IS WRONG.
// The plan describes it as "translation mutates a `ref string`, it does not return a value". The
// first half is right; :L9 declares `event type long ontranslate`, so it does return one. The
// correction is recorded here, at the point of reproduction; the project file carries the corrected
// wording too, so the two agree.
//
// The alphabet is documented identically in all three providers' doc blocks - 1 means handled, 0
// means not handled [n_cst_i18n_chs.sru:L20-L23, n_cst_i18n_en.sru:L25-L28,
// n_cst_i18n_cht.sru:L25-L28]. It is not pedantic: SimplifiedChineseProvider's original is a
// genuine no-op that NEVER touches text and communicates entirely through the code, returning 1 for
// the framework source and 0 otherwise [n_cst_i18n_chs.sru:L25-L26]. Drop the return and that
// provider becomes unrepresentable. Simplified Chinese is the base locale, so "handled, and the text
// was already correct" is a real answer and distinct from "not handled".
//
// AND THE CALLER DISCARDS IT, WHICH IS ALSO CONTRACT. The legacy entry point invokes the event as a
// bare statement and returns the text regardless [i18n.srf:L17, :L21]. Both halves belong here:
// modelling the return keeps providers faithful and keeps the value reachable from a test and a
// characterization recording, and documenting that the facade ignores it stops a later reader from
// "improving" I18n.cs to branch on it. In particular A 0 MUST NOT BECOME an exception, a log line or
// an untranslated marker - with no provider installed the legacy simply returns the text unchanged,
// so a provider answering 0 has to be indistinguishable from that. That is the silent-passthrough
// fallback I18n.cs preserves.
//
// A POWERBUILDER EVENT HAS BECOME A PLAIN METHOD CALL (AAP 0.4.5.4). Two consequences that are easy
// to assume away: it is SYNCHRONOUS and always was - the legacy TRIGGERS rather than POSTS, which is
// why the facade can return `text` on the following line and see the mutation, so there is no async
// form of this member; and the `call super::ontranslate` at the head of every provider's handler
// [n_cst_i18n_chs.sru:L19, n_cst_i18n_en.sru:L24, n_cst_i18n_cht.sru:L24] chains to a base handler
// that is EMPTY, so an interface loses nothing and needs no default interface method standing in.
//
// `text` IS `ref string?`, AND BOTH HALVES WERE MEASURED RATHER THAN CHOSEN.
// `ref` is how a translation is delivered: the provider rewrites the caller's variable [`text = sTo`
// at n_cst_i18n_en.sru:L53, n_cst_i18n_cht.sru:L54] and the facade returns that same variable next
// line. A return value, an `out` or a tuple would each be a different contract and would leave the
// numeric code nowhere to go. The nullable annotation is forced by this repository's `Nullable`
// enable plus `TreatWarningsAsErrors`: `ref string` raises CS8601 when a `string?` variable is
// passed, whose only escapes are `ref text!` at the call site or a non-nullable facade that makes a
// PowerBuilder null string unrepresentable - and AAP 0.4.5.4 forbids collapsing null, which is real
// here because the DataWindow handlers reaching CAT_DWSVC carry explicit null-and-null comparison
// arms [se_cst_dw.sru:L200, :L372]. Two obligations follow for the rest of this folder:
//   * I18n.cs declares its own `text` as `string?`, so it passes `ref text` with no `!` anywhere.
//   * No provider throws on a null text and none substitutes the empty string for it. "Not handled"
//     is the correct answer to a null lookup key; a NullReferenceException is not.
//
// THE OTHER TWO PARAMETERS ARE PLAIN BY-VALUE `long`: `long` not `int` per AAP 0.4.5.2's type table
// and because Enums.I18N_SRC_PFW and Enums.I18N_CAT_CUSTOM are `const long`; NOT an enum, because
// both spaces are open-ended by design - the framework's categories are offsets from
// Enums.I18N_CAT_CUSTOM [ne_cst_i18n.sru:L16-L17] and Enums.I18N_SRC_CUSTOM is the matching source
// extension point, so an enum would make a caller-defined value unrepresentable without a cast; and
// NOT `in`, because the legacy EVENT declares bare `long` [:L9] - it is i18n.srf's FUNCTION
// parameters that are `readonly` [i18n.srf:L8-L9], so `in` belongs on I18n.cs and not here.
//
// DELIBERATELY ABSENT, so nobody "completes" this file by adding one. The legacy type declares
// exactly one member, so this interface declares exactly one.
//   * Install / Current / Default / Instance, or any ambient holder. Installation is i18n.srf's job
//     and AAP 0.4.5.1 rules that the shadowing global becomes an INJECTED dependency, which lives
//     on I18n.cs. Nothing in this project declares a static holder, and that lets two independent
//     tests install two different fakes without either seeing the other's.
//   * Language / Locale / CultureInfo. No provider exposes one: the locale is WHICH provider was
//     constructed [pfw.sra:L95-L102], and the concrete providers query their table by a hardcoded
//     `@lang` literal [n_cst_i18n_en.sru:L51, n_cst_i18n_cht.sru:L52].
//   * IDisposable. This type holds nothing to release. The two providers that DO own a resource
//     [n_cst_i18n_cht.sru:L62-L63, :L66] are a concrete-class concern whose mandated substitute is
//     an in-memory XDocument, so putting disposal on the contract would oblige every caller and
//     every test double to dispose something that never needs it.
//   * IsAvailable / Supports / CanTranslate, or any capability probe. The legacy answers by
//     ATTEMPTING the translation and returning 0 when it did not apply - an unmatched category falls
//     through the `choose case` [n_cst_i18n_en.sru:L34-L47] and an unmatched source returns 0 at
//     once [:L32] - so a probe would be a second, separately fallible way to ask the same question.
//   * A "was it translated" flag, out parameter or result record. That is the return value.
//   * A default interface method, a static abstract member, or any implementation: the base handler
//     is empty, and a default body would let a provider silently not implement its one member.
//   * Any async form, logging parameter, fallback chain or composite provider - all new capability,
//     which AAP 0.7.3 C-B forbids, and the first would break the mutate-then-read sequence.
//   * Any using directive, and any SCREAMING_SNAKE constant. This is a pure leaf contract: Enums and
//     Categories supply VALUES that travel through it, and a contract must not depend on the
//     catalogue of its own values, so every cross-project name below is prose in a `c` element
//     rather than a resolved cref - the same pattern PowerFramework.Shared.Eventful/VetoResult.cs
//     uses. Declaring no constant is also why this path needs no .editorconfig naming band.
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
