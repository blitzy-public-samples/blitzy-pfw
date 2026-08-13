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
//   * NO credential VALUE of any kind. Not as a default, not as an example, not in a comment
//     (constraint C-F). Exactly one property below carries a credential at run time -
//     SecurityClientOptions.ClientSecret, the password half of the Basic identity this service
//     presents to obtain its first service token - and it is empty in source, has no key in the
//     sibling appsettings.json, and cannot be given one there: it arrives through the flat
//     configuration key named by SecurityClientOptions.ClientSecretConfigurationKey, which the
//     environment-variable provider cannot map into a section. So the declaration is here and the
//     material is not, which is the only arrangement that lets the value be required and still
//     leave nothing to leak in a committed file.
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
//  LOCATOR CONVENTION: every bare `pfw.sra:L...` in this file means ws_objects/pfw.pbl.src/pfw.sra,
//  the framework application - never the same-named packager object at
//  ws_objects/pfw.pack.pbl.src/pfw.sra (AAP 0.8.6 R7).
// ==============================================================================================

using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
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
    /// Bounds on the thin REST projection Gateway consumes.
    /// </summary>
    public RestProjectionOptions RestProjection { get; set; } = new();

    /// <summary>
    /// The transaction session this service opens against Persistence for a retrieval or an update.
    /// </summary>
    public PersistenceSessionOptions PersistenceSession { get; set; } = new();

    /// <summary>
    /// How the event chain treats out-of-order delivery. Created by the decomposition; no legacy
    /// equivalent.
    /// </summary>
    public EventChainOptions EventChain { get; set; } = new();

    /// <summary>
    /// The trust anchor this service verifies its two upstreams' server certificates against. Bound
    /// from <c>DataServices:InternalTls</c>. Created by the decomposition; no legacy equivalent.
    /// </summary>
    /// <remarks>
    /// WHY IT IS REQUIRED AND NOT A HARDENING PREFERENCE. Persistence and Security both terminate TLS
    /// with certificates issued by the local authority <c>docs/ARCHITECTURE.md</c> §9.3.1 generates,
    /// and that authority is in no container's operating-system trust store. Left on platform default
    /// trust every outbound channel this service opens - the four Persistence gRPC channels, the
    /// Security token and crypto channel, and the bearer handler's key-set backchannel - refuses the
    /// certificate the documented topology presents, and the service cannot make a single authenticated
    /// call. Setting the anchor NARROWS trust to it; nothing here relaxes validation.
    /// </remarks>
    public InternalTlsTrustOptions InternalTls { get; set; } = new();
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

    /// <summary>
    /// The backstop on one macro invocation over the inverted channel. Must be positive. Defaults to
    /// <see cref="SessionLifetimeOptions.DefaultIdleTimeout"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A BACKSTOP RATHER THAN A BUDGET, AND THE DISTINCTION IS THE WHOLE JUSTIFICATION FOR IT EXISTING.
    /// The ordinary bound on a macro invocation is the calculating call's own cancellation, which is
    /// real for any caller that attaches a gRPC deadline: an abandoned calculation is cancelled and the
    /// invocation with it. What that does not cover is a caller that attaches no deadline at all. This
    /// service cannot require one, and against such a caller an unserviced macro channel would hold the
    /// session, its engines and the calculating call open indefinitely.
    /// </para>
    /// <para>
    /// DERIVED, NOT CHOSEN. The default is the expression session's own idle lifetime, because an
    /// invocation still outstanding past the point at which its session would have been reclaimed is
    /// holding something the service's own policy had already given up on. It is deliberately an order
    /// of magnitude away from any plausible invocation, so a properly deadlined client never reaches it
    /// - which matters because an elapsed backstop produces a DEFINED timeout outcome rather than a
    /// value, and a value fabricated for a macro is the substitution the whole channel exists to avoid.
    /// </para>
    /// <para>
    /// SEPARATE FROM THE SESSION SETTING RATHER THAN READ FROM IT, so that shortening one does not
    /// silently shorten the other. They start equal; a deployment that needs to move one moves one.
    /// </para>
    /// </remarks>
    public TimeSpan MacroInvocationTimeout { get; set; } = SessionLifetimeOptions.DefaultIdleTimeout;
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

    // ==========================================================================================
    //  NO Transaction PROPERTY LIVES HERE, AND ITS ABSENCE IS DELIBERATE RATHER THAN AN OVERSIGHT.
    //
    //  A `PersistenceTransactionOptions Transaction` group bound from
    //  `DataServices:Persistence:Transaction` - carrying a database name, a DBMS token, a server
    //  name, a DBParm string and an auto-commit flag - is the shape this class attracts, AND NOTHING
    //  WOULD READ IT. The descriptor every session is actually begun with comes from
    //  `DataServices:PersistenceSession`, which Grpc/DataWindowService.BuildSessionRequest composes -
    //  and the two groups disagree on their defaults, the second one defaulting the database to
    //  COMPANY and auto-commit to true where the live one defaults the database to empty and
    //  auto-commit to false.
    //
    //  A DEAD CONFIGURATION GROUP IS WORSE THAN A MISSING ONE, which is why none is declared here
    //  even annotated: it appears in a documented section name, an operator setting it observes no
    //  effect whatsoever, and the value most likely to be set - auto-commit, whose two readings have
    //  opposite consequences for a partially applied multi-row update - is the one that would look
    //  most like it had taken. Exactly one operator-facing authority exists for the session
    //  descriptor, and it is the one the code reads.
    // ==========================================================================================
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
    /// The client certificate this service presents on the token-issuance edge wherever a deployment
    /// terminates TLS at Security. Bound from <c>DataServices:Security:MutualTls</c>. Paths only, never
    /// material. The alternative, and the one the documented topology uses, is
    /// <see cref="ClientSecret"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CREATED BY THE DECOMPOSITION, and required for a functional reason rather than as hardening.
    /// <c>POST /v1/tokens</c> is the one operation a bearer token cannot protect, because <b>a caller
    /// cannot present a bearer token in order to obtain its first bearer token</b>. DataServices is one
    /// of the two services that request tokens - it needs one for its own C-02 cryptographic calls and
    /// one for the audience beneath it - so a deployment in which it can present NEITHER accepted
    /// credential obtains none, and every authenticated call it would make is unreachable. That is why
    /// <see cref="DataServicesOptions"/>'s validator refuses to start such a deployment rather than
    /// letting the readiness probe report healthy over a service that cannot call anything.
    /// </para>
    /// <para>
    /// THIS GROUP IS THE SECOND OF THE TWO SCHEMES THE CONTRACT PUBLISHES, NOT THE ONLY ONE. C-01
    /// accepts <c>clientCredential</c> - an HTTP <c>Basic</c> credential naming a subject on Security's
    /// issuance roster - OR <c>mutualTls</c>. A client certificate exists only inside a TLS handshake
    /// and TLS may be terminated ahead of Security by a proxy or sidecar that strips it, so
    /// this group cannot be the credential and <see cref="ClientSecret"/> is. It is retained, bound and
    /// genuinely consumed - the composition root attaches it to the typed client's primary handler - so
    /// the published alternative is reachable rather than declared, which is what makes it an
    /// alternative instead of dead configuration.
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

    /// <summary>
    /// The FLAT configuration key that carries this service's issuance secret:
    /// <c>SECURITY_CLIENT_SECRET_DATASERVICES</c>. This is a NAME, not a value; no secret appears in
    /// this file, in any settings file, or in any container definition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY CONVENTIONAL SECTION BINDING CANNOT WORK, and why this is a constant rather than a literal
    /// written at the point of use. The environment-variable configuration provider translates a
    /// double underscore, and only a double underscore, into the <c>:</c> section separator. This name
    /// contains none, so it is a top-level configuration key of exactly this literal spelling rather
    /// than a path into the <c>DataServices</c> section, and binding that section - however it is
    /// bound - will never populate <see cref="ClientSecret"/>. The resolution is an explicit
    /// post-configure step in the service entry point that reads
    /// <c>configuration[SecurityClientOptions.ClientSecretConfigurationKey]</c> onto the bound
    /// instance, which is the same mechanism Security uses for its own signing key and for every
    /// roster secret it resolves. The constant is public so that the entry point and the tests which
    /// drive configuration read one spelling from one place.
    /// </para>
    /// <para>
    /// THE NAME IS FIXED BY SECURITY'S ISSUANCE ROSTER AND MUST MATCH IT EXACTLY. Security's
    /// <c>Security:Clients</c> entry for the subject <c>powerframework-dataservices</c> names this same
    /// key as the source of the secret it will compare against, and <c>orchestration/.env.example</c>
    /// declares it once for both sides. Two spellings would be two ways for one deployment to be half
    /// configured, and the failure would present as an authentication refusal rather than as the
    /// configuration mismatch it is. No <c>DataServices__Security__ClientSecret</c> alias may be added.
    /// </para>
    /// </remarks>
    public const string ClientSecretConfigurationKey = "SECURITY_CLIENT_SECRET_DATASERVICES";

    /// <summary>
    /// The shared secret this service presents as the password half of its <c>Basic</c> issuance
    /// credential. Reaches the process through <see cref="ClientSecretConfigurationKey"/> and never
    /// through a settings file. Never defaulted, never logged, never echoed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE USER-ID HALF IS NOT CONFIGURABLE, AND THAT IS DELIBERATE. It is the subject
    /// <c>Clients/SecurityClient.cs</c> already declares for every token it requests, and Security
    /// refuses a request whose claimed subject disagrees with the identity its credential establishes.
    /// A separate setting for the identity would therefore add one value whose only legal content is
    /// what the client already sends, and one more way for a deployment to be refused <c>403</c> for a
    /// reason that reads like an authorization fault rather than a typo.
    /// </para>
    /// <para>
    /// EMPTY IS LEGAL ONLY WHEN <see cref="MutualTls"/> IS CONFIGURED. The two are alternatives, so a
    /// deployment supplies one or the other; supplying neither leaves this service unable to obtain any
    /// credential at all, and <see cref="DataServicesOptions"/>'s validator refuses it at startup with
    /// a named error rather than letting the fault surface on the first cryptographic call. Supplying
    /// both is legal and is not a conflict: the <c>Basic</c> credential is what travels on the request,
    /// and the certificate is available to whatever handshake the transport performs.
    /// </para>
    /// <para>
    /// NOT AN <c>[Required]</c> MEMBER, because the requirement is conditional on the other scheme and
    /// no single attribute can express "this one or that one". The rule lives in the validator, where
    /// it can name both keys in one message.
    /// </para>
    /// </remarks>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Whether this deployment can present any accepted issuance credential at all.
    /// </summary>
    /// <remarks>
    /// True when either scheme is configured. Read by the validator, and by the client when it decides
    /// which credential to attach, so the two cannot disagree about what "configured" means.
    /// </remarks>
    public bool HasIssuanceCredential =>
        !string.IsNullOrWhiteSpace(ClientSecret) || MutualTls.IsConfigured;
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

/// <summary>
/// The trust anchor internal TLS is verified against. Bound from <c>DataServices:InternalTls</c>. One
/// path, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// A ROOT CERTIFICATE IS PUBLIC MATERIAL, SO THE PATH IS NOT ABOUT CONFIDENTIALITY. It is that the
/// anchor is a DEPLOYMENT artefact - one local authority per environment, rotated on its own schedule,
/// mounted read-only from the orchestration layer. Embedding one in a settings file would pin every
/// environment to a single authority and make rotation a code change.
/// </para>
/// <para>
/// ONE MEMBER, NOT TWO. Unlike <see cref="MutualTlsClientOptions"/> there is no key path, because
/// verifying a chain needs only the public root. A trust anchor with a private key beside it would mean
/// this service could ISSUE certificates for the internal topology, which is a capability it must not
/// have.
/// </para>
/// </remarks>
public sealed class InternalTlsTrustOptions
{
    /// <summary>
    /// Path to the PEM-encoded certificate authority bundle internal server certificates are verified
    /// against. Empty means platform default trust.
    /// </summary>
    /// <remarks>
    /// The file may carry one certificate or a concatenated chain of them; every certificate it carries
    /// becomes an acceptable root for internal traffic, and nothing else does.
    /// </remarks>
    public string TrustedCaPath { get; set; } = string.Empty;

    /// <summary>
    /// Whether this deployment narrows internal trust to a mounted anchor.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(TrustedCaPath);
    /// <summary>
    /// How the revocation status of an internal peer's certificate is checked. One of
    /// <see cref="RevocationModes.NoCheck"/>, <see cref="RevocationModes.Offline"/> or
    /// <see cref="RevocationModes.Online"/>, compared case-insensitively.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>CONFIGURABLE, AND THE SHIPPED DEFAULT IS THE ONLY VALUE THE DOCUMENTED TOPOLOGY CAN ANSWER.</b>
    /// This was a hardcoded <c>NoCheck</c>, which meant a deployment whose authority DOES publish revocation
    /// information had no way to ask for it and a stolen peer certificate stayed acceptable until it expired
    /// (CWE-295). It is a setting now. What it is NOT is a setting whose default can be the strict value:
    /// the local authority the documented recipe generates publishes no distribution point and runs no
    /// responder, and that was MEASURED rather than assumed - building a chain for a leaf it issued under
    /// <c>CustomRootTrust</c> succeeds under <c>NoCheck</c> and FAILS under both <c>Offline</c> and
    /// <c>Online</c> with <c>RevocationStatusUnknown | OfflineRevocation</c>. Shipping a strict default
    /// would therefore refuse every internal peer on a clean bring-up and hold every dependent behind an
    /// unsatisfiable health gate.
    /// </para>
    /// <para>
    /// AN INDETERMINATE STATUS IS A REFUSAL UNDER THE STRICTER MODES, NEVER A PASS. The chain policy sets no
    /// verification flag that ignores a revocation failure, so a deployment that selects <c>Offline</c> or
    /// <c>Online</c> gets a genuine check whose unknown answer refuses the peer - which is the only reading
    /// under which selecting the mode means anything at all.
    /// </para>
    /// <para>
    /// WHAT SUBSTITUTES FOR REVOCATION WHILE THIS IS <c>NoCheck</c> IS CERTIFICATE LIFETIME, and the
    /// documented issuance recipe is <c>-days 30</c> for both the authority and every leaf. The operational
    /// surfaces state the production recommendation - issue from an authority that publishes a distribution
    /// point or a responder and set this to <c>Online</c> - and the emergency procedure for a compromise
    /// under <c>NoCheck</c>, which is to replace the anchor and restart rather than to revoke.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string RevocationMode { get; set; } = RevocationModes.NoCheck;

    /// <summary>
    /// Resolves <see cref="RevocationMode"/> to the platform value the chain policy is built with.
    /// </summary>
    /// <param name="configurationKeyPrefix">
    /// The configuration path of this group, quoted into the failure so an operator can find the offending
    /// key without reading source.
    /// </param>
    /// <returns>The resolved mode.</returns>
    /// <exception cref="InvalidOperationException">
    /// The configured value names no recognised mode. Structural, and therefore fatal: guessing a mode
    /// would either silently weaken the check a deployment asked for or silently refuse every peer, and
    /// both are worse than not starting. The value IS quoted, because a mode name is not a secret and an
    /// operator fixing a typo needs to see what was read.
    /// </exception>
    internal X509RevocationMode ResolveRevocationMode(string configurationKeyPrefix)
    {
        string configured = RevocationMode?.Trim() ?? string.Empty;

        if (RevocationModes.Matches(configured, RevocationModes.NoCheck))
        {
            return X509RevocationMode.NoCheck;
        }

        if (RevocationModes.Matches(configured, RevocationModes.Offline))
        {
            return X509RevocationMode.Offline;
        }

        if (RevocationModes.Matches(configured, RevocationModes.Online))
        {
            return X509RevocationMode.Online;
        }

        throw new InvalidOperationException(
            $"'{configurationKeyPrefix}:{nameof(RevocationMode)}' is set to '{RevocationMode}', which "
                + "names no recognised revocation posture, so this service will not start. Set one of "
                + $"{string.Join(", ", RevocationModes.Recognised)}, or remove the key to accept the "
                + "default. Note that the stricter two require an authority that publishes a certificate "
                + "revocation list or runs a responder: against one that does not, every peer is refused "
                + "with an indeterminate revocation status, which is a refusal by design.");
    }

    /// <summary>The revocation postures this group accepts.</summary>
    /// <remarks>
    /// Declared here rather than as loose strings so that the settings file, the validator, the resolver
    /// and the failure message cannot spell them three different ways.
    /// </remarks>
    public static class RevocationModes
    {
        /// <summary>No revocation check is performed. The shipped default.</summary>
        public const string NoCheck = "NoCheck";

        /// <summary>Only cached revocation information is consulted.</summary>
        public const string Offline = "Offline";

        /// <summary>Revocation information is fetched from the authority.</summary>
        public const string Online = "Online";

        /// <summary>Every accepted spelling, in the order a failure message lists them.</summary>
        public static IReadOnlyList<string> Recognised { get; } = [NoCheck, Offline, Online];

        /// <summary>Reports whether a configured value is recognised.</summary>
        /// <param name="candidate">The configured value, which may be <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when it names a mode.</returns>
        public static bool IsRecognised(string? candidate) =>
            candidate is not null
                && Recognised.Any(mode => Matches(candidate.Trim(), mode));

        /// <summary>Compares a configured value against one mode, case-insensitively.</summary>
        /// <param name="candidate">The configured value.</param>
        /// <param name="mode">The mode to compare against.</param>
        /// <returns><see langword="true"/> when they name the same mode.</returns>
        internal static bool Matches(string candidate, string mode) =>
            string.Equals(candidate, mode, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// Describes the one way this group can be wrong: present but blank.
    /// </summary>
    /// <param name="configurationKeyPrefix">
    /// The configuration path of this group, quoted into the message so an operator can find the
    /// offending key without reading source.
    /// </param>
    /// <returns>
    /// One message when the value is whitespace rather than either a path or empty, otherwise an empty
    /// sequence.
    /// </returns>
    /// <remarks>
    /// Whether the file exists and parses is decided when it is loaded at startup, so that the failure
    /// carries the loader's own diagnosis rather than a second, weaker copy of it. The value is not
    /// echoed: a container's secret mount layout is not something a startup record should publish.
    /// </remarks>
    internal IEnumerable<string> DescribeFailures(string configurationKeyPrefix)
    {
        if (TrustedCaPath.Length == 0 || !string.IsNullOrWhiteSpace(TrustedCaPath))
        {
            yield break;
        }

        yield return string.Concat(
            configurationKeyPrefix,
            ":",
            nameof(TrustedCaPath),
            " is set to whitespace, which is neither a path nor the empty value that means platform "
                + "default trust. Set a path to the PEM certificate authority bundle internal server "
                + "certificates are issued by, or remove the key entirely. The value is not quoted "
                + "here, because a startup record must not publish a container's secret mount layout.");
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
    /// How many times a failed attempt is retried. Zero disables retrying; the ceiling is
    /// <see cref="MaxRetryAttemptsCeiling"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zero is a legal value: a deployment that wants a single attempt and an immediate surfaced failure
    /// is expressing a policy, not a misconfiguration.
    /// </para>
    /// <para>
    /// 🔴 <b>AND IT NOW BEHAVES AS ONE, WHICH IT DID NOT BEFORE.</b> Both retry layers consumed the
    /// value unconditionally and NEITHER accepts zero. The resilience package declares its retry
    /// strategy's <c>MaxRetryAttempts</c> in the range one to <see cref="int.MaxValue"/>, so a configured
    /// zero made the SERVICE FAIL TO START - "The field &lt;client&gt;-standard.Retry.MaxRetryAttempts
    /// must be between 1 and 2147483647", observed on the sibling Gateway edge, whose wiring is identical
    /// - and the gRPC layer's <c>MaxAttempts = retries + 1</c> became one, which its own retry policy
    /// rejects. The composition root now BRANCHES on zero: no gRPC service configuration and no channel
    /// ceiling at all, and the HTTP-layer strategy takes the never-retry predicate with
    /// <see cref="DisabledRetryPlaceholderAttempts"/> as the count the package's range requires.
    /// </para>
    /// <para>
    /// <b>THE CEILING REPLACES <see cref="int.MaxValue"/> BECAUSE THE UNBOUNDED FORM SILENTLY BROKE
    /// RETRY.</b> The gRPC layer increments the value to an attempt count, and unchecked
    /// <c>int.MaxValue + 1</c> wraps to <see cref="int.MinValue"/> - a negative count that the channel and
    /// its retry policy both ACCEPTED, leaving retry mis-configured on a service that started and reported
    /// itself healthy (observed). Ten is production-safe rather than arbitrary: the total request budget
    /// bounds how many attempts can occur at all, since the shipped two-second base delay growing
    /// exponentially means a fourth retry already cannot fit a thirty-second budget - so a larger value
    /// expresses a mistake, not a policy. NO PERFORMANCE CLAIM IS MADE OR IMPLIED (AAP 0.8.5).
    /// </para>
    /// </remarks>
    [Range(0, MaxRetryAttemptsCeiling)]
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>The largest accepted value of <see cref="MaxRetryAttempts"/>.</summary>
    /// <remarks>
    /// A NAMED CONSTANT BECAUSE THREE PLACES MUST AGREE ON IT: the range annotation that refuses a larger
    /// value, the documentation an operator reads, and the boundary tests.
    /// </remarks>
    public const int MaxRetryAttemptsCeiling = 10;

    /// <summary>
    /// The retry count assigned to the HTTP-layer strategy when retrying is DISABLED.
    /// </summary>
    /// <remarks>
    /// A PLACEHOLDER, AND THE PREDICATE IS WHAT ACTUALLY DISABLES. The resilience package's range forbids
    /// zero, so a disabled pipeline still has to name a legal count; it names the smallest one, and its
    /// <c>ShouldHandle</c> answers false for everything, so no attempt is ever repeated. Naming the
    /// constant is what stops a reader from concluding that one retry survives the disable.
    /// </remarks>
    public const int DisabledRetryPlaceholderAttempts = 1;

    /// <summary>Whether the configuration asks for any retrying at all.</summary>
    public bool RetriesEnabled => MaxRetryAttempts > 0;

    /// <summary>
    /// The inclusive ATTEMPT count the gRPC retry policy takes, which is one more than the retry count.
    /// </summary>
    /// <returns>The attempt count.</returns>
    /// <exception cref="OverflowException">
    /// The retry count is <see cref="int.MaxValue"/>. Unreachable through validated configuration - the
    /// range annotation refuses it long before - and CHECKED anyway, because the unchecked form's failure
    /// mode was a silently negative attempt count that every layer accepted.
    /// </exception>
    public int ResolveGrpcAttemptCount() => checked(MaxRetryAttempts + 1);

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
    /// <para>
    /// This is a failure-handling bound, not a target: it decides when an unanswered call is
    /// treated as failed rather than waited on indefinitely, which is a decision the legacy never
    /// had to make because an in-process call always returned.
    /// </para>
    /// <para>
    /// IT IS ALSO THE gRPC DEADLINE ON EVERY UNARY CALL OF THIS EDGE, and there is only one setting
    /// because there must only be one number. A gRPC deadline is enforced by the client as a TOTAL
    /// bound across the retries the resilience pipeline performs beneath it, so a deadline shorter
    /// than the pipeline's budget would cancel the call while the pipeline was still retrying, and a
    /// longer one would leave the upstream working after the caller had stopped waiting. Deriving
    /// both from this value makes disagreement impossible rather than merely unlikely.
    /// </para>
    /// </remarks>
    public TimeSpan RequestTimeout { get; set; } = DefaultRequestTimeout;

    /// <summary>
    /// How long ONE attempt of a unary upstream call may take, including establishing the connection.
    /// Unset means "the same as <see cref="RequestTimeout"/>".
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 LEAVING THE PER-ATTEMPT BOUND TO BE INHERITED SILENTLY MAKES IT THE BOUND THAT APPLIES.
    /// Configuring only <see cref="RequestTimeout"/>, onto the pipeline's TOTAL timeout, leaves the
    /// pipeline's per-attempt timeout at its package default of ten seconds. Retry here is
    /// operation-scoped, so for every operation that creates, mutates or advances upstream state the
    /// total budget is never reached and the inherited ten seconds decides when the call gives up - a
    /// different number from the one this service documents.
    /// </para>
    /// <para>
    /// DEFAULTING TO <see cref="RequestTimeout"/> so the documented bound is the observed bound with no
    /// configuration, and so lowering <see cref="RequestTimeout"/> cannot leave a stale per-attempt value
    /// above it. Set it explicitly only to make one attempt tighter than the total, which is what leaves
    /// room inside the budget for a retry.
    /// </para>
    /// <para>
    /// NO PERFORMANCE CLAIM IS MADE OR IMPLIED (AAP 0.8.5): this bounds how long to wait before reporting
    /// a failure, and is not a latency target.
    /// </para>
    /// </remarks>
    public TimeSpan? AttemptTimeout { get; set; }

    /// <summary>
    /// The per-attempt bound to apply, given the total budget and the breaker's sampling window.
    /// </summary>
    /// <param name="samplingDuration">The circuit breaker's sampling window on the same pipeline.</param>
    /// <returns>The explicit override when set, otherwise the largest value the constraints permit.</returns>
    /// <remarks>
    /// <para>
    /// TWO CONSTRAINTS BOUND THIS, AND THE SECOND IS NOT OBVIOUS. The per-attempt timeout must not exceed
    /// the total budget that contains it, and the resilience package ADDITIONALLY requires the circuit
    /// breaker's sampling duration to be AT LEAST DOUBLE the per-attempt timeout - otherwise the breaker
    /// cannot observe enough attempts inside one window to judge a failure ratio, and the package refuses
    /// the configuration at startup rather than running an ineffective breaker.
    /// </para>
    /// <para>
    /// So the derived default is the LARGEST value both constraints permit, which with the shipped
    /// settings is half the sampling window rather than the whole total budget. That is stated here
    /// because it means the per-attempt bound is NOT simply the documented request timeout, and a reader
    /// who assumed it was would be wrong in the same way the inherited ten-second default made everyone
    /// wrong before.
    /// </para>
    /// <para>
    /// AN EXPLICIT VALUE IS NOT CLAMPED. Silently narrowing an operator's stated intent would repeat the
    /// original fault in a new place; a value that breaks the halving rule is refused at startup by the
    /// package's own validator, which names both durations in its message.
    /// </para>
    /// </remarks>
    internal TimeSpan ResolveAttemptTimeout(TimeSpan samplingDuration)
    {
        TimeSpan half = samplingDuration / 2;

        return AttemptTimeout ?? (RequestTimeout < half ? RequestTimeout : half);
    }

    /// <summary>
    /// The ceiling on one STREAMING call, after which it is abandoned. Must be positive. Defaults to
    /// <see cref="DefaultStreamDeadline"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A SEPARATE BOUND BECAUSE A STREAM IS A DIFFERENT QUANTITY. A retrieval that runs for minutes is
    /// correct behaviour, while a unary call that does so is not, so bounding both by
    /// <see cref="RequestTimeout"/> would abandon legitimate retrievals mid-flight.
    /// </para>
    /// <para>
    /// DERIVED FROM THE UPSTREAM RATHER THAN CHOSEN. Every stream on this edge reads from a
    /// server-held Persistence task, and Persistence reclaims an idle handle after
    /// <c>Persistence:Handles:IdleExpirySeconds</c>, whose shipped value is nine hundred seconds. A
    /// stream still open past that point is holding a handle the upstream's own policy would already
    /// have released had the stream not been the thing refreshing its activity stamp, so this is the
    /// bound at which continuing to wait stops being meaningful.
    /// </para>
    /// <para>
    /// PRESENT ON BOTH CONFIGURED GROUPS AND MEANINGFUL ON ONE. The Security edge is REST and carries
    /// no streaming call, so its value bounds nothing; the two groups are deliberately the same type
    /// so that the projection onto a handler is written once and they cannot diverge in shape, which
    /// is the same reason recorded on <c>Program.ApplyResilience</c>.
    /// </para>
    /// </remarks>
    public TimeSpan StreamDeadline { get; set; } = DefaultStreamDeadline;

    /// <summary>
    /// The shipped total bound on a unary call.
    /// </summary>
    /// <remarks>
    /// Named so that <c>Clients/OutboundCallPolicy.OutboundDeadlines.Default</c> can carry the same
    /// value: a client constructed without a configured pair still bounds its calls, and the shipped
    /// posture is stated in exactly one place.
    /// </remarks>
    public static TimeSpan DefaultRequestTimeout => TimeSpan.FromSeconds(30);

    /// <summary>
    /// The shipped total bound on a streaming call, matching Persistence's own handle idle expiry.
    /// </summary>
    public static TimeSpan DefaultStreamDeadline => TimeSpan.FromSeconds(900);
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
    public TimeSpan IdleTimeout { get; set; } = DefaultIdleTimeout;

    /// <summary>
    /// The shipped idle lifetime of a server-held session.
    /// </summary>
    /// <remarks>
    /// Named because it is the DERIVATION of a second setting rather than only a default of this one:
    /// <see cref="ColumnExpressionOptions.MacroInvocationTimeout"/> is the point past which an
    /// outstanding macro invocation is holding a session the service's own policy would already have
    /// released, so the two values start equal and a reader can see why.
    /// </remarks>
    public static TimeSpan DefaultIdleTimeout => TimeSpan.FromMinutes(5);

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

    /// <summary>
    /// The backstop on one outbound semantic question awaiting the client's answer. Must be positive.
    /// Defaults to <see cref="SessionLifetimeOptions.DefaultIdleTimeout"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>CREATED BY THE DECOMPOSITION, AND WITHOUT IT A SILENT CLIENT HELD THE STREAM FOR EVER.</b>
    /// Nine of the 22 events are semantic invocations the chain makes BACK, and FIVE of those are
    /// answer-bearing, so five await a reply. Under the synchronous discipline the dispatch that raised one
    /// blocks until it is answered - which is the oracle's own shape, because in process the handler simply
    /// returned. Across a wire the answer may never come: a
    /// client that reads the outbound question and sends nothing leaves the dispatch waiting on a task
    /// nothing will complete, and the stream neither fails nor finishes.
    /// </para>
    /// <para>
    /// A BACKSTOP RATHER THAN A BUDGET, exactly as
    /// <see cref="ColumnExpressionOptions.MacroInvocationTimeout"/> is for C-04's inverted channel. The
    /// ordinary bound is the call's own cancellation, which is real for any client that attaches a gRPC
    /// deadline or simply disconnects. What that does not cover is a client that stays connected and
    /// silent, and against one of those the deadline is the only thing that ends the wait.
    /// </para>
    /// <para>
    /// DERIVED, NOT CHOSEN. The default is the validation session's own idle lifetime, because a question
    /// still outstanding past the point at which its session would have been reclaimed is holding
    /// something the service's own policy had already given up on. It is an order of magnitude away from
    /// any plausible answer, so a well-behaved client never reaches it - which matters because an elapsed
    /// backstop ENDS THE CALL with <c>DeadlineExceeded</c> rather than fabricating an answer, and a
    /// fabricated answer to <c>ondoitemchange</c> would drive the whole <c>{0,1,2,3}</c> dispatch down an
    /// arm the client never chose.
    /// </para>
    /// <para>
    /// SEPARATE FROM THE SESSION SETTING RATHER THAN READ FROM IT, so that shortening one does not
    /// silently shorten the other. They start equal; a deployment that needs to move one moves one.
    /// </para>
    /// </remarks>
    public TimeSpan AnswerTimeout { get; set; } = SessionLifetimeOptions.DefaultIdleTimeout;

    /// <summary>
    /// How many notifications one event-chain stream may hold queued-or-in-flight at once before a
    /// further one is refused. Must be at least 2. Defaults to
    /// <see cref="DefaultMaxPendingNotifications"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>CREATED BY THE DECOMPOSITION, AND IT IS THE SERVER-SIDE ENFORCEMENT OF A DISCIPLINE THAT
    /// IS OTHERWISE ONLY ASSUMED.</b> A notification is handed to a single ordered consumer rather
    /// than dispatched on the read loop, because nine of the 22 events are questions the chain asks
    /// BACK and blocks on, and only the read loop can deliver the answer
    /// [<c>se_cst_dw.sru:L11-L14</c>, <c>:L24-L26</c>, <c>:L28</c>, <c>:L32</c>]. That handover is
    /// correct and stays. What it left open is that the read loop accepts a notification in O(1) while
    /// the consumer may be blocked on one answer for as long as <see cref="AnswerTimeout"/> allows, so
    /// a client that pipelines instead of conversing could enqueue without limit. The synchronous
    /// discipline says it will not; this setting is what makes the server INDEPENDENT of that promise.
    /// </para>
    /// <para>
    /// WHAT "PENDING" COUNTS, PRECISELY: every notification accepted and not yet dispatched to
    /// completion - the queued ones AND the one currently in flight. A conforming client under the
    /// synchronous discipline holds exactly ONE, because it must read a response to learn its next
    /// token, so the shipped default is roughly two orders of magnitude of headroom above what the
    /// protocol itself requires. It is a backstop against a client that ignores the discipline, not a
    /// working limit any conversation approaches.
    /// </para>
    /// <para>
    /// EXCEEDING IT ENDS THE CALL WITH <c>ResourceExhausted</c> RATHER THAN DROPPING THE MESSAGE, and
    /// the choice is forced. Dropping a notification would mean the chain skipped an event the client
    /// believes was dispatched, and the item-change and validation arms read state the preceding event
    /// wrote [<c>se_cst_dw.sru:L89-L96</c>] - so a silent gap produces a WRONG chain rather than a
    /// short one. Refusing the whole call is the same posture strict ordering already takes for an
    /// out-of-order arrival, and it leaves nothing half-applied because the refused message was never
    /// dispatched. <c>ResourceExhausted</c> rather than <c>FailedPrecondition</c> because the remedy is
    /// to converse instead of pipelining, which is a quota's remedy and not a corrupted-state one.
    /// </para>
    /// <para>
    /// THE QUEUE ITSELF STAYS UNBOUNDED AND THE WRITE STAYS NON-BLOCKING, which is why this is a
    /// counted ceiling rather than a bounded channel. A bounded channel would stall the READ LOOP once
    /// full, and the read loop is the only thing that can deliver the answer the consumer is waiting
    /// on - so a full queue would reinstate the original deadlock in a slower form. The admission
    /// decision is therefore taken before the write, in constant time, and a refusal is raised on the
    /// read loop instead of being waited out.
    /// </para>
    /// <para>
    /// TWO IS THE SMALLEST COHERENT VALUE, AND 1 IS REFUSED FOR A PRECISE REASON RATHER THAN A
    /// CAUTIOUS ONE. A slot is released when the dispatch that holds it has finished - which is
    /// immediately AFTER its result has been written - so there is an instant in which the client has
    /// already read the response and is entitled to send the next notification while the previous
    /// slot is still counted. One slot for the conversation and one to cover that instant is
    /// therefore the least that can never refuse a conforming client, and a ceiling of 1 would
    /// refuse one intermittently. That is far worse than any value being too small, because it fails
    /// only under timing.
    /// </para>
    /// </remarks>
    [Range(2, int.MaxValue)]
    public int MaxPendingNotifications { get; set; } = DefaultMaxPendingNotifications;

    /// <summary>
    /// The shipped ceiling on one stream's queued-or-in-flight notifications.
    /// </summary>
    /// <remarks>
    /// NAMED SO THE VALUE IS ASSERTABLE WITHOUT RESTATING A LITERAL. 64 is chosen as a multiple of the
    /// ONE outstanding notification the synchronous discipline admits, large enough that no conforming
    /// client can reach it - including a relaxed-ordering deployment whose consumers pipeline the
    /// sequenced notification-shaped events - and small enough that a flood is refused within 64
    /// messages instead of within available memory. It is not a throughput figure and no performance
    /// objective is asserted for it (AAP 0.8.5).
    /// </remarks>
    public const int DefaultMaxPendingNotifications = 64;
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
/// NO PROPERTY NAME ON THIS TYPE CONTAINS THE WORD "KEY" AT ALL. The one that tempts its way in is a
/// boolean switch selecting whether the framework's handler verifies the signature it retrieves from
/// Security's published verification document, and it is absent along with the other three validation
/// switches, because a boundary whose signature checking a settings file can switch off is only
/// optionally authenticated. Signature verification is a compiled-in <c>true</c> in Program.cs and is not
/// configurable from anywhere.
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
/// annotations on the members below are necessary but NOT sufficient, however complete they look.
/// An attribute can say that an authority is present; it cannot say that the value
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
    /// Whether the credential's issuer is checked against the discovered issuer. Invariantly
    /// <see langword="true"/>; a configured <see langword="false"/> is refused at startup.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="ValidateIssuerSigningKey" path="/remarks/para[@id='invariant']"/>
    /// </remarks>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>
    /// Whether the credential's audience is checked against <see cref="Audience"/>. Invariantly
    /// <see langword="true"/>; a configured <see langword="false"/> is refused at startup.
    /// </summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>
    /// Whether the credential's validity window is enforced. Invariantly <see langword="true"/>; a
    /// configured <see langword="false"/> is refused at startup.
    /// </summary>
    /// <remarks>
    /// Security mints short-lived credentials, so this switch is what makes the shortness mean
    /// anything.
    /// </remarks>
    public bool ValidateLifetime { get; set; } = true;

    /// <summary>
    /// Whether the credential's signature is verified against the material published by the
    /// authority. Invariantly <see langword="true"/>; a configured <see langword="false"/> is refused
    /// at startup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A switch, not a value. It selects whether verification happens; it does not and cannot carry
    /// anything used to perform it, which arrives from the authority at runtime. This is the only
    /// property in this file whose name contains the word "key", and the distinction is recorded so
    /// that a search for that word lands on an explanation rather than on a suspicion.
    /// </para>
    /// <para id="invariant">
    /// THESE FOUR SWITCHES ARE BOUND SO THEY CAN BE AUDITED, NOT SO THEY CAN BE TURNED OFF. Each one
    /// removes an entire class of forgery when it is on: without issuer validation a token from any
    /// issuer is accepted; without audience validation a token minted for another service is replayable
    /// here; without lifetime validation Security's short lifetimes mean nothing and a leaked credential
    /// is permanent; without signing-key validation the signature is not checked at all and any
    /// well-formed token is accepted. None of the four is therefore a deployment choice, and the
    /// composition root assigns <see langword="true"/> unconditionally rather than reading these
    /// values, so no configuration can weaken the delivered behaviour. Because ignoring a configured
    /// <see langword="false"/> in silence would be worse than honouring it - an operator would believe a
    /// switch took effect when it did not - the options validator REFUSES a <see langword="false"/> and
    /// the host does not start. Constraint C-G, and the reason the properties remain visible: a
    /// deployment can still be audited for them by reading its settings file.
    /// </para>
    /// </remarks>
    public bool ValidateIssuerSigningKey { get; set; } = true;

    /// <summary>
    /// The default floor between two on-demand key-set refreshes: five seconds.
    /// </summary>
    /// <remarks>
    /// Bounds how long this boundary keeps refusing a correctly signed token after Security rotates its
    /// signing key. Not lower, because this floor is also the only rate limit on the refresh a REJECTED
    /// token can provoke, so a value near zero would make a forged key identifier a request amplifier
    /// aimed at Security's published key set. Stated identically on all three verification boundaries so
    /// one rotation converges at one rate across the estate.
    /// </remarks>
    public static readonly TimeSpan DefaultMetadataRefreshInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The default interval at which the cached key set is refreshed even when every token validates:
    /// five minutes, the library's own minimum.
    /// </summary>
    /// <remarks>
    /// The library's default is TWELVE HOURS, and this interval is the only thing that ever drops a
    /// RETIRED key, because a successful validation provokes no refresh - so the default left a token
    /// signed by a superseded key acceptable here for half a day.
    /// </remarks>
    public static readonly TimeSpan DefaultMetadataAutomaticRefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The shortest time that must pass before a key-set refresh requested by a failed validation is
    /// actually performed. Defaults to <see cref="DefaultMetadataRefreshInterval"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>ROTATION INVERTS THIS BOUNDARY'S VERDICTS FOR AS LONG AS THIS INTERVAL.</b> When Security
    /// rotates its signing key and key identifier, a token minted BEFORE the rotation keeps validating
    /// against the cached set while one minted AFTER it is refused <c>401</c> with <c>IDX10503</c>. The
    /// handler requests a refresh on exactly that failure, but the configuration manager will not fetch
    /// again until this interval has elapsed - so the library's five-minute default made the inversion
    /// last minutes.
    /// </para>
    /// <para>
    /// <b>A BOUND RATHER THAN AN OVERLAP, AND THAT IS AN AAP CONSTRAINT RATHER THAN A PREFERENCE.</b> The
    /// alternative is for the issuer to publish the superseded key beside the new one until consumers
    /// converge, which a key SET can obviously carry - but AAP 0.6.6.3 fixes exactly one signing secret in
    /// the estate and Security's set is built from that single key, so an overlap would add a second slot
    /// of issuer key material rather than a setting. The window is bounded and documented instead; the
    /// rotation runbook is in <c>docs/SECRETS.md</c> section 4.2.1 and the two intervals are tabulated in
    /// <c>docs/ARCHITECTURE.md</c> section 9.6.
    /// </para>
    /// <para>
    /// The validator refuses anything below <see cref="BaseConfigurationManager.MinimumRefreshInterval"/>,
    /// which the configuration manager would otherwise reject by throwing on the first authenticated
    /// request rather than at startup.
    /// </para>
    /// </remarks>
    public TimeSpan MetadataRefreshInterval { get; set; } = DefaultMetadataRefreshInterval;

    /// <summary>
    /// How often the cached key set is refreshed in the background, independently of any validation
    /// failure. Defaults to <see cref="DefaultMetadataAutomaticRefreshInterval"/>.
    /// </summary>
    /// <remarks>
    /// The half of rotation <see cref="MetadataRefreshInterval"/> cannot bound: a token signed by the
    /// RETIRED key still validates against the cached set and a success provokes no refresh, so only this
    /// interval retires it. The validator refuses anything below
    /// <see cref="BaseConfigurationManager.MinimumAutomaticRefreshInterval"/>.
    /// </remarks>
    public TimeSpan MetadataAutomaticRefreshInterval { get; set; } = DefaultMetadataAutomaticRefreshInterval;

    /// <summary>
    /// The caller identities permitted to reach this service's two contracts and their projected routes.
    /// Bound from <c>Authentication:Jwt:PermittedCallers</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AUTHENTICATION IS NOT AUTHORIZATION, AND THIS IS THE SUBJECT HALF OF THE DIFFERENCE. Protecting both
    /// contracts and all thirty-nine projected routes with "an authenticated user" and nothing
    /// more is the default that has to be refused: any holder of any token minted for this audience could
    /// then call every operation - including a caller with no business here at all. The AAP fixes the call graph as layered and acyclic: nothing but
    /// Gateway calls DataServices. This roster is that statement made enforceable, and the operation's scope
    /// is enforced alongside it, because either alone leaves a hole (CWE-862, CWE-863).
    /// </para>
    /// <para>
    /// COMPARED ORDINALLY against the token's subject claim, matching how the issuer compares an identity
    /// everywhere else. The subject is read from <c>sub</c> or, when the bearer handler maps inbound claims,
    /// from the framework's name-identifier claim type - whichever is present.
    /// </para>
    /// <para>
    /// AN EMPTY ROSTER REFUSES EVERY CALLER, and validation requires at least one entry so that state is
    /// unreachable through configuration. Reading an empty list as "permit everyone" would be a fail-open
    /// default, which is the exact shape of the defect this setting closes.
    /// </para>
    /// <para>
    /// EMPTY BY DEFAULT, AND THE SETTINGS FILE SUPPLIES THE VALUE. The configuration binder POPULATES an
    /// existing collection rather than replacing it, so a non-empty default would ACCUMULATE with whatever
    /// a deployment declares - an operator narrowing the roster would silently still permit the built-in
    /// identity as well. An empty default plus a required minimum length makes the declared value the whole
    /// value.
    /// </para>
    /// <para>
    /// IDENTITIES ONLY. There is no member here that could hold a certificate, a key or a secret: the
    /// token's signature establishes that the subject is genuine, and this records which subjects are
    /// welcome.
    /// </para>
    /// </remarks>
    [MinLength(1)]
    public IList<string> PermittedCallers { get; } = [];
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

            // A zero or negative backstop would abandon every macro invocation before the client could
            // possibly answer, turning the inverted channel into a channel that always times out - and
            // it would do so quietly, as a defined timeout outcome rather than as an error, which is
            // exactly the kind of fault a validator has to catch instead of a caller discovering it.
            AppendPositiveDurationFailure(
                options.ColumnExpression.MacroInvocationTimeout,
                string.Concat(path, ":MacroInvocationTimeout"),
                failures);
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

            // The token-issuance edge's client certificate. Optional as a group and inseparable when
            // present, which is why it is checked here rather than expressed as attributes: no single
            // attribute can say "both or neither".
            string mutualTlsPath = string.Concat(path, ":MutualTls");
            if (EnsureGroupBound(options.Security.MutualTls, mutualTlsPath, failures))
            {
                failures.AddRange(options.Security.MutualTls.DescribeFailures(mutualTlsPath));
            }

            // AND AT LEAST ONE OF THE TWO ACCEPTED SCHEMES MUST BE PRESENT. This is the one rule that
            // spans both groups, so neither group can express it alone. Without it a deployment that
            // configured neither would start, report healthy on its anonymous probe, and then fail every
            // cryptographic call and every call to the audience beneath it - because POST /v1/tokens is
            // the one operation a bearer token cannot protect, so a service with no caller credential
            // obtains no token and nothing downstream is reachable. Refusing at startup puts that fault
            // where the missing configuration is rather than in the first request that needed it.
            if (!options.Security.HasIssuanceCredential)
            {
                failures.Add(string.Concat(
                    "Configuration key '",
                    SecurityClientOptions.ClientSecretConfigurationKey,
                    "' is not set and '",
                    path,
                    ":MutualTls' is not configured either, so this service can present neither of the "
                        + "two credentials contract C-01 accepts on POST /v1/tokens and can therefore "
                        + "obtain no service token at all. Every C-02 cryptographic call and every call "
                        + "to the Persistence audience would be unreachable. Set the issuance secret - "
                        + "the same value Security's issuance roster names for the subject this service "
                        + "presents - or, on a deployment that terminates TLS at Security, configure the "
                        + "client-certificate pair instead. Neither value is quoted here."));
            }
        }

        // --- Internal TLS trust: one optional path, checked for shape only -----------------------
        path = string.Concat(prefix, ":InternalTls");
        if (EnsureGroupBound(options.InternalTls, path, failures))
        {
            failures.AddRange(options.InternalTls.DescribeFailures(path));
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

        // --- EventChain: the strictness dial has no rule - a boolean whose whole domain is legal -
        // --- but the answer backstop does. A zero or negative value would abandon EVERY semantic
        // --- question before the client could possibly answer, so the nine question-shaped events
        // --- would all fail with DeadlineExceeded and the whole synchronous half of the chain would
        // --- be unusable. Caught here because the failure looks like a client fault at run time.
        // ---
        // --- The pending-notification ceiling carries a range annotation, so it is only enforced if
        // --- the annotations are actually walked. Without this a zero or negative value would bind
        // --- silently and then refuse the FIRST notification of every event chain, which reads as a
        // --- client fault at run time while being a configuration one - and a 1, which is legal-looking,
        // --- would refuse a CONFORMING client intermittently.
        path = string.Concat(prefix, ":EventChain");
        if (EnsureGroupBound(options.EventChain, path, failures))
        {
            AppendPositiveDurationFailure(
                options.EventChain.AnswerTimeout,
                string.Concat(path, ":AnswerTimeout"),
                failures);

            // The admission ceiling carries a range annotation, so it is only enforced if the annotations
            // are actually walked. Without this a zero or negative value would bind silently and then refuse
            // EVERY notification on every stream - the whole chain unusable, reported as a client fault.
            AppendAnnotationFailures(options.EventChain, path, failures);
        }

        // The streamed-element bound carries a range annotation, so it is only enforced if the annotations
        // are actually walked. Without this the bound would bind a zero or a negative value silently and
        // then refuse EVERY streamed response at the first element.
        //
        // The collection window's two rules cannot be annotations at all - a TimeSpan has no numeric range
        // and the upper bound is a RELATIONSHIP - so the group checks them itself. Both are fail-fast for
        // the same reason: a zero window answers every event-stream poll empty even with records waiting,
        // and a window past the consumer's own budget cannot fire first, so the caller sees a transport
        // failure instead of the empty collection the operation means. Either reads as a run-time fault
        // while being a configuration one.
        path = string.Concat(prefix, ":RestProjection");
        if (EnsureGroupBound(options.RestProjection, path, failures))
        {
            AppendAnnotationFailures(options.RestProjection, path, failures);

            foreach (ValidationResult result in options.RestProjection.Validate(
                path,
                nameof(DataServicesOptions.RestProjection)))
            {
                if (result.ErrorMessage is { Length: > 0 } message)
                {
                    failures.Add(message);
                }
            }
        }

        // THE SESSION DESCRIPTOR IS FAIL-FAST BECAUSE EVERY RETRIEVAL AND EVERY UPDATE DEPENDS ON IT
        // (AAP 0.1.4). Its DBMS field is not a label - Persistence substring-tests it to choose the paging
        // dialect and falls back to SQL Server when the test does not match
        // [n_cst_thread_trans.sru:L356-L362] - so an unbound value would silently select a dialect rather
        // than refuse to start. Starting and then failing every data operation is precisely the graceful
        // degradation the ported fail-fast posture forbids.
        path = string.Concat(prefix, ":PersistenceSession");
        if (EnsureGroupBound(options.PersistenceSession, path, failures))
        {
            AppendAnnotationFailures(options.PersistenceSession, path, failures);

            // AUTOCOMMIT IS THE ONE MEMBER OF THIS GROUP WITH A SINGLE DEPLOYABLE VALUE, AND THAT IS A
            // PUBLISHED CONTRACT RULE RATHER THAN A PREFERENCE OF THIS SERVICE.
            //
            // The member mirrors transactiondata.srs:L11 and travels on the session request, but the
            // legacy ERASES it before the descriptor reaches either the connection pool or a transaction
            // object [n_cst_thread_task_sqlbase.sru:L118-L119, n_cst_thread_trans.sru:L343-L354], so
            // C-08 refuses a descriptor that sets it instead of accepting it and quietly discarding it -
            // see the TransactionDescriptor header in persistence.v1.proto. Setting it true here would
            // therefore refuse EVERY session this service opens, which is not a per-request fault: no
            // retrieval, no update and no expression host can be created until the setting is corrected.
            //
            // FAIL-FAST RATHER THAN DERIVED-AND-REMOVED, which is the difference between this member and
            // the two connection flags that used to sit beside it. Those were removed because a SECOND
            // authority for the same behaviour could contradict the first, and a leftover key binding to
            // nothing is harmless. This one has no second authority to defer to and its default already
            // is the only legal value, so removing the property would make a deployed
            // "AutoCommit": true bind to nothing and be IGNORED IN SILENCE - strictly worse than a
            // startup failure that names the key and the three routes that do work.
            if (options.PersistenceSession.AutoCommit)
            {
                failures.Add(string.Concat(
                    path,
                    ":AutoCommit must be false. The member mirrors transactiondata.srs:L11 and is ",
                    "carried on the session request, but the legacy erases it before the descriptor ",
                    "reaches the connection pool or a transaction object ",
                    "(ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L118-L119 and ",
                    "ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L343-L354), so ",
                    "Persistence refuses a descriptor that sets it rather than accept it and discard ",
                    "it - setting it true here would refuse every session this service opens. Leave it ",
                    "false and choose where the commit belongs instead: this service already sets the ",
                    "update task's own autocommit switch so a single-call update commits before its ",
                    "session ends, and a caller holding a session of its own moves the connection-level ",
                    "mode with the transaction contract's SetAutoCommit."));
            }
        }

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
        AppendPositiveDurationFailure(
            client.StreamDeadline,
            string.Concat(configurationPath, ":StreamDeadline"),
            failures);

        // ONE ATTEMPT CANNOT OUTLAST THE TOTAL THAT CONTAINS IT - the resilience pipeline refuses that
        // configuration outright, so it is caught here with the key named rather than left to surface as a
        // framework validation failure against a generated handler name.
        if (client.AttemptTimeout is { } attempt
            && (attempt <= TimeSpan.Zero || attempt > client.RequestTimeout))
        {
            failures.Add(string.Concat(
                configurationPath,
                ":AttemptTimeout must be greater than zero and no greater than ",
                configurationPath,
                ":RequestTimeout. Leave it unset to bound one attempt by the total."));
        }

        // A STREAM BOUND BELOW THE UNARY BOUND IS A CONTRADICTION RATHER THAN A TIGHTER POLICY: the
        // whole reason the two settings exist separately is that a stream is legitimately longer-lived
        // than a single request, so a shorter stream bound abandons retrievals sooner than the ordinary
        // calls beside them and does so silently, as a deadline failure that looks like an upstream
        // fault. Checked here rather than by annotation because it is a relationship between two values.
        if (client.StreamDeadline > TimeSpan.Zero
            && client.RequestTimeout > TimeSpan.Zero
            && client.StreamDeadline < client.RequestTimeout)
        {
            failures.Add(string.Concat(
                configurationPath,
                ":StreamDeadline must not be shorter than ",
                configurationPath,
                ":RequestTimeout. A stream is bounded by the upstream's own handle lifetime rather ",
                "than by one request's budget, so a stream bound below the unary bound would abandon ",
                "retrievals before an ordinary call beside them would time out."));
        }
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

        // --- The two metadata refresh intervals, checked against the LIBRARY'S published floors -----
        // BaseConfigurationManager throws ArgumentOutOfRangeException - IDX10107 and IDX10108 - when
        // either is set below its minimum, and it throws while the bearer handler builds its configuration
        // manager, which happens on the first authenticated request rather than at startup. A host that
        // started healthy and then failed every authenticated call is exactly the shape of fault this
        // validator exists to convert into a refusal to start. The floors are read from the library rather
        // than restated as literals so the rule cannot drift away from the behaviour it guards.
        if (options.MetadataRefreshInterval < BaseConfigurationManager.MinimumRefreshInterval)
        {
            failures.Add(string.Concat(
                prefix,
                ":",
                nameof(JwtAuthenticationOptions.MetadataRefreshInterval),
                " is ",
                options.MetadataRefreshInterval.ToString(),
                ", which is below the ",
                BaseConfigurationManager.MinimumRefreshInterval.ToString(),
                " minimum the token library enforces. It would be rejected while the bearer handler ",
                "builds its configuration manager - on the first authenticated request, not at startup."));
        }

        if (options.MetadataAutomaticRefreshInterval
            < BaseConfigurationManager.MinimumAutomaticRefreshInterval)
        {
            failures.Add(string.Concat(
                prefix,
                ":",
                nameof(JwtAuthenticationOptions.MetadataAutomaticRefreshInterval),
                " is ",
                options.MetadataAutomaticRefreshInterval.ToString(),
                ", which is below the ",
                BaseConfigurationManager.MinimumAutomaticRefreshInterval.ToString(),
                " minimum the token library enforces, and would be rejected on the first authenticated ",
                "request rather than at startup."));
        }
        else if (options.MetadataRefreshInterval > options.MetadataAutomaticRefreshInterval)
        {
            failures.Add(string.Concat(
                prefix,
                ":",
                nameof(JwtAuthenticationOptions.MetadataRefreshInterval),
                " is ",
                options.MetadataRefreshInterval.ToString(),
                ", which is longer than ",
                nameof(JwtAuthenticationOptions.MetadataAutomaticRefreshInterval),
                " (",
                options.MetadataAutomaticRefreshInterval.ToString(),
                "). The first is the floor on a refresh a REJECTED token asks for and the second is the ",
                "background interval, so a floor above it makes a rotation converge more slowly for a ",
                "caller presenting a new token than for one presenting nothing at all."));
        }

        // --- The four validation switches: invariant, and a configured `false` is refused -----------
        AppendDisabledValidationFailures(options, prefix, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Appends one failure per token-validation switch a deployment has turned off.
    /// </summary>
    /// <param name="options">The bound instance being validated.</param>
    /// <param name="prefix">The configuration path this instance was bound from.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// <para>
    /// EACH OF THE FOUR REMOVES A WHOLE CLASS OF FORGERY, SO NONE IS A DEPLOYMENT CHOICE. Without issuer
    /// validation a token from any issuer is accepted; without audience validation a token minted for
    /// another service is replayable here, which is exactly what the one-audience-per-token rule of
    /// contract C-01 exists to prevent; without lifetime validation Security's short lifetimes mean
    /// nothing and a leaked credential is permanent; without signing-key validation the signature is not
    /// checked at all and any well-formed token is accepted. A host that starts with one of them off is
    /// an unauthenticated boundary wearing the shape of an authenticated one, which constraint C-G
    /// forbids outright.
    /// </para>
    /// <para>
    /// AND THE COMPOSITION ROOT DOES NOT READ THEM, WHICH IS WHY THIS CHECK EXISTS RATHER THAN BEING
    /// REDUNDANT WITH IT. The bearer handler is configured with <see langword="true"/> unconditionally,
    /// so a configured <see langword="false"/> would otherwise be silently ignored - and silence is the
    /// worse failure of the two, because an operator would believe the switch took effect. Refusing to
    /// start says plainly that the setting is not honoured and not honourable. Every message names the
    /// exact key and states what the switch protects, so the fix is a one-line edit rather than an
    /// investigation.
    /// </para>
    /// </remarks>
    private static void AppendDisabledValidationFailures(
        JwtAuthenticationOptions options,
        string prefix,
        List<string> failures)
    {
        AppendIfDisabled(
            options.ValidateIssuer,
            nameof(JwtAuthenticationOptions.ValidateIssuer),
            "a credential minted by any issuer whatsoever would be accepted, so Security would no "
                + "longer be the sole authority this boundary trusts");

        AppendIfDisabled(
            options.ValidateAudience,
            nameof(JwtAuthenticationOptions.ValidateAudience),
            "a credential minted for a different service would be replayable here, which is precisely "
                + "what the one-audience-per-token rule of contract C-01 exists to prevent");

        AppendIfDisabled(
            options.ValidateLifetime,
            nameof(JwtAuthenticationOptions.ValidateLifetime),
            "an expired credential would be accepted indefinitely, so the short lifetimes Security "
                + "mints would bound nothing");

        AppendIfDisabled(
            options.ValidateIssuerSigningKey,
            nameof(JwtAuthenticationOptions.ValidateIssuerSigningKey),
            "the signature would not be verified at all, so any well-formed token would be accepted");

        void AppendIfDisabled(bool enabled, string member, string consequence)
        {
            if (enabled)
            {
                return;
            }

            failures.Add(string.Concat(
                prefix,
                ":",
                member,
                " is false. This switch is invariant and cannot be turned off: with it disabled, ",
                consequence,
                ". The bearer handler is configured with it enabled regardless of this value, so the "
                    + "setting would not take effect - and a setting that is silently ignored is worse "
                    + "than one that is honoured, which is why the host refuses to start instead. Remove "
                    + "the key or set it to true."));
        }
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

/// <summary>
/// Bounds on the thin REST projection of the two gRPC contracts.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS IS A RESOURCE BOUND, NOT A PERFORMANCE SETTING, and the distinction is load bearing because no
/// performance objective may be asserted anywhere in this refactor (AAP 0.8.5).</b> The projection answers
/// a server-streaming gRPC method as one JSON array, which means it must hold the whole sequence before it
/// can answer at all - that is deliberate, and it is what makes a mid-stream failure produce a clean
/// problem body instead of a half-written success. The consequence is that an unbounded stream is an
/// unbounded allocation, and the bound below is what stops one request from exhausting the process.
/// </para>
/// <para>
/// It is CONFIGURED rather than fixed because the honest limit depends on the deployment's memory budget
/// and on the row width its DataWindows carry, neither of which this code can know. Exceeding it is a
/// DEFINED ERROR rather than a truncation: a truncated array is indistinguishable from a complete one, so
/// silently dropping the tail would answer a retrieval with the wrong answer and report success.
/// </para>
/// </remarks>
public sealed class RestProjectionOptions
{
    /// <summary>
    /// The largest number of elements the projection will hold for one streamed response.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is generous rather than tight, because the projection exists for Gateway and a
    /// legitimate retrieval of a chunked DataWindow produces one element per chunk rather than one per
    /// row - so the element count is small for any realistic result and the bound is a backstop against a
    /// runaway producer, not a working limit anyone should meet.
    /// </para>
    /// <para>
    /// The range starts at one and not zero: a bound of zero would refuse every streamed response including
    /// an empty one, which is a misconfiguration that reads as a total outage, and refusing it at startup
    /// is the fail-fast posture this service applies to every structural fault.
    /// </para>
    /// </remarks>
    [Range(1, int.MaxValue)]
    public int MaxStreamedElements { get; set; } = 10_000;

    /// <summary>
    /// The shipped collection window for a projected stream that never ends on its own.
    /// </summary>
    /// <remarks>
    /// TWO SECONDS ANSWERS AN IDLE SUBSCRIPTION PROMPTLY WHILE LEAVING A WIDE MARGIN under every patience
    /// the request passes through. It is also the value Gateway's own projection of the same operation
    /// ships, so the two windows cannot fight: a caller reaching this service directly and a caller
    /// reaching it through the ingress observe the same completeness rule rather than two.
    /// </remarks>
    public static readonly TimeSpan DefaultStreamCollectionWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The exclusive upper bound on <see cref="StreamCollectionWindow"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TEN SECONDS, BECAUSE THAT IS THE PER-ATTEMPT BUDGET THE ONE DOCUMENTED CONSUMER OF THIS PROJECTION
    /// APPLIES TO IT. A window at or beyond that cannot fire first: the consumer abandons the attempt
    /// while this service is still collecting, and the caller receives a transport failure instead of the
    /// empty collection the operation means - which is the very defect the window exists to close, moved
    /// one hop out rather than fixed.
    /// </para>
    /// <para>
    /// THE NUMBER IS RESTATED HERE RATHER THAN READ FROM THE OTHER SERVICE, and that is constraint C-A
    /// rather than duplication for its own sake: no behaviour and no options type crosses a service
    /// boundary in this system, so a bound that reached into the ingress's configuration would be exactly
    /// the coupling the decomposition forbids. It is documented on both sides instead, and the DEFAULT
    /// leaves so much margin - two seconds against ten - that the two can only collide if an operator
    /// deliberately moves one.
    /// </para>
    /// </remarks>
    internal static readonly TimeSpan MaximumStreamCollectionWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long the projection collects from a stream that never completes on its own, before answering
    /// with what it has. Defaults to <see cref="DefaultStreamCollectionWindow"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THIS APPLIES TO ONE OPERATION AND NOT TO STREAMING IN GENERAL, WHICH IS WHY IT IS OPT-IN PER
    /// ROUTE.</b> The expression event stream is a SUBSCRIPTION: the engine ends it only when the client
    /// goes away, so a request/response projection of it has to decide for itself when the collection is
    /// complete. Without that decision the projected route collected an intentionally open-ended stream
    /// to completion and therefore never answered at all - a normal HTTP request received neither the
    /// events nor a success status, and pinned a request thread, a relay subscription and a connection
    /// for as long as the caller was willing to wait. Every OTHER projected stream terminates itself - a
    /// retrieval ends with its final-marked chunk - so applying a window to one of those would truncate a
    /// legitimate result.
    /// </para>
    /// <para>
    /// A WINDOW THAT EXPIRES IS A COMPLETE ANSWER RATHER THAN A TRUNCATION. The caller asked for the
    /// records available now, so the collected sequence - empty included - is exactly what the operation
    /// means, and the response is a well-formed 200. That is the opposite of exceeding
    /// <see cref="MaxStreamedElements"/>, which refuses the document precisely so the caller can tell it
    /// did not receive everything.
    /// </para>
    /// <para>
    /// It is a completeness rule and not a performance claim - no performance objective is asserted
    /// anywhere in this refactor (AAP 0.8.5).
    /// </para>
    /// </remarks>
    public TimeSpan StreamCollectionWindow { get; set; } = DefaultStreamCollectionWindow;

    /// <summary>
    /// Checks the one setting on this type whose correctness is a relationship rather than a range.
    /// </summary>
    /// <param name="configurationKeyPrefix">The configuration path this group binds from.</param>
    /// <param name="memberName">The member name reported on a failure.</param>
    /// <returns>The failures, or an empty sequence when the group is coherent.</returns>
    /// <remarks>
    /// BOTH BOUNDS ARE NAMED IN THE MESSAGE, AND SO IS THE KEY, because an operator reading a refusal to
    /// start needs the setting to change rather than a description of a category of fault. Neither rule is
    /// expressible as a range annotation: a <see cref="TimeSpan"/> has no numeric range, and the upper
    /// bound is a relationship to another value rather than a constant a caller could read off the type.
    /// </remarks>
    internal IEnumerable<ValidationResult> Validate(string configurationKeyPrefix, string memberName)
    {
        if (StreamCollectionWindow <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"'{configurationKeyPrefix}:{nameof(StreamCollectionWindow)}' is "
                    + $"{StreamCollectionWindow}, which collects nothing at all: the projected event "
                    + "stream would answer an empty collection for every request, including one with "
                    + "records already waiting. It must be greater than zero.",
                [memberName]);
        }
        else if (StreamCollectionWindow >= MaximumStreamCollectionWindow)
        {
            yield return new ValidationResult(
                $"'{configurationKeyPrefix}:{nameof(StreamCollectionWindow)}' is "
                    + $"{StreamCollectionWindow}, which is not below the "
                    + $"{MaximumStreamCollectionWindow} per-attempt budget this projection's documented "
                    + "consumer applies to it. A window that cannot fire first leaves the consumer's own "
                    + "timeout to end an idle subscription, which reaches the caller as a transport "
                    + "failure rather than as the empty collection the operation means.",
                [memberName]);
        }
    }
}

/// <summary>
/// The transaction session this service opens against Persistence, mirroring the legacy transaction
/// structure field for field.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY THIS EXISTS AT ALL.</b> C-05 and C-06 are TASK-scoped and a task is SESSION-scoped: a retrieval
/// needs a query task, a query task needs a session, and a session needs a transaction descriptor. There
/// is nowhere else for that descriptor to come from - it describes a connection, and where a connection
/// points is a deployment decision, so it is configuration by construction. Without it this service can
/// only send task-less requests, which C-05 and C-06 correctly reject.
/// </para>
/// <para>
/// <b>THE FIELDS MIRROR <c>transactiondata.srs</c> AND ARE NOT A SUBSET</b>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs</c>]. Persistence resolves the paging dialect
/// by substring-testing <see cref="Dbms"/>, so it is behaviourally load bearing and not a label.
/// </para>
/// <para>
/// <b><see cref="LogPass"/> IS WRITE-ONLY AND IS NEVER ECHOED OR LOGGED.</b> The legacy structure carries
/// it and the contract carries it, but the response view RESERVES its field number permanently so it can
/// never appear on an answer. It is bound from configuration like every other secret in this system - from
/// the orchestration secret layer, never from a literal in source or in an application settings file
/// (constraint C-F) - and the shipped settings file leaves it EMPTY rather than supplying a value, which
/// is correct for SQLite: the only evidenced storage engine takes no password on the unencrypted path,
/// and the encrypted path is out of Phase-1 scope (AAP 0.6.4).
/// </para>
/// </remarks>
public sealed class PersistenceSessionOptions
{
    /// <summary>
    /// The DBMS identifier. <c>[transactiondata.srs:L4]</c>
    /// </summary>
    /// <remarks>
    /// ALSO THE DIALECT SELECTOR, which is why it is required rather than defaulted to an empty string:
    /// Persistence substring-tests this value to choose the paging rewriter and falls back to SQL Server
    /// when the test does not match [<c>n_cst_thread_trans.sru:L356-L362</c>], so an unset value would
    /// silently select a dialect rather than fail.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Dbms { get; set; } = "SQLite";

    /// <summary>The server name. <c>[transactiondata.srs:L5]</c></summary>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>The database name. <c>[transactiondata.srs:L6]</c></summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// The login identifier. <c>[transactiondata.srs:L7]</c>
    /// </summary>
    /// <remarks>
    /// An ACCOUNT NAME rather than a secret, which is why - unlike the password - the contract does place
    /// it on the response view. It is still configuration and never a literal.
    /// </remarks>
    public string LogId { get; set; } = string.Empty;

    /// <summary>
    /// The login password. WRITE-ONLY. <c>[transactiondata.srs:L8]</c>
    /// </summary>
    /// <remarks>
    /// Supplied here, sent on the session request, and NEVER read back, echoed or logged anywhere. Empty
    /// by default because SQLite on the unencrypted path takes none; a deployment that needs one injects it
    /// from the orchestration secret layer.
    /// </remarks>
    public string LogPass { get; set; } = string.Empty;

    /// <summary>The provider parameter string. <c>[transactiondata.srs:L9]</c></summary>
    /// <remarks>
    /// <para>
    /// Opaque to this service and forwarded unexamined. It is the value Persistence parses for the
    /// bind-disabling and national-character-binding flags
    /// [<c>n_cst_thread_task_sqlbase.sru:L128-L129</c>], and it can carry a whole connection string, which
    /// is why the contract's response view reserves its field number alongside the password's.
    /// </para>
    /// <para>
    /// <b>⚠ THIS IS THE SINGLE AUTHORITY FOR THE TWO CONNECTION FLAGS, AND THERE IS DELIBERATELY NO
    /// SECOND WAY TO SET THEM.</b> Persistence derives <c>DisableBind</c> and <c>NCharBind</c> from this
    /// string by the oracle's own regular expressions and REFUSES a session whose explicitly supplied
    /// flags disagree with what the string resolves to - correctly, because honouring a disagreeing flag
    /// would mean rewriting the caller's connection string and ignoring one would let a caller believe it
    /// had disabled binding when it had not. This class therefore exposed the flags as independently
    /// settable properties and this service sent them alongside the string, which made a plausible partial
    /// configuration - the string set and a flag forgotten, or both set but inconsistent under the nesting
    /// rule - break EVERY session with <c>E_INVALID_ARGUMENT</c> rather than only the request that got it
    /// wrong. The properties are gone; the string is the only input.
    /// </para>
    /// <para>
    /// <b>THE NESTING RULE IS WHY A SEPARATE PAIR OF BOOLEANS WOULD BE A TRAP.</b> The oracle reads
    /// <c>NCharBind</c> ONLY inside the <c>DisableBind</c> branch [<c>:L127-L132</c>], so
    /// <c>"DisableBind=1"</c> on its own resolves to <c>disable_bind=true, nchar_bind=FALSE</c> - and an
    /// operator who set that string and then set both properties true, which reads as the obviously
    /// consistent thing to do, would produce a disagreement. Deriving both from one string cannot produce
    /// one.
    /// </para>
    /// <para>
    /// This service does not parse the string. It forwards it and lets the service that owns the
    /// connection resolve it, which keeps ONE reproduction of the legacy regular expressions in the whole
    /// system rather than two that can drift across a network boundary.
    /// </para>
    /// </remarks>
    public string DbParm { get; set; } = string.Empty;

    /// <summary>The isolation setting. <c>[transactiondata.srs:L10]</c></summary>
    public string Lock { get; set; } = string.Empty;

    /// <summary>
    /// Whether the session commits each statement as it runs. <c>[transactiondata.srs:L11]</c>
    /// </summary>
    /// <remarks>
    /// <para>
    /// FALSE by default, and that is the preserved legacy posture rather than a preference: an update
    /// applies many rows and the caller owns the carrier's state afterwards, so a session that committed
    /// per statement would make a partially applied update unrecoverable.
    /// </para>
    /// <para>
    /// <b>⚠ FALSE IS ALSO THE ONLY DEPLOYABLE VALUE, AND THE VALIDATOR REFUSES TRUE AT STARTUP.</b> The
    /// member mirrors <c>[transactiondata.srs:L11]</c> and is carried on the session request, but the
    /// legacy erases it before the descriptor reaches either the connection pool or a transaction object
    /// [<c>n_cst_thread_task_sqlbase.sru:L118-L119</c>, <c>n_cst_thread_trans.sru:L343-L354</c>], so C-08
    /// refuses a descriptor that sets it rather than accept it and discard it. It is still forwarded
    /// verbatim by <c>BuildTransactionDescriptor</c> - a graph built in code that bypasses the validator
    /// then meets that refusal at <c>BeginSession</c> instead of having its request silently rewritten.
    /// </para>
    /// <para>
    /// WHERE THE COMMIT ACTUALLY BELONGS. A single-call update commits because this service sets C-06's
    /// own task-level autocommit switch, which is the oracle's epilogue
    /// <c>if _bAutoCommit then rtCode = of_Commit(true)</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L386-L387</c>] and is a different switch from this one; a
    /// caller holding a session of its own moves the connection-level mode with C-08's
    /// <c>SetAutoCommit</c>.
    /// </para>
    /// </remarks>
    public bool AutoCommit { get; set; }

    /// <summary>The user parameter string. <c>[transactiondata.srs:L12]</c></summary>
    public string UserParm { get; set; } = string.Empty;

    // ==============================================================================================
    //  NO DisableBind AND NO NCharBind PROPERTY LIVES HERE, AND THAT IS THE FIX RATHER THAN AN
    //  OMISSION.
    //
    //  Both flags are DERIVED from DbParm by the legacy's own regular expressions
    //  [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128-L129], and Persistence
    //  reproduces that derivation in exactly one place. Offering them here as independently settable
    //  properties gave one behaviour two inputs, and a disagreement between the two is not a
    //  recoverable request-level error: Persistence refuses the SESSION, so every retrieval and every
    //  update on the deployment fails until the configuration is corrected.
    //
    //  The safe default the removed DisableBind property documented survives unchanged, because it was
    //  never this property that produced it: an empty DbParm matches neither pattern, so binding stays
    //  ENABLED and values travel as parameters rather than as interpolated literals. The shipped
    //  settings file leaves DbParm empty, so a deployment that configures nothing gets the safe arm -
    //  which is the same guarantee, with one fewer way to contradict it.
    //
    //  See the remarks on DbParm for the nesting rule that makes a separate boolean pair a trap for
    //  exactly the configuration an operator would most plausibly write.
    // ==============================================================================================
}
