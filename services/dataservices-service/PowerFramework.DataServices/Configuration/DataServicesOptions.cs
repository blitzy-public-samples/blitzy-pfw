// ==============================================================================================
// DataServicesOptions.cs
// The complete, strongly typed, validated configuration surface of the DataServices service.
// ==============================================================================================
//
// WHAT THIS FILE IS
// The single file in Configuration/, and the single place any externally supplied value enters
// this service. Nothing else in the project may carry a literal address, port, audience,
// authority, locale, duration or behavioural threshold: every such value is declared here first,
// bound from configuration, validated at startup, and injected as IOptions<T>. That is not a
// stylistic preference. It is how constraint C-F ("nothing hardcoded may be carried forward") is
// discharged STRUCTURALLY rather than by review - a value that has nowhere else to live cannot be
// smuggled into a client, an endpoint or a domain type.
//
// This folder is generated FIRST in its project and deliberately depends on no sibling folder.
// Clients/, Domain/, Expressions/, Services/, Endpoints/, Grpc/ and Validators/ all depend on
// this file; this file depends on none of them. The one and only cross-assembly reference below
// is PowerFramework.Shared.Kernel, for the three Pinyin flag constants, and it is present for a
// stated reason recorded on the property that uses it.
//
// RULES POSITION, STATED PLAINLY
// The project's rules document was retrieved and contains exactly one statement: no user rules
// were provided. That is a finding, not latitude. This file is therefore held to the enterprise
// baseline of the transformation plan (section 0.7.2) and to the twelve binding non-rule
// constraints C-A through C-L (section 0.7.3). No rule is invented, inferred or back-filled from
// convention, and no constraint is relaxed on the strength of the rules' absence.
//
// TWO BINDABLE ROOTS IN ONE FILE, AND WHY THAT IS CORRECT RATHER THAN UNTIDY
// Two root types are declared here, with two different section paths:
//
//     DataServicesOptions       binds "DataServices"        - this service's own settings
//     JwtAuthenticationOptions  binds "Authentication:Jwt"  - inbound bearer VALIDATION settings
//
// The second is deliberately NOT nested inside the first. "Authentication" sits at the top level
// of the configuration document by ASP.NET Core convention, which is where the stock JWT bearer
// handler and Program.cs expect to find it. Folding it under the "DataServices" root would move
// its path to "DataServices:Authentication:Jwt" and silently break the sibling appsettings.json
// that already declares it at the top level. Since Configuration/ holds exactly one file, both
// types live here.
//
// TWO KINDS OF VALUE, AND THE LINE BETWEEN THEM IS THE MOST IMPORTANT THING IN THIS FILE
//
//   (1) PRESERVED LEGACY DEFAULTS. Every default in the Localization, RowSelect, ContextMenu,
//       ColumnExpression and DropDownSearch groups is a behaviour-preservation claim under
//       constraint C-B. Each one was read at a named line of ws_objects/** and is reproduced
//       exactly; each carries its locator in its own documentation so any single value can be
//       adjudicated against the oracle without guesswork. A value off by one, or an identifier
//       misspelled, is a silent behavioural regression that no unit test would flag AS such -
//       it would simply record the wrong behaviour as correct. The inventory:
//
//         Localization.Locale                              "en"   pfw.sra:L94
//         RowSelect.Style                                     1    n_cst_dwsvc_rowselect.sru:L25
//         ContextMenu.ColAutoWidth                         true    n_cst_dwsvc_contextmenu.sru:L49
//         ContextMenu.ColCheck                             true    n_cst_dwsvc_contextmenu.sru:L50
//         ContextMenu.ColCopy                              true    n_cst_dwsvc_contextmenu.sru:L51
//         ContextMenu.ColPaste                             true    n_cst_dwsvc_contextmenu.sru:L52
//         ContextMenu.ItemCopy                             true    n_cst_dwsvc_contextmenu.sru:L53
//         ColumnExpression.Trace                          false    n_cst_dwsvc_columnexp.sru:L98
//         ColumnExpression.RedrawSuppressionRowThreshold     200    n_cst_dwsvc_columnexp.sru:L223, L225
//         ColumnExpression.CalcStackInitialCapacity          20    n_cst_dwsvc_columnexp.sru:L2421-L2422
//         DropDownSearch.FilterType                           3    n_cst_dwsvc_dropdownsearch.sru:L57
//         DropDownSearch.ShowFilteredRows                  null    n_cst_dwsvc_dropdownsearch.sru:L58, L503
//         DropDownSearch.PinyinMatchFlags                     7    n_cst_dwsvc_dropdownsearch.sru:L323
//
//   (2) BOUNDARY-CREATED SETTINGS. The Persistence, Security, Resilience, Sessions and EventChain
//       groups have NO legacy locator, and their absence is not an oversight. PowerFramework is a
//       library with no process of its own, no listener and no server tier, so it had no network
//       address to configure, no transport failure to handle, no server-held session to expire and
//       no delivery order to enforce across a wire. Those five groups exist because the
//       decomposition created the system's first process boundary, and each is labelled as such on
//       its own type so that nobody later hunts for the legacy line it came from.
//
// THE ONE ASYMMETRY THAT MUST SURVIVE, BECAUSE HARMONISING IT IS THE EASIEST C-B VIOLATION HERE
// RowSelect.Style REJECTS zero. DropDownSearch.FilterType ACCEPTS anything, zero included.
// Both halves are evidenced, and neither is a judgement call:
//
//     n_cst_dwsvc_rowselect.sru:L169        if style = 0 then return RetCode.E_INVALID_ARGUMENT
//     n_cst_dwsvc_dropdownsearch.sru:L304   #FilterType = nType
//     n_cst_dwsvc_dropdownsearch.sru:L305   return RetCode.OK
//
// The row-select setter guards its argument; the filter-type setter performs no validation of any
// kind. Adding a non-zero or range guard to FilterType would add behaviour the legacy does not
// have, which constraint C-B forbids exactly as firmly as it forbids removing behaviour. The
// validator below therefore checks one and pointedly does not check the other, and says so at the
// point of omission.
//
// WHY VALIDATION FAILURE AT STARTUP IS FATAL, NOT ADVISORY
// Program.cs binds both roots with ValidateOnStart(), so a structural configuration fault stops
// the process before it serves a request. That is the faithful translation of the legacy posture,
// not defensive over-engineering. ws_objects/pfw.pbl.src/pfw.sra:L111-L144 is the framework's
// systemerror handler: it unpacks a seven-field assertion payload split on "~r~n" (:L115, :L119),
// formats it, and then executes HALT CLOSE at :L143 - it terminates the application. The .NET
// equivalent of that is fail-fast startup validation and process termination on a structural
// fault. Softening it into warn-and-continue would be a behavioural change dressed as robustness,
// and it would let a service start with, for example, no upstream address at all and fail later
// on a request path where the fault is far harder to attribute.
//
// DELIBERATE ABSENCES. Each is a named constraint, so no reader "completes" this file by adding one.
//
//   * NO credential value of any kind, and no property that could hold one. Not as a default, not
//     as an example, not in a comment (constraint C-F). The one property name below that contains
//     the word "key" is ValidateIssuerSigningKey, which is a BOOLEAN SWITCH selecting whether the
//     stock bearer handler verifies a signature; it holds no material and never could. Signing
//     material exists in exactly one place in this system, and that place is the Security service.
//   * NO signing property of any name - no SigningKey, no IssuerSigningKey, no symmetric key, no
//     issuer credential (constraint C-G). Security is the SOLE issuer; this service holds
//     verification settings only, pointed at Security's published JWKS and discovery document so
//     the framework's own handler self-configures. The project file corroborates this by
//     deliberately omitting the minting package, Microsoft.IdentityModel.JsonWebTokens.
//   * NO storage configuration of any kind - no provider setting, no data-source group, no
//     database group (constraint C-E). Persistence is the only service in the refactor that holds
//     a storage provider; DataServices reaches data solely over the generated persistence.v1 gRPC
//     client, addressed by the single Persistence.Address value below.
//   * NO option belonging to a deferred service (constraint C-D). Specifically: no DPI or scale
//     option, no font name or measurement option, no window position or size option, no input
//     method option, no theme or colour option, no HTTP-transport, FTP, WebSocket or MQTT option,
//     and no embedded-browser or scripting-engine option. Those are the deferred rendering and
//     transport halves; an option here would be a partial implementation of a forbidden target,
//     which the requirements rank as worse than a documented gap.
//   * NO menu-identifier option. n_cst_dwsvc_contextmenu.sru:L37-L45 declares the reserved
//     identifier block MID_RESERVED=10000 through MID_ITEMCOPY=10007. Those are the menu model's
//     own constants and belong to Services/ContextMenuModel.cs, not to configuration.
//   * NO expression-engine constant. DWOSUFFIX (n_cst_dwsvc_columnexp.sru:L95), the tri-state
//     calculation cache CLC_UNKNOWN/CLC_YES/CLC_NO (:L113-L115), the variable-kind pair and the
//     macro sentinels are engine internals owned by Expressions/ColumnExpressionEngine.cs. A
//     constant that no deployment may vary is not a setting.
//   * NO validation framework. The five ported type validators must show their exact legacy
//     semantics, so a rule framework that obscured them would cost the one property that matters
//     most here, which is parity. Everything below uses only System.ComponentModel.DataAnnotations
//     and Microsoft.Extensions.Options, both of which arrive from the Microsoft.AspNetCore.App
//     shared framework - so this file adds no package reference and constraint C-I is untouched.
//
// NAMING, AND WHY THIS FILE LOOKS DIFFERENT FROM THE CONSTANT CATALOGUES
// Members here are PascalCase. The legacy SCREAMING_SNAKE spellings - RS_SINGLE, FILTER_DISP,
// FILTER_DISP_PY, FILTER_ALL, PY_LIKE_IGNORE_CASE and the rest - are preserved in the
// documentation of the property that carries their value, never in an identifier. The
// repository-root .editorconfig carries narrow naming-analyzer bands for the files on its BAND 3
// roster - published once there rather than counted again here - whose IDENTIFIERS are the
// recording-visible artifact, and this path is deliberately not one of them:
// what travels in a recording here is a configuration VALUE, not the C# name that holds it. So no
// constant is declared in this file, and none should be added.
//
// ==============================================================================================

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Expressions;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Configuration;

/// <summary>
/// The root configuration object of the DataServices service, bound from the
/// <see cref="SectionName"/> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// Every group property is non-nullable and initialised to a fresh instance, so binding never has
/// to construct a null graph and a partially specified section still yields the preserved legacy
/// default for each key it does not mention. That property is load bearing: the sibling
/// appsettings.Development.json deliberately redeclares none of the preserved legacy defaults,
/// precisely so a developer environment cannot diverge behaviourally and silently invalidate a
/// characterization recording. The defaults in this file are therefore the single authority for
/// those values.
/// </para>
/// <para>
/// Program.cs binds this type with startup validation, which is fatal by design. The legacy
/// framework's own posture on a structural fault is to terminate: its systemerror handler unpacks
/// a seven-field assertion payload split on a carriage-return/line-feed pair and then executes
/// <c>HALT CLOSE</c> [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, terminating at :L143]. Fail-fast
/// startup validation with process termination is the faithful equivalent; degrading to a warning
/// would be a behavioural change dressed as robustness.
/// </para>
/// </remarks>
public sealed class DataServicesOptions
{
    /// <summary>
    /// The configuration section this type binds from.
    /// </summary>
    /// <remarks>
    /// Consumed by Program.cs and by the sibling appsettings files, which must agree with it
    /// exactly. The nested group paths that follow from it are <c>DataServices:Persistence:Address</c>,
    /// <c>DataServices:Security:BaseAddress</c>, <c>DataServices:Resilience</c> and so on.
    /// </remarks>
    public const string SectionName = "DataServices";

    /// <summary>
    /// Locale selection for the ported localization providers. Carries a preserved legacy default.
    /// </summary>
    public LocalizationOptions Localization { get; set; } = new();

    /// <summary>
    /// Row-selection behaviour of the ported row-select service. Carries a preserved legacy default.
    /// </summary>
    public RowSelectOptions RowSelect { get; set; } = new();

    /// <summary>
    /// Which built-in context-menu commands the menu model offers. Carries preserved legacy defaults.
    /// </summary>
    public ContextMenuOptions ContextMenu { get; set; } = new();

    /// <summary>
    /// Column-expression engine behaviour. Carries preserved legacy defaults.
    /// </summary>
    public ColumnExpressionOptions ColumnExpression { get; set; } = new();

    /// <summary>
    /// Drop-down search behaviour of the headless half of the drop-down search service. Carries
    /// preserved legacy defaults.
    /// </summary>
    public DropDownSearchOptions DropDownSearch { get; set; } = new();

    /// <summary>
    /// Where to reach the Persistence service. Created by the decomposition; no legacy equivalent.
    /// </summary>
    public PersistenceClientOptions Persistence { get; set; } = new();

    /// <summary>
    /// Where to reach the Security service. Created by the decomposition; no legacy equivalent.
    /// </summary>
    public SecurityClientOptions Security { get; set; } = new();

    /// <summary>
    /// Transport-failure handling for the two typed clients. Created by the decomposition; no
    /// legacy equivalent.
    /// </summary>
    public ResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// Lifetime and admission settings for the two server-held session kinds. Created by the
    /// decomposition; no legacy equivalent.
    /// </summary>
    public SessionsOptions Sessions { get; set; } = new();

    /// <summary>
    /// How the event chain treats out-of-order delivery. Created by the decomposition; no legacy
    /// equivalent.
    /// </summary>
    public EventChainOptions EventChain { get; set; } = new();
}

// ----------------------------------------------------------------------------------------------
// PRESERVED LEGACY DEFAULTS - group 1 of 5: localization
// ----------------------------------------------------------------------------------------------

/// <summary>
/// Selects which ported localization provider is installed. Bound from
/// <c>DataServices:Localization</c>.
/// </summary>
/// <remarks>
/// The legacy call site is <c>ws_objects/pfw.pbl.src/pfw.sra:L88-L106</c>, the framework
/// application's open event: it initializes the framework, sets a locale, selects a provider from
/// it and installs that provider. The locale is a hardcoded literal there. This group reproduces
/// the observable default and makes it overridable, which is precisely the remediation the plan
/// specifies: the defect is the hardcoding, and the un-configurability is a structural property
/// rather than a behaviour, so the value is preserved while the immovability is not.
/// </remarks>
public sealed class LocalizationOptions
{
    /// <summary>
    /// The locale identifier Program.cs maps to a localization provider. Defaults to <c>"en"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY DEFAULT. <c>ws_objects/pfw.pbl.src/pfw.sra:L94</c> reads
    /// <c>lang = "en"</c>, sitting directly beneath the multi-language-support comment at
    /// <c>:L93</c>.
    /// </para>
    /// <para>
    /// The accepted values are exactly three, taken from the provider selection at
    /// <c>pfw.sra:L95-L102</c>: <c>"en"</c> selects the English provider, <c>"chs"</c> the
    /// Simplified Chinese provider - which reports <em>handled</em> without mutating the text for
    /// framework-sourced strings and <em>not handled</em> for anything else, because Simplified
    /// Chinese is the base locale - and <c>"cht"</c> the Traditional Chinese provider. The validator
    /// enforces that
    /// set, and it compares ORDINALLY: PowerScript's <c>choose case</c> on strings is
    /// case-sensitive, so the legacy would not have matched a differently cased spelling either.
    /// </para>
    /// <para>
    /// One legacy detail is deliberately NOT reproduced here, and the distinction matters. That
    /// selection has no <c>case else</c> arm, so an unrecognised locale left the provider variable
    /// null and the install call at <c>:L103</c> installed nothing, landing every subsequent
    /// lookup in the localization surface's silent-passthrough fallback. Rejecting an unrecognised
    /// value at startup is legitimate because Program.cs must map this string onto a concrete
    /// provider, and a value it cannot map is a structural configuration fault rather than a
    /// behaviour to imitate. The silent-passthrough fallback itself is preserved where it belongs,
    /// which is inside the localization surface when no provider is installed at all.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Locale { get; set; } = "en";
}

// ----------------------------------------------------------------------------------------------
// PRESERVED LEGACY DEFAULTS - group 2 of 5: row selection
// ----------------------------------------------------------------------------------------------

/// <summary>
/// Row-selection behaviour of the ported row-select service. Bound from
/// <c>DataServices:RowSelect</c>.
/// </summary>
/// <remarks>
/// Ported from <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru</c>, which
/// is headless apart from a single dialog call site that becomes a structured error result
/// elsewhere in this service. Nothing in this group is presentational.
/// </remarks>
public sealed class RowSelectOptions
{
    /// <summary>
    /// The row-selection style, as a combinable bitmask. Defaults to <c>1</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY DEFAULT. <c>n_cst_dwsvc_rowselect.sru:L25</c> declares
    /// <c>privatewrite long #Style = RS_SINGLE</c>, and the two style constants are declared
    /// immediately above it: <c>RS_SINGLE = 1</c> at <c>:L20</c> and <c>RS_MULTIPLE = 2</c> at
    /// <c>:L21</c>. The default is therefore <c>1</c>, spelled here as the numeric literal.
    /// </para>
    /// <para>
    /// It is written as a literal rather than as a reference to a named constant on purpose. The
    /// <c>RS_*</c> constants are declared on the legacy service object itself and are absent from
    /// the framework-wide constant catalogue - a search of <c>enums.sru</c> for them returns
    /// nothing - so in the .NET tree they port onto Services/RowSelectService.cs. Referencing them
    /// from here would create a dependency from Configuration/ onto a sibling folder and forfeit
    /// this folder's declared property of depending on none of them.
    /// </para>
    /// <para>
    /// The type is <c>long</c>, matching the legacy 32-bit signed declaration, and NOT a
    /// two-member enumeration: the comment at <c>:L19</c> states explicitly that the style
    /// supports combination, so a value may carry both bits and a closed enumeration would
    /// misrepresent the contract.
    /// </para>
    /// <para>
    /// The validator rejects zero. That is evidenced rather than invented -
    /// <c>n_cst_dwsvc_rowselect.sru:L169</c> reads
    /// <c>if style = 0 then return RetCode.E_INVALID_ARGUMENT</c> - and it is deliberately NOT
    /// mirrored onto <see cref="DropDownSearchOptions.FilterType"/>, whose legacy setter validates
    /// nothing at all.
    /// </para>
    /// </remarks>
    public long Style { get; set; } = 1;
}

// ----------------------------------------------------------------------------------------------
// PRESERVED LEGACY DEFAULTS - group 3 of 5: which context-menu commands are offered
// ----------------------------------------------------------------------------------------------

/// <summary>
/// Which built-in commands the context-menu model offers. Bound from
/// <c>DataServices:ContextMenu</c>.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru</c>,
/// which the plan splits: the headless half ships here as the menu ITEM MODEL - labels,
/// identifiers, enabled and split flags and computed logical text widths - while window geometry,
/// scale conversion, font measurement and the actual rendering are deferred and named as a
/// reserved Gateway extension point. This group configures only the headless half: it decides
/// which commands appear in the model that crosses the contract, and nothing about how any of them
/// is drawn.
/// </para>
/// <para>
/// All five legacy declarations are <c>public boolean</c> rather than <c>privatewrite</c>, so they
/// were directly assignable by the hosting application. Plain settable properties reproduce that
/// faithfully.
/// </para>
/// <para>
/// The reserved menu identifiers at <c>:L37-L45</c> are deliberately absent from this group. They
/// are the menu model's own constants, not settings, and belong to Services/ContextMenuModel.cs.
/// </para>
/// </remarks>
public sealed class ContextMenuOptions
{
    /// <summary>
    /// Whether the automatic column-width commands are offered. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// PRESERVED LEGACY DEFAULT. <c>n_cst_dwsvc_contextmenu.sru:L49</c> declares
    /// <c>boolean #ColAutoWidth = true</c>, annotated as automatic column width. What this flag
    /// governs is whether the command appears in the menu model at all; the width COMPUTATION the
    /// model carries is logical rather than device-specific, so nothing about this option reaches the
    /// deferred rendering half.
    /// </remarks>
    public bool ColAutoWidth { get; set; } = true;

    /// <summary>
    /// Whether the whole-column check and uncheck commands are offered. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// PRESERVED LEGACY DEFAULT. <c>n_cst_dwsvc_contextmenu.sru:L50</c> declares
    /// <c>boolean #ColCheck = true</c>, annotated as check and clear-check of a whole column. One
    /// flag governs both commands, exactly as the legacy does; splitting it into two would be a new
    /// capability.
    /// </remarks>
    public bool ColCheck { get; set; } = true;

    /// <summary>
    /// Whether the copy-whole-column command is offered. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// PRESERVED LEGACY DEFAULT. <c>n_cst_dwsvc_contextmenu.sru:L51</c> declares
    /// <c>boolean #ColCopy = true</c>, annotated as copying a whole column's values.
    /// </remarks>
    public bool ColCopy { get; set; } = true;

    /// <summary>
    /// Whether the paste-over-whole-column command is offered. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// PRESERVED LEGACY DEFAULT. <c>n_cst_dwsvc_contextmenu.sru:L52</c> declares
    /// <c>boolean #ColPaste = true</c>, annotated as overwriting a whole column with clipboard
    /// data.
    /// </remarks>
    public bool ColPaste { get; set; } = true;

    /// <summary>
    /// Whether the copy-single-cell command is offered. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// PRESERVED LEGACY DEFAULT. <c>n_cst_dwsvc_contextmenu.sru:L53</c> declares
    /// <c>boolean #ItemCopy = true</c>, annotated as copying a single cell's value.
    /// </remarks>
    public bool ItemCopy { get; set; } = true;
}


// ----------------------------------------------------------------------------------------------
// PRESERVED LEGACY DEFAULTS - group 4 of 5: the column-expression engine
// ----------------------------------------------------------------------------------------------

/// <summary>
/// Column-expression engine behaviour. Bound from <c>DataServices:ColumnExpression</c>.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru</c>, at
/// 2,435 lines the largest in-scope legacy object. THREE of the settings below are the values that
/// object exposes as varying state. Its grammar sentinels, its tri-state calculation cache and its
/// variable-kind discriminators are engine internals, and a value no deployment may vary is not a
/// setting.
/// </para>
/// <para>
/// THE FOURTH AND FIFTH SETTINGS ARE NOT LEGACY DEFAULTS, AND MUST NOT BE READ AS ONE.
/// <see cref="PageResolution"/> and <see cref="PageRowsPerPage"/> have no counterpart in the legacy
/// object at all, because the legacy is an in-process library inside a PowerBuilder application that
/// OWNED its own band geometry: <c>for page</c> simply asked the runtime which rows were on the page.
/// Decomposition put that geometry in the deferred DesignSystem, reserved at <c>/v1/design/**</c>
/// (AAP 0.4.4), so the headless half has to be TOLD the pagination - and being told once, in
/// configuration, is what makes the answer attributable to a stated fact rather than to a default.
/// They are annotated as new rather than dressed as preserved, because a fabricated legacy locator
/// would be worse than no locator.
/// </para>
/// </remarks>
public sealed class ColumnExpressionOptions
{
    /// <summary>
    /// Whether the engine emits its expression trace. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY DEFAULT. <c>n_cst_dwsvc_columnexp.sru:L98</c> declares
    /// <c>privatewrite boolean #Trace</c> with NO initializer, so the PowerBuilder default is
    /// false. This property is likewise declared without an initializer, which is both the exact
    /// mirror of that declaration and the C# default for <see cref="bool"/>.
    /// </para>
    /// <para>
    /// The consumption site is <c>:L752</c>, <c>if #Trace then</c>: when set, the engine walks its
    /// calculation stack to build a call-stack string and raises the trace event carrying the
    /// stack, the expression and the evaluated value. On the wire that becomes the trace stream of
    /// the column-expression contract, whose ordering pattern is a sequencing token because the
    /// trace is diagnostic and fire-and-forget.
    /// </para>
    /// </remarks>
    public bool Trace { get; set; }

    /// <summary>
    /// The row count above which the engine suppresses host redraw for the duration of one
    /// item-changed cascade. Defaults to <c>200</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY DEFAULT. <c>n_cst_dwsvc_columnexp.sru:L223</c> and <c>:L225</c> are a
    /// matched pair bracketing the cascade dispatch at <c>:L224</c>, inside the item-changed event
    /// spanning <c>:L211-L227</c>: the row count is read at <c>:L222</c>, redraw is switched off
    /// before the cascade when that count exceeds 200, and switched back on after it under the
    /// identical condition.
    /// </para>
    /// <para>
    /// THE COMPARISON IS STRICTLY GREATER-THAN, AND THAT BOUNDARY IS OBSERVABLE. At exactly 200
    /// rows there is NO suppression. Consumers must therefore compare
    /// <c>rowCount &gt; RedrawSuppressionRowThreshold</c> and never
    /// <c>rowCount &gt;= RedrawSuppressionRowThreshold</c>; substituting the inclusive form would
    /// move the boundary by one row and change behaviour at exactly the value the legacy names.
    /// </para>
    /// <para>
    /// This is a reproduced legacy behaviour with an observable boundary, and it is deliberately
    /// not described as anything else. The repository publishes no service-level agreement, no
    /// latency budget, no throughput target and no availability commitment anywhere, so no such
    /// objective may be asserted, claimed as met, or used to justify a design choice - including
    /// here, where the temptation to reach for that vocabulary is strongest.
    /// </para>
    /// <para>
    /// Zero is a legal value and means the suppression applies to every non-empty cascade. The
    /// validator therefore requires non-negative rather than positive.
    /// </para>
    /// </remarks>
    [Range(0, int.MaxValue)]
    public int RedrawSuppressionRowThreshold { get; set; } = 200;

    /// <summary>
    /// The initial capacity reserved for the engine's calculation and recursion stack. Defaults to
    /// <c>20</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY DEFAULT. <c>n_cst_dwsvc_columnexp.sru:L2421-L2422</c> is the engine's
    /// constructor: it creates the vector held at <c>:L110</c> and then reserves 20 entries in it.
    /// </para>
    /// <para>
    /// The stack this capacity belongs to is not an implementation detail hidden behind the
    /// boundary: its contents are what the trace event's call-stack string is built from at
    /// <c>:L752-L757</c>, so the ordering it preserves is observable on the contract. The capacity
    /// itself is not - it is a starting allocation, and the stack grows past it - which is why it
    /// is configurable rather than fixed.
    /// </para>
    /// <para>
    /// The validator requires a positive value: a reserved capacity is a size, and zero would
    /// express no intent while quietly discarding the one number the legacy states.
    /// </para>
    /// </remarks>
    [Range(1, int.MaxValue)]
    public int CalcStackInitialCapacity { get; set; } = 20;

    /// <summary>
    /// The pagination that <c>for page</c> aggregates are measured against. Defaults to
    /// <see cref="ExpressionPageResolution.WholeBuffer"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>NOT A PRESERVED LEGACY DEFAULT - see the note on this class.</b> The legacy had no such setting
    /// because a PowerBuilder application owned its own band geometry and <c>for page</c> asked the
    /// runtime which rows were on the page. That geometry is the deferred DesignSystem's half, so the
    /// headless engine takes the range from <see cref="IExpressionPageResolver"/> and this setting is how
    /// a deployment states which one it means.
    /// </para>
    /// <para>
    /// <b>WHY IT EXISTS: WITHOUT IT, THE DEPLOYED SERVICE REFUSED <c>for page</c> ENTIRELY.</b> The
    /// evaluator's class default is <see cref="UnresolvedPageResolver"/> - correct for an evaluator built
    /// by code that has stated nothing - so
    /// <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L27</c>'s <c>sum(salary for page)</c>, the only
    /// <c>for page</c> expression in the repository, answered the malformed sentinel in the running
    /// service unless some wiring site remembered to inject a resolver. A capability that works only when
    /// someone remembers is not wired.
    /// </para>
    /// <para>
    /// <b>THE DEFAULT IS <see cref="ExpressionPageResolution.WholeBuffer"/>, AND IT IS EVIDENCED.</b> The
    /// one surface this system has evidence for declares no page-break band and its only <c>for page</c>
    /// use is a footer sum over a four-row fixture, so "this surface is one page" is measured rather than
    /// assumed. It is still a STATEMENT: a paginated deployment must choose
    /// <see cref="ExpressionPageResolution.FixedRowsPerPage"/>, or
    /// <see cref="ExpressionPageResolution.Unresolved"/> to restore the refusal, because leaving this
    /// value in place on a paginated surface returns the grand total where the page total was asked for.
    /// </para>
    /// <para>
    /// The validator rejects any value outside the three declared members, so a mistyped setting fails
    /// startup rather than silently selecting a resolver.
    /// </para>
    /// </remarks>
    public ExpressionPageResolution PageResolution { get; set; } = ExpressionPageResolution.WholeBuffer;

    /// <summary>
    /// The number of detail rows on a page, used only when <see cref="PageResolution"/> is
    /// <see cref="ExpressionPageResolution.FixedRowsPerPage"/>. Defaults to <c>0</c>, meaning unstated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>NOT A PRESERVED LEGACY DEFAULT</b>, for the same reason as <see cref="PageResolution"/>: the
    /// legacy derived a page from band heights, which this service does not have.
    /// </para>
    /// <para>
    /// <b>THE VALIDATOR ENFORCES THE COUPLING IN BOTH DIRECTIONS, and the second direction is the one
    /// worth stating.</b> A value is REQUIRED and must be positive when the mode is
    /// <see cref="ExpressionPageResolution.FixedRowsPerPage"/> - a page of zero rows describes no
    /// pagination. And a positive value is REFUSED when the mode is anything else, rather than being
    /// ignored: an operator who wrote <c>PageRowsPerPage: 50</c> and left the mode alone has stated a
    /// pagination that would not be applied, and silently ignoring it is how a page total becomes a grand
    /// total with nothing in the configuration to show why.
    /// </para>
    /// <para>
    /// Zero rather than a nullable integer, because zero is already the "no pagination at all" value the
    /// resolver itself rejects, so it carries the unstated meaning without a second kind of absence.
    /// </para>
    /// </remarks>
    [Range(0, int.MaxValue)]
    public int PageRowsPerPage { get; set; }
}

// ----------------------------------------------------------------------------------------------
// PRESERVED LEGACY DEFAULTS - group 5 of 5: drop-down search
// ----------------------------------------------------------------------------------------------

/// <summary>
/// Drop-down search behaviour. Bound from <c>DataServices:DropDownSearch</c>.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru</c>,
/// which the plan splits: the headless half ships here - filter-expression construction, the
/// search state machine, and the row and filtered counts - while window positioning and text-input
/// method handling are deferred and named as a reserved Gateway extension point. Every setting in
/// this group belongs to the headless half.
/// </para>
/// <para>
/// This is the most hazardous group in the file, for one specific reason recorded on
/// <see cref="FilterType"/>: two unrelated legacy values in this single object are both the number
/// seven, and confusing them is a silent behavioural change.
/// </para>
/// </remarks>
public sealed class DropDownSearchOptions
{
    /// <summary>
    /// Which columns the search filter matches against, as a combinable bitmask. Defaults to
    /// <c>3</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY DEFAULT. <c>n_cst_dwsvc_dropdownsearch.sru:L57</c> declares
    /// <c>privatewrite ulong #FilterType = FILTER_DISP + FILTER_DISP_PY</c>. The four constants
    /// above it are <c>FILTER_DISP = 1</c> at <c>:L50</c> (match the display column),
    /// <c>FILTER_DISP_PY = 2</c> at <c>:L51</c> (match the display column by the Pinyin first
    /// letters of the input), <c>FILTER_DATA = 4</c> at <c>:L52</c> (match the data column) and
    /// <c>FILTER_ALL</c> at <c>:L53</c>, which is their sum. The default is therefore
    /// <c>1 | 2</c>, that is <c>3</c>, spelled here as the numeric literal for the same reason
    /// <see cref="RowSelectOptions.Style"/> is: these constants live on the legacy service object,
    /// not in the framework-wide catalogue.
    /// </para>
    /// <para>
    /// HAZARD, STATED EXPLICITLY BECAUSE THE TWO NUMBERS ARE IDENTICAL AND THE CONTRACTS ARE NOT.
    /// <c>FILTER_ALL</c> is 7. THE DEFAULT IS NOT SEVEN - it is 3, because the legacy deliberately
    /// omits the data-column bit. Separately, <see cref="PinyinMatchFlags"/> IS 7, and that one is
    /// correct. Same number, different contract. Setting this property to 7 would enable a
    /// data-column match the legacy does not perform by default, which is a new behaviour rather
    /// than a preserved one.
    /// </para>
    /// <para>
    /// The value is combinable - the comment at <c>:L49</c> says so - and the three bits are read
    /// independently by bit tests at <c>:L318</c>, <c>:L321</c> and <c>:L326</c>, each guarding one
    /// clause of the constructed filter expression. The type is <c>uint</c>, the exact width of the
    /// legacy 32-bit unsigned declaration.
    /// </para>
    /// <para>
    /// THE VALIDATOR DELIBERATELY DOES NOT CHECK THIS PROPERTY, AND THE OMISSION IS THE POINT.
    /// The legacy setter at <c>:L304-L305</c> is two statements - assign the argument, return
    /// success - with no validation whatsoever, so zero is accepted and simply produces an empty
    /// filter. Adding a non-zero or range guard here would add behaviour the legacy does not have.
    /// That is the exact inverse of <see cref="RowSelectOptions.Style"/>, whose legacy setter DOES
    /// guard zero, and the asymmetry between the two must not be harmonised in either direction.
    /// </para>
    /// </remarks>
    public uint FilterType { get; set; } = 3;

    /// <summary>
    /// Whether rows filtered out of the drop-down are still shown. Three-valued: defaults to
    /// <see langword="null"/>, meaning determine automatically.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY DEFAULT, AND THE NULL IS THE DEFAULT.
    /// <c>n_cst_dwsvc_dropdownsearch.sru:L58</c> declares
    /// <c>privatewrite boolean #ShowFilteredRows</c>, and its trailing comment continued at
    /// <c>:L59</c> ends by stating that a null value means automatic determination, and that this
    /// is the default. The null is explicit rather than incidental: the constructor at <c>:L503</c>
    /// consists of nothing but a super call and a call setting this very field to null.
    /// </para>
    /// <para>
    /// THE NULL GATES A REAL THREE-BRANCH ALGORITHM, WHICH IS WHY <c>bool?</c> IS MANDATORY. The
    /// resolver at <c>:L260</c> opens with
    /// <c>if Not IsNull(#ShowFilteredRows) then return #ShowFilteredRows</c> - an explicit value
    /// short-circuits it - and only when the field is null does it evaluate <c>:L261-L263</c> in
    /// order: if the display column and the data column are the same column, false; otherwise if
    /// the presentation style is grid, true; otherwise the conjunction of more than one row and a
    /// visible vertical scroll bar.
    /// </para>
    /// <para>
    /// So collapsing this to a non-nullable <see cref="bool"/> would not merely change a default,
    /// it would DELETE that algorithm outright, because false and "decide for me" would become
    /// indistinguishable. Never write an initializer here, never widen the type, and never
    /// normalise the null during validation - the validator therefore checks this property not at
    /// all, deliberately, and that omission is as load bearing as any rule it does apply.
    /// </para>
    /// </remarks>
    public bool? ShowFilteredRows { get; set; }

    /// <summary>
    /// The flags passed to Pinyin first-letter matching when the search input is alphabetic.
    /// Defaults to all three flags set, that is <c>7</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY DEFAULT. <c>n_cst_dwsvc_dropdownsearch.sru:L323</c> appends a Pinyin
    /// first-letter clause to the constructed filter with the flags argument written as the literal
    /// 7. That clause is reached only when both guards above it hold: the Pinyin bit of
    /// <see cref="FilterType"/> is set, tested at <c>:L321</c>, and the input matches an alphabetic
    /// pattern, tested at <c>:L322</c>.
    /// </para>
    /// <para>
    /// Unlike the two bitmasks above, this default is composed from NAMED constants rather than
    /// written as a literal, because these three - unlike the row-select and filter-type sets - do
    /// live in the framework-wide catalogue, at
    /// <c>ws_objects/pfw.shared.pbl.src/enums.sru:L1147-L1149</c>: ignore case is 1, ignore
    /// full-width versus half-width forms is 2, and match fuzzy pronunciation is 4. Their sum is
    /// exactly the 7 the call site hardcodes, so composing them proves the literal rather than
    /// merely restating it. This is the one and only place this file reaches into
    /// PowerFramework.Shared.Kernel, and the reference exists because the plan requires every value
    /// the legacy hardcodes to move into an options type.
    /// </para>
    /// <para>
    /// The type is <c>long</c>, matching the declared width of those three constants.
    /// </para>
    /// </remarks>
    public long PinyinMatchFlags { get; set; } =
        Enums.PY_LIKE_IGNORE_CASE | Enums.PY_LIKE_IGNORE_WIDTH | Enums.PY_LIKE_FUZZY_SOUND;
}


// ----------------------------------------------------------------------------------------------
// BOUNDARY-CREATED SETTINGS - the two upstream addresses
// ----------------------------------------------------------------------------------------------

/// <summary>
/// Where to reach the Persistence service. Bound from <c>DataServices:Persistence</c>.
/// </summary>
/// <remarks>
/// CREATED BY THE DECOMPOSITION; there is no legacy equivalent and none is missing. PowerFramework
/// is a library with no process of its own, no listener and no server tier, so the call this group
/// addresses used to be an in-process method call with no address to configure. It became a network
/// call when the decomposition drew a boundary between the DataWindow service layer and the only
/// component permitted to generate or execute SQL.
/// </remarks>
public sealed class PersistenceClientOptions
{
    /// <summary>
    /// The absolute base address of the Persistence service's gRPC endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Consumed by Clients/PersistenceClient.cs to construct the channel carrying the
    /// persistence.v1 query, update, command and transaction contracts. The service listens on port
    /// 5101 in the documented port map, and the sibling appsettings.Development.json retargets this
    /// value at a loopback address for developer runs.
    /// </para>
    /// <para>
    /// NO DEFAULT IS SUPPLIED, AND THAT IS DELIBERATE. A fallback address would let a misconfigured
    /// deployment start successfully and then fail on the first request, where the fault is far
    /// harder to attribute than at startup. Leaving it empty makes the validator's rules bite at
    /// startup instead - present, absolute, and using a scheme the client can reach - which is the
    /// fail-fast posture this service inherits from the legacy framework's
    /// terminate-on-structural-fault handler [ws_objects/pfw.pbl.src/pfw.sra:L111-L144].
    /// </para>
    /// <para>
    /// Only an address appears here. Constraint C-A permits exactly that much cross-service
    /// knowledge and no more: no upstream schema, internal type or configuration value of the
    /// Persistence service is represented anywhere in this file, and the wire shape travels
    /// exclusively through the generated contract assembly.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Address { get; set; } = string.Empty;
}

/// <summary>
/// Where to reach the Security service. Bound from <c>DataServices:Security</c>.
/// </summary>
/// <remarks>
/// CREATED BY THE DECOMPOSITION; there is no legacy equivalent. The legacy cryptographic surface
/// was an in-process object in the same address space, so it had no address either. It became a
/// network call when the decomposition made Security the sole holder of the keyed cryptographic
/// surface and the sole issuer of bearer credentials.
/// </remarks>
public sealed class SecurityClientOptions
{
    /// <summary>
    /// The absolute base address of the Security service's REST endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Consumed by Clients/SecurityClient.cs for the crypto contract, which is REST precisely so
    /// that its published verification material and discovery document are reachable by the
    /// framework's stock handlers with no bespoke retrieval code. The service listens on port 5104
    /// in the documented port map, and the sibling appsettings.Development.json retargets this value
    /// at a loopback address for developer runs.
    /// </para>
    /// <para>
    /// This is an ADDRESS ONLY. It carries no credential and no signing material of any kind, and
    /// no property in this file could hold either: raw material never crosses the wire from a
    /// caller under the crypto contract, which takes an opaque reference that Security resolves on
    /// its own side. The inbound verification settings this service applies to its own requests are
    /// declared separately on <see cref="JwtAuthenticationOptions"/>.
    /// </para>
    /// <para>
    /// No default is supplied, for the same fail-fast reason recorded on
    /// <see cref="PersistenceClientOptions.Address"/>.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string BaseAddress { get; set; } = string.Empty;

    /// <summary>
    /// The client identity this service presents on the single mutual-TLS edge in the system - the
    /// Security service's token endpoint. Bound from <c>DataServices:Security:MutualTls</c>. Paths
    /// only, never material.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CREATED BY THE DECOMPOSITION, and required for a functional reason rather than as hardening.
    /// <c>POST /v1/tokens</c> is protected by mutual TLS and by nothing else, because <b>a caller
    /// cannot present a bearer token in order to obtain its first bearer token</b>. DataServices is one
    /// of the two services that request tokens - it needs one for its own C-02 cryptographic calls and
    /// one for the audience beneath it - so with no client certificate to present it obtains none, and
    /// every authenticated call it would make is unreachable. The published contract has required this
    /// since it was authored; this group is what makes it configurable.
    /// </para>
    /// <para>
    /// TWO PATHS AND NO MATERIAL, ENFORCED BY THE MEMBER SET RATHER THAN BY A CONVENTION. There is no
    /// property here for a certificate body, a private key body or a passphrase, so there is nowhere
    /// for one to be placed - which is the same rule the keyed cryptographic surface follows, where a
    /// caller passes an opaque reference and never key bytes. Both values name material mounted from
    /// the orchestration secret layer; nothing is committed to this repository or embedded in an image.
    /// </para>
    /// <para>
    /// OPTIONAL AS A GROUP AND INSEPARABLE WHEN PRESENT. Both empty means this deployment presents no
    /// client certificate and requests no token. Setting one without the other is refused by
    /// <see cref="DataServicesOptions"/>'s validator: a certificate cannot complete a handshake without
    /// its key and a key has nothing to present without its certificate, so half a client identity is
    /// unusable rather than merely weaker.
    /// </para>
    /// </remarks>
    public MutualTlsClientOptions MutualTls { get; set; } = new();
}

/// <summary>
/// The client identity presented on the token-issuance edge. Bound from
/// <c>DataServices:Security:MutualTls</c>.
/// </summary>
/// <remarks>
/// A separate type rather than two loose properties, so that the pair can be validated as a pair and
/// so that the absence of any material-bearing member is a property of a named type a reader can check
/// at a glance.
/// </remarks>
public sealed class MutualTlsClientOptions
{
    /// <summary>
    /// Path to the PEM-encoded client certificate this service presents. Empty means none.
    /// </summary>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>
    /// Path to the PEM-encoded key file for <see cref="CertificatePath"/>. Empty means none.
    /// </summary>
    public string CertificateKeyPath { get; set; } = string.Empty;

    /// <summary>
    /// Whether this deployment presents a client certificate at all.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CertificatePath) || !string.IsNullOrWhiteSpace(CertificateKeyPath);

    /// <summary>
    /// Describes the one way this group can be wrong: half-configured.
    /// </summary>
    /// <param name="configurationKeyPrefix">
    /// The configuration path of this group, quoted into the message so an operator can find the
    /// offending key without reading source.
    /// </param>
    /// <returns>
    /// One message when exactly one of the two paths is set, otherwise an empty sequence.
    /// </returns>
    /// <remarks>
    /// NO PATH IS EVER ECHOED INTO A MESSAGE. A path is not itself a credential, but it names where one
    /// is mounted, and a startup log is exactly the wrong place to publish that. The configuration key
    /// is sufficient for an operator to find the setting.
    /// </remarks>
    internal IEnumerable<string> DescribeFailures(string configurationKeyPrefix)
    {
        bool hasCertificate = !string.IsNullOrWhiteSpace(CertificatePath);
        bool hasKey = !string.IsNullOrWhiteSpace(CertificateKeyPath);

        if (hasCertificate == hasKey)
        {
            yield break;
        }

        yield return string.Concat(
            configurationKeyPrefix,
            ":",
            nameof(CertificatePath),
            " and ",
            configurationKeyPrefix,
            ":",
            nameof(CertificateKeyPath),
            " must be configured together or not at all. A client certificate cannot complete a TLS "
                + "handshake without its key, and a key has nothing to present without its certificate, "
                + "so half of this pair is unusable rather than merely weaker. Neither path is quoted "
                + "here, because a startup log must not record where key material is mounted.");
    }
}

// ----------------------------------------------------------------------------------------------
// BOUNDARY-CREATED SETTINGS - transport failure handling for the two typed clients
// ----------------------------------------------------------------------------------------------

/// <summary>
/// Transport-failure handling for the two typed clients. Bound from
/// <c>DataServices:Resilience</c>.
/// </summary>
/// <remarks>
/// <para>
/// CREATED BY THE DECOMPOSITION, and it exists for exactly one reason: AN IN-PROCESS CALL CANNOT
/// FAIL IN TRANSIT AND A NETWORK CALL CAN. Every call this group covers was an in-process method
/// call in the legacy library, so the failure mode did not exist and there was nothing to
/// configure. Handling a failure mode that the decomposition itself creates is required BY the
/// transition, not an improvement layered on top of it - without it, the first transient transport
/// fault would surface as a defect the legacy could not have had, which would be a regression
/// introduced by the refactor rather than a preserved behaviour.
/// </para>
/// <para>
/// The repository publishes no service-level agreement, no latency budget, no throughput target and
/// no availability commitment anywhere, so this group asserts none, and none of the values below
/// may be read as one. They are failure-handling settings and nothing else.
/// </para>
/// <para>
/// The two clients are configured separately rather than sharing one instance, because the two
/// upstreams are different services reached over different transports. The shape is shared; the
/// values are not.
/// </para>
/// </remarks>
public sealed class ResilienceOptions
{
    /// <summary>
    /// Failure handling applied to the Persistence gRPC client.
    /// </summary>
    public ClientResilienceOptions Persistence { get; set; } = new();

    /// <summary>
    /// Failure handling applied to the Security REST client.
    /// </summary>
    public ClientResilienceOptions Security { get; set; } = new();
}

/// <summary>
/// Failure-handling settings for one typed client. Created by the decomposition; no legacy
/// equivalent.
/// </summary>
/// <remarks>
/// <para>
/// The defaults below are the values the resilience library's own standard handler declares for
/// itself. Restating them here rather than leaving them implicit is the whole point of the group:
/// it makes each one visible in the configuration document and overridable per environment, and it
/// means nothing in this file is an invented number. Consumed by Clients/PersistenceClient.cs and
/// Clients/SecurityClient.cs when they build their pipelines.
/// </para>
/// <para>
/// Every duration is a <see cref="TimeSpan"/>, which the configuration binder parses from the
/// usual textual form, so an environment override is expressible without a bespoke converter. The
/// durations carry no data-annotation attribute on purpose: a required-value annotation on a value
/// type is inert, because a value type is never absent, so their positivity is enforced by
/// <see cref="DataServicesOptionsValidator"/> instead. Annotating them would have looked like a
/// rule while enforcing nothing, which is worse than no annotation at all.
/// </para>
/// </remarks>
public sealed class ClientResilienceOptions
{
    /// <summary>
    /// How many times a failed attempt is retried. Zero disables retrying.
    /// </summary>
    /// <remarks>
    /// Zero is a legal value and is validated as such: a deployment that wants a single attempt and
    /// an immediate surfaced failure is expressing a policy, not a misconfiguration.
    /// </remarks>
    [Range(0, int.MaxValue)]
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// The base delay from which the backoff between retry attempts is derived. Must be positive.
    /// </summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The proportion of failed attempts within one sampling window that opens the circuit,
    /// expressed as a fraction greater than zero and at most one.
    /// </summary>
    /// <remarks>
    /// The validator enforces both bounds. Zero is rejected rather than treated as "never open",
    /// because the underlying strategy rejects it too, and a setting that faults the pipeline at
    /// construction time is exactly the kind of structural fault startup validation exists to catch.
    /// </remarks>
    [Range(0.0d, 1.0d)]
    public double CircuitBreakerFailureRatio { get; set; } = 0.1d;

    /// <summary>
    /// The minimum number of attempts that must be observed within one sampling window before the
    /// failure proportion is considered at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lower bound of two is the underlying strategy's own, and it is meaningful rather than
    /// arbitrary: a proportion computed from a single observation carries no information.
    /// </para>
    /// <para>
    /// The name mirrors the property this value feeds on the underlying circuit-breaker strategy, so
    /// that the mapping between configuration and pipeline is one to one and needs no lookup table.
    /// It is A COUNT OF OBSERVED ATTEMPTS and nothing else - it is not a rate, not a target and not
    /// a claim of any kind about what this service achieves. Renaming it would break that one-to-one
    /// mapping for no gain.
    /// </para>
    /// </remarks>
    [Range(2, int.MaxValue)]
    public int CircuitBreakerMinimumThroughput { get; set; } = 100;

    /// <summary>
    /// The window over which attempts are sampled to compute the failure proportion. Must be
    /// positive.
    /// </summary>
    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the circuit stays open before attempts are allowed through again. Must be positive.
    /// </summary>
    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The ceiling on one whole request, retries included, after which it is abandoned. Must be
    /// positive.
    /// </summary>
    /// <remarks>
    /// This is a failure-handling bound, not a target: it decides when an unanswered call is
    /// treated as failed rather than waited on indefinitely, which is a decision the legacy never
    /// had to make because an in-process call always returned.
    /// </remarks>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}


// ----------------------------------------------------------------------------------------------
// BOUNDARY-CREATED SETTINGS - server-held session lifetimes
// ----------------------------------------------------------------------------------------------

/// <summary>
/// Lifetime and admission settings for the two server-held session kinds. Bound from
/// <c>DataServices:Sessions</c>.
/// </summary>
/// <remarks>
/// <para>
/// CREATED BY THE DECOMPOSITION; there is no legacy equivalent, and the reason is structural rather
/// than an omission. In the legacy library both kinds of state lived inside an object whose lifetime
/// was the hosting control's, so neither needed a lifetime of its own:
/// </para>
/// <para>
/// The validation chain's cross-event state is four private fields on the DataWindow service
/// extension at
/// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L89-L96</c> - the disabled-event
/// mask, the two re-entrancy flags, and the item-changed result stashed for the validation-error
/// event to consume. The expression engine's cross-DataWindow references are live object pointers,
/// declared at <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L80-L83</c>.
/// </para>
/// <para>
/// A stateless request boundary has nowhere to put either, so both become server-held state
/// correlated by an identifier and opened and closed by dedicated calls - and anything server-held
/// and correlated by an identifier needs a lifetime, because a client that never closes its session
/// must not retain it forever. Hence this group.
/// </para>
/// </remarks>
public sealed class SessionsOptions
{
    /// <summary>
    /// Lifetime and admission settings for validation sessions, which carry the four pieces of
    /// cross-event state of the item-change and validation chain.
    /// </summary>
    public SessionLifetimeOptions ValidationSession { get; set; } = new();

    /// <summary>
    /// Lifetime and admission settings for expression sessions, which scope the DataWindow handles
    /// that cross-DataWindow expression variables resolve through.
    /// </summary>
    /// <remarks>
    /// The scoping is what makes those variables expressible at all across a boundary, and its
    /// limit is deliberate and documented: a reference is supported only when both DataWindows are
    /// co-resident in the same session, and a reference reaching outside one is refused with a
    /// defined error rather than answered with a guess.
    /// </remarks>
    public SessionLifetimeOptions ExpressionSession { get; set; } = new();
}

/// <summary>
/// Lifetime and admission settings for one kind of server-held session. Created by the
/// decomposition; no legacy equivalent.
/// </summary>
/// <remarks>
/// The defaults are starting points for a deployment to tune rather than reproductions of any
/// legacy value, because no legacy value exists to reproduce - the legacy lifetime was the hosting
/// control's own. Both are validated as positive, since a zero idle timeout would expire a session
/// the instant it opened and a zero concurrency ceiling would refuse every session.
/// </remarks>
public sealed class SessionLifetimeOptions
{
    /// <summary>
    /// How long a session may sit without a call before it is closed and its state released. Must
    /// be positive.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How many sessions of this kind may be open at once. Must be positive.
    /// </summary>
    /// <remarks>
    /// An admission bound rather than a target: it decides when a further session is refused
    /// outright, which is a decision the legacy never had to make because its sessions were objects
    /// in the caller's own address space.
    /// </remarks>
    [Range(1, int.MaxValue)]
    public int MaxConcurrentSessions { get; set; } = 100;
}

// ----------------------------------------------------------------------------------------------
// BOUNDARY-CREATED SETTINGS - event-chain ordering
// ----------------------------------------------------------------------------------------------

/// <summary>
/// How the event chain treats out-of-order delivery. Bound from <c>DataServices:EventChain</c>.
/// </summary>
/// <remarks>
/// CREATED BY THE DECOMPOSITION; there is no legacy equivalent, because in the legacy library the
/// 22-event chain was dispatched in-process by the runtime and could not arrive out of order at all.
/// The question this group answers only becomes askable once the chain crosses a wire.
/// </remarks>
public sealed class EventChainOptions
{
    /// <summary>
    /// Whether an out-of-order event arrival is a hard error. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The item-change and validation chain is assigned the strictly synchronous ordering pattern,
    /// under which the monotonic sequence numbers on the stream exist FOR DETECTION ONLY: an
    /// out-of-order arrival is a hard error and never a reorder opportunity. Defaulting to
    /// <see langword="true"/> encodes that assignment.
    /// </para>
    /// <para>
    /// Reordering that group is not merely undesirable, it is semantically impossible, which is why
    /// the default is what it is. The validation-error handler reads AND CLEARS the result stashed by
    /// the item-change event that precedes it and pre-sets its own outcome from that value
    /// [<c>se_cst_dw.sru:L322-L385</c>], the item-change handler fires a nested event from inside
    /// itself [<c>:L182-L253</c>], and the kill-focus handler queues a deferred accept only when the
    /// item-change re-entrancy flag is clear [<c>:L387-L393</c>]. Each of those is a function of the
    /// preceding event's outcome, so delivering them in a different order would not produce a
    /// delayed result - it would produce a wrong one.
    /// </para>
    /// <para>
    /// The setting exists rather than being hardwired because the notification-shaped events of the
    /// chain - focus, mouse and row-focus - are assigned the sequencing-token pattern instead, and a
    /// deployment must be able to state which posture its consumers implement without a rebuild.
    /// </para>
    /// </remarks>
    public bool StrictOrdering { get; set; } = true;
}

// ----------------------------------------------------------------------------------------------
// INBOUND BEARER VALIDATION - the second bindable root
// ----------------------------------------------------------------------------------------------

/// <summary>
/// Inbound bearer-credential VALIDATION settings, bound from the top-level
/// <see cref="SectionName"/> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// CREATED BY THE DECOMPOSITION. The legacy opened no listening socket, registered no route and
/// received no unsolicited request, so it had nothing to authenticate and no authentication to
/// configure. Decomposition created the system's first ingress, so the requirement that no new
/// attack surface be introduced can only mean that every newly created surface is authenticated
/// from the outset - which is what this type configures, on every internal edge rather than only at
/// the perimeter.
/// </para>
/// <para>
/// VERIFICATION MATERIAL ONLY, AND THAT IS A HARD BOUNDARY. Security is the SOLE issuer in this
/// system: exactly one signing value exists anywhere in it and Security holds it. This service
/// verifies and never issues, so this type declares no signing property of any name - none, in any
/// spelling, and one must never be added. The project file corroborates the same boundary from the
/// other direction by deliberately omitting the minting package, which means the capability is not
/// merely unused here but unreachable.
/// </para>
/// <para>
/// The one property name below containing the word "key" is
/// <see cref="ValidateIssuerSigningKey"/>, and it is a BOOLEAN SWITCH: it selects whether the
/// framework's handler verifies the signature it retrieves from Security's published verification
/// document. It holds no material, and being a <see cref="bool"/> it could not.
/// </para>
/// <para>
/// This type is bound at the TOP LEVEL rather than under the <c>DataServices</c> root, by
/// convention, so that the framework's own handler and Program.cs find it where they expect. Nesting
/// it would move its path and break the sibling appsettings.json that declares it there.
/// </para>
/// <para>
/// Validated at startup by <see cref="JwtAuthenticationOptionsValidator"/>, which is fatal for the
/// same fail-fast reason recorded on <see cref="DataServicesOptions"/>: a service that cannot verify
/// an inbound credential must not start and then accept requests it cannot authenticate. The
/// annotations on the members below are necessary but NOT sufficient, and an earlier form of this file
/// claimed otherwise. An attribute can say that an authority is present; it cannot say that the value
/// is an absolute address, that its scheme is one the handler can fetch metadata over, or that it does
/// not directly contradict <see cref="RequireHttpsMetadata"/>. Each of those survives an
/// annotation-only check and then fails at metadata retrieval or on the first protected request, long
/// after the process reported itself started. They are enforced in the validator instead.
/// </para>
/// </remarks>
public sealed class JwtAuthenticationOptions
{
    /// <summary>
    /// The configuration section this type binds from.
    /// </summary>
    /// <remarks>
    /// A colon-delimited path, so it binds the <c>Jwt</c> child of the top-level
    /// <c>Authentication</c> section. The sibling appsettings files must agree with it exactly.
    /// </remarks>
    public const string SectionName = "Authentication:Jwt";

    /// <summary>
    /// The absolute address of the Security service, from which the framework's handler discovers
    /// the verification document and the issuer.
    /// </summary>
    /// <remarks>
    /// Security is REST precisely so that this works with no bespoke retrieval code: the handler
    /// fetches the standard discovery document and the published verification set from this
    /// authority by itself. That is a net REDUCTION in hand-written security code across the
    /// estate, which is the reason the transport was chosen.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// An explicit address for the discovery document, overriding the one derived from
    /// <see cref="Authority"/>. Optional.
    /// </summary>
    /// <remarks>
    /// Nullable and unset by default, because the derived address is correct whenever the authority
    /// is addressed directly. It exists for the deployment shapes where it is not - an authority
    /// reached through a differently addressed route, for instance - and when it is supplied it must
    /// be an absolute address, which is what the framework's handler requires of it.
    /// </remarks>
    public string? MetadataAddress { get; set; }

    /// <summary>
    /// The audience this service accepts in an inbound credential.
    /// </summary>
    /// <remarks>
    /// Required, because accepting a credential minted for a different service would defeat the
    /// point of authenticating the edge at all. The value is this service's own identity in the
    /// audience set Security mints against.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Whether discovery metadata must be retrieved over a transport-secured connection. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// The secure value is the default deliberately, so that only a deployment that has explicitly
    /// chosen otherwise - a developer run over loopback, expressed in the development settings file -
    /// relaxes it. A default of <see langword="false"/> would make the relaxation invisible.
    /// </remarks>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Whether the credential's issuer is checked against the discovered issuer. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>
    /// Whether the credential's audience is checked against <see cref="Audience"/>. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>
    /// Whether the credential's validity window is enforced. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Security mints short-lived credentials, so this switch is what makes the shortness mean
    /// anything.
    /// </remarks>
    public bool ValidateLifetime { get; set; } = true;

    /// <summary>
    /// Whether the credential's signature is verified against the material published by the
    /// authority. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// A switch, not a value. It selects whether verification happens; it does not and cannot carry
    /// anything used to perform it, which arrives from the authority at runtime. This is the only
    /// property in this file whose name contains the word "key", and the distinction is recorded so
    /// that a search for that word lands on an explanation rather than on a suspicion.
    /// </remarks>
    public bool ValidateIssuerSigningKey { get; set; } = true;
}


// ----------------------------------------------------------------------------------------------
// VALIDATION
// ----------------------------------------------------------------------------------------------

/// <summary>
/// Validates a bound <see cref="DataServicesOptions"/> instance at startup.
/// </summary>
/// <remarks>
/// <para>
/// Registered by Program.cs alongside the binding so that validation runs at startup rather than on
/// first access, which makes a structural configuration fault fatal before the service accepts a
/// request. That is deliberate. The legacy framework's own posture on a structural fault is to
/// terminate - its systemerror handler unpacks a seven-field assertion payload and then executes
/// <c>HALT CLOSE</c> [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, terminating at :L143] - so the
/// faithful .NET equivalent is fail-fast startup validation and process termination. Softening this
/// into warn-and-continue would be a behavioural change dressed as robustness.
/// </para>
/// <para>
/// WHY THIS TYPE EXISTS AT ALL, RATHER THAN JUST ANNOTATIONS. Data-annotation validation does not
/// recurse into nested objects, so annotating a group would achieve nothing on its own: every rule
/// below the root has to be reached deliberately. This type reaches them - it runs the annotations
/// of each group itself and then adds the cross-field and set-membership rules that no annotation can
/// express. It is hand-written rather than delegated to a rule framework on purpose: the ported
/// validators in this service must show their exact legacy semantics, and a framework would obscure
/// the one property that matters most here, which is parity. Nothing outside
/// System.ComponentModel.DataAnnotations and Microsoft.Extensions.Options is used, both of which
/// arrive from the shared framework, so no package is added and each service still builds
/// independently.
/// </para>
/// <para>
/// ALL failures are collected and returned together rather than the first one being thrown. A
/// deployment correcting its configuration should see every fault in one startup attempt, not
/// discover them one restart at a time.
/// </para>
/// <para>
/// Its subject is <see cref="DataServicesOptions"/> only. <see cref="JwtAuthenticationOptions"/> binds
/// a different section and has its own counterpart, <see cref="JwtAuthenticationOptionsValidator"/>,
/// registered separately. One validator per bound section, so a failure message always names a
/// section that exists.
/// </para>
/// <para>
/// TWO OMISSIONS BELOW ARE DELIBERATE AND LOAD BEARING, and both are marked at the point of
/// omission so that nobody "completes" this type by adding them:
/// <see cref="DropDownSearchOptions.FilterType"/> is not checked, because its legacy setter
/// validates nothing at all, and <see cref="DropDownSearchOptions.ShowFilteredRows"/> is neither
/// checked nor normalised, because its null is a third state that gates a three-branch algorithm.
/// </para>
/// </remarks>
public sealed class DataServicesOptionsValidator : IValidateOptions<DataServicesOptions>
{
    /// <summary>
    /// The three locale identifiers the legacy provider selection accepts, in its own order.
    /// </summary>
    /// <remarks>
    /// From <c>ws_objects/pfw.pbl.src/pfw.sra:L95-L102</c>. Compared ordinally, because PowerScript's
    /// <c>choose case</c> on strings is case-sensitive and so the legacy would not have matched a
    /// differently cased spelling either.
    /// </remarks>
    private static readonly string[] AcceptedLocales = ["en", "chs", "cht"];

    /// <summary>
    /// Validates one bound instance.
    /// </summary>
    /// <param name="name">
    /// The named options instance being validated, or <see langword="null"/> or empty for the
    /// default instance. Included in every message so a fault in a named instance is attributable.
    /// </param>
    /// <param name="options">The bound instance to validate.</param>
    /// <returns>
    /// <see cref="ValidateOptionsResult.Success"/> when no rule is broken, otherwise a failure
    /// result carrying one message per broken rule, each prefixed with the configuration path it
    /// belongs to.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public ValidateOptionsResult Validate(string? name, DataServicesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];
        string prefix = string.IsNullOrEmpty(name)
            ? DataServicesOptions.SectionName
            : string.Concat(DataServicesOptions.SectionName, "[", name, "]");

        // --- Localization: required, and one of exactly three accepted identifiers -------------
        string path = string.Concat(prefix, ":Localization");
        if (EnsureGroupBound(options.Localization, path, failures))
        {
            AppendAnnotationFailures(options.Localization, path, failures);

            string locale = options.Localization.Locale;
            if (!string.IsNullOrWhiteSpace(locale) && !AcceptedLocales.Contains(locale, StringComparer.Ordinal))
            {
                failures.Add(string.Concat(
                    path,
                    ":Locale must be one of \"en\", \"chs\" or \"cht\" (case-sensitively, as at ",
                    "ws_objects/pfw.pbl.src/pfw.sra:L95-L102), because Program.cs maps it onto a ",
                    "localization provider and cannot map any other value. Supplied: \"",
                    locale,
                    "\"."));
            }
        }

        // --- RowSelect: zero is rejected, and this is the evidenced half of the asymmetry -------
        path = string.Concat(prefix, ":RowSelect");
        if (EnsureGroupBound(options.RowSelect, path, failures) && options.RowSelect.Style == 0L)
        {
            failures.Add(string.Concat(
                path,
                ":Style must not be zero. The legacy setter rejects zero explicitly at ",
                "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru:L169, which ",
                "returns an invalid-argument code for it."));
        }

        // --- ContextMenu: no rule, and none may be added ----------------------------------------
        // Every member of that group is a boolean whose whole domain - true and false - is legal in
        // the legacy, whose declarations at n_cst_dwsvc_contextmenu.sru:L49-L53 are directly
        // assignable rather than guarded. There is nothing here to check, so nothing is checked.

        // --- ColumnExpression: the numeric bounds, plus the page-resolution coupling -------------
        path = string.Concat(prefix, ":ColumnExpression");
        if (EnsureGroupBound(options.ColumnExpression, path, failures))
        {
            AppendAnnotationFailures(options.ColumnExpression, path, failures);
            AppendPageResolutionFailures(options.ColumnExpression, path, failures);
        }

        // --- DropDownSearch: DELIBERATELY UNVALIDATED, and the omission is the rule --------------
        // FilterType accepts every value including zero, because its legacy setter at
        // n_cst_dwsvc_dropdownsearch.sru:L304-L305 assigns the argument and returns success with no
        // validation of any kind; a guard here would add behaviour the legacy does not have.
        // ShowFilteredRows is neither checked nor normalised, because its null is an explicit third
        // state - set at the legacy constructor, :L503 - which gates the three-branch resolution at
        // :L260-L263; coercing it would delete that algorithm. PinyinMatchFlags is a combinable
        // bitmask whose whole domain is likewise legal at its call site, :L323.
        // This group is therefore passed over on purpose. Do not add a case for it.

        // --- Persistence and Security: required, and usable absolute addresses -------------------
        path = string.Concat(prefix, ":Persistence");
        if (EnsureGroupBound(options.Persistence, path, failures))
        {
            AppendAnnotationFailures(options.Persistence, path, failures);
            AppendAddressFailure(options.Persistence.Address, string.Concat(path, ":Address"), failures);
        }

        path = string.Concat(prefix, ":Security");
        if (EnsureGroupBound(options.Security, path, failures))
        {
            AppendAnnotationFailures(options.Security, path, failures);
            AppendAddressFailure(options.Security.BaseAddress, string.Concat(path, ":BaseAddress"), failures);

            // The token-issuance edge's client identity. Optional as a group and inseparable when
            // present, which is why it is checked here rather than expressed as attributes: no single
            // attribute can say "both or neither".
            string mutualTlsPath = string.Concat(path, ":MutualTls");
            if (EnsureGroupBound(options.Security.MutualTls, mutualTlsPath, failures))
            {
                failures.AddRange(options.Security.MutualTls.DescribeFailures(mutualTlsPath));
            }
        }

        // --- Resilience: both clients, same rules, separate values -------------------------------
        path = string.Concat(prefix, ":Resilience");
        if (EnsureGroupBound(options.Resilience, path, failures))
        {
            AppendClientResilienceFailures(
                options.Resilience.Persistence, string.Concat(path, ":Persistence"), failures);
            AppendClientResilienceFailures(
                options.Resilience.Security, string.Concat(path, ":Security"), failures);
        }

        // --- Sessions: both kinds, same rules, separate values -----------------------------------
        path = string.Concat(prefix, ":Sessions");
        if (EnsureGroupBound(options.Sessions, path, failures))
        {
            AppendSessionLifetimeFailures(
                options.Sessions.ValidationSession, string.Concat(path, ":ValidationSession"), failures);
            AppendSessionLifetimeFailures(
                options.Sessions.ExpressionSession, string.Concat(path, ":ExpressionSession"), failures);
        }

        // --- EventChain: no rule. Its single member is a boolean whose whole domain is legal. -----
        _ = EnsureGroupBound(options.EventChain, string.Concat(prefix, ":EventChain"), failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Records a failure when a group was bound to an explicit null, and reports whether it is safe
    /// to inspect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every group property is declared non-nullable and initialised to a fresh instance, so an
    /// absent section leaves the defaults in place and this check passes. It exists for the one shape
    /// that can still defeat that, which is a configuration document declaring the group as an
    /// explicit null, and for a caller constructing the graph by hand. Reporting it as a failure is
    /// strictly better than dereferencing it: the fault becomes an attributable startup message
    /// instead of a null-reference exception with no configuration path attached.
    /// </para>
    /// <para>
    /// The parameter carries a not-null-when-true annotation so that a caller returning early on a
    /// false result may dereference the group afterwards without a second null check. That is not
    /// cosmetic: without it every call site needs a redundant re-check to satisfy nullable analysis,
    /// and the second half of such a check is a line no test can ever reach.
    /// </para>
    /// </remarks>
    private static bool EnsureGroupBound(
        [NotNullWhen(true)] object? group,
        string configurationPath,
        List<string> failures)
    {
        if (group is not null)
        {
            return true;
        }

        failures.Add(string.Concat(
            configurationPath,
            " was bound to null. Omit the section entirely to accept its defaults, rather than ",
            "declaring it as a null value."));
        return false;
    }

    /// <summary>
    /// Runs the data annotations declared on one group and appends one message per violation,
    /// qualified by the configuration path.
    /// </summary>
    /// <remarks>
    /// Data-annotation validation does not recurse, so this is called once per group rather than once
    /// per root. Annotations are used for the rules they express cleanly - presence and numeric range
    /// - and everything else is written out explicitly in <see cref="Validate"/>.
    /// </remarks>
    private static void AppendAnnotationFailures(object group, string configurationPath, List<string> failures)
    {
        ValidationContext context = new(group);
        List<ValidationResult> results = [];
        if (Validator.TryValidateObject(group, context, results, validateAllProperties: true))
        {
            return;
        }

        foreach (ValidationResult result in results)
        {
            string message = result.ErrorMessage ?? "is invalid.";
            bool attributed = false;

            foreach (string member in result.MemberNames)
            {
                attributed = true;
                failures.Add(string.Concat(configurationPath, ":", member, " ", message));
            }

            if (!attributed)
            {
                failures.Add(string.Concat(configurationPath, " ", message));
            }
        }
    }

    /// <summary>
    /// Appends failures for the page-resolution pair: an undeclared mode, and a row count that
    /// contradicts the mode in either direction.
    /// </summary>
    /// <param name="options">The bound column-expression group.</param>
    /// <param name="configurationPath">The group's configuration path, for the message.</param>
    /// <param name="failures">The failure list to append to.</param>
    /// <remarks>
    /// <para>
    /// EXPRESSED HERE RATHER THAN AS ATTRIBUTES BECAUSE NO SINGLE ATTRIBUTE CAN SAY "these two must
    /// agree". The <c>[Range]</c> annotation on the row count already bounds it on its own; what it cannot
    /// state is that the bound depends on a sibling's value.
    /// </para>
    /// <para>
    /// THE UNDECLARED-MODE CHECK IS NOT REDUNDANT WITH THE ENUM TYPE. Configuration binding will happily
    /// produce <c>(ExpressionPageResolution)7</c> from the string <c>"7"</c>, and the factory's
    /// unrecognised arm throws for it - which is the right backstop and the wrong FIRST failure, because
    /// an operator gets an exception from a resolver instead of a named configuration path. Checking here
    /// means a mistyped mode fails startup naming the setting.
    /// </para>
    /// <para>
    /// THE ROW COUNT IS CHECKED IN BOTH DIRECTIONS, and the second is the one that catches a real
    /// mistake: a positive count with a mode that ignores it is a stated pagination that would not be
    /// applied, so it is REFUSED rather than passed over. Silently ignoring it is how a page total becomes
    /// a grand total with nothing in the configuration to show why.
    /// </para>
    /// </remarks>
    private static void AppendPageResolutionFailures(
        ColumnExpressionOptions options,
        string configurationPath,
        List<string> failures)
    {
        if (!Enum.IsDefined(options.PageResolution))
        {
            failures.Add(string.Concat(
                configurationPath,
                ":PageResolution must be Unresolved, WholeBuffer or FixedRowsPerPage. It states the ",
                "pagination that `for page` aggregates are measured against - see ",
                "ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L27 for the only such expression in the ",
                "repository - and there is deliberately no fallback for an unrecognised value."));

            // No point testing the coupling against a mode that is not a mode.
            return;
        }

        if (options.PageResolution == ExpressionPageResolution.FixedRowsPerPage)
        {
            if (options.PageRowsPerPage <= 0)
            {
                failures.Add(string.Concat(
                    configurationPath,
                    ":PageRowsPerPage must be positive when PageResolution is FixedRowsPerPage: a page ",
                    "of zero or fewer rows describes no pagination at all."));
            }

            return;
        }

        if (options.PageRowsPerPage != 0)
        {
            failures.Add(string.Concat(
                configurationPath,
                ":PageRowsPerPage must be 0 unless PageResolution is FixedRowsPerPage. It is refused ",
                "rather than ignored because a stated page size that is not applied is how a page ",
                "total silently becomes a grand total, with nothing in the configuration to show why."));
        }
    }

    /// <summary>
    /// Appends a failure when a required upstream address is present but is not a usable absolute base
    /// address.
    /// </summary>
    /// <remarks>
    /// An empty value is passed over here rather than reported twice: the presence annotation on the
    /// property has already reported it, and two messages for one fault reads as two faults. Every
    /// other rule, and the reasoning behind each, lives in <see cref="ConfiguredAddress"/>, which the
    /// authentication validator applies to the same effect - one rule set, applied twice, rather than
    /// two copies able to drift.
    /// </remarks>
    private static void AppendAddressFailure(string value, string configurationPath, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string? fault = ConfiguredAddress.DescribeFault(value, configurationPath, allowQuery: false);
        if (fault is not null)
        {
            failures.Add(fault);
        }
    }

    /// <summary>
    /// Appends every failure for one typed client's failure-handling settings.
    /// </summary>
    /// <remarks>
    /// The annotations cover the retry count, the failure-proportion bounds and the minimum
    /// observation count. The three rules added here are the ones no annotation expresses: a
    /// proportion of zero is rejected because the underlying strategy rejects it, and each duration
    /// must be positive because a non-positive duration is not a shorter wait, it is a setting the
    /// pipeline cannot be built from.
    /// </remarks>
    private static void AppendClientResilienceFailures(
        ClientResilienceOptions? client,
        string configurationPath,
        List<string> failures)
    {
        if (!EnsureGroupBound(client, configurationPath, failures))
        {
            return;
        }

        AppendAnnotationFailures(client, configurationPath, failures);

        if (client.CircuitBreakerFailureRatio <= 0.0d)
        {
            failures.Add(string.Concat(
                configurationPath,
                ":CircuitBreakerFailureRatio must be greater than zero and at most one."));
        }

        AppendPositiveDurationFailure(
            client.RetryBaseDelay,
            string.Concat(configurationPath, ":RetryBaseDelay"),
            failures);
        AppendPositiveDurationFailure(
            client.CircuitBreakerSamplingDuration,
            string.Concat(configurationPath, ":CircuitBreakerSamplingDuration"),
            failures);
        AppendPositiveDurationFailure(
            client.CircuitBreakerBreakDuration,
            string.Concat(configurationPath, ":CircuitBreakerBreakDuration"),
            failures);
        AppendPositiveDurationFailure(
            client.RequestTimeout,
            string.Concat(configurationPath, ":RequestTimeout"),
            failures);
    }

    /// <summary>
    /// Appends every failure for one session kind's lifetime settings.
    /// </summary>
    /// <remarks>
    /// A zero or negative idle timeout would expire a session the instant it opened, and the
    /// annotation on the concurrency ceiling already rejects a value that would refuse every session.
    /// </remarks>
    private static void AppendSessionLifetimeFailures(
        SessionLifetimeOptions? session,
        string configurationPath,
        List<string> failures)
    {
        if (!EnsureGroupBound(session, configurationPath, failures))
        {
            return;
        }

        AppendAnnotationFailures(session, configurationPath, failures);
        AppendPositiveDurationFailure(
            session.IdleTimeout,
            string.Concat(configurationPath, ":IdleTimeout"),
            failures);
    }

    /// <summary>
    /// Appends a failure when a duration that must be positive is zero or negative.
    /// </summary>
    private static void AppendPositiveDurationFailure(TimeSpan value, string configurationPath, List<string> failures)
    {
        if (value <= TimeSpan.Zero)
        {
            failures.Add(string.Concat(
                configurationPath,
                " must be a positive duration. Supplied: \"",
                value.ToString(null, CultureInfo.InvariantCulture),
                "\"."));
        }
    }
}

/// <summary>
/// Validates a bound <see cref="JwtAuthenticationOptions"/> instance at startup.
/// </summary>
/// <remarks>
/// <para>
/// Registered by Program.cs alongside the binding of the <c>Authentication:Jwt</c> section, exactly as
/// <see cref="DataServicesOptionsValidator"/> is registered for the <c>DataServices</c> section:
/// <c>services.AddSingleton&lt;IValidateOptions&lt;JwtAuthenticationOptions&gt;,
/// JwtAuthenticationOptionsValidator&gt;()</c> together with <c>ValidateOnStart</c>. Because this type
/// runs the section's data annotations itself, the registration must NOT also call
/// <c>ValidateDataAnnotations</c>, or every presence failure would be reported twice and read as two
/// faults. One validator per bound section is the rule this file follows throughout.
/// </para>
/// <para>
/// WHY THIS TYPE EXISTS. An earlier form of this file asserted that the section's rules "are
/// expressible as annotations on its own members, so it needs no counterpart here". They are not. An
/// attribute can establish that <see cref="JwtAuthenticationOptions.Authority"/> is present; it cannot
/// establish that the value is an absolute address, that its scheme is one over which the framework's
/// bearer handler can retrieve discovery metadata, that
/// <see cref="JwtAuthenticationOptions.MetadataAddress"/> - which carries no annotation at all, being
/// optional - is usable when supplied, or that
/// <see cref="JwtAuthenticationOptions.RequireHttpsMetadata"/> does not directly contradict the scheme
/// of the endpoint it governs. Every one of those defects survives an annotation-only check and then
/// surfaces at metadata retrieval or on the first protected request, by which time the process has
/// reported itself started and is accepting traffic it cannot authenticate. Catching them at startup is
/// the same fail-fast posture the legacy framework had when a structural fault terminated the
/// application outright - its systemerror handler unpacks a seven-field assertion payload and then
/// executes <c>HALT CLOSE</c> [ws_objects/pfw.pbl.src/pfw.sra:L111-L144] - rather than degrading into a
/// service that answers requests it cannot verify.
/// </para>
/// <para>
/// THE TLS CONSISTENCY RULE IS THE ONE NO SINGLE ATTRIBUTE COULD EVER SEE, because it is a relationship
/// between two properties. Requiring transport-secured metadata while pointing at a plain-http
/// authority is a contradiction: the handler would discover it only on its first fetch. Note the
/// direction of the check. A plain-http endpoint with the requirement switched OFF is legitimate and is
/// deliberately allowed - that is the local topology, where every service address is plain http on a
/// private container network - so the rule fires only on the combination that cannot work.
/// </para>
/// <para>
/// VERIFICATION ONLY, AND NOTHING HERE CHANGES THAT. This type inspects addresses, an audience and four
/// boolean switches. It reads no key material, because the section carries none: Security is the sole
/// issuer in this system and this service holds verification material only. Nothing in this type may
/// ever validate, parse or normalise a signing value, because there is none here to validate.
/// </para>
/// <para>
/// ALL failures are collected and returned together rather than the first one being thrown, for the
/// same reason as the sibling validator: a deployment correcting its configuration should see every
/// fault in one startup attempt instead of discovering them one restart at a time.
/// </para>
/// </remarks>
public sealed class JwtAuthenticationOptionsValidator : IValidateOptions<JwtAuthenticationOptions>
{
    /// <summary>
    /// Validates one bound instance.
    /// </summary>
    /// <param name="name">
    /// The named options instance being validated, or <see langword="null"/> or empty for the default
    /// instance. Included in every message so a fault in a named instance is attributable.
    /// </param>
    /// <param name="options">The bound instance to validate.</param>
    /// <returns>
    /// <see cref="ValidateOptionsResult.Success"/> when no rule is broken, otherwise a failure result
    /// carrying one message per broken rule, each prefixed with the configuration path it belongs to.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public ValidateOptionsResult Validate(string? name, JwtAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];
        string prefix = string.IsNullOrEmpty(name)
            ? JwtAuthenticationOptions.SectionName
            : string.Concat(JwtAuthenticationOptions.SectionName, "[", name, "]");

        // --- The annotations first, so a missing value is reported once and by name ---------------
        ValidationContext context = new(options);
        List<ValidationResult> annotationResults = [];
        if (!Validator.TryValidateObject(options, context, annotationResults, validateAllProperties: true))
        {
            foreach (ValidationResult result in annotationResults)
            {
                string message = result.ErrorMessage ?? "is invalid.";
                bool attributed = false;

                foreach (string member in result.MemberNames)
                {
                    attributed = true;
                    failures.Add(string.Concat(prefix, ":", member, " ", message));
                }

                if (!attributed)
                {
                    failures.Add(string.Concat(prefix, " ", message));
                }
            }
        }

        // --- Authority: a usable absolute base address ---------------------------------------------
        // The discovery document and the published verification set are resolved BENEATH this address,
        // so it is a base address and is held to the base-address shape: no credentials, no query, no
        // fragment. A blank value is passed over because the annotation above has already named it.
        if (!string.IsNullOrWhiteSpace(options.Authority))
        {
            string authorityPath = string.Concat(prefix, ":", nameof(JwtAuthenticationOptions.Authority));
            string? authorityFault = ConfiguredAddress.DescribeFault(
                options.Authority, authorityPath, allowQuery: false);

            if (authorityFault is not null)
            {
                failures.Add(authorityFault);
            }
            else
            {
                AppendHttpsConsistencyFailure(options, options.Authority, authorityPath, failures);
            }
        }

        // --- MetadataAddress: optional, and only validated when supplied ---------------------------
        // Unset is the normal case and is correct whenever the authority is addressed directly, so an
        // absent value is not a fault. A PRESENT one must be usable, because the handler requires an
        // absolute address of it: an unusable value here is a fault the handler would only discover on
        // its first fetch, which is precisely what this validator exists to pre-empt. A query string is
        // permitted, unlike on the authority, because this is a complete document address rather than a
        // base onto which paths are composed, so a query on it is transmitted as written.
        if (options.MetadataAddress is not null)
        {
            string metadataPath = string.Concat(
                prefix, ":", nameof(JwtAuthenticationOptions.MetadataAddress));

            if (string.IsNullOrWhiteSpace(options.MetadataAddress))
            {
                // Present but blank, which is distinguished from absent deliberately. An unset property
                // means "derive the discovery address from the authority" and is the normal case; a
                // property written and then left empty means a configuration entry was started and not
                // finished, which is a mistake rather than a choice, and silently treating it as unset
                // would hide it.
                failures.Add(string.Concat(
                    metadataPath,
                    " was supplied but is blank. Remove the key entirely to derive the discovery ",
                    "address from the authority, rather than setting it to an empty value."));
            }
            else
            {
                string? metadataFault = ConfiguredAddress.DescribeFault(
                    options.MetadataAddress, metadataPath, allowQuery: true);

                if (metadataFault is not null)
                {
                    failures.Add(metadataFault);
                }
                else
                {
                    AppendHttpsConsistencyFailure(
                        options, options.MetadataAddress, metadataPath, failures);
                }
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Appends a failure when transport-secured metadata is required but the endpoint that would be
    /// retrieved is not an https address.
    /// </summary>
    /// <remarks>
    /// Only ever called with an address that has already been proven parseable and http or https, so the
    /// parse below cannot fail for a reason this method would have to report. The one-directional rule
    /// is deliberate and is explained on the type: a plain-http endpoint with the requirement switched
    /// off is the supported local topology, so only the contradictory combination is rejected.
    /// </remarks>
    private static void AppendHttpsConsistencyFailure(
        JwtAuthenticationOptions options,
        string address,
        string configurationPath,
        List<string> failures)
    {
        if (!options.RequireHttpsMetadata)
        {
            return;
        }

        if (Uri.TryCreate(address, UriKind.Absolute, out Uri? parsed)
            && !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            failures.Add(string.Concat(
                configurationPath,
                " is not an https address, but ",
                nameof(JwtAuthenticationOptions.RequireHttpsMetadata),
                " is true, so discovery metadata could never be retrieved from it. Either publish the ",
                "endpoint over https, or set ",
                nameof(JwtAuthenticationOptions.RequireHttpsMetadata),
                " to false for a plain-http topology - and set it in the development settings file ",
                "rather than in the base settings, so the relaxation reaches only the environment that ",
                "asked for it."));
        }
    }
}

/// <summary>
/// Shared shape checking for every configured address in this file.
/// </summary>
/// <remarks>
/// <para>
/// Internal, static and deliberately small. It exists so that the upstream service addresses and the
/// authentication endpoints are held to ONE rule set rather than to two copies able to drift, and it
/// lives in this file because the scope of this folder is one file. It adds no public surface and holds
/// no state. Its counterpart in the Gateway service applies the identical rules to that service's own
/// addresses, so an operator sees the same diagnosis from either service.
/// </para>
/// <para>
/// PARSEABILITY ALONE IS A WEAKER RULE THAN IT LOOKS, and the scheme check is not decoration. On the
/// target platform, which is Linux containers, a rooted filesystem path such as <c>/v1/persistence</c>
/// parses successfully AS AN ABSOLUTE URI, because it is a well-formed local-file address. Measured
/// directly on this toolchain rather than assumed. A value like that would pass a parse-only rule at
/// startup and then fail when a channel or client was constructed - the late, hard-to-attribute failure
/// that startup validation exists to prevent. Constraining the parsed scheme closes that gap and
/// excludes nothing a deployment could legitimately want: the Persistence contract is gRPC over HTTP/2,
/// the Security contract is REST over HTTP, and the discovery endpoints are plain HTTP, so none of them
/// has any other reachable scheme.
/// </para>
/// <para>
/// THREE FURTHER COMPONENTS ARE REJECTED, each of which parses successfully and each of which would
/// otherwise fail late and confusingly:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="Uri.UserInfo"/> - credentials embedded in the address, as in
/// <c>http://user:secret@host:5101</c>. Rejecting it is a secrets control rather than tidiness. Such a
/// value would be a credential living under a configuration key named for an address; it would be
/// copied into every log line, exception and trace that records the request URI; and this system has no
/// use for it, because every internal edge is authenticated with a bearer token minted by the Security
/// service. No address is ever a place to put a secret (constraints C-F and C-G).
/// </description></item>
/// <item><description>
/// A query string, on a BASE address only. A base address is composed with per-request paths, and a
/// query on the base is dropped rather than merged, so a caller believing it had configured one would be
/// wrong with no diagnostic at all. A complete document address - a discovery metadata address - is
/// exempt, because a query on it is transmitted as written.
/// </description></item>
/// <item><description>
/// A fragment, always. Fragments are never transmitted, so one in configuration can only be a mistake.
/// </description></item>
/// </list>
/// <para>
/// NO MESSAGE EVER ECHOES THE CONFIGURED VALUE. The configuration path is what an operator needs in
/// order to find the offending setting; the value adds nothing, and for the userinfo case it would put a
/// credential-bearing address into the startup log - reintroducing through the error message the very
/// leak the rule exists to prevent. A validator cannot know which of its inputs is sensitive, so none is
/// quoted. The scheme rule quotes the parsed scheme alone, which is a fixed token from a small set and
/// carries nothing.
/// </para>
/// </remarks>
internal static class ConfiguredAddress
{
    /// <summary>
    /// Describes why a configured address is unusable, or returns <see langword="null"/> when it is
    /// usable.
    /// </summary>
    /// <param name="value">
    /// The configured value. Callers screen out null, empty and whitespace beforehand, so that a missing
    /// value is reported once by its presence rule rather than twice.
    /// </param>
    /// <param name="configurationPath">
    /// The full configuration path, quoted into the message so an operator can find the offending key
    /// without consulting source.
    /// </param>
    /// <param name="allowQuery">
    /// <see langword="true"/> for a complete document address, which may carry a query;
    /// <see langword="false"/> for a base address, which may not.
    /// </param>
    /// <returns>The message describing the fault, or <see langword="null"/> when there is none.</returns>
    internal static string? DescribeFault(string value, string configurationPath, bool allowQuery)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed))
        {
            return string.Concat(
                configurationPath,
                " must be an absolute address, for example a scheme, host and port. The configured ",
                "value is deliberately not quoted here, because a rejected address may carry a ",
                "credential.");
        }

        // Uri.Scheme is already lower-cased by the parser, so an ordinal comparison is both correct and
        // free of any culture dependency.
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return string.Concat(
                configurationPath,
                " must use the http or https scheme, which are the only two this service can reach. ",
                "Supplied scheme: \"",
                parsed.Scheme,
                "\".");
        }

        if (parsed.UserInfo.Length > 0)
        {
            return string.Concat(
                configurationPath,
                " must not embed credentials in the address. Remove the \"user:password@\" portion: ",
                "every edge in this system is authenticated with a bearer token issued by the Security ",
                "service, and an address carrying credentials would leak them into logs and traces.");
        }

        if (!allowQuery && !string.IsNullOrEmpty(parsed.Query))
        {
            return string.Concat(
                configurationPath,
                " is a base address and must not carry a query string. A query on a base address is ",
                "dropped rather than merged when a per-request path is composed onto it, so it would ",
                "have no effect and no diagnostic.");
        }

        if (!string.IsNullOrEmpty(parsed.Fragment))
        {
            return string.Concat(
                configurationPath,
                " must not carry a fragment. A fragment is never sent to a server, so one here can ",
                "only be a mistake.");
        }

        return null;
    }
}
