// ======================================================================================================
//  FileBackedSecretTests.cs - Gateway'S ONE SECRET, SUPPLIED AS A FILE
//  ------------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  Gateway holds no signing key and mints nothing, but it does hold one secret: the client credential it
//  presents to Security to obtain a token. That credential used to arrive as an ENVIRONMENT VARIABLE,
//  which `docker compose config` renders in cleartext, `docker inspect` returns to anyone who can reach
//  the daemon socket, `/proc/<pid>/environ` exposes to any process under the same account, and every
//  child process inherits. A projected file has none of those properties.
//
//  WHY THE REFUSALS ARE THE SUBSTANCE
//  Reading a file is the easy half. What decides whether this is an improvement is the behaviour when the
//  file is not there: falling back to the environment value would present a DIFFERENT credential from the
//  one the deployment declared, so a rotation would look applied while the old secret was still in use.
//  Each refusal below is loud and recoverable; the fallback would be silent and would look like success.
//
//  WHY THIS SERVICE HAS ITS OWN COPY OF THE RESOLVER
//  No behaviour crosses a service boundary in this estate - the contracts project is the only permitted
//  coupling (AAP 0.7.2) - so each service carries its own copy, exactly as each carries its own
//  InternalTlsTrust and its own IngressOptions. These rows cover THIS service's copy.
// ======================================================================================================

using Microsoft.Extensions.Configuration;
using Xunit;
using PowerFramework.Gateway.Configuration;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// Covers the <c>&lt;KEY&gt;_FILE</c> convention on this service's one secret-bearing configuration key.
/// </summary>
public sealed class FileBackedSecretTests
{
    /// <summary>A value standing in for a credential. Not a real secret.</summary>
    private const string Material = "not-a-real-secret-just-a-marker";

    [Fact]
    public void TheDeclaredKeyIsThisServicesOwnClientCredentialAndNothingElse()
    {
        // ONE SECRET, AND THE NAME COMES FROM THE OPTIONS TYPE RATHER THAN BEING RESTATED HERE, so a
        // respelling cannot leave the resolver behind. Nothing else is file-backable: this service holds
        // no signing key, and asserting the SET rather than mere membership is what would catch one being
        // added without the reasoning that should accompany it.
        string[] declared =
            [.. FileBackedSecrets.DeclaredSecretKeys(Build(("unused", "unused")))];

        Assert.Equal((string[])[GatewayOptions.SecurityClientSecretConfigurationKey], declared);
    }

    [Fact]
    public void AFileFormSuppliesTheValueAndTheTrailingNewlineIsNotPartOfIt()
    {
        // THE TRIM IS THE POINT. Every ordinary way of writing a secret to a file appends a newline, so a
        // resolver that did not trim would present a credential with a trailing byte the operator never
        // intended - which fails as a mismatch that looks exactly like a wrong secret.
        IReadOnlyDictionary<string, string> resolved = FileBackedSecrets.Resolve(
            Build((Key + "_FILE", "/secrets/client")),
            [Key],
            static _ => Material + "\n",
            static _ => true);

        Assert.Equal(Material, Assert.Contains(Key, resolved));
    }

    [Fact]
    public void NoFileFormLeavesTheEnvironmentValueUntouched()
    {
        // THE FALLBACK THAT MUST KEEP WORKING, so a deployment that has not migrated still brings the
        // stack up (C-I). The resolver contributes nothing rather than an empty value, because an empty
        // entry added last in the chain would override the environment value with nothing.
        Assert.Empty(FileBackedSecrets.Resolve(
            Build((Key, Material)),
            [Key],
            static _ => throw new InvalidOperationException("no file should be read"),
            static _ => throw new InvalidOperationException("no path should be probed")));
    }

    [Fact]
    public void DeclaringBothAnInlineValueAndAFileIsRefusedRatherThanResolved()
    {
        // With two sources nobody can tell which credential is in use, so a rotation applied to one looks
        // effective while the other is still being presented. Neither is preferred.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => FileBackedSecrets.Resolve(
                Build((Key, Material), (Key + "_FILE", "/secrets/client")),
                [Key],
                static _ => Material,
                static _ => true));

        Assert.Contains(Key, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Material, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingFileRefusesRatherThanFallingBackToTheEnvironmentValue()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => FileBackedSecrets.Resolve(
                Build((Key + "_FILE", "/secrets/absent")),
                [Key],
                static _ => throw new InvalidOperationException("an absent file must not be read"),
                static _ => false));

        Assert.Contains("/secrets/absent", failure.Message, StringComparison.Ordinal);
        Assert.Contains("does not exist", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyFileRefusesBecauseAnEmptySecretIsNotACredential()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => FileBackedSecrets.Resolve(
                Build((Key + "_FILE", "/secrets/client")),
                [Key],
                static _ => "  \n ",
                static _ => true));

        Assert.Contains("empty", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnEmptyFilePathIsTreatedAsNoDeclarationRatherThanAsAFault()
    {
        // THE STATE OF EVERY ORDINARY BRING-UP, not an edge case: the manifest passes the optional file
        // variables through as empty. Treating that as a fault would make the stack unstartable from the
        // committed template (C-I).
        Assert.Empty(FileBackedSecrets.Resolve(
            Build((Key, Material), (Key + "_FILE", string.Empty)),
            [Key],
            static _ => throw new InvalidOperationException("no file should be read"),
            static _ => throw new InvalidOperationException("no path should be probed")));
    }

    /// <summary>The one configuration key this service reads secret material from.</summary>
    private static string Key => GatewayOptions.SecurityClientSecretConfigurationKey;

    /// <summary>Builds an in-memory configuration from key/value pairs.</summary>
    /// <param name="entries">The entries to seed.</param>
    /// <returns>The configuration.</returns>
    private static IConfiguration Build(params (string Key, string Value)[] entries)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(
                entries.Select(static entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)))
            .Build();
}
