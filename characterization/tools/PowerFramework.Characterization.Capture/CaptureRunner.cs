// ==================================================================================================
//  CaptureRunner - resolves the plan's placeholders, drives the service, and builds the recording
//  ------------------------------------------------------------------------------------------------
//  THREE PROPERTIES THIS FILE EXISTS TO GUARANTEE, none of which a hand-run curl sequence can:
//
//    1. A DIVERGENCE FAILS THE RUN. Every step declares the status it must receive, so a captured
//       rejection is distinguishable from a broken service. The first divergence ends the run and
//       nothing is written - a partial recording is indistinguishable from a capture of a broken
//       system, and it is the one artifact a golden-master store must never hold.
//    2. THE RECORDING IS DETERMINISTIC BY CONSTRUCTION. Member order is the plan's order, numbers and
//       strings are written as they arrived, no clock is read, no duration is recorded and no
//       identifier the run happens to generate is carried. characterization/README.md section 6.4
//       forbids a performance measurement in this store and AAP 0.8.5 forbids asserting one anywhere.
//    3. NO SECRET REACHES THE OUTPUT. Sensitive values are replaced with the placeholder names they
//       came from and then their absence is PROVED before a byte is written.
// ==================================================================================================

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerFramework.Characterization.Capture;

/// <summary>Runs one capture plan against one running service.</summary>
internal sealed class CaptureRunner
{
    /// <summary>
    /// The three named references that also carry a dedicated placeholder spelling and option.
    /// </summary>
    /// <remarks>
    /// Reference name to the placeholder token that resolves it. They exist as well as the general
    /// <c>${ref:&lt;name&gt;}</c> form because these three are the ones every keyed plan needs, and a plan
    /// reads better saying <c>${keyRef}</c> than <c>${ref:key}</c>. One table so the option, the
    /// placeholder and the reference name cannot drift apart.
    /// </remarks>
    private static readonly Dictionary<string, string> WellKnownReferences = new(StringComparer.Ordinal)
    {
        ["${keyRef}"] = "key",
        ["${ivRef}"] = "iv",
        ["${fileRef}"] = "file",
    };

    /// <summary>The placeholder a bearer credential is recorded as, if one ever reached a value.</summary>
    internal const string TokenPlaceholder = "${bearerToken}";

    /// <summary>The plan under execution.</summary>
    private readonly CapturePlan _plan;

    /// <summary>The parsed command line.</summary>
    private readonly CaptureOptions _options;

    /// <summary>The screen every captured value passes through.</summary>
    private readonly SecretScreen _screen;

    /// <summary>
    /// The chainable values of the step currently executing, keyed by call name then member.
    /// </summary>
    /// <remarks>
    /// SEPARATE FROM THE RECORDED OBSERVATION, AND CLEARED BETWEEN STEPS. Separate because an opaque
    /// value is chainable and unrecorded, so it cannot live in the object that becomes the artifact -
    /// keeping the two tables apart is what makes "chainable" and "recorded" independent rather than
    /// accidentally identical. Cleared because one recorded output must not depend on another's side
    /// effects: a plan whose steps could reach into each other would produce a recording whose meaning
    /// changed if a step were reordered or removed.
    /// </remarks>
    private readonly Dictionary<string, Dictionary<string, string>> _calls = new(StringComparer.Ordinal);

    /// <summary>Which call-and-member pairs of the current step were captured opaquely.</summary>
    private readonly HashSet<string> _opaqueCalls = new(StringComparer.Ordinal);

    /// <summary>Creates a runner.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="options">The command line.</param>
    /// <param name="screen">The secret screen, already carrying the credentials in play.</param>
    internal CaptureRunner(CapturePlan plan, CaptureOptions options, SecretScreen screen)
    {
        _plan = plan;
        _options = options;
        _screen = screen;
    }

    /// <summary>Builds the HTTP client the run uses.</summary>
    /// <param name="options">The command line.</param>
    /// <param name="bearerToken">The credential to present, if any.</param>
    /// <returns>The client, which the caller disposes.</returns>
    /// <remarks>
    /// THE AUTHORITY IS PINNED WHEN ONE IS SUPPLIED AND VERIFICATION IS NEVER DISABLED. The estate's
    /// internal listeners present certificates from a locally generated authority that no container trust
    /// store carries, so a capture against them needs that authority - and the alternative a hurried run
    /// reaches for is switching verification off, which would let a capture be taken against anything
    /// answering on the port.
    /// </remarks>
    internal static HttpClient CreateClient(CaptureOptions options, string? bearerToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        HttpClientHandler handler = new();

        if (options.CertificateAuthorityPath is { } authorityPath)
        {
            X509Certificate2 authority = X509Certificate2.CreateFromPem(File.ReadAllText(authorityPath));

            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null)
                {
                    return false;
                }

                using X509Chain chain = new();

                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.CustomTrustStore.Add(authority);

                return chain.Build(certificate);
            };
        }

        HttpClient client = new(handler, disposeHandler: true)
        {
            BaseAddress = options.BaseAddress,
        };

        if (!string.IsNullOrEmpty(bearerToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return client;
    }

    /// <summary>
    /// Mints a token at the issuance edge, reading the credential from its file rather than an argument.
    /// </summary>
    /// <param name="options">The command line.</param>
    /// <param name="cancellationToken">The abort signal.</param>
    /// <returns>The token.</returns>
    /// <exception cref="CaptureFailure">Issuance refused, or answered no token.</exception>
    internal static async Task<string> MintTokenAsync(
        CaptureOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        CaptureOptions issuance = options;

        using HttpClient client = CreateClient(options, bearerToken: null);

        client.BaseAddress = issuance.IssuerAddress;

        string secret = File.ReadAllText(options.ClientSecretPath!).Trim();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{options.ClientId}:{secret}")));

        using HttpResponseMessage response = await client
            .PostAsJsonAsync(
                "/v1/tokens",
                new
                {
                    subject = options.ClientId,
                    audience = options.Audience,
                    scopes = options.Scopes,
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new CaptureFailure(
                $"Issuance refused the capture identity with {(int)response.StatusCode}. No capture was "
                + "taken. The credential is deliberately not echoed.");
        }

        JsonNode? body = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        return body?["access_token"]?.GetValue<string>()
            ?? throw new CaptureFailure("Issuance answered 200 with no access_token member.");
    }
    /// <summary>Drives every step of the plan, then its teardown.</summary>
    /// <param name="client">The client, already carrying the credential.</param>
    /// <param name="cancellationToken">The abort signal.</param>
    /// <returns>Artifact name to its recording content.</returns>
    /// <exception cref="CaptureFailure">A call diverged from its declared expectation.</exception>
    internal async Task<IReadOnlyDictionary<string, JsonObject>> RunAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        Dictionary<string, JsonObject> artifacts = new(StringComparer.Ordinal);

        foreach (PlannedArtifact artifact in _plan.Artifacts)
        {
            artifacts[artifact.Name] = new JsonObject
            {
                ["workflowId"] = _plan.WorkflowId,
                ["artifact"] = artifact.Name,
                ["description"] = artifact.Description,
                ["newlineConvention"] = _plan.NewlineConvention,
                ["outputs"] = new JsonObject(),
            };
        }

        foreach (PlannedStep step in _plan.Steps)
        {
            JsonObject observation = await ExecuteStepAsync(step, client, cancellationToken)
                .ConfigureAwait(false);

            ((JsonObject)artifacts[step.Artifact]["outputs"]!)[step.Output] = observation;
        }

        return artifacts;
    }

    /// <summary>Executes one declared output: its calls, then its derivations.</summary>
    /// <param name="step">The step.</param>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">The abort signal.</param>
    /// <returns>The observation to record.</returns>
    /// <exception cref="CaptureFailure">A call diverged, or a derivation is inadmissible.</exception>
    private async Task<JsonObject> ExecuteStepAsync(
        PlannedStep step,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        _calls.Clear();
        _opaqueCalls.Clear();

        JsonObject observation = new()
        {
            ["observes"] = step.Observes,
        };

        if (step.Notes is { } notes)
        {
            observation["notes"] = notes;
        }

        if (step.Record is { } record)
        {
            // A CONTRACT-DATA FACT, recorded verbatim. Screened all the same: a plan is reviewed, but a
            // review is not a proof, and the screen is the proof.
            JsonNode? declared = JsonNode.Parse(record.GetRawText());

            _screen.Screen(declared);

            observation["declared"] = declared;
        }

        if (step.Calls.Count > 0)
        {
            JsonObject calls = [];

            foreach (PlannedCall call in step.Calls)
            {
                calls[call.Name] = await ExecuteCallAsync(call, step, client, cancellationToken)
                    .ConfigureAwait(false);
            }

            observation["calls"] = calls;
        }

        if (step.Derive.Count > 0)
        {
            JsonObject derived = [];

            foreach (PlannedDerivation derivation in step.Derive)
            {
                derived[derivation.Name] = Derive(derivation, step);
            }

            _screen.Screen(derived);

            observation["derived"] = derived;
        }

        // TEARDOWN RUNS LAST, WITH THE STEP'S CHAIN STILL IN SCOPE, AND IS RECORDED NOWHERE. It releases
        // whatever server-side state this step registered, so a run leaves the deployment as it found it.
        // A teardown failure is still a failure: leaving a generated key registered is exactly the residue
        // this exists to prevent.
        foreach (PlannedCall operation in step.Teardown)
        {
            _ = await SendAsync(operation, step.Output + " teardown", client, cancellationToken)
                .ConfigureAwait(false);
        }

        return observation;
    }

    /// <summary>Issues one call and captures from its response.</summary>
    /// <param name="call">The call.</param>
    /// <param name="step">The owning step, for the failure messages.</param>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">The abort signal.</param>
    /// <returns>What the call observed.</returns>
    /// <exception cref="CaptureFailure">The call diverged from its declared status.</exception>
    private async Task<JsonObject> ExecuteCallAsync(
        PlannedCall call,
        PlannedStep step,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        JsonNode? responseBody = await SendAsync(call, step.Output, client, cancellationToken)
            .ConfigureAwait(false);

        JsonObject observed = new()
        {
            ["status"] = call.ExpectStatus,
        };

        if (call.Notes is { } notes)
        {
            observed["notes"] = notes;
        }

        JsonObject captured = [];

        foreach (string member in call.Capture)
        {
            JsonNode? value = Member(responseBody, member, call, step);

            captured[member] = value?.DeepClone();

            if (value?.GetValueKind() == JsonValueKind.String)
            {
                Chain(call.Name)[member] = value.GetValue<string>();
            }
        }

        foreach (string member in call.CaptureShape)
        {
            // NOT CHAINED, deliberately. See PlannedCall.CaptureShape.
            captured[member] = Shape(Member(responseBody, member, call, step));
        }

        foreach (string member in call.CaptureOpaque)
        {
            JsonNode? value = Member(responseBody, member, call, step);

            if (value?.GetValueKind() != JsonValueKind.String)
            {
                throw new CaptureFailure(
                    $"Call '{call.Name}' of step '{step.Output}' captures '{member}' opaquely, but the "
                    + "response member is not a string. Only a string can be chained into a later request.");
            }

            string opaque = value.GetValue<string>();

            Chain(call.Name)[member] = opaque;
            _opaqueCalls.Add(call.Name + "\u0000" + member);

            // REGISTERED BEFORE IT CAN REACH ANYTHING. From here the screen rewrites this value out of
            // every artifact and then proves its absence, so a plan that later captured the same value
            // verbatim by mistake fails the run instead of publishing it.
            _screen.Register(opaque, "${opaque:" + call.Name + ":" + member + "}");

            captured[member] = Shape(value);
        }

        _screen.Screen(captured);

        observed["captured"] = captured;

        return observed;
    }

    /// <summary>Sends one request, enforcing the declared status.</summary>
    /// <param name="call">The call.</param>
    /// <param name="context">The step name, or "teardown", for the message.</param>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">The abort signal.</param>
    /// <returns>The parsed response body, or null when the response carried none.</returns>
    /// <exception cref="CaptureFailure">The response status diverged from the declared one.</exception>
    private async Task<JsonNode?> SendAsync(
        PlannedCall call,
        string context,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        JsonNode? requestBody = call.Body is { } body
            ? Resolve(JsonNode.Parse(body.GetRawText()), call, context)
            : null;

        using HttpRequestMessage request = new(new HttpMethod(call.Method!), call.Path!);

        if (requestBody is not null)
        {
            request.Content = new StringContent(
                requestBody.ToJsonString(CapturePlan.SerializerOptions),
                System.Text.Encoding.UTF8,
                "application/json");
        }

        using HttpResponseMessage response = await client
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        string payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if ((int)response.StatusCode != call.ExpectStatus)
        {
            throw new CaptureFailure(
                $"Call '{call.Name}' of '{context}' expected HTTP {call.ExpectStatus} from {call.Method} "
                + $"{call.Path} and received {(int)response.StatusCode}. The run stops here and NOTHING is "
                + "written: a partial recording cannot be told apart from a capture of a broken service. "
                + $"Response body: {_screen.Screen(Truncate(payload))}");
        }

        return string.IsNullOrWhiteSpace(payload) ? null : JsonNode.Parse(payload);
    }

    /// <summary>Resolves a derivation's inputs and applies it.</summary>
    /// <param name="derivation">The derivation.</param>
    /// <param name="step">The owning step, for the messages.</param>
    /// <returns>The derived observation.</returns>
    /// <exception cref="CaptureFailure">The derivation is inadmissible or its input is unusable.</exception>
    /// <remarks>
    /// THE RUNNER RESOLVES; <see cref="Derivations"/> COMPUTES. Splitting them is what makes the seven
    /// derivations unit-testable without a service, a plan or a seam of any kind: the computation takes
    /// resolved strings and answers a JSON object, and everything needing the run's state stays here.
    /// </remarks>
    private JsonObject Derive(PlannedDerivation derivation, PlannedStep step)
    {
        RefuseIfItWouldExposeAnOpaqueValue(derivation, step);

        List<string> inputs = [ResolveText(derivation.Value, null, step.Output)];

        inputs.AddRange(derivation.Values.Select(reference => ResolveText(reference, null, step.Output)));

        string? other = derivation.Other is null
            ? null
            : ResolveText(derivation.Other, null, step.Output);

        return Derivations.Apply(derivation, inputs, other, step.Output);
    }

    /// <summary>Refuses a value-revealing derivation over a value captured opaquely.</summary>
    /// <param name="derivation">The derivation.</param>
    /// <param name="step">The owning step, for the message.</param>
    /// <exception cref="CaptureFailure">The derivation would expose an opaque value.</exception>
    private void RefuseIfItWouldExposeAnOpaqueValue(PlannedDerivation derivation, PlannedStep step)
    {
        if (CapturePlan.ValueRevealingDerivations.Contains(derivation.Kind)
            && (ReferencesAnOpaqueValue(derivation.Value)
                || derivation.Values.Any(ReferencesAnOpaqueValue)))
        {
            throw new CaptureFailure(
                $"Derivation '{derivation.Name}' of step '{step.Output}' would render an OPAQUE value - one "
                + "captured for chaining and deliberately never recorded - into the recording. That "
                + "defeats the screen, which rewrites a value's exact spelling and cannot recognise the "
                + "same bytes in another encoding. Use a summarising derivation, or capture the member "
                + "verbatim if it is genuinely recordable.");
        }
    }

    /// <summary>Says whether a reference names a member of the current step captured opaquely.</summary>
    /// <param name="reference">The reference text.</param>
    /// <returns>True when it does.</returns>
    private bool ReferencesAnOpaqueValue(string reference)
    {
        if (!reference.StartsWith("${call:", StringComparison.Ordinal) || !reference.EndsWith('}'))
        {
            return false;
        }

        string[] parts = reference[2..^1].Split(':');

        return parts.Length == 3 && _opaqueCalls.Contains(parts[1] + "\u0000" + parts[2]);
    }

    /// <summary>
    /// Seeds the chain table, so reference resolution can be exercised without a running service.
    /// </summary>
    /// <param name="callName">The call whose value this is.</param>
    /// <param name="member">The member name.</param>
    /// <param name="value">The value.</param>
    /// <param name="opaque">Whether it was captured opaquely.</param>
    /// <remarks>
    /// A TEST SEAM, AND A NARROW ONE. Reference resolution is the part of this file a plan author is most
    /// likely to trip - a forward reference, a shape-captured member, a malformed token - and every one of
    /// those paths ends in a refusal whose wording is the whole help a plan author gets. Exercising them
    /// through HTTP would need a service, a plan and a credential per case; this admits the state directly
    /// and nothing else. AAP 0.6.7 is what licenses a seam of this kind.
    /// </remarks>
    internal void SeedChainForTesting(string callName, string member, string value, bool opaque)
    {
        Chain(callName)[member] = value;

        if (opaque)
        {
            _opaqueCalls.Add(callName + "\u0000" + member);
        }
    }

    /// <summary>Applies the opaque-exposure refusal, for a test that needs no service.</summary>
    /// <param name="derivation">The derivation.</param>
    /// <param name="step">The owning step.</param>
    internal void CheckDerivationAdmissibility(PlannedDerivation derivation, PlannedStep step) =>
        RefuseIfItWouldExposeAnOpaqueValue(derivation, step);

    /// <summary>The chain table for one call, created on first use.</summary>
    /// <param name="callName">The call's name.</param>
    /// <returns>The call's chainable values.</returns>
    private Dictionary<string, string> Chain(string callName)
    {
        if (!_calls.TryGetValue(callName, out Dictionary<string, string>? values))
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            _calls[callName] = values;
        }

        return values;
    }

    /// <summary>Reduces a value to its shape, for members that carry or may carry key material.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The shape.</returns>
    private static JsonObject Shape(JsonNode? value) => value?.GetValueKind() switch
    {
        JsonValueKind.String => new JsonObject
        {
            ["kind"] = "string",
            ["characters"] = value!.GetValue<string>().Length,
        },
        JsonValueKind.Number => new JsonObject
        {
            ["kind"] = "number",
            ["value"] = value!.DeepClone(),
        },
        JsonValueKind.True or JsonValueKind.False => new JsonObject
        {
            ["kind"] = "boolean",
            ["value"] = value!.DeepClone(),
        },
        null or JsonValueKind.Null => new JsonObject { ["kind"] = "absent" },
        _ => new JsonObject { ["kind"] = "structured" },
    };

    /// <summary>Reads a member the plan says the response carries.</summary>
    /// <param name="body">The response body.</param>
    /// <param name="member">The member name.</param>
    /// <param name="call">The call, for the message.</param>
    /// <param name="step">The owning step, for the message.</param>
    /// <returns>The member's value.</returns>
    /// <exception cref="CaptureFailure">The body is not an object, or carries no such member.</exception>
    private static JsonNode? Member(JsonNode? body, string member, PlannedCall call, PlannedStep step)
    {
        if (body is not JsonObject members)
        {
            throw new CaptureFailure(
                $"Call '{call.Name}' of step '{step.Output}' captures member '{member}', but the response "
                + "body is not a JSON object.");
        }

        return members.TryGetPropertyValue(member, out JsonNode? value)
            ? value
            : throw new CaptureFailure(
                $"Call '{call.Name}' of step '{step.Output}' captures member '{member}', which the response "
                + "does not carry. The published contract and the plan disagree - reconcile the plan with "
                + "shared/PowerFramework.Contracts/OpenApi rather than dropping the member.");
    }

    /// <summary>Substitutes every placeholder in a request body.</summary>
    /// <param name="node">The body.</param>
    /// <param name="call">The call, for the failure message.</param>
    /// <param name="context">The step name, for the failure message.</param>
    /// <returns>The resolved body.</returns>
    private JsonNode? Resolve(JsonNode? node, PlannedCall? call, string context)
    {
        switch (node)
        {
            case JsonObject members:
                foreach (string name in members.Select(static member => member.Key).ToArray())
                {
                    members[name] = Resolve(members[name], call, context);
                }

                return members;

            case JsonArray items:
                for (int index = 0; index < items.Count; index++)
                {
                    items[index] = Resolve(items[index], call, context);
                }

                return items;

            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                return JsonValue.Create(ResolveText(value.GetValue<string>(), call, context));

            default:
                return node;
        }
    }

    /// <summary>
    /// Resolves one placeholder, or returns the text unchanged when it is not one.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="call">The call, or null when resolving a derivation input.</param>
    /// <param name="context">The step name, for the failure message.</param>
    /// <returns>The resolved text.</returns>
    /// <exception cref="CaptureFailure">The placeholder is unknown, malformed or unresolvable.</exception>
    /// <remarks>
    /// THE PLACEHOLDER SET IS CLOSED AND EVERY MEMBER IS PURE. ${bytes} and ${base64} exist so a plan can
    /// state a 118-byte payload as its intent rather than as an opaque base64 blob a reviewer cannot read;
    /// ${tamperLastByte} exists because altering one byte of a ciphertext is the whole method of the
    /// tampering case, and doing it in the plan would mean writing the tampered value as a literal - which
    /// would only be correct for one particular ciphertext.
    /// </remarks>
    internal string ResolveText(string text, PlannedCall? call, string context)
    {
        if (!text.StartsWith("${", StringComparison.Ordinal) || !text.EndsWith('}'))
        {
            return text;
        }

        string token = text[2..^1];

        if (WellKnownReferences.TryGetValue(text, out string? wellKnown))
        {
            return Required(_options.Reference(wellKnown), $"--{wellKnown}-ref", context);
        }

        string[] parts = token.Split(':');

        if (parts.Length == 2 && string.Equals(parts[0], "ref", StringComparison.Ordinal))
        {
            return Required(_options.Reference(parts[1]), $"--ref {parts[1]}=<reference>", context);
        }

        if (parts.Length == 3 && string.Equals(parts[0], "bytes", StringComparison.Ordinal))
        {
            // A repeated single byte, as base64. The plan states the length it means to offer, which is
            // what the two asymmetric size-limit outputs are actually about.
            if (parts[1].Length != 1 || !int.TryParse(parts[2], out int count) || count <= 0)
            {
                throw new CaptureFailure(
                    $"'{context}' carries malformed '{text}'. The shape is ${{bytes:<oneCharacter>:<count>}}.");
            }

            return Convert.ToBase64String(Enumerable.Repeat((byte)parts[1][0], count).ToArray());
        }

        if (parts.Length == 2 && string.Equals(parts[0], "base64", StringComparison.Ordinal))
        {
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(parts[1]));
        }

        if (parts.Length != 3
            || (!string.Equals(parts[0], "call", StringComparison.Ordinal)
                && !string.Equals(parts[0], "tamperLastByte", StringComparison.Ordinal)))
        {
            throw new CaptureFailure(
                $"'{context}' carries unknown or malformed placeholder '{text}'. The set is exactly "
                + "${keyRef}, ${ivRef}, ${fileRef}, ${ref:<name>}, ${bytes:<char>:<count>}, "
                + "${base64:<text>}, ${call:<callName>:<member>} and "
                + "${tamperLastByte:<callName>:<member>}.");
        }

        string chained = Chained(parts[1], parts[2], context);

        if (string.Equals(parts[0], "call", StringComparison.Ordinal))
        {
            return chained;
        }

        // ONE BYTE ALTERED, DETERMINISTICALLY. The low bit of the final byte is flipped: enough to
        // invalidate the block, reproducible across runs, and never a value the plan had to hardcode.
        byte[] bytes = Convert.FromBase64String(chained);

        if (bytes.Length == 0)
        {
            throw new CaptureFailure($"'{context}' cannot alter a byte of an empty value.");
        }

        bytes[^1] ^= 0x01;

        return Convert.ToBase64String(bytes);
    }

    /// <summary>Reads a value an earlier call in the same step made chainable.</summary>
    /// <param name="callName">The earlier call.</param>
    /// <param name="member">The member.</param>
    /// <param name="context">The step name, for the message.</param>
    /// <returns>The chained value.</returns>
    /// <exception cref="CaptureFailure">The call has not run, or made nothing chainable.</exception>
    private string Chained(string callName, string member, string context)
    {
        if (!_calls.TryGetValue(callName, out Dictionary<string, string>? earlier))
        {
            throw new CaptureFailure(
                $"'{context}' references call '{callName}', which has not run or captured nothing "
                + "chainable. A step's calls execute in declaration order, so a referenced call must be "
                + "declared before it - and a reference across steps is deliberately not supported, "
                + "because it would make one recorded output depend on another's side effects.");
        }

        return earlier.TryGetValue(member, out string? chained)
            ? chained
            : throw new CaptureFailure(
                $"'{context}' references '{callName}.{member}', which that call did not make chainable. A "
                + "member captured as a SHAPE deliberately cannot be chained - which is what stops key "
                + "material being threaded through a plan by accident - so declare it under capture or "
                + "captureOpaque instead.");
    }

    /// <summary>Demands a command-line value the plan needs.</summary>
    /// <param name="value">The value, possibly absent.</param>
    /// <param name="option">The option that supplies it.</param>
    /// <param name="context">The step name, for the message.</param>
    /// <returns>The value.</returns>
    /// <exception cref="CaptureFailure">The value is absent.</exception>
    private static string Required(string? value, string option, string context) =>
        string.IsNullOrEmpty(value)
            ? throw new CaptureFailure(
                $"'{context}' needs {option}, which was not supplied. The plan names key material only by "
                + "opaque reference, so the reference has to come from the command line - and it is a "
                + "REFERENCE, never material.")
            : value;

    /// <summary>Shortens a response body for a failure message.</summary>
    /// <param name="payload">The body.</param>
    /// <returns>The shortened body.</returns>
    private static string Truncate(string payload) =>
        payload.Length <= 600 ? payload : payload[..600] + "... [truncated]";
}
