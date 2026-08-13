// ==================================================================================================
//  InvariantTokenValidationTests - THE FIVE SETTINGS THAT ARE NOT DEPLOYMENT CHOICES
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE GUARDS, AND WHY THIS SERVICE HAS THE MOST TO LOSE
//
//  This service used to read the four inbound token-validation checks - issuer, audience, lifetime and
//  signature - out of configuration and hand whatever it found to the bearer handler. A settings file
//  could therefore turn any of them off while the host reported healthy. Each removes a whole class of
//  forgery, and on the SOLE ISSUER the consequences are worse than anywhere else in the system:
//
//    * without issuer validation a credential from any issuer is accepted - and this service IS the
//      issuer, so it would honour forgeries of its own authority;
//    * without audience validation a credential minted for Gateway, DataServices or Persistence is
//      replayable at the cryptographic surface, which is exactly what the one-audience-per-token rule of
//      contract C-01 exists to prevent;
//    * without lifetime validation the short lifetimes this service itself mints bound nothing;
//    * without signature validation the signature is not checked at all and any well-formed token is
//      accepted.
//
//  AND A FIFTH SETTING WHOSE POLARITY IS THE OPPOSITE ONE. MapInboundClaims is likewise assigned
//  literally - to FALSE - and on this service it is load bearing rather than tidy: the legacy handler
//  rewrites `scope` and `sub` into WS-Federation URIs, so with mapping ON the scope policy would look for
//  claims that are no longer there and every scope check would silently pass nothing. So the required
//  value is false and a configured TRUE is what must be refused. It was READ from configuration in
//  section 5 and then overwritten by the literal, which is the same read-then-ignore shape the four
//  switches used to have: the read decided nothing while appearing to. The read is gone and this file's
//  last three rows are what replaces it.
//
//  THE REFUSAL IS A STARTUP GATE HERE, NOT AN OPTIONS RULE, AND THAT SHAPES EVERY ROW BELOW. The two
//  sibling services model these switches on their own typed options and refuse a disabled one through
//  their options validator, so their equivalents of this file call a validator directly. This service
//  reads them straight from the inbound-authentication configuration section, so the refusal lives in
//  the composition root and is reached only by BUILDING A HOST. Every row therefore boots one and
//  asserts on what it throws - which also makes these rows the only place in this suite where a host is
//  expected NOT to start.
//
//  Without the refusal a deployment would carry a setting that reads as disabled and behaves as enabled,
//  because section 5 of the composition root assigns all four literally. That is the more dangerous of
//  the two failures: an operator would believe the switch applied. And without this file the refusal
//  could be deleted and every other suite would still pass.
//
//  CONSTRAINT COMPLIANCE
//  ------------------------------------------------------------------------------------------------
//  C-G   EVERY NEW BOUNDARY AUTHENTICATED. An authenticated boundary whose validation checks can be
//        switched off from a settings file is authenticated in name only.
//  C-H   80 PER CENT LINE COVERAGE PER SERVICE. This file's named share is the startup gate in
//        Program.cs - the loop over the four keys, the absent-key default arm and the throw - none of
//        which any other suite reaches, because every other suite builds a host that starts.
//  C-I   EACH SERVICE BUILDS AND TESTS INDEPENDENTLY. Every host below is in-memory, so no port is
//        bound and no sibling service is required.
//  .editorconfig  NO PRESERVED-SPELLING IDENTIFIER IS DECLARED HERE.
// ==================================================================================================

namespace PowerFramework.Security.Tests;

/// <summary>
/// Proves that this service refuses to start when a deployment turns off any of the four inbound
/// token-validation checks, and starts when all four are on or absent.
/// </summary>
public sealed class InvariantTokenValidationTests
{
    /// <summary>The configuration section the four switches are read from.</summary>
    private const string InboundAuthenticationSection = "Authentication:Jwt";

    /// <summary>The key whose required value is FALSE rather than true.</summary>
    private const string MapInboundClaimsKey = "MapInboundClaims";

    /// <summary>The four keys, in the order the gate reports them.</summary>
    /// <remarks>
    /// WRITTEN AS CONFIGURATION KEYS RATHER THAN AS MEMBER NAMES, because on this service they are not
    /// members of anything: they are read directly out of the configuration section, which is exactly why
    /// the refusal is a startup gate here and an options rule on the two sibling services.
    /// </remarks>
    private static readonly string[] SwitchKeys =
    [
        "ValidateIssuer",
        "ValidateAudience",
        "ValidateLifetime",
        "ValidateIssuerSigningKey",
    ];

    /// <summary>
    /// Every switch, projected onto theory rows.
    /// </summary>
    /// <returns>One row per switch.</returns>
    public static TheoryData<string> Switches() => [.. SwitchKeys];

    /// <summary>
    /// A deployment that turns one check off does not start, and the fault names the key and the remedy.
    /// </summary>
    /// <param name="switchKey">The key that is set to false.</param>
    /// <remarks>
    /// <para>
    /// THE HOST IS PROVOKED INTO STARTING BY RESOLVING ITS OPTIONS, which is how this suite's factory
    /// exposes a startup fault as a throw from a test rather than as an unobservable failure inside the
    /// framework. The gate runs after the host is built, so this is the earliest observable point.
    /// </para>
    /// <para>
    /// THE MESSAGE CONTENT IS ASSERTED, NOT JUST THE THROW. An operator meeting this failure has to be
    /// able to fix it without reading the source, so the offending key must appear, the reader must be
    /// told the setting would not have taken effect anyway, and the remedy must be stated. A refusal
    /// carrying none of those is a wall rather than a diagnostic.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Switches))]
    public void ADeploymentDisablingOneInboundValidationCheckDoesNotStart(string switchKey)
    {
        using SecurityAppFactory factory = new();

        factory.Settings[$"{InboundAuthenticationSection}:{switchKey}"] = "false";

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => factory.ResolveSecurityOptions());

        Assert.Contains(switchKey, refused.Message, StringComparison.Ordinal);
        Assert.Contains("set to false", refused.Message, StringComparison.Ordinal);
        Assert.Contains("would not take effect", refused.Message, StringComparison.Ordinal);
        Assert.Contains("set them to true", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A deployment that turns all four off is told about all four in one message.
    /// </summary>
    /// <remarks>
    /// FIXED IN ONE PASS RATHER THAN IN FOUR RESTARTS. Reporting the first and stopping would make an
    /// operator restart four times to discover four faults, and each restart would look like a new
    /// problem rather than the same one. Booting a host is the slowest way to learn anything, which is
    /// what makes reporting them together worth asserting rather than assuming.
    /// </remarks>
    [Fact]
    public void ADeploymentDisablingEveryInboundValidationCheckIsToldAboutAllFourAtOnce()
    {
        using SecurityAppFactory factory = new();

        foreach (string switchKey in SwitchKeys)
        {
            factory.Settings[$"{InboundAuthenticationSection}:{switchKey}"] = "false";
        }

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => factory.ResolveSecurityOptions());

        foreach (string switchKey in SwitchKeys)
        {
            Assert.Contains(switchKey, refused.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A deployment that states all four explicitly as true starts.
    /// </summary>
    /// <remarks>
    /// THE CONTROL ROW FOR THE EXPLICIT CASE. Without it every refusal row would prove only that
    /// SOMETHING about the shaped configuration prevented startup, and would keep passing if the gate
    /// began refusing the enabled value as well.
    /// </remarks>
    [Fact]
    public void ADeploymentStatingEveryCheckExplicitlyTrueStarts()
    {
        using SecurityAppFactory factory = new();

        foreach (string switchKey in SwitchKeys)
        {
            factory.Settings[$"{InboundAuthenticationSection}:{switchKey}"] = "true";
        }

        Assert.NotNull(factory.ResolveSecurityOptions());
    }

    /// <summary>
    /// A deployment that states none of the four starts, because the absent value is the safe one.
    /// </summary>
    /// <remarks>
    /// AN ABSENT KEY IS THE ORDINARY CASE AND MUST NOT BE A FAULT. The gate defaults each key to true
    /// when it is missing, so the shipped settings file states all four only for a reviewer's benefit. A
    /// gate that treated absence as disablement would refuse every deployment that had not enumerated
    /// them, which is the opposite of a safe default.
    /// </remarks>
    [Fact]
    public void ADeploymentStatingNoneOfTheFourStarts()
    {
        using SecurityAppFactory factory = new();

        foreach (string switchKey in SwitchKeys)
        {
            factory.Settings[$"{InboundAuthenticationSection}:{switchKey}"] = null;
        }

        Assert.NotNull(factory.ResolveSecurityOptions());
    }

    /// <summary>
    /// A deployment that turns inbound claim mapping ON does not start, and the fault names the key and
    /// the consequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THE FIFTH SETTING, AND THE ONLY ONE WHOSE FAULT IS A CONFIGURED <c>true</c>.</b> It cannot
    /// join the theory above for exactly that reason: the four switches are refused when false, and this
    /// one when true, so a single loop would have to carry the polarity as data and would then be asserting
    /// the loop rather than the rule.
    /// </para>
    /// <para>
    /// <b>WHAT IT PROTECTS, stated because "claim mapping" sounds cosmetic and is not.</b> The legacy
    /// handler rewrites the standard <c>scope</c> and <c>sub</c> claim names into WS-Federation URIs. The
    /// scope policy looks for <c>scope</c>; with mapping on it would find nothing, and a policy that finds
    /// no scopes admits no scope - so every authenticated request would meet a 403 and the authorization
    /// surface would be uniformly broken rather than visibly misconfigured.
    /// </para>
    /// <para>
    /// <b>AND IT REPLACES A DEAD READ.</b> Section 5 of the composition root used to assign this from
    /// configuration and then overwrite it with the literal a few lines later, so a configured value took
    /// no effect while an operator reading the settings file would believe it had. The read is gone; the
    /// refusal is what tells a deployment that the value is neither honoured nor honourable.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADeploymentEnablingInboundClaimMappingDoesNotStart()
    {
        using SecurityAppFactory factory = new();

        factory.Settings[$"{InboundAuthenticationSection}:{MapInboundClaimsKey}"] = "true";

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => factory.ResolveSecurityOptions());

        Assert.Contains(MapInboundClaimsKey, refused.Message, StringComparison.Ordinal);
        Assert.Contains("set to true", refused.Message, StringComparison.Ordinal);
        Assert.Contains("would not take effect", refused.Message, StringComparison.Ordinal);
        Assert.Contains("set it to false", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A deployment stating claim mapping explicitly false starts, because it agrees with the literal.
    /// </summary>
    /// <remarks>
    /// THE SHIPPED SETTINGS FILE STATES IT, so this is not a hypothetical shape. A value that agrees with
    /// the literal changes nothing and is accepted for the same reason the four switches accept an explicit
    /// <c>true</c>: the point of the gate is to refuse a setting that would be IGNORED, not to refuse the
    /// key's existence.
    /// </remarks>
    [Fact]
    public void ADeploymentStatingClaimMappingExplicitlyFalseStarts()
    {
        using SecurityAppFactory factory = new();

        factory.Settings[$"{InboundAuthenticationSection}:{MapInboundClaimsKey}"] = "false";

        Assert.NotNull(factory.ResolveSecurityOptions());
    }

    /// <summary>
    /// A deployment stating claim mapping not at all starts, because the absent value is the safe one.
    /// </summary>
    [Fact]
    public void ADeploymentStatingNoClaimMappingValueStarts()
    {
        using SecurityAppFactory factory = new();

        factory.Settings[$"{InboundAuthenticationSection}:{MapInboundClaimsKey}"] = null;

        Assert.NotNull(factory.ResolveSecurityOptions());
    }
}
