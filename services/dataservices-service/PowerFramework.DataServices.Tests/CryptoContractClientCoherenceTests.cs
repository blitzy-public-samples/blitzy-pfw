// ==================================================================================================
//  CryptoContractClientCoherenceTests.cs
//  One question, asked in both directions: does DataServices' typed crypto client reach EVERY operation
//  contract C-02 publishes, and does it publish NOTHING C-02 does not?
//  ------------------------------------------------------------------------------------------------
//  WHY THIS SUITE EXISTS, STATED AS THE DEFECT IT CATCHES
//    shared/PowerFramework.Contracts/OpenApi/security.v1.yaml published eighteen C-02 operations while
//    Clients/SecurityClient.cs implemented seventeen. The missing one was DELETE
//    /v1/crypto/rsa/keys/{keyRef} - the release for a generated private key - so a key obtained through
//    generateRsaKey had NO release path from its only consumer, and the published contract carried an
//    operation nothing in the system could reach. Neither half was wrong on its own terms: the document
//    declared a coherent operation and the client implemented a coherent subset. The defect lived
//    entirely in the gap between them, which is exactly the class of fault no test of either half can
//    see.
//
//  FAIL-CLOSED IN BOTH DIRECTIONS, WHICH IS THE PROPERTY THAT MATTERS
//    A one-directional check would let the same defect recur. Asserting only that every client method
//    has a published operation passes trivially on a client that implements nothing; asserting only that
//    every published operation has a client method lets a client accumulate methods for operations the
//    contract never declared. So:
//
//      * every C-02 operationId in the document must map to a typed member on ICryptoServiceClient, and
//      * ICryptoServiceClient must publish no member outside that map.
//
//    A NINETEENTH OPERATION ADDED TO THE DOCUMENT THEREFORE FAILS THIS SUITE UNTIL THE CLIENT REACHES
//    IT, and a method added to the client fails it until the contract declares it. That is the whole
//    point: the next person to publish an unreachable operation finds out at build time rather than in
//    review.
//
//  THE DOCUMENT IS READ, NOT TRANSCRIBED
//    The authored contract is the authority (constraint C-A), so the operation set is parsed from the
//    file itself. A list copied into this file would be a second source of truth that agrees with the
//    client precisely because both were written by the same hand - which would make this suite pass
//    while the product stayed broken.
//
//  PARSED WITH A LINE SCANNER RATHER THAN A YAML LIBRARY, on the same terms as
//  RestProjectionTests.cs's own reading of gateway.v1.yaml: the dependency inventory carries five test
//  packages and no YAML parser, and central package management holds no version for one, so adding it
//  would fail restore outright. The scan reads only the mapping keys of the top-level `paths` block, the
//  operation keys beneath them, and the two scalars `operationId` and `x-contract-id` - all at fixed
//  indentation in this authored file - and it REFUSES rather than returning a short list when it finds
//  nothing, so a structural change to the document breaks this suite loudly instead of quietly reducing
//  what it checks.
//
//  THE MAP FROM operationId TO METHOD NAME IS THE ONE THING THAT MUST BE WRITTEN DOWN, because no rule
//  derives one from the other: the document names operations in camel case and the client names members
//  on the .NET asynchronous convention. It is declared once below and both directions are checked
//  against it, so a wrong entry fails rather than hides.
// ==================================================================================================

using System.Reflection;
using PowerFramework.DataServices.Clients;
using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Coherence between contract C-02's published operation set and the typed client that consumes it.
/// </summary>
public sealed class CryptoContractClientCoherenceTests
{
    /// <summary>
    /// Every published C-02 operation and the client member that reaches it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONE HAND-WRITTEN THING IN THIS SUITE, and it is hand-written because nothing derives it: the
    /// document's <c>generateRandomBlob</c> and the client's <c>GenerateRandomBlobAsync</c> are related by
    /// a naming convention rather than by a rule, and encoding the convention as a transformation would
    /// make the suite assert its own guess about naming instead of the correspondence it is here for.
    /// </para>
    /// <para>
    /// It is checked against the document in both directions, so an entry naming an operation the document
    /// does not declare fails, and an operation the document declares that is absent from here fails too.
    /// The map cannot therefore silently disagree with either side.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> ClientMemberByOperationId =
        new(StringComparer.Ordinal)
        {
            ["hash"] = nameof(ICryptoServiceClient.HashAsync),
            ["hmac"] = nameof(ICryptoServiceClient.HmacAsync),
            ["hashFile"] = nameof(ICryptoServiceClient.HashFileAsync),
            ["hmacFile"] = nameof(ICryptoServiceClient.HmacFileAsync),
            ["symmetricEncrypt"] = nameof(ICryptoServiceClient.SymmetricEncryptAsync),
            ["symmetricDecrypt"] = nameof(ICryptoServiceClient.SymmetricDecryptAsync),
            ["rsaEncrypt"] = nameof(ICryptoServiceClient.RsaEncryptAsync),
            ["rsaDecrypt"] = nameof(ICryptoServiceClient.RsaDecryptAsync),
            ["rsaSign"] = nameof(ICryptoServiceClient.RsaSignAsync),
            ["rsaVerify"] = nameof(ICryptoServiceClient.RsaVerifyAsync),
            ["generateRsaKey"] = nameof(ICryptoServiceClient.GenerateRsaKeyAsync),
            ["releaseRsaKey"] = nameof(ICryptoServiceClient.ReleaseRsaKeyAsync),
            ["generateRandomBlob"] = nameof(ICryptoServiceClient.GenerateRandomBlobAsync),
            ["generateRandomString"] = nameof(ICryptoServiceClient.GenerateRandomStringAsync),
            ["generateGuid"] = nameof(ICryptoServiceClient.GenerateGuidAsync),
            ["stringToBlob"] = nameof(ICryptoServiceClient.StringToBlobAsync),
            ["blobToString"] = nameof(ICryptoServiceClient.BlobToStringAsync),
            ["reverseBlob"] = nameof(ICryptoServiceClient.ReverseBlobAsync),
        };

    /// <summary>
    /// Every operation the authored document publishes under contract C-02 has a typed client member.
    /// </summary>
    /// <remarks>
    /// <b>THIS IS THE ROW THAT FAILED BEFORE THE RELEASE OPERATION WAS IMPLEMENTED.</b> A published
    /// operation with no client member is unreachable from the only service that consumes this contract,
    /// which for the key release meant a generated private key could be retained and never given back.
    /// </remarks>
    [Fact]
    public void EveryPublishedCryptoOperationIsReachableThroughTheTypedClient()
    {
        IReadOnlyList<string> published = CryptoContract.PublishedOperationIds;

        List<string> unreachable = [];

        foreach (string operationId in published)
        {
            if (!ClientMemberByOperationId.TryGetValue(operationId, out string? memberName)
                || typeof(ICryptoServiceClient).GetMethod(
                    memberName,
                    BindingFlags.Instance | BindingFlags.Public) is null)
            {
                unreachable.Add(operationId);
            }
        }

        Assert.Empty(unreachable);
    }

    /// <summary>
    /// The typed client publishes no member that contract C-02 does not declare.
    /// </summary>
    /// <remarks>
    /// THE OTHER DIRECTION, AND IT IS NOT SYMMETRY FOR ITS OWN SAKE. A client method with no published
    /// operation behind it is a route a consumer can call that the contract never promised - which is the
    /// same defect as an unreachable operation seen from the other end, and constraint C-A's
    /// "the published contract is the only cross-service coupling" forbids it in that direction too.
    /// </remarks>
    [Fact]
    public void TheTypedClientPublishesNoMemberContractC02DoesNotDeclare()
    {
        HashSet<string> mapped = new(ClientMemberByOperationId.Values, StringComparer.Ordinal);

        string[] unmapped = typeof(ICryptoServiceClient)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .Where(name => !mapped.Contains(name))
            .ToArray();

        Assert.Empty(unmapped);
    }

    /// <summary>
    /// The map and the document name exactly the same operation set.
    /// </summary>
    /// <remarks>
    /// WITHOUT THIS ROW THE MAP COULD DRIFT AND BOTH ROWS ABOVE WOULD STILL PASS. An entry naming an
    /// operation the document has since renamed would satisfy the reachability row - the member exists -
    /// while pointing at nothing published; and the operation the document actually declares would be
    /// caught, but only by accident. Comparing the two sets directly is what makes the map an assertion
    /// rather than a convenience.
    /// </remarks>
    [Fact]
    public void TheOperationMapNamesExactlyThePublishedOperationSet()
    {
        Assert.Equal(
            CryptoContract.PublishedOperationIds.Order(StringComparer.Ordinal),
            ClientMemberByOperationId.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The published count is eighteen: seventeen covering the legacy overloads and one authored release.
    /// </summary>
    /// <remarks>
    /// AN EXACT NUMBER RATHER THAN AN APPROXIMATE ONE, and it is asserted because the count is stated in
    /// prose in several places - this client's own file header, its interface summary and
    /// <c>docs/CONTRACTS.md</c>'s C-02 rows. A count that is written down in prose and nowhere checked is
    /// a count that goes stale, and it went stale exactly once already: the client claimed seventeen while
    /// the document published eighteen. This row is what turns the next such drift into a failure.
    /// </remarks>
    [Fact]
    public void ContractC02PublishesEighteenOperations()
    {
        Assert.Equal(18, CryptoContract.PublishedOperationIds.Count);
    }

    /// <summary>
    /// The release operation is the one C-02 operation that is not a <c>POST</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TWO THINGS TURN ON THE METHOD, which is why it is asserted rather than assumed. The transport
    /// itself: the client's request/response sender hard-codes <c>POST</c> and serializes a body in both
    /// directions, so a bodiless <c>DELETE</c> needed a sender of its own. And replay safety: a release
    /// must never be retried, and <c>Clients/OutboundCallPolicy.cs</c>'s admission is a property of the
    /// method - <c>OutboundCallPolicyTests</c> asserts that half against the production predicate.
    /// </para>
    /// <para>
    /// The remaining seventeen are asserted to be <c>POST</c> in the same breath, so a future operation
    /// published under another method cannot slip past the sender that assumes one.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnlyTheKeyReleaseIsDeclaredUnderAMethodOtherThanPost()
    {
        Assert.Equal("delete", CryptoContract.MethodOf("releaseRsaKey"));

        string[] notPost = CryptoContract.PublishedOperationIds
            .Where(operationId => !string.Equals(operationId, "releaseRsaKey", StringComparison.Ordinal))
            .Where(operationId => !string.Equals(
                CryptoContract.MethodOf(operationId),
                "post",
                StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(notPost);
    }
}

/// <summary>
/// The authored contract C-02 surface, read out of
/// <c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c> itself.
/// </summary>
/// <remarks>
/// Read once and cached: the file cannot change during a run, and the scan would otherwise repeat for
/// every row.
/// </remarks>
internal static class CryptoContract
{
    /// <summary>The contract identifier the document tags each operation with.</summary>
    private const string ContractId = "C-02";

    /// <summary>The authored document, relative to the repository root.</summary>
    private const string ContractPath = "shared/PowerFramework.Contracts/OpenApi/security.v1.yaml";

    /// <summary>The document member that opens the path mapping.</summary>
    private const string PathsKey = "paths:";

    /// <summary>The operation keys an OpenAPI path item may declare, as the specification closes the set.</summary>
    /// <remarks>
    /// A path item may carry keys that are NOT operations - <c>parameters</c>, <c>summary</c>,
    /// <c>description</c>, <c>servers</c> - at the same indentation, so the scan matches against this set
    /// rather than against "any key at operation level".
    /// </remarks>
    private static readonly HashSet<string> HttpMethodKeys = new(StringComparer.Ordinal)
    {
        "get",
        "put",
        "post",
        "delete",
        "options",
        "head",
        "patch",
        "trace",
    };

    /// <summary>Every declared operation, in document order, with its method and contract tag.</summary>
    private static readonly IReadOnlyList<DeclaredOperation> Declared = ReadDeclared();

    /// <summary>The operation identifiers the document publishes under contract C-02, in document order.</summary>
    internal static IReadOnlyList<string> PublishedOperationIds { get; } = Declared
        .Where(declaration => string.Equals(declaration.ContractId, ContractId, StringComparison.Ordinal))
        .Select(declaration => declaration.OperationId)
        .ToArray();

    /// <summary>Reports the HTTP method one published operation is declared under.</summary>
    /// <param name="operationId">The operation identifier as the document spells it.</param>
    /// <returns>The method key, lower-cased exactly as the document writes it.</returns>
    internal static string MethodOf(string operationId)
    {
        DeclaredOperation declaration = Declared.Single(candidate =>
            string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal));

        return declaration.Method;
    }

    /// <summary>Scans the authored document for every operation it declares.</summary>
    /// <returns>The declarations, in document order.</returns>
    /// <exception cref="InvalidOperationException">
    /// The document could not be located or declares no operation at all.
    /// </exception>
    private static IReadOnlyList<DeclaredOperation> ReadDeclared()
    {
        string contract = RestProjectionContract.Resolve(ContractPath);

        List<DeclaredOperation> declared = [];
        bool insidePaths = false;
        string? currentMethod = null;
        string? currentOperationId = null;
        string? currentContractId = null;

        void Flush()
        {
            if (currentMethod is not null && currentOperationId is not null)
            {
                declared.Add(new DeclaredOperation(
                    currentMethod,
                    currentOperationId,
                    currentContractId ?? string.Empty));
            }

            currentMethod = null;
            currentOperationId = null;
            currentContractId = null;
        }

        foreach (string line in File.ReadLines(contract))
        {
            if (line.Length == 0)
            {
                continue;
            }

            // A top-level key. `paths:` opens the mapping and any other one closes it, which bounds the
            // scan to the block that carries operations.
            if (!char.IsWhiteSpace(line[0]))
            {
                Flush();
                insidePaths = line.StartsWith(PathsKey, StringComparison.Ordinal);

                continue;
            }

            if (!insidePaths)
            {
                continue;
            }

            string trimmed = line.Trim();

            // A path key: exactly two spaces of indentation, a leading slash and a trailing colon.
            if (line.StartsWith("  /", StringComparison.Ordinal)
                && !line.StartsWith("   ", StringComparison.Ordinal)
                && trimmed.EndsWith(':'))
            {
                Flush();

                continue;
            }

            // An operation key: four spaces of indentation and a member of the closed method set.
            if (line.StartsWith("    ", StringComparison.Ordinal)
                && !line.StartsWith("     ", StringComparison.Ordinal)
                && trimmed.EndsWith(':')
                && HttpMethodKeys.Contains(trimmed[..^1]))
            {
                Flush();
                currentMethod = trimmed[..^1];

                continue;
            }

            if (currentMethod is null)
            {
                continue;
            }

            if (trimmed.StartsWith("operationId:", StringComparison.Ordinal))
            {
                currentOperationId = trimmed["operationId:".Length..].Trim();
            }
            else if (trimmed.StartsWith("x-contract-id:", StringComparison.Ordinal))
            {
                currentContractId = trimmed["x-contract-id:".Length..].Trim();
            }
        }

        Flush();

        // REFUSE RATHER THAN RETURN A SHORT LIST. A structural change to the document that defeated the
        // scan would otherwise leave every row above asserting nothing at all.
        if (declared.Count == 0)
        {
            throw new InvalidOperationException(
                $"No operation was read from '{contract}', so the C-02 coherence rows would assert "
                + "nothing. The document's structure has changed and this scan needs revisiting.");
        }

        return declared;
    }

    /// <summary>One operation as the authored document declares it.</summary>
    /// <param name="Method">The HTTP method key, as the document spells it.</param>
    /// <param name="OperationId">The operation identifier.</param>
    /// <param name="ContractId">The <c>x-contract-id</c> tag, or empty when the operation carries none.</param>
    private sealed record DeclaredOperation(string Method, string OperationId, string ContractId);
}
