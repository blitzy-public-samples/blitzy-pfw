// ======================================================================================================
//  FileBackedSecretTests.cs - A SECRET SUPPLIED AS A FILE, AND THE THREE THINGS THAT MUST REFUSE
//  ------------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  Every secret this service consumes used to arrive as an ENVIRONMENT VARIABLE: the RSA private key that
//  signs every token in the estate, and each caller's issuance credential. An environment variable is
//  readable from far more places than its owner expects - `docker compose config` renders it in cleartext
//  and is the command an operator is told to run when a bring-up misbehaves, `docker inspect` returns it
//  to anyone who can reach the daemon socket, `/proc/<pid>/environ` exposes it to any process under the
//  same account, every child process inherits it, and it lands in shell history and CI logs. A projected
//  file has none of those properties.
//
//  WHY THE REFUSALS MATTER MORE THAN THE HAPPY PATH
//  Reading a file is the easy half. The half that decides whether this is an improvement is what happens
//  when the file is not there: a resolver that quietly fell back to the environment value would
//  substitute a DIFFERENT credential for the one the deployment declared, so a rotation would appear to
//  have been applied while the old secret was still being presented - and nothing would say so. The three
//  refusal rows below are therefore the substance of this file, and the positive rows exist so a blanket
//  refusal cannot pass them.
//
//  WHY THE KEY LIST IS ASSERTED AS DERIVED
//  The resolver knows no key names of its own. It takes the names this service already declares it reads
//  secrets from - two signing-key constants plus whatever each `Security:Clients[n]:SecretConfigurationKey`
//  names - so a client added to the roster becomes file-backable with no code change. A hand-maintained
//  list would drift, and the failure mode is silent: the secret keeps using its environment form after an
//  operator has moved it to a file.
//
//  WHAT IS DELIBERATELY NOT HERE
//  No row asserts that a file form is REQUIRED. It must not be: the environment-variable form still works,
//  because making it fail would break the documented bring-up (C-I) and every deployment that had not
//  migrated. The projection is what the shipped manifest does; the fallback is what the contract allows.
// ======================================================================================================

using Microsoft.Extensions.Configuration;
using PowerFramework.Security.Configuration;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Covers the <c>&lt;KEY&gt;_FILE</c> convention: what it resolves, what it refuses, and that the key list
/// it operates over is derived from this service's own declarations rather than restated.
/// </summary>
public sealed class FileBackedSecretTests
{
    /// <summary>The key the active signing material is read from.</summary>
    private const string SigningKey = "SECURITY_JWT_SIGNING_KEY";

    /// <summary>A value standing in for key material. Not a real key and not of a real key's shape.</summary>
    private const string Material = "not-a-real-key-just-a-marker";

    [Fact]
    public void AFileFormSuppliesTheValueAndTheTrailingNewlineIsNotPartOfIt()
    {
        // THE TRIM IS THE POINT OF THIS ROW, not an incidental detail. Every ordinary way of writing a
        // secret to a file appends a newline - `openssl rand -base64 32 > f`, `echo`, a text editor - so a
        // resolver that did not trim would hand the consumer material with a trailing byte the operator
        // never intended. For a base64 key that produces a decode failure at first use; for a shared
        // secret it produces a mismatch that looks exactly like a wrong credential.
        IConfiguration configuration = Build((SigningKey + "_FILE", "/secrets/jwt"));

        IReadOnlyDictionary<string, string> resolved = FileBackedSecrets.Resolve(
            configuration,
            [SigningKey],
            static _ => Material + "\n",
            static _ => true);

        Assert.Equal(Material, Assert.Contains(SigningKey, resolved));
    }

    [Fact]
    public void NoFileFormLeavesTheEnvironmentValueUntouched()
    {
        // THE FALLBACK THAT MUST KEEP WORKING. A deployment that has not migrated to files still brings the
        // stack up, which is what stops this change breaking the documented path (C-I). The resolver
        // contributes NOTHING here rather than contributing an empty value, because an empty entry added
        // last in the configuration chain would override the environment value with nothing.
        IConfiguration configuration = Build((SigningKey, Material));

        IReadOnlyDictionary<string, string> resolved = FileBackedSecrets.Resolve(
            configuration,
            [SigningKey],
            static _ => throw new InvalidOperationException("no file should be read"),
            static _ => throw new InvalidOperationException("no path should be probed"));

        Assert.Empty(resolved);
    }

    [Fact]
    public void DeclaringBothAnInlineValueAndAFileIsRefusedRatherThanResolved()
    {
        // AMBIGUITY IN A SECRET IS NOT A PREFERENCE PROBLEM. With two sources an operator cannot tell which
        // credential the service is presenting, so a rotation applied to one of them appears to have taken
        // effect while the other is still in use - and the service that keeps working is the evidence the
        // operator reads as success. Preferring either silently is how that happens, so neither is
        // preferred.
        IConfiguration configuration = Build(
            (SigningKey, Material),
            (SigningKey + "_FILE", "/secrets/jwt"));

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => FileBackedSecrets.Resolve(
                configuration,
                [SigningKey],
                static _ => Material,
                static _ => true));

        // The diagnostic names BOTH keys and the path, because an operator cannot resolve a conflict they
        // cannot locate. It quotes no value.
        Assert.Contains(SigningKey, failure.Message, StringComparison.Ordinal);
        Assert.Contains(SigningKey + "_FILE", failure.Message, StringComparison.Ordinal);
        Assert.Contains("/secrets/jwt", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Material, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingFileRefusesRatherThanFallingBackToTheEnvironmentValue()
    {
        // THE MOST IMPORTANT ROW HERE. The fallback is the dangerous behaviour, not the failure: a
        // deployment that moved its secret to a file and mis-granted the projection would otherwise start
        // successfully, presenting whatever stale value was still in the environment. Refusing is loud and
        // recoverable; substituting a different credential is silent and looks like a working service.
        IConfiguration configuration = Build(
            (SigningKey + "_FILE", "/secrets/absent"));

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => FileBackedSecrets.Resolve(
                configuration,
                [SigningKey],
                static _ => throw new InvalidOperationException("an absent file must not be read"),
                static _ => false));

        Assert.Contains("/secrets/absent", failure.Message, StringComparison.Ordinal);
        Assert.Contains("does not exist", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreadableFileRefusesAndSaysWhereToLook()
    {
        // PERMISSIONS ARE THE LIKELIEST CAUSE AND THE DIAGNOSTIC SAYS SO. Compose accepts `mode:`, `uid:`
        // and `gid:` on a secret and ignores all three outside Swarm, so a host file at 0600 projects as
        // root-owned while every image here runs unprivileged. Without that pointer the failure reads as a
        // missing secret and an operator re-checks the grant they already got right.
        IConfiguration configuration = Build((SigningKey + "_FILE", "/secrets/jwt"));

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => FileBackedSecrets.Resolve(
                configuration,
                [SigningKey],
                static _ => throw new UnauthorizedAccessException("denied"),
                static _ => true));

        Assert.Contains("permissions", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<UnauthorizedAccessException>(failure.InnerException);
    }

    [Fact]
    public void AnEmptyFileRefusesBecauseAnEmptySecretIsNotACredential()
    {
        // A TRUNCATED PROJECTION IS THE CASE THIS CATCHES - an empty mount, a file written by a job that
        // failed after creating it. Accepting it would start the service with a zero-length signing key,
        // and the first token would be signed with nothing.
        IConfiguration configuration = Build((SigningKey + "_FILE", "/secrets/jwt"));

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => FileBackedSecrets.Resolve(
                configuration,
                [SigningKey],
                static _ => "   \n  ",
                static _ => true));

        Assert.Contains("empty", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheDeclaredKeysCoverBothSigningSlotsAndEveryClientSecretTheRosterNames()
    {
        // THE LIST IS DERIVED, AND THIS ROW IS WHAT KEEPS IT DERIVED. A client added to the roster must
        // become file-backable with no code change; if this were a hand-maintained list the new client
        // would keep reading its environment variable after an operator had moved it to a file, and
        // nothing would report that.
        IConfiguration configuration = Build(
            ("Security:Clients:0:SecretConfigurationKey", "SECURITY_CLIENT_SECRET_GATEWAY"),
            ("Security:Clients:1:SecretConfigurationKey", "SECURITY_CLIENT_SECRET_DATASERVICES"),
            ("Security:Clients:2:SecretConfigurationKey", "SECURITY_CLIENT_SECRET_A_LATER_CALLER"));

        string[] declared = [.. FileBackedSecrets.DeclaredSecretKeys(configuration).Order(StringComparer.Ordinal)];

        Assert.Equal(
            (string[])
            [
                "SECURITY_CLIENT_SECRET_A_LATER_CALLER",
                "SECURITY_CLIENT_SECRET_DATASERVICES",
                "SECURITY_CLIENT_SECRET_GATEWAY",
                SecurityOptions.RetiringSigningKeyEnvironmentVariableName,
                SecurityOptions.SigningKeyEnvironmentVariableName,
            ],
            declared);
    }

    [Fact]
    public void AClientDeclaringNoSecretKeyContributesNothingRatherThanAnEmptyName()
    {
        // A CALLER AUTHENTICATING BY CLIENT CERTIFICATE HAS NO SECRET KEY, and that is a supported roster
        // shape rather than a misconfiguration - `SecretConfigurationKey` is optional precisely for it. An
        // empty or absent declaration must therefore be skipped, not turned into a key named "" whose
        // `_FILE` companion is the bare suffix.
        IConfiguration configuration = Build(
            ("Security:Clients:0:Subject", "powerframework-gateway"),
            ("Security:Clients:1:SecretConfigurationKey", "   "));

        string[] declared = [.. FileBackedSecrets.DeclaredSecretKeys(configuration)];

        Assert.Equal(2, declared.Length);
        Assert.DoesNotContain(string.Empty, declared);
        Assert.DoesNotContain(FileBackedSecrets.FileSuffix, declared);
    }

    [Fact]
    public void AnEmptyFilePathIsTreatedAsNoDeclarationRatherThanAsAFault()
    {
        // THE SHIPPED TEMPLATE DECLARES THE OPTIONAL FILE VARIABLES EMPTY, and the manifest passes them
        // through as empty, so this is the state of every ordinary bring-up rather than an edge case. An
        // empty path must mean "no file form declared"; treating it as a fault would make the stack
        // unstartable from the committed template, which is exactly the regression constraint C-I forbids.
        IConfiguration configuration = Build(
            (SigningKey, Material),
            (SigningKey + "_FILE", string.Empty));

        IReadOnlyDictionary<string, string> resolved = FileBackedSecrets.Resolve(
            configuration,
            [SigningKey],
            static _ => throw new InvalidOperationException("no file should be read"),
            static _ => throw new InvalidOperationException("no path should be probed"));

        Assert.Empty(resolved);
    }

    /// <summary>Builds an in-memory configuration from key/value pairs.</summary>
    /// <param name="entries">The entries to seed.</param>
    /// <returns>The configuration.</returns>
    private static IConfiguration Build(params (string Key, string Value)[] entries)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(
                entries.Select(static entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)))
            .Build();
}
