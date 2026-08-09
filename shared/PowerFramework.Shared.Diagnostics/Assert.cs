// ==================================================================================================
//  Assert.cs - the assertion guards and the seven field assertion failure payload
//  ------------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.common.pbl.src/assert.srf (125 lines)
//  ORACLE STATUS  That .srf is the ONLY specification for this behaviour, and it is READ ONLY. Every
//                 guard, every arithmetic step, every literal and every ordering decision below
//                 cites the line of it that authorises the decision, because AAP 0.1.4 establishes
//                 there is no other statement of intended behaviour anywhere to consult.
//
//  SEVEN MORE LEGACY PATHS WERE READ AS SPECIFICATION AND ARE EQUALLY READ ONLY:
//      ws_objects/pfw.common.pbl.src/assertionfailed.sru        the thrown type. Seven payload
//                                                              members [:L18-L24], `from
//                                                              runtimeerror` [:L4,L8].
//      ws_objects/pfw.shared.pbl.src/issucceeded.srf            the long guard. Null returns false
//                                                              [:L11]; the test is `>= RetCode.OK`
//                                                              [:L12].
//      ws_objects/pfw.shared.pbl.src/isvalidobject.srf          the object guard. Null returns false
//                                                              [:L15]; then `IsValid` [:L16].
//      ws_objects/pfw.shared.pbl.src/retcode.sru                OK = 0 [:L39], PREVENT = 1 [:L42],
//                                                              CANCELED and CANCELLED = -2
//                                                              [:L44-L45]. The two values that
//                                                              demonstrate the tri state hole.
//      ws_objects/pfw.pbl.src/pfw.sra                           the CONSUMER of the payload
//                                                              [:L111-L144], including its splitter
//                                                              [:L48-L69].
//      ws_objects/pfw.tests.pbl.src/w_test_assert.srw            the behavioural oracle. REFERENCE
//                                                              only per AAP 0.2.2.3: never ported
//                                                              and never edited.
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru
//                                                              three of the seven live production
//                                                              call sites [:L276,L304,L313], all of
//                                                              them the (bool, string) overload
//                                                              inside `#IF DEFINED DEBUG` blocks.
//
//  ================================================================================================
//  THE CLASS IS NAMED Assertions, THE MEMBERS ARE NAMED Assert AND AssertFailed
//  ================================================================================================
//  The legacy PowerBuilder function object is named `assert` and declares a subroutine also named
//  `assert` [assert.srf:L3,L8]. `Assert.Assert(...)` is NOT EXPRESSIBLE IN C#: a class may not
//  contain a member with the same name as the class unless that member is a constructor, which is
//  compiler error CS0542, and the restriction applies to static members too. One of the two names
//  therefore had to give, and the ruling is:
//
//      * the FILE keeps the name Assert.cs, which AAP 0.3.1 fixes, and which C# permits because it
//        does not require a file name to match a type name;
//      * the MEMBERS keep the names Assert and AssertFailed, because those are the legacy visible
//        identifiers that appear at every live call site and in the oracle;
//      * the CONTAINING CLASS is named Assertions - role named and plural, exactly what AAP 0.4.5.2
//        prescribes for a `*.srf` global function ("a static method on a role named static class")
//        and exactly the convention the sibling Kernel project already established with Predicates,
//        Bits, Formatting, Text and Ancestry.
//
//  Downstream callers therefore write Assertions.Assert(...) and Assertions.AssertFailed(...).
//
//  A second, independent reason the class may not be called Assert: the sibling test project's
//  suites are in namespace PowerFramework.Shared.Diagnostics.Tests and use Xunit's own Assert type.
//  A type named Assert in the enclosing PowerFramework.Shared.Diagnostics namespace would collide
//  with it in every one of those files. The ruling above avoids that collision as a side effect
//  rather than by design, and the collision is recorded so nobody "simplifies" the name later.
//
//  ================================================================================================
//  GOVERNING CONSTRAINTS, AND HOW THIS FILE SATISFIES EACH
//  ================================================================================================
//  There are NO user specified rules for this project. The rules document was retrieved and it
//  contains exactly one statement: that no user rules were provided. Nothing is therefore invented,
//  inferred or back filled from convention in their place, and the absence is not read as licence to
//  lower the bar. The binding constraints instead are AAP 0.7.2, the enterprise standard baseline,
//  and AAP 0.7.3, the twelve non rule constraints. Five bind this file:
//
//      C-A  Pure behaviour, no I/O. Nothing here logs, writes a file, opens a socket, reads
//           configuration or touches the console. There is no static mutable state, so every member
//           is safe to call from any thread. The project carries zero package references and this
//           file imports nothing outside the Base Class Library and the sibling Kernel project.
//      C-B  No new features, no behaviour improvements, documented defects replicated rather than
//           corrected. FIVE preserved defects live in this file and each is annotated where it is
//           reproduced; they are listed together immediately below because every one of them looks
//           like a bug and none of them may be fixed.
//      C-C  The legacy tree is read only and is the only specification. All eight legacy paths above
//           were read and none was modified, moved or reformatted.
//      C-H  80 percent line coverage per in scope service, measured from the Cobertura report. The
//           payload builder is stack dependent and cannot be driven deterministically through the
//           public overloads alone, so it is exposed as an INTERNAL seam taking a supplied frame
//           list. See the seam's own remarks.
//      C-K  Every technology specific and boundary specific decision is documented at the point it
//           applies. The CS0542 rename above, the IsValid collapse, the CRLF versus LF discipline,
//           the one based indexing discipline, the frame order contract, the null literal overload
//           binding, the by value parameter choice and the JIT frame hazard each have their own
//           block, here or on the member that implements them.
//
//  FAIL FAST IS THE POINT OF THIS FILE (AAP 0.1.4, 0.6.7). AssertFailed ALWAYS throws. It never
//  returns, never logs and continues, never returns a result code and never swallows the failure.
//  The legacy's `HALT CLOSE` [pfw.sra:L143] is the CONSUMER's half of the protocol and deliberately
//  does not appear here: this project RAISES, and the Gateway composition root TERMINATES. Softening
//  the raise into a warning would be a behavioural change dressed up as robustness.
//
//  ================================================================================================
//  THE FIVE PRESERVED DEFECTS. EVERY ONE LOOKS LIKE A BUG. NONE MAY BE FIXED (C-B)
//  ================================================================================================
//  D1  THE TRI STATE HOLE IS INHERITED THROUGH THE GUARD. The long overloads guard on IsSucceeded,
//      whose test is `>= RetCode.OK` [issucceeded.srf:L12], so Assert(RetCode.PREVENT) NEVER FIRES
//      even though a prevention is not a success in any ordinary reading, while
//      Assert(RetCode.CANCELLED) does fire and Assert(null) fires. Annotated on both long overloads.
//
//  D2  BOTH PAYLOAD SHAPES SURVIVE, INCLUDING THE SHALLOW ONE. A frame count of 2 or less produces a
//      two field payload with no window, object, event, line number or trace at all
//      [assert.srf:L37]. That is the shape a failed stack capture produces, and it is emitted
//      silently. Annotated on the shape gate inside the seam.
//
//  D3  THE STACK CAPTURE FAILURE IS SWALLOWED BY AN EMPTY HANDLER. The legacy writes
//      `catch(throwable ex1)` with an empty body and never reads ex1 [assert.srf:L31-L32], so a
//      capture failure leaves the count at zero and silently downgrades the payload to D2's shallow
//      shape. Annotated on the handler in AssertFailed.
//
//  D4  Info AND PAYLOAD FIELD 2 DIVERGE IN THE DEEP SHAPE. Field 2 is copied into Info first
//      [assert.srf:L35] and only afterwards is the location suffix appended to Info ALONE
//      [assert.srf:L71]. Field 2 never receives it, and is never re read. The oracle displays Info,
//      with the suffix [w_test_assert.srw:L100]; the consumer assigns its error text from field 2,
//      without it [pfw.sra:L118]. Annotated at the suffix.
//
//  D5  THE DORMANT try/catch AROUND THE THROW IS CARRIED ACROSS AS INERT COMMENTS. The legacy
//      comments out a try, a catch and a rethrow around the live throw [assert.srf:L82-L86]. It is
//      reproduced as comments, is not active, and MUST NOT BE REVIVED. Annotated at the throw.
//
//  ================================================================================================
//  NEWLINE DISCIPLINE. GET THIS WRONG AND THE PROTOCOL BREAKS SILENTLY
//  ================================================================================================
//  The FIELD DELIMITER is CRLF, the two characters "\r\n" [assert.srf:L19]. Every separator INSIDE a
//  field is a BARE LINE FEED "\n": the info continuation [assert.srf:L26], the trace join
//  [assert.srf:L63] and the Info location suffix [assert.srf:L71].
//
//  That asymmetry is load bearing rather than stylistic. It is the ONLY reason the consumer's split
//  on CRLF [pfw.sra:L115] yields exactly two fields or exactly seven. A CRLF used for any internal
//  separator would split one field into extra segments and defeat the exactly seven test
//  [pfw.sra:L119], silently costing the consumer the window, the object, the event, the line number
//  and the whole trace at once.
//
//  Environment.NewLine THEREFORE APPEARS NOWHERE IN THIS FILE, and may never be introduced. On
//  Linux, the target operating system, it is "\n", which would destroy the delimiter; on Windows it
//  is "\r\n", which would destroy the internal separators. Both roles are hard coded literals. A
//  test greps this file for the symbol and asserts zero occurrences.
//
//  Nothing here normalises, trims or otherwise adjusts the payload or any field. The consumer's very
//  first act is to split on CRLF, so any adjustment would shift every field index after it.
//
//  ================================================================================================
//  ONE BASED INDEXING DISCIPLINE (AAP 0.4.5.4, 0.8.6 R9)
//  ================================================================================================
//  AAP 0.4.5.4 names one based to zero based translation the single most dangerous mechanical hazard
//  in this refactor, because a silent off by one is indistinguishable from a behavioural regression.
//  Two mechanisms keep this file out of that trap, and neither is optional:
//
//      * THE FRAME ARRAY is only ever measured and indexed through the two centralized accessors the
//        sibling StackTraceProvider already published for exactly this purpose,
//        StackTraceProvider.UpperBound and StackTraceProvider.FrameAt. They are internal rather than
//        private specifically so this file can reuse them, and its own header says so. They are NOT
//        duplicated here.
//      * THE STRING PRIMITIVES Pos, LastPos, Left, Mid and Long are all one based in PowerScript, so
//        one based equivalents are implemented privately at the end of this file and the parse is
//        written in terms of them. That is what lets `Mid(sCalling, nPos2 + 1, nPos - nPos2 - 1)`
//        [assert.srf:L46] port CHARACTER FOR CHARACTER. Hand converting that expression into zero
//        based substring arithmetic is precisely how a silent off by one enters.
//
//  The one place where the port does not use an accessor is the FIELD LIST, and that is deliberate:
//  the legacy indexes `sMessages[1]` through `sMessages[7]` in strictly ascending contiguous order
//  and never re reads a field once written, so the faithful managed form is an ordered list appended
//  to in that same order. It therefore carries NO INDEX ARITHMETIC AT ALL, which is safer than any
//  accessor, and PowerScript's `UpperBound(sMessages)` [assert.srf:L74] becomes the list's count.
//
//  ================================================================================================
//  THE FRAME ORDER CONTRACT, AND WHY THE CAPTURE CANNOT MOVE INTO THE SEAM
//  ================================================================================================
//  StackTraceProvider returns frames OUTERMOST FIRST, so legacy index 1 is the outermost frame and
//  the count is the innermost. `sCallStack[nCount - 2]` [assert.srf:L38] therefore selects the frame
//  two above the innermost, skipping this project's own AssertFailed and the Assert overload that
//  called it, and landing on the USER's frame - the only frame an assertion report could usefully
//  name. Reversing that direction produces a report of exactly the right length that names the
//  assertion framework as the failure site, and a frame count assertion would still pass. An end to
//  end test drives a real nested call chain and asserts the reported member is the caller.
//
//  THE CAPTURE MUST STAY IN AssertFailed. This is structural, not stylistic. StackTraceProvider
//  excludes exactly its own frame, so called from AssertFailed the innermost captured frame IS
//  AssertFailed - which is precisely the legacy arrangement, where the native primitive is not a
//  PowerScript frame and the innermost frame is `assertfailed` itself [assert.srf:L30]. Capturing
//  inside the internal seam instead would insert the seam's own frame and make `nCount - 2` select
//  the Assert overload rather than the user's method.
//
//  THE JIT MUST NOT REMOVE A FRAME, AND THIS HAZARD HAS NO LEGACY ANALOGUE. PowerBuilder neither
//  inlines PowerScript functions nor reuses a caller's frame; .NET can do both, and either silently
//  changes what the ported arithmetic trims. Every public member here therefore carries
//  MethodImplOptions.NoInlining, which is what StackTraceProvider's own caller contract requires of
//  this file by name, and the six delegating Assert overloads additionally carry
//  MethodImplOptions.NoOptimization because each ends in a call in tail position - the one shape for
//  which a JIT may reuse the caller's frame instead of pushing a new one. That is the same mitigation
//  StackTraceProvider applies to its own three delegating overloads, for the same reason. Both are
//  FRAME PRESERVATION and are NOT a performance statement in either direction; AAP 0.8.5 forbids
//  asserting a performance objective anywhere in this refactor, and none is asserted here.
//
//  ================================================================================================
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//  ================================================================================================
//      * An eighth public member of any kind. The published surface is exactly the seven the legacy
//        declares [assert.srf:L7-L13]. In particular there is no public member carrying the literal
//        "assert": the legacy consumer discriminates on `Error.Object = "assert"` [pfw.sra:L114],
//        the name of the throwing PowerBuilder function object, and the .NET analogue of that test
//        is catching the AssertionFailure TYPE. Re exposing the string would widen the contract to
//        no end.
//      * Any overload the legacy does not declare. There is deliberately no Assert(bool?), no
//        Assert taking a message factory, no Assert with a caller info parameter and no generic
//        overload. C-B forbids the convenience.
//      * Argument validation of any kind. No argument is rejected, and info is neither trimmed nor
//        normalised. The legacy validates nothing and the empty string is a legal, common value.
//      * A conditional compilation attribute. Three of the seven live call sites sit inside the
//        legacy's own `#IF DEFINED DEBUG` blocks [n_cst_threading_task_sqlquery.sru:L275-L277], so
//        the DECISION to compile an assertion away belongs to the CALLER, exactly as it does in the
//        legacy. Marking these members [Conditional("DEBUG")] would move that decision here and
//        silently disable every assertion in a Release build, which is a behavioural change.
//      * Any logging, tracing or metric. C-A forbids I/O in this layer, and D3's swallow means a
//        capture failure is genuinely silent in the legacy.
//      * Any derivation from Kernel's PfwException. The Kernel dependency of this file is Predicates
//        and nothing else. AssertionFailure is a SIBLING of PfwException, not a subclass, and its
//        own file records what breaks if that is "unified": PfwException's setmessage prefixes every
//        message [pfwexception.sru:L23], which would corrupt payload field 1 and make the consumer's
//        numeric parse read 0 instead of -10000 [pfw.sra:L117].
// ==================================================================================================

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

using PowerFramework.Shared.Kernel;

namespace PowerFramework.Shared.Diagnostics;

/// <summary>
/// The assertion guards and the assertion failure payload builder, ported from
/// <c>ws_objects/pfw.common.pbl.src/assert.srf</c>.
/// </summary>
/// <remarks>
/// <para>
/// Seven members, matching the legacy prototype list one for one [assert.srf:L7-L13]: six
/// <c>Assert</c> guards over three argument shapes with and without an info string, and
/// <see cref="AssertFailed(string)"/>, which builds the payload and throws.
/// </para>
///
/// <para><b>NAMING DEVIATION, RECORDED DELIBERATELY.</b></para>
/// <para>
/// The legacy PowerBuilder function object is named <c>assert</c> and its subroutine is named
/// <c>assert</c> as well [assert.srf:L3,L8], so the legacy visible contract reads
/// <c>Assert.Assert(...)</c>. <b>C# cannot express that</b>: a class may not contain a member whose
/// name matches the class name unless the member is a constructor, which is compiler error
/// <b>CS0542</b>, and the restriction applies to static members too. The member names are the ones
/// worth keeping - they appear at every live call site and in the behavioural oracle - so the
/// CONTAINING CLASS took the new name instead. <c>Assertions</c> is role named and plural, which is
/// what the refactor's own object kind mapping prescribes for a global function object, and it
/// matches the sibling Kernel project's <c>Predicates</c>, <c>Bits</c>, <c>Formatting</c>,
/// <c>Text</c> and <c>Ancestry</c>. Callers write <c>Assertions.Assert(...)</c>. The file is still
/// named <c>Assert.cs</c>.
/// </para>
///
/// <para><b>FAIL FAST, NOT DEGRADE GRACEFULLY.</b></para>
/// <para>
/// A failing guard raises <see cref="AssertionFailure"/> and there is no other outcome: no return
/// code, no log line, no swallow. The legacy consumer decodes the payload and then halts the process
/// [pfw.sra:L115-L143]; that terminating half belongs to the Gateway composition root and is
/// deliberately not reproduced here.
/// </para>
///
/// <para><b>THREAD SAFETY.</b></para>
/// <para>
/// Every member is static and stateless, and the type holds no mutable state whatsoever, so all
/// seven are safe to call concurrently from any thread. Each call captures its own stack and builds
/// its own <see cref="AssertionFailure"/>.
/// </para>
/// </remarks>
public static class Assertions
{
    /// <summary>
    /// Builds the assertion failure payload from the current call stack and throws it. Never
    /// returns. [assert.srf:L7,L16-L87]
    /// </summary>
    /// <param name="info">
    /// Additional failure text, appended to payload field 2 after a bare line feed when it is not
    /// the empty string [assert.srf:L25-L27]. The empty string is the normal value from the
    /// info-less guards and suppresses the continuation entirely.
    /// </param>
    /// <exception cref="AssertionFailure">
    /// Always. This is the member's purpose, not an error path.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>THIS MEMBER ALWAYS THROWS</b>, which is why it carries
    /// <see cref="DoesNotReturnAttribute"/>. That attribute is not a behaviour change: it is the
    /// managed expression of the legacy contract that the routine's last act is an unconditional
    /// <c>throw</c> [assert.srf:L83]. Declaring it lets the compiler treat code after a call as
    /// unreachable, which suppresses spurious nullable and definite-assignment diagnostics at call
    /// sites - a caller may write <c>AssertFailed("...")</c> in place of a <c>return</c> without the
    /// compiler demanding a value afterwards.
    /// </para>
    /// <para>
    /// <b>THE STACK IS CAPTURED HERE AND NOWHERE ELSE.</b> See the frame order contract in this
    /// file's header: the provider excludes exactly its own frame, so the innermost captured frame is
    /// this method, which reproduces the legacy arrangement in which the native capture primitive is
    /// not a PowerScript frame [assert.srf:L30]. Moving the capture into
    /// <see cref="BuildFailure(string[], int, string)"/> would insert an extra frame and shift the
    /// frame the report blames.
    /// </para>
    /// <para>
    /// A capture failure is SWALLOWED, leaving the frame count at zero and silently producing the
    /// two-field shallow payload. That is preserved defect <b>D3</b>; see the handler below and the
    /// header.
    /// </para>
    /// <para>
    /// Payload assembly itself lives in <see cref="BuildFailure(string[], int, string)"/> so that
    /// both payload shapes and all three frame-parse branches are drivable from tests without a real
    /// stack. This member is therefore exactly three steps: capture, build, throw.
    /// </para>
    /// </remarks>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void AssertFailed(string info)
    {
        // assert.srf:L17 - `string sCallStack[]`, an empty array local. Initialised rather than left
        // to the `out` parameter for a reason that matters: if the capture below throws, the `out`
        // may never be written, and this initialisation is what guarantees the swallow still leaves
        // an indexable array behind. The legacy local is likewise empty, never null.
        string[] callStack = [];

        // assert.srf:L16 declares `long nPos,nPos2,nIndex,nCount`; this is its nCount. PowerScript
        // numeric locals default to zero, and that zero is load bearing: it is the value the swallowed
        // capture leaves behind. The three position locals in that same declaration are declared where
        // they are used instead, inside the parse in BuildFailure.
        //
        // WIDTH. The legacy declares this `long`. It is `int` here because the frame count is an
        // array measurement: .NET array indices are `int`, and StackTraceProvider's two centralized
        // one-based accessors - the ones this file is required to reuse rather than duplicate - are
        // `int` based. No reachable value approaches either limit. The PUBLIC surface keeps `long`
        // where the legacy has it, which is the `rtcode` parameter and AssertionFailure.Line.
        int frameCount = 0;

        // assert.srf:L29-L32 - `try nCount = StackTrace(ref sCallStack) catch(throwable ex1) end try`
        try
        {
            frameCount = StackTraceProvider.StackTrace(out callStack);
        }
        catch (Exception)
        {
            // PRESERVED DEFECT D3 (C-B). The legacy handler body is EMPTY [assert.srf:L32] and never
            // reads its caught variable [assert.srf:L31], so a capture failure is silent: the count
            // stays at zero, the shape gate below takes its shallow branch, and the caller receives a
            // two-field payload with no window, object, event, line number or trace. That is the
            // ported behaviour and it must not be "improved" by logging, rethrowing, recording a
            // diagnostic or substituting a synthetic frame.
            //
            // THIS HANDLER IS UNREACHABLE IN PRACTICE AND THEREFORE SHOWS AS UNCOVERED, which is
            // recorded so nobody chases the line. StackTraceProvider.StackTrace throws nothing: it
            // returns an empty array and a count of zero when it cannot capture, and its per-frame
            // degradation is handled internally. The handler is kept because the legacy has one, and
            // because the SHALLOW SHAPE it selects is reachable by other means - the internal seam
            // takes the count as an argument precisely so that shape is testable without provoking a
            // capture failure. Do not delete the handler to buy coverage, and do not add a throwing
            // test double: either would be a behaviour change to satisfy a metric.
            //
            // The exception is caught WITHOUT A VARIABLE on purpose. The legacy names one and never
            // uses it; naming one here would raise CS0168, and TreatWarningsAsErrors is inherited
            // from the repository root, so an unused caught variable is a BUILD FAILURE rather than a
            // stylistic wrinkle. Dropping the name is the only faithful spelling available.
        }

        AssertionFailure failure = BuildFailure(callStack, frameCount, info);

        // PRESERVED DEFECT D5 (C-B). The legacy wraps the throw in a try/catch that is COMMENTED OUT
        // [assert.srf:L82-L86]. It is carried across verbatim, as comments, and is INERT. Do not
        // convert it into a real try/catch, and do not delete it: a catch-and-rethrow here would
        // reset nothing observable in PowerScript but would, in .NET, overwrite the exception's own
        // captured stack trace, so reviving it is a behavioural change as well as a scope breach.
        //
        //try                                     assert.srf:L82  - dormant
        throw failure;                         // assert.srf:L83  - LIVE
        //catch(throwable ex2)                    assert.srf:L84  - dormant
        //	throw ex2                             assert.srf:L85  - dormant
        //end try                                 assert.srf:L86  - dormant
    }

    // ----------------------------------------------------------------------------------------------
    // THE SIX GUARDS
    // ----------------------------------------------------------------------------------------------
    // Each is two statements: an early return on the satisfied condition, then a delegation to
    // AssertFailed. That is the whole of every legacy body [assert.srf:L89-L123] and nothing is added
    // to any of them.
    //
    // PARAMETERS ARE BY VALUE, AND THE LEGACY `readonly` IS NOT MAPPED TO `in` (C-K). Every legacy
    // prototype marks its parameters `readonly` [assert.srf:L7-L13], and AAP 0.4.5.2's type table
    // maps that modifier to `in`. It is deliberately not applied here, for the same reason the
    // sibling StackTraceProvider and the sibling Kernel Bits.cs record for their own signatures: on a
    // bool, a nullable 64-bit integer or an object reference, `readonly` carries no observable
    // contract, because the callee cannot reach the caller's copy either way. Dropping it changes
    // nothing a test could detect, whereas applying it WOULD change something - `in` participates in
    // overload resolution and in the by-value versus by-reference distinction the resolution rules
    // apply to a three-way overload set like this one. By value is chosen because each operand is no
    // larger than the reference that would point at it; that is the reason for the choice and not a
    // performance claim, which AAP 0.8.5 forbids asserting.
    //
    // A BARE `null` LITERAL BINDS TO THE `long?` OVERLOAD. VERIFIED BY EXECUTION, NOT ASSUMED.
    // Given overloads over bool, long? and object?, `Assertions.Assert(null)` is NOT ambiguous and
    // does compile: `null` has no conversion to non-nullable bool, and between long? and object? the
    // better-conversion-target rule prefers long?, because long? converts to object? while the
    // reverse does not. Measured on this toolchain with a distinguishing probe, both
    // `Assert(null)` and `Assert(null, "...")` select the long? overload. The OBSERVABLE OUTCOME is
    // the same either way - IsSucceeded(null) and IsValidObject(null) both answer false, so both
    // overloads fire - but the binding is silent, so callers who mean the object overload should say
    // so: `Assert((object?)null)`. Each overload's own documentation repeats that guidance.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Fails unless <paramref name="condition"/> is <see langword="true"/>. [assert.srf:L8,L89-L93]
    /// </summary>
    /// <param name="condition">The condition that must hold.</param>
    /// <exception cref="AssertionFailure">
    /// <paramref name="condition"/> is <see langword="false"/>. The payload carries the fixed text
    /// <c>"Assertion failed"</c> with no continuation, because this overload passes the empty info
    /// string [assert.srf:L91].
    /// </exception>
    /// <remarks>
    /// The plainest of the six, and the one whose sibling - the <c>(bool, string)</c> overload below -
    /// carries every live production call site in the in-scope estate. No conversion, no predicate
    /// and no null handling is involved: <see cref="bool"/> is not nullable here, so the legacy's
    /// PowerScript null-boolean case is not reachable through this signature.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void Assert(bool condition)
    {
        // assert.srf:L89 - `if condition then return`
        if (condition)
        {
            return;
        }

        // assert.srf:L91 - `AssertFailed("")`
        AssertFailed(string.Empty);
    }

    /// <summary>
    /// Fails unless <paramref name="condition"/> is <see langword="true"/>, reporting
    /// <paramref name="info"/>. [assert.srf:L9,L95-L99]
    /// </summary>
    /// <param name="condition">The condition that must hold.</param>
    /// <param name="info">
    /// Failure text appended to payload field 2 after a bare line feed when it is not the empty
    /// string [assert.srf:L25-L27].
    /// </param>
    /// <exception cref="AssertionFailure">
    /// <paramref name="condition"/> is <see langword="false"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>THE ONLY OVERLOAD WITH LIVE PRODUCTION CALLERS IN THE IN-SCOPE ESTATE</b>, all seven of
    /// them in the Persistence threading proxies - for example
    /// <c>Assert(Len(sql) &gt; 0, "Len(sql) &lt;= 0")</c>
    /// [n_cst_threading_task_sqlquery.sru:L276], and likewise at <c>:L304</c> and <c>:L313</c>, at
    /// <c>n_cst_threading_task_sqlcommand.sru:L37</c>, at
    /// <c>n_cst_threading_task_sqlupdate.sru:L101,L114</c> and at
    /// <c>n_cst_threading_task.sru:L185</c>. The oracle exercises this shape too, with the exact
    /// string <c>"Invalid Number!"</c> [w_test_assert.srw:L42].
    /// </para>
    /// <para>
    /// Three of those call sites sit inside the legacy's own <c>#IF DEFINED DEBUG</c> blocks, which is
    /// why this member is NOT marked <see cref="ConditionalAttribute"/>: the decision to compile an
    /// assertion away belongs to the caller in the legacy and still does here. See the deliberate
    /// omissions in this file's header.
    /// </para>
    /// <para>
    /// <paramref name="info"/> is declared non-nullable and is used verbatim - not trimmed, not
    /// normalised, not rejected when empty. One boundary note under C-K: the legacy test is
    /// <c>info &lt;&gt; ""</c> [assert.srf:L25], and PowerScript comparison against a null string
    /// yields null rather than true, so a null <c>info</c> would suppress the continuation there
    /// exactly as the empty string does. The managed signature declares the parameter non-nullable,
    /// so null is not a legal argument; one arriving anyway from nullable-oblivious code would append
    /// a bare separator. No null check is added, because the port must reproduce the empty-string test
    /// and not a null-or-whitespace test.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void Assert(bool condition, string info)
    {
        // assert.srf:L95 - `if condition then return`
        if (condition)
        {
            return;
        }

        // assert.srf:L97 - `AssertFailed(info)`
        AssertFailed(info);
    }

    /// <summary>
    /// Fails unless <paramref name="rtcode"/> is a SUCCESS code by the return-code algebra's own
    /// definition, which is zero or greater. [assert.srf:L10,L101-L105]
    /// </summary>
    /// <param name="rtcode">A return code, or <see langword="null"/>.</param>
    /// <exception cref="AssertionFailure">
    /// <see cref="Predicates.IsSucceeded(long?)"/> answers <see langword="false"/> for
    /// <paramref name="rtcode"/>, which is every negative value and <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED DEFECT D1 - THE TRI-STATE HOLE IS INHERITED THROUGH THIS GUARD (C-B).</b> The
    /// guard is <see cref="Predicates.IsSucceeded(long?)"/>, whose test is
    /// <c>&gt;= RetCode.OK</c> [issucceeded.srf:L12], so EVERY non-negative code counts as a success
    /// and passes. The three consequences, each of which looks wrong and each of which is
    /// deliberately preserved:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///     <c>Assert(RetCode.PREVENT)</c> <b>NEVER FIRES.</b> <see cref="RetCode.PREVENT"/> is
    ///     <c>1</c> [retcode.sru:L42], which is <c>&gt;= 0</c>, so a prevention reads as a success
    ///     here even though a prevention is not a success in any ordinary reading. The same applies
    ///     to every other positive code in the catalogue.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     <c>Assert(RetCode.CANCELLED)</c> <b>DOES FIRE.</b> <see cref="RetCode.CANCELLED"/> is
    ///     <c>-2</c> [retcode.sru:L45], which fails the <c>&gt;= 0</c> test - even though the
    ///     algebra's own <c>IsFailed</c> explicitly excludes cancelled from failure, so cancelled is
    ///     NEITHER succeeded nor failed. This guard resolves that hole towards firing.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     <c>Assert((long?)null)</c> <b>DOES FIRE</b>, because the predicate returns false on null
    ///     [issucceeded.srf:L11]. The parameter is <c>long?</c> and not <c>long</c> precisely so that
    ///     this case survives: AAP 0.4.5.4 is explicit that collapsing null to zero would convert
    ///     "neither succeeded nor failed" into "succeeded" and stop the guard firing.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// This is INHERITED legacy behaviour preserved on purpose, not an oversight, and it must not be
    /// narrowed to <c>== RetCode.OK</c> nor widened to exclude cancelled. Characterization tests
    /// assert the observed outcomes rather than the intuitive ones.
    /// </para>
    /// <para>
    /// Neither this overload nor its info-carrying sibling has a live caller in the in-scope estate;
    /// both exist for API parity with the legacy prototype list. So the two overloads that look least
    /// important are exactly the ones carrying the most subtle preserved defect.
    /// </para>
    /// <para>
    /// A bare <c>null</c> literal binds HERE rather than to the object overload; pass
    /// <c>(long?)null</c> to say so explicitly, or <c>(object?)null</c> to reach the other one. See
    /// the overload note above this group.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void Assert(long? rtcode)
    {
        // assert.srf:L101 - `if IsSucceeded(rtcode) then return`
        if (Predicates.IsSucceeded(rtcode))
        {
            return;
        }

        // assert.srf:L103 - `AssertFailed("")`
        AssertFailed(string.Empty);
    }

    /// <summary>
    /// Fails unless <paramref name="rtcode"/> is a success code, reporting <paramref name="info"/>.
    /// [assert.srf:L11,L107-L111]
    /// </summary>
    /// <param name="rtcode">A return code, or <see langword="null"/>.</param>
    /// <param name="info">
    /// Failure text appended to payload field 2 after a bare line feed when it is not the empty
    /// string [assert.srf:L25-L27].
    /// </param>
    /// <exception cref="AssertionFailure">
    /// <see cref="Predicates.IsSucceeded(long?)"/> answers <see langword="false"/> for
    /// <paramref name="rtcode"/>.
    /// </exception>
    /// <remarks>
    /// Identical guard to <see cref="Assert(long?)"/>, so <b>PRESERVED DEFECT D1 applies here in
    /// full</b>: <see cref="RetCode.PREVENT"/> does not fire, <see cref="RetCode.CANCELLED"/> does,
    /// and <see langword="null"/> does. Read that overload's remarks for the mechanism and the
    /// locators; they are not repeated here so the two cannot drift apart.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void Assert(long? rtcode, string info)
    {
        // assert.srf:L107 - `if IsSucceeded(rtcode) then return`
        if (Predicates.IsSucceeded(rtcode))
        {
            return;
        }

        // assert.srf:L109 - `AssertFailed(info)`
        AssertFailed(info);
    }

    /// <summary>
    /// Fails unless <paramref name="obj"/> is a usable object reference. [assert.srf:L12,L113-L117]
    /// </summary>
    /// <param name="obj">Any object reference, or <see langword="null"/>.</param>
    /// <exception cref="AssertionFailure">
    /// <see cref="Predicates.IsValidObject(object?)"/> answers <see langword="false"/> for
    /// <paramref name="obj"/>, which in .NET means exactly that it is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>SUBSTITUTION, DOCUMENTED AT ITS POINT OF REPRODUCTION (C-K): THE <c>IsValid</c>
    /// COLLAPSE.</b> The legacy guard is <c>IsValidObject</c>, which returns the PowerBuilder
    /// intrinsic <c>IsValid</c> after a null test [isvalidobject.srf:L15-L16], and <c>IsValid</c>
    /// detects one condition the managed world has no analogue for: an object that was created and
    /// has since been <c>Destroy</c>ed, leaving a reference that is non-null yet unusable. A managed
    /// reference cannot dangle - it is either null or points at an object the garbage collector
    /// guarantees is alive - so the .NET predicate reduces to a null test. That reduction is a
    /// SUBSTITUTION rather than a translation, which is why it is recorded here rather than left
    /// implicit. The residual gap is narrow and unreachable from a faithful port of the legacy call
    /// sites: a disposed-but-non-null argument answers true, and disposal is not the concept
    /// <c>IsValid</c> was testing in any case.
    /// </para>
    /// <para>
    /// The parameter is <c>object?</c> and not <c>object</c> because <see langword="null"/> is
    /// precisely the firing condition; declaring it non-nullable would make the only interesting
    /// argument a compile-time warning at every call site. The legacy declares
    /// <c>readonly powerobject</c>, and AAP 0.4.5.2 maps <c>powerobject</c> to <c>object</c>.
    /// </para>
    /// <para>
    /// A bare <c>null</c> literal does NOT bind here - it binds to <see cref="Assert(long?)"/>. Write
    /// <c>Assert((object?)null)</c> to reach this overload. Both fire, so the outcome is the same,
    /// but the intent is only visible with the cast. See the overload note above this group.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void Assert(object? obj)
    {
        // assert.srf:L113 - `if IsValidObject(object) then return`
        if (Predicates.IsValidObject(obj))
        {
            return;
        }

        // assert.srf:L115 - `AssertFailed("")`
        AssertFailed(string.Empty);
    }

    /// <summary>
    /// Fails unless <paramref name="obj"/> is a usable object reference, reporting
    /// <paramref name="info"/>. [assert.srf:L13,L119-L123]
    /// </summary>
    /// <param name="obj">Any object reference, or <see langword="null"/>.</param>
    /// <param name="info">
    /// Failure text appended to payload field 2 after a bare line feed when it is not the empty
    /// string [assert.srf:L25-L27].
    /// </param>
    /// <exception cref="AssertionFailure">
    /// <see cref="Predicates.IsValidObject(object?)"/> answers <see langword="false"/> for
    /// <paramref name="obj"/>.
    /// </exception>
    /// <remarks>
    /// Identical guard to <see cref="Assert(object?)"/>, so the <c>IsValid</c> collapse documented
    /// there applies here in full. It is not repeated so the two cannot drift apart.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void Assert(object? obj, string info)
    {
        // assert.srf:L119 - `if IsValidObject(object) then return`
        if (Predicates.IsValidObject(obj))
        {
            return;
        }

        // assert.srf:L121 - `AssertFailed(info)`
        AssertFailed(info);
    }

    // ----------------------------------------------------------------------------------------------
    // THE PAYLOAD BUILDER - AN INTERNAL TEST SEAM (C-H)
    // ----------------------------------------------------------------------------------------------
    // WHY IT IS A SEPARATE MEMBER. Everything from assert.srf:L19 to :L80 formats a payload derived
    // from the captured call stack, and the frames it reads depend on whichever method called the
    // assertion. Driven only through the six public guards, the deep shape's three parse branches and
    // the shallow shape are not reachable deterministically, so the 80 percent line coverage gate
    // (C-H), measured from the Cobertura report, could not be met with real assertions. Taking the
    // frames as an argument makes every branch drivable from hand-written frame strings with no real
    // stack at all.
    //
    // WHY IT IS INTERNAL AND MUST STAY INTERNAL. The published contract is the seven members the
    // legacy declares [assert.srf:L7-L13]. A public seam would widen it by an eighth. The project
    // file grants InternalsVisibleTo for exactly the sibling test project and for exactly this
    // reason, and the grant confers no behavioural latitude: nothing about the observable assertion
    // surface changes because this member is visible to tests.
    //
    // WHY THE COUNT IS A PARAMETER RATHER THAN DERIVED FROM THE ARRAY. In the legacy the count is the
    // capture primitive's RETURN VALUE, held in its own local, and the swallowed capture failure
    // leaves that local at zero INDEPENDENTLY of the array [assert.srf:L30-L32]. Deriving it from the
    // array here would fuse two values the legacy keeps separate, and would make the shallow shape
    // unreachable for a test that wants to supply frames.
    //
    // WHY THE CAPTURE IS NOT DONE HERE. Structural, and explained on AssertFailed: the provider
    // excludes only its own frame, so capturing here would insert this member's frame and shift which
    // frame `nCount - 2` blames.
    //
    // IT NEITHER THROWS NOR LOGS. It raises no exception of its own - only AssertFailed throws - and
    // it performs no I/O of any kind (C-A). It also validates nothing: for the deliberately
    // inconsistent argument pair a test could construct, a count exceeding the array's length, the
    // array's own IndexOutOfRangeException propagates from StackTraceProvider.FrameAt. That is the
    // faithful behaviour and the provider's own documentation says so - PowerScript raises its own
    // error on an out-of-range subscript, and adding a check that returned a substitute value would
    // be the behaviour change C-B forbids. From AssertFailed the pair is always consistent, because
    // the count is the length of the array the same call produced.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the assertion failure payload without throwing it, from a SUPPLIED frame list rather
    /// than from the live call stack. [assert.srf:L19-L80]
    /// </summary>
    /// <param name="callStack">
    /// The captured frames, <b>OUTERMOST FIRST</b>, in the order and text format
    /// <see cref="StackTraceProvider.StackTrace(out string[])"/> produces. Never
    /// <see langword="null"/>; may be empty.
    /// </param>
    /// <param name="frameCount">
    /// The frame count the capture reported [assert.srf:L30]. Zero when the capture failed and was
    /// swallowed, which is what selects the shallow payload shape. Values of <c>2</c> or less select
    /// the shallow shape [assert.srf:L37].
    /// </param>
    /// <param name="info">
    /// Additional failure text, appended to field 2 after a bare line feed when it is not the empty
    /// string [assert.srf:L25-L27].
    /// </param>
    /// <returns>
    /// A fully populated <see cref="AssertionFailure"/> whose <see cref="AssertionFailure.Message"/>
    /// is the assembled payload. Never <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The steps below appear in the legacy's order and are not reordered, merged or hoisted. One
    /// consequence of the seam boundary is recorded honestly: the legacy assembles fields 1 and 2
    /// [assert.srf:L22-L27] BEFORE it captures the stack [assert.srf:L29-L32], whereas here the
    /// capture has already happened in <see cref="AssertFailed(string)"/> by the time this member
    /// runs. That inversion is unobservable - assembling two strings has no side effect and the
    /// capture does not read them - and it is forced by the frame-order requirement, not chosen.
    /// </para>
    /// <para>
    /// One micro-decision inside field 2 is also recorded rather than left to be noticed. The legacy
    /// assigns field 2 and then appends to it in place [assert.srf:L24-L27]. Here the text is
    /// assembled in a local and appended to the field list once, purely so that no zero-based list
    /// index appears anywhere in a file whose defining hazard is one-based translation (AAP 0.4.5.4).
    /// The resulting string and its position in the payload are identical.
    /// </para>
    /// <para>
    /// <b>THE FIELD LIST CARRIES NO INDEX ARITHMETIC.</b> The legacy writes <c>sMessages[1]</c>
    /// through <c>sMessages[7]</c> in strictly ascending contiguous order and never re-reads a field,
    /// so an ordered list appended to in the same order is exact. PowerScript's
    /// <c>UpperBound(sMessages)</c> [assert.srf:L74] is the list's count, and it is 2 or 7 and
    /// nothing else.
    /// </para>
    /// <para>
    /// <b>ONE NOTE ON THE REUSED ACCESSORS.</b> <see cref="StackTraceProvider.FrameAt"/> carries
    /// every frame-array access here. Its sibling <c>UpperBound</c> has no ported expression in this
    /// file, and that is correct rather than an omission: <c>assert.srf</c> never measures the frame
    /// array - it uses the capture primitive's return value - and its two <c>UpperBound</c> calls are
    /// the list-append idiom at :L62, which becomes an ordinary <c>Add</c>, and the field count at
    /// :L74, which becomes the field list's count.
    /// </para>
    /// </remarks>
    internal static AssertionFailure BuildFailure(string[] callStack, int frameCount, string info)
    {
        // assert.srf:L19 - `constant string DELIMITER = "~r~n"`
        //
        // CRLF, and CRLF only. This is the FIELD delimiter. Every separator INSIDE a field below is a
        // BARE LINE FEED, and that asymmetry is the only reason the consumer's split on CRLF yields
        // exactly two fields or exactly seven [pfw.sra:L115,L119]. Environment.NewLine would destroy
        // one role or the other depending on the host platform and must never appear here; see the
        // newline discipline in this file's header. The legacy spells this constant in upper case,
        // which is not available: the repository root .editorconfig scopes its naming-analyzer
        // suppressions to ten named files and none of them is in this project, so under the inherited
        // TreatWarningsAsErrors a SCREAMING_SNAKE member here would be a build failure with no way to
        // grant an exception. PascalCase preserves the word and the value.
        const string Delimiter = "\r\n";

        // assert.srf:L17 declares `string sCallStack[],sCalling,sMessages[],sMessageStr`; this is its
        // sMessages. The payload fields, in payload order. Its sCallStack is this member's parameter,
        // and its sCalling and sMessageStr are declared where they are used, below.
        List<string> messages = [];

        // assert.srf:L22 - `sMessages[1] = "-10000"`
        //
        // A STRING LITERAL IN THE PAYLOAD, not a numeric constant whose spelling is being preserved.
        // The consumer parses it back with `Error.Number = Long(sMessages[1])` [pfw.sra:L117], so its
        // textual form is the contract. No named numeric constant is introduced for it, deliberately:
        // one would invite a caller to compare against a number that never crosses the boundary as a
        // number.
        messages.Add("-10000");

        // assert.srf:L24 - `sMessages[2] = "Assertion failed"`
        string text = "Assertion failed";

        // assert.srf:L25-L27 - `if info <> "" then sMessages[2] += "~n" + info end if`
        //
        // The test is against the EMPTY STRING, exactly as the legacy writes it - not a null test, not
        // a whitespace test, and not a length-or-null convenience. The separator is a BARE LINE FEED
        // because it sits INSIDE field 2; a CRLF here would split field 2 in two at the consumer and
        // defeat its exactly-seven test.
        if (info != "")
        {
            text += "\n" + info;
        }

        messages.Add(text);

        // assert.srf:L34 - `ex = Create AssertionFailed`
        //
        // Constructed EMPTY and BEFORE the shape decision, which is load bearing: the parse below
        // writes straight into its members, and the divergence D4 depends on Info being copied from
        // field 2 here and extended only later.
        AssertionFailure failure = new();

        // assert.srf:L35 - `ex.#Info = sMessages[2]`
        //
        // A COPY of field 2 as it stands right now. Field 2 itself is never re-read after this point.
        failure.Info = text;

        // assert.srf:L37 - `if nCount > 2 then`
        //
        // PRESERVED DEFECT D2 (C-B). This single comparison is the ONLY gate between the two payload
        // shapes. A count of 2 or less - which includes the zero a swallowed capture failure leaves
        // behind (D3) - produces a two-field payload with no window, no object, no event, no line
        // number and no trace, silently. The consumer is written to expect exactly that: it requires
        // at least two fields [pfw.sra:L116] and then tests for EXACTLY seven, never for at least
        // seven [pfw.sra:L119]. There is no shape in between and none may be introduced.
        if (frameCount > 2)
        {
            // assert.srf:L38 - `sCalling = sCallStack[nCount - 2]`
            //
            // The frame to blame. Indexed ONE-BASED through the provider's centralized accessor, so
            // the expression ports character for character. The frames are outermost first, so this
            // skips the two innermost - AssertFailed and the Assert overload that called it - and
            // lands on the user's frame. See the frame order contract in this file's header.
            string calling = StackTraceProvider.FrameAt(callStack, frameCount - 2);

            // assert.srf:L39 - `nPos = LastPos(sCalling,".")`
            int lastDot = LastPos(calling, ".");

            // assert.srf:L40 - `if nPos > 0 then`
            //
            // WindowMenu and Object are assigned ONLY inside this guard. The ObjectEvent, Line and
            // StackTraceInfo steps that follow sit OUTSIDE it, which is why a frame with no dot at all
            // still yields a seven-field payload whose fields 3 and 4 are empty. That structure is
            // reproduced exactly; do not wrap the whole parse in one guard.
            if (lastDot > 0)
            {
                // assert.srf:L41 - `nPos2 = Pos(sCalling,".")`
                int firstDot = Pos(calling, ".");

                // assert.srf:L42 - `if nPos2 < nPos then`  (two or more dots)
                if (firstDot < lastDot)
                {
                    // assert.srf:L44 - `ex.#WindowMenu = Left(sCalling,nPos2 - 1)`
                    failure.WindowMenu = Left(calling, firstDot - 1);

                    // assert.srf:L46 - `ex.#Object = Mid(sCalling,nPos2 + 1,nPos - nPos2 - 1)`
                    failure.Object = Mid(calling, firstDot + 1, lastDot - firstDot - 1);
                }
                else
                {
                    // EXACTLY ONE DOT. assert.srf:L49 - `ex.#WindowMenu = Left(sCalling,nPos - 1)`
                    failure.WindowMenu = Left(calling, lastDot - 1);

                    // assert.srf:L51 - `ex.#Object = ex.#WindowMenu`
                    //
                    // THE SAME VALUE, DELIBERATELY, AND NOT A COPY-PASTE SLIP. This equality is what
                    // the consumer detects: `if Error.WindowMenu <> Error.Object` [pfw.sra:L131] is
                    // how it decides to SUPPRESS the window line from its rendered report. Making
                    // these two differ - by parsing a substring, by emptying one, or by "improving"
                    // the single-dot case - would make the consumer print a window line that the
                    // legacy suppresses.
                    failure.Object = failure.WindowMenu;
                }
            }

            // assert.srf:L55 - `nPos2 = Pos(sCalling," ",nPos + 1)`
            //
            // OUTSIDE the dot guard above. When there is no dot, lastDot is 0 and this searches from
            // position 1, which is the behaviour being preserved.
            int space = Pos(calling, " ", lastDot + 1);

            // assert.srf:L56 - `ex.#ObjectEvent = Mid(sCalling,nPos + 1,nPos2 - nPos - 1)`
            failure.ObjectEvent = Mid(calling, lastDot + 1, space - lastDot - 1);

            // assert.srf:L58 - `nPos = Pos(sCalling,":",nPos2 + 1)`
            //
            // The legacy REUSES its nPos local here, discarding the last-dot position it held. Named
            // separately in the port because nothing after this point needs the dot position, and a
            // distinct name is what makes that fact checkable rather than a matter of trust.
            int colon = Pos(calling, ":", space + 1);

            // assert.srf:L59 - `ex.#Line = Long(Mid(sCalling,nPos + 1))`
            failure.Line = Long(Mid(calling, colon + 1));

            // assert.srf:L61-L65 - the trace loop, over ONE-BASED indices 1 through nCount - 2.
            //
            // Everything from the outermost frame down to and including the user's frame, excluding
            // the two innermost framework frames. Both the frame collection and the joined text are
            // built in this one pass, exactly as the legacy does.
            for (int index = 1; index <= frameCount - 2; index++)
            {
                string frame = StackTraceProvider.FrameAt(callStack, index);

                // assert.srf:L62 - `ex.#StackTrace[UpperBound(ex.#StackTrace) + 1] = sCallStack[nIndex]`
                //
                // The PowerScript append idiom becomes an ordinary Add, so it needs no index
                // arithmetic at all - which is why the provider deliberately publishes no accessor
                // for it.
                failure.StackTrace.Add(frame);

                // assert.srf:L63 - `if nIndex > 1 then ex.#StackTraceInfo += "~n"`
                //
                // BARE LINE FEED, inserted BETWEEN entries only - never leading, never trailing. This
                // separator lives inside field 7, so a CRLF here would split that field at the
                // consumer and collapse the seven-field shape.
                if (index > 1)
                {
                    failure.StackTraceInfo += "\n";
                }

                // assert.srf:L64 - `ex.#StackTraceInfo += sCallStack[nIndex]`
                failure.StackTraceInfo += frame;
            }

            // assert.srf:L66-L70 - fields 3 through 7, read back from the members just assigned.
            //
            // Read from the failure record rather than from parse locals, exactly as the legacy does,
            // so the payload and the members can never disagree - and so the single-dot case's
            // WindowMenu-equals-Object really does reach fields 3 and 4 as the same string.
            messages.Add(failure.WindowMenu);
            messages.Add(failure.Object);
            messages.Add(failure.ObjectEvent);

            // assert.srf:L69 - `sMessages[6] = String(ex.#Line)`
            //
            // INVARIANT CULTURE, so no hosting locale can inject a group separator or a non-ASCII
            // negative sign into a field the consumer parses numerically with
            // `Error.Line = Long(sMessages[6])` [pfw.sra:L123].
            messages.Add(failure.Line.ToString(CultureInfo.InvariantCulture));

            messages.Add(failure.StackTraceInfo);

            // assert.srf:L71 - `ex.#Info += "~nat " + ex.#Object + "::" + ex.#ObjectEvent + "(" +
            //                   String(ex.#Line) + ")"`
            //
            // PRESERVED DEFECT D4 (C-B). THIS SUFFIX GOES TO Info AND TO Info ALONE. Field 2 was
            // copied at :L35 and is never re-read, so in the deep shape Info is strictly longer than
            // field 2 and the two are NOT interchangeable. The oracle displays Info, with the suffix
            // [w_test_assert.srw:L100]; the consumer assigns its error text from field 2, without it
            // [pfw.sra:L118].
            //
            // BUILDING FIELD 2 FROM Info IS THE SINGLE EASIEST WAY TO GET THIS FILE WRONG. It would
            // compile, it would look tidier, every field count would still be seven, and the consumer
            // would silently start reporting the location twice. The suffix is also appended AFTER
            // fields 3 to 7 have been added above, which is the ordering that makes the divergence
            // happen at all - do not hoist it above them.
            failure.Info += "\nat " + failure.Object + "::" + failure.ObjectEvent + "(" +
                            failure.Line.ToString(CultureInfo.InvariantCulture) + ")";
        }

        // assert.srf:L74-L78 - `nCount = UpperBound(sMessages)` then the join loop.
        //
        // The delimiter is inserted BETWEEN elements only, never leading and never trailing, which is
        // precisely what the legacy's `if nIndex > 1 then sMessageStr += DELIMITER` produces. Joining
        // rather than looping keeps the last index expression out of this file entirely, which the
        // one-based discipline prefers; the field count the legacy takes from UpperBound is the list's
        // own count, and it is 2 or 7.
        string payload = string.Join(Delimiter, messages);

        // assert.srf:L80 - `ex.SetMessage(sMessageStr)`
        //
        // Attached LAST, after every member has been assigned, and stored verbatim. AssertionFailure
        // deliberately does not decorate it; its own file records that inheriting PfwException's
        // prefixing setmessage would corrupt field 1 and break the consumer's numeric parse.
        failure.SetMessage(payload);

        // The throw is the caller's, not this member's. See AssertFailed, where assert.srf:L82-L86
        // lives - including the dormant try/catch preserved there as inert comments (D5).
        return failure;
    }

    // ----------------------------------------------------------------------------------------------
    // ONE-BASED POWERSCRIPT STRING PRIMITIVES (AAP 0.4.5.4, 0.8.6 R9)
    // ----------------------------------------------------------------------------------------------
    // Pos, LastPos, Left, Mid and Long are all ONE BASED in PowerScript. The frame parse above is
    // written in terms of these equivalents so that every expression at assert.srf:L39-L59 ports
    // CHARACTER FOR CHARACTER, which is the whole point: hand converting
    // `Mid(sCalling, nPos2 + 1, nPos - nPos2 - 1)` into zero-based substring arithmetic is exactly how
    // a silent off-by-one enters, and AAP 0.4.5.4 names that the single most dangerous mechanical
    // hazard in this refactor.
    //
    // They are PRIVATE. The two accessors this project shares - StackTraceProvider.UpperBound and
    // StackTraceProvider.FrameAt - are the ones that index the FRAME ARRAY, and they are reused rather
    // than duplicated. These five index STRINGS, which the provider does not do and therefore does not
    // publish an accessor for. They are covered by tests through BuildFailure, which reaches every
    // reachable branch of all five with hand-written frame strings.
    //
    // DEGENERATE INPUT BEHAVIOUR IS A DOCUMENTED DECISION, NOT VERIFIED PARITY (C-K). The parse does
    // reach these boundaries - a frame with no dot, no space or no colon drives all of them - and the
    // repository documents PowerBuilder's exact behaviour at none of them, because the runtime is
    // closed and there is no specification in the tree to consult. The choices below are therefore
    // stated as choices:
    //
    //      * a search returns 0 when the target is absent, when the start position is past the end of
    //        the string, when the start position is below 1, or when the target is empty;
    //      * Left with a count of zero or less yields the empty string, and with a count at or beyond
    //        the length yields the whole string;
    //      * Mid with a start past the end of the string, a start below 1, or a length of zero or
    //        less yields the empty string, and a length beyond the end is clamped to the end;
    //      * Long over text that is not a number yields 0 and NEVER THROWS, which matters because it
    //        is reached whenever a frame carries no line number at all.
    //
    // All five are ordinal, culture-insensitive and allocation-light, and NONE of them throws for any
    // input. A throw here would surface as an exception escaping an assertion, which would replace a
    // reported failure with an unrelated one.
    //
    // FOUR GUARDS BELOW ARE DEFENSIVE ONLY AND CANNOT BE REACHED THROUGH THE PARSE. They therefore
    // show as uncovered, and the reason is recorded here so that nobody chases the lines, widens the
    // visibility of a helper to reach them, or deletes a guard to buy a coverage point:
    //
    //      * the empty-target guards in Pos and LastPos - every call site passes a one-character
    //        literal, a dot, a space or a colon;
    //      * Left's count-at-or-beyond-length branch - both call sites pass a position minus one, and
    //        a one-based position never exceeds the length, so the count is always below it;
    //      * the three-argument Mid's start guard - it is SHADOWED by the length guard that precedes
    //        it. A start past the end requires the last dot to be at or after the penultimate
    //        position, which forces the computed length negative, so the length guard returns first.
    //
    // Everything else here, including every zero and empty-string answer, is a LIVE branch driven by a
    // frame that lacks a dot, a space or a colon, and each is asserted.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the ONE-BASED position of the first occurrence of <paramref name="target"/> in
    /// <paramref name="source"/>, or <c>0</c> when it is absent. Reproduces PowerScript
    /// <c>Pos(string, target)</c>. [assert.srf:L41]
    /// </summary>
    /// <param name="source">The string to search.</param>
    /// <param name="target">The string to search for.</param>
    /// <returns>A one-based position, or <c>0</c> when there is no match.</returns>
    /// <remarks>
    /// Delegates to the three-argument form with a start of <c>1</c>, which is PowerScript's own
    /// default, so the two cannot diverge. Ordinal comparison: these are structural searches for a
    /// dot, a space and a colon in machine-generated frame text, never linguistic ones.
    /// </remarks>
    private static int Pos(string source, string target)
    {
        return Pos(source, target, 1);
    }

    /// <summary>
    /// Returns the ONE-BASED position of the first occurrence of <paramref name="target"/> in
    /// <paramref name="source"/> at or after <paramref name="start"/>, or <c>0</c> when there is
    /// none. Reproduces PowerScript <c>Pos(string, target, start)</c>. [assert.srf:L55,L58]
    /// </summary>
    /// <param name="source">The string to search.</param>
    /// <param name="target">The string to search for.</param>
    /// <param name="start">A one-based position to begin searching from.</param>
    /// <returns>A one-based position, or <c>0</c> when there is no match.</returns>
    /// <remarks>
    /// Both call sites can legitimately pass a start of <c>1</c> - <c>lastDot + 1</c> when the frame
    /// carries no dot, and <c>space + 1</c> when it carries no space - so a start of exactly <c>1</c>
    /// is a normal argument rather than an edge case. A start past the end of the string, or below
    /// <c>1</c>, answers <c>0</c>; so does an empty target, which is guarded because
    /// <see cref="string.IndexOf(string, int, StringComparison)"/> would otherwise report a match at
    /// the start position and silently fabricate a position the legacy never produces.
    /// </remarks>
    private static int Pos(string source, string target, int start)
    {
        if (target.Length == 0)
        {
            return 0;
        }

        if (start < 1 || start > source.Length)
        {
            return 0;
        }

        int found = source.IndexOf(target, start - 1, StringComparison.Ordinal);
        return found < 0 ? 0 : found + 1;
    }

    /// <summary>
    /// Returns the ONE-BASED position of the LAST occurrence of <paramref name="target"/> in
    /// <paramref name="source"/>, or <c>0</c> when it is absent. Reproduces PowerScript
    /// <c>LastPos</c>. [assert.srf:L39]
    /// </summary>
    /// <param name="source">The string to search.</param>
    /// <param name="target">The string to search for.</param>
    /// <returns>A one-based position, or <c>0</c> when there is no match.</returns>
    /// <remarks>
    /// The <c>0</c> answer is a live branch rather than a defensive one: it is what a frame carrying
    /// no dot produces, and the parse is written so that such a frame still populates fields 5, 6 and
    /// 7 [assert.srf:L55-L59]. An empty target answers <c>0</c> for the same reason as in
    /// <see cref="Pos(string, string, int)"/>.
    /// </remarks>
    private static int LastPos(string source, string target)
    {
        if (target.Length == 0)
        {
            return 0;
        }

        int found = source.LastIndexOf(target, StringComparison.Ordinal);
        return found < 0 ? 0 : found + 1;
    }

    /// <summary>
    /// Returns the leftmost <paramref name="count"/> characters of <paramref name="source"/>.
    /// Reproduces PowerScript <c>Left</c>. [assert.srf:L44,L49]
    /// </summary>
    /// <param name="source">The string to take from.</param>
    /// <param name="count">How many characters to take.</param>
    /// <returns>
    /// The empty string when <paramref name="count"/> is zero or less; the whole of
    /// <paramref name="source"/> when <paramref name="count"/> reaches or exceeds its length;
    /// otherwise the first <paramref name="count"/> characters.
    /// </returns>
    /// <remarks>
    /// A count of zero is reachable and normal: a frame whose first character is a dot gives
    /// <c>firstDot - 1 == 0</c> [assert.srf:L44], and a frame whose only dot is first gives
    /// <c>lastDot - 1 == 0</c> [assert.srf:L49]. Both must yield an empty WindowMenu rather than
    /// throw.
    /// </remarks>
    private static string Left(string source, int count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        if (count >= source.Length)
        {
            return source;
        }

        return source[..count];
    }

    /// <summary>
    /// Returns the remainder of <paramref name="source"/> from the ONE-BASED position
    /// <paramref name="start"/>. Reproduces PowerScript <c>Mid(string, start)</c>.
    /// [assert.srf:L59]
    /// </summary>
    /// <param name="source">The string to take from.</param>
    /// <param name="start">A one-based position to take from.</param>
    /// <returns>
    /// The empty string when <paramref name="start"/> is below <c>1</c> or past the end of
    /// <paramref name="source"/>; otherwise everything from that position onwards.
    /// </returns>
    /// <remarks>
    /// The past-the-end answer is reachable: a frame ending in a colon puts <c>colon</c> at the last
    /// position, so <c>colon + 1</c> is one past the end and the line number text is legitimately
    /// empty. <see cref="Long(string)"/> then answers <c>0</c> for it.
    /// </remarks>
    private static string Mid(string source, int start)
    {
        if (start < 1 || start > source.Length)
        {
            return string.Empty;
        }

        return source[(start - 1)..];
    }

    /// <summary>
    /// Returns up to <paramref name="length"/> characters of <paramref name="source"/> from the
    /// ONE-BASED position <paramref name="start"/>. Reproduces PowerScript
    /// <c>Mid(string, start, length)</c>. [assert.srf:L46,L56]
    /// </summary>
    /// <param name="source">The string to take from.</param>
    /// <param name="start">A one-based position to take from.</param>
    /// <param name="length">How many characters to take, clamped to the end of the string.</param>
    /// <returns>
    /// The empty string when <paramref name="length"/> is zero or less, or when
    /// <paramref name="start"/> is below <c>1</c> or past the end of <paramref name="source"/>;
    /// otherwise the requested span, shortened to the end of the string where necessary.
    /// </returns>
    /// <remarks>
    /// A length of zero or less is reachable and is the reason the guard comes first: a frame carrying
    /// no space after its last dot leaves <c>space</c> at <c>0</c>, so
    /// <c>space - lastDot - 1</c> is negative at [assert.srf:L56] and ObjectEvent is legitimately
    /// empty. Clamping rather than throwing on an over-long length matches the same tolerance.
    /// </remarks>
    private static string Mid(string source, int start, int length)
    {
        if (length <= 0)
        {
            return string.Empty;
        }

        if (start < 1 || start > source.Length)
        {
            return string.Empty;
        }

        int available = source.Length - (start - 1);
        return source.Substring(start - 1, Math.Min(length, available));
    }

    /// <summary>
    /// Converts <paramref name="text"/> to a 64-bit integer, answering <c>0</c> when it is not a
    /// number. Reproduces PowerScript <c>Long(string)</c>. [assert.srf:L59]
    /// </summary>
    /// <param name="text">The text to convert.</param>
    /// <returns>
    /// The parsed value, or <c>0</c> when <paramref name="text"/> is empty or not a valid integer.
    /// </returns>
    /// <remarks>
    /// <b>NEVER THROWS</b>, which is the property that matters: the <c>0</c> answer is a live branch,
    /// reached whenever a frame carries no line number - the normal case for a release build with no
    /// portable symbols, where the provider emits a trailing <c>":0"</c> - and whenever the colon
    /// search finds nothing and the whole frame text is handed here instead. Parsing is INVARIANT and
    /// accepts a leading sign and surrounding white space, matching PowerScript's tolerant conversion;
    /// invariant culture is what stops a hosting locale's own sign or separator conventions from
    /// changing the answer.
    /// </remarks>
    private static long Long(string text)
    {
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : 0L;
    }
}
