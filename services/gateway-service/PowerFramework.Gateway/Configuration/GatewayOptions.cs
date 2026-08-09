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
//         "DataServices": "https://localhost:5102",   <-- https, because h2 needs ALPN
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
//       "ValidIssuers": [ "https://localhost:5104" ]
//     } } }
//
//   ONLY THE HOST CHANGES THERE, AND RequireHttpsMetadata IS NOT OVERRIDDEN. Security's listener is
//   TLS in every environment - its issuance path authenticates the caller with a client certificate,
//   which cannot be presented on a plaintext listener at all, and the key set fetched beneath this
//   authority is the system's trust bootstrap - so the loopback form is https and the base file's
//   RequireHttpsMetadata of true stays inherited. A false there would relax nothing that needs
//   relaxing: the setting only PERMITS a plaintext metadata address, it does not make one exist.
//
//   So an environment that configures nothing gets the strict shape and fails to start naming the
//   missing authority, instead of inheriting a relaxation it never asked for. The binding contract is
//   unchanged either way: the same keys, the same spellings, the same path.
//
//   THE SCHEME IS https EVEN ON LOOPBACK, AND THAT IS A CORRECTION WORTH KNOWING ABOUT. An earlier
//   revision put a plain-HTTP loopback authority here together with a RequireHttpsMetadata relaxation
//   to permit it. Security's listener is TLS in development as well as deployed, and functionally so:
//   `POST /v1/tokens` authenticates its caller with a CLIENT CERTIFICATE, which cannot be requested or
//   presented on a plaintext listener at all, so on plain http no service in the system - this one
//   included - could obtain a first token. RequireHttpsMetadata therefore stays true in every
//   environment and appears in the base file only. A developer's Security instance presents a
//   SELF-SIGNED certificate, trusted through the host trust store; the relaxation this system grants
//   is an untrusted issuer, never cleartext, and there is no setting anywhere in Gateway that turns
//   certificate validation off.
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
    /// <c>POST /v1/tokens</c> on the Security service is protected by <c>mutualTls</c> and by nothing
    /// else, because <b>a caller cannot present a bearer token in order to obtain its first bearer
    /// token</b>. Gateway is one of the two services that request tokens, so without a client
    /// certificate to present it cannot obtain one, and every authenticated call it would make is
    /// unreachable. The contract has required this since it was authored; this group is what makes it
    /// configurable.
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
        /// <b>5102</b> over <b>https</b>, which is that service's single listener.
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
        /// THE SCHEME IS THE LOAD-BEARING HALF OF THIS VALUE, NOT THE PORT. DataServices serves the
        /// C-03 and C-04 gRPC contracts, which REQUIRE HTTP/2, and the anonymous <c>/health</c> plus
        /// <c>/v1/ping</c>, which the readiness gate probes with HTTP/1.1 (C-L). A <b>cleartext</b>
        /// Kestrel endpoint cannot carry both: configured for both versions without TLS it disables
        /// HTTP/2 outright and says so at startup, and configured for <c>Http2</c> alone it answers an
        /// HTTP/1.1 <c>GET</c> with <c>400</c>. With TLS the ambiguity does not arise, because ALPN
        /// selects the version per connection - so DataServices declares ONE endpoint,
        /// <c>https://+:5102</c> with <c>Protocols</c> <c>Http1AndHttp2</c>, and this address names it.
        /// </para>
        /// <para>
        /// A cleartext <c>http://…:5102</c> here therefore fails every RPC on this edge with the
        /// HTTP/2 error <c>HTTP_1_1_REQUIRED</c> before the request reaches a method, which surfaces as
        /// a transport fault naming no operation. An earlier revision of this file answered the same
        /// constraint with a second, undeclared h2c port outside the fixed 5101-5105 band; that band is
        /// the one the attached environment fixes and the reserved 5103 DesignSystem slot is the only
        /// spare in it, so the parallel band was withdrawn in favour of TLS on the assigned port. This
        /// value and <see cref="GatewayOptions.HealthProbes"/>'s DataServices entry consequently name
        /// the SAME listener; they stay separate members because one is a call edge and the other is an
        /// observation, which is a topology distinction rather than an addressing one.
        /// </para>
        /// </remarks>
        [Required(AllowEmptyStrings = false)]
        public string DataServices { get; set; } = "https://localhost:5102";

        /// <summary>
        /// The Security service's REST address. Defaults to the local topology's port 5104.
        /// </summary>
        /// <remarks>
        /// REST is the transport on this edge so that token issuance and key publication use ordinary
        /// HTTP semantics, which is what lets a stock bearer handler fetch the published key material
        /// with no bespoke code. HTTP semantics, not the plain-http scheme: the default here is https,
        /// and appsettings.Development.json overrides it to http for the loopback bring-up only.
        /// Overridden per environment through <c>Gateway__Upstreams__Security</c>. This address
        /// identifies the service; it never carries credentials of any kind.
        /// </remarks>
        /// <remarks>
        /// <para>
        /// THE SCHEME IS <c>https</c> AND THAT IS NOT INTERCHANGEABLE WITH <c>http</c> HERE. Security
        /// is this system's trust bootstrap: it is the sole token issuer, its token endpoint
        /// authenticates callers with a client certificate - which cannot be presented on a plaintext
        /// listener at all - and the key set every other service verifies against is fetched from it.
        /// A plaintext address on this edge means an on-path attacker can substitute the published keys
        /// and have all three verifying services accept tokens the attacker signed, while behaving
        /// exactly as designed. See <c>OpenApi/security.v1.yaml</c>'s <c>servers</c> block, which
        /// records the same decision on the publishing side.
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
    /// THE PORTS ARE THE ASSIGNED PORTS, BECAUSE EACH SERVICE HAS EXACTLY ONE LISTENER. A readiness
    /// probe is an HTTP/1.1 <c>GET</c>, and Persistence on 5101, DataServices on 5102 and Security on
    /// 5104 each serve it on the same TLS endpoint that carries their contract traffic - ALPN selects
    /// HTTP/1.1 for the probe and HTTP/2 for gRPC on the one connection-by-connection basis.
    /// </para>
    /// <para>
    /// All three default to the local topology over https for the same reason the upstream addresses
    /// do: a missing override must not silently downgrade a probe onto a channel an attacker can
    /// rewrite, since a forged readiness verdict opens the dependency gate early. The loopback
    /// plain-http topology is an override in appsettings.Development.json.
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
        /// </remarks>
        [Required(AllowEmptyStrings = false)]
        public string Persistence { get; set; } = "https://localhost:5101";

        /// <summary>
        /// The DataServices service's REST base address, probed for readiness. Port 5102.
        /// </summary>
        /// <remarks>
        /// The same listener <see cref="UpstreamAddresses.DataServices"/> names, and deliberately a
        /// separate member rather than a shared one: this address authorises exactly one anonymous
        /// <c>GET /health</c> for the C-10 aggregate, while that one carries the C-03 and C-04 call
        /// edge. Collapsing the two would make an observation indistinguishable from an invocation in
        /// configuration, which is the distinction the two groups exist to keep.
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
    /// to the loopback address of the local topology. That put an environment-specific plain-HTTP
    /// address into source, where it is invisible to a deployment review, and it meant a deployment
    /// that configured nothing still got a usable-looking authority pointing at a host that does not
    /// exist for it. With no default, the presence rule below rejects the omission by name, so an
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
    /// THE RELAXATION IS SCOPED TO DEVELOPMENT, and it was not always. An earlier form of the base
    /// appsettings.json set this to <see langword="false"/> so that the local plain-HTTP topology
    /// worked out of the box. Base settings load in EVERY environment and take precedence over a code
    /// default, so that arrangement silently disabled transport security for metadata retrieval
    /// everywhere - a production deployment that simply omitted an override inherited the relaxation
    /// without anything saying so. It now lives in appsettings.Development.json alongside the
    /// plain-HTTP authority it exists for, so the two travel together and neither reaches an
    /// environment that did not ask for it. Do not move either one back into the base file.
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
                    + "'http://service-host:5102'. The configured value is not quoted here because a "
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
