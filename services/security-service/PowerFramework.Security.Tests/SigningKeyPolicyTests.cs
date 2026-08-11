// ==================================================================================================
//  SIGNING-KEY POLICY - THE ISSUER FLOOR, THE FORMAT DISCRIMINATOR, AND THE SURFACE THEY DO NOT TOUCH
// ==================================================================================================
//
//  WHY THIS FILE EXISTS AS ITS OWN FILE RATHER THAN AS CASES BOLTED ONTO SigningKeyProviderTests
//
//  Two settings - Security:SigningKeyFormat and Security:SigningKeyMinimumSizeBits - were declared in
//  appsettings.json, documented there at length, and then bound by nothing and enforced by nothing. A
//  deployment could set the floor to 4096, read the documentation that says the floor is enforced, and
//  start the service with a 1024-bit signing key. A setting that is declared, documented and inert is a
//  FALSE ASSURANCE, which is strictly worse than no setting at all, and that is the defect these tests
//  exist to keep closed.
//
//  THE ARGUMENT THAT KEPT THEM UNBOUND, AND WHY IT WAS WRONG
//
//  The earlier reasoning - recorded in prose in SecurityOptions.cs and in SigningKeyProvider.cs, and
//  since rewritten in both - ran like this: the legacy constant catalogue keeps CRYPTO_RSA_BITS_1024 =
//  1024 as a first-class legal key size [ws_objects/pfw.shared.pbl.src/enums.sru:L965], this refactor
//  replicates legacy weaknesses rather than correcting them, therefore refusing a 1024-bit key would be
//  a forbidden silent correction.
//
//  The premise is true. The conclusion does not follow, because the premise and the conclusion are
//  about DIFFERENT KEYS:
//
//    * The 1024 allowance governs C-02's key-GENERATION surface, where a caller names the size and
//      byte-for-byte parity with the oracle is the obligation [AAP 0.6.6.4 lists 1024-bit RSA among
//      the weak defaults preserved as ANNOTATED defaults]. RsaProvider.GenRSAKey enforces no minimum,
//      and after this change it still does not.
//    * The floor governs THIS SERVICE'S OWN SIGNING IDENTITY - the trust root every other service
//      validates against. It is NET-NEW: the legacy framework has no token issuer at all, because a
//      library with no listener has nothing to issue tokens for [AAP 0.1.4]. There is therefore no
//      legacy behaviour on this key that a floor could correct.
//
//  So the two rules coexist, and the only way that stays true under later editing is a test that
//  asserts BOTH HALVES AT ONCE. That is what SEPARATION below does: it drives the floor to a refusal
//  and the legacy generator to a success, in the same case, at the same key size. A future change that
//  "harmonised" the two surfaces in either direction would fail it.
//
//  WHAT IS ASSERTED, AND AT WHICH OF THE TWO ENFORCEMENT POINTS
//
//  The floor is enforced twice on purpose and each point is tested at its own level:
//    * SecurityOptionsValidator turns a short key into a NAMED configuration failure, which is what an
//      operator reads. Tested by calling the validator directly.
//    * SigningKeyProvider's constructor makes the guarantee STRUCTURAL: it stands between the import
//      and the point at which SigningCredentials become reachable, so no construction path - including
//      one that never ran options validation - can produce a credential over a short key. Tested by
//      constructing the provider.
//  Neither point is redundant, and a test that only exercised one would let the other be deleted.
//
//  KEY HYGIENE. Every key in this file is GENERATED IN THIS PROCESS. No key literal appears, and
//  nothing is copied from any of the eight hardcoded-secret sites the repository carries [AAP 0.6.6.1],
//  which are read-only legacy material and are never replicated.
// ==================================================================================================

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;

using Microsoft.Extensions.Configuration;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using PowerFramework.Security.Configuration;
using PowerFramework.Security.Crypto;
using PowerFramework.Security.Tokens;

using Xunit;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Verifies that the two signing-key policy settings are bound and enforced, and that enforcing them
/// leaves the legacy key-generation surface exactly as weak as the oracle is.
/// </summary>
public sealed class SigningKeyPolicyTests
{
    /// <summary>
    /// The identifier used throughout. Its value is immaterial to every assertion here.
    /// </summary>
    private const string TestKeyId = "signing-key-policy-tests";

    /// <summary>
    /// An issuer identity for the cases that run the whole validator. It uses a reserved name that can
    /// never resolve, so nothing here could reach a network even by mistake.
    /// </summary>
    private const string TestIssuer = "https://security.invalid";

    /// <summary>
    /// The size the oracle keeps legal and this service refuses for its own identity.
    /// </summary>
    private const int LegacySmallestKeySizeBits = 1024;

    /// <summary>
    /// The two documented defaults are the values the settings file states, and the floor cannot be
    /// configured below itself.
    /// </summary>
    /// <remarks>
    /// Asserted because the defaults are the ONLY policy a deployment that configures nothing gets.
    /// appsettings.json restates both values, so a default that drifted from the file would leave two
    /// sources of truth disagreeing silently for any deployment whose configuration omits the section.
    /// </remarks>
    [Fact]
    public void TheDocumentedDefaultsAreTheDeclaredDefaults()
    {
        SecurityOptions options = new();

        Assert.Equal(SecurityOptions.PermittedSigningKeyFormat, options.SigningKeyFormat);
        Assert.Equal(SecurityOptions.DefaultSigningKeySizeBits, options.SigningKeyMinimumSizeBits);
        Assert.Equal(2048, SecurityOptions.DefaultSigningKeySizeBits);

        // The floor may be raised and never lowered, which is expressed by making the configurable
        // minimum equal to the default rather than by writing a second, lower bound.
        Assert.Equal(
            SecurityOptions.DefaultSigningKeySizeBits,
            SecurityOptions.AbsoluteMinimumSigningKeySizeBits);
    }

    /// <summary>
    /// THE SEPARATION PROOF. At one and the same key size, the issuer identity refuses and the legacy
    /// generation surface accepts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the case that makes the whole change defensible, so it asserts both halves together
    /// rather than trusting two independent tests to stay in agreement. If a later edit lowered the
    /// issuer floor to match the legacy allowance, the first half fails. If a later edit raised the
    /// legacy generator to match the issuer floor - which WOULD be the forbidden silent correction -
    /// the second half fails.
    /// </para>
    /// <para>
    /// The legacy half asserts the published constant as well as the behaviour, because the constant is
    /// what documents the allowance to a reader
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L965</c>].
    /// </para>
    /// </remarks>
    [Fact]
    public void TheIssuerFloorAndTheLegacyGenerationAllowanceAreIndependent()
    {
        // ---- half one: the issuer identity refuses 1024 bits ----
        string shortKey = GeneratePkcs8Base64(LegacySmallestKeySizeBits);

        InvalidOperationException refusal =
            Assert.Throws<InvalidOperationException>(() => CreateProvider(shortKey));

        Assert.Contains(
            LegacySmallestKeySizeBits.ToString(CultureInfo.InvariantCulture),
            refusal.Message,
            StringComparison.Ordinal);

        // ---- half two: C-02's generation surface still accepts exactly that size ----
        Assert.Equal(
            LegacySmallestKeySizeBits,
            LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS);

        RsaProvider legacyGeneration = new(new EncodingProvider());
        string privateKey = string.Empty;
        string publicKey = string.Empty;

        bool generated = legacyGeneration.GenRSAKey(
            LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS,
            ref privateKey,
            ref publicKey);

        Assert.True(
            generated,
            "The legacy key-generation surface must still accept 1024 bits. Refusing it here would be " +
            "the silent legacy correction this refactor forbids [enums.sru:L965, AAP 0.6.6.4].");
        Assert.NotEmpty(privateKey);
        Assert.NotEmpty(publicKey);
    }

    /// <summary>
    /// The provider refuses a short key STRUCTURALLY - before any credential exists - and says why.
    /// </summary>
    /// <remarks>
    /// The message is asserted to carry the measured size, the configured floor and the name of the
    /// setting that carries it, because those three together are what let an operator act without
    /// reading the source. A modulus length is not a secret: this service publishes it in the key set it
    /// serves anonymously, so stating it discloses nothing.
    /// </remarks>
    [Fact]
    public void AShortKeyIsRefusedBeforeAnyCredentialExists()
    {
        string shortKey = GeneratePkcs8Base64(LegacySmallestKeySizeBits);

        InvalidOperationException refusal =
            Assert.Throws<InvalidOperationException>(() => CreateProvider(shortKey));

        Assert.Contains("1024", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("2048", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(
            nameof(SecurityOptions.SigningKeyMinimumSizeBits),
            refusal.Message,
            StringComparison.Ordinal);

        // The refusal must not read as a blanket ban on short RSA keys, because it is not one - so the
        // message names the surface that still allows them.
        Assert.Contains("/v1/crypto", refusal.Message, StringComparison.Ordinal);

        // And it must never echo the key.
        Assert.DoesNotContain(shortKey, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The default floor and anything above it construct normally.
    /// </summary>
    /// <param name="keySizeBits">The generated key size.</param>
    [Theory]
    [InlineData(2048)]
    [InlineData(3072)]
    public void AKeyAtOrAboveTheFloorIsAccepted(int keySizeBits)
    {
        using SigningKeyProvider provider = CreateProvider(GeneratePkcs8Base64(keySizeBits));

        // Reached only if the constructor completed, which is the assertion; reading the credential
        // proves the member the floor guards is genuinely reachable afterwards.
        Assert.NotNull(provider.SigningCredentials);
    }

    /// <summary>
    /// The floor is READ FROM CONFIGURATION rather than compiled in: a deployment that raises it above
    /// its own key is refused.
    /// </summary>
    /// <remarks>
    /// This is the case that proves the setting is genuinely BOUND. Every other floor assertion here
    /// would still pass against a hardcoded 2048, which is exactly the defect being fixed - a value that
    /// looks configured and is not.
    /// </remarks>
    [Fact]
    public void ARaisedFloorIsHonouredAgainstAKeyThatWouldOtherwisePass()
    {
        SecurityOptions options = new()
        {
            Issuer = TestIssuer,
            SigningKeyId = TestKeyId,
            SigningAlgorithm = SecurityAlgorithms.RsaSha256,
            SigningKey = GeneratePkcs8Base64(2048),
            SigningKeyMinimumSizeBits = 4096,
        };

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
            () => new SigningKeyProvider(Options.Create(options)));

        Assert.Contains("4096", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The validator reports a short key as a NAMED configuration failure.
    /// </summary>
    /// <remarks>
    /// Distinct from the provider's structural refusal and asserted separately, because the two serve
    /// different readers: this one is what an operator sees at startup, and it must name the setting
    /// rather than surface as an exception from a cryptographic layer.
    /// </remarks>
    [Fact]
    public void TheValidatorReportsAShortKeyAsAConfigurationFailure()
    {
        ValidateOptionsResult result = Validate(
            options => options.SigningKey = GeneratePkcs8Base64(LegacySmallestKeySizeBits));

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures ?? [],
            failure => failure.Contains(
                nameof(SecurityOptions.SigningKeyMinimumSizeBits),
                StringComparison.Ordinal));
    }

    /// <summary>
    /// A key at the floor produces no size failure at all.
    /// </summary>
    /// <remarks>
    /// The validator accumulates every failure it finds, so this asserts the ABSENCE of the size
    /// failure specifically rather than overall success - the object under test declares no audience
    /// roster and would fail for that unrelated reason.
    /// </remarks>
    [Fact]
    public void TheValidatorRaisesNoSizeFailureForAKeyAtTheFloor()
    {
        ValidateOptionsResult result = Validate(
            options => options.SigningKey = GeneratePkcs8Base64(2048));

        Assert.DoesNotContain(
            result.Failures ?? [],
            failure => failure.Contains(
                nameof(SecurityOptions.SigningKeyMinimumSizeBits),
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Unusable material reports ONE fault, not two.
    /// </summary>
    /// <remarks>
    /// The measured size of material that never imported is meaningless, so a deployment that supplied
    /// random bytes must not additionally be told those bytes are too short. Collapsing one defect into
    /// two messages sends an operator to the wrong place.
    /// </remarks>
    [Fact]
    public void UnusableMaterialIsNotAlsoReportedAsTooShort()
    {
        ValidateOptionsResult result = Validate(
            options => options.SigningKey =
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        Assert.True(result.Failed);

        Assert.Contains(
            result.Failures ?? [],
            failure => failure.Contains(
                SecurityOptionsValidator.SigningKeyUnusableMessage,
                StringComparison.Ordinal));

        Assert.DoesNotContain(
            result.Failures ?? [],
            failure => failure.Contains(
                nameof(SecurityOptions.SigningKeyMinimumSizeBits),
                StringComparison.Ordinal));
    }

    /// <summary>
    /// An encoding this service does not implement is refused at startup, and the refusal names what is
    /// accepted without echoing what was supplied.
    /// </summary>
    /// <param name="format">The unsupported value.</param>
    [Theory]
    [InlineData("Pkcs12")]
    [InlineData("Jwk")]
    [InlineData("Der")]
    public void AnUnsupportedSigningKeyFormatIsRefused(string format)
    {
        ValidateOptionsResult result = Validate(options =>
        {
            options.SigningKey = GeneratePkcs8Base64(2048);
            options.SigningKeyFormat = format;
        });

        Assert.True(result.Failed);

        string failure = Assert.Single(
            result.Failures ?? [],
            candidate => candidate.Contains(
                nameof(SecurityOptions.SigningKeyFormat),
                StringComparison.Ordinal));

        Assert.Contains(
            SecurityOptions.PermittedSigningKeyFormat,
            failure,
            StringComparison.Ordinal);

        // Consistent with every other rejection in the options layer: the supplied value is not echoed.
        // Argument order matters here - the REJECTED VALUE is the substring that must be absent FROM the
        // failure text, not the other way round, and reversing the two makes the assertion vacuous.
        Assert.DoesNotContain(format, failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// The permitted value is accepted whatever its casing.
    /// </summary>
    /// <param name="format">A casing of the one permitted value.</param>
    /// <remarks>
    /// Case-insensitive deliberately: unlike a JOSE algorithm identifier, this value is a name coined by
    /// this project for its own settings file, so no external specification makes its casing
    /// significant and refusing a bring-up over one would be pedantry.
    /// </remarks>
    [Theory]
    [InlineData("PemOrPkcs8Base64")]
    [InlineData("pemorpkcs8base64")]
    [InlineData("PEMORPKCS8BASE64")]
    public void ThePermittedSigningKeyFormatIsAcceptedInAnyCasing(string format)
    {
        ValidateOptionsResult result = Validate(options =>
        {
            options.SigningKey = GeneratePkcs8Base64(2048);
            options.SigningKeyFormat = format;
        });

        Assert.DoesNotContain(
            result.Failures ?? [],
            failure => failure.Contains(
                nameof(SecurityOptions.SigningKeyFormat),
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Both accepted encodings still import at the floor, so enforcing a size did not narrow the shape
    /// contract.
    /// </summary>
    /// <remarks>
    /// NEITHER SHAPE MAY BE REFUSED - the legacy generator's armoured output is an OPTIONAL fourth
    /// argument [<c>ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20</c>], so material genuinely
    /// exists in both. The size check reads the modulus of the IMPORTED key, which means it has to work
    /// identically whichever path imported it; this asserts that it does.
    /// </remarks>
    [Fact]
    public void BothAcceptedEncodingsAreMeasuredTheSameWay()
    {
        using RSA key = RSA.Create(2048);

        using SigningKeyProvider fromArmoured = CreateProvider(key.ExportPkcs8PrivateKeyPem());
        using SigningKeyProvider fromBase64 =
            CreateProvider(Convert.ToBase64String(key.ExportPkcs8PrivateKey()));

        Assert.NotNull(fromArmoured.SigningCredentials);
        Assert.NotNull(fromBase64.SigningCredentials);

        // The same key through both paths, so a floor applied on one path only would be visible as a
        // disagreement here rather than as an accepted short key later.
        using RSA armouredShort = RSA.Create(LegacySmallestKeySizeBits);
        string armouredShortPem = armouredShort.ExportPkcs8PrivateKeyPem();
        string base64Short = Convert.ToBase64String(armouredShort.ExportPkcs8PrivateKey());

        Assert.Throws<InvalidOperationException>(() => CreateProvider(armouredShortPem));
        Assert.Throws<InvalidOperationException>(() => CreateProvider(base64Short));
    }

    /// <summary>
    /// BOTH SETTINGS BIND FROM THEIR CONFIGURATION KEYS. This is the assertion that would have caught
    /// the original defect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other case here sets the properties directly, and every one of them would still pass
    /// against a floor that was hardcoded and a format leaf that nothing read - which is exactly the
    /// state this change repaired: two keys declared and documented in appsettings.json, bound by
    /// nothing. So this case goes through the SAME MECHANISM THE HOST USES,
    /// <c>Bind(configuration.GetSection(SecurityOptions.SectionName))</c>, and asserts the values
    /// arrive.
    /// </para>
    /// <para>
    /// The section name is read from the constant rather than written out, so a rename cannot leave this
    /// test passing against a section the host no longer reads.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothPolicySettingsBindFromTheirConfigurationKeys()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SecurityOptions.SectionName + ":" + nameof(SecurityOptions.SigningKeyFormat)] =
                    "PemOrPkcs8Base64",
                [SecurityOptions.SectionName + ":" +
                    nameof(SecurityOptions.SigningKeyMinimumSizeBits)] = "3072",
            })
            .Build();

        SecurityOptions options = new();

        configuration.GetSection(SecurityOptions.SectionName).Bind(options);

        Assert.Equal("PemOrPkcs8Base64", options.SigningKeyFormat);
        Assert.Equal(3072, options.SigningKeyMinimumSizeBits);
    }

    /// <summary>
    /// A configured floor that BINDS is then genuinely ENFORCED, end to end.
    /// </summary>
    /// <remarks>
    /// Binding and enforcement are asserted together in one case on purpose: a setting that binds but is
    /// never consulted, and a setting that is consulted but never binds, are both the same defect from
    /// an operator's seat - the configured value does not govern. This drives the whole path the host
    /// drives, from configuration keys through to the refusal.
    /// </remarks>
    [Fact]
    public void AFloorSuppliedThroughConfigurationGovernsTheImportedKey()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SecurityOptions.SectionName + ":" + nameof(SecurityOptions.Issuer)] = TestIssuer,
                [SecurityOptions.SectionName + ":" + nameof(SecurityOptions.SigningKeyId)] = TestKeyId,
                [SecurityOptions.SectionName + ":" + nameof(SecurityOptions.SigningAlgorithm)] =
                    SecurityAlgorithms.RsaSha256,
                [SecurityOptions.SectionName + ":" +
                    nameof(SecurityOptions.SigningKeyMinimumSizeBits)] = "4096",
            })
            .Build();

        SecurityOptions options = new();

        configuration.GetSection(SecurityOptions.SectionName).Bind(options);

        // The signing key itself arrives through a FLAT key rather than through the section, exactly as
        // it does in the host, because the environment provider only folds a double underscore into a
        // section separator.
        options.SigningKey = GeneratePkcs8Base64(2048);

        ValidateOptionsResult result = new SecurityOptionsValidator().Validate(
            Options.DefaultName,
            options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures ?? [],
            failure => failure.Contains("4096", StringComparison.Ordinal));

        Assert.Throws<InvalidOperationException>(() => new SigningKeyProvider(Options.Create(options)));
    }

    /// <summary>
    /// The floor cannot be configured BELOW itself: the declared range refuses it.
    /// </summary>
    /// <param name="configuredFloor">A floor below the absolute minimum, or absurdly above it.</param>
    /// <remarks>
    /// Enforced by the declared annotation, which the host activates with
    /// <c>ValidateDataAnnotations()</c>, so this asserts the annotation rather than a hand-written check.
    /// A configuration file able to lower the floor to nothing would make the floor decorative, and the
    /// upper bound exists so that an extra digit is refused at startup instead of rejecting every key
    /// the deployment owns.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1024)]
    [InlineData(2047)]
    [InlineData(1_000_000)]
    public void TheFloorItselfCannotBeConfiguredOutsideItsDeclaredRange(int configuredFloor)
    {
        SecurityOptions options = new()
        {
            Issuer = TestIssuer,
            SigningKeyId = TestKeyId,
            SigningAlgorithm = SecurityAlgorithms.RsaSha256,
            SigningKey = GeneratePkcs8Base64(2048),
            SigningKeyMinimumSizeBits = configuredFloor,
        };

        List<ValidationResult> results = [];

        bool valid = Validator.TryValidateObject(
            options,
            new ValidationContext(options),
            results,
            validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(SecurityOptions.SigningKeyMinimumSizeBits)));
    }

    /// <summary>
    /// Builds a provider over the supplied material with the documented defaults everywhere else.
    /// </summary>
    /// <param name="signingKey">The signing material.</param>
    /// <returns>The constructed provider.</returns>
    private static SigningKeyProvider CreateProvider(string signingKey)
    {
        SecurityOptions options = new()
        {
            Issuer = TestIssuer,
            SigningKeyId = TestKeyId,
            SigningAlgorithm = SecurityAlgorithms.RsaSha256,
            SigningKey = signingKey,
        };

        return new SigningKeyProvider(Options.Create(options));
    }

    /// <summary>
    /// Runs the registered validator over an options instance the caller adjusts.
    /// </summary>
    /// <param name="adjust">Applies the case's settings.</param>
    /// <returns>The validation result, failures and all.</returns>
    /// <remarks>
    /// The instance is deliberately NOT made otherwise-valid. The validator accumulates every failure,
    /// so each case asserts the presence or absence of ITS OWN failure and is unaffected by the
    /// unrelated ones - which keeps these cases from breaking whenever an unrelated rule is added.
    /// </remarks>
    private static ValidateOptionsResult Validate(Action<SecurityOptions> adjust)
    {
        SecurityOptions options = new()
        {
            Issuer = TestIssuer,
            SigningKeyId = TestKeyId,
            SigningAlgorithm = SecurityAlgorithms.RsaSha256,
        };

        adjust(options);

        return new SecurityOptionsValidator().Validate(Options.DefaultName, options);
    }

    /// <summary>
    /// Generates a fresh key of the requested size and renders it as single-line base64 of its
    /// algorithm-tagged binary structure - the shape an environment file can carry.
    /// </summary>
    /// <param name="keySizeBits">The modulus size to generate.</param>
    /// <returns>The rendered private key.</returns>
    private static string GeneratePkcs8Base64(int keySizeBits)
    {
        using RSA key = RSA.Create(keySizeBits);

        return Convert.ToBase64String(key.ExportPkcs8PrivateKey());
    }
}
