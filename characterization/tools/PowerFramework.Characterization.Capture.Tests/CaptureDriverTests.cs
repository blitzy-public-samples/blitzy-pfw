// ==================================================================================================
//  CaptureDriverTests - the driver's own guards, exercised without a service
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS ARE FOR. A capture that succeeds proves the happy path. Everything that makes the
//  resulting recording TRUSTWORTHY is a refusal, and a refusal is only ever exercised deliberately:
//
//    - the plan is refused before the first request when it is incoherent, so no fault can leave a
//      partial recording on disk;
//    - a member captured as a SHAPE cannot be chained, which is what stops key material being threaded
//      through a plan into a later request that echoes it;
//    - a value-revealing derivation is refused over an OPAQUE value, because a hexadecimal rendering
//      would put that value into the artifact by a route the screen cannot see;
//    - an unpaired capture is refused inside the store, which is the store's pairing rule as code;
//    - a credential is never an argument value, only ever a file path.
//
//  Every row here runs offline. Nothing starts a service, nothing mints a token and nothing writes into
//  the store.
// ==================================================================================================

using System.Text.Json;
using System.Text.Json.Nodes;

using PowerFramework.Characterization.Capture;

using Xunit;

namespace PowerFramework.Characterization.Capture.Tests;

/// <summary>The plan loader's coherence guards.</summary>
public sealed class CapturePlanValidationTests
{
    /// <summary>
    /// An incoherent plan is refused, and refused before any request could have been issued.
    /// </summary>
    /// <param name="label">What the row is about, so a failure names it.</param>
    /// <param name="plan">The plan document.</param>
    /// <param name="expectedFragment">A phrase the refusal must carry.</param>
    /// <remarks>
    /// THE MESSAGE IS ASSERTED, NOT ONLY THE THROW. The refusal text is the whole help a plan author
    /// gets, and a plan that failed with a bare exception type would send them to the driver's source.
    /// </remarks>
    [Theory]
    [MemberData(nameof(IncoherentPlans))]
    public void AnIncoherentPlanIsRefusedByTheLoader(string label, string plan, string expectedFragment)
    {
        Assert.False(string.IsNullOrWhiteSpace(label));

        string path = Path.Combine(Path.GetTempPath(), $"pfw-plan-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(path, plan);

            CaptureFailure failure = Assert.Throws<CaptureFailure>(() => CapturePlan.Load(path));

            Assert.Contains(expectedFragment, failure.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>The incoherent plans, one per rule the loader enforces.</summary>
    /// <returns>Label, plan document, expected refusal fragment.</returns>
    public static TheoryData<string, string, string> IncoherentPlans()
    {
        const string Artifact = """{ "name": "one.json", "description": "d" }""";
        const string Step = """
            { "output": "o", "artifact": "one.json", "observes": "x",
              "calls": [ { "name": "c", "method": "POST", "path": "/p" } ] }
            """;

        static string Plan(string body) => $$"""
            { "planVersion": "1", "workflowId": "w", {{body}} }
            """;

        return new TheoryData<string, string, string>
        {
            {
                "not JSON at all",
                "{ this is not json",
                "not valid JSON"
            },
            {
                "an unknown plan version",
                $$"""{ "planVersion": "9", "workflowId": "w", "artifacts": [{{Artifact}}], "steps": [{{Step}}] }""",
                "only '1' is known"
            },
            {
                "no workflow identifier",
                $$"""{ "planVersion": "1", "artifacts": [{{Artifact}}], "steps": [{{Step}}] }""",
                "names no workflowId"
            },
            {
                "no steps",
                Plan($$"""  "artifacts": [{{Artifact}}], "steps": [] """),
                "declares no steps"
            },
            {
                "a newline convention nothing implements",
                Plan($$""" "newlineConvention": "crlf", "artifacts": [{{Artifact}}], "steps": [{{Step}}] """),
                "Only 'lf' is"
            },
            {
                "an artifact name the store swallows",
                Plan("""
                     "artifacts": [ { "name": "capture.log", "description": "d" } ],
                     "steps": [ { "output": "o", "artifact": "capture.log", "observes": "x",
                                  "calls": [ { "name": "c", "method": "POST", "path": "/p" } ] } ]
                     """),
                "gitignore"
            },
            {
                "the same output declared twice",
                Plan($$""" "artifacts": [{{Artifact}}], "steps": [{{Step}}, {{Step}}] """),
                "twice"
            },
            {
                "a step writing an artifact the plan does not declare",
                Plan("""
                     "artifacts": [ { "name": "one.json", "description": "d" } ],
                     "steps": [ { "output": "o", "artifact": "other.json", "observes": "x",
                                  "calls": [ { "name": "c", "method": "POST", "path": "/p" } ] } ]
                     """),
                "does not declare"
            },
            {
                "a declared artifact no step writes",
                Plan($$"""
                      "artifacts": [{{Artifact}}, { "name": "unwritten.json", "description": "d" }],
                      "steps": [{{Step}}]
                      """),
                "that no step writes"
            },
            {
                "a step that observes nothing",
                Plan($$"""
                      "artifacts": [{{Artifact}}],
                      "steps": [ { "output": "o", "artifact": "one.json", "observes": "x" } ]
                      """),
                "observes nothing"
            },
            {
                "two calls sharing a name",
                Plan($$"""
                      "artifacts": [{{Artifact}}],
                      "steps": [ { "output": "o", "artifact": "one.json", "observes": "x", "calls": [
                        { "name": "c", "method": "POST", "path": "/p" },
                        { "name": "c", "method": "POST", "path": "/q" } ] } ]
                      """),
                "declares call 'c' twice"
            },
            {
                "a call with no path",
                Plan($$"""
                      "artifacts": [{{Artifact}}],
                      "steps": [ { "output": "o", "artifact": "one.json", "observes": "x",
                                   "calls": [ { "name": "c", "method": "POST" } ] } ]
                      """),
                "names no method or no path"
            },
            {
                "a derivation kind nothing implements",
                Plan($$"""
                      "artifacts": [{{Artifact}}],
                      "steps": [ { "output": "o", "artifact": "one.json", "observes": "x",
                                   "calls": [ { "name": "c", "method": "POST", "path": "/p" } ],
                                   "derive": [ { "name": "d", "kind": "invent", "value": "x" } ] } ]
                      """),
                "does not implement"
            },
            {
                "a derivation named after a call",
                Plan($$"""
                      "artifacts": [{{Artifact}}],
                      "steps": [ { "output": "o", "artifact": "one.json", "observes": "x",
                                   "calls": [ { "name": "c", "method": "POST", "path": "/p" } ],
                                   "derive": [ { "name": "c", "kind": "characterLength", "value": "x" } ] } ]
                      """),
                "same as one of its calls"
            },
            {
                "a teardown operation that captures",
                Plan($$"""
                      "artifacts": [{{Artifact}}],
                      "steps": [ { "output": "o", "artifact": "one.json", "observes": "x",
                                   "calls": [ { "name": "c", "method": "POST", "path": "/p" } ],
                                   "teardown": [ { "name": "t", "method": "POST", "path": "/r",
                                                   "capture": ["x"] } ] } ]
                      """),
                "captures something"
            },
            {
                "a teardown operation with no request",
                Plan($$"""
                      "artifacts": [{{Artifact}}],
                      "steps": [ { "output": "o", "artifact": "one.json", "observes": "x",
                                   "calls": [ { "name": "c", "method": "POST", "path": "/p" } ],
                                   "teardown": [ { "name": "t" } ] } ]
                      """),
                "no request to issue"
            },
        };
    }

    /// <summary>A plan file that is not there is refused with the reason, not a filesystem error.</summary>
    [Fact]
    public void AnAbsentPlanIsRefusedWithTheReasonRatherThanAFileNotFound()
    {
        CaptureFailure failure = Assert.Throws<CaptureFailure>(() =>
            CapturePlan.Load(Path.Combine(Path.GetTempPath(), $"pfw-absent-{Guid.NewGuid():N}.json")));

        Assert.Contains("No capture plan exists", failure.Message, StringComparison.Ordinal);
        Assert.Contains("reviewed plan file", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every derivation kind the plan model admits is implemented, in both directions.
    /// </summary>
    /// <remarks>
    /// 🔴 THE TWO-DIRECTIONAL CHECK IS THE POINT. A kind admitted by validation but missing from the
    /// computation would pass every plan and fail at run time, halfway through a capture; a kind
    /// implemented but not admitted would be unreachable from any plan. Asserting the sets are equal
    /// catches both, and it is the reason the switch carries a default arm saying exactly that.
    /// </remarks>
    [Fact]
    public void EveryAdmittedDerivationKindIsImplementedAndEveryImplementedKindIsAdmitted()
    {
        foreach (string kind in CapturePlan.DerivationKinds)
        {
            PlannedDerivation derivation = new()
            {
                Name = "d",
                Kind = kind,
                Value = "AAAA",
                Other = "AAAA",
                BlockBytes = 1,
            };

            // Applying it must not fall through to the "not implemented" arm. Every kind either answers
            // or fails for a reason specific to its own inputs, never for being unknown.
            JsonObject derived = Derivations.Apply(derivation, ["AAAA"], "AAAA", "row");

            Assert.Equal(kind, derived["kind"]!.GetValue<string>());
        }

        // ...and nothing is implemented that no plan can ask for: an unadmitted kind reaches the default
        // arm, which is how this direction is observable at all.
        CaptureFailure failure = Assert.Throws<CaptureFailure>(() => Derivations.Apply(
            new PlannedDerivation { Name = "d", Kind = "notAKind", Value = "AAAA" },
            ["AAAA"],
            null,
            "row"));

        Assert.Contains("have diverged", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every value-revealing kind is a kind that exists, so the refusal cannot be silently disarmed.
    /// </summary>
    [Fact]
    public void EveryValueRevealingDerivationIsAnAdmittedDerivation() =>
        Assert.All(
            CapturePlan.ValueRevealingDerivations,
            kind => Assert.Contains(kind, CapturePlan.DerivationKinds));
}

/// <summary>The seven derivations, as pure functions.</summary>
public sealed class DerivationTests
{
    /// <summary>The comparing derivation answers on equality and refuses without a second operand.</summary>
    [Fact]
    public void TheComparingDerivationAnswersOnOrdinalEqualityAndDemandsASecondOperand()
    {
        PlannedDerivation derivation = new() { Name = "roundTrip", Kind = "equals", Value = "abc" };

        Assert.True(Derivations.Apply(derivation, ["abc"], "abc", "row")["equal"]!.GetValue<bool>());
        Assert.False(Derivations.Apply(derivation, ["abc"], "abd", "row")["equal"]!.GetValue<bool>());

        // Case matters: a round trip that returned differently-cased text did not return the original.
        Assert.False(Derivations.Apply(derivation, ["abc"], "ABC", "row")["equal"]!.GetValue<bool>());

        CaptureFailure failure = Assert.Throws<CaptureFailure>(() =>
            Derivations.Apply(derivation, ["abc"], null, "row"));

        Assert.Contains("names no 'other'", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Hexadecimal rendering is lower case and reports the byte count.</summary>
    [Fact]
    public void HexadecimalRenderingIsLowerCaseAndCarriesTheByteCount()
    {
        JsonObject derived = Derivations.Apply(
            new PlannedDerivation { Name = "hex", Kind = "hexOfBase64", Value = "x" },
            [Convert.ToBase64String([0xDE, 0xAD, 0xBE, 0xEF])],
            null,
            "row");

        Assert.Equal("deadbeef", derived["hex"]!.GetValue<string>());
        Assert.Equal(4, derived["byteCount"]!.GetValue<int>());
    }

    /// <summary>A non-base64 input is refused with a message naming the contract's forms.</summary>
    [Fact]
    public void ANonBase64InputIsRefusedWithAMessageAPlanAuthorCanAct0n()
    {
        CaptureFailure failure = Assert.Throws<CaptureFailure>(() => Derivations.Apply(
            new PlannedDerivation { Name = "hex", Kind = "hexOfBase64", Value = "x" },
            ["not base64 !!"],
            null,
            "row"));

        Assert.Contains("expects base64", failure.Message, StringComparison.Ordinal);
        Assert.Contains("BLOB payload", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The block-repetition derivation makes the unchained mode's leak measurable.
    /// </summary>
    /// <remarks>
    /// The figures asserted here are the ones that carry the finding: four identical blocks out of five
    /// means the plaintext's structure survived into the ciphertext. A ciphertext with no repetition
    /// would give distinctBlocks equal to blockCount and a maximum repetition of one.
    /// </remarks>
    [Fact]
    public void TheBlockRepetitionDerivationCountsTheRepetitionAnUnchainedModeLeaks()
    {
        byte[] block = [0x47, 0x10, 0x80, 0x5B, 0x0F, 0xF7, 0x67, 0x93];
        byte[] tail = [0x8D, 0x34, 0xBD, 0xBB, 0xC7, 0x0B, 0x6B, 0x7F];

        byte[] cipher = [.. block, .. block, .. block, .. block, .. tail];

        JsonObject derived = Derivations.Apply(
            new PlannedDerivation { Name = "blocks", Kind = "hexBlockRepetition", Value = "x", BlockBytes = 8 },
            [Convert.ToBase64String(cipher)],
            null,
            "row");

        Assert.Equal(8, derived["blockBytes"]!.GetValue<int>());
        Assert.Equal(40, derived["byteCount"]!.GetValue<int>());
        Assert.Equal(5, derived["blockCount"]!.GetValue<int>());
        Assert.Equal(2, derived["distinctBlocks"]!.GetValue<int>());
        Assert.Equal(4, derived["maximumBlockRepetition"]!.GetValue<int>());
        Assert.Equal("4710805b0ff76793", ((JsonArray)derived["blocks"]!)[0]!.GetValue<string>());
    }

    /// <summary>A block-repetition derivation with no block size is refused.</summary>
    [Fact]
    public void BlockRepetitionWithoutABlockSizeIsRefusedRatherThanGuessed()
    {
        CaptureFailure failure = Assert.Throws<CaptureFailure>(() => Derivations.Apply(
            new PlannedDerivation { Name = "blocks", Kind = "hexBlockRepetition", Value = "x" },
            ["AAAAAAAAAAA="],
            null,
            "row"));

        Assert.Contains("cannot be inferred from the ciphertext", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The character-class derivation unions across samples, which is why it is sound.
    /// </summary>
    /// <remarks>
    /// 🔴 THIS ROW ENCODES A MEASURED DEFECT. A single 24-character sample under the digit, letter and
    /// symbol flags was observed to report no digit on one run and a digit on the next - because the
    /// presence of a class in one short sample is a property of the sample, not of the generator. The
    /// union across samples cannot gain a class the alphabet lacks, so it is a sound estimate where a
    /// single sample is not. The row below is exactly that situation: sample one has no digit, sample
    /// two does, and the union says the generator draws digits.
    /// </remarks>
    [Fact]
    public void TheCharacterClassDerivationUnionsAcrossSamplesRatherThanTrustingOne()
    {
        JsonObject derived = Derivations.Apply(
            new PlannedDerivation { Name = "classes", Kind = "characterClasses", Value = "a" },
            ["abcdef", "abc123", "abc!@#"],
            null,
            "row");

        Assert.Equal(3, derived["sampleCount"]!.GetValue<int>());
        Assert.Equal(18, derived["charactersObserved"]!.GetValue<int>());

        JsonObject union = (JsonObject)derived["union"]!;

        Assert.True(union["digits"]!.GetValue<bool>());
        Assert.True(union["letters"]!.GetValue<bool>());
        Assert.True(union["symbols"]!.GetValue<bool>());

        // The per-sample figures are recorded as well, and they DISAGREE with each other. That
        // disagreement is the evidence that the union is the sound comparison rather than a convenience.
        JsonArray perSample = (JsonArray)derived["perSample"]!;

        Assert.False(((JsonObject)perSample[0]!)["digits"]!.GetValue<bool>());
        Assert.True(((JsonObject)perSample[1]!)["digits"]!.GetValue<bool>());
        Assert.False(((JsonObject)perSample[2]!)["digits"]!.GetValue<bool>());
    }

    /// <summary>Identifier formatting is described without revealing the identifier.</summary>
    /// <param name="value">The identifier.</param>
    /// <param name="bracketed">Whether it should read as bracketed.</param>
    /// <param name="separators">How many separators it should report.</param>
    /// <remarks>
    /// The four rows are the four flag combinations the workflow definition requires be distinguished.
    /// A descriptor that could not tell them apart would let a generator ignoring its flags pass.
    /// </remarks>
    [Theory]
    [InlineData("2a14b3a5f0e64869a2284134b7ad72c8", false, 0)]
    [InlineData("{e3ad4253805241d1b84415158b002bc7}", true, 0)]
    [InlineData("e93f8e87-8dcb-4451-a771-ee77d16ccded", false, 4)]
    [InlineData("{beab6d79-383e-4915-9663-f0e685c47616}", true, 4)]
    public void IdentifierFormattingDistinguishesTheFourFlagCombinations(
        string value,
        bool bracketed,
        int separators)
    {
        JsonObject derived = Derivations.Apply(
            new PlannedDerivation { Name = "formatting", Kind = "guidFormatting", Value = "x" },
            [value],
            null,
            "row");

        Assert.Equal(bracketed, derived["bracketed"]!.GetValue<bool>());
        Assert.Equal(separators, derived["separatorCount"]!.GetValue<int>());
        Assert.Equal(value.Length, derived["characters"]!.GetValue<int>());

        // The descriptor carries POSITIONS, not just a count: two identifiers with the same number of
        // separators in different places are differently formatted.
        Assert.Equal(separators, ((JsonArray)derived["separatorPositions"]!).Count);
    }

    /// <summary>The length derivations report a length and nothing else.</summary>
    [Fact]
    public void TheLengthDerivationsReportALengthAndNothingElse()
    {
        JsonObject bytes = Derivations.Apply(
            new PlannedDerivation { Name = "n", Kind = "byteLengthOfBase64", Value = "x" },
            [Convert.ToBase64String(new byte[128])],
            null,
            "row");

        Assert.Equal(128, bytes["byteCount"]!.GetValue<int>());
        Assert.DoesNotContain("hex", bytes.Select(static member => member.Key));

        JsonObject characters = Derivations.Apply(
            new PlannedDerivation { Name = "n", Kind = "characterLength", Value = "x" },
            ["abcdef"],
            null,
            "row");

        Assert.Equal(6, characters["characters"]!.GetValue<int>());
        Assert.DoesNotContain("value", characters.Select(static member => member.Key));
    }

    /// <summary>A derivation with no input at all is refused rather than answering on nothing.</summary>
    [Fact]
    public void ADerivationWithNoInputIsRefused()
    {
        CaptureFailure failure = Assert.Throws<CaptureFailure>(() => Derivations.Apply(
            new PlannedDerivation { Name = "d", Kind = "characterLength", Value = "x" },
            [],
            null,
            "row"));

        Assert.Contains("no input to derive from", failure.Message, StringComparison.Ordinal);
    }
}

/// <summary>Reference resolution, including every refusal a plan author can trip.</summary>
public sealed class ReferenceResolutionTests
{
    /// <summary>Builds a runner with no plan to execute, for resolution rows.</summary>
    /// <param name="arguments">The command line.</param>
    /// <returns>The runner and its screen.</returns>
    private static (CaptureRunner Runner, SecretScreen Screen) Runner(params string[] arguments)
    {
        CaptureOptions options = CaptureOptions.Parse(
            [.. arguments, "--workflow", "w", "--base-url", "https://localhost:1", "--dry-run"]);

        SecretScreen screen = new();

        CapturePlan plan = JsonSerializer.Deserialize<CapturePlan>(
            """{ "planVersion": "1", "workflowId": "w" }""",
            CapturePlan.SerializerOptions)!;

        return (new CaptureRunner(plan, options, screen), screen);
    }

    /// <summary>Text that is not a placeholder is returned unchanged.</summary>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("PowerFramework")]
    [InlineData("")]
    [InlineData("${unterminated")]
    [InlineData("not a ${placeholder} because it does not start with one")]
    public void TextThatIsNotAPlaceholderPassesThroughUnchanged(string text) =>
        Assert.Equal(text, Runner().Runner.ResolveText(text, null, "row"));

    /// <summary>The three well-known references resolve from their own options.</summary>
    [Fact]
    public void TheWellKnownReferencesResolveFromTheirOwnOptions()
    {
        (CaptureRunner runner, _) = Runner("--key-ref", "k", "--iv-ref", "v", "--file-ref", "f");

        Assert.Equal("k", runner.ResolveText("${keyRef}", null, "row"));
        Assert.Equal("v", runner.ResolveText("${ivRef}", null, "row"));
        Assert.Equal("f", runner.ResolveText("${fileRef}", null, "row"));

        // ...and they are the SAME named references the general form reaches, so a plan may spell either.
        Assert.Equal("k", runner.ResolveText("${ref:key}", null, "row"));
    }

    /// <summary>A reference the command line did not supply is refused by name.</summary>
    [Fact]
    public void AnUnsuppliedReferenceIsRefusedNamingTheOptionThatSuppliesIt()
    {
        CaptureFailure failure = Assert.Throws<CaptureFailure>(() =>
            Runner().Runner.ResolveText("${keyRef}", null, "the step"));

        Assert.Contains("--key-ref", failure.Message, StringComparison.Ordinal);
        Assert.Contains("the step", failure.Message, StringComparison.Ordinal);

        // The message says REFERENCE, never material, because that distinction is the whole reason a
        // reference may be a command-line value at all.
        Assert.Contains("never material", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The byte and base64 helpers let a plan state its intent rather than a blob.</summary>
    [Fact]
    public void TheLiteralHelpersLetAPlanStateItsIntentRatherThanABlob()
    {
        (CaptureRunner runner, _) = Runner();

        string filler = runner.ResolveText("${bytes:Z:118}", null, "row");

        Assert.Equal(118, Convert.FromBase64String(filler).Length);
        Assert.All(Convert.FromBase64String(filler), value => Assert.Equal((byte)'Z', value));

        Assert.Equal("YWJj", runner.ResolveText("${base64:abc}", null, "row"));
    }

    /// <summary>A malformed literal helper is refused with its shape.</summary>
    /// <param name="text">The malformed placeholder.</param>
    [Theory]
    [InlineData("${bytes:ZZ:4}")]
    [InlineData("${bytes:Z:0}")]
    [InlineData("${bytes:Z:notanumber}")]
    public void AMalformedLiteralHelperIsRefusedWithItsShape(string text)
    {
        CaptureFailure failure = Assert.Throws<CaptureFailure>(() =>
            Runner().Runner.ResolveText(text, null, "row"));

        Assert.Contains("${bytes:", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>An unknown placeholder is refused, and the refusal enumerates the whole closed set.</summary>
    [Fact]
    public void AnUnknownPlaceholderIsRefusedAndTheRefusalEnumeratesTheClosedSet()
    {
        CaptureFailure failure = Assert.Throws<CaptureFailure>(() =>
            Runner().Runner.ResolveText("${invented}", null, "row"));

        foreach (string token in (string[])["${keyRef}", "${ref:", "${bytes:", "${base64:", "${call:",
            "${tamperLastByte:"])
        {
            Assert.Contains(token, failure.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>A reference to a call that has not run is refused as a forward reference.</summary>
    [Fact]
    public void AForwardReferenceIsRefusedRatherThanResolvingToNothing()
    {
        CaptureFailure failure = Assert.Throws<CaptureFailure>(() =>
            Runner().Runner.ResolveText("${call:later:data}", null, "the step"));

        Assert.Contains("has not run", failure.Message, StringComparison.Ordinal);
        Assert.Contains("declaration order", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A member captured as a SHAPE cannot be chained, and the refusal says why that is deliberate.
    /// </summary>
    /// <remarks>
    /// 🔴 THIS IS THE GUARD THAT STOPS KEY MATERIAL BEING THREADED THROUGH A PLAN. The driver caught a
    /// real instance of it in this repository's own plan: the randomized asymmetric ciphertext was
    /// captured as a shape and then chained into the decrypt call, and the run was refused rather than
    /// producing a recording nobody could compare.
    /// </remarks>
    [Fact]
    public void AShapeCapturedMemberCannotBeChained()
    {
        (CaptureRunner runner, _) = Runner();

        // A shape capture never enters the chain table at all, so the reference finds the call present
        // and the member absent - which is a different, more precise refusal than a forward reference.
        runner.SeedChainForTesting("generate", "keyRef", "opaque-value", opaque: true);

        CaptureFailure failure = Assert.Throws<CaptureFailure>(() =>
            runner.ResolveText("${call:generate:publicKey}", null, "the step"));

        Assert.Contains("did not make chainable", failure.Message, StringComparison.Ordinal);
        Assert.Contains("stops key material being threaded", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A chained value resolves, and the tampering form alters exactly one byte.</summary>
    [Fact]
    public void TheTamperingFormAltersExactlyOneByteAndIsReproducible()
    {
        (CaptureRunner runner, _) = Runner();

        byte[] cipher = [1, 2, 3, 4];

        runner.SeedChainForTesting("encrypt", "data", Convert.ToBase64String(cipher), opaque: false);

        Assert.Equal(
            Convert.ToBase64String(cipher),
            runner.ResolveText("${call:encrypt:data}", null, "row"));

        byte[] tampered = Convert.FromBase64String(
            runner.ResolveText("${tamperLastByte:encrypt:data}", null, "row"));

        Assert.Equal(cipher.Length, tampered.Length);
        Assert.Equal(cipher[..^1], tampered[..^1]);
        Assert.Equal(cipher[^1] ^ 0x01, tampered[^1]);

        // Reproducible: the same input gives the same altered value every time, so a plan never has to
        // hardcode a tampered ciphertext that would only be correct for one particular run.
        Assert.Equal(
            runner.ResolveText("${tamperLastByte:encrypt:data}", null, "row"),
            runner.ResolveText("${tamperLastByte:encrypt:data}", null, "row"));
    }

    /// <summary>
    /// A value-revealing derivation over an opaque value is refused; a summarising one is allowed.
    /// </summary>
    /// <remarks>
    /// 🔴 BOTH DIRECTIONS MATTER. Refusing everything over an opaque value would make the masked random
    /// seams uncharacterizable - the workflow definitions require their character classes and formatting
    /// be compared. Allowing everything would let a hexadecimal rendering carry an opaque value into the
    /// artifact by a route the screen cannot see, because the screen rewrites exact spellings rather
    /// than the same bytes in another encoding.
    /// </remarks>
    [Fact]
    public void AValueRevealingDerivationOverAnOpaqueValueIsRefusedAndASummarisingOneIsNot()
    {
        (CaptureRunner runner, _) = Runner();

        runner.SeedChainForTesting("sign", "data", "AAECAwQ=", opaque: true);

        PlannedStep step = new() { Output = "rsa.sign.sha256", Artifact = "a.json" };

        foreach (string kind in CapturePlan.ValueRevealingDerivations)
        {
            CaptureFailure failure = Assert.Throws<CaptureFailure>(() =>
                runner.CheckDerivationAdmissibility(
                    new PlannedDerivation { Name = "d", Kind = kind, Value = "${call:sign:data}", BlockBytes = 1 },
                    step));

            Assert.Contains("OPAQUE value", failure.Message, StringComparison.Ordinal);
            Assert.Contains("defeats the screen", failure.Message, StringComparison.Ordinal);
        }

        foreach (string kind in CapturePlan.DerivationKinds.Except(CapturePlan.ValueRevealingDerivations))
        {
            runner.CheckDerivationAdmissibility(
                new PlannedDerivation { Name = "d", Kind = kind, Value = "${call:sign:data}" },
                step);
        }
    }

    /// <summary>The extra inputs of a union derivation are screened for opacity too.</summary>
    /// <remarks>
    /// A refusal that inspected only the primary input would be trivially bypassed by moving the opaque
    /// reference into the second position.
    /// </remarks>
    [Fact]
    public void TheOpacityRefusalInspectsEveryInputRatherThanOnlyTheFirst()
    {
        (CaptureRunner runner, _) = Runner();

        runner.SeedChainForTesting("first", "data", "AAECAwQ=", opaque: false);
        runner.SeedChainForTesting("second", "data", "AAECAwQ=", opaque: true);

        CaptureFailure failure = Assert.Throws<CaptureFailure>(() =>
            runner.CheckDerivationAdmissibility(
                new PlannedDerivation
                {
                    Name = "d",
                    Kind = "hexOfBase64",
                    Value = "${call:first:data}",
                    Values = ["${call:second:data}"],
                },
                new PlannedStep { Output = "o", Artifact = "a.json" }));

        Assert.Contains("OPAQUE value", failure.Message, StringComparison.Ordinal);
    }
}

/// <summary>The command line, and the credential rule it enforces by refusing options.</summary>
public sealed class CaptureOptionsTests
{
    /// <summary>
    /// A credential cannot be an argument VALUE, and the option that would allow it does not exist.
    /// </summary>
    /// <param name="option">The option a hurried operator would reach for.</param>
    /// <remarks>
    /// 🔴 REFUSED BY NAME RATHER THAN SIMPLY ABSENT. An unknown option would produce a generic message
    /// and an operator would look for the right spelling; naming these two explicitly is what turns the
    /// refusal into the explanation. Anything on a command line is readable from /proc/&lt;pid&gt;/cmdline
    /// by any process of the same user for as long as the run lasts, which is why the driver takes a
    /// PATH and reads the value itself.
    /// </remarks>
    [Theory]
    [InlineData("--token")]
    [InlineData("--client-secret")]
    public void ACredentialCannotBeAnArgumentValue(string option)
    {
        CaptureFailure failure = Assert.Throws<CaptureFailure>(() =>
            CaptureOptions.Parse([option, "a-secret-value", "--workflow", "w"]));

        Assert.Contains(option, failure.Message, StringComparison.Ordinal);
        Assert.Contains(option + "-file", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("a-secret-value", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Named references accumulate, and a malformed pair is refused.</summary>
    [Fact]
    public void NamedReferencesAccumulateAndAMalformedPairIsRefused()
    {
        CaptureOptions options = CaptureOptions.Parse([
            "--workflow", "w", "--dry-run",
            "--ref", "hmac=qa_hmac", "--ref", "passphrase=qa_pass", "--key-ref", "qa_key"]);

        Assert.Equal("qa_hmac", options.Reference("hmac"));
        Assert.Equal("qa_pass", options.Reference("passphrase"));
        Assert.Equal("qa_key", options.Reference("key"));
        Assert.Null(options.Reference("absent"));

        foreach (string malformed in (string[])["nopair", "=novalue", "noname="])
        {
            CaptureFailure failure = Assert.Throws<CaptureFailure>(() =>
                CaptureOptions.Parse(["--workflow", "w", "--dry-run", "--ref", malformed]));

            Assert.Contains("<name>=<reference>", failure.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>The reporting mode needs neither a workflow nor a service.</summary>
    /// <remarks>
    /// Requiring them would mean the one command that always works - saying what can and cannot be
    /// compared - needed a running service in order to say that nothing can.
    /// </remarks>
    [Fact]
    public void TheReportingModeNeedsNeitherAWorkflowNorAService()
    {
        CaptureOptions options = CaptureOptions.Parse(["--pair-state"]);

        Assert.True(options.PairStateOnly);
        Assert.Equal(string.Empty, options.WorkflowId);
        Assert.Null(options.BaseAddress);
    }

    /// <summary>A capture demands a workflow and, unless it is a dry run, a base address.</summary>
    [Fact]
    public void ACaptureDemandsAWorkflowAndABaseAddress()
    {
        Assert.Contains(
            "--workflow is required",
            Assert.Throws<CaptureFailure>(() => CaptureOptions.Parse([])).Message,
            StringComparison.Ordinal);

        Assert.Contains(
            "--base-url is required",
            Assert.Throws<CaptureFailure>(() => CaptureOptions.Parse(["--workflow", "w"])).Message,
            StringComparison.Ordinal);

        // A dry run validates the plan and the destination without issuing anything, so it needs no
        // address at all.
        Assert.Null(CaptureOptions.Parse(["--workflow", "w", "--dry-run"]).BaseAddress);
    }

    /// <summary>Presenting a token and a credential at once is refused as ambiguous.</summary>
    [Fact]
    public void PresentingBothATokenAndAnIssuanceCredentialIsRefused()
    {
        CaptureFailure failure = Assert.Throws<CaptureFailure>(() => CaptureOptions.Parse([
            "--workflow", "w", "--dry-run", "--token-file", "/tmp/t", "--client-secret-file", "/tmp/s"]));

        Assert.Contains("alternatives", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The usage text names every option the parser accepts.</summary>
    /// <remarks>
    /// A DOCUMENTATION GUARD, and a cheap one. An option added without a usage line is invisible to
    /// whoever runs the tool, which is the same as not existing.
    /// </remarks>
    [Fact]
    public void TheUsageTextNamesEveryOptionTheParserAccepts()
    {
        foreach (string option in (string[])[
            "--workflow", "--base-url", "--token-file", "--issuer-url", "--client-id",
            "--client-secret-file", "--audience", "--scope", "--key-ref", "--iv-ref", "--file-ref",
            "--ref", "--output", "--plan", "--ca-file", "--dry-run", "--pair-state", "--help"])
        {
            Assert.Contains(option, CaptureOptions.Usage, StringComparison.Ordinal);
        }
    }
}

/// <summary>The screen that proves no secret survives into an artifact.</summary>
public sealed class SecretScreenTests
{
    /// <summary>A registered value is rewritten wherever it appears, at any depth.</summary>
    [Fact]
    public void ARegisteredValueIsRewrittenAtAnyDepth()
    {
        SecretScreen screen = new();

        screen.Register("qa_key", "${keyRef}");

        JsonObject document = new()
        {
            ["top"] = "qa_key",
            ["nested"] = new JsonObject { ["inner"] = "prefix qa_key suffix" },
            ["array"] = new JsonArray("qa_key", "unrelated"),
        };

        screen.Screen(document);

        string rendered = document.ToJsonString();

        Assert.DoesNotContain("qa_key", rendered, StringComparison.Ordinal);
        Assert.Contains("${keyRef}", rendered, StringComparison.Ordinal);
        Assert.Contains("unrelated", rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// The proof fails when a registered value survives, and it never quotes the value it found.
    /// </summary>
    /// <remarks>
    /// 🔴 A FAILURE MESSAGE THAT QUOTED THE LEAKED VALUE WOULD LEAK IT INTO THE BUILD LOG, which is the
    /// same disclosure by another route - and a build log is retained longer than a recording.
    /// </remarks>
    [Fact]
    public void TheProofFailsOnASurvivingValueWithoutQuotingIt()
    {
        SecretScreen screen = new();

        screen.Register("s3cret-material", "${keyRef}");

        CaptureFailure failure = Assert.Throws<CaptureFailure>(() =>
            screen.Prove("a document containing s3cret-material verbatim", "hashing.json"));

        Assert.Contains("hashing.json", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret-material", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Registering nothing is a no-op, so an unsupplied option is not a hazard.</summary>
    [Fact]
    public void RegisteringNothingIsANoOperation()
    {
        SecretScreen screen = new();

        screen.Register(null, "${keyRef}");
        screen.Register(string.Empty, "${ivRef}");

        // An empty registration that matched would rewrite every character of every artifact.
        screen.Prove("anything at all", "any.json");
    }
}

/// <summary>The store rules, which decide what may be written and where.</summary>
public sealed class StoreRuleTests
{
    /// <summary>Only the admitted extensions are accepted, and a swallowed name is explained.</summary>
    /// <param name="fileName">The name.</param>
    /// <param name="admitted">Whether it should be admitted.</param>
    [Theory]
    [InlineData("hashing.json", true)]
    [InlineData("trace.jsonl", true)]
    [InlineData("notes.txt", true)]
    [InlineData("rows.csv", true)]
    [InlineData("statements.sql", true)]
    [InlineData("README.md", true)]
    [InlineData("definition.yaml", true)]
    [InlineData("capture.log", false)]
    [InlineData("bundle.zip", false)]
    [InlineData("state.bak", false)]
    [InlineData("crash.dmp", false)]
    [InlineData("run.bat", false)]
    [InlineData("thumbs.db", false)]
    [InlineData("image.png", false)]
    public void OnlyTheAdmittedExtensionsAreAccepted(string fileName, bool admitted) =>
        Assert.Equal(admitted, StoreRules.IsAdmittedRecordingName(fileName));

    /// <summary>
    /// A name the repository's own ignore rules would swallow is refused with THAT as the reason.
    /// </summary>
    /// <remarks>
    /// 🔴 THE TRAP THIS CLOSES. The repository root .gitignore carries unanchored patterns, so a
    /// recording named capture.log would be written, would look present locally, and would be silently
    /// untracked - a pair that exists on one machine and nowhere else. The refusal names the ignore
    /// rule rather than saying "unsupported extension", because the extension is not the problem.
    /// </remarks>
    [Fact]
    public void ASwallowedNameIsRefusedWithTheIgnoreRuleAsTheReason()
    {
        string explanation = StoreRules.ExplainRefusedName("capture.log");

        Assert.Contains(".gitignore", explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("capture.log", explanation, StringComparison.Ordinal);
    }

    /// <summary>Scaffolding does not count as a recording; an ordinary file does.</summary>
    /// <remarks>
    /// 🔴 THIS PREDICATE HAS THREE COPIES IN THE TREE and they must agree: the driver's, the store
    /// guard's, and the pinyin oracle hook's activation predicate. If a readme or a dot-file counted,
    /// the AAP's own scaffold directories would read as captures the moment they landed - and the
    /// pinyin matrix would un-skip and fail a build over a recording nobody took.
    /// </remarks>
    [Fact]
    public void ScaffoldingDoesNotCountAsARecordingAndAnOrdinaryFileDoes()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"pfw-store-{Guid.NewGuid():N}");

        try
        {
            Assert.False(StoreRules.CarriesARecording(directory));

            Directory.CreateDirectory(directory);
            Assert.False(StoreRules.CarriesARecording(directory));

            File.WriteAllText(Path.Combine(directory, "README.md"), "scaffolding");
            Assert.False(StoreRules.CarriesARecording(directory));

            File.WriteAllText(Path.Combine(directory, ".gitkeep"), string.Empty);
            Assert.False(StoreRules.CarriesARecording(directory));

            File.WriteAllText(Path.Combine(directory, "hashing.json"), "{}");
            Assert.True(StoreRules.CarriesARecording(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
