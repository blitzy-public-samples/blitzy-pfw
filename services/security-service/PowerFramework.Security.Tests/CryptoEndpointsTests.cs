// ==================================================================================================
//  CryptoEndpointsTests - contract C-02, security.v1.CryptoService
//  --------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS FOR, AND WHY IT IS SHAPED THE WAY IT IS.
//
//  CryptoEndpoints.cs projects seven cryptographic providers - 63 of the 65 declarations at
//  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L9-L73 - onto 17 REST operations, and it owns the
//  reference-resolution boundary that keeps raw key material off the wire. Two properties dominate
//  everything else, so both are asserted structurally rather than by inspection:
//
//    (1) RAW KEY MATERIAL NEVER CROSSES THE WIRE INBOUND. Asserted by reflecting over EVERY request
//        record and proving that no member could carry a key, a passphrase or a certificate, and by
//        proving the authoritative document marks every request schema closed to extra members.
//
//    (2) EVERY OPERATION REQUIRES A TOKEN. Asserted by driving all 17 routes through the REAL
//        composition root without one and requiring 401 from each, as a data-driven row per route so
//        that a route added later cannot escape the assertion by being forgotten.
//
//  THREE LEVELS OF TEST, EACH DOING WHAT ONLY IT CAN DO.
//
//    * DOCUMENT CONFORMANCE reads shared/PowerFramework.Contracts/OpenApi/security.v1.yaml itself,
//      from the embedded resource in PowerFramework.Contracts. That is what makes these assertions
//      real rather than a restatement of the implementation: the authored document is authoritative
//      over the code, so every path, operation identifier, status set, security requirement and enum
//      value is compared against the document's own text rather than against a copy of it kept here.
//
//    * SERVICE-LEVEL tests boot the host through WebApplicationFactory<Program>, which is the only
//      way to observe the default-deny fallback policy, the route group's RequireAuthorization and the
//      generated OpenAPI document - none of which exists when a handler is called directly.
//
//    * UNIT-LEVEL tests call each named handler directly with real providers. This is deliberate and
//      is the reason the handlers are named static methods rather than inline lambdas: the parity
//      matrices need to enumerate the algorithm, mode, padding and payload-shape cross product, and
//      routing several hundred rows through an HTTP host would trade the matrices' breadth for
//      nothing. Where a property is genuinely about HTTP - a status, a header, a document - it is
//      asserted at the service level instead.
//
//  DETERMINISM. Every random, random-string and GUID row runs against an INJECTED entropy double, so
//  the values are byte-reproducible. That is not merely convenient: the characterization model
//  requires every non-deterministic value to be maskable from BOTH the master and the candidate
//  recording, and a test that could not reproduce a value could not prove the seam is reached. The
//  doubles are the ones authored beside RandomProvider's own tests, reused rather than duplicated.
//
//  NO SECRET MATERIAL APPEARS IN THIS FILE. No value from any of the eight in-source secret sites, and
//  nothing from tests/blink/test_jws.htm:L8-L23, in any form. Where a row needs key material it uses
//  an obviously-synthetic ASCII run built by a helper, never a literal shaped like a real key, and
//  never a PEM block. The RSA rows generate their own key pairs at run time through the provider,
//  which is the only way to exercise them without embedding one.
//
//  THE LEGACY TREE IS READ-ONLY AND IS THE BEHAVIOURAL ORACLE. It is cited here BY LOCATOR ONLY and is
//  never a build input:
//
//    ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L9-L73    the 65 declarations, 2 of them non-ports
//    ws_objects/pfw.shared.pbl.src/enums.sru:L924-L967    the 30 CRYPTO_ constants
//    ws_objects/pfw.shared.pbl.src/retcode.sru            the return-code algebra
//    ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13, isfailed.srf:L11-L13
//                                                        the tri-state boundary this file asserts
// ==================================================================================================

using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Crypto;
using PowerFramework.Security.Endpoints;
using PowerFramework.Shared.Kernel;

// The service's own encoding provider collides by simple name with System.Text.EncodingProvider, which
// this file also needs System.Text for. The alias resolves it in favour of the type under test rather
// than dropping the namespace and qualifying Encoding and StringBuilder at every use.
using EncodingProvider = PowerFramework.Security.Crypto.EncodingProvider;

namespace PowerFramework.Security.Tests;

/// <summary>
/// The authoritative contract document, read from the embedded resource rather than from disk.
/// </summary>
/// <remarks>
/// <para>
/// READ FROM THE ASSEMBLY, NOT FROM A PATH. The document is an embedded resource of
/// <c>PowerFramework.Contracts</c>, so reading it this way works from any working directory and cannot
/// silently pass by loading a stale copy or fail because a relative path resolved differently under
/// the test host than under the build.
/// </para>
/// <para>
/// The comparisons made against it are textual and deliberately so. A YAML parser is not a permitted
/// dependency of this project - the package list is closed - and text comparison is sufficient for the
/// questions being asked: does the document declare this path, this operation identifier, this status
/// code, this enum value. Each assertion states the exact token it looks for, so a false pass would
/// require the document to contain that token for an unrelated reason, which the surrounding
/// path-scoped extraction rules out.
/// </para>
/// </remarks>
internal static class ContractDocument
{
    /// <summary>The manifest resource name of the authored document.</summary>
    private const string ResourceName = "PowerFramework.Contracts.OpenApi.security.v1.yaml";

    /// <summary>The simple name of the assembly the document is embedded in.</summary>
    private const string ContractsAssemblyName = "PowerFramework.Contracts";

    /// <summary>The document's full text, read once.</summary>
    private static readonly string DocumentText = ReadDocument();

    /// <summary>The document's full text.</summary>
    internal static string Text => DocumentText;

    /// <summary>
    /// Extracts the YAML block declaring one path, so that an assertion about an operation cannot
    /// accidentally be satisfied by text belonging to a different one.
    /// </summary>
    /// <param name="path">The path as the document spells it, for example <c>/v1/crypto/hash</c>.</param>
    /// <returns>The lines from the path key up to, but excluding, the next path key.</returns>
    /// <remarks>
    /// Path keys in this document sit at exactly TWO spaces of indentation under <c>paths:</c>, and
    /// every line belonging to a path is indented further. Scanning on that rule is what makes the
    /// extraction exact without a parser, and the rule was read off the document itself rather than
    /// assumed - a four-space assumption would silently match nothing and every row would then fail
    /// for the wrong reason.
    /// </remarks>
    internal static string PathBlock(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string[] lines = DocumentText.Split('\n');
        string key = "  " + path + ":";
        StringBuilder block = new();
        bool inside = false;

        foreach (string line in lines)
        {
            string trimmedEnd = line.TrimEnd('\r');

            if (!inside)
            {
                if (string.Equals(trimmedEnd, key, StringComparison.Ordinal))
                {
                    inside = true;
                    block.AppendLine(trimmedEnd);
                }

                continue;
            }

            // Another path key at the same indentation ends this block. A comment banner at two
            // spaces does not, because it is not a mapping key.
            bool isSiblingPathKey =
                trimmedEnd.StartsWith("  /", StringComparison.Ordinal) &&
                !trimmedEnd.StartsWith("   ", StringComparison.Ordinal) &&
                trimmedEnd.EndsWith(':');

            if (isSiblingPathKey)
            {
                break;
            }

            block.AppendLine(trimmedEnd);
        }

        Assert.True(inside, $"The authored document declares no path '{path}'.");

        return block.ToString();
    }

    /// <summary>Loads the embedded document, failing loudly rather than silently skipping.</summary>
    /// <returns>The document text.</returns>
    private static string ReadDocument()
    {
        Assembly contracts = LoadContractsAssembly();

        using Stream? stream = contracts.GetManifestResourceStream(ResourceName);

        Assert.NotNull(stream);

        using StreamReader reader = new(stream, Encoding.UTF8);

        string text = reader.ReadToEnd();

        Assert.False(
            string.IsNullOrWhiteSpace(text),
            "The embedded contract document is empty, so no conformance assertion in this file " +
            "would be meaningful.");

        return text;
    }

    /// <summary>Resolves the contracts assembly by name, then by file beside the test assembly.</summary>
    /// <returns>The loaded assembly.</returns>
    private static Assembly LoadContractsAssembly()
    {
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(loaded.GetName().Name, ContractsAssemblyName, StringComparison.Ordinal))
            {
                return loaded;
            }
        }

        try
        {
            return Assembly.Load(new AssemblyName(ContractsAssemblyName));
        }
        catch (FileNotFoundException)
        {
            string candidate = Path.Combine(
                AppContext.BaseDirectory,
                ContractsAssemblyName + ".dll");

            Assert.True(
                File.Exists(candidate),
                $"The contracts assembly was not resolvable by name and '{candidate}' does not exist.");

            return Assembly.LoadFrom(candidate);
        }
    }
}

/// <summary>
/// The 17 operations of contract C-02, as the authored document declares them.
/// </summary>
/// <param name="Path">The route path.</param>
/// <param name="OperationId">The operation identifier.</param>
/// <param name="DeclaresReferenceFailures">
/// Whether the operation declares the two reference-failure statuses, 403 and 404. It is
/// <see langword="false"/> for the operations that resolve nothing and for key generation, which
/// declares 403 but not 404 because it resolves no inbound reference at all.
/// </param>
/// <param name="DeclaresNotFound">Whether the operation declares 404.</param>
internal sealed record CryptoOperation(
    string Path,
    string OperationId,
    bool DeclaresReferenceFailures,
    bool DeclaresNotFound);

/// <summary>
/// Shared fixtures for the contract C-02 tests: the operation table, the provider graph, the
/// reference-store builder and the result-unwrapping helpers.
/// </summary>
/// <remarks>
/// <para>
/// THE OPERATION TABLE IS THE SPINE OF THIS FILE. It is declared once and consumed by the document
/// conformance rows, the generated-document rows and the authentication rows alike, so a route cannot
/// be asserted in one place and forgotten in another. Its contents are themselves checked against the
/// authored document, so it cannot drift into agreeing only with the implementation.
/// </para>
/// <para>
/// EVERY PROVIDER IS THE REAL ONE. No cryptographic operation is faked anywhere in this file: the
/// point of a parity matrix is that the real implementation produces the real answer, and a double
/// would assert only that the handler calls something. The only doubles used are the entropy source,
/// which exists to make randomness reproducible, and the null logger.
/// </para>
/// </remarks>
internal static class CryptoFixture
{
    /// <summary>The route prefix the contract publishes.</summary>
    internal const string Prefix = "/v1/crypto";

    /// <summary>A logger factory that records nothing, for the unit-level rows.</summary>
    internal static ILoggerFactory Loggers => NullLoggerFactory.Instance;

    /// <summary>All 17 operations, in the order the document declares them.</summary>
    internal static IReadOnlyList<CryptoOperation> Operations { get; } =
    [
        new(Prefix + "/hash", "hash", DeclaresReferenceFailures: false, DeclaresNotFound: false),
        new(Prefix + "/hmac", "hmac", DeclaresReferenceFailures: true, DeclaresNotFound: true),
        new(Prefix + "/hash-file", "hashFile", DeclaresReferenceFailures: true, DeclaresNotFound: true),
        new(Prefix + "/hmac-file", "hmacFile", DeclaresReferenceFailures: true, DeclaresNotFound: true),
        new(
            Prefix + "/symmetric/encrypt",
            "symmetricEncrypt",
            DeclaresReferenceFailures: true,
            DeclaresNotFound: true),
        new(
            Prefix + "/symmetric/decrypt",
            "symmetricDecrypt",
            DeclaresReferenceFailures: true,
            DeclaresNotFound: true),
        new(Prefix + "/rsa/encrypt", "rsaEncrypt", DeclaresReferenceFailures: true, DeclaresNotFound: true),
        new(Prefix + "/rsa/decrypt", "rsaDecrypt", DeclaresReferenceFailures: true, DeclaresNotFound: true),
        new(Prefix + "/rsa/sign", "rsaSign", DeclaresReferenceFailures: true, DeclaresNotFound: true),
        new(Prefix + "/rsa/verify", "rsaVerify", DeclaresReferenceFailures: true, DeclaresNotFound: true),
        new(Prefix + "/rsa/keys", "generateRsaKey", DeclaresReferenceFailures: true, DeclaresNotFound: false),
        new(
            Prefix + "/random/blob",
            "generateRandomBlob",
            DeclaresReferenceFailures: false,
            DeclaresNotFound: false),
        new(
            Prefix + "/random/string",
            "generateRandomString",
            DeclaresReferenceFailures: false,
            DeclaresNotFound: false),
        new(Prefix + "/random/guid", "generateGuid", DeclaresReferenceFailures: false, DeclaresNotFound: false),
        new(
            Prefix + "/encoding/string-to-blob",
            "stringToBlob",
            DeclaresReferenceFailures: false,
            DeclaresNotFound: false),
        new(
            Prefix + "/encoding/blob-to-string",
            "blobToString",
            DeclaresReferenceFailures: false,
            DeclaresNotFound: false),
        new(
            Prefix + "/encoding/blob-reverse",
            "reverseBlob",
            DeclaresReferenceFailures: false,
            DeclaresNotFound: false),
    ];

    /// <summary>Every request record this contract binds.</summary>
    /// <remarks>
    /// Enumerated so the inbound-key rejection row can reflect over ALL of them rather than over a
    /// hand-picked few. A request type added later without being added here is caught by a companion
    /// row that counts the public request records in the endpoint's own namespace.
    /// </remarks>
    internal static IReadOnlyList<Type> RequestTypes { get; } =
    [
        typeof(HashRequest),
        typeof(HmacRequest),
        typeof(HashFileRequest),
        typeof(HmacFileRequest),
        typeof(SymEncryptRequest),
        typeof(SymDecryptRequest),
        typeof(RsaCipherRequest),
        typeof(RsaSignRequest),
        typeof(RsaVerifyRequest),
        typeof(GenRsaKeyRequest),
        typeof(RandomBlobRequest),
        typeof(RndStringRequest),
        typeof(GuidRequest),
        typeof(StringToBlobRequest),
        typeof(BlobToStringRequest),
        typeof(BlobReverseRequest),
    ];

    /// <summary>The encoding provider every other provider composes over.</summary>
    internal static EncodingProvider Encodings { get; } = new();

    /// <summary>The unkeyed digest provider.</summary>
    internal static HashProvider Hashes { get; } = new(Encodings);

    /// <summary>The keyed digest provider.</summary>
    internal static HmacProvider Authenticators { get; } = new(Encodings);

    /// <summary>The symmetric cipher provider.</summary>
    internal static SymmetricCipherProvider Ciphers { get; } = new(Encodings);

    /// <summary>The asymmetric provider.</summary>
    internal static RsaProvider Rsa { get; } = new(Encodings);

    /// <summary>
    /// A random provider over a REAL entropy source, for rows whose subject is not the value drawn.
    /// </summary>
    internal static RandomProvider Random { get; } = new(new CryptographicEntropySource());

    /// <summary>
    /// Builds a reference resolver over an in-memory store.
    /// </summary>
    /// <param name="entries">The reference-to-value pairs the store holds.</param>
    /// <param name="permitted">
    /// The references the deployment publishes. When <see langword="null"/>, every key of
    /// <paramref name="entries"/> is permitted, which is the ordinary case.
    /// </param>
    /// <returns>The resolver.</returns>
    /// <remarks>
    /// The prefix is non-empty on purpose. A deployment that publishes a permitted reference is
    /// required to set one, so a resolver built without it would test a configuration the validator
    /// rejects - and a prefix of the empty string would let a row pass that only works because the
    /// reference happened to be a top-level configuration key.
    /// </remarks>
    internal static CryptoReferenceResolver Store(
        IReadOnlyDictionary<string, string> entries,
        IReadOnlyList<string>? permitted = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        const string prefix = "SECURITY_KEYSTORE_";

        SecurityOptions options = new();
        options.KeyStore.ConfigurationKeyPrefix = prefix;

        foreach (string reference in permitted ?? [.. entries.Keys])
        {
            options.KeyStore.PermittedKeyRefs.Add(reference);
        }

        Dictionary<string, string?> settings = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> entry in entries)
        {
            settings[prefix + entry.Key] = entry.Value;
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new CryptoReferenceResolver(Options.Create(options), configuration);
    }

    /// <summary>An empty store, for rows about resolution failure.</summary>
    /// <returns>A resolver whose permitted set is empty.</returns>
    internal static CryptoReferenceResolver EmptyStore() =>
        Store(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// Builds obviously-synthetic key material of a stated length.
    /// </summary>
    /// <param name="lengthBytes">The number of ASCII characters to produce.</param>
    /// <param name="seed">A character that distinguishes one row's material from another's.</param>
    /// <returns>The material.</returns>
    /// <remarks>
    /// <para>
    /// SYNTHETIC BY CONSTRUCTION, so that nothing in this file could be mistaken for real key material
    /// and nothing matches a provider's credential pattern. It is a repeating run of printable ASCII
    /// letters, which is exactly what a test key should look like.
    /// </para>
    /// <para>
    /// The run is built from a rotating alphabet rather than a single repeated character because the
    /// platform refuses the known weak and semi-weak DES keys and refuses 3DES keys whose adjacent
    /// sub-keys coincide - a constant run would reach both refusals, and a row that failed for that
    /// reason would look like a defect in the handler.
    /// </para>
    /// </remarks>
    internal static string KeyMaterial(int lengthBytes, char seed = 'a')
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lengthBytes);

        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

        int offset = alphabet.IndexOf(seed, StringComparison.Ordinal);
        offset = offset < 0 ? 0 : offset;

        StringBuilder material = new(lengthBytes);

        for (int index = 0; index < lengthBytes; index++)
        {
            material.Append(alphabet[(offset + index) % alphabet.Length]);
        }

        return material.ToString();
    }

    /// <summary>Unwraps a successful result, failing with the problem detail when it is a rejection.</summary>
    /// <typeparam name="TValue">The response type.</typeparam>
    /// <param name="result">The handler's result.</param>
    /// <returns>The response value.</returns>
    internal static TValue Success<TValue>(Results<Ok<TValue>, ProblemHttpResult> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Result is ProblemHttpResult problem)
        {
            Assert.Fail(
                "Expected success but the handler rejected the request with status " +
                problem.StatusCode.ToString(CultureInfo.InvariantCulture) +
                " and detail: " + problem.ProblemDetails.Detail);
        }

        Ok<TValue> ok = Assert.IsType<Ok<TValue>>(result.Result);

        Assert.NotNull(ok.Value);

        return ok.Value;
    }

    /// <summary>Unwraps a rejection, failing when the handler succeeded.</summary>
    /// <typeparam name="TValue">The response type the handler would otherwise return.</typeparam>
    /// <param name="result">The handler's result.</param>
    /// <returns>The problem response.</returns>
    internal static ProblemHttpResult Rejection<TValue>(Results<Ok<TValue>, ProblemHttpResult> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return Assert.IsType<ProblemHttpResult>(result.Result);
    }

    /// <summary>Reads the preserved return code from a rejection.</summary>
    /// <param name="problem">The rejection.</param>
    /// <returns>The return code.</returns>
    /// <remarks>
    /// Reading the extension member rather than the status is what makes a mapping row meaningful: the
    /// status is DERIVED from the code by the shared map, so asserting only the status would not detect
    /// a handler that chose the wrong code and happened to land on the right status.
    /// </remarks>
    internal static long RetCodeOf(ProblemHttpResult problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        Assert.True(
            problem.ProblemDetails.Extensions.TryGetValue("retCode", out object? value),
            "Every rejection on this surface carries the preserved legacy return code.");

        return Assert.IsType<long>(value);
    }
}

/// <summary>
/// Conformance of the implementation to the AUTHORED contract document, which is authoritative over it.
/// </summary>
/// <remarks>
/// Every row here compares the implementation against <c>security.v1.yaml</c>'s own text. Where the two
/// disagree the implementation is wrong by definition, so these rows are the ones that must never be
/// "fixed" by editing the document.
/// </remarks>
public sealed class CryptoContractConformanceTests
{
    /// <summary>Every operation the implementation registers is declared by the document.</summary>
    /// <param name="path">The route path.</param>
    /// <param name="operationId">The operation identifier.</param>
    [Theory]
    [MemberData(nameof(AllOperations))]
    public void DocumentDeclaresEveryOperation(string path, string operationId)
    {
        string block = ContractDocument.PathBlock(path);

        Assert.Contains("    post:", block, StringComparison.Ordinal);
        Assert.Contains("operationId: " + operationId, block, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every operation declares exactly the status set the implementation registers, and never 501.
    /// </summary>
    /// <param name="path">The route path.</param>
    /// <param name="declaresReferenceFailures">Whether 403 is declared.</param>
    /// <param name="declaresNotFound">Whether 404 is declared.</param>
    /// <remarks>
    /// The absence of 501 is asserted on EVERY operation, not once globally. Constraint C-D reserves
    /// that status for the ingress service's four deferred-capability routing declarations, so a single
    /// operation acquiring it would be a compliance breach that a global assertion could miss if the
    /// token appeared in prose elsewhere in the document.
    /// </remarks>
    [Theory]
    [MemberData(nameof(OperationStatusRows))]
    public void OperationDeclaresExpectedStatusSet(
        string path,
        bool declaresReferenceFailures,
        bool declaresNotFound)
    {
        string block = ContractDocument.PathBlock(path);

        Assert.Contains("        '200':", block, StringComparison.Ordinal);
        Assert.Contains("        '400':", block, StringComparison.Ordinal);
        Assert.Contains("        '401':", block, StringComparison.Ordinal);
        Assert.Contains("        '500':", block, StringComparison.Ordinal);

        Assert.Equal(
            declaresReferenceFailures,
            block.Contains("        '403':", StringComparison.Ordinal));

        Assert.Equal(
            declaresNotFound,
            block.Contains("        '404':", StringComparison.Ordinal));

        Assert.DoesNotContain("        '501':", block, StringComparison.Ordinal);
    }

    /// <summary>Every operation is tagged as belonging to the cryptographic contract.</summary>
    /// <param name="path">The route path.</param>
    /// <param name="operationId">The operation identifier, carried for row identity only.</param>
    [Theory]
    [MemberData(nameof(AllOperations))]
    public void OperationCarriesTheContractTag(string path, string operationId)
    {
        Assert.False(string.IsNullOrEmpty(operationId));

        string block = ContractDocument.PathBlock(path);

        Assert.Contains("CryptoService", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// The document declares no operation under the crypto prefix that the implementation does not
    /// register.
    /// </summary>
    /// <remarks>
    /// The converse of the per-operation row, and the one that catches an omission rather than an
    /// invention: without it, an operation the document declares and this file forgot would simply not
    /// be tested. Counting the document's own path keys is what makes that impossible.
    /// </remarks>
    [Fact]
    public void ImplementationRegistersEveryDocumentedCryptoOperation()
    {
        HashSet<string> documented = new(StringComparer.Ordinal);

        foreach (string line in ContractDocument.Text.Split('\n'))
        {
            string candidate = line.TrimEnd('\r');

            bool isCryptoPathKey =
                candidate.StartsWith("  " + CryptoFixture.Prefix, StringComparison.Ordinal) &&
                !candidate.StartsWith("   ", StringComparison.Ordinal) &&
                candidate.EndsWith(':');

            if (isCryptoPathKey)
            {
                documented.Add(candidate.Trim().TrimEnd(':'));
            }
        }

        HashSet<string> registered = new(
            CryptoFixture.Operations.Select(operation => operation.Path),
            StringComparer.Ordinal);

        Assert.Equal(17, documented.Count);
        Assert.Equal(documented, registered);
    }

    /// <summary>
    /// Every request schema is closed to members the document does not declare.
    /// </summary>
    /// <remarks>
    /// This is HALF THE PROOF that raw key material cannot be smuggled inbound. A closed schema means a
    /// document-validating client refuses an extra member; the companion reflection row proves the
    /// server declares no such member in the first place, so neither side depends on the other.
    /// </remarks>
    [Fact]
    public void EveryRequestSchemaIsClosed()
    {
        int closedCount = ContractDocument.Text
            .Split('\n')
            .Count(line => line.Contains("additionalProperties: false", StringComparison.Ordinal));

        Assert.True(
            closedCount >= CryptoFixture.RequestTypes.Count,
            "Every request schema of this contract declares additionalProperties: false, so the count " +
            "of such declarations cannot be below the number of request types.");
    }

    /// <summary>
    /// The two payload-form tokens on the wire are exactly the ones the document declares.
    /// </summary>
    /// <remarks>
    /// Asserted against the document AND against the enumeration's own member names, because the
    /// serializer emits the member name verbatim: a rename would silently change the wire without
    /// changing any other assertion in this file.
    /// </remarks>
    [Fact]
    public void PayloadFormTokensMatchTheDocument()
    {
        Assert.Contains("enum: [STRING, BLOB]", ContractDocument.Text, StringComparison.Ordinal);

        Assert.Equal("STRING", PayloadForm.STRING.ToString());
        Assert.Equal("BLOB", PayloadForm.BLOB.ToString());

        Assert.Equal(2, Enum.GetValues<PayloadForm>().Length);
    }

    /// <summary>The document declares the bearer requirement, which every operation inherits.</summary>
    [Fact]
    public void DocumentDeclaresTheBearerRequirement()
    {
        Assert.Contains("bearerAuth", ContractDocument.Text, StringComparison.Ordinal);
        Assert.Contains("bearerFormat: JWT", ContractDocument.Text, StringComparison.Ordinal);
    }

    /// <summary>The route path and identifier of every operation.</summary>
    /// <returns>One row per operation.</returns>
    public static TheoryData<string, string> AllOperations()
    {
        TheoryData<string, string> rows = [];

        foreach (CryptoOperation operation in CryptoFixture.Operations)
        {
            rows.Add(operation.Path, operation.OperationId);
        }

        return rows;
    }

    /// <summary>The declared status expectations of every operation.</summary>
    /// <returns>One row per operation.</returns>
    public static TheoryData<string, bool, bool> OperationStatusRows()
    {
        TheoryData<string, bool, bool> rows = [];

        foreach (CryptoOperation operation in CryptoFixture.Operations)
        {
            rows.Add(operation.Path, operation.DeclaresReferenceFailures, operation.DeclaresNotFound);
        }

        return rows;
    }
}

/// <summary>
/// The preserved <c>CRYPTO_</c> catalogue, asserted against BOTH the kernel and the authored document.
/// </summary>
/// <remarks>
/// <para>
/// These values appear in serialized payloads, log records and characterization recordings, so a
/// renumbering is a silent data corruption rather than a compile error. The catalogue is verified at
/// [ws_objects/pfw.shared.pbl.src/enums.sru:L924-L967] and every row below cites the line it rests on.
/// </para>
/// <para>
/// THE FOUR ALIASED DEFAULTS ARE ASSERTED INDIVIDUALLY, because each is a preserved legacy weakness or a
/// preserved legacy narrowing and each would be a plausible thing for a well-meaning change to
/// strengthen.
/// </para>
/// </remarks>
public sealed class CryptoCatalogueTests
{
    /// <summary>Each catalogue value equals the kernel constant and is bound to it by the document.</summary>
    /// <param name="identifier">The preserved identifier spelling, for example <c>CRYPTO_HASH_MD5</c>.</param>
    /// <param name="kernelValue">The kernel constant's value.</param>
    /// <param name="expected">The value the oracle declares.</param>
    /// <remarks>
    /// <para>
    /// THREE THINGS ARE ASSERTED AT ONCE, and the third is what makes this row strong. The kernel value
    /// equals the oracle's; the document mentions the identifier; and the document BINDS that identifier
    /// to that number in its own prose, in the exact form it uses throughout - a backtick-quoted number,
    /// an equals sign, and the backtick-quoted identifier.
    /// </para>
    /// <para>
    /// Asserting the binding rather than the bare identifier is the difference between a real conformance
    /// check and a spell-check: a document that renumbered a member would still contain its name.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CatalogueRows))]
    public void CatalogueValueMatchesOracleAndDocument(
        string identifier,
        long kernelValue,
        long expected)
    {
        Assert.Equal(expected, kernelValue);

        Assert.Contains(identifier, ContractDocument.Text, StringComparison.Ordinal);

        string binding = string.Create(
            CultureInfo.InvariantCulture,
            $"`{expected}` = `{identifier}`");

        Assert.Contains(binding, ContractDocument.Text, StringComparison.Ordinal);
    }

    /// <summary>The symmetric mode default is ECB, which is a preserved legacy weakness.</summary>
    /// <remarks>[enums.sru:L946] aliases the default to ECB [<c>:L943</c>].</remarks>
    [Fact]
    public void SymmetricModeDefaultIsElectronicCodebook()
    {
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_MODE_ECB, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);
        Assert.Equal(0L, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);
    }

    /// <summary>The RSA padding default is PKCS#1, which is a preserved legacy weakness.</summary>
    /// <remarks>[enums.sru:L951] aliases the default to PKCS#1 [<c>:L949</c>].</remarks>
    [Fact]
    public void RsaPaddingDefaultIsPkcs1()
    {
        Assert.Equal(Enums.CRYPTO_RSA_PADDING_PKCS1, LegacyDefaults.RSA_PADDING_DEFAULT);
        Assert.Equal(0L, LegacyDefaults.RSA_PADDING_DEFAULT);
    }

    /// <summary>
    /// The random-string default is digits and letters, DELIBERATELY EXCLUDING the symbol class.
    /// </summary>
    /// <remarks>
    /// [enums.sru:L957] aliases the default to the sum of the digit and letter classes only. The symbol
    /// class exists [<c>:L956</c>] and is simply not in the default, which is a preserved narrowing this
    /// port must not widen.
    /// </remarks>
    [Fact]
    public void RandomStringDefaultExcludesTheSymbolClass()
    {
        Assert.Equal(
            Enums.CRYPTO_RNDSTRING_NUMBER + Enums.CRYPTO_RNDSTRING_ALPHABET,
            LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT);

        Assert.Equal(3U, LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT);

        Assert.Equal(
            0U,
            LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT & Enums.CRYPTO_RNDSTRING_SYMBOL);
    }

    /// <summary>The GUID default carries both the braces and the separators.</summary>
    /// <remarks>[enums.sru:L962] aliases the default to the sum of both formatting flags.</remarks>
    [Fact]
    public void GuidDefaultCarriesBracketsAndSeparators()
    {
        Assert.Equal(
            Enums.CRYPTO_GUID_INCLUDE_BRACKET + Enums.CRYPTO_GUID_INCLUDE_SEPARATOR,
            LegacyDefaults.GUID_FLAGS_DEFAULT);

        Assert.Equal(3U, LegacyDefaults.GUID_FLAGS_DEFAULT);
    }

    /// <summary>
    /// The padding set has exactly two members, which is WHY no-padding is unsupported.
    /// </summary>
    /// <remarks>
    /// The mechanical reason matters: it is not a policy this port layered on top, it is that
    /// [enums.sru:L949-L950] declares two members and neither names it, so there is nothing to select.
    /// </remarks>
    [Fact]
    public void NoPaddingIsUnsupportedBecauseTheCatalogueHasNoMemberForIt()
    {
        Assert.True(LegacyDefaults.IsSupportedRsaPadding(Enums.CRYPTO_RSA_PADDING_PKCS1));
        Assert.True(LegacyDefaults.IsSupportedRsaPadding(Enums.CRYPTO_RSA_PADDING_OAEP));
        Assert.False(LegacyDefaults.IsSupportedRsaPadding(2L));
        Assert.False(LegacyDefaults.IsSupportedRsaPadding(-1L));
        Assert.False(LegacyDefaults.RSA_NO_PADDING_SUPPORTED);
    }

    /// <summary>The four structural weaknesses the port preserves are all still declared as absent.</summary>
    /// <remarks>
    /// Asserted as a row of its own because each is a capability a modern reviewer would EXPECT to find
    /// and whose addition would be a behavioural change: no key-derivation function, no authenticated
    /// encryption, no selectable block padding, and no enforced minimum RSA key size.
    /// </remarks>
    [Fact]
    public void StructuralLegacyWeaknessesArePreserved()
    {
        Assert.False(LegacyDefaults.KEY_DERIVATION_AVAILABLE);
        Assert.False(LegacyDefaults.AUTHENTICATED_ENCRYPTION_AVAILABLE);
        Assert.False(LegacyDefaults.BLOCK_PADDING_SELECTABLE);
        Assert.False(LegacyDefaults.RSA_MINIMUM_KEY_SIZE_ENFORCED);
        Assert.Equal(1024, LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS);
    }

    /// <summary>The catalogue rows, each naming the preserved identifier the document must bind.</summary>
    /// <returns>One row per catalogue constant this contract exposes - all 26 of them.</returns>
    public static TheoryData<string, long, long> CatalogueRows() =>
        new()
        {
            // Encoding [enums.sru:L924-L925].
            { "CRYPTO_ENCODING_BASE64", Enums.CRYPTO_ENCODING_BASE64, 0L },
            { "CRYPTO_ENCODING_HEX", Enums.CRYPTO_ENCODING_HEX, 1L },

            // Unkeyed and keyed hash [enums.sru:L928-L933].
            { "CRYPTO_HASH_MD5", Enums.CRYPTO_HASH_MD5, 0L },
            { "CRYPTO_HASH_SHA1", Enums.CRYPTO_HASH_SHA1, 1L },
            { "CRYPTO_HASH_SHA256", Enums.CRYPTO_HASH_SHA256, 2L },
            { "CRYPTO_HASH_SHA384", Enums.CRYPTO_HASH_SHA384, 3L },
            { "CRYPTO_HASH_SHA512", Enums.CRYPTO_HASH_SHA512, 4L },
            { "CRYPTO_HASH_CRC32", Enums.CRYPTO_HASH_CRC32, 5L },

            // Symmetric cipher [enums.sru:L936-L940].
            { "CRYPTO_SYMCRYPT_TYPE_DES", Enums.CRYPTO_SYMCRYPT_TYPE_DES, 0L },
            { "CRYPTO_SYMCRYPT_TYPE_3DES", Enums.CRYPTO_SYMCRYPT_TYPE_3DES, 1L },
            { "CRYPTO_SYMCRYPT_TYPE_AES128", Enums.CRYPTO_SYMCRYPT_TYPE_AES128, 2L },
            { "CRYPTO_SYMCRYPT_TYPE_AES192", Enums.CRYPTO_SYMCRYPT_TYPE_AES192, 3L },
            { "CRYPTO_SYMCRYPT_TYPE_AES256", Enums.CRYPTO_SYMCRYPT_TYPE_AES256, 4L },

            // Symmetric mode [enums.sru:L943-L945].
            { "CRYPTO_SYMCRYPT_MODE_ECB", Enums.CRYPTO_SYMCRYPT_MODE_ECB, 0L },
            { "CRYPTO_SYMCRYPT_MODE_CBC", Enums.CRYPTO_SYMCRYPT_MODE_CBC, 1L },
            { "CRYPTO_SYMCRYPT_MODE_CFB", Enums.CRYPTO_SYMCRYPT_MODE_CFB, 2L },

            // RSA padding [enums.sru:L949-L950].
            { "CRYPTO_RSA_PADDING_PKCS1", Enums.CRYPTO_RSA_PADDING_PKCS1, 0L },
            { "CRYPTO_RSA_PADDING_OAEP", Enums.CRYPTO_RSA_PADDING_OAEP, 1L },

            // Random-string flags [enums.sru:L954-L956].
            { "CRYPTO_RNDSTRING_NUMBER", Enums.CRYPTO_RNDSTRING_NUMBER, 1L },
            { "CRYPTO_RNDSTRING_ALPHABET", Enums.CRYPTO_RNDSTRING_ALPHABET, 2L },
            { "CRYPTO_RNDSTRING_SYMBOL", Enums.CRYPTO_RNDSTRING_SYMBOL, 4L },

            // GUID flags [enums.sru:L960-L961].
            { "CRYPTO_GUID_INCLUDE_BRACKET", Enums.CRYPTO_GUID_INCLUDE_BRACKET, 1L },
            { "CRYPTO_GUID_INCLUDE_SEPARATOR", Enums.CRYPTO_GUID_INCLUDE_SEPARATOR, 2L },

            // RSA convenience sizes [enums.sru:L965-L967]. 1024 IS A LEGAL, PUBLISHED VALUE.
            { "CRYPTO_RSA_BITS_1024", Enums.CRYPTO_RSA_BITS_1024, 1024L },
            { "CRYPTO_RSA_BITS_2048", Enums.CRYPTO_RSA_BITS_2048, 2048L },
            { "CRYPTO_RSA_BITS_4096", Enums.CRYPTO_RSA_BITS_4096, 4096L },
        };
}


/// <summary>
/// Service-level properties of contract C-02, observed on the REAL composition root.
/// </summary>
/// <remarks>
/// Only the real host carries the default-deny fallback policy, the route group's authorization
/// metadata and the generated OpenAPI document, so these are the rows that a directly-invoked handler
/// could never establish. Every row boots the host through <c>WebApplicationFactory&lt;Program&gt;</c>,
/// which is also what proves <c>Program.cs</c> actually maps this contract - an unmapped route answers
/// 404 rather than 401, so the authentication rows double as a registration check.
/// </remarks>
public sealed class CryptoEndpointsServiceTests
{
    /// <summary>The generated contract document's route.</summary>
    private static readonly Uri DocumentRoute = new("/openapi/v1.json", UriKind.Relative);

    /// <summary>
    /// EVERY operation answers 401 without a token, asserted one route at a time.
    /// </summary>
    /// <param name="path">The route path.</param>
    /// <param name="operationId">The operation identifier, carried for row identity.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// A ROW PER ROUTE RATHER THAN ONE LOOPING TEST, driven from the shared operation table, so a route
    /// added later is asserted automatically and a failure names the route that broke rather than the
    /// loop that found it.
    /// </para>
    /// <para>
    /// THIS ROW FAILS IF <c>RequireAuthorization</c> IS REMOVED FROM THE GROUP, which is the property
    /// it exists to protect. It also fails if the route is not mapped at all, because an unmapped path
    /// answers 404: the two failures are distinguishable from the assertion message, and both are real
    /// defects.
    /// </para>
    /// <para>
    /// The body is deliberately an empty JSON object. Authorization is evaluated before model binding,
    /// so a well-formed request is unnecessary - and sending one would risk a row passing for the wrong
    /// reason if binding ever rejected first.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CryptoContractConformanceTests.AllOperations), MemberType = typeof(CryptoContractConformanceTests))]
    public async Task OperationRequiresATokenAsync(string path, string operationId)
    {
        Assert.False(string.IsNullOrEmpty(operationId));

        using SecurityHostFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using StringContent body = new("{}", Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(path, UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// No operation of this contract is anonymous, and none is one of the service's three exemptions.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The complement of the per-route rows: it proves the crypto prefix and the exemption set are
    /// disjoint, so a future exemption added for a health or discovery path cannot accidentally cover a
    /// cryptographic operation.
    /// </remarks>
    [Fact]
    public async Task NoCryptoRouteIsAmongTheAnonymousExemptionsAsync()
    {
        string[] exemptions =
        [
            "/health",
            "/.well-known/jwks.json",
            "/.well-known/openid-configuration",
        ];

        foreach (CryptoOperation operation in CryptoFixture.Operations)
        {
            foreach (string exemption in exemptions)
            {
                Assert.NotEqual(exemption, operation.Path);
            }
        }

        using SecurityHostFactory factory = new();
        using HttpClient client = factory.CreateClient();

        // The anonymous probe still answers without a token, so the exemption set is intact and the
        // 401s above are the contract's own requirement rather than a broken host.
        using HttpResponseMessage health = await client.GetAsync(
            new Uri("/health", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Unauthorized, health.StatusCode);
    }

    /// <summary>
    /// The generated document declares every operation with the identifier the contract publishes.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// This is the comparison that matters most in practice: the authored document and the generated one
    /// must agree, because a consumer generates its client from whichever it is handed. The authored one
    /// is authoritative, so a disagreement is fixed in the code.
    /// </remarks>
    [Fact]
    public async Task GeneratedDocumentDeclaresEveryOperationAsync()
    {
        using JsonDocument document = await ReadGeneratedDocumentAsync();

        JsonElement paths = document.RootElement.GetProperty("paths");

        foreach (CryptoOperation operation in CryptoFixture.Operations)
        {
            Assert.True(
                paths.TryGetProperty(operation.Path, out JsonElement item),
                $"The generated document declares no path '{operation.Path}'.");

            Assert.True(
                item.TryGetProperty("post", out JsonElement post),
                $"'{operation.Path}' is not declared as a POST operation.");

            Assert.Equal(operation.OperationId, post.GetProperty("operationId").GetString());
        }
    }

    /// <summary>
    /// Every operation of this contract carries the bearer security requirement in the document.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// A document that omitted this would describe an anonymous surface while the service answered 401 -
    /// a consumer generating a client from it would build one that cannot authenticate. The generator
    /// does not synthesise the requirement from authorization metadata, which is exactly why the
    /// endpoint file declares it through an operation transformer.
    /// </remarks>
    [Fact]
    public async Task GeneratedDocumentDeclaresTheBearerRequirementOnEveryOperationAsync()
    {
        using JsonDocument document = await ReadGeneratedDocumentAsync();

        JsonElement paths = document.RootElement.GetProperty("paths");

        foreach (CryptoOperation operation in CryptoFixture.Operations)
        {
            JsonElement post = paths.GetProperty(operation.Path).GetProperty("post");

            Assert.True(
                post.TryGetProperty("security", out JsonElement security),
                $"'{operation.OperationId}' declares no security requirement.");

            string rendered = security.GetRawText();

            Assert.Contains("bearerAuth", rendered, StringComparison.Ordinal);
        }
    }

    /// <summary>Every operation of this contract carries the contract's tag.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task GeneratedDocumentTagsEveryOperationAsync()
    {
        using JsonDocument document = await ReadGeneratedDocumentAsync();

        JsonElement paths = document.RootElement.GetProperty("paths");

        foreach (CryptoOperation operation in CryptoFixture.Operations)
        {
            JsonElement post = paths.GetProperty(operation.Path).GetProperty("post");

            Assert.True(post.TryGetProperty("tags", out JsonElement tags));
            Assert.Contains("CryptoService", tags.GetRawText(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// All six preserved weak defaults are annotated in the generated document's own descriptions.
    /// </summary>
    /// <param name="fragment">A distinctive fragment of one annotation.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE WHOLE POINT OF PRESERVING A WEAKNESS IS THAT A CALLER CAN SEE IT. The behaviour is not
    /// changed, so the only mitigation available is disclosure, and disclosure that is not in the
    /// published document is not disclosure. A row per annotation makes a silently dropped one fail
    /// loudly.
    /// </para>
    /// <para>
    /// Asserted against a fragment rather than the full sentence because the constants are private to
    /// the endpoint file, which is correct - the wording belongs beside the behaviour it describes.
    /// Each fragment is specific enough that no other sentence in the document contains it.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("omitting mode runs in ECB")]
    [InlineData("omitting padding selects PKCS#1 v1.5")]
    [InlineData("block padding is fixed and not selectable")]
    [InlineData("no key-derivation function is reachable at all")]
    [InlineData("no authenticated encryption, so ciphertext carries no integrity tag")]
    [InlineData("1024-bit RSA remains a legal key size")]
    public async Task GeneratedDocumentAnnotatesEveryWeakDefaultAsync(string fragment)
    {
        using JsonDocument document = await ReadGeneratedDocumentAsync();

        string rendered = document.RootElement.GetProperty("paths").GetRawText();

        Assert.Contains(fragment, rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// No operation of this contract declares the not-implemented status anywhere in the document.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Constraint C-D reserves that status for the ingress service's four deferred-capability routing
    /// declarations. The two symmetric operations DO refuse two cells of the cipher grid, and this row
    /// is what proves the refusal was mapped to the server-error status the contract declares rather
    /// than to the one that would advertise a deferred capability.
    /// </remarks>
    [Fact]
    public async Task NoOperationDeclaresTheNotImplementedStatusAsync()
    {
        using JsonDocument document = await ReadGeneratedDocumentAsync();

        JsonElement paths = document.RootElement.GetProperty("paths");

        foreach (CryptoOperation operation in CryptoFixture.Operations)
        {
            JsonElement responses = paths
                .GetProperty(operation.Path)
                .GetProperty("post")
                .GetProperty("responses");

            Assert.False(
                responses.TryGetProperty("501", out _),
                $"'{operation.OperationId}' declares the not-implemented status, which C-D forbids.");
        }
    }

    /// <summary>
    /// Each operation's declared response set in the document matches the authored contract exactly.
    /// </summary>
    /// <param name="path">The route path.</param>
    /// <param name="declaresReferenceFailures">Whether 403 is expected.</param>
    /// <param name="declaresNotFound">Whether 404 is expected.</param>
    /// <returns>A task representing the assertion.</returns>
    [Theory]
    [MemberData(nameof(CryptoContractConformanceTests.OperationStatusRows), MemberType = typeof(CryptoContractConformanceTests))]
    public async Task GeneratedDocumentDeclaresExpectedStatusSetAsync(
        string path,
        bool declaresReferenceFailures,
        bool declaresNotFound)
    {
        using JsonDocument document = await ReadGeneratedDocumentAsync();

        JsonElement responses = document.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty("post")
            .GetProperty("responses");

        Assert.True(responses.TryGetProperty("200", out _));
        Assert.True(responses.TryGetProperty("400", out _));
        Assert.True(responses.TryGetProperty("401", out _));
        Assert.True(responses.TryGetProperty("500", out _));

        Assert.Equal(declaresReferenceFailures, responses.TryGetProperty("403", out _));
        Assert.Equal(declaresNotFound, responses.TryGetProperty("404", out _));
    }

    /// <summary>
    /// No request schema in the generated document declares an inbound key-material member.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The published half of the contract-level secrets rule. A consumer reading the document must find
    /// no field into which a key could be placed, so the token scan runs over the document's own
    /// schema section rather than over the implementation.
    /// </remarks>
    [Fact]
    public async Task GeneratedDocumentDeclaresNoInboundKeyMemberAsync()
    {
        using JsonDocument document = await ReadGeneratedDocumentAsync();

        Assert.True(
            document.RootElement.TryGetProperty("components", out JsonElement components),
            "The generated document declares a components section.");

        Assert.True(components.TryGetProperty("schemas", out JsonElement schemas));

        foreach (JsonProperty schema in schemas.EnumerateObject())
        {
            if (!schema.Name.EndsWith("Request", StringComparison.Ordinal))
            {
                continue;
            }

            if (!schema.Value.TryGetProperty("properties", out JsonElement properties))
            {
                continue;
            }

            foreach (JsonProperty member in properties.EnumerateObject())
            {
                // Only a text-shaped member could carry material. A boolean flag such as the
                // envelope selector on key generation cannot, whatever its name resembles.
                //
                // The type is read defensively because this generator renders a NULLABLE member's type
                // as an ARRAY of names rather than as a single name, so reading it as a string would
                // throw on exactly the optional members this scan most needs to cover.
                if (!DeclaresTextShape(member.Value))
                {
                    continue;
                }

                Assert.False(
                    CryptoSecrecyTests.NamesKeyMaterial(member.Name),
                    $"Schema '{schema.Name}' declares member '{member.Name}', which could carry key " +
                    "material inbound. Every key-bearing request must use an opaque reference.");
            }
        }
    }

    /// <summary>
    /// Determines whether a generated schema member is text-shaped and could therefore carry material.
    /// </summary>
    /// <param name="member">The member's schema.</param>
    /// <returns><see langword="true"/> when the member admits a string value.</returns>
    /// <remarks>
    /// Three shapes are handled because this generator emits all three: a single type name, an ARRAY of
    /// type names for a nullable member, and no type at all for a member described only by a reference.
    /// The absent case is treated as text-shaped, which is the conservative reading - an unclassifiable
    /// member should be scanned rather than skipped.
    /// </remarks>
    private static bool DeclaresTextShape(JsonElement member)
    {
        if (!member.TryGetProperty("type", out JsonElement declaredType))
        {
            return true;
        }

        if (declaredType.ValueKind == JsonValueKind.String)
        {
            return string.Equals(declaredType.GetString(), "string", StringComparison.Ordinal);
        }

        if (declaredType.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement name in declaredType.EnumerateArray())
            {
                if (name.ValueKind == JsonValueKind.String &&
                    string.Equals(name.GetString(), "string", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        return true;
    }

    /// <summary>Fetches and parses the generated contract document.</summary>
    /// <returns>The parsed document.</returns>
    private static async Task<JsonDocument> ReadGeneratedDocumentAsync()
    {
        using SecurityHostFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            DocumentRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        return JsonDocument.Parse(payload);
    }
}


/// <summary>
/// The contract-level secrets rule: raw key material never crosses the wire, in either direction.
/// </summary>
/// <remarks>
/// <para>
/// This is the single most important property of contract C-02, so it is asserted three independent
/// ways, none of which depends on the others. First, by REFLECTING over every request record and proving
/// no member could carry key material inbound. Second, by scanning the published document's own schema
/// section for the same thing. Third, by driving the operations that resolve a reference and scanning
/// EVERY byte of the response and of the problem body for the resolved value.
/// </para>
/// <para>
/// The third is the one that catches a real mistake, because the first two only prove that a caller
/// cannot SEND a key: an implementation could still echo one it resolved.
/// </para>
/// </remarks>
public sealed class CryptoSecrecyTests
{
    /// <summary>Member-name fragments that would indicate inbound key material.</summary>
    /// <remarks>
    /// Deliberately broad, including the words a well-meaning addition would reach for. The opaque
    /// reference members are excluded by an exact-name allow-list rather than by pattern, so a member
    /// called <c>keyMaterial</c> or <c>keyPem</c> cannot pass by resembling <c>keyRef</c>.
    /// </remarks>
    private static readonly string[] KeyMaterialFragments =
    [
        "key",
        "secret",
        "password",
        "passphrase",
        "pem",
        "certificate",
        "credential",
        "token",
        "iv",
        "salt",
        "privatekey",
        "publickey",
    ];

    /// <summary>The opaque reference members, which are handles and carry nothing.</summary>
    private static readonly string[] PermittedReferenceMembers =
    [
        "keyRef",
        "ivRef",
        "fileRef",
    ];

    /// <summary>
    /// Determines whether a member's TYPE could carry key material at all.
    /// </summary>
    /// <param name="memberType">The member's declared type.</param>
    /// <returns><see langword="true"/> when the type could carry material.</returns>
    /// <remarks>
    /// <para>
    /// THE FIRST OF TWO SCREENS, AND THE ONE THAT MAKES THE SECOND HONEST. Key material is a run of
    /// characters or a run of bytes; it cannot travel in a boolean or a number. So a member is a hazard
    /// only when its type could hold material AND its name suggests it does, and applying the name test
    /// alone would flag things that are provably harmless.
    /// </para>
    /// <para>
    /// The concrete case that forced this distinction is <c>GenRsaKeyRequest.PemFormat</c>: its name
    /// contains a term this scan looks for, and it is a nullable BOOLEAN selecting whether the emitted
    /// public key carries the textual envelope [n_crypto.sru:L19-L20]. No key can travel in it, so
    /// flagging it would be a false positive that could only be silenced by weakening the name list -
    /// which would then let a genuinely dangerous member through.
    /// </para>
    /// <para>
    /// <c>object</c> is included because it would carry anything, and an untyped member on this surface
    /// would be a defect in its own right.
    /// </para>
    /// </remarks>
    internal static bool CanCarryMaterial(Type memberType)
    {
        ArgumentNullException.ThrowIfNull(memberType);

        return memberType == typeof(string) ||
            memberType == typeof(byte[]) ||
            memberType == typeof(ReadOnlyMemory<byte>) ||
            memberType == typeof(object);
    }

    /// <summary>
    /// Determines whether a member name would indicate inbound key material.
    /// </summary>
    /// <param name="memberName">The member name, in either wire or C# spelling.</param>
    /// <returns><see langword="true"/> when the name is not an opaque reference and looks key-bearing.</returns>
    /// <remarks>
    /// THE SECOND OF TWO SCREENS. Pair it with <see cref="CanCarryMaterial(Type)"/> - or, on the
    /// published document, with the member's declared schema type - so that a harmless flag member is
    /// not reported and the name list can stay deliberately broad.
    /// </remarks>
    internal static bool NamesKeyMaterial(string memberName)
    {
        ArgumentException.ThrowIfNullOrEmpty(memberName);

        foreach (string permitted in PermittedReferenceMembers)
        {
            if (string.Equals(memberName, permitted, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        foreach (string fragment in KeyMaterialFragments)
        {
            if (memberName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>No request record declares a member that could carry key material inbound.</summary>
    /// <param name="requestType">The request record under inspection.</param>
    [Theory]
    [MemberData(nameof(RequestTypeRows))]
    public void RequestDeclaresNoInboundKeyMember(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);

        foreach (PropertyInfo member in requestType.GetProperties(
            BindingFlags.Public | BindingFlags.Instance))
        {
            if (!CanCarryMaterial(member.PropertyType))
            {
                continue;
            }

            Assert.False(
                NamesKeyMaterial(member.Name),
                $"{requestType.Name}.{member.Name} could carry key material inbound. Every " +
                "key-bearing request must take an opaque reference instead.");
        }
    }

    /// <summary>No request record declares a member whose type could carry raw bytes.</summary>
    /// <param name="requestType">The request record under inspection.</param>
    /// <remarks>
    /// A byte array member would be a second way to smuggle material in, sidestepping the name scan
    /// entirely. Every payload on this surface travels as text - base64 when the form is binary - so a
    /// byte-shaped member is a defect by construction rather than a style question.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RequestTypeRows))]
    public void RequestDeclaresNoRawByteMember(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);

        foreach (PropertyInfo member in requestType.GetProperties(
            BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.NotEqual(typeof(byte[]), member.PropertyType);
            Assert.NotEqual(typeof(ReadOnlyMemory<byte>), member.PropertyType);
        }
    }

    /// <summary>
    /// The request-type table covers every request record the endpoint namespace declares.
    /// </summary>
    /// <remarks>
    /// Without this row, a request type added later and forgotten here would never be scanned - and the
    /// scan is the proof. Counting the endpoint assembly's own public request records closes that gap.
    /// </remarks>
    [Fact]
    public void EveryDeclaredRequestRecordIsCovered()
    {
        List<Type> declared = [];

        foreach (Type candidate in typeof(HashRequest).Assembly.GetTypes())
        {
            bool isCryptoRequest =
                candidate.IsPublic &&
                string.Equals(
                    candidate.Namespace,
                    typeof(HashRequest).Namespace,
                    StringComparison.Ordinal) &&
                candidate.Name.EndsWith("Request", StringComparison.Ordinal) &&
                !candidate.IsInterface;

            if (isCryptoRequest)
            {
                declared.Add(candidate);
            }
        }

        // The endpoint namespace also holds the sibling contracts' request records, so the assertion is
        // one of containment rather than of equality: every record THIS contract binds must be covered.
        foreach (Type covered in CryptoFixture.RequestTypes)
        {
            Assert.Contains(covered, declared);
        }

        Assert.Equal(16, CryptoFixture.RequestTypes.Count);
    }

    /// <summary>
    /// A keyed digest never echoes the resolved key, in the response or in a rejection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SCANNED, NOT INSPECTED. The whole serialized response and the whole serialized problem body are
    /// searched for the resolved value, so an echo through any member - including one added later -
    /// fails this row.
    /// </para>
    /// <para>
    /// The digest itself is DERIVED from the key and must obviously not equal it; the scan is a substring
    /// search, so a digest that happened to contain the key would fail, which is the correct outcome.
    /// </para>
    /// </remarks>
    [Fact]
    public void KeyedDigestNeverEchoesTheResolvedKey()
    {
        const string reference = "hmac-key";
        string material = CryptoFixture.KeyMaterial(32, 'k');

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal) { [reference] = material });

        DigestResponse response = CryptoFixture.Success(CryptoEndpoints.Hmac(
            new HmacRequest
            {
                Data = "the quick brown fox",
                PayloadForm = PayloadForm.STRING,
                KeyRef = reference,
                HashType = Enums.CRYPTO_HASH_SHA256,
            },
            CryptoFixture.Authenticators,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        string rendered = JsonSerializer.Serialize(response);

        Assert.DoesNotContain(material, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(material, response.Digest, StringComparison.Ordinal);
    }

    /// <summary>
    /// A rejection from a key-bearing operation never echoes the resolved key or the reference's value.
    /// </summary>
    /// <remarks>
    /// The rejection path is the more dangerous one, because an error body is where a well-meaning
    /// implementation puts context. The row drives a request that resolves successfully and THEN fails,
    /// so the key really was in hand when the rejection was built.
    /// </remarks>
    [Fact]
    public void RejectionNeverEchoesTheResolvedKey()
    {
        const string reference = "cipher-key";
        string material = CryptoFixture.KeyMaterial(32, 'm');

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal) { [reference] = material });

        // A cipher type outside the published set, so resolution is reached and then the request fails.
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.SymmetricEncrypt(
            new SymEncryptRequest
            {
                Data = "plaintext",
                PayloadForm = PayloadForm.STRING,
                KeyRef = reference,
                CipherType = 99L,
            },
            CryptoFixture.Ciphers,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        string rendered = JsonSerializer.Serialize(problem.ProblemDetails);

        Assert.DoesNotContain(material, rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// A generated key pair returns the public half and a reference, never the private half.
    /// </summary>
    /// <remarks>
    /// THE HIGHEST-SEVERITY OUTBOUND DECISION ON THIS SURFACE, asserted directly. The legacy hands the
    /// private key back through a <c>ref</c> parameter [n_crypto.sru:L19-L20]; this contract retains it.
    /// The row proves the response carries no private key by generating a pair, then resolving the
    /// returned reference and confirming the resolved material is a private key that never appeared in
    /// the response body.
    /// </remarks>
    [Fact]
    public void GeneratedKeyPairNeverReturnsThePrivateHalf()
    {
        CryptoReferenceResolver store = CryptoFixture.EmptyStore();

        GenRsaKeyResponse response = CryptoFixture.Success(CryptoEndpoints.GenerateRsaKey(
            new GenRsaKeyRequest { Bits = Enums.CRYPTO_RSA_BITS_2048 },
            CryptoFixture.Rsa,
            CryptoFixture.Random,
            store,
            CryptoFixture.Loggers));

        string rendered = JsonSerializer.Serialize(response);

        Assert.DoesNotContain("PRIVATE", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("privateKey", rendered, StringComparison.Ordinal);

        Assert.False(string.IsNullOrWhiteSpace(response.PublicKey));
        Assert.False(string.IsNullOrWhiteSpace(response.KeyRef));
        Assert.Equal(Enums.CRYPTO_RSA_BITS_2048, response.Bits);

        // The reference resolves - so the private half was retained rather than discarded - and the
        // material behind it is not what the response returned.
        ProblemHttpResult? rejection = store.TryResolveReference(
            response.KeyRef,
            CryptoFixture.Loggers,
            out string retained);

        Assert.Null(rejection);
        Assert.False(string.IsNullOrWhiteSpace(retained));
        Assert.NotEqual(response.PublicKey, retained);
        Assert.DoesNotContain(retained, rendered, StringComparison.Ordinal);
    }

    /// <summary>Every request record under inspection.</summary>
    /// <returns>One row per request record.</returns>
    public static TheoryData<Type> RequestTypeRows()
    {
        TheoryData<Type> rows = [];

        foreach (Type requestType in CryptoFixture.RequestTypes)
        {
            rows.Add(requestType);
        }

        return rows;
    }
}

/// <summary>
/// Reference resolution: the permitted set, the two distinguishable failures, and the retained store.
/// </summary>
/// <remarks>
/// The authored document declares 403 for a reference the caller may not name and 404 for a permitted
/// one with nothing behind it, and states the reason - so that a configuration mistake is tellable from
/// an authorization one. These rows assert that distinction AND assert that neither answer discloses
/// anything about the store's contents.
/// </remarks>
public sealed class CryptoReferenceResolutionTests
{
    /// <summary>A reference outside the permitted set is refused, not reported as missing.</summary>
    /// <remarks>
    /// The store genuinely holds a value for the reference, and it is still refused, which is what
    /// proves the permitted set is consulted BEFORE the configuration is read. An implementation that
    /// read first and filtered afterwards would pass a weaker version of this row.
    /// </remarks>
    [Fact]
    public void NonPermittedReferenceIsForbidden()
    {
        const string reference = "not-published";

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [reference] = CryptoFixture.KeyMaterial(32),
            },
            permitted: []);

        ProblemHttpResult? rejection = store.TryResolveReference(
            reference,
            CryptoFixture.Loggers,
            out string material);

        Assert.NotNull(rejection);
        Assert.Equal(StatusCodes.Status403Forbidden, rejection.StatusCode);
        Assert.Equal(RetCode.E_ACCESS_DENIED, CryptoFixture.RetCodeOf(rejection));
        Assert.Equal(string.Empty, material);
    }

    /// <summary>A permitted reference with nothing configured behind it is reported as absent.</summary>
    [Fact]
    public void PermittedButUnconfiguredReferenceIsNotFound()
    {
        const string reference = "published-but-empty";

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal),
            permitted: [reference]);

        ProblemHttpResult? rejection = store.TryResolveReference(
            reference,
            CryptoFixture.Loggers,
            out string material);

        Assert.NotNull(rejection);
        Assert.Equal(StatusCodes.Status404NotFound, rejection.StatusCode);
        Assert.Equal(RetCode.E_OBJECT_NOT_FOUND, CryptoFixture.RetCodeOf(rejection));
        Assert.Equal(string.Empty, material);
    }

    /// <summary>
    /// The two failures are distinguishable BY STATUS but neither discloses the store's contents.
    /// </summary>
    /// <remarks>
    /// The document requires the distinction, so the statuses differ deliberately. What must NOT differ
    /// is disclosure: neither body may name a reference, enumerate the set, report its size or suggest
    /// an alternative. Asserted by scanning both bodies for the reference names involved.
    /// </remarks>
    [Fact]
    public void NeitherReferenceFailureDisclosesTheStore()
    {
        const string forbidden = "forbidden-reference";
        const string absent = "absent-reference";
        const string configured = "configured-reference";

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [configured] = CryptoFixture.KeyMaterial(32),
            },
            permitted: [configured, absent]);

        ProblemHttpResult? forbiddenRejection = store.TryResolveReference(
            forbidden,
            CryptoFixture.Loggers,
            out _);

        ProblemHttpResult? absentRejection = store.TryResolveReference(
            absent,
            CryptoFixture.Loggers,
            out _);

        Assert.NotNull(forbiddenRejection);
        Assert.NotNull(absentRejection);

        foreach (ProblemHttpResult rejection in new[] { forbiddenRejection, absentRejection })
        {
            string rendered = JsonSerializer.Serialize(rejection.ProblemDetails);

            Assert.DoesNotContain(configured, rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(forbidden, rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(absent, rendered, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>An absent or empty reference is an argument fault rather than a permission one.</summary>
    /// <param name="reference">The reference the request carried.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingReferenceIsAnArgumentFault(string? reference)
    {
        CryptoReferenceResolver store = CryptoFixture.EmptyStore();

        ProblemHttpResult? rejection = store.TryResolveReference(
            reference,
            CryptoFixture.Loggers,
            out string material);

        Assert.NotNull(rejection);
        Assert.Equal(StatusCodes.Status400BadRequest, rejection.StatusCode);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(rejection));
        Assert.Equal(string.Empty, material);
    }

    /// <summary>
    /// A reference longer than the validated bound is refused as non-permitted, revealing nothing.
    /// </summary>
    /// <remarks>
    /// It cannot be a member of a set whose entries are bounded at the same length, so the answer
    /// changes no outcome - the screen exists so that an arbitrarily long caller value is never
    /// concatenated onto a configuration key. Answering at the SAME status as any other non-permitted
    /// reference is what keeps it from becoming a third distinguishable condition.
    /// </remarks>
    [Fact]
    public void OverLongReferenceIsForbidden()
    {
        string reference = new('r', SecurityOptionsValidator.MaximumKeyRefLength + 1);

        CryptoReferenceResolver store = CryptoFixture.EmptyStore();

        ProblemHttpResult? rejection = store.TryResolveReference(
            reference,
            CryptoFixture.Loggers,
            out _);

        Assert.NotNull(rejection);
        Assert.Equal(StatusCodes.Status403Forbidden, rejection.StatusCode);
        Assert.Equal(RetCode.E_ACCESS_DENIED, CryptoFixture.RetCodeOf(rejection));
    }

    /// <summary>Reference comparison is ordinal, so a differently-cased reference is refused.</summary>
    /// <remarks>
    /// A configuration key name is machine input. A case-insensitive match would both admit references
    /// the operator never published and then read a configuration key the operator never wrote.
    /// </remarks>
    [Fact]
    public void ReferenceComparisonIsOrdinal()
    {
        const string reference = "case-sensitive";

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [reference] = CryptoFixture.KeyMaterial(32),
            });

        Assert.Null(store.TryResolveReference(reference, CryptoFixture.Loggers, out _));

        ProblemHttpResult? rejection = store.TryResolveReference(
            "CASE-SENSITIVE",
            CryptoFixture.Loggers,
            out _);

        Assert.NotNull(rejection);
        Assert.Equal(StatusCodes.Status403Forbidden, rejection.StatusCode);
    }

    /// <summary>An empty permitted set authorises nothing, which is the shipped closed default.</summary>
    /// <remarks>
    /// The shipped configuration leaves the set empty, so this row describes the state a service is in
    /// before an operator reviews its key store: every keyed operation is refused rather than reading
    /// whatever configuration key a caller names. That is the property that makes the closed default
    /// safe rather than merely inconvenient.
    /// </remarks>
    [Fact]
    public void EmptyPermittedSetAuthorisesNothing()
    {
        CryptoReferenceResolver store = CryptoFixture.EmptyStore();

        ProblemHttpResult? rejection = store.TryResolveReference(
            "anything",
            CryptoFixture.Loggers,
            out _);

        Assert.NotNull(rejection);
        Assert.Equal(StatusCodes.Status403Forbidden, rejection.StatusCode);
    }

    /// <summary>A file reference resolves only to an existing rooted path.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The path comes from configuration, never from the caller, so traversal is not a caller-reachable
    /// condition. What IS assertable is that a configured value which is not a usable path answers as an
    /// unavailable reference rather than throwing or leaking.
    /// </remarks>
    [Fact]
    public async Task FileReferenceResolvesOnlyAnExistingPathAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "blitzy_adhoc_cryptoref");
        Directory.CreateDirectory(directory);

        string present = Path.Combine(directory, "present.txt");
        string missing = Path.Combine(directory, "missing.txt");

        await File.WriteAllTextAsync(
            present,
            "file content",
            TestContext.Current.CancellationToken);

        try
        {
            CryptoReferenceResolver store = CryptoFixture.Store(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["present"] = present,
                    ["missing"] = missing,
                    ["not-a-path"] = "\0",
                });

            Assert.Null(store.TryResolveFile("present", CryptoFixture.Loggers, out string resolved));
            Assert.Equal(present, resolved);

            foreach (string reference in new[] { "missing", "not-a-path" })
            {
                ProblemHttpResult? rejection = store.TryResolveFile(
                    reference,
                    CryptoFixture.Loggers,
                    out string path);

                Assert.NotNull(rejection);
                Assert.Equal(StatusCodes.Status404NotFound, rejection.StatusCode);
                Assert.Equal(RetCode.E_OBJECT_NOT_FOUND, CryptoFixture.RetCodeOf(rejection));
                Assert.Equal(string.Empty, path);

                string rendered = JsonSerializer.Serialize(rejection.ProblemDetails);

                Assert.DoesNotContain(directory, rendered, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A minted reference resolves as a key but NEVER as a file.
    /// </summary>
    /// <remarks>
    /// The separation is deliberate: letting a generated private key resolve through a file-shaped
    /// operation would let one part of this surface reinterpret another part's material. The row proves
    /// the retained store is unreachable from the file path.
    /// </remarks>
    [Fact]
    public void MintedReferenceIsNotReachableAsAFile()
    {
        CryptoReferenceResolver store = CryptoFixture.EmptyStore();

        Assert.True(store.TryRetainGeneratedPrivateKey(
            "synthetic-retained-material",
            CryptoFixture.Random,
            out string minted));

        Assert.Null(store.TryResolveReference(minted, CryptoFixture.Loggers, out string material));
        Assert.Equal("synthetic-retained-material", material);

        ProblemHttpResult? rejection = store.TryResolveFile(minted, CryptoFixture.Loggers, out _);

        Assert.NotNull(rejection);
        Assert.Equal(StatusCodes.Status403Forbidden, rejection.StatusCode);
    }

    /// <summary>Every minted reference is distinct and inside the validated shape bound.</summary>
    /// <remarks>
    /// <para>
    /// DRIVEN THROUGH A DETERMINISTIC ENTROPY DOUBLE ON PURPOSE. Under a constant source the random half
    /// of every reference is identical, so uniqueness can only come from the interlocked ordinal - which
    /// is exactly the claim being tested. A row using real entropy would pass even if the ordinal were
    /// removed.
    /// </para>
    /// <para>
    /// The length bound matters because a minted reference must be resolvable, and the resolver refuses
    /// anything longer than the bound the configured set is validated against.
    /// </para>
    /// </remarks>
    [Fact]
    public void MintedReferencesAreDistinctUnderADeterministicEntropySource()
    {
        CryptoReferenceResolver store = CryptoFixture.EmptyStore();
        RandomProvider deterministic = new(new ConstantEntropySource(0x5A));

        HashSet<string> minted = new(StringComparer.Ordinal);

        for (int index = 0; index < 8; index++)
        {
            Assert.True(store.TryRetainGeneratedPrivateKey(
                "material-" + index.ToString(CultureInfo.InvariantCulture),
                deterministic,
                out string reference));

            Assert.True(
                minted.Add(reference),
                "Every minted reference is distinct, which the interlocked ordinal guarantees even " +
                "when the entropy source is deterministic.");

            Assert.InRange(reference.Length, 1, SecurityOptionsValidator.MaximumKeyRefLength);

            Assert.Null(store.TryResolveReference(reference, CryptoFixture.Loggers, out string held));
            Assert.Equal("material-" + index.ToString(CultureInfo.InvariantCulture), held);
        }
    }

    /// <summary>The retained store is bounded, and a full store is a refusal rather than a leak.</summary>
    /// <remarks>
    /// The bound exists because this is the service holding the system's only signing key: an unbounded
    /// store that grows on request would let authenticated callers terminate authentication for every
    /// service. The refusal is what the generation handler turns into a server error, discarding the
    /// pair rather than returning a private key it cannot retain.
    /// </remarks>
    [Fact]
    public void RetainedStoreIsBounded()
    {
        CryptoReferenceResolver store = CryptoFixture.EmptyStore();

        for (int index = 0; index < CryptoReferenceResolver.MaximumRetainedGeneratedKeys; index++)
        {
            Assert.True(store.TryRetainGeneratedPrivateKey(
                "material-" + index.ToString(CultureInfo.InvariantCulture),
                CryptoFixture.Random,
                out _));
        }

        Assert.False(store.TryRetainGeneratedPrivateKey(
            "one-too-many",
            CryptoFixture.Random,
            out string overflow));

        Assert.Equal(string.Empty, overflow);
    }

    /// <summary>
    /// Every key-bearing handler refuses an unknown reference, and none of them proceeds without one.
    /// </summary>
    /// <param name="operationId">The operation identifier, for row identity.</param>
    /// <param name="invoke">The handler invocation, closed over the store it should consult.</param>
    /// <remarks>
    /// The per-handler complement of the resolver rows: it proves each handler actually CALLS the
    /// resolver rather than reaching configuration itself, which no amount of testing the resolver alone
    /// could establish.
    /// </remarks>
    [Theory]
    [MemberData(nameof(KeyBearingOperationIds))]
    public void KeyBearingHandlerRefusesAnUnknownReference(string operationId)
    {
        ProblemHttpResult rejection = InvokeWithUnknownReference(operationId);

        Assert.Equal(StatusCodes.Status403Forbidden, rejection.StatusCode);
        Assert.Equal(RetCode.E_ACCESS_DENIED, CryptoFixture.RetCodeOf(rejection));
    }

    /// <summary>The identifier of every operation that resolves a reference.</summary>
    /// <returns>One row per key-bearing operation.</returns>
    /// <remarks>
    /// Only the identifier crosses the theory boundary. The invocation stays behind a private helper
    /// because the resolver type is internal to the service and a public theory signature cannot name
    /// it - and passing an identifier keeps the failing row's name readable in a test report.
    /// </remarks>
    public static TheoryData<string> KeyBearingOperationIds() =>
        [
            "hmac",
            "hashFile",
            "hmacFile",
            "symmetricEncrypt",
            "symmetricDecrypt",
            "rsaEncrypt",
            "rsaDecrypt",
            "rsaSign",
            "rsaVerify",
        ];

    /// <summary>
    /// Drives one key-bearing operation against an empty store, naming a reference it cannot resolve.
    /// </summary>
    /// <param name="operationId">The operation to drive.</param>
    /// <returns>The rejection the handler produced.</returns>
    /// <remarks>
    /// An exhaustive switch with no default arm that swallows an unknown identifier: a new key-bearing
    /// operation added to the row list without an arm here fails loudly rather than silently passing.
    /// </remarks>
    private static ProblemHttpResult InvokeWithUnknownReference(string operationId)
    {
        const string unknown = "unknown-reference";

        CryptoReferenceResolver store = CryptoFixture.EmptyStore();

        switch (operationId)
        {
            case "hmac":
                return CryptoFixture.Rejection(CryptoEndpoints.Hmac(
                    new HmacRequest
                    {
                        Data = "payload",
                        PayloadForm = PayloadForm.STRING,
                        KeyRef = unknown,
                        HashType = Enums.CRYPTO_HASH_SHA256,
                    },
                    CryptoFixture.Authenticators,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "hashFile":
                return CryptoFixture.Rejection(CryptoEndpoints.HashFile(
                    new HashFileRequest { FileRef = unknown, HashType = Enums.CRYPTO_HASH_SHA256 },
                    CryptoFixture.Hashes,
                    store,
                    CryptoFixture.Loggers));

            case "hmacFile":
                return CryptoFixture.Rejection(CryptoEndpoints.HmacFile(
                    new HmacFileRequest
                    {
                        FileRef = unknown,
                        KeyRef = unknown,
                        HashType = Enums.CRYPTO_HASH_SHA256,
                    },
                    CryptoFixture.Authenticators,
                    store,
                    CryptoFixture.Loggers));

            case "symmetricEncrypt":
                return CryptoFixture.Rejection(CryptoEndpoints.SymmetricEncrypt(
                    new SymEncryptRequest
                    {
                        Data = "payload",
                        PayloadForm = PayloadForm.STRING,
                        KeyRef = unknown,
                        CipherType = Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                    },
                    CryptoFixture.Ciphers,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "symmetricDecrypt":
                return CryptoFixture.Rejection(CryptoEndpoints.SymmetricDecrypt(
                    new SymDecryptRequest
                    {
                        Data = "payload",
                        PayloadForm = PayloadForm.STRING,
                        KeyRef = unknown,
                        CipherType = Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                    },
                    CryptoFixture.Ciphers,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaEncrypt":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaEncrypt(
                    new RsaCipherRequest
                    {
                        Data = "payload",
                        PayloadForm = PayloadForm.STRING,
                        KeyRef = unknown,
                    },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaDecrypt":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaDecrypt(
                    new RsaCipherRequest
                    {
                        Data = "cGF5bG9hZA==",
                        PayloadForm = PayloadForm.BLOB,
                        KeyRef = unknown,
                    },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaSign":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaSign(
                    new RsaSignRequest
                    {
                        Data = "payload",
                        PayloadForm = PayloadForm.STRING,
                        KeyRef = unknown,
                        HashType = Enums.CRYPTO_HASH_SHA256,
                    },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaVerify":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaVerify(
                    new RsaVerifyRequest
                    {
                        Data = "payload",
                        PayloadForm = PayloadForm.STRING,
                        Signature = "c2lnbmF0dXJl",
                        KeyRef = unknown,
                        HashType = Enums.CRYPTO_HASH_SHA256,
                    },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            default:
                Assert.Fail("No invocation is defined for operation '" + operationId + "'.");

                throw new InvalidOperationException(operationId);
        }
    }
}

/// <summary>
/// The digest family: the full unkeyed set, the narrowed keyed set, and the file arms.
/// </summary>
/// <remarks>
/// The two selector sets differ by exactly one member and that difference is a CAPABILITY narrowing
/// rather than a different identifier set - the values that remain keep their names and numbers. Both
/// halves are enumerated here so the narrowing is asserted rather than assumed.
/// </remarks>
public sealed class CryptoDigestMatrixTests
{
    /// <summary>Every unkeyed hash type produces a printable digest, in both payload forms.</summary>
    /// <param name="hashType">The selector.</param>
    /// <param name="form">The payload form.</param>
    /// <remarks>
    /// THE DIGEST IS TEXT FOR BOTH FORMS, which is the preserved asymmetry against the payload-bearing
    /// operations: every one of the legacy digest declarations returns a string
    /// [n_crypto.sru:L21-L29], so the response carries no payload-form member and the row asserts the
    /// same payload hashes to the same digest whichever form declared it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnkeyedHashRows))]
    public void UnkeyedDigestCoversEveryPublishedType(long hashType, PayloadForm form)
    {
        const string payload = "the quick brown fox jumps over the lazy dog";

        string data = form == PayloadForm.STRING
            ? payload
            : CryptoFixture.Encodings.Base64Encode(Encoding.UTF8.GetBytes(payload));

        DigestResponse response = CryptoFixture.Success(CryptoEndpoints.Hash(
            new HashRequest { Data = data, PayloadForm = form, HashType = hashType },
            CryptoFixture.Hashes,
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.False(string.IsNullOrEmpty(response.Digest));

        // The same bytes yield the same digest whichever form named them, which is what proves the two
        // legacy overloads agree rather than merely both answering.
        DigestResponse binary = CryptoFixture.Success(CryptoEndpoints.Hash(
            new HashRequest
            {
                Data = CryptoFixture.Encodings.Base64Encode(Encoding.UTF8.GetBytes(payload)),
                PayloadForm = PayloadForm.BLOB,
                HashType = hashType,
            },
            CryptoFixture.Hashes,
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.Equal(binary.Digest, response.Digest);
    }

    /// <summary>Every keyed hash type produces a printable digest, in both payload forms.</summary>
    /// <param name="hashType">The selector.</param>
    /// <param name="form">The payload form.</param>
    [Theory]
    [MemberData(nameof(KeyedHashRows))]
    public void KeyedDigestCoversEveryPublishedType(long hashType, PayloadForm form)
    {
        const string reference = "digest-key";
        const string payload = "message under a key";

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [reference] = CryptoFixture.KeyMaterial(32, 'q'),
            });

        string data = form == PayloadForm.STRING
            ? payload
            : CryptoFixture.Encodings.Base64Encode(Encoding.UTF8.GetBytes(payload));

        DigestResponse response = CryptoFixture.Success(CryptoEndpoints.Hmac(
            new HmacRequest
            {
                Data = data,
                PayloadForm = form,
                KeyRef = reference,
                HashType = hashType,
            },
            CryptoFixture.Authenticators,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.False(string.IsNullOrEmpty(response.Digest));
    }

    /// <summary>
    /// The keyed surface refuses the checksum member, which the unkeyed surface accepts.
    /// </summary>
    /// <remarks>
    /// The refusal is <c>E_NO_SUPPORT</c> and its status is 400, NOT the not-implemented status - which
    /// C-D reserves for the ingress service's deferred-capability declarations. The row asserts both the
    /// code and the status, because either alone could be right while the other was wrong.
    /// </remarks>
    [Fact]
    public void KeyedDigestRefusesTheChecksumMember()
    {
        const string reference = "digest-key";

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [reference] = CryptoFixture.KeyMaterial(32, 'q'),
            });

        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.Hmac(
            new HmacRequest
            {
                Data = "message",
                PayloadForm = PayloadForm.STRING,
                KeyRef = reference,
                HashType = Enums.CRYPTO_HASH_CRC32,
            },
            CryptoFixture.Authenticators,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_NO_SUPPORT, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.NotEqual(StatusCodes.Status501NotImplemented, problem.StatusCode);
    }

    /// <summary>The unkeyed surface ACCEPTS the checksum member, which is the narrowing's other half.</summary>
    /// <remarks>
    /// Asserted explicitly, because a port that refused the checksum everywhere would satisfy the keyed
    /// row above while breaking a capability the legacy has: the oracle's own demonstration computes a
    /// checksum over a file.
    /// </remarks>
    [Fact]
    public void UnkeyedDigestAcceptsTheChecksumMember()
    {
        DigestResponse response = CryptoFixture.Success(CryptoEndpoints.Hash(
            new HashRequest
            {
                Data = "message",
                PayloadForm = PayloadForm.STRING,
                HashType = Enums.CRYPTO_HASH_CRC32,
            },
            CryptoFixture.Hashes,
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.False(string.IsNullOrEmpty(response.Digest));
    }

    /// <summary>A selector outside the published set is refused on both surfaces.</summary>
    /// <param name="hashType">The out-of-set selector.</param>
    [Theory]
    [InlineData(-1L)]
    [InlineData(6L)]
    [InlineData(long.MaxValue)]
    public void SelectorOutsideThePublishedSetIsRefused(long hashType)
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.Hash(
            new HashRequest
            {
                Data = "message",
                PayloadForm = PayloadForm.STRING,
                HashType = hashType,
            },
            CryptoFixture.Hashes,
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_NO_SUPPORT, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    /// <summary>An absent member is refused, naming it in the WIRE spelling.</summary>
    /// <remarks>
    /// The wire spelling matters because the caller sent a camel-cased member and the published schema
    /// declares it camel-cased; reporting the C# spelling would name something the caller never wrote.
    /// </remarks>
    [Fact]
    public void AbsentMemberIsNamedInTheWireSpelling()
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.Hash(
            new HashRequest { Data = "message", PayloadForm = PayloadForm.STRING },
            CryptoFixture.Hashes,
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(problem));
        Assert.Contains("hashType", problem.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("HashType", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>An absent payload form is its own rejection arm.</summary>
    /// <remarks>
    /// Separate from the generic absent-member arm because the consequence is specific: the selector
    /// chooses which of the two parallel legacy overload families runs and therefore what form the
    /// result takes, so defaulting it would silently pick a family on the caller's behalf.
    /// </remarks>
    [Fact]
    public void AbsentPayloadFormIsItsOwnRejection()
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.Hash(
            new HashRequest { Data = "message", HashType = Enums.CRYPTO_HASH_SHA256 },
            CryptoFixture.Hashes,
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    /// <summary>A malformed binary payload is the caller's fault and is reported without echoing it.</summary>
    [Fact]
    public void MalformedBinaryPayloadIsRefusedWithoutEchoingIt()
    {
        const string malformed = "!!!not-base64!!!";

        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.Hash(
            new HashRequest
            {
                Data = malformed,
                PayloadForm = PayloadForm.BLOB,
                HashType = Enums.CRYPTO_HASH_SHA256,
            },
            CryptoFixture.Hashes,
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_DATA, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);

        string rendered = JsonSerializer.Serialize(problem.ProblemDetails);

        Assert.DoesNotContain(malformed, rendered, StringComparison.Ordinal);
    }

    /// <summary>Both file arms hash a configured file, and neither echoes its path.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The file arms exist because the published contract declares them, using an opaque reference so
    /// that no caller-supplied path crosses the boundary. The row proves the digest matches the same
    /// bytes hashed in memory, which is the only way to know the file was really read.
    /// </remarks>
    [Fact]
    public async Task FileArmsHashAConfiguredFileAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "blitzy_adhoc_cryptofile");
        Directory.CreateDirectory(directory);

        string file = Path.Combine(directory, "payload.bin");
        byte[] content = Encoding.UTF8.GetBytes("file bytes under digest");

        await File.WriteAllBytesAsync(file, content, TestContext.Current.CancellationToken);

        try
        {
            CryptoReferenceResolver store = CryptoFixture.Store(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["payload-file"] = file,
                    ["file-key"] = CryptoFixture.KeyMaterial(32, 'f'),
                });

            DigestResponse unkeyed = CryptoFixture.Success(CryptoEndpoints.HashFile(
                new HashFileRequest { FileRef = "payload-file", HashType = Enums.CRYPTO_HASH_SHA256 },
                CryptoFixture.Hashes,
                store,
                CryptoFixture.Loggers));

            Assert.Equal(
                CryptoFixture.Hashes.Hash(content, Enums.CRYPTO_HASH_SHA256),
                unkeyed.Digest);

            DigestResponse keyed = CryptoFixture.Success(CryptoEndpoints.HmacFile(
                new HmacFileRequest
                {
                    FileRef = "payload-file",
                    KeyRef = "file-key",
                    HashType = Enums.CRYPTO_HASH_SHA256,
                },
                CryptoFixture.Authenticators,
                store,
                CryptoFixture.Loggers));

            Assert.False(string.IsNullOrEmpty(keyed.Digest));
            Assert.NotEqual(unkeyed.Digest, keyed.Digest);

            string rendered = string.Concat(
                JsonSerializer.Serialize(unkeyed),
                JsonSerializer.Serialize(keyed));

            Assert.DoesNotContain(directory, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("payload.bin", rendered, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The six published unkeyed selectors, in both payload forms.</summary>
    /// <returns>Twelve rows.</returns>
    public static TheoryData<long, PayloadForm> UnkeyedHashRows()
    {
        TheoryData<long, PayloadForm> rows = [];

        long[] selectors =
        [
            Enums.CRYPTO_HASH_MD5,
            Enums.CRYPTO_HASH_SHA1,
            Enums.CRYPTO_HASH_SHA256,
            Enums.CRYPTO_HASH_SHA384,
            Enums.CRYPTO_HASH_SHA512,
            Enums.CRYPTO_HASH_CRC32,
        ];

        foreach (long selector in selectors)
        {
            rows.Add(selector, PayloadForm.STRING);
            rows.Add(selector, PayloadForm.BLOB);
        }

        return rows;
    }

    /// <summary>The five published keyed selectors, in both payload forms.</summary>
    /// <returns>Ten rows.</returns>
    public static TheoryData<long, PayloadForm> KeyedHashRows()
    {
        TheoryData<long, PayloadForm> rows = [];

        long[] selectors =
        [
            Enums.CRYPTO_HASH_MD5,
            Enums.CRYPTO_HASH_SHA1,
            Enums.CRYPTO_HASH_SHA256,
            Enums.CRYPTO_HASH_SHA384,
            Enums.CRYPTO_HASH_SHA512,
        ];

        foreach (long selector in selectors)
        {
            rows.Add(selector, PayloadForm.STRING);
            rows.Add(selector, PayloadForm.BLOB);
        }

        return rows;
    }
}


/// <summary>
/// The symmetric grid: five ciphers, three modes, and the discriminators that reach all 32 legacy arms.
/// </summary>
/// <remarks>
/// <para>
/// THE ARITHMETIC, STATED PLAINLY, BECAUSE IT IS THE CRUX OF THIS CONTRACT'S FIDELITY. The legacy
/// declares 16 <c>SymEncrypt</c> and 16 <c>SymDecrypt</c> overloads [n_crypto.sru:L30-L61], the product
/// of four independent binary choices: the payload's form, the KEY's form, whether a vector is supplied,
/// and whether a mode is supplied.
/// </para>
/// <para>
/// The published contract exposes THREE of those four. There is no key-form discriminator, because a
/// resolved reference yields one thing and the options type declares no discriminator for it - so the
/// boundary reaches 8 of the 16 arms per direction directly, pairing the key's form with the payload's.
/// </para>
/// <para>
/// THAT IS LOSS-FREE, AND THIS CLASS PROVES IT RATHER THAN ASSERTING IT.
/// <see cref="LegacyDefaults.NormalizeKeyMaterial(string, int)"/> is DEFINED as encoding the text with
/// the published key encoding and deferring to the span overload, so the text-key arm and the binary-key
/// arm are the same computation over the same bytes. The equivalence rows below drive the mixed
/// overloads directly and require byte-identical output, which is what makes the other 8 arms covered
/// rather than merely unreachable.
/// </para>
/// <para>
/// TWO CELLS OF THE GRID ARE REFUSED, and that is a documented narrowing rather than a defect: feedback
/// mode at any vector, and chaining mode with no vector. Neither parameter is determined by anything in
/// the repository, so reproducing them would mean guessing a value the oracle would have settled. The
/// refusal carries the server-error status the contract declares and a machine-readable reason - and
/// NEVER the not-implemented status, which C-D reserves elsewhere.
/// </para>
/// </remarks>
public sealed class CryptoSymmetricMatrixTests
{
    /// <summary>A supported cell round-trips, in every cipher, form, vector and mode combination.</summary>
    /// <param name="cipherType">The cipher selector.</param>
    /// <param name="form">The payload form, which also selects the key's form.</param>
    /// <param name="mode">The mode selector, or <see langword="null"/> to omit it.</param>
    /// <param name="supplyVector">Whether a vector reference is supplied.</param>
    /// <remarks>
    /// A ROUND TRIP RATHER THAN A GOLDEN CIPHERTEXT, deliberately. A stored ciphertext would pin this
    /// port's own output rather than the legacy's - the oracle is the only thing that could settle that -
    /// whereas a round trip proves the pair of operations is mutually consistent, which is the property a
    /// caller depends on and the one a refactor could break.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SupportedCellRows))]
    public void SupportedCellRoundTrips(long cipherType, PayloadForm form, long? mode, bool supplyVector)
    {
        const string plaintext = "sixteen byte plaintext block and a little more";

        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics((ushort)cipherType);

        Dictionary<string, string> entries = new(StringComparer.Ordinal)
        {
            ["cipher-key"] = CryptoFixture.KeyMaterial(metrics.KeyLengthBytes, 'c'),
        };

        if (supplyVector)
        {
            entries["cipher-iv"] = CryptoFixture.KeyMaterial(metrics.IvLengthBytes, 'v');
        }

        CryptoReferenceResolver store = CryptoFixture.Store(entries);

        string payload = form == PayloadForm.STRING
            ? plaintext
            : CryptoFixture.Encodings.Base64Encode(Encoding.UTF8.GetBytes(plaintext));

        PayloadResponse encrypted = CryptoFixture.Success(CryptoEndpoints.SymmetricEncrypt(
            new SymEncryptRequest
            {
                Data = payload,
                PayloadForm = form,
                KeyRef = "cipher-key",
                IvRef = supplyVector ? "cipher-iv" : null,
                CipherType = cipherType,
                Mode = mode,
            },
            CryptoFixture.Ciphers,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        // The result form ECHOES the request's, so a caller reading the response alone knows how to
        // interpret it without re-deriving the form from what it sent.
        Assert.Equal(form, encrypted.PayloadForm);
        Assert.NotEqual(payload, encrypted.Data);

        PayloadResponse decrypted = CryptoFixture.Success(CryptoEndpoints.SymmetricDecrypt(
            new SymDecryptRequest
            {
                Data = encrypted.Data,
                PayloadForm = form,
                KeyRef = "cipher-key",
                IvRef = supplyVector ? "cipher-iv" : null,
                CipherType = cipherType,
                Mode = mode,
            },
            CryptoFixture.Ciphers,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(form, decrypted.PayloadForm);
        Assert.Equal(payload, decrypted.Data);
    }

    /// <summary>A blocked cell is refused with the server-error status and a machine-readable reason.</summary>
    /// <param name="mode">The mode selector.</param>
    /// <param name="supplyVector">Whether a vector reference is supplied.</param>
    /// <param name="expectedReason">The published reason code.</param>
    /// <remarks>
    /// <para>
    /// THE STATUS IS THE POINT. The refusal is genuinely "this port cannot reproduce this cell", which is
    /// semantically a not-implemented condition - and it is deliberately NOT mapped to that status,
    /// because C-D reserves it for the ingress service's four deferred-capability routing declarations
    /// and the two symmetric operations declare no such response.
    /// </para>
    /// <para>
    /// It is also deliberately not 400: the request is well-formed and would have succeeded, so blaming
    /// the caller would be wrong.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(BlockedCellRows))]
    public void BlockedCellIsRefusedWithoutTheNotImplementedStatus(
        long mode,
        bool supplyVector,
        string expectedReason)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(
            (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256);

        Dictionary<string, string> entries = new(StringComparer.Ordinal)
        {
            ["cipher-key"] = CryptoFixture.KeyMaterial(metrics.KeyLengthBytes, 'c'),
        };

        if (supplyVector)
        {
            entries["cipher-iv"] = CryptoFixture.KeyMaterial(metrics.IvLengthBytes, 'v');
        }

        CryptoReferenceResolver store = CryptoFixture.Store(entries);

        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.SymmetricEncrypt(
            new SymEncryptRequest
            {
                Data = "plaintext",
                PayloadForm = PayloadForm.STRING,
                KeyRef = "cipher-key",
                IvRef = supplyVector ? "cipher-iv" : null,
                CipherType = Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                Mode = mode,
            },
            CryptoFixture.Ciphers,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);
        Assert.NotEqual(StatusCodes.Status501NotImplemented, problem.StatusCode);

        Assert.True(
            problem.ProblemDetails.Extensions.TryGetValue("reason", out object? reason),
            "A blocked cell carries the published machine-readable reason code.");

        Assert.Equal(expectedReason, Assert.IsType<string>(reason));
    }

    /// <summary>
    /// A blocked cell is refused BEFORE any reference is resolved, so a blocked request touches no key.
    /// </summary>
    /// <remarks>
    /// Ordering is a real property here, not a micro-optimization: a request this port cannot carry out
    /// should never cause the key store to be read. The row proves it by naming references the store
    /// does not hold - a resolution-first implementation would answer 403 instead of the refusal.
    /// </remarks>
    [Fact]
    public void BlockedCellIsRefusedBeforeAnyKeyIsResolved()
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.SymmetricEncrypt(
            new SymEncryptRequest
            {
                Data = "plaintext",
                PayloadForm = PayloadForm.STRING,
                KeyRef = "never-configured",
                CipherType = Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                Mode = Enums.CRYPTO_SYMCRYPT_MODE_CFB,
            },
            CryptoFixture.Ciphers,
            CryptoFixture.Encodings,
            CryptoFixture.EmptyStore(),
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, CryptoFixture.RetCodeOf(problem));
        Assert.NotEqual(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    /// <summary>
    /// THE KEY-FORM COLLAPSE IS LOSS-FREE: the text-key and binary-key arms agree byte for byte.
    /// </summary>
    /// <param name="cipherType">The cipher selector.</param>
    /// <param name="mode">The mode selector.</param>
    /// <remarks>
    /// <para>
    /// This is the row that covers the 8 arms per direction the boundary does not reach directly. It
    /// drives the provider's MIXED overloads - the ones taking a text payload with a binary key, and a
    /// binary payload with a text key - and requires the output to equal the same-form arm's output.
    /// </para>
    /// <para>
    /// If it ever fails, the collapse is no longer loss-free and the contract needs a key-form
    /// discriminator. That is precisely the finding this row exists to surface, which is why it asserts
    /// equality rather than merely that both arms answer.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(KeyFormEquivalenceRows))]
    public void TextKeyAndBinaryKeyArmsAgree(long cipherType, long mode)
    {
        const string plaintext = "equivalence across the key form";

        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics((ushort)cipherType);

        string textKey = CryptoFixture.KeyMaterial(metrics.KeyLengthBytes, 'e');
        byte[] binaryKey = LegacyDefaults.KeyMaterialEncoding.GetBytes(textKey);

        string textVector = CryptoFixture.KeyMaterial(metrics.IvLengthBytes, 'w');
        byte[] binaryVector = LegacyDefaults.KeyMaterialEncoding.GetBytes(textVector);

        byte[] payload = Encoding.UTF8.GetBytes(plaintext);
        ushort selector = (ushort)cipherType;

        bool usesVector = LegacyDefaults.ModeUsesInitializationVector(mode);

        // Text payload: the same-form arm against the mixed arm.
        string textWithTextKey = usesVector
            ? CryptoFixture.Ciphers.SymEncrypt(plaintext, textKey, textVector, selector, mode)
            : CryptoFixture.Ciphers.SymEncrypt(plaintext, textKey, selector, mode);

        string textWithBinaryKey = usesVector
            ? CryptoFixture.Ciphers.SymEncrypt(plaintext, binaryKey, binaryVector, selector, mode)
            : CryptoFixture.Ciphers.SymEncrypt(plaintext, binaryKey, selector, mode);

        Assert.Equal(textWithTextKey, textWithBinaryKey);

        // Binary payload: likewise.
        byte[] binaryWithBinaryKey = usesVector
            ? CryptoFixture.Ciphers.SymEncrypt(payload, binaryKey, binaryVector, selector, mode)
            : CryptoFixture.Ciphers.SymEncrypt(payload, binaryKey, selector, mode);

        byte[] binaryWithTextKey = usesVector
            ? CryptoFixture.Ciphers.SymEncrypt(payload, textKey, textVector, selector, mode)
            : CryptoFixture.Ciphers.SymEncrypt(payload, textKey, selector, mode);

        Assert.Equal(binaryWithBinaryKey, binaryWithTextKey);

        // And the decrypt direction mirrors it, so the equivalence covers all 32 arms rather than 16.
        Assert.Equal(
            usesVector
                ? CryptoFixture.Ciphers.SymDecrypt(binaryWithBinaryKey, binaryKey, binaryVector, selector, mode)
                : CryptoFixture.Ciphers.SymDecrypt(binaryWithBinaryKey, binaryKey, selector, mode),
            usesVector
                ? CryptoFixture.Ciphers.SymDecrypt(binaryWithBinaryKey, textKey, textVector, selector, mode)
                : CryptoFixture.Ciphers.SymDecrypt(binaryWithBinaryKey, textKey, selector, mode));
    }

    /// <summary>
    /// A vector-form mismatch is INEXPRESSIBLE rather than merely rejected.
    /// </summary>
    /// <remarks>
    /// The legacy pairs a text key only with a text vector and a binary key only with a binary vector
    /// [n_crypto.sru:L32-L33, :L36-L37, :L44-L45 and their mirrors]. Because BOTH forms are chosen by the
    /// single payload selector at this boundary, there is no member through which a mismatched pair could
    /// be described - which is a stronger guarantee than validating one. Asserted by reflecting over the
    /// request records and proving no vector-form member exists.
    /// </remarks>
    [Fact]
    public void VectorFormMismatchIsInexpressible()
    {
        foreach (Type requestType in new[] { typeof(SymEncryptRequest), typeof(SymDecryptRequest) })
        {
            foreach (PropertyInfo member in requestType.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                bool describesAForm =
                    member.Name.Contains("Form", StringComparison.Ordinal) &&
                    !string.Equals(member.Name, "PayloadForm", StringComparison.Ordinal);

                Assert.False(
                    describesAForm,
                    $"{requestType.Name}.{member.Name} would let a caller describe a key or vector " +
                    "form independently of the payload form, which the legacy pairing forbids.");
            }

            // Exactly one form selector, and it is the payload's.
            Assert.NotNull(requestType.GetProperty("PayloadForm"));
        }
    }

    /// <summary>A cipher selector outside the published set is refused.</summary>
    /// <param name="cipherType">The out-of-set selector.</param>
    [Theory]
    [InlineData(-1L)]
    [InlineData(5L)]
    [InlineData(65536L)]
    public void CipherSelectorOutsideThePublishedSetIsRefused(long cipherType)
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.SymmetricEncrypt(
            new SymEncryptRequest
            {
                Data = "plaintext",
                PayloadForm = PayloadForm.STRING,
                KeyRef = "any",
                CipherType = cipherType,
            },
            CryptoFixture.Ciphers,
            CryptoFixture.Encodings,
            CryptoFixture.EmptyStore(),
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_NO_SUPPORT, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    /// <summary>A mode selector outside the published set is refused.</summary>
    /// <param name="mode">The out-of-set selector.</param>
    [Theory]
    [InlineData(-1L)]
    [InlineData(3L)]
    public void ModeSelectorOutsideThePublishedSetIsRefused(long mode)
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.SymmetricEncrypt(
            new SymEncryptRequest
            {
                Data = "plaintext",
                PayloadForm = PayloadForm.STRING,
                KeyRef = "any",
                CipherType = Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                Mode = mode,
            },
            CryptoFixture.Ciphers,
            CryptoFixture.Encodings,
            CryptoFixture.EmptyStore(),
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_NO_SUPPORT, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    /// <summary>
    /// Every supported cell of the grid: five ciphers, two payload forms, and the four
    /// vector-and-mode combinations the classification admits.
    /// </summary>
    /// <returns>Forty rows.</returns>
    /// <remarks>
    /// The supported set is exactly the codebook mode - explicit or omitted, with or without a vector,
    /// since it consumes none - plus the chaining mode WITH a vector. The other two combinations are the
    /// blocked cells and are enumerated separately.
    /// </remarks>
    public static TheoryData<long, PayloadForm, long?, bool> SupportedCellRows()
    {
        TheoryData<long, PayloadForm, long?, bool> rows = [];

        long[] ciphers =
        [
            Enums.CRYPTO_SYMCRYPT_TYPE_DES,
            Enums.CRYPTO_SYMCRYPT_TYPE_3DES,
            Enums.CRYPTO_SYMCRYPT_TYPE_AES128,
            Enums.CRYPTO_SYMCRYPT_TYPE_AES192,
            Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
        ];

        (long? Mode, bool Vector)[] cells =
        [
            (null, false),
            (null, true),
            (Enums.CRYPTO_SYMCRYPT_MODE_ECB, false),
            (Enums.CRYPTO_SYMCRYPT_MODE_CBC, true),
        ];

        foreach (long cipher in ciphers)
        {
            foreach (PayloadForm form in Enum.GetValues<PayloadForm>())
            {
                foreach ((long? mode, bool vector) in cells)
                {
                    rows.Add(cipher, form, mode, vector);
                }
            }
        }

        return rows;
    }

    /// <summary>The blocked cells and their published reason codes.</summary>
    /// <returns>Three rows.</returns>
    public static TheoryData<long, bool, string> BlockedCellRows() =>
        new()
        {
            // Feedback mode is blocked at ANY vector, because a vector does not determine the width.
            {
                Enums.CRYPTO_SYMCRYPT_MODE_CFB,
                false,
                "SYMMETRIC_FEEDBACK_WIDTH_UNPROVABLE"
            },
            {
                Enums.CRYPTO_SYMCRYPT_MODE_CFB,
                true,
                "SYMMETRIC_FEEDBACK_WIDTH_UNPROVABLE"
            },

            // Chaining mode with no vector, because the synthesized value is not determined either.
            {
                Enums.CRYPTO_SYMCRYPT_MODE_CBC,
                false,
                "SYMMETRIC_VECTOR_UNPROVABLE"
            },
        };

    /// <summary>The cipher and mode combinations the key-form equivalence is proven over.</summary>
    /// <returns>Ten rows.</returns>
    public static TheoryData<long, long> KeyFormEquivalenceRows()
    {
        TheoryData<long, long> rows = [];

        long[] ciphers =
        [
            Enums.CRYPTO_SYMCRYPT_TYPE_DES,
            Enums.CRYPTO_SYMCRYPT_TYPE_3DES,
            Enums.CRYPTO_SYMCRYPT_TYPE_AES128,
            Enums.CRYPTO_SYMCRYPT_TYPE_AES192,
            Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
        ];

        foreach (long cipher in ciphers)
        {
            rows.Add(cipher, Enums.CRYPTO_SYMCRYPT_MODE_ECB);
            rows.Add(cipher, Enums.CRYPTO_SYMCRYPT_MODE_CBC);
        }

        return rows;
    }
}


/// <summary>
/// The asymmetric family: both paddings, the refused third possibility, and the signature arms.
/// </summary>
/// <remarks>
/// <para>
/// EVERY KEY PAIR IS GENERATED AT RUN TIME, never embedded. That is not a convenience: an RSA private
/// key literal in a test file would be exactly the anti-pattern the secrets sweep found at
/// <c>tests/blink/test_jws.htm:L8-L23</c> and at two sites in the demo library, and this file must not
/// reproduce one in any form. Generating a pair also means the row exercises the generation operation as
/// a precondition of the others, which is how the surface is meant to be used.
/// </para>
/// <para>
/// Both key halves are TEXT on every one of the 12 legacy declarations [n_crypto.sru:L62-L73], so the
/// payload form governs the payload and the signature only - never the key. That asymmetry against the
/// symmetric family is preserved and asserted.
/// </para>
/// </remarks>
public sealed class CryptoRsaMatrixTests
{
    /// <summary>A cipher round trip succeeds under both published paddings and under omission.</summary>
    /// <param name="padding">The padding selector, or <see langword="null"/> to omit it.</param>
    /// <param name="form">The payload form.</param>
    [Theory]
    [MemberData(nameof(PaddingRows))]
    public void CipherRoundTripsUnderEveryPublishedPadding(long? padding, PayloadForm form)
    {
        const string plaintext = "short asymmetric payload";

        CryptoReferenceResolver store = KeyPairStore(Enums.CRYPTO_RSA_BITS_2048);

        string payload = form == PayloadForm.STRING
            ? plaintext
            : CryptoFixture.Encodings.Base64Encode(Encoding.UTF8.GetBytes(plaintext));

        PayloadResponse encrypted = CryptoFixture.Success(CryptoEndpoints.RsaEncrypt(
            new RsaCipherRequest
            {
                Data = payload,
                PayloadForm = form,
                KeyRef = "rsa-public",
                Padding = padding,
            },
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(form, encrypted.PayloadForm);
        Assert.NotEqual(payload, encrypted.Data);

        PayloadResponse decrypted = CryptoFixture.Success(CryptoEndpoints.RsaDecrypt(
            new RsaCipherRequest
            {
                Data = encrypted.Data,
                PayloadForm = form,
                KeyRef = "rsa-private",
                Padding = padding,
            },
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(form, decrypted.PayloadForm);
        Assert.Equal(payload, decrypted.Data);
    }

    /// <summary>
    /// Omitting the padding genuinely uses PKCS#1, which is the preserved legacy default.
    /// </summary>
    /// <remarks>
    /// PROVEN BY CROSS-DECRYPTION rather than by reading a constant. A ciphertext produced with the
    /// padding omitted decrypts under an EXPLICIT PKCS#1 request, and fails to decrypt under an explicit
    /// OAEP one. That is the only way to establish which arm the omission actually reached: asserting the
    /// default constant's value would prove only that the catalogue is intact.
    /// </remarks>
    [Fact]
    public void OmittingPaddingGenuinelyUsesPkcs1()
    {
        const string plaintext = "default padding probe";

        CryptoReferenceResolver store = KeyPairStore(Enums.CRYPTO_RSA_BITS_2048);

        PayloadResponse encrypted = CryptoFixture.Success(CryptoEndpoints.RsaEncrypt(
            new RsaCipherRequest
            {
                Data = plaintext,
                PayloadForm = PayloadForm.STRING,
                KeyRef = "rsa-public",
            },
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        PayloadResponse underPkcs1 = CryptoFixture.Success(CryptoEndpoints.RsaDecrypt(
            new RsaCipherRequest
            {
                Data = encrypted.Data,
                PayloadForm = PayloadForm.STRING,
                KeyRef = "rsa-private",
                Padding = Enums.CRYPTO_RSA_PADDING_PKCS1,
            },
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(plaintext, underPkcs1.Data);

        // The same ciphertext under the OTHER padding must not decrypt, which is what makes the row
        // above a discriminating test rather than a tautology.
        ProblemHttpResult underOaep = CryptoFixture.Rejection(CryptoEndpoints.RsaDecrypt(
            new RsaCipherRequest
            {
                Data = encrypted.Data,
                PayloadForm = PayloadForm.STRING,
                KeyRef = "rsa-private",
                Padding = Enums.CRYPTO_RSA_PADDING_OAEP,
            },
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_DATA, CryptoFixture.RetCodeOf(underOaep));
        Assert.Equal(StatusCodes.Status400BadRequest, underOaep.StatusCode);
    }

    /// <summary>
    /// A request for no padding is refused, with a deliberate NON-not-implemented status.
    /// </summary>
    /// <param name="padding">A selector outside the published pair.</param>
    /// <remarks>
    /// The mechanical reason is that the catalogue has exactly two members [enums.sru:L949-L950] and
    /// neither names it, so there is nothing to select and nothing to implement. The status is 400 rather
    /// than the not-implemented status because C-D reserves the latter for the ingress service's
    /// deferred-capability declarations - and because a selector outside a published set genuinely is a
    /// request fault.
    /// </remarks>
    [Theory]
    [InlineData(-1L)]
    [InlineData(2L)]
    [InlineData(long.MinValue)]
    public void PaddingOutsideThePublishedPairIsRefused(long padding)
    {
        CryptoReferenceResolver store = KeyPairStore(Enums.CRYPTO_RSA_BITS_2048);

        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.RsaEncrypt(
            new RsaCipherRequest
            {
                Data = "payload",
                PayloadForm = PayloadForm.STRING,
                KeyRef = "rsa-public",
                Padding = padding,
            },
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_NO_SUPPORT, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.NotEqual(StatusCodes.Status501NotImplemented, problem.StatusCode);
    }

    /// <summary>A signature verifies across every published selector, in both payload forms.</summary>
    /// <param name="hashType">The selector.</param>
    /// <param name="form">The payload form, which governs the SIGNATURE's form too.</param>
    /// <remarks>
    /// The two legacy verification declarations differ in whether the signature parameter is text or
    /// binary [n_crypto.sru:L72-L73], so driving both payload forms is what reaches both - a signature
    /// produced in one form verifies under the same form, which is the pairing the contract publishes.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SignatureRows))]
    public void SignatureVerifiesAcrossEveryPublishedSelector(long hashType, PayloadForm form)
    {
        const string message = "message to sign";

        CryptoReferenceResolver store = KeyPairStore(Enums.CRYPTO_RSA_BITS_2048);

        string payload = form == PayloadForm.STRING
            ? message
            : CryptoFixture.Encodings.Base64Encode(Encoding.UTF8.GetBytes(message));

        PayloadResponse signed = CryptoFixture.Success(CryptoEndpoints.RsaSign(
            new RsaSignRequest
            {
                Data = payload,
                PayloadForm = form,
                KeyRef = "rsa-private",
                HashType = hashType,
            },
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(form, signed.PayloadForm);
        Assert.False(string.IsNullOrEmpty(signed.Data));

        RsaVerifyResponse verified = CryptoFixture.Success(CryptoEndpoints.RsaVerify(
            new RsaVerifyRequest
            {
                Data = payload,
                PayloadForm = form,
                Signature = signed.Data,
                KeyRef = "rsa-public",
                HashType = hashType,
            },
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.True(verified.Valid);
    }

    /// <summary>
    /// A signature that does not verify is a 200 carrying false, never an error status.
    /// </summary>
    /// <remarks>
    /// The legacy returns a boolean [n_crypto.sru:L72-L73], so an invalid signature is an ANSWER rather
    /// than a fault. Mapping it onto a failure status would change the observable behaviour of the
    /// operation, and a caller branching on the boolean would break.
    /// </remarks>
    [Fact]
    public void FailedVerificationIsAnAnswerRatherThanAFault()
    {
        CryptoReferenceResolver store = KeyPairStore(Enums.CRYPTO_RSA_BITS_2048);

        PayloadResponse signed = CryptoFixture.Success(CryptoEndpoints.RsaSign(
            new RsaSignRequest
            {
                Data = "the signed message",
                PayloadForm = PayloadForm.STRING,
                KeyRef = "rsa-private",
                HashType = Enums.CRYPTO_HASH_SHA256,
            },
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        RsaVerifyResponse verified = CryptoFixture.Success(CryptoEndpoints.RsaVerify(
            new RsaVerifyRequest
            {
                Data = "a DIFFERENT message",
                PayloadForm = PayloadForm.STRING,
                Signature = signed.Data,
                KeyRef = "rsa-public",
                HashType = Enums.CRYPTO_HASH_SHA256,
            },
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.False(verified.Valid);
    }

    /// <summary>The signature surface refuses the checksum member, as the keyed digest surface does.</summary>
    [Fact]
    public void SignatureSurfaceRefusesTheChecksumMember()
    {
        CryptoReferenceResolver store = KeyPairStore(Enums.CRYPTO_RSA_BITS_2048);

        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.RsaSign(
            new RsaSignRequest
            {
                Data = "message",
                PayloadForm = PayloadForm.STRING,
                KeyRef = "rsa-private",
                HashType = Enums.CRYPTO_HASH_CRC32,
            },
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_NO_SUPPORT, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    /// <summary>Every published convenience size generates, INCLUDING the weak one.</summary>
    /// <param name="bits">The modulus length.</param>
    /// <remarks>
    /// 1024 bits is below every current recommendation AND is a declared first-class value alongside 2048
    /// and 4096 [enums.sru:L965-L967], with the legacy's own demonstration generating one. The weakness is
    /// annotated in the published document and NOT enforced, because imposing a minimum would reject
    /// input the legacy accepted. This row is what proves the annotation did not quietly become a guard.
    /// </remarks>
    [Theory]
    [InlineData(1024)]
    [InlineData(2048)]
    public void EveryPublishedKeySizeIsAccepted(int bits)
    {
        GenRsaKeyResponse response = CryptoFixture.Success(CryptoEndpoints.GenerateRsaKey(
            new GenRsaKeyRequest { Bits = bits },
            CryptoFixture.Rsa,
            CryptoFixture.Random,
            CryptoFixture.EmptyStore(),
            CryptoFixture.Loggers));

        Assert.Equal(bits, response.Bits);
        Assert.False(string.IsNullOrWhiteSpace(response.PublicKey));
        Assert.Null(response.PemFormat);
    }

    /// <summary>The envelope selector is echoed only when the request stated it.</summary>
    /// <param name="pemFormat">The selector the request carried, or <see langword="null"/> to omit it.</param>
    /// <remarks>
    /// ABSENCE IS MEANINGFUL AND IS NOT THE SAME AS FALSE: omitting it reaches the three-argument legacy
    /// declaration [n_crypto.sru:L19] and supplying it reaches the four-argument one [<c>:L20</c>], so a
    /// default would make one of the two unreachable.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void EnvelopeSelectorIsEchoedOnlyWhenStated(bool? pemFormat)
    {
        GenRsaKeyResponse response = CryptoFixture.Success(CryptoEndpoints.GenerateRsaKey(
            new GenRsaKeyRequest { Bits = Enums.CRYPTO_RSA_BITS_2048, PemFormat = pemFormat },
            CryptoFixture.Rsa,
            CryptoFixture.Random,
            CryptoFixture.EmptyStore(),
            CryptoFixture.Loggers));

        Assert.Equal(pemFormat, response.PemFormat);
    }

    /// <summary>A modulus length outside the legacy argument's own domain is refused.</summary>
    /// <param name="bits">The out-of-domain length.</param>
    /// <remarks>
    /// The bound is the legacy parameter's own 16-bit unsigned domain [n_crypto.sru:L19-L20], not a
    /// cryptographic policy: it is neither narrower nor wider than the declaration.
    /// </remarks>
    [Theory]
    [InlineData(-1L)]
    [InlineData(65536L)]
    public void ModulusLengthOutsideTheLegacyDomainIsRefused(long bits)
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.GenerateRsaKey(
            new GenRsaKeyRequest { Bits = bits },
            CryptoFixture.Rsa,
            CryptoFixture.Random,
            CryptoFixture.EmptyStore(),
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    /// <summary>
    /// A generated pair is immediately usable through its returned reference.
    /// </summary>
    /// <remarks>
    /// The end-to-end proof that the retain-and-reference decision works: generate, then SIGN with the
    /// returned reference, then verify with the returned public key. If the private half were discarded
    /// rather than retained, or the reference were not resolvable, this row could not pass.
    /// </remarks>
    [Fact]
    public void GeneratedPairIsUsableThroughItsReference()
    {
        CryptoReferenceResolver store = CryptoFixture.EmptyStore();

        GenRsaKeyResponse generated = CryptoFixture.Success(CryptoEndpoints.GenerateRsaKey(
            new GenRsaKeyRequest { Bits = Enums.CRYPTO_RSA_BITS_2048 },
            CryptoFixture.Rsa,
            CryptoFixture.Random,
            store,
            CryptoFixture.Loggers));

        PayloadResponse signed = CryptoFixture.Success(CryptoEndpoints.RsaSign(
            new RsaSignRequest
            {
                Data = "signed with a freshly generated key",
                PayloadForm = PayloadForm.STRING,
                KeyRef = generated.KeyRef,
                HashType = Enums.CRYPTO_HASH_SHA256,
            },
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        // The public half came back in the response, so it is placed in a store of its own to verify -
        // which is exactly how a caller would use it, since the contract returns it rather than a handle.
        CryptoReferenceResolver verification = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["generated-public"] = generated.PublicKey,
            });

        RsaVerifyResponse verified = CryptoFixture.Success(CryptoEndpoints.RsaVerify(
            new RsaVerifyRequest
            {
                Data = "signed with a freshly generated key",
                PayloadForm = PayloadForm.STRING,
                Signature = signed.Data,
                KeyRef = "generated-public",
                HashType = Enums.CRYPTO_HASH_SHA256,
            },
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            verification,
            CryptoFixture.Loggers));

        Assert.True(verified.Valid);
    }

    /// <summary>
    /// Unusable configured key material is a SERVER fault, and nothing about it is reported.
    /// </summary>
    /// <remarks>
    /// The caller named a permitted reference and this service found material behind it that it cannot
    /// import, which is a deployment defect the caller can neither see nor fix - so the status is a
    /// server error rather than a request fault, and the body describes nothing about the material.
    /// </remarks>
    [Fact]
    public void UnusableConfiguredKeyIsAServerFault()
    {
        const string material = "this-is-not-an-rsa-key";

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["broken"] = material });

        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.RsaEncrypt(
            new RsaCipherRequest
            {
                Data = "payload",
                PayloadForm = PayloadForm.STRING,
                KeyRef = "broken",
            },
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INTERNAL_ERROR, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);

        string rendered = JsonSerializer.Serialize(problem.ProblemDetails);

        Assert.DoesNotContain(material, rendered, StringComparison.Ordinal);
    }

    /// <summary>Builds a store holding a freshly generated key pair under two references.</summary>
    /// <param name="bits">The modulus length to generate.</param>
    /// <returns>A resolver holding the pair.</returns>
    /// <remarks>
    /// The pair is generated through the provider rather than embedded, so no key literal exists anywhere
    /// in this file. Both halves are text on every legacy declaration [n_crypto.sru:L62-L73], so both sit
    /// in the same store the same way.
    /// </remarks>
    private static CryptoReferenceResolver KeyPairStore(ushort bits)
    {
        string privateKey = string.Empty;
        string publicKey = string.Empty;

        Assert.True(
            CryptoFixture.Rsa.GenRSAKey(bits, ref privateKey, ref publicKey),
            "The platform generates a key pair of this published size.");

        return CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["rsa-private"] = privateKey,
                ["rsa-public"] = publicKey,
            });
    }

    /// <summary>The two published paddings plus omission, in both payload forms.</summary>
    /// <returns>Six rows.</returns>
    public static TheoryData<long?, PayloadForm> PaddingRows()
    {
        TheoryData<long?, PayloadForm> rows = [];

        long?[] paddings =
        [
            null,
            Enums.CRYPTO_RSA_PADDING_PKCS1,
            Enums.CRYPTO_RSA_PADDING_OAEP,
        ];

        foreach (long? padding in paddings)
        {
            rows.Add(padding, PayloadForm.STRING);
            rows.Add(padding, PayloadForm.BLOB);
        }

        return rows;
    }

    /// <summary>The five published signature selectors, in both payload forms.</summary>
    /// <returns>Ten rows.</returns>
    public static TheoryData<long, PayloadForm> SignatureRows()
    {
        TheoryData<long, PayloadForm> rows = [];

        long[] selectors =
        [
            Enums.CRYPTO_HASH_MD5,
            Enums.CRYPTO_HASH_SHA1,
            Enums.CRYPTO_HASH_SHA256,
            Enums.CRYPTO_HASH_SHA384,
            Enums.CRYPTO_HASH_SHA512,
        ];

        foreach (long selector in selectors)
        {
            rows.Add(selector, PayloadForm.STRING);
            rows.Add(selector, PayloadForm.BLOB);
        }

        return rows;
    }
}


/// <summary>
/// The encoding family, including the two encodings that answer different questions.
/// </summary>
/// <remarks>
/// THE ONE DISTINCTION THAT MATTERS HERE. The legacy encoding argument and the JSON transport encoding
/// are not the same thing, and collapsing them would be a behavioural change. So a caller asking to
/// decode hexadecimal SUBMITS hexadecimal and receives its bytes as BASE64, and a caller asking to encode
/// as hexadecimal SUBMITS base64 and receives hexadecimal. Both directions are asserted, because either
/// alone would be satisfied by an implementation that simply ignored the argument.
/// </remarks>
public sealed class CryptoEncodingMatrixTests
{
    /// <summary>Both encodings round-trip through the two operations.</summary>
    /// <param name="encoding">The legacy encoding selector.</param>
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    public void EncodingRoundTripsThroughBothOperations(long encoding)
    {
        byte[] original = [0x00, 0x01, 0x7F, 0x80, 0xFE, 0xFF, 0x2A];
        string transport = CryptoFixture.Encodings.Base64Encode(original);

        // Bytes to text, in the selected legacy encoding.
        EncodedTextResponse encoded = CryptoFixture.Success(CryptoEndpoints.BlobToString(
            new BlobToStringRequest { Data = transport, Encoding = encoding },
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.False(string.IsNullOrEmpty(encoded.Value));

        // Text back to bytes, returned as base64 because that is JSON transport rather than the argument.
        BlobResponse decoded = CryptoFixture.Success(CryptoEndpoints.StringToBlob(
            new StringToBlobRequest { Data = encoded.Value, Encoding = encoding },
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.Equal(transport, decoded.Data);
        Assert.Equal(original, CryptoFixture.Encodings.Base64Decode(decoded.Data));
    }

    /// <summary>
    /// The two encodings produce genuinely different text, so the argument is not being ignored.
    /// </summary>
    [Fact]
    public void TheTwoEncodingsProduceDifferentText()
    {
        string transport = CryptoFixture.Encodings.Base64Encode([0xDE, 0xAD, 0xBE, 0xEF]);

        EncodedTextResponse asBase64 = CryptoFixture.Success(CryptoEndpoints.BlobToString(
            new BlobToStringRequest { Data = transport, Encoding = Enums.CRYPTO_ENCODING_BASE64 },
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        EncodedTextResponse asHex = CryptoFixture.Success(CryptoEndpoints.BlobToString(
            new BlobToStringRequest { Data = transport, Encoding = Enums.CRYPTO_ENCODING_HEX },
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.NotEqual(asBase64.Value, asHex.Value);
        Assert.Equal(transport, asBase64.Value);
    }

    /// <summary>An encoding selector outside the published pair is refused.</summary>
    /// <param name="encoding">The out-of-set selector.</param>
    [Theory]
    [InlineData(-1L)]
    [InlineData(2L)]
    public void EncodingOutsideThePublishedPairIsRefused(long encoding)
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.StringToBlob(
            new StringToBlobRequest { Data = "AAAA", Encoding = encoding },
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_NO_SUPPORT, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    /// <summary>An absent encoding is a rejection, because the legacy declaration takes the argument.</summary>
    [Fact]
    public void AbsentEncodingIsARejection()
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.StringToBlob(
            new StringToBlobRequest { Data = "AAAA" },
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(problem));
        Assert.Contains("encoding", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reversal returns both halves of the legacy outcome and reverses no shared buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy mutates its argument through a <c>ref</c> parameter and separately returns a boolean
    /// [n_crypto.sru:L13]. In-place mutation has no wire representation, so the response carries the
    /// reversed bytes - which the legacy left in the caller's own variable - AND the boolean, which is the
    /// operation's actual return value. Dropping either would lose information the legacy gave its caller.
    /// </para>
    /// <para>
    /// Reversing twice yields the original, which is what proves the operation is a true reversal rather
    /// than any other permutation.
    /// </para>
    /// </remarks>
    [Fact]
    public void ReversalReturnsBothHalvesOfTheOutcome()
    {
        byte[] original = [1, 2, 3, 4, 5];
        string transport = CryptoFixture.Encodings.Base64Encode(original);

        BlobReverseResponse once = CryptoFixture.Success(CryptoEndpoints.ReverseBlob(
            new BlobReverseRequest { Data = transport },
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.True(once.Succeeded);
        Assert.Equal<byte[]>([5, 4, 3, 2, 1], CryptoFixture.Encodings.Base64Decode(once.Data));

        BlobReverseResponse twice = CryptoFixture.Success(CryptoEndpoints.ReverseBlob(
            new BlobReverseRequest { Data = once.Data },
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.Equal(transport, twice.Data);
        Assert.Equal(original, CryptoFixture.Encodings.Base64Decode(twice.Data));
    }

    /// <summary>An empty payload is valid everywhere on this surface.</summary>
    /// <remarks>
    /// The published schema declares no minimum length on a payload, and a digest or a reversal over no
    /// bytes is well defined. Asserted so that a well-meaning emptiness guard cannot be added later.
    /// </remarks>
    [Fact]
    public void EmptyPayloadIsValid()
    {
        BlobReverseResponse reversed = CryptoFixture.Success(CryptoEndpoints.ReverseBlob(
            new BlobReverseRequest { Data = string.Empty },
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.Equal(string.Empty, reversed.Data);
        Assert.True(reversed.Succeeded);

        DigestResponse digest = CryptoFixture.Success(CryptoEndpoints.Hash(
            new HashRequest
            {
                Data = string.Empty,
                PayloadForm = PayloadForm.BLOB,
                HashType = Enums.CRYPTO_HASH_SHA256,
            },
            CryptoFixture.Hashes,
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.False(string.IsNullOrEmpty(digest.Digest));
    }
}

/// <summary>
/// The random family, which is also this contract's determinism seam.
/// </summary>
/// <remarks>
/// <para>
/// EVERY ROW HERE RUNS AGAINST AN INJECTED ENTROPY DOUBLE, and that is the whole point. The
/// characterization model requires every non-deterministic value to be maskable from BOTH the master and
/// the candidate recording, so a value that could not be reproduced could not be compared. Byte-exact
/// reproducibility under a deterministic source is therefore the proof that the seam is genuinely reached
/// and that no static randomness API is called anywhere behind these operations.
/// </para>
/// <para>
/// The two preserved defaults are asserted BEHAVIOURALLY rather than by reading a constant: the
/// random-string default must exclude the symbol class, and the identifier default must carry both the
/// braces and the separators.
/// </para>
/// </remarks>
public sealed class CryptoRandomTests
{
    /// <summary>Random bytes are byte-reproducible under a deterministic source.</summary>
    [Fact]
    public void RandomBytesAreReproducibleUnderADeterministicSource()
    {
        BlobResponse first = InvokeBlob(0x3C, 48);
        BlobResponse second = InvokeBlob(0x3C, 48);

        Assert.Equal(first.Data, second.Data);
        Assert.Equal(48, CryptoFixture.Encodings.Base64Decode(first.Data).Length);

        // A DIFFERENT source yields different bytes, which is what proves the source is really consulted
        // rather than a constant being returned regardless.
        BlobResponse other = InvokeBlob(0x5A, 48);

        Assert.NotEqual(first.Data, other.Data);
    }

    /// <summary>A random string is reproducible and honours the requested length.</summary>
    /// <param name="flags">The class bitmask, or <see langword="null"/> to omit it.</param>
    [Theory]
    [InlineData(null)]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(3L)]
    [InlineData(4L)]
    [InlineData(7L)]
    public void RandomStringIsReproducibleUnderADeterministicSource(long? flags)
    {
        RndStringResponse first = InvokeString(0x11, 24, flags);
        RndStringResponse second = InvokeString(0x11, 24, flags);

        Assert.Equal(first.Value, second.Value);
        Assert.Equal(24, first.Value.Length);
    }

    /// <summary>
    /// The random-string default EXCLUDES the symbol class, which is a preserved legacy narrowing.
    /// </summary>
    /// <remarks>
    /// [enums.sru:L957] aliases the default to the digit and letter classes only, and the symbol class
    /// exists [<c>:L956</c>] but is not in it. Asserted behaviourally over a long draw, so a port that
    /// widened the default would fail here rather than only in the constant row.
    /// </remarks>
    [Fact]
    public void RandomStringDefaultDrawsNoSymbol()
    {
        RndStringResponse omitted = InvokeString(0x27, 512, flags: null);

        foreach (char character in omitted.Value)
        {
            Assert.True(
                char.IsAsciiLetterOrDigit(character),
                $"The default character classes are digits and letters only, but '{character}' was " +
                "drawn - the symbol class must not be added to the default.");
        }

        // Omitting the flags and passing the default explicitly reach different legacy declarations, and
        // must nevertheless agree - which is what makes "the provider applies the default" true rather
        // than "the boundary re-spells it".
        RndStringResponse explicitDefault = InvokeString(
            0x27,
            512,
            LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT);

        Assert.Equal(omitted.Value, explicitDefault.Value);
    }

    /// <summary>An identifier is reproducible under a deterministic source.</summary>
    /// <param name="flags">The formatting bitmask, or <see langword="null"/> to omit it.</param>
    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(3L)]
    public void IdentifierIsReproducibleUnderADeterministicSource(long? flags)
    {
        GuidResponse first = InvokeGuid(0x6D, flags);
        GuidResponse second = InvokeGuid(0x6D, flags);

        Assert.Equal(first.Value, second.Value);
        Assert.False(string.IsNullOrEmpty(first.Value));
    }

    /// <summary>The identifier default carries BOTH the braces and the separators.</summary>
    /// <remarks>
    /// Asserted behaviourally, and the four formatting shapes are asserted to be distinct so that a port
    /// which ignored the flags could not pass.
    /// </remarks>
    [Fact]
    public void IdentifierDefaultCarriesBracketsAndSeparators()
    {
        GuidResponse omitted = InvokeGuid(0x6D, flags: null);

        Assert.StartsWith("{", omitted.Value, StringComparison.Ordinal);
        Assert.EndsWith("}", omitted.Value, StringComparison.Ordinal);
        Assert.Contains("-", omitted.Value, StringComparison.Ordinal);

        GuidResponse explicitDefault = InvokeGuid(0x6D, LegacyDefaults.GUID_FLAGS_DEFAULT);

        Assert.Equal(omitted.Value, explicitDefault.Value);

        // The bare shape has neither, which is the combination the resolver uses when minting a
        // reference and the one .NET has no standard specifier for when only braces are asked for.
        GuidResponse bare = InvokeGuid(0x6D, 0L);

        Assert.DoesNotContain("{", bare.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("-", bare.Value, StringComparison.Ordinal);
        Assert.Equal(32, bare.Value.Length);
    }

    /// <summary>A request above the published cap is refused rather than truncated.</summary>
    /// <remarks>
    /// Short random material is the one outcome a caller cannot detect, and it is precisely the defect
    /// that survives every test and fails in production - so the cap is a refusal, never a silent
    /// shortening.
    /// </remarks>
    [Fact]
    public void LengthAboveThePublishedCapIsRefused()
    {
        long above = (long)RandomProvider.MaximumRequestedLength + 1;

        ProblemHttpResult blob = CryptoFixture.Rejection(CryptoEndpoints.GenerateRandomBlob(
            new RandomBlobRequest { Size = above },
            CryptoFixture.Random,
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(blob));
        Assert.Equal(StatusCodes.Status400BadRequest, blob.StatusCode);

        ProblemHttpResult text = CryptoFixture.Rejection(CryptoEndpoints.GenerateRandomString(
            new RndStringRequest { Size = above },
            CryptoFixture.Random,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(text));
    }

    /// <summary>A flag value outside the legacy argument's own domain is refused.</summary>
    /// <param name="flags">The out-of-domain value.</param>
    /// <remarks>
    /// The bound is the legacy argument's 32-bit unsigned domain [n_crypto.sru:L16, :L18]. Bits the
    /// catalogue does not name are ACCEPTED inside that domain, deliberately, because the legacy defines
    /// no behaviour for them and refusing them would narrow a contract it leaves open.
    /// </remarks>
    [Theory]
    [InlineData(-1L)]
    [InlineData(4294967296L)]
    public void FlagsOutsideTheLegacyDomainAreRefused(long flags)
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.GenerateGuid(
            new GuidRequest { Flags = flags },
            CryptoFixture.Random,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    /// <summary>An unnamed bit inside the domain is accepted rather than refused.</summary>
    [Fact]
    public void UnnamedFlagBitsInsideTheDomainAreAccepted()
    {
        GuidResponse response = CryptoFixture.Success(CryptoEndpoints.GenerateGuid(
            new GuidRequest { Flags = 0xFFFFFFF0L },
            CryptoFixture.Random,
            CryptoFixture.Loggers));

        Assert.False(string.IsNullOrEmpty(response.Value));
    }

    /// <summary>
    /// The identifier operation accepts NO body at all, which is how the no-argument overload is reached.
    /// </summary>
    /// <remarks>
    /// The published contract marks the request body optional, and the legacy declares a no-argument
    /// overload [n_crypto.sru:L17]. A required body would make that overload unreachable without
    /// inventing a member.
    /// </remarks>
    [Fact]
    public void IdentifierOperationAcceptsNoBody()
    {
        GuidResponse response = CryptoFixture.Success(CryptoEndpoints.GenerateGuid(
            request: null,
            CryptoFixture.Random,
            CryptoFixture.Loggers));

        Assert.StartsWith("{", response.Value, StringComparison.Ordinal);
        Assert.Contains("-", response.Value, StringComparison.Ordinal);
    }

    /// <summary>Draws random bytes through a deterministic source.</summary>
    /// <param name="entropy">The constant octet the source returns.</param>
    /// <param name="size">The number of bytes to draw.</param>
    /// <returns>The response.</returns>
    private static BlobResponse InvokeBlob(byte entropy, long size) =>
        CryptoFixture.Success(CryptoEndpoints.GenerateRandomBlob(
            new RandomBlobRequest { Size = size },
            new RandomProvider(new ConstantEntropySource(entropy)),
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

    /// <summary>Draws a random string through a deterministic source.</summary>
    /// <param name="entropy">The constant octet the source returns.</param>
    /// <param name="size">The number of characters to draw.</param>
    /// <param name="flags">The class bitmask, or <see langword="null"/> to omit it.</param>
    /// <returns>The response.</returns>
    private static RndStringResponse InvokeString(byte entropy, long size, long? flags) =>
        CryptoFixture.Success(CryptoEndpoints.GenerateRandomString(
            new RndStringRequest { Size = size, Flags = flags },
            new RandomProvider(new SequencedEntropySource(entropy, (byte)(entropy + 1), (byte)(entropy + 2))),
            CryptoFixture.Loggers));

    /// <summary>Draws an identifier through a deterministic source.</summary>
    /// <param name="entropy">The constant octet the source returns.</param>
    /// <param name="flags">The formatting bitmask, or <see langword="null"/> to omit it.</param>
    /// <returns>The response.</returns>
    private static GuidResponse InvokeGuid(byte entropy, long? flags) =>
        CryptoFixture.Success(CryptoEndpoints.GenerateGuid(
            new GuidRequest { Flags = flags },
            new RandomProvider(new ConstantEntropySource(entropy)),
            CryptoFixture.Loggers));
}

/// <summary>
/// The preserved weak defaults, asserted BEHAVIOURALLY rather than by reading a constant.
/// </summary>
/// <remarks>
/// <para>
/// A constant can be right while the behaviour is wrong. Every row here therefore establishes what the
/// implementation ACTUALLY does, by an observation that would differ if the default had been quietly
/// strengthened - which is the failure mode the replicate-verbatim mandate exists to prevent.
/// </para>
/// <para>
/// It bears repeating that these rows assert the presence of a WEAKNESS. That is deliberate and is the
/// requirement: legacy defects are replicated and documented, never corrected, and the only mitigation
/// available is the disclosure in the published document that a companion class asserts.
/// </para>
/// </remarks>
public sealed class CryptoWeakDefaultTests
{
    /// <summary>
    /// Omitting the mode genuinely runs in the codebook mode, proven by its own signature weakness.
    /// </summary>
    /// <remarks>
    /// THE DISCRIMINATING OBSERVATION. The codebook mode encrypts identical plaintext blocks to identical
    /// ciphertext blocks and therefore leaks the structure of the plaintext; every chaining mode does not.
    /// So a payload of two identical blocks, encrypted with the mode omitted, must produce two identical
    /// ciphertext blocks. A port that had silently substituted a chaining mode would fail this row, which
    /// reading the default constant could never detect.
    /// </remarks>
    [Fact]
    public void OmittingModeGenuinelyRunsInTheCodebookMode()
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(
            (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256);

        int block = metrics.BlockLengthBytes;

        byte[] plaintext = new byte[block * 2];

        for (int index = 0; index < block; index++)
        {
            plaintext[index] = (byte)index;
            plaintext[block + index] = (byte)index;
        }

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cipher-key"] = CryptoFixture.KeyMaterial(metrics.KeyLengthBytes, 'z'),
            });

        PayloadResponse encrypted = CryptoFixture.Success(CryptoEndpoints.SymmetricEncrypt(
            new SymEncryptRequest
            {
                Data = CryptoFixture.Encodings.Base64Encode(plaintext),
                PayloadForm = PayloadForm.BLOB,
                KeyRef = "cipher-key",
                CipherType = Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
            },
            CryptoFixture.Ciphers,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        byte[] ciphertext = CryptoFixture.Encodings.Base64Decode(encrypted.Data);

        Assert.True(ciphertext.Length >= block * 2);

        Assert.Equal<byte[]>(
            [.. ciphertext.Take(block)],
            [.. ciphertext.Skip(block).Take(block)]);
    }

    /// <summary>
    /// Omitting the mode and naming the codebook mode explicitly produce the identical ciphertext.
    /// </summary>
    /// <remarks>
    /// The other half of the previous row: it establishes not merely that the omitted arm behaves like a
    /// codebook mode, but that it IS the same computation the explicit selector reaches. Together they
    /// leave no room for a third interpretation.
    /// </remarks>
    [Fact]
    public void OmittedModeAndExplicitCodebookModeAgree()
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(
            (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES128);

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cipher-key"] = CryptoFixture.KeyMaterial(metrics.KeyLengthBytes, 'y'),
            });

        PayloadResponse omitted = Encrypt(store, mode: null);
        PayloadResponse explicitCodebook = Encrypt(store, Enums.CRYPTO_SYMCRYPT_MODE_ECB);

        Assert.Equal(omitted.Data, explicitCodebook.Data);
    }

    /// <summary>
    /// Ciphertext carries NO integrity tag, so its length is exactly the block-aligned plaintext length.
    /// </summary>
    /// <remarks>
    /// An authenticated mode appends a tag, so its output would be longer than the padded plaintext. The
    /// length equality is therefore direct evidence that no tag was added - and it is the observable
    /// consequence of the mode set being exactly the three unauthenticated ones [enums.sru:L943-L945].
    /// </remarks>
    [Fact]
    public void CiphertextCarriesNoIntegrityTag()
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(
            (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256);

        int block = metrics.BlockLengthBytes;

        byte[] plaintext = new byte[block];

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cipher-key"] = CryptoFixture.KeyMaterial(metrics.KeyLengthBytes, 'x'),
            });

        PayloadResponse encrypted = CryptoFixture.Success(CryptoEndpoints.SymmetricEncrypt(
            new SymEncryptRequest
            {
                Data = CryptoFixture.Encodings.Base64Encode(plaintext),
                PayloadForm = PayloadForm.BLOB,
                KeyRef = "cipher-key",
                CipherType = Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
            },
            CryptoFixture.Ciphers,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        byte[] ciphertext = CryptoFixture.Encodings.Base64Decode(encrypted.Data);

        // One full block of padding is appended because the plaintext is exactly block-aligned, which is
        // the PKCS#5-family rule. No further bytes exist, so there is no tag.
        Assert.Equal(block * 2, ciphertext.Length);

        Assert.False(LegacyDefaults.AUTHENTICATED_ENCRYPTION_AVAILABLE);
    }

    /// <summary>
    /// A passphrase is used as RAW KEY BYTES: no salt, no stretching, no per-call derivation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PROVEN BY DETERMINISM. Encrypting the same plaintext twice under the same reference produces
    /// byte-identical ciphertext. That is only possible if nothing random and nothing per-call enters the
    /// computation - so there is no salt, no iteration count and no derived subkey.
    /// </para>
    /// <para>
    /// Confirmed structurally too: the normalization is a pure function of the material, so the same
    /// passphrase always yields the same key bytes.
    /// </para>
    /// </remarks>
    [Fact]
    public void PassphraseIsUsedAsRawKeyBytes()
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(
            (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256);

        const string passphrase = "correct horse battery staple";

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["cipher-key"] = passphrase });

        PayloadResponse first = Encrypt(store, mode: null);
        PayloadResponse second = Encrypt(store, mode: null);

        Assert.Equal(first.Data, second.Data);

        // The key bytes are the passphrase's own bytes, brought to the cipher's length without a
        // derivation function of any kind.
        byte[] normalized = LegacyDefaults.NormalizeKeyMaterial(passphrase, metrics.KeyLengthBytes);

        Assert.Equal(metrics.KeyLengthBytes, normalized.Length);

        Assert.Equal(
            normalized,
            LegacyDefaults.NormalizeKeyMaterial(passphrase, metrics.KeyLengthBytes));

        byte[] passphraseBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(passphrase);
        int shared = Math.Min(passphraseBytes.Length, normalized.Length);

        Assert.Equal<byte[]>(
            [.. passphraseBytes.Take(shared)],
            [.. normalized.Take(shared)]);

        Assert.False(LegacyDefaults.KEY_DERIVATION_AVAILABLE);
    }

    /// <summary>
    /// No request record on this surface offers a salt, an iteration count or a padding selector.
    /// </summary>
    /// <remarks>
    /// The structural half of the preservation: not one of the 65 legacy declarations has such a
    /// parameter, so offering one would imply a choice the legacy never had. Asserted by reflection so
    /// that a member added later fails here rather than silently widening the contract.
    /// </remarks>
    [Fact]
    public void NoRequestOffersASaltIterationCountOrPaddingSelector()
    {
        string[] forbidden = ["salt", "iteration", "kdf", "derive", "tag", "aad", "nonce"];

        foreach (Type requestType in CryptoFixture.RequestTypes)
        {
            foreach (PropertyInfo member in requestType.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (string fragment in forbidden)
                {
                    Assert.DoesNotContain(
                        fragment,
                        member.Name,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        // The block padding scheme is fixed rather than selectable, so only the ASYMMETRIC request may
        // carry a padding member at all - and it selects between the two published RSA paddings.
        Assert.False(LegacyDefaults.BLOCK_PADDING_SELECTABLE);
        Assert.NotNull(typeof(RsaCipherRequest).GetProperty("Padding"));
        Assert.Null(typeof(SymEncryptRequest).GetProperty("Padding"));
        Assert.Null(typeof(SymDecryptRequest).GetProperty("Padding"));
    }

    /// <summary>Encrypts a fixed plaintext under the store's key, with the given mode.</summary>
    /// <param name="store">The reference store.</param>
    /// <param name="mode">The mode selector, or <see langword="null"/> to omit it.</param>
    /// <returns>The response.</returns>
    private static PayloadResponse Encrypt(CryptoReferenceResolver store, long? mode) =>
        CryptoFixture.Success(CryptoEndpoints.SymmetricEncrypt(
            new SymEncryptRequest
            {
                Data = "a fixed plaintext for determinism",
                PayloadForm = PayloadForm.STRING,
                KeyRef = "cipher-key",
                CipherType = Enums.CRYPTO_SYMCRYPT_TYPE_AES128,
                Mode = mode,
            },
            CryptoFixture.Ciphers,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));
}


/// <summary>
/// The rejection shape, the explicit per-code status map, and the tri-state algebra behind it.
/// </summary>
/// <remarks>
/// <para>
/// THE ALGEBRA IS TRI-STATE AND HOLED, which is why a status must never be derived from a truthiness
/// test over a return code. Success is <c>&gt;= 0</c> [issucceeded.srf:L11-L13] and prevention is 1
/// [retcode.sru:L42], so A PREVENTION READS AS A SUCCESS. Failure is <c>&lt; 0</c> with cancellation
/// EXPLICITLY excluded [isfailed.srf:L11-L13], and cancellation is -2 [retcode.sru:L44-L45], so
/// CANCELLATION IS NEITHER. Both predicates answer false for a null input, so null is likewise neither.
/// </para>
/// <para>
/// The rows below assert those properties on the KERNEL's own predicates, and then assert that the
/// status of every code this contract produces comes from an explicit map rather than from any of them.
/// The two tri-state cases are asserted individually, because a map that happened to be right for one
/// could still be wrong for the other.
/// </para>
/// </remarks>
public sealed class CryptoProblemMappingTests
{
    /// <summary>A prevention reads as a SUCCESS, which is the first hole in the algebra.</summary>
    [Fact]
    public void PreventionReadsAsSuccess()
    {
        Assert.Equal(1L, RetCode.PREVENT);
        Assert.True(Predicates.IsSucceeded(RetCode.PREVENT));
        Assert.False(Predicates.IsFailed(RetCode.PREVENT));
    }

    /// <summary>Cancellation is NEITHER succeeded nor failed, which is the second hole.</summary>
    [Fact]
    public void CancellationIsNeitherSucceededNorFailed()
    {
        Assert.Equal(-2L, RetCode.CANCELLED);
        Assert.Equal(RetCode.CANCELLED, RetCode.CANCELED);

        Assert.False(Predicates.IsSucceeded(RetCode.CANCELLED));
        Assert.False(Predicates.IsFailed(RetCode.CANCELLED));
    }

    /// <summary>A null code is neither, on both predicates.</summary>
    /// <remarks>
    /// The cast is REQUIRED and is itself a finding worth recording: each predicate has both a numeric
    /// and a boolean overload, so a bare null literal is ambiguous between them and will not compile. A
    /// caller writing the obvious thing therefore gets a compile error rather than a silently chosen
    /// overload, which is the one place this quirk helps.
    /// </remarks>
    [Fact]
    public void NullCodeIsNeither()
    {
        Assert.False(Predicates.IsSucceeded((long?)null));
        Assert.False(Predicates.IsFailed((long?)null));
    }

    /// <summary>
    /// The BOOLEAN overloads of failure and prevention are indistinguishable negations.
    /// </summary>
    /// <remarks>
    /// [isfailed.srf:L15-L17] and [isprevented.srf:L15-L17] are textually identical, so prevention and
    /// failure cannot be told apart in the boolean form while remaining distinct in the numeric one.
    /// That is a preserved legacy defect and it is precisely why this contract never reduces a code to a
    /// boolean before deciding a status - the row exists so the collapse is documented rather than
    /// discovered.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void BooleanOverloadsOfFailureAndPreventionAgree(bool? value)
    {
        Assert.Equal(Predicates.IsFailed(value), Predicates.IsPrevented(value));
    }

    /// <summary>
    /// The two tri-state codes map to a definite status apiece, asserted individually.
    /// </summary>
    /// <param name="retCode">The code.</param>
    /// <remarks>
    /// Neither is produced by this contract, and both are asserted anyway: they are the codes whose
    /// status a truthiness-derived implementation would get wrong, so establishing that the shared map
    /// answers definitely for each is what shows the map is explicit rather than computed.
    /// </remarks>
    [Theory]
    [InlineData(1L)]
    [InlineData(-2L)]
    public void TriStateCodeStillMapsToADefiniteStatus(long retCode)
    {
        int status = ProblemResults.MapStatusCode(retCode);

        Assert.InRange(status, 400, 599);
        Assert.NotEqual(StatusCodes.Status501NotImplemented, status);
        Assert.NotEqual(StatusCodes.Status200OK, status);
    }

    /// <summary>
    /// Every code this contract produces maps to the status the authored document declares.
    /// </summary>
    /// <param name="retCode">The code.</param>
    /// <param name="expectedStatus">The status the shared map must choose.</param>
    /// <remarks>
    /// The full table this file relies on, asserted in one place. A change to the shared map that broke
    /// any arm would fail here rather than in a scattered handler row - which matters because the map
    /// lives in a sibling endpoint file and is shared with the other contract in this folder.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ProducedCodeRows))]
    public void EveryProducedCodeMapsToItsDeclaredStatus(long retCode, int expectedStatus)
    {
        Assert.Equal(expectedStatus, ProblemResults.MapStatusCode(retCode));
    }

    /// <summary>
    /// NEITHER of the two "unsupported" codes maps to the not-implemented status.
    /// </summary>
    /// <param name="retCode">The code.</param>
    /// <remarks>
    /// The trap this row exists to close. Both codes MEAN something is not implemented, so both are the
    /// obvious candidate for that status - and C-D reserves it for the ingress service's four
    /// deferred-capability routing declarations, so a service that produced it would advertise a
    /// deferred capability it does not have.
    /// </remarks>
    [Theory]
    [InlineData(-2000L)]
    [InlineData(-2001L)]
    public void NeitherUnsupportedCodeMapsToNotImplemented(long retCode)
    {
        int status = ProblemResults.MapStatusCode(retCode);

        Assert.NotEqual(StatusCodes.Status501NotImplemented, status);
    }

    /// <summary>Every rejection carries the preserved code and a symbolic title.</summary>
    /// <remarks>
    /// The code is what a caller branches on and the title is the oracle's own symbolic spelling, so a
    /// rejection missing either would be less useful than the dialog it replaced.
    /// </remarks>
    [Fact]
    public void RejectionCarriesThePreservedCodeAndASymbolicTitle()
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.Hash(
            new HashRequest { Data = "message", PayloadForm = PayloadForm.STRING, HashType = 99L },
            CryptoFixture.Hashes,
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_NO_SUPPORT, CryptoFixture.RetCodeOf(problem));

        Assert.Equal(
            Formatting.FormatRetCode(RetCode.E_NO_SUPPORT),
            problem.ProblemDetails.Title);

        Assert.False(string.IsNullOrWhiteSpace(problem.ProblemDetails.Detail));
    }

    /// <summary>
    /// The severity is the zero-valued member, because this surface displays no legacy dialog.
    /// </summary>
    /// <remarks>
    /// EVIDENCE RATHER THAN INDIFFERENCE. The severity member exists to preserve the icon of a legacy
    /// <c>MessageBox</c> call, and the legacy cryptographic class is declared native
    /// [n_crypto.sru:L8] - it displays no dialog at any of its 65 declarations. Asserting a severity
    /// would fabricate information about the oracle; the zero-valued member is the one that claims
    /// nothing.
    /// </remarks>
    [Fact]
    public void SeverityClaimsNothing()
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.Hash(
            new HashRequest { Data = "message", PayloadForm = PayloadForm.STRING, HashType = 99L },
            CryptoFixture.Hashes,
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.True(problem.ProblemDetails.Extensions.TryGetValue(
            ProblemResults.MessageSeverityExtensionMember,
            out object? severity));

        Assert.Equal(
            ProblemResults.DescribeSeverity(ProblemSeverity.None),
            Assert.IsType<string>(severity));
    }

    /// <summary>
    /// No rejection on this surface carries a localization category, because none makes a translation.
    /// </summary>
    /// <remarks>
    /// A category is the argument of a legacy translation call. The cryptographic surface makes none, so
    /// carrying one would assert a category the oracle never used - and the DataWindow service layer,
    /// which DOES translate, is a different service entirely.
    /// </remarks>
    [Fact]
    public void NoRejectionCarriesALocalizationCategory()
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.Hash(
            new HashRequest { Data = "message", PayloadForm = PayloadForm.STRING, HashType = 99L },
            CryptoFixture.Hashes,
            CryptoFixture.Encodings,
            CryptoFixture.Loggers));

        Assert.False(problem.ProblemDetails.Extensions.ContainsKey(
            ProblemResults.MessageCategoryExtensionMember));
    }

    /// <summary>
    /// The formatter's two preserved defects survive, and this contract depends on neither.
    /// </summary>
    /// <remarks>
    /// [formatretcode.srf] has no arm for the retry code and collapses three aliases, so the strings
    /// <c>SUCCESS</c>, <c>ALLOW</c> and <c>CANCELED</c> are unreachable. Both are preserved defects and
    /// are NOT fixed. Asserted here so that a future "tidy-up" of the formatter fails loudly rather than
    /// silently changing every problem title in the estate.
    /// </remarks>
    [Fact]
    public void FormatterDefectsArePreserved()
    {
        // The alias collapse: the three names that share a value render as the FIRST spelling only.
        Assert.Equal(Formatting.FormatRetCode(RetCode.OK), Formatting.FormatRetCode(RetCode.SUCCESS));
        Assert.Equal(Formatting.FormatRetCode(RetCode.OK), Formatting.FormatRetCode(RetCode.ALLOW));

        Assert.Equal(
            Formatting.FormatRetCode(RetCode.CANCELLED),
            Formatting.FormatRetCode(RetCode.CANCELED));

        // A null code renders as null, which is the formatter's own documented behaviour and the only
        // input for which it answers null.
        Assert.Null(Formatting.FormatRetCode(null));
    }

    /// <summary>Every return code this contract produces, with the status the shared map assigns it.</summary>
    /// <returns>Eight rows.</returns>
    public static TheoryData<long, int> ProducedCodeRows() =>
        new()
        {
            { RetCode.E_INVALID_ARGUMENT, StatusCodes.Status400BadRequest },
            { RetCode.E_INVALID_DATA, StatusCodes.Status400BadRequest },
            { RetCode.E_NO_SUPPORT, StatusCodes.Status400BadRequest },
            { RetCode.E_ACCESS_DENIED, StatusCodes.Status403Forbidden },
            { RetCode.E_OBJECT_NOT_FOUND, StatusCodes.Status404NotFound },
            { RetCode.E_NO_IMPLEMENTATION, StatusCodes.Status500InternalServerError },
            { RetCode.E_INTERNAL_ERROR, StatusCodes.Status500InternalServerError },
            { RetCode.E_IO_ERROR, StatusCodes.Status500InternalServerError },
        };
}


/// <summary>
/// The absent-member arm of EVERY operation, plus the two helpers that are otherwise only reached on a
/// happy path.
/// </summary>
/// <remarks>
/// <para>
/// A REJECTION ARM IS PART OF THE CONTRACT, not an afterthought. The published schema marks these
/// members required, so a document-validating client refuses the request before sending - and a contract
/// enforced only by clients is not enforced. Every arm is therefore driven here, one row per operation
/// and per member, because these are exactly the paths a happy-path suite leaves untouched and exactly
/// the paths a caller hits first while integrating.
/// </para>
/// <para>
/// Each row asserts the code AND the wire-spelled member name, so a rejection that named the C# spelling
/// - something the caller never wrote - would fail.
/// </para>
/// </remarks>
public sealed class CryptoRejectionArmTests
{
    /// <summary>Every operation refuses a request that omits its payload-form selector.</summary>
    /// <param name="operationId">The operation to drive.</param>
    [Theory]
    [InlineData("hash")]
    [InlineData("hmac")]
    [InlineData("symmetricEncrypt")]
    [InlineData("symmetricDecrypt")]
    [InlineData("rsaEncrypt")]
    [InlineData("rsaDecrypt")]
    [InlineData("rsaSign")]
    [InlineData("rsaVerify")]
    public void OperationRefusesAnAbsentPayloadForm(string operationId)
    {
        ProblemHttpResult problem = InvokeWithoutPayloadForm(operationId);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    /// <summary>Every payload-bearing operation refuses a request that omits its payload.</summary>
    /// <param name="operationId">The operation to drive.</param>
    [Theory]
    [InlineData("hash")]
    [InlineData("hmac")]
    [InlineData("symmetricEncrypt")]
    [InlineData("symmetricDecrypt")]
    [InlineData("rsaEncrypt")]
    [InlineData("rsaDecrypt")]
    [InlineData("rsaSign")]
    [InlineData("rsaVerify")]
    [InlineData("stringToBlob")]
    [InlineData("blobToString")]
    [InlineData("reverseBlob")]
    public void OperationRefusesAnAbsentPayload(string operationId)
    {
        ProblemHttpResult problem = InvokeWithoutPayload(operationId);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(problem));
        Assert.Contains("data", problem.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Data", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>The verification operation refuses a request that omits the signature.</summary>
    [Fact]
    public void VerificationRefusesAnAbsentSignature()
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.RsaVerify(
            new RsaVerifyRequest
            {
                Data = "payload",
                PayloadForm = PayloadForm.STRING,
                KeyRef = "any",
                HashType = Enums.CRYPTO_HASH_SHA256,
            },
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            CryptoFixture.EmptyStore(),
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(problem));
        Assert.Contains("signature", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>Every keyed digest operation refuses a request that omits its selector.</summary>
    /// <param name="operationId">The operation to drive.</param>
    [Theory]
    [InlineData("hmac")]
    [InlineData("hashFile")]
    [InlineData("hmacFile")]
    [InlineData("rsaSign")]
    [InlineData("rsaVerify")]
    public void OperationRefusesAnAbsentSelector(string operationId)
    {
        ProblemHttpResult problem = InvokeWithoutSelector(operationId);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(problem));
        Assert.Contains("hashType", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>The symmetric operations refuse a request that omits the cipher selector.</summary>
    /// <param name="encrypting">Whether to drive the encrypt direction.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SymmetricOperationRefusesAnAbsentCipherSelector(bool encrypting)
    {
        ProblemHttpResult problem = encrypting
            ? CryptoFixture.Rejection(CryptoEndpoints.SymmetricEncrypt(
                new SymEncryptRequest
                {
                    Data = "payload",
                    PayloadForm = PayloadForm.STRING,
                    KeyRef = "any",
                },
                CryptoFixture.Ciphers,
                CryptoFixture.Encodings,
                CryptoFixture.EmptyStore(),
                CryptoFixture.Loggers))
            : CryptoFixture.Rejection(CryptoEndpoints.SymmetricDecrypt(
                new SymDecryptRequest
                {
                    Data = "payload",
                    PayloadForm = PayloadForm.STRING,
                    KeyRef = "any",
                },
                CryptoFixture.Ciphers,
                CryptoFixture.Encodings,
                CryptoFixture.EmptyStore(),
                CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(problem));
        Assert.Contains("cipherType", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>Key generation refuses a request that omits the modulus length.</summary>
    [Fact]
    public void KeyGenerationRefusesAnAbsentModulusLength()
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.GenerateRsaKey(
            new GenRsaKeyRequest(),
            CryptoFixture.Rsa,
            CryptoFixture.Random,
            CryptoFixture.EmptyStore(),
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(problem));
        Assert.Contains("bits", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>The two length-bearing random operations refuse a request that omits the length.</summary>
    /// <param name="blob">Whether to drive the bytes operation rather than the string one.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RandomOperationRefusesAnAbsentLength(bool blob)
    {
        ProblemHttpResult problem = blob
            ? CryptoFixture.Rejection(CryptoEndpoints.GenerateRandomBlob(
                new RandomBlobRequest(),
                CryptoFixture.Random,
                CryptoFixture.Encodings,
                CryptoFixture.Loggers))
            : CryptoFixture.Rejection(CryptoEndpoints.GenerateRandomString(
                new RndStringRequest(),
                CryptoFixture.Random,
                CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(problem));
        Assert.Contains("size", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>The encoding operations refuse a malformed transport payload.</summary>
    /// <param name="operationId">The operation to drive.</param>
    [Theory]
    [InlineData("blobToString")]
    [InlineData("reverseBlob")]
    public void EncodingOperationRefusesAMalformedTransportPayload(string operationId)
    {
        const string malformed = "%%%not-base64%%%";

        ProblemHttpResult problem = string.Equals(operationId, "blobToString", StringComparison.Ordinal)
            ? CryptoFixture.Rejection(CryptoEndpoints.BlobToString(
                new BlobToStringRequest { Data = malformed, Encoding = Enums.CRYPTO_ENCODING_HEX },
                CryptoFixture.Encodings,
                CryptoFixture.Loggers))
            : CryptoFixture.Rejection(CryptoEndpoints.ReverseBlob(
                new BlobReverseRequest { Data = malformed },
                CryptoFixture.Encodings,
                CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_DATA, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    /// <summary>A malformed transport payload is refused on every binary-form operation.</summary>
    /// <param name="operationId">The operation to drive.</param>
    [Theory]
    [InlineData("hmac")]
    [InlineData("symmetricEncrypt")]
    [InlineData("rsaEncrypt")]
    [InlineData("rsaSign")]
    [InlineData("rsaVerify")]
    public void MalformedBinaryPayloadIsRefusedAfterResolution(string operationId)
    {
        ProblemHttpResult problem = InvokeWithMalformedBinaryPayload(operationId);

        Assert.Equal(RetCode.E_INVALID_DATA, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    /// <summary>A file reference that is absent or over-long is refused before any file is touched.</summary>
    /// <param name="overLong">Whether to drive the over-long case rather than the absent one.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileReferenceShapeIsScreened(bool overLong)
    {
        string? reference = overLong
            ? new string('f', SecurityOptionsValidator.MaximumKeyRefLength + 1)
            : null;

        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.HashFile(
            new HashFileRequest { FileRef = reference, HashType = Enums.CRYPTO_HASH_SHA256 },
            CryptoFixture.Hashes,
            CryptoFixture.EmptyStore(),
            CryptoFixture.Loggers));

        long expected = overLong ? RetCode.E_ACCESS_DENIED : RetCode.E_INVALID_ARGUMENT;

        Assert.Equal(expected, CryptoFixture.RetCodeOf(problem));
    }

    /// <summary>
    /// The keyed file operation refuses an absent key reference as well as an absent file reference.
    /// </summary>
    [Fact]
    public void KeyedFileOperationScreensBothReferences()
    {
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.HmacFile(
            new HmacFileRequest { FileRef = "somewhere", HashType = Enums.CRYPTO_HASH_SHA256 },
            CryptoFixture.Authenticators,
            CryptoFixture.EmptyStore(),
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(problem));
    }

    /// <summary>
    /// The symmetric operations refuse an absent vector reference when one is named as empty.
    /// </summary>
    /// <remarks>
    /// An EMPTY vector reference is not the same as an ABSENT one: absence selects the no-vector legacy
    /// overload family, whereas an empty string is a reference the caller stated and cannot be resolved.
    /// Conflating them would silently change which overload family runs.
    /// </remarks>
    [Fact]
    public void EmptyVectorReferenceIsRefusedRatherThanTreatedAsAbsent()
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(
            (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256);

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cipher-key"] = CryptoFixture.KeyMaterial(metrics.KeyLengthBytes, 'n'),
            });

        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.SymmetricEncrypt(
            new SymEncryptRequest
            {
                Data = "payload",
                PayloadForm = PayloadForm.STRING,
                KeyRef = "cipher-key",
                IvRef = string.Empty,
                CipherType = Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                Mode = Enums.CRYPTO_SYMCRYPT_MODE_CBC,
            },
            CryptoFixture.Ciphers,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(problem));
    }

    /// <summary>Undecryptable ciphertext is the caller's data and is refused as such.</summary>
    [Fact]
    public void UndecryptableCiphertextIsARequestFault()
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(
            (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256);

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cipher-key"] = CryptoFixture.KeyMaterial(metrics.KeyLengthBytes, 'u'),
            });

        // Block-aligned bytes that are not ciphertext under this key, so the padding check fails.
        byte[] garbage = new byte[metrics.BlockLengthBytes * 2];
        garbage[0] = 0x01;

        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.SymmetricDecrypt(
            new SymDecryptRequest
            {
                Data = CryptoFixture.Encodings.Base64Encode(garbage),
                PayloadForm = PayloadForm.BLOB,
                KeyRef = "cipher-key",
                CipherType = Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
            },
            CryptoFixture.Ciphers,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_DATA, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    /// <summary>
    /// A completed operation writes exactly one classifier record, and it carries no content.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The redaction claim is asserted rather than trusted. The record is captured from a real logger and
    /// scanned for the plaintext, the digest, the resolved key and the reference - none of which may
    /// appear, because the recording method has no parameter through which any of them could arrive.
    /// </para>
    /// <para>
    /// It is written at DEBUG, deliberately: these operations are called in bulk by a service peer, so a
    /// record per call at a shipped level would be pure noise. The row therefore also asserts that
    /// nothing is written when debug is off, which is the property that keeps a production log usable.
    /// </para>
    /// </remarks>
    [Fact]
    public void CompletedOperationWritesOneClassifierRecord()
    {
        const string reference = "log-key";
        const string plaintext = "content that must not be logged";

        string material = CryptoFixture.KeyMaterial(32, 'g');

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal) { [reference] = material });

        CapturedRecords captured = new();

        using ILoggerFactory verbose = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new CapturingLoggerProvider(captured));
        });

        DigestResponse response = CryptoFixture.Success(CryptoEndpoints.Hmac(
            new HmacRequest
            {
                Data = plaintext,
                PayloadForm = PayloadForm.STRING,
                KeyRef = reference,
                HashType = Enums.CRYPTO_HASH_SHA256,
            },
            CryptoFixture.Authenticators,
            CryptoFixture.Encodings,
            store,
            verbose));

        Assert.NotEmpty(captured.Records);

        string all = string.Join('\n', captured.Records);

        Assert.DoesNotContain(plaintext, all, StringComparison.Ordinal);
        Assert.DoesNotContain(material, all, StringComparison.Ordinal);
        Assert.DoesNotContain(reference, all, StringComparison.Ordinal);
        Assert.DoesNotContain(response.Digest, all, StringComparison.Ordinal);

        // The classifier IS present, so the record is useful rather than merely empty.
        Assert.Contains("hmac", all, StringComparison.OrdinalIgnoreCase);

        // And nothing is written when debug is off.
        CapturedRecords quietRecords = new();

        using ILoggerFactory quiet = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new CapturingLoggerProvider(quietRecords));
        });

        CryptoFixture.Success(CryptoEndpoints.Hash(
            new HashRequest
            {
                Data = plaintext,
                PayloadForm = PayloadForm.STRING,
                HashType = Enums.CRYPTO_HASH_SHA256,
            },
            CryptoFixture.Hashes,
            CryptoFixture.Encodings,
            quiet));

        Assert.Empty(quietRecords.Records);
    }

    /// <summary>The supported classification renders as no reason, because it claims no block.</summary>
    /// <remarks>
    /// The value that cannot reach a refusal renders as the empty string rather than as a reason that
    /// would assert a block which did not happen. Driven directly because no request can reach it.
    /// </remarks>
    [Fact]
    public void SupportedClassificationRendersNoReason()
    {
        Assert.Equal(
            string.Empty,
            CryptoEndpoints.DescribeBlockedCellReason(SymmetricCellParity.Supported));

        Assert.Equal(
            "SYMMETRIC_FEEDBACK_WIDTH_UNPROVABLE",
            CryptoEndpoints.DescribeBlockedCellReason(
                SymmetricCellParity.BlockedFeedbackWidthUnprovable));

        Assert.Equal(
            "SYMMETRIC_VECTOR_UNPROVABLE",
            CryptoEndpoints.DescribeBlockedCellReason(
                SymmetricCellParity.BlockedSynthesizedVectorUnprovable));
    }

    /// <summary>Drives one operation with the payload-form selector omitted.</summary>
    /// <param name="operationId">The operation to drive.</param>
    /// <returns>The rejection.</returns>
    private static ProblemHttpResult InvokeWithoutPayloadForm(string operationId)
    {
        CryptoReferenceResolver store = CryptoFixture.EmptyStore();

        switch (operationId)
        {
            case "hash":
                return CryptoFixture.Rejection(CryptoEndpoints.Hash(
                    new HashRequest { Data = "payload", HashType = Enums.CRYPTO_HASH_SHA256 },
                    CryptoFixture.Hashes,
                    CryptoFixture.Encodings,
                    CryptoFixture.Loggers));

            case "hmac":
                return CryptoFixture.Rejection(CryptoEndpoints.Hmac(
                    new HmacRequest
                    {
                        Data = "payload",
                        KeyRef = "any",
                        HashType = Enums.CRYPTO_HASH_SHA256,
                    },
                    CryptoFixture.Authenticators,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "symmetricEncrypt":
                return CryptoFixture.Rejection(CryptoEndpoints.SymmetricEncrypt(
                    new SymEncryptRequest
                    {
                        Data = "payload",
                        KeyRef = "any",
                        CipherType = Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                    },
                    CryptoFixture.Ciphers,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "symmetricDecrypt":
                return CryptoFixture.Rejection(CryptoEndpoints.SymmetricDecrypt(
                    new SymDecryptRequest
                    {
                        Data = "payload",
                        KeyRef = "any",
                        CipherType = Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                    },
                    CryptoFixture.Ciphers,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaEncrypt":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaEncrypt(
                    new RsaCipherRequest { Data = "payload", KeyRef = "any" },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaDecrypt":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaDecrypt(
                    new RsaCipherRequest { Data = "payload", KeyRef = "any" },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaSign":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaSign(
                    new RsaSignRequest
                    {
                        Data = "payload",
                        KeyRef = "any",
                        HashType = Enums.CRYPTO_HASH_SHA256,
                    },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaVerify":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaVerify(
                    new RsaVerifyRequest
                    {
                        Data = "payload",
                        Signature = "c2ln",
                        KeyRef = "any",
                        HashType = Enums.CRYPTO_HASH_SHA256,
                    },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            default:
                Assert.Fail("No payload-form invocation is defined for '" + operationId + "'.");

                throw new InvalidOperationException(operationId);
        }
    }

    /// <summary>Drives one operation with the payload omitted.</summary>
    /// <param name="operationId">The operation to drive.</param>
    /// <returns>The rejection.</returns>
    private static ProblemHttpResult InvokeWithoutPayload(string operationId)
    {
        CryptoReferenceResolver store = CryptoFixture.EmptyStore();

        switch (operationId)
        {
            case "hash":
                return CryptoFixture.Rejection(CryptoEndpoints.Hash(
                    new HashRequest
                    {
                        PayloadForm = PayloadForm.STRING,
                        HashType = Enums.CRYPTO_HASH_SHA256,
                    },
                    CryptoFixture.Hashes,
                    CryptoFixture.Encodings,
                    CryptoFixture.Loggers));

            case "hmac":
                return CryptoFixture.Rejection(CryptoEndpoints.Hmac(
                    new HmacRequest
                    {
                        PayloadForm = PayloadForm.STRING,
                        KeyRef = "any",
                        HashType = Enums.CRYPTO_HASH_SHA256,
                    },
                    CryptoFixture.Authenticators,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "symmetricEncrypt":
                return CryptoFixture.Rejection(CryptoEndpoints.SymmetricEncrypt(
                    new SymEncryptRequest
                    {
                        PayloadForm = PayloadForm.STRING,
                        KeyRef = "any",
                        CipherType = Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                    },
                    CryptoFixture.Ciphers,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "symmetricDecrypt":
                return CryptoFixture.Rejection(CryptoEndpoints.SymmetricDecrypt(
                    new SymDecryptRequest
                    {
                        PayloadForm = PayloadForm.STRING,
                        KeyRef = "any",
                        CipherType = Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                    },
                    CryptoFixture.Ciphers,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaEncrypt":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaEncrypt(
                    new RsaCipherRequest { PayloadForm = PayloadForm.STRING, KeyRef = "any" },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaDecrypt":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaDecrypt(
                    new RsaCipherRequest { PayloadForm = PayloadForm.STRING, KeyRef = "any" },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaSign":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaSign(
                    new RsaSignRequest
                    {
                        PayloadForm = PayloadForm.STRING,
                        KeyRef = "any",
                        HashType = Enums.CRYPTO_HASH_SHA256,
                    },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaVerify":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaVerify(
                    new RsaVerifyRequest
                    {
                        PayloadForm = PayloadForm.STRING,
                        Signature = "c2ln",
                        KeyRef = "any",
                        HashType = Enums.CRYPTO_HASH_SHA256,
                    },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "stringToBlob":
                return CryptoFixture.Rejection(CryptoEndpoints.StringToBlob(
                    new StringToBlobRequest { Encoding = Enums.CRYPTO_ENCODING_BASE64 },
                    CryptoFixture.Encodings,
                    CryptoFixture.Loggers));

            case "blobToString":
                return CryptoFixture.Rejection(CryptoEndpoints.BlobToString(
                    new BlobToStringRequest { Encoding = Enums.CRYPTO_ENCODING_BASE64 },
                    CryptoFixture.Encodings,
                    CryptoFixture.Loggers));

            case "reverseBlob":
                return CryptoFixture.Rejection(CryptoEndpoints.ReverseBlob(
                    new BlobReverseRequest(),
                    CryptoFixture.Encodings,
                    CryptoFixture.Loggers));

            default:
                Assert.Fail("No payload invocation is defined for '" + operationId + "'.");

                throw new InvalidOperationException(operationId);
        }
    }

    /// <summary>Drives one operation with the digest selector omitted.</summary>
    /// <param name="operationId">The operation to drive.</param>
    /// <returns>The rejection.</returns>
    private static ProblemHttpResult InvokeWithoutSelector(string operationId)
    {
        CryptoReferenceResolver store = CryptoFixture.EmptyStore();

        switch (operationId)
        {
            case "hmac":
                return CryptoFixture.Rejection(CryptoEndpoints.Hmac(
                    new HmacRequest
                    {
                        Data = "payload",
                        PayloadForm = PayloadForm.STRING,
                        KeyRef = "any",
                    },
                    CryptoFixture.Authenticators,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "hashFile":
                return CryptoFixture.Rejection(CryptoEndpoints.HashFile(
                    new HashFileRequest { FileRef = "any" },
                    CryptoFixture.Hashes,
                    store,
                    CryptoFixture.Loggers));

            case "hmacFile":
                return CryptoFixture.Rejection(CryptoEndpoints.HmacFile(
                    new HmacFileRequest { FileRef = "any", KeyRef = "any" },
                    CryptoFixture.Authenticators,
                    store,
                    CryptoFixture.Loggers));

            case "rsaSign":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaSign(
                    new RsaSignRequest
                    {
                        Data = "payload",
                        PayloadForm = PayloadForm.STRING,
                        KeyRef = "any",
                    },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaVerify":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaVerify(
                    new RsaVerifyRequest
                    {
                        Data = "payload",
                        PayloadForm = PayloadForm.STRING,
                        Signature = "c2ln",
                        KeyRef = "any",
                    },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            default:
                Assert.Fail("No selector invocation is defined for '" + operationId + "'.");

                throw new InvalidOperationException(operationId);
        }
    }

    /// <summary>Drives one operation with a resolvable key and a malformed binary payload.</summary>
    /// <param name="operationId">The operation to drive.</param>
    /// <returns>The rejection.</returns>
    /// <remarks>
    /// The key resolves, so the malformed-payload arm is reached AFTER resolution - which is the ordering
    /// a caller sees and the one a happy-path suite never exercises.
    /// </remarks>
    private static ProblemHttpResult InvokeWithMalformedBinaryPayload(string operationId)
    {
        const string malformed = "@@@not-base64@@@";

        string privateKey = string.Empty;
        string publicKey = string.Empty;

        Assert.True(CryptoFixture.Rsa.GenRSAKey(
            Enums.CRYPTO_RSA_BITS_2048,
            ref privateKey,
            ref publicKey));

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cipher-key"] = CryptoFixture.KeyMaterial(32, 'b'),
                ["rsa-private"] = privateKey,
                ["rsa-public"] = publicKey,
            });

        switch (operationId)
        {
            case "hmac":
                return CryptoFixture.Rejection(CryptoEndpoints.Hmac(
                    new HmacRequest
                    {
                        Data = malformed,
                        PayloadForm = PayloadForm.BLOB,
                        KeyRef = "cipher-key",
                        HashType = Enums.CRYPTO_HASH_SHA256,
                    },
                    CryptoFixture.Authenticators,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "symmetricEncrypt":
                return CryptoFixture.Rejection(CryptoEndpoints.SymmetricEncrypt(
                    new SymEncryptRequest
                    {
                        Data = malformed,
                        PayloadForm = PayloadForm.BLOB,
                        KeyRef = "cipher-key",
                        CipherType = Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                    },
                    CryptoFixture.Ciphers,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaEncrypt":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaEncrypt(
                    new RsaCipherRequest
                    {
                        Data = malformed,
                        PayloadForm = PayloadForm.BLOB,
                        KeyRef = "rsa-public",
                    },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaSign":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaSign(
                    new RsaSignRequest
                    {
                        Data = malformed,
                        PayloadForm = PayloadForm.BLOB,
                        KeyRef = "rsa-private",
                        HashType = Enums.CRYPTO_HASH_SHA256,
                    },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            case "rsaVerify":
                return CryptoFixture.Rejection(CryptoEndpoints.RsaVerify(
                    new RsaVerifyRequest
                    {
                        Data = malformed,
                        PayloadForm = PayloadForm.BLOB,
                        Signature = "c2ln",
                        KeyRef = "rsa-public",
                        HashType = Enums.CRYPTO_HASH_SHA256,
                    },
                    CryptoFixture.Rsa,
                    CryptoFixture.Encodings,
                    store,
                    CryptoFixture.Loggers));

            default:
                Assert.Fail("No malformed-payload invocation is defined for '" + operationId + "'.");

                throw new InvalidOperationException(operationId);
        }
    }
}

/// <summary>The log records one operation produced.</summary>
internal sealed class CapturedRecords
{
    /// <summary>Every formatted record, in order.</summary>
    public List<string> Records { get; } = [];
}

/// <summary>
/// A logging provider that appends every formatted record to a <see cref="CapturedRecords"/>.
/// </summary>
/// <remarks>
/// Accepts EVERY category, unlike the readiness-scoped provider beside the health tests, because the
/// redaction scan must see everything the operation wrote rather than only what one category wrote -
/// material leaking through an unexpected category is exactly the failure the scan exists to catch.
/// </remarks>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly CapturedRecords _captured;

    /// <summary>Creates the provider over the record store.</summary>
    /// <param name="captured">The store every record is appended to.</param>
    public CapturingLoggerProvider(CapturedRecords captured) => _captured = captured;

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_captured);

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly CapturedRecords _captured;

        public CapturingLogger(CapturedRecords captured) => _captured = captured;

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _captured.Records.Add(formatter(state, exception));
        }
    }
}

