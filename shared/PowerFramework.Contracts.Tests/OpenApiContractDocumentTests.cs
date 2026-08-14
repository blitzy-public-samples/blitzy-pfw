// ==================================================================================================
//  OpenApiContractDocumentTests - the two published OpenAPI documents, as ARTIFACTS
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   OpenApi/security.v1.yaml  (C-01 TokenService, C-02 CryptoService)
//            OpenApi/gateway.v1.yaml   (C-09 REST ingress, C-10 readiness)
//
//  WHAT THIS FILE OWNS, AND WHY IT IS SEPARATE FROM GatewayContractTests
//  ------------------------------------------------------------------------------------------------
//  This file asserts the properties both documents must have as PUBLISHED ARTIFACTS: that they are
//  embedded in the assembly at all, that they parse as the OpenAPI version they claim, that they
//  parse CLEANLY, and that they agree on the conventions a consumer relies on across both - one error
//  shape, one bearer scheme shape, an authenticated-by-default posture.
//
//  GatewayContractTests owns what is specific to C-09 and C-10: the route inventory, the status
//  mapping, the reserved routes and the projection's correspondence to the protocol definitions.
//
//  WHY EMBEDDING IS ASSERTED AT ALL - IT IS THE FAILURE THAT ACTUALLY HAPPENED
//  ------------------------------------------------------------------------------------------------
//  PowerFramework.Contracts.csproj globs `OpenApi/**/*.yaml` as EmbeddedResource. A glob silently
//  matches nothing when nothing is there: for a period this project declared and shipped an OpenApi
//  directory containing only security.v1.yaml while docs/CONTRACTS.md documented C-09 and C-10 as
//  published contracts, so the gateway contract was documented and absent simultaneously and the
//  build was green throughout. Nothing detected it because a glob that matches one file looks exactly
//  like a glob that matches two.
//
//  The manifest assertions below are the guard against that recurring. They name the expected logical
//  resource names EXACTLY, and they assert the COUNT, so a document that is deleted, renamed, moved
//  out of the globbed directory, or has its build action changed fails here rather than being noticed
//  by a consumer.
// ==================================================================================================

using System.Reflection;
using System.Text;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using PowerFramework.Contracts.Common.V1;
using Xunit;

namespace PowerFramework.Contracts.Tests;

public sealed class OpenApiContractDocumentTests
{
    // ==============================================================================================
    //  THE TWO EXPECTED LOGICAL RESOURCE NAMES
    //
    //  The SDK derives a logical resource name from the root namespace plus the file's path with
    //  directory separators replaced by dots. `OpenApi/gateway.v1.yaml` in a project whose root
    //  namespace is `PowerFramework.Contracts` therefore becomes
    //  `PowerFramework.Contracts.OpenApi.gateway.v1.yaml`.
    //
    //  These are written out in full rather than composed from parts, deliberately: a consumer that
    //  reads a document by name hardcodes exactly this string, so the test should break if the string
    //  changes for ANY reason - including a root-namespace change, which composing from parts would
    //  hide.
    // ==============================================================================================

    private const string GatewayResourceName = "PowerFramework.Contracts.OpenApi.gateway.v1.yaml";
    private const string SecurityResourceName = "PowerFramework.Contracts.OpenApi.security.v1.yaml";

    /// <summary>
    /// The assembly carrying the contracts. Reached through a generated type rather than through
    /// <c>Assembly.Load</c> by name, so a rename breaks the build instead of the test at runtime.
    /// </summary>
    private static Assembly ContractsAssembly => typeof(RetCode).Assembly;

    private static string[] YamlResourceNames =>
        ContractsAssembly.GetManifestResourceNames()
            .Where(static name => name.EndsWith(".yaml", StringComparison.Ordinal))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Reads an embedded document's text. Fails with the available names listed when the requested
    /// name is absent, because "resource not found" without the actual manifest is the least useful
    /// possible failure message for exactly the mistake this is guarding.
    /// </summary>
    internal static string ReadEmbeddedDocument(string resourceName)
    {
        using Stream? stream = ContractsAssembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();
        Assert.False(
            string.IsNullOrWhiteSpace(text),
            $"Embedded resource '{resourceName}' is present but empty.");
        return text;
    }

    /// <summary>
    /// Parses an embedded document with the YAML reader registered, and asserts it parsed CLEANLY.
    /// </summary>
    /// <remarks>
    /// The YAML reader is an explicit registration rather than a default: Microsoft.OpenApi reads JSON
    /// out of the box and needs <c>AddYamlReader</c> from the separate YamlReader package to read
    /// YAML. That package is referenced by this test project alone - the Contracts project does not
    /// need it, because it only EMBEDS the documents - and the reason is recorded in the test
    /// project's own file.
    /// </remarks>
    internal static OpenApiDocument ParseEmbeddedDocument(string resourceName)
    {
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        ReadResult result = OpenApiModelFactory.Parse(
            ReadEmbeddedDocument(resourceName),
            format: "yaml",
            settings: settings);

        Assert.NotNull(result.Diagnostic);

        // ERRORS AND WARNINGS ARE BOTH ASSERTED AT ZERO.
        //
        // Warnings are not cosmetic in an OpenAPI diagnostic: an unresolved $ref, a $ref pointing at a
        // component that does not exist, and a construct the declared specification version does not
        // support all surface as warnings rather than errors. A document with warnings still parses,
        // and a consumer's generator will then either skip the affected operation or emit something
        // wrong - which is precisely the class of defect a published contract must not ship with.
        Assert.Empty(result.Diagnostic.Errors);
        Assert.Empty(result.Diagnostic.Warnings);

        Assert.NotNull(result.Document);
        return result.Document;
    }

    // ==============================================================================================
    //  MANIFEST - THE GUARD AGAINST A DECLARED-BUT-ABSENT DOCUMENT
    // ==============================================================================================

    [Fact]
    public void BothContractDocumentsAreEmbeddedUnderTheirExactLogicalNames()
    {
        string[] actual = YamlResourceNames;

        Assert.Equal(
            [GatewayResourceName, SecurityResourceName],
            actual);
    }

    [Fact]
    public void ExactlyTwoOpenApiDocumentsAreEmbedded()
    {
        // THE COUNT IS ASSERTED, NOT JUST THE MEMBERSHIP.
        //
        // Asserting only that the two expected names are present would let a third document be added
        // to the globbed directory and ship undocumented. Two is the number docs/CONTRACTS.md
        // publishes as REST contracts - C-01/C-02 on Security and C-09/C-10 on Gateway - and the other
        // eight contracts are gRPC and live in the protocol definitions instead. A third YAML here
        // means either a new REST contract that the documentation does not mention, or a stray file.
        Assert.Equal(2, YamlResourceNames.Length);
    }

    [Fact]
    public void NoDocumentIsEmbeddedForAnyDeferredService()
    {
        // C-D COMPLIANCE, ASSERTED RATHER THAN ASSUMED.
        //
        // The four deferred services receive no code, no test, no container and no contract. A
        // published OpenAPI document for one of them would be a partial implementation of exactly the
        // kind the requirements forbid - a consumer could generate a client against it. The four
        // reserved Gateway ROUTES are the only permitted representation, and they live inside
        // gateway.v1.yaml rather than in documents of their own.
        string[] forbidden = ["design", "documents", "integration", "scripting", "designsystem", "scriptbridge"];

        foreach (string name in ContractsAssembly.GetManifestResourceNames())
        {
            foreach (string token in forbidden)
            {
                Assert.False(
                    name.Contains(token, StringComparison.OrdinalIgnoreCase),
                    $"Resource '{name}' names deferred capability '{token}'. The four deferred services "
                        + "must have no contract document of their own; the reserved routes inside "
                        + "gateway.v1.yaml are their only permitted representation.");
            }
        }
    }

    // ==============================================================================================
    //  PARSE FIDELITY
    // ==============================================================================================

    [Theory]
    [InlineData(GatewayResourceName)]
    [InlineData(SecurityResourceName)]
    public void EachDocumentParsesAsOpenApiThreeOneWithNoErrorsAndNoWarnings(string resourceName)
    {
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        ReadResult result = OpenApiModelFactory.Parse(
            ReadEmbeddedDocument(resourceName),
            format: "yaml",
            settings: settings);

        Assert.NotNull(result.Diagnostic);

        // THE VERSION IS ASSERTED, NOT INFERRED FROM THE `openapi:` STRING.
        //
        // This is what the reader RESOLVED, which is the value that governs how every construct in the
        // document is interpreted. A document declaring 3.1.0 while using a 3.0-only construct would
        // still resolve as 3.1 here and produce a warning - which the assertions below catch - so the
        // two checks together are what establish that the declared version is the one actually honoured.
        Assert.Equal(OpenApiSpecVersion.OpenApi3_1, result.Diagnostic.SpecificationVersion);

        Assert.Empty(result.Diagnostic.Errors);
        Assert.Empty(result.Diagnostic.Warnings);
    }

    [Theory]
    [InlineData(GatewayResourceName)]
    [InlineData(SecurityResourceName)]
    public void NeitherDocumentUsesTheThreeZeroOnlyNullableKeyword(string resourceName)
    {
        // `nullable` WAS REMOVED IN OPENAPI 3.1.
        //
        // 3.1 aligned with JSON Schema, which expresses an optional null as a type ARRAY - `type: [string,
        // 'null']` - and dropped the 3.0 `nullable: true` sibling keyword. A 3.1 document carrying
        // `nullable` is not invalid YAML and many readers ignore it silently, so a schema meaning
        // "may be null" would quietly become "must be a string" for every consumer.
        //
        // Both documents state in their own comments that no 3.0-only construct is used. This is that
        // claim made executable, which is the difference between a comment and a guarantee.
        string text = ReadEmbeddedDocument(resourceName);

        Assert.DoesNotContain("nullable:", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GatewayResourceName)]
    [InlineData(SecurityResourceName)]
    public void EachDocumentDeclaresATitleAVersionAndTheProjectLicence(string resourceName)
    {
        OpenApiDocument document = ParseEmbeddedDocument(resourceName);

        Assert.NotNull(document.Info);
        Assert.False(string.IsNullOrWhiteSpace(document.Info.Title));
        Assert.False(string.IsNullOrWhiteSpace(document.Info.Version));

        // THE LICENCE NAME IS THE PROJECT'S, AND ONLY THE NAME APPEARS HERE.
        //
        // PowerFramework is BSD 2-Clause with a copyright spanning 2013-2022, PLUS two additional
        // conditions from the Chinese restatement in the project readme, PLUS eleven upstream
        // attributions. None of that belongs inline in a wire contract, so the documents carry the
        // identifier and the repository-root NOTICE and LICENSE files carry the text.
        Assert.NotNull(document.Info.License);
        Assert.Equal("BSD-2-Clause", document.Info.License.Name);
    }

    [Theory]
    [InlineData(GatewayResourceName)]
    [InlineData(SecurityResourceName)]
    public void EachDocumentDeclaresExactlyOneServerAndItIsItsOwnListener(string resourceName)
    {
        OpenApiDocument document = ParseEmbeddedDocument(resourceName);

        Assert.NotNull(document.Servers);

        // ONE SERVER, DELIBERATELY.
        //
        // A service's own document describes its own listener. Listing a sibling's port here would
        // invite a consumer to send a Gateway request to Security's port, or the reverse, and receive a
        // 404 with nothing explaining why. Each document names one URL, and docs/ARCHITECTURE.md is
        // where the whole port map lives.
        OpenApiServer server = Assert.Single(document.Servers);
        Assert.False(string.IsNullOrWhiteSpace(server.Url));

        // The port band is 5101-5105 and each document must claim the port its own service listens on.
        string expectedPort = resourceName == GatewayResourceName ? ":5105" : ":5104";
        Assert.Contains(expectedPort, server.Url, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  CONVENTIONS BOTH DOCUMENTS SHARE - what a consumer relies on across the pair
    // ==============================================================================================

    [Theory]
    [InlineData(GatewayResourceName)]
    [InlineData(SecurityResourceName)]
    public void EachDocumentRequiresABearerTokenByDefaultAtTheDocumentLevel(string resourceName)
    {
        OpenApiDocument document = ParseEmbeddedDocument(resourceName);

        // AUTHENTICATED IS THE DEFAULT, AND THAT IS A STRUCTURAL PROPERTY RATHER THAN A HABIT.
        //
        // Stating the requirement once at the document level and forcing every anonymous operation to
        // OVERRIDE it means a new operation is authenticated by DEFAULT. The opposite arrangement -
        // no document-level requirement, each operation adding its own - makes an unauthenticated
        // surface the consequence of FORGETTING something, which is the single most common way a
        // boundary ends up open. Constraint C-G requires every new boundary to be authenticated, and
        // this is the arrangement that makes that hard to get wrong rather than merely required.
        Assert.NotNull(document.Security);
        OpenApiSecurityRequirement requirement = Assert.Single(document.Security);
        Assert.Single(requirement);
    }

    [Theory]
    [InlineData(GatewayResourceName)]
    [InlineData(SecurityResourceName)]
    public void EachDocumentDeclaresTheBearerSchemeAsAJwtHttpScheme(string resourceName)
    {
        OpenApiDocument document = ParseEmbeddedDocument(resourceName);

        Assert.NotNull(document.Components?.SecuritySchemes);
        Assert.True(
            document.Components.SecuritySchemes.TryGetValue("bearerAuth", out IOpenApiSecurityScheme? scheme),
            $"'{resourceName}' does not declare a 'bearerAuth' security scheme.");
        Assert.NotNull(scheme);

        Assert.Equal(SecuritySchemeType.Http, scheme.Type);
        Assert.Equal("bearer", scheme.Scheme);

        // `bearerFormat: JWT` IS DESCRIPTIVE, AND IT IS ALSO THE HOOK A GENERATOR USES.
        //
        // It tells a code generator to emit a JWT-shaped credential type rather than an opaque string,
        // and it tells a reader that Security's JWKS endpoint is where verification material comes
        // from. Omitting it still produces a valid document and a worse one.
        Assert.Equal("JWT", scheme.BearerFormat);
    }

    [Theory]
    [InlineData(GatewayResourceName)]
    [InlineData(SecurityResourceName)]
    public void NeitherDocumentContainsAnyKeyMaterialOrCredentialLiteral(string resourceName)
    {
        // C-F SELF-AUDIT ON THE PUBLISHED CONTRACTS.
        //
        // The repository-wide sweep found eight in-source hardcoded-secret sites plus three binary
        // ones, and every one lives in the read-only legacy tree. The remediation posture is "never
        // replicate, document, and rotate" - so the obligation on anything this refactor AUTHORS is
        // that no value from any of those sites, and no credential-shaped literal of any kind, appears
        // in it.
        //
        // A published contract is the highest-exposure place such a literal could land: it is the file
        // a consumer reads first, it is embedded in a shipped assembly, and it is the kind of file
        // where an "example" credential looks entirely at home. Hence this check, on both documents,
        // over the raw text rather than the parsed model - because a value in a comment is still
        // committed to version control.
        string text = ReadEmbeddedDocument(resourceName);

        string[] forbidden =
        [
            "BEGIN RSA PRIVATE KEY",
            "BEGIN PRIVATE KEY",
            "BEGIN EC PRIVATE KEY",
            "BEGIN OPENSSH PRIVATE KEY",
            "BEGIN CERTIFICATE",
            "PRIVATE KEY-----",
            "MIIEvQIBADAN",   // the DER prologue of an unencrypted PKCS#8 RSA private key
            "MIIBOgIBAAJB",   // the DER prologue of a small PKCS#1 RSA private key
        ];

        foreach (string marker in forbidden)
        {
            Assert.DoesNotContain(marker, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(GatewayResourceName)]
    [InlineData(SecurityResourceName)]
    public void EveryOperationCarriesAnOperationIdAndTheyAreUniqueWithinTheDocument(string resourceName)
    {
        OpenApiDocument document = ParseEmbeddedDocument(resourceName);

        List<string> ids = [];

        foreach ((string route, IOpenApiPathItem pathItem) in document.Paths)
        {
            Assert.NotNull(pathItem.Operations);

            foreach ((HttpMethod method, OpenApiOperation operation) in pathItem.Operations)
            {
                // AN OPERATION ID IS NOT DECORATION.
                //
                // It is the name a generated client's method takes. Omitting it makes a generator
                // invent one from the route and the verb, which produces names that change whenever a
                // route is reorganised - so a consumer's code breaks on a change that was meant to be
                // purely cosmetic.
                Assert.False(
                    string.IsNullOrWhiteSpace(operation.OperationId),
                    $"{method} {route} in '{resourceName}' has no operationId.");
                ids.Add(operation.OperationId);
            }
        }

        Assert.NotEmpty(ids);

        // UNIQUENESS IS REQUIRED BY THE SPECIFICATION, and a duplicate is a generator error rather
        // than a warning in most toolchains - one of the two colliding operations simply disappears.
        string[] duplicates = ids.GroupBy(static id => id, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Theory]
    [InlineData(GatewayResourceName)]
    [InlineData(SecurityResourceName)]
    public void EveryOperationDeclaresAtLeastOneResponse(string resourceName)
    {
        OpenApiDocument document = ParseEmbeddedDocument(resourceName);

        foreach ((string route, IOpenApiPathItem pathItem) in document.Paths)
        {
            foreach ((HttpMethod method, OpenApiOperation operation) in pathItem.Operations!)
            {
                Assert.NotNull(operation.Responses);
                Assert.NotEmpty(operation.Responses);
            }
        }
    }

    [Theory]
    [InlineData(GatewayResourceName)]
    [InlineData(SecurityResourceName)]
    public void EveryErrorResponseUsesTheProblemDetailsMediaTypeAndShape(string resourceName)
    {
        OpenApiDocument document = ParseEmbeddedDocument(resourceName);

        // ONE ERROR SHAPE ACROSS BOTH DOCUMENTS, SO A CONSUMER WRITES ONE ERROR HANDLER.
        //
        // Every 4xx and 5xx uses RFC 9457 problem details under `application/problem+json`. That is
        // also what ASP.NET Core Minimal APIs emit WITHOUT hand-written code, so adopting the
        // framework's own error shape keeps the error path out of hand-written code - the same
        // reasoning that leaves token validation to the stock bearer handler rather than
        // reimplementing it.
        int checkedResponses = 0;

        foreach ((string route, IOpenApiPathItem pathItem) in document.Paths)
        {
            foreach ((HttpMethod method, OpenApiOperation operation) in pathItem.Operations!)
            {
                foreach ((string status, IOpenApiResponse response) in operation.Responses!)
                {
                    if (status.Length != 3 || (status[0] != '4' && status[0] != '5'))
                    {
                        continue;
                    }

                    // 501 is the one deliberate exception and it is checked separately in
                    // GatewayContractTests: the reserved routes return a MACHINE-READABLE body naming
                    // the deferred service, under `application/json`, precisely so a client can branch
                    // on it without parsing prose. A problem-details body would not carry that.
                    if (status == "501")
                    {
                        continue;
                    }

                    Assert.NotNull(response.Content);
                    Assert.True(
                        response.Content.ContainsKey("application/problem+json"),
                        $"{method} {route} -> {status} in '{resourceName}' does not use "
                            + $"application/problem+json. Declared: "
                            + $"{string.Join(", ", response.Content.Keys)}");

                    checkedResponses++;
                }
            }
        }

        Assert.True(checkedResponses > 0, $"No error responses were found in '{resourceName}'.");
    }

    [Theory]
    [InlineData(GatewayResourceName)]
    [InlineData(SecurityResourceName)]
    public void TheProblemDetailsSchemaPermitsExtensionMembersAndCarriesTheLegacyReturnCode(
        string resourceName)
    {
        OpenApiDocument document = ParseEmbeddedDocument(resourceName);

        Assert.NotNull(document.Components?.Schemas);
        Assert.True(
            document.Components.Schemas.TryGetValue("ProblemDetails", out IOpenApiSchema? problem),
            $"'{resourceName}' does not define a ProblemDetails schema.");
        Assert.NotNull(problem);

        // `additionalProperties: true` IS REQUIRED HERE, AND IT IS THE ONLY SCHEMA WHERE IT IS.
        //
        // RFC 9457 EXPLICITLY permits extension members on a problem-details object, and `retCode` is
        // one. Every other schema in both documents closes itself with `additionalProperties: false`
        // so an unrecognised field is a detectable error rather than silently ignored data - this one
        // cannot, because the specification it implements says otherwise.
        Assert.True(
            problem.AdditionalPropertiesAllowed,
            "ProblemDetails must permit extension members: RFC 9457 defines them, and `retCode` is one.");

        Assert.NotNull(problem.Properties);

        // THE LEGACY RETURN CODE TRAVELS WITH EVERY ERROR.
        //
        // An HTTP status says a request failed; the legacy return code says WHICH legacy validation
        // rejected it. Without it, `E_INVALID_ARGUMENT` and a column-expression parse failure are both
        // just "400" and a consumer cannot tell a retryable mistake from a malformed expression.
        Assert.True(
            problem.Properties.ContainsKey("retCode"),
            "ProblemDetails must carry `retCode` so the originating legacy return code survives the "
                + "projection into HTTP.");
    }

    [Fact]
    public void TheGatewayProblemDetailsDeclaresTheCorrelationIdentifierItActuallyReturns()
    {
        OpenApiDocument document = ParseEmbeddedDocument(GatewayResourceName);

        Assert.NotNull(document.Components?.Schemas);
        Assert.True(
            document.Components.Schemas.TryGetValue("ProblemDetails", out IOpenApiSchema? problem),
            "The gateway document does not define a ProblemDetails schema.");
        Assert.NotNull(problem);
        Assert.NotNull(problem.Properties);

        // THE CORRELATION IDENTIFIER IS THE ONLY WAY OUT OF THE REDACTION, SO IT MUST BE DECLARED.
        //
        // Gateway's `500` body is deliberately uninformative: `detail` is fixed prose and names nothing
        // about the fault - no stack trace, no exception message, no host, no port, no path, no key
        // material. That is defensible only because it REDIRECTS rather than refuses: the full
        // diagnostic exists on the operator channel and `traceId` is what locates it.
        //
        // `additionalProperties: true` would already TOLERATE the member, and tolerating it is exactly
        // what is not sufficient here. A caller has no contractual basis for reading a member the
        // document does not declare, so an undeclared bridge is a bridge a conforming consumer must
        // ignore - which would leave the redaction with no far side at all.
        //
        // This test is therefore paired with the handler that emits the member. If a future change
        // stops emitting it, the emitting side's own suite fails; if a future change removes the
        // declaration, this one does.
        Assert.True(
            problem.Properties.ContainsKey("traceId"),
            "Gateway's ProblemDetails must DECLARE `traceId`: it is the one member through which a "
                + "caller can reach the diagnostic detail the body withholds, so `additionalProperties: "
                + "true` merely tolerating it is not enough.");

        IOpenApiSchema traceId = problem.Properties["traceId"];

        // A string, because it carries either a W3C hexadecimal trace identifier or a host-generated
        // request identifier, and neither is numeric.
        Assert.Equal(JsonSchemaType.String, traceId.Type);

        // NOT required. The member is present on every response Gateway itself produces, but a
        // `required` declaration would also bind the forwarded-failure bodies, and an upstream's
        // problem-details response is not Gateway's to guarantee.
        Assert.False(
            problem.Required?.Contains("traceId") == true,
            "`traceId` must not be `required`: a forwarded upstream problem-details body is not "
                + "Gateway's to guarantee.");
    }
}
