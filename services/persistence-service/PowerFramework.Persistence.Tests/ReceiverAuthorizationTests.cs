// ==================================================================================================
//  ReceiverAuthorizationTests - AUTHENTICATION IS NOT AUTHORIZATION, ASSERTED ON ALL FOUR CONTRACTS
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE GUARDS
//
//  Contracts C-05 Query, C-06 Update, C-07 Command and C-08 Transaction used to require an
//  authenticated caller and nothing more. Combined with an issuer that granted every requested scope to
//  any caller whose certificate chained to the configured authority, that meant ANY trusted service
//  identity could mint a token addressed to this service and drive every one of its operations -
//  including the write, command and transaction surfaces, which are the only paths in the system that
//  generate or execute SQL (CWE-862 missing authorization, CWE-863 incorrect authorization).
//
//  The receivers now require, on top of authentication, BOTH of:
//
//    * the operation's own scope - `persistence.read` for the query contract, `persistence.write` for
//      update and command, and per RPC on the transaction contract because it straddles the line - read
//      out of the space-delimited RFC 6749 scope claim rather than compared against it whole; and
//    * a caller identity on this deployment's own `Jwt:PermittedCallers` roster, which ships carrying
//      exactly the one service the evidenced call graph shows reaching this one.
//
//  Either half alone leaves a hole. Scope without subject admits any caller the issuer serves as long as
//  it holds the scope; subject without scope lets the permitted caller reach the write surface with a
//  credential obtained for reading.
//
//  WHY THE READ/WRITE SPLIT IS ASSERTED IN BOTH DIRECTIONS
//  ------------------------------------------------------------------------------------------------
//  The single most valuable row here is the one that presents a READ credential to the WRITE surface.
//  A receiver that required "some persistence scope" rather than the operation's own scope would pass
//  every other row in this file, and would leave a read-only caller able to execute arbitrary SQL
//  through the command contract. The reverse row is asserted too, because a policy pair that had been
//  wired to the same scope by a copy-paste slip would otherwise look correct.
//
//  WHY EVERY ROW IS A PLAIN HTTP REQUEST TO A gRPC PATH
//  ------------------------------------------------------------------------------------------------
//  Authorization runs in middleware, ahead of the gRPC handler, so a refusal is written as an ordinary
//  HTTP status before any protocol buffer is read. That is what lets these rows carry no request payload
//  and take no dependency on the ninety-odd message types of persistence.v1 - and it is itself an
//  assertion, because a service that bound the message first would answer something other than 401 or
//  403. It also means the ADMITTED rows must assert "not refused" rather than a success status: past the
//  policy the handler reads a body this suite deliberately does not send, so its answer belongs to the
//  contract suites and not here.
//
//  CONSTRAINT COMPLIANCE
//  ------------------------------------------------------------------------------------------------
//  C-G   EVERY NEW BOUNDARY AUTHENTICATED - and this file is the half of C-G that an
//        authentication-only assertion cannot reach.
//  C-H   80 PER CENT LINE COVERAGE PER SERVICE. This file's named share is the authorization layer in
//        Authorization/ScopeAuthorization.cs, driven through the real pipeline: both arms of the scope
//        split, the roster walk, the subject-claim fallback and the empty-roster refusal.
//  C-I   EACH SERVICE BUILDS AND TESTS INDEPENDENTLY. Every host is in-memory with a static
//        verification configuration, so no port is bound, no metadata is fetched and no Security
//        instance is required.
// ==================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Proves that the four published gRPC contracts require the operation's own scope and a permitted
/// caller identity, not merely an authenticated principal.
/// </summary>
public sealed class ReceiverAuthorizationTests
{
    /// <summary>The scope the read contract is served under.</summary>
    private const string ReadScope = "persistence.read";

    /// <summary>The scope the three write-shaped contracts are served under.</summary>
    private const string WriteScope = "persistence.write";

    /// <summary>The identity this deployment's roster admits.</summary>
    private const string PermittedCaller = "powerframework-dataservices";

    /// <summary>An identity the roster does not carry.</summary>
    private const string UnlistedCaller = "powerframework-someone-else";

    /// <summary>A read-shaped method on contract C-05.</summary>
    /// <remarks>
    /// CHOSEN FOR ITS SHAPE, NOT ITS BEHAVIOUR. It is a unary method on the query contract, which is all
    /// that matters: the policy is declared on the service class and every method inherits it, so any one
    /// method of C-05, C-06 or C-07 is a faithful probe of that contract's requirement. C-08 is the
    /// exception and is probed by the write-side method named below.
    /// </remarks>
    private const string QueryMethod = "/persistence.v1.QueryService/Count";

    /// <summary>A method on contract C-06.</summary>
    private const string UpdateMethod = "/persistence.v1.UpdateService/Reset";

    /// <summary>A method on contract C-07 - the only path that executes arbitrary SQL text.</summary>
    private const string CommandMethod = "/persistence.v1.CommandService/Reset";

    /// <summary>A WRITE-side method on contract C-08.</summary>
    /// <remarks>
    /// CHOSEN FOR ITS SIDE OF THE SPLIT, NOT MERELY FOR BEING ON THE CONTRACT. C-08 is the one contract
    /// annotated PER RPC - its session, descriptor and state readers take the read scope and its state
    /// changers take the write scope - so a probe of "the transaction contract's write requirement" has to
    /// be one of the state changers. <c>Commit</c> is the least ambiguous of them: it applies a unit of
    /// work to durable storage, so no reading of the split puts it on the read side. An observer such as
    /// <c>IsConnected</c> would be the wrong probe in both directions at once - the write rows would refuse
    /// where they expect admission, and the read-credential row would be ADMITTED and so would stop
    /// asserting anything about the write surface.
    /// </remarks>
    private const string TransactionMethod = "/persistence.v1.TransactionService/Commit";

    /// <summary>The compact-serialization scheme name.</summary>
    private const string BearerScheme = "Bearer";

    /// <summary>The issuer the hosts below are configured to trust.</summary>
    /// <remarks>A reserved test host, so nothing resolves and no metadata is ever fetched.</remarks>
    private const string TrustedIssuer = "https://security.invalid";

    /// <summary>The audience the hosts below are configured to accept.</summary>
    private const string TrustedAudience = "powerframework-persistence-receiver";

    /// <summary>The three write-shaped contract methods.</summary>
    private static readonly string[] WriteMethods = [UpdateMethod, CommandMethod, TransactionMethod];

    /// <summary>
    /// Every write-shaped contract method, projected onto theory rows.
    /// </summary>
    /// <returns>One row per method.</returns>
    public static TheoryData<string> WriteContracts() => [.. WriteMethods];

    /// <summary>
    /// Every published contract method, projected onto theory rows.
    /// </summary>
    /// <returns>One row per method.</returns>
    public static TheoryData<string> AllContracts() =>
        [QueryMethod, UpdateMethod, CommandMethod, TransactionMethod];

    // ==============================================================================================
    //  GROUP 1 - AN ABSENT CREDENTIAL IS 401, WHICH FIXES THE ORDERING
    // ==============================================================================================

    /// <summary>
    /// Every contract refuses a caller presenting no credential, and does so as 401 rather than 403.
    /// </summary>
    /// <param name="method">The contract method.</param>
    /// <remarks>
    /// THE DISTINCTION IS THE ASSERTION. Authentication runs before authorization, so an absent
    /// credential is a 401; a service that evaluated the policy first would answer 403 and would tell an
    /// unauthenticated caller that its problem was permission. It also proves the refusal precedes
    /// message binding, since no request payload is sent at all.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllContracts))]
    public async Task EveryContractRefusesACallerPresentingNoCredential(string method)
    {
        using ReceiverHost host = new();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage refused = await PostAsync(client, method, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    // ==============================================================================================
    //  GROUP 2 - THE SCOPE HALF
    // ==============================================================================================

    /// <summary>
    /// The read contract admits the permitted caller holding the read scope.
    /// </summary>
    /// <remarks>
    /// THE CONTROL ROW FOR THE READ SURFACE. Every refusal row below differs from this one in exactly one
    /// dimension, so without it a refusal could not be attributed to the dimension it names. Success is
    /// asserted as "neither refusal", because past the policy the handler reads a payload this suite does
    /// not send.
    /// </remarks>
    [Fact]
    public async Task TheReadContractAdmitsThePermittedCallerHoldingTheReadScope()
    {
        using ReceiverHost host = new();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage answered = await PostAsync(
            client,
            QueryMethod,
            host.MintToken(PermittedCaller, ReadScope));

        Assert.NotEqual(HttpStatusCode.Unauthorized, answered.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, answered.StatusCode);
    }

    /// <summary>
    /// Each write-shaped contract admits the permitted caller holding the write scope.
    /// </summary>
    /// <param name="method">The contract method.</param>
    /// <remarks>
    /// The control row for the write surface, one per contract, because the three are mapped
    /// independently and a policy omitted from one of them would otherwise be invisible.
    /// </remarks>
    [Theory]
    [MemberData(nameof(WriteContracts))]
    public async Task EachWriteContractAdmitsThePermittedCallerHoldingTheWriteScope(string method)
    {
        using ReceiverHost host = new();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage answered = await PostAsync(
            client,
            method,
            host.MintToken(PermittedCaller, WriteScope));

        Assert.NotEqual(HttpStatusCode.Unauthorized, answered.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, answered.StatusCode);
    }

    /// <summary>
    /// A read credential does not reach any write-shaped contract.
    /// </summary>
    /// <param name="method">The contract method.</param>
    /// <remarks>
    /// THE MOST VALUABLE ROW IN THIS FILE. A receiver requiring "some persistence scope" rather than the
    /// operation's own would pass every other row here while leaving a read-only caller able to execute
    /// arbitrary SQL through the command contract. That is the whole reason the split exists, so it is
    /// asserted per contract rather than sampled.
    /// </remarks>
    [Theory]
    [MemberData(nameof(WriteContracts))]
    public async Task AReadCredentialDoesNotReachAWriteContract(string method)
    {
        using ReceiverHost host = new();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage refused = await PostAsync(
            client,
            method,
            host.MintToken(PermittedCaller, ReadScope));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    /// <summary>
    /// A write credential does not reach the read contract.
    /// </summary>
    /// <remarks>
    /// THE REVERSE OF THE ROW ABOVE, and it is not redundant: a policy pair wired to the same scope by a
    /// copy-paste slip would refuse a read credential at the write surface and still admit a write
    /// credential at the read surface. Asserting only one direction would leave that slip passing.
    /// </remarks>
    [Fact]
    public async Task AWriteCredentialDoesNotReachTheReadContract()
    {
        using ReceiverHost host = new();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage refused = await PostAsync(
            client,
            QueryMethod,
            host.MintToken(PermittedCaller, WriteScope));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    /// <summary>
    /// A credential carrying no scope at all is refused.
    /// </summary>
    /// <remarks>
    /// The credential is valid in every other respect - this host signed it, addressed it to itself and
    /// minted it for a caller its roster carries - so a 403 here is a statement about the scope
    /// requirement and nothing else.
    /// </remarks>
    [Fact]
    public async Task ACredentialCarryingNoScopeIsRefused()
    {
        using ReceiverHost host = new();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage refused = await PostAsync(
            client,
            QueryMethod,
            host.MintToken(PermittedCaller, scope: null));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    /// <summary>
    /// A scope differing from the required one only by case is refused.
    /// </summary>
    /// <remarks>
    /// RFC 6749 SCOPE TOKENS ARE CASE-SENSITIVE, so folding them would admit a spelling the issuer never
    /// mints. This row is the guard on an ordinal comparison that is easy to relax while reviewing
    /// something else.
    /// </remarks>
    [Fact]
    public async Task AScopeDifferingOnlyByCaseIsRefused()
    {
        using ReceiverHost host = new();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage refused = await PostAsync(
            client,
            QueryMethod,
            host.MintToken(PermittedCaller, ReadScope.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    /// <summary>
    /// The required scope is recognised inside a space-delimited set rather than only as the whole claim.
    /// </summary>
    /// <remarks>
    /// THE ROW THAT RULES OUT A CLAIM-EQUALITY REQUIREMENT. The granted set travels as ONE claim holding a
    /// space-delimited list, and this deployment's own roster grants this caller BOTH persistence scopes -
    /// so a requirement comparing the claim whole would refuse the very credential Security mints for it.
    /// </remarks>
    [Fact]
    public async Task TheRequiredScopeIsRecognisedInsideASpaceDelimitedSet()
    {
        using ReceiverHost host = new();
        using HttpClient client = host.CreateClient();

        string both = string.Concat(ReadScope, " ", WriteScope);

        using HttpResponseMessage read = await PostAsync(
            client,
            QueryMethod,
            host.MintToken(PermittedCaller, both));
        using HttpResponseMessage write = await PostAsync(
            client,
            CommandMethod,
            host.MintToken(PermittedCaller, both));

        Assert.NotEqual(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, write.StatusCode);
    }

    // ==============================================================================================
    //  GROUP 3 - THE CALLER-IDENTITY HALF
    // ==============================================================================================

    /// <summary>
    /// An identity absent from the roster is refused even when it holds the required scope.
    /// </summary>
    /// <remarks>
    /// The complement of the scope rows: this credential is sufficient in every respect except the
    /// identity it was minted for. Without this row a receiver checking only the scope claim would pass
    /// the whole file.
    /// </remarks>
    [Fact]
    public async Task AnUnlistedCallerIsRefusedEvenHoldingTheRequiredScope()
    {
        using ReceiverHost host = new();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage refused = await PostAsync(
            client,
            QueryMethod,
            host.MintToken(UnlistedCaller, ReadScope));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    /// <summary>
    /// A subject differing from the rostered identity only by case is refused.
    /// </summary>
    /// <remarks>
    /// A CALLER IDENTITY IS AN OPAQUE PROTOCOL IDENTIFIER, so two spellings differing by case are two
    /// identities. Folding them would admit a caller the deployment never listed, and would do so
    /// silently because the folded spelling reads like the listed one.
    /// </remarks>
    [Fact]
    public async Task ASubjectDifferingOnlyByCaseIsRefused()
    {
        using ReceiverHost host = new();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage refused = await PostAsync(
            client,
            QueryMethod,
            host.MintToken(PermittedCaller.ToUpperInvariant(), ReadScope));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>
    /// Sends a payload-free request to a gRPC method path, optionally presenting a credential.
    /// </summary>
    /// <param name="client">The client to send through.</param>
    /// <param name="method">The gRPC method path.</param>
    /// <param name="token">The compact-serialized token, or <see langword="null"/> to present none.</param>
    /// <returns>The response, which the caller owns.</returns>
    /// <remarks>
    /// NO REQUEST PAYLOAD AND NO gRPC CONTENT TYPE. Authorization runs in middleware ahead of the gRPC
    /// handler, so the refusal rows are decided before either would be read - which keeps this suite free
    /// of the ninety-odd message types of persistence.v1 and makes the ordering an assertion in its own
    /// right.
    /// </remarks>
    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string method,
        string? token)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(method, UriKind.Relative));

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Boots this service's own composition root in process, with a static verification configuration and
    /// a roster carrying exactly one permitted caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE BEARER HANDLER IS GIVEN ITS VERIFICATION MATERIAL DIRECTLY, which is what makes it skip
    /// metadata retrieval entirely - so every row proves the DEPLOYED authorization posture without a
    /// Security instance being reachable. The roster is supplied as HOST configuration rather than through
    /// a service override, so this service's own startup gate and options validator run against it.
    /// </para>
    /// <para>
    /// THE SIGNING KEY IS SYMMETRIC AND PER HOST, which is a property of the double rather than of the
    /// service: Security signs asymmetrically and this service holds verification material only, so what
    /// the key must satisfy here is that the handler accepts a token this host minted and nothing else.
    /// A fresh key per host is what makes a token unusable against any other host in the run.
    /// </para>
    /// </remarks>
    private sealed class ReceiverHost : WebApplicationFactory<Program>
    {
        /// <summary>The key credentials are signed and verified with.</summary>
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

        /// <summary>
        /// The data directory THIS host is configured with, unique to this instance.
        /// </summary>
        /// <remarks>
        /// <para>
        /// PER INSTANCE, AND THAT REPLACED A FIXED NAME. This host used to be pointed at one constant path
        /// under the system temporary directory, shared by every run of the suite and by every process
        /// running it. Two concurrent runs on one agent - routine under a parallel batch - then shared a
        /// directory and a SQLite file, and residue from a run that did not finish was visible to the next.
        /// Neither has anything to do with the authorization behaviour these rows assert, which is exactly
        /// why it must not be able to influence them.
        /// </para>
        /// <para>
        /// NOTHING NEEDS TO PRE-CREATE IT: the service creates its own data directory during startup
        /// [<c>Program.cs:L1658</c>], so a fresh path is the ordinary case rather than a fault.
        /// </para>
        /// </remarks>
        private readonly string _dataDirectory = Path.Combine(
            Path.GetTempPath(),
            string.Create(CultureInfo.InvariantCulture, $"pfw-receiver-authorization-{Guid.NewGuid():n}"));

        /// <summary>
        /// Mints a credential this host accepts, for a chosen identity and scope set.
        /// </summary>
        /// <param name="subject">The identity the token claims.</param>
        /// <param name="scope">The space-delimited scope set, or <see langword="null"/> for no claim.</param>
        /// <returns>A compact-serialized token.</returns>
        /// <remarks>
        /// A NULL SCOPE OMITS THE CLAIM ENTIRELY rather than sending an empty one, because those are
        /// different credentials: an empty claim would still exercise the split path, and the case worth
        /// covering is a token that never carried a scope at all.
        /// </remarks>
        internal string MintToken(string subject, string? scope)
        {
            DateTimeOffset now = TimeProvider.System.GetUtcNow();
            DateTimeOffset issued = now - TimeSpan.FromMinutes(1);
            DateTimeOffset expires = now + TimeSpan.FromMinutes(10);

            string scopeMember = scope is null
                ? string.Empty
                : string.Concat(",\"scope\":\"", scope, "\"");

            string header = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";
            string payload = string.Concat(
                "{\"iss\":\"",
                TrustedIssuer,
                "\",\"aud\":\"",
                TrustedAudience,
                "\",\"sub\":\"",
                subject,
                "\",\"iat\":",
                issued.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ",\"nbf\":",
                issued.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ",\"exp\":",
                expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                scopeMember,
                "}");

            string signingInput = string.Concat(
                Encode(Encoding.UTF8.GetBytes(header)),
                ".",
                Encode(Encoding.UTF8.GetBytes(payload)));

            return string.Concat(
                signingInput,
                ".",
                Encode(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(signingInput))));
        }

        /// <inheritdoc/>
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment(Environments.Production);

            builder.UseSetting("Jwt:Authority", TrustedIssuer);
            builder.UseSetting("Jwt:Audience", TrustedAudience);
            builder.UseSetting("Jwt:RequireHttpsMetadata", "true");
            builder.UseSetting("Jwt:PermittedCallers:0", PermittedCaller);
            // THIS HOST'S OWN DIRECTORY. See _dataDirectory for why it is per instance.
            builder.UseSetting("Sqlite:DataDirectory", _dataDirectory);

            builder.ConfigureServices(services => services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    OpenIdConnectConfiguration verification = new() { Issuer = TrustedIssuer };
                    verification.SigningKeys.Add(new SymmetricSecurityKey(_key));

                    options.Configuration = verification;
                    options.TokenValidationParameters.ValidIssuer = TrustedIssuer;
                    options.TokenValidationParameters.ValidAudience = TrustedAudience;
                }));
        }

        /// <summary>Base64url-encodes a segment, unpadded, as the compact serialization requires.</summary>
        /// <param name="value">The bytes to encode.</param>
        /// <returns>The encoded segment.</returns>
        private static string Encode(byte[] value) => System.Buffers.Text.Base64Url.EncodeToString(value);

        /// <summary>
        /// Disposes the host and then removes this instance's data directory.
        /// </summary>
        /// <param name="disposing">Whether managed state is being released.</param>
        /// <remarks>
        /// UNCONDITIONAL, and in this ORDER. Every row creates its host with <c>using</c>, so this runs on
        /// the failure path too - which is the path that used to leave a directory behind. The base
        /// disposal stops the host and closes the engine's handle on the database file, so it has to
        /// complete before the directory can be removed. No <c>catch</c>: the directory is one this
        /// instance composed and owns exclusively, and a suppressed teardown is indistinguishable from one
        /// that worked.
        /// </remarks>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing)
            {
                return;
            }

            if (Directory.Exists(_dataDirectory))
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
        }
    }
}
