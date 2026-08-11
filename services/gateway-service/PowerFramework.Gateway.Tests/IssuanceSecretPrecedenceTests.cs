// ==================================================================================================
//  THE ISSUANCE SECRET'S TWO INGRESSES, AND WHICH ONE WINS
//
//  WHAT THIS FILE EXISTS FOR. `GatewayOptions.SecurityClientSecret` is the password half of the HTTP
//  Basic credential Gateway presents to Security's issuance operation, and it can arrive by two routes:
//
//    * the FLAT key SECURITY_CLIENT_SECRET_GATEWAY, which is the documented route and the name
//      Security's own issuance roster and orchestration/.env.example declare for this caller. It is flat
//      because the environment-variable provider folds only a DOUBLE underscore onto the ':' separator,
//      so a value under that spelling cannot land inside the `Gateway` section by binding at all; and
//
//    * ordinary section binding, because the property is a public settable leaf of the bound `Gateway`
//      section - so `Gateway__SecurityClientSecret` in the environment does reach it.
//
//  THE DEFECT THESE ROWS PIN CLOSED. The composition root's post-configure step assigned the flat key
//  UNCONDITIONALLY, with `?? string.Empty`. An absent flat key therefore did not leave the property
//  alone - it OVERWROTE whatever binding had put there with empty. A deployment supplying the credential
//  through the section watched the binder accept it and was then refused at startup for presenting
//  nothing, with a message naming a key it had deliberately not used. A silently discarded input is
//  worse than a rejected one, because there is nothing anywhere that says the value was dropped.
//
//  AND ONE IDEA HAD THREE BEHAVIOURS ACROSS THREE SERVICES, which is the part that makes this more than
//  a local bug. Security's signing-key step states the rule normatively - when the key is absent the step
//  assigns nothing, so material that reached the options instance through another legitimate ingress
//  survives - and DataServices' ApplyIssuanceSecret is the same shape. Gateway was the outlier.
//
//  WHAT IS ASSERTED. The four cells of the precedence table, one row each, so a failure names the cell
//  that changed rather than reporting that "the secret resolution changed":
//
//    | flat key | section value | expected                                                |
//    | set      | set           | starts, flat wins                                       |
//    | set      | unset         | starts, flat value in force                             |
//    | UNSET    | set           | starts, SECTION value survives   <- the defect          |
//    | unset    | unset         | refused at startup, message names the flat key          |
//
//  NO ASSERTION RENDERS A SECRET (C-F). Every comparison is made as a boolean with a value-free message,
//  because Assert.Equal on two strings prints both operands - which would publish the credential at
//  exactly the moment a row failed. The values here are fixture-shaped and never leave the process, and
//  the discipline is applied anyway: a suite that prints test credentials teaches the habit that prints
//  real ones.
// ==================================================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using PowerFramework.Gateway.Configuration;

using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// The flat configuration key wins when present, and an absent one discards nothing.
/// </summary>
public sealed class IssuanceSecretPrecedenceTests
{
    /// <summary>The configuration path the bound section exposes for the same property.</summary>
    /// <remarks>
    /// COMPOSED FROM THE PRODUCTION CONSTANTS rather than spelled, so a section rename or a property
    /// rename moves this with it instead of leaving the row asserting a path nothing reads.
    /// </remarks>
    private static readonly string SectionKey =
        $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.SecurityClientSecret)}";

    /// <summary>A value supplied through the flat key.</summary>
    private const string FlatValue = "flat-key-issuance-value";

    /// <summary>A value supplied through the bound section.</summary>
    private const string SectionValue = "section-bound-issuance-value";

    /// <summary>
    /// A value supplied through the section survives an absent flat key.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THIS IS THE FINDING. Before the presence guard, the post-configure step overwrote this value with
    /// empty and the host was refused at startup - so the row asserts both that it STARTS and that the
    /// value actually in force is the one the deployment supplied, since a host that started carrying a
    /// different credential would fail authentication later for a reason no log explains.
    /// </remarks>
    [Fact]
    public async Task ASectionSuppliedSecretSurvivesAnAbsentFlatKey()
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        // The fixture sets the flat key itself, so it is cleared through the SAME key rather than by
        // reaching past the production resolution path.
        host.AdditionalSettings[GatewayOptions.SecurityClientSecretConfigurationKey] = string.Empty;
        host.AdditionalSettings[SectionKey] = SectionValue;

        using HttpClient client = host.CreateAnonymousClient();

        AssertSecretInForce(host, SectionValue, "the section-supplied value");
    }

    /// <summary>
    /// The flat key wins when both are supplied.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// PRECEDENCE IS ASSERTED IN THE DIRECTION THAT MATTERS. The flat key is the name both sides of the
    /// issuance edge agree on - Security's roster resolves this caller's secret from it - so a deployment
    /// setting both has stated its intent through the channel that is authoritative. Reversing this would
    /// let a stale section value shadow the credential the orchestration layer actually provisioned.
    /// </remarks>
    [Fact]
    public async Task TheFlatKeyWinsWhenBothAreSupplied()
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        host.AdditionalSettings[GatewayOptions.SecurityClientSecretConfigurationKey] = FlatValue;
        host.AdditionalSettings[SectionKey] = SectionValue;

        using HttpClient client = host.CreateAnonymousClient();

        AssertSecretInForce(host, FlatValue, "the flat-key value");
    }

    /// <summary>
    /// The flat key alone is sufficient, which is the documented bring-up.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE CONTROL THAT KEEPS THE OTHER ROWS HONEST. Without it, a guard that had stopped reading the flat
    /// key altogether would satisfy the section row and the refusal row while breaking every real
    /// deployment, since the flat key is the only route orchestration/.env.example describes.
    /// </remarks>
    [Fact]
    public async Task TheFlatKeyAloneIsSufficient()
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        host.AdditionalSettings[GatewayOptions.SecurityClientSecretConfigurationKey] = FlatValue;

        using HttpClient client = host.CreateAnonymousClient();

        AssertSecretInForce(host, FlatValue, "the flat-key value");
    }

    /// <summary>
    /// Neither ingress supplied is still a startup refusal.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE GUARD MUST NOT HAVE BECOME PERMISSIVENESS. Leaving an absent flat key alone is correct only
    /// while "nothing supplied it by any route" remains a refusal - otherwise the fix would have traded a
    /// discarded credential for a host that starts holding none, which is the state contract C-01 makes
    /// unusable: `POST /v1/tokens` is the one operation a bearer token cannot protect, so a caller with no
    /// credential obtains no token and nothing downstream is reachable. The refusal names the flat key,
    /// because that is the route an operator is expected to use.
    /// </remarks>
    [Fact]
    public async Task NeitherIngressSuppliedIsStillARefusal()
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        host.AdditionalSettings[GatewayOptions.SecurityClientSecretConfigurationKey] = string.Empty;

        OptionsValidationException refused = Assert.Throws<OptionsValidationException>(
            () => host.CreateAnonymousClient().Dispose());

        Assert.Contains(
            refused.Failures,
            failure => failure.Contains(
                GatewayOptions.SecurityClientSecretConfigurationKey,
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Whitespace is treated as absent, on both ingresses.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// A BLANK CREDENTIAL CANNOT AUTHENTICATE, so accepting one would produce a 401 whose cause is
    /// invisible - the deployment supplied something, the binder accepted it, and the edge refuses it. The
    /// row asserts both halves of that rule at once: a whitespace FLAT key does not displace a real
    /// section value (so whitespace is absent for precedence), and a whitespace SECTION value with no flat
    /// key is refused at startup (so whitespace is absent for validity too).
    /// </remarks>
    [Fact]
    public async Task WhitespaceIsTreatedAsAbsentOnBothIngresses()
    {
        await using GatewayTestHostFixture surviving =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        surviving.AdditionalSettings[GatewayOptions.SecurityClientSecretConfigurationKey] = "   ";
        surviving.AdditionalSettings[SectionKey] = SectionValue;

        using HttpClient survivingClient = surviving.CreateAnonymousClient();

        AssertSecretInForce(surviving, SectionValue, "the section-supplied value");

        await using GatewayTestHostFixture refusedHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        refusedHost.AdditionalSettings[GatewayOptions.SecurityClientSecretConfigurationKey] = "   ";
        refusedHost.AdditionalSettings[SectionKey] = "   ";

        Assert.Throws<OptionsValidationException>(
            () => refusedHost.CreateAnonymousClient().Dispose());
    }

    /// <summary>
    /// Asserts which value the running host actually holds, without rendering any secret.
    /// </summary>
    /// <param name="host">The started fixture.</param>
    /// <param name="expected">The value that must be in force.</param>
    /// <param name="description">A value-free description of that value, for the failure message.</param>
    /// <remarks>
    /// BOOLEAN RATHER THAN <c>Assert.Equal</c>, because the equality overload renders both operands and a
    /// failing row would then print two credentials into the test output (C-F). The description names
    /// WHICH value was expected without being it.
    /// </remarks>
    private static void AssertSecretInForce(
        GatewayTestHostFixture host,
        string expected,
        string description)
    {
        GatewayOptions options = host.Services
            .GetRequiredService<IOptions<GatewayOptions>>()
            .Value;

        Assert.True(
            string.Equals(options.SecurityClientSecret, expected, StringComparison.Ordinal),
            $"The issuance secret in force is not {description}.");

        // And it is non-blank, so a row cannot pass by both sides being empty.
        Assert.False(string.IsNullOrWhiteSpace(options.SecurityClientSecret));
    }
}
