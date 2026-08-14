// ==================================================================================================
//  DataWindowCompositionTests.cs - THE PROVISIONED WIRING, ASSERTED
//  ------------------------------------------------------------------------------------------------
//  WHY THIS FILE EXISTS
//  Two production seams of this service are easily wired to implementations that refuse, and such a
//  refusal is defended in a comment rather than caught by a test - which is exactly how it survives.
//  These cases assert the wiring itself, so a regression to either shape fails the build:
//
//    1. THE MUTUAL-TLS CLIENT IDENTITY. Binding and validating the configured certificate pair is not
//       the same as PRESENTING it. Doing only the first two - never attaching the certificate to a
//       handler - leaves a deployment that mounted key material handshaking anonymously: configured,
//       validated, and inert (constraint C-G).
//    2. THE THREE HOST-BINDING SEAMS. Returning null from all three makes eight C-03 operations and the
//       whole of C-04 unreachable. AAP 0.2.1.3 Correction 3 and AAP 0.3.5 both require a bound headless
//       host in DataServices, so such a refusal is an AAP-compliance failure rather than a documented gap.
//    3. THE HOST'S OWN DESCRIBE SURFACE, EXERCISED THROUGH THE ENGINE THAT READS IT. A bound host is not
//       the same as a USABLE one: with the seams above provisioned, the whole of C-04 still refuses every
//       bind unless two further properties the ported engine reads off the host answer - a column's
//       ordinal and the positional `#n` address, the two most easily left out of a Describe surface. Every
//       C-04 case in this suite passed throughout, because each supplies its own host double which does
//       answer them. That is the shape this section exists to make impossible: a test double that is more
//       capable than the shipped host cannot catch a shipped host that is less capable than the engine
//       needs, so the cases below drive the REAL HeadlessDataWindowHost and nothing else.
//
//  WHAT IS NOT ASSERTED HERE, AND WHY. No case opens a socket or completes a TLS handshake. Proving that
//  a client certificate is presented on the wire needs a listener that requests one, which is an
//  orchestration-level check rather than a unit-level one. What IS provable here is the part that
//  breaks silently: that the identity is LOADED, that it is REACHABLE from the container, and that
//  unreadable configured material fails rather than being silently dropped.
// ==================================================================================================

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Grpc;
using PowerFramework.Shared.Localization;
using Xunit;

// NAMED EXPLICITLY RATHER THAN IMPORTING PowerFramework.Contracts.DataServices.V1 WHOLESALE, because that
// namespace publishes generated messages whose bare names collide with this project's own domain
// vocabulary - EventGate, EventId and ItemStatus among them - and a wholesale import makes several
// ambiguous (CS0104). Every other file in this suite names the contract types it needs the same way.
using EventNotification = PowerFramework.Contracts.DataServices.V1.EventNotification;
using WireEventResult = PowerFramework.Contracts.DataServices.V1.EventResult;

// THE KERNEL RETURN ALGEBRA, named explicitly for the same reason: PowerFramework.Contracts.Common.V1
// publishes a generated message ALSO called RetCode, so the bare name is ambiguous in this file. The
// alias keeps the ported member-access spelling - RetCode.OK, RetCode.E_INVALID_ARGUMENT - which is what
// the oracle's own `retcode.OK` reads as (AAP 0.4.5.3).
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Asserts that the composition root presents a configured client identity and binds a real headless
/// DataWindow.
/// </summary>
public sealed class DataWindowCompositionTests
{
    /// <summary>
    /// An unset mutual-TLS pair composes to an empty identity, which is a legitimate posture rather than
    /// a fault.
    /// </summary>
    /// <remarks>
    /// THE LOCAL BRING-UP PRESENTS NO CLIENT CERTIFICATE, and mutual TLS is the AAP's documented PER-PAIR
    /// fallback rather than the default (AAP 0.6.6.3) - the default is a bearer token minted by Security.
    /// So an empty collection has to resolve without throwing, or every developer bring-up would fail to
    /// start.
    /// </remarks>
    [Fact]
    public void AnUnsetMutualTlsPairComposesToAnEmptyIdentity()
    {
        using ServiceProvider provider = BuildClientProvider(certificatePath: null, keyPath: null);

        X509Certificate2Collection identity = provider.GetRequiredService<X509Certificate2Collection>();

        Assert.Empty(identity);
    }

    /// <summary>
    /// A configured mutual-TLS pair is loaded and is reachable from the container, which is what makes it
    /// presentable at all.
    /// </summary>
    /// <remarks>
    /// THIS IS THE CASE THE PREVIOUS SHAPE WOULD HAVE FAILED. There was no registration to resolve, so
    /// this line would not have compiled against it - and no test named the absence, which is how a
    /// bound-but-inert control reached a review. The private key assertion matters as much as the
    /// certificate one: a certificate without its key cannot complete a client handshake, so an identity
    /// that loaded only the public half would be just as inert as no identity at all.
    /// </remarks>
    [Fact]
    public void AConfiguredMutualTlsPairIsLoadedAndReachable()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"pfw-mtls-{Guid.NewGuid():n}");

        try
        {
            (string certificatePath, string keyPath) = WritePemPair(directory);

            using ServiceProvider provider = BuildClientProvider(certificatePath, keyPath);

            X509Certificate2Collection identity =
                provider.GetRequiredService<X509Certificate2Collection>();

            X509Certificate2 presented = Assert.Single(identity);

            Assert.Contains("PowerFramework", presented.Subject, StringComparison.Ordinal);
            Assert.True(presented.HasPrivateKey);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    /// <summary>
    /// A half-configured pair is refused with a diagnostic that names the configuration key and not the
    /// path.
    /// </summary>
    /// <remarks>
    /// BOTH HALVES OF THE ASSERTION ARE LOAD BEARING. The refusal is what turns an unusable identity into
    /// a startup failure rather than a silent anonymous handshake; the path assertion is the secrets
    /// obligation (constraint C-F) - a startup log must not record where a private key is mounted.
    /// </remarks>
    [Fact]
    public void AHalfConfiguredMutualTlsPairIsRefusedWithoutQuotingThePath()
    {
        using ServiceProvider provider = BuildClientProvider(
            certificatePath: "/mounted/secrets/dataservices-client.pem",
            keyPath: null);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            provider.GetRequiredService<X509Certificate2Collection>);

        Assert.Contains("half configured", refused.Message, StringComparison.Ordinal);
        Assert.Contains("MutualTls", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/mounted/secrets", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A configured pair that cannot be read fails at composition rather than at the first token request.
    /// </summary>
    [Fact]
    public void AnUnreadableMutualTlsPairFailsAtComposition()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"pfw-mtls-bad-{Guid.NewGuid():n}");

        try
        {
            _ = Directory.CreateDirectory(directory);

            string certificatePath = Path.Combine(directory, "client.pem");
            string keyPath = Path.Combine(directory, "client.key");

            // Present but not PEM, which is the realistic fault: a mount that carried the wrong file.
            File.WriteAllText(certificatePath, "this is not a certificate");
            File.WriteAllText(keyPath, "this is not a key");

            using ServiceProvider provider = BuildClientProvider(certificatePath, keyPath);

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
                provider.GetRequiredService<X509Certificate2Collection>);

            Assert.Contains("could not be loaded", refused.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(directory, refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    // ==============================================================================================
    //  THE THREE HOST-BINDING SEAMS
    // ==============================================================================================

    /// <summary>
    /// The host factory binds the evidenced DataWindow and retains one host per name.
    /// </summary>
    /// <remarks>
    /// RETENTION IS CONTRACT, NOT AN OPTIMISATION. C-04's expression session holds variables and bound
    /// expressions against a host, so a second resolve that produced a second host would silently lose
    /// every variable the first session had set.
    /// </remarks>
    [Fact]
    public void TheHostFactoryBindsTheEvidencedDataWindowAndRetainsIt()
    {
        HeadlessDataWindowHostFactory factory = new(new DataWindowCatalogue());

        DataWindowServiceHost? first = factory.Create(DataWindowCatalogue.SqliteFixtureName);
        DataWindowServiceHost? second = factory.Create(DataWindowCatalogue.SqliteFixtureName);

        Assert.NotNull(first);
        Assert.Same(first, second);

        // The transcribed table specification is readable through the published Describe surface, which
        // is how every ported path reads it.
        Assert.Equal("SELECT * FROM COMPANY", first.Describe("DataWindow.Table.Select"));
        Assert.Equal("COMPANY", first.Describe("DataWindow.Table.UpdateTable"));
        Assert.Equal("1", first.Describe("DataWindow.Table.UpdateWhere"));
        Assert.Equal("no", first.Describe("DataWindow.Table.UpdateKeyInPlace"));

        // The TRAILING SPACE is part of the oracle's literal [dw_sqlite.srd:L14] and is preserved.
        Assert.Equal("age A salary A ", first.Describe("DataWindow.Table.Sort"));

        // Six columns, numbered one to six DESPITE the six header text objects declared before them
        // [dw_sqlite.srd:L15-L26]. A host that numbered every object would be off by six here.
        Assert.Equal("6", first.Describe("DataWindow.Column.Count"));
        Assert.Equal(1L, first.GetObjectAttribute("id").ColumnId());
        Assert.Equal(6L, first.GetObjectAttribute("birth").ColumnId());
    }

    /// <summary>
    /// An unknown DataWindow name still resolves to nothing, so the published negative stays reachable.
    /// </summary>
    /// <remarks>
    /// THE NEGATIVE DID NOT DISAPPEAR WHEN THE WIRING WAS PROVISIONED - it moved to the input that earns
    /// it. The surface turns a null host into <c>RetCode.E_INVALID_HANDLE</c> for a model or chain request
    /// and into a failed open for an expression session, and that is published contract.
    /// </remarks>
    [Theory]
    [InlineData("d_never_transcribed")]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnknownDataWindowNameStillResolvesToNothing(string name)
    {
        HeadlessDataWindowHostFactory factory = new(new DataWindowCatalogue());

        Assert.Null(factory.Create(name));
    }

    /// <summary>
    /// The model-set provider builds all five models over one host and retains the set.
    /// </summary>
    /// <remarks>
    /// THE SHARED-HOST ASSERTION IS THE ONE THAT WOULD CATCH THE SUBTLE MIS-WIRING. Every model reads its
    /// event broker off its host - <c>#Eventful = dw.Eventful</c> [<c>n_cst_dwsvc.sru:L86</c>] - so two
    /// hosts here would split the broker in two and a subscription one model registered would be
    /// invisible to a topic another triggered.
    /// </remarks>
    [Fact]
    public void TheModelSetProviderBuildsFiveModelsOverOneHostAndRetainsTheSet()
    {
        HeadlessDataWindowHostFactory hosts = new(new DataWindowCatalogue());
        HeadlessDataWindowModelSetProvider provider = BuildModelSetProvider(hosts);

        DataWindowModelSet? first = provider.GetOrCreate(DataWindowCatalogue.SqliteFixtureName);
        DataWindowModelSet? second = provider.GetOrCreate(DataWindowCatalogue.SqliteFixtureName);

        Assert.NotNull(first);
        Assert.Same(first, second);

        Assert.NotNull(first.ContextMenu);
        Assert.NotNull(first.RowSelect);
        Assert.NotNull(first.ColumnSort);
        Assert.NotNull(first.DropDownSearch);

        // ONE host, shared by the set and by the factory that produced it.
        Assert.Same(hosts.Create(DataWindowCatalogue.SqliteFixtureName), first.Host);

        Assert.Null(provider.GetOrCreate("d_never_transcribed"));
    }

    /// <summary>
    /// The headless host carries a real, one-based row model with buffers, statuses and originals.
    /// </summary>
    /// <remarks>
    /// THE ORIGINALS ASSERTION IS WHY THIS CASE EXISTS. Under <c>updatewhere=1</c> the generated update
    /// predicate carries the ORIGINAL value of every marked column, so a host that held only current
    /// values could not produce a predicate at all - and a caller would silently overwrite a concurrent
    /// writer.
    /// </remarks>
    [Fact]
    public void TheHeadlessHostCarriesAOneBasedRowModelWithOriginals()
    {
        HeadlessDataWindowHost host = Assert.IsType<HeadlessDataWindowHost>(
            new HeadlessDataWindowHostFactory(new DataWindowCatalogue())
                .Create(DataWindowCatalogue.SqliteFixtureName));

        long row = host.AppendRow(
            DwBuffer.Primary,
            ItemStatus.NotModified,
            1L,
            "Ada Lovelace",
            36L,
            "London",
            92500m,
            new DateOnly(1815, 12, 10));

        // ONE-BASED, so the first row is 1 rather than 0 (hazard R9).
        Assert.Equal(1L, row);
        Assert.Equal(1L, host.RowCount());

        Assert.Equal("Ada Lovelace", host.GetItemString(1L, "name"));
        Assert.Equal(92500m, host.GetItemDecimal(1L, "salary"));
        Assert.Equal(new DateOnly(1815, 12, 10), host.GetItemDate(1L, "birth"));

        // The retrieval baseline is in place, so an original exists to build a predicate from.
        Assert.Equal(92500m, host.GetItemOriginalValue(1L, 5L, DwBuffer.Primary));

        // A write changes the CURRENT value and leaves the ORIGINAL alone, which is the whole mechanism.
        Assert.Equal(HeadlessDataWindowHost.Success, host.SetItem(1L, 5L, 95000m));
        Assert.Equal(95000m, host.GetItemDecimal(1L, "salary"));
        Assert.Equal(92500m, host.GetItemOriginalValue(1L, 5L, DwBuffer.Primary));

        // Null stays null rather than collapsing to zero or to an empty string.
        Assert.Equal(HeadlessDataWindowHost.Success, host.SetItem(1L, 4L, (string?)null));
        Assert.Null(host.GetItemString(1L, "address"));

        // Column zero addresses the ROW, which is what makes it the row-status probe.
        Assert.Equal(
            HeadlessDataWindowHost.Success,
            host.SetItemStatus(1L, 0L, DwBuffer.Primary, ItemStatus.DataModified));
        Assert.Equal(ItemStatus.DataModified, host.GetItemStatus(1L, 0L, DwBuffer.Primary));

        // A row status does NOT cascade to its columns, and that asymmetry is PowerBuilder's.
        Assert.Equal(ItemStatus.NotModified, host.GetItemStatus(1L, 5L, DwBuffer.Primary));

        // An out-of-range ordinal answers failure rather than throwing.
        Assert.Equal(HeadlessDataWindowHost.Failure, host.SetRow(2L));
        Assert.Equal(HeadlessDataWindowHost.Failure, host.SetItem(1L, 99L, "out of range"));
    }

    /// <summary>
    /// Deleting a row moves it into the Delete buffer, and a never-stored row is discarded instead.
    /// </summary>
    /// <remarks>
    /// THE DELETE BUFFER IS WHAT GENERATES A DELETE STATEMENT, so discarding a stored row would make the
    /// deletion invisible to the next update and the row would silently survive in storage. A
    /// <c>NewModified</c> row has no stored row to delete, which is why it is dropped outright.
    /// </remarks>
    [Fact]
    public void DeletingAStoredRowBuffersItAndDeletingANewRowDiscardsIt()
    {
        HeadlessDataWindowHost host = Assert.IsType<HeadlessDataWindowHost>(
            new HeadlessDataWindowHostFactory(new DataWindowCatalogue())
                .Create(DataWindowCatalogue.SqliteFixtureName));

        _ = host.AppendRow(DwBuffer.Primary, ItemStatus.NotModified, 1L, "stored");

        Assert.Equal(HeadlessDataWindowHost.Success, host.DeleteRow(1L));
        Assert.Equal(0L, host.RowCount());
        Assert.Equal(1L, host.RowCountOf(DwBuffer.Delete));

        long inserted = host.InsertRow(0L);

        Assert.Equal(1L, inserted);
        Assert.Equal(ItemStatus.NewModified, host.GetItemStatus(1L, 0L, DwBuffer.Primary));

        Assert.Equal(HeadlessDataWindowHost.Success, host.DeleteRow(1L));
        Assert.Equal(0L, host.RowCount());

        // Still one, because the never-stored row was discarded rather than buffered.
        Assert.Equal(1L, host.RowCountOf(DwBuffer.Delete));
    }

    /// <summary>
    /// The installed sort reorders numerically and preserves the selection across the reorder.
    /// </summary>
    /// <remarks>
    /// SELECTION IS KEYED ON ROW IDENTITY RATHER THAN ON ORDINAL, which is why it survives. Keying on the
    /// ordinal would silently move the selection onto a different row after a sort - a defect only a
    /// sorted fixture would reveal, and the evidenced definition sorts on two numeric columns.
    /// </remarks>
    [Fact]
    public void SortingReordersNumericallyAndKeepsTheSelectionOnItsRow()
    {
        HeadlessDataWindowHost host = Assert.IsType<HeadlessDataWindowHost>(
            new HeadlessDataWindowHostFactory(new DataWindowCatalogue())
                .Create(DataWindowCatalogue.SqliteFixtureName));

        _ = host.AppendRow(DwBuffer.Primary, ItemStatus.NotModified, 1L, "nine", 9L);
        _ = host.AppendRow(DwBuffer.Primary, ItemStatus.NotModified, 2L, "ten", 10L);
        _ = host.AppendRow(DwBuffer.Primary, ItemStatus.NotModified, 3L, "one", 1L);

        Assert.Equal(HeadlessDataWindowHost.Success, host.SelectRow(2L, select: true));
        Assert.Equal(HeadlessDataWindowHost.Success, host.SetSort("age A"));
        Assert.Equal(HeadlessDataWindowHost.Success, host.Sort());

        // NUMERIC, not textual: a text comparison would order 10 before 9.
        Assert.Equal("one", host.GetItemString(1L, "name"));
        Assert.Equal("nine", host.GetItemString(2L, "name"));
        Assert.Equal("ten", host.GetItemString(3L, "name"));

        // The selection followed its row from ordinal 2 to ordinal 3.
        Assert.True(host.IsSelected(3L));
        Assert.False(host.IsSelected(2L));

        // Zero is the documented wildcard, and it is what the ported "clear the selection" call uses.
        Assert.Equal(HeadlessDataWindowHost.Success, host.SelectRow(0L, select: false));
        Assert.Equal(0L, host.GetSelectedRow(0L));
    }

    /// <summary>
    /// Describe answers its three sentinels distinctly, and never collapses one into another.
    /// </summary>
    /// <remarks>
    /// <c>n_cst_dwsvc.sru:L850</c> PROBES A HEADER BAND BY TESTING THE INVALID-EXPRESSION SENTINEL
    /// EXACTLY, so answering an empty string for an unknown object would make that probe report a band
    /// that does not exist. This case pins the difference.
    /// </remarks>
    [Fact]
    public void DescribeKeepsItsSentinelsDistinct()
    {
        DataWindowServiceHost? resolved = new HeadlessDataWindowHostFactory(new DataWindowCatalogue())
            .Create(DataWindowCatalogue.SqliteFixtureName);

        Assert.NotNull(resolved);

        DataWindowServiceHost host = resolved;

        // An attribute the object carries and HAS been given.
        Assert.Equal("number", host.Describe("id.coltype"));
        Assert.Equal("yes", host.Describe("id.key"));
        Assert.Equal("no", host.Describe("name.key"));

        // An attribute the object carries but has NOT been given: the empty string.
        Assert.Equal(string.Empty, host.Describe("id.editstyle"));

        // An object that does not exist, and a group band no definition declares: the sentinel.
        Assert.Equal(
            HeadlessDataWindowHost.InvalidExpressionSentinel,
            host.Describe("no_such_object.coltype"));
        Assert.Equal(
            HeadlessDataWindowHost.InvalidExpressionSentinel,
            host.Describe("DataWindow.Header.1.Height"));

        // A malformed expression with no separator at all.
        Assert.Equal(HeadlessDataWindowHost.InvalidExpressionSentinel, host.Describe("nodot"));
    }

    /// <summary>
    /// Describe answers a column's ONE-BASED ordinal for the <c>ID</c> property.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS ONE PROPERTY IS THE GATE ON THE WHOLE OF C-04.</b> Binding an expression begins by
    /// resolving the target column's ordinal exactly this way -
    /// <c>Long(#DataWindow.Describe(colname + ".ID"))</c>
    /// [<c>n_cst_dwsvc_columnexp.sru:L1541-L1542</c>] - and the oracle reads a zero there as "no such
    /// column" and refuses the bind. While the property went unanswered, Describe returned the
    /// invalid-expression sentinel, <c>Long</c> of that is zero, and so EVERY bind against the shipped
    /// host answered <c>E_INVALID_ARGUMENT</c>: the expansion engine, the calculation chain, macro
    /// invocation and the trace were all unreachable in the deployed service while every C-04 case in
    /// this suite passed against a double that answered the property.
    /// </para>
    /// <para>
    /// THE ORDINAL COUNTS COLUMNS ONLY. This definition declares six header text objects BEFORE its first
    /// column [<c>dw_sqlite.srd:L15-L20</c>], and they consume no column number - so <c>id</c> is 1 rather
    /// than 7. A non-column answers <c>"0"</c> rather than the sentinel, because the ported readers do
    /// arithmetic on the value; an object the definition does not declare at all still answers the
    /// sentinel, so "no column number" and "no such object" stay distinguishable.
    /// </para>
    /// </remarks>
    [Fact]
    public void DescribeAnswersTheColumnOrdinalForTheIdPropertyWhichIsWhatEveryBindReads()
    {
        DataWindowServiceHost host = new HeadlessDataWindowHostFactory(new DataWindowCatalogue())
            .Create(DataWindowCatalogue.SqliteFixtureName)!;

        // dw_sqlite.srd:L21-L26 - the six columns in declaration order, and nothing else is numbered.
        Assert.Equal("1", host.Describe("id.ID"));
        Assert.Equal("2", host.Describe("name.ID"));
        Assert.Equal("3", host.Describe("age.ID"));
        Assert.Equal("4", host.Describe("address.ID"));
        Assert.Equal("5", host.Describe("salary.ID"));
        Assert.Equal("6", host.Describe("birth.ID"));

        // PowerBuilder's property names are case insensitive, and the ported readers spell this one both
        // ways - ".ID" at n_cst_dwsvc_columnexp.sru:L1541 against ".id" elsewhere in the estate.
        Assert.Equal("5", host.Describe("salary.id"));
        Assert.Equal("5", host.Describe("SALARY.Id"));

        // A DECLARED NON-COLUMN: zero, which is what dwo.ID already answers for the same object.
        Assert.Equal("0", host.Describe("salary_t.ID"));
        Assert.Equal("0", host.Describe("compute_1.ID"));

        // AN UNDECLARED OBJECT: still the sentinel, so absence has not been collapsed into "no ordinal".
        Assert.Equal(
            HeadlessDataWindowHost.InvalidExpressionSentinel,
            host.Describe("no_such_object.ID"));

        // ⚠ THE REGRESSION GUARD. Long("!") is zero and zero is the oracle's own "no such column", so a
        // host that answered the sentinel here would refuse every bind while looking perfectly healthy.
        Assert.NotEqual(HeadlessDataWindowHost.InvalidExpressionSentinel, host.Describe("salary.ID"));
    }

    /// <summary>
    /// The positional <c>#n</c> form addresses exactly the column its declared name does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE <c>#n</c> FORM IS THE ONLY WAY THE PORTED SERVICE LAYER ADDRESSES A COLUMN BY ORDINAL.</b>
    /// <c>_of_getdwobject(readonly long colnum)</c> is one line composing <c>"#" + String(colNum)</c> and
    /// handing it to the by-name resolver [<c>n_cst_dwsvc.sru:L97</c>]; the same convention reappears as
    /// <c>"#" + String(colNum) + ".Name"</c> [<c>:L666</c>] and across the estate's query task, which reads
    /// <c>#n.Name</c>, <c>#n.DDDW.Name</c> and <c>#n.DDDW.AutoRetrieve</c>
    /// [<c>n_cst_thread_task_sqlquery.sru:L117-L119</c>]. A host that answered only declared names reported
    /// "no such object" for every ordinal-addressed lookup while the name-addressed lookup for the SAME
    /// column succeeded, so the failure appeared only on the paths that count columns rather than name them.
    /// </para>
    /// <para>
    /// THE ORDINAL IS ONE-BASED AND IS NEVER REBASED (hazard R9), and a value outside <c>1..count</c>
    /// resolves to nothing rather than clamping. Only digits parse, so the three shapes a careless
    /// composition produces - a thousands separator, a sign, a decimal point - all resolve to nothing
    /// rather than to a column.
    /// </para>
    /// </remarks>
    [Fact]
    public void PositionalOrdinalAddressingResolvesTheSameObjectAsItsDeclaredName()
    {
        DataWindowServiceHost host = new HeadlessDataWindowHostFactory(new DataWindowCatalogue())
            .Create(DataWindowCatalogue.SqliteFixtureName)!;

        // n_cst_dwsvc.sru:L666 - the exact expression the ported reader composes.
        Assert.Equal("id", host.Describe("#1.Name"));
        Assert.Equal("name", host.Describe("#2.Name"));
        Assert.Equal("salary", host.Describe("#5.Name"));
        Assert.Equal("birth", host.Describe("#6.Name"));

        // The positional address and the declared name are the SAME object, property for property.
        Assert.Equal(host.Describe("salary.coltype"), host.Describe("#5.coltype"));
        Assert.Equal(host.Describe("salary.dbname"), host.Describe("#5.dbname"));
        Assert.Equal("5", host.Describe("#5.ID"));

        // n_cst_dwsvc.sru:L97 - _of_getdwobject(5) composes "#5" and resolves it through this surface.
        IDataWindowObject positional = host.GetObjectAttribute("#5");

        Assert.Equal("salary", positional.Name);
        Assert.Equal(5L, positional.ID);
        Assert.Equal("decimal(2)", positional.ColType);

        // OUT OF RANGE RESOLVES TO NOTHING. Zero addresses the ROW rather than a column, and seven is past
        // the last one - neither clamps to a real column, which is what would make an off-by-one silent.
        Assert.Equal(HeadlessDataWindowHost.InvalidExpressionSentinel, host.Describe("#0.Name"));
        Assert.Equal(HeadlessDataWindowHost.InvalidExpressionSentinel, host.Describe("#7.Name"));
        Assert.Equal(
            HeadlessDataWindowHost.InvalidExpressionSentinel,
            host.GetObjectAttribute("#7").ColType);

        // MALFORMED FORMS RESOLVE TO NOTHING RATHER THAN TO COLUMN ONE.
        Assert.Equal(HeadlessDataWindowHost.InvalidExpressionSentinel, host.Describe("#.Name"));
        Assert.Equal(HeadlessDataWindowHost.InvalidExpressionSentinel, host.Describe("#+1.Name"));
        Assert.Equal(HeadlessDataWindowHost.InvalidExpressionSentinel, host.Describe("#1,234.Name"));
        Assert.Equal(HeadlessDataWindowHost.InvalidExpressionSentinel, host.Describe("#abc.Name"));

        // A DECLARED NAME WINS OVER THE POSITIONAL READING OF THE SAME TEXT, which is PowerBuilder's own
        // precedence and is why the form is resolved as a NAME COMPOSITION rather than as a number.
        DataWindowDefinition collides = new("d_collides");
        _ = collides.AddColumn("first", "long");
        _ = collides.AddColumn("#1", "char(10)");

        DataWindowCatalogue catalogue = new();
        catalogue.Register(collides);

        DataWindowServiceHost colliding = new HeadlessDataWindowHostFactory(catalogue)
            .Create("d_collides")!;

        // "#1" is the SECOND column here, because the definition declares an object actually called that.
        Assert.Equal("char(10)", colliding.Describe("#1.coltype"));
        Assert.Equal("2", colliding.Describe("#1.ID"));
    }

    /// <summary>
    /// The expression engine binds, expands and calculates over the SHIPPED host rather than a double.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE CASE THAT WOULD HAVE CAUGHT THE LIVE-COMPOSITION GAP.</b> Every other C-04 case in
    /// this project supplies its own host double, and a double that answers more properties than the
    /// shipped host cannot detect a shipped host that answers fewer than the engine reads. So this case
    /// deliberately uses no double at all: the host is the one the composition root registers, over the
    /// catalogue definition the service actually serves, and the two golden expansion semantics of AAP
    /// 0.1.3 G4 are re-asserted through it.
    /// </para>
    /// <para>
    /// NO SYNTAX MUTATOR IS SUPPLIED, AND THAT IS THE DEPLOYED SHAPE. Syntax mutation belongs to the
    /// presentational half of the DataWindow that Phase 1 does not ship (C-D), so the compute-object cache
    /// is unavailable and calculation takes its documented uncached path
    /// [<c>ColumnExpressionEngine.ModifySyntax</c>] - a degradation rather than a failure. Asserting the
    /// calculated value here proves that path end to end.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheExpressionEngineBindsAndCalculatesOverTheShippedHostAndNotOnlyOverADouble()
    {
        DataWindowServiceHost host = new HeadlessDataWindowHostFactory(new DataWindowCatalogue())
            .Create(DataWindowCatalogue.SqliteFixtureName)!;

        HeadlessDataWindowHost rows = Assert.IsType<HeadlessDataWindowHost>(host);
        _ = rows.AppendRow(DwBuffer.Primary, ItemStatus.NotModified, 1L, "Ada", 36L, "London", 1m);

        ColumnExpressionEngine engine = new();
        engine.OnInit(host);
        engine.SetEnabled(true);

        Assert.Equal(RetCode.OK, engine.AddVar("v", 5L));

        // ⚠ A POSITIVE INDEX IS THE ASSERTION. of_addexp answers an INDEX on success and a negative
        // RetCode on failure [:L1581], and the deployed service answered -3 (E_INVALID_ARGUMENT) here for
        // every column because the ".ID" probe above could not resolve.
        int staticIndex = engine.AddExp("salary", "$v + 3");
        Assert.True(staticIndex > 0, "of_addexp answered " + staticIndex + " rather than an index.");

        int dynamicIndex = engine.AddExp("age", "$$v + 3");
        Assert.True(dynamicIndex > 0, "of_addexp answered " + dynamicIndex + " rather than an index.");

        // The two indices are DISTINCT, because a column may carry several expressions and each append
        // takes the next slot.
        Assert.NotEqual(staticIndex, dynamicIndex);

        // THE STATIC REFERENCE IS GONE FROM THE STORED TEXT, replaced by its bind-time value - which is
        // the mechanism docs/n_cst_dwsvc_columnexp.md's static-expansion section describes.
        Assert.Equal("(5) + 3", engine.GetExp(staticIndex));
        Assert.DoesNotContain("$", engine.GetExp(staticIndex), StringComparison.Ordinal);

        // THE DYNAMIC REFERENCE SURVIVES AS A REFERENCE, so a later assignment can still reach it.
        Assert.Contains("$$v", engine.GetExp(dynamicIndex), StringComparison.Ordinal);

        // The uncached calculation path runs over the shipped host and writes both columns.
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, TestContext.Current.CancellationToken));

        Assert.Equal(8m, rows.GetItemDecimal(1L, "salary"));
        Assert.Equal(8m, rows.GetItemDecimal(1L, "age"));

        // Assigning the variable moves the DYNAMIC answer and leaves the STATIC one fixed - the two
        // golden semantics, re-asserted against the shipped host.
        Assert.Equal(
            RetCode.OK,
            await engine.SetVarAsync("v", 10L, true, TestContext.Current.CancellationToken));

        Assert.Equal(8m, rows.GetItemDecimal(1L, "salary"));
        Assert.Equal(13m, rows.GetItemDecimal(1L, "age"));

        // An unbindable column is still refused, so the fix widened nothing: a name no column carries
        // resolves to no ordinal and the bind takes the oracle's own refusal arm.
        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp("no_such_column", "$v"));
        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp("salary_t", "$v"));
    }

    /// <summary>
    /// The event-chain factory produces a chain for a bound handle and refuses an unknown one.
    /// </summary>
    /// <remarks>
    /// THE SHARED-HOST ASSERTION IS AGAIN THE LOAD-BEARING ONE. A chain that carried its own host would
    /// let an event change a row the models beside it could not see, so the chain forwards its host
    /// members to the SAME host the model set holds.
    /// </remarks>
    [Fact]
    public void TheChainFactoryProducesAChainForABoundHandleAndRefusesAnUnknownOne()
    {
        HeadlessDataWindowHostFactory hosts = new(new DataWindowCatalogue());
        HeadlessDataWindowModelSetProvider models = BuildModelSetProvider(hosts);
        HeadlessDataWindowEventChainFactory chains = new(models);

        DataServicesOptions configured = new();
        ValidationSessionRegistry registry = new(configured);
        ValidationSessionOpenResult opened = registry.Open(DataWindowCatalogue.SqliteFixtureName);

        Assert.NotNull(opened.Session);

        ValidationSession session = opened.Session;

        RecordingResponder responder = new();
        RecordingObserver observer = new();

        DataWindowEventChain? chain = chains.Create(
            session,
            DataWindowCatalogue.SqliteFixtureName,
            observer,
            responder);

        Assert.NotNull(chain);

        // The chain reads the SAME rows the model set does, because it forwards to the shared host.
        HeadlessDataWindowHost host = Assert.IsType<HeadlessDataWindowHost>(
            hosts.Create(DataWindowCatalogue.SqliteFixtureName));

        _ = host.AppendRow(DwBuffer.Primary, ItemStatus.NotModified, 1L, "shared");

        Assert.Equal(1L, chain.RowCount());
        Assert.Equal("shared", chain.GetItemString(1L, "name"));

        // The five attached services were created and initialised, so the chain is usable rather than
        // merely constructed.
        Assert.NotNull(chain.RowSelect);
        Assert.NotNull(chain.ContextMenu);
        Assert.NotNull(chain.ColumnSort);
        Assert.NotNull(chain.DropDownSearch);

        // The column-expression slot is DISABLED, which is the legacy's own default: the chain reads
        // #Enabled before notifying [se_cst_dw.sru:L313-L315], so it is never notified.
        Assert.False(chain.ColumnExp.Enabled);

        Assert.Null(chains.Create(session, "d_never_transcribed", observer, responder));
    }

    /// <summary>
    /// A semantic event asks the client through the responder and returns the answer it gave.
    /// </summary>
    /// <remarks>
    /// THIS IS THE BEHAVIOUR THE NULL FACTORY MADE UNREACHABLE. The chain's semantic members are the
    /// inverted half of C-03: the server asks the client, because in the legacy the APPLICATION implements
    /// these events. A chain that was never created could not ask anything.
    /// </remarks>
    [Fact]
    public void ASemanticEventAsksTheClientAndReturnsItsAnswer()
    {
        HeadlessDataWindowHostFactory hosts = new(new DataWindowCatalogue());
        HeadlessDataWindowModelSetProvider models = BuildModelSetProvider(hosts);
        HeadlessDataWindowEventChainFactory chains = new(models);

        DataServicesOptions configured = new();
        ValidationSessionRegistry registry = new(configured);
        ValidationSessionOpenResult opened = registry.Open(DataWindowCatalogue.SqliteFixtureName);

        Assert.NotNull(opened.Session);

        ValidationSession session = opened.Session;

        RecordingResponder responder = new() { ReturnValue = 3L, ProducedFilter = "name = 'Ada'" };

        DataWindowEventChain? created = chains.Create(
            session,
            DataWindowCatalogue.SqliteFixtureName,
            new RecordingObserver(),
            responder);

        Assert.NotNull(created);

        DataWindowEventChain chain = created;

        DataWindowServiceHost host = chain;
        IDataWindowObject column = host.GetObjectAttribute("name");

        // The item-change question carries the row, the column and the data, and its answer is returned
        // to the caller verbatim - the {0,1,2,3} alphabet travels rather than being reinterpreted.
        Assert.Equal(3L, chain.OnDoItemChange(1L, column, "Ada"));

        // The drop-down filter arrives through the ref out-parameter. It projects onto a wire field
        // without difficulty; what it cannot be is fire-and-forget, because the caller cannot proceed
        // without it - which is why AAP 0.6.1.4 assigns that pair pattern (b), strictly synchronous.
        string filter = "untouched";
        chain.OnDDSGetFilter(1L, column, "Ada", ref filter);
        Assert.Equal("name = 'Ada'", filter);

        // An answer that produced NO filter leaves the caller's value alone rather than erasing it.
        responder.ProducedFilter = null;
        string preserved = "already composed";
        chain.OnDDSGetFilter(1L, column, "Ada", ref preserved);
        Assert.Equal("already composed", preserved);

        // THREE ASKS FOR THREE SEMANTIC CALLS - one item change and two filter requests. The count is
        // asserted rather than merely the answers, because a chain that asked TWICE per call would still
        // produce the right answers while doubling every client round trip.
        Assert.Equal(3, responder.Asked.Count);
        Assert.All(
            responder.Asked,
            asked => Assert.Equal(DataWindowCatalogue.SqliteFixtureName, asked.DatawindowHandle));
    }

    /// <summary>
    /// A conversation teardown RESTORES the retained models to their durable host, so no later call can
    /// reach the conversation it has finished with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DEFECT THIS PINS, AND WHY IT IS A REBIND RATHER THAN A GUARD. The four models are retained PER
    /// DATAWINDOW HANDLE and shared between the streaming chain and the unary read/apply surface, because
    /// the legacy shares them: one <c>se_cst_dw</c> per DataWindow control holds all five services as
    /// instance members [<c>se_cst_dw.sru:L80-L84</c>]. A chain's constructor then RE-HOSTS those retained
    /// models onto itself - the <c>OnInit(this)</c> fan-out at [<c>se_cst_dw.sru:L576-L580</c>], with
    /// <c>DataWindowServiceBase.OnInit</c> assigning <c>#DataWindow = dw</c>
    /// [<c>n_cst_dwsvc.sru:L85-L86</c>] - which is harmless in process because the control outlives the
    /// service, and is NOT harmless here because the chain is per-CONVERSATION and is torn down at stream
    /// end. The re-hosting is therefore UNDONE at teardown. Giving the chain its own copies instead would
    /// split state the oracle keeps together, which is why that was not the fix.
    /// </para>
    /// <para>
    /// THE FAULT WAS AN <c>ObjectDisposedException</c> ESCAPING A UNARY RPC AS gRPC UNKNOWN. A caller that
    /// had opened and closed an event-chain conversation could not afterwards apply a drop-down search on
    /// the same DataWindow: the retained model raised <c>OnDDSGetFilter</c>
    /// [<c>DropDownSearchModel.GetBoundFilter</c>, oracle <c>:L342</c>] against a chain whose conversation
    /// was gone. <c>DestroyService</c> does not detach a model - it disposes only what is
    /// <c>IDisposable</c>, and none of the four is - so teardown alone left them pointing at the chain.
    /// </para>
    /// </remarks>
    [Fact]
    public void AConversationTeardownRestoresTheRetainedModelsToTheirDurableHost()
    {
        HeadlessDataWindowHostFactory hosts = new(new DataWindowCatalogue());
        HeadlessDataWindowModelSetProvider models = BuildModelSetProvider(hosts);
        HeadlessDataWindowEventChainFactory chains = new(models);

        DataServicesOptions configured = new();
        ValidationSessionRegistry registry = new(configured);
        ValidationSessionOpenResult opened = registry.Open(DataWindowCatalogue.SqliteFixtureName);

        Assert.NotNull(opened.Session);

        DataWindowModelSet? retained = models.GetOrCreate(DataWindowCatalogue.SqliteFixtureName);

        Assert.NotNull(retained);

        // BEFORE ANY CONVERSATION the retained model is on its durable host, whose semantic bodies are
        // empty - PowerBuilder's "event with no script attached" - so the ask completes and asks nobody.
        // The composed filter is captured as the baseline every later call is compared against.
        RecordingResponder responder = new();

        string durableAnswer = retained.DropDownSearch.GetFilter("Ada");

        Assert.NotEmpty(durableAnswer);
        Assert.Empty(responder.Asked);

        DataWindowEventChain? created = chains.Create(
            opened.Session,
            DataWindowCatalogue.SqliteFixtureName,
            new RecordingObserver(),
            responder);

        Assert.NotNull(created);

        // THE CONVERSATION RE-HOSTED THE RETAINED MODEL, which is the behaviour being preserved and not a
        // side effect being tolerated: the ask now travels to the client that opened the stream.
        _ = retained.DropDownSearch.GetFilter("Ada");

        Assert.Single(responder.Asked);
        Assert.Equal(
            PowerFramework.Contracts.DataServices.V1.EventId.Onddsgetfilter,
            responder.Asked[0].EventId);

        // The stream ends: the chain is torn down and its conversation disposed.
        created.Teardown();
        responder.Disposed = true;

        // ⚠ THIS IS THE DEFECT, ASSERTED RATHER THAN DESCRIBED. Teardown on its own leaves the retained
        // model pointing at the finished conversation, so the very next unary use of it faults. If the
        // rebind below were removed, this is the exception a caller received as gRPC UNKNOWN.
        _ = Assert.Throws<ObjectDisposedException>(() => retained.DropDownSearch.GetFilter("Ada"));

        models.RebindToDurableHost(DataWindowCatalogue.SqliteFixtureName);

        // AND AFTER THE REBIND the same call is served again, against the durable host, asking nobody.
        // THE ASK COUNT IS THE LOAD-BEARING HALF, not the answer. The answer is identical either way,
        // because a responder that produces no filter leaves the composed text alone [se_cst_dw.sru:L13] -
        // so comparing only the string would pass against a model still bound to the conversation. The
        // count is what shows the ask stopped reaching the dead conversation rather than merely stopping
        // throwing.
        Assert.Equal(durableAnswer, retained.DropDownSearch.GetFilter("Ada"));
        Assert.Single(responder.Asked);

        // THE SAME OBJECT, NOT A REPLACEMENT. The rebind re-hosts the retained models; it does not discard
        // and rebuild them, which would throw away the state the legacy keeps for the life of the control.
        Assert.Same(retained, models.GetOrCreate(DataWindowCatalogue.SqliteFixtureName));
        Assert.Same(retained.DropDownSearch, models.GetOrCreate(DataWindowCatalogue.SqliteFixtureName)!.DropDownSearch);
    }

    /// <summary>
    /// ALL FOUR re-hosted models are restored, not only the one the defect was reported on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CHAIN RE-HOSTS FOUR, SO FOUR ARE RESTORED. The constructor's fan-out covers the context menu,
    /// the row-selection service, the drop-down search model and the sort model
    /// [<c>se_cst_dw.sru:L576-L580</c>]; the column-expression engine is the fifth and is deliberately
    /// EXCLUDED from the restore because the chain builds a fresh one per conversation and therefore owns
    /// its lifetime - re-hosting an engine the chain destroyed would resurrect a reference to it.
    /// </para>
    /// <para>
    /// ASSERTED ON THE BINDING RATHER THAN ON BEHAVIOUR, AND HERE IS WHY THAT IS THE STRONGER TEST. Only
    /// ONE unary RPC on this contract can currently reach a semantic ask on a retained model -
    /// <c>ApplyDropDownSearch</c>, through <c>OnDDSGetFilter</c> - which is exactly why the defect surfaced
    /// there and nowhere else. <c>GetContextMenuModel</c> deliberately does not invoke the build path (it
    /// would need a pointer context, which is the deferred rendering half), and the row-selection and sort
    /// models raise nothing from any unary entry point. A behavioural assertion for those three would
    /// therefore have nothing to observe today, so the binding itself is asserted: the invariant is "no
    /// retained model points at a finished conversation", and that stays checkable if a later unary path
    /// does reach one.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryReHostedModelIsRestoredAndNotOnlyTheOneTheDefectWasReportedOn()
    {
        HeadlessDataWindowHostFactory hosts = new(new DataWindowCatalogue());
        HeadlessDataWindowModelSetProvider models = BuildModelSetProvider(hosts);
        HeadlessDataWindowEventChainFactory chains = new(models);

        DataServicesOptions configured = new();
        ValidationSessionRegistry registry = new(configured);
        ValidationSessionOpenResult opened = registry.Open(DataWindowCatalogue.SqliteFixtureName);

        Assert.NotNull(opened.Session);

        DataWindowModelSet? retained = models.GetOrCreate(DataWindowCatalogue.SqliteFixtureName);

        Assert.NotNull(retained);

        DataWindowServiceHost durable = retained.Host;

        // Every one of the four starts on the durable host.
        Assert.Same(durable, retained.ContextMenu.DataWindow);
        Assert.Same(durable, retained.RowSelect.DataWindow);
        Assert.Same(durable, retained.ColumnSort.DataWindow);
        Assert.Same(durable, retained.DropDownSearch.DataWindow);

        RecordingResponder responder = new();

        DataWindowEventChain? created = chains.Create(
            opened.Session,
            DataWindowCatalogue.SqliteFixtureName,
            new RecordingObserver(),
            responder);

        Assert.NotNull(created);

        // The conversation re-hosted all four onto itself - the behaviour being preserved.
        Assert.Same(created, retained.ContextMenu.DataWindow);
        Assert.Same(created, retained.RowSelect.DataWindow);
        Assert.Same(created, retained.ColumnSort.DataWindow);
        Assert.Same(created, retained.DropDownSearch.DataWindow);

        created.Teardown();
        responder.Disposed = true;

        models.RebindToDurableHost(DataWindowCatalogue.SqliteFixtureName);

        // And the teardown put all four back. Not one of them still addresses the finished conversation.
        Assert.Same(durable, retained.ContextMenu.DataWindow);
        Assert.Same(durable, retained.RowSelect.DataWindow);
        Assert.Same(durable, retained.ColumnSort.DataWindow);
        Assert.Same(durable, retained.DropDownSearch.DataWindow);

        // The one path that CAN ask is served again, and asks nobody.
        Assert.NotEmpty(retained.DropDownSearch.GetFilter("Ada"));
        Assert.Empty(responder.Asked);
    }

    /// <summary>
    /// Rebinding a handle the provider retains nothing for is a no-op rather than a fault.
    /// </summary>
    /// <remarks>
    /// The teardown path calls this unconditionally with the handle the conversation was bound to, and it
    /// must not care whether anything is still retained for it - a provider that threw here would turn a
    /// clean stream end into a faulted one.
    /// </remarks>
    [Fact]
    public void RebindingAHandleNothingIsRetainedForIsANoOp()
    {
        HeadlessDataWindowHostFactory hosts = new(new DataWindowCatalogue());
        HeadlessDataWindowModelSetProvider models = BuildModelSetProvider(hosts);

        models.RebindToDurableHost("d_never_transcribed");
        models.RebindToDurableHost(string.Empty);

        // And the provider still serves a real handle afterwards, so nothing was corrupted by either call.
        Assert.NotNull(models.GetOrCreate(DataWindowCatalogue.SqliteFixtureName));
    }

    // ==============================================================================================
    //  FIXTURES
    // ==============================================================================================

    /// <summary>Builds a provider carrying only the mutual-TLS registration under test.</summary>
    /// <param name="certificatePath">The configured certificate path, or <see langword="null"/>.</param>
    /// <param name="keyPath">The configured key path, or <see langword="null"/>.</param>
    /// <returns>A provider the identity can be resolved from.</returns>
    /// <remarks>
    /// COMPOSED THROUGH THE SERVICE'S OWN <c>AddDataServicesClients</c>, so these cases cannot describe a
    /// registration the host does not actually make. That is the whole point: the previous defect was a
    /// MISSING registration, and a fixture that registered the identity itself would have passed against
    /// the broken shape.
    /// </remarks>
    private static ServiceProvider BuildClientProvider(string? certificatePath, string? keyPath)
    {
        ServiceCollection services = new();

        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<DataServicesOptions>().Configure(options =>
        {
            options.Security.MutualTls.CertificatePath = certificatePath ?? string.Empty;
            options.Security.MutualTls.CertificateKeyPath = keyPath ?? string.Empty;
        });

        services.AddDataServicesClients();

        return services.BuildServiceProvider();
    }

    /// <summary>Builds a model-set provider over the real collaborators.</summary>
    /// <param name="hosts">The host factory.</param>
    /// <returns>The provider.</returns>
    private static HeadlessDataWindowModelSetProvider BuildModelSetProvider(
        HeadlessDataWindowHostFactory hosts)
    {
        DataServicesOptions configured = new();

        return new HeadlessDataWindowModelSetProvider(
            hosts,
            Options.Create(configured),
            new I18n(),
            // THE SHIPPED BLOCKED MATCHER, which is what the composition root registers: the pinyin
            // lookup table lives only inside the closed pfw.dll, so AAP 0.6.5 records it as the single
            // genuine parity risk and requires it be reported BLOCKED rather than approximated.
            PinyinFirstLetterMatcher.Blocked,
            ExpressionPageResolverFactory.Create(
                configured.ColumnExpression.PageResolution,
                configured.ColumnExpression.PageRowsPerPage));
    }

    /// <summary>Writes a self-signed PEM certificate and its PEM private key to a fresh directory.</summary>
    /// <param name="directory">The directory to write into.</param>
    /// <returns>The certificate path and the key path.</returns>
    /// <remarks>
    /// GENERATED RATHER THAN CHECKED IN, and that is a secrets requirement rather than a convenience: a
    /// committed private key would be exactly the defect the repository-wide sweep of AAP 0.6.6 exists to
    /// eliminate, even in a test. The key lives for the duration of one case and is deleted with the
    /// directory.
    /// </remarks>
    private static (string CertificatePath, string KeyPath) WritePemPair(string directory)
    {
        _ = Directory.CreateDirectory(directory);

        using RSA key = RSA.Create(2048);

        CertificateRequest request = new(
            "CN=PowerFramework.DataServices.Tests",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(5));

        string certificatePath = Path.Combine(directory, "client.pem");
        string keyPath = Path.Combine(directory, "client.key");

        File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());

        return (certificatePath, keyPath);
    }

    /// <summary>Removes a directory a case created, tolerating one that was never created.</summary>
    /// <param name="directory">The directory.</param>
    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Records every semantic question and answers with a scripted result.</summary>
    private sealed class RecordingResponder : IDataWindowSemanticResponder
    {
        /// <summary>Every question asked, in order.</summary>
        internal List<EventNotification> Asked { get; } = [];

        /// <summary>The return value every answer carries.</summary>
        internal long ReturnValue { get; set; }

        /// <summary>The produced filter, or <see langword="null"/> to leave the field unset.</summary>
        internal string? ProducedFilter { get; set; }

        /// <summary>
        /// Whether this responder now stands for a conversation that has been disposed.
        /// </summary>
        /// <remarks>
        /// THE PRODUCTION RESPONDER IS <c>DataWindowEventConversation</c>, AND IT IS DISPOSABLE. Its
        /// <c>AskAsync</c> throws <see cref="ObjectDisposedException"/> once the duplex call it wraps has
        /// ended, which is the exact fault that escaped a unary RPC as gRPC UNKNOWN. Setting this
        /// reproduces that state without needing a real stream.
        /// </remarks>
        internal bool Disposed { get; set; }

        /// <inheritdoc/>
        public ValueTask<WireEventResult> AskAsync(
            EventNotification question,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(question);

            ObjectDisposedException.ThrowIf(Disposed, this);

            Asked.Add(question);

            WireEventResult answer = new()
            {
                CorrelationId = question.CorrelationId,
                EventId = question.EventId,
                ReturnValue = ReturnValue,
            };

            if (ProducedFilter is not null)
            {
                answer.ProducedFilter = ProducedFilter;
            }

            return ValueTask.FromResult(answer);
        }
    }

    /// <summary>Counts dispatch notifications without asserting on them.</summary>
    private sealed class RecordingObserver : IDataWindowEventObserver
    {
        /// <summary>How many dispatches were observed.</summary>
        internal int Count { get; private set; }

        /// <inheritdoc/>
        public void OnEventDispatched(DataWindowEventOutcome outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);

            Count++;
        }
    }
}
