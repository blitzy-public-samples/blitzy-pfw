// ---------------------------------------------------------------------------------------------
// TraditionalChineseProvider.cs
// PowerFramework.Shared.Localization
// ---------------------------------------------------------------------------------------------
//
// PORTED FROM   ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_cht.sru (68 lines)
// DATA SOURCE   pfw.i18n.xml (151 lines), the lang="cht" half at :L77-L151
// ORACLE STATUS Both are READ ONLY. They are the behavioural oracle for parity testing: read as
//               specification, never edited, moved, reformatted or built. Every claim below
//               carries a :Lnnn locator, and every locator was verified against the file on disk
//               rather than copied forward from the English provider, because the two objects are
//               near-identical and a copied locator is exactly the kind of error that would not
//               show up in a diff.
//
// WHAT THE LEGACY OBJECT IS, ENUMERATED IN FULL
// ---------------------------------------------------------------------------------------------
//     :L3, :L7      global type n_cst_i18n_cht from ne_cst_i18n
//     :L9           global n_cst_i18n_cht n_cst_i18n_cht   <- a global auto-instance whose name
//                                                             shadows its own type name
//     :L11-L14      type variables / protected: / privatewrite <XML document handle>
//     :L16-L22      on create / on destroy - each a bare 'call super', no behaviour
//     :L24          event ontranslate;call super::ontranslate;   <- the base call comes FIRST
//     :L24-L29      the doc block: source:来源 / category:分类 / text:待转换的文本 /
//                   返回1代表已处理 ("returning 1 means handled")
//     :L30          string sCat,sTo                              <- the two locals that are used
//     :L31          one further local, of the deferred XML query-result type, DECLARED AND NEVER
//                   REFERENCED anywhere in the file. Deliberately NOT ported; see DECISION 8
//     :L33          the source filter and the first early return
//     :L35-L48      the category-to-element choose case: SIX arms, no 'case else'
//     :L50-L57      the guarded lookup, the //XPATH comment, the query, the hit path
//     :L59          return 0 - the single final fall-through
//     :L62-L63      event constructor - create the document handle, then LoadFile("pfw.i18n.xml"),
//                   and the load result is IGNORED
//     :L66          event destructor - destroy the document handle
//
// That inventory is exhaustive. The object declares no function of its own, no instance variable
// beyond the document handle, and no native binding: one event script and a two-line constructor
// are the whole of it. It is one of only four in-scope objects in pfw.ui.controls.ext.pbl - the
// other 39 are the REFERENCE-only DataWindow service parent and 38 objects deferred to
// DesignSystem - so no reference is followed out of this object into that library.
//
// THE TWIN, AND THE ONE SUBSTANTIVE DIFFERENCE BETWEEN THEM
// ---------------------------------------------------------------------------------------------
// This file is the structural twin of EnglishProvider.cs, and the oracle is the reason: the two
// legacy event scripts are the same script twice over. Diffed line for line,
//
//     n_cst_i18n_cht.sru:L33-L59   against   n_cst_i18n_en.sru:L32-L56
//
// agree on every character EXCEPT the language token inside the query - 'cht' at cht:L52 where
// en:L51 has 'en'. Same source filter, same six arms in the same order, same two shared-arm
// categories, same absent 'case else', same empty-element guard, same non-empty test, same
// assignment, same 1, same final 0. Everything this file does that its twin does not, and
// everything it declines to do that its twin does, is enumerated in DECISION 4, DECISION 8 and
// DECISION 9 - and nowhere else.
//
// The other two differences in the ORACLE are both absences on this side, and both are handled
// deliberately rather than by omission:
//
//     cht:L31 declares an unused local that en does not.            -> NOT ported (DECISION 8)
//     en:L58-L153 carries a dormant translation table that cht
//     does not, and en's data carries two mistranslations that
//     cht's data does not.                                          -> absent here (DECISION 9)
//
// DECISION 1 - A DIRECT II18nProvider IMPLEMENTATION, SEALED, NOT A THREE-LINK CLASS CHAIN
// ---------------------------------------------------------------------------------------------
// AAP §0.4.5.2 maps an 'ne_cst_*' object to a derived class, and the legacy chain is genuinely
// three links deep:
//
//     n_cst_i18n  ->  ne_cst_i18n  ->  n_cst_i18n_cht
//
// Both links are flattened away, on evidence rather than taste, and the evidence is that neither
// has a body to inherit. The ROOT link declares one member and holds no state -
// ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru:L9 declares
// 'event type long ontranslate(long source, long category, ref string text)' and its
// 'type variables' block at :L13-L37 holds a licence header and NOT ONE VARIABLE - so it is
// II18nProvider in this same folder. The MIDDLE link contributes only two constants,
// ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru:L16-L17, so it is Categories in this same
// folder. What is left of the chain once both empty links are removed is this class, implementing
// that interface and reading those constants.
//
// The type is sealed, and that is measured rather than defaulted. Nothing in the 544-object estate
// DERIVES from n_cst_i18n_cht: its single reference anywhere in ws_objects is
// 'locale = Create n_cst_i18n_cht' at ws_objects/pfw.pbl.src/pfw.sra:L101, an instantiation inside
// the composition root's locale switch. It is a leaf, so sealing reproduces the shape the estate
// actually has and declines to publish an extension point no consumer exists for. It costs nothing
// in testability, because the one collaborator arrives through the constructor rather than through
// a virtual member.
//
// DECISION 2 - THE BASE CALL IS OMITTED, AND THE OMISSION IS PROVED RATHER THAN ASSUMED
// ---------------------------------------------------------------------------------------------
// :L24 runs 'call super::ontranslate' BEFORE any of this provider's own logic, so the ordering is
// part of the contract and is honoured below - the deliberate no-op marker sits at the head of
// OnTranslate, ahead of the source filter. There is nothing to invoke, and both ancestors were read
// in full to establish that rather than inferred from their names:
//
//     n_cst_i18n.sru      DECLARES the event at :L9 and implements NO handler for it anywhere in
//                         the file. create/destroy at :L39-L47 only chain to super and raise the
//                         constructor and destructor events.
//     ne_cst_i18n.sru     declares the two constants at :L16-L17 and NO event script at all;
//                         create/destroy at :L20-L26 are bare 'call super'.
//
// So the chained call reaches an empty handler and returns. No base class is invented here to host
// it and no default interface method stands in for it.
//
// DECISION 3 - WHERE THE XPATH IS ASSEMBLED, AND WHY NOT HERE  (constraint C-K)
// ---------------------------------------------------------------------------------------------
// The legacy performs assembly and resolution in one statement, :L52 - the single line on which
// this provider and its twin differ:
//
//     sTo = _doc.Query(Sprintf("string(pfw/{}[@lang='cht']/tr[@text='{}']/@to)",sCat,text)).GetValueString( )
//
// Resolution is not this file's to perform. AAP §0.2.1.3 Correction 6 mandates that the legacy XML
// read be SUBSTITUTED with the base class library rather than ported, so that this library acquires
// no coupling to the deferred Documents XML family (constraint C-D), and I18nResourceReader in this
// same folder is that substitute. THIS FILE'S OWN :L13 AND :L62-L63 ARE THE EVIDENCE THE AAP CITES
// FOR THAT CORRECTION, alongside the English provider's equivalents, and docs/SERVICE_MAPPING.md
// §6.6 and docs/DEFERRED.md §4.3 both record the resolution. This provider therefore holds a reader
// and calls it.
//
// Assembly is the reader's too, and that is a contract fact rather than a preference. Its only
// query member is
//
//     public string Lookup(string language, string category, string text)
//
// which assembles the expression itself from those three parts (I18nResourceReader.cs:L351). It
// publishes NO member that accepts a pre-built expression, and its header records that as a
// deliberate decision - a second way to ask one question would leak the query language to its
// callers. Rendering a second expression here with Kernel's Formatting.Sprintf would therefore
// produce a string with NO CONSUMER: dead code, which this refactor forbids outright, and which
// under this repository's TreatWarningsAsErrors would not merely be untidy but a BUILD FAILURE
// (an assigned-but-unread local is CS0219). EnglishProvider.cs reached the same conclusion for the
// same reason in its own DECISION 3, so the twin diff stays clean on this line too.
//
// What survives here instead is the whole of the requirement's substance:
//
//   (a) THE FORMAT STRING IS RECORDED VERBATIM, in this comment and again at the reproduction site
//       inside OnTranslate, so the expression shape - and in particular the 'cht' token that is
//       this file's reason to exist - remains auditable against :L52 from this file alone.
//   (b) ITS PLACEHOLDERS ARE SEQUENTIAL EMPTY BRACES, '{}' and not '{0}'. That is the
//       PowerFramework Sprintf dialect, which Kernel's Formatting.Sprintf implements: an omitted
//       index takes the next argument in sequence (Formatting.cs:L694-L696). It is NOT .NET
//       composite formatting, and the framework's own dialect is knowable only from usage because
//       the legacy Sprintf lives inside the closed pfw.dll.
//   (c) THE ARGUMENT ORDER IS PRESERVED: the element name first, the source text second.
//   (d) THE LOOKUP IS LANGUAGE-PARAMETERISED FROM THIS SIDE. The token belongs to the provider,
//       exactly as the legacy hardcodes it per provider, and the reader stays language-agnostic.
//       That split is what lets ONE reader serve both table-reading providers, which is the
//       clearest single piece of evidence that the reader's shape is right.
//
// DECISION 4 - THE BODY IS DUPLICATED FROM THE TWIN RATHER THAN SHARED WITH IT
// ---------------------------------------------------------------------------------------------
// The two provider bodies differ only in the language token, so a common base class or a shared
// private helper taking the token would be behaviour-preserving and was genuinely available.
// Duplication is chosen, and the reasons are stated here because the choice has to be legible:
//
//   * IT IS WHAT THE ORACLE IS. The legacy is two independent objects, each with its own complete
//     event script, differing in one token - copy-paste, and deliberately so, because PowerScript
//     offers no way to parameterise a hardcoded predicate. A shared abstraction would be a
//     structure the estate does not have.
//   * NEITHER TYPE CHANGES SHAPE. pfw.sra:L95-L102 constructs one provider or the other from a
//     locale token, so both must remain separate, independently constructible public types
//     [:L97, :L99, :L101]. A base class would additionally consume the single base slot each
//     implementor has, for no member either of them needs.
//   * THE OTHER FILE IS NOT THIS FILE'S TO REWRITE. Factoring a shared base out would require
//     editing EnglishProvider.cs, which is complete, reviewed and already carries its own decision
//     record. A refactor that improves nothing observable is not worth destabilising it for.
//   * NO FACTORY AND NO REGISTRY. There is deliberately no provider factory, no token-keyed lookup
//     and no 'CreateFor(locale)' anywhere: the legacy has none, and the selection lives in the
//     composition root where the legacy puts it.
//
// The cost of duplication is that a genuine correction to the shared shape has to be applied twice.
// That cost is bounded and visible - two files, one screen each - and the sibling test project
// pins both independently, which is what makes divergence detectable rather than silent.
//
// DECISION 5 - THE LOOKUP KEY IS NOT ESCAPED, QUOTED, VALIDATED OR REJECTED HERE  (constraint C-K)
// ---------------------------------------------------------------------------------------------
// :L52 splices the caller's text straight into an XPath expression with no escaping, no quoting
// helper and no validation. Nothing in this file adds any of those. The key is passed to the reader
// exactly as the caller supplied it, byte for byte, and the only transformation applied anywhere on
// the path is the null-to-empty substitution documented at the call site - which is what BOTH
// renderings produce for a null anyway.
//
// Where the legacy's injection analogue went is worth stating precisely, because "preserved" and
// "not reproduced" are both wrong as one-word answers. The legacy exposure is real: an apostrophe
// in the key closes the expression's own string literal, and a BALANCED payload does not merely
// break the expression, it EXTENDS it - returning a translation the caller never asked for, or one
// from a different category element altogether, which crosses the very boundary the switch below
// exists to enforce. The mandated substitute does not build a query string at all: it walks the
// same four steps of the same location path and compares the same two attribute values as DATA
// (I18nResourceReader.cs:L426-L446), so no argument can contribute SYNTAX. That was not an
// oversight but a measured ruling, recorded in the reader at :L373-L402: ALL 126 <tr> entries of
// pfw.i18n.xml resolve byte-identically under both spellings, no text attribute anywhere in the
// table contains an apostrophe, and the only measured divergences are the injection payloads
// themselves - which AAP §0.1.5 permits declining, since the implementation may be safer than the
// legacy where the change is unobservable, and which constraint C-G forbids preserving on a method
// whose output is shown to a user as validation and error text.
//
// The consequence for a reader of THIS file: every ordinary input, including every one of the 63
// cht entries, behaves exactly as the oracle behaves; a key carrying an apostrophe is a MISS rather
// than a throw, which is the same outcome the legacy reaches for an unbalanced payload; and the
// caller-side obligation stands regardless - do not route untrusted text through the translation
// path, because a translation key is not an input channel.
//
// DECISION 6 - CONSTRUCT ONCE, AND NEVER CHECK WHETHER THE TABLE LOADED
// ---------------------------------------------------------------------------------------------
// :L62-L63 creates the document handle and loads the file in the constructor, once per provider
// instance, and :L66 destroys it in the destructor. The reader is therefore constructed once and
// held in a readonly field; it is never re-created per call and never reloaded. Two consequences
// are reproduced deliberately:
//
//   * THE LOAD RESULT IS IGNORED. LoadFile at :L63 returns a value and the legacy does not read it.
//     A missing or unparseable pfw.i18n.xml therefore degrades to every lookup missing, and from
//     there to the facade's silent passthrough - it never throws, never logs and never reports.
//     This file adds no load check, no availability probe and no diagnostic, because each would let
//     a caller take a decision the legacy has no way to take.
//   * NO IDisposable. The destructor's destroy has no managed analogue: the reader holds an
//     in-memory document with no unmanaged handle, is not itself disposable, and is reclaimed by
//     the collector. Implementing IDisposable here would invent a lifetime contract the legacy does
//     not have and put a disposal obligation on every caller and every test.
//
// Both providers load THE SAME FILE BY THE SAME BARE RELATIVE NAME - "pfw.i18n.xml" at cht:L63 and
// at n_cst_i18n_en.sru:L159 - so the resolution rule is shared and neither may resolve it
// differently. The name is taken from I18nResourceReader.DefaultResourceFileName rather than
// respelt here, so the two providers and the test project all name the one literal the legacy
// names.
//
// DECISION 7 - NO PRESERVED-SPELLING CONSTANT IS DECLARED IN THIS FILE
// ---------------------------------------------------------------------------------------------
// The category identifiers this file switches on keep their SCREAMING_SNAKE spelling, but they are
// DECLARED elsewhere - Enums.I18N_CAT_* in Kernel and Categories.CAT_MSGBOX / CAT_DWSVC in this
// project - and are only READ here, by identifier and never as a numeric literal (AAP §0.4.5.3).
// That distinction is load-bearing under this repository's build settings: TreatWarningsAsErrors is
// on for every project and the root .editorconfig scopes its CA1707 and IDE1006 suppressions to the
// exact paths of the files that DECLARE preserved identifiers. Of this project's files only
// Categories.cs is covered; THIS FILE DELIBERATELY IS NOT, so declaring a SCREAMING_SNAKE constant
// here would fail the build rather than merely warn. Every constant declared below is therefore
// ordinary PascalCase carrying a lower-case string VALUE, which is what the resource table's
// element names actually are - data, not identifiers.
//
// DECISION 8 - THE UNUSED LOCAL AT :L31 IS DELIBERATELY NOT PORTED  (constraint C-D, constraint C-K)
// ---------------------------------------------------------------------------------------------
// This is the one thing the twin has no counterpart for, so it is recorded in full rather than left
// to a reviewer's inference. :L30 declares the two locals the script uses, and :L31 declares one
// more - of the deferred XML query-result type - which is NEVER REFERENCED ANYWHERE IN THE FILE:
// not read, not written, not passed and not compared. It is the residue of an earlier spelling of
// :L52, where the query result would have been held before its string value was taken, and it
// survived when that statement was collapsed into a single chained expression.
//
// It was CONSIDERED AND REJECTED, on three independent grounds, any one of which would be
// sufficient:
//
//   1. IT HAS NO OBSERVABLE BEHAVIOUR. An unreferenced local declaration produces no value, no
//      side effect and no allocation the script can detect. Declining to port it therefore cannot
//      change what any caller observes, which is precisely the test constraint C-B applies - the
//      constraint forbids improving BEHAVIOUR, and this declaration has none to improve.
//   2. ITS TYPE IS A MEMBER OF THE DEFERRED DOCUMENTS XML FAMILY. Reproducing it would mean naming
//      or standing in for a type from a capability this phase must not implement even partially, so
//      porting it would breach constraint C-D. That is why this decision is REQUIRED rather than
//      merely permitted, and it is also why neither that type's identifier nor the document
//      handle's at :L13 is spelled anywhere in this file: the audit for this port expects zero
//      occurrences of either name, and a mention in a comment is still an occurrence. Both are
//      referred to descriptively, with their line numbers, which loses nothing - the oracle is one
//      command away and is the authority for its own spelling.
//   3. IT WOULD NOT COMPILE UNDER THIS REPOSITORY'S SETTINGS. An assigned-but-unread or
//      declared-but-unused local raises a compiler diagnostic that TreatWarningsAsErrors turns into
//      an error, so a "faithful" port of the dead declaration would fail the build.
//
// CONTRAST WITH THE DORMANT TABLE IN EnglishProvider.cs, WHICH *IS* CARRIED ACROSS. The two look
// alike - both are dead legacy text - and they are treated oppositely on purpose. That block is
// preserved because it DOCUMENTS A FORBIDDEN CORRECTION: it holds the wording the shipped English
// data gets wrong, so anyone investigating those defects is looking straight at the "fix", and
// carrying it is what records that the defects are defects rather than choices. This is an inert
// variable of a forbidden type: it documents nothing, and reproducing it would import exactly the
// coupling AAP §0.2.1.3 Correction 6 exists to remove.
//
// DECISION 9 - THERE IS NO DORMANT TABLE HERE AND NO MISTRANSLATION TO PRESERVE
// ---------------------------------------------------------------------------------------------
// Two absences, both verified against the oracle rather than assumed from the twin, and both stated
// so that a reader diffing the two providers sees they were checked:
//
//   * NO DORMANT TABLE. n_cst_i18n_cht.sru runs straight from the guarded lookup at :L57 to
//     'return 0' at :L59. The commented-out pre-resource translation table exists ONLY in
//     n_cst_i18n_en.sru, at :L58-L153. It is not copied here, in any form - not as a comment, not
//     as a fallback, not as a fixture.
//   * NO MISTRANSLATION. Both preserved data defects are in the lang="en" half of the resource
//     table, at pfw.i18n.xml:L4 and :L49. Their cht counterparts are correct: :L79 maps the
//     minimise label to itself and :L124 maps the hide label to its proper Traditional form. All
//     six cht elements are present and complete - window, splitcontainer, tabcontrol, ribbonbar,
//     msgbox and dwsvc, 63 entries between them at :L77-L151 - and not one of their values is
//     corrupted. So this file carries no defect-preservation annotation, because there is no defect
//     on this side to preserve, and none was invented to make the twin symmetrical.
//
// A FURTHER PROPERTY OF THE cht DATA THAT THE RETURN VALUE EXISTS FOR. Many cht entries map a value
// to ITSELF - :L78, :L85, :L86, :L105, :L112, :L114 and others - because Traditional and Simplified
// Chinese agree on those characters. Such a lookup is a genuine HIT that leaves the text visibly
// unchanged, and the ONLY way a caller can distinguish it from a miss is the 1 this method returns.
// That is a concrete reason II18nProvider's long return has to exist and must never be collapsed
// into "did the text change".
//
// DELIBERATELY ABSENT, each for a stated reason, so nobody "completes" this file by adding one
// ---------------------------------------------------------------------------------------------
//     * Any fallback of any kind. No chs fallback, no en fallback, no Simplified-to-Traditional
//       conversion and no character folding. A cht entry that is missing must simply MISS and fall
//       through to the final return, because that is what the legacy does; anything else invents
//       translations the table does not contain (constraint C-B).
//     * A culture object, a culture property, a language parameter or a string-localizer adapter -
//       none of the framework's own culture or localization abstractions is reached from here, and
//       the identifiers are deliberately not even named, for the reason given in DECISION 8 item 2.
//       The locale is not data on this provider, it is WHICH provider the composition root
//       constructed (pfw.sra:L95-L102), and the language token is hardcoded in the legacy query at
//       :L52.
//     * Any caching, memoisation or lookup index. The legacy re-evaluates the query on every
//       translate, and AAP §0.1.2 and §0.8.5 forbid asserting any performance objective.
//     * Logging, telemetry or metrics. A miss is the ordinary path here, not an event.
//     * Trimming, casing, normalisation or formatting of the key or the result. The key must match
//       byte for byte and the result must survive byte for byte, braces included: pfw.i18n.xml:L127
//       carries literal braces in its cht value and the call sites fill them with their own Sprintf
//       AFTER the lookup returns, so consuming or rewriting them here would break every one.
//     * A datawindow element, a custom element, or any arm for Enums.I18N_CAT_CUSTOM. All three
//       would be invented data or invented behaviour; see the quirks at the switch below.
//     * Any XML or XPath type, any XML package reference, and any stand-in for either of the two
//       legacy XML types this object mentions. Constraint C-D forbids implementing the deferred
//       Documents capability even partially, and the reader is the mandated substitute.
//     * Any throw on a miss, on a null key or on a malformed key, and any async form. The facade
//       discards this method's return value and has no handler, so an exception here would escape to
//       the caller of a lookup the legacy answers by leaving the text alone. The one guard that does
//       throw is a null reader, which is a caller-contract violation rather than a translation
//       outcome.
// ---------------------------------------------------------------------------------------------

using PowerFramework.Shared.Kernel;

namespace PowerFramework.Shared.Localization;

/// <summary>
/// The Traditional Chinese localization provider: translates PowerFramework's own resource text by
/// reading the <c>lang="cht"</c> half of the <c>pfw.i18n.xml</c> table. Ported from
/// <c>ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_cht.sru</c>.
/// </summary>
/// <remarks>
/// <para>
/// One of the three shipped locale providers, and one of the two that read the resource table. The
/// composition root picks exactly one of them from its locale token and installs it through the
/// <c>I18n</c> entry point [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L95-L103]; this one is selected for
/// the token <c>"cht"</c> at :L101. <see cref="EnglishProvider"/> is selected for <c>"en"</c> at :L97
/// and <see cref="SimplifiedChineseProvider"/> for <c>"chs"</c> at :L99 - the last of which needs no
/// table at all, because Simplified Chinese is the framework's base locale.
/// </para>
/// <para>
/// <b>The structural twin of <see cref="EnglishProvider"/>, differing in one token.</b> The two
/// legacy scripts agree on every character of the ported body -
/// <c>n_cst_i18n_cht.sru</c>:L33-L59 against <c>n_cst_i18n_en.sru</c>:L32-L56 - except the language
/// token inside the query, <c>cht</c> here where the other has <c>en</c>. The body is DUPLICATED
/// rather than shared with it, because the legacy is two independent objects rather than one
/// parameterised one; DECISION 4 in this file's header states the reasoning.
/// </para>
/// <para>
/// <b>No defect is preserved through this provider, and that is a finding rather than an
/// omission.</b> The two documented mistranslations of the resource table are in its
/// <c>lang="en"</c> half only [<c>pfw.i18n.xml</c>:L4, :L49]; the <c>cht</c> counterparts at :L79
/// and :L124 are correct, all six <c>cht</c> elements are present, and none of their 63 values is
/// corrupted. Neither is there a dormant translation table on this side - that exists only in the
/// English original at :L58-L153. See DECISION 9.
/// </para>
/// <para>
/// <b>Two mapping quirks are reproduced exactly.</b> The window and DataWindow categories share one
/// element of the table, and the custom category itself has no arm at all, so it never translates.
/// Both are documented at the switch inside <see cref="OnTranslate"/>.
/// </para>
/// <para>
/// Instances are cheap, immutable after construction and safe to share: the resource table is loaded
/// once by the reader this provider holds and is only read thereafter. There is no disposal
/// obligation.
/// </para>
/// </remarks>
public sealed class TraditionalChineseProvider : II18nProvider
{
    /// <summary>
    /// The language token this provider selects from the resource table, hardcoded in the legacy
    /// query as <c>@lang='cht'</c> [<c>n_cst_i18n_cht.sru</c>:L52].
    /// </summary>
    /// <remarks>
    /// <b>The one substantive difference between this provider and <see cref="EnglishProvider"/>.</b>
    /// It is a constant rather than a parameter or a property because the legacy hardcodes it. The
    /// table holds only <c>en</c> and <c>cht</c> sections, and the choice between them is made by
    /// WHICH provider the composition root constructed, not by configuring one of them. Because the
    /// token lives here and not in the reader, one language-agnostic reader serves both
    /// table-reading providers.
    /// </remarks>
    private const string LanguageToken = "cht";

    /// <summary>
    /// The table element serving both the window and DataWindow categories: <c>window</c>
    /// [<c>n_cst_i18n_cht.sru</c>:L37, <c>pfw.i18n.xml</c>:L77].
    /// </summary>
    private const string WindowElement = "window";

    /// <summary>
    /// The table element serving the ribbon-bar category: <c>ribbonbar</c>
    /// [<c>n_cst_i18n_cht.sru</c>:L39, <c>pfw.i18n.xml</c>:L107].
    /// </summary>
    private const string RibbonBarElement = "ribbonbar";

    /// <summary>
    /// The table element serving the split-container category: <c>splitcontainer</c>
    /// [<c>n_cst_i18n_cht.sru</c>:L41, <c>pfw.i18n.xml</c>:L90].
    /// </summary>
    private const string SplitContainerElement = "splitcontainer";

    /// <summary>
    /// The table element serving the tab-control category: <c>tabcontrol</c>
    /// [<c>n_cst_i18n_cht.sru</c>:L43, <c>pfw.i18n.xml</c>:L104].
    /// </summary>
    private const string TabControlElement = "tabcontrol";

    /// <summary>
    /// The table element serving the message-box category: <c>msgbox</c>
    /// [<c>n_cst_i18n_cht.sru</c>:L45, <c>pfw.i18n.xml</c>:L111].
    /// </summary>
    private const string MessageBoxElement = "msgbox";

    /// <summary>
    /// The table element serving the DataWindow-service category: <c>dwsvc</c>
    /// [<c>n_cst_i18n_cht.sru</c>:L47, <c>pfw.i18n.xml</c>:L126].
    /// </summary>
    /// <remarks>
    /// The only element of the six with an in-scope consumer in this phase: the DataWindow service
    /// layer reaches it from the item-validation path, the row-selection report and the context-menu
    /// captions.
    /// </remarks>
    private const string DataWindowServiceElement = "dwsvc";

    /// <summary>
    /// The resource table, loaded once. Stands in for the legacy protected document handle declared
    /// at [<c>n_cst_i18n_cht.sru</c>:L13].
    /// </summary>
    /// <remarks>
    /// Readonly and constructed once, mirroring the legacy's create-in-constructor lifetime
    /// [:L62-L63]. It is never re-created per call, never reloaded and never replaced. It is also
    /// the whole of this type's state: the legacy declares no other instance variable, and the
    /// unreferenced local at :L31 is deliberately not reproduced as one - see DECISION 8.
    /// </remarks>
    private readonly I18nResourceReader _resourceReader;

    /// <summary>
    /// Creates the provider over the default resource table, reproducing the legacy constructor's
    /// document creation plus <c>LoadFile("pfw.i18n.xml")</c>
    /// [<c>n_cst_i18n_cht.sru</c>:L62-L63].
    /// </summary>
    /// <remarks>
    /// The table is resolved by bare file name against the process working directory, which is the
    /// legacy resolution rule, and it is the SAME file and the same bare name the English provider
    /// loads [<c>n_cst_i18n_en.sru</c>:L159], so neither provider may resolve it differently. A table
    /// that is absent or unreadable does not fail construction: it makes every lookup miss, exactly
    /// as the legacy's unchecked load does. This is the constructor <c>pfw.sra</c>'s open event
    /// reaches at :L101, so it has to work with no arguments at all.
    /// </remarks>
    public TraditionalChineseProvider()
        : this(new I18nResourceReader())
    {
    }

    /// <summary>
    /// Creates the provider over an already-constructed resource table.
    /// </summary>
    /// <param name="resourceReader">
    /// The resource table to read. Supplying it is how a caller points this provider at a table in a
    /// location of its own choosing without changing any resolution behaviour.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="resourceReader"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The guard is a caller-contract violation and is deliberately the only condition in this file
    /// that throws. It is unreachable from the legacy path, where the provider always owns the
    /// document it created, and it is distinct from every data condition: a missing table, an
    /// unmapped category, a malformed key and an untranslated string are all ordinary misses.
    /// </remarks>
    public TraditionalChineseProvider(I18nResourceReader resourceReader)
    {
        ArgumentNullException.ThrowIfNull(resourceReader);

        _resourceReader = resourceReader;
    }

    /// <summary>
    /// Translates <paramref name="text"/> in place from the <c>lang="cht"</c> half of the resource
    /// table, returning <c>1</c> when it did and <c>0</c> when it did not. Ports the
    /// <c>ontranslate</c> event script at <c>n_cst_i18n_cht.sru</c>:L24-L59.
    /// </summary>
    /// <param name="source">
    /// The originating subsystem. Anything other than <see cref="Enums.I18N_SRC_PFW"/> is declined
    /// immediately and the text is left untouched [:L33].
    /// </param>
    /// <param name="category">
    /// The resource category, selecting one element of the table. Six values map to an element and
    /// every other value - including <see cref="Enums.I18N_CAT_CUSTOM"/> itself - maps to nothing and
    /// therefore never translates [:L35-L48].
    /// </param>
    /// <param name="text">
    /// The text to translate, used as the lookup key and overwritten with the translation on a hit
    /// [:L54]. Left exactly as found on a miss, on a declined source, on an unmapped category and
    /// when it is <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <c>1</c> when a translation was found and assigned, otherwise <c>0</c>. The legacy states the
    /// alphabet as <c>返回1代表已处理</c>, "returning 1 means handled" [:L28].
    /// </returns>
    /// <remarks>
    /// <para>
    /// The statement order is the legacy's and is contract: the base call first, then the source
    /// filter, then the category mapping, then the guarded lookup, then a single fall-through. Both
    /// early exits answer <c>0</c> without touching the text, which is what makes an unhandled request
    /// indistinguishable from having no provider installed at all - the silent-passthrough fallback
    /// the entry point preserves.
    /// </para>
    /// <para>
    /// The return value carries information the text alone cannot. Many <c>cht</c> entries map a value
    /// to itself [<c>pfw.i18n.xml</c>:L78, :L85, :L105, :L112], so a hit that changes nothing visible
    /// is reported by the <c>1</c> and by nothing else.
    /// </para>
    /// <para>
    /// Nothing here throws. A miss is the ordinary path, reached by every declined source, every
    /// unmapped category, every untranslated string, every malformed key and an absent resource table
    /// alike.
    /// </para>
    /// </remarks>
    public long OnTranslate(long source, long category, ref string? text)
    {
        // ---------------------------------------------------------------------------------------
        // :L24   event ontranslate;call super::ontranslate;
        //
        // The base call comes FIRST in the legacy, ahead of every statement below, so its position is
        // reproduced here even though there is nothing to invoke. Both ancestors were read in full to
        // establish that: n_cst_i18n.sru DECLARES the event at :L9 and implements no handler, and
        // ne_cst_i18n.sru declares only two constants and no event script at all. The chained call
        // therefore reaches an empty handler and returns.
        //
        // No base class is invented to host it and no default interface method stands in for it -
        // II18nProvider is a pure contract by decision, not by omission. DECISION 2 in the file header
        // records the evidence; this marker exists so the absence reads as deliberate at the exact
        // point the legacy performs the call.
        //
        // :L30-L31 belong here too, and only one of the two lines has a counterpart below. :L30
        // declares the two locals the script uses - the element name and the translation - and they
        // appear as `categoryElement` and `translated`. :L31 declares one further local, of the
        // deferred XML query-result type, which the script NEVER REFERENCES; it is deliberately not
        // ported, for the three independent reasons set out in DECISION 8, the second of which
        // (constraint C-D) makes the non-port required rather than merely permitted.
        // ---------------------------------------------------------------------------------------

        // :L33   if source <> Enums.I18N_SRC_PFW then return 0
        //
        // The source filter, and the first of the two early returns. It runs BEFORE any category
        // work, so a foreign source never reaches the mapping or the table however well its category
        // and text would have matched. Read by identifier rather than as the literal 0, per
        // AAP §0.4.5.3.
        if (source != Enums.I18N_SRC_PFW)
        {
            return 0;
        }

        // ---------------------------------------------------------------------------------------
        // :L35-L48   choose case category ... end choose
        //
        // The category-to-element map, transcribed arm for arm in the legacy's own order, and
        // identical to the English provider's [n_cst_i18n_en.sru:L34-L47] because the two legacy
        // scripts are the same script twice. The legacy declares 'string sCat' at :L30, which
        // PowerScript initialises to the empty string, and the choose case carries NO 'case else'; an
        // unmatched category therefore leaves sCat empty and the guard below skips the lookup
        // entirely. C# requires a switch expression to be exhaustive over long, so the discard arm
        // below is that empty initial value made explicit - it reproduces the ABSENCE of a
        // 'case else' and deliberately guesses nothing.
        //
        // QUIRK 1 - THE WINDOW AND DATAWINDOW CATEGORIES SHARE ONE ELEMENT.       [:L36-L37]
        // Two distinct categories, Enums.I18N_CAT_WINDOW (0) and Enums.I18N_CAT_DATAWINDOW (4), both
        // select 'window'. They are not split here and no 'datawindow' element is invented, which
        // would be inventing data into a read-only file that has none: the cht half of
        // pfw.i18n.xml carries window, splitcontainer, tabcontrol, ribbonbar, msgbox and dwsvc
        // elements at :L77, :L90, :L104, :L107, :L111 and :L126, and nothing else. Measured
        // consequence, and the reason this arm is load-bearing rather than cosmetic: a
        // DataWindow-category lookup of a window caption RESOLVES, because it reads the window
        // element, whereas a query against a 'datawindow' element would match nothing at all.
        //
        // QUIRK 2 - Enums.I18N_CAT_CUSTOM HAS NO ARM, SO IT NEVER TRANSLATES.     [:L35-L48]
        // The switch covers 0, 1, 2, 3, 4, 6 and 7 - and deliberately NOT 5. Enums.I18N_CAT_CUSTOM is
        // the boundary above which a consumer of the framework defines its own categories, not a
        // category with content of its own, so the two that ARE defined above it (6 and 7) have arms
        // and the boundary value does not. Any value the legacy does not list - 5, anything above 7,
        // and any negative - leaves the element name empty, skips the lookup and falls through to the
        // final return. No arm is added for it, no 'custom' element is invented, and the discard arm
        // below does not guess an element on its behalf.
        // ---------------------------------------------------------------------------------------
        string categoryElement = category switch
        {
            // :L36-L37   case Enums.I18N_CAT_WINDOW,Enums.I18N_CAT_DATAWINDOW / sCat = "window"
            Enums.I18N_CAT_WINDOW or Enums.I18N_CAT_DATAWINDOW => WindowElement,

            // :L38-L39   case Enums.I18N_CAT_RIBBONBAR / sCat = "ribbonbar"
            Enums.I18N_CAT_RIBBONBAR => RibbonBarElement,

            // :L40-L41   case Enums.I18N_CAT_SPLITCONTAINER / sCat = "splitcontainer"
            Enums.I18N_CAT_SPLITCONTAINER => SplitContainerElement,

            // :L42-L43   case Enums.I18N_CAT_TABCONTROL / sCat = "tabcontrol"
            Enums.I18N_CAT_TABCONTROL => TabControlElement,

            // :L44-L45   case CAT_MSGBOX / sCat = "msgbox"
            // Inherited from ne_cst_i18n.sru:L16 in the legacy and read from Categories here. It has
            // no in-scope consumer in this phase - both of its consumers are visual objects in the
            // deferred DesignSystem library - and the arm is reproduced regardless, because the map
            // must be faithful rather than pruned by a scheduling accident.
            Categories.CAT_MSGBOX => MessageBoxElement,

            // :L46-L47   case CAT_DWSVC / sCat = "dwsvc"
            // The live one. The DataWindow service layer reaches it from the item-validation path,
            // the row-selection report and the context-menu captions, so this provider sits on a real
            // code path in this phase and not a hypothetical one.
            Categories.CAT_DWSVC => DataWindowServiceElement,

            // :L48   end choose - with no 'case else'. See QUIRK 2 above: this is the empty initial
            // value of sCat made explicit for C#'s exhaustiveness rule, not a default element.
            _ => string.Empty,
        };

        // :L50   if sCat <> "" then
        //
        // The guard that makes QUIRK 2 observable: an unmapped category never reaches the table at
        // all. It is also what keeps a malformed expression from being built for an empty element
        // name, which is why the reader carries no guard of its own for that case.
        if (categoryElement.Length != 0)
        {
            // -----------------------------------------------------------------------------------
            // :L51-L52   //XPATH
            //            sTo = _doc.Query(Sprintf("string(pfw/{}[@lang='cht']/tr[@text='{}']/@to)",sCat,text)).GetValueString( )
            //
            // THE PORTED STATEMENT, AND THE ONE LINE ON WHICH THIS FILE AND ITS TWIN DIFFER: the
            // language token is 'cht' where n_cst_i18n_en.sru:L51 has 'en', and nothing else in the
            // two scripts differs at all. The legacy assembles the expression above with the
            // framework's Sprintf - note the SEQUENTIAL EMPTY BRACES '{}' of the PowerFramework
            // dialect, which are not .NET composite-formatting placeholders - substituting the
            // element name first and the source text second, and then evaluates it against the
            // document it loaded in its constructor.
            //
            // Assembly and evaluation both belong to I18nResourceReader here. That is not a
            // shortcut: AAP §0.2.1.3 Correction 6 requires the legacy XML read to be SUBSTITUTED
            // with the base class library rather than ported, so that this library acquires no
            // coupling to the deferred Documents XML family (constraint C-D), and the reader is that
            // substitute. Its Lookup takes the expression's three variable parts rather than an
            // assembled string, and it publishes no member that accepts one. Rendering a second
            // expression here with Kernel's Formatting.Sprintf would therefore produce a string with
            // no consumer - dead code, and under TreatWarningsAsErrors a build failure rather than
            // merely untidy - so the format string is carried verbatim above instead, which keeps
            // the expression shape and the 'cht' token auditable against :L52 from this file alone.
            // DECISION 3 in the file header states the ruling and the evidence in full.
            //
            // THE KEY IS NOT ESCAPED, QUOTED, VALIDATED OR REJECTED HERE (constraint C-K). It is
            // passed on exactly as the caller supplied it. The legacy's unescaped interpolation is a
            // genuine injection analogue - an apostrophe closes the expression's own string literal,
            // and a BALANCED payload extends it rather than breaking it, returning a translation the
            // caller never asked for or one from a different element, crossing the very category
            // boundary the switch above enforces. The mandated substitute builds no query string at
            // all: it walks the same location path and compares the same two attribute values as
            // DATA (I18nResourceReader.cs:L426-L446), so no argument can contribute SYNTAX. That was
            // measured rather than assumed and is recorded at I18nResourceReader.cs:L373-L402 - all
            // 126 entries of the table resolve byte-identically under both spellings, no text
            // attribute anywhere in it contains an apostrophe, and the only divergences are the
            // injection payloads themselves, which AAP §0.1.5 permits declining because the change
            // is unobservable and which constraint C-G forbids preserving on a method whose output
            // is shown to a user. A key carrying an apostrophe is therefore a MISS, which is also
            // what the legacy answers for an unbalanced payload; DECISION 5 states the position and
            // the caller-side obligation that survives it.
            //
            // THE NULL KEY. II18nProvider declares text as nullable and obliges an implementor to
            // answer "not handled" rather than throw, while the reader requires a non-null key. The
            // empty string is the faithful substitution because it is what BOTH renderings produce
            // for a null: the legacy's Sprintf renders a null argument as the empty string, and so
            // does the reader's comparison. The lookup finds no entry, because no entry in the table
            // carries an empty text attribute, and text is returned still null.
            //
            // THE RESULT IS TAKEN BYTE FOR BYTE: no trimming, no casing, no normalisation, no
            // culture and no formatting. pfw.i18n.xml:L127 carries literal braces in its cht value
            // and the DataWindow service call sites fill them with their own Sprintf AFTER this
            // lookup returns, so consuming or rewriting them here would break every one of them.
            // -----------------------------------------------------------------------------------
            string translated = _resourceReader.Lookup(
                LanguageToken,
                categoryElement,
                text ?? string.Empty);

            // :L53-L56   if sTo <> "" then / text = sTo / return 1 / end if
            //
            // The empty string is the miss signal, and it is the legacy's own: the query is wrapped in
            // XPath's string() function, which yields the empty string for an empty node set, and the
            // legacy tests exactly that. So a hit is any non-empty translation - the assignment is the
            // only mutation this method performs, and 1 is the only value other than 0 it ever
            // returns. Note that a hit need not CHANGE the text: several cht entries map a value to
            // itself, and this return is the only way a caller can tell such a hit from a miss.
            if (translated.Length != 0)
            {
                text = translated;
                return 1;
            }
        }

        // :L59   return 0
        //
        // The single final fall-through, reached by an unmapped category, an untranslated key, a
        // malformed key and an absent resource table alike. The legacy reaches it directly from :L57,
        // with nothing in between: unlike the English provider it carries NO dormant translation
        // table at this position, and none is copied here (DECISION 9). The text is returned exactly
        // as it arrived, which is what makes "not handled" indistinguishable from having no provider
        // installed - the silent passthrough the entry point preserves.
        return 0;
    }
}
