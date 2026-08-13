// =====================================================================================================
//  SubscriptionTopic.cs
//  PowerFramework.Shared.Eventful
// =====================================================================================================
//
//  WHAT THIS FILE IS
//  The DECOMPOSED topic identity of the event broker, replacing the legacy's single fused topic string.
//  It is a pure leaf: it decodes a topic string into named fields, re-emits it, orders two of them, and
//  answers one match question. It performs no I/O, writes no log, holds no subscription state, owns no
//  callback and throws no exception for a malformed topic - a parse failure is reported the way the
//  legacy reports it, as a return code. Within this project it consumes only SubscriptionOptions.cs, and
//  from outside it only PowerFramework.Shared.Kernel's RetCode. It deliberately does NOT reference
//  EventBroker.cs; dispatch, subscription storage, the deferred sweep and the broker-level `excluding`
//  inversion all live there.
//
//  WHY IT EXISTS - THE WIRE-TRIPLE DECOMPOSITION (AAP 0.4.2.3 and 0.6.1.2; C-K decision 1 of 4)
//  AAP 0.4.2.3 gives this file its own transformation row: "Decompose the fused topic string into
//  sequence, name and lifetime; reconstitute the legacy form only at the compatibility edge." AAP 0.6.1.2
//  states the mechanism it is protecting against: the legacy topic string "fuses three independent
//  encodings - ordering, logical identity and lifetime - into one opaque value ... transmit the string and
//  the ordering becomes invisible; parse the string at the far end and the contract has an undocumented
//  grammar."
//
//  This type is therefore the ONLY place in the .NET tree that knows the grammar, and the boundary rule
//  is one-directional and absolute:
//
//      * A CONTRACT carries the decomposed fields - Sequence, LogicalName, Lifetime and the option and
//        filter fields beside them. It never carries a topic string and calls it a name.
//      * The FUSED legacy string is reconstituted at the compatibility edge ONLY, by ToLegacyString(),
//        for the one purpose of speaking to something that still expects the legacy spelling: a
//        characterization recording, a log line to be diffed against the oracle, or a legacy-shaped call.
//
//  The decomposition is a PROJECTION, never a replacement. LegacyName remains the authoritative name, and
//  Sequence and LogicalName are computed from it rather than stored beside it, so the two can never
//  disagree. See the reconstitution invariant on LegacyName and the trap note below.
//
//  AUTHORITATIVE SOURCE (the behavioural oracle - read as specification, never edited: AAP 0.7.3 C-C)
//      ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru   (1,328 lines, ZERO native declarations
//                                                                   - every rule below is readable
//                                                                   PowerScript, nothing is hidden in a
//                                                                   closed binary)
//          :L91-L98      the eight grammar symbols
//          :L100-L112    the capture flags, the priorities, the modification kinds, the prevent flags
//          :L12-L25      the EVENTDATA structure, whose `name` field is what LegacyName ports (:L15)
//          :L326-L381    of_on - THE SUBSCRIBE GRAMMAR, reproduced statement for statement by
//                        ParseSubscription
//          :L407-L422    of_on - the ordered insert that fixes the dispatch ordering key, reproduced by
//                        CompareDispatchOrder
//          :L439-L440    of_on - the lexical name bounds, maintained with PowerScript's string relational
//                        operators
//          :L767-L770    of_has - those bounds used as a fast reject
//          :L797-L798    _of_trigger - the same fast reject on the dispatch path
//          :L993-L1090   _of_modify - THE FILTER GRAMMAR, the single engine behind all seven of_off and
//                        all seven of_disable overloads, reproduced by ParseFilter
//
//      NOTE ON LINE NUMBERS. Every locator in this file was re-verified against the oracle character by
//      character while it was written. Several differ by one to three lines from the citations in the
//      task brief; where they differ, the numbers here are the verified ones.
//
//  REFERENCE SOURCES (read, never ported, never edited)
//      ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L47-L76
//          The twelve live DataWindow topics. Two of them, EVT_ITEMCHANGED = "0-itemchanged" (:L54) and
//          EVT_EDITCHANGED = "1-editchanged" (:L57), are the whole reason the sequence prefix exists and
//          the whole reason the prepend symbol must never be recognised outside the leading run.
//      ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L2429
//          of_On("!" + EVT_ITEMCHANGED, this, "onItemChanged"), i.e. the real topic "!0-itemchanged".
//          Worth recording precisely because a grep for topic literals reports a bare "!" here and that
//          reads like a standalone topic. It is not: it is a concatenation prefix. The symbols-only guard
//          is nonetheless real, so ParseSubscription("!") does fail - see its remarks.
//      ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544 and :L593-L598
//          The ".^persistent" bulk unsubscribe and the caller-side rule that produces it. Six sites across
//          three objects: n_cst_threading.sru:L544 and :L596, n_cst_threading_task.sru:L385 and :L391,
//          n_cst_thread.sru:L552 and :L561.
//      ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L207-L248
//          The in-source grammar specification. AAP 0.2.2.3 places the whole of pfw.tests permanently out
//          of scope as characterization-fixture and REFERENCE source. It is authoritative where it and the
//          implementation agree; the IMPLEMENTATION is authoritative where they differ.
//
//  BINDING CONSTRAINTS AT THIS SITE
//      C-B   No behaviour improvements and no legacy-defect correction. Both grammars are reproduced
//            exactly, INCLUDING the asymmetries between them, and no validation the legacy lacks is
//            added. The two load-bearing asymmetries: an empty namespace is an ERROR when subscribing
//            (:L379) and a MEANINGFUL FILTER when modifying (:L1002 with nothing after the separator);
//            and a non-numeric prefix before a colon is silently left in the name (:L368), never
//            rejected. Neither is harmonised.
//      C-C   The legacy tree is read-only and is the oracle. Every .sru path above is specification only
//            and never a build input; nothing here compiles or embeds a legacy path.
//      C-K   Every boundary-specific decision is documented at its point of reproduction. The four this
//            file is required to carry are each marked DECISION and are: (1) the wire-triple
//            decomposition and the compatibility-edge rule, in this header; (2) that LegacyName rather
//            than LogicalName is the dispatch and sort key, on LegacyName and on CompareDispatchOrder;
//            (3) the two-grammar split, on TopicGrammar and on both parse entry points; (4) that
//            PowerBuilder's string relational comparison is reproduced with StringComparer.Ordinal, on
//            CompareDispatchOrder. Three further decisions are marked the same way where they arise: the
//            code-returning parse shape, the sequence-prefix recognition guard, and the numeric-priority
//            acceptance narrowing.
//
//  NAMING: PascalCase MEMBERS, LEGACY SPELLINGS IN THE DOC COMMENTS ONLY
//  The repository-root .editorconfig scopes its naming-analyzer suppressions file by file to those that
//  genuinely must carry the preserved SCREAMING_SNAKE constant identifiers. This file is NOT one of them -
//  its sibling VetoResult.cs is, because PREVENT_ONCE and PREVENT_DEEP travel on the wire - and
//  Directory.Build.props sets TreatWarningsAsErrors, so every member here is PascalCase and each legacy
//  spelling is recorded in its own member's doc comment instead. No widening of that glob list was
//  requested and none is needed: not one identifier SPELLING in this file crosses a wire. The topic
//  STRINGS do, and they are reproduced byte for byte.
//
//  A related forward-compatibility choice, since the .editorconfig warns in as many words that raising
//  AnalysisMode is a one-property change: this file exposes no public nested type (CA1034), implements no
//  IComparable without the comparison operators (CA1036), and keeps its only out-parameters on methods
//  whose entire purpose is to return a code alongside a value. Nothing here depends on the analysis mode
//  staying at its default.
// =====================================================================================================

using System.Globalization;

using PowerFramework.Shared.Kernel;

namespace PowerFramework.Shared.Eventful
{
    /// <summary>
    /// Discriminates which of the legacy's <b>two</b> topic grammars a <see cref="SubscriptionTopic"/>
    /// was decoded under, and therefore which of its fields carry meaning.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>DECISION (AAP 0.7.3 C-K) - there are two grammars over the same eight symbols, not one, and
    /// this is the discriminator.</b> The legacy implements them in two different functions with two
    /// different sets of rules, and the choice of function IS the discriminator - which is why this
    /// enumeration has no legacy value to port and its members carry no preserved numbers:
    /// </para>
    /// <list type="table">
    ///   <listheader>
    ///     <term>Aspect</term>
    ///     <description>
    ///     <see cref="Subscription"/> (<c>of_on</c>, <c>n_cst_eventful.sru:L326-L381</c>) versus
    ///     <see cref="Filter"/> (<c>_of_modify</c>, <c>:L993-L1023</c>)
    ///     </description>
    ///   </listheader>
    ///   <item>
    ///     <term>Leading symbol run</term>
    ///     <description>
    ///     Scanned, with duplicate guards, in arbitrary order (<c>:L339-L360</c>) - versus not scanned at
    ///     all, so <c>-</c>, <c>!</c>, <c>@</c>, <c>%</c> and <c>*</c> are ordinary characters.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>Numeric <c>digits:</c> priority</term>
    ///     <description>Parsed (<c>:L365-L373</c>) - versus not parsed; a colon is an ordinary
    ///     character.</description>
    ///   </item>
    ///   <item>
    ///     <term>Negation <c>^</c></term>
    ///     <description>
    ///     Not recognised - <c>of_on</c> has no <c>case</c> for it, so it would end the run and become
    ///     part of an event name - versus recognised in two independent positions
    ///     (<c>:L1008-L1015</c>).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>Empty name</term>
    ///     <description>
    ///     Rejected (<c>:L381</c>) - versus meaningful, denoting "match every name" (<c>:L1017</c>,
    ///     <c>:L1030</c>).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>Empty namespace after a separator</term>
    ///     <description>
    ///     Rejected (<c>:L379</c>) - versus meaningful, denoting "match only subscriptions that have no
    ///     namespace" (<c>:L1002</c>, <c>:L1042</c>).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>Absent namespace</term>
    ///     <description>
    ///     A stored value that happens to be empty - versus not a criterion at all, which is a third
    ///     state (<c>:L1004-L1005</c>, <c>:L1038</c>).
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// The consequence for an implementer is blunt: <b>sharing one scanner between the two grammars is
    /// the fastest way to break this file</b>, because every row above would have to be conditionalised
    /// inside it. <see cref="SubscriptionTopic.ParseSubscription"/> and
    /// <see cref="SubscriptionTopic.ParseFilter"/> are therefore two separate, self-contained decoders
    /// that share no scanning code, exactly as the legacy's two functions do.
    /// </para>
    /// <para>
    /// Carrying the discriminator on the value also stops the fields being misread. A
    /// <see cref="Filter"/> topic always reports the option defaults for
    /// <see cref="SubscriptionTopic.Capture"/>, <see cref="SubscriptionTopic.Priority"/> and
    /// <see cref="SubscriptionTopic.Prepend"/> because a filter never sets them; a
    /// <see cref="Subscription"/> topic always reports <see langword="false"/> for
    /// <see cref="SubscriptionTopic.NegateName"/> and <see cref="SubscriptionTopic.NegateNamespace"/>
    /// because a subscription cannot negate. Reading either set without the discriminator invites the
    /// conclusion that the topic said something it never said.
    /// </para>
    /// </remarks>
    public enum TopicGrammar
    {
        /// <summary>
        /// The topic was decoded under the subscribe grammar, <c>of_on</c>
        /// (<c>n_cst_eventful.sru:L326-L381</c>), so
        /// <see cref="SubscriptionTopic.Capture"/>, <see cref="SubscriptionTopic.Priority"/> and
        /// <see cref="SubscriptionTopic.Prepend"/> are meaningful and the negation flags are not.
        /// </summary>
        /// <remarks>
        /// The default, so a <see cref="SubscriptionTopic"/> built from parts without naming a grammar is
        /// a subscription - which is the far commoner case and the only one that can be dispatched.
        /// </remarks>
        Subscription = 0,

        /// <summary>
        /// The topic was decoded under the off/disable filter grammar, <c>_of_modify</c>
        /// (<c>n_cst_eventful.sru:L993-L1023</c>), so
        /// <see cref="SubscriptionTopic.NegateName"/>,
        /// <see cref="SubscriptionTopic.NegateNamespace"/>,
        /// <see cref="SubscriptionTopic.MatchesEveryName"/> and the three-state
        /// <see cref="SubscriptionTopic.NamespaceCriterion"/> are meaningful and the subscribe options
        /// are not.
        /// </summary>
        Filter = 1
    }

    /// <summary>
    /// One decoded event-broker topic: the legacy fused topic string taken apart into the wire triple
    /// (<see cref="Sequence"/>, <see cref="LogicalName"/>, <see cref="Lifetime"/>) plus the subscribe
    /// options and filter criteria that triple alone cannot carry, with byte-exact re-emission of the
    /// original string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Produced by <see cref="ParseSubscription"/> or <see cref="ParseFilter"/> - never by both, see
    /// <see cref="TopicGrammar"/> - and immutable once produced. Immutability is not decoration: the
    /// legacy records that subscribing during a dispatch takes effect only on the <i>next</i> dispatch
    /// (<c>ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L237</c>), so a parsed topic is a value fixed
    /// at subscribe time and nothing may later mutate what a live subscription was registered under.
    /// </para>
    /// <para>
    /// <b>The eight-symbol grammar, in one place.</b> A three-field model cannot round-trip this language,
    /// which is why the wire triple is a projection over the fields below rather than the whole of them.
    /// The symbols are declared on <see cref="TopicSymbols"/> and consumed here:
    /// <see cref="TopicSymbols.Prepend"/> (<c>-</c>), <see cref="TopicSymbols.Handled"/> (<c>%</c>),
    /// <see cref="TopicSymbols.All"/> (<c>*</c>), <see cref="TopicSymbols.Low"/> (<c>@</c>),
    /// <see cref="TopicSymbols.High"/> (<c>!</c>),
    /// <see cref="TopicSymbols.PriorityDelimiter"/> (<c>:</c>),
    /// <see cref="TopicSymbols.NamespaceSeparator"/> (<c>.</c>) and
    /// <see cref="TopicSymbols.Negation"/> (<c>^</c>).
    /// </para>
    /// <para>
    /// <b>Equality is over the DECODED fields; <see cref="RawValue"/> is excluded.</b> That is a decision
    /// with a visible consequence, so it is stated here rather than left to be discovered:
    /// <c>"-!clicked"</c> and <c>"!-clicked"</c> are both legal, decode identically, and are therefore
    /// <b>equal</b> - while <see cref="ToLegacyString"/> still returns each one's own original spelling.
    /// Two equal topics may thus re-emit different strings. The alternative - folding the raw text into
    /// identity - would make two topics that mean exactly the same thing to the broker compare unequal,
    /// which is the more damaging surprise of the two, since identity is what a set, a dictionary key or
    /// an assertion is built on. See <see cref="Equals(SubscriptionTopic)"/>.
    /// </para>
    /// <para>
    /// <b>The three case rules differ from one another and are not unified.</b> The <b>event name is
    /// case-SENSITIVE</b> - <c>of_on</c> stores it exactly as written (<c>n_cst_eventful.sru:L335</c>) and
    /// every comparison of it, including the ordering, is ordinal. The <b>namespace is case-SENSITIVE</b>
    /// and never folded either (<c>:L377</c>, <c>:L1002</c>, compared at <c>:L1040</c> and <c>:L1042</c>).
    /// The <b>handler name is case-INSENSITIVE</b>, because it alone is folded to lower case at both ends,
    /// on storage at <c>:L336</c> and on matching at <c>:L998</c>; that asymmetry is stated inline at
    /// <c>w_test_eventful.srw:L250</c>. A handler name is not carried by this type at all - it belongs to
    /// the subscription entry in <c>EventBroker.cs</c> - but the asymmetry is recorded here because a
    /// reader who has just seen two ordinal rules will otherwise assume the third.
    /// </para>
    /// <para>
    /// No member of this type performs I/O, logs, allocates a regular expression or throws for malformed
    /// input. A malformed topic is reported as <c>RetCode.E_INVALID_ARGUMENT</c>, because that is what the
    /// legacy returns.
    /// </para>
    /// </remarks>
    public sealed record SubscriptionTopic
    {
        private readonly string _legacyName = string.Empty;

        /// <summary>
        /// Which of the two grammars decoded this topic, and therefore which of its fields carry meaning.
        /// Defaults to <see cref="TopicGrammar.Subscription"/>.
        /// </summary>
        /// <remarks>
        /// Set by whichever parse entry point produced the value; see <see cref="TopicGrammar"/> for the
        /// six-row table of what the two grammars do differently, and why they share no scanning code.
        /// </remarks>
        public TopicGrammar Grammar { get; init; } = TopicGrammar.Subscription;

        /// <summary>
        /// <b>The residual event name exactly as the legacy stores it</b>, after the leading symbol run,
        /// the numeric priority prefix and the namespace suffix have been removed - and it is <b>the
        /// dispatch key and the ordinal sort key</b>. Never <see langword="null"/>.
        /// </summary>
        /// <value>
        /// The port of <c>EVENTDATA.name</c> (<c>n_cst_eventful.sru:L15</c>) as assigned at <c>:L335</c>
        /// and successively narrowed at <c>:L363</c>, <c>:L371</c> and <c>:L378</c>. For the topic
        /// <c>"0-itemchanged"</c> this is <c>"0-itemchanged"</c>, <b>prefix included</b>.
        /// </value>
        /// <remarks>
        /// <para>
        /// <b>DECISION (AAP 0.7.3 C-K) - this, and NOT <see cref="LogicalName"/>, is the dispatch and sort
        /// key.</b> Confusing the two silently destroys dispatch ordering, so the distinction is worth
        /// stating twice. The legacy matches a triggered event against this stored value and orders the
        /// subscription list by it: the ordered insert compares it (<c>:L408-L409</c>), the lexical bounds
        /// are maintained from it (<c>:L439-L440</c>) and both fast rejects test against those bounds
        /// (<c>:L767-L770</c>, <c>:L797-L798</c>). <see cref="Sequence"/> and <see cref="LogicalName"/> are
        /// a projection <i>over</i> this value for the benefit of a wire contract; they are never a
        /// replacement for it, and nothing dispatches on them.
        /// </para>
        /// <para>
        /// <b>THE RECONSTITUTION INVARIANT, enforced rather than asserted.</b>
        /// <c>LegacyName == (Sequence is null ? LogicalName : $"{Sequence}{TopicSymbols.Prepend}{LogicalName}")</c>
        /// holds for every instance without exception, because <see cref="Sequence"/> and
        /// <see cref="LogicalName"/> are <i>computed from this property</i> rather than stored beside it.
        /// There is no code path on which the three can disagree, and the recognition guard described on
        /// <see cref="Sequence"/> exists precisely to keep it true for inputs such as <c>"00-x"</c>, where
        /// a naive split would break it.
        /// </para>
        /// <para>
        /// <b>THE TRAP - a symbol is only a symbol inside the leading run, and this is where it bites.</b>
        /// <c>se_cst_dw.sru:L54</c> declares <c>EVT_ITEMCHANGED = "0-itemchanged"</c> and <c>:L57</c>
        /// declares <c>EVT_EDITCHANGED = "1-editchanged"</c>. Trace <c>of_on</c> against the first: the
        /// character loop reads position 1, finds <c>'0'</c>, matches no symbol case, hits
        /// <c>case else</c> and <b>exits at position 1</b> (<c>:L357-L358</c>), so <c>Mid(name, 1)</c> at
        /// <c>:L363</c> returns the whole string. The <c>-</c> is an ordinary character <i>of the event
        /// name</i> and is <b>not</b> a prepend request; <c>Pos(name, ":")</c> and <c>Pos(name, ".")</c>
        /// are both zero, so nothing further is stripped, and <c>EVENTDATA.name</c> ends up as
        /// <c>"0-itemchanged"</c> verbatim. Two consequences are encoded here and asserted by the parity
        /// tests: <see cref="Prepend"/> is <see langword="false"/> for that topic, and a parser that
        /// stripped or detected <c>-</c> at an arbitrary position would corrupt both live DataWindow
        /// topics and silently detach every subscriber.
        /// </para>
        /// <para>
        /// So why are the digits there at all? Because the ordering those two topics achieve comes
        /// <b>entirely from the ordinal name sort</b> - <c>"0-itemchanged"</c> sorts before
        /// <c>"1-editchanged"</c> - and the legacy documents that subscriptions are ordered by name
        /// ascending, adding that a shorter name improves lookup and dispatch performance
        /// (<c>w_test_eventful.srw:L238</c>). That performance note is why <c>EventBroker</c> keeps the
        /// lexical bounds and probes them before scanning at all. The digits are a hand-rolled sequence
        /// number spelled into the name, which is exactly the fusion AAP 0.6.1.2 requires be decomposed -
        /// and exactly why decomposing it must not disturb the name it was spelled into.
        /// </para>
        /// <para>
        /// A <see langword="null"/> assignment is normalised to <see cref="string.Empty"/>, matching the
        /// legacy's always-present possibly-empty string. Nothing else is altered: no trimming, no case
        /// folding, no validation. An empty value is legal for a <see cref="TopicGrammar.Filter"/> - it
        /// means "match every name" - and is rejected by <see cref="ParseSubscription"/> for a
        /// subscription (<c>:L381</c>).
        /// </para>
        /// </remarks>
        public string LegacyName
        {
            get => _legacyName;
            init => _legacyName = value ?? string.Empty;
        }

        /// <summary>
        /// <b>Wire field.</b> The leading digits of a <c>[digits]-</c> ordering prefix on
        /// <see cref="LegacyName"/>, or <see langword="null"/> when the name carries no such prefix.
        /// </summary>
        /// <value>
        /// <c>0</c> for <c>"0-itemchanged"</c>, <c>1</c> for <c>"1-editchanged"</c>, and
        /// <see langword="null"/> for <c>"clicked"</c>, for <c>"-clicked"</c> (whose <c>-</c> was consumed
        /// as <see cref="Prepend"/> before the name was formed) and for <c>"itemchanged"</c>.
        /// </value>
        /// <remarks>
        /// <para>
        /// This is the first of the three fields AAP 0.6.1.2 requires be carried separately, and it is
        /// <b>computed</b> from <see cref="LegacyName"/> on each read rather than stored. That is
        /// deliberate on two counts. It makes the reconstitution invariant unbreakable, and it costs
        /// nothing on the path that matters: dispatch keys on <see cref="LegacyName"/>, so this projection
        /// is read at the contract boundary, not in the dispatch loop. The scan is a handful of characters.
        /// </para>
        /// <para>
        /// <b>DECISION (AAP 0.7.3 C-K) - the recognition guard, and why it is deliberately narrow.</b> The
        /// legacy has no notion of a sequence prefix at all; it is a convention its authors spelled into
        /// two event names, and this projection is the .NET tree's reading of that convention. A prefix is
        /// recognised only when every one of the following holds, and otherwise this is
        /// <see langword="null"/> and <see cref="LogicalName"/> is the whole of
        /// <see cref="LegacyName"/>:
        /// </para>
        /// <list type="number">
        ///   <item><description>
        ///   The name begins with one or more <b>ASCII</b> digits <c>'0'</c> to <c>'9'</c>. Unicode digits
        ///   are not accepted: they are not what either evidenced topic is spelled with, and treating them
        ///   as digits would let a name round-trip into a different string.
        ///   </description></item>
        ///   <item><description>
        ///   The digit run is immediately followed by <see cref="TopicSymbols.Prepend"/> (<c>-</c>). That
        ///   character is part of the prefix, which is why a bare numeric name such as <c>"0"</c> has no
        ///   sequence.
        ///   </description></item>
        ///   <item><description>
        ///   The run carries no redundant leading zero - it is either the single digit <c>"0"</c> or begins
        ///   with a non-zero digit. <c>"00-x"</c> is therefore not a sequence, because
        ///   <c>$"{0}-x"</c> is <c>"0-x"</c> and re-emitting that would silently rename the event.
        ///   </description></item>
        ///   <item><description>
        ///   The value fits <see cref="int"/>. A longer run of digits is a name, not a sequence.
        ///   </description></item>
        /// </list>
        /// <para>
        /// Conditions 3 and 4 exist solely to keep the invariant on <see cref="LegacyName"/> true for
        /// every possible input, which is the difference between an invariant and an aspiration.
        /// </para>
        /// </remarks>
        public int? Sequence
        {
            get => TryReadSequence(_legacyName, out int sequence, out _) ? sequence : null;
        }

        /// <summary>
        /// <b>Wire field.</b> <see cref="LegacyName"/> with the <c>[digits]-</c> ordering prefix removed,
        /// or the whole of <see cref="LegacyName"/> when there is no prefix. Never <see langword="null"/>.
        /// </summary>
        /// <value>
        /// <c>"itemchanged"</c> for <c>"0-itemchanged"</c>, <c>"editchanged"</c> for
        /// <c>"1-editchanged"</c>, and <c>"clicked"</c> for <c>"clicked"</c>.
        /// </value>
        /// <remarks>
        /// The second of the three wire fields, and the logical identity a contract should present to a
        /// human or to a consumer that has no business knowing how the legacy encoded ordering.
        /// <b>Nothing dispatches on it and nothing sorts by it</b> - see the decision note on
        /// <see cref="LegacyName"/>. Computed on each read from <see cref="LegacyName"/> under the same
        /// recognition guard as <see cref="Sequence"/>, so the two always agree.
        /// </remarks>
        public string LogicalName
        {
            get => TryReadSequence(_legacyName, out _, out string logicalName) ? logicalName : _legacyName;
        }

        /// <summary>
        /// <b>The three-state namespace criterion</b>, and the authoritative namespace field:
        /// <see langword="null"/> when the topic carried no
        /// <see cref="TopicSymbols.NamespaceSeparator"/> at all, otherwise the raw text that followed it,
        /// which may itself be empty.
        /// </summary>
        /// <value>
        /// One of exactly three states, which two booleans cannot express and which the evidenced corpus
        /// exercises in full, negations included:
        /// <list type="table">
        ///   <listheader><term>State</term><description>Spelling and meaning</description></listheader>
        ///   <item>
        ///     <term><b>Absent</b> - <see langword="null"/></term>
        ///     <description>
        ///     No separator was present, as in the filter <c>"clicked"</c>. For a
        ///     <see cref="TopicGrammar.Filter"/> the namespace is <b>not a criterion at all</b> and the
        ///     namespace test is skipped entirely - the legacy's <c>bNoNamespace</c> flag
        ///     (<c>n_cst_eventful.sru:L1004-L1005</c>, honoured at <c>:L1038</c>). For a
        ///     <see cref="TopicGrammar.Subscription"/> this same state is how an absent namespace is
        ///     represented, and <see cref="Namespace"/> flattens it to the empty string the legacy field
        ///     actually holds; the absent-versus-empty distinction is inert there, because the subscribe
        ///     grammar rejects a present-but-empty namespace outright.
        ///     </description>
        ///   </item>
        ///   <item>
        ///     <term><b>Present and empty</b> - <see cref="string.Empty"/></term>
        ///     <description>
        ///     A separator with nothing after it, as in the filter <c>"clicked."</c>, which matches
        ///     <b>only subscriptions that have no namespace</b> (<c>:L1002</c>, compared at
        ///     <c>:L1042</c>). This is one half of the load-bearing asymmetry between the grammars: the
        ///     identical string is <b>rejected outright</b> when subscribing (<c>:L379</c>).
        ///     </description>
        ///   </item>
        ///   <item>
        ///     <term><b>Present and non-empty</b> - e.g. <c>"myns"</c></term>
        ///     <description>
        ///     Match that namespace, as in <c>"clicked.myns"</c>. Note the split takes the <b>first</b>
        ///     separator, so <c>"a.b.c"</c> yields the name <c>"a"</c> and the namespace <c>"b.c"</c>.
        ///     </description>
        ///   </item>
        /// </list>
        /// </value>
        /// <remarks>
        /// An arbitrary caller-chosen string; never validated, trimmed or case-folded, because the legacy
        /// stores whatever followed the separator unexamined. Compare namespaces with
        /// <see cref="SubscriptionOptions.NamespaceComparer"/>, which is ordinal - so <c>"Persistent"</c>
        /// is a <i>different</i> namespace from <c>"persistent"</c>, and a bulk unsubscribe sweeps it.
        /// </remarks>
        public string? NamespaceCriterion { get; init; }

        /// <summary>
        /// The raw namespace string, empty when absent - the flattening of
        /// <see cref="NamespaceCriterion"/> that collapses its <i>absent</i> and <i>present and empty</i>
        /// states into one. Never <see langword="null"/>.
        /// </summary>
        /// <value><see cref="NamespaceCriterion"/>, or <see cref="SubscriptionNamespaces.None"/> when it
        /// is <see langword="null"/>.</value>
        /// <remarks>
        /// This is the field a <see cref="TopicGrammar.Subscription"/> wants, because a subscription's
        /// namespace is a stored value and an absent one genuinely is the empty string
        /// (<c>n_cst_eventful.sru:L377</c>, and the field's uninitialised PowerScript value otherwise). A
        /// <see cref="TopicGrammar.Filter"/> must use <see cref="NamespaceCriterion"/> instead, since for a
        /// filter those two states mean different things and this property cannot tell them apart. Use
        /// <see cref="HasNamespaceCriterion"/> to distinguish them.
        /// </remarks>
        public string Namespace
        {
            get => NamespaceCriterion ?? SubscriptionNamespaces.None;
        }

        /// <summary>
        /// Whether the topic carried a <see cref="TopicSymbols.NamespaceSeparator"/> at all, and therefore
        /// whether - for a <see cref="TopicGrammar.Filter"/> - the namespace participates in matching.
        /// </summary>
        /// <value><see langword="true"/> when <see cref="NamespaceCriterion"/> is not
        /// <see langword="null"/>; otherwise <see langword="false"/>.</value>
        /// <remarks>
        /// The port of the legacy's <c>bNoNamespace</c> flag, inverted
        /// (<c>n_cst_eventful.sru:L1004-L1005</c>). It is the discriminator between the first two states
        /// listed on <see cref="NamespaceCriterion"/>, and <see cref="Matches"/> consumes it.
        /// </remarks>
        public bool HasNamespaceCriterion
        {
            get => NamespaceCriterion is not null;
        }

        /// <summary>
        /// Whether the namespace is present <i>and</i> non-empty.
        /// </summary>
        /// <value><see langword="true"/> when <see cref="Namespace"/> is non-empty; otherwise
        /// <see langword="false"/>.</value>
        /// <remarks>
        /// Mirrors <see cref="SubscriptionOptions.HasNamespace"/> so that a topic and the option set it
        /// produces answer the question identically. Distinct from
        /// <see cref="HasNamespaceCriterion"/>: for the filter <c>"clicked."</c> a criterion is present
        /// while the namespace is empty, so this is <see langword="false"/> and that one is
        /// <see langword="true"/>.
        /// </remarks>
        public bool HasNamespace
        {
            get => Namespace.Length > 0;
        }

        /// <summary>
        /// <b>Wire field.</b> The lifetime <see cref="Namespace"/> projects onto: whether a
        /// <c>".^persistent"</c> bulk unsubscribe sweeps a subscription in this namespace away or spares
        /// it.
        /// </summary>
        /// <value>
        /// <see cref="SubscriptionLifetime.Persistent"/> when <see cref="Namespace"/> is ordinally equal to
        /// <see cref="SubscriptionNamespaces.Persistent"/>; otherwise
        /// <see cref="SubscriptionLifetime.Transient"/>.
        /// </value>
        /// <remarks>
        /// <para>
        /// The third of the three wire fields, and the one with the highest stakes. It is a
        /// <b>projection</b> and it is intentionally lossy: <see cref="Namespace"/> remains authoritative,
        /// <c>"myns"</c> and the empty default alike collapse to
        /// <see cref="SubscriptionLifetime.Transient"/>, and no round trip from this value back to a
        /// namespace exists. Delegates to <see cref="SubscriptionNamespaces.ToLifetime"/> so the mapping is
        /// stated once.
        /// </para>
        /// <para>
        /// <b>THE FAILURE MODE, stated so it cannot be reintroduced.</b> This value describes what a
        /// namespace <i>is</i>. It cannot describe what a filter <i>excludes</i>, and the framework's bulk
        /// unsubscribe is precisely an exclusion. <c>of_Off(".^persistent")</c> decodes to <b>"every name
        /// WHERE namespace is NOT persistent"</b>, so it deliberately <b>spares</b> persistent
        /// subscriptions. Rebuilding that filter from a lifetime flag alone - matching on
        /// "persistent-ness" and dropping the negation - deletes exactly the subscriptions that were
        /// registered to survive. Nothing announces it: the unsubscribe still returns success, no assertion
        /// fails, and the survivors are simply gone. <see cref="Namespace"/> together with
        /// <see cref="NegateNamespace"/> are therefore the authoritative pair for any filter, this
        /// projection is for reading intent, and <see cref="Matches"/> exists so that the negation is
        /// applied in one place rather than at every call site.
        /// </para>
        /// </remarks>
        public SubscriptionLifetime Lifetime
        {
            get => SubscriptionNamespaces.ToLifetime(Namespace);
        }

        /// <summary>
        /// <b>Subscribe-grammar option.</b> Which dispatch handled-states the subscription observes, as
        /// selected by <see cref="TopicSymbols.All"/> (<c>*</c>) or
        /// <see cref="TopicSymbols.Handled"/> (<c>%</c>). Defaults to
        /// <see cref="CaptureMode.Unhandled"/>.
        /// </summary>
        /// <remarks>
        /// The legacy default, established before the topic is parsed
        /// (<c>n_cst_eventful.sru:L328-L336</c>, tested against at <c>:L346</c> and <c>:L349</c>). The two
        /// symbols are mutually exclusive because they claim the same slot, so whichever appears second
        /// fails the whole subscription. Always this default on a <see cref="TopicGrammar.Filter"/> topic,
        /// where <c>*</c> and <c>%</c> are ordinary characters.
        /// </remarks>
        public CaptureMode Capture { get; init; } = CaptureMode.Unhandled;

        /// <summary>
        /// <b>Subscribe-grammar option.</b> The dispatch priority, where a larger number is dispatched
        /// earlier. Defaults to <see cref="Priorities.Normal"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Set by <see cref="TopicSymbols.High"/> (<c>!</c>) to <see cref="Priorities.High"/>, by
        /// <see cref="TopicSymbols.Low"/> (<c>@</c>) to <see cref="Priorities.Low"/>, or by a numeric
        /// <c>[digits]</c><see cref="TopicSymbols.PriorityDelimiter"/> prefix to that number
        /// (<c>n_cst_eventful.sru:L351-L356</c> and <c>:L365-L373</c>). Any <see cref="int"/> is legal, not
        /// merely the three named values: the legacy parses the prefix with no range check and
        /// <c>of_On("9999:clicked", ...)</c> is an evidenced call
        /// (<c>w_test_eventful.srw:L226</c>).
        /// </para>
        /// <para>
        /// <b>An explicit <c>"0:"</c> prefix is legal and leaves this at <see cref="Priorities.Normal"/>,
        /// but it still cannot follow <c>!</c> or <c>@</c>.</b> The guard at <c>:L369</c> is evaluated
        /// <i>before</i> the assignment at <c>:L370</c> and tests only whether the field has already moved
        /// off <see cref="Priorities.Normal"/> - so <c>"0:clicked"</c> parses to name <c>"clicked"</c> at
        /// priority <c>0</c>, while <c>"!0:clicked"</c> and <c>"@0:clicked"</c> both fail, the symbol having
        /// moved the field first. And because the leading run must precede the prefix, the reverse ordering
        /// is not a combination at all: in <c>"0:!clicked"</c> the <c>!</c> is an ordinary character of the
        /// name. The rule stated at <c>w_test_eventful.srw:L232</c> - that <c>!</c> and <c>@</c> cannot
        /// combine with a numeric priority - therefore holds in both directions, and this note records the
        /// exact mechanism because a sibling summary reads as though the zero case were an exception to it.
        /// </para>
        /// <para>
        /// Always <see cref="Priorities.Normal"/> on a <see cref="TopicGrammar.Filter"/> topic. Priority is
        /// independent of the namespace (<c>w_test_eventful.srw:L233</c>): the two are orthogonal axes of
        /// the same string and only the name and the priority take part in ordering.
        /// </para>
        /// </remarks>
        public int Priority { get; init; } = Priorities.Normal;

        /// <summary>
        /// <b>Subscribe-grammar option.</b> When <see langword="true"/>, insert at the <i>head</i> of the
        /// matching priority's queue rather than its tail, as selected by
        /// <see cref="TopicSymbols.Prepend"/> (<c>-</c>). Defaults to <see langword="false"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This selects an end of the queue, not a priority: it changes only where the subscription lands
        /// among the peers that share its <see cref="Priority"/>. The whole of the semantics is one
        /// character in the ordered insert - prepend stops at the first entry whose priority is
        /// <c>&lt;=</c> the incoming one (<c>n_cst_eventful.sru:L417</c>), landing the new entry at the
        /// <i>head</i> of the equal-priority run, while the default stops only at a strictly
        /// <c>&lt;</c> lower priority (<c>:L419</c>), landing it at the <i>tail</i>. That insert lives in
        /// <c>EventBroker.cs</c>; <see cref="CompareDispatchOrder"/> publishes the ordering key it
        /// implements.
        /// </para>
        /// <para>
        /// <b>It is <see langword="false"/> for <c>"0-itemchanged"</c> and for
        /// <c>"1-editchanged"</c></b> - see the trap note on <see cref="LegacyName"/>. A repeat of the
        /// symbol inside the leading run is rejected (<c>:L343</c>), which is why <c>"--clicked"</c> fails.
        /// Always <see langword="false"/> on a <see cref="TopicGrammar.Filter"/> topic.
        /// </para>
        /// </remarks>
        public bool Prepend { get; init; }

        /// <summary>
        /// <b>Filter-grammar flag.</b> When <see langword="true"/>, the name criterion matches by
        /// <b>inequality</b> - every name except <see cref="LegacyName"/>. Set by a leading
        /// <see cref="TopicSymbols.Negation"/> (<c>^</c>) on the name part.
        /// </summary>
        /// <remarks>
        /// Ported from <c>n_cst_eventful.sru:L1008-L1011</c>, which strips the symbol, and <c>:L1033</c>,
        /// where it flips the comparison. Independent of <see cref="NegateNamespace"/>: either, both or
        /// neither may be set, as the evidenced corpus shows with <c>"^clicked"</c>,
        /// <c>"clicked.^myns"</c> and <c>"^clicked.^myns"</c>. A negated <i>empty</i> name is the filter
        /// grammar's one rejection (<c>:L1021-L1023</c>), since "every name except no name" has no
        /// meaning. Always <see langword="false"/> on a <see cref="TopicGrammar.Subscription"/> topic,
        /// where <c>^</c> is an ordinary character.
        /// </remarks>
        public bool NegateName { get; init; }

        /// <summary>
        /// <b>Filter-grammar flag.</b> When <see langword="true"/>, the namespace criterion matches by
        /// <b>inequality</b> - every namespace except <see cref="Namespace"/>. Set by a leading
        /// <see cref="TopicSymbols.Negation"/> (<c>^</c>) on the namespace part.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Ported from <c>n_cst_eventful.sru:L1012-L1015</c> and <c>:L1040</c>. <b>This flag is half of the
        /// framework's bulk-unsubscribe contract</b>: it is what turns <c>".^persistent"</c> into "every
        /// name WHERE namespace is NOT persistent", and dropping it while keeping the namespace deletes
        /// every subscription that was registered to survive. See the failure-mode note on
        /// <see cref="Lifetime"/>, and prefer <see cref="Matches"/> over rebuilding the comparison by hand.
        /// </para>
        /// <para>
        /// It applies only when <see cref="HasNamespaceCriterion"/> is <see langword="true"/>, because an
        /// absent namespace is not a criterion and its test is skipped altogether (<c>:L1038</c>). Note
        /// that it composes with an empty namespace to useful effect: the evidenced filter
        /// <c>"clicked.^"</c> reads "namespace is not empty", i.e. match only subscriptions that
        /// <i>have</i> a namespace.
        /// </para>
        /// </remarks>
        public bool NegateNamespace { get; init; }

        /// <summary>
        /// <b>Filter-grammar flag.</b> Whether the name criterion is empty and therefore matches
        /// <b>every</b> name.
        /// </summary>
        /// <value><see langword="true"/> when <see cref="LegacyName"/> is empty; otherwise
        /// <see langword="false"/>.</value>
        /// <remarks>
        /// The port of the legacy's <c>bNoName</c> flag (<c>n_cst_eventful.sru:L1017</c>), which seeds the
        /// match at <c>:L1030</c> and, when set, causes the name comparison to be skipped entirely rather
        /// than performed against an empty string. It is what makes <c>".^persistent"</c> and <c>"."</c>
        /// filters over the whole subscription list. For a <see cref="TopicGrammar.Subscription"/> this is
        /// always <see langword="false"/>, because an empty name is rejected there (<c>:L381</c>).
        /// </remarks>
        public bool MatchesEveryName
        {
            get => _legacyName.Length == 0;
        }

        /// <summary>
        /// The original topic string exactly as supplied to the parser, retained verbatim so that
        /// <see cref="ToLegacyString"/> is byte-exact - or <see langword="null"/> for a topic built from
        /// parts, which <see cref="ToLegacyString"/> then re-emits in canonical form.
        /// </summary>
        /// <remarks>
        /// <b>Round-trip metadata, and deliberately NOT part of this record's identity</b> - see the
        /// equality note in the type's own remarks and <see cref="Equals(SubscriptionTopic)"/>. Retaining
        /// the string is the only design that is byte-exact under a grammar whose leading symbols may
        /// appear in any order: a canonical re-emission cannot know whether the caller wrote
        /// <c>"-!clicked"</c> or <c>"!-clicked"</c>, and both are legal and identical in meaning.
        /// </remarks>
        public string? RawValue { get; init; }

        /// <summary>
        /// Reconstitutes the fused legacy topic string. <b>This method is the compatibility edge</b>, and
        /// the only place the decomposed fields are refused back into one opaque value.
        /// </summary>
        /// <returns>
        /// For a topic produced by <see cref="ParseSubscription"/> or <see cref="ParseFilter"/>, the
        /// <b>original string byte for byte</b>. For a topic built from parts - one whose
        /// <see cref="RawValue"/> is <see langword="null"/> - the canonical spelling described below.
        /// Never <see langword="null"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>DECISION (AAP 0.7.3 C-K) - the legacy form is reconstituted HERE and nowhere else.</b> A
        /// contract carries <see cref="Sequence"/>, <see cref="LogicalName"/>, <see cref="Lifetime"/> and
        /// the fields beside them; this method exists for the three cases that genuinely need the fused
        /// spelling - writing a characterization recording, emitting a log line that will be diffed against
        /// the oracle's, and calling something that still speaks the legacy grammar. Using it as a
        /// general-purpose name is how the ordering becomes invisible again, which is precisely what
        /// AAP 0.6.1.2 warns against.
        /// </para>
        /// <para>
        /// <b>Byte-exactness is achieved by retention, not by re-emission</b>, for the reason given on
        /// <see cref="RawValue"/>: the leading symbols may be written in any order, so no canonical
        /// generator can recover the caller's spelling. Every one of the twenty-four evidenced subscribe
        /// topics and eleven evidenced filter topics therefore round-trips to itself exactly.
        /// </para>
        /// <para>
        /// <b>The canonical order used for a topic built from parts</b>, which is canonical rather than
        /// round-tripped and is stated here so it can be relied upon:
        /// </para>
        /// <list type="bullet">
        ///   <item><description>
        ///   <see cref="TopicGrammar.Subscription"/>: <see cref="TopicSymbols.Prepend"/> if
        ///   <see cref="Prepend"/>; then the capture symbol - <see cref="TopicSymbols.All"/> for
        ///   <see cref="CaptureMode.All"/>, <see cref="TopicSymbols.Handled"/> for
        ///   <see cref="CaptureMode.Handled"/>, nothing for <see cref="CaptureMode.Unhandled"/>; then the
        ///   priority - <see cref="TopicSymbols.High"/> for <see cref="Priorities.High"/>,
        ///   <see cref="TopicSymbols.Low"/> for <see cref="Priorities.Low"/>, nothing for
        ///   <see cref="Priorities.Normal"/>, otherwise the digits followed by
        ///   <see cref="TopicSymbols.PriorityDelimiter"/>; then <see cref="LegacyName"/>; then, when
        ///   <see cref="HasNamespaceCriterion"/>, <see cref="TopicSymbols.NamespaceSeparator"/> followed by
        ///   <see cref="Namespace"/>. Example: <c>"-*9999:clicked.myns"</c>.
        ///   </description></item>
        ///   <item><description>
        ///   <see cref="TopicGrammar.Filter"/>: <see cref="TopicSymbols.Negation"/> if
        ///   <see cref="NegateName"/>; then <see cref="LegacyName"/>; then, when
        ///   <see cref="HasNamespaceCriterion"/>, <see cref="TopicSymbols.NamespaceSeparator"/> followed by
        ///   <see cref="TopicSymbols.Negation"/> if <see cref="NegateNamespace"/> and then
        ///   <see cref="Namespace"/>. Example: <c>".^persistent"</c>. The subscribe options are never
        ///   emitted for a filter, because the filter grammar cannot express them.
        ///   </description></item>
        /// </list>
        /// <para>
        /// The canonical form re-parses to an equal topic for any field values a parser could itself have
        /// produced. Two field combinations are unreachable by either parser and consequently cannot
        /// round-trip, and are recorded rather than defended against, since guarding them would mean adding
        /// validation the legacy does not have: a <see cref="LegacyName"/> that itself contains a
        /// <see cref="TopicSymbols.NamespaceSeparator"/> - the legacy grammar cannot express such a name,
        /// because the split always consumes the first separator - and a
        /// <see cref="TopicGrammar.Subscription"/> whose <see cref="NamespaceCriterion"/> is present but
        /// empty, which the subscribe grammar rejects (<c>n_cst_eventful.sru:L379</c>) and whose canonical
        /// spelling would therefore be a string that fails to parse.
        /// </para>
        /// </remarks>
        public string ToLegacyString()
        {
            // Retention first: a parsed topic re-emits its own bytes, which is the whole contract.
            if (RawValue is not null)
            {
                return RawValue;
            }

            return Grammar == TopicGrammar.Filter ? BuildCanonicalFilter() : BuildCanonicalSubscription();
        }

        /// <summary>
        /// Projects this topic's subscribe-grammar options onto the option aggregate a subscription is
        /// created with.
        /// </summary>
        /// <returns>
        /// A <see cref="SubscriptionOptions"/> carrying <see cref="Capture"/>, <see cref="Priority"/>,
        /// <see cref="Prepend"/> and <see cref="Namespace"/>. Never <see langword="null"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The natural handoff from decoding to subscribing, kept here so that the four fields are copied in
        /// one audited place rather than at every call site. <see cref="SubscriptionOptions.HandlerName"/> is
        /// deliberately left at its default: a handler name is not part of a topic, it arrives as a separate
        /// argument to <c>of_on</c> (<c>n_cst_eventful.sru:L336</c>), and it is the broker that owns both
        /// setting it and rejecting an empty one (<c>:L331</c>).
        /// </para>
        /// <para>
        /// Meaningful for a <see cref="TopicGrammar.Subscription"/>. Calling it on a
        /// <see cref="TopicGrammar.Filter"/> is harmless but uninformative - a filter never sets any of the
        /// three options, so the result carries their defaults - and it silently discards
        /// <see cref="NegateName"/>, <see cref="NegateNamespace"/> and the absent-versus-empty namespace
        /// distinction, none of which an option set can express. It does not throw, because nothing in this
        /// file throws.
        /// </para>
        /// </remarks>
        public SubscriptionOptions ToSubscriptionOptions()
        {
            return new SubscriptionOptions
            {
                Capture = Capture,
                Priority = Priority,
                Prepend = Prepend,
                Namespace = Namespace
            };
        }

        /// <summary>
        /// Applies this topic's <b>name and namespace</b> criteria, negations included, to one candidate
        /// subscription - the composite test at the heart of every <c>of_off</c> and every
        /// <c>of_disable</c>.
        /// </summary>
        /// <param name="subscriptionName">
        /// The candidate subscription's stored event name, i.e. its own <see cref="LegacyName"/>. A
        /// <see langword="null"/> is treated as empty, matching the legacy's always-present possibly-empty
        /// string.
        /// </param>
        /// <param name="subscriptionNamespace">
        /// The candidate subscription's stored namespace. A <see langword="null"/> is treated as
        /// <see cref="SubscriptionNamespaces.None"/>, since a subscription's namespace is a value and an
        /// absent one is the empty string.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the candidate satisfies both criteria; otherwise
        /// <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Ported from <c>n_cst_eventful.sru:L1030-L1044</c>, in the legacy's own order and with its own
        /// short-circuiting:
        /// </para>
        /// <list type="number">
        ///   <item><description>
        ///   The match is seeded from <see cref="MatchesEveryName"/> (<c>:L1030</c>), so an empty name
        ///   criterion matches every name and the comparison below is <b>skipped entirely</b> rather than
        ///   performed against an empty string. This is also why a negated empty name is rejected at parse
        ///   time: the negation would have nothing to invert.
        ///   </description></item>
        ///   <item><description>
        ///   Otherwise the name matches by equality, or by <b>inequality</b> when
        ///   <see cref="NegateName"/> (<c>:L1032-L1036</c>).
        ///   </description></item>
        ///   <item><description>
        ///   The namespace is tested only when <see cref="HasNamespaceCriterion"/> (<c>:L1038</c>), again by
        ///   equality or by <b>inequality</b> when <see cref="NegateNamespace"/> (<c>:L1039-L1043</c>).
        ///   </description></item>
        /// </list>
        /// <para>
        /// Every comparison is ordinal, because both the event name and the namespace are case-sensitive in
        /// the legacy and neither is ever folded.
        /// </para>
        /// <para>
        /// <b>Two narrowings, both deliberate.</b> This method covers the name and namespace halves only.
        /// The legacy narrows further by target object (<c>:L1045-L1047</c>) and by handler name
        /// (<c>:L1048-L1050</c>) when either is supplied, and neither is carried by this type - an object
        /// reference and a handler name belong to the subscription entry in <c>EventBroker.cs</c>. And the
        /// broker applies a <b>second, independent negation</b> afterwards: the <c>excluding</c> flag at
        /// <c>:L1051-L1053</c> inverts the whole composite result, which is how the
        /// <c>of_Off(object, excluding)</c> overload works. <b>The two levels must not be conflated.</b>
        /// <see cref="TopicSymbols.Negation"/> negates one criterion inside the topic string;
        /// <c>excluding</c> negates the finished verdict from outside it, after the object and handler tests
        /// have been folded in, and it therefore cannot be expressed as a topic at all.
        /// </para>
        /// <para>
        /// Meaningful for a <see cref="TopicGrammar.Filter"/>. On a
        /// <see cref="TopicGrammar.Subscription"/> topic it degenerates to a plain equality test on the
        /// name plus the namespace, both negation flags being <see langword="false"/> there, which is
        /// well-defined but is not what a subscription is for.
        /// </para>
        /// </remarks>
        public bool Matches(string? subscriptionName, string? subscriptionNamespace)
        {
            // :L1030 - an empty name criterion matches every name, and the name comparison is skipped.
            bool matched = MatchesEveryName;

            if (!matched)
            {
                // :L1032-L1036 - equality, or inequality when the name is negated.
                bool nameEqual = string.Equals(
                    subscriptionName ?? string.Empty,
                    _legacyName,
                    StringComparison.Ordinal);

                matched = NegateName ? !nameEqual : nameEqual;
            }

            // :L1038 - the namespace participates only when it is a criterion at all.
            if (matched && HasNamespaceCriterion)
            {
                // :L1039-L1043 - equality, or inequality when the namespace is negated.
                bool namespaceEqual = string.Equals(
                    subscriptionNamespace ?? SubscriptionNamespaces.None,
                    Namespace,
                    StringComparison.Ordinal);

                matched = NegateNamespace ? !namespaceEqual : namespaceEqual;
            }

            return matched;
        }

        /// <summary>
        /// Decodes a topic string under the <b>subscribe</b> grammar, reproducing <c>of_on</c>
        /// (<c>n_cst_eventful.sru:L326-L381</c>) statement for statement.
        /// </summary>
        /// <param name="topic">
        /// The topic string as a caller would pass it to <c>of_on</c>: an optional leading run of symbols in
        /// any order, an optional numeric <c>[digits]:</c> priority, the event name, and an optional
        /// <c>.namespace</c> suffix. <see langword="null"/> is treated as empty and therefore fails, exactly
        /// as an empty string does.
        /// </param>
        /// <param name="result">
        /// On success, the decoded topic with <see cref="Grammar"/> set to
        /// <see cref="TopicGrammar.Subscription"/> and <see cref="RawValue"/> holding
        /// <paramref name="topic"/> verbatim. On failure, <see langword="null"/>.
        /// </param>
        /// <returns>
        /// <c>RetCode.OK</c> on success, or <c>RetCode.E_INVALID_ARGUMENT</c> for a malformed topic.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>DECISION (AAP 0.7.3 C-K) - a parse failure returns a CODE and never throws.</b> The legacy
        /// signals every one of these failures with <c>RetCode.E_INVALID_ARGUMENT</c> as an ordinary return
        /// value, so the port does the same, using the identical constant from
        /// <c>PowerFramework.Shared.Kernel</c>. Throwing would convert a routine, expected, caller-handled
        /// outcome into an exceptional one and would change how every call site is written - which is why
        /// this method carries an out-parameter rather than returning the topic: the code is the primary
        /// result, as it is in the legacy.
        /// </para>
        /// <para>
        /// <b>The algorithm, in the legacy's own order.</b> Each step names its locator so the two can be
        /// walked side by side:
        /// </para>
        /// <list type="number">
        ///   <item><description>
        ///   <c>:L331</c> An empty topic is invalid. The legacy tests the handler name on the same line and
        ///   the target object on <c>:L332</c>; both of those arguments belong to the broker, so both checks
        ///   do.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L328-L336</c> The defaults are established <b>before</b> parsing:
        ///   <see cref="Priorities.Normal"/>, <see cref="CaptureMode.Unhandled"/>, append rather than
        ///   prepend, and no namespace. "Still equal to the default" is then used as the legacy's proxy for
        ///   "not yet chosen", which is what the duplicate guards below test.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L339-L360</c> The leading symbol run: a character loop whose <c>choose case</c> handles
        ///   <c>-</c>, <c>%</c>, <c>*</c>, <c>@</c> and <c>!</c>, and whose <c>case else</c> arm <b>exits</b>.
        ///   <b>Symbol order is arbitrary</b> - only the position is fixed, in that the run must be leading -
        ///   so <c>"-!clicked"</c> and <c>"!-clicked"</c> are equally legal. Each symbol may appear once:
        ///   a repeated <c>-</c> fails, and <c>*</c> with <c>%</c> or <c>!</c> with <c>@</c> fail because the
        ///   second of the pair finds its slot already taken. Those duplicate guards are the whole mechanism
        ///   behind the combination rules stated at <c>w_test_eventful.srw:L232</c>; every other combination
        ///   is legal.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L361</c> If the run consumed the entire string, no event name remains and the topic is
        ///   invalid - so <c>"!"</c> fails. Worth knowing when reading the corpus: the only bare <c>"!"</c>
        ///   literal in the repository is not a topic but a concatenation prefix,
        ///   <c>of_On("!" + EVT_ITEMCHANGED, ...)</c> at
        ///   <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L2429</c>, whose real
        ///   topic is <c>"!0-itemchanged"</c>.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L363</c> The run is stripped; what remains is the residual name.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L365-L373</c> The numeric priority prefix. The <b>first</b>
        ///   <see cref="TopicSymbols.PriorityDelimiter"/> is located, and the text before it is treated as a
        ///   priority <b>only if it is numeric</b>. When it is: the duplicate guard fires if a symbol already
        ///   set the priority, then the value is taken and the prefix and its colon are stripped.
        ///   <b>When it is not, nothing at all happens and the colon stays in the name - this is not an
        ///   error.</b> So <c>"abc:def"</c> is a legal event name that contains a colon, and
        ///   <c>":clicked"</c> keeps its colon because the empty string is not numeric. See
        ///   <see cref="Priority"/> for the <c>"0:"</c> case and why it still cannot follow a symbol.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L375-L380</c> The namespace suffix. The <b>first</b>
        ///   <see cref="TopicSymbols.NamespaceSeparator"/> is located; the namespace is everything after it,
        ///   so <c>"a.b.c"</c> gives the name <c>"a"</c> and the namespace <c>"b.c"</c>, and the name is
        ///   everything before it. <b>An empty namespace is rejected</b>, which is why <c>"clicked."</c>
        ///   fails here while being a perfectly good filter under <see cref="ParseFilter"/>.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L381</c> An empty residual name is rejected, which is why <c>".myns"</c> fails as a
        ///   subscription while again being a legal filter.
        ///   </description></item>
        /// </list>
        /// <para>
        /// <b><see cref="TopicSymbols.Negation"/> (<c>^</c>) is not part of this grammar.</b> <c>of_on</c>
        /// has no <c>case</c> for it, so a leading <c>^</c> simply ends the run and becomes an ordinary
        /// character of the event name. No negation handling exists here, deliberately.
        /// </para>
        /// <para>
        /// <see cref="Sequence"/> and <see cref="LogicalName"/> need no work at this point: they are computed
        /// from the finished <see cref="LegacyName"/>, which is what keeps the reconstitution invariant
        /// true.
        /// </para>
        /// </remarks>
        public static long ParseSubscription(string? topic, out SubscriptionTopic? result)
        {
            result = null;

            // A null topic is the empty topic; the legacy models this argument as an always-present
            // possibly-empty string and rejects the empty one, so both arrive at the same outcome.
            string raw = topic ?? string.Empty;

            // :L331 - if name = "" then return RetCode.E_INVALID_ARGUMENT. The handler-name half of that
            // same test and the IsValidObject test on :L332 belong to the broker, which owns those
            // arguments; this parser is given only the topic.
            if (raw.Length == 0)
            {
                return RetCode.E_INVALID_ARGUMENT;
            }

            // :L328-L336 - the defaults, established BEFORE any parsing. "Still at the default" is the
            // legacy's proxy for "not yet chosen", which is exactly what the duplicate guards below test.
            int priority = Priorities.Normal;
            CaptureMode capture = CaptureMode.Unhandled;
            bool prepend = false;
            string? namespaceCriterion = null;

            string name = raw;

            // :L339-L360 - the leading symbol run. A character scan, not a regular expression: the
            // arbitrary symbol order and the once-only duplicate guards are the substance of this loop and
            // a pattern would hide both.
            int position = 0;
            while (position < name.Length)
            {
                char symbol = name[position];

                if (symbol == TopicSymbols.Prepend)
                {
                    // :L342-L344 - once only.
                    if (prepend)
                    {
                        return RetCode.E_INVALID_ARGUMENT;
                    }

                    prepend = true;
                }
                else if (symbol == TopicSymbols.Handled)
                {
                    // :L345-L347 - the capture slot is claimed once, by either % or *.
                    if (capture != CaptureMode.Unhandled)
                    {
                        return RetCode.E_INVALID_ARGUMENT;
                    }

                    capture = CaptureMode.Handled;
                }
                else if (symbol == TopicSymbols.All)
                {
                    // :L348-L350 - the other claimant of the capture slot.
                    if (capture != CaptureMode.Unhandled)
                    {
                        return RetCode.E_INVALID_ARGUMENT;
                    }

                    capture = CaptureMode.All;
                }
                else if (symbol == TopicSymbols.Low)
                {
                    // :L351-L353 - the priority slot is claimed once, by @, ! or a numeric prefix.
                    if (priority != Priorities.Normal)
                    {
                        return RetCode.E_INVALID_ARGUMENT;
                    }

                    priority = Priorities.Low;
                }
                else if (symbol == TopicSymbols.High)
                {
                    // :L354-L356 - the other symbolic claimant of the priority slot.
                    if (priority != Priorities.Normal)
                    {
                        return RetCode.E_INVALID_ARGUMENT;
                    }

                    priority = Priorities.High;
                }
                else
                {
                    // :L357-L358 - case else / exit. THE TRAP lives here: the run ends at the first
                    // character that is not a symbol, so the '0' of "0-itemchanged" ends it at position
                    // one and that topic's '-' is an ordinary character of the name.
                    break;
                }

                position++;
            }

            // :L361 - if nPos > nLen then return RetCode.E_INVALID_ARGUMENT. The run consumed everything,
            // so no event name remains.
            if (position >= name.Length)
            {
                return RetCode.E_INVALID_ARGUMENT;
            }

            // :L363 - newEvent.name = Mid(newEvent.name,nPos).
            name = name[position..];

            // :L365-L373 - the numeric priority prefix. IndexOf(char) is ordinal by definition, matching
            // Pos()'s first-occurrence search with no culture involvement.
            int delimiter = name.IndexOf(TopicSymbols.PriorityDelimiter);
            if (delimiter >= 0 && TryReadNumericPriority(name.AsSpan(0, delimiter), out int prefixPriority))
            {
                // :L369 - the guard is evaluated BEFORE the assignment on :L370, and tests only whether the
                // field has already moved off Normal. That is why "!9999:clicked" fails and why an
                // explicit "0:clicked" succeeds without consuming the slot.
                if (priority != Priorities.Normal)
                {
                    return RetCode.E_INVALID_ARGUMENT;
                }

                priority = prefixPriority;

                // :L371 - strip the prefix and its delimiter.
                name = name[(delimiter + 1)..];
            }

            // :L375-L380 - the namespace suffix, split on the FIRST separator.
            int separator = name.IndexOf(TopicSymbols.NamespaceSeparator);
            if (separator >= 0)
            {
                // :L377-L378 - everything after the separator is the namespace, everything before it the
                // name. Assigned in this order because the legacy reads the name before overwriting it.
                namespaceCriterion = name[(separator + 1)..];
                name = name[..separator];

                // :L379 - an empty namespace is invalid WHEN SUBSCRIBING. The identical string is a
                // meaningful filter under ParseFilter, and the two are deliberately not harmonised
                // (AAP 0.7.3 C-B).
                if (namespaceCriterion.Length == 0)
                {
                    return RetCode.E_INVALID_ARGUMENT;
                }
            }

            // :L381 - an empty residual name is invalid.
            if (name.Length == 0)
            {
                return RetCode.E_INVALID_ARGUMENT;
            }

            result = new SubscriptionTopic
            {
                Grammar = TopicGrammar.Subscription,
                LegacyName = name,
                NamespaceCriterion = namespaceCriterion,
                Capture = capture,
                Priority = priority,
                Prepend = prepend,
                RawValue = raw
            };

            return RetCode.OK;
        }

        /// <summary>
        /// Decodes a topic string under the <b>off/disable filter</b> grammar, reproducing
        /// <c>_of_modify</c>'s decode (<c>n_cst_eventful.sru:L1000-L1023</c>) statement for statement.
        /// </summary>
        /// <param name="filter">
        /// The filter string as a caller would pass it to any <c>of_off</c> or <c>of_disable</c> overload:
        /// an optionally <see cref="TopicSymbols.Negation"/>-prefixed name, and an optional
        /// <c>.namespace</c> suffix whose namespace may itself be negated and may be empty.
        /// <see langword="null"/> is treated as empty, which is the legal "match everything" filter.
        /// </param>
        /// <param name="result">
        /// On success, the decoded topic with <see cref="Grammar"/> set to
        /// <see cref="TopicGrammar.Filter"/> and <see cref="RawValue"/> holding <paramref name="filter"/>
        /// verbatim. On failure, <see langword="null"/>.
        /// </param>
        /// <returns>
        /// <c>RetCode.OK</c> on success, or <c>RetCode.E_INVALID_ARGUMENT</c> for the grammar's single
        /// rejection.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>DECISION (AAP 0.7.3 C-K) - this is a SECOND grammar and it shares no scanning code with
        /// <see cref="ParseSubscription"/>.</b> <c>_of_modify</c> is the single engine behind all seven
        /// <c>of_off</c> overloads and all seven <c>of_disable</c> overloads, and it parses the same eight
        /// symbols by entirely different rules. See <see cref="TopicGrammar"/> for the row-by-row
        /// comparison. Attempting to serve both from one scanner would require conditionalising every one
        /// of those rows inside it.
        /// </para>
        /// <para>
        /// <b>The algorithm, in the legacy's own order:</b>
        /// </para>
        /// <list type="number">
        ///   <item><description>
        ///   <c>:L1000-L1006</c> Split on the <b>first</b>
        ///   <see cref="TopicSymbols.NamespaceSeparator"/>. When one is present the namespace is everything
        ///   after it and the name everything before it, and the namespace <b>is</b> a criterion. When none
        ///   is present the namespace is <b>not a criterion at all</b> - the legacy's <c>bNoNamespace</c>
        ///   flag - which is a third state, not the empty namespace. <see cref="NamespaceCriterion"/> models
        ///   all three.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L1008-L1011</c> A leading <see cref="TopicSymbols.Negation"/> on the <b>name</b> sets
        ///   <see cref="NegateName"/> and is stripped.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L1012-L1015</c> <b>Separately and independently</b>, a leading
        ///   <see cref="TopicSymbols.Negation"/> on the <b>namespace</b> sets
        ///   <see cref="NegateNamespace"/> and is stripped. Either, both or neither may be negated; the
        ///   evidenced corpus exercises all four combinations.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L1017-L1019</c> Emptiness is <b>meaningful, not an error</b>: an empty name means "match
        ///   every name" (<see cref="MatchesEveryName"/>), and an empty namespace - when the separator was
        ///   present - means "match only subscriptions that have no namespace". The legacy computes its
        ///   object and handler-name flags on these same lines; both belong to the broker.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L1021-L1023</c> The grammar's <b>only</b> validation: a negated <i>empty</i> name is
        ///   rejected. Nothing else about a filter can fail, because the name comparison is skipped
        ///   entirely when the name is empty, leaving a negation with nothing to invert.
        ///   </description></item>
        /// </list>
        /// <para>
        /// <b>No symbol run, no priority, no capture.</b> A <c>-</c>, <c>!</c>, <c>@</c>, <c>%</c> or
        /// <c>*</c> in a filter string is an ordinary character, so the filter <c>"clicked"</c> matches the
        /// subscription made by <c>"!clicked"</c> - the symbol was consumed at subscribe time and never
        /// became part of the stored name.
        /// </para>
        /// <para>
        /// <b>The two evidenced extremes are worth contrasting.</b> The broker's own parameterless
        /// <c>of_off()</c> passes the <b>empty</b> filter and so removes <i>every</i> subscription
        /// (<c>:L228-L230</c>). The threading layer's parameterless <c>of_off()</c> instead passes
        /// <c>".^persistent"</c> (<c>ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544</c>), which
        /// decodes to an empty name - every name - together with a negated <c>"persistent"</c> namespace, and
        /// so removes everything <i>except</i> the persistent subscriptions. One symbol is the entire
        /// difference between those two behaviours; see the failure-mode note on <see cref="Lifetime"/>.
        /// </para>
        /// </remarks>
        public static long ParseFilter(string? filter, out SubscriptionTopic? result)
        {
            result = null;

            // A null filter is the empty filter, which is legal here and means "match everything" - the
            // legacy's own of_off() passes exactly that (:L228-L230).
            string raw = filter ?? string.Empty;

            string name = raw;
            string? namespaceCriterion = null;
            bool negateName = false;
            bool negateNamespace = false;

            // :L1000-L1006 - split on the first separator; its ABSENCE is the bNoNamespace state, which a
            // null criterion represents. Note this happens BEFORE the negation checks, so a '^' is only
            // ever recognised at the start of one of the two parts.
            int separator = name.IndexOf(TopicSymbols.NamespaceSeparator);
            if (separator >= 0)
            {
                namespaceCriterion = name[(separator + 1)..];
                name = name[..separator];
            }

            // :L1008-L1011 - Left(name,1) = SYMBOL_NOT. Left("",1) is "" and never equals "^", so an empty
            // name is not treated as negated; the length test reproduces that.
            if (name.Length > 0 && name[0] == TopicSymbols.Negation)
            {
                negateName = true;
                name = name[1..];
            }

            // :L1012-L1015 - the namespace's negation is tested INDEPENDENTLY of the name's. When no
            // separator was present the legacy's namespace variable is "", whose Left(...,1) is "", so the
            // test is false; skipping it for a null criterion is the same outcome.
            if (namespaceCriterion is not null
                && namespaceCriterion.Length > 0
                && namespaceCriterion[0] == TopicSymbols.Negation)
            {
                negateNamespace = true;
                namespaceCriterion = namespaceCriterion[1..];
            }

            // :L1017 bNoName = (name = ""), then :L1021-L1023 - the one and only validation in this
            // grammar. Every other emptiness is meaningful rather than invalid, which is the load-bearing
            // asymmetry with ParseSubscription and must not be harmonised (AAP 0.7.3 C-B).
            if (negateName && name.Length == 0)
            {
                return RetCode.E_INVALID_ARGUMENT;
            }

            result = new SubscriptionTopic
            {
                Grammar = TopicGrammar.Filter,
                LegacyName = name,
                NamespaceCriterion = namespaceCriterion,
                NegateName = negateName,
                NegateNamespace = negateNamespace,
                RawValue = raw
            };

            return RetCode.OK;
        }

        /// <summary>
        /// The dispatch ordering key the broker's ordered insert implements:
        /// <b><see cref="LegacyName"/> ascending under <see cref="StringComparer.Ordinal"/>, then
        /// <see cref="Priority"/> descending.</b>
        /// </summary>
        /// <param name="left">The first topic. May be <see langword="null"/>.</param>
        /// <param name="right">The second topic. May be <see langword="null"/>.</param>
        /// <returns>
        /// A negative value when <paramref name="left"/> is dispatched earlier, zero when the two are
        /// indistinguishable in dispatch order, and a positive value when <paramref name="right"/> is
        /// dispatched earlier.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>DECISION (AAP 0.7.3 C-K) - PowerBuilder's string relational comparison is reproduced with
        /// <see cref="StringComparer.Ordinal"/>, and a culture-sensitive comparison would be a defect.</b>
        /// The legacy compares event names with PowerScript's <c>&gt;</c> and <c>&lt;</c> operators in three
        /// distinct places: the ordered insert (<c>n_cst_eventful.sru:L408-L409</c>), the maintenance of the
        /// lexical name bounds (<c>:L439-L440</c>), and the fast rejects that test a triggered name against
        /// those bounds before scanning at all (<c>:L767-L770</c> and <c>:L797-L798</c>). Ordinal comparison
        /// is the faithful reproduction, and it is what keeps <c>"0-itemchanged"</c> ordered before
        /// <c>"1-editchanged"</c>. A culture-sensitive collation can reorder digits and punctuation, which
        /// would change dispatch order and the fast-reject outcome <i>per deployment locale</i> - a defect
        /// that would appear only on some machines and would additionally break the reproducibility that
        /// AAP 0.6.7 makes a hard prerequisite of the characterization evidence.
        /// </para>
        /// <para>
        /// <b>How the legacy's insert produces this key.</b> <c>:L407-L422</c> walks the subscription list:
        /// it skips entries whose name is ordinally lower, stops at the first whose name is ordinally
        /// greater, and within a run of equal names walks past entries of higher priority. The result is a
        /// list sorted by name ascending and, within each name, by priority descending - a larger priority
        /// number being dispatched earlier (<c>w_test_eventful.srw:L212</c>). Sorting a subscription list
        /// with this comparer reproduces that order.
        /// </para>
        /// <para>
        /// <b>What this key deliberately does NOT decide, because it cannot.</b> Within a run of <i>equal
        /// name and equal priority</i> the legacy's order is the insertion order, and
        /// <see cref="Prepend"/> selects which end of that run a new entry joins: prepend stops at the
        /// first entry whose priority is <c>&lt;=</c> the incoming one (<c>:L417</c>) and so lands at the
        /// head of the equal-priority run, while the default stops only at a strictly <c>&lt;</c> lower
        /// priority (<c>:L419</c>) and so lands at the tail. <b>Those two comparisons differ by exactly one
        /// character in the legacy, and that difference is the whole of the prepend semantics.</b> It is a
        /// property of an <i>insertion</i> into an existing list, not of a pairwise comparison, so it lives
        /// in <c>EventBroker.cs</c> and no comparer can express it. This one returns zero for two topics
        /// that differ only in <see cref="Prepend"/>, which is correct: they are equal <i>in this key</i>,
        /// and a stable sort therefore preserves whatever order the insert produced.
        /// </para>
        /// <para>
        /// A <see langword="null"/> sorts before any topic, so the comparison is total and never throws. The
        /// legacy has no null analogue here; the rule exists only so that the published comparer is
        /// well-behaved.
        /// </para>
        /// </remarks>
        public static int CompareDispatchOrder(SubscriptionTopic? left, SubscriptionTopic? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            // :L408-L409 - name ascending, ordinal. StringComparer.Ordinal reproduces PowerScript's
            // relational operators on strings; see this member's remarks for why nothing else will do.
            int byName = StringComparer.Ordinal.Compare(left.LegacyName, right.LegacyName);
            if (byName != 0)
            {
                return byName;
            }

            // :L416-L421 - within an equal name, higher priority first. Compared rather than subtracted,
            // because Priorities.Low and Priorities.High are the 32-bit bounds and their difference
            // overflows.
            return right.Priority.CompareTo(left.Priority);
        }

        /// <summary>
        /// The dispatch ordering key of <see cref="CompareDispatchOrder"/>, packaged for
        /// <see cref="List{T}.Sort(IComparer{T})"/>, <c>OrderBy</c> and the sorted collections.
        /// </summary>
        /// <value>
        /// A stateless, thread-safe singleton comparing <see cref="LegacyName"/> ascending under
        /// <see cref="StringComparer.Ordinal"/>, then <see cref="Priority"/> descending.
        /// </value>
        /// <remarks>
        /// Delegates to <see cref="CompareDispatchOrder"/>, so there is exactly one implementation of the
        /// key and the two can never disagree. Read that method's remarks before relying on this: in
        /// particular it deliberately returns zero for two topics that differ only in
        /// <see cref="Prepend"/>, because prepend is a property of an insertion rather than of a pairwise
        /// comparison.
        /// </remarks>
        public static IComparer<SubscriptionTopic> DispatchOrderComparer { get; } =
            new DispatchOrderComparerImplementation();

        /// <summary>
        /// Compares two topics by their <b>decoded</b> fields. <see cref="RawValue"/> is deliberately
        /// excluded.
        /// </summary>
        /// <param name="other">The topic to compare with. May be <see langword="null"/>.</param>
        /// <returns>
        /// <see langword="true"/> when both topics were decoded under the same grammar and agree on every
        /// decoded field; otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Written out rather than left to the record synthesizer for one reason: the synthesizer compares
        /// every instance field, which would draw <see cref="RawValue"/> into identity. <b>Identity here is
        /// what the topic MEANS</b> - <see cref="Grammar"/>, <see cref="LegacyName"/>,
        /// <see cref="NamespaceCriterion"/>, <see cref="Capture"/>, <see cref="Priority"/>,
        /// <see cref="Prepend"/>, <see cref="NegateName"/> and <see cref="NegateNamespace"/> - and not how
        /// it happened to be spelled.
        /// </para>
        /// <para>
        /// <b>The visible consequence, stated so it is never a surprise:</b> <c>"-!clicked"</c> and
        /// <c>"!-clicked"</c> are equal, and so are two topics that reached the same decoding by any other
        /// legal reordering of the leading run, yet <see cref="ToLegacyString"/> still returns each one's own
        /// original bytes. Equality and re-emission answer two different questions on purpose. The reverse
        /// choice would make two topics the broker cannot tell apart compare unequal, which is the more
        /// damaging of the two surprises given that identity is what a dictionary key, a set and an
        /// assertion are built on.
        /// </para>
        /// <para>
        /// The two string comparisons are ordinal, because the event name and the namespace are both
        /// case-sensitive in the legacy and neither is ever folded. The three-state
        /// <see cref="NamespaceCriterion"/> participates directly, so the filters <c>"clicked"</c>,
        /// <c>"clicked."</c> and <c>"clicked.myns"</c> are three <i>distinguishable</i> values rather than
        /// two.
        /// </para>
        /// </remarks>
        public bool Equals(SubscriptionTopic? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is null)
            {
                return false;
            }

            return Grammar == other.Grammar
                && string.Equals(_legacyName, other._legacyName, StringComparison.Ordinal)
                && string.Equals(NamespaceCriterion, other.NamespaceCriterion, StringComparison.Ordinal)
                && Capture == other.Capture
                && Priority == other.Priority
                && Prepend == other.Prepend
                && NegateName == other.NegateName
                && NegateNamespace == other.NegateNamespace;
        }

        /// <summary>
        /// Returns a hash code over exactly the fields <see cref="Equals(SubscriptionTopic)"/> compares.
        /// </summary>
        /// <returns>A hash code consistent with <see cref="Equals(SubscriptionTopic)"/>.</returns>
        /// <remarks>
        /// Written out alongside <see cref="Equals(SubscriptionTopic)"/> because the two must agree, and
        /// <see cref="RawValue"/> is excluded from both. <see cref="string"/>'s own hash is ordinal, which
        /// matches the ordinal equality used above.
        /// </remarks>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                Grammar,
                _legacyName,
                NamespaceCriterion,
                Capture,
                Priority,
                Prepend,
                NegateName,
                NegateNamespace);
        }

        /// <summary>
        /// Returns a short diagnostic rendering: the grammar, then the topic in its legacy spelling.
        /// </summary>
        /// <returns>For example <c>Subscription[-!clicked]</c> or <c>Filter[.^persistent]</c>.</returns>
        /// <remarks>
        /// <b>Diagnostic only.</b> Overridden rather than left to the record synthesizer both to keep log
        /// lines short and, more importantly, to name the grammar - a topic string read without its grammar
        /// invites exactly the misreading <see cref="TopicGrammar"/> warns about. Never parse this and never
        /// write it to a wire or a recording: <see cref="ToLegacyString"/> is the round-trip API, and it is
        /// the only one.
        /// </remarks>
        public override string ToString()
        {
            return string.Concat(Grammar.ToString(), "[", ToLegacyString(), "]");
        }

        /// <summary>
        /// Reads a <c>[digits]-</c> ordering prefix off a legacy event name under the recognition guard
        /// documented on <see cref="Sequence"/>.
        /// </summary>
        /// <param name="legacyName">The name to inspect. Never <see langword="null"/> at any call site.</param>
        /// <param name="sequence">The prefix value on success; zero otherwise.</param>
        /// <param name="logicalName">
        /// The name with the prefix removed on success; the whole of <paramref name="legacyName"/>
        /// otherwise.
        /// </param>
        /// <returns><see langword="true"/> when a prefix was recognised; otherwise <see langword="false"/>.</returns>
        /// <remarks>
        /// The single implementation behind both <see cref="Sequence"/> and <see cref="LogicalName"/>, which
        /// is what makes the reconstitution invariant on <see cref="LegacyName"/> unbreakable rather than
        /// merely intended. Every guard below exists to keep
        /// <c>Sequence is null ? LogicalName : $"{Sequence}-{LogicalName}"</c> equal to
        /// <paramref name="legacyName"/> for <i>every</i> input.
        /// </remarks>
        private static bool TryReadSequence(string legacyName, out int sequence, out string logicalName)
        {
            sequence = 0;
            logicalName = legacyName;

            // char.IsAsciiDigit rather than char.IsDigit: the latter accepts Unicode decimal digits, which
            // neither evidenced topic is spelled with and which could not be re-emitted as themselves.
            int digits = 0;
            while (digits < legacyName.Length && char.IsAsciiDigit(legacyName[digits]))
            {
                digits++;
            }

            // Guard 1 - there must be at least one digit.
            if (digits == 0)
            {
                return false;
            }

            // Guard 2 - the run must be terminated by the '-' that makes it a prefix rather than a name. A
            // purely numeric name such as "0" therefore has no sequence, and neither does "0abc".
            if (digits >= legacyName.Length || legacyName[digits] != TopicSymbols.Prepend)
            {
                return false;
            }

            // Guard 3 - no redundant leading zero. "00-x" is not a sequence, because re-emitting 0 would
            // yield "0-x" and silently rename the event.
            if (digits > 1 && legacyName[0] == FirstAsciiDigit)
            {
                return false;
            }

            // Guard 4 - the value must fit an int. The digit-count test is a cheap pre-filter that also
            // keeps the accumulator below the 64-bit range.
            if (digits > MaximumSequenceDigits)
            {
                return false;
            }

            long accumulated = 0;
            for (int index = 0; index < digits; index++)
            {
                accumulated = (accumulated * 10) + (legacyName[index] - FirstAsciiDigit);
            }

            if (accumulated > int.MaxValue)
            {
                return false;
            }

            sequence = (int)accumulated;
            logicalName = legacyName[(digits + 1)..];

            return true;
        }

        /// <summary>
        /// Reproduces the legacy's <c>IsNumber</c> test and <c>Long</c> conversion over the text preceding a
        /// <see cref="TopicSymbols.PriorityDelimiter"/>, for the integral forms whose conversion is
        /// determinable.
        /// </summary>
        /// <param name="text">The candidate prefix - the text before the first delimiter.</param>
        /// <param name="priority">The parsed priority on success; <see cref="Priorities.Normal"/> otherwise.</param>
        /// <returns>
        /// <see langword="true"/> when the text is accepted as a priority; otherwise
        /// <see langword="false"/>, in which case the caller must leave the text and its delimiter in the
        /// event name.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>DECISION (AAP 0.7.3 C-K, and AAP 0.1.5's rule that an unreproducible behaviour is narrowed
        /// with a defined outcome rather than widened with a guess) - the accepted forms.</b> Accepted: an
        /// optional single leading sign, then one or more <b>ASCII</b> digits, with the value fitting
        /// <see cref="int"/>. Rejected, and therefore left in the event name: the empty string, a decimal
        /// point, an exponent, whitespace, a group separator, a Unicode digit, and any value outside the
        /// 32-bit range.
        /// </para>
        /// <para>
        /// <b>Why this is narrower than PowerBuilder's <c>IsNumber</c>, and why narrowing is the correct
        /// call.</b> <c>IsNumber</c> also accepts decimal and exponent notation, so the legacy would treat
        /// <c>"1.5:clicked"</c> as carrying a priority - but the rounding <c>Long()</c> applies to such a
        /// value is not determinable from anything in the repository, the native runtime that decides it is
        /// closed, and <b>no call site anywhere in the 544 legacy objects exercises a non-integral
        /// priority</b>: the only evidenced numeric topic is <c>"9999:clicked"</c>
        /// (<c>ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L226</c>). Guessing a rounding direction
        /// would invent behaviour; rejecting the prefix instead lands on an outcome the legacy grammar
        /// already has and already documents - a non-numeric prefix is silently left in the name
        /// (<c>n_cst_eventful.sru:L368</c>) - so the shape of the result is legal even where the
        /// classification differs. Should the behavioural oracle ever be exercised, this method is the one
        /// place to revisit.
        /// </para>
        /// <para>
        /// <b>On the sign.</b> A leading <c>+</c> is reachable - <c>"+5:clicked"</c> ends the symbol run at
        /// the <c>+</c> and offers <c>"+5"</c> here - and PowerBuilder accepts it, so this method does too. A
        /// leading <c>-</c> is accepted for the same faithfulness even though it is unreachable in practice:
        /// the leading run consumes a first <c>-</c> as <see cref="TopicSymbols.Prepend"/> and rejects a
        /// second, so no residual name can begin with one. The character is the same as
        /// <see cref="TopicSymbols.Prepend"/> but its role here is arithmetic, which is why it is named
        /// separately below rather than borrowed from the symbol vocabulary.
        /// </para>
        /// </remarks>
        private static bool TryReadNumericPriority(ReadOnlySpan<char> text, out int priority)
        {
            priority = Priorities.Normal;

            if (text.Length == 0)
            {
                return false;
            }

            int index = 0;
            bool negative = false;

            if (text[0] == NegativeSign || text[0] == PositiveSign)
            {
                negative = text[0] == NegativeSign;
                index = 1;
            }

            // A sign on its own is not a number.
            if (index >= text.Length)
            {
                return false;
            }

            long accumulated = 0;

            for (; index < text.Length; index++)
            {
                if (!char.IsAsciiDigit(text[index]))
                {
                    return false;
                }

                accumulated = (accumulated * 10) + (text[index] - FirstAsciiDigit);

                // Bounded on every iteration so an arbitrarily long run of digits cannot overflow the
                // accumulator itself. The bound is the magnitude of int.MinValue, so the negative extreme
                // remains representable.
                if (accumulated > MaximumPriorityMagnitude)
                {
                    return false;
                }
            }

            if (negative)
            {
                priority = (int)(-accumulated);

                return true;
            }

            if (accumulated > int.MaxValue)
            {
                return false;
            }

            priority = (int)accumulated;

            return true;
        }

        /// <summary>
        /// Emits the canonical subscribe-grammar spelling of a topic built from parts, in the order
        /// documented on <see cref="ToLegacyString"/>.
        /// </summary>
        /// <returns>The canonical topic string. Never <see langword="null"/>.</returns>
        /// <remarks>
        /// Reached only when <see cref="RawValue"/> is <see langword="null"/>; a parsed topic re-emits its
        /// own bytes instead. The priority is written with the invariant culture so that the digits are
        /// ASCII and carry no group separator, which is what the parser will accept when reading them back.
        /// </remarks>
        private string BuildCanonicalSubscription()
        {
            string canonical = string.Empty;

            if (Prepend)
            {
                canonical += TopicSymbols.Prepend;
            }

            if (Capture == CaptureMode.All)
            {
                canonical += TopicSymbols.All;
            }
            else if (Capture == CaptureMode.Handled)
            {
                canonical += TopicSymbols.Handled;
            }

            if (Priority == Priorities.High)
            {
                canonical += TopicSymbols.High;
            }
            else if (Priority == Priorities.Low)
            {
                canonical += TopicSymbols.Low;
            }
            else if (Priority != Priorities.Normal)
            {
                canonical += Priority.ToString(CultureInfo.InvariantCulture);
                canonical += TopicSymbols.PriorityDelimiter;
            }

            canonical += LegacyName;

            if (HasNamespaceCriterion)
            {
                canonical += TopicSymbols.NamespaceSeparator;
                canonical += Namespace;
            }

            return canonical;
        }

        /// <summary>
        /// Emits the canonical filter-grammar spelling of a topic built from parts, in the order documented
        /// on <see cref="ToLegacyString"/>.
        /// </summary>
        /// <returns>The canonical filter string, which may legitimately be empty. Never <see langword="null"/>.</returns>
        /// <remarks>
        /// The subscribe options are never emitted, because the filter grammar cannot express them and a
        /// symbol written here would be read back as an ordinary character of the name. The empty result is
        /// meaningful: it is the "match everything" filter the broker's own parameterless <c>of_off()</c>
        /// uses (<c>n_cst_eventful.sru:L228-L230</c>).
        /// </remarks>
        private string BuildCanonicalFilter()
        {
            string canonical = string.Empty;

            if (NegateName)
            {
                canonical += TopicSymbols.Negation;
            }

            canonical += LegacyName;

            if (HasNamespaceCriterion)
            {
                canonical += TopicSymbols.NamespaceSeparator;

                if (NegateNamespace)
                {
                    canonical += TopicSymbols.Negation;
                }

                canonical += Namespace;
            }

            return canonical;
        }

        /// <summary>
        /// The lowest ASCII digit, used both as the digit-run floor test and as the arithmetic origin when
        /// accumulating a digit's value.
        /// </summary>
        private const char FirstAsciiDigit = '0';

        /// <summary>
        /// The arithmetic minus sign accepted at the head of a numeric priority prefix. It is the same
        /// character as <see cref="TopicSymbols.Prepend"/> but plays an arithmetic rather than a grammatical
        /// role here, so it is named separately; see <see cref="TryReadNumericPriority"/>.
        /// </summary>
        private const char NegativeSign = '-';

        /// <summary>
        /// The arithmetic plus sign accepted at the head of a numeric priority prefix. It is not a grammar
        /// symbol, which is why a topic beginning with it ends the leading symbol run.
        /// </summary>
        private const char PositiveSign = '+';

        /// <summary>
        /// The number of digits in <see cref="int.MaxValue"/>, used as a cheap pre-filter on a candidate
        /// sequence prefix before the value is accumulated.
        /// </summary>
        private const int MaximumSequenceDigits = 10;

        /// <summary>
        /// The magnitude of <see cref="int.MinValue"/>, which is the largest absolute value a signed 32-bit
        /// priority can represent and therefore the bound the priority accumulator is held below.
        /// </summary>
        private const long MaximumPriorityMagnitude = 2147483648L;

        /// <summary>
        /// The <see cref="IComparer{T}"/> face of <see cref="CompareDispatchOrder"/>.
        /// </summary>
        /// <remarks>
        /// Private and nested so that the ordering key is published as an interface rather than as a type
        /// callers could subclass or depend on by name, and so that no public nested type exists in this
        /// file. Stateless, therefore trivially thread-safe and safe to hand out as a singleton.
        /// </remarks>
        private sealed class DispatchOrderComparerImplementation : IComparer<SubscriptionTopic>
        {
            /// <summary>
            /// Compares two topics by the dispatch ordering key.
            /// </summary>
            /// <param name="x">The first topic. May be <see langword="null"/>.</param>
            /// <param name="y">The second topic. May be <see langword="null"/>.</param>
            /// <returns>The result of <see cref="CompareDispatchOrder"/>.</returns>
            public int Compare(SubscriptionTopic? x, SubscriptionTopic? y)
            {
                return CompareDispatchOrder(x, y);
            }
        }
    }
}
