// ==============================================================================================
//  Categories - the two PowerFramework localization category identifiers
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru (27 lines)
//  ORACLE STATUS  That .sru is the ONLY specification for these two values, and it is READ ONLY:
//                 it is the behavioural oracle for parity testing, never an edit target. The base
//                 the two values are derived from, ws_objects/pfw.shared.pbl.src/enums.sru:L123,
//                 is read only for the same reason. Both locators are carried on every member
//                 below so a single value can be adjudicated against the source without guesswork.
//
//  WHAT THE LEGACY OBJECT IS, ENUMERATED
//  --------------------------------------------------------------------------------------------
//      ne_cst_i18n.sru:L4, L8     global type ne_cst_i18n from n_cst_i18n
//      ne_cst_i18n.sru:L10        global ne_cst_i18n ne_cst_i18n   <- a global auto-instance whose
//                                                                    name shadows its own type
//                                                                    name
//      ne_cst_i18n.sru:L12-L18    type variables ... end variables - ONE block, holding BOTH
//                                                                    constants and nothing else
//      ne_cst_i18n.sru:L14        Public:                          - the sole access specifier
//      ne_cst_i18n.sru:L15        //Category                       - the block's own label, which
//                                                                    is where this file's name
//                                                                    comes from
//      ne_cst_i18n.sru:L16-L17    the two constants, transcribed verbatim below
//      ne_cst_i18n.sru:L20-L26    on create / on destroy           - each a bare
//                                                                    'call super::create' and
//                                                                    'call super::destroy'
//
//  That inventory is exhaustive. The object declares no function, no event script, no instance
//  variable and no native binding, so it carries no behaviour whatsoever: those two constants ARE
//  the object. Everything else in this file is documentation.
//
//  DECISION 1 - A STATIC CONSTANT HOLDER, NOT A DERIVED CLASS
//  --------------------------------------------------------------------------------------------
//  The transformation plan's object-kind table maps an 'ne_cst_*' object to a derived class, and
//  ne_cst_i18n genuinely IS a derived type: it is the middle link of the chain
//
//      n_cst_i18n  ->  ne_cst_i18n  ->  { n_cst_i18n_en, n_cst_i18n_chs, n_cst_i18n_cht }
//
//  Reproducing that link as a C# base class was considered and rejected, because the class would
//  be empty. It overrides nothing, it adds no member other than the two constants, and it holds no
//  state. The abstraction a base class would have supplied is the translate contract itself, and
//  that comes from the ROOT of the chain rather than from this link: n_cst_i18n.sru becomes
//  II18nProvider.cs in this same folder, and the three concrete providers implement it directly.
//
//  What is genuinely left once the empty inheritance link is removed is a constant catalogue, and
//  the same table maps a constant carrier to a role-named static class - the shape RetCode and
//  Enums already take in PowerFramework.Shared.Kernel. Two further facts confirm the shape rather
//  than merely permitting it:
//
//      * The legacy CALL SHAPE is already static. Every consumer reads the constant off the global
//        auto-instance declared at L10 - 'I18N(ne_cst_i18n.CAT_DWSVC, ...)' at se_cst_dw.sru:L355
//        is the canonical form - which is a type-qualified member read, not a virtual dispatch.
//        'Categories.CAT_DWSVC' is that same read. No instance is constructed in either language.
//      * The constants are consumed from OUTSIDE the provider chain far more than from inside it.
//        Of the 25 verified call sites, 23 are in the DataWindow service layer and the message-box
//        window, none of which derives from n_cst_i18n. Only the two provider switch arms are
//        internal to the chain, and they read the constants as plainly as anyone else.
//
//  DECISION 2 - COMPUTED OFFSETS, NEVER THE EVALUATED LITERALS
//  --------------------------------------------------------------------------------------------
//  The source writes both members as arithmetic over Enums.I18N_CAT_CUSTOM, and that arithmetic is
//  TRANSCRIBED here rather than evaluated:
//
//      ne_cst_i18n.sru:L16    Constant Long CAT_MSGBOX = Enums.I18N_CAT_CUSTOM + 1
//      ne_cst_i18n.sru:L17    Constant Long CAT_DWSVC  = Enums.I18N_CAT_CUSTOM + 2
//
//  Writing '= 6' and '= 7' would compile, and it would satisfy any value assertion written against
//  it, while quietly destroying two things the source encodes. First, the members would stop moving
//  with their base, so a change to I18N_CAT_CUSTOM would silently desynchronise this file from the
//  seven sibling categories that share the same numbering. Second, the STATEMENT the arithmetic
//  makes - that these are the first and second categories a consumer of the framework may define
//  after the framework's own five - would be gone from the code and survive only in prose.
//
//  The evaluated values are 6 and 7, and each member's documentation states its own so a reader
//  never has to compute one. They appear nowhere in an initializer, and the sibling test project
//  asserts them, which is what turns the derivation into a checked property rather than a hope.
//  The relationship is agreed from the other side too: Kernel's Enums.cs annotates
//  I18N_CAT_CUSTOM as the base these two are offset from and instructs that 5 be treated as
//  frozen, naming this file as the reason.
//
//  DECISION 3 - const long: NOT static readonly, NOT int, NOT an enum member
//  --------------------------------------------------------------------------------------------
//      const rather than static readonly    The source says 'Constant', and the transformation
//                                           plan maps that to C# const. Nothing forces the weaker
//                                           form: Enums.I18N_CAT_CUSTOM is itself declared
//                                           'public const long', so 'I18N_CAT_CUSTOM + 1' is a
//                                           compile-time constant expression and the initializer
//                                           is legal as written.
//      long rather than int                 PB Long is a 32-bit SIGNED integer and the plan's type
//                                           table maps it to C# long - a widening that is always
//                                           safe. The width also matches the parameter these
//                                           values are passed to: i18n.srf:L8 declares
//                                           'global function string i18n (readonly long category,
//                                           string text)', and its three-argument sibling at L9
//                                           declares the same category type. Kernel's whole I18N
//                                           family is long for the identical reason, so a category
//                                           argument never needs a conversion at a call site.
//      not an enum                          Two independent reasons. The general one is the one
//                                           Kernel's catalogue records: a C# enum cannot carry two
//                                           names for one value and does not combine cleanly where
//                                           the legacy combines. The specific one is that these
//                                           members are offsets from a constant declared in a
//                                           DIFFERENT type in a DIFFERENT assembly; an enum member
//                                           can be initialized that way only by repeating the
//                                           arithmetic, which puts DECISION 2 and an enum in
//                                           direct conflict. A plain const has no such tension.
//
//  DECISION 4 - THE IDENTIFIERS KEEP THEIR SCREAMING_SNAKE SPELLING
//  --------------------------------------------------------------------------------------------
//  CAT_MSGBOX and CAT_DWSVC are spelled exactly as PowerScript declares them, in deliberate
//  defiance of .NET naming convention. The reason is data integrity, not taste: these exact
//  identifier strings appear in serialized payloads on the new service contracts, in log records,
//  and in the characterization recordings that are the parity evidence for the whole migration.
//  CAT_DWSVC is reached from inside the DataWindow item-validation path, so it appears in recorded
//  validation output; renaming it would not restyle a symbol, it would silently invalidate every
//  stored comparison that mentions it, and a golden-master suite cannot report a comparison it can
//  no longer make.
//
//  WHAT MAKES THAT LEGAL UNDER A WARNINGS-AS-ERRORS BUILD, and the constraint it imposes:
//  Directory.Build.props sets TreatWarningsAsErrors=true for every project and deliberately
//  declares no suppression of its own; the root .editorconfig then scopes the naming suppressions
//  to the individual files that DECLARE the preserved identifiers. One of those sections names
//  this file by its exact repository-relative path and turns off CA1707 ("identifiers should not
//  contain underscores") and IDE1006 ("naming rule violation") for it alone. Three consequences
//  follow, and all three are load-bearing:
//
//      1. No '#pragma warning disable' and no project-level NoWarn belongs in this project. The
//         .csproj says so explicitly in its deliberately-absent section: a suppression there would
//         widen a file-scoped exception to the whole project.
//      2. This file's PATH is part of its contract. Moving or renaming it silently un-suppresses
//         both diagnostics, and under warnings-as-errors that is a build failure rather than a
//         hint. The .editorconfig glob is not a wildcard and must not be widened to match a new
//         location.
//      3. Every preserved-spelling identifier belonging to this project must live in THIS file,
//         because no other file of this project is covered. That is a real constraint rather than
//         a theoretical one, and it is satisfied without strain: the providers' category-to-element
//         names are lowercase string DATA ("msgbox" and "dwsvc" at n_cst_i18n_en.sru:L43-L46), not
//         SCREAMING_SNAKE identifiers, so they raise nothing where they belong.
//
//  Measured on this toolchain, CA1707 is off at the default analysis mode, so the build is clean
//  today either way. The suppression is the guard that keeps a routine hardening of the analysis
//  mode from breaking a port whose identifiers are non-negotiable.
//
//  DECISION 5 - CAT_MSGBOX IS DECLARED EVEN THOUGH NOTHING IN THIS PHASE CONSUMES IT
//  --------------------------------------------------------------------------------------------
//  Its consumers were enumerated, not assumed. There are exactly two: w_cst_msgbox.srw:L177-L226,
//  which localizes the message-box button captions and their tooltips, and
//  messageboxex.srf:L184-L190, which localizes the four dialog titles it derives from the icon.
//  Both are visual objects living in pfw.ui.controls.ext, so both are assigned to the DEFERRED
//  DesignSystem service. CAT_MSGBOX therefore has no in-scope consumer in this phase.
//
//  It is declared regardless, because the alternative is a silent narrowing of a ported contract
//  on what amounts to a scheduling accident. The oracle declares both constants side by side at
//  L16-L17, the transformation plan names both, and both concrete providers carry a 'case
//  CAT_MSGBOX' arm - n_cst_i18n_en.sru:L43 and n_cst_i18n_cht.sru:L44 - that cannot be reproduced
//  faithfully against a catalogue that is missing the constant it switches on.
//
//  Declaring it is NOT an implementation of the deferred service, and the distinction is worth
//  stating so it can be audited rather than argued: what lands here is one 64-bit integer in a
//  shared library, exactly as the legacy has it. Nothing message-box shaped accompanies it - no
//  dialog type, no button-set model, no icon or default-button enumeration, no caption strings and
//  no title table. Every one of those belongs to w_cst_msgbox.srw and messageboxex.srf and stays
//  deferred with them.
//
//  DECISION 6 - WHAT IS DELIBERATELY ABSENT, EACH WITH ITS REASON
//  --------------------------------------------------------------------------------------------
//      no [Flags]                      Categories are mutually exclusive selectors, matched by a
//                                      'choose case category' in each provider
//                                      (n_cst_i18n_en.sru:L34-L47, n_cst_i18n_cht.sru:L35-L48) and
//                                      never combined with a bitwise operator anywhere in the
//                                      estate. Two of them even SHARE one arm
//                                      (I18N_CAT_WINDOW and I18N_CAT_DATAWINDOW at
//                                      n_cst_i18n_en.sru:L35), which a flags set could not express.
//      no lookup dictionary and no     The legacy holds that mapping inside each provider's own
//      category-to-element-name map    translate switch, and it is per provider rather than
//                                      shared: the English provider maps CAT_MSGBOX to "msgbox"
//                                      and CAT_DWSVC to "dwsvc" for the XPath query built at
//                                      n_cst_i18n_en.sru:L50, the Traditional Chinese provider
//                                      does the same at :L44-L48, and the Simplified Chinese
//                                      provider has NO such switch at all because it is a genuine
//                                      no-op - Simplified Chinese is the base locale. Hoisting the
//                                      map here would move behaviour out of the objects that own
//                                      it and hand the providers a shared table the legacy does
//                                      not have, which is exactly the kind of harmonisation this
//                                      refactor forbids.
//      no predicate, no IsCustom-      ne_cst_i18n declares no function, so any helper here would
//      Category, no TryParse, no       be a new feature rather than a port. A caller that needs
//      name table                      the custom-category boundary reads Enums.I18N_CAT_CUSTOM
//                                      directly, which is where the boundary is declared.
//      no mirror of the seven Kernel   I18N_SRC_PFW, I18N_SRC_CUSTOM and I18N_CAT_WINDOW through
//      I18N constants                  I18N_CAT_CUSTOM are declared in enums.sru:L115-L123, so
//                                      they belong to Kernel's Enums.cs and are reached from
//                                      there. Re-exporting or mirroring them would create two
//                                      places for one value - the precise failure the shared
//                                      Kernel exists to prevent.
//      no translation entry point and  Those are separate objects and separate files in this same
//      no provider                     folder: i18n.srf becomes I18n.cs and n_cst_i18n.sru becomes
//                                      II18nProvider.cs. This file carries values only.
//
//  DEPENDENCY SURFACE
//  --------------------------------------------------------------------------------------------
//  Exactly one, and it is deliberately the only one: PowerFramework.Shared.Kernel, for
//  Enums.I18N_CAT_CUSTOM. The .csproj declares that single ProjectReference and no package
//  reference at all, and this file is the edge's proof: if the reference were missing or the
//  constant renamed, this file would not compile. Nothing else is imported, nothing from the
//  deferred services is touched, and no I/O of any kind occurs here.
// ==============================================================================================

using PowerFramework.Shared.Kernel;

namespace PowerFramework.Shared.Localization;

/// <summary>
/// The two PowerFramework localization categories, ported from
/// <c>ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru</c>.
/// </summary>
/// <remarks>
/// <para>
/// A category selects WHICH group of translations a lookup searches. It is neither a locale nor a
/// source: it is the first argument of the two-argument translation entry point and the second of
/// the three-argument one [i18n.srf:L8-L9], while the locale is chosen by which provider is
/// installed and the source is the separate <see cref="Enums.I18N_SRC_PFW"/> selector. The
/// framework declares five categories of its own - window, tab control, ribbon bar, split container
/// and DataWindow - and reserves everything above <see cref="Enums.I18N_CAT_CUSTOM"/> for
/// categories defined on top of it [enums.sru:L118-L123]. These two are those categories
/// [ne_cst_i18n.sru:L15-L17], and the estate defines no others.
/// </para>
/// <para>
/// Both members are declared as ARITHMETIC over <see cref="Enums.I18N_CAT_CUSTOM"/> rather than as
/// the integers they evaluate to, because that is how the oracle declares them and because the
/// derivation is the part that carries meaning; see DECISION 2 in this file's header. Their
/// evaluated values are stated on each member so no reader has to compute one.
/// </para>
/// <para>
/// The class carries values only: no behaviour, no state, no table and no helper. That is not a
/// simplification of the source but a faithful reproduction of it - <c>ne_cst_i18n</c> declares no
/// function, no event script and no instance variable, so these two constants are the entire
/// object. The translate contract that the object's own base type supplies becomes
/// <c>II18nProvider</c>, the three concrete providers implement it, and the entry point that
/// dispatches to whichever provider is installed is <c>I18n</c>; all three are separate files in
/// this same library.
/// </para>
/// <para>
/// IDENTIFIER SPELLINGS ARE PART OF THE CONTRACT, NOT A STYLE CHOICE. Both names keep the
/// SCREAMING_SNAKE form PowerScript declares, because they travel in serialized payloads on the new
/// service contracts, in log records and in characterization recordings. The naming analyzers are
/// suppressed for this file's exact path in the root <c>.editorconfig</c> and nowhere broader, which
/// makes the path itself load-bearing and means any other preserved-spelling constant of this
/// library must be declared here too. DECISION 4 in the header states the full mechanism and its
/// three consequences.
/// </para>
/// </remarks>
/// <example>
/// Every legacy consumer reads the constant off the global auto-instance declared at
/// <c>ne_cst_i18n.sru:L10</c> and passes it straight through as the category argument. The
/// canonical site is the DataWindow item-validation path:
/// <code>
/// // ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L357
/// // MessageBox(I18N(ne_cst_i18n.CAT_DWSVC, "..."), sErrMsg, StopSign!)
/// </code>
/// The ported form is the same read, against this class instead of the auto-instance, and the value
/// is a plain <see cref="long"/> that needs no conversion at the call site because the category
/// parameter is itself a <c>long</c> [i18n.srf:L8]:
/// <code>
/// long category = Categories.CAT_DWSVC;   // 7
/// </code>
/// </example>
public static class Categories
{
    // ------------------------------------------------------------------------------------------
    //  THE CATEGORY CONSTANTS                                            ne_cst_i18n.sru:L16-L17
    // ------------------------------------------------------------------------------------------
    //  The oracle introduces the block with '/*--- Constants ---*/' at L13, the access specifier
    //  'Public:' at L14 - which C# expresses per member - and the label '//Category' at L15. Two
    //  constants follow, at L16 and L17, and they are declared here in that same order.
    //
    //  Both initializers are the source's arithmetic, transcribed rather than evaluated. Neither
    //  the literal 6 nor the literal 7 appears anywhere in this file.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The message-box category: <c>Enums.I18N_CAT_CUSTOM + 1</c>, which evaluates to <c>6</c>.
    /// [ne_cst_i18n.sru:L16]
    /// </summary>
    /// <remarks>
    /// <para>
    /// Selects the translations for the framework's own message-box chrome: the button captions and
    /// their matching tooltips at <c>w_cst_msgbox.srw:L177-L226</c>, covering the OK, OK/Cancel,
    /// Yes/No, Yes/No/Cancel, Retry/Cancel and Abort/Retry/Ignore button sets, and the four dialog
    /// titles that <c>messageboxex.srf:L184-L190</c> derives from the requested icon. In the English
    /// provider the category resolves to the <c>msgbox</c> element of the translation table
    /// [n_cst_i18n_en.sru:L43]; the Traditional Chinese provider carries the matching arm
    /// [n_cst_i18n_cht.sru:L44]; the Simplified Chinese provider carries none, because Simplified
    /// Chinese is the base locale and its translate body is a genuine no-op.
    /// </para>
    /// <para>
    /// NO IN-SCOPE CONSUMER IN THIS PHASE, AND DECLARED ANYWAY. Both consumers named above are
    /// visual objects in <c>pfw.ui.controls.ext</c> and are therefore assigned to the deferred
    /// DesignSystem service, so nothing built in this phase reads this constant. Dropping it would
    /// nonetheless be a silent narrowing of a ported contract driven by scheduling rather than by
    /// behaviour, and it would leave both providers' <c>case CAT_MSGBOX</c> arms with nothing to
    /// switch on. Declaring one integer is not an implementation of the deferred service: nothing
    /// message-box shaped accompanies it. DECISION 5 in the header states the reasoning in full.
    /// </para>
    /// <para>
    /// A second <c>case CAT_MSGBOX</c> exists at <c>n_cst_i18n_en.sru:L123</c>, inside the
    /// commented-out pre-XML translation table at <c>:L58-L153</c>. It is dormant, it is not a
    /// consumer, and it must not be revived - that table holds wording the live resource file
    /// contradicts, and restoring it would be exactly the silent correction this refactor forbids.
    /// </para>
    /// </remarks>
    public const long CAT_MSGBOX = Enums.I18N_CAT_CUSTOM + 1;

    /// <summary>
    /// The DataWindow-service category: <c>Enums.I18N_CAT_CUSTOM + 2</c>, which evaluates to
    /// <c>7</c>. [ne_cst_i18n.sru:L17]
    /// </summary>
    /// <remarks>
    /// <para>
    /// Selects the translations the DataWindow service layer emits, and it is the reason this
    /// library exists in this phase at all. There are 23 verified call sites across three in-scope
    /// objects: <c>se_cst_dw.sru:L355</c> and <c>:L357</c> - the invalid-value text and the dialog
    /// title raised from inside the item-validation path; <c>n_cst_dwsvc_rowselect.sru:L239</c> -
    /// the rejected-change report; and <c>n_cst_dwsvc_contextmenu.sru</c> - 20 sites spanning
    /// <c>L243</c> to <c>L1052</c>, being the context-menu item captions and tooltips plus two
    /// failure reports. This is what makes localization a CONFIRMED dependency of the refactor
    /// rather than a conditional one: a service built in this phase reaches it directly, so it
    /// could not have been deferred with the rest of the user-interface estate.
    /// </para>
    /// <para>
    /// RECORDING-VISIBLE, WHICH IS WHY THE SPELLING IS FROZEN. The validation path above is
    /// exercised by the characterization workflows, so both the value <c>7</c> and the identifier
    /// <c>CAT_DWSVC</c> appear in recorded output. This is the concrete case behind DECISION 4:
    /// renaming the member would invalidate every stored comparison that mentions it.
    /// </para>
    /// <para>
    /// TRANSLATIONS REACHED THROUGH THIS CATEGORY MAY CARRY AN UNFILLED PLACEHOLDER. At least one
    /// entry is looked up and only THEN formatted, by a <c>Sprintf</c> that wraps the lookup at the
    /// call site rather than inside the provider: <c>n_cst_dwsvc_rowselect.sru:L239</c> and
    /// <c>n_cst_dwsvc_contextmenu.sru:L1045</c> and <c>:L1052</c> all wrap the row-number text that
    /// way. The translated string must therefore survive the lookup with its placeholder intact,
    /// and a provider that formatted eagerly would break all three sites. The obligation belongs to
    /// the providers and to the entry point, not to this file; it is recorded here because it is
    /// the reason this category matters behaviourally and not merely as a lookup key.
    /// </para>
    /// <para>
    /// In the English provider the category resolves to the <c>dwsvc</c> element of the translation
    /// table [n_cst_i18n_en.sru:L45] and in the Traditional Chinese provider to the arm at
    /// <c>n_cst_i18n_cht.sru:L46</c>. The Simplified Chinese provider has no arm, for the reason
    /// given on <see cref="CAT_MSGBOX"/>.
    /// </para>
    /// </remarks>
    public const long CAT_DWSVC = Enums.I18N_CAT_CUSTOM + 2;
}
