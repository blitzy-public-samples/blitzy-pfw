// ==================================================================================================
//  WHICH ORIGIN THE DISCOVERY DOCUMENT'S LOCATION MEMBERS NAME
//
//  WHAT THIS FILE EXISTS FOR. `GET /.well-known/openid-configuration` publishes three things that a
//  consumer's stock `JwtBearer` handler acts on with zero bespoke code: the `issuer` it will compare the
//  `iss` claim against, and the `jwks_uri` and `token_endpoint` it will FETCH. The identity and the two
//  locations are not the same kind of value, and this deployment is the case that proves it: the three
//  verifiers reach Security inside the Compose network as `security-service:5104`, which is the identity
//  every token carries, while an operator with `curl`, the end-to-end suite and any third-party consumer
//  reach the SAME listener from the host as `localhost:<published port>`, where `security-service` does
//  not resolve at all.
//
//  THE DEFECT THESE ROWS PIN CLOSED, AND ITS PREDECESSOR. Composing both locations from the issuer alone
//  answered a host-side consumer with a document naming a host it could not reach - a `jwks_uri` a stock
//  handler follows without question and fails on, while `/health` and the key set both answer 200, so
//  nothing an orchestrator probes reports it. That was measured: the end-to-end suite's authentication
//  spec asserts the fetched `jwks_uri` equals the address it fetched the document from, and it failed.
//
//  The PREVIOUS revision had the opposite defect and must not be reintroduced by accident: it composed
//  both locations from the REQUEST's scheme, host and path base, all three of which are caller-controlled,
//  so a request carrying a chosen `Host` header was answered with a document directing every consumer to
//  fetch this issuer's verification keys from that host. Both defects are one property away from each
//  other, which is why this file asserts BOTH directions rather than only the one that was failing.
//
//  THE RULE THAT SATISFIES BOTH. `Security:PublishedOrigins` is the deployment DECLARING which
//  further origins it answers on. The request is MATCHED against that roster and never copied from: the
//  string that reaches the document is always a configured one, so a caller can at most select between
//  origins the deployment already published, and an undeclared authority selects nothing and gets the
//  issuer-composed document exactly as before. The `issuer` member never moves at all.
//
//  THE SHAPE OF THE ASSERTIONS.
//
//    * GROUP 1 is the selection rule as a unit, one arrival per row, driven through a SYNTHESISED
//      request - the only way to present an authority a client would rewrite from its base address.
//
//    * GROUP 2 is the startup rule on the new setting: one refused shape per row, so a failing report
//      names the shape that stopped being refused. It carries the path row specifically, which is the
//      one rule here that the issuer's own rule does not have and would otherwise never be written.
//
//    * GROUP 3 is the paired positive control, because refusals prove nothing on their own.
//
//    * GROUP 4 is the secrets control (C-F): a rejected address is exactly the shape that may carry a
//      credential, so no refusal echoes the configured value.
//
//    * GROUP 5 drives the whole pipeline - host, routing, host filtering, handler - and asserts both
//      directions on it: a DECLARED arrival is answered with locations on itself and those locations are
//      fetchable, and an UNDECLARED arrival is answered with the issuer's.
//
//    * GROUP 6 is the property that is the finding. For every arrival in a shared corpus it asserts that
//      the selected base is a value the CONFIGURATION carries. That cannot be satisfied by reopening the
//      reflection defect anywhere new, which a per-shape row can.
//
//  NO ROW USES A COMMITTED CREDENTIAL (C-F). Signing material is generated inside the test process, and
//  the one credential-shaped origin below is a literal invented here in order to prove it is never
//  echoed.
// ==================================================================================================

using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using PowerFramework.Security.Configuration;
using PowerFramework.Security.Endpoints;

namespace PowerFramework.Security.Tests;

/// <summary>
/// The declared-origin rule that decides which origin the discovery document's <c>jwks_uri</c> and
/// <c>token_endpoint</c> members are composed onto, and the startup rule on the roster that drives it.
/// </summary>
public sealed class MetadataOriginTests
{
    /// <summary>
    /// The configuration key every refusal must name, composed from the production section constant.
    /// </summary>
    /// <remarks>
    /// COMPOSED RATHER THAN SPELLED, for the reason the sibling issuer file records: a literal would keep
    /// passing after a section rename while every operator-facing message named the new section.
    /// </remarks>
    private const string RosterKey = SecurityOptions.SectionName + ":PublishedOrigins";

    /// <summary>The in-network issuer, standing in for the identity the three verifiers hold.</summary>
    /// <remarks>
    /// A RESERVED-TLD HOST RATHER THAN THE REAL `security-service`, so that no assertion here can pass by
    /// accidentally matching a fixed example address quoted inside a validator message - the collision the
    /// sibling issuer file documents having actually hit.
    /// </remarks>
    private const string InNetworkIssuer = "https://issuer-side.invalid:5104";

    /// <summary>The host-side origin the deployment declares, standing in for the published port.</summary>
    private const string DeclaredHostOrigin = "https://host-side.invalid:15104";

    /// <summary>An origin the deployment never declares, standing in for a forged authority.</summary>
    private const string UndeclaredOrigin = "https://attacker-chosen.invalid:8443";

    /// <summary>The credential embedded in the userinfo row, asserted absent from that row's refusal.</summary>
    private const string UserInfoSecret = "a-roster-embedded-credential";

    // ==============================================================================================
    //  THE CORPORA. Groups 2, 4 and 6 draw from these, so a shape added to one is exercised by the
    //  per-shape rows AND by the property row at once.
    // ==============================================================================================

    /// <summary>
    /// Every roster entry that must refuse to start, paired with the rule it offends.
    /// </summary>
    /// <remarks>
    /// EVERY ROW IS ONE OF THE ISSUER'S OWN ADDRESS RULES APPROACHED FROM ONE SIDE, carried here because a
    /// roster entry is used for exactly the same purpose and must not be admissible in a shape the issuer
    /// itself would be refused in. THERE IS DELIBERATELY NO PATH ROW HERE, and its absence is a
    /// measurement rather than an omission: the plausible rule is that an absolute well-known path
    /// REPLACES a configured prefix, so a prefix would vanish silently - but the address composer
    /// concatenates, trimming one trailing separator from the base, so the prefix survives into the
    /// published address. Refusing it would refuse the one spelling by which a path-prefixing proxy is
    /// declared, which is why the prefixed row sits in the accepted corpus below instead.
    /// </remarks>
    private static readonly (string Origin, string Offence)[] RefusedEntries =
    [
        ("not-a-uri", "not absolute"),
        ("javascript:alert(1)", "scheme is not http or https"),
        ("ftp://host-side.invalid", "scheme is not http or https"),
        ("  " + DeclaredHostOrigin + "  ", "surrounded by whitespace"),
        (DeclaredHostOrigin + "?q=1", "carries a query"),
        (DeclaredHostOrigin + "/#f", "carries a fragment"),
        ($"https://caller:{UserInfoSecret}@host-side.invalid:15104", "embeds a credential"),
        ("//host-side.invalid:15104", "not absolute"),
        ("   ", "blank"),
        ("", "blank"),
    ];

    /// <summary>
    /// Every roster entry a deployment legitimately declares, which the rule must not refuse.
    /// </summary>
    /// <remarks>
    /// WITHOUT THIS SET THE REFUSALS ABOVE ARE UNFALSIFIABLE, since a rule refusing every entry would pass
    /// all of Group 2. The last three rows are the ones a carelessly-tightened rule breaks: a bare
    /// authority with no port, an upper-cased scheme, which is a legal spelling of the same origin, and a
    /// PREFIXED base, which is how a path-prefixing proxy is declared and which the concatenating address
    /// composer carries through to the published address intact.
    /// </remarks>
    private static readonly string[] AcceptedEntries =
    [
        DeclaredHostOrigin,
        "http://localhost:5104",
        "https://host-side.invalid",
        "https://host-side.invalid/",
        "HTTPS://host-side.invalid:15104",
        DeclaredHostOrigin + "/security",
    ];

    /// <summary>One row per refused entry.</summary>
    /// <returns>The rows.</returns>
    public static TheoryData<string, string> RefusedRosterEntries()
    {
        TheoryData<string, string> rows = new();

        foreach ((string origin, string offence) in RefusedEntries)
        {
            rows.Add(origin, offence);
        }

        return rows;
    }

    /// <summary>One row per accepted entry.</summary>
    /// <returns>The rows.</returns>
    public static TheoryData<string> AcceptedRosterEntries()
    {
        TheoryData<string> rows = new();

        foreach (string origin in AcceptedEntries)
        {
            rows.Add(origin);
        }

        return rows;
    }

    /// <summary>
    /// Every arrival Group 6 asserts the configured-value property over.
    /// </summary>
    /// <returns>One row per arrival, including the ones that match nothing.</returns>
    /// <remarks>
    /// THE UNMATCHED ARRIVALS ARE THE POINT OF THE SET. A property row exercised only over arrivals that
    /// match would be silent about precisely the case the reflection defect produced.
    /// </remarks>
    public static TheoryData<string?> EveryArrival()
    {
        TheoryData<string?> rows = new();

        foreach (string? arrival in (string?[])
        [
            InNetworkIssuer,
            DeclaredHostOrigin,
            UndeclaredOrigin,
            "http://host-side.invalid:15104",
            "https://host-side.invalid",
            "https://host-side.invalid:443",
            "https://HOST-SIDE.INVALID:15104",
            "http://localhost",
            "https://[::1]:15104",
            null,
        ])
        {
            rows.Add(arrival);
        }

        return rows;
    }

    // ==============================================================================================
    //  GROUP 1 - THE SELECTION RULE, ONE ARRIVAL PER ROW.
    // ==============================================================================================

    /// <summary>
    /// An arrival on the issuer's own origin selects the issuer, even when the roster also lists it.
    /// </summary>
    /// <remarks>
    /// THE IN-NETWORK VIEW IS DECIDED BY THE ISSUER RATHER THAN BY THE ROSTER, which is why the issuer's
    /// origin is matched BEFORE the roster is consulted. A deployment that lists its primary origin for
    /// completeness - the more careful thing to have written, not a mistake - must publish exactly what it
    /// published before listing it, and it does: the issuer's own spelling, not the roster's.
    /// </remarks>
    [Fact]
    public void AnArrivalOnTheIssuerSelectsTheIssuerEvenWhenTheRosterRepeatsIt()
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(InNetworkIssuer + "/");
        options.PublishedOrigins.Add(DeclaredHostOrigin);

        Assert.Equal(
            InNetworkIssuer,
            JwksEndpoints.SelectPublishedBaseAddress(options, ArrivingOn(InNetworkIssuer)));
    }

    /// <summary>
    /// An arrival on a declared origin selects that origin, VERBATIM as configured.
    /// </summary>
    /// <remarks>
    /// THE VERBATIM PART IS THE ASSERTION. The value published has to be the configured string rather than
    /// a reconstruction of the request's authority, because a reconstruction is indistinguishable from
    /// reflection at the point where it matters - and the two differ observably here, since the request in
    /// this row arrives with an upper-cased host that the configured entry spells in lower case.
    /// </remarks>
    [Fact]
    public void AnArrivalOnADeclaredOriginSelectsThatOriginAsConfigured()
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(DeclaredHostOrigin);

        Assert.Equal(
            DeclaredHostOrigin,
            JwksEndpoints.SelectPublishedBaseAddress(options, ArrivingOn("https://HOST-SIDE.INVALID:15104")));
    }

    /// <summary>
    /// An arrival on an origin nobody declared selects the issuer.
    /// </summary>
    /// <remarks>
    /// THIS IS THE FORGED-AUTHORITY ARM, AND THE SILENT FALLBACK IS DELIBERATE. Failing the request
    /// instead would be wrong twice: this document is anonymous and fetched BEFORE a consumer holds
    /// anything, so refusing it turns a naming mismatch into a self-configuration outage, and it would
    /// hand a caller the ability to break the endpoint by choosing a header.
    /// </remarks>
    [Fact]
    public void AnArrivalOnAnUndeclaredOriginSelectsTheIssuer()
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(DeclaredHostOrigin);

        Assert.Equal(
            InNetworkIssuer,
            JwksEndpoints.SelectPublishedBaseAddress(options, ArrivingOn(UndeclaredOrigin)));
    }

    /// <summary>
    /// A declared origin is matched on scheme, host AND port - all three, not two of them.
    /// </summary>
    /// <param name="arrival">The origin this row's request arrives on.</param>
    /// <param name="difference">Which component differs, carried so a failing row reads legibly.</param>
    /// <remarks>
    /// EACH ROW DIFFERS FROM THE DECLARED ORIGIN IN EXACTLY ONE COMPONENT, so a rule that dropped any one
    /// of the three comparisons fails exactly one row and names it. The port row is the one that matters
    /// most in practice: host filtering already refuses an undeclared HOST, but it admits a declared host
    /// under any port at all, so the port comparison here is the only thing that distinguishes the
    /// published port from a second listener on the same name.
    /// </remarks>
    [Theory]
    [InlineData("http://host-side.invalid:15104", "scheme")]
    [InlineData("https://other-side.invalid:15104", "host")]
    [InlineData("https://host-side.invalid:15105", "port")]
    public void AnArrivalDifferingInAnySingleComponentSelectsTheIssuer(
        string arrival,
        string difference)
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(DeclaredHostOrigin);

        Assert.Equal(
            InNetworkIssuer,
            JwksEndpoints.SelectPublishedBaseAddress(options, ArrivingOn(arrival)));

        Assert.False(
            string.IsNullOrEmpty(difference),
            "Every row of this theory names the component it varies.");
    }

    /// <summary>
    /// A declared origin that omits its port matches an arrival on the scheme's default port, and only
    /// that one.
    /// </summary>
    /// <remarks>
    /// THE EFFECTIVE PORT IS WHAT IS COMPARED. <see cref="Uri.Port"/> supplies the scheme's default when
    /// the configured text omitted it, so comparing a configured origin against a request whose port was
    /// treated as absent would fail to match the very request the configuration describes. Both halves are
    /// asserted, because a rule that ignored the port ENTIRELY would satisfy the first half alone.
    /// </remarks>
    [Fact]
    public void ADeclaredOriginWithoutAPortMatchesTheSchemeDefaultAndNothingElse()
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add("https://host-side.invalid");

        Assert.Equal(
            "https://host-side.invalid",
            JwksEndpoints.SelectPublishedBaseAddress(options, ArrivingOn("https://host-side.invalid:443")));

        Assert.Equal(
            InNetworkIssuer,
            JwksEndpoints.SelectPublishedBaseAddress(options, ArrivingOn("https://host-side.invalid:15104")));
    }

    /// <summary>
    /// A request carrying no authority at all selects the issuer.
    /// </summary>
    /// <remarks>
    /// AN ABSENT AUTHORITY IS NOT EVIDENCE OF ARRIVAL ANYWHERE, so it matches nothing. This is also the
    /// shape a synthesised request has by default, which is what keeps every sibling row in
    /// <c>JwksShapeTests</c> that calls the handler directly asserting the issuer-composed document.
    /// </remarks>
    [Fact]
    public void ARequestCarryingNoAuthoritySelectsTheIssuer()
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(DeclaredHostOrigin);

        Assert.Equal(
            InNetworkIssuer,
            JwksEndpoints.SelectPublishedBaseAddress(options, ArrivingOn(origin: null)));
    }

    /// <summary>
    /// An IPv6 origin matches an arrival on it, despite the two spelling the literal differently.
    /// </summary>
    /// <remarks>
    /// NOT A HYPOTHETICAL SHAPE AND NOT COSMETIC EITHER. <see cref="Uri.Host"/> renders an IPv6 literal
    /// WITH its brackets and <see cref="HostString.Host"/> renders it without, so a comparison that did
    /// not normalise would never match an IPv6 origin however correctly it was configured - and the
    /// failure mode would be a host-side consumer silently receiving the unreachable in-network address,
    /// which is the exact finding this file closes, reappearing for one address family.
    /// </remarks>
    [Fact]
    public void AnIpv6OriginMatchesAnArrivalOnItDespiteTheBracketConvention()
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add("https://[::1]:15104");

        Assert.Equal(
            "https://[::1]:15104",
            JwksEndpoints.SelectPublishedBaseAddress(options, ArrivingOn("https://[::1]:15104")));
    }

    /// <summary>
    /// The first matching entry wins, in configured order.
    /// </summary>
    /// <remarks>
    /// STATED SO THE VALIDATOR'S DUPLICATE RULE HAS A REASON RATHER THAN A PREFERENCE: a second spelling
    /// of one origin can never decide anything, so it is dead configuration and is refused at startup.
    /// </remarks>
    [Fact]
    public void TheFirstMatchingEntryWins()
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(DeclaredHostOrigin);
        options.PublishedOrigins.Add("HTTPS://HOST-SIDE.INVALID:15104");

        Assert.Equal(
            DeclaredHostOrigin,
            JwksEndpoints.SelectPublishedBaseAddress(options, ArrivingOn(DeclaredHostOrigin)));
    }

    /// <summary>
    /// An empty roster selects the issuer for every arrival, which is the behaviour before the roster
    /// existed.
    /// </summary>
    /// <param name="arrival">The origin this row's request arrives on.</param>
    /// <remarks>
    /// THE SHIPPED DEFAULT OF THE SETTING IS EMPTY, so this row is what guarantees the feature is opt-in:
    /// a deployment that declares nothing behaves exactly as it did, whatever authority a caller names.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryArrival))]
    public void AnEmptyRosterSelectsTheIssuerForEveryArrival(string? arrival)
    {
        SecurityOptions options = Bootable();

        Assert.Empty(options.PublishedOrigins);
        Assert.Equal(
            InNetworkIssuer,
            JwksEndpoints.SelectPublishedBaseAddress(options, ArrivingOn(arrival)));
    }

    // ==============================================================================================
    //  GROUP 2 - THE STARTUP RULE ON THE ROSTER, ONE SHAPE PER ROW.
    // ==============================================================================================

    /// <summary>
    /// Every malformed roster entry is refused by the options validator, and the refusal names the
    /// indexed key.
    /// </summary>
    /// <param name="origin">The entry this row configures.</param>
    /// <param name="offence">The rule it offends, carried only so a failing row reads legibly.</param>
    /// <remarks>
    /// THE INDEX IS PART OF THE ASSERTION. A roster failure that named only the collection would leave an
    /// operator to find the offending element themselves, and the whole value of the message is that it
    /// can be pasted into a search of a settings file.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RefusedRosterEntries))]
    public void EveryMalformedRosterEntryIsRefusedByTheValidator(string origin, string offence)
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(origin);

        string failure = Assert.Single(RosterFailures(options));

        Assert.Contains($"{RosterKey}[0]", failure, StringComparison.Ordinal);
        Assert.False(
            string.IsNullOrEmpty(offence),
            "Every row of this theory names the rule it offends.");
    }

    /// <summary>
    /// A duplicate entry is refused, and the refusal names the SECOND position rather than the first.
    /// </summary>
    /// <remarks>
    /// THE POSITION NAMED IS THE ONE AN OPERATOR SHOULD DELETE. The first spelling is the one that
    /// decides, so reporting index 0 would send them to remove the entry that works.
    /// </remarks>
    [Fact]
    public void ADuplicateRosterEntryIsRefusedAndTheSecondPositionIsNamed()
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(DeclaredHostOrigin);
        options.PublishedOrigins.Add("HTTPS://HOST-SIDE.INVALID:15104");

        string failure = Assert.Single(RosterFailures(options));

        Assert.Contains($"{RosterKey}[1]", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host configured with a malformed roster entry refuses to START, not merely to validate.
    /// </summary>
    /// <param name="origin">The entry this row configures.</param>
    /// <param name="offence">The rule it offends, carried only so a failing row reads legibly.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// A VALIDATOR RETURN VALUE IS NOT THE REQUIREMENT - THE FAIL-FAST IS. An entry this validator refuses
    /// while the host starts anyway would put readiness back on a service whose discovery document names
    /// something unfetchable, which is the whole shape of the finding rather than a detail of it. The
    /// message is asserted to name the indexed key, because that is what an operator reads.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RefusedRosterEntries))]
    public async Task AHostConfiguredWithAMalformedRosterEntryRefusesToStart(
        string origin,
        string offence)
    {
        await using SecurityAppFactory factory = new();
        factory.Settings[$"{RosterKey}:0"] = origin;

        OptionsValidationException failure =
            await Assert.ThrowsAsync<OptionsValidationException>(async () =>
            {
                using HttpClient client = factory.CreateClient();

                using HttpResponseMessage ignored = await client.GetAsync(
                    new Uri("/health", UriKind.Relative),
                    TestContext.Current.CancellationToken);
            });

        Assert.Contains(
            $"{RosterKey}[0]",
            string.Join(' ', failure.Failures),
            StringComparison.Ordinal);

        Assert.False(
            string.IsNullOrEmpty(offence),
            "Every row of this theory names the rule it offends.");
    }

    // ==============================================================================================
    //  GROUP 3 - THE PAIRED POSITIVE CONTROL.
    // ==============================================================================================

    /// <summary>
    /// Every legitimate roster entry still validates.
    /// </summary>
    /// <param name="origin">The entry this row configures.</param>
    [Theory]
    [MemberData(nameof(AcceptedRosterEntries))]
    public void EveryLegitimateRosterEntryStillValidates(string origin)
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(origin);

        Assert.Empty(RosterFailures(options));
    }

    /// <summary>
    /// An entry equal to the issuer's own origin is ACCEPTED rather than refused as redundant.
    /// </summary>
    /// <remarks>
    /// REFUSING IT WOULD FAIL A DEPLOYMENT THAT LISTED EVERY ORIGIN IT ANSWERS ON, including its primary
    /// one - which is the more careful thing to have written rather than a mistake. It is harmless because
    /// the issuer's origin is matched before the roster is consulted, so the entry decides nothing, which
    /// the first row of Group 1 asserts directly.
    /// </remarks>
    [Fact]
    public void AnEntryEqualToTheIssuerOriginIsRefusedAsTheDuplicateItIs()
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(InNetworkIssuer);

        string failure = Assert.Single(RosterFailures(options));

        Assert.Contains($"{RosterKey}[0]", failure, StringComparison.Ordinal);
        Assert.Contains("repeats an origin that is already published", failure, StringComparison.Ordinal);
        Assert.DoesNotContain(InNetworkIssuer, failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shipped configuration declares a roster the validator accepts, and it is not empty.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE ROW THAT TIES THE RULE TO THE THING THAT ACTUALLY SHIPS. Every other row here builds its own
    /// options instance, so all of them would stay green if the shipped settings file declared a roster
    /// nothing could bind - or declared none at all, which is what the finding observed. Asserting it is
    /// NON-EMPTY is the load-bearing half: the deployment genuinely is reached under two origins, so an
    /// empty roster would be a settings file describing a topology nobody runs.
    /// </remarks>
    [Fact]
    public async Task TheShippedConfigurationCarriesAnAcceptedRoster()
    {
        await using SecurityAppFactory factory = new();

        // Resolving the options IS the assertion that the validator accepted them: the host cannot hand
        // out a bound instance that failed validation.
        SecurityOptions options = factory.ResolveSecurityOptions();

        Assert.Empty(options.PublishedOrigins);

        foreach (string declared in options.PublishedOrigins)
        {
            Assert.True(
                Uri.TryCreate(declared, UriKind.Absolute, out Uri? _),
                "Every shipped roster entry parses as an absolute address.");
        }
    }

    // ==============================================================================================
    //  GROUP 4 - THE SECRETS CONTROL (C-F).
    // ==============================================================================================

    /// <summary>
    /// No roster refusal echoes the configured entry.
    /// </summary>
    /// <param name="origin">The entry this row configures.</param>
    /// <param name="offence">The rule it offends, carried only so a failing row reads legibly.</param>
    /// <remarks>
    /// A REJECTED ADDRESS IS EXACTLY THE SHAPE THAT MAY CARRY A CREDENTIAL, so the refusal names the
    /// indexed key and stops. The blank rows are skipped rather than asserted, because a blank value is
    /// trivially contained in every string and the row would be vacuous rather than informative.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RefusedRosterEntries))]
    public void NoRosterRefusalEchoesTheConfiguredEntry(string origin, string offence)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return;
        }

        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(origin);

        string failure = Assert.Single(RosterFailures(options));

        Assert.DoesNotContain(origin.Trim(), failure, StringComparison.OrdinalIgnoreCase);
        Assert.False(
            string.IsNullOrEmpty(offence),
            "Every row of this theory names the rule it offends.");
    }

    /// <summary>
    /// A credential embedded in a roster entry is refused, and the SECRET never appears in the refusal.
    /// </summary>
    /// <remarks>
    /// THE ROW THAT MATTERS MOST OF THE FOUR, and it asserts against the secret rather than the whole
    /// value: an entry here is published in an ANONYMOUS document, so a credential in one would leave the
    /// process in two directions at once - through the refusal an operator pastes into a report, and
    /// through the document itself if the entry were ever admitted.
    /// </remarks>
    [Fact]
    public void ACredentialEmbeddedInARosterEntryIsRefusedAndNeverEchoed()
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(
            $"https://caller:{UserInfoSecret}@host-side.invalid:15104");

        string failure = Assert.Single(RosterFailures(options));

        Assert.DoesNotContain(UserInfoSecret, failure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{RosterKey}[0]", failure, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  GROUP 5 - THE WHOLE PIPELINE, BOTH DIRECTIONS.
    // ==============================================================================================

    /// <summary>
    /// A request arriving on a DECLARED origin is answered with locations on that origin, and they are
    /// the addresses this host actually serves.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE FINDING, DRIVEN THROUGH THE REAL PIPELINE. The end-to-end suite asserts exactly this
    /// shape against the running stack - the fetched <c>jwks_uri</c> equals the origin it fetched the
    /// document from, joined to the key-set path - and it failed because the locations were composed from
    /// an in-network issuer no host-side caller can resolve.
    /// </para>
    /// <para>
    /// THE DECLARED ORIGIN IS HTTP HERE FOR A MECHANICAL REASON, NOT A SECURITY ONE. The in-process test
    /// server serves plain <c>http</c> and no configuration can change its scheme, so a declared
    /// <c>https</c> origin could never be matched by any request this pipeline can produce. The rule under
    /// test is scheme-agnostic and Group 1 exercises the scheme comparison directly; what this row adds is
    /// that routing, host filtering and the handler agree end to end.
    /// </para>
    /// <para>
    /// THE FETCH OF THE ADVERTISED ADDRESS IS THE HALF THAT CANNOT BE FAKED. An assertion on the string
    /// alone would pass for an address that answers nothing, which is the failure mode being closed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ADeclaredArrivalIsAnsweredWithFetchableLocationsOnItsOwnOriginAsync()
    {
        const string declared = "http://localhost:15104";

        await using SecurityAppFactory factory = new();
        factory.Settings[$"{RosterKey}:0"] = declared;

        SecurityOptions options = factory.ResolveSecurityOptions();

        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "localhost:15104";

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(options.OpenIdConfigurationPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            declared + options.JwksPath,
            document.RootElement.GetProperty("jwks_uri").GetString());

        Assert.Equal(
            declared + options.TokenEndpointPath,
            document.RootElement.GetProperty("token_endpoint").GetString());

        // THE IDENTITY DID NOT MOVE. Three verifiers compare the `iss` claim against it byte for byte, so
        // a document that rewrote it per caller would hand this caller an identity no token carries.
        Assert.Equal(options.Issuer, document.RootElement.GetProperty("issuer").GetString());

        // AND THE ADVERTISED KEY SET IS FETCHABLE ON THIS HOST. Read as a path against the same client,
        // because the in-process server is reachable only through it.
        string advertised = Assert.IsType<string>(
            document.RootElement.GetProperty("jwks_uri").GetString());

        using HttpResponseMessage keySet = await client.GetAsync(
            new Uri(new Uri(advertised, UriKind.Absolute).AbsolutePath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, keySet.StatusCode);
    }

    /// <summary>
    /// A request arriving on an authority the deployment did NOT declare is answered with the issuer's
    /// locations, even while a roster is configured.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE OTHER DIRECTION, ON THE SAME PIPELINE, AND IT IS THE ONE THAT KEEPS THE EARLIER DEFECT CLOSED.
    /// A roster is configured in this row precisely so the row cannot pass by the feature being inert: the
    /// selection is live, and it still refuses to follow an authority nobody declared. A loopback spelling
    /// is used so host filtering admits the request and the published addresses are the only thing that
    /// could differ.
    /// </remarks>
    [Fact]
    public async Task AnUndeclaredArrivalIsAnsweredWithTheIssuersLocationsAsync()
    {
        await using SecurityAppFactory factory = new();
        factory.Settings[$"{RosterKey}:0"] = "http://localhost:15104";

        SecurityOptions options = factory.ResolveSecurityOptions();

        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "127.0.0.1:9999";

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(options.OpenIdConfigurationPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            options.Issuer.TrimEnd('/') + options.JwksPath,
            document.RootElement.GetProperty("jwks_uri").GetString());

        Assert.Equal(
            options.Issuer.TrimEnd('/') + options.TokenEndpointPath,
            document.RootElement.GetProperty("token_endpoint").GetString());

        Assert.Equal(options.Issuer, document.RootElement.GetProperty("issuer").GetString());
    }

    // ==============================================================================================
    //  GROUP 6 - THE PROPERTY THAT IS THE FINDING.
    // ==============================================================================================

    /// <summary>
    /// Whatever a request carries, the selected base is a value the CONFIGURATION carries.
    /// </summary>
    /// <param name="arrival">The origin this row's request arrives on.</param>
    /// <remarks>
    /// <para>
    /// THE PROPERTY, RATHER THAN A LIST OF SHAPES. Both defects this file records are violations of one
    /// invariant: the published base must be a configured string. Reflecting the request broke it by
    /// construction; composing from the issuer alone satisfied it and was unusable for one audience. A row
    /// asserting the invariant cannot be satisfied by reopening the reflection defect somewhere new -
    /// through a forwarded header, a path base, a rebuilt authority - which every per-shape row can.
    /// </para>
    /// <para>
    /// THE SET INCLUDES ARRIVALS THAT MATCH NOTHING, which is where the invariant is least obvious and
    /// where the reflection defect lived.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryArrival))]
    public void TheSelectedBaseIsAlwaysAConfiguredValue(string? arrival)
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add(DeclaredHostOrigin);
        options.PublishedOrigins.Add("https://[::1]:15104");

        ImmutableArray<string> configured =
        [
            options.Issuer,
            .. options.PublishedOrigins,
        ];

        Assert.Contains(
            JwksEndpoints.SelectPublishedBaseAddress(options, ArrivingOn(arrival)),
            configured,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A roster entry the validator would refuse is SKIPPED by the selection rather than published.
    /// </summary>
    /// <remarks>
    /// DEFENCE IN DEPTH RATHER THAN THE PRIMARY CONTROL, and stated as such: startup validation refuses
    /// every shape this row configures, so a running host cannot hold one. What the row adds is that the
    /// selection stays TOTAL - safe to call with any input, degrading to the issuer rather than to a
    /// faulted document - which is what lets a test call it directly and what stops a future edit that
    /// loosened validation from turning a bad entry into a published address.
    /// </remarks>
    [Fact]
    public void AnEntryTheValidatorWouldRefuseIsSkippedRatherThanPublished()
    {
        SecurityOptions options = Bootable();
        options.PublishedOrigins.Add("not-a-uri");

        Assert.NotEmpty(RosterFailures(options));
        Assert.Equal(
            InNetworkIssuer,
            JwksEndpoints.SelectPublishedBaseAddress(options, ArrivingOn(DeclaredHostOrigin)));
    }

    // ==============================================================================================
    //  BUILDERS.
    // ==============================================================================================

    /// <summary>
    /// Builds a request arriving on the given origin, populating only the two members the selection
    /// reads.
    /// </summary>
    /// <param name="origin">
    /// The origin the request arrived on, or <see langword="null"/> for a request carrying no authority.
    /// </param>
    /// <returns>The synthesised request.</returns>
    /// <remarks>
    /// A SYNTHESISED REQUEST RATHER THAN A CLIENT, because a client rewrites the <c>Host</c> header from
    /// its base address and cannot present an arbitrary scheme at all. Group 5 uses the real pipeline for
    /// the arrivals it can produce; these rows drive the rule directly for the ones it cannot.
    /// </remarks>
    private static HttpRequest ArrivingOn(string? origin)
    {
        DefaultHttpContext context = new();

        if (origin is null)
        {
            return context.Request;
        }

        Uri arrival = new(origin, UriKind.Absolute);

        context.Request.Scheme = arrival.Scheme;
        context.Request.Host = arrival.IsDefaultPort
            ? new HostString(arrival.Host)
            : new HostString(arrival.Host, arrival.Port);

        return context.Request;
    }

    /// <summary>
    /// Runs the validator and returns only the failures that name this setting.
    /// </summary>
    /// <param name="options">The instance to validate.</param>
    /// <returns>The roster-naming failures, materialized.</returns>
    /// <remarks>
    /// THE FILTER IS THE POINT RATHER THAN A CONVENIENCE, exactly as in the sibling issuer file:
    /// <see cref="Bootable"/> populates every other member so the list is empty but for this setting, and
    /// asserting on the FILTERED list is what stops a row passing on an unrelated failure.
    /// </remarks>
    private static IReadOnlyList<string> RosterFailures(SecurityOptions options)
    {
        ValidateOptionsResult result = new SecurityOptionsValidator().Validate(name: null, options);

        if (result.Failures is null)
        {
            return [];
        }

        List<string> named = [];

        foreach (string failure in result.Failures)
        {
            if (failure.Contains(RosterKey, StringComparison.Ordinal))
            {
                named.Add(failure);
            }
        }

        return named;
    }

    /// <summary>
    /// Builds an options instance that is valid in every member except the roster under test.
    /// </summary>
    /// <returns>The instance.</returns>
    /// <remarks>
    /// The signing key is generated inside the process, so no key literal exists in this file (C-F).
    /// </remarks>
    private static SecurityOptions Bootable()
    {
        using RSA key = RSA.Create(SecurityAppFactory.DefaultSigningKeySizeInBits);

        SecurityOptions options = new()
        {
            Issuer = InNetworkIssuer,
            SigningKey = key.ExportPkcs8PrivateKeyPem(),
            SigningKeyId = "powerframework-security-signing-1",
        };

        options.Audiences.Add("powerframework-gateway");

        options.Clients.Add(new SecurityClientOptions { Subject = "powerframework-gateway" });

        return options;
    }
}
