// ==================================================================================================
//  OrchestrationTemplateCoherenceTests - THE MECHANICAL GUARD OVER orchestration/.env.example
//  ------------------------------------------------------------------------------------------------
//  SUBJECT     One file: orchestration/.env.example, the committed template for the uncommitted
//              environment file the Compose manifest reads.
//  AUTHORITY   Agent Action Plan 0.3.2.2 (the fixed port map), 0.6.6.3 (the token topology - exactly
//              one signing secret, held by Security), 0.7.3 constraints C-B, C-F and C-G,
//              docs/SECRETS.md, and the authoritative option surfaces of the four services as
//              declared by their own appsettings files.
//
//  WHY THIS FILE NEEDS A TEST AT ALL, WHICH IS THE FIRST QUESTION A READER SHOULD HAVE
//  ------------------------------------------------------------------------------------------------
//  It is prose and assignments, so nothing about it compiles and nothing about it runs. Every defect
//  it can carry is therefore invisible to the compiler, invisible to the linter, and invisible until
//  an operator follows it. Three classes of defect matter, and all three have already occurred here:
//
//    * A DOCUMENTED KEY PATH THAT RESOLVES TO NOTHING. The template's own parity rule requires every
//      variable to name the configuration key it maps onto. A name that no option binds is worse than
//      a missing one: it reads as configuration, an operator edits it expecting an effect, and nothing
//      happens. An absolute JWKS URL was declared here and no verifier had a reader for it.
//    * AN INSTRUCTION THAT CANNOT PRODUCE USABLE MATERIAL. The template once documented symmetric
//      random bytes for a signing key the service imports as an RSA private key. Following it produced
//      a stack that refused to start, and the natural "fix" - switching the algorithm to an HMAC family
//      - would have published the signing secret in the JWK set.
//    * A DEPLOYMENT-TIME OVERRIDE OF A PRESERVED BEHAVIOURAL DEFAULT. The template once narrowed the
//      Gateway capability mask, so the running system disagreed with both the code default and the
//      legacy call site it reproduces. That is the silent behavioural change C-B forbids.
//
//  Each assertion below turns one of those into a build-time failure that names the offending line.
//
//  WHY A CONTRACTS TEST OWNS IT
//  ------------------------------------------------------------------------------------------------
//  The invariants compare the template against the option surfaces of all four services at once, so
//  no single service's test project is where they can be stated. This project already owns the
//  published cross-service boundary. It is also the only test project that compiles while the four
//  service Program.cs files are still pending, so a guard placed anywhere else could not run.
//
//  WHAT THESE ASSERTIONS DELIBERATELY DO NOT DO
//  ------------------------------------------------------------------------------------------------
//  They read no environment variable, open no socket and start no host: they are assertions about the
//  authored text of one file plus the eight authored settings files, which is what makes them runnable
//  in a clean checkout with nothing brought up. They also assert nothing about, and name none of, the
//  four deferred services - C-D is satisfied by absence rather than by a blocklist.
// ==================================================================================================

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Assertions over <c>orchestration/.env.example</c>: its declared format, its variable roster, the
/// resolvability of every configuration key path it documents, the absence of secret material and of
/// instructions that cannot produce usable material, and its agreement with the port map and with the
/// preserved behavioural defaults the services declare.
/// </summary>
public sealed class OrchestrationTemplateCoherenceTests
{
    /// <summary>Repository-root marker: the solution file, in the .NET 10 XML format.</summary>
    private const string SolutionMarkerFileName = "PowerFramework.slnx";

    /// <summary>Repository-root marker: the central package-version manifest.</summary>
    private const string PackageVersionMarkerFileName = "Directory.Packages.props";

    /// <summary>The orchestration directory, relative to the repository root.</summary>
    private const string OrchestrationDirectoryName = "orchestration";

    /// <summary>The template file name.</summary>
    private const string TemplateFileName = ".env.example";

    /// <summary>The services directory, relative to the repository root.</summary>
    private const string ServicesDirectoryName = "services";

    /// <summary>Base settings file name, authored for every service.</summary>
    private const string BaseSettingsFileName = "appsettings.json";

    /// <summary>Environment overlay file name, authored for every service.</summary>
    private const string DevelopmentSettingsFileName = "appsettings.Development.json";

    /// <summary>
    /// Reading the settings files the way the host reads them: comments skipped, trailing commas
    /// tolerated. The authored files use both.
    /// </summary>
    private static readonly JsonDocumentOptions SettingsDocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Every settings file the documented key paths are resolved against.</summary>
    /// <remarks>
    /// Both files of all four services, because a variable may legitimately target a key that only the
    /// environment overlay declares.
    /// </remarks>
    private static readonly string[] SettingsFileRelativePaths =
    [
        $"persistence-service/PowerFramework.Persistence/{BaseSettingsFileName}",
        $"persistence-service/PowerFramework.Persistence/{DevelopmentSettingsFileName}",
        $"dataservices-service/PowerFramework.DataServices/{BaseSettingsFileName}",
        $"dataservices-service/PowerFramework.DataServices/{DevelopmentSettingsFileName}",
        $"security-service/PowerFramework.Security/{BaseSettingsFileName}",
        $"security-service/PowerFramework.Security/{DevelopmentSettingsFileName}",
        $"gateway-service/PowerFramework.Gateway/{BaseSettingsFileName}",
        $"gateway-service/PowerFramework.Gateway/{DevelopmentSettingsFileName}",
    ];

    /// <summary>The complete expected variable roster - twenty-eight names, no twenty-ninth.</summary>
    /// <remarks>
    /// <para>
    /// Spelled out rather than derived, so that both an addition and a removal are findings. The
    /// template's parity rule runs in both directions: a variable the manifest references but the
    /// template omits makes bring-up fail or silently resolve empty, and a variable the template
    /// declares that nothing references is dead configuration.
    /// </para>
    /// <para>
    /// The roster is four families, and reading it as one flat list hides why the count is what it is.
    /// TWELVE are the service-shape variables an operator always sets: the environment name, the three
    /// in-network REST addresses, the TWO gRPC addresses, the two token-topology names, the signing
    /// secret, and the three Persistence storage and pool settings.
    /// </para>
    /// <para>
    /// THE TWO <c>_GRPC_URL</c> VARIABLES ARE NOT A DUPLICATION OF THE <c>_BASE_URL</c> PAIR, even though
    /// each pair resolves to the same address. Persistence and DataServices each declare ONE endpoint,
    /// <c>Http1AndHttp2</c> over <c>https</c>, so <c>/health</c>, <c>/v1/ping</c>, REST and the gRPC
    /// contracts all answer on the documented port and ALPN selects between them. The two variables are
    /// kept apart because each names a distinct EDGE rather than a distinct listener: a <c>_GRPC_URL</c> is
    /// what a SERVICE dials for a contract call and a <c>_BASE_URL</c> is what a PROBE and the end-to-end
    /// suite read, and they bind different service settings. Folding them into one would let a change of
    /// call address silently move a probe.
    /// </para>
    /// <para>
    /// TWO are the shared server-certificate paths, which are deliberately ONE pair for the whole stack
    /// rather than one pair per listener. FIVE are CLIENT-side mutual-TLS material, which is a different
    /// thing from the server pair and cannot be folded onto it: the client certificate Gateway presents
    /// to Security, the one DataServices presents to Security, and the issuer whose client certificates
    /// Security is willing to accept. All seven are empty in the TEMPLATE rather than optional in effect:
    /// every listener in this estate is <c>https</c>, so the server pair has to be supplied for the
    /// documented bring-up to start at all, and an example path is indistinguishable from a real one to a
    /// reader. The five client-side paths are genuinely optional, because JWT is the standing mechanism
    /// and mutual TLS is the documented per-pair fallback.
    /// </para>
    /// <para>
    /// THREE are Gateway's health-probe addresses, which stay separate from its upstream call addresses
    /// because one is what a caller dials and the other is what an observer reads - and because C-10
    /// bounds the aggregate at three upstreams while Gateway's call roster has only two. Their values
    /// additionally differ from the call addresses, since a probe is an HTTP/1.1 <c>GET</c> and must
    /// therefore reach the <c>Http1</c> endpoint rather than the <c>Http2</c> one.
    /// </para>
    /// <para>
    /// GATEWAY_LOCALE and GATEWAY_CAPABILITY_FLAGS are the last two, and they are the ones that restate a
    /// preserved behavioural default rather than configuring anything new; the pairing below holds them to
    /// the value the service itself declares.
    /// </para>
    /// <para>
    /// SECURITY_MTLS_CLIENT_REVOCATION_MODE belongs with the client-side material above - it says how
    /// thoroughly a presented client certificate's revocation is checked, and it is separate from the
    /// anchor because a deployment can trust an issuer without being able to reach its revocation data.
    /// </para>
    /// <para>
    /// 🔴 NO SECTION-PATH VARIABLE BELONGS IN THIS ROSTER - EVERY ENTRY IS A FLAT SCREAMING-SNAKE NAME.
    /// <c>Security__CallerAuthorizations__3__{Caller,Audience,Scopes__0..2}</c>
    /// is the shape that grants the end-to-end suite's identity through the environment at a literal array
    /// index, and it is redundant in <c>Development</c>, where Security's own overlay already states that
    /// grant at that index ALONGSIDE the credential entry the caller needs in order to authenticate for it -
    /// which no environment block supplies - and wrong in <c>Production</c>, where the base settings file
    /// registers no such caller, so the block creates a permission nothing can authenticate to exercise. The
    /// index is additionally load-bearing across two files under merge-by-index: a fourth row added to the
    /// settings file is silently merged INTO the block rather than appended, yielding a hybrid
    /// row that no duplicate guard can see. The grant therefore lives only where the caller is registered.
    /// </para>
    /// <para>
    /// THREE GROUPS REACH NO APPLICATION SETTING AT ALL, and they are the only ones: the four
    /// <c>_HOST_PORT</c> variables, the four <c>_HOST_BIND</c> variables and the eight resource ceilings.
    /// Each is consumed by the manifest itself rather than bound by an options type, so no settings file
    /// can restate or contradict one.
    /// </para>
    /// <para>
    /// The <c>_HOST_PORT</c> four are the LEFT half of each <c>ports:</c> mapping. The container half
    /// stays fixed because it is what Kestrel binds, and a variable for it could only ever disagree with
    /// the settings file. These exist because the documented second-stack recipe
    /// (<c>docker compose -p pfw-2</c>) separates networks, volumes and container names but NOT published
    /// ports, so with four fixed publications it collides on all four and cannot start.
    /// </para>
    /// <para>
    /// <c>SECURITY_HOST_PORT</c> IS THE ONE EXCEPTION, AND DELIBERATELY SO. It additionally composes the
    /// DEFAULT of <c>SECURITY_PUBLIC_BASE_URL</c>, which the manifest maps onto
    /// <c>Security__PublishedOrigins__0</c> - how Security declares the HOST origin it answers on, and
    /// therefore how a host-side consumer receives a <c>jwks_uri</c> it can actually fetch, since the
    /// in-network issuer name does not resolve there. Defaulting that declaration from the SAME variable
    /// that publishes the port is what keeps the two from disagreeing when no origin is named explicitly:
    /// remapping the port cannot leave the declared origin pointing at the old one. A deployment fronted
    /// by a proxy names the origin outright instead, which is why the variable exists as well.
    /// </para>
    /// <para>
    /// AND <c>INTERNAL_TLS_CA_PATH</c> IS NOT A RESPELLING OF <c>INTERNAL_TLS_TRUSTED_CA_PATH</c> - the two
    /// differ in MEANING. That second spelling belongs to the manifest and carries a path INSIDE a
    /// container; declaring it here, in a file of operator-host paths, points every internal channel and
    /// every image probe at a file that does not exist on either side. The manifest declares the anchor as a
    /// Compose secret and owns the container-side path as a literal, and this variable names the SOURCE on
    /// the operator's host - which is why it also joins the must-be-empty set below.
    /// </para>
    /// </remarks>
    private static readonly string[] ExpectedVariableNames =
    [
        "ASPNETCORE_ENVIRONMENT",
        "DATASERVICES_BASE_URL",
        "DATASERVICES_GRPC_URL",
        "DATASERVICES_MTLS_CERT_PATH",
        "DATASERVICES_MTLS_KEY_PATH",
        "GATEWAY_CAPABILITY_FLAGS",
        "GATEWAY_HEALTH_PROBE_DATASERVICES_URL",
        "GATEWAY_HEALTH_PROBE_PERSISTENCE_URL",
        "GATEWAY_HEALTH_PROBE_SECURITY_URL",
        "GATEWAY_LOCALE",
        "GATEWAY_MTLS_CERT_PATH",
        "GATEWAY_MTLS_KEY_PATH",

        // The host side of Gateway's published port. See the remarks: these four are the left half of a
        // `ports:` mapping and reach no application setting.
        "GATEWAY_HOST_PORT",

        // The trust anchor every internal client verifies its peer against, as a path ON THE HOST which
        // the manifest projects as a Compose secret. Four consumers downstream of that one projection:
        // Gateway__InternalTls__TrustedCaPath, DataServices__InternalTls__TrustedCaPath, Persistence's
        // unprefixed InternalTls__TrustedCaPath, and the INTERNAL_TLS_TRUSTED_CA_PATH each image's own
        // HEALTHCHECK reads - all four stated as the literal projected path by the manifest rather than
        // as a variable. Its absence was the defect behind an entire class of unreachable-upstream
        // failure: the documented topology issues every internal certificate from a LOCAL authority that
        // no container's OS trust store carries, so without the anchor every internal channel refuses
        // the certificate it is presented.
        "INTERNAL_TLS_CA_PATH",

        "DATASERVICES_HOST_PORT",
        "PERSISTENCE_HOST_PORT",
        "SECURITY_HOST_PORT",

        // WHICH HOST INTERFACE EACH PUBLISHED PORT IS OFFERED ON, and these four exist because the
        // answer used to be "every one of them". A two-field `ports:` mapping binds the host half to
        // 0.0.0.0, so Persistence, DataServices and Security were reachable DIRECTLY from any host on
        // the same network, going around Gateway and defeating the sole-ingress topology at the network
        // layer. All four now default to loopback, which is what every access route the documentation
        // publishes actually uses.
        "DATASERVICES_HOST_BIND",
        "GATEWAY_HOST_BIND",
        "PERSISTENCE_HOST_BIND",
        "SECURITY_HOST_BIND",

        // The switch that makes the ONE documented bring-up command sufficient on a fresh volume. It is
        // true in the template and FALSE in Persistence's own settings file, and that asymmetry is the
        // design rather than a drift: the code default is opted out, so nothing that fails to set it
        // changes behaviour, and the template is the single visible place the stack opts in. It is
        // consequently NOT a member of PreservedDefaults - that list compares a variable against a
        // settings key it must AGREE with, and this pair is required to differ.
        "PERSISTENCE_APPLY_MIGRATIONS_ON_STARTUP",

        "PERSISTENCE_BASE_URL",
        "PERSISTENCE_GRPC_URL",
        "PERSISTENCE_SQLITE_DATA_DIRECTORY",
        "PERSISTENCE_TRANSPOOL_KEEPALIVE",
        "PERSISTENCE_TRANSPOOL_KEEPALIVE_EXPIRE_SECONDS",
        "SECURITY_BASE_URL",
        "SECURITY_JWT_AUDIENCE",
        "SECURITY_JWT_ISSUER",

        // ⚠ A PUBLISHED LOCATION, NOT A SECOND IDENTITY, and the distinction is why both names are
        // declared. SECURITY_JWT_ISSUER above is the `iss` claim every verifier compares byte for
        // byte; this one is the address a HOST-SIDE consumer reaches Security on, which the
        // discovery document composes `jwks_uri` and `token_endpoint` from when a request arrives on
        // it. The document previously composed both from the issuer alone, so a consumer outside the
        // Compose network was handed a key-set address naming a host only resolvable inside it.
        "SECURITY_PUBLIC_BASE_URL",
        // ⚠ A PATH TO THE KEY, NOT THE KEY. This variable used to carry the RSA private key
        // itself, which the manifest put into the container ENVIRONMENT - where `docker compose
        // config` renders it in cleartext, `docker inspect` returns it to anyone who can reach
        // the daemon socket, and every child process inherits it. It now names a host FILE that
        // Compose projects read-only and the service reads through the `_FILE` convention.
        "SECURITY_JWT_SIGNING_KEY_PATH",

        // The file form of the OPTIONAL retiring key. A pass-through rather than a projection,
        // because the retiring slot is empty in the steady state and a Compose secret's `file:`
        // must name a path that already exists - so declaring one would abort every ordinary
        // bring-up to serve a rollover that is not happening.
        "SECURITY_JWT_RETIRING_SIGNING_KEY_FILE",

        // THE ROLLOVER TRIO. Replacing the estate's one signing key in place refuses every token minted
        // under the previous one until all three verifiers' cached key sets refresh, so a rollover
        // publishes the outgoing key alongside the incoming one and each carries its own `kid`. Two of
        // these three are IDENTIFIERS rather than material - a `kid` is published anonymously in the key
        // set by design - which is why only the first joins the must-be-empty set below, and why the
        // exactly-one-signing-secret row distinguishes material from identifier rather than matching on
        // the name. The active identifier is a variable at all because the incoming key needs a NEW id and
        // the shipped one is baked into the image's settings file: without it no rollover could be
        // performed through this manifest.
        "SECURITY_JWT_RETIRING_SIGNING_KEY",
        "SECURITY_JWT_RETIRING_SIGNING_KEY_ID",
        "SECURITY_JWT_SIGNING_KEY_ID",

        // THE ISSUANCE-ROSTER SECRETS - one per caller that may obtain a token. Required, and a missing
        // one REFUSES THE HOST: Security resolves every secret its roster names at startup and reports
        // the roster position of any that resolves to nothing. Three rather than five, because
        // Persistence requests no token at all (it reads the published key set anonymously) and the third
        // is the operator/end-to-end identity the Development overlay registers.
        // Both caller credentials are PATHS for the same reason as the signing key above; the third
        // is the optional operator identity, which keeps its value form and gains a file form.
        "SECURITY_CLIENT_SECRET_GATEWAY_PATH",
        "SECURITY_CLIENT_SECRET_DATASERVICES_PATH",
        "SECURITY_CLIENT_SECRET",
        "SECURITY_CLIENT_SECRET_FILE",

        "SECURITY_MTLS_CLIENT_CA_PATH",
        "SECURITY_MTLS_CLIENT_REVOCATION_MODE",

        // THE TWO SETTINGS THAT MADE REVOCATION A DECISION INSTEAD OF A HARDCODED VALUE, plus the control
        // that substitutes for it while the shipped posture cannot check.
        //
        // INTERNAL_TLS_REVOCATION_MODE is one variable for three services because all three verify
        // against the single authority INTERNAL_TLS_CA_PATH names - a posture differing between them
        // would leave the estate with the weaker guarantee and the appearance of the stronger. Both
        // default to the value the settings files ship, so a deployment that copies this template
        // unedited behaves identically to one that sets neither.
        //
        // SECURITY_MTLS_CLIENT_MAX_LIFETIME_DAYS is not a duplicate of the revocation mode above it. It
        // bounds how long a caller certificate may DECLARE itself valid for, which is the only bound
        // available on a compromised credential while nothing consults a CRL - and it is enforced rather
        // than asserted in a comment, which was the state this replaced.
        "SECURITY_MTLS_CLIENT_MAX_LIFETIME_DAYS",
        "INTERNAL_TLS_REVOCATION_MODE",

        // EIGHT PER-SERVICE HALVES, NOT ONE SHARED PAIR. The shared `TLS_CERTIFICATE_PATH` /
        // `TLS_CERTIFICATE_KEY_PATH` gave all four services one cryptographic identity: the key read
        // out of any container was the key every other service presented, and the certificate had to
        // name every origin so it validated as any peer. Each service now supplies its own pair.
        "SECURITY_TLS_CERTIFICATE_PATH",
        "SECURITY_TLS_CERTIFICATE_KEY_PATH",
        "PERSISTENCE_TLS_CERTIFICATE_PATH",
        "PERSISTENCE_TLS_CERTIFICATE_KEY_PATH",
        "DATASERVICES_TLS_CERTIFICATE_PATH",
        "DATASERVICES_TLS_CERTIFICATE_KEY_PATH",
        "GATEWAY_TLS_CERTIFICATE_PATH",
        "GATEWAY_TLS_CERTIFICATE_KEY_PATH",

        // THE EIGHT RESOURCE CEILINGS - a memory value and a CPU value per service. Like the four
        // `_HOST_PORT` and four `_HOST_BIND` entries, they reach NO application setting: they are cgroup
        // limits Compose applies to the container, so nothing binds them to an options type and nothing
        // in a settings file can restate them.
        //
        // WHY THEY ARE ON THE ROSTER AT ALL. Without a ceiling a container reports the cgroup-v2 "max"
        // sentinel as its limit, so the collector has no collection pressure and retains: nineteen
        // identical full REST projections drove Gateway's resident set from 573 MiB to about 1,500 MiB
        // with no plateau, while the same run under a declared ceiling plateaued at about 85 per cent of
        // it with zero OOM kills and every response byte-complete. That growth was retention rather than
        // a leak, and the ceiling is what gives the collector a reason to collect. The CPU value is here
        // for a second reason that is not throttling: the runtime derives its thread-pool and server-GC
        // heap counts from the processor count it observes, and an unquoted container observes the whole
        // host.
        //
        // THEY ARE NOT IN THE MUST-BE-EMPTY SET AND MUST NOT BE. They are quantities rather than material
        // or paths to it, and the template states each one so that the manifest's default is visible to
        // an operator reading only the template - the same treatment the four ports and four binds get.
        // Nothing here asserts a performance objective (AAP 0.8.5): a ceiling states the most a service
        // MAY consume, not what it needs or how fast it is.
        //
        // THERE IS NO CEILING FOR ANY DEFERRED SERVICE (C-D), and that absence is load bearing rather
        // than incidental: a memory limit for a service this phase does not build would be the first
        // orchestration artifact to imply one exists.
        "SECURITY_MEMORY_LIMIT",
        "PERSISTENCE_MEMORY_LIMIT",
        "DATASERVICES_MEMORY_LIMIT",
        "GATEWAY_MEMORY_LIMIT",
        "SECURITY_CPU_LIMIT",
        "PERSISTENCE_CPU_LIMIT",
        "DATASERVICES_CPU_LIMIT",
        "GATEWAY_CPU_LIMIT",
    ];

    /// <summary>
    /// The variables whose value must be EMPTY in the committed template, because a populated one
    /// would be secret material or a path to it.
    /// </summary>
    /// <remarks>
    /// An example key is indistinguishable from a real one to a reader, and placeholder keys have a
    /// long history of reaching production unchanged - so these are left empty rather than pre-filled
    /// (C-F, docs/SECRETS.md).
    /// </remarks>
    private static readonly string[] MustBeEmptyVariableNames =
    [
        // THE PATHS AND FILE REFERENCES SHIP EMPTY FOR THE SAME REASON THE MATERIAL DID. They name a
        // location on the OPERATOR'S machine, which this template cannot know, and a pre-filled mount
        // point that no manifest provides stops the documented bring-up dead while reading as configured.
        "SECURITY_JWT_SIGNING_KEY_PATH",
        "SECURITY_JWT_RETIRING_SIGNING_KEY_FILE",
        "SECURITY_CLIENT_SECRET_GATEWAY_PATH",
        "SECURITY_CLIENT_SECRET_DATASERVICES_PATH",
        "SECURITY_CLIENT_SECRET_FILE",

        // THE RETIRING KEY IS MATERIAL TOO, and it is additionally empty for a second reason: a populated
        // one declares a rollover this template is not in the middle of, and Security refuses to start on
        // it because the matching identifier would be absent. Its two IDENTIFIER siblings are deliberately
        // NOT here - the retiring identifier is empty in the template but is not secret, and the active
        // identifier carries a real default that must equal the settings value it repeats.
        "SECURITY_JWT_RETIRING_SIGNING_KEY",
        // EIGHT PER-SERVICE HALVES, NOT ONE SHARED PAIR. The shared `TLS_CERTIFICATE_PATH` /
        // `TLS_CERTIFICATE_KEY_PATH` gave all four services one cryptographic identity: the key read
        // out of any container was the key every other service presented, and the certificate had to
        // name every origin so it validated as any peer. Each service now supplies its own pair.
        "SECURITY_TLS_CERTIFICATE_PATH",
        "SECURITY_TLS_CERTIFICATE_KEY_PATH",
        "PERSISTENCE_TLS_CERTIFICATE_PATH",
        "PERSISTENCE_TLS_CERTIFICATE_KEY_PATH",
        "DATASERVICES_TLS_CERTIFICATE_PATH",
        "DATASERVICES_TLS_CERTIFICATE_KEY_PATH",
        "GATEWAY_TLS_CERTIFICATE_PATH",
        "GATEWAY_TLS_CERTIFICATE_KEY_PATH",

        // THE TRUST ANCHOR IS HERE FOR A REASON THAT IS NOT SECRECY, and saying so matters because the
        // file is the PUBLIC half of an authority and is genuinely not sensitive. It is empty because it
        // is a path on the OPERATOR'S OWN MACHINE, which this template cannot know - and because its
        // previous non-empty value was a path inside a container, which is exactly the confusion that
        // made every internal channel verify against a file nothing had put there.
        "INTERNAL_TLS_CA_PATH",

        // THE FIVE CLIENT-SIDE MUTUAL-TLS PATHS BELONG HERE FOR A SECOND REASON BESIDES SECRECY, and it
        // is the sharper of the two: Gateway and DataServices load their pair EAGERLY at startup with
        // X509Certificate2.CreateFromPemFile and turn an IOException into a fail-fast host failure. A
        // pre-filled mount point that no manifest provides therefore stops the documented bring-up dead,
        // while reading as configured to anyone auditing the template. Both-empty is the supported state
        // meaning "this deployment presents no client certificate", which is the estate's default because
        // JWT is the standing mechanism and mutual TLS is the documented per-pair fallback - a deployment
        // that wants it mounts the pair and fills these in.
        "SECURITY_MTLS_CLIENT_CA_PATH",
        "GATEWAY_MTLS_CERT_PATH",
        "GATEWAY_MTLS_KEY_PATH",
        "DATASERVICES_MTLS_CERT_PATH",
        "DATASERVICES_MTLS_KEY_PATH",
    ];

    /// <summary>
    /// Configuration keys that legitimately have no settings-file declaration, with the reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each is environment-only BY DESIGN: the two certificate paths and the two secrets must never
    /// appear in an authored settings file (C-F), and the signing key additionally binds as a FLAT key
    /// because <c>__</c> is the section separator and <c>SECURITY_JWT_</c> would otherwise be read as
    /// three nested sections.
    /// </para>
    /// <para>
    /// <c>Jwt:MetadataAddress</c> is environment-only for a different reason, and it is a LIVE key
    /// rather than an indulgence: Persistence reads it straight off its bearer section and, when it is
    /// non-blank, assigns it to the handler
    /// (<c>services/persistence-service/PowerFramework.Persistence/Program.cs:376</c> and <c>:391</c>).
    /// Declaring it in the settings file would mean giving it a VALUE, and any value at all overrides
    /// the discovery address - so the only way to express "left unset so discovery resolves from the
    /// authority", which is the documented default, is to omit it. It is the same shape as
    /// <c>Sqlite:Password</c>: real, read, and deliberately absent from every authored file.
    /// </para>
    /// </remarks>
    private static readonly string[] EnvironmentOnlyConfigurationKeys =
    [
        "Kestrel:Certificates:Default:Path",
        "Kestrel:Certificates:Default:KeyPath",
        "Sqlite:Password",
        "Jwt:MetadataAddress",
        "SECURITY_JWT_SIGNING_KEY",

        // The retiring key is flat for exactly the same reason the active one is: SECURITY_JWT_ carries no
        // double underscore, so section binding cannot reach it and Security reads it by literal name. Its
        // IDENTIFIER is not here, because that one binds through the ordinary section path
        // Security__RetiringSigningKeyId and IS declared in Security's settings file.
        "SECURITY_JWT_RETIRING_SIGNING_KEY",
    ];

    /// <summary>
    /// Key spellings the template mentions only in order to FORBID them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The template tells a reader not to rename the flat signing-key variable into a section path, and
    /// naming the wrong spelling is how that instruction is made actionable. Without this allowance the
    /// resolvability check below would report the prohibition itself as the defect it exists to prevent.
    /// Both signing inputs are here for that one reason: the retiring key is flat for exactly the same
    /// double-underscore cause as the active one, so the template forbids its section spelling too, and a
    /// rollover is the moment an operator is most likely to reach for the regularised name.
    /// </para>
    /// <para>
    /// <c>Jwt__JwksPath</c> is here for the neighbouring reason: it is named in order to record that
    /// Persistence has NO SUCH KEY and nothing reads one. Documenting a key that does not exist is worth
    /// more than saying nothing - an operator carrying it forward from another template needs to be told
    /// it has no effect rather than left wondering why - and the resolvability check would otherwise
    /// report that explanation as an unresolvable key path, which is the same category error as reporting
    /// a prohibition.
    /// </para>
    /// </remarks>
    private static readonly string[] DocumentedOnlyToBeForbidden =
        ["Security__SigningKey", "Security__RetiringSigningKey", "Jwt__JwksPath"];

    /// <summary>Every port the map assigns, as it may appear inside a template VALUE.</summary>
    /// <remarks>
    /// <para>
    /// 5103 is absent deliberately: the map reserves it as a commented Phase-2 slot, so a value naming it
    /// would be an address for a service that does not exist.
    /// </para>
    /// <para>
    /// THE ROSTER IS FOUR PORTS AND NOT SIX, WHICH IS THE ONE-ENDPOINT-PER-SERVICE RULE MADE ASSERTABLE.
    /// A six-port roster would add 5111 and 5112, separate <c>Http2</c>-only gRPC endpoints for
    /// Persistence and DataServices placed outside the documented band, so that an address the attached
    /// environment never named could be added without moving one it did. That is not available: AAP
    /// 0.3.2.2 assigns contracts C-05..C-08 to 5101 and C-03/C-04 to 5102, so a gRPC contract answering
    /// anywhere else is not on the port the map gives it. Each of those two services binds ONE TLS
    /// endpoint with <c>Protocols: Http1AndHttp2</c>, where ALPN carries the readiness probe and the gRPC
    /// contracts together. Every documented port keeps exactly the meaning it was given, and a template
    /// value naming 5111 or 5112 is an address no listener answers.
    /// </para>
    /// </remarks>
    private static readonly int[] AssignedPorts = [5101, 5102, 5104, 5105];

    /// <summary>Each address variable, the port it must name, and the scheme it must use.</summary>
    /// <remarks>
    /// <para>
    /// EVERY SCHEME IS <c>https</c>, AND THAT IS ASSERTED RATHER THAN TOLERATED. The environment gates
    /// each service's readiness on <c>curl -sf http://localhost:&lt;port&gt;/health</c>, which fixes the
    /// probe SHAPE - an anonymous <c>GET</c> of <c>/health</c> on that port answering 200 - and not the
    /// transport beneath it, so honouring it with a trust anchor added is not one of the deviations AAP
    /// 0.8.3 governs. What a template naming an <c>http</c> listener WOULD describe is a stack carrying
    /// bearer credentials and a verification key set in cleartext, which is CWE-319 on surfaces the
    /// decomposition itself created (AAP 0.1.4). The issuer is held to the same scheme as the base address
    /// because the two are compared byte for byte against the <c>iss</c> claim: a one-character divergence
    /// rejects every token in the system, silently, until the first authenticated request.
    /// </para>
    /// <para>
    /// A BASE-URL VARIABLE AND ITS <c>_GRPC_URL</c> SIBLING NOW NAME THE SAME PORT, AND BOTH SURVIVE. Each
    /// gRPC-serving service binds one endpoint carrying both protocol versions, so the two variables no
    /// longer differ in ADDRESS - they differ in the EDGE they configure, and each binds a different
    /// service setting: the base URL reaches a readiness probe, the gRPC URL is the call address a caller
    /// builds a channel from. Keeping them separate keeps a call address out of a probe setting, which is
    /// a substitution neither side would report.
    /// </para>
    /// </remarks>
    private static readonly AddressExpectation[] AddressExpectations =
    [
        new("PERSISTENCE_BASE_URL", 5101, Uri.UriSchemeHttps),
        new("PERSISTENCE_GRPC_URL", 5101, Uri.UriSchemeHttps),
        new("DATASERVICES_BASE_URL", 5102, Uri.UriSchemeHttps),
        new("DATASERVICES_GRPC_URL", 5102, Uri.UriSchemeHttps),
        new("SECURITY_BASE_URL", 5104, Uri.UriSchemeHttps),
        new("SECURITY_JWT_ISSUER", 5104, Uri.UriSchemeHttps),
        new("SECURITY_PUBLIC_BASE_URL", 5104, Uri.UriSchemeHttps),
        new("GATEWAY_HEALTH_PROBE_PERSISTENCE_URL", 5101, Uri.UriSchemeHttps),
        new("GATEWAY_HEALTH_PROBE_DATASERVICES_URL", 5102, Uri.UriSchemeHttps),
        new("GATEWAY_HEALTH_PROBE_SECURITY_URL", 5104, Uri.UriSchemeHttps),
    ];

    /// <summary>
    /// The variables that restate a preserved behavioural default, paired with the service settings key
    /// that default is authored at.
    /// </summary>
    /// <remarks>
    /// These variables exist so an operator can SEE the knob, not so orchestration can move it. Any
    /// divergence between the two sides means the deployed system disagrees with the code default, and
    /// for a legacy-preserving default that divergence is the silent behavioural change C-B forbids.
    /// </remarks>
    private static readonly PreservedDefault[] PreservedDefaults =
    [
        new(
            "GATEWAY_CAPABILITY_FLAGS",
            $"gateway-service/PowerFramework.Gateway/{BaseSettingsFileName}",
            "Gateway:CapabilityFlags"),
        new(
            "GATEWAY_LOCALE",
            $"gateway-service/PowerFramework.Gateway/{BaseSettingsFileName}",
            "Gateway:Locale"),

        // The active signing-key identifier. The template restates it so that a rollover can change it
        // without rebuilding the image, and this row is what stops the restatement from drifting: the two
        // spellings of one `kid` disagreeing would publish a key set under one identifier while stamping
        // tokens with the other, which every verifier reads as a key it does not have.
        new(
            "SECURITY_JWT_SIGNING_KEY_ID",
            $"security-service/PowerFramework.Security/{BaseSettingsFileName}",
            "Security:SigningKeyId"),
    ];

    /// <summary>Every variable that must be left empty.</summary>
    public static TheoryData<string> AllMustBeEmptyVariableNames { get; } =
        BuildStringData(MustBeEmptyVariableNames);

    /// <summary>Every address variable, with its expected port and scheme.</summary>
    public static TheoryData<string, int, string> AllAddressExpectations { get; } =
        BuildAddressData();

    /// <summary>Every preserved default, with the settings key it must agree with.</summary>
    public static TheoryData<string, string, string> AllPreservedDefaults { get; } =
        BuildPreservedDefaultData();

    /// <summary>
    /// Every assignment obeys the format the template declares for itself: a bare
    /// <c>KEY=value</c> line, with no <c>export</c>, no quoting and no line continuation.
    /// </summary>
    /// <remarks>
    /// This is not house style. The format is what makes the single-line base64 signing key the only
    /// expressible shape for that variable - a multi-line PEM block cannot be written here at all - and
    /// it is what lets reserved URI characters pass through unquoted. A quoted or continued line would
    /// silently change how Compose's dotenv parser reads the value.
    /// </remarks>
    [Fact]
    public void TemplateObeysTheBareKeyEqualsValueFormatItDeclares()
    {
        string[] lines = ReadTemplateLines();
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> failures = [];

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            int number = index + 1;

            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                failures.Add($"L{number}: begins with 'export', which the declared format excludes.");
                continue;
            }

            int separator = line.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0 || !IsVariableName(line[..separator]))
            {
                failures.Add($"L{number}: is neither blank, a whole-line comment, nor 'KEY=value'.");
                continue;
            }

            string name = line[..separator];
            string value = line[(separator + 1)..];

            if (!seen.Add(name))
            {
                failures.Add(
                    $"L{number}: '{name}' is assigned twice. The later line wins, so the file would "
                        + "state one value while a reader deduced another.");
            }

            if (value.Contains('"', StringComparison.Ordinal)
                || value.Contains('\'', StringComparison.Ordinal))
            {
                failures.Add($"L{number}: '{name}' is quoted; values are taken literally here.");
            }

            if (value.EndsWith('\\'))
            {
                failures.Add(
                    $"L{number}: '{name}' ends with a continuation, which this format does not support.");
            }

            if (value.Length != value.Trim().Length)
            {
                failures.Add(
                    $"L{number}: '{name}' has leading or trailing whitespace, which becomes part of the "
                        + "value because nothing is trimmed.");
            }
        }

        Assert.True(failures.Count == 0, Describe("format violations", failures));
    }

    /// <summary>The template declares exactly the expected roster, with nothing added or missing.</summary>
    [Fact]
    public void TemplateDeclaresExactlyTheExpectedVariableRoster()
    {
        string[] declared = [.. ReadTemplateVariables().Keys.Order(StringComparer.Ordinal)];
        string[] expected = [.. ExpectedVariableNames.Order(StringComparer.Ordinal)];

        string[] missing = [.. expected.Except(declared, StringComparer.Ordinal)];
        string[] unexpected = [.. declared.Except(expected, StringComparer.Ordinal)];

        Assert.True(
            missing.Length == 0 && unexpected.Length == 0,
            $"The orchestration roster has drifted. Missing: {Join(missing)}. Unexpected: "
                + $"{Join(unexpected)}. A missing variable makes bring-up fail or silently resolve to an "
                + "empty value; an unexpected one is configuration nothing consumes.");
    }

    /// <summary>Every secret-valued variable is present and empty.</summary>
    [Theory]
    [MemberData(nameof(AllMustBeEmptyVariableNames))]
    public void TemplateLeavesEverySecretValuedVariableEmpty(string variableName)
    {
        IReadOnlyDictionary<string, string> variables = ReadTemplateVariables();

        Assert.True(
            variables.TryGetValue(variableName, out string? value),
            $"'{variableName}' is not declared. It is part of the roster, and its absence would make the "
                + "manifest resolve it to an empty value with no statement that it had to be supplied.");

        Assert.True(
            value!.Length == 0,
            $"'{variableName}' carries a value. It must be empty in the committed template: an example is "
                + "indistinguishable from real material to a reader, and placeholder credentials have a "
                + "long history of reaching production unchanged.");
    }

    /// <summary>
    /// No line of the template - assignment or comment - carries secret-shaped text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Comments are searched as well as values, because a key pasted into an example is exactly as
    /// disclosed as one pasted into an assignment. The base64 test is applied to values only: prose
    /// legitimately contains long words, whereas a value that is a long encoded run is key material.
    /// </para>
    /// <para>
    /// The armour marker is the discriminator rather than a phrase like "private key". This file's whole
    /// subject IS a private key, so it discusses one in English throughout and a phrase match would
    /// report the documentation as the defect. The five-hyphen BEGIN delimiter is never natural prose,
    /// and it is what every secret scanner keys on, so it is both the precise signal and the one that
    /// matters operationally. It covers a key, a certificate and an ASCII-armoured block alike.
    /// </para>
    /// </remarks>
    [Fact]
    public void TemplateCarriesNoSecretShapedText()
    {
        const string ArmourMarker = "-----BEGIN";

        string text = ReadTemplateText();
        List<string> failures = [];

        if (text.Contains(ArmourMarker, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"the text contains the armour delimiter '{ArmourMarker}'.");
        }

        foreach ((string name, string value) in ReadTemplateVariables())
        {
            if (LooksLikeEncodedMaterial(value))
            {
                failures.Add($"'{name}' holds a long encoded run, which is the shape of key material.");
            }
        }

        Assert.True(failures.Count == 0, Describe("secret-shaped text", failures));
    }

    /// <summary>
    /// The signing-key instructions generate ASYMMETRIC material, and no symmetric-generation
    /// instruction appears anywhere in the file.
    /// </summary>
    /// <remarks>
    /// Security signs with RS256 over a closed RS-family allow-list and imports the configured material
    /// as an RSA private key, so random bytes fail the startup usability check and the host refuses to
    /// start. Measured: base64 of 32 random bytes and of 64 random bytes are both rejected by the import
    /// path, while a PKCS#8 PEM key, a PKCS#1 PEM key and a single-line base64 of the DER encoding are
    /// all accepted at 2048 bits.
    /// </remarks>
    [Fact]
    public void TemplateInstructsAsymmetricKeyGenerationForTheSigningKey()
    {
        string text = ReadTemplateText();

        Assert.True(
            text.Contains("openssl genpkey", StringComparison.Ordinal),
            "The template documents no asymmetric key generation. An operator has to be told how to "
                + "produce material Security can import, or the first bring-up fails at startup with "
                + "nothing to act on.");

        // ------------------------------------------------------------------------------------------
        // `openssl rand` MUST NOT BE OFFERED FOR THE SIGNING KEY, AND MUST BE OFFERED FOR THE ROSTER
        // SECRETS. The assertion is therefore per line rather than over the whole file.
        //
        // The distinction is real and getting it wrong costs a bring-up either way. The signing key is
        // an ASYMMETRIC PRIVATE KEY that Security imports: random bytes cannot be imported at all, so
        // an operator who reached for `openssl rand` gets a host that refuses to start. The issuance
        // roster's secrets are SHARED SECRETS compared byte for byte and imported by nothing, so
        // random bytes are exactly right - and an operator told nothing about how to produce one
        // invents something weaker.
        //
        // An earlier form of this row banned the string outright, which was correct while the signing
        // key was the only secret in the template and became wrong the moment the roster arrived. Per
        // line keeps the original intent - no random-bytes recipe anywhere near the signing key - while
        // admitting the recipe the roster needs.
        // ------------------------------------------------------------------------------------------
        string[] offendingLines =
        [
            .. text.Split('\n')
                .Where(static line =>
                    line.Contains("openssl rand", StringComparison.Ordinal)
                    && line.Contains("SIGNING", StringComparison.Ordinal)),
        ];

        Assert.True(
            offendingLines.Length == 0,
            "The template offers a random-bytes recipe on a line naming the signing key. Random bytes "
                + "cannot be imported as an asymmetric private key, so an operator who followed it "
                + "would get a host that refuses to start.");

        Assert.True(
            text.Contains("openssl rand -base64 32", StringComparison.Ordinal),
            "The template documents no way to generate an issuance-roster secret. Every roster entry "
                + "that names a secret key must have one supplied or the host refuses to start, so an "
                + "operator told nothing here either cannot bring the stack up or invents something "
                + "weaker.");

        // AND THE WARNING AGAINST CONFUSING THE TWO IS STILL PRESENT, which is the part a reader acts on
        // when a startup refusal names the signing key.
        Assert.True(
            text.Contains("fails the startup usability check", StringComparison.Ordinal),
            "The template no longer states that random material is rejected for the signing key at "
                + "startup. That sentence is what turns a refusal an operator has already hit into an "
                + "action.");
    }

    /// <summary>
    /// No variable exists for an absolute JWK-set address, because no verifier can read one.
    /// </summary>
    /// <remarks>
    /// All three verifiers reach the key set through the discovery document beneath their bearer
    /// authority and expose no key-set setting of their own - not even a relative
    /// <c>Jwt:JwksPath</c> on Persistence, which nothing would read. So an absolute URL has nowhere
    /// to go in any of the three shapes.
    /// </remarks>
    [Fact]
    public void TemplateDeclaresNoAbsoluteJwksAddressVariable()
    {
        string[] offending =
        [
            .. ReadTemplateVariables()
                .Keys
                .Where(static name => name.Contains("JWKS", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            offending.Length == 0,
            $"The template declares {Join(offending)}. No service binds an absolute key-set address, so "
                + "such a variable reads as configuration, gets edited, and changes nothing.");
    }

    /// <summary>
    /// Every configuration key path the template documents resolves to a real option leaf.
    /// </summary>
    /// <remarks>
    /// This is the assertion the template's own parity rule asks for, made mechanical. It harvests every
    /// <c>Section__Key</c> token in the file and resolves it against the leaves of all eight authored
    /// settings files, allowing only the environment-only keys and the spellings the template mentions
    /// in order to forbid them. It is what catches a key path left behind when a section is re-rooted.
    /// </remarks>
    [Fact]
    public void EveryDocumentedConfigurationKeyPathResolvesToADeclaredOptionLeaf()
    {
        HashSet<string> declared = ReadAllDeclaredLeafPaths();
        HashSet<string> shapes = ReadDeclaredShapesThroughArrays();
        List<string> failures = [];

        foreach (string token in HarvestDocumentedKeyPaths(ReadTemplateText()))
        {
            if (DocumentedOnlyToBeForbidden.Contains(token, StringComparer.Ordinal))
            {
                continue;
            }

            string path = token.Replace("__", ":", StringComparison.Ordinal);

            if (declared.Contains(path)
                || EnvironmentOnlyConfigurationKeys.Contains(path, StringComparer.Ordinal))
            {
                continue;
            }

            // An indexed reference addresses an ELEMENT, while the settings file authors a SHAPE, so the
            // two are compared with indices normalized away at every depth. This resolves both array
            // kinds - a scalar array such as Security:Audiences:0, and an array of objects such as
            // Security:CallerAuthorizations:3:Caller - while still holding the member name after the
            // index to the shape the element actually declares.
            if (shapes.Contains(NormalizeAwayIndexes(path)))
            {
                continue;
            }

            failures.Add($"'{token}' resolves to '{path}', which no settings file declares.");
        }

        Assert.True(failures.Count == 0, Describe("unresolvable key paths", failures));
    }

    /// <summary>Every address variable names the port the map assigns, on the scheme it serves.</summary>
    [Theory]
    [MemberData(nameof(AllAddressExpectations))]
    public void TemplateAddressResolvesToTheAssignedListener(
        string variableName,
        int expectedPort,
        string expectedScheme)
    {
        string value = RequireVariable(variableName);

        Assert.True(
            Uri.TryCreate(value, UriKind.Absolute, out Uri? address),
            $"'{variableName}' is not an absolute address.");

        Assert.Equal(expectedScheme, address!.Scheme);

        Assert.True(
            address.Port == expectedPort,
            $"'{variableName}' names port {address.Port}, but the map assigns {expectedPort}. A caller "
                + "pointed at a port nobody serves fails at first use, three layers from its cause.");
    }

    /// <summary>
    /// The end-to-end suite's compiled-in fallback for each address variable names the same listener the
    /// template does — same scheme, same port.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS CHECK IS HERE, IN A .NET TEST, READING TYPESCRIPT. There are two places a default address
    /// for these services is written: the orchestration template, which an operator fills in, and
    /// <c>tests/e2e/fixtures/service-endpoints.ts</c>, whose <c>resolveBaseUrl</c> fallbacks are what the
    /// suite uses when no variable is set — which is the documented local run. Nothing else compares them.
    /// The suite's own type check cannot: a string literal is a valid string whatever it names, and the
    /// TypeScript has no access to the settings files that decide which listeners exist.
    /// </para>
    /// <para>
    /// AND THE TWO HAD DIVERGED, WHICH IS WHY IT IS WORTH A TEST RATHER THAN A CONVENTION. The fixture
    /// defaulted Security to an <c>http</c> address while Security declares exactly one listener and it
    /// terminates TLS — so the documented default run could not bootstrap at all: a client certificate is
    /// the only caller authentication <c>POST /v1/tokens</c> accepts, and a certificate cannot be
    /// presented where no handshake happens. Seventy lines of commentary directly above the literal argued
    /// correctly for <c>https</c>; the literal said otherwise, and no gate read either.
    /// </para>
    /// <para>
    /// SCHEME AND PORT ONLY — the HOST is deliberately not compared. The template names container DNS
    /// (<c>security-service</c>) because Compose resolves it; the fixture names <c>localhost</c> because a
    /// developer runs it from the host. Those are the same listener reached from two places, so requiring
    /// them to match would encode a topology assumption rather than a coherence rule. What must agree is
    /// what an operator cannot override away: whether TLS is spoken, and which port answers.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllAddressExpectations))]
    public void EndToEndFallbackAddressNamesTheSameListenerAsTheTemplate(
        string variableName,
        int expectedPort,
        string expectedScheme)
    {
        string? fallback = ReadEndToEndFallback(variableName);

        if (fallback is null)
        {
            // Not every template variable is an address the suite resolves - SECURITY_JWT_ISSUER is
            // Security's own identity rather than an address a spec calls - so absence is not a failure.
            // What would be a failure is a fallback that exists and names a different listener.
            return;
        }

        Assert.True(
            Uri.TryCreate(fallback, UriKind.Absolute, out Uri? address),
            $"The end-to-end fallback for '{variableName}' is not an absolute address.");

        Assert.Equal(expectedScheme, address!.Scheme);

        Assert.True(
            address.Port == expectedPort,
            $"The end-to-end fallback for '{variableName}' names port {address.Port}, but the map assigns "
                + $"{expectedPort}. The documented run uses this fallback, so a wrong port here fails the "
                + "suite before any assertion in it is reached.");
    }

    /// <summary>No value names a port outside the assigned set.</summary>
    /// <remarks>
    /// The check is on VALUES only. Comments discuss the reserved Phase-2 slot and record a rejected
    /// parallel-band alternative, and recording a rejected alternative is a documentation requirement
    /// (C-K) rather than a live address.
    /// </remarks>
    [Fact]
    public void NoTemplateValueNamesAnUnassignedPort()
    {
        List<string> failures = [];

        foreach ((string name, string value) in ReadTemplateVariables())
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? address) || address.IsDefaultPort)
            {
                continue;
            }

            if (!AssignedPorts.Contains(address.Port))
            {
                failures.Add(
                    $"'{name}' names port {address.Port}, which the map does not assign to any service.");
            }
        }

        Assert.True(failures.Count == 0, Describe("unassigned ports", failures));
    }

    /// <summary>
    /// A variable that restates a preserved default states the same value the service declares.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllPreservedDefaults))]
    public void TemplateRestatesPreservedDefaultsWithoutMovingThem(
        string variableName,
        string settingsRelativePath,
        string keyPath)
    {
        string declared = RequireVariable(variableName);
        JsonNode? node = FindNode(LoadSettings(settingsRelativePath), keyPath);

        Assert.True(
            node is JsonValue,
            $"'{settingsRelativePath}' declares no value at '{keyPath}', so '{variableName}' has nothing "
                + "to agree with.");

        string authored = node!.ToJsonString().Trim('"');

        Assert.True(
            string.Equals(declared, authored, StringComparison.Ordinal),
            $"'{variableName}' is '{declared}' while '{keyPath}' is '{authored}'. This variable exists so "
                + "an operator can see the knob, not so orchestration can move it: for a default that "
                + "reproduces legacy behaviour, a deployment-time divergence is a silent behavioural "
                + "change.");
    }

    /// <summary>
    /// The audience the template templates is one the ingress actually validates.
    /// </summary>
    [Fact]
    public void TemplateIngressAudienceIsTheOneGatewayValidates()
    {
        string templated = RequireVariable("SECURITY_JWT_AUDIENCE");
        JsonObject gateway = LoadSettings($"gateway-service/PowerFramework.Gateway/{BaseSettingsFileName}");
        JsonNode? node = FindNode(gateway, "Authentication:Schemes:Bearer:ValidAudiences");

        Assert.True(node is JsonArray, "Gateway declares no 'ValidAudiences' list to compare against.");

        string[] validated =
        [
            .. ((JsonArray)node!)
                .Where(static entry => entry is JsonValue)
                .Select(static entry => entry!.ToJsonString().Trim('"')),
        ];

        Assert.True(
            validated.Contains(templated, StringComparer.Ordinal),
            $"The template issues for audience '{templated}', which Gateway does not validate "
                + $"({Join(validated)}). A token minted for an audience the ingress rejects fails closed, "
                + "as a 401 that looks like a caller error.");
    }

    /// <summary>
    /// Exactly one signing secret MINTS, at most one more is published for a rollover, and no per-service
    /// signing key exists under any spelling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Security is the sole issuer; the other three hold verification material only. A per-service signing
    /// key would make each of them a second issuer, and the security properties of a sole-issuer topology
    /// depend on there being exactly one (AAP 0.6.6.3).
    /// </para>
    /// <para>
    /// <b>MATERIAL AND IDENTIFIER ARE SEPARATED, BECAUSE AN EARLIER SHAPE OF THIS ROW CONFLATED THEM.</b>
    /// It matched every variable whose name contained <c>SIGNING_KEY</c> and required exactly one, which
    /// read as a sole-issuer assertion but was in fact a NAMING assertion: a <c>kid</c> variable would have
    /// failed it even though a <c>kid</c> is published anonymously in the key set and authorises nothing,
    /// and a second signing secret named without that substring would have passed it. What matters is how
    /// many pieces of signing MATERIAL the template carries and whose they are, so identifiers - the names
    /// ending <c>_KEY_ID</c> - are excluded and the material set is then pinned exactly.
    /// </para>
    /// <para>
    /// TWO MATERIAL VARIABLES RATHER THAN ONE, AND THE SECOND IS NOT A SECOND ISSUER. A rollover publishes
    /// the outgoing key beside the incoming one so that tokens still in flight keep verifying; minting uses
    /// the ACTIVE key only, and the retiring key is verification material this service happens to hold
    /// because it was its own key a moment ago. Both are prefixed <c>SECURITY_</c>, which is the property
    /// that actually carries the sole-issuer guarantee, so that prefix is asserted too - a
    /// <c>GATEWAY_JWT_SIGNING_KEY</c> would fail this row on the exact ground the row exists for.
    /// </para>
    /// </remarks>
    [Fact]
    public void TemplateDeclaresExactlyOneMintingSecretAndAtMostOneRetiringOne()
    {
        string[] signingMaterialVariables =
        [
            .. ReadTemplateVariables()
                .Keys
                .Where(static name => name.Contains("SIGNING_KEY", StringComparison.OrdinalIgnoreCase))
                .Where(static name => !name.EndsWith("_KEY_ID", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal),
        ];

        // ⚠ NONE OF THESE CARRIES MATERIAL ANY MORE, WHICH STRENGTHENS THIS ROW RATHER THAN WEAKENING IT.
        // The active key is named by a PATH and projected as a file; the retiring key keeps a value form
        // for the length of a rollover and gains a preferred file form. So the census now counts the
        // variables that REFER to signing material, and there is still exactly one active and at most one
        // retiring - which is the sole-issuer property (AAP 0.6.6.3) this row exists to hold.
        Assert.Equal(
            (string[])
            [
                "SECURITY_JWT_RETIRING_SIGNING_KEY",
                "SECURITY_JWT_RETIRING_SIGNING_KEY_FILE",
                "SECURITY_JWT_SIGNING_KEY_PATH",
            ],
            signingMaterialVariables);

        string[] foreignHolders =
        [
            .. signingMaterialVariables
                .Where(static name => !name.StartsWith("SECURITY_", StringComparison.Ordinal)),
        ];

        Assert.True(
            foreignHolders.Length == 0,
            $"The template declares {Join(foreignHolders)} as signing material for a service other than "
                + "Security. Security is the sole issuer and the other three hold verification material "
                + "only; a per-service signing key makes its holder a second issuer.");
    }

    /// <summary>Builds theory data from a name list.</summary>
    /// <param name="names">The names to project.</param>
    /// <returns>One row per name.</returns>
    private static TheoryData<string> BuildStringData(string[] names)
    {
        TheoryData<string> data = [];

        foreach (string name in names)
        {
            data.Add(name);
        }

        return data;
    }

    /// <summary>Builds theory data for the address expectations.</summary>
    /// <returns>One row per address variable.</returns>
    private static TheoryData<string, int, string> BuildAddressData()
    {
        TheoryData<string, int, string> data = [];

        foreach (AddressExpectation expectation in AddressExpectations)
        {
            data.Add(expectation.VariableName, expectation.Port, expectation.Scheme);
        }

        return data;
    }

    /// <summary>Builds theory data for the preserved defaults.</summary>
    /// <returns>One row per preserved default.</returns>
    private static TheoryData<string, string, string> BuildPreservedDefaultData()
    {
        TheoryData<string, string, string> data = [];

        foreach (PreservedDefault preserved in PreservedDefaults)
        {
            data.Add(preserved.VariableName, preserved.SettingsRelativePath, preserved.KeyPath);
        }

        return data;
    }

    /// <summary>Reports whether a token is a legal environment-variable name.</summary>
    /// <param name="candidate">The text before the first <c>=</c>.</param>
    /// <returns><see langword="true"/> when it is a legal name.</returns>
    private static bool IsVariableName(string candidate)
    {
        if (candidate.Length == 0 || char.IsAsciiDigit(candidate[0]))
        {
            return false;
        }

        foreach (char character in candidate)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reports whether a value has the shape of encoded key material.</summary>
    /// <param name="value">The declared value.</param>
    /// <returns><see langword="true"/> when it is a long base64 or hex run.</returns>
    private static bool LooksLikeEncodedMaterial(string value)
    {
        if (value.Length < 24)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character)
                && character != '+'
                && character != '/'
                && character != '=')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Harvests every <c>Section__Key</c> token from the template text.
    /// </summary>
    /// <param name="text">The whole template.</param>
    /// <returns>The distinct tokens, ordered.</returns>
    /// <remarks>
    /// A token is a maximal run of letters, digits and underscores that contains a DOUBLE underscore
    /// and neither starts nor ends with one. That excludes the flat variable names, which carry single
    /// underscores only, and captures a documented section path however deeply nested.
    /// </remarks>
    private static IReadOnlyCollection<string> HarvestDocumentedKeyPaths(string text)
    {
        SortedSet<string> tokens = new(StringComparer.Ordinal);

        for (int index = 0; index + 1 < text.Length; index++)
        {
            if (text[index] != '_' || text[index + 1] != '_')
            {
                continue;
            }

            int start = index;

            while (start > 0 && IsTokenCharacter(text[start - 1]))
            {
                start--;
            }

            int end = index + 2;

            while (end < text.Length && IsTokenCharacter(text[end]))
            {
                end++;
            }

            string token = text[start..end];

            if (token.StartsWith('_') || token.EndsWith('_'))
            {
                continue;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    /// <summary>Reports whether a character may appear inside a harvested token.</summary>
    /// <param name="character">The character to test.</param>
    /// <returns><see langword="true"/> when it is a letter, a digit or an underscore.</returns>
    private static bool IsTokenCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character == '_';

    /// <summary>
    /// Removes every all-digit segment from a configuration path, yielding the SHAPE the path
    /// addresses rather than the particular element it addresses.
    /// </summary>
    /// <param name="path">The colon-separated path.</param>
    /// <returns>The path with all index segments removed.</returns>
    /// <remarks>
    /// <para>
    /// The earlier form of this helper removed only a TRAILING index, which resolved an array of scalars
    /// - <c>Security:Audiences:0</c> reduces to the one authored leaf <c>Security:Audiences</c> - but
    /// could not resolve an array of OBJECTS, where the index sits in the middle. Security's issuance
    /// allowlist is exactly that shape, so every one of its documented key paths read as unresolvable
    /// while being perfectly correct.
    /// </para>
    /// <para>
    /// Removing indices at ANY depth is strictly STRONGER than truncating at the array, which is the
    /// other way this could have been fixed. Truncating would have accepted anything at all after the
    /// index - <c>CallerAuthorizations:3:Typo</c> included - because the array's own existence would have
    /// been the whole test. Normalizing both sides instead compares the member name against the element
    /// shape the settings file actually authors, so a misspelled member inside an element is still a
    /// finding. Indices are DROPPED rather than preserved because one element's shape describes every
    /// element's: an environment file may legitimately address an index the settings file never authors,
    /// which is precisely how a row gets appended.
    /// </para>
    /// </remarks>
    private static string NormalizeAwayIndexes(string path) =>
        string.Join(
            ':',
            path.Split(':')
                .Where(static segment =>
                    segment.Length == 0 || !segment.All(char.IsAsciiDigit)));

    /// <summary>
    /// Reads every declared path, descending THROUGH arrays so that the members of an array element
    /// are declared shapes too, with index segments normalized away.
    /// </summary>
    /// <returns>The normalized declared shapes.</returns>
    /// <remarks>
    /// <see cref="Flatten"/> stops at an array because an array is one authored leaf, and the
    /// exact-roster assertions depend on it continuing to do so. This is the additional, wider view used
    /// only for resolving a documented key path, so the two coexist rather than one replacing the other.
    /// </remarks>
    private static HashSet<string> ReadDeclaredShapesThroughArrays()
    {
        HashSet<string> shapes = new(StringComparer.Ordinal);

        foreach (string relativePath in SettingsFileRelativePaths)
        {
            foreach (string path in FlattenThroughArrays(LoadSettings(relativePath), prefix: string.Empty))
            {
                shapes.Add(NormalizeAwayIndexes(path));
            }
        }

        return shapes;
    }

    /// <summary>Enumerates every path beneath a node, descending into arrays as well as objects.</summary>
    /// <param name="node">The node to walk.</param>
    /// <param name="prefix">The path accumulated so far.</param>
    /// <returns>Every path the node contributes.</returns>
    private static IEnumerable<string> FlattenThroughArrays(JsonNode? node, string prefix)
    {
        if (node is JsonObject owner)
        {
            foreach ((string name, JsonNode? value) in owner)
            {
                string path = prefix.Length == 0 ? name : $"{prefix}:{name}";

                // An array is reported as a leaf in its own right - matching Flatten, so that a
                // reference to the array itself still resolves - AND then descended into.
                if (value is not JsonObject)
                {
                    yield return path;
                }

                foreach (string nested in FlattenThroughArrays(value, path))
                {
                    yield return nested;
                }
            }

            yield break;
        }

        if (node is JsonArray elements)
        {
            foreach (JsonNode? element in elements)
            {
                // The index contributes nothing: the prefix is carried through unchanged so that every
                // element describes the same shape.
                foreach (string nested in FlattenThroughArrays(element, prefix))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>Reads the template as lines, with line endings normalized away.</summary>
    /// <returns>The template's lines.</returns>
    private static string[] ReadTemplateLines() =>
        ReadTemplateText().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    /// <summary>
    /// Reads the compiled-in fallback the end-to-end suite uses for one address variable.
    /// </summary>
    /// <param name="variableName">The variable name, as both artifacts spell it.</param>
    /// <returns>
    /// The fallback literal, or <see langword="null"/> when the fixture resolves no such variable.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A TARGETED TEXT MATCH RATHER THAN A PARSE, and the shape it matches is the one the fixture actually
    /// uses: <c>resolveBaseUrl('NAME', 'value')</c>, whether written on one line or across three. Parsing
    /// TypeScript from a .NET test would need a parser this repository does not have and must not acquire
    /// for a two-argument call.
    /// </para>
    /// <para>
    /// A MISSING FIXTURE IS A FAILURE, NOT A SKIP. The suite is committed, so a check that quietly passed
    /// when the file moved would be worth nothing — the one state it exists to catch is the fixture and the
    /// template disagreeing, and a renamed fixture is indistinguishable from that until somebody looks.
    /// </para>
    /// </remarks>
    private static string? ReadEndToEndFallback(string variableName)
    {
        string path = Path.Combine(
            RequireRepositoryRoot(),
            "tests",
            "e2e",
            "fixtures",
            "service-endpoints.ts");

        string text = File.Exists(path)
            ? File.ReadAllText(path)
            : throw FailException.ForFailure(
                "'tests/e2e/fixtures/service-endpoints.ts' does not exist. It carries the addresses the "
                    + "documented end-to-end run uses when no variable is set, so its absence is a finding "
                    + "rather than a reason to skip a check.");

        Match match = Regex.Match(
            text,
            @"resolveBaseUrl\(\s*'" + Regex.Escape(variableName) + @"'\s*,\s*'(?<fallback>[^']+)'",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        return match.Success ? match.Groups["fallback"].Value : null;
    }

    /// <summary>Reads the whole template.</summary>
    /// <returns>The template text.</returns>
    private static string ReadTemplateText()
    {
        string path = Path.Combine(
            RequireRepositoryRoot(),
            OrchestrationDirectoryName,
            TemplateFileName);

        return File.Exists(path)
            ? File.ReadAllText(path)
            : throw FailException.ForFailure(
                $"'{OrchestrationDirectoryName}/{TemplateFileName}' does not exist. It is the committed "
                    + "template the Compose manifest reads, so its absence is a finding rather than a "
                    + "reason to skip a check.");
    }

    /// <summary>Reads every assignment in the template.</summary>
    /// <returns>The variables, in declaration order, later assignment winning as the parser does.</returns>
    private static IReadOnlyDictionary<string, string> ReadTemplateVariables()
    {
        Dictionary<string, string> variables = new(StringComparer.Ordinal);

        foreach (string line in ReadTemplateLines())
        {
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            int separator = line.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0 || !IsVariableName(line[..separator]))
            {
                continue;
            }

            variables[line[..separator]] = line[(separator + 1)..];
        }

        return variables;
    }

    /// <summary>Reads one variable, failing with a named diagnostic when it is absent.</summary>
    /// <param name="variableName">The variable to read.</param>
    /// <returns>Its declared value.</returns>
    private static string RequireVariable(string variableName) =>
        ReadTemplateVariables().TryGetValue(variableName, out string? value)
            ? value
            : throw FailException.ForFailure(
                $"The template declares no '{variableName}'.");

    /// <summary>Reads the union of the leaf paths of all eight authored settings files.</summary>
    /// <returns>Every declared configuration leaf path.</returns>
    private static HashSet<string> ReadAllDeclaredLeafPaths()
    {
        HashSet<string> paths = new(StringComparer.Ordinal);

        foreach (string relativePath in SettingsFileRelativePaths)
        {
            foreach (string path in Flatten(LoadSettings(relativePath)))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>Reads and parses one settings file.</summary>
    /// <param name="relativePath">Path relative to the services directory.</param>
    /// <returns>The parsed root object.</returns>
    private static JsonObject LoadSettings(string relativePath)
    {
        string path = Path.Combine(
            RequireRepositoryRoot(),
            ServicesDirectoryName,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            throw FailException.ForFailure($"'{ServicesDirectoryName}/{relativePath}' does not exist.");
        }

        JsonNode? parsed;

        try
        {
            parsed = JsonNode.Parse(File.ReadAllText(path), documentOptions: SettingsDocumentOptions);
        }
        catch (JsonException failure)
        {
            throw FailException.ForFailure(
                $"'{ServicesDirectoryName}/{relativePath}' is not parseable as the host reads it: "
                    + failure.Message);
        }

        return parsed as JsonObject
            ?? throw FailException.ForFailure(
                $"'{ServicesDirectoryName}/{relativePath}' has no JSON object at its root.");
    }

    /// <summary>Locates the repository root by walking up from the test binary.</summary>
    /// <returns>The absolute repository-root path.</returns>
    /// <remarks>
    /// Both repository markers are required together with the orchestration directory, so the walk
    /// cannot latch onto a same-named directory elsewhere on the machine. The probe is read-only.
    /// </remarks>
    private static string RequireRepositoryRoot()
    {
        // STARTS AT THE EMBEDDED REPOSITORY ROOT WHEN THE BUILD SUPPLIED ONE, so this locator works when
        // the test output sits outside the checkout - `dotnet test --artifacts-path` - where no ancestor
        // of the output directory carries the marker below. The walk itself is unchanged and still
        // verifies that marker, so an absent or stale value simply falls back to the previous start.
        // See TestRepositoryRoot.
        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

        while (candidate is not null)
        {
            if (Directory.Exists(Path.Combine(candidate.FullName, OrchestrationDirectoryName))
                && File.Exists(Path.Combine(candidate.FullName, SolutionMarkerFileName))
                && File.Exists(Path.Combine(candidate.FullName, PackageVersionMarkerFileName)))
            {
                return candidate.FullName;
            }

            // Parent is null at the filesystem root, which is what terminates the walk.
            candidate = candidate.Parent;
        }

        throw FailException.ForFailure(
            $"No directory from '{AppContext.BaseDirectory}' up to the filesystem root holds "
                + $"'{OrchestrationDirectoryName}' together with both repository markers "
                + $"'{SolutionMarkerFileName}' and '{PackageVersionMarkerFileName}'.");
    }

    /// <summary>Resolves a configuration key path against a parsed settings object.</summary>
    /// <param name="settings">The parsed root.</param>
    /// <param name="keyPath">The colon-separated path.</param>
    /// <returns>The node, or <see langword="null"/> when the path is not declared.</returns>
    private static JsonNode? FindNode(JsonObject settings, string keyPath)
    {
        JsonNode? current = settings;

        foreach (string segment in keyPath.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is not JsonObject owner || !owner.TryGetPropertyValue(segment, out JsonNode? next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    /// <summary>Enumerates a settings object as leaf paths, treating an array as a single leaf.</summary>
    /// <param name="owner">The object to flatten.</param>
    /// <param name="prefix">Path prefix, empty for the root.</param>
    /// <returns>One entry per leaf.</returns>
    private static IEnumerable<string> Flatten(JsonObject owner, string prefix = "")
    {
        foreach ((string name, JsonNode? value) in owner)
        {
            string path = prefix.Length == 0 ? name : $"{prefix}:{name}";

            if (value is JsonObject nested)
            {
                foreach (string nestedPath in Flatten(nested, path))
                {
                    yield return nestedPath;
                }

                continue;
            }

            yield return path;
        }
    }

    /// <summary>Renders a failure list as one diagnostic.</summary>
    /// <param name="subject">What the failures are about.</param>
    /// <param name="failures">The collected failures.</param>
    /// <returns>A single message naming every failure.</returns>
    private static string Describe(string subject, IReadOnlyList<string> failures) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"orchestration/.env.example has {failures.Count} {subject}: {string.Join(" | ", failures)}");

    /// <summary>Renders a name list for a diagnostic.</summary>
    /// <param name="names">The names to render.</param>
    /// <returns>A readable list, or the word none.</returns>
    private static string Join(IReadOnlyCollection<string> names) =>
        names.Count == 0 ? "none" : string.Join(", ", names);

    /// <summary>One address variable and the listener it must resolve to.</summary>
    /// <param name="VariableName">The variable.</param>
    /// <param name="Port">The port the map assigns its target.</param>
    /// <param name="Scheme">The scheme its target serves.</param>
    private sealed record AddressExpectation(string VariableName, int Port, string Scheme);

    /// <summary>One variable that restates a service's authored default.</summary>
    /// <param name="VariableName">The variable.</param>
    /// <param name="SettingsRelativePath">The settings file holding the default.</param>
    /// <param name="KeyPath">The configuration key path of the default.</param>
    private sealed record PreservedDefault(
        string VariableName,
        string SettingsRelativePath,
        string KeyPath);
}
