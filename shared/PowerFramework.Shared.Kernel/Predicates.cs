// ==============================================================================================
//  Predicates - the PowerFramework tri-state return-code algebra and the object-validity test
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    six global function objects under ws_objects/pfw.shared.pbl.src/, in the order
//                 they appear below:
//
//                     issucceeded.srf    18 lines   2 overloads   long, boolean
//                     isfailed.srf       18 lines   2 overloads   long, boolean
//                     isprevented.srf    18 lines   2 overloads   long, boolean
//                     isallowed.srf      16 lines   2 overloads   long, boolean
//                     iscancelled.srf    11 lines   1 overload    long          <-- the odd one
//                     isvalidobject.srf  18 lines   2 overloads   any, powerobject
//
//                 Eleven legacy overloads become the ten members of this class; the single
//                 collapse is in IsValidObject and is explained in DECISION 6.
//
//  ORACLE STATUS  Those six .srf files are the ONLY specification for this behaviour and they are
//                 READ ONLY: they are the behavioural oracle for parity testing, never an edit
//                 target. Nothing else in the repository can adjudicate a disagreement about what
//                 a predicate returns, so EVERY member below carries the `<file>.srf:L` locator of
//                 the line it reproduces. A reader who doubts a body should read the locator, not
//                 reason from first principles - reasoning from first principles is exactly how the
//                 quirks recorded here get "fixed".
//
//  LICENCE        The pfw.shared licence block and its four-condition Chinese restatement live at
//                 retcode.sru:L13-L34 and are not copied into each ported file. The obligation is
//                 discharged by the repository-root NOTICE and LICENSE.
//
//  THE ONE THING TO UNDERSTAND BEFORE CHANGING ANYTHING IN THIS FILE
//  --------------------------------------------------------------------------------------------
//  NULL HANDLING IS DELIBERATELY NON-UNIFORM ACROSS THE SIX, AND THE ASYMMETRY IS THE CONTRACT.
//  It is not drift, not an oversight, and not something four of the six got wrong. Measured from
//  the oracle, line by line:
//
//      issucceeded.srf   L11, L15   explicit guard    `if IsNull(rtCode) then return false`
//      isfailed.srf      L11, L15   explicit guard    `if IsNull(rtCode) then return false`
//      isprevented.srf   L11, L15   explicit guard    `if IsNull(rtCode) then return false`
//      isvalidobject.srf L11, L15   explicit guard    `if IsNull(object) then return false`
//      isallowed.srf     L11, L14   NO GUARD          IsNull is an ARM OF THE RESULT, so null
//                                                     answers TRUE - the opposite of the other four
//      iscancelled.srf   L9         NO GUARD AT ALL    null answers false by PowerScript null
//                                                     propagation, not by a decision
//
//  So `IsAllowed(null)` is true while `IsSucceeded(null)`, `IsFailed(null)`, `IsPrevented(null)`,
//  `IsCancelled(null)` and `IsValidObject(null)` are all false. Making the six agree would be a
//  behavioural change, and the refactor's binding constraint C-B forbids exactly that: legacy
//  quirks are replicated and documented, never corrected.
//
//  THE TRI-STATE HOLE - THIS FILE IS NAMED AS A USER EXAMPLE TO PRESERVE
//  --------------------------------------------------------------------------------------------
//  The return-code algebra is nominally boolean - succeeded or failed - but three inputs fall
//  through both predicates, and one input satisfies the wrong one:
//
//      PREVENT (1)     IsSucceeded is `rtCode >= RetCode.OK` [issucceeded.srf:L12] and 1 >= 0, so
//                      A PREVENTION READS AS A SUCCESS. This is the defect the refactor names
//                      first. It is reproduced exactly.
//      CANCELLED (-2)  IsFailed excludes it explicitly with `and rtCode <> RetCode.CANCELLED`
//                      [isfailed.srf:L12] while it also fails `>= OK`, so CANCELLED IS NEITHER
//                      SUCCEEDED NOR FAILED. CANCELED and CANCELLED are two names for -2, so both
//                      spellings land in the hole.
//      null            Both predicates guard it to false, so NULL IS LIKEWISE NEITHER.
//
//  A fourth quirk sits alongside them: in the BOOLEAN form, prevent and failed are
//  indistinguishable, because isfailed.srf:L16 and isprevented.srf:L16 are the same text,
//  `return (Not rtCode)`. In the NUMERIC form they are cleanly distinct - 1 versus anything below
//  zero. See DECISION 3.
//
//  Callers must therefore not treat `!IsSucceeded(x)` as "failed" or `!IsFailed(x)` as
//  "succeeded". Those are four different questions about a five-way answer, and the gap between
//  them is load-bearing legacy behaviour that the characterization suite pins.
//
//  DECISION 1 - NULLABLE VALUE TYPES, AND NULL IS NEVER COLLAPSED TO ZERO
//  --------------------------------------------------------------------------------------------
//  The numeric overloads take `long?` and the boolean overloads take `bool?`. PowerBuilder has
//  null for value types and all six of these functions inspect it, so the parameter types must be
//  able to represent it; the refactor's null-semantics rule states this directly and adds the
//  failure mode it prevents. Collapsing null to 0 would make `IsSucceeded(null)` return true,
//  because 0 >= 0 - it would convert "neither succeeded nor failed" into "succeeded" and silently
//  delete the tri-state hole this file exists to preserve. A non-nullable `long` parameter is
//  therefore not an option, and neither is a `?? 0` anywhere below.
//
//  Every member returns plain `bool`, never `bool?`. The legacy prototypes are all declared
//  `global function boolean`, so each always produces a definite answer even when its input is
//  indefinite; propagating null into the RESULT would widen the contract.
//
//  The legacy parameters are declared `readonly`, which the refactor's type map renders as `in`.
//  It is deliberately not used here: `in` on an 8-byte or 1-byte value type adds an indirection
//  for no benefit, and these parameters are never assigned, so the guarantee is already met.
//
//  DECISION 2 - THE GUARD'S PRESENCE OR ABSENCE MIRRORS THE ORACLE, STATEMENT FOR STATEMENT
//  --------------------------------------------------------------------------------------------
//  Where the oracle writes an explicit `if IsNull(...) then return false` line, this file writes
//  an explicit `if (value is null) { return false; }`. Where the oracle folds the null test into a
//  single return expression, this file writes a single return expression. Where the oracle has no
//  null test at all, this file has none.
//
//  This is not stylistic mimicry. It makes the asymmetry visible in the CODE SHAPE rather than
//  only in a comment, so the four-guards-two-not split survives a reader who skims the bodies and
//  ignores the prose. C# offers shorter spellings for several of these bodies - a lifted
//  comparison returns false for null without any guard, so four of the guards could be deleted
//  with no change in observable behaviour - and they are deliberately not used, because deleting a
//  guard the oracle wrote would erase the evidence that isallowed and iscancelled are the two that
//  never had one.
//
//  DECISION 3 - THE TWO IDENTICAL BOOLEAN NEGATIONS STAY DUPLICATED
//  --------------------------------------------------------------------------------------------
//  `IsFailed(bool?)` and `IsPrevented(bool?)` have the same body, because isfailed.srf:L16 and
//  isprevented.srf:L16 have the same body. There is deliberately NO shared private helper, no
//  expression-bodied delegation of one to the other, and no `=> IsFailed(value)` on the second.
//
//  The reason is that these are two independent legacy functions that happen to coincide, not one
//  function with two names. Routing one through the other would assert a relationship the oracle
//  does not state and would make a future divergence impossible to express without first undoing
//  the refactoring. The collapse is listed among the quirks to carry, so it is carried as what it
//  is: a coincidence, recorded twice, with a cross-reference on each.
//
//  Note also what is NOT claimed: neither body is a mistake. This file contains no comment
//  suggesting isprevented's boolean overload "should" test for PREVENT, because the oracle is the
//  specification and the oracle says `Not rtCode`.
//
//  DECISION 4 - IsCancelled HAS ONE OVERLOAD AND NO GUARD, AND BOTH ARE PRESERVED
//  --------------------------------------------------------------------------------------------
//  iscancelled.srf declares exactly one prototype, at L6, and its entire body is one line, L9:
//  `return rtCode = RetCode.CANCELLED`. There is no boolean overload and no null guard. It is also
//  the only one of the six with no `$PBExportComments` header line.
//
//  No `IsCancelled(bool?)` is added for symmetry. Adding one would widen the surface beyond the
//  legacy, and "the other five have two overloads" is not evidence that this one should.
//
//  The false-for-null answer is reproduced through a lifted `Nullable<long>` comparison, which is
//  a genuine structural correspondence rather than a coincidence: in PowerScript, comparing null
//  yields null, and null coerces to false at a `boolean` return; in C#, `long? == long` yields
//  false when the left operand has no value. Both languages let the null answer FALL OUT of an
//  unguarded comparison. That is why this body needs no guard to match, and why adding one - which
//  would not change the answer - would still be wrong: it would misrepresent where the answer
//  comes from, and invite a reader to believe the missing guard had been a bug.
//
//  DECISION 5 - IsAllowed's THIRD ARM IS UNDOCUMENTED AND IS REPRODUCED ANYWAY
//  --------------------------------------------------------------------------------------------
//  isallowed.srf:L11 is `return (rtCode = RetCode.ALLOW or IsNull(rtCode) or rtCode > 1000)`. The
//  third arm has no explanation anywhere in the repository: no comment at the declaration, no
//  mention in any of the five legacy documents, and no constant in retcode.sru equal to 1000 or
//  1001 that would hint at an intended boundary. It is reproduced verbatim and labelled as
//  undocumented rather than guessed at, dropped, or "corrected" to a named constant.
//
//  Its reach is worth stating because it is narrower than it looks. Of the 156 constants in
//  RetCode, the largest positive value that a caller would plausibly pass here is
//  SQLITE_OK_LOAD_PERMANENTLY, which is SQLITE_OK + (1 * 256) = 256 [retcode.sru:L206]. That does
//  NOT exceed 1000, so no catalogued return code reaches the third arm; only an out-of-catalogue
//  value above 1000 does. The boundary is exclusive: 1000 does not satisfy it, 1001 does.
//
//  The arm order is the one deviation from the oracle's text in this file, and it is confined to
//  evaluation order rather than result. PowerScript evaluates the three arms as one expression;
//  C# `||` short-circuits left to right, so the null arm is hoisted to the front here to guarantee
//  that no arm is ever reached holding a null operand. All three arms are pure, side-effect-free
//  comparisons, so reordering a chain of `||` over them cannot change the answer for any input.
//  The hoist is belt-and-braces rather than load-bearing - the remaining two arms are lifted
//  nullable comparisons that already answer false for null - but it is what makes the safety
//  obvious at a glance and keeps a later edit from introducing an exception the legacy cannot
//  throw. The oracle's own order, for the record, is ALLOW, then IsNull, then the 1000 test.
//
//  DECISION 6 - IsValidObject COLLAPSES TWO LEGACY OVERLOADS INTO ONE MEMBER
//  --------------------------------------------------------------------------------------------
//  isvalidobject.srf declares two prototypes, `readonly any object` at L7 and
//  `readonly powerobject object` at L8, whose bodies at L11-L13 and L15-L17 are identical. The
//  refactor's type map renders `any` as `object?` and `powerobject` as `object`, and two overloads
//  differing only in the nullable annotation of a reference parameter are not distinguishable in
//  C# - nullability is not part of a method signature - so declaring both is not merely redundant,
//  it does not compile. One member takes `object?` and subsumes both.
//
//  This narrows the SIGNATURE, not the BEHAVIOUR. The legacy pair distinguishes a boxed `any` from
//  a typed object reference, a distinction with no C# analogue because a C# `object` reference
//  already holds either; and since the two legacy bodies are byte-identical, no caller of either
//  can observe which one it reached. Every legacy call site therefore has exactly one destination
//  here and gets exactly the answer it got before.
//
//  DECISION 7 - WHAT `IsValid` MEANS, AND THE ONE PART OF IT NOT REPRODUCED
//  --------------------------------------------------------------------------------------------
//  isvalidobject.srf:L12 and L16 both return `IsValid(object)`, the PowerBuilder intrinsic that
//  reports whether an object reference is LIVE: created, and not yet `Destroy`ed. PowerBuilder
//  object identity is a pointer with manual lifetime, so a variable can hold a non-null reference
//  to an object that has already been destroyed, and `IsValid` is how a caller detects it. That
//  state is precisely what the 472 legacy call sites are guarding against.
//
//  .NET HAS NO EQUIVALENT STATE. A managed reference is either null or points at an object the
//  garbage collector guarantees is alive; there is no destroy-and-dangle. The exact managed
//  equivalent of "created and not yet destroyed" is therefore "the reference is not null", and
//  that is what this member implements.
//
//  DISPOSAL IS DELIBERATELY NOT CONSULTED, and this is the one part of `IsValid` not reproduced.
//  The reasoning, stated so it is auditable rather than assumed:
//
//      There is no way to ask.       `IDisposable` exposes only `Dispose()`. The BCL publishes no
//                                    general, side-effect-free query for whether an arbitrary
//                                    object has been disposed. A try/catch probe is rejected
//                                    outright: it is neither cheap nor exception-free, and this is
//                                    the highest-traffic member in the project.
//      Type-sniffing would be wrong, not merely partial. The per-type approximations that do exist
//                                    do not mean disposal. `Stream.CanRead` is false for a live
//                                    write-only stream, and `SafeHandle.IsInvalid` is true for a
//                                    handle that was never valid. Either would make this member
//                                    answer false for an object the legacy calls valid - a
//                                    behavioural change dressed as fidelity, and a C-B violation.
//      Disposal is not the legacy concept anyway. A disposed .NET object is still a live, valid
//                                    reference: its members are reachable and its identity is
//                                    intact. Equating disposal with PowerBuilder destruction would
//                                    import a distinction the oracle never made.
//
//  So the member answers the question the legacy asked, in the only form the target runtime can
//  answer it, and the residual gap - a disposed-but-non-null argument answers true, where a
//  destroyed PowerBuilder object would have answered false - is recorded here rather than papered
//  over. It is not reachable from a faithful port of the legacy call sites, because none of them
//  disposes an object and then asks whether it is valid; the legacy has no disposal concept to do
//  it with.
//
//  DECISION 8 - NO CONSTANT IS DECLARED OR ALIASED IN THIS FILE
//  --------------------------------------------------------------------------------------------
//  The bodies below reference RetCode.OK, RetCode.PREVENT, RetCode.CANCELLED and RetCode.ALLOW,
//  and declare nothing. That is a hard build constraint, not a preference. The refactor preserves
//  the legacy SCREAMING_SNAKE constant spellings verbatim, and the repository-root .editorconfig
//  suppresses the naming diagnostics for exactly two files in this project - RetCode.cs and
//  Enums.cs - while Directory.Build.props sets TreatWarningsAsErrors. A preserved-spelling
//  identifier DECLARED here would therefore be a build error, not advice. Uses are unaffected:
//  the naming diagnostic reports declarations only, which is why this file needs no suppression of
//  its own and must not acquire one.
//
//  The comparisons are also written against the named constants rather than against literals -
//  `value >= RetCode.OK`, not `value >= 0` - because that is what the oracle does, and because a
//  literal would hide which of the three names for zero the legacy line actually cites.
//
//  THE COMPLETE TRUTH TABLE
//  --------------------------------------------------------------------------------------------
//  Derived from the ten bodies below, not from intuition. The starred cells are the preserved
//  defects; a change to any one of them is a behavioural regression, and each is pinned by a test.
//
//      NUMERIC  (long?)                    Succeeded  Failed  Prevented  Allowed  Cancelled
//      null                                  false     false    false     TRUE*     false
//      0     OK / SUCCESS / ALLOW            true      false    false     true      false
//      1     PREVENT                         TRUE*     false    true      false     false
//      -1    FAILED                          false     true     false     false     false
//      -2    CANCELED / CANCELLED            false     FALSE*   false     false     true
//      -3    E_INVALID_ARGUMENT              false     true     false     false     false
//      -33   E_RETRY                         false     true     false     false     false
//      -2000 E_NO_SUPPORT                    false     true     false     false     false
//      -4000 UNKNOWN                         false     true     false     false     false
//      256   SQLITE_OK_LOAD_PERMANENTLY      true      false    false     false     false
//      1000                                  true      false    false     false     false
//      1001                                  true      false    false     TRUE*     false
//
//      BOOLEAN  (bool?)                    Succeeded  Failed  Prevented  Allowed
//      null                                  false     false    false     TRUE*
//      true                                  true      false    false     true
//      false                                 false     true     true*     false
//
//  Three readings of that table are the whole point of this file. The Allowed column disagrees
//  with every other column on null. The Failed column disagrees with the Cancelled column on -2,
//  where neither Succeeded nor Failed claims the value. And in the boolean table the Failed and
//  Prevented columns are identical in all three rows, while in the numeric table they are not.
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//  --------------------------------------------------------------------------------------------
//    * `IsCancelled(bool?)`. See DECISION 4. The legacy has one overload; symmetry is not a
//      reason to invent a second.
//    * Any shared private helper, and any delegation of one member to another. See DECISION 3.
//      The two identical boolean negations are two functions, not one.
//    * `IsOk`, `IsPreventOnce`, `IsPreventDeep`, `IsNeither`, `IsIndeterminate`, `TryGet...`, any
//      aggregate convenience predicate and any extension method. The surface is exactly the legacy
//      surface: ten members, no more. An `IsNeither` helper in particular would be the tri-state
//      hole routed around instead of preserved, and the hole is the behaviour.
//      For the record, the broker's tri-valued veto - PREVENT_ONCE and PREVENT_DEEP - is a
//      different algebra living in PowerFramework.Shared.Eventful, and does not belong here.
//    * Any `throw`, and any argument validation. None of the six legacy functions can fail: each
//      returns a `boolean` for every input including null. A guard clause that threw would add a
//      failure mode the legacy does not have.
//    * Any logging, metric, tracing hook, dependency injection seam or instance state. These are
//      pure functions of their arguments, consumed IN PROCESS through a project reference. This
//      file is not a cross-service channel: the only published cross-service coupling in the
//      refactor is the separate PowerFramework.Contracts project, and nothing here may become a
//      back door around it.
//    * `[MethodImpl(MethodImplOptions.AggressiveInlining)]` and every other tuning attribute. The
//      refactor is explicitly not a performance refactor and publishes no performance objective,
//      so no such claim is made or implied here. The bodies are single comparisons that the JIT
//      inlines on its own merits.
//    * Any `using` directive. `long`, `bool` and `object` are keywords, and RetCode sits in this
//      same namespace, so nothing needs importing.
//    * Any `#pragma warning disable` or project-wide NoWarn. See DECISION 8.
//    * A per-file licence header. See the LICENCE note at the top of this banner.
//
//  HOW THIS FILE SATISFIES THE BINDING CONSTRAINTS
//  --------------------------------------------------------------------------------------------
//  No user rules were provided for this project, so the enterprise-standard baseline and the
//  refactor's own non-rule constraint inventory govern instead. The five that reach this file:
//
//    C-A   Shared implementation, consumed in process. No serialization, no contracts reference,
//          no logging, no DI, no I/O, no state - ten pure static functions in a library that sits
//          at the bottom of the dependency graph and references nothing.
//    C-B   No behaviour improvement. Every quirk is replicated: the prevention that reads as a
//          success, the cancelled value that is neither, the non-uniform null answers, the
//          undocumented 1000 boundary, the duplicated boolean negation, the single-overload
//          IsCancelled. Nothing is harmonised and nothing is corrected.
//    C-C   The legacy tree is the read-only oracle. All six .srf files were read in full and none
//          was modified; every member below cites the ws_objects locator it reproduces, because
//          nothing outside ws_objects can adjudicate these answers.
//    C-K   Every technology-specific decision is documented at its point of reproduction: the
//          nullable value types (DECISION 1), the guard-shape mirroring (2), the preserved
//          duplication (3), the lifted-comparison correspondence for IsCancelled (4), the
//          short-circuit hoist in IsAllowed (5), the two-overloads-to-one collapse (6), the
//          IsValid substitution and its residual gap (7), and the constant-declaration ban (8).
//    Null semantics. Nullable value types throughout, null handled explicitly per member, and
//          null never collapsed to zero - see DECISION 1 for why that collapse would delete the
//          tri-state hole outright.
// ==============================================================================================

namespace PowerFramework.Shared.Kernel;

/// <summary>
/// The PowerFramework return-code algebra and object-validity test, ported from the six
/// <c>is*.srf</c> global function objects under <c>ws_objects/pfw.shared.pbl.src/</c>.
/// </summary>
/// <remarks>
/// <para>
/// Ten pure static functions over the constants in <see cref="RetCode"/>. The algebra is nominally
/// boolean but is in fact TRI-STATE, and the gaps are deliberate legacy behaviour that this class
/// reproduces rather than corrects: <see cref="IsSucceeded(long?)"/> answers <see langword="true"/>
/// for <see cref="RetCode.PREVENT"/> because it tests <c>&gt;= RetCode.OK</c>
/// [issucceeded.srf:L12], and <see cref="RetCode.CANCELLED"/> is excluded from
/// <see cref="IsFailed(long?)"/> explicitly [isfailed.srf:L12] while also failing
/// <see cref="IsSucceeded(long?)"/>, so it is NEITHER.
/// </para>
/// <para>
/// NULL HANDLING IS DELIBERATELY NON-UNIFORM. <see cref="IsAllowed(long?)"/> and
/// <see cref="IsAllowed(bool?)"/> answer <see langword="true"/> for <see langword="null"/>, because
/// the oracle folds the null test into the result instead of guarding on it [isallowed.srf:L11,
/// L14]. Every other member answers <see langword="false"/>. Do not "make the six consistent" -
/// the asymmetry is the contract, and this file's header explains each case with its locator.
/// </para>
/// <para>
/// Consequently <c>!IsSucceeded(x)</c> does not mean "failed" and <c>!IsFailed(x)</c> does not mean
/// "succeeded". Ask the question you actually mean, and read the complete truth table in this
/// file's header before relying on a combination.
/// </para>
/// <para>
/// Every member is total: none throws, none validates its argument, and none has any side effect,
/// so a caller needs no try/catch and no pre-check. The class holds no state and is therefore
/// trivially thread safe.
/// </para>
/// </remarks>
public static class Predicates
{
    // ------------------------------------------------------------------------------------------
    //  IsSucceeded                                    ws_objects/.../issucceeded.srf (18 lines)
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Reports whether <paramref name="value"/> is a success code, i.e. zero or greater.
    /// [issucceeded.srf:L11-L13]
    /// </summary>
    /// <param name="value">A return code, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="false"/> when <paramref name="value"/> is <see langword="null"/>; otherwise
    /// <see langword="true"/> when it is greater than or equal to <see cref="RetCode.OK"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// PRESERVED DEFECT. The test is <c>&gt;= RetCode.OK</c>, so EVERY non-negative code answers
    /// <see langword="true"/> - including <see cref="RetCode.PREVENT"/>, which is <c>1</c>
    /// [retcode.sru:L42]. A PREVENTION THEREFORE READS AS A SUCCESS. This is the defect the
    /// refactor names first as behaviour to replicate; it is deliberate and must not be narrowed to
    /// <c>== RetCode.OK</c>. <see cref="RetCode.SQLITE_OK_LOAD_PERMANENTLY"/> (<c>256</c>) and any
    /// other positive value answer <see langword="true"/> for the same reason.
    /// </para>
    /// <para>
    /// PRESERVED DEFECT. <see langword="null"/> answers <see langword="false"/> here and
    /// <see cref="IsFailed(long?)"/> also answers <see langword="false"/> for it, so null is
    /// NEITHER succeeded nor failed. <see cref="RetCode.CANCELLED"/> falls in the same hole.
    /// </para>
    /// </remarks>
    public static bool IsSucceeded(long? value)
    {
        // issucceeded.srf:L11 - `if IsNull(rtCode) then return false`
        // An explicit guard, mirroring the oracle statement for statement (DECISION 2). A lifted
        // `value >= RetCode.OK` would answer false for null too, so this line is not load-bearing
        // for the RESULT - it is load-bearing as evidence that the oracle wrote a guard here and
        // did not write one in isallowed.srf or iscancelled.srf.
        if (value is null)
        {
            return false;
        }

        // issucceeded.srf:L12 - `return (rtCode >= RetCode.OK)`
        // >= and not ==, which is exactly why PREVENT (1) reads as a success.
        return value.Value >= RetCode.OK;
    }

    /// <summary>
    /// Reports whether <paramref name="value"/> is a success, returning the flag unchanged.
    /// [issucceeded.srf:L15-L17]
    /// </summary>
    /// <param name="value">A success flag, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="false"/> when <paramref name="value"/> is <see langword="null"/>; otherwise
    /// <paramref name="value"/> itself.
    /// </returns>
    /// <remarks>
    /// The oracle body is the bare <c>return rtCode</c>, so this overload is an identity function
    /// on the non-null domain. It exists because the legacy declares it, giving PowerScript callers
    /// one predicate name over both a numeric code and a boolean flag.
    /// </remarks>
    public static bool IsSucceeded(bool? value)
    {
        // issucceeded.srf:L15 - `if IsNull(rtCode) then return false`
        if (value is null)
        {
            return false;
        }

        // issucceeded.srf:L16 - `return rtCode`  (the input, unchanged)
        return value.Value;
    }

    // ------------------------------------------------------------------------------------------
    //  IsFailed                                          ws_objects/.../isfailed.srf (18 lines)
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Reports whether <paramref name="value"/> is a failure code - negative, but NOT cancelled.
    /// [isfailed.srf:L11-L13]
    /// </summary>
    /// <param name="value">A return code, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="false"/> when <paramref name="value"/> is <see langword="null"/> or equals
    /// <see cref="RetCode.CANCELLED"/>; otherwise <see langword="true"/> when it is less than
    /// <see cref="RetCode.OK"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// PRESERVED DEFECT - THE TRI-STATE HOLE. <see cref="RetCode.CANCELLED"/> (<c>-2</c>) is
    /// excluded by an explicit second conjunct, yet it also fails
    /// <see cref="IsSucceeded(long?)"/>'s <c>&gt;= RetCode.OK</c> test, so CANCELLED IS NEITHER
    /// SUCCEEDED NOR FAILED. <see cref="RetCode.CANCELED"/> is a second name for the same
    /// <c>-2</c> [retcode.sru:L44-L45], so both spellings fall in the hole. Do not remove the
    /// exclusion, and do not treat <c>!IsFailed(x)</c> as "succeeded".
    /// </para>
    /// <para>
    /// PRESERVED DEFECT. <see langword="null"/> answers <see langword="false"/> here as well, so
    /// null is likewise neither.
    /// </para>
    /// </remarks>
    public static bool IsFailed(long? value)
    {
        // isfailed.srf:L11 - `if IsNull(rtCode) then return false`
        if (value is null)
        {
            return false;
        }

        // isfailed.srf:L12 - `return (rtCode < RetCode.OK and rtCode <> RetCode.CANCELLED)`
        // The second conjunct is the tri-state hole. CANCELED (retcode.sru:L44) shares CANCELLED's
        // value, so testing either name excludes both; CANCELLED is used because it is the name the
        // oracle line cites.
        return value.Value < RetCode.OK && value.Value != RetCode.CANCELLED;
    }

    /// <summary>
    /// Reports whether <paramref name="value"/> indicates failure, i.e. is <see langword="false"/>.
    /// [isfailed.srf:L15-L17]
    /// </summary>
    /// <param name="value">A success flag, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="false"/> when <paramref name="value"/> is <see langword="null"/>; otherwise
    /// the negation of <paramref name="value"/>.
    /// </returns>
    /// <remarks>
    /// This body is textually identical to <see cref="IsPrevented(bool?)"/> in the oracle
    /// [isfailed.srf:L16, isprevented.srf:L16] and is preserved as such, so in the BOOLEAN form
    /// failure and prevention are indistinguishable. They remain distinct in the numeric form. See
    /// DECISION 3 in this file's header for why the duplication is not factored out.
    /// </remarks>
    public static bool IsFailed(bool? value)
    {
        // isfailed.srf:L15 - `if IsNull(rtCode) then return false`
        if (value is null)
        {
            return false;
        }

        // isfailed.srf:L16 - `return (Not rtCode)`
        return !value.Value;
    }

    // ------------------------------------------------------------------------------------------
    //  IsPrevented                                    ws_objects/.../isprevented.srf (18 lines)
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Reports whether <paramref name="value"/> is exactly <see cref="RetCode.PREVENT"/>, i.e. a
    /// veto. [isprevented.srf:L11-L13]
    /// </summary>
    /// <param name="value">A return code, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="false"/> when <paramref name="value"/> is <see langword="null"/>; otherwise
    /// <see langword="true"/> only when it equals <see cref="RetCode.PREVENT"/>.
    /// </returns>
    /// <remarks>
    /// An exact equality test, unlike <see cref="IsSucceeded(long?)"/>'s range test - which is
    /// precisely why a vetoed operation satisfies BOTH predicates: <c>1 == RetCode.PREVENT</c> and
    /// <c>1 &gt;= RetCode.OK</c> are both true. Callers that must distinguish a veto from a plain
    /// success have to ask this question specifically; asking <see cref="IsSucceeded(long?)"/>
    /// cannot tell them.
    /// </remarks>
    public static bool IsPrevented(long? value)
    {
        // isprevented.srf:L11 - `if IsNull(rtCode) then return false`
        if (value is null)
        {
            return false;
        }

        // isprevented.srf:L12 - `return (rtCode = RetCode.PREVENT)`
        return value.Value == RetCode.PREVENT;
    }

    /// <summary>
    /// Reports whether <paramref name="value"/> indicates prevention, i.e. is
    /// <see langword="false"/>. [isprevented.srf:L15-L17]
    /// </summary>
    /// <param name="value">A success flag, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="false"/> when <paramref name="value"/> is <see langword="null"/>; otherwise
    /// the negation of <paramref name="value"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// PRESERVED QUIRK. The oracle body here, <c>return (Not rtCode)</c> [isprevented.srf:L16], is
    /// TEXTUALLY IDENTICAL to <see cref="IsFailed(bool?)"/>'s [isfailed.srf:L16]. Prevention and
    /// failure are therefore INDISTINGUISHABLE in the boolean form for every input, while the
    /// numeric overloads separate them cleanly - <c>== RetCode.PREVENT</c> versus
    /// <c>&lt; RetCode.OK</c>.
    /// </para>
    /// <para>
    /// Both bodies are reproduced independently rather than one delegating to the other, because
    /// the oracle declares two separate functions that coincide; see DECISION 3 in this file's
    /// header. Neither is treated as a mistake: the oracle is the specification.
    /// </para>
    /// </remarks>
    public static bool IsPrevented(bool? value)
    {
        // isprevented.srf:L15 - `if IsNull(rtCode) then return false`
        if (value is null)
        {
            return false;
        }

        // isprevented.srf:L16 - `return (Not rtCode)`
        // Deliberately duplicated from IsFailed(bool?) rather than shared (DECISION 3): two legacy
        // functions that coincide, not one function with two names.
        return !value.Value;
    }


    // ------------------------------------------------------------------------------------------
    //  IsAllowed                                        ws_objects/.../isallowed.srf (16 lines)
    //  --------------------------------------------------------------------------------------
    //  The odd one out, twice over: neither overload has a null guard, so BOTH answer true for
    //  null; and the numeric overload carries a third arm, `rtCode > 1000`, that nothing in the
    //  repository explains. See DECISION 5 in this file's header.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Reports whether <paramref name="value"/> permits the operation to proceed:
    /// <see cref="RetCode.ALLOW"/>, <see langword="null"/>, or any value above <c>1000</c>.
    /// [isallowed.srf:L11]
    /// </summary>
    /// <param name="value">A return code, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="value"/> is <see langword="null"/>, or equals
    /// <see cref="RetCode.ALLOW"/>, or is greater than <c>1000</c>; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// PRESERVED ANOMALY 1 - TRUE ON NULL. The oracle has NO null guard here: <c>IsNull(rtCode)</c>
    /// is an ARM OF THE RESULT, so <see langword="null"/> answers <see langword="true"/> - the
    /// opposite of <see cref="IsSucceeded(long?)"/>, <see cref="IsFailed(long?)"/>,
    /// <see cref="IsPrevented(long?)"/> and <see cref="IsCancelled(long?)"/>, which all answer
    /// <see langword="false"/>. Read as an absent answer being treated as permission. The
    /// asymmetry is the contract and must not be normalised.
    /// </para>
    /// <para>
    /// PRESERVED ANOMALY 2 - AN UNDOCUMENTED THIRD ARM. <c>rtCode &gt; 1000</c> has no explanation
    /// anywhere in the repository: no comment at the declaration, no mention in any legacy
    /// document, and no constant in <see cref="RetCode"/> equal to <c>1000</c>. It is reproduced
    /// verbatim and labelled undocumented rather than guessed at or dropped. The boundary is
    /// EXCLUSIVE - <c>1000</c> answers <see langword="false"/> and <c>1001</c> answers
    /// <see langword="true"/> - and no catalogued return code reaches it, the largest plausible
    /// positive being <see cref="RetCode.SQLITE_OK_LOAD_PERMANENTLY"/> at <c>256</c>.
    /// </para>
    /// <para>
    /// Note that <see cref="RetCode.ALLOW"/> is <c>0</c>, the same value as
    /// <see cref="RetCode.OK"/> and <see cref="RetCode.SUCCESS"/> [retcode.sru:L39-L41], so the
    /// first arm also matches a plain success. It is written against the <c>ALLOW</c> spelling
    /// because that is the name the oracle line cites.
    /// </para>
    /// </remarks>
    public static bool IsAllowed(long? value)
    {
        // isallowed.srf:L11 - `return (rtCode = RetCode.ALLOW or IsNull(rtCode) or rtCode > 1000)`
        //
        // One expression and NO null guard, mirroring the oracle's shape (DECISION 2). The null arm
        // is hoisted ahead of the two comparisons rather than left in the oracle's middle position,
        // so that C#'s left-to-right `||` short-circuit guarantees no arm is ever reached holding a
        // null operand - which is what keeps this from throwing an exception the legacy cannot
        // throw. The hoist cannot change the answer for any input: all three arms are pure,
        // side-effect-free comparisons, so the order of a `||` chain over them is immaterial. It is
        // also belt-and-braces rather than load-bearing, because the two remaining arms are lifted
        // Nullable<long> comparisons that already answer false for null.
        //
        // The `> 1000` arm is the undocumented one. It stays.
        return value is null || value == RetCode.ALLOW || value > 1000;
    }

    /// <summary>
    /// Reports whether <paramref name="value"/> permits the operation to proceed:
    /// <see langword="true"/> or <see langword="null"/>. [isallowed.srf:L14]
    /// </summary>
    /// <param name="value">A permission flag, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="value"/> is <see langword="true"/> or
    /// <see langword="null"/>; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// PRESERVED ANOMALY - TRUE ON NULL, again. Like its numeric sibling this overload has no null
    /// guard; the oracle body is <c>return (rtCode or IsNull(rtCode))</c>, so an absent flag reads
    /// as permission. Every other boolean overload in this class answers <see langword="false"/>
    /// for <see langword="null"/>.
    /// </remarks>
    public static bool IsAllowed(bool? value)
    {
        // isallowed.srf:L14 - `return (rtCode or IsNull(rtCode))`
        // One expression and no null guard, as above. The null arm is hoisted for the same reason
        // and with the same non-effect on the answer; `value == true` is a lifted comparison that
        // already answers false for null.
        return value is null || value == true;
    }

    // ------------------------------------------------------------------------------------------
    //  IsCancelled                                     ws_objects/.../iscancelled.srf (11 lines)
    //  --------------------------------------------------------------------------------------
    //  The smallest of the six: ONE overload, ONE line, NO null guard. There is deliberately no
    //  boolean counterpart. See DECISION 4 in this file's header.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Reports whether <paramref name="value"/> is exactly <see cref="RetCode.CANCELLED"/>.
    /// [iscancelled.srf:L9]
    /// </summary>
    /// <param name="value">A return code, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> only when <paramref name="value"/> equals
    /// <see cref="RetCode.CANCELLED"/>; <see langword="false"/> otherwise, including for
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the ONLY member of this class with a single overload - the oracle declares no
    /// boolean form [iscancelled.srf:L6] - and none is added for symmetry, because doing so would
    /// widen the surface beyond the legacy.
    /// </para>
    /// <para>
    /// PRESERVED SHAPE - THERE IS NO NULL GUARD. The oracle's entire body is one unguarded
    /// comparison. <see langword="null"/> still answers <see langword="false"/>, but it does so by
    /// null PROPAGATION rather than by a decision: in PowerScript a comparison with null yields
    /// null, which coerces to <see langword="false"/> at a <c>boolean</c> return. The C# body below
    /// reproduces both the answer and the shape - see DECISION 4. The missing guard is not a bug
    /// and adding one would not change any answer; it would only misrepresent where the answer
    /// comes from.
    /// </para>
    /// <para>
    /// <see cref="RetCode.CANCELED"/> is a second name for the same <c>-2</c>
    /// [retcode.sru:L44-L45], so both spellings answer <see langword="true"/>. Note the
    /// relationship to the tri-state hole: a value this predicate accepts is one that BOTH
    /// <see cref="IsSucceeded(long?)"/> and <see cref="IsFailed(long?)"/> reject.
    /// </para>
    /// </remarks>
    public static bool IsCancelled(long? value)
    {
        // iscancelled.srf:L9 - `return rtCode = RetCode.CANCELLED`  (the entire body)
        //
        // No guard, exactly as the oracle wrote it. This is a lifted Nullable<long> comparison,
        // which yields false when the left operand has no value - the same "null falls out of an
        // unguarded comparison" behaviour PowerScript gets from null propagation and a boolean
        // return. The correspondence is structural, not coincidental, which is why no guard is
        // needed to match and why none is added.
        return value == RetCode.CANCELLED;
    }

    // ------------------------------------------------------------------------------------------
    //  IsValidObject                                 ws_objects/.../isvalidobject.srf (18 lines)
    //  --------------------------------------------------------------------------------------
    //  The most heavily used member of this project: 472 call sites across ws_objects. Two legacy
    //  overloads with byte-identical bodies collapse into one member here, because C# cannot
    //  distinguish them. See DECISIONS 6 and 7 in this file's header.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Reports whether <paramref name="value"/> is a usable object reference, i.e. not
    /// <see langword="null"/>. [isvalidobject.srf:L11-L17]
    /// </summary>
    /// <param name="value">Any object reference, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="false"/> when <paramref name="value"/> is <see langword="null"/>; otherwise
    /// <see langword="true"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// SUBSUMES BOTH LEGACY OVERLOADS. The oracle declares one taking <c>any</c> and one taking
    /// <c>powerobject</c> [isvalidobject.srf:L7-L8] whose bodies are identical. The refactor's type
    /// map renders those as <c>object?</c> and <c>object</c>, which C# cannot tell apart -
    /// nullability is not part of a signature - so a single <c>object?</c> parameter carries both.
    /// That narrows the SIGNATURE, not the BEHAVIOUR: the legacy distinction is unobservable
    /// because the two bodies agree, so every legacy call site gets the answer it got before.
    /// </para>
    /// <para>
    /// SUBSTITUTION FOR <c>IsValid</c>. The oracle returns the PowerBuilder intrinsic
    /// <c>IsValid(object)</c>, which reports whether a reference is LIVE - created and not yet
    /// <c>Destroy</c>ed. .NET has no destroy-and-dangle state: a reference is either
    /// <see langword="null"/> or points at an object the garbage collector guarantees is alive, so
    /// the exact managed equivalent of "created and not yet destroyed" is "not null".
    /// </para>
    /// <para>
    /// DISPOSAL IS NOT CONSULTED, and that is the one part of <c>IsValid</c> not reproduced. The
    /// BCL publishes no general side-effect-free query for whether an arbitrary object has been
    /// disposed; a try/catch probe is rejected as neither cheap nor exception-free; and the
    /// per-type approximations that exist do not actually mean disposal - <c>Stream.CanRead</c> is
    /// <see langword="false"/> for a live write-only stream and <c>SafeHandle.IsInvalid</c> is
    /// <see langword="true"/> for a handle that was never valid - so consulting them would make
    /// this member answer <see langword="false"/> for objects the legacy calls valid, which is a
    /// behavioural change rather than added fidelity. A disposed .NET object is in any case still a
    /// live, valid reference, so disposal is not the concept the oracle was testing. The residual
    /// gap is therefore that a disposed-but-non-null argument answers <see langword="true"/>; it is
    /// unreachable from a faithful port of the legacy call sites, none of which has a disposal
    /// concept to reach it with. DECISION 7 records the full reasoning.
    /// </para>
    /// <para>
    /// The implementation is a single reference comparison: allocation-free, exception-free, and
    /// safe on any argument including a boxed value type.
    /// </para>
    /// </remarks>
    public static bool IsValidObject(object? value)
    {
        // isvalidobject.srf:L11 and L15 - `if IsNull(object) then return false`
        // isvalidobject.srf:L12 and L16 - `return IsValid(object)`
        //
        // The two legacy bodies are identical, so both collapse onto this one (DECISION 6). The
        // guard and the IsValid call also collapse: in .NET the only way a reference can fail to
        // denote a live object is by being null, so `IsValid` after a null guard is unconditionally
        // true and the pair reduces to exactly this test (DECISION 7). No disposal check is
        // performed, deliberately; the reason is documented above rather than left to be inferred.
        return value is not null;
    }
}

