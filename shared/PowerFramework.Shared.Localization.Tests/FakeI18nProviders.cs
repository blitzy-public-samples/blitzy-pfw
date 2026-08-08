// ==============================================================================================
//  FakeI18nProviders - the shared, multi-consumer test doubles for II18nProvider
//  --------------------------------------------------------------------------------------------
//  CONTRACT UNDER DOUBLE   PowerFramework.Shared.Localization.II18nProvider
//                          (shared/PowerFramework.Shared.Localization/II18nProvider.cs)
//  BEHAVIOURAL             ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru:L9        the one member
//  ORACLE                  ws_objects/pfw.ui.pbl.src/i18n.srf:L12-L22        the three overloads
//                          ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru:L19-L26
//                                                                            the 1 / 0 alphabet
//                          Those .sru and .srf files are READ ONLY. They are the behavioural
//                          oracle for parity testing and are referenced here by line number in
//                          comments only - this file opens no path under ws_objects/ and reads no
//                          file at all.
//
//  THIS FILE CONTAINS NO TESTS, AND THAT IS A HARD PROPERTY RATHER THAN A HABIT
//  --------------------------------------------------------------------------------------------
//  It is fixture code: three hand-written implementations of the one-member provider contract plus
//  the small record their calls are recorded as. There is no test method here, no fact or theory
//  attribute, no inline or member data provider, and no xunit attribute of any kind on any of the
//  four types below - the file declares no attribute at all and imports no xunit namespace, so a
//  scan for either finds nothing. `dotnet test` must therefore discover exactly the same number of
//  tests with this file present as without it. If that count moves, an xunit attribute has leaked
//  onto a fixture type and the leak is the defect, not the count.
//
//  WHY HAND-WRITTEN DOUBLES AND NOT A MOCKING FRAMEWORK
//  --------------------------------------------------------------------------------------------
//  The dependency set for this refactor is fixed, and no mocking library is in it; neither is a
//  fluent assertion library. Adding one would be scope creep against the minimal-change clause,
//  and this project's own file records the same decision from the other side - its package list is
//  exhaustive by design and names this file as where the doubles live instead. The contract being
//  doubled is a single method with three parameters, so a hand-written class is also simply
//  smaller than the configuration a framework would need: none of the four types below has a base
//  class to construct, an interface beyond II18nProvider to satisfy, or a resource to dispose.
//
//  A mocking framework would additionally struggle with the one thing that matters most here. The
//  member mutates a `ref` parameter, and the whole point of the recorder below is to capture that
//  parameter's value BEFORE the mutation. Expressing "record the incoming value of a ref argument,
//  then overwrite it" is awkward in every mocking DSL and is four lines of ordinary C# here.
//
//  WHY THESE THREE LIVE IN A SHARED FILE WHILE OTHER DOUBLES DO NOT
//  --------------------------------------------------------------------------------------------
//  Only MULTI-CONSUMER doubles belong here. All three below are consumed by more than one suite in
//  this folder: the handled-returning and not-handled-returning pair are what a suite needs to
//  prove that the facade DISCARDS the provider's return code (both must produce the same visible
//  outcome for text the provider left alone), and the recorder is what the silent-passthrough and
//  installer suites need in order to observe the call at all.
//
//  A single-consumer double must NOT be added to this file. It stays a `private sealed class`
//  nested inside the one test class that needs it, which is the convention the sibling suites
//  already follow - see the nested probe and case types in
//  shared/PowerFramework.Shared.Kernel.Tests/TextTests.cs:L144 and :L514. Keeping that boundary is
//  what stops this file from slowly becoming a grab bag whose types nobody dares change because
//  the blast radius is unknown.
//
//  C-B - THE DOUBLES MODEL THE LEGACY CONTRACT AS IT IS, NOT A TIDIED VERSION OF IT
//  --------------------------------------------------------------------------------------------
//  This is the single most important constraint on this file, because a double that is nicer than
//  the contract stops proving anything about the contract. Two shapes are therefore reproduced
//  exactly, and neither may be "improved":
//
//    * THE RETURN IS A NUMERIC `long`, NOT A BOOLEAN. The legacy declares
//      `event type long ontranslate ( long source,  long category,  ref string text )`
//      [n_cst_i18n.sru:L9] and states the alphabet in the doc block all three shipped providers
//      carry identically - `返回1代表已处理`, "returning 1 means handled" [n_cst_i18n_chs.sru:L23,
//      n_cst_i18n_en.sru:L28, n_cst_i18n_cht.sru:L28]. So 1 means handled and 0 means not handled,
//      and the Simplified Chinese provider consists of nothing but that
//      [n_cst_i18n_chs.sru:L25-L26]:
//
//          if source = Enums.I18N_SRC_PFW then return 1
//          return 0
//
//      There is deliberately no enum, no boolean, no result record and no named Handled /
//      NotHandled constant anywhere below. The bare numerals 1 and 0 appear as literals in the
//      three bodies exactly as they appear in the oracle, because an abstraction over the numeric
//      code is precisely the improvement C-B forbids, and because the numerals are what travel in
//      serialized payloads and in characterization recordings.
//
//    * THE TRANSLATION IS DELIVERED THROUGH `ref string?`, NOT RETURNED. The provider rewrites the
//      caller's variable [`text = sTo` at n_cst_i18n_en.sru:L53, n_cst_i18n_cht.sru:L54] and the
//      facade returns that same variable on the very next line [i18n.srf:L18, :L22]. No double
//      below returns the translated text, takes an `out` parameter, or hands back a tuple.
//
//    The nullable annotation on `text` is likewise copied from the contract rather than chosen.
//    II18nProvider declares `ref string?` because PowerBuilder strings can genuinely be null and
//    the non-nullable form raises CS8601 under this repository's warnings-as-errors gate. A double
//    that narrowed it to `ref string` would not implement the interface at all.
//
//  THE OBLIGATION A 0 CARRIES: LEAVE THE TEXT EXACTLY AS YOU FOUND IT
//  --------------------------------------------------------------------------------------------
//  II18nProvider states it and the doubles honour it: a provider that answers 0 must not touch
//  `text`. This is why NotHandledProvider's body contains no assignment to `text` at all - not
//  even a self-assignment - and why the recorder writes only when its configured code is 1. With
//  no provider installed the legacy simply returns the text unchanged [i18n.srf:L17-L18], so a
//  provider answering 0 has to be indistinguishable from that. A double that assigned `text = text`
//  would still pass an equality assertion while destroying the ability of a future suite to prove
//  the stronger property.
//
//  WHAT THE RECORDER MAKES ASSERTABLE - WHY IT IS NOT OVER-ENGINEERING
//  --------------------------------------------------------------------------------------------
//  RecordingProvider is the only way to observe three properties of the facade, each of which is
//  invisible to an assertion on the returned text alone:
//
//   1. WHICH SOURCE EACH FACADE OVERLOAD SUPPLIES. The two-argument translating overload HARDCODES
//      the framework's own source - `n_cst_i18n.Event OnTranslate(Enums.I18N_SRC_PFW,category,ref
//      text)` [i18n.srf:L17] - while the three-argument overload passes the caller's source
//      straight through [i18n.srf:L21]. Both overloads can produce the identical translated text,
//      so the returned string cannot distinguish them. Only a recording of the `source` argument
//      can, and getting this wrong would silently break every caller that relies on a
//      caller-defined source reaching the provider.
//
//   2. THAT THE PROVIDER IS REACHED THROUGH AN INJECTED INSTANCE AND NOT A GLOBAL. The legacy held
//      a global auto-instance whose name shadows its own type name -
//      `global n_cst_i18n n_cst_i18n` [n_cst_i18n.sru:L11] - assigned by the installer overload
//      [i18n.srf:L13] and read by both translating overloads [:L17, :L21]. The port resolves that
//      collision by keeping the descriptive .NET name on the TYPE and making the INSTANCE an
//      injected dependency, so nothing declares a static ambient holder. The assertion that pins
//      it is: give two independent facade instances one recorder EACH, exercise both, and each
//      recorder must hold only its own calls. A static mutable slot would cross-contaminate them
//      and that test would fail - which is the whole point of writing it.
//
//   3. THAT THE FACADE INVOKES THE PROVIDER EXACTLY ONCE PER TRANSLATE CALL, with no retry and no
//      double dispatch. The legacy invokes the event as a single bare statement, so one call in is
//      one call out; a recorder's ordered call list is what turns that into an assertion.
//
//  THE CONSTANT COLLISION EVERY CONSUMER OF THESE DOUBLES INHERITS
//  --------------------------------------------------------------------------------------------
//  A hazard worth carrying forward, verified in the kernel constant catalogue and in its oracle
//  [ws_objects/pfw.shared.pbl.src/enums.sru:L115-L123]:
//
//      Enums.I18N_SRC_PFW     = 0        Enums.I18N_CAT_WINDOW        = 0
//      Enums.I18N_SRC_CUSTOM  = 1        Enums.I18N_CAT_TABCONTROL    = 1
//                                        Enums.I18N_CAT_RIBBONBAR     = 2
//                                        Enums.I18N_CAT_SPLITCONTAINER = 3
//                                        Enums.I18N_CAT_DATAWINDOW    = 4
//                                        Enums.I18N_CAT_CUSTOM        = 5
//
//  THE SOURCE SPACE AND THE CATEGORY SPACE OVERLAP AT 0 AND AGAIN AT 1. A suite that exercises a
//  double with source 0 and category 0 - the two values a careless fixture reaches for first -
//  cannot afterwards tell which argument the implementation actually read, because a recorded pair
//  of (0, 0) is consistent with reading either argument twice. The same trap is set at (1, 1).
//
//  This file supplies NO default for either argument, which is deliberate: both arrive as
//  parameters from the caller under test, so the choice belongs to the suite and cannot be made
//  safe from here. The obligation passed to every consumer is therefore stated rather than
//  defaulted - PICK A SOURCE AND A CATEGORY THAT DIFFER, and prefer a non-zero category such as
//  `Enums.I18N_CAT_DATAWINDOW` or `Categories.CAT_DWSVC` so that a recorded pair is unambiguous
//  about which slot each value came from.
//
//  C-H - WARNINGS ARE ERRORS HERE TOO, FIXTURE CODE INCLUDED
//  --------------------------------------------------------------------------------------------
//  Directory.Build.props sets Nullable, ImplicitUsings, EnableNETAnalyzers and
//  TreatWarningsAsErrors for every project in the tree including this one, and this project's file
//  carries no NoWarn. Fixture code is held to the same bar as the tests, so below: every field is
//  either initialised at its declaration or assigned by the constructor, every nullable reference
//  is annotated, nothing is dereferenced without the annotation permitting it, and there is no
//  `#pragma warning disable` and no suppression attribute anywhere in the file.
//
//  The repository-root .editorconfig scopes its CA1707 / IDE1006 naming suppressions BY FILE GLOB
//  to ten implementation files and to NO test file, so this file DECLARES only conventional
//  identifiers - PascalCase types and members, `_camelCase` private fields, no underscores in any
//  declared name. It REFERENCES the SCREAMING_SNAKE kernel constants only in documentation prose,
//  where naming analyzers do not reach.
//
//  C-F - NO CREDENTIAL MATERIAL, WHICH IS TRIVIAL TO GUARANTEE HERE
//  --------------------------------------------------------------------------------------------
//  Nothing below is a key, a token, a password or a connection string, and none may ever be added.
//  The only literal string in the file is a deliberately absurd, self-describing replacement marker
//  whose whole purpose is to be obvious in a failing assertion.
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//  --------------------------------------------------------------------------------------------
//    * Any using directive. The implicit usings supply System and System.Collections.Generic,
//      which is everything the four types need, and II18nProvider needs no using at all because
//      this namespace is a CHILD of the contract's namespace. In particular there is no
//      `using PowerFramework.Shared.Kernel;`: the kernel constants appear only as documentation
//      prose in `c` elements, exactly as II18nProvider.cs does for the same reason - a fixture
//      must not acquire a compile-time dependency on the catalogue of values that merely travel
//      through it. Consuming suites reference Enums directly and add the using themselves.
//    * A throwing double, a delay, a callback hook or a call-sequence script. None is needed by
//      any obligation this folder carries, and each would be new capability.
//    * A reset or clear method on the recorder. A double is cheap; a test that needs a clean
//      recorder constructs a new one, which is also what keeps two suites from sharing state.
//    * A base class or abstract shim shared by the three doubles. The contract has one member.
//      A hierarchy over one method would obscure the very thing each double exists to state
//      plainly.
//    * IDisposable on anything. None of the four types owns a resource.
// ==============================================================================================

namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// One recorded invocation of <see cref="II18nProvider.OnTranslate"/>: the two by-value arguments
/// as they arrived, and the value of the <c>ref</c> text argument BEFORE the callee mutated it.
/// </summary>
/// <param name="Source">
/// The <c>source</c> argument as received. Ported from the legacy <c>source:来源</c>
/// [<c>ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru</c>:L20]. Recording this is the
/// only way to tell the facade's two translating overloads apart, because the two-argument one
/// hardcodes <c>Enums.I18N_SRC_PFW</c> [<c>ws_objects/pfw.ui.pbl.src/i18n.srf</c>:L17] while the
/// three-argument one passes the caller's value through [:L21].
/// </param>
/// <param name="Category">
/// The <c>category</c> argument as received, the legacy <c>category:分类</c>
/// [<c>n_cst_i18n_chs.sru</c>:L21].
/// <para>
/// <b>Beware the overlap with <paramref name="Source"/>.</b> The source and category spaces share
/// the values 0 and 1 - <c>Enums.I18N_SRC_PFW</c> and <c>Enums.I18N_CAT_WINDOW</c> are both 0, and
/// <c>Enums.I18N_SRC_CUSTOM</c> and <c>Enums.I18N_CAT_TABCONTROL</c> are both 1
/// [<c>ws_objects/pfw.shared.pbl.src/enums.sru</c>:L115-L123]. A recorded pair in which the two
/// fields are equal cannot prove which argument the callee read, so a suite should exercise a
/// double with a source and a category that differ.
/// </para>
/// </param>
/// <param name="Text">
/// The <c>text</c> argument as it was on entry, captured before the recording provider assigned
/// anything to it. It is nullable because the contract's parameter is <c>ref string?</c> and a
/// PowerBuilder string can be null; a null here means the caller passed a null lookup key, which
/// is a legitimate call and not a fault.
/// </param>
/// <remarks>
/// <para>
/// A <see langword="readonly"/> <see langword="record"/> <see langword="struct"/> because a
/// recorded call is a small immutable value: it carries two 64-bit integers and one reference,
/// value equality is exactly the comparison a suite wants, and the compiler-generated
/// <c>ToString</c> names every member, which makes a failing collection assertion readable without
/// any extra formatting code.
/// </para>
/// <para>
/// It is a top-level type rather than one nested inside <see cref="RecordingProvider"/> for two
/// reasons: a consuming suite can name it without qualification, and no visible-nested-type
/// diagnostic arises under this repository's warnings-as-errors gate.
/// </para>
/// </remarks>
public readonly record struct I18nTranslateCall(long Source, long Category, string? Text);

/// <summary>
/// A provider that always reports HANDLED: it returns <c>1</c> and rewrites <c>text</c> to a
/// configurable replacement, for every source and every category.
/// </summary>
/// <remarks>
/// <para>
/// The positive half of the pair a suite needs in order to prove that the facade DISCARDS the
/// provider's return code. Paired with <see cref="NotHandledProvider"/> it isolates the return
/// value as the only difference between two calls, because this double's visible effect on
/// <c>text</c> is total and its partner's is nil.
/// </para>
/// <para>
/// Unconditional by design. The three shipped providers all gate on the source - by an equality
/// test at <c>ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru</c>:L25 and by an early
/// inequality guard at <c>n_cst_i18n_en.sru</c>:L32 - but a double that reproduced that gate would
/// make every assertion about it conditional on the suite having picked the right source, which is
/// the opposite of what a fixture is for. Faithfulness to the legacy's GATE belongs in the tests
/// for the three real providers; faithfulness to the legacy's SHAPE - a numeric return plus a
/// <c>ref</c> mutation - is what this double owes, and it is preserved exactly.
/// </para>
/// </remarks>
public sealed class HandledProvider : II18nProvider
{
    /// <summary>
    /// The replacement written when no other is supplied to the constructor.
    /// </summary>
    /// <remarks>
    /// Chosen to be impossible to mistake for a real translation, for a lookup key, or for text any
    /// other fixture in this folder produces, so that it is immediately recognisable in a failing
    /// assertion's message. It is a plain marker string and carries no credential, key or token of
    /// any kind. A suite that wants to assert on a value of its own choosing passes one to the
    /// constructor instead of relying on this.
    /// </remarks>
    public const string DefaultReplacement = "<<PFW-TRANSLATED>>";

    private readonly string? _replacement;

    /// <summary>
    /// Creates a provider that rewrites <c>text</c> to <paramref name="replacement"/> and reports
    /// handled.
    /// </summary>
    /// <param name="replacement">
    /// The value written to the <c>ref</c> text argument on every call. Defaults to
    /// <see cref="DefaultReplacement"/>.
    /// <para>
    /// <see langword="null"/> is deliberately permitted and is not validated. The contract's text
    /// parameter is <c>ref string?</c> because a PowerBuilder string can be null, so a suite that
    /// needs to prove a null translation survives the round trip has to be able to construct a
    /// provider that writes one. Rejecting null here would make that case untestable.
    /// </para>
    /// </param>
    public HandledProvider(string? replacement = DefaultReplacement)
    {
        _replacement = replacement;
    }

    /// <summary>
    /// The value this provider writes to the <c>ref</c> text argument, as supplied to the
    /// constructor.
    /// </summary>
    /// <value>
    /// The constructor's <c>replacement</c> argument, which may be <see langword="null"/>.
    /// </value>
    /// <remarks>
    /// Exposed so that a suite can assert against the value it configured without restating the
    /// literal, which keeps the expectation and the configuration from drifting apart.
    /// </remarks>
    public string? Replacement => _replacement;

    /// <summary>
    /// Rewrites <paramref name="text"/> to <see cref="Replacement"/> and returns <c>1</c>.
    /// </summary>
    /// <param name="source">
    /// Ignored. This double does not gate on the source; see the type-level remarks.
    /// </param>
    /// <param name="category">
    /// Ignored. This double does not gate on the category.
    /// </param>
    /// <param name="text">
    /// Overwritten with <see cref="Replacement"/>. Its incoming value is neither read nor
    /// validated, so a null lookup key is accepted rather than throwing - which is the behaviour
    /// II18nProvider requires of every implementation.
    /// </param>
    /// <returns>
    /// Always <c>1</c>, the legacy's code for handled - stated in the oracle as
    /// <c>返回1代表已处理</c> [<c>n_cst_i18n_chs.sru</c>:L23] and returned there as a bare literal
    /// [:L25]. It is a numeral rather than a named constant or an enum member on purpose; see the
    /// C-B section of this file's header.
    /// </returns>
    public long OnTranslate(long source, long category, ref string? text)
    {
        text = _replacement;

        return 1;
    }
}

/// <summary>
/// A provider that always DECLINES: it returns <c>0</c> and leaves <c>text</c> completely
/// untouched, for every source and every category.
/// </summary>
/// <remarks>
/// <para>
/// The negative half of the pair described on <see cref="HandledProvider"/>, and the double that
/// models the legacy's ordinary miss. All three shipped providers reach this outcome - the
/// Simplified Chinese one by returning 0 for any source other than the framework's own
/// [<c>ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru</c>:L26], the two table-driven
/// ones by falling through their category switch and leaving the section key empty
/// [<c>n_cst_i18n_en.sru</c>:L34-L47, :L155].
/// </para>
/// <para>
/// <b>Its body assigns nothing at all - not even <c>text = text</c>.</b> That is the entire point
/// of the double, and the distinction is observable: a self-assignment would still satisfy an
/// equality assertion, so it would let a genuinely misbehaving implementation pass a suite whose
/// purpose is to prove the stronger property that II18nProvider states - a provider answering 0
/// must leave the text exactly as it found it. A suite pins that with an ordinal comparison against
/// the original input after the call.
/// </para>
/// <para>
/// It is also the double that makes the facade's silent-passthrough fallback assertable against a
/// provider that IS installed. With no provider at all the legacy returns the text unchanged
/// [<c>ws_objects/pfw.ui.pbl.src/i18n.srf</c>:L17-L18], and a provider answering 0 has to be
/// indistinguishable from that; this double is the second half of that comparison.
/// </para>
/// </remarks>
public sealed class NotHandledProvider : II18nProvider
{
    /// <summary>
    /// Returns <c>0</c> without touching <paramref name="text"/>.
    /// </summary>
    /// <param name="source">
    /// Ignored. This double declines unconditionally.
    /// </param>
    /// <param name="category">
    /// Ignored. This double declines unconditionally.
    /// </param>
    /// <param name="text">
    /// Neither read nor written. A caller's variable is returned to it byte for byte, null
    /// included.
    /// </param>
    /// <returns>
    /// Always <c>0</c>, the legacy's code for not handled, returned there as a bare literal
    /// [<c>n_cst_i18n_chs.sru</c>:L26].
    /// </returns>
    public long OnTranslate(long source, long category, ref string? text)
    {
        return 0;
    }
}


/// <summary>
/// A provider that records every invocation it receives - source, category and the INCOMING text -
/// and answers with a code the suite configures, writing a configured replacement only when that
/// code is <c>1</c>.
/// </summary>
/// <remarks>
/// <para>
/// The load-bearing double of the three. It is the only way to observe the three properties set out
/// in this file's header: which source each facade overload supplies, that the provider is reached
/// through an injected instance rather than a static ambient slot, and that one translate call
/// produces exactly one dispatch. None of the three is visible in the returned text.
/// </para>
/// <para>
/// <b>It records before it mutates.</b> <see cref="I18nTranslateCall.Text"/> therefore holds the
/// lookup key the caller passed in, never the replacement this provider went on to write. That
/// ordering is the whole reason the recorder exists rather than an assertion on the facade's return
/// value: after the call the caller's variable holds the OUTPUT, so the INPUT is unrecoverable from
/// anywhere else.
/// </para>
/// <para>
/// <b>It honours the obligation a 0 carries.</b> When <see cref="ReturnCode"/> is anything other
/// than <c>1</c> the text is left exactly as it arrived, because II18nProvider requires a provider
/// that does not report handled to leave the caller's variable alone. That also makes the default
/// configuration - see <see cref="ReturnCode"/> - a pure observer that changes nothing it watches.
/// </para>
/// <para>
/// Not thread-safe, deliberately. The contract it doubles is synchronous and the legacy TRIGGERS
/// the event rather than posting it, so a call completes before the next statement runs; nothing in
/// this folder dispatches a translation concurrently. Adding a lock would imply a concurrency
/// guarantee the contract does not make.
/// </para>
/// </remarks>
public sealed class RecordingProvider : II18nProvider
{
    /// <summary>
    /// The backing store, in the order the calls arrived. Mutated only by
    /// <see cref="OnTranslate"/>, and exposed only through the read-only view held in
    /// <see cref="Calls"/>.
    /// </summary>
    private readonly List<I18nTranslateCall> _received = [];

    /// <summary>
    /// Creates a recorder configured to answer <paramref name="returnCode"/> and, when that code is
    /// <c>1</c>, to write <paramref name="replacement"/>.
    /// </summary>
    /// <param name="returnCode">
    /// The code every call answers with. Defaults to <c>0</c> - not handled - which makes a
    /// default-constructed recorder a pure observer.
    /// </param>
    /// <param name="replacement">
    /// The value written to the <c>ref</c> text argument when <paramref name="returnCode"/> is
    /// <c>1</c>. Defaults to <see langword="null"/>, which is inert while the code stays <c>0</c>.
    /// </param>
    /// <remarks>
    /// Both arguments are optional, so all three construction styles work and a suite can pick
    /// whichever reads best at the call site: <c>new RecordingProvider()</c> for a pure observer,
    /// <c>new RecordingProvider(1, "expected")</c> positionally, or an object initializer such as
    /// <c>new RecordingProvider { ReturnCode = 1 }</c>. The corresponding properties stay settable
    /// so that one recorder can be reconfigured between calls, which is what a suite asserting on an
    /// ordered SEQUENCE of differing outcomes needs.
    /// </remarks>
    public RecordingProvider(long returnCode = 0, string? replacement = null)
    {
        ReturnCode = returnCode;
        Replacement = replacement;
        Calls = _received.AsReadOnly();
    }

    /// <summary>
    /// Every call this provider has received, oldest first.
    /// </summary>
    /// <value>
    /// A live read-only view over the recorder's backing list: it reflects calls made after it was
    /// read, and it cannot be added to, cleared or reordered through this property.
    /// </value>
    /// <remarks>
    /// The view is created once by the constructor rather than on each read, so the property returns
    /// the same instance every time and a suite may safely capture it before exercising the system
    /// under test. Order is significant - it is what makes "the second call carried the caller's own
    /// source" expressible - so this is an
    /// <see cref="IReadOnlyList{T}"/> rather than an unordered collection.
    /// </remarks>
    public IReadOnlyList<I18nTranslateCall> Calls { get; }

    /// <summary>
    /// The number of calls received.
    /// </summary>
    /// <value>
    /// <see cref="Calls"/>.<c>Count</c>.
    /// </value>
    /// <remarks>
    /// Derived from <see cref="Calls"/> on purpose. A separate counter field would be a second
    /// source of truth for the same fact and could disagree with the list after a future edit,
    /// turning a fixture into something that itself needs testing.
    /// </remarks>
    public int CallCount => Calls.Count;

    /// <summary>
    /// The code every call answers with, and the switch that decides whether
    /// <see cref="Replacement"/> is written.
    /// </summary>
    /// <value>
    /// <c>1</c> for handled or <c>0</c> for not handled; <c>0</c> unless the constructor or a
    /// caller set otherwise.
    /// </value>
    /// <remarks>
    /// <para>
    /// The contract's alphabet is exactly <c>1</c> and <c>0</c>, so those are the only two values
    /// with a meaning. This property is nonetheless a plain <see langword="long"/> and is not
    /// validated, for the same reason the contract's return type is a <see langword="long"/>: a
    /// suite proving that the facade DISCARDS the provider's answer needs to be able to make that
    /// answer arbitrary. Any value other than <c>1</c> is treated as not handled and therefore
    /// leaves the text untouched.
    /// </para>
    /// <para>
    /// Settable rather than construction-only so that a single recorder can answer differently on
    /// successive calls within one recorded sequence.
    /// </para>
    /// </remarks>
    public long ReturnCode { get; set; }

    /// <summary>
    /// The value written to the <c>ref</c> text argument when <see cref="ReturnCode"/> is <c>1</c>.
    /// </summary>
    /// <value>
    /// The replacement text, which may be <see langword="null"/> to model a translation to a null
    /// PowerBuilder string.
    /// </value>
    /// <remarks>
    /// Ignored entirely while <see cref="ReturnCode"/> is not <c>1</c>, so leaving it at its
    /// <see langword="null"/> default is safe for a pure observer.
    /// </remarks>
    public string? Replacement { get; set; }

    /// <summary>
    /// Records the call, then returns <see cref="ReturnCode"/> - writing <see cref="Replacement"/>
    /// to <paramref name="text"/> only when that code is <c>1</c>.
    /// </summary>
    /// <param name="source">
    /// Recorded verbatim into <see cref="I18nTranslateCall.Source"/>. This is the argument that
    /// distinguishes the facade's two translating overloads
    /// [<c>ws_objects/pfw.ui.pbl.src/i18n.srf</c>:L17 versus :L21].
    /// </param>
    /// <param name="category">
    /// Recorded verbatim into <see cref="I18nTranslateCall.Category"/>. Remember that it shares the
    /// values 0 and 1 with the source space
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru</c>:L115-L123], so a suite should pass a category
    /// that differs from the source it passed.
    /// </param>
    /// <param name="text">
    /// Recorded as it arrives - before anything is written to it - and then overwritten with
    /// <see cref="Replacement"/> if and only if <see cref="ReturnCode"/> is <c>1</c>. Its incoming
    /// value is never dereferenced, so a null lookup key is recorded as null rather than throwing.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode"/>, unchanged and uninterpreted.
    /// </returns>
    public long OnTranslate(long source, long category, ref string? text)
    {
        // RECORD FIRST. `text` still holds the caller's incoming value at this point, and that
        // value is unrecoverable once the assignment below has run, because the caller's variable
        // IS the storage. Reordering these two statements would silently turn every recorded entry
        // into the output it was supposed to be compared against.
        _received.Add(new I18nTranslateCall(source, category, text));

        // Write ONLY when reporting handled. II18nProvider requires that a provider which does not
        // report handled leave the caller's variable exactly as it found it, so that it stays
        // indistinguishable from the no-provider-installed passthrough at
        // ws_objects/pfw.ui.pbl.src/i18n.srf:L17-L18. The comparison is against the literal 1
        // rather than a truth test because 1 and 0 are the contract's whole alphabet.
        if (ReturnCode == 1)
        {
            text = Replacement;
        }

        return ReturnCode;
    }
}

