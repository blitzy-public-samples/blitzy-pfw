// ======================================================================================================
// GatewayOptions.cs
// The Gateway service's bound configuration surface.
// ======================================================================================================
//
// WHAT THIS FILE IS
//   Every value the legacy PowerBuilder framework nailed into its application object arrives here
//   instead, as an options type bound from configuration. The options pattern is the mechanism this
//   refactor uses to discharge configuration and secret injection, so this type is not a convenience
//   wrapper over IConfiguration: it IS the Gateway's declared configuration contract, and it is the
//   only place that contract is stated in code.
//
//   Three types live in this one file, and that is deliberate rather than untidy. The scope of this
//   folder is exactly one file, so a companion validator file, a separate upstream file and a separate
//   JWT file are all forbidden; the constraint is one FILE, not one TYPE. Each type below binds a
//   different configuration shape and is documented with the shape it binds.
//
// THE KEY CONTRACT IS TAKEN FROM appsettings.json, NOT FROM PROSE
//   A property whose name does not match its configuration key binds to nothing, silently: no
//   exception, no log entry, just a default that looks deliberate. Every name below was therefore
//   copied from the sibling appsettings.json rather than inferred, and the two shapes it declares are
//   reproduced exactly:
//
//     "Gateway": {                                  <-- GatewayOptions, section name "Gateway"
//       "Locale": "en",
//       "CapabilityFlags": 3847,
//       "Upstreams": {                              <-- UpstreamAddresses, nested
//         "DataServices": "https://localhost:5112",  <-- the gRPC endpoint, not the REST one
//         "Security":     "https://localhost:5104"
//       }
//     }
//
//     "Authentication": { "Schemes": { "Bearer": {  <-- JwtBearerVerificationOptions
//       "RequireHttpsMetadata": true,               <-- a SEPARATE root, not a child of "Gateway"
//       "ValidAudiences": [ "powerframework-gateway" ],
//       "MapInboundClaims": false
//     } } }
//
//   THE BEARER SHAPE IS SPLIT ACROSS TWO FILES, AND THE SPLIT IS A SECURITY BOUNDARY RATHER THAN
//   TIDINESS. The base appsettings.json above carries only settings that are safe in EVERY
//   environment, because that is exactly where base settings load. It deliberately declares NO
//   authority and NO issuer, because either would be one topology's address presented as an estate
//   default. The loopback pair lives in appsettings.Development.json and nowhere else:
//
//     "Authentication": { "Schemes": { "Bearer": {  <-- appsettings.Development.json ONLY
//       "Authority":    "https://localhost:5104",
//       "ValidIssuers": [ "https://localhost:5104" ],
//       "RequireHttpsMetadata": true
//     } } }
//
//   THE OVERLAY CARRIES THE AUTHORITY IT EXISTS FOR AND CARRIES NO RELAXATION AT ALL. Security binds
//   ONE TLS LISTENER in every environment, for the reason recorded on the scheme note below, so the
//   loopback authority is https and RequireHttpsMetadata stays true beside it - the pair is coherent in
//   both files and the validator has nothing to refuse. What the overlay changes is the HOST and only
//   the host. The base file keeps the strict value and declares no authority at all, so a deployment
//   that configures nothing fails to start naming the missing authority rather than inheriting either a
//   relaxation or a guessed address.
//
//   So an environment that configures nothing gets the strict shape and fails to start naming the
//   missing authority, instead of inheriting a relaxation it never asked for. The binding contract is
//   unchanged either way: the same keys, the same spellings, the same path.
//
//   THE SCHEME IS https, AND ON THE ISSUANCE EDGE THE TRANSPORT IS PART OF THE SECURITY ARGUMENT.
//   Every request Gateway sends to Security on this edge carries a caller credential, and every response
//   carries either a bearer token or the key set the whole estate trusts. On cleartext all three are
//   observable and the key set is substitutable by anyone on path, which is CWE-319 on the one edge where
//   it costs the most. AAP 0.1.4 settles the reading: the requirement cannot mean "no new surface" -
//   decomposition creates the system's first-ever ingress - it means every newly created surface is
//   authenticated from the outset, and an authenticated surface whose channel is readable authenticates
//   nothing an observer cannot replay. The certificate and key come from the deployment's secret layer
//   exactly as the signing key does; none is committed here (constraint C-F). AAP 0.6.6.3 is about
//   CALLER IDENTITY rather than about the channel: mutual TLS is the documented FALLBACK "for any pair
//   where a token issuer is inappropriate, adding certificate and key
//   path settings for that pair only", not the transport of the estate. What authenticates the issuance
//   edge instead is the CLIENT CREDENTIAL Gateway presents, which Security checks against its own
//   roster; a client certificate is still honoured wherever a deployment terminates TLS and presents
//   one. What plaintext WOULD have cost is stated rather than glossed, because it is the reason none is
//   declared: on a readable channel the published key set is substitutable and a bearer token replayable
//   by anyone on path. That exposure is closed here rather than accepted, and every address nonetheless
//   remains an environment override rather than a compiled-in decision, so a deployment can move a host
//   without editing code.
//
//   The JWT settings live under the stock "Authentication:Schemes:Bearer" path because that is the
//   path the framework's own JWT bearer handler binds itself, which is precisely the "zero bespoke
//   security code" property this refactor wants. Modelling them as a child of "Gateway" would have
//   produced a type that compiles, passes a naive default-value test, and binds absolutely nothing.
//   JwtBearerVerificationOptions is therefore a second top-level type rather than a nested one: it
//   binds a different configuration root, and nesting it inside GatewayOptions would falsely imply it
//   is part of the "Gateway" section.
//
// EVERYTHING HERE IS OVERRIDABLE BY ENVIRONMENT VARIABLE
//   The double underscore convention maps onto the nesting above, so the orchestration manifest can
//   supply per-environment values without editing source:
//
//     Gateway__Locale                            Gateway__Upstreams__DataServices
//     Gateway__CapabilityFlags                   Gateway__Upstreams__Security
//     Authentication__Schemes__Bearer__Authority
//     Authentication__Schemes__Bearer__ValidIssuers__0
//     Authentication__Schemes__Bearer__ValidAudiences__0
//
//   Nothing that carries a bindable value is a const or a computed read-only member, because either
//   would be a value configuration cannot displace. The two SectionName constants are the sole
//   exception and are not an exception at all: they name a section rather than carry a value from one.
//
// LEGACY REFERENCE (read only: the behavioural oracle, never edited, never built, never shipped)
//   ws_objects/pfw.pbl.src/pfw.sra is the composition-root reference, and its open event is the direct
//   ancestor of this type:
//
//     L88  event open;string lang        lang is a LOCAL variable, so the value below is structurally
//                                       un-configurable in the legacy
//     L91  pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)      -> CapabilityFlags
//     L93  //多语言支持                                     ("multilingual support")
//     L94  lang = "en"                                    -> Locale, THE PRESERVED DEFECT
//     L95  choose case lang ... case "en" / "chs" / "cht"  -> the three legal tokens
//     L103 I18N(locale)                                   -> the provider install, done by Program.cs
//
//   Two halves of that translation both matter. The hardcoded value is reproduced as the DEFAULT, so
//   observable behaviour is byte identical to the legacy. It is also made OVERRIDABLE, so the
//   un-configurability - a structural property rather than a behaviour - is not re-created.
//
// WHAT THIS TYPE DELIBERATELY DOES NOT CARRY
//   Recorded so each omission reads as a decision rather than an oversight, and so nobody "completes"
//   this file by adding one of them. Each is expanded at the point it would otherwise have appeared.
//
//     * No upstream address for the storage-owning service that sits beneath DataServices. Gateway
//       does not call it; DataServices does. See UpstreamAddresses for why that is an invariant.
//     * No signing key, secret, password, certificate or private key of any kind. Gateway validates
//       tokens and never mints them.
//     * No connection string and no storage, SQLite or EF Core option. Exactly one service in this
//       system holds a storage provider, and it is not this one.
//     * No option, flag, address or placeholder for any capability deferred out of this phase.
//     * No path or probe for the repository root localization resource table. The localization
//       provider opens that file by bare relative filename with no configurability at all, and the
//       image build context excludes the legacy tree, so at run time the file is simply absent and
//       its absence is exactly what produces the preserved silent-passthrough translation behaviour.
//       Inventing a path setting would invent configurability the legacy never had.
//     * No listening port. Where this service listens is host and orchestration configuration, not
//       application configuration, so it is absent here by design.
//     * No logging or telemetry options type. The host binds the "Logging" section itself.
//     * No retry count, timeout budget, latency target or availability figure. The repository
//       publishes no such budget anywhere, so none may be asserted here or anywhere else.
//
// ======================================================================================================

using System.ComponentModel.DataAnnotations;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Gateway.Configuration;

/// <summary>
/// The Gateway service's configuration, bound from the <c>Gateway</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// This is the successor to the legacy framework application object's open event, which hardcoded
/// both of the scalar values below into local variables. Each is reproduced here as a default, which
/// preserves the legacy's observable behaviour, and each is bindable, which removes the legacy's
/// un-configurability without changing what the software does when nothing overrides it.
/// </para>
/// <para>
/// The type is a plain sealed class with settable properties and a working parameterless construction
/// path. That shape is required twice over: the configuration binder populates public settable
/// properties, and a test must be able to construct an instance and assert its defaults with no
/// configuration provider and no host present. It holds no static mutable state, performs no I/O,
/// reads no clock and reads no environment variable, so constructing it has no side effect.
/// </para>
/// </remarks>
public sealed class GatewayOptions : IValidatableObject
{
    /// <summary>
    /// The configuration section this type binds: <c>Gateway</c>.
    /// </summary>
    /// <remarks>
    /// Exposed so the section name is stated once, here, rather than repeated as a bare string at
    /// every binding site. This is the only constant in the file that is legitimate, and it is
    /// legitimate precisely because it names a section instead of carrying a value bound from one:
    /// every actual value in this file must remain displaceable by configuration.
    /// </remarks>
    public const string SectionName = "Gateway";

    /// <summary>
    /// The locale token selecting which localization provider the composition root installs. One of
    /// <c>en</c>, <c>chs</c> or <c>cht</c>. Defaults to <c>en</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DEFAULT IS A PRESERVED LEGACY DEFECT, REPRODUCED ON PURPOSE.
    /// <c>ws_objects/pfw.pbl.src/pfw.sra:L94</c> reads <c>lang = "en"</c>, assigning a hardcoded
    /// literal to a local variable declared at <c>:L88</c>. Because the variable is local, the legacy
    /// locale cannot be configured at all. The defect is the hardcoding, and this default value is its
    /// faithful reproduction: with nothing configured, this service selects the English provider
    /// exactly as the legacy did. Do not "correct" it and do not raise it as a finding - correcting a
    /// documented legacy behaviour is out of bounds for this refactor.
    /// </para>
    /// <para>
    /// The three legal tokens come from the <c>choose case</c> at <c>pfw.sra:L95</c> to <c>:L102</c>,
    /// whose arms are <c>"en"</c>, <c>"chs"</c> and <c>"cht"</c>. They are selectors for concrete
    /// provider classes, NOT culture identifiers, so a BCP-47 name such as <c>en-US</c>,
    /// <c>zh-Hans</c> or <c>zh-TW</c> is rejected: no provider corresponds to it. Matching is
    /// case-sensitive and ordinal because the legacy <c>choose case</c> on a string is, and no culture
    /// auto-detection or system-culture fallback exists here for the same reason - the legacy performed
    /// neither, and adding either would change behaviour rather than preserve it.
    /// </para>
    /// <para>
    /// REJECTING AN UNKNOWN TOKEN IS NOT AN IMPROVEMENT OVER THE LEGACY. The legacy
    /// <c>choose case</c> has no <c>case else</c> arm, so an unknown token would leave its provider
    /// variable null and pass null to the install call at <c>:L103</c>. That arm is unreachable by
    /// construction, because <c>:L94</c> hardcodes a token the switch handles, so it has no observable
    /// behaviour to preserve. Making the value overridable is what first makes that state reachable,
    /// which is why the whitelist below belongs to the fail-fast posture rather than to behaviour
    /// change: an unknown token stops the host from starting instead of being coerced back to
    /// <c>en</c> or degrading into a null provider at first request.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    [AllowedValues("en", "chs", "cht")]
    public string Locale { get; set; } = "en";

    /// <summary>
    /// The capability bitmask gating which framework modules the composition root initializes.
    /// Defaults to <see cref="Enums.INIT_FLAG_ENABLE_ALL"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy call site is <c>pfw.sra:L91</c>, <c>pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)</c>,
    /// and it is the only consumer of that constant anywhere in the legacy tree. This property is its
    /// sole successor, which is why the default is the whole mask rather than some narrower subset
    /// chosen to match the services that exist in this phase.
    /// </para>
    /// <para>
    /// The default CONSUMES <see cref="Enums.INIT_FLAG_ENABLE_ALL"/> rather than restating its value.
    /// That keeps one source of truth and preserves something a literal would hide: the constant is a
    /// seven-term sum over eight declared bits, because
    /// <see cref="Enums.INIT_FLAG_ENABLE_BLINKFAST"/> is deliberately excluded - the fast and standard
    /// engine builds it distinguishes are alternative builds of a single engine, so enabling both is
    /// meaningless. Writing the arithmetic out here, or hardcoding the total, would silently drop that
    /// distinction the first time either side changed.
    /// </para>
    /// <para>
    /// The property is declared <see cref="long"/> to match the declared type of the constants it
    /// consumes, which in turn matches the 32-bit signed integer the legacy declared, and it binds
    /// correctly from a JSON number. No validation is applied to it, and that is a decision rather
    /// than an omission: the legacy initialize call validates nothing about the mask it receives, so a
    /// rule rejecting an unknown or negative bit pattern would be new behaviour. This differs from the
    /// locale case above, where the legacy's unknown-token path was unreachable and therefore had no
    /// behaviour to preserve; here the permissive behaviour is real, reachable, and preserved.
    /// </para>
    /// </remarks>
    public long CapabilityFlags { get; set; } = Enums.INIT_FLAG_ENABLE_ALL;

    /// <summary>
    /// The addresses of the two services Gateway calls, bound from <c>Gateway:Upstreams</c>.
    /// </summary>
    /// <remarks>
    /// Never null in normal use: it is initialized so that construction always yields a usable object
    /// graph, which both satisfies nullable reference type analysis and keeps the type trivially
    /// constructible in a test. It carries a setter because the configuration binder needs one, and
    /// <see cref="Validate"/> re-checks it for null precisely because that public setter makes null
    /// reachable from any caller.
    /// </remarks>
    public UpstreamAddresses Upstreams { get; set; } = new();

    /// <summary>
    /// The addresses Gateway probes to build its C-10 readiness aggregate, bound from
    /// <c>Gateway:HealthProbes</c>. THREE entries, one per upstream, and every one of them is
    /// health observation only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A SEPARATE GROUP FROM <see cref="Upstreams"/> BECAUSE IT CARRIES A DIFFERENT KIND OF
    /// PERMISSION, AND THE SEPARATION IS THE WHOLE POINT. <see cref="Upstreams"/> says "Gateway may
    /// call this service" and has exactly two members, because Gateway calls exactly two services.
    /// This group says "Gateway may read this service's anonymous readiness probe" and has three,
    /// because contract C-10 requires the aggregate to name Persistence, DataServices and Security
    /// each with its own state - <c>OpenApi/gateway.v1.yaml</c> bounds
    /// <c>AggregateHealthReport.upstreams</c> at exactly three items and closes
    /// <c>UpstreamHealth.service</c> over exactly those three names. Without the Persistence entry
    /// the published aggregate is unbuildable; with Persistence in <see cref="Upstreams"/> the
    /// layering would be broken. Two groups is what satisfies both at once.
    /// </para>
    /// <para>
    /// THE PERSISTENCE ENTRY IS NOT A FUNCTIONAL DEPENDENCY AND MUST NEVER BECOME ONE. It addresses
    /// <c>GET /health</c> and nothing else. Gateway holds no Persistence client, opens no channel to
    /// it, and issues no contract call against it: DataServices - not Gateway - calls the service that
    /// owns SQL generation and storage, and the same document that requires the three-way aggregate
    /// also states that Gateway never calls Persistence. Reading a service's anonymous readiness
    /// probe is not reaching into its internals; it is reading the one endpoint C-10 publishes to
    /// every caller precisely so that a readiness verdict can be composed. Anything beyond
    /// <c>/health</c> on any address in this group is a layering breach.
    /// </para>
    /// <para>
    /// Each address is validated by the same rules as an upstream address - present, absolute, http or
    /// https, no embedded credential, no query, no fragment - in
    /// <see cref="Validate(ValidationContext)"/>. Reachability is deliberately not checked, because
    /// whether an upstream currently answers is precisely what the probe reports at request time
    /// rather than something a startup validator should decide.
    /// </para>
    /// </remarks>
    public HealthProbeAddresses HealthProbes { get; set; } = new();

    /// <summary>
    /// The client identity Gateway presents on the single mutual-TLS edge in the system, bound from
    /// <c>Gateway:MutualTls</c>. Paths only - never material.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS EXISTS AT ALL, WHICH IS A FUNCTIONAL REASON AND NOT A HARDENING PREFERENCE.
    /// <c>POST /v1/tokens</c> on the Security service cannot be protected by a bearer token, because
    /// <b>a caller cannot present a bearer token in order to obtain its first bearer token</b>. It
    /// therefore publishes TWO schemes as a disjunction - an HTTP Basic client credential and this
    /// client certificate - and a caller satisfies it by presenting either. Gateway is one of the two
    /// services that request tokens, so a deployment presenting NEITHER obtains no token at all and
    /// every authenticated call it would make is unreachable; <see cref="Validate(ValidationContext)"/>
    /// refuses that deployment rather than letting it start and fail on first use.
    /// </para>
    /// <para>
    /// THIS GROUP IS THE SECOND SCHEME, AND IT IS GENUINELY REACHABLE RATHER THAN MERELY DECLARED. When
    /// it is configured the composition root loads the pair and attaches it to the typed client's
    /// primary handler, so a deployment that prefers certificates to a shared secret has a working
    /// path. <see cref="SecurityClientSecret"/> is the first scheme and is the one the documented
    /// bring-up uses.
    /// </para>
    /// <para>
    /// PATHS, NOT MATERIAL, AND THAT IS ENFORCED BY THE MEMBER SET RATHER THAN BY A CONVENTION. There
    /// is no property here for a certificate body, a private key body or a passphrase, so there is
    /// nowhere for one to be placed. Both values name files mounted from the orchestration secret
    /// layer: no certificate and no key is committed to this repository or embedded in an image.
    /// </para>
    /// <para>
    /// OPTIONAL AS A GROUP. Both members default to empty, which means "this deployment presents no
    /// client certificate" - the loopback development posture in which no token is requested. When
    /// either is set both must be, which <see cref="Validate(ValidationContext)"/> enforces: a
    /// certificate without its key cannot complete a handshake and a key without its certificate has
    /// nothing to present, so half a client identity is unusable rather than merely weaker.
    /// </para>
    /// </remarks>
    public MutualTlsClientOptions MutualTls { get; set; } = new();

    /// <summary>
    /// The name of the FLAT configuration key carrying the client credential Gateway presents on the
    /// token-issuance edge. The name is declared here; the material never is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FLAT RATHER THAN SECTION-BOUND, AND THAT IS FORCED RATHER THAN CHOSEN. The environment-variable
    /// configuration provider maps only a double underscore onto the section separator, so a value
    /// arriving as <c>SECURITY_CLIENT_SECRET_GATEWAY</c> cannot land on a property inside
    /// <c>Gateway:</c> by binding. It is applied to the bound options by an explicit post-configure
    /// step in the composition root, which runs between binding and start-time validation so the
    /// validator sees the material the deployment actually supplied.
    /// </para>
    /// <para>
    /// AND IT KEEPS THE SECRET OUT OF EVERY SETTINGS FILE (constraint C-F). A section-bound spelling
    /// would need a key in <c>appsettings.json</c> for an operator to discover it, and a credential-named
    /// leaf in a committed file is the defect the whole configuration layer exists to avoid - the estate
    /// forbids one by name in
    /// <c>shared/PowerFramework.Contracts.Tests/ServiceConfigurationCoherenceTests.cs</c>. The name
    /// below is documented in <c>orchestration/.env.example</c> beside the two sibling callers.
    /// </para>
    /// </remarks>
    public const string SecurityClientSecretConfigurationKey = "SECURITY_CLIENT_SECRET_GATEWAY";

    /// <summary>
    /// The password half of the HTTP Basic credential Gateway presents to Security's issuance
    /// operation. Empty in source, empty in every settings file, and supplied through
    /// <see cref="SecurityClientSecretConfigurationKey"/>, which takes precedence over any value that
    /// reached this property by binding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PRECEDENCE, STATED BECAUSE THIS PROPERTY HAS TWO POSSIBLE INGRESSES AND ONE OF THEM USED TO BE
    /// SILENTLY DISCARDED.</b> The flat key above is the DOCUMENTED route and wins whenever it is set.
    /// This property is nonetheless a bindable leaf of the <c>Gateway</c> section - the environment
    /// provider folds <c>Gateway__SecurityClientSecret</c> onto it - and the composition root's
    /// post-configure step once assigned the flat key UNCONDITIONALLY, so an unset flat key overwrote a
    /// bound value with empty. A deployment supplying the credential that way was then refused at startup
    /// for presenting nothing, with a message naming a key it had deliberately not used. The step is now
    /// guarded on presence, matching Security's signing-key step and DataServices'
    /// <c>ApplyIssuanceSecret</c>: an absent flat key assigns nothing, so a value from another legitimate
    /// ingress survives.
    /// </para>
    /// <para>
    /// NONE OF THAT WEAKENS CONSTRAINT C-F. "Empty in every settings file" remains a rule about COMMITTED
    /// FILES and is enforced independently by the estate-wide configuration coherence test, which forbids
    /// a credential-named leaf in any of them. Binding from an environment variable is not a committed
    /// file.
    /// </para>
    /// <para>
    /// THE USER-ID HALF IS DELIBERATELY NOT CONFIGURABLE. It is the subject the outbound request
    /// already claims - <c>powerframework-gateway</c>, declared once in
    /// <c>Clients/DataServicesClient.cs</c> - and the issuance edge reconciles the claimed subject
    /// against the identity the presented credential establishes, refusing a mismatch with <c>403</c>.
    /// A second setting for the same identity could therefore only ever disagree with the first and be
    /// refused, so there is one spelling and the credential is built from it.
    /// </para>
    /// <para>
    /// EMPTY IS A LEGITIMATE VALUE ON ITS OWN, AND IS NOT ONE WHEN <see cref="MutualTls"/> IS ALSO
    /// UNSET. The two schemes are alternatives, so a deployment supplies whichever it operates; the
    /// validator refuses only the state in which it can present neither. That refusal is a startup
    /// failure rather than a warning, matching the fail-fast posture the legacy application object sets
    /// by ending a structural fault in termination [ws_objects/pfw.pbl.src/pfw.sra:L111-L144].
    /// </para>
    /// <para>
    /// NEVER LOGGED, NEVER ECHOED, AND NEVER PART OF A DIAGNOSTIC. The validation message below names
    /// the two configuration keys and quotes neither value; the client reads this property at the
    /// moment of use, holds it in no field of its own, and includes it in no exception message. A
    /// failure to authenticate is reported by Security as a status, and repeating the secret into a
    /// diagnostic would put it in an operator's log.
    /// </para>
    /// </remarks>
    public string SecurityClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Whether this deployment can present something on the token-issuance edge - a Basic credential, a
    /// client certificate, or both.
    /// </summary>
    /// <remarks>
    /// Computed, so it binds nothing and cannot be set by configuration. It is the single expression of
    /// "this deployment can obtain a token", read by the validator and by the diagnostics that describe
    /// the outbound posture, so the disjunction is stated once rather than re-derived at each use.
    /// </remarks>
    public bool HasIssuanceCredential =>
        !string.IsNullOrWhiteSpace(SecurityClientSecret) || (MutualTls?.IsConfigured ?? false);

    // NO FURTHER PROPERTY BELONGS ON THIS TYPE, AND EACH ABSENCE IS A DECISION
    //
    //   * No connection string, and no storage, SQLite or EF Core setting. Exactly one service in
    //     this system holds a storage provider and it is not this one, so a storage setting here

    /// <summary>
    /// The trust anchor Gateway verifies its two internal upstreams' server certificates against,
    /// bound from <c>Gateway:InternalTls</c>. A path - never material.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS EXISTS, AND WHY ITS ABSENCE WAS A DEFECT RATHER THAN A POSTURE. Security and
    /// DataServices both terminate TLS, and in every topology this system documents their certificates
    /// are issued by a LOCAL certificate authority mounted from the orchestration secret layer - the
    /// generation recipe in <c>docs/ARCHITECTURE.md</c> §9.3.1 creates exactly such a CA. That CA is
    /// in no container's operating-system trust store, so a client left on platform default trust
    /// rejects every certificate the documented topology presents: the token-issuance channel, the two
    /// gRPC channels, the key-set backchannel and the readiness probes all fail to connect. Declaring
    /// the anchor and verifying against it is what makes the documented topology reachable.
    /// </para>
    /// <para>
    /// IT NARROWS TRUST RATHER THAN RELAXING IT, WHICH IS THE PROPERTY THAT MATTERS. When this path is
    /// set, the chain policy built from it uses <c>X509ChainTrustMode.CustomRootTrust</c>, so the ONLY
    /// acceptable root is the mounted anchor and the machine's several hundred public roots stop being
    /// acceptable for internal traffic. Name validation, validity dates and chain building are all
    /// still performed by the platform. There is no validation callback, no
    /// <c>ServerCertificateCustomValidationCallback</c>, no
    /// <c>DangerousAcceptAnyServerCertificate</c> and no environment-conditional bypass anywhere in
    /// this service (constraint C-G).
    /// </para>
    /// <para>
    /// OPTIONAL, AND THE UNSET STATE IS PLATFORM DEFAULT TRUST. A deployment whose internal
    /// certificates are issued by a publicly trusted authority, or whose containers install the anchor
    /// into their own trust store, leaves this empty and the platform decides. Unset is therefore a
    /// legitimate configuration rather than a missing one; a set-but-unreadable path is a structural
    /// fault and refuses to start.
    /// </para>
    /// </remarks>
    public InternalTlsTrustOptions InternalTls { get; set; } = new();

    /// <summary>
    /// The bounds Gateway places on the calls it makes outward, bound from <c>Gateway:Outbound</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS BELONGS ON A CONFIGURATION SURFACE AT ALL, given that AAP 0.8.5 forbids asserting any
    /// performance objective. Neither value here is a latency target and neither is a service-level
    /// promise. Both are CORRECTNESS bounds: they say when Gateway stops waiting, and therefore when
    /// the upstream is told it may stop working and release what it is holding. Without them an
    /// upstream keeps a server-held session or task alive for a caller that has already gone - a
    /// half-open connection is exactly that case - and the resource is only reclaimed by the
    /// upstream's own idle sweep, long after the request it belonged to ended.
    /// </para>
    /// <para>
    /// THEY ARE OPERATOR-VISIBLE BECAUSE THE CORRECT VALUE IS A PROPERTY OF THE DEPLOYMENT, not of
    /// this code. The stream bound in particular must not exceed the upstream's own session lifetime,
    /// and that lifetime is configured in the upstream, so only the operator can see both numbers at
    /// once.
    /// </para>
    /// </remarks>
    public OutboundCallOptions Outbound { get; set; } = new();

    /// Bounds on the REST projection of the upstream DataWindow and column-expression contracts.
    /// </summary>
    public RestProjectionOptions RestProjection { get; set; } = new();

    // NO FURTHER PROPERTY BELONGS ON THIS TYPE, AND EACH ABSENCE IS A DECISION
    //
    //   * No connection string, and no storage, SQLite or EF Core setting. Exactly one service in
    //     this system holds a storage provider and it is not this one, so a storage setting here
    //     would advertise a capability Gateway must not have. Gateway does not even reach that
    //     service (see UpstreamAddresses below), let alone open a database.
    //
    //   * No signing key, symmetric secret, password, token, private key body, certificate body or
    //     certificate passphrase. Exactly one signing secret exists in this system and it belongs to
    //     the Security service, which is the sole token issuer; every other service holds
    //     verification material only. See JwtBearerVerificationOptions below.
    //     MutualTls above carries two PATHS and no material, which is the distinction that matters:
    //     a path names a file mounted from the orchestration secret layer, and there is no property
    //     anywhere on this surface into which a certificate, a key or a passphrase could be placed.
    //
    //   * No option, flag, address, URL or enumeration member for any capability deferred out of
    //     this phase. Those capabilities have no project, no container and no implementation
    //     anywhere in this build graph, so there is nothing behind them to configure. Their reserved
    //     routes are route-table metadata in the endpoint layer, and metadata takes no settings.
    //
    //   * No listening port or bound address for this service itself. Where a container listens is
    //     host and orchestration configuration, and baking it in here would let application
    //     configuration contradict the manifest that actually publishes the port.

    /// <summary>
    /// Validates the parts of this section that a single attribute cannot express, so that a host
    /// configured to validate options on start refuses to start rather than failing later.
    /// </summary>
    /// <param name="validationContext">
    /// Supplied by the validation infrastructure. It is not consulted: every rule below is expressed
    /// entirely in terms of this instance, so there is no service to resolve and no display name to
    /// read.
    /// </param>
    /// <returns>One result per problem found, or an empty sequence when the section is usable.</returns>
    /// <remarks>
    /// <para>
    /// This exists because the framework's object validator is NOT recursive: validating a
    /// <see cref="GatewayOptions"/> instance evaluates the attributes on <see cref="GatewayOptions"/>
    /// itself and does not descend into <see cref="Upstreams"/>. The attributes on
    /// <see cref="UpstreamAddresses"/> are therefore genuinely useful only when that type is validated
    /// directly, and the checks below are what actually enforce the nested section at startup. Missing
    /// this distinction is an easy way to ship a service that silently accepts an unusable address.
    /// </para>
    /// <para>
    /// Presence and syntax are checked; reachability deliberately is not. Whether an upstream is
    /// currently answering is a health-check concern with its own endpoint, and a configuration
    /// validator that dialled out would turn an ordinary transient condition into a failure to start.
    /// </para>
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Declared non-nullable and initialized, so this is null only when a caller assigns null
        // through the public setter the configuration binder requires. That path is reachable, so it
        // is checked rather than assumed away.
        UpstreamAddresses? upstreams = Upstreams;

        if (upstreams is null)
        {
            yield return new ValidationResult(
                $"'{SectionName}:{nameof(Upstreams)}' is required. Gateway is a composition root and "
                    + "cannot serve a request without the addresses of the services it calls.",
                [nameof(Upstreams)]);

            // Nothing below can be evaluated against a null group, and reporting the same missing
            // section once per member would obscure the single real problem.
            yield break;
        }

        ValidationResult? dataServices = AddressValidation.Check(
            upstreams.DataServices,
            $"{SectionName}:{nameof(Upstreams)}:{nameof(UpstreamAddresses.DataServices)}",
            nameof(Upstreams));

        if (dataServices is not null)
        {
            yield return dataServices;
        }

        ValidationResult? security = AddressValidation.Check(
            upstreams.Security,
            $"{SectionName}:{nameof(Upstreams)}:{nameof(UpstreamAddresses.Security)}",
            nameof(Upstreams));

        if (security is not null)
        {
            yield return security;
        }

        // The probe group is validated by the same rules and for the same reason: contract C-10
        // requires the aggregate to name all THREE upstreams individually, so all three addresses
        // must be present and well formed or the aggregate cannot be produced at all.
        HealthProbeAddresses? probes = HealthProbes;

        if (probes is null)
        {
            yield return new ValidationResult(
                $"'{SectionName}:{nameof(HealthProbes)}' is required. Contract C-10 makes Gateway the "
                    + "single aggregator and requires its report to name Persistence, DataServices and "
                    + "Security each with its own state, so a missing probe group leaves the aggregate "
                    + "unbuildable.",
                [nameof(HealthProbes)]);

            yield break;
        }

        ValidationResult? persistenceProbe = AddressValidation.Check(
            probes.Persistence,
            $"{SectionName}:{nameof(HealthProbes)}:{nameof(HealthProbeAddresses.Persistence)}",
            nameof(HealthProbes));

        if (persistenceProbe is not null)
        {
            yield return persistenceProbe;
        }

        ValidationResult? dataServicesProbe = AddressValidation.Check(
            probes.DataServices,
            $"{SectionName}:{nameof(HealthProbes)}:{nameof(HealthProbeAddresses.DataServices)}",
            nameof(HealthProbes));

        if (dataServicesProbe is not null)
        {
            yield return dataServicesProbe;
        }

        ValidationResult? securityProbe = AddressValidation.Check(
            probes.Security,
            $"{SectionName}:{nameof(HealthProbes)}:{nameof(HealthProbeAddresses.Security)}",
            nameof(HealthProbes));

        if (securityProbe is not null)
        {
            yield return securityProbe;
        }

        // The mutual-TLS caller material is OPTIONAL AS A GROUP and INSEPARABLE WHEN PRESENT. Half a
        // client identity is not a weaker identity, it is an unusable one: a certificate with no
        // private key cannot complete a handshake, and a key with no certificate has nothing to
        // present. Reporting the halves separately would let a deployment start with one of them set
        // and discover the fault only when the first token was requested.
        MutualTlsClientOptions? mutualTls = MutualTls;

        if (mutualTls is not null)
        {
            foreach (ValidationResult result in mutualTls.Validate(
                $"{SectionName}:{nameof(MutualTls)}",
                nameof(MutualTls)))
            {
                yield return result;
            }
        }

        // The internal trust anchor is a single OPTIONAL path, so the only thing to check is that a
        // value which is present is usable as a path at all. Whether the file exists and parses is
        // decided when it is loaded at startup, because that is where the failure can carry the
        // loader's own diagnosis rather than a second, weaker copy of it.
        InternalTlsTrustOptions? internalTls = InternalTls;

        if (internalTls is not null)
        {
            foreach (ValidationResult result in internalTls.Validate(
                $"{SectionName}:{nameof(InternalTls)}",
                nameof(InternalTls)))
            {
                yield return result;
            }
        }

        // The outbound bounds are two durations whose relationship to each other and to the resilience
        // pipeline is what makes them correct, and no attribute can express that. A misconfigured
        // deadline does not fail loudly at the point of use - it either expires before the upstream can
        // answer, so every call reports a deadline failure, or it is so long that it bounds nothing -
        // which is exactly the class of fault that belongs in a refusal to start.
        OutboundCallOptions? outbound = Outbound;

        if (outbound is not null)
        {
            foreach (ValidationResult result in outbound.Validate(
                $"{SectionName}:{nameof(Outbound)}",
                nameof(Outbound)))
            {
                yield return result;
            }
        }

        // THE COLLECTION WINDOW IS CHECKED HERE FOR THE SAME REASON THE OUTBOUND BOUNDS ARE: its
        // correctness is a RELATIONSHIP to another duration, which no attribute can express. A window at
        // or beyond the per-attempt outbound timeout cannot fire first, so the pipeline's timeout wins and
        // an idle subscription is once again answered as a server fault - the exact defect the window
        // exists to remove, silently reintroduced by a plausible-looking setting.
        RestProjectionOptions? restProjection = RestProjection;

        if (restProjection is not null)
        {
            foreach (ValidationResult result in restProjection.Validate(
                $"{SectionName}:{nameof(RestProjection)}",
                nameof(RestProjection)))
            {
                yield return result;
            }
        }

        // AND THE ONE STATE IN WHICH GATEWAY CANNOT OBTAIN A TOKEN AT ALL: neither scheme configured.
        //
        // This is a structural fault rather than a reduced capability. Gateway requests a token before
        // every call it makes to DataServices, so a deployment that can present nothing on the issuance
        // edge has already lost every authenticated call it would make - the readiness probe would
        // report healthy and the first proxied request would fail with what looks like an upstream
        // problem. Refusing at startup names the cause once, in the place an operator is already
        // reading, and matches the posture the legacy application object sets by ending a structural
        // fault in termination rather than a warning [ws_objects/pfw.pbl.src/pfw.sra:L111-L144].
        //
        // BOTH KEYS ARE NAMED AND NEITHER VALUE IS QUOTED. A message that echoed the material it was
        // complaining about would put a credential in a startup log, which is the failure this whole
        // indirection exists to prevent (C-F).
        //
        // THIS ARM IS LAST, AND THAT ORDERING IS DELIBERATE RATHER THAN INCIDENTAL. The two arms above
        // that end in `yield break` - a missing upstream group and a missing probe group - are reporting
        // that the section an operator was supposed to write does not exist at all, and an absent
        // section cannot also be usefully told that its credential is absent. Placing the issuance
        // refusal after them keeps a wholly unconfigured deployment reporting the single root cause
        // rather than two facts that both reduce to "nothing was configured".
        if (!HasIssuanceCredential)
        {
            yield return new ValidationResult(
                "This deployment can present nothing on Security's token-issuance edge, so it can "
                    + "obtain no service token and every authenticated call it would make is "
                    + "unreachable. Supply EITHER the client credential in "
                    + $"'{SecurityClientSecretConfigurationKey}' - the documented bring-up path, see "
                    + "orchestration/.env.example - OR the client certificate pair in "
                    + $"'{SectionName}:{nameof(MutualTls)}'. The two are alternatives and either alone "
                    + "is sufficient; no value is reproduced here.",
                [nameof(SecurityClientSecret), nameof(MutualTls)]);
        }
    }

    /// <summary>
    /// The addresses of the services Gateway calls, bound from <c>Gateway:Upstreams</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nested inside <see cref="GatewayOptions"/> because it is not an independent configuration root:
    /// it exists only as the shape of that type's <c>Upstreams</c> child object, and nesting says so
    /// in the type system. It is declared in this same file because the scope of this folder is one
    /// file; the constraint is one file rather than one type.
    /// </para>
    /// <para>
    /// EXACTLY TWO MEMBERS, AND THE THIRD ONE'S ABSENCE IS THE POINT. The service graph is layered and
    /// acyclic: external clients reach Gateway, Gateway calls DataServices and Security, and
    /// DataServices - not Gateway - calls the storage-owning service beneath it. There is therefore no
    /// third address here, and adding one would not be a helpful completion. It would advertise a call
    /// path that flattens the layering and breaks the rule that no service reaches into another
    /// service's internals, and it would do so convincingly enough that the topology violation would
    /// look like configuration rather than a design breach.
    /// </para>
    /// <para>
    /// Both values are plain strings rather than <see cref="Uri"/> because configuration is text and a
    /// malformed value must surface as a validation message naming its configuration key, not as a
    /// binder exception naming a type. They are validated in
    /// <see cref="GatewayOptions.Validate(ValidationContext)"/>.
    /// </para>
    /// </remarks>
    public sealed class UpstreamAddresses
    {
        /// <summary>
        /// The DataServices service's gRPC address. Defaults to the local topology's port
        /// <b>5112</b> over <b>https</b>, which is that service's HTTP/2 listener - the one the gRPC
        /// contracts are served on. Its REST surface answers on 5102, and the remarks below record why
        /// this member names the other endpoint of the same service.
        /// </summary>
        /// <remarks>
        /// <para>
        /// gRPC is the transport on this edge because the interface it carries is an ordered event
        /// chain with typed veto semantics, which needs compile-time contract enforcement and
        /// bidirectional streaming rather than a resource-shaped REST surface. The default names the
        /// local orchestration topology over https, matching the base appsettings.json, and is
        /// overridden per environment through <c>Gateway__Upstreams__DataServices</c> - by
        /// appsettings.Development.json for the loopback bring-up, and by the orchestration manifest
        /// for a deployed one. The default is https so that a missing override cannot silently
        /// downgrade the edge; the host name comes from configuration rather than from a scheme
        /// invented here.
        /// </para>
        /// <para>
        /// THE PORT IS THE LOAD-BEARING HALF OF THIS VALUE. DataServices serves the C-03 and C-04 gRPC
        /// contracts, which REQUIRE HTTP/2, and the anonymous <c>/health</c> plus <c>/v1/ping</c>, which
        /// the readiness gate probes with HTTP/1.1 (C-L). DataServices gives each version its own TLS
        /// endpoint - <c>https://+:5102</c> HTTP/1.1 for the REST surface and <c>https://+:5112</c>
        /// HTTP/2 prior knowledge for the gRPC contracts - and THIS address, being the call edge, names
        /// the second. One version per endpoint is a misaddressing guard: a listener that accepts only
        /// what it is for cannot be reached by the wrong client and answer anyway.
        /// </para>
        /// <para>
        /// Naming 5102 here therefore fails every RPC on this edge during transport negotiation, before
        /// the request reaches a method, which surfaces as a transport fault naming no operation - a
        /// clean, immediate failure, which is the point of the split rather than a cost of it. Merging
        /// both versions onto one TLS endpoint with ALPN negotiating between them is a supported
        /// arrangement and needs no code change; it is not the shipped default because a misdirected
        /// gRPC caller would then get a <c>404</c> from the REST surface instead. One measurement is
        /// worth recording because it rules the CLEARTEXT variant out on functional grounds too: a
        /// cleartext endpoint configured for both versions disables HTTP/2 outright and logs that it
        /// has, and configured for <c>Http2</c> alone answers an HTTP/1.1 <c>GET</c> with <c>400</c>.
        /// The band the environment fixes is the band it DOCUMENTS - health and ping on 5101-5105 - and
        /// it documents no gRPC address at all, so 5112 moves nothing it fixes and leaves the reserved
        /// 5103 DesignSystem slot untouched (C-D). This value and <see cref="GatewayOptions.HealthProbes"/>'s DataServices entry
        /// consequently name DIFFERENT endpoints of the same service, which is why they were already
        /// separate members: one is a call edge and the other an observation.
        /// </para>
        /// <para>
        /// <b>THE DEFAULT IS TLS, AND THE DEFAULT IS THE PART THAT MATTERS.</b> A deployment that binds
        /// this section supplies its own address; a deployment that forgets to gets THIS value. A
        /// cleartext default therefore fails silently in the one case where nobody is looking - which is
        /// the whole shape of CWE-319 - and every request on this edge carries a bearer token. The shipped
        /// <c>appsettings.json</c> names the same scheme, so the two cannot disagree.
        /// </para>
        /// </remarks>
        [Required(AllowEmptyStrings = false)]
        public string DataServices { get; set; } = "https://localhost:5112";

        /// <summary>
        /// The Security service's REST address. Defaults to the local topology's port 5104.
        /// </summary>
        /// <remarks>
        /// REST is the transport on this edge so that token issuance and key publication use ordinary
        /// HTTP semantics, which is what lets a stock bearer handler fetch the published key material
        /// with no bespoke code. Overridden per environment through
        /// <c>Gateway__Upstreams__Security</c>. This address identifies the service; it never carries
        /// credentials of any kind.
        /// </remarks>
        /// <remarks>
        /// <para>
        /// THE SCHEME IS <c>https</c> BECAUSE IT IS THE SCHEME SECURITY BINDS (C-L). Security's shipped
        /// listener is <c>https://+:5104</c>, and its token endpoint identifies its caller from a
        /// presented client certificate - which cannot be requested, presented or validated on a
        /// cleartext listener at all, so a plaintext Security could not issue a single token. The
        /// environment's plaintext probe command fixes the readiness probe's SHAPE, not the transport
        /// beneath it. Security is REST-only, so it needs no second endpoint - the protocol-version
        /// split DataServices and Persistence carry does not arise here.
        /// </para>
        /// <para>
        /// WHAT PLAINTEXT WOULD HAVE COST ON THIS EDGE, WHICH IS WHY NONE IS DECLARED. Security is this
        /// system's trust bootstrap: on a channel an on-path attacker can rewrite, a substituted key set
        /// makes all three verifying services accept tokens the attacker signed while behaving exactly
        /// as designed. That is CWE-319 on the one edge where it costs the most, so the exposure is
        /// closed rather than accepted; the member nonetheless stays an environment override so a
        /// deployment can move the HOST without touching code. See <c>OpenApi/security.v1.yaml</c>'s
        /// <c>servers</c> block, which records the same decision on the publishing side.
        /// </para>
        /// <para>
        /// <b>THE DEFAULT IS TLS, AND THE DEFAULT IS THE PART THAT MATTERS.</b> A deployment that binds
        /// this section supplies its own address; a deployment that forgets to gets THIS value. A
        /// cleartext default therefore fails silently in the one case where nobody is looking - which is
        /// the whole shape of CWE-319 - and every request on this edge carries a bearer token. The shipped
        /// <c>appsettings.json</c> names the same scheme, so the two cannot disagree.
        /// </para>
        /// </remarks>
        [Required(AllowEmptyStrings = false)]
        public string Security { get; set; } = "https://localhost:5104";
    }

    /// <summary>
    /// The three anonymous readiness endpoints Gateway composes into its C-10 aggregate, bound from
    /// <c>Gateway:HealthProbes</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EVERY ADDRESS HERE IS READ-ONLY OBSERVATION OF ONE ENDPOINT. Each names the base address of a
    /// service whose <c>GET /health</c> Gateway reads; nothing in this group authorises a contract
    /// call, and Gateway holds no client for a service it does not call. The distinction between this
    /// group and <see cref="UpstreamAddresses"/> is the difference between reading a published
    /// readiness verdict and invoking a capability, and it is deliberately expressed in the type
    /// system rather than in a comment on a shared group.
    /// </para>
    /// <para>
    /// THE PORTS ARE THE PORTS THE ENVIRONMENT DOCUMENTS, WHICH ARE THE HTTP/1.1 ONES. A readiness
    /// probe is an HTTP/1.1 <c>GET</c>, and Persistence on 5101, DataServices on 5102 and Security on
    /// 5104 each answer it there. Persistence and DataServices additionally bind a SECOND TLS
    /// endpoint - 5111 and 5112 - carrying HTTP/2 for their gRPC contracts, one protocol version per
    /// endpoint. A probe must never name those: an HTTP/1.1
    /// <c>GET</c> against an HTTP/2-only endpoint answers <c>400</c>, so the readiness verdict would be
    /// permanently negative and the gate that holds Gateway behind its upstreams would never open.
    /// </para>
    /// <para>
    /// All three default to the local topology on the scheme those listeners actually bind. A default
    /// naming a scheme nobody binds is the failure this group must not have, because a probe that
    /// cannot connect reports the upstream down and holds the gate closed for a reason no log explains.
    /// </para>
    /// </remarks>
    public sealed class HealthProbeAddresses
    {
        /// <summary>
        /// The Persistence service's base address, probed for readiness ONLY. Port 5101.
        /// </summary>
        /// <remarks>
        /// <para>
        /// THE ONE ADDRESS IN THIS FILE THAT WOULD BE A LAYERING BREACH IF IT WERE ANYWHERE ELSE.
        /// Contract C-10 requires Gateway's aggregate to name Persistence with its own state, and the
        /// same contract states that Gateway never calls Persistence. Both are satisfied because this
        /// value addresses exactly one anonymous endpoint - <c>GET /health</c> - and Gateway has no
        /// Persistence client, no channel and no generated stub with which it could do anything else.
        /// </para>
        /// <para>
        /// Do not move this member to <see cref="UpstreamAddresses"/>, and do not add a Persistence
        /// client that reads it. Either change would put SQL generation one hop from the ingress while
        /// looking like configuration rather than the design breach it is.
        /// </para>
        /// <para>
        /// <b>THE DEFAULT IS TLS, AND THE DEFAULT IS THE PART THAT MATTERS.</b> A deployment that binds
        /// this section supplies its own address; a deployment that forgets to gets THIS value. A
        /// cleartext default therefore fails silently in the one case where nobody is looking - which is
        /// the whole shape of CWE-319 - and every request on this edge carries a bearer token. The shipped
        /// <c>appsettings.json</c> names the same scheme, so the two cannot disagree.
        /// </para>
        /// </remarks>
        [Required(AllowEmptyStrings = false)]
        public string Persistence { get; set; } = "https://localhost:5101";

        /// <summary>
        /// The DataServices service's REST base address, probed for readiness. Port 5102.
        /// </summary>
        /// <remarks>
        /// A DIFFERENT endpoint of the same service from the one
        /// <see cref="UpstreamAddresses.DataServices"/> names, and that is why the two were already
        /// separate members: this address authorises exactly one anonymous <c>GET /health</c> for the
        /// C-10 aggregate on the HTTP/1.1 endpoint 5102, while that one carries the C-03 and C-04 call
        /// edge on the HTTP/2 endpoint 5112. Collapsing them would make an observation
        /// indistinguishable from an invocation in configuration - and would now also point one of the
        /// two at a listener that cannot answer it.
        /// <para>
        /// <b>THE DEFAULT IS TLS, AND THE DEFAULT IS THE PART THAT MATTERS.</b> A deployment that binds
        /// this section supplies its own address; a deployment that forgets to gets THIS value. A
        /// cleartext default therefore fails silently in the one case where nobody is looking - which is
        /// the whole shape of CWE-319 - and every request on this edge carries a bearer token. The shipped
        /// <c>appsettings.json</c> names the same scheme, so the two cannot disagree.
        /// </para>
        /// </remarks>
        [Required(AllowEmptyStrings = false)]
        public string DataServices { get; set; } = "https://localhost:5102";

        /// <summary>
        /// The Security service's base address, probed for readiness. Port 5104.
        /// </summary>
        /// <remarks>
        /// The same address <see cref="UpstreamAddresses.Security"/> carries, and duplicated
        /// deliberately rather than aliased: the two express different permissions and a deployment
        /// that terminated the probe somewhere else - at a sidecar, say - must be able to say so
        /// without also redirecting token issuance.
        /// <para>
        /// <b>THE DEFAULT IS TLS, AND THE DEFAULT IS THE PART THAT MATTERS.</b> A deployment that binds
        /// this section supplies its own address; a deployment that forgets to gets THIS value. A
        /// cleartext default therefore fails silently in the one case where nobody is looking - which is
        /// the whole shape of CWE-319 - and every request on this edge carries a bearer token. The shipped
        /// <c>appsettings.json</c> names the same scheme, so the two cannot disagree.
        /// </para>
        /// </remarks>
        [Required(AllowEmptyStrings = false)]
        public string Security { get; set; } = "https://localhost:5104";
    }

    /// <summary>
    /// The client identity Gateway presents to the Security service's mutual-TLS token endpoint,
    /// bound from <c>Gateway:MutualTls</c>. Two paths, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THERE IS NO MEMBER HERE THAT COULD HOLD KEY MATERIAL, AND THAT IS THE DESIGN. Both members are
    /// filesystem paths naming material mounted from the orchestration secret layer. No certificate
    /// body, no private key body and no passphrase member exists, so none can be configured, logged or
    /// captured in a recording.
    /// </para>
    /// <para>
    /// The pair is optional and inseparable: unset means this deployment requests no token, and
    /// setting one member without the other is refused with a named error rather than deferred to the
    /// first handshake.
    /// </para>
    /// </remarks>
    public sealed class MutualTlsClientOptions
    {
        /// <summary>
        /// Path to the PEM-encoded client certificate Gateway presents. Empty means none.
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
        /// Checks that the pair is either wholly absent or wholly present.
        /// </summary>
        /// <param name="configurationKeyPrefix">
        /// The configuration path of this group, quoted into each message so an operator can find the
        /// offending key without reading source.
        /// </param>
        /// <param name="memberName">The property name on the parent to attribute a failure to.</param>
        /// <returns>One result per problem found, or an empty sequence when the group is usable.</returns>
        /// <remarks>
        /// No path is ever echoed into a message. A path is not itself a credential, but it names the
        /// location of one, and a startup log is exactly the wrong place to publish where a private key
        /// is mounted. The configuration key is sufficient for an operator to find the setting.
        /// </remarks>
        internal IEnumerable<ValidationResult> Validate(string configurationKeyPrefix, string memberName)
        {
            bool hasCertificate = !string.IsNullOrWhiteSpace(CertificatePath);
            bool hasKey = !string.IsNullOrWhiteSpace(CertificateKeyPath);

            if (hasCertificate == hasKey)
            {
                yield break;
            }

            yield return new ValidationResult(
                $"'{configurationKeyPrefix}:{nameof(CertificatePath)}' and "
                    + $"'{configurationKeyPrefix}:{nameof(CertificateKeyPath)}' must be configured "
                    + "together or not at all. A client certificate cannot complete a TLS handshake "
                    + "without its key, and a key has nothing to present without its certificate, so "
                    + "half of this pair is unusable rather than merely weaker. Neither path is quoted "
                    + "here, because a startup log must not record where key material is mounted.",
                [memberName]);
        }
    }

    /// <summary>
    /// The trust anchor internal TLS is verified against, bound from <c>Gateway:InternalTls</c>. One
    /// path, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A CA CERTIFICATE IS PUBLIC MATERIAL, WHICH IS WHY THIS GROUP CARRIES A PATH ANYWAY. Nothing
    /// about a root certificate is secret - it is the thing a server hands out - so the reason for a
    /// path here is not confidentiality. It is that the anchor is a DEPLOYMENT artefact: one local CA
    /// per environment, rotated on its own schedule, mounted read-only from the orchestration layer.
    /// Embedding one in a settings file would pin every environment to one authority and make rotation
    /// a code change. There is deliberately no member for certificate material, so none can be placed
    /// in configuration, a log record or a characterization recording.
    /// </para>
    /// <para>
    /// ONE MEMBER, NOT TWO. Unlike <see cref="MutualTlsClientOptions"/> there is no key path, because
    /// verifying a chain needs only the public root. A trust anchor with a private key beside it would
    /// mean this service could ISSUE certificates for the internal topology, which is a capability it
    /// must not have.
    /// </para>
    /// </remarks>
    public sealed class InternalTlsTrustOptions
    {
        /// <summary>
        /// Path to the PEM-encoded certificate authority bundle internal server certificates are
        /// verified against. Empty means platform default trust.
        /// </summary>
        /// <remarks>
        /// The file may contain one certificate or a concatenated chain of them; every certificate it
        /// carries becomes an acceptable root for internal traffic and nothing else does.
        /// </remarks>
        public string TrustedCaPath { get; set; } = string.Empty;

        /// <summary>
        /// Whether this deployment narrows internal trust to a mounted anchor.
        /// </summary>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(TrustedCaPath);

        /// <summary>
        /// Checks that a supplied anchor path is at least shaped like a path.
        /// </summary>
        /// <param name="configurationKeyPrefix">
        /// The configuration path of this group, quoted into the message so an operator can find the
        /// offending key without reading source.
        /// </param>
        /// <param name="memberName">The property name on the parent to attribute a failure to.</param>
        /// <returns>One result per problem found, or an empty sequence when the group is usable.</returns>
        /// <remarks>
        /// The path is not echoed. A trust anchor is public material, but the MOUNT LAYOUT of a
        /// container's secret volume is not something a startup record should publish, and the
        /// configuration key alone is enough for an operator to find the setting.
        /// </remarks>
        internal IEnumerable<ValidationResult> Validate(string configurationKeyPrefix, string memberName)
        {
            if (TrustedCaPath.Length == 0 || !string.IsNullOrWhiteSpace(TrustedCaPath))
            {
                yield break;
            }

            yield return new ValidationResult(
                $"'{configurationKeyPrefix}:{nameof(TrustedCaPath)}' is set to whitespace, which is "
                    + "neither a path nor the empty value that means platform default trust. Set a "
                    + "path to the PEM certificate authority bundle internal server certificates are "
                    + "issued by, or remove the key entirely. The value is not quoted here, because a "
                    + "startup record must not publish a container's secret mount layout.",
                [memberName]);
        }
    }

    /// <summary>
    /// The bounds Gateway places on its outbound calls, bound from <c>Gateway:Outbound</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EXACTLY TWO MEMBERS, AND THE SHAPE OF THE PAIR IS THE POINT. A unary call is bounded by the
    /// moment the caller gives up on it; a stream is bounded by the moment the upstream would have
    /// reclaimed what the stream is reading from. Those are different quantities with different
    /// derivations, so one value could not serve both without either cutting streams off or leaving
    /// unary calls effectively unbounded.
    /// </para>
    /// <para>
    /// NEITHER IS A RETRY SETTING, AND THAT ABSENCE IS DELIBERATE. Which operations may be retried is
    /// a property of the CONTRACTS - whether replaying a call leaves a second server-held session
    /// behind, whether a clause setter appends twice - and not a property a deployment gets to choose.
    /// It is therefore classified in code, from the contract descriptors, in
    /// <c>Clients/OutboundCallPolicy.cs</c>, where a contract change breaks the build rather than
    /// silently changing an operator's retry posture. What a deployment does get to choose is how many
    /// attempts and how long to spend, which is what <see cref="RequestTimeout"/> bounds.
    /// </para>
    /// </remarks>
    public sealed class OutboundCallOptions
    {
        /// <summary>
        /// The shipped total bound on a unary call.
        /// </summary>
        /// <remarks>
        /// This is the documented default of the resilience package's own total request timeout, made
        /// explicit here so that the deadline sent to the upstream and the budget the local pipeline
        /// spends are the same number rather than two numbers that happen to agree.
        /// </remarks>
        public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The shipped total bound on a streaming call.
        /// </summary>
        /// <remarks>
        /// DERIVED FROM THE UPSTREAM RATHER THAN CHOSEN. Every stream Gateway opens belongs to a
        /// DataServices session - a validation session or an expression session - and DataServices
        /// reclaims an idle session after <c>DataServices:SessionLifetime:*:IdleTimeout</c>, whose
        /// shipped value is five minutes. A stream still open past that point is holding a session
        /// that the upstream's own policy would already have released, so this is the bound at which
        /// continuing to wait stops being meaningful.
        /// </remarks>
        public static readonly TimeSpan DefaultStreamDeadline = TimeSpan.FromMinutes(5);

        /// <summary>
        /// The lower bound on <see cref="RequestTimeout"/>.
        /// </summary>
        /// <remarks>
        /// This is the resilience package's documented default PER-ATTEMPT timeout, and the package's
        /// own options validator requires the total to be at least the per-attempt value. A smaller
        /// total is therefore not a tighter policy but an unstartable one, so it is refused here with
        /// a message naming the key rather than left to surface as a framework validation error
        /// against a generated handler name.
        /// </remarks>
        internal static readonly TimeSpan MinimumRequestTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// How long Gateway waits for a unary upstream call in total, across every attempt the
        /// resilience pipeline makes. Defaults to <see cref="DefaultRequestTimeout"/>.
        /// </summary>
        /// <remarks>
        /// THIS VALUE IS USED TWICE, WHICH IS WHY THERE IS ONLY ONE OF IT. It configures the outbound
        /// resilience pipeline's total request timeout, and it is the gRPC deadline attached to every
        /// unary call. A gRPC deadline is enforced by the client as a TOTAL bound across the retries
        /// the pipeline performs beneath it, so a deadline shorter than the pipeline's budget would
        /// cancel the call while the pipeline was still retrying, and a longer one would leave the
        /// upstream working after the caller had stopped waiting. Deriving both from one setting makes
        /// disagreement impossible rather than unlikely.
        /// </remarks>
        public TimeSpan RequestTimeout { get; set; } = DefaultRequestTimeout;

        /// <summary>
        /// How long Gateway holds a streaming upstream call open before abandoning it. Defaults to
        /// <see cref="DefaultStreamDeadline"/>.
        /// </summary>
        /// <remarks>
        /// MUST NOT EXCEED THE UPSTREAM'S SESSION IDLE LIFETIME. Past that point the upstream may have
        /// reclaimed the session the stream reads from, so waiting longer cannot produce a result and
        /// only delays the caller's error. It is a separate setting from
        /// <see cref="RequestTimeout"/> because a stream that runs for minutes is correct behaviour
        /// while a unary call that does so is not.
        /// </remarks>
        public TimeSpan StreamDeadline { get; set; } = DefaultStreamDeadline;

        /// <summary>
        /// Checks the two bounds and the relationship between them.
        /// </summary>
        /// <param name="configurationKeyPrefix">
        /// The configuration path of this group, quoted into each message so an operator can find the
        /// offending key without reading source.
        /// </param>
        /// <param name="memberName">The property name on the parent to attribute a failure to.</param>
        /// <returns>One result per problem found, or an empty sequence when the group is usable.</returns>
        internal IEnumerable<ValidationResult> Validate(
            string configurationKeyPrefix,
            string memberName)
        {
            if (RequestTimeout < MinimumRequestTimeout)
            {
                yield return new ValidationResult(
                    $"'{configurationKeyPrefix}:{nameof(RequestTimeout)}' is "
                        + $"{RequestTimeout}, which is below the {MinimumRequestTimeout} minimum. The "
                        + "resilience pipeline's per-attempt timeout is that value, and a total budget "
                        + "smaller than one attempt cannot be satisfied - the pipeline itself refuses "
                        + "the configuration, and every outbound call would report a deadline failure "
                        + "before the upstream could answer.",
                    [memberName]);
            }

            if (StreamDeadline < RequestTimeout)
            {
                yield return new ValidationResult(
                    $"'{configurationKeyPrefix}:{nameof(StreamDeadline)}' is {StreamDeadline}, which "
                        + $"is shorter than '{configurationKeyPrefix}:{nameof(RequestTimeout)}' "
                        + $"({RequestTimeout}). A stream is a long-lived call and is bounded by the "
                        + "upstream's session lifetime rather than by a single request's budget, so a "
                        + "stream bound below the unary bound would abandon event chains and retrieval "
                        + "streams sooner than the ordinary calls beside them.",
                    [memberName]);
            }
        }
    }
}

/// <summary>
/// The inbound JWT bearer verification settings, bound from <c>Authentication:Schemes:Bearer</c>.
/// </summary>
/// <remarks>
/// <para>
/// A SECOND TOP-LEVEL TYPE, NOT A CHILD OF <see cref="GatewayOptions"/>, AND THAT IS THE WHOLE POINT.
/// The sibling appsettings files declare these settings under the stock
/// <c>Authentication:Schemes:Bearer</c> path, which is a different configuration root from
/// <c>Gateway</c>. Modelling them as <c>Gateway:Jwt:*</c> would have produced a type that compiles,
/// passes a naive default-value test, and binds nothing whatsoever, because no such key exists. The
/// stock path is also the path the framework's own bearer handler binds for itself, which is exactly
/// the property that keeps the security-critical path framework code rather than hand-written code.
/// </para>
/// <para>
/// This type is therefore a typed, validated projection of those same keys rather than a competing
/// source of truth: identical path, identical spellings, so the handler and this type cannot disagree.
/// Its reason for existing is fail-fast. The handler alone would not discover a missing or malformed
/// authority until it first tried to fetch metadata, by which time the process is up and answering;
/// binding and validating the same keys at startup turns that into a refusal to start, which is the
/// posture the legacy framework had when a structural fault terminated the application outright rather
/// than degrading it.
/// </para>
/// <para>
/// VERIFICATION MATERIAL ONLY. There is deliberately no signing key, symmetric secret, private key,
/// certificate path or certificate password on this type, and no mutual-TLS setting either. Exactly
/// one signing secret exists in the system and the Security service holds it as the sole token issuer;
/// Gateway validates tokens against the key material Security publishes and has no signing authority
/// of its own. A signing key here would be a structural contradiction even if no code ever read it.
/// </para>
/// </remarks>
public sealed class JwtBearerVerificationOptions : IValidatableObject
{
    /// <summary>
    /// The configuration section this type binds: <c>Authentication:Schemes:Bearer</c>.
    /// </summary>
    /// <remarks>
    /// The same category of constant as <see cref="GatewayOptions.SectionName"/>, and legitimate for
    /// the same reason: it names a section rather than carrying a value bound from one, so it displaces
    /// nothing that configuration must be able to set. The colon separator is the configuration path
    /// separator; the equivalent environment variable prefix is
    /// <c>Authentication__Schemes__Bearer__</c>.
    /// </remarks>
    public const string SectionName = "Authentication:Schemes:Bearer";

    /// <summary>
    /// The token authority, which is the Security service. Its discovery document and published key
    /// set are resolved beneath this address.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Security publishes standard discovery metadata and a standard key set document, so a stock
    /// bearer handler given this address self-configures and fetches verification keys without bespoke
    /// code. This is an address, never a credential.
    /// </para>
    /// <para>
    /// DELIBERATELY UNSET, AND THE EMPTY DEFAULT IS THE POINT. An earlier form of this type defaulted
    /// to the loopback address of the local topology. That put an environment-specific address into
    /// source, where it is invisible to a deployment review, and it meant a deployment that configured
    /// nothing still got a usable-looking authority pointing at a host that does not exist for it. With no default, the presence rule below rejects the omission by name, so an
    /// unconfigured deployment fails to start with a message identifying the exact configuration key -
    /// which is the fail-fast posture the legacy framework had when a structural fault terminated the
    /// application rather than degrading it. The loopback value now lives in
    /// appsettings.Development.json, where it is one file, one environment and visible as such, and it
    /// is supplied in every other environment through
    /// <c>Authentication__Schemes__Bearer__Authority</c>.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// Whether discovery metadata must be retrieved over HTTPS. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The safe value is the default in code AND in the base appsettings.json, which states it
    /// explicitly rather than leaving it implied.
    /// </para>
    /// <para>
    /// <b>THERE IS NO RELAXATION ANY MORE, IN EITHER ENVIRONMENT, AND THAT IS THE CURRENT STATE.</b> An
    /// earlier form of the base appsettings.json set this to <see langword="false"/> so that a local
    /// plain-HTTP topology worked out of the box. Base settings load in EVERY environment and take
    /// precedence over a code default, so that arrangement silently disabled transport security for
    /// metadata retrieval everywhere - a production deployment that simply omitted an override inherited
    /// the relaxation without anything saying so. It was first narrowed to appsettings.Development.json
    /// and is now gone from both: every listener in this system terminates TLS in every environment, so
    /// the Development authority is <c>https://localhost:5104</c> and BOTH settings files state
    /// <see langword="true"/> here. Do not reintroduce a <see langword="false"/> anywhere - a local
    /// certificate is what the local bring-up supplies, not a relaxation.
    /// </para>
    /// </remarks>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// The issuers a token may declare. Empty means the issuer is taken from the authority's
    /// discovery metadata instead.
    /// </summary>
    /// <remarks>
    /// Getter-only and pre-initialized. The configuration binder populates an existing collection
    /// through its getter, so this binds from an array exactly as a settable property would while
    /// remaining impossible to replace with null. Empty is legal here, unlike
    /// <see cref="ValidAudiences"/>, because an issuer is discoverable from the authority's metadata
    /// whereas an audience is not.
    /// </remarks>
    public IList<string> ValidIssuers { get; } = [];

    /// <summary>
    /// The audiences a token may be addressed to. At least one entry is required.
    /// </summary>
    /// <remarks>
    /// Required because an audience cannot be discovered from metadata and audience validation is on
    /// by default, so a service that starts with none configured would reject every token it is ever
    /// given. That is a misconfiguration a startup check can catch precisely, which is far better than
    /// a service that reports healthy and then refuses all authenticated traffic.
    /// </remarks>
    public IList<string> ValidAudiences { get; } = [];

    /// <summary>
    /// Whether inbound claim types are remapped to their long-form legacy names. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// False keeps the claim types exactly as the token carries them, which is what the sibling
    /// appsettings.json declares and what makes an authorization rule read the same way as the token
    /// that satisfies it.
    /// </remarks>
    public bool MapInboundClaims { get; set; }

    /// <summary>
    /// Validates the authority and the declared issuers and audiences, including the one contradiction
    /// no single attribute can see.
    /// </summary>
    /// <param name="validationContext">
    /// Supplied by the validation infrastructure and not consulted; every rule is expressed in terms
    /// of this instance alone.
    /// </param>
    /// <returns>One result per problem found, or an empty sequence when the section is usable.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        ValidationResult? authority = AddressValidation.Check(
            Authority,
            $"{SectionName}:{nameof(Authority)}",
            nameof(Authority));

        if (authority is not null)
        {
            yield return authority;
        }

        // The genuine cross-property check, and the reason this type implements the interface at all:
        // requiring HTTPS metadata while pointing at a plain HTTP authority is a contradiction the
        // handler would only discover on its first metadata fetch, long after startup succeeded.
        else if (RequireHttpsMetadata
            && Uri.TryCreate(Authority, UriKind.Absolute, out Uri? parsedAuthority)
            && !string.Equals(parsedAuthority.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            yield return new ValidationResult(
                $"'{SectionName}:{nameof(RequireHttpsMetadata)}' is true but "
                    + $"'{SectionName}:{nameof(Authority)}' is not an https address. Either publish the "
                    + "authority over https or set RequireHttpsMetadata to false for a plain-http "
                    + "topology.",
                [nameof(RequireHttpsMetadata), nameof(Authority)]);
        }

        for (int index = 0; index < ValidIssuers.Count; index++)
        {
            ValidationResult? issuer = AddressValidation.Check(
                ValidIssuers[index],
                $"{SectionName}:{nameof(ValidIssuers)}:{index}",
                nameof(ValidIssuers));

            if (issuer is not null)
            {
                yield return issuer;
            }
        }

        if (ValidAudiences.Count == 0)
        {
            yield return new ValidationResult(
                $"'{SectionName}:{nameof(ValidAudiences)}' requires at least one audience. An audience "
                    + "is not discoverable from the authority's metadata, and audience validation is "
                    + "enabled, so a service with none configured would reject every token.",
                [nameof(ValidAudiences)]);

            yield break;
        }

        for (int index = 0; index < ValidAudiences.Count; index++)
        {
            // An audience is an opaque identifier rather than an address, so it is checked only for
            // being present. Imposing a URI shape on it would reject the plain identifier the sibling
            // appsettings.json actually declares.
            if (string.IsNullOrWhiteSpace(ValidAudiences[index]))
            {
                yield return new ValidationResult(
                    $"'{SectionName}:{nameof(ValidAudiences)}:{index}' is blank. Remove the entry or "
                        + "give it the audience identifier this service's tokens are addressed to.",
                    [nameof(ValidAudiences)]);
            }
        }
    }
}

/// <summary>
/// Shared syntax checking for the configured service addresses in this file.
/// </summary>
/// <remarks>
/// Internal, static and deliberately tiny. It exists so the two option types above apply one identical
/// rule instead of two copies that can drift, and it lives in this file because the scope of this
/// folder is one file. It adds no public surface and holds no state.
/// </remarks>
internal static class AddressValidation
{
    /// <summary>
    /// Checks that a configured address is present, is an absolute http or https URI, and is shaped
    /// like a base address: no embedded credentials, no query string and no fragment.
    /// </summary>
    /// <param name="value">The configured value, which may be null, empty or whitespace.</param>
    /// <param name="configurationKey">
    /// The full configuration path, quoted into the message so an operator can find the offending key
    /// without consulting source.
    /// </param>
    /// <param name="memberName">The property name to attribute the failure to.</param>
    /// <returns>
    /// <see langword="null"/> when the address is usable, otherwise the single result describing why
    /// it is not.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The scheme is constrained to http or https because both consumers of these addresses require
    /// it: a gRPC channel and an HTTP client base address each reject anything else. Reachability is
    /// never attempted here - that is a health-check concern, and dialling out from a configuration
    /// validator would turn a transient condition into a failure to start.
    /// </para>
    /// <para>
    /// THESE ARE BASE ADDRESSES, WHICH IS WHY THE LAST THREE RULES EXIST. A base address carries a
    /// scheme, a host, a port and optionally a path prefix, and nothing else. The three components
    /// rejected below are each accepted by <see cref="Uri.TryCreate(string?, UriKind, out Uri?)"/>
    /// and each would fail late and confusingly rather than at startup:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="Uri.UserInfo"/> - credentials embedded in the address, as in
    /// <c>http://user:secret@host:5102</c>. Rejecting it is a secrets control, not tidiness. Such a
    /// value would be a credential living in configuration under a key named for an address, it would
    /// be copied into every log line, exception message and trace that records the request URI, and
    /// this system has no use for it in any case: every internal edge is authenticated with a bearer
    /// token minted by the Security service, and no address here is ever a place to put a secret.
    /// </description></item>
    /// <item><description>
    /// A query string. A base address is composed with per-request paths, and a query on the base
    /// would be silently dropped by both consumers rather than merged, so a caller believing it had
    /// configured one would be wrong with no diagnostic.
    /// </description></item>
    /// <item><description>
    /// A fragment. Fragments are never transmitted, so one here can only be a mistake.
    /// </description></item>
    /// </list>
    /// <para>
    /// EVERY MESSAGE NAMES THE KEY AND NEVER ECHOES THE VALUE. The configuration key is enough for an
    /// operator to find the offending setting, whereas echoing the value would put a credential
    /// bearing address into the startup log - which is exactly the failure the userinfo rule exists
    /// to prevent, reintroduced by the error message that reports it. The scheme rule quotes the
    /// parsed scheme alone, which is a fixed token from a small set and carries nothing.
    /// </para>
    /// </remarks>
    internal static ValidationResult? Check(string? value, string configurationKey, string memberName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new ValidationResult(
                $"'{configurationKey}' is required and must not be blank.",
                [memberName]);
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed))
        {
            return new ValidationResult(
                $"'{configurationKey}' must be an absolute URI, for example "
                    + "'https://service-host:5102'. The configured value is not quoted here because a "
                    + "rejected address may carry a credential.",
                [memberName]);
        }

        // Uri.Scheme is already lower-cased by the parser, so an ordinal comparison is both correct
        // and free of any culture dependency.
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return new ValidationResult(
                $"'{configurationKey}' must use the http or https scheme; '{parsed.Scheme}' is not "
                    + "supported by a gRPC channel or an HTTP client base address.",
                [memberName]);
        }

        if (parsed.UserInfo.Length > 0)
        {
            return new ValidationResult(
                $"'{configurationKey}' must not embed credentials in the address. Remove the "
                    + "'user:password@' portion: every edge in this system is authenticated with a "
                    + "bearer token issued by the Security service, and an address carrying "
                    + "credentials would leak them into logs and traces. The configured value is "
                    + "deliberately not quoted here.",
                [memberName]);
        }

        if (!string.IsNullOrEmpty(parsed.Query))
        {
            return new ValidationResult(
                $"'{configurationKey}' is a base address and must not carry a query string. A query "
                    + "on a base address is dropped rather than merged when a per-request path is "
                    + "composed onto it, so it would have no effect and no diagnostic.",
                [memberName]);
        }

        if (!string.IsNullOrEmpty(parsed.Fragment))
        {
            return new ValidationResult(
                $"'{configurationKey}' is a base address and must not carry a fragment. A fragment "
                    + "is never sent to a server, so one here can only be a mistake.",
                [memberName]);
        }

        return null;
    }
}

/// <summary>
/// Bounds on the REST projection of the two upstream gRPC contracts.
/// </summary>
/// <remarks>
/// <b>A RESOURCE BOUND, NOT A PERFORMANCE SETTING</b> - the distinction matters because no performance
/// objective may be asserted anywhere in this refactor (AAP 0.8.5). The projection forwards a
/// server-streaming upstream method as one JSON array, and this gateway is the process every request in
/// the system passes through, so an unbounded upstream response is an unbounded amount of work at the one
/// place that cannot afford it. Exceeding the bound abandons the document WITHOUT its closing bracket, so
/// a caller detects an incomplete answer rather than receiving a truncated array that reads as complete.
/// </remarks>
public sealed class RestProjectionOptions
{
    /// <summary>
    /// The largest number of elements the projection will forward for one streamed response.
    /// </summary>
    /// <remarks>
    /// Generous rather than tight: a legitimate chunked retrieval produces one element per CHUNK rather
    /// than per row, so a realistic result is nowhere near this and the bound is a backstop against a
    /// runaway upstream. The range starts at one because a bound of zero would refuse every streamed
    /// response including an empty one, which reads as a total outage; refusing that at startup is the
    /// fail-fast posture this service applies to every structural fault.
    /// </remarks>
    [Range(1, int.MaxValue)]
    public int MaxStreamedElements { get; set; } = 10_000;

    /// <summary>
    /// The shipped collection window for a projected stream that never ends on its own.
    /// </summary>
    /// <remarks>
    /// COMFORTABLY INSIDE THE PER-ATTEMPT OUTBOUND BUDGET, which is the whole point of the value. The
    /// resilience package's per-attempt timeout is ten seconds and is what fired before this window
    /// existed, so anything close to it would reproduce the defect; two seconds answers an idle stream
    /// promptly while leaving a wide margin.
    /// </remarks>
    public static readonly TimeSpan DefaultStreamCollectionWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The exclusive upper bound on <see cref="StreamCollectionWindow"/>.
    /// </summary>
    /// <remarks>
    /// THE RESILIENCE PACKAGE'S OWN PER-ATTEMPT TIMEOUT, and the reason the bound exists rather than being
    /// left to judgement: a window at or beyond it cannot fire first, so the outbound pipeline's timeout
    /// wins and the projection is back to answering a server fault for an idle stream. Refusing that value
    /// at startup is the fail-fast posture this service applies to every structural fault.
    /// </remarks>
    internal static readonly TimeSpan MaximumStreamCollectionWindow =
        GatewayOptions.OutboundCallOptions.MinimumRequestTimeout;

    /// <summary>
    /// How long the projection collects from a stream that never completes on its own, before answering
    /// with what it has. Defaults to <see cref="DefaultStreamCollectionWindow"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS APPLIES TO ONE OPERATION AND NOT TO STREAMS IN GENERAL.</b> The expression event stream is a
    /// SUBSCRIPTION: the upstream ends it only when the client goes away, so a request/response projection
    /// of it has to decide for itself when the collection is complete. Every other projected stream
    /// terminates itself - a retrieval ends with its final-marked chunk - and applying a window to one of
    /// those would truncate a legitimate result, so the window is opt-in per operation rather than a
    /// property of streaming.
    /// </para>
    /// <para>
    /// A WINDOW THAT EXPIRES IS A COMPLETE ANSWER RATHER THAN A TRUNCATION. The caller asked for the
    /// records available now, so the collected sequence - empty included - is exactly what the operation
    /// means, and the response is a well-formed 200. That is the opposite of exceeding
    /// <see cref="MaxStreamedElements"/>, which abandons the document precisely so the caller can tell it
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
    /// BOTH BOUNDS ARE NAMED IN THE MESSAGE, and the key is named too, because an operator reading a
    /// refusal to start needs the setting to change rather than a description of a category of fault.
    /// </remarks>
    internal IEnumerable<ValidationResult> Validate(string configurationKeyPrefix, string memberName)
    {
        if (StreamCollectionWindow <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"'{configurationKeyPrefix}:{nameof(StreamCollectionWindow)}' is "
                    + $"{StreamCollectionWindow}, which collects nothing at all: a projected subscription "
                    + "would answer an empty collection for every request, including one with records "
                    + "waiting. It must be greater than zero.",
                [memberName]);
        }
        else if (StreamCollectionWindow >= MaximumStreamCollectionWindow)
        {
            yield return new ValidationResult(
                $"'{configurationKeyPrefix}:{nameof(StreamCollectionWindow)}' is "
                    + $"{StreamCollectionWindow}, which is not below the "
                    + $"{MaximumStreamCollectionWindow} per-attempt outbound timeout. A window that "
                    + "cannot fire first leaves the outbound pipeline's timeout to end an idle "
                    + "subscription, which reaches the caller as a server fault rather than as the empty "
                    + "collection the operation means.",
                [memberName]);
        }
    }
}
