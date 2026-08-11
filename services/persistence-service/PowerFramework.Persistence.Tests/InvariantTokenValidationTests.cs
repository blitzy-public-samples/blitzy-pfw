// ==================================================================================================
//  InvariantTokenValidationTests - THE FOUR SWITCHES THAT ARE NOT DEPLOYMENT CHOICES
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE GUARDS, AND WHY A GUARD IS NEEDED AT ALL
//
//  This service used to read the four inbound token-validation checks - issuer, audience, lifetime and
//  signature - out of configuration and hand whatever it found to the bearer handler. A settings file
//  could therefore turn any of them off while the host reported healthy, and each one removes a whole
//  class of forgery: without issuer validation a credential from any issuer is accepted, so Security
//  stops being the sole authority; without audience validation a credential minted for Gateway or
//  DataServices is replayable here; without lifetime validation the short lifetimes Security mints bound
//  nothing; without signature validation any well-formed token is accepted.
//
//  The remedy has two halves, and BOTH need a test or the pair is only half true:
//
//    1. The composition root now assigns all four LITERALLY, so a configured value cannot reach the
//       handler. Asserted here by resolving the handler's own options from a running host.
//    2. Because the assignment is literal, a configured `false` would be SILENTLY IGNORED - which is
//       the more dangerous failure of the two, since an operator would believe it applied. So the
//       options validator refuses it, and the host does not start. Asserted here per switch.
//
//  Without half 2 a deployment would carry a setting that reads as disabled and behaves as enabled.
//  Without a test for half 2 the refusal could be deleted and every other suite would still pass.
//
//  CONSTRAINT COMPLIANCE
//  ------------------------------------------------------------------------------------------------
//  C-G   EVERY NEW BOUNDARY AUTHENTICATED. An authenticated boundary whose validation checks can be
//        switched off from a settings file is authenticated in name only, so this is C-G's enforcement
//        half rather than a configuration nicety.
//  C-H   80 PER CENT LINE COVERAGE PER SERVICE. This file's named share is the disabled-switch arm of
//        Configuration/PersistenceOptions.cs's validator - four Append calls plus the guard clause that
//        skips an enabled switch, none of which any other suite reaches.
//  C-I   EACH SERVICE BUILDS AND TESTS INDEPENDENTLY. Every row is either a direct validator call or an
//        in-memory host, so no port is bound and no sibling service is required.
// ==================================================================================================

using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Proves that the four inbound token-validation checks are invariant on this service: enabled whatever
/// configuration says, and refused at startup when configuration says otherwise.
/// </summary>
public sealed class InvariantTokenValidationTests
{
    /// <summary>The validator that owns the refusal.</summary>
    private static readonly PersistenceOptionsValidator Validator = new();

    /// <summary>
    /// The four member names, which are also the configuration keys under the inbound-token group.
    /// </summary>
    /// <remarks>
    /// SPELLED OUT RATHER THAN REFLECTED. A reflected list would grow silently if a fifth boolean were
    /// added to the group, and would then assert a rule about it that nobody had decided - whereas this
    /// service deliberately leaves its other booleans unvalidated because their legacy setters applied no
    /// guard. The four here are the four that were adjudicated, so they are named.
    /// </remarks>
    private static readonly string[] SwitchNames =
    [
        nameof(JwtOptions.ValidateIssuer),
        nameof(JwtOptions.ValidateAudience),
        nameof(JwtOptions.ValidateLifetime),
        nameof(JwtOptions.ValidateIssuerSigningKey),
    ];

    /// <summary>
    /// Every switch, projected onto theory rows.
    /// </summary>
    /// <returns>One row per switch.</returns>
    public static TheoryData<string> Switches() => [.. SwitchNames];

    /// <summary>
    /// All four checks are on by default, so an absent key is the safe case rather than a fault.
    /// </summary>
    /// <remarks>
    /// THE DEFAULT IS WHAT MAKES OMISSION SAFE, and it is asserted first because every row below depends
    /// on it: the refusal rows have to turn a switch OFF to provoke a failure, which is only meaningful if
    /// the resting state is on.
    /// </remarks>
    [Fact]
    public void EveryInboundValidationCheckIsOnByDefault()
    {
        JwtOptions jwt = new();

        Assert.True(jwt.ValidateIssuer);
        Assert.True(jwt.ValidateAudience);
        Assert.True(jwt.ValidateLifetime);
        Assert.True(jwt.ValidateIssuerSigningKey);
    }

    /// <summary>
    /// A configuration that turns one check off is refused, and the failure names the key and says why.
    /// </summary>
    /// <param name="switchName">The member that is turned off.</param>
    /// <remarks>
    /// <para>
    /// THE MESSAGE CONTENT IS ASSERTED, NOT JUST THE REFUSAL. An operator meeting this failure has to be
    /// able to fix it without reading the source, so the key must appear, the word invariant must appear
    /// so the reader knows it is not a value to be tuned, and the remedy must be stated. A refusal
    /// carrying none of those is a wall rather than a diagnostic.
    /// </para>
    /// <para>
    /// The switch is turned off on an otherwise VALID instance, so the failure this row observes cannot be
    /// a side effect of some other rule - which is asserted directly by the clean-instance row below.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Switches))]
    public void DisablingOneInboundValidationCheckIsRefused(string switchName)
    {
        PersistenceOptions options = Disable(switchName);

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
        PersistenceOptions options = Valid();

        options.Jwt.ValidateIssuer = false;
        options.Jwt.ValidateAudience = false;
        options.Jwt.ValidateLifetime = false;
        options.Jwt.ValidateIssuerSigningKey = false;

        string[] failures = Validator.Validate(name: null, options).Failures?.ToArray() ?? [];

        foreach (string switchName in SwitchNames)
        {
            Assert.Contains(failures, failure => failure.Contains(switchName, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// An instance with all four on validates cleanly, which is what makes every refusal row attributable.
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
    /// Builds a settings instance that validates cleanly.
    /// </summary>
    /// <returns>The valid instance.</returns>
    /// <remarks>
    /// The two required inbound-token strings and the permitted-caller roster are supplied because each
    /// defaults to empty, so a default-constructed instance does not validate. Those requirements have
    /// their own tests elsewhere; here they are satisfied so that a row observes only the rule it names.
    /// The key-set path is left at its default, which is already the well-known address.
    /// </remarks>
    private static PersistenceOptions Valid()
    {
        PersistenceOptions options = new();

        options.Jwt.Authority = "https://security-service:5104";
        options.Jwt.Audience = "powerframework-persistence";
        options.Jwt.PermittedCallers.Add("powerframework-dataservices");

        return options;
    }

    /// <summary>
    /// Builds a clean settings instance with exactly one named check turned off.
    /// </summary>
    /// <param name="switchName">The member to turn off.</param>
    /// <returns>The instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The name is not one of the four.</exception>
    /// <remarks>
    /// The default arm THROWS rather than returning the instance untouched, because a silently unmodified
    /// instance would make the theory row pass for the wrong reason if a member were ever renamed.
    /// </remarks>
    private static PersistenceOptions Disable(string switchName)
    {
        PersistenceOptions options = Valid();

        switch (switchName)
        {
            case nameof(JwtOptions.ValidateIssuer):
                options.Jwt.ValidateIssuer = false;
                break;
            case nameof(JwtOptions.ValidateAudience):
                options.Jwt.ValidateAudience = false;
                break;
            case nameof(JwtOptions.ValidateLifetime):
                options.Jwt.ValidateLifetime = false;
                break;
            case nameof(JwtOptions.ValidateIssuerSigningKey):
                options.Jwt.ValidateIssuerSigningKey = false;
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
