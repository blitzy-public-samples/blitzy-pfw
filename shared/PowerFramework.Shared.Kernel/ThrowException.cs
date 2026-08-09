// ==============================================================================================
//  ThrowException.cs - the PowerFramework throw helpers
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.shared.pbl.src/throwexception.srf      (33 lines, two overloads)
//                 ws_objects/pfw.shared.pbl.src/pfwthrowexception.srf   (11 lines, one overload)
//  READ WITH      ws_objects/pfw.shared.pbl.src/pfwexception.sru        (coupled by one literal)
//
//  ORACLE STATUS  Those three files are the ONLY specification for this behaviour and they are
//                 READ ONLY: the legacy PowerBuilder tree is the behavioural oracle for parity
//                 testing, never an edit target. Every behaviour reproduced below therefore
//                 carries the :L locator it was taken from.
//
//  NO NATIVE SUBSTITUTION PROBLEM. The pfw.shared library declares zero PBNI class bindings and
//  zero external function prototypes, so both sources are pure PowerScript: there is no pfw.dll
//  or pfwx.dll entry point behind either of them, and nothing in this file stands in for a
//  closed-source binary. The only substitution here is a language one - see CREATE USING below.
//
//  DP-7 EVIDENCE STATUS - THIS FILE IS THE STRONG CASE, AND ITS INFERENCES ARE A DIFFERENT KIND
//  --------------------------------------------------------------------------------------------
//  Recorded explicitly so this file is not swept into the same caveat as the PBNI substitutions,
//  which would understate the evidence that genuinely exists here. Because both sources are
//  readable PowerScript, the ported LOGIC is TIER 1 TRACEABLE: every behaviour carries the :L
//  locator it was taken from, and a reviewer can check each against the source without needing a
//  recording at all. There is no closed body anywhere behind this file.
//
//  The `...Inferred...` test names below therefore do NOT mark reconstructed native behaviour.
//  They mark decisions about how PowerScript's semantics land on the CLR - which base type an
//  un-named throw maps to, how a null or differently-cased class name resolves, what happens when
//  a name resolves to nothing, and how far assembly lookup reaches. Those questions are created by
//  the TARGET runtime's type system; the legacy has no equivalent question to answer, so no legacy
//  recording could settle them even once one exists. They are TARGET-DEFINED, not target-guessed.
//
//  The practical consequence is the opposite of the PBNI files': a characterization recording will
//  confirm or refute their logic, and will simply have nothing to say about these type-resolution
//  choices. They remain reviewable against the CLR's documented behaviour instead. Nothing in this
//  file is a Golden-Master claim either - no recording exists yet - but the gap being covered here
//  is a language-mapping gap rather than a missing-oracle gap.
//
//  ##############################################################################################
//  ##  READ THIS FIRST - THE PRESERVED DEFECT                                                  ##
//  ##############################################################################################
//  ws_objects/pfw.shared.pbl.src/throwexception.srf:L23 reads `if cls = "" then return`. The
//  two-argument form therefore RETURNS WITHOUT THROWING when the class name is the empty string,
//  and a caller written on the assumption that it always raises simply carries on executing past
//  the call site. That is reproduced EXACTLY by the guard in ThrowException(string, string)
//  below, because constraint C-B requires documented legacy defects to be REPLICATED rather than
//  corrected. It is not an oversight here and it was not an argument check that got forgotten:
//  it is the legacy behaviour. It must not be "fixed" - no ArgumentException, no fall back to the
//  one-argument form, no logging, no assertion, no fallback exception of any kind.
//
//  The asymmetry that follows from it is deliberate and is the honest way to keep the defect:
//  the one-argument form and the framework form are marked [DoesNotReturn] because they always
//  throw, while the two-argument form is NOT marked, because it can return. Attributing it would
//  tell the compiler and the analyzers something untrue and would let callers write code that
//  looks unreachable but in fact executes.
//
//  THE WHOLE OF THE LEGACY SURFACE, ENUMERATED
//  --------------------------------------------------------------------------------------------
//  Knowing exactly how small the legacy is matters, because it means every member in this file is
//  either a faithful re-expression of one of the lines below or an explicitly annotated addition.
//  There is no third category.
//
//      throwexception.srf:L2-L3      global type throwexception from function_object
//      throwexception.srf:L6         global subroutine throwexception (readonly string text)
//      throwexception.srf:L10-L19        runtimeerror ex
//                                        try
//                                            ex = Create RuntimeError
//                                            ex.SetMessage(text)
//                                            throw ex
//                                        catch(throwable e)
//                                            throw e
//                                        end try
//      throwexception.srf:L7         global subroutine throwexception (readonly string cls,
//                                                                      readonly string text)
//      throwexception.srf:L21-L32        throwable ex
//                                        if cls = "" then return          <-- THE DEFECT, :L23
//                                        try
//                                            ex = Create Using cls        <-- THE SUBSTITUTION
//                                            ex.SetMessage(text)
//                                            throw ex
//                                        catch(throwable e)
//                                            throw e
//                                        end try
//      pfwthrowexception.srf:L6      global subroutine pfwthrowexception (readonly string text)
//      pfwthrowexception.srf:L9          ThrowException("pfwexception",text)
//
//  MEASURED CONSUMER PRESSURE. A sweep of ws_objects/** finds pfwThrowException at exactly two
//  sites - the live one at ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:860, whose
//  enclosing handler reads ex.text off the raised object, and a commented-out one at
//  ws_objects/pfw.tests.pbl.src/w_test_webview.srw:288 - and ThrowException at exactly one, the
//  delegation at pfwthrowexception.srf:L9 itself. Consequently "pfwexception" is the ONLY class
//  name string that is ever passed anywhere in the legacy tree. The surface is tiny; the
//  semantics still matter, which is why they are pinned this precisely.
//
//  WHY THE CLASS IS CALLED Exceptions WHILE THE FILE IS CALLED ThrowException.cs
//  --------------------------------------------------------------------------------------------
//  C# forbids a member whose name equals its enclosing type's name, so `class ThrowException`
//  cannot expose a `ThrowException` method. The collision is real and is resolved deliberately.
//
//  The plan's object-kind mapping (section 0.4.5.2) says a *.srf global function becomes a static
//  method on a ROLE-NAMED static class, and its own examples keep the legacy function name as the
//  method name: Predicates.IsSucceeded from issucceeded.srf, Bits.BitAnd, Formatting.Sprintf.
//  This file follows that pattern exactly - the class carries the role name `Exceptions` and the
//  members keep the legacy spellings `ThrowException` and `PfwThrowException`.
//
//  The alternative (class `ThrowException` with members renamed to `Throw` and `PfwThrow`) was
//  considered and rejected: it would invent two member names that appear nowhere in the legacy
//  tree, and it would break the one-to-one greppability that lets a reader match
//  `pfwThrowException("Invalid method")` at n_cst_eventful.sru:860 against its managed
//  counterpart. The FILE name stays ThrowException.cs because the plan's structure and
//  transformation tables (sections 0.3.1 and 0.4.2.3) name that exact path.
//
//  THE `Create Using cls` SUBSTITUTION, AND ITS EXACT SCOPE
//  --------------------------------------------------------------------------------------------
//  throwexception.srf:L26 is `ex = Create Using cls`: PowerBuilder's dynamic instantiation of a
//  class from a NAME STRING. It works there because PowerBuilder has one flat global namespace
//  with no import statements, in which symbol resolution follows the ordering of the library list
//  in the target file, so every global object in every loaded library is creatable by bare name.
//  C# has no equivalent, because .NET requires a resolution SCOPE. The substitution is therefore
//  a two-stage resolver, and its scope is stated here rather than left to be discovered:
//
//    STAGE 1  An explicit registry, seeded with the one name the legacy actually uses. Fully
//             deterministic, no reflection, and the only stage any legacy call site reaches.
//    STAGE 2  A best-effort scan of the ALREADY-LOADED assemblies for a public, non-abstract,
//             non-generic Exception subclass whose simple name matches and which exposes a public
//             single-string constructor. This is the closest honest analogue of the flat global
//             namespace.
//
//  THE HONEST LIMITATION, NAMED RATHER THAN PAPERED OVER: a type in an assembly that has not been
//  loaded, or that is neither registered nor publicly visible, will NOT resolve here even though
//  the legacy would have created it. Assembly loading is never forced to close that gap - this file
//  contains no Assembly.LoadFrom, no Assembly.LoadFile, no Assembly.Load by name, no probing path,
//  and no direct file-system call of any kind. To be exact rather than merely reassuring: while
//  enumerating an already-loaded assembly's PUBLIC signatures the runtime may itself resolve an
//  assembly that one of those signatures references, and that resolution can reach the disk. That
//  is the runtime's own doing and not a load this file requests, and it is precisely why the two
//  file-related inspection faults are caught and absorbed in GetExportedTypesOrEmpty below.
//  Narrowing the contract with a defined error is the plan's stated preference over widening it
//  with a guess.
//
//  DOCUMENTED-CHOICE AUDIT
//  --------------------------------------------------------------------------------------------
//  These are CHOICES, not derivations: the legacy does not settle them, so each is recorded with
//  its reasoning and with the test that pins it, so that a later characterization run against the
//  behavioural oracle can revisit any of them without archaeology.
//
//   1. ONE-ARGUMENT EXCEPTION TYPE = System.Exception.
//      throwexception.srf:L13 creates PowerBuilder's `RuntimeError`. PfwException.cs already maps
//      that ancestor to System.Exception in its DECISION 1 (pfwexception.sru:L7 `from
//      runtimeerror` becomes `: Exception`), so `Create RuntimeError` maps to `new Exception(...)`
//      by the same correspondence. System.SystemException was rejected because it is reserved by
//      convention for faults the runtime itself raises, and System.ApplicationException was
//      rejected on the grounds PfwException.cs already recorded: the .NET design guidelines
//      deprecate it, it adds no member and no behaviour, and nothing branches on it. PfwException
//      was rejected because it is what the FRAMEWORK form raises; using it here would erase the
//      distinction between the two legacy entry points and would attach the framework message
//      prefix to a path that does not carry it in the legacy.
//      PINNED BY
//        OneArgumentForm_ThrowsPlainException_WithoutFrameworkPrefix_InferredBaseTypeMapping
//
//   2. NULL CLASS NAME FALLS THROUGH; ONLY THE EMPTY STRING RETURNS SILENTLY.
//      The guard reproduces `cls = ""` and nothing wider. In PowerScript a comparison against a
//      NULL string evaluates to NULL, an `if NULL then` branch is not taken, so the legacy also
//      falls through on a null class name and fails inside `Create Using cls`. Widening the guard
//      to string.IsNullOrEmpty was considered and rejected: extending a SILENT-RETURN defect to a
//      case the legacy does not cover would hide a caller bug that the legacy surfaces, which is
//      a behaviour change in the more dangerous of the two directions. The resolver null-checks
//      internally so that a null produces the same clear, documented fault as any other
//      unresolvable name rather than an ArgumentNullException raised from inside a dictionary.
//      PINNED BY
//        TwoArgumentForm_WithNullClassName_Throws_InferredFromPowerScriptNullSemantics
//
//   3. NAME MATCHING IS ORDINAL AND CASE INSENSITIVE.
//      PowerBuilder identifiers are case insensitive, and this repository proves it in situ:
//      pfwthrowexception.srf:L9 calls `ThrowException` with capitals while the object it resolves
//      to is declared all lowercase at throwexception.srf:L2, and pfwexception.sru:L8-L9 spell the
//      type name in lowercase while the demo call sites capitalise it. OrdinalIgnoreCase is
//      therefore the faithful reading. Culture-sensitive comparison is never used anywhere in
//      this file: under tr-TR the dotless-i casing rules would change which names resolve, and the
//      parity model's determinism requirement (section 0.6.7) forbids that.
//      PINNED BY
//        TwoArgumentForm_WithTypeNameInAnyCasing_ThrowsPfwException_InferredCaseInsensitivity
//        CaseInsensitiveResolution_IsOrdinal_UnderTurkishCulture
//
//   4. AN UNRESOLVABLE NAME THROWS InvalidOperationException.
//      The legacy's `Create Using cls` failure surfaces as a runtime error through the
//      catch/rethrow at throwexception.srf:L29-L30, so throwing is the faithful outcome; the type
//      is the choice. InvalidOperationException states what actually happened - the operation
//      cannot be completed in the resolver's current state - and is not reserved. Rejected:
//      TypeLoadException and TypeAccessException, which derive from SystemException and carry the
//      same runtime-reserved objection as item 1; ArgumentException and ArgumentNullException,
//      which would misattribute the fault to the caller's argument and would blur the line
//      against the empty-name path that must NOT throw; and PfwException, which would masquerade
//      as the very fault the caller was trying to raise. THIS IS DELIBERATELY DIFFERENT FROM THE
//      EMPTY-NAME CASE, WHICH RETURNS SILENTLY.
//      PINNED BY
//        TwoArgumentForm_WithUnresolvableClassName_ThrowsInvalidOperationException_InferredChoice
//
//   5. THE NO-OP catch/rethrow IS NOT REPRODUCED.
//      throwexception.srf:L16-L17 and :L29-L30 are `catch(throwable e) throw e` - they catch
//      anything the creation or the throw produces and rethrow it unchanged, so they add nothing
//      observable. They are omitted rather than transcribed, because the literal C# transcription
//      `catch (Exception e) { throw e; }` RESETS the stack trace, which is an observable
//      regression. Omitting the wrapper preserves observable behaviour exactly and keeps the
//      original throw site intact. There is no `throw e;` anywhere in this file.
//      PINNED BY
//        EntryPoints_PreserveThrowSiteInStackTrace
//
//   6. THE FALLBACK REQUIRES A PUBLIC SINGLE-STRING CONSTRUCTOR AND IS ORDERED DETERMINISTICALLY.
//      A same-named Exception subclass without one is treated as UNRESOLVED rather than created
//      message-less, because silently discarding the caller's text would be worse than a clear
//      failure. Where several loaded assemblies expose a matching name the winner is the ordinal
//      minimum of assembly full name then type full name, so the outcome cannot depend on
//      assembly load order. Nothing is cached, positively or negatively, so an assembly loaded
//      later still resolves and a registration always wins over a scan.
//      PINNED BY
//        LoadedAssemblyFallback_ResolvesUnregisteredExceptionTypeByName_InferredScope
//
//   7. THE CLASS AND MEMBER NAMING RESOLUTION described above.
//      PINNED BY
//        Resolution_UsesPfwExceptionTypeName_NotALiteral_TwoFileContract
//
//  FAIL FAST, AND WHERE ITS BOUNDARY LIES
//  --------------------------------------------------------------------------------------------
//  The plan is emphatic (section 0.1.4) that the legacy's fail-fast posture must survive as fail
//  fast and never be softened into graceful degradation. That principle governs the framework's
//  STRUCTURAL faults, and those belong to the Gateway composition root and to the Diagnostics
//  assert path - pfw.sra:L111-L144 decodes a seven-field assert payload and then executes
//  HALT CLOSE. It does NOT license hardening the empty-class-name path here. That path's legacy
//  behaviour is to RETURN, and returning is what is reproduced. The two must not be conflated:
//  softening a structural fault would be a behaviour change, and hardening this one would be too.
//
//  CONSTRAINT SELF-AUDIT
//  --------------------------------------------------------------------------------------------
//  No user rules exist for this project - review_rules returns exactly "No user rules provided",
//  a single line - so nothing here is written to satisfy a user rule and none is inferred. The
//  binding constraints are the plan's own non-rule inventory (section 0.7.3) and its
//  enterprise-standard baseline (section 0.7.2), held to in the rules' absence rather than
//  treating that absence as licence to lower the bar.
//
//  C-A  These are shared IMPLEMENTATION helpers consumed in process. They are not a cross-service
//       channel: no I/O, no logging, no serialization, no HTTP or gRPC status, no reference to
//       PowerFramework.Contracts, and no assembly loading from disk. A fault that must cross a
//       service boundary is expressed with the structured error types in the Contracts project.
//  C-B  Behaviour is replicated, never improved: the silent return at throwexception.srf:L23 is
//       kept verbatim, all three legacy entry points are kept even though only three call sites
//       exist between them, no validation or logging or assertion is added to any ported path,
//       and the message decoration is left entirely to PfwException.SetMessage so that this file
//       cannot double-prefix it.
//  C-C  The legacy tree is read only. All three sources were read as the specification and left
//       untouched, and every reproduced behaviour cites its :L locator.
//  C-K  Every technology-specific decision is documented at its point of reproduction: the
//       CREATE USING substitution and its exact scope, the one-argument exception type, the
//       omitted no-op catch, the null and unresolvable class-name behaviours, the ordinal
//       case-insensitive matching, the [DoesNotReturn] asymmetry, and the two-file name contract.
//  0.4.5.2  Both *.srf global function objects become static methods on one role-named static
//       class, with their legacy names preserved.
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//  --------------------------------------------------------------------------------------------
//    * Any SCREAMING_SNAKE identifier. The repository-root .editorconfig scopes its naming
//      analyzer suppressions to the files on its BAND 3 roster that genuinely carry preserved
//      constant spellings,
//      and this file is not one of them; with TreatWarningsAsErrors inherited from
//      Directory.Build.props such an identifier here would be a build ERROR. The legacy string
//      VALUES appear below as literals, which is unaffected.
//    * Any reference to RetCode or Enums. These helpers carry no return code: the legacy
//      subroutines return nothing at all, and the tri-state return code algebra travels
//      separately through Predicates and RetCode.
//    * Any reference to PowerFramework.Shared.Diagnostics. Diagnostics depends on Kernel, so the
//      reverse edge would be circular. AssertionFailure - the port of assertionfailed.sru - is a
//      legitimate future registry entry, and TryRegisterExceptionType exists precisely so it can
//      be added from that side without this file changing and without that edge being created.
//    * A message-formatting or Sprintf call. The text arrives already formatted; formatretcode.srf
//      and sprintf.srf are ported in Formatting.cs and are not reached from here.
//    * A per-file licence header. Neither source carries one - both open with a $PBExportHeader$
//      line only - so there is nothing to carry over; the assembly-level copyright is set once in
//      the repository-root Directory.Build.props and the full notice lives in LICENSE and NOTICE.
// ==============================================================================================

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace PowerFramework.Shared.Kernel;

/// <summary>
/// The PowerFramework throw helpers, ported from
/// <c>ws_objects/pfw.shared.pbl.src/throwexception.srf</c> and
/// <c>ws_objects/pfw.shared.pbl.src/pfwthrowexception.srf</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two legacy <c>function_object</c> globals collapse into this one role-named static class, as the
/// plan's object-kind mapping prescribes for a <c>*.srf</c>. The legacy member spellings are kept:
/// <see cref="ThrowException(string)"/> and <see cref="ThrowException(string, string)"/> are the
/// two overloads of <c>throwexception</c>, and <see cref="PfwThrowException(string)"/> is
/// <c>pfwthrowexception</c>.
/// </para>
/// <para>
/// <b>The two-argument overload can return without throwing.</b> On an empty class name it returns
/// silently [throwexception.srf:L23]. That is a preserved legacy defect, not an omission, and it is
/// why only the other two members carry <see cref="DoesNotReturnAttribute"/>. See the file header
/// for the full analysis.
/// </para>
/// </remarks>
public static class Exceptions
{
    /// <summary>
    /// Stage 1 of the <c>Create Using cls</c> substitution: the explicit class-name registry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seeded with the single name the legacy actually uses, taken from
    /// <see cref="PfwException.TypeName"/> rather than retyped as a literal, so that the two-file
    /// contract with PfwException.cs lives in exactly one place and the two sides cannot drift
    /// [pfwthrowexception.srf:L9, throwexception.srf:L26].
    /// </para>
    /// <para>
    /// The value is a FACTORY rather than a <see cref="Type"/>. That is what lets the resolved type
    /// apply its own message decoration through its own constructor - for
    /// <see cref="PfwException"/> the prefix and single line feed of pfwexception.sru:L23 -
    /// which is the managed equivalent of the legacy calling <c>ex.SetMessage(text)</c> on the
    /// object it just created [throwexception.srf:L27]. It also keeps stage 1 free of reflection
    /// entirely, so the path every legacy call site takes is direct, allocation-light and trivially
    /// deterministic.
    /// </para>
    /// <para>
    /// The comparer is <see cref="StringComparer.OrdinalIgnoreCase"/>: ordinal because a
    /// culture-sensitive comparer would change which names resolve under a culture such as tr-TR,
    /// and case insensitive because PowerBuilder identifiers are - as pfwthrowexception.srf:L9
    /// demonstrates by calling <c>ThrowException</c> against an object declared
    /// <c>throwexception</c>. A concurrent dictionary is used because registration may happen from
    /// any thread during host start-up while another thread is already raising a fault.
    /// </para>
    /// </remarks>
    private static readonly ConcurrentDictionary<string, Func<string, Exception>> ExceptionFactories
        = new(
            [
                new KeyValuePair<string, Func<string, Exception>>(
                    PfwException.TypeName,
                    static text => new PfwException(text)),
            ],
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Raises a plain runtime fault carrying <paramref name="text"/> as its message. This method
    /// never returns.
    /// </summary>
    /// <param name="text">
    /// The message text. Declared non-nullable because the legacy prototype at
    /// <c>throwexception.srf:L6</c> declares a plain <c>readonly string</c>; a caller who forces a
    /// null through anyway gets the base class's own null handling rather than a new failure mode.
    /// </param>
    /// <exception cref="Exception">
    /// Always, carrying <paramref name="text"/> verbatim.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The port of <c>throwexception.srf:L10-L19</c>, whose body creates PowerBuilder's
    /// <c>RuntimeError</c>, calls <c>SetMessage(text)</c> on it and throws it. The three legacy
    /// steps collapse into one construction because <see cref="Exception"/> takes its message
    /// through its constructor, which is the managed equivalent of that <c>SetMessage</c> call.
    /// </para>
    /// <para>
    /// The type is <see cref="Exception"/> itself, by the same correspondence PfwException.cs uses:
    /// <c>runtimeerror</c> maps to <see cref="Exception"/>, so <c>Create RuntimeError</c> maps to
    /// <c>new Exception(...)</c>. In particular this member does NOT raise
    /// <see cref="PfwException"/> and the message it carries therefore does NOT acquire the
    /// <see cref="PfwException.MessagePrefix"/> decoration - that prefix belongs to the framework
    /// form alone, and attaching it here would erase the distinction between the two legacy entry
    /// points. See choice 1 in the file header.
    /// </para>
    /// <para>
    /// The legacy's surrounding <c>try ... catch(throwable e) throw e</c> is not transcribed: it is
    /// a no-op wrapper, and its literal C# form would reset the stack trace. See choice 5.
    /// </para>
    /// </remarks>
    [DoesNotReturn]
    public static void ThrowException(string text) => throw new Exception(text);

    /// <summary>
    /// Raises the exception type named by <paramref name="className"/> carrying
    /// <paramref name="text"/> as its message - or, WHEN <paramref name="className"/> IS THE EMPTY
    /// STRING, RETURNS WITHOUT THROWING ANYTHING AT ALL.
    /// </summary>
    /// <param name="className">
    /// The legacy class name to raise, matched case insensitively and ordinally. The empty string
    /// makes this method a no-op; see the remarks, because that is a preserved legacy defect and
    /// not a convenience.
    /// </param>
    /// <param name="text">
    /// The message text, passed to the resolved type UNDECORATED so that whatever decoration that
    /// type applies is applied exactly once and by that type.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="className"/> is non-empty but names no exception type this resolver can
    /// reach. Deliberately distinct from the empty-string case, which returns silently.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The port of <c>throwexception.srf:L21-L32</c>. <b>THIS MEMBER MAY RETURN WITHOUT
    /// THROWING.</b> That is stated in the summary, not merely here, so the quirk is visible
    /// through IntelliSense at every call site - the cheapest available mitigation of a defect that
    /// must be kept. It is also why this member carries no
    /// <see cref="DoesNotReturnAttribute"/> while its two siblings do.
    /// </para>
    /// <para>
    /// A null <paramref name="className"/> is NOT treated as empty: it falls through to resolution
    /// and surfaces the documented <see cref="InvalidOperationException"/>, matching the legacy,
    /// whose PowerScript comparison against a null string yields null and so does not take the
    /// branch either. See choice 2 in the file header.
    /// </para>
    /// </remarks>
    public static void ThrowException(string className, string text)
    {
        // ------------------------------------------------------------------------------------
        //  PRESERVED LEGACY DEFECT - ws_objects/pfw.shared.pbl.src/throwexception.srf:L23
        //      if cls = "" then return
        //
        //  On an EMPTY class name the legacy subroutine returns without raising anything, so a
        //  caller that assumed it always raises continues executing past the call. This guard
        //  reproduces that exactly and MUST NOT BE "FIXED": no ArgumentException, no fall back to
        //  the one-argument overload, no logging, no assertion. Constraint C-B requires documented
        //  legacy defects to be replicated rather than corrected, and the fail-fast posture of
        //  section 0.1.4 governs STRUCTURAL faults elsewhere in the framework, not this path.
        //
        //  A constant pattern is used rather than a null-or-empty test on purpose: null does not
        //  match it, so a null class name falls through to resolution exactly as the legacy's
        //  three-valued comparison does. See choice 2 in the file header.
        //
        //  The test that stops a future contributor from removing this guard is
        //  TwoArgumentForm_WithEmptyClassName_SilentlyReturns_PreservedLegacyDefect.
        // ------------------------------------------------------------------------------------
        if (className is "")
        {
            return;
        }

        // The legacy sequence is Create Using cls, then SetMessage(text), then throw
        // [throwexception.srf:L26-L28]. CreateByClassName performs the first two - the resolved
        // type's own constructor is the SetMessage step, so no decoration is applied here - and the
        // throw stays at this level so the stack trace names this call site rather than a helper.
        throw CreateByClassName(className, text);
    }

    /// <summary>
    /// Raises <see cref="PfwException"/> carrying <paramref name="text"/> as its message, decorated
    /// by that type. This method never returns.
    /// </summary>
    /// <param name="text">
    /// The message text, passed through undecorated. <see cref="PfwException"/> applies
    /// <see cref="PfwException.MessagePrefix"/> and a single line feed itself
    /// [pfwexception.sru:L23], so the prefix appears exactly once.
    /// </param>
    /// <exception cref="PfwException">
    /// Always, unless a caller has displaced the seeded registry entry - which
    /// <see cref="TryRegisterExceptionType"/> structurally prevents.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The port of <c>pfwthrowexception.srf:L9</c>, whose entire body is
    /// <c>ThrowException("pfwexception",text)</c>. The delegation is kept literal, and the name
    /// string comes from <see cref="PfwException.TypeName"/> rather than being retyped, so the
    /// two-file contract cannot drift.
    /// </para>
    /// <para>
    /// This path inherits the empty-name guard VACUOUSLY: <see cref="PfwException.TypeName"/> is a
    /// compile-time constant and is never the empty string, so the guard at
    /// <c>throwexception.srf:L23</c> cannot fire here. It is worth saying explicitly, because a
    /// reader who has just met that guard may otherwise conclude it is unreachable dead code - it
    /// is not, it is simply unreachable ON THIS PATH.
    /// </para>
    /// </remarks>
    [DoesNotReturn]
    public static void PfwThrowException(string text)
    {
        // Literal delegation, exactly as pfwthrowexception.srf:L9 delegates.
        ThrowException(PfwException.TypeName, text);

        // Unreachable, and unreachable for a reason worth encoding rather than asserting: the only
        // way the call above returns is the empty-class-name guard at throwexception.srf:L23, and
        // PfwException.TypeName is a non-empty compile-time constant. The compiler cannot prove
        // that, so without this statement the [DoesNotReturn] attribute above would raise CS8763 -
        // which, under the inherited TreatWarningsAsErrors, is a build error. Throwing
        // UnreachableException states the invariant in code instead of suppressing the diagnostic.
        throw new UnreachableException(
            $"{nameof(PfwThrowException)} returned from {nameof(ThrowException)}, which is only "
            + "possible for an empty class name; "
            + $"{nameof(PfwException)}.{nameof(PfwException.TypeName)} is a non-empty "
            + "compile-time constant, so this state is unreachable.");
    }

    /// <summary>
    /// Registers the way to construct the exception type named <paramref name="className"/>,
    /// extending the <c>Create Using cls</c> substitution from outside this file.
    /// </summary>
    /// <param name="className">
    /// The legacy class name to register. Matched case insensitively and ordinally, like every
    /// other name in this resolver.
    /// </param>
    /// <param name="factory">
    /// Constructs the exception from the caller's UNDECORATED message text. Any decoration the type
    /// applies must be applied by the type, exactly as <see cref="PfwException"/> does at
    /// pfwexception.sru:L23, so that a message is never prefixed twice.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the registration was accepted; <see langword="false"/> when
    /// <paramref name="className"/> is null or empty, when <paramref name="factory"/> is null, or
    /// when that name is already registered.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The extension point the plan's registry design calls for, and the reason this file needs no
    /// edit to gain a new exception name. Its motivating case is
    /// <c>PowerFramework.Shared.Diagnostics.AssertionFailure</c>, the port of
    /// <c>assertionfailed.sru</c>: Diagnostics depends on Kernel, so Kernel must not reference
    /// Diagnostics - that edge would be circular - and this member is how the dependent side
    /// registers itself instead. It is public rather than internal because this assembly
    /// deliberately declares no <c>InternalsVisibleTo</c>, so an internal member would be
    /// unreachable from the very assemblies that need it.
    /// </para>
    /// <para>
    /// FIRST REGISTRATION WINS, and that is a safety property rather than a convenience: it means
    /// the seeded <see cref="PfwException.TypeName"/> entry can never be displaced, so no caller
    /// can quietly redirect <see cref="PfwThrowException(string)"/> at some other type and break
    /// the two-file contract with PfwException.cs. A second attempt on the same name reports
    /// <see langword="false"/> and changes nothing.
    /// </para>
    /// <para>
    /// This member NEVER THROWS - not for a null or empty name, not for a null factory, not for a
    /// duplicate. Two reasons. It is a new .NET-only member with no legacy counterpart, so there is
    /// no legacy raising behaviour to reproduce; and a registry that threw would invite an
    /// <see cref="ArgumentException"/> into a file whose entire subject is a path that must return
    /// silently, where the two could be confused. The arguments are declared non-nullable to state
    /// the intended contract, matching the convention PfwException.cs already sets, while the
    /// runtime checks below still tolerate a null forced through from a nullable-oblivious caller.
    /// </para>
    /// </remarks>
    public static bool TryRegisterExceptionType(string className, Func<string, Exception> factory)
    {
        if (className is null || className.Length == 0 || factory is null)
        {
            return false;
        }

        return ExceptionFactories.TryAdd(className, factory);
    }

    /// <summary>
    /// The managed substitute for <c>ex = Create Using cls</c> followed by
    /// <c>ex.SetMessage(text)</c> [throwexception.srf:L26-L27]: resolves
    /// <paramref name="className"/> and returns the constructed exception for the caller to throw.
    /// </summary>
    /// <param name="className">
    /// The class name to resolve. Annotated nullable because the public overload's guard
    /// deliberately lets a null through - see choice 2 in the file header - so this is where a null
    /// is genuinely handled rather than merely permitted by an annotation.
    /// </param>
    /// <param name="text">
    /// The undecorated message text, passed to the resolved type unchanged.
    /// </param>
    /// <returns>
    /// The constructed exception. The caller throws it, so the throw site stays theirs.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="className"/> resolves to nothing through either stage.
    /// </exception>
    private static Exception CreateByClassName(string? className, string text)
    {
        // NOT the preserved defect guard. The defect guard lives in the public two-argument
        // overload and tests the EMPTY STRING ONLY [throwexception.srf:L23]. The test here exists
        // only to make this private helper total: an empty name cannot reach it, and a null one can
        // only arrive from a caller who forced a null past the non-nullable public declaration.
        // Screening both keeps a null out of the dictionary lookup, which would otherwise raise an
        // ArgumentNullException from inside the collection instead of the clear fault built below.
        if (!string.IsNullOrEmpty(className))
        {
            // STAGE 1 - the explicit registry. This is the only stage any legacy call site reaches,
            // because "pfwexception" is the sole class name the legacy tree ever passes, and a
            // registration therefore always takes precedence over the scan below.
            if (ExceptionFactories.TryGetValue(className, out Func<string, Exception>? factory))
            {
                return factory(text);
            }

            // STAGE 2 - the documented best-effort scan of the already-loaded assemblies.
            Exception? scanned = CreateFromLoadedAssemblies(className, text);
            if (scanned is not null)
            {
                return scanned;
            }
        }

        // Unresolved. Throwing here is faithful - the legacy's Create Using failure surfaces as a
        // runtime error through the catch/rethrow at throwexception.srf:L29-L30 - while the TYPE is
        // the documented choice recorded as choice 4 in the file header. This is deliberately
        // different from the empty-class-name path, which returns silently and never reaches here.
        throw new InvalidOperationException(BuildUnresolvedClassNameMessage(className));
    }

    /// <summary>
    /// Stage 2 of the <c>Create Using cls</c> substitution: constructs the exception from an
    /// already-loaded assembly, or reports that no such type is reachable.
    /// </summary>
    /// <param name="className">
    /// The non-empty class name to match against each type's simple name.
    /// </param>
    /// <param name="text">
    /// The undecorated message text, handed to the type's single-string constructor.
    /// </param>
    /// <returns>
    /// The constructed exception, or <see langword="null"/> when nothing loaded matches. A null
    /// return is the definite "unresolved" answer the caller turns into a clear fault; this method
    /// never reports failure by throwing something unrelated.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the closest honest analogue of PowerBuilder's flat global namespace, and its limits
    /// are exactly the limits stated in the file header: it inspects only assemblies that are
    /// ALREADY loaded, and it never loads one. There is no
    /// <see cref="Assembly.LoadFrom(string)"/>, no <c>Assembly.LoadFile</c>, no
    /// <c>Assembly.Load</c> and no direct file-system call anywhere in this file, so a type in an
    /// unloaded assembly does not resolve here even though the legacy would have created it. The
    /// one nuance, stated exactly: enumerating an assembly's public signatures can make the RUNTIME
    /// resolve an assembly those signatures reference, which is why the two file-related inspection
    /// faults below are caught rather than assumed impossible.
    /// </para>
    /// <para>
    /// Only PUBLIC types are considered, through <see cref="Assembly.GetExportedTypes"/>: the
    /// legacy mechanism reaches GLOBAL objects, whose managed counterpart is a publicly visible
    /// type, and restricting the scan this way also keeps it from surfacing a type no consumer
    /// could name.
    /// </para>
    /// <para>
    /// The result is ORDER INDEPENDENT. Assembly enumeration order reflects load order, which is
    /// not stable across runs, so a match is chosen as the ordinal minimum of assembly full name
    /// then type full name rather than as the first one seen. Nothing is cached in either
    /// direction, so an assembly loaded later can still resolve and a registration always beats a
    /// scan; the cost is irrelevant because this runs only while a fault is already being raised.
    /// </para>
    /// <para>
    /// A construction fault is NOT swallowed. If the matched constructor itself throws, the
    /// reflection layer's <see cref="TargetInvocationException"/> propagates, which is the closest
    /// behaviour to the legacy's <c>catch(throwable e) throw e</c> rethrow at
    /// throwexception.srf:L29-L30. Only the per-assembly INSPECTION faults below are absorbed, and
    /// each is absorbed by skipping that assembly rather than by abandoning the search.
    /// </para>
    /// </remarks>
    private static Exception? CreateFromLoadedAssemblies(string className, string text)
    {
        Type? match = null;
        string matchKey = string.Empty;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // A dynamic assembly has no exported-type view to enumerate; skipping it first is
            // cheaper and clearer than relying on the NotSupportedException handler below.
            if (assembly.IsDynamic)
            {
                continue;
            }

            foreach (Type candidate in GetExportedTypesOrEmpty(assembly))
            {
                if (!IsCreatableExceptionNamed(candidate, className))
                {
                    continue;
                }

                string candidateKey = BuildOrderingKey(assembly, candidate);
                if (match is null || string.CompareOrdinal(candidateKey, matchKey) < 0)
                {
                    match = candidate;
                    matchKey = candidateKey;
                }
            }
        }

        if (match is null)
        {
            return null;
        }

        // The single-string constructor IS the managed SetMessage step, so the text goes in
        // undecorated and whatever decoration the type applies is applied once, by the type. The
        // cast is a match filter restatement rather than a hope: IsCreatableExceptionNamed already
        // required assignability to Exception, and an unexpected result becomes a null that the
        // caller reports as unresolved instead of a blind InvalidCastException.
        return Activator.CreateInstance(match, [text]) as Exception;
    }

    /// <summary>
    /// Determines whether <paramref name="candidate"/> is a type this resolver may raise under the
    /// name <paramref name="className"/>.
    /// </summary>
    /// <param name="candidate">The type to test.</param>
    /// <param name="className">The requested class name.</param>
    /// <returns><see langword="true"/> when every condition below holds.</returns>
    /// <remarks>
    /// Four conditions, each with a reason. The simple name must match ORDINALLY and CASE
    /// INSENSITIVELY, because PowerBuilder identifiers are case insensitive and a culture-sensitive
    /// comparison would make resolution depend on the ambient culture. The type must derive from
    /// <see cref="Exception"/>, because the legacy variable at throwexception.srf:L21 is declared
    /// <c>throwable</c> and only a throwable can be thrown. It must be neither abstract nor an open
    /// generic, because neither can be instantiated. And it must expose a public single-string
    /// constructor, because a type without one could only be created message-less, and silently
    /// discarding the caller's text would be worse than the clear failure the caller gets instead.
    /// </remarks>
    private static bool IsCreatableExceptionNamed(Type candidate, string className) =>
        string.Equals(candidate.Name, className, StringComparison.OrdinalIgnoreCase)
        && !candidate.IsAbstract
        && !candidate.IsGenericTypeDefinition
        && typeof(Exception).IsAssignableFrom(candidate)
        && candidate.GetConstructor([typeof(string)]) is not null;

    /// <summary>
    /// Builds the stable ordinal sort key that makes stage 2 independent of assembly load order.
    /// </summary>
    /// <param name="assembly">The assembly the candidate was found in.</param>
    /// <param name="candidate">The candidate type.</param>
    /// <returns>The assembly's full name, a NUL separator, then the type's full name.</returns>
    /// <remarks>
    /// The separator is U+0000 because it sorts below every character that can appear in an
    /// assembly or type name, so no pair of names can produce an ambiguous ordering. Both
    /// components fall back to a defined value rather than null, so the key is always comparable.
    /// </remarks>
    private static string BuildOrderingKey(Assembly assembly, Type candidate) =>
        (assembly.FullName ?? assembly.GetName().Name ?? string.Empty)
        + "\u0000"
        + (candidate.FullName ?? candidate.Name);

    /// <summary>
    /// Enumerates an assembly's public types, absorbing the inspection faults a partially
    /// resolvable assembly can raise.
    /// </summary>
    /// <param name="assembly">The already-loaded assembly to inspect.</param>
    /// <returns>Its exported types, the loadable subset of them, or an empty sequence.</returns>
    /// <remarks>
    /// Each catch is a specific, expected inspection fault rather than a blanket suppression, and
    /// each is documented at the point it is absorbed. Absorbing them is what keeps one unrelated
    /// assembly with a missing dependency from turning every framework throw into a reflection
    /// failure - which would be a new failure mode the legacy does not have.
    /// </remarks>
    private static IEnumerable<Type> GetExportedTypesOrEmpty(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            // Some types in this assembly could not be loaded. The ones that could still are, and a
            // match may well be among them, so the loadable subset is searched rather than skipped.
            return exception.Types.OfType<Type>();
        }
        catch (NotSupportedException)
        {
            // The assembly does not support an exported-type view at all.
            return [];
        }
        catch (FileNotFoundException)
        {
            // A dependency this assembly's public signatures need is not present on disk.
            return [];
        }
        catch (FileLoadException)
        {
            // A dependency was found but could not be loaded, for instance on a version conflict.
            return [];
        }
    }

    /// <summary>
    /// Builds the diagnostic for a class name that resolved through neither stage.
    /// </summary>
    /// <param name="className">The unresolved name, rendered explicitly when it is null.</param>
    /// <returns>
    /// A message naming the name, both resolution stages, and the two ways to fix it.
    /// </returns>
    /// <remarks>
    /// The message states the legacy locator, both stages and the remedy, and it closes by naming
    /// the empty-string case explicitly, so that anyone who reaches this fault while investigating
    /// a MISSING exception cannot mistake the two paths for one another.
    /// </remarks>
    private static string BuildUnresolvedClassNameMessage(string? className) =>
        "PowerFramework cannot raise an exception for class name "
        + (className is null ? "<null>" : "\"" + className + "\"")
        + " (the substitute for 'Create Using cls' at "
        + "ws_objects/pfw.shared.pbl.src/throwexception.srf:L26). The name is not registered, "
        + "and no already-loaded assembly exports a non-abstract, non-generic System.Exception "
        + "subclass of that name with a public single-string constructor. Register it through "
        + nameof(Exceptions) + "." + nameof(TryRegisterExceptionType)
        + ", or ensure its assembly is loaded before the raise. Note that an EMPTY class name does "
        + "not reach this fault: it returns silently, which is preserved legacy behaviour "
        + "[throwexception.srf:L23].";
}
