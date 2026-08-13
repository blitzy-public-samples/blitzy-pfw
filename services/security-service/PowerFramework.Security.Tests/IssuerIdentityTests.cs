// ==================================================================================================
//  THE ISSUER IDENTITY, VALIDATED AT STARTUP RATHER THAN AT REQUEST TIME
//
//  WHAT THIS FILE EXISTS FOR. `Security:Issuer` is not an ordinary string setting. It is stamped into
//  the `iss` claim of every token this service mints, it is published as the `issuer` member of the
//  OIDC discovery document, and - the part that makes its SHAPE load-bearing rather than cosmetic -
//  the discovery document's `jwks_uri` and `token_endpoint` members are COMPOSED from it. Contract
//  C-01's entire mechanism is that a consumer's stock `JwtBearer` handler self-configures by fetching
//  that document with zero bespoke code, so a value that cannot be composed into a fetchable address
//  breaks every verifier in the system while breaking nothing locally.
//
//  THE DEFECT THESE ROWS PIN CLOSED. Validating this setting for BLANKNESS ONLY - on the reasoning that
//  its format is a deployment decision - lets `not-a-uri`, `javascript:alert(1)`, `file:///etc/passwd`,
//  `ftp://h/p`, a whitespace-padded value and a value carrying a query and a fragment ALL START THE HOST.
//  That is measured, not hypothesised. Readiness then opens on a service whose `/health` answers 200 and
//  whose key set answers 200 while its discovery document answers 500 - the one artifact every verifier
//  must fetch is the only one that fails, and the probe every orchestrator gates on says nothing. A merely
//  MISTYPED but absolute issuer is worse still: the document publishes cleanly and makes every issued
//  token unverifiable, with no signal anywhere in the system.
//
//  THE SHAPE OF THE ASSERTIONS, AND WHY IT IS THIS SHAPE.
//
//    * GROUP 1 refuses one shape per row, so a failing continuous-integration report NAMES the shape
//      that stopped being refused rather than reporting that "issuer validation changed".
//
//    * GROUP 2 is the paired positive control. Six refusals prove nothing on their own - a validator
//      that refused EVERY issuer would pass all of Group 1 - so the shapes a deployment legitimately
//      uses are asserted to validate in the same file, including plain `http` and a path-scoped
//      issuer, both of which are legal and neither of which may be collateral damage of the rule.
//
//    * GROUP 3 is the secrets control (C-F). A rejected address is exactly the shape that may carry a
//      credential, so no refusal may echo the configured value. The `user:secret@` row is the one
//      that matters most and it is asserted against the SECRET rather than against the whole value.
//
//    * GROUP 4 asserts the refusal is FAIL-FAST at the host boundary rather than merely a validator
//      return value, because the finding was about a host that started.
//
//    * GROUP 5 is the property-shaped row that IS the finding. For every issuer spelling in a shared
//      corpus it asserts the implication "the request-time guard would refuse this => the startup
//      validator refuses it too". The defect was precisely a gap in that implication, and a row
//      asserting the implication cannot be satisfied by re-opening the gap somewhere new.
//
//  NO ROW USES A COMMITTED CREDENTIAL (C-F). Signing material is generated inside the test process,
//  and the one credential-shaped issuer spelling below is a literal invented here for the purpose of
//  proving it is never echoed.
// ==================================================================================================

using System.Net;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Extensions.Options;

using PowerFramework.Security.Configuration;
using PowerFramework.Security.Endpoints;

namespace PowerFramework.Security.Tests;

/// <summary>
/// The startup rule applied to <c>Security:Issuer</c>, and the fail-fast that carries it.
/// </summary>
public sealed class IssuerIdentityTests
{
    /// <summary>
    /// The configuration key every refusal must name, composed from the production section constant.
    /// </summary>
    /// <remarks>
    /// COMPOSED RATHER THAN SPELLED, because the validator composes it the same way. A literal here
    /// would keep passing after a section rename while every operator-facing message named the new
    /// section and this assertion named the old one.
    /// </remarks>
    private const string IssuerKey = SecurityOptions.SectionName + ":Issuer";

    /// <summary>
    /// The configuration key every published-origin refusal must name, composed the same way.
    /// </summary>
    /// <remarks>
    /// NEITHER KEY CONTAINS THE OTHER AS A SUBSTRING, which is what keeps the two settings' failure
    /// filters independent: a row about the location cannot pass on a failure about the identity, and the
    /// reverse. That is a property of these two spellings rather than of the filters, so it is recorded
    /// here where a rename would have to confront it.
    /// </remarks>
    private const string PublishedOriginsKey = SecurityOptions.SectionName + ":PublishedOrigins";

    /// <summary>The secret embedded in the userinfo row, asserted absent from that row's refusal.</summary>
    private const string UserInfoSecret = "an-issuer-embedded-credential";

    /// <summary>A shape that parses as absolute, is fetchable, and is what a deployment uses.</summary>
    /// <remarks>
    /// <b>DELIBERATELY NOT THE DEPLOYMENT'S OWN ADDRESS, AND A FUTURE READER SHOULD NOT "CORRECT" IT
    /// BACK.</b> The validator's not-absolute refusal quotes a fixed EXAMPLE address - the compose
    /// topology's real one - to tell an operator what a good value looks like. A corpus built on that
    /// same host would make Group 3's never-echoes-the-value rows fail against the example sentence
    /// rather than against any echo, so the corpus uses a reserved-TLD host that cannot appear in any
    /// fixed message. Three rows failed exactly that way before the host was changed, which is how the
    /// collision is known rather than assumed.
    /// </remarks>
    private const string ValidIssuer = "https://not-echoed.issuer.invalid:5104";

    // ==============================================================================================
    //  THE CORPUS. Every row below and the property row in Group 5 draw from these two sets, so a
    //  shape added to the corpus is exercised by the per-shape rows AND by the implication at once.
    // ==============================================================================================

    /// <summary>
    /// Every issuer spelling that must refuse to start, paired with the rule it offends.
    /// </summary>
    /// <remarks>
    /// THE FIRST SIX ARE THE MEASURED ONES - each was observed starting the host before the rule was
    /// applied. The remainder are the same rules approached from their other side: a query without a
    /// fragment, a fragment without a query, an embedded credential, and the relative spellings the
    /// request-time guard already knew about.
    /// </remarks>
    private static readonly (string Issuer, string Offence)[] RefusedSpellings =
    [
        // MEASURED: these six started the host.
        ("not-a-uri", "not absolute"),
        ("javascript:alert(1)", "scheme is not http or https"),
        ("file:///etc/passwd", "scheme is not http or https"),
        ("ftp://h/p", "scheme is not http or https"),
        ("  " + ValidIssuer + "  ", "surrounded by whitespace"),
        (ValidIssuer + "/t?q=1#f", "carries a query and a fragment"),

        // The same rules from their other side.
        (ValidIssuer + "/?q=1", "carries a query"),
        (ValidIssuer + "/#f", "carries a fragment"),
        ($"https://issuer:{UserInfoSecret}@not-echoed.issuer.invalid:5104", "embeds a credential"),
        ("not-echoed-relative-issuer", "not absolute"),
        ("//not-echoed.issuer.invalid:5104", "not absolute"),

        // REFUSED BY THE SCHEME RULE RATHER THAN THE ABSOLUTENESS RULE, and the distinction is a
        // measured platform fact rather than a guess: on Unix a rooted path parses as an ABSOLUTE
        // `file` URI, so `Uri.TryCreate(..., UriKind.Absolute, ...)` succeeds and it is the scheme
        // rule that catches it. Running Security with this value reports "must use the http or https
        // scheme; 'file'". It is listed with its real offence so a reader is not told something the
        // diagnostic contradicts, and it is worth a row precisely BECAUSE the absoluteness rule alone
        // would have admitted it.
        ("/not-echoed-relative-issuer", "absolute file URI, so the scheme rule refuses it"),
        ("   ", "blank"),
        ("", "blank"),
    ];

    /// <summary>
    /// Every issuer spelling a deployment legitimately uses, which the rule must not refuse.
    /// </summary>
    /// <remarks>
    /// WITHOUT THIS SET THE REFUSALS ABOVE ARE UNFALSIFIABLE. Two entries here are the ones a
    /// carelessly-tightened rule would break and are therefore the reason the set exists: plain
    /// <c>http</c>, which is what the attached environment's own documented topology names, and a
    /// PATH-SCOPED issuer, which OpenID Connect permits and a multi-tenant deployment uses. A rule
    /// that demanded https, or that demanded an empty path, would pass every Group 1 row and break a
    /// legitimate bring-up.
    /// </remarks>
    private static readonly string[] AcceptedSpellings =
    [
        ValidIssuer,
        "http://localhost:5104",
        "https://security.powerframework.test",
        "https://security.powerframework.test/",
        "https://security.powerframework.test/tenant-a",
        "HTTPS://not-echoed.issuer.invalid:5104",
    ];

    /// <summary>One row per refused spelling.</summary>
    /// <returns>The rows.</returns>
    public static TheoryData<string, string> RefusedIssuers()
    {
        TheoryData<string, string> rows = new();

        foreach ((string issuer, string offence) in RefusedSpellings)
        {
            rows.Add(issuer, offence);
        }

        return rows;
    }

    /// <summary>One row per accepted spelling.</summary>
    /// <returns>The rows.</returns>
    public static TheoryData<string> AcceptedIssuers()
    {
        TheoryData<string> rows = new();

        foreach (string issuer in AcceptedSpellings)
        {
            rows.Add(issuer);
        }

        return rows;
    }

    // ==============================================================================================
    //  GROUP 1 - REFUSAL, ONE SHAPE PER ROW.
    // ==============================================================================================

    /// <summary>
    /// Every measured bogus spelling is refused by the options validator, and the refusal names the key.
    /// </summary>
    /// <param name="issuer">The spelling this row configures.</param>
    /// <param name="offence">The rule it offends, carried only so a failing row reads legibly.</param>
    [Theory]
    [MemberData(nameof(RefusedIssuers))]
    public void EveryBogusIssuerSpellingIsRefusedByTheValidator(string issuer, string offence)
    {
        SecurityOptions options = Bootable();
        options.Issuer = issuer;

        // THE FAILURE MUST BE ABOUT THIS SETTING. Bootable() populates every other member, so a row
        // passing on an unrelated failure would be a false green - which is exactly how a rule can be
        // removed without any row noticing.
        string refusal = Assert.Single(IssuerFailures(options));

        Assert.Contains(IssuerKey, refusal, StringComparison.Ordinal);
        Assert.False(
            string.IsNullOrWhiteSpace(offence),
            "Every row states the rule it offends so a failure is readable.");
    }

    /// <summary>
    /// A blank issuer is refused by its own branch, distinctly from the shape branch.
    /// </summary>
    /// <remarks>
    /// ASSERTED SEPARATELY BECAUSE THE TWO BRANCHES SAY DIFFERENT THINGS TO AN OPERATOR. "Required and
    /// must not be blank" tells a deployment it forgot a setting; "must be an absolute address" tells
    /// it the setting is wrong. Collapsing them would answer the first case with the second message,
    /// which reads as though a value were supplied.
    /// </remarks>
    [Fact]
    public void ABlankIssuerIsRefusedAsAbsentRatherThanAsMalformed()
    {
        SecurityOptions options = Bootable();
        options.Issuer = "   ";

        string refusal = Assert.Single(IssuerFailures(options));

        Assert.Contains("must not be blank", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("absolute address", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A rooted path is refused, and the row records WHICH rule refuses it on this platform.
    /// </summary>
    /// <remarks>
    /// THE ABSOLUTENESS RULE ALONE WOULD ADMIT THIS VALUE, which is the only reason the row exists. On
    /// Unix a rooted path parses as an absolute <c>file</c> URI, so it clears
    /// <see cref="Uri.TryCreate(string, UriKind, out Uri)"/> and is caught one rule later by the scheme
    /// restriction. Asserting the BRANCH rather than merely the refusal is what stops a future
    /// simplification - "the scheme check is redundant once we require absoluteness" - from re-opening
    /// the gap. Measured on a running host, which reported <c>'file'</c> as the offending scheme.
    /// </remarks>
    [Fact]
    public void ARootedPathIsRefusedByTheSchemeRuleBecauseItParsesAsAnAbsoluteFileUri()
    {
        const string rooted = "/not-echoed-relative-issuer";

        // The platform premise is asserted rather than assumed: if a future runtime stopped treating a
        // rooted path as an absolute file URI, this row should say so rather than quietly change meaning.
        Assert.True(Uri.TryCreate(rooted, UriKind.Absolute, out Uri? parsed));
        Assert.Equal(Uri.UriSchemeFile, parsed!.Scheme);

        SecurityOptions options = Bootable();
        options.Issuer = rooted;

        string refusal = Assert.Single(IssuerFailures(options));

        Assert.Contains("must use the http or https scheme", refusal, StringComparison.Ordinal);
        Assert.Contains($"'{Uri.UriSchemeFile}'", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A padded value is refused rather than trimmed, and the refusal says so.
    /// </summary>
    /// <remarks>
    /// THE DISTINCTION IS NOT PEDANTRY. Three verifiers compare the <c>iss</c> claim
    /// BYTE-IDENTICALLY, so trimming here would make this service mint tokens carrying a value the
    /// operator did not configure, and the operator would have no way to see which of the two
    /// spellings was in force. Refusing is the only answer that keeps the configured value and the
    /// minted claim the same string.
    /// </remarks>
    [Fact]
    public void AWhitespacePaddedIssuerIsRefusedRatherThanSilentlyTrimmed()
    {
        SecurityOptions padded = Bootable();
        padded.Issuer = "  " + ValidIssuer + "  ";

        Assert.Single(IssuerFailures(padded));

        // The identical value without the padding validates, which is what makes the row above about
        // the PADDING rather than about the address.
        SecurityOptions trimmed = Bootable();
        trimmed.Issuer = ValidIssuer;

        Assert.Empty(IssuerFailures(trimmed));
    }

    // ==============================================================================================
    //  GROUP 2 - THE PAIRED POSITIVE CONTROL.
    // ==============================================================================================

    /// <summary>
    /// Every spelling a deployment legitimately uses still validates.
    /// </summary>
    /// <param name="issuer">The spelling this row configures.</param>
    [Theory]
    [MemberData(nameof(AcceptedIssuers))]
    public void EveryLegitimateIssuerSpellingStillValidates(string issuer)
    {
        SecurityOptions options = Bootable();
        options.Issuer = issuer;

        Assert.Empty(IssuerFailures(options));
    }

    /// <summary>
    /// A path-scoped issuer validates AND composes into fetchable well-known addresses.
    /// </summary>
    /// <remarks>
    /// THE ROW EXISTS BECAUSE ACCEPTANCE ALONE WOULD BE THE WEAKER CLAIM. A path-scoped issuer is
    /// permitted precisely so a multi-tenant deployment can run one issuer per tenant, and that is
    /// only true if the composed addresses remain absolute and retain the path segment. Composing them
    /// here from the production members is what proves acceptance was not merely permissive.
    /// </remarks>
    [Fact]
    public void APathScopedIssuerComposesIntoAbsoluteWellKnownAddresses()
    {
        SecurityOptions options = Bootable();
        options.Issuer = "https://security.powerframework.test/tenant-a";

        Assert.Empty(IssuerFailures(options));
        Assert.Null(JwksEndpoints.DescribeMetadataInconsistency(options));

        string keySet = options.Issuer.TrimEnd('/') + options.JwksPath;
        string tokenEndpoint = options.Issuer.TrimEnd('/') + options.TokenEndpointPath;

        Assert.True(Uri.TryCreate(keySet, UriKind.Absolute, out Uri? keySetAddress));
        Assert.True(Uri.TryCreate(tokenEndpoint, UriKind.Absolute, out Uri? tokenAddress));
        Assert.Equal(Uri.UriSchemeHttps, keySetAddress!.Scheme);
        Assert.Equal(Uri.UriSchemeHttps, tokenAddress!.Scheme);
        Assert.StartsWith("/tenant-a/", keySetAddress.AbsolutePath, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  GROUP 3 - THE SECRETS CONTROL (C-F).
    // ==============================================================================================

    /// <summary>
    /// No refusal echoes the configured issuer.
    /// </summary>
    /// <param name="issuer">The spelling this row configures.</param>
    /// <param name="offence">The rule it offends, unused by the assertion.</param>
    /// <remarks>
    /// A REJECTED ADDRESS IS EXACTLY THE SHAPE THAT MAY CARRY A CREDENTIAL, which is why the rule
    /// refusing userinfo and the rule withholding the value are the same concern rather than two. The
    /// blank rows are exempt because every string contains the empty string, so the assertion would be
    /// vacuously false rather than meaningful; the scheme is exempt because it is a fixed token from a
    /// closed set and naming it is what makes the message actionable.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RefusedIssuers))]
    public void NoRefusalEchoesTheConfiguredIssuer(string issuer, string offence)
    {
        Assert.NotNull(offence);

        if (issuer.Trim().Length == 0)
        {
            return;
        }

        SecurityOptions options = Bootable();
        options.Issuer = issuer;

        foreach (string failure in Validate(options))
        {
            Assert.DoesNotContain(issuer, failure, StringComparison.Ordinal);
            Assert.DoesNotContain(issuer.Trim(), failure, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A credential embedded in the issuer is refused, and the refusal carries neither it nor the value.
    /// </summary>
    /// <remarks>
    /// THIS IS THE ROW THE USERINFO RULE EXISTS FOR. An issuer carrying <c>user:secret@</c> would be
    /// stamped into the <c>iss</c> claim of every minted token AND published in the anonymously-served
    /// discovery document, so the credential would leave the process in both directions at once. The
    /// assertion is made against the SECRET rather than against the whole address, because a refusal
    /// that redacted the address but quoted the credential would pass a whole-value assertion.
    /// </remarks>
    [Fact]
    public void ACredentialEmbeddedInTheIssuerIsRefusedAndNeverEchoed()
    {
        SecurityOptions options = Bootable();
        options.Issuer = $"https://issuer:{UserInfoSecret}@not-echoed.issuer.invalid:5104";

        string refusal = Assert.Single(IssuerFailures(options));

        Assert.Contains("must not embed credentials", refusal, StringComparison.Ordinal);

        foreach (string failure in Validate(options))
        {
            Assert.DoesNotContain(UserInfoSecret, failure, StringComparison.Ordinal);
            Assert.False(
                SensitiveValueAssertions.Carries(failure, UserInfoSecret),
                "A refusal carries the credential embedded in the rejected issuer.");
        }
    }

    // ==============================================================================================
    //  GROUP 4 - FAIL-FAST AT THE HOST BOUNDARY.
    // ==============================================================================================

    /// <summary>
    /// A host configured with a bogus issuer refuses to start rather than opening readiness.
    /// </summary>
    /// <param name="issuer">The spelling this row configures.</param>
    /// <param name="offence">The rule it offends, unused by the assertion.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE VALIDATOR ROWS ABOVE ARE NOT THIS ROW. A validator that returned a failure nobody acted on
    /// would satisfy every one of them while the host still started, which is the state the finding
    /// described. This asserts the composition root's <c>ValidateOnStart</c> actually carries the
    /// refusal to a terminating exception, which is the legacy fail-fast posture
    /// [ws_objects/pfw.pbl.src/pfw.sra:L111-L144] rather than graceful degradation.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RefusedIssuers))]
    public async Task AHostConfiguredWithABogusIssuerRefusesToStart(string issuer, string offence)
    {
        Assert.NotNull(offence);

        await using SecurityAppFactory factory = new() { Issuer = issuer };

        OptionsValidationException refusal =
            Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(
            refusal.Failures,
            failure => failure.Contains(IssuerKey, StringComparison.Ordinal));

        if (issuer.Trim().Length > 0)
        {
            foreach (string failure in refusal.Failures)
            {
                Assert.DoesNotContain(issuer.Trim(), failure, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// The same host with a valid issuer starts and publishes a usable discovery document.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE COUNTERPART THAT MAKES THE REFUSALS MEAN SOMETHING. It also asserts the two composed
    /// members are present and absolute, because those are the members the finding observed a 500 in
    /// place of - a document that answered 200 with a <c>jwks_uri</c> nobody can fetch would be the
    /// same defect wearing a success status.
    /// </remarks>
    [Fact]
    public async Task AValidIssuerStartsAndPublishesFetchableWellKnownAddressesAsync()
    {
        await using SecurityAppFactory factory = new() { Issuer = ValidIssuer };
        using HttpClient client = factory.CreateClient();

        SecurityOptions options = factory.ResolveSecurityOptions();

        Assert.Equal(ValidIssuer, options.Issuer);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(options.OpenIdConfigurationPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            MediaTypeNames.Application.Json,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ValidIssuer, document.RootElement.GetProperty("issuer").GetString());

        string keySet = Assert.IsType<string>(
            document.RootElement.GetProperty("jwks_uri").GetString());
        string tokenEndpoint = Assert.IsType<string>(
            document.RootElement.GetProperty("token_endpoint").GetString());

        Assert.True(Uri.TryCreate(keySet, UriKind.Absolute, out _));
        Assert.True(Uri.TryCreate(tokenEndpoint, UriKind.Absolute, out _));
        Assert.StartsWith(ValidIssuer, keySet, StringComparison.Ordinal);
        Assert.StartsWith(ValidIssuer, tokenEndpoint, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  GROUP 5 - THE IMPLICATION THAT IS THE FINDING.
    // ==============================================================================================

    /// <summary>
    /// No issuer spelling exists that starts the host and then fails discovery.
    /// </summary>
    /// <param name="issuer">The spelling this row configures.</param>
    /// <remarks>
    /// <para>
    /// THIS ROW IS THE FINDING, STATED AS A PROPERTY. The defect was not that any particular spelling
    /// was accepted - it was that the two guards disagreed: <c>JwksEndpoints</c> refused a value at
    /// REQUEST time that the validator admitted at STARTUP time, so readiness opened on a service
    /// whose discovery document was already broken. A per-shape row can be satisfied by adding one
    /// more rule; this can only be satisfied by keeping the startup rule at least as strict as the
    /// request-time one, which is the actual requirement.
    /// </para>
    /// <para>
    /// THE OTHER DIRECTION IS DELIBERATELY NOT ASSERTED. The startup rule is strictly stronger - it
    /// also refuses a non-http scheme, userinfo, a query, a fragment and padding, none of which the
    /// request-time guard inspects - and requiring equivalence would forbid exactly that strengthening.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryIssuerSpelling))]
    public void WhateverTheRequestTimeGuardRefusesTheStartupValidatorRefusesFirst(string issuer)
    {
        SecurityOptions options = Bootable();
        options.Issuer = issuer;

        bool requestTimeRefuses = JwksEndpoints.DescribeMetadataInconsistency(options) is not null;
        bool startupRefuses = IssuerFailures(options).Count > 0;

        if (requestTimeRefuses)
        {
            Assert.True(
                startupRefuses,
                "An issuer the discovery endpoint refuses at request time started the host, which is " +
                "the readiness-opens-on-a-broken-document defect this rule exists to close.");
        }
    }

    /// <summary>
    /// Both corpora, so the implication above is exercised over accepted and refused spellings alike.
    /// </summary>
    /// <returns>One row per spelling in either corpus.</returns>
    public static TheoryData<string> EveryIssuerSpelling()
    {
        TheoryData<string> rows = new();

        foreach ((string issuer, _) in RefusedSpellings)
        {
            rows.Add(issuer);
        }

        foreach (string issuer in AcceptedSpellings)
        {
            rows.Add(issuer);
        }

        return rows;
    }

    // ==============================================================================================
    //  GROUP 6 - THE PUBLISHED LOCATION, WHICH IS A SECOND SETTING AND NOT A SECOND IDENTITY.
    //
    //  Security:PublishedOrigins declares the OTHER addresses this one service is reachable on, so the
    //  discovery document can name a key-set address the caller can actually resolve. The identity above
    //  is unaffected by it and every row here leaves it alone.
    //
    //  WHY THE RULES ARE THE ISSUER'S RULES. Each entry is composed with the well-known paths in exactly
    //  the way the issuer is, so an entry the issuer's rule would reject produces exactly the same
    //  unfetchable document. A looser rule here would mean the two sources of one published address
    //  disagreed about what a valid address is.
    // ==============================================================================================

    /// <summary>
    /// Every published-origin spelling that must refuse to start, drawn from the ISSUER's own refusal
    /// corpus so the two rules cannot drift apart.
    /// </summary>
    /// <returns>One row per refused spelling.</returns>
    /// <remarks>
    /// REUSING THE ISSUER CORPUS IS THE ASSERTION. If a shape is added to <see cref="RefusedSpellings"/>
    /// for the issuer, this rule is required to refuse it too, with no second list to remember to update.
    /// The two blank rows are excluded and driven separately: a blank entry has its own diagnostic here
    /// because an empty LIST ENTRY means something different from an absent setting.
    /// </remarks>
    public static TheoryData<string, string> RefusedPublishedOrigins()
    {
        TheoryData<string, string> rows = new();

        foreach ((string origin, string offence) in RefusedSpellings)
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                continue;
            }

            rows.Add(origin, offence);
        }

        return rows;
    }

    /// <summary>
    /// Every published-origin spelling a deployment legitimately uses, which the rule must not refuse.
    /// </summary>
    /// <returns>One row per accepted spelling.</returns>
    /// <remarks>
    /// The issuer's accepted corpus is NOT reused wholesale here, because every one of its entries would
    /// collide with the configured issuer's own origin on at least one row and be refused as a duplicate -
    /// correctly. These are distinct origins covering the same shape rules: a non-default port, a default
    /// port, plain http, a path-scoped base for a prefixing proxy, and a mixed-case scheme.
    /// </remarks>
    public static TheoryData<string> AcceptedPublishedOrigins()
    {
        TheoryData<string> rows = new();

        foreach (string origin in (string[])
            [
                "https://localhost:5104",
                "https://security.published.invalid",
                "http://localhost:5104",
                "https://proxy.published.invalid/security",
                "HTTPS://other.published.invalid:8443",
            ])
        {
            rows.Add(origin);
        }

        return rows;
    }

    /// <summary>
    /// The shipped default - no declared origin at all - validates, because it is the behaviour that
    /// preceded the setting.
    /// </summary>
    /// <remarks>
    /// THIS ROW IS THE COMPATIBILITY GUARANTEE. An empty collection must not be a fault, or every existing
    /// deployment would stop starting the moment the setting was introduced. It also pins the intended
    /// default: <c>appsettings.json</c> ships an empty list and the endpoint composes from the issuer
    /// alone under it.
    /// </remarks>
    [Fact]
    public void DeclaringNoPublishedOriginIsValidBecauseItIsTheShippedDefault()
    {
        SecurityOptions options = Bootable();

        Assert.Empty(options.PublishedOrigins);
        Assert.Empty(PublishedOriginFailures(options));
    }

    /// <summary>
    /// Every spelling the issuer rule refuses is refused here too, and the refusal names the indexed key.
    /// </summary>
    /// <param name="origin">The spelling this row configures.</param>
    /// <param name="offence">The rule it offends, carried only so a failing row reads legibly.</param>
    [Theory]
    [MemberData(nameof(RefusedPublishedOrigins))]
    public void EveryBogusPublishedOriginSpellingIsRefusedByTheValidator(string origin, string offence)
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(origin);

        string refusal = Assert.Single(PublishedOriginFailures(options));

        // THE INDEX IS NAMED, not merely the collection. A deployment sets these through an indexed
        // environment variable, so a diagnostic that named only the collection would leave an operator
        // reading every entry to find the one at fault.
        Assert.Contains(PublishedOriginsKey + "[0]", refusal, StringComparison.Ordinal);
        Assert.False(
            string.IsNullOrWhiteSpace(offence),
            "Every row states the rule it offends so a failure is readable.");
    }

    /// <summary>
    /// No published-origin refusal echoes the configured value, so a rejected address carrying a
    /// credential cannot reach a log through the diagnostic.
    /// </summary>
    /// <param name="origin">The spelling this row configures.</param>
    /// <param name="offence">The rule it offends, carried only so a failing row reads legibly.</param>
    /// <remarks>
    /// THE SAME POSTURE THE ISSUER'S REFUSALS TAKE, ASSERTED SEPARATELY BECAUSE IT IS A SEPARATE CODE
    /// PATH. One corpus row embeds a credential specifically so this row has something to catch: a
    /// diagnostic that quoted the offending entry would write that credential into the startup log of a
    /// service whose whole purpose is holding secrets.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RefusedPublishedOrigins))]
    public void NoPublishedOriginRefusalEchoesTheConfiguredValue(string origin, string offence)
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(origin);

        foreach (string refusal in PublishedOriginFailures(options))
        {
            Assert.DoesNotContain(UserInfoSecret, refusal, StringComparison.Ordinal);
            Assert.DoesNotContain(origin, refusal, StringComparison.Ordinal);
        }

        Assert.False(
            string.IsNullOrWhiteSpace(offence),
            "Every row states the rule it offends so a failure is readable.");
    }

    /// <summary>
    /// Every legitimate published-origin spelling validates, so the refusals above are falsifiable.
    /// </summary>
    /// <param name="origin">The spelling this row configures.</param>
    [Theory]
    [MemberData(nameof(AcceptedPublishedOrigins))]
    public void EveryLegitimatePublishedOriginSpellingStillValidates(string origin)
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(origin);

        Assert.Empty(PublishedOriginFailures(options));
    }

    /// <summary>
    /// A blank entry is refused on its own branch, distinctly from the malformed branch.
    /// </summary>
    /// <remarks>
    /// AN EMPTY LIST ENTRY IS NOT THE SAME FAULT AS AN ABSENT SETTING, which is why it gets its own
    /// message rather than falling into "must be an absolute address". An absent setting is a supported
    /// state; an empty entry is a slot a deployment believes it filled.
    /// </remarks>
    [Fact]
    public void ABlankPublishedOriginEntryIsRefusedAsBlankRatherThanAsMalformed()
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add("   ");

        string refusal = Assert.Single(PublishedOriginFailures(options));

        Assert.Contains("must not be blank", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("absolute address", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// An entry restating the configured ISSUER's own origin is refused as the duplicate it is.
    /// </summary>
    /// <remarks>
    /// THE ISSUER'S ORIGIN IS ALREADY PUBLISHED AND NEEDS NO ENTRY - it is the fallback the endpoint uses
    /// whenever nothing else matches - so an entry for it cannot change any published address. Reporting
    /// it matters because a deployment that added it is expressing an intent the setting cannot carry, and
    /// silently accepting it would leave that misunderstanding in place. The comparison is on ORIGIN
    /// rather than on the whole string, so a trailing separator does not disguise it.
    /// </remarks>
    [Fact]
    public void AnEntryRepeatingTheIssuersOwnOriginIsRefused()
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(ValidIssuer + "/");

        string refusal = Assert.Single(PublishedOriginFailures(options));

        Assert.Contains("repeats an origin", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two entries naming one origin are refused, and case in the host does not disguise it.
    /// </summary>
    /// <remarks>
    /// ONE ORIGIN RESOLVES TO ONE PUBLISHED ADDRESS, so a second entry for it cannot take effect: the
    /// endpoint returns the first match. A deployment that wrote two therefore intended two addresses and
    /// has one silently ignored, which is the state this refusal exists to prevent. A host is
    /// case-insensitive by specification, so the two spellings below are one origin.
    /// </remarks>
    [Fact]
    public void TwoEntriesNamingOneOriginAreRefusedEvenWhenTheirCaseDiffers()
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add("https://published.invalid:5104");
        options.PublishedOrigins.Add("https://PUBLISHED.invalid:5104");

        string refusal = Assert.Single(PublishedOriginFailures(options));

        Assert.Contains(PublishedOriginsKey + "[1]", refusal, StringComparison.Ordinal);
        Assert.Contains("repeats an origin", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host configured with a bogus published origin refuses to START, not merely to validate.
    /// </summary>
    /// <param name="origin">The spelling this row configures.</param>
    /// <param name="offence">The rule it offends, carried only so a failing row reads legibly.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE VALIDATOR ROWS ABOVE PROVE THE RULE; THIS ONE PROVES IT IS WIRED. A validator nobody registers
    /// refuses nothing, and the failure mode of a SKIPPED entry is the exact fault the whole setting
    /// exists to fix - consumers arriving on the declared origin receive the canonical document, with the
    /// configuration apparently correct. So a malformed entry has to stop the host.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(RefusedPublishedOrigins))]
    public async Task AHostConfiguredWithABogusPublishedOriginRefusesToStart(string origin, string offence)
    {
        Assert.NotNull(offence);

        // The collection is MUTATED rather than replaced, which is what ShapeOptions exists for: an
        // indexed configuration override would be merged with the application's own list by the binder,
        // and this row needs the entry under test to be the only one present.
        await using SecurityAppFactory factory = new()
        {
            ShapeOptions = options => options.PublishedOrigins.Add(origin),
        };

        OptionsValidationException refusal =
            Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(
            refusal.Failures,
            failure => failure.Contains(PublishedOriginsKey, StringComparison.Ordinal));

        // The startup diagnostic must not echo the rejected address either, for the same reason the
        // validator's own message must not: one corpus row embeds a credential.
        foreach (string failure in refusal.Failures)
        {
            Assert.DoesNotContain(UserInfoSecret, failure, StringComparison.Ordinal);
        }
    }

    // ==============================================================================================
    //  BUILDERS.
    // ==============================================================================================

    /// <summary>
    /// Runs the validator and returns only the failures that name the published-origin setting.
    /// </summary>
    /// <param name="options">The instance to validate.</param>
    /// <returns>The published-origin-naming failures, materialized.</returns>
    /// <remarks>
    /// FILTERED FOR THE SAME REASON <see cref="IssuerFailures"/> IS. The filter also keeps the two
    /// settings' rows independent: this key does not contain the issuer key as a substring and the issuer
    /// key does not contain this one, so neither filter can pick up the other's failure.
    /// </remarks>
    private static IReadOnlyList<string> PublishedOriginFailures(SecurityOptions options)
    {
        List<string> named = [];

        foreach (string failure in Validate(options))
        {
            if (failure.Contains(PublishedOriginsKey, StringComparison.Ordinal))
            {
                named.Add(failure);
            }
        }

        return named;
    }

    /// <summary>Runs the options validator and returns its failure messages.</summary>
    /// <param name="options">The instance to validate.</param>
    /// <returns>The failures, or an empty sequence when it validated.</returns>
    private static IReadOnlyList<string> Validate(SecurityOptions options)
    {
        ValidateOptionsResult result = new SecurityOptionsValidator().Validate(name: null, options);

        return result.Failures is null ? [] : [.. result.Failures];
    }

    /// <summary>
    /// Runs the validator and returns only the failures that name this setting.
    /// </summary>
    /// <param name="options">The instance to validate.</param>
    /// <returns>The issuer-naming failures, materialized.</returns>
    /// <remarks>
    /// THE FILTER IS THE POINT RATHER THAN A CONVENIENCE. <see cref="Bootable"/> populates every other
    /// member specifically so the list is empty but for this setting, and asserting on the FILTERED
    /// list is what stops a row from passing on an unrelated failure - which is the way a rule gets
    /// deleted with every row still green.
    /// </remarks>
    private static IReadOnlyList<string> IssuerFailures(SecurityOptions options)
    {
        List<string> named = [];

        foreach (string failure in Validate(options))
        {
            if (failure.Contains(IssuerKey, StringComparison.Ordinal))
            {
                named.Add(failure);
            }
        }

        return named;
    }

    /// <summary>Builds an options instance that is valid in every member except the one under test.</summary>
    /// <returns>The instance.</returns>
    /// <remarks>
    /// EVERY OTHER MEMBER IS POPULATED so a row's assertion is about ITS rule. A failure list carrying
    /// unrelated entries would let a row pass on the wrong one, and the single-failure assertions above
    /// depend on exactly one entry naming this setting. The signing key is generated, so no key literal
    /// exists in this file.
    /// </remarks>
    private static SecurityOptions Bootable()
    {
        using RSA key = RSA.Create(SecurityAppFactory.DefaultSigningKeySizeInBits);

        SecurityOptions options = new()
        {
            Issuer = ValidIssuer,
            SigningKey = key.ExportPkcs8PrivateKeyPem(),
            SigningKeyId = "powerframework-security-signing-1",
        };

        options.Audiences.Add("powerframework-gateway");

        options.Clients.Add(new SecurityClientOptions { Subject = "powerframework-gateway" });

        return options;
    }
}
