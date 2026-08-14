// ==================================================================================================
//  SigningKeyRolloverTests.cs - REPLACING THE ESTATE'S ONE SIGNING KEY WITHOUT REFUSING THE TOKENS
//                               ALREADY IN FLIGHT
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  Gateway, DataServices and Persistence verify against the key set this service publishes, and they
//  CACHE it. Replacing the signing key in place removes the old `kid` in the same instant the new one
//  appears, so every token minted under the previous key is refused at every boundary until each cache
//  refreshes - an estate-wide 401 that presents as a caller fault. The consequence measured before this
//  existed was blunter than that: the key was never rotated at all, because rotating it was an outage.
//
//  A rollover therefore runs TWO keys and mints with ONE. The retiring key is PUBLISHED for verification
//  and never signs, which is what keeps AAP 0.6.6.3's property intact: exactly one component in this
//  estate can mint a token, and exactly one key it mints with, during a rollover as outside one.
//
//  WHAT IS ASSERTED, AND WHY EACH ROW CANNOT BE DROPPED
//    * THE STEADY STATE IS UNCHANGED. One key published, one verification key, no rollover log record.
//      Without this row every row below would pass against a provider that always published two.
//    * THE ROLLOVER STATE PUBLISHES BOTH, under DISTINCT `kid`s, in a two-entry set - and exposes both
//      as verification keys so this service's own inbound handler accepts both generations too.
//    * MINTING USES THE ACTIVE KEY ONLY. Asserted on the signing credential's own key identifier, which
//      is the value that reaches the token header.
//    * THE VALIDATOR REFUSES EVERY HALF-APPLIED SHAPE: material with no identifier (unpublishable),
//      an identifier with no material (publishes nothing while reading as a rollover in progress), two
//      keys under one identifier (an ambiguous set in which a verifier may select the wrong key and
//      report what looks like forgery), and a retiring key below the 2048-bit floor.
//    * NO MESSAGE ECHOES MATERIAL. A rollover is the moment an operator is most likely to paste the
//      wrong thing into the wrong variable, so every refusal is checked for the material it was handed.
//
//  KEY HYGIENE. Every key here is GENERATED IN THIS PROCESS. No key literal appears, and nothing is
//  copied from any of the eight hardcoded-secret sites the repository carries [AAP 0.6.6.1], which are
//  read-only legacy material and are never replicated.
// ==================================================================================================

using System.Security.Cryptography;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using PowerFramework.Security.Configuration;
using PowerFramework.Security.Tokens;

using Xunit;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Verifies the signing-key ring: one key in the steady state, two under distinct identifiers during a
/// rollover, minting from the active key only, and a refusal for every half-applied shape.
/// </summary>
public sealed class SigningKeyRolloverTests
{
    /// <summary>The active key's identifier throughout.</summary>
    private const string ActiveKeyId = "rollover-tests-active";

    /// <summary>The retiring key's identifier throughout. Distinct from the active one by construction.</summary>
    private const string RetiringKeyId = "rollover-tests-retiring";

    /// <summary>
    /// An issuer identity for the cases that run the whole validator. It uses a reserved name that can
    /// never resolve, so nothing here could reach a network even by mistake.
    /// </summary>
    private const string TestIssuer = "https://security.invalid";

    /// <summary>The size the C-02 generation surface keeps legal and the issuer refuses.</summary>
    private const int LegacySmallestKeySizeBits = 1024;

    /// <summary>
    /// The steady state publishes exactly one key and reports no rollover.
    /// </summary>
    /// <remarks>
    /// THE ROW THAT KEEPS THE OTHERS HONEST. A provider that published two keys unconditionally would
    /// satisfy every rollover assertion below while leaving a withdrawn key in the set forever, which is
    /// the opposite of a bounded overlap.
    /// </remarks>
    [Fact]
    public void WithNoRetiringKeyOneKeyIsPublishedAndNothingReportsARollover()
    {
        RecordingLogger log = new();

        using SigningKeyProvider provider = CreateProvider(
            GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits),
            retiringKey: null,
            retiringKeyId: string.Empty,
            log);

        Assert.Single(provider.PublishedKeySet.Keys);
        Assert.Single(provider.PublicVerificationKeys);
        Assert.Equal(ActiveKeyId, provider.PublicVerificationKey.KeyId);
        Assert.Empty(log.Records);
    }

    /// <summary>
    /// A rollover publishes both keys, under distinct identifiers, and offers both for verification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BOTH SURFACES ARE ASSERTED because they serve different consumers and are built separately. The
    /// PUBLISHED set is what the other three services fetch; the VERIFICATION key collection is what this
    /// service's own inbound bearer handler validates against. A rollover that updated only one of them
    /// would either refuse the previous generation at this service's own <c>/v1/ping</c> or publish a key
    /// the estate cannot see.
    /// </para>
    /// <para>
    /// THE PUBLIC HALVES ARE ASSERTED DISTINCT, not merely two in number: a provider that published the
    /// active key twice under two identifiers would satisfy a count and verify nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARolloverPublishesBothKeysUnderDistinctIdentifiers()
    {
        RecordingLogger log = new();

        using SigningKeyProvider provider = CreateProvider(
            GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits),
            GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits),
            RetiringKeyId,
            log);

        Assert.Equal(2, provider.PublishedKeySet.Keys.Length);

        string[] publishedIds = [.. provider.PublishedKeySet.Keys.Select(static key => key.KeyId)];

        Assert.Contains(ActiveKeyId, publishedIds);
        Assert.Contains(RetiringKeyId, publishedIds);

        string[] publishedModuli = [.. provider.PublishedKeySet.Keys.Select(static key => key.Modulus)];

        Assert.Equal(2, publishedModuli.Distinct(StringComparer.Ordinal).Count());

        Assert.Equal(2, provider.PublicVerificationKeys.Length);

        string?[] verificationIds = [.. provider.PublicVerificationKeys.Select(static key => key.KeyId)];

        Assert.Contains(ActiveKeyId, verificationIds);
        Assert.Contains(RetiringKeyId, verificationIds);

        // The record is what tells an operator a rollover is live and names the variable to clear when the
        // overlap has elapsed - the one step of the procedure nothing enforces.
        (LogLevel Level, string Message) record = Assert.Single(log.Records);

        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Contains(
            SecurityOptions.RetiringSigningKeyEnvironmentVariableName,
            record.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Minting uses the ACTIVE key even while the retiring one is published.
    /// </summary>
    /// <remarks>
    /// THE PROPERTY AAP 0.6.6.3 ACTUALLY PROTECTS. A key ring that signed with either key would give the
    /// estate two minting identities, and a withdrawn key would keep minting for as long as an operator
    /// left it configured. The assertion reads the signing credential's own key identifier, which is the
    /// value stamped into the token header, rather than a property that merely describes intent.
    /// </remarks>
    [Fact]
    public void MintingUsesTheActiveKeyWhileTheRetiringKeyIsOnlyPublished()
    {
        using SigningKeyProvider provider = CreateProvider(
            GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits),
            GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits),
            RetiringKeyId,
            log: null);

        SigningCredentials credentials = provider.SigningCredentials;

        Assert.Equal(ActiveKeyId, credentials.Key.KeyId);
        Assert.Equal(ActiveKeyId, provider.PublicVerificationKey.KeyId);
        Assert.Equal(SecurityAlgorithms.RsaSha256, credentials.Algorithm);
    }

    /// <summary>
    /// A half-configured rollover pair is a startup failure naming both configuration keys.
    /// </summary>
    /// <param name="withMaterial">Whether this row supplies the material half.</param>
    /// <remarks>
    /// BOTH DIRECTIONS ARE DRIVEN because they fail for different reasons and an implementation could
    /// easily catch one: material with no identifier cannot be published at all, since a key-set entry
    /// carries a <c>kid</c>; an identifier with no material publishes nothing while reading, to anyone
    /// inspecting the configuration, as a rollover in progress.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AHalfConfiguredRolloverPairIsRefused(bool withMaterial)
    {
        string material = GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits);

        ValidateOptionsResult result = Validate(options =>
        {
            options.SigningKey = GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits);
            options.RetiringSigningKey = withMaterial ? material : null;
            options.RetiringSigningKeyId = withMaterial ? string.Empty : RetiringKeyId;
        });

        Assert.True(result.Failed);

        string failure = Assert.Single(
            result.Failures!,
            candidate => candidate.Contains("matched pair", StringComparison.Ordinal));

        Assert.Contains(
            SecurityOptions.RetiringSigningKeyEnvironmentVariableName,
            failure,
            StringComparison.Ordinal);
        Assert.Contains("RetiringSigningKeyId", failure, StringComparison.Ordinal);
        Assert.DoesNotContain(material, failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two keys under one identifier is refused, because it is an ambiguous key set rather than a rollover.
    /// </summary>
    /// <param name="retiringKeyId">The identifier this row configures, including a padded spelling.</param>
    /// <remarks>
    /// THE PADDED SPELLING IS DRIVEN DELIBERATELY. A verifier selects by the identifier as published, and
    /// the publication path trims - so an identifier that differs only in surrounding whitespace would
    /// publish two entries under one effective <c>kid</c> while passing a naive inequality check.
    /// </remarks>
    [Theory]
    [InlineData(ActiveKeyId)]
    [InlineData("  " + ActiveKeyId + "  ")]
    public void TwoKeysUnderOneIdentifierIsRefused(string retiringKeyId)
    {
        ValidateOptionsResult result = Validate(options =>
        {
            options.SigningKey = GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits);
            options.RetiringSigningKey = GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits);
            options.RetiringSigningKeyId = retiringKeyId;
        });

        Assert.True(result.Failed);

        Assert.Contains(
            result.Failures!,
            failure => failure.Contains("repeats the value of", StringComparison.Ordinal));
    }

    /// <summary>
    /// The retiring key is held to the same 2048-bit floor as the active one, in both enforcement places.
    /// </summary>
    /// <remarks>
    /// A RETIRING KEY IS STILL MATERIAL THE WHOLE ESTATE IS ASKED TO TRUST, so exempting it would put a
    /// weak modulus into the published set through the one door that looks temporary. Both the validator
    /// and the provider are asserted, because a deployment can reach the provider without the validator
    /// only if one of them is missing the rule - which is exactly the drift this row exists to catch.
    /// </remarks>
    [Fact]
    public void AShortRetiringKeyIsRefusedByBothTheValidatorAndTheProvider()
    {
        string shortKey = GeneratePkcs8Base64(LegacySmallestKeySizeBits);

        ValidateOptionsResult result = Validate(options =>
        {
            options.SigningKey = GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits);
            options.RetiringSigningKey = shortKey;
            options.RetiringSigningKeyId = RetiringKeyId;
        });

        Assert.True(result.Failed);

        string validatorFailure = Assert.Single(
            result.Failures!,
            candidate => candidate.Contains(
                SecurityOptions.RetiringSigningKeyEnvironmentVariableName,
                StringComparison.Ordinal));

        Assert.Contains(
            $"{SecurityOptions.MinimumSigningKeySizeBits}-bit minimum",
            validatorFailure,
            StringComparison.Ordinal);
        Assert.DoesNotContain(shortKey, validatorFailure, StringComparison.Ordinal);

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
            () => CreateProvider(
                GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits),
                shortKey,
                RetiringKeyId,
                log: null));

        Assert.Contains(
            SecurityOptions.RetiringSigningKeyEnvironmentVariableName,
            refusal.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(shortKey, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Unusable retiring material is refused, and reported as unusable rather than as a size fault.
    /// </summary>
    /// <remarks>
    /// THE ATTRIBUTION IS THE ASSERTION. Pasting the wrong value into the retiring variable is the most
    /// likely mistake in the whole procedure, and a message about key SIZE would send an operator to
    /// generate a longer key when the value they supplied is not a key at all.
    /// </remarks>
    [Fact]
    public void UnusableRetiringMaterialIsReportedAsUnusable()
    {
        string notAKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        ValidateOptionsResult result = Validate(options =>
        {
            options.SigningKey = GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits);
            options.RetiringSigningKey = notAKey;
            options.RetiringSigningKeyId = RetiringKeyId;
        });

        Assert.True(result.Failed);

        string failure = Assert.Single(
            result.Failures!,
            candidate => candidate.Contains("cannot be imported", StringComparison.Ordinal));

        Assert.Contains(
            SecurityOptions.RetiringSigningKeyEnvironmentVariableName,
            failure,
            StringComparison.Ordinal);
        Assert.DoesNotContain(notAKey, failure, StringComparison.Ordinal);

        // And no size failure is reported for it, because the size of a non-key is not a fact.
        Assert.DoesNotContain(
            result.Failures!,
            candidate => candidate.Contains(
                    SecurityOptions.RetiringSigningKeyEnvironmentVariableName,
                    StringComparison.Ordinal)
                && candidate.Contains("-bit minimum", StringComparison.Ordinal));
    }

    /// <summary>
    /// A fully configured rollover draws no failure of its own from the validator.
    /// </summary>
    /// <remarks>
    /// THE POSITIVE ARM. Every refusal row above would be satisfied by a rule that rejected all rollovers,
    /// which would leave the estate exactly where it started: unable to rotate its one signing key.
    /// </remarks>
    [Fact]
    public void AFullyConfiguredRolloverDrawsNoRetiringFailure()
    {
        ValidateOptionsResult result = Validate(options =>
        {
            options.SigningKey = GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits);
            options.RetiringSigningKey = GeneratePkcs8Base64(SecurityOptions.MinimumSigningKeySizeBits);
            options.RetiringSigningKeyId = RetiringKeyId;
        });

        // The instance is deliberately not otherwise valid, so only THIS rule's failures are asserted
        // absent - which is what keeps the row from breaking when an unrelated rule is added.
        Assert.DoesNotContain(
            result.Failures ?? [],
            failure => failure.Contains(
                SecurityOptions.RetiringSigningKeyEnvironmentVariableName,
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.Failures ?? [],
            failure => failure.Contains("RetiringSigningKeyId", StringComparison.Ordinal));
    }

    /// <summary>
    /// The retiring variable's spelling is the flat one, and it is not a section path.
    /// </summary>
    /// <remarks>
    /// A DOUBLE UNDERSCORE WOULD BE FOLDED INTO A SECTION SEPARATOR by the environment provider, so a
    /// name carrying one would land under a configuration key the composition root does not read and the
    /// rollover would silently not happen. The active key's name is asserted the same way elsewhere; this
    /// row keeps the pair consistent.
    /// </remarks>
    [Fact]
    public void TheRetiringVariableIsAFlatNameCarryingNoSectionSeparator()
    {
        Assert.Equal(
            "SECURITY_JWT_RETIRING_SIGNING_KEY",
            SecurityOptions.RetiringSigningKeyEnvironmentVariableName);
        Assert.DoesNotContain(
            "__",
            SecurityOptions.RetiringSigningKeyEnvironmentVariableName,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds a provider over the supplied ring with the documented defaults everywhere else.
    /// </summary>
    /// <param name="signingKey">The active material.</param>
    /// <param name="retiringKey">The retiring material, or <see langword="null"/> for the steady state.</param>
    /// <param name="retiringKeyId">The retiring identifier.</param>
    /// <param name="log">A logger to observe the rollover record, or <see langword="null"/>.</param>
    /// <returns>The constructed provider.</returns>
    private static SigningKeyProvider CreateProvider(
        string signingKey,
        string? retiringKey,
        string retiringKeyId,
        RecordingLogger? log)
    {
        SecurityOptions options = new()
        {
            Issuer = TestIssuer,
            SigningKeyId = ActiveKeyId,
            SigningAlgorithm = SecurityAlgorithms.RsaSha256,
            SigningKey = signingKey,
            RetiringSigningKey = retiringKey,
            RetiringSigningKeyId = retiringKeyId,
        };

        return new SigningKeyProvider(Options.Create(options), log);
    }

    /// <summary>
    /// Runs the registered validator over an options instance the caller adjusts.
    /// </summary>
    /// <param name="adjust">Applies the case's settings.</param>
    /// <returns>The validation result, failures and all.</returns>
    private static ValidateOptionsResult Validate(Action<SecurityOptions> adjust)
    {
        SecurityOptions options = new()
        {
            Issuer = TestIssuer,
            SigningKeyId = ActiveKeyId,
            SigningAlgorithm = SecurityAlgorithms.RsaSha256,
        };

        adjust(options);

        return new SecurityOptionsValidator().Validate(Options.DefaultName, options);
    }

    /// <summary>
    /// Generates a fresh key of the requested size as single-line base64 of its PKCS#8 structure.
    /// </summary>
    /// <param name="keySizeBits">The modulus size to generate.</param>
    /// <returns>The rendered private key.</returns>
    private static string GeneratePkcs8Base64(int keySizeBits)
    {
        using RSA key = RSA.Create(keySizeBits);

        return Convert.ToBase64String(key.ExportPkcs8PrivateKey());
    }

    /// <summary>A logger that keeps the rendered text of every record written through it.</summary>
    /// <remarks>
    /// The RENDERED message is the assertable artifact: the rollover record names a configuration variable
    /// through a format argument, and a structured-state assertion cannot see that argument go missing.
    /// </remarks>
    private sealed class RecordingLogger : ILogger<SigningKeyProvider>
    {
        /// <summary>Every record written, in order.</summary>
        internal List<(LogLevel Level, string Message)> Records { get; } = [];

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
}
