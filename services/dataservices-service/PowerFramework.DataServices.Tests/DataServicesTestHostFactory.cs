// =====================================================================================================
//  DataServicesTestHostFactory - THE IN-PROCESS HOST FIXTURE FOR THE DataServices SERVICE-LEVEL SUITES
//  ---------------------------------------------------------------------------------------------------
//  SUBJECT   The DEPLOYED composition root, services/dataservices-service/PowerFramework.DataServices/
//            Program.cs, booted over Microsoft.AspNetCore.TestHost rather than Kestrel. What runs is the
//            real options binding and its start-time validation, the real localization provider
//            selection, the real STOCK JwtBearer handler, the real default AND fallback authorization
//            policies, the real endpoint registrations, the real thin REST projection and the two real
//            published gRPC surfaces. Only the collaborators that would LEAVE THE PROCESS are
//            substituted, and each through a seam the application project published on purpose.
//
//  ORACLE    ws_objects/pfw.pbl.src/pfw.sra:L88-L108     The framework lifecycle this host reproduces
//              as startup and shutdown. :L91 `pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)`, :L108
//              `pfwFinalize()` - docs/README.md's initialisation section states the pair MUST be paired.
//            ws_objects/pfw.pbl.src/pfw.sra:L94         `lang = "en"`, hardcoded. THE DEFECT IS THE
//              HARDCODING, NOT THE VALUE, so "en" survives as the CONFIGURED DEFAULT and this fixture
//              does not override it - see the DO-NOT-OVERRIDE ledger in ConfigureWebHost.
//            ws_objects/pfw.pbl.src/pfw.sra:L111-L144    `systemerror` unpacks a seven-field assert
//              payload split on `~r~n` and then executes `HALT CLOSE` [:L143]. The POSTURE this fixture
//              inherits is that a STRUCTURAL fault ends the process: the host's `ValidateOnStart` and
//              its three eager `Assertions.Assert` guards are NEVER suppressed here.
//            ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru
//              The 22-event surface (9 semantic + 13 raw `pbm_dwn*`), the fused topic strings
//              "0-itemchanged" [:L54] and "1-editchanged" [:L57], the EID_* gate [:L41-L43] and the four
//              cross-event state fields [:L89-L96] that a stateless boundary has nowhere to put. The
//              recording broker exposed below is what lets a service-level suite observe that dispatch.
//            ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L503
//              `event constructor;call super::constructor;SetNull(#ShowFilteredRows)` - the THREE-STATE
//              auto-determine default. Null is a value here, and this fixture never coerces it.
//            ws_objects/pfw.tests.pbl.src/w_test_assert.srw
//              The assert oracle - `Assert(num > 0)` and `Assert(num > 0,"Invalid Number!")`. Its
//              relevance to this file is the fail-fast posture, not a payload.
//            tests/blink/test_jws.htm:L8-L23
//              READ AS THE ANTI-PATTERN AND NOTHING ELSE. That page hardcodes a plaintext PEM RSA
//              PRIVATE KEY and signs a JWS with it. NOT ONE CHARACTER OF IT APPEARS HERE, and neither
//              does anything from ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw. This fixture
//              mints NO token at all - see section 3.
//
//  USER RULES: NONE. `review_rules` returns "No user rules provided." - a single line, verified twice.
//  No user rule governs this file, nothing is inferred or back-filled from convention, and the
//  enterprise baseline of AAP 0.7.2 applies in its place: nullable on, warnings as errors, no secret in
//  source, hand-written doubles rather than a mocking package, and a test project held to exactly the
//  same bar as the application it tests.
//
//  THE CONSTRAINT LEDGER FOR THIS FILE (AAP 0.7.3), each with where it is discharged
//    C-G  no new attack surface: every created boundary authenticated. THE CONSTRAINT THIS FILE MOST
//         DIRECTLY SERVES. It makes the authenticated posture TESTABLE WITHOUT WEAKENING IT: section 3
//         defines exactly two postures, the anonymous one runs through the UNTOUCHED JwtBearer pipeline
//         and the authenticated one is supplied by an EXPLICIT test scheme. Authorization is never
//         disabled globally, no AllowAnonymous fallback is registered, `JwtBearerOptions` is never
//         touched, no `RequireAuthorization` is removed from any route or gRPC service, and there is no
//         development bypass - Endpoints/PingEndpoints.cs deliberately has none in ANY environment, and
//         `ForDevelopment()` exists precisely so that can be PROVEN rather than assumed.
//    C-F  secrets: never replicate; the three named sites are a floor, not a ceiling. NO key,
//         certificate, password or token literal appears anywhere below, in any form. There is no PEM
//         block marker, no base64 key blob, no password-shaped literal, no signing-key configuration
//         key and no reference to any signing secret by name. The only credential-shaped value in the
//         whole fixture is the OPAQUE PLACEHOLDER that TestDoubles' `ScriptedTokenProvider` composes at
//         runtime behind `DeterministicRandomSource.PlaceholderCredentialPrefix`; it is generated, never
//         written down, structurally incapable of matching a real provider's pattern, and never
//         persisted.
//    sole-issuer invariant (AAP 0.6.6.3). Exactly ONE signing secret exists in the whole system and
//         Security holds it. DataServices holds VERIFICATION MATERIAL ONLY, and its project
//         deliberately declares no reference to `Microsoft.IdentityModel.JsonWebTokens`. That assembly
//         does arrive in this test project's output transitively through the bearer handler - and this
//         file does not touch it, does not name a type from it, and does not mint a credential with it.
//         The test authentication scheme of section 3 is what makes that abstinence possible.
//    C-E  no fabricated database. This host boots with NO database. There is no DbContext, no EF Core
//         type, no `Microsoft.Data.Sqlite` type, no connection string, no connection factory and no
//         migration call anywhere below. Persistence is the only service in the system that holds a
//         storage provider; storage lives behind the Persistence edge, which section 4 substitutes.
//    C-I / C-J / AAP 0.6.7 R4  no network, no Docker daemon, no live upstream. A unit and service suite
//         must pass on a host with no daemon at all, so nothing here may depend on compose even though
//         the orchestrated bring-up itself has been exercised. Every outbound edge is
//         intercepted: the primary HTTP message handler for EVERY factory-created client is replaced,
//         and an unmatched request FAILS LOUDLY rather than being answered with a plausible status. The
//         bearer handler resolves its metadata LAZILY, so the mandated 401 needs no network at all.
//    C-B  no behaviour improvements. ConfigureWebHost overrides ONLY addresses, metadata and resilience
//         tuning - never a preserved legacy default. The DO-NOT-OVERRIDE ledger enumerates every one of
//         them with its legacy locator, and `AdditionalSettings` is the PER-TEST OPT-IN for a suite that
//         genuinely needs a different value, so the shared default is never moved to suit one test.
//    C-K  document every decision. Every substitution seam below states WHY it was chosen over the
//         alternatives, and every deliberate NON-substitution states why substituting would be wrong.
//    AAP 0.4.5.3 naming. No test file is in the repository .editorconfig's CA1707 / IDE1006 suppression
//         list - only fifteen named APPLICATION files are. This file therefore REFERENCES preserved
//         SCREAMING_SNAKE identifiers (`RetCode.E_DB_ERROR`) and DECLARES none.
//
//  THREE DIVERGENCES FROM THIS FILE'S OWN BRIEF, RECORDED BECAUSE THE BRIEF ASKED FOR THEM TO BE
//    1. The brief states there is "no `public partial class Program` shim on this service". THERE IS
//       ONE: Program.cs declares `public partial class Program` with a `protected Program()`
//       constructor, documented there as load-bearing for exactly this factory. So the entry point is
//       PUBLIC and nameable directly. `InternalsVisibleTo("PowerFramework.DataServices.Tests")` also
//       exists in the application .csproj, granted for the internal registration groups and for the
//       coverage gate. Either mechanism alone would suffice; both are present. No shim is added here
//       and no reflection is used.
//    2. The brief names five consumer files. Their real spellings differ: HealthEndpointsTests.cs,
//       DataServicesOptionsTests.cs, DataWindowServiceContractTests.cs and
//       ColumnExpressionServiceContractTests.cs. The last three drive the gRPC implementations DIRECTLY
//       with a hand-built `ServerCallContext`, and HealthEndpointsTests.cs builds its own
//       `WebApplicationBuilder`. This fixture is therefore COMPLEMENTARY rather than duplicative: it is
//       the only path in the suite that exercises the real composition root end to end over a transport.
//    3. The brief cites `Authentication:Jwt` as if it sat under `DataServices`. It is a TOP-LEVEL
//       section - `JwtAuthenticationOptions.SectionName` is "Authentication:Jwt" - and the keys written
//       below use the constants rather than restating either path.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * It does not weaken authentication or authorization by any route. No AllowAnonymous, no relaxed
//      validation switch, no environment-conditional branch, no fallback-policy edit, no token minted.
//    * It does not suppress `ValidateOnStart` or any of the three eager startup guards. A structurally
//      invalid configuration MUST fail startup, and DataServicesOptionsTests.cs asserts that.
//    * It does not substitute `IExpressionTraceSink` or `II18nProvider` BY DEFAULT, because either
//      would change observable behaviour - see the two opt-in flags and their reasons.
//    * It does not bind `IDataWindowEventChainFactory`. That seam's fake lives in another test file
//      outside this file's dependency set; `AdditionalServiceConfiguration` is how a suite supplies one.
//    * It registers nothing whatsoever for DesignSystem, Documents, Integration or ScriptBridge, and no
//      route under /v1/design/**, /v1/documents/**, /v1/integration/** or /v1/scripting/**. Those four
//      reserved 501 declarations are GATEWAY's routing metadata (constraint C-D, AAP 0.4.4).
// =====================================================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Grpc;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;
using Xunit;

// The generated contract types are reached through ALIASES rather than a namespace import, for the same
// reason Program.cs does it: PowerFramework.Contracts.Common.V1 publishes a `RetCode` message whose bare
// name collides with the kernel's preserved `RetCode` constant class, and
// PowerFramework.Contracts.DataServices.V1 publishes a `DataWindowService` and a `ContextMenuModel` whose
// bare names collide with this service's own domain vocabulary. Naming exactly the types used keeps the
// collision surface at zero (CS0104) instead of relying on lexical luck.
using ColumnExpressionContractClient =
    PowerFramework.Contracts.DataServices.V1.ColumnExpressionService.ColumnExpressionServiceClient;
using ConflictDetail = PowerFramework.Contracts.Common.V1.ConflictDetail;
using ConflictRow = PowerFramework.Contracts.Common.V1.ConflictRow;
using DataWindowContractClient =
    PowerFramework.Contracts.DataServices.V1.DataWindowService.DataWindowServiceClient;
using DomainContextMenuModel = PowerFramework.DataServices.Services.ContextMenuModel;
using PersistenceBeginSessionRequest = PowerFramework.Contracts.Persistence.V1.BeginSessionRequest;
using PersistenceBeginSessionResponse = PowerFramework.Contracts.Persistence.V1.BeginSessionResponse;
using PersistenceCommandClient =
    PowerFramework.Contracts.Persistence.V1.CommandService.CommandServiceClient;
using PersistenceCreateQueryTaskRequest =
    PowerFramework.Contracts.Persistence.V1.CreateQueryTaskRequest;
using PersistenceCreateQueryTaskResponse =
    PowerFramework.Contracts.Persistence.V1.CreateQueryTaskResponse;
using PersistenceCreateUpdateTaskRequest =
    PowerFramework.Contracts.Persistence.V1.CreateUpdateTaskRequest;
using PersistenceCreateUpdateTaskResponse =
    PowerFramework.Contracts.Persistence.V1.CreateUpdateTaskResponse;
using PersistenceEndSessionRequest = PowerFramework.Contracts.Persistence.V1.EndSessionRequest;
using PersistenceEndSessionResponse = PowerFramework.Contracts.Persistence.V1.EndSessionResponse;
using PersistenceOperationStatus = PowerFramework.Contracts.Persistence.V1.OperationStatus;
using PersistenceQueryClient = PowerFramework.Contracts.Persistence.V1.QueryService.QueryServiceClient;
using PersistenceQueryRequest = PowerFramework.Contracts.Persistence.V1.QueryRequest;
using PersistenceQueryResponse = PowerFramework.Contracts.Persistence.V1.QueryResponse;
using PersistenceReleaseQueryTaskRequest =
    PowerFramework.Contracts.Persistence.V1.ReleaseQueryTaskRequest;
using PersistenceReleaseQueryTaskResponse =
    PowerFramework.Contracts.Persistence.V1.ReleaseQueryTaskResponse;
using PersistenceReleaseUpdateTaskRequest =
    PowerFramework.Contracts.Persistence.V1.ReleaseUpdateTaskRequest;
using PersistenceReleaseUpdateTaskResponse =
    PowerFramework.Contracts.Persistence.V1.ReleaseUpdateTaskResponse;
using PersistenceSessionHandle = PowerFramework.Contracts.Persistence.V1.SessionHandle;
using PersistenceTaskHandle = PowerFramework.Contracts.Persistence.V1.TaskHandle;
using PersistenceTransactionClient =
    PowerFramework.Contracts.Persistence.V1.TransactionService.TransactionServiceClient;
using PersistenceUpdateClient =
    PowerFramework.Contracts.Persistence.V1.UpdateService.UpdateServiceClient;
using PersistenceUpdateCounts = PowerFramework.Contracts.Persistence.V1.UpdateCounts;
using PersistenceUpdateRequest = PowerFramework.Contracts.Persistence.V1.UpdateRequest;
using PersistenceUpdateResponse = PowerFramework.Contracts.Persistence.V1.UpdateResponse;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.DataServices.Tests;

// -----------------------------------------------------------------------------------------------------
//  1. THE FIXTURE
// -----------------------------------------------------------------------------------------------------

/// <summary>
/// The shared in-process host the DataServices service-level suites run against: the deployed
/// composition root over <c>TestHost</c>, with every out-of-process collaborator substituted through a
/// published seam.
/// </summary>
/// <remarks>
/// <para>
/// USE IT AS AN xUNIT <b>CLASS</b> FIXTURE - <c>IClassFixture&lt;DataServicesTestHostFactory&gt;</c> -
/// rather than a collection fixture. A class fixture is created once per test class and disposed with
/// it, which keeps each consuming class ISOLATED; a collection fixture would serialise every test in
/// every class that joined the collection and would let one class's mutation of a scriptable double be
/// observed by another. The cost of the extra hosts is one host per class, which is one more than the
/// theoretical minimum and far cheaper than cross-class coupling. A class that needs to mutate a
/// default owns its own instance instead - see <see cref="ForDevelopment"/>.
/// </para>
/// <para>
/// THE ENVIRONMENT IS SET EXPLICITLY AND DEFAULTS TO <c>Production</c>, never inherited from the machine
/// running the suite. Inheriting it would let a developer's <c>ASPNETCORE_ENVIRONMENT</c> decide which
/// settings overlay a test asserted against, and a Development-only relaxation could then flatter a
/// result. <see cref="ForDevelopment"/> is the second entry point, and it exists for exactly one
/// purpose: proving that <c>/v1/ping</c> still answers 401 without a credential in Development, i.e.
/// that there is NO development bypass. <c>appsettings.Development.json</c> overrides only addresses and
/// idle timeouts and relaxes no validation switch, so the property is real rather than aspirational.
/// </para>
/// <para>
/// TWO AUTHENTICATION POSTURES, AND THEY ARE NAMED SO THEY CANNOT BE CONFUSED.
/// <see cref="CreateAnonymousClient"/> is <b>Posture A</b>: it proves the endpoint REJECTS AN ANONYMOUS
/// CALLER, and it does so through the completely untouched stock bearer pipeline.
/// <see cref="CreateAuthenticatedClient"/> is <b>Posture B</b>: it proves the endpoint ACCEPTS A VALID
/// PRINCIPAL, and it supplies that principal from an explicit test scheme rather than from a credential.
/// Neither posture makes <c>/health</c> require authentication - <c>/health</c> is anonymous on all four
/// services under contract C-10 and stays that way here.
/// </para>
/// <para>
/// NOTHING BELOW REACHES A SOCKET, A DAEMON OR A DATABASE. The primary message handler of every
/// factory-created client is replaced by <see cref="Transport"/>, which answers scripted routes and
/// THROWS on an unmatched one, so a suite that mis-wires an address gets a loud failure naming what
/// arrived instead of a plausible status. That is what makes the whole file runnable with networking
/// unavailable (constraints C-I and C-J, AAP 0.6.7 R4).
/// </para>
/// </remarks>
public sealed class DataServicesTestHostFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The authentication scheme that supplies <b>Posture B</b>'s principal.
    /// </summary>
    /// <remarks>
    /// Named for this assembly so it can never be mistaken for a scheme the application registers. The
    /// application registers exactly one scheme of its own - the stock bearer scheme - and this file adds
    /// to that set rather than replacing anything in it.
    /// </remarks>
    public const string TestPrincipalSchemeName = "PowerFramework.DataServices.Tests.Principal";

    /// <summary>
    /// The request header whose PRESENCE selects <b>Posture B</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESENCE ONLY - the value is never read, never compared and never required to be anything, which
    /// is the whole point: a header whose value mattered would be a credential, and this file holds none
    /// (constraint C-F). <see cref="CreateAuthenticatedClient"/> sets it to a fixed non-secret marker.
    /// </para>
    /// <para>
    /// A HEADER RATHER THAN A DEFAULT SCHEME SWITCH, because that is what keeps the two postures
    /// separable inside ONE host: with the header absent the selector forwards to the untouched bearer
    /// handler, so Posture A is genuinely the deployed pipeline and not a stand-in for it.
    /// </para>
    /// </remarks>
    public const string TestPrincipalHeaderName = "X-PowerFramework-Test-Principal";

    /// <summary>
    /// The non-secret marker <see cref="CreateAuthenticatedClient"/> writes into
    /// <see cref="TestPrincipalHeaderName"/>.
    /// </summary>
    /// <remarks>
    /// It is a WORD, not a credential: it authenticates nothing on its own, it is not a token, it has no
    /// dot-separated segments, and it says what it is. It exists so that a captured request is legible in
    /// a diagnostic rather than showing an empty header.
    /// </remarks>
    public const string TestPrincipalHeaderValue = "service-level-test-principal";

    /// <summary>The subject claim the test principal carries by default.</summary>
    /// <remarks>
    /// <para>
    /// A claim, never a credential. No secret, key or token value is present in it.
    /// </para>
    /// <para>
    /// IT IS GATEWAY'S IDENTITY, AND IT HAS TO BE. Both contracts and every projected route now require the
    /// caller to be one of <c>Authentication:Jwt:PermittedCallers</c>, which the shipped settings file
    /// declares as Gateway alone - the AAP fixes the call graph as layered and acyclic, so nothing but
    /// Gateway calls DataServices. A fixture claiming any other subject would produce a principal the
    /// service correctly refuses, and Posture B would then be proving a refusal rather than an acceptance.
    /// </para>
    /// </remarks>
    public const string TestPrincipalSubject = "powerframework-gateway";

    /// <summary>
    /// The request header a test uses to override the subject the test principal claims.
    /// </summary>
    /// <remarks>
    /// PER REQUEST RATHER THAN PER FIXTURE, deliberately. A row proving that an unpermitted caller is
    /// refused must vary the subject WITHOUT disturbing any other row, and fixture-level state shared
    /// across a class would do exactly that. Absent, the default above applies.
    /// </remarks>
    public const string TestPrincipalSubjectHeaderName = "X-PowerFramework-Test-Subject";

    /// <summary>
    /// The request header a test uses to override the space-delimited scope set the test principal carries.
    /// </summary>
    /// <remarks>
    /// Space-delimited because that is how RFC 6749 carries a granted scope set and how the receiver's
    /// policy reads it, so a row exercising the split exercises the real format. An EMPTY value is
    /// meaningful and distinct from an absent header: it produces a principal with a scope claim carrying
    /// nothing, which is what a caller granted no scope actually holds.
    /// </remarks>
    public const string TestPrincipalScopeHeaderName = "X-PowerFramework-Test-Scope";

    /// <summary>
    /// The scope set the test principal carries by default - both of this service's published scopes.
    /// </summary>
    /// <remarks>
    /// BOTH, because that is what Gateway's own credential carries: it requests the DataWindow scope and
    /// the column-expression scope together in one token, since a single credential is attached to every
    /// call it makes. A default carrying only one would make every row touching the other contract fail on
    /// a permission it never meant to exercise.
    /// </remarks>
    public const string TestPrincipalScopes =
        "dataservices.datawindow dataservices.columnexpression";

    /// <summary>
    /// The forwarding policy scheme that becomes the host's default authenticate scheme under test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE MECHANISM THAT KEEPS CONSTRAINT C-G INTACT WHILE MAKING IT TESTABLE. A policy scheme
    /// carries no handler of its own; its <c>ForwardDefaultSelector</c> chooses, per request, which real
    /// scheme handles authenticate, challenge and forbid. With
    /// <see cref="TestPrincipalHeaderName"/> absent it forwards to
    /// <see cref="JwtBearerDefaults.AuthenticationScheme"/>, so an anonymous request is authenticated,
    /// challenged and refused by the STOCK BEARER HANDLER exactly as deployed - the 401 and its
    /// <c>WWW-Authenticate</c> come from the real handler, not from anything in this file.
    /// </para>
    /// <para>
    /// THE ALTERNATIVES WERE REJECTED FOR SPECIFIC REASONS. Making the test scheme the outright default
    /// would mean Posture A's 401 came from the TEST handler, so the deployed pipeline would go unproven
    /// - the one thing the environment's own acceptance criterion asks to be verified rather than
    /// assumed. Adding the test scheme to the authorization policy's scheme list would mutate the
    /// application's default and fallback policies, which this file must not do. Amending
    /// <c>JwtBearerOptions</c> with locally held verification material would work, but it would put a
    /// key in this assembly, and the brief's preferred design - mint no token at all - is also the one
    /// that keeps the sole-issuer invariant structurally true rather than merely observed.
    /// </para>
    /// </remarks>
    public const string SchemeSelectorName = "PowerFramework.DataServices.Tests.SchemeSelector";

    /// <summary>
    /// A DataWindow name nothing binds - neither the deployed catalogue nor
    /// <see cref="BindsDataWindowHost"/>.
    /// </summary>
    /// <remarks>
    /// The unknown-handle arm of every headless-model operation is published contract - it answers
    /// <c>RetCode.E_INVALID_HANDLE</c> - so it has to stay reachable whichever wiring is in force. This
    /// value is absent from <c>Domain/DataWindowCatalogue.cs</c> AND explicitly refused by the fixture's
    /// own binding, so a case can assert the negative without knowing which of the two it is running
    /// against.
    /// </remarks>
    public const string UnboundDataWindowName = "pfw-unbound-datawindow";

    /// <summary>
    /// The audience the host under test is configured to accept.
    /// </summary>
    /// <remarks>
    /// A fixed test value rather than the deployed one, because the deployed audience names a deployment
    /// and this host is not one. It is metadata, not behaviour, which is why overriding it is inside
    /// constraint C-B rather than against it.
    /// </remarks>
    private const string TestAudience = "powerframework-dataservices-tests";

    /// <summary>
    /// The authority the bearer handler is pointed at: a reserved name that resolves nowhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SAFE PRECISELY BECAUSE IT IS NEVER CONTACTED. The stock handler resolves discovery metadata
    /// LAZILY, on the first request that actually needs a key, and an anonymous request is refused
    /// before any key is needed - so Posture A's 401 costs zero network calls even though this address
    /// could not be reached if it were tried. The <c>.invalid</c> top-level domain is reserved by
    /// specification for exactly this use, so the value cannot collide with a real host.
    /// </para>
    /// <para>
    /// PLAIN HTTP, AND THE PAIRING IS MANDATORY. <c>JwtAuthenticationOptionsValidator</c> refuses a
    /// non-https authority while <c>RequireHttpsMetadata</c> is true, so this fixture writes
    /// <c>RequireHttpsMetadata=false</c> alongside it. That is the supported loopback topology the
    /// validator's own message describes, and it relaxes no token VALIDATION: issuer, audience, lifetime
    /// and issuer-signing-key validation all stay on, because this fixture never writes any of them.
    /// </para>
    /// </remarks>
    private const string UnreachableAuthority = "http://security.dataservices-tests.invalid";

    /// <summary>The loopback address the Persistence gRPC clients are pointed at.</summary>
    /// <remarks>
    /// An address is required - the options validator refuses a blank or non-absolute one - and it must
    /// be one nothing is listening on, because the traffic is intercepted before it leaves the process.
    /// The port is the one the deployed manifest gives Persistence, so a captured request is legible
    /// against the documented port map.
    /// </remarks>
    private const string LoopbackPersistenceAddress = "http://127.0.0.1:5101";

    /// <summary>The loopback base address the Security typed client is pointed at.</summary>
    /// <remarks>
    /// Same reasoning as <see cref="LoopbackPersistenceAddress"/>, with the port the manifest gives
    /// Security. No query, no fragment and no user information: <c>ConfiguredAddress</c> rejects all
    /// three on a base address, and the third rejection is a secrets control rather than tidiness.
    /// </remarks>
    private const string LoopbackSecurityAddress = "http://127.0.0.1:5104";

    /// <summary>
    /// The issuance secret every host this factory boots presents on the token-issuance edge - a value
    /// generated once per test process and never a literal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WITHOUT THIS, NO HOST IN THIS ASSEMBLY STARTS. <c>Configuration/DataServicesOptions.cs</c>'s
    /// validator refuses a deployment that can present neither of the two credentials contract C-01
    /// accepts on <c>POST /v1/tokens</c>, because such a service obtains no token and can reach nothing
    /// downstream. That refusal is the point of the rule, so it is satisfied here rather than suppressed:
    /// this factory does not disable <c>ValidateOnStart</c> and does not want to.
    /// </para>
    /// <para>
    /// GENERATED RATHER THAN WRITTEN, for the same reason the production value is: a literal in a test
    /// file is a credential-shaped string in version control, and a scanner cannot tell one from a real
    /// one. It is not asserted against by any test - what tests assert is the effect of its presence or
    /// absence - so its value is genuinely arbitrary.
    /// </para>
    /// </remarks>
    private static readonly string IssuanceSecret =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// The retry count both resilience pipelines are reduced to.
    /// </summary>
    /// <remarks>
    /// ONE, NOT ZERO. Zero is not merely undesirable, it fails the library's own option validation, and
    /// disabling resilience would remove behaviour the edge tests cover:
    /// <c>Microsoft.Extensions.Http.Resilience</c> is in the stack precisely because an in-process call
    /// could not fail in transit and a network call can (AAP 0.5.3). One attempt after the first keeps
    /// the retry path live while a deliberately failing upstream test still finishes promptly. No
    /// latency or throughput claim attaches to the value; the repository publishes no such target
    /// anywhere (AAP 0.8.5).
    /// </remarks>
    private const string ReducedRetryAttempts = "1";

    /// <summary>
    /// The retry base delay both resilience pipelines are reduced to.
    /// </summary>
    /// <remarks>
    /// Ten milliseconds, written in the invariant time-span form the configuration binder parses. The
    /// circuit-breaker settings are left at the deployed values on purpose: the standard handler
    /// validates that the sampling duration is at least double the attempt timeout and that the total
    /// request timeout is not shorter than one attempt, so lowering one of the three while inventing
    /// values for the others is how a host ends up failing option validation for a reason nobody stated.
    /// </remarks>
    private const string ReducedRetryDelay = "00:00:00.010";

    /// <summary>The environment name this host runs under.</summary>
    private readonly string _environmentName;

    /// <summary>
    /// The recording transport every factory-created client sends through.
    /// </summary>
    /// <remarks>
    /// ONE SEAM COVERS BOTH OUTBOUND EDGES, which is why it was chosen over two narrower ones: the
    /// Security typed client and all four Persistence gRPC clients resolve their primary handler through
    /// <c>IHttpClientFactory</c>, so replacing the factory-wide default intercepts every one of them with
    /// a single registration and needs no mocking package.
    /// </remarks>
    private readonly ScriptedHttpMessageHandler _transport = new();

    /// <summary>The reproducible source the placeholder credential is composed from.</summary>
    private readonly DeterministicRandomSource _random = new();

    /// <summary>The frozen clock every component in the host reads.</summary>
    private readonly DeterministicTimeProvider _clock = new();

    /// <summary>The narrow Security seam: an issuer that answers an opaque placeholder.</summary>
    private readonly ScriptedTokenProvider _tokenProvider;

    /// <summary>The arrival-ordered record of every broker dispatch.</summary>
    private readonly ScriptedDispatchLog _dispatchLog = new();

    /// <summary>The recording broker, so the fused-topic dispatch order is observable.</summary>
    private readonly ScriptedEventBroker _broker;

    /// <summary>The recording localization provider. Installed only on opt-in.</summary>
    private readonly ScriptedI18nProvider _localization = new();

    /// <summary>The scriptable macro-invocation double for the inverted C-04 channel.</summary>
    private readonly ScriptedMacroChannel _macroChannel = new();

    /// <summary>The recording expression-trace sink. Installed only on opt-in.</summary>
    private readonly ScriptedTraceSink _traceSink = new();

    /// <summary>The scriptable, observable Persistence edge.</summary>
    private readonly ScriptedPersistenceEdge _persistenceEdge = new();

    /// <summary>The optional DataWindow host binding, retained per name.</summary>
    private readonly BoundDataWindowHostFactory _hostBinding;

    /// <summary>Every gRPC channel this fixture created, so all of them are disposed with it.</summary>
    private readonly List<GrpcChannel> _channels = [];

    /// <summary>Guards <see cref="_channels"/> against concurrent creation and disposal.</summary>
    private readonly Lock _channelGate = new();

    /// <summary>
    /// Whether the resources this fixture owns beyond the host have already been released.
    /// </summary>
    /// <remarks>
    /// Both disposal entry points converge on one release, so a consumer that uses <c>using</c> and one
    /// that uses <c>await using</c> get identical cleanup and neither can double-release.
    /// </remarks>
    private bool _released;

    /// <summary>Initializes a fixture for the <c>Production</c> environment.</summary>
    /// <remarks>
    /// THE ONLY PUBLIC CONSTRUCTOR, and it has to be: xUnit requires a fixture type to declare exactly
    /// one, and a second public overload would fail every test in every consuming class with a
    /// fixture-construction error rather than with anything about the code under test.
    /// <see cref="ForDevelopment"/> is the way to any other environment.
    /// </remarks>
    public DataServicesTestHostFactory()
        : this(Environments.Production)
    {
    }

    /// <summary>Initializes a fixture for an explicit environment.</summary>
    /// <param name="environmentName">The environment name the host should run under.</param>
    private DataServicesTestHostFactory(string environmentName)
    {
        _environmentName = environmentName;

        // Constructed over the SAME clock and the same reproducible source the host reads, so the
        // provider's expiry arithmetic and the host's cannot disagree for a reason that has nothing to do
        // with the product. The credential it hands out is composed here, at run time, and is never
        // written down anywhere (constraint C-F).
        _tokenProvider = new ScriptedTokenProvider(_clock, _random);

        // ONE recording broker across every bound host, deliberately. The application creates a broker
        // per control [se_cst_dw.sru:L562] and this fixture does NOT contradict that for the deployed
        // path - it applies only to the optional host binding below, where a single log is what makes the
        // whole dispatch sequence readable from one place. A suite that needs per-host isolation
        // registers its own IDataWindowHostFactory through AdditionalServiceConfiguration.
        _broker = new ScriptedEventBroker(_dispatchLog);
        _hostBinding = new BoundDataWindowHostFactory(_broker);
    }

    /// <summary>Creates a fixture that boots the host in the <c>Development</c> environment.</summary>
    /// <returns>A fixture the caller owns and MUST dispose.</returns>
    /// <remarks>
    /// <para>
    /// THE SECOND ENTRY POINT, and its purpose is a single property: that <c>/v1/ping</c> answers 401
    /// without a credential in Development exactly as in Production, i.e. that no development bypass
    /// exists. <c>Endpoints/PingEndpoints.cs</c> applies <c>RequireAuthorization</c> unconditionally and
    /// <c>appsettings.Development.json</c> relaxes no validation switch, so the property holds - and this
    /// entry point is how it is VERIFIED rather than asserted from reading.
    /// </para>
    /// <para>
    /// The caller owns the instance because the shared class fixture must outlive every test in its
    /// class; a test that needs a different environment therefore creates and disposes its own.
    /// </para>
    /// </remarks>
    public static DataServicesTestHostFactory ForDevelopment() =>
        new(Environments.Development);

    /// <summary>The environment name this host runs under.</summary>
    /// <remarks>
    /// Exposed so a suite can state which overlay it is asserting against instead of inferring it, and so
    /// the no-development-bypass test can prove it really did boot Development.
    /// </remarks>
    public string EnvironmentName => _environmentName;

    /// <summary>The audience the host under test is configured to accept.</summary>
    /// <remarks>
    /// Published so a suite asserting on the bound options names the same value the fixture wrote, rather
    /// than restating a literal that could then drift away from it.
    /// </remarks>
    public static string ConfiguredAudience => TestAudience;

    /// <summary>The authority the host under test is pointed at.</summary>
    /// <remarks>
    /// Published for the same reason as <see cref="ConfiguredAudience"/>. It is deliberately unreachable;
    /// see the constant's own remarks for why that is safe.
    /// </remarks>
    public static string ConfiguredAuthority => UnreachableAuthority;

    /// <summary>
    /// The issuance secret every host this factory boots is configured with.
    /// </summary>
    /// <remarks>
    /// PUBLISHED FOR EXACTLY ONE PURPOSE, WHICH IS TO ASSERT ITS ABSENCE. The constant's own remarks say
    /// no test asserts against its value, and that remains true of its CONTENT: what
    /// <c>IssuanceCredentialStartupRecordTests</c> needs it for is the C-F assertion that no startup
    /// record carries it. An assertion of that kind cannot be written against a value the suite cannot
    /// name, and the alternative - searching records for anything base64-shaped - would be a weaker claim
    /// that a passing run could not distinguish from a leak of a differently-shaped secret.
    /// </remarks>
    internal static string ConfiguredIssuanceSecret => IssuanceSecret;

    /// <summary>
    /// Extra host configuration applied ON TOP of the deployed settings files, keyed by configuration
    /// path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE PER-TEST OPT-IN THAT CONSTRAINT C-B REQUIRES. A suite that genuinely needs a different
    /// value - enabling <c>DataServices:ColumnExpression:Trace</c> to exercise the trace channel, say -
    /// writes it here rather than having the fixture move a shared default. The distinction matters:
    /// moving the default would make every other suite's assertion about that default vacuous, which is
    /// exactly why the fixture overrides none of them.
    /// </para>
    /// <para>
    /// Populate it BEFORE the first call that starts the host, because host configuration is read once
    /// during startup. A consumer that needs it should own its own fixture rather than mutate a shared
    /// one, so no test in another class inherits a setting it never asked for.
    /// </para>
    /// <para>
    /// It may also be used to write a DELIBERATELY INVALID value, which is how the fail-fast posture is
    /// proven: the host's <c>ValidateOnStart</c> and its three eager startup guards are never suppressed
    /// here, so a structurally invalid configuration fails startup rather than the first request that
    /// touched it.
    /// </para>
    /// </remarks>
    public IDictionary<string, string?> AdditionalSettings { get; } =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>
    /// Extra service registrations applied LAST, after every substitution this fixture makes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONE GENERAL EXTENSION POINT. Applied last so a suite's bespoke registration wins over the
    /// fixture's own defaults. It is how a suite supplies the one seam this fixture deliberately leaves
    /// alone - <c>IDataWindowEventChainFactory</c>, whose fake lives in another test file and is
    /// therefore not this file's to reference - and how it substitutes anything else the composition root
    /// registers with <c>TryAdd</c>.
    /// </para>
    /// <para>
    /// IT MAY NOT BE USED TO WEAKEN THE BOUNDARY. Nothing registered through it may relax token
    /// validation, grant an anonymous fallback, remove a <c>RequireAuthorization</c>, or open a socket:
    /// those are precisely the properties this fixture exists to keep provable, and a convenient
    /// registration is the easiest way to undo one by accident.
    /// </para>
    /// <para>
    /// Populate it before the host is first used.
    /// </para>
    /// </remarks>
    public IList<Action<IServiceCollection>> AdditionalServiceConfiguration { get; } = [];

    /// <summary>
    /// Whether the recording localization provider replaces the configured one.
    /// <see langword="false"/> by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OFF BY DEFAULT, AND THAT IS A CONSTRAINT RATHER THAN A PREFERENCE. The configured provider is
    /// selected from <c>DataServices:Localization:Locale</c>, whose preserved default <c>"en"</c> comes
    /// from <c>ws_objects/pfw.pbl.src/pfw.sra:L94</c>, and the English provider REPRODUCES BOTH
    /// documented mistranslations. Substituting a recording provider changes what
    /// <c>se_cst_dw.sru:L355</c> and <c>:L357</c> resolve to, so doing it by default would be a
    /// behaviour change dressed up as instrumentation (constraint C-B).
    /// </para>
    /// <para>
    /// Switch it on when the assertion is about WHICH keys the host asked to translate rather than about
    /// the text it got back, then read <see cref="Localization"/>. Set it before the host is first used.
    /// </para>
    /// </remarks>
    public bool SubstitutesLocalizationProvider { get; set; }

    /// <summary>
    /// Whether the recording trace sink replaces the registered one. <see langword="false"/> by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OFF BY DEFAULT FOR A STRUCTURAL REASON, not a stylistic one. The composition root registers ONE
    /// <c>ExpressionTraceBroker</c> and reaches it under two names - the concrete type and
    /// <c>IExpressionTraceSink</c> - so that C-04's <c>TraceChannel</c> and the expression sessions that
    /// emit into it share a single instance. Replacing only the interface would leave the channel reading
    /// the broker while every record went to the substitute, and half the trace would silently vanish.
    /// </para>
    /// <para>
    /// Switch it on for a suite that wants the emitted records without opening the channel, then read
    /// <see cref="TraceSink"/>. Set it before the host is first used.
    /// </para>
    /// </remarks>
    public bool SubstitutesExpressionTraceSink { get; set; }

    /// <summary>
    /// Whether the fixture binds a DataWindow host and model set for every name except
    /// <see cref="UnboundDataWindowName"/>. <see langword="false"/> by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OFF BY DEFAULT MEANS "USE THE DEPLOYED WIRING", AND THAT WIRING IS NO LONGER A REFUSAL. The
    /// composition root binds a real headless host, model set and event chain over the transcribed
    /// definitions in <c>Domain/DataWindowCatalogue.cs</c>, which is what AAP 0.2.1.3 Correction 3 and
    /// AAP 0.3.5 require of DataServices: Correction 3 tells it to define its own host contract and
    /// IMPLEMENT AGAINST IT, recording <c>se_cst_datawindow</c> as REFERENCE-only, and 0.3.5 assigns the
    /// HEADLESS half of every UI capability here while deferring only the RENDERING half. An earlier
    /// revision registered three <c>Unbound*</c> implementations and cited constraint C-D against
    /// binding; that reading was wrong, and this switch's prose said so along with it.
    /// </para>
    /// <para>
    /// SO WITH THIS OFF, A DEPLOYED HANDLE NAME NOW RESOLVES. <c>dw_sqlite</c> and
    /// <c>dw_test_dwsvc</c> bind against the production catalogue, and any other name - including
    /// <see cref="UnboundDataWindowName"/> - still answers the published negative,
    /// <c>RetCode.E_INVALID_HANDLE</c> for a model or chain request and a failed open for an expression
    /// session. That negative remains PUBLISHED CONTRACT and a suite must still be able to assert it.
    /// </para>
    /// <para>
    /// Switch it on to substitute the FIXTURE'S OWN host - a test double built from
    /// <c>FakeDataWindowFixtures</c>, reachable only from this assembly and shipped nowhere - when a case
    /// needs to script host behaviour rather than exercise the deployed catalogue. It binds every name
    /// except <see cref="UnboundDataWindowName"/>, which is broader than the catalogue and is why some
    /// cases still want it.
    /// </para>
    /// <para>
    /// <c>IDataWindowEventChainFactory</c> is NOT substituted even when this is on, so a chain request
    /// reaches the DEPLOYED chain factory and therefore resolves for a catalogue name and refuses for any
    /// other. A chain double lives in another test file and is that file's to register through
    /// <see cref="AdditionalServiceConfiguration"/>; reaching into it from here would couple this fixture
    /// to a type outside its own dependency set.
    /// </para>
    /// <para>
    /// Set it before the host is first used.
    /// </para>
    /// </remarks>
    public bool BindsDataWindowHost { get; set; }

    /// <summary>
    /// The recording transport every factory-created client sends through, and the proof that none of
    /// them reached a network.
    /// </summary>
    /// <remarks>
    /// Script routes on it with <c>RouteJson</c>, <c>RouteStatus</c>, <c>RouteFault</c> or
    /// <c>RouteSequence</c>, and read <c>Exchanges</c> to assert what was sent - including WHETHER an
    /// <c>Authorization</c> header was present, which it records without ever storing the parameter's
    /// value. An unmatched request throws rather than being answered, which is deliberate: a plausible
    /// 500 would let a mis-wired test pass through the client's error handling and assert something true
    /// about a request that never reached its intended endpoint.
    /// </remarks>
    internal ScriptedHttpMessageHandler Transport => _transport;

    /// <summary>The scriptable, observable Persistence edge behind contracts C-05 to C-08.</summary>
    /// <remarks>
    /// <para>
    /// THE NARROW SEAM, CHOSEN OVER THE TRANSPORT FOR THIS EDGE. <c>PersistenceClient</c> is a public
    /// class whose operations are <c>virtual</c> precisely so a test can substitute a derived double, so
    /// scripting an answer here needs no protocol framing, no trailer encoding and no channel. The
    /// transport substitution still stands underneath it, which is what makes an UNSCRIPTED operation
    /// fail loudly instead of silently reaching for a socket.
    /// </para>
    /// <para>
    /// Use <see cref="ScriptedPersistenceEdge.ScriptUpdateConflict(long, long, ConflictRow[])"/> to make
    /// <c>Update</c> answer gRPC <c>Aborted</c> carrying a <c>common.v1.ConflictDetail</c>; the REST
    /// projection then answers HTTP 409 with the same payload, which is the canonical mapping AAP 0.6.3.8
    /// requires. There is no silent overwrite anywhere in the system, and nothing retries a conflict.
    /// </para>
    /// </remarks>
    internal ScriptedPersistenceEdge PersistenceEdge => _persistenceEdge;

    /// <summary>The frozen clock every component in the host reads.</summary>
    /// <remarks>
    /// A REQUIREMENT OF THE PARITY MODEL RATHER THAN A CONVENIENCE. The characterization technique's one
    /// hard prerequisite is repeatability, with every non-deterministic value masked from BOTH the master
    /// and the candidate recording (AAP 0.6.7). Session idle expiry, the <c>/v1/ping</c> timestamp and the
    /// expression trace all read a clock, so substituting this one instance makes the whole process
    /// deterministic. <c>Advance</c> is how an idle-expiry sweep is driven without waiting.
    /// </remarks>
    internal DeterministicTimeProvider Clock => _clock;

    /// <summary>The reproducible source the placeholder credential was composed from.</summary>
    /// <remarks>
    /// Exposed so a suite can reproduce a value from a seed rather than capture it, which is the other
    /// half of the determinism requirement. It generates no real credential of any kind.
    /// </remarks>
    internal DeterministicRandomSource Random => _random;

    /// <summary>The Security token seam: an issuer answering an opaque placeholder.</summary>
    /// <remarks>
    /// IT NEVER DISABLES AUTHENTICATION. It always issues, so an outbound client still attaches an
    /// <c>Authorization</c> header and <see cref="Transport"/> can prove it did. A suite that needs the
    /// refused-issuance path sets its <c>Fault</c>, which models an issuer that said no - the honest way
    /// to reach that path, as against a provider that quietly answered with nothing.
    /// </remarks>
    internal ScriptedTokenProvider TokenProvider => _tokenProvider;

    /// <summary>The recording broker whose dispatches the bound hosts publish through.</summary>
    /// <remarks>
    /// Reachable so a service-level suite and a unit test observe the SAME double. It matters for the
    /// fused topic strings: dispatch order derives from the LEGACY name, which is what keeps
    /// <c>"0-itemchanged"</c> [<c>se_cst_dw.sru:L54</c>] ahead of <c>"1-editchanged"</c> [<c>:L57</c>],
    /// and the log decomposes each into its sequence, logical name and lifetime without flattening any of
    /// the three.
    /// </remarks>
    internal ScriptedEventBroker Broker => _broker;

    /// <summary>The arrival-ordered record of every dispatch <see cref="Broker"/> saw.</summary>
    internal ScriptedDispatchLog DispatchLog => _dispatchLog;

    /// <summary>
    /// The recording localization provider. Installed into the host only when
    /// <see cref="SubstitutesLocalizationProvider"/> is set.
    /// </summary>
    internal ScriptedI18nProvider Localization => _localization;

    /// <summary>
    /// The scriptable macro-invocation double for C-04's inverted macro channel.
    /// </summary>
    /// <remarks>
    /// EXPOSED RATHER THAN REGISTERED, because <c>IMacroInvocationChannel</c> is not a container seam:
    /// the host's own <c>MacroInvocationRouter</c> holds a channel PER SESSION and serves it through
    /// <c>ChannelFor</c>, and an in-process suite attaches a real one by opening
    /// <c>InvokeMethodChannel</c> with the <c>ColumnExpressionChannelHeaders</c> metadata. This double is
    /// the unit-level counterpart, published here so both levels script the same shape - the legacy
    /// expects the APPLICATION to implement the macro switch [<c>se_cst_dw.sru:L14</c>], which is why the
    /// stream is inverted in the first place.
    /// </remarks>
    internal ScriptedMacroChannel MacroChannel => _macroChannel;

    /// <summary>
    /// The recording expression-trace sink. Installed into the host only when
    /// <see cref="SubstitutesExpressionTraceSink"/> is set.
    /// </summary>
    internal ScriptedTraceSink TraceSink => _traceSink;

    // -------------------------------------------------------------------------------------------------
    //  2. THE TWO AUTHENTICATION POSTURES, AS CLIENTS
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>POSTURE A.</b> A client that presents NO credential at all, against the completely untouched
    /// bearer pipeline.
    /// </summary>
    /// <returns>A client whose requests carry no <c>Authorization</c> header and no test header.</returns>
    /// <remarks>
    /// <para>
    /// WHAT IT PROVES: that the endpoint REJECTS AN ANONYMOUS CALLER. <c>GET /v1/ping</c> must answer
    /// <b>401</b> - the acceptance criterion the environment's own setup instructions state (constraint
    /// C-L) - and it must do so in Development exactly as in Production, which
    /// <see cref="ForDevelopment"/> exists to verify. <c>GET /health</c> must still answer <b>200</b>
    /// through this same client, because <c>/health</c> is the one documented anonymous exception in the
    /// system (contract C-10).
    /// </para>
    /// <para>
    /// WITH THE TEST HEADER ABSENT the scheme selector forwards to
    /// <see cref="JwtBearerDefaults.AuthenticationScheme"/>, so the authenticate, challenge and refusal
    /// are all performed by the STOCK HANDLER as configured by <c>Program.cs</c>. Nothing in this fixture
    /// touches <c>JwtBearerOptions</c>, and the 401 therefore comes from the deployed pipeline rather than
    /// from a stand-in for it. No metadata is fetched to produce it, so the assertion holds with Security
    /// unreachable and with networking unavailable.
    /// </para>
    /// <para>
    /// The response-version handler is installed on every client this fixture creates, including this
    /// one. It is a no-op for HTTP/1.1 traffic and is what makes the gRPC channels work at all - see
    /// <see cref="TestServerResponseVersionHandler"/>.
    /// </para>
    /// </remarks>
    public HttpClient CreateAnonymousClient() =>
        CreateDefaultClient(new TestServerResponseVersionHandler());

    /// <summary>
    /// <b>POSTURE B.</b> A client that arrives with an authenticated principal and WITHOUT any token.
    /// </summary>
    /// <returns>A client whose requests carry <see cref="TestPrincipalHeaderName"/>.</returns>
    /// <remarks>
    /// <para>
    /// WHAT IT PROVES: that the endpoint ACCEPTS A VALID PRINCIPAL. It proves nothing about token
    /// validation, and it is named so that it cannot be mistaken for doing so - that is Posture A's and
    /// the Security service's business, not this fixture's.
    /// </para>
    /// <para>
    /// NO TOKEN IS MINTED AND NO KEY EXISTS IN THIS ASSEMBLY. The header's PRESENCE routes the request to
    /// <see cref="TestPrincipalSchemeName"/>, whose handler returns a principal directly. That is what
    /// keeps the sole-issuer invariant STRUCTURALLY true: Security holds the only signing secret in the
    /// system, DataServices holds verification material only, and this file adds no third party to that
    /// arrangement (AAP 0.6.6.3, constraints C-F and C-G).
    /// </para>
    /// <para>
    /// The principal satisfies the application's default policy AND both named contract policies: it claims
    /// Gateway's identity, which is the one entry in the shipped permitted-caller roster, and it carries
    /// both published scopes, which is exactly what Gateway's own credential carries. The claims are
    /// therefore sufficient by construction rather than by coincidence, and the fixture invents no
    /// requirement the application does not have - a row that needs an INSUFFICIENT principal overrides the
    /// subject or the scope set through the two headers declared above.
    /// </para>
    /// </remarks>
    public HttpClient CreateAuthenticatedClient()
    {
        HttpClient client = CreateAnonymousClient();

        // Added rather than assigned, because DefaultRequestHeaders.Add validates the name and keeps the
        // collection's own semantics; TryAddWithoutValidation would accept a name this fixture controls
        // anyway and would hide a typo in it.
        client.DefaultRequestHeaders.Add(TestPrincipalHeaderName, TestPrincipalHeaderValue);

        return client;
    }

    /// <summary>
    /// <b>POSTURE C.</b> A client whose principal is authenticated AND carries a chosen scope set.
    /// </summary>
    /// <param name="scopes">The scopes the principal should hold.</param>
    /// <returns>A client whose requests carry both the principal header and the scope header.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scopes"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// WHAT IT PROVES: that an endpoint accepts a principal holding a PARTICULAR permission, and - used
    /// with the wrong scope, or with none - that it refuses one that does not. Posture B is authenticated
    /// and unscoped, which the scope-gated surfaces refuse; this is the posture that gets past them.
    /// </para>
    /// <para>
    /// THE SET IS JOINED WITH ONE SPACE INTO ONE CLAIM, deliberately, because that is the form Security's
    /// issuer produces and the form the service's handler must be able to read. A fixture that emitted one
    /// claim per scope would let a row pass against an implementation that could not read a real token.
    /// </para>
    /// <para>
    /// It mints nothing and signs nothing. The scope header names permissions; it does not grant them, and
    /// no key exists in this assembly.
    /// </para>
    /// </remarks>
    public HttpClient CreateScopedClient(IReadOnlyList<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        HttpClient client = CreateAuthenticatedClient();

        // ⚠ AN EMPTY SET TRAVELS AS A SINGLE SPACE, NOT AS AN EMPTY STRING ⚠
        //
        // "Holds no scope" is a state a row must be able to produce, and the handler honours an empty header
        // value as exactly that rather than replacing it with the default - only an ABSENT header defaults.
        // But an empty header VALUE does not reliably survive the round trip: the message pipeline is free
        // to omit a header carrying no content, and when it does the handler sees an absent header and hands
        // back the DEFAULT credential, which carries both scopes. The row then asserts a refusal against a
        // fully authorised principal and fails for a reason that has nothing to do with the policy it was
        // testing.
        //
        // One space is a non-empty value that cannot be dropped and that splits to nothing: the claim
        // arrives, and both this fixture's handler and the production scope handler skip empty entries when
        // they split the space-delimited set, so the principal holds no scope by the same rule a real token
        // would be read under.
        client.DefaultRequestHeaders.Add(
            TestPrincipalScopeHeaderName,
            scopes.Count == 0 ? " " : string.Join(' ', scopes));

        return client;
    }

    // -------------------------------------------------------------------------------------------------
    //  3. THE gRPC HELPERS - C-03 AND C-04 IN PROCESS
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Creates a gRPC channel over the test server that presents an authenticated principal.
    /// </summary>
    /// <returns>The channel. It is disposed with this fixture; the caller need not dispose it.</returns>
    /// <remarks>
    /// <para>
    /// THIS IS WHAT MAKES THE TWO PUBLISHED gRPC SURFACES REACHABLE WITHOUT A LISTENER. Both are mapped
    /// with <c>RequireAuthorization</c> in <c>Program.cs</c>, so an unauthenticated channel is refused -
    /// which is correct and is what <see cref="CreateAnonymousGrpcChannel"/> exists to demonstrate.
    /// </para>
    /// <para>
    /// STREAMING WORKS, AND THAT IS THE POINT. C-03's <c>EventChain</c> is BIDIRECTIONAL and C-04 carries
    /// two INVERTED streams - <c>InvokeMethodChannel</c>, where the server calls back into its client
    /// because the legacy expects the application to implement the macro switch
    /// [<c>se_cst_dw.sru:L14</c>], and <c>TraceChannel</c>. All three run over this channel. The C-04
    /// channels are keyed by metadata rather than by a request field, so a caller supplies
    /// <c>ColumnExpressionChannelHeaders.SessionId</c> and
    /// <c>ColumnExpressionChannelHeaders.DataWindowHandle</c> as call headers; those constants are
    /// published by the application, so this fixture does not restate them.
    /// </para>
    /// <para>
    /// NO PACKAGE WAS ADDED FOR ANY OF THIS. <c>Grpc.Net.Client</c> arrives transitively through the
    /// application project's <c>Grpc.AspNetCore</c> reference, and the test host arrives with
    /// <c>Microsoft.AspNetCore.Mvc.Testing</c>.
    /// </para>
    /// </remarks>
    public GrpcChannel CreateAuthenticatedGrpcChannel() => CreateGrpcChannel(CreateAuthenticatedClient());

    /// <summary>Creates a gRPC channel over the test server that presents no credential.</summary>
    /// <returns>The channel. It is disposed with this fixture; the caller need not dispose it.</returns>
    /// <remarks>
    /// Every call on it is refused, because both gRPC services declare <c>RequireAuthorization</c> and the
    /// application's fallback policy would close them even if one did not. The HTTP 401 the challenge
    /// produces surfaces to a gRPC caller as <see cref="StatusCode.Unauthenticated"/>, so an internal edge
    /// is demonstrably authenticated too - constraint C-G covers internal edges exactly as it covers a
    /// public one, because the legacy opened no listening socket at all and every one of these surfaces
    /// was brought into existence by the decomposition itself.
    /// </remarks>
    public GrpcChannel CreateAnonymousGrpcChannel() => CreateGrpcChannel(CreateAnonymousClient());

    /// <summary>Creates a C-03 client over its own authenticated channel.</summary>
    /// <returns>The generated <c>dataservices.v1.DataWindowService</c> client.</returns>
    /// <remarks>
    /// A convenience over <see cref="CreateAuthenticatedGrpcChannel"/> for the common case. A suite that
    /// wants both contract clients on ONE channel creates the channel itself and constructs both from it,
    /// which is what the two channel helpers are for.
    /// </remarks>
    public DataWindowContractClient CreateDataWindowClient() =>
        new(CreateAuthenticatedGrpcChannel());

    /// <summary>Creates a C-04 client over its own authenticated channel.</summary>
    /// <returns>The generated <c>dataservices.v1.ColumnExpressionService</c> client.</returns>
    /// <remarks>
    /// C-04 is a SEPARATE contract from C-03 so the expansion engine can version independently of the
    /// event chain (AAP 0.4.3), and it is reached through a separate client here for the same reason.
    /// </remarks>
    public ColumnExpressionContractClient CreateColumnExpressionClient() =>
        new(CreateAuthenticatedGrpcChannel());

    /// <summary>Wraps one already-created client in a tracked gRPC channel.</summary>
    /// <param name="client">The client to send through. Owned and disposed by the base factory.</param>
    /// <returns>The tracked channel.</returns>
    /// <exception cref="InvalidOperationException">The client carries no base address.</exception>
    /// <remarks>
    /// <c>DisposeHttpClient</c> is left <see langword="false"/> deliberately: the base factory already
    /// tracks and disposes every client it created, and letting the channel dispose it too would be a
    /// second owner for one object. The channel itself is tracked here so that nothing is left holding a
    /// pipeline when the test assembly exits - a leaked host is what makes a test run hang, and a hung run
    /// never writes the coverage report the per-service gate is measured from (constraint C-H).
    /// </remarks>
    private GrpcChannel CreateGrpcChannel(HttpClient client)
    {
        Uri baseAddress = client.BaseAddress
            ?? throw new InvalidOperationException(
                "The test client was created without a base address, so a gRPC channel cannot be "
                + "addressed. WebApplicationFactory supplies one from its own client options, so an "
                + "absent value means those options were replaced rather than extended.");

        GrpcChannel channel = GrpcChannel.ForAddress(
            baseAddress,
            new GrpcChannelOptions
            {
                HttpClient = client,
                DisposeHttpClient = false,
            });

        lock (_channelGate)
        {
            _channels.Add(channel);
        }

        return channel;
    }

    // -------------------------------------------------------------------------------------------------
    //  4. THE HOST CONFIGURATION
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Configures the host under test: the deployed composition root, plus the environment-dependent
    /// addresses it cannot reach and the substitutions for every collaborator that would leave the
    /// process.
    /// </summary>
    /// <param name="builder">The web host builder the factory is populating.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// ============================ WHAT IS OVERRIDDEN, AND WHY IT IS ALLOWED ============================
    /// </para>
    /// <para>
    /// Every setting written below is an ADDRESS, a piece of AUTHENTICATION METADATA, or a RESILIENCE
    /// TUNING VALUE. None of them is behaviour, which is what keeps this inside constraint C-B: a
    /// deployment address describes where this host is running, and this host is not a deployment.
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>Authentication:Jwt:Authority</c> - pointed at a reserved address that resolves nowhere. The
    /// stock handler resolves discovery metadata LAZILY, on first need, and an anonymous request is
    /// refused before any key is needed, so the mandated 401 costs no network call. Nothing here fetches,
    /// caches or pins a key.
    /// </description></item>
    /// <item><description>
    /// <c>Authentication:Jwt:RequireHttpsMetadata</c> - false, and MANDATORY alongside a plain-http
    /// authority: <c>JwtAuthenticationOptionsValidator</c> rejects the contradictory combination on start.
    /// This is a transport requirement for retrieving metadata, NOT a token validation. All four
    /// validations - issuer, audience, lifetime and issuer signing key - stay exactly as the settings
    /// files declare them, because this fixture never writes one of them.
    /// </description></item>
    /// <item><description>
    /// <c>Authentication:Jwt:Audience</c> - a fixed test value, published as
    /// <see cref="ConfiguredAudience"/> so an assertion names it rather than restating it.
    /// </description></item>
    /// <item><description>
    /// <c>Authentication:Jwt:MetadataAddress</c> - <b>deliberately NOT written.</b> Unset means "derive
    /// the discovery address from the authority", which is the normal case; a key written and then left
    /// blank is a distinct, REJECTED state - the validator reports it as a configuration entry started and
    /// not finished. Writing an empty value here would fail startup for a reason nobody asked for.
    /// </description></item>
    /// <item><description>
    /// <c>DataServices:Persistence:Address</c> and <c>DataServices:Security:BaseAddress</c> - loopback
    /// addresses on the ports the deployed manifest gives those services, so a captured request is legible
    /// against the documented port map. The real traffic never leaves the process: it is intercepted by
    /// <see cref="Transport"/>.
    /// </description></item>
    /// <item><description>
    /// <c>DataServices:Resilience:{Persistence,Security}:MaxRetryAttempts</c> and
    /// <c>:RetryBaseDelay</c> - reduced so a deliberately failing upstream test finishes promptly, and
    /// <b>not</b> disabled. See <see cref="ReducedRetryAttempts"/> for why one rather than zero, and why
    /// the circuit-breaker values are left at the deployed settings.
    /// </description></item>
    /// </list>
    /// <para>
    /// ================== WHAT IS DELIBERATELY NOT OVERRIDDEN - THE PRESERVED DEFAULTS ==================
    /// </para>
    /// <para>
    /// Each key below carries a PRESERVED LEGACY DEFAULT whose VALUE IS BEHAVIOUR. Overriding any of them
    /// from a shared fixture would be the silent correction constraint C-B forbids, and it would make
    /// <c>DataServicesOptionsTests.cs</c>'s assertions about those defaults vacuous - a test that passes
    /// because a fixture wrote the value it was checking for is not evidence of anything. A suite that
    /// genuinely needs a different value writes it into <see cref="AdditionalSettings"/> instead.
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>DataServices:Localization:Locale</c> = <c>"en"</c>. The hardcoded locale at
    /// <c>ws_objects/pfw.pbl.src/pfw.sra:L94</c>. The DEFECT is the hardcoding, not the value, so the
    /// value survives as the configured default while the un-configurability is gone.
    /// </description></item>
    /// <item><description>
    /// <c>DataServices:RowSelect:Style</c> = single selection. The legacy setter rejects zero outright
    /// [<c>n_cst_dwsvc_rowselect.sru:L169</c>], and the validator reproduces that refusal.
    /// </description></item>
    /// <item><description>
    /// The five <c>DataServices:ContextMenu</c> feature toggles, all true
    /// [<c>n_cst_dwsvc_contextmenu.sru:L49-L53</c>]. Their whole domain is legal, which is why the
    /// validator declines to check them - and why a fixture must not quietly move one.
    /// </description></item>
    /// <item><description>
    /// <c>DataServices:ColumnExpression:Trace</c> = false, <c>:RedrawSuppressionRowThreshold</c> = 200 and
    /// <c>:CalcStackInitialCapacity</c> = 20. The third is the initial capacity of the calculation and
    /// recursion stack the vector container holds
    /// [<c>n_cst_dwsvc_columnexp.sru:L110</c>, created at <c>:L2421</c>].
    /// </description></item>
    /// <item><description>
    /// <c>DataServices:DropDownSearch:FilterType</c> = 3, which is display plus display-pinyin and
    /// DELIBERATELY not the all-fields value; <c>:ShowFilteredRows</c> = null, the THREE-STATE
    /// auto-determine default set at <c>n_cst_dwsvc_dropdownsearch.sru:L503</c> by
    /// <c>SetNull(#ShowFilteredRows)</c> and read by the three-branch resolution at <c>:L260-L263</c>;
    /// and <c>:PinyinMatchFlags</c>, the combinable mask the sole call site passes at <c>:L323</c>.
    /// Coercing that null to false would delete an algorithm, not tidy a value.
    /// </description></item>
    /// </list>
    /// <para>
    /// ============================== WHAT IS SUBSTITUTED IN THE CONTAINER ==============================
    /// </para>
    /// <para>
    /// <c>ConfigureTestServices</c> runs AFTER the composition root, so each substitution below amends a
    /// fully-composed container rather than pre-empting it: the deployed registration runs first and is
    /// then replaced by name, which is why every one of them is preceded by <c>RemoveAll</c>. The
    /// authentication registration is the one exception in shape - it ADDS two schemes and re-points the
    /// default, and removes nothing, so the stock bearer scheme survives untouched underneath it.
    /// </para>
    /// </remarks>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(_environmentName);

        // --- The authentication endpoints. Metadata and addresses only; no validation switch. ---------
        builder.UseSetting(
            Key(JwtAuthenticationOptions.SectionName, nameof(JwtAuthenticationOptions.Authority)),
            UnreachableAuthority);
        builder.UseSetting(
            Key(JwtAuthenticationOptions.SectionName, nameof(JwtAuthenticationOptions.Audience)),
            TestAudience);
        builder.UseSetting(
            Key(
                JwtAuthenticationOptions.SectionName,
                nameof(JwtAuthenticationOptions.RequireHttpsMetadata)),
            false.ToString(CultureInfo.InvariantCulture));

        // --- The two upstream addresses. Intercepted before anything leaves the process. --------------
        builder.UseSetting(
            Key(
                DataServicesOptions.SectionName,
                nameof(DataServicesOptions.Persistence),
                nameof(PersistenceClientOptions.Address)),
            LoopbackPersistenceAddress);
        builder.UseSetting(
            Key(
                DataServicesOptions.SectionName,
                nameof(DataServicesOptions.Security),
                nameof(SecurityClientOptions.BaseAddress)),
            LoopbackSecurityAddress);

        // --- The issuance credential, so the host can start at all. -----------------------------------
        //
        // A FLAT KEY, NOT A SECTIONED ONE, and the spelling matters. The environment-variable provider
        // maps only a double underscore onto the ':' separator, so this name is a top-level configuration
        // key rather than a path into `DataServices` - which is exactly why the composition root reads it
        // with an explicit post-configure step instead of binding it. `UseSetting` writes into the same
        // host configuration that step reads, so this is the production resolution path and not a
        // test-only door.
        builder.UseSetting(SecurityClientOptions.ClientSecretConfigurationKey, IssuanceSecret);

        // --- Resilience, reduced on both edges and disabled on neither. -------------------------------
        foreach (string edge in ResilienceEdgeNames)
        {
            builder.UseSetting(
                Key(
                    DataServicesOptions.SectionName,
                    nameof(DataServicesOptions.Resilience),
                    edge,
                    nameof(ClientResilienceOptions.MaxRetryAttempts)),
                ReducedRetryAttempts);
            builder.UseSetting(
                Key(
                    DataServicesOptions.SectionName,
                    nameof(DataServicesOptions.Resilience),
                    edge,
                    nameof(ClientResilienceOptions.RetryBaseDelay)),
                ReducedRetryDelay);
        }

        // --- The per-test opt-in, applied last so a suite's own value wins over the fixture's. --------
        foreach (KeyValuePair<string, string?> setting in AdditionalSettings)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }

        builder.ConfigureTestServices(ConfigureTestServices);
    }

    /// <summary>The two configured resilience groups, named from the options type.</summary>
    /// <remarks>
    /// Read from <c>nameof</c> rather than written as strings so the loop above cannot address a group the
    /// options type does not declare. Both edges get the same reduction because both are network edges the
    /// decomposition created; they remain SEPARATE groups so a suite can still tune them apart.
    /// </remarks>
    private static ImmutableArray<string> ResilienceEdgeNames { get; } =
    [
        nameof(ResilienceOptions.Persistence),
        nameof(ResilienceOptions.Security),
    ];

    /// <summary>Joins configuration path segments with the binder's separator.</summary>
    /// <param name="segments">The segments, outermost first.</param>
    /// <returns>The full configuration path.</returns>
    /// <remarks>
    /// Present so that every key written above is composed from <c>nameof</c> and published section-name
    /// constants rather than from a hand-typed path. A mistyped path is the single easiest way for a test
    /// fixture to silently configure nothing at all, and this removes the opportunity.
    /// </remarks>
    private static string Key(params string[] segments) =>
        string.Join(':', segments);

    /// <summary>
    /// Applies every container substitution, in the order a reader should meet them.
    /// </summary>
    /// <param name="services">The fully-composed container, ready to be amended.</param>
    private void ConfigureTestServices(IServiceCollection services)
    {
        AddSchemeSelector(services);
        SubstituteDeterminismSeam(services);
        SubstituteSecurityEdge(services);
        SubstitutePersistenceEdge(services);
        SubstituteOutboundTransport(services);
        ApplyOptionalSubstitutions(services);

        // APPLIED LAST, so a suite's bespoke registration wins over every default above. See the property's
        // own remarks for the one thing it may not be used for.
        foreach (Action<IServiceCollection> configure in AdditionalServiceConfiguration)
        {
            configure(services);
        }
    }

    /// <summary>
    /// Adds the forwarding policy scheme and the test principal scheme, and re-points the default.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <remarks>
    /// <para>
    /// NOTHING IS REMOVED HERE, WHICH IS THE WHOLE DESIGN. The stock bearer scheme the composition root
    /// registered survives exactly as configured, and this method only ADDS two schemes and changes which
    /// one the default resolves to. <c>services.Configure&lt;AuthenticationOptions&gt;</c> calls run in
    /// registration order and this one runs after the application's, so the new default wins without any
    /// removal - and <c>JwtBearerOptions</c> is never read, written or post-configured by this file.
    /// </para>
    /// <para>
    /// THE SELECTOR IS THE ONLY DECISION IT MAKES: header present means Posture B, header absent means the
    /// deployed bearer handler. It never answers "allow anonymously", it registers no fallback, and it does
    /// not consult the environment - so there is no development bypass to be found in it, in any
    /// environment (constraint C-G).
    /// </para>
    /// <para>
    /// <c>ForwardDefaultSelector</c> covers authenticate, challenge, forbid, sign-in and sign-out
    /// together, which is what keeps the two postures coherent: a Posture A request is challenged by the
    /// handler that also refused it, so the 401 and its <c>WWW-Authenticate</c> are the deployed
    /// handler's own output rather than a reconstruction of it.
    /// </para>
    /// </remarks>
    private static void AddSchemeSelector(IServiceCollection services) =>
        services
            .AddAuthentication(SchemeSelectorName)
            .AddPolicyScheme(
                SchemeSelectorName,
                displayName: SchemeSelectorName,
                configureOptions: static options => options.ForwardDefaultSelector =
                    static context => context is not null
                        && context.Request.Headers.ContainsKey(TestPrincipalHeaderName)
                            ? TestPrincipalSchemeName
                            : JwtBearerDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, TestPrincipalAuthenticationHandler>(
                TestPrincipalSchemeName,
                displayName: TestPrincipalSchemeName,

                // Null rather than an empty delegate: AuthenticationSchemeOptions carries nothing this
                // handler reads, so there is genuinely nothing to configure, and an empty lambda would
                // suggest otherwise.
                configureOptions: null);

    /// <summary>Substitutes the clock, so every timestamp the host emits is fixed.</summary>
    /// <param name="services">The container.</param>
    /// <remarks>
    /// The composition root registers <c>TimeProvider.System</c> as the single seam every component reads,
    /// which is what makes one replacement sufficient. It is a requirement of the parity model rather than
    /// a convenience: non-deterministic values must be masked from BOTH the master and the candidate
    /// recording, and a clock read is the commonest such value (AAP 0.6.7).
    /// </remarks>
    private void SubstituteDeterminismSeam(IServiceCollection services)
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(_clock);
    }

    /// <summary>Substitutes the token seam on the Security edge.</summary>
    /// <param name="services">The container.</param>
    /// <remarks>
    /// <para>
    /// THE NARROWEST AVAILABLE SEAM, WHICH IS WHY IT IS PREFERRED OVER THE TRANSPORT HERE.
    /// <c>Clients/SecurityClient.cs</c> publishes <c>IServiceTokenProvider</c> for exactly this purpose, so
    /// substituting it removes the need to script a token route, frame a response body, or model contract
    /// C-01's wire shape at all. It also leaves <c>ICryptoServiceClient</c> resolving to the REAL
    /// <c>SecurityClient</c>, so a crypto assertion still runs the shipped client code - over the
    /// substituted transport, and therefore without a network.
    /// </para>
    /// <para>
    /// Registered as a SINGLETON where the application registers a transient, deliberately: the point of a
    /// recording double is that every request adds to ONE record, and a transient would hand each caller a
    /// fresh, empty one. The provider holds no per-request state, so the lifetime widening is safe.
    /// </para>
    /// <para>
    /// WHAT THIS SUBSTITUTION DOES NOT COVER, MEASURED RATHER THAN ASSUMED. It supplies the credential for
    /// callers that ASK the container for one - which is <c>PersistenceClient</c>, on every outbound C-05
    /// to C-08 call. It does NOT redirect <c>SecurityClient</c>'s own C-02 calls, because that client IS an
    /// <see cref="IServiceTokenProvider"/> and acquires its credential through its own C-01 operation
    /// rather than through the container. So a suite exercising the crypto surface must ALSO script
    /// <c>POST /v1/tokens</c> on <see cref="Transport"/>, or the first crypto call fails with the
    /// recorder's own "no scripted route matched" diagnostic naming that path. Verified by running it.
    /// </para>
    /// <para>
    /// NO TOKEN-SHAPED BODY IS SUPPLIED HERE FOR THAT PURPOSE, and that omission is deliberate rather than
    /// an oversight: a suite that needs one composes it at run time from
    /// <see cref="Random"/>'s placeholder generator, which is exactly what constraint C-F asks for -
    /// generate locally, never persist. A convenience helper on this fixture would put a token-shaped
    /// literal into the assembly for the benefit of a case that may never be written.
    /// </para>
    /// </remarks>
    private void SubstituteSecurityEdge(IServiceCollection services)
    {
        services.RemoveAll<IServiceTokenProvider>();
        services.AddSingleton<IServiceTokenProvider>(_tokenProvider);
    }

    /// <summary>Substitutes the Persistence client with the derived, scriptable double.</summary>
    /// <param name="services">The container.</param>
    /// <remarks>
    /// <para>
    /// A DERIVED DOUBLE RATHER THAN A PROTOCOL SCRIPT. <c>PersistenceClient</c> is public and its
    /// operations are <c>virtual</c> so that a test can substitute exactly this way, and the alternative -
    /// scripting the four gRPC method paths on the transport - would mean hand-framing protobuf bodies and
    /// a rich-error trailer to assert something about C-03's projection rather than about the framing.
    /// </para>
    /// <para>
    /// THE FOUR GENERATED CLIENTS ARE RESOLVED FROM THE CONTAINER, not fabricated. That keeps the deployed
    /// <c>AddGrpcClient</c> registrations - their address, their resilience pipeline and their handler
    /// chain - genuinely in the graph, so an operation the double does not override still goes through the
    /// real client, reaches the substituted transport, and fails LOUDLY with a message naming the
    /// unmatched route. That is the correct default: a suite that calls an unscripted operation learns it
    /// immediately instead of receiving a plausible empty answer.
    /// </para>
    /// <para>
    /// SCOPED, matching the application's own lifetime for this type, so the client's logger scope and its
    /// resolved gRPC clients stay aligned with the call using them. The SCRIPT itself lives on
    /// <see cref="PersistenceEdge"/>, which is created with the fixture - so a suite can script a conflict
    /// BEFORE the host has ever been started, without the fixture having to boot a host to hand out a
    /// double.
    /// </para>
    /// </remarks>
    private void SubstitutePersistenceEdge(IServiceCollection services)
    {
        services.RemoveAll<PersistenceClient>();
        services.AddScoped<PersistenceClient>(serviceProvider => new ScriptedPersistenceClient(
            _persistenceEdge,
            serviceProvider.GetRequiredService<PersistenceQueryClient>(),
            serviceProvider.GetRequiredService<PersistenceUpdateClient>(),
            serviceProvider.GetRequiredService<PersistenceCommandClient>(),
            serviceProvider.GetRequiredService<PersistenceTransactionClient>(),
            serviceProvider.GetRequiredService<IServiceTokenProvider>(),
            serviceProvider.GetRequiredService<ILogger<PersistenceClient>>()));
    }

    /// <summary>Replaces the primary message handler of every factory-created client.</summary>
    /// <param name="services">The container.</param>
    /// <remarks>
    /// <para>
    /// ONE REGISTRATION, BOTH EDGES, AND NO MOCKING PACKAGE. The Security typed client and all four
    /// Persistence gRPC clients obtain their primary handler through <c>IHttpClientFactory</c>, so
    /// configuring the factory-wide default intercepts every one of them. This is the structural half of
    /// "no network": it holds even for a client a later edit adds without telling this fixture.
    /// </para>
    /// <para>
    /// THE SHARED RECORDER IS WRAPPED RATHER THAN HANDED OVER. The factory owns and eventually disposes
    /// whatever the primary-handler delegate returns, so returning the shared recorder directly would let
    /// a handler-lifetime expiry dispose an object this fixture still owns and other suites still read.
    /// <see cref="SharedTransportHandler"/> is a per-client shell around it that the factory may dispose
    /// freely.
    /// </para>
    /// </remarks>
    private void SubstituteOutboundTransport(IServiceCollection services) =>
        services.ConfigureHttpClientDefaults(clients =>
            clients.ConfigurePrimaryHttpMessageHandler(() => new SharedTransportHandler(_transport)));

    /// <summary>Applies the three substitutions that are off unless a suite asks for them.</summary>
    /// <param name="services">The container.</param>
    /// <remarks>
    /// Each of the three would change observable behaviour if it were on by default, and each property's
    /// own remarks record exactly what that change would be. They are grouped here so a reader can see at
    /// one glance everything the fixture does NOT do to a host by default.
    /// </remarks>
    private void ApplyOptionalSubstitutions(IServiceCollection services)
    {
        if (SubstitutesLocalizationProvider)
        {
            services.RemoveAll<II18nProvider>();
            services.AddSingleton<II18nProvider>(_localization);
        }

        if (SubstitutesExpressionTraceSink)
        {
            services.RemoveAll<IExpressionTraceSink>();
            services.AddSingleton<IExpressionTraceSink>(_traceSink);
        }

        if (!BindsDataWindowHost)
        {
            return;
        }

        services.RemoveAll<IDataWindowHostFactory>();
        services.AddSingleton<IDataWindowHostFactory>(_hostBinding);

        // The model-set provider is built by a factory because it needs four collaborators the container
        // owns: the bound options the four models read, the localization facade the context-menu and
        // row-select models route their diagnostics through, the pinyin matcher in its shipped BLOCKED
        // state, and the configured `for page` resolver. Constructing them by hand instead would quietly
        // substitute a second set of decisions for the composition root's.
        services.RemoveAll<IDataWindowModelSetProvider>();
        services.AddSingleton<IDataWindowModelSetProvider>(serviceProvider =>
            new BoundDataWindowModelSetProvider(
                _hostBinding,
                serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>(),
                serviceProvider.GetRequiredService<I18n>(),
                serviceProvider.GetRequiredService<PinyinFirstLetterMatcher>(),
                serviceProvider.GetRequiredService<IExpressionPageResolver>()));
    }

    /// <summary>
    /// Resolves the RETAINED headless host this fixture serves for one data-object name, so a suite can
    /// populate the DataWindow the published surface will then operate over.
    /// </summary>
    /// <param name="dataWindowName">The registered data-object name, for example the primary fixture.</param>
    /// <returns>The retained host, or <see langword="null"/> when this fixture declines the name.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="dataWindowName"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THIS IS THE SEAM THAT REPLACES A PUBLISHED ROW-INGESTION OPERATION, and the substitution is the
    /// contract rather than a convenience. Neither C-03 nor C-04 publishes an operation that materializes a
    /// row into a DataWindow: the ported surface never had one, because in process the application owned
    /// the DataWindow and the five attached services operated over whatever it already held. A suite that
    /// needs rows therefore arranges them the same way the application does - directly on the host - and
    /// then drives the published surface over them.
    /// </para>
    /// <para>
    /// IT ANSWERS THE SAME INSTANCE THE SERVICES RESOLVE, because the binding is retentive by name. That is
    /// what makes a row seeded here visible to C-03's headless models and to a C-04 expression session over
    /// the same name, which is precisely the composition the two contracts rely on.
    /// </para>
    /// </remarks>
    internal DataWindowServiceHost BindDataWindowHost(string dataWindowName)
    {
        ArgumentNullException.ThrowIfNull(dataWindowName);

        // RESOLVED FROM THE RUNNING HOST'S OWN CONTAINER rather than from a field, so this answers whichever
        // factory the fixture actually registered - the deployed one by default, and the bound double when
        // BindsDataWindowHost is set. A field read would have quietly answered the double even in the
        // configuration that does not install it.
        IDataWindowHostFactory factory = Services.GetRequiredService<IDataWindowHostFactory>();

        return factory.Create(dataWindowName)
            ?? throw new InvalidOperationException(
                "The registered DataWindow host factory declined the data-object name '"
                    + dataWindowName
                    + "'. A suite seeding rows must name a DataWindow the catalogue carries; a declined "
                    + "name is a setup fault rather than an empty DataWindow.");
    }

    // -------------------------------------------------------------------------------------------------
    //  5. DISPOSAL
    // -------------------------------------------------------------------------------------------------

    /// <summary>Releases the host and everything this fixture created around it.</summary>
    /// <param name="disposing">Whether managed state should be released.</param>
    /// <remarks>
    /// The channels are released BEFORE the base disposes the host and the clients they send through, so
    /// nothing is torn down underneath something still holding it. A LEAKED HOST IS NOT A COSMETIC
    /// PROBLEM: it makes <c>dotnet test</c> hang, and a hung run never writes the coverage report the
    /// per-service 80 percent line gate is measured from (constraint C-H).
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseFixtureOwnedResources();
        }

        base.Dispose(disposing);
    }

    /// <summary>Releases the host asynchronously and everything this fixture created around it.</summary>
    /// <returns>A task that completes when the host has been released.</returns>
    /// <remarks>
    /// Overridden as well as <see cref="Dispose(bool)"/> so that a consumer using <c>await using</c> and
    /// one using <c>using</c> get identical cleanup. Both converge on one guarded release, so neither can
    /// double-release and neither can skip it.
    /// </remarks>
    public override async ValueTask DisposeAsync()
    {
        ReleaseFixtureOwnedResources();

        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Releases the channels and the shared transport exactly once.</summary>
    private void ReleaseFixtureOwnedResources()
    {
        List<GrpcChannel> channels;

        lock (_channelGate)
        {
            if (_released)
            {
                return;
            }

            _released = true;
            channels = [.. _channels];
            _channels.Clear();
        }

        foreach (GrpcChannel channel in channels)
        {
            channel.Dispose();
        }

        // Disposed here rather than by the client factory, because this fixture created it and hands out
        // only shells around it - see SubstituteOutboundTransport.
        _transport.Dispose();
    }
}

// -----------------------------------------------------------------------------------------------------
//  6. POSTURE B'S HANDLER - AN AUTHENTICATED PRINCIPAL WITH NO CREDENTIAL ANYWHERE
// -----------------------------------------------------------------------------------------------------

/// <summary>
/// Supplies <b>Posture B</b>'s principal: the scheme
/// <see cref="DataServicesTestHostFactory.TestPrincipalSchemeName"/> forwards to when
/// <see cref="DataServicesTestHostFactory.TestPrincipalHeaderName"/> is present.
/// </summary>
/// <param name="options">The scheme options monitor supplied by the authentication stack.</param>
/// <param name="logger">The logger factory supplied by the authentication stack.</param>
/// <param name="encoder">The URL encoder supplied by the authentication stack.</param>
/// <remarks>
/// <para>
/// THIS TYPE IS THE REASON NO SIGNING KEY EXISTS IN THIS ASSEMBLY. It answers with a principal directly,
/// so nothing has to mint, sign, encode or verify a credential in order to prove that an authenticated
/// caller is accepted. That keeps the sole-issuer invariant structurally true rather than merely
/// observed: Security holds the one signing secret in the system, DataServices holds verification
/// material only, and this file introduces no third authority (AAP 0.6.6.3, constraints C-F and C-G).
/// The anti-pattern being replaced is <c>tests/blink/test_jws.htm:L8-L23</c>, which hardcodes a plaintext
/// PEM RSA private key and signs a JWS with it; nothing from it appears here in any form.
/// </para>
/// <para>
/// IT DOES NOT WEAKEN ANYTHING. It is reachable only when the fixture's own header is present, it never
/// answers on the bearer scheme's behalf, and it is registered nowhere except in
/// <c>ConfigureTestServices</c> - so it cannot exist in a deployed host at all. An anonymous request
/// never reaches it: the selector forwards that case to the deployed bearer handler, which is what makes
/// the mandated 401 the deployed pipeline's own answer.
/// </para>
/// <para>
/// THE 3-PARAMETER BASE CONSTRUCTOR IS THE CURRENT ONE. The 4-parameter overload taking a clock is
/// obsolete, and using it would be a warning - which is an error here, because warnings are errors
/// repository wide.
/// </para>
/// </remarks>
internal sealed class TestPrincipalAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>Produces the principal, or no result when the selecting header is absent.</summary>
    /// <returns>The authentication outcome.</returns>
    /// <remarks>
    /// <para>
    /// THE ABSENT-HEADER BRANCH IS A REAL DEFENSIVE PATH, NOT DEAD CODE. The selector should never route a
    /// header-less request here, and if a future edit registers this scheme somewhere else it must not
    /// silently authenticate one. <c>NoResult</c> - rather than <c>Fail</c> - is the correct answer for
    /// "this handler has nothing to say", because failing would suppress any other scheme's verdict.
    /// </para>
    /// <para>
    /// THE CLAIMS ARE EXACTLY WHAT THE APPLICATION'S POLICIES NEED AND NOTHING MORE. The ping route applies
    /// the parameterless <c>RequireAuthorization</c> and reads no claim; both gRPC services and the
    /// projected REST groups name a policy that requires a PERMITTED CALLER IDENTITY and the operation's
    /// SCOPE, so the subject and the scope set are supplied and are overridable per request. A name claim
    /// is supplied so a diagnostic has something legible to print, and the audience claim so the principal
    /// is consistent with the audience the host is configured for. NONE OF THEM IS A SECRET - a scope is a
    /// permission name, not a credential - and no key, token or signature value appears in any of them.
    /// </para>
    /// <para>
    /// The identity names this scheme as its authentication type, which is what makes
    /// <c>IIdentity.IsAuthenticated</c> answer true - an identity with no authentication type is NOT
    /// authenticated, and getting that wrong would make Posture B silently behave like Posture A.
    /// </para>
    /// </remarks>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey(DataServicesTestHostFactory.TestPrincipalHeaderName))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // THE SUBJECT AND THE SCOPE SET ARE OVERRIDABLE PER REQUEST. Both contracts now require the caller
        // to be a configured permitted identity AND to hold the operation's scope, so a row proving a
        // refusal has to be able to vary either without disturbing another row. Absent headers yield the
        // defaults, which are Gateway's identity and both published scopes - the credential Gateway
        // actually obtains.
        string subject =
            Request.Headers[DataServicesTestHostFactory.TestPrincipalSubjectHeaderName].ToString() is
                { Length: > 0 } declaredSubject
                ? declaredSubject
                : DataServicesTestHostFactory.TestPrincipalSubject;

        // An EMPTY header value is honoured as an empty scope set rather than replaced by the default,
        // because "holds no scope" is a state a row must be able to produce. Only an ABSENT header defaults.
        string scopes =
            Request.Headers.TryGetValue(
                DataServicesTestHostFactory.TestPrincipalScopeHeaderName,
                out Microsoft.Extensions.Primitives.StringValues declaredScopes)
                ? declaredScopes.ToString()
                : DataServicesTestHostFactory.TestPrincipalScopes;

        ClaimsIdentity identity = new(
            [
                // BOTH SPELLINGS OF THE SUBJECT, because the receiver's policy reads whichever is present:
                // a bearer handler with inbound claim mapping on renames `sub` to the name-identifier claim
                // type, and with it off leaves `sub` alone. Supplying both means the fixture does not
                // silently depend on which mapping the host happens to use.
                new Claim("sub", subject),
                new Claim(ClaimTypes.NameIdentifier, subject),
                new Claim(ClaimTypes.Name, subject),
                new Claim("aud", DataServicesTestHostFactory.ConfiguredAudience),
                new Claim("scope", scopes),
            ],
            Scheme.Name);

        return Task.FromResult(
            AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

// -----------------------------------------------------------------------------------------------------
//  7. THE TWO TRANSPORT SHELLS
// -----------------------------------------------------------------------------------------------------

/// <summary>
/// Copies the request's HTTP version onto the response, so that gRPC calls can run over the test server.
/// </summary>
/// <remarks>
/// <para>
/// LOAD BEARING FOR EVERY gRPC HELPER ON THE FIXTURE, AND INERT FOR EVERYTHING ELSE. The test host maps a
/// request whose version is 2.0 onto the <c>HTTP/2</c> request protocol, which is what lets the ASP.NET
/// Core gRPC layer accept the call at all - it refuses a non-HTTP/2 protocol outright. The RESPONSE,
/// however, comes back reporting 1.1, and <c>Grpc.Net.Client</c> treats that as a protocol downgrade and
/// abandons the call. Restating the request's version on the response is the documented remedy.
/// </para>
/// <para>
/// For an ordinary HTTP/1.1 REST call this is an assignment of 1.1 over 1.1, so the fixture installs it on
/// every client it creates rather than maintaining two creation paths that could drift apart.
/// </para>
/// </remarks>
internal sealed class TestServerResponseVersionHandler : DelegatingHandler
{
    /// <summary>Sends the request and restates its version on the response.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>The response, reporting the request's own version.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        HttpResponseMessage response = await base
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        response.Version = request.Version;

        return response;
    }
}

/// <summary>
/// A disposable per-client shell around the fixture's single shared recording transport.
/// </summary>
/// <param name="shared">The shared recorder every shell forwards to.</param>
/// <remarks>
/// <para>
/// IT EXISTS TO SETTLE OWNERSHIP, NOT TO ADD BEHAVIOUR. <c>IHttpClientFactory</c> owns whatever the
/// primary-handler delegate returns and disposes it once the handler's lifetime lapses. Handing it the
/// shared recorder directly would therefore let one client's expiry dispose an object the fixture still
/// owns and other suites are still reading, producing a failure with no relationship to the code under
/// test. Each client gets its own shell instead, and the factory may dispose those as freely as it likes.
/// </para>
/// <para>
/// <c>Dispose</c> is overridden to release NOTHING and deliberately does not chain to the base, because the
/// base's contract for a delegating handler is to dispose its inner handler - which is exactly the shared
/// recorder this type exists to protect. The shell holds no other resource, so releasing nothing is
/// complete rather than partial. The fixture disposes the recorder itself, exactly once.
/// </para>
/// </remarks>
internal sealed class SharedTransportHandler(HttpMessageHandler shared) : DelegatingHandler(shared)
{
    /// <summary>Releases nothing, so that the shared inner recorder survives this shell.</summary>
    /// <param name="disposing">Ignored; this shell owns no resource of its own.</param>
    protected override void Dispose(bool disposing)
    {
        // Intentionally empty, and intentionally not chaining. See the type's remarks: chaining would
        // dispose the shared recorder, which the fixture owns and disposes once for the whole run.
    }
}

// -----------------------------------------------------------------------------------------------------
//  8. THE PERSISTENCE EDGE - SCRIPT AND OBSERVATION, SEPARATED FROM THE CLIENT THAT SERVES THEM
// -----------------------------------------------------------------------------------------------------

/// <summary>
/// The script a suite writes and the record it reads for the Persistence edge, contracts C-05 to C-08.
/// </summary>
/// <remarks>
/// <para>
/// SEPARATE FROM THE CLIENT ON PURPOSE. The client is SCOPED, so the container builds one per request and
/// a suite could never hold on to it; the script has to outlive every request and be writable BEFORE the
/// host has started at all. Splitting them is what lets
/// <c>fixture.PersistenceEdge.ScriptUpdateConflict(...)</c> be the first line of a test rather than
/// something that has to boot a host to reach a double.
/// </para>
/// <para>
/// EIGHT OPERATIONS ARE SCRIPTED: the streamed retrieval, the update, and the SIX HANDLE-LIFECYCLE calls
/// the two of them now make. The lifecycle six are scripted rather than left to the real implementation
/// because the real one reaches the substituted transport, so a retrieval would spend the resilience
/// pipeline's whole retry budget failing to begin a session before it ever asked for a row. Everything
/// else on the client is still left to the real implementation, which fails LOUDLY - the honest default,
/// because a loud failure names the unscripted route and the fix, whereas a fabricated answer would let a
/// suite assert against a fiction.
/// </para>
/// <para>
/// THE LIFECYCLE HALF RECORDS RATHER THAN MERELY ANSWERS, and that is the point of it. It keeps the set of
/// handles it has issued and not yet seen released, so a suite can assert that a request RELEASED WHAT IT
/// TOOK - including on the conflict path and the cancellation path, where a leak is invisible to every
/// assertion about the response. A release naming a handle it never issued, or naming one twice, answers
/// <c>E_INVALID_HANDLE</c> exactly as the server's registries do, so a double release cannot pass as a
/// clean one.
/// </para>
/// <para>
/// ⚠ AND THE SAME RULE APPLIES TO THE TWO OPERATIONS THAT *ARE* SCRIPTED: NEITHER HAS A DEFAULT ANSWER ⚠
/// </para>
/// <para>
/// An earlier form of this double answered an unscripted <c>Query</c> with a completely empty stream and an
/// unscripted <c>Update</c> with a bare success carrying zero counts. Both are PLAUSIBLE, which is exactly
/// what makes them dangerous: a real C-05 zero-row retrieval is not an empty stream but a three-part one -
/// the row count, then the chunks, then the terminal status - and a real C-06 success reports the counts and
/// the identity round trip. A test that forgot to script therefore did not fail; it passed against a shape
/// the contract never produces, and it went on passing after the production path stopped producing anything
/// at all.
/// </para>
/// <para>
/// So an unscripted call THROWS, naming the operation and the scripting method that fixes it. A zero-row
/// retrieval and a zero-count success are both still reachable - through
/// <see cref="ScriptEmptyQuery"/> and <see cref="ScriptUpdateSuccess"/> - but only by asking for them,
/// which is the difference between a suite asserting an outcome and a suite inheriting one.
/// </para>
/// </remarks>
internal sealed class ScriptedPersistenceEdge
{
    /// <summary>Whether a retrieval has been scripted at all.</summary>
    /// <remarks>
    /// Tracked separately from the message list's emptiness, because "no script" and "a script whose
    /// content happens to be nothing" are different statements and only the first is an error. A suite can
    /// legitimately script an empty message list - <see cref="ScriptQueryMessages"/> permits it - and that
    /// models a transport that closed the stream with no messages at all, which is a real fault worth being
    /// able to reproduce.
    /// </remarks>
    private bool _queryScripted;

    /// <summary>The response <c>Update</c> answers with, or <see langword="null"/> when none is scripted.</summary>
    private PersistenceUpdateResponse? _updateResponse;

    /// <summary>Guards the lifecycle counters and the held-handle sets.</summary>
    /// <remarks>
    /// GENUINELY NEEDED RATHER THAN DEFENSIVE. A scope's release runs from <c>DisposeAsync</c>, which the
    /// host may complete on a different thread from the one that acquired it, and a cancelled retrieval
    /// releases while its stream is still unwinding. A <see cref="HashSet{T}"/> mutated from two threads
    /// can corrupt its buckets and then answer wrongly rather than throw, which would make a leak
    /// assertion pass for the wrong reason.
    /// </remarks>
    private readonly Lock _lifecycleGate = new();

    /// <summary>Sessions issued and not yet ended, in issue order.</summary>
    private readonly List<string> _heldSessions = [];

    /// <summary>Query tasks issued and not yet released, in issue order.</summary>
    private readonly List<string> _heldQueryTasks = [];

    /// <summary>Update tasks issued and not yet released, in issue order.</summary>
    private readonly List<string> _heldUpdateTasks = [];

    /// <summary>Every release, in the order it arrived, as a coarse kind.</summary>
    /// <remarks>
    /// THE ORDER IS A CONTRACT, NOT AN INCIDENTAL. A task borrows the pooled transaction its session owns,
    /// so releasing the session first would leave the task holding a reference to something already
    /// collected - and a count-only assertion cannot tell the two orders apart.
    /// </remarks>
    private readonly List<string> _releaseOrder = [];

    /// <summary>The source of the identifiers this edge issues.</summary>
    /// <remarks>
    /// MONOTONIC AND NEVER REUSED, so a stale handle from an earlier request can never be mistaken for a
    /// live one - which is exactly the property the server's own registries have, and the reason a double
    /// release is detectable at all.
    /// </remarks>
    private long _nextHandle;

    /// <summary>
    /// The messages <c>Query</c> answers with, in delivery order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Writing to this list directly is supported and marks the retrieval as scripted, so a suite that needs
    /// a shape none of the helpers builds can compose one message at a time. Reading it before anything has
    /// been scripted yields an empty list; that does NOT mean an unscripted <c>Query</c> answers with
    /// nothing - it throws. See <see cref="IsQueryScripted"/>.
    /// </para>
    /// <para>
    /// A suite that wants the ordinary shape calls <see cref="ScriptQuery(long, int)"/>, which builds the
    /// three-part form the contract publishes: the row count, then the chunks, then the terminal status.
    /// </para>
    /// </remarks>
    public IList<PersistenceQueryResponse> QueryScript { get; } = [];

    /// <summary>Whether a retrieval answer has been scripted.</summary>
    /// <remarks>
    /// Exposed so a suite can assert its own arrangement rather than discovering a missing script through a
    /// failure - useful in a shared class fixture, where one test's <see cref="Reset"/> is the other's
    /// missing arrangement.
    /// </remarks>
    public bool IsQueryScripted => _queryScripted || QueryScript.Count > 0;

    /// <summary>Whether an update answer or an update conflict has been scripted.</summary>
    public bool IsUpdateScripted => _updateResponse is not null || UpdateConflict is not null;

    /// <summary>
    /// The response <c>Update</c> answers with when no conflict is scripted.
    /// </summary>
    /// <remarks>
    /// NO DEFAULT, DELIBERATELY - see the type's remarks. Reading it before anything is scripted throws
    /// rather than inventing a neutral success, because a neutral success is the answer that lets a
    /// forgotten arrangement pass. <see cref="ScriptUpdateSuccess"/> is the explicit way to a zero-count
    /// success, and the setter accepts any response a suite composes itself.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No update answer has been scripted.</exception>
    public PersistenceUpdateResponse UpdateResponse
    {
        get => _updateResponse ?? throw Unscripted(
            "Update",
            "ScriptUpdateSuccess(...), ScriptUpdateConflict(...), or the UpdateResponse setter");
        set => _updateResponse = value;
    }

    /// <summary>
    /// The failure <c>Update</c> throws instead of answering, or <see langword="null"/> to answer.
    /// </summary>
    /// <remarks>
    /// The real client raises exactly this type after decoding the contract's rich-error trailer, so a
    /// double that threw a bare <c>RpcException</c> would be modelling a DIFFERENT path - the undecodable
    /// one, which is real and separate. Use <see cref="ScriptUpdateConflict(long, long, ConflictRow[])"/>
    /// to reach the decoded path.
    /// </remarks>
    public PersistenceConflictException? UpdateConflict { get; set; }

    /// <summary>How many times <c>Query</c> was called.</summary>
    public int QueryCalls { get; private set; }

    /// <summary>How many times <c>Update</c> was called.</summary>
    /// <remarks>
    /// The count matters as much as the answer for the conflict case: A CONFLICT IS NEVER RETRIED. It is a
    /// definitive answer about the state of the data rather than a transient fault, and retrying one
    /// automatically is precisely the silent-overwrite failure mode the system forbids everywhere
    /// (AAP 0.6.3.8). One call for one request is the assertion that proves it.
    /// </remarks>
    public int UpdateCalls { get; private set; }

    /// <summary>The most recent retrieval request, or <see langword="null"/> when none was made.</summary>
    public PersistenceQueryRequest? LastQueryRequest { get; private set; }

    /// <summary>The most recent update request, or <see langword="null"/> when none was made.</summary>
    public PersistenceUpdateRequest? LastUpdateRequest { get; private set; }

    // -------------------------------------------------------------------------------------------------
    //  THE HANDLE LIFECYCLE - C-05's and C-06's create/release pairs and C-08's session pair
    // -------------------------------------------------------------------------------------------------

    /// <summary>The outcome <c>BeginSession</c> answers with. <c>OK</c> by default.</summary>
    /// <remarks>
    /// Set it to a failing code to drive the acquisition's FIRST refusal - the case where no session was
    /// obtained at all, so there is nothing to release and the operation must refuse without having asked
    /// Persistence for a single row.
    /// </remarks>
    public long BeginSessionCode { get; set; } = RetCode.OK;

    /// <summary>The outcome <c>CreateQueryTask</c> answers with. <c>OK</c> by default.</summary>
    /// <remarks>
    /// Set it to a failing code to drive the acquisition's SECOND refusal, which is the interesting one: a
    /// session HAS been obtained and must still be ended. C-05 adjudicates each initial setting on this
    /// call, so this is also how a rejected <c>QuerySpec</c> setting is modelled.
    /// </remarks>
    public long CreateQueryTaskCode { get; set; } = RetCode.OK;

    /// <summary>The outcome <c>CreateUpdateTask</c> answers with. <c>OK</c> by default.</summary>
    public long CreateUpdateTaskCode { get; set; } = RetCode.OK;

    /// <summary>
    /// Whether a SUCCEEDING create answers without a usable handle, modelling a malformed server reply.
    /// </summary>
    /// <remarks>
    /// A REAL AND SEPARATE FAILURE MODE, not a contrivance. A status of <c>OK</c> with no handle beside it
    /// is a producer bug, and a consumer that read the handle without testing it would send a blank one and
    /// be refused with a code that named the wrong problem. The session is still acquired in this case, so
    /// it must still be ended.
    /// </remarks>
    public bool IssueBlankTaskHandle { get; set; }

    /// <summary>How many times <c>BeginSession</c> was called.</summary>
    public int BeginSessionCalls { get; private set; }

    /// <summary>How many times <c>EndSession</c> was called.</summary>
    public int EndSessionCalls { get; private set; }

    /// <summary>How many times <c>CreateQueryTask</c> was called.</summary>
    public int CreateQueryTaskCalls { get; private set; }

    /// <summary>How many times <c>ReleaseQueryTask</c> was called.</summary>
    public int ReleaseQueryTaskCalls { get; private set; }

    /// <summary>How many times <c>CreateUpdateTask</c> was called.</summary>
    public int CreateUpdateTaskCalls { get; private set; }

    /// <summary>How many times <c>ReleaseUpdateTask</c> was called.</summary>
    public int ReleaseUpdateTaskCalls { get; private set; }

    /// <summary>The most recent query-task creation, or <see langword="null"/> when none was made.</summary>
    /// <remarks>
    /// The assertion target for "the spec travelled on the CREATE call": C-05 puts the initial
    /// configuration here, and a spec that arrived on the run call instead would be applied a second time
    /// over a task that already had it - which a clause setter carrying <c>SQL_MS_APPEND</c> would turn
    /// into a doubled fragment in the executed statement.
    /// </remarks>
    public PersistenceCreateQueryTaskRequest? LastCreateQueryTaskRequest { get; private set; }

    /// <summary>The session identifiers issued and not yet ended, in issue order.</summary>
    public ImmutableArray<string> HeldSessionIds => SnapshotHeld(_heldSessions);

    /// <summary>The query-task identifiers issued and not yet released, in issue order.</summary>
    public ImmutableArray<string> HeldQueryTaskIds => SnapshotHeld(_heldQueryTasks);

    /// <summary>The update-task identifiers issued and not yet released, in issue order.</summary>
    public ImmutableArray<string> HeldUpdateTaskIds => SnapshotHeld(_heldUpdateTasks);

    /// <summary>
    /// Every release this edge served, in arrival order: <c>"query-task"</c>, <c>"update-task"</c> or
    /// <c>"session"</c>.
    /// </summary>
    public ImmutableArray<string> ReleaseOrder => SnapshotHeld(_releaseOrder);

    /// <summary>
    /// Whether every handle this edge issued has since been released.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when no session and no task remains held - the assertion that a request
    /// released what it took.
    /// </value>
    public bool NothingIsStillHeld
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _heldSessions.Count == 0
                    && _heldQueryTasks.Count == 0
                    && _heldUpdateTasks.Count == 0;
            }
        }
    }

    /// <summary>Scripts a retrieval as the contract's three-part stream.</summary>
    /// <param name="rowCount">The row count the leading message reports.</param>
    /// <param name="chunkCount">How many data chunks follow it. One-based and at least one.</param>
    /// <remarks>
    /// Replaces any previous script rather than appending to it, so a suite cannot accidentally inherit
    /// another's rows. The chunk index is ONE-BASED, matching the legacy's own indexing.
    /// </remarks>
    public void ScriptQuery(long rowCount, int chunkCount = 1) =>
        ScriptQueryMessages(ScriptedPersistenceResponses.QueryStream(rowCount, chunkCount));

    /// <summary>
    /// Scripts a retrieval that matched no rows, as the contract's three-part stream.
    /// </summary>
    /// <remarks>
    /// THE EXPLICIT WAY TO A ZERO-ROW RESULT, and the shape is the point: a leading row count of zero, no
    /// data chunks, and the terminal status - which is what a real C-05 retrieval that matched nothing
    /// delivers. An empty stream is a DIFFERENT thing and models a transport that closed without saying
    /// anything, so the two are not interchangeable and a suite has to say which it means.
    /// </remarks>
    public void ScriptEmptyQuery() =>
        ScriptQueryMessages(
        [
            ScriptedPersistenceResponses.RowCountMessage(0L),
            ScriptedPersistenceResponses.StatusMessage(WireRetCode.Ok),
        ]);

    /// <summary>Scripts a retrieval from messages the caller composed.</summary>
    /// <param name="messages">The messages to deliver, in order. May be empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Replaces any previous script rather than appending to it, so a suite cannot accidentally inherit
    /// another's messages. An empty sequence is accepted and marks the retrieval as scripted: that is how a
    /// suite reproduces a stream that carried nothing, which is a real fault rather than a zero-row result.
    /// </remarks>
    public void ScriptQueryMessages(IEnumerable<PersistenceQueryResponse> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        QueryScript.Clear();

        foreach (PersistenceQueryResponse message in messages)
        {
            QueryScript.Add(message);
        }

        _queryScripted = true;
    }

    /// <summary>Scripts an applied update, with the counts stated rather than assumed.</summary>
    /// <param name="rowsInserted">How many rows the update reports inserted.</param>
    /// <param name="rowsUpdated">How many rows it reports updated.</param>
    /// <param name="rowsDeleted">How many rows it reports deleted.</param>
    /// <returns>The response that was scripted, so the same values can be asserted on the way back.</returns>
    /// <remarks>
    /// The three counts DEFAULT TO ZERO, so <c>ScriptUpdateSuccess()</c> is the neutral success the old
    /// implicit default used to supply - but a suite now has to write it, which is the whole difference.
    /// Nothing here invents a non-zero count: a suite that cares about counts states them, and the response
    /// is returned so it can assert the same values rather than a plausible shape.
    /// </remarks>
    public PersistenceUpdateResponse ScriptUpdateSuccess(
        long rowsInserted = 0L,
        long rowsUpdated = 0L,
        long rowsDeleted = 0L)
    {
        PersistenceUpdateResponse response = new()
        {
            Status = new PersistenceOperationStatus { RetCode = WireRetCode.Ok },
            Counts = new PersistenceUpdateCounts
            {
                Inserted = rowsInserted,
                Updated = rowsUpdated,
                Deleted = rowsDeleted,
            },
        };

        UpdateResponse = response;

        return response;
    }

    /// <summary>Builds the refusal an unscripted operation answers with.</summary>
    /// <param name="operation">The contract operation that was reached.</param>
    /// <param name="scriptingMethods">The scripting entry points that would satisfy it.</param>
    /// <returns>The exception to throw.</returns>
    /// <remarks>
    /// THE MESSAGE IS THE POINT. A double that fails has to say which collaborator was reached and how to
    /// arrange it, or the failure reads as a defect in the code under test. It also states WHY there is no
    /// default, so the next reader does not add one back as a convenience.
    /// </remarks>
    private static InvalidOperationException Unscripted(string operation, string scriptingMethods) =>
        new(
            $"The Persistence edge's {operation} was reached with no script. This double has no default "
                + "answer for it, on purpose: a plausible default - an empty stream, or a bare success with "
                + "zero counts - is not a shape the contract produces, so a test that forgot to arrange its "
                + "collaborator would have passed against a fiction and kept passing after the production "
                + $"path stopped answering at all. Arrange it with {scriptingMethods}.");

    /// <summary>
    /// Scripts an <c>updatewhereclause</c> mismatch: gRPC <c>Aborted</c> carrying the current row state.
    /// </summary>
    /// <param name="rowsExpected">How many rows the statement expected to match.</param>
    /// <param name="rowsMatched">How many it actually matched.</param>
    /// <param name="rows">The conflicting rows and their current state.</param>
    /// <returns>The detail that was scripted, so the same payload can be asserted on the way back.</returns>
    /// <remarks>
    /// <para>
    /// THE CANONICAL MAPPING, NOT A CHOICE. <c>Aborted</c> is the gRPC status that projects to HTTP
    /// <b>409</b>, which is what the REST projection must surface, and the detail MUST carry the current
    /// row state so a caller can implement an explicit retry-or-surface policy (AAP 0.6.3.8). The detail is
    /// returned rather than merely stored so a suite can assert that the 409 body carried the SAME payload
    /// instead of merely a payload of the right shape.
    /// </para>
    /// <para>
    /// The outcome code defaults to the one the legacy's own defensive override produces when the
    /// transaction reports an error [<c>n_cst_thread_task_sqlupdate.sru:L208-L210</c>], where a claimed
    /// success is rewritten into a failure.
    /// </para>
    /// </remarks>
    public ConflictDetail ScriptUpdateConflict(
        long rowsExpected,
        long rowsMatched,
        params ConflictRow[] rows)
    {
        ConflictDetail detail = ScriptedPersistenceResponses.Conflict(rowsExpected, rowsMatched, rows);

        ScriptUpdateConflict(detail);

        return detail;
    }

    /// <summary>Scripts an <c>updatewhereclause</c> mismatch from a detail the caller built.</summary>
    /// <param name="detail">The conflict detail to carry.</param>
    /// <param name="retCode">The reconciled outcome code the trailer carries.</param>
    /// <exception cref="ArgumentNullException"><paramref name="detail"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The trailer key, the status and the payload type all come from the generated descriptor by way of
    /// <c>ScriptedPersistenceResponses</c>, never from a literal here: the client refuses to decode a
    /// trailer whose binding disagrees with the contract, so a hard-coded key would keep passing after a
    /// contract change that had already broken the product.
    /// </remarks>
    public void ScriptUpdateConflict(ConflictDetail detail, long retCode = RetCode.E_DB_ERROR)
    {
        ArgumentNullException.ThrowIfNull(detail);

        UpdateConflict = new PersistenceConflictException(
            detail,
            retCode,
            ScriptedPersistenceResponses.AbortedConflict(detail, retCode));
    }

    /// <summary>Clears every script and every recorded observation.</summary>
    /// <remarks>
    /// For a suite that owns its own fixture and drives several scenarios through it. A suite sharing the
    /// class fixture should not call this, because another test in the class may be reading what it clears.
    /// </remarks>
    public void Reset()
    {
        QueryScript.Clear();
        _queryScripted = false;
        UpdateConflict = null;
        _updateResponse = null;

        QueryCalls = 0;
        UpdateCalls = 0;
        LastQueryRequest = null;
        LastUpdateRequest = null;

        BeginSessionCode = RetCode.OK;
        CreateQueryTaskCode = RetCode.OK;
        CreateUpdateTaskCode = RetCode.OK;
        IssueBlankTaskHandle = false;

        lock (_lifecycleGate)
        {
            BeginSessionCalls = 0;
            EndSessionCalls = 0;
            CreateQueryTaskCalls = 0;
            ReleaseQueryTaskCalls = 0;
            CreateUpdateTaskCalls = 0;
            ReleaseUpdateTaskCalls = 0;
            LastCreateQueryTaskRequest = null;

            _heldSessions.Clear();
            _heldQueryTasks.Clear();
            _heldUpdateTasks.Clear();
            _releaseOrder.Clear();

            // THE HANDLE COUNTER IS DELIBERATELY NOT RESET. Reusing an identifier a previous scenario had
            // issued would let a stale handle from that scenario pass a release as valid, which is the one
            // thing these sets exist to detect.
        }
    }

    /// <summary>Records a retrieval and reports the scripted messages.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <returns>The messages to deliver, snapshotted so a later edit cannot mutate a live stream.</returns>
    internal ImmutableArray<PersistenceQueryResponse> RecordQuery(PersistenceQueryRequest request)
    {
        QueryCalls++;
        LastQueryRequest = request;

        // RECORDED BEFORE THE REFUSAL, so the attempt is evidence either way - a suite diagnosing a missing
        // script can still see that the route was reached, and once.
        if (!IsQueryScripted)
        {
            throw Unscripted("Query", "ScriptQuery(...), ScriptEmptyQuery() or ScriptQueryMessages(...)");
        }

        return [.. QueryScript];
    }

    /// <summary>Records an update and reports the scripted answer, or the scripted failure.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <returns>The response to answer with.</returns>
    /// <exception cref="PersistenceConflictException">A conflict was scripted.</exception>
    /// <remarks>
    /// The request is recorded BEFORE the failure is raised, so the attempt is evidence either way. A test
    /// asserting that a conflict was not retried needs the count to be right even on the throwing path.
    /// </remarks>
    internal PersistenceUpdateResponse RecordUpdate(PersistenceUpdateRequest request)
    {
        UpdateCalls++;
        LastUpdateRequest = request;

        // The conflict wins when both are scripted, because a conflict is the answer the upstream gives
        // INSTEAD of a response. When neither is scripted the property getter refuses, naming the operation
        // and the scripting methods - and the count above has already been taken, so the attempt is evidence
        // either way.
        return UpdateConflict is not null ? throw UpdateConflict : UpdateResponse;
    }

    /// <summary>Records a session begin and answers with the scripted outcome. Contract <b>C-08</b>.</summary>
    /// <returns>The response, carrying a handle only when the outcome is <c>OK</c>.</returns>
    /// <remarks>
    /// A FAILING OUTCOME CARRIES NO HANDLE, which is what the contract requires of a producer and what makes
    /// the consumer's own "status OK but no handle" test reachable only through
    /// <see cref="IssueBlankTaskHandle"/> rather than by accident here.
    /// </remarks>
    internal PersistenceBeginSessionResponse RecordBeginSession()
    {
        lock (_lifecycleGate)
        {
            BeginSessionCalls++;

            if (BeginSessionCode != RetCode.OK)
            {
                return new PersistenceBeginSessionResponse
                {
                    Status = new PersistenceOperationStatus
                    {
                        RetCode = (WireRetCode)(int)BeginSessionCode,
                        ErrorText = "Scripted: the session was refused.",
                    },
                };
            }

            string sessionId = string.Create(
                CultureInfo.InvariantCulture,
                $"scripted-session-{++_nextHandle}");

            _heldSessions.Add(sessionId);

            return new PersistenceBeginSessionResponse
            {
                Status = new PersistenceOperationStatus { RetCode = WireRetCode.Ok },
                Session = new PersistenceSessionHandle { SessionId = sessionId },
            };
        }
    }

    /// <summary>Records a session end. Contract <b>C-08</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <returns>The response, refusing a handle this edge does not hold.</returns>
    internal PersistenceEndSessionResponse RecordEndSession(PersistenceEndSessionRequest request)
    {
        lock (_lifecycleGate)
        {
            EndSessionCalls++;
            _releaseOrder.Add("session");

            bool held = request.Session is not null && _heldSessions.Remove(request.Session.SessionId);

            return new PersistenceEndSessionResponse
            {
                Status = new PersistenceOperationStatus
                {
                    RetCode = held ? WireRetCode.Ok : (WireRetCode)(int)RetCode.E_INVALID_HANDLE,
                    ErrorText = held ? string.Empty : "Scripted: no such session.",
                },
            };
        }
    }

    /// <summary>Records a query-task creation and answers with the scripted outcome. Contract <b>C-05</b>.</summary>
    /// <param name="request">The request as it arrived, recorded so the spec's arrival can be asserted.</param>
    /// <returns>The response.</returns>
    internal PersistenceCreateQueryTaskResponse RecordCreateQueryTask(
        PersistenceCreateQueryTaskRequest request)
    {
        lock (_lifecycleGate)
        {
            CreateQueryTaskCalls++;
            LastCreateQueryTaskRequest = request;

            if (CreateQueryTaskCode != RetCode.OK)
            {
                return new PersistenceCreateQueryTaskResponse
                {
                    Status = new PersistenceOperationStatus
                    {
                        RetCode = (WireRetCode)(int)CreateQueryTaskCode,
                        ErrorText = "Scripted: the query task was refused.",
                    },
                };
            }

            PersistenceCreateQueryTaskResponse response = new()
            {
                Status = new PersistenceOperationStatus { RetCode = WireRetCode.Ok },
            };

            if (!IssueBlankTaskHandle)
            {
                string taskId = string.Create(
                    CultureInfo.InvariantCulture,
                    $"scripted-query-task-{++_nextHandle}");

                _heldQueryTasks.Add(taskId);
                response.Task = new PersistenceTaskHandle { TaskId = taskId };
            }

            return response;
        }
    }

    /// <summary>Records a query-task release. Contract <b>C-05</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <returns>The response, refusing a handle this edge does not hold.</returns>
    internal PersistenceReleaseQueryTaskResponse RecordReleaseQueryTask(
        PersistenceReleaseQueryTaskRequest request)
    {
        lock (_lifecycleGate)
        {
            ReleaseQueryTaskCalls++;
            _releaseOrder.Add("query-task");

            bool held = request.Task is not null && _heldQueryTasks.Remove(request.Task.TaskId);

            return new PersistenceReleaseQueryTaskResponse
            {
                Status = new PersistenceOperationStatus
                {
                    RetCode = held ? WireRetCode.Ok : (WireRetCode)(int)RetCode.E_INVALID_HANDLE,
                    ErrorText = held ? string.Empty : "Scripted: no such query task.",
                },
            };
        }
    }

    /// <summary>Records an update-task creation and answers with the scripted outcome. Contract <b>C-06</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <returns>The response.</returns>
    internal PersistenceCreateUpdateTaskResponse RecordCreateUpdateTask(
        PersistenceCreateUpdateTaskRequest request)
    {
        lock (_lifecycleGate)
        {
            CreateUpdateTaskCalls++;

            if (CreateUpdateTaskCode != RetCode.OK)
            {
                return new PersistenceCreateUpdateTaskResponse
                {
                    Status = new PersistenceOperationStatus
                    {
                        RetCode = (WireRetCode)(int)CreateUpdateTaskCode,
                        ErrorText = "Scripted: the update task was refused.",
                    },
                };
            }

            PersistenceCreateUpdateTaskResponse response = new()
            {
                Status = new PersistenceOperationStatus { RetCode = WireRetCode.Ok },
            };

            if (!IssueBlankTaskHandle)
            {
                string taskId = string.Create(
                    CultureInfo.InvariantCulture,
                    $"scripted-update-task-{++_nextHandle}");

                _heldUpdateTasks.Add(taskId);
                response.Task = new PersistenceTaskHandle { TaskId = taskId };
            }

            return response;
        }
    }

    /// <summary>Records an update-task release. Contract <b>C-06</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <returns>The response, refusing a handle this edge does not hold.</returns>
    internal PersistenceReleaseUpdateTaskResponse RecordReleaseUpdateTask(
        PersistenceReleaseUpdateTaskRequest request)
    {
        lock (_lifecycleGate)
        {
            ReleaseUpdateTaskCalls++;
            _releaseOrder.Add("update-task");

            bool held = request.Task is not null && _heldUpdateTasks.Remove(request.Task.TaskId);

            return new PersistenceReleaseUpdateTaskResponse
            {
                Status = new PersistenceOperationStatus
                {
                    RetCode = held ? WireRetCode.Ok : (WireRetCode)(int)RetCode.E_INVALID_HANDLE,
                    ErrorText = held ? string.Empty : "Scripted: no such update task.",
                },
            };
        }
    }

    /// <summary>Snapshots a held-handle list under the gate.</summary>
    /// <param name="held">The list to snapshot.</param>
    /// <returns>The identifiers, in issue order.</returns>
    /// <remarks>
    /// A SNAPSHOT RATHER THAN THE LIST ITSELF, so an assertion cannot observe a release happening midway
    /// through its own enumeration - the exact race a leak test would otherwise be flaky on.
    /// </remarks>
    private ImmutableArray<string> SnapshotHeld(List<string> held)
    {
        lock (_lifecycleGate)
        {
            return [.. held];
        }
    }
}

/// <summary>
/// The container-facing half of the Persistence edge: a derived client that consults
/// <see cref="ScriptedPersistenceEdge"/> for the two scripted operations and defers everything else to the
/// real implementation.
/// </summary>
/// <remarks>
/// <para>
/// DERIVING IS THE PUBLISHED SEAM. <c>PersistenceClient</c> is a public class whose operations are
/// <c>virtual</c> precisely so a test can substitute a derived double, and its constructor takes the four
/// generated stubs so the composition root keeps ownership of the channel, the address and the resilience
/// pipeline. This double honours that: it passes the stubs the CONTAINER resolved straight through, so an
/// un-overridden operation still travels the deployed registration.
/// </para>
/// <para>
/// The two overrides are the retrieval and the update - the two thirds of the triple that cross this edge.
/// The validation third never does: it runs inside DataServices, against the session state the four
/// cross-event fields became [<c>se_cst_dw.sru:L89-L96</c>].
/// </para>
/// </remarks>
internal sealed class ScriptedPersistenceClient : PersistenceClient
{
    private readonly ScriptedPersistenceEdge _edge;

    /// <summary>Creates the double over the container's own generated stubs.</summary>
    /// <param name="edge">The script and record this double consults.</param>
    /// <param name="queryClient">The container's C-05 stub.</param>
    /// <param name="updateClient">The container's C-06 stub.</param>
    /// <param name="commandClient">The container's C-07 stub.</param>
    /// <param name="transactionClient">The container's C-08 stub.</param>
    /// <param name="tokenProvider">The substituted token seam.</param>
    /// <param name="logger">The host's logger for the real client type.</param>
    /// <exception cref="ArgumentNullException"><paramref name="edge"/> is <see langword="null"/>.</exception>
    internal ScriptedPersistenceClient(
        ScriptedPersistenceEdge edge,
        PersistenceQueryClient queryClient,
        PersistenceUpdateClient updateClient,
        PersistenceCommandClient commandClient,
        PersistenceTransactionClient transactionClient,
        IServiceTokenProvider tokenProvider,
        ILogger<PersistenceClient> logger)
        : base(queryClient, updateClient, commandClient, transactionClient, tokenProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(edge);

        _edge = edge;
    }

    /// <summary>Answers the scripted retrieval stream. Contract <b>C-05</b>.</summary>
    /// <param name="request">The retrieval request.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    /// <returns>The scripted messages, in delivery order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public override IAsyncEnumerable<PersistenceQueryResponse> QueryAsync(
        PersistenceQueryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Replay(_edge.RecordQuery(request), cancellationToken);
    }

    /// <summary>Answers the scripted update, or raises the scripted conflict. Contract <b>C-06</b>.</summary>
    /// <param name="request">The update request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scripted response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public override Task<PersistenceUpdateResponse> UpdateAsync(
        PersistenceUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_edge.RecordUpdate(request));
    }

    /// <summary>Answers the scripted session begin. Contract <b>C-08</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scripted response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// THE SIX LIFECYCLE OVERRIDES BELOW SIT UNDER THE CLIENT'S OWN COMPOSITION, NOT OVER IT. The scope
    /// helpers - <c>OpenQueryScopeAsync</c> and <c>OpenUpdateScopeAsync</c> - are deliberately left to the
    /// real implementation so that every test driving a retrieval or an update exercises the ordering, the
    /// refusal arms and the release path of the PRODUCTION composition. Overriding the scope helpers instead
    /// would have been fewer lines and would have tested nothing.
    /// </remarks>
    public override Task<PersistenceBeginSessionResponse> BeginSessionAsync(
        PersistenceBeginSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_edge.RecordBeginSession());
    }

    /// <summary>Answers the scripted session end. Contract <b>C-08</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scripted response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// CANCELLATION IS NOT OBSERVED ON A RELEASE, deliberately mirroring the client: the release is issued
    /// with <see cref="CancellationToken.None"/> precisely so a cancelled operation still gives its handles
    /// back, and a double that threw on a cancelled token would make that unobservable.
    /// </remarks>
    public override Task<PersistenceEndSessionResponse> EndSessionAsync(
        PersistenceEndSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(_edge.RecordEndSession(request));
    }

    /// <summary>Answers the scripted query-task creation. Contract <b>C-05</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scripted response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public override Task<PersistenceCreateQueryTaskResponse> CreateQueryTaskAsync(
        PersistenceCreateQueryTaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_edge.RecordCreateQueryTask(request));
    }

    /// <summary>Answers the scripted query-task release. Contract <b>C-05</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scripted response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public override Task<PersistenceReleaseQueryTaskResponse> ReleaseQueryTaskAsync(
        PersistenceReleaseQueryTaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(_edge.RecordReleaseQueryTask(request));
    }

    /// <summary>Answers the scripted update-task creation. Contract <b>C-06</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scripted response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public override Task<PersistenceCreateUpdateTaskResponse> CreateUpdateTaskAsync(
        PersistenceCreateUpdateTaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_edge.RecordCreateUpdateTask(request));
    }

    /// <summary>Answers the scripted update-task release. Contract <b>C-06</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scripted response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public override Task<PersistenceReleaseUpdateTaskResponse> ReleaseUpdateTaskAsync(
        PersistenceReleaseUpdateTaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(_edge.RecordReleaseUpdateTask(request));
    }

    /// <summary>Delivers a snapshot as an asynchronous stream, honouring cancellation between messages.</summary>
    /// <param name="messages">The messages to deliver.</param>
    /// <param name="cancellationToken">Cancels delivery.</param>
    /// <returns>The messages, one at a time.</returns>
    /// <remarks>
    /// <c>Task.Yield</c> between messages so the consumer genuinely observes PROGRESSIVE delivery rather
    /// than one synchronous burst - which is what reproduces the legacy recordset's own chunked arrival and
    /// is the only way a streaming assertion can be about ordering rather than about content.
    /// </remarks>
    private static async IAsyncEnumerable<PersistenceQueryResponse> Replay(
        ImmutableArray<PersistenceQueryResponse> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (PersistenceQueryResponse message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Yield();

            yield return message;
        }
    }
}

// -----------------------------------------------------------------------------------------------------
//  9. THE OPTIONAL DATAWINDOW HOST BINDING
//  ---------------------------------------------------------------------------------------------------
//  BOTH TYPES BELOW ARE OFF UNLESS DataServicesTestHostFactory.BindsDataWindowHost IS SET, and neither is
//  a DesignSystem implementation.
//
//  WHAT THEY ARE NOT: they are not the only way to reach a bound host. The shipped composition root binds
//  a real headless host, model set and event chain over the transcribed definitions in
//  Domain/DataWindowCatalogue.cs. That is what AAP 0.2.1.3 Correction 3 requires - DataServices declares
//  its own abstract host contract and IMPLEMENTS AGAINST IT, recording `se_cst_datawindow` as
//  REFERENCE-only - and what AAP 0.3.5 requires, which assigns the HEADLESS half of every UI capability to
//  DataServices and defers only the RENDERING half. An earlier revision registered three `Unbound*`
//  implementations here and cited constraint C-D against binding at all; that reading was wrong, and the
//  prose in this file repeated it.
//
//  WHAT THESE TWO ARE, THEN: test doubles over `FakeDataWindowFixtures`, reachable only from this test
//  assembly and shipped nowhere, present so a suite can SCRIPT host behaviour - a describe override, a
//  forced AcceptText failure, a recorded call log - rather than exercise the deployed catalogue. They also
//  bind ANY name except UnboundDataWindowName, which is broader than the catalogue and is the second
//  reason a case may want them.
//
//  THE RENDERING HALF IS STILL DEFERRED, and neither these doubles nor the deployed host touches it:
//  nothing under Domain/ computes, stores or answers a coordinate, a size, a colour, a font, a DPI
//  conversion or a redraw, and the rendering capability stays reserved behind the `/v1/design/**`
//  extension point (AAP 0.4.4).
//
//  AND THE UNBOUND ANSWER STAYS REACHABLE UNDER EITHER WIRING. Both doubles refuse
//  DataServicesTestHostFactory.UnboundDataWindowName and a blank name, and the deployed catalogue carries
//  neither - so the contract's own defined negative, RetCode.E_INVALID_HANDLE for a model request and a
//  failed open for a session, can still be asserted whichever wiring is in force.
// -----------------------------------------------------------------------------------------------------

/// <summary>
/// Binds a DataWindow name to a retained <c>FakeDataWindowHost</c>, publishing the fixture's recording
/// broker as the host's own.
/// </summary>
/// <remarks>
/// <para>
/// RETENTION IS THE CONTRACT, not an optimisation. The legacy attached services live as long as their
/// control does [<c>se_cst_dw.sru:L80-L84</c>], so a second call for the same name MUST return the same
/// host or an applied state would be written to one object and read from another.
/// </para>
/// <para>
/// The fixture's single recording broker is published to every host it creates, deliberately and with a
/// stated cost: the application creates a broker PER CONTROL [<c>:L562</c>], so one shared broker means
/// dispatches from two bound DataWindows land in one log. That is what makes the whole dispatch sequence
/// readable from one place, which is the reason a suite reaches for this at all; a suite that needs
/// per-host isolation registers its own factory through
/// <c>DataServicesTestHostFactory.AdditionalServiceConfiguration</c>.
/// </para>
/// </remarks>
internal sealed class BoundDataWindowHostFactory : IDataWindowHostFactory
{
    private readonly ConcurrentDictionary<string, FakeDataWindowHost> _hosts =
        new(StringComparer.Ordinal);

    private readonly EventBroker _broker;

    /// <summary>Creates the binding over the broker every host it creates will publish.</summary>
    /// <param name="broker">The broker to publish as each host's own.</param>
    /// <exception cref="ArgumentNullException"><paramref name="broker"/> is <see langword="null"/>.</exception>
    internal BoundDataWindowHostFactory(EventBroker broker)
    {
        ArgumentNullException.ThrowIfNull(broker);

        _broker = broker;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The primary fixture is the <c>COMPANY</c> DataWindow of
    /// <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd</c> - the ONLY updatable DataWindow in the whole
    /// repository and therefore the golden master for the retrieval, validation and update triple. It
    /// starts with six columns and NO ROWS, which is faithful: the oracle's own definition carries no data
    /// line because it retrieves from SQLite.
    /// </remarks>
    public DataWindowServiceHost? Create(string dataWindowName)
    {
        // Validated even though a blank name is also refused below, because a caller that supplied nothing
        // has made a different mistake from one that supplied a name this factory declines, and collapsing
        // the two would send a reader of the resulting diagnostic to the wrong place.
        ArgumentNullException.ThrowIfNull(dataWindowName);

        return Bind(dataWindowName);
    }

    /// <summary>Binds a name, or answers <see langword="null"/> when this factory declines it.</summary>
    /// <param name="dataWindowName">The name to bind.</param>
    /// <returns>The retained host, or <see langword="null"/>.</returns>
    internal FakeDataWindowHost? Bind(string dataWindowName)
    {
        if (string.IsNullOrWhiteSpace(dataWindowName)
            || string.Equals(
                dataWindowName,
                DataServicesTestHostFactory.UnboundDataWindowName,
                StringComparison.Ordinal))
        {
            return null;
        }

        return _hosts.GetOrAdd(dataWindowName, _ => FakeDataWindowFixtures.CreateCompanyFixture(_broker));
    }

}

/// <summary>
/// Binds a DataWindow handle to a retained set of the four headless models, attached to the host
/// <see cref="BoundDataWindowHostFactory"/> serves for the same name.
/// </summary>
/// <remarks>
/// <para>
/// THE FOUR MODELS ARE USELESS UNTIL ATTACHED, which is why they arrive as a SET rather than as four
/// injected singletons: each derives from the service base, each requires <c>OnInit</c>, and each throws
/// rather than guessing when asked to work unattached. Four container singletons would be four permanently
/// unattached objects whose applied state one caller wrote and another read.
/// </para>
/// <para>
/// EVERY COLLABORATOR COMES FROM THE CONTAINER, so the models are built with the composition root's own
/// decisions rather than with a second set invented here: the bound options the four read, the
/// localization facade the context-menu and row-select models route their diagnostics through
/// [<c>se_cst_dw.sru:L355</c>, <c>:L357</c>, <c>n_cst_dwsvc_rowselect.sru:L239</c>], the pinyin matcher in
/// its shipped BLOCKED state - the single genuine parity risk in the in-scope set, whose lookup table
/// exists only inside the closed binary - and the configured <c>for page</c> resolver.
/// </para>
/// <para>
/// NO ORDER IS IMPOSED HERE THAT THE APPLICATION DOES NOT ALREADY OWN. The legacy's two DIFFERENT
/// sequences - creation at <c>se_cst_dw.sru:L570-L574</c> and initialisation at <c>:L576-L580</c> with
/// positions three and four swapped - belong to the event chain, which this provider does not build. A
/// model SET has no ordering contract of its own, so constructing its four members in declaration order is
/// not a third sequence competing with those two.
/// </para>
/// </remarks>
internal sealed class BoundDataWindowModelSetProvider : IDataWindowModelSetProvider
{
    private readonly ConcurrentDictionary<string, DataWindowModelSet> _sets = new(StringComparer.Ordinal);
    private readonly BoundDataWindowHostFactory _hosts;
    private readonly IOptions<DataServicesOptions> _options;
    private readonly I18n _localization;
    private readonly PinyinFirstLetterMatcher _pinyinMatcher;
    private readonly IExpressionPageResolver _pageResolver;

    /// <summary>Creates the provider over the container's own collaborators.</summary>
    /// <param name="hosts">The host binding whose retained hosts the models attach to.</param>
    /// <param name="options">The bound options the four models read.</param>
    /// <param name="localization">The localization facade the diagnostics route through.</param>
    /// <param name="pinyinMatcher">The matcher in whatever state the host registered it.</param>
    /// <param name="pageResolver">The configured <c>for page</c> resolver.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    internal BoundDataWindowModelSetProvider(
        BoundDataWindowHostFactory hosts,
        IOptions<DataServicesOptions> options,
        I18n localization,
        PinyinFirstLetterMatcher pinyinMatcher,
        IExpressionPageResolver pageResolver)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(pinyinMatcher);
        ArgumentNullException.ThrowIfNull(pageResolver);

        _hosts = hosts;
        _options = options;
        _localization = localization;
        _pinyinMatcher = pinyinMatcher;
        _pageResolver = pageResolver;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A null answer is the interface's own "this provider cannot serve the handle" result, and each of the
    /// eight consuming operations turns it into <c>RetCode.E_INVALID_HANDLE</c> - which is a true statement
    /// about a handle that names nothing, as against silently creating a second empty DataWindow whose
    /// applied state nobody will ever read back.
    /// </remarks>
    public DataWindowModelSet? GetOrCreate(string dataWindowHandle)
    {
        ArgumentNullException.ThrowIfNull(dataWindowHandle);

        if (_sets.TryGetValue(dataWindowHandle, out DataWindowModelSet? existing))
        {
            return existing;
        }

        FakeDataWindowHost? host = _hosts.Bind(dataWindowHandle);

        if (host is null)
        {
            return null;
        }

        // GetOrAdd with an already-built value rather than with a factory, so that a race adds one set and
        // both racers observe the SAME one. A factory overload could build two and retain whichever
        // finished second, and retention is the half of this interface's contract that matters most.
        return _sets.GetOrAdd(dataWindowHandle, Build(host));
    }

    /// <summary>Builds and attaches one model set for a host.</summary>
    /// <param name="host">The host the four models attach to.</param>
    /// <returns>The attached set.</returns>
    private DataWindowModelSet Build(FakeDataWindowHost host)
    {
        DataWindowExpressionEvaluator evaluator = new(host, _pinyinMatcher, _pageResolver);

        DomainContextMenuModel contextMenu = new(_localization, evaluator, _options);
        contextMenu.OnInit(host);

        RowSelectService rowSelect = new(_localization, _options);
        rowSelect.OnInit(host);

        ColumnSortModel columnSort = new();
        columnSort.OnInit(host);

        DropDownSearchModel dropDownSearch = new(_options);
        dropDownSearch.OnInit(host);

        return new DataWindowModelSet(host, contextMenu, rowSelect, columnSort, dropDownSearch);
    }
}

/// <summary>
/// The self-check on <see cref="ScriptedPersistenceEdge"/>: an unscripted collaborator FAILS LOUDLY, and
/// every plausible answer has to be asked for by name.
/// </summary>
/// <remarks>
/// <para>
/// WHY A DOUBLE NEEDS ITS OWN TESTS. This one decides what a suite sees when it forgets to arrange its
/// collaborator, and that decision is the difference between a forgotten arrangement failing and a
/// forgotten arrangement passing against a shape the contract never produces. A permissive default is
/// invisible precisely because it produces green runs, so the refusal is pinned here rather than left to
/// be relied upon.
/// </para>
/// <para>
/// The two members under test are the ones the retrieval/validation/update triple crosses this edge with:
/// C-05's streamed <c>Query</c> and C-06's <c>Update</c>.
/// </para>
/// </remarks>
public sealed class ScriptedPersistenceEdgeTests
{
    /// <summary>An unscripted retrieval refuses, names the operation, and still records the attempt.</summary>
    [Fact]
    public void AnUnscriptedQueryRefusesAndNamesItsScriptingEntryPoints()
    {
        ScriptedPersistenceEdge edge = new();

        Assert.False(edge.IsQueryScripted);

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
            () => edge.RecordQuery(new PersistenceQueryRequest()));

        // The message has to be actionable, or the failure reads as a defect in the code under test.
        Assert.Contains("Query", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("ScriptQuery", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("ScriptEmptyQuery", refusal.Message, StringComparison.Ordinal);

        // RECORDED BEFORE THE REFUSAL, so a suite diagnosing a missing script can still see that the route
        // was reached, and reached once.
        Assert.Equal(1, edge.QueryCalls);
        Assert.NotNull(edge.LastQueryRequest);
    }

    /// <summary>An unscripted update refuses on the same terms.</summary>
    [Fact]
    public void AnUnscriptedUpdateRefusesAndNamesItsScriptingEntryPoints()
    {
        ScriptedPersistenceEdge edge = new();

        Assert.False(edge.IsUpdateScripted);

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
            () => edge.RecordUpdate(new PersistenceUpdateRequest()));

        Assert.Contains("Update", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("ScriptUpdateSuccess", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("ScriptUpdateConflict", refusal.Message, StringComparison.Ordinal);

        Assert.Equal(1, edge.UpdateCalls);
        Assert.NotNull(edge.LastUpdateRequest);
    }

    /// <summary>
    /// A zero-row retrieval is reachable, and it is the contract's three-part stream rather than nothing.
    /// </summary>
    /// <remarks>
    /// THE DISTINCTION THIS ROW EXISTS FOR. A retrieval that matched nothing still carries a leading row
    /// count and a terminal status; a stream that carried no messages at all is a transport fault. The old
    /// permissive default conflated them by answering the second when a suite meant neither.
    /// </remarks>
    [Fact]
    public void AZeroRowRetrievalIsScriptedExplicitlyAndKeepsItsThreePartShape()
    {
        ScriptedPersistenceEdge edge = new();

        edge.ScriptEmptyQuery();

        Assert.True(edge.IsQueryScripted);

        ImmutableArray<PersistenceQueryResponse> messages =
            edge.RecordQuery(new PersistenceQueryRequest());

        Assert.Equal(2, messages.Length);

        // THE SHAPE, MEMBER BY MEMBER: the leading message is the row count carrying zero, and the trailing
        // one is the terminal status. Neither is a data chunk, and the sequence is not empty.
        Assert.NotNull(messages[0].RowCount);
        Assert.Equal(0L, messages[0].RowCount.RowCount);
        Assert.NotNull(messages[1].Status);
        Assert.Equal(WireRetCode.Ok, messages[1].Status.RetCode);
    }

    /// <summary>An explicitly empty message sequence is a scripted transport fault, not an error.</summary>
    /// <remarks>
    /// The reverse of the row above: a suite that genuinely wants a stream carrying nothing can have one,
    /// because that models a real fault. What it may not do is get one by accident.
    /// </remarks>
    [Fact]
    public void AnExplicitlyEmptyMessageSequenceIsAcceptedAsAScriptedFault()
    {
        ScriptedPersistenceEdge edge = new();

        edge.ScriptQueryMessages([]);

        Assert.True(edge.IsQueryScripted);
        Assert.Empty(edge.RecordQuery(new PersistenceQueryRequest()));
    }

    /// <summary>A success is scripted with its counts stated, and zero counts have to be asked for.</summary>
    [Fact]
    public void AScriptedSuccessCarriesTheCountsTheSuiteStated()
    {
        ScriptedPersistenceEdge edge = new();

        PersistenceUpdateResponse scripted = edge.ScriptUpdateSuccess(rowsInserted: 1L, rowsUpdated: 2L);

        Assert.True(edge.IsUpdateScripted);

        PersistenceUpdateResponse answered =
            edge.RecordUpdate(new PersistenceUpdateRequest());

        // The SAME instance, so a suite asserts the payload it scripted rather than one of the right shape.
        Assert.Same(scripted, answered);
        Assert.NotNull(answered.Counts);
        Assert.Equal(1L, answered.Counts.Inserted);
        Assert.Equal(2L, answered.Counts.Updated);
        Assert.Equal(0L, answered.Counts.Deleted);

        // The neutral success the old implicit default used to supply is still reachable - by writing it.
        ScriptedPersistenceEdge neutral = new();
        _ = neutral.ScriptUpdateSuccess();
        PersistenceUpdateCounts counts =
            neutral.RecordUpdate(new PersistenceUpdateRequest()).Counts;

        Assert.Equal(0L, counts.Inserted);
        Assert.Equal(0L, counts.Updated);
        Assert.Equal(0L, counts.Deleted);
    }

    /// <summary>A conflict answers instead of a response, and wins when both are scripted.</summary>
    /// <remarks>
    /// A CONFLICT IS AN ANSWER RATHER THAN A FAILURE OF ONE, so it takes precedence: the upstream returns
    /// <c>Aborted</c> INSTEAD of a response, and a suite that scripted both is describing an upstream that
    /// conflicts. The call is still counted, because "a conflict is never retried" is a statement about how
    /// many times the upstream was invoked.
    /// </remarks>
    [Fact]
    public void AScriptedConflictAnswersInsteadOfAResponseAndIsStillCounted()
    {
        ScriptedPersistenceEdge edge = new();

        _ = edge.ScriptUpdateSuccess(rowsUpdated: 1L);

        ConflictDetail detail = edge.ScriptUpdateConflict(rowsExpected: 1L, rowsMatched: 0L);

        Assert.True(edge.IsUpdateScripted);

        PersistenceConflictException raised = Assert.Throws<PersistenceConflictException>(
            () => edge.RecordUpdate(new PersistenceUpdateRequest()));

        Assert.Same(detail, raised.Conflict);
        Assert.Equal(1, edge.UpdateCalls);
    }

    /// <summary>Reset returns the edge to the unscripted state rather than to a permissive one.</summary>
    /// <remarks>
    /// The property that makes <see cref="ScriptedPersistenceEdge.Reset"/> safe to call between scenarios:
    /// it clears arrangements without installing defaults, so a scenario that forgets to re-arrange fails
    /// instead of inheriting the previous one's answer or a plausible substitute for it.
    /// </remarks>
    [Fact]
    public void ResetReturnsTheEdgeToTheUnscriptedStateAndNotToADefault()
    {
        ScriptedPersistenceEdge edge = new();

        edge.ScriptQuery(rowCount: 3L);
        _ = edge.ScriptUpdateSuccess(rowsUpdated: 1L);
        _ = edge.RecordQuery(new PersistenceQueryRequest());

        edge.Reset();

        Assert.False(edge.IsQueryScripted);
        Assert.False(edge.IsUpdateScripted);
        Assert.Equal(0, edge.QueryCalls);
        Assert.Equal(0, edge.UpdateCalls);
        Assert.Null(edge.LastQueryRequest);
        Assert.Null(edge.LastUpdateRequest);

        _ = Assert.Throws<InvalidOperationException>(
            () => edge.RecordQuery(new PersistenceQueryRequest()));
        _ = Assert.Throws<InvalidOperationException>(
            () => edge.RecordUpdate(new PersistenceUpdateRequest()));
    }
}
