// ==================================================================================================
//  DataWindowCompositionTests.cs - THE PROVISIONED WIRING, ASSERTED
//  ------------------------------------------------------------------------------------------------
//  WHY THIS FILE EXISTS
//  Two production seams of this service were previously wired to implementations that refused, and both
//  refusals were defended in comments rather than caught by a test - which is exactly why they survived.
//  These cases assert the wiring itself, so a regression to either shape fails the build:
//
//    1. THE MUTUAL-TLS CLIENT IDENTITY. Binding and validating the configured certificate pair is not
//       the same as PRESENTING it. An earlier revision did the first two and never attached the
//       certificate to a handler, so a deployment that mounted key material still handshook
//       anonymously - configured, validated, and inert (constraint C-G).
//    2. THE THREE HOST-BINDING SEAMS. All three returned null, which made eight C-03 operations and the
//       whole of C-04 unreachable. AAP 0.2.1.3 Correction 3 and AAP 0.3.5 both require a bound headless
//       host in DataServices, so the refusal was an AAP-compliance failure rather than a documented gap.
//
//  WHAT IS NOT ASSERTED HERE, AND WHY. No case opens a socket or completes a TLS handshake. Proving that
//  a client certificate is presented on the wire needs a listener that requests one, which is an
//  orchestration-level check rather than a unit-level one. What IS provable here is the part that was
//  actually broken: that the identity is LOADED, that it is REACHABLE from the container, and that
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

        // The drop-down filter arrives through the ref out-parameter, which has no asynchronous form and
        // is why AAP 0.6.1.4 assigns that pair pattern (b), strictly synchronous.
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

        /// <inheritdoc/>
        public ValueTask<WireEventResult> AskAsync(
            EventNotification question,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(question);

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
