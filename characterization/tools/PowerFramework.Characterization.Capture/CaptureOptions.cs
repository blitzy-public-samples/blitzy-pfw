// ==================================================================================================
//  CaptureOptions - the command line, and the rule that no credential is ever an argv element
//  ------------------------------------------------------------------------------------------------
//  🔴 THE CREDENTIAL RULE IS THE SAME ONE orchestration/README.md STATES FOR curl, AND IT IS HERE FOR
//  THE SAME MEASURED REASON: an argument vector is world-readable through /proc/<pid>/cmdline for the
//  life of the process, lands in shell history, and is captured by any process accounting the host
//  runs. So this tool accepts a bearer token or a client secret ONLY as a file path, reads the file
//  itself, and offers no flag that takes the value. There is deliberately no --token and no
//  --client-secret.
// ==================================================================================================

namespace PowerFramework.Characterization.Capture;

/// <summary>The parsed command line.</summary>
internal sealed class CaptureOptions
{
    /// <summary>The workflow to capture, which is also the pairing key.</summary>
    internal string WorkflowId { get; private set; } = string.Empty;

    /// <summary>The base address of the service the plan drives.</summary>
    internal Uri? BaseAddress { get; private set; }

    /// <summary>Where to write the recording. Defaults to the store's own target-side directory.</summary>
    internal string? OutputDirectory { get; private set; }

    /// <summary>The plan file. Defaults to the tool's own plan for the workflow.</summary>
    internal string? PlanPath { get; private set; }

    /// <summary>A PEM certificate authority to verify the service's TLS certificate against.</summary>
    internal string? CertificateAuthorityPath { get; private set; }

    /// <summary>A file holding a bearer token to present.</summary>
    internal string? TokenPath { get; private set; }

    /// <summary>The issuance base address, when the tool is to mint its own token.</summary>
    internal Uri? IssuerAddress { get; private set; }

    /// <summary>The caller identity to authenticate as at the issuance edge.</summary>
    internal string? ClientId { get; private set; }

    /// <summary>A file holding that caller's issuance credential.</summary>
    internal string? ClientSecretPath { get; private set; }

    /// <summary>The audience to request.</summary>
    internal string? Audience { get; private set; }

    /// <summary>The scopes to request.</summary>
    internal IReadOnlyList<string> Scopes { get; private set; } = [];

    /// <summary>
    /// The OPAQUE key-store references a plan's placeholders resolve against, by name.
    /// </summary>
    /// <remarks>
    /// 🔴 EVERY ENTRY IS A REFERENCE AND NEVER MATERIAL. The C-02 contract requires a caller pass an
    /// opaque reference the service resolves against its own configured store rather than key bytes
    /// (docs/CONTRACTS.md section 5.2), so this dictionary is safe to carry on a command line in a way
    /// key material never is. It is a NAMED map rather than three fixed options because a plan needs more
    /// than three - a symmetric key, a vector, a keyed-hash key, a deliberately short passphrase for the
    /// no-derivation case, and two file locators - and inventing a new option per plan would put plan
    /// detail into the tool.
    /// </remarks>
    internal IReadOnlyDictionary<string, string> References => _references;

    /// <summary>The named references, accumulated as the command line is parsed.</summary>
    private readonly Dictionary<string, string> _references = new(StringComparer.Ordinal);

    /// <summary>Reads one named reference.</summary>
    /// <param name="name">The reference name.</param>
    /// <returns>The reference, or null when it was not supplied.</returns>
    internal string? Reference(string name) =>
        _references.TryGetValue(name, out string? reference) ? reference : null;

    /// <summary>Validate and report without issuing a request or writing a file.</summary>
    internal bool DryRun { get; private set; }

    /// <summary>Report the pair state of every declared workflow instead of capturing.</summary>
    internal bool PairStateOnly { get; private set; }

    /// <summary>Print the usage text instead of running.</summary>
    internal bool ShowHelp { get; private set; }

    /// <summary>The usage text, printed on request and on a parse failure.</summary>
    internal static string Usage =>
        """
        pfw-capture - target-side capture driver for the paired characterization store

        USAGE
          dotnet run --project characterization/tools/PowerFramework.Characterization.Capture -- \
            --workflow <workflowId> --base-url <uri> [options]

        REQUIRED
          --workflow <id>          The workflow to capture. Also the pairing key, and it must be one
                                   characterization/workflows/ declares.
          --base-url <uri>         Base address of the running service the plan drives.

        AUTHENTICATION - a credential is NEVER an argument value, only ever a file path
          --token-file <path>      File holding a bearer token to present verbatim.
          --issuer-url <uri>       Issuance base address, to mint a token instead.
          --client-id <id>         Caller identity for issuance. Not a secret.
          --client-secret-file <p> File holding that caller's issuance credential.
          --audience <aud>         Audience to request.
          --scope <scope>          Scope to request. Repeatable.

        KEY-STORE REFERENCES - opaque references, never key material (docs/CONTRACTS.md 5.2)
          --key-ref <ref>          Resolves the plan's ${keyRef} placeholder. Same as --ref key=<ref>.
          --iv-ref <ref>           Resolves ${ivRef}. Same as --ref iv=<ref>.
          --file-ref <ref>         Resolves ${fileRef}. Same as --ref file=<ref>.
          --ref <name>=<ref>       Resolves ${ref:<name>}. Repeatable, for the references a plan needs
                                   beyond those three.

        OUTPUT
          --output <dir>           Where to write. Defaults to
                                   characterization/recordings/dotnet/<workflowId>/, which the driver
                                   refuses to write into until the legacy half of the pair exists.
          --plan <path>            Plan file. Defaults to
                                   characterization/tools/plans/<workflowId>.plan.json.
          --ca-file <path>         PEM authority to verify the service certificate against.
          --dry-run                Validate the plan and the destination, issue nothing, write nothing.

        REPORTING
          --pair-state             Report the pair state of every declared workflow and exit. Needs no
                                   service, no plan and no credential. Exits 0 when no workflow holds
                                   exactly one half of its pair, and 1 when one does - because a store
                                   with one half invites a conclusion no one may draw from it.
          --help                   Print this text.

        EXIT CODES
          0  every declared output was captured and written
          1  the run was refused, or a step diverged from its declared expectation
        """;

    /// <summary>Records one <c>name=reference</c> pair.</summary>
    /// <param name="pair">The raw argument value.</param>
    /// <exception cref="CaptureFailure">The pair is malformed.</exception>
    private void AddReference(string pair)
    {
        int separator = pair.IndexOf('=', StringComparison.Ordinal);

        if (separator <= 0 || separator == pair.Length - 1)
        {
            throw new CaptureFailure(
                $"--ref takes '<name>=<reference>' and received '{pair}'. Both halves are required, and "
                + "the right-hand half is an OPAQUE key-store reference rather than key material.");
        }

        _references[pair[..separator]] = pair[(separator + 1)..];
    }

    /// <summary>Parses the command line.</summary>
    /// <param name="arguments">The raw arguments.</param>
    /// <returns>The parsed options.</returns>
    /// <exception cref="CaptureFailure">An argument is unknown, missing a value or incoherent.</exception>
    internal static CaptureOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        CaptureOptions options = new();
        List<string> scopes = [];

        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];

            switch (argument)
            {
                case "--help" or "-h":
                    options.ShowHelp = true;
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--pair-state":
                    options.PairStateOnly = true;
                    break;
                case "--workflow":
                    options.WorkflowId = Value(arguments, ref index);
                    break;
                case "--base-url":
                    options.BaseAddress = AbsoluteUri(Value(arguments, ref index), argument);
                    break;
                case "--issuer-url":
                    options.IssuerAddress = AbsoluteUri(Value(arguments, ref index), argument);
                    break;
                case "--output":
                    options.OutputDirectory = Value(arguments, ref index);
                    break;
                case "--plan":
                    options.PlanPath = Value(arguments, ref index);
                    break;
                case "--ca-file":
                    options.CertificateAuthorityPath = Value(arguments, ref index);
                    break;
                case "--token-file":
                    options.TokenPath = Value(arguments, ref index);
                    break;
                case "--client-id":
                    options.ClientId = Value(arguments, ref index);
                    break;
                case "--client-secret-file":
                    options.ClientSecretPath = Value(arguments, ref index);
                    break;
                case "--audience":
                    options.Audience = Value(arguments, ref index);
                    break;
                case "--scope":
                    scopes.Add(Value(arguments, ref index));
                    break;
                case "--ref":
                    options.AddReference(Value(arguments, ref index));
                    break;
                case "--key-ref":
                    options._references["key"] = Value(arguments, ref index);
                    break;
                case "--iv-ref":
                    options._references["iv"] = Value(arguments, ref index);
                    break;
                case "--file-ref":
                    options._references["file"] = Value(arguments, ref index);
                    break;
                default:
                    // A VALUE-CARRYING CREDENTIAL FLAG IS REFUSED BY NAME rather than ignored, because
                    // somebody reaching for it has a credential on the command line right now.
                    throw new CaptureFailure(
                        argument is "--token" or "--client-secret" or "--secret" or "--password"
                            ? $"'{argument}' does not exist, deliberately. A credential is never an "
                                + "argument value here: it is world-readable through /proc/<pid>/cmdline "
                                + "and lands in shell history. Use --token-file or "
                                + "--client-secret-file with a path."
                            : $"Unknown argument '{argument}'. Run with --help for the usage text.");
            }
        }

        options.Scopes = scopes;

        if (options.ShowHelp || options.PairStateOnly)
        {
            // NEITHER NEEDS A RUNNABLE REQUEST. --pair-state reads the store off disk; requiring a
            // workflow and a base address for it would make the reporting command need a running service
            // in order to say that nothing can be compared.
            return options;
        }

        options.Verify();

        return options;
    }

    /// <summary>Checks the parsed options make a runnable request.</summary>
    /// <exception cref="CaptureFailure">The combination cannot run.</exception>
    private void Verify()
    {
        if (string.IsNullOrWhiteSpace(WorkflowId))
        {
            throw new CaptureFailure("--workflow is required. Run with --help for the usage text.");
        }

        if (BaseAddress is null && !DryRun)
        {
            throw new CaptureFailure("--base-url is required unless --dry-run is passed.");
        }

        if (TokenPath is not null && ClientSecretPath is not null)
        {
            throw new CaptureFailure(
                "--token-file and --client-secret-file are alternatives: present a token, or present an "
                + "issuance credential and let the tool mint one. Passing both leaves it ambiguous which "
                + "identity a recording was taken under, which is a fact a recording has to carry.");
        }

        if (ClientSecretPath is not null
            && (IssuerAddress is null || string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(Audience)))
        {
            throw new CaptureFailure(
                "--client-secret-file needs --issuer-url, --client-id and --audience: the issuance edge "
                + "authorises a caller for one audience and scope set, so all four are one decision.");
        }

        foreach ((string flag, string? path) in new[]
        {
            ("--token-file", TokenPath),
            ("--client-secret-file", ClientSecretPath),
            ("--ca-file", CertificateAuthorityPath),
        })
        {
            if (path is not null && !File.Exists(path))
            {
                throw new CaptureFailure($"{flag} names '{path}', which does not exist.");
            }
        }
    }

    /// <summary>Reads the value following a flag.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <param name="index">The flag's index, advanced past the value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="CaptureFailure">The flag carries no value.</exception>
    private static string Value(IReadOnlyList<string> arguments, ref int index)
    {
        string flag = arguments[index];

        if (index + 1 >= arguments.Count)
        {
            throw new CaptureFailure($"'{flag}' expects a value.");
        }

        index++;

        return arguments[index];
    }

    /// <summary>Parses an absolute address.</summary>
    /// <param name="value">The supplied text.</param>
    /// <param name="flag">The flag it came from.</param>
    /// <returns>The address.</returns>
    /// <exception cref="CaptureFailure">The value is not an absolute address.</exception>
    private static Uri AbsoluteUri(string value, string flag) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)
            ? parsed
            : throw new CaptureFailure($"'{flag}' expects an absolute address; '{value}' is not one.");
}
