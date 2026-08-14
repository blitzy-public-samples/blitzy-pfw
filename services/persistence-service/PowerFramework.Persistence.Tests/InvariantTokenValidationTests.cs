// ==================================================================================================
//  InvariantTokenValidationTests - THE FOUR SWITCHES THAT ARE NOT DEPLOYMENT CHOICES
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE GUARDS, AND WHY A GUARD IS NEEDED AT ALL
//
//  READING the four inbound token-validation checks - issuer, audience, lifetime and
//  signature - out of configuration and handing whatever is found to the bearer handler is the obvious
//  shape, and it lets a settings file turn any of them off while the host reports healthy. Each one
//  removes a whole class of forgery: without issuer validation a credential from any issuer is accepted, so Security
//  stops being the sole authority; without audience validation a credential minted for Gateway or
//  DataServices is replayable here; without lifetime validation the short lifetimes Security mints bound
//  nothing; without signature validation any well-formed token is accepted.
//
//  The discipline has two halves, and BOTH need a test or the pair is only half true:
//
//    1. The composition root assigns all four LITERALLY, so a configured value cannot reach the
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

using System.Globalization;
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
    /// The only <see cref="TimeSpan"/> members this options type may carry, and neither is a tolerance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AN ALLOW-LIST RATHER THAN A RELAXED HEURISTIC, so the scan below keeps its teeth. Both entries
    /// govern KEY-SET RETRIEVAL - how soon a refresh a rejected token asked for may happen, and how often
    /// the cached set is refreshed anyway - and neither reaches
    /// <see cref="TokenValidationParameters.ClockSkew"/>. They are configured because leaving them unset
    /// is a rotation decision taken by omission: the token library's defaults are five minutes and TWELVE
    /// HOURS, so a signing-key rotation at Security would leave this boundary accepting the retired
    /// credential and refusing the current one.
    /// </para>
    /// <para>
    /// The sibling row below is what makes the allow-list safe rather than a hole: it boots the deployed
    /// composition root with BOTH intervals configured to unusual values and asserts the tolerance has not
    /// moved, so a future member that fed the skew through one of these names would fail there even though
    /// it passed here.
    /// </para>
    /// </remarks>
    private static readonly string[] PermittedTimeSpanMembers =
    [
        nameof(JwtOptions.MetadataRefreshInterval),
        nameof(JwtOptions.MetadataAutomaticRefreshInterval),
    ];

    /// <summary>
    /// No configuration key can move the lifetime tolerance.
    /// </summary>
    /// <remarks>
    /// THE ABSENCE IS THE GUARANTEE. A configurable tolerance is lifetime validation switched off under
    /// another name, since nothing would stop a deployment setting it past the token lifetime. The bound
    /// options type is SCANNED rather than one property name checked, so a member arriving under any
    /// spelling - <c>Skew</c>, <c>Tolerance</c>, or any bare <see cref="TimeSpan"/> - trips this row unless
    /// it is one of the two retrieval intervals named in <see cref="PermittedTimeSpanMembers"/>, and a name
    /// carrying <c>Skew</c> or <c>Tolerance</c> trips it even if it is listed.
    /// </remarks>
    [Fact]
    public void NoConfiguredValueCanMoveTheLifetimeTolerance()
    {
        foreach (System.Reflection.PropertyInfo property in typeof(JwtOptions).GetProperties())
        {
            bool toleranceShaped =
                property.Name.Contains("Skew", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Tolerance", StringComparison.OrdinalIgnoreCase);

            bool permitted =
                !toleranceShaped
                && PermittedTimeSpanMembers.Contains(property.Name, StringComparer.Ordinal);

            Assert.False(
                !permitted
                    && (property.PropertyType == typeof(TimeSpan)
                        || property.PropertyType == typeof(TimeSpan?)
                        || toleranceShaped),
                $"{nameof(JwtOptions)}.{property.Name} looks like a configurable lifetime tolerance. The "
                    + "skew is compiled in at Program.cs precisely so no deployment can widen it past "
                    + "the token lifetime.");
        }
    }

    /// <summary>
    /// The two configured retrieval intervals reach the deployed handler, and neither moves the tolerance.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 <b>SAYING NOTHING HERE WAS A ROTATION DECISION TAKEN BY OMISSION, AND THE TWO DEFAULTS FAILED IN
    /// OPPOSITE DIRECTIONS AT ONCE.</b> <c>RefreshInterval</c> defaults to five minutes, so a token minted
    /// AFTER Security rotates its signing key is refused <c>401</c> (IDX10503, no key matched the
    /// identifier) for up to that long even though the handler asks to refresh the instant it sees an
    /// unknown identifier. <c>AutomaticRefreshInterval</c> defaults to TWELVE HOURS and is the only thing
    /// that ever drops a RETIRED key, because a successful validation provokes no refresh - so the
    /// superseded credential stayed acceptable here for half a day. Rotation therefore inverted this
    /// boundary's verdicts: the old token worked and the new one did not.
    /// </para>
    /// <para>
    /// ASSERTED ON THE RESOLVED HANDLER OPTIONS OF A HOST BUILT FROM CONFIGURATION, and with values that
    /// are neither the library's defaults nor this service's own, so the row cannot pass on a host whose
    /// composition root reads nothing. The tolerance is asserted UNMOVED in the same breath, which is what
    /// makes the allow-list above safe: a member that fed the skew under one of those names would fail
    /// here.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheConfiguredRetrievalIntervalsReachTheHandlerAndLeaveTheToleranceAloneAsync()
    {
        TimeSpan requested = TimeSpan.FromSeconds(7);
        TimeSpan background = TimeSpan.FromMinutes(11);

        await using CompositionHost host = CompositionHost.Create(new Dictionary<string, string?>(
            StringComparer.Ordinal)
        {
            ["Jwt:MetadataRefreshInterval"] = requested.ToString(),
            ["Jwt:MetadataAutomaticRefreshInterval"] = background.ToString(),
        });

        using HttpClient client = host.CreateClient();

        JwtBearerOptions bearer = host.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(requested, bearer.RefreshInterval);
        Assert.Equal(background, bearer.AutomaticRefreshInterval);

        // Neither is the value the library would have used had the composition root said nothing.
        Assert.NotEqual(BaseConfigurationManager.DefaultRefreshInterval, bearer.RefreshInterval);
        Assert.NotEqual(
            BaseConfigurationManager.DefaultAutomaticRefreshInterval,
            bearer.AutomaticRefreshInterval);

        // And the tolerance the rest of this file defends has not moved.
        Assert.Equal(LifetimeTolerance, bearer.TokenValidationParameters.ClockSkew);
    }

    /// <summary>
    /// The SHIPPED defaults are the documented ones, and both are tighter than the library's.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The row above proves configuration is READ; this one proves what an operator gets when they
    /// configure nothing, which is the case every deployment starts from. Both bounds are asserted against
    /// the LIBRARY's published values rather than against literals, so a runtime that changed either would
    /// report the change here instead of leaving a stale claim standing.
    /// </remarks>
    [Fact]
    public async Task TheShippedRetrievalIntervalsAreTighterThanTheLibraryDefaultsAsync()
    {
        await using CompositionHost host = CompositionHost.Create();

        using HttpClient client = host.CreateClient();

        JwtBearerOptions bearer = host.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(JwtOptions.DefaultMetadataRefreshInterval, bearer.RefreshInterval);
        Assert.Equal(JwtOptions.DefaultMetadataAutomaticRefreshInterval, bearer.AutomaticRefreshInterval);

        Assert.True(
            bearer.RefreshInterval < BaseConfigurationManager.DefaultRefreshInterval,
            "The shipped requested-refresh floor is not tighter than the library default it displaces.");

        Assert.True(
            bearer.AutomaticRefreshInterval < BaseConfigurationManager.DefaultAutomaticRefreshInterval,
            "The shipped background interval is not tighter than the library default it displaces.");

        // Both are at or above the floors the library would throw on, which is what makes a host built
        // from the shipped settings startable at all.
        Assert.True(bearer.RefreshInterval >= BaseConfigurationManager.MinimumRefreshInterval);
        Assert.True(
            bearer.AutomaticRefreshInterval >= BaseConfigurationManager.MinimumAutomaticRefreshInterval);
    }

    /// <summary>
    /// A retrieval interval below the library's own floor is a refusal to start, naming the key.
    /// </summary>
    /// <param name="requested">The requested-refresh floor to configure.</param>
    /// <param name="background">The background interval to configure.</param>
    /// <param name="expectedMember">The member the failure must name.</param>
    /// <remarks>
    /// WITHOUT THIS THE FAULT SURFACES ON THE FIRST AUTHENTICATED REQUEST. The configuration manager
    /// throws <see cref="ArgumentOutOfRangeException"/> - IDX10107 and IDX10108 - while the handler builds
    /// it, which happens on the first request rather than at startup, so a host would report healthy and
    /// then fail every authenticated call with a message naming neither the setting nor the file. The third
    /// row is a relationship rather than a range: a floor LONGER than the background interval makes a
    /// rotation converge more slowly for a caller presenting a new token than for one presenting nothing.
    /// </remarks>
    [Theory]
    [InlineData("00:00:00.500", "00:05:00", nameof(JwtOptions.MetadataRefreshInterval))]
    [InlineData("00:00:05", "00:01:00", nameof(JwtOptions.MetadataAutomaticRefreshInterval))]
    [InlineData("00:10:00", "00:05:00", nameof(JwtOptions.MetadataRefreshInterval))]
    public void ARetrievalIntervalBelowTheLibraryFloorIsARefusalToStart(
        string requested,
        string background,
        string expectedMember)
    {
        PersistenceOptions options = Bootable();
        options.Jwt.MetadataRefreshInterval = TimeSpan.Parse(requested, CultureInfo.InvariantCulture);
        options.Jwt.MetadataAutomaticRefreshInterval =
            TimeSpan.Parse(background, CultureInfo.InvariantCulture);

        ValidateOptionsResult result = new PersistenceOptionsValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.NotNull(result.Failures);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                string.Concat("Jwt:", expectedMember),
                StringComparison.Ordinal));
    }

    /// <summary>
    /// THE POSITIVE ARM: the shipped pair raises no validation failure.
    /// </summary>
    /// <remarks>
    /// Without it the three rows above would pass against a validator that refused every pair, and the
    /// shipped settings file would be unstartable while the suite stayed green.
    /// </remarks>
    [Fact]
    public void TheShippedRetrievalIntervalPairRaisesNoFailure()
    {
        ValidateOptionsResult result = new PersistenceOptionsValidator().Validate(name: null, Bootable());

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// The last-known-good fallback is bounded to the background interval, not left at an hour.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// MEASURED AT A SIBLING BOUNDARY, NOT REASONED: with both intervals configured, a token signed by a
    /// RETIRED key was still accepted nine and a half minutes after the rotation, because
    /// <c>BaseConfigurationManager</c> keeps a cache of recently-good configurations that the handler
    /// retries against and its entries live for <c>LastKnownGoodLifetime</c> - one hour by default. It is
    /// BOUNDED rather than switched off: the fallback keeps this boundary validating through a transient
    /// inability to FETCH the key set, so both halves are asserted.
    /// </remarks>
    [Fact]
    public async Task TheLastKnownGoodFallbackIsBoundedToTheBackgroundIntervalAsync()
    {
        TimeSpan background = TimeSpan.FromMinutes(23);

        await using CompositionHost host = CompositionHost.Create(new Dictionary<string, string?>(
            StringComparer.Ordinal)
        {
            ["Jwt:MetadataAutomaticRefreshInterval"] = background.ToString(),
        });

        using HttpClient client = host.CreateClient();

        JwtBearerOptions bearer = host.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        BaseConfigurationManager manager =
            Assert.IsAssignableFrom<BaseConfigurationManager>(bearer.ConfigurationManager);

        Assert.Equal(background, manager.LastKnownGoodLifetime);
        Assert.NotEqual(
            BaseConfigurationManager.DefaultLastKnownGoodConfigurationLifetime,
            manager.LastKnownGoodLifetime);

        Assert.True(manager.UseLastKnownGoodConfiguration);
    }

    /// <summary>
    /// The SHIPPED host bounds the fallback to its own five-minute background interval.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task TheShippedHostBoundsTheFallbackToItsBackgroundIntervalAsync()
    {
        await using CompositionHost host = CompositionHost.Create();

        using HttpClient client = host.CreateClient();

        JwtBearerOptions bearer = host.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        BaseConfigurationManager manager =
            Assert.IsAssignableFrom<BaseConfigurationManager>(bearer.ConfigurationManager);

        Assert.Equal(
            JwtOptions.DefaultMetadataAutomaticRefreshInterval,
            manager.LastKnownGoodLifetime);
    }

    /// <summary>Builds an otherwise-startable options graph.</summary>
    /// <returns>A graph whose only fault is whatever the caller introduces.</returns>
    /// <remarks>
    /// The two required strings and one roster entry are supplied because an empty roster and a blank
    /// authority are refusals of their own, and a row about an interval must not pass on somebody else's
    /// failure. Every other member keeps its shipped default, which is what makes the positive row above an
    /// assertion about the shipped configuration.
    /// </remarks>
    private static PersistenceOptions Bootable()
    {
        PersistenceOptions options = new();
        options.Jwt.Authority = "https://security.invalid";
        options.Jwt.Audience = "powerframework-persistence";
        options.Jwt.PermittedCallers.Add("powerframework-dataservices");

        return options;
    }
}
