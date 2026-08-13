// ======================================================================================================
//  OutboundCredentialScopeTests.cs - A CREDENTIAL AS NARROW AS THE CALL IT IS ATTACHED TO
//  ------------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  Gateway calls two separate published contracts on DataServices - C-03 `DataWindowService` and C-04
//  `ColumnExpressionService` - and it used to obtain ONE token carrying both scopes and attach it to every
//  outbound call. So the credential presented on a column-expression call also authorized the DataWindow
//  surface, and the credential on a DataWindow call also authorized the expression engine.
//
//  WHY THAT IS A REAL FAILURE AND NOT A TIDINESS POINT
//  A token captured from any single call carried the WHOLE of this client's authority rather than the
//  authority of the call it was taken from - from a log that recorded a header, from a compromised
//  upstream, from an operator's packet capture. The two surfaces are separate contracts precisely so they
//  can version and be authorized independently (AAP 0.4.3 keeps C-04 apart from C-03 "so the expansion
//  engine can version independently"), and a single credential spanning both discards that separation at
//  the only point where it would have limited damage.
//
//  WHY MINIMISATION COSTS NOTHING HERE
//  The token provider's cache key is built from the subject, the audience and the SORTED SCOPE SET, so two
//  narrower requests occupy two distinct cache entries and each is minted once per lifetime rather than
//  once per call. The steady state is two held credentials instead of one, each narrower than the one it
//  replaced - no extra issuance traffic in the steady state at all.
//
//  WHAT IS DELIBERATELY NOT ASSERTED
//  Nothing here requires a particular GRANTED set. A narrowing by Security is a successful outcome the
//  token contract obliges a caller to read rather than assert on, so these rows are about what is
//  REQUESTED - which is the part this client decides.
// ======================================================================================================

using System.Reflection;
using PowerFramework.Gateway.Clients;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// Covers outbound credential scope minimisation: one scope per published surface, two independently
/// cached requests, and no request that spans both contracts.
/// </summary>
public sealed class OutboundCredentialScopeTests
{
    /// <summary>The scope belonging to contract C-03.</summary>
    private const string DataWindowScope = "dataservices.datawindow";

    /// <summary>The scope belonging to contract C-04.</summary>
    private const string ColumnExpressionScope = "dataservices.columnexpression";

    [Fact]
    public void EachDeclaredTokenRequestCarriesExactlyOneScope()
    {
        // THE CORE ASSERTION. A request carrying two scopes is the defect these rows exist for, so the
        // count is pinned at one rather than merely checked for membership.
        foreach ((string name, ServiceTokenRequest request) in DeclaredTokenRequests())
        {
            Assert.Single(request.Scopes);

            Assert.False(
                string.IsNullOrWhiteSpace(request.Audience),
                $"{name} requests no audience, so the credential it obtains would not be bound to a "
                    + "single service.");
        }
    }

    [Fact]
    public void TheTwoRequestsCoverBothPublishedSurfacesAndNeitherSpansThem()
    {
        Dictionary<string, ServiceTokenRequest> declared = DeclaredTokenRequests()
            .ToDictionary(static entry => entry.Name, static entry => entry.Request, StringComparer.Ordinal);

        // BOTH SURFACES ARE STILL REACHABLE - minimisation must not become a capability the client lost.
        // A blanket refusal to request anything would satisfy the row above and break the service.
        string[] requested = [.. declared.Values.SelectMany(static request => request.Scopes).Order(StringComparer.Ordinal)];

        Assert.Equal(
            (string[])[ColumnExpressionScope, DataWindowScope],
            requested);

        // AND THE TWO ARE DISTINCT REQUESTS, so neither surface's credential can authorize the other.
        Assert.Equal(2, declared.Count);
    }

    [Fact]
    public void TheTwoRequestsShareAnAudienceSoOnlyTheScopeDistinguishesThem()
    {
        // ONE AUDIENCE, TWO SCOPES. The audience says WHICH SERVICE may accept the credential and the
        // scope says WHAT it permits there; both requests address the same upstream, so the audience is
        // deliberately identical and the scope is the only difference. Splitting the audience as well
        // would claim DataServices was two services, which the port map and the contracts both deny.
        ServiceTokenRequest[] requests = [.. DeclaredTokenRequests().Select(static entry => entry.Request)];

        Assert.Equal(requests[0].Audience, requests[1].Audience);
        Assert.NotEqual(requests[0].Scopes[0], requests[1].Scopes[0]);
    }

    [Fact]
    public void NoDeclaredRequestSpansBothScopes()
    {
        // THE REGRESSION GUARD, STATED AS THE THING THAT MUST NOT EXIST. Re-merging the two is a one-line
        // edit that reads as a simplification, and the row above would still pass if a THIRD combined
        // request were added beside them - this one would not.
        foreach ((string name, ServiceTokenRequest request) in DeclaredTokenRequests())
        {
            bool spans = request.Scopes.Contains(DataWindowScope)
                && request.Scopes.Contains(ColumnExpressionScope);

            Assert.False(
                spans,
                $"{name} requests both DataServices scopes in one credential. A token captured from any "
                    + "single call would then carry the whole of this client's authority rather than the "
                    + "authority of the call it was taken from.");
        }
    }

    [Fact]
    public void EverySurfaceTheEnumDeclaresHasAMatchingTokenRequest()
    {
        // THE GUARD ON THE GUARD. The rows above read the token requests; this one reads the SURFACE enum
        // and requires the two sets to correspond. Without it, adding a third surface with no request of
        // its own would leave the credential factory throwing at runtime while every row above passed.
        int surfaces = Enum.GetValues<OutboundSurface>().Length;

        Assert.Equal(surfaces, DeclaredTokenRequests().Count);
    }

    /// <summary>
    /// Reads the token requests the outbound client declares, by reflection over its private statics.
    /// </summary>
    /// <returns>Each declared request with the field name that carries it.</returns>
    /// <remarks>
    /// REFLECTION RATHER THAN A LIVE CALL, deliberately. What a credential is REQUESTED for is decided
    /// where these are declared; provoking it through a booted host would require a reachable upstream and
    /// would still only exercise whichever surfaces the test happened to call, which is exactly how one
    /// credential came to serve both contracts unnoticed. Reading the declarations covers every surface,
    /// including any added later.
    /// </remarks>
    private static List<(string Name, ServiceTokenRequest Request)> DeclaredTokenRequests()
    {
        Type client = typeof(DataServicesClient);

        List<(string, ServiceTokenRequest)> found = [];

        foreach (FieldInfo field in client.GetFields(BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (field.FieldType == typeof(ServiceTokenRequest)
                && field.GetValue(null) is ServiceTokenRequest request)
            {
                found.Add((field.Name, request));
            }
        }

        Assert.NotEmpty(found);

        return found;
    }
}
