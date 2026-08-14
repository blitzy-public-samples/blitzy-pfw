// ==================================================================================================
//  CapturePlan - the declarative capture plan, and why the plan is DATA rather than code
//  ------------------------------------------------------------------------------------------------
//  A workflow definition under characterization/workflows/ declares WHAT must be observed - its
//  observedOutputs are the store's contract. It deliberately declares no request bodies, no endpoints
//  and no chaining, because a definition is reviewed as a specification and must not become a script.
//  The plan supplies exactly that missing half, as a JSON document, for one reason above the others:
//  a guard test can then read BOTH and assert that the plan's step names are precisely the
//  definition's observedOutputs. Written as C# the same agreement would need the guard to reference
//  this assembly and reflect over it, and a step nobody declared would look like code rather than
//  like a divergence.
//
//  NO SECRET, NO KEY, NO CREDENTIAL AND NO MATERIAL MAY APPEAR IN A PLAN. A plan names key material
//  only by REFERENCE placeholder - the opaque keyRef the C-02 contract requires of every caller
//  (docs/CONTRACTS.md section 5.2) - and the guard test asserts that every reference in every plan is a
//  placeholder rather than a value.
// ==================================================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerFramework.Characterization.Capture;

/// <summary>One capture plan: the target-side script for a single workflow definition.</summary>
internal sealed class CapturePlan
{
    /// <summary>The plan format version, so a later shape change is explicit.</summary>
    [JsonPropertyName("planVersion")]
    public string PlanVersion { get; init; } = string.Empty;

    /// <summary>The pairing key. Must equal the workflow definition's own identifier.</summary>
    [JsonPropertyName("workflowId")]
    public string WorkflowId { get; init; } = string.Empty;

    /// <summary>The service this plan drives, for the manifest.</summary>
    [JsonPropertyName("targetService")]
    public string TargetService { get; init; } = string.Empty;

    /// <summary>How newlines are normalized in every artifact this plan writes.</summary>
    /// <remarks>
    /// characterization/README.md §3.7: the legacy master is produced on Windows and the candidate in a
    /// Linux container, so raw line endings differ BY PLATFORM rather than by behaviour. The convention
    /// belongs in the mask and is applied to both halves; the driver records which convention it applied
    /// so a reader never has to infer it.
    /// </remarks>
    [JsonPropertyName("newlineConvention")]
    public string NewlineConvention { get; init; } = "lf";

    /// <summary>A human note carried into the manifest.</summary>
    [JsonPropertyName("notes")]
    public string Notes { get; init; } = string.Empty;

    /// <summary>The artifacts this plan writes, each grouping a set of outputs.</summary>
    [JsonPropertyName("artifacts")]
    public IReadOnlyList<PlannedArtifact> Artifacts { get; init; } = [];

    /// <summary>The steps, in execution order.</summary>
    [JsonPropertyName("steps")]
    public IReadOnlyList<PlannedStep> Steps { get; init; } = [];

    /// <summary>
    /// Serializer settings shared by the loader and by every artifact this tool writes.
    /// </summary>
    /// <remarks>
    /// INDENTED AND CASE-SENSITIVE ON PURPOSE. characterization/README.md §3.4 requires every artifact be
    /// text-based and diffable, and an indented document diffs line by line where a single-line one
    /// diffs as one enormous changed line. Case sensitivity means a plan member spelled wrongly is a
    /// refusal rather than a silently ignored member.
    /// </remarks>
    internal static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Loads a plan from disk.</summary>
    /// <param name="path">The plan file.</param>
    /// <returns>The parsed plan.</returns>
    /// <exception cref="CaptureFailure">The file is missing, unparsable or incoherent.</exception>
    internal static CapturePlan Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new CaptureFailure(
                $"No capture plan exists at '{path}'. Only workflows whose declared outputs are "
                + "reachable over a published REST contract carry a plan today; a workflow driven "
                + "exclusively over gRPC has none, and inventing one here rather than writing it as a "
                + "reviewed plan file is precisely what this tool refuses to do.");
        }

        CapturePlan? plan;

        try
        {
            plan = JsonSerializer.Deserialize<CapturePlan>(File.ReadAllText(path), SerializerOptions);
        }
        catch (JsonException failure)
        {
            throw new CaptureFailure($"The capture plan at '{path}' is not valid JSON: {failure.Message}");
        }

        if (plan is null)
        {
            throw new CaptureFailure($"The capture plan at '{path}' parsed to nothing.");
        }

        plan.Validate(path);

        return plan;
    }

    /// <summary>Checks the plan is internally coherent before a single request is issued.</summary>
    /// <param name="path">The plan's path, for the message.</param>
    /// <exception cref="CaptureFailure">The plan is incoherent.</exception>
    /// <remarks>
    /// FAIL BEFORE THE FIRST CALL, NOT DURING THE RUN. A plan whose fault surfaces halfway through
    /// leaves a partial recording on disk, and a partial recording is the one artifact this store must
    /// never hold - it is indistinguishable from a capture of a broken service.
    /// </remarks>
    private void Validate(string path)
    {
        if (PlanVersion != "1")
        {
            throw new CaptureFailure($"The plan at '{path}' declares planVersion '{PlanVersion}'; only '1' is known.");
        }

        if (string.IsNullOrWhiteSpace(WorkflowId))
        {
            throw new CaptureFailure($"The plan at '{path}' names no workflowId.");
        }

        if (Steps.Count == 0)
        {
            throw new CaptureFailure($"The plan at '{path}' declares no steps.");
        }

        if (!string.Equals(NewlineConvention, "lf", StringComparison.Ordinal))
        {
            throw new CaptureFailure(
                $"The plan at '{path}' declares newlineConvention '{NewlineConvention}'. Only 'lf' is "
                + "implemented, and it is the convention characterization/README.md section 3.7 requires "
                + "be applied identically to both halves of a pair.");
        }

        foreach (PlannedArtifact artifact in Artifacts)
        {
            if (!StoreRules.IsAdmittedRecordingName(artifact.Name))
            {
                throw new CaptureFailure(
                    $"The plan at '{path}' declares artifact {StoreRules.ExplainRefusedName(artifact.Name)}");
            }
        }

        HashSet<string> artifactNames = new(Artifacts.Select(static artifact => artifact.Name), StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (PlannedStep step in Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Output))
            {
                throw new CaptureFailure($"The plan at '{path}' carries a step with no output name.");
            }

            if (!seen.Add(step.Output))
            {
                throw new CaptureFailure($"The plan at '{path}' declares output '{step.Output}' twice.");
            }

            if (!artifactNames.Contains(step.Artifact))
            {
                throw new CaptureFailure(
                    $"Step '{step.Output}' writes artifact '{step.Artifact}', which the plan at '{path}' "
                    + "does not declare.");
            }

            if (step.Calls.Count == 0 && step.Record is null)
            {
                throw new CaptureFailure(
                    $"Step '{step.Output}' issues no call and records no declared fact, so it observes "
                    + "nothing. A step with no calls is a CONTRACT-DATA step and must carry a 'record' "
                    + "member holding the facts it pins.");
            }

            HashSet<string> callNames = new(StringComparer.Ordinal);

            foreach (PlannedCall call in step.Calls)
            {
                if (string.IsNullOrWhiteSpace(call.Name))
                {
                    throw new CaptureFailure($"Step '{step.Output}' carries a call with no name.");
                }

                if (!callNames.Add(call.Name))
                {
                    throw new CaptureFailure(
                        $"Step '{step.Output}' declares call '{call.Name}' twice. Call names are the "
                        + "reference keys within a step, so a duplicate would make a reference ambiguous.");
                }

                if (string.IsNullOrWhiteSpace(call.Method) || string.IsNullOrWhiteSpace(call.Path))
                {
                    throw new CaptureFailure(
                        $"Call '{call.Name}' of step '{step.Output}' names no method or no path.");
                }
            }

            foreach (PlannedCall operation in step.Teardown)
            {
                if (string.IsNullOrWhiteSpace(operation.Method) || string.IsNullOrWhiteSpace(operation.Path))
                {
                    throw new CaptureFailure(
                        $"Step '{step.Output}' carries a teardown operation with no request to issue.");
                }

                if (operation.Capture.Count > 0
                    || operation.CaptureShape.Count > 0
                    || operation.CaptureOpaque.Count > 0)
                {
                    throw new CaptureFailure(
                        $"Teardown operation '{operation.Name}' of step '{step.Output}' captures something. "
                        + "Teardown is recorded nowhere, so a capture on it would be discarded silently - "
                        + "which reads as a recorded fact that never reached the recording.");
                }
            }

            foreach (PlannedDerivation derivation in step.Derive)
            {
                if (string.IsNullOrWhiteSpace(derivation.Name))
                {
                    throw new CaptureFailure($"Step '{step.Output}' carries a derivation with no name.");
                }

                if (callNames.Contains(derivation.Name))
                {
                    throw new CaptureFailure(
                        $"Step '{step.Output}' names derivation '{derivation.Name}' the same as one of its "
                        + "calls. The two share a namespace inside the recording, so the collision would "
                        + "silently overwrite one with the other.");
                }

                if (!DerivationKinds.Contains(derivation.Kind))
                {
                    throw new CaptureFailure(
                        $"Derivation '{derivation.Name}' of step '{step.Output}' asks for kind "
                        + $"'{derivation.Kind}', which this driver does not implement. The implemented set "
                        + $"is exactly: {string.Join(", ", DerivationKinds.Order(StringComparer.Ordinal))}. "
                        + "The set is CLOSED on purpose - an open-ended expression language in a plan "
                        + "would make the plan code, and a plan is reviewed as data.");
                }

                if (string.IsNullOrWhiteSpace(derivation.Value))
                {
                    throw new CaptureFailure(
                        $"Derivation '{derivation.Name}' of step '{step.Output}' names no value to derive from.");
                }
            }
        }

        // Every declared artifact must actually be written by at least one step, so a plan cannot promise
        // a file that never appears - a reviewer comparing the store against the plan would read the gap
        // as a failed capture.
        foreach (PlannedArtifact artifact in Artifacts)
        {
            if (!Steps.Any(step => string.Equals(step.Artifact, artifact.Name, StringComparison.Ordinal)))
            {
                throw new CaptureFailure(
                    $"The plan at '{path}' declares artifact '{artifact.Name}' that no step writes.");
            }
        }

    }

    /// <summary>The closed set of derivations this driver implements.</summary>
    /// <remarks>
    /// WHY A CLOSED SET RATHER THAN AN EXPRESSION LANGUAGE. Several declared outputs are not values a
    /// response carries - a round-trip verdict, the block repetition an electronic-codebook cipher leaks,
    /// the character classes a masked random string still has to exhibit. Each is a small deterministic
    /// function of values the run already holds. Implementing them as a fixed, named set keeps every
    /// plan reviewable as data and keeps the functions themselves unit-testable; an expression language
    /// would move the behaviour into the plan, where no test reaches it.
    /// </remarks>
    internal static IReadOnlySet<string> DerivationKinds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "equals",
        "hexOfBase64",
        "byteLengthOfBase64",
        "characterLength",
        "hexBlockRepetition",
        "characterClasses",
        "guidFormatting",
    };

    /// <summary>
    /// The derivations that expose their input, so they are refused over an OPAQUE value.
    /// </summary>
    /// <remarks>
    /// 🔴 THE POINT OF THE DISTINCTION. An opaque capture is chainable but never recorded - a generated
    /// keyRef, a per-run signature. Rendering one as hexadecimal would put it into the artifact by
    /// another route and defeat the screen, which only rewrites the value's exact spelling. The
    /// remaining derivations answer with a boolean, a length, a set of character classes or a formatting
    /// descriptor: summaries no one can invert, which is exactly what the workflow definition asks be
    /// compared for its masked seams.
    /// </remarks>
    internal static IReadOnlySet<string> ValueRevealingDerivations { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "hexOfBase64", "hexBlockRepetition" };
}

/// <summary>One recording file a plan produces.</summary>
internal sealed class PlannedArtifact
{
    /// <summary>The file name, which must be one the store admits.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>What the file groups, carried into the artifact itself.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

/// <summary>One declared output, and every call and derivation that observes it.</summary>
/// <remarks>
/// ONE STEP IS EXACTLY ONE DECLARED OUTPUT. That is what lets a guard test assert the plan's step names
/// are precisely the workflow definition's observedOutputs. Several outputs are genuinely composed of
/// more than one call - a round trip is an encrypt and a decrypt, a verification is a valid case and an
/// altered one, an identifier's formatting is four flag combinations - so a step carries a LIST of
/// calls rather than one, and the list collapses into the single observation the definition declared.
/// </remarks>
internal sealed class PlannedStep
{
    /// <summary>The declared output name, verbatim from the workflow definition.</summary>
    [JsonPropertyName("output")]
    public string Output { get; init; } = string.Empty;

    /// <summary>Which artifact this output is written into.</summary>
    [JsonPropertyName("artifact")]
    public string Artifact { get; init; } = string.Empty;

    /// <summary>One line saying what is being observed, carried into the recording.</summary>
    [JsonPropertyName("observes")]
    public string Observes { get; init; } = string.Empty;

    /// <summary>The calls, in order. Empty makes this a contract-data step.</summary>
    [JsonPropertyName("calls")]
    public IReadOnlyList<PlannedCall> Calls { get; init; } = [];

    /// <summary>Deterministic functions of the captured values, computed after the calls.</summary>
    [JsonPropertyName("derive")]
    public IReadOnlyList<PlannedDerivation> Derive { get; init; } = [];

    /// <summary>
    /// Facts recorded verbatim: identifier sets, and the identifiers in force for a call.
    /// </summary>
    /// <remarks>
    /// Several declared outputs are properties of the PUBLISHED CONTRACT rather than of any call - the
    /// numeric algorithm, cipher, mode, padding and encoding values in force. Recording them costs no
    /// request and pins the preserved legacy spellings and values, which is where a silent renaming or
    /// renumbering would be caught.
    /// </remarks>
    [JsonPropertyName("record")]
    public JsonElement? Record { get; init; }

    /// <summary>
    /// Operations that run after this step's calls and derivations, and are RECORDED NOWHERE.
    /// </summary>
    /// <remarks>
    /// 🔴 TEARDOWN IS PER STEP, NOT PER PLAN, AND THAT IS THE WHOLE POINT. A step that generates an
    /// asymmetric key registers state in the service, and a capture that walked away from it would leave
    /// a key registered for every run ever taken. Scoping teardown to the step is what lets it name the
    /// thing it is releasing - the step's own chain is still in scope, so
    /// <c>${call:generate:keyRef}</c> resolves - where a plan-level teardown could only release
    /// something named on the command line, which is to say something the run did not create.
    /// Teardown carries no output name: it observes nothing, so recording it would put an undeclared
    /// fact into a recording. A teardown failure is still a failure, because the residue is exactly what
    /// this exists to prevent.
    /// </remarks>
    [JsonPropertyName("teardown")]
    public IReadOnlyList<PlannedCall> Teardown { get; init; } = [];

    /// <summary>An optional note carried into the recording beside the observation.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

/// <summary>One HTTP call within a step.</summary>
internal sealed class PlannedCall
{
    /// <summary>The call's name, which is its reference key inside the step.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>The HTTP method.</summary>
    [JsonPropertyName("method")]
    public string? Method { get; init; }

    /// <summary>The path, relative to the base address.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>The request body, with placeholders resolved before it is sent.</summary>
    [JsonPropertyName("body")]
    public JsonElement? Body { get; init; }

    /// <summary>
    /// The status this call must answer with.
    /// </summary>
    /// <remarks>
    /// 🔴 DECLARED, NOT OBSERVED. A REFUSAL IS A FIRST-CLASS OUTPUT on this surface - three of the
    /// asymmetric outputs ARE refusals - so the plan states the status it expects and any divergence
    /// stops the run. Recording whatever came back would turn a service that started refusing
    /// everything into a successful capture of a refusal.
    /// </remarks>
    [JsonPropertyName("expectStatus")]
    public int ExpectStatus { get; init; } = 200;

    /// <summary>Members captured verbatim into the recording. Also chainable.</summary>
    [JsonPropertyName("capture")]
    public IReadOnlyList<string> Capture { get; init; } = [];

    /// <summary>Members reduced to their shape. NOT chainable, on purpose.</summary>
    /// <remarks>
    /// This is the setting a member carrying or possibly carrying key material gets. Shape means kind
    /// and size and nothing else, and the deliberate absence of chaining stops a plan threading such a
    /// member into a later request where it would be echoed.
    /// </remarks>
    [JsonPropertyName("captureShape")]
    public IReadOnlyList<string> CaptureShape { get; init; } = [];

    /// <summary>
    /// Members captured for CHAINING ONLY: registered with the screen, recorded as shape.
    /// </summary>
    /// <remarks>
    /// The third mode, and the one the generated key reference needs. A keyRef is credential-equivalent
    /// (docs/CONTRACTS.md section 5.2) yet a later call cannot be made without it, so the value is held
    /// in memory for chaining, registered with the screen so any accidental appearance elsewhere is
    /// rewritten and then PROVED absent, and only its shape reaches the artifact.
    /// </remarks>
    [JsonPropertyName("captureOpaque")]
    public IReadOnlyList<string> CaptureOpaque { get; init; } = [];

    /// <summary>An optional note carried into the recording beside this call.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

/// <summary>One deterministic function of values the run already holds.</summary>
internal sealed class PlannedDerivation
{
    /// <summary>The derivation's name inside the step's observation.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Which derivation to apply. One of <see cref="CapturePlan.DerivationKinds"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    /// <summary>The input, normally a reference placeholder.</summary>
    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// Additional inputs, for the derivation that unions an observation across several samples.
    /// </summary>
    /// <remarks>
    /// 🔴 WHY A UNION IS NEEDED AT ALL. "Which character classes does this generator draw from" is a
    /// property of the generator; "which classes does this one string exhibit" is a property of the
    /// sample. They are not the same, and taking the second for the first was measured to be wrong here:
    /// two runs of a 24-character sample under the same flags disagreed on whether a digit was present.
    /// A class the alphabet does not contain can never be observed, so unioning across samples only ever
    /// adds classes that are genuinely there - which makes the union a sound estimate of the alphabet and
    /// a single sample an unsound one.
    /// </remarks>
    [JsonPropertyName("values")]
    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>The second input, for the comparing derivation.</summary>
    [JsonPropertyName("other")]
    public string? Other { get; init; }

    /// <summary>The cipher block size in bytes, for the block-repetition derivation.</summary>
    [JsonPropertyName("blockBytes")]
    public int BlockBytes { get; init; }

    /// <summary>What this derivation establishes, carried into the recording.</summary>
    [JsonPropertyName("observes")]
    public string? Observes { get; init; }
}
