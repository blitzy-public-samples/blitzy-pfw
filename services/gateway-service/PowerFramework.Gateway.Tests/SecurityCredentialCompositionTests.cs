// ==================================================================================================
//  SecurityCredentialCompositionTests - THE HELD CREDENTIAL IS ACTUALLY HELD
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   The registrations Program.cs makes for Gateway's token-issuance edge: the single
//            credential store, the scoped client that reads it, and the interface every authenticated
//            outbound call goes through.
//
//  WHY THIS FILE EXISTS
//  ------------------------------------------------------------------------------------------------
//  A typed HttpClient is registered TRANSIENT by the framework. With the credential store owned by
//  SecurityClient as a field, every resolve produced an EMPTY store - so the "reuse a held credential
//  while it remains valid" path documented on the client's token accessor was never reached across
//  calls in a running host, and Gateway asked Security to mint a fresh token for every request that
//  needed one. Nothing failed and no test caught it: the behaviour was correct per call and wrong per
//  process, and the composition root's own remarks described the intended arrangement rather than the
//  delivered one.
//
//  WHAT THESE TESTS GUARD
//  ------------------------------------------------------------------------------------------------
//    - The store is a SINGLETON, so a credential obtained by one request is visible to the next.
//    - The client is SCOPED, so one request reaches one client and the token provider a consumer is
//      handed is the same object that obtained the credential.
//    - The client's channel is resolved under the REGISTERED name, which is what keeps the pinned
//      trust anchor, the client identity and the resilience pipeline attached to it. Resolving an
//      unregistered name silently yields a default-configured client, and that is precisely how the
//      health probe once ended up on platform default trust.
//
//  NOTHING IS SENT BY THIS FILE. Composition is the subject: every test resolves and inspects.
// ==================================================================================================

using System.Net.Http;
using PowerFramework.Gateway.Clients;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PowerFramework.Gateway.Tests;

public sealed class SecurityCredentialCompositionTests(GatewayTestHostFixture host)
    : IClassFixture<GatewayTestHostFixture>
{
    /// <summary>
    /// The credential store is one object for the whole process.
    /// </summary>
    /// <remarks>
    /// THE STORE IS WHAT MAKES REUSE REAL, and its lifetime is the only thing that could make it not. It
    /// holds no connection and no handler - only short-lived tokens keyed by audience and scope - so
    /// unlike the client itself it is safe to keep for the life of the process, and it has to be kept for
    /// that long or no second request can ever observe the first one's credential.
    /// </remarks>
    [Fact]
    public void TheCredentialStoreIsASingletonSharedAcrossScopes()
    {
        using IServiceScope first = host.Services.CreateScope();
        using IServiceScope second = host.Services.CreateScope();

        Assert.Same(
            first.ServiceProvider.GetRequiredService<ServiceTokenCache>(),
            second.ServiceProvider.GetRequiredService<ServiceTokenCache>());
    }

    /// <summary>
    /// One scope resolves one client, however many times it is asked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EVERY AUTHENTICATED OUTBOUND CALL GOES THROUGH THIS CLIENT, and <c>DataServicesClient</c> - the
    /// consumer that carries the credential onto the gRPC edge - is itself scoped. Pinning the client to
    /// the same lifetime is what makes the object that obtains the credential and the object that uses it
    /// one, rather than two clients that happen to agree because they share a store. A transient
    /// registration returned a new client to every consumer in the same request.
    /// </para>
    /// <para>
    /// THE INTERFACE IS NOT ASSERTED HERE, AND DELIBERATELY SO. This fixture substitutes
    /// <c>IServiceTokenProvider</c> with a refusing double on purpose - that substitution is what lets the
    /// authorization suite prove no request escapes without a credential - so asserting the interface
    /// against it would be asserting the double. The interface-to-client identity is asserted against a
    /// DEPLOYED container in the DataServices suite, which is where the two-interface case that raised the
    /// finding actually lives.
    /// </para>
    /// </remarks>
    [Fact]
    public void OneScopeResolvesOneClientHoweverOftenItIsAsked()
    {
        using IServiceScope scope = host.Services.CreateScope();

        SecurityClient client = scope.ServiceProvider.GetRequiredService<SecurityClient>();

        Assert.Same(client, scope.ServiceProvider.GetRequiredService<SecurityClient>());
        Assert.Same(client, scope.ServiceProvider.GetRequiredService<SecurityClient>());
    }

    /// <summary>
    /// Two scopes hold two clients, and both read the one store.
    /// </summary>
    /// <remarks>
    /// THIS IS THE TEST THAT STOPS THE STORE BEING COLLAPSED BACK INTO THE CLIENT. Separating the two
    /// lifetimes is deliberate: the client is per-scope because its transport is factory-managed and
    /// recycled, while the credential outlives the request that obtained it and is valid for any caller of
    /// the same audience and scope set. Collapsing them would restore per-request minting while every
    /// other assertion here still passed.
    /// </remarks>
    [Fact]
    public void DifferentScopesHoldDifferentClientsButShareOneStore()
    {
        using IServiceScope first = host.Services.CreateScope();
        using IServiceScope second = host.Services.CreateScope();

        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<SecurityClient>(),
            second.ServiceProvider.GetRequiredService<SecurityClient>());
        Assert.Same(
            first.ServiceProvider.GetRequiredService<ServiceTokenCache>(),
            second.ServiceProvider.GetRequiredService<ServiceTokenCache>());
    }

    /// <summary>
    /// The client's channel is registered under the name the client publishes, so the configured
    /// address, trust anchor, client identity and resilience pipeline are all attached to what it uses.
    /// </summary>
    /// <remarks>
    /// A NAME MISMATCH IS SILENT, WHICH IS WHY IT IS ASSERTED. The client factory answers any name: an
    /// unregistered one yields a default-configured client with no base address, platform default trust,
    /// no client certificate and no resilience handler, and the first symptom is a failed handshake or a
    /// relative-URI exception far from the cause. Asserting the base address is what proves the resolved
    /// channel is the CONFIGURED one rather than a default.
    /// </remarks>
    [Fact]
    public void TheRegisteredChannelNameIsTheOneTheClientResolves()
    {
        HttpClient channel = host.Services
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(SecurityClient.HttpClientName);

        Assert.NotNull(channel.BaseAddress);
        Assert.Equal(Uri.UriSchemeHttps, channel.BaseAddress!.Scheme);
    }
}
