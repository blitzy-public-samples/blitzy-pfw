// ==================================================================================================
//  SIGNING-KEY POLICY - THE FORMAT DISCRIMINATOR, AND THE FLOOR THAT APPLIES TO THE ISSUER KEY ALONE
// ==================================================================================================
//
//  WHY THIS FILE EXISTS AS ITS OWN FILE RATHER THAN AS CASES BOLTED ONTO SigningKeyProviderTests
//
//  Two decisions about the issuer's own key material need holding in place, and they pull in opposite
//  directions, so they are asserted together where the tension is visible:
//
//    * Security:SigningKeyFormat IS BOUND AND ENFORCED. An unrecognised encoding is refused at startup
//      rather than reaching an import that would fail for an unexplained reason. A setting that is
//      declared, documented and inert is a FALSE ASSURANCE, which is worse than no setting at all.
//    * A KEY BELOW SecurityOptions.MinimumSigningKeySizeBits IS REFUSED, AND NO SETTING EXISTS THAT
//      COULD LOWER THAT FLOOR. This file previously asserted the opposite, and the reasoning it carried
//      was wrong in one specific way that is worth recording rather than quietly deleting: it read AAP
//      0.6.6.4's 1024-bit allowance as governing this key. It does not. That section governs the C-02
//      CRYPTOGRAPHIC SURFACE - the operations n_crypto actually published, whose weak defaults a caller
//      can observe today and which C-B forbids correcting. The legacy has NO token issuer, NO JWT, NO
//      JWKS and NO signing identity of any kind; it opens no listening socket at all [AAP 0.1.4]. So
//      there is no legacy behaviour here for a floor to correct, and what governs a boundary the
//      decomposition CREATED is AAP G7 and constraint C-G - every created boundary authenticated from
//      the outset - plus the enterprise baseline of AAP 0.7.2 where no legacy behaviour speaks.
//
//  THE TWO SURFACES NOW DIFFER DELIBERATELY, AND THAT IS WHAT THESE CASES PIN
//
//    * SigningKeyProvider REFUSES a modulus below the floor, disposing the imported key and throwing, so
//      the host does not start. Tested by constructing over a 1024-bit key and asserting the throw, the
//      critical log record, and that neither the message nor the record echoes the material.
//    * SecurityOptionsValidator reports the SAME floor from the SAME constant, so a deployment gets a
//      configuration diagnostic beside its other startup faults rather than a bare constructor crash.
//    * C-02's key-GENERATION surface is UNCHANGED. RsaProvider.GenRSAKey still accepts 1024 bits and
//      LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS still publishes it
//      [ws_objects/pfw.shared.pbl.src/enums.sru:L965]. The divergence case asserts BOTH halves at once -
//      the issuer refusing and the legacy generator accepting - so that neither can be "harmonised" into
//      the other by a later edit in either direction.
//    * NO CONFIGURABLE FLOOR EXISTS. A floor an operator can lower is not a floor, so the reflection
//      guard that once proved the absence of any size PROPERTY is kept and re-aimed: the floor must be a
//      constant, and no bound setting may govern the modulus.
//
//  KEY HYGIENE. Every key in this file is GENERATED IN THIS PROCESS. No key literal appears, and
//  nothing is copied from any of the eight hardcoded-secret sites the repository carries [AAP 0.6.6.1],
//  which are read-only legacy material and are never replicated.
// ==================================================================================================

using System.Security.Cryptography;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using PowerFramework.Security.Configuration;
using PowerFramework.Security.Crypto;
using PowerFramework.Security.Tokens;

using Xunit;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Verifies that the signing-key format discriminator is bound and enforced, that a modulus below
/// <see cref="SecurityOptions.MinimumSigningKeySizeBits"/> is refused rather than merely remarked on, and
/// that no configuration surface exists which could lower that floor.
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
    /// The size the oracle keeps legal on its own C-02 generation surface, and which this service's
    /// ISSUER identity therefore refuses. The two are different surfaces, and that difference is the
    /// point of several cases below.
    /// </summary>
    private const int LegacySmallestKeySizeBits = 1024;

    /// <summary>
    /// The documented default is the value the settings file states, the floor is a CONSTANT, and no
    /// bound setting on the options type governs the modulus.
    /// </summary>
    /// <remarks>
    /// The second half is a lowering guard rather than a restatement of the first: it walks the bound
    /// properties by reflection, so a future edit that turned the floor into configuration under any
    /// spelling fails here even if every other case in this file were left untouched. A floor an operator
    /// can lower is not a floor, and the operator most likely to lower it is one whose deployment already
    /// holds a short key.
    /// </remarks>
    [Fact]
    public void TheDocumentedDefaultIsTheDeclaredDefaultAndTheFloorIsNotConfigurable()
    {
        SecurityOptions options = new();

        Assert.Equal(SecurityOptions.PermittedSigningKeyFormat, options.SigningKeyFormat);
        Assert.Equal(2048, SecurityOptions.MinimumSigningKeySizeBits);

        string[] sizeProperties =
        [
            .. typeof(SecurityOptions)
                .GetProperties()
                .Select(static property => property.Name)
                .Where(static name =>
                    name.Contains("Minimum", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("SizeBits", StringComparison.OrdinalIgnoreCase)),
        ];

        Assert.True(
            sizeProperties.Length == 0,
            "SecurityOptions declares a key-size PROPERTY: " + string.Join(", ", sizeProperties) +
            ". The signing-key floor must stay the constant MinimumSigningKeySizeBits. A bound setting " +
            "would be a floor an operator could lower, which returns this estate to the state the floor " +
            "closes - a weak modulus signing every credential every service accepts.");
    }

    /// <summary>
    /// 🔴 THE DIVERGENCE PROOF. At one and the same key size the ISSUER identity refuses and the LEGACY
    /// generation surface accepts, and both halves are asserted together so neither can be harmonised
    /// into the other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the case that carries the whole argument, which is why both halves live in one test rather
    /// than in two that could drift. If a later edit removed the issuer floor - the change a reader of AAP
    /// 0.6.6.4 is most likely to make, because that section really does keep 1024-bit RSA legal - the
    /// first half fails. If a later edit "tidied" the legacy generator to match the issuer, which WOULD be
    /// the silent legacy correction C-B forbids, the second half fails.
    /// </para>
    /// <para>
    /// WHY THE TWO DIFFER. AAP 0.6.6.4 governs the C-02 surface: operations the oracle published, whose
    /// weak defaults are observable behaviour. Token issuance is not among them - the legacy has no token
    /// issuer at all - so the issuer key is a boundary this decomposition created, and constraint C-G
    /// governs it. The legacy half asserts the published constant as well as the behaviour, because that
    /// constant is what documents the allowance to a reader
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L965</c>].
    /// </para>
    /// </remarks>
    [Fact]
    public void TheIssuerRefusesTheLegacyAllowanceTheGenerationSurfaceStillAccepts()
    {
        // ---- half one: the ISSUER identity refuses 1024 bits and never produces a credential ----
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => CreateProvider(GeneratePkcs8Base64(LegacySmallestKeySizeBits)));

        Assert.Contains("1024", refused.Message, StringComparison.Ordinal);
        Assert.Contains("2048", refused.Message, StringComparison.Ordinal);

        // ---- half two: C-02's generation surface still accepts exactly that size ----
        Assert.Equal(
            LegacySmallestKeySizeBits,
            LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS);

        RsaProvider legacyGeneration = new(new PowerFramework.Security.Crypto.EncodingProvider());
        string privateKey = string.Empty;
        string publicKey = string.Empty;

        bool generated = legacyGeneration.GenRSAKey(
            LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS,
            ref privateKey,
            ref publicKey);

        Assert.True(
            generated,
            "The legacy key-generation surface must still accept 1024 bits. Refusing it here would be " +
            "the silent legacy correction this refactor forbids [enums.sru:L965, AAP 0.6.6.4]. The " +
            "issuer floor is scoped to the issuer's own signing identity and must not reach this " +
            "surface.");
        Assert.NotEmpty(privateKey);
        Assert.NotEmpty(publicKey);
    }

    /// <summary>
    /// 🔴 A short key is REFUSED, and both the exception and the log record name the measured size without
    /// echoing the material.
    /// </summary>
    /// <remarks>
    /// The record is asserted as well as the throw, because an operator reading a startup failure needs to
    /// know WHICH modulus was too short: "too short" alone sends them to guess. A modulus length is not a
    /// secret - this service publishes it in the key set it serves anonymously - so naming it discloses
    /// nothing, and the assertion additionally proves the key material itself appears in neither.
    /// </remarks>
    [Fact]
    public void AShortKeyIsRefusedAndTheRefusalNamesTheMeasuredSizeOnly()
    {
        string shortKey = GeneratePkcs8Base64(LegacySmallestKeySizeBits);
        RecordingLogger<SigningKeyProvider> logger = new();

        SecurityOptions options = new()
        {
            Issuer = TestIssuer,
            SigningKeyId = TestKeyId,
            SigningAlgorithm = SecurityAlgorithms.RsaSha256,
            SigningKey = shortKey,
        };

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => new SigningKeyProvider(Options.Create(options), logger));

        Assert.Contains("1024", refused.Message, StringComparison.Ordinal);
        Assert.Contains("2048", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(shortKey, refused.Message, StringComparison.Ordinal);

        (LogLevel level, string message) = Assert.Single(logger.Records);

        Assert.Equal(LogLevel.Critical, level);
        Assert.Contains("1024", message, StringComparison.Ordinal);
        Assert.Contains("2048", message, StringComparison.Ordinal);
        Assert.DoesNotContain(shortKey, message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A key at or above the floor constructs normally and draws no remark.
    /// </summary>
    /// <param name="keySizeBits">The generated key size.</param>
    /// <remarks>
    /// The silence is half the assertion. 2048 is included deliberately as the BOUNDARY: the floor is
    /// inclusive, so the smallest acceptable key must construct, and an off-by-one that made it exclusive
    /// would refuse every deployment following the documented generation procedure.
    /// </remarks>
    [Theory]
    [InlineData(2048)]
    [InlineData(3072)]
    public void AKeyAtOrAboveTheFloorConstructsAndDrawsNoRemark(int keySizeBits)
    {
        RecordingLogger<SigningKeyProvider> logger = new();

        SecurityOptions options = new()
        {
            Issuer = TestIssuer,
            SigningKeyId = TestKeyId,
            SigningAlgorithm = SecurityAlgorithms.RsaSha256,
            SigningKey = GeneratePkcs8Base64(keySizeBits),
        };

        using SigningKeyProvider provider = new(Options.Create(options), logger);

        Assert.NotNull(provider.SigningCredentials);
        Assert.Equal(keySizeBits, provider.SigningKeySizeBits);
        Assert.Empty(logger.Records);
    }

    /// <summary>
    /// A STRAY MINIMUM-SIZE KEY IN CONFIGURATION GOVERNS NOTHING - IN EITHER DIRECTION.
    /// </summary>
    /// <remarks>
    /// The floor is a constant, so a deployment that writes <c>Security:SigningKeyMinimumSizeBits</c> into
    /// its own settings changes nothing: an unmatched configuration key binds to nothing silently. Both
    /// directions are asserted from the one stray key, because both are ways a reader could believe the
    /// setting works. A value ABOVE the constant does not tighten the floor - the 2048-bit key is still
    /// accepted - and, by the same mechanism, a value BELOW it could not loosen one. It goes through the
    /// same mechanism the host uses, <c>Bind</c> over the named section, so it would fail if a property of
    /// that name were reintroduced.
    /// </remarks>
    [Fact]
    public void AStrayMinimumSizeKeyInConfigurationGovernsNothing()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SecurityOptions.SectionName + ":" + nameof(SecurityOptions.Issuer)] = TestIssuer,
                [SecurityOptions.SectionName + ":" + nameof(SecurityOptions.SigningKeyId)] = TestKeyId,
                [SecurityOptions.SectionName + ":" + nameof(SecurityOptions.SigningAlgorithm)] =
                    SecurityAlgorithms.RsaSha256,
                [SecurityOptions.SectionName + ":SigningKeyMinimumSizeBits"] = "4096",
            })
            .Build();

        SecurityOptions options = new();

        configuration.GetSection(SecurityOptions.SectionName).Bind(options);

        // The signing key itself arrives through a FLAT key rather than through the section, exactly as
        // it does in the host, because the environment provider only folds a double underscore into a
        // section separator.
        options.SigningKey = GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits);

        ValidateOptionsResult result = new SecurityOptionsValidator().Validate(
            Options.DefaultName,
            options);

        Assert.DoesNotContain(
            result.Failures ?? [],
            failure => failure.Contains("4096", StringComparison.Ordinal));

        using SigningKeyProvider provider = new(Options.Create(options));

        Assert.Equal(SecurityOptions.MinimumSigningKeySizeBits, provider.SigningKeySizeBits);
    }

    /// <summary>
    /// 🔴 The validator REPORTS the floor for a short key, naming the measured size and the minimum, and
    /// reports nothing at all for a key at the floor.
    /// </summary>
    /// <remarks>
    /// The validator accumulates every failure it finds, so each half asserts the presence or absence of
    /// ITS OWN failure rather than overall success - the object under test declares no audience roster and
    /// would fail for that unrelated reason. Reporting here as well as in the provider is what turns a bare
    /// constructor crash into a configuration diagnostic that arrives beside every other startup fault, and
    /// the material is asserted absent from the message for the same reason it is absent from every other
    /// rejection in the options layer (constraint C-F).
    /// </remarks>
    [Fact]
    public void TheValidatorReportsTheFloorForAShortKeyAndNothingForAKeyAtIt()
    {
        string shortKey = GeneratePkcs8Base64(LegacySmallestKeySizeBits);

        ValidateOptionsResult tooShort = Validate(options => options.SigningKey = shortKey);

        string failure = Assert.Single(
            tooShort.Failures ?? [],
            candidate => candidate.Contains("1024", StringComparison.Ordinal));

        Assert.Contains("2048", failure, StringComparison.Ordinal);
        Assert.Contains(
            SecurityOptions.SigningKeyEnvironmentVariableName,
            failure,
            StringComparison.Ordinal);
        Assert.DoesNotContain(shortKey, failure, StringComparison.Ordinal);

        ValidateOptionsResult atTheFloor = Validate(
            options => options.SigningKey =
                GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits));

        Assert.DoesNotContain(
            atTheFloor.Failures ?? [],
            candidate => candidate.Contains("2048-bit minimum", StringComparison.Ordinal));
    }

    /// <summary>
    /// Unusable material reports ONE fault, and it is a usability fault rather than a size one.
    /// </summary>
    /// <remarks>
    /// Material that cannot be imported at all is a BROKEN configuration rather than a weak one, so it
    /// remains fatal - the fail-fast posture of <c>ws_objects/pfw.pbl.src/pfw.sra:L143</c>. What must not
    /// happen is a second message about its size, which would send an operator to a rule that no longer
    /// exists.
    /// </remarks>
    [Fact]
    public void UnusableMaterialIsReportedAsUnusableAndNothingElse()
    {
        ValidateOptionsResult result = Validate(
            options => options.SigningKey =
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        Assert.True(result.Failed);

        string failure = Assert.Single(
            result.Failures ?? [],
            candidate => candidate.Contains(
                SecurityOptionsValidator.SigningKeyUnusableMessage,
                StringComparison.Ordinal));

        // One fault, one message, and it says nothing about a size: the measurement of material that
        // never imported is meaningless, and there is no size rule to report against in any case.
        Assert.DoesNotContain("bits", failure, StringComparison.OrdinalIgnoreCase);
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
    /// Both accepted encodings are measured the same way, so the floor does not depend on which shape the
    /// material arrived in.
    /// </summary>
    /// <remarks>
    /// NEITHER SHAPE MAY BE REFUSED - the legacy generator's armoured output is an OPTIONAL fourth
    /// argument [<c>ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20</c>], so material genuinely
    /// exists in both. The measurement reads the modulus of the IMPORTED key, which means it has to work
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

        Assert.Equal(2048, fromArmoured.SigningKeySizeBits);
        Assert.Equal(2048, fromBase64.SigningKeySizeBits);

        // The same short key through both paths, so a floor applied on one path only would be visible as
        // a disagreement here rather than as an unremarked short key later.
        using RSA armouredShort = RSA.Create(LegacySmallestKeySizeBits);

        Assert.Throws<InvalidOperationException>(
            () => CreateProvider(armouredShort.ExportPkcs8PrivateKeyPem()));
        Assert.Throws<InvalidOperationException>(
            () => CreateProvider(Convert.ToBase64String(armouredShort.ExportPkcs8PrivateKey())));
    }

    /// <summary>
    /// THE FORMAT SETTING BINDS FROM ITS CONFIGURATION KEY. This is the assertion that would have caught
    /// the original inert-setting defect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other case here sets the property directly, and every one of them would still pass against
    /// a leaf that nothing read - which is exactly the state this repaired: a key declared and documented
    /// in appsettings.json, bound by nothing. So this case goes through the SAME MECHANISM THE HOST USES,
    /// <c>Bind(configuration.GetSection(SecurityOptions.SectionName))</c>, and asserts the value arrives.
    /// </para>
    /// <para>
    /// The section name is read from the constant rather than written out, so a rename cannot leave this
    /// test passing against a section the host no longer reads.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSigningKeyFormatBindsFromItsConfigurationKey()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SecurityOptions.SectionName + ":" + nameof(SecurityOptions.SigningKeyFormat)] =
                    "PemOrPkcs8Base64",
            })
            .Build();

        SecurityOptions options = new();

        configuration.GetSection(SecurityOptions.SectionName).Bind(options);

        Assert.Equal("PemOrPkcs8Base64", options.SigningKeyFormat);
    }

    /// <summary>
    /// A minimal <see cref="ILogger{TCategoryName}"/> that keeps every record for assertion.
    /// </summary>
    /// <typeparam name="TCategory">The logger category.</typeparam>
    /// <remarks>
    /// Hand-written rather than mocked: the only thing any case here needs is the level and the formatted
    /// message, and a formatted message is what an operator reads.
    /// </remarks>
    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        /// <summary>Every record written, in order.</summary>
        public List<(LogLevel Level, string Message)> Records { get; } = [];

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Records.Add((logLevel, formatter(state, exception)));
        }
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
