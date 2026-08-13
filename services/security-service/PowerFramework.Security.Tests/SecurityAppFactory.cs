// ==================================================================================================
//  SecurityAppFactory.cs - THE IN-PROCESS HOST, AND THE TWO DETERMINISM DOUBLES, FOR THE SECURITY
//  SERVICE'S SERVICE-LEVEL TESTS
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  Security is the only service in this decomposition with an HTTP surface worth exercising from
//  inside the process that serves it: token issuance, the published key set, the OpenID discovery
//  metadata, the eighteen cryptographic operations, the anonymous readiness probe and the
//  authenticated ping route. This file boots that surface through the service's OWN composition root
//  and hands a sibling test class three things it cannot obtain any other way:
//
//      1. a host that STARTS, which requires supplying the one input a deployment supplies - the
//         asymmetric signing material - because the composition root refuses to start without it;
//      2. the two substituted determinism seams the characterization model requires, namely a clock
//         and an entropy source, neither of which may come from a package;
//      3. a bearer token the service ITSELF minted, so an authenticated assertion is made against
//         the configuration the host actually loaded rather than against a test's beliefs about it.
//
//  IT CONTAINS NO TEST. Not one test-method attribute of any kind appears below - no fact, no theory,
//  no data row - and none may be added: this is the shared infrastructure the test classes consume,
//  and folding an assertion into it would make the infrastructure's own failure indistinguishable
//  from the failure of whatever behaviour a sibling was actually asserting. A search of this file for
//  a test attribute is expected to return nothing at all, which is a property worth keeping greppable.
//
//  WHY THE DOUBLES ARE HAND AUTHORED RATHER THAN REFERENCED
//  Microsoft.Extensions.TimeProvider.Testing is deliberately absent from the repository root
//  Directory.Packages.props, and central package management makes a locally-versioned reference a
//  hard restore failure rather than a quiet drift. That is not an oversight to work around: this
//  project's own file header records that the clock and entropy doubles are hand authored HERE, so
//  the two small types at the foot of this file are the sanctioned implementation and adding a
//  package to replace them would violate the project's declared shape.
//
//  WHAT THE COMPOSITION ROOT DOES THAT THIS FILE MUST NOT UNDO
//  Every one of the following is a property of PowerFramework.Security/Program.cs that a factory is
//  in a position to destroy by accident. Each is stated so a future edit cannot destroy it quietly.
//
//    * DEFAULT DENY WITH EXACTLY THREE ANONYMOUS ROUTES. A fallback authorization policy requires an
//      authenticated user, and only GET /health and the two /.well-known/ publications opt out, each
//      at its own declaration. Nothing below relaxes that: there is no always-succeed authentication
//      handler, no test authentication scheme, no AllowAnonymous fallback and no policy override. A
//      test that must be unauthenticated simply uses CreateClient() and presents nothing, which is
//      what makes an unauthorized assertion mean something.
//
//    * POST /v1/tokens IS NOT ANONYMOUS, AND IT IS NOT BEARER EITHER. Its route-level policy requires
//      a TRANSPORT credential - a client certificate - because a caller cannot present a bearer token
//      in order to obtain its first bearer token. A bearer client from this factory therefore does
//      NOT reach issuance, and that is correct rather than a gap: the issuance suite owns the
//      transport-credential seam, and duplicating it here would put two implementations of one
//      mechanism in one assembly. The token helper below exists for the bearer-authenticated routes.
//
//    * FAIL FAST ON MISCONFIGURATION, NEVER GRACEFUL DEGRADATION. The options contract is bound,
//      annotated, validated by a discrete validator and gated with ValidateOnStart, and the signing
//      chain is resolved eagerly during startup, so a misconfigured host REFUSES TO START instead of
//      reporting healthy and then failing every request. That posture is inherited from the legacy
//      framework application object, which decodes a structural fault out of a seven-field payload
//      split on a carriage-return-newline pair and then ends the process outright
//      [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, ending in HALT CLOSE at :L143]. This factory
//      therefore NEVER invents a value the configuration omitted: setting SigningKeyMaterial to null
//      produces a host that throws on start, on purpose, because that is the behaviour a sibling test
//      needs to be able to observe. Quietly generating a key in that case would be a behavioural
//      change dressed up as robustness.
//
//    * THE TWO SEAMS ARE REGISTERED ONCE, THERE, AND ARE REPLACED HERE RATHER THAN DUPLICATED. The
//      composition root registers the production clock and the production entropy source; this file
//      calls Replace on both descriptors, so the container resolves exactly one of each and no test
//      can be fooled by resolution order.
//
//  ==================================================================================================
//  CONSTRAINT COMPLIANCE - WHAT EACH GOVERNING CONSTRAINT REQUIRES OF THIS FILE SPECIFICALLY
//  ==================================================================================================
//
//  RULES POSITION. The project's rules document contains exactly one line: no user rules were
//  provided. Nothing is invented in their place and their absence is not treated as licence to lower
//  the bar; the enterprise-standard baseline applies instead - warning-clean under warnings as
//  errors, correct nullable annotations, no secret in source, no package added for convenience, and
//  no performance property asserted, because the repository publishes none.
//
//  C-F  NOTHING HARDCODED, AND THE NAMED SECRET SITES ARE A FLOOR RATHER THAN A CEILING. This is the
//       file most exposed to that constraint, because it is the one that holds signing material.
//       EVERY BYTE OF KEY MATERIAL BELOW IS GENERATED AT RUN TIME - RSA.Create for an asymmetric key
//       and RandomNumberGenerator for anything else - or is read from an in-memory configuration
//       collection that the calling test populated. There is no key literal, no armoured block, no
//       base64 blob, no passphrase, no initialization vector and no token anywhere in this file, and
//       none of the eight in-source sites inventoried in docs/SECRETS.md is reproduced in any form:
//       not the armoured private key at tests/blink/test_jws.htm:L8-L22, not the two base64 private
//       keys at ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L466 and :L718, and not
//       the symmetric configuration key at ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru:L22-L23.
//       Those are LOCATORS. Their contents are not here and must never be pasted here. Nothing below
//       logs, prints, asserts on or writes resolved key material to test output either; the material
//       this factory generates is returned to its caller and to the host and goes nowhere else.
//
//  C-C  THE LEGACY TREE IS READ ONLY AND IS THE BEHAVIOURAL ORACLE. Every legacy path in this file
//       appears in a comment and only in a comment. NOTHING below opens, reads, copies or
//       fixture-loads any path under ws_objects/ or under the pre-existing browser harness
//       directories in tests/, at construction time or at request time.
//       tests/blink/test_jws.htm is cited for one reason: it is the ANTI-PATTERN THIS FILE REPLACES.
//       That page carries a plaintext armoured RSA private key inline and signs a web token with it,
//       so the key that verifies its output is the key that produced it and both are in version
//       control forever. This factory is the inversion of that page - the key exists for the lifetime
//       of one test host, is generated in memory, is never written anywhere, and is never the same
//       twice. The page is not emulated, not read, and not quoted.
//
//  C-G  NO NEW ATTACK SURFACE: EVERY NEW BOUNDARY AUTHENTICATED. The token helper exists so that a
//       test can prove authentication WORKS, never so that a test can go around it. Consequently
//       there is no mechanism below to disable authentication, no stub authentication handler that
//       always succeeds, no anonymous fallback policy and no way to obtain a principal without a
//       token the real issuer signed with the real key. Any of those would make a sibling's
//       unauthorized assertion vacuously true while looking like it passed.
//
//  C-A  NO CROSS-SERVICE COUPLING. The only application types referenced are PowerFramework.Security
//       and what it exposes transitively. No other service, no other service's test project, and no
//       client of any kind: Security is called BY the other three and calls none of them, so a
//       factory that reached for another service would be modelling traffic that does not exist.
//
//  C-D  NOTHING FOR ANY DEFERRED SERVICE. No signing, verification or mutual-TLS setting is
//       configured for DesignSystem, Documents, Integration or ScriptBridge, no route is registered
//       for one, and no placeholder reserves a slot for one.
//
//  C-I / C-L  INDEPENDENT BUILD, AND THE ENVIRONMENT'S DOCUMENTED OPERATIONAL CONTRACT. This file
//       compiles and runs under the verbatim per-service workflow - restore, then build in the
//       release configuration, then test collecting cross-platform coverage - from this service's
//       directory with no sibling service, no shared solution and no orchestration present. THE HOST
//       IS BOUND TO THE IN-MEMORY TRANSPORT AND TO NOTHING ELSE: there is no UseUrls call below, no
//       listening address and no port number, so the service's real port cannot be contended for and
//       a busy continuous-integration agent cannot fail a run for a reason that has nothing to do
//       with the code under test. The application's own TLS listener configuration is inert here,
//       which is why nothing below has to disable it.
//
//  .editorconfig  NO PRESERVED-SPELLING IDENTIFIER IS DECLARED HERE. The repository's naming
//       suppressions are file-glob scoped to the production files that genuinely carry the legacy
//       screaming-snake constants, and the only one in this service is Crypto/LegacyDefaults.cs. A
//       test file is NOT in that scope, so with warnings as errors a preserved-spelling declaration
//       here would be a build error rather than a style nit. Where such a constant is meant, it is
//       CONSUMED from PowerFramework.Shared.Kernel and named in a documentation reference; it is
//       never re-declared.
// ==================================================================================================

using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PowerFramework.Security.Authorization;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Crypto;
using PowerFramework.Security.Endpoints;
using PowerFramework.Security.Tokens;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Boots the Security service's own composition root in process, supplies the one input a deployment
/// supplies, substitutes the two determinism seams, and mints bearer tokens through the service's real
/// issuer.
/// </summary>
/// <remarks>
/// <para>
/// THE COMPOSITION ROOT IS NEVER REPLACED, ONLY SUPPLIED AND SEAMED. Every registration the service
/// makes survives: the options contract and its startup gate, the cryptographic graph, the reference
/// resolver, the signing-key layer, the sole minter, inbound token validation, the default-deny
/// authorization posture, the single published error shape and all five route groups are the
/// production ones. This factory adds signing material, replaces two descriptors, and does nothing
/// else. A factory that rebuilt the pipeline would be testing a different application.
/// </para>
/// <para>
/// EVERY OTHER SETTING DEFAULTS TO INHERITANCE RATHER THAN TO A COPY. The application's own
/// <c>appsettings.json</c> already declares the issuer, the audience roster, the lifetime, the key
/// identifier, the algorithm, the three published paths and the key-store descriptor, so the ONLY
/// value this factory must supply is the signing material - which is exactly the division a
/// deployment observes. Each of those settings is exposed as a nullable override that is emitted only
/// when a test sets it, so a test varies ONE value without restating the other nine, and no
/// deployment fact is duplicated here as a second source of truth able to drift from the settings
/// file it was copied from.
/// </para>
/// <para>
/// TYPICAL USE. Construct, optionally set one override, then let the base class create clients:
/// </para>
/// <code>
/// using SecurityAppFactory factory = new();
/// using HttpClient anonymous = factory.CreateClient();          // presents nothing: expect 401
/// using HttpClient caller = factory.CreateAuthenticatedClient(); // presents a real minted token
/// </code>
/// <para>
/// AND TO OBSERVE THE FAIL-FAST PATH, which is a first-class use rather than an edge case:
/// </para>
/// <code>
/// using SecurityAppFactory factory = new() { SigningKeyMaterial = null };
/// // Host start runs both validation passes, so the refusal surfaces here rather than on a request.
/// Assert.ThrowsAny&lt;Exception&gt;(factory.CreateClient);
/// </code>
/// <para>
/// LIFETIME AND THREADING. The host is created lazily on first access to
/// <see cref="WebApplicationFactory{TEntryPoint}.Services"/> or on the first client, so every override
/// must be set BEFORE either. After that point an override is
/// inert, because the configuration has already been composed; the two seams remain mutable on
/// purpose, since advancing the clock or resetting the entropy counter mid-test is the whole reason
/// they are exposed. Disposing the factory disposes the host and every client the base class handed
/// out.
/// </para>
/// </remarks>
internal sealed class SecurityAppFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The modulus size of the asymmetric key this factory generates for a host, in bits: 2048.
    /// </summary>
    /// <remarks>
    /// Chosen because it is the smallest size the minting library will sign an <c>RS256</c> token
    /// with, so a host built on it exercises the ordinary path rather than a boundary. It is also the
    /// size at and above which the service stops remarking on the modulus -
    /// <see cref="SecurityOptions.LegacyWeakSigningKeySizeBits"/> - so a host this factory builds starts
    /// with no weak-key warning in its log, and a case that WANTS that warning has to ask for a shorter
    /// key deliberately. NO SIZE IS REFUSED ANYWHERE, AND NOTHING HERE NEEDS LOWERING TO USE A SHORT
    /// KEY: material requested through <see cref="CreateSigningKeyMaterial(int)"/> at 1024 bits starts a
    /// host just as well, annotated rather than rejected, because AAP 0.6.6.4 keeps that size legal
    /// across this estate [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L965</c>]. This constant is the one
    /// place a 2048-bit size is still named as a VALUE, and it belongs here because it is a test's choice
    /// of an unremarkable size rather than a policy of the service.
    /// </remarks>
    internal const int DefaultSigningKeySizeInBits = 2048;

    /// <summary>
    /// The configuration-key NAME PREFIX this factory resolves key references against when a test
    /// adds one, and the same spelling the application's settings file declares.
    /// </summary>
    /// <remarks>
    /// A NAME AND NOT A VALUE, which is the whole reason it may appear as a literal at all. It has to
    /// be known here rather than inherited, because <see cref="AddKeyStoreEntry(string)"/> composes
    /// the flat configuration key that carries a reference's material by concatenating this prefix
    /// onto the reference - and a prefix that was only discoverable after the configuration had been
    /// built could not be concatenated onto anything at the point the collection is assembled.
    /// </remarks>
    internal const string DefaultKeyStoreConfigurationKeyPrefix = "SECURITY_KEYSTORE_";

    /// <summary>
    /// The number of random bytes generated for a key-store entry's material: 32, which is a 256-bit
    /// key.
    /// </summary>
    /// <remarks>
    /// The material is a keyed-hash or symmetric key from the service's point of view, so any length
    /// is legal and none is validated. Thirty-two bytes is chosen because it is the largest key the
    /// preserved symmetric catalogue admits, so one length serves every operation a test might drive.
    /// </remarks>
    private const int KeyStoreMaterialSizeInBytes = 32;

    /// <summary>The configuration section the inbound token-validation settings are read from.</summary>
    /// <remarks>
    /// Spelled here because the composition root's own spelling of it is a private local constant and
    /// therefore unreachable. It is a configuration KEY NAME rather than a value, and the two must
    /// agree: this is the section whose <c>Audience</c> member decides which single audience the
    /// inbound handler accepts, and whose absence from the issuance roster the host refuses to start
    /// on.
    /// </remarks>
    private const string InboundAuthenticationSectionName = "Authentication:Jwt";

    /// <summary>The flat configuration key carrying this service's own inbound audience identity.</summary>
    private const string InboundAudienceConfigurationKey =
        InboundAuthenticationSectionName + ":Audience";

    /// <summary>
    /// The SECTION-SCOPED spelling of the signing-material key, composed from the options type's own
    /// constants so that a rename cannot leave this file behind.
    /// </summary>
    /// <remarks>
    /// Supplied ALONGSIDE the flat spelling rather than instead of it, because the two reach the bound
    /// instance by different routes and a test should not have to know which one the service used. The
    /// flat key is resolved by an explicit post-configure step in the composition root - the
    /// environment provider folds only a doubled underscore into a section separator, so the fixed
    /// variable name can never bind into the section by convention - while this spelling is picked up
    /// by ordinary section binding. Both carry the same value, so there is nothing for them to
    /// disagree about.
    /// </remarks>
    private const string SectionScopedSigningKeyConfigurationKey =
        SecurityOptions.SectionName + ":" + nameof(SecurityOptions.SigningKey);

    /// <summary>The subject claim of a token minted through the convenience overloads.</summary>
    /// <remarks>
    /// <para>
    /// A ROSTERED PRODUCTION IDENTITY RATHER THAN A TEST-SHAPED ONE, AND THAT IS NOW A REQUIREMENT
    /// rather than a preference. The issuer consults the deployment's issuance roster on every request:
    /// a subject with no entry is refused, and one whose entry does not grant the requested audience or
    /// scopes is refused too. A self-describing invented identity would therefore mint nothing at all,
    /// so the convenience overloads mint as the caller the settings file actually registers for this
    /// service - DataServices, which is the one caller in the system that holds a token addressed to
    /// Security [services/dataservices-service/PowerFramework.DataServices/Clients/SecurityClient.cs].
    /// </para>
    /// <para>
    /// The upside is that the convenience path now exercises the real production flow end to end rather
    /// than a shape no deployment produces, which is what makes a passing suite evidence about the
    /// deployment.
    /// </para>
    /// </remarks>
    internal const string DefaultTokenSubject = "powerframework-security-tests";

    /// <summary>
    /// The scopes requested by the convenience overloads: exactly the two this service's protected
    /// routes require.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NO LONGER AN ARBITRARY TOKEN, BECAUSE THE ROUTES NOW READ IT. The cryptographic contract's 18
    /// operations are gated by a named policy requiring
    /// <see cref="CryptoEndpoints.RequiredScope"/> and the authenticated probe by one requiring
    /// <see cref="PingEndpoints.RequiredScope"/>, so a token carrying neither is authenticated and then
    /// forbidden. The two names are read from the routes' own declarations rather than spelled here, so
    /// a rename is a compile-time change instead of a suite that fails with a 403 nobody can place.
    /// </para>
    /// <para>
    /// BOTH SCOPES, NOT ONE, so a single convenience client can reach every protected route on the
    /// service. Both are granted to the default subject by the settings file's roster entry for it,
    /// which is what makes the request succeed - a test wanting a refusal asks for something the roster
    /// does not grant, which no longer requires any special configuration.
    /// </para>
    /// </remarks>
    internal const string DefaultTokenScope = "security.test";

    /// <summary>The authorization scheme name a minted token is presented under.</summary>
    private const string BearerSchemeName = "Bearer";

    /// <summary>
    /// The scopes requested by the convenience overloads: exactly the two this service's protected
    /// routes require.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NO LONGER AN ARBITRARY TOKEN, BECAUSE THE ROUTES NOW READ IT. The cryptographic contract's 18
    /// operations are gated by a named policy requiring
    /// <see cref="CryptoEndpoints.RequiredScope"/> and the authenticated probe by one requiring
    /// <see cref="PingEndpoints.RequiredScope"/>, so a token carrying neither is authenticated and then
    /// forbidden. The two names are read from the routes' own declarations rather than spelled here, so
    /// a rename is a compile-time change instead of a suite that fails with a 403 nobody can place.
    /// </para>
    /// <para>
    /// BOTH SCOPES, NOT ONE, so a single convenience client can reach every protected route on the
    /// service. Both are granted to the default subject by the settings file's roster entry for it,
    /// which is what makes the request succeed - a test wanting a refusal asks for something the roster
    /// does not grant, which no longer requires any special configuration.
    /// </para>
    /// </remarks>
    private static readonly string[] DefaultTokenScopes =
        [CryptoEndpoints.RequiredScope, PingEndpoints.RequiredScope];

    /// <summary>
    /// The roster secret this factory supplies for every issuance-roster entry that names a key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A CREDENTIAL GENERATED PER PROCESS, NEVER A LITERAL. The issuance roster in the settings files
    /// names a configuration key per registered caller and the registry REFUSES TO START when a named
    /// key resolves to nothing - which is the posture that turns a missing deployment secret into a
    /// startup failure instead of a caller that mysteriously cannot authenticate. A test host therefore
    /// has to supply one, and it supplies a freshly generated value rather than a committed string so
    /// that nothing in this repository is a usable credential (constraint C-F).
    /// </para>
    /// <para>
    /// ONE VALUE FOR EVERY ENTRY, because a test that needs two callers to have DIFFERENT secrets is
    /// asserting the comparison rather than the wiring, and it can add its own configuration entry for
    /// that. Sharing one value here keeps the collection small and the intent obvious.
    /// </para>
    /// <para>
    /// STATIC AND READ ONCE PER TEST PROCESS. It is not a deployment fact and never leaves the process:
    /// it exists so a host can start and so a row driving the Basic credential scheme has something to
    /// present.
    /// </para>
    /// </remarks>
    internal static readonly string RosterSecret =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// The configuration keys the shipped issuance roster names its callers' secrets under.
    /// </summary>
    /// <remarks>
    /// NAMES ONLY, AND THEY MIRROR THE SETTINGS FILES RATHER THAN INVENTING ANYTHING. Every key a
    /// roster entry can name has to resolve for the host to start, so this factory emits
    /// <see cref="RosterSecret"/> under each of them. Adding a roster entry to either settings file
    /// means adding its key here in the same change - the failure if that is forgotten is a loud
    /// startup refusal naming the roster position, which is the intended way to find out.
    /// </remarks>
    private static readonly string[] RosterSecretConfigurationKeys =
    [
        "SECURITY_CLIENT_SECRET_GATEWAY",
        "SECURITY_CLIENT_SECRET_DATASERVICES",
        "SECURITY_CLIENT_SECRET",
    ];

    /// <summary>
    /// The configuration entries that satisfy every secret the shipped issuance roster names.
    /// </summary>
    /// <returns>One entry per named key, each carrying <see cref="RosterSecret"/>.</returns>
    /// <remarks>
    /// <para>
    /// EXPOSED SO EVERY HOST IN THIS ASSEMBLY CAN REUSE IT rather than re-deriving the same list. Five
    /// independent <see cref="WebApplicationFactory{TEntryPoint}"/> subclasses boot this service's
    /// composition root in this project - this factory, the issuance host in
    /// <c>TokenEndpointsTests.cs</c>, the readiness host in <c>HealthEndpointsTests.cs</c>, the trust
    /// anchor host in <c>ClientCertificateAnchorAdoptionTests.cs</c> and the reporting host in
    /// <c>IssuanceRosterAuthorityTests.cs</c> - and EVERY ONE of them must supply these keys or it does
    /// not start, so one declaration of them is the only shape in which the five cannot drift.
    /// </para>
    /// <para>
    /// EACH HOST MERGES THIS INTO ITS OWN IN-MEMORY CONFIGURATION, AND THAT IS THE CONTRACT. An earlier
    /// form of this file satisfied all five at once from a <c>[ModuleInitializer]</c> that wrote the three
    /// keys into the PROCESS ENVIRONMENT and never restored them. It worked, and it was wrong in two ways
    /// that matter for a test suite. It mutated state shared by every test class in the assembly, so a
    /// host that believed it was reading its own configuration was in fact reading a value some other
    /// class's initializer had installed, and a row asserting a REFUSAL for a missing secret could not
    /// state that the secret was missing. And it silently adopted, or silently displaced, whatever the
    /// host machine already had under those names, which makes a local run and a continuous-integration
    /// run two different experiments. In-memory configuration is the same ingress a deployment uses - the
    /// options pipeline - so nothing about the production resolution path is bypassed by supplying it per
    /// host; the only thing that changes is that the value cannot escape the host that asked for it.
    /// </para>
    /// <para>
    /// THE VALUES ARE GENERATED PER PROCESS AND NOTHING HERE IS A COMMITTED CREDENTIAL (constraint C-F).
    /// </para>
    /// </remarks>
    internal static Dictionary<string, string?> RosterSecretOverrides()
    {
        Dictionary<string, string?> secrets = new(StringComparer.Ordinal);

        foreach (string key in RosterSecretConfigurationKeys)
        {
            secrets[key] = RosterSecret;
        }

        return secrets;
    }

    /// <summary>
    /// The material behind each key reference a test added, by reference. Generated, never configured.
    /// </summary>
    private readonly Dictionary<string, string> _keyStoreMaterial = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityAppFactory"/> class, generating the
    /// asymmetric signing material this host will run on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A DISTINCT KEY PAIR PER FACTORY, GENERATED HERE, AND THAT IS NOT MERELY TIDINESS. The minting
    /// library caches signature providers in a process-wide cache keyed by the key's own material
    /// rather than by the object holding it, so two hosts configured from one pair share a cached
    /// provider whose lifetime outlives whichever host is disposed first. Sharing a pair therefore
    /// makes a test fail intermittently for a reason unrelated to what it asserts. A fresh pair
    /// removes the sharing entirely.
    /// </para>
    /// <para>
    /// THE KEY OBJECT IS DISPOSED BEFORE THIS CONSTRUCTOR RETURNS. The material is exported to the
    /// text encoding the service accepts and the platform key is released by the declaration's own
    /// scope, so this factory holds no cryptographic handle for its lifetime and there is nothing for
    /// a disposal path to have to remember. That is the strongest available form of the requirement to
    /// dispose what was generated, rather than the weakest form of storing a handle and hoping.
    /// </para>
    /// </remarks>
    public SecurityAppFactory()
    {
        SigningKeyMaterial = CreateSigningKeyMaterial(DefaultSigningKeySizeInBits);
    }

    /// <summary>
    /// The substituted clock this host reads every instant from. Mutable for the whole lifetime of the
    /// factory, including after the host has started.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It replaces the production registration rather than joining it, so the sole minter, and anything
    /// else in the service that reads time, resolve exactly this instance. Two issuances made without
    /// touching it therefore carry byte-identical issued-at, not-before and expiry claims, which is the
    /// property a determinism assertion needs and which no ambient clock can provide.
    /// </para>
    /// <para>
    /// ITS DEFAULT INSTANT IS THE REAL CURRENT INSTANT, AND THAT DEFAULT IS LOAD BEARING. The inbound
    /// bearer handler's clock-skew allowance is configured to zero rather than to the framework's five
    /// minutes, so a token whose validity window sits away from real time is rejected by the very host
    /// that minted it - which would make the authenticated helpers below useless. Starting from now
    /// keeps a minted token acceptable while still being FIXED, so determinism and round-tripping hold
    /// at the same time. A test that wants a literal instant, and does not need the token to survive
    /// inbound validation, pins one with <see cref="DeterministicTimeProvider.SetUtcNow"/>.
    /// </para>
    /// </remarks>
    internal DeterministicTimeProvider Clock { get; } = new();

    /// <summary>
    /// The substituted entropy source every random byte in this host is drawn from. Mutable for the
    /// whole lifetime of the factory.
    /// </summary>
    /// <remarks>
    /// It replaces the production cryptographically-secure source, so the random blob, random string
    /// and identifier surfaces become exact functions of a known byte sequence and a golden-master
    /// comparison can mask them on both sides. See <see cref="DeterministicEntropySource"/> for the
    /// sequence itself, which is specified rather than incidental.
    /// </remarks>
    internal DeterministicEntropySource Entropy { get; } = new();

    /// <summary>
    /// The asymmetric signing material this host runs on, or <see langword="null"/> to supply none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generated in the constructor, so the happy path needs no attention at all. A test overrides it
    /// to drive the composition root's startup gate, and the three shapes worth driving are:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see langword="null"/> - NO USABLE MATERIAL. The empty string is emitted for BOTH configuration
    /// spellings, which does two jobs at once: it lands on the validator's single absent-or-blank
    /// rejection, and it MASKS any signing key the surrounding process environment happens to carry, so
    /// the outcome is the same on a developer machine and on a continuous-integration agent. Host start
    /// throws.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="CreateUnusableSigningMaterial"/> - PRESENT BUT NOT A KEY. Random bytes are the case
    /// that actually happens in a deployment, because they look like a plausible way to produce a
    /// signing value, and the validator reports them with its own fixed message. Host start throws.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="CreateSigningKeyMaterial(int)"/> with a smaller modulus - A SHORT BUT ENTIRELY VALID
    /// KEY. The validator applies no size rule at all, which preserves the legacy catalogue's allowance
    /// of 1024-bit RSA as a legal value rather than silently correcting it, and that was MEASURED on the
    /// pinned toolchain rather than assumed: a 1024-bit key starts the host AND mints. Any size refusal
    /// therefore belongs to the library that signs, not to configuration validation, and a test asserting
    /// one is asserting that library's behaviour.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// It is a value and not a path, and it never reaches a log: nothing in this file writes it to test
    /// output, and the service's own diagnostics name configuration KEYS and never echo what was
    /// configured under them.
    /// </para>
    /// </remarks>
    internal string? SigningKeyMaterial { get; set; }

    /// <summary>
    /// Overrides the issuer identity, or <see langword="null"/> to inherit the application's own.
    /// </summary>
    /// <remarks>
    /// The empty string is a MEANINGFUL value distinct from <see langword="null"/>: it is emitted, binds
    /// as blank, and drives the validator's blank-issuer rejection. That distinction holds for every
    /// nullable override on this type.
    /// </remarks>
    internal string? Issuer { get; set; }

    /// <summary>
    /// Replaces the audience roster entirely when it holds at least one entry; an empty collection
    /// inherits the application's own roster.
    /// </summary>
    /// <remarks>
    /// <para>
    /// APPLIED THROUGH THE OPTIONS PIPELINE RATHER THAN THROUGH CONFIGURATION, AND THAT IS A CORRECTNESS
    /// REQUIREMENT RATHER THAN A PREFERENCE. Configuration merges collections BY INDEX, and the bound
    /// property is a get-only list the binder appends into, so a two-entry override laid over a
    /// four-entry settings roster yields a four-entry list whose last two members the test never asked
    /// for - and, because the validator rejects a case-insensitive duplicate, an override that happened
    /// to repeat one of those tail entries would fail startup for a reason with no visible cause. There
    /// is no configuration key that removes an index. Clearing and re-adding the list after binding is
    /// therefore the only total override available, and it is what happens here.
    /// </para>
    /// <para>
    /// KEEP IT COHERENT WITH <see cref="InboundAudience"/>. The composition root refuses to start when
    /// the declared inbound audience is absent from the roster, because the authenticated ping route
    /// could then never be reached by any token this issuer is able to mint. That refusal is a
    /// legitimate thing for a test to assert; it is not something this factory silently repairs by
    /// adding an entry nobody asked for.
    /// </para>
    /// <para>
    /// To drive the EMPTY-roster rejection instead, clear the bound list through
    /// <see cref="ShapeOptions"/>: an empty collection here means inherit, so it cannot express emptiness.
    /// </para>
    /// </remarks>
    internal List<string> Audiences { get; } = [];

    /// <summary>
    /// Overrides this service's own inbound audience identity, or <see langword="null"/> to inherit the
    /// application's own.
    /// </summary>
    /// <remarks>
    /// This is the single audience the inbound bearer handler accepts, which is deliberately narrower
    /// than the issuance roster: a token addressed to Gateway must not be replayable at Security. When a
    /// deployment declares none the handler falls back to the whole roster, so the empty string is a
    /// meaningful override that exercises exactly that fallback.
    /// </remarks>
    internal string? InboundAudience { get; set; }

    /// <summary>
    /// Overrides the minted-token lifetime, or <see langword="null"/> to inherit the application's own.
    /// </summary>
    /// <remarks>
    /// Emitted in the invariant constant format the configuration binder parses, so the value a test
    /// writes is the value the host binds whatever culture the machine runs under. A zero or negative
    /// duration is a legitimate override: it drives the validator's rejection of a lifetime that would
    /// mint tokens already expired at the instant they were issued.
    /// </remarks>
    internal TimeSpan? TokenLifetime { get; set; }

    /// <summary>
    /// Overrides the published key identifier - the <c>kid</c> - or <see langword="null"/> to inherit the
    /// application's own.
    /// </summary>
    internal string? SigningKeyId { get; set; }

    /// <summary>
    /// Overrides the signature algorithm, or <see langword="null"/> to inherit the application's own.
    /// </summary>
    /// <remarks>
    /// The accepted set is closed and compiled into the validator, and comparison is ordinal because
    /// signature-algorithm identifiers are case-sensitive, so a lowercase spelling of a permitted value
    /// is a rejection rather than a match. That makes this override useful for driving the rejection as
    /// well as for selecting a stronger digest.
    /// </remarks>
    internal string? SigningAlgorithm { get; set; }

    /// <summary>
    /// Overrides the address the key set is published at, or <see langword="null"/> to inherit the
    /// application's own.
    /// </summary>
    /// <remarks>
    /// Constrained by the service to the well-known namespace, because that namespace is what its three
    /// anonymous exemptions are scoped to; an address outside it fails startup rather than quietly
    /// moving an anonymous document behind authentication.
    /// </remarks>
    internal string? JwksPath { get; set; }

    /// <summary>
    /// Overrides the address the discovery document is published at, or <see langword="null"/> to inherit
    /// the application's own.
    /// </summary>
    internal string? OpenIdConfigurationPath { get; set; }

    /// <summary>
    /// Overrides the address token issuance is served at, or <see langword="null"/> to inherit the
    /// application's own.
    /// </summary>
    /// <remarks>
    /// Constrained as the INVERSE of the two metadata addresses: it must be rooted and must not fall
    /// inside the well-known namespace, so the one route that reaches a signing key can never land
    /// inside the namespace the composition root exempts from authorization.
    /// </remarks>
    internal string? TokenEndpointPath { get; set; }

    /// <summary>
    /// Overrides the key-store configuration-key prefix, or <see langword="null"/> to inherit the
    /// application's own.
    /// </summary>
    /// <remarks>
    /// Set automatically by <see cref="AddKeyStoreEntry(string)"/> when it is still
    /// <see langword="null"/>, because the material's flat configuration key is this prefix concatenated
    /// onto the reference and the two halves have to be composed from one known value.
    /// </remarks>
    internal string? KeyStoreConfigurationKeyPrefix { get; set; }

    /// <summary>
    /// Replaces the permitted key-reference set entirely when it holds at least one entry; an empty
    /// collection inherits the application's own set, which ships empty and therefore authorises nothing.
    /// </summary>
    /// <remarks>
    /// Applied through the options pipeline for the same index-merge reason as
    /// <see cref="Audiences"/>. Adding a reference here declares that a caller MAY name it; it does not
    /// make it resolvable. Use <see cref="AddKeyStoreEntry(string)"/> to do both at once.
    /// </remarks>
    internal List<string> PermittedKeyRefs { get; } = [];

    /// <summary>
    /// Arbitrary additional configuration entries, applied LAST so that they win over every typed
    /// override above.
    /// </summary>
    /// <remarks>
    /// The escape hatch for a setting this type does not model - an inbound validation switch, a
    /// clock-skew allowance, a logging filter - so that reaching one never requires either a new property
    /// here or a second factory. Keys are configuration paths using the colon separator, exactly as they
    /// would be written in a settings file.
    /// </remarks>
    internal Dictionary<string, string?> Settings { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// A callback invoked against the bound options instance after every other override has been applied,
    /// or <see langword="null"/> for none.
    /// </summary>
    /// <remarks>
    /// The last word on the options contract, and the way to express anything configuration cannot: an
    /// empty audience roster, a collection mutated rather than replaced, or a value whose textual form
    /// the binder would reject. It runs BEFORE validation, so whatever it produces is what the startup
    /// gate judges.
    /// </remarks>
    internal Action<SecurityOptions>? ShapeOptions { get; set; }

    /// <summary>
    /// Generates an asymmetric private key and returns it in the armoured text encoding the service
    /// accepts.
    /// </summary>
    /// <param name="keySizeInBits">The modulus size in bits.</param>
    /// <returns>The generated private key, armoured.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="keySizeInBits"/> is not positive.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The platform refuses the requested size.
    /// </exception>
    /// <remarks>
    /// <para>
    /// GENERATED, NEVER STORED. There is no key literal in this file and none may be added; this method
    /// and <see cref="CreateUnusableSigningMaterial"/> are the only two sources of signing material the
    /// factory has, and both produce a fresh value on every call. The armoured encoding is chosen because
    /// it is the shape the service attempts FIRST, so the happy path exercises the ordinary branch of the
    /// import rather than its fallback.
    /// </para>
    /// <para>
    /// The platform key is released by the declaration's own scope before the method returns, so no
    /// cryptographic handle escapes and there is nothing for a caller to remember to dispose.
    /// </para>
    /// </remarks>
    internal static string CreateSigningKeyMaterial(int keySizeInBits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keySizeInBits);

        using RSA key = RSA.Create(keySizeInBits);

        return key.ExportPkcs8PrivateKeyPem();
    }

    /// <summary>
    /// Generates material that is present, well-formed as text, and NOT an asymmetric key.
    /// </summary>
    /// <returns>Base64 of freshly generated random bytes.</returns>
    /// <remarks>
    /// This is the shape a deployment actually gets wrong: random bytes look like a plausible way to
    /// produce a signing value, decode cleanly as base64, and then cannot be imported as a private key in
    /// either bare structure the service attempts. Supplying it drives the validator's own fixed
    /// unusable-material message, which describes the accepted encodings and says nothing whatsoever
    /// about what was supplied. The bytes come from the platform's cryptographic generator, so this method
    /// introduces no literal and no reproducible value.
    /// </remarks>
    internal static string CreateUnusableSigningMaterial() => CreateRandomMaterial();

    /// <summary>
    /// Generates fresh random material and renders it as base64 text.
    /// </summary>
    /// <returns>Base64 of <see cref="KeyStoreMaterialSizeInBytes"/> cryptographically strong bytes.</returns>
    /// <remarks>
    /// The single generation primitive behind both the unusable-signing-material helper and a key-store
    /// entry's material, so there is exactly one place in this file where opaque secret-shaped text is
    /// produced and exactly one guarantee to check: it comes from the platform's cryptographic generator
    /// and is therefore never the same twice and never a literal.
    /// </remarks>
    private static string CreateRandomMaterial() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyStoreMaterialSizeInBytes));

    /// <summary>
    /// Declares a key reference as permitted AND supplies freshly generated material for it, so that a
    /// keyed operation naming that reference resolves.
    /// </summary>
    /// <param name="keyRef">The opaque reference a caller will name.</param>
    /// <returns>
    /// The generated material, so that a test can compute the expected result of a keyed operation
    /// without the service having to disclose it.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="keyRef"/> is <see langword="null"/>, empty or
    /// whitespace.</exception>
    /// <remarks>
    /// <para>
    /// BOTH HALVES OR NEITHER. The permitted set is what the service consults BEFORE it reads anything,
    /// and the flat configuration key is where it reads from, so declaring one without the other produces
    /// either a reference that is refused or a value that can never be reached. Doing both here is what
    /// makes the resolution boundary drivable at all.
    /// </para>
    /// <para>
    /// The material is generated by the platform's cryptographic generator and is returned to the caller
    /// only. It is not logged, not written to test output, and not retained past the factory's disposal.
    /// Calling this twice for one reference replaces its material, which keeps the collection consistent
    /// rather than accumulating a second entry the service could never see.
    /// </para>
    /// <para>
    /// Keep the reference inside the conservative character set the service validates the declared set
    /// against - letters, digits, dot, underscore and hyphen, bounded in length - because that check runs
    /// against the DECLARED set at startup, so an unsafe entry here fails the host rather than the
    /// request.
    /// </para>
    /// </remarks>
    internal string AddKeyStoreEntry(string keyRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyRef);

        KeyStoreConfigurationKeyPrefix ??= DefaultKeyStoreConfigurationKeyPrefix;

        if (!PermittedKeyRefs.Contains(keyRef))
        {
            PermittedKeyRefs.Add(keyRef);
        }

        string material = CreateRandomMaterial();

        _keyStoreMaterial[keyRef] = material;

        return material;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// THREE STEPS, IN THIS ORDER, EACH DOING WORK THE OTHER TWO CANNOT.
    /// </para>
    /// <para>
    /// FIRST, THE OVERRIDES ARE APPLIED AS HOST SETTINGS AND AS AN IN-MEMORY CONFIGURATION SOURCE. Both,
    /// deliberately. The in-memory source is added after the application's own sources and therefore wins
    /// on an identical key, which is what makes an override an override; the host settings carry the same
    /// pairs so that anything reading host configuration before the application's sources exist sees them
    /// too. They cannot disagree, because both are projected from the same collection. Supplying settings
    /// this way is also the DEPLOYMENT-REALISTIC ingress: the composition root's own flat-key resolution
    /// step - the one that exists because the environment provider folds only a doubled underscore into a
    /// section separator - is exercised rather than bypassed, which would not be true of a factory that
    /// assigned the bound instance directly.
    /// </para>
    /// <para>
    /// SECOND, THE TWO SEAMS REPLACE THEIR PRODUCTION DESCRIPTORS. Replace rather than add: the
    /// composition root has already registered a clock and an entropy source, and adding a second
    /// registration for either would leave the resolved instance decided by registration order, so a test
    /// could pass or fail on which descriptor the container happened to pick. After this, exactly one of
    /// each exists and it is this factory's.
    /// </para>
    /// <para>
    /// THIRD, A SINGLE POST-CONFIGURE PASS APPLIES WHAT CONFIGURATION CANNOT EXPRESS. It runs after every
    /// registration the composition root made - including its own flat-key resolution step - and before
    /// validation, so it is the last word on the options contract and whatever it produces is what the
    /// startup gate judges.
    /// </para>
    /// <para>
    /// NOTHING HERE TOUCHES THE PIPELINE, THE ROUTES, THE AUTHENTICATION SCHEME OR THE AUTHORIZATION
    /// POLICIES, and nothing binds a listening address: the base class's in-memory transport is the whole
    /// of this host's ingress, so the service's real port is never contended for.
    /// </para>
    /// </remarks>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Dictionary<string, string?> overrides = BuildConfigurationOverrides();

        foreach (KeyValuePair<string, string?> setting in overrides)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }

        builder.ConfigureAppConfiguration(configuration =>
        {
            ArgumentNullException.ThrowIfNull(configuration);

            configuration.AddInMemoryCollection(overrides);
        });

        builder.ConfigureTestServices(services =>
        {
            ArgumentNullException.ThrowIfNull(services);

            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
            services.Replace(ServiceDescriptor.Singleton<IEntropySource>(Entropy));

            services.PostConfigure<SecurityOptions>(ApplyOptionOverrides);
        });
    }

    /// <summary>
    /// Resolves the validated options contract from the booted host.
    /// </summary>
    /// <returns>The bound and validated settings this host is running on.</returns>
    /// <remarks>
    /// Touching this STARTS THE HOST, so both validation passes and the eager resolution of the signing
    /// chain happen inside this call - which is why a deliberately misconfigured factory throws here
    /// rather than on a later request. It is exposed so that a test reads the address of a published
    /// document, or the roster, from the configuration the host actually loaded instead of restating a
    /// deployment fact and hoping the two agree.
    /// </remarks>
    internal SecurityOptions ResolveSecurityOptions() =>
        Services.GetRequiredService<IOptions<SecurityOptions>>().Value;

    /// <summary>
    /// Resolves the audiences this host's grant matrix permits one caller to request, in declaration
    /// order.
    /// </summary>
    /// <param name="subject">The caller identity to read grants for.</param>
    /// <returns>
    /// The permitted audiences, or an empty list when the matrix grants that caller nothing.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="subject"/> is absent.</exception>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THE MATRIX IS THE ONLY PLACE A PERMISSION IS DECLARED, WHICH IS WHY THIS HELPER EXISTS.</b>
    /// Tests used to read <c>Security:Clients[n]:Audiences</c> and <c>:Scopes</c> for "an audience this
    /// caller may address" and "a scope it may hold". Those lists described permissions without deciding
    /// them - every issuance decision is taken against the matrix folded from <c>Security:Callers</c> and
    /// <c>Security:CallerAuthorizations</c> - and they are gone. Reading the matrix is therefore not a
    /// substitution of convenience: it is reading the surface that actually decides, which is what a
    /// setup step needs if the row is to reach the behaviour it exists to assert.
    /// </para>
    /// <para>
    /// BOTH SHAPES ARE FOLDED, in the same order the issuer folds them - nested first, flat added on top -
    /// so a host configured either way is read correctly. Ordinal comparison and declaration order, both
    /// matching the enforcement point, so the first entry is the deterministic choice rather than an
    /// arbitrary one.
    /// </para>
    /// </remarks>
    internal IReadOnlyList<string> ResolveGrantedAudiences(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        SecurityOptions configured = ResolveSecurityOptions();
        List<string> audiences = [];

        void Add(string? audience)
        {
            if (!string.IsNullOrWhiteSpace(audience) && !audiences.Contains(audience.Trim(), StringComparer.Ordinal))
            {
                audiences.Add(audience.Trim());
            }
        }

        foreach (SecurityCallerOptions caller in configured.Callers)
        {
            if (caller is null || !string.Equals(caller.Identity?.Trim(), subject, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (SecurityCallerGrantOptions grant in caller.Grants)
            {
                Add(grant?.Audience);
            }
        }

        foreach (CallerAuthorizationOptions row in configured.CallerAuthorizations)
        {
            if (row is not null && string.Equals(row.Caller?.Trim(), subject, StringComparison.Ordinal))
            {
                Add(row.Audience);
            }
        }

        return audiences;
    }

    /// <summary>
    /// Resolves the scopes this host's grant matrix permits one caller to request for one audience.
    /// </summary>
    /// <param name="subject">The caller identity to read grants for.</param>
    /// <param name="audience">The audience the grant addresses.</param>
    /// <returns>
    /// The permitted scopes, or an empty list when the matrix grants that caller-audience pair nothing.
    /// </returns>
    /// <exception cref="ArgumentException">Either argument is absent.</exception>
    /// <remarks>
    /// The union of both shapes for that pair, because the issuer unions them too: a flat row for a pair
    /// the nested shape also mentions is additive rather than a replacement. See
    /// <see cref="ResolveGrantedAudiences"/> for why the matrix rather than the credential directory is
    /// the surface read.
    /// </remarks>
    internal IReadOnlyList<string> ResolveGrantedScopes(string subject, string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        SecurityOptions configured = ResolveSecurityOptions();
        List<string> scopes = [];

        void AddAll(IList<string> declared)
        {
            foreach (string scope in declared)
            {
                if (!string.IsNullOrWhiteSpace(scope) && !scopes.Contains(scope.Trim(), StringComparer.Ordinal))
                {
                    scopes.Add(scope.Trim());
                }
            }
        }

        foreach (SecurityCallerOptions caller in configured.Callers)
        {
            if (caller is null || !string.Equals(caller.Identity?.Trim(), subject, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (SecurityCallerGrantOptions grant in caller.Grants)
            {
                if (grant is not null && string.Equals(grant.Audience?.Trim(), audience, StringComparison.Ordinal))
                {
                    AddAll(grant.Scopes);
                }
            }
        }

        foreach (CallerAuthorizationOptions row in configured.CallerAuthorizations)
        {
            if (row is not null
                && string.Equals(row.Caller?.Trim(), subject, StringComparison.Ordinal)
                && string.Equals(row.Audience?.Trim(), audience, StringComparison.Ordinal))
            {
                AddAll(row.Scopes);
            }
        }

        return scopes;
    }

    /// <summary>
    /// Resolves the first caller-audience-scope triple this host's grant matrix permits, for a row that
    /// needs "some credential this host will actually mint".
    /// </summary>
    /// <returns>The caller identity, the audience it may address and one scope it may hold.</returns>
    /// <exception cref="InvalidOperationException">
    /// This host's matrix grants nothing, so no mintable triple exists.
    /// </exception>
    /// <remarks>
    /// The FIRST credential-directory subject the matrix grants something, and that grant's first audience
    /// and first scope - deterministic rather than arbitrary. It raises rather than returning an empty
    /// triple because an absent grant is a fault in the test host's own configuration and not a result to
    /// inspect; the message names the keys to correct and echoes no configured value.
    /// </remarks>
    internal (string Subject, string Audience, string Scope) ResolveFirstGrant()
    {
        foreach (SecurityClientOptions client in ResolveSecurityOptions().Clients)
        {
            if (client is null || string.IsNullOrWhiteSpace(client.Subject))
            {
                continue;
            }

            string subject = client.Subject.Trim();

            foreach (string audience in ResolveGrantedAudiences(subject))
            {
                IReadOnlyList<string> scopes = ResolveGrantedScopes(subject, audience);

                if (scopes.Count > 0)
                {
                    return (subject, audience, scopes[0]);
                }
            }
        }

        throw new InvalidOperationException(
            $"This host's grant matrix - '{SecurityOptions.SectionName}:Callers' folded with "
            + $"'{SecurityOptions.SectionName}:{nameof(SecurityOptions.CallerAuthorizations)}' - grants no "
            + $"caller from '{SecurityOptions.SectionName}:Clients' any audience with any scope, so no "
            + "token can be minted for any credential this host declares. Add a grant before the host "
            + "starts. This message echoes no configured value.");
    }

    /// <summary>
    /// Resolves the single audience identity this host accepts on an inbound token.
    /// </summary>
    /// <returns>The accepted audience identity.</returns>
    /// <remarks>
    /// <para>
    /// MIRRORS THE COMPOSITION ROOT'S OWN RESOLUTION RATHER THAN GUESSING IT: the declared inbound
    /// identity when a deployment declares one, trimmed because a settings value that acquired
    /// surrounding whitespace would otherwise be an audience no token could carry, and otherwise the
    /// first entry of the issuance roster, which the startup gate has already proved non-empty. Deriving
    /// it instead of naming it is what keeps this file free of a hardcoded deployment fact, and it means
    /// the helpers below keep working when a test overrides either half.
    /// </para>
    /// <para>
    /// The roster is a wider set than the accepted one when no identity is declared, and any member of it
    /// is then acceptable; the first is chosen because it is the deterministic choice.
    /// </para>
    /// </remarks>
    internal string ResolveInboundAudience()
    {
        string declared =
            (Services.GetRequiredService<IConfiguration>()[InboundAudienceConfigurationKey]
                ?? string.Empty).Trim();

        return declared.Length > 0 ? declared : ResolveSecurityOptions().Audiences[0];
    }

    /// <summary>
    /// Mints a token for the default subject, this host's accepted audience and one scope.
    /// </summary>
    /// <returns>The minted token.</returns>
    /// <exception cref="InvalidOperationException">The host's issuer refused the audience.</exception>
    internal IssuedToken IssueToken() =>
        IssueToken(DefaultTokenSubject, ResolveInboundAudience(), DefaultTokenScopes);

    /// <summary>
    /// Mints a token through the host's OWN issuer.
    /// </summary>
    /// <param name="subject">The identity the token is minted for; becomes the subject claim verbatim.</param>
    /// <param name="audience">The audience the token is addressed to; must be on the issuance roster.</param>
    /// <param name="scopes">
    /// The requested scopes. At least one is required, none may be blank or contain whitespace, and no
    /// two may repeat - the request type enforces all four rules itself.
    /// </param>
    /// <returns>The minted token.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="subject"/> or <paramref name="audience"/> is <see langword="null"/>, empty or
    /// whitespace, or <paramref name="scopes"/> violates one of the request contract's rules.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="scopes"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The host's issuer refused the requested audience because it is not on the configured roster.
    /// </exception>
    /// <remarks>
    /// <para>
    /// MINTED THROUGH THE REAL ISSUER, NEVER ASSEMBLED BY HAND. The service's minter is the only caller of
    /// the token-creation primitive anywhere in this repository, and resolving it from the booted host
    /// means the issuer identity, the audience, the key identifier, the algorithm, the header and every
    /// instant in the token are by construction the ones this host's configuration produced. A
    /// hand-assembled token would assert the test's beliefs about that configuration rather than the
    /// configuration itself, and would keep passing after the configuration changed underneath it.
    /// </para>
    /// <para>
    /// A REFUSAL IS RAISED RATHER THAN RETURNED. The issuer answers an unlisted audience with a refusal
    /// outcome and no token, which for infrastructure is a fault in the test's own setup rather than a
    /// result to be inspected; raising it names the configuration key to correct, whereas returning a null
    /// token would surface later as an unexplained failure at whichever route the client was pointed at.
    /// Assertion is left to the caller: this type is infrastructure and does not depend on the assertion
    /// library.
    /// </para>
    /// </remarks>
    internal IssuedToken IssueToken(string subject, string audience, IEnumerable<string> scopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentNullException.ThrowIfNull(scopes);

        List<string> requested = [.. scopes];

        TokenIssuanceResult result = CreateForgingIssuer(subject, audience, requested)
            .Issue(new TokenIssuanceRequest(subject, audience, requested));

        return result.Token ?? throw new InvalidOperationException(
            $"This host's token issuer answered '{result.Outcome}' instead of minting, so no token is "
            + "available to present. The outcome names which of the issuer's three gates refused, and "
            + "each has a different fix. '"
            + nameof(TokenIssuanceOutcome.AudienceNotPermitted)
            + $"': the audience is not a member of configuration key '{SecurityOptions.SectionName}:"
            + $"{nameof(SecurityOptions.Audiences)}' - request one the roster carries, which "
            + $"{nameof(ResolveInboundAudience)}() returns, or extend the roster through "
            + $"{nameof(Audiences)} before the host starts. '"
            + nameof(TokenIssuanceOutcome.CallerNotPermitted)
            + "': the audience IS on the roster but this caller is not authorised for it - add the "
            + $"identity to {nameof(IssuanceFixture)}.{nameof(IssuanceFixture.TestCallers)}, which every "
            + "host this suite builds authorises for every roster audience. '"
            + nameof(TokenIssuanceOutcome.ScopesNotPermitted)
            + "': the caller is authorised for the audience but NONE of the requested scopes is "
            + $"permitted - add the scope to {nameof(IssuanceFixture)}."
            + $"{nameof(IssuanceFixture.TestScopes)}. Note that a PARTLY permitted scope set is not a "
            + "refusal at all: it mints a token carrying the narrower granted set, so a missing scope "
            + "surfaces as an unexpected granted value rather than as this exception. This message never "
            + "echoes an audience, a caller, a scope or any configured value.");
    }

    /// <summary>
    /// Builds an issuer over this host's real signing chain and clock, carrying a permission roster that
    /// admits exactly the request about to be made.
    /// </summary>
    /// <param name="subject">The caller identity the token will claim.</param>
    /// <param name="audience">The audience the token will address.</param>
    /// <param name="scopes">The scopes the token will carry.</param>
    /// <returns>An issuer that will mint the requested token rather than refuse or narrow it.</returns>
    /// <remarks>
    /// <para>
    /// WHY THIS EXISTS, STATED PLAINLY BECAUSE IT LOOKS LIKE A BYPASS AND IS NOT ONE. The service's issuer
    /// consults an issuance permission matrix - <c>Security:Callers</c> - that decides which caller may
    /// address which audience and which scopes it may hold, refusing an unlisted pairing and intersecting
    /// the requested scope set with the permitted one. That decision is a security property with its own
    /// dedicated tests. <see cref="IssueToken"/> is not one of them: it is a CREDENTIAL FORGE whose job is
    /// to produce a valid token so that a RECEIVER can be exercised, for arbitrary subjects, audiences and
    /// scopes chosen by whichever test needs them - including a subject that is deliberately not any
    /// service's identity. Routing the forge through the deployed roster would force every one of those
    /// tests to enumerate a permission matrix that has nothing to do with what it is asserting, and would
    /// silently narrow the scope claim of any token whose scopes the roster did not happen to list.
    /// </para>
    /// <para>
    /// WHAT IS STILL REAL, WHICH IS EVERYTHING THAT MAKES A TOKEN A TOKEN. The signing chain is this host's
    /// <see cref="SigningKeyProvider"/> - the same imported key, the same key identifier, the same
    /// algorithm - and the clock is this host's substituted clock, so the header, the issuer, the key
    /// identifier and every instant in the minted token are by construction the ones this host's
    /// configuration produces. It is the same <see cref="TokenIssuer"/> TYPE performing the mint, so the
    /// claim set, the truncation and the granted-scope formatting are the production ones. The ONLY thing
    /// substituted is the permission roster, and it is substituted with a roster that permits precisely
    /// what was asked for - so the forge narrows nothing and invents nothing.
    /// </para>
    /// <para>
    /// The issuer identity, lifetime, algorithm and key identifier are COPIED from the host's own resolved
    /// options rather than restated, so a test that reshapes any of them through
    /// <see cref="ShapeOptions"/> still gets a token that matches what the host would have minted.
    /// </para>
    /// </remarks>
    private TokenIssuer CreateForgingIssuer(string subject, string audience, IList<string> scopes)
    {
        SecurityOptions configured = ResolveSecurityOptions();

        SecurityOptions forging = new()
        {
            Issuer = configured.Issuer,
            SigningKeyId = configured.SigningKeyId,
            SigningAlgorithm = configured.SigningAlgorithm,
            TokenLifetime = configured.TokenLifetime,
        };

        forging.Audiences.Add(audience);

        SecurityCallerOptions caller = new() { Identity = subject };
        SecurityCallerGrantOptions grant = new() { Audience = audience };

        // Exactly the requested set, so the intersection the production issuer performs is the identity
        // function here and the granted claim equals the request. A blank entry cannot arrive - the
        // request type refuses one - and a grant needs at least one scope, so a caller minting with no
        // scope at all is given a single placeholder that the request will not match and therefore will
        // not carry. That case is unreachable through the request type today and is handled rather than
        // asserted, because an unreachable branch that throws is worse than one that behaves.
        foreach (string scope in scopes)
        {
            grant.Scopes.Add(scope);
        }

        if (grant.Scopes.Count == 0)
        {
            grant.Scopes.Add("unreachable.placeholder");
        }

        caller.Grants.Add(grant);
        forging.Callers.Add(caller);

        return new TokenIssuer(
            Services.GetRequiredService<SigningKeyProvider>(),
            Options.Create(forging),
            Services.GetRequiredService<IssuanceClientRegistry>(),
            Services.GetRequiredService<TimeProvider>(),
            Services.GetRequiredService<ILoggerFactory>().CreateLogger<TokenIssuer>());
    }

    /// <summary>
    /// Creates a client that presents a token this host minted for the default subject, its own accepted
    /// audience and one scope.
    /// </summary>
    /// <returns>A client whose every request carries a valid bearer token.</returns>
    /// <exception cref="InvalidOperationException">The host's issuer refused the audience.</exception>
    /// <remarks>
    /// <para>
    /// THE SCOPE SET IS THREE ENTRIES, AND THE SECOND AND THIRD ARE BOTH LOAD-BEARING. Two of this
    /// service's route groups demand a named scope, and a client that omitted either would be refused with
    /// the published 403 on exactly the routes most rows using this helper are calling: the C-02
    /// cryptographic group demands <c>security.crypto</c>, and the authenticated probe demands
    /// <c>ping</c> - which the published document declares, with its own 403, on <c>GET /v1/ping</c>.
    /// </para>
    /// <para>
    /// IT IS STILL NOT A WILDCARD. Three named scopes, every one of them a member of the suite's declared
    /// set, none an administrative name, and each corresponding to a route group that actually demands it -
    /// so a row asserting a scope REFUSAL cannot accidentally pass here: it mints its own narrower token
    /// instead. Adding every scope in existence to this default would have made the scope gate
    /// unobservable, which is the failure this comment exists to prevent.
    /// </para>
    /// </remarks>
    internal HttpClient CreateAuthenticatedClient() =>
        CreateAuthenticatedClient(
            DefaultTokenSubject,
            ResolveInboundAudience(),
            [DefaultTokenScope, SecurityScopes.Crypto, SecurityScopes.Ping]);

    /// <summary>
    /// Creates a client that presents a token this host minted for the given identity, audience and
    /// scopes.
    /// </summary>
    /// <param name="subject">The identity the token is minted for.</param>
    /// <param name="audience">The audience the token is addressed to.</param>
    /// <param name="scopes">The requested scopes.</param>
    /// <returns>A client whose every request carries that token.</returns>
    /// <exception cref="ArgumentException">An argument violates the request contract.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="scopes"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The host's issuer refused the audience.</exception>
    /// <remarks>
    /// <para>
    /// NEEDED BECAUSE THE FALLBACK POLICY GOVERNS EVERY ROUTE THAT DID NOT EXPLICITLY OPT OUT, and this
    /// service has exactly three that did: the readiness probe and the two published metadata documents.
    /// The generated contract document is NOT among them, so a test that reads it authenticates like any
    /// other caller.
    /// </para>
    /// <para>
    /// THIS IS NOT A WAY AROUND AUTHENTICATION AND MUST NEVER BECOME ONE. The client presents a genuine
    /// token, signed with the host's own key, verifiable against the key set that same host publishes.
    /// A test that needs to observe the unauthorized response uses the base class's own client and
    /// presents nothing.
    /// </para>
    /// <para>
    /// The client is created through the base class, which tracks it and disposes it with the factory, so
    /// there is nothing extra for a caller to own. Addressing an audience OTHER than the one this host
    /// accepts inbound yields a client whose requests are refused, which is the point of audience
    /// validation and a legitimate thing to assert.
    /// </para>
    /// </remarks>
    internal HttpClient CreateAuthenticatedClient(
        string subject,
        string audience,
        IEnumerable<string> scopes)
    {
        IssuedToken token = IssueToken(subject, audience, scopes);

        HttpClient client = CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerSchemeName, token.AccessToken);

        return client;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// THE CRYPTOGRAPHIC HANDLE IS ALREADY GONE BY THE TIME ANYTHING GETS HERE. The generated key object
    /// is released by its own declaration scope inside the constructor, so this factory never holds one
    /// for its lifetime and there is no unmanaged resource for a disposal path to leak. Likewise every
    /// client handed out by the helpers above is created through the base class, which tracks it and
    /// disposes it together with the host, so this override owns no client of its own.
    /// </para>
    /// <para>
    /// WHAT IS RELEASED HERE IS THE GENERATED MATERIAL, AS A HYGIENE MEASURE RATHER THAN A CORRECTNESS
    /// ONE. Dropping the references stops a disposed factory that is still reachable - held by a test
    /// class field, say - from keeping secret-shaped text alive for the rest of the run. A managed string
    /// cannot be overwritten in place, so releasing the only reference to it is the strongest action
    /// available; that is stated plainly rather than dressed up as erasure. The base implementation is
    /// then invoked on both paths, which is what actually shuts the host down.
    /// </para>
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _keyStoreMaterial.Clear();
            SigningKeyMaterial = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Projects every override this factory holds into a flat configuration collection.
    /// </summary>
    /// <returns>
    /// The configuration entries to apply, which is empty of everything a test did not set apart from the
    /// two signing-material spellings.
    /// </returns>
    /// <remarks>
    /// <para>
    /// ONLY WHAT WAS SET IS EMITTED. An unset override contributes no key at all, so the application's own
    /// settings file remains the source of that value and this factory does not become a second copy of a
    /// deployment fact able to drift from the first. The signing material is the one exception and is
    /// always emitted, because it is the one value the settings file deliberately does not carry.
    /// </para>
    /// <para>
    /// THE SIGNING MATERIAL IS EMITTED UNDER BOTH SPELLINGS, AND EMITTED AS THE EMPTY STRING WHEN THERE IS
    /// NONE. Both spellings because the flat key and the section member reach the bound instance by
    /// different routes and a test should not need to know which one the service used; the empty string
    /// rather than omission because omission would leave any value the surrounding process environment
    /// carries in force, and a fail-fast assertion has to hold on a developer machine and on a
    /// continuous-integration agent alike. An empty value is exactly what the composition root's presence
    /// guard skips and what the validator rejects, so the modelled outcome is the same either way.
    /// </para>
    /// <para>
    /// EVERY RENDERED VALUE IS CULTURE-INVARIANT. A duration goes out in the constant format the binder
    /// parses and an index is rendered with invariant digits, so the collection a test writes is the
    /// collection the host binds whatever locale the machine is set to.
    /// </para>
    /// <para>
    /// The collection is assembled fresh on each host build and the caller-supplied entries are merged
    /// LAST, so they win over every typed override above them.
    /// </para>
    /// </remarks>
    private Dictionary<string, string?> BuildConfigurationOverrides()
    {
        Dictionary<string, string?> overrides = new(StringComparer.Ordinal)
        {
            [SecurityOptions.SigningKeyEnvironmentVariableName] =
                SigningKeyMaterial ?? string.Empty,
            [SectionScopedSigningKeyConfigurationKey] = SigningKeyMaterial ?? string.Empty,
        };

        // EVERY ROSTER SECRET THE SETTINGS FILES NAME, WITHOUT WHICH NO HOST STARTS. The registry
        // resolves each named key eagerly and refuses to construct when one is absent, so these are not
        // convenience values - they are the difference between a host and a startup failure. Generated
        // per process, never committed (C-F).
        foreach (KeyValuePair<string, string?> secret in RosterSecretOverrides())
        {
            overrides[secret.Key] = secret.Value;
        }

        AddOverride(overrides, nameof(SecurityOptions.Issuer), Issuer);
        AddOverride(overrides, nameof(SecurityOptions.SigningKeyId), SigningKeyId);
        AddOverride(overrides, nameof(SecurityOptions.SigningAlgorithm), SigningAlgorithm);
        AddOverride(overrides, nameof(SecurityOptions.JwksPath), JwksPath);
        AddOverride(
            overrides,
            nameof(SecurityOptions.OpenIdConfigurationPath),
            OpenIdConfigurationPath);
        AddOverride(overrides, nameof(SecurityOptions.TokenEndpointPath), TokenEndpointPath);

        if (TokenLifetime is TimeSpan lifetime)
        {
            AddOverride(
                overrides,
                nameof(SecurityOptions.TokenLifetime),
                lifetime.ToString("c", CultureInfo.InvariantCulture));
        }

        if (KeyStoreConfigurationKeyPrefix is not null)
        {
            overrides[
                $"{SecurityOptions.SectionName}:{nameof(SecurityOptions.KeyStore)}:"
                + nameof(SecurityKeyStoreOptions.ConfigurationKeyPrefix)] =
                KeyStoreConfigurationKeyPrefix;
        }

        if (InboundAudience is not null)
        {
            overrides[InboundAudienceConfigurationKey] = InboundAudience;
        }

        // The material behind each declared reference, under the flat key the resolver composes:
        // the configured prefix followed by the reference itself, with no separator of its own.
        string prefix = KeyStoreConfigurationKeyPrefix ?? DefaultKeyStoreConfigurationKeyPrefix;

        foreach (KeyValuePair<string, string> entry in _keyStoreMaterial)
        {
            overrides[prefix + entry.Key] = entry.Value;
        }

        foreach (KeyValuePair<string, string?> setting in Settings)
        {
            overrides[setting.Key] = setting.Value;
        }

        return overrides;
    }

    /// <summary>
    /// Records one override against a member of the security section, or nothing when it was not set.
    /// </summary>
    /// <param name="overrides">The collection being assembled.</param>
    /// <param name="memberName">The member's name, taken from the options type rather than spelled.</param>
    /// <param name="value">The value to record, or <see langword="null"/> to record nothing.</param>
    /// <remarks>
    /// The section path is composed from the options type's own section constant and the member's own
    /// name, so a rename on either side is a compile-time change here rather than a silently ineffective
    /// configuration key. The empty string is NOT null and is recorded, which is what lets a test drive a
    /// blank-value rejection.
    /// </remarks>
    private static void AddOverride(
        Dictionary<string, string?> overrides,
        string memberName,
        string? value)
    {
        if (value is null)
        {
            return;
        }

        overrides[$"{SecurityOptions.SectionName}:{memberName}"] = value;
    }

    /// <summary>
    /// Applies the overrides that configuration cannot express to the bound options instance.
    /// </summary>
    /// <param name="options">The bound instance, after binding and after the composition root's own
    /// post-configure step.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// EXACTLY THREE THINGS HAPPEN HERE, AND EACH IS SOMETHING A CONFIGURATION KEY GENUINELY CANNOT DO.
    /// </para>
    /// <para>
    /// The two collections are REPLACED rather than merged. Configuration binds a collection by index into
    /// a get-only list, and there is no key that removes an index, so a shorter override laid over a
    /// longer settings value leaves the tail behind - which for the audience roster can even trip the
    /// duplicate rule and fail startup with no visible cause. Clearing and re-adding is the only total
    /// override there is. An empty override collection means inherit, so neither clear runs unless a test
    /// actually asked for one.
    /// </para>
    /// <para>
    /// Then the caller's shaping callback runs, last, so it can express anything at all - an empty roster,
    /// a mutated rather than replaced collection, a value whose textual form the binder would refuse - and
    /// so that whatever it produces is what the startup validation judges.
    /// </para>
    /// </remarks>
    private void ApplyOptionOverrides(SecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The suite's shared client-certificate authority, so that a row driving the issuance operation
        // presents a certificate this host actually trusts. Applied unconditionally and BEFORE the
        // caller's own shaping delegate, so a row that needs the fail-closed no-anchor posture can clear
        // it deliberately rather than inheriting it by accident.
        options.ClientCertificateAuthorityPath = IssuanceFixture.ClientCertificateAuthorityPath;

        if (Audiences.Count > 0)
        {
            options.Audiences.Clear();

            foreach (string audience in Audiences)
            {
                options.Audiences.Add(audience);
            }
        }

        if (PermittedKeyRefs.Count > 0)
        {
            options.KeyStore.PermittedKeyRefs.Clear();

            foreach (string keyRef in PermittedKeyRefs)
            {
                options.KeyStore.PermittedKeyRefs.Add(keyRef);
            }
        }

        // The suite's authorization matrix, installed AFTER the roster above is settled - the matrix's
        // audiences must be roster members or the service's own validator refuses this host - and BEFORE
        // the caller's shaping delegate, so a row exercising the matrix itself can narrow or clear what
        // this installed. It replaces the settings file's production rows rather than extending them; see
        // IssuanceFixture.PermitTestCallers for why replacing is the correct direction.
        IssuanceFixture.PermitTestCallers(options);

        // SNAPSHOTTED BEFORE THE DELEGATE RUNS, which is the only moment at which "what was here already"
        // is still answerable. The reconciliation below prunes only these, so a grant or a roster entry the
        // delegate declares is left exactly as declared - including one that deliberately names an audience
        // the delegate also removed, which must still refuse the host.
        List<CallerAuthorizationOptions> preexistingRows = [.. options.CallerAuthorizations];
        List<SecurityClientOptions> preexistingClients = [.. options.Clients];

        ShapeOptions?.Invoke(options);

        // AND THE ONE STEP THAT CANNOT RUN BEFORE THE DELEGATE. The matrix and the roster above were built
        // from the audience roster as it stood a moment ago; a delegate that NARROWS the roster - several
        // declare a complete deployment of their own - leaves grants and roster entries naming audiences
        // the deployment no longer serves, and the service refuses to START on either.
        IssuanceFixture.ReconcileGrantsWithRoster(options, preexistingRows, preexistingClients);
    }
}

/// <summary>
/// A clock that reports exactly the instant it was given and never advances on its own: the substituted
/// end of this service's clock seam.
/// </summary>
/// <remarks>
/// <para>
/// WHY IT IS HAND AUTHORED. The framework's own testing clock ships in a package that is deliberately
/// absent from the repository's central package versions, and central package management makes a locally
/// versioned reference a hard restore failure rather than a quiet drift. The test project's own header
/// records that this double is authored here for exactly that reason, so this type is the sanctioned
/// implementation rather than a workaround.
/// </para>
/// <para>
/// WHAT IT PROVES WHEN IT IS INSTALLED. The clock is one of the two primary non-determinism sources in
/// this service, and the characterization model requires every such value to be maskable from BOTH the
/// master and the candidate recording. Substituting the whole seam also proves the code under test reads
/// NO ambient clock of its own: if it did, a clock that never moves would not produce byte-identical
/// output twice.
/// </para>
/// <para>
/// IT DOES NOT TRUNCATE, ROUND OR OTHERWISE OPINIONATE, ON PURPOSE. It reports back precisely what was
/// set, including any fractional second, because the service's minter performs its own truncation to a
/// whole second and a double that pre-truncated would silently make that behaviour untestable - the
/// fractional input needed to observe it could never reach the code that drops it.
/// </para>
/// <para>
/// THREADING. Reads are safe from any number of request threads: the instant is held as a single integer
/// field and read through a volatile access, so a reader can never observe a half-written value. Mutation
/// is expected from the controlling test thread rather than concurrently with other mutation, which is
/// what a test seam is for; <see cref="Advance"/> is a read-modify-write and is not atomic against a
/// second concurrent mutation.
/// </para>
/// <para>
/// It is directly constructible and holds no reference to a host, so a pure unit test that needs a fixed
/// clock can use it without booting anything.
/// </para>
/// </remarks>
internal sealed class DeterministicTimeProvider : TimeProvider
{
    /// <summary>The reported instant, as a count of UTC ticks.</summary>
    /// <remarks>
    /// Stored as an integer rather than as a date-and-offset value so that a read is a single machine word
    /// and cannot tear under a concurrent write. The offset is reconstructed as zero on every read, which
    /// is correct because the stored count is a UTC one.
    /// </remarks>
    private long _utcTicks;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeterministicTimeProvider"/> class fixed at the real
    /// current instant.
    /// </summary>
    /// <remarks>
    /// THE DEFAULT IS THE REAL INSTANT RATHER THAN A LITERAL ONE, AND THAT IS LOAD BEARING. The inbound
    /// bearer handler's clock-skew allowance is configured to zero rather than to the framework's five
    /// minutes, so a token whose validity window sits away from real time is refused by the very host that
    /// minted it. Starting from now keeps a minted token acceptable while the clock is still FIXED, so a
    /// determinism assertion and a round-trip assertion can both hold against one factory. A test that
    /// wants a literal instant, and does not need its token to survive inbound validation, pins one with
    /// <see cref="SetUtcNow"/>.
    /// </remarks>
    public DeterministicTimeProvider()
        : this(DateTimeOffset.UtcNow)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeterministicTimeProvider"/> class fixed at a given
    /// instant.
    /// </summary>
    /// <param name="instant">The instant this clock will report until it is set or advanced.</param>
    public DeterministicTimeProvider(DateTimeOffset instant) => _utcTicks = instant.UtcTicks;

    /// <summary>
    /// The time zone reported as local: always coordinated universal time.
    /// </summary>
    /// <remarks>
    /// Fixed rather than inherited so that a local-time read is deterministic too. Left to the base
    /// implementation it would follow the machine's own zone, and a value derived from it would then differ
    /// between a developer machine and a continuous-integration agent while every instant in the test was
    /// identical - a difference that is invisible in the assertion and obvious only in the diff.
    /// </remarks>
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <summary>
    /// The number of timestamp units in one second: one tick's worth, so a timestamp is a tick count.
    /// </summary>
    /// <remarks>
    /// Reporting the tick as the unit makes <see cref="GetTimestamp"/> and <see cref="GetUtcNow"/> the same
    /// quantity in the same scale, so an elapsed-time measurement taken across this clock equals the
    /// difference between the two instants exactly, with no conversion error and no dependence on the
    /// machine's high-resolution counter.
    /// </remarks>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => new(Volatile.Read(ref _utcTicks), TimeSpan.Zero);

    /// <inheritdoc/>
    /// <remarks>
    /// Derived from the reported instant rather than from the machine's counter, so that elapsed time
    /// measured through this clock is zero until the clock is deliberately advanced. A timestamp taken from
    /// the real counter would reintroduce exactly the non-determinism the rest of this type removes.
    /// </remarks>
    public override long GetTimestamp() => Volatile.Read(ref _utcTicks);

    /// <summary>
    /// Fixes this clock at a new instant.
    /// </summary>
    /// <param name="instant">The instant to report from now on.</param>
    /// <remarks>
    /// The way to pin a literal instant, which is what makes an issued claim identical across RUNS rather
    /// than merely across two calls in one run. A token minted under an instant far from real time will not
    /// survive this service's own inbound validation, since its clock-skew allowance is zero; that is a
    /// property to exploit deliberately, not a limitation to work around.
    /// </remarks>
    internal void SetUtcNow(DateTimeOffset instant) => Volatile.Write(ref _utcTicks, instant.UtcTicks);

    /// <summary>
    /// Moves this clock forward by an interval.
    /// </summary>
    /// <param name="interval">A non-negative interval to add to the reported instant.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="interval"/> is negative, or the resulting instant is outside the representable
    /// range.
    /// </exception>
    /// <remarks>
    /// NON-NEGATIVE ONLY, DELIBERATELY. A clock that runs backwards is a different concept from one that
    /// advances, and conflating the two would let a sign error read as an ordinary step;
    /// <see cref="SetUtcNow"/> expresses going back explicitly. Advancing past a configured token lifetime
    /// is how an expiry is observed without waiting for one.
    /// </remarks>
    internal void Advance(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.Zero);

        // Composed through the date-and-offset type rather than by adding tick counts, so that an interval
        // large enough to leave the representable range is reported as the argument fault it is instead of
        // silently storing a count no read could reconstruct.
        DateTimeOffset advanced = GetUtcNow() + interval;

        Volatile.Write(ref _utcTicks, advanced.UtcTicks);
    }
}

/// <summary>
/// An entropy source that yields a specified, repeatable byte sequence: the substituted end of this
/// service's randomness seam.
/// </summary>
/// <remarks>
/// <para>
/// THE SEQUENCE IS SPECIFIED RATHER THAN INCIDENTAL. The nth byte this instance has ever yielded, counting
/// from zero across every call, is the low byte of <see cref="Seed"/> plus n. With the default seed of zero
/// that is the ramp 0, 1, 2 and so on to 255, then wrapping to zero - so a drawn byte equals its own
/// position, which makes every value the random blob, random string and identifier surfaces derive from it
/// an exact and hand-checkable function of the draw. Because the counter spans calls rather than restarting
/// at each one, two consecutive draws yield DIFFERENT bytes, which is what a real source would do and what
/// keeps a rejection-sampling loop from spinning; <see cref="Reset"/> returns to the start of the sequence
/// when a test wants two calls to agree.
/// </para>
/// <para>
/// WHY NOT A PSEUDO-RANDOM GENERATOR WITH A FIXED SEED. Its sequence is an implementation detail of the
/// runtime rather than a specification, so an expected value derived from it is not reproducible across
/// runtime versions and cannot be written down in a test at all. An arithmetic ramp is reproducible by
/// construction and is documented above in one sentence.
/// </para>
/// <para>
/// WHAT IS REPRODUCIBLE DOWNSTREAM, AND UNDER WHICH PRESERVED DEFAULTS. Installing this source makes the
/// random blob exactly the ramp, and makes the random-string and identifier forms exact functions of it -
/// but only against a stated flag set, because both of those surfaces choose an alphabet or a layout from
/// one. Their no-argument forms use the legacy defaults
/// <see cref="PowerFramework.Shared.Kernel.Enums.CRYPTO_RNDSTRING_DEFAULT"/>, which is digits plus
/// letters, and <see cref="PowerFramework.Shared.Kernel.Enums.CRYPTO_GUID_DEFAULT"/>, which is braces
/// plus separators. Those constants are CONSUMED from the shared kernel here and are never re-declared:
/// their preserved spellings appear in serialized payloads, log records and characterization recordings,
/// and a copy in a test file would be a second definition able to drift from the one the service reads.
/// </para>
/// <para>
/// WHAT IT IS NOT FOR. This is a test double and produces entirely predictable bytes. It exists so that a
/// golden-master comparison can mask the values this service draws on both sides; it is never registered
/// outside a test host, and the production source it replaces draws from the operating system's
/// cryptographically secure generator.
/// </para>
/// <para>
/// THREADING. The seam's contract requires an implementation to be safe to call concurrently, because the
/// providers that consume it are registered once and shared. The draw and the counter update therefore
/// happen together under a lock, which also keeps the sequence contiguous: two concurrent callers receive
/// adjacent stretches of it rather than overlapping ones.
/// </para>
/// <para>
/// It is directly constructible and holds no reference to a host, so a pure unit test can drive a provider
/// with it without booting anything.
/// </para>
/// </remarks>
internal sealed class DeterministicEntropySource : IEntropySource
{
    /// <summary>Serializes a draw with the counter update that belongs to it.</summary>
    private readonly Lock _gate = new();

    /// <summary>The number of bytes yielded so far, which is also the sequence position.</summary>
    private long _bytesDrawn;

    /// <summary>The number of completed draws.</summary>
    private long _fillCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeterministicEntropySource"/> class starting the
    /// sequence at zero.
    /// </summary>
    public DeterministicEntropySource()
        : this(0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeterministicEntropySource"/> class starting the
    /// sequence at a given value.
    /// </summary>
    /// <param name="seed">The first byte of the sequence.</param>
    /// <remarks>
    /// An offset into the same ramp rather than a different generator, so the sequence stays as easy to
    /// write down. Two sources with different seeds yield different values for the same draw, which is how
    /// a test shows that an output genuinely depends on the bytes drawn rather than on the length asked for.
    /// </remarks>
    public DeterministicEntropySource(byte seed) => Seed = seed;

    /// <summary>The first byte of the sequence this source yields.</summary>
    internal byte Seed { get; }

    /// <summary>
    /// The number of bytes yielded since construction or since the last reset, which is also the current
    /// position in the sequence.
    /// </summary>
    /// <remarks>
    /// Observing it is how the BLOCK-DRAWING behaviour of a consumer is established: a provider that drew
    /// one byte per character and one that drew a block per call can produce identical strings and different
    /// counts, and only the count tells them apart.
    /// </remarks>
    internal long BytesDrawn
    {
        get
        {
            lock (_gate)
            {
                return _bytesDrawn;
            }
        }
    }

    /// <summary>The number of completed draws since construction or since the last reset.</summary>
    internal long FillCount
    {
        get
        {
            lock (_gate)
            {
                return _fillCount;
            }
        }
    }

    /// <summary>
    /// Fills the buffer with the next bytes of the sequence.
    /// </summary>
    /// <param name="destination">
    /// The buffer to fill. Every byte of it is overwritten. An empty buffer is a valid request, completes
    /// without effect on the sequence position, and still counts as a draw - which is what lets a consumer
    /// pass a zero-length buffer without special-casing it while a test can still see that it did.
    /// </param>
    /// <remarks>
    /// The WHOLE buffer is written before the position advances, so this implementation can never return
    /// normally having left a caller-visible byte at whatever value it already held - a partial fill that
    /// looked successful would be indistinguishable from a legitimate draw of zeros on a freshly allocated
    /// buffer.
    /// </remarks>
    public void Fill(Span<byte> destination)
    {
        lock (_gate)
        {
            for (int index = 0; index < destination.Length; index++)
            {
                destination[index] = (byte)((Seed + _bytesDrawn + index) & ByteMask);
            }

            _bytesDrawn += destination.Length;
            _fillCount++;
        }
    }

    /// <summary>
    /// Returns the sequence to its start and clears both counters.
    /// </summary>
    /// <remarks>
    /// The way to make two calls agree: draw, reset, draw again, and the second result is byte-identical to
    /// the first. Without it the counter would keep climbing and the two would legitimately differ, which is
    /// the right default for a source standing in for a real one.
    /// </remarks>
    internal void Reset()
    {
        lock (_gate)
        {
            _bytesDrawn = 0;
            _fillCount = 0;
        }
    }

    /// <summary>The mask that keeps the computed sequence value inside one byte.</summary>
    private const int ByteMask = 0xFF;
}
