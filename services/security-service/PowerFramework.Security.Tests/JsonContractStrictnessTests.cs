// ==================================================================================================
//  JsonContractStrictnessTests.cs - THE READER ACCEPTS EXACTLY WHAT THE DOCUMENT DECLARES
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  shared/PowerFramework.Contracts/OpenApi/security.v1.yaml is AUTHORITATIVE for everything on this
//  service's wire, and it is strict in two ways that a serializer's web defaults are not:
//
//    * Every request schema is CLOSED with additionalProperties: false, while the default reader skips
//      an undeclared member in silence. A server that accepts a body its own published document forbids
//      disagrees with every client that validates against that document, and the disagreement is
//      invisible until the two are wired together (CWE-20).
//    * Every member name and the payload-form tokens are declared with one exact spelling, while the
//      web defaults match property names case-insensitively AND the stock string-enum converter matches
//      token names case-insensitively by construction. Both leniencies admit spellings a
//      document-validating client refuses.
//
//  NEITHER IS A LEGACY BEHAVIOUR QUESTION, AND THAT IS WHY TIGHTENING BOTH IS PRESERVATION RATHER THAN
//  CORRECTION (constraint C-B). PowerFramework is a library with no listener, no route and no wire
//  format of any kind - ws_objects/pfw.crypto.pbl.src/n_crypto.sru is roughly sixty in-process
//  overloads - so there is no legacy request body whose leniency could be preserved. The authored
//  contract document is the only authority these bodies ever had.
//
//  HOW THE ROWS ARE ORGANISED
//  Section 1 asserts the payload-form converter directly, with no host, because it is a unit with an
//  exactly enumerable accepted set. Section 2 asserts the same properties THROUGH A BOOTED HOST, which
//  is the only vantage point that proves the configured reader is the one the endpoints actually use.
//  Section 3 asserts that the generated document still declares what the authored one declares, which
//  is the cost the custom converter incurs and the transformer repays.
//
//  EVERY SECTION CARRIES A POSITIVE ARM. A suite that only proved refusals would pass just as happily
//  against a reader that refused everything, which would be a far worse defect than the one being
//  fixed.
// ==================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using PowerFramework.Security.Endpoints;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

// ==================================================================================================
//  SECTION 1 - THE PAYLOAD-FORM SELECTOR ADMITS TWO TOKENS, CHARACTER FOR CHARACTER
// ==================================================================================================

/// <summary>
/// The payload-form selector reads and writes exactly the two tokens the contract declares.
/// </summary>
/// <remarks>
/// <para>
/// THE SELECTOR GOVERNS WHICH LEGACY OVERLOAD FAMILY RUNS, so a value coerced into one of the two is
/// not a cosmetic difference: it decides whether the <c>data</c> member is read as a legacy string
/// verbatim or base64-decoded into raw bytes, and therefore what the caller gets back.
/// </para>
/// <para>
/// Asserted through a private carrier record rather than through a request record, so that these rows
/// keep passing if a request record's member set changes for an unrelated reason. The carrier declares
/// the member as NULLABLE, exactly as every request record does, so the null arm exercises the real
/// shape.
/// </para>
/// </remarks>
public sealed class PayloadFormSelectorTests
{
    /// <summary>A minimal carrier for the selector, shaped like the request records.</summary>
    private sealed record FormCarrier
    {
        /// <summary>The selector under test.</summary>
        public PayloadForm? Form { get; init; }
    }

    /// <summary>Both declared tokens are accepted and map to their own member.</summary>
    /// <param name="token">The token as it appears on the wire.</param>
    /// <param name="expected">The member the token names.</param>
    [Theory]
    [InlineData("STRING", PayloadForm.STRING)]
    [InlineData("BLOB", PayloadForm.BLOB)]
    public void EitherDeclaredTokenIsAccepted(string token, PayloadForm expected)
    {
        FormCarrier? carrier = JsonSerializer.Deserialize<FormCarrier>(
            "{\"Form\":\"" + token + "\"}");

        Assert.NotNull(carrier);
        Assert.Equal(expected, carrier.Form);
    }

    /// <summary>
    /// A token differing from a declared one only in casing is refused.
    /// </summary>
    /// <param name="token">The mis-cased token.</param>
    /// <remarks>
    /// THIS IS THE ROW THE STOCK CONVERTER CANNOT PASS. A converter derived from
    /// <c>JsonStringEnumConverter&lt;PayloadForm&gt;</c> with a null naming policy and integer values
    /// refused still accepts every one of these, because that converter matches names
    /// case-insensitively by construction and does not expose the setting. The document declares
    /// <c>enum: [STRING, BLOB]</c>, so a validating client refuses all of them - which makes accepting
    /// them a divergence between this service and its own contract.
    /// </remarks>
    [Theory]
    [InlineData("string")]
    [InlineData("String")]
    [InlineData("sTRING")]
    [InlineData("blob")]
    [InlineData("Blob")]
    [InlineData("bLOB")]
    public void EveryOtherCasingOfADeclaredTokenIsRefused(string token)
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<FormCarrier>("{\"Form\":\"" + token + "\"}"));
    }

    /// <summary>
    /// Every form that is not one of the two declared strings is refused.
    /// </summary>
    /// <param name="rawValue">The value exactly as it appears in the JSON body.</param>
    /// <remarks>
    /// The numeric rows matter for a reason beyond schema fidelity: an enumeration in .NET does not
    /// restrict a value to its declared members, so a coerced <c>7</c> would compare unequal to
    /// <see cref="PayloadForm.STRING"/> and be classified as the blob family by every
    /// "string, otherwise blob" test - base64-decoding a payload nobody described that way.
    /// </remarks>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("7")]
    [InlineData("-1")]
    [InlineData("\"\"")]
    [InlineData("\"TEXT\"")]
    [InlineData("\"STRING \"")]
    [InlineData("\" STRING\"")]
    [InlineData("true")]
    [InlineData("[\"STRING\"]")]
    [InlineData("{\"value\":\"STRING\"}")]
    public void EveryOtherFormIsRefused(string rawValue)
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<FormCarrier>("{\"Form\":" + rawValue + "}"));
    }

    /// <summary>
    /// An absent or null selector still binds as absent, which is the state the handlers answer.
    /// </summary>
    /// <param name="body">The body, with the member null and with it omitted.</param>
    /// <remarks>
    /// LOAD-BEARING RATHER THAN INCIDENTAL. Every request record models the selector as nullable so
    /// that absence is an OBSERVABLE state the handler answers with the contract's own bad-request
    /// body carrying a return code; a converter that threw on null would make that documented response
    /// unreachable and answer a bodiless framework failure instead.
    /// </remarks>
    [Theory]
    [InlineData("{\"Form\":null}")]
    [InlineData("{}")]
    public void AnAbsentSelectorRemainsTheObservableAbsentState(string body)
    {
        FormCarrier? carrier = JsonSerializer.Deserialize<FormCarrier>(body);

        Assert.NotNull(carrier);
        Assert.Null(carrier.Form);
    }

    /// <summary>
    /// The declared tokens are what the selector writes, and an undefined value is refused.
    /// </summary>
    /// <remarks>
    /// The write half is asserted because the exact-token read half would be worthless if the responses
    /// this service produces carried something else: the two ends of the contract have to agree, and
    /// the consuming service's reader is equally exact.
    /// </remarks>
    [Fact]
    public void TheDeclaredTokensAreWrittenAndAnUndefinedValueIsRefused()
    {
        Assert.Equal(
            "{\"Form\":\"STRING\"}",
            JsonSerializer.Serialize(new FormCarrier { Form = PayloadForm.STRING }),
            StringComparer.Ordinal);

        Assert.Equal(
            "{\"Form\":\"BLOB\"}",
            JsonSerializer.Serialize(new FormCarrier { Form = PayloadForm.BLOB }),
            StringComparer.Ordinal);

        Assert.Throws<JsonException>(
            () => JsonSerializer.Serialize(new FormCarrier { Form = (PayloadForm)7 }));
    }

    /// <summary>
    /// The refusal text names the accepted set and echoes nothing the caller sent.
    /// </summary>
    /// <remarks>
    /// A deserialization failure is answered as the contract's bad request, and a message that quoted
    /// the rejected bytes back would put caller-supplied content into a response body and into the log
    /// record beside it. The accepted set IS named, because a refusal a caller cannot act on is a
    /// refusal it will retry.
    /// </remarks>
    [Fact]
    public void TheRefusalNamesTheAcceptedSetAndEchoesNothing()
    {
        const string Smuggled = "definitely-not-a-declared-token";

        JsonException refusal = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<FormCarrier>("{\"Form\":\"" + Smuggled + "\"}"));

        Assert.NotNull(refusal.Message);
        Assert.Contains("STRING", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("BLOB", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Smuggled, refusal.Message, StringComparison.Ordinal);
    }
}

// ==================================================================================================
//  SECTION 2 - THE CONFIGURED READER IS THE ONE THE ENDPOINTS USE
// ==================================================================================================

/// <summary>
/// A booted host accepts exactly the bodies the published document declares.
/// </summary>
/// <remarks>
/// <para>
/// THESE ROWS NEED A HOST AND CANNOT BE UNIT TESTS. The strictness lives in the composition root's
/// serializer options, and the property under test is that the ENDPOINTS read through those options -
/// which is exactly what a unit test constructing a request record by hand cannot observe.
/// </para>
/// <para>
/// The unkeyed digest operation is the representative body, chosen because it needs no key reference,
/// no retained state and no configured material, so a refusal can only be about the reader. The
/// issuance body is asserted separately, because it is reached with a transport credential rather than
/// a token and is therefore a different pipeline into the same reader.
/// </para>
/// </remarks>
public sealed class RequestBindingStrictnessTests
{
    /// <summary>The representative cryptographic route: the unkeyed digest.</summary>
    private static readonly Uri HashRoute = new("/v1/crypto/hash", UriKind.Relative);

    /// <summary>The single error media type this service produces for every non-success response.</summary>
    private const string ProblemMediaType = "application/problem+json";

    /// <summary>The digest algorithm every row here asks for.</summary>
    /// <remarks>
    /// SHA-256, the strongest identifier the legacy catalogue offers that is also a defensible default
    /// for a row that is not about algorithm selection. Written as the preserved constant rather than
    /// as a literal so a renumbering of the catalogue breaks here rather than silently changing what
    /// these rows hash with.
    /// </remarks>
    private static readonly long HashType = Enums.CRYPTO_HASH_SHA256;

    /// <summary>Builds a body that the document declares in full.</summary>
    /// <returns>The JSON text.</returns>
    private static string WellFormedBody() => string.Create(
        CultureInfo.InvariantCulture,
        $"{{\"data\":\"payload\",\"payloadForm\":\"STRING\",\"hashType\":{HashType}}}");

    /// <summary>Posts a raw JSON body to the digest operation with a token.</summary>
    /// <param name="factory">The host.</param>
    /// <param name="body">The exact bytes to send.</param>
    /// <returns>The response.</returns>
    /// <remarks>
    /// RAW TEXT RATHER THAN A SERIALIZED RECORD, DELIBERATELY. A record cannot express an undeclared
    /// member or a mis-cased one, which is the whole subject of this suite - serializing one would
    /// assert that the test's own serializer agrees with itself.
    /// </remarks>
    private static async Task<HttpResponseMessage> PostAsync(SecurityHostFactory factory, string body)
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        using StringContent content = new(body, Encoding.UTF8, "application/json");

        return await client.PostAsync(HashRoute, content, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// THE POSITIVE ARM: a body the document declares in full is accepted.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// FIRST IN THE FILE ON PURPOSE. Every row below asserts a refusal, and a reader that refused every
    /// body would satisfy all of them - so the suite is worthless without this one.
    /// </remarks>
    [Fact]
    public async Task ADocumentedBodyIsAcceptedAsync()
    {
        using SecurityHostFactory factory = new();

        using HttpResponseMessage response = await PostAsync(factory, WellFormedBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// A member the document does not declare is refused rather than ignored.
    /// </summary>
    /// <param name="undeclaredMember">The undeclared member, as JSON text including its comma.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE CLOSED-SCHEMA HALF OF THE CONTRACT, ENFORCED. The default reader skips these in silence, so
    /// before this was configured the service accepted a body its own document forbids.
    /// </para>
    /// <para>
    /// THE KEY-MATERIAL ROW IS THE ONE THAT MATTERS MOST. The contract-level secrets rule is that raw
    /// key material never crosses the wire inbound, and the request records declare no member that
    /// could carry it. Silently DROPPING a member called <c>key</c> satisfied that rule only by
    /// accident: the caller believed it had supplied a key, the service ignored it, and the mismatch
    /// was undetectable from either end. Refusing it makes the rule observable.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("\"key\":\"material\",")]
    [InlineData("\"keyMaterial\":\"material\",")]
    [InlineData("\"password\":\"material\",")]
    [InlineData("\"mode\":1,")]
    [InlineData("\"unexpected\":null,")]
    public async Task AnUndeclaredMemberIsRefusedAsync(string undeclaredMember)
    {
        using SecurityHostFactory factory = new();

        using HttpResponseMessage response = await PostAsync(
            factory,
            "{" + undeclaredMember + "\"data\":\"payload\",\"payloadForm\":\"STRING\",\"hashType\":"
                + HashType.ToString(CultureInfo.InvariantCulture) + "}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A declared member sent under a different casing is refused.
    /// </summary>
    /// <param name="body">The whole body, with one member mis-cased.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The web defaults match property names case-insensitively, so each of these would otherwise BIND
    /// to the member the document spells in one exact way. A document-validating client refuses them as
    /// undeclared members, so accepting them is the same divergence as the row above wearing a
    /// different hat - which is why both are corrected in the same place and asserted separately.
    /// </remarks>
    [Theory]
    [InlineData("{\"Data\":\"payload\",\"payloadForm\":\"STRING\",\"hashType\":2}")]
    [InlineData("{\"data\":\"payload\",\"PayloadForm\":\"STRING\",\"hashType\":2}")]
    [InlineData("{\"data\":\"payload\",\"payloadForm\":\"STRING\",\"HASHTYPE\":2}")]
    public async Task AMisCasedMemberNameIsRefusedAsync(string body)
    {
        using SecurityHostFactory factory = new();

        using HttpResponseMessage response = await PostAsync(factory, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A mis-cased payload-form token is refused by the host, not merely by the converter.
    /// </summary>
    /// <param name="token">The mis-cased token.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The converter is asserted directly in section 1; this row proves the ENDPOINT reads through it,
    /// which is the property that would break if a later change registered a serializer option set that
    /// bypassed the attribute.
    /// </remarks>
    [Theory]
    [InlineData("string")]
    [InlineData("Blob")]
    public async Task AMisCasedPayloadFormTokenIsRefusedByTheHostAsync(string token)
    {
        using SecurityHostFactory factory = new();

        using HttpResponseMessage response = await PostAsync(
            factory,
            "{\"data\":\"payload\",\"payloadForm\":\"" + token + "\",\"hashType\":"
                + HashType.ToString(CultureInfo.InvariantCulture) + "}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A refused body is answered with the single error shape the document publishes.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE ROW THAT PROVES THE REFUSAL IS STILL CONTRACT-CONFORMANT. A binding failure is
    /// produced by the FRAMEWORK rather than by a handler, so it carries a status and no body of its
    /// own - and the document declares that every non-2xx response of this service carries the problem
    /// shape with the legacy return code in its <c>retCode</c> member. Tightening the reader would
    /// otherwise have introduced a documented response the service does not actually produce.
    /// </para>
    /// <para>
    /// The return code is asserted to be the invalid-argument code specifically, because that is what
    /// the composition root's classifier maps a bad request to, and a malformed request is exactly
    /// that under the legacy algebra.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ARefusedBodyCarriesThePublishedProblemShapeAsync()
    {
        using SecurityHostFactory factory = new();

        using HttpResponseMessage response = await PostAsync(
            factory,
            "{\"smuggled\":\"member\",\"data\":\"payload\",\"payloadForm\":\"STRING\",\"hashType\":"
                + HashType.ToString(CultureInfo.InvariantCulture) + "}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Equal(
            ProblemMediaType,
            response.Content.Headers.ContentType.MediaType,
            StringComparer.Ordinal);

        string payload = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        using JsonDocument problem = JsonDocument.Parse(payload);

        Assert.True(
            problem.RootElement.TryGetProperty("retCode", out JsonElement retCode),
            "Every non-success response of this service carries the legacy return code.");

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, retCode.GetInt64());

        // The rejected member name is caller-supplied content and must not be reflected back.
        Assert.DoesNotContain("smuggled", payload, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The issuance body is read by the same strict reader, and its documented body still works.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// ASSERTED SEPARATELY BECAUSE ISSUANCE IS A DIFFERENT PIPELINE INTO THE SAME READER: it is reached
    /// with a mutual-TLS client certificate rather than a bearer token, so a row on the cryptographic
    /// surface proves nothing about it. Both arms are in one row so that the refusal and the acceptance
    /// are measured against the SAME host and the same certificate - which is what makes the refusal
    /// attributable to the body rather than to the credential.
    /// </remarks>
    [Fact]
    public async Task TheIssuanceBodyIsReadByTheSameStrictReaderAsync()
    {
        using X509Certificate2 certificate = IssuanceFixture.CreateCallerCertificate(
            IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        Uri issuance = new(IssuanceFixture.IssuancePath, UriKind.Relative);

        using StringContent smuggled = new(
            "{\"subject\":\"" + IssuanceFixture.CallerIdentity + "\",\"audience\":\""
                + IssuanceFixture.CallerIdentity + "\",\"scopes\":[\"" + IssuanceFixture.ReadScope
                + "\"],\"lifetimeMinutes\":525600}",
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage refused = await client.PostAsync(
            issuance,
            smuggled,
            TestContext.Current.CancellationToken);

        // AN UNDECLARED MEMBER THAT WOULD HAVE BEEN A LIFETIME OVERRIDE IS THE POINT OF THIS ROW. Under
        // the default reader a caller could send one, receive a 200, and reasonably believe it had asked
        // for a year-long token - while the issuer minted its configured short-lived one. Refusing the
        // body is what makes the two ends agree about what was asked for.
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        using HttpResponseMessage issued = await client.PostAsJsonAsync(
            issuance,
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
    }
}

// ==================================================================================================
//  SECTION 3 - THE GENERATED DOCUMENT STILL SAYS WHAT THE AUTHORED ONE SAYS
// ==================================================================================================

/// <summary>
/// The generated contract document declares the payload-form selector as the authored one does.
/// </summary>
/// <remarks>
/// <para>
/// THIS SUITE EXISTS BECAUSE THE EXACT-TOKEN CONVERTER HAS A DOCUMENTATION COST, and the cost is paid
/// by a schema transformer rather than left unpaid. The document generator builds each schema from the
/// serializer's type information, and the exporter can describe converters it recognises but not custom
/// ones - so a type carrying a hand-written converter is emitted as an UNCONSTRAINED schema. Measured
/// on this toolchain, that turns <c>enum: [STRING, BLOB]</c> into "anything".
/// </para>
/// <para>
/// A consumer generates its client from whichever document it is handed, so a generated document that
/// constrained nothing would hand every consumer a client that could send a token this service refuses.
/// </para>
/// </remarks>
public sealed class GeneratedDocumentPayloadFormTests
{
    /// <summary>The generated contract document's route.</summary>
    private static readonly Uri DocumentRoute = new("/openapi/v1.json", UriKind.Relative);

    /// <summary>
    /// The generated schema is a string constrained to the two declared tokens.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Asserted against the SCHEMA rather than against a substring of the document text, so a row
    /// cannot pass because the tokens happen to appear in a description paragraph - which they do, in
    /// several.
    /// </remarks>
    [Fact]
    public async Task TheGeneratedSchemaDeclaresTheTwoDeclaredTokensAsync()
    {
        using SecurityHostFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(
            DocumentRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        JsonElement schema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(nameof(PayloadForm));

        Assert.Equal("string", schema.GetProperty("type").GetString(), StringComparer.Ordinal);

        string[] tokens =
        [
            .. schema.GetProperty("enum")
                .EnumerateArray()
                .Select(static token => token.GetString() ?? string.Empty)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal([nameof(PayloadForm.BLOB), nameof(PayloadForm.STRING)], tokens);
    }
}
