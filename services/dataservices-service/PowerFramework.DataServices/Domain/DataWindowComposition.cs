// ==================================================================================================
//  DataWindowComposition.cs - THE THREE PROVISIONED DATAWINDOW FACTORIES
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The three seams the published surface resolves a DataWindow handle through: the host factory
//  (C-04's expression session), the model-set provider (C-03's eight headless model operations) and the
//  event-chain factory (C-03's bidirectional EventChain).
//
//  WHAT THEY REPLACED, AND WHY THE REPLACEMENT WAS REQUIRED
//  All three previously shipped as `Unbound*` implementations returning null. The defence recorded at
//  those types was that binding a DataWindow needs the DesignSystem ancestry `se_cst_dw` inherits from
//  `se_cst_datawindow` [se_cst_dw.sru:L4, :L10] and that constraint C-D forbids implementing a deferred
//  service even partially. That reasoning does not survive contact with the AAP:
//    * AAP 0.2.1.3 Correction 3 resolves that exact inheritance edge by telling DataServices to define
//      its own host contract and IMPLEMENT AGAINST IT, recording se_cst_datawindow as REFERENCE-only.
//    * AAP 0.3.5 assigns the HEADLESS half of every UI capability to DataServices and defers only the
//      RENDERING half.
//  So a bound headless host is the AAP's own instruction rather than a deferred-service implementation,
//  and returning null made eight C-03 operations plus the whole of C-04 permanently unreachable.
//
//  WHAT DID *NOT* CHANGE - THE NEGATIVE IS STILL REACHABLE
//  A handle no definition matches STILL resolves to nothing, and the surface still answers
//  `RetCode.E_INVALID_HANDLE` for it. That negative is published contract rather than a gap, and
//  `DataWindowCatalogue.TryGet` is where it now comes from. The difference is that a KNOWN handle now
//  works, which is the whole distinction between a provisioned service and a documented gap.
//
//  ALL THREE ARE STILL `TryAdd`-REGISTERED, so a deployment that materialises DataWindows another way
//  substitutes its own implementation without editing this file or the composition root.
//
//  LEGACY REFERENCE (read only)
//      ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L562-L580
//          the creation order and the DIFFERENT initialisation order of the five attached services
//      ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru:L86
//          `#Eventful = dw.Eventful` - why one broker per host is load bearing
// ==================================================================================================

using System.Globalization;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Grpc;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Localization;
using DomainContextMenuModel = PowerFramework.DataServices.Services.ContextMenuModel;

// ALIASED, BECAUSE Microsoft.Extensions.Logging ALSO PUBLISHES AN `EventId`. That namespace arrives
// through the implicit usings this repository enables, so naming the contract's enum explicitly is what
// keeps every semantic ask unambiguous (CS0104) without dropping the logging namespace.
using WireEventId = PowerFramework.Contracts.DataServices.V1.EventId;

namespace PowerFramework.DataServices.Domain;

/// <summary>
/// Produces a headless host for a DataWindow name the catalogue carries.
/// </summary>
/// <remarks>
/// HOSTS ARE RETAINED PER NAME, and the retention is contract rather than an optimisation: C-04's
/// expression session holds variables and bound expressions against a host, so a second resolve of the
/// same handle that produced a second host would silently lose every variable the first session set.
/// </remarks>
public sealed class HeadlessDataWindowHostFactory : IDataWindowHostFactory
{
    /// <summary>The definitions a handle can resolve to.</summary>
    private readonly DataWindowCatalogue _catalogue;

    /// <summary>The retained hosts, by DataWindow name.</summary>
    private readonly Dictionary<string, HeadlessDataWindowHost> _hosts =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Guards the retention table.</summary>
    private readonly object _gate = new();

    /// <summary>Initializes the factory.</summary>
    /// <param name="catalogue">The definition catalogue.</param>
    /// <exception cref="ArgumentNullException"><paramref name="catalogue"/> is <see langword="null"/>.</exception>
    public HeadlessDataWindowHostFactory(DataWindowCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        _catalogue = catalogue;
    }

    /// <inheritdoc/>
    public DataWindowServiceHost? Create(string dataWindowName) => Bind(dataWindowName);

    /// <inheritdoc/>
    public DataWindowServiceHost? CreateIsolated(string dataWindowName) => BindIsolated(dataWindowName);

    /// <summary>Creates a host for one expression session, retained by nothing.</summary>
    /// <param name="dataWindowName">The name a handle carries.</param>
    /// <returns>A fresh host, or <see langword="null"/> when no definition carries that name.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="dataWindowName"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// DELIBERATELY NOT ENTERED IN THE RETENTION TABLE, and it does not read from it either: every call
    /// answers a new host. Two expression sessions over the same data-object name therefore hold two
    /// independent DataWindows, which is what makes a session's loaded rows its own.
    /// </para>
    /// <para>
    /// THE DEFINITION IS SHARED AND THAT IS SAFE. A host only READS its definition - it builds its own
    /// object wrappers, its own row buffers, its own sort string and its own event broker - so nothing a
    /// host does to itself is visible through the definition to another host over the same one.
    /// </para>
    /// <para>
    /// The lock is still taken, so that this and <see cref="Bind"/> cannot interleave against the
    /// catalogue while it is being read.
    /// </para>
    /// </remarks>
    public HeadlessDataWindowHost? BindIsolated(string dataWindowName)
    {
        ArgumentNullException.ThrowIfNull(dataWindowName);

        if (string.IsNullOrWhiteSpace(dataWindowName))
        {
            return null;
        }

        lock (_gate)
        {
            return _catalogue.TryGet(dataWindowName, out DataWindowDefinition? definition)
                ? new HeadlessDataWindowHost(definition)
                : null;
        }
    }

    /// <summary>Resolves or creates the retained host for one DataWindow name.</summary>
    /// <param name="dataWindowName">The name a handle carries.</param>
    /// <returns>The host, or <see langword="null"/> when no definition carries that name.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="dataWindowName"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// TYPED MORE NARROWLY THAN THE INTERFACE MEMBER, so the model-set provider and the chain factory
    /// can reach the headless host's own members - appending a retrieved row, reading an original value -
    /// without a cast. The interface member above stays typed to the published base.
    /// </remarks>
    public HeadlessDataWindowHost? Bind(string dataWindowName)
    {
        // Validated even though a blank name is also refused below, because a caller that supplied
        // nothing has made a different mistake from one that supplied a name the catalogue declines, and
        // collapsing the two would send a reader of the resulting diagnostic to the wrong place.
        ArgumentNullException.ThrowIfNull(dataWindowName);

        if (string.IsNullOrWhiteSpace(dataWindowName))
        {
            return null;
        }

        lock (_gate)
        {
            if (_hosts.TryGetValue(dataWindowName, out HeadlessDataWindowHost? retained))
            {
                return retained;
            }

            if (!_catalogue.TryGet(dataWindowName, out DataWindowDefinition? definition))
            {
                // THE PUBLISHED NEGATIVE. See this file's header: an unknown handle answers nothing and
                // the surface turns that into RetCode.E_INVALID_HANDLE, which is contract.
                return null;
            }

            HeadlessDataWindowHost created = new(definition);
            _hosts[dataWindowName] = created;

            return created;
        }
    }
}

/// <summary>
/// Produces and retains the five headless models for a DataWindow handle.
/// </summary>
/// <remarks>
/// RETENTION IS THE HALF OF THIS INTERFACE'S CONTRACT THAT MATTERS MOST. Each model holds per-handle
/// state - the row-selection set, the installed sort, the drop-down search's filter state, the context
/// menu's built item model - so a second resolve that built a second set would hand a caller a
/// DataWindow that had forgotten what it just did.
/// </remarks>
public sealed class HeadlessDataWindowModelSetProvider : IDataWindowModelSetProvider
{
    /// <summary>The host factory, whose retention this provider's own retention mirrors.</summary>
    private readonly HeadlessDataWindowHostFactory _hosts;

    /// <summary>The bound options every model reads its preserved legacy defaults from.</summary>
    private readonly IOptions<DataServicesOptions> _options;

    /// <summary>The localization facade the diagnostic-bearing models route their text through.</summary>
    private readonly I18n _localization;

    /// <summary>The pinyin matcher the expression evaluator needs.</summary>
    private readonly PinyinFirstLetterMatcher _pinyinMatcher;

    /// <summary>The page resolver the page-scoped aggregate needs.</summary>
    private readonly IExpressionPageResolver _pageResolver;

    /// <summary>The retained sets, by handle.</summary>
    private readonly Dictionary<string, DataWindowModelSet> _sets = new(StringComparer.Ordinal);

    /// <summary>Guards the retention table.</summary>
    private readonly object _gate = new();

    /// <summary>Initializes the provider.</summary>
    /// <param name="hosts">The host factory.</param>
    /// <param name="options">The bound options.</param>
    /// <param name="localization">The localization facade.</param>
    /// <param name="pinyinMatcher">The pinyin matcher.</param>
    /// <param name="pageResolver">The page resolver.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public HeadlessDataWindowModelSetProvider(
        HeadlessDataWindowHostFactory hosts,
        IOptions<DataServicesOptions> options,
        I18n localization,
        PinyinFirstLetterMatcher pinyinMatcher,
        IExpressionPageResolver pageResolver)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(pinyinMatcher);
        ArgumentNullException.ThrowIfNull(pageResolver);

        _hosts = hosts;
        _options = options;
        _localization = localization;
        _pinyinMatcher = pinyinMatcher;
        _pageResolver = pageResolver;
    }

    /// <inheritdoc/>
    public DataWindowModelSet? GetOrCreate(string dataWindowHandle)
    {
        ArgumentNullException.ThrowIfNull(dataWindowHandle);

        lock (_gate)
        {
            if (_sets.TryGetValue(dataWindowHandle, out DataWindowModelSet? retained))
            {
                return retained;
            }

            HeadlessDataWindowHost? host = _hosts.Bind(dataWindowHandle);

            if (host is null)
            {
                return null;
            }

            DataWindowModelSet built = Build(host);
            _sets[dataWindowHandle] = built;

            return built;
        }
    }

    /// <summary>Builds the five models over one host and initialises each against it.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The set.</returns>
    /// <remarks>
    /// EVERY MODEL IS INITIALISED AGAINST THE SAME HOST, which is what makes them share ONE event broker:
    /// the service base reads the broker off its host - <c>#Eventful = dw.Eventful</c>
    /// [<c>n_cst_dwsvc.sru:L86</c>] - so a subscription one model registers is visible to a topic another
    /// triggers. Two hosts here would silently split the broker in two.
    /// </remarks>
    private DataWindowModelSet Build(HeadlessDataWindowHost host)
    {
        DataWindowExpressionEvaluator evaluator = new(host, _pinyinMatcher, _pageResolver);

        DomainContextMenuModel contextMenu = new(_localization, evaluator, _options);
        contextMenu.OnInit(host);

        RowSelectService rowSelect = new(_localization, _options);
        rowSelect.OnInit(host);

        ColumnSortModel columnSort = new();
        columnSort.OnInit(host);

        DropDownSearchModel dropDownSearch = new(_options);
        dropDownSearch.OnInit(host);

        return new DataWindowModelSet(host, contextMenu, rowSelect, columnSort, dropDownSearch);
    }
}

/// <summary>
/// Produces the event chain for one validation session and one DataWindow handle.
/// </summary>
/// <remarks>
/// A CHAIN PER SESSION, NOT PER HANDLE, and the difference is the reason this is not folded into the
/// model-set provider. A chain holds the validation session whose four fields of cross-event state the
/// item-change protocol reads and writes between events [<c>se_cst_dw.sru:L89-L96</c>], so two
/// concurrent sessions over one DataWindow need two chains and one model set.
/// </remarks>
internal sealed class HeadlessDataWindowEventChainFactory : IDataWindowEventChainFactory
{
    /// <summary>The model-set provider, which is where the chain's five services come from.</summary>
    private readonly HeadlessDataWindowModelSetProvider _models;

    /// <summary>Initializes the factory.</summary>
    /// <param name="models">The model-set provider.</param>
    /// <exception cref="ArgumentNullException"><paramref name="models"/> is <see langword="null"/>.</exception>
    internal HeadlessDataWindowEventChainFactory(HeadlessDataWindowModelSetProvider models)
    {
        ArgumentNullException.ThrowIfNull(models);

        _models = models;
    }

    /// <inheritdoc/>
    public DataWindowEventChain? Create(
        ValidationSession session,
        string dataWindowHandle,
        IDataWindowEventObserver observer,
        IDataWindowSemanticResponder responder)
    {
        // All four are validated because all four are required by the interface, and a null here is a
        // caller defect rather than an unbound handle. Conflating the two would report a missing
        // DataWindow when the real fault was a missing observer.
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dataWindowHandle);
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(responder);

        DataWindowModelSet? models = _models.GetOrCreate(dataWindowHandle);

        // THE HOST IS READ BACK AS ITS CONCRETE TYPE RATHER THAN DOWNCAST FROM THE SET. The set publishes
        // its host as the base contract, which is right for its own consumers, but the chain forwards
        // members this provider knows are on the headless host - so the provider hands the concrete
        // instance over instead of the chain casting and hoping. A set built by a substituted provider
        // therefore cannot silently reach this chain with a host of another shape.
        if (models is null || models.Host is not HeadlessDataWindowHost host)
        {
            // The published unknown-handle negative, unchanged.
            return null;
        }

        return new HeadlessDataWindowEventChain(
            session,
            new HeadlessAttachedServiceFactory(models),
            host,
            dataWindowHandle,
            observer,
            responder);
    }
}

/// <summary>
/// Supplies the chain's five attached services from an already-built model set.
/// </summary>
/// <remarks>
/// ADAPTERS RATHER THAN DIRECT IMPLEMENTATIONS, because four of the five models derive from
/// <c>DataWindowServiceBase</c> without declaring the chain's service interfaces. The adapters are
/// four-line delegations, and putting the interface on the models instead would couple every model to
/// the chain's vocabulary for no behavioural gain.
/// </remarks>
internal sealed class HeadlessAttachedServiceFactory : IDataWindowAttachedServiceFactory
{
    /// <summary>The set the five services are adapted from.</summary>
    private readonly DataWindowModelSet _models;

    /// <summary>Initializes the factory.</summary>
    /// <param name="models">The built model set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="models"/> is <see langword="null"/>.</exception>
    internal HeadlessAttachedServiceFactory(DataWindowModelSet models)
    {
        ArgumentNullException.ThrowIfNull(models);

        _models = models;
    }

    /// <inheritdoc/>
    public IDataWindowContextMenuService CreateContextMenu() =>
        new ContextMenuServiceAdapter(_models.ContextMenu);

    /// <inheritdoc/>
    public IDataWindowRowSelectService CreateRowSelect() =>
        new RowSelectServiceAdapter(_models.RowSelect);

    /// <inheritdoc/>
    public IDataWindowColumnSortService CreateColumnSort() =>
        new ColumnSortServiceAdapter(_models.ColumnSort);

    /// <inheritdoc/>
    public IDataWindowDropDownSearchService CreateDropDownSearch() => _models.DropDownSearch;

    /// <inheritdoc/>
    /// <remarks>
    /// THE EXPRESSION SLOT IS DISABLED, AND THAT IS THE LEGACY'S OWN DEFAULT RATHER THAN AN OMISSION.
    /// The column-expression service reports <c>#Enabled</c> false until a caller opens an expression
    /// session and binds an expression [<c>n_cst_dwsvc_columnexp.sru</c>], and the chain reads exactly
    /// that flag before notifying it [<c>se_cst_dw.sru:L313-L315</c>]. C-04 is where an expression is
    /// bound, and it drives the engine directly through its own session rather than through this slot,
    /// so a chain-side notification would recalculate a second, unbound engine.
    /// </remarks>
    public IDataWindowColumnExpressionService CreateColumnExp() => new DisabledColumnExpressionService();

    /// <summary>Adapts the context-menu model onto the chain's service interface.</summary>
    /// <param name="model">The model.</param>
    private sealed class ContextMenuServiceAdapter(DomainContextMenuModel model)
        : IDataWindowContextMenuService
    {
        /// <inheritdoc/>
        public bool Enabled => model.Enabled;

        /// <inheritdoc/>
        public void OnInit(DataWindowServiceHost dw) => model.OnInit(dw);
    }

    /// <summary>Adapts the row-select model onto the chain's service interface.</summary>
    /// <param name="model">The model.</param>
    private sealed class RowSelectServiceAdapter(RowSelectService model) : IDataWindowRowSelectService
    {
        /// <inheritdoc/>
        public bool Enabled => model.Enabled;

        /// <inheritdoc/>
        public void OnInit(DataWindowServiceHost dw) => model.OnInit(dw);

        /// <inheritdoc/>
        public void OnFiltered() => model.OnFiltered();
    }

    /// <summary>Adapts the column-sort model onto the chain's service interface.</summary>
    /// <param name="model">The model.</param>
    private sealed class ColumnSortServiceAdapter(ColumnSortModel model) : IDataWindowColumnSortService
    {
        /// <inheritdoc/>
        public bool Enabled => model.Enabled;

        /// <inheritdoc/>
        public void OnInit(DataWindowServiceHost dw) => model.OnInit(dw);
    }

    /// <summary>
    /// The column-expression slot in its disabled state, which is the state the legacy ships it in.
    /// </summary>
    /// <remarks>
    /// NOT A STUB - A DISABLED SERVICE, and the distinction is observable. The chain reads
    /// <see cref="Enabled"/> before it notifies [<c>se_cst_dw.sru:L313-L315</c>], so a disabled service
    /// is never notified at all and <see cref="OnItemChanged"/> is unreachable rather than empty. Its
    /// body would be dead code either way; it is written to do nothing because the interface requires
    /// the member, and the reason it can do nothing is that no expression is bound to this slot.
    /// </remarks>
    private sealed class DisabledColumnExpressionService : IDataWindowColumnExpressionService
    {
        /// <inheritdoc/>
        public bool Enabled => false;

        /// <inheritdoc/>
        public void OnInit(DataWindowServiceHost dw) => ArgumentNullException.ThrowIfNull(dw);

        /// <inheritdoc/>
        public void OnItemChanged(long row, IDataWindowObject dwo) =>
            ArgumentNullException.ThrowIfNull(dwo);
    }
}

/// <summary>
/// The provisioned event chain: the ported event protocol over a headless host, asking the client for
/// every semantic answer.
/// </summary>
/// <remarks>
/// <para>
/// THE HOST IS COMPOSED RATHER THAN INHERITED, and it has to be: the chain's base already derives from
/// the host contract, so the 48 host members are forwarded to the composed headless host below. That
/// keeps exactly one implementation of the row and column model in the service - the one the model set
/// shares - so a chain and the models beside it read and write the SAME rows. A second model would let
/// an event change a row the models could not see.
/// </para>
/// <para>
/// THE SEMANTIC MEMBERS ASK THE CLIENT, SYNCHRONOUSLY, AND THE BLOCKING IS THE CONTRACT. AAP 0.6.1.4
/// assigns the item-change and validation chain, the context menu and the drop-down search to pattern
/// (b) - strictly synchronous, NO REORDERING PERMITTED - because the validation-error handler reads the
/// code the preceding item-change event stashed, so its behaviour is a function of the prior event's
/// return value. The ported members are synchronous by declaration and the transport is asynchronous, so
/// the ask is awaited to completion here. This service has no synchronization context, so the wait is a
/// thread-pool wait rather than the classic deadlock; making these members async instead would change
/// the ordering guarantee the AAP names the highest risk in the whole refactor.
/// </para>
/// </remarks>
internal sealed class HeadlessDataWindowEventChain : DataWindowEventChain
{
    /// <summary>The composed host every forwarded member reaches.</summary>
    private readonly HeadlessDataWindowHost _host;

    /// <summary>The handle every question is stamped with.</summary>
    private readonly string _handle;

    /// <summary>The transport the semantic questions are asked through.</summary>
    private readonly IDataWindowSemanticResponder _responder;

    /// <summary>Initializes the chain.</summary>
    /// <param name="session">The validation session the chain holds for its whole life.</param>
    /// <param name="services">The five attached services.</param>
    /// <param name="host">The headless host the model set shares.</param>
    /// <param name="handle">The DataWindow handle.</param>
    /// <param name="observer">The dispatch observer.</param>
    /// <param name="responder">The transport semantic questions are asked through.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// THE BASE'S FOURTH PARAMETER - ITS SEQUENCER - IS DELIBERATELY NOT SURFACED, so the base builds
    /// its own. The sequencer's accept rule is what turns an out-of-order arrival into a hard error
    /// rather than a reorder opportunity, and supplying one from here would let a caller weaken it.
    /// </remarks>
    internal HeadlessDataWindowEventChain(
        ValidationSession session,
        HeadlessAttachedServiceFactory services,
        HeadlessDataWindowHost host,
        string handle,
        IDataWindowEventObserver observer,
        IDataWindowSemanticResponder responder)
        : base(session, services, observer)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(responder);

        _host = host;
        _handle = handle;
        _responder = responder;
    }

    // ---------------------------------------------------------------------------------------------
    //  THE SIX SEMANTIC MEMBERS - each asks the client and returns its answer
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override long OnDoItemChange(long row, IDataWindowObject dwo, string? data)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        return Ask(
            WireEventId.Ondoitemchange,
            notification => notification.DoItemChange = new DoItemChangeEvent
            {
                Row = row,
                Dwo = DataWindowWireProjection.ToWireDwObject(dwo),
                Data = data ?? string.Empty,
            }).ReturnValue;
    }

    /// <inheritdoc/>
    public override void OnDoItemChanged(long row, IDataWindowObject dwo)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        _ = Ask(
            WireEventId.Ondoitemchanged,
            notification => notification.DoItemChanged = new DoItemChangedEvent
            {
                Row = row,
                Dwo = DataWindowWireProjection.ToWireDwObject(dwo),
            });
    }

    /// <inheritdoc/>
    public override long OnInitContextMenu(long row, IDataWindowObject dwo)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        return Ask(
            WireEventId.Oninitcontextmenu,
            notification => notification.InitContextMenu = new InitContextMenuEvent
            {
                Row = row,
                Dwo = DataWindowWireProjection.ToWireDwObject(dwo),
            }).ReturnValue;
    }

    /// <inheritdoc/>
    public override long OnContextMenu(long row, IDataWindowObject dwo, long mid)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        return Ask(
            WireEventId.Oncontextmenu,
            notification => notification.ContextMenu = new ContextMenuEvent
            {
                Row = row,
                Dwo = DataWindowWireProjection.ToWireDwObject(dwo),
                Mid = mid,
            }).ReturnValue;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// THE PRODUCED FILTER IS WRITTEN BACK THROUGH THE <c>ref</c> PARAMETER ONLY WHEN THE CLIENT SET IT.
    /// The contract carries it as an OPTIONAL field precisely so that a client which handled the event
    /// without producing a filter is distinguishable from one that produced an empty filter, and
    /// overwriting the caller's value with an empty string would erase a filter the caller had already
    /// composed [<c>se_cst_dw.sru:L13</c>].
    /// </remarks>
    public override void OnDDSGetFilter(
        long row,
        IDataWindowObject? dwo,
        string data,
        ref string filter)
    {
        ArgumentNullException.ThrowIfNull(data);

        EventResult answer = Ask(
            WireEventId.Onddsgetfilter,
            notification => notification.DdsGetFilter = new DdsGetFilterEvent
            {
                Row = row,
                Dwo = DataWindowWireProjection.ToWireDwObject(dwo),
                Data = data,
            });

        if (answer.HasProducedFilter)
        {
            filter = answer.ProducedFilter;
        }
    }

    /// <inheritdoc/>
    public override void OnDDSFiltered(
        long row,
        IDataWindowObject? dwo,
        long rowCount,
        long filteredCount) =>
        _ = Ask(
            WireEventId.Onddsfiltered,
            notification => notification.DdsFiltered = new DdsFilteredEvent
            {
                Row = row,
                Dwo = DataWindowWireProjection.ToWireDwObject(dwo),
                RowCount = rowCount,
                FilteredCount = filteredCount,
            });

    /// <summary>Asks the client one semantic question and waits for its answer.</summary>
    /// <param name="eventId">Which event is being asked.</param>
    /// <param name="populate">Populates the notification's body.</param>
    /// <returns>The client's answer.</returns>
    /// <remarks>
    /// SYNCHRONOUS BY CONTRACT - see this type's remarks for why the wait is correct rather than a
    /// compromise. <c>GetAwaiter().GetResult()</c> rather than <c>.Result</c>, so a fault propagates as
    /// itself instead of wrapped in an <c>AggregateException</c> that no ported arm expects.
    /// </remarks>
    private EventResult Ask(WireEventId eventId, Action<EventNotification> populate)
    {
        EventNotification question = new()
        {
            CorrelationId = Guid.NewGuid().ToString("n"),
            EventId = eventId,
            DatawindowHandle = _handle,
        };

        populate(question);

        return _responder.AskAsync(question, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    // ---------------------------------------------------------------------------------------------
    //  THE HOST CONTRACT - forty-eight members plus the object model, all forwarded to the shared host
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override object? ObjectModel => _host.ObjectModel;

    /// <inheritdoc/>
    public override IDataWindowObject GetObjectAttribute(string dwoName) =>
        _host.GetObjectAttribute(dwoName);

    /// <inheritdoc/>
    public override string Describe(string property) => _host.Describe(property);

    /// <inheritdoc/>
    public override long RowCount() => _host.RowCount();

    /// <inheritdoc/>
    public override long GetRow() => _host.GetRow();

    /// <inheritdoc/>
    public override int SetRow(long row) => _host.SetRow(row);

    /// <inheritdoc/>
    public override string GetColumnName() => _host.GetColumnName();

    /// <inheritdoc/>
    public override int AcceptText() => _host.AcceptText();

    /// <inheritdoc/>
    public override int SetRedraw(bool enable) => _host.SetRedraw(enable);

    /// <inheritdoc/>
    public override long GetRowIDFromRow(long row) => _host.GetRowIDFromRow(row);

    /// <inheritdoc/>
    public override long GetRowFromRowID(long rowId) => _host.GetRowFromRowID(rowId);

    /// <inheritdoc/>
    public override int SetSort(string sort) => _host.SetSort(sort);

    /// <inheritdoc/>
    public override int Sort() => _host.Sort();

    /// <inheritdoc/>
    public override int GroupCalc() => _host.GroupCalc();

    /// <inheritdoc/>
    public override bool IsEventDisabled(uint evt) => _host.IsEventDisabled(evt);

    /// <inheritdoc/>
    public override long DisableEvent(uint evt) => _host.DisableEvent(evt);

    /// <inheritdoc/>
    public override int EnableEvent(uint evt) => _host.EnableEvent(evt);

    /// <inheritdoc/>
    public override int SelectRow(long row, bool select) => _host.SelectRow(row, select);

    /// <inheritdoc/>
    public override bool IsSelected(long row) => _host.IsSelected(row);

    /// <inheritdoc/>
    public override long GetSelectedRow(long startRow) => _host.GetSelectedRow(startRow);

    /// <inheritdoc/>
    public override object? GetFocusedObject() => _host.GetFocusedObject();

    /// <inheritdoc/>
    public override int SetFocus() => _host.SetFocus();

    /// <inheritdoc/>
    public override ItemStatus GetItemStatus(long row, long columnId, DwBuffer buffer) =>
        _host.GetItemStatus(row, columnId, buffer);

    /// <inheritdoc/>
    public override int SetItemStatus(long row, long columnId, DwBuffer buffer, ItemStatus status) =>
        _host.SetItemStatus(row, columnId, buffer, status);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, string? value) =>
        _host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, decimal? value) =>
        _host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, long? value) =>
        _host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, DateTime? value) =>
        _host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, DateOnly? value) =>
        _host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, TimeOnly? value) =>
        _host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, object? value) =>
        _host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override string? GetItemString(long row, string column) =>
        _host.GetItemString(row, column);

    /// <inheritdoc/>
    public override decimal? GetItemDecimal(long row, string column) =>
        _host.GetItemDecimal(row, column);

    /// <inheritdoc/>
    public override double? GetItemNumber(long row, string column) =>
        _host.GetItemNumber(row, column);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, string? value) =>
        _host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, decimal? value) =>
        _host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, long? value) =>
        _host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override DateTime? GetItemDateTime(long row, string column) =>
        _host.GetItemDateTime(row, column);

    /// <inheritdoc/>
    public override DateOnly? GetItemDate(long row, string column) => _host.GetItemDate(row, column);

    /// <inheritdoc/>
    public override TimeOnly? GetItemTime(long row, string column) => _host.GetItemTime(row, column);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, DateTime? value) =>
        _host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, DateOnly? value) =>
        _host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, TimeOnly? value) =>
        _host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override long Find(string expression, long start, long end) =>
        _host.Find(expression, start, end);

    /// <inheritdoc/>
    public override long InsertRow(long row) => _host.InsertRow(row);

    /// <inheritdoc/>
    public override string GetValue(string column, long index) => _host.GetValue(column, index);

    /// <inheritdoc/>
    public override int GetChild(string column, ref IDataWindowChild? child) =>
        _host.GetChild(column, ref child);

    /// <inheritdoc/>
    /// <remarks>
    /// FORWARDED THROUGH THE HOST'S PUBLIC <c>Filter</c> RATHER THAN ITS PROTECTED CORE, because the
    /// protected member is not reachable across two unrelated types. The host's own <c>Filter</c> is the
    /// base's, which calls its core - so the behaviour reached is identical and the seam is respected.
    /// </remarks>
    protected override int FilterCore() => _host.Filter();

    /// <inheritdoc/>
    /// <remarks>Forwarded through the host's public member for the same reason as <see cref="FilterCore"/>.</remarks>
    protected override int DeleteRowCore(long row) => _host.DeleteRow(row);
}

/// <summary>
/// The shipped <see cref="IDataWindowUpdateContractProvider"/>: derives an update descriptor from the
/// registered definition a handle names.
/// </summary>
/// <param name="catalogue">The definition catalogue every registered handle resolves through.</param>
/// <remarks>
/// <para>
/// <b>WITHOUT THIS THE PREPARE STEP HAS NOTHING TO SEND, AND A MULTI-COLUMN CONCURRENCY CONTRACT
/// SILENTLY BECOMES NONE.</b> C-03's update path asks this seam for a descriptor and, on
/// <see langword="null"/>, names only the data object and lets the carrier's own definition govern. That
/// is the correct SINGLE-TABLE fallback, and it is also indistinguishable from a deployment in which the
/// seam was never bound - so an unregistered seam produces an update that runs, reports success, and
/// carries whatever predicate the carrier defaulted to rather than the one the definition declares.
/// </para>
/// <para>
/// THE FLAGS ARE READ FROM THE DEFINITION AND NEVER DEFAULTED. <c>updatewhere</c> and
/// <c>updatekeyinplace</c> are absent-or-present on the wire, and C-06 tests the two SEPARATELY - an
/// unset field there means "leave the carrier's own setting alone". So an empty declared value is
/// carried through as absence rather than as a fabricated zero or false, which is why
/// <see cref="DataWindowUpdateContract.UpdateWhere"/> and
/// <see cref="DataWindowUpdateContract.UpdateKeyInPlace"/> are nullable.
/// </para>
/// <para>
/// COLUMN ORDER IS THE DEFINITION'S, UNSORTED. C-06 walks the updatable and key arrays positionally
/// [<c>n_cst_thread_task_sqlupdate.sru:L111-L114</c>], and the evidenced definition declares its six
/// columns in a fixed order [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>].
/// </para>
/// <para>
/// FIRST WINS ON THE IDENTITY COLUMN, which reproduces the legacy's own first-wins resolution
/// [<c>n_cst_thread_task_sqlupdate.sru:L215-L245</c>] rather than refusing a definition that flagged two.
/// </para>
/// </remarks>
public sealed class HeadlessDataWindowUpdateContractProvider(DataWindowCatalogue catalogue)
    : IDataWindowUpdateContractProvider
{
    /// <inheritdoc/>
    public DataWindowUpdateContract? GetUpdateContract(string dataWindowHandle)
    {
        ArgumentNullException.ThrowIfNull(dataWindowHandle);
        ArgumentNullException.ThrowIfNull(catalogue);

        if (!catalogue.TryGet(dataWindowHandle, out DataWindowDefinition? definition)
            || definition.UpdateTable.Length == 0)
        {
            // No update table means no descriptor to send, which is ordinary rather than a fault: see the
            // seam's own remarks. A derived or read-only DataWindow declares none.
            return null;
        }

        List<string> updatable = [];
        List<string> keys = [];
        string identity = string.Empty;

        foreach (DataWindowObjectDefinition column in definition.Columns())
        {
            if (column.Update)
            {
                updatable.Add(column.Name);
            }

            if (column.Key)
            {
                keys.Add(column.Name);
            }

            if (column.Identity && identity.Length == 0)
            {
                identity = column.Name;
            }
        }

        return new DataWindowUpdateContract(
            definition.UpdateTable,
            updatable,
            keys,
            identity,
            ParseUpdateWhere(definition.UpdateWhere),
            ParseUpdateKeyInPlace(definition.UpdateKeyInPlace));
    }

    /// <summary>Reads the declared concurrency mode, or answers absence.</summary>
    /// <param name="declared">The declared value, as <c>DataWindow.Table.UpdateWhere</c> answers it.</param>
    /// <returns>The mode, or <see langword="null"/> when none was declared or it did not parse.</returns>
    /// <remarks>
    /// INVARIANT PARSING, because the value is a DataWindow describe string rather than user input, and a
    /// culture-sensitive parse of a machine-generated numeral is a defect waiting for a deployment in a
    /// different locale. An unparseable value answers ABSENCE rather than zero: zero is a real mode
    /// (key-only), and inventing it would silently narrow the predicate.
    /// </remarks>
    private static long? ParseUpdateWhere(string declared) =>
        long.TryParse(declared, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out long mode)
            ? mode
            : null;

    /// <summary>Reads the declared key handling, or answers absence.</summary>
    /// <param name="declared">The declared value, as <c>DataWindow.Table.UpdateKeyInPlace</c> answers it.</param>
    /// <returns>The setting, or <see langword="null"/> when none was declared.</returns>
    /// <remarks>
    /// THE DATAWINDOW SPELLING IS <c>yes</c>/<c>no</c>, not <c>true</c>/<c>false</c>, so a boolean parse
    /// would answer absence for every real definition. Compared ordinally and case-insensitively, because a
    /// describe string is ASCII and its case is not guaranteed.
    /// </remarks>
    private static bool? ParseUpdateKeyInPlace(string declared) => declared switch
    {
        _ when string.Equals(declared, "yes", StringComparison.OrdinalIgnoreCase) => true,
        _ when string.Equals(declared, "no", StringComparison.OrdinalIgnoreCase) => false,
        _ => null,
    };
}
