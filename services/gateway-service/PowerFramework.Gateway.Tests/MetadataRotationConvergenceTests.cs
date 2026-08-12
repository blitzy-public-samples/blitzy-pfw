// ======================================================================================================
//  SIGNING-KEY ROTATION INVERTED THIS BOUNDARY'S VERDICTS, AND THE CAUSE WAS TWO SETTINGS NOBODY SET.
//
//  When Security rotates its signing key and key identifier, its published key set changes at once - but a
//  verification boundary keeps the set it cached, so for a while:
//
//    * a token minted BEFORE the rotation still validates, because the retired key is still cached; and
//    * a token minted AFTER it is refused 401 with IDX10503, naming a key identifier that matched nothing.
//
//  The composition root left both of the library's retrieval intervals unassigned, and saying nothing meant
//  taking the library's defaults - which fail in OPPOSITE directions at once. RefreshInterval defaults to
//  five minutes, and it is the floor on the refresh the handler ASKS FOR the instant it sees an unknown key
//  identifier (RefreshOnIssuerKeyNotFound is true by default), so the "new token refused" half lasted
//  minutes. AutomaticRefreshInterval defaults to TWELVE HOURS, and because a SUCCESSFUL validation provokes
//  no refresh at all it is the only thing that ever drops a retired key - so the "old token still accepted"
//  half lasted half a day.
//
//  A BOUND RATHER THAN AN OVERLAP, AND THAT IS AN AAP CONSTRAINT RATHER THAN A PREFERENCE. The other way to
//  remove the window is for the issuer to publish the superseded key beside the new one until consumers
//  converge, which a JSON web key SET can obviously carry. AAP 0.6.6.3 fixes exactly ONE signing secret in
//  the estate and Security's published set is built from that single key, so an overlap would mean a second
//  slot of issuer key material - new capability, which constraint C-B forbids - rather than a setting. The
//  window is therefore made short, validated, and documented with a rotation order.
//
//  Every row here reads the RESOLVED handler options out of the DEPLOYED composition root, because the
//  defect was an omission in that composition root and a restatement of it could not have caught one.
// ======================================================================================================

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PowerFramework.Gateway.Configuration;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// Covers the two key-set retrieval intervals this ingress verifies tokens with: that the composition root
/// assigns them, that configuration reaches them, that the shipped values are tighter than the library's,
/// and that a value the library would throw on is refused at startup instead.
/// </summary>
public sealed class MetadataRotationConvergenceTests
{
    /// <summary>The configuration key the requested-refresh floor binds from.</summary>
    private const string RequestedRefreshKey =
        JwtBearerVerificationOptions.SectionName + ":MetadataRefreshInterval";

    /// <summary>The configuration key the background interval binds from.</summary>
    private const string BackgroundRefreshKey =
        JwtBearerVerificationOptions.SectionName + ":MetadataAutomaticRefreshInterval";

    /// <summary>
    /// The shipped host carries both intervals, and both are tighter than the library's defaults.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE ROW THE FINDING ASKED FOR, AND IT IS ASSERTED AGAINST THE LIBRARY'S OWN PUBLISHED DEFAULTS
    /// rather than against literals - so a runtime that changed either default would report the change here
    /// instead of leaving a stale claim standing. An equality-only row would pass on a host that assigned
    /// the defaults back again, which is why both the equality and the strict inequality are asserted.
    /// </remarks>
    [Fact]
    public async Task TheShippedHostRetrievesKeysMoreOftenThanTheLibraryWould()
    {
        await using GatewayTestHostFixture host = new();

        JwtBearerOptions bearer = Resolve(host);

        Assert.Equal(
            JwtBearerVerificationOptions.DefaultMetadataRefreshInterval,
            bearer.RefreshInterval);

        Assert.Equal(
            JwtBearerVerificationOptions.DefaultMetadataAutomaticRefreshInterval,
            bearer.AutomaticRefreshInterval);

        Assert.True(
            bearer.RefreshInterval < BaseConfigurationManager.DefaultRefreshInterval,
            "The requested-refresh floor is not tighter than the library default it displaces, so a "
                + "rotation still leaves a correctly signed token refused for the library's five minutes.");

        Assert.True(
            bearer.AutomaticRefreshInterval < BaseConfigurationManager.DefaultAutomaticRefreshInterval,
            "The background interval is not tighter than the library default it displaces, so a RETIRED "
                + "signing key stays acceptable here for the library's twelve hours.");

        // THE REFRESH-ON-UNKNOWN-KEY BEHAVIOUR IS WHAT MAKES THE FIRST INTERVAL MEAN ANYTHING. With it off,
        // no failure would ever ask for a refresh and only the background interval would converge.
        Assert.True(bearer.RefreshOnIssuerKeyNotFound);
    }

    /// <summary>
    /// Configuration reaches both intervals, and the values are the deployment's rather than the shipped
    /// ones.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE VALUES ARE NEITHER THE LIBRARY'S DEFAULTS NOR THIS SERVICE'S OWN, so this row cannot pass on a
    /// composition root that reads nothing and cannot pass on one that hardcodes the shipped constants -
    /// the two ways an operator's setting comes to be silently ignored.
    /// </remarks>
    [Fact]
    public async Task ADeploymentCanTuneBothIntervals()
    {
        TimeSpan requested = TimeSpan.FromSeconds(9);
        TimeSpan background = TimeSpan.FromMinutes(13);

        await using GatewayTestHostFixture host = new();
        host.AdditionalSettings[RequestedRefreshKey] = requested.ToString();
        host.AdditionalSettings[BackgroundRefreshKey] = background.ToString();

        JwtBearerOptions bearer = Resolve(host);

        Assert.Equal(requested, bearer.RefreshInterval);
        Assert.Equal(background, bearer.AutomaticRefreshInterval);

        Assert.NotEqual(
            JwtBearerVerificationOptions.DefaultMetadataRefreshInterval,
            bearer.RefreshInterval);

        Assert.NotEqual(
            JwtBearerVerificationOptions.DefaultMetadataAutomaticRefreshInterval,
            bearer.AutomaticRefreshInterval);
    }

    /// <summary>
    /// Neither interval reaches the lifetime tolerance, whatever it is configured to.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// TWO NEW CONFIGURABLE DURATIONS ARRIVED ON A TYPE WHOSE OTHER DURATIONS ARE DELIBERATELY NOT
    /// CONFIGURABLE. The clock skew is a compiled constant precisely so that no deployment can widen a
    /// token's usable life past the lifetime it claims, and this row is what stops a later change routing a
    /// configured duration into it: both intervals are set to values far larger than the tolerance, and the
    /// tolerance is asserted unmoved.
    /// </remarks>
    [Fact]
    public async Task NeitherIntervalCanMoveTheLifetimeTolerance()
    {
        await using GatewayTestHostFixture host = new();
        host.AdditionalSettings[RequestedRefreshKey] = TimeSpan.FromMinutes(4).ToString();
        host.AdditionalSettings[BackgroundRefreshKey] = TimeSpan.FromHours(3).ToString();

        JwtBearerOptions bearer = Resolve(host);

        Assert.Equal(TimeSpan.FromSeconds(30), bearer.TokenValidationParameters.ClockSkew);
        Assert.True(bearer.TokenValidationParameters.ValidateLifetime);
    }

    /// <summary>
    /// A value below the floor the token library enforces is refused by the options validator, by name.
    /// </summary>
    /// <param name="requested">The requested-refresh floor to configure.</param>
    /// <param name="background">The background interval to configure.</param>
    /// <param name="expectedMember">The member the failure must name.</param>
    /// <remarks>
    /// WITHOUT THIS THE FAULT SURFACES ON THE FIRST AUTHENTICATED REQUEST AND NAMES NEITHER SETTING.
    /// <c>BaseConfigurationManager</c> throws <see cref="ArgumentOutOfRangeException"/> - IDX10107 and
    /// IDX10108 - while the handler builds its configuration manager, which happens on the first
    /// authenticated request rather than at startup, so a host would report healthy and then fail every
    /// authenticated call. The third row is a relationship rather than a range: a floor LONGER than the
    /// background interval makes a rotation converge more slowly for a caller presenting a NEW token than
    /// for one presenting nothing at all.
    /// </remarks>
    [Theory]
    [InlineData("00:00:00.500", "00:05:00", "MetadataRefreshInterval")]
    [InlineData("00:00:05", "00:01:00", "MetadataAutomaticRefreshInterval")]
    [InlineData("00:10:00", "00:05:00", "MetadataRefreshInterval")]
    public void AnIntervalBelowTheLibraryFloorIsRefusedByName(
        string requested,
        string background,
        string expectedMember)
    {
        JwtBearerVerificationOptions options = Bootable();
        options.MetadataRefreshInterval = TimeSpan.Parse(requested, CultureInfo.InvariantCulture);
        options.MetadataAutomaticRefreshInterval =
            TimeSpan.Parse(background, CultureInfo.InvariantCulture);

        List<ValidationResult> failures = [.. options.Validate(new ValidationContext(options))];

        Assert.Contains(
            failures,
            failure => failure.ErrorMessage is not null
                && failure.ErrorMessage.Contains(
                    string.Concat(JwtBearerVerificationOptions.SectionName, ":", expectedMember),
                    StringComparison.Ordinal));
    }

    /// <summary>
    /// THE POSITIVE ARM: the shipped pair raises no failure.
    /// </summary>
    /// <remarks>
    /// Without it the three rows above would pass against a validator that refused every pair, and the
    /// shipped settings file would be unstartable while this suite stayed green.
    /// </remarks>
    [Fact]
    public void TheShippedIntervalPairRaisesNoFailure()
    {
        JwtBearerVerificationOptions options = Bootable();

        Assert.Empty(options.Validate(new ValidationContext(options)));
    }

    /// <summary>
    /// The last-known-good fallback is bounded to the background interval, not left at an hour.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THE TWO INTERVALS ABOVE DO NOT BOUND THIS, AND THAT WAS MEASURED RATHER THAN REASONED.</b>
    /// With both of them configured, a token signed by a RETIRED key was still accepted at a running
    /// boundary NINE AND A HALF MINUTES after the rotation - well past the five-minute background refresh
    /// that had already replaced the current configuration. <c>BaseConfigurationManager</c> keeps a CACHE of
    /// recently-good configurations that the token handler retries against when validation fails against
    /// the current one, and those entries live for <c>LastKnownGoodLifetime</c>, which defaults to ONE HOUR.
    /// Two separately retired identities were both still honoured, so it caches several rather than one.
    /// </para>
    /// <para>
    /// <b>BOUNDED RATHER THAN TURNED OFF, AND THE ROW ASSERTS BOTH HALVES OF THAT CHOICE.</b>
    /// <c>UseLastKnownGoodConfiguration</c> must stay TRUE - it is what keeps this boundary validating
    /// through a transient inability to FETCH the key set - while the lifetime must equal the background
    /// interval, so a superseded set is gone after one refresh cycle. Asserted against the CONFIGURED
    /// interval rather than a literal, so the two cannot drift into an incoherent pair, and against a value
    /// that is not the library's default, so the row cannot pass on a host that assigns nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheLastKnownGoodFallbackIsBoundedToTheBackgroundInterval()
    {
        TimeSpan background = TimeSpan.FromMinutes(19);

        await using GatewayTestHostFixture host = new();
        host.AdditionalSettings[BackgroundRefreshKey] = background.ToString();

        JwtBearerOptions bearer = Resolve(host);

        BaseConfigurationManager manager =
            Assert.IsAssignableFrom<BaseConfigurationManager>(bearer.ConfigurationManager);

        Assert.Equal(background, manager.LastKnownGoodLifetime);
        Assert.NotEqual(
            BaseConfigurationManager.DefaultLastKnownGoodConfigurationLifetime,
            manager.LastKnownGoodLifetime);

        Assert.True(
            manager.UseLastKnownGoodConfiguration,
            "The last-known-good fallback has been switched off rather than bounded, which trades a "
                + "transient metadata outage for a hard authentication failure.");
    }

    /// <summary>
    /// The SHIPPED host bounds the fallback to its own five-minute background interval.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The row above proves the derivation follows configuration; this one proves what a deployment that
    /// configures nothing actually gets, which is the case every deployment starts from.
    /// </remarks>
    [Fact]
    public async Task TheShippedHostBoundsTheFallbackToFiveMinutes()
    {
        await using GatewayTestHostFixture host = new();

        JwtBearerOptions bearer = Resolve(host);

        BaseConfigurationManager manager =
            Assert.IsAssignableFrom<BaseConfigurationManager>(bearer.ConfigurationManager);

        Assert.Equal(
            JwtBearerVerificationOptions.DefaultMetadataAutomaticRefreshInterval,
            manager.LastKnownGoodLifetime);
    }

    /// <summary>Starts the host and returns the bearer options it actually runs on.</summary>
    /// <param name="host">The host to start.</param>
    /// <returns>The resolved handler options.</returns>
    /// <remarks>
    /// Creating a client forces the host to build, so what is returned has been through binding,
    /// post-configuration and startup validation exactly as a deployed process would.
    /// </remarks>
    private static JwtBearerOptions Resolve(GatewayTestHostFixture host)
    {
        using HttpClient client = host.CreateClient();

        return host.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    /// <summary>Builds an otherwise-valid verification section.</summary>
    /// <returns>A section whose only fault is whatever the caller introduces.</returns>
    /// <remarks>
    /// The authority and one audience are supplied because a blank authority and an empty audience list are
    /// refusals of their own, and a row about an interval must not pass on somebody else's failure. Every
    /// other member keeps its shipped default, which is what makes the positive row an assertion about the
    /// shipped configuration rather than about a fabricated one.
    /// </remarks>
    private static JwtBearerVerificationOptions Bootable()
    {
        JwtBearerVerificationOptions options = new()
        {
            Authority = "https://security.invalid",
        };

        options.ValidAudiences.Add("powerframework-gateway");

        return options;
    }
}
