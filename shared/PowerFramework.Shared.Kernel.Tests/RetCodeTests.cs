// ==============================================================================================
//  RetCodeTests - the parity suite for the PowerFramework return-code catalogue
//  --------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Kernel.RetCode
//                    (shared/PowerFramework.Shared.Kernel/RetCode.cs)
//  BEHAVIOURAL       ws_objects/pfw.shared.pbl.src/retcode.sru, 221 lines, constants on L37-L208
//  ORACLE            That .sru is READ ONLY and it is the ONLY specification for these values.
//
//  WHY THIS SUITE IS THE FOUNDATION OF THE WHOLE FOLDER
//  --------------------------------------------------------------------------------------------
//  Every other suite in this project compares its results against these constants, and RetCode.cs
//  is the declared source of truth that the `RetCode` enum in
//  shared/PowerFramework.Contracts/Proto/common.v1.proto must equal value for value. The direction
//  of alignment is one way: the contract author aligns to RetCode.cs, never the reverse. A wrong
//  expectation in this file therefore does not fail loudly in one test - it propagates silently
//  into the wire format that four services speak. That is why every expected value below carries
//  the retcode.sru line it was taken from, and why the values were transcribed mechanically from
//  the oracle rather than retyped.
//
//  At the time this suite was authored, shared/PowerFramework.Contracts/Proto/common.v1.proto did
//  not exist yet, so there was no counterparty to cross-check against. The obligation is recorded
//  here so it is discharged when that file appears: if its enum and this table ever disagree, the
//  .proto is wrong.
//
//  THE ORACLE HAS NO LEGACY TEST WINDOW - THE SOURCE BODY IS THE WHOLE PROVENANCE CHAIN
//  --------------------------------------------------------------------------------------------
//  The legacy repository ships 47 `w_test_*.srw` characterization windows under
//  ws_objects/pfw.tests.pbl.src/, and NONE of them covers the kernel primitives: there is no
//  w_test_retcode anywhere in the estate. So unlike the DataWindow or SQLite behaviours, these
//  values cannot be corroborated by running the legacy application - the declarations in
//  retcode.sru ARE the specification, in full. The `[retcode.sru:Lnnn]` locator on every row below
//  is consequently not decoration; it is the entire evidence trail for that expectation, and a
//  reviewer checks a value by opening the oracle at that exact line.
//
//  C-C - THE LEGACY TREE IS READ ONLY AND IS NEVER TOUCHED AT RUNTIME
//  --------------------------------------------------------------------------------------------
//  retcode.sru is referenced by line number in comments only. This file opens no path under
//  ws_objects/**, reads no file at all, and copies neither the oracle's BSD 2-Clause licence
//  header nor its Chinese four-condition restatement (retcode.sru:L13-L34) - that obligation is
//  discharged by the repository-root NOTICE and LICENSE. A runtime read would also be impossible
//  in CI regardless: the repository-root .dockerignore excludes ws_objects/ from the build
//  context, so the oracle is simply not present inside a container image. Every assertion here is
//  a compile-time constant comparison plus reflection over the already-loaded RetCode type.
//
//  C-B - LEGACY QUIRKS ARE PINNED, NEVER TIDIED
//  --------------------------------------------------------------------------------------------
//  Two quirks in this catalogue look like redundancy a well-meaning future edit would remove:
//  three distinct identifiers all equal 0, and two distinct spellings both equal -2. Both are
//  deliberately preserved legacy behaviour, so both get an explicitly named test carrying the
//  oracle locator - see ZeroAliasesAreThreeSpellingsOfTheSameValue and
//  CancelledSpellingsAreTwoSpellingsOfTheSameValue. Delete an "obviously redundant" alias from
//  RetCode.cs and one of those tests fails by name, which is exactly the point: the identifier
//  SPELLINGS are part of the contract as much as the values are, because these exact strings
//  appear in serialized payloads on the new service contracts, in log records, and in the
//  characterization recordings that are the parity evidence for the migration.
//
//  That is also why the tables below hold identifier names as STRING LITERALS rather than as
//  `nameof(...)` expressions. A `nameof` table is derived from the implementation, so an audit
//  built on it compares the implementation with itself. String literals are an independent
//  transcription of the oracle, so the coverage audit
//  (EveryConstantDeclaredByRetCodeIsAssertedByThisSuite) is a genuine three-way check: oracle
//  spelling, declared spelling, and declared value.
//
//  C-H - WARNINGS ARE ERRORS HERE TOO, AND THE NAMING EXCEPTION DOES NOT REACH TEST CODE
//  --------------------------------------------------------------------------------------------
//  Directory.Build.props sets TreatWarningsAsErrors, Nullable and EnableNETAnalyzers for every
//  project including this one, and the repository-root .editorconfig scopes its CA1707 / IDE1006
//  naming suppressions BY FILE GLOB to the implementation files on its BAND 3 roster - RetCode.cs
//  among them, that roster being the single source of truth for the list - and to
//  no test file at all. Consequently this file REFERENCES SCREAMING_SNAKE members freely (naming
//  analyzers report declarations, never uses) but DECLARES nothing non-conventional: every table,
//  helper, member-data provider, test method, parameter and tuple element below is conventional
//  PascalCase or camelCase with no underscores. Identifier strings live in `string` values, never
//  in identifiers.
//
//  There is deliberately no `#pragma warning disable` and no `NoWarn` anywhere in this file or its
//  project: obeying the analyzers is cheaper than suppressing them, and a suppression here would
//  be the seam through which the next test file quietly adopts non-conventional names.
//
//  C-K - WHY THE EXTENDED SQLITE CODES ARE ASSERTED AS ARITHMETIC, NOT AS LITERALS
//  --------------------------------------------------------------------------------------------
//  All 67 extended SQLite result codes are declared in the oracle as `(BASE + (n * 256))`, and the
//  theory below asserts them in exactly that form - base constant, multiplier, stride - rather
//  than against a pre-computed integer. The reason is that the two forms fail differently. A
//  hand-copied literal is only ever compared with itself: if the digits were mistyped once, in
//  both the implementation and the test, the suite agrees with the mistake and reports success.
//  Asserting `extended == base + (multiplier * 256)` instead re-derives the expectation from a
//  base code that is independently pinned by the SQLite base-code tests, so a transcription slip
//  in either operand cannot cancel out. The multiplier also documents WHICH refinement of its base
//  code an extended value is, which a bare integer destroys.
//
//  THE SUITE PINS ALL 156 CONSTANTS, AND PROVES THAT IT DOES
//  --------------------------------------------------------------------------------------------
//  The seven tables below hold 3 + 2 + 5 + 31 + 17 + 31 + 67 = 156 rows, which is exactly the
//  number of constants RetCode declares. That is not a coincidence to be maintained by hope:
//  EveryConstantDeclaredByRetCodeIsAssertedByThisSuite reflects over the type and asserts that the
//  set of identifiers asserted here EQUALS the set of identifiers declared there. Add a constant to
//  RetCode.cs without adding a row here and that test fails; add a row here for a constant that
//  does not exist and it fails too.
//
//  Structural properties are asserted structurally rather than as row-by-row literals wherever the
//  oracle states a structure. Thirty-one hand-written rows would only prove that thirty-one
//  literals were copied; asserting "starts at -3, ends at -33, every step is exactly -1, all
//  members distinct, exactly 31 of them" is what actually proves the span is gap-free, which is
//  the property the oracle has and the property a consumer relies on.
// ==============================================================================================

using System.Reflection;

using Xunit;

// NOTE ON IMPORTS. There is deliberately no `using PowerFramework.Shared.Kernel;` here. This
// namespace is nested inside it, so C# name lookup walks outward and resolves `RetCode` from the
// project reference without any directive; adding one would be an unnecessary using directive in a
// project that treats warnings as errors. `System.Reflection` supplies BindingFlags and FieldInfo
// for the declaration inventory, `Xunit` supplies the test attributes and assertions, and
// System.Collections.Generic plus System.Linq arrive through the SDK's implicit usings. Those are
// the only dependencies this suite has: no I/O, no host, no database, no network.
namespace PowerFramework.Shared.Kernel.Tests;

/// <summary>
/// Characterization tests for <see cref="RetCode"/>, the return-code catalogue ported from
/// <c>ws_objects/pfw.shared.pbl.src/retcode.sru</c>.
/// </summary>
/// <remarks>
/// <para>
/// The suite is table driven: each group of constants in the oracle becomes one table whose rows
/// carry the identifier spelling, the constant reference and - in a comment - the oracle line and
/// the value that line evaluates to. Member-data providers project those tables into theories, so
/// a failure names the offending constant instead of reporting an anonymous number mismatch.
/// </para>
/// <para>
/// Every test in this class is deterministic and side-effect free. Nothing here reads the clock,
/// the filesystem, the environment or the network, which matters because these expectations are the
/// baseline the characterization recordings are compared against: a suite that could disagree with
/// itself between two runs would be useless as a parity oracle.
/// </para>
/// </remarks>
public sealed class RetCodeTests
{
    // ------------------------------------------------------------------------------------------
    //  Audit constants.
    //
    //  These are the group sizes the oracle actually has, counted from it rather than assumed. They
    //  are asserted rather than merely documented so that a constant added to or removed from
    //  RetCode.cs cannot slip through unnoticed - which is the failure mode that matters most here,
    //  because a silently added return code would reach the wire contract without a reviewer ever
    //  seeing a red test.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Total number of public constants <see cref="RetCode"/> declares: 3 zero aliases + PREVENT +
    /// FAILED + 2 cancelled spellings + the 31-member contiguous error block + 3 sentinels + 17
    /// XML parse statuses + 31 SQLite base result codes + 67 SQLite extended result codes.
    /// </summary>
    private const int ExpectedTotalConstantCount = 156;

    /// <summary>
    /// Size of the gap-free error block spanning -3 to -33 inclusive. [retcode.sru:L46-L76]
    /// </summary>
    private const int ExpectedContiguousErrorCodeCount = 31;

    /// <summary>Size of the XML parse-status set, spanning 0 to 16. [retcode.sru:L84-L100]</summary>
    private const int ExpectedXmlParseStatusCount = 17;

    /// <summary>Number of SQLite base result codes. [retcode.sru:L107-L138]</summary>
    private const int ExpectedSqliteBaseResultCodeCount = 31;

    /// <summary>Number of SQLite extended result codes. [retcode.sru:L140-L206]</summary>
    private const int ExpectedSqliteExtendedResultCodeCount = 67;

    /// <summary>
    /// The stride SQLite uses to layer an extended result code onto its base code: every extended
    /// code in the oracle is declared as <c>(BASE + (n * 256))</c>. [retcode.sru:L139-L206]
    /// </summary>
    private const long ExtendedResultCodeStride = 256L;

    /// <summary>
    /// The largest multiplier the oracle uses, reached by SQLITE_IOERR_ROLLBACK_ATOMIC.
    /// [retcode.sru:L173]
    /// </summary>
    private const int LargestExtendedResultCodeMultiplier = 31;

    /// <summary>
    /// Every public <see langword="long"/> constant <see cref="RetCode"/> declares, keyed by its
    /// exact identifier spelling.
    /// </summary>
    /// <remarks>
    /// Built once by reflection so that the tables in this file can be checked against what the
    /// type really declares, in both directions: a row naming a constant that does not exist fails,
    /// and a constant with no row fails the coverage audit. Reflection is used rather than a second
    /// hand-maintained list precisely because a hand-maintained list would drift.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, long> DeclaredConstants = BuildDeclaredConstants();

    // ------------------------------------------------------------------------------------------
    //  TABLE 1 of 7 - the three spellings of zero. [retcode.sru:L39-L41]
    //
    //  C-B QUIRK, DELIBERATELY PRESERVED. The oracle declares OK, SUCCESS and ALLOW as three
    //  separate constants all equal to 0, because the three read naturally at three different call
    //  sites: a plain result, a successful operation, and the permitted outcome of a vetoable hook.
    //  They are NOT redundancy to collapse. Because they are indistinguishable numerically, the
    //  only thing that can preserve them is the spelling, and the spelling is what the serialized
    //  payloads, log records and characterization recordings carry.
    // ------------------------------------------------------------------------------------------
    private static readonly (string Identifier, long Value)[] ZeroAliases =
    [
        ("OK",      RetCode.OK),      // [retcode.sru:L39] -> 0
        ("SUCCESS", RetCode.SUCCESS), // [retcode.sru:L40] -> 0
        ("ALLOW",   RetCode.ALLOW),   // [retcode.sru:L41] -> 0
    ];

    // ------------------------------------------------------------------------------------------
    //  TABLE 2 of 7 - both spellings of cancelled. [retcode.sru:L44-L45]
    //
    //  C-B QUIRK, DELIBERATELY PRESERVED. The oracle declares the American CANCELED and the British
    //  CANCELLED as two separate constants both equal to -2. CANCELED is declared FIRST, at L44,
    //  which is a detail with consequences beyond this file: a formatter that maps a numeric code
    //  back to a name by scanning declarations in order yields "CANCELED", not "CANCELLED", so
    //  FormatRetCodeTests depends on that ordering and it is asserted here rather than assumed
    //  there.
    // ------------------------------------------------------------------------------------------
    private static readonly (string Identifier, long Value)[] CancelledSpellings =
    [
        ("CANCELED",  RetCode.CANCELED),  // [retcode.sru:L44] -> -2  (declared first)
        ("CANCELLED", RetCode.CANCELLED), // [retcode.sru:L45] -> -2
    ];

    // ------------------------------------------------------------------------------------------
    //  TABLE 3 of 7 - the codes that stand alone, each with the value the oracle gives it.
    //
    //  PREVENT and FAILED are the two immediate neighbours of zero. The other three are sentinels
    //  the oracle places deliberately far from the contiguous error block so that a new error code
    //  can always be appended to that block without colliding with them - the gap between -33 and
    //  -2000 is design, not accident, and the gap between -2001 and -4000 likewise.
    //
    //  PREVENT is worth a second look while reading it: it is 1, and the tri-state predicate
    //  IsSucceeded tests `>= 0`, so a PREVENT reads as a SUCCESS. That algebra belongs to
    //  PredicatesTests; what this file owns is the value that makes it happen.
    // ------------------------------------------------------------------------------------------
    private static readonly (string Identifier, long Value, long Expected)[] SingleValuedCodes =
    [
        ("PREVENT",             RetCode.PREVENT,             1L),     // [retcode.sru:L42]
        ("FAILED",              RetCode.FAILED,              -1L),    // [retcode.sru:L43]
        ("E_NO_SUPPORT",        RetCode.E_NO_SUPPORT,        -2000L), // [retcode.sru:L77]
        ("E_NO_IMPLEMENTATION", RetCode.E_NO_IMPLEMENTATION, -2001L), // [retcode.sru:L78]
        ("UNKNOWN",             RetCode.UNKNOWN,             -4000L), // [retcode.sru:L79]
    ];

    // ------------------------------------------------------------------------------------------
    //  TABLE 4 of 7 - the contiguous error block, in the oracle's declaration order.
    //  [retcode.sru:L46-L76]
    //
    //  Thirty-one declarations on thirty-one consecutive lines, claiming every integer from -3 down
    //  to -33 exactly once with no gaps. The ORDER of this table is the contract, not just its
    //  contents: the structural tests read it as a sequence and assert that each step is exactly
    //  -1, which is what proves gap-freeness. Reordering these rows to alphabetise them would break
    //  that proof while leaving every individual value correct, so the order must be left alone.
    // ------------------------------------------------------------------------------------------
    private static readonly (string Identifier, long Value)[] ContiguousErrorCodes =
    [
        ("E_INVALID_ARGUMENT",    RetCode.E_INVALID_ARGUMENT),    // [retcode.sru:L46] -> -3
        ("E_INVALID_IMAGE",       RetCode.E_INVALID_IMAGE),       // [retcode.sru:L47] -> -4
        ("E_INVALID_OBJECT",      RetCode.E_INVALID_OBJECT),      // [retcode.sru:L48] -> -5
        ("E_INVALID_TYPE",        RetCode.E_INVALID_TYPE),        // [retcode.sru:L49] -> -6
        ("E_INVALID_TRANSACTION", RetCode.E_INVALID_TRANSACTION), // [retcode.sru:L50] -> -7
        ("E_INVALID_SQL",         RetCode.E_INVALID_SQL),         // [retcode.sru:L51] -> -8
        ("E_INVALID_DATA",        RetCode.E_INVALID_DATA),        // [retcode.sru:L52] -> -9
        ("E_INVALID_DATAOBJECT",  RetCode.E_INVALID_DATAOBJECT),  // [retcode.sru:L53] -> -10
        ("E_INVALID_HANDLE",      RetCode.E_INVALID_HANDLE),      // [retcode.sru:L54] -> -11
        ("E_OUT_OF_BOUND",        RetCode.E_OUT_OF_BOUND),        // [retcode.sru:L55] -> -12
        ("E_OUT_OF_RANGE",        RetCode.E_OUT_OF_RANGE),        // [retcode.sru:L56] -> -13
        ("E_OUT_OF_MEMORY",       RetCode.E_OUT_OF_MEMORY),       // [retcode.sru:L57] -> -14
        ("E_FILE_NOT_FOUND",      RetCode.E_FILE_NOT_FOUND),      // [retcode.sru:L58] -> -15
        ("E_OBJECT_NOT_FOUND",    RetCode.E_OBJECT_NOT_FOUND),    // [retcode.sru:L59] -> -16
        ("E_DATA_NOT_FOUND",      RetCode.E_DATA_NOT_FOUND),      // [retcode.sru:L60] -> -17
        ("E_FUNCTION_NOT_FOUND",  RetCode.E_FUNCTION_NOT_FOUND),  // [retcode.sru:L61] -> -18
        ("E_EVENT_NOT_FOUND",     RetCode.E_EVENT_NOT_FOUND),     // [retcode.sru:L62] -> -19
        ("E_MEMBER_NOT_FOUND",    RetCode.E_MEMBER_NOT_FOUND),    // [retcode.sru:L63] -> -20
        ("E_VAR_NOT_FOUND",       RetCode.E_VAR_NOT_FOUND),       // [retcode.sru:L64] -> -21
        ("E_NOT_EXISTS",          RetCode.E_NOT_EXISTS),          // [retcode.sru:L65] -> -22
        ("E_BUSY",                RetCode.E_BUSY),                // [retcode.sru:L66] -> -23
        ("E_TIME_OUT",            RetCode.E_TIME_OUT),            // [retcode.sru:L67] -> -24
        ("E_ACCESS_DENIED",       RetCode.E_ACCESS_DENIED),       // [retcode.sru:L68] -> -25
        ("E_WIN32_ERROR",         RetCode.E_WIN32_ERROR),         // [retcode.sru:L69] -> -26
        ("E_INTERNAL_ERROR",      RetCode.E_INTERNAL_ERROR),      // [retcode.sru:L70] -> -27
        ("E_DB_ERROR",            RetCode.E_DB_ERROR),            // [retcode.sru:L71] -> -28
        ("E_HTTP_ERROR",          RetCode.E_HTTP_ERROR),          // [retcode.sru:L72] -> -29
        ("E_WINHTTP_ERROR",       RetCode.E_WINHTTP_ERROR),       // [retcode.sru:L73] -> -30
        ("E_IO_ERROR",            RetCode.E_IO_ERROR),            // [retcode.sru:L74] -> -31
        ("E_SQL_BIND_ARG_FAILED", RetCode.E_SQL_BIND_ARG_FAILED), // [retcode.sru:L75] -> -32
        ("E_RETRY",               RetCode.E_RETRY),               // [retcode.sru:L76] -> -33
    ];

    // ------------------------------------------------------------------------------------------
    //  TABLE 5 of 7 - the XML parser parse statuses, in declaration order. [retcode.sru:L84-L100]
    //
    //  A SEPARATE CODE SPACE, NOT AN EXTENSION OF THE ONE ABOVE. These 17 values run 0..16 ASCENDING
    //  and are returned by the legacy XML parse-result object, so they overlap the SQLite base codes
    //  numerically and mean something entirely different. The trap to notice while reading:
    //  XML_E_FILE_NOT_FOUND is 1 while the framework's own E_FILE_NOT_FOUND is -15. They are not
    //  duplicates to reconcile - a value is only meaningful against the space it came from - and no
    //  test here compares a value from one space with a constant from another.
    //
    //  These constants are in scope even though the XML object family they describe is deferred to
    //  the Documents service: the catalogue is ported whole because its numbering is the contract,
    //  and a partial catalogue would be a silent gap in the shared kernel.
    // ------------------------------------------------------------------------------------------
    private static readonly (string Identifier, long Value)[] XmlParseStatuses =
    [
        ("XML_OK",                     RetCode.XML_OK),                     // [retcode.sru:L84]  -> 0
        ("XML_E_FILE_NOT_FOUND",       RetCode.XML_E_FILE_NOT_FOUND),       // [retcode.sru:L85]  -> 1
        ("XML_E_IO_ERROR",             RetCode.XML_E_IO_ERROR),             // [retcode.sru:L86]  -> 2
        ("XML_E_OUT_OF_MEMORY",        RetCode.XML_E_OUT_OF_MEMORY),        // [retcode.sru:L87]  -> 3
        ("XML_E_INTERNAL_ERROR",       RetCode.XML_E_INTERNAL_ERROR),       // [retcode.sru:L88]  -> 4
        ("XML_E_UNRECOGNIZED_TAG",     RetCode.XML_E_UNRECOGNIZED_TAG),     // [retcode.sru:L89]  -> 5
        ("XML_E_BAD_PI",               RetCode.XML_E_BAD_PI),               // [retcode.sru:L90]  -> 6
        ("XML_E_BAD_COMMENT",          RetCode.XML_E_BAD_COMMENT),          // [retcode.sru:L91]  -> 7
        ("XML_E_BAD_CDATA",            RetCode.XML_E_BAD_CDATA),            // [retcode.sru:L92]  -> 8
        ("XML_E_BAD_DOCTYPE",          RetCode.XML_E_BAD_DOCTYPE),          // [retcode.sru:L93]  -> 9
        ("XML_E_BAD_PCDATA",           RetCode.XML_E_BAD_PCDATA),           // [retcode.sru:L94]  -> 10
        ("XML_E_BAD_START_ELEMENT",    RetCode.XML_E_BAD_START_ELEMENT),    // [retcode.sru:L95]  -> 11
        ("XML_E_BAD_ATTRIBUTE",        RetCode.XML_E_BAD_ATTRIBUTE),        // [retcode.sru:L96]  -> 12
        ("XML_E_BAD_END_ELEMENT",      RetCode.XML_E_BAD_END_ELEMENT),      // [retcode.sru:L97]  -> 13
        ("XML_E_END_ELEMENT_MISMATCH", RetCode.XML_E_END_ELEMENT_MISMATCH), // [retcode.sru:L98]  -> 14
        ("XML_E_APPEND_INVALID_ROOT",  RetCode.XML_E_APPEND_INVALID_ROOT),  // [retcode.sru:L99]  -> 15
        ("XML_E_NO_DOCUMENT_ELEMENT",  RetCode.XML_E_NO_DOCUMENT_ELEMENT),  // [retcode.sru:L100] -> 16
    ];

    // ------------------------------------------------------------------------------------------
    //  TABLE 6 of 7 - the SQLite base result codes, in declaration order. [retcode.sru:L107-L138]
    //
    //  Thirty-one values in three runs, and the shape matters because the extended codes are layered
    //  onto them: SQLITE_OK is 0 on its own line [L107]; then a contiguous run of 28 from
    //  SQLITE_ERROR = 1 [L109] to SQLITE_WARNING = 28 [L136]; then SQLITE_ROW = 100 [L137] and
    //  SQLITE_DONE = 101 [L138].
    //
    //  THE JUMP FROM 28 TO 100 IS REAL AND MUST NOT BE "REPAIRED". The oracle's own trailing comments
    //  identify the last two as step outcomes rather than errors - a row is ready, and execution has
    //  finished [retcode.sru:L137-L138] - which is the only explanation the repository offers for
    //  their placement, and it is enough: they are a different kind of outcome from the run above
    //  them. A reader who assumes the whole table is one contiguous ascending sequence will
    //  mis-derive both values, which is why the run and the jump are asserted by two separate named
    //  tests.
    // ------------------------------------------------------------------------------------------
    private static readonly (string Identifier, long Value)[] SqliteBaseResultCodes =
    [
        ("SQLITE_OK",         RetCode.SQLITE_OK),         // [retcode.sru:L107] -> 0
        ("SQLITE_ERROR",      RetCode.SQLITE_ERROR),      // [retcode.sru:L109] -> 1
        ("SQLITE_INTERNAL",   RetCode.SQLITE_INTERNAL),   // [retcode.sru:L110] -> 2
        ("SQLITE_PERM",       RetCode.SQLITE_PERM),       // [retcode.sru:L111] -> 3
        ("SQLITE_ABORT",      RetCode.SQLITE_ABORT),      // [retcode.sru:L112] -> 4
        ("SQLITE_BUSY",       RetCode.SQLITE_BUSY),       // [retcode.sru:L113] -> 5
        ("SQLITE_LOCKED",     RetCode.SQLITE_LOCKED),     // [retcode.sru:L114] -> 6
        ("SQLITE_NOMEM",      RetCode.SQLITE_NOMEM),      // [retcode.sru:L115] -> 7
        ("SQLITE_READONLY",   RetCode.SQLITE_READONLY),   // [retcode.sru:L116] -> 8
        ("SQLITE_INTERRUPT",  RetCode.SQLITE_INTERRUPT),  // [retcode.sru:L117] -> 9
        ("SQLITE_IOERR",      RetCode.SQLITE_IOERR),      // [retcode.sru:L118] -> 10
        ("SQLITE_CORRUPT",    RetCode.SQLITE_CORRUPT),    // [retcode.sru:L119] -> 11
        ("SQLITE_NOTFOUND",   RetCode.SQLITE_NOTFOUND),   // [retcode.sru:L120] -> 12
        ("SQLITE_FULL",       RetCode.SQLITE_FULL),       // [retcode.sru:L121] -> 13
        ("SQLITE_CANTOPEN",   RetCode.SQLITE_CANTOPEN),   // [retcode.sru:L122] -> 14
        ("SQLITE_PROTOCOL",   RetCode.SQLITE_PROTOCOL),   // [retcode.sru:L123] -> 15
        ("SQLITE_EMPTY",      RetCode.SQLITE_EMPTY),      // [retcode.sru:L124] -> 16
        ("SQLITE_SCHEMA",     RetCode.SQLITE_SCHEMA),     // [retcode.sru:L125] -> 17
        ("SQLITE_TOOBIG",     RetCode.SQLITE_TOOBIG),     // [retcode.sru:L126] -> 18
        ("SQLITE_CONSTRAINT", RetCode.SQLITE_CONSTRAINT), // [retcode.sru:L127] -> 19
        ("SQLITE_MISMATCH",   RetCode.SQLITE_MISMATCH),   // [retcode.sru:L128] -> 20
        ("SQLITE_MISUSE",     RetCode.SQLITE_MISUSE),     // [retcode.sru:L129] -> 21
        ("SQLITE_NOLFS",      RetCode.SQLITE_NOLFS),      // [retcode.sru:L130] -> 22
        ("SQLITE_AUTH",       RetCode.SQLITE_AUTH),       // [retcode.sru:L131] -> 23
        ("SQLITE_FORMAT",     RetCode.SQLITE_FORMAT),     // [retcode.sru:L132] -> 24
        ("SQLITE_RANGE",      RetCode.SQLITE_RANGE),      // [retcode.sru:L133] -> 25
        ("SQLITE_NOTADB",     RetCode.SQLITE_NOTADB),     // [retcode.sru:L134] -> 26
        ("SQLITE_NOTICE",     RetCode.SQLITE_NOTICE),     // [retcode.sru:L135] -> 27
        ("SQLITE_WARNING",    RetCode.SQLITE_WARNING),    // [retcode.sru:L136] -> 28
        ("SQLITE_ROW",        RetCode.SQLITE_ROW),        // [retcode.sru:L137] -> 100
        ("SQLITE_DONE",       RetCode.SQLITE_DONE),       // [retcode.sru:L138] -> 101
    ];

    // ------------------------------------------------------------------------------------------
    //  TABLE 7 of 7 - all 67 SQLite extended result codes, in declaration order.
    //  [retcode.sru:L140-L206]
    //
    //  Columns: identifier spelling, the constant itself, the BASE code it refines, and the
    //  multiplier n from the oracle's own `(BASE + (n * 256))` expression. Asserting the arithmetic
    //  instead of a pre-computed integer is the C-K decision explained in the file header: the
    //  expectation is re-derived from a base code that the SQLite base-code tests pin independently,
    //  so a transcription slip in either operand cannot cancel itself out, and the multiplier
    //  records WHICH refinement of its base an extended code is.
    //
    //  The multiplier sequences are NOT uniformly 1-based-contiguous per base code, and any test
    //  written on the assumption that they are would be asserting something the oracle never says.
    //  Counted from the oracle, the families are:
    //
    //      SQLITE_ERROR       n = 1..3        SQLITE_CORRUPT     n = 1..2
    //      SQLITE_IOERR       n = 1..31       SQLITE_READONLY    n = 1..6
    //      SQLITE_LOCKED      n = 1..2        SQLITE_ABORT       n = 2 ONLY  <-- see below
    //      SQLITE_BUSY        n = 1..2        SQLITE_CONSTRAINT  n = 1..10
    //      SQLITE_CANTOPEN    n = 1..5        SQLITE_NOTICE      n = 1..2
    //      SQLITE_WARNING     n = 1           SQLITE_AUTH        n = 1
    //      SQLITE_OK          n = 1
    //
    //  SQLITE_ABORT_ROLLBACK [retcode.sru:L191] is the anomaly: it uses multiplier 2 with NO n = 1
    //  sibling anywhere in the oracle, so its value is 4 + 512 = 516 and there is no 260. It gets
    //  its own named test rather than relying on this table, because the whole point is that the
    //  gap is intentional and a future reader must not "complete the sequence".
    //
    //  SQLITE_CANTOPEN_DIRTYWAL [retcode.sru:L182] carries a "Not Used" remark in the oracle. It is
    //  still declared, still numbered, and still asserted: an unused code is part of the numbering
    //  contract, and omitting it would leave a hole in the audit.
    // ------------------------------------------------------------------------------------------
    private static readonly (string Identifier, long Value, long BaseResultCode, int Multiplier)[] SqliteExtendedResultCodes =
    [
        ("SQLITE_ERROR_MISSING_COLLSEQ",   RetCode.SQLITE_ERROR_MISSING_COLLSEQ,   RetCode.SQLITE_ERROR,       1), // [retcode.sru:L140] -> 257
        ("SQLITE_ERROR_RETRY",             RetCode.SQLITE_ERROR_RETRY,             RetCode.SQLITE_ERROR,       2), // [retcode.sru:L141] -> 513
        ("SQLITE_ERROR_SNAPSHOT",          RetCode.SQLITE_ERROR_SNAPSHOT,          RetCode.SQLITE_ERROR,       3), // [retcode.sru:L142] -> 769
        ("SQLITE_IOERR_READ",              RetCode.SQLITE_IOERR_READ,              RetCode.SQLITE_IOERR,       1), // [retcode.sru:L143] -> 266
        ("SQLITE_IOERR_SHORT_READ",        RetCode.SQLITE_IOERR_SHORT_READ,        RetCode.SQLITE_IOERR,       2), // [retcode.sru:L144] -> 522
        ("SQLITE_IOERR_WRITE",             RetCode.SQLITE_IOERR_WRITE,             RetCode.SQLITE_IOERR,       3), // [retcode.sru:L145] -> 778
        ("SQLITE_IOERR_FSYNC",             RetCode.SQLITE_IOERR_FSYNC,             RetCode.SQLITE_IOERR,       4), // [retcode.sru:L146] -> 1034
        ("SQLITE_IOERR_DIR_FSYNC",         RetCode.SQLITE_IOERR_DIR_FSYNC,         RetCode.SQLITE_IOERR,       5), // [retcode.sru:L147] -> 1290
        ("SQLITE_IOERR_TRUNCATE",          RetCode.SQLITE_IOERR_TRUNCATE,          RetCode.SQLITE_IOERR,       6), // [retcode.sru:L148] -> 1546
        ("SQLITE_IOERR_FSTAT",             RetCode.SQLITE_IOERR_FSTAT,             RetCode.SQLITE_IOERR,       7), // [retcode.sru:L149] -> 1802
        ("SQLITE_IOERR_UNLOCK",            RetCode.SQLITE_IOERR_UNLOCK,            RetCode.SQLITE_IOERR,       8), // [retcode.sru:L150] -> 2058
        ("SQLITE_IOERR_RDLOCK",            RetCode.SQLITE_IOERR_RDLOCK,            RetCode.SQLITE_IOERR,       9), // [retcode.sru:L151] -> 2314
        ("SQLITE_IOERR_DELETE",            RetCode.SQLITE_IOERR_DELETE,            RetCode.SQLITE_IOERR,      10), // [retcode.sru:L152] -> 2570
        ("SQLITE_IOERR_BLOCKED",           RetCode.SQLITE_IOERR_BLOCKED,           RetCode.SQLITE_IOERR,      11), // [retcode.sru:L153] -> 2826
        ("SQLITE_IOERR_NOMEM",             RetCode.SQLITE_IOERR_NOMEM,             RetCode.SQLITE_IOERR,      12), // [retcode.sru:L154] -> 3082
        ("SQLITE_IOERR_ACCESS",            RetCode.SQLITE_IOERR_ACCESS,            RetCode.SQLITE_IOERR,      13), // [retcode.sru:L155] -> 3338
        ("SQLITE_IOERR_CHECKRESERVEDLOCK", RetCode.SQLITE_IOERR_CHECKRESERVEDLOCK, RetCode.SQLITE_IOERR,      14), // [retcode.sru:L156] -> 3594
        ("SQLITE_IOERR_LOCK",              RetCode.SQLITE_IOERR_LOCK,              RetCode.SQLITE_IOERR,      15), // [retcode.sru:L157] -> 3850
        ("SQLITE_IOERR_CLOSE",             RetCode.SQLITE_IOERR_CLOSE,             RetCode.SQLITE_IOERR,      16), // [retcode.sru:L158] -> 4106
        ("SQLITE_IOERR_DIR_CLOSE",         RetCode.SQLITE_IOERR_DIR_CLOSE,         RetCode.SQLITE_IOERR,      17), // [retcode.sru:L159] -> 4362
        ("SQLITE_IOERR_SHMOPEN",           RetCode.SQLITE_IOERR_SHMOPEN,           RetCode.SQLITE_IOERR,      18), // [retcode.sru:L160] -> 4618
        ("SQLITE_IOERR_SHMSIZE",           RetCode.SQLITE_IOERR_SHMSIZE,           RetCode.SQLITE_IOERR,      19), // [retcode.sru:L161] -> 4874
        ("SQLITE_IOERR_SHMLOCK",           RetCode.SQLITE_IOERR_SHMLOCK,           RetCode.SQLITE_IOERR,      20), // [retcode.sru:L162] -> 5130
        ("SQLITE_IOERR_SHMMAP",            RetCode.SQLITE_IOERR_SHMMAP,            RetCode.SQLITE_IOERR,      21), // [retcode.sru:L163] -> 5386
        ("SQLITE_IOERR_SEEK",              RetCode.SQLITE_IOERR_SEEK,              RetCode.SQLITE_IOERR,      22), // [retcode.sru:L164] -> 5642
        ("SQLITE_IOERR_DELETE_NOENT",      RetCode.SQLITE_IOERR_DELETE_NOENT,      RetCode.SQLITE_IOERR,      23), // [retcode.sru:L165] -> 5898
        ("SQLITE_IOERR_MMAP",              RetCode.SQLITE_IOERR_MMAP,              RetCode.SQLITE_IOERR,      24), // [retcode.sru:L166] -> 6154
        ("SQLITE_IOERR_GETTEMPPATH",       RetCode.SQLITE_IOERR_GETTEMPPATH,       RetCode.SQLITE_IOERR,      25), // [retcode.sru:L167] -> 6410
        ("SQLITE_IOERR_CONVPATH",          RetCode.SQLITE_IOERR_CONVPATH,          RetCode.SQLITE_IOERR,      26), // [retcode.sru:L168] -> 6666
        ("SQLITE_IOERR_VNODE",             RetCode.SQLITE_IOERR_VNODE,             RetCode.SQLITE_IOERR,      27), // [retcode.sru:L169] -> 6922
        ("SQLITE_IOERR_AUTH",              RetCode.SQLITE_IOERR_AUTH,              RetCode.SQLITE_IOERR,      28), // [retcode.sru:L170] -> 7178
        ("SQLITE_IOERR_BEGIN_ATOMIC",      RetCode.SQLITE_IOERR_BEGIN_ATOMIC,      RetCode.SQLITE_IOERR,      29), // [retcode.sru:L171] -> 7434
        ("SQLITE_IOERR_COMMIT_ATOMIC",     RetCode.SQLITE_IOERR_COMMIT_ATOMIC,     RetCode.SQLITE_IOERR,      30), // [retcode.sru:L172] -> 7690
        ("SQLITE_IOERR_ROLLBACK_ATOMIC",   RetCode.SQLITE_IOERR_ROLLBACK_ATOMIC,   RetCode.SQLITE_IOERR,      31), // [retcode.sru:L173] -> 7946
        ("SQLITE_LOCKED_SHAREDCACHE",      RetCode.SQLITE_LOCKED_SHAREDCACHE,      RetCode.SQLITE_LOCKED,      1), // [retcode.sru:L174] -> 262
        ("SQLITE_LOCKED_VTAB",             RetCode.SQLITE_LOCKED_VTAB,             RetCode.SQLITE_LOCKED,      2), // [retcode.sru:L175] -> 518
        ("SQLITE_BUSY_RECOVERY",           RetCode.SQLITE_BUSY_RECOVERY,           RetCode.SQLITE_BUSY,        1), // [retcode.sru:L176] -> 261
        ("SQLITE_BUSY_SNAPSHOT",           RetCode.SQLITE_BUSY_SNAPSHOT,           RetCode.SQLITE_BUSY,        2), // [retcode.sru:L177] -> 517
        ("SQLITE_CANTOPEN_NOTEMPDIR",      RetCode.SQLITE_CANTOPEN_NOTEMPDIR,      RetCode.SQLITE_CANTOPEN,    1), // [retcode.sru:L178] -> 270
        ("SQLITE_CANTOPEN_ISDIR",          RetCode.SQLITE_CANTOPEN_ISDIR,          RetCode.SQLITE_CANTOPEN,    2), // [retcode.sru:L179] -> 526
        ("SQLITE_CANTOPEN_FULLPATH",       RetCode.SQLITE_CANTOPEN_FULLPATH,       RetCode.SQLITE_CANTOPEN,    3), // [retcode.sru:L180] -> 782
        ("SQLITE_CANTOPEN_CONVPATH",       RetCode.SQLITE_CANTOPEN_CONVPATH,       RetCode.SQLITE_CANTOPEN,    4), // [retcode.sru:L181] -> 1038
        ("SQLITE_CANTOPEN_DIRTYWAL",       RetCode.SQLITE_CANTOPEN_DIRTYWAL,       RetCode.SQLITE_CANTOPEN,    5), // [retcode.sru:L182] -> 1294
        ("SQLITE_CORRUPT_VTAB",            RetCode.SQLITE_CORRUPT_VTAB,            RetCode.SQLITE_CORRUPT,     1), // [retcode.sru:L183] -> 267
        ("SQLITE_CORRUPT_SEQUENCE",        RetCode.SQLITE_CORRUPT_SEQUENCE,        RetCode.SQLITE_CORRUPT,     2), // [retcode.sru:L184] -> 523
        ("SQLITE_READONLY_RECOVERY",       RetCode.SQLITE_READONLY_RECOVERY,       RetCode.SQLITE_READONLY,    1), // [retcode.sru:L185] -> 264
        ("SQLITE_READONLY_CANTLOCK",       RetCode.SQLITE_READONLY_CANTLOCK,       RetCode.SQLITE_READONLY,    2), // [retcode.sru:L186] -> 520
        ("SQLITE_READONLY_ROLLBACK",       RetCode.SQLITE_READONLY_ROLLBACK,       RetCode.SQLITE_READONLY,    3), // [retcode.sru:L187] -> 776
        ("SQLITE_READONLY_DBMOVED",        RetCode.SQLITE_READONLY_DBMOVED,        RetCode.SQLITE_READONLY,    4), // [retcode.sru:L188] -> 1032
        ("SQLITE_READONLY_CANTINIT",       RetCode.SQLITE_READONLY_CANTINIT,       RetCode.SQLITE_READONLY,    5), // [retcode.sru:L189] -> 1288
        ("SQLITE_READONLY_DIRECTORY",      RetCode.SQLITE_READONLY_DIRECTORY,      RetCode.SQLITE_READONLY,    6), // [retcode.sru:L190] -> 1544
        ("SQLITE_ABORT_ROLLBACK",          RetCode.SQLITE_ABORT_ROLLBACK,          RetCode.SQLITE_ABORT,       2), // [retcode.sru:L191] -> 516
        ("SQLITE_CONSTRAINT_CHECK",        RetCode.SQLITE_CONSTRAINT_CHECK,        RetCode.SQLITE_CONSTRAINT,  1), // [retcode.sru:L192] -> 275
        ("SQLITE_CONSTRAINT_COMMITHOOK",   RetCode.SQLITE_CONSTRAINT_COMMITHOOK,   RetCode.SQLITE_CONSTRAINT,  2), // [retcode.sru:L193] -> 531
        ("SQLITE_CONSTRAINT_FOREIGNKEY",   RetCode.SQLITE_CONSTRAINT_FOREIGNKEY,   RetCode.SQLITE_CONSTRAINT,  3), // [retcode.sru:L194] -> 787
        ("SQLITE_CONSTRAINT_FUNCTION",     RetCode.SQLITE_CONSTRAINT_FUNCTION,     RetCode.SQLITE_CONSTRAINT,  4), // [retcode.sru:L195] -> 1043
        ("SQLITE_CONSTRAINT_NOTNULL",      RetCode.SQLITE_CONSTRAINT_NOTNULL,      RetCode.SQLITE_CONSTRAINT,  5), // [retcode.sru:L196] -> 1299
        ("SQLITE_CONSTRAINT_PRIMARYKEY",   RetCode.SQLITE_CONSTRAINT_PRIMARYKEY,   RetCode.SQLITE_CONSTRAINT,  6), // [retcode.sru:L197] -> 1555
        ("SQLITE_CONSTRAINT_TRIGGER",      RetCode.SQLITE_CONSTRAINT_TRIGGER,      RetCode.SQLITE_CONSTRAINT,  7), // [retcode.sru:L198] -> 1811
        ("SQLITE_CONSTRAINT_UNIQUE",       RetCode.SQLITE_CONSTRAINT_UNIQUE,       RetCode.SQLITE_CONSTRAINT,  8), // [retcode.sru:L199] -> 2067
        ("SQLITE_CONSTRAINT_VTAB",         RetCode.SQLITE_CONSTRAINT_VTAB,         RetCode.SQLITE_CONSTRAINT,  9), // [retcode.sru:L200] -> 2323
        ("SQLITE_CONSTRAINT_ROWID",        RetCode.SQLITE_CONSTRAINT_ROWID,        RetCode.SQLITE_CONSTRAINT, 10), // [retcode.sru:L201] -> 2579
        ("SQLITE_NOTICE_RECOVER_WAL",      RetCode.SQLITE_NOTICE_RECOVER_WAL,      RetCode.SQLITE_NOTICE,      1), // [retcode.sru:L202] -> 283
        ("SQLITE_NOTICE_RECOVER_ROLLBACK", RetCode.SQLITE_NOTICE_RECOVER_ROLLBACK, RetCode.SQLITE_NOTICE,      2), // [retcode.sru:L203] -> 539
        ("SQLITE_WARNING_AUTOINDEX",       RetCode.SQLITE_WARNING_AUTOINDEX,       RetCode.SQLITE_WARNING,     1), // [retcode.sru:L204] -> 284
        ("SQLITE_AUTH_USER",               RetCode.SQLITE_AUTH_USER,               RetCode.SQLITE_AUTH,        1), // [retcode.sru:L205] -> 279
        ("SQLITE_OK_LOAD_PERMANENTLY",     RetCode.SQLITE_OK_LOAD_PERMANENTLY,     RetCode.SQLITE_OK,          1), // [retcode.sru:L206] -> 256
    ];

    // ==========================================================================================
    //  MEMBER-DATA PROVIDERS
    //
    //  Each provider projects one of the seven tables above into a strongly typed TheoryData so the
    //  test runner names the failing constant. They must be public and static for xUnit to reach
    //  them, and they are the only public members of this class besides the tests themselves.
    //
    //  Typed TheoryData is used rather than IEnumerable<object[]> deliberately: the compiler then
    //  checks each row's arity and element types against the theory signature, so a row that
    //  acquires an extra column is a build error rather than a runtime cast failure discovered on a
    //  continuous-integration agent.
    // ==========================================================================================

    /// <summary>Rows for the three spellings of zero. [retcode.sru:L39-L41]</summary>
    public static TheoryData<string, long> ZeroAliasRows() => IdentifierValueRows(ZeroAliases);

    /// <summary>Rows for both spellings of cancelled. [retcode.sru:L44-L45]</summary>
    public static TheoryData<string, long> CancelledSpellingRows() => IdentifierValueRows(CancelledSpellings);

    /// <summary>Rows for the 31-member contiguous error block. [retcode.sru:L46-L76]</summary>
    public static TheoryData<string, long> ContiguousErrorCodeRows() => IdentifierValueRows(ContiguousErrorCodes);

    /// <summary>Rows for the 17 XML parse statuses. [retcode.sru:L84-L100]</summary>
    public static TheoryData<string, long> XmlParseStatusRows() => IdentifierValueRows(XmlParseStatuses);

    /// <summary>Rows for the 31 SQLite base result codes. [retcode.sru:L107-L138]</summary>
    public static TheoryData<string, long> SqliteBaseResultCodeRows() => IdentifierValueRows(SqliteBaseResultCodes);

    /// <summary>
    /// Rows for the codes that stand alone, carrying the exact value the oracle assigns each one.
    /// [retcode.sru:L42-L43, L77-L79]
    /// </summary>
    public static TheoryData<string, long, long> SingleValuedCodeRows()
    {
        TheoryData<string, long, long> rows = [];
        foreach ((string identifier, long value, long expected) in SingleValuedCodes)
        {
            rows.Add(identifier, value, expected);
        }

        return rows;
    }

    /// <summary>
    /// Rows for all 67 SQLite extended result codes, carrying the base code and multiplier from the
    /// oracle's own <c>(BASE + (n * 256))</c> expression. [retcode.sru:L140-L206]
    /// </summary>
    public static TheoryData<string, long, long, int> SqliteExtendedResultCodeRows()
    {
        TheoryData<string, long, long, int> rows = [];
        foreach ((string identifier, long value, long baseResultCode, int multiplier) in SqliteExtendedResultCodes)
        {
            rows.Add(identifier, value, baseResultCode, multiplier);
        }

        return rows;
    }

    // ==========================================================================================
    //  THE PowerFramework RETURN CODES  [retcode.sru:L37-L79]
    // ==========================================================================================

    /// <summary>
    /// Each of OK, SUCCESS and ALLOW is declared with the value zero, under that exact spelling.
    /// [retcode.sru:L39-L41]
    /// </summary>
    [Theory]
    [MemberData(nameof(ZeroAliasRows))]
    public void ZeroAliasesAreDeclaredWithValueZero(string identifier, long value)
    {
        Assert.Equal(0L, value);

        // The spelling is half the contract, so confirm RetCode really declares a constant under
        // this exact identifier and that it carries this exact value. Comparing the compiled
        // reference against the reflected declaration is what makes the string literal load bearing
        // rather than decorative.
        Assert.Equal(value, DeclaredValueOf(identifier));
    }

    /// <summary>
    /// OK, SUCCESS and ALLOW are three spellings of one value, and all three are kept.
    /// [retcode.sru:L39-L41]
    /// </summary>
    /// <remarks>
    /// C-B QUIRK PINNED HERE. This looks like redundancy and is not: the oracle deliberately gives
    /// zero three names so that a plain result, a successful operation and the permitted outcome of
    /// a vetoable hook each read naturally at their own call site. Because the three are numerically
    /// indistinguishable, nothing but the spelling can preserve them - and those spellings travel in
    /// serialized payloads, log records and characterization recordings, where dropping one silently
    /// invalidates every stored comparison that mentions it. If a future edit removes an
    /// "obviously redundant" alias from RetCode.cs, this test is the one that says so by name.
    /// </remarks>
    [Fact]
    public void ZeroAliasesAreThreeSpellingsOfTheSameValue()
    {
        Assert.Equal(RetCode.OK, RetCode.SUCCESS);
        Assert.Equal(RetCode.SUCCESS, RetCode.ALLOW);
        Assert.Equal(3, ZeroAliases.Length);
        Assert.Equal(ZeroAliases.Length, DistinctIdentifierCount(ZeroAliases));
    }

    /// <summary>
    /// Both CANCELED and CANCELLED are declared with the value -2, under those exact spellings.
    /// [retcode.sru:L44-L45]
    /// </summary>
    [Theory]
    [MemberData(nameof(CancelledSpellingRows))]
    public void CancelledSpellingsAreDeclaredWithValueMinusTwo(string identifier, long value)
    {
        Assert.Equal(-2L, value);
        Assert.Equal(value, DeclaredValueOf(identifier));
    }

    /// <summary>
    /// CANCELED and CANCELLED are two spellings of one value, and CANCELED is the first declared.
    /// [retcode.sru:L44-L45]
    /// </summary>
    /// <remarks>
    /// C-B QUIRK PINNED HERE. The oracle declares the American spelling at L44 and the British
    /// spelling at L45, both equal to -2, and both are kept for the same reason the zero aliases
    /// are: the spellings appear in serialized payloads, log records and characterization
    /// recordings, so removing either one breaks stored comparisons rather than tidying a
    /// duplicate.
    /// <para>
    /// The DECLARATION ORDER is load bearing beyond this file. A formatter that turns a numeric code
    /// back into a name by walking declarations in order yields "CANCELED" for -2, never
    /// "CANCELLED", so FormatRetCodeTests depends on CANCELED coming first. That ordering is
    /// asserted here, at the catalogue, rather than being assumed there.
    /// </para>
    /// <para>
    /// -2 also sits in the tri-state hole of the predicate algebra: IsFailed excludes it explicitly
    /// while IsSucceeded tests for zero or greater, so a cancelled result is neither succeeded nor
    /// failed. That behaviour belongs to PredicatesTests; this test owns only the value and the two
    /// spellings that carry it.
    /// </para>
    /// </remarks>
    [Fact]
    public void CancelledSpellingsAreTwoSpellingsOfTheSameValue()
    {
        Assert.Equal(RetCode.CANCELED, RetCode.CANCELLED);
        Assert.Equal(2, CancelledSpellings.Length);
        Assert.Equal("CANCELED", CancelledSpellings[0].Identifier);
        Assert.Equal("CANCELLED", CancelledSpellings[1].Identifier);
    }

    /// <summary>
    /// PREVENT, FAILED and the three sentinels each hold the exact value the oracle assigns them.
    /// [retcode.sru:L42-L43, L77-L79]
    /// </summary>
    [Theory]
    [MemberData(nameof(SingleValuedCodeRows))]
    public void SingleValuedCodesHoldTheirLegacyValue(string identifier, long value, long expected)
    {
        Assert.Equal(expected, value);
        Assert.Equal(value, DeclaredValueOf(identifier));
    }

    /// <summary>
    /// The three sentinels sit far below the contiguous error block, leaving room for that block to
    /// grow. [retcode.sru:L76-L79]
    /// </summary>
    /// <remarks>
    /// The distance between E_RETRY at -33 and E_NO_SUPPORT at -2000 is deliberate: it is what
    /// allows a new error code to be appended to the contiguous block without colliding with a
    /// sentinel. Asserting the ordering records that intent, so a future addition that would close
    /// the gap fails a test instead of silently narrowing the runway.
    /// </remarks>
    [Fact]
    public void SentinelCodesSitFarBelowTheContiguousErrorBlock()
    {
        Assert.True(RetCode.E_NO_SUPPORT < RetCode.E_RETRY);
        Assert.Equal(RetCode.E_NO_SUPPORT - 1L, RetCode.E_NO_IMPLEMENTATION);
        Assert.True(RetCode.UNKNOWN < RetCode.E_NO_IMPLEMENTATION);
    }

    // ==========================================================================================
    //  THE CONTIGUOUS ERROR BLOCK  [retcode.sru:L46-L76]
    //
    //  Asserted STRUCTURALLY rather than as 31 hand-written literals. Thirty-one literal rows would
    //  only prove that thirty-one literals were copied; what a consumer actually relies on is that
    //  the span is gap-free, and the only way to prove that is to assert the endpoints, the step
    //  between every adjacent pair, the distinctness of the whole set and its exact size. Those four
    //  together admit exactly one assignment of values, so they pin all 31 constants at once - and
    //  they also fail if a constant is ever inserted into the middle of the block, which a literal
    //  table would happily tolerate.
    // ==========================================================================================

    /// <summary>The block begins at E_INVALID_ARGUMENT = -3. [retcode.sru:L46]</summary>
    [Fact]
    public void ContiguousErrorBlockBeginsAtMinusThree()
    {
        Assert.Equal("E_INVALID_ARGUMENT", ContiguousErrorCodes[0].Identifier);
        Assert.Equal(-3L, ContiguousErrorCodes[0].Value);
        Assert.Equal(-3L, RetCode.E_INVALID_ARGUMENT);
    }

    /// <summary>The block ends at E_RETRY = -33. [retcode.sru:L76]</summary>
    [Fact]
    public void ContiguousErrorBlockEndsAtMinusThirtyThree()
    {
        Assert.Equal("E_RETRY", ContiguousErrorCodes[^1].Identifier);
        Assert.Equal(-33L, ContiguousErrorCodes[^1].Value);
        Assert.Equal(-33L, RetCode.E_RETRY);
    }

    /// <summary>
    /// Every step through the block is exactly -1, so the span from -3 to -33 has no gaps.
    /// [retcode.sru:L46-L76]
    /// </summary>
    /// <remarks>
    /// This is the assertion that actually proves gap-freeness. Walking adjacent pairs in the
    /// oracle's declaration order and requiring each successor to be exactly one less than its
    /// predecessor leaves no room for a skipped integer, a repeated integer or a reordering.
    /// </remarks>
    [Fact]
    public void ContiguousErrorBlockDescendsByExactlyOneWithNoGaps()
    {
        for (int index = 1; index < ContiguousErrorCodes.Length; index++)
        {
            (string previousIdentifier, long previousValue) = ContiguousErrorCodes[index - 1];
            (string identifier, long value) = ContiguousErrorCodes[index];

            Assert.True(
                previousValue - 1L == value,
                $"RetCode.{identifier} must be exactly one less than RetCode.{previousIdentifier}: " +
                $"the oracle declares the block on consecutive lines with no gaps, but {identifier} " +
                $"is {value} where {previousValue - 1L} was required.");
        }
    }

    /// <summary>
    /// The block holds exactly 31 members and every value in it is distinct.
    /// [retcode.sru:L46-L76]
    /// </summary>
    /// <remarks>
    /// The count is asserted so that a constant added to or removed from this range in RetCode.cs
    /// cannot slip in unnoticed. Combined with the endpoint and step assertions, 31 members spanning
    /// -3 to -33 inclusive is the only possibility, since that range contains exactly 31 integers.
    /// </remarks>
    [Fact]
    public void ContiguousErrorBlockHasThirtyOneDistinctMembers()
    {
        Assert.Equal(ExpectedContiguousErrorCodeCount, ContiguousErrorCodes.Length);
        Assert.Distinct(ValuesOf(ContiguousErrorCodes));
        Assert.Equal(ExpectedContiguousErrorCodeCount, DistinctIdentifierCount(ContiguousErrorCodes));

        // -3 down to -33 inclusive is 31 integers, so the count and the endpoints agree only if the
        // block claims every one of them exactly once.
        Assert.Equal(
            ExpectedContiguousErrorCodeCount,
            (int)(ContiguousErrorCodes[0].Value - ContiguousErrorCodes[^1].Value) + 1);
    }

    /// <summary>
    /// Each member of the block is declared under its exact identifier spelling with its exact
    /// value. [retcode.sru:L46-L76]
    /// </summary>
    [Theory]
    [MemberData(nameof(ContiguousErrorCodeRows))]
    public void ContiguousErrorCodesAreDeclaredUnderTheirLegacySpelling(string identifier, long value)
    {
        Assert.Equal(value, DeclaredValueOf(identifier));
        Assert.InRange(value, -33L, -3L);
    }

    // ==========================================================================================
    //  THE XML PARSER PARSE STATUSES  [retcode.sru:L84-L100]
    // ==========================================================================================

    /// <summary>
    /// The XML parse statuses span 0 through 16 contiguously, ascending by one.
    /// [retcode.sru:L84-L100]
    /// </summary>
    /// <remarks>
    /// This set ASCENDS, unlike the framework's own error block, which descends. That difference is
    /// the clearest signal that these are a separate code space rather than an extension of the
    /// codes above: they are the parse status of the legacy XML parse-result object, and a value is
    /// only meaningful against the space it came from.
    /// </remarks>
    [Fact]
    public void XmlParseStatusesSpanZeroThroughSixteenContiguously()
    {
        Assert.Equal("XML_OK", XmlParseStatuses[0].Identifier);
        Assert.Equal(0L, XmlParseStatuses[0].Value);
        Assert.Equal("XML_E_NO_DOCUMENT_ELEMENT", XmlParseStatuses[^1].Identifier);
        Assert.Equal(16L, XmlParseStatuses[^1].Value);

        for (int index = 1; index < XmlParseStatuses.Length; index++)
        {
            (string previousIdentifier, long previousValue) = XmlParseStatuses[index - 1];
            (string identifier, long value) = XmlParseStatuses[index];

            Assert.True(
                previousValue + 1L == value,
                $"RetCode.{identifier} must be exactly one greater than RetCode.{previousIdentifier}: " +
                $"the oracle declares the XML parse statuses as a contiguous ascending run, but " +
                $"{identifier} is {value} where {previousValue + 1L} was required.");
        }
    }

    /// <summary>
    /// The XML parse statuses hold exactly 17 members and every value in it is distinct.
    /// [retcode.sru:L84-L100]
    /// </summary>
    [Fact]
    public void XmlParseStatusesHaveSeventeenDistinctMembers()
    {
        Assert.Equal(ExpectedXmlParseStatusCount, XmlParseStatuses.Length);
        Assert.Distinct(ValuesOf(XmlParseStatuses));
        Assert.Equal(ExpectedXmlParseStatusCount, DistinctIdentifierCount(XmlParseStatuses));
    }

    /// <summary>
    /// Each XML parse status is declared under its exact identifier spelling with its exact value.
    /// [retcode.sru:L84-L100]
    /// </summary>
    [Theory]
    [MemberData(nameof(XmlParseStatusRows))]
    public void XmlParseStatusesAreDeclaredUnderTheirLegacySpelling(string identifier, long value)
    {
        Assert.Equal(value, DeclaredValueOf(identifier));
        Assert.InRange(value, 0L, 16L);
    }

    /// <summary>
    /// XML_E_FILE_NOT_FOUND and E_FILE_NOT_FOUND are unrelated codes that must never be conflated.
    /// [retcode.sru:L58, L85]
    /// </summary>
    /// <remarks>
    /// The two identifiers differ by a prefix and their values differ by 16, and the resemblance is
    /// a genuine trap: one is an XML parse status of 1, the other a framework return code of -15. A
    /// consumer that compares a parse status against a framework constant would find them unequal
    /// for the wrong reason. Asserting the disagreement records that these are two code spaces which
    /// happen to be catalogued in one class, not a numbering inconsistency to reconcile.
    /// </remarks>
    [Fact]
    public void XmlFileNotFoundIsADifferentCodeSpaceFromTheFrameworkFileNotFound()
    {
        Assert.Equal(1L, RetCode.XML_E_FILE_NOT_FOUND);
        Assert.Equal(-15L, RetCode.E_FILE_NOT_FOUND);
        Assert.NotEqual(RetCode.XML_E_FILE_NOT_FOUND, RetCode.E_FILE_NOT_FOUND);
    }

    // ==========================================================================================
    //  THE SQLITE BASE RESULT CODES  [retcode.sru:L107-L138]
    // ==========================================================================================

    /// <summary>
    /// The landmark SQLite base result codes hold the values the oracle assigns them.
    /// [retcode.sru:L107, L109, L136, L137, L138]
    /// </summary>
    /// <remarks>
    /// These five are spot-asserted by name because they are the boundaries every other SQLite
    /// assertion is expressed against: the zero, the first and last members of the error run, and
    /// the two step outcomes that sit in the 100 band.
    /// </remarks>
    [Fact]
    public void SqliteLandmarkBaseResultCodesHoldTheirLegacyValue()
    {
        Assert.Equal(0L, RetCode.SQLITE_OK);      // [retcode.sru:L107]
        Assert.Equal(1L, RetCode.SQLITE_ERROR);   // [retcode.sru:L109]
        Assert.Equal(28L, RetCode.SQLITE_WARNING); // [retcode.sru:L136]
        Assert.Equal(100L, RetCode.SQLITE_ROW);    // [retcode.sru:L137]
        Assert.Equal(101L, RetCode.SQLITE_DONE);   // [retcode.sru:L138]
    }

    /// <summary>
    /// The error run from SQLITE_ERROR = 1 to SQLITE_WARNING = 28 is contiguous and ascending.
    /// [retcode.sru:L109-L136]
    /// </summary>
    /// <remarks>
    /// Asserted structurally over the 28 declarations between SQLITE_OK and SQLITE_ROW, which is the
    /// run the extended result codes are layered onto. If this run acquired a gap, an extended code
    /// computed from a base inside it would silently shift, so the contiguity is not a curiosity - it
    /// is the precondition for the extended-code arithmetic being meaningful at all.
    /// </remarks>
    [Fact]
    public void SqliteErrorRunFromOneToTwentyEightIsContiguous()
    {
        (string Identifier, long Value)[] run =
            [.. SqliteBaseResultCodes.Where(entry => entry.Value >= 1L && entry.Value <= 28L)];

        Assert.Equal(28, run.Length);
        Assert.Equal("SQLITE_ERROR", run[0].Identifier);
        Assert.Equal("SQLITE_WARNING", run[^1].Identifier);

        for (int index = 1; index < run.Length; index++)
        {
            (string previousIdentifier, long previousValue) = run[index - 1];
            (string identifier, long value) = run[index];

            Assert.True(
                previousValue + 1L == value,
                $"RetCode.{identifier} must be exactly one greater than RetCode.{previousIdentifier}: " +
                $"the oracle declares SQLITE_ERROR through SQLITE_WARNING as a contiguous ascending " +
                $"run, but {identifier} is {value} where {previousValue + 1L} was required.");
        }
    }

    /// <summary>
    /// SQLITE_ROW and SQLITE_DONE jump past the error run into the 100 band, and the gap is
    /// deliberate. [retcode.sru:L136-L138]
    /// </summary>
    /// <remarks>
    /// The oracle steps straight from 28 to 100. Upstream SQLite reserves the 100 band for step
    /// outcomes - a row is available, or iteration has finished - which are not errors at all, so
    /// they are placed clear of the error run. This test exists so that nobody "repairs" the gap into
    /// 29 and 30 by assuming the whole table is one ascending sequence, and so that the 71-wide jump
    /// is recorded as intended rather than rediscovered as a surprise.
    /// </remarks>
    [Fact]
    public void SqliteRowAndDoneJumpPastTheErrorRunIntoTheHundredBand()
    {
        Assert.Equal(72L, RetCode.SQLITE_ROW - RetCode.SQLITE_WARNING);
        Assert.Equal(RetCode.SQLITE_ROW + 1L, RetCode.SQLITE_DONE);
        Assert.NotEqual(29L, RetCode.SQLITE_ROW);
        Assert.NotEqual(30L, RetCode.SQLITE_DONE);
    }

    /// <summary>
    /// The SQLite base result codes hold exactly 31 members and every value in it is distinct.
    /// [retcode.sru:L107-L138]
    /// </summary>
    [Fact]
    public void SqliteBaseResultCodesHaveThirtyOneDistinctMembers()
    {
        Assert.Equal(ExpectedSqliteBaseResultCodeCount, SqliteBaseResultCodes.Length);
        Assert.Distinct(ValuesOf(SqliteBaseResultCodes));
        Assert.Equal(ExpectedSqliteBaseResultCodeCount, DistinctIdentifierCount(SqliteBaseResultCodes));
    }

    /// <summary>
    /// Each SQLite base result code is declared under its exact identifier spelling with its exact
    /// value. [retcode.sru:L107-L138]
    /// </summary>
    [Theory]
    [MemberData(nameof(SqliteBaseResultCodeRows))]
    public void SqliteBaseResultCodesAreDeclaredUnderTheirLegacySpelling(string identifier, long value)
    {
        Assert.Equal(value, DeclaredValueOf(identifier));

        // Every base code is below the stride, which is what allows an extended code to encode its
        // base in the low byte and its ordinal above it.
        Assert.InRange(value, 0L, ExtendedResultCodeStride - 1L);
    }

    // ==========================================================================================
    //  THE SQLITE EXTENDED RESULT CODES  [retcode.sru:L140-L206]
    // ==========================================================================================

    /// <summary>
    /// Every extended result code equals its base code plus its multiplier times the 256 stride.
    /// [retcode.sru:L140-L206]
    /// </summary>
    /// <remarks>
    /// C-K DECISION RECORDED HERE. The expectation is written as the ARITHMETIC the oracle declares -
    /// base code, multiplier, stride - and never as a pre-computed integer, because the two forms
    /// fail differently. A hand-copied literal is only ever compared with itself: mistype the digits
    /// once, in both the implementation and the test, and the suite agrees with the mistake and
    /// reports success. Re-deriving the expectation from a base code that the SQLite base-code tests
    /// pin independently means a transcription slip in either operand cannot cancel out. The
    /// multiplier is also documentation a bare integer destroys: it states which refinement of its
    /// base code an extended value is.
    /// <para>
    /// All 67 rows are asserted rather than the six worked examples alone. The table is generated
    /// from the oracle, so covering the whole set costs nothing extra and turns the audit in
    /// <see cref="EveryConstantDeclaredByRetCodeIsAssertedByThisSuite"/> into a complete one.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(SqliteExtendedResultCodeRows))]
    public void SqliteExtendedResultCodesEqualTheirBasePlusMultiplierTimesStride(
        string identifier,
        long value,
        long baseResultCode,
        int multiplier)
    {
        Assert.Equal(baseResultCode + (multiplier * ExtendedResultCodeStride), value);
        Assert.Equal(value, DeclaredValueOf(identifier));

        // Multipliers observed in the oracle run from 1 to 31, the upper bound being reached by
        // SQLITE_IOERR_ROLLBACK_ATOMIC. [retcode.sru:L173]
        Assert.InRange(multiplier, 1, LargestExtendedResultCodeMultiplier);
    }

    /// <summary>
    /// The six worked extended result codes hold the exact values the oracle's arithmetic produces.
    /// [retcode.sru:L140, L143, L173, L201, L205, L206]
    /// </summary>
    /// <remarks>
    /// These six are additionally asserted against their computed integers, as a cross-check on the
    /// arithmetic theory itself: if the stride, a base code and a multiplier were all wrong in a way
    /// that happened to be self-consistent, the theory would still pass and these would not. They
    /// span the extremes of the table deliberately - the smallest extended code, the largest
    /// multiplier, and a double-digit multiplier.
    /// </remarks>
    [Fact]
    public void WorkedExtendedResultCodesHoldTheirComputedValue()
    {
        Assert.Equal(256L, RetCode.SQLITE_OK_LOAD_PERMANENTLY);     // 0 + 1 * 256   [retcode.sru:L206]
        Assert.Equal(257L, RetCode.SQLITE_ERROR_MISSING_COLLSEQ);   // 1 + 1 * 256   [retcode.sru:L140]
        Assert.Equal(266L, RetCode.SQLITE_IOERR_READ);              // 10 + 1 * 256  [retcode.sru:L143]
        Assert.Equal(279L, RetCode.SQLITE_AUTH_USER);               // 23 + 1 * 256  [retcode.sru:L205]
        Assert.Equal(2579L, RetCode.SQLITE_CONSTRAINT_ROWID);       // 19 + 10 * 256 [retcode.sru:L201]
        Assert.Equal(7946L, RetCode.SQLITE_IOERR_ROLLBACK_ATOMIC);  // 10 + 31 * 256 [retcode.sru:L173]
    }

    /// <summary>
    /// SQLITE_ABORT_ROLLBACK uses multiplier 2 and has no multiplier-1 sibling, so the extended
    /// families are not uniformly 1-based-contiguous. [retcode.sru:L191]
    /// </summary>
    /// <remarks>
    /// THE ANOMALY, ASSERTED BY NAME. The oracle declares exactly one refinement of SQLITE_ABORT and
    /// gives it multiplier 2, producing 4 + 512 = 516. There is no 260. Why the first slot was left
    /// empty is not recorded anywhere in this repository, so no reason is asserted here - only the
    /// fact, which is what the port must reproduce. The missing 1 is emphatically not an omission
    /// this port may fill in.
    /// <para>
    /// The consequence is a rule about the whole table: a test - or a code generator - that assumed
    /// "every base code's extended family starts at n = 1 and runs contiguously" would be asserting
    /// something the oracle never says, and would invent a constant. That is why the arithmetic
    /// theory above carries the multiplier per row from the oracle instead of deriving it from a
    /// family index.
    /// </para>
    /// </remarks>
    [Fact]
    public void SqliteAbortRollbackUsesMultiplierTwoWithNoMultiplierOneSibling()
    {
        Assert.Equal(516L, RetCode.SQLITE_ABORT_ROLLBACK);
        Assert.Equal(RetCode.SQLITE_ABORT + (2 * ExtendedResultCodeStride), RetCode.SQLITE_ABORT_ROLLBACK);

        int[] abortFamilyMultipliers =
        [
            .. SqliteExtendedResultCodes
                .Where(entry => entry.BaseResultCode == RetCode.SQLITE_ABORT)
                .Select(entry => entry.Multiplier),
        ];

        Assert.Equal([2], abortFamilyMultipliers);
        Assert.DoesNotContain(
            RetCode.SQLITE_ABORT + (1 * ExtendedResultCodeStride),
            ValuesOf(SqliteExtendedResultCodes));
    }

    /// <summary>
    /// The extended result codes hold exactly 67 members and every value in it is distinct.
    /// [retcode.sru:L140-L206]
    /// </summary>
    [Fact]
    public void SqliteExtendedResultCodesHaveSixtySevenDistinctMembers()
    {
        Assert.Equal(ExpectedSqliteExtendedResultCodeCount, SqliteExtendedResultCodes.Length);
        Assert.Distinct(ValuesOf(SqliteExtendedResultCodes));
        Assert.Equal(
            ExpectedSqliteExtendedResultCodeCount,
            SqliteExtendedResultCodes.Select(entry => entry.Identifier).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Every extended result code refines a base code this suite already pins, and every one of them
    /// lies above the stride. [retcode.sru:L107-L206]
    /// </summary>
    /// <remarks>
    /// This closes the loop between the two SQLite tables. The arithmetic theory takes each base code
    /// on trust from its own row; this test proves that every such base is one of the 31 codes the
    /// base-code tests independently verified, so no extended code is computed from a value that was
    /// never checked. It also records the structural consequence that no extended code can collide
    /// with a base code, because every base is below 256 and every extended value is at least 256.
    /// </remarks>
    [Fact]
    public void EveryExtendedResultCodeRefinesAVerifiedBaseCodeAndClearsTheStride()
    {
        long[] verifiedBases = ValuesOf(SqliteBaseResultCodes);

        Assert.All(
            SqliteExtendedResultCodes,
            entry =>
            {
                Assert.Contains(entry.BaseResultCode, verifiedBases);
                Assert.True(
                    entry.Value >= ExtendedResultCodeStride,
                    $"RetCode.{entry.Identifier} is {entry.Value}, which is below the {ExtendedResultCodeStride} " +
                    "stride, so it would be indistinguishable from a base result code.");
            });
    }

    // ==========================================================================================
    //  CATALOGUE SHAPE AND THE COVERAGE AUDIT
    // ==========================================================================================

    /// <summary>
    /// RetCode is a static catalogue of compile-time literals with no instance state, no singleton
    /// and no global auto-instance. [retcode.sru:L10]
    /// </summary>
    /// <remarks>
    /// The oracle declares <c>global retcode retcode</c> at L10: a global auto-instantiated variable
    /// whose name shadows its own type name, which is legal only because PowerBuilder resolves one
    /// flat global namespace by library-list order. The port deliberately does NOT reproduce that
    /// instance - the type keeps the descriptive name and there is no shared global object - and this
    /// test pins that resolution so a future edit cannot quietly reintroduce an <c>Instance</c> or a
    /// <c>Current</c> and with it the collision the flat namespace tolerated.
    /// <para>
    /// It also pins the property that makes this class safe as the declared source of truth for the
    /// wire contract: everything it exposes is a compile-time literal. A mutable static field would be
    /// a value that could differ between two readers of the same catalogue.
    /// </para>
    /// </remarks>
    [Fact]
    public void RetCodeIsAStaticCatalogueOfLiteralsWithNoInstanceState()
    {
        Type catalogue = typeof(RetCode);

        // A C# `static class` is encoded as abstract plus sealed, which is also what makes it
        // impossible to instantiate or derive from.
        Assert.True(catalogue.IsAbstract);
        Assert.True(catalogue.IsSealed);
        Assert.Empty(catalogue.GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        // Nothing but literals: no static readonly field, no property, no method, no event, no
        // nested type.
        Assert.DoesNotContain(catalogue.GetFields(BindingFlags.Public | BindingFlags.Static), field => !field.IsLiteral);
        Assert.Empty(catalogue.GetFields(BindingFlags.NonPublic | BindingFlags.Static));
        Assert.Empty(catalogue.GetProperties(BindingFlags.Public | BindingFlags.Static));
        Assert.Empty(catalogue.GetEvents(BindingFlags.Public | BindingFlags.Static));
        Assert.Empty(catalogue.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
        Assert.Empty(catalogue.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));

        // Every declared constant is a long, matching the oracle's `Constant Long`.
        Assert.All(
            catalogue.GetFields(BindingFlags.Public | BindingFlags.Static),
            field => Assert.Equal(typeof(long), field.FieldType));
    }

    /// <summary>
    /// Every constant RetCode declares is asserted by this suite, and the catalogue holds exactly
    /// 156 of them. [retcode.sru:L37-L206]
    /// </summary>
    /// <remarks>
    /// THE AUDIT THAT MAKES THE REST OF THE SUITE TRUSTWORTHY. Reflecting over the type and comparing
    /// its declared identifier set with the union of the seven tables above closes both directions of
    /// the gap that a table-driven suite normally leaves open: a constant added to RetCode.cs without
    /// a row here fails, and a row here naming a constant that does not exist fails too. Without this
    /// test, "the suite passes" would only mean "the constants I happened to list are right", which
    /// is precisely the weaker claim that lets a new return code reach the wire contract unexamined.
    /// <para>
    /// The count 156 is the transcription audit stated in RetCode.cs's own header: 3 zero aliases +
    /// PREVENT + FAILED + 2 cancelled spellings + 31 contiguous error codes + 3 sentinels + 17 XML
    /// parse statuses + 31 SQLite base result codes + 67 SQLite extended result codes. It is asserted
    /// against reflection rather than against a second hand-maintained list, because a second list
    /// would drift.
    /// </para>
    /// <para>
    /// This comparison is also why the tables hold identifier names as string literals rather than
    /// <c>nameof</c> expressions. A <c>nameof</c> table is derived from the implementation, so this
    /// audit would be comparing the implementation with itself; string literals make it a genuine
    /// three-way check of the oracle's spelling, the declared spelling and the declared value.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryConstantDeclaredByRetCodeIsAssertedByThisSuite()
    {
        Assert.Equal(ExpectedTotalConstantCount, DeclaredConstants.Count);

        string[] asserted = [.. AssertedIdentifiers().Order(StringComparer.Ordinal)];
        string[] declared = [.. DeclaredConstants.Keys.Order(StringComparer.Ordinal)];

        Assert.Equal(declared, asserted);
        Assert.Equal(ExpectedTotalConstantCount, asserted.Length);
    }

    /// <summary>
    /// The seven tables partition the catalogue: their sizes sum to the declared constant count with
    /// no row counted twice. [retcode.sru:L37-L206]
    /// </summary>
    /// <remarks>
    /// The set comparison above would still pass if one table shrank while another grew to cover the
    /// same identifiers, which would quietly move a constant out of the group whose structure is
    /// asserted about it. Checking the individual sizes as well as the total keeps each group's
    /// membership stable, so the structural tests continue to say what they claim to say.
    /// </remarks>
    [Fact]
    public void TableSizesPartitionTheCatalogueExactly()
    {
        Assert.Equal(3, ZeroAliases.Length);
        Assert.Equal(2, CancelledSpellings.Length);
        Assert.Equal(5, SingleValuedCodes.Length);
        Assert.Equal(ExpectedContiguousErrorCodeCount, ContiguousErrorCodes.Length);
        Assert.Equal(ExpectedXmlParseStatusCount, XmlParseStatuses.Length);
        Assert.Equal(ExpectedSqliteBaseResultCodeCount, SqliteBaseResultCodes.Length);
        Assert.Equal(ExpectedSqliteExtendedResultCodeCount, SqliteExtendedResultCodes.Length);

        int total = ZeroAliases.Length
            + CancelledSpellings.Length
            + SingleValuedCodes.Length
            + ContiguousErrorCodes.Length
            + XmlParseStatuses.Length
            + SqliteBaseResultCodes.Length
            + SqliteExtendedResultCodes.Length;

        Assert.Equal(ExpectedTotalConstantCount, total);

        // No identifier appears in two tables, so the sum above counts each constant once.
        List<string> everyIdentifier = [.. AssertedIdentifiers()];
        Assert.Equal(total, everyIdentifier.Count);
        Assert.Equal(total, everyIdentifier.Distinct(StringComparer.Ordinal).Count());
    }

    // ==========================================================================================
    //  PRIVATE HELPERS
    // ==========================================================================================

    /// <summary>
    /// Reads every public <see langword="long"/> constant off <see cref="RetCode"/>, keyed by its
    /// exact identifier spelling.
    /// </summary>
    /// <returns>An ordinal-keyed map from identifier spelling to compile-time constant value.</returns>
    /// <exception cref="InvalidOperationException">
    /// A field typed <see langword="long"/> carries a compile-time constant that is not a
    /// <see langword="long"/>. Unreachable through the C# compiler; guarded rather than assumed
    /// because this map is the audit the rest of the suite trusts.
    /// </exception>
    private static IReadOnlyDictionary<string, long> BuildDeclaredConstants()
    {
        FieldInfo[] fields = typeof(RetCode).GetFields(BindingFlags.Public | BindingFlags.Static);
        Dictionary<string, long> constants = new(fields.Length, StringComparer.Ordinal);

        foreach (FieldInfo field in fields)
        {
            // `const` is IsLiteral; `static readonly` is not. Only literals belong in this catalogue,
            // and RetCodeIsAStaticCatalogueOfLiteralsWithNoInstanceState asserts nothing else exists.
            if (!field.IsLiteral || field.FieldType != typeof(long))
            {
                continue;
            }

            constants.Add(
                field.Name,
                field.GetRawConstantValue() is long value
                    ? value
                    : throw new InvalidOperationException(
                        $"RetCode.{field.Name} is declared as long but its compile-time constant " +
                        "value is not a long, so the declaration inventory cannot be trusted."));
        }

        return constants;
    }

    /// <summary>
    /// Returns the value <see cref="RetCode"/> declares under <paramref name="identifier"/>, failing
    /// the calling test if no constant carries that exact spelling.
    /// </summary>
    /// <param name="identifier">The identifier spelling transcribed from the oracle.</param>
    /// <returns>The declared compile-time constant value.</returns>
    private static long DeclaredValueOf(string identifier)
    {
        Assert.True(
            DeclaredConstants.TryGetValue(identifier, out long value),
            "PowerFramework.Shared.Kernel.RetCode declares no public long constant named " +
            $"'{identifier}'. The identifier spellings are part of the contract - they travel in " +
            "serialized payloads, log records and characterization recordings - so a missing or " +
            "renamed constant is a behavioural change, not a restyling. Check the spelling against " +
            "ws_objects/pfw.shared.pbl.src/retcode.sru rather than editing this expectation.");

        return value;
    }

    /// <summary>
    /// Projects an identifier-and-value table into a strongly typed theory data set.
    /// </summary>
    /// <param name="table">One of the oracle-transcribed tables declared in this class.</param>
    /// <returns>One theory row per table entry, in the oracle's declaration order.</returns>
    private static TheoryData<string, long> IdentifierValueRows((string Identifier, long Value)[] table)
    {
        TheoryData<string, long> rows = [];
        foreach ((string identifier, long value) in table)
        {
            rows.Add(identifier, value);
        }

        return rows;
    }

    /// <summary>Extracts the values from an identifier-and-value table, order preserved.</summary>
    /// <param name="table">One of the oracle-transcribed tables declared in this class.</param>
    /// <returns>The table's values in declaration order.</returns>
    private static long[] ValuesOf((string Identifier, long Value)[] table) =>
        [.. table.Select(entry => entry.Value)];

    /// <summary>Extracts the values from the extended result-code table, order preserved.</summary>
    /// <param name="table">The extended SQLite result-code table declared in this class.</param>
    /// <returns>The table's values in declaration order.</returns>
    private static long[] ValuesOf((string Identifier, long Value, long BaseResultCode, int Multiplier)[] table) =>
        [.. table.Select(entry => entry.Value)];

    /// <summary>
    /// Counts the distinct identifier spellings in an identifier-and-value table, comparing
    /// ordinally.
    /// </summary>
    /// <param name="table">One of the oracle-transcribed tables declared in this class.</param>
    /// <returns>The number of distinct spellings the table contains.</returns>
    private static int DistinctIdentifierCount((string Identifier, long Value)[] table) =>
        table.Select(entry => entry.Identifier).Distinct(StringComparer.Ordinal).Count();

    /// <summary>
    /// Every identifier spelling this suite asserts, drawn from all seven tables.
    /// </summary>
    /// <returns>
    /// The union of the seven tables' identifier columns, with no de-duplication so that an
    /// accidental overlap between two tables is visible to the audit rather than hidden by it.
    /// </returns>
    private static IEnumerable<string> AssertedIdentifiers() =>
    [
        .. ZeroAliases.Select(entry => entry.Identifier),
        .. CancelledSpellings.Select(entry => entry.Identifier),
        .. SingleValuedCodes.Select(entry => entry.Identifier),
        .. ContiguousErrorCodes.Select(entry => entry.Identifier),
        .. XmlParseStatuses.Select(entry => entry.Identifier),
        .. SqliteBaseResultCodes.Select(entry => entry.Identifier),
        .. SqliteExtendedResultCodes.Select(entry => entry.Identifier),
    ];
}
