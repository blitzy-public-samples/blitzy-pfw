// ==================================================================================================
//  IssuanceCredentialContractTests - C-01's TWO CALLER CREDENTIALS, AND THE PROSE THAT HAS TO AGREE
//                                    WITH THEM
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   `POST /v1/tokens` in shared/PowerFramework.Contracts/OpenApi/security.v1.yaml: the two
//            security schemes it accepts, the ALTERNATIVE form in which it declares them, and the
//            agreement between that declaration and the description printed beside it.
//
//  WHY A SUITE FOR A DESCRIPTION
//  ------------------------------------------------------------------------------------------------
//  The executable contract has accepted two caller credentials since it was authored - an HTTP
//  `Basic` credential naming a subject on the issuance roster, or a client certificate - and its
//  `security` block declares them as a two-entry list, which in OpenAPI means EITHER satisfies the
//  operation. The DESCRIPTION directly above that block said the operation was "protected by mutual
//  TLS and by nothing else" and that there was "no address, on any topology, at which a token is
//  minted without a client certificate".
//
//  A consumer reads the description. Acting on that one, an integrator provisions certificates it
//  does not need, or concludes the system cannot be brought up without a certificate authority - and
//  the drift had already reached executable code in three places, each of which refused or
//  down-reported a deployment that had configured the OTHER scheme. So the agreement between the
//  declaration and the prose is a contract property, and it is asserted here rather than reviewed.
//
//  WHAT THIS SUITE WILL NOT DO
//  ------------------------------------------------------------------------------------------------
//  It does not assert wording. Pinning sentences would fail on every legitimate edit and would say
//  nothing about correctness. What it pins is narrower and durable: that both scheme NAMES appear in
//  the description of the operation that accepts them, and that a closed list of EXCLUSIVITY claims -
//  the specific phrasings that assert one scheme and deny the other - appears nowhere in the
//  document. A description can be rewritten freely; it cannot go back to claiming the operation
//  accepts one credential.
// ==================================================================================================

using Microsoft.OpenApi;
using Xunit;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// The two caller credentials contract C-01 accepts on token issuance, and the description that has to
/// agree with them.
/// </summary>
/// <param name="documents">The assembly-wide OpenAPI fixture supplying the parsed Security document.</param>
public sealed class IssuanceCredentialContractTests(OpenApiContractDocuments documents)
{
    /// <summary>The operation identifier of the token-issuance operation.</summary>
    private const string IssuanceOperationId = "issueToken";

    /// <summary>The shared-secret scheme name, as the document declares it.</summary>
    private const string ClientCredentialSchemeName = "clientCredential";

    /// <summary>The client-certificate scheme name, as the document declares it.</summary>
    private const string MutualTlsSchemeName = "mutualTls";

    /// <summary>
    /// The status a caller receives when no accepted credential verified - the one description a refused
    /// integrator actually reads.
    /// </summary>
    private const string RefusalStatusCode = "401";

    /// <summary>
    /// The exclusivity claims that must appear NOWHERE in the document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A CLOSED LIST OF CLAIMS, NOT A WORDING RULE. Each entry asserts that one of the two schemes is
    /// the only credential the operation accepts, which contradicts the operation's own
    /// <c>security</c> block. They are matched case-insensitively over the whole document text,
    /// because the drift they describe reappeared in five separate places the first time and a
    /// per-operation check would have missed four of them.
    /// </para>
    /// <para>
    /// The absolute-guarantee phrasings are the ones that mattered most: "no address ... without a
    /// client certificate" reads as a promise about every topology, and an integrator who believes it
    /// provisions a certificate authority it does not need in order to bring the system up at all.
    /// </para>
    /// <para>
    /// THE LIST GREW ONCE, AND THE ADDITIONS ARE WHY IT IS A LIST. A later revision of the `401`
    /// description said mutual TLS was the "only accepted caller authentication on this operation" -
    /// the same claim in wording none of the original entries matched, in a response description
    /// rather than in the operation description, three lines below a <c>security</c> block declaring
    /// two alternatives. The three "is the only" forms were added with it, because that is the shape
    /// the claim keeps returning in.
    /// </para>
    /// </remarks>
    private static readonly string[] ExclusivityClaims =
    [
        "mutual tls and by nothing else",
        "mutual tls and with nothing else",
        "mutual-tls-only",
        "client certificate and by nothing else",
        "client certificate and with nothing else",
        "without a client certificate",
        "only accepted caller authentication",
        "mutual tls is the only",
        "client certificate is the only",
    ];

    /// <summary>
    /// Token issuance declares BOTH schemes, and declares them as alternatives rather than as a pair
    /// that must both be satisfied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SHAPE IS THE ASSERTION, AND OPENAPI MAKES THE TWO SHAPES LOOK ALIKE. Two requirement objects
    /// each naming one scheme means EITHER satisfies the operation; ONE requirement object naming two
    /// schemes means BOTH are required. The second is a contract no deployment could satisfy - a caller
    /// on a topology that terminates TLS ahead of this service cannot present a certificate at all - so
    /// collapsing the list would make issuance unreachable while still looking like a two-scheme
    /// declaration.
    /// </para>
    /// <para>
    /// EMPTY SCOPE LISTS ARE ASSERTED TOO. A scope list on a non-OAuth scheme is meaningless, and a
    /// value there would be read by a stock consumer as a scope it must request.
    /// </para>
    /// </remarks>
    [Fact]
    public void TokenIssuanceAcceptsEitherOfTwoCallerCredentials()
    {
        OpenApiOperation issuance = RequireIssuanceOperation();

        Assert.NotNull(issuance.Security);

        // TWO REQUIREMENT OBJECTS, each naming exactly one scheme - the OpenAPI spelling of "either".
        Assert.Equal(2, issuance.Security.Count);

        List<string> declared = [];

        foreach (OpenApiSecurityRequirement requirement in issuance.Security)
        {
            OpenApiSecuritySchemeReference scheme = Assert.Single(requirement.Keys);

            Assert.NotNull(scheme.Reference?.Id);

            declared.Add(scheme.Reference.Id!);

            Assert.Empty(requirement[scheme]);
        }

        declared.Sort(StringComparer.Ordinal);

        Assert.Equal(
            (string[])[ClientCredentialSchemeName, MutualTlsSchemeName],
            declared);
    }

    /// <summary>
    /// Both schemes are declared as components, so each requirement above resolves rather than dangling.
    /// </summary>
    /// <remarks>
    /// A requirement referencing an undeclared scheme is not merely undocumented: a stock consumer
    /// resolves the reference to nothing and cannot tell what to present. The <c>Basic</c> scheme's TYPE
    /// is asserted as well as its presence, because that is the part a consumer's own credential builder
    /// reads - an <c>http</c> scheme named <c>basic</c> is what makes a stock client attach RFC 7617
    /// encoding without bespoke code.
    /// </remarks>
    [Fact]
    public void BothIssuanceSchemesAreDeclaredComponents()
    {
        Assert.NotNull(documents.Security.Components?.SecuritySchemes);

        IDictionary<string, IOpenApiSecurityScheme> schemes = documents.Security.Components.SecuritySchemes;

        Assert.True(
            schemes.TryGetValue(ClientCredentialSchemeName, out IOpenApiSecurityScheme? clientCredential),
            $"The Security document at '{documents.SecurityDocumentPath}' declares no "
                + $"'{ClientCredentialSchemeName}' security scheme, so the requirement that references it "
                + "cannot be resolved by a consumer.");

        Assert.Equal(SecuritySchemeType.Http, clientCredential!.Type);
        Assert.Equal("basic", clientCredential.Scheme, StringComparer.OrdinalIgnoreCase);

        Assert.True(
            schemes.ContainsKey(MutualTlsSchemeName),
            $"The Security document at '{documents.SecurityDocumentPath}' declares no "
                + $"'{MutualTlsSchemeName}' security scheme.");
    }

    /// <summary>
    /// The issuance operation's own description names BOTH schemes it accepts.
    /// </summary>
    /// <remarks>
    /// NAMES AND NOT SENTENCES. The scheme identifiers are the join between the prose and the
    /// declaration, so requiring both to appear is what stops a description from documenting one
    /// credential while the block beside it declares two - without constraining how the description
    /// explains them.
    /// </remarks>
    [Fact]
    public void TheIssuanceDescriptionNamesBothSchemesItAccepts()
    {
        string description = RequireIssuanceOperation().Description ?? string.Empty;

        Assert.False(
            string.IsNullOrWhiteSpace(description),
            $"The '{IssuanceOperationId}' operation carries no description, so a consumer has only the "
                + "security block to work from.");

        Assert.Contains(ClientCredentialSchemeName, description, StringComparison.Ordinal);
        Assert.Contains(MutualTlsSchemeName, description, StringComparison.Ordinal);
    }

    /// <summary>
    /// No description anywhere in the document claims that either scheme is the ONLY accepted credential.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE ROW THAT WOULD HAVE FAILED, AND IT IS WHY THE SUITE EXISTS.</b> The issuance
    /// description asserted mutual TLS "and by nothing else" and that no address existed at which a token
    /// was minted "without a client certificate", directly above a <c>security</c> block declaring two
    /// alternatives - and the same claim had spread to the composition roots of two other services, to
    /// their readiness reporting, to the build documentation and to the end-to-end suite's own
    /// preconditions.
    /// </para>
    /// <para>
    /// THE WHOLE DOCUMENT IS SCANNED, not only the issuance operation, because the claim is exactly the
    /// kind that gets restated in a schema description or a document-level section where a per-operation
    /// check cannot see it.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoDescriptionClaimsEitherSchemeIsTheOnlyAcceptedCredential()
    {
        string document = File.ReadAllText(documents.SecurityDocumentPath);

        List<string> found = [.. ExclusivityClaims.Where(claim =>
            document.Contains(claim, StringComparison.OrdinalIgnoreCase))];

        // Composed by concatenation rather than through a culture-aware formatter because every
        // substitution is already a string: there is no number, date or decimal here for a culture to
        // render differently.
        Assert.True(
            found.Count == 0,
            $"The Security document at '{documents.SecurityDocumentPath}' claims one of the two "
                + $"accepted issuance credentials is the only one: {string.Join("; ", found)}. The "
                + $"'{IssuanceOperationId}' operation declares '{ClientCredentialSchemeName}' and "
                + $"'{MutualTlsSchemeName}' as ALTERNATIVES, and either satisfies it - so a description "
                + "asserting exclusivity sends an integrator to provision material no topology requires, "
                + "or to conclude the system cannot be brought up at all.");
    }

    /// <summary>
    /// The prose a refused caller actually reads agrees with the <c>security</c> block: every scheme the
    /// block declares is named by the operation description AND by the description of the refusal status.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE SCHEME NAMES COME FROM THE MACHINE CONTRACT, NOT FROM THIS FILE.</b> That is the whole
    /// point of this row and what makes it a semantic-agreement assertion rather than a second wording
    /// rule: the accepted set is read out of the operation's own <c>security</c> block, so a scheme
    /// added, renamed or removed there is carried into the assertion automatically. The two sibling
    /// rows above pin the declaration's SHAPE and pin that the operation description names the two
    /// schemes by their literal identifiers; neither would notice a third scheme being declared and
    /// then explained nowhere.
    /// </para>
    /// <para>
    /// THE REFUSAL DESCRIPTION IS INCLUDED BECAUSE IT IS WHERE THE DRIFT LANDED THE SECOND TIME. A
    /// caller that fails to authenticate reads <c>401</c>, not the operation summary, and a <c>401</c>
    /// attributing the refusal to a missing client certificate sends a deployment whose proxy strips
    /// certificates to provision a certificate authority instead of checking its roster entry. Naming
    /// both schemes there is what makes the status mean "neither accepted credential verified".
    /// </para>
    /// <para>
    /// IT ASSERTS PRESENCE OF THE IDENTIFIERS AND NOTHING ELSE. How each scheme is explained, in what
    /// order, and at what length are all free; the closed exclusivity list in the row above is what
    /// stops a description that names both from still claiming only one is accepted.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryCredentialTheOperationAcceptsIsNamedByItsDescriptionAndByItsRefusalStatus()
    {
        OpenApiOperation issuance = RequireIssuanceOperation();

        Assert.NotNull(issuance.Security);

        List<string> declaredSchemes = [];

        foreach (OpenApiSecurityRequirement requirement in issuance.Security)
        {
            foreach (OpenApiSecuritySchemeReference scheme in requirement.Keys)
            {
                string? id = scheme.Reference?.Id;

                if (!string.IsNullOrEmpty(id) && !declaredSchemes.Contains(id, StringComparer.Ordinal))
                {
                    declaredSchemes.Add(id);
                }
            }
        }

        // A block that resolved to no scheme name at all would make every assertion below vacuous, so
        // the count is asserted before the descriptions are read.
        Assert.Equal(2, declaredSchemes.Count);

        Assert.NotNull(issuance.Responses);

        IOpenApiResponse refusal = Assert.Contains(RefusalStatusCode, issuance.Responses);

        (string Location, string Text)[] proseThatMustAgree =
        [
            ($"the '{IssuanceOperationId}' operation description", issuance.Description ?? string.Empty),
            ($"the '{RefusalStatusCode}' response description", refusal.Description ?? string.Empty),
        ];

        foreach ((string location, string text) in proseThatMustAgree)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(text),
                $"The Security document at '{documents.SecurityDocumentPath}' leaves {location} empty, so "
                    + "a consumer has only the security block to work from and cannot tell which of the "
                    + "declared credentials its own topology can present.");

            List<string> unexplained = [.. declaredSchemes.Where(
                scheme => !text.Contains(scheme, StringComparison.Ordinal))];

            // Composed by concatenation rather than through a culture-aware formatter because every
            // substitution is already a string.
            Assert.True(
                unexplained.Count == 0,
                $"The '{IssuanceOperationId}' operation in '{documents.SecurityDocumentPath}' declares "
                    + $"{string.Join(" and ", declaredSchemes)} as ALTERNATIVES, either of which satisfies "
                    + $"it, but {location} never names {string.Join("; ", unexplained)}. Prose that "
                    + "explains one of two accepted credentials reads as a requirement for the one it "
                    + "names: on the refusal status it sends a deployment whose proxy terminates TLS to "
                    + "provision a certificate authority no topology requires, and on the operation it "
                    + "hides the scheme that deployment must actually use.");
        }
    }

    /// <summary>Resolves the token-issuance operation, or fails naming what the document does declare.</summary>
    /// <returns>The operation.</returns>
    private OpenApiOperation RequireIssuanceOperation()
    {
        List<OpenApiOperation> matches =
        [
            .. documents.Security.Paths
                .SelectMany(path => path.Value.Operations ?? new Dictionary<HttpMethod, OpenApiOperation>())
                .Select(entry => entry.Value)
                .Where(operation => string.Equals(
                    operation.OperationId,
                    IssuanceOperationId,
                    StringComparison.Ordinal)),
        ];

        // Ambiguity is an error rather than a first-match coin toss: a duplicated operationId would hand
        // this suite the wrong operation and still pass.
        Assert.True(
            matches.Count == 1,
            $"Expected exactly one operation with operationId '{IssuanceOperationId}' in "
                + $"'{documents.SecurityDocumentPath}'; found {matches.Count}.");

        return matches[0];
    }
}
