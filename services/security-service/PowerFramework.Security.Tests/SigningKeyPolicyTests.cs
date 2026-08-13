// ==================================================================================================
//  SIGNING-KEY POLICY - THE FORMAT DISCRIMINATOR, THE WEAK-KEY ANNOTATION, AND THE REFUSAL THAT IS NOT
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
//    * NO KEY IS REFUSED FOR BEING SHORT, AND NO SETTING EXISTS THAT COULD MAKE ONE BE. AAP 0.6.6.4
//      keeps 1024-bit RSA a legal size across this estate - the oracle's catalogue lists
//      CRYPTO_RSA_BITS_1024 = 1024 as first class [ws_objects/pfw.shared.pbl.src/enums.sru:L965] - and
//      requires every weak cryptographic default to be replicated as an ANNOTATED default rather than
//      corrected. An earlier revision declared Security:SigningKeyMinimumSizeBits, defaulted it to 2048
//      and failed startup below that floor; that is the behaviour change C-B forbids however desirable
//      it looks, and these tests are what keep it from returning.
//
//  WHAT REPLACES THE REFUSAL, AND WHERE EACH HALF IS ASSERTED
//
//    * SigningKeyProvider measures the imported modulus, publishes it on SigningKeySizeBits, sets
//      SigningKeyIsLegacyWeak below SecurityOptions.LegacyWeakSigningKeySizeBits, and logs a warning
//      naming the measured size. Tested by constructing the provider over a 1024-bit key and reading
//      all three, including the captured log record.
//    * SecurityOptionsValidator raises NO size failure at all, at any size. Tested by calling the
//      validator directly and asserting the absence.
//    * The issuer and C-02's key-GENERATION surface now share one allowance rather than differing.
//      RsaProvider.GenRSAKey still accepts 1024 bits, and the shared-allowance case asserts both halves
//      at once so that neither can be "harmonised" in the wrong direction by a later edit.
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
/// Verifies that the signing-key format discriminator is bound and enforced, that a short modulus is
/// annotated rather than refused, and that no configuration surface exists which could turn the
/// annotation back into a rejection.
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
    /// The size the oracle keeps legal and this service therefore accepts for its own identity, with an
    /// annotation rather than a refusal.
    /// </summary>
    private const int LegacySmallestKeySizeBits = 1024;

    /// <summary>
    /// The documented default is the value the settings file states, and NO minimum-size setting exists
    /// on the options type at all.
    /// </summary>
    /// <remarks>
    /// The second half is a reintroduction guard rather than a restatement of the first: it walks the
    /// bound properties by reflection, so a future edit that added back a size floor under any spelling
    /// fails here even if every other case in this file were left untouched. The annotation threshold is
    /// asserted to be a CONSTANT and not a property for the same reason - a configurable threshold reads
    /// as a policy an operator could tighten into the rejection AAP 0.6.6.4 forbids.
    /// </remarks>
    [Fact]
    public void TheDocumentedDefaultIsTheDeclaredDefaultAndNoSizeFloorExists()
    {
        SecurityOptions options = new();

        Assert.Equal(SecurityOptions.PermittedSigningKeyFormat, options.SigningKeyFormat);
        Assert.Equal(2048, SecurityOptions.LegacyWeakSigningKeySizeBits);

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
            "SecurityOptions declares a key-size property: " + string.Join(", ", sizeProperties) +
            ". AAP 0.6.6.4 keeps 1024-bit RSA a legal size in this estate and requires the weakness to " +
            "be annotated rather than corrected, so no bound setting may govern the signing key's " +
            "modulus. The annotation threshold is the constant LegacyWeakSigningKeySizeBits.");
    }

    /// <summary>
    /// THE SHARED-ALLOWANCE PROOF. At one and the same key size, the issuer identity accepts and the
    /// legacy generation surface accepts, and the issuer says so rather than staying silent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves are asserted in one case rather than trusted to two independent tests. If a later
    /// edit reintroduced an issuer floor, the first half fails. If a later edit raised the legacy
    /// generator's own allowance - which WOULD be the forbidden silent correction - the second half
    /// fails. The annotation is asserted alongside the acceptance so that "accepted" cannot quietly
    /// become "accepted and unremarked".
    /// </para>
    /// <para>
    /// The legacy half asserts the published constant as well as the behaviour, because the constant is
    /// what documents the allowance to a reader
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L965</c>].
    /// </para>
    /// </remarks>
    [Fact]
    public void TheIssuerAndTheLegacyGenerationSurfaceShareTheLegacyAllowance()
    {
        // ---- half one: the issuer identity accepts 1024 bits, and annotates it ----
        using SigningKeyProvider issuer = CreateProvider(GeneratePkcs8Base64(LegacySmallestKeySizeBits));

        Assert.NotNull(issuer.SigningCredentials);
        Assert.Equal(LegacySmallestKeySizeBits, issuer.SigningKeySizeBits);
        Assert.True(issuer.SigningKeyIsLegacyWeak);

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
            "the silent legacy correction this refactor forbids [enums.sru:L965, AAP 0.6.6.4].");
        Assert.NotEmpty(privateKey);
        Assert.NotEmpty(publicKey);
    }

    /// <summary>
    /// A short key is ACCEPTED, flagged, and reported through the log - not refused.
    /// </summary>
    /// <remarks>
    /// The warning is asserted through a captured log record rather than inferred, because the record is
    /// the only thing an operator ever sees: nothing fails, nothing throws, and the service starts. A
    /// modulus length is not a secret - this service publishes it in the key set it serves anonymously -
    /// so the record states the measured size, and the assertion additionally proves the key material
    /// itself is never echoed.
    /// </remarks>
    [Fact]
    public void AShortKeyIsAcceptedAndAnnotatedRatherThanRefused()
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

        using SigningKeyProvider provider = new(Options.Create(options), logger);

        Assert.NotNull(provider.SigningCredentials);
        Assert.Equal(LegacySmallestKeySizeBits, provider.SigningKeySizeBits);
        Assert.True(provider.SigningKeyIsLegacyWeak);

        (LogLevel level, string message) = Assert.Single(logger.Records);

        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("1024", message, StringComparison.Ordinal);
        Assert.Contains("2048", message, StringComparison.Ordinal);
        Assert.DoesNotContain(shortKey, message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A key at or above the annotation threshold constructs normally and draws no remark.
    /// </summary>
    /// <param name="keySizeBits">The generated key size.</param>
    /// <remarks>
    /// The silence is the assertion. Every key is accepted, so acceptance alone would prove nothing;
    /// what distinguishes these sizes is that neither the flag nor the log record is raised for them.
    /// </remarks>
    [Theory]
    [InlineData(2048)]
    [InlineData(3072)]
    public void AKeyAtOrAboveTheAnnotationThresholdDrawsNoRemark(int keySizeBits)
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
        Assert.False(provider.SigningKeyIsLegacyWeak);
        Assert.Empty(logger.Records);
    }

    /// <summary>
    /// A STRAY MINIMUM-SIZE KEY IN CONFIGURATION GOVERNS NOTHING, and the short key it names still
    /// constructs.
    /// </summary>
    /// <remarks>
    /// This is the case that pins the removal rather than merely the current behaviour. A deployment
    /// upgraded from the revision that carried Security:SigningKeyMinimumSizeBits keeps the key in its
    /// own settings, and an unmatched configuration key binds to nothing silently - so the assertion is
    /// that the service starts anyway with the 1024-bit key that setting would once have refused. It
    /// goes through the same mechanism the host uses, Bind over the named section, so it would fail if a
    /// property of that name were reintroduced.
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
        options.SigningKey = GeneratePkcs8Base64(LegacySmallestKeySizeBits);

        ValidateOptionsResult result = new SecurityOptionsValidator().Validate(
            Options.DefaultName,
            options);

        Assert.DoesNotContain(
            result.Failures ?? [],
            failure => failure.Contains("4096", StringComparison.Ordinal));

        using SigningKeyProvider provider = new(Options.Create(options));

        Assert.Equal(LegacySmallestKeySizeBits, provider.SigningKeySizeBits);
    }

    /// <summary>
    /// The validator raises NO failure for a short key, at any size.
    /// </summary>
    /// <remarks>
    /// The validator accumulates every failure it finds, so this asserts the absence of any failure
    /// MENTIONING the measured size rather than overall success - the object under test declares no
    /// audience roster and would fail for that unrelated reason. A validator that reported a size fault
    /// would fail the host at startup, which is the rejection AAP 0.6.6.4 forbids.
    /// </remarks>
    [Fact]
    public void TheValidatorRaisesNoSizeFailureForAShortKey()
    {
        ValidateOptionsResult result = Validate(
            options => options.SigningKey = GeneratePkcs8Base64(LegacySmallestKeySizeBits));

        Assert.DoesNotContain(
            result.Failures ?? [],
            failure => failure.Contains("1024", StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.Failures ?? [],
            failure => failure.Contains("too short", StringComparison.OrdinalIgnoreCase));
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
    /// Both accepted encodings are measured the same way, so the annotation does not depend on which
    /// shape the material arrived in.
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

        // The same short key through both paths, so an annotation applied on one path only would be
        // visible as a disagreement here rather than as an unremarked short key later.
        using RSA armouredShort = RSA.Create(LegacySmallestKeySizeBits);

        using SigningKeyProvider shortFromArmoured =
            CreateProvider(armouredShort.ExportPkcs8PrivateKeyPem());
        using SigningKeyProvider shortFromBase64 =
            CreateProvider(Convert.ToBase64String(armouredShort.ExportPkcs8PrivateKey()));

        Assert.Equal(LegacySmallestKeySizeBits, shortFromArmoured.SigningKeySizeBits);
        Assert.Equal(LegacySmallestKeySizeBits, shortFromBase64.SigningKeySizeBits);
        Assert.True(shortFromArmoured.SigningKeyIsLegacyWeak);
        Assert.True(shortFromBase64.SigningKeyIsLegacyWeak);
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
