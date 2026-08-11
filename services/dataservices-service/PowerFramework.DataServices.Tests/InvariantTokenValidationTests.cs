// ==================================================================================================
//  InvariantTokenValidationTests - THE FOUR SWITCHES THAT ARE NOT DEPLOYMENT CHOICES
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE GUARDS, AND WHY A GUARD IS NEEDED AT ALL
//
//  This service used to read the four inbound token-validation checks - issuer, audience, lifetime and
//  signature - out of configuration and hand whatever it found to the bearer handler. A settings file
//  could therefore turn any of them off while the host reported healthy, and each one removes a whole
//  class of forgery: without issuer validation a credential from any issuer is accepted, so Security
//  stops being the sole authority this boundary trusts; without audience validation a credential minted
//  for Gateway, Persistence or Security is replayable here, which is precisely what the
//  one-audience-per-token rule of contract C-01 exists to prevent; without lifetime validation the short
//  lifetimes Security mints bound nothing; without signature validation any well-formed token is
//  accepted.
//
//  The remedy has two halves, and BOTH need a test or the pair is only half true:
//
//    1. The composition root now assigns all four LITERALLY, so a configured value cannot reach the
//       handler.
//    2. Because the assignment is literal, a configured `false` would be SILENTLY IGNORED - which is
//       the more dangerous failure of the two, since an operator would believe it applied. So the
//       options validator refuses it and the host does not start. Asserted here per switch.
//
//  Without half 2 a deployment would carry a setting that reads as disabled and behaves as enabled.
//  Without a test for half 2 the refusal could be deleted and every other suite would still pass -
//  which is the specific reason this file exists rather than the assertion living inside the large
//  options suite, where a reader would not find it by name.
//
//  CONSTRAINT COMPLIANCE
//  ------------------------------------------------------------------------------------------------
//  C-G   EVERY NEW BOUNDARY AUTHENTICATED. An authenticated boundary whose validation checks can be
//        switched off from a settings file is authenticated in name only, so this is C-G's enforcement
//        half rather than a configuration nicety.
//  C-H   80 PER CENT LINE COVERAGE PER SERVICE. This file's named share is the disabled-switch arm of
//        Configuration/DataServicesOptions.cs's inbound-token validator - four calls plus the guard
//        clause that skips an enabled switch, none of which any other suite reaches.
//  C-I   EACH SERVICE BUILDS AND TESTS INDEPENDENTLY. Every row is a direct validator call, so no host
//        is booted, no port is bound and no sibling service is required.
// ==================================================================================================

using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Configuration;
using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Proves that the four inbound token-validation checks are invariant on this service: on by default,
/// and refused at startup when a deployment turns one off.
/// </summary>
public sealed class InvariantTokenValidationTests
{
    /// <summary>The validator that owns the refusal.</summary>
    private static readonly JwtAuthenticationOptionsValidator Validator = new();

    /// <summary>
    /// The four member names, which are also the configuration keys under the inbound-token section.
    /// </summary>
    /// <remarks>
    /// SPELLED OUT RATHER THAN REFLECTED. A reflected list would grow silently if a fifth boolean were
    /// added and would then assert a rule nobody had decided - and this type deliberately leaves its
    /// metadata-transport boolean unvalidated, because a plain-http loopback run legitimately needs it
    /// false. The four here are the four that were adjudicated, so they are named.
    /// </remarks>
    private static readonly string[] SwitchNames =
    [
        nameof(JwtAuthenticationOptions.ValidateIssuer),
        nameof(JwtAuthenticationOptions.ValidateAudience),
        nameof(JwtAuthenticationOptions.ValidateLifetime),
        nameof(JwtAuthenticationOptions.ValidateIssuerSigningKey),
    ];

    /// <summary>
    /// Every switch, projected onto theory rows.
    /// </summary>
    /// <returns>One row per switch.</returns>
    public static TheoryData<string> Switches() => [.. SwitchNames];

    /// <summary>
    /// A configuration that turns one check off is refused, and the failure names the key and says why.
    /// </summary>
    /// <param name="switchName">The member that is turned off.</param>
    /// <remarks>
    /// <para>
    /// THE MESSAGE CONTENT IS ASSERTED, NOT JUST THE REFUSAL. An operator meeting this failure has to be
    /// able to fix it without reading the source: the key must appear, the word invariant must appear so
    /// the reader knows it is not a value to be tuned, and the remedy must be stated. A refusal carrying
    /// none of those is a wall rather than a diagnostic.
    /// </para>
    /// <para>
    /// EXACTLY ONE FAILURE MENTIONS THE SWITCH, which rules out a duplicated rule - two rules producing
    /// the same refusal would be two places to keep true and only one would be found when it changed.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Switches))]
    public void DisablingOneInboundValidationCheckIsRefused(string switchName)
    {
        JwtAuthenticationOptions options = Disable(switchName);

        ValidateOptionsResult result = Validator.Validate(name: null, options);

        Assert.True(result.Failed);

        string[] failures = result.Failures?.ToArray() ?? [];

        string offending = Assert.Single(
            failures,
            failure => failure.Contains(switchName, StringComparison.Ordinal));

        Assert.Contains("is false", offending, StringComparison.Ordinal);
        Assert.Contains("invariant", offending, StringComparison.Ordinal);
        Assert.Contains("set it to true", offending, StringComparison.Ordinal);
    }

    /// <summary>
    /// Turning all four off reports all four together rather than one per restart.
    /// </summary>
    /// <remarks>
    /// A DEPLOYMENT WITH SEVERAL DISABLED IS FIXED IN ONE PASS. Reporting the first and stopping would
    /// make an operator restart four times to discover four faults, and each restart would look like a
    /// new problem rather than the same one.
    /// </remarks>
    [Fact]
    public void DisablingEveryInboundValidationCheckReportsAllFourTogether()
    {
        JwtAuthenticationOptions options = Valid();

        options.ValidateIssuer = false;
        options.ValidateAudience = false;
        options.ValidateLifetime = false;
        options.ValidateIssuerSigningKey = false;

        string[] failures = Validator.Validate(name: null, options).Failures?.ToArray() ?? [];

        foreach (string switchName in SwitchNames)
        {
            Assert.Contains(failures, failure => failure.Contains(switchName, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// An instance with all four on validates cleanly, which is what makes every refusal row
    /// attributable.
    /// </summary>
    /// <remarks>
    /// THE CONTROL ROW. Without it a refusal row would prove only that SOMETHING was wrong with the
    /// instance, and would keep passing if the disabled-switch rule were deleted and replaced by an
    /// unrelated rule that happened to mention the same member name.
    /// </remarks>
    [Fact]
    public void AnInstanceWithEveryCheckOnValidatesCleanly()
    {
        Assert.True(Validator.Validate(name: null, Valid()).Succeeded);
    }

    /// <summary>
    /// Builds an inbound-token instance that validates cleanly.
    /// </summary>
    /// <returns>The valid instance.</returns>
    /// <remarks>
    /// The authority, the audience and the permitted-caller roster are supplied because each is required
    /// and each defaults to empty, so a default-constructed instance does not validate. Those
    /// requirements have their own assertions in the options suite; here they are satisfied so that a row
    /// observes only the rule it names.
    /// </remarks>
    private static JwtAuthenticationOptions Valid()
    {
        JwtAuthenticationOptions options = new()
        {
            Authority = "https://security-service:5104",
            Audience = "powerframework-dataservices",
        };

        options.PermittedCallers.Add("powerframework-gateway");

        return options;
    }

    /// <summary>
    /// Builds a clean instance with exactly one named check turned off.
    /// </summary>
    /// <param name="switchName">The member to turn off.</param>
    /// <returns>The instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The name is not one of the four.</exception>
    /// <remarks>
    /// The default arm THROWS rather than returning the instance untouched, because a silently unmodified
    /// instance would make the theory row pass for the wrong reason if a member were ever renamed.
    /// </remarks>
    private static JwtAuthenticationOptions Disable(string switchName)
    {
        JwtAuthenticationOptions options = Valid();

        switch (switchName)
        {
            case nameof(JwtAuthenticationOptions.ValidateIssuer):
                options.ValidateIssuer = false;
                break;
            case nameof(JwtAuthenticationOptions.ValidateAudience):
                options.ValidateAudience = false;
                break;
            case nameof(JwtAuthenticationOptions.ValidateLifetime):
                options.ValidateLifetime = false;
                break;
            case nameof(JwtAuthenticationOptions.ValidateIssuerSigningKey):
                options.ValidateIssuerSigningKey = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(switchName),
                    switchName,
                    "Not one of the four invariant inbound token-validation checks.");
        }

        return options;
    }
}
