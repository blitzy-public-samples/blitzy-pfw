// ==================================================================================================
//  pfw-capture - the entry point
//  ------------------------------------------------------------------------------------------------
//  WHAT A RUN IS. One workflow, one running service, one plan: resolve the plan, mint or read the
//  credential, drive every declared output, screen every captured value, and write the artifacts plus
//  a manifest - or refuse, with a message that says what to do. Nothing else. It takes no capture of
//  the legacy half, it never edits a workflow definition, it never touches an executionStatus, and it
//  never writes into the store's target-side directory while the legacy half is missing.
//
//  EXIT CODES ARE THE INTERFACE FOR CI: 0 when every declared output was captured and written, 1 for
//  every refusal and every divergence. A divergence is a failure rather than a recorded observation,
//  because a capture that half happened must not look like one that did.
// ==================================================================================================

using System.Text.Json.Nodes;

namespace PowerFramework.Characterization.Capture;

/// <summary>The command-line entry point.</summary>
internal static class Program
{
    /// <summary>Runs one capture.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>0 on a complete capture, 1 on any refusal or divergence.</returns>
    internal static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args, CancellationToken.None).ConfigureAwait(false);
        }
        catch (CaptureFailure failure)
        {
            // NO STACK TRACE. The message is the whole information, and it is addressed to whoever ran
            // the tool rather than to whoever wrote it.
            Console.Error.WriteLine("pfw-capture: " + failure.Message);

            return 1;
        }
        catch (HttpRequestException failure)
        {
            Console.Error.WriteLine(
                "pfw-capture: the service could not be reached: " + failure.Message
                + Environment.NewLine
                + "Nothing was written. Bring the stack up as orchestration/README.md documents and check "
                + "--base-url and --ca-file.");

            return 1;
        }
    }

    /// <summary>Runs one capture, raising <see cref="CaptureFailure"/> on any refusal.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="cancellationToken">The abort signal.</param>
    /// <returns>The exit code.</returns>
    private static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        CaptureOptions options = CaptureOptions.Parse(args);

        if (options.ShowHelp)
        {
            Console.WriteLine(CaptureOptions.Usage);

            return 0;
        }

        string repositoryRoot = RecordingWriter.LocateRepositoryRoot();

        if (options.PairStateOnly)
        {
            // BEFORE ANY OTHER REQUIREMENT. The report needs no service, no plan and no credential, so
            // demanding them would make the one command that always works the hardest one to run.
            return PairState.Report(repositoryRoot, Console.Out);
        }

        string planPath = options.PlanPath ?? Path.Combine(
            repositoryRoot,
            StoreRules.StoreDirectoryName,
            "tools",
            "plans",
            options.WorkflowId + ".plan.json");

        CapturePlan plan = CapturePlan.Load(planPath);

        if (!string.Equals(plan.WorkflowId, options.WorkflowId, StringComparison.Ordinal))
        {
            throw new CaptureFailure(
                $"The plan at '{planPath}' declares workflowId '{plan.WorkflowId}' but --workflow named "
                + $"'{options.WorkflowId}'. The identifier is the PAIRING KEY, so a mismatch would file a "
                + "recording against the wrong workflow and make a comparison meaningless.");
        }

        string destination = options.OutputDirectory
            ?? RecordingWriter.StoreDirectoryFor(repositoryRoot, options.WorkflowId);

        RecordingWriter.Authorize(repositoryRoot, destination, options.WorkflowId);

        SecretScreen screen = new();

        // EVERY SUPPLIED REFERENCE IS REGISTERED, not only the three with dedicated options. A keyRef is
        // credential-equivalent (docs/CONTRACTS.md section 5.2, and QA finding F1 was exactly a keyRef
        // reaching a log), so a reference that came back echoed in a response body must be rewritten to
        // its placeholder rather than recorded - and then proved absent.
        foreach (KeyValuePair<string, string> reference in options.References)
        {
            screen.Register(reference.Value, "${ref:" + reference.Key + "}");
        }

        if (options.ClientSecretPath is { } secretPath)
        {
            screen.Register(File.ReadAllText(secretPath).Trim(), "${issuanceCredential}");
        }

        Console.WriteLine($"workflow      : {plan.WorkflowId}");
        Console.WriteLine($"plan          : {Relative(repositoryRoot, planPath)}");
        Console.WriteLine($"target service: {plan.TargetService}");
        Console.WriteLine($"declared steps: {plan.Steps.Count}");
        Console.WriteLine($"artifacts     : {plan.Artifacts.Count}");
        Console.WriteLine($"destination   : {Relative(repositoryRoot, destination)}");

        if (options.DryRun)
        {
            Console.WriteLine(
                "dry run       : the plan is coherent and the destination is writable. Nothing was "
                + "requested and nothing was written.");

            return 0;
        }

        string? token = options.TokenPath is { } tokenPath
            ? File.ReadAllText(tokenPath).Trim()
            : options.ClientSecretPath is null
                ? null
                : await CaptureRunner.MintTokenAsync(options, cancellationToken).ConfigureAwait(false);

        screen.Register(token, CaptureRunner.TokenPlaceholder);

        using HttpClient client = CaptureRunner.CreateClient(options, token);

        CaptureRunner runner = new(plan, options, screen);

        IReadOnlyDictionary<string, JsonObject> artifacts = await runner
            .RunAsync(client, cancellationToken)
            .ConfigureAwait(false);

        JsonObject manifest = BuildManifest(plan, options, artifacts);

        IReadOnlyList<string> written = RecordingWriter.Write(destination, artifacts, manifest, screen);

        foreach (string path in written)
        {
            Console.WriteLine("wrote         : " + Relative(repositoryRoot, path));
        }

        Console.WriteLine(
            $"captured      : {plan.Steps.Count}/{plan.Steps.Count} declared outputs, "
            + $"{plan.Artifacts.Count} artifacts and the manifest");

        return 0;
    }

    /// <summary>Builds the run manifest.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="options">The command line.</param>
    /// <param name="artifacts">The artifacts produced.</param>
    /// <returns>The manifest.</returns>
    /// <remarks>
    /// 🔴 NO CLOCK, NO DURATION, NO HOST NAME AND NO RUN IDENTIFIER. Every one of those would differ
    /// between two runs of the same capture and would surface as a difference where no behaviour changed -
    /// the exact failure mode the determinism seams exist to prevent - and a duration would additionally
    /// be a performance measurement, which characterization/README.md section 6.4 forbids in this store
    /// and AAP 0.8.5 forbids asserting anywhere. What the manifest carries is what a reviewer needs in
    /// order to know WHAT was captured and under which rules.
    /// </remarks>
    private static JsonObject BuildManifest(
        CapturePlan plan,
        CaptureOptions options,
        IReadOnlyDictionary<string, JsonObject> artifacts) => new()
        {
            ["workflowId"] = plan.WorkflowId,
            ["half"] = "dotnet",
            ["planVersion"] = plan.PlanVersion,
            ["targetService"] = plan.TargetService,
            ["baseAddress"] = options.BaseAddress?.GetLeftPart(UriPartial.Authority),
            ["declaredOutputs"] = plan.Steps.Count,
            ["capturedOutputs"] = plan.Steps.Count,
            ["artifacts"] = new JsonArray(
                [.. artifacts.Keys.OrderBy(static name => name, StringComparer.Ordinal)
                    .Select(static name => (JsonNode)JsonValue.Create(name)!)]),
            ["newlineConvention"] = plan.NewlineConvention,
            ["keyMaterial"] = "none - every reference is recorded as its placeholder and the absence of "
                + "each supplied value is proved before a byte is written",
            ["timing"] = "not recorded - characterization/README.md section 6.4 and AAP 0.8.5",
            ["pairState"] = "target half only; the legacy half needs the Appeon PowerBuilder virtual "
                + "machine, which no pbvm library in this repository provides, so no comparison may be "
                + "reported from this recording alone (characterization/README.md section 3.2)",
            ["notes"] = plan.Notes,
        };

    /// <summary>Renders a path relative to the repository root, for a legible message.</summary>
    /// <param name="repositoryRoot">The root.</param>
    /// <param name="path">The path.</param>
    /// <returns>The relative path with forward slashes.</returns>
    private static string Relative(string repositoryRoot, string path) =>
        Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
}
