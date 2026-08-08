// =====================================================================================================
//  SubscriptionOptions.cs
//  PowerFramework.Shared.Eventful
// =====================================================================================================
//
//  WHAT THIS FILE IS
//  The event broker's subscription-option VOCABULARY: the capture modes, the dispatch priorities, the
//  modification kinds, the eight topic-grammar symbols, the namespace and lifetime concept, and the
//  immutable option aggregate a subscription is created with. It is a pure leaf - no dispatch state, no
//  callback delegate, no object reference, no I/O, and not one using directive. The subscription entry
//  itself, the topic parser and the veto protocol live in EventBroker.cs, SubscriptionTopic.cs and
//  VetoResult.cs respectively; this file is the shared word list all three spell against.
//
//  AUTHORITATIVE SOURCE (the behavioural oracle - read as specification, never edited: AAP 0.7.3 C-C)
//      ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru
//          :L91-L98      the eight grammar symbols
//          :L100-L102    the capture flags
//          :L104-L106    the priorities
//          :L108-L109    the modification kinds
//          :L326-L381    of_on - the pre-parse defaults and the leading-run symbol scan
//          :L407-L422    of_on - the ordered insert that fixes the priority ordering rule
//          :L786, :L831  _of_trigger - the capture filter
//          :L909-L933    _of_trigger - the handled-state rule
//          :L993-L1090   _of_modify - the single engine behind every of_off and every of_disable
//      The prevent flags at :L110-L112 (PREVENT_ONCE = 1, PREVENT_DEEP = 2) belong to VetoResult.cs and
//      are deliberately absent from this file.
//
//  REFERENCE SOURCES (read, never ported, never edited)
//      ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L207-L248
//          The in-source grammar specification, and the closest thing this codebase has to written
//          documentation for the topic language. AAP 0.2.2.3 places the whole of pfw.tests permanently
//          out of scope as characterization-fixture and REFERENCE source. It is authoritative where it
//          and the implementation agree; the IMPLEMENTATION is authoritative where they differ, and they
//          already differ in one place - the block names a function of_SetDefaultValue where the real
//          function is of_SetDefaultReturnValue (w_test_eventful.srw:L260).
//      ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L54, :L57
//          The two real topics whose names begin "0-" and "1-", which is why the prepend symbol is only
//          ever a symbol inside the leading run. See the trap note on TopicSymbols.
//      ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544, :L596
//          The ".^persistent" bulk-unsubscribe filter that gives the lifetime concept its reason to
//          exist.
//
//  RULES POSITION, STATED EXPLICITLY
//  review_rules reports exactly one line: "No user rules provided." No user-specified rule governs this
//  file, none was invented to fill the gap, and that absence is not licence to lower the bar. In their
//  place the AAP 0.7.2 enterprise baseline and the AAP 0.7.3 binding non-rule constraints apply:
//
//      C-B   No behaviour improvements. Every numeric value and every symbol character below is
//            reproduced exactly - nothing renumbered, nothing reordered, no value invented that the
//            legacy does not have, and the two 32-bit bounds left bit-identical rather than "tidied"
//            into something that merely reads better.
//      C-C   The legacy tree is read-only. Every .sru path above is specification only and no build
//            input; nothing in this project compiles or embeds a legacy path.
//      C-K   Every technology-specific decision is documented at its point of reproduction. The four in
//            this file are each marked DECISION and cover the 32-bit priority width, the const-versus-
//            enum choice for the priorities, the one-character-string-to-char change for the symbols,
//            and the culture-invariant handler-name folding.
//
//  NAMING: PascalCase MEMBERS CARRYING THE LEGACY VALUES, LEGACY SPELLINGS IN THE DOC COMMENTS
//  The repository-root .editorconfig scopes its naming-analyzer suppressions to the individual files
//  that genuinely must carry the preserved SCREAMING_SNAKE constant identifiers, file glob by file glob.
//  This file is NOT one of them and Directory.Build.props sets TreatWarningsAsErrors, so every member
//  here is PascalCase and each legacy spelling is recorded in its own member's doc comment instead. That
//  costs nothing and no widening of that glob list was requested: unlike RetCode and Enums, none of
//  these identifier SPELLINGS crosses a wire or lands in a characterization recording. The VALUES do,
//  and the values are exact.
// =====================================================================================================

namespace PowerFramework.Shared.Eventful
{
    /// <summary>
    /// Selects which dispatch handled-states a subscription is willing to observe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from the capture flags at
    /// <c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L100-L102</c>, whose legacy
    /// identifiers are <c>CAP_UNHANDLED</c> (0), <c>CAP_HANDLED</c> (1) and <c>CAP_ALL</c> (2). The
    /// numeric values are reproduced exactly because they are a behavioural contract, not an
    /// implementation detail: they appear in comparisons and in characterization recordings.
    /// </para>
    /// <para>
    /// <b>Dispatch semantics</b>, from the filter in <c>_of_trigger</c>. A dispatch begins in the
    /// unhandled state (<c>nCap = CAP_UNHANDLED</c>, <c>:L786</c>) and each candidate subscription is
    /// tested at <c>:L831-L832</c>: a subscription is <i>skipped</i> when its capture mode differs from
    /// the dispatch's current handled-state, <b>unless</b> its mode is <see cref="All"/>. The default is
    /// <see cref="Unhandled"/>, so a plain subscription only ever sees an event that no earlier
    /// subscriber has handled.
    /// </para>
    /// <para>
    /// <b>How the grammar reaches this</b>, from <c>w_test_eventful.srw:L214-L216</c>:
    /// <see cref="TopicSymbols.All"/> (<c>*</c>) captures every handled state,
    /// <see cref="TopicSymbols.Handled"/> (<c>%</c>) captures only events already handled by an earlier
    /// subscriber, and a topic carrying neither symbol captures unhandled events only.
    /// </para>
    /// <para>
    /// <b>How handled-ness is decided</b>, from <c>w_test_eventful.srw:L234</c> and the implementation at
    /// <c>:L909-L933</c>. A subscriber is treated as having handled the event when its return value is
    /// <i>unequal to the default return value</i> configured for that event; a subscriber that returns
    /// nothing at all is defined as not having handled it. The legacy also promotes to handled when the
    /// default return value is null, when its runtime class is <c>any</c>, or when the comparison itself
    /// throws.
    /// </para>
    /// <para>
    /// <b>The handled state cannot roll back.</b> The transition at <c>:L910</c> is guarded by
    /// <c>if nCap = CAP_UNHANDLED</c> and no reverse assignment exists anywhere in the object, so once a
    /// dispatch is handled it never returns to unhandled - although the return <i>value</i> may still be
    /// overwritten by a later subscriber (<c>:L925-L932</c>, and stated as such at
    /// <c>w_test_eventful.srw:L235</c>). Any consumer that models this as a resettable flag reintroduces
    /// a defect the legacy does not have.
    /// </para>
    /// <para>
    /// These are mutually exclusive states rather than combinable bits, so this enumeration deliberately
    /// carries no <see cref="System.FlagsAttribute"/>. The legacy confirms the exclusivity mechanically:
    /// the scan at <c>:L345-L350</c> rejects a second capture symbol outright, because whichever of
    /// <c>%</c> or <c>*</c> arrives second finds the field already moved off <c>CAP_UNHANDLED</c> and
    /// fails the whole subscription with an invalid-argument code.
    /// </para>
    /// </remarks>
    public enum CaptureMode
    {
        /// <summary>
        /// Observe only events that no earlier subscriber has handled. Legacy <c>CAP_UNHANDLED</c> = 0
        /// (<c>n_cst_eventful.sru:L100</c>).
        /// </summary>
        /// <remarks>
        /// This is the default in both directions, and by two independent mechanisms. It is the value
        /// the legacy subscription record starts at before the topic is parsed, and being zero it is
        /// also the default of this enumeration, so a defaulted <see cref="SubscriptionOptions"/> and a
        /// defaulted legacy record agree without either having to say so.
        /// </remarks>
        Unhandled = 0,

        /// <summary>
        /// Observe only events an earlier subscriber has already handled. Legacy <c>CAP_HANDLED</c> = 1
        /// (<c>n_cst_eventful.sru:L101</c>); selected by the <see cref="TopicSymbols.Handled"/>
        /// (<c>%</c>) symbol.
        /// </summary>
        Handled = 1,

        /// <summary>
        /// Observe the event in every handled state. Legacy <c>CAP_ALL</c> = 2
        /// (<c>n_cst_eventful.sru:L102</c>); selected by the <see cref="TopicSymbols.All"/> (<c>*</c>)
        /// symbol.
        /// </summary>
        /// <remarks>
        /// This is the one value that survives the <c>:L831-L832</c> filter unconditionally: it is
        /// tested for by name there, which is precisely what makes it a wildcard over the handled-state
        /// axis rather than a third point on it.
        /// </remarks>
        All = 2
    }

    /// <summary>
    /// The three well-known dispatch priorities, reproduced with the legacy's exact 32-bit values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L104-L106</c>, whose
    /// legacy identifiers are <c>PRIORITY_LOW</c> (-2147483648), <c>PRIORITY_NORMAL</c> (0) and
    /// <c>PRIORITY_HIGH</c> (2147483647).
    /// </para>
    /// <para>
    /// <b>DECISION (AAP 0.7.3 C-K) - why the type is <see cref="int"/> and not <see cref="long"/>.</b>
    /// A PowerBuilder <c>long</c> is a <b>32-bit</b> signed integer, not the 64-bit type the C# keyword
    /// of the same name names. The legacy literals -2147483648 and 2147483647 are therefore exactly the
    /// 32-bit bounds, which is to say exactly <see cref="int.MinValue"/> and <see cref="int.MaxValue"/>.
    /// Mapping a PowerBuilder <c>long</c> onto a C# <see cref="int"/> is consequently loss-free in both
    /// directions - no truncation, no widening - and the two named bounds below are the legacy values
    /// themselves rather than an approximation of them. Porting the field as a C# <c>long</c> would
    /// silently widen the domain and make <see cref="Low"/> and <see cref="High"/> stop being the
    /// extremes they are relied upon to be. The same 32-bit width applies to the capture and
    /// modification fields, which is why <see cref="CaptureMode"/> and <see cref="ModificationKind"/>
    /// keep the default <see cref="int"/> underlying type.
    /// </para>
    /// <para>
    /// <b>DECISION (AAP 0.7.3 C-K) - why these are constants on a static class and not enum members.</b>
    /// A custom numeric priority is a first-class legal value in this grammar, not an escape hatch:
    /// <c>of_On("9999:clicked", ...)</c> is an evidenced call
    /// (<c>ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L226</c>), and <c>of_on</c> parses any
    /// numeric prefix terminated by <see cref="TopicSymbols.PriorityDelimiter"/> straight into the
    /// priority field (<c>n_cst_eventful.sru:L365-L372</c>) with no range check and no membership test.
    /// Modelling the priority as an enumeration would misrepresent the domain by implying that the three
    /// named values are the only legal ones. The priority is an <see cref="int"/>; these are three
    /// well-known points on it.
    /// </para>
    /// <para>
    /// <b>Ordering rule - a larger number is a higher dispatch priority.</b> Stated at
    /// <c>w_test_eventful.srw:L212</c> and mechanically fixed by the ordered insert at
    /// <c>n_cst_eventful.sru:L407-L422</c>, which walks the subscription list and stops at the first
    /// entry whose priority is lower than the incoming one, leaving each name's run sorted by descending
    /// priority. The default is <see cref="Normal"/>, assigned at <c>:L334</c> before the topic is
    /// parsed. Any ordered-insert implementation in <c>EventBroker.cs</c> depends on this direction, and
    /// inverting it would reverse dispatch order across the whole framework while still passing a naive
    /// subscriber-count assertion.
    /// </para>
    /// <para>
    /// <b>Priority is independent of the namespace</b> (<c>w_test_eventful.srw:L233</c>). The two are
    /// orthogonal axes of the same topic string: the namespace is generally used only in combination
    /// with the off and disable operations, and never participates in ordering. See
    /// <see cref="SubscriptionNamespaces"/>.
    /// </para>
    /// </remarks>
    public static class Priorities
    {
        /// <summary>
        /// The lowest dispatch priority; such subscriptions are dispatched last. Legacy
        /// <c>PRIORITY_LOW</c> = -2147483648 (<c>n_cst_eventful.sru:L104</c>), selected by the
        /// <see cref="TopicSymbols.Low"/> (<c>@</c>) symbol.
        /// </summary>
        /// <remarks>
        /// <see cref="int.MinValue"/> <i>is</i> -2147483648, so this is the legacy literal exactly and
        /// not a stand-in for it. It is spelled as the named bound rather than as the digits so that the
        /// identity with the 32-bit floor is visible at the point of use, which is the whole reason the
        /// port is loss-free.
        /// </remarks>
        public const int Low = int.MinValue;

        /// <summary>
        /// The default dispatch priority, used by any topic that names no priority at all. Legacy
        /// <c>PRIORITY_NORMAL</c> = 0 (<c>n_cst_eventful.sru:L105</c>).
        /// </summary>
        /// <remarks>
        /// This value carries a second, load-bearing role beyond being the default. The legacy uses
        /// "still equal to <c>PRIORITY_NORMAL</c>" as its proxy for "no priority has been chosen yet",
        /// at <c>:L352</c>, <c>:L355</c> and <c>:L369</c>. That is the exact mechanism by which
        /// <c>!</c> or <c>@</c> combined with a numeric <c>digits:</c> prefix is rejected - whichever
        /// arrives second finds the field already moved off this value and fails the subscription with
        /// an invalid-argument code. A consequence worth knowing when reading recordings: an explicit
        /// <c>"0:clicked"</c> leaves the field at this value, so it does not consume the single priority
        /// slot and may still be combined with <c>!</c> or <c>@</c>.
        /// </remarks>
        public const int Normal = 0;

        /// <summary>
        /// The highest dispatch priority; such subscriptions are dispatched first. Legacy
        /// <c>PRIORITY_HIGH</c> = 2147483647 (<c>n_cst_eventful.sru:L106</c>), selected by the
        /// <see cref="TopicSymbols.High"/> (<c>!</c>) symbol.
        /// </summary>
        /// <remarks>
        /// <see cref="int.MaxValue"/> <i>is</i> 2147483647, so this is the legacy literal exactly. As
        /// with <see cref="Low"/> it is spelled as the named bound to keep the 32-bit identity visible.
        /// </remarks>
        public const int High = int.MaxValue;
    }

    /// <summary>
    /// Discriminates the two operations the broker's single subscription-modification engine performs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L108-L109</c>, whose
    /// legacy identifiers are <c>MOD_OFF</c> (1) and <c>MOD_DISABLE</c> (2).
    /// </para>
    /// <para>
    /// <b>Why this discriminator exists at all.</b> The legacy has exactly one private engine,
    /// <c>_of_modify</c> (<c>:L993-L1090</c>), and every one of the seven <c>of_off</c> overloads and
    /// every one of the seven <c>of_disable</c> overloads funnels through it. They differ in nothing but
    /// this value and, for the disable case, the boolean it carries. Removal and enable/disable therefore
    /// share <i>one</i> subscription-matching filter (<c>:L1030-L1053</c>) and diverge only at the very
    /// end, where <c>:L1054</c> destroys or invalidates the matched entry and <c>:L1068</c> merely sets
    /// its disabled flag.
    /// </para>
    /// <para>
    /// That shared filter is the reason the negation grammar has to be understood once and then applies
    /// to both operations identically - see <see cref="TopicSymbols.Negation"/> and
    /// <see cref="SubscriptionNamespaces"/>. Getting the filter right once saves getting it wrong twice.
    /// </para>
    /// <para>
    /// The legacy set has no zero member: the values begin at 1. That is reproduced verbatim under
    /// AAP 0.7.3 C-B, which forbids inventing a value the legacy does not have - a synthesized
    /// <c>None = 0</c> would be a new state that no legacy code path can produce and no legacy consumer
    /// knows how to interpret. The two states are mutually exclusive, never combined, so this
    /// enumeration deliberately carries no <see cref="System.FlagsAttribute"/>; the legacy tests it with
    /// equality (<c>if mod = MOD_OFF</c>, <c>elseif mod = MOD_DISABLE</c>) and never with a bit test.
    /// </para>
    /// </remarks>
    public enum ModificationKind
    {
        /// <summary>
        /// Remove the matched subscriptions outright. Legacy <c>MOD_OFF</c> = 1
        /// (<c>n_cst_eventful.sru:L108</c>), the mode behind every <c>of_off</c> overload.
        /// </summary>
        /// <remarks>
        /// Removal is re-entrancy aware in the legacy and a faithful port must stay so: while a dispatch
        /// is in progress the matched entries are only <i>marked</i> invalid and the actual collection is
        /// deferred, whereas outside a dispatch they are destroyed and the list rebuilt immediately
        /// (<c>:L1054-L1067</c>, with the deferred sweep posted at <c>:L1081-L1087</c>). Removing entries
        /// from underneath an in-flight dispatch would otherwise shift the indices that dispatch is
        /// walking.
        /// </remarks>
        Off = 1,

        /// <summary>
        /// Set or clear the disabled flag on the matched subscriptions, leaving them in place. Legacy
        /// <c>MOD_DISABLE</c> = 2 (<c>n_cst_eventful.sru:L109</c>), the mode behind every
        /// <c>of_disable</c> overload.
        /// </summary>
        /// <remarks>
        /// This mode carries a separate boolean payload, so it both disables and re-enables; it is not a
        /// one-way operation. A disabled subscription is skipped during dispatch (<c>:L830</c>) while
        /// remaining subscribed, which is what distinguishes it from <see cref="Off"/>.
        /// </remarks>
        Disable = 2
    }

    /// <summary>
    /// The eight single-character symbols of the subscription topic grammar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L91-L98</c>, whose legacy
    /// identifiers are <c>SYMBOL_PREPEND</c>, <c>SYMBOL_LOW</c>, <c>SYMBOL_HIGH</c>, <c>SYMBOL_PRIO</c>,
    /// <c>SYMBOL_HANDLED</c>, <c>SYMBOL_ALL</c>, <c>SYMBOL_NS</c> and <c>SYMBOL_NOT</c>. Each character
    /// is reproduced exactly; the grammar is documented with worked examples at
    /// <c>ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L207-L248</c>.
    /// </para>
    /// <para>
    /// <b>DECISION (AAP 0.7.3 C-K) - why these are <see cref="char"/> and the legacy declares
    /// <c>string</c>.</b> The legacy declares each symbol as a one-character <c>string</c> because
    /// PowerScript has no character type and compares single characters by taking a one-character
    /// substring with <c>Mid(name, nPos, 1)</c>. C# has <see cref="char"/>, so the port uses it. This is
    /// a representation change with no observable effect whatsoever: the characters, the grammar and
    /// every accept/reject decision are identical, and a <see cref="char"/> additionally removes a class
    /// of defect the legacy shape invites, namely a multi-character value silently reaching a field the
    /// scanner can only ever compare one character at a time.
    /// </para>
    /// <para>
    /// <b>Combination rules</b> (<c>w_test_eventful.srw:L232</c>). <see cref="High"/> and
    /// <see cref="Low"/> must <b>not</b> be combined with a numeric <c>digits:</c> prefix, and
    /// <see cref="All"/> must <b>not</b> be combined with <see cref="Handled"/>. <b>Every other
    /// combination is legal and the symbol order is arbitrary, provided every symbol sits at the very
    /// start of the string.</b> Both prohibitions are enforced by the same trick rather than by a pattern
    /// match - the scanner treats "the field still holds its default" as "nothing has claimed this slot
    /// yet", so the second claimant to a slot fails with an invalid-argument code
    /// (<c>:L343</c>, <c>:L346</c>, <c>:L349</c>, <c>:L352</c>, <c>:L355</c> and <c>:L369</c>). That
    /// arbitrary ordering is exactly why <c>of_on</c> implements the scan as a <c>choose case</c> inside a
    /// character loop instead of a fixed-order pattern, and it is why the evidenced calls
    /// <c>"-!clicked"</c> and <c>"-@clicked"</c> (<c>w_test_eventful.srw:L224-L225</c>) are both valid.
    /// </para>
    /// <para>
    /// <b>THE TRAP - a symbol is only a symbol inside the leading run.</b> The scanner's <c>choose case</c>
    /// ends in <c>case else / exit</c> (<c>:L357-L358</c>), so it stops at the <i>first</i> character that
    /// matches no symbol and <c>:L363</c> then strips only what it consumed. In the real topic
    /// <c>"0-itemchanged"</c> (<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L54</c>) the
    /// leading <c>0</c> matches no symbol, so the scan exits at the first character and the <c>-</c> is an
    /// ordinary character <i>of the event name</i> - never a prepend request. The same holds for its
    /// sibling <c>"1-editchanged"</c> (<c>:L57</c>), where those leading digits are the lexical sequence
    /// prefix that orders the two topics relative to one another, subscriptions being sorted by name
    /// ascending (<c>w_test_eventful.srw:L238</c>). A helper that stripped or detected symbol characters
    /// <i>anywhere</i> in a topic would silently corrupt both of these live topics. This type therefore
    /// offers exactly one convenience predicate, <see cref="IsLeadingRunSymbol"/>, and it is scoped to
    /// leading-run testing by contract - read its own remarks before using it.
    /// </para>
    /// </remarks>
    public static class TopicSymbols
    {
        /// <summary>
        /// <c>'-'</c> - insert at the <i>head</i> of the matching priority's queue instead of its tail.
        /// Legacy <c>SYMBOL_PREPEND</c> (<c>n_cst_eventful.sru:L91</c>).
        /// </summary>
        /// <remarks>
        /// The default is to append to the tail (<c>w_test_eventful.srw:L209</c>). The two orderings
        /// differ by a single comparison in the ordered insert: prepend stops at the first entry whose
        /// priority is less than <i>or equal to</i> the incoming one (<c>:L417</c>), landing the new
        /// entry before its equal-priority peers, while the default stops only at a strictly lower
        /// priority (<c>:L419</c>), landing it after them. Reproducing that <c>&lt;=</c> versus <c>&lt;</c>
        /// distinction is what makes prepend observable at all, since it changes nothing about which
        /// priority band the subscription joins. Note that this symbol counts once only: a repeat inside
        /// the leading run is rejected (<c>:L343</c>).
        /// </remarks>
        public const char Prepend = '-';

        /// <summary>
        /// <c>'@'</c> - request the lowest dispatch priority, <see cref="Priorities.Low"/>. Legacy
        /// <c>SYMBOL_LOW</c> (<c>n_cst_eventful.sru:L92</c>).
        /// </summary>
        public const char Low = '@';

        /// <summary>
        /// <c>'!'</c> - request the highest dispatch priority, <see cref="Priorities.High"/>. Legacy
        /// <c>SYMBOL_HIGH</c> (<c>n_cst_eventful.sru:L93</c>).
        /// </summary>
        public const char High = '!';

        /// <summary>
        /// <c>':'</c> - terminates a numeric custom-priority prefix, as in <c>"9999:clicked"</c>. Legacy
        /// <c>SYMBOL_PRIO</c> (<c>n_cst_eventful.sru:L94</c>).
        /// </summary>
        /// <remarks>
        /// This symbol is <b>not</b> part of the leading run and is deliberately excluded from
        /// <see cref="IsLeadingRunSymbol"/>. The legacy handles it in a separate step <i>after</i> the
        /// leading-run scan has finished (<c>:L365-L373</c>), by searching the remaining text for the
        /// first occurrence and treating the text before it as a priority only when that text is
        /// numeric. Two consequences follow that a parser must honour: a non-numeric prefix such as
        /// <c>"a:clicked"</c> is left entirely alone, colon and all, so the name simply contains a colon;
        /// and because the search is not anchored, the prefix is only recognised at the start of what
        /// remains after the leading run.
        /// </remarks>
        public const char PriorityDelimiter = ':';

        /// <summary>
        /// <c>'%'</c> - capture only events an earlier subscriber has already handled, selecting
        /// <see cref="CaptureMode.Handled"/>. Legacy <c>SYMBOL_HANDLED</c>
        /// (<c>n_cst_eventful.sru:L95</c>).
        /// </summary>
        public const char Handled = '%';

        /// <summary>
        /// <c>'*'</c> - capture the event in every handled state, selecting
        /// <see cref="CaptureMode.All"/>. Legacy <c>SYMBOL_ALL</c> (<c>n_cst_eventful.sru:L96</c>).
        /// </summary>
        /// <remarks>
        /// Mutually exclusive with <see cref="Handled"/>: both claim the same capture slot, so whichever
        /// appears second is rejected (<c>:L346</c>, <c>:L349</c>).
        /// </remarks>
        public const char All = '*';

        /// <summary>
        /// <c>'.'</c> - separates the event name from its namespace, as in <c>"clicked.myns"</c>. Legacy
        /// <c>SYMBOL_NS</c> (<c>n_cst_eventful.sru:L97</c>).
        /// </summary>
        /// <remarks>
        /// Like <see cref="PriorityDelimiter"/> this is <b>not</b> part of the leading run and is
        /// excluded from <see cref="IsLeadingRunSymbol"/>; the legacy splits on it after the leading-run
        /// scan and after the priority prefix (<c>:L375-L380</c> when subscribing,
        /// <c>:L1000-L1006</c> when modifying). A separator present with nothing after it is rejected
        /// (<c>:L379</c>), whereas a separator with nothing <i>before</i> it is meaningful and important
        /// - it is what makes <c>".^persistent"</c> a filter over every name. See
        /// <see cref="SubscriptionNamespaces"/>.
        /// </remarks>
        public const char NamespaceSeparator = '.';

        /// <summary>
        /// <c>'^'</c> - negates the name or the namespace of an off/disable filter, as in
        /// <c>".^persistent"</c>. Legacy <c>SYMBOL_NOT</c> (<c>n_cst_eventful.sru:L98</c>).
        /// </summary>
        /// <remarks>
        /// This symbol belongs to the modification grammar only. It is not recognised when subscribing -
        /// <c>of_on</c>'s scan has no case for it, so it would terminate the leading run and become part
        /// of an event name - and it is correspondingly excluded from
        /// <see cref="IsLeadingRunSymbol"/>. <c>_of_modify</c> recognises it in <i>two independent
        /// positions</i>, as the first character of the name and as the first character of the namespace
        /// (<c>:L1008-L1015</c>), and each flips its own comparison from equality to inequality
        /// (<c>:L1033</c> and <c>:L1040</c>). A bare negation with no name is rejected (<c>:L1022</c>).
        /// See <see cref="SubscriptionNamespaces"/> for why this matters more than its size suggests.
        /// </remarks>
        public const char Negation = '^';

        /// <summary>
        /// The five symbols the subscription scanner recognises inside a topic's <b>leading run</b>, in
        /// the order the legacy <c>choose case</c> tests them.
        /// </summary>
        /// <value>
        /// <see cref="Prepend"/>, <see cref="Low"/>, <see cref="High"/>, <see cref="Handled"/> and
        /// <see cref="All"/> - and deliberately <i>not</i> <see cref="PriorityDelimiter"/>,
        /// <see cref="NamespaceSeparator"/> or <see cref="Negation"/>.
        /// </value>
        /// <remarks>
        /// Exposed so a parser or a parity test can enumerate the recognised set without restating the
        /// characters and without reflection. It is derived from the constants above rather than from
        /// repeated literals, so the set cannot drift from them. The three excluded symbols are excluded
        /// because the legacy scanner has no <c>case</c> for any of them (<c>:L341-L359</c>) and would
        /// therefore end the leading run on encountering one; each is instead consumed by a later,
        /// separate step. See <see cref="IsLeadingRunSymbol"/>.
        /// </remarks>
        public static ReadOnlySpan<char> LeadingRunSymbols => [Prepend, Handled, All, Low, High];

        /// <summary>
        /// Tests whether <paramref name="candidate"/> is one of the five symbols the subscription scanner
        /// recognises inside a topic's <b>leading run</b> - that is, whether it would continue the run
        /// rather than end it.
        /// </summary>
        /// <param name="candidate">The character to test.</param>
        /// <returns>
        /// <see langword="true"/> for <see cref="Prepend"/>, <see cref="Handled"/>, <see cref="All"/>,
        /// <see cref="Low"/> and <see cref="High"/>; otherwise <see langword="false"/>, including for
        /// <see cref="PriorityDelimiter"/>, <see cref="NamespaceSeparator"/> and
        /// <see cref="Negation"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>Scope, by contract: this predicate answers a question about the leading run and nothing
        /// else.</b> It is deliberately <i>not</i> a general "is this character a grammar symbol" test,
        /// and it must never be used to scan, strip or split a topic at an arbitrary position. Correct
        /// use is to walk a topic from index 0 and stop at the first character for which this returns
        /// <see langword="false"/>, exactly mirroring the legacy <c>choose case</c> whose
        /// <c>case else</c> arm exits the loop (<c>n_cst_eventful.sru:L341-L359</c>).
        /// </para>
        /// <para>
        /// Using it position-independently would corrupt live topics. <c>"0-itemchanged"</c>
        /// (<c>se_cst_dw.sru:L54</c>) and <c>"1-editchanged"</c> (<c>:L57</c>) each contain a
        /// <see cref="Prepend"/> character that is an ordinary part of the event name, because the
        /// leading digit already ended the run; a position-independent strip would silently rename both
        /// and detach every subscriber. Likewise <c>"clicked.myns"</c>
        /// (<c>w_test_eventful.srw:L227</c>) contains a <see cref="NamespaceSeparator"/> that this
        /// predicate rejects by design, because the separator is consumed by a later step rather than by
        /// the run.
        /// </para>
        /// <para>
        /// A topic consisting only of characters this predicate accepts is invalid - the legacy rejects
        /// it at <c>:L361</c> because no event name remains after the run is stripped. Detecting that is
        /// the caller's job; this predicate reports on one character.
        /// </para>
        /// </remarks>
        public static bool IsLeadingRunSymbol(char candidate)
        {
            // Ordered as the legacy choose case orders its arms (:L342-L356). The order is immaterial to
            // the result - the arms are disjoint single characters - but it keeps this readable directly
            // against the oracle. Note the three symbols consumed by later parsing steps are absent by
            // design; see this member's remarks.
            return candidate is Prepend or Handled or All or Low or High;
        }
    }

    /// <summary>
    /// A convenience projection of a subscription's namespace onto the one lifetime distinction the
    /// legacy actually acts upon: whether a bulk unsubscribe sweeps the subscription away or spares it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AAP 0.3.4 and 0.6.1.2 require the wire contract to carry <c>sequence</c>, <c>name</c> and
    /// <c>lifetime</c> as three separate fields, because the legacy topic string fuses three independent
    /// encodings - a lexical ordering prefix, the logical name, and a persistence namespace suffix - into
    /// one opaque value that does not survive naive serialization. This enumeration is the decomposition
    /// of that third encoding.
    /// </para>
    /// <para>
    /// <b>THE NAMESPACE IS AN ARBITRARY STRING IN THE LEGACY, NOT A CLOSED SET, so this enumeration is
    /// deliberately NOT its only representation and must never become its only representation.</b>
    /// <c>of_on</c> takes whatever text follows the separator and stores it unexamined
    /// (<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L375-L380</c>); the sole validation
    /// is that it is non-empty. <c>of_On("clicked.myns", ...)</c> is an evidenced call
    /// (<c>ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L227</c>), as is the compound
    /// <c>"*clicked.myns"</c> (<c>:L251</c>). <see cref="SubscriptionOptions.Namespace"/> therefore
    /// carries the raw string and <b>that string is authoritative</b>; this projection is a convenience
    /// over it and is intentionally lossy, mapping every namespace that is not the well-known persistent
    /// one - <c>"myns"</c> and the empty default alike - onto <see cref="Transient"/>. Constraining a
    /// namespace to this enumeration's members would reject <c>.myns</c> outright and break the real
    /// usage in DataServices and Persistence.
    /// </para>
    /// <para>
    /// <b>Why the distinction is load-bearing rather than cosmetic.</b> The threading layer's
    /// parameterless bulk unsubscribe is <c>of_Off(".^persistent")</c>, evidenced at six sites across
    /// three objects: <c>ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544</c> and <c>:L596</c>,
    /// <c>n_cst_threading_task.sru:L385</c> and <c>:L391</c>, and <c>n_cst_thread.sru:L552</c> and
    /// <c>:L561</c>. Decoded against <c>_of_modify</c>, that topic carries an empty name - which matches
    /// <i>every</i> name, because an absent name disables the name test entirely (<c>:L1017</c>,
    /// <c>:L1030</c>) - and a <see cref="TopicSymbols.Negation"/>-prefixed namespace, which flips the
    /// namespace test from equality to inequality (<c>:L1012-L1014</c>, <c>:L1040</c>). It therefore
    /// reads as <i>"every name WHERE namespace is NOT persistent"</i>, and a bulk unsubscribe
    /// deliberately <b>spares</b> persistent subscriptions. The name-scoped variants at <c>:L596</c>,
    /// <c>:L391</c> and <c>:L561</c> confirm the intent: each appends the same negated filter unless the
    /// caller already supplied a namespace of its own.
    /// </para>
    /// <para>
    /// <b>The consequence for any consumer.</b> Modelling lifetime as a plain flag and dropping the
    /// negation on the way into the filter deletes exactly the subscriptions that were registered to
    /// survive - a silent behavioural regression with no failing assertion to announce it, since the
    /// unsubscribe still reports success and the survivors are simply gone. That is why
    /// <see cref="TopicSymbols.Negation"/> exists as a first-class symbol, and why the negation must be
    /// carried through into the filter by <c>SubscriptionTopic</c> and <c>EventBroker</c> rather than
    /// being collapsed here. This projection reports what a namespace <i>is</i>; it cannot express what a
    /// filter <i>excludes</i>.
    /// </para>
    /// </remarks>
    public enum SubscriptionLifetime
    {
        /// <summary>
        /// The subscription is not in the persistent namespace and is therefore swept by the
        /// <c>".^persistent"</c> bulk unsubscribe.
        /// </summary>
        /// <remarks>
        /// This covers both a subscription with no namespace at all - the legacy default, an empty string
        /// - and one in any other namespace, <c>"myns"</c> included. Being zero, it is also this
        /// enumeration's default, so a defaulted <see cref="SubscriptionOptions"/> reports the lifetime
        /// its empty namespace implies without either value having to be set.
        /// </remarks>
        Transient = 0,

        /// <summary>
        /// The subscription is in the well-known <see cref="SubscriptionNamespaces.Persistent"/>
        /// namespace and is therefore spared by the <c>".^persistent"</c> bulk unsubscribe.
        /// </summary>
        Persistent = 1
    }

    /// <summary>
    /// The subscription namespace vocabulary: the legacy default, the one evidenced well-known namespace,
    /// and the projection from a raw namespace string onto <see cref="SubscriptionLifetime"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A namespace is an arbitrary, caller-chosen string. This type names the two values the framework
    /// itself relies on and converts a raw string to its lifetime; it neither validates a namespace nor
    /// constrains one. See <see cref="SubscriptionLifetime"/> for why that restraint is required rather
    /// than merely permissive.
    /// </para>
    /// <para>
    /// <b>Namespace comparison is case-SENSITIVE.</b> Unlike the handler name, the namespace is never
    /// case-folded: <c>of_on</c> stores the substring as written
    /// (<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L377</c>), <c>_of_modify</c> takes
    /// the filter's namespace as written (<c>:L1002</c>) and the two are compared with PowerScript's
    /// case-sensitive string equality (<c>:L1040</c>, <c>:L1042</c>). Every comparison here is therefore
    /// ordinal, and <c>"Persistent"</c> with a capital letter is a <i>different</i> namespace from
    /// <c>"persistent"</c> that a bulk unsubscribe will sweep. That is legacy behaviour, reproduced under
    /// AAP 0.7.3 C-B rather than smoothed over. Use
    /// <see cref="SubscriptionOptions.NamespaceComparer"/> when comparing namespaces yourself.
    /// </para>
    /// </remarks>
    public static class SubscriptionNamespaces
    {
        /// <summary>
        /// The absence of a namespace, represented as the empty string.
        /// </summary>
        /// <remarks>
        /// This is the legacy default. <c>of_on</c> leaves the namespace field at its uninitialised
        /// PowerScript value - the empty string - whenever the topic carries no
        /// <see cref="TopicSymbols.NamespaceSeparator"/>, and <c>_of_modify</c> treats an absent
        /// namespace as "do not filter on the namespace at all" rather than as "match the empty
        /// namespace" (<c>n_cst_eventful.sru:L1004-L1005</c>, <c>:L1038</c>). A namespace that is present
        /// but empty is a different thing again and is rejected outright (<c>:L379</c>).
        /// </remarks>
        public const string None = "";

        /// <summary>
        /// The one namespace name the framework itself relies on, spelled exactly as the legacy spells
        /// it: <c>"persistent"</c>.
        /// </summary>
        /// <remarks>
        /// Evidenced at six sites across three objects, always as the negated half of a bulk unsubscribe:
        /// <c>ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544</c> and <c>:L596</c>,
        /// <c>n_cst_threading_task.sru:L385</c> and <c>:L391</c>, and <c>n_cst_thread.sru:L552</c> and
        /// <c>:L561</c>. The lower-case spelling is part of the contract, because namespaces are compared
        /// ordinally - see this type's remarks.
        /// </remarks>
        public const string Persistent = "persistent";

        /// <summary>
        /// Projects a raw namespace string onto the lifetime distinction the legacy acts upon.
        /// </summary>
        /// <param name="subscriptionNamespace">
        /// The raw namespace, exactly as it appeared after the
        /// <see cref="TopicSymbols.NamespaceSeparator"/>. May be <see langword="null"/> or empty, both of
        /// which mean "no namespace".
        /// </param>
        /// <returns>
        /// <see cref="SubscriptionLifetime.Persistent"/> when <paramref name="subscriptionNamespace"/> is
        /// ordinally equal to <see cref="Persistent"/>; otherwise
        /// <see cref="SubscriptionLifetime.Transient"/>.
        /// </returns>
        /// <remarks>
        /// The comparison is ordinal and therefore case-sensitive, matching the legacy exactly. The
        /// projection is intentionally lossy - every namespace other than the well-known one collapses to
        /// <see cref="SubscriptionLifetime.Transient"/> - so it is a convenience for reading intent and
        /// never a substitute for carrying the raw string. It does not round-trip: recovering
        /// <c>"myns"</c> from <see cref="SubscriptionLifetime.Transient"/> is impossible, which is
        /// precisely why <see cref="SubscriptionOptions.Namespace"/> exists alongside
        /// <see cref="SubscriptionOptions.Lifetime"/>.
        /// </remarks>
        public static SubscriptionLifetime ToLifetime(string? subscriptionNamespace)
        {
            // Ordinal by design, not by omission: see the type-level remarks. A null and an empty
            // namespace are the same thing to the legacy, and string.Equals handles null on the left
            // without a guard, so neither needs one here.
            return string.Equals(subscriptionNamespace, Persistent, StringComparison.Ordinal)
                ? SubscriptionLifetime.Persistent
                : SubscriptionLifetime.Transient;
        }

        /// <summary>
        /// Reports whether a raw namespace string is the well-known <see cref="Persistent"/> namespace,
        /// and therefore whether a <c>".^persistent"</c> bulk unsubscribe spares it.
        /// </summary>
        /// <param name="subscriptionNamespace">
        /// The raw namespace. May be <see langword="null"/> or empty, both of which mean "no namespace"
        /// and are not persistent.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the namespace is ordinally equal to <see cref="Persistent"/>;
        /// otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// Equivalent to testing <see cref="ToLifetime"/> against
        /// <see cref="SubscriptionLifetime.Persistent"/>, and offered because the negated form is what
        /// the legacy filter actually asks: the sweep removes everything for which this returns
        /// <see langword="false"/>.
        /// </remarks>
        public static bool IsPersistent(string? subscriptionNamespace)
        {
            return string.Equals(subscriptionNamespace, Persistent, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The resolved set of options a single subscription is created with, once its topic string has been
    /// decoded: the capture mode, the dispatch priority, the same-priority insertion end, the namespace,
    /// and the handler name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the aggregate the file is named for, and it is deliberately <b>vocabulary only</b>. It
    /// carries no callback delegate, no target object reference and no dispatch state - not the disabled
    /// flag, not the invalidated flag, not the re-entrancy marker, not the resolved method identifier.
    /// Those belong to the subscription entry inside <c>EventBroker.cs</c>, which is the port of the
    /// legacy <c>EVENTDATA</c> structure; this type is the immutable, comparable, freely copyable subset
    /// of it that decoding produces and that a contract can carry. Keeping the two apart is what lets an
    /// option set be compared, logged and asserted on without dragging a live object graph along.
    /// </para>
    /// <para>
    /// <b>The defaults are the legacy's own</b>, as <c>of_on</c> establishes them <i>before</i> it parses
    /// the topic (<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L328-L336</c>):
    /// <see cref="Priority"/> starts at <see cref="Priorities.Normal"/> (<c>:L334</c>),
    /// <see cref="Capture"/> at <see cref="CaptureMode.Unhandled"/> (the PowerScript field's
    /// uninitialised value, which the scan then tests against at <c>:L346</c> and <c>:L349</c>),
    /// <see cref="Prepend"/> at <see langword="false"/> - meaning append to the tail of the priority
    /// queue (<c>:L328</c>, <c>:L419</c>) - and <see cref="Namespace"/> at
    /// <see cref="SubscriptionNamespaces.None"/>. A default-constructed instance therefore matches a
    /// freshly initialised legacy subscription record field for field, which is what
    /// <see cref="Default"/> exposes.
    /// </para>
    /// <para>
    /// <b>The two case rules, which differ from each other and must not be unified.</b> Stated inline at
    /// <c>ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L250</c> and implemented at
    /// <c>n_cst_eventful.sru:L336</c> and <c>:L998</c>:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///     The <b>event name is case-sensitive</b>. It is stored and compared as written, so
    ///     <c>"clicked"</c> and <c>"Clicked"</c> are two different events. Compare event names with
    ///     <see cref="NameComparer"/>. Ordinal comparison is doubly required here because event names are
    ///     also <i>ordered</i> - the subscription list is sorted by name ascending and dispatch relies on
    ///     that order (<c>:L407-L422</c>, <c>w_test_eventful.srw:L238</c>) - and a culture-sensitive
    ///     collation would reorder dispatch on a machine with a different culture.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     The <b>handler name is case-insensitive</b>, because the legacy folds it to lower case at both
    ///     ends: on storage when subscribing (<c>:L336</c>) and on comparison when modifying
    ///     (<c>:L998</c>). Compare handler names with <see cref="HandlerNameComparer"/>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     The <b>namespace is case-sensitive</b> and is never folded at all - see
    ///     <see cref="SubscriptionNamespaces"/>. Compare namespaces with
    ///     <see cref="NamespaceComparer"/>.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// <see cref="HandlerName"/> reproduces the legacy folding at the point of assignment rather than
    /// leaving every consumer to remember it, which also makes this record's synthesized structural
    /// equality agree with legacy matching for free: two option sets differing only in the case of the
    /// handler name are equal, because both were normalised on the way in.
    /// </para>
    /// </remarks>
    public sealed record SubscriptionOptions
    {
        private readonly string _namespace = SubscriptionNamespaces.None;
        private readonly string _handlerName = string.Empty;

        /// <summary>
        /// Which dispatch handled-states this subscription observes. Defaults to
        /// <see cref="CaptureMode.Unhandled"/>, the legacy default.
        /// </summary>
        public CaptureMode Capture { get; init; } = CaptureMode.Unhandled;

        /// <summary>
        /// The dispatch priority, where a larger number is dispatched earlier. Defaults to
        /// <see cref="Priorities.Normal"/>, the legacy default.
        /// </summary>
        /// <remarks>
        /// Any <see cref="int"/> is legal, not merely the three named values on
        /// <see cref="Priorities"/>: the legacy parses an arbitrary numeric prefix straight into this
        /// field with no range check (<c>n_cst_eventful.sru:L370</c>), and
        /// <c>of_On("9999:clicked", ...)</c> is an evidenced call
        /// (<c>ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L226</c>). No validation is applied here
        /// for the same reason - adding one would be new behaviour, which AAP 0.7.3 C-B forbids.
        /// </remarks>
        public int Priority { get; init; } = Priorities.Normal;

        /// <summary>
        /// When <see langword="true"/>, insert at the <i>head</i> of the matching priority's queue rather
        /// than its tail. Defaults to <see langword="false"/>, the legacy default of appending to the
        /// tail.
        /// </summary>
        /// <remarks>
        /// Set by the <see cref="TopicSymbols.Prepend"/> (<c>-</c>) symbol. This selects an end of the
        /// queue, not a priority: it changes only where the subscription lands among the peers that share
        /// its <see cref="Priority"/> (<c>n_cst_eventful.sru:L416-L420</c>).
        /// </remarks>
        public bool Prepend { get; init; }

        /// <summary>
        /// The raw namespace, exactly as written after the
        /// <see cref="TopicSymbols.NamespaceSeparator"/>, or <see cref="SubscriptionNamespaces.None"/>
        /// when the topic named none. Never <see langword="null"/>.
        /// </summary>
        /// <value>
        /// An arbitrary caller-chosen string. <b>This raw string is authoritative</b>, not the
        /// <see cref="Lifetime"/> projection over it, and it is deliberately unconstrained so that a
        /// namespace such as <c>"myns"</c> round-trips intact - see <see cref="SubscriptionLifetime"/>.
        /// </value>
        /// <remarks>
        /// A <see langword="null"/> assignment is normalised to <see cref="SubscriptionNamespaces.None"/>
        /// so that consumers never need a null check on a value the legacy models as an always-present
        /// possibly-empty string. Nothing else is altered: no trimming, no case folding, no validation.
        /// Namespace comparison is ordinal; use <see cref="NamespaceComparer"/>.
        /// </remarks>
        public string Namespace
        {
            get => _namespace;
            init => _namespace = value ?? SubscriptionNamespaces.None;
        }

        /// <summary>
        /// The name of the callback event the broker invokes on the target, folded to lower case exactly
        /// as the legacy folds it. Never <see langword="null"/>.
        /// </summary>
        /// <value>
        /// The handler name in lower case. Assigning <c>"onButtonClicked1"</c> yields
        /// <c>"onbuttonclicked1"</c>.
        /// </value>
        /// <remarks>
        /// <para>
        /// <b>DECISION (AAP 0.7.3 C-K) - the fold is culture-invariant.</b> The legacy applies PowerScript
        /// <c>Lower()</c> to this value at both ends, when storing a new subscription
        /// (<c>n_cst_eventful.sru:L336</c>) and when matching one for removal or disabling
        /// (<c>:L998</c>), which is the entire mechanism by which handler names are case-insensitive. The
        /// port reproduces that fold here, at the one place the value enters the type, and does so with
        /// the invariant culture rather than the ambient one. Culture-sensitive folding would make the
        /// result depend on the host's locale - the Turkish dotless i being the standard example, where
        /// <c>"I"</c> folds to <c>"\u0131"</c> instead of <c>"i"</c> - and that would break two things at
        /// once: handler matching would vary by deployment, and the characterization recordings this
        /// refactor is verified against would stop being reproducible, which AAP 0.6.7 makes a hard
        /// prerequisite. Invariant folding is therefore the faithful choice, not merely the tidy one.
        /// </para>
        /// <para>
        /// Because the value is normalised on the way in, this record's structural equality already
        /// behaves case-insensitively for the handler name. <see cref="HandlerNameComparer"/> is exposed
        /// for comparisons against strings that have <i>not</i> come through this type - a name read from
        /// a wire payload or a configuration file, for instance.
        /// </para>
        /// <para>
        /// A <see langword="null"/> assignment is normalised to <see cref="string.Empty"/>. Note that the
        /// legacy rejects an empty handler name at the subscription boundary (<c>:L331</c>), so an empty
        /// value here denotes "not yet set" rather than a subscribable option set; validating that is the
        /// broker's responsibility, since it is the broker that owns the subscribe operation.
        /// </para>
        /// </remarks>
        public string HandlerName
        {
            get => _handlerName;
            init => _handlerName = Fold(value);
        }

        /// <summary>
        /// The lifetime <see cref="Namespace"/> projects onto: whether a <c>".^persistent"</c> bulk
        /// unsubscribe sweeps this subscription away or spares it.
        /// </summary>
        /// <value>
        /// <see cref="SubscriptionLifetime.Persistent"/> when <see cref="Namespace"/> is ordinally equal
        /// to <see cref="SubscriptionNamespaces.Persistent"/>; otherwise
        /// <see cref="SubscriptionLifetime.Transient"/>.
        /// </value>
        /// <remarks>
        /// A derived, intentionally lossy read over <see cref="Namespace"/> - it is not separately
        /// settable, precisely so the two can never disagree. Every namespace that is not the well-known
        /// persistent one collapses to <see cref="SubscriptionLifetime.Transient"/>, so this value does
        /// not round-trip and must not be persisted in place of the raw string.
        /// </remarks>
        public SubscriptionLifetime Lifetime => SubscriptionNamespaces.ToLifetime(_namespace);

        /// <summary>
        /// Whether the topic named a namespace at all.
        /// </summary>
        /// <value>
        /// <see langword="true"/> when <see cref="Namespace"/> is non-empty; otherwise
        /// <see langword="false"/>.
        /// </value>
        /// <remarks>
        /// This distinction is not cosmetic on the modification side, where an absent namespace means
        /// "do not filter on the namespace at all" rather than "match the empty namespace"
        /// (<c>n_cst_eventful.sru:L1004-L1005</c>, <c>:L1038</c>). A subscription always has a namespace
        /// value; a <i>filter</i> may legitimately have none.
        /// </remarks>
        public bool HasNamespace => _namespace.Length > 0;

        /// <summary>
        /// The legacy defaults as <c>of_on</c> establishes them before parsing a topic: capture
        /// <see cref="CaptureMode.Unhandled"/>, priority <see cref="Priorities.Normal"/>, append to the
        /// tail, no namespace, no handler name.
        /// </summary>
        /// <remarks>
        /// A shared immutable instance, safe to hand out because this type is a record with no mutable
        /// state. Use it as the base of a <c>with</c> expression to express only what a topic actually
        /// specified, which keeps the defaults asserted in exactly one place.
        /// </remarks>
        public static SubscriptionOptions Default { get; } = new();

        /// <summary>
        /// The comparer to use for <b>event names</b>: ordinal, and therefore case-sensitive.
        /// </summary>
        /// <remarks>
        /// Event names are case-sensitive (<c>ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L250</c>)
        /// and are additionally the sort key that fixes dispatch order
        /// (<c>n_cst_eventful.sru:L407-L422</c>), so ordinal comparison is required for ordering as well
        /// as for equality. This comparer is exposed on the option type because the event name itself
        /// lives on the topic rather than here, and both need to agree on how names compare.
        /// </remarks>
        public static StringComparer NameComparer => StringComparer.Ordinal;

        /// <summary>
        /// The comparer to use for <b>handler names</b>: ordinal and case-insensitive.
        /// </summary>
        /// <remarks>
        /// Handler names are case-insensitive because the legacy folds them at both ends
        /// (<c>n_cst_eventful.sru:L336</c>, <c>:L998</c>). <see cref="HandlerName"/> already applies that
        /// fold, so this comparer matters when comparing against a string that has not come through this
        /// type.
        /// </remarks>
        public static StringComparer HandlerNameComparer => StringComparer.OrdinalIgnoreCase;

        /// <summary>
        /// The comparer to use for <b>namespaces</b>: ordinal, and therefore case-sensitive.
        /// </summary>
        /// <remarks>
        /// Namespaces are never folded by the legacy and are compared with case-sensitive string equality
        /// (<c>n_cst_eventful.sru:L1040</c>, <c>:L1042</c>), so <c>"Persistent"</c> is a different
        /// namespace from <c>"persistent"</c>. See <see cref="SubscriptionNamespaces"/>.
        /// </remarks>
        public static StringComparer NamespaceComparer => StringComparer.Ordinal;

        /// <summary>
        /// Applies the legacy handler-name fold: culture-invariant lower case, with
        /// <see langword="null"/> normalised to <see cref="string.Empty"/>.
        /// </summary>
        /// <param name="value">The handler name as supplied.</param>
        /// <returns>The folded handler name, never <see langword="null"/>.</returns>
        /// <remarks>
        /// <para>
        /// Private because it is an implementation detail of <see cref="HandlerName"/>; the fold is part
        /// of that property's observable contract and is documented there.
        /// </para>
        /// <para>
        /// <b>The direction of the fold is fixed by the legacy and is not a stylistic choice.</b> General
        /// guidance prefers folding to upper case, on the grounds that lower-case folding is not
        /// round-trip safe for every script. That guidance does not apply here: the legacy folds to LOWER
        /// case (<c>n_cst_eventful.sru:L336</c> when subscribing and <c>:L998</c> when matching) and the
        /// folded value is <i>observable</i> - it is what a stored handler name looks like to anything
        /// that reads it back and what every subsequent comparison is made against. Folding upward
        /// instead would change behaviour, which AAP 0.7.3 C-B forbids. Should a future analyzer
        /// configuration flag this call, the resolution is a narrowly scoped suppression citing this
        /// remark, never a change of direction.
        /// </para>
        /// </remarks>
        private static string Fold(string? value)
        {
            if (value is null)
            {
                return string.Empty;
            }

            return value.ToLowerInvariant();
        }
    }
}
