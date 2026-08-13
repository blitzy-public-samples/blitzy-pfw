// ==================================================================================================
//  InboundAudienceDeclarationTests.cs - THIS SERVICE ACCEPTS TOKENS ADDRESSED TO IT, AND TO NOTHING
//                                      ELSE
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  Audience validation is the mechanism that stops a token minted for one service being replayed at
//  another. Security's own inbound audience is declared at `Authentication:Jwt:Audience`, and the
//  composition root used to treat an ABSENT declaration as an instruction to accept the WHOLE ISSUANCE
//  ROSTER - so a host that simply had not declared its own identity accepted, at its own authenticated
//  routes, every token minted for Gateway, DataServices or Persistence. That is a fail-OPEN default in
//  the one place the estate cannot afford one: the service that mints, and whose `/v1/crypto` surface
//  reads configured key material.
//
//  The widening is withdrawn. An undeclared inbound audience is a REFUSAL TO START naming the
//  configuration key, which is the legacy fail-fast posture [ws_objects/pfw.pbl.src/pfw.sra:L111-L144]
//  rather than a fallback - and it costs a shipped deployment nothing, because Security's own settings
//  file declares the audience.
//
//  WHAT IS ASSERTED
//    * The shipped configuration starts and its inbound audience is its own identity, not the roster.
//    * A blank declaration - in every spelling a configuration provider can deliver - refuses the host,
//      and the refusal names the key rather than describing it.
//    * The refusal is not the roster-membership check firing under another name: the two rules answer
//      different faults and this row proves the ABSENCE case reaches its own message.
//
//  C-I INDEPENDENCE. Every host below is in-memory. No port is bound and no sibling service is required.
// ==================================================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PowerFramework.Security.Configuration;

using Xunit;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Verifies that Security validates inbound tokens against its OWN audience, and that an undeclared
/// audience refuses the host instead of widening acceptance to the issuance roster.
/// </summary>
public sealed class InboundAudienceDeclarationTests
{
    /// <summary>The configuration key carrying this service's own inbound audience.</summary>
    private const string InboundAudienceKey = "Authentication:Jwt:Audience";

    /// <summary>
    /// The shipped configuration starts, and the audience it validates against is its own identity.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE POSITIVE ARM, AND IT IS WHAT MAKES THE REFUSAL SAFE TO ADD. A rule that refused every host
    /// would satisfy the rows below and make the service unstartable, so this row boots the real
    /// composition root on the real settings file and reads the value back out of the built host.
    /// </para>
    /// <para>
    /// IT ALSO ASSERTS THE NARROWNESS. The declared audience is a SINGLE identity and the issuance roster
    /// is wider than it, so a host that had silently widened to the roster would carry an audience the
    /// roster contains alongside others - which is exactly the state the withdrawn fallback produced.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheShippedConfigurationValidatesAgainstItsOwnAudienceAsync()
    {
        await using SecurityAppFactory factory = new();

        using HttpClient client = factory.CreateClient();

        string declared = factory.Services.GetRequiredService<IConfiguration>()[InboundAudienceKey]
            ?? string.Empty;

        Assert.False(string.IsNullOrWhiteSpace(declared));

        SecurityOptions options = factory.ResolveSecurityOptions();

        // The declared identity is ON the roster - the composition root refuses a host whose inbound
        // identity no token it can mint would carry - and the roster is not itself the accepted set.
        Assert.Contains(declared, options.Audiences);
        Assert.Equal(declared, factory.ResolveInboundAudience());
    }

    /// <summary>
    /// A blank inbound audience refuses the host and the refusal names the configuration key.
    /// </summary>
    /// <param name="declared">The blank spelling this row configures.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// EVERY BLANK SPELLING IS DRIVEN because a configuration provider can deliver any of them and they
    /// arrive indistinguishable in intent: an empty string from an environment variable set to nothing, and
    /// whitespace from a settings file whose value was cleared by hand. A rule that tested only for null
    /// would widen acceptance for both.
    /// </para>
    /// <para>
    /// THE MESSAGE IS ASSERTED TO NAME THE KEY, not merely to exist. An operator reading "no audience is
    /// declared" has to know where to declare one, and this service reads it from a section path that is
    /// spelled differently on each of the four services.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task ABlankInboundAudienceRefusesTheHostAsync(string declared)
    {
        await using SecurityAppFactory factory = new() { InboundAudience = declared };

        InvalidOperationException refusal =
            Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(InboundAudienceKey, refusal.Message, StringComparison.Ordinal);

        // AND IT IS THE ABSENCE RULE RATHER THAN THE MEMBERSHIP RULE. The roster-membership check answers a
        // DECLARED audience no token can carry; conflating the two would leave the absence case reported by
        // a message about membership, which names the wrong remedy.
        Assert.DoesNotContain("not a member of", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A declared audience the issuance roster does not carry still refuses the host, by its own rule.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE SIBLING RULE, ASSERTED HERE SO THE TWO CANNOT COLLAPSE INTO ONE. An audience no token this
    /// issuer can mint would mean `/v1/ping` is unreachable by any credential the estate can produce - a
    /// service that reports ready and answers 401 to everything. Its message is distinct from the absence
    /// case above, and the pair of rows is what keeps each fault reported by the message that names its own
    /// remedy.
    /// </remarks>
    [Fact]
    public async Task ADeclaredAudienceOutsideTheIssuanceRosterRefusesTheHostAsync()
    {
        await using SecurityAppFactory factory = new()
        {
            InboundAudience = "powerframework-security-audience-not-on-the-roster",
        };

        InvalidOperationException refusal =
            Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(InboundAudienceKey, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("not a member of", refusal.Message, StringComparison.Ordinal);
    }
}
