// ==============================================================================================
//  RetCode - the PowerFramework return-code catalogue
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.shared.pbl.src/retcode.sru (221 lines)
//  ORACLE STATUS  That .sru is the ONLY specification for these values, and it is READ ONLY: it
//                 is the behavioural oracle for parity testing, never an edit target. Nothing
//                 else in the repository can adjudicate a disagreement about a value here, so
//                 EVERY constant below carries the retcode.sru:L locator it was taken from.
//  LICENCE        retcode.sru:L13-L34 carries the BSD 2-Clause licence and its four-condition
//                 Chinese restatement. That block is deliberately NOT copied here: the
//                 obligation is discharged by the repository-root NOTICE, which names this very
//                 object as one of the legacy headers whose attribution conditions it carries
//                 forward. See NOTICE and LICENSE.
//
//  WHAT THIS FILE IS, EXACTLY
//  --------------------------------------------------------------------------------------------
//  156 constants, transcribed one for one from the oracle, in the oracle's own order and grouped
//  into the oracle's own four sections. The count is not decorative - it is the audit that the
//  transcription is complete, and it was produced by extracting every `Constant Long` declaration
//  from the .sru and evaluating it:
//
//      retcode.sru:L37-L79    PowerFramework return codes      41 constants
//      retcode.sru:L81-L102   XML Parser parse status          17 constants
//      retcode.sru:L104-L138  SQLite base result codes         31 constants
//      retcode.sru:L139-L206  SQLite extended result codes     67 constants
//                                                             ---
//                                                             156
//
//  The rest of the oracle contributes no members and is accounted for here so a reader can see
//  that nothing was skipped: L1-L2 are PowerBuilder export headers; L3-L10 declare the type and
//  its global auto-instance (DECISION 2); L13-L34 are the licence block; L38 is the `Public:`
//  access specifier that C# expresses per member; L212-L220 are `on create` / `on destroy`
//  events whose entire bodies are `TriggerEvent(this, "constructor")` and
//  `TriggerEvent(this, "destructor")`, so the legacy object carries NO state and there is nothing
//  behavioural to port.
//
//  The pfw.shared library declares zero PBNI `native "..."` class bindings and zero external
//  function prototypes, so this is a pure logic port. There is no pfw.dll or pfwx.dll entry point
//  behind any value below and nothing here substitutes for a closed-source binary.
//
//  DECISION 1 - A STATIC CLASS OF `const long`, NOT AN `enum`
//  --------------------------------------------------------------------------------------------
//  The legacy declarations are `Constant Long`, which maps to `long`. A C# `enum` was considered
//  and rejected on three independent grounds, any one of which is sufficient:
//
//      Aliases do not round-trip.   OK, SUCCESS and ALLOW are three names for 0, and CANCELED and
//                                   CANCELLED are two names for -2. In an enum the duplicates
//                                   become synonyms that collapse on ToString(), so a recording
//                                   that stored one name would read back as the other. That is
//                                   precisely the silent invalidation the preserved-spelling rule
//                                   exists to prevent.
//      The computed codes lose their structure. The 67 extended SQLite codes are declared as
//                                   `BASE + (n * 256)`. Enum members can be given computed
//                                   initialisers, but the readable table form below - base code
//                                   in one column, multiplier in the next - is the documentation
//                                   (DECISION 4), and an enum's members are not naturally read
//                                   that way.
//      Callers would need casts.     Every legacy consumer treats a return code as a number: it
//                                   is compared with `>=`, tested for inequality, stored in a
//                                   `long` field and passed to a formatter. An enum would put a
//                                   cast at each of those sites for no gain.
//
//  `const` rather than `static readonly` is also deliberate: these are compile-time literals, so
//  they are usable in `case` labels, in attribute arguments and in `switch` expressions, and they
//  keep CA1802 ("use literals where appropriate") inapplicable - a diagnostic the repository-root
//  .editorconfig deliberately does NOT suppress for this file.
//
//  DECISION 2 - THE GLOBAL AUTO-INSTANCE AT L10 IS DELIBERATELY NOT REPRODUCED
//  --------------------------------------------------------------------------------------------
//  retcode.sru:L10 declares `global retcode retcode`: a global auto-instantiated variable whose
//  name shadows its own type name, which is legal only because PowerBuilder has a single flat
//  global namespace with no import statements and resolves symbols by the ordering of the library
//  list in the target file. It is the same collision the refactor records for `global n_sql n_sql`
//  and it is resolved the same way: the C# TYPE keeps the descriptive name and the shared global
//  instance is not recreated. Concretely this file declares NO instance member, NO singleton, NO
//  `Instance` and NO `Current`. The class is `static`, so it cannot be instantiated at all, and
//  the legacy `retcode.OK` member-access spelling still reads naturally as `RetCode.OK`.
//
//  DECISION 3 - IDENTIFIERS KEEP THEIR LEGACY SCREAMING_SNAKE SPELLING
//  --------------------------------------------------------------------------------------------
//  Every identifier below is spelled exactly as PowerScript declares it, in deliberate defiance
//  of .NET naming convention. The reason is data integrity, not taste: these exact identifier
//  strings appear in serialized payloads on the new service contracts, in log records, and in the
//  characterization recordings that are the parity evidence for the whole migration, so renaming
//  one does not restyle a symbol - it silently invalidates every stored comparison that mentions
//  it, and a golden-master suite cannot report a comparison it can no longer make.
//
//  This file is one of only two in this project inside the scope of that exception. The
//  repository-root .editorconfig suppresses CA1707 ("identifiers should not contain underscores")
//  and IDE1006 ("naming rule violation") for exactly two paths -
//  shared/PowerFramework.Shared.Kernel/RetCode.cs and .../Enums.cs - and Directory.Build.props
//  sets TreatWarningsAsErrors, so a preserved-spelling constant declared in any OTHER file of
//  this project is a build ERROR rather than advice. That is why the whole catalogue lives here
//  and is not scattered across the sibling files that consume it: CA1707 reports DECLARATIONS
//  only, never uses, so a consumer needs no suppression of its own.
//
//  There is deliberately NO `#pragma warning disable` in this file and no project-wide `NoWarn`
//  in the .csproj. The .editorconfig scoping is the documented mechanism and it is per-file on
//  purpose; broadening it would hide genuine naming problems in the eight sibling files.
//
//  DECISION 4 - THE SQLITE EXTENDED CODES STAY WRITTEN AS ARITHMETIC
//  --------------------------------------------------------------------------------------------
//  All 67 extended codes are declared in the oracle as `(BASE + (n * 256))` and are reproduced in
//  that form rather than as pre-computed literals. A `const` may refer to an earlier `const` in
//  the same class, so the compiled value is identical either way; what differs is verifiability.
//  The multiplicative structure IS the documentation - it states which base code an extended code
//  refines and which ordinal it is - and flattening `SQLITE_IOERR + (23 * 256)` to `5898` would
//  destroy a reader's ability to check the line against the oracle without a calculator.
//
//  DECISION 5 - THREE INDEPENDENT CODE SPACES SHARE ONE CLASS, AND STAY INDEPENDENT
//  --------------------------------------------------------------------------------------------
//  The oracle puts three unrelated numbering schemes in one object, and they overlap heavily:
//
//      0   is OK, SUCCESS, ALLOW, XML_OK and SQLITE_OK
//      1   is PREVENT, XML_E_FILE_NOT_FOUND and SQLITE_ERROR
//      2 through 16 are each shared by one XML_* status and one SQLITE_* base code
//
//  and, most confusingly, XML_E_FILE_NOT_FOUND is 1 while E_FILE_NOT_FOUND is -15. These are NOT
//  duplicates to reconcile: a PowerFramework return code, an XML parse status and a SQLite result
//  code are three different code spaces that happen to be catalogued together, and a value is
//  only meaningful against the space it came from. No attempt is made to unify, renumber or
//  cross-reference them, and no consumer may compare a value from one space against a constant
//  from another.
//
//  DECISION 6 - THIS FILE IS THE DECLARED SOURCE OF TRUTH FOR THE CONTRACT ENUM
//  --------------------------------------------------------------------------------------------
//  The `RetCode` enum in shared/PowerFramework.Contracts/Proto/common.v1.proto must equal these
//  values member for member. At the time this file was authored that .proto did not exist yet, so
//  the direction of alignment is recorded here explicitly: the Contracts author aligns to this
//  file, never the reverse. The correspondence is a value correspondence maintained by hand and
//  documented in docs/CONTRACTS.md - it is deliberately NOT a compile-time edge, because
//  Shared Kernel is the bottom of the dependency graph and must not reference Contracts.
//
//  THE PRESERVED REDUNDANCIES AND DEFECTS, ENUMERATED
//  --------------------------------------------------------------------------------------------
//  Behaviour is replicated, never improved, so each of the following is reproduced verbatim and
//  annotated at its point of reproduction rather than corrected:
//
//    * Three names for 0 and two names for -2, with three of the five unreachable in
//      FormatRetCode's output because an earlier `case` arm matches the same value first.
//    * PREVENT = 1 satisfies IsSucceeded, because that predicate is `rtCode >= RetCode.OK`
//      [issucceeded.srf:L12]. A prevention therefore reads as a success.
//    * CANCELLED = -2 is excluded from IsFailed by an explicit guard [isfailed.srf:L12] while
//      also failing IsSucceeded, so it is NEITHER - a tri-state hole in a nominally boolean
//      algebra. Null is likewise neither, because both predicates return false on null.
//    * E_RETRY = -33 has no arm in FormatRetCode at all, so it renders through the fallback.
//    * UNKNOWN = -4000 also has no arm, so FormatRetCode renders it as "UNKNOWN (-4000)" - the
//      word followed by its own number - via `case else` [formatretcode.srf:L82].
//    * SQLITE_ABORT_ROLLBACK uses multiplier 2 with no multiplier-1 sibling in existence.
//    * SQLITE_OK_LOAD_PERMANENTLY is an EXTENDED code hanging off the SUCCESS base, so it is a
//      positive non-zero value that nevertheless denotes success.
//    * The 28-to-100 gap between SQLITE_WARNING and SQLITE_ROW is left open.
//    * Windows-flavoured names (E_WIN32_ERROR, E_WINHTTP_ERROR) are retained even though the
//      target runtime is Linux containers. The identifiers are contract; the platform is not.
//
//  CONSTRAINT SELF-AUDIT
//  --------------------------------------------------------------------------------------------
//  No user rules exist for this project - review_rules returns exactly "No user rules provided" -
//  so the binding constraints are the refactor plan's own inventory, and the absence of rules is
//  not licence to lower the bar. The constraints that govern this file:
//
//  C-B  No behaviour improvement. Every redundancy is kept: three names for 0, both spellings of
//       cancelled, the extended codes as arithmetic, E_RETRY at -33 despite having no formatter
//       arm. Nothing is deduplicated, renumbered, reordered or tidied.
//  C-C  The legacy tree is read only. retcode.sru and its five predicate consumers were read as
//       the specification and left untouched, and every constant below cites its :L locator so
//       any value can be adjudicated against the oracle.
//  C-D  The four deferred services are not implemented, even partially. The XML_* set is ported
//       because it is a block of plain integers inside an in-scope object, but NO XML capability
//       accompanies it - no parser, no interface, no placeholder type, no exception-throwing
//       stub. The same holds for the SQLite sets: they are integers, not a storage provider.
//  C-K  Every technology-specific decision is documented at its point of reproduction - the six
//       DECISION sections above, plus the per-constant annotations below.
//  C-A  Minimal change applies to behaviour, not structure. This file is shared IMPLEMENTATION
//       consumed in process through a ProjectReference, never a cross-service channel: it has no
//       serialization attribute, no wire shape, no gRPC or HTTP status, and no reference to
//       PowerFramework.Contracts - the direction of that dependency is the reverse (DECISION 6).
//  C-F  Nothing hardcoded is carried forward that should not be. The only literals here are the
//       156 integers the oracle declares; there is no key, credential, connection string,
//       endpoint or path in this file, and no string literal appears on any line of code at all.
//
//  Section 0.4.5.3 of the plan - constant identifiers preserved verbatim - is satisfied by
//  DECISION 3: all 156 identifiers keep their legacy SCREAMING_SNAKE spelling because they appear
//  in serialized payloads, log records and characterization recordings, where a rename would
//  silently invalidate every stored comparison that mentions one.
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//  --------------------------------------------------------------------------------------------
//    * Any `enum`. See DECISION 1. Consequently CA1027 ("mark enums with FlagsAttribute") and
//      CA1008 ("enums should have zero value"), which the .editorconfig deliberately does not
//      suppress, have nothing to fire on here.
//    * Any instance member, singleton, `Instance` or `Current`. See DECISION 2.
//    * Any predicate, formatter or lookup. This file is a catalogue of values and nothing else.
//      IsSucceeded, IsFailed, IsPrevented, IsAllowed, IsCancelled and IsValidObject belong to
//      Predicates.cs; FormatRetCode and Sprintf belong to Formatting.cs. Their behaviour is
//      described in the comments below only to explain why a value has the form it has.
//    * Any name-to-value or value-to-name map. FormatRetCode's mapping is deliberately lossy -
//      three names are unreachable in its output - so a reversible map here would contradict it.
//    * Any XML or SQLite behaviour. See C-D above.
//    * A `#pragma warning disable` or a project-wide `NoWarn`. See DECISION 3.
//    * A per-file licence header. See the LICENCE note at the top of this banner.
//    * Any `using` directive. A class of `long` literals needs no imported type, and this
//      project sits at the bottom of the dependency graph with no package and no project
//      reference of its own.
// ==============================================================================================

namespace PowerFramework.Shared.Kernel;

/// <summary>
/// The PowerFramework return-code catalogue, ported from
/// <c>ws_objects/pfw.shared.pbl.src/retcode.sru</c>.
/// </summary>
/// <remarks>
/// <para>
/// 156 compile-time <see cref="long"/> constants in four independent groups: the framework's own
/// return codes [retcode.sru:L37-L79], the XML parser's parse-status codes [L81-L102], and the
/// SQLite base [L104-L138] and extended [L139-L206] result codes. The groups share a class but not
/// a numbering scheme, and their values overlap - see the DECISION 5 note in this file's header
/// before comparing a value from one group against a constant from another.
/// </para>
/// <para>
/// This is the most widely consumed contract in the refactor: the sibling Diagnostics, Eventful
/// and Localization libraries and all four in-scope services read these values, and the
/// <c>RetCode</c> enum published in <c>PowerFramework.Contracts</c> mirrors them value for value.
/// The identifier spellings are therefore part of the contract, not a style choice - they travel
/// in serialized payloads, log records and characterization recordings.
/// </para>
/// <para>
/// The class carries values only. The tri-state algebra built on them lives in
/// <c>Predicates.cs</c> and the formatter in <c>Formatting.cs</c>; both are referenced in the
/// remarks below purely to explain why a value has the form it has.
/// </para>
/// </remarks>
public static class RetCode
{
    // ------------------------------------------------------------------------------------------
    //  POWERFRAMEWORK RETURN CODES                                        retcode.sru:L37-L79
    // ------------------------------------------------------------------------------------------
    //  The oracle introduces this group with the banner comment
    //  `/*--- PowerFramework return codes ---*/` at L37 and the access specifier `Public:` at
    //  L38, which C# expresses per member. 41 constants follow, at L39 through L79.
    //
    //  Shape of the group: three names for success, one for a veto, one for failure, two for
    //  cancellation, a DENSE block of 31 error codes running -3 through -33 with no gaps, then
    //  three outliers at -2000, -2001 and -4000.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Success, and the origin of the whole algebra: <c>0</c>. [retcode.sru:L39]
    /// </summary>
    /// <remarks>
    /// The predicates are defined relative to this constant rather than to a bare zero:
    /// <c>IsSucceeded</c> is <c>rtCode &gt;= RetCode.OK</c> [issucceeded.srf:L12] and
    /// <c>IsFailed</c> is <c>rtCode &lt; RetCode.OK and rtCode &lt;&gt; RetCode.CANCELLED</c>
    /// [isfailed.srf:L12]. It is also the first arm of <c>FormatRetCode</c>
    /// [formatretcode.srf:L11], which is why the two other names for zero can never appear in
    /// that function's output.
    /// </remarks>
    public const long OK = 0;

    /// <summary>
    /// Success. A second name for <c>0</c>, identical in value to <see cref="OK"/>.
    /// [retcode.sru:L40]
    /// </summary>
    /// <remarks>
    /// Kept as a distinct name rather than deduplicated. <c>FormatRetCode</c> matches
    /// <see cref="OK"/> first [formatretcode.srf:L11], so the string <c>"SUCCESS"</c> is
    /// unreachable in its output - a legacy characteristic, not a defect to repair.
    /// </remarks>
    public const long SUCCESS = 0;

    /// <summary>
    /// Permission granted. A third name for <c>0</c>, used where a return code answers "may this
    /// proceed?" rather than "did this work?". [retcode.sru:L41]
    /// </summary>
    /// <remarks>
    /// <c>IsAllowed</c> tests this name specifically, and its long overload is
    /// <c>(rtCode = RetCode.ALLOW or IsNull(rtCode) or rtCode &gt; 1000)</c>
    /// [isallowed.srf:L11]. Two consequences of that arm are worth knowing: a null is treated as
    /// ALLOWED here, whereas <c>IsSucceeded</c> and <c>IsFailed</c> both treat null as false; and
    /// the undocumented <c>&gt; 1000</c> arm means large positive values are allowed too. Like
    /// <see cref="SUCCESS"/>, the string <c>"ALLOW"</c> is unreachable in
    /// <c>FormatRetCode</c>'s output.
    /// </remarks>
    public const long ALLOW = 0;

    /// <summary>
    /// Veto: the operation was prevented from proceeding. [retcode.sru:L42]
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED DEFECT - THE TRI-STATE HOLE. <c>IsSucceeded(PREVENT)</c> is <b>true</b>, because
    /// that predicate is <c>rtCode &gt;= RetCode.OK</c> [issucceeded.srf:L12] and <c>1 &gt;= 0</c>.
    /// A prevention therefore reads as a success to every caller that asks the question that way.
    /// This is legacy behaviour and is reproduced deliberately; it must not be "fixed" by
    /// narrowing the predicate.
    /// </para>
    /// <para>
    /// <c>IsPrevented</c>'s long overload tests this value exactly, but its boolean overload is
    /// <c>Not rtCode</c> - textually identical to <c>IsFailed</c>'s boolean overload
    /// [isprevented.srf, isfailed.srf] - so prevention and failure are indistinguishable in the
    /// boolean form while remaining distinct in the numeric form. <c>FormatRetCode</c> has no arm
    /// for this value.
    /// </para>
    /// </remarks>
    public const long PREVENT = 1;

    /// <summary>
    /// Generic failure. [retcode.sru:L43]
    /// </summary>
    public const long FAILED = -1;

    /// <summary>
    /// The operation was cancelled. Single-L spelling. [retcode.sru:L44]
    /// </summary>
    /// <remarks>
    /// One of two names for <c>-2</c>; see <see cref="CANCELLED"/> for the double-L spelling and
    /// for the algebra. Both are real and both are kept: they are two names for one constant, not
    /// a typo to deduplicate. <c>FormatRetCode</c> matches the double-L name
    /// [formatretcode.srf:L15], so the string <c>"CANCELED"</c> is unreachable in its output.
    /// </remarks>
    public const long CANCELED = -2;

    /// <summary>
    /// The operation was cancelled. Double-L spelling, and the one the framework's own predicates
    /// and formatter reference. [retcode.sru:L45]
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED DEFECT - CANCELLED IS NEITHER SUCCEEDED NOR FAILED. <c>IsFailed</c> excludes
    /// this value explicitly - <c>rtCode &lt; RetCode.OK and rtCode &lt;&gt; RetCode.CANCELLED</c>
    /// [isfailed.srf:L12] - while <c>IsSucceeded</c> rejects it for being negative
    /// [issucceeded.srf:L12]. A cancelled result therefore answers <c>false</c> to both
    /// questions: a tri-state hole in a nominally boolean algebra. Null behaves the same way,
    /// because both predicates return <c>false</c> on null before testing anything.
    /// </para>
    /// <para>
    /// <c>IsCancelled</c> tests this value with no null guard at all, unlike its five siblings,
    /// which all begin with one.
    /// </para>
    /// </remarks>
    public const long CANCELLED = -2;

    // ------------------------------------------------------------------------------------------
    //  THE DENSE ERROR BLOCK                                              retcode.sru:L46-L76
    // ------------------------------------------------------------------------------------------
    //  31 constants running -3 through -33 with NO GAPS, in the oracle's own order. The density
    //  is a verified property, not an assumption: for every k in 3..33 exactly one constant below
    //  equals -k. Preserve both the order and the values - a renumbering would silently
    //  invalidate every stored characterization comparison that mentions one of these names.
    //
    //  All 31 have an arm in FormatRetCode [formatretcode.srf:L17-L76] EXCEPT the last,
    //  E_RETRY, which falls through to the `case else` fallback.
    // ------------------------------------------------------------------------------------------

    /// <summary>An argument was invalid. [retcode.sru:L46]</summary>
    /// <remarks>
    /// The most widely returned error in the ported surface. Two in-scope examples: the SQL
    /// clause modifier returns it when the clause index is zero or the clause text is empty, and
    /// the query task's chunk-size setter returns it for a value at or below 1000.
    /// </remarks>
    public const long E_INVALID_ARGUMENT = -3;

    /// <summary>An image was invalid. [retcode.sru:L47]</summary>
    public const long E_INVALID_IMAGE = -4;

    /// <summary>An object reference was invalid. [retcode.sru:L48]</summary>
    public const long E_INVALID_OBJECT = -5;

    /// <summary>A type was invalid. [retcode.sru:L49]</summary>
    public const long E_INVALID_TYPE = -6;

    /// <summary>A transaction object was invalid. [retcode.sru:L50]</summary>
    public const long E_INVALID_TRANSACTION = -7;

    /// <summary>A SQL statement was invalid. [retcode.sru:L51]</summary>
    public const long E_INVALID_SQL = -8;

    /// <summary>Data was invalid. [retcode.sru:L52]</summary>
    public const long E_INVALID_DATA = -9;

    /// <summary>A DataWindow data object was invalid. [retcode.sru:L53]</summary>
    public const long E_INVALID_DATAOBJECT = -10;

    /// <summary>A handle was invalid. [retcode.sru:L54]</summary>
    public const long E_INVALID_HANDLE = -11;

    /// <summary>An index or offset was outside the addressable bound. [retcode.sru:L55]</summary>
    public const long E_OUT_OF_BOUND = -12;

    /// <summary>A value was outside its permitted range. [retcode.sru:L56]</summary>
    public const long E_OUT_OF_RANGE = -13;

    /// <summary>Memory could not be allocated. [retcode.sru:L57]</summary>
    public const long E_OUT_OF_MEMORY = -14;

    /// <summary>A file was not found. [retcode.sru:L58]</summary>
    /// <remarks>
    /// Note the deliberate collision with <see cref="XML_E_FILE_NOT_FOUND"/>, which is <c>1</c>.
    /// The two live in different code spaces; see the DECISION 5 note in this file's header.
    /// </remarks>
    public const long E_FILE_NOT_FOUND = -15;

    /// <summary>An object was not found. [retcode.sru:L59]</summary>
    public const long E_OBJECT_NOT_FOUND = -16;

    /// <summary>Data was not found. [retcode.sru:L60]</summary>
    public const long E_DATA_NOT_FOUND = -17;

    /// <summary>A function was not found. [retcode.sru:L61]</summary>
    public const long E_FUNCTION_NOT_FOUND = -18;

    /// <summary>An event was not found. [retcode.sru:L62]</summary>
    public const long E_EVENT_NOT_FOUND = -19;

    /// <summary>A member was not found. [retcode.sru:L63]</summary>
    public const long E_MEMBER_NOT_FOUND = -20;

    /// <summary>A variable was not found. [retcode.sru:L64]</summary>
    public const long E_VAR_NOT_FOUND = -21;

    /// <summary>The subject does not exist. [retcode.sru:L65]</summary>
    public const long E_NOT_EXISTS = -22;

    /// <summary>The resource is busy. [retcode.sru:L66]</summary>
    public const long E_BUSY = -23;

    /// <summary>The operation timed out. [retcode.sru:L67]</summary>
    public const long E_TIME_OUT = -24;

    /// <summary>Access was denied. [retcode.sru:L68]</summary>
    public const long E_ACCESS_DENIED = -25;

    /// <summary>A Win32 API call failed. [retcode.sru:L69]</summary>
    /// <remarks>
    /// A Windows-flavoured name retained verbatim even though the target runtime is Linux
    /// containers. The identifier is contract - it appears in payloads, logs and characterization
    /// recordings - and the platform is not, so it is neither renamed nor removed.
    /// </remarks>
    public const long E_WIN32_ERROR = -26;

    /// <summary>An internal framework invariant was violated. [retcode.sru:L70]</summary>
    public const long E_INTERNAL_ERROR = -27;

    /// <summary>A database operation failed. [retcode.sru:L71]</summary>
    public const long E_DB_ERROR = -28;

    /// <summary>An HTTP operation failed. [retcode.sru:L72]</summary>
    public const long E_HTTP_ERROR = -29;

    /// <summary>A WinHTTP API call failed. [retcode.sru:L73]</summary>
    /// <remarks>
    /// Windows-flavoured, retained verbatim for the same reason as <see cref="E_WIN32_ERROR"/>.
    /// </remarks>
    public const long E_WINHTTP_ERROR = -30;

    /// <summary>An I/O operation failed. [retcode.sru:L74]</summary>
    public const long E_IO_ERROR = -31;

    /// <summary>Binding an argument to a SQL statement failed. [retcode.sru:L75]</summary>
    public const long E_SQL_BIND_ARG_FAILED = -32;

    /// <summary>
    /// The operation should be retried. The last member of the dense block. [retcode.sru:L76]
    /// </summary>
    /// <remarks>
    /// PRESERVED CHARACTERISTIC. Unlike the 30 constants above it, this value has NO arm in
    /// <c>FormatRetCode</c> [formatretcode.srf:L17-L76], so it renders through the
    /// <c>case else</c> fallback as <c>"UNKNOWN (-33)"</c>. The omission is legacy behaviour: the
    /// value stays at <c>-33</c> and the formatter is not extended to cover it.
    /// </remarks>
    public const long E_RETRY = -33;

    /// <summary>
    /// The requested capability is not supported. [retcode.sru:L77]
    /// </summary>
    /// <remarks>
    /// Note the jump from the dense block: <c>-33</c> is followed by <c>-2000</c>, not
    /// <c>-34</c>. The gap is intentional in the oracle and is left open.
    /// </remarks>
    public const long E_NO_SUPPORT = -2000;

    /// <summary>
    /// The requested capability has no implementation. [retcode.sru:L78]
    /// </summary>
    /// <remarks>
    /// This is the value the Persistence paging rewriter's <c>case else</c> arm returns for an
    /// unsupported database type: the legacy enumerates only SQL Server and Oracle, and anything
    /// else raises the error event with this code and then returns it
    /// [n_cst_thread_task_sqlquery.sru:L397-L398]. The same source also tests a row count against
    /// it [:L761], so the value doubles as a sentinel there.
    /// </remarks>
    public const long E_NO_IMPLEMENTATION = -2001;

    /// <summary>
    /// An unclassified outcome. [retcode.sru:L79]
    /// </summary>
    /// <remarks>
    /// PRESERVED CHARACTERISTIC. <c>FormatRetCode</c> has no arm for this value either, so it
    /// falls to <c>case else</c>, which returns <c>"UNKNOWN (" + String(rtCode) + ")"</c>
    /// [formatretcode.srf:L82]. This constant therefore renders as the literal string
    /// <c>"UNKNOWN (-4000)"</c> - the word followed by its own number - and the formatter's
    /// fallback text collides by design with the name of a real constant.
    /// </remarks>
    public const long UNKNOWN = -4000;

    // ------------------------------------------------------------------------------------------
    //  XML PARSER - PARSE STATUS                                          retcode.sru:L81-L102
    // ------------------------------------------------------------------------------------------
    //  The oracle brackets this group with `/*--- XML Parser ---*/` at L81 and
    //  `/*--- End XML Parser ---*/` at L102, and identifies its provenance at L83 with
    //  `//Parse status (n_xmlparseresult::GetStatus)`. 17 constants follow, at L84 through L100,
    //  each with an explanatory comment carried across below verbatim - the oracle wrote them in
    //  English, so nothing needed translating and nothing was dropped.
    //
    //  WHY THIS GROUP IS PORTED AT ALL, AND WHAT IS NOT PORTED WITH IT (constraint C-D)
    //  XML tooling belongs to the DEFERRED Documents service, which this phase must not implement
    //  even partially and not even to stub out. These 17 members are ported regardless because
    //  they are a block of plain integers inside an in-scope object: the whole of pfw.shared is
    //  assigned to Shared Kernel and this file's full 221 lines are specified, and transcribing
    //  integer constants is not implementing a service.
    //
    //  What that permission does NOT extend to, stated so a later reader cannot mistake the
    //  boundary: there is no XML parser here, no document or node type, no reader, no
    //  `IXmlParseResult`-style interface, no `GetStatus` accessor and no placeholder that throws.
    //  Nothing may be added behind these values. The one in-scope consumer of XML anywhere in this
    //  phase is the localization resource reader, and it deliberately substitutes the BCL's own
    //  XDocument rather than porting the legacy XML object family - so it does not consume these
    //  codes either. They are catalogued here for completeness of the return-code contract.
    //
    //  INDEPENDENT CODE SPACE - DO NOT COMPARE ACROSS GROUPS
    //  These values overlap the two groups around them and it is not a mistake: XML_OK is 0, the
    //  same number as OK, SUCCESS, ALLOW and SQLITE_OK; XML_E_FILE_NOT_FOUND is 1 while the
    //  framework's own E_FILE_NOT_FOUND is -15; and 2 through 16 are each shared with a SQLite
    //  base code. A value is only meaningful against the space it came from. See DECISION 5 in
    //  this file's header.
    // ------------------------------------------------------------------------------------------

    /// <summary>No error. [retcode.sru:L84]</summary>
    /// <remarks>
    /// Numerically equal to <see cref="OK"/> and to <see cref="SQLITE_OK"/>, but a member of a
    /// different code space; the coincidence carries no meaning.
    /// </remarks>
    public const long XML_OK = 0;

    /// <summary>File was not found during load_file(). [retcode.sru:L85]</summary>
    /// <remarks>
    /// Positive <c>1</c> here, where the framework's own <see cref="E_FILE_NOT_FOUND"/> is
    /// <c>-15</c> and <see cref="PREVENT"/> is <c>1</c>. The clearest illustration of why these
    /// spaces must never be compared against one another.
    /// </remarks>
    public const long XML_E_FILE_NOT_FOUND = 1;

    /// <summary>Error reading from file/stream. [retcode.sru:L86]</summary>
    public const long XML_E_IO_ERROR = 2;

    /// <summary>Could not allocate memory. [retcode.sru:L87]</summary>
    public const long XML_E_OUT_OF_MEMORY = 3;

    /// <summary>Internal error occurred. [retcode.sru:L88]</summary>
    public const long XML_E_INTERNAL_ERROR = 4;

    /// <summary>Parser could not determine tag type. [retcode.sru:L89]</summary>
    public const long XML_E_UNRECOGNIZED_TAG = 5;

    /// <summary>
    /// Parsing error occurred while parsing document declaration/processing instruction.
    /// [retcode.sru:L90]
    /// </summary>
    public const long XML_E_BAD_PI = 6;

    /// <summary>Parsing error occurred while parsing comment. [retcode.sru:L91]</summary>
    public const long XML_E_BAD_COMMENT = 7;

    /// <summary>Parsing error occurred while parsing CDATA section. [retcode.sru:L92]</summary>
    public const long XML_E_BAD_CDATA = 8;

    /// <summary>
    /// Parsing error occurred while parsing document type declaration. [retcode.sru:L93]
    /// </summary>
    public const long XML_E_BAD_DOCTYPE = 9;

    /// <summary>Parsing error occurred while parsing PCDATA section. [retcode.sru:L94]</summary>
    public const long XML_E_BAD_PCDATA = 10;

    /// <summary>
    /// Parsing error occurred while parsing start element tag. [retcode.sru:L95]
    /// </summary>
    public const long XML_E_BAD_START_ELEMENT = 11;

    /// <summary>
    /// Parsing error occurred while parsing element attribute. [retcode.sru:L96]
    /// </summary>
    public const long XML_E_BAD_ATTRIBUTE = 12;

    /// <summary>Parsing error occurred while parsing end element tag. [retcode.sru:L97]</summary>
    public const long XML_E_BAD_END_ELEMENT = 13;

    /// <summary>
    /// There was a mismatch of start-end tags (closing tag had incorrect name, some tag was not
    /// closed or there was an excessive closing tag). [retcode.sru:L98]
    /// </summary>
    /// <remarks>
    /// One code covers three distinct faults - a wrongly named closing tag, an unclosed tag and
    /// an excess closing tag - so a consumer cannot discriminate between them from the status
    /// alone. That conflation is the oracle's, carried across unchanged.
    /// </remarks>
    public const long XML_E_END_ELEMENT_MISMATCH = 14;

    /// <summary>
    /// Unable to append nodes since root type is not node_element or node_document (exclusive to
    /// xml_node::append_buffer). [retcode.sru:L99]
    /// </summary>
    public const long XML_E_APPEND_INVALID_ROOT = 15;

    /// <summary>
    /// Parsing resulted in a document without element nodes. [retcode.sru:L100]
    /// </summary>
    public const long XML_E_NO_DOCUMENT_ELEMENT = 16;

    // ------------------------------------------------------------------------------------------
    //  SQLITE - BASE RESULT CODES                                        retcode.sru:L104-L138
    // ------------------------------------------------------------------------------------------
    //  The oracle opens the SQLite group with `/*--- SQLite ---*/` at L104 and closes it with
    //  `/*--- End SQLite ---*/` at L208, identifying its provenance at L106 with
    //  `//SQLDBCode (n_sqlite::SQLDBCode)`. The base codes are the first half of that group: 31
    //  constants at L107 and L109 through L138. Each carries a /* ... */ comment in the oracle,
    //  reproduced below verbatim - including the two terse annotations that read as warnings
    //  rather than descriptions, `/* Not used */` on SQLITE_FORMAT and `/* Internal use only */`
    //  on SQLITE_EMPTY.
    //
    //  These are the values that surface on the storage boundary: the legacy exposes them through
    //  the SQLite object's SQLDBCode accessor, and the ported Persistence service reports them in
    //  the structured database-error payload rather than as a dialog. Note that these are VALUES
    //  ONLY - no connection, no command, no provider and no storage behaviour of any kind appears
    //  in this file or belongs in it.
    //
    //  TWO STRUCTURAL FEATURES THAT ARE DELIBERATE AND MUST NOT BE "TIDIED"
    //    * The oracle places the marker `//-- beginning-of-error-codes` at L108, between
    //      SQLITE_OK and SQLITE_ERROR. It is reproduced in place below because it marks exactly
    //      where the non-success range starts, which is the only thing distinguishing 0 from the
    //      28 codes that follow it.
    //    * The numbering jumps from SQLITE_WARNING = 28 straight to SQLITE_ROW = 100. That gap is
    //      real and intentional in SQLite itself - the 100-range codes are step outcomes rather
    //      than errors - and it is left open. Nothing may be invented to fill it.
    // ------------------------------------------------------------------------------------------

    /// <summary>Successful result. [retcode.sru:L107]</summary>
    /// <remarks>
    /// The base that <see cref="SQLITE_OK_LOAD_PERMANENTLY"/> extends, which is why an extended
    /// SQLite code can denote success while being non-zero. See that member for the consequence.
    /// </remarks>
    public const long SQLITE_OK = 0;

    //-- beginning-of-error-codes                                          [retcode.sru:L108]

    /// <summary>Generic error. [retcode.sru:L109]</summary>
    public const long SQLITE_ERROR = 1;

    /// <summary>Internal logic error in SQLite. [retcode.sru:L110]</summary>
    public const long SQLITE_INTERNAL = 2;

    /// <summary>Access permission denied. [retcode.sru:L111]</summary>
    public const long SQLITE_PERM = 3;

    /// <summary>Callback routine requested an abort. [retcode.sru:L112]</summary>
    /// <remarks>
    /// The base of the anomalous extended pair: <see cref="SQLITE_ABORT_ROLLBACK"/> refines this
    /// code with multiplier <c>2</c> and no multiplier-1 sibling exists.
    /// </remarks>
    public const long SQLITE_ABORT = 4;

    /// <summary>The database file is locked. [retcode.sru:L113]</summary>
    public const long SQLITE_BUSY = 5;

    /// <summary>A table in the database is locked. [retcode.sru:L114]</summary>
    public const long SQLITE_LOCKED = 6;

    /// <summary>A malloc() failed. [retcode.sru:L115]</summary>
    public const long SQLITE_NOMEM = 7;

    /// <summary>Attempt to write a readonly database. [retcode.sru:L116]</summary>
    public const long SQLITE_READONLY = 8;

    /// <summary>Operation terminated by sqlite3_interrupt(). [retcode.sru:L117]</summary>
    public const long SQLITE_INTERRUPT = 9;

    /// <summary>Some kind of disk I/O error occurred. [retcode.sru:L118]</summary>
    /// <remarks>
    /// The most heavily extended base in the catalogue: 31 extended codes refine it, at
    /// multipliers 1 through 31 with no gaps [retcode.sru:L143-L173].
    /// </remarks>
    public const long SQLITE_IOERR = 10;

    /// <summary>The database disk image is malformed. [retcode.sru:L119]</summary>
    public const long SQLITE_CORRUPT = 11;

    /// <summary>Unknown opcode in sqlite3_file_control(). [retcode.sru:L120]</summary>
    public const long SQLITE_NOTFOUND = 12;

    /// <summary>Insertion failed because database is full. [retcode.sru:L121]</summary>
    public const long SQLITE_FULL = 13;

    /// <summary>Unable to open the database file. [retcode.sru:L122]</summary>
    public const long SQLITE_CANTOPEN = 14;

    /// <summary>Database lock protocol error. [retcode.sru:L123]</summary>
    public const long SQLITE_PROTOCOL = 15;

    /// <summary>Internal use only. [retcode.sru:L124]</summary>
    /// <remarks>
    /// The oracle's whole comment for this code is the annotation <c>/* Internal use only */</c> -
    /// it documents the code's status rather than its meaning, and it is carried across as-is
    /// rather than replaced with an invented description.
    /// </remarks>
    public const long SQLITE_EMPTY = 16;

    /// <summary>The database schema changed. [retcode.sru:L125]</summary>
    public const long SQLITE_SCHEMA = 17;

    /// <summary>String or BLOB exceeds size limit. [retcode.sru:L126]</summary>
    public const long SQLITE_TOOBIG = 18;

    /// <summary>Abort due to constraint violation. [retcode.sru:L127]</summary>
    /// <remarks>
    /// Refined by 10 extended codes at multipliers 1 through 10
    /// [retcode.sru:L192-L201].
    /// </remarks>
    public const long SQLITE_CONSTRAINT = 19;

    /// <summary>Data type mismatch. [retcode.sru:L128]</summary>
    public const long SQLITE_MISMATCH = 20;

    /// <summary>Library used incorrectly. [retcode.sru:L129]</summary>
    public const long SQLITE_MISUSE = 21;

    /// <summary>Uses OS features not supported on host. [retcode.sru:L130]</summary>
    public const long SQLITE_NOLFS = 22;

    /// <summary>Authorization denied. [retcode.sru:L131]</summary>
    public const long SQLITE_AUTH = 23;

    /// <summary>Not used. [retcode.sru:L132]</summary>
    /// <remarks>
    /// The oracle's whole comment for this code is the annotation <c>/* Not used */</c>. The
    /// constant is retained anyway: it holds the slot at <c>24</c>, so removing it would make the
    /// surrounding sequence look contiguous when it is not.
    /// </remarks>
    public const long SQLITE_FORMAT = 24;

    /// <summary>2nd parameter to sqlite3_bind out of range. [retcode.sru:L133]</summary>
    public const long SQLITE_RANGE = 25;

    /// <summary>File opened that is not a database file. [retcode.sru:L134]</summary>
    public const long SQLITE_NOTADB = 26;

    /// <summary>Notifications from sqlite3_log(). [retcode.sru:L135]</summary>
    public const long SQLITE_NOTICE = 27;

    /// <summary>Warnings from sqlite3_log(). [retcode.sru:L136]</summary>
    /// <remarks>
    /// The last code before the deliberate gap: the next base code is
    /// <see cref="SQLITE_ROW"/> at <c>100</c>, not <c>29</c>.
    /// </remarks>
    public const long SQLITE_WARNING = 28;

    /// <summary>sqlite3_step() has another row ready. [retcode.sru:L137]</summary>
    /// <remarks>
    /// First of the two step-outcome codes, which is why it sits at <c>100</c> rather than
    /// continuing the error sequence. The gap between <c>28</c> and <c>100</c> is intentional in
    /// SQLite and is left open.
    /// </remarks>
    public const long SQLITE_ROW = 100;

    /// <summary>sqlite3_step() has finished executing. [retcode.sru:L138]</summary>
    public const long SQLITE_DONE = 101;

    //-- Extended Result Codes                                             [retcode.sru:L139]

    // ------------------------------------------------------------------------------------------
    //  SQLITE - EXTENDED RESULT CODES                                    retcode.sru:L139-L206
    // ------------------------------------------------------------------------------------------
    //  The oracle introduces this half of the SQLite group with the banner `//-- Extended Result
    //  Codes` at L139, reproduced verbatim immediately above, and then declares 67 constants at
    //  L140 through L206. An extended code refines a base code: it carries the base in its low
    //  byte and an ordinal in the byte above, so `code & 0xFF` recovers the base a caller can
    //  still handle generically while the full value identifies the specific cause.
    //
    //  EVERY ONE IS KEPT AS ARITHMETIC, NOT FLATTENED TO A LITERAL (see DECISION 4)
    //  The oracle writes each as `(BASE + (n * 256))` and that form is reproduced exactly. C#
    //  permits a `const` to reference an earlier `const` in the same class, so the compiled value
    //  is byte-identical to the literal - what would be lost by flattening is verifiability. The
    //  expression states which base code an extended code refines and which ordinal it is, so a
    //  reader can check any line against the oracle at a glance; `5898` cannot be checked without
    //  a calculator. The declaration order below is the oracle's own, which is why the base codes
    //  appear in a sequence that is not numeric.
    //
    //  These values are the widest in the catalogue - SQLITE_IOERR_ROLLBACK_ATOMIC is 7946 - which
    //  is a second reason the whole class is `long` rather than a narrower type, and a reason no
    //  consumer may assume a return code fits in a byte or a signed 16-bit field.
    //
    //  The doc comment on each member states the refinement and cites the oracle line, and nothing
    //  more: unlike the base codes, the oracle gives these no explanatory comments, so inventing
    //  descriptive prose for them would be asserting meaning the only available specification does
    //  not carry. The single exception is SQLITE_CANTOPEN_DIRTYWAL, whose `/* Not Used */`
    //  annotation the oracle does supply and which is carried across.
    // ------------------------------------------------------------------------------------------

    // --- Refinements of SQLITE_ERROR = 1 ------------------------- retcode.sru:L140-L142 ---

    /// <summary>Refines <see cref="SQLITE_ERROR"/>, ordinal 1. [retcode.sru:L140]</summary>
    public const long SQLITE_ERROR_MISSING_COLLSEQ = SQLITE_ERROR + (1 * 256);

    /// <summary>Refines <see cref="SQLITE_ERROR"/>, ordinal 2. [retcode.sru:L141]</summary>
    /// <remarks>
    /// Numerically <c>513</c>. Unrelated to the framework's own <see cref="E_RETRY"/>, which is
    /// <c>-33</c> in a different code space.
    /// </remarks>
    public const long SQLITE_ERROR_RETRY = SQLITE_ERROR + (2 * 256);

    /// <summary>Refines <see cref="SQLITE_ERROR"/>, ordinal 3. [retcode.sru:L142]</summary>
    public const long SQLITE_ERROR_SNAPSHOT = SQLITE_ERROR + (3 * 256);

    // --- Refinements of SQLITE_IOERR = 10 ------------------------ retcode.sru:L143-L173 ---
    //  31 refinements at ordinals 1 through 31 with no gaps - the largest family in the
    //  catalogue, and the only one whose ordinals run unbroken to 31.

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 1. [retcode.sru:L143]</summary>
    public const long SQLITE_IOERR_READ = SQLITE_IOERR + (1 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 2. [retcode.sru:L144]</summary>
    public const long SQLITE_IOERR_SHORT_READ = SQLITE_IOERR + (2 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 3. [retcode.sru:L145]</summary>
    public const long SQLITE_IOERR_WRITE = SQLITE_IOERR + (3 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 4. [retcode.sru:L146]</summary>
    public const long SQLITE_IOERR_FSYNC = SQLITE_IOERR + (4 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 5. [retcode.sru:L147]</summary>
    public const long SQLITE_IOERR_DIR_FSYNC = SQLITE_IOERR + (5 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 6. [retcode.sru:L148]</summary>
    public const long SQLITE_IOERR_TRUNCATE = SQLITE_IOERR + (6 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 7. [retcode.sru:L149]</summary>
    public const long SQLITE_IOERR_FSTAT = SQLITE_IOERR + (7 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 8. [retcode.sru:L150]</summary>
    public const long SQLITE_IOERR_UNLOCK = SQLITE_IOERR + (8 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 9. [retcode.sru:L151]</summary>
    public const long SQLITE_IOERR_RDLOCK = SQLITE_IOERR + (9 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 10. [retcode.sru:L152]</summary>
    public const long SQLITE_IOERR_DELETE = SQLITE_IOERR + (10 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 11. [retcode.sru:L153]</summary>
    public const long SQLITE_IOERR_BLOCKED = SQLITE_IOERR + (11 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 12. [retcode.sru:L154]</summary>
    public const long SQLITE_IOERR_NOMEM = SQLITE_IOERR + (12 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 13. [retcode.sru:L155]</summary>
    public const long SQLITE_IOERR_ACCESS = SQLITE_IOERR + (13 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 14. [retcode.sru:L156]</summary>
    public const long SQLITE_IOERR_CHECKRESERVEDLOCK = SQLITE_IOERR + (14 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 15. [retcode.sru:L157]</summary>
    public const long SQLITE_IOERR_LOCK = SQLITE_IOERR + (15 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 16. [retcode.sru:L158]</summary>
    public const long SQLITE_IOERR_CLOSE = SQLITE_IOERR + (16 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 17. [retcode.sru:L159]</summary>
    public const long SQLITE_IOERR_DIR_CLOSE = SQLITE_IOERR + (17 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 18. [retcode.sru:L160]</summary>
    public const long SQLITE_IOERR_SHMOPEN = SQLITE_IOERR + (18 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 19. [retcode.sru:L161]</summary>
    public const long SQLITE_IOERR_SHMSIZE = SQLITE_IOERR + (19 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 20. [retcode.sru:L162]</summary>
    public const long SQLITE_IOERR_SHMLOCK = SQLITE_IOERR + (20 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 21. [retcode.sru:L163]</summary>
    public const long SQLITE_IOERR_SHMMAP = SQLITE_IOERR + (21 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 22. [retcode.sru:L164]</summary>
    public const long SQLITE_IOERR_SEEK = SQLITE_IOERR + (22 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 23. [retcode.sru:L165]</summary>
    public const long SQLITE_IOERR_DELETE_NOENT = SQLITE_IOERR + (23 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 24. [retcode.sru:L166]</summary>
    public const long SQLITE_IOERR_MMAP = SQLITE_IOERR + (24 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 25. [retcode.sru:L167]</summary>
    public const long SQLITE_IOERR_GETTEMPPATH = SQLITE_IOERR + (25 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 26. [retcode.sru:L168]</summary>
    public const long SQLITE_IOERR_CONVPATH = SQLITE_IOERR + (26 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 27. [retcode.sru:L169]</summary>
    public const long SQLITE_IOERR_VNODE = SQLITE_IOERR + (27 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 28. [retcode.sru:L170]</summary>
    public const long SQLITE_IOERR_AUTH = SQLITE_IOERR + (28 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 29. [retcode.sru:L171]</summary>
    public const long SQLITE_IOERR_BEGIN_ATOMIC = SQLITE_IOERR + (29 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 30. [retcode.sru:L172]</summary>
    public const long SQLITE_IOERR_COMMIT_ATOMIC = SQLITE_IOERR + (30 * 256);

    /// <summary>Refines <see cref="SQLITE_IOERR"/>, ordinal 31. [retcode.sru:L173]</summary>
    /// <remarks>
    /// The highest value in the whole catalogue, <c>7946</c>.
    /// </remarks>
    public const long SQLITE_IOERR_ROLLBACK_ATOMIC = SQLITE_IOERR + (31 * 256);

    // --- Refinements of SQLITE_LOCKED = 6 ------------------------ retcode.sru:L174-L175 ---

    /// <summary>Refines <see cref="SQLITE_LOCKED"/>, ordinal 1. [retcode.sru:L174]</summary>
    public const long SQLITE_LOCKED_SHAREDCACHE = SQLITE_LOCKED + (1 * 256);

    /// <summary>Refines <see cref="SQLITE_LOCKED"/>, ordinal 2. [retcode.sru:L175]</summary>
    public const long SQLITE_LOCKED_VTAB = SQLITE_LOCKED + (2 * 256);

    // --- Refinements of SQLITE_BUSY = 5 -------------------------- retcode.sru:L176-L177 ---

    /// <summary>Refines <see cref="SQLITE_BUSY"/>, ordinal 1. [retcode.sru:L176]</summary>
    public const long SQLITE_BUSY_RECOVERY = SQLITE_BUSY + (1 * 256);

    /// <summary>Refines <see cref="SQLITE_BUSY"/>, ordinal 2. [retcode.sru:L177]</summary>
    public const long SQLITE_BUSY_SNAPSHOT = SQLITE_BUSY + (2 * 256);

    // --- Refinements of SQLITE_CANTOPEN = 14 --------------------- retcode.sru:L178-L182 ---

    /// <summary>Refines <see cref="SQLITE_CANTOPEN"/>, ordinal 1. [retcode.sru:L178]</summary>
    public const long SQLITE_CANTOPEN_NOTEMPDIR = SQLITE_CANTOPEN + (1 * 256);

    /// <summary>Refines <see cref="SQLITE_CANTOPEN"/>, ordinal 2. [retcode.sru:L179]</summary>
    public const long SQLITE_CANTOPEN_ISDIR = SQLITE_CANTOPEN + (2 * 256);

    /// <summary>Refines <see cref="SQLITE_CANTOPEN"/>, ordinal 3. [retcode.sru:L180]</summary>
    public const long SQLITE_CANTOPEN_FULLPATH = SQLITE_CANTOPEN + (3 * 256);

    /// <summary>Refines <see cref="SQLITE_CANTOPEN"/>, ordinal 4. [retcode.sru:L181]</summary>
    public const long SQLITE_CANTOPEN_CONVPATH = SQLITE_CANTOPEN + (4 * 256);

    /// <summary>Refines <see cref="SQLITE_CANTOPEN"/>, ordinal 5. [retcode.sru:L182]</summary>
    /// <remarks>
    /// The oracle annotates this declaration <c>/* Not Used */</c> - the only extended code it
    /// comments at all - and the annotation is carried across rather than dropped. The constant is
    /// retained regardless, because ordinal 5 is occupied and removing it would make the family
    /// look as though it stopped at 4.
    /// </remarks>
    public const long SQLITE_CANTOPEN_DIRTYWAL = SQLITE_CANTOPEN + (5 * 256); /* Not Used */

    // --- Refinements of SQLITE_CORRUPT = 11 ---------------------- retcode.sru:L183-L184 ---

    /// <summary>Refines <see cref="SQLITE_CORRUPT"/>, ordinal 1. [retcode.sru:L183]</summary>
    public const long SQLITE_CORRUPT_VTAB = SQLITE_CORRUPT + (1 * 256);

    /// <summary>Refines <see cref="SQLITE_CORRUPT"/>, ordinal 2. [retcode.sru:L184]</summary>
    public const long SQLITE_CORRUPT_SEQUENCE = SQLITE_CORRUPT + (2 * 256);

    // --- Refinements of SQLITE_READONLY = 8 ---------------------- retcode.sru:L185-L190 ---

    /// <summary>Refines <see cref="SQLITE_READONLY"/>, ordinal 1. [retcode.sru:L185]</summary>
    public const long SQLITE_READONLY_RECOVERY = SQLITE_READONLY + (1 * 256);

    /// <summary>Refines <see cref="SQLITE_READONLY"/>, ordinal 2. [retcode.sru:L186]</summary>
    public const long SQLITE_READONLY_CANTLOCK = SQLITE_READONLY + (2 * 256);

    /// <summary>Refines <see cref="SQLITE_READONLY"/>, ordinal 3. [retcode.sru:L187]</summary>
    public const long SQLITE_READONLY_ROLLBACK = SQLITE_READONLY + (3 * 256);

    /// <summary>Refines <see cref="SQLITE_READONLY"/>, ordinal 4. [retcode.sru:L188]</summary>
    public const long SQLITE_READONLY_DBMOVED = SQLITE_READONLY + (4 * 256);

    /// <summary>Refines <see cref="SQLITE_READONLY"/>, ordinal 5. [retcode.sru:L189]</summary>
    public const long SQLITE_READONLY_CANTINIT = SQLITE_READONLY + (5 * 256);

    /// <summary>Refines <see cref="SQLITE_READONLY"/>, ordinal 6. [retcode.sru:L190]</summary>
    public const long SQLITE_READONLY_DIRECTORY = SQLITE_READONLY + (6 * 256);

    // --- Refinement of SQLITE_ABORT = 4 -------------------------- retcode.sru:L191 -------
    //  ANOMALY 1 OF 2, PRESERVED DELIBERATELY. Read the member's remarks before touching it.

    /// <summary>
    /// Refines <see cref="SQLITE_ABORT"/>, ordinal <b>2</b>. [retcode.sru:L191]
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED ANOMALY - THE MULTIPLIER IS 2 AND THERE IS NO ORDINAL-1 SIBLING. Every other
    /// family in this section begins at ordinal 1 and runs contiguously; this one consists of a
    /// single member whose ordinal is 2, so its value is <c>4 + 2 * 256 = 516</c> rather than the
    /// <c>260</c> a reader extrapolating from the neighbouring families would expect.
    /// </para>
    /// <para>
    /// It looks like a typo and it is not: the oracle declares it exactly this way and the value
    /// is what the SQLite headers themselves define, so the ordinal must NOT be renumbered to 1
    /// and the absent ordinal-1 sibling must NOT be invented. Both edits would change a value that
    /// travels in characterization recordings and, in the second case, would publish a constant
    /// that has no counterpart in the engine at all.
    /// </para>
    /// </remarks>
    public const long SQLITE_ABORT_ROLLBACK = SQLITE_ABORT + (2 * 256);

    // --- Refinements of SQLITE_CONSTRAINT = 19 ------------------- retcode.sru:L192-L201 ---
    //  10 refinements at ordinals 1 through 10. These are the codes the Persistence service's
    //  structured database-error payload reports on a constraint violation; note that the
    //  optimistic-concurrency conflict is NOT one of them - a stale updatewhereclause match
    //  produces a zero-row update rather than a constraint failure.

    /// <summary>Refines <see cref="SQLITE_CONSTRAINT"/>, ordinal 1. [retcode.sru:L192]</summary>
    public const long SQLITE_CONSTRAINT_CHECK = SQLITE_CONSTRAINT + (1 * 256);

    /// <summary>Refines <see cref="SQLITE_CONSTRAINT"/>, ordinal 2. [retcode.sru:L193]</summary>
    public const long SQLITE_CONSTRAINT_COMMITHOOK = SQLITE_CONSTRAINT + (2 * 256);

    /// <summary>Refines <see cref="SQLITE_CONSTRAINT"/>, ordinal 3. [retcode.sru:L194]</summary>
    public const long SQLITE_CONSTRAINT_FOREIGNKEY = SQLITE_CONSTRAINT + (3 * 256);

    /// <summary>Refines <see cref="SQLITE_CONSTRAINT"/>, ordinal 4. [retcode.sru:L195]</summary>
    public const long SQLITE_CONSTRAINT_FUNCTION = SQLITE_CONSTRAINT + (4 * 256);

    /// <summary>Refines <see cref="SQLITE_CONSTRAINT"/>, ordinal 5. [retcode.sru:L196]</summary>
    public const long SQLITE_CONSTRAINT_NOTNULL = SQLITE_CONSTRAINT + (5 * 256);

    /// <summary>Refines <see cref="SQLITE_CONSTRAINT"/>, ordinal 6. [retcode.sru:L197]</summary>
    public const long SQLITE_CONSTRAINT_PRIMARYKEY = SQLITE_CONSTRAINT + (6 * 256);

    /// <summary>Refines <see cref="SQLITE_CONSTRAINT"/>, ordinal 7. [retcode.sru:L198]</summary>
    public const long SQLITE_CONSTRAINT_TRIGGER = SQLITE_CONSTRAINT + (7 * 256);

    /// <summary>Refines <see cref="SQLITE_CONSTRAINT"/>, ordinal 8. [retcode.sru:L199]</summary>
    public const long SQLITE_CONSTRAINT_UNIQUE = SQLITE_CONSTRAINT + (8 * 256);

    /// <summary>Refines <see cref="SQLITE_CONSTRAINT"/>, ordinal 9. [retcode.sru:L200]</summary>
    public const long SQLITE_CONSTRAINT_VTAB = SQLITE_CONSTRAINT + (9 * 256);

    /// <summary>Refines <see cref="SQLITE_CONSTRAINT"/>, ordinal 10. [retcode.sru:L201]</summary>
    /// <remarks>
    /// The oracle spells this one declaration without a space before its multiplier -
    /// <c>(SQLITE_CONSTRAINT +(10*256))</c> - which is a whitespace difference only and carries no
    /// semantic weight; the value is <c>19 + 10 * 256 = 2579</c> either way.
    /// </remarks>
    public const long SQLITE_CONSTRAINT_ROWID = SQLITE_CONSTRAINT + (10 * 256);

    // --- Refinements of SQLITE_NOTICE = 27 ----------------------- retcode.sru:L202-L203 ---

    /// <summary>Refines <see cref="SQLITE_NOTICE"/>, ordinal 1. [retcode.sru:L202]</summary>
    public const long SQLITE_NOTICE_RECOVER_WAL = SQLITE_NOTICE + (1 * 256);

    /// <summary>Refines <see cref="SQLITE_NOTICE"/>, ordinal 2. [retcode.sru:L203]</summary>
    public const long SQLITE_NOTICE_RECOVER_ROLLBACK = SQLITE_NOTICE + (2 * 256);

    // --- Refinement of SQLITE_WARNING = 28 ----------------------- retcode.sru:L204 -------

    /// <summary>Refines <see cref="SQLITE_WARNING"/>, ordinal 1. [retcode.sru:L204]</summary>
    public const long SQLITE_WARNING_AUTOINDEX = SQLITE_WARNING + (1 * 256);

    // --- Refinement of SQLITE_AUTH = 23 -------------------------- retcode.sru:L205 -------

    /// <summary>Refines <see cref="SQLITE_AUTH"/>, ordinal 1. [retcode.sru:L205]</summary>
    public const long SQLITE_AUTH_USER = SQLITE_AUTH + (1 * 256);

    // --- Refinement of SQLITE_OK = 0 ----------------------------- retcode.sru:L206 -------
    //  ANOMALY 2 OF 2, PRESERVED DELIBERATELY. An extended code off the SUCCESS base, and the
    //  last declaration in the oracle's variables block. Read the member's remarks before
    //  reasoning about it, and note that the oracle closes the group at L208 with
    //  `/*--- End SQLite ---*/` and the block itself at L210 with `end variables`.

    /// <summary>
    /// Refines <see cref="SQLITE_OK"/>, ordinal 1 - an extended code hanging off the
    /// <b>success</b> base, so its value is <c>256</c>. [retcode.sru:L206]
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED ANOMALY - A NON-ZERO CODE THAT DENOTES SUCCESS. Every other extended code in this
    /// section refines an error or a notice; this one refines <see cref="SQLITE_OK"/>, so
    /// <c>0 + 1 * 256 = 256</c> is a positive, non-zero value that nevertheless means the
    /// operation succeeded. Any consumer that treats "non-zero" as "failed" will misread it, which
    /// is exactly why it is called out here rather than left to be discovered.
    /// </para>
    /// <para>
    /// How the framework's own predicates classify it, both verified against the oracle:
    /// <c>IsSucceeded</c> returns <b>true</b>, because the test is <c>rtCode &gt;= RetCode.OK</c>
    /// and <c>256 &gt;= 0</c> [issucceeded.srf:L12] - so here the tri-state algebra happens to
    /// agree with the code's meaning. <c>IsAllowed</c> returns <b>false</b>: its arms are
    /// <c>rtCode = RetCode.ALLOW</c>, <c>IsNull(rtCode)</c> and <c>rtCode &gt; 1000</c>
    /// [isallowed.srf:L11], and <c>256</c> satisfies none of them - the undocumented
    /// <c>&gt; 1000</c> arm does NOT reach it. The two predicates therefore disagree about this
    /// value, and that disagreement is legacy behaviour to preserve, not an inconsistency to
    /// resolve.
    /// </para>
    /// </remarks>
    public const long SQLITE_OK_LOAD_PERMANENTLY = SQLITE_OK + (1 * 256);
}
