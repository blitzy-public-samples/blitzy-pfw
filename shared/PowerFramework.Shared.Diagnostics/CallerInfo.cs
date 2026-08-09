// ==============================================================================================
//  CallerInfo - the current script identity primitive, in both of the shapes the legacy declares
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.common.pbl.src/getcurrentscript.srf (9 lines, DECLARATION ONLY)
//
//  ALSO READ, AND EQUALLY READ ONLY:
//      ws_objects/pfw.common.pbl.src/stacktrace.srf   the OTHER classic external prototype in this
//                                                     folder's source set. AAP 0.4.2.3 pairs the
//                                                     two substitutions - pfwStackTrace becomes
//                                                     System.Diagnostics.StackTrace, and
//                                                     pfwGetCurrentScript becomes the caller
//                                                     information attributes used here - so the two
//                                                     files are documented and formatted alike.
//
//  ORACLE STATUS  That path is READ ONLY: it is the behavioural oracle for parity testing, never an
//                 edit target (AAP 0.7.3 C-C). It was read, and it was not modified.
//
//  THE WHOLE OF THE LEGACY EVIDENCE, WHICH IS NINE LINES AND NO BODY
//  --------------------------------------------------------------------------------------------
//  getcurrentscript.srf declares a function object and two prototypes over it, and stops:
//
//      global type getcurrentscript from function_object            [getcurrentscript.srf:L3-L4]
//      end type
//
//      forward prototypes                                          [getcurrentscript.srf:L6]
//      global function string getcurrentscript () system library "pfw.dll" &
//          alias for "pfwGetCurrentScript"                          [getcurrentscript.srf:L7]
//      global function boolean getcurrentscript (ref string module, ref long lineno) &
//          system library "pfw.dll" alias for "pfwGetCurrentScript" [getcurrentscript.srf:L8]
//      end prototypes                                              [getcurrentscript.srf:L9]
//
//  Three properties of that declaration decide everything below:
//
//      1. THERE IS NO POWERSCRIPT BODY. Nothing was implemented in PowerScript, so there is no
//         legacy logic to port - only the contract. The two signatures ARE the specification.
//      2. BOTH PROTOTYPES SHARE ONE NATIVE ALIAS, pfwGetCurrentScript. The overload is resolved on
//         the PowerBuilder side by argument list, and a single native export serves both. The two
//         managed members below are therefore two views of ONE primitive, which is why the second
//         is defined as the decomposition of the first rather than as independent logic.
//      3. THE REPOSITORY CONTAINS ZERO CALL SITES. See the dedicated note below; it is the reason
//         no behaviour may be inferred beyond what the signatures state.
//
//  THE NATIVE SURFACE HERE IS THE SECOND OF TWO MECHANISMS, AND IT IS THE EASY ONE TO MISS
//  --------------------------------------------------------------------------------------------
//  AAP 0.6.5 establishes that this estate binds native code in TWO different ways, and records that
//  an early analysis which searched only for the conventional external-function keyword returned
//  zero results and would have missed this mechanism entirely. This file is an instance of it, so
//  the record is stated rather than left to be inferred:
//
//      1. PBNI CLASS BINDING      `global type <name> from function_object native "pfw.dll"`,
//                                 with the binding on the TYPE
//      2. CLASSIC EXTERNAL        `... system library "pfw.dll" alias for "<export>"`, with the
//         PROTOTYPE               binding on the PROTOTYPE and no native clause on the type
//
//  getcurrentscript.srf is mechanism 2, and the distinction is visible in the source: its type
//  declaration is a bare `global type getcurrentscript from function_object` with NO native clause
//  [getcurrentscript.srf:L3], and the whole of the binding lives on the two prototypes
//  [getcurrentscript.srf:L7-L8]. No C++ source for pfw.dll exists anywhere in the repository, so
//  the prototypes are the entirety of the available evidence.
//
//  THE SUBSTITUTION, AND EXACTLY WHAT DIFFERS ACROSS IT (C-K)
//  --------------------------------------------------------------------------------------------
//  AAP 0.6.5 classifies current-script retrieval as SUBSTITUTE, alongside thread identifier
//  generation, thread waiting and stack trace. It is NOT a deliberate non-port and NOT a parity
//  risk: the single genuine parity risk in the in-scope set is pinyin first-letter matching, which
//  lives in DataServices and not here. AAP 0.4.2.3 names the substitute:
//
//      pfwGetCurrentScript, exported from the closed pfw.dll   ->   the System.Runtime.
//                                                                  CompilerServices caller
//                                                                  information attributes
//
//  PRESERVED across the substitution: both shapes, their names, their return types, the `ref`
//  parameter passing of the second, and the pairing of a script identity with a line number.
//
//  DIFFERENT across the substitution, and this is the honest core of the port:
//
//      pfwGetCurrentScript INTERROGATED THE RUNTIME. It was a native export reaching into the
//      PowerBuilder virtual machine's live state, so it reported the DYNAMIC caller - the script
//      actually executing at that instant - and would have reported correctly even when reached
//      through an indirect or dynamically dispatched invocation.
//
//      CALLER INFORMATION ATTRIBUTES ARE RESOLVED BY THE COMPILER. Roslyn substitutes each omitted
//      argument with a literal at every CALL SITE, so the values describe the LEXICAL caller and
//      are fixed at compile time. Nothing is inspected at run time and there is no virtual machine
//      to ask.
//
//  THE CONSEQUENCE, STATED PLAINLY SO IT CANNOT SURPRISE ANYONE. Call either member from inside a
//  helper and it reports THE HELPER'S OWN LOCATION, not the location of whoever called the helper.
//  That is not a defect in this file; it is what compile-time substitution means. The legacy's
//  effect is recovered by FORWARDING: give the helper its own caller information parameters and
//  pass them through, so the substitution happens at the helper's call site instead.
//
//      // Reports the helper - the substitution happened inside Helper.
//      static string Helper() => CallerInfo.GetCurrentScript();
//
//      // Reports Helper's caller - the substitution happened at the call to Helper, and Helper
//      // forwards the three values it was given instead of letting them be substituted again.
//      static string Helper(
//          [CallerMemberName] string memberName = "",
//          [CallerFilePath] string filePath = "",
//          [CallerLineNumber] int lineNumber = 0)
//          => CallerInfo.GetCurrentScript(memberName, filePath, lineNumber);
//
//  Forwarding is the ONLY sanctioned way to pass these arguments explicitly, and the forwarding
//  member is the only place a value may be supplied by hand. Anywhere else, passing them defeats
//  the substitution and silently reports a location that is simply wrong - see the warning on both
//  members. Note also that forwarding cannot be made transitive by accident: a helper that forwards
//  must itself declare the three parameters, so the chain is visible in every signature it passes
//  through.
//
//  ZERO CALL SITES, WHICH IS WHY THIS FILE IS API PARITY AND NOTHING MORE
//  --------------------------------------------------------------------------------------------
//  A case-insensitive search of ws_objects/** for getcurrentscript matches exactly ONE file: the
//  declaration itself. There is no consumer in the framework, none in the 47 test windows of
//  ws_objects/pfw.tests.pbl.src and none in the demos. Three consequences follow, and recording
//  them here saves a future reader from hunting for a fixture that does not exist:
//
//      * THERE IS NOTHING TO CHARACTERISE. No workflow exercises this primitive, so no paired
//        legacy and .NET recording under characterization/recordings can compare against it. The
//        parity criterion for this file is the SHAPE of its API, not recorded output.
//      * NO CALLER CONSTRAINS THE SEMANTICS, so none may be invented. Nothing here validates an
//        argument, throws, logs or reports, because there is no evidence that the legacy did any
//        of those things and C-C forbids inferring behaviour the source does not state.
//      * BOTH SHAPES ARE KEPT ANYWAY. The absence of callers is emphatically NOT licence to
//        rationalise two overloads into one: the declaration is the published contract and C-B
//        forbids narrowing it as firmly as it forbids extending it.
//
//  THE COMPOSED TEXT FORMAT IS NOT A FREE CHOICE
//  --------------------------------------------------------------------------------------------
//  pfw.dll's output cannot be read, so the text has to be defined rather than copied - and the
//  sibling StackTraceProvider.cs already defined exactly this shape for a captured frame, in order
//  to satisfy the four positional searches that assert.srf:L39-L59 performs on every frame. The
//  same shape is emitted here so that the two diagnostics surfaces of this project read alike and a
//  value from either one parses identically:
//
//      <scope>.<member> line:<lineNumber>
//
//  All four of that parser's invariants hold for every value this file can produce, including every
//  degraded one:
//
//      I1  at least one dot exists, so a last-dot search never returns 0
//      I2  a space follows the last dot
//      I3  a colon follows that space
//      I4  the value ends in decimal digits
//
//  A value carrying exactly one dot - which is the normal case here, because the scope is a single
//  segment - is a DEFINED branch of that parser and not a degenerate one: with one dot its window
//  and object readings become the same value [assert.srf:L49-L51]. The degenerate case is a missing
//  colon, which I3 rules out unconditionally.
//
//  THE ABSOLUTE SOURCE PATH NEVER LEAVES THIS FILE, AND THAT IS A DELIBERATE DECISION
//  --------------------------------------------------------------------------------------------
//  [CallerFilePath] is substituted with the path of the source file AS SEEN BY THE COMPILER ON THE
//  BUILD MACHINE, which is normally an absolute path. It is reduced to its file name here and the
//  directory portion is discarded immediately, for four separate reasons that happen to agree:
//
//      1. HYGIENE. AAP 0.8.6 R5 records that the legacy build definitions already embed developer
//         workstation absolute paths under F:\pfw\. Emitting a build machine path into a returned
//         string - which a caller may well put in a log record, an error payload or a response body
//         - would create a new instance of exactly that problem. The directory never appears in any
//         value this file returns.
//      2. THE PARSER. A file name carries the dot of its extension. Left in place it would become
//         the LAST dot of the value and move the parser's member-name window onto "cs", so the
//         extension is stripped. This is the same reason StackTraceProvider.cs keeps the path out
//         of a frame entirely: nothing bearing a dot may follow the member name.
//      3. DETERMINISM. Directory.Build.props sets ContinuousIntegrationBuild when CI is set, which
//         normalizes the source paths the compiler embeds. The DIRECTORY therefore differs between
//         a local build and a CI build while the FILE NAME does not, so composing from the file
//         name alone is what makes a local value and a CI value comparable.
//      4. IT IS THE ONLY SCOPE AVAILABLE. Caller information supplies no namespace and no declaring
//         type - only a member name, a file path and a line number. By C# convention a file is
//         named for the type it declares, so its name is the closest available stand-in for the
//         declaring scope, and reaching for the real one would mean inspecting the call stack,
//         which this file must not do.
//
//  CONSTRAINTS DISCHARGED HERE
//  --------------------------------------------------------------------------------------------
//  review_rules reports "No user rules provided", so no user rule governs this file and none was
//  invented. In their place AAP 0.7.2 (the enterprise baseline) and 0.7.3 (the twelve binding
//  non-rule constraints) apply. The four that bind this file:
//
//      C-A  Pure behaviour, no I/O. Every member is a pure function of its arguments. There is no
//           file access, no logging, no networking, no serialization and no reflection over
//           anything on disk. Zero package references, Base Class Library only. Notably there is
//           no System.IO dependency at all: the file name is extracted by scanning characters,
//           for the reason given on the scope helper.
//      C-B  No new features and no behaviour improvements. Exactly the two declared shapes, no
//           more and no fewer. See the deliberately-absent list below.
//      C-C  The legacy tree is read only and is the only specification. getcurrentscript.srf was
//           read and not modified, and every claim above cites the line it came from.
//      C-K  Every technology-specific and boundary-specific decision is documented at its point of
//           reproduction. The substitution, the lexical versus dynamic difference, the forwarding
//           pattern, the zero-call-site finding, the text format, the path hygiene decision, the
//           `ref` versus `out` ruling and the int-to-long widening each have their own block, above
//           or on the member that implements them.
//
//  This file declares NO SCREAMING_SNAKE and no underscore bearing identifier. That is a build
//  requirement rather than a preference: TreatWarningsAsErrors is inherited from
//  Directory.Build.props, and the repository root .editorconfig scopes its CA1707 and IDE1006
//  suppressions to the individual files on its BAND 3 roster - the single source of truth for that
//  list - that genuinely carry preserved legacy constant
//  identifiers. No file in this project is one of them, so an identifier here that needed a
//  suppression would be a build error with no way to grant it.
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON (C-B). Recorded so the omissions read as decisions
//  rather than oversights, and so nobody "completes" this file by adding one:
//
//      * A [CallerArgumentExpression] overload. Not declared in the legacy, and it answers a
//        different question - what an argument looked like, not which script is running.
//      * A GetCurrentModule convenience returning only the scope, or a GetCurrentLine returning
//        only the number. The second shape already exposes both parts separately; a third member
//        would be a new feature.
//      * An overload taking an explicit frame offset. stacktraceinfo.srf declares an offset
//        parameter and StackTraceProvider.cs therefore has one; getcurrentscript.srf declares
//        nothing of the kind, and an offset is meaningless against compile-time substitution
//        anyway because there is no stack to walk.
//      * A column number, and any accessor returning the raw absolute path. Neither is declared,
//        and the second is exactly what the hygiene decision above exists to prevent.
//      * Any throw, and any argument validation. See the zero-call-sites note.
//      * A StringBuilder, a cache or any memoisation. No performance objective is published
//        anywhere in this repository, so none may be claimed as justification (AAP 0.8.5).
//
//  DEPENDENCIES AND SELF-CONTAINMENT
//  --------------------------------------------------------------------------------------------
//  Two Base Class Library namespaces and nothing else. There is deliberately NO
//  `using System.Diagnostics;`: StackTraceProvider.cs owns stack inspection, keeping the two
//  separate is the entire point of having two files, and omitting the using also makes the name
//  collision between System.Diagnostics.StackTrace and this project's own
//  PowerFramework.Shared.Diagnostics namespace structurally impossible rather than merely avoided.
//
//  This file references NONE of its three siblings - Assert.cs, AssertionFailure.cs and
//  StackTraceProvider.cs - and none of them references it. The three format constants below
//  therefore RESTATE the values StackTraceProvider.cs declares rather than sharing them. That is
//  deliberate twice over: sharing would create precisely the coupling this file is required not to
//  have, and the originals are `private const` there so they are unreachable in any case. The
//  values must stay identical, which is what the format block above is for. PowerFramework.Shared.
//  Kernel is not imported either: this project references it for the return-code predicates that
//  the assertion overloads guard on, and nothing in THIS file needs a symbol from it.
// ==============================================================================================

using System.Globalization;
using System.Runtime.CompilerServices;

namespace PowerFramework.Shared.Diagnostics;

/// <summary>
/// The current script identity primitive ported from the legacy <c>pfw.common</c> library's
/// <c>getcurrentscript</c> function object, in both of the shapes it declares.
/// </summary>
/// <remarks>
/// <para>
/// Both members substitute for <c>pfwGetCurrentScript</c>, a classic external prototype bound to
/// the closed <c>pfw.dll</c> for which no source exists anywhere in the repository. The legacy
/// declares two prototypes over one native alias and no PowerScript body at all:
/// <c>global function string getcurrentscript () system library "pfw.dll" alias for
/// "pfwGetCurrentScript"</c> [getcurrentscript.srf:L7] and <c>global function boolean
/// getcurrentscript (ref string module, ref long lineno) system library "pfw.dll" alias for
/// "pfwGetCurrentScript"</c> [getcurrentscript.srf:L8]. The substitute is the
/// <see cref="CallerMemberNameAttribute"/> family of caller information attributes, per AAP
/// section 0.4.2.3.
/// </para>
/// <para>
/// <b>The substitution reports the LEXICAL caller, where the legacy reported the DYNAMIC one.</b>
/// Caller information arguments are baked in by the compiler at each call site, so they describe
/// the code that lexically contains the call. <c>pfwGetCurrentScript</c> interrogated the
/// PowerBuilder virtual machine's live state instead, so it described whatever was actually
/// executing and stayed correct through an indirect or dynamic invocation. The practical
/// consequence is that calling either member from inside a helper reports the helper, unless the
/// helper declares its own caller information parameters and forwards them. The file header gives
/// that forwarding pattern in full.
/// </para>
/// <para>
/// <b>Callers must not pass the caller information arguments explicitly</b>, other than in a
/// forwarding member written for that purpose. Supplying one stops the compiler substituting it,
/// and the reported location becomes whatever was passed instead - wrong, and silently so.
/// </para>
/// <para>
/// <b>No returned value ever contains the absolute source path.</b> <c>[CallerFilePath]</c> is
/// substituted with the build machine's view of the file, so only its file name is used and the
/// directory is discarded. The header records the four reasons, of which the operative one is
/// hygiene: nothing here may put a build machine path where a caller might log it.
/// </para>
/// <para>
/// The legacy declaration has <b>no call site anywhere in the repository</b>, so there is no
/// observable behaviour to characterise and no oracle recording to compare against. This file is
/// API parity, and both shapes are reproduced exactly as declared.
/// </para>
/// <para>
/// Nothing here validates an argument and nothing throws. Every member returns a value for every
/// input, degrading to a documented token when caller information is unavailable. Every member is
/// a pure function of its arguments with no static mutable state, so all are safe to call
/// concurrently from any thread.
/// </para>
/// </remarks>
public static class CallerInfo
{
    // ------------------------------------------------------------------------------------------
    // DECISION 1 - THE THREE FORMAT TOKENS, RESTATED RATHER THAN SHARED
    // ------------------------------------------------------------------------------------------
    // These three literals plus the dot that joins scope to member are the whole of the composed
    // text format. Their values are identical to the ones StackTraceProvider.cs declares for a
    // captured frame, and they are RESTATED here rather than referenced because this file is
    // required to be independent of its three siblings - and because the originals are private
    // const there, so they are unreachable regardless. They must stay identical: the format block
    // in the file header is the contract both files answer to, and a divergence would mean this
    // project emitted two different diagnostics formats from one namespace.
    //
    // Do not add a file path token. The header's hygiene decision explains why the directory is
    // discarded, and the format block explains why a dot may never follow the member name.
    // ------------------------------------------------------------------------------------------
    private const string ScopeSeparator = ".";
    private const string LineNumberIntroducer = " line:";
    private const string UnknownScopeName = "<unknown>";
    private const string UnknownMemberName = "?";

    // ------------------------------------------------------------------------------------------
    // DECISION 2 - THE SECOND SHAPE IS THE DECOMPOSITION OF THE FIRST, NOT A SECOND ALGORITHM
    // ------------------------------------------------------------------------------------------
    // Both legacy prototypes bind ONE native export, pfwGetCurrentScript, and are distinguished
    // only by argument list [getcurrentscript.srf:L7-L8]. They are therefore two views of a single
    // primitive, and this file reproduces that relationship exactly rather than writing the logic
    // twice: the string shape composes what the boolean shape hands back in pieces, so
    //
    //      GetCurrentScript() == module + " line:" + lineNo
    //
    // holds for every input, where module and lineNo are what the boolean shape wrote at the same
    // call site. Both shapes share the same two private helpers, so the scope derivation and the
    // member-name degradation cannot drift between them. That identity is asserted by test rather
    // than assumed.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the identity of the calling script as one string: the calling member qualified by
    /// its source file's name, followed by the line number of the call.
    /// </summary>
    /// <param name="memberName">
    /// Substituted by the compiler with the name of the calling member.
    /// <b>Do not pass this argument</b> except from a forwarding member, as described in the
    /// remarks.
    /// </param>
    /// <param name="filePath">
    /// Substituted by the compiler with the path of the calling source file as the build machine
    /// saw it. Only its file name is used; the directory is discarded and never appears in the
    /// returned value. <b>Do not pass this argument</b> except from a forwarding member.
    /// </param>
    /// <param name="lineNumber">
    /// Substituted by the compiler with the line number of the call.
    /// <b>Do not pass this argument</b> except from a forwarding member.
    /// </param>
    /// <returns>
    /// <c>&lt;scope&gt;.&lt;member&gt; line:&lt;lineNumber&gt;</c>, for example
    /// <c>OrderService.Submit line:42</c>. Never <see langword="null"/> and never empty: when
    /// caller information is unavailable the scope degrades to <c>&lt;unknown&gt;</c>, the member to
    /// <c>?</c> and the line number to <c>0</c>, which still yields a well formed value.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ports <c>global function string getcurrentscript () system library "pfw.dll" alias for
    /// "pfwGetCurrentScript"</c> [getcurrentscript.srf:L7]. The legacy prototype takes no argument;
    /// the three parameters here are compiler supplied substitutions and are the mechanism of the
    /// port, not an extension of the contract. A call written as <c>CallerInfo.GetCurrentScript()</c>
    /// therefore matches the legacy call shape exactly.
    /// </para>
    /// <para>
    /// The returned shape is the same one <c>StackTraceProvider</c> emits for a captured frame, so a
    /// value from either surface reads and parses identically. The file header records the format
    /// contract, the four invariants it upholds, and why the source file's directory and extension
    /// are both absent.
    /// </para>
    /// <para>
    /// <b>The reported location is the LEXICAL call site.</b> Called from a helper, this reports the
    /// helper. To report the helper's caller, give the helper its own caller information parameters
    /// and forward them to the three parameters here; that is the only sanctioned reason to pass
    /// them by hand. Passing them anywhere else silently reports the wrong location, because the
    /// compiler stops substituting as soon as an argument is supplied.
    /// </para>
    /// </remarks>
    public static string GetCurrentScript(
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        // Composed through the same two helpers the boolean shape uses, so the two can never
        // disagree about the scope or the member. See DECISION 2.
        return DescribeScope(filePath) + ScopeSeparator + DescribeMember(memberName) +
            LineNumberIntroducer + lineNumber.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Writes the identity of the calling script into its two parts: the qualified member into
    /// <paramref name="module"/> and the line number of the call into <paramref name="lineNo"/>.
    /// </summary>
    /// <param name="module">
    /// Receives <c>&lt;scope&gt;.&lt;member&gt;</c>, that is the value
    /// <see cref="GetCurrentScript(string, string, int)"/> returns without its
    /// <c>line:&lt;lineNumber&gt;</c> tail. Always written, on both the <see langword="true"/> and
    /// the <see langword="false"/> return; never <see langword="null"/> and never empty. Its
    /// incoming value is never read.
    /// </param>
    /// <param name="lineNo">
    /// Receives the line number of the call, widened from the <see cref="int"/> the compiler
    /// supplies. Always written, on both returns. Its incoming value is never read.
    /// </param>
    /// <param name="memberName">
    /// Substituted by the compiler with the name of the calling member.
    /// <b>Do not pass this argument</b> except from a forwarding member.
    /// </param>
    /// <param name="filePath">
    /// Substituted by the compiler with the path of the calling source file as the build machine
    /// saw it. Only its file name is used; the directory is discarded and never reaches
    /// <paramref name="module"/>. <b>Do not pass this argument</b> except from a forwarding member.
    /// </param>
    /// <param name="lineNumber">
    /// Substituted by the compiler with the line number of the call.
    /// <b>Do not pass this argument</b> except from a forwarding member.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the calling member's identity was determined, which at any
    /// genuine C# call site is always the case because the compiler always substitutes a member
    /// name. <see langword="false"/> only when caller information was suppressed by an explicit
    /// empty argument, in which case both parameters still receive their documented degraded values
    /// rather than being left as the caller set them.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ports <c>global function boolean getcurrentscript (ref string module, ref long lineno)
    /// system library "pfw.dll" alias for "pfwGetCurrentScript"</c> [getcurrentscript.srf:L8]. Both
    /// legacy prototypes bind the same native alias, so this shape is defined as the decomposition
    /// of the string shape and shares its helpers; see DECISION 2 for the identity that ties them
    /// together.
    /// </para>
    /// <para>
    /// <b>The parameters are <c>ref</c> and not <c>out</c>, deliberately.</b> AAP section 0.4.5.2
    /// maps a PowerBuilder <c>ref</c> parameter to a C# <c>ref</c> parameter, and the legacy
    /// signature is the published contract rather than a starting point to modernise. The
    /// observable difference for a caller is that C# requires a <c>ref</c> argument to be
    /// initialised before the call, so a caller writes <c>string module = string.Empty; long lineNo
    /// = 0;</c> and then calls. In exchange the caller keeps the ability to pass a pre-initialised
    /// variable, which <c>out</c> would take away. This method never reads either incoming value,
    /// so the initialiser a caller chooses cannot affect the result.
    /// </para>
    /// <para>
    /// <b><paramref name="lineNo"/> is <see cref="long"/> because the legacy declares
    /// <c>long</c></b>, and AAP section 0.4.5.2 maps PowerBuilder <c>long</c> to C# <c>long</c>.
    /// <see cref="CallerLineNumberAttribute"/> supplies an <see cref="int"/>, so the widening is
    /// written out explicitly below rather than left implicit. The conversion cannot lose
    /// information in that direction, and the parameter width is not narrowed to match the source
    /// of the value because the declared contract, not the substitution mechanism, decides it.
    /// </para>
    /// <para>
    /// The same lexical versus dynamic caveat applies as to
    /// <see cref="GetCurrentScript(string, string, int)"/>: the reported location is the lexical
    /// call site, and forwarding is what recovers the legacy's behaviour through a helper.
    /// </para>
    /// </remarks>
    public static bool GetCurrentScript(
        ref string module,
        ref long lineNo,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        // The caller information parameters are optional and must therefore follow the two required
        // ref parameters. That ordering is a hard C# rule, not a stylistic choice, and it is the
        // reason the two shapes list their compiler supplied parameters in different positions.
        string member = DescribeMember(memberName);

        // Both parameters are written unconditionally, before the outcome is decided, so a caller
        // that ignores the return value still observes documented values rather than whatever it
        // happened to initialise. Explicit widening: see the int-to-long note in the remarks.
        module = DescribeScope(filePath) + ScopeSeparator + member;
        lineNo = (long)lineNumber;

        // The identity was determined unless the member degraded to the unknown token, which
        // DescribeMember produces only when no usable member name was supplied. At a real call site
        // the compiler always supplies one, so this is true; it can be false only when an explicit
        // argument suppressed the substitution.
        return !string.Equals(member, UnknownMemberName, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------
    // DECISION 3 - THE FILE NAME IS EXTRACTED BY SCANNING, NOT BY System.IO.Path
    // ------------------------------------------------------------------------------------------
    // Path.GetFileNameWithoutExtension is the obvious call and it is the wrong one here, for a
    // reason that only shows up across machines. [CallerFilePath] is baked in by the compiler on
    // the BUILD machine, so a Windows build embeds a backslash separated path - and on Linux, where
    // this code runs in a container, Path does not treat a backslash as a directory separator at
    // all. Path.GetFileName would then return the WHOLE path, and the absolute directory the
    // hygiene decision exists to suppress would leak into a returned value on exactly the platform
    // that matters. Scanning for the last occurrence of '/', '\' or ':' is correct for a path
    // produced on either platform and consumed on either platform, because all three characters are
    // illegal inside a file name on both. It also keeps this file free of any System.IO reference,
    // which C-A wants anyway.
    //
    // The extension is stripped because the parser reads the dot before the member name as the
    // separator that ends the scope: left in place, "OrderService.cs.Submit" would report "cs" as
    // the object. The search for it stops short of the first character of the file name, so a name
    // that begins with a dot keeps its text instead of reducing to nothing.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Reduces a caller supplied source path to the scope portion of a composed value: its file
    /// name, with the directory and the extension both removed.
    /// </summary>
    /// <param name="filePath">
    /// The value <see cref="CallerFilePathAttribute"/> supplied, which is normally an absolute path
    /// on the build machine. Never <see langword="null"/>, because the parameters it arrives
    /// through default to <see cref="string.Empty"/> rather than <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The file name without its extension, or <c>&lt;unknown&gt;</c> when the path is empty or
    /// carries no file name portion. Never <see langword="null"/> and never empty, so the composed
    /// value always has text before its first dot.
    /// </returns>
    /// <remarks>
    /// See <c>DECISION 3</c> above for why the scan is hand written rather than delegated to
    /// <c>System.IO.Path</c>, and the file header for the four reasons the directory is discarded.
    /// A file name containing further dots, such as <c>Order.Service.cs</c>, keeps them and yields a
    /// scope of <c>Order.Service</c>; that only changes which segment a consumer reads as the
    /// object and cannot break the format's invariants, so it is tolerated rather than rewritten.
    /// </remarks>
    private static string DescribeScope(string filePath)
    {
        // Walk back to the character after the last separator. Nothing found means the whole string
        // is already a bare file name, so the start stays at 0.
        int nameStart = 0;
        for (int index = filePath.Length - 1; index >= 0; index--)
        {
            char candidate = filePath[index];
            if (candidate == '/' || candidate == '\\' || candidate == ':')
            {
                nameStart = index + 1;
                break;
            }
        }

        // Walk back to the extension's dot, stopping strictly AFTER the first character of the file
        // name so that a leading dot is not mistaken for an extension separator. Nothing found
        // means there is no extension and the whole file name is the scope.
        int nameEnd = filePath.Length;
        for (int index = filePath.Length - 1; index > nameStart; index--)
        {
            if (filePath[index] == '.')
            {
                nameEnd = index;
                break;
            }
        }

        // An empty path, or one ending in a separator, leaves nothing to name the scope with. The
        // token keeps the value well formed rather than emitting a leading dot.
        return nameEnd > nameStart
            ? filePath.Substring(nameStart, nameEnd - nameStart)
            : UnknownScopeName;
    }

    /// <summary>
    /// Reduces a caller supplied member name to the member portion of a composed value, stripping
    /// the leading dot that a constructor's name carries.
    /// </summary>
    /// <param name="memberName">
    /// The value <see cref="CallerMemberNameAttribute"/> supplied. Never <see langword="null"/>,
    /// because the parameters it arrives through default to <see cref="string.Empty"/> rather than
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The member name without any leading dot, or <c>?</c> when no usable name was supplied. Never
    /// <see langword="null"/>, never empty and never beginning with a dot, so exactly one dot
    /// separates the scope from the member in the composed value.
    /// </returns>
    /// <remarks>
    /// The strip is not defensive padding: a call made in an instance constructor is substituted
    /// with <c>.ctor</c> and one in a static constructor with <c>.cctor</c>, so without it the scope
    /// would end in a stray second dot and a consumer would read <c>ctor</c> as the member while the
    /// portion before it came back empty. The sibling <c>StackTraceProvider</c> performs the same
    /// strip on a reflected method name for the same reason, which is what keeps a composed value
    /// and a captured frame interchangeable.
    /// </remarks>
    private static string DescribeMember(string memberName)
    {
        if (memberName.Length == 0)
        {
            return UnknownMemberName;
        }

        if (memberName[0] == '.')
        {
            // ".ctor" -> "ctor", ".cctor" -> "cctor". A name that is nothing but a dot would leave
            // the member portion empty, so it degrades to the token instead.
            return memberName.Length > 1 ? memberName.Substring(1) : UnknownMemberName;
        }

        return memberName;
    }
}
