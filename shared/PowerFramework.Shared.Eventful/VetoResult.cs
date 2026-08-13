// ==============================================================================================
// VetoResult.cs
// The event broker's tri-valued dispatch-veto contract.
// ==============================================================================================
//
// WHAT THIS FILE IS
// One enum, and deliberately nothing else. It names the three states of the broker's "prevent"
// flag: the state a subscriber puts the broker into when it wants the dispatch in progress to
// stop, and the state the dispatch loop tests on every iteration to decide whether to keep
// delivering to the next subscriber.
//
// AUTHORITATIVE SOURCE, AND THE EXACT DECLARATION THIS FILE PORTS
//     ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L110-L112
//
//         //Prevent flags
//         constant long PREVENT_ONCE = 1
//         constant long PREVENT_DEEP = 2
//
// The legacy declares only the two non-zero flags. The third state is the implicit zero of the
// private field that carries them, `long _nPrevent` at :L85, which `_of_trigger` sets to 0 on
// entry to a dispatch level (:L813) and clears back to 0 on unwind (:L957). This port gives that
// zero the name Continue instead of leaving it anonymous, because an unnamed default is a state
// nobody can assert against in a test or read in a recording. The numeric value is unchanged, so
// nothing observable moves: naming a value is not changing it.
//
// PowerBuilder's `long` is 32 bits wide, so the faithful C# width is the enum's DEFAULT
// underlying type, int. It is left implicit rather than written as `: long`, which would double
// the width of a value that travels on a service contract for no legacy reason.
//
// WHY THIS IS A THREE-VALUED TYPE AND NOT A BOOLEAN
// Because once and deep differ in what survives the unwind of a nested dispatch, and a boolean
// cannot carry that difference. Collapsing them would silently turn every deep prevention into a
// shallow one, and only for nested dispatches - the failure mode hardest to notice and hardest to
// attribute. The mechanism is documented in full on the type below, traced to the two branches of
// the `finally` block at :L954-L957 that produce it.
//
// THE NAMING DECISION, RECORDED BECAUSE THE FILE LOOKS LIKE A CONSTANT CATALOGUE AND IS NOT ONE
// Members are declared in PascalCase - Continue, PreventOnce, PreventDeep - with the legacy
// SCREAMING_SNAKE spellings preserved in the documentation of each member rather than in its
// identifier. That is a deliberate departure from the sibling constant catalogues (RetCode.cs,
// Enums.cs, Categories.cs and the rest), which keep their legacy spellings verbatim in the
// identifier itself, and the distinction is worth stating precisely so neither side reads as an
// accident:
//
//   * For those catalogues the IDENTIFIER is the recording-visible artifact. They are named
//     constants consumed by name across log records and serialized payloads, so a rename
//     invalidates stored comparisons.
//   * For this type the VALUES are the recording-visible artifact. 0, 1 and 2 are what the broker
//     writes into `_nPrevent`, what the dispatch loop compares, and what crosses a contract. This
//     file therefore fixes those three numbers exactly and treats the C# identifier as
//     presentation, which is why the rename costs nothing here and would cost data there.
//
// Both legacy spellings are still recoverable from this file without leaving it: each appears in
// the summary of the member that carries its value, so a reader arriving from the PowerScript can
// map either direction. The repository-root .editorconfig additionally carries a naming-analyzer
// band scoped to this exact path; that band is permissive rather than prescriptive - it only
// lowers two diagnostics to none - so PascalCase members leave it inert and correct rather than
// violated. It is deliberately left exactly as it stands: narrowing or widening a .editorconfig
// glob from here is out of this file's scope.
//
// DELIBERATELY ABSENT, EACH FOR A STATED REASON, so no reader "completes" this file by adding one
//
//   * [Flags]. The three states are mutually exclusive, not composable. `PreventOnce | PreventDeep`
//     is 3, and 3 is not a state the legacy can ever hold: `of_prevent` assigns one flag or the
//     other by an if/else (:L1294-L1298) and never combines them. Marking this enum [Flags] would
//     advertise a combination the broker cannot produce and cannot interpret, and would make
//     ToString render 3 as a plausible-looking "PreventOnce, PreventDeep" instead of the invalid
//     value it is. The repository-root .editorconfig deliberately declines to suppress CA1027
//     ("Mark enums with FlagsAttribute") for any file, on the stated ground that an enum-shape
//     finding should reach the author rather than be silenced, so the answer to it is recorded here
//     rather than suppressed anywhere. Measured on the pinned SDK, this file raises CA1027 at neither
//     the default analysis mode nor AnalysisMode=All; should a future toolchain raise it, the
//     finding is a false positive produced by the values merely happening to be 1 and 2, and the
//     correct resolution is to leave the attribute off - neither adding it nor silencing the rule.
//   * Any alias member. Exactly three members exist, so Enum.GetValues and Enum.GetNames both
//     return three entries and a test can assert that count. The legacy has no second spelling for
//     either flag, unlike the return-code algebra where CANCELED and CANCELLED are genuinely two
//     names for one value.
//   * Any helper that maps to or from the other two numeric alphabets described on the type below.
//     Those alphabets belong to the broker's subclassing hooks, so their translation belongs in
//     EventBroker.cs where the hooks are invoked, next to the code that can be read against the
//     `choose case` it reproduces. Putting it here would make this leaf type depend on the
//     semantics of code that lives above it.
//   * Any using directive. This type needs no import at all, not even one the implicit usings
//     already supply, and no ProjectReference of its own: it is a pure leaf, which is what lets
//     both in-scope services depend on it without inheriting anything else.
//
// THE LEGACY TREE IS READ-ONLY AND SHARES THIS WORKING DIRECTORY
// Every `:Lnnn` locator above and below points into ws_objects/, which is the behavioural oracle
// for parity testing: read as specification, never edited, moved or reformatted, and never a build
// input. The line numbers were verified against the file on disk rather than copied forward.
// ==============================================================================================

namespace PowerFramework.Shared.Eventful
{
    /// <summary>
    /// The three states of the event broker's dispatch veto: no veto in effect, veto the dispatch
    /// level that raised it, or veto that level and every enclosing level as the call stack
    /// unwinds. Ported from the two <c>PREVENT_*</c> flags and the implicit zero of the private
    /// <c>_nPrevent</c> field in
    /// <c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>How a dispatch level aborts.</b> The broker runs one loop per dispatch level over its
    /// ordered subscription table, and the loop's guard on every iteration is
    /// <c>if _nPrevent &lt;&gt; 0 then exit</c> [n_cst_eventful.sru:L822]. The test is simply
    /// "the state is not <see cref="Continue"/>": <see cref="PreventOnce"/> and
    /// <see cref="PreventDeep"/> are indistinguishable to it, and both stop delivery to any
    /// remaining subscriber at the level where they were raised. The entire once-versus-deep
    /// difference is in what survives the unwind, not in what aborts.
    /// </para>
    /// <para>
    /// <b>What survives the unwind.</b> On entry a level saves the enclosing level's veto state
    /// and zeroes the field [:L812-L813]. Its <c>finally</c> block then restores the enclosing
    /// depth [:L952] and decides in two branches [:L954-L957]: if the saved enclosing state was
    /// non-zero it is restored verbatim; otherwise the field is cleared <i>only</i> when it
    /// currently holds <see cref="PreventOnce"/>, or when the dispatch depth has returned to zero.
    /// Consequently a <see cref="PreventOnce"/> raised at an inner level is consumed by that level
    /// and the enclosing loop resumes delivering, whereas a <see cref="PreventDeep"/> is left
    /// standing, is seen by the enclosing level's own guard on its next iteration, and so keeps
    /// aborting outward until the depth reaches zero. This is precisely why the veto must never be
    /// flattened to a boolean: flattening converts every deep prevention into a shallow one,
    /// silently, and only for nested dispatches.
    /// </para>
    /// <para>
    /// <b>Where the state may be set, and when setting it is meaningful.</b> Only the broker's
    /// prevent operation assigns it, from the legacy <c>of_prevent</c> [:L1276-L1303], whose
    /// parameterless overload delegates with a false <c>deep</c> argument [:L1302] and therefore
    /// means <see cref="PreventOnce"/>. A veto is meaningful only inside a dispatch: when the
    /// dispatch depth is at or below zero, <c>of_prevent</c> leaves the state untouched and returns
    /// the failure code [:L1293]. That guard is the broker's to enforce and is not modelled here,
    /// but it is recorded so this contract is legible from the type alone.
    /// </para>
    /// <para>
    /// <b>Warning - three distinct numeric alphabets share the numerals 1 and 2, and this type is
    /// only the first of them.</b> Conflating any two of the three is the single most likely error
    /// in this area, because the numbers coincide while the meanings do not. The legacy uses all
    /// three within one dispatch, and the subclass at
    /// <c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L48-L53</c> uses the first two
    /// four lines apart - calling the prevent operation to set the state, then returning 1 as a
    /// return code - which proves they are separate mechanisms rather than one value read twice.
    /// </para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// <b>The veto state - this type.</b> 0 continue, 1 once, 2 deep. Written only by the broker's
    /// prevent operation and read only by the dispatch guard and the unwind branches described
    /// above.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>The prepare and triggering hook return value - a return code, not this type.</b> The
    /// legacy tests it with the shared <c>IsPrevented</c> predicate, which compares against
    /// <c>RetCode.PREVENT</c> (1) [ws_objects/pfw.shared.pbl.src/retcode.sru:L42], at
    /// <c>n_cst_eventful.sru:L609</c> when passing arguments to a subscriber and at <c>:L840</c>
    /// before the first delivery of a dispatch. These hooks return an integer return code checked
    /// through that predicate, never a value of this enum.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>The exception hook return value - a third alphabet reusing the same numerals with
    /// different meanings.</b> At <c>n_cst_eventful.sru:L889-L895</c> a returned 1 means prevent
    /// and exits the dispatch loop, a returned 2 means continue and additionally clears the
    /// has-exception latch before moving to the next subscriber, and any other value falls through
    /// to rethrow [:L902]. Note that 2 means <i>continue</i> there and <i>deep prevention</i> here:
    /// the two are near opposites at the same numeral. That alphabet is owned by the broker, and
    /// this type must not be substituted for it.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    public enum VetoResult
    {
        /// <summary>
        /// No veto is in effect and dispatch proceeds to the next subscriber. Value <c>0</c>.
        /// Legacy spelling: none - this state is the implicit zero of the private
        /// <c>long _nPrevent</c> field [n_cst_eventful.sru:L85]. Every dispatch level zeroes that
        /// field on entry [:L813], and the unwind clears it back to zero [:L957] whenever a
        /// shallow veto has been consumed or the dispatch depth has returned to zero. It is named
        /// here rather than left anonymous so that it can be asserted in a test and read in a
        /// characterization recording; the value itself is untouched.
        /// </summary>
        Continue = 0,

        /// <summary>
        /// Veto the dispatch level that raised it, and that level only. Value <c>1</c>. Legacy
        /// spelling <c>PREVENT_ONCE</c> [n_cst_eventful.sru:L111]; also the state produced by the
        /// parameterless legacy prevent overload, which delegates with a false <c>deep</c>
        /// argument [:L1302]. It stops delivery to every remaining subscriber at its own level and
        /// is then consumed by that level's unwind [:L956-L957], so an enclosing dispatch resumes
        /// delivering as though no veto had been raised.
        /// </summary>
        PreventOnce = 1,

        /// <summary>
        /// Veto the dispatch level that raised it and every enclosing level as the call stack
        /// unwinds. Value <c>2</c>. Legacy spelling <c>PREVENT_DEEP</c>
        /// [n_cst_eventful.sru:L112]. It aborts its own level exactly as
        /// <see cref="PreventOnce"/> does, but survives the unwind [:L956-L957] and is therefore
        /// seen by the enclosing level's dispatch guard [:L822], continuing to abort outward until
        /// the dispatch depth reaches zero. This survival is the whole reason the veto is
        /// three-valued rather than boolean.
        /// </summary>
        PreventDeep = 2
    }
}
