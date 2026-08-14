// ==============================================================================================
//  Formatting - the symbolic return-code renderer and the framework's composite formatter
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.shared.pbl.src/formatretcode.srf (85 lines, readable PowerScript)
//                 ws_objects/pfw.common.pbl.src/sprintf.srf       (30 lines, DECLARATION ONLY)
//                 ws_objects/pfw.shared.pbl.src/retcode.sru       (the constant values, read for
//                                                                  their numeric values only)
//
//  ORACLE STATUS  All three sources are READ ONLY. They are the behavioural oracle for parity
//                 testing, never an edit target, and nothing else in the repository can adjudicate
//                 a disagreement about what either member does. Every behaviour reproduced below
//                 therefore carries the source line locator it was taken from.
//
//  TWO VERY DIFFERENT KINDS OF PORT LIVE IN THIS FILE, AND CONFUSING THEM WOULD BE A MISTAKE
//  --------------------------------------------------------------------------------------------
//      FormatRetCode   `formatretcode.srf:L3` declares
//                      `global type formatretcode from function_object` with NO `native` clause,
//                      so the PowerScript body is present and readable at L10-L83. This half is a
//                      straight TRANSCRIPTION with nothing inferred, and it carries three
//                      reproduced defects that a well-meaning reader would otherwise "fix".
//
//      Sprintf         `sprintf.srf:L3` declares
//                      `global type sprintf from function_object native "pfw.dll"`.
//                      The body lives inside the CLOSED pfw.dll and there is no C++ source
//                      anywhere in this repository, so nothing about its implementation can be
//                      read. The ONLY in-repo evidence of its contract is the 21 forward
//                      prototypes at L7-L27 plus the 78 call sites across ws_objects/. This half
//                      is therefore a RECONSTRUCTION FROM EVIDENCE, and every place where the
//                      evidence runs out is labelled below as a CHOICE rather than presented as
//                      verified legacy behaviour, so a later characterization run against the
//                      behavioural oracle can correct it without anyone having to re-derive the
//                      question.
//
//  WHY THESE TWO SHARE ONE CLASS
//  --------------------------------------------------------------------------------------------
//  PowerBuilder has no namespaces and no import statements: a *.srf global function lives in one
//  flat global namespace whose symbol resolution follows the ordering of the library list in the
//  target file. There is consequently no import statement to rewrite here, only a namespace to
//  create. The plan's object-kind mapping (section 0.4.5.2) turns each *.srf global function into a
//  `static` method on a role-named static class, and names this pair explicitly as
//  `Formatting.FormatRetCode` and `Formatting.Sprintf`. `Formatting` is the role they share: each
//  one's entire job is to render a value as text. They come from two different legacy libraries
//  (pfw.shared and pfw.common) and had no relationship in the legacy at all; they are grouped by
//  role, not by lineage.
//
//  EVERYTHING IN THIS FILE IS CULTURE-INVARIANT, AND THAT IS A HARD REQUIREMENT
//  --------------------------------------------------------------------------------------------
//  Section 0.6.7 of the plan makes paired legacy/target characterization recordings the parity
//  evidence for the whole migration, and the Golden-Master technique's one hard prerequisite is
//  repeatability. A number, a date or a negative sign that rendered differently on a host with a
//  different ambient culture would make two recordings diverge for a reason that has nothing to do
//  with the port, and the divergence would look exactly like a behavioural regression.
//
//  Concretely: every numeric and date rendering below passes CultureInfo.InvariantCulture
//  explicitly, and `CultureInfo.CurrentCulture` appears nowhere in this file, directly or by
//  omission. Every character and string comparison is ordinal. There is no code path on which the
//  ambient culture can influence the output.
//
//  NO PERFORMANCE OBJECTIVE IS ASSERTED ANYWHERE IN THIS FILE
//  --------------------------------------------------------------------------------------------
//  The repository publishes no service level agreement, no latency budget and no throughput
//  target, so none may be claimed or used to justify a design choice (plan section 0.8.5). The
//  implementation below is written to be obvious and auditable against its two sources. Where it
//  happens to avoid an allocation, that is described as what it does and is not offered as a
//  performance property.
//
//  GOVERNING CONSTRAINTS, AND HOW THIS FILE SATISFIES EACH
//  --------------------------------------------------------------------------------------------
//  There are NO user-specified rules for this project. The rules document was retrieved and it
//  contains exactly one statement: that no user rules were provided. No rule is therefore invented
//  or inferred here, and the absence is not treated as licence to lower the bar. The binding
//  constraints in their place are the plan's own constraint inventory (section 0.7.3) and its
//  enterprise-standard baseline (section 0.7.2):
//
//      C-A   Shared IMPLEMENTATION consumed in process, never a cross-service channel. Both public
//            members are pure functions of their arguments: no I/O, no logging, no clock, no
//            static mutable state, no localization lookup and no reference to the Contracts
//            project. In particular the sibling Localization library calls INTO Sprintf; this file
//            deliberately holds no back-reference to it, so no dependency cycle can form.
//      C-B   No behaviour improvements, and documented defects are replicated rather than
//            corrected. FormatRetCode keeps NO arm for E_RETRY, keeps "SUCCESS", "ALLOW" and
//            "CANCELED" unreachable, keeps PREVENT and UNKNOWN falling through to the fallback,
//            and keeps the fallback text exactly as the oracle spells it. There is no reverse
//            parser, no added arm and no "improved" message.
//      C-C   The legacy tree is read only and is the oracle. All three sources were read in full
//            and none was modified; every reproduced behaviour cites its locator.
//      C-K   Every technology-specific decision is documented at its point of reproduction, as the
//            numbered DECISION blocks below. This constraint carries more weight here than
//            anywhere else in the project precisely because Sprintf is a reconstruction.
//      0.4.5.2  A *.srf global function becomes a static method on a role-named static class,
//            which is why these two unrelated helpers share this class.
//      0.4.5.4  Nullable value semantics are preserved rather than collapsed on BOTH sides of
//            FormatRetCode: it takes `long?` because PowerScript has null for value types and the
//            legacy caller can pass one, and it RETURNS `string?` because the oracle's fallback
//            concatenates `String(rtCode)` and PowerScript propagates null through both. Mapping
//            either end onto a non-null stand-in would be the null flattening this clause forbids.
//      0.6.5 The native-binding matrix classifies the pfw.dll formatting primitives SUBSTITUTE
//            with "composite formatting", so a managed reconstruction is the sanctioned outcome
//            and reverse engineering the binary is explicitly not expected.
//      0.6.7 Determinism, as set out above.
//
//  This file declares NO SCREAMING_SNAKE and no underscore-bearing identifier. That is a build
//  requirement rather than a style preference: Directory.Build.props sets TreatWarningsAsErrors,
//  and the root .editorconfig scopes its CA1707 and IDE1006 suppressions to the individual files on
//  its BAND 3 roster - the single source of truth for that list - that genuinely carry preserved
//  legacy constant identifiers. This file is deliberately not
//  one of them, so a SCREAMING_SNAKE declaration here would be a build ERROR. The return-code
//  identifiers are REFERENCED from RetCode and never redeclared; CA1707 reports declarations only,
//  never uses, so referencing them needs no suppression. The string literals "E_INVALID_ARGUMENT"
//  and friends are data, not identifiers, and are unaffected.
// ==============================================================================================

using System.Globalization;
using System.Text;

namespace PowerFramework.Shared.Kernel;

/// <summary>
/// Text rendering helpers ported from the legacy PowerBuilder global functions
/// <c>formatretcode</c> and <c>sprintf</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every member is a pure function of its arguments. Nothing in this class performs I/O, logs,
/// reads a clock, or holds mutable state, so every member is safe to call concurrently from any
/// number of threads.
/// </para>
/// <para>
/// All formatting performed by this class uses <see cref="CultureInfo.InvariantCulture"/> and all
/// comparison is ordinal. No member consults the ambient culture, which is what allows a legacy
/// characterization recording and a target recording to be compared without the host locale
/// influencing the result.
/// </para>
/// </remarks>
public static class Formatting
{
    // ==========================================================================================
    // REGION 1 - FormatRetCode
    // Ported from ws_objects/pfw.shared.pbl.src/formatretcode.srf
    //     L3-L4    `global type formatretcode from function_object` - NO `native` clause, so this
    //              is a readable PowerScript body and a straight logic port
    //     L6-L8    the single forward prototype
    //              `global function string formatretcode (readonly long rtcode)`
    //     L10      `choose case rtCode`
    //     L11-L80  exactly 35 recognised arms, two lines each, each returning its own constant
    //              identifier spelling as a string literal
    //     L81-L82  `case else` -> `return "UNKNOWN (" + String(rtCode) + ")"`
    //     L83-L84  `end choose` / `end function`
    //
    // The arm count is not decorative. It is the audit that the transcription is complete: L11
    // through L80 is 70 lines, every arm occupies exactly two of them, and 70 / 2 = 35. The order
    // below is the oracle's own order, unsorted and unregrouped, so the two can be read side by
    // side.
    // ==========================================================================================
    //
    // DECISION 1 - THREE GAPS IN THE ARM TABLE ARE REPRODUCED, NOT REPAIRED
    // ------------------------------------------------------------------------------------------
    // The oracle recognises 35 of the 41 PowerFramework return codes that retcode.sru declares.
    // Six declared codes reach the `case else` fallback instead, and three of those are worth
    // naming because each one looks like an oversight and each one is behaviour that consumers,
    // log records and characterization recordings already depend on:
    //
    //     E_RETRY   (-33)   retcode.sru:L76 declares it. The arms run contiguously from
    //                       E_INVALID_ARGUMENT (-3) to E_SQL_BIND_ARG_FAILED (-32) at
    //                       formatretcode.srf:L75-L76 and then jump straight to E_NO_SUPPORT
    //                       (-2000) at L77-L78, skipping -33 entirely. So
    //                       FormatRetCode(RetCode.E_RETRY) renders "UNKNOWN (-33)".
    //                       DO NOT ADD THE MISSING ARM. Adding it is the single most likely
    //                       well-intentioned change to this file and it would be a behavioural
    //                       regression under C-B.
    //     PREVENT   (1)     retcode.sru:L42 declares it and the whole tri-state return algebra
    //                       turns on it, yet the oracle has no arm for it either, so it renders
    //                       "UNKNOWN (1)". This gap is not named in the migration brief; it was
    //                       found by diffing the 35 arms against the 41 declared codes and is
    //                       recorded here so the finding is not lost.
    //     UNKNOWN   (-4000) retcode.sru:L79 declares it and the oracle has no arm for it, so it
    //                       renders "UNKNOWN (-4000)" - the word followed by its own number. That
    //                       reads like a bug and is simply the fallback doing its job.
    //
    // The remaining three unrecognised codes are SUCCESS, ALLOW and CANCELED, which are aliases
    // and are covered by DECISION 2.
    //
    // DECISION 2 - THE ALIAS COLLAPSE MAKES THREE STRINGS UNREACHABLE, AND THIS COMMENT IS HOW
    //              THAT DEFECT IS PRESERVED
    // ------------------------------------------------------------------------------------------
    // retcode.sru declares three names for 0 and two names for -2:
    //
    //     OK = SUCCESS = ALLOW = 0        retcode.sru:L39-L41
    //     CANCELED = CANCELLED = -2       retcode.sru:L44-L45
    //
    // In PowerScript a `choose case` evaluates its arms in order, so the OK arm at
    // formatretcode.srf:L11-L12 wins for the value 0 and the CANCELLED arm at L15-L16 wins for the
    // value -2. The consequence is that the strings "SUCCESS", "ALLOW" and "CANCELED" can NEVER be
    // returned by this function, and no caller has ever seen one.
    //
    // C# cannot express that shadowing structurally: a `switch` on `long` rejects two case labels
    // with the same constant value with CS0152, so `case RetCode.SUCCESS:` next to
    // `case RetCode.OK:` would not even compile, and there is nothing to "attempt" here. The
    // preservation of this defect IS this comment plus the deliberate absence of those three
    // string literals from the file. A reader who adds them, or who reaches for a dictionary keyed
    // by name to "cover all the constants", reintroduces output the legacy never produced.
    //
    // The corresponding unit tests assert the shadowing from the outside, which is the only place
    // it is observable: FormatRetCode(RetCode.SUCCESS) and FormatRetCode(RetCode.ALLOW) must both
    // return "OK", and FormatRetCode(RetCode.CANCELED) must return "CANCELLED".
    //
    // DECISION 3 - A `switch` STATEMENT WITH ONE `return` PER ARM, MIRRORING `choose case`
    // ------------------------------------------------------------------------------------------
    // A switch expression would be more idiomatic C# and would be shorter. A switch statement is
    // used instead because it reproduces the oracle's shape line for line, which is what makes the
    // mandated exhaustive audit possible: a script can extract every (constant, string) pair from
    // formatretcode.srf and every `case RetCode.X:` / `return "X";` pair from this file and diff
    // the two lists directly. That audit is the evidence that all 35 arms survived, and it is
    // worth more here than brevity. The root .editorconfig pins
    // csharp_style_prefer_switch_expression to `silent` precisely so that choosing the shape of
    // the original can never become a build gate.
    //
    // DECISION 4 - A NULL CODE RENDERS AS NULL, WHICH IS THE ORACLE'S OWN BEHAVIOUR
    // ------------------------------------------------------------------------------------------
    // The oracle declares `readonly long rtcode`, and PowerScript has null for value types, so a
    // caller can pass a null long. Traced through the oracle, a null falls to the `case else` arm
    // at L81-L82, `String(null)` yields null, and PowerScript string concatenation with null
    // yields null - so the legacy's observable result for a null input is a NULL STRING, not the
    // text "UNKNOWN ()" and not the empty string.
    //
    // That trace is short, complete and mechanical, so it is EVIDENCE rather than an inference:
    // every step of it is a documented PowerScript rule, and there is no arm in formatretcode.srf
    // that could intercept a null before the fallback reaches `String`. The return type is
    // therefore `string?` and a null code returns null.
    //
    // RETURNING THE EMPTY STRING HERE is the tempting alternative, on the reasoning that a
    // null-returning `string` pushes a null check onto every call site. That reasoning does not
    // survive contact with the facts. Collapsing null to the empty string is exactly the
    // null-flattening AAP 0.4.5.4 forbids - "Never collapse null to zero", whose string analogue
    // is this - and it converts a value the oracle demonstrably produces into a different one. The
    // cost it avoids is not real either: FormatRetCode has NO caller anywhere in this repository
    // outside its own test suite, so the nullable return type propagates a null check to nothing
    // at all. If a caller is added later it must handle the null, because the legacy would have
    // handed it one.
    //
    // WHAT IS STILL NOT DETERMINED, stated so the boundary of the claim is exact: no call site in
    // the corpus passes a null code, so this trace establishes what the oracle WOULD return rather
    // than a recorded observation of it returning that. The distinction changes nothing about the
    // implementation - a trace through three documented language rules is the best evidence
    // available for a function whose body IS readable - but it is the reason the pinning test
    // states the derivation rather than merely asserting the value.
    //
    // DECISION 5 - THE FALLBACK RENDERS THE NUMBER WITH THE INVARIANT CULTURE
    // ------------------------------------------------------------------------------------------
    // `String(rtCode)` on a PowerScript Long produces plain decimal digits, a leading minus for a
    // negative value, and no thousands separator. `long.ToString(CultureInfo.InvariantCulture)`
    // produces exactly that. The invariant culture is passed explicitly rather than relied upon by
    // default: a culture whose NegativeSign is U+2212 MINUS SIGN, or whose NativeDigits are not
    // ASCII, would otherwise change "UNKNOWN (-33)" into something a stored characterization
    // recording could not match.
    //
    // The fallback text itself is byte-for-byte from formatretcode.srf:L82: the word UNKNOWN, ONE
    // space, an opening parenthesis, the number, a closing parenthesis. No colon, no square
    // brackets, no hexadecimal, and no symbolic name appended.
    // ==========================================================================================

    /// <summary>
    /// Renders a PowerFramework return code as its symbolic constant name, falling back to
    /// <c>UNKNOWN (n)</c> for any value the legacy arm table does not recognise.
    /// </summary>
    /// <param name="rtCode">
    /// The return code to render. This is one of the <see cref="RetCode"/> values from the
    /// PowerFramework code space; the XML parser and SQLite code spaces catalogued alongside them
    /// are different, overlapping numbering schemes and are not recognised here.
    /// </param>
    /// <returns>
    /// The symbolic name for one of the 35 recognised codes; <c>UNKNOWN (n)</c> with the code
    /// rendered as invariant decimal for anything else; or <see langword="null"/> when
    /// <paramref name="rtCode"/> is <see langword="null"/>, which is what the oracle's own
    /// null-propagating concatenation produces. See DECISION 4.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ported from <c>formatretcode.srf:L10-L83</c>. Six of the 41 codes that
    /// <c>retcode.sru:L39-L79</c> declares have no arm and reach the fallback:
    /// <c>PREVENT</c>, <c>E_RETRY</c> and <c>UNKNOWN</c> because the oracle simply omits them, and
    /// <c>SUCCESS</c>, <c>ALLOW</c> and <c>CANCELED</c> because they are aliases shadowed by an
    /// earlier arm. All six behaviours are reproduced deliberately and none is a defect in this
    /// port; see DECISION 1 and DECISION 2 above.
    /// </para>
    /// <para>
    /// The rendering is culture-invariant, so the same code produces the same text on every host.
    /// </para>
    /// </remarks>
    public static string? FormatRetCode(long? rtCode)
    {
        // DECISION 4. The oracle's `case else` arm concatenates `String(rtCode)`, and PowerScript
        // propagates null through both `String` and `+`, so a null code yields a NULL result. Not
        // the empty string, and not "UNKNOWN ()".
        if (rtCode is null)
        {
            return null;
        }

        long code = rtCode.Value;

        switch (code)
        {
            // formatretcode.srf:L11-L16 - the three general outcomes.
            //
            // DECISION 2 applies to both arms in this group:
            //   RetCode.OK shadows RetCode.SUCCESS and RetCode.ALLOW, which are also 0
            //   (retcode.sru:L39-L41), so the strings "SUCCESS" and "ALLOW" are UNREACHABLE and
            //   must not appear anywhere in this file.
            //   RetCode.CANCELLED shadows RetCode.CANCELED, which is also -2
            //   (retcode.sru:L44-L45), so the string "CANCELED" is UNREACHABLE for the same
            //   reason. Note that the arm returns the double-L spelling.
            //
            // DECISION 1 also applies here by omission: RetCode.PREVENT (1) sits numerically
            // between OK and these arms and has NO arm of its own, so it falls through to the
            // fallback and renders "UNKNOWN (1)".
            case RetCode.OK:
                return "OK";
            case RetCode.FAILED:
                return "FAILED";
            case RetCode.CANCELLED:
                return "CANCELLED";

            // formatretcode.srf:L17-L34 - the invalid-input family, -3 through -11.
            case RetCode.E_INVALID_ARGUMENT:
                return "E_INVALID_ARGUMENT";
            case RetCode.E_INVALID_IMAGE:
                return "E_INVALID_IMAGE";
            case RetCode.E_INVALID_OBJECT:
                return "E_INVALID_OBJECT";
            case RetCode.E_INVALID_TYPE:
                return "E_INVALID_TYPE";
            case RetCode.E_INVALID_TRANSACTION:
                return "E_INVALID_TRANSACTION";
            case RetCode.E_INVALID_SQL:
                return "E_INVALID_SQL";
            case RetCode.E_INVALID_DATA:
                return "E_INVALID_DATA";
            case RetCode.E_INVALID_DATAOBJECT:
                return "E_INVALID_DATAOBJECT";
            case RetCode.E_INVALID_HANDLE:
                return "E_INVALID_HANDLE";

            // formatretcode.srf:L35-L40 - the out-of-something family, -12 through -14.
            case RetCode.E_OUT_OF_BOUND:
                return "E_OUT_OF_BOUND";
            case RetCode.E_OUT_OF_RANGE:
                return "E_OUT_OF_RANGE";
            case RetCode.E_OUT_OF_MEMORY:
                return "E_OUT_OF_MEMORY";

            // formatretcode.srf:L41-L56 - the not-found family, -15 through -22.
            case RetCode.E_FILE_NOT_FOUND:
                return "E_FILE_NOT_FOUND";
            case RetCode.E_OBJECT_NOT_FOUND:
                return "E_OBJECT_NOT_FOUND";
            case RetCode.E_DATA_NOT_FOUND:
                return "E_DATA_NOT_FOUND";
            case RetCode.E_FUNCTION_NOT_FOUND:
                return "E_FUNCTION_NOT_FOUND";
            case RetCode.E_EVENT_NOT_FOUND:
                return "E_EVENT_NOT_FOUND";
            case RetCode.E_MEMBER_NOT_FOUND:
                return "E_MEMBER_NOT_FOUND";
            case RetCode.E_VAR_NOT_FOUND:
                return "E_VAR_NOT_FOUND";
            case RetCode.E_NOT_EXISTS:
                return "E_NOT_EXISTS";

            // formatretcode.srf:L57-L62 - contention and access, -23 through -25.
            case RetCode.E_BUSY:
                return "E_BUSY";
            case RetCode.E_TIME_OUT:
                return "E_TIME_OUT";
            case RetCode.E_ACCESS_DENIED:
                return "E_ACCESS_DENIED";

            // formatretcode.srf:L63-L76 - the subsystem error family, -26 through -32.
            //
            // DECISION 1 applies at the END of this group. The oracle's arms stop at
            // E_SQL_BIND_ARG_FAILED (-32) and resume at E_NO_SUPPORT (-2000), so
            // RetCode.E_RETRY (-33, retcode.sru:L76) has NO ARM and renders "UNKNOWN (-33)".
            // That omission is the legacy's, it is reproduced deliberately, and the arm must not
            // be added.
            case RetCode.E_WIN32_ERROR:
                return "E_WIN32_ERROR";
            case RetCode.E_INTERNAL_ERROR:
                return "E_INTERNAL_ERROR";
            case RetCode.E_DB_ERROR:
                return "E_DB_ERROR";
            case RetCode.E_HTTP_ERROR:
                return "E_HTTP_ERROR";
            case RetCode.E_WINHTTP_ERROR:
                return "E_WINHTTP_ERROR";
            case RetCode.E_IO_ERROR:
                return "E_IO_ERROR";
            case RetCode.E_SQL_BIND_ARG_FAILED:
                return "E_SQL_BIND_ARG_FAILED";

            // formatretcode.srf:L77-L80 - the two capability codes, -2000 and -2001.
            //
            // DECISION 1 applies again by omission after this group: RetCode.UNKNOWN (-4000,
            // retcode.sru:L79) has no arm either, so it renders "UNKNOWN (-4000)".
            case RetCode.E_NO_SUPPORT:
                return "E_NO_SUPPORT";
            case RetCode.E_NO_IMPLEMENTATION:
                return "E_NO_IMPLEMENTATION";

            // formatretcode.srf:L81-L82 - `case else`. DECISION 5: the text is byte-for-byte from
            // the oracle and the number is rendered with the invariant culture.
            default:
                return "UNKNOWN (" + code.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }

    // ==========================================================================================
    // REGION 2 - Sprintf
    // Reconstructed from ws_objects/pfw.common.pbl.src/sprintf.srf
    //     L3       `global type sprintf from function_object native "pfw.dll"` - the body is
    //              inside the CLOSED pfw.dll. There is no C++ source anywhere in this repository,
    //              so the implementation cannot be read, only inferred.
    //     L7-L27   the 21 forward prototypes, which are the ENTIRE in-repo evidence of the
    //              signature: `Sprintf(readonly string fmt, any param1)` through a 21-value form.
    // plus the 78 Sprintf call sites across ws_objects/, which are the entire in-repo evidence of
    // the format grammar.
    //
    // Everything in this region that is derived from that evidence is stated as such. Everything
    // the evidence does NOT settle is stated as a CHOICE and collected in DECISION 13, so that a
    // later characterization run against the behavioural oracle can correct it deliberately.
    // ==========================================================================================
    //
    // DECISION 6 - 21 OVERLOADS COLLAPSE TO ONE VARIADIC METHOD, AND THE ARITY CEILING GOES AWAY
    // ------------------------------------------------------------------------------------------
    // The oracle declares the same function 21 times, once per argument count. That is not a
    // design; it is a workaround for a language limitation. PowerScript cannot forward an
    // arbitrary-length argument list, so a variadic function has to be hand-unrolled to a fixed
    // maximum - the same reason the legacy SQL layer `choose case`-unrolls a dynamic invocation to
    // 20 positional arguments. C# has native variadic support, so the workaround has no analogue
    // to port and reproducing 21 near-identical overloads would preserve the limitation rather
    // than the behaviour.
    //
    // The legacy ceiling of 21 values is therefore recorded here as a legacy LIMIT rather than a
    // contract: the .NET surface accepts more without behavioural regression, exactly as the
    // migration plan records for the legacy arity ceilings elsewhere in the estate. A call with 30
    // arguments could not have been written against the legacy at all, so there is no legacy
    // behaviour for it to diverge from.
    //
    // A DECLARATION ANOMALY IN THE ORACLE, RECORDED BUT DELIBERATELY NOT REPRODUCED:
    // sprintf.srf:L24 declares `any param17` TWICE in the same parameter list, and L25, L26 and
    // L27 each keep that duplicate before appending param18, param19 and param20. The 21-value
    // form at L27 consequently has 21 parameters but only 20 DISTINCT parameter names. It is a
    // legacy declaration defect - a copy-and-paste slip in a hand-unrolled list - and it has no C#
    // analogue at all once the overloads collapse into one variadic method, because there are no
    // parameter names left to duplicate. Nothing here reproduces it and nothing needs to.
    //
    // Note also the capitalisation split in the oracle, preserved on the C# side by taking the
    // FUNCTION name: the PBNI type at L3 is lowercase `sprintf` while every prototype declares the
    // function as capitalised `Sprintf`. The C# member is `Sprintf`.
    //
    // NO FIXED-ARITY OVERLOADS ARE ADDED. Small one-to-four-argument overloads were considered and
    // rejected on a correctness ground rather than a stylistic one. Adding
    // `Sprintf(string?, object?)` would silently change the meaning of the existing legal call
    // `Sprintf("{1}", null)`: today the null literal binds to the params ARRAY parameter, so the
    // method receives a null array; with a single-object overload in scope it would instead bind as
    // one null ARGUMENT. Two spellings of the same call would then take different code paths, which
    // is precisely the overload-resolution ambiguity that must be avoided. They would also add
    // public surface the oracle does not define, for a benefit this refactor is forbidden from
    // claiming (section 0.8.5).
    //
    // DECISION 7 - THE FORMAT GRAMMAR, RECONSTRUCTED FROM 78 CALL SITES
    // ------------------------------------------------------------------------------------------
    // The grammar implemented below is:
    //
    //     placeholder := '{' [ index ] [ ',' [ '+' | '-' ] alignment ] [ ':' mask ] '}'
    //
    // It was reconstructed by extracting every Sprintf call site in ws_objects/ - 78 of them
    // across 26 files, 39 of which pass a literal format string, yielding 35 distinct literals -
    // and taking the census of every placeholder that appears:
    //
    //     explicit index      {1} {2} {3} {4} {5} {6} {7} {8}    highest index observed is 8
    //     index omitted       {}                                 25-plus occurrences
    //     alignment           {1,-20} {2,-4} {3,-10} {4,-20} {5,-20} and {,-4}
    //     mask                {5:#.0#} {4:YYYY-MM-DD HH:MM:SS} {5:YYYY-MM-DD HH:MM:SS}
    //                         {6:YYYY-MM-DD}
    //
    // Three VERIFIED NEGATIVES matter as much as the positives, because each one closes off a
    // plausible wrong implementation:
    //
    //     {0} appears NOWHERE in the repository, so the numbering genuinely starts at 1 and there
    //     is no zero-based call site to accommodate.
    //     NO printf-style conversion appears anywhere - no %s, %d, %i, %f, %u or %x. Despite the
    //     name, this is not a printf. There is no conversion-specifier sub-language to implement.
    //     NO doubled brace appears anywhere, which is why brace escaping is undetermined
    //     (DECISION 13).
    //
    // Representative live call sites, quoted with their locators so the grammar can be re-checked
    // against the oracle without re-deriving the census:
    //
    //     ws_objects/pfw.tests.pbl.src/w_test_camera_capture.srw:L341
    //         Sprintf("{1} x {2}", ...)
    //     ws_objects/pfw.ui.pbl.src/../n_cst_base_theme.sru:L501
    //         Sprintf("rgb({},{},{})", ...)
    //     ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru:L51
    //         Sprintf("string(pfw/{}[@lang='en']/tr[@text='{}']/@to)", sCat, text)
    //     ws_objects/pfw.tests.pbl.src/w_test_filescanner.srw:L165
    //         Sprintf("{1,-20}~t{2,-4}~t{3,-10}~t{4,-20}~t{5,-20}~r~n", five strings)
    //     ws_objects/pfw.tests.pbl.src/w_test_filescanner.srw:L167
    //         Sprintf("{1,-20}~t{2,-4}~t{3,-10}~t{4:YYYY-MM-DD HH:MM:SS}~t{5:YYYY-MM-DD HH:MM:SS}~r~n", ...)
    //     ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L356
    //         Sprintf("~n{1}~t{2}~t{3}~t{4}~t{5:#.0#}~t{6:YYYY-MM-DD}", ...)
    //     ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw:L315
    //         Sprintf("{,-4} {}({})>{}: {}, Expr: {}", six values)
    //     ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L643
    //         Sprintf("参数类型不匹配: 期望 {} 实际 {}", ...)   (inside a commented-out block, but
    //                                                          still valid grammar evidence)
    //
    // The `~t`, `~n` and `~r~n` sequences in those quotations are PowerScript escapes in the
    // SOURCE literal for tab, line feed and carriage-return-line-feed. They are not part of the
    // Sprintf grammar and this implementation gives them no special treatment: by the time a
    // format string reaches Sprintf they are already the actual characters, exactly as a C# "\t"
    // or "\r\n" would be.
    //
    // Anything in the format string that is not a placeholder is copied through verbatim,
    // including text that immediately abuts a placeholder. That abutment is real and load-bearing:
    // n_cst_dwsvc_contextmenu.sru:L1186 builds a DataWindow expression in which "{1}[1]" means the
    // resolved column name followed by a literal "[1]" array subscript.
    //
    // DECISION 8 - THE INDEX IS ONE-BASED, AND AN EXPLICIT INDEX IS A REPEATABLE DIRECT LOOKUP
    // ------------------------------------------------------------------------------------------
    // This is the first of the two traps in this region. {1} resolves to the FIRST argument. A
    // naive hand-off to string.Format would be off by one on EVERY indexed placeholder in the
    // entire codebase - 40 occurrences of {1} alone - and the resulting output would be wrong in a
    // way that still looks like a formatted string. The substitution is therefore performed here
    // rather than delegated, and no code path in this file passes a legacy format string to
    // string.Format.
    //
    // A second property is VERIFIED rather than inferred, and it is easy to get wrong: an explicit
    // index is a direct positional lookup that may be REPEATED, and repeating it does not consume
    // anything. Three live call sites prove it, all in
    // ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru:
    //
    //     L1183   one argument, {1} used TWICE
    //     L1186   Sprintf("if(IsNull({1}),'',{1}) <> if(IsNull({1}[1]),'',{1}[1])", sColName)
    //             ONE argument, {1} used FOUR times
    //     L1188   one argument, {1} used FOUR times
    //
    // An implementation that consumed an argument per placeholder would render the second and
    // subsequent {1} as nothing and produce a syntactically broken DataWindow Find expression.
    //
    // DECISION 9 - THE SEQUENTIAL CURSOR, AND WHAT ABOUT IT IS VERIFIED
    // ------------------------------------------------------------------------------------------
    // An index-omitted placeholder takes the next argument from a sequential cursor that starts at
    // the first argument and advances by one each time such a placeholder is resolved. This is
    // verified by w_test_dwsvc_columnexp.srw:L315, where "{,-4} {}({})>{}: {}, Expr: {}" is called
    // with exactly six values and maps them positionally in order of appearance. That call site
    // additionally proves that an index-omitted placeholder WITH an alignment - the {,-4} form -
    // participates in the cursor like any other, taking the first argument.
    //
    // What is NOT verified is what happens when the two forms are MIXED in one format string. No
    // literal anywhere in the repository mixes bare {} with explicit {N}. The behaviour
    // implemented here is stated as a CHOICE in DECISION 13: an explicit index does not advance
    // the cursor, because DECISION 8 establishes that an explicit index is a lookup rather than a
    // consumption, and a lookup that also consumed would make the four repetitions of {1} at
    // n_cst_dwsvc_contextmenu.sru:L1186 advance the cursor four times for no reason. The choice is
    // the one that is internally consistent with the verified half.
    //
    // DECISION 10 - THE MASK IS POWERBUILDER'S, NOT .NET'S, AND `MM` IS THE TRAP
    // ------------------------------------------------------------------------------------------
    // This is the second trap, and it is the one that fails silently. In a PowerBuilder display
    // mask, `MM` means MONTH in the date part and MINUTES in the time part; the two are
    // disambiguated by POSITION, and PowerBuilder's own rule is that m or mm is a minute when it
    // follows h or hh. .NET's custom date and time format strings read `MM` as month EVERYWHERE
    // and `mm` as minute everywhere, and they are case-SENSITIVE, whereas PowerBuilder masks are
    // case-INSENSITIVE. That combination is the whole hazard: forwarding
    // "YYYY-MM-DD HH:MM:SS" unchanged to .NET puts the MONTH in the minutes slot and produces a
    // wrong-but-entirely-plausible timestamp that no compiler and no casually chosen test would
    // catch. A test whose month and minute happen to coincide passes while the bug is present,
    // which is why the pinned test below uses 2024-05-15 14:07:09 - month 05, minute 07.
    //
    // The mask is therefore TRANSLATED token by token before it reaches .NET, with these rules:
    //
    //     y      -> y repeated, so YYYY becomes yyyy and YY becomes yy
    //     d      -> d repeated, so DD becomes dd and DDDD becomes dddd (day name)
    //     h      -> H repeated for a 24-hour clock, h repeated for a 12-hour clock. The clock is
    //               selected by whether the mask carries a meridiem designator anywhere, which is
    //               PowerBuilder's rule: a mask with no AM/PM designator is 24-hour.
    //     s      -> s repeated
    //     f      -> f repeated (fractional seconds; PowerBuilder uses up to six for microseconds,
    //               .NET accepts up to seven)
    //     m      -> m repeated when the PRECEDING DESIGNATOR was an hour and the run is one or two
    //               characters long, i.e. minutes; otherwise M repeated, i.e. month. Separator
    //               characters between the two designators are skipped when deciding, which is
    //               what makes "HH:MM" resolve to minutes even though a colon sits in between.
    //     am/pm  -> tt        a/p -> t        (either order, case-insensitively)
    //     anything else -> emitted as an ESCAPED literal
    //
    // Literals are escaped individually with a backslash rather than passed through. That is
    // deliberate and it is a determinism measure: in a .NET custom date format an unescaped '/' is
    // the culture's DATE SEPARATOR placeholder and an unescaped ':' is the culture's TIME
    // SEPARATOR placeholder, so an unescaped mask could still take a culture-dependent path even
    // with an invariant format provider supplied. Escaping every literal removes that path
    // entirely, and it also stops an incidental alphabetic character - the 'T' in an ISO-8601-style
    // mask, say - from being read as a specifier.
    //
    // One further .NET-specific hazard is handled: a custom format string exactly ONE character
    // long is interpreted as a STANDARD format specifier instead, so a single-character result is
    // prefixed with '%' to force the custom reading.
    //
    // THE VERIFIED SUBSET, stated precisely as required and MEASURED rather than assumed. Every
    // mask literal in the estate is one of nine, across three families:
    //     date and time - "YYYY-MM-DD" [w_test_logger.srw:L107, and one more site],
    //                     "YYYY-MM-DD HH:MM:SS" (two sites),
    //                     "MMM-DDD-YY HH:MM:SS" [w_test_logger.srw:L171],
    //                     "H:MM:SS AM/PM"       [w_test_logger.srw:L171]
    //     numeric       - "#.0#", "###,###,##0" [w_test_logger.srw:L107],
    //                     "###,###,##0.0"       [w_test_logger.srw:L171]
    //     string ('@')  - "(@@@)-@@@@" [w_test_logger.srw:L107],
    //                     "\{@@@\}-(@@@@@)"     [w_test_logger.srw:L171]
    // All nine are pinned by test, so all nine are verified rather than best effort. Every token
    // rule above that NO literal exercises remains a documented best effort at the general case and
    // is listed in DECISION 13 CHOICE 12 accordingly.
    //
    // DECISION 11 - MASK DISPATCH IS TYPE-DIRECTED, AND A NUMERIC MASK IS PASSED THROUGH
    // ------------------------------------------------------------------------------------------
    // A mask is translated as a PowerBuilder date and time mask only when the argument it applies
    // to is a date or time value - DateTime, DateTimeOffset, DateOnly or TimeOnly, which are
    // exactly the types the migration plan's type mapping produces for the legacy datetime, date
    // and time types. For any other value the mask is passed through unchanged to .NET.
    //
    // That is sound for the numeric case rather than merely convenient: PowerBuilder's numeric
    // mask alphabet of '#' for an optional digit and '0' for a required digit is the same alphabet
    // .NET custom numeric formats use, so "#.0#" needs no translation and means the same thing on
    // both sides. Both call sites that use it pass a number: w_test_sqlite.srw:L356 formats a
    // salary read as a number through {5:#.0#}, and the same line formats a date through
    // {6:YYYY-MM-DD}, so one live statement exercises both halves of this dispatch.
    //
    // DECISION 12 - ALIGNMENT PADS WITH SPACES, NEGATIVE MEANS LEFT-ALIGNED
    // ------------------------------------------------------------------------------------------
    // Alignment is applied AFTER the mask, to the already-rendered text, which is the only order
    // that can produce the fixed-width columns the corpus builds. A negative width left-aligns and
    // a positive width right-aligns, matching both .NET's convention and the -20, -4 and -10 usages
    // at w_test_filescanner.srw:L165-L167. A rendered value at least as long as the width is never
    // truncated; it is emitted in full and the column simply runs wide.
    // ==========================================================================================

    /// <summary>
    /// The largest number this implementation will accept for a placeholder index or an alignment
    /// width before treating the placeholder as malformed.
    /// </summary>
    /// <remarks>
    /// A guard against a runaway digit run in a hand-authored format string, not a contract: the
    /// highest index anywhere in the legacy corpus is 8 and the widest alignment is 20. A number
    /// beyond this bound makes the placeholder malformed, which DECISION 13 renders verbatim.
    /// </remarks>
    private const int MaximumPlaceholderNumber = 1_000_000;

    /// <summary>
    /// The character that escapes a literal <c>{</c> or <c>}</c> in a format string and inside a
    /// mask, exactly as the legacy documents it.
    /// </summary>
    /// <remarks>
    /// VERIFIED, not inferred. <c>w_test_logger.srw:L104</c> states the rule in words -
    /// <c>使用右斜杠'\'转义中括号:'{','}'</c>, "use the backslash to escape the braces { and }" -
    /// and all five format literals in that file rely on it: <c>:L107</c>, <c>:L171</c>,
    /// <c>:L198</c>, <c>:L226</c> and <c>:L253</c>. See DECISION 14.
    /// </remarks>
    private const char EscapeCharacter = '\\';

    /// <summary>
    /// The character that stands in for one character of a string value inside a PowerBuilder
    /// <c>String()</c> display mask.
    /// </summary>
    /// <remarks>
    /// <c>w_test_logger.srw:L103</c> points the mask field at the <c>String()</c> function's format
    /// documentation, whose string masks use this character as a per-character placeholder. See
    /// DECISION 15.
    /// </remarks>
    private const char StringMaskPlaceholder = '@';

    /// <summary>
    /// Reports whether a character is one of the two braces the backslash escape covers.
    /// </summary>
    /// <param name="candidate">The character to classify.</param>
    /// <returns>
    /// <see langword="true"/> for <c>{</c> or <c>}</c>; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// The set is exactly the two characters <c>w_test_logger.srw:L104</c> names. A backslash before
    /// anything else is NOT an escape and survives into the output unchanged, which is what keeps a
    /// Windows path or a regular expression in a format string intact. See DECISION 14.
    /// </remarks>
    private static bool IsBrace(char candidate)
    {
        return candidate is '{' or '}';
    }

    /// <summary>
    /// Renders a composite format string in the PowerFramework dialect, substituting
    /// <paramref name="args"/> into its placeholders.
    /// </summary>
    /// <param name="fmt">
    /// The format string. Placeholders take the form
    /// <c>{index,alignment:mask}</c>, where every part is optional; the index is ONE-BASED, so
    /// <c>{1}</c> is the first argument, and an omitted index takes the next argument in sequence.
    /// Anything that is not a placeholder is copied through verbatim.
    /// </param>
    /// <param name="args">
    /// The values to substitute. The legacy ceiling of 21 values is a limitation of the
    /// hand-unrolled PowerScript declaration, not a contract, so more may be passed; see
    /// DECISION 6.
    /// </param>
    /// <returns>
    /// The rendered string; the empty string when <paramref name="fmt"/> is
    /// <see langword="null"/> or empty.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This member is a RECONSTRUCTION. The legacy implementation lives inside the closed
    /// <c>pfw.dll</c> and cannot be read, so the grammar was inferred from the 21 prototypes at
    /// <c>sprintf.srf:L7-L27</c> and from all 78 call sites in <c>ws_objects/</c>. Behaviours the
    /// corpus does not exercise are inferred choices rather than verified legacy behaviour and are
    /// enumerated in DECISION 13 above the implementation.
    /// </para>
    /// <para>
    /// Despite the name this is not a printf: no <c>%</c> conversion specifier exists in the
    /// dialect and none is recognised here.
    /// </para>
    /// <para>
    /// The method never throws. Every malformed input - an unparseable placeholder, an index with
    /// no corresponding argument, a mask a value cannot honour - degrades to an inert rendering,
    /// which matters because many legacy call sites use this function to BUILD an error message
    /// and an exception raised here would mask the fault being reported.
    /// </para>
    /// <para>
    /// All rendering is culture-invariant, so the same arguments produce the same text on every
    /// host.
    /// </para>
    /// </remarks>
    public static string Sprintf(string? fmt, params object?[]? args)
    {
        // CHOICE (DECISION 13): a null or empty format string yields the empty string rather than
        // throwing. `params` in C# also allows the array itself to be null, at the call
        // `Sprintf("{1}", null)`, so the array is normalised before use rather than dereferenced.
        if (string.IsNullOrEmpty(fmt))
        {
            return string.Empty;
        }

        object?[] values = args ?? [];

        StringBuilder result = new(fmt.Length + 16);

        // Zero-based index of the next argument an index-omitted placeholder will take. DECISION 9:
        // an explicit index is a lookup and deliberately does not advance this.
        int cursor = 0;
        int position = 0;

        while (position < fmt.Length)
        {
            char current = fmt[position];

            // DECISION 14, and it is checked BEFORE the brace arms because it has to win over
            // them: the backslash is the escape the legacy documents [w_test_logger.srw:L104],
            // so `\{` and `\}` each contribute ONE literal brace and consume both characters.
            // Doing this here rather than as a pre-pass over the whole format string is what
            // keeps `\{1\}` from becoming the placeholder `{1}` - see DECISION 14.
            if (current == EscapeCharacter
                && position + 1 < fmt.Length
                && IsBrace(fmt[position + 1]))
            {
                result.Append(fmt[position + 1]);
                position += 2;
                continue;
            }

            if (current == '{')
            {
                // CHOICE (DECISION 13): a doubled brace ALSO escapes a literal brace, mirroring
                // .NET composite formatting. No call site in the repository contains a doubled
                // brace, so this cannot regress any existing caller; it is retained alongside the
                // documented backslash form rather than instead of it. See DECISION 14.
                if (position + 1 < fmt.Length && fmt[position + 1] == '{')
                {
                    result.Append('{');
                    position += 2;
                    continue;
                }

                if (TryReadPlaceholder(
                        fmt,
                        position,
                        out int afterPlaceholder,
                        out int oneBasedIndex,
                        out int alignment,
                        out string? mask))
                {
                    // DECISION 8 and DECISION 9. A negative oneBasedIndex means the index was
                    // omitted, so the cursor supplies it and then advances. An explicit index is
                    // translated from one-based to zero-based and leaves the cursor alone; an
                    // explicit {0} therefore becomes -1 and is handled as out of range, which is
                    // the documented choice rather than a silent alias for the first argument.
                    int argumentIndex = oneBasedIndex < 0 ? cursor++ : oneBasedIndex - 1;

                    AppendAligned(result, RenderArgument(values, argumentIndex, mask), alignment);
                    position = afterPlaceholder;
                    continue;
                }

                // CHOICE (DECISION 13): a brace that does not begin a well-formed placeholder is
                // copied verbatim and scanning resumes after it. Nothing throws, and a format
                // string carrying incidental braces - a CSS rule body, say - survives intact.
                result.Append(current);
                position++;
                continue;
            }

            if (current == '}' && position + 1 < fmt.Length && fmt[position + 1] == '}')
            {
                // CHOICE (DECISION 13): the closing half of the doubled-brace escape. A LONE
                // closing brace is copied verbatim by the fall-through below rather than treated
                // as an error, which is where this deliberately diverges from string.Format.
                result.Append('}');
                position += 2;
                continue;
            }

            result.Append(current);
            position++;
        }

        return result.ToString();
    }

    // ==========================================================================================
    // DECISION 13 - THE COMPLETE REGISTER OF INFERRED CHOICES
    // ------------------------------------------------------------------------------------------
    // Sprintf's body is inside the closed pfw.dll and the 78-call-site corpus does not exercise
    // any of the behaviours below, so the repository DOES NOT ADJUDICATE THEM. Each is a
    // conservative choice made here, implemented consistently, and named so that a later
    // characterization run against the behavioural oracle can overturn it without anyone having to
    // re-derive the question. NONE of them is verified legacy behaviour and none may be cited as
    // such.
    //
    // The general posture, stated once: mirror .NET composite-formatting conventions where the
    // legacy is silent, never throw when something inert can be rendered instead, and treat an
    // absent value as the empty string.
    //
    // CHOICE 1 - whether a DOUBLED brace escapes, IN ADDITION to the documented backslash.
    //     The backslash form is not on this list at all: it is documented at
    //     w_test_logger.srw:L104 and used by five format literals in that file, so it is verified
    //     behaviour and is implemented as such under DECISION 14. What remains a choice is only
    //     whether `{{` and `}}` ALSO collapse to one brace. No corpus literal contains a doubled
    //     brace, so nothing adjudicates it.
    //     Chosen: yes, they do, mirroring .NET composite formatting. Nothing can regress,
    //     precisely because no corpus literal contains one, and a .NET author reaching for the
    //     convention they already know is not silently surprised.
    //     Pinned by: SprintfDoubledBraceAlsoEscapesWhichIsAnInferredChoice
    //
    // CHOICE 2 - an index with no corresponding argument, including the explicit {0} that appears
    //            nowhere in the corpus.
    //     Chosen: renders the empty string, with any alignment still applied, and does not throw.
    //     Pinned by: SprintfOutOfRangeIndexIsAnInferredChoice
    //
    // CHOICE 3 - how a null argument renders.
    //     Chosen: the empty string.
    //     Pinned by: SprintfNullArgumentIsAnInferredChoice
    //
    // CHOICE 4 - a null or empty format string, and a null params array.
    //     Chosen: the empty string, rather than throwing.
    //     Pinned by: SprintfNullFormatIsAnInferredChoice
    //
    // CHOICE 5 - mixing index-omitted {} with explicit {N} in one format string, and whether an
    //            explicit index advances the sequential cursor. No corpus literal mixes the forms.
    //     Chosen: both are honoured, and an explicit index is a lookup that does NOT advance the
    //     cursor; only an index-omitted placeholder does. This is the reading consistent with the
    //     VERIFIED repetition at n_cst_dwsvc_contextmenu.sru:L1186, where {1} appears four times
    //     against a single argument.
    //     Pinned by: SprintfMixedIndexAndSequentialIsAnInferredChoice
    //
    // CHOICE 6 - a malformed placeholder: a non-digit where an index belongs, a comma with no
    //            width, an unterminated brace, embedded whitespace, a number past the guard.
    //     Chosen: copied verbatim, with scanning resuming after the brace.
    //     Pinned by: SprintfMalformedPlaceholderIsAnInferredChoice
    //
    // CHOICE 7 - a boolean argument. System.Boolean is not IFormattable and no call site passes one.
    //     Chosen: the invariant ToString, giving "True" and "False".
    //     Pinned by: SprintfBooleanRenderingIsAnInferredChoice
    //
    // CHOICE 8 - a date or time argument with NO mask. The legacy default would be locale
    //            dependent, which section 0.6.7 forbids outright.
    //     Chosen: the invariant general format, in preference to inventing a mask.
    //     Pinned by: SprintfMasklessDateRenderingIsAnInferredChoice
    //
    // CHOICE 9 - whether alignment counts characters or display columns. The two differ for the CJK
    //            text the corpus pads at w_test_filescanner.srw:L165.
    //     Chosen: CHARACTERS, as .NET counts them.
    //     Pinned by: SprintfAlignmentIsMeasuredInCharactersIsAnInferredChoice
    //
    // CHOICE 10 - a mask with NO '@' placeholder applied to a value that cannot honour a .NET
    //             format string, such as a date or numeric mask landing on a string.
    //     NARROWER THAN "ANY MASK ON A STRING". A mask that DOES carry '@' is a
    //     PowerBuilder String() display mask, the corpus applies one to a string at five call
    //     sites, and it is implemented as verified behaviour under DECISION 15. What is left
    //     unadjudicated is only the residue: a mask that is not a string mask and cannot be a
    //     .NET format either.
    //     Chosen: the mask is ignored and the value rendered unmasked.
    //     Pinned by: SprintfNonStringMaskOnAStringIsAnInferredChoice
    //
    // CHOICE 14 - a string mask with MORE '@' placeholders than the value has characters.
    //     Chosen: the surplus placeholders emit nothing, so the value is not padded and no filler
    //     character is invented. Mask literals around them are still emitted.
    //     Pinned by: SprintfStringMaskExhaustedValueIsAnInferredChoice
    //
    // CHOICE 15 - a value with MORE characters than the string mask has '@' placeholders.
    //     Chosen: the surplus characters are dropped, because a mask states a fixed shape and an
    //     unmasked tail would match neither the mask nor the value.
    //     Pinned by: SprintfStringMaskSurplusCharactersAreAnInferredChoice
    //
    // CHOICE 11 - a mask the value rejects, such as a date mask naming an hour on a DateOnly.
    //     Chosen: falls back to the unmasked invariant rendering and never throws, because callers
    //     build error text with this method.
    //     Pinned by: SprintfRejectedMaskIsAnInferredChoice
    //
    // CHOICE 12 - every mask token beyond the nine mask literals the corpus verifies, enumerated in
    //             DECISION 10's verified-subset note.
    //     Chosen: translated by the general token rules of DECISION 10.
    //     Pinned by: SprintfGeneralMaskTokensAreAnInferredChoice
    //
    // NOT ON THIS LIST, AND DELIBERATELY SO: what a null return code renders as. That question is
    // settled by a complete trace through the oracle's own readable body rather than chosen here,
    // so it is recorded as behaviour under DECISION 4 and pinned by
    // FormatRetCodeNullPropagatesNullThroughTheFallbackConcatenation.
    // ==========================================================================================

    // ==========================================================================================
    // DECISION 14 - THE BACKSLASH IS THE BRACE ESCAPE, AND IT IS DOCUMENTED RATHER THAN INFERRED
    // ------------------------------------------------------------------------------------------
    // w_test_logger.srw:L100-L104 is the legacy's own written specification of this dialect, sitting
    // directly above a live Sprintf call site. Line by line it gives the placeholder grammar
    // `{index[,alignment][:format]}`, states that the index starts at 1, states that a positive
    // alignment right-aligns and a negative one left-aligns, points the format field at the
    // `String()` function's display formats, and then states the escape:
    //
    //      //*使用右斜杠'\'转义中括号:'{','}'
    //      "use the backslash '\' to escape the braces '{' and '}'"
    //
    // All five format literals in that file use it - :L107 in a genuine Sprintf call and :L171,
    // :L198, :L226 and :L253 through the logger - so `\{1\}` renders the literal text `{1}`. This is
    // therefore VERIFIED BEHAVIOUR and not a member of the DECISION 13 register.
    //
    // TWO PLACES IMPLEMENT IT, AND BOTH ARE NECESSARY
    //   1. The main scan loop, ahead of both brace arms. `\{` and `\}` each emit one brace and
    //      consume two characters, so no placeholder parse is attempted and NO ARGUMENT IS
    //      CONSUMED. This is why the escape cannot be a pre-pass that decodes the whole format
    //      string first: decoding `\{1\}` up front would produce `{1}`, which the scanner would
    //      then read as a placeholder and substitute - the exact opposite of the intent.
    //   2. The mask scan inside TryReadPlaceholder, where an escaped brace is DATA rather than the
    //      end of the field. The live mask at :L171 is `\{@@@\}-(@@@@@)`, so a scan that stopped at
    //      the first '}' would take the mask to be `\{@@@\`, leak `-(@@@@@)}` into the surrounding
    //      literal text, and then hand the truncated remains to the value.
    //
    // A BACKSLASH BEFORE ANYTHING ELSE IS NOT AN ESCAPE. The specification names exactly two
    // characters, so `\n`, `\\` and a Windows path all survive verbatim. Extending the escape to
    // other characters would invent grammar the legacy does not have and would corrupt any format
    // string carrying a path or a regular expression.
    //
    // CONSEQUENCE WORTH STATING: a format string that wants a literal backslash immediately before
    // a placeholder cannot have one, because `\{` is always the escape. That limitation is the
    // legacy's, not this port's.
    // ==========================================================================================

    // ==========================================================================================
    // DECISION 15 - THE '@' STRING MASK IS APPLIED BY STRING SURGERY, BECAUSE THAT IS WHAT IT IS
    // ------------------------------------------------------------------------------------------
    // w_test_logger.srw:L103 points the mask field at the `String()` function's format
    // documentation, and that dialect's STRING masks use '@' as a placeholder for one character of
    // the value with every other mask character emitted literally. The corpus applies exactly that
    // to a string, at five call sites:
    //
    //      :L107   {1:(@@@)-@@@@}          over "1234567"   ->  (123)-4567
    //      :L171   {2,20:\{@@@\}-(@@@@@)}  over "12345678"  ->  {123}-(45678), right-aligned to 20
    //      :L198, :L226, :L253             the same mask through Warning, Error and Info
    //
    // So a mask on a string is NOT an unexercised case, and it is not a mask with nothing to apply
    // it to. It is a different KIND of mask, and the type-directed dispatch in RenderArgument has
    // three arms rather than two:
    //
    //      IFormattable + date or time      translate the PowerBuilder date mask (DECISION 10)
    //      IFormattable + anything else     pass the mask through, for the '#'/'0' alphabet
    //      not IFormattable + mask has '@'  apply the String() character mask HERE
    //      not IFormattable + no '@'        drop the mask (DECISION 13 CHOICE 10)
    //
    // The '@' test is what keeps the last two arms apart, and it is a test on the MASK rather than
    // on the value because that is the only thing that distinguishes the two dialects: `YYYY-MM-DD`
    // and `###,###` carry no '@' and cannot be string masks, so a date or numeric mask that lands
    // on a string still degrades to the unmasked value exactly as it did before.
    //
    // ALIGNMENT IS APPLIED AFTER MASKING, not instead of it, which is what makes the :L171 column
    // line up: the mask produces 13 characters and the width of 20 then pads them.
    //
    // NOT IMPLEMENTED, AND DELIBERATELY: the `String()` dialect's second section after a semicolon,
    // and its '~' escape. No mask anywhere in the corpus uses either, so a semicolon or a tilde in a
    // mask is treated as a literal mask character rather than as grammar. Inventing the rest of the
    // dialect on no evidence would be a larger risk than the gap.
    // ==========================================================================================

    /// <summary>
    /// Identifies which kind of date or time designator was most recently emitted while
    /// translating a PowerBuilder mask, which is what disambiguates month from minute.
    /// </summary>
    /// <remarks>
    /// Separator and literal characters deliberately do NOT produce a value here. That omission is
    /// the mechanism that makes <c>HH:MM</c> resolve its <c>MM</c> to minutes even though a colon
    /// sits between the two designators; see DECISION 10.
    /// </remarks>
    private enum MaskDesignator
    {
        /// <summary>No designator has been emitted yet.</summary>
        None,

        /// <summary>A year designator.</summary>
        Year,

        /// <summary>A month designator.</summary>
        Month,

        /// <summary>A day designator.</summary>
        Day,

        /// <summary>An hour designator. The only value that makes a following m mean minutes.</summary>
        Hour,

        /// <summary>A minute designator.</summary>
        Minute,

        /// <summary>A second designator.</summary>
        Second,

        /// <summary>A fractional-seconds designator.</summary>
        Fraction,

        /// <summary>An AM or PM designator.</summary>
        Meridiem,
    }

    /// <summary>
    /// Attempts to read one placeholder beginning at the opening brace at
    /// <paramref name="start"/>.
    /// </summary>
    /// <param name="fmt">The format string being scanned.</param>
    /// <param name="start">The index of the opening brace.</param>
    /// <param name="afterPlaceholder">
    /// On success, the index just past the closing brace. Unspecified on failure.
    /// </param>
    /// <param name="oneBasedIndex">
    /// On success, the one-based argument index, or a negative value when the index was omitted.
    /// The distinction matters: an omitted index draws from the sequential cursor whereas an
    /// explicit zero is an out-of-range lookup.
    /// </param>
    /// <param name="alignment">
    /// On success, the alignment width; negative for left-aligned, positive for right-aligned, and
    /// zero when no alignment was given.
    /// </param>
    /// <param name="mask">
    /// On success, the mask text, or <see langword="null"/> when no mask was given.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a well-formed placeholder was read; otherwise
    /// <see langword="false"/>, in which case the caller copies the brace verbatim.
    /// </returns>
    private static bool TryReadPlaceholder(
        string fmt,
        int start,
        out int afterPlaceholder,
        out int oneBasedIndex,
        out int alignment,
        out string? mask)
    {
        afterPlaceholder = start;
        oneBasedIndex = -1;
        alignment = 0;
        mask = null;

        // start indexes the opening brace, so the grammar begins one character later.
        int position = start + 1;

        // [ index ] - optional. Absence is meaningful, so it is detected by looking before reading
        // rather than by treating an unsuccessful read as absence: a digit run that overflows the
        // guard is MALFORMED, not omitted.
        if (position < fmt.Length && char.IsAsciiDigit(fmt[position]))
        {
            if (!TryReadNumber(fmt, ref position, out int parsedIndex))
            {
                return false;
            }

            oneBasedIndex = parsedIndex;
        }

        // [ ',' [ '+' | '-' ] alignment ] - optional as a whole, but once the comma is present the
        // width is mandatory. The '+' sign is accepted for symmetry with '-'; no corpus call site
        // uses it, and it means the same as no sign at all.
        if (position < fmt.Length && fmt[position] == ',')
        {
            position++;

            bool leftAligned = false;
            if (position < fmt.Length && (fmt[position] == '-' || fmt[position] == '+'))
            {
                leftAligned = fmt[position] == '-';
                position++;
            }

            if (!TryReadNumber(fmt, ref position, out int width))
            {
                return false;
            }

            alignment = leftAligned ? -width : width;
        }

        // [ ':' mask ] - optional. The mask runs to the first UNESCAPED closing brace, so it may
        // contain colons, commas, spaces and hyphens, all of which the corpus masks do, AND it may
        // contain a brace of its own provided the brace is backslash-escaped. The live mask
        // `\{@@@\}-(@@@@@)` at w_test_logger.srw:L171 is exactly that case, which is why the scan
        // below decodes escapes as it goes instead of slicing the span. See DECISION 14.
        if (position < fmt.Length && fmt[position] == ':')
        {
            position++;

            StringBuilder maskText = new();
            bool terminated = false;

            while (position < fmt.Length)
            {
                char maskCharacter = fmt[position];

                // An escaped brace is DATA: it contributes the brace itself to the mask and does
                // not terminate the field. Decoding here rather than after the fact is what makes
                // the two jobs - finding the end of the mask, and knowing what the mask says -
                // agree; slicing to the first '}' answers the first question wrongly and then
                // leaks the remainder into the surrounding literal text.
                if (maskCharacter == EscapeCharacter
                    && position + 1 < fmt.Length
                    && IsBrace(fmt[position + 1]))
                {
                    maskText.Append(fmt[position + 1]);
                    position += 2;
                    continue;
                }

                if (maskCharacter == '}')
                {
                    terminated = true;
                    break;
                }

                maskText.Append(maskCharacter);
                position++;
            }

            if (!terminated)
            {
                // Unterminated. CHOICE 6: malformed, so the caller renders it verbatim.
                return false;
            }

            mask = maskText.ToString();
        }

        if (position < fmt.Length && fmt[position] == '}')
        {
            afterPlaceholder = position + 1;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads a run of one or more ASCII digits, advancing <paramref name="position"/> past them.
    /// </summary>
    /// <param name="fmt">The format string being scanned.</param>
    /// <param name="position">The scan position, advanced past the digits that were consumed.</param>
    /// <param name="value">The parsed value on success; zero otherwise.</param>
    /// <returns>
    /// <see langword="true"/> when at least one digit was read and the value stayed within
    /// <see cref="MaximumPlaceholderNumber"/>; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Parsed by hand rather than through a parsing API for two reasons that both matter here: it
    /// cannot consult a culture, so no digit shape or sign convention can leak in, and the guard
    /// makes an absurd digit run malformed instead of overflowing.
    /// </remarks>
    private static bool TryReadNumber(string fmt, ref int position, out int value)
    {
        value = 0;
        int digits = 0;

        while (position < fmt.Length && char.IsAsciiDigit(fmt[position]))
        {
            value = (value * 10) + (fmt[position] - '0');
            position++;
            digits++;

            if (value > MaximumPlaceholderNumber)
            {
                return false;
            }
        }

        return digits > 0;
    }

    /// <summary>
    /// Resolves one argument and renders it, applying <paramref name="mask"/> when there is one.
    /// </summary>
    /// <param name="values">The argument array, already normalised to non-null.</param>
    /// <param name="argumentIndex">The zero-based argument index, which may be out of range.</param>
    /// <param name="mask">The mask to apply, or <see langword="null"/> for none.</param>
    /// <returns>The rendered text, which is the empty string for an absent or null argument.</returns>
    /// <remarks>
    /// Every rendering path passes <see cref="CultureInfo.InvariantCulture"/> explicitly, which is
    /// what section 0.6.7 requires of anything a characterization recording will compare.
    /// </remarks>
    private static string RenderArgument(object?[] values, int argumentIndex, string? mask)
    {
        // CHOICE 2: an index with no argument behind it renders as nothing rather than throwing.
        // This is also the path an explicit {0} takes, since one-based 0 maps to -1.
        if (argumentIndex < 0 || argumentIndex >= values.Length)
        {
            return string.Empty;
        }

        object? value = values[argumentIndex];

        // CHOICE 3: a null argument renders as nothing.
        if (value is null)
        {
            return string.Empty;
        }

        if (mask is null || mask.Length == 0)
        {
            // No mask, so nothing to translate. An empty mask - the {1:} form, which no call site
            // uses - is treated as no mask at all.
            // CHOICE 7 and CHOICE 8 both live on this path: a boolean is not IFormattable so it
            // takes the plain ToString, and a maskless date takes the invariant general format.
            return value is IFormattable unmasked
                ? unmasked.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString() ?? string.Empty;
        }

        if (value is IFormattable formattable)
        {
            // DECISION 11: type-directed dispatch. A date or time value gets the PowerBuilder mask
            // translated; anything else - a number, in practice - gets it passed through, which is
            // correct for the '#' and '0' numeric alphabet the two dialects share.
            string netFormat = IsDateOrTime(value) ? TranslateDateTimeMask(mask) : mask;

            try
            {
                return formattable.ToString(netFormat, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                // CHOICE 11: the value rejected the mask, for instance a DateOnly asked for an
                // hour. Degrade to the unmasked rendering rather than propagate, because callers
                // build error messages with this method and an exception here would replace the
                // fault being reported with a formatting fault.
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }
        }

        // The value cannot honour a .NET format string, so there is nothing to hand the mask to -
        // but that does NOT mean the mask has no meaning. DECISION 15: a PowerBuilder String()
        // display mask carrying the '@' character placeholder is applied by string surgery here,
        // because that is the only way it was ever meant to be applied and the corpus applies it
        // to a string at w_test_logger.srw:L107, :L171, :L198, :L226 and :L253.
        string text = value.ToString() ?? string.Empty;

        if (mask.Contains(StringMaskPlaceholder, StringComparison.Ordinal))
        {
            return ApplyStringMask(text, mask);
        }

        // CHOICE 10, narrowed to what it always actually covered: a mask with no character
        // placeholder in it cannot be a string mask, so there is genuinely nothing to apply and it
        // is dropped. This is the path a date or numeric mask takes when it lands on a string.
        return text;
    }

    /// <summary>
    /// Applies a PowerBuilder <c>String()</c> display mask to a value that cannot honour a .NET
    /// format string, substituting one character of <paramref name="text"/> for each
    /// <c>@</c> placeholder in <paramref name="mask"/>.
    /// </summary>
    /// <param name="text">The already-rendered value the mask is applied to.</param>
    /// <param name="mask">
    /// The mask, with any backslash escapes already decoded by <c>TryReadPlaceholder</c>, and known
    /// to contain at least one <c>@</c>.
    /// </param>
    /// <returns>The masked text.</returns>
    /// <remarks>
    /// <para>
    /// The rule, from the <c>String()</c> function's display-format documentation that
    /// <c>w_test_logger.srw:L103</c> points the mask field at: <c>@</c> is a placeholder for one
    /// character of the value, and every other character of the mask is a literal that is emitted as
    /// written. So <c>(@@@)-@@@@</c> over <c>"1234567"</c> gives <c>(123)-4567</c>
    /// [<c>w_test_logger.srw:L107</c>], and <c>{@@@}-(@@@@@)</c> over <c>"12345678"</c> gives
    /// <c>{123}-(45678)</c> [<c>:L171</c>] - the braces in the second mask being literals precisely
    /// because the format literal escapes them.
    /// </para>
    /// <para>
    /// TWO EDGES THE CORPUS DOES NOT EXERCISE, and both are handled by continuing rather than
    /// throwing, for the same reason the rest of this class never throws - callers build error text
    /// with it. When the value runs out before the placeholders do, the remaining placeholders emit
    /// NOTHING, so a short value is not padded and no filler character is invented. When the
    /// placeholders run out before the value does, the surplus characters are DROPPED, because a
    /// mask states a fixed shape and appending an unmasked tail would produce output matching
    /// neither the mask nor the value. Both are recorded as CHOICE 14 and CHOICE 15 in DECISION 13.
    /// </para>
    /// <para>
    /// Only the <c>@</c> placeholder is implemented. The <c>String()</c> string-format dialect also
    /// admits a second section after a semicolon and a <c>~</c> escape, and NO mask anywhere in the
    /// corpus uses either, so neither is invented here; a semicolon or tilde in a mask is a literal.
    /// </para>
    /// </remarks>
    private static string ApplyStringMask(string text, string mask)
    {
        StringBuilder masked = new(mask.Length);
        int consumed = 0;

        for (int maskIndex = 0; maskIndex < mask.Length; maskIndex++)
        {
            char maskCharacter = mask[maskIndex];

            if (maskCharacter != StringMaskPlaceholder)
            {
                // A literal position in the mask. Emitted whether or not the value still has
                // characters left, so the mask's fixed shape survives a short value.
                masked.Append(maskCharacter);
                continue;
            }

            if (consumed < text.Length)
            {
                masked.Append(text[consumed]);
                consumed++;
            }

            // else: CHOICE 14 - the value is exhausted, so this placeholder contributes nothing.
        }

        // CHOICE 15 - any characters of the value beyond the last placeholder are dropped.
        return masked.ToString();
    }

    /// <summary>
    /// Reports whether a value is one of the date or time types the legacy type mapping produces,
    /// and therefore whether a mask applied to it must be translated from the PowerBuilder dialect.
    /// </summary>
    /// <param name="value">The value to classify. Never <see langword="null"/> on this path.</param>
    /// <returns><see langword="true"/> for a date or time value; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// The set is exactly the migration plan's type mapping for the legacy <c>datetime</c>,
    /// <c>date</c> and <c>time</c> types, plus <see cref="DateTimeOffset"/>.
    /// <see cref="TimeSpan"/> is deliberately EXCLUDED: the legacy type mapping never produces one,
    /// and its custom format alphabet differs from the date and time alphabet - it has no 24-hour
    /// specifier - so translating a PowerBuilder mask into it would be wrong rather than merely
    /// unverified. A TimeSpan therefore takes the mask-passthrough path.
    /// </remarks>
    private static bool IsDateOrTime(object value)
    {
        return value is DateTime or DateTimeOffset or DateOnly or TimeOnly;
    }

    /// <summary>
    /// Translates a PowerBuilder date and time display mask into the equivalent .NET custom date
    /// and time format string.
    /// </summary>
    /// <param name="mask">The PowerBuilder mask, matched case-insensitively.</param>
    /// <returns>The equivalent .NET custom format string.</returns>
    /// <remarks>
    /// <para>
    /// This method exists because of the trap described in DECISION 10: PowerBuilder resolves
    /// <c>MM</c> to month or minute BY POSITION, .NET resolves it to month everywhere, and .NET is
    /// case-sensitive where PowerBuilder is not. Forwarding a mask unchanged would put the month
    /// in the minutes slot and produce a plausible-looking wrong timestamp.
    /// </para>
    /// <para>
    /// Verified against the corpus for all four date and time masks any call site uses -
    /// <c>YYYY-MM-DD</c>, <c>YYYY-MM-DD HH:MM:SS</c>, <c>MMM-DDD-YY HH:MM:SS</c> and
    /// <c>H:MM:SS AM/PM</c>. Token rules no literal exercises are a documented best effort
    /// at the general case; see CHOICE 12 in DECISION 13.
    /// </para>
    /// </remarks>
    private static string TranslateDateTimeMask(string mask)
    {
        // PowerBuilder's rule: a mask carrying no meridiem designator is a 24-hour clock. This is
        // decided over the WHOLE mask before scanning, because the designator legitimately appears
        // after the hour token it governs.
        bool twelveHourClock = HasMeridiemDesignator(mask);

        StringBuilder translated = new(mask.Length + 8);

        // The last date or time DESIGNATOR emitted. Literals do not touch it, which is exactly what
        // lets the colon in "HH:MM" be skipped when deciding that MM means minutes.
        MaskDesignator previous = MaskDesignator.None;

        int position = 0;

        while (position < mask.Length)
        {
            // Meridiem designators are multi-character sequences containing a solidus, so they are
            // matched before the single-character token scan. Matching them first is also what
            // keeps their solidus out of the literal branch.
            if (TryReadMeridiem(mask, position, out int meridiemLength, out string meridiemFormat))
            {
                translated.Append(meridiemFormat);
                previous = MaskDesignator.Meridiem;
                position += meridiemLength;
                continue;
            }

            char lowered = char.ToLowerInvariant(mask[position]);
            int run = RunLength(mask, position, lowered);

            switch (lowered)
            {
                case 'y':
                    // YYYY becomes yyyy, YY becomes yy. .NET accepts up to five y, so the run is
                    // capped there rather than passed through unbounded.
                    translated.Append('y', run < 5 ? run : 5);
                    previous = MaskDesignator.Year;
                    break;

                case 'd':
                    // DD becomes dd; DDD and DDDD are day names in both dialects.
                    translated.Append('d', run < 4 ? run : 4);
                    previous = MaskDesignator.Day;
                    break;

                case 'h':
                    // The 24-hour specifier is uppercase H in .NET and has no PowerBuilder
                    // equivalent, so the clock choice is made here rather than in the mask.
                    translated.Append(twelveHourClock ? 'h' : 'H', run < 2 ? run : 2);
                    previous = MaskDesignator.Hour;
                    break;

                case 's':
                    translated.Append('s', run < 2 ? run : 2);
                    previous = MaskDesignator.Second;
                    break;

                case 'f':
                    // PowerBuilder uses up to six f for microseconds; .NET accepts up to seven.
                    translated.Append('f', run < 7 ? run : 7);
                    previous = MaskDesignator.Fraction;
                    break;

                case 'm':
                    // THE TRAP, resolved. Minutes only when the preceding DESIGNATOR was an hour
                    // and the run is one or two characters; three or four m are month NAMES in
                    // PowerBuilder and have no minute reading at all.
                    if (previous == MaskDesignator.Hour && run <= 2)
                    {
                        translated.Append('m', run);
                        previous = MaskDesignator.Minute;
                    }
                    else
                    {
                        translated.Append('M', run < 4 ? run : 4);
                        previous = MaskDesignator.Month;
                    }

                    break;

                default:
                    // A literal. Escaped character by character so that neither the culture date
                    // separator '/' nor the culture time separator ':' can be reintroduced, and so
                    // that an incidental letter cannot be read as a specifier.
                    //
                    // `previous` is deliberately NOT updated here. See the remarks on
                    // MaskDesignator: that omission is what makes "HH:MM" mean minutes.
                    for (int repeat = 0; repeat < run; repeat++)
                    {
                        translated.Append('\\').Append(mask[position + repeat]);
                    }

                    break;
            }

            position += run;
        }

        if (translated.Length == 1)
        {
            // A one-character custom format string is read as a STANDARD format specifier by .NET,
            // so '%' forces the custom reading. Reachable for a single-token mask such as "D".
            return "%" + translated.ToString();
        }

        return translated.ToString();
    }

    /// <summary>
    /// Reports whether the mask carries an AM or PM designator anywhere, which selects a 12-hour
    /// clock for its hour tokens.
    /// </summary>
    /// <param name="mask">The PowerBuilder mask.</param>
    /// <returns><see langword="true"/> when a designator is present; otherwise <see langword="false"/>.</returns>
    private static bool HasMeridiemDesignator(string mask)
    {
        for (int position = 0; position < mask.Length; position++)
        {
            if (TryReadMeridiem(mask, position, out _, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to read a PowerBuilder AM or PM designator at <paramref name="position"/>.
    /// </summary>
    /// <param name="mask">The PowerBuilder mask.</param>
    /// <param name="position">The scan position.</param>
    /// <param name="length">The number of characters the designator occupies, on success.</param>
    /// <param name="netFormat">The equivalent .NET specifier, on success.</param>
    /// <returns><see langword="true"/> when a designator was read; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Both orders are accepted because PowerBuilder documents both; .NET has a single two-letter
    /// designator specifier and a single one-letter one, so both orders map onto the same output.
    /// No call site in the corpus uses either form, so this is part of CHOICE 12.
    /// </remarks>
    private static bool TryReadMeridiem(string mask, int position, out int length, out string netFormat)
    {
        if (MatchesAt(mask, position, "am/pm") || MatchesAt(mask, position, "pm/am"))
        {
            length = 5;
            netFormat = "tt";
            return true;
        }

        if (MatchesAt(mask, position, "a/p") || MatchesAt(mask, position, "p/a"))
        {
            length = 3;
            netFormat = "t";
            return true;
        }

        length = 0;
        netFormat = string.Empty;
        return false;
    }

    /// <summary>
    /// Reports whether <paramref name="mask"/> carries <paramref name="lowercaseToken"/> at
    /// <paramref name="position"/>, comparing case-insensitively because PowerBuilder masks are.
    /// </summary>
    /// <param name="mask">The PowerBuilder mask.</param>
    /// <param name="position">The scan position.</param>
    /// <param name="lowercaseToken">The token to look for, which must already be lowercase.</param>
    /// <returns><see langword="true"/> on a match; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Compared character by character against the invariant lowercase form rather than through a
    /// culture-aware comparison, so that no locale can make a designator match or fail to match.
    /// </remarks>
    private static bool MatchesAt(string mask, int position, string lowercaseToken)
    {
        if (position + lowercaseToken.Length > mask.Length)
        {
            return false;
        }

        for (int offset = 0; offset < lowercaseToken.Length; offset++)
        {
            if (char.ToLowerInvariant(mask[position + offset]) != lowercaseToken[offset])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Counts the run of characters at <paramref name="start"/> whose invariant lowercase form
    /// equals <paramref name="lowered"/>.
    /// </summary>
    /// <param name="mask">The PowerBuilder mask.</param>
    /// <param name="start">The index of the first character of the run.</param>
    /// <param name="lowered">The invariant lowercase form of the character at the start.</param>
    /// <returns>The run length, always at least one.</returns>
    /// <remarks>
    /// The run is what carries a token's width, so YYYY and yyYY are the same four-character year
    /// token: PowerBuilder masks are case-insensitive and a mixed-case run must not be split.
    /// </remarks>
    private static int RunLength(string mask, int start, char lowered)
    {
        int length = 1;

        while (start + length < mask.Length && char.ToLowerInvariant(mask[start + length]) == lowered)
        {
            length++;
        }

        return length;
    }

    /// <summary>
    /// Appends <paramref name="text"/> padded to <paramref name="alignment"/>, left-aligned for a
    /// negative width and right-aligned for a positive one.
    /// </summary>
    /// <param name="target">The buffer being built.</param>
    /// <param name="text">The already-rendered text.</param>
    /// <param name="alignment">The signed width; zero means no padding.</param>
    /// <remarks>
    /// DECISION 12. Padding is applied after the mask, which is the only order that produces the
    /// fixed-width columns at <c>w_test_filescanner.srw:L165-L167</c>. Text at least as wide as the
    /// field is never truncated; CHOICE 9 records that the width counts characters rather than
    /// display columns, which differs for the CJK text that call site pads.
    /// </remarks>
    private static void AppendAligned(StringBuilder target, string text, int alignment)
    {
        if (alignment == 0)
        {
            target.Append(text);
            return;
        }

        int width = alignment < 0 ? -alignment : alignment;
        int padding = width - text.Length;

        if (padding <= 0)
        {
            target.Append(text);
            return;
        }

        if (alignment < 0)
        {
            target.Append(text).Append(' ', padding);
        }
        else
        {
            target.Append(' ', padding).Append(text);
        }
    }
}
