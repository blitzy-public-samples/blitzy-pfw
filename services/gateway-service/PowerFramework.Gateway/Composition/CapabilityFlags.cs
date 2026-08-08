// ======================================================================================================
//  CapabilityFlags - the legacy PowerFramework module gating bitmask, re-expressed as configuration
//  ----------------------------------------------------------------------------------------------------
//  PORTED FROM   ws_objects/pfw.shared.pbl.src/enums.sru:L40-L49
//                    the eight capability bits, and the composite INIT_FLAG_ENABLE_ALL at :L49
//                ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L7-L8
//                    the two native entry points the mask is an argument to
//                ws_objects/pfw.pbl.src/pfw.sra:L91
//                    pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL), the mask's ONLY consumer in the estate
//                ws_objects/pfw.base.pbl.src/n_initializer.sru:L14-L20
//                    the alternative seven switch gate, recorded here as an asymmetry and not ported
//                docs/README.md:L9, L17-L21, L26, L30
//                    what a capability bit actually controls, plus two documentation defects
//
//  ORACLE STATUS Every path above is READ ONLY. Those files are the behavioural oracle for parity
//                testing and never an edit target, so every assertion in this file carries the locator
//                that settles it. Nothing here was inferred from an identifier's name.
//
//  WHY THIS TYPE EXISTS, AND WHY ITS FIDELITY IS NOT COSMETIC
//  ----------------------------------------------------------------------------------------------------
//  The legacy framework gates its own modules with a bitmask handed to pfwInitialize. That bitmask is
//  the legacy's OWN decomposition intent: it was written years before this refactor, for its own
//  reasons, and mapping its bits onto the Phase 1 service roster corroborates independently that the
//  four service slice is drawn along seams the framework's authors had already recognised.
//
//      INIT_FLAG_ENABLE_UI          1     enums.sru:L41   DesignSystem  (deferred)
//      INIT_FLAG_ENABLE_SCITER      2     enums.sru:L42   ScriptBridge  (deferred)
//      INIT_FLAG_ENABLE_BLINK       4     enums.sru:L43   ScriptBridge  (deferred)
//      INIT_FLAG_ENABLE_BLINKFAST   8     enums.sru:L44   ScriptBridge  (deferred)
//      INIT_FLAG_ENABLE_ORCA        256   enums.sru:L45   PowerBuilder packaging tooling, not a service
//      INIT_FLAG_ENABLE_SQLITE      512   enums.sru:L46   Persistence   <- the ONLY in scope consumer
//      INIT_FLAG_ENABLE_DPIAWARE    1024  enums.sru:L47   DesignSystem  (deferred)
//      INIT_FLAG_ENABLE_WEBVIEW     2048  enums.sru:L48   ScriptBridge  (deferred)
//
//  Read the right hand column and the Phase 1 boundary falls out of it: storage, presentation, scripting
//  and packaging tooling were already separate concepts in the legacy's own gating vocabulary.
//
//  WHAT A BIT CONTROLS, WHICH IS WHY THE GATE IS CONFIGURATION RATHER THAN A CONSTANT
//  ----------------------------------------------------------------------------------------------------
//  docs/README.md:L26 states that a module which is not explicitly initialized has its related
//  functionality UNUSABLE and does not need its DLL shipped at all, and :L30 warns that a module asked
//  for without its DLL present makes initialization FAIL outright. That is a hard failure, not a
//  degradation, and it is the same posture the composition root preserves as fail fast startup. The
//  legacy therefore already treated its capability set as a deployment time decision determining which
//  artifacts even need to be present, and docs/README.md:L9 records that the flags argument is optional
//  and that combinations are supported, i.e. bitwise composition. Exposing the equivalent gate as
//  configuration reproduces that posture; hard wiring a fixed set would not.
//
//  ASYMMETRY A - INIT_FLAG_ENABLE_ALL IS A SEVEN TERM SUM, SO IT IS 3847 AND NOT 3855
//  ----------------------------------------------------------------------------------------------------
//  Reproduced and annotated at its point of reproduction below, on AllCapabilitiesMask. In summary:
//  enums.sru:L49 sums UI + SCITER + BLINK + ORCA + SQLITE + DPIAWARE + WEBVIEW and omits BLINKFAST,
//  because blink.dll and blinkfast.dll are alternative builds of ONE engine rather than two independent
//  capabilities, so an "everything on" constant naming both would be incoherent. It looks like an
//  oversight and it is not one. Folding the eighth bit in would change behaviour (C-B).
//
//  ASYMMETRY B - THE OTHER LEGACY GATE ALSO HAS SEVEN MEMBERS, AND IT IS A DIFFERENT SEVEN
//  ----------------------------------------------------------------------------------------------------
//  The legacy offers a second initialization mechanism: inherit an initializer object and let automatic
//  instantiation drive the lifecycle [docs/README.md:L17-L21]. That object declares exactly SEVEN
//  protected boolean switches [ws_objects/pfw.base.pbl.src/n_initializer.sru:L14-L20]:
//
//      #UI        = true     n_initializer.sru:L14    the ONLY one defaulting to true
//      #Sciter    = false    n_initializer.sru:L15
//      #Blink     = false    n_initializer.sru:L16
//      #BlinkFast = false    n_initializer.sru:L17
//      #ORCA      = false    n_initializer.sru:L18
//      #SQLite    = false    n_initializer.sru:L19
//      #DPIAware  = false    n_initializer.sru:L20
//
//  There is NO #WebView switch, even though INIT_FLAG_ENABLE_WEBVIEW = 2048 exists at enums.sru:L48.
//
//  STATE THE TWO SEVENS PRECISELY AND DO NOT BLUR THEM:
//
//      the bitmask composite INIT_FLAG_ENABLE_ALL omits   BLINKFAST
//      the initializer object's switch set omits          WEBVIEW
//
//  Those are DIFFERENT sevens. Their union is all eight capabilities, and NEITHER legacy artifact on its
//  own expresses all eight. They are never "the same seven". This type models all EIGHT bits, because
//  the bitmask at enums.sru:L41-L48 is the authoritative set, and it RECORDS the seven switch asymmetry
//  here instead of silently harmonizing the two mechanisms.
//
//  The '#' prefix those switches carry is not a legal C# identifier character, which is a second and
//  independent reason the switch set is recorded as an observation rather than ported as a structure.
//
//  TWO DEFECTS IN THE LEGACY DOCUMENTATION - RECORDED, DELIBERATELY NOT CORRECTED
//  ----------------------------------------------------------------------------------------------------
//  Both live in a read only document (C-C), so neither is edited there and neither is repeated as fact
//  anywhere in this refactor. They are written down here so that a later reader does not "fix" this code
//  to agree with the document.
//
//    1. WRONG LIBRARY. docs/README.md:L19 attributes n_initializer to pfw.common.pbl. The only
//       n_initializer.sru in the tree is exported under ws_objects/pfw.base.pbl.src/, and
//       ws_objects/pfw.common.pbl.src/ carries no initialization object at all. Cite the real location.
//
//    2. THE EQUIVALENCE CLAIM IS FALSE. docs/README.md:L20 states that the initializer's property
//       switches are equivalent to the Enums.INIT_FLAG_XXX values. Asymmetry B disproves it: seven
//       switches against eight bits, and the missing one is WebView, so the two mechanisms are not
//       interchangeable for a WebView enabled application.
//
//  SCOPE RULING - REPRESENTING A BIT IS NOT IMPLEMENTING ITS SERVICE (C-D)
//  ----------------------------------------------------------------------------------------------------
//  Seven of the eight bits name capability areas that are outside this phase entirely: UI and DPIAWARE
//  belong to the deferred DesignSystem, SCITER, BLINK, BLINKFAST and WEBVIEW to the deferred
//  ScriptBridge, and ORCA to PowerBuilder packaging tooling, which is not a service at any phase. Each
//  of those seven ships here as a NAME, a VALUE and a predicate over the configured mask, and as nothing
//  else whatsoever. There is no client, no handler, no service class, no partial implementation and no
//  placeholder that throws behind any of them, here or anywhere.
//
//  The eighth bit, SQLITE, is the only one with an in scope consumer, and that consumer is PERSISTENCE
//  rather than Gateway. So this type does not act on it either: it reports the bit and stops. Nothing in
//  this file opens a connection, names a database, references a storage package or reaches a provider
//  (C-E) - the SQLite bit is a capability flag, not a database. Structural corroboration sits in the
//  sibling project file, which references no EF Core, Sqlite or SQLitePCLRaw package at all.
//
//  This file likewise holds no key material, no credential, no token and no signing authority, and it
//  touches authentication nowhere (C-F, C-G). Security is the sole token issuer in this system, and a
//  capability gate has no business anywhere near that.
//
//  WIDTH IS A CONTRACT, AND IT IS 32 BITS
//  ----------------------------------------------------------------------------------------------------
//  The eight constants are declared Constant Long in PowerScript, a 32-bit SIGNED type, while the
//  parameter they are passed to is declared readonly unsignedlong [pfwinitialize.srf:L8]. PowerBuilder's
//  unsignedlong is 32 bits wide, so the target type is C# uint and NEVER ulong. The shared constant
//  catalogue surfaces the eight bits as C# long, matching their declared PowerScript type, and the
//  shared bit helpers are authored against uint, matching the parameter. This type is where those two
//  widths meet, so every conversion it performs is written out EXPLICITLY rather than left implicit; see
//  FromConfiguredValue. No ulong appears anywhere on the path to the native flags argument.
//
//  THE BIT LAYOUT IS SPARSE, AND THE GAP IS NOT AN INVITATION
//  ----------------------------------------------------------------------------------------------------
//  The values run 1, 2, 4, 8 and then jump straight to 256: bit positions 4 through 7, values 16, 32, 64
//  and 128, are unassigned. An exhaustive search of the estate returns exactly nine INIT_FLAG_
//  identifiers, the eight bits plus the composite, so nothing anywhere claims that range. This type
//  therefore invents no meaning for it, reserves no name in it, and does not treat the mask as dense.
//  Unassigned bits, and any bit above 2048, are reported as DATA through UnrecognizedBits.
//
//  NO VALIDATION, BECAUSE THE LEGACY HAS NONE (C-B)
//  ----------------------------------------------------------------------------------------------------
//  pfwInitialize validates nothing about the mask it receives, and the configuration property this type
//  is constructed from is deliberately left unvalidated for exactly that reason. Construction here
//  consequently cannot fail on the value of a mask: an all bits set mask, a zero mask, a negative
//  configured value and a mask carrying unassigned bits are all well defined inputs producing a well
//  defined instance. Rejecting any of them would be new behaviour dressed up as robustness.
//
//  PURITY, WHICH IS WHAT MAKES THE COVERAGE GATE REACHABLE (C-H)
//  ----------------------------------------------------------------------------------------------------
//  This type is a pure function of one 32-bit mask. It reads no configuration, injects no options
//  monitor, logs nothing, reads no clock, touches no environment variable, performs no I/O and holds no
//  static mutable state. Given the same mask it produces identical results in any order in any process,
//  so its tests need no web host, no application factory and no fixture. Do not add a dependency that
//  would drag a host into them.
//
//  NAMING - CONSUME THE PRESERVED IDENTIFIERS, NEVER DECLARE ONE
//  ----------------------------------------------------------------------------------------------------
//  The refactor preserves the legacy SCREAMING_SNAKE constant spellings verbatim, because those exact
//  strings appear in serialized payloads, in log records and in characterization recordings, where a
//  rename would silently invalidate every stored comparison. The naming analyzer suppressions that make
//  those spellings buildable are scoped in the repository root .editorconfig to a fixed list of files
//  that DECLARE them, and this file is deliberately not on that list. With warnings treated as errors, a
//  single SCREAMING_SNAKE declaration here would be a BUILD ERROR rather than a style nit, and the
//  correct response to one would be to rename the offending member, never to widen that suppression
//  list.
//
//  So this file declares none. It CONSUMES the constants by name from the shared catalogue, its own
//  members carry ordinary PascalCase .NET names, and the preserved identifier strings it publishes for
//  the capabilities endpoint are produced with nameof over the catalogue constants themselves. That last
//  choice is load bearing rather than clever: there is exactly one source of truth for both the value
//  and the spelling, and renaming a catalogue constant breaks this file's compilation instead of quietly
//  drifting a wire payload. The underscore diagnostic reports DECLARATIONS, so a preserved identifier
//  appearing as a string VALUE is unaffected by it.
// ======================================================================================================

using PowerFramework.Gateway.Configuration;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Gateway.Composition;

/// <summary>
/// The resolved PowerFramework capability gate: an immutable 32-bit module gating bitmask, one read only
/// predicate per capability, and the preserved capability identifiers the composition root publishes.
/// </summary>
/// <remarks>
/// <para>
/// This is the managed counterpart of the flags argument the legacy framework's initialize entry point
/// accepts, <c>global function long pfwInitialize (readonly unsignedlong flags)</c> at
/// <c>ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L8</c>, whose only legacy call site is
/// <c>pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)</c> at <c>ws_objects/pfw.pbl.src/pfw.sra:L91</c>. The
/// eight bits it carries are declared at <c>ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48</c> under the
/// comment <c>//Initialize flags (pfwInitialize:[flags])</c> at <c>:L40</c>.
/// </para>
/// <para>
/// It is a pure function of the single mask it is constructed from: no configuration is read here, no
/// options monitor is injected, nothing is logged, no clock or environment variable is consulted, no I/O
/// is performed and no static mutable state exists. Two instances built from the same mask are equal, and
/// every member returns the same answer every time in any order in any process. That is what lets the
/// capability gate be tested with no web host and no fixture present.
/// </para>
/// <para>
/// It is also deliberately permissive, because the legacy is: the native entry point validates nothing
/// about the mask it receives. A zero mask, an all bits set mask, a negative configured value and a mask
/// carrying the unassigned bits 16, 32, 64 or 128 are all well defined inputs. Nothing about the value of
/// a mask can make construction throw, and unrecognised bits surface as data through
/// <see cref="UnrecognizedBits"/> rather than as an exception or as a silent correction of the mask.
/// </para>
/// <para>
/// Seven of the eight bits describe capability areas outside this phase: UI and DPIAWARE belong to the
/// deferred DesignSystem service, SCITER, BLINK, BLINKFAST and WEBVIEW to the deferred ScriptBridge
/// service, and ORCA to PowerBuilder packaging tooling, which is not a service at all. Those seven are
/// metadata about the eventual system and nothing more: representing a bit is not implementing the
/// capability behind it, and no client, handler, service class or placeholder exists for any of them.
/// <see cref="Sqlite"/> is the only bit with a consumer inside this phase, and that consumer is the
/// Persistence service rather than Gateway, so this type reports that bit and takes no action on it
/// either.
/// </para>
/// <para>
/// The type is a <see langword="readonly" /> <see langword="record" /> <see langword="struct" /> carrying
/// exactly one field, <see cref="EffectiveMask"/>. That is a deliberate shape. Value equality then means
/// mask equality, which is precisely the equality a caller wants; the language supplied
/// <see langword="default" /> value is the meaningful all capabilities off mask rather than an invalid
/// half built instance; and because no member caches anything, there is no uninitialized collection for
/// a defaulted instance to trip over.
/// </para>
/// </remarks>
public readonly record struct CapabilityFlags
{
    // ==================================================================================================
    //  THE EIGHT CAPABILITY BITS, WIDTH ADAPTED TO 32 BITS
    //  ------------------------------------------------------------------------------------------------
    //  Each of these consumes its catalogue constant BY NAME. None of them restates a numeric literal,
    //  so the values continue to live in exactly one place, and each initializer is a compile time
    //  constant expression whose conversion the compiler evaluates in a checked context: were a
    //  catalogue value ever to stop fitting 32 unsigned bits, that would be a compile error here rather
    //  than a silent truncation.
    //
    //  The cast exists because the two shared surfaces this type joins were each given the width its own
    //  legacy declaration demands. The catalogue declares the bits C# long, mirroring PowerScript
    //  Constant Long, a 32-bit SIGNED type. The bit helpers are authored against C# uint, mirroring the
    //  readonly unsignedlong parameter the mask is ultimately passed to. Neither is wrong; this is the
    //  seam, so the adaptation is stated here once, explicitly, instead of being sprinkled over call
    //  sites as implicit conversions that a reader would have to reconstruct.
    // ==================================================================================================

    /// <summary>
    /// The user interface capability bit: <see cref="Enums.INIT_FLAG_ENABLE_UI"/>, value 1,
    /// declared at <c>ws_objects/pfw.shared.pbl.src/enums.sru:L41</c>.
    /// </summary>
    /// <remarks>
    /// Metadata about the eventual system, not a capability this service implements. The capability area
    /// behind it is the deferred DesignSystem service, which has no project, no container and no code in
    /// this phase.
    /// </remarks>
    public const uint UiBit = (uint)Enums.INIT_FLAG_ENABLE_UI;

    /// <summary>
    /// The Sciter engine capability bit: <see cref="Enums.INIT_FLAG_ENABLE_SCITER"/>, value 2,
    /// declared at <c>ws_objects/pfw.shared.pbl.src/enums.sru:L42</c>.
    /// </summary>
    /// <remarks>
    /// Metadata about the eventual system, not a capability this service implements. The capability area
    /// behind it is the deferred ScriptBridge service, which has no project, no container and no code in
    /// this phase. <c>docs/README.md:L28</c> uses this very bit as its worked example of selective
    /// initialization, and warns at <c>:L30</c> that asking for it without <c>sciter.dll</c> present
    /// makes initialization fail outright.
    /// </remarks>
    public const uint SciterBit = (uint)Enums.INIT_FLAG_ENABLE_SCITER;

    /// <summary>
    /// The MiniBlink engine capability bit: <see cref="Enums.INIT_FLAG_ENABLE_BLINK"/>, value 4,
    /// declared at <c>ws_objects/pfw.shared.pbl.src/enums.sru:L43</c>.
    /// </summary>
    /// <remarks>
    /// Metadata about the eventual system, not a capability this service implements. The capability area
    /// behind it is the deferred ScriptBridge service. It pairs with <see cref="BlinkFastBit"/>: the two
    /// select between alternative builds of one engine rather than naming two engines, which is the
    /// reason <see cref="AllCapabilitiesMask"/> carries this bit and not that one.
    /// </remarks>
    public const uint BlinkBit = (uint)Enums.INIT_FLAG_ENABLE_BLINK;

    /// <summary>
    /// The fast MiniBlink build capability bit: <see cref="Enums.INIT_FLAG_ENABLE_BLINKFAST"/>, value 8,
    /// declared at <c>ws_objects/pfw.shared.pbl.src/enums.sru:L44</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Metadata about the eventual system, not a capability this service implements. The capability area
    /// behind it is the deferred ScriptBridge service.
    /// </para>
    /// <para>
    /// This is the one bit <see cref="AllCapabilitiesMask"/> deliberately omits, and the omission is
    /// correct legacy behaviour rather than a defect. It is still modelled here, and still predicable
    /// through <see cref="BlinkFast"/>, because <c>enums.sru:L44</c> declares it: a caller may ask for it
    /// explicitly even though the composite never does.
    /// </para>
    /// </remarks>
    public const uint BlinkFastBit = (uint)Enums.INIT_FLAG_ENABLE_BLINKFAST;

    /// <summary>
    /// The ORCA capability bit: <see cref="Enums.INIT_FLAG_ENABLE_ORCA"/>, value 256,
    /// declared at <c>ws_objects/pfw.shared.pbl.src/enums.sru:L45</c>.
    /// </summary>
    /// <remarks>
    /// Metadata about the eventual system, not a capability this service implements. Unlike the other six
    /// out of phase bits it does not name a deferred service at all: ORCA is the PowerBuilder library
    /// automation interface used by packaging tooling, which is permanently outside the scope of this
    /// decomposition rather than scheduled for a later phase. The bit is carried because
    /// <c>enums.sru:L45</c> declares it and <see cref="AllCapabilitiesMask"/> includes it.
    /// </remarks>
    public const uint OrcaBit = (uint)Enums.INIT_FLAG_ENABLE_ORCA;

    /// <summary>
    /// The SQLite capability bit: <see cref="Enums.INIT_FLAG_ENABLE_SQLITE"/>, value 512,
    /// declared at <c>ws_objects/pfw.shared.pbl.src/enums.sru:L46</c>.
    /// </summary>
    /// <remarks>
    /// The only one of the eight bits with a consumer inside this phase, and that consumer is the
    /// Persistence service rather than Gateway. This type therefore reports the bit and takes no action
    /// on it: it opens nothing, names no database and reaches no provider. The bit is a capability flag,
    /// not a database, and the sibling project file corroborates that structurally by referencing no
    /// storage package of any kind.
    /// </remarks>
    public const uint SqliteBit = (uint)Enums.INIT_FLAG_ENABLE_SQLITE;

    /// <summary>
    /// The DPI awareness capability bit: <see cref="Enums.INIT_FLAG_ENABLE_DPIAWARE"/>, value 1024,
    /// declared at <c>ws_objects/pfw.shared.pbl.src/enums.sru:L47</c>.
    /// </summary>
    /// <remarks>
    /// Metadata about the eventual system, not a capability this service implements. The capability area
    /// behind it is the deferred DesignSystem service, which owns the whole DPI to pixel conversion
    /// family; this phase ships no presentation surface at all.
    /// </remarks>
    public const uint DpiAwareBit = (uint)Enums.INIT_FLAG_ENABLE_DPIAWARE;

    /// <summary>
    /// The WebView capability bit: <see cref="Enums.INIT_FLAG_ENABLE_WEBVIEW"/>, value 2048,
    /// declared at <c>ws_objects/pfw.shared.pbl.src/enums.sru:L48</c>.
    /// </summary>
    /// <remarks>
    /// Metadata about the eventual system, not a capability this service implements. The capability area
    /// behind it is the deferred ScriptBridge service. This is the bit the legacy's alternative
    /// initializer object has no switch for, which is the asymmetry recorded in this file's header: the
    /// composite mask omits BLINKFAST while the switch set omits WEBVIEW, so the two sevens are different
    /// sevens and neither legacy mechanism alone expresses all eight capabilities.
    /// </remarks>
    public const uint WebViewBit = (uint)Enums.INIT_FLAG_ENABLE_WEBVIEW;

    // ==================================================================================================
    //  ASYMMETRY A, AT ITS POINT OF REPRODUCTION
    //  ------------------------------------------------------------------------------------------------
    //  THE COMPOSITE IS 3847. IT IS NOT 3855. THAT IS CORRECT AND IT IS NOT A TYPO.
    //
    //  enums.sru:L49 declares the composite as a SEVEN term sum over the EIGHT bits above:
    //
    //      Constant Long INIT_FLAG_ENABLE_ALL = INIT_FLAG_ENABLE_UI
    //                                         + INIT_FLAG_ENABLE_SCITER
    //                                         + INIT_FLAG_ENABLE_BLINK
    //                                         + INIT_FLAG_ENABLE_ORCA
    //                                         + INIT_FLAG_ENABLE_SQLITE
    //                                         + INIT_FLAG_ENABLE_DPIAWARE
    //                                         + INIT_FLAG_ENABLE_WEBVIEW
    //
    //      1 + 2 + 4 + 256 + 512 + 1024 + 2048  =  3847
    //
    //  INIT_FLAG_ENABLE_BLINKFAST (8) IS DELIBERATELY ABSENT. blink.dll and blinkfast.dll are two
    //  alternative BUILDS of one engine rather than two independent capabilities, so an "everything on"
    //  constant asking for both at once would be incoherent. Arriving at 3855 means the eighth bit was
    //  folded in and a deliberate legacy decision reversed; that is a behavioural change, not the repair
    //  of a typo (C-B).
    //
    //  The value is obtained by CONSUMING the catalogue composite rather than by re-summing the seven
    //  terms here or by writing 3847 as a literal. Both alternatives were rejected for the same reason:
    //  either would give the omission a second place to be got wrong, and a literal would additionally
    //  hide the reasoning behind a number that a later reader could only check by opening PowerScript.
    // ==================================================================================================

    /// <summary>
    /// The composite mask the legacy application actually initializes with:
    /// <see cref="Enums.INIT_FLAG_ENABLE_ALL"/>, declared at
    /// <c>ws_objects/pfw.shared.pbl.src/enums.sru:L49</c> and evaluating to <c>3847</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>3847</c>, and not <c>3855</c>. The legacy constant is written as a sum of SEVEN of the eight
    /// declared bits, and <see cref="Enums.INIT_FLAG_ENABLE_BLINKFAST"/> is deliberately excluded, because
    /// the standard and fast MiniBlink binaries are alternative builds of a single engine rather than two
    /// capabilities that could sensibly be enabled together. The omission looks like an oversight and is
    /// not one; a future reader who "corrects" it to <c>3855</c> changes behaviour.
    /// <see cref="KnownCapabilityMask"/> is the value that does span all eight bits, and it exists for an
    /// entirely different purpose.
    /// </para>
    /// <para>
    /// This is the default the composition root resolves when configuration overrides nothing, because
    /// <c>pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)</c> at <c>ws_objects/pfw.pbl.src/pfw.sra:L91</c> is
    /// the mask's only legacy consumer and this property is its successor.
    /// </para>
    /// </remarks>
    public const uint AllCapabilitiesMask = (uint)Enums.INIT_FLAG_ENABLE_ALL;

    /// <summary>
    /// The union of all eight declared capability bits, evaluating to <c>3855</c>. This is the vocabulary
    /// of the gate, and it is emphatically <b>not</b> <see cref="AllCapabilitiesMask"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Do not use this as an enable everything value.</b> It exists for exactly one purpose: to
    /// identify the bits that no <c>INIT_FLAG_ENABLE_</c> constant claims, which is how
    /// <see cref="UnrecognizedBits"/> is computed. The mask that expresses the legacy's own idea of
    /// everything on is <see cref="AllCapabilitiesMask"/>, which is <c>3847</c> because it excludes
    /// <see cref="Enums.INIT_FLAG_ENABLE_BLINKFAST"/>.
    /// </para>
    /// <para>
    /// The difference between the two values is therefore precisely
    /// <see cref="Enums.INIT_FLAG_ENABLE_BLINKFAST"/>, and the fact that they differ at all is the whole
    /// content of the legacy quirk this file preserves. Comparing them is the clearest available
    /// expression of that quirk in a test, and considerably clearer than asserting against a bare
    /// <c>3855</c>.
    /// </para>
    /// <para>
    /// Composed with bitwise OR rather than by addition. The legacy sums its masks, and addition happens
    /// to agree with OR while the combined bits are disjoint, which they are here; OR is used anyway
    /// because it states the intent and cannot carry.
    /// </para>
    /// </remarks>
    public const uint KnownCapabilityMask = UiBit
                                          | SciterBit
                                          | BlinkBit
                                          | BlinkFastBit
                                          | OrcaBit
                                          | SqliteBit
                                          | DpiAwareBit
                                          | WebViewBit;

    // ==================================================================================================
    //  THE PRESERVED CAPABILITY IDENTIFIERS, AS PUBLISHED PAYLOAD STRINGS
    //  ------------------------------------------------------------------------------------------------
    //  The capabilities endpoint carries the eight capabilities BY THEIR PRESERVED LEGACY IDENTIFIERS,
    //  because those exact spellings appear in serialized payloads, in log records and in
    //  characterization recordings. Each string below is produced with nameof over the catalogue constant
    //  it names, which buys two things a string literal would not:
    //
    //      ONE SOURCE OF TRUTH   the spelling is derived from the declaration, so value and spelling can
    //                            never drift apart
    //      A BUILD BREAK, NOT A  renaming a catalogue constant fails compilation here instead of quietly
    //      SILENT WIRE CHANGE    changing what a wire payload says
    //
    //  These are C# declarations with ordinary PascalCase identifiers whose VALUES contain underscores.
    //  The underscore naming diagnostic reports declarations, not string contents, so nothing here needs
    //  or receives an analyzer suppression, and this file declares no SCREAMING_SNAKE member of its own.
    // ==================================================================================================

    private const string UiCapabilityName = nameof(Enums.INIT_FLAG_ENABLE_UI);
    private const string SciterCapabilityName = nameof(Enums.INIT_FLAG_ENABLE_SCITER);
    private const string BlinkCapabilityName = nameof(Enums.INIT_FLAG_ENABLE_BLINK);
    private const string BlinkFastCapabilityName = nameof(Enums.INIT_FLAG_ENABLE_BLINKFAST);
    private const string OrcaCapabilityName = nameof(Enums.INIT_FLAG_ENABLE_ORCA);
    private const string SqliteCapabilityName = nameof(Enums.INIT_FLAG_ENABLE_SQLITE);
    private const string DpiAwareCapabilityName = nameof(Enums.INIT_FLAG_ENABLE_DPIAWARE);
    private const string WebViewCapabilityName = nameof(Enums.INIT_FLAG_ENABLE_WEBVIEW);

    /// <summary>
    /// The number of capability bits the gate declares, which is eight
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48</c>].
    /// </summary>
    /// <remarks>
    /// Published so that the two name collections below can size themselves exactly, and so that a test
    /// asserting the gate's arity has a member to assert against rather than a literal. It counts
    /// DECLARED bits, so it is eight even though the composite at <c>enums.sru:L49</c> names only seven of
    /// them and the legacy's alternative initializer object exposes a different seven.
    /// </remarks>
    public const int DeclaredCapabilityCount = 8;

    // ==================================================================================================
    //  STATE AND CONSTRUCTION
    //  ------------------------------------------------------------------------------------------------
    //  Exactly one field, and the constructor's only job is to store it. That is what makes this type a
    //  pure function of its input mask: there is nothing else to be inconsistent with, nothing computed
    //  eagerly that could go stale, and no collection that a defaulted instance could leave
    //  uninitialized. Record value equality reduces to mask equality for the same reason.
    // ==================================================================================================

    /// <summary>
    /// Initializes a new capability gate over an already 32-bit wide mask.
    /// </summary>
    /// <param name="effectiveMask">
    /// The module gating bitmask, in the same 32-bit unsigned width the legacy native entry point
    /// declares at <c>ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L8</c>. Any value is accepted,
    /// including zero, <see cref="uint.MaxValue"/> and values carrying the unassigned bits 16, 32, 64
    /// and 128; see the class remarks for why no validation is applied.
    /// </param>
    /// <remarks>
    /// Use <see cref="FromConfiguredValue(long)"/> when starting from the configured value, which is
    /// declared <see cref="long"/>, so that the narrowing is performed once in a documented place rather
    /// than at each call site.
    /// </remarks>
    public CapabilityFlags(uint effectiveMask)
    {
        EffectiveMask = effectiveMask;
    }

    /// <summary>
    /// The resolved 32-bit module gating bitmask, ready to hand to the framework initialize boundary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <see cref="uint"/>, and never a 64-bit type. The legacy parameter is
    /// <c>readonly unsignedlong flags</c> [<c>ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L8</c>], and
    /// PowerBuilder's <c>unsignedlong</c> is 32 bits wide, so <see cref="uint"/> is the exact width rather
    /// than a convenient approximation of it. No wider type appears anywhere on the path from
    /// configuration to that boundary.
    /// </para>
    /// <para>
    /// This is the whole state of the instance. Every other member on the type is derived from it, which
    /// is why two gates built from the same mask compare equal and why the language supplied
    /// <see langword="default" /> value is simply <see cref="None"/>.
    /// </para>
    /// </remarks>
    public uint EffectiveMask { get; }

    /// <summary>
    /// The gate the legacy application initializes with: <see cref="AllCapabilitiesMask"/>, which is
    /// <c>3847</c> because <see cref="Enums.INIT_FLAG_ENABLE_BLINKFAST"/> is deliberately excluded.
    /// </summary>
    /// <remarks>
    /// The successor to <c>pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)</c> at
    /// <c>ws_objects/pfw.pbl.src/pfw.sra:L91</c>, and the value the configuration property defaults to
    /// when nothing overrides it. <see cref="BlinkFast"/> is <see langword="false"/> on this instance,
    /// which is the legacy quirk preserved rather than a gap to close.
    /// </remarks>
    public static CapabilityFlags All => new(AllCapabilitiesMask);

    /// <summary>
    /// The empty gate, with no capability enabled.
    /// </summary>
    /// <remarks>
    /// Equal to the language supplied <see langword="default" /> value of the type, which is a property of
    /// the single field shape rather than a coincidence: a defaulted instance is a meaningful all off gate
    /// and never an invalid one. It is a legitimate configured value too, since
    /// <c>pfwInitialize()</c> at <c>ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L7</c> is an overload
    /// taking no flags at all, and <c>docs/README.md:L9</c> records the flags argument as optional.
    /// </remarks>
    public static CapabilityFlags None => new(0u);

    /// <summary>
    /// Resolves a capability gate from the configured mask value.
    /// </summary>
    /// <param name="configuredValue">
    /// The mask as configuration supplies it, matching the declared type of
    /// <see cref="GatewayOptions.CapabilityFlags"/> and of the catalogue constants it is composed from.
    /// </param>
    /// <returns>The gate described by that value. This never fails and never throws.</returns>
    /// <remarks>
    /// <para>
    /// This is the type's single width adaptation point, and the conversion is written out explicitly by
    /// design. The configured value is <see cref="long"/> because the catalogue constants are
    /// <see cref="long"/>, mirroring the PowerScript <c>Constant Long</c> declarations at
    /// <c>ws_objects/pfw.shared.pbl.src/enums.sru:L41-L49</c>; the mask handed onward is
    /// <see cref="uint"/> because the native parameter is <c>readonly unsignedlong</c>, a 32-bit unsigned
    /// type, at <c>ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L8</c>. The legacy performs exactly this
    /// same signed to unsigned handover, and it performs no check while doing it.
    /// </para>
    /// <para>
    /// The conversion is therefore unchecked and keeps the low 32 bits, which reproduces the legacy
    /// handover rather than improving on it: a configured <c>-1</c> resolves to <c>0xFFFFFFFF</c>, and a
    /// configured <c>4294967296</c> resolves to <c>0</c>. <see langword="unchecked" /> is stated in the
    /// source rather than relied upon as the compiler's default context, so that enabling arithmetic
    /// overflow checks for the project could never turn a configured value into an exception at startup.
    /// Every declared capability value fits 32 bits comfortably, so no ordinary configuration is affected
    /// by any of this; the behaviour is defined here only so that an extraordinary one has a defined
    /// answer instead of a crash.
    /// </para>
    /// </remarks>
    public static CapabilityFlags FromConfiguredValue(long configuredValue)
    {
        return new CapabilityFlags(unchecked((uint)configuredValue));
    }

    /// <summary>
    /// Resolves a capability gate from the Gateway service's bound configuration.
    /// </summary>
    /// <param name="options">
    /// The bound options whose <see cref="GatewayOptions.CapabilityFlags"/> value supplies the mask.
    /// </param>
    /// <returns>The gate described by that configuration. Never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A one line projection, provided so that the composition root's wiring reads as the single
    /// expression it is, and so that the configuration key behind the gate,
    /// <c>Gateway:CapabilityFlags</c>, is discoverable from this type. It reads one already bound property
    /// and nothing else: no configuration provider is consulted here, no options monitor is injected, and
    /// no value is cached, so the type remains a pure function of the mask it ends up with. The null guard
    /// covers a null reference rather than an unwelcome mask value; no mask value is unwelcome.
    /// </remarks>
    public static CapabilityFlags FromOptions(GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return FromConfiguredValue(options.CapabilityFlags);
    }

    // ==================================================================================================
    //  THE EIGHT PREDICATES
    //  ------------------------------------------------------------------------------------------------
    //  All eight route through IsEnabled, which routes through the shared bit helper. Nothing here
    //  hand rolls a mask test with an operator or a shift: the helpers are the managed substitutes for
    //  the legacy PowerBuilder Native Interface bit primitives, they are the code the legacy's own bit
    //  behaviour was characterized into, and going around them would put a second, uncharacterized
    //  implementation of the same operation in the tree.
    // ==================================================================================================

    /// <summary>
    /// Reports whether the resolved gate enables the capability named by a bit mask.
    /// </summary>
    /// <param name="capabilityBit">
    /// The capability bit to test, as a VALUE and not as a bit position. Pass
    /// <see cref="SqliteBit"/> rather than <c>9</c>, or cast a catalogue constant such as
    /// <see cref="Enums.INIT_FLAG_ENABLE_SQLITE"/> to <see cref="uint"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when at least one bit of <paramref name="capabilityBit"/> is set in
    /// <see cref="EffectiveMask"/>; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The underlying shared helper has ANY bit semantics, so passing a multi bit mask asks whether ANY of
    /// those capabilities is enabled and not whether all of them are. Each of the eight named predicates
    /// passes a single bit, where the distinction does not arise; it is documented because it does arise
    /// for a caller composing a mask of its own.
    /// </para>
    /// <para>
    /// An empty mask returns <see langword="false"/>, since no bit can be shared with it, and an
    /// unassigned or otherwise unrecognised bit is answered on its merits rather than rejected. That is
    /// the permissive posture the legacy has; see <see cref="UnrecognizedBits"/> for how such bits are
    /// surfaced as data.
    /// </para>
    /// </remarks>
    public bool IsEnabled(uint capabilityBit)
    {
        return Bits.BitTest(EffectiveMask, capabilityBit);
    }

    /// <summary>
    /// Whether the user interface capability is enabled: <see cref="UiBit"/>, value 1
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L41</c>].
    /// </summary>
    /// <remarks>
    /// Metadata about the eventual system, not a capability this service implements. The area behind it is
    /// the deferred DesignSystem service, so a <see langword="true"/> answer here reports what
    /// configuration asked for and grants access to nothing. Named <c>Ui</c> rather than in the legacy's
    /// spelling for the same reason every member of this type is: the preserved SCREAMING_SNAKE
    /// identifiers are consumed from the shared catalogue and published as payload strings, never
    /// redeclared here.
    /// </remarks>
    public bool Ui => IsEnabled(UiBit);

    /// <summary>
    /// Whether the Sciter engine capability is enabled: <see cref="SciterBit"/>, value 2
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L42</c>].
    /// </summary>
    /// <remarks>
    /// Metadata about the eventual system, not a capability this service implements. The area behind it is
    /// the deferred ScriptBridge service.
    /// </remarks>
    public bool Sciter => IsEnabled(SciterBit);

    /// <summary>
    /// Whether the MiniBlink engine capability is enabled: <see cref="BlinkBit"/>, value 4
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L43</c>].
    /// </summary>
    /// <remarks>
    /// Metadata about the eventual system, not a capability this service implements. The area behind it is
    /// the deferred ScriptBridge service. This is the MiniBlink bit that
    /// <see cref="AllCapabilitiesMask"/> does include; <see cref="BlinkFast"/> is the one it does not.
    /// </remarks>
    public bool Blink => IsEnabled(BlinkBit);

    /// <summary>
    /// Whether the fast MiniBlink build capability is enabled: <see cref="BlinkFastBit"/>, value 8
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L44</c>].
    /// </summary>
    /// <remarks>
    /// Metadata about the eventual system, not a capability this service implements. The area behind it is
    /// the deferred ScriptBridge service. This predicate is <see langword="false"/> on <see cref="All"/>,
    /// and that is the point: <see cref="AllCapabilitiesMask"/> omits this one bit deliberately, because
    /// the standard and fast MiniBlink binaries are alternative builds of one engine. Enabling it requires
    /// naming it explicitly in configuration, exactly as the legacy requires.
    /// </remarks>
    public bool BlinkFast => IsEnabled(BlinkFastBit);

    /// <summary>
    /// Whether the ORCA capability is enabled: <see cref="OrcaBit"/>, value 256
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L45</c>].
    /// </summary>
    /// <remarks>
    /// Metadata about the eventual system, not a capability this service implements, and the one bit of
    /// the eight that names no service at any phase: ORCA is PowerBuilder packaging tooling, permanently
    /// outside this decomposition.
    /// </remarks>
    public bool Orca => IsEnabled(OrcaBit);

    /// <summary>
    /// Whether the SQLite capability is enabled: <see cref="SqliteBit"/>, value 512
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L46</c>].
    /// </summary>
    /// <remarks>
    /// The only one of the eight capabilities with a consumer inside this phase, and that consumer is the
    /// Persistence service rather than Gateway. This predicate therefore reports the configured bit and
    /// grants nothing: no connection is opened, no database is named and no provider is reached from this
    /// type. The bit is a capability flag, not a database.
    /// </remarks>
    public bool Sqlite => IsEnabled(SqliteBit);

    /// <summary>
    /// Whether the DPI awareness capability is enabled: <see cref="DpiAwareBit"/>, value 1024
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L47</c>].
    /// </summary>
    /// <remarks>
    /// Metadata about the eventual system, not a capability this service implements. The area behind it is
    /// the deferred DesignSystem service, which owns the DPI to pixel conversion family; this phase ships
    /// no presentation surface at all.
    /// </remarks>
    public bool DpiAware => IsEnabled(DpiAwareBit);

    /// <summary>
    /// Whether the WebView capability is enabled: <see cref="WebViewBit"/>, value 2048
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L48</c>].
    /// </summary>
    /// <remarks>
    /// Metadata about the eventual system, not a capability this service implements. The area behind it is
    /// the deferred ScriptBridge service. This is the capability the legacy's alternative initializer
    /// object cannot express at all, having no switch for it, which is why the two legacy gating
    /// mechanisms are not interchangeable and why this type is modelled on the eight bit mask.
    /// </remarks>
    public bool WebView => IsEnabled(WebViewBit);

    // ==================================================================================================
    //  UNRECOGNISED BITS - REPORTED AS DATA, NEVER AS AN EXCEPTION AND NEVER BY CORRECTING THE MASK
    //  ------------------------------------------------------------------------------------------------
    //  Bit positions 4 through 7, values 16, 32, 64 and 128, are unassigned: the declared values run
    //  1, 2, 4, 8 and then jump to 256, and an exhaustive search of the estate finds exactly nine
    //  INIT_FLAG_ identifiers, so nothing claims that range. Anything above 2048 is unclaimed as well.
    //
    //  The legacy accepts such a mask silently, so this type accepts it too. It neither throws nor clears
    //  the bits: clearing them would mean handing the native boundary a different mask than the one
    //  configuration asked for, which is a behavioural change performed invisibly. Reporting them lets the
    //  composition root and the capabilities endpoint surface a probable configuration mistake while the
    //  gate itself still behaves exactly as the legacy would.
    // ==================================================================================================

    /// <summary>
    /// The bits of <see cref="EffectiveMask"/> that no declared capability claims.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The unassigned positions are values 16, 32, 64 and 128, which sit in the gap between
    /// <see cref="BlinkFastBit"/> (8) and <see cref="OrcaBit"/> (256), plus everything above
    /// <see cref="WebViewBit"/> (2048). No <c>INIT_FLAG_ENABLE_</c> constant exists for any of them
    /// anywhere in the legacy estate, so this type invents no meaning for them and reserves no name in the
    /// gap.
    /// </para>
    /// <para>
    /// Purely diagnostic, and purely data. The value is a report, not a fault: <see cref="EffectiveMask"/>
    /// still carries these bits verbatim, they are still answered on their merits by
    /// <see cref="IsEnabled(uint)"/>, and nothing here throws or silently rewrites the mask. Returns
    /// <c>0</c> when every set bit is a declared capability.
    /// </para>
    /// </remarks>
    public uint UnrecognizedBits => Bits.BitAnd(EffectiveMask, Bits.BitNot(KnownCapabilityMask));

    /// <summary>
    /// Whether <see cref="EffectiveMask"/> carries any bit that no declared capability claims.
    /// </summary>
    /// <remarks>
    /// A convenience over <see cref="UnrecognizedBits"/> for the common case of wanting to report the
    /// condition rather than inspect it. Like that member it is diagnostic only: a <see langword="true"/>
    /// answer changes nothing about how the gate behaves, and is not an error.
    /// </remarks>
    public bool HasUnrecognizedBits => Bits.BitTest(EffectiveMask, Bits.BitNot(KnownCapabilityMask));

    // ==================================================================================================
    //  THE PUBLISHED NAME PROJECTIONS
    //  ------------------------------------------------------------------------------------------------
    //  Both collections are built in enums.sru declaration order, which makes the sparse bit layout and
    //  the two asymmetries legible in any payload they appear in, and makes the output of a given mask
    //  byte for byte reproducible. They are built on each access rather than cached, which keeps this type
    //  free of static state and keeps a defaulted instance safe; there is no collection field for
    //  default(CapabilityFlags) to leave uninitialized.
    //
    //  Eight explicit conditionals rather than a table walk. A private static array of name and bit pairs
    //  would be shorter and would introduce exactly the thing this type is documented not to have, a piece
    //  of static state whose elements are writable, in exchange for hiding the one ordering that has to be
    //  auditable against the legacy source.
    // ==================================================================================================

    /// <summary>
    /// The preserved legacy identifiers of the capabilities this gate enables, in
    /// <c>ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48</c> declaration order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The strings are the legacy constant spellings, <c>INIT_FLAG_ENABLE_UI</c> through
    /// <c>INIT_FLAG_ENABLE_WEBVIEW</c>, because the published capabilities contract carries the eight
    /// capabilities by those preserved identifiers, and because those exact spellings appear in serialized
    /// payloads, log records and characterization recordings where a rename would silently invalidate every
    /// stored comparison. Each is derived with <see langword="nameof" /> from the catalogue constant it
    /// names, so the spelling cannot drift from the declaration.
    /// </para>
    /// <para>
    /// Empty for <see cref="None"/>. Seven entries for <see cref="All"/>, not eight, because
    /// <see cref="AllCapabilitiesMask"/> omits <see cref="BlinkFastBit"/>. Unrecognised bits contribute no
    /// entry, since they have no identifier to report; <see cref="UnrecognizedBits"/> is where they surface.
    /// </para>
    /// <para>
    /// A fresh collection each time, and never <see langword="null"/>, including on a defaulted instance.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> EnabledCapabilityNames
    {
        get
        {
            List<string> names = new(DeclaredCapabilityCount);

            if (Ui)
            {
                names.Add(UiCapabilityName);
            }

            if (Sciter)
            {
                names.Add(SciterCapabilityName);
            }

            if (Blink)
            {
                names.Add(BlinkCapabilityName);
            }

            if (BlinkFast)
            {
                names.Add(BlinkFastCapabilityName);
            }

            if (Orca)
            {
                names.Add(OrcaCapabilityName);
            }

            if (Sqlite)
            {
                names.Add(SqliteCapabilityName);
            }

            if (DpiAware)
            {
                names.Add(DpiAwareCapabilityName);
            }

            if (WebView)
            {
                names.Add(WebViewCapabilityName);
            }

            return names;
        }
    }

    /// <summary>
    /// The preserved legacy identifiers of all <see cref="DeclaredCapabilityCount"/> declared
    /// capabilities, in <c>ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48</c> declaration order,
    /// irrespective of any mask.
    /// </summary>
    /// <remarks>
    /// The vocabulary of the gate, so that a caller projecting the capability surface can describe every
    /// capability and its state rather than only the enabled subset. All eight appear, including
    /// <c>INIT_FLAG_ENABLE_BLINKFAST</c>, which no composite enables by default, and
    /// <c>INIT_FLAG_ENABLE_WEBVIEW</c>, which the legacy's alternative initializer object cannot express at
    /// all. A fresh collection each time, and never <see langword="null"/>.
    /// </remarks>
    public static IReadOnlyList<string> KnownCapabilityNames =>
    [
        UiCapabilityName,
        SciterCapabilityName,
        BlinkCapabilityName,
        BlinkFastCapabilityName,
        OrcaCapabilityName,
        SqliteCapabilityName,
        DpiAwareCapabilityName,
        WebViewCapabilityName,
    ];
}
