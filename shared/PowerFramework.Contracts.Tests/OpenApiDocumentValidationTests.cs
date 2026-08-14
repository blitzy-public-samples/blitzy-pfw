// ==================================================================================================
//  OpenApiDocumentValidationTests - the two published OpenAPI documents, as PARSED MODELS
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml   (C-09 ingress, C-10 readiness)
//            shared/PowerFramework.Contracts/OpenApi/security.v1.yaml  (C-01 TokenService, C-02 Crypto)
//
//  THIS SUITE HAS TWO JOBS THAT LOOK LIKE ONE
//  ------------------------------------------------------------------------------------------------
//  Validating the two documents is the VISIBLE job. Guarding the `Microsoft.OpenApi` version pin is
//  the VALUABLE one, because that pin sits between a security advisory on one side and a compiler
//  error on the other, and nothing else in this repository notices when it moves. A pin change has
//  three possible outcomes: a vulnerable restore, a broken compile, or - the quiet one - a silently
//  different object model that parses these documents differently. The first two announce themselves.
//  The third is what this file makes visible, here, rather than deep inside a service build where it
//  would surface as an unexplained client-generation difference.
//
//  THE PIN IS 2.12.0, IN BOTH DIRECTIONS, AND IT IS MANDATORY  (AAP 0.5.2, constraint C-K)
//  ------------------------------------------------------------------------------------------------
//  Recorded here in full because a future reader who sees a row in this file fail will be tempted to
//  "just upgrade", and both neighbouring directions are worse than the failure:
//
//    * BELOW the pin - every version at or below 2.7.4 carries NuGet advisory NU1903, high severity.
//      The transitive floor is chosen by `Microsoft.AspNetCore.OpenApi` rather than by this
//      repository, and it has sat inside that range before now, so it arrives without anybody asking
//      for it. Restore reports it, and warnings are errors repository-wide, so it is a build failure
//      rather than a warning.
//
//    * ABOVE the pin - the 3.x line BREAKS THE BUILD. 3.9.0 and 3.10.0 were each tested directly and
//      produce two `error CS0200` diagnostics reporting that a media-type example property cannot be
//      assigned because it is read-only, raised inside the SDK's OWN generated OpenAPI XML-comment
//      support file. The cause is not this repository's code: the 10.0.11 OpenAPI source generator is
//      compiled against the 2.x object model, so nothing in the 3.x line can satisfy it.
//
//    * 2.12.0, the highest published 2.x, is therefore the ONLY value that is simultaneously
//      non-vulnerable and compatible. Corroborated independently: the NuGet vulnerability index lists
//      advisories for `Microsoft.OpenApi` covering only [2.0.0-preview.11, 2.7.4] and [3.0.0, 3.5.3],
//      so 2.12.0 sits clear of both ranges.
//
//    * AND WITHIN the 2.x line the value is not free either. The pin moved from 2.11.0 to 2.12.0
//      because its lock-stepped YAML reader below is DEPRECATED upstream at 2.11.0 - reason
//      CriticalBugs, recommended alternate [3.10.0, ), which is the line that cannot compile - and
//      2.12.0 is the first 2.x of the reader that carries no deprecation. Neither Microsoft.OpenApi
//      2.11.0 nor 2.12.0 is vulnerable or deprecated itself; the reader is what forced the pair.
//
//  The version itself is NOT written in this file, and must not be. It lives once, centrally, in the
//  repository-root Directory.Packages.props, and `Directory.Build.props` disables central version
//  overriding so a per-project divergence is a hard restore error (NU1013) rather than a silent one.
//  What this file contributes is BEHAVIOURAL: if the resolved object model ever changes shape, these
//  rows change with it.
//
//  THE YAML READER IS A SEPARATE PACKAGE, AND THAT IS NOT AN OVERSIGHT
//  ------------------------------------------------------------------------------------------------
//  The pinned `Microsoft.OpenApi` 2.12.0 assembly ships NO YAML READER. It has `OpenApiJsonReader`,
//  `OpenApiJsonWriter` and `OpenApiYamlWriter` - and a writer cannot parse. Verified by inspecting the
//  published package. Both contract documents are YAML, so YAML support comes from the separately
//  published `Microsoft.OpenApi.YamlReader` 2.12.0, registered through `settings.AddYamlReader()`. Its
//  dependency on `Microsoft.OpenApi` is a MINIMUM range - [2.12.0, ) - rather than the exact lock an
//  earlier note here claimed, so it cannot pull the mandatory pin DOWN but it does not hold it either;
//  the central PackageVersion entry is what holds it, and the two must therefore be moved together.
//
//  `ContractTestContext.cs` OWNS that registration; this suite consumes its result. That division
//  matters: the fixture parses each document exactly once for the whole assembly, and this file
//  asserts over the diagnostics it exposes rather than reparsing and thereby measuring its own
//  settings instead of the fixture's. The `Format` assertion in Phase 1 is what proves the YAML
//  reader was the reader actually used - without it, `AddYamlReader()` could be dropped and the
//  failure would surface as an unrelated parse error somewhere downstream.
//
//  WHY THIS SUITE IS SEPARATE FROM ITS TWO SIBLINGS  (division of labour, stated so it is not merged)
//  ------------------------------------------------------------------------------------------------
//  All three read the same two documents; none of them reads them the same way, and the difference is
//  the point rather than an accident of authorship.
//
//    OpenApiContractDocumentTests   reads the EMBEDDED copies out of the compiled assembly manifest
//                                   and parses them itself. It proves the documents SHIP - that the
//                                   `OpenApi/**/*.yaml` glob matched both files, that exactly two are
//                                   embedded, and that the cross-document conventions hold.
//    GatewayContractTests           also parses the embedded copy, and owns everything specific to
//                                   C-09 and C-10 on Gateway: the route inventory, the status mapping,
//                                   the four reserved routes, the x-proto-* delegation.
//    THIS FILE                      reads the AUTHORED FILES through the shared assembly fixture. It
//                                   proves what the documents SAY, and it owns the four things nothing
//                                   else in this project asserts at all:
//                                       1. an explicit Validate() pass against the object model's own
//                                          default rule set;
//                                       2. the integrity of that rule set, so the pass cannot become
//                                          vacuous;
//                                       3. that every $ref actually RESOLVES - see the measurement
//                                          below, because this one is not what it appears;
//                                       4. the anonymous-versus-authenticated inventory across BOTH
//                                          documents, which is the document-level form of C-G.
//
//  Two views of the same file through two different code paths is deliberate belt-and-braces on the
//  pin: a model change that altered parsing would have to alter both identically to stay hidden.
//
//  THE MEASUREMENT THAT DETERMINED HOW REFERENCES ARE CHECKED  (constraint C-K)
//  ------------------------------------------------------------------------------------------------
//  The obvious way to assert that references resolve is to look for a reader diagnostic. Under this
//  pin THAT DOES NOT WORK, and an assertion written that way would pass forever while the document
//  was broken. Measured directly against gateway.v1.yaml on this toolchain:
//
//    * Repointing `#/components/responses/Unauthorized` at a component that does not exist yields
//      ZERO reader errors, ZERO reader warnings and ZERO validator errors - even though
//      `OpenApiDocumentReferencesAreValid` is a member of the default rule set. The breakage surfaces
//      ONLY as `IOpenApiReferenceHolder.UnresolvedReference == true` with a null `Target`, at 41
//      distinct sites, each carrying an exact JSON pointer.
//    * Repointing a schema at an off-machine URL while `LoadExternalRefs` is false likewise yields no
//      diagnostic at all. It surfaces only as `Reference.IsExternal == true`.
//
//  Both assertions below therefore WALK THE PARSED MODEL with `OpenApiWalker`, which is the only
//  mechanism in this version that can see either fault. That is the mechanism, recorded as required.
//
//  By contrast, deleting a response `description` IS caught, twice - it appears in
//  `Diagnostic.Errors` AND in the explicit Validate() pass as rule `ResponseRequiredFields` at
//  pointer `#/paths/~1v1~1ping/get/responses/200/description` - and the same document under
//  `GetEmptyRuleSet()` reports nothing. That contrast is why `TheDefaultRuleSetCarriesTheRules...`
//  exists as a permanent test rather than as a one-off manual check: it is the guard on the guard.
//
//  WHAT THIS SUITE DELIBERATELY DOES NOT DO
//  ------------------------------------------------------------------------------------------------
//  * No host, no HTTP client, no generated client, no endpoint invocation - document-level assertions
//    only (constraint C-A). Nothing here starts anything or reaches anything.
//  * No house style. Validation uses `ValidationRuleSet.GetDefaultRuleSet()`, the model's OWN rules,
//    and never a bespoke rule set (constraint C-B). Asserting a local convention here would force the
//    documents to be rewritten to satisfy a preference, which is precisely the kind of unrequested
//    change the no-improvement constraint forbids.
//  * No `GetEmptyRuleSet()` in any assertion. An empty rule set makes the validation row pass for a
//    document that is provably broken - measured above - and a vacuous assertion is the single worst
//    outcome available to this file. It appears below only as the SUBJECT of the rule-set integrity
//    test, never as the rule set anything is validated with.
//  * No secret, no key, no certificate and no sample token in any form (constraint C-F). Nothing here
//    needs one: every assertion is about document STRUCTURE, and the one operation that carries
//    credentials, `POST /v1/tokens`, is asserted by the NAME of the scheme protecting it.
//  * No read of the legacy tree at runtime (constraint C-C). The legacy locators cited in this file -
//    `ws_objects/pfw.shared.pbl.src/enums.sru:L41-L49` for the capability bits,
//    `ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73` for the signing primitives, and
//    `ws_objects/pfw.shared.pbl.src/retcode.sru` for the return-code catalogue - are citations in
//    comments and nothing more. No file is created, written, moved or deleted by anything here.
//
// ==================================================================================================

using System.Globalization;
using System.Text;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Xunit;
using Xunit.Sdk;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Asserts that both published OpenAPI documents load cleanly, validate against the pinned object
/// model's own default rule set, resolve every reference locally, and publish the cross-cutting
/// operations of contracts C-01, C-09 and C-10 with the intended anonymous-versus-authenticated split.
/// </summary>
/// <remarks>
/// <para>
/// The two documents and their reader diagnostics arrive from
/// <see cref="OpenApiContractDocuments"/>, the assembly-scoped fixture registered once in
/// <c>ContractTestContext.cs</c>. Taking a constructor parameter of that type is the documented
/// convention for this folder; this suite adds no second registration and no collection definition.
/// </para>
/// <para>
/// <b>Purity.</b> Every assertion reads the already-parsed fixture. There is no clock, no randomness,
/// no environment variable, no network, no host and no I/O of any kind in this file - the fixture owns
/// the only file access in the project, and it is read-only. Nothing here is awaited either, which is
/// why no <see cref="TestContext.Current"/> cancellation token appears: an assertion over an
/// in-memory model has nothing to cancel.
/// </para>
/// </remarks>
/// <param name="documents">
/// The assembly-scoped fixture holding both parsed documents, their reader diagnostics and the
/// absolute path each was read from.
/// </param>
public sealed class OpenApiDocumentValidationTests(OpenApiContractDocuments documents)
{
    // ==============================================================================================
    //  EXPECTATIONS
    //
    //  Every value below was READ FROM THE DOCUMENTS rather than imposed on them. The specification
    //  version is the clearest case: both documents declare `openapi: 3.1.0`, so 3.1 is what is
    //  asserted. Had they declared 3.0 this constant would say 3.0, because the job of this suite is
    //  to hold the documents to what they claim - not to move them to a version somebody preferred.
    // ==============================================================================================

    /// <summary>
    /// The specification version both documents declare, and therefore the version the reader must
    /// resolve them as.
    /// </summary>
    /// <remarks>
    /// Asserting the RESOLVED version rather than grepping the <c>openapi:</c> string matters, because
    /// the resolved value is what governs how every construct in the document is interpreted - and
    /// 3.1 is not a cosmetic increment over 3.0. It aligns with JSON Schema, which is why the sibling
    /// suite separately forbids the 3.0-only <c>nullable</c> keyword: a document that declared 3.1 and
    /// used <c>nullable: true</c> would still resolve as 3.1 here while every consumer silently read
    /// "must be a string" where "may be null" was meant.
    /// </remarks>
    private const OpenApiSpecVersion DeclaredSpecificationVersion = OpenApiSpecVersion.OpenApi3_1;

    /// <summary>
    /// The format the reader must report. This is the assertion that proves the YAML reader from the
    /// lock-stepped companion package was the reader actually used.
    /// </summary>
    private const string DeclaredFormat = "yaml";

    /// <summary>The bearer scheme both documents declare and default to. Contract C-01, constraint C-G.</summary>
    private const string BearerSchemeName = "bearerAuth";

    /// <summary>
    /// The mutual-TLS scheme <c>security.v1.yaml</c> declares for token issuance alone.
    /// </summary>
    /// <remarks>
    /// AAP 0.6.6.3 names mutual TLS as the documented fallback for a pair where a token issuer is
    /// inappropriate, and adds certificate and key path settings for THAT PAIR ONLY. Token issuance is
    /// exactly that pair, for a reason no configuration choice can remove: a caller cannot present a
    /// token in order to obtain its first token.
    /// </remarks>
    private const string MutualTlsSchemeName = "mutualTls";

    /// <summary>RFC 9457 problem details - the single error media type across both documents.</summary>
    private const string ProblemDetailsMediaType = "application/problem+json";

    /// <summary>The component schema name both documents use for that media type.</summary>
    private const string ProblemDetailsSchemaName = "ProblemDetails";

    /// <summary>
    /// The extension member carrying the originating legacy return code, from
    /// <c>ws_objects/pfw.shared.pbl.src/retcode.sru</c>, on every error body.
    /// </summary>
    /// <remarks>
    /// This is the thread that ties the REST surface back to the legacy algebra. An HTTP status says a
    /// request failed; <c>retCode</c> says WHICH legacy validation rejected it, so a consumer can tell
    /// <c>E_INVALID_ARGUMENT</c> from a column-expression parse failure where HTTP offers only "400".
    /// </remarks>
    private const string LegacyReturnCodeMember = "retCode";

    private const string UnauthorizedStatus = "401";

    private const string HealthRoute = "/health";
    private const string PingRoute = "/v1/ping";
    private const string CapabilitiesRoute = "/v1/capabilities";
    private const string TokenIssuanceRoute = "/v1/tokens";
    private const string JsonWebKeySetRoute = "/.well-known/jwks.json";
    private const string DiscoveryRoute = "/.well-known/openid-configuration";

    /// <summary>How many located diagnostics a failure message lists before truncating.</summary>
    /// <remarks>
    /// Capped because one broken reference can produce dozens of sites - the measured broken-<c>$ref</c>
    /// experiment produced 41 - and a message that printed every one would bury the first, which is the
    /// only one a reader needs in order to start.
    /// </remarks>
    private const int MaxReportedFindings = 15;

    /// <summary>
    /// The two published documents, as theory rows. Both file names come from the fixture's own
    /// constants so a rename cannot leave this suite asserting over a name that no longer exists.
    /// </summary>
    public static TheoryData<string> BothDocuments =>
    [
        OpenApiContractDocuments.GatewayDocumentFileName,
        OpenApiContractDocuments.SecurityDocumentFileName,
    ];

    /// <summary>
    /// One published document, paired with the diagnostic the reader produced for it and the absolute
    /// path it was read from.
    /// </summary>
    /// <remarks>
    /// The path is carried purely so that every failure message in this file can name the file on disk
    /// that has to be edited. A validation failure that reports a JSON pointer but not which of two
    /// documents it belongs to costs the reader a search; one that reports neither is nearly useless.
    /// </remarks>
    private readonly record struct ContractDocument(
        string FileName,
        string AbsolutePath,
        OpenApiDocument Document,
        OpenApiDiagnostic Diagnostic);

    /// <summary>
    /// Resolves a theory row's file name to the fixture's parsed document, diagnostic and path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Theory rows carry a FILE NAME rather than a document because xunit serialises theory data, and
    /// because a row labelled <c>gateway.v1.yaml</c> in the test output says which document failed
    /// without the reader opening anything.
    /// </para>
    /// <para>
    /// <b>Nullability, handled rather than suppressed (constraint C-H).</b>
    /// <c>ReadResult.Document</c> and <c>ReadResult.Diagnostic</c> are both nullable, and the fixture
    /// deals with that at the point of parsing: it fails with a message naming the path when the reader
    /// returns no document, and it fails naming BOTH attempted paths when a document cannot be located.
    /// Its properties are therefore non-nullable by contract, and reading them here is what surfaces
    /// either failure as a located assertion failure instead of a
    /// <see cref="NullReferenceException"/>. No <c>!</c> operator and no suppression appears in this
    /// file as a result.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The file name is not one of the two published documents, which is a defect in the calling
    /// theory rather than a finding about a contract - hence an argument exception rather than an
    /// assertion failure.
    /// </exception>
    private ContractDocument Resolve(string fileName) => fileName switch
    {
        OpenApiContractDocuments.GatewayDocumentFileName => new ContractDocument(
            fileName,
            documents.GatewayDocumentPath,
            documents.Gateway,
            documents.GatewayDiagnostic),

        OpenApiContractDocuments.SecurityDocumentFileName => new ContractDocument(
            fileName,
            documents.SecurityDocumentPath,
            documents.Security,
            documents.SecurityDiagnostic),

        _ => throw new ArgumentOutOfRangeException(
            nameof(fileName),
            fileName,
            "Only the two published contract documents can be resolved: "
                + $"'{OpenApiContractDocuments.GatewayDocumentFileName}' and "
                + $"'{OpenApiContractDocuments.SecurityDocumentFileName}'."),
    };

    // ==============================================================================================
    //  PHASE 1 - BOTH DOCUMENTS LOAD
    //
    //  Everything downstream of here assumes a document exists and parsed cleanly. These rows are
    //  what make that assumption checked rather than hoped for, and they are deliberately the first
    //  thing in the file: when a document is malformed, a reader wants THIS failure, not the twelve
    //  consequential ones.
    // ==============================================================================================

    [Theory]
    [MemberData(nameof(BothDocuments))]
    public void EachDocumentIsLoadedByTheFixtureAsAUsableDocument(string fileName)
    {
        ContractDocument contract = Resolve(fileName);

        // THE EXPLICIT NULL CHECK IS NOT REDUNDANT, IT IS THE CONTRACT BOUNDARY.
        //
        // The fixture's properties are non-nullable today because it converts a null ReadResult.Document
        // into a located failure at parse time. Asserting non-null here keeps that guarantee checked
        // from the consuming side, so loosening the fixture to hand back a nullable document would fail
        // in this row rather than as a NullReferenceException inside an unrelated assertion.
        Assert.NotNull(contract.Document);
        Assert.NotNull(contract.Diagnostic);

        // A DOCUMENT THAT PARSED TO NOTHING IS NOT A DOCUMENT.
        //
        // An empty YAML mapping parses successfully into a model with no paths and no components. Both
        // of these contracts publish operations, so "loaded" has to mean "loaded with content" - the
        // exact counts belong to the sibling suites, but non-emptiness belongs here, because every
        // later row in this file would pass vacuously over an empty document.
        Assert.NotEmpty(contract.Document.Paths);
        Required(contract.Document.Components, contract, "components section");

        // AND IT MUST BE THE DOCUMENT THIS ROW ASKED FOR.
        //
        // The fixture exposes Gateway and Security through two property pairs. Crossing them - handing
        // back the security document under the gateway path, or the reverse - would leave every
        // structural assertion in Phase 3 looking for gateway routes in the security document, and the
        // resulting failure would read as a missing route rather than as a wiring mistake. Checking the
        // path the fixture actually read from ends that ambiguity in one line.
        Assert.EndsWith(fileName, contract.AbsolutePath, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(BothDocuments))]
    public void EachDocumentLoadsWithNoReaderError(string fileName)
    {
        ContractDocument contract = Resolve(fileName);

        if (contract.Diagnostic.Errors.Count > 0)
        {
            Assert.Fail(Describe(contract, "reader error", contract.Diagnostic.Errors));
        }
    }

    [Theory]
    [MemberData(nameof(BothDocuments))]
    public void EachDocumentLoadsWithNoReaderWarning(string fileName)
    {
        ContractDocument contract = Resolve(fileName);

        // ZERO IS ASSERTED, AND ZERO IS THE MEASURED TRUTH FOR BOTH DOCUMENTS.
        //
        // The instruction for this file was to prefer zero warnings, and to assert an EXACT expected
        // set with a written reason for each accepted one only if the documents legitimately produced
        // any. They produce none - measured on this toolchain, both documents, zero errors and zero
        // warnings - so the exact expected set IS the empty set and no exception list exists to
        // justify. The collection is never blanket-ignored.
        //
        // Warnings are not cosmetic in an OpenAPI diagnostic, which is why this is a row of its own
        // rather than a footnote: a $ref pointing at a component that does not exist, and a construct
        // the declared specification version does not support, both surface as WARNINGS. A document
        // carrying either still parses, and a consumer's generator then either skips the affected
        // operation or emits something wrong - a defect that reaches the consumer and never the author.
        if (contract.Diagnostic.Warnings.Count > 0)
        {
            Assert.Fail(Describe(contract, "reader warning", contract.Diagnostic.Warnings));
        }
    }

    [Theory]
    [MemberData(nameof(BothDocuments))]
    public void EachDocumentParsesAsTheSpecificationVersionAndFormatItDeclares(string fileName)
    {
        ContractDocument contract = Resolve(fileName);

        Assert.Equal(DeclaredSpecificationVersion, contract.Diagnostic.SpecificationVersion);

        // THE FORMAT ASSERTION IS THE YAML READER'S REGRESSION GUARD.
        //
        // `Microsoft.OpenApi` 2.12.0 registers exactly one reader out of the box, `json`. The fixture
        // calls AddYamlReader() from the lock-stepped companion package, which takes the registration
        // to three - `json`, `yaml`, `yml`. Asserting the reported format is `yaml` is how this suite
        // confirms the companion package is present and was the reader used, rather than inferring it
        // from the fact that parsing did not throw.
        Assert.Equal(DeclaredFormat, contract.Diagnostic.Format, StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(BothDocuments))]
    public void EachDocumentDeclaresANonEmptyTitleAndVersion(string fileName)
    {
        ContractDocument contract = Resolve(fileName);

        // THE MINIMUM IDENTITY OF A PUBLISHED CONTRACT.
        //
        // Title and version are what a generated client is named and versioned from. Present-and-
        // non-empty is asserted rather than an exact string, because the exact wording is the sibling
        // suite's subject and pinning it twice would make a harmless editorial change fail two files.
        OpenApiInfo info = Required(contract.Document.Info, contract, "info section");

        Assert.False(
            string.IsNullOrWhiteSpace(info.Title),
            $"'{contract.FileName}' declares no info.title. Read from '{contract.AbsolutePath}'.");

        Assert.False(
            string.IsNullOrWhiteSpace(info.Version),
            $"'{contract.FileName}' declares no info.version. Read from '{contract.AbsolutePath}'.");
    }

    // ==============================================================================================
    //  PHASE 2 - BOTH DOCUMENTS VALIDATE
    //
    //  Four rows, and they are not interchangeable. The first runs the model's own rules. The second
    //  proves those rules exist, because a rule set that emptied itself would make the first pass
    //  forever. The third and fourth cover the two faults the rules provably DO NOT catch under this
    //  pin, and both were established by measurement rather than assumed - see the file header.
    // ==============================================================================================

    [Theory]
    [MemberData(nameof(BothDocuments))]
    public void EachDocumentValidatesAgainstTheModelsOwnDefaultRuleSet(string fileName)
    {
        ContractDocument contract = Resolve(fileName);

        // `Microsoft.OpenApi.OpenApiElementExtensions.Validate(IOpenApiElement, ValidationRuleSet)`.
        //
        // GetDefaultRuleSet(), NEVER GetEmptyRuleSet(). The default set is the object model's own
        // 21 rules across 15 element types - InfoRequiredFields, ResponseRequiredFields,
        // ResponsesMustContainAtLeastOneResponse, PathNameMustBeginWithSlash, PathMustBeUnique and the
        // rest. Using the model's rules rather than a hand-written set is constraint C-B applied here:
        // a bespoke rule set would be a house style, and enforcing one would demand the documents be
        // rewritten to satisfy a preference nobody asked for.
        List<OpenApiError> findings = contract.Document
            .Validate(ValidationRuleSet.GetDefaultRuleSet())
            .ToList();

        // THE WHOLE RETURNED SET IS ASSERTED EMPTY, NOT ONLY THE ERRORS.
        //
        // Validate() returns OpenApiValidatorError and OpenApiValidatorWarning through one
        // IEnumerable<OpenApiError>. Errors are the required assertion; warnings are asserted too
        // because a validator warning on a PUBLISHED contract is a defect rather than a note - the
        // reader of this document is a consumer generating a client from it, and a warning is exactly
        // the class of finding that produces a client which compiles and behaves wrongly. Both are
        // measured at zero for both documents, so this costs nothing today and catches a regression
        // that would otherwise be invisible. The failure message partitions by category so the
        // distinction is never lost in the report.
        if (findings.Count > 0)
        {
            Assert.Fail(DescribeValidation(contract, findings));
        }
    }

    [Fact]
    public void TheDefaultRuleSetCarriesTheRulesTheseAssertionsDependOn()
    {
        // THIS IS THE GUARD ON THE GUARD, AND IT IS THE MOST IMPORTANT ROW IN THE FILE.
        //
        // Every validation row above is only as strong as the rule set it runs. Swapping
        // GetDefaultRuleSet() for GetEmptyRuleSet() - a one-word edit that reads as a harmless
        // simplification - makes those rows pass for a document that is provably broken. Measured:
        // a gateway document with a response `description` deleted reports one located
        // ResponseRequiredFields error under the default set and NOTHING under the empty set.
        //
        // So the distinction is asserted here permanently rather than checked once by hand.
        ValidationRuleSet defaultRules = ValidationRuleSet.GetDefaultRuleSet();
        ValidationRuleSet emptyRules = ValidationRuleSet.GetEmptyRuleSet();

        Assert.NotEmpty(defaultRules.Rules);

        // The empty set is the contrast that gives the assertion above its meaning: it must genuinely
        // be empty, because if BOTH sets carried rules there would be nothing to distinguish and the
        // reasoning behind "never validate with the empty set" would be unfounded.
        Assert.Empty(emptyRules.Rules);

        // NAMED RULES, NOT JUST A COUNT.
        //
        // A count would be satisfied by 21 rules that check nothing this suite relies on. These four
        // are the ones whose absence would silently weaken the rows above: response and info required
        // fields, at-least-one-response, and the reference rule. `OpenApiDocumentReferencesAreValid` is
        // included deliberately even though it demonstrably does NOT catch a dangling local $ref under
        // this pin - if a future version makes it fire, the dedicated walk below becomes a second line
        // of defence rather than the only one, and this row records which rules were present when that
        // measurement was taken.
        //
        // The exact rule COUNT is not asserted: it is a property of the pinned package rather than of
        // these documents, and pinning it would turn an unrelated upstream addition into a failure in
        // a file about contract documents.
        string[] expected =
        [
            "InfoRequiredFields",
            "OpenApiDocumentReferencesAreValid",
            "ResponseRequiredFields",
            "ResponsesMustContainAtLeastOneResponse",
        ];

        string[] actual = defaultRules.Rules
            .Select(static rule => rule.Name)
            .ToArray();

        foreach (string ruleName in expected)
        {
            Assert.Contains(ruleName, actual, StringComparer.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(BothDocuments))]
    public void NoNonQueryParameterCarriesAllowReserved(string fileName)
    {
        ContractDocument contract = Resolve(fileName);

        // A THIRD FAULT THE DEFAULT RULE SET PROVABLY DOES NOT CATCH, AND IT REACHED A PUBLISHED
        // DOCUMENT.
        //
        // OpenAPI 3.1 defines `allowReserved` for `in: query` parameters ONLY - it says whether RFC 3986
        // reserved characters may appear unescaped in a query value. On any other parameter location the
        // field is not part of the specification, so a document carrying one fails third-party 3.1
        // validation even though `Microsoft.OpenApi`'s default rule set reports nothing: measured, the
        // 21-rule default set has no parameter-location rule at all, which is why this row exists rather
        // than being left to `EachDocumentValidatesAgainstTheModelsOwnDefaultRuleSet`.
        //
        // WHAT ACTUALLY HAPPENED, recorded so the row is not mistaken for a hypothetical. gateway.v1.yaml
        // set `allowReserved: true` on its `ReservedPath` PATH parameter as a way of saying "the captured
        // value may itself contain '/'", which is how ASP.NET Core's `{**path}` catch-all behaves. It did
        // not work in either direction: the document stopped validating, and a path parameter still
        // matches a single segment whatever that field says, so the intended meaning was never expressed.
        // The behaviour now lives in an `x-catch-all` vendor extension on the same parameter - a vendor
        // extension is the conformant way to say something the specification has no field for - and the
        // Gateway runtime's own generated parameter carries the identical extension
        // (Endpoints/DeferredCapabilityEndpoints.cs, decision D2).
        //
        // Every parameter site is walked: components, path-level and operation-level, in both documents.
        List<string> offenders = [];

        OpenApiComponents? components = contract.Document.Components;

        if (components?.Parameters is { Count: > 0 } componentParameters)
        {
            foreach ((string name, IOpenApiParameter parameter) in componentParameters)
            {
                Collect(offenders, $"#/components/parameters/{name}", parameter);
            }
        }

        if (contract.Document.Paths is { Count: > 0 } paths)
        {
            foreach ((string route, IOpenApiPathItem pathItem) in paths)
            {
                if (pathItem.Parameters is { Count: > 0 } pathParameters)
                {
                    foreach (IOpenApiParameter parameter in pathParameters)
                    {
                        Collect(offenders, $"{route} (path-level)", parameter);
                    }
                }

                if (pathItem.Operations is not { Count: > 0 } operations)
                {
                    continue;
                }

                foreach ((HttpMethod method, OpenApiOperation operation) in operations)
                {
                    if (operation.Parameters is not { Count: > 0 } operationParameters)
                    {
                        continue;
                    }

                    foreach (IOpenApiParameter parameter in operationParameters)
                    {
                        Collect(offenders, $"{method} {route}", parameter);
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"'{contract.FileName}' carries 'allowReserved' on {offenders.Count} parameter(s) whose "
                + "location is not 'query'. OpenAPI 3.1 defines that field for query parameters only, so "
                + "the document fails 3.1 validation. If the intent is to describe multi-segment "
                + "catch-all matching, say it in an 'x-catch-all' vendor extension - a path parameter "
                + $"matches one segment whatever 'allowReserved' says. Sites: "
                + $"{string.Join("; ", offenders)}. Read from '{contract.AbsolutePath}'.");

        static void Collect(List<string> offenders, string site, IOpenApiParameter parameter)
        {
            if (parameter.AllowReserved && parameter.In != ParameterLocation.Query)
            {
                offenders.Add($"{site} -> '{parameter.Name}' (in: {parameter.In?.ToString() ?? "<unset>"})");
            }
        }
    }

    [Theory]
    [MemberData(nameof(BothDocuments))]
    public void NoReferenceInEitherDocumentIsLeftUnresolved(string fileName)
    {
        ContractDocument contract = Resolve(fileName);

        IReadOnlyList<ReferenceSite> sites = CollectReferences(contract.Document);

        // A WALK THAT SAW NOTHING WOULD PASS WHILE PROVING NOTHING.
        //
        // Both documents delegate heavily to their components sections - measured at 438 reference
        // sites in gateway.v1.yaml and 197 in security.v1.yaml - so an empty walk means the traversal
        // stopped seeing references, not that the documents stopped using them.
        Assert.NotEmpty(sites);

        // AND IT MUST STILL BE SEEING EVERY KIND OF REFERENCE THE DOCUMENTS USE.
        //
        // Non-emptiness alone would survive a model change that narrowed the walk to one category. Both
        // documents use all four of these kinds, so requiring one site of each is what keeps the walk
        // honest as the object model evolves. Parameter references are excluded from the required set
        // deliberately: gateway.v1.yaml has seven and security.v1.yaml has none, so demanding one would
        // fail the security row for a difference that is correct.
        string[] requiredKinds =
        [
            nameof(OpenApiResponseReference),
            nameof(OpenApiSchemaReference),
            nameof(OpenApiSecuritySchemeReference),
            nameof(OpenApiTagReference),
        ];

        string[] observedKinds = sites
            .Select(static site => site.Kind)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (string kind in requiredKinds)
        {
            Assert.Contains(kind, observedKinds, StringComparer.Ordinal);
        }

        // AND IT MUST REACH THE COMPONENTS SECTION, NOT ONLY THE PATHS.
        //
        // This is the part that is easy to assume and worth measuring. Component schemas reference each
        // other - a problem-details schema whose extension member is itself a schema reference, a
        // conflict schema composed with allOf out of two others - so a walk confined to `#/paths` would
        // check the majority of sites and miss the composition layer entirely. Measured: of 438 sites in
        // gateway.v1.yaml, 376 are under `#/paths`, 61 under `#/components` and 1 under `#/security`;
        // security.v1.yaml splits 141 / 55 / 1. Requiring a site in each of the three regions is what
        // keeps all three covered.
        string[] requiredRegions = ["#/paths", "#/components", "#/security"];

        foreach (string region in requiredRegions)
        {
            Assert.Contains(
                sites,
                site => site.Pointer.StartsWith(region, StringComparison.Ordinal));
        }

        // EVERY REFERENCE HOLDER MUST BE ONE THIS SUITE UNDERSTANDS.
        //
        // The inspection below switches over the concrete reference types the object model publishes. A
        // holder it does not recognise cannot be checked for resolution, so it is reported as a finding
        // rather than skipped - an unrecognised holder means the model gained a reference kind and this
        // file needs a new arm, which is worth a loud failure and not a silent gap in coverage.
        ReferenceSite[] unrecognised = sites
            .Where(static site => !site.Recognised)
            .ToArray();

        if (unrecognised.Length > 0)
        {
            Assert.Fail(DescribeReferences(
                contract,
                "reference holder of a kind this suite does not know how to inspect",
                unrecognised));
        }

        // THE ACTUAL ASSERTION - AND THE ONLY MECHANISM THAT CAN MAKE IT.
        //
        // Measured on this pin: repointing a component $ref at a name that does not exist produces NO
        // reader error, NO reader warning and NO validator error. It surfaces exclusively as
        // UnresolvedReference == true with a null Target. Both halves are checked because they are
        // separate signals on separate members: the flag is what the model reports, and the null target
        // is what a consumer would actually trip over. Measured together at 41 sites out of 438 in the
        // deliberate-breakage experiment, and at zero in both real documents.
        ReferenceSite[] dangling = sites
            .Where(static site => site.Unresolved || site.TargetMissing)
            .ToArray();

        if (dangling.Length > 0)
        {
            Assert.Fail(DescribeReferences(contract, "unresolved $ref", dangling));
        }
    }

    [Theory]
    [MemberData(nameof(BothDocuments))]
    public void NoReferenceInEitherDocumentReachesOutsideTheDocument(string fileName)
    {
        ContractDocument contract = Resolve(fileName);

        // THIS IS THE OFFLINE GUARANTEE, MADE STRUCTURAL.
        //
        // The fixture pins loading offline - LoadExternalRefs is set to false explicitly and no
        // HttpClient is supplied - but that is a setting on an object this suite cannot see, and
        // reconstructing the same settings here to assert over them would be asserting over its own
        // object rather than the fixture's. Measured, too: an external $ref under LoadExternalRefs
        // false produces no diagnostic at all, so there is nothing in the reader's output to assert on
        // either.
        //
        // What CAN be asserted is stronger than the setting anyway. If no reference in either document
        // is external, then no load of either document can reach the network REGARDLESS of how a reader
        // is configured - the repeatability holds structurally rather than by configuration. And
        // repeatability is not a preference here: it is the one hard prerequisite of the Golden-Master
        // characterization approach this repository adopts (AAP 0.6.7), and CI carries no network
        // guarantee.
        ReferenceSite[] external = CollectReferences(contract.Document)
            .Where(static site => site.External || !string.IsNullOrEmpty(site.ExternalResource))
            .ToArray();

        if (external.Length > 0)
        {
            Assert.Fail(DescribeReferences(contract, "external $ref", external));
        }
    }

    // ==============================================================================================
    //  PHASE 3 - THE MINIMUM STRUCTURAL CONTRACT OF EACH DOCUMENT
    //
    //  DELIBERATELY SMALL. The sibling suites own the deep shape assertions - GatewayContractTests
    //  alone carries the route inventory, the status mapping, the capability bit table and the reserved
    //  routes. What is asserted here is only the two things Phase 3 exists for: that the RIGHT
    //  documents were loaded, and that the cross-cutting contracts C-01 and C-10 are present with the
    //  authentication posture C-G requires.
    // ==============================================================================================

    [Fact]
    public void TheGatewayDocumentPublishesTheThreeCrossCuttingOperations()
    {
        ContractDocument contract = Resolve(OpenApiContractDocuments.GatewayDocumentFileName);

        // C-10 - `/health`, ANONYMOUS.
        //
        // Anonymous on all four services, and Gateway reports healthy only after Persistence,
        // DataServices and Security all do. The anonymity is not a convenience: the thing that probes
        // this endpoint - an orchestrator, a load balancer, an operator - holds no token, and it probes
        // precisely during the window in which the service is still starting. Requiring a token would
        // make readiness depend on Security's issuance already being live, which is a circular
        // dependency that cannot resolve during a cold start.
        OpenApiOperation health = RequireOperation(contract, HealthRoute, HttpMethod.Get);
        AssertAnonymous(contract, HealthRoute, HttpMethod.Get, health);

        // C-10 - `/v1/ping`, TOKEN REQUIRED, AND ITS 401 IS PART OF THE PUBLISHED CONTRACT.
        //
        // AAP 0.3.2.2 and constraint C-G: /v1/ping requires a JWT on all four services and returns 401
        // without one. Both halves are asserted because both are the contract - the requirement makes
        // the boundary authenticated, and the published 401 is what makes that testable rather than
        // merely asserted. The legacy opens no listening socket and receives no unsolicited request, so
        // decomposition creates every boundary in this system from nothing; this operation is the
        // standing proof that each one is closed.
        OpenApiOperation ping = RequireOperation(contract, PingRoute, HttpMethod.Get);
        AssertRequiresScheme(contract, PingRoute, HttpMethod.Get, ping, BearerSchemeName);
        AssertDeclaresResponse(contract, PingRoute, HttpMethod.Get, ping, UnauthorizedStatus);

        // C-09 - `/v1/capabilities`.
        //
        // The projection of the framework's eight-bit capability gate from
        // ws_objects/pfw.shared.pbl.src/enums.sru:L41-L49, which the gateway document cites by that
        // exact locator in its own description. Presence only is asserted here; the bit values, the
        // seven-term INIT_FLAG_ENABLE_ALL sum that omits BLINKFAST, and the in-scope mapping are
        // GatewayContractTests' subject.
        RequireOperation(contract, CapabilitiesRoute, HttpMethod.Get);

        // The scheme the document defaults to must actually be declared, or every requirement above
        // names something that does not exist.
        AssertSchemeIsDeclared(contract, BearerSchemeName);
    }

    [Fact]
    public void TheSecurityDocumentPublishesTheSoleIssuerSurface()
    {
        ContractDocument contract = Resolve(OpenApiContractDocuments.SecurityDocumentFileName);

        // C-01 - `POST /v1/tokens`, THE SOLE MINTER IN THE SYSTEM.
        //
        // Security issues; Gateway, DataServices and Persistence hold verification material only. The
        // legacy already contains the signing primitives this service owns -
        // ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73 declares RSASign and VerifyRSASign - which
        // is why the cryptographic surface and the token authority land on the same service.
        //
        // IT IS PROTECTED BY MUTUAL TLS, NOT BY THE BEARER SCHEME, AND THAT IS THE CONTRACT.
        //
        // This is the single operation in either document that overrides the document-level bearer
        // default with a DIFFERENT scheme rather than with anonymity, and it is not an omission: a
        // caller cannot present a token in order to obtain its first token. Something other than a
        // token has to establish identity at that one edge, and AAP 0.6.6.3 names mutual TLS as the
        // documented fallback for exactly the pair where a token issuer is inappropriate - applying to
        // that pair only, with tokens remaining the default on every other edge. Asserting the scheme
        // by NAME is what distinguishes this from the anonymous case: the operation is authenticated,
        // and the row would fail just as loudly if the override became `security: []`.
        OpenApiOperation issueToken = RequireOperation(contract, TokenIssuanceRoute, HttpMethod.Post);
        AssertRequiresScheme(contract, TokenIssuanceRoute, HttpMethod.Post, issueToken, MutualTlsSchemeName);
        AssertDeclaresResponse(contract, TokenIssuanceRoute, HttpMethod.Post, issueToken, UnauthorizedStatus);

        // C-01 - THE TWO ANONYMOUS PUBLICATIONS.
        //
        // Verification material is PUBLIC by design - that is precisely what distinguishes it from a
        // signing key - so both publications are anonymous. The discovery document is the reason this
        // contract is REST rather than gRPC: a consumer's stock JwtBearer handler self-configures from
        // it and fetches the key set itself, with zero bespoke code. gRPC would have forced a
        // hand-written key-set retrieval path into three separate services, which is a net INCREASE in
        // hand-written security code - the opposite of what the requirement asks for.
        OpenApiOperation jwks = RequireOperation(contract, JsonWebKeySetRoute, HttpMethod.Get);
        AssertAnonymous(contract, JsonWebKeySetRoute, HttpMethod.Get, jwks);

        OpenApiOperation discovery = RequireOperation(contract, DiscoveryRoute, HttpMethod.Get);
        AssertAnonymous(contract, DiscoveryRoute, HttpMethod.Get, discovery);

        // C-10 - the same health and ping pair as Gateway, because an internal edge is a created
        // boundary too and the uniformity is part of the contract.
        OpenApiOperation health = RequireOperation(contract, HealthRoute, HttpMethod.Get);
        AssertAnonymous(contract, HealthRoute, HttpMethod.Get, health);

        OpenApiOperation ping = RequireOperation(contract, PingRoute, HttpMethod.Get);
        AssertRequiresScheme(contract, PingRoute, HttpMethod.Get, ping, BearerSchemeName);
        AssertDeclaresResponse(contract, PingRoute, HttpMethod.Get, ping, UnauthorizedStatus);

        AssertSchemeIsDeclared(contract, BearerSchemeName);
        AssertSchemeIsDeclared(contract, MutualTlsSchemeName);
    }

    [Theory]
    [MemberData(nameof(BothDocuments))]
    public void TheUnauthorizedBodyIsProblemDetailsCarryingTheLegacyReturnCode(string fileName)
    {
        ContractDocument contract = Resolve(fileName);

        // ONE ERROR SHAPE ACROSS BOTH DOCUMENTS, SO A CONSUMER WRITES ONE ERROR HANDLER.
        //
        // RFC 9457 problem details under application/problem+json, which is also what ASP.NET Core
        // Minimal APIs emit without hand-written code - the same reasoning that leaves token validation
        // to the stock bearer handler.
        //
        // This row reaches the body THROUGH the 401's $ref rather than through the components section
        // directly, deliberately: both documents declare that response once as a component and reference
        // it from every authenticated operation, so navigating the reference proves the delegation
        // actually resolves to a real body. It is a targeted, named resolution proof to sit alongside
        // the whole-document walk above.
        OpenApiOperation ping = RequireOperation(contract, PingRoute, HttpMethod.Get);
        IOpenApiResponse unauthorized = RequireResponse(contract, PingRoute, HttpMethod.Get, ping, UnauthorizedStatus);

        IDictionary<string, OpenApiMediaType> content = Required(
            unauthorized.Content,
            contract,
            $"content on the {UnauthorizedStatus} response of GET {PingRoute}",
            "Both documents declare that response once as a component and reference it from every "
                + "authenticated operation, so a null body here is what a DANGLING $ref looks like from "
                + $"this side - check {nameof(NoReferenceInEitherDocumentIsLeftUnresolved)} first, which "
                + "reports the exact JSON pointer.");

        Assert.True(
            content.ContainsKey(ProblemDetailsMediaType),
            $"'{contract.FileName}' GET {PingRoute} -> {UnauthorizedStatus} does not use "
                + $"'{ProblemDetailsMediaType}'. Declared media types: "
                + $"{string.Join(", ", content.Keys)}. Read from '{contract.AbsolutePath}'.");

        IOpenApiSchema body = Required(
            content[ProblemDetailsMediaType].Schema,
            contract,
            $"schema on the {ProblemDetailsMediaType} body of GET {PingRoute} -> {UnauthorizedStatus}");

        // THE LEGACY RETURN CODE TRAVELS WITH EVERY ERROR.
        //
        // From ws_objects/pfw.shared.pbl.src/retcode.sru. Asserted on the schema reached through the
        // reference AND on the component definition, because those are two different claims: the first
        // says this operation's error body carries it, the second says the shared definition does. A
        // reference that resolved to some other schema would satisfy only one of them.
        IDictionary<string, IOpenApiSchema> bodyProperties = Required(
            body.Properties,
            contract,
            $"properties on the {UnauthorizedStatus} body schema of GET {PingRoute}");

        Assert.True(
            bodyProperties.ContainsKey(LegacyReturnCodeMember),
            $"The {UnauthorizedStatus} body of GET {PingRoute} in '{contract.FileName}' does not carry "
                + $"'{LegacyReturnCodeMember}', so the originating legacy return code would not survive "
                + $"the projection into HTTP. Read from '{contract.AbsolutePath}'.");

        IDictionary<string, IOpenApiSchema> schemas = Required(
            Required(contract.Document.Components, contract, "components section").Schemas,
            contract,
            "components.schemas section");

        Assert.True(
            schemas.TryGetValue(ProblemDetailsSchemaName, out IOpenApiSchema? declared),
            $"'{contract.FileName}' defines no '{ProblemDetailsSchemaName}' schema, so its error bodies "
                + $"have no shared shape. Declared schemas: {schemas.Count}. "
                + $"Read from '{contract.AbsolutePath}'.");

        IDictionary<string, IOpenApiSchema> declaredProperties = Required(
            Required(declared, contract, $"'{ProblemDetailsSchemaName}' schema").Properties,
            contract,
            $"properties on the '{ProblemDetailsSchemaName}' schema");

        Assert.True(
            declaredProperties.ContainsKey(LegacyReturnCodeMember),
            $"'{ProblemDetailsSchemaName}' in '{contract.FileName}' does not carry "
                + $"'{LegacyReturnCodeMember}'. Read from '{contract.AbsolutePath}'.");
    }

    [Theory]
    [MemberData(nameof(BothDocuments))]
    public void ExactlyTheDocumentedOperationsAreAnonymousAndEveryOtherOneNamesAScheme(string fileName)
    {
        ContractDocument contract = Resolve(fileName);

        // THIS ROW IS THE DOCUMENT-LEVEL EXPRESSION OF C-G.
        //
        // "No new attack surface" cannot mean "no new surface" literally - the legacy opens no listening
        // socket, registers no route and receives no unsolicited request, so decomposition creates this
        // system's first-ever ingress. The requirement therefore reads as: every newly created surface
        // is authenticated from the outset. At document level that becomes a closed-world claim, which
        // is why this is set EQUALITY rather than a membership check: asserting only that the known
        // anonymous operations are anonymous would let a fifth one be added and never be noticed.
        //
        // Both documents state the requirement once at document level and force each anonymous
        // operation to override it, so that an unauthenticated surface can only ever be created
        // DELIBERATELY and never by forgetting to add a requirement.
        // BOTH SIDES ARE SORTED THE SAME WAY, so the comparison is about MEMBERSHIP and never about the
        // order the expectations happen to be written in. Sorting only the actual side and hand-ordering
        // the expected side is a trap: this row failed on exactly that during development, reporting a
        // difference in position 0 between two identical sets because '/.well-known/jwks.json' sorts
        // before '/.well-known/openid-configuration' and the expectation had been written the other way
        // round. A test that can fail for a reason its subject does not care about is a test that will
        // eventually be "fixed" by weakening it.
        string[] expectedAnonymous = ExpectedAnonymousOperations(fileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] actualAnonymous = Operations(contract.Document)
            .Where(entry => RequiredSchemeNames(contract.Document, entry.Operation).Count == 0)
            .Select(static entry => FormatOperation(entry.Route, entry.Method))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedAnonymous, actualAnonymous, StringComparer.Ordinal);

        // AND EVERY OTHER OPERATION MUST NAME A SCHEME THAT ACTUALLY EXISTS.
        //
        // Non-empty is not sufficient on its own: a requirement naming a scheme absent from components
        // is unenforceable and most generators ignore it silently, which would leave an operation
        // documented as protected and generated as open. The scheme names in play are bearerAuth
        // throughout, plus mutualTls on token issuance alone.
        List<string> undeclared = [];

        foreach ((string route, HttpMethod method, OpenApiOperation operation) in Operations(contract.Document))
        {
            IReadOnlyList<string> schemes = RequiredSchemeNames(contract.Document, operation);

            foreach (string scheme in schemes)
            {
                if (contract.Document.Components?.SecuritySchemes?.ContainsKey(scheme) != true)
                {
                    undeclared.Add($"{FormatOperation(route, method)} requires '{scheme}'");
                }
            }
        }

        if (undeclared.Count > 0)
        {
            Assert.Fail(
                $"'{contract.FileName}' has {undeclared.Count} operation(s) requiring a security scheme "
                    + "that components.securitySchemes does not declare, which is unenforceable and is "
                    + "silently ignored by most generators: "
                    + string.Join("; ", undeclared.Take(MaxReportedFindings))
                    + $". Read from '{contract.AbsolutePath}'.");
        }
    }

    // ==============================================================================================
    //  EXPECTATIONS THAT DIFFER PER DOCUMENT
    // ==============================================================================================

    /// <summary>
    /// The complete set of operations permitted to be anonymous, per document, formatted as
    /// <c>METHOD route</c> and ordinally sorted so it can be compared as a set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four operations across the two documents, and each is anonymous for a reason that cannot be
    /// configured away:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>GET /health</c> on both - the probe holds no token and probes during startup, so requiring one
    /// would make readiness circular on a cold start (contract C-10).
    /// </description></item>
    /// <item><description>
    /// <c>GET /.well-known/jwks.json</c> - verification material is public by design; that is what
    /// distinguishes it from a signing key.
    /// </description></item>
    /// <item><description>
    /// <c>GET /.well-known/openid-configuration</c> - a stock bearer handler must be able to
    /// self-configure before it holds any credential at all.
    /// </description></item>
    /// </list>
    /// <para>
    /// <c>POST /v1/tokens</c> is deliberately NOT in this set. It overrides the document default with
    /// mutual TLS rather than with anonymity, so it is authenticated and must appear on the other side
    /// of the comparison.
    /// </para>
    /// </remarks>
    private static string[] ExpectedAnonymousOperations(string fileName) => fileName switch
    {
        OpenApiContractDocuments.GatewayDocumentFileName =>
        [
            FormatOperation(HealthRoute, HttpMethod.Get),
        ],

        OpenApiContractDocuments.SecurityDocumentFileName =>
        [
            // Written in the order they appear in the document, for readability. The caller sorts both
            // sides before comparing, so this order carries no meaning and must not be relied on.
            FormatOperation(JsonWebKeySetRoute, HttpMethod.Get),
            FormatOperation(DiscoveryRoute, HttpMethod.Get),
            FormatOperation(HealthRoute, HttpMethod.Get),
        ],

        _ => throw new ArgumentOutOfRangeException(
            nameof(fileName),
            fileName,
            "Only the two published contract documents have an expected anonymous set."),
    };

    // ==============================================================================================
    //  MODEL NAVIGATION
    //
    //  Every member here is a pure read over an already-parsed document. None of them asserts; the
    //  Require* members below do, and they are separated so that a navigation failure reports what was
    //  sought rather than throwing a KeyNotFoundException from inside an indexer.
    // ==============================================================================================

    /// <summary>Every (route, method, operation) triple in a document, flattened in declaration order.</summary>
    private static IEnumerable<(string Route, HttpMethod Method, OpenApiOperation Operation)> Operations(
        OpenApiDocument document)
    {
        foreach ((string route, IOpenApiPathItem pathItem) in document.Paths)
        {
            // A path item with no operations is legal in the specification and carries nothing to
            // assert, so it is skipped rather than treated as a fault. Neither document has one today.
            if (pathItem.Operations is null)
            {
                continue;
            }

            foreach ((HttpMethod method, OpenApiOperation operation) in pathItem.Operations)
            {
                yield return (route, method, operation);
            }
        }
    }

    /// <summary>
    /// The security scheme names an operation effectively requires: its own requirement when it declares
    /// one, otherwise the document-level default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The distinction that matters is null versus empty, and conflating them would invert the
    /// result.</b> A null <c>security</c> on an operation means "not stated, inherit the document
    /// default" - which for both of these documents means the bearer scheme. An EMPTY list means "no
    /// requirement applies", which is an explicit override to anonymous and must NOT fall back to the
    /// document default. Treating null as empty would report every authenticated operation as anonymous;
    /// treating empty as null would report every anonymous operation as protected. Both mistakes are
    /// silent, and each inverts exactly the property constraint C-G turns on.
    /// </para>
    /// <para>
    /// The specification also distinguishes <c>[]</c> from <c>[{}]</c>: both permit anonymous access, but
    /// only <c>[]</c> says so in the shape generators and policy checkers read. Both documents use
    /// <c>[]</c>, and this member returns an empty set for it.
    /// </para>
    /// <para>
    /// Requirement keys are <see cref="OpenApiSecuritySchemeReference"/>, so the scheme name is the
    /// reference id. A key whose id is missing is reported as <c>&lt;unnamed&gt;</c> rather than dropped,
    /// so it fails the declared-scheme check instead of silently reducing the requirement to nothing.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> RequiredSchemeNames(
        OpenApiDocument document,
        OpenApiOperation operation)
    {
        IList<OpenApiSecurityRequirement>? effective = operation.Security ?? document.Security;

        if (effective is null || effective.Count == 0)
        {
            return [];
        }

        return effective
            .SelectMany(static requirement =>
                requirement.Keys.Select(static key => key.Reference?.Id ?? "<unnamed>"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Formats one operation as <c>METHOD route</c> for comparison and for failure messages.</summary>
    private static string FormatOperation(string route, HttpMethod method) =>
        string.Create(CultureInfo.InvariantCulture, $"{method.Method} {route}");

    /// <summary>
    /// Returns a required part of a document, or fails naming the document, the part and the path it was
    /// read from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This exists because a bare <c>Assert.NotNull</c> reports "Value is null" and nothing else.</b>
    /// Which document? Which member? Read from where? During validation of this suite a deliberately
    /// broken <c>$ref</c> produced exactly that message from a null response body, and it was the one
    /// failure in the whole run that could not be acted on without opening the file - so every optional
    /// member this suite has to traverse now goes through here instead.
    /// </para>
    /// <para>
    /// Throwing <see cref="FailException"/> rather than calling <c>Assert.Fail</c> is deliberate and
    /// matches the convention <c>ContractTestContext.cs</c> already sets in this folder: it is the same
    /// exception <c>Assert.Fail</c> raises, so xunit reports it as an assertion failure rather than as an
    /// unexpected error, and because it is a <c>throw</c> the compiler's null-state analysis narrows the
    /// return value - which is what lets this file carry no null-forgiving <c>!</c> operator anywhere.
    /// </para>
    /// </remarks>
    /// <param name="value">The optional member being traversed.</param>
    /// <param name="contract">The document being asserted over, for the failure message.</param>
    /// <param name="what">What was expected, phrased as a noun so it reads inside the message.</param>
    /// <param name="hint">
    /// An optional second sentence pointing at the likely cause. Used where a null has a known
    /// explanation that is not obvious from the member itself - a null response <c>content</c>, for
    /// instance, is what a dangling <c>$ref</c> on that response looks like from here.
    /// </param>
    private static T Required<T>(T? value, ContractDocument contract, string what, string? hint = null)
        where T : class
    {
        if (value is null)
        {
            StringBuilder message = new();

            message.Append(CultureInfo.InvariantCulture,
                $"'{contract.FileName}' has no {what}, so there is nothing to assert over.");

            if (!string.IsNullOrEmpty(hint))
            {
                message.Append(CultureInfo.InvariantCulture, $" {hint}");
            }

            message.Append(CultureInfo.InvariantCulture, $" Read from '{contract.AbsolutePath}'.");

            throw FailException.ForFailure(message.ToString());
        }

        return value;
    }

    // ==============================================================================================
    //  ASSERTING NAVIGATION - each one names what was sought and where, and never dereferences blind
    // ==============================================================================================

    private static OpenApiOperation RequireOperation(
        ContractDocument contract,
        string route,
        HttpMethod method)
    {
        Assert.True(
            contract.Document.Paths.TryGetValue(route, out IOpenApiPathItem? pathItem),
            $"'{contract.FileName}' declares no path '{route}'. Declared paths: "
                + $"{string.Join(", ", contract.Document.Paths.Keys.Order(StringComparer.Ordinal).Take(MaxReportedFindings))}"
                + $". Read from '{contract.AbsolutePath}'.");

        IDictionary<HttpMethod, OpenApiOperation> operations = Required(
            Required(pathItem, contract, $"path item for '{route}'").Operations,
            contract,
            $"operation on path '{route}'");

        Assert.True(
            operations.TryGetValue(method, out OpenApiOperation? operation),
            $"'{contract.FileName}' declares path '{route}' but no {method.Method} operation on it. "
                + $"Declared methods: {string.Join(", ", operations.Keys.Select(static m => m.Method))}"
                + $". Read from '{contract.AbsolutePath}'.");

        return Required(operation, contract, $"{FormatOperation(route, method)} operation");
    }

    private static IOpenApiResponse RequireResponse(
        ContractDocument contract,
        string route,
        HttpMethod method,
        OpenApiOperation operation,
        string status)
    {
        OpenApiResponses responses = Required(
            operation.Responses,
            contract,
            $"responses on {FormatOperation(route, method)}");

        Assert.True(
            responses.TryGetValue(status, out IOpenApiResponse? response),
            $"'{contract.FileName}' {FormatOperation(route, method)} declares no '{status}' response. "
                + $"Declared: {string.Join(", ", responses.Keys.Order(StringComparer.Ordinal))}"
                + $". Read from '{contract.AbsolutePath}'.");

        return Required(
            response,
            contract,
            $"'{status}' response on {FormatOperation(route, method)}");
    }

    private static void AssertDeclaresResponse(
        ContractDocument contract,
        string route,
        HttpMethod method,
        OpenApiOperation operation,
        string status) =>
        RequireResponse(contract, route, method, operation, status);

    private static void AssertAnonymous(
        ContractDocument contract,
        string route,
        HttpMethod method,
        OpenApiOperation operation)
    {
        IReadOnlyList<string> schemes = RequiredSchemeNames(contract.Document, operation);

        Assert.True(
            schemes.Count == 0,
            $"'{contract.FileName}' {FormatOperation(route, method)} must be anonymous but requires "
                + $"[{string.Join(", ", schemes)}]. An operation goes anonymous by overriding the "
                + "document-level requirement with an empty list; omitting `security` inherits the "
                + $"document default instead. Read from '{contract.AbsolutePath}'.");
    }

    private static void AssertRequiresScheme(
        ContractDocument contract,
        string route,
        HttpMethod method,
        OpenApiOperation operation,
        string schemeName)
    {
        IReadOnlyList<string> schemes = RequiredSchemeNames(contract.Document, operation);

        Assert.True(
            schemes.Contains(schemeName, StringComparer.Ordinal),
            $"'{contract.FileName}' {FormatOperation(route, method)} must require the '{schemeName}' "
                + $"scheme but requires [{string.Join(", ", schemes)}]"
                + (schemes.Count == 0
                    ? " - it is anonymous, which for this operation is an open boundary rather than a "
                        + "simplification (constraint C-G)"
                    : string.Empty)
                + $". Read from '{contract.AbsolutePath}'.");
    }

    private static void AssertSchemeIsDeclared(ContractDocument contract, string schemeName)
    {
        IDictionary<string, IOpenApiSecurityScheme> schemes = Required(
            Required(contract.Document.Components, contract, "components section").SecuritySchemes,
            contract,
            "components.securitySchemes section");

        Assert.True(
            schemes.ContainsKey(schemeName),
            $"'{contract.FileName}' does not declare a '{schemeName}' security scheme in components. "
                + $"Declared: {string.Join(", ", schemes.Keys.Order(StringComparer.Ordinal))}"
                + $". Read from '{contract.AbsolutePath}'.");
    }

    // ==============================================================================================
    //  REFERENCE WALK
    //
    //  The mechanism the file header records: OpenApiWalker drives the traversal and the visitor below
    //  records one site per reference holder, carrying the JSON pointer the walker reports. This is the
    //  ONLY way an unresolved or external $ref can be observed under this pin - measured, not assumed.
    // ==============================================================================================

    /// <summary>One <c>$ref</c> occurrence in a document, with everything a failure message needs.</summary>
    /// <param name="Pointer">
    /// The JSON pointer the walker reports for the site, for example
    /// <c>#/paths/~1v1~1ping/get/responses/401</c>. This is the whole reason the walk is used rather
    /// than a hand-rolled recursion: a reference fault with no location is nearly useless.
    /// </param>
    /// <param name="Kind">The concrete reference-holder type name, so the report says WHAT was referenced.</param>
    /// <param name="Id">The reference id, normally the component name.</param>
    /// <param name="External">Whether the reference points outside this document.</param>
    /// <param name="ExternalResource">The external document, when there is one.</param>
    /// <param name="Unresolved">What the model itself reports about resolution.</param>
    /// <param name="TargetMissing">Whether the resolved target is absent - the fault a consumer trips over.</param>
    /// <param name="Recognised">
    /// Whether this suite knew how to inspect the holder. False means the object model published a
    /// reference kind that <c>Inspect</c> has no arm for, which is reported rather than skipped.
    /// </param>
    private readonly record struct ReferenceSite(
        string Pointer,
        string Kind,
        string? Id,
        bool External,
        string? ExternalResource,
        bool Unresolved,
        bool TargetMissing,
        bool Recognised);

    /// <summary>Walks a document and records every reference site in it.</summary>
    private static IReadOnlyList<ReferenceSite> CollectReferences(OpenApiDocument document)
    {
        ReferenceCollector collector = new();
        new OpenApiWalker(collector).Walk(document);

        return collector.Sites;
    }

    /// <summary>
    /// Records one <see cref="ReferenceSite"/> per reference holder the walker reaches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The base implementation is deliberately not called.</b> <see cref="OpenApiWalker"/> owns
    /// traversal and the visitor's methods are notification hooks, so there is nothing to delegate to -
    /// and every count quoted in this file was measured with this exact shape. Walking into a target
    /// from here would additionally risk non-termination on a recursive schema.
    /// </para>
    /// <para>
    /// The collector is stateful and therefore constructed per walk, never shared between rows.
    /// </para>
    /// </remarks>
    private sealed class ReferenceCollector : OpenApiVisitorBase
    {
        private readonly List<ReferenceSite> _sites = [];

        /// <summary>Every reference site seen, in traversal order.</summary>
        public IReadOnlyList<ReferenceSite> Sites => _sites;

        public override void Visit(IOpenApiReferenceHolder referenceHolder)
        {
            (BaseOpenApiReference? reference, bool targetMissing) = Inspect(referenceHolder);

            _sites.Add(new ReferenceSite(
                Pointer: PathString,
                Kind: referenceHolder.GetType().Name,
                Id: reference?.Id,
                External: reference?.IsExternal ?? false,
                ExternalResource: reference?.ExternalResource,
                Unresolved: referenceHolder.UnresolvedReference,
                TargetMissing: targetMissing,
                Recognised: reference is not null));
        }

        /// <summary>
        /// Reads the typed reference and target out of a holder.
        /// </summary>
        /// <remarks>
        /// <para>
        /// An explicit switch rather than reflection, because the arms are then compile-time checked
        /// against the pinned object model - which is the whole point of a suite that guards a version
        /// pin. <c>IOpenApiReferenceHolder</c> exposes only <c>UnresolvedReference</c>; the reference and
        /// target live on the generic interfaces, whose type argument differs per holder
        /// (<c>JsonSchemaReference</c> for a schema, <c>OpenApiReferenceWithDescription</c> for most
        /// others, <c>BaseOpenApiReference</c> for a tag), so no single pattern can reach them.
        /// </para>
        /// <para>
        /// The default arm returns a null reference, which the caller reports as an unrecognised holder
        /// rather than as a pass. A silent skip there would quietly stop checking a whole reference kind
        /// the day the model added one.
        /// </para>
        /// </remarks>
        private static (BaseOpenApiReference? Reference, bool TargetMissing) Inspect(
            IOpenApiReferenceHolder holder) => holder switch
            {
                OpenApiSchemaReference reference => (reference.Reference, reference.Target is null),
                OpenApiResponseReference reference => (reference.Reference, reference.Target is null),
                OpenApiParameterReference reference => (reference.Reference, reference.Target is null),
                OpenApiRequestBodyReference reference => (reference.Reference, reference.Target is null),
                OpenApiHeaderReference reference => (reference.Reference, reference.Target is null),
                OpenApiExampleReference reference => (reference.Reference, reference.Target is null),
                OpenApiLinkReference reference => (reference.Reference, reference.Target is null),
                OpenApiCallbackReference reference => (reference.Reference, reference.Target is null),
                OpenApiPathItemReference reference => (reference.Reference, reference.Target is null),
                OpenApiSecuritySchemeReference reference => (reference.Reference, reference.Target is null),
                OpenApiTagReference reference => (reference.Reference, reference.Target is null),
                _ => (null, holder.UnresolvedReference),
            };
    }

    // ==============================================================================================
    //  FAILURE MESSAGES
    //
    //  Every message names the DOCUMENT FILE, the ABSOLUTE PATH it was read from, the JSON POINTER or
    //  route, and the diagnostic text. That combination is the difference between a failure a reader can
    //  act on immediately and one that starts a search - and for a suite whose subject is two files of
    //  4,585 and 3,460 lines, the search is the expensive part.
    // ==============================================================================================

    /// <summary>Builds the report for a set of reader diagnostics.</summary>
    /// <remarks>
    /// Takes <see cref="ICollection{T}"/> rather than <see cref="IReadOnlyCollection{T}"/> because
    /// <c>OpenApiDiagnostic.Errors</c> and <c>Warnings</c> are declared as <see cref="IList{T}"/>, which
    /// does not derive from the read-only interface. Widening the parameter is preferable to copying the
    /// collection at each call site purely to satisfy a signature.
    /// </remarks>
    private static string Describe(
        ContractDocument contract,
        string category,
        ICollection<OpenApiError> findings)
    {
        StringBuilder text = new();

        text.Append(CultureInfo.InvariantCulture,
            $"'{contract.FileName}' produced {findings.Count} {category}(s) while loading. ");
        text.Append(CultureInfo.InvariantCulture, $"Read from '{contract.AbsolutePath}'.");

        foreach (OpenApiError finding in findings.Take(MaxReportedFindings))
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{Environment.NewLine}  at '{Located(finding.Pointer)}': {finding.Message}");
        }

        AppendTruncationNote(text, findings.Count);

        return text.ToString();
    }

    /// <summary>
    /// Builds the report for an explicit <c>Validate</c> pass, partitioned by validator category.
    /// </summary>
    /// <remarks>
    /// The rule name is included because it is the actionable part: <c>ResponseRequiredFields</c> at a
    /// pointer tells a reader exactly which specification requirement was missed, where a bare message
    /// leaves them inferring it.
    /// </remarks>
    private static string DescribeValidation(ContractDocument contract, ICollection<OpenApiError> findings)
    {
        StringBuilder text = new();

        int errors = findings.OfType<OpenApiValidatorError>().Count();
        int warnings = findings.OfType<OpenApiValidatorWarning>().Count();

        text.Append(CultureInfo.InvariantCulture,
            $"'{contract.FileName}' failed validation against ValidationRuleSet.GetDefaultRuleSet(): ");
        text.Append(CultureInfo.InvariantCulture,
            $"{errors} error(s), {warnings} warning(s), {findings.Count} finding(s) in total. ");
        text.Append(CultureInfo.InvariantCulture, $"Read from '{contract.AbsolutePath}'.");

        foreach (OpenApiError finding in findings.Take(MaxReportedFindings))
        {
            string rule = finding switch
            {
                OpenApiValidatorError validatorError => validatorError.RuleName,
                OpenApiValidatorWarning validatorWarning => validatorWarning.RuleName,
                _ => finding.GetType().Name,
            };

            text.Append(CultureInfo.InvariantCulture,
                $"{Environment.NewLine}  [{rule}] at '{Located(finding.Pointer)}': {finding.Message}");
        }

        AppendTruncationNote(text, findings.Count);

        return text.ToString();
    }

    /// <summary>Builds the report for a set of faulty reference sites.</summary>
    private static string DescribeReferences(
        ContractDocument contract,
        string category,
        IReadOnlyCollection<ReferenceSite> sites)
    {
        StringBuilder text = new();

        text.Append(CultureInfo.InvariantCulture,
            $"'{contract.FileName}' carries {sites.Count} {category}(s). ");
        text.Append(CultureInfo.InvariantCulture, $"Read from '{contract.AbsolutePath}'.");

        foreach (ReferenceSite site in sites.Take(MaxReportedFindings))
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{Environment.NewLine}  at '{Located(site.Pointer)}': {site.Kind} -> '{site.Id ?? "<no id>"}'");

            if (!string.IsNullOrEmpty(site.ExternalResource))
            {
                text.Append(CultureInfo.InvariantCulture, $" in external resource '{site.ExternalResource}'");
            }

            text.Append(CultureInfo.InvariantCulture,
                $" (unresolved: {site.Unresolved}, target missing: {site.TargetMissing}, external: {site.External})");
        }

        AppendTruncationNote(text, sites.Count);

        return text.ToString();
    }

    /// <summary>
    /// Substitutes a readable marker for a diagnostic that carries no location, so a message never reads
    /// <c>at ''</c>.
    /// </summary>
    private static string Located(string? pointer) =>
        string.IsNullOrWhiteSpace(pointer) ? "<no pointer reported>" : pointer;

    /// <summary>Notes how many findings were withheld, so a truncated report never looks complete.</summary>
    private static void AppendTruncationNote(StringBuilder text, int total)
    {
        if (total <= MaxReportedFindings)
        {
            return;
        }

        int withheld = total - MaxReportedFindings;

        // ONE interpolated string, not two concatenated with `+`. Concatenating interpolated strings
        // produces a plain string, which binds StringBuilder.Append to its (char*, int) overload and
        // fails to compile against a CultureInfo first argument - so the culture-aware overload is only
        // reached when the second argument is a single interpolated expression.
        text.Append(CultureInfo.InvariantCulture,
            $"{Environment.NewLine}  ... and {withheld} more (listing capped at {MaxReportedFindings}; one broken component reference typically produces dozens of sites, so fixing the first usually clears the rest).");
    }
}
