// ==================================================================================================
//  TokenLifetimeCeilingTests.cs - A SHORT-LIVED TOKEN IS SHORT-LIVED BECAUSE SOMETHING SAYS SO
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  This estate has no revocation mechanism. There is no introspection endpoint, no deny-list and no
//  session store: once minted, a token is accepted by all four services until it expires, so the
//  LIFETIME is the entire bound on the damage a leaked token can do. The settings file ships five
//  minutes and the documentation describes a five-minute posture - but `Security:TokenLifetime` is a
//  bound setting, and before this ceiling existed the only rule on it was that it be POSITIVE. A
//  deployment could configure a year, satisfy every check, report healthy, and mint credentials that
//  outlive the deployment itself.
//
//  WHY THE CEILING IS A CONSTANT AND NOT A SETTING
//  A ceiling a deployment can raise is not a ceiling. The value is three times the documented posture,
//  which leaves room for a deployment that genuinely needs longer-lived service tokens without leaving
//  room for one that has stopped thinking about it.
//
//  WHAT IS ASSERTED
//    * The constant's value, so a silent widening is a failing test rather than a quiet policy change.
//    * The boundary in both directions: exactly at the ceiling is accepted, one tick over is refused.
//    * The refusal names both durations, because "too long" without the limit is not actionable.
//    * A non-positive lifetime still reports the ORIGINAL fault and not the ceiling, since a zero
//      lifetime is not a long one and reporting both would be two messages for one mistake.
//    * The published contract's `maximum` agrees with the constant, because a bound a caller cannot
//      discover is a bound the caller finds out about by being refused.
// ==================================================================================================

using System.Globalization;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using PowerFramework.Security.Configuration;

using Xunit;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Verifies that the minted-token lifetime has an enforced upper bound, that the bound is not
/// configurable, and that the published contract states the same number.
/// </summary>
public sealed class TokenLifetimeCeilingTests
{
    /// <summary>An issuer identity that can never resolve, so nothing here could reach a network.</summary>
    private const string TestIssuer = "https://security.invalid";

    /// <summary>The key identifier used throughout. Its value is immaterial to every assertion here.</summary>
    private const string TestKeyId = "token-lifetime-ceiling-tests";

    /// <summary>The `expires_in` member the published contract bounds.</summary>
    private const string ExpiresInMaximumMarker = "maximum: 900";

    /// <summary>
    /// The ceiling is fifteen minutes and is not a configurable member.
    /// </summary>
    /// <remarks>
    /// THE REFLECTION HALF IS THE DURABLE HALF. A future edit that turned the ceiling into a bound setting
    /// under any spelling fails here even if every other row in this file were left untouched - which is
    /// the same guard the signing-key floor carries, for the same reason: the deployment most likely to
    /// raise a ceiling is the one whose tokens already live too long.
    /// </remarks>
    [Fact]
    public void TheCeilingIsFifteenMinutesAndIsNotConfigurable()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), SecurityOptions.MaximumTokenLifetime);

        string[] ceilingProperties =
        [
            .. typeof(SecurityOptions)
                .GetProperties()
                .Select(static property => property.Name)
                .Where(static name =>
                    name.Contains("Maximum", StringComparison.OrdinalIgnoreCase)
                    && name.Contains("Lifetime", StringComparison.OrdinalIgnoreCase)),
        ];

        Assert.True(
            ceilingProperties.Length == 0,
            "SecurityOptions declares a lifetime-ceiling PROPERTY: "
                + string.Join(", ", ceilingProperties)
                + ". The ceiling must stay the constant MaximumTokenLifetime: this estate has no "
                + "revocation mechanism, so the lifetime is the whole bound on a leaked token, and a "
                + "bound a deployment can raise is not a bound.");
    }

    /// <summary>
    /// A lifetime at the ceiling is accepted; one tick over it is refused.
    /// </summary>
    /// <remarks>
    /// ONE TICK IS THE SMALLEST OBSERVABLE STEP OVER, which is what makes this a boundary assertion rather
    /// than a demonstration. A rule written with the wrong comparison operator passes a test that offers it
    /// a year and fails this one.
    /// </remarks>
    [Fact]
    public void ExactlyAtTheCeilingIsAcceptedAndOneTickOverIsRefused()
    {
        Assert.DoesNotContain(
            Validate(SecurityOptions.MaximumTokenLifetime).Failures ?? [],
            failure => failure.Contains("TokenLifetime", StringComparison.Ordinal));

        ValidateOptionsResult over = Validate(
            SecurityOptions.MaximumTokenLifetime + TimeSpan.FromTicks(1));

        Assert.True(over.Failed);

        Assert.Contains(
            over.Failures!,
            failure => failure.Contains("TokenLifetime", StringComparison.Ordinal));
    }

    /// <summary>
    /// The refusal names the configured duration and the ceiling, in a culture-independent rendering.
    /// </summary>
    /// <param name="days">The number of days this row configures.</param>
    /// <remarks>
    /// BOTH DURATIONS OR THE MESSAGE IS NOT ACTIONABLE. An operator told only that the value is too long
    /// has to find the limit in documentation; told both, the edit is obvious. The rendering is asserted as
    /// the invariant one because a startup diagnostic is machine-read as often as it is human-read.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(365)]
    public void TheRefusalNamesTheConfiguredDurationAndTheCeiling(int days)
    {
        TimeSpan configured = TimeSpan.FromDays(days);

        ValidateOptionsResult result = Validate(configured);

        string failure = Assert.Single(
            result.Failures!,
            candidate => candidate.Contains("TokenLifetime", StringComparison.Ordinal));

        Assert.Contains(
            configured.ToString("c", CultureInfo.InvariantCulture),
            failure,
            StringComparison.Ordinal);
        Assert.Contains(
            SecurityOptions.MaximumTokenLifetime.ToString("c", CultureInfo.InvariantCulture),
            failure,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A non-positive lifetime reports its own fault and not the ceiling.
    /// </summary>
    /// <param name="lifetime">The offending value, expressed in ticks so zero and negative both fit.</param>
    /// <remarks>
    /// ONE MISTAKE, ONE MESSAGE. A zero or negative lifetime is not a long one, and a rule that reported
    /// both faults would tell an operator to shorten a duration that is already impossible.
    /// </remarks>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void ANonPositiveLifetimeReportsItsOwnFaultAndNotTheCeiling(long ticks)
    {
        ValidateOptionsResult result = Validate(TimeSpan.FromTicks(ticks));

        string failure = Assert.Single(
            result.Failures!,
            candidate => candidate.Contains("TokenLifetime", StringComparison.Ordinal));

        Assert.DoesNotContain(
            SecurityOptions.MaximumTokenLifetime.ToString("c", CultureInfo.InvariantCulture),
            failure,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The published contract states the same ceiling, in seconds, on the member that carries it.
    /// </summary>
    /// <remarks>
    /// A BOUND A CALLER CANNOT READ IS A BOUND IT DISCOVERS BY BEING REFUSED. The document is read as text
    /// rather than through a parser because the assertion is that the number agrees with the constant, and
    /// the constant is what the runtime enforces - so the two are compared directly, with the seconds
    /// conversion done here rather than trusted.
    /// </remarks>
    [Fact]
    public void ThePublishedContractStatesTheSameCeiling()
    {
        string document = File.ReadAllText(LocateContractDocument());

        string expected = "maximum: "
            + ((long)SecurityOptions.MaximumTokenLifetime.TotalSeconds)
                .ToString(CultureInfo.InvariantCulture);

        Assert.Equal(ExpiresInMaximumMarker, expected);
        Assert.Contains(expected, document, StringComparison.Ordinal);
    }

    /// <summary>
    /// Locates the published contract document by walking up from the test assembly.
    /// </summary>
    /// <returns>The absolute path of the document.</returns>
    /// <remarks>
    /// STARTS AT THE EMBEDDED REPOSITORY ROOT WHEN THE BUILD SUPPLIED ONE, so the locator works when the
    /// test output sits outside the checkout - <c>dotnet test --artifacts-path</c> - where no ancestor of
    /// the output directory carries the solution marker. The walk still verifies that marker, so an absent
    /// or stale embedded value simply falls back to the walk. See <c>TestRepositoryRoot</c>.
    /// </remarks>
    private static string LocateContractDocument()
    {
        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, "PowerFramework.slnx")))
            {
                string documentPath = Path.Combine(
                    candidate.FullName,
                    "shared",
                    "PowerFramework.Contracts",
                    "OpenApi",
                    "security.v1.yaml");

                Assert.True(
                    File.Exists(documentPath),
                    FormattableString.Invariant(
                        $"The contract document is missing at '{documentPath}'."));

                return documentPath;
            }

            candidate = candidate.Parent;
        }

        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"No repository root was found above '{AppContext.BaseDirectory}'."));
    }

    /// <summary>
    /// Runs the registered validator over an options instance carrying the supplied lifetime.
    /// </summary>
    /// <param name="lifetime">The lifetime to validate.</param>
    /// <returns>The validation result, failures and all.</returns>
    /// <remarks>
    /// The instance is deliberately NOT made otherwise-valid: the validator accumulates every failure, so
    /// each row asserts the presence or absence of ITS OWN failure and is unaffected by unrelated ones.
    /// </remarks>
    private static ValidateOptionsResult Validate(TimeSpan lifetime)
    {
        SecurityOptions options = new()
        {
            Issuer = TestIssuer,
            SigningKeyId = TestKeyId,
            SigningAlgorithm = SecurityAlgorithms.RsaSha256,
            TokenLifetime = lifetime,
        };

        return new SecurityOptionsValidator().Validate(Options.DefaultName, options);
    }
}
