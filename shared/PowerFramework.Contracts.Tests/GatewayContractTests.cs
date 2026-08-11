// ==================================================================================================
//  GatewayContractTests - CONTRACT C-09 (REST INGRESS) AND THE INGRESS HALF OF C-10 (READINESS)
//  ------------------------------------------------------------------------------------------------
//  SUBJECT     OpenApi/gateway.v1.yaml
//  AUTHORITY   docs/CONTRACTS.md 12.1 (C-09), 12.2 (C-10), 13 (the four reserved routes)
//              Agent Action Plan 0.4.3 C-09/C-10 and 0.4.4
//
//  WHAT THIS FILE ESTABLISHES
//  ------------------------------------------------------------------------------------------------
//  Gateway is the composition root and the system's ONLY ingress. The legacy has none - it is a
//  library with no listener, no route table and no authentication of any kind, because there was
//  nothing to authenticate against. So every property asserted here is a property of a NEW boundary,
//  and there is no legacy behaviour to compare it against: the specification in docs/CONTRACTS.md IS
//  the oracle for this contract, and these tests hold the document to it.
//
//  That is a different kind of test from the parity suites elsewhere in this repository, and the
//  distinction is worth being explicit about. A parity test asserts that ported code reproduces
//  measured legacy behaviour. These assert that a PUBLISHED CONTRACT matches its SPECIFICATION -
//  agreement between two authored artifacts, one of which is prose. Neither is a Golden-Master
//  comparison against the PowerBuilder oracle, and nothing here should be read as one.
//
//  THE FOUR THINGS WORTH THE MOST SCRUTINY
//  ------------------------------------------------------------------------------------------------
//  1. `Aborted` -> 409. The optimistic-concurrency conflict must reach a REST caller as a status it
//     can act on, carrying the conflict detail. A silent overwrite anywhere in the system is a
//     correctness failure, not a robustness one.
//  2. /health anonymous, /v1/ping authenticated. These two together are the whole of the
//     authenticated-boundary requirement made testable.
//  3. The four reserved routes return 501 with a MACHINE-READABLE body, and NOTHING exists behind
//     them. This is the C-D compliance boundary and it is asserted from both directions - that the
//     routes are declared, and that no deferred service is implemented.
//  4. Every `x-proto-*` extension resolves to a REAL generated message. gateway.v1.yaml delegates its
//     payload shapes to the protocol definitions rather than transcribing them, and states in its own
//     comments that this test project checks the delegation. This file is that claim being kept.
// ==================================================================================================

using System.Text.Json.Nodes;
using Google.Protobuf.Reflection;
using Microsoft.OpenApi;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Contracts.Persistence.V1;
using Xunit;

namespace PowerFramework.Contracts.Tests;

public sealed class GatewayContractTests
{
    private const string GatewayResourceName = "PowerFramework.Contracts.OpenApi.gateway.v1.yaml";

    private static OpenApiDocument Document =>
        OpenApiContractDocumentTests.ParseEmbeddedDocument(GatewayResourceName);

    /// <summary>Every (route, method, operation) triple in the document, flattened.</summary>
    private static IEnumerable<(string Route, HttpMethod Method, OpenApiOperation Operation)> Operations(
        OpenApiDocument document)
    {
        foreach ((string route, IOpenApiPathItem pathItem) in document.Paths)
        {
            foreach ((HttpMethod method, OpenApiOperation operation) in pathItem.Operations!)
            {
                yield return (route, method, operation);
            }
        }
    }

    /// <summary>
    /// Reads a string-valued specification extension. Returns null when absent.
    /// </summary>
    /// <remarks>
    /// Microsoft.OpenApi 2.x models an unrecognised extension as a <c>JsonNodeExtension</c> wrapping a
    /// <c>JsonNode</c>, so the value is reached through the node rather than off a typed property.
    /// </remarks>
    private static string? Extension(OpenApiOperation operation, string name)
    {
        if (operation.Extensions is null
            || !operation.Extensions.TryGetValue(name, out IOpenApiExtension? extension))
        {
            return null;
        }

        return extension is JsonNodeExtension node ? node.Node?.GetValue<string>() : null;
    }

    /// <summary>
    /// Resolves a fully-qualified protobuf message name - for example
    /// <c>dataservices.v1.UpdateRequest</c> - against the generated file descriptors.
    /// </summary>
    /// <remarks>
    /// Resolution goes through the DESCRIPTORS rather than through <c>Type.GetType</c> on the
    /// generated C# names, deliberately. The descriptor is the contract; the C# type is one projection
    /// of it, and its name is subject to the generator's own conventions - <c>csharp_namespace</c>,
    /// message nesting rendered through a <c>Types</c> holder, and reserved-word mangling. Asserting
    /// against the descriptor asserts against the thing the wire actually agrees on.
    /// </remarks>
    private static MessageDescriptor? ResolveProtoMessage(string fullName)
    {
        FileDescriptor[] files =
        [
            CommonV1Reflection.Descriptor,
            DataservicesV1Reflection.Descriptor,
            PersistenceV1Reflection.Descriptor,
        ];

        foreach (FileDescriptor file in files)
        {
            if (!fullName.StartsWith(file.Package + ".", StringComparison.Ordinal))
            {
                continue;
            }

            string relative = fullName[(file.Package.Length + 1)..];
            MessageDescriptor? found = file.FindTypeByName<MessageDescriptor>(relative);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    // ==============================================================================================
    //  C-10 - /health IS ANONYMOUS, AND IT IS THE ONLY ANONYMOUS OPERATION
    // ==============================================================================================

    [Fact]
    public void HealthIsTheOnlyAnonymousOperationInTheDocument()
    {
        OpenApiDocument document = Document;

        // An anonymous operation overrides the document-level requirement with an EMPTY REQUIREMENT
        // LIST - `security: []`. An operation that simply inherits has no `security` member at all,
        // which the reader models as NULL rather than as an empty list, so the two states are
        // distinguishable and `Count: 0` means "declared itself anonymous" and nothing else.
        //
        // `[]` AND `[{}]` ARE BOTH ANONYMOUS BUT ARE NOT INTERCHANGEABLE, and the document uses `[]`:
        // `[]` says "no requirement applies", whereas `[{}]` says "one requirement applies and it is
        // satisfied by nothing". Both permit anonymous access, but only `[]` says so in the shape every
        // generator and policy checker reads - and it is the form the sibling security.v1.yaml uses for
        // the same purpose, so the two documents in this folder state anonymity one way rather than two.
        (string Route, HttpMethod Method, OpenApiOperation Operation)[] anonymous = Operations(document)
            .Where(static entry => entry.Operation.Security is { Count: 0 })
            .ToArray();

        // EXACTLY ONE. The count matters as much as the identity: this is the assertion that stops a
        // future operation being made anonymous without anyone noticing, which is the failure mode
        // constraint C-G exists to prevent.
        (string route, HttpMethod method, _) = Assert.Single(anonymous);
        Assert.Equal("/health", route);
        Assert.Equal(HttpMethod.Get, method);
    }

    [Fact]
    public void HealthIsAnonymousBecauseAProbeHoldsNoTokenAndProbesDuringStartup()
    {
        OpenApiDocument document = Document;
        OpenApiOperation health = document.Paths["/health"].Operations![HttpMethod.Get];

        // The requirement is a real one and worth restating where it is asserted: requiring a token on
        // a readiness probe would make readiness depend on the very service being probed AND on
        // Security's token issuance already being live. During a cold start neither holds, so the
        // dependency cannot resolve and the service never reports ready. Anonymity here is a
        // correctness requirement, not a convenience.
        Assert.NotNull(health.Security);
        Assert.Empty(health.Security);

        Assert.True(health.Responses!.ContainsKey("200"));
        Assert.True(health.Responses.ContainsKey("503"));

        // AND IT MUST NOT DECLARE A 401. Declaring one would tell a consumer a token is sometimes
        // required, which would be a contradiction of the anonymity this operation depends on.
        Assert.False(
            health.Responses.ContainsKey("401"),
            "/health is anonymous, so a 401 in its response set would contradict its own contract.");
    }

    [Fact]
    public void TheHealthAggregateNamesExactlyThreeUpstreamsIndividually()
    {
        OpenApiDocument document = Document;
        IOpenApiSchema report = document.Components!.Schemas!["AggregateHealthReport"];

        Assert.NotNull(report.Properties);
        Assert.True(report.Properties.ContainsKey("upstreams"));

        IOpenApiSchema upstreams = report.Properties["upstreams"];

        // THREE, BOUNDED AT BOTH ENDS.
        //
        // C-10 requires that the aggregate NAME each upstream and its individual state rather than
        // returning one opaque verdict, because an operator reading a failed aggregate needs to know
        // WHICH upstream is responsible. Bounding the array at exactly three encodes the topology:
        // Gateway depends on Persistence, DataServices and Security, and on nothing else. The 5103
        // slot in the port band is a commented Phase-2 placeholder, not a fourth participant.
        Assert.Equal(3, upstreams.MinItems);
        Assert.Equal(3, upstreams.MaxItems);

        Assert.NotNull(report.Required);
        Assert.Contains("upstreams", report.Required);
        Assert.Contains("status", report.Required);
    }

    [Fact]
    public void TheAggregateDistinguishesNotReadyFromUnhealthy()
    {
        OpenApiDocument document = Document;
        IOpenApiSchema status = document.Components!.Schemas!["AggregateHealthReport"].Properties!["status"];

        string[] states = status.Enum!.Select(static node => node!.GetValue<string>()).ToArray();

        // C-10 REQUIRES THE DISTINCTION EXPLICITLY.
        //
        // A service still completing startup validation is not the same as one whose dependency has
        // failed, and collapsing the two would make the endpoint useless for the decision it exists to
        // support. `Degraded` is reported with 200 so a probe that tears down on any non-2xx does not
        // kill a service that is merely still starting; `Unhealthy` is reported with 503.
        Assert.Equal(["Healthy", "Degraded", "Unhealthy"], states);
    }

    [Fact]
    public void AnUpstreamCanBeReportedUnreachableSeparatelyFromUnhealthy()
    {
        OpenApiDocument document = Document;
        IOpenApiSchema upstreamStatus =
            document.Components!.Schemas!["UpstreamHealth"].Properties!["status"];

        string[] states = upstreamStatus.Enum!.Select(static node => node!.GetValue<string>()).ToArray();

        // `Unreachable` IS A FOURTH STATE ON AN UPSTREAM, AND IT IS NOT REDUNDANT.
        //
        // "I asked and it said it was unhealthy" and "I could not reach it to ask" call for different
        // operator action - a failed dependency inside a running service, versus a network or
        // startup-ordering problem. This distinction exists only because of decomposition: an
        // in-process call cannot be unreachable.
        Assert.Equal(["Healthy", "Degraded", "Unhealthy", "Unreachable"], states);

        IOpenApiSchema service = document.Components.Schemas["UpstreamHealth"].Properties!["service"];
        Assert.Equal(
            ["persistence", "dataservices", "security"],
            service.Enum!.Select(static node => node!.GetValue<string>()).ToArray());
    }

    [Fact]
    public void TheHealthSchemasCarryNoConfigurationValueOrCredentialField()
    {
        OpenApiDocument document = Document;

        // A HEALTH ENDPOINT IS ANONYMOUS, SO EVERYTHING IT REPORTS IS PUBLIC.
        //
        // That makes it the one place where a "helpful" diagnostic field - the connection string it
        // could not open, the key it could not load, the upstream URL it could not reach - would be
        // disclosed to an unauthenticated caller. The schemas therefore carry a free-text `detail` and
        // nothing structured that names a configuration value.
        string[] forbidden =
        [
            "connectionString", "connection_string", "password", "secret", "key", "signingKey",
            "token", "credential", "dbparm", "logpass", "url", "uri", "endpoint", "address",
        ];

        foreach (string schemaName in (string[])["AggregateHealthReport", "UpstreamHealth", "HealthCheckResult"])
        {
            IOpenApiSchema schema = document.Components!.Schemas![schemaName];
            foreach (string property in schema.Properties!.Keys)
            {
                foreach (string marker in forbidden)
                {
                    Assert.False(
                        property.Equals(marker, StringComparison.OrdinalIgnoreCase),
                        $"{schemaName}.{property} would disclose '{marker}' to an unauthenticated "
                            + "caller, because /health is anonymous.");
                }
            }
        }
    }

    // ==============================================================================================
    //  C-10 - /v1/ping IS THE AUTHENTICATION PROOF
    // ==============================================================================================

    [Fact]
    public void PingRequiresATokenAndPublishesItsUnauthorizedResponse()
    {
        OpenApiDocument document = Document;
        OpenApiOperation ping = document.Paths["/v1/ping"].Operations![HttpMethod.Get];

        // NO SECURITY OVERRIDE means the document-level bearer requirement applies. That is the
        // authenticated-by-default posture working as intended: this operation is protected because it
        // said nothing, not because it remembered to.
        Assert.Null(ping.Security);

        Assert.True(ping.Responses!.ContainsKey("200"));

        // THE 401 IS PART OF THE PUBLISHED CONTRACT, NOT AN IMPLEMENTATION DETAIL.
        //
        // The whole purpose of this operation is to make "every new boundary is authenticated"
        // TESTABLE rather than merely asserted. A conformance test needs a documented negative outcome
        // to assert against, and this is it.
        Assert.True(
            ping.Responses.ContainsKey("401"),
            "/v1/ping must publish its 401: the unauthenticated outcome is the point of the endpoint.");
    }

    [Fact]
    public void ThePingResponseAssertsAuthenticationWithoutEchoingTheToken()
    {
        OpenApiDocument document = Document;
        IOpenApiSchema pingResponse = document.Components!.Schemas!["PingResponse"];

        Assert.NotNull(pingResponse.Properties);

        // `authenticated: true` AS A CONSTANT, so a conformance test has something explicit to assert
        // on rather than inferring success from a status code alone.
        Assert.Equal("gateway", pingResponse.Properties["service"].Const);

        // MEASURED, NOT ASSUMED - AND THE CAPITAL LETTER IS NOT A TYPO.
        //
        // Microsoft.OpenApi 2.x exposes `const` as a STRING holding the value's rendered form, and the
        // rendering is .NET's rather than YAML's or JSON's. The document carries the YAML boolean
        // `true`; the reader parses it to a JSON boolean; `Const` renders that with
        // `Boolean.ToString()`, which produces "True". A numeric const comes back as its digits and a
        // string const comes back unchanged, so booleans are the only kind that changes shape in transit.
        //
        // This was measured against the real reader rather than inferred. The natural-looking assertion
        // here is "true" and it FAILS, so the comment exists to stop a future reader "correcting" this
        // line and rediscovering the same failure.
        Assert.Equal("True", pingResponse.Properties["authenticated"].Const);

        // AND NOTHING FROM THE TOKEN COMES BACK.
        //
        // A ping that echoed its own credential, or a claim of it, would turn an authentication probe
        // into a token-disclosure endpoint - and it would do so in the exact response a caller is most
        // likely to log verbatim.
        foreach (string property in pingResponse.Properties.Keys)
        {
            foreach (string marker in (string[])["token", "jwt", "bearer", "claim", "authorization", "sub", "key"])
            {
                Assert.False(
                    property.Contains(marker, StringComparison.OrdinalIgnoreCase),
                    $"PingResponse.{property} would echo credential material back to the caller.");
            }
        }

        Assert.False(pingResponse.AdditionalPropertiesAllowed);
    }

    [Fact]
    public void EveryAuthenticatedOperationPublishesItsUnauthorizedResponse()
    {
        OpenApiDocument document = Document;

        int checkedOperations = 0;

        foreach ((string route, HttpMethod method, OpenApiOperation operation) in Operations(document))
        {
            if (operation.Security is { Count: 0 })
            {
                continue;
            }

            // THE FOUR RESERVED ROUTES ARE THE ONE EXEMPTION, AND IT IS A C-D OBLIGATION RATHER THAN AN
            // OVERSIGHT.
            //
            // They are still authenticated - none of them overrides the document-level requirement, and
            // AReservedRouteRequiresATokenSoTheDeferredRosterIsNotAnonymouslyEnumerable asserts exactly
            // that - but their DECLARED response set is exactly {501}, because C-D permits a reserved
            // route to declare its 501 and its machine-readable body and nothing else. A second status
            // in the set would suggest the route evaluates something before answering, which is the
            // "stub them out" reading the requirements forbid. What an unauthenticated caller meets is
            // the authentication middleware, which is a cross-cutting concern declared once at the
            // security scheme; it is not a response the ROUTE produces.
            if (Extension(operation, "x-deferred-service") is not null)
            {
                continue;
            }

            // A CONSUMER CANNOT HANDLE AN UNDOCUMENTED STATUS.
            //
            // A generated client typically throws on any status absent from the contract, so an
            // operation that can return 401 but does not say so produces an unhandled exception rather
            // than a re-authentication attempt. Every authenticated operation that does real work can
            // return 401, so every one declares it.
            Assert.True(
                operation.Responses!.ContainsKey("401"),
                $"{method} {route} requires a token but does not declare a 401.");

            checkedOperations++;
        }

        // 50 operations, less the one anonymous /health and the eight reserved-route operations.
        Assert.Equal(41, checkedOperations);
    }

    // ==============================================================================================
    //  C-09 - THE CAPABILITY PROJECTION
    // ==============================================================================================

    [Fact]
    public void TheCapabilityReportNamesAllEightLegacyBitsWithTheirPreservedIdentifiers()
    {
        OpenApiDocument document = Document;
        IOpenApiSchema name = document.Components!.Schemas!["Capability"].Properties!["name"];

        string[] identifiers = name.Enum!.Select(static node => node!.GetValue<string>()).ToArray();

        // THE SPELLINGS ARE THE LEGACY'S, VERBATIM.
        //
        // These are the SCREAMING_SNAKE identifiers from
        // ws_objects/pfw.shared.pbl.src/enums.sru:L41-L49, kept in deliberate departure from .NET
        // naming convention because they appear in serialized payloads, log records and
        // characterization recordings - where a rename would silently invalidate every stored
        // comparison rather than failing loudly.
        Assert.Equal(
            [
                "INIT_FLAG_ENABLE_UI",
                "INIT_FLAG_ENABLE_SCITER",
                "INIT_FLAG_ENABLE_BLINK",
                "INIT_FLAG_ENABLE_BLINKFAST",
                "INIT_FLAG_ENABLE_ORCA",
                "INIT_FLAG_ENABLE_SQLITE",
                "INIT_FLAG_ENABLE_DPIAWARE",
                "INIT_FLAG_ENABLE_WEBVIEW",
            ],
            identifiers);

        IOpenApiSchema capabilities =
            document.Components.Schemas["CapabilityReport"].Properties!["capabilities"];
        Assert.Equal(8, capabilities.MinItems);
        Assert.Equal(8, capabilities.MaxItems);
    }

    [Fact]
    public void TheCapabilityMasksSpanTheFullUnsignedThirtyTwoBitDomain()
    {
        OpenApiDocument document = Document;
        IOpenApiSchema report = document.Components!.Schemas!["CapabilityReport"];

        // `allMask` IS DELIBERATELY NOT IN THIS LOOP - see the separate assertion below. It is a legacy
        // CONSTANT rather than a deployment-dependent value, so the document states it as a `const` and
        // a range would be strictly weaker.
        foreach (string maskProperty in (string[])["effectiveMask", "unrecognizedBits"])
        {
            IOpenApiSchema mask = report.Properties![maskProperty];

            // THE DOMAIN IS UNSIGNED 32-BIT, NOT SIGNED, AND THAT IS A DELIBERATE DECISION.
            //
            // Gateway projects the configured value with an UNCHECKED conversion to `uint`, so every
            // one of the 2^32 values is representable and a negative configured value WRAPS rather
            // than being rejected. The legacy flag word is an unsigned long, so rejecting a wrapped
            // value would make a configuration the legacy accepted fail to start - which would be a
            // behavioural change introduced by the refactor, not a preserved behaviour.
            //
            // Publishing 0..4294967295 rather than 0..2147483647 is what tells a consumer that. A
            // signed-32-bit bound here would have been the natural-looking choice and would have
            // contradicted the implementation.
            // NOTE ON THE COMPARISON TYPE: Microsoft.OpenApi 2.x models a JSON Schema numeric bound as a
            // STRING rather than a decimal, deliberately - JSON Schema numbers have no precision limit
            // and parsing them into a CLR numeric type would silently round a bound a document is
            // entitled to state exactly. So the bounds are compared as the text the document carries.
            Assert.Equal("0", mask.Minimum);
            Assert.Equal("4294967295", mask.Maximum);
        }
    }

    [Fact]
    public void TheAllMaskIsTheLegacySevenTermSumThatOmitsBlinkfast()
    {
        OpenApiDocument document = Document;
        IOpenApiSchema allMask =
            document.Components!.Schemas!["CapabilityReport"].Properties!["allMask"];

        // 3847, AND THE VALUE IS THE ASSERTION.
        //
        // ws_objects/pfw.shared.pbl.src/enums.sru:L49 declares INIT_FLAG_ENABLE_ALL as a SEVEN-TERM sum:
        // UI(1) + SCITER(2) + BLINK(4) + ORCA(256) + SQLITE(512) + DPIAWARE(1024) + WEBVIEW(2048) = 3847.
        // INIT_FLAG_ENABLE_BLINKFAST(8) is NOT one of the terms, so 3847 is NOT the bitwise union of the
        // eight declared bits - the union is 3855.
        //
        // THE OMISSION IS PRESERVED, NOT CORRECTED (C-B). blink.dll and blinkfast.dll are alternative
        // builds of ONE engine, so enabling both is meaningless. A future reader "tidying" this to 3855
        // would be making the framework's own initialization flag disagree with the framework, and this
        // assertion is what stops that being a silent change. pfw.sra:L91 initializes with exactly this
        // constant, so it is the runtime-effective capability set rather than a documented ideal.
        // NOTE ON THE COMPARISON TYPE: Microsoft.OpenApi 2.x models a JSON Schema `const` as a STRING,
        // for the same reason it models a numeric bound as one - a JSON Schema value has no precision
        // limit and parsing it into a CLR numeric type would silently round a value the document is
        // entitled to state exactly. So the constant is compared as the text the document carries.
        Assert.Equal("3847", allMask.Const);

        int union = 1 + 2 + 4 + 8 + 256 + 512 + 1024 + 2048;
        Assert.Equal(3855, union);
        Assert.Equal(8, union - 3847);

        // AND BLINKFAST IS STILL PRESENT AS A BIT IN ITS OWN RIGHT. It is excluded from the AGGREGATE,
        // not from the capability set - a consumer can still enable it explicitly, exactly as the legacy
        // permits.
        // NOTE ON THE CONVERSION: the OpenAPI YAML reader materializes a JSON Schema number as a
        // `decimal`, so the members are read as decimals and narrowed here rather than requested as
        // `long` directly - which throws.
        IOpenApiSchema value = document.Components.Schemas["Capability"].Properties!["value"];
        long[] values = value.Enum!.Select(static node => (long)node!.GetValue<decimal>()).ToArray();
        Assert.Equal([1L, 2L, 4L, 8L, 256L, 512L, 1024L, 2048L], values);
        Assert.Contains(8L, values);

        // THE NUMERIC SET AND THE IDENTIFIER SET LINE UP POSITIONALLY, which is what makes the pairing
        // convention meaningful rather than decorative.
        string[] names = value.Extensions!["x-enum-varnames"] is JsonNodeExtension varnames
            ? varnames.Node!.AsArray().Select(static node => node!.GetValue<string>()).ToArray()
            : [];
        Assert.Equal(values.Length, names.Length);
        Assert.Equal("INIT_FLAG_ENABLE_BLINKFAST", names[Array.IndexOf(values, 8L)]);
    }

    [Fact]
    public void OnlyTheSqliteCapabilityMapsToAnInScopeServiceInThisPhase()
    {
        OpenApiDocument document = Document;
        IOpenApiSchema destination =
            document.Components!.Schemas!["Capability"].Properties!["phaseOneDestination"];

        string[] destinations = destination.Enum!.Select(static node => node!.GetValue<string>()).ToArray();

        // MAPPING THE EIGHT BITS ONTO THE SERVICE ROSTER IS INDEPENDENT CORROBORATION THAT THE PHASE-1
        // SLICE IS DRAWN CORRECTLY.
        //
        // Of the eight, exactly one - SQLITE - has an in-scope consumer. UI and DPIAWARE are
        // DesignSystem; SCITER, BLINK, BLINKFAST and WEBVIEW are ScriptBridge; ORCA is packaging
        // tooling and not a service at all. Publishing the destination lets a consumer see that
        // enabling a deferred bit has no runtime effect in this phase, rather than discovering it.
        Assert.Equal(
            ["Persistence", "DesignSystem", "ScriptBridge", "PackagingTooling"],
            destinations);

        // AND ONLY ONE OF THOSE FOUR IS AN IN-SCOPE PHASE-1 SERVICE.
        string[] inScope = ["Gateway", "DataServices", "Persistence", "Security"];
        Assert.Equal(["Persistence"], destinations.Where(inScope.Contains).ToArray());
    }

    // ==============================================================================================
    //  C-09 - THE STATUS MAPPING, WHOSE CENTREPIECE IS Aborted -> 409
    // ==============================================================================================

    [Fact]
    public void TheUpdateOperationIsTheOnlyOneCarryingAConflictAndItProjectsAborted()
    {
        OpenApiDocument document = Document;

        (string Route, HttpMethod Method, OpenApiOperation Operation)[] conflicting = Operations(document)
            .Where(static entry => entry.Operation.Responses!.ContainsKey("409"))
            .ToArray();

        // EXACTLY ONE OPERATION CAN CONFLICT, AND IT IS THE UPDATE.
        //
        // The optimistic-concurrency check belongs to the update half of the retrieval/validation/update
        // triple and to nothing else. A 409 appearing on a read would mean the mapping had been applied
        // by habit rather than from the semantics.
        (string route, HttpMethod method, OpenApiOperation update) = Assert.Single(conflicting);
        Assert.Equal("/v1/datawindow/update", route);
        Assert.Equal(HttpMethod.Post, method);

        Assert.Equal("dataservices.v1.DataWindowService/Update", Extension(update, "x-grpc-method"));
    }

    [Fact]
    public void TheConflictResponseCarriesTheCurrentRowStateRatherThanABareStatus()
    {
        OpenApiDocument document = Document;
        IOpenApiResponse conflict = document.Components!.Responses!["Conflict"];

        Assert.NotNull(conflict.Content);
        Assert.True(conflict.Content.ContainsKey("application/problem+json"));

        IOpenApiSchema specialised = document.Components.Schemas!["ConflictProblemDetails"];

        // THE CONFLICT DETAIL IS WHAT MAKES THE 409 ACTIONABLE.
        //
        // The sole updatable DataWindow in the legacy estate carries `updatewhere=1` with all six
        // columns marked, so the concurrency check spans ALL SIX columns' original values
        // [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14]. A caller told only "409" cannot tell
        // which column moved, and cannot construct a retry - it would re-send the same stale original
        // values and receive the same 409 forever.
        //
        // So the response carries the CURRENT ROW STATE, forwarded unchanged from C-06 rather than
        // reshaped, because a caller deciding between retrying and surfacing needs it exactly as the
        // database reported it.
        Assert.NotNull(specialised.AllOf);
        Assert.NotEmpty(specialised.AllOf);

        bool carriesConflict = specialised.AllOf
            .Any(static part => part.Properties is not null && part.Properties.ContainsKey("conflict"));

        Assert.True(
            carriesConflict,
            "ConflictProblemDetails must carry the conflict detail; a bare 409 cannot be retried "
                + "against, because the caller has no way to learn the current row state.");
    }

    [Fact]
    public void EveryProjectedOperationDeclaresTheFullStatusSurfaceItsMappingCanProduce()
    {
        OpenApiDocument document = Document;

        (string Route, HttpMethod Method, OpenApiOperation Operation)[] projected = Operations(document)
            .Where(static entry => Extension(entry.Operation, "x-grpc-method") is not null)
            .ToArray();

        Assert.NotEmpty(projected);

        foreach ((string route, HttpMethod method, OpenApiOperation operation) in projected)
        {
            // EVERY PROJECTED OPERATION CAN FAIL IN TRANSIT, AND MUST SAY SO.
            //
            // 502 is the status that exists BECAUSE OF the decomposition: an in-process call cannot
            // fail in transit and a network call can. Handling that is required BY the transition
            // rather than being a robustness improvement layered on top - without it, the first
            // transient network fault surfaces as a defect the legacy could not have had, which is a
            // regression introduced by the refactor.
            Assert.True(
                operation.Responses!.ContainsKey("502"),
                $"{method} {route} projects a gRPC call, so it can fail in transit and must declare 502.");

            Assert.True(
                operation.Responses.ContainsKey("500"),
                $"{method} {route} must declare 500 for a projected gRPC Internal status.");

            Assert.True(
                operation.Responses.ContainsKey("403"),
                $"{method} {route} must declare 403 for a projected gRPC PermissionDenied status: a "
                    + "valid-but-insufficient credential is a different outcome from a missing one.");

            // THE THREE THAT WERE EMITTED AND UNDECLARED, and none of them depends on which method is
            // projected - which is exactly why declaring them per operation is checkable here.
            //
            // 429: a handle registry behind C-05..C-08 refusing to hold more work answers the legacy
            // E_BUSY code, which surfaces as ResourceExhausted on any call that needs a handle.
            // 503: an upstream answering that it is not currently serving - a different fact from 502,
            // which means no gRPC response arrived at all.
            // 504: the deadline this service sets on EVERY outbound call elapsing, so its expiry is an
            // ordinary outcome of a slow upstream rather than a hypothetical.
            Assert.True(
                operation.Responses.ContainsKey("429"),
                $"{method} {route} can be refused by a capacity ceiling, so it must declare 429.");

            Assert.True(
                operation.Responses.ContainsKey("503"),
                $"{method} {route} projects a gRPC call whose upstream can answer Unavailable, so it "
                    + "must declare 503.");

            Assert.True(
                operation.Responses.ContainsKey("504"),
                $"{method} {route} carries an outbound deadline, so it must declare 504.");
        }
    }

    [Fact]
    public void TheInternalErrorResponseCommitsToRedactingTheStatementText()
    {
        OpenApiDocument document = Document;
        IOpenApiResponse internalError = document.Components!.Responses!["InternalError"];

        // THIS IS A C-F OBLIGATION SURFACING IN A STATUS DESCRIPTION.
        //
        // The legacy `sqlsyntax` field carries the COMPLETE generated statement including interpolated
        // literal values - and `DisableBind=1` means the runtime does not use bind variables at all, so
        // those literals are real user data. The legacy logger performs NO redaction. Forwarding that
        // field verbatim through an ingress would publish row data to whoever reads an error response.
        //
        // The commitment is published in the contract rather than left to the implementation, so a
        // consumer knows the field is redacted and does not build a diagnostic flow that depends on
        // seeing the raw statement.
        Assert.NotNull(internalError.Description);
        Assert.Contains("redact", internalError.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheCrossSessionForeignReferenceLimitIsPublishedAsItsOwnStatus()
    {
        OpenApiDocument document = Document;

        Assert.True(document.Components!.Responses!.ContainsKey("CrossSessionReferenceBlocked"));

        // THE ONE DELIBERATELY NARROWED CONTRACT IN THE REFACTOR IS PUBLISHED, NOT HIDDEN.
        //
        // `foreignvardata.expsvc` [n_cst_dwsvc_columnexp.sru:L80-L83] is a LIVE OBJECT POINTER to
        // another DataWindow's expression service, and a pointer does not serialize. Cross-DataWindow
        // references are therefore supported only when both DataWindows are co-resident in one
        // expression session; anything wider is BLOCKED with a defined error rather than approximated.
        //
        // IT IS PROJECTED AS 400, NOT AS A STATUS OF ITS OWN, AND THAT CHOICE IS THE SPECIFICATION'S
        // RATHER THAN THIS DOCUMENT'S.
        //
        // A dedicated 422 is the intuitive reading - the request is well-formed and its arguments are
        // individually valid, so what fails is a semantic precondition of the topology. But the status
        // mapping in docs/CONTRACTS.md 12.1 is the specification for C-09 and it sanctions eight
        // statuses, not nine; and it already provides the mechanism for this need, because
        // `InvalidArgument` projects to 400 CARRYING THE ORIGINATING RetCode so a caller distinguishes
        // one rejection from another by reading the code. Adding a ninth status would have been the
        // document overriding its own specification for a case the specification already covers.
        //
        // So the outcome is identified from `retCode` and from the expression-error category
        // CATEGORY_FOREIGN_REFERENCE_BLOCKED, which ProtoDescriptorTests asserts exists.
        (string Route, HttpMethod Method, OpenApiOperation Operation)[] blocked = Operations(document)
            .Where(static entry =>
                entry.Operation.Responses!.TryGetValue("400", out IOpenApiResponse? response)
                && response.Description is not null
                && response.Description.Contains("BLOCKED", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(blocked);

        // NO STATUS OUTSIDE THE SANCTIONED SET APPEARS ANYWHERE.
        Assert.DoesNotContain(
            "422",
            Operations(document).SelectMany(static entry => entry.Operation.Responses!.Keys));

        // It must reach the operation that ADDS a foreign variable, and the calculation operations that
        // RESOLVE one - a reference can be accepted and later become unresolvable when its session ends.
        string[] routes = blocked.Select(static entry => entry.Route).ToArray();
        Assert.Contains("/v1/datawindow/expression/foreign-variables/add", routes);
        Assert.Contains("/v1/datawindow/expression/calc", routes);
        Assert.Contains("/v1/datawindow/expression/calc-item", routes);
    }

    [Fact]
    public void NoOperationPromisesAFourTwentyTwoInProseThatItsResponsesDoNotDeclare()
    {
        OpenApiDocument document = Document;

        // A PROSE PROMISE IS AS BINDING AS A RESPONSE KEY, AND THIS IS THE GAP THAT PROVED IT.
        //
        // The assertion above already forbids a literal "422" response key, and it passed while the
        // foreign-variable operation's own description told the reader, in bold, that a cross-session
        // reference "returns 422 with a defined error" and then explained why 422 rather than 400. The
        // machine surface said 400 and the human surface said 422. A consumer reads the description -
        // that is what a description is for - and branches on a status the runtime never returns, so the
        // handler for the outcome that actually occurs is the one that never runs.
        //
        // The check is deliberately narrow: it looks for the STATUS TOKEN, not for the digits. "422"
        // inside a larger number, a byte count or an identifier is not a status promise, so the sweep
        // matches the token with a word boundary and reports the operation that carries it.
        (string Route, HttpMethod Method, OpenApiOperation Operation)[] promising = Operations(document)
            .Where(static entry => MentionsStatusToken(entry.Operation.Summary, "422")
                || MentionsStatusToken(entry.Operation.Description, "422"))
            .ToArray();

        Assert.True(
            promising.Length == 0,
            "These operations mention the status 422 in prose while the document declares no 422 "
                + "response anywhere: "
                + string.Join(
                    ", ",
                    promising.Select(static entry => $"{entry.Method} {entry.Route}"))
                + ". The status mapping in docs/CONTRACTS.md 12.1 sanctions eight statuses and 422 is "
                + "not one of them; a cross-session foreign reference is a 400 carrying its own retCode "
                + "and the CATEGORY_FOREIGN_REFERENCE_BLOCKED category. Prose and responses have to "
                + "agree, because a consumer branches on whichever it read.");
    }

    [Fact]
    public void TheUnsignedSixtyFourBitMemberIsNotDeclaredWithASignedFormat()
    {
        OpenApiDocument document = Document;

        IOpenApiSchema anyValue = document.Components!.Schemas!["AnyValue"];
        Assert.NotNull(anyValue.Properties);

        IOpenApiSchema unsigned = anyValue.Properties["uint64Value"];
        IOpenApiSchema signed = anyValue.Properties["int64Value"];

        // SIGNEDNESS IS PART OF THE TYPE, NOT A LABEL ON IT.
        //
        // `AnyValue.uint64Value` mirrors a protobuf `uint64`, whose domain runs to
        // 18446744073709551615 - more than twice Int64.MaxValue. Declared `format: int64` a generator
        // emits a signed 64-bit model, and every value in the upper half of the legacy `unsignedlong`
        // domain is then rejected by validation or wrapped to a negative number. Silently, on the one
        // member that exists to carry a value the sender could not express as signed.
        Assert.Equal("uint64", unsigned.Format, StringComparer.Ordinal);
        Assert.Equal("int64", signed.Format, StringComparer.Ordinal);

        // AND THE DOMAIN IS STATED AS A BOUND, SO IT IS CHECKABLE WITHOUT TRUSTING THE FORMAT NAME.
        // The upper bound is UINT64 MAX, which is 9223372036854775807 more than Int64.MaxValue - the
        // exact span a signed declaration would have lost.
        Assert.Equal("0", unsigned.Minimum, StringComparer.Ordinal);
        Assert.Equal("18446744073709551615", unsigned.Maximum, StringComparer.Ordinal);

        // BOTH STILL ACCEPT THE STRING ENCODING THE CANONICAL PROTOBUF JSON MAPPING EMITS.
        foreach (IOpenApiSchema schema in (IOpenApiSchema[])[unsigned, signed])
        {
            Assert.NotNull(schema.Type);
            Assert.True(schema.Type.Value.HasFlag(JsonSchemaType.String));
            Assert.True(schema.Type.Value.HasFlag(JsonSchemaType.Integer));
        }
    }

    [Fact]
    public void TheProjectedEventStreamPublishesItsOrderingDisciplineRatherThanImplyingIt()
    {
        OpenApiDocument document = Document;

        // THE ENUMERATION ALONE IS NOT ENOUGH, WHICH IS WHY THIS TEST EXISTS BESIDE THE PROTO ONE.
        //
        // ProtoDescriptorTests already pins the two OrderingDiscipline members and their numbers. That
        // proves the vocabulary exists; it says nothing about whether a consumer of THIS document can
        // tell which discipline governs the stream it is reading - and the two disciplines have
        // OPPOSITE rules for an out-of-order arrival. Under SEQUENCED the token carries reorder
        // authority; under SYNCHRONOUS a gap is a hard error that fails the session. An implementation
        // applying the wrong rule either reorders a chain that must not be reordered or fails a stream
        // that may be, and both look correct from inside the consumer.
        //
        // So the discipline has to be readable from the projected message itself.
        IOpenApiSchema discipline = document.Components!.Schemas!["OrderingDiscipline"];
        Assert.Equal(
            ["ORDERING_DISCIPLINE_UNSPECIFIED", "ORDERING_DISCIPLINE_SYNCHRONOUS", "ORDERING_DISCIPLINE_SEQUENCED"],
            discipline.Enum!.Select(static node => node!.GetValue<string>()).ToArray());

        IOpenApiSchema stream = document.Components.Schemas["EventStreamResponse"];

        // AS A REQUIRED MEMBER, so it cannot be omitted by a server and defaulted by a consumer.
        Assert.NotNull(stream.Properties);
        Assert.True(stream.Properties.ContainsKey("discipline"));
        Assert.NotNull(stream.Required);
        Assert.Contains("discipline", stream.Required);

        // AND AS A SPECIFICATION EXTENSION ON THE SCHEMA, so a generated document and a reader of the
        // authored contract name the discipline in the same place with the same spelling.
        Assert.NotNull(stream.Extensions);
        Assert.True(
            stream.Extensions!.TryGetValue("x-ordering-discipline", out IOpenApiExtension? declared),
            "EventStreamResponse declares no x-ordering-discipline extension. The projected stream is "
                + "pattern (a) and the reader has to be able to see that from the schema.");

        JsonNodeExtension disciplineExtension = Assert.IsType<JsonNodeExtension>(declared);
        Assert.Equal(
            "ORDERING_DISCIPLINE_SEQUENCED",
            disciplineExtension.Node.GetValue<string>(),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Whether a block of contract prose mentions an HTTP status as a status, rather than as digits
    /// inside some larger number or identifier.
    /// </summary>
    /// <param name="prose">The summary or description to search. A null or empty value mentions nothing.</param>
    /// <param name="status">The three-digit status token to look for.</param>
    /// <returns><see langword="true"/> when the token appears delimited by non-digits.</returns>
    /// <remarks>
    /// Deliberately a token test rather than a substring test. A substring test would fire on a byte
    /// count, a line number or an identifier that happens to contain the digits, and a control that
    /// reports false positives gets relaxed rather than fixed.
    /// </remarks>
    private static bool MentionsStatusToken(string? prose, string status)
    {
        if (string.IsNullOrEmpty(prose))
        {
            return false;
        }

        for (int index = prose.IndexOf(status, StringComparison.Ordinal);
             index >= 0;
             index = prose.IndexOf(status, index + 1, StringComparison.Ordinal))
        {
            bool digitBefore = index > 0 && char.IsAsciiDigit(prose[index - 1]);
            int after = index + status.Length;
            bool digitAfter = after < prose.Length && char.IsAsciiDigit(prose[after]);

            if (!digitBefore && !digitAfter)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void TheStatusSurfaceStaysWithinTheSetTheSpecificationSanctions()
    {
        OpenApiDocument document = Document;

        // THE MAPPED STATUSES OF docs/CONTRACTS.md 12.1, PLUS EXACTLY ONE ADDITION.
        //
        // The mapping table covers the statuses C-03 and C-04 RETURN: 200, 400, 401, 403, 404, 409, 429,
        // 500, 501, 503 and 504. It does not cover the case where the call never reached them, so this
        // document adds 502 - and only 502 - for an unreachable upstream or a transit failure after the
        // retry policy is exhausted. That gap is real rather than an oversight in the specification: a
        // gRPC status mapping cannot describe the absence of a gRPC response.
        //
        // 429, 503 AND 504 ARE IN THE SET BECAUSE THE RUNTIME REALLY EMITS THEM, and they were absent
        // from it while it did. A capacity ceiling in a handle registry answers ResourceExhausted, an
        // upstream that is not serving answers Unavailable, and every outbound call now carries a
        // deadline whose expiry answers DeadlineExceeded. A status the runtime can produce and the
        // document does not declare is the same defect as its opposite, read from the other side: a
        // generated client has no branch for it.
        //
        // 503 IS ALSO SANCTIONED, BUT BY A DIFFERENT CONTRACT, AND THE DISTINCTION IS THE POINT.
        //
        // It is not a projected gRPC status at all - it is C-10's `Unhealthy` verdict on `/health`, and
        // C-10 is a separate contract with its own specification in docs/CONTRACTS.md 12.2. Reading the
        // C-09 mapping table as the whole status surface of this document would have been the mistake:
        // the document publishes TWO contracts, and each brings its own statuses.
        //
        // Asserting the closed set is what stops the surface growing by accretion. Every status is a
        // branch every generated client must handle, and one added without a specification change is a
        // divergence rather than a feature.
        string[] sanctioned =
        [
            // C-09's mapping table (docs/CONTRACTS.md 12.1).
            "200", "400", "401", "403", "404", "409", "429", "500", "501", "504",

            // The one addition, for a failure the mapping cannot describe: no gRPC response at all.
            "502",

            // Both C-09's `Unavailable` translation AND C-10's `Unhealthy` aggregate verdict
            // (docs/CONTRACTS.md 12.2) land here. The two are different responses on different routes -
            // a problem document on a projected operation, the aggregate report on /health - which is
            // why the document declares them through different response components.
            "503",
        ];

        foreach ((string route, HttpMethod method, OpenApiOperation operation) in Operations(document))
        {
            foreach (string status in operation.Responses!.Keys)
            {
                Assert.Contains(status, sanctioned);
            }
        }
    }

    // ==============================================================================================
    //  C-09 - THE PROJECTION'S CORRESPONDENCE TO C-03 AND C-04
    //         This is the section that keeps the document's own stated promise.
    // ==============================================================================================

    [Fact]
    public void EveryProtoMessageNamedByTheProjectionResolvesToARealGeneratedMessage()
    {
        OpenApiDocument document = Document;

        int checkedNames = 0;

        foreach ((string route, HttpMethod method, OpenApiOperation operation) in Operations(document))
        {
            foreach (string extensionName in (string[])["x-proto-request", "x-proto-response"])
            {
                string? fullName = Extension(operation, extensionName);
                if (fullName is null)
                {
                    continue;
                }

                // THIS IS THE ASSERTION gateway.v1.yaml PROMISES IN ITS OWN COMMENTS.
                //
                // The document delegates its payload shapes to the protocol definitions instead of
                // transcribing well over a hundred messages into JSON Schema - a transcription would
                // have been a second source of truth in a different language with nothing keeping the
                // two in step, and the first divergence would have been SILENT because nothing
                // compiles a YAML file against a .proto.
                //
                // The delegation is only defensible if it is checked. This is the check: every named
                // message must resolve against the generated descriptors. A message renamed or removed
                // in the proto now breaks the build's test run rather than misleading a consumer.
                MessageDescriptor? resolved = ResolveProtoMessage(fullName);

                Assert.True(
                    resolved is not null,
                    $"{method} {route} declares {extensionName}: {fullName}, which does not resolve to "
                        + "any message in common.v1, dataservices.v1 or persistence.v1. The projection's "
                        + "payload delegation is broken.");

                Assert.Equal(fullName, resolved!.FullName);
                checkedNames++;
            }
        }

        // 39 projected operations, each naming a request and a response.
        Assert.Equal(78, checkedNames);
    }

    [Fact]
    public void EveryProjectedOperationNamesAnRpcThatActuallyExistsAndPairsWithItsOwnMessages()
    {
        OpenApiDocument document = Document;

        FileDescriptor dataServices = DataservicesV1Reflection.Descriptor;
        int checkedOperations = 0;

        foreach ((string route, HttpMethod method, OpenApiOperation operation) in Operations(document))
        {
            string? grpcMethod = Extension(operation, "x-grpc-method");
            if (grpcMethod is null)
            {
                continue;
            }

            // `x-grpc-method` IS `<package>.<Service>/<Method>` - the same shape a gRPC path uses, so a
            // reader can match it against a trace without translating.
            string[] parts = grpcMethod.Split('/');
            Assert.Equal(2, parts.Length);

            string serviceFullName = parts[0];
            string methodName = parts[1];

            Assert.StartsWith("dataservices.v1.", serviceFullName, StringComparison.Ordinal);
            string serviceName = serviceFullName["dataservices.v1.".Length..];

            ServiceDescriptor? service = dataServices.FindTypeByName<ServiceDescriptor>(serviceName);
            Assert.True(service is not null, $"{route} names service '{serviceFullName}', which does not exist.");

            MethodDescriptor? rpc = service!.FindMethodByName(methodName);
            Assert.True(rpc is not null, $"{route} names RPC '{grpcMethod}', which does not exist.");

            // THE PAIRING IS CHECKED, NOT JUST THE EXISTENCE.
            //
            // A projection that named a real RPC but the wrong message types would look entirely
            // plausible and would generate a client that sends the wrong payload. Asserting that the
            // declared request and response ARE that RPC's input and output types is what makes the
            // mapping trustworthy rather than merely well-formed.
            Assert.Equal(rpc!.InputType.FullName, Extension(operation, "x-proto-request"));
            Assert.Equal(rpc.OutputType.FullName, Extension(operation, "x-proto-response"));

            // AND IT MUST NOT BE A CLIENT-TO-SERVER STREAM - see the dedicated test below for why.
            //
            // A SERVER stream IS projectable and two of them are projected. Its ordering is the trivial
            // one - the server produces a sequence, the client consumes it in order - so it has a
            // faithful request/response form: one request, and a response carrying the same chunks in
            // the same order. A CLIENT-streaming or BIDIRECTIONAL RPC has no such form: there is no
            // single request to send, and in the inverted case the callee calls back into the caller.
            Assert.False(
                rpc.IsClientStreaming,
                $"{route} projects client-streaming RPC '{grpcMethod}'. A client-to-server stream has no "
                    + "faithful REST projection and must not be projected.");

            // A PROJECTED SERVER STREAM MUST SAY SO, so a consumer knows the body is the whole sequence
            // rather than one message, and so the collection's element order is known to be significant.
            if (rpc.IsServerStreaming)
            {
                Assert.Equal("server", Extension(operation, "x-grpc-streaming"));
            }
            else
            {
                Assert.Null(Extension(operation, "x-grpc-streaming"));
            }

            checkedOperations++;
        }

        Assert.Equal(39, checkedOperations);
    }

    [Fact]
    public void EveryUnaryAndServerStreamingRpcOfCThreeAndCFourIsProjectedAndNoBidirectionalOneIs()
    {
        OpenApiDocument document = Document;

        string[] projectedRpcs = Operations(document)
            .Select(static entry => Extension(entry.Operation, "x-grpc-method"))
            .OfType<string>()
            .Select(static name => name.Split('/')[1])
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        FileDescriptor dataServices = DataservicesV1Reflection.Descriptor;

        List<MethodDescriptor> allRpcs = dataServices.Services
            .SelectMany(static service => service.Methods)
            .ToList();

        // PROJECTABLE = UNARY OR SERVER-STREAMING. The property that decides it is whether the RPC has
        // a single request and a determinate response sequence, not whether it streams at all.
        string[] projectable = allRpcs
            .Where(static rpc => !rpc.IsClientStreaming)
            .Select(static rpc => rpc.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        string[] serverStreaming = allRpcs
            .Where(static rpc => rpc.IsServerStreaming && !rpc.IsClientStreaming)
            .Select(static rpc => rpc.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        string[] bidirectional = allRpcs
            .Where(static rpc => rpc.IsClientStreaming)
            .Select(static rpc => rpc.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        // COMPLETENESS IN BOTH DIRECTIONS.
        //
        // A PARTIAL projection would be the worst outcome: a consumer would find most of the surface
        // over REST and have no way to know which parts were missing, so it would look like a bug in
        // their client rather than a boundary of the contract. Gateway is the SOLE ingress, so an
        // operation the gRPC contract publishes and this document omits is unreachable from outside the
        // cluster. Every unary AND every server-streaming RPC is projected.
        Assert.Equal(projectable, projectedRpcs);

        // THE TWO SERVER STREAMS ARE PROJECTED, AS ORDERED COLLECTIONS.
        //
        // `Retrieve` carries the buffer chunks of the retrieval third of the triple, and `EventStream`
        // carries the three events the expression engine declares on itself. Each projects to one
        // operation whose response is the same sequence the stream would have delivered, in the same
        // order and with the chunking contract intact - the chunk index, the final-chunk flag and the
        // cumulative row count all travel.
        Assert.Equal(["EventStream", "Retrieve"], serverStreaming);
        foreach (string name in serverStreaming)
        {
            Assert.Contains(name, projectedRpcs);
        }

        // AND THE THREE BIDIRECTIONAL STREAMS ARE EXCLUDED, DELIBERATELY.
        //
        // `EventChain` carries the item-change and validation chain, which is STRICTLY SYNCHRONOUS with
        // no reordering permitted: the validation-error handler READS AND CLEARS the result the
        // preceding item-change event stashed, so its behaviour is a function of the prior event's
        // return value. JSON over independent REST requests would lose both that ordering and the
        // TRI-VALUED typed veto - prevent-once, prevent-deep, continue - which collapses to a boolean
        // the moment it is flattened. `InvokeMethodChannel` and `TraceChannel` are additionally
        // INVERTED: the legacy expects the APPLICATION to implement the macro switch, so across a
        // boundary DataServices calls back INTO its client, and an inverted stream has no
        // request/response direction to project at all.
        //
        // THE SET IS ASSERTED RATHER THAN THE COUNT, so a stream added to either service fails here
        // with its own name in the message instead of an off-by-one - and it must fail, because a new
        // bidirectional stream is a new documented gap in the ingress and the document names its gaps
        // individually.
        Assert.Equal(["EventChain", "InvokeMethodChannel", "TraceChannel"], bidirectional);
        Assert.Empty(projectedRpcs.Intersect(bidirectional, StringComparer.Ordinal));
    }

    [Fact]
    public void TheProjectionReachesNoServiceOtherThanDataServices()
    {
        OpenApiDocument document = Document;

        // GATEWAY CALLS DATASERVICES AND SECURITY, AND NOTHING ELSE.
        //
        // The topology is layered and acyclic: Gateway -> DataServices, Gateway -> Security,
        // DataServices -> Persistence, Persistence -> Security's keys. Gateway reaching Persistence
        // DIRECTLY would flatten that and put SQL generation one hop from the ingress - so no
        // `/v1/datawindow` operation may project a `persistence.v1` RPC, and none does.
        foreach ((string route, HttpMethod method, OpenApiOperation operation) in Operations(document))
        {
            string? grpcMethod = Extension(operation, "x-grpc-method");
            if (grpcMethod is null)
            {
                continue;
            }

            Assert.DoesNotContain("persistence.v1", grpcMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("security.v1", grpcMethod, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryDataWindowRouteSitsUnderTheDeclaredPathPrefix()
    {
        OpenApiDocument document = Document;

        foreach ((string route, HttpMethod method, OpenApiOperation operation) in Operations(document))
        {
            if (Extension(operation, "x-grpc-method") is null)
            {
                continue;
            }

            // C-09 DECLARES THE FAMILY AS `/v1/datawindow/**`, AND THE TESTS' e2e FIXTURE HARDCODES
            // THAT PREFIX. A projected operation outside it would be unreachable through the
            // documented ingress surface.
            Assert.StartsWith("/v1/datawindow/", route, StringComparison.Ordinal);
        }
    }

    // ==============================================================================================
    //  THE FOUR RESERVED EXTENSION POINTS - C-D COMPLIANCE, ASSERTED FROM BOTH DIRECTIONS
    // ==============================================================================================

    [Fact]
    public void ExactlyFourReservedRoutesAreDeclaredAndTheyNameTheFourDeferredServices()
    {
        OpenApiDocument document = Document;

        (string Route, string Service)[] reserved = Operations(document)
            .Select(entry => (entry.Route, Service: Extension(entry.Operation, "x-deferred-service")))
            .Where(static entry => entry.Service is not null)
            .Select(static entry => (entry.Route, Service: entry.Service!))
            .Distinct()
            .OrderBy(static entry => entry.Route, StringComparer.Ordinal)
            .ToArray();

        // EIGHT OPERATIONS ACROSS FOUR ROUTES - `get` and `post` on each, and both name the same
        // deferred service, which is why the pairs are de-duplicated above before the set is compared.
        // Declaring both methods is what makes "nothing here is implemented" cover more than one verb:
        // each answers the same 501, neither accepts a request body, and every OTHER method on the route
        // answers the same way too.
        Assert.Equal(
            8,
            Operations(document)
                .Count(entry => Extension(entry.Operation, "x-deferred-service") is not null));

        // FOUR, AND EXACTLY FOUR.
        //
        // The eight-service target roster is four in-scope plus four deferred. A fifth reserved route
        // would mean a capability area nobody mapped; a third would mean one silently dropped from the
        // roster. docs/DEFERRED.md is the authoritative list and this is the wire agreeing with it.
        Assert.Equal(
            [
                ("/v1/design/{path}", "DesignSystem"),
                ("/v1/documents/{path}", "Documents"),
                ("/v1/integration/{path}", "Integration"),
                ("/v1/scripting/{path}", "ScriptBridge"),
            ],
            reserved);
    }

    [Fact]
    public void EveryReservedRouteReturnsNotImplementedAndNothingElseSucceeds()
    {
        OpenApiDocument document = Document;

        foreach ((string route, HttpMethod method, OpenApiOperation operation) in Operations(document))
        {
            if (Extension(operation, "x-deferred-service") is null)
            {
                continue;
            }

            Assert.True(operation.Responses!.ContainsKey("501"));

            // NO SUCCESS STATUS ANYWHERE ON A RESERVED ROUTE.
            //
            // This is the sharpest available statement that nothing is implemented behind it. A 2xx in
            // the response set - even one described as a placeholder - would tell a consumer the route
            // sometimes works, and would be exactly the "stub them out" outcome the requirements forbid.
            foreach (string status in operation.Responses.Keys)
            {
                Assert.False(
                    status.StartsWith('2'),
                    $"Reserved route {route} declares success status {status}. Nothing is implemented "
                        + "behind a reserved route, so it can never succeed.");
            }
        }
    }

    [Fact]
    public void TheReservedRouteBodyIsMachineReadableAndCarriesTheExactPhaseTwoMarker()
    {
        OpenApiDocument document = Document;
        IOpenApiSchema body = document.Components!.Schemas!["ReservedRouteBody"];

        Assert.NotNull(body.Properties);

        // MACHINE-READABLE, WHICH IS THE WHOLE POINT OF DECLARING THE ROUTES AT ALL.
        //
        // The requirement is that the shape of the eventual system be legible from the gateway's
        // contract while nothing is implemented behind it. A prose message would satisfy neither half:
        // a client could not branch on it, and a human would have to guess which service was meant.
        Assert.Equal("501", body.Properties["status"].Const);
        Assert.Equal("reserved for Phase 2", body.Properties["marker"].Const);

        // TWO MEMBERS NAME THE DEFERRED SERVICE, THEY CARRY THE IDENTICAL DOMAIN, AND BOTH ARE PINNED.
        //
        // `deferredService` is the unambiguous spelling: `service` already means the RESPONDING service
        // on PingResponse and an UPSTREAM service on UpstreamHealth, so a third meaning on the same word
        // makes a body ambiguous in exactly the place a client branches on it, and the name matches the
        // `x-deferred-service` extension each reserved operation carries.
        //
        // `service` IS RETAINED ANYWAY, AND ASSERTING IT IS THE POINT OF THIS PAIR. It is the member
        // this document published for v1 first. The revision that introduced `deferredService` also
        // removed it, which is a silent break of an unversioned wire member: every v1 consumer reading
        // `service` stopped seeing the deferred-service name while the version number went on promising
        // it had not changed. A better name is not a reason to break a published one - that is a v2
        // decision - so both are emitted with the same value and both are asserted, in both directions.
        foreach (string member in (string[])["service", "deferredService"])
        {
            Assert.Equal(
                ["DesignSystem", "Documents", "Integration", "ScriptBridge"],
                body.Properties[member].Enum!
                    .Select(static node => node!.GetValue<string>())
                    .ToArray());
        }

        // THE RETURN CODE IS THE LEGACY'S OWN, NOT A PARALLEL VOCABULARY.
        //
        // E_NO_IMPLEMENTATION is -2001 [ws_objects/pfw.shared.pbl.src/retcode.sru:L78], which is the same
        // value common.v1.RetCode.Value.E_NO_IMPLEMENTATION carries on the gRPC half of the boundary and
        // PowerFramework.Shared.Kernel.RetCode.E_NO_IMPLEMENTATION carries in process. A client that
        // already branches on retCode therefore handles a reserved route with the code it knows. Its
        // neighbour E_NO_SUPPORT (-2000) is a DIFFERENT statement and is deliberately not used.
        Assert.Equal("-2001", body.Properties["retCode"].Const);

        // AND THE GRPC HALF OF THE BOUNDARY CARRIES THE SAME NUMBER. Asserted against the generated
        // descriptor rather than against a literal, so a change on either side of the boundary fails here
        // instead of leaving the two halves quietly disagreeing.
        Assert.Equal(-2001, (long)RetCode.Types.Value.ENoImplementation);

        Assert.NotNull(body.Required);
        foreach (string required in (string[])["status", "service", "deferredService", "marker", "route", "retCode"])
        {
            Assert.Contains(required, body.Required);
        }

        // CLOSED, so an unrecognised field in a 501 body is a detectable error rather than ignored data.
        Assert.False(body.AdditionalPropertiesAllowed);

        // THE 501 IS `application/json`, NOT `application/problem+json`.
        //
        // Deliberate, and the one documented exception to the single-error-shape convention: a
        // problem-details body would not carry the deferred-service name and the marker as structured
        // members a client can branch on, which is the only reason the routes exist.
        IOpenApiResponse response = document.Components.Responses!["ReservedForPhaseTwo"];
        Assert.True(response.Content!.ContainsKey("application/json"));
        Assert.False(response.Content.ContainsKey("application/problem+json"));
    }

    [Fact]
    public void AReservedRouteIsAFamilyRatherThanASingleRoute()
    {
        OpenApiDocument document = Document;

        foreach (string route in (string[])
        [
            "/v1/design/{path}",
            "/v1/documents/{path}",
            "/v1/integration/{path}",
            "/v1/scripting/{path}",
        ])
        {
            IOpenApiPathItem pathItem = document.Paths[route];

            // THE CATCH-ALL PARAMETER IS WHAT MAKES `/v1/design/**` A FAMILY.
            //
            // docs/CONTRACTS.md 13 specifies each reserved entry as `/v1/<area>/**`, so the eventual
            // URL shape of the deferred service is legible now. A single fixed route would only reserve
            // one URL and would tell a reader nothing about the surface behind it.
            Assert.NotNull(pathItem.Parameters);
            IOpenApiParameter parameter = Assert.Single(pathItem.Parameters);
            Assert.Equal("path", parameter.Name);
            Assert.Equal(ParameterLocation.Path, parameter.In);
            Assert.True(parameter.Required);
        }
    }

    [Fact]
    public void AReservedRouteRequiresATokenSoTheDeferredRosterIsNotAnonymouslyEnumerable()
    {
        OpenApiDocument document = Document;

        foreach ((string route, HttpMethod method, OpenApiOperation operation) in Operations(document))
        {
            if (Extension(operation, "x-deferred-service") is null)
            {
                continue;
            }

            // AUTHENTICATED, DESPITE IMPLEMENTING NOTHING.
            //
            // It would be tempting to leave a route that only ever returns 501 anonymous, since it
            // exposes no capability. But the BODY names the deferred service and the roadmap marker, so
            // an anonymous reserved route publishes the system's Phase-2 plan to any unauthenticated
            // caller. Requiring a token costs nothing and keeps the roster inside the boundary.
            //
            // A NULL `security` MEMBER IS HOW THAT IS EXPRESSED: the operation inherits the
            // document-level bearer requirement rather than overriding it. An empty list would have made
            // it anonymous, which is why the distinction between null and empty is load-bearing here.
            Assert.Null(operation.Security);

            // AND THE DECLARED RESPONSE SET IS EXACTLY {401, 501} - THE C-D AUDIT, MADE EXECUTABLE.
            //
            // C-D permits a reserved route to declare its four paths, its not-implemented response and
            // the machine-readable body, and nothing else. What C-D constrains is what the route
            // IMPLEMENTS, and 501 is still the only outcome its handler produces - so the audit is that
            // there is NO 2xx and no projected-capability status here, not that the response map has one
            // entry.
            //
            // THE 401 IS DECLARED BECAUSE THE OPERATION REALLY RETURNS IT. `.RequireAuthorization()` is
            // applied to every reserved route, so an unauthenticated caller receives 401 before the
            // handler is reached. Declaring only 501 tells a generated client and a conformance tool
            // that this operation cannot answer 401, which is false about the deployed surface; the
            // token requirement asserted immediately above is precisely what makes it true. A
            // cross-cutting origin is a reason to declare the status ONCE as a shared component - which
            // is how `#/components/responses/Unauthorized` is used here - not a reason to omit it.
            string[] declared = operation.Responses!.Keys.OrderBy(
                static key => key,
                StringComparer.Ordinal).ToArray();

            Assert.Equal(["401", "501"], declared);

            // NO SUCCESS STATUS, WHICH IS THE PART THAT WOULD MEAN AN IMPLEMENTATION EXISTS.
            Assert.DoesNotContain(
                declared,
                static key => key.StartsWith('2') || key.StartsWith("2XX", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void NoReservedRouteProjectsAnyRpcOrNamesAnyImplementationTarget()
    {
        OpenApiDocument document = Document;

        foreach ((string route, HttpMethod method, OpenApiOperation operation) in Operations(document))
        {
            if (Extension(operation, "x-deferred-service") is null)
            {
                continue;
            }

            // A ROUTING DECLARATION IS NOT A STUB, AND THIS IS THAT STATED AS AN ASSERTION.
            //
            // The prohibition in C-D is on IMPLEMENTING the deferred services. A reserved route has no
            // handler beyond the constant response, calls nothing, and can reach nothing - so it names
            // NO gRPC method, NO request message and NO response message. If any of these extensions
            // ever appeared here, something behind the route would have been built.
            Assert.Null(Extension(operation, "x-grpc-method"));
            Assert.Null(Extension(operation, "x-proto-request"));
            Assert.Null(Extension(operation, "x-proto-response"));

            Assert.Equal("reserved for Phase 2", Extension(operation, "x-reserved-marker"));

            Assert.False(operation.Responses!.ContainsKey("409"));
            Assert.False(operation.Responses.ContainsKey("502"));
        }
    }

    [Fact]
    public void NoContractIdentifierForADeferredServiceAppearsAnywhereInTheDocument()
    {
        OpenApiDocument document = Document;

        string[] contractIds = Operations(document)
            .Select(static entry => Extension(entry.Operation, "x-contract-id"))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

        // THE TEN CONTRACTS ARE C-01..C-10 AND ALL TEN BELONG TO THE FOUR IN-SCOPE SERVICES.
        //
        // Gateway's own document touches four of them: C-09 for its ingress surface, C-10 for
        // readiness, and C-03/C-04 for the operations it projects. A C-11 or higher would mean a
        // contract nobody inventoried, which is precisely what the requirement to review the contract
        // inventory before code generation exists to prevent.
        Assert.Equal(["C-03", "C-04", "C-09", "C-10"], contractIds);
    }

    // ==============================================================================================
    //  DOCUMENT-WIDE STRUCTURE
    // ==============================================================================================

    [Fact]
    public void TheDocumentDeclaresFiftyOperationsAcrossFortySixRoutes()
    {
        OpenApiDocument document = Document;

        // ROUTES: 3 (health, ping, capabilities) + 39 (projected) + 4 (reserved) = 46.
        // OPERATIONS: the same 42, plus a second method on each reserved route = 50.
        //
        // The 39 is every unary AND every server-streaming RPC of C-03 and C-04: fifteen of C-03's
        // sixteen - retrieval, the five lifecycle/update/gate operations and the eight read-and-apply
        // operations of the four headless models - and twenty-four of C-04's twenty-six. The three
        // BIDIRECTIONAL streams are excluded and are named individually in
        // EveryUnaryAndServerStreamingRpcOfCThreeAndCFourIsProjectedAndNoBidirectionalOneIs.
        //
        // THE TWO NUMBERS DIFFER BY FOUR, AND THAT IS THE RESERVED ROUTES' SECOND METHOD. Every other
        // route carries exactly one operation; each reserved route carries `get` and `post`, both
        // answering the same 501 and neither accepting a body.
        //
        // The counts are asserted so a route added without a test, or removed without the documentation
        // being updated, fails here. It is the cheapest possible guard against the document and the
        // specification drifting apart, which is the failure that produced finding I-2 in the first
        // place - a contract documented as published while absent.
        Assert.Equal(46, document.Paths.Count);
        Assert.Equal(50, Operations(document).Count());
    }

    [Fact]
    public void EverySchemaExceptProblemDetailsIsClosedToUnknownMembers()
    {
        OpenApiDocument document = Document;

        int checkedSchemas = 0;

        foreach ((string name, IOpenApiSchema schema) in document.Components!.Schemas!)
        {
            // `ProblemDetails` MUST be open - RFC 9457 defines extension members and `retCode` is one.
            // `ConflictProblemDetails` composes it through allOf and inherits that openness.
            // `ProtoPayload` MUST be open - its authority is the .proto, so this placeholder cannot
            // enumerate its members without becoming the second source of truth it exists to avoid.
            if (name is "ProblemDetails" or "ConflictProblemDetails" or "ProtoPayload")
            {
                continue;
            }

            // A NON-OBJECT SCHEMA IS SKIPPED BECAUSE THE KEYWORD HAS NO MEANING ON ONE, NOT BECAUSE IT IS
            // EXEMPT FROM THE CONVENTION.
            //
            // `additionalProperties` constrains members of an OBJECT. A string enum - DwBuffer,
            // ItemStatus, ExpansionMode, DataWindowEventBit - and an array - RetrieveResult,
            // ExpressionEventStreamResult - have no members to constrain, so writing
            // `additionalProperties: false` on one would be inert noise that invites a reader to ask what
            // it is doing there. The convention is unchanged and every OBJECT schema below is still
            // required to close itself; the reader's default for an unstated keyword is "allowed", which
            // is why the check has to distinguish the two cases rather than treat the default as a
            // violation.
            if (schema.Type is not null && !schema.Type.Value.HasFlag(JsonSchemaType.Object))
            {
                continue;
            }

            // EVERY OTHER SCHEMA IS CLOSED.
            //
            // An open schema silently accepts a misspelled field, so a consumer sending `serivce`
            // instead of `service` gets a success with a missing value rather than a validation error.
            // Closing them makes that a detectable mistake.
            Assert.False(
                schema.AdditionalPropertiesAllowed,
                $"Schema '{name}' permits unknown members. Only ProblemDetails (RFC 9457 extension "
                    + "members) and ProtoPayload (authority delegated to the .proto) may.");

            checkedSchemas++;
        }

        // AND THE CHECK ACTUALLY REACHED SOMETHING. Without this, a change that made every schema
        // non-object - or misspelled the exemption list - would pass an empty loop silently.
        Assert.Equal(32, checkedSchemas);
    }

    [Fact]
    public void EveryTagUsedByAnOperationIsDeclaredWithAContractIdentifier()
    {
        OpenApiDocument document = Document;

        Assert.NotNull(document.Tags);

        var declared = document.Tags.ToDictionary(static tag => tag.Name!, StringComparer.Ordinal);

        foreach ((string route, HttpMethod method, OpenApiOperation operation) in Operations(document))
        {
            Assert.NotNull(operation.Tags);
            Assert.NotEmpty(operation.Tags);

            foreach (OpenApiTagReference tag in operation.Tags)
            {
                Assert.True(
                    declared.ContainsKey(tag.Name!),
                    $"{method} {route} uses undeclared tag '{tag.Name}'. An undeclared tag produces an "
                        + "unnamed, undescribed group in generated documentation and clients.");
            }
        }

        // AND EVERY DECLARED TAG CARRIES ITS CONTRACT IDENTIFIER, so a reader of any grouping can trace
        // it back to the entry in docs/CONTRACTS.md that specifies it.
        foreach (OpenApiTag tag in document.Tags)
        {
            Assert.NotNull(tag.Extensions);
            Assert.True(
                tag.Extensions.ContainsKey("x-contract-id"),
                $"Tag '{tag.Name}' does not declare x-contract-id.");
        }
    }

    [Fact]
    public void EveryDeclaredTagIsActuallyUsedByAnOperation()
    {
        OpenApiDocument document = Document;

        var used = Operations(document)
            .SelectMany(static entry => entry.Operation.Tags!)
            .Select(static tag => tag.Name!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (OpenApiTag tag in document.Tags!)
        {
            // AN UNUSED TAG IS A LEFTOVER, and it is usually the residue of a route that was renamed or
            // removed - which is a signal worth catching, because the route may have been removed
            // without the documentation following.
            Assert.Contains(tag.Name!, used);
        }
    }
}
