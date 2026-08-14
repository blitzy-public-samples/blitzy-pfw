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
//
//  LOCATOR CONVENTION: every bare `pfw.sra:L...` in this file means ws_objects/pfw.pbl.src/pfw.sra,
//  the framework application - never the same-named packager object at
//  ws_objects/pfw.pack.pbl.src/pfw.sra (AAP 0.8.6 R7).
// ==================================================================================================

using System.ComponentModel.DataAnnotations;
using System.Reflection;
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
    /// <remarks>
    /// Every service declares ONE listener on its assigned port, so the upstream and probe addresses for
    /// DataServices name the same endpoint; they stay in separate groups because one is a call edge and
    /// the other an observation. Both groups are populated because both are validated. The values here
    /// are deliberately NOT the defaults - a fixture that reproduced the defaults could not distinguish
    /// "the validator accepted this" from "the validator never read it".
    /// </remarks>
    private static GatewayOptions ValidOptions() => new()
    {
        Upstreams = new GatewayOptions.UpstreamAddresses
        {
            DataServices = "https://dataservices:5102",
            Security = "https://security:5104",
        },
        HealthProbes = new GatewayOptions.HealthProbeAddresses
        {
            Persistence = "http://persistence:5101",
            DataServices = "http://dataservices:5102",
            Security = "https://security:5104",
        },

        // ONE OF THE TWO ISSUANCE SCHEMES, BECAUSE A DEPLOYMENT WITH NEITHER IS DELIBERATELY REFUSED.
        // Gateway requests a token before every call it makes, so it must be able to present something
        // on Security's issuance edge; the validator treats "neither" as a structural fault. A
        // fixture-shaped value, never a real one - nothing here is presented to anything.
        SecurityClientSecret = "an-issuance-secret-shaped-value",
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

        // 5102 IS DATASERVICES AND 5104 IS SECURITY, per the port map of AAP 0.3.2.2 and
        // docs/ARCHITECTURE.md 4.1. The documented band is 5101-5105 with 5103 reserved, and every
        // address a service holds for another names a port inside it.
        //
        // BOTH HALVES ARE ASSERTED, AND EACH FOR ITS OWN REASON.
        //
        // THE SCHEME: every listener in this estate is TLS, and the DEFAULT is the part that matters. A
        // deployment that binds this section supplies its own address; one that forgets gets this value,
        // so a cleartext default fails silently in the one case where nobody is looking - and every
        // request on both edges carries a bearer token (CWE-319). TLS is also what makes ONE port able to
        // carry both surfaces at all: ALPN selects the protocol version during the handshake.
        //
        // THE PORT: DataServices declares ONE endpoint, `https://+:5102` with `Protocols:
        // Http1AndHttp2`, which answers the readiness probe over HTTP/1.1 and the C-03/C-04 gRPC
        // contracts over HTTP/2. Gateway calls those contracts over gRPC and names that same 5102.
        // Defaulting this to 5112 - a second Http2-only endpoint outside the documented band - is the
        // tempting alternative and is not available: AAP 0.3.2.2 assigns C-03 and C-04 to 5102, so a
        // channel built on 5112 reaches a port the map does not give those contracts.
        Assert.Equal("https://localhost:5102", upstreams.DataServices);
        Assert.Equal("https://localhost:5104", upstreams.Security);
    }

    [Fact]
    public void TheDataServicesUpstreamNamesTheAssignedPortAndNotAWithdrawnOne()
    {
        // A REGRESSION GUARD WITH A SPECIFIC FAILURE IN MIND, NOT A RESTATEMENT OF THE TEST ABOVE.
        //
        // `https://localhost:5112` is the value that looks plausible from every angle except the one that
        // matters: right service, right host, right scheme, and it is the address a second Http2-only
        // DataServices listener would carry. NOTHING BINDS 5112 - no such listener is declared. A channel
        // built on it fails at connect, BEFORE any request reaches DataServices, so nothing in DataServices
        // logs it - the symptom is a Gateway 502 on every /v1/datawindow route and the cause is two
        // characters of port.
        //
        // The positive half is asserted here too rather than left to the equality above, because the
        // discriminating property is that the CALL address and the PROBE address agree: 5102 carries
        // both surfaces, so naming anything else is the defect.
        Assert.EndsWith(":5102", new GatewayOptions().Upstreams.DataServices, StringComparison.Ordinal);
        Assert.DoesNotContain(":5112", new GatewayOptions().Upstreams.DataServices, StringComparison.Ordinal);
        Assert.DoesNotContain(":5111", new GatewayOptions().Upstreams.DataServices, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProbeDefaultsCoverAllThreeUpstreamsOnTheirRestPorts()
    {
        var probes = new GatewayOptions().HealthProbes;

        // THREE, WHERE Upstreams HAS TWO, AND THE ASYMMETRY IS THE CONTRACT. C-09's
        // AggregateHealthReport bounds `upstreams` at exactly three items and closes UpstreamHealth's
        // service name over persistence, dataservices and security, so a two-member probe group leaves
        // the published aggregate unbuildable.
        Assert.Equal("https://localhost:5101", probes.Persistence);
        Assert.Equal("https://localhost:5102", probes.DataServices);
        Assert.Equal("https://localhost:5104", probes.Security);

        // THE PROBE AND THE CALL EDGE NAME THE SAME ENDPOINT OF THE SAME SERVICE, AND THAT IS WHAT IS
        // ASSERTED. DataServices binds one `Http1AndHttp2` listener on 5102: ALPN gives the probe HTTP/1.1
        // and the gRPC channel HTTP/2 on that single port, so both members carry 5102. Splitting them -
        // probe on 5102, channel on a second Http2-only 5112 - would make this row assert they DIFFER, and
        // the split is not available because AAP 0.3.2.2 assigns C-03 and C-04 to 5102.
        //
        // THE TWO MEMBERS STAY SEPARATE ANYWAY, AND THE REASON IS AUTHORITY RATHER THAN ADDRESS. The probe
        // group authorises one anonymous GET /health; the upstream group is a call address a gRPC channel
        // is built from. Aliasing one to the other would let a change of call address silently move the
        // probe, and vice versa, which is exactly the substitution the sibling rows below forbid.
        Assert.Equal(new GatewayOptions().Upstreams.DataServices, probes.DataServices);
    }

    [Fact]
    public void TheProbeGroupCarriesPersistenceWhileTheCallGroupDoesNot()
    {
        // THE ONE PLACE PERSISTENCE MAY BE NAMED IN GATEWAY'S CONFIGURATION, AND THE REASON IS THE
        // DIFFERENCE BETWEEN OBSERVING AND CALLING.
        //
        // C-10 requires Gateway's aggregate to name Persistence with its own state; the same contract
        // states that Gateway never calls Persistence. Both hold because the probe group authorises one
        // anonymous endpoint - GET /health - and Gateway holds no Persistence client, channel or
        // generated stub with which anything further could be reached. Moving this member onto
        // UpstreamAddresses would put SQL generation one hop from the ingress while looking like
        // configuration, which is why the two groups are separate types rather than one.
        Assert.Contains(
            typeof(GatewayOptions.HealthProbeAddresses).GetProperties(),
            static p => p.Name == "Persistence");

        Assert.DoesNotContain(
            typeof(GatewayOptions.UpstreamAddresses).GetProperties(),
            static p => p.Name == "Persistence");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-uri")]
    [InlineData("ftp://persistence:5101")]
    [InlineData("http://user:secret@persistence:5101")]
    [InlineData("http://persistence:5101?probe=1")]
    [InlineData("http://persistence:5101#fragment")]
    public void AnUnusableProbeAddressIsRefusedByTheSameRulesAsAnUpstreamAddress(string? address)
    {
        GatewayOptions options = ValidOptions();
        options.HealthProbes.Persistence = address!;

        ValidationResult result = Assert.Single(Validate(options));

        Assert.Contains(nameof(GatewayOptions.HealthProbes), result.MemberNames);

        // The value is never echoed, for the same reason an upstream address is not: a rejected address
        // may carry a credential, and the startup log is the wrong place to publish one.
        if (!string.IsNullOrWhiteSpace(address))
        {
            Assert.DoesNotContain(address, result.ErrorMessage!, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("DataServices")]
    [InlineData("Security")]
    [InlineData("Persistence")]
    public void EVERYProbeMemberIsCheckedAndNotOnlyTheFirst(string member)
    {
        // The row above varies the ADDRESS on one member. This one varies the MEMBER, because the
        // validator checks the three probes with three separate blocks and a block that was never
        // reached would let an unusable address for that upstream through - Gateway would then start
        // with a probe it can never call, and report an upstream unhealthy for the wrong reason.
        GatewayOptions options = ValidOptions();

        PropertyInfo property = typeof(GatewayOptions.HealthProbeAddresses).GetProperty(member)!;

        Assert.NotNull(property);

        property.SetValue(options.HealthProbes, "ftp://probe:5101");

        ValidationResult result = Assert.Single(Validate(options));

        // The GROUP is named, so a startup failure points at the section to correct...
        Assert.Contains(nameof(GatewayOptions.HealthProbes), result.MemberNames);

        // ...and the MEMBER is named in the message, so it points at the row within it.
        Assert.Contains(member, result.ErrorMessage!, StringComparison.Ordinal);

        // The rejected value is still never echoed, for the reason the row above states.
        Assert.DoesNotContain("ftp://probe:5101", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingProbeGroupIsASingleFatalErrorThatStopsFurtherChecking()
    {
        GatewayOptions options = ValidOptions();
        options.HealthProbes = null!;

        ValidationResult single = Assert.Single(Validate(options));

        Assert.Contains(nameof(GatewayOptions.HealthProbes), single.MemberNames);
        Assert.Contains("C-10", single.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMutualTlsPairIsOptionalAsAGroupAndInseparableWhenPresent()
    {
        // BOTH EMPTY IS VALID: this deployment presents no client certificate and requests no token.
        Assert.Empty(Validate(ValidOptions()));
        Assert.False(new GatewayOptions().MutualTls.IsConfigured);

        // BOTH SET IS VALID.
        GatewayOptions both = ValidOptions();
        both.MutualTls.CertificatePath = "/run/secrets/powerframework/gateway.crt";
        both.MutualTls.CertificateKeyPath = "/run/secrets/powerframework/gateway.key";
        Assert.Empty(Validate(both));
        Assert.True(both.MutualTls.IsConfigured);

        // EITHER ONE ALONE IS REFUSED. A certificate cannot complete a handshake without its key, and a
        // key has nothing to present without its certificate, so half a client identity is unusable
        // rather than merely weaker - and it would fail at the first token request rather than at start.
        foreach ((string? certificate, string? key) in ((string?, string?)[])
            [("/run/secrets/powerframework/gateway.crt", null), (null, "/run/secrets/powerframework/gateway.key")])
        {
            GatewayOptions half = ValidOptions();
            half.MutualTls.CertificatePath = certificate ?? string.Empty;
            half.MutualTls.CertificateKeyPath = key ?? string.Empty;

            ValidationResult single = Assert.Single(Validate(half));

            Assert.Contains(nameof(GatewayOptions.MutualTls), single.MemberNames);

            // NEITHER PATH IS ECHOED. A path names where key material is mounted, and a startup log must
            // not record that.
            Assert.DoesNotContain("gateway.crt", single.ErrorMessage!, StringComparison.Ordinal);
            Assert.DoesNotContain("gateway.key", single.ErrorMessage!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheMutualTlsGroupCarriesPathsAndHasNoMemberThatCouldHoldMaterial()
    {
        // THE ABSENCE IS THE CONTROL. There is no certificate body, no key body and no passphrase member,
        // so C-F cannot be violated by filling one in - there is no such member to fill. Every property
        // is a path, which names material mounted from the orchestration secret layer.
        System.Reflection.PropertyInfo[] properties =
            typeof(GatewayOptions.MutualTlsClientOptions).GetProperties();

        foreach (var property in properties)
        {
            if (property.PropertyType == typeof(bool))
            {
                continue;
            }

            Assert.EndsWith("Path", property.Name, StringComparison.Ordinal);
            Assert.Equal(typeof(string), property.PropertyType);
        }

        foreach (string forbidden in (string[])["Passphrase", "Password", "Pem", "Body", "Material", "Content"])
        {
            Assert.DoesNotContain(
                properties,
                p => p.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
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
        // EXACTLY ONE PROPERTY ON THE WHOLE SURFACE MAY CARRY A CREDENTIAL, AND IT IS NAMED HERE.
        //
        // The sweep below used to forbid every credential-shaped name outright, which read as "Gateway
        // holds nothing sensitive" - and that is not what the token topology says. It says Gateway holds
        // no SIGNING material: it validates tokens and never mints one. It must still AUTHENTICATE to
        // the sole issuer to obtain a token at all, because POST /v1/tokens is the one operation a
        // bearer token cannot protect, so an outbound client credential is required BY the contract.
        //
        // Naming the one permitted property rather than relaxing the marker list keeps the sweep sharp:
        // a SECOND credential-shaped property, or a signing key under any spelling, still fails.
        HashSet<string> permittedCredentialProperties =
            [nameof(GatewayOptions.SecurityClientSecret)];

        foreach (Type type in (Type[])
            [
                typeof(GatewayOptions),
                typeof(GatewayOptions.UpstreamAddresses),
                typeof(GatewayOptions.HealthProbeAddresses),
                typeof(GatewayOptions.MutualTlsClientOptions),
                typeof(JwtBearerVerificationOptions),
            ])
        {
            foreach (var property in type.GetProperties())
            {
                // NO SIGNING MATERIAL, UNDER ANY SPELLING, WITH NO EXCEPTION. A signing key here would
                // be a second signing authority, which is the specific thing the topology forbids.
                foreach (string marker in (string[])["SigningKey", "PrivateKey", "Password"])
                {
                    Assert.False(
                        property.Name.Contains(marker, StringComparison.OrdinalIgnoreCase),
                        $"{type.Name}.{property.Name} looks like signing material. Gateway validates "
                            + "tokens and never issues them; Security is the sole issuer.");
                }

                foreach (string marker in (string[])["Secret", "Credential"])
                {
                    if (!property.Name.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // A BOOLEAN CANNOT CARRY MATERIAL, so a credential-shaped boolean name is a
                    // PREDICATE about the configuration rather than a place to put a credential -
                    // HasIssuanceCredential is the one that exists. Excluding the type rather than
                    // listing the name keeps the check about what a property could hold.
                    if (property.PropertyType == typeof(bool))
                    {
                        continue;
                    }

                    Assert.Contains(property.Name, permittedCredentialProperties);
                }
            }
        }

        // AND THE ONE PERMITTED PROPERTY IS EMPTY IN SOURCE AND HAS NO SETTINGS LEAF. The declaration
        // is here; the material arrives through a flat environment key the section binder cannot reach,
        // which is what keeps it out of every committed file (C-F).
        Assert.Equal(string.Empty, new GatewayOptions().SecurityClientSecret);
        Assert.Equal(
            "SECURITY_CLIENT_SECRET_GATEWAY",
            GatewayOptions.SecurityClientSecretConfigurationKey);
        Assert.DoesNotContain(':', GatewayOptions.SecurityClientSecretConfigurationKey);
    }

    // ==============================================================================================
    //  THE ISSUANCE CREDENTIAL - the disjunction, and the one state that is refused
    // ==============================================================================================

    [Fact]
    public void EitherIssuanceSchemeAloneSatisfiesTheRequirementAndNeitherIsRefused()
    {
        // SCHEME ONE ALONE. The documented bring-up path.
        GatewayOptions secretOnly = ValidOptions();
        Assert.True(secretOnly.HasIssuanceCredential);
        Assert.Empty(Validate(secretOnly));

        // SCHEME TWO ALONE. A deployment that terminates TLS at Security and prefers certificates.
        GatewayOptions certificateOnly = ValidOptions();
        certificateOnly.SecurityClientSecret = string.Empty;
        certificateOnly.MutualTls = new GatewayOptions.MutualTlsClientOptions
        {
            CertificatePath = "/run/secrets/gateway-client.pem",
            CertificateKeyPath = "/run/secrets/gateway-client.key",
        };
        Assert.True(certificateOnly.HasIssuanceCredential);
        Assert.Empty(Validate(certificateOnly));

        // BOTH. Legitimate, and not a conflict: the client presents the Basic credential and the
        // handler carries the certificate, and Security accepts either.
        GatewayOptions both = ValidOptions();
        both.MutualTls = certificateOnly.MutualTls;
        Assert.True(both.HasIssuanceCredential);
        Assert.Empty(Validate(both));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ADeploymentThatCanPresentNeitherSchemeIsRefusedNamingBothKeysAndQuotingNoValue(
        string? secret)
    {
        GatewayOptions options = ValidOptions();
        options.SecurityClientSecret = secret!;

        Assert.False(options.HasIssuanceCredential);

        ValidationResult[] results = Validate(options);

        ValidationResult refusal = Assert.Single(results);
        string message = refusal.ErrorMessage ?? string.Empty;

        // BOTH KEYS ARE NAMED, so an operator can act on either.
        Assert.Contains(GatewayOptions.SecurityClientSecretConfigurationKey, message, StringComparison.Ordinal);
        Assert.Contains("Gateway:MutualTls", message, StringComparison.Ordinal);

        // AND THE REFUSAL POINTS AT BOTH MEMBERS rather than only the one that happened to be read
        // last, because either one being set would resolve it.
        Assert.Contains(nameof(GatewayOptions.SecurityClientSecret), refusal.MemberNames);
        Assert.Contains(nameof(GatewayOptions.MutualTls), refusal.MemberNames);
    }

    [Fact]
    public void TheIssuanceDisjunctionIsComputedAndThereforeBindsNothing()
    {
        // A computed property has no setter, so no configuration key can assert "this deployment can
        // authenticate" independently of whether it actually can.
        System.Reflection.PropertyInfo? property =
            typeof(GatewayOptions).GetProperty(nameof(GatewayOptions.HasIssuanceCredential));

        Assert.NotNull(property);
        Assert.False(property.CanWrite);
    }

    [Fact]
    public void AnUnsetMutualTlsGroupIsNotTreatedAsAConfiguredOne()
    {
        // The disjunction reads MutualTls.IsConfigured, and a null group must answer "not configured"
        // rather than faulting: the public setter makes null reachable, and a NullReferenceException
        // inside a validator would be reported as a host crash rather than as a configuration error.
        GatewayOptions options = ValidOptions();
        options.SecurityClientSecret = string.Empty;
        options.MutualTls = null!;

        Assert.False(options.HasIssuanceCredential);
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
        // STARTED FROM A VALID CONFIGURATION AND BROKEN IN EXACTLY TWO PLACES. Constructing a bare
        // instance instead would also be missing the issuance credential, and the third result that
        // produced would make this test pass or fail for a reason it is not about.
        GatewayOptions options = ValidOptions();
        options.Upstreams.DataServices = string.Empty;
        options.Upstreams.Security = "ftp://x";

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

    // ==============================================================================================
    //  THE RETRY COUNT - THE ONE SETTING WHOSE DOCUMENTED DOMAIN WAS NOT ITS DEPLOYABLE DOMAIN
    // ==============================================================================================

    /// <summary>
    /// The retry count is accepted across its whole documented domain and refused outside it.
    /// </summary>
    /// <param name="attempts">The configured value.</param>
    /// <param name="accepted">Whether the validator must accept it.</param>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THE TWO BOUNDS EXIST FOR TWO DIFFERENT OBSERVED FAILURES, AND NEITHER WAS A THEORY.</b>
    /// </para>
    /// <para>
    /// ZERO was documented as disabling retries and validated only against being negative, while both
    /// retry layers consumed it unconditionally - and neither accepts it. The resilience package declares
    /// its retry strategy's count in the range one to <see cref="int.MaxValue"/>, so a configured zero
    /// made the host FAIL TO START: "The field &lt;client&gt;-standard.Retry.MaxRetryAttempts must be
    /// between 1 and 2147483647", raised for all three named pipelines at once. So the single value an
    /// operator would reach for to turn retry off was the one value that could not be deployed. The
    /// composition root now branches on it, and this row is what keeps zero a legal configuration.
    /// </para>
    /// <para>
    /// <see cref="int.MaxValue"/> was legal and SILENTLY BROKE RETRY rather than failing. The gRPC layer
    /// increments the count to an attempt count, and unchecked <c>int.MaxValue + 1</c> wraps to
    /// <see cref="int.MinValue"/>; the channel and its retry policy both ACCEPTED that negative count and
    /// the pipeline built successfully - a service that started, reported itself healthy and had a
    /// nonsensical retry policy. The ceiling refuses the input instead, and the increment is checked so
    /// the arithmetic cannot fail quietly even if the ceiling were ever widened.
    /// </para>
    /// <para>
    /// THE ROWS ARE THE BOUNDARIES AND THEIR NEIGHBOURS, because a range check is exactly where an
    /// off-by-one hides: the two accepted extremes, the two values just outside them, and the extreme that
    /// caused the overflow.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(GatewayOptions.OutboundCallOptions.MaxRetryAttemptsCeiling, true)]
    [InlineData(GatewayOptions.OutboundCallOptions.MaxRetryAttemptsCeiling + 1, false)]
    [InlineData(-1, false)]
    [InlineData(int.MaxValue, false)]
    public void TheRetryCountIsAcceptedAcrossItsDocumentedDomainAndRefusedOutsideIt(
        int attempts,
        bool accepted)
    {
        GatewayOptions options = ValidOptions();
        options.Outbound.MaxRetryAttempts = attempts;

        ValidationResult[] failures = Validate(options);

        if (accepted)
        {
            Assert.Empty(failures);

            return;
        }

        ValidationResult failure = Assert.Single(failures);

        Assert.Contains(
            "Outbound:MaxRetryAttempts",
            failure.ErrorMessage ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Zero answers "no retrying", and every accepted positive value answers the count plus one.
    /// </summary>
    /// <remarks>
    /// THE TWO DERIVED READINGS ARE ASSERTED TOGETHER because the composition root branches on the first
    /// and consumes the second, so a build where they disagreed would install a retry policy for a
    /// deployment that asked for none. The attempt count is INCLUSIVE - a gRPC retry policy counts
    /// attempts where this setting counts retries - and the increment is checked, which is why the
    /// int.MaxValue row above is refused by validation rather than left to wrap.
    /// </remarks>
    [Fact]
    public void TheRetryCountAnswersWhetherRetryingIsEnabledAndTheInclusiveAttemptCount()
    {
        GatewayOptions.OutboundCallOptions outbound = new();

        Assert.Equal(3, outbound.MaxRetryAttempts);
        Assert.True(outbound.RetriesEnabled);
        Assert.Equal(4, outbound.ResolveGrpcAttemptCount());

        outbound.MaxRetryAttempts = 0;
        Assert.False(outbound.RetriesEnabled);

        outbound.MaxRetryAttempts = 1;
        Assert.True(outbound.RetriesEnabled);
        Assert.Equal(2, outbound.ResolveGrpcAttemptCount());

        outbound.MaxRetryAttempts = GatewayOptions.OutboundCallOptions.MaxRetryAttemptsCeiling;
        Assert.Equal(11, outbound.ResolveGrpcAttemptCount());

        // THE CHECKED ARITHMETIC IS ASSERTED RATHER THAN ASSUMED. Validation refuses this value, so this
        // is the guard behind the guard: if the ceiling were ever widened to int.MaxValue the increment
        // would THROW instead of wrapping to a negative count that every layer accepts.
        outbound.MaxRetryAttempts = int.MaxValue;
        Assert.Throws<OverflowException>(() => outbound.ResolveGrpcAttemptCount());
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
