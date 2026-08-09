// ==================================================================================================
//  GatewayOptionsTests - THE COMPOSITION ROOT'S CONFIGURATION CONTRACT
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.Gateway.Configuration.GatewayOptions
//            PowerFramework.Gateway.Configuration.JwtBearerVerificationOptions
//            PowerFramework.Gateway.Configuration.AddressValidation (internal)
//
//  WHY THE VALIDATION IS THE INTERESTING PART
//  ------------------------------------------------------------------------------------------------
//  The legacy's fail-fast posture must survive as fail-fast, never as graceful degradation.
//  `pfw.sra`'s systemerror event unpacks a seven-field assert payload and then executes `HALT CLOSE`
//  [ws_objects/pfw.pbl.src/pfw.sra:L111-L144]; worker-session creation failure is fatal. The .NET
//  equivalent is fail-fast STARTUP VALIDATION, and softening it into a warning-and-continue would be
//  a behavioural change dressed up as robustness.
//
//  So these tests care less about what the defaults are than about what the validator REFUSES - and
//  about one property that is deliberately NOT validated.
//
//  THE PRESERVED DEFECT THIS TYPE CARRIES
//  ------------------------------------------------------------------------------------------------
//  `Locale` defaults to "en", reproducing the hardcoded `lang = "en"` at
//  ws_objects/pfw.pbl.src/pfw.sra:L94. The DEFECT is the hardcoding, so the fix is to preserve the
//  observable default while removing the UN-CONFIGURABILITY - which is a structural property, not a
//  behaviour. That distinction is why the value is "en" and yet settable.
// ==================================================================================================

using System.ComponentModel.DataAnnotations;
using PowerFramework.Gateway.Configuration;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Gateway.Tests;

public sealed class GatewayOptionsTests
{
    private static ValidationResult[] Validate(GatewayOptions options) =>
        options.Validate(new ValidationContext(options)).ToArray();

    private static ValidationResult[] Validate(JwtBearerVerificationOptions options) =>
        options.Validate(new ValidationContext(options)).ToArray();

    /// <summary>An options instance whose upstream addresses are valid, for isolating other checks.</summary>
    private static GatewayOptions ValidOptions() => new()
    {
        Upstreams = new GatewayOptions.UpstreamAddresses
        {
            DataServices = "http://dataservices:5102",
            Security = "http://security:5104",
        },
    };

    // ==============================================================================================
    //  SECTION NAMES - the binding keys, which are part of the operational contract
    // ==============================================================================================

    [Fact]
    public void TheSectionNamesAreTheKeysAnOperatorConfiguresAgainst()
    {
        Assert.Equal("Gateway", GatewayOptions.SectionName);

        // THE BEARER SECTION NAME IS THE FRAMEWORK'S OWN, NOT A BESPOKE ONE.
        //
        // "Authentication:Schemes:Bearer" is where ASP.NET Core's own JWT bearer handler reads its
        // configuration from. Binding to the framework's key rather than inventing one is what lets the
        // stock handler self-configure - which is the whole reason Security is REST and publishes OIDC
        // discovery. A bespoke key would force hand-written wiring into the security-critical path.
        Assert.Equal("Authentication:Schemes:Bearer", JwtBearerVerificationOptions.SectionName);
    }

    // ==============================================================================================
    //  DEFAULTS, INCLUDING THE PRESERVED HARDCODED LOCALE
    // ==============================================================================================

    [Fact]
    public void TheLocaleDefaultsToEnglishReproducingTheHardcodedLegacyValue()
    {
        // THE DEFECT IS THE HARDCODING, AND IT IS REPRODUCED AS A DEFAULT RATHER THAN AS A CONSTANT.
        //
        // ws_objects/pfw.pbl.src/pfw.sra:L94 sets `lang = "en"` literally inside the open event, then
        // selects one of three provider classes from it [:L95-L102]. The observable behaviour of a
        // default-configured framework is therefore English, and that is preserved exactly.
        //
        // What is NOT reproduced is the un-configurability, because that is a structural property rather
        // than a behaviour - and the AAP is explicit that the value is "preserved as the default but
        // made overridable". A test asserting the value were a `const` would be asserting the defect
        // rather than the behaviour.
        Assert.Equal("en", new GatewayOptions().Locale);
    }

    [Fact]
    public void TheLocaleIsOverridableAndIsDeliberatelyNotValidated()
    {
        var options = ValidOptions();

        // OVERRIDABLE - the half of the defect that IS fixed.
        options.Locale = "chs";
        Assert.Equal("chs", options.Locale);
        Assert.Empty(Validate(options));

        // AND NOT VALIDATED, WHICH IS A DELIBERATE ASYMMETRY WORTH RECORDING.
        //
        // DataServices validates its locale against an accepted set; Gateway does not. That is not an
        // oversight to be harmonised: Gateway does not RESOLVE translations - it forwards a locale
        // preference - and the localization facade's documented fallback is SILENT PASSTHROUGH, which
        // returns the text unchanged when no provider matches. Rejecting an unknown locale here would
        // convert that documented passthrough into a startup failure, which is stricter than the legacy
        // and therefore a behavioural change.
        //
        // Pinned so the absence of a check reads as a decision rather than as a gap.
        options.Locale = "not-a-real-locale";
        Assert.Empty(Validate(options));

        options.Locale = string.Empty;
        Assert.Empty(Validate(options));
    }

    [Fact]
    public void TheCapabilityFlagsDefaultToTheSevenAllCapabilities()
    {
        var options = new GatewayOptions();

        Assert.Equal(Enums.INIT_FLAG_ENABLE_ALL, options.CapabilityFlags);
        Assert.Equal(3847L, options.CapabilityFlags);

        // AND 3847 IS SEVEN BITS, NOT EIGHT - the BLINKFAST omission, reaching configuration.
        Assert.Equal(7, System.Numerics.BitOperations.PopCount(3847u));
    }

    [Fact]
    public void TheCapabilityFlagsAreNotRangeCheckedByValidation()
    {
        var options = ValidOptions();

        // CONSISTENT WITH THE UNCHECKED PROJECTION IN CapabilityFlags.
        //
        // The projection accepts the whole 2^32 domain and wraps a negative value, because the legacy
        // flag word is an unsigned long and rejecting a value the legacy accepted would be a
        // regression introduced by the refactor. A range check HERE would contradict that and make the
        // two halves disagree about what is configurable.
        foreach (long configured in (long[])[long.MinValue, -1L, 0L, 4294967296L, long.MaxValue])
        {
            options.CapabilityFlags = configured;
            Assert.Empty(Validate(options));
        }
    }

    [Fact]
    public void TheUpstreamDefaultsPointAtThePublishedPortsOfTheTwoServicesGatewayCalls()
    {
        var upstreams = new GatewayOptions().Upstreams;

        // 5102 IS DATASERVICES AND 5104 IS SECURITY, per the port map in docs/ARCHITECTURE.md.
        //
        // THE SCHEME IS PART OF THE ASSERTION, NOT INCIDENTAL. Both defaults are https: Security is the
        // trust bootstrap whose published key set every service verifies against, and DataServices
        // carries buffer state and conflict detail. The loopback plain-http topology the local bring-up
        // publishes is an override in appsettings.Development.json, so it applies only when
        // ASPNETCORE_ENVIRONMENT is Development and can never be inherited by a deployment that simply
        // forgets to override something.
        Assert.Equal("https://localhost:5102", upstreams.DataServices);
        Assert.Equal("https://localhost:5104", upstreams.Security);
    }

    [Fact]
    public void GatewayDeclaresExactlyTwoUpstreamsAndNoSigningKey()
    {
        // TWO, NOT THREE - AND THIS IS NOT AN OMISSION.
        //
        // Gateway's HEALTH aggregate covers three upstreams, but Gateway only CALLS two: DataServices
        // and Security. It never calls Persistence - DataServices does. Configuring a Persistence
        // address here would invite exactly the topology violation the architecture forbids, putting
        // SQL generation one hop from the ingress.
        var properties = typeof(GatewayOptions.UpstreamAddresses).GetProperties();

        Assert.Equal(2, properties.Length);
        Assert.Contains(properties, static p => p.Name == "DataServices");
        Assert.Contains(properties, static p => p.Name == "Security");
        Assert.DoesNotContain(properties, static p => p.Name == "Persistence");

        // AND NO SIGNING KEY ANYWHERE ON THE OPTIONS SURFACE.
        //
        // Security is the SOLE issuer and exactly one signing secret exists in the whole system. Gateway
        // holds verification material only, so a signing-key property here would be a second signing
        // authority - which is the specific thing the token topology forbids.
        foreach (Type type in (Type[])
            [typeof(GatewayOptions), typeof(GatewayOptions.UpstreamAddresses), typeof(JwtBearerVerificationOptions)])
        {
            foreach (var property in type.GetProperties())
            {
                foreach (string marker in (string[])["SigningKey", "Secret", "PrivateKey", "Password", "Credential"])
                {
                    Assert.False(
                        property.Name.Contains(marker, StringComparison.OrdinalIgnoreCase),
                        $"{type.Name}.{property.Name} looks like signing material. Gateway validates "
                            + "tokens and never issues them; Security is the sole issuer.");
                }
            }
        }
    }

    // ==============================================================================================
    //  FAIL-FAST VALIDATION - what the validator REFUSES
    // ==============================================================================================

    [Fact]
    public void AValidConfigurationProducesNoValidationResults()
    {
        Assert.Empty(Validate(ValidOptions()));
    }

    [Fact]
    public void AMissingUpstreamsSectionIsASingleFatalErrorThatStopsFurtherChecking()
    {
        var options = new GatewayOptions { Upstreams = null! };

        ValidationResult[] results = Validate(options);

        // ONE RESULT, NOT THREE.
        //
        // The validator yields the Upstreams error and then BREAKS, rather than going on to report that
        // DataServices and Security are also missing. That is the right shape: three errors that all
        // say "the section you did not configure is not configured" bury the one actionable fact, and an
        // operator reading a startup failure needs the root cause first.
        ValidationResult single = Assert.Single(results);

        Assert.Contains("Upstreams", single.ErrorMessage!, StringComparison.Ordinal);
        Assert.Contains("composition root", single.ErrorMessage!, StringComparison.Ordinal);
        Assert.Contains(nameof(GatewayOptions.Upstreams), single.MemberNames);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ABlankUpstreamAddressIsRejected(string? address)
    {
        var options = ValidOptions();
        options.Upstreams.DataServices = address!;

        ValidationResult result = Assert.Single(Validate(options));

        Assert.Contains("required", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.Contains(nameof(GatewayOptions.Upstreams), result.MemberNames);
    }

    [Theory]
    [InlineData("localhost")]                 // a bare host
    [InlineData("dataservices")]              // a bare service name
    [InlineData("5102")]                      // a bare port
    [InlineData("//dataservices:5102")]       // a protocol-relative reference
    [InlineData("not a uri at all")]
    public void AnAddressThatIsNotAnAbsoluteUriAtAllIsRejectedByTheUriCheck(string address)
    {
        var options = ValidOptions();
        options.Upstreams.Security = address;

        ValidationResult result = Assert.Single(Validate(options));

        // "ABSOLUTE" IS THE REQUIREMENT BECAUSE A RELATIVE ADDRESS CANNOT BE A BASE ADDRESS.
        //
        // An HttpClient base address and a gRPC channel target both need a scheme and an authority. A
        // relative reference binds successfully from configuration and then fails at the FIRST CALL,
        // which is exactly the deferred failure fail-fast startup validation exists to prevent.
        //
        // The five inputs above are the ones MEASURED to fail `Uri.TryCreate(..., UriKind.Absolute, ...)`.
        // The companion theory below covers the inputs that surprisingly PASS it.
        Assert.Contains("absolute URI", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ftp://dataservices:5102", "ftp")]
    [InlineData("file:///tmp/dataservices", "file")]
    [InlineData("net.tcp://dataservices:5102", "net.tcp")]
    [InlineData("grpc://dataservices:5102", "grpc")]

    // THE TWO THAT LOOK LIKE THEY SHOULD FAIL THE *ABSOLUTE* CHECK AND DO NOT. Both were measured
    // against the real runtime on this platform rather than assumed - see the remarks below.
    [InlineData("dataservices:5102", "dataservices")]
    [InlineData("/v1/datawindow", "file")]
    [InlineData("c:/temp/dataservices", "file")]
    public void AnAddressWithAnUnsupportedSchemeIsRejectedByTheSchemeCheck(
        string address,
        string expectedScheme)
    {
        var options = ValidOptions();
        options.Upstreams.DataServices = address;

        ValidationResult result = Assert.Single(Validate(options));

        // ONLY http AND https ARE ACCEPTED, AND THE SCHEME CHECK CARRIES MORE WEIGHT THAN IT APPEARS TO.
        //
        // Two measured findings put a genuinely misleading case here, and both would have been asserted
        // wrongly on intuition:
        //
        //   * `"dataservices:5102"` - which reads like a bare host:port with no scheme - IS a valid
        //     absolute URI. It parses as scheme `dataservices` with path `5102`. So the address check
        //     passes it and only the scheme check stops it. Anyone reasoning that "the absolute-URI
        //     check catches a missing scheme" is wrong: a colon is all it takes to look like a scheme.
        //
        //   * `"/v1/datawindow"` and `"c:/temp/..."` are also absolute URIs ON THIS PLATFORM, resolving
        //     to scheme `file`, because .NET treats an absolute filesystem path as a file URI. So a
        //     configuration value that is obviously a path - a very plausible copy-paste mistake - also
        //     reaches the scheme check rather than the address check.
        //
        // The scheme check is therefore the load-bearing one for the realistic mistakes, and asserting
        // WHICH arm rejects each input is what makes that visible instead of incidental.
        Assert.Contains("http or https", result.ErrorMessage!, StringComparison.Ordinal);

        // AND THE OFFENDING SCHEME IS NAMED IN THE MESSAGE, which is what turns "your address is wrong"
        // into something an operator can act on - especially for the `file` cases, where the scheme is
        // not visible anywhere in what they typed.
        Assert.Contains($"'{expectedScheme}'", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HTTP://DATASERVICES:5102")]
    [InlineData("Https://DataServices:5102")]
    public void AnUppercaseSchemeIsAcceptedBecauseTheUriTypeNormalisesIt(string address)
    {
        var options = ValidOptions();
        options.Upstreams.DataServices = address;

        // THE ORDINAL SCHEME COMPARISON IS SAFE, AND ONLY BECAUSE `Uri` LOWERCASES THE SCHEME FIRST.
        //
        // `AddressValidation` compares `parsed.Scheme` to `Uri.UriSchemeHttp` with
        // StringComparison.Ordinal - a case-SENSITIVE comparison. That would reject "HTTP://..." if the
        // scheme reached it verbatim, and it does not: `Uri` normalises the scheme to lower case during
        // parsing, which was measured rather than assumed.
        //
        // Worth pinning because the ordinal comparison looks like a latent bug on inspection. It is not,
        // but it depends on a normalisation happening elsewhere - so if that comparison were ever
        // "hardened" to OrdinalIgnoreCase, this test documents that the change is unnecessary rather
        // than a fix.
        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData("http://dataservices:5102")]
    [InlineData("https://dataservices:5102")]
    [InlineData("http://localhost:5102")]
    [InlineData("https://gateway.example.internal")]
    [InlineData("http://10.0.0.4:5102")]
    [InlineData("http://dataservices:5102/")]
    public void BothSupportedSchemesAndTypicalHostFormsAreAccepted(string address)
    {
        var options = ValidOptions();
        options.Upstreams.DataServices = address;

        Assert.Empty(Validate(options));
    }

    [Fact]
    public void TwoBadUpstreamAddressesAreBothReportedRatherThanOnlyTheFirst()
    {
        var options = new GatewayOptions
        {
            Upstreams = new GatewayOptions.UpstreamAddresses { DataServices = string.Empty, Security = "ftp://x" },
        };

        ValidationResult[] results = Validate(options);

        // BOTH, BECAUSE BOTH ARE INDEPENDENTLY ACTIONABLE.
        //
        // Contrast with the missing-section case above, which stops after one. The difference is
        // whether the second error tells the operator anything new: two distinct bad addresses are two
        // distinct fixes, whereas an absent section makes every field error redundant.
        Assert.Equal(2, results.Length);
    }

    // ==============================================================================================
    //  THE BEARER VERIFICATION OPTIONS
    // ==============================================================================================

    [Fact]
    public void TheBearerDefaultsCarryNoAuthorityAndRequireHttpsMetadata()
    {
        var options = new JwtBearerVerificationOptions();

        // THE AUTHORITY HAS NO CODE DEFAULT, AND THE EMPTY STRING IS THE POINT.
        //
        // Security's address is environment-specific, so a code default could only ever be one
        // environment's - and a deployment that configured nothing would then get a usable-looking
        // authority pointing at a host that does not exist for it. With no default the presence rule
        // rejects the omission by name at startup. The loopback value lives in
        // appsettings.Development.json, and every other environment supplies it through
        // Authentication__Schemes__Bearer__Authority.
        Assert.Equal(string.Empty, options.Authority);

        // AND HTTPS METADATA IS REQUIRED BY DEFAULT - the secure default, not the convenient one.
        Assert.True(options.RequireHttpsMetadata);

        // `MapInboundClaims` DEFAULTS TO FALSE, which keeps the claim names as the token carries them
        // rather than remapping them to the legacy WS-Security URIs. Remapping is the historical
        // default and it surprises anyone reading a claim by its JWT name.
        Assert.False(options.MapInboundClaims);

        Assert.Empty(options.ValidIssuers);
        Assert.Empty(options.ValidAudiences);
    }

    [Fact]
    public void TheDefaultBearerConfigurationDoesNotValidateAndThatIsDeliberate()
    {
        // MEASURED, AND INITIALLY SURPRISING: THE DEFAULTS DO NOT PASS.
        //
        // `Authority` has NO default at all and `ValidAudiences` defaults to EMPTY, so a Gateway started
        // with no bearer configuration fails startup validation with two errors.
        //
        // That is the correct behaviour rather than a defective default pair. Both errors force a
        // DELIBERATE operator decision on a security-critical setting: name the authority whose metadata
        // this service trusts, and name the audience this service's tokens are addressed to, neither of
        // which is discoverable from source. Note the cross-property RequireHttpsMetadata check does not
        // fire here: the authority error short-circuits it, so the strict flag is reported only once the
        // address itself parses (asserted separately below).
        //
        // Defaults that silently passed would mean a service could reach production with unauthenticated
        // metadata retrieval and audience validation effectively disabled - which is exactly the "no new
        // attack surface" requirement being violated by omission.
        ValidationResult[] results = Validate(new JwtBearerVerificationOptions());

        Assert.Equal(2, results.Length);
        Assert.Contains(results, static r =>
            r.ErrorMessage!.Contains("Authority", StringComparison.Ordinal));
        Assert.Contains(results, static r =>
            r.ErrorMessage!.Contains("ValidAudiences", StringComparison.Ordinal));
    }

    [Fact]
    public void AnHttpsAuthorityWithAnAudienceValidatesCleanly()
    {
        var options = new JwtBearerVerificationOptions { Authority = "https://security:5104" };
        options.ValidAudiences.Add("powerframework-gateway");

        Assert.Empty(Validate(options));
    }

    [Fact]
    public void APlainHttpAuthorityIsAcceptedOnlyWhenHttpsMetadataIsExplicitlyNotRequired()
    {
        var options = new JwtBearerVerificationOptions
        {
            Authority = "http://security:5104",
            RequireHttpsMetadata = false,
        };
        options.ValidAudiences.Add("powerframework-gateway");

        // THE ESCAPE HATCH EXISTS AND IS EXPLICIT.
        //
        // The local orchestration topology is plain HTTP on a private compose network, so this
        // combination has to be expressible. What matters is that it requires SAYING SO - the operator
        // opts out rather than inheriting the weaker posture silently.
        Assert.Empty(Validate(options));
    }

    [Fact]
    public void AnHttpsRequirementIsCheckedOnlyAfterTheAuthorityItselfParses()
    {
        var options = new JwtBearerVerificationOptions { Authority = "not a uri" };
        options.ValidAudiences.Add("aud");

        ValidationResult result = Assert.Single(Validate(options));

        // ONE ERROR, ABOUT THE URI - NOT TWO.
        //
        // The scheme check is in an `else if` after the address check, so a malformed authority reports
        // "not an absolute URI" and does NOT additionally report "is not https". Reporting both would
        // tell the operator to fix the scheme of a string that has no scheme, which sends them in the
        // wrong direction.
        Assert.Contains("absolute URI", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain("RequireHttpsMetadata", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyAudienceListIsFatalAndStopsFurtherAudienceChecking()
    {
        var options = new JwtBearerVerificationOptions
        {
            Authority = "https://security:5104",
            RequireHttpsMetadata = true,
        };

        ValidationResult result = Assert.Single(Validate(options));

        // THE MESSAGE EXPLAINS THE CONSEQUENCE, WHICH IS WHY IT IS FATAL.
        //
        // An audience is not discoverable from the authority's metadata, and audience validation is
        // enabled - so a service with none configured would reject EVERY token. Failing at startup with
        // that explanation is far better than a running service that returns 401 to everything.
        Assert.Contains("at least one audience", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.Contains("reject every token", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankAudienceEntryIsReportedWithoutRejectingTheWholeList(string blank)
    {
        var options = new JwtBearerVerificationOptions { Authority = "https://security:5104" };
        options.ValidAudiences.Add("powerframework-gateway");
        options.ValidAudiences.Add(blank);

        ValidationResult result = Assert.Single(Validate(options));

        // THE INDEX IS IN THE MESSAGE, which is what makes a list error actionable - an operator with a
        // five-entry audience list needs to know WHICH entry is blank.
        Assert.Contains("ValidAudiences:1", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void EachInvalidIssuerIsReportedWithItsOwnIndex()
    {
        var options = new JwtBearerVerificationOptions { Authority = "https://security:5104" };
        options.ValidAudiences.Add("powerframework-gateway");
        options.ValidIssuers.Add("https://security:5104");
        options.ValidIssuers.Add("ftp://wrong");
        options.ValidIssuers.Add(string.Empty);

        ValidationResult[] results = Validate(options);

        Assert.Equal(2, results.Length);
        Assert.Contains(results, static r =>
            r.ErrorMessage!.Contains("ValidIssuers:1", StringComparison.Ordinal));
        Assert.Contains(results, static r =>
            r.ErrorMessage!.Contains("ValidIssuers:2", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptyIssuerListIsAcceptedBecauseTheAuthorityImpliesTheIssuer()
    {
        var options = new JwtBearerVerificationOptions { Authority = "https://security:5104" };
        options.ValidAudiences.Add("powerframework-gateway");

        // ASYMMETRIC WITH ValidAudiences, AND CORRECTLY SO.
        //
        // The issuer IS discoverable: the stock bearer handler derives it from the authority's OIDC
        // discovery document. The audience is not, which is why one list may be empty and the other may
        // not. An operator may still pin issuers explicitly, and each entry is then validated.
        Assert.Empty(Validate(options));
        Assert.Empty(options.ValidIssuers);
    }

    [Fact]
    public void TheTwoListsAreGetOnlySoConfigurationBindingAppendsRatherThanReplaces()
    {
        var options = new JwtBearerVerificationOptions();

        // GET-ONLY COLLECTION PROPERTIES ARE THE OPTIONS-PATTERN IDIOM, and the reason is behavioural:
        // the configuration binder populates an existing instance rather than assigning a new one, so a
        // get-only list can never be left null by a partial binding. A settable list can arrive null
        // from a section that declares the key with no value, and then every read throws.
        Assert.Null(typeof(JwtBearerVerificationOptions).GetProperty(nameof(options.ValidIssuers))!.SetMethod);
        Assert.Null(typeof(JwtBearerVerificationOptions).GetProperty(nameof(options.ValidAudiences))!.SetMethod);

        Assert.NotNull(options.ValidIssuers);
        Assert.NotNull(options.ValidAudiences);
    }

    [Fact]
    public void BothOptionsTypesParticipateInStartupValidationThroughTheFrameworkInterface()
    {
        // IValidatableObject IS WHAT MAKES THE VALIDATION FAIL-FAST RATHER THAN ADVISORY.
        //
        // Implementing it is what lets `ValidateOnStart` run these checks during host startup and
        // terminate the process on failure, reproducing the legacy's `HALT CLOSE` posture. A validator
        // that was merely a public method nobody called would leave the fail-fast requirement
        // unsatisfied while looking satisfied.
        Assert.True(typeof(IValidatableObject).IsAssignableFrom(typeof(GatewayOptions)));
        Assert.True(typeof(IValidatableObject).IsAssignableFrom(typeof(JwtBearerVerificationOptions)));
    }
}
