// ==================================================================================================
//  ProtoSerializationTests - THE CLAIMS gateway.v1.yaml MAKES ABOUT PAYLOAD ENCODING, PROVEN
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   The generated message types of common.v1, dataservices.v1 and persistence.v1, exercised
//            through actual serialization rather than inspected as descriptors.
//
//  WHY THIS FILE EXISTS SEPARATELY FROM ProtoDescriptorTests
//  ------------------------------------------------------------------------------------------------
//  ProtoDescriptorTests asserts what the contract DECLARES. This file asserts what it DOES when a
//  message is actually encoded - and the two are not the same claim.
//
//  The reason it matters here specifically: gateway.v1.yaml deliberately does NOT transcribe the
//  protobuf messages into JSON Schema. It delegates, stating that a /v1/datawindow body is "the
//  canonical protobuf JSON mapping" of the message named in its `x-proto-request` extension. That
//  delegation is the right decision - transcribing well over a hundred messages would create a second
//  source of truth in a different language with nothing keeping the two in step - but it is only
//  defensible if the delegated encoding is actually what the document says it is.
//
//  A consumer generating a client from gateway.v1.yaml will send `lowerCamelCase` JSON because that is
//  what canonical protobuf JSON specifies. If the service were to expect the `snake_case` spelling from
//  the .proto instead, every field would silently arrive unset - a request would succeed and do
//  nothing. Nothing in the build detects that, because the OpenAPI document and the .proto are compiled
//  by different toolchains and neither one references the other.
//
//  So: GatewayContractTests proves every `x-proto-*` name resolves to a real message. This file proves
//  that resolving it tells a consumer the truth about the bytes.
// ==================================================================================================

using Google.Protobuf;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Contracts.Persistence.V1;
using Xunit;

namespace PowerFramework.Contracts.Tests;

public sealed class ProtoSerializationTests
{
    // ==============================================================================================
    //  THE CANONICAL JSON MAPPING - the claim gateway.v1.yaml rests on
    // ==============================================================================================

    [Fact]
    public void CanonicalJsonRendersFieldNamesInLowerCamelCaseNotTheProtoSnakeCase()
    {
        var descriptor = new TransactionDescriptor
        {
            Dbms = "SQLite",
            Servername = "localhost",
            Database = "test.db",
            Logid = "pfw",
            Logpass = "not-a-real-secret-only-a-test-value",
            Dbparm = "DisableBind=1",
        };

        string json = JsonFormatter.Default.Format(descriptor);

        // THIS IS THE ASSERTION gateway.v1.yaml's PAYLOAD DELEGATION DEPENDS ON.
        //
        // Canonical protobuf JSON renders `servername` as `servername` (already single-word) but a
        // multi-word field like `update_table` as `updateTable`. A consumer that generated its client
        // from the OpenAPI document and read the .proto for the shapes would otherwise have to guess
        // which spelling the wire uses, and guessing wrong produces a request whose fields all arrive
        // unset - a silent no-op rather than an error.
        Assert.Contains("\"dbms\"", json, StringComparison.Ordinal);
        Assert.Contains("\"servername\"", json, StringComparison.Ordinal);

        // AND THE UNSET FIELDS ARE OMITTED, which is also canonical-JSON behaviour and also load-bearing:
        // it is why a partially-populated request is legal and why proto3 has no way to distinguish
        // "absent" from "default" on a scalar.
        Assert.DoesNotContain("\"lock\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AMultiWordFieldIsRenderedInLowerCamelCaseInCanonicalJson()
    {
        var detail = new ConflictDetail
        {
            UpdateTable = "COMPANY",
            RowsExpected = 1,
            RowsMatched = 0,
        };

        string json = JsonFormatter.Default.Format(detail);

        // `update_table` -> `updateTable`, `rows_expected` -> `rowsExpected`.
        //
        // This is the case that actually bites. A single-word field name looks identical in both
        // spellings, so a consumer testing only against `dbms` would conclude the two agree and then
        // fail on the first multi-word field. Asserting a multi-word field explicitly is what makes the
        // claim meaningful.
        Assert.Contains("\"updateTable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"rowsExpected\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("update_table", json, StringComparison.Ordinal);
        Assert.DoesNotContain("rows_expected", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalJsonRendersAnEnumByItsNameSoAStoredRecordingStaysReadable()
    {
        var row = new ConflictRow
        {
            Buffer = DwBuffer.Filter,
            Row = 3,
            ItemStatus = ItemStatus.NewModified,
        };

        string json = JsonFormatter.Default.Format(row);

        // ENUMS RENDER AS THEIR DECLARED NAMES, NOT THEIR NUMBERS, AND THAT IS WHY THE IDENTIFIER
        // PRESERVATION DISCIPLINE REACHES THE WIRE.
        //
        // The whole reason this refactor preserves legacy constant SPELLINGS verbatim - in deliberate
        // departure from .NET naming convention - is that they appear in serialized payloads, log records
        // and characterization recordings. This is that actually happening: a captured JSON body carries
        // `DW_BUFFER_FILTER`, so a stored recording is readable against the legacy source, and renaming
        // the constant would silently invalidate every stored comparison.
        Assert.Contains("DW_BUFFER_FILTER", json, StringComparison.Ordinal);
        Assert.Contains("ITEM_STATUS_NEW_MODIFIED", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TheItemChangeAlphabetSurvivesACanonicalJsonRoundTripByName()
    {
        // ALL FOUR VALUES, INCLUDING THE ZERO.
        //
        // Zero is the risk here. In proto3 a field set to its default is OMITTED from canonical JSON, so
        // `ITEM_CHANGE_RESULT_DEFAULT` (0) does not appear in the rendered body at all - and it must
        // still round-trip to 0 rather than to something else. An implementation that treated "absent"
        // as "unknown" would turn the legacy's default arm into an error.
        foreach (ItemChangeResult expected in Enum.GetValues<ItemChangeResult>())
        {
            var before = new EventResult { ItemChangeResult = expected };

            string json = JsonFormatter.Default.Format(before);
            EventResult after = JsonParser.Default.Parse<EventResult>(json);

            Assert.Equal(expected, after.ItemChangeResult);
        }

        // AND THE FOUR ARE MUTUALLY DISTINGUISHABLE, so the alphabet is not collapsed in transit.
        Assert.Equal(
            4,
            Enum.GetValues<ItemChangeResult>()
                .Select(static value => JsonParser.Default
                    .Parse<EventResult>(JsonFormatter.Default.Format(new EventResult { ItemChangeResult = value }))
                    .ItemChangeResult)
                .Distinct()
                .Count());
    }

    [Fact]
    public void EveryExpansionModeSurvivesARoundTripSoAPerReferenceModeIsNeverLost()
    {
        // THE MODE IS A PER-REFERENCE PROPERTY AND MUST SURVIVE INDIVIDUALLY.
        //
        // The legacy specification documents an expression mixing static and dynamic expansion, so one
        // payload carries references in DIFFERENT modes simultaneously. Losing or defaulting any single
        // one converts a dynamic binding into a static one - which produces a value that is correct on
        // the first calculation and stale on every subsequent one. That is the hardest possible defect to
        // notice, because the first observation is right.
        foreach (ExpansionMode mode in Enum.GetValues<ExpansionMode>())
        {
            var reference = new VarData { Name = "probe", ExpansionMode = mode };

            byte[] bytes = reference.ToByteArray();
            VarData after = VarData.Parser.ParseFrom(bytes);

            Assert.Equal(mode, after.ExpansionMode);
        }
    }

    [Fact]
    public void EveryVetoStateSurvivesARoundTripSoADeepPreventionStaysDeep()
    {
        // THE VETO IS CARRIED ON `EventResult`, IN TWO SEPARATE FIELDS.
        //
        // `Veto` itself is a message declaring only the nested enum - it holds no field of its own -
        // because a veto is never transmitted alone. It is always an outcome OF an event, and there are
        // TWO of them per event: the semantic handler's veto and the broker's, which the legacy chain
        // consults in that order and which can disagree. Collapsing them into one field would lose which
        // of the two prevented the event, and therefore whether a subscriber or the host said no.
        foreach (Veto.Types.Result expected in Enum.GetValues<Veto.Types.Result>())
        {
            var result = new EventResult { SemanticVeto = expected, BrokerVeto = expected };

            byte[] bytes = result.ToByteArray();
            EventResult after = EventResult.Parser.ParseFrom(bytes);

            Assert.Equal(expected, after.SemanticVeto);
            Assert.Equal(expected, after.BrokerVeto);
        }

        // AND THE TWO VETOES ARE INDEPENDENT: a semantic continue with a broker deep-prevention is a
        // representable and meaningful combination.
        var mixed = EventResult.Parser.ParseFrom(new EventResult
        {
            SemanticVeto = Veto.Types.Result.Continue,
            BrokerVeto = Veto.Types.Result.PreventDeep,
        }.ToByteArray());

        Assert.Equal(Veto.Types.Result.Continue, mixed.SemanticVeto);
        Assert.Equal(Veto.Types.Result.PreventDeep, mixed.BrokerVeto);

        // AND THE THREE STATES ARE MUTUALLY DISTINGUISHABLE AFTER A ROUND TRIP.
        //
        // Asserting each round-trips to itself is not quite enough - a serializer that mapped everything
        // to one value would still pass a per-value identity check if the comparison were loose. Three
        // distinct results after three round trips is the assertion that the tri-valued veto is not
        // flattened anywhere in the encoding.
        var results = Enum.GetValues<Veto.Types.Result>()
            .Select(static value => EventResult.Parser
                .ParseFrom(new EventResult { BrokerVeto = value }.ToByteArray()).BrokerVeto)
            .Distinct()
            .ToArray();

        Assert.Equal(3, results.Length);
    }

    // ==============================================================================================
    //  THE WRITE-ONLY PASSWORD, PROVEN AT THE ENCODING LEVEL
    // ==============================================================================================

    [Fact]
    public void TheResponseSideDescriptorCannotCarryAPasswordEvenWhenBuiltBesideOne()
    {
        // BUILT DELIBERATELY ADJACENT TO A POPULATED PASSWORD, which is the realistic mistake.
        //
        // An implementation copying a connection descriptor into a response view is exactly where a
        // password leaks, because the legacy's own `of_gettransdata` copies the whole structure INCLUDING
        // the password [n_cst_thread_trans.sru:L402-L419]. A faithful single-message port would echo it
        // on every query, update and command response.
        var request = new TransactionDescriptor
        {
            Dbms = "SQLite",
            Servername = "localhost",
            Database = "test.db",
            Logid = "pfw",
            Logpass = "SENTINEL-PASSWORD-MUST-NOT-APPEAR",
            Dbparm = "DisableBind=1",
            Lock = string.Empty,
        };

        // THE VIEW REFUSES `dbparm` AS WELL AS `logpass`, WHICH IS WHY THE COPY CANNOT BE MECHANICAL.
        //
        // `dbparm` is an opaque provider string that can itself carry a password or a whole connection
        // string, so slot 6 is permanently reserved on the view exactly as slot 5 is. The only part of it
        // a consumer needs is the closed, typed allowlist below - the two flags that change observable
        // statement generation - so the projection is a decision this contract makes rather than
        // whatever a caller happened to put in a connection string. Assigning `request.Dbparm` here
        // would not compile, which is the point.
        var view = new TransactionDescriptorView
        {
            Dbms = request.Dbms,
            Servername = request.Servername,
            Database = request.Database,
            Logid = request.Logid,
            Lock = request.Lock,
            Flags = new ConnectionParameterFlags { DisableBind = true },
        };

        string json = JsonFormatter.Default.Format(view);
        byte[] bytes = view.ToByteArray();

        // THE SENTINEL APPEARS IN NEITHER ENCODING, because the view HAS NO FIELD to put it in.
        //
        // This is the payoff of enforcing write-only structurally with two messages rather than by
        // convention with one. There is no property to assign, so the leak is a COMPILE error rather
        // than a review finding - and no amount of careless copying can reintroduce it. The same holds
        // for `dbparm`: neither name survives on the response side.
        Assert.DoesNotContain("SENTINEL-PASSWORD-MUST-NOT-APPEAR", json, StringComparison.Ordinal);
        Assert.DoesNotContain("logpass", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Logpass", json, StringComparison.Ordinal);
        Assert.DoesNotContain("dbparm", json, StringComparison.OrdinalIgnoreCase);

        string wire = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("SENTINEL-PASSWORD-MUST-NOT-APPEAR", wire, StringComparison.Ordinal);

        // AND THE NON-SECRET FIELDS DID SURVIVE, so this is not passing because the view is empty.
        Assert.Contains("localhost", json, StringComparison.Ordinal);
        Assert.Equal("pfw", view.Logid);
        Assert.True(view.Flags.DisableBind);
    }

    [Fact]
    public void TheAccountNameIsCarriedOnTheResponseBecauseItIsNotASecret()
    {
        var view = new TransactionDescriptorView { Logid = "pfw" };

        // `logid` IS AN ACCOUNT NAME, NOT A CREDENTIAL, AND THE DISTINCTION IS DELIBERATE.
        //
        // Removing it alongside the password would have been the over-cautious choice and the wrong one:
        // an operator diagnosing a connection failure needs to know WHICH account was used, and a view
        // that omitted it would force that question to be answered from configuration instead. The
        // secret is the password; the identity is not.
        Assert.Equal("pfw", view.Logid);
        Assert.Contains("\"logid\"", JsonFormatter.Default.Format(view), StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  BINARY ROUND-TRIPS OF THE PAYLOADS THAT CARRY THE HARDEST SEMANTICS
    // ==============================================================================================

    [Fact]
    public void AConflictDetailRoundTripsWithBothValueSetsIntact()
    {
        var detail = new ConflictDetail
        {
            UpdateTable = "COMPANY",
            RowsExpected = 1,
            RowsMatched = 0,
        };

        detail.Rows.Add(new ConflictRow
        {
            Buffer = DwBuffer.Primary,
            Row = 1,
            ItemStatus = ItemStatus.DataModified,
        });

        // NOTE THE SHAPE: a ColumnValue names its column and carries an `AnyValue`, not a bare string.
        //
        // The indirection is load-bearing. `AnyValue` is a discriminated union across eleven typed
        // alternatives - and crucially it separates `decimal_value` from `double_value`, because the
        // legacy `dec`/`decimal(n)` is NOT floating point and rendering a salary through a double would
        // introduce representation error into a concurrency comparison. It also carries `is_null`
        // explicitly, which the tri-state algebra depends on: PowerBuilder has null for value types, and
        // collapsing null to zero would convert "neither succeeded nor failed" into "succeeded".
        // The ordinal literals are written `5L` rather than `5`, and the suffix is deliberate rather than
        // decorative. `ColumnValue.column_id` is `int64` because the legacy produces the ordinal as
        // `Long(dwo.ID)`, so the generated property is a `long` and every sibling column identifier in the
        // contract folder is one too. Spelling the literal as a long is what makes a future narrowing back
        // to `int32` visible HERE, at a call site, rather than only as a cast somewhere downstream.
        detail.Rows[0].CurrentValues.Add(new ColumnValue
        {
            ColumnName = "salary",
            ColumnId = 5L,
            Value = new AnyValue { StringValue = "5000.00" },
        });

        detail.Rows[0].OriginalValues.Add(new ColumnValue
        {
            ColumnName = "salary",
            ColumnId = 5L,
            Value = new AnyValue { StringValue = "4000.00" },
        });

        byte[] bytes = detail.ToByteArray();
        ConflictDetail after = ConflictDetail.Parser.ParseFrom(bytes);

        // BOTH SETS MUST ARRIVE, AND THEY MUST STILL BE DISTINGUISHABLE.
        //
        // `updatewhere=1` means the generated where-clause carries the ORIGINAL values of every marked
        // column, so a caller reconstructing why an update matched no rows needs the originals AND the
        // currents side by side. A codec that merged them - or that dropped one because both are
        // `repeated ColumnValue` on the same message - would leave the conflict undiagnosable while
        // still round-tripping something plausible.
        Assert.Equal("COMPANY", after.UpdateTable);
        Assert.Equal(1, after.RowsExpected);
        Assert.Equal(0, after.RowsMatched);

        ConflictRow row = Assert.Single(after.Rows);
        Assert.Equal("4000.00", Assert.Single(row.OriginalValues).Value.StringValue);
        Assert.Equal("5000.00", Assert.Single(row.CurrentValues).Value.StringValue);
        Assert.NotEqual(
            Assert.Single(row.OriginalValues).Value.StringValue,
            Assert.Single(row.CurrentValues).Value.StringValue);

        // AND THE COLUMN IS IDENTIFIED BY BOTH NAME AND ONE-BASED ORDINAL, because the legacy addresses
        // columns both ways and a conflict must be resolvable by either. The expectation is typed `long`
        // explicitly, matching the field's int64 width.
        Assert.Equal("salary", Assert.Single(row.CurrentValues).ColumnName);
        Assert.Equal(5L, Assert.Single(row.CurrentValues).ColumnId);
    }

    [Fact]
    public void ADatabaseErrorRoundTripsWithItsStructuredFields()
    {
        var error = new DbError
        {
            Sqldbcode = 19,
            Sqlerrtext = "UNIQUE constraint failed: COMPANY.ID",
        };

        byte[] bytes = error.ToByteArray();
        DbError after = DbError.Parser.ParseFrom(bytes);

        // 19 IS SQLITE_CONSTRAINT, and the code travels alongside the text rather than only inside it.
        //
        // A caller branching on a constraint violation must not have to parse the message string - the
        // string is localized-adjacent, provider-specific and unstable, while the code is none of those.
        Assert.Equal(19, after.Sqldbcode);
        Assert.Equal("UNIQUE constraint failed: COMPANY.ID", after.Sqlerrtext);
    }

    [Fact]
    public void ABrokerTopicRoundTripsWithItsThreeEncodingsStillSeparate()
    {
        var topic = new BrokerTopic
        {
            Sequence = 0,
            Name = "itemchanged",
            Lifetime = BrokerTopic.Types.Lifetime.Persistent,
        };

        byte[] bytes = topic.ToByteArray();
        BrokerTopic after = BrokerTopic.Parser.ParseFrom(bytes);

        // THE THREE STAY SEPARATE ACROSS THE WIRE, WHICH IS THE ENTIRE POINT OF DECOMPOSING THEM.
        //
        // The legacy fuses ordering, identity and lifetime into one opaque string - the item-changed
        // topic is literally spelled with a leading "0-" and the broker's dispatch order derives from a
        // LEXICAL SORT of that string, while a `.^persistent` suffix encodes lifetime. Round-tripping
        // them as separate typed fields is what makes the ordering visible instead of hidden inside a
        // string with an undocumented grammar.
        //
        // Sequence 0 is the meaningful case: it is the item-changed topic's real ordering position, and
        // proto3 omits it from the encoding as a default - so it must still arrive as 0 rather than
        // becoming unknown.
        Assert.Equal(0, after.Sequence);
        Assert.Equal("itemchanged", after.Name);
        Assert.Equal(BrokerTopic.Types.Lifetime.Persistent, after.Lifetime);
    }

    [Fact]
    public void AColumnExpressionPayloadRoundTripsCarryingTheUnexpandedTextAndItsBindings()
    {
        var data = new ColumnExpData
        {
            Name = "total",
            Id = 6,
            ColType = ColumnExpData.Types.ColType.Decimal,
            Exp = "$$rate * salary",
            AlwaysCalc = true,
            Cacheable = false,
        };

        data.Vars.Add(new VarData { Name = "rate", ExpansionMode = ExpansionMode.Dynamic, Index = 1 });
        data.DupExps.Add(2);

        byte[] bytes = data.ToByteArray();
        ColumnExpData after = ColumnExpData.Parser.ParseFrom(bytes);

        // THE UNEXPANDED TEXT SURVIVES ALONGSIDE THE BINDING, which is what makes the static/dynamic
        // distinction reconstructable at the far end.
        //
        // Had the payload carried only an already-expanded string, `$$rate` would have been replaced by
        // its value in transit and a dynamic binding would have arrived indistinguishable from a
        // literal - the exact failure mode the requirements describe as "does not survive naive
        // serialization".
        Assert.Equal("$$rate * salary", after.Exp);
        Assert.Equal(ExpansionMode.Dynamic, Assert.Single(after.Vars).ExpansionMode);
        Assert.Equal("rate", Assert.Single(after.Vars).Name);

        // `id` IS A ONE-BASED COLUMN ORDINAL and must not be renormalized in transit. Six is the sixth
        // column, not the seventh - the one-based-to-zero-based hazard is the most dangerous mechanical
        // risk in this refactor and the wire is one more place it could be introduced.
        Assert.Equal(6, after.Id);

        // `dup_exps` SURVIVES, so the multiple-expressions-per-column mechanism is not lost.
        Assert.Equal(2, Assert.Single(after.DupExps));

        // AND `cacheable: false` IS STILL false RATHER THAN HAVING BECOME UNSET-MEANING-TRUE.
        Assert.True(after.AlwaysCalc);
        Assert.False(after.Cacheable);
    }

    [Fact]
    public void EveryReturnCodeValueSurvivesARoundTripIncludingTheNegativeSentinels()
    {
        // NEGATIVE ENUM VALUES ARE THE INTERESTING CASE.
        //
        // Protobuf encodes a negative enum as a varint of its two's-complement representation, which
        // costs ten bytes and - much more importantly - is exactly where a signedness mistake would
        // surface. The return-code algebra is almost entirely negative, and its SIGN is what every
        // caller's success/failure classification depends on: `IsSucceeded` tests `>= 0`. A code that
        // arrived with its sign flipped would be classified as a success.
        (string Name, RetCode.Types.Value Value)[] cases =
        [
            ("OK", RetCode.Types.Value.Ok),
            ("PREVENT", RetCode.Types.Value.Prevent),
            ("FAILED", RetCode.Types.Value.Failed),
            ("CANCELLED", RetCode.Types.Value.Cancelled),
            ("E_INVALID_ARGUMENT", RetCode.Types.Value.EInvalidArgument),
            ("E_RETRY", RetCode.Types.Value.ERetry),
            ("E_NO_SUPPORT", RetCode.Types.Value.ENoSupport),
            ("E_NO_IMPLEMENTATION", RetCode.Types.Value.ENoImplementation),
            ("UNKNOWN", RetCode.Types.Value.Unknown),
        ];

        foreach ((string name, RetCode.Types.Value value) in cases)
        {
            var response = new CloseValidationSessionResponse { RetCode = value };

            byte[] bytes = response.ToByteArray();
            CloseValidationSessionResponse after =
                CloseValidationSessionResponse.Parser.ParseFrom(bytes);

            Assert.Equal(value, after.RetCode);

            // AND THE SIGN IS PRESERVED, asserted separately from the value because the sign is the
            // property the tri-state predicates actually read.
            Assert.Equal(Math.Sign((int)value), Math.Sign((int)after.RetCode));
        }
    }

    [Fact]
    public void AnUnknownEnumNumberIsPreservedRatherThanCoercedToTheDefault()
    {
        // FORWARD COMPATIBILITY, AND IT IS LOAD-BEARING FOR AN INDEPENDENTLY-DEPLOYED SYSTEM.
        //
        // Four services deploy independently, so a newer Persistence can legitimately send a return code
        // that an older DataServices' generated enum does not know. Protobuf preserves the NUMBER in
        // that case rather than coercing it to the zero default - which matters enormously here, because
        // the zero default is `OK`. Coercion would turn an unrecognised FAILURE into a SUCCESS at a
        // service boundary during a rolling deployment.
        var response = new CloseValidationSessionResponse();

        // -9999 IS NOT A DECLARED RETURN CODE. Assigned by casting into the generated enum, exactly as an
        // unrecognised value arriving from a newer peer would materialise.
        response.RetCode = (RetCode.Types.Value)(-9999);

        byte[] bytes = response.ToByteArray();
        CloseValidationSessionResponse after = CloseValidationSessionResponse.Parser.ParseFrom(bytes);

        Assert.Equal(-9999, (int)after.RetCode);

        // AND CRUCIALLY IT DID NOT BECOME `OK`.
        //
        // This is the assertion that matters. The proto3 zero default for this field is OK (0), so a
        // serializer that discarded an unrecognised enum value would silently convert an unknown FAILURE
        // into a SUCCESS - during a rolling deployment, at a service boundary, with no error anywhere.
        Assert.NotEqual(RetCode.Types.Value.Ok, after.RetCode);
    }

    [Fact]
    public void AnUnknownFieldSurvivesARoundTripSoARollingDeploymentDoesNotLoseData()
    {
        // THE SAME CONCERN AT THE FIELD LEVEL.
        //
        // A newer peer may send a field this build's generated type does not declare. Protobuf retains it
        // in an unknown-field set and re-emits it on serialization, so a message passing THROUGH an older
        // intermediary arrives at its destination intact. That is what makes Gateway safe to deploy at a
        // different version from DataServices - which is the whole premise of independent deployability.
        var forward = new ConflictRow { Row = 7 };
        byte[] original = forward.ToByteArray();

        // Append an unknown field. The tag byte encodes the field number in its upper five bits and the
        // wire type in its lower three: field 99 with wire type 0 (varint) is (99 << 3) | 0 = 792, which
        // exceeds a single byte, so the tag itself is varint-encoded as two bytes - 0x98 0x06 - followed
        // by the value. Hand-encoding it is the point: the bytes must look exactly like those a newer
        // peer would emit, not like something this library produced.
        byte[] withUnknown = [.. original, 0x98, 0x06, 42];

        ConflictRow parsed = ConflictRow.Parser.ParseFrom(withUnknown);
        Assert.Equal(7, parsed.Row);

        byte[] reserialized = parsed.ToByteArray();

        // THE UNKNOWN FIELD IS STILL THERE after a parse-and-reserialize cycle.
        Assert.Equal(withUnknown.Length, reserialized.Length);
        Assert.Contains((byte)42, reserialized);
    }
}
