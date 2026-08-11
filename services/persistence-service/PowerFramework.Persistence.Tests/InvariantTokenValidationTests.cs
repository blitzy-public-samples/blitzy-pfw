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

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

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

    // ==============================================================================================
    //  THE FIFTH INVARIANT: THE TOLERANCE BESIDE THE LIFETIME CHECK
    //
    //  ValidateLifetime is only as tight as ClockSkew, so a file about switches that are not deployment
    //  choices is where the tolerance belongs. It is not a boolean, so the two halves above take a
    //  different shape here: half 1 is asserted against the HANDLER'S OWN OPTIONS resolved from a
    //  running host, and half 2 is asserted as ABSENCE from the bound options - a numeric setting has no
    //  "refuse the unsafe value" arm that a boolean's `false` provides, so the safe form is no key at all.
    // ==============================================================================================

    /// <summary>The lifetime tolerance the composition root assigns on this boundary.</summary>
    /// <remarks>
    /// RESTATED HERE BECAUSE IT IS A COMPILE-TIME CONSTANT, not a setting, so there is no configuration
    /// source to read it back from. Pinning it is the point: the three verifying services must agree with
    /// one another, and an unpinned constant can be widened in one of them with no suite noticing. See
    /// docs/ARCHITECTURE.md 9.5 for all four boundaries in one table.
    /// </remarks>
    private static readonly TimeSpan LifetimeTolerance = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The handler's lifetime tolerance is explicit, bounded, and not the library's five-minute default.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// SAYING NOTHING WAS NOT THE SAME AS ALLOWING NOTHING. The composition root once left
    /// <c>ClockSkew</c> unassigned, which does not mean no tolerance - it means the bearer handler's
    /// default of FIVE MINUTES. Security issues with a five-minute lifetime, so the tolerance was as long
    /// as the lifetime it qualifies and every credential reaching this service stayed usable for twice as
    /// long as it claims to be. The <c>ValidateLifetime</c> invariant the rest of this file defends was
    /// therefore enforcing a bound five times looser than it reads.
    /// </para>
    /// <para>
    /// AND THE FOUR BOUNDARIES DISAGREED, WHICH IS THE SHARPER PROBLEM. Security refused an expired token
    /// the instant it lapsed, DataServices allowed thirty seconds, and Gateway and this service inherited
    /// five minutes by omission - four validators of ONE issuer's tokens, so whether an expired credential
    /// was accepted depended only on which service it reached. This is the innermost service and the only
    /// holder of a storage provider, and it was among the two most permissive.
    /// </para>
    /// <para>
    /// THE PREMISE IS MEASURED RATHER THAN RECITED: the library default is read off a fresh parameters
    /// instance, so a future runtime that changed it would make this row report the change instead of
    /// resting on a stale claim. Both bounds are asserted, because an equality-only row would pass after
    /// someone widened the constant - whereas the bounds state the rule, which is that zero is wrong here
    /// (unlike Security, this boundary validates tokens minted on another container's clock) and that a
    /// tolerance approaching the token lifetime makes expiry checking ceremonial.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheLifetimeToleranceIsBoundedAndIsNotTheLibraryDefaultAsync()
    {
        await using CompositionHost host = CompositionHost.Create();

        // Resolving the handler's options forces the host to build, which is what makes this an assertion
        // about the DEPLOYED composition root rather than about a restatement of it.
        using HttpClient client = host.CreateClient();

        JwtBearerOptions bearer = host.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        TimeSpan configured = bearer.TokenValidationParameters.ClockSkew;

        Assert.Equal(LifetimeTolerance, configured);

        TimeSpan libraryDefault = new TokenValidationParameters().ClockSkew;

        Assert.Equal(TimeSpan.FromMinutes(5), libraryDefault);
        Assert.NotEqual(libraryDefault, configured);
        Assert.True(
            configured < libraryDefault,
            "The tolerance is not tighter than the library default it was assigned to displace.");
        Assert.True(configured > TimeSpan.Zero, "No clock drift at all is allowed on this boundary.");
        Assert.True(
            configured <= TimeSpan.FromMinutes(1),
            "The lifetime tolerance has been widened beyond one minute.");

        // The lifetime check itself is still on, asserted on the same resolved options so this row cannot
        // pass on a host that stopped validating lifetimes altogether.
        Assert.True(bearer.TokenValidationParameters.ValidateLifetime);
    }

    /// <summary>
    /// No configuration key can move the lifetime tolerance.
    /// </summary>
    /// <remarks>
    /// THE ABSENCE IS THE GUARANTEE. A configurable tolerance is lifetime validation switched off under
    /// another name, since nothing would stop a deployment setting it past the token lifetime. The bound
    /// options type is SCANNED rather than one property name checked, so a member arriving under any
    /// spelling - <c>Skew</c>, <c>Tolerance</c>, or any bare <see cref="TimeSpan"/> - trips this row.
    /// </remarks>
    [Fact]
    public void NoConfiguredValueCanMoveTheLifetimeTolerance()
    {
        foreach (System.Reflection.PropertyInfo property in typeof(JwtOptions).GetProperties())
        {
            Assert.False(
                property.PropertyType == typeof(TimeSpan)
                    || property.PropertyType == typeof(TimeSpan?)
                    || property.Name.Contains("Skew", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Contains("Tolerance", StringComparison.OrdinalIgnoreCase),
                $"{nameof(JwtOptions)}.{property.Name} looks like a configurable lifetime tolerance. The "
                    + "skew is compiled in at Program.cs precisely so no deployment can widen it past "
                    + "the token lifetime.");
        }
    }
}
