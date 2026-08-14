// ======================================================================================================
//  THE DATA-PROTECTION KEY RING IS DELIBERATELY IN MEMORY, AND THIS FILE IS THE ASSERTION THAT IT IS.
//
//  AddAuthentication registers the ASP.NET Core data-protection stack whether or not anything protects a
//  payload - the authentication assembly calls AddDataProtection for the ticket formats its remote handlers
//  use, and this service registers no remote handler. Data protection's own eager initialiser then
//  materialises a key ring during host start. That was MEASURED on all four services rather than inferred:
//  each wrote an unencrypted private key into the process's user profile at startup, and one host that
//  afterwards FAILED TO BIND ITS PORT had already written it.
//
//  Nothing here protects a payload. Inbound authentication is bearer-token validation against Security's
//  published key set - stateless, no protector - and there is no cookie, no session, no antiforgery token
//  and no protected payload that outlives a request. The default therefore wrote key material to disk for
//  NO CONSUMER, which is a secret at rest with no purpose.
//
//  🔴 WHY THE ASSERTION IS ON THE REPOSITORY AND NOT ON THE PROVIDER. Swapping IDataProtectionProvider for
//  the framework's ephemeral provider was tried first and MEASURED TO BE INSUFFICIENT: a key file was still
//  written on every start, because the eager initialiser warms the KEY-MANAGEMENT stack rather than
//  whichever provider is registered. The repository is the layer that decides where bytes go, so it is the
//  layer these rows pin. An assertion on the absence of a file could not distinguish the posture from a test
//  process that shares a key ring an earlier run created, and would go green for the wrong reason.
//
//  docs/SECRETS.md section 5.4 records the posture, the rejected alternative and what a later phase must
//  put in its place.
// ======================================================================================================

using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Pins the composition root's data-protection posture: the key ring is in memory, and nothing is written.
/// </summary>
public sealed class DataProtectionPostureTests
{
    /// <summary>
    /// The configured key repository is this service's in-memory one, so no file-system repository exists.
    /// </summary>
    /// <remarks>
    /// ASSERTED ON THE REAL COMPOSITION ROOT rather than on a restatement of it. The registration lives in
    /// Program.cs, so a row that configured the option itself would pass while the host was wired to the
    /// default - which is precisely the state this exists to prevent.
    /// </remarks>
    [Fact]
    public void TheConfiguredKeyRepositoryIsInMemoryRatherThanOnDisk()
    {
        using CompositionHost host = CompositionHost.Create();
        using HttpClient client = host.CreateClient();

        KeyManagementOptions options = host.Services
            .GetRequiredService<IOptions<KeyManagementOptions>>()
            .Value;

        Assert.NotNull(options.XmlRepository);
        Assert.Equal(
            typeof(InMemoryDataProtectionKeyRepository),
            options.XmlRepository.GetType());
    }

    /// <summary>
    /// The eager initialiser's key landed IN that repository, which is what proves nothing went to disk.
    /// </summary>
    /// <remarks>
    /// THE ROW THAT CATCHES THE DEFECT THE FIRST ATTEMPT LEFT BEHIND. A repository that is configured but
    /// never reached would satisfy the row above while the key-management stack still wrote a file - so this
    /// one asserts the ring was actually created THROUGH it. The element count is not asserted exactly,
    /// because how many keys the initialiser creates is the framework's business; that it created them here
    /// is this service's.
    /// </remarks>
    [Fact]
    public void TheKeyRingWasCreatedInThatRepository()
    {
        using CompositionHost host = CompositionHost.Create();
        using HttpClient client = host.CreateClient();

        IXmlRepository repository = Assert.IsType<InMemoryDataProtectionKeyRepository>(
            host.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);

        // Force the ring to exist even if the eager initialiser has not run yet on this host.
        _ = host.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("PowerFramework.Tests.DataProtectionPosture")
            .Protect("payload");

        Assert.NotEmpty(repository.GetAllElements());
    }

    /// <summary>
    /// The posture is functional rather than merely absent: a protector still round-trips in process.
    /// </summary>
    /// <remarks>
    /// THE POSITIVE ARM, AND IT IS NOT DECORATION. "In memory" must mean "keys live and die with this
    /// process", never "protection is broken" - a future consumer has to fail on the RESTART boundary,
    /// loudly and unmistakably, rather than on its first call in a way that reads as a wiring bug.
    /// </remarks>
    [Fact]
    public void AProtectorStillRoundTripsWithinTheProcess()
    {
        using CompositionHost host = CompositionHost.Create();
        using HttpClient client = host.CreateClient();

        IDataProtector protector = host.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("PowerFramework.Tests.DataProtectionPosture");

        Assert.Equal("payload", protector.Unprotect(protector.Protect("payload")));
    }

    /// <summary>
    /// The repository hands out snapshots and clones, so a caller cannot mutate the stored ring.
    /// </summary>
    /// <remarks>
    /// THE KEY MANAGER IS FREE TO EDIT WHAT IT IS HANDED. A repository that returned its own instances would
    /// let one reader's edit reach another's read, and a key ring that mutates under a concurrent reader is a
    /// fault that presents as an intermittent decryption failure - the hardest shape to diagnose.
    /// </remarks>
    [Fact]
    public void StoredElementsAreClonedInBothDirections()
    {
        InMemoryDataProtectionKeyRepository repository = new();
        XElement stored = new("key", new XAttribute("id", "one"));

        repository.StoreElement(stored, "one");
        stored.SetAttributeValue("id", "mutated-after-storing");

        XElement first = Assert.Single(repository.GetAllElements());
        Assert.Equal("one", first.Attribute("id")?.Value);

        first.SetAttributeValue("id", "mutated-after-reading");

        Assert.Equal("one", Assert.Single(repository.GetAllElements()).Attribute("id")?.Value);
    }

    /// <summary>A null element is refused rather than stored.</summary>
    [Fact]
    public void ANullElementIsRefused() =>
        Assert.Throws<ArgumentNullException>(
            () => new InMemoryDataProtectionKeyRepository().StoreElement(null!, "name"));
}
