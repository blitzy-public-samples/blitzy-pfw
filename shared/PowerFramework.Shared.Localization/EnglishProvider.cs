// ---------------------------------------------------------------------------------------------
// EnglishProvider.cs
// PowerFramework.Shared.Localization
// ---------------------------------------------------------------------------------------------
//
// PORTED FROM   ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru (164 lines)
// DATA SOURCE   pfw.i18n.xml (151 lines), the lang="en" half at :L2-L76
// ORACLE STATUS Both are READ ONLY. They are the behavioural oracle for parity testing: read as
//               specification, never edited, moved, reformatted or built. That matters more here
//               than almost anywhere else in this refactor, because the two defects this file is
//               required to preserve live in the DATA rather than in the code, and the only way to
//               preserve a defect in a file you may not touch is to READ IT HONESTLY. Every claim
//               below carries a :Lnnn locator, and every locator was verified against the file on
//               disk rather than copied forward.
//
// WHAT THE LEGACY OBJECT IS, ENUMERATED IN FULL
// ---------------------------------------------------------------------------------------------
//     :L3, :L7      global type n_cst_i18n_en from ne_cst_i18n
//     :L9           global n_cst_i18n_en n_cst_i18n_en   <- a global auto-instance whose name
//                                                           shadows its own type name
//     :L11-L14      type variables / protected: / privatewrite n_xmldoc _doc
//     :L16-L22      on create / on destroy - each a bare 'call super', no behaviour
//     :L24          event ontranslate;call super::ontranslate;   <- the base call comes FIRST
//     :L24-L29      the doc block: source:来源 / category:分类 / text:待转换的文本 /
//                   返回1代表已处理 ("returning 1 means handled")
//     :L30          string sCat,sTo                              <- two locals, nothing else
//     :L32          the source filter and the first early return
//     :L34-L47      the category-to-element choose case: SIX arms, no 'case else'
//     :L49-L56      the guarded lookup, the //XPATH comment, the query, the hit path
//     :L58-L153     a commented-out pre-resource translation table - DORMANT, and reproduced as a
//                   comment at the foot of OnTranslate below. It must stay dead; see DECISION 7
//     :L155         return 0 - the single final fall-through
//     :L158-L160    event constructor - Create n_xmldoc then LoadFile("pfw.i18n.xml"), and the
//                   load result is IGNORED
//     :L162         event destructor - Destroy _doc
//
// That inventory is exhaustive. The object declares no function of its own, no instance variable
// beyond the document handle, and no native binding: one event script and a two-line constructor
// are the whole of it.
//
// DECISION 1 - A DIRECT II18nProvider IMPLEMENTATION, NOT A THREE-LINK CLASS CHAIN
// ---------------------------------------------------------------------------------------------
// AAP §0.4.5.2 maps an 'ne_cst_*' object to a derived class, and the legacy chain is genuinely
// three links deep:
//
//     n_cst_i18n  ->  ne_cst_i18n  ->  n_cst_i18n_en
//
// Reproducing both links was considered and rejected on evidence, not taste. The ROOT link
// contributes exactly one member and no state - ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru declares
// 'event type long ontranslate(long source, long category, ref string text)' at :L9 and its
// 'type variables' block at :L13-L37 holds a licence header and NOT ONE VARIABLE - so it is
// II18nProvider in this same folder. The MIDDLE link contributes only two constants,
// ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru:L16-L17, so it is Categories in this same
// folder. Neither has a body to inherit. What is left of the chain once both empty links are
// removed is this class, implementing that interface and reading those constants.
//
// The type is sealed, and that is a measured decision rather than a default. Nothing in the
// 544-object estate DERIVES from n_cst_i18n_en; its single reference anywhere is
// 'locale = Create n_cst_i18n_en' at ws_objects/pfw.pbl.src/pfw.sra:L97, an instantiation. It is a
// leaf, so sealing reproduces the shape the estate actually has and declines to publish an
// extension point no consumer exists for. Sealing costs nothing in testability because the one
// collaborator arrives through the constructor rather than through a virtual member.
//
// DECISION 2 - THE BASE CALL IS OMITTED, AND THE OMISSION IS PROVED RATHER THAN ASSUMED
// ---------------------------------------------------------------------------------------------
// :L24 runs 'call super::ontranslate' BEFORE any of this provider's own logic, so the ordering is
// part of the contract and is honoured below - the deliberate no-op sits at the head of
// OnTranslate, ahead of the source filter. There is nothing to invoke, and both ancestors were
// read in full to establish that rather than inferred from their names:
//
//     n_cst_i18n.sru      DECLARES the event at :L9 and implements NO handler for it anywhere in
//                         the file. create/destroy at :L39-L47 only chain to super and raise the
//                         constructor and destructor events.
//     ne_cst_i18n.sru     declares the two constants at :L16-L17 and NO event script at all;
//                         create/destroy at :L20-L26 are bare 'call super'.
//
// So the chained call reaches an empty handler and returns. No base class is invented here to host
// it, no default interface method stands in for it, and the omission is recorded at the point of
// reproduction so that a later reader can see it was decided rather than forgotten.
//
// DECISION 3 - WHERE THE XPATH IS ASSEMBLED, AND WHY NOT HERE  (constraint C-K)
// ---------------------------------------------------------------------------------------------
// The legacy performs assembly and resolution in one statement, :L51:
//
//     sTo = _doc.Query(Sprintf("string(pfw/{}[@lang='en']/tr[@text='{}']/@to)",sCat,text)).GetValueString( )
//
// Resolution is not this file's to perform. AAP §0.2.1.3 Correction 6 mandates that the n_xmldoc
// read be SUBSTITUTED with System.Xml.Linq rather than ported, so that this library acquires no
// coupling to the deferred Documents XML family (constraint C-D), and I18nResourceReader in this
// same folder is that substitute. This provider therefore holds a reader and calls it.
//
// The brief for this file additionally asked that the XPath be assembled HERE with Kernel's
// Formatting.Sprintf from that exact format string. It is not, and the reason is the reader's
// published contract: its only query member is
//
//     public string Lookup(string language, string category, string text)
//
// which assembles the expression itself, from those three parts, at I18nResourceReader.cs:L360.
// It exposes NO member that accepts a pre-built expression, and its header records that as a
// deliberate design decision at :L130-L134 - "there is no overload taking a pre-built XPath
// expression: that would put two ways to ask the same question on the surface and would leak the
// query language to the callers". Assembling a second expression here would therefore produce a
// string with no consumer: dead code, which this refactor forbids outright, and a second
// expression builder for one query, which is precisely what the reader's author ruled out. The
// brief's own qualifier is the tie-breaker - the reader's resolution behaviour is to be left
// unchanged - and the reader owns assembly as part of that behaviour.
//
// What survives here instead is the whole of the requirement's substance:
//
//   (a) THE FORMAT STRING IS RECORDED VERBATIM, in this comment and again at the reproduction site
//       inside OnTranslate, so the expression shape remains auditable against :L51 from this file.
//       Its placeholders are SEQUENTIAL EMPTY BRACES - '{}' and not '{0}' - which is the
//       PowerFramework Sprintf dialect and not .NET composite formatting; Kernel's
//       Formatting.Sprintf implements that dialect, and string.Format would reject the same text.
//   (b) THE ARGUMENT ORDER IS PRESERVED: the element name first, the source text second.
//   (c) THE RENDERED KEY IS PROVED IDENTICAL, so nothing observable rides on which component
//       assembles it. Kernel's Formatting.Sprintf is a single-pass scanner - it walks the format
//       string once and appends each rendered argument to a StringBuilder without rescanning it
//       (Formatting.cs:L692-L748) - and it renders a null argument as the empty string
//       (Formatting.cs, DECISION 13 CHOICE 3). The reader's interpolation does both of those too.
//       Measured against the real pfw.i18n.xml, the two paths agree on every input class,
//       including the one that matters most: a key carrying literal braces, '第{}行' at
//       pfw.i18n.xml:L52, is substituted ONCE and reaches the query intact under both, so the
//       entry resolves and its own braces survive into the result for a later caller to fill.
//       That last property is load-bearing rather than incidental, and the call sites were counted
//       rather than estimated: ELEVEN in-scope sites wrap this lookup in their own Sprintf, all of
//       them on the '第{}行' row-number entry - n_cst_dwsvc_rowselect.sru:L239 and
//       n_cst_dwsvc_contextmenu.sru:L795, :L863, :L916, :L1009, :L1018, :L1027, :L1033, :L1039,
//       :L1045 and :L1052. Every one of them has the shape Sprintf(I18N(CAT_DWSVC,"第{}行"),nRow),
//       so a provider that consumed, filled or rewrote those braces would break all eleven.
//
// DECISION 4 - THE LOOKUP KEY IS INTERPOLATED UNESCAPED, AND THAT IS PRESERVED  (constraint C-K)
// ---------------------------------------------------------------------------------------------
// :L51 splices the caller's text straight into an XPath expression with no escaping, no quoting
// helper and no validation. It is an injection analogue in the exact sense the SQL sites elsewhere
// in this refactor are, and it is a LIVE one rather than a theoretical one. An apostrophe in the
// key closes the expression's own string literal early, and the two outcomes that follow were both
// MEASURED against the real resource table - both, because only one of them is benign:
//
//   (i)  A MALFORMED EXPRESSION, WHICH IS A MISS. The key "it's" renders as tr[@text='it's'], the
//        expression is rejected, the reader answers with the empty string, the hit guard fails,
//        OnTranslate returns 0, and the I18n facade hands back the caller's text unchanged. A bare
//        apostrophe and a truncated union behave the same way.
//   (ii) A WELL-FORMED INJECTED EXPRESSION, WHICH RETURNS A TRANSLATION THE KEY DOES NOT NAME.
//        This is the serious half, and it is easy to miss when reasoning only about outcome (i).
//        Two verified cases:
//            x' or '1'='1
//                renders as tr[@text='x' or '1'='1'] - a valid predicate that is always true, so it
//                matches EVERY entry of the element and the first in document order is returned.
//                OnTranslate answers 1, with a translation for a key that is not in the table.
//            x'] | pfw/msgbox[@lang='en']/tr[@text='确定
//                renders as tr[@text='x'] | pfw/msgbox[@lang='en']/tr[@text='确定'] - a union that
//                reaches into a DIFFERENT element of the table. It CROSSES THE CATEGORY BOUNDARY
//                the switch above exists to enforce, returning the message-box entry from a
//                window-category lookup.
//
// The legacy carries precisely the same exposure, and that was established rather than assumed: for
// every key above, the expression Kernel's Sprintf renders from the legacy format string at :L51 is
// CHARACTER-IDENTICAL to the expression the reader interpolates. XPath's string() of a node set
// takes the first node in document order, which is the rule the reader reproduces, so the injected
// results agree too.
//
// The behaviour is therefore PRESERVED, not corrected. Escaping or rejecting the key would make
// this port answer differently from the oracle on every input in class (ii) - a behavioural change
// (constraint C-B) dressed up as a fix, and a silent one. It is documented at its point of
// reproduction instead - here, and again at the call inside OnTranslate - and the reader carries the
// matching note plus the catch that turns a malformed expression into an ordinary miss
// (I18nResourceReader.cs:L335-L345, :L374-L381). Nothing in this file sanitises, quotes, rejects or
// reports a key.
//
// WHO CAN REACH IT, stated so the risk can be judged rather than guessed. The key is not free input
// today: every in-scope caller passes a literal authored in the DataWindow service layer. The
// exposure becomes reachable the moment a caller passes text it did not author - a column value, a
// user-entered filter, or a message assembled from either. Callers must therefore not route
// untrusted text through the translation path. That obligation sits with the caller because the fix
// cannot be applied here without diverging from the oracle, and recording it is what turns a
// preserved defect into a known one rather than a latent one.
//
// DECISION 5 - CONSTRUCT ONCE, AND NEVER CHECK WHETHER THE TABLE LOADED
// ---------------------------------------------------------------------------------------------
// :L158-L160 creates the document and loads the file in the constructor, once per provider
// instance, and :L162 destroys it in the destructor. The reader is therefore constructed once and
// held in a readonly field; it is never re-created per call and never reloaded. Two consequences
// are reproduced deliberately:
//
//   * THE LOAD RESULT IS IGNORED. LoadFile at :L159 returns a value and the legacy does not read
//     it. A missing or unparseable pfw.i18n.xml therefore degrades to every lookup missing, and
//     from there to the facade's silent passthrough - it never throws, never logs and never
//     reports. This file adds no load check, no availability probe and no diagnostic, because each
//     would let a caller take a decision the legacy has no way to take.
//   * NO IDisposable. The destructor's 'Destroy _doc' has no managed analogue: the reader holds an
//     in-memory document with no unmanaged handle, is not itself disposable, and is reclaimed by
//     the collector. Implementing IDisposable here would invent a lifetime contract the legacy
//     does not have and put a disposal obligation on every caller and every test.
//
// DECISION 6 - NO PRESERVED-SPELLING CONSTANT IS DECLARED IN THIS FILE
// ---------------------------------------------------------------------------------------------
// The category identifiers this file switches on keep their SCREAMING_SNAKE spelling, but they are
// DECLARED elsewhere - Enums.I18N_CAT_* in Kernel and Categories.CAT_MSGBOX / CAT_DWSVC in this
// project - and are only READ here, by identifier and never as a numeric literal (AAP §0.4.5.3).
// That distinction is load-bearing under this repository's build settings: TreatWarningsAsErrors
// is on for every project and the root .editorconfig scopes its CA1707 and IDE1006 suppressions to
// the exact paths of the files that DECLARE preserved identifiers. Categories.cs is one of those
// paths; this file deliberately is not. Every constant declared below is therefore ordinary
// PascalCase carrying a lower-case string VALUE, which is what the resource table's element names
// actually are - data, not identifiers.
//
// DECISION 7 - THE DORMANT TABLE AT :L58-L153 STAYS DEAD
// ---------------------------------------------------------------------------------------------
// The legacy carries a complete commented-out translation table between the guarded lookup and the
// final return. It predates the resource file, it is unreachable, and - this is the dangerous part
// - it is CORRECT where the shipped data is wrong. It is reproduced verbatim as a comment at the
// foot of OnTranslate, at the position it occupies in the source, with the reasons it must not be
// revived stated beside it. AAP §0.8.2 is explicit: reviving it would be exactly the silent
// correction this refactor forbids. It is not converted into a fallback, a dictionary, a seed, a
// test fixture or a conditional branch, and no live statement in this file references it.
//
// DELIBERATELY ABSENT, each for a stated reason, so nobody "completes" this file by adding one
// ---------------------------------------------------------------------------------------------
//     * Any repair, special case or warning for the two mistranslated entries. They are data
//       defects in a read-only file and are required to be observable; see the note at the lookup.
//     * A CultureInfo, a culture property, a language parameter or an IStringLocalizer adapter.
//       The locale is not data on this provider, it is WHICH provider the composition root
//       constructed (pfw.sra:L95-L102), and the language token is hardcoded in the legacy query.
//     * Any caching, memoisation or lookup index. The legacy re-evaluates the query on every
//       translate, and AAP §0.1.2 and §0.8.5 forbid asserting any performance objective.
//     * Logging, telemetry or metrics. A miss is the ordinary path here, not an event.
//     * Trimming, casing, normalisation or formatting of the key or the result. The key must match
//       byte for byte and the result must survive byte for byte, braces included.
//     * A datawindow element, a custom element, or any arm for Enums.I18N_CAT_CUSTOM. All three
//       would be invented data or invented behaviour; see the quirks at the switch below.
//     * Any XML type, XPath type, XML package reference or n_xmldoc/n_xmlqueryresult stand-in.
//       Constraint C-D forbids implementing the deferred Documents capability even partially, and
//       the reader is the mandated substitute.
//     * Any throw on a miss, on a null key or on a malformed expression, and any async form. The
//       facade discards this method's return value and has no handler, so an exception here would
//       escape to the caller of a lookup the legacy answers by leaving the text alone.
// ---------------------------------------------------------------------------------------------

using PowerFramework.Shared.Kernel;

namespace PowerFramework.Shared.Localization;

/// <summary>
/// The English localization provider: translates PowerFramework's own resource text by reading the
/// <c>lang="en"</c> half of the <c>pfw.i18n.xml</c> table. Ported from
/// <c>ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru</c>.
/// </summary>
/// <remarks>
/// <para>
/// One of the three shipped locale providers, and one of the two that read the resource table. The
/// composition root picks exactly one of them from its locale token and installs it through the
/// <c>I18n</c> entry point [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L95-L103]; this one is selected
/// for the token <c>"en"</c> at :L97. The Simplified Chinese provider needs no table at all,
/// because Simplified Chinese is the framework's base locale.
/// </para>
/// <para>
/// <b>Two documented defects are reproduced through this provider, and both live in the data.</b>
/// The provider logic is faithful and the resource file is what is wrong, so preserving the defects
/// requires only that nothing here repair them on read: the entry at <c>pfw.i18n.xml</c>:L4
/// resolves the minimise label to the English word for its opposite, so that label and the one at
/// :L3 collapse onto a single English word, and the entry at :L49 resolves the hide label to a
/// corrupted mixed string. Both are required to remain observable [AAP §0.8.2], the resource file
/// is read-only [constraint C-C], and the wording those entries should carry survives only inside
/// the dormant block reproduced at the foot of <see cref="OnTranslate"/> - which must not be
/// revived.
/// </para>
/// <para>
/// <b>Two mapping quirks are reproduced exactly.</b> The window and DataWindow categories share one
/// element of the table, and the custom category itself has no arm at all, so it never translates.
/// Both are documented at the switch inside <see cref="OnTranslate"/>.
/// </para>
/// <para>
/// Instances are cheap, immutable after construction and safe to share: the resource table is
/// loaded once by the reader this provider holds and is only read thereafter. There is no disposal
/// obligation.
/// </para>
/// </remarks>
public sealed class EnglishProvider : II18nProvider
{
    /// <summary>
    /// The language token this provider selects from the resource table, hardcoded in the legacy
    /// query as <c>@lang='en'</c> [<c>n_cst_i18n_en.sru</c>:L51].
    /// </summary>
    /// <remarks>
    /// It is a constant rather than a parameter or a property because the legacy hardcodes it. The
    /// table holds only <c>en</c> and <c>cht</c> sections, and the choice between them is made by
    /// WHICH provider the composition root constructed, not by configuring one of them.
    /// </remarks>
    private const string LanguageToken = "en";

    /// <summary>
    /// The table element serving both the window and DataWindow categories: <c>window</c>
    /// [<c>n_cst_i18n_en.sru</c>:L36, <c>pfw.i18n.xml</c>:L2].
    /// </summary>
    private const string WindowElement = "window";

    /// <summary>
    /// The table element serving the ribbon-bar category: <c>ribbonbar</c>
    /// [<c>n_cst_i18n_en.sru</c>:L38, <c>pfw.i18n.xml</c>:L32].
    /// </summary>
    private const string RibbonBarElement = "ribbonbar";

    /// <summary>
    /// The table element serving the split-container category: <c>splitcontainer</c>
    /// [<c>n_cst_i18n_en.sru</c>:L40, <c>pfw.i18n.xml</c>:L15].
    /// </summary>
    private const string SplitContainerElement = "splitcontainer";

    /// <summary>
    /// The table element serving the tab-control category: <c>tabcontrol</c>
    /// [<c>n_cst_i18n_en.sru</c>:L42, <c>pfw.i18n.xml</c>:L29].
    /// </summary>
    private const string TabControlElement = "tabcontrol";

    /// <summary>
    /// The table element serving the message-box category: <c>msgbox</c>
    /// [<c>n_cst_i18n_en.sru</c>:L44, <c>pfw.i18n.xml</c>:L36].
    /// </summary>
    private const string MessageBoxElement = "msgbox";

    /// <summary>
    /// The table element serving the DataWindow-service category: <c>dwsvc</c>
    /// [<c>n_cst_i18n_en.sru</c>:L46, <c>pfw.i18n.xml</c>:L51].
    /// </summary>
    /// <remarks>
    /// The only element of the six with an in-scope consumer in this phase: the DataWindow service
    /// layer reaches it from the item-validation path, the row-selection report and the context-menu
    /// captions.
    /// </remarks>
    private const string DataWindowServiceElement = "dwsvc";

    /// <summary>
    /// The resource table, loaded once. Stands in for the legacy <c>privatewrite n_xmldoc _doc</c>
    /// [<c>n_cst_i18n_en.sru</c>:L13].
    /// </summary>
    /// <remarks>
    /// Readonly and constructed once, mirroring the legacy's create-in-constructor lifetime
    /// [:L158-L160]. It is never re-created per call, never reloaded and never replaced.
    /// </remarks>
    private readonly I18nResourceReader _resourceReader;

    /// <summary>
    /// Creates the provider over the default resource table, reproducing the legacy constructor's
    /// <c>Create n_xmldoc</c> plus <c>LoadFile("pfw.i18n.xml")</c>
    /// [<c>n_cst_i18n_en.sru</c>:L158-L160].
    /// </summary>
    /// <remarks>
    /// The table is resolved by bare file name against the process working directory, which is the
    /// legacy resolution rule. A table that is absent or unreadable does not fail construction: it
    /// makes every lookup miss, exactly as the legacy's unchecked load does.
    /// </remarks>
    public EnglishProvider()
        : this(new I18nResourceReader())
    {
    }

    /// <summary>
    /// Creates the provider over an already-constructed resource table.
    /// </summary>
    /// <param name="resourceReader">
    /// The resource table to read. Supplying it is how a caller points this provider at a table in
    /// a location of its own choosing without changing any resolution behaviour.
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
    public EnglishProvider(I18nResourceReader resourceReader)
    {
        ArgumentNullException.ThrowIfNull(resourceReader);

        _resourceReader = resourceReader;
    }

    /// <summary>
    /// Translates <paramref name="text"/> in place from the <c>lang="en"</c> half of the resource
    /// table, returning <c>1</c> when it did and <c>0</c> when it did not. Ports the
    /// <c>ontranslate</c> event script at <c>n_cst_i18n_en.sru</c>:L24-L155.
    /// </summary>
    /// <param name="source">
    /// The originating subsystem. Anything other than <see cref="Enums.I18N_SRC_PFW"/> is declined
    /// immediately and the text is left untouched [:L32].
    /// </param>
    /// <param name="category">
    /// The resource category, selecting one element of the table. Six values map to an element and
    /// every other value - including <see cref="Enums.I18N_CAT_CUSTOM"/> itself - maps to nothing
    /// and therefore never translates [:L34-L47].
    /// </param>
    /// <param name="text">
    /// The text to translate, used as the lookup key and overwritten with the translation on a hit
    /// [:L53]. Left exactly as found on a miss, on a declined source, on an unmapped category and
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
    /// early exits answer <c>0</c> without touching the text, which is what makes an unhandled
    /// request indistinguishable from having no provider installed at all - the silent-passthrough
    /// fallback the entry point preserves.
    /// </para>
    /// <para>
    /// Nothing here throws. A miss is the ordinary path, reached by every declined source, every
    /// unmapped category, every untranslated string, every malformed key and an absent resource
    /// table alike.
    /// </para>
    /// </remarks>
    public long OnTranslate(long source, long category, ref string? text)
    {
        // ---------------------------------------------------------------------------------------
        // :L24   event ontranslate;call super::ontranslate;
        //
        // The base call comes FIRST in the legacy, ahead of every statement below, so its position
        // is reproduced here even though there is nothing to invoke. Both ancestors were read in
        // full to establish that: n_cst_i18n.sru DECLARES the event at :L9 and implements no
        // handler, and ne_cst_i18n.sru declares only two constants and no event script at all. The
        // chained call therefore reaches an empty handler and returns.
        //
        // No base class is invented to host it and no default interface method stands in for it -
        // II18nProvider is a pure contract by decision, not by omission. DECISION 2 in the file
        // header records the evidence; this marker exists so the absence reads as deliberate at the
        // exact point the legacy performs the call.
        // ---------------------------------------------------------------------------------------

        // :L32   if source <> Enums.I18N_SRC_PFW then return 0
        //
        // The source filter, and the first of the two early returns. It runs BEFORE any category
        // work, so a foreign source never reaches the mapping or the table however well its
        // category and text would have matched. Read by identifier rather than as the literal 0,
        // per AAP §0.4.5.3.
        if (source != Enums.I18N_SRC_PFW)
        {
            return 0;
        }

        // ---------------------------------------------------------------------------------------
        // :L34-L47   choose case category ... end choose
        //
        // The category-to-element map, transcribed arm for arm in the legacy's own order. The
        // legacy declares 'string sCat' at :L30, which PowerScript initialises to the empty string,
        // and the choose case carries NO 'case else'; an unmatched category therefore leaves sCat
        // empty and the guard below skips the lookup entirely. C# requires a switch expression to
        // be exhaustive over long, so the discard arm below is that empty initial value made
        // explicit - it reproduces the ABSENCE of a 'case else' and deliberately guesses nothing.
        //
        // QUIRK 1 - THE WINDOW AND DATAWINDOW CATEGORIES SHARE ONE ELEMENT.       [:L35-L36]
        // Two distinct categories, Enums.I18N_CAT_WINDOW (0) and Enums.I18N_CAT_DATAWINDOW (4),
        // both select 'window'. They are not split here and no 'datawindow' element is invented,
        // which would be inventing data into a read-only file that has none: pfw.i18n.xml carries
        // window, splitcontainer, tabcontrol, ribbonbar, msgbox and dwsvc elements and nothing
        // else. Measured consequence, and the reason this arm is load-bearing rather than cosmetic:
        // a DataWindow-category lookup of a window caption RESOLVES, because it reads the window
        // element, whereas a query against a 'datawindow' element would match nothing at all.
        //
        // QUIRK 2 - Enums.I18N_CAT_CUSTOM HAS NO ARM, SO IT NEVER TRANSLATES.     [:L34-L47]
        // The switch covers 0, 1, 2, 3, 4, 6 and 7 - and deliberately NOT 5. Enums.I18N_CAT_CUSTOM
        // is the boundary above which a consumer of the framework defines its own categories, not a
        // category with content of its own, so the two that ARE defined above it (6 and 7) have arms
        // and the boundary value does not. Any value the legacy does not list - 5, anything above 7,
        // and any negative - leaves the element name empty, skips the lookup and falls through to
        // the final return. No arm is added for it, no 'custom' element is invented, and the discard
        // arm below does not guess an element on its behalf.
        // ---------------------------------------------------------------------------------------
        string categoryElement = category switch
        {
            // :L35-L36   case Enums.I18N_CAT_WINDOW,Enums.I18N_CAT_DATAWINDOW / sCat = "window"
            Enums.I18N_CAT_WINDOW or Enums.I18N_CAT_DATAWINDOW => WindowElement,

            // :L37-L38   case Enums.I18N_CAT_RIBBONBAR / sCat = "ribbonbar"
            Enums.I18N_CAT_RIBBONBAR => RibbonBarElement,

            // :L39-L40   case Enums.I18N_CAT_SPLITCONTAINER / sCat = "splitcontainer"
            Enums.I18N_CAT_SPLITCONTAINER => SplitContainerElement,

            // :L41-L42   case Enums.I18N_CAT_TABCONTROL / sCat = "tabcontrol"
            Enums.I18N_CAT_TABCONTROL => TabControlElement,

            // :L43-L44   case CAT_MSGBOX / sCat = "msgbox"
            // Inherited from ne_cst_i18n.sru:L16 in the legacy and read from Categories here. It
            // has no in-scope consumer in this phase - both of its consumers are visual objects in
            // the deferred DesignSystem library - and the arm is reproduced regardless, because the
            // map must be faithful rather than pruned by a scheduling accident.
            Categories.CAT_MSGBOX => MessageBoxElement,

            // :L45-L46   case CAT_DWSVC / sCat = "dwsvc"
            // The live one. The DataWindow service layer reaches it from the item-validation path,
            // the row-selection report and the context-menu captions, so this provider sits on a
            // real code path in this phase and not a hypothetical one.
            Categories.CAT_DWSVC => DataWindowServiceElement,

            // :L47   end choose - with no 'case else'. See QUIRK 2 above: this is the empty initial
            // value of sCat made explicit for C#'s exhaustiveness rule, not a default element.
            _ => string.Empty,
        };

        // :L49   if sCat <> "" then
        //
        // The guard that makes QUIRK 2 observable: an unmapped category never reaches the table at
        // all. It is also what keeps a malformed expression from being built for an empty element
        // name, which is why the reader carries no guard of its own for that case.
        if (categoryElement.Length != 0)
        {
            // -----------------------------------------------------------------------------------
            // :L50-L51   //XPATH
            //            sTo = _doc.Query(Sprintf("string(pfw/{}[@lang='en']/tr[@text='{}']/@to)",sCat,text)).GetValueString( )
            //
            // THE PORTED STATEMENT. The legacy assembles an XPath expression with the framework's
            // Sprintf - note the SEQUENTIAL EMPTY BRACES '{}' of the PowerFramework dialect, which
            // are not .NET composite-formatting placeholders - substituting the element name first
            // and the source text second, and then evaluates it against the document it loaded in
            // its constructor.
            //
            // Assembly and evaluation both belong to I18nResourceReader here. That is not a
            // shortcut: AAP §0.2.1.3 Correction 6 requires the n_xmldoc read to be SUBSTITUTED
            // with System.Xml.Linq rather than ported, so that this library acquires no coupling to
            // the deferred Documents XML family (constraint C-D), and the reader is that
            // substitute. Its Lookup takes the expression's three variable parts rather than an
            // assembled string, and it publishes no member that accepts one - a decision recorded
            // at I18nResourceReader.cs:L130-L134. Building a second expression here with Sprintf
            // would produce a string with no consumer, which is dead code, and a second expression
            // builder for one query. The format string above is carried verbatim instead, so the
            // expression shape stays auditable against :L51 from this file, and the equivalence was
            // measured rather than assumed: Kernel's Sprintf is a single-pass scanner that appends
            // each rendered argument without rescanning it, and it renders a null argument as the
            // empty string, so the key it produces is byte-identical to the key the reader
            // interpolates - for every input, including one carrying literal braces. DECISION 3 in
            // the file header states the ruling and the evidence in full.
            //
            // THE KEY IS INTERPOLATED UNESCAPED, AND THAT IS PRESERVED (constraint C-K). Neither
            // the legacy nor this port escapes, quotes or validates the key, so an apostrophe in it
            // closes the expression's own string literal early. This is the same injection analogue
            // as the SQL sites elsewhere in this refactor, it is LIVE rather than theoretical, and
            // it has TWO measured outcomes - only one of which is benign:
            //     "it's"                              -> a MALFORMED expression. Rejected, the
            //                                            reader answers empty, the guard below
            //                                            fails, this method returns 0 and the
            //                                            caller's text is untouched.
            //     "x' or '1'='1"                       -> a WELL-FORMED always-true predicate. It
            //                                            matches every entry of the element and the
            //                                            first in document order is returned, so
            //                                            this method answers 1 with a translation
            //                                            for a key that is not in the table.
            //     "x'] | pfw/msgbox[...]/tr[@text='…"  -> a WELL-FORMED union reaching a DIFFERENT
            //                                            element, which CROSSES the category
            //                                            boundary the switch above enforces.
            // The legacy is identical on all three: the expression its Sprintf renders from the
            // format string above is character-identical to the one the reader interpolates, and
            // XPath's string() takes the first node in document order just as the reader does.
            // Escaping or rejecting the key would therefore diverge from the oracle on every
            // injected input - a behavioural change (constraint C-B), and a silent one. It is
            // documented here and in DECISION 4 rather than fixed, together with the caller-side
            // obligation that follows from it: do not route untrusted text through this path.
            //
            // THE NULL KEY. II18nProvider declares text as nullable and obliges an implementor to
            // answer "not handled" rather than throw, while the reader requires a non-null key. The
            // empty string is the faithful substitution because it is what BOTH renderings produce
            // for a null: the legacy's Sprintf renders a null argument as the empty string, and so
            // does interpolation. The query becomes tr[@text=''], no entry in the table carries an
            // empty text attribute, the lookup misses, and text is returned still null.
            //
            // TWO DATA DEFECTS SURFACE THROUGH THIS CALL AND MUST NOT BE REPAIRED. The resource
            // file is read-only (constraint C-C) and AAP §0.8.2 requires both to stay observable:
            //     pfw.i18n.xml:L4    the minimise label resolves to the English word for its
            //                        OPPOSITE, so it and the label at :L3 collapse onto one word
            //     pfw.i18n.xml:L49   the hide label resolves to a corrupted mixed string, the
            //                        residue of a botched search-and-replace frozen into the data
            // Nothing here repairs, special-cases, post-processes, flags or warns about either. The
            // wording those two entries should carry survives ONLY inside the dormant block quoted
            // below, and reviving it is exactly the silent correction this refactor forbids.
            //
            // The result is taken byte for byte in every other respect too: no trimming, no casing,
            // no normalisation, no culture and no formatting. The entry at pfw.i18n.xml:L52 carries
            // literal braces in its translation and ELEVEN in-scope call sites fill them with their
            // own Sprintf AFTER the lookup returns - n_cst_dwsvc_rowselect.sru:L239 and
            // n_cst_dwsvc_contextmenu.sru:L795, :L863, :L916, :L1009, :L1018, :L1027, :L1033,
            // :L1039, :L1045, :L1052 - so filling or rewriting them here would break all eleven.
            // -----------------------------------------------------------------------------------
            string translated = _resourceReader.Lookup(
                LanguageToken,
                categoryElement,
                text ?? string.Empty);

            // :L52-L55   if sTo <> "" then / text = sTo / return 1 / end if
            //
            // The empty string is the miss signal, and it is the legacy's own: the query is wrapped
            // in XPath's string() function, which yields the empty string for an empty node set, and
            // the legacy tests exactly that. So a hit is any non-empty translation - the assignment
            // is the only mutation this method performs, and 1 is the only value other than 0 it
            // ever returns.
            if (translated.Length != 0)
            {
                text = translated;
                return 1;
            }
        }

        // ===================================================================================
        //  DORMANT - DO NOT REVIVE - DO NOT UNCOMMENT - DO NOT CONVERT INTO A FALLBACK
        //  n_cst_i18n_en.sru:L58-L153, reproduced verbatim at the position it occupies in the
        //  source: between the guarded lookup that ends at :L56 and the final return at :L155.
        // -----------------------------------------------------------------------------------
        //  WHAT IT IS. The pre-resource translation table. Before pfw.i18n.xml existed, this
        //  provider translated by matching the text against literals in a nested choose case.
        //  When the table moved into the resource file the code was commented out rather than
        //  deleted, and it has been dead ever since. It is unreachable in the legacy and it is
        //  unreachable here.
        //
        //  WHY IT IS THE MOST DANGEROUS ARTIFACT IN THIS FILE. It is CORRECT where the shipped
        //  data is WRONG. Two of its entries carry the wording the resource file mistranslates -
        //  the minimise label at :L64-L65 and the hide label at :L149-L150, against
        //  pfw.i18n.xml:L4 and :L49 respectively - so anyone who reads this block while
        //  investigating either defect is looking directly at the "fix". Applying it would be
        //  exactly the silent correction of legacy behaviour that AAP §0.8.2 and constraint C-B
        //  forbid, and it would be invisible in a diff of the resource file because the resource
        //  file would not change.
        //
        //  IT IS CARRIED, NOT DELETED, for the same reason the legacy carries it: removing it
        //  would remove the evidence that the mistranslations are defects rather than choices. It
        //  is a QUOTATION and nothing more. It is not converted into a dictionary, a seed, a
        //  fallback for a miss, a conditional branch, a test fixture or a resource of any kind, no
        //  live statement in this file references it, and it deliberately carries no C# syntax
        //  that could be uncommented into working code.
        //
        //  ONE STRUCTURAL NOTE, because it dates the block. There is no CAT_DWSVC arm anywhere in
        //  it: the DataWindow-service category is newer than the table, which is why the only
        //  categories represented are window/DataWindow, ribbon bar, split container, tab control
        //  and message box. Its window arm also pairs I18N_CAT_WINDOW with I18N_CAT_DATAWINDOW at
        //  :L60, exactly as the live switch above does, so QUIRK 1 predates the resource file.
        // -----------------------------------------------------------------------------------
        //  L58    /*
        //  L59    choose case category
        //  L60    	case Enums.I18N_CAT_WINDOW,Enums.I18N_CAT_DATAWINDOW
        //  L61    		choose case text
        //  L62    			case "最大化"
        //  L63    				text = "Maximize"
        //  L64    			case "最小化"
        //  L65    				text = "Minimize"
        //  L66    			case "还原"
        //  L67    				text = "Restore"
        //  L68    			case "关闭"
        //  L69    				text = "Close"
        //  L70    			case "窗口列表"
        //  L71    				text = "Window List"
        //  L72    			case "层叠排列"
        //  L73    				text = "Cascade"
        //  L74    			case "平铺排列"
        //  L75    				text = "Layer"
        //  L76    			case "水平排列"
        //  L77    				text = "Tile Horizontal"
        //  L78    			case "垂直排列"
        //  L79    				text = "Tile Vertical"
        //  L80    			case "排列图标"
        //  L81    				text = "Arrange Icons"
        //  L82    			case "关闭所有窗口"
        //  L83    				text = "Close All"
        //  L84    		end choose
        //  L85    	case Enums.I18N_CAT_RIBBONBAR
        //  L86    		choose case text
        //  L87    			case "折叠功能区"
        //  L88    				text = "Collapse"
        //  L89    			case "展开功能区"
        //  L90    				text = "Expand"
        //  L91    		end choose
        //  L92    	case Enums.I18N_CAT_SPLITCONTAINER
        //  L93    		choose case text
        //  L94    			case "双击折叠左侧面板"
        //  L95    				text = "Double-click to collapse the left panel"
        //  L96    			case "双击折叠右侧面板"
        //  L97    				text = "Double-click to collapse the right panel"
        //  L98    			case "双击折叠上侧面板"
        //  L99    				text = "Double-click to collapse the top panel"
        //  L100   			case "双击折叠下侧面板"
        //  L101   				text = "Double-click to collapse the bottom panel"
        //  L102   			case "双击展开左侧面板"
        //  L103   				text = "Double-click to expand the left panel"
        //  L104   			case "双击展开右侧面板"
        //  L105   				text = "Double-click to expand the right panel"
        //  L106   			case "双击展开上侧面板"
        //  L107   				text = "Double-click to expand the top panel"
        //  L108   			case "双击展开下侧面板"
        //  L109   				text = "Double-click to expand the bottom panel"
        //  L110   			case "展开左侧面板"
        //  L111   				text = "Expand the left panel"
        //  L112   			case "展开右侧面板"
        //  L113   				text = "Expand the right panel"
        //  L114   			case "展开上侧面板"
        //  L115   				text = "Expand the top panel"
        //  L116   			case "展开下侧面板"
        //  L117   				text = "Expand the bottom panel"
        //  L118   		end choose
        //  L119   	case Enums.I18N_CAT_TABCONTROL
        //  L120   		if text = "固定" then
        //  L121   			text = "Dock"
        //  L122   		end if
        //  L123   	case CAT_MSGBOX
        //  L124   		choose case text
        //  L125   			case "提示"
        //  L126   				text = "Information"
        //  L127   			case "询问"
        //  L128   				text = "Question"
        //  L129   			case "警告"
        //  L130   				text = "Warning"
        //  L131   			case "错误"
        //  L132   				text = "Error"
        //  L133   			case "确定"
        //  L134   				text = "OK"
        //  L135   			case "取消"
        //  L136   				text = "Cancel"
        //  L137   			case "是"
        //  L138   				text = "Yes"
        //  L139   			case "否"
        //  L140   				text = "No"
        //  L141   			case "中止"
        //  L142   				text = "Abort"
        //  L143   			case "重试"
        //  L144   				text = "Retry"
        //  L145   			case "忽略"
        //  L146   				text = "Ignore"
        //  L147   			case "查看详情"
        //  L148   				text = "Show details"
        //  L149   			case "隐藏"
        //  L150   				text = "Hide details"
        //  L151   		end choose
        //  L152   end choose
        //  L153   */
        // ===================================================================================

        // :L155   return 0
        //
        // The single final fall-through, reached by an unmapped category, an untranslated key, a
        // malformed key and an absent resource table alike. The text is returned exactly as it
        // arrived, which is what makes "not handled" indistinguishable from having no provider
        // installed - the silent passthrough the entry point preserves.
        return 0;
    }
}
