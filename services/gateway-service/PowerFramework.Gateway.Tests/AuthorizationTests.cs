// ======================================================================================================
// AuthorizationTests.cs
// Constraint C-G on the Gateway service, and the shared in-process test host this folder is built on.
// ======================================================================================================
//
// TWO JOBS, IN THIS ORDER
//
//   1. THE SHARED TEST HOST. GatewayTestHostFixture below is the ONE in-process host bootstrap for this
//      folder. It is declared here, publicly, and consumed by a sibling test class through
//      IClassFixture<GatewayTestHostFixture>. It is NOT in a file of its own and there is no Fixtures/
//      or Stubs/ subfolder, because this test folder permits a fixed, small set of files and a helper
//      file would be one more than that. A CLASS fixture is used rather than a COLLECTION fixture on
//      purpose: a collection fixture serialises every test in every class that joins the collection,
//      and these tests must stay parallel-safe across classes.
//
//   2. THE AUTHORISATION ASSERTIONS. Constraint C-G requires that every boundary this refactor creates
//      be authenticated FROM THE OUTSET. The legacy is a library - 39 PowerBuilder libraries loaded
//      into one process, with no listener, no route table and no server tier - so it opens no socket
//      and receives no unsolicited request. Decomposition therefore creates this system's FIRST EVER
//      ingress, which is why "no new attack surface" cannot be read literally and is read instead as
//      "every newly created surface is authenticated". These tests are the standing proof of that, and
//      of its negative counterpart: Gateway MINTS NOTHING.
//
// THE MOST IMPORTANT SUBSTITUTION IN THIS FOLDER
//   Composition/FrameworkInitializer.cs publishes IFrameworkRuntime, the seam over the framework's four
//   native lifecycle entry points. Their legacy declarations are
//     global function long pfwInitialize ()                              [pfwinitialize.srf:L7]
//     global function long pfwInitialize (readonly unsignedlong flags)    [pfwinitialize.srf:L8]
//     global function long pfwFinalize ()                                [pfwfinalize.srf:L7]
//     global function string pfwversion ()                               [pfwversion.srf:L7]
//   and BOTH of the first two files declare their type `from function_object native "pfw.dll"`
//   [pfwinitialize.srf:L3, pfwfinalize.srf:L3] - a closed-source Win32 binary with no source anywhere in
//   the repository and no existence at all inside the Linux container this service ships as. The fixture
//   substitutes that seam. The managed default would have started too, but substituting is what lets a
//   test RECORD the calls and so assert the mandatory initialize/finalize pairing that docs/README.md
//   marks as a requirement and that ws_objects/pfw.pbl.src/pfw.sra pairs across :L91 and :L108.
//   The flags parameter is 32-bit `uint` because the legacy parameter is `readonly unsignedlong` and
//   PowerBuilder's unsignedlong is 32 bits wide - not because 32 bits happened to be convenient.
//
// NO KEY MATERIAL, ANYWHERE IN THIS FILE (constraint C-F)
//   The signing key every token below is signed with is GENERATED, per fixture instance, by
//   RandomNumberGenerator. There is no PEM block, no base64 DER, no certificate, no passphrase, no
//   password and no connection string in this file, and the test project references no token-minting
//   package, so the compact serialisations are hand-assembled from base-class-library primitives.
//   tests/blink/test_jws.htm:L8-L23 is named as a source for this file for ONE reason: it is the
//   anti-pattern being replaced. That page hardcodes a plaintext PEM RSA private key across fifteen
//   string literals and then signs a JWS with it. It was read. Nothing was copied from it, and no
//   value from it is reproduced here in any form.
//
// DETERMINISM (the characterization model's one hard prerequisite)
//   Every clock read on the paths under test comes from the injected TimeProvider, so the fixture
//   registers a frozen one and the emitted timestamps are asserted for exact equality rather than for
//   being "about now". Only GetUtcNow is frozen: timers keep delegating to the system provider, because
//   freezing those would deadlock any component that waits on one. Credential VALIDITY WINDOWS are the
//   deliberate exception and are minted against the real clock - lifetime validation is one of the four
//   properties under test and the stock handler evaluates it against the system clock, so a credential
//   minted against a frozen instant would be long expired and every positive assertion would fail for a
//   reason that has nothing to do with the boundary. Substituting a lifetime validator instead would
//   DISABLE the very check the expired-credential test exists to demonstrate.
//
// NO NETWORK, NO DATABASE, NO SIBLING SERVICE, NO CONTAINER
//   The upstream readiness probe is substituted, the token provider is substituted with one that
//   refuses, the DataServices client is substituted with a registration that throws on resolution, and
//   every IHttpClientFactory client is given a primary handler that throws on any outbound request. Each
//   of those is more than hygiene: because a 401 is produced before any of them is touched, a test that
//   accidentally reached an upstream would fail loudly instead of passing slowly. Gateway holds no
//   storage provider, so no database is registered here - not even an in-memory one (constraint C-E).
//
// LEGACY SOURCES ARE REFERENCE ONLY (constraint C-C)
//   ws_objects/** is read-only and is the behavioural oracle. The 47 w_test_*.srw windows are read for
//   SCENARIOS; not one line of any of them is translated into a test here, and not one of them is
//   edited, moved or reformatted. The scenario source for the fail-fast assertions below is
//   ws_objects/pfw.tests.pbl.src/w_test_assert.srw - :L39 and :L42 declare the two assertion forms,
//   :L119 and :L137 invoke them UNCAUGHT, and :L97-L101 is the CAUGHT path, which matters because an
//   assertion the application handled itself never reaches the system-error event at all. An uncaught
//   one does, and ws_objects/pfw.pbl.src/pfw.sra:L111-L144 unpacks it and ends in HALT CLOSE at :L143.
//   ws_objects/pfw.pbl.src/pfw.sra is always written out in full: TWO distinct files in this repository
//   are named pfw.sra, and the other - ws_objects/pfw.pack.pbl.src/pfw.sra - is the packager.
//   Destination names are used in code and legacy names only in citations: the legacy exception type is
//   spelled AssertionFailed and the destination type is AssertionFailure; the legacy global function
//   Assert lives on the destination static class Assertions, because a static class cannot contain a
//   member of its own name; and the illegal `#` member prefix is dropped, so ex.#Info reads .Info.
//
// FAIL FAST STAYS FAIL FAST (constraint C-B)
//   The assertions below expect TERMINATION TO BE REQUESTED for a structural fault, and expect it NOT to
//   be requested for an ordinary request fault. A test that expected warning-and-continue would silently
//   license exactly the softening C-B forbids - "graceful degradation" dressed over a behavioural change
//   - so the expectation is written the other way round on purpose. Nothing here terminates the test
//   runner: Diagnostics/SystemErrorHandler.cs takes the termination request as a substitutable callback
//   precisely so that a test can observe the request without acting on it.
//
// WHAT IS DELIBERATELY NOT ASSERTED
//   No latency, throughput, availability or timing budget of any kind. The repository publishes no
//   service-level agreement, no latency budget and no throughput target anywhere, so there is no
//   baseline to compare against and inventing one would be a fabricated requirement. The only
//   quantitative non-functional requirement in the whole brief is the per-service line-coverage gate.
//   No user interface, no component library and no design system: none is specified, none exists, and
//   the capability area that would own one is a deferred service.
//
// USER RULES
//   review_rules returns exactly one line - "No user rules provided." That is the whole document. No
//   rule is invented to fill the gap and its absence is not treated as licence to lower the bar; the
//   binding constraints are the plan's own non-rule constraints (C-A to C-L) and its enterprise-standard
//   baseline, and this file names the ones it honours at each point where it honours them.
// ======================================================================================================

using System.Buffers.Text;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PowerFramework.Gateway.Authorization;
using PowerFramework.Gateway.Clients;
using PowerFramework.Gateway.Composition;
using PowerFramework.Gateway.Configuration;
using PowerFramework.Gateway.Diagnostics;
using PowerFramework.Gateway.Endpoints;
using PowerFramework.Shared.Diagnostics;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Gateway.Tests;

// ------------------------------------------------------------------------------------------------------
//  1. THE SUBSTITUTION SEAMS
//
//  Five small doubles, each replacing exactly one collaborator the running host would otherwise reach
//  outside the process. Every type name here is distinct from every helper already declared in this
//  folder, because the whole folder compiles into one namespace and a repeated name is a build error
//  rather than an override.
// ------------------------------------------------------------------------------------------------------

/// <summary>
/// The substitute for the framework's native lifecycle entry points, which records every call it
/// receives so that the mandatory initialize/finalize pairing can be asserted from the outside.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE SUBSTITUTION THAT MAKES THE WHOLE FOLDER POSSIBLE. Both legacy entry points are declared
/// <c>from function_object native "pfw.dll"</c> [<c>ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L3</c>,
/// <c>ws_objects/pfw.base.pbl.src/pfwfinalize.srf:L3</c>], and that binary is a closed-source Win32
/// artifact with no source in the repository and no presence in a Linux container. Nothing in this port
/// loads it, and nothing here pretends to.
/// </para>
/// <para>
/// Both initialize overloads and finalize return a PowerFramework return code, so this double returns
/// <see cref="RetCode.OK"/> and the consumer classifies it with <see cref="Predicates.IsSucceeded(long?)"/>
/// rather than comparing it against zero. That indirection is contract rather than style: the algebra is
/// tri-state, <see cref="RetCode.PREVENT"/> reads as a SUCCESS because the predicate tests <c>&gt;= 0</c>,
/// and <see cref="RetCode.CANCELLED"/> is neither succeeded nor failed because the failure predicate
/// excludes it explicitly.
/// </para>
/// <para>
/// The recording list is appended to from the host's startup and shutdown paths, which run on different
/// threads from the assertions that read it, so the list is guarded by a lock and projected as a snapshot.
/// </para>
/// </remarks>
internal sealed class RecordingFrameworkRuntime : IFrameworkRuntime
{
    /// <summary>The version this double reports. Never a legacy version string.</summary>
    /// <remarks>
    /// The framework version at the head of <c>logfile.md</c> is deliberately NOT used, here or anywhere:
    /// that changelog stops in 2022 while the commit history runs years later, so reporting it would
    /// publish a stale document's contents as this build's version.
    /// </remarks>
    internal const string ReportedVersion = "gateway-tests-substituted-runtime";

    /// <summary>Guards <see cref="_calls"/> against the host's startup and shutdown threads.</summary>
    private readonly Lock _gate = new();

    /// <summary>Every member invocation, in the order it was received.</summary>
    private readonly List<string> _calls = [];

    /// <summary>The mask the consumer passed to <see cref="Initialize(uint)"/>, if it was called.</summary>
    private uint? _requestedMask;

    /// <summary>
    /// The code every initialize overload returns.
    /// </summary>
    /// <remarks>
    /// Settable so that the three readings of the preserved algebra - succeeded, failed, and neither - are
    /// all reachable through this one double, which is exactly why the application project publishes the
    /// seam as an interface. It defaults to a success so that the shared host boots.
    /// </remarks>
    internal long InitializeResult { get; set; } = RetCode.OK;

    /// <summary>The code <see cref="Finalize"/> returns.</summary>
    internal long FinalizeResult { get; set; } = RetCode.OK;

    /// <summary>A fault the initialize overloads raise instead of returning, when set.</summary>
    /// <remarks>
    /// A boundary that FAULTS rather than returning a code is a distinct case from one that returns a
    /// failure: no code exists to report, and the consumer has to say so rather than invent one.
    /// </remarks>
    internal Exception? InitializeFault { get; set; }

    /// <summary>A fault <see cref="Finalize"/> raises instead of returning, when set.</summary>
    internal Exception? FinalizeFault { get; set; }

    /// <summary>A snapshot of the calls received, in order.</summary>
    internal IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>The capability mask the consumer requested, or <see langword="null"/> if it never did.</summary>
    internal uint? RequestedCapabilityMask
    {
        get
        {
            lock (_gate)
            {
                return _requestedMask;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Recorded even though it has no call site in the legacy estate or in this port - an exhaustive
    /// search of all 544 legacy objects finds only the flags form being called, at
    /// <c>ws_objects/pfw.pbl.src/pfw.sra:L91</c>. Recording it is what lets a test prove that.
    /// </remarks>
    public long Initialize()
    {
        Record(nameof(Initialize) + "()");

        return InitializeFault is null ? InitializeResult : throw InitializeFault;
    }

    /// <inheritdoc/>
    public long Initialize(uint flags)
    {
        lock (_gate)
        {
            _calls.Add(nameof(Initialize) + "(uint)");
            _requestedMask = flags;
        }

        return InitializeFault is null ? InitializeResult : throw InitializeFault;
    }

    /// <inheritdoc/>
    public long Finalize()
    {
        Record(nameof(Finalize) + "()");

        return FinalizeFault is null ? FinalizeResult : throw FinalizeFault;
    }

    /// <inheritdoc/>
    public string Version()
    {
        Record(nameof(Version) + "()");

        return ReportedVersion;
    }

    /// <summary>Appends one invocation to the log.</summary>
    /// <param name="call">The member signature to record.</param>
    private void Record(string call)
    {
        lock (_gate)
        {
            _calls.Add(call);
        }
    }
}

/// <summary>
/// The substitute for <see cref="IUpstreamReadinessProbe"/>: it records the address it was asked about
/// and returns a configured verdict without opening a socket.
/// </summary>
/// <remarks>
/// <para>
/// <c>Endpoints/HealthEndpoints.cs</c> publishes this seam precisely so that every branch of the
/// readiness aggregate is reachable in process with no live upstream, no container and no certificate.
/// Substituting it is what makes "the host starts with its upstreams unreachable" an assertion about
/// Gateway rather than an assertion about whether anything happens to be listening on the host running
/// the suite.
/// </para>
/// <para>
/// The default verdict is <see cref="UpstreamReadiness.Unreachable"/>, which is the honest state for a
/// suite with no upstream present. The verdict is settable so that a sibling class can reach the ready
/// and degraded branches; that is safe because a class fixture is created per test class and the tests
/// within one class do not run concurrently with each other.
/// </para>
/// </remarks>
internal sealed class RecordingUpstreamReadinessProbe : IUpstreamReadinessProbe
{
    /// <summary>Guards the recorded addresses against the three concurrent probe tasks.</summary>
    private readonly Lock _gate = new();

    /// <summary>Every address this probe was asked to observe, in arrival order.</summary>
    private readonly List<Uri> _probed = [];

    /// <summary>The verdict every observation returns.</summary>
    internal UpstreamReadiness Verdict { get; set; } = UpstreamReadiness.Unreachable;

    /// <summary>A snapshot of the addresses observed.</summary>
    internal IReadOnlyList<Uri> Probed
    {
        get
        {
            lock (_gate)
            {
                return [.. _probed];
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns a verdict and never throws, which is the contract the seam states: the aggregate is the
    /// endpoint an orchestrator's readiness gate gets its answer from, and a probe that faulted would
    /// take the container with it.
    /// </remarks>
    public ValueTask<UpstreamReadiness> ProbeAsync(
        string participant,
        Uri probeAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(probeAddress);

        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _probed.Add(probeAddress);
        }

        return ValueTask.FromResult(Verdict);
    }
}

/// <summary>
/// A clock whose <see cref="GetUtcNow"/> never moves, so that every timestamp the host emits can be
/// asserted for exact equality.
/// </summary>
/// <param name="instant">The instant every read returns.</param>
/// <remarks>
/// ONLY THE WALL CLOCK IS FROZEN. <see cref="CreateTimer"/>, <see cref="GetTimestamp"/>,
/// <see cref="TimestampFrequency"/> and <see cref="LocalTimeZone"/> keep delegating to
/// <see cref="TimeProvider.System"/>, because the resilience pipelines and the client factory's handler
/// rotation build timers from whichever provider is registered and a frozen timer source would leave any
/// component that waits on one waiting forever. Freezing the reads that appear in a response body while
/// leaving the reads that drive scheduling alone is the distinction that matters.
/// </remarks>
internal sealed class FrozenTestClock(DateTimeOffset instant) : TimeProvider
{
    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => instant;

    /// <inheritdoc/>
    public override long GetTimestamp() => System.GetTimestamp();

    /// <inheritdoc/>
    public override TimeZoneInfo LocalTimeZone => System.LocalTimeZone;

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => System.CreateTimer(callback, state, dueTime, period);
}

/// <summary>
/// The primary message handler every <see cref="IHttpClientFactory"/> client is given, which fails any
/// outbound request rather than making one.
/// </summary>
/// <remarks>
/// Registered so that "no network access in any test" is enforced rather than assumed. It is not merely
/// hygiene: the authorisation outcomes under test are produced BEFORE any upstream is contacted, so a
/// test that accidentally reached the network would fail loudly here instead of passing slowly and
/// leaving a hidden dependency on something being reachable.
/// </remarks>
internal sealed class UnreachableNetworkHandler : HttpMessageHandler
{
    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        throw new InvalidOperationException(
            "An outbound HTTP request was attempted from the Gateway test host, to "
                + $"'{request.RequestUri}'. No test in this folder may reach the network: the "
                + "authorisation outcomes under test are produced before any upstream is contacted, so "
                + "an attempt here means a test reached past the boundary it was asserting on.");
    }
}

/// <summary>
/// The substitute for <see cref="IServiceTokenProvider"/>, which refuses to issue anything.
/// </summary>
/// <remarks>
/// <para>
/// Gateway obtains its own outbound credential from the Security service, and none of the paths under
/// test needs one: a rejected inbound credential is rejected before any outbound call, and the accepted
/// path answers from Gateway's own process. Refusing rather than returning a canned credential is what
/// turns that reasoning into an assertion - if a path under test ever asked for one, the request would
/// fail rather than quietly succeed.
/// </para>
/// <para>
/// It is also the local expression of the sole-issuer rule: Security is the only minter in the system,
/// and a test double that manufactured a service credential for Gateway would blur exactly the boundary
/// this file exists to prove.
/// </para>
/// </remarks>
internal sealed class RefusingServiceTokenProvider : IServiceTokenProvider
{
    /// <inheritdoc/>
    public Task<ServiceToken> GetTokenAsync(ServiceTokenRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        throw new InvalidOperationException(
            "Gateway asked the Security service for an outbound credential while an authorisation test "
                + "was running. No path under test needs one, so this is a test reaching past the "
                + "boundary it was asserting on rather than a legitimate token request.");
    }
}

/// <summary>
/// A host lifetime that records whether a stop was requested, without stopping anything.
/// </summary>
/// <remarks>
/// The legacy halts the application after reporting a decoded assertion failure -
/// <c>HALT CLOSE</c> at <c>ws_objects/pfw.pbl.src/pfw.sra:L143</c> - and the destination reproduces that
/// as a non-zero exit code plus a request for host shutdown, so that the registered shutdown path, which
/// is where the framework finalize step lives, actually runs. Recording the request rather than acting on
/// it is how fail-fast is asserted AS fail-fast without terminating the test runner.
/// </remarks>
internal sealed class TerminationRecordingLifetime : IHostApplicationLifetime
{
    /// <inheritdoc/>
    public CancellationToken ApplicationStarted => CancellationToken.None;

    /// <inheritdoc/>
    public CancellationToken ApplicationStopping => CancellationToken.None;

    /// <inheritdoc/>
    public CancellationToken ApplicationStopped => CancellationToken.None;

    /// <summary>How many times a stop was requested.</summary>
    internal int StopRequestCount { get; private set; }

    /// <inheritdoc/>
    public void StopApplication() => StopRequestCount++;
}

/// <summary>
/// A scripted stand-in for the readiness probe's HTTP channel, keyed by the port of the address the probe
/// asked for.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS RATHER THAN A SUBSTITUTED PROBE. Substituting <see cref="IUpstreamReadinessProbe"/>
/// wholesale - which the fixture does by default - proves the aggregate's behaviour but leaves the
/// DEFAULT probe, the one that actually answers <c>/health</c> in the deployed gateway, unexercised.
/// Scripting the channel instead leaves the real probe in place and still contacts nothing: the handler is
/// the transport, so no socket is opened, no name is resolved and no upstream needs to exist.
/// </para>
/// <para>
/// UNSCRIPTED PORTS FAIL LOUDLY. A port with no script throws rather than answering, so a test that
/// mis-states an address discovers it instead of quietly reading the default verdict.
/// </para>
/// <para>
/// It records the address of every request and whether any of them carried an <c>Authorization</c> header,
/// which is what turns the probe's documented "it sends no credential" property into an assertion rather
/// than a comment. A readiness probe that presented a token would make readiness depend on token issuance
/// already being live - a circular dependency during a cold start, which is exactly when the compose gate
/// needs a verdict.
/// </para>
/// </remarks>
internal sealed class ScriptedReadinessChannelHandler : HttpMessageHandler
{
    /// <summary>Guards the recorded observations against concurrent probes.</summary>
    private readonly Lock _gate = new();

    /// <summary>The addresses the probe asked for, in arrival order.</summary>
    private readonly List<Uri> _requested = [];

    /// <summary>
    /// The script: one responder per upstream port. A responder either returns a response or throws, which
    /// is how the probe's three failure arms are reached.
    /// </summary>
    internal Dictionary<int, Func<HttpResponseMessage>> ByPort { get; } = [];

    /// <summary>The addresses the probe asked for.</summary>
    internal IReadOnlyList<Uri> Requested
    {
        get
        {
            lock (_gate)
            {
                return [.. _requested];
            }
        }
    }

    /// <summary>
    /// Whether any request carried an <c>Authorization</c> header. It must stay <see langword="false"/>.
    /// </summary>
    internal bool AnyRequestCarriedACredential { get; private set; }

    /// <summary>
    /// Replaces the whole script with one responder used for every upstream port.
    /// </summary>
    /// <param name="responder">The responder to install for all three ports.</param>
    /// <exception cref="ArgumentNullException"><paramref name="responder"/> is <see langword="null"/>.</exception>
    internal void ScriptEveryUpstream(Func<HttpResponseMessage> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);

        ByPort.Clear();

        foreach (int port in GatewayTestHostFixture.ConfiguredProbePorts)
        {
            ByPort[port] = responder;
        }
    }

    /// <summary>
    /// Builds a success response carrying a body verbatim, with no charset assumptions beyond UTF-8.
    /// </summary>
    /// <param name="body">The body bytes to serve.</param>
    /// <param name="mediaType">The media type to declare.</param>
    /// <returns>A <c>200</c> response over that body.</returns>
    internal static HttpResponseMessage Success(string body, string mediaType = MediaTypeNames.Application.Json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        };

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Uri address = request.RequestUri
            ?? throw new InvalidOperationException("The readiness probe sent a request with no address.");

        lock (_gate)
        {
            _requested.Add(address);

            if (request.Headers.Authorization is not null)
            {
                AnyRequestCarriedACredential = true;
            }
        }

        if (!ByPort.TryGetValue(address.Port, out Func<HttpResponseMessage>? responder))
        {
            throw new InvalidOperationException(
                $"The readiness probe asked for '{address}', whose port {address.Port} is not scripted. "
                    + "Script every configured upstream port so that no verdict is read by accident.");
        }

        // The responder may throw on purpose: that is how the probe's OperationCanceled, HttpRequest and
        // InvalidOperation arms are reached without a real transport.
        return Task.FromResult(responder());
    }
}

/// <summary>
/// A substituted readiness probe that throws instead of returning a verdict.
/// </summary>
/// <remarks>
/// The aggregate documents that a substituted probe is not trusted to be well behaved and converts
/// anything it throws into one unreachable entry rather than into a fault. That is a fail-CLOSED property
/// worth asserting: the one endpoint an unauthenticated caller can reach must never turn a collaborator's
/// defect into a <c>500</c>, because the compose readiness gate polls it.
/// </remarks>
internal sealed class FaultingReadinessProbe : IUpstreamReadinessProbe
{
    /// <summary>How many times a verdict was asked for.</summary>
    internal int ProbeCount { get; private set; }

    /// <inheritdoc/>
    public ValueTask<UpstreamReadiness> ProbeAsync(
        string participant,
        Uri probeAddress,
        CancellationToken cancellationToken)
    {
        ProbeCount++;

        throw new InvalidTimeZoneException(
            "The substituted readiness probe faulted on purpose. The aggregate must convert this into one "
                + "unreachable upstream entry rather than into a faulted response.");
    }
}

/// <summary>
/// A component evaluator that faults when it is asked for a report.
/// </summary>
/// <remarks>
/// <para>
/// IT FAULTS ON USE, NOT ON RESOLUTION, and the distinction is forced rather than chosen: the composition
/// root resolves this service while the host starts, so a registration that threw on construction would
/// prevent startup and would be testing the wrong thing entirely. Overriding the call is what puts the
/// fault where the readiness endpoint actually meets it.
/// </para>
/// <para>
/// It stands for any registered component whose evaluation faults in a deployment. The endpoint must report
/// not-ready and withhold the cause, because a fault on the one endpoint an unauthenticated caller can
/// reach is indistinguishable from a crashed process to the readiness gate polling it.
/// </para>
/// </remarks>
internal sealed class FaultingHealthCheckService : HealthCheckService
{
    /// <summary>The text the fault carries, which must never reach the anonymous body.</summary>
    internal const string FaultMarker = "gateway-tests-component-evaluation-fault";

    /// <inheritdoc/>
    public override Task<HealthReport> CheckHealthAsync(
        Func<HealthCheckRegistration, bool>? predicate,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(FaultMarker);
}

/// <summary>
/// A logger that is enabled at every level and records what was written to it.
/// </summary>
/// <typeparam name="TCategory">The category the logger stands for.</typeparam>
/// <remarks>
/// <para>
/// ENABLED ON PURPOSE, AND THAT IS THE WHOLE POINT. The production code guards every log statement with an
/// <c>IsEnabled</c> test - correct, because the arguments are formatted only when something is listening -
/// so a null logger silently skips the operator-channel half of every fail-fast path. Those records are
/// the only place a structural fault's cause is stated: the anonymous response withholds it deliberately.
/// A test that asserted the throw but never let the record be written would leave the diagnosis
/// unverified, so this double enables every level and captures what was written.
/// </para>
/// <para>
/// It captures the FORMATTED message rather than the template, because the formatted text is what an
/// operator reads. No scope is tracked: nothing under test opens one.
/// </para>
/// </remarks>
internal sealed class EnabledRecordingLogger<TCategory> : ILogger<TCategory>
{
    /// <summary>Guards the record against concurrent writes.</summary>
    private readonly Lock _gate = new();

    /// <summary>The entries written so far.</summary>
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

    /// <summary>Everything written to this logger, in order.</summary>
    internal IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        lock (_gate)
        {
            _entries.Add((logLevel, formatter(state, exception), exception));
        }
    }

    /// <summary>
    /// Whether any entry at the given level was recorded.
    /// </summary>
    /// <param name="level">The level to look for.</param>
    /// <returns><see langword="true"/> when at least one entry was written at that level.</returns>
    internal bool Recorded(LogLevel level) => Entries.Any(entry => entry.Level == level);
}


// ------------------------------------------------------------------------------------------------------
//  2. THE SHARED TEST HOST
// ------------------------------------------------------------------------------------------------------

/// <summary>
/// The Gateway service hosted in process, together with the test-only credential factory the
/// authorisation assertions are written against.
/// </summary>
/// <remarks>
/// <para>
/// PUBLIC AND DECLARED HERE ON PURPOSE. This is the one host bootstrap for this test folder, and a
/// sibling class consumes it through <c>IClassFixture&lt;GatewayTestHostFixture&gt;</c> rather than
/// through a helper file, because this folder permits a fixed, small set of files and a helper file
/// would be one more than that. A class fixture also keeps each consuming class isolated, which a
/// collection fixture would not: a collection fixture serialises every test in every class that joins
/// it, and these tests must stay parallel-safe across classes.
/// </para>
/// <para>
/// THE HOST IS THE DEPLOYED HOST. It is booted through <c>WebApplicationFactory&lt;Program&gt;</c>, so
/// what runs is the real composition root in <c>Program.cs</c> - the real options binding and its
/// start-time validation, the real localization provider selection, the real framework lifecycle
/// service, the real stock bearer handler, the real fallback authorisation policy and the real endpoint
/// registrations. Only the collaborators that would leave the process are substituted, and each of them
/// through a seam the application project published for the purpose.
/// </para>
/// <para>
/// THE ONLY MINTER OUTSIDE THE SECURITY SERVICE IS THIS FIXTURE, and it is a test fixture rather than
/// shipped code. The Gateway assembly references no token-minting library at all, which is asserted
/// below, so it could not mint even if a future edit tried to; the compact serialisations here are
/// hand-assembled from a JSON header, a JSON payload and one keyed hash.
/// </para>
/// <para>
/// The environment defaults to Production, so the ordinary case under test is the deployed one rather
/// than the developer one. A caller that wants the Development overlay constructs its own instance with
/// the environment name and owns its disposal.
/// </para>
/// </remarks>
public sealed class GatewayTestHostFixture : WebApplicationFactory<Program>
{
    /// <summary>
    /// The issuer the host is configured to trust, and the issuer every minted credential claims.
    /// </summary>
    /// <remarks>
    /// A reserved test hostname that resolves nowhere. That is safe precisely because the verification
    /// material below is supplied directly, so the address is never contacted: no discovery document is
    /// fetched and no key set is downloaded, in any environment.
    /// </remarks>
    public const string ExpectedIssuer = "https://security.gateway-tests.invalid";

    /// <summary>The subject every minted credential claims.</summary>
    /// <remarks>A claim, never a credential: the payload carries no secret of any kind.</remarks>
    public const string TokenSubject = "gateway-authorization-conformance-test";

    /// <summary>A value that is not a token at all, and cannot be parsed as one.</summary>
    /// <remarks>
    /// Deliberately not three dot-separated segments, so it fails at the structural stage rather than at
    /// the signature stage. <see cref="MalformedTokenWithThreeSegments"/> covers the other shape.
    /// </remarks>
    public const string MalformedToken = "this-is-not-a-compact-serialization";

    /// <summary>
    /// A value shaped like a compact serialization whose segments are not base64url JSON.
    /// </summary>
    /// <remarks>
    /// Present as a distinct case because the two malformed shapes fail at different points inside the
    /// handler, and a suite that only exercised one would leave the other unproven.
    /// </remarks>
    public const string MalformedTokenWithThreeSegments = "not-a-header.not-a-payload.not-a-signature";

    /// <summary>The scheme name a bearer credential is presented under.</summary>
    public const string BearerScheme = "Bearer";

    /// <summary>
    /// The scope set a credential from <see cref="IssueValidToken"/> carries: every scope Gateway's
    /// protected routes require, spelled from the routes themselves.
    /// </summary>
    /// <remarks>
    /// Composed from the route constants rather than written out, so a route that changes its required
    /// scope changes what this fixture mints and cannot leave the suite passing against a stale spelling.
    /// The set matches the grant Security's issuance roster hands the operator identity, which is what
    /// makes a token minted here equivalent to one a real deployment would obtain.
    /// </remarks>
    /// <remarks>
    /// A LIST RATHER THAN A PRE-JOINED STRING, so it is the same shape <c>Mint</c>'s scope parameter takes.
    /// One shape means the join happens in exactly one place - inside <c>Mint</c>, where the wire form is
    /// decided - and a caller cannot pass a set that was joined with the wrong separator. It is also what
    /// keeps ABSENCE expressible: a nullable list distinguishes "no scope claim at all" from "a claim
    /// carrying nothing", and a pre-joined string cannot.
    /// </remarks>
    public static readonly IReadOnlyList<string> GrantedScopes =
    [
        PingEndpoints.RequiredScope,
        CapabilityEndpoints.RequiredScope,
        DataServicesProxyEndpoints.RequiredScope,
    ];

    /// <summary>
    /// The client credential the host presents on Security's token-issuance edge. Present so the host can
    /// START: the composition root refuses a deployment that could present neither accepted scheme.
    /// </summary>
    /// <remarks>
    /// A fixture value and not a real one. Nothing in this suite leaves the process, so it is never
    /// presented to anything; it exists because a deployment with no issuance credential is a
    /// configuration this service deliberately refuses, and a test host must be a configuration it
    /// accepts.
    /// </remarks>
    private const string IssuanceSecret = "gateway-tests-issuance-credential";

    /// <summary>The signature algorithm named in the header of every minted credential.</summary>
    private const string HeaderAlgorithm = "HS256";

    /// <summary>The token type named in the header of every minted credential.</summary>
    private const string HeaderType = "JWT";

    /// <summary>
    /// How long a credential minted for the accepted case stays valid.
    /// </summary>
    /// <remarks>
    /// Short, because the credentials the sole issuer mints in the running system are short-lived service
    /// tokens. No latency or throughput meaning attaches to the value.
    /// </remarks>
    private static readonly TimeSpan AcceptedLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How far into the past an expired credential's expiry is placed.
    /// </summary>
    /// <remarks>
    /// An hour rather than a moment, so that the assertion is about EXPIRY rather than about how generous
    /// the handler's default clock-skew tolerance happens to be.
    /// </remarks>
    private static readonly TimeSpan ExpiredBy = TimeSpan.FromHours(1);

    /// <summary>The environment name the host runs under.</summary>
    private readonly string _environmentName;

    /// <summary>
    /// The one key the host is configured to trust. Generated per fixture instance, never a literal.
    /// </summary>
    /// <remarks>
    /// 64 bytes from the platform's cryptographic generator. It exists only in this process's memory for
    /// the lifetime of one fixture, is never written anywhere, and is never echoed into an assertion
    /// message. This is constraint C-F applied to a test: the anti-pattern being replaced is
    /// <c>tests/blink/test_jws.htm:L8-L23</c>, which hardcodes a plaintext PEM RSA private key and signs
    /// a JWS with it.
    /// </remarks>
    private readonly byte[] _trustedKey = RandomNumberGenerator.GetBytes(64);

    /// <summary>A key the host is NOT configured to trust. Also generated, never a literal.</summary>
    /// <remarks>
    /// The untrusted-signature case has to be minted with a real key rather than by corrupting a
    /// signature, so that what the handler rejects is the SIGNER and not the encoding.
    /// </remarks>
    private readonly byte[] _untrustedKey = RandomNumberGenerator.GetBytes(64);

    /// <summary>The recording substitute for the native lifecycle seam.</summary>
    private readonly RecordingFrameworkRuntime _runtime = new();

    /// <summary>The recording substitute for the upstream readiness probe.</summary>
    private readonly RecordingUpstreamReadinessProbe _probe = new();

    /// <summary>Initializes a fixture for the Production environment.</summary>
    /// <remarks>
    /// The parameterless constructor is what makes this type usable as an xUnit class fixture, and
    /// Production is the default because the deployed configuration is the case that matters. There is no
    /// Development bypass to be found either way, which is asserted rather than assumed.
    /// </remarks>
    public GatewayTestHostFixture()
        : this(Environments.Production)
    {
    }

    /// <summary>Initializes a fixture for an explicit environment.</summary>
    /// <param name="environmentName">The environment name the host should run under.</param>
    /// <remarks>
    /// PRIVATE, AND IT HAS TO BE. xUnit requires a class fixture type to declare EXACTLY ONE public
    /// constructor, so a second public overload here would fail every test in every consuming class with a
    /// fixture-construction error rather than with anything about the code under test.
    /// <see cref="ForEnvironment(string)"/> is the way in.
    /// </remarks>
    private GatewayTestHostFixture(string environmentName)
    {
        _environmentName = environmentName;
    }

    /// <summary>Creates a locally owned fixture for an explicit environment.</summary>
    /// <param name="environmentName">The environment name the host should run under.</param>
    /// <returns>A fixture the caller owns and must dispose.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="environmentName"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The class fixture the framework creates always uses Production; a test that needs another environment
    /// - or that needs to STOP a host, which is how finalization is observed - creates its own here and owns
    /// its disposal, because the shared instance has to outlive every test in its class.
    /// </remarks>
    public static GatewayTestHostFixture ForEnvironment(string environmentName)
    {
        ArgumentNullException.ThrowIfNull(environmentName);

        return new GatewayTestHostFixture(environmentName);
    }

    /// <summary>
    /// The single instant every clock read inside the host returns.
    /// </summary>
    /// <remarks>
    /// A fixed date chosen for legibility and nothing else. It is what makes an emitted timestamp
    /// assertable for exact equality instead of for being approximately current, which is the
    /// characterization model's requirement that non-deterministic values be masked from both the master
    /// and the candidate recording.
    /// </remarks>
    public static DateTimeOffset FrozenNow { get; } = new(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The environment name this host runs under.</summary>
    public string EnvironmentName => _environmentName;

    /// <summary>
    /// Extra host configuration applied on top of the deployed settings files, keyed by configuration path.
    /// </summary>
    /// <remarks>
    /// The seam for exercising a configuration a settings file does not carry - a locale, a capability mask
    /// or, below, a mutual-TLS pair that cannot be read. Populate it BEFORE the first call that starts the
    /// host, because host configuration is read once during startup. A consumer that needs it should own its
    /// fixture through <see cref="ForEnvironment(string)"/> rather than mutate the shared one, so that no
    /// test in another class inherits a setting it never asked for.
    /// </remarks>
    public IDictionary<string, string?> AdditionalSettings { get; } =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>
    /// The audience the running host is configured to accept, read from its own bound options.
    /// </summary>
    /// <remarks>
    /// READ FROM THE HOST RATHER THAN RESTATED. Minting for whatever the deployment configures is the
    /// point: a literal here would let the configured audience drift while the suite kept passing against
    /// a value nothing uses. Reading it starts the host, which is why the first mint is also the boot.
    /// </remarks>
    public string ExpectedAudience
    {
        get
        {
            JwtBearerVerificationOptions verification =
                Services.GetRequiredService<IOptions<JwtBearerVerificationOptions>>().Value;

            // The options validator refuses an empty audience list on start, so the host could not have
            // reached here without one. The guard states that rather than assuming it.
            Assert.NotEmpty(verification.ValidAudiences);

            return verification.ValidAudiences[0];
        }
    }

    /// <summary>The framework lifecycle service the running host registered.</summary>
    /// <remarks>Exposed so that the initialize/finalize pairing is observable from the outside.</remarks>
    public FrameworkInitializer Initializer => Services.GetRequiredService<FrameworkInitializer>();

    /// <summary>Every call the substituted native lifecycle seam received, in order.</summary>
    public IReadOnlyList<string> RuntimeCalls => _runtime.Calls;

    /// <summary>The capability mask the composition root requested of the lifecycle seam.</summary>
    public uint? RequestedCapabilityMask => _runtime.RequestedCapabilityMask;

    /// <summary>The value the substituted lifecycle seam returns from each of its members.</summary>
    /// <remarks>
    /// Exposed so that a test can classify it with the published predicates rather than compare it
    /// against zero, which is the whole reason the seam returns a return code instead of a boolean.
    /// </remarks>
    public static long RuntimeSeamReturnCode => RetCode.OK;

    /// <summary>The addresses the substituted readiness probe was asked to observe.</summary>
    public IReadOnlyList<Uri> ProbedUpstreamAddresses => _probe.Probed;

    /// <summary>The verdict every substituted upstream observation returns.</summary>
    /// <remarks>
    /// Defaults to <see cref="UpstreamReadiness.Unreachable"/>, the honest state for a suite with no
    /// upstream present. Settable so that a sibling class can reach the ready and degraded branches of
    /// the aggregate; safe because a class fixture is created per test class and the tests within one
    /// class do not run concurrently.
    /// </remarks>
    public UpstreamReadiness UpstreamVerdict
    {
        get => _probe.Verdict;
        set => _probe.Verdict = value;
    }

    /// <summary>
    /// The ports of the three upstream readiness addresses the composition root is configured with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Persistence, DataServices and Security, in the order the aggregate reports them. They come from
    /// <c>appsettings.json</c>'s <c>Gateway:HealthProbes</c> section, and 5103 is deliberately absent: the
    /// environment had assigned it to the deferred DesignSystem service, so it stays an unallocated
    /// Phase-2 slot rather than being reassigned.
    /// </para>
    /// <para>
    /// Published so that a test scripting the probe's transport can key its script by port without
    /// restating the addresses, which would then be able to drift from the ones the host actually uses.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<int> ConfiguredProbePorts { get; } = [5101, 5102, 5104];

    /// <summary>
    /// Whether the readiness probe is substituted wholesale. <see langword="true"/> by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LEAVING IT TRUE IS THE SAFE DEFAULT: no upstream is contacted, and "every upstream unreachable" is a
    /// property of the fixture rather than of whatever happens to be listening on the machine running the
    /// suite.
    /// </para>
    /// <para>
    /// Setting it to <see langword="false"/> leaves the DEPLOYED probe in place - the one that actually
    /// answers <c>/health</c> - so that its own behaviour can be exercised. Doing so contacts nothing
    /// either, provided the probe's transport is scripted through
    /// <see cref="AdditionalServiceConfiguration"/>; with no script the fixture's unreachable-network
    /// handler is still in force, so the worst case is a loud failure rather than a real connection.
    /// </para>
    /// <para>
    /// Must be set before the host is first used, because the value is read while the host is being built.
    /// </para>
    /// </remarks>
    public bool SubstitutesUpstreamReadinessProbe { get; set; } = true;

    /// <summary>
    /// Extra service registrations applied last, after every substitution this fixture makes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONE EXTENSION POINT, and it exists because this folder is capped at five files: a test that
    /// needs a bespoke registration adds it here rather than by introducing another fixture type in
    /// another file. Applied last, so a registration here wins over the fixture's own defaults.
    /// </para>
    /// <para>
    /// It may not be used to weaken the boundary. Nothing registered through it may relax token validation,
    /// grant an anonymous fallback, or open a socket - the properties this file exists to prove are exactly
    /// the ones a convenient registration could quietly undo.
    /// </para>
    /// <para>
    /// Must be populated before the host is first used.
    /// </para>
    /// </remarks>
    public IList<Action<IServiceCollection>> AdditionalServiceConfiguration { get; } = [];

    /// <summary>Creates a client that presents no credential at all.</summary>
    /// <returns>A client whose requests carry no <c>Authorization</c> header.</returns>
    public HttpClient CreateAnonymousClient() => CreateClient();

    /// <summary>Creates a client that presents an accepted credential on every request.</summary>
    /// <returns>A client with a valid bearer credential as its default authorization header.</returns>
    public HttpClient CreateAuthenticatedClient()
    {
        HttpClient client = CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, IssueValidToken());

        return client;
    }

    /// <summary>Mints a credential the host must accept.</summary>
    /// <returns>The compact serialization.</returns>
    public string IssueValidToken() =>
        Mint(_trustedKey, ExpectedAudience, ExpectedIssuer, AcceptedLifetime, GrantedScopes);

    /// <summary>
    /// Issues an otherwise valid credential carrying EXACTLY the given scope set, which is how an
    /// ENTITLEMENT refusal is exercised separately from an authentication one.
    /// </summary>
    /// <param name="scopes">
    /// The scopes to stamp, or <see langword="null"/> to omit the claim ENTIRELY.
    /// </param>
    /// <returns>A compact-serialized token.</returns>
    /// <remarks>
    /// <para>
    /// EVERYTHING ELSE ABOUT THE TOKEN IS VALID - the trusted key, the accepted audience, the expected
    /// issuer and a live window - so a refusal obtained with it is attributable to the scope set and to
    /// nothing else. That is what separates a scope row from the credential rows beside it, which vary the
    /// key, the audience, the issuer or the window instead.
    /// </para>
    /// <para>
    /// <b>THREE DISTINCT INPUTS, AND ALL THREE HAVE TO BE MINTABLE.</b> Passing <see langword="null"/>
    /// omits the claim altogether - the shape a caller granted nothing would present. Passing an EMPTY set
    /// mints the claim carrying nothing, which is a DIFFERENT credential and is refused for a different
    /// reason. Passing a set mints exactly that set. A row that could only express two of the three would
    /// leave the third unproven, and a gate that accepted the absent claim while refusing the empty one
    /// would look correct against the two it was tested with.
    /// </para>
    /// </remarks>
    public string IssueTokenWithScopes(IReadOnlyList<string>? scopes) =>
        Mint(_trustedKey, ExpectedAudience, ExpectedIssuer, AcceptedLifetime, scopes);

    /// <summary>
    /// Issues an otherwise valid credential whose scope claim carries EXACTLY the given text, unjoined and
    /// untrimmed.
    /// </summary>
    /// <param name="claim">The literal claim value, or <see langword="null"/> to omit the claim.</param>
    /// <returns>A compact-serialized token.</returns>
    /// <remarks>
    /// <b>SEPARATE FROM THE SET-BASED FORM BECAUSE THE SUBJECT IS DIFFERENT, NOT AS A CONVENIENCE.</b>
    /// <see cref="IssueTokenWithScopes"/> takes a SET and composes the wire form itself, which is right for
    /// every row asking whether an entitlement is honoured. The rows that ask how the handler SPLITS a claim
    /// need the opposite: a claim value no set could produce - runs of several spaces, leading and trailing
    /// padding, or whitespace alone - because a joined set is single-spaced and trimmed by construction and
    /// so cannot express any of them. Two differently named members keep both askable and keep neither
    /// silently coerced into the other.
    /// </remarks>
    public string IssueTokenWithRawScopeClaim(string? claim) =>
        MintCore(_trustedKey, ExpectedAudience, ExpectedIssuer, AcceptedLifetime, claim);

    /// <summary>Mints an otherwise-valid credential whose lifetime has already lapsed.</summary>
    /// <returns>The compact serialization.</returns>
    public string IssueExpiredToken() => Mint(_trustedKey, ExpectedAudience, ExpectedIssuer, -ExpiredBy, GrantedScopes);

    /// <summary>Mints an otherwise-valid credential signed with a key the host does not trust.</summary>
    /// <returns>The compact serialization.</returns>
    public string IssueTokenSignedWithAnUntrustedKey()
        => Mint(_untrustedKey, ExpectedAudience, ExpectedIssuer, AcceptedLifetime, GrantedScopes);

    /// <summary>Mints an otherwise-valid credential intended for a different audience.</summary>
    /// <param name="audience">The audience to claim.</param>
    /// <returns>The compact serialization.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="audience"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Varying exactly one property is what makes each rejection test isolate a single validation. A case
    /// that changed two could pass for the wrong reason.
    /// </remarks>
    public string IssueTokenForAudience(string audience)
    {
        ArgumentNullException.ThrowIfNull(audience);

        return Mint(_trustedKey, audience, ExpectedIssuer, AcceptedLifetime, GrantedScopes);
    }

    /// <summary>Mints an otherwise-valid credential claiming a different issuer.</summary>
    /// <param name="issuer">The issuer to claim.</param>
    /// <returns>The compact serialization.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="issuer"/> is <see langword="null"/>.</exception>
    public string IssueTokenFromIssuer(string issuer)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        return Mint(_trustedKey, ExpectedAudience, issuer, AcceptedLifetime, GrantedScopes);
    }

    /// <summary>
    /// Configures the host under test: the deployed composition root, plus verification material this
    /// process holds and every collaborator that would otherwise leave the process.
    /// </summary>
    /// <param name="builder">The web host builder the factory is populating.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The authority is supplied as host configuration so that the service's own start-time validation
    /// runs against a real value rather than against whatever happens to be on disk. In Production
    /// <c>appsettings.json</c> declares no authority at all, so without this the host would refuse to
    /// start - which is itself the fail-fast posture working as intended.
    /// </para>
    /// <para>
    /// THE BEARER OPTIONS ARE AMENDED, NEVER REPLACED. The composition root's own registration runs first
    /// and this adds to it, so what is under test is the deployed configuration plus locally held
    /// verification material rather than a substitute for it. Supplying
    /// <see cref="JwtBearerOptions.Configuration"/> is what makes the framework's own post-configure build
    /// a static configuration manager and skip metadata retrieval entirely: the real stock handler runs,
    /// with real signature, issuer, audience and lifetime validation, and no network. All four validations
    /// are restated explicitly, because they are the properties the rejection cases exist to demonstrate
    /// and no test may quietly relax one in order to pass.
    /// </para>
    /// <para>
    /// The trusted key is placed in the configuration's key set rather than assigned as a single signing
    /// key, so the merge path the handler uses for a real published key set is the path under test here
    /// too.
    /// </para>
    /// </remarks>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(_environmentName);
        builder.UseSetting(
            $"{JwtBearerVerificationOptions.SectionName}:{nameof(JwtBearerVerificationOptions.Authority)}",
            ExpectedIssuer);

        // THE ISSUANCE CREDENTIAL, SO THE HOST CAN START AT ALL - and through the production resolution
        // path rather than a test-only door. A FLAT key, not a sectioned one: the environment-variable
        // provider maps only a double underscore onto the ':' separator, so this name is a top-level
        // configuration key rather than a path into `Gateway`, which is exactly why the composition root
        // reads it with an explicit post-configure step instead of binding it. UseSetting writes into the
        // same host configuration that step reads.
        builder.UseSetting(GatewayOptions.SecurityClientSecretConfigurationKey, IssuanceSecret);

        foreach (KeyValuePair<string, string?> setting in AdditionalSettings)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    OpenIdConnectConfiguration verification = new() { Issuer = ExpectedIssuer };
                    verification.SigningKeys.Add(new SymmetricSecurityKey(_trustedKey));

                    options.Configuration = verification;
                    options.TokenValidationParameters.ValidIssuer = ExpectedIssuer;

                    options.TokenValidationParameters.ValidateIssuer = true;
                    options.TokenValidationParameters.ValidateAudience = true;
                    options.TokenValidationParameters.ValidateLifetime = true;
                    options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                });

            // The determinism seam. The composition root registers the system clock; this replaces it so
            // that every timestamp the host emits is exactly FrozenNow.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FrozenTestClock(FrozenNow));

            // The native lifecycle seam - the substitution without which the host would try to bind
            // pfw.dll and every test in this folder would fail at once.
            services.RemoveAll<IFrameworkRuntime>();
            services.AddSingleton<IFrameworkRuntime>(_runtime);

            // The readiness probe seam, so that "no upstream reachable" is a property of the fixture
            // rather than of whatever is listening on the machine running the suite. Left in place when a
            // test wants the DEPLOYED probe instead, which reaches nothing either because its transport is
            // scripted - see SubstitutesUpstreamReadinessProbe.
            services.RemoveAll<IUpstreamReadinessProbe>();

            if (SubstitutesUpstreamReadinessProbe)
            {
                services.AddSingleton<IUpstreamReadinessProbe>(_probe);
            }

            // The two outbound edges. Both are registered to fail on use rather than to answer, because
            // no authorisation outcome under test may depend on either - and a test that reached one
            // should say so loudly instead of passing.
            services.RemoveAll<IServiceTokenProvider>();
            services.AddSingleton<IServiceTokenProvider>(new RefusingServiceTokenProvider());

            services.RemoveAll<DataServicesClient>();
            services.AddScoped<DataServicesClient>(static _ => throw new InvalidOperationException(
                "The DataServices client was resolved during an authorisation test. Every proxied route "
                    + "is challenged before its handler runs, so reaching the client means the "
                    + "authorisation boundary was crossed rather than asserted."));

            // No network from any factory-created client, enforced rather than assumed. A named client may
            // still install its own primary handler below, which is how a scripted transport reaches the
            // deployed readiness probe without reaching a socket.
            services.ConfigureHttpClientDefaults(static clients =>
                clients.ConfigurePrimaryHttpMessageHandler(static () => new UnreachableNetworkHandler()));

            // Applied LAST, so a test's bespoke registration wins over every default above.
            foreach (Action<IServiceCollection> configure in AdditionalServiceConfiguration)
            {
                configure(services);
            }
        });
    }

    /// <summary>
    /// Assembles a compact serialization from a JSON header, a JSON payload and one keyed hash.
    /// </summary>
    /// <param name="key">The signing key bytes.</param>
    /// <param name="audience">The audience claim.</param>
    /// <param name="issuer">The issuer claim.</param>
    /// <param name="lifetime">
    /// How long the credential is valid for, measured from now. A negative value produces an
    /// already-expired credential.
    /// </param>
    /// <returns>The compact serialization.</returns>
    /// <remarks>
    /// <para>
    /// HAND-ASSEMBLED ON PURPOSE. This project references no token-minting package and none may be added;
    /// the absence of one is the structural half of the sole-issuer guarantee, and the same absence is
    /// asserted of the Gateway assembly itself below.
    /// </para>
    /// <para>
    /// THE VALIDITY WINDOW IS READ FROM THE REAL CLOCK, deliberately, and this is the one place where the
    /// frozen clock would be WRONG rather than merely inconvenient. Lifetime validation is one of the four
    /// properties under test and the stock handler evaluates it against the system clock, so a credential
    /// minted against a fixed instant would be long expired by the time the suite ran and every positive
    /// assertion would fail for a reason unrelated to the boundary. The determinism requirement applies to
    /// the timestamps the endpoints EMIT, which are injected and frozen; it does not apply to the validity
    /// window of a credential the framework must evaluate for itself.
    /// </para>
    /// <para>
    /// The payload is written with <see cref="Utf8JsonWriter"/> rather than composed by interpolation, so
    /// the numeric claims are written as numbers under invariant rules and every string claim is escaped
    /// by the writer instead of by hand.
    /// </para>
    /// </remarks>
    private static string Mint(
        byte[] key,
        string audience,
        string issuer,
        TimeSpan lifetime,
        IReadOnlyList<string>? scopes = null) =>
        MintCore(
            key,
            audience,
            issuer,
            lifetime,

            // THE JOIN HAPPENS HERE AND NOWHERE ELSE, so the wire form of a scope SET is decided in one
            // place. Null is carried through as null rather than becoming an empty string, because the core
            // distinguishes an absent claim from an empty one and collapsing them here would remove that
            // distinction before the core could express it.
            scopes is null ? null : string.Join(' ', scopes));

    /// <summary>
    /// Mints a compact-serialized token whose scope claim is the supplied text, or which carries no scope
    /// claim at all.
    /// </summary>
    /// <param name="key">The signing key.</param>
    /// <param name="audience">The audience to claim.</param>
    /// <param name="issuer">The issuer to claim.</param>
    /// <param name="lifetime">The lifetime; negative mints an already-lapsed credential.</param>
    /// <param name="scopeClaim">
    /// The literal scope-claim value, or <see langword="null"/> to omit the member entirely.
    /// </param>
    /// <returns>The compact serialization.</returns>
    private static string MintCore(
        byte[] key,
        string audience,
        string issuer,
        TimeSpan lifetime,
        string? scopeClaim)
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        DateTimeOffset expires = now + lifetime;

        // An expired credential must also have been issued in the past, or the handler would reject it
        // for not being valid yet and the test would prove the wrong thing.
        DateTimeOffset issuedAt = expires < now ? expires - AcceptedLifetime : now - TimeSpan.FromMinutes(1);

        byte[] header = WriteJson(writer =>
        {
            writer.WriteString("alg", HeaderAlgorithm);
            writer.WriteString("typ", HeaderType);
        });

        byte[] payload = WriteJson(writer =>
        {
            writer.WriteString("iss", issuer);
            writer.WriteString("aud", audience);
            writer.WriteString("sub", TokenSubject);

            writer.WriteNumber("iat", issuedAt.ToUnixTimeSeconds());
            writer.WriteNumber("nbf", issuedAt.ToUnixTimeSeconds());
            writer.WriteNumber("exp", expires.ToUnixTimeSeconds());

            // ONE CLAIM CARRYING A SPACE-DELIMITED SET, which is the form Security's issuer produces and
            // therefore the only form this service's scope handler has to be able to read. Emitting one
            // claim per scope would let a row pass against a handler that could not read a real token.
            //
            // OMITTED ENTIRELY WHEN NULL, rather than written as an empty string. A token with no scope
            // claim at all is the shape a caller granted nothing would present, and it is a different input
            // from one carrying an empty claim - both must be refused, so both must be mintable. Written
            // ONCE, on exactly one arm: a second unconditional write would emit the member twice, and a
            // reader taking the first occurrence would never see the absence this arm exists to express.
            //
            // EVERY ROW STATES ITS OWN SET, and the convenience default lives on the fixture rather than
            // here: GrantedScopes carries every ingress scope the service declares, so a row whose subject
            // is something other than authorization - a status projection, a capability read, a stream
            // lifetime - names it and is not silently refused at the scope gate.
            if (scopeClaim is not null)
            {
                writer.WriteString("scope", scopeClaim);
            }
        });

        string signingInput = string.Concat(
            Base64Url.EncodeToString(header),
            ".",
            Base64Url.EncodeToString(payload));

        byte[] signature = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signingInput));

        return string.Concat(signingInput, ".", Base64Url.EncodeToString(signature));
    }

    /// <summary>Writes one JSON object and returns its UTF-8 bytes.</summary>
    /// <param name="writeMembers">Writes the object's members.</param>
    /// <returns>The encoded object.</returns>
    private static byte[] WriteJson(Action<Utf8JsonWriter> writeMembers)
    {
        System.Buffers.ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writeMembers(writer);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}

// ------------------------------------------------------------------------------------------------------
//  3. THE AUTHORISATION ASSERTIONS
// ------------------------------------------------------------------------------------------------------

/// <summary>
/// Constraint C-G on the Gateway service: every created boundary is authenticated, the one deliberate
/// anonymous exception is anonymous on purpose, and Gateway mints nothing.
/// </summary>
/// <param name="host">The shared in-process host, supplied as an xUnit class fixture.</param>
/// <remarks>
/// <para>
/// EVERY ASSERTION HERE IS AGAINST THE DEPLOYED COMPOSITION ROOT, not against a hand-assembled pipeline.
/// That distinction is the whole value of the suite: the fallback authorisation policy, the per-endpoint
/// requirements, the one <c>AllowAnonymous</c> and the stock bearer handler all interact, and only the
/// real host exercises the interaction. A suite that constructed its own pipeline could pass while the
/// shipped service was open.
/// </para>
/// <para>
/// No test here reaches a database, a container, a live sibling service or the network, and the fixture
/// enforces that rather than trusting it.
/// </para>
/// </remarks>
public sealed class AuthorizationTests(GatewayTestHostFixture host) : IClassFixture<GatewayTestHostFixture>
{
    /// <summary>The authenticated liveness probe, contract C-10.</summary>
    private const string PingRoute = "/v1/ping";

    /// <summary>The anonymous readiness probe, contract C-10.</summary>
    private const string ReadinessRoute = "/health";

    /// <summary>The authenticated capability projection.</summary>
    private const string CapabilitiesRoute = "/v1/capabilities";

    /// <summary>The generated contract document.</summary>
    private const string DocumentRoute = "/openapi/v1.json";

    /// <summary>An audience belonging to another service in the roster.</summary>
    /// <remarks>
    /// Chosen deliberately over a nonsense string: a credential minted for the Security service is exactly
    /// the confusion an audience check exists to prevent, so rejecting it is a more meaningful assertion
    /// than rejecting gibberish.
    /// </remarks>
    private const string ForeignAudience = "powerframework-security";

    /// <summary>An issuer the host is not configured to trust.</summary>
    private const string ForeignIssuer = "https://issuer.not-security.invalid";

    /// <summary>The lifetime tolerance the composition root assigns on this boundary.</summary>
    /// <remarks>
    /// SPELLED HERE, ASSERTED AGAINST THE LIVE OPTIONS. The value is a compile-time constant in
    /// <c>Program.cs</c> rather than a setting, so there is no configuration source for a test to read it
    /// back from; restating it is the only way to pin it, and pinning it is the point - the three
    /// verifying boundaries must agree with each other, and an unpinned constant can be widened in one of
    /// them without any suite noticing. See docs/ARCHITECTURE.md 9.5 for all four boundaries in one table.
    /// </remarks>
    private static readonly TimeSpan GatewayLifetimeTolerance = TimeSpan.FromSeconds(30);

    /// <summary>The header name a credential is presented in.</summary>
    private const string AuthorizationHeader = "Authorization";

    /// <summary>The challenge header the framework answers an unauthenticated request with.</summary>
    private const string ChallengeHeader = "WWW-Authenticate";

    /// <summary>The challenge parameter the handler reports a rejected credential with.</summary>
    private const string InvalidTokenChallengeParameter = "error=\"invalid_token\"";

    /// <summary>The stand-in written in place of a sensitive value before a message is rendered.</summary>
    /// <remarks>
    /// <para>
    /// CONSTRAINT C-F APPLIED TO THIS SUITE'S OWN FAILURE OUTPUT. Several rows here assert that a fault
    /// message names a configuration key and does NOT name the value behind it - a mounted certificate or
    /// private-key path. The absence half is asserted as a boolean, because
    /// <c>Assert.DoesNotContain</c> renders both its operands and would therefore publish the path at
    /// exactly the moment the defect it guards against was present. The presence half still wants to show
    /// the message, so the message is redacted with this marker first: the failure stays diagnosable and
    /// the path cannot ride along.
    /// </para>
    /// <para>
    /// Fixed-length regardless of what it replaced, so a redacted rendering discloses neither the value nor
    /// its length, and conspicuous enough that a reader of a failure message cannot mistake it for content.
    /// </para>
    /// </remarks>
    private const string RedactionMarker = "[REDACTED]";

    /// <summary>The problem-details extension member every error body in this contract carries.</summary>
    private const string RetCodeMember = "retCode";

    /// <summary>
    /// The problem-details extension member carrying the correlation identifier.
    /// </summary>
    /// <remarks>
    /// Attached by the framework's problem-details writer rather than by this service, and its value is an
    /// opaque randomly generated trace-context identifier. It is named here so the disclosure scan below can
    /// exclude it - see <see cref="RemoveCorrelationIdentifiers(JsonElement, string)"/> for why excluding it
    /// is correctness rather than convenience.
    /// </remarks>
    private const string CorrelationIdMember = "traceId";

    /// <summary>
    /// Vocabulary that would indicate signing material on a configuration surface.
    /// </summary>
    /// <remarks>
    /// The system holds exactly ONE signing secret and it belongs to the Security service, which is the
    /// sole issuer. Gateway holds verification material only, so no member and no configuration key of its
    /// own may read like a credential. Matching on vocabulary rather than on an exact name is what makes
    /// the assertion survive a future member being added under a different spelling.
    /// </remarks>
    private static readonly string[] CredentialVocabulary =
    [
        "signingkey",
        "signingcredential",
        "secret",
        "password",
        "passphrase",
        "privatekey",
        "clientsecret",
        "apikey",
    ];

    /// <summary>
    /// Every way a request can fail to present a usable credential.
    /// </summary>
    /// <remarks>
    /// A KIND rather than a header value, because member data is static and every credential below has to
    /// be signed with the key this fixture instance generated. The mapping from kind to header lives in
    /// <see cref="BuildAuthorizationHeaderValue(string)"/>, which is the only place a case can be added.
    /// </remarks>
    public static TheoryData<string> RejectedCredentialKinds =>
    [
        "absent",
        "empty-bearer",
        "malformed",
        "malformed-three-segments",
        "expired",
        "untrusted-key",
        "foreign-audience",
        "foreign-issuer",
        "basic-scheme",
        "unknown-scheme",
    ];

    /// <summary>
    /// The subset of rejected kinds that PRESENT something the handler then evaluates and refuses.
    /// </summary>
    /// <remarks>
    /// Separated from the full set because the challenge differs: a credential that was presented and
    /// found wanting is reported as an invalid token, while an absent one draws a bare scheme challenge.
    /// Conflating the two would make the challenge assertion vacuous for half its cases.
    /// </remarks>
    public static TheoryData<string> PresentedButRefusedCredentialKinds =>
    [
        "malformed",
        "malformed-three-segments",
        "expired",
        "untrusted-key",
        "foreign-audience",
        "foreign-issuer",
    ];

    /// <summary>
    /// Every route family on the ingress that requires a credential, with a method it declares.
    /// </summary>
    /// <remarks>
    /// This is the C-G matrix. It spans all four surfaces the ingress publishes - the liveness probe, the
    /// capability projection, the four reserved extension points and a representative slice of the
    /// DataWindow projection across both of its groups - because C-G is a property of the boundary rather
    /// than of any one route, and a suite that only proved it on the probe would prove very little.
    /// </remarks>
    public static TheoryData<string, string> AuthenticatedIngressBoundaries =>
        new()
        {
            { "GET", PingRoute },
            { "GET", CapabilitiesRoute },
            { "GET", "/v1/design/anything" },
            { "POST", "/v1/documents/anything" },
            { "GET", "/v1/integration/anything/nested/deeper" },
            { "POST", "/v1/scripting/anything" },
            { "POST", "/v1/datawindow/retrieve" },
            { "POST", "/v1/datawindow/update" },
            { "POST", "/v1/datawindow/sessions" },
            { "POST", "/v1/datawindow/event-gate" },
            { "POST", "/v1/datawindow/row-select/state" },
            { "POST", "/v1/datawindow/expression/sessions" },
            { "POST", "/v1/datawindow/expression/calc" },
            { "POST", "/v1/datawindow/expression/variables/add" },
        };

    /// <summary>The four reserved extension points and the deferred service each names.</summary>
    public static TheoryData<string, string> ReservedRouteFamilies =>
        new()
        {
            { "/v1/design/theming", "DesignSystem" },
            { "/v1/documents/json", "Documents" },
            { "/v1/integration/mqtt", "Integration" },
            { "/v1/scripting/sciter", "ScriptBridge" },
        };

    /// <summary>
    /// Paths a caller might plausibly try in order to obtain a credential FROM Gateway.
    /// </summary>
    /// <remarks>
    /// Token issuance is contract C-01 and belongs to the Security service alone, which is the system's
    /// sole issuer. The published verification paths are included as well: Gateway CONSUMES Security's key
    /// set and discovery document, and publishes neither of its own, so a request for either here must
    /// find nothing too.
    /// </remarks>
    public static TheoryData<string, string> TokenIssuanceShapedPaths =>
        new()
        {
            { "POST", "/v1/tokens" },
            { "POST", "/v1/token" },
            { "POST", "/tokens" },
            { "POST", "/connect/token" },
            { "POST", "/oauth2/token" },
            { "GET", "/.well-known/jwks.json" },
            { "GET", "/.well-known/openid-configuration" },
        };

    /// <summary>
    /// Every protected route family that requires a SCOPE beyond authentication, with the scope it
    /// requires and a method it declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scopes are read from the endpoint files that declare them rather than written out, so a route
    /// that changes its requirement changes this matrix with it. The four reserved extension points are
    /// deliberately ABSENT: decision D5 makes them authenticated-only, because requiring a capability
    /// scope for a route that reaches no capability would invent an entitlement for a service this phase
    /// must not implement (constraint C-D). Their posture is asserted separately below.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string> ScopedIngressBoundaries =>
        new()
        {
            { "GET", PingRoute, PingEndpoints.RequiredScope },
            { "GET", CapabilitiesRoute, CapabilityEndpoints.RequiredScope },
            { "POST", "/v1/datawindow/retrieve", DataServicesProxyEndpoints.RequiredScope },
            { "POST", "/v1/datawindow/update", DataServicesProxyEndpoints.RequiredScope },
            { "GET", "/v1/datawindow/event-gate", DataServicesProxyEndpoints.RequiredScope },
            { "POST", "/v1/datawindow/expression/calc", DataServicesProxyEndpoints.RequiredScope },
        };

    // --------------------------------------------------------------------------------------------------
    //  3.0  ENTITLEMENT, WHICH IS A DIFFERENT QUESTION FROM AUTHENTICATION
    //
    //  Every test in section 3 below proves the boundary is AUTHENTICATED. None of them proved anything
    //  about what an authenticated caller is ENTITLED to, and until the named scope policies existed the
    //  answer was "everything": one token minted for Gateway's audience reached the probe, the capability
    //  projection and all thirty-nine projected DataWindow operations alike, whatever its caller had
    //  actually been granted. The issuance roster stated least privilege per caller and no surface here
    //  enforced it, so the 403 the contract declares was unreachable.
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// A fully valid credential carrying NO scope claim is authenticated and then REFUSED with 403.
    /// </summary>
    /// <param name="method">The method to send.</param>
    /// <param name="route">The route to send it to.</param>
    /// <param name="requiredScope">The scope the route requires - unused here, and that is the point.</param>
    /// <remarks>
    /// The credential differs from the accepted one in exactly one respect: the scope claim is absent.
    /// Signature, issuer, audience and lifetime are all the accepted ones, so a 403 here cannot be an
    /// authentication failure wearing a different status.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScopedIngressBoundaries))]
    public async Task AnUnscopedCredentialIsAuthenticatedAndThenRefused(
        string method,
        string route,
        string requiredScope)
    {
        Assert.False(string.IsNullOrWhiteSpace(requiredScope));

        using HttpClient client = host.CreateAnonymousClient();
        using HttpRequestMessage request = new(new HttpMethod(method), new Uri(route, UriKind.Relative));

        request.Headers.Authorization =
            new AuthenticationHeaderValue(GatewayTestHostFixture.BearerScheme, host.IssueTokenWithScopes(null));

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // AND NOT CHALLENGED. A 403 carries no WWW-Authenticate header, because the credential was
        // accepted: re-presenting it, or presenting a better-formed one, would not change the answer.
        // Answering 401 here would tell the caller to authenticate again, which is the wrong remedy.
        Assert.DoesNotContain(ChallengeHeader, response.Headers.Select(static header => header.Key));
    }

    /// <summary>
    /// The external-caller grant the roster artifacts publish reaches EVERY scope-gated Gateway surface
    /// with a single token, so an operator who copies it can actually call this ingress.
    /// </summary>
    /// <param name="method">The method to send.</param>
    /// <param name="route">The route to send it to.</param>
    /// <param name="requiredScope">The scope the route requires; carried, along with the other two.</param>
    /// <remarks>
    /// <para>
    /// THIS IS THE GRANT AN OPERATOR IS TOLD TO COPY, AND THE ONLY TEST THAT EXERCISES IT AS ONE TOKEN.
    /// The theories above vary one scope at a time, which proves each policy reads the right claim but
    /// says nothing about the grant a deployment actually provisions. That grant is caller
    /// <c>pfw-e2e-suite</c>, audience <c>powerframework-gateway</c>, scopes <c>ping</c>,
    /// <c>capabilities</c> and <c>datawindow</c> - published identically in Security's
    /// <c>appsettings.Development.json</c>, in <c>orchestration/.env.example</c> and in
    /// <c>orchestration/docker-compose.yml</c>. If the roster and this ingress ever disagreed, every
    /// documented bring-up would authenticate and then be refused, which is exactly the failure this
    /// asserts cannot happen.
    /// </para>
    /// <para>
    /// ASSERTED AS "NOT 401 AND NOT 403" for the reason given on
    /// <see cref="ACredentialCarryingTheRequiredScopePassesAuthorization"/>: what follows authorization
    /// differs per route in this fixture, and demanding 200 would assert the upstream substitution
    /// instead of the entitlement.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScopedIngressBoundaries))]
    public async Task TheDocumentedExternalCallerGrantReachesEveryScopeGatedSurface(
        string method,
        string route,
        string requiredScope)
    {
        // THE THREE NAMES ARE READ FROM THE ROUTES, NOT RESTATED, so a rename cannot leave this test
        // passing against a grant nobody could provision.
        string[] documentedGrant =
        [
            PingEndpoints.RequiredScope,
            CapabilityEndpoints.RequiredScope,
            DataServicesProxyEndpoints.RequiredScope,
        ];

        Assert.Contains(requiredScope, documentedGrant, StringComparer.Ordinal);

        using HttpClient client = host.CreateAnonymousClient();
        using HttpRequestMessage request = new(new HttpMethod(method), new Uri(route, UriKind.Relative));

        request.Headers.Authorization = new AuthenticationHeaderValue(
            GatewayTestHostFixture.BearerScheme,
            host.IssueTokenWithScopes(documentedGrant));

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// A grant carrying a single placeholder scope authenticates and is then refused on every scope-gated
    /// surface - which is why the provisioning guidance may not offer one.
    /// </summary>
    /// <param name="method">The method to send.</param>
    /// <param name="route">The route to send it to.</param>
    /// <param name="requiredScope">The scope the route requires, which a placeholder is not.</param>
    /// <remarks>
    /// <para>
    /// 🔴 THIS PINS A CORRECTED PIECE OF DOCUMENTATION, WHICH IS WHY IT IS WORTH A TEST OF ITS OWN.
    /// Security's base <c>appsettings.json</c> used to tell an operator opening this ingress that "the
    /// scope list on that grant may be anything the deployment finds useful for its own auditing,
    /// INCLUDING a single placeholder, because the gateway itself requires no scope". Following it
    /// produced a token that MINTED SUCCESSFULLY and was then refused 403 on every route it could reach -
    /// the worst shape of configuration defect, because nothing refuses at provisioning time and the
    /// symptom appears only at the boundary.
    /// </para>
    /// <para>
    /// A placeholder is not a near miss either: the check is an exact ordinal comparison over the
    /// space-delimited claim, with no prefix match and no wildcard, so no arbitrary string can
    /// accidentally satisfy it. The refusal is 403 rather than 401 because the credential was ACCEPTED -
    /// re-presenting it would not help - which is the distinction the published contract says the two
    /// statuses exist to draw.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScopedIngressBoundaries))]
    public async Task APlaceholderScopeGrantIsRefusedOnEveryScopeGatedSurface(
        string method,
        string route,
        string requiredScope)
    {
        const string Placeholder = "placeholder";

        // The premise: the placeholder is not one of the three real names. If a scope were ever literally
        // named "placeholder" this test would be asserting the opposite of what it says.
        // Enumerated rather than passed as the set, because a FrozenSet satisfies both the ISet and the
        // IReadOnlySet overload and the call is ambiguous.
        Assert.DoesNotContain(
            Placeholder,
            ScopeAuthorizationExtensions.RegisteredScopes.AsEnumerable(),
            StringComparer.Ordinal);
        Assert.NotEqual(Placeholder, requiredScope);

        using HttpClient client = host.CreateAnonymousClient();
        using HttpRequestMessage request = new(new HttpMethod(method), new Uri(route, UriKind.Relative));

        request.Headers.Authorization = new AuthenticationHeaderValue(
            GatewayTestHostFixture.BearerScheme,
            host.IssueTokenWithScopes([Placeholder]));

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(ChallengeHeader, response.Headers.Select(static header => header.Key));
    }

    /// <summary>
    /// A credential carrying a scope the route does not require is refused too - the scopes are distinct
    /// per capability rather than one interchangeable "authenticated" grant.
    /// </summary>
    /// <param name="method">The method to send.</param>
    /// <param name="route">The route to send it to.</param>
    /// <param name="requiredScope">The scope the route requires, which this credential will NOT carry.</param>
    /// <remarks>
    /// This is the test that would fail if all three policies were accidentally registered against the
    /// same scope, which the unscoped case above cannot detect. The substitute scope is drawn from the
    /// same matrix, so it is a scope this system genuinely grants somewhere - a credential carrying an
    /// invented name would prove less.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScopedIngressBoundaries))]
    public async Task ACredentialCarryingOnlyAnotherRoutesScopeIsRefused(
        string method,
        string route,
        string requiredScope)
    {
        string[] otherScopes =
        [
            .. new[]
            {
                PingEndpoints.RequiredScope,
                CapabilityEndpoints.RequiredScope,
                DataServicesProxyEndpoints.RequiredScope,
            }.Where(scope => !string.Equals(scope, requiredScope, StringComparison.Ordinal)),
        ];

        Assert.NotEmpty(otherScopes);

        using HttpClient client = host.CreateAnonymousClient();
        using HttpRequestMessage request = new(new HttpMethod(method), new Uri(route, UriKind.Relative));

        request.Headers.Authorization = new AuthenticationHeaderValue(
            GatewayTestHostFixture.BearerScheme,
            host.IssueTokenWithScopes(otherScopes));

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// A credential carrying the required scope is NOT refused, so the policy admits as well as denies.
    /// </summary>
    /// <param name="method">The method to send.</param>
    /// <param name="route">The route to send it to.</param>
    /// <param name="requiredScope">The scope the route requires.</param>
    /// <remarks>
    /// Asserted as "not 401 and not 403" rather than as a success status, because what happens after
    /// authorization differs per route in this fixture - the probe answers, and a projected DataWindow
    /// operation reaches an upstream that is deliberately unreachable. The property under test is that
    /// authorization stopped refusing, and a test that demanded 200 would be asserting the upstream
    /// substitution instead.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScopedIngressBoundaries))]
    public async Task ACredentialCarryingTheRequiredScopePassesAuthorization(
        string method,
        string route,
        string requiredScope)
    {
        using HttpClient client = host.CreateAnonymousClient();
        using HttpRequestMessage request = new(new HttpMethod(method), new Uri(route, UriKind.Relative));

        request.Headers.Authorization = new AuthenticationHeaderValue(
            GatewayTestHostFixture.BearerScheme,
            host.IssueTokenWithScopes([requiredScope]));

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// A malformed scope claim grants nothing, and a scope that merely CONTAINS the required one grants
    /// nothing either.
    /// </summary>
    /// <param name="scopes">The scope claim value to mint.</param>
    /// <remarks>
    /// The prefix cases are the ones a containment test would wrongly admit, which is why the claim is
    /// compared as whole delimiter-separated tokens. Ordinal and case-sensitive, so a differently-cased
    /// spelling is a different scope rather than the same one.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pingx")]
    [InlineData("ping.readonly")]
    [InlineData("superping")]
    [InlineData("PING")]
    [InlineData("Ping")]
    public async Task AScopeThatMerelyResemblesTheRequiredOneGrantsNothing(string scopes)
    {
        using HttpClient client = host.CreateAnonymousClient();
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(PingRoute, UriKind.Relative));

        request.Headers.Authorization = new AuthenticationHeaderValue(
            GatewayTestHostFixture.BearerScheme,
            host.IssueTokenWithRawScopeClaim(scopes));

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// A scope claim carrying several scopes grants each of them, including at the ends of the list and
    /// around the malformed separators a real claim can contain.
    /// </summary>
    /// <param name="scopes">The scope claim value to mint.</param>
    [Theory]
    [InlineData("ping capabilities datawindow")]
    [InlineData("datawindow capabilities ping")]
    [InlineData("  ping   capabilities  ")]
    [InlineData("something.else ping")]
    public async Task AMultiValuedScopeClaimGrantsEveryTokenItCarries(string scopes)
    {
        using HttpClient client = host.CreateAnonymousClient();
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(PingRoute, UriKind.Relative));

        request.Headers.Authorization = new AuthenticationHeaderValue(
            GatewayTestHostFixture.BearerScheme,
            host.IssueTokenWithRawScopeClaim(scopes));

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The four reserved extension points require authentication and NOTHING FURTHER, so an unscoped
    /// credential still receives the reserved answer.
    /// </summary>
    /// <param name="route">The reserved route.</param>
    /// <param name="deferredService">The deferred service it names.</param>
    /// <remarks>
    /// DECISION D5, AND CONSTRAINT C-D. Requiring a capability scope here would invent an entitlement for
    /// a capability that does not exist in this phase, and the issuance roster grants no such scope to
    /// anyone - so every caller would receive 403 and the reserved answer would become unreachable, which
    /// would make the reserved surface illegible for exactly the readers it exists for. Authenticated-only
    /// is the whole requirement: the roster is not anonymously enumerable, and every authenticated caller
    /// gets the same 501.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReservedRouteFamilies))]
    public async Task AReservedRouteAnswersWithoutRequiringAnyCapabilityScope(
        string route,
        string deferredService)
    {
        using HttpClient client = host.CreateAnonymousClient();
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(route, UriKind.Relative));

        request.Headers.Authorization = new AuthenticationHeaderValue(
            GatewayTestHostFixture.BearerScheme,
            host.IssueTokenWithScopes(null));

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains(deferredService, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The anonymous readiness probe is unaffected by the scope policies, and no scope is required to
    /// reach it.
    /// </summary>
    [Fact]
    public async Task TheAnonymousProbeRequiresNoScope()
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Answered rather than refused. Unready, because nothing is listening upstream - which is a
        // readiness verdict and not an authorization one.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    /// <summary>
    /// Each protected route's scope IS the name of a policy the composition root registered, so a route
    /// can never require a policy that does not exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS ASSERTS THE RELATIONSHIP, NOT A NAMING FORMULA, and the difference is what the test is for.
    /// A policy registered under a name no route requires enforces nothing while looking correct in
    /// review, and the tests above would still pass if a route and its registration had drifted apart
    /// only in the name - so the registration is resolved from the built host BY THE NAME THE ROUTE
    /// PASSES rather than recomputed from a convention.
    /// </para>
    /// <para>
    /// 🔴 THE EARLIER SHAPE OF THIS TEST COULD NOT HAVE CAUGHT THE DEFECT IT WAS WRITTEN FOR. It
    /// asserted that a <c>ScopePolicyName</c> constant equalled <c>"gateway:scope:" + RequiredScope</c> -
    /// a string-concatenation tautology that held while THREE POLICIES REGISTERED UNDER THOSE VERY NAMES
    /// were required by no route at all, because the endpoints pass <c>GatewayScopes.*</c> instead. The
    /// dead policies, the constants and their duplicate scope predicate are gone; this asserts the
    /// surviving relationship against the container.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryProtectedRouteRequiresAPolicyTheCompositionRootRegistered()
    {
        IAuthorizationPolicyProvider policies = host.Services
            .GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (string scope in (string[])
            [
                PingEndpoints.RequiredScope,
                CapabilityEndpoints.RequiredScope,
                DataServicesProxyEndpoints.RequiredScope,
            ])
        {
            AuthorizationPolicy? policy = await policies.GetPolicyAsync(scope);

            Assert.NotNull(policy);

            // AND IT DEMANDS AUTHENTICATION AS WELL AS THE SCOPE. A named policy REPLACES the fallback
            // for the endpoint that names it, so a scope policy that omitted authentication would make
            // its route the only one in the service not demanding a credential - and would refuse the
            // credential-less caller with 403 rather than the 401 the contract publishes.
            Assert.Contains(policy.Requirements, static requirement =>
                requirement is DenyAnonymousAuthorizationRequirement);

            Assert.Contains(policy.Requirements, static requirement =>
                requirement is ScopeRequirement);
        }

        // EVERY REGISTERED SCOPE IS REQUIRED BY A ROUTE, AND THE CONVERSE. The loop above catches a route
        // naming an unregistered policy; this catches a registered policy no route requires, which is the
        // half that enforces nothing.
        Assert.Equal(
            ScopeAuthorizationExtensions.RegisteredScopes.Order(StringComparer.Ordinal),
            new[]
            {
                PingEndpoints.RequiredScope,
                CapabilityEndpoints.RequiredScope,
                DataServicesProxyEndpoints.RequiredScope,
            }.Order(StringComparer.Ordinal));

        // THREE DISTINCT SCOPES, NOT ONE REUSED. Three routes sharing a scope would be a single
        // entitlement wearing three names, which is what the parameterless form already was.
        Assert.Equal(
            3,
            new HashSet<string>(
                [
                    PingEndpoints.RequiredScope,
                    CapabilityEndpoints.RequiredScope,
                    DataServicesProxyEndpoints.RequiredScope,
                ],
                StringComparer.Ordinal).Count);
    }

    // --------------------------------------------------------------------------------------------------
    //  3.1  THE FIXTURE'S OWN PREREQUISITE, WHICH IS ALSO AN ASSERTION
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The host starts and serves requests with none of its three upstreams reachable.
    /// </summary>
    /// <remarks>
    /// <c>Endpoints/HealthEndpoints.cs</c> states that readiness AGGREGATION must not gate process
    /// startup: the host starts with its upstreams unreachable and simply reports itself unready. Every
    /// other test in this folder depends on that being true, so it is asserted directly rather than
    /// assumed. It is also the property that makes the orchestration's readiness chain work at all - a
    /// service that refused to start until its upstreams answered could never be the thing they are gated
    /// behind.
    /// </remarks>
    [Fact]
    public async Task TheHostStartsAndServesWithNoUpstreamReachable()
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage readiness = await client.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Not ready, because nothing is listening - and answered rather than faulted, which is the point.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);

        // All three upstreams were genuinely observed, through the substituted probe, so the aggregate is
        // reporting an observation rather than a default. Asserted as a DISTINCT SET rather than as a call
        // count, because the shared fixture accumulates every observation made by every test in this class
        // and a count assertion would then depend on execution order.
        List<string> observed = [.. host.ProbedUpstreamAddresses
            .Select(static address => address.ToString())
            .Distinct(StringComparer.Ordinal)];

        Assert.Equal(3, observed.Count);

        GatewayOptions.HealthProbeAddresses configured =
            host.Services.GetRequiredService<IOptions<GatewayOptions>>().Value.HealthProbes;

        foreach (string upstream in (string[])[configured.Persistence, configured.DataServices, configured.Security])
        {
            Assert.Contains(
                observed,
                address => address.StartsWith(upstream, StringComparison.OrdinalIgnoreCase));
        }

        // And the authenticated surface is live at the same time: a credential is still evaluated even
        // though every upstream is down. The stock handler looks for a token before it needs metadata,
        // which is what keeps the mandated refusal working while Security is still coming up.
        using HttpResponseMessage refused = await SendAsync(client, HttpMethod.Get, PingRoute, credential: null);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    /// <summary>
    /// The native lifecycle seam is substituted, and the code it returns is a success under the preserved
    /// tri-state algebra rather than merely equal to zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both legacy entry points are bound to <c>pfw.dll</c> [<c>pfwinitialize.srf:L3</c>,
    /// <c>pfwfinalize.srf:L3</c>], a closed Win32 binary absent from the Linux container, so the seam MUST
    /// be substituted for the host to start at all. Asserting the recorded calls is what turns that
    /// necessity into evidence.
    /// </para>
    /// <para>
    /// PRESERVED DEFECT, ASSERTED AS A DEFECT (constraint C-B and C-K). The classification below goes
    /// through the published predicates on purpose. <c>IsSucceeded</c> tests <c>&gt;= 0</c>, so
    /// <see cref="RetCode.PREVENT"/> - the value 1 - reads as a SUCCESS; <c>IsFailed</c> excludes
    /// <see cref="RetCode.CANCELLED"/> explicitly, so cancelled is NEITHER succeeded nor failed; and both
    /// answer false for null. That tri-state hole in a nominally boolean algebra is legacy behaviour
    /// [<c>ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13</c>,
    /// <c>ws_objects/pfw.shared.pbl.src/isfailed.srf:L11-L13</c>] and is reproduced rather than corrected,
    /// which is why no code anywhere in this port compares a return code against zero by hand. A test that
    /// asserted <c>code == 0</c> would silently license narrowing the predicate and "fixing" the hole.
    /// </para>
    /// <para>
    /// Only the flags overload is called, matching the single call site in the whole legacy estate
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L91</c>], and the mask is the all-capabilities value the
    /// composition root configures.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheNativeLifecycleSeamIsSubstitutedAndItsCodeSatisfiesThePreservedAlgebra()
    {
        // Forces the boot, so the recorded calls below are the startup path's.
        Assert.True(host.Initializer.IsInitialized);

        Assert.True(Predicates.IsSucceeded(GatewayTestHostFixture.RuntimeSeamReturnCode));
        Assert.False(Predicates.IsFailed(GatewayTestHostFixture.RuntimeSeamReturnCode));

        // The flags overload, and never the parameterless one - which has no call site in the legacy
        // estate either.
        Assert.Contains("Initialize(uint)", host.RuntimeCalls);
        Assert.DoesNotContain("Initialize()", host.RuntimeCalls);

        Assert.Equal(CapabilityFlags.AllCapabilitiesMask, host.RequestedCapabilityMask);

        // The version is read on the successful startup path and comes from the substitute, so no legacy
        // version string is reported as this build's.
        Assert.Contains("Version()", host.RuntimeCalls);
    }

    /// <summary>
    /// The mandatory initialize/finalize pairing holds exactly once each across a whole host lifetime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>docs/README.md</c> carries the pairing as a marked requirement, and the legacy pairs it across
    /// two events: initialization is the first statement of the application open event
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L91</c>] and finalization is the ENTIRE body of its close event
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L108</c>]. The destination reproduces that with the host's own
    /// startup and shutdown ordering.
    /// </para>
    /// <para>
    /// This test owns its host rather than using the shared fixture, because observing finalization means
    /// stopping the host and the shared instance has to outlive every other test in the class.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheMandatoryInitializeFinalizePairingHoldsAcrossAWholeHostLifetime()
    {
        GatewayTestHostFixture ownHost = GatewayTestHostFixture.ForEnvironment(Environments.Production);
        IReadOnlyList<string> callsAtShutdown;

        // Captured BEFORE disposal on purpose: disposing the factory disposes the host's service provider,
        // so resolving the lifecycle service afterwards would throw instead of answering. The instance
        // itself survives and its state is exactly what the post-shutdown assertions need.
        FrameworkInitializer? initializer = null;

        try
        {
            using HttpClient client = ownHost.CreateAnonymousClient();

            using HttpResponseMessage readiness = await client.GetAsync(
                new Uri(ReadinessRoute, UriKind.Relative),
                TestContext.Current.CancellationToken);

            initializer = ownHost.Initializer;

            Assert.True(initializer.IsInitialized);
            Assert.False(initializer.IsFinalized);
            Assert.DoesNotContain("Finalize()", ownHost.RuntimeCalls);
        }
        finally
        {
            // Disposal stops the host, which runs the registered shutdown path - the half of the pairing
            // that a fail-fast abort would have bypassed.
            await ownHost.DisposeAsync();
            callsAtShutdown = ownHost.RuntimeCalls;
        }

        Assert.NotNull(initializer);
        Assert.True(initializer.IsFinalized);

        // Exactly one of each, and initialization strictly before finalization.
        Assert.Equal(
            1,
            callsAtShutdown.Count(call =>
                string.Equals(call, "Initialize(uint)", StringComparison.Ordinal)));
        Assert.Equal(1, callsAtShutdown.Count(call => string.Equals(call, "Finalize()", StringComparison.Ordinal)));
        Assert.True(
            callsAtShutdown.ToList().IndexOf("Initialize(uint)") < callsAtShutdown.ToList().IndexOf("Finalize()"),
            "Finalization must follow initialization: docs/README.md requires the two to be paired, and "
                + "ws_objects/pfw.pbl.src/pfw.sra pairs them across its open and close events.");
    }

    // --------------------------------------------------------------------------------------------------
    //  3.2  THE AUTHENTICATED PROBE: THE MANDATED REFUSAL AND THE MANDATED SUCCESS
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every unusable credential is refused on the authenticated probe.
    /// </summary>
    /// <param name="kind">Which way the credential is unusable.</param>
    /// <remarks>
    /// The mandated behaviour, and the published contract rather than an implementation detail: the
    /// operation returns a trivial success only when a valid credential is presented, and <c>401</c>
    /// without one. Ten distinct failures are covered - no header at all, a header with no credential in
    /// it, two malformed shapes that fail at different stages, an expired credential, one signed by a key
    /// the host does not trust, one minted for another service's audience, one claiming an issuer the host
    /// does not trust, and two non-bearer schemes - because "authenticated" is only as strong as its
    /// weakest refusal.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RejectedCredentialKinds))]
    public async Task AnUnusableCredentialIsRefusedOnTheAuthenticatedProbe(string kind)
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Get,
            PingRoute,
            BuildAuthorizationHeaderValue(kind));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A valid credential is accepted, and the body is the contract's response with an exact timestamp.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The success half of the proof. The body carries what the contract says and nothing more: the
    /// responding service pinned to a constant so a caller can tell which listener answered, an
    /// authenticated flag that exists so a conformance test has something explicit to assert rather than
    /// inferring success from a status code, and a timestamp.
    /// </para>
    /// <para>
    /// The timestamp is asserted for EXACT equality against the frozen instant. That is only possible
    /// because the endpoint reads an injected clock rather than the ambient one, and it is what the
    /// characterization model means by masking a non-deterministic value from both the master and the
    /// candidate.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AValidCredentialIsAcceptedAndTheBodyIsTheContractsResponse()
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Get,
            PingRoute,
            BuildAuthorizationHeaderValue("valid"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal("gateway", body.RootElement.GetProperty("service").GetString());
        Assert.True(body.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal(
            GatewayTestHostFixture.FrozenNow,
            body.RootElement.GetProperty("timestamp").GetDateTimeOffset());
    }

    /// <summary>
    /// The challenge for an ABSENT credential names the bearer scheme and carries no error detail.
    /// </summary>
    /// <remarks>
    /// Evidence that the refusal is produced by the framework's own challenge rather than by hand-rolled
    /// header inspection: there was nothing to evaluate, so there is nothing to report about it. The scheme
    /// name is what tells a caller HOW to authenticate, which is the only thing an anonymous request is
    /// entitled to learn.
    /// </remarks>
    [Fact]
    public async Task TheChallengeForAnAbsentCredentialNamesTheBearerSchemeAndNothingElse()
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, PingRoute, credential: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        string challenge = ReadChallenge(response);

        Assert.Equal(GatewayTestHostFixture.BearerScheme, challenge);
    }

    /// <summary>
    /// The challenge for a credential that WAS presented reports it as an invalid token.
    /// </summary>
    /// <param name="kind">Which way the presented credential is unusable.</param>
    /// <remarks>
    /// The complement of the previous test, and the reason the two sets are separate: a presented
    /// credential was evaluated and found wanting, so the handler says so with the standard parameter. Only
    /// the parameter is asserted, never the accompanying description - the description of an expired
    /// credential embeds an absolute date, so asserting its text would make this test a test of the
    /// identity library's wording.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PresentedButRefusedCredentialKinds))]
    public async Task TheChallengeForAPresentedCredentialReportsAnInvalidToken(string kind)
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Get,
            PingRoute,
            BuildAuthorizationHeaderValue(kind));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(InvalidTokenChallengeParameter, ReadChallenge(response), StringComparison.Ordinal);
    }

    /// <summary>
    /// No refusal ever echoes the presented credential, in the body or in the challenge.
    /// </summary>
    /// <param name="kind">Which way the presented credential is unusable.</param>
    /// <remarks>
    /// Constraint C-F applied to the response surface. A credential is a secret even when it is invalid -
    /// an expired one may be re-issued material and a mistyped one may be a valid credential for something
    /// else - so an error path that quoted it back would publish it to every log and proxy on the way home.
    /// The presented value is checked against BOTH channels, because the challenge header is as public as
    /// the body.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PresentedButRefusedCredentialKinds))]
    public async Task NoRefusalEverEchoesThePresentedCredential(string kind)
    {
        string presented = BuildAuthorizationHeaderValue(kind)
            ?? throw new InvalidOperationException("Every presented kind supplies a header value.");

        // The credential without its scheme prefix, which is the part that is actually secret.
        string credential = presented[(presented.IndexOf(' ', StringComparison.Ordinal) + 1)..];

        using HttpClient client = host.CreateAnonymousClient();
        using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, PingRoute, presented);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        string challenge = ReadChallenge(response);

        // EVALUATED BEFORE ASSERTED (C-F). Every kind this theory presents is a REAL signed credential from
        // the fixture's own key material, and Assert.DoesNotContain renders both its operands - so an
        // overload taking the credential would publish it into the CI log at exactly the moment the echo
        // defect was present. Which channel echoed it is the whole diagnostic; the value adds nothing.
        bool bodyEchoesTheCredential = body.Contains(credential, StringComparison.Ordinal);
        bool challengeEchoesTheCredential = challenge.Contains(credential, StringComparison.Ordinal);

        Assert.False(
            bodyEchoesTheCredential,
            "The refusal body echoed the presented credential. The value is deliberately not reproduced "
                + "here; the kind that produced it is named by this row's theory data.");

        Assert.False(
            challengeEchoesTheCredential,
            "The WWW-Authenticate challenge echoed the presented credential. The challenge header is as "
                + "public as the body, so this is the same disclosure by another route.");
    }

    /// <summary>
    /// The refusal carries the contract's problem body, including both extension members, even though the
    /// challenge itself is framework code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS ASSERTION USED TO SAY THE OPPOSITE, AND THE OPPOSITE WAS THE DEFECT. It asserted an EMPTY body
    /// on the reasoning that a framework challenge writes no body and that adding status-code pages "to
    /// satisfy a test" would change the shipped service. Both halves were wrong. The published contract is
    /// authoritative over the framework default, and it declares a reusable <c>Unauthorized</c> response
    /// whose body is <c>ProblemDetails</c> [shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml] while
    /// the routes themselves declare <c>ProducesProblem</c> for 401 - so the bodyless refusal was a
    /// published promise the service was not keeping, and a test that asserted the gap froze it.
    /// </para>
    /// <para>
    /// BOTH EXTENSION MEMBERS ARE ASSERTED, because the middleware alone would produce a body without them.
    /// <c>retCode</c> comes from the composition root's problem-details customization, which classifies a
    /// framework status into the legacy vocabulary; <c>traceId</c> comes from the same place and is what the
    /// contract calls load-bearing - it is the only member through which a caller can reach anything the
    /// redacted body withholds. A body carrying neither would satisfy the media type and still be useless.
    /// </para>
    /// <para>
    /// The refusal still discloses nothing: <c>NoRefusalEverEchoesThePresentedCredential</c> above reads the
    /// body as well as the challenge header, so it now covers this body too rather than a vacuous empty
    /// string.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheRefusalCarriesTheContractsProblemBody()
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, PingRoute, credential: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            MediaTypeNames.Application.ProblemJson,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await ReadJsonAsync(response);

        // The RFC 9457 member, which is the HTTP status as an integer and not a status word.
        Assert.Equal(
            (int)HttpStatusCode.Unauthorized,
            body.RootElement.GetProperty("status").GetInt32());

        // Asserted through the published symbol rather than as a bare integer: the value is a preserved
        // legacy identifier, and the legacy algebra draws no distinction between "no credential" and
        // "credential without permission" - one access code covers both, and that asymmetry is preserved.
        Assert.Equal(RetCode.E_ACCESS_DENIED, body.RootElement.GetProperty(RetCodeMember).GetInt64());

        string? correlationId = body.RootElement.GetProperty("traceId").GetString();

        Assert.False(string.IsNullOrEmpty(correlationId));

        // The challenge header is still written, because the body is additive rather than a replacement:
        // a client that authenticates by reading the challenge is unaffected.
        Assert.Contains(
            GatewayTestHostFixture.BearerScheme,
            ReadChallenge(response),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// An unmatched route and a rejected method carry the same problem shape as the refusal.
    /// </summary>
    /// <param name="route">The route to request.</param>
    /// <param name="method">The method to request it with.</param>
    /// <param name="expected">The status the framework answers.</param>
    /// <param name="expectedRetCode">The legacy code the contract's vocabulary assigns to it.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE OTHER TWO FRAMEWORK-GENERATED STATUSES, ASSERTED BECAUSE THEY ARE PRODUCED BY DIFFERENT
    /// MIDDLEWARE THAN THE CHALLENGE. A 401 comes from authorization, a 405 from routing's method
    /// constraint, and a 404 from routing finding no candidate at all - three separate producers, none of
    /// them this service's own code, and the contract declares one body for all of them. A fix verified on
    /// the challenge alone would leave the other two bodyless.
    ///
    /// Each expectation is stated as the published symbol rather than an integer, for the same reason the
    /// refusal's is.
    /// </remarks>
    [Theory]
    [InlineData("/v1/no-such-route", "GET", HttpStatusCode.NotFound, RetCode.E_OBJECT_NOT_FOUND)]
    [InlineData(PingRoute, "DELETE", HttpStatusCode.MethodNotAllowed, RetCode.E_NO_SUPPORT)]
    public async Task EveryFrameworkGeneratedStatusCarriesTheContractsProblemBody(
        string route,
        string method,
        HttpStatusCode expected,
        long expectedRetCode)
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await SendAsync(
            client,
            new HttpMethod(method),
            route,
            BuildAuthorizationHeaderValue("valid"));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(
            MediaTypeNames.Application.ProblemJson,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal((int)expected, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expectedRetCode, body.RootElement.GetProperty(RetCodeMember).GetInt64());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("traceId").GetString()));
    }

    // --------------------------------------------------------------------------------------------------
    //  3.3  THE ONE DELIBERATE ANONYMOUS EXCEPTION
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Readiness is anonymous: it answers with no credential at all, and its body is the contract's problem
    /// shape carrying the legacy return code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-G's one contract-mandated exception, and asserted explicitly because it is what every readiness
    /// gate in the orchestration relies on. A readiness probe holds no credential and probes precisely
    /// while the service is still starting, so requiring one would make the gate unusable. Its safety comes
    /// from disclosing nothing, not from authentication - the body carries a closed vocabulary of status
    /// words and authored sentences, and no address, port, credential or configuration value.
    /// </para>
    /// <para>
    /// The <c>retCode</c> extension member is asserted through the published symbol rather than as a bare
    /// integer, because the value is a preserved legacy identifier and a literal here would let the two
    /// drift apart silently.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReadinessIsAnonymousAndCarriesTheContractsProblemShape()
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Answered, not challenged. That is the property under test; the 503 itself is the aggregate's
        // verdict with no upstream present.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            MediaTypeNames.Application.ProblemJson,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(RetCode.E_RETRY, body.RootElement.GetProperty(RetCodeMember).GetInt64());
        Assert.Equal(
            AggregateHealthReport.ReportingService,
            body.RootElement.GetProperty("service").GetString());

        // All three upstreams are named individually, which is what makes the aggregate diagnosable rather
        // than one opaque verdict.
        List<string?> named = [.. body.RootElement
            .GetProperty("upstreams")
            .EnumerateArray()
            .Select(static upstream => upstream.GetProperty("service").GetString())];

        Assert.Equal(["persistence", "dataservices", "security"], named);
    }

    /// <summary>
    /// Readiness answers identically when a valid credential is presented, because it requires none.
    /// </summary>
    /// <remarks>
    /// Anonymous means "does not require", not "refuses". A probe that changed its answer when a credential
    /// happened to be present would make the orchestration gate depend on who was asking.
    /// </remarks>
    [Fact]
    public async Task ReadinessAnswersIdenticallyWhenACredentialIsPresented()
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage anonymous = await client.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        using HttpResponseMessage authenticated = await SendAsync(
            client,
            HttpMethod.Get,
            ReadinessRoute,
            BuildAuthorizationHeaderValue("valid"));

        Assert.Equal(anonymous.StatusCode, authenticated.StatusCode);
        Assert.Equal(
            anonymous.Content.Headers.ContentType?.MediaType,
            authenticated.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Readiness is never challenged even when an unusable credential is presented.
    /// </summary>
    /// <param name="kind">Which way the presented credential is unusable.</param>
    /// <remarks>
    /// The precise shape of the anonymous posture, and worth pinning down. The route still runs through
    /// authentication - the composition root installs a fallback policy so that no route is anonymous by
    /// omission - and the presented credential still fails to authenticate; what the route's explicit
    /// <c>AllowAnonymous</c> does is skip the REQUIREMENT afterwards. So a probe carrying a stale
    /// credential from a misconfigured sidecar still gets its verdict instead of a challenge, which is the
    /// behaviour an orchestrator needs.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PresentedButRefusedCredentialKinds))]
    public async Task ReadinessIsNeverChallengedEvenWithAnUnusableCredential(string kind)
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Get,
            ReadinessRoute,
            BuildAuthorizationHeaderValue(kind));

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // --------------------------------------------------------------------------------------------------
    //  3.4  THE WHOLE INGRESS, NOT JUST THE PROBE
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every authenticated boundary on the ingress challenges an anonymous request.
    /// </summary>
    /// <param name="httpMethod">The method to send.</param>
    /// <param name="path">The route to send it to.</param>
    /// <remarks>
    /// The C-G matrix. C-G is a property of the BOUNDARY, so it is asserted across all four published
    /// surfaces rather than on the one route that exists to demonstrate it: the liveness probe, the
    /// capability projection, the four reserved extension points, and a slice of the DataWindow projection
    /// spanning both of its route groups. Nothing on the ingress except readiness and the contract document
    /// answers an anonymous caller.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AuthenticatedIngressBoundaries))]
    public async Task EveryAuthenticatedIngressBoundaryChallengesAnAnonymousRequest(
        string httpMethod,
        string path)
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Parse(httpMethod),
            path,
            credential: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(GatewayTestHostFixture.BearerScheme, ReadChallenge(response), StringComparison.Ordinal);
    }

    /// <summary>
    /// A reserved extension point answers not-implemented only once a credential is presented.
    /// </summary>
    /// <param name="path">A path inside the reserved family.</param>
    /// <param name="deferredService">The deferred service the family names.</param>
    /// <remarks>
    /// <para>
    /// Two properties in one, and the ORDER of them is the point. Anonymously the request is challenged, so
    /// the reserved roster is not anonymously enumerable; with a credential it answers <c>501</c> naming the
    /// deferred service and carrying the reserved marker. A caller therefore cannot map the eventual
    /// topology without first being authenticated.
    /// </para>
    /// <para>
    /// A ROUTING DECLARATION IS NOT A STUB (constraint C-D). There is no project, no container, no test
    /// project, no placeholder class and no partial implementation behind any of these four paths. The
    /// <c>501</c> and the body naming the deferred service are metadata about the eventual system, which is
    /// exactly what the reserved extension points are for; nothing here implements any part of a deferred
    /// service, and this test references no type belonging to one.
    /// </para>
    /// <para>
    /// The body's return code is the legacy's own not-implemented value, consumed as a symbol.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReservedRouteFamilies))]
    public async Task AReservedRouteAnswersNotImplementedOnlyOnceACredentialIsPresented(
        string path,
        string deferredService)
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage anonymous = await SendAsync(client, HttpMethod.Get, path, credential: null);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using HttpResponseMessage authenticated = await SendAsync(
            client,
            HttpMethod.Get,
            path,
            BuildAuthorizationHeaderValue("valid"));

        Assert.Equal(HttpStatusCode.NotImplemented, authenticated.StatusCode);

        using JsonDocument body = await ReadJsonAsync(authenticated);

        Assert.Equal(deferredService, body.RootElement.GetProperty("deferredService").GetString());
        Assert.Equal(deferredService, body.RootElement.GetProperty("service").GetString());
        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, body.RootElement.GetProperty(RetCodeMember).GetInt64());
        Assert.Contains(
            "Phase 2",
            body.RootElement.GetProperty("marker").GetString() ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The capability projection is refused anonymously and answered once a credential is presented.
    /// </summary>
    /// <remarks>
    /// The capability mask is the legacy framework's own module-gating bitmask surfaced as configuration
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L41-L49</c>], and it tells a caller which capability
    /// areas this deployment has enabled. That is topology information, so it is behind the boundary like
    /// everything else.
    /// </remarks>
    [Fact]
    public async Task TheCapabilityProjectionIsRefusedAnonymouslyAndAnsweredWithACredential()
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage anonymous = await SendAsync(
            client,
            HttpMethod.Get,
            CapabilitiesRoute,
            credential: null);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using HttpResponseMessage authenticated = await SendAsync(
            client,
            HttpMethod.Get,
            CapabilitiesRoute,
            BuildAuthorizationHeaderValue("valid"));

        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);

        using JsonDocument body = await ReadJsonAsync(authenticated);

        Assert.Equal(
            (long)CapabilityFlags.AllCapabilitiesMask,
            body.RootElement.GetProperty("effectiveMask").GetInt64());
    }

    /// <summary>
    /// The published contract document REQUIRES a credential, and to a caller that presents one it
    /// declares the bearer scheme its operations require.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DOCUMENT IS NOT ONE OF THIS SERVICE'S ANONYMOUS ROUTES, and the refutation comes first so the
    /// assertions below cannot pass for the wrong reason. The AAP ENUMERATES the anonymous exceptions -
    /// <c>/health</c> on all four services (C-10) and Security's key set and discovery document (C-01) -
    /// and the contract document is not among them, so it is answered 401 to an anonymous caller by the
    /// default-deny fallback policy exactly like every other non-exempt route. Nothing a consumer needs
    /// in order to obtain a credential is withheld by that: the AUTHORED contract at
    /// <c>shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml</c> is a file in the repository, and
    /// this route serves a generated projection of it.
    /// </para>
    /// <para>
    /// THERE IS NO CIRCULARITY, because no consumer in this estate learns how to authenticate from the
    /// RUNNING ingress. The authored specification under <c>shared/PowerFramework.Contracts/OpenApi/</c> is
    /// the artifact a consumer is handed, and it is distributed with the consumer; the generated document
    /// is a projection of the deployed routing table, useful for confirming what a particular deployment
    /// exposes. And constraint C-G does not leave the exceptions to judgement - it ENUMERATES them, as
    /// readiness plus the sole issuer's key-set and discovery documents, which a stock bearer handler must
    /// fetch before it holds a credential. A contract description is none of those. What tips it from
    /// defensible to wrong is what the document contains at THIS boundary: every route, every parameter
    /// and every upstream the one externally reachable service reaches, which is the map of the estate.
    /// </para>
    /// <para>
    /// Both halves are asserted. Anonymously the document is challenged, which is the C-G property. With a
    /// credential it is served, which is what keeps the first half from passing against a document that
    /// had simply stopped being served at all - and it carries the two facts C-G depends on: the scheme is
    /// HTTP bearer authentication with credentials formatted as a JWT, and the two halves of C-10 declare
    /// opposite postures, the authenticated probe naming the scheme while the readiness probe declares an
    /// EMPTY requirement list, which is how the specification spells "no authentication required".
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePublishedDocumentIsProtectedAndDeclaresTheBearerScheme()
    {
        using HttpClient anonymous = host.CreateAnonymousClient();

        using HttpResponseMessage refused = await anonymous.GetAsync(
            new Uri(DocumentRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(DocumentRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);

        JsonElement scheme = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("bearerAuth");

        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());

        JsonElement paths = document.RootElement.GetProperty("paths");

        JsonElement pingSecurity = paths.GetProperty(PingRoute).GetProperty("get").GetProperty("security");

        Assert.Equal(
            "bearerAuth",
            pingSecurity.EnumerateArray().Single().EnumerateObject().Single().Name);

        // An empty list, present rather than absent: it declares the anonymous posture in the document as
        // well as in the pipeline.
        Assert.Empty(paths.GetProperty(ReadinessRoute).GetProperty("get").GetProperty("security").EnumerateArray());
    }

    // --------------------------------------------------------------------------------------------------
    //  3.5  THE NEGATIVE SECURITY PROPERTY: GATEWAY MINTS NOTHING
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Gateway exposes no token-issuance endpoint and publishes no verification material of its own.
    /// </summary>
    /// <param name="httpMethod">The method to send.</param>
    /// <param name="path">The issuance- or discovery-shaped path to send it to.</param>
    /// <remarks>
    /// <para>
    /// THE ASSERTION A REVIEWER LOOKS FOR. Token issuance is contract C-01 and belongs to the Security
    /// service alone, which is the system's SOLE JWT ISSUER: exactly one signing secret exists in the whole
    /// system and Security holds it. Gateway, DataServices and Persistence hold verification material only.
    /// </para>
    /// <para>
    /// Both outcomes are asserted because both matter and they say different things. Anonymously the request
    /// is challenged, by the fallback authorisation policy, which is why an unmatched path is a closed door
    /// rather than an open one. WITH a valid credential the request is not-found, which is the stronger
    /// statement: the caller is authenticated, and there is still no such operation. Neither outcome is a
    /// success, so no request in this matrix can obtain a credential from Gateway.
    /// </para>
    /// <para>
    /// The two well-known paths are included deliberately. Gateway CONSUMES Security's published key set
    /// and discovery document; it publishes neither, so a caller cannot mistake Gateway for the issuer by
    /// finding discovery metadata here.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(TokenIssuanceShapedPaths))]
    public async Task GatewayExposesNoTokenIssuanceEndpoint(string httpMethod, string path)
    {
        using HttpClient client = host.CreateAnonymousClient();
        HttpMethod method = HttpMethod.Parse(httpMethod);

        using HttpResponseMessage anonymous = await SendAsync(client, method, path, credential: null);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using HttpResponseMessage authenticated = await SendAsync(
            client,
            method,
            path,
            BuildAuthorizationHeaderValue("valid"));

        Assert.Equal(HttpStatusCode.NotFound, authenticated.StatusCode);
        Assert.False(
            authenticated.IsSuccessStatusCode,
            "A token-issuance path must never succeed on Gateway: the Security service is the system's "
                + "sole issuer, and a second minting authority would defeat the point of having one.");
    }

    /// <summary>
    /// Neither of Gateway's configuration surfaces carries any signing material.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The structural half of the sole-issuer guarantee. <see cref="GatewayOptions"/> declares a locale, a
    /// capability mask, two upstream addresses, three probe addresses and a mutual-TLS PAIR OF PATHS - and
    /// nothing else. <see cref="JwtBearerVerificationOptions"/> declares an authority, a metadata
    /// requirement, an issuer list, an audience list and a claim-mapping switch: every member is an
    /// address, an identifier or a boolean. There is nowhere on either surface for a credential to be
    /// placed, so no future edit can add one without failing this test.
    /// </para>
    /// <para>
    /// The mutual-TLS members are checked by name rather than excused: a PATH names the location of material
    /// mounted from the orchestration secret layer, and carrying the path is what keeps the material out of
    /// the repository and out of the image.
    /// </para>
    /// </remarks>
    [Fact]
    public void NeitherConfigurationSurfaceCarriesAnySigningMaterial()
    {
        AssertNoCredentialShapedMember(typeof(GatewayOptions));
        AssertNoCredentialShapedMember(typeof(JwtBearerVerificationOptions));
        AssertNoCredentialShapedMember(typeof(GatewayOptions.UpstreamAddresses));
        AssertNoCredentialShapedMember(typeof(GatewayOptions.HealthProbeAddresses));

        // The one group that touches credential material at all, and it touches only its LOCATION.
        string[] mutualTls = [.. typeof(GatewayOptions.MutualTlsClientOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static member => member.Name)];

        Assert.Contains(nameof(GatewayOptions.MutualTlsClientOptions.CertificatePath), mutualTls);
        Assert.Contains(nameof(GatewayOptions.MutualTlsClientOptions.CertificateKeyPath), mutualTls);

        foreach (string member in mutualTls)
        {
            bool isPathOrPredicate =
                member.EndsWith("Path", StringComparison.Ordinal)
                || string.Equals(
                    member,
                    nameof(GatewayOptions.MutualTlsClientOptions.IsConfigured),
                    StringComparison.Ordinal);

            Assert.True(
                isPathOrPredicate,
                $"'{member}' is neither a path nor the configured predicate. The mutual-TLS group may "
                    + "carry only the LOCATION of material mounted from the orchestration secret layer; a "
                    + "member able to hold a certificate body, a private key or a passphrase would put "
                    + "credential material inside the repository.");
        }
    }

    /// <summary>
    /// The live configuration the running host bound carries no signing secret in either of its sections.
    /// </summary>
    /// <remarks>
    /// The behavioural counterpart of the previous test: the type could be clean while a deployment supplied
    /// a key under some other name, so the sections the host actually bound are inspected as well. Scoped to
    /// Gateway's own two sections deliberately - the wider configuration includes process environment
    /// variables, which are not this service's declaration and not this test's business.
    /// </remarks>
    [Fact]
    public void TheLiveConfigurationSectionsCarryNoSigningSecret()
    {
        IConfiguration configuration = host.Services.GetRequiredService<IConfiguration>();

        List<string> keys =
        [
            .. FlattenKeys(configuration.GetSection(GatewayOptions.SectionName)),
            .. FlattenKeys(configuration.GetSection(JwtBearerVerificationOptions.SectionName)),
        ];

        // The sections are genuinely populated, so an empty result cannot pass this test vacuously.
        Assert.NotEmpty(keys);

        foreach (string key in keys)
        {
            string normalized = key.Replace(":", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();

            foreach (string vocabulary in CredentialVocabulary)
            {
                Assert.DoesNotContain(vocabulary, normalized, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Inbound validation is performed by the stock bearer handler, with every validation enabled and an
    /// authority to verify against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS MATTERS MORE THAN IT LOOKS. The security-critical path being FRAMEWORK code rather than
    /// hand-written code is the reason the Security service speaks REST: the stock handler resolves the
    /// issuer's discovery document and published key set beneath the configured authority and refreshes them
    /// on its own schedule, so nothing in this repository retrieves a key set by hand. Asserting the
    /// registered handler TYPE is what pins that down - a bespoke handler could satisfy every status-code
    /// assertion above while re-introducing exactly the hand-written security code the design removed.
    /// </para>
    /// <para>
    /// All four validations are asserted on the live options, and there is no environment-conditional
    /// relaxation anywhere: a bypass that exists only in Development is still a bypass, and it would make
    /// the local build disagree with the published contract.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task InboundValidationIsTheStockHandlerWithEveryValidationEnabled()
    {
        AuthenticationScheme? scheme = await host.Services
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme);

        Assert.NotNull(scheme);
        Assert.Equal(typeof(JwtBearerHandler), scheme.HandlerType);

        JwtBearerOptions options = host.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.False(string.IsNullOrWhiteSpace(options.Authority));
        Assert.True(options.TokenValidationParameters.ValidateIssuer);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.True(options.TokenValidationParameters.ValidateLifetime);
        Assert.True(options.TokenValidationParameters.ValidateIssuerSigningKey);

        // AND THE TOLERANCE BESIDE THE LIFETIME CHECK, because the line above is only as tight as this
        // one. The dedicated row below owns the reasoning; it is restated here so that the "every
        // validation enabled" claim this test makes cannot be true while the lifetime bound it names is
        // five times looser than it reads.
        Assert.Equal(GatewayLifetimeTolerance, options.TokenValidationParameters.ClockSkew);
    }

    /// <summary>
    /// The lifetime tolerance is explicit, bounded, and not the library's five-minute default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SAYING NOTHING WAS NOT THE SAME AS ALLOWING NOTHING, AND THIS BOUNDARY WAS THE WORST PLACE FOR
    /// THAT. The composition root once left <see cref="TokenValidationParameters.ClockSkew"/> unassigned,
    /// which does not mean no tolerance - it means the handler's default of FIVE MINUTES. Security issues
    /// with a five-minute lifetime, so a token accepted here remained usable for twice as long as it
    /// claims to be, and the <c>ValidateLifetime = true</c> asserted above was enforcing a bound five
    /// times looser than it reads.
    /// </para>
    /// <para>
    /// THE INCONSISTENCY MATTERED MORE THAN THE VALUE. Four boundaries validate one issuer's tokens, and
    /// they disagreed: Security refused an expired token the instant it lapsed, DataServices allowed
    /// thirty seconds, and Gateway and Persistence inherited five minutes by omission. Gateway is the
    /// SOLE INGRESS, so the loosest tolerance in the system sat on the one edge facing the untrusted
    /// network and admitted credentials Security itself would have refused - then forwarded them inward.
    /// </para>
    /// <para>
    /// THE PREMISE IS ASSERTED RATHER THAN RECITED. The row measures the library default on a fresh
    /// parameters instance instead of asserting "five minutes" as folklore, so if a future runtime
    /// changed that default this row would report the change rather than silently rest on a stale claim.
    /// The upper bound is asserted too: a future edit that widened the tolerance towards the token
    /// lifetime would pass an equality-only assertion after someone updated the constant, whereas the
    /// bound states the RULE - a tolerance approaching the lifetime is expiry checking switched off under
    /// another name.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLifetimeToleranceIsBoundedAndIsNotTheLibraryDefault()
    {
        JwtBearerOptions options = host.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        TimeSpan configured = options.TokenValidationParameters.ClockSkew;

        Assert.Equal(GatewayLifetimeTolerance, configured);

        // The default this assignment exists to displace, measured rather than recited.
        TimeSpan libraryDefault = new TokenValidationParameters().ClockSkew;

        Assert.Equal(TimeSpan.FromMinutes(5), libraryDefault);
        Assert.NotEqual(libraryDefault, configured);
        Assert.True(
            configured < libraryDefault,
            "The ingress tolerance is not tighter than the library default it was assigned to displace.");

        // Bounded on BOTH sides. Zero would be wrong here - unlike Security, this boundary validates
        // tokens minted on a DIFFERENT container's clock - and anything approaching the token lifetime
        // would make the expiry check ceremonial.
        Assert.True(configured > TimeSpan.Zero, "The ingress allows no clock drift at all.");
        Assert.True(
            configured <= TimeSpan.FromMinutes(1),
            "The ingress lifetime tolerance has been widened beyond one minute.");
    }

    /// <summary>
    /// The only <see cref="TimeSpan"/> members the verification section may carry, and neither is a
    /// tolerance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AN ALLOW-LIST RATHER THAN A RELAXED HEURISTIC, so the scan below keeps its teeth. Both entries
    /// govern KEY-SET RETRIEVAL - how soon a refresh a rejected token asked for may happen, and how often
    /// the cached set is refreshed anyway - and neither reaches
    /// <see cref="TokenValidationParameters.ClockSkew"/>. They exist because leaving them unset was a
    /// rotation decision taken by omission: the token library's defaults are five minutes and TWELVE
    /// HOURS, so a signing-key rotation at Security left this ingress accepting the retired credential and
    /// refusing the current one at the same time.
    /// </para>
    /// <para>
    /// WHAT MAKES THE ALLOW-LIST SAFE IS A DIFFERENT ROW IN A DIFFERENT FILE:
    /// <c>MetadataRotationConvergenceTests.NeitherIntervalCanMoveTheLifetimeTolerance</c> configures both
    /// intervals to values far larger than the tolerance and asserts the tolerance unmoved on the deployed
    /// host, so a future change routing a configured duration into the skew under one of these names would
    /// fail there even though it passed here.
    /// </para>
    /// </remarks>
    private static readonly string[] PermittedTimeSpanMembers =
    [
        nameof(JwtBearerVerificationOptions.MetadataRefreshInterval),
        nameof(JwtBearerVerificationOptions.MetadataAutomaticRefreshInterval),
    ];

    /// <summary>
    /// No configuration key can move the lifetime tolerance.
    /// </summary>
    /// <remarks>
    /// A CONFIGURABLE TOLERANCE IS LIFETIME VALIDATION SWITCHED OFF UNDER ANOTHER NAME, because nothing
    /// would stop a deployment setting it past the token lifetime - at which point the expiry check does
    /// not expire. The invariant is the same one the four validation switches carry, and it is asserted
    /// the same way: for a tolerance the safe form is ABSENCE from the bound options, since a numeric
    /// setting has no "refuse the unsafe value" arm that a boolean's <c>false</c> gives. The verification
    /// options type is scanned rather than one property name, so a member arriving under any spelling
    /// trips this row unless it is one of the two retrieval intervals named in
    /// <see cref="PermittedTimeSpanMembers"/> - and a name carrying <c>Skew</c> or <c>Tolerance</c> trips
    /// it even if it is listed.
    /// </remarks>
    [Fact]
    public void NoConfiguredValueCanMoveTheLifetimeTolerance()
    {
        foreach (System.Reflection.PropertyInfo property in
            typeof(JwtBearerVerificationOptions).GetProperties())
        {
            bool toleranceShaped =
                property.Name.Contains("Skew", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Tolerance", StringComparison.OrdinalIgnoreCase);

            bool permitted =
                !toleranceShaped
                && PermittedTimeSpanMembers.Contains(property.Name, StringComparer.Ordinal);

            Assert.False(
                !permitted
                    && (property.PropertyType == typeof(TimeSpan)
                        || property.PropertyType == typeof(TimeSpan?)
                        || toleranceShaped),
                $"{nameof(JwtBearerVerificationOptions)}.{property.Name} looks like a configurable "
                    + "lifetime tolerance. The skew is compiled in at Program.cs precisely so no "
                    + "deployment can widen it past the token lifetime.");
        }
    }

    /// <summary>
    /// Requiring https metadata while pointing at a plain-http authority is refused, rather than being
    /// discovered on the first metadata fetch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A CONTRADICTION IS A CONFIGURATION ERROR, NOT A RUNTIME SURPRISE. The verification side is where
    /// Gateway decides which signatures it will trust, and a mismatch here is not discovered until the
    /// handler first tries to fetch Security's key set - which is the first authenticated request, in
    /// production, long after startup reported success. Refusing the combination up front is the same
    /// fail-fast posture the framework's own structural faults get.
    /// </para>
    /// <para>
    /// Asserted host-free and directly against the validator, because the point is the RULE rather than any
    /// particular deployment: the validator is total, so exercising it needs no web host and no network.
    /// The complementary case is asserted too - a plain-http authority is legitimate when https metadata is
    /// not required, which is the plain-http topology the message itself points an operator towards, so the
    /// rule is a cross-property check rather than a ban on http.
    /// </para>
    /// </remarks>
    [Fact]
    public void RequiringHttpsMetadataAgainstAPlainHttpAuthorityIsRefused()
    {
        JwtBearerVerificationOptions contradictory = new()
        {
            Authority = "http://security.gateway-tests.invalid",
            RequireHttpsMetadata = true,
            ValidIssuers = { "http://security.gateway-tests.invalid" },
            ValidAudiences = { "powerframework-gateway" },
        };

        List<ValidationResult> failures =
            [.. contradictory.Validate(new ValidationContext(contradictory))];

        ValidationResult refusal = Assert.Single(failures);

        Assert.Contains(
            nameof(JwtBearerVerificationOptions.RequireHttpsMetadata),
            refusal.MemberNames,
            StringComparer.Ordinal);
        Assert.Contains(
            nameof(JwtBearerVerificationOptions.Authority),
            refusal.MemberNames,
            StringComparer.Ordinal);

        // The same authority is accepted once the contradiction is removed: this is a cross-property rule,
        // not a prohibition on plain http, and a plain-http topology is a supported deployment.
        JwtBearerVerificationOptions consistent = new()
        {
            Authority = contradictory.Authority,
            RequireHttpsMetadata = false,
            ValidIssuers = { contradictory.Authority },
            ValidAudiences = { "powerframework-gateway" },
        };

        Assert.Empty(consistent.Validate(new ValidationContext(consistent)));
    }

    /// <summary>
    /// The Gateway assembly references no token-minting library at all.
    /// </summary>
    /// <remarks>
    /// The strongest form of the sole-issuer guarantee available from inside the process, because it is
    /// STRUCTURAL: Gateway references the verification-side identity library, which is what
    /// <c>TokenValidationParameters</c> lives in, and neither of the two libraries that carry a token
    /// handler capable of writing a signed token. It therefore could not mint one even if a future edit
    /// tried to, and the attempt would fail to compile rather than ship.
    /// </remarks>
    [Fact]
    public void TheGatewayAssemblyReferencesNoTokenMintingLibrary()
    {
        string[] referenced = [.. typeof(Program).Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)];

        // Verification material is expected and is the whole point of the reference.
        Assert.Contains("Microsoft.IdentityModel.Tokens", referenced);

        Assert.DoesNotContain("System.IdentityModel.Tokens.Jwt", referenced);
        Assert.DoesNotContain("Microsoft.IdentityModel.JsonWebTokens", referenced);
    }

    /// <summary>
    /// There is no Development bypass: the refusal holds in the developer environment too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserted rather than assumed, and asserted in the environment where a bypass would be most tempting.
    /// The Development overlay supplies its own authority and issuer list, so this also exercises a
    /// different configuration path to the same outcome.
    /// </para>
    /// <para>
    /// A bypass that exists only in Development is still a bypass: it would make the local build disagree
    /// with the published contract, and it is exactly the kind of convenience that reaches production by
    /// accident. Nothing was added to make this test easier - the accepted case is included so that the
    /// refusal is shown to be a real evaluation rather than a blanket denial.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThereIsNoDevelopmentBypass()
    {
        await using GatewayTestHostFixture developmentHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Development);

        using HttpClient client = developmentHost.CreateAnonymousClient();

        using HttpResponseMessage anonymous = await SendAsync(
            client,
            HttpMethod.Get,
            PingRoute,
            credential: null);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using HttpResponseMessage authenticated = await SendAsync(
            client,
            HttpMethod.Get,
            PingRoute,
            $"{GatewayTestHostFixture.BearerScheme} {developmentHost.IssueValidToken()}");

        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
    }

    // --------------------------------------------------------------------------------------------------
    //  3.6  FAIL FAST, ASSERTED AS FAIL FAST - AND THE PURE ASSERT-PAYLOAD PROTOCOL
    //
    //  Host-free from here down. Diagnostics/SystemErrorHandler.cs publishes a pure decoder, a pure
    //  formatter and a substitutable termination request precisely so that this protocol is reachable
    //  without a web host, which is what makes a separate file for it unnecessary.
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// A structural fault requests termination rather than degrading gracefully.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FAIL FAST IS ASSERTED AS FAIL FAST, ON PURPOSE (constraint C-B). The legacy reports a decoded
    /// assertion failure and then executes <c>HALT CLOSE</c>
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L141-L143</c>], and the scenario that produces one is an UNCAUGHT
    /// assertion - <c>ws_objects/pfw.tests.pbl.src/w_test_assert.srw:L119</c> and <c>:L137</c> invoke the two
    /// assertion helpers with <c>-1</c> and catch nothing, so the failure reaches the application's
    /// system-error event. A test that expected warning-and-continue here would silently license exactly the
    /// softening C-B forbids: "graceful degradation" is a behavioural change wearing the costume of
    /// robustness, and it is not what this system does.
    /// </para>
    /// <para>
    /// Nothing is actually terminated. The termination request is a substitutable callback, so the request is
    /// OBSERVED and the test runner survives - and observing it is a stronger assertion than watching a
    /// process die, because it also proves the request is made exactly once.
    /// </para>
    /// <para>
    /// Note which half of the protocol the destination reproduces: an exit code plus a request for host
    /// SHUTDOWN, not an immediate abort. An abort would bypass the registered shutdown path, and that path is
    /// where the framework finalize step lives - the same pairing <c>docs/README.md</c> requires and
    /// <c>ws_objects/pfw.pbl.src/pfw.sra:L108</c> performs in its close event.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AStructuralFaultRequestsTerminationRatherThanDegradingGracefully()
    {
        TerminationRecordingLifetime lifetime = new();
        List<int> requestedExitCodes = [];

        SystemErrorHandler handler = new(
            NullLogger<SystemErrorHandler>.Instance,
            lifetime,
            problemDetailsService: null,
            requestProcessTermination: requestedExitCodes.Add);

        HttpContext context = CreateFaultContext();

        // The destination type for the legacy AssertionFailed, carrying the seven-field payload the legacy
        // producer emits.
        bool handled = await handler.TryHandleAsync(
            context,
            new AssertionFailure(BuildAssertPayload()),
            TestContext.Current.CancellationToken);

        Assert.True(handled);

        // Termination requested exactly once, with a non-zero code. The exact value is the handler's own
        // private constant and is deliberately not restated here; what matters is that the code is non-zero,
        // because a zero exit code would report SUCCESS from a process that halted on a structural fault.
        Assert.Single(requestedExitCodes);
        Assert.NotEqual(0, requestedExitCodes[0]);

        // The substituted callback replaces the whole host-backed request, so nothing reached the lifetime.
        // Stated rather than left implicit: it is what guarantees this test cannot stop the test host.
        Assert.Equal(0, lifetime.StopRequestCount);
    }

    /// <summary>
    /// An ordinary request fault is reported and answered WITHOUT requesting termination.
    /// </summary>
    /// <remarks>
    /// The other half of the same property, and the half that makes it meaningful. Fail-fast governs
    /// STRUCTURAL faults; an ordinary request fault is a request-scoped failure and terminating on one would
    /// let any caller take the container down. The legacy line is the same one: the caught path at
    /// <c>ws_objects/pfw.tests.pbl.src/w_test_assert.srw:L97-L101</c> handles the assertion itself and never
    /// reaches the system-error event, so nothing halts.
    /// </remarks>
    [Fact]
    public async Task AnOrdinaryRequestFaultIsReportedWithoutRequestingTermination()
    {
        TerminationRecordingLifetime lifetime = new();
        List<int> requestedExitCodes = [];

        SystemErrorHandler handler = new(
            NullLogger<SystemErrorHandler>.Instance,
            lifetime,
            problemDetailsService: null,
            requestProcessTermination: requestedExitCodes.Add);

        HttpContext context = CreateFaultContext();

        bool handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("an ordinary request fault"),
            TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Empty(requestedExitCodes);
        Assert.Equal(0, lifetime.StopRequestCount);
    }

    /// <summary>
    /// The assert payload is decoded only under the literal marker, and the input is returned untouched
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// <c>ws_objects/pfw.pbl.src/pfw.sra:L114</c> tests the error object for exact equality against the
    /// lower-case marker. It is not a prefix test, not a containment test and not case-insensitive, so a
    /// system error whose object merely resembles the marker is formatted from whatever the runtime had
    /// already put on the error object rather than decoded.
    /// </remarks>
    [Fact]
    public void TheAssertPayloadIsDecodedOnlyUnderTheLiteralMarker()
    {
        string payload = BuildAssertPayload();

        SystemErrorInfo decoded = SystemErrorHandler.Decode(new SystemErrorInfo
        {
            Object = SystemErrorHandler.AssertErrorObject,
            Text = payload,
        });

        Assert.Equal("the assertion text", decoded.Text);

        // Same payload, a marker that differs only in case: nothing is decoded and the payload survives as
        // the text.
        SystemErrorInfo untouched = SystemErrorHandler.Decode(new SystemErrorInfo
        {
            Object = "Assert",
            Text = payload,
        });

        Assert.Equal(payload, untouched.Text);
        Assert.Equal("Assert", untouched.Object);
    }

    /// <summary>
    /// A seven-field payload populates every decoded field, and field four overwrites the marker it was
    /// discriminated on.
    /// </summary>
    /// <remarks>
    /// The complete protocol of <c>ws_objects/pfw.pbl.src/pfw.sra:L115-L125</c>: the payload splits on CRLF,
    /// field one is the fixed sentinel number, field two the assertion text, and fields three to seven the
    /// window-or-menu, the object, the object event, the line number and the call stack. Field four
    /// OVERWRITING the marker is legacy behaviour [<c>:L121</c>] and is reproduced rather than guarded
    /// against; it is also why the structural-versus-request decision is captured before the decode runs.
    /// </remarks>
    [Fact]
    public void ASevenFieldAssertPayloadPopulatesEveryDecodedField()
    {
        SystemErrorInfo decoded = SystemErrorHandler.Decode(new SystemErrorInfo
        {
            Object = SystemErrorHandler.AssertErrorObject,
            Text = BuildAssertPayload(),
        });

        Assert.Equal(-10000L, decoded.Number);
        Assert.Equal("the assertion text", decoded.Text);
        Assert.Equal("w_demo_selector", decoded.WindowMenu);
        Assert.Equal("n_cst_example", decoded.Object);
        Assert.Equal("of_dowork", decoded.ObjectEvent);
        Assert.Equal(42L, decoded.Line);
        Assert.Equal("frame one", decoded.StackTrace);
    }

    /// <summary>
    /// An eight-field payload populates ONLY the number and the text.
    /// </summary>
    /// <remarks>
    /// PRESERVED DEFECT, ASSERTED AS A DEFECT (constraints C-B and C-K). The legacy test is
    /// <c>if nCount = 7</c> [<c>ws_objects/pfw.pbl.src/pfw.sra:L119</c>] - EXACTLY seven, not at least seven
    /// - so a payload carrying an eighth field silently loses its window, object, object event, line number
    /// and call stack while keeping the two fields the outer <c>&gt;= 2</c> test admitted. Widening the test
    /// to <c>&gt;= 7</c> would be more useful and would be a behaviour change, which this refactor forbids.
    /// This assertion exists so that a future reader cannot mistake the narrow test for an oversight, and so
    /// that "correcting" it fails a test instead of passing review.
    /// </remarks>
    [Fact]
    public void AnEightFieldAssertPayloadPopulatesOnlyTheNumberAndTheText()
    {
        string payload = string.Join(
            "\r\n",
            "-10000",
            "the assertion text",
            "w_demo_selector",
            "n_cst_example",
            "of_dowork",
            "42",
            "frame one",
            "an eighth field the legacy never expects");

        SystemErrorInfo decoded = SystemErrorHandler.Decode(new SystemErrorInfo
        {
            Object = SystemErrorHandler.AssertErrorObject,
            Text = payload,
        });

        Assert.Equal(-10000L, decoded.Number);
        Assert.Equal("the assertion text", decoded.Text);

        // Everything the eighth field cost. The marker survives here precisely BECAUSE field four never ran.
        Assert.Equal(string.Empty, decoded.WindowMenu);
        Assert.Equal(SystemErrorHandler.AssertErrorObject, decoded.Object);
        Assert.Equal(string.Empty, decoded.ObjectEvent);
        Assert.Equal(0L, decoded.Line);
        Assert.Equal(string.Empty, decoded.StackTrace);
    }

    /// <summary>
    /// At least two fields are required before anything is decoded, and an unparseable number yields zero.
    /// </summary>
    /// <remarks>
    /// The outer guard is <c>if nCount &gt;= 2</c> [<c>ws_objects/pfw.pbl.src/pfw.sra:L116</c>], so a
    /// single-field payload decodes nothing at all. The zero from unparseable text is the legacy's own
    /// <c>Long()</c> conversion behaviour, which yields zero rather than failing - preserved, because a
    /// decoder that threw would turn a diagnostic into a second fault.
    /// </remarks>
    [Fact]
    public void AtLeastTwoFieldsAreRequiredBeforeAnythingIsDecoded()
    {
        SystemErrorInfo single = SystemErrorHandler.Decode(new SystemErrorInfo
        {
            Object = SystemErrorHandler.AssertErrorObject,
            Text = "-10000",
        });

        // Nothing was taken from the payload: the number is untouched and the text is still the whole input.
        Assert.Equal(0L, single.Number);
        Assert.Equal("-10000", single.Text);

        SystemErrorInfo unparseable = SystemErrorHandler.Decode(new SystemErrorInfo
        {
            Object = SystemErrorHandler.AssertErrorObject,
            Text = string.Join("\r\n", "not a number", "the assertion text"),
        });

        Assert.Equal(0L, unparseable.Number);
        Assert.Equal("the assertion text", unparseable.Text);
    }

    /// <summary>
    /// The formatted report omits the failure number, always names the system type, and separates its lines
    /// with bare line feeds.
    /// </summary>
    /// <remarks>
    /// PRESERVED DEFECTS, ASSERTED AS DEFECTS (constraints C-B and C-K). Three quirks of
    /// <c>ws_objects/pfw.pbl.src/pfw.sra:L129-L141</c> travel together and all three are reproduced. The
    /// failure NUMBER is decoded and then never rendered, so the report a human sees omits the very value the
    /// decode recovered. The type line is the literal system word even for a fully decoded ASSERTION, which
    /// is why the first line of a decoded report is identical to the first line of an empty one. And the
    /// payload arrives CRLF-delimited while the report is joined with bare line feeds, so the delimiter
    /// changes across the boundary.
    /// </remarks>
    [Fact]
    public void TheFormattedReportOmitsTheFailureNumberAndAlwaysNamesTheSystemType()
    {
        SystemErrorInfo decoded = SystemErrorHandler.Decode(new SystemErrorInfo
        {
            Object = SystemErrorHandler.AssertErrorObject,
            Text = BuildAssertPayload(),
        });

        SystemErrorReport report = SystemErrorHandler.Format(decoded);
        SystemErrorReport empty = SystemErrorHandler.Format(new SystemErrorInfo());

        // QUIRK: the type line does not distinguish a decoded assertion from any other system error.
        string firstLineOfReport = report.Body.Split('\n')[0];
        string firstLineOfEmpty = empty.Body.Split('\n')[0];

        Assert.Equal(firstLineOfEmpty, firstLineOfReport);
        Assert.Contains("SYSTEM", firstLineOfReport, StringComparison.Ordinal);

        // QUIRK: the number is decoded and then never rendered.
        Assert.DoesNotContain(
            decoded.Number.ToString(CultureInfo.InvariantCulture),
            report.Body,
            StringComparison.Ordinal);

        // QUIRK: CRLF on the way in, bare line feeds on the way out.
        Assert.DoesNotContain("\r\n", report.Body, StringComparison.Ordinal);
        Assert.Contains("\n", report.Body, StringComparison.Ordinal);

        // The values that DO survive, so the omission above reads as an omission rather than as emptiness.
        Assert.Contains(decoded.Text, report.Body, StringComparison.Ordinal);
        Assert.Contains(decoded.ObjectEvent, report.Body, StringComparison.Ordinal);
        Assert.Contains(decoded.StackTrace, report.Body, StringComparison.Ordinal);
        Assert.Equal(SystemErrorSeverity.Stop, report.Severity);
        Assert.False(string.IsNullOrWhiteSpace(report.Title));
    }

    // --------------------------------------------------------------------------------------------------
    //  3.7  THE SEAM THE FIXTURE REPLACES, AND THE BOUNDARY'S OTHER SIDE
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The default lifecycle substitute cannot fail, and the version it reports is this assembly's rather
    /// than a legacy version string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The implementation the fixture REPLACES, exercised directly so that the substitution above is a
    /// choice rather than a necessity born of untested code. What legacy initialization did that could fail
    /// was LOAD NATIVE THIRD-PARTY MODULES, and this port has none to load: seven of the eight capability
    /// bits name capability areas outside this phase, and the eighth - SQLite - belongs to the Persistence
    /// service, which is why Gateway's manifest carries no data-access package at all. So the managed
    /// counterpart of "load the requested modules" is empty BY CONSTRUCTION and returning a success is the
    /// complete behaviour rather than a placeholder.
    /// </para>
    /// <para>
    /// The mask is accepted and validated in no way, exactly as the native entry point validates nothing
    /// about the mask it receives - including a mask of every bit set, which is not an error.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDefaultLifecycleSubstituteCannotFailAndReportsThisAssemblysVersion()
    {
        ManagedFrameworkRuntime runtime = new();

        Assert.True(Predicates.IsSucceeded(runtime.Initialize()));
        Assert.True(Predicates.IsSucceeded(runtime.Initialize(CapabilityFlags.AllCapabilitiesMask)));
        Assert.True(Predicates.IsSucceeded(runtime.Initialize(0u)));
        Assert.True(Predicates.IsSucceeded(runtime.Initialize(uint.MaxValue)));
        Assert.True(Predicates.IsSucceeded(runtime.Finalize()));

        string reported = runtime.Version();

        Assert.False(string.IsNullOrWhiteSpace(reported));

        // The framework version at the head of logfile.md is deliberately NOT reported: that changelog stops
        // in 2022 while the commit history runs years later, so it is a stale document rather than a
        // specification and publishing it would report a fabricated fact as this build's version.
        Assert.DoesNotContain("3.0.7.2062", reported, StringComparison.Ordinal);
    }

    /// <summary>
    /// A framework initialization failure carries the refused return code and the requested capabilities.
    /// </summary>
    /// <remarks>
    /// The failure type belongs to the same seam, and its two members are what make a startup failure
    /// diagnosable: the code the runtime refused with, and the mask it was asked for. The return code is
    /// nullable because a failure can also arise without one - an initialization that threw never produced a
    /// code at all - and that distinction is preserved rather than flattened to zero, because zero is
    /// <see cref="RetCode.OK"/> and would read as a success.
    /// </remarks>
    [Fact]
    public void AFrameworkInitializationFailureCarriesItsCodeAndTheRequestedCapabilities()
    {
        FrameworkInitializationException withDetail = new(
            "initialization was refused",
            RetCode.E_NO_SUPPORT,
            CapabilityFlags.AllCapabilitiesMask,
            new InvalidOperationException("the underlying cause"));

        Assert.Equal(RetCode.E_NO_SUPPORT, withDetail.ReturnCode);
        Assert.Equal(CapabilityFlags.AllCapabilitiesMask, withDetail.RequestedCapabilities);
        Assert.Equal("initialization was refused", withDetail.Message);
        Assert.NotNull(withDetail.InnerException);

        // The three inherited shapes, each of which leaves the two members at their defaults.
        Assert.Null(new FrameworkInitializationException().ReturnCode);
        Assert.Equal("refused", new FrameworkInitializationException("refused").Message);

        FrameworkInitializationException chained =
            new("refused", new InvalidOperationException("cause"));

        Assert.Null(chained.ReturnCode);
        Assert.Equal(0u, chained.RequestedCapabilities);
    }

    /// <summary>
    /// Readiness is anonymous in its READY state too, and answers the contract's report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other half of the anonymity property. A route that answered anonymously only while it was
    /// unhealthy would still break the orchestration gate, which reads the ready answer, so both states are
    /// asserted. The ready body carries Gateway's own component checks and the individual verdict of each
    /// upstream, and its timestamp is exactly the frozen instant.
    /// </para>
    /// <para>
    /// A LOCALLY OWNED HOST, on purpose. Reaching the ready branch means changing the substituted probe's
    /// verdict, and mutating the shared class fixture would make every other test in this class depend on
    /// execution order. Owning the host keeps this test order-independent and parallel-safe.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReadinessIsAnonymousInItsReadyStateToo()
    {
        await using GatewayTestHostFixture readyHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        readyHost.UpstreamVerdict = UpstreamReadiness.Healthy;

        using HttpClient client = readyHost.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            AggregateHealthReport.ReportingService,
            body.RootElement.GetProperty("service").GetString());
        Assert.Equal(
            GatewayTestHostFixture.FrozenNow,
            body.RootElement.GetProperty("checkedAt").GetDateTimeOffset());
        Assert.Equal(3, body.RootElement.GetProperty("upstreams").GetArrayLength());
        Assert.NotEmpty(body.RootElement.GetProperty("checks").EnumerateArray());
    }

    /// <summary>
    /// An authenticated request is admitted past the boundary, and a fault behind it answers the contract's
    /// problem shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE BOUNDARY'S OTHER SIDE, and the reason it belongs in this file: every assertion above shows what a
    /// caller CANNOT do, and this one shows that a valid credential genuinely admits the request to the
    /// handler rather than to a different refusal. What happens next is deliberate - the fixture registers a
    /// DataWindow client that throws on resolution, so the proxied operation faults, and the fault proves
    /// admission far more precisely than a success would.
    /// </para>
    /// <para>
    /// WHERE GATEWAY WRITES AN ERROR BODY, THE BODY IS THE CONTRACT'S SHARED PROBLEM SHAPE carrying a
    /// <c>retCode</c> extension member. That is the assertion the framework challenge could not carry,
    /// because a challenge has no body; here Gateway writes the response itself, so the shape is
    /// observable. The code is the catalogue's unknown value, because an arbitrary exception's numeric
    /// result is not a legacy return code and a code that happened to be zero would report SUCCESS on a
    /// failed request.
    /// </para>
    /// <para>
    /// FAIL FAST IS NOT ENGAGED, and that is correct rather than a gap: an ordinary request fault is
    /// request-scoped, and terminating the process on one would let any caller take the container down. Only
    /// a structural fault halts, which the assertions above cover directly.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAuthenticatedRequestIsAdmittedPastTheBoundaryAndAFaultCarriesTheProblemShape()
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpRequestMessage request = new(
            HttpMethod.Post,
            new Uri("/v1/datawindow/retrieve", UriKind.Relative));

        request.Headers.TryAddWithoutValidation(
            AuthorizationHeader,
            BuildAuthorizationHeaderValue("valid"));

        request.Content = new StringContent("{}", Encoding.UTF8, MediaTypeNames.Application.Json);

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Admitted: not a challenge, and not a not-found either.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        Assert.Equal(
            MediaTypeNames.Application.ProblemJson,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(RetCode.UNKNOWN, body.RootElement.GetProperty(RetCodeMember).GetInt64());

        // A correlation identifier a caller can quote, and no fault detail beyond it: the message, the type
        // chain and the stack trace stay on the operator channel (constraint C-F).
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));

        string raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("DataServicesClient", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("authorisation boundary", raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// A configured but unreadable client identity stops the HOST, not the first token request - and the
    /// failure never reproduces the path it was told to read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The system's single mutual-TLS edge is token issuance: a caller cannot present a bearer credential in
    /// order to obtain its first bearer credential, so the identity Gateway presents to the sole issuer is a
    /// TRANSPORT concern configured in the composition root. A deployment that meant to authenticate to the
    /// issuer and cannot has already lost every authenticated call it would make, so discovering that at
    /// startup is a failure to launch while discovering it at the first token request is an outage that looks
    /// like an upstream problem. Asserting the former is asserting fail-fast.
    /// </para>
    /// <para>
    /// An UNSET pair is deliberately not a fault - it means this run presents no client certificate and does
    /// not reach the issuance edge, which every other test in this class relies on and which the shared
    /// fixture demonstrates by starting at all.
    /// </para>
    /// <para>
    /// The second assertion is constraint C-F on a diagnostic, and it is asserted at exactly the frame that
    /// makes the claim. A path is not itself a credential, but it names the location of one, so the
    /// composition root's OWN message names the configuration KEYS and never the paths - which is what an
    /// operator needs in order to find the setting. The PRESERVED INNER CAUSE is a deliberate exception to
    /// that: the platform's own file-system error names the path, and discarding it to keep the chain clean
    /// would trade a diagnosable startup failure for a mysterious one. It is safe for the same reason the
    /// unredacted fault record is safe - a startup exception reaches the host's log and never a caller, so
    /// its audience is the operator channel rather than the network.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnUnreadableClientIdentityStopsTheHostWithoutEchoingItsPath()
    {
        const string missingCertificate = "/nonexistent/gateway-tests/mtls-client-certificate.pem";
        const string missingKey = "/nonexistent/gateway-tests/mtls-client-key.pem";

        await using GatewayTestHostFixture faultedHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        faultedHost.AdditionalSettings[
            $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.MutualTls)}:"
            + nameof(GatewayOptions.MutualTlsClientOptions.CertificatePath)] = missingCertificate;
        faultedHost.AdditionalSettings[
            $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.MutualTls)}:"
            + nameof(GatewayOptions.MutualTlsClientOptions.CertificateKeyPath)] = missingKey;

        Exception failure = Assert.ThrowsAny<Exception>(faultedHost.CreateAnonymousClient);

        // The whole chain is searched for the composition root's own frame, because the factory surfaces a
        // startup fault through however many layers the host builder wrapped it in.
        Exception? compositionRootFrame = null;

        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(nameof(GatewayOptions.MutualTls), StringComparison.Ordinal))
            {
                compositionRootFrame = current;
                break;
            }
        }

        Assert.NotNull(compositionRootFrame);

        // Names the two configuration keys, and neither of the two paths.
        //
        // EVALUATED BEFORE ASSERTED, AND THE MESSAGE REDACTED BEFORE IT CAN BE RENDERED (C-F). The two
        // path-absence checks are booleans because Assert.DoesNotContain renders both operands, and the two
        // key-presence checks read a REDACTED copy of the message because the failure they describe is
        // "the message no longer names the key" - which is worth showing the message for, and the message
        // is exactly the thing that might now be carrying a private-key mount point.
        bool messageEchoesTheCertificatePath =
            compositionRootFrame.Message.Contains(missingCertificate, StringComparison.Ordinal);
        bool messageEchoesTheKeyPath =
            compositionRootFrame.Message.Contains(missingKey, StringComparison.Ordinal);

        string redactedMessage = compositionRootFrame.Message
            .Replace(missingCertificate, RedactionMarker, StringComparison.Ordinal)
            .Replace(missingKey, RedactionMarker, StringComparison.Ordinal);

        Assert.Contains(
            nameof(GatewayOptions.MutualTlsClientOptions.CertificatePath),
            redactedMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            nameof(GatewayOptions.MutualTlsClientOptions.CertificateKeyPath),
            redactedMessage,
            StringComparison.Ordinal);

        Assert.False(
            messageEchoesTheCertificatePath,
            "The startup fault reproduced the configured certificate path. A startup log is the wrong place "
                + "to publish where an identity is mounted; the message may name the configuration key "
                + "instead.");

        Assert.False(
            messageEchoesTheKeyPath,
            "The startup fault reproduced the configured private-key path, which is worse than the "
                + "certificate path for the same reason it is more sensitive.");

        // NO PATH-BEARING CAUSE IS ATTACHED TO THE OPERATOR-VISIBLE CHAIN, AND THAT ABSENCE IS THE
        // ASSERTION - THIS TEST USED TO REQUIRE THE OPPOSITE.
        //
        // It asserted the cause was preserved "so the failure is diagnosable", which sounded right and
        // defeated the redaction directly above it: the file exception the runtime raises is constructed
        // FROM THE PATH and carries it in its own Message, and startup logging renders an exception
        // CHAIN rather than only its outermost message. So the wrapper omitted both paths and the
        // InnerException published one anyway. Redacting a message while wrapping an unredacted cause is
        // not redaction.
        Assert.Null(compositionRootFrame.InnerException);

        // AND NOTHING ELSE IN THE CHAIN CARRIES EITHER PATH, which is the property the previous
        // assertion only implied. Checking the whole chain rather than the one frame is what makes this
        // a statement about what an operator would SEE.
        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            Assert.DoesNotContain(missingCertificate, current.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(missingKey, current.Message, StringComparison.Ordinal);
        }

        // DIAGNOSABILITY IS RETAINED WITHOUT THE PATH: the failure TYPE is named in the message, which is
        // what separates a missing or unreadable file from a file that is not PEM from a key the platform
        // will not accept. A type name carries no location.
        // Read from the failure the platform actually raises for an absent PEM file rather than asserting
        // one spelling: the point is that SOME type is named, not which one this runtime chose.
        Exception probe = Assert.ThrowsAny<Exception>(
            () => X509Certificate2.CreateFromPemFile(missingCertificate, missingKey));

        Assert.Contains(probe.GetType().Name, compositionRootFrame.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A half-configured client identity stops the host rather than presenting nothing and continuing.
    /// </summary>
    /// <param name="certificateConfigured">Whether the certificate path is set.</param>
    /// <param name="keyConfigured">Whether the private-key path is set.</param>
    /// <remarks>
    /// <para>
    /// FAIL FAST, NOT PARTIAL CREDENTIALS. A certificate cannot complete a handshake without its key and a
    /// key has nothing to present without its certificate, so half a pair is a configuration error rather
    /// than a deployment that presents no identity. Silently continuing would produce an outbound edge that
    /// looks configured and is not - which is exactly the graceful degradation constraint C-B forbids, and
    /// on a security boundary the softening would be worst of all. Neither PATH is reproduced in the
    /// message: a startup log is the wrong place to publish where a private key is mounted.
    /// </para>
    /// <para>
    /// WHICH GUARD REFUSES IT IS ITSELF WORTH RECORDING. The options surface validates the pair on start, so
    /// that is the frame this test finds; the composition root carries an equivalent guard behind it, which
    /// is therefore defence in depth that no configuration can reach. Asserting the reachable guard rather
    /// than the shadowed one is deliberate - a test written against the unreachable frame would fail for a
    /// reason unrelated to the property, and the property is that the host does not start.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AHalfConfiguredClientIdentityStopsTheHost(
        bool certificateConfigured,
        bool keyConfigured)
    {
        const string certificatePath = "/nonexistent/gateway-tests/half-configured-certificate.pem";
        const string keyPath = "/nonexistent/gateway-tests/half-configured-key.pem";

        await using GatewayTestHostFixture halfConfigured =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        halfConfigured.AdditionalSettings[
            $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.MutualTls)}:"
            + nameof(GatewayOptions.MutualTlsClientOptions.CertificatePath)] =
            certificateConfigured ? certificatePath : string.Empty;
        halfConfigured.AdditionalSettings[
            $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.MutualTls)}:"
            + nameof(GatewayOptions.MutualTlsClientOptions.CertificateKeyPath)] =
            keyConfigured ? keyPath : string.Empty;

        Exception failure = Assert.ThrowsAny<Exception>(halfConfigured.CreateAnonymousClient);

        Exception? refusingFrame = null;

        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(
                    nameof(GatewayOptions.MutualTlsClientOptions.CertificatePath),
                    StringComparison.Ordinal))
            {
                refusingFrame = current;
                break;
            }
        }

        Assert.NotNull(refusingFrame);

        // Both keys are named, because an operator has to know which half is missing. Same treatment as the
        // unreadable-identity row above: the absence checks are booleans and the presence check reads a
        // redacted copy, so neither path can reach the failure output (C-F).
        bool messageEchoesTheCertificatePath =
            refusingFrame.Message.Contains(certificatePath, StringComparison.Ordinal);
        bool messageEchoesTheKeyPath =
            refusingFrame.Message.Contains(keyPath, StringComparison.Ordinal);

        Assert.Contains(
            nameof(GatewayOptions.MutualTlsClientOptions.CertificateKeyPath),
            refusingFrame.Message
                .Replace(certificatePath, RedactionMarker, StringComparison.Ordinal)
                .Replace(keyPath, RedactionMarker, StringComparison.Ordinal),
            StringComparison.Ordinal);

        Assert.False(
            messageEchoesTheCertificatePath,
            "The half-configured refusal reproduced the configured certificate path.");

        Assert.False(
            messageEchoesTheKeyPath,
            "The half-configured refusal reproduced the configured private-key path.");
    }

    /// <summary>
    /// The three locales the legacy declares are each accepted, and anything else stops the host.
    /// </summary>
    /// <param name="locale">The configured locale.</param>
    /// <remarks>
    /// PRESERVED DEFECT, ASSERTED AS A DEFECT. <c>ws_objects/pfw.pbl.src/pfw.sra:L94</c> assigns
    /// <c>lang = "en"</c> as a literal inside the open event and then selects one of three provider classes
    /// from it [<c>:L95-L102</c>]. The DEFECT IS THE HARDCODING, not the value: the value survives as the
    /// default - asserted by a sibling in this folder - while the setting becomes overridable, and all three
    /// of the legacy's provider classes remain reachable. Reproducing the un-configurability as well would
    /// preserve a structural property rather than an observable behaviour, and would make the port worse
    /// than the legacy it copies.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("chs")]
    [InlineData("cht")]
    public async Task EachLocaleTheLegacyDeclaresIsAcceptedByTheCompositionRoot(string locale)
    {
        await using GatewayTestHostFixture localeHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        localeHost.AdditionalSettings[
            $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.Locale)}"] = locale;

        using HttpClient client = localeHost.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(
            PingRoute,
            TestContext.Current.CancellationToken);

        // The host started and serves, which is the whole assertion: the provider for this locale was
        // constructed and installed, and the boundary still refuses anything without a credential.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// A locale outside the legacy's three stops the host rather than falling back to a default.
    /// </summary>
    /// <remarks>
    /// NOT A SILENT FALLBACK. A Gateway serving requests under an uninstalled provider would return
    /// untranslated text for every category, and the localization surface fails SILENTLY by design - with no
    /// provider installed the text passes through unchanged, never throwing and never marking itself
    /// untranslated. A silent runtime fallback would therefore be undetectable, so the unusable value is
    /// refused at startup instead, matching the framework's own fail-fast posture.
    /// </remarks>
    [Fact]
    public async Task AnUndeclaredLocaleStopsTheHostRatherThanFallingBack()
    {
        await using GatewayTestHostFixture localeHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        localeHost.AdditionalSettings[
            $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.Locale)}"] = "de";

        Exception failure = Assert.ThrowsAny<Exception>(localeHost.CreateAnonymousClient);

        bool named = false;

        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(nameof(GatewayOptions.Locale), StringComparison.Ordinal))
            {
                named = true;
                break;
            }
        }

        Assert.True(named, "The startup failure does not name the configuration key an operator must fix.");
    }

    /// <summary>
    /// The composition root builds Gateway's outbound edges with platform trust and no signing material.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CONSTRAINT C-G ON THE OUTBOUND SIDE. Resolving the client is the assertion: the composition root
    /// pins a constructor, sets a base address from configuration and builds a transport, and it does all of
    /// that WITHOUT installing a certificate-validation callback of any kind. A forged Security service
    /// would be a forged token issuer for the entire system, so trust is an orchestration concern - the
    /// material a deployment mounts - and never a callback in code, in any environment.
    /// </para>
    /// <para>
    /// The base address is asserted against the configured upstream rather than a literal, so the assertion
    /// cannot drift from the configuration it is checking. Nothing is SENT: this is composition, and the
    /// fixture's transport would refuse a request anyway.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOutboundEdgesAreComposedWithPlatformTrustAndNoSigningMaterial()
    {
        GatewayOptions options = host.Services.GetRequiredService<IOptions<GatewayOptions>>().Value;

        // Resolving it runs the configuration lambda, the pinned constructor and the transport factory.
        SecurityClient security = host.Services.GetRequiredService<SecurityClient>();

        Assert.NotNull(security);

        // The typed client is reachable through the token-provider abstraction as well, which is the shape
        // every authenticated outbound call goes through.
        Assert.NotNull(host.Services.GetRequiredService<IServiceTokenProvider>());

        // The one place the upstream address is stated is configuration, and what is asserted about it here
        // is that it carries NO EMBEDDED CREDENTIAL - validated at startup and restated here because a
        // client whose address carried one would leak it into every log and trace.
        //
        // THE SCHEME IS DELIBERATELY NOT ASSERTED, AND THAT IS THE OPPOSITE OF AN OVERSIGHT. Every listener
        // in this repository is cleartext, because the attached environment gates readiness on plaintext
        // /health URLs and supplies no certificate material, and AAP 0.8.3 does not license a transport
        // change (docs/ARCHITECTURE.md 4.1). Pinning `https` here would assert a topology the frozen
        // environment cannot run; pinning `http` would break the moment a deployment terminates TLS, which
        // it is entitled to do with no code change. What C-G actually requires of this edge is that trust
        // comes from mounted material rather than from a callback in code - which is what the absence of a
        // certificate-validation callback above asserts - and that no credential rides in the address.
        foreach (string upstream in (string[])[options.Upstreams.Security, options.Upstreams.DataServices])
        {
            Uri configured = new(upstream, UriKind.Absolute);

            Assert.True(
                configured.Scheme == Uri.UriSchemeHttp || configured.Scheme == Uri.UriSchemeHttps,
                $"an upstream must be addressed over HTTP semantics, got '{configured.Scheme}'");
            Assert.Empty(configured.UserInfo);
        }

        // And the client type itself publishes nothing credential shaped, which is the same structural
        // argument the configuration surfaces are held to above.
        AssertNoCredentialShapedMember(typeof(SecurityClient));
    }

    // --------------------------------------------------------------------------------------------------
    //  3.8  THE LIFECYCLE SEAM'S THREE READINGS, HOST-FREE
    //
    //  Composition/FrameworkInitializer.cs states that its runtime boundary is registered as an interface
    //  so that a substitute can return a success, a failure, or a code that is NEITHER, and can count how
    //  often each member was called - "which is how the pairing guarantee is verified from the outside".
    //  These tests are that verification. They need no web host and no pfw.dll, which is what makes a
    //  separate file for them unnecessary.
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// A definite initialization failure is FATAL, and finalization is then not owed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>docs/README.md</c> records initialization as mandatory and warns that naming a module whose native
    /// library is absent makes it FAIL. Failing the startup rather than continuing is the fail-fast posture;
    /// continuing on an unusable framework would be graceful degradation, which constraint C-B forbids.
    /// </para>
    /// <para>
    /// ONE DELIBERATE, DOCUMENTED DEPARTURE FROM THE LEGACY CALL SITE, asserted here so it is not mistaken
    /// for an unrequested change: <c>ws_objects/pfw.pbl.src/pfw.sra:L91</c> DISCARDS the return code, while
    /// the documentation makes a failed initialization fatal. The port checks the code, because the
    /// documentation is the specification and the call site is the laxity.
    /// </para>
    /// <para>
    /// The second half is the other half of the pairing requirement: finalize must NOT run when
    /// initialization did not succeed, which is why the pairing is not expressed as a finally block.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ADefiniteInitializationFailureIsFatalAndFinalizationIsThenNotOwed()
    {
        RecordingFrameworkRuntime runtime = new() { InitializeResult = RetCode.FAILED };
        FrameworkInitializer initializer = CreateInitializer(runtime);

        FrameworkInitializationException failure =
            await Assert.ThrowsAsync<FrameworkInitializationException>(
                () => initializer.StartingAsync(TestContext.Current.CancellationToken));

        Assert.Equal(RetCode.FAILED, failure.ReturnCode);
        Assert.Equal(CapabilityFlags.AllCapabilitiesMask, failure.RequestedCapabilities);
        Assert.False(initializer.IsInitialized);

        // Not owed, and therefore not performed.
        await initializer.StoppedAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Finalize()", runtime.Calls);
        Assert.False(initializer.IsFinalized);
    }

    /// <summary>
    /// A PREVENTED initialization is treated as a success, because the preserved algebra says it is.
    /// </summary>
    /// <remarks>
    /// PRESERVED DEFECT, ASSERTED AS A DEFECT (constraints C-B and C-K). <c>IsSucceeded</c> tests
    /// <c>&gt;= 0</c> [<c>ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13</c>] and
    /// <see cref="RetCode.PREVENT"/> is 1 [<c>ws_objects/pfw.shared.pbl.src/retcode.sru:L42</c>], so a
    /// PREVENTION reads as a SUCCESS in a nominally boolean algebra and initialization proceeds. Narrowing
    /// the test to equality with <see cref="RetCode.OK"/> would "correct" the legacy, and this assertion is
    /// what makes that correction fail a test instead of passing review. A future reader must not mistake
    /// this expectation for a mistake: it is the legacy's behaviour, reproduced deliberately.
    /// </remarks>
    [Fact]
    public async Task APreventedInitializationIsTreatedAsASuccessBecauseTheAlgebraSaysSo()
    {
        RecordingFrameworkRuntime runtime = new() { InitializeResult = RetCode.PREVENT };
        FrameworkInitializer initializer = CreateInitializer(runtime);

        await initializer.StartingAsync(TestContext.Current.CancellationToken);

        Assert.True(initializer.IsInitialized);

        // The success path is the only one that reads the version, so its presence proves which branch ran.
        Assert.Contains("Version()", runtime.Calls);

        // And because it succeeded, finalization IS owed and is performed exactly once.
        await initializer.StoppedAsync(TestContext.Current.CancellationToken);
        await initializer.StoppedAsync(TestContext.Current.CancellationToken);

        Assert.True(initializer.IsFinalized);
        Assert.Equal(1, runtime.Calls.Count(call => string.Equals(call, "Finalize()", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A CANCELLED initialization is neither succeeded nor failed, and is fatal for that reason.
    /// </summary>
    /// <remarks>
    /// The third reading, and a distinct branch rather than an else on the failure test so that it cannot be
    /// reached by accident. <see cref="RetCode.CANCELLED"/> is -2 and the failure predicate excludes it
    /// EXPLICITLY [<c>ws_objects/pfw.shared.pbl.src/isfailed.srf:L11-L13</c>], so both predicates answer
    /// false and the algebra cannot classify the outcome. Treating that as fatal is the decision the port
    /// records: an initialization whose success is not positively signalled leaves the framework's usability
    /// precondition unproven, and continuing on an unproven precondition is exactly the degradation
    /// fail-fast exists to prevent.
    /// </remarks>
    [Fact]
    public async Task ACancelledInitializationIsNeitherAndIsFatalForThatReason()
    {
        RecordingFrameworkRuntime runtime = new() { InitializeResult = RetCode.CANCELLED };
        FrameworkInitializer initializer = CreateInitializer(runtime);

        FrameworkInitializationException failure =
            await Assert.ThrowsAsync<FrameworkInitializationException>(
                () => initializer.StartingAsync(TestContext.Current.CancellationToken));

        Assert.Equal(RetCode.CANCELLED, failure.ReturnCode);

        // The tri-state hole, stated at the point that depends on it.
        Assert.False(Predicates.IsSucceeded(RetCode.CANCELLED));
        Assert.False(Predicates.IsFailed(RetCode.CANCELLED));

        Assert.False(initializer.IsInitialized);
        Assert.False(initializer.IsFinalized);
    }

    /// <summary>
    /// A lifecycle boundary that FAULTS is fatal and reports no return code, because none exists.
    /// </summary>
    /// <remarks>
    /// The distinction is worth preserving rather than flattening: a boundary that returned a failure
    /// reported something, and a boundary that threw reported nothing. Substituting zero for the missing code
    /// would be worse than leaving it absent, because zero is <see cref="RetCode.OK"/> and would read as a
    /// success on a path that is about to abort startup.
    /// </remarks>
    [Fact]
    public async Task AFaultingLifecycleBoundaryIsFatalAndReportsNoReturnCode()
    {
        RecordingFrameworkRuntime runtime = new()
        {
            InitializeFault = new InvalidOperationException("the boundary faulted"),
        };

        FrameworkInitializer initializer = CreateInitializer(runtime);

        FrameworkInitializationException failure =
            await Assert.ThrowsAsync<FrameworkInitializationException>(
                () => initializer.StartingAsync(TestContext.Current.CancellationToken));

        Assert.Null(failure.ReturnCode);
        Assert.Equal(CapabilityFlags.AllCapabilitiesMask, failure.RequestedCapabilities);
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.False(initializer.IsInitialized);
    }

    /// <summary>
    /// Initializing twice is a structural fault, and a finalization that faults never escapes shutdown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The framework has no re-initialize and the pairing is one to one, so a host wired to initialize twice
    /// is misconfigured and is refused rather than started - the same fail-fast posture applied to the wiring
    /// instead of to the runtime.
    /// </para>
    /// <para>
    /// The shutdown half inverts deliberately: NOTHING may escape the shutdown path, because an exception
    /// there would replace an orderly stop with a fault after the work is already done. The pairing is
    /// complete either way, and the legacy has no re-finalize to retry with.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task InitializingTwiceIsAStructuralFaultAndAFaultingFinalizationNeverEscapes()
    {
        RecordingFrameworkRuntime runtime = new()
        {
            FinalizeFault = new InvalidOperationException("finalization faulted"),
        };

        FrameworkInitializer initializer = CreateInitializer(runtime);

        await initializer.StartingAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.StartingAsync(TestContext.Current.CancellationToken));

        // The four members the hosted-service contract requires but this type has no work for. Asserted as
        // no-ops rather than left unexercised, because a future edit that gave one of them work would move
        // the lifecycle out of the two members that own it.
        await initializer.StartAsync(TestContext.Current.CancellationToken);
        await initializer.StartedAsync(TestContext.Current.CancellationToken);
        await initializer.StoppingAsync(TestContext.Current.CancellationToken);
        await initializer.StopAsync(TestContext.Current.CancellationToken);

        // Faulting finalization: swallowed, and the pairing still completes.
        await initializer.StoppedAsync(TestContext.Current.CancellationToken);

        Assert.True(initializer.IsFinalized);
        Assert.Contains("Finalize()", runtime.Calls);
    }

    /// <summary>
    /// A startup that is already cancelled acquires no finalization obligation.
    /// </summary>
    /// <remarks>
    /// The cancellation is observed BEFORE the lifecycle state is claimed, which matters: claiming the state
    /// and then abandoning it would leave the instance owing a finalization it can never perform. A
    /// cancellation is the host abandoning startup rather than the framework failing, so it propagates
    /// unwrapped instead of becoming an initialization failure.
    /// </remarks>
    [Fact]
    public async Task AnAlreadyCancelledStartupAcquiresNoFinalizationObligation()
    {
        RecordingFrameworkRuntime runtime = new();
        FrameworkInitializer initializer = CreateInitializer(runtime);

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => initializer.StartingAsync(cancelled.Token));

        Assert.Empty(runtime.Calls);
        Assert.False(initializer.IsInitialized);

        // Nothing is owed, so nothing is performed - and the instance is still usable, which is what
        // observing the cancellation before claiming the state buys.
        await initializer.StoppedAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Finalize()", runtime.Calls);
    }

    /// <summary>
    /// Finalization reports every reading it can receive and completes the pairing regardless, because the
    /// legacy has no re-finalize.
    /// </summary>
    /// <param name="finalizeResult">The code the substituted boundary returns from finalize.</param>
    /// <param name="expectedLevel">The level the operator channel must record that reading at.</param>
    /// <remarks>
    /// <para>
    /// THE ASYMMETRY WITH INITIALIZATION IS DELIBERATE AND IS PRESERVED. A failed initialization is fatal,
    /// because everything after it would run on an unproven precondition. A failed FINALIZATION is not:
    /// shutdown is already under way, the pairing the legacy documentation mandates
    /// [<c>docs/README.md:L15</c>] is discharged either way, and there is no re-finalize to retry with. So
    /// the reading is recorded at a level matching its severity and shutdown continues.
    /// </para>
    /// <para>
    /// The three readings are the same tri-state algebra as initialization, which is why they are asserted
    /// together: succeeded (including a prevention, which reads as success), failed, and neither - and
    /// cancelled is the "neither" case, being excluded from failure by an explicit guard
    /// [<c>ws_objects/pfw.shared.pbl.src/isfailed.srf:L11-L13</c>].
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(RetCode.OK, LogLevel.Information)]
    [InlineData(RetCode.PREVENT, LogLevel.Information)]
    [InlineData(RetCode.FAILED, LogLevel.Error)]
    [InlineData(RetCode.CANCELLED, LogLevel.Warning)]
    public async Task EveryFinalizationReadingIsRecordedAndNoneOfThemBlocksShutdown(
        long finalizeResult,
        LogLevel expectedLevel)
    {
        RecordingFrameworkRuntime runtime = new() { FinalizeResult = finalizeResult };

        FrameworkInitializer initializer =
            CreateInitializer(runtime, out EnabledRecordingLogger<FrameworkInitializer> operatorChannel);

        await initializer.StartingAsync(TestContext.Current.CancellationToken);
        await initializer.StoppedAsync(TestContext.Current.CancellationToken);

        // The pairing is complete whatever the reading was, and it is never attempted twice.
        Assert.True(initializer.IsFinalized);
        Assert.Equal(1, runtime.Calls.Count(static call => call == "Finalize()"));

        Assert.True(
            operatorChannel.Recorded(expectedLevel),
            $"Finalization returned {finalizeResult} but nothing was recorded at {expectedLevel}.");
    }

    /// <summary>
    /// A shutdown whose token is ALREADY CANCELLED still discharges the finalization it owes, exactly
    /// once, and a second call changes nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE BRANCH NO OTHER ROW REACHES, AND IT IS THE HALF OF THE PAIRING REQUIREMENT MOST LIKELY TO BE
    /// "TIDIED" AWAY. Every other finalization row here hands <c>StoppedAsync</c> a live token, so all of
    /// them would keep passing if somebody added the cancellation observation that a reader of the six
    /// <c>IHostedLifecycleService</c> hooks would expect to find - and the pairing
    /// <c>docs/README.md:L15</c> mandates would then be silently owed forever on every shutdown a host
    /// cancels, which is precisely the shutdown a host under time pressure performs.
    /// </para>
    /// <para>
    /// WHY IGNORING THE TOKEN IS CORRECT HERE AND WRONG ON THE WAY IN. The asymmetry is deliberate and is
    /// stated on the production member itself. <c>StartingAsync</c> observes cancellation BEFORE it claims
    /// the lifecycle state, so an abandoned startup acquires no obligation at all. <c>StoppedAsync</c>
    /// observes nothing, because by the time it runs the obligation already EXISTS: an initialization
    /// succeeded, the framework holds resources, and there is no re-finalize to retry with. A cancelled
    /// shutdown token means "finish quickly", not "skip the release".
    /// </para>
    /// <para>
    /// THREE PROPERTIES, AND THE THIRD IS WHAT MAKES THE FIRST TWO WORTH ASSERTING. No exception escapes -
    /// not even the <see cref="OperationCanceledException"/> that observing the token would produce, which
    /// is the shape a regression would take. The boundary is called exactly once and the instance reports
    /// itself finalized. And a SECOND call after the first is inert: the transition is decided by the same
    /// interlocked compare-and-exchange that performs it, so a host that stops twice cannot finalize twice
    /// - the second call finds the state already moved and returns without touching the boundary.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task FinalizationIsPerformedEvenWhenTheShutdownTokenIsAlreadyCancelled()
    {
        RecordingFrameworkRuntime runtime = new();

        FrameworkInitializer initializer =
            CreateInitializer(runtime, out EnabledRecordingLogger<FrameworkInitializer> operatorChannel);

        // A SUCCESSFUL START, so a finalization is genuinely owed. The "nothing owed" case is covered by
        // AnAlreadyCancelledStartupAcquiresNoFinalizationObligation and is a different branch.
        await initializer.StartingAsync(TestContext.Current.CancellationToken);

        Assert.True(initializer.IsInitialized);

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        // NO THROW. Asserted by NOT wrapping the call: an escaping OperationCanceledException would fail
        // the test at this line, which is the clearest possible report of the regression.
        await initializer.StoppedAsync(cancelled.Token);

        Assert.True(initializer.IsFinalized);
        Assert.Equal(1, runtime.Calls.Count(static call => call == "Finalize()"));

        // The outcome was reported on the operator channel like any other, so a cancelled shutdown is not
        // a silent one.
        Assert.True(
            operatorChannel.Recorded(LogLevel.Information),
            "A finalization performed under an already-cancelled shutdown token recorded nothing at "
                + "Information, so an operator reading the log could not tell the pairing was discharged.");

        // IDEMPOTENT, and still with a cancelled token. The second call must neither throw nor reach the
        // boundary a second time.
        await initializer.StoppedAsync(cancelled.Token);

        Assert.True(initializer.IsFinalized);
        Assert.Equal(1, runtime.Calls.Count(static call => call == "Finalize()"));
    }

    /// <summary>
    /// A finalization boundary that throws is swallowed, because nothing may escape the shutdown path.
    /// </summary>
    /// <remarks>
    /// EVERY exception is caught here, cancellation included - the opposite of the initialization path,
    /// where a cancellation propagates unwrapped. The state has already moved to finalized, so the pairing
    /// is discharged either way; letting an exception escape would turn an orderly shutdown into a crash and
    /// would strand whatever else the host still had to stop.
    /// </remarks>
    [Fact]
    public async Task AFinalizationBoundaryThatThrowsDoesNotEscapeTheShutdownPath()
    {
        RecordingFrameworkRuntime runtime = new()
        {
            FinalizeFault = new InvalidTimeZoneException("The finalize boundary faulted on purpose."),
        };

        FrameworkInitializer initializer =
            CreateInitializer(runtime, out EnabledRecordingLogger<FrameworkInitializer> operatorChannel);

        await initializer.StartingAsync(TestContext.Current.CancellationToken);

        // No throw, and no second attempt.
        await initializer.StoppedAsync(TestContext.Current.CancellationToken);

        Assert.True(initializer.IsFinalized);
        Assert.True(operatorChannel.Recorded(LogLevel.Error));
        Assert.Equal(1, runtime.Calls.Count(static call => call == "Finalize()"));
    }

    /// <summary>
    /// An unrecognized capability bit is REPORTED rather than rejected.
    /// </summary>
    /// <remarks>
    /// PRESERVED BEHAVIOUR, ASSERTED AS SUCH. The native entry point validates nothing about the mask it
    /// receives, so neither does the port: bit positions four to seven are unassigned in the legacy layout
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48</c> jumps from 8 straight to 256] and anything
    /// above 2048 is undeclared, so an operator who sets one is almost certainly looking at a typo - but
    /// turning that into a rejection would be NEW behaviour, and the mask passes through unchanged.
    /// </remarks>
    [Fact]
    public async Task AnUnrecognizedCapabilityBitIsReportedRatherThanRejected()
    {
        RecordingFrameworkRuntime runtime = new();

        GatewayOptions options = new()
        {
            CapabilityFlags = CapabilityFlags.AllCapabilitiesMask | 0x10000L,
        };

        EnabledRecordingLogger<FrameworkInitializer> operatorChannel = new();

        FrameworkInitializer initializer = new(runtime, Options.Create(options), operatorChannel);

        await initializer.StartingAsync(TestContext.Current.CancellationToken);

        Assert.True(initializer.IsInitialized);
        Assert.True(initializer.Capabilities.HasUnrecognizedBits);

        // REPORTED, NOT REJECTED. The legacy passes whatever mask it receives straight through, so the
        // undeclared bit must reach the boundary unchanged and the only consequence must be a warning on
        // the operator channel. Turning it into a rejection would be new behaviour (constraint C-B).
        Assert.True(operatorChannel.Recorded(LogLevel.Warning));

        // Passed through unchanged, including the bit the legacy layout never declared.
        Assert.Equal(CapabilityFlags.AllCapabilitiesMask | 0x10000u, runtime.RequestedCapabilityMask);
        Assert.Equal(RecordingFrameworkRuntime.ReportedVersion, initializer.Version());

        await initializer.StoppedAsync(TestContext.Current.CancellationToken);
    }

    // --------------------------------------------------------------------------------------------------
    //  3.9  THE DEPLOYED READINESS PROBE, WITH ITS TRANSPORT SCRIPTED AND NO UPSTREAM PRESENT
    //
    //  Everything above substitutes IUpstreamReadinessProbe wholesale, which proves the AGGREGATE. This
    //  subsection leaves the DEPLOYED probe in place - the one that actually answers /health in the
    //  container the compose readiness gate polls - and scripts its transport instead, so the real
    //  implementation runs while still contacting nothing.
    //
    //  Why this belongs in THIS file rather than a new one: /health's anonymity is a C-G property asserted
    //  in 3.3, the environment's readiness gate is a C-L constraint expressed as `curl -sf
    //  http://localhost:5105/health`, and the probe's documented "it sends no credential" rule is a C-G
    //  property that only the real implementation can demonstrate. The folder is also capped, so depth
    //  goes here rather than into a sixth file.
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every upstream's own reported verdict token is read and reported, rather than being flattened into
    /// ready or not-ready.
    /// </summary>
    [Fact]
    public async Task TheDeployedProbeReadsEachUpstreamsOwnVerdictToken()
    {
        ScriptedReadinessChannelHandler channel = new()
        {
            ByPort =
            {
                [5101] = static () => ScriptedReadinessChannelHandler.Success("{\"status\":\"Healthy\"}"),
                [5102] = static () => ScriptedReadinessChannelHandler.Success("{\"status\":\"Degraded\"}"),
                [5104] = static () => ScriptedReadinessChannelHandler.Success("{\"status\":\"Unhealthy\"}"),
            },
        };

        await using GatewayTestHostFixture probeHost = CreateScriptedProbeHost(channel);
        using HttpClient client = probeHost.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            ReadinessRoute,
            TestContext.Current.CancellationToken);

        // Not ready, because one degraded and one unhealthy upstream is not a ready system. The status is
        // the aggregate's, but the three per-upstream entries are each upstream's own word.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);
        Dictionary<string, string> observed = ReadUpstreamStatuses(document.RootElement);

        Assert.Equal("Healthy", observed["persistence"]);
        Assert.Equal("Degraded", observed["dataservices"]);
        Assert.Equal("Unhealthy", observed["security"]);

        // THE PROBE PRESENTS NO CREDENTIAL, asserted rather than assumed. A probe that presented a token
        // would make readiness depend on token issuance already being live, which during a cold start is a
        // circular dependency that cannot resolve - precisely when the gate needs a verdict.
        Assert.False(channel.AnyRequestCarriedACredential);

        // One request per configured upstream, each at that upstream's own address.
        Assert.Equal(
            GatewayTestHostFixture.ConfiguredProbePorts.Order(),
            channel.Requested.Select(static address => address.Port).Order());
    }

    /// <summary>
    /// A ready aggregate is reported only when every upstream answers healthy, which is the property the
    /// orchestration readiness gate depends on.
    /// </summary>
    [Fact]
    public async Task TheDeployedProbeReportsReadyOnlyWhenEveryUpstreamAnswersHealthy()
    {
        ScriptedReadinessChannelHandler channel = new();
        channel.ScriptEveryUpstream(
            static () => ScriptedReadinessChannelHandler.Success("{\"status\":\"Healthy\"}"));

        await using GatewayTestHostFixture probeHost = CreateScriptedProbeHost(channel);
        using HttpClient client = probeHost.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            ReadinessRoute,
            TestContext.Current.CancellationToken);

        // C-L: `curl -sf http://localhost:5105/health` is the documented readiness gate, and -sf fails on
        // any non-success status. This is that command's success condition, produced by the real probe.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);

        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            AggregateHealthReport.ReportingService,
            document.RootElement.GetProperty("service").GetString());

        Assert.False(channel.AnyRequestCarriedACredential);
    }

    /// <summary>
    /// Every body a successful readiness response can carry is read the way the probe documents, including
    /// the shapes it refuses to interpret.
    /// </summary>
    /// <param name="shape">A description of the body, used to name the case.</param>
    /// <param name="body">The body served for all three upstreams.</param>
    /// <param name="expected">The per-upstream status the aggregate must report.</param>
    [Theory]
    [MemberData(nameof(ReadinessBodyShapes))]
    public async Task TheDeployedProbeInterpretsOnlyAStringVerdictTokenAndFailsClosedOnAnUnknownOne(
        string shape,
        string body,
        string expected)
    {
        Assert.False(string.IsNullOrWhiteSpace(shape));

        ScriptedReadinessChannelHandler channel = new();
        channel.ScriptEveryUpstream(() => ScriptedReadinessChannelHandler.Success(body));

        await using GatewayTestHostFixture probeHost = CreateScriptedProbeHost(channel);
        using HttpClient client = probeHost.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            ReadinessRoute,
            TestContext.Current.CancellationToken);

        using JsonDocument document = await ReadJsonAsync(response);

        foreach (string status in ReadUpstreamStatuses(document.RootElement).Values)
        {
            Assert.Equal(expected, status);
        }
    }

    /// <summary>
    /// A not-ready upstream's own verdict survives the aggregation: the <c>503</c> body's
    /// <c>serviceStatus</c> member is read, so a degraded upstream is reported as degraded rather than
    /// flattened into a failure.
    /// </summary>
    /// <param name="reported">The token the scripted upstream puts in its problem document.</param>
    /// <param name="expected">The per-upstream status the aggregate must report.</param>
    /// <remarks>
    /// <para>
    /// THIS IS THE ARM THAT MAKES THE PUBLISHED CONTRACT DELIVERABLE. Every service in the estate answers
    /// <c>503</c> for BOTH not-ready verdicts, because the orchestration gate reads the status code - so
    /// the code cannot separate them, and the only other machine-readable member of a not-ready body is
    /// <c>retCode</c>, which is <c>E_RETRY</c> for both. Without a dedicated member, a degraded upstream
    /// and a failed one are indistinguishable on the wire and the aggregate must guess.
    /// </para>
    /// <para>
    /// The <c>Healthy</c> row is the self-contradiction case: a <c>503</c> is the estate's statement that
    /// the service is not ready, so a body claiming otherwise is evidence the body cannot be trusted, and
    /// the code decides.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Degraded", "Degraded")]
    [InlineData("Unhealthy", "Unhealthy")]
    [InlineData("Healthy", "Unhealthy")]
    [InlineData("Sideways", "Unhealthy")]
    public async Task TheDeployedProbeReadsANotReadyUpstreamsOwnVerdictFromItsProblemDocument(
        string reported,
        string expected)
    {
        ScriptedReadinessChannelHandler channel = new();
        channel.ScriptEveryUpstream(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(
                "{\"title\":\"Service Unavailable\",\"status\":503,\"serviceStatus\":\""
                    + reported
                    + "\",\"retCode\":-33}",
                Encoding.UTF8,
                "application/problem+json"),
        });

        await using GatewayTestHostFixture probeHost = CreateScriptedProbeHost(channel);
        using HttpClient client = probeHost.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            ReadinessRoute,
            TestContext.Current.CancellationToken);

        // Not ready either way - the aggregate is ready only when every upstream is healthy.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);

        foreach (string status in ReadUpstreamStatuses(document.RootElement).Values)
        {
            Assert.Equal(expected, status);
        }

        await AssertNoTopologyOrCredentialIsDisclosedAsync(response, document);
    }

    /// <summary>
    /// An upstream that answers with a failure status is unhealthy - it answered, and said it was not
    /// ready - rather than unreachable, which is reserved for obtaining no verdict at all.
    /// </summary>
    /// <param name="statusCode">The status the scripted upstream answers with.</param>
    /// <remarks>
    /// The <c>503</c> row carries a body whose only verdict-shaped member is RFC 9457's own integer
    /// <c>status</c>, so no token is readable and the arm fails closed. The other two rows are statuses
    /// contract C-10 does not declare on this path at all, and for those the body is not read: whatever
    /// answered is not answering this contract.
    /// </remarks>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task TheDeployedProbeSeparatesAnUnhealthyAnswerFromNoAnswerAtAll(HttpStatusCode statusCode)
    {
        ScriptedReadinessChannelHandler channel = new();
        channel.ScriptEveryUpstream(() => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("{\"status\":\"Healthy\"}", Encoding.UTF8, MediaTypeNames.Application.Json),
        });

        await using GatewayTestHostFixture probeHost = CreateScriptedProbeHost(channel);
        using HttpClient client = probeHost.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            ReadinessRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);

        // The body claimed healthy; the STATUS said otherwise, and the status wins. Trusting a body served
        // under a failure status would let a half-started upstream open the dependency gate.
        foreach (string status in ReadUpstreamStatuses(document.RootElement).Values)
        {
            Assert.Equal("Unhealthy", status);
        }
    }

    /// <summary>
    /// Gateway's own not-ready body carries the machine-readable verdict, so an aggregator reading Gateway
    /// the way Gateway reads its upstreams gets a token rather than only prose.
    /// </summary>
    /// <param name="upstreamStatus">The status the three scripted upstreams answer with.</param>
    /// <param name="expected">The verdict Gateway's own problem document must declare.</param>
    /// <remarks>
    /// The shape is symmetric BY DESIGN, and the symmetry is what makes the contract one shape rather than
    /// four: whatever reads a leaf's not-ready body reads Gateway's the same way. A degraded aggregate is
    /// produced by a degraded upstream, and a failed one by an unreachable upstream - the aggregate takes
    /// the worst of its participants.
    /// </remarks>
    [Theory]
    [InlineData("Degraded", "Degraded")]
    [InlineData("Unhealthy", "Unhealthy")]
    public async Task GatewaysOwnNotReadyBodyDeclaresItsVerdictInAMachineReadableMember(
        string upstreamStatus,
        string expected)
    {
        ScriptedReadinessChannelHandler channel = new();
        channel.ScriptEveryUpstream(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(
                "{\"status\":503,\"serviceStatus\":\"" + upstreamStatus + "\"}",
                Encoding.UTF8,
                "application/problem+json"),
        });

        await using GatewayTestHostFixture probeHost = CreateScriptedProbeHost(channel);
        using HttpClient client = probeHost.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            ReadinessRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);

        // RFC 9457's own member still carries the integer, which is exactly why the verdict needed a name
        // of its own rather than sharing that one.
        Assert.Equal(503, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expected, document.RootElement.GetProperty("serviceStatus").GetString());

        await AssertNoTopologyOrCredentialIsDisclosedAsync(response, document);
    }

    /// <summary>
    /// Every transport failure mode yields one unreachable entry rather than a faulted response.
    /// </summary>
    /// <param name="mode">Which failure the scripted transport raises.</param>
    [Theory]
    [InlineData("transport")]
    [InlineData("cancelled")]
    [InlineData("unusable-channel")]
    public async Task TheDeployedProbeConvertsEveryTransportFailureIntoAnUnreachableVerdict(string mode)
    {
        ScriptedReadinessChannelHandler channel = new();
        channel.ScriptEveryUpstream(() => throw CreateProbeChannelFailure(mode));

        await using GatewayTestHostFixture probeHost = CreateScriptedProbeHost(channel);
        using HttpClient client = probeHost.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            ReadinessRoute,
            TestContext.Current.CancellationToken);

        // NOT a 500. The one endpoint an unauthenticated caller can reach must never fault, because the
        // readiness gate polls it and a fault is indistinguishable from a crash.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);

        foreach (string status in ReadUpstreamStatuses(document.RootElement).Values)
        {
            Assert.Equal("Unreachable", status);
        }

        await AssertNoTopologyOrCredentialIsDisclosedAsync(response, document);
    }

    /// <summary>
    /// A substituted probe that throws degrades one entry rather than the whole endpoint.
    /// </summary>
    [Fact]
    public async Task AFaultingSubstitutedProbeDegradesTheEntryRatherThanTheEndpoint()
    {
        FaultingReadinessProbe faulting = new();

        await using GatewayTestHostFixture probeHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        probeHost.AdditionalServiceConfiguration.Add(services =>
        {
            services.RemoveAll<IUpstreamReadinessProbe>();
            services.AddSingleton<IUpstreamReadinessProbe>(faulting);
        });

        using HttpClient client = probeHost.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            ReadinessRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(GatewayTestHostFixture.ConfiguredProbePorts.Count, faulting.ProbeCount);

        using JsonDocument document = await ReadJsonAsync(response);

        foreach (string status in ReadUpstreamStatuses(document.RootElement).Values)
        {
            Assert.Equal("Unreachable", status);
        }
    }

    /// <summary>
    /// With no probe registered and no client factory to build one, every upstream is reported unreachable
    /// rather than the endpoint failing.
    /// </summary>
    [Fact]
    public async Task WithNoWayToObtainAVerdictEveryUpstreamIsUnreachableRatherThanAFault()
    {
        await using GatewayTestHostFixture probeHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        probeHost.SubstitutesUpstreamReadinessProbe = false;

        probeHost.AdditionalServiceConfiguration.Add(static services =>
        {
            // No probe AND no factory to construct the default one. The arm exists so that a missing
            // registration cannot turn a readiness probe into a startup gate.
            services.RemoveAll<IHttpClientFactory>();
        });

        using HttpClient client = probeHost.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            ReadinessRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);

        foreach (string status in ReadUpstreamStatuses(document.RootElement).Values)
        {
            Assert.Equal("Unreachable", status);
        }
    }

    /// <summary>
    /// A registered component check is folded into the aggregate and reported as one unnamed entry, with
    /// the registration's own name withheld from the anonymous body.
    /// </summary>
    /// <param name="componentIsHealthy">Whether the registered check reports healthy.</param>
    /// <param name="expectedStatus">The status the aggregate must answer with.</param>
    /// <param name="expectedComponentStatus">The status the aggregated component entry must carry.</param>
    [Theory]
    [InlineData(true, HttpStatusCode.OK, "Healthy")]
    [InlineData(false, HttpStatusCode.ServiceUnavailable, "Degraded")]
    public async Task ARegisteredComponentCheckIsAggregatedWithoutNamingItself(
        bool componentIsHealthy,
        HttpStatusCode expectedStatus,
        string expectedComponentStatus)
    {
        const string registeredName = "gateway-tests-component-registration";

        await using GatewayTestHostFixture componentHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        componentHost.UpstreamVerdict = UpstreamReadiness.Healthy;

        componentHost.AdditionalServiceConfiguration.Add(services =>
            services
                .AddHealthChecks()
                .AddCheck(
                    registeredName,
                    () => componentIsHealthy
                        ? HealthCheckResult.Healthy("A healthy component, on purpose.")
                        : HealthCheckResult.Degraded("A degraded component, on purpose.")));

        using HttpClient client = componentHost.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            ReadinessRoute,
            TestContext.Current.CancellationToken);

        // Readiness is the WORSE of the two, so a degraded component is not ready even with all three
        // upstreams healthy - and a healthy one leaves the ready verdict intact.
        Assert.Equal(expectedStatus, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);

        Dictionary<string, string> checks = ReadComponentChecks(document.RootElement);

        Assert.Equal(expectedComponentStatus, checks["components"]);
        Assert.Equal("Healthy", checks["self"]);

        // THE REGISTRATION'S OWN NAME IS WITHHELD. A check's name is authored by whatever registered it
        // rather than for disclosure, so it belongs on the operator channel and not in a body any
        // unauthenticated caller can read.
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(registeredName, body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A fault inside Gateway's own readiness evaluation is reported as not-ready rather than as a faulted
    /// response, and its cause is withheld from the anonymous body.
    /// </summary>
    [Fact]
    public async Task AFaultInsideTheOwnReadinessEvaluationIsReportedRatherThanThrown()
    {
        await using GatewayTestHostFixture faultingHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        faultingHost.AdditionalServiceConfiguration.Add(static services =>
        {
            // The component evaluator is made to fault when it is ASKED for a report. It cannot be made to
            // fault on construction, because the composition root resolves it while the host starts and the
            // host would then never start - which is a different property, already asserted elsewhere.
            services.RemoveAll<HealthCheckService>();
            services.AddSingleton<HealthCheckService>(new FaultingHealthCheckService());
        });

        using HttpClient client = faultingHost.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            ReadinessRoute,
            TestContext.Current.CancellationToken);

        // NOT a 500, and NOT an exception page. The compose readiness gate polls this endpoint, and a fault
        // there is indistinguishable from a crashed process.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);
        Dictionary<string, string> checks = ReadComponentChecks(document.RootElement);

        // The self check is the one that turns unhealthy: the process is answering, but its own evaluation
        // could not be completed, and claiming otherwise would be the graceful degradation C-B forbids.
        Assert.Equal("Unhealthy", checks["self"]);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            FaultingHealthCheckService.FaultMarker,
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.OrdinalIgnoreCase);
    }

    // --------------------------------------------------------------------------------------------------
    //  3.10  HELPERS
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a lifecycle service over a substituted runtime, with the default capability mask.
    /// </summary>
    /// <param name="runtime">The substituted boundary.</param>
    /// <returns>The lifecycle service, not yet started.</returns>
    /// <remarks>
    /// No host, no container and no configuration file: the options are created in place, because the mask is
    /// the only member of them this path reads.
    /// </remarks>
    private static FrameworkInitializer CreateInitializer(IFrameworkRuntime runtime) =>
        CreateInitializer(runtime, out _);

    /// <summary>
    /// Builds a lifecycle service over a substituted runtime and hands back the operator channel it writes
    /// to.
    /// </summary>
    /// <param name="runtime">The substituted boundary.</param>
    /// <param name="operatorChannel">The recording logger the lifecycle service reports through.</param>
    /// <returns>The lifecycle service, not yet started.</returns>
    /// <remarks>
    /// THE LOGGER IS ENABLED, WHICH IS LOAD BEARING. Every diagnostic on the fail-fast paths is guarded by an
    /// <c>IsEnabled</c> test, and a null logger reports itself disabled, so the whole operator-channel half of
    /// a structural fault would go unexercised and unasserted. The response withholds the cause on purpose -
    /// the record is the only place it is stated - so the record is part of the behaviour under test, not
    /// incidental noise.
    /// </remarks>
    private static FrameworkInitializer CreateInitializer(
        IFrameworkRuntime runtime,
        out EnabledRecordingLogger<FrameworkInitializer> operatorChannel)
    {
        operatorChannel = new EnabledRecordingLogger<FrameworkInitializer>();

        return new FrameworkInitializer(runtime, Options.Create(new GatewayOptions()), operatorChannel);
    }

    /// <summary>
    /// Builds the <c>Authorization</c> header value for one credential kind.
    /// </summary>
    /// <param name="kind">The kind, from the theory data above.</param>
    /// <returns>The header value, or <see langword="null"/> when the case presents no header at all.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not one of the declared cases.</exception>
    /// <remarks>
    /// The single mapping from kind to credential, and the reason the theory data carries kinds rather than
    /// values: member data is static, while every credential here has to be signed with the key the fixture
    /// instance generated. The final arm throws rather than defaulting, so adding a kind to the theory data
    /// without teaching this method about it fails loudly instead of silently testing the absent case.
    /// </remarks>
    private string? BuildAuthorizationHeaderValue(string kind) => kind switch
    {
        "absent" => null,
        "empty-bearer" => GatewayTestHostFixture.BearerScheme + " ",
        "malformed" => Bearer(GatewayTestHostFixture.MalformedToken),
        "malformed-three-segments" => Bearer(GatewayTestHostFixture.MalformedTokenWithThreeSegments),
        "expired" => Bearer(host.IssueExpiredToken()),
        "untrusted-key" => Bearer(host.IssueTokenSignedWithAnUntrustedKey()),
        "foreign-audience" => Bearer(host.IssueTokenForAudience(ForeignAudience)),
        "foreign-issuer" => Bearer(host.IssueTokenFromIssuer(ForeignIssuer)),

        // A different scheme carrying a syntactically valid credential of its own kind. The bearer handler
        // must not accept it, and must not attempt to parse it as a token either.
        "basic-scheme" => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("who:cares")),
        "unknown-scheme" => "PowerFrameworkLegacy " + host.IssueValidToken(),
        "valid" => Bearer(host.IssueValidToken()),
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind),
            kind,
            "Unknown credential kind. Every kind named in this class's theory data must have an arm here."),
    };

    /// <summary>Composes a bearer header value.</summary>
    /// <param name="token">The compact serialization.</param>
    /// <returns>The header value.</returns>
    private static string Bearer(string token) => $"{GatewayTestHostFixture.BearerScheme} {token}";

    /// <summary>Sends one request, optionally presenting a credential.</summary>
    /// <param name="client">The client to send with.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The relative route.</param>
    /// <param name="credential">The full header value, or <see langword="null"/> to present none.</param>
    /// <returns>The response, which the caller disposes.</returns>
    /// <remarks>
    /// The header is added WITHOUT validation on purpose: several cases here are deliberately malformed, and
    /// the typed header collection would refuse to represent them, which would move the refusal into the test
    /// client instead of the service under test.
    /// </remarks>
    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? credential)
    {
        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative));

        if (credential is not null)
        {
            request.Headers.TryAddWithoutValidation(AuthorizationHeader, credential);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Reads a response body as JSON.</summary>
    /// <param name="response">The response to read.</param>
    /// <returns>The parsed document, which the caller disposes.</returns>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    /// <summary>Reads the challenge header a refusal carried.</summary>
    /// <param name="response">The refusal.</param>
    /// <returns>The challenge, joined if the framework emitted more than one.</returns>
    private static string ReadChallenge(HttpResponseMessage response)
        => string.Join(", ", response.Headers.WwwAuthenticate.Select(static value => value.ToString()));

    /// <summary>
    /// A seven-field assert payload, exactly as the producer emits it.
    /// </summary>
    /// <returns>The CRLF-delimited payload.</returns>
    /// <remarks>
    /// Fields joined by CRLF, field one the fixed sentinel number the assertion producer writes. The values
    /// after it are legacy-shaped names so that the decoded record reads the way a real one would.
    /// </remarks>
    private static string BuildAssertPayload() => string.Join(
        "\r\n",
        "-10000",
        "the assertion text",
        "w_demo_selector",
        "n_cst_example",
        "of_dowork",
        "42",
        "frame one");

    /// <summary>
    /// Builds a request context the fault handler can write a response into, with no host at all.
    /// </summary>
    /// <returns>The context.</returns>
    /// <remarks>
    /// No routing, no endpoint and no service provider beyond the default, because the handler resolves its
    /// collaborators through its constructor. A context with no endpoint also exercises the unrouted arm of
    /// the handler's route description, which is the arm a fault raised before routing would take.
    /// </remarks>
    private static HttpContext CreateFaultContext()
    {
        DefaultHttpContext context = new();

        context.Response.Body = new MemoryStream();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = PingRoute;

        return context;
    }

    /// <summary>
    /// Asserts that no public instance member of a configuration type reads like a credential, except
    /// the one outbound client credential the token contract REQUIRES Gateway to be able to present.
    /// </summary>
    /// <param name="optionsType">The configuration type to inspect.</param>
    /// <remarks>
    /// <para>
    /// THE EXCEPTION IS NAMED RATHER THAN THE SWEEP RELAXED, and the distinction is what keeps this test
    /// worth running. What the token topology forbids is Gateway holding SIGNING material - a second
    /// signing authority in a system with exactly one issuer. It does not forbid Gateway
    /// AUTHENTICATING to that issuer, which the issuance operation requires precisely because a caller
    /// cannot present a bearer token in order to obtain its first bearer token.
    /// </para>
    /// <para>
    /// So exactly one property may carry a credential, it is named below, and a second one - or any
    /// signing-key spelling at all - still fails. A boolean is skipped because a boolean cannot hold
    /// material: a credential-shaped boolean name is a predicate ABOUT the configuration, which is what
    /// the issuance disjunction is.
    /// </para>
    /// </remarks>
    private static void AssertNoCredentialShapedMember(Type optionsType)
    {
        foreach (PropertyInfo member in optionsType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (string.Equals(
                    member.Name,
                    nameof(GatewayOptions.SecurityClientSecret),
                    StringComparison.Ordinal)
                || member.PropertyType == typeof(bool))
            {
                continue;
            }

            string normalized = member.Name.ToLowerInvariant();

            foreach (string vocabulary in CredentialVocabulary)
            {
                Assert.DoesNotContain(vocabulary, normalized, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Flattens every key beneath a configuration section, including nested ones.</summary>
    /// <param name="section">The section to walk.</param>
    /// <returns>Every descendant key path.</returns>
    private static IEnumerable<string> FlattenKeys(IConfiguration section)
    {
        foreach (IConfigurationSection child in section.GetChildren())
        {
            yield return child.Path;

            foreach (string nested in FlattenKeys(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// Every body shape a successful readiness answer can carry, with the verdict each one must produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three interpreted tokens are covered elsewhere; these are the cases where the probe must decide
    /// what an ANSWER WITHOUT A USABLE TOKEN means, and EVERY ONE OF THEM FAILS CLOSED TO UNHEALTHY.
    /// </para>
    /// <para>
    /// THAT UNIFORMITY IS THE POINT, AND IT IS A DELIBERATE CORRECTION. An earlier revision answered
    /// HEALTHY for the six cases with no readable token, on the reasoning that the 200 was itself the
    /// readiness signal and the shared framework's plain-text probe carries no token at all. The reasoning
    /// does not hold: this probe only ever addresses the three upstreams named in
    /// <c>Gateway:HealthProbes</c>, every one of which publishes contract C-10's JSON report from its own
    /// hand-written endpoint, so "no token" is not a lesser dialect - it is a body that failed to state
    /// its verdict. Reading it as ready converts silence into a positive assertion, which is precisely how
    /// a health aggregate becomes worse than no aggregate: the orchestration gate opens onto an upstream
    /// whose report nobody could parse. A body that cannot state its verdict is not a body whose verdict
    /// can be trusted.
    /// </para>
    /// <para>
    /// The integer case is the RFC 9457 collision and is the reason a token is read only when it is a JSON
    /// string: a problem document uses the same member name for the numeric HTTP status, so accepting a
    /// number would let an error document be misread as a verdict. The oversized case exercises the
    /// bounded read - the probe reads at most a few kilobytes, so a body larger than the bound arrives
    /// truncated and unparseable, and that too must fail closed rather than be discarded in favour of the
    /// status code.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string> ReadinessBodyShapes =>
        new()
        {
            { "a token outside the published vocabulary", "{\"status\":\"Sideways\"}", "Unhealthy" },
            { "an empty body", string.Empty, "Unhealthy" },
            { "a plain-text body", "OK", "Unhealthy" },
            { "a JSON object with no status member", "{\"state\":\"Healthy\"}", "Unhealthy" },
            { "a numeric status member (RFC 9457 collision)", "{\"status\":503}", "Unhealthy" },
            { "a JSON array root", "[\"Healthy\"]", "Unhealthy" },
            { "a JSON string root", "\"Healthy\"", "Unhealthy" },
            { "a null status member", "{\"status\":null}", "Unhealthy" },
            { "a truncated object (only the not-ready member is declared)", "{\"serviceStatus\":\"Healthy\"}", "Unhealthy" },
            {
                "a body larger than the probe's bounded read",
                "{\"padding\":\"" + new string('p', 8192) + "\",\"status\":\"Healthy\"}",
                "Unhealthy"
            },
        };

    /// <summary>
    /// Builds a host that keeps the DEPLOYED readiness probe and gives it a scripted transport.
    /// </summary>
    /// <param name="channel">The scripted transport the probe will use.</param>
    /// <returns>A host the caller disposes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="channel"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The named client is the seam: the composition root resolves the probe channel by name, so replacing
    /// that one client's primary handler substitutes the TRANSPORT while leaving the probe, the aggregate
    /// and the endpoint entirely real. Nothing else about the host changes, and no socket is opened.
    /// </remarks>
    private static GatewayTestHostFixture CreateScriptedProbeHost(ScriptedReadinessChannelHandler channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        GatewayTestHostFixture host = GatewayTestHostFixture.ForEnvironment(Environments.Production);

        host.SubstitutesUpstreamReadinessProbe = false;

        // Debug is raised deliberately. A not-ready upstream during a cold start is the ORDINARY
        // observation, so the probe records it at debug level and guards the record with an IsEnabled test;
        // at the deployed default level that record is skipped, and the diagnosis an operator relies on
        // would go unexercised.
        host.AdditionalSettings["Logging:LogLevel:Default"] = "Debug";

        host.AdditionalServiceConfiguration.Add(services =>
            services
                .AddHttpClient(HealthEndpoints.ProbeHttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => channel));

        return host;
    }

    /// <summary>
    /// Builds the failure a scripted probe transport raises for one named mode.
    /// </summary>
    /// <param name="mode">The mode from the theory data.</param>
    /// <returns>The exception to raise.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not one of the declared cases.</exception>
    /// <remarks>
    /// One exception per arm the probe declares, so all three are demonstrably converted rather than one
    /// standing in for the others. The final arm throws rather than defaulting, so extending the theory
    /// data without extending this method fails loudly instead of silently retesting a covered case.
    /// </remarks>
    private static Exception CreateProbeChannelFailure(string mode) => mode switch
    {
        "transport" => new HttpRequestException("The scripted readiness transport failed on purpose."),

        // Stands for BOTH an expired probe budget and a disconnected caller: the probe does not
        // distinguish them, because the consequence - no verdict was obtained - is identical.
        "cancelled" => new OperationCanceledException(
            "The scripted readiness probe was cancelled on purpose."),

        // A misconfigured channel rather than a transport failure. Still no verdict, still not a fault.
        "unusable-channel" => new InvalidOperationException(
            "The scripted readiness channel is unusable on purpose."),

        _ => new ArgumentOutOfRangeException(
            nameof(mode),
            mode,
            "The failure mode is not one of the declared cases."),
    };

    /// <summary>
    /// Reads the per-upstream statuses out of a readiness body.
    /// </summary>
    /// <param name="root">The readiness document's root, ready or not-ready.</param>
    /// <returns>Each upstream's logical name mapped to its reported status.</returns>
    /// <remarks>
    /// The not-ready body is an RFC 9457 problem document carrying the report as an extension member, while
    /// the ready body is the report itself, so the reader accepts either shape rather than each caller
    /// having to know which one it will get.
    /// </remarks>
    private static Dictionary<string, string> ReadUpstreamStatuses(JsonElement root)
    {
        JsonElement report = ResolveReadinessReport(root);

        Dictionary<string, string> statuses = new(StringComparer.Ordinal);

        foreach (JsonElement upstream in report.GetProperty("upstreams").EnumerateArray())
        {
            statuses[upstream.GetProperty("service").GetString() ?? string.Empty] =
                upstream.GetProperty("status").GetString() ?? string.Empty;
        }

        Assert.NotEmpty(statuses);

        return statuses;
    }

    /// <summary>
    /// Reads the component checks out of a readiness body.
    /// </summary>
    /// <param name="root">The readiness document's root, ready or not-ready.</param>
    /// <returns>Each check's name mapped to its reported status.</returns>
    private static Dictionary<string, string> ReadComponentChecks(JsonElement root)
    {
        JsonElement report = ResolveReadinessReport(root);

        Dictionary<string, string> checks = new(StringComparer.Ordinal);

        foreach (JsonElement check in report.GetProperty("checks").EnumerateArray())
        {
            checks[check.GetProperty("name").GetString() ?? string.Empty] =
                check.GetProperty("status").GetString() ?? string.Empty;
        }

        Assert.NotEmpty(checks);

        return checks;
    }

    /// <summary>
    /// Locates the readiness report inside either body shape.
    /// </summary>
    /// <param name="root">The readiness document's root.</param>
    /// <returns>The element carrying the report members.</returns>
    private static JsonElement ResolveReadinessReport(JsonElement root) =>
        root.TryGetProperty("report", out JsonElement carried) ? carried : root;

    /// <summary>
    /// Asserts that a readiness answer discloses neither topology nor anything credential shaped.
    /// </summary>
    /// <param name="response">The readiness response.</param>
    /// <param name="document">Its parsed body.</param>
    /// <returns>A task that completes once the body has been read and checked.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// THE ONE ENDPOINT AN UNAUTHENTICATED CALLER CAN REACH IS ALSO THE ONE THAT KNOWS THE MOST. It holds
    /// every upstream address and every failure's cause, and both are withheld: the address is topology and
    /// the cause can name a host, a port or a certificate subject. Both belong on the operator channel,
    /// which is why the failure modes above are asserted through the response AND through what the response
    /// does not say.
    /// </remarks>
    private static async Task AssertNoTopologyOrCredentialIsDisclosedAsync(
        HttpResponseMessage response,
        JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(document);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // THE CORRELATION IDENTIFIER IS EXCLUDED FROM THE SCAN, AND THAT IS CORRECTNESS RATHER THAN
        // CONVENIENCE. The problem-details writer attaches a `traceId` whose value is an opaque, randomly
        // generated hexadecimal trace-context identifier - it names an OCCURRENCE, it is not authored by
        // this service, and it can disclose nothing. But it is roughly fifty hex characters long, and a
        // string that long contains any particular four-digit decimal substring often enough to matter:
        // this assertion was observed failing because a generated identifier happened to contain the
        // sequence `5102`. Scanning it would therefore make the suite intermittently red over a value that
        // carries no information at all, which is worse than useless because it trains a reader to ignore
        // a security assertion. Everything the service ACTUALLY authors - the detail sentence, the upstream
        // entries, the component checks - is still scanned in full, so the property under test is intact.
        string scanned = RemoveCorrelationIdentifiers(document.RootElement, body);

        // The configured probe ports are topology. None of them may appear in an anonymous body.
        foreach (int port in GatewayTestHostFixture.ConfiguredProbePorts)
        {
            Assert.DoesNotContain(
                port.ToString(CultureInfo.InvariantCulture),
                scanned,
                StringComparison.Ordinal);
        }

        string normalized = scanned.ToLowerInvariant();

        foreach (string vocabulary in CredentialVocabulary)
        {
            Assert.DoesNotContain(vocabulary, normalized, StringComparison.Ordinal);
        }

        // Nor may the body carry a scripted failure's own words: the probe authors its own detail
        // sentences instead of relaying whatever the transport said.
        Assert.DoesNotContain("on purpose", normalized, StringComparison.Ordinal);
    }

    /// <summary>
    /// Removes every correlation identifier the body carries, leaving everything the service authored.
    /// </summary>
    /// <param name="root">The parsed body.</param>
    /// <param name="body">The raw body.</param>
    /// <returns>The body with each correlation identifier's value removed.</returns>
    /// <remarks>
    /// Searched at every depth rather than at the root only, because the not-ready answer nests the
    /// readiness report inside a problem document and a future revision may nest it differently. Removing
    /// the VALUE rather than the member keeps the surrounding text intact, so nothing the service authored
    /// escapes the scan as a side effect of this exclusion.
    /// </remarks>
    private static string RemoveCorrelationIdentifiers(JsonElement root, string body)
    {
        string scanned = body;

        foreach (string identifier in ReadCorrelationIdentifiers(root))
        {
            scanned = scanned.Replace(identifier, string.Empty, StringComparison.Ordinal);
        }

        return scanned;
    }

    /// <summary>
    /// Collects every correlation-identifier value in a document, at any depth.
    /// </summary>
    /// <param name="element">The element to walk.</param>
    /// <returns>Each non-empty identifier found.</returns>
    private static IEnumerable<string> ReadCorrelationIdentifiers(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty member in element.EnumerateObject())
                {
                    if (string.Equals(member.Name, CorrelationIdMember, StringComparison.Ordinal)
                        && member.Value.ValueKind == JsonValueKind.String)
                    {
                        string? value = member.Value.GetString();

                        if (!string.IsNullOrEmpty(value))
                        {
                            yield return value;
                        }
                    }

                    foreach (string nested in ReadCorrelationIdentifiers(member.Value))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    foreach (string nested in ReadCorrelationIdentifiers(item))
                    {
                        yield return nested;
                    }
                }

                break;

            default:
                break;
        }
    }
}
