// ==============================================================================================
//  FormatRetCodeTests - the parity suite that PINS TWO PRESERVED DEFECTS as correct behaviour
//  --------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Kernel.Formatting.FormatRetCode
//                    (shared/PowerFramework.Shared.Kernel/Formatting.cs, L274-L396)
//  BEHAVIOURAL       ws_objects/pfw.shared.pbl.src/formatretcode.srf - 85 lines, the whole
//  ORACLE            function body at L10-L84, declared at L3 as
//                    `global type formatretcode from function_object` with NO `native` clause, so
//                    the PowerScript is readable and this is a straight transcription rather than
//                    a reconstruction.
//                    ws_objects/pfw.shared.pbl.src/retcode.sru - the constant VALUES and, just as
//                    importantly, their DECLARATION ORDER.
//
//  THIS SUITE EXISTS TO STOP TWO "OBVIOUS FIXES"
//  --------------------------------------------------------------------------------------------
//  FormatRetCode carries two legacy defects. Neither is subtle once named and both are invisible
//  until named, which is exactly why they are pinned here rather than left to a code review:
//
//      DEFECT ONE, THE MISSING ARM.   The oracle's arms run contiguously from E_INVALID_ARGUMENT
//      (-3) to E_SQL_BIND_ARG_FAILED (-32) at formatretcode.srf:L75-L76 and then jump straight to
//      E_NO_SUPPORT (-2000) at L77-L78. E_RETRY (-33, retcode.sru:L76) is skipped entirely, so it
//      renders through the `case else` fallback at L81-L82 as "UNKNOWN (-33)". PREVENT (1) and
//      UNKNOWN (-4000) fall through for the same reason.
//
//      DEFECT TWO, THE ALIAS COLLAPSE.  retcode.sru declares three names for 0 and two for -2. A
//      PowerScript `choose case` takes the first matching arm, so the value 0 always renders as
//      "OK" and the value -2 always renders as "CANCELLED". The strings "SUCCESS", "ALLOW" and
//      "CANCELED" are therefore UNREACHABLE - no caller has ever seen one.
//
//  Both look like oversights. Adding the E_RETRY arm is the single most likely well-intentioned
//  edit to Formatting.cs, and "restoring" the unreachable alias names is the second. Under the
//  no-behaviour-improvement constraint each would be a REGRESSION, because these renderings
//  already appear in log records, in the serialized payloads of the new service contracts, and in
//  the stored characterization recordings that are the parity evidence for the whole migration.
//  Every assertion below that pins a defect says so in a comment, with its oracle locator, so a
//  future reader who sets out to "fix" the implementation finds a red test that explains itself
//  rather than a red test that looks wrong.
//
//  THE ALIAS RULE, STATED CORRECTLY - THE LOOSE VERSION PRODUCES A FAILING TEST
//  --------------------------------------------------------------------------------------------
//  It is tempting to summarise defect two as "an aliased value yields the first alias's name".
//  That is WRONG, and a suite built on it goes red. The accurate rule, established by reading both
//  oracle files together, is:
//
//      The name returned is the one spelled on the switch arm that appears FIRST IN
//      formatretcode.srf for that value - which is not always the first-declared alias in
//      retcode.sru.
//
//  The two cases behave differently and the difference is the whole point:
//
//      value  0   arm is `case RetCode.OK -> "OK"`        [formatretcode.srf:L11-L12]
//                 OK is ALSO the first-declared alias     [retcode.sru:L39, then L40, L41]
//                 -> the loose rule and the accurate rule happen to agree here.
//
//      value -2   arm is `case RetCode.CANCELLED -> "CANCELLED"` [formatretcode.srf:L15-L16]
//                 but CANCELED is declared FIRST                 [retcode.sru:L44, CANCELLED L45]
//                 -> the loose rule predicts "CANCELED" and the implementation returns
//                    "CANCELLED". The FIRST-DECLARED alias is the UNREACHABLE one here, the exact
//                    inverse of the loose phrasing.
//
//  So the unreachable set is {"SUCCESS", "ALLOW", "CANCELED"} - which is what Formatting.cs
//  DECISION 2 independently names - and NOT {"SUCCESS", "ALLOW", "CANCELLED"}. The reasoning is
//  recorded here so nobody reintroduces the loose rule from memory.
//
//  C-K - WHY EVERY EXPECTED STRING BELOW IS A LITERAL AND NOTHING IS DERIVED BY REFLECTION
//  --------------------------------------------------------------------------------------------
//  The tables hold `"E_INVALID_ARGUMENT"` rather than `nameof(RetCode.E_INVALID_ARGUMENT)`, and
//  nothing here walks RetCode's fields to build an expectation. That is deliberate and it is the
//  load-bearing design decision of this file.
//
//  The entire point of both defects is that THE RENDERED NAME AND THE REQUESTED IDENTIFIER CAN
//  DIFFER. Derive the expectation from the identifier and the alias rows become
//  `Assert.Equal(nameof(RetCode.SUCCESS), FormatRetCode(RetCode.SUCCESS))`, which asserts
//  "SUCCESS" == "SUCCESS" is what the code should do - the tautology, and the precise opposite of
//  the truth. A reflection-derived table is worse still: it compares the implementation with
//  itself, so it would keep passing after somebody added the E_RETRY arm. String literals are an
//  INDEPENDENT transcription of the oracle, which is what makes each row falsifiable.
//
//  Verified rather than assumed, during authoring: the 35 `case RetCode.X` labels at
//  formatretcode.srf:L11-L80 and the 35 `return "Y"` literals beside them were extracted and
//  diffed, and they are textually identical. So for the 35 recognised codes - and only for those -
//  the rendered name does coincide with the identifier spelling. That coincidence is a FINDING
//  about the oracle, not a rule the tests may lean on.
//
//  C-C - THE LEGACY TREE IS READ ONLY AND IS NEVER TOUCHED AT RUNTIME
//  --------------------------------------------------------------------------------------------
//  Both oracle files are referenced by line number in comments only. This file opens no path under
//  ws_objects/**, reads no file at all, and copies neither the oracle's BSD 2-Clause header nor its
//  Chinese four-condition restatement (retcode.sru:L13-L34) - that obligation is discharged by the
//  repository-root LICENSE and NOTICE. A runtime read would be impossible in continuous
//  integration anyway: the repository-root .dockerignore excludes ws_objects/ from the build
//  context, so the oracle is simply not present inside a container image. Every assertion here is
//  a compile-time constant compared against a pure function's return value.
//
//  NO LEGACY TEST WINDOW EXISTS FOR THE KERNEL PRIMITIVES
//  --------------------------------------------------------------------------------------------
//  The legacy estate ships 47 `w_test_*.srw` characterization windows under
//  ws_objects/pfw.tests.pbl.src/ and NONE of them covers the kernel: there is no w_test_retcode and
//  no w_test_formatretcode anywhere. Unlike the DataWindow or SQLite behaviours, these expectations
//  cannot be corroborated by running the legacy application - the function body IS the whole
//  specification. The `[formatretcode.srf:Lnn]` locator on each row is therefore the complete
//  evidence trail for that row, and a reviewer checks it by opening the oracle at that exact line.
//
//  C-H - WARNINGS ARE ERRORS HERE TOO, AND THE NAMING EXCEPTION DOES NOT REACH TEST CODE
//  --------------------------------------------------------------------------------------------
//  Directory.Build.props sets TreatWarningsAsErrors, Nullable and EnableNETAnalyzers for this
//  project exactly as for the library, and the Tests project deliberately overrides none of them.
//  The repository-root .editorconfig scopes its CA1707 and IDE1006 suppressions BY FILE GLOB to ten
//  implementation files that carry preserved legacy constant identifiers, and to NO test file. So
//  this file REFERENCES SCREAMING_SNAKE members freely - CA1707 reports declarations, never uses -
//  and DECLARES nothing non-conventional: every table, provider, test and local below is
//  PascalCase or camelCase. The SCREAMING_SNAKE text inside the string literals is data, not an
//  identifier, and is unaffected.
//
//  Nullability is handled by typing rather than by suppression. FormatRetCode's parameter is
//  `long?`, so the one null input has its own named fact and the tables stay `long`-typed because
//  every row in them carries a value. No null-forgiving operator and no `#pragma` directive appears
//  anywhere in this file.
//
//  THERE ARE NO USER-SPECIFIED RULES FOR THIS PROJECT
//  --------------------------------------------------------------------------------------------
//  The rules document was retrieved and contains exactly one statement: that no user rules were
//  provided. That is a finding, not latitude. No rule is invented or inferred here; the binding
//  constraints in their place are the plan's constraint inventory (C-B, C-C, C-H, C-K above) and
//  its enterprise-standard baseline, which this suite meets by testing one library with no
//  container, no daemon, no database, no network and no clock.
// ==============================================================================================

using System.Globalization;

using Xunit;

namespace PowerFramework.Shared.Kernel.Tests;

/// <summary>
/// Parity tests for <see cref="Formatting.FormatRetCode"/>, transcribed from
/// <c>ws_objects/pfw.shared.pbl.src/formatretcode.srf</c>.
/// </summary>
/// <remarks>
/// Two legacy defects are asserted here as correct behaviour: the arm table skips
/// <c>E_RETRY</c> so it renders through the fallback, and the alias collapse makes the strings
/// <c>SUCCESS</c>, <c>ALLOW</c> and <c>CANCELED</c> unreachable. Neither may be "fixed"; see the
/// file header for the reasoning and the oracle locators.
/// </remarks>
public sealed class FormatRetCodeTests
{
    // ==========================================================================================
    //  THE FALLBACK GRAMMAR, ONCE, SO EVERY ROW BELOW SPELLS IT THE SAME WAY
    //
    //  formatretcode.srf:L82 is `return "UNKNOWN (" + String(rtCode) + ")"`. Rendered: the word
    //  UNKNOWN, ONE space, an opening parenthesis, the decimal value with a leading minus when
    //  negative, a closing parenthesis. No colon, no square brackets, no hexadecimal and no
    //  symbolic name appended. Every expected fallback string below is written out in full rather
    //  than composed from a format string, for the C-K reason in the header: a composed expectation
    //  built from the same expression the implementation uses would stop being an independent
    //  transcription.
    // ==========================================================================================

    // ------------------------------------------------------------------------------------------
    //  TABLE 1 of 5 - THE 35 RECOGNISED ARMS, IN ORACLE ORDER
    //
    //  formatretcode.srf:L11-L80. Seventy lines, two per arm, so thirty-five arms - and that
    //  arithmetic is the audit that the transcription is complete rather than merely plausible.
    //  The order is the oracle's own, unsorted and unregrouped, so the two can be read side by
    //  side.
    //
    //  The locator on each row is the arm's two lines in formatretcode.srf. The value in the
    //  comment is the constant's value from retcode.sru, repeated as prose so a reviewer can check
    //  a row against the oracle without holding RetCode.cs open as well.
    //
    //  C-B NOTE ON WHAT IS ABSENT FROM THIS TABLE. Six of the 41 PowerFramework codes that
    //  retcode.sru:L39-L79 declares are deliberately NOT here, because the oracle gives them no arm
    //  of their own: SUCCESS, ALLOW and CANCELED are aliases shadowed by an earlier arm (TABLE 2),
    //  and PREVENT, E_RETRY and UNKNOWN are simply skipped (TABLE 3). 35 + 3 + 3 = 41, and
    //  DeclaredCodeSpacePartitionsIntoThirtyFiveArmsAndSixNonArms asserts exactly that.
    // ------------------------------------------------------------------------------------------
    private static readonly (long Code, string ExpectedName)[] RecognisedArms =
    [
        (RetCode.OK,                     "OK"),                     // [formatretcode.srf:L11-L12]  0
        (RetCode.FAILED,                 "FAILED"),                 // [formatretcode.srf:L13-L14]  -1
        (RetCode.CANCELLED,              "CANCELLED"),              // [formatretcode.srf:L15-L16]  -2, double-L spelling
        (RetCode.E_INVALID_ARGUMENT,     "E_INVALID_ARGUMENT"),     // [formatretcode.srf:L17-L18]  -3
        (RetCode.E_INVALID_IMAGE,        "E_INVALID_IMAGE"),        // [formatretcode.srf:L19-L20]  -4
        (RetCode.E_INVALID_OBJECT,       "E_INVALID_OBJECT"),       // [formatretcode.srf:L21-L22]  -5
        (RetCode.E_INVALID_TYPE,         "E_INVALID_TYPE"),         // [formatretcode.srf:L23-L24]  -6
        (RetCode.E_INVALID_TRANSACTION,  "E_INVALID_TRANSACTION"),  // [formatretcode.srf:L25-L26]  -7
        (RetCode.E_INVALID_SQL,          "E_INVALID_SQL"),          // [formatretcode.srf:L27-L28]  -8
        (RetCode.E_INVALID_DATA,         "E_INVALID_DATA"),         // [formatretcode.srf:L29-L30]  -9
        (RetCode.E_INVALID_DATAOBJECT,   "E_INVALID_DATAOBJECT"),   // [formatretcode.srf:L31-L32]  -10
        (RetCode.E_INVALID_HANDLE,       "E_INVALID_HANDLE"),       // [formatretcode.srf:L33-L34]  -11
        (RetCode.E_OUT_OF_BOUND,         "E_OUT_OF_BOUND"),         // [formatretcode.srf:L35-L36]  -12
        (RetCode.E_OUT_OF_RANGE,         "E_OUT_OF_RANGE"),         // [formatretcode.srf:L37-L38]  -13
        (RetCode.E_OUT_OF_MEMORY,        "E_OUT_OF_MEMORY"),        // [formatretcode.srf:L39-L40]  -14
        (RetCode.E_FILE_NOT_FOUND,       "E_FILE_NOT_FOUND"),       // [formatretcode.srf:L41-L42]  -15
        (RetCode.E_OBJECT_NOT_FOUND,     "E_OBJECT_NOT_FOUND"),     // [formatretcode.srf:L43-L44]  -16
        (RetCode.E_DATA_NOT_FOUND,       "E_DATA_NOT_FOUND"),       // [formatretcode.srf:L45-L46]  -17
        (RetCode.E_FUNCTION_NOT_FOUND,   "E_FUNCTION_NOT_FOUND"),   // [formatretcode.srf:L47-L48]  -18
        (RetCode.E_EVENT_NOT_FOUND,      "E_EVENT_NOT_FOUND"),      // [formatretcode.srf:L49-L50]  -19
        (RetCode.E_MEMBER_NOT_FOUND,     "E_MEMBER_NOT_FOUND"),     // [formatretcode.srf:L51-L52]  -20
        (RetCode.E_VAR_NOT_FOUND,        "E_VAR_NOT_FOUND"),        // [formatretcode.srf:L53-L54]  -21
        (RetCode.E_NOT_EXISTS,           "E_NOT_EXISTS"),           // [formatretcode.srf:L55-L56]  -22
        (RetCode.E_BUSY,                 "E_BUSY"),                 // [formatretcode.srf:L57-L58]  -23
        (RetCode.E_TIME_OUT,             "E_TIME_OUT"),             // [formatretcode.srf:L59-L60]  -24
        (RetCode.E_ACCESS_DENIED,        "E_ACCESS_DENIED"),        // [formatretcode.srf:L61-L62]  -25
        (RetCode.E_WIN32_ERROR,          "E_WIN32_ERROR"),          // [formatretcode.srf:L63-L64]  -26
        (RetCode.E_INTERNAL_ERROR,       "E_INTERNAL_ERROR"),       // [formatretcode.srf:L65-L66]  -27
        (RetCode.E_DB_ERROR,             "E_DB_ERROR"),             // [formatretcode.srf:L67-L68]  -28
        (RetCode.E_HTTP_ERROR,           "E_HTTP_ERROR"),           // [formatretcode.srf:L69-L70]  -29
        (RetCode.E_WINHTTP_ERROR,        "E_WINHTTP_ERROR"),        // [formatretcode.srf:L71-L72]  -30
        (RetCode.E_IO_ERROR,             "E_IO_ERROR"),             // [formatretcode.srf:L73-L74]  -31
        (RetCode.E_SQL_BIND_ARG_FAILED,  "E_SQL_BIND_ARG_FAILED"),  // [formatretcode.srf:L75-L76]  -32, LAST of the contiguous block
        (RetCode.E_NO_SUPPORT,           "E_NO_SUPPORT"),           // [formatretcode.srf:L77-L78]  -2000, the arms JUMP to here from -32
        (RetCode.E_NO_IMPLEMENTATION,    "E_NO_IMPLEMENTATION"),    // [formatretcode.srf:L79-L80]  -2001, last arm before `case else`
    ];

    // ------------------------------------------------------------------------------------------
    //  TABLE 2 of 5 - DEFECT TWO, THE THREE ALIAS-COLLAPSED IDENTIFIERS
    //
    //  C-B DEFECT PINNED AS CORRECT BEHAVIOUR.
    //  [formatretcode.srf:L11-L12, L15-L16] and [retcode.sru:L39-L41, L44-L45]
    //
    //  Each row names an identifier the caller may legitimately pass, the value it carries, the
    //  name that actually comes back, and the identifier's OWN spelling - which is UNREACHABLE
    //  OUTPUT. The fourth column is the one that makes the row falsifiable: asserting only the
    //  third column would pass against an implementation that had somehow started returning both.
    //
    //  Read the two groups separately, because they do NOT follow the same rule:
    //
    //      SUCCESS and ALLOW are the second and third declarations of 0 (retcode.sru:L40, L41) and
    //      OK is the first (L39). The winning arm is OK's, at formatretcode.srf:L11-L12. Here
    //      first-declared and first-arm agree.
    //
    //      CANCELED is the FIRST declaration of -2 (retcode.sru:L44) and CANCELLED is the second
    //      (L45) - yet the winning arm is CANCELLED's, at formatretcode.srf:L15-L16, because the
    //      oracle simply never wrote a CANCELED arm. So the FIRST-DECLARED name is the unreachable
    //      one. Anyone summarising this defect as "the first alias wins" gets this row backwards;
    //      see the header.
    //
    //  Sibling cross-check, so this is not asserted in isolation: RetCodeTests already pins the
    //  declaration order itself (CancelledSpellingsAreTwoSpellingsOfTheSameValue asserts CANCELED
    //  at index 0), and its own comments name this suite as the counterparty that depends on it.
    //  Formatting.cs DECISION 2 independently names the same three unreachable strings.
    // ------------------------------------------------------------------------------------------
    private static readonly (string Identifier, long Code, string ExpectedName, string UnreachableSpelling)[] AliasCollapsedIdentifiers =
    [
        ("SUCCESS",  RetCode.SUCCESS,  "OK",        "SUCCESS"),  // [retcode.sru:L40] 0  shadowed by the OK arm        [formatretcode.srf:L11-L12]
        ("ALLOW",    RetCode.ALLOW,    "OK",        "ALLOW"),    // [retcode.sru:L41] 0  shadowed by the OK arm        [formatretcode.srf:L11-L12]
        ("CANCELED", RetCode.CANCELED, "CANCELLED", "CANCELED"), // [retcode.sru:L44] -2 shadowed by the CANCELLED arm [formatretcode.srf:L15-L16]
    ];

    // ------------------------------------------------------------------------------------------
    //  TABLE 3 of 5 - DEFECT ONE, THE THREE DECLARED CODES WITH NO ARM AT ALL
    //
    //  C-B DEFECT PINNED AS CORRECT BEHAVIOUR. [formatretcode.srf:L75-L82]
    //
    //  These are declared in retcode.sru and are absent from the arm table, so each one reaches the
    //  `case else` at L81-L82. Unlike TABLE 2 these are not aliases of anything: the values are
    //  unique and the oracle just does not mention them.
    //
    //      E_RETRY  (-33)   THE HEADLINE DEFECT. The arms are contiguous from -3 to -32
    //                       [formatretcode.srf:L17-L76] and resume at -2000 [L77-L78]; -33 is the
    //                       one hole in that run, and it is a hole in the middle rather than at the
    //                       end, which is precisely why it reads as an oversight. retcode.sru:L76
    //                       declares it, so a caller can obtain and pass it. DO NOT ADD THE ARM.
    //                       This row is the tripwire: adding a 36th arm for E_RETRY turns it red.
    //
    //      PREVENT  (1)     retcode.sru:L42. The whole tri-state return algebra turns on this
    //                       value - a prevention reads as a success because IsSucceeded tests for
    //                       zero or greater - and the formatter still has no arm for it. It is the
    //                       only positive PowerFramework code, so it is also the only one whose
    //                       fallback text carries no minus sign, which makes it the row that would
    //                       catch a fallback that hard-coded a sign.
    //
    //      UNKNOWN  (-4000) retcode.sru:L79. WORTH ITS OWN COMMENT, because this row reads like a
    //                       deliberate mapping and is not one. The identifier is spelled UNKNOWN
    //                       and the fallback text begins with the word UNKNOWN, so
    //                       "UNKNOWN (-4000)" looks like an arm that renders the name and appends
    //                       the value. There is no such arm. The word in the output is the
    //                       fallback's own literal from L82 and the coincidence is purely lexical -
    //                       which also means that if somebody DID add an arm returning plain
    //                       "UNKNOWN", this row would catch it, because the value would vanish.
    // ------------------------------------------------------------------------------------------
    private static readonly (string Identifier, long Code, string ExpectedText)[] UnrecognisedDeclaredCodes =
    [
        ("PREVENT", RetCode.PREVENT, "UNKNOWN (1)"),     // [retcode.sru:L42] 1     -> [formatretcode.srf:L81-L82]
        ("E_RETRY", RetCode.E_RETRY, "UNKNOWN (-33)"),   // [retcode.sru:L76] -33   -> [formatretcode.srf:L81-L82]
        ("UNKNOWN", RetCode.UNKNOWN, "UNKNOWN (-4000)"), // [retcode.sru:L79] -4000 -> [formatretcode.srf:L81-L82]
    ];

    // ------------------------------------------------------------------------------------------
    //  TABLE 4 of 5 - VALUES THAT APPEAR NOWHERE IN THE ALGEBRA AT ALL
    //
    //  TABLE 3 proves that three DECLARED codes reach the fallback. This table proves something
    //  different and equally necessary: that the fallback renders whatever value it is handed,
    //  verbatim, rather than recognising known codes and treating everything else as one bucket.
    //
    //  The probes are chosen adversarially rather than arbitrarily:
    //
    //      12345    a plain positive value from nowhere near the algebra.
    //      2        immediately above PREVENT (1), the only positive declared code, so a fallback
    //               that special-cased small positives would show up here.
    //      -34      immediately below E_RETRY (-33). Together with the -33 row in TABLE 3 this
    //               pins that the hole is exactly one value wide and not a range.
    //      -1999    immediately above E_NO_SUPPORT (-2000), inside the wide unnamed gap between
    //               -33 and -2000.
    //      -2002    immediately below E_NO_IMPLEMENTATION (-2001), the value a range-based
    //               "anything past the last arm" implementation would fold into something else.
    //      long.MinValue and long.MaxValue
    //               the extremes of the parameter type. The oracle's parameter is a PowerScript
    //               Long, which is 32-bit, so these two exceed anything the legacy could receive
    //               and no legacy behaviour is claimed for them. What they DO pin is that the port
    //               widened the parameter to a 64-bit long without introducing a truncation, an
    //               overflow or a thousands separator on the way to the text - which matters
    //               because the value reaches the string through the port's own ToString call
    //               [Formatting.cs:L394] rather than through anything the oracle wrote.
    // ------------------------------------------------------------------------------------------
    private static readonly (long Code, string ExpectedText)[] OutOfAlgebraProbes =
    [
        (12345L,          "UNKNOWN (12345)"),                 // no declared code holds this value
        (2L,              "UNKNOWN (2)"),                     // just above PREVENT [retcode.sru:L42]
        (-34L,            "UNKNOWN (-34)"),                   // just below E_RETRY [retcode.sru:L76]
        (-1999L,          "UNKNOWN (-1999)"),                 // just above E_NO_SUPPORT [retcode.sru:L77]
        (-2002L,          "UNKNOWN (-2002)"),                 // just below E_NO_IMPLEMENTATION [retcode.sru:L78]
        (long.MaxValue,   "UNKNOWN (9223372036854775807)"),   // port-widened parameter, no legacy claim
        (long.MinValue,   "UNKNOWN (-9223372036854775808)"),  // port-widened parameter, no legacy claim
    ];

    // ------------------------------------------------------------------------------------------
    //  TABLE 5 of 5 - THE THREE STRINGS THIS FUNCTION CAN NEVER RETURN
    //
    //  Held separately from TABLE 2 because it is used for a different purpose: TABLE 2 checks
    //  three specific inputs, while this one is the subject of the domain-wide unreachability
    //  argument in ThreeAliasSpellingsAreUnreachableOverTheEntireDomain. Keeping the set in one
    //  place means the two tests cannot drift apart.
    // ------------------------------------------------------------------------------------------
    private static readonly string[] UnreachableSpellings =
    [
        "SUCCESS",  // [retcode.sru:L40] - the value 0 renders as "OK" instead
        "ALLOW",    // [retcode.sru:L41] - the value 0 renders as "OK" instead
        "CANCELED", // [retcode.sru:L44] - the value -2 renders as "CANCELLED" instead
    ];

    // ==========================================================================================
    //  MEMBER-DATA PROVIDERS
    //
    //  Each provider projects one of the tables above into a strongly typed TheoryData, so the test
    //  runner names the offending code in the failure output. They must be public and static for
    //  xUnit to reach them, and they are the only public members of this class besides the tests.
    //
    //  Typed TheoryData is used rather than IEnumerable<object[]> deliberately: the compiler then
    //  checks each row's arity and element types against the theory signature, so a row that
    //  acquires or loses a column is a build error rather than a runtime cast failure discovered on
    //  a continuous-integration agent.
    //
    //  The row type is `long`, not `long?`, because every row here carries a value. Widening the
    //  tables to `long?` to accommodate the single null input would put a null in six tables to
    //  serve one case and would weaken the compiler's help everywhere else; the null input has its
    //  own named fact instead. That is also why neither a null-forgiving operator nor a `#pragma`
    //  directive appears in this file: nullability is handled by choosing the right type, not by
    //  silencing the analyzer.
    // ==========================================================================================

    /// <summary>
    /// One row per recognised arm, all 35 of them. [formatretcode.srf:L11-L80]
    /// </summary>
    public static TheoryData<long, string> RecognisedArmRows()
    {
        TheoryData<long, string> rows = [];
        foreach ((long code, string expectedName) in RecognisedArms)
        {
            rows.Add(code, expectedName);
        }

        return rows;
    }

    /// <summary>
    /// Every input that reaches the <c>case else</c> fallback: the three declared codes the oracle
    /// skips, plus seven values from outside the algebra entirely.
    /// [formatretcode.srf:L81-L82]
    /// </summary>
    public static TheoryData<long, string> FallbackRows()
    {
        TheoryData<long, string> rows = [];
        foreach ((_, long code, string expectedText) in UnrecognisedDeclaredCodes)
        {
            rows.Add(code, expectedText);
        }

        foreach ((long code, string expectedText) in OutOfAlgebraProbes)
        {
            rows.Add(code, expectedText);
        }

        return rows;
    }

    /// <summary>
    /// One row per alias-collapsed identifier, carrying both the name that comes back and the
    /// identifier's own unreachable spelling.
    /// [formatretcode.srf:L11-L12, L15-L16] [retcode.sru:L39-L41, L44-L45]
    /// </summary>
    public static TheoryData<string, long, string, string> AliasCollapseRows()
    {
        TheoryData<string, long, string, string> rows = [];
        foreach ((string identifier, long code, string expectedName, string unreachableSpelling) in AliasCollapsedIdentifiers)
        {
            rows.Add(identifier, code, expectedName, unreachableSpelling);
        }

        return rows;
    }

    // ==========================================================================================
    //  THE NON-NULL RENDERING HELPER, AND WHY IT IS AN ASSERTION RATHER THAN A CAST
    // ==========================================================================================
    //  Formatting.FormatRetCode returns string? because the oracle's fallback concatenation
    //  propagates null, so a null CODE yields a null RESULT [Formatting.cs DECISION 4]. Every test
    //  in this file except the null one supplies a code that HAS a value, and for those the result
    //  is never null - each of the 35 arms returns a literal and the fallback concatenates a
    //  rendered long onto a literal, so there is no path to null with a value in hand.
    //
    //  That "never null for a non-null code" claim is itself worth checking rather than assumed
    //  away, which is why this helper asserts instead of using the null-forgiving operator. A `!`
    //  would silence the compiler and prove nothing; Assert.NotNull turns the same line into a
    //  standing check that the nullable return type widened the CONTRACT without widening the
    //  BEHAVIOUR. Every value-bearing call in this file therefore goes through here, so the check
    //  runs once per theory row rather than once in a test of its own.
    // ==========================================================================================

    /// <summary>
    /// Renders a code that HAS a value and asserts the result is not <see langword="null"/> before
    /// handing it back as a non-nullable string.
    /// </summary>
    /// <param name="code">A return code with a value. Never <see langword="null"/> by construction.</param>
    /// <returns>The rendered text.</returns>
    private static string Render(long code)
    {
        string? rendered = Formatting.FormatRetCode(code);

        Assert.NotNull(rendered);

        return rendered;
    }

    // ==========================================================================================
    //  THE 35 RECOGNISED ARMS  [formatretcode.srf:L11-L80]
    // ==========================================================================================

    /// <summary>
    /// Each of the 35 codes the oracle recognises renders as its own constant name, exactly, with no
    /// prefix, no suffix, no parentheses and no numeric value.
    /// </summary>
    /// <remarks>
    /// The expected text is an independent transcription of the string literal on the arm's
    /// <c>return</c> line in <c>formatretcode.srf</c>, not a value derived from the identifier; see
    /// the C-K section of the file header for why that distinction is what makes the row
    /// falsifiable.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RecognisedArmRows))]
    public void RecognisedCodeRendersItsOwnConstantName(long code, string expectedName)
    {
        Assert.Equal(expectedName, Formatting.FormatRetCode(code));
    }

    /// <summary>
    /// A recognised code renders as a bare symbolic name, so it never carries any part of the
    /// fallback grammar. [formatretcode.srf:L11-L80 versus L81-L82]
    /// </summary>
    /// <remarks>
    /// This is the complement of <see cref="UnrecognisedCodeRendersThroughTheUnknownFallback"/> and
    /// it is what keeps the two halves of the function distinguishable. Without it, an
    /// implementation that returned "E_BUSY (-23)" for every code would satisfy a suite that only
    /// ever checked for a substring.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RecognisedArmRows))]
    public void RecognisedCodeRendersWithoutAnyFallbackDecoration(long code, string expectedName)
    {
        string actual = Render(code);

        Assert.Equal(expectedName, actual);
        Assert.DoesNotContain("UNKNOWN", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("(", actual, StringComparison.Ordinal);
        Assert.DoesNotContain(")", actual, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", actual, StringComparison.Ordinal);
    }

    // ==========================================================================================
    //  DEFECT ONE - THE MISSING ARM AND THE FALLBACK  [formatretcode.srf:L81-L82]
    // ==========================================================================================

    /// <summary>
    /// Every input the arm table does not recognise renders as <c>UNKNOWN (n)</c> with the value
    /// spelled out verbatim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B DEFECT PINNED AS CORRECT BEHAVIOUR. [formatretcode.srf:L75-L82] The
    /// <c>E_RETRY</c> row is the reason this test exists. The oracle's arms run contiguously from
    /// <c>E_INVALID_ARGUMENT</c> (-3) to <c>E_SQL_BIND_ARG_FAILED</c> (-32) at
    /// <c>formatretcode.srf:L75-L76</c> and then jump straight to <c>E_NO_SUPPORT</c> (-2000) at
    /// <c>L77-L78</c>, so <c>E_RETRY</c> (-33, <c>retcode.sru:L76</c>) has NO ARM and reaches the
    /// <c>case else</c>. That omission is the legacy's, it is reproduced deliberately, and ADDING
    /// THE MISSING ARM WOULD BE A BEHAVIOURAL REGRESSION rather than a fix. If this test is failing
    /// on the <c>E_RETRY</c> row, the correct response is to remove the arm somebody added to
    /// <see cref="Formatting.FormatRetCode"/>, not to change this expectation.
    /// </para>
    /// <para>
    /// The same applies to <c>PREVENT</c> (1) and <c>UNKNOWN</c> (-4000), which the oracle also
    /// skips. The <c>UNKNOWN</c> row is a lexical coincidence and not a mapping: the word in the
    /// output is the fallback's own literal, so "UNKNOWN (-4000)" is the fallback doing its job.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(FallbackRows))]
    public void UnrecognisedCodeRendersThroughTheUnknownFallback(long code, string expectedText)
    {
        Assert.Equal(expectedText, Formatting.FormatRetCode(code));
    }

    /// <summary>
    /// The fallback carries the requested value, so no two unrecognised codes can render alike.
    /// </summary>
    /// <remarks>
    /// Equality against the literal in <see cref="UnrecognisedCodeRendersThroughTheUnknownFallback"/>
    /// already forces this, but stating it separately pins the PROPERTY rather than the ten
    /// individual strings: a fallback that had degraded to a constant "UNKNOWN" - dropping the value
    /// and thereby destroying the diagnostic that the whole fallback exists to provide - would fail
    /// here by name.
    /// </remarks>
    [Fact]
    public void FallbackRenderingsAreAllDistinctBecauseEachCarriesItsValue()
    {
        HashSet<string> renderings = new(StringComparer.Ordinal);

        foreach ((string identifier, long code, _) in UnrecognisedDeclaredCodes)
        {
            Assert.True(
                renderings.Add(Render(code)),
                $"{identifier} rendered a fallback string that another code had already produced, so the fallback has stopped carrying the value.");
        }

        foreach ((long code, _) in OutOfAlgebraProbes)
        {
            Assert.True(
                renderings.Add(Render(code)),
                $"The probe {code} rendered a fallback string that another code had already produced, so the fallback has stopped carrying the value.");
        }
    }

    /// <summary>
    /// The fallback text is byte for byte what the oracle spells: the word <c>UNKNOWN</c>, ONE
    /// space, an opening parenthesis, the value, a closing parenthesis. [formatretcode.srf:L82]
    /// </summary>
    /// <remarks>
    /// Equality against a literal already pins this, so the value of decomposing the string is that
    /// each element of the grammar becomes individually auditable and individually reportable. A
    /// well-meant tidy of the fallback into <c>UNKNOWN(-33)</c>, <c>UNKNOWN ( -33 )</c>,
    /// <c>UNKNOWN [-33]</c> or <c>UNKNOWN: -33</c> fails here on the specific element that moved,
    /// rather than on one opaque string comparison. The spacing is not cosmetic: this text is
    /// written into log records and into stored characterization recordings, so a changed space is a
    /// changed artifact.
    /// </remarks>
    [Fact]
    public void FallbackTextFollowsTheOracleGrammarExactly()
    {
        string actual = Render(RetCode.E_RETRY);

        Assert.Equal("UNKNOWN (-33)", actual);

        // The grammar, element by element.
        Assert.Equal("UNKNOWN", actual[..7]);  // the literal word, upper case, no trailing colon
        Assert.Equal(' ', actual[7]);          // EXACTLY one space, and it is before the parenthesis
        Assert.Equal('(', actual[8]);          // round parenthesis, not a square bracket
        Assert.Equal("-33", actual[9..^1]);    // the value, with a leading minus and no padding
        Assert.Equal(')', actual[^1]);         // closing parenthesis, and nothing after it

        // Shapes the oracle does not produce.
        Assert.DoesNotContain(":", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("[", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("0x", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("( ", actual, StringComparison.Ordinal);
        Assert.DoesNotContain(" )", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("E_RETRY", actual, StringComparison.Ordinal);
    }

    /// <summary>
    /// The fallback renders its value with the invariant culture, so the same code produces the same
    /// text on every host. [Formatting.cs DECISION 5, reproducing formatretcode.srf:L82]
    /// </summary>
    /// <remarks>
    /// This is a determinism guard rather than a legacy-behaviour claim. Paired legacy and target
    /// characterization recordings are the parity evidence for the whole migration, and the
    /// Golden-Master technique's one hard prerequisite is repeatability. A host whose culture
    /// defines the negative sign as U+2212 MINUS SIGN, or which supplies non-ASCII native digits,
    /// would silently turn "UNKNOWN (-33)" into text that a stored recording cannot match - and the
    /// divergence would look exactly like a behavioural regression while having nothing to do with
    /// the port. The culture below is deliberately hostile in that specific way, and it is restored
    /// in a <c>finally</c> so a failure here cannot leak into another test.
    /// </remarks>
    [Fact]
    public void FallbackRenderingIgnoresTheAmbientCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo hostile = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        hostile.NumberFormat.NegativeSign = "MINUS";
        hostile.NumberFormat.NumberGroupSeparator = "_";
        hostile.NumberFormat.NumberGroupSizes = [3];

        try
        {
            CultureInfo.CurrentCulture = hostile;

            Assert.Equal("UNKNOWN (-33)", Formatting.FormatRetCode(RetCode.E_RETRY));
            Assert.Equal("UNKNOWN (-4000)", Formatting.FormatRetCode(RetCode.UNKNOWN));
            Assert.Equal("UNKNOWN (12345)", Formatting.FormatRetCode(12345L));

            // A recognised arm returns a literal, so it cannot be culture-sensitive at all; assert
            // it anyway so the whole public surface is covered by the guard rather than half of it.
            Assert.Equal("E_SQL_BIND_ARG_FAILED", Formatting.FormatRetCode(RetCode.E_SQL_BIND_ARG_FAILED));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ==========================================================================================
    //  DEFECT TWO - THE ALIAS COLLAPSE  [formatretcode.srf:L11-L12, L15-L16]
    // ==========================================================================================

    /// <summary>
    /// An aliased code renders as the name on the FIRST SWITCH ARM for its value, never as the alias
    /// the caller named.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B DEFECT PINNED AS CORRECT BEHAVIOUR. [formatretcode.srf:L11-L12, L15-L16] and
    /// [retcode.sru:L39-L41, L44-L45] <c>retcode.sru</c> declares three names for 0 (<c>OK</c> L39,
    /// <c>SUCCESS</c> L40, <c>ALLOW</c> L41) and two for -2 (<c>CANCELED</c> L44,
    /// <c>CANCELLED</c> L45). A PowerScript <c>choose case</c> takes the first matching arm, so the
    /// <c>OK</c> arm wins for 0 and the <c>CANCELLED</c> arm wins for -2. THE THREE ALIAS NAMES
    /// <c>SUCCESS</c>, <c>ALLOW</c> AND <c>CANCELED</c> ARE THEREFORE UNREACHABLE OUTPUT, BY DESIGN,
    /// and no caller has ever seen one. This looks like dead code and must not be "completed".
    /// </para>
    /// <para>
    /// The rule is the FIRST SWITCH ARM's spelling, NOT the first-declared alias, and the -2 row is
    /// where those two differ: <c>CANCELED</c> is declared first yet <c>CANCELLED</c> is what comes
    /// back. Anyone reasoning from "the first alias wins" predicts this row backwards.
    /// </para>
    /// <para>
    /// C# cannot express the shadowing structurally - two <c>case</c> labels with the same constant
    /// value is CS0152 - so the defect is only observable from outside, which is what this test
    /// does. The declaration ORDER that produces it is pinned separately by <c>RetCodeTests</c>, so
    /// between the two suites both halves of the cause are asserted rather than assumed.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AliasCollapseRows))]
    public void AliasedCodeRendersTheFirstArmSpellingAndNeverItsOwn(
        string identifier,
        long code,
        string expectedName,
        string unreachableSpelling)
    {
        string actual = Render(code);

        Assert.Equal(expectedName, actual);
        Assert.NotEqual(unreachableSpelling, actual);

        // TABLE INTEGRITY, AND IT GUARDS THE EXACT MISTAKE THE HEADER WARNS ABOUT. A row's
        // unreachable spelling must be that row's OWN identifier: SUCCESS cannot reach "SUCCESS",
        // ALLOW cannot reach "ALLOW", CANCELED cannot reach "CANCELED". Someone applying the loose
        // "the first alias wins" rule would edit the -2 row's fourth column to "CANCELLED" - which
        // is the one string that value CAN produce - and this assertion is what catches that edit
        // before the table starts asserting the opposite of the truth.
        Assert.Equal(identifier, unreachableSpelling);
    }

    /// <summary>
    /// The strings <c>SUCCESS</c>, <c>ALLOW</c> and <c>CANCELED</c> are unreachable over the
    /// function's ENTIRE domain, not merely for the three inputs that would most obviously have
    /// produced them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B DEFECT PINNED AS CORRECT BEHAVIOUR. [formatretcode.srf:L11-L82]
    /// </para>
    /// <para>
    /// Sweeping every <c>long</c> is neither possible nor necessary, and the claim is proved instead
    /// by partitioning the range of the function, which has exactly three arms of its own:
    /// </para>
    /// <list type="number">
    /// <item>a recognised code returns one of the 35 arm literals - so it is enough to show that no
    /// arm literal equals any of the three spellings;</item>
    /// <item>an unrecognised code returns a fallback string, and every fallback string begins
    /// <c>UNKNOWN (</c> while none of the three spellings does;</item>
    /// <item>a null code returns <see langword="null"/>, which is none of the three.</item>
    /// </list>
    /// <para>
    /// Those three checks are exhaustive over every possible input, so this fact is a proof rather
    /// than a sample. The three concrete alias inputs are asserted as well, because they are the
    /// ones a reader will look for.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThreeAliasSpellingsAreUnreachableOverTheEntireDomain()
    {
        Assert.Equal(3, UnreachableSpellings.Length);

        foreach (string unreachable in UnreachableSpellings)
        {
            // Case 1 - no recognised arm returns this spelling.
            foreach ((long code, string expectedName) in RecognisedArms)
            {
                Assert.NotEqual(unreachable, expectedName);
                Assert.NotEqual(unreachable, Formatting.FormatRetCode(code));
            }

            // Case 2 - no fallback string can equal this spelling, because every fallback string
            // starts with the oracle's literal prefix and none of the three spellings does.
            Assert.DoesNotContain("UNKNOWN (", unreachable, StringComparison.Ordinal);
            foreach ((_, long code, _) in UnrecognisedDeclaredCodes)
            {
                Assert.StartsWith("UNKNOWN (", Render(code), StringComparison.Ordinal);
                Assert.NotEqual(unreachable, Formatting.FormatRetCode(code));
            }

            foreach ((long code, _) in OutOfAlgebraProbes)
            {
                Assert.StartsWith("UNKNOWN (", Render(code), StringComparison.Ordinal);
                Assert.NotEqual(unreachable, Formatting.FormatRetCode(code));
            }

            // Case 3 - a null code returns null, and null is not one of the three spellings.
            Assert.NotNull(unreachable);
            Assert.NotEqual(unreachable, Formatting.FormatRetCode(null));
        }

        // And the three inputs a reader will reach for first.
        Assert.Equal("OK", Formatting.FormatRetCode(RetCode.SUCCESS));
        Assert.Equal("OK", Formatting.FormatRetCode(RetCode.ALLOW));
        Assert.Equal("CANCELLED", Formatting.FormatRetCode(RetCode.CANCELED));
    }

    // ==========================================================================================
    //  THE ARM-COUNT AUDIT, EXPRESSED BEHAVIOURALLY
    //
    //  "Formatting.cs implements exactly 35 arms and no more" is a statement about source text, and
    //  a test cannot read source text - the .cs is not shipped, and reading the oracle at runtime is
    //  forbidden by C-C and impossible in a container anyway, since .dockerignore excludes
    //  ws_objects/ from the build context. The two facts below assert the same property from the
    //  outside instead, over the closed set of 41 codes that retcode.sru:L39-L79 declares, which is
    //  where a 36th arm could only ever come from.
    // ==========================================================================================

    /// <summary>
    /// The 41 declared PowerFramework codes partition into exactly 35 that render as their own name
    /// and 6 that do not. [retcode.sru:L39-L79 against formatretcode.srf:L11-L82]
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>retcode.sru:L39-L79</c> is 41 consecutive constant declarations and the oracle's arm table
    /// covers 35 of them, so the six that fall outside it are not a rounding error - they are the
    /// two defects, enumerated. Three are aliases shadowed by an earlier arm and three are simply
    /// skipped, and the arithmetic 35 + 3 + 3 = 41 is what proves the tables above are complete
    /// rather than merely long.
    /// </para>
    /// <para>
    /// This fact is the guard against a 36th arm. Adding one for <c>E_RETRY</c> - by far the most
    /// likely well-intentioned edit - moves that code from the six to the 35 and turns
    /// <see cref="UnrecognisedCodeRendersThroughTheUnknownFallback"/> red; if the tables were then
    /// "corrected" to match, this fact would go red as well, because a row cannot be in two of the
    /// three groups and the disjointness is asserted below.
    /// </para>
    /// </remarks>
    [Fact]
    public void FortyOneDeclaredCodesPartitionIntoThirtyFiveArmsAndSixNonArms()
    {
        Assert.Equal(35, RecognisedArms.Length);
        Assert.Equal(3, AliasCollapsedIdentifiers.Length);
        Assert.Equal(3, UnrecognisedDeclaredCodes.Length);
        Assert.Equal(
            41,
            RecognisedArms.Length + AliasCollapsedIdentifiers.Length + UnrecognisedDeclaredCodes.Length);

        // The 35 recognised codes are 35 DISTINCT values. This is what makes the alias collapse the
        // only shadowing in the function: if two arms shared a value, a second string would be
        // unreachable and TABLE 5 would be incomplete.
        HashSet<long> recognisedCodes = [];
        HashSet<string> recognisedNames = new(StringComparer.Ordinal);
        foreach ((long code, string expectedName) in RecognisedArms)
        {
            Assert.True(recognisedCodes.Add(code), $"The value {code} appears twice among the recognised arms.");
            Assert.True(recognisedNames.Add(expectedName), $"The name {expectedName} appears twice among the recognised arms.");
        }

        // The three groups are disjoint by VALUE for the skipped codes, which is the sense in which
        // PREVENT, E_RETRY and UNKNOWN are genuinely unrecognised rather than aliases.
        foreach ((string identifier, long code, _) in UnrecognisedDeclaredCodes)
        {
            Assert.DoesNotContain(code, recognisedCodes);
            Assert.DoesNotContain(identifier, recognisedNames);
        }

        // The alias identifiers, by contrast, SHARE a value with a recognised arm - that sharing is
        // the defect - while their own spellings are absent from the arm names.
        foreach ((string identifier, long code, string expectedName, _) in AliasCollapsedIdentifiers)
        {
            Assert.Contains(code, recognisedCodes);
            Assert.Contains(expectedName, recognisedNames);
            Assert.DoesNotContain(identifier, recognisedNames);
        }
    }

    /// <summary>
    /// Every declared PowerFramework code renders as something, and each rendering is either one of
    /// the 35 arm names or a fallback string - there is no third shape.
    /// </summary>
    /// <remarks>
    /// The closing half of the audit. Together with
    /// <see cref="FortyOneDeclaredCodesPartitionIntoThirtyFiveArmsAndSixNonArms"/> this walks the
    /// whole declared code space and classifies each result, so an implementation that grew an arm,
    /// lost an arm, or started decorating its output is caught here regardless of which of the 41
    /// codes it happened to affect.
    /// </remarks>
    [Fact]
    public void EveryDeclaredCodeRendersAsAnArmNameOrAFallbackAndNothingElse()
    {
        HashSet<string> armNames = new(StringComparer.Ordinal);
        foreach ((_, string expectedName) in RecognisedArms)
        {
            armNames.Add(expectedName);
        }

        foreach ((long code, string expectedName) in RecognisedArms)
        {
            string actual = Render(code);
            Assert.Equal(expectedName, actual);
            Assert.Contains(actual, armNames);
        }

        foreach ((_, long code, string expectedName, _) in AliasCollapsedIdentifiers)
        {
            string actual = Render(code);
            Assert.Equal(expectedName, actual);
            Assert.Contains(actual, armNames);
        }

        foreach ((_, long code, string expectedText) in UnrecognisedDeclaredCodes)
        {
            string actual = Render(code);
            Assert.Equal(expectedText, actual);
            Assert.DoesNotContain(actual, armNames);
            Assert.StartsWith("UNKNOWN (", actual, StringComparison.Ordinal);
            Assert.EndsWith(")", actual, StringComparison.Ordinal);
        }
    }

    // ==========================================================================================
    //  THE NULL CODE, AND THE ONE PLACE THE RESULT IS NULL
    // ==========================================================================================

    /// <summary>
    /// A null code renders as <see langword="null"/>, which is what the oracle's own fallback
    /// concatenation produces. This is a LEGACY BEHAVIOUR claim, derived from the oracle's readable
    /// body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle's parameter is <c>readonly long rtcode</c> [formatretcode.srf:L7], and PowerScript
    /// has null for value types, so a caller can pass a null long and the plan preserves that rather
    /// than collapsing it. Traced through the oracle's own body, three documented PowerScript rules
    /// settle the answer with nothing left to choose:
    /// </para>
    /// <list type="number">
    ///   <item>no arm between <c>:L11</c> and <c>:L80</c> can match a null, so control reaches
    ///     <c>case else</c> at <c>:L81-L82</c>;</item>
    ///   <item><c>String(null)</c> yields null; and</item>
    ///   <item>concatenating a null string with <c>+</c> yields null, so the whole
    ///     <c>"UNKNOWN (" + String(rtCode) + ")"</c> expression is null.</item>
    /// </list>
    /// <para>
    /// So the legacy returns a NULL STRING - not the text <c>"UNKNOWN ()"</c>, and not the empty
    /// string. <see cref="Formatting.FormatRetCode"/> returns <see langword="null"/> to match, which
    /// is why its return type is <c>string?</c>; see its DECISION 4.
    /// </para>
    /// <para>
    /// AN EARLIER REVISION OF THIS TEST ASSERTED <c>string.Empty</c> HERE, having derived the null
    /// in its own remarks and then declined to assert it. That is the worst of the available
    /// positions: it made a known semantic change permanently green, and it would have failed the
    /// moment the production code was corrected - so the test would have read as the authority and
    /// the correction as the regression. The empty string is also exactly the null flattening
    /// AAP 0.4.5.4 forbids, one type over from the integer case it names. The cost the old reasoning
    /// was avoiding turned out not to exist either: <c>FormatRetCode</c> has no caller anywhere in
    /// this repository outside this suite, so widening the return type propagated a null check to
    /// nothing at all.
    /// </para>
    /// <para>
    /// THE EXACT BOUNDARY OF THE CLAIM, so it is not overstated: no call site in the corpus passes a
    /// null code, so this is what the oracle WOULD return rather than a recorded observation of it
    /// returning that. For a function whose body is readable, a trace through three documented
    /// language rules is the strongest evidence available short of running the legacy - which is
    /// why the derivation is spelled out above rather than replaced by a bare assertion.
    /// </para>
    /// </remarks>
    [Fact]
    public void FormatRetCodeNullPropagatesNullThroughTheFallbackConcatenation()
    {
        long? absentCode = null;

        string? actual = Formatting.FormatRetCode(absentCode);

        Assert.Null(actual);

        // Specifically NOT the shapes a reader might reach for instead. Each is a DIFFERENT answer
        // that the oracle does not produce, and each has been a real temptation: the empty string is
        // what this file used to assert, "UNKNOWN ()" is what a fallback that ignored the null
        // propagation would render, and "OK" is what treating an absent code as a zero would give.
        Assert.NotEqual(string.Empty, actual);
        Assert.NotEqual("UNKNOWN ()", actual);
        Assert.NotEqual("UNKNOWN (0)", actual);
        Assert.NotEqual("OK", actual);
    }

    /// <summary>
    /// Null is the ONLY input that renders as <see langword="null"/>: every code that has a value
    /// renders as a non-null string, across an arm name, an alias collapse and the fallback alike.
    /// </summary>
    /// <remarks>
    /// The complement of the fact above, and what stops the nullable return type from being read as
    /// "this may return null at any time". The 35 arms each return a string literal and the fallback
    /// concatenates a rendered long onto a literal, so there is no path to null with a value in hand
    /// - and this walks the whole declared code space plus the out-of-algebra probes to say so.
    /// </remarks>
    [Fact]
    public void EveryCodeWithAValueRendersANonNullNonEmptyString()
    {
        foreach ((long code, _) in RecognisedArms)
        {
            Assert.NotNull(Formatting.FormatRetCode(code));
            Assert.NotEmpty(Render(code));
        }

        foreach ((_, long code, _) in UnrecognisedDeclaredCodes)
        {
            Assert.NotNull(Formatting.FormatRetCode(code));
            Assert.NotEmpty(Render(code));
        }

        foreach ((long code, _) in OutOfAlgebraProbes)
        {
            Assert.NotNull(Formatting.FormatRetCode(code));
            Assert.NotEmpty(Render(code));
        }
    }

    /// <summary>
    /// A code supplied through a <see cref="Nullable{T}"/> that HAS a value renders exactly as the
    /// bare value does, so the port's widened parameter adds no second behaviour.
    /// </summary>
    /// <remarks>
    /// The complement of the null fact. The parameter widening is meant to admit null and change
    /// nothing else, and this walks the whole declared code space to confirm that the implicit
    /// conversion at the call site and an explicit <c>long?</c> are indistinguishable - across an arm
    /// name, an alias collapse and a fallback alike.
    /// </remarks>
    [Fact]
    public void NullableCodeWithAValueRendersIdenticallyToTheBareValue()
    {
        foreach ((long code, _) in RecognisedArms)
        {
            long? wrapped = code;
            Assert.Equal(Formatting.FormatRetCode(code), Formatting.FormatRetCode(wrapped));
        }

        foreach ((_, long code, _, _) in AliasCollapsedIdentifiers)
        {
            long? wrapped = code;
            Assert.Equal(Formatting.FormatRetCode(code), Formatting.FormatRetCode(wrapped));
        }

        foreach ((_, long code, _) in UnrecognisedDeclaredCodes)
        {
            long? wrapped = code;
            Assert.Equal(Formatting.FormatRetCode(code), Formatting.FormatRetCode(wrapped));
        }
    }

    // ==========================================================================================
    //  DETERMINISM AND PURITY
    // ==========================================================================================

    /// <summary>
    /// The function is a pure function of its argument: the same code renders the same text every
    /// time, on every call.
    /// </summary>
    /// <remarks>
    /// Cheap to assert and load bearing for parity. Paired characterization recordings are only
    /// comparable if repeated calls agree, so a rendering that ever varied - by caching, by holding
    /// state, or by consulting anything outside its argument - would invalidate the evidence rather
    /// than merely surprise a caller.
    /// </remarks>
    [Fact]
    public void RenderingIsRepeatableForEveryDeclaredCode()
    {
        foreach ((long code, _) in RecognisedArms)
        {
            Assert.Equal(Formatting.FormatRetCode(code), Formatting.FormatRetCode(code));
        }

        foreach ((_, long code, _) in UnrecognisedDeclaredCodes)
        {
            Assert.Equal(Formatting.FormatRetCode(code), Formatting.FormatRetCode(code));
        }

        Assert.Equal(Formatting.FormatRetCode(null), Formatting.FormatRetCode(null));
    }
}
