// ======================================================================================================
//  FrameworkInitializer - the legacy PowerFramework lifecycle, re-expressed as ASP.NET Core host
//  startup and shutdown, with FAIL FAST PRESERVED AS FAIL FAST
//  ----------------------------------------------------------------------------------------------------
//  PORTED FROM   ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L3, L7-L8
//                    the native declaration and its TWO overloads, both returning long
//                ws_objects/pfw.base.pbl.src/pfwfinalize.srf:L3, L7
//                    the zero argument counterpart, also returning long
//                ws_objects/pfw.base.pbl.src/pfwversion.srf:L3, L7
//                    the version accessor, declared all lowercase
//                ws_objects/pfw.base.pbl.src/n_initializer.sru:L8, L10, L23-L31
//                    the alternative auto instantiated lifecycle object, its global auto instance, and
//                    the strict inverse create/destroy ordering this file's ordering mirrors
//                ws_objects/pfw.pbl.src/pfw.sra:L88-L91, L108
//                    the ONLY two call sites in the entire 544 object estate
//                ws_objects/pfw.shared.pbl.src/enums.sru:L40-L49
//                    the capability bits the flags overload carries, and their composite
//                docs/README.md:L3, L9, L11-L12, L15, L19-L21, L26, L30, L38-L42
//                    the authoritative statement of what initialization means and when it FAILS
//
//  ORACLE STATUS Every path above is READ ONLY. Those files are the behavioural oracle for parity
//                testing and never an edit target, so every assertion below carries the locator that
//                settles it. Nothing here was inferred from an identifier's name, and no line of any of
//                them was edited, moved, reformatted or copied into the build.
//
//  REPOSITORY ANOMALY WORTH ONE LINE, BECAUSE A RELATIVE REFERENCE WOULD BE AMBIGUOUS
//  ----------------------------------------------------------------------------------------------------
//  TWO DISTINCT FILES ARE NAMED pfw.sra. ws_objects/pfw.pbl.src/pfw.sra is the framework application and
//  is the authoritative composition root reference cited throughout this file;
//  ws_objects/pfw.pack.pbl.src/pfw.sra is the PowerBuilder packager and is a different program entirely.
//  Every citation below states the full path for that reason.
//
//  THE WHOLE LEGACY SURFACE IS THREE DECLARATIONS AND TWO CALL SITES
//  ----------------------------------------------------------------------------------------------------
//  The three entry points are DECLARATION ONLY. Each is a function_object bound to the closed
//  pfw.dll [pfwinitialize.srf:L3, pfwfinalize.srf:L3, pfwversion.srf:L3], there is no PowerScript body
//  to read and no C++ source for that binary anywhere in the repository. So the specification for what
//  initialization MEANS is docs/README.md, not code, and that is why this file cites the document as
//  heavily as it cites the sources.
//
//      pfwinitialize.srf:L7   global function long pfwInitialize ()
//      pfwinitialize.srf:L8   global function long pfwInitialize (readonly unsignedlong flags)
//      pfwfinalize.srf:L7     global function long pfwFinalize ()
//      pfwversion.srf:L7      global function  string	pfwversion()
//
//  Exhaustive search of all 544 objects finds exactly two call sites, both in the framework application:
//
//      ws_objects/pfw.pbl.src/pfw.sra:L91    pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)
//                                            the FIRST EXECUTABLE STATEMENT of the open event; L88-L89
//                                            are variable declarations
//      ws_objects/pfw.pbl.src/pfw.sra:L108   event close;pfwFinalize()
//                                            the ONLY statement of the close event
//
//  The flags overload is therefore the only one the estate exercises, and the zero argument overload has
//  no call site anywhere. Both facts are reproduced below: the composition root always passes a mask, and
//  the parameterless overload is carried on the seam for API parity and marked as call site free.
//
//  FAIL FAST IS THE BEHAVIOUR, NOT AN ERROR HANDLING STYLE
//  ----------------------------------------------------------------------------------------------------
//  This is the single most important property of this file, so the evidence is written out rather than
//  summarised. All of it is in docs/README.md, the framework's own documentation:
//
//      :L3       initialization has been MANDATORY since pfw 2.0 - the framework cannot be used
//                un-initialized at all
//      :L11      pfwInitialize belongs at the VERY BEGINNING of the Application Open event
//      :L12      pfwFinalize belongs at the VERY END of the Application Close event
//      :L15      a warning marked entry: pfwFinalize MUST be paired with pfwInitialize
//      :L26      a module that is not explicitly initialized has its functionality UNUSABLE, and its
//                native library does not need to ship at all
//      :L30      a warning marked entry: a module requested WITHOUT its native library present makes
//                initialization FAIL
//      :L38-L42  a second, independent, real failure mode - an Oracle OCI driver changes the DLL search
//                path, so loading pfw.dll itself fails on a second IDE run
//
//  Two of those are live failure conditions rather than theory, which is what makes the posture load
//  bearing. A failed initialization is FATAL: the framework is unusable, so there is nothing to degrade
//  to. This port therefore prevents the host from starting and lets the process terminate. It does NOT
//  log a warning and continue, it does NOT start endpoints, and it does NOT report healthy in a reduced
//  mode. Softening a structural fault into warning-and-continue would be a behavioural change dressed up
//  as robustness, and it is exactly what the no-behaviour-improvement constraint forbids.
//
//  THE ONE DELIBERATE DEPARTURE - THIS PORT CHECKS A RETURN CODE THE LEGACY DISCARDS
//  ----------------------------------------------------------------------------------------------------
//  ws_objects/pfw.pbl.src/pfw.sra:L91 DISCARDS pfwInitialize's return code. There is no assignment, no
//  comparison and no guard - the call stands alone as a statement. Yet the same framework's documentation
//  makes a failed initialization fatal [docs/README.md:L3, :L30]. The two legacy sources contradict each
//  other, and this port follows the documentation: it CHECKS the code and treats a non success as fatal.
//
//  That is the ONLY place in this folder where a legacy laxity is intentionally not copied, it is
//  annotated again at the call site itself so a reviewer meets the justification where the decision
//  lives, and the reasoning does NOT extend to anything else. Every other legacy quirk in scope is
//  reproduced verbatim - including the two the return code algebra itself carries, described next.
//
//  THE TRI-STATE HOLE, DECIDED RATHER THAN INHERITED BY ACCIDENT
//  ----------------------------------------------------------------------------------------------------
//  Predicates.IsFailed is NOT the negation of Predicates.IsSucceeded, and both preserved defects matter
//  here:
//
//      IsSucceeded tests >= RetCode.OK, so RetCode.PREVENT (1) reads as a SUCCESS
//      IsFailed excludes RetCode.CANCELLED (-2) explicitly, so CANCELLED is NEITHER
//      both return false for null, so null is likewise NEITHER
//
//  So a return code has THREE possible readings, not two, and letting the third fall through an if/else
//  by accident would be a coin toss on the most safety critical decision this service makes.
//
//  THE DECISION, stated once and expressed as its own branch in the code: anything that is not
//  IsSucceeded is FATAL. docs/README.md:L3 makes a usable framework conditional on a completed
//  initialization, so the absence of a positive success signal leaves that precondition UNPROVEN, and
//  continuing on an unproven precondition is precisely the graceful degradation the fail fast posture
//  exists to prevent. A cancelled initialization is not an initialization.
//
//  The two fatal readings are nevertheless kept DISTINCT rather than merged, because they say different
//  things to whoever reads the log: a definite failure means the boundary reported a fault, while the
//  third state means the boundary reported something the algebra cannot classify at all. Both stop the
//  host; only their diagnostics differ.
//
//  And PREVENT is deliberately NOT special cased. Under the preserved algebra it IS a success and
//  initialization proceeds. Narrowing the test to == RetCode.OK would "fix" the tri state hole, which is
//  a behavioural change this refactor forbids.
//
//  THE PAIRING IS EXPLICIT STATE, NEVER A finally BLOCK
//  ----------------------------------------------------------------------------------------------------
//  docs/README.md:L15 requires pfwFinalize to be paired with pfwInitialize, and the requirement cuts
//  BOTH ways: finalize must run exactly once after an initialize that succeeded, and it must NOT run when
//  initialization never succeeded. A try/finally around the initialize call would satisfy neither half -
//  it fires on the failure path, which is the case the pairing warning excludes, and it says nothing
//  about a second call.
//
//  This file therefore tracks the pairing in an explicit interlocked state field with five states, and
//  the transition into the finalized state is the same atomic operation that decides whether to call
//  finalize at all. Exactly once, only after success, never twice, and never on the failure path -
//  by construction rather than by convention.
//
//  ORDERING - StartingAsync AND StoppedAsync, WHICH IS NOT AN ARBITRARY PAIR OF HOOKS
//  ----------------------------------------------------------------------------------------------------
//  ws_objects/pfw.base.pbl.src/n_initializer.sru:L23-L31 expresses the same lifecycle as object lifetime,
//  and its ordering is STRICTLY INVERSE:
//
//      on create    call super::create              then    TriggerEvent(this,"constructor")
//      on destroy   TriggerEvent(this,"destructor") then    call super::destroy
//
//  Construction runs the base first and the framework hook last; destruction runs the framework hook
//  first and the base last. Mapped onto a host, and matched to docs/README.md:L11-L12 which places
//  initialize at the VERY BEGINNING of Open and finalize at the VERY END of Close, that gives exactly one
//  correct pair of hooks on IHostedLifecycleService:
//
//      StartingAsync   runs for every hosted service BEFORE ANY of them is started, so it precedes the
//                      web server binding its ports. That is what makes "prevent host start" literal:
//                      when initialization fails here, no endpoint has been reached and no listener has
//                      been opened, so nothing can report healthy.
//      StoppedAsync    runs AFTER every hosted service has stopped, which is the true end of shutdown
//                      and the inverse position of StartingAsync.
//
//  StartAsync and StopAsync would both be wrong: hosted services are started in registration order, so
//  the web server - registered by the web host builder before any application service - would already
//  have started listening by the time a StartAsync on this type ran. The ordering discipline is
//  reproduced; the PowerBuilder event mechanism behind it is not.
//
//  WIDTH IS A CONTRACT - 32 BITS, SIGNED TO UNSIGNED, CROSSED EXACTLY ONCE
//  ----------------------------------------------------------------------------------------------------
//  The eight capability constants are declared Constant Long [enums.sru:L41-L48], a 32-bit SIGNED
//  PowerScript type, and the parameter they are handed to is declared readonly unsignedlong
//  [pfwinitialize.srf:L8]. PowerBuilder's unsignedlong is 32 bits wide, so the target type is C# uint and
//  NEVER ulong: a 64-bit type would silently widen a boundary the legacy declares at 32 bits.
//
//  The signed to unsigned crossing happens in exactly one place, CapabilityFlags.FromConfiguredValue,
//  which is documented there as that type's single width adaptation point and performs the conversion
//  explicitly and unchecked - reproducing the legacy handover, which performs no check either. This file
//  consumes the result and never re-crosses it: the mask is uint from CapabilityFlags.EffectiveMask
//  through to the seam argument, and no long or ulong appears anywhere on that path.
//
//  INJECTED, NEVER GLOBAL - AND THERE ARE TWO INDEPENDENT REASONS
//  ----------------------------------------------------------------------------------------------------
//  ws_objects/pfw.base.pbl.src/n_initializer.sru:L10 declares `global n_initializer n_initializer`: a
//  global auto instance SHADOWING ITS OWN TYPE NAME. This port keeps a descriptive type name and makes
//  the instance an INJECTED DEPENDENCY. It is never a global, never a static mutable, and never a
//  singleton field reached for from arbitrary code.
//
//  The second reason is the legacy's own warning. docs/README.md:L21 records that the auto instantiate
//  path MAY BE UNSTABLE, because PowerBuilder's lifetime management of a global auto instance is outside
//  user code control and its release timing may cause access violations. The framework application does
//  not use that path either - ws_objects/pfw.pbl.src/pfw.sra:L91 and :L108 call the global functions
//  explicitly. So the EXPLICIT pairing is both the mechanism the estate actually exercises and the one
//  its own documentation prefers, and dependency injection is how a .NET host expresses it.
//
//  One documentation defect recorded in passing, so nobody searches the wrong library: docs/README.md:L19
//  attributes n_initializer to pfw.common.pbl. The only n_initializer.sru in the tree is exported under
//  ws_objects/pfw.base.pbl.src/. The document is read only and is not corrected there.
//
//  WHY THE NATIVE BOUNDARY IS A SEAM, AND WHY THE SEAM LIVES IN THIS FILE
//  ----------------------------------------------------------------------------------------------------
//  pfw.dll is a closed Win32 binary. It will not exist in a Linux container, and the migration eliminates
//  the PowerBuilder Native Interface dependency outright rather than wrapping it, so there is nothing to
//  call into and nothing here loads a native library. The seam is consequently a FUNCTIONAL NECESSITY
//  and not merely a test affordance: it is where the substituted lifecycle plugs in.
//
//  It is declared in THIS file on purpose. This folder is exactly two files by design - the capability
//  gate and this one - so there is no separate interface file and no service collection extension method,
//  which would be the same third file in spirit. Declaring several types in one file is the same choice
//  the sibling configuration file already makes and states: the constraint is one FILE, not one TYPE.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//  ----------------------------------------------------------------------------------------------------
//  Recorded so each omission reads as a decision rather than an oversight.
//
//    * It does not initialize, load, probe, resolve or reference ANY deferred capability's module. UI and
//      DPIAWARE belong to the deferred DesignSystem, SCITER, BLINK, BLINKFAST and WEBVIEW to the deferred
//      ScriptBridge, and ORCA to PowerBuilder packaging tooling which is not a service at any phase.
//      Handing a mask across a boundary is not implementing the capabilities the mask names, and nothing
//      here probes for a native library, resolves a path, or holds a client, handler, placeholder or
//      exception throwing shell for any of them. SQLITE is the only bit with an in phase consumer and
//      that consumer is the Persistence service, not Gateway, so this file does not act on it either.
//    * It opens no database. No storage provider, connection string, schema, migration or data access
//      package appears here or in this project's manifest. Initialization does not touch storage.
//    * It holds no key material, credential, token, password or certificate, in any form, and it touches
//      authentication nowhere. Security is the sole token issuer in this system and Gateway holds
//      verification material only, injected through the options pattern.
//    * It does not decode the seven field assert payload. The system error path at
//      ws_objects/pfw.pbl.src/pfw.sra:L111-L144 - the CRLF split and the HALT CLOSE reproduction - is
//      owned independently by a sibling under Diagnostics/, which depends on the host lifetime
//      abstraction rather than on this file. There is no edge between them in either direction. This file
//      owns startup/shutdown pairing and its own fail fast termination, and nothing else.
//    * It never calls Environment.Exit, Process.Kill or Environment.FailFast. Fail fast is expressed by
//      throwing out of the startup path, which the host propagates so the process terminates with a non
//      zero exit code. That is both stronger - the host still unwinds whatever it had started - and
//      testable, because a test can intercept an exception and cannot intercept a process exit.
//    * It declares no SCREAMING_SNAKE constant. The refactor preserves those legacy identifier spellings
//      verbatim, and the analyzer suppressions that make them buildable are scoped in the repository root
//      .editorconfig to the files that DECLARE them. This file is deliberately not on that list, so with
//      warnings treated as errors a single such declaration here would be a BUILD ERROR. It CONSUMES
//      RetCode and Enums members by name instead, which is where those spellings belong.
//    * It asserts no latency, throughput, availability or performance property. The repository publishes
//      no such target anywhere, so none may be claimed here.
// ======================================================================================================

using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Options;
using PowerFramework.Gateway.Configuration;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Gateway.Composition;

/// <summary>
/// The injectable seam over the legacy framework's native lifecycle entry points: the two
/// <c>pfwInitialize</c> overloads, <c>pfwFinalize</c> and <c>pfwversion</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each member mirrors one PowerScript declaration exactly, including its return type, so that the
/// mapping between the two is mechanical rather than interpreted:
/// </para>
/// <list type="table">
///   <item>
///     <term><see cref="Initialize()"/></term>
///     <description>
///       <c>global function long pfwInitialize ()</c> at
///       <c>ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L7</c>.
///     </description>
///   </item>
///   <item>
///     <term><see cref="Initialize(uint)"/></term>
///     <description>
///       <c>global function long pfwInitialize (readonly unsignedlong flags)</c> at
///       <c>ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L8</c>.
///     </description>
///   </item>
///   <item>
///     <term><see cref="Finalize()"/></term>
///     <description>
///       <c>global function long pfwFinalize ()</c> at
///       <c>ws_objects/pfw.base.pbl.src/pfwfinalize.srf:L7</c>.
///     </description>
///   </item>
///   <item>
///     <term><see cref="Version()"/></term>
///     <description>
///       <c>global function string pfwversion()</c> at
///       <c>ws_objects/pfw.base.pbl.src/pfwversion.srf:L7</c>.
///     </description>
///   </item>
/// </list>
/// <para>
/// All three legacy functions are declaration only, bound to the closed <c>pfw.dll</c>
/// [<c>pfwinitialize.srf:L3</c>, <c>pfwfinalize.srf:L3</c>, <c>pfwversion.srf:L3</c>], and no source for
/// that binary exists in the repository. An implementation of this interface is therefore a SUBSTITUTE
/// for behaviour that cannot be read, never a wrapper around it: nothing in this port loads a native
/// library, and the closed binary is a Win32 artifact that does not exist in the Linux container this
/// service ships as.
/// </para>
/// <para>
/// Both <see cref="Initialize()"/> overloads and <see cref="Finalize()"/> return a PowerFramework return
/// code, so every result must be classified with <see cref="Predicates.IsSucceeded(long?)"/> and
/// <see cref="Predicates.IsFailed(long?)"/> rather than compared against zero. Those two predicates are
/// not each other's negation - see the remarks on <see cref="FrameworkInitializer"/> for the tri-state
/// hole and how this port decides it.
/// </para>
/// <para>
/// This is also the seam that makes <see cref="FrameworkInitializer"/> testable with no web host, no
/// application factory and no <c>pfw.dll</c>: a substitute implementation can return a success, a
/// failure, or a code that is neither, and can count how often each member was called, which is how the
/// pairing guarantee is verified from the outside.
/// </para>
/// </remarks>
public interface IFrameworkRuntime
{
    /// <summary>
    /// Initializes the framework environment with the implementation's default capability set.
    /// </summary>
    /// <returns>
    /// A PowerFramework return code. Classify it with <see cref="Predicates.IsSucceeded(long?)"/>;
    /// do not compare it against <see cref="RetCode.OK"/> directly.
    /// </returns>
    /// <remarks>
    /// NO CALL SITE EXISTS FOR THIS OVERLOAD, in the legacy estate or in this port. It is declared at
    /// <c>ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L7</c> and <c>docs/README.md:L9</c> records the
    /// flags argument as optional, but an exhaustive search of all 544 legacy objects finds only the
    /// flags form being called, at <c>ws_objects/pfw.pbl.src/pfw.sra:L91</c>. It is carried here for API
    /// parity with the declaration and is marked as call site free for exactly that reason.
    /// <see cref="FrameworkInitializer"/> always uses <see cref="Initialize(uint)"/>, because the
    /// composition root always has a configured mask; see that type's remarks for why a configured mask
    /// of zero is NOT routed here.
    /// </remarks>
    long Initialize();

    /// <summary>
    /// Initializes the framework environment with an explicit capability gating bitmask.
    /// </summary>
    /// <param name="flags">
    /// The module gating bitmask, composed from the capability bits declared at
    /// <c>ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48</c>. Deliberately <see cref="uint"/> and never
    /// a 64-bit type: the legacy parameter is <c>readonly unsignedlong</c>
    /// [<c>ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L8</c>] and PowerBuilder's
    /// <c>unsignedlong</c> is 32 bits wide.
    /// </param>
    /// <returns>
    /// A PowerFramework return code. Classify it with <see cref="Predicates.IsSucceeded(long?)"/>;
    /// do not compare it against <see cref="RetCode.OK"/> directly.
    /// </returns>
    /// <remarks>
    /// The overload the estate actually exercises, at <c>ws_objects/pfw.pbl.src/pfw.sra:L91</c> as
    /// <c>pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)</c>. <c>docs/README.md:L26</c> states that a module
    /// which is not named here has its functionality unusable and its native library need not ship, and
    /// <c>:L30</c> warns that a module named here WITHOUT its library present makes initialization FAIL -
    /// which is why the result is checked rather than discarded, and why a failure is fatal.
    /// </remarks>
    long Initialize(uint flags);

    /// <summary>
    /// Releases the framework environment. Must be paired with a successful initialization.
    /// </summary>
    /// <returns>
    /// A PowerFramework return code. Classify it with <see cref="Predicates.IsSucceeded(long?)"/>;
    /// do not compare it against <see cref="RetCode.OK"/> directly.
    /// </returns>
    /// <remarks>
    /// <c>global function long pfwFinalize ()</c> at
    /// <c>ws_objects/pfw.base.pbl.src/pfwfinalize.srf:L7</c>, whose only call site is the entire body of
    /// the framework application's close event, <c>event close;pfwFinalize()</c> at
    /// <c>ws_objects/pfw.pbl.src/pfw.sra:L108</c>. <c>docs/README.md:L15</c> carries a warning marked
    /// requirement that it be PAIRED with initialization, and <c>:L12</c> places it at the very end of
    /// shutdown. <see cref="FrameworkInitializer"/> enforces both, so an implementation of this member
    /// need not defend itself against being called unpaired or twice.
    /// </remarks>
    long Finalize();

    /// <summary>
    /// Returns the running framework version.
    /// </summary>
    /// <returns>The version string. Never <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// <c>global function string pfwversion()</c> at
    /// <c>ws_objects/pfw.base.pbl.src/pfwversion.srf:L7</c>. The declaration spells the name all
    /// lowercase while every call site spells it with a capital V - PowerBuilder is case insensitive, so
    /// both are the same function. <c>Version</c> is the single canonical .NET spelling used everywhere
    /// in this port.
    /// </para>
    /// <para>
    /// It has ZERO in scope call sites: its only five legacy callers are windows in the demos library,
    /// which is permanently out of scope. It is ported for API parity and to give the composition root a
    /// version to surface, and it is genuinely consumed here - <see cref="FrameworkInitializer"/> records
    /// it on the successful startup log line.
    /// </para>
    /// <para>
    /// An implementation MUST NOT return the legacy version string that appears at the head of the
    /// repository's changelog. That changelog stops in 2022 while the commit history runs years later, so
    /// it is a stale document rather than a specification, and reporting it as this assembly's version
    /// would be a fabricated fact.
    /// </para>
    /// </remarks>
    string Version();
}

/// <summary>
/// The managed substitute for the framework's native lifecycle entry points: the default
/// <see cref="IFrameworkRuntime"/> the composition root registers.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS A SUBSTITUTION, NOT A WRAPPER, AND NOT A STUB. The refactor's native binding decision for the
/// four lifecycle entry points is SUBSTITUTE: the host lifetime reproduces the mandatory pairing and the
/// fail fast posture, and the PowerBuilder Native Interface dependency is eliminated rather than
/// preserved behind a marshalling layer. This type is the boundary that decision is expressed against,
/// and <see cref="FrameworkInitializer"/> carries the substance.
/// </para>
/// <para>
/// WHY IT CANNOT FAIL, STATED PRECISELY SO THE RETURN VALUES READ AS A DECISION. What legacy
/// initialization did that could fail was LOAD NATIVE THIRD PARTY MODULES: <c>docs/README.md:L26</c>
/// records that a module not named in the mask has its functionality unusable and its library need not
/// ship, and <c>:L30</c> warns that naming a module whose library is absent makes initialization FAIL.
/// In this port there is no such module to load, for two independent reasons, and neither of them is a
/// gap this type is entitled to close:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       Seven of the eight capability bits name capability areas outside this phase - UI and DPIAWARE
///       belong to the deferred DesignSystem service, SCITER, BLINK, BLINKFAST and WEBVIEW to the
///       deferred ScriptBridge service, and ORCA to PowerBuilder packaging tooling which is not a
///       service at any phase. Probing for any of their binaries would be work on a forbidden target,
///       so this type performs no probe, no path resolution and no load for any of them.
///     </description>
///   </item>
///   <item>
///     <description>
///       The eighth bit, SQLITE, is the only one with a consumer inside this phase, and that consumer is
///       the Persistence service rather than Gateway. Gateway neither opens nor names a database, and
///       this project's manifest carries no data access package at all.
///     </description>
///   </item>
/// </list>
/// <para>
/// So the managed counterpart of "load the requested modules" is empty BY CONSTRUCTION, and there is no
/// operation here that can fail. Returning <see cref="RetCode.OK"/> is therefore the complete and correct
/// behaviour of this substitute, not a placeholder for behaviour to be filled in later. Inventing a
/// failure mode the port does not have - for instance reporting every deferred bit as unsatisfiable -
/// would make the default capability mask fatal and prevent this service from ever starting, which is a
/// fabricated requirement rather than preserved behaviour.
/// </para>
/// <para>
/// WHAT WOULD HAVE TO CHANGE LATER, so a future phase does not have to rediscover it: if a capability bit
/// ever acquires an in process owner inside Gateway, the owner's readiness check belongs in
/// <see cref="Initialize(uint)"/> and a failed check must return a failing return code so that the
/// existing fail fast path in <see cref="FrameworkInitializer"/> stops the host. No change to
/// <see cref="FrameworkInitializer"/> is needed for that, which is the point of the seam.
/// </para>
/// <para>
/// The type is stateless, immutable and thread safe. It deliberately does NOT track whether it has been
/// initialized: the pairing discipline lives in <see cref="FrameworkInitializer"/> as explicit
/// interlocked state, and a second authority here could disagree with the first.
/// </para>
/// </remarks>
public sealed class ManagedFrameworkRuntime : IFrameworkRuntime
{
    /// <summary>
    /// Initializes the framework environment with no explicit capability mask.
    /// </summary>
    /// <returns>Always <see cref="RetCode.OK"/>; see the class remarks for why this cannot fail.</returns>
    /// <remarks>
    /// Carried for parity with <c>ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L7</c>, which HAS NO CALL
    /// SITE anywhere in the legacy estate and none here either - <see cref="FrameworkInitializer"/> always
    /// passes the configured mask through <see cref="Initialize(uint)"/>. Behaviourally identical to
    /// passing an empty mask, because a mask selects native modules to load and this substitute loads
    /// none.
    /// </remarks>
    public long Initialize()
    {
        return RetCode.OK;
    }

    /// <summary>
    /// Initializes the framework environment with an explicit capability gating bitmask.
    /// </summary>
    /// <param name="flags">
    /// The 32-bit module gating bitmask, accepted in full and validated in no way - the legacy native
    /// entry point validates nothing about the mask it receives and neither does this substitute.
    /// </param>
    /// <returns>Always <see cref="RetCode.OK"/>; see the class remarks for why this cannot fail.</returns>
    public long Initialize(uint flags)
    {
        // The mask is ACCEPTED and deliberately NOT acted upon. Acting on a bit would mean initializing
        // the capability behind it, and every one of the eight belongs either to a service outside this
        // phase or to another service in it - see the class remarks. The discard is written out rather
        // than left as a silently unused parameter so that a reader cannot mistake it for an oversight.
        _ = flags;

        return RetCode.OK;
    }

    /// <summary>
    /// Releases the framework environment.
    /// </summary>
    /// <returns>Always <see cref="RetCode.OK"/>; see the class remarks for why this cannot fail.</returns>
    /// <remarks>
    /// The counterpart of <see cref="Initialize(uint)"/>: nothing was loaded, so nothing is released.
    /// The PAIRING that <c>docs/README.md:L15</c> requires is still enforced, by
    /// <see cref="FrameworkInitializer"/>, which is what guarantees this member is reached exactly once
    /// after a successful initialization and never otherwise.
    /// </remarks>
    public long Finalize()
    {
        return RetCode.OK;
    }

    /// <summary>
    /// Returns the running framework version, taken from this assembly's own metadata.
    /// </summary>
    /// <returns>
    /// The assembly's informational version when one is present, otherwise its assembly version, and
    /// <see cref="string.Empty"/> only in the case where neither is available. Never
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The version reported is THIS ASSEMBLY'S, resolved from its own attributes at run time. The legacy
    /// framework version string that heads the repository's changelog is deliberately NOT returned and
    /// does not appear anywhere in this file: that changelog stops in 2022 while the commit history runs
    /// years later, so it is a stale document rather than a specification, and hardcoding it would report
    /// a fabricated fact as this build's version.
    /// </para>
    /// <para>
    /// The informational version is preferred because the SDK emits it for every build and it carries the
    /// source revision suffix, which is what makes a log line traceable to a commit. Both fallbacks exist
    /// so the member is total: it has no failure mode and no null result for a caller to guard.
    /// </para>
    /// </remarks>
    public string Version()
    {
        Assembly assembly = typeof(ManagedFrameworkRuntime).Assembly;

        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? string.Empty;
    }
}

/// <summary>
/// Thrown when the framework lifecycle boundary fails to initialize, which is a fatal, host preventing
/// condition rather than a recoverable one.
/// </summary>
/// <remarks>
/// <para>
/// THIS EXCEPTION IS THE FAIL FAST MECHANISM. It is raised out of the host's startup path, before any
/// hosted service has been started and therefore before the web server has bound a port, so the host
/// never starts, no endpoint is reachable, nothing reports healthy, and the process terminates with a non
/// zero exit code once the host propagates it. <c>docs/README.md:L3</c> makes initialization mandatory
/// and <c>:L30</c> makes a failed initialization a real, documented outcome, so there is no reduced mode
/// to fall back to and catching this to continue would defeat the entire posture.
/// </para>
/// <para>
/// It carries <see cref="ReturnCode"/> and <see cref="RequestedCapabilities"/> as structured data rather
/// than only inside its message, so a diagnostic consumer does not have to parse prose to learn what the
/// boundary reported or which capability mask was in force.
/// </para>
/// <para>
/// The three conventional exception constructors are present because the type is public and general
/// purpose consumers expect them; the four argument constructor is the one this file uses.
/// </para>
/// </remarks>
public sealed class FrameworkInitializationException : Exception
{
    /// <summary>
    /// Initializes a new instance with a default message and no captured diagnostics.
    /// </summary>
    public FrameworkInitializationException()
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified message.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    public FrameworkInitializationException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The exception that caused this failure, if any.</param>
    public FrameworkInitializationException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance carrying the boundary's return code and the capability mask that was
    /// in force when initialization failed.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="returnCode">
    /// The return code the boundary reported, or <see langword="null"/> when the boundary threw instead
    /// of returning - and also when it returned a null code, which the preserved return code algebra
    /// classifies as neither succeeded nor failed.
    /// </param>
    /// <param name="requestedCapabilities">
    /// The 32-bit capability gating bitmask that was handed to the boundary.
    /// </param>
    /// <param name="innerException">The exception that caused this failure, if any.</param>
    public FrameworkInitializationException(
        string? message,
        long? returnCode,
        uint requestedCapabilities,
        Exception? innerException)
        : base(message, innerException)
    {
        ReturnCode = returnCode;
        RequestedCapabilities = requestedCapabilities;
    }

    /// <summary>
    /// The return code the framework boundary reported, or <see langword="null"/> when it threw rather
    /// than returning one.
    /// </summary>
    /// <remarks>
    /// Deliberately <see cref="long"/> shaped and nullable, matching the PowerScript <c>long</c> return
    /// type of <c>pfwInitialize</c> and the null that the preserved algebra treats as neither succeeded
    /// nor failed. Render it for display with <see cref="Formatting.FormatRetCode(long?)"/>.
    /// </remarks>
    public long? ReturnCode { get; }

    /// <summary>
    /// The 32-bit capability gating bitmask that was in force when initialization failed.
    /// </summary>
    /// <remarks>
    /// <see cref="uint"/> rather than a wider type, because the legacy parameter is
    /// <c>readonly unsignedlong</c> [<c>ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L8</c>] and
    /// PowerBuilder's <c>unsignedlong</c> is 32 bits wide. Wrap it in
    /// <see cref="CapabilityFlags"/> to read the individual capability bits back out.
    /// </remarks>
    public uint RequestedCapabilities { get; }
}


/// <summary>
/// The Gateway composition root's framework lifecycle: it initializes the framework before any hosted
/// service starts and releases it after every hosted service has stopped, failing the host outright when
/// initialization does not succeed.
/// </summary>
/// <remarks>
/// <para>
/// The managed successor of the two call sites that make up the framework's entire lifecycle usage in the
/// legacy estate - <c>pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)</c> as the first executable statement of
/// the application open event [<c>ws_objects/pfw.pbl.src/pfw.sra:L91</c>] and <c>pfwFinalize()</c> as the
/// only statement of its close event [<c>ws_objects/pfw.pbl.src/pfw.sra:L108</c>].
/// </para>
/// <para>
/// REGISTER IT AS A HOSTED SERVICE, EXACTLY ONCE, TOGETHER WITH AN <see cref="IFrameworkRuntime"/>. This
/// type is registration READY and performs no registration of itself: the composition root owns the
/// wiring, and there is deliberately no service collection extension method here. Because it implements
/// <see cref="IHostedLifecycleService"/>, the host invokes <see cref="StartingAsync(CancellationToken)"/>
/// before it starts ANY hosted service - so before the web server binds a port - and
/// <see cref="StoppedAsync(CancellationToken)"/> after every hosted service has stopped. That is the
/// strict inverse ordering of <c>ws_objects/pfw.base.pbl.src/n_initializer.sru:L23-L31</c> and the
/// placement <c>docs/README.md:L11-L12</c> requires.
/// </para>
/// <para>
/// FAILURE IS FATAL, AND THAT IS PRESERVED BEHAVIOUR RATHER THAN A CHOICE OF ERROR HANDLING STYLE. When
/// initialization does not succeed, this type throws <see cref="FrameworkInitializationException"/> out of
/// the startup path. The host does not start, no endpoint becomes reachable, nothing reports healthy, and
/// the process terminates with a non zero exit code. <c>docs/README.md:L3</c> makes initialization
/// mandatory since pfw 2.0 and <c>:L30</c> documents a real condition under which it fails, so there is
/// no reduced mode to degrade into. This type never calls <c>Environment.Exit</c> or
/// <c>Environment.FailFast</c>: throwing is both stronger, because the host still unwinds anything it had
/// started, and testable, because a test can intercept an exception and cannot intercept a process exit.
/// </para>
/// <para>
/// THREE READINGS OF A RETURN CODE, NOT TWO. <see cref="Predicates.IsFailed(long?)"/> is not the negation
/// of <see cref="Predicates.IsSucceeded(long?)"/>: success tests <c>&gt;= RetCode.OK</c> so
/// <see cref="RetCode.PREVENT"/> reads as a SUCCESS, failure explicitly excludes
/// <see cref="RetCode.CANCELLED"/> so cancelled is NEITHER, and both answer <see langword="false"/> for
/// null. Anything that is not a success is fatal here, and the two fatal readings are reported as
/// separate, deliberately distinct branches - see the file header for the decision and its justification.
/// </para>
/// <para>
/// THE PAIRING IS GUARANTEED BY EXPLICIT STATE. <c>docs/README.md:L15</c> requires finalize to be paired
/// with initialize, and this type enforces both halves through one interlocked state field: finalize runs
/// exactly once after an initialization that SUCCEEDED, never when initialization failed or was never
/// attempted, and never twice. It is not a <see langword="finally" /> block, which would fire on exactly
/// the failure path the pairing requirement excludes.
/// </para>
/// <para>
/// INJECTED, NEVER GLOBAL. The legacy alternative declares a global auto instance shadowing its own type
/// name [<c>ws_objects/pfw.base.pbl.src/n_initializer.sru:L10</c>], and <c>docs/README.md:L21</c> warns
/// that path may be unstable because PowerBuilder's lifetime management of such an instance is outside
/// user code control and its release timing may cause access violations. This type is an ordinary
/// injected dependency with no static state of any kind, which is why its whole behaviour is reachable
/// from a unit test with no web host, no application factory and no <c>pfw.dll</c>.
/// </para>
/// <para>
/// Instance members are safe to call concurrently. The lifecycle transitions use interlocked operations
/// so that the exactly once guarantees hold even though the host in practice invokes the lifecycle
/// callbacks sequentially.
/// </para>
/// </remarks>
public sealed class FrameworkInitializer : IHostedLifecycleService
{
    // ==================================================================================================
    //  LIFECYCLE STATES
    //  ------------------------------------------------------------------------------------------------
    //  Five states, held in one int so every transition can be a single interlocked operation. The
    //  Initializing state is what makes a concurrent or repeated start attempt detectable rather than
    //  merely unlikely, and the InitializationFailed state is what makes "do not finalize after a failed
    //  initialize" a fact about the state machine instead of an ordering assumption.
    //
    //      NotInitialized       -> Initializing            claimed by StartingAsync
    //      Initializing         -> Initialized             initialize succeeded; finalize is now OWED
    //      Initializing         -> InitializationFailed    initialize did not succeed; TERMINAL
    //      Initialized          -> Finalized               claimed by StoppedAsync; TERMINAL
    //
    //  There is no transition out of InitializationFailed or Finalized, deliberately. The legacy has no
    //  re-initialize: ws_objects/pfw.pbl.src/pfw.sra calls pfwInitialize once at :L91 and pfwFinalize once
    //  at :L108, and a process that failed to initialize is on its way out.
    //
    //  These are ordinary PascalCase private constants. This file is not on the repository root
    //  .editorconfig's scoped naming suppression list, so a SCREAMING_SNAKE declaration here would be a
    //  build error under warnings as errors; the preserved legacy spellings are CONSUMED from RetCode and
    //  Enums, never declared here.
    // ==================================================================================================

    private const int StateNotInitialized = 0;
    private const int StateInitializing = 1;
    private const int StateInitialized = 2;
    private const int StateInitializationFailed = 3;
    private const int StateFinalized = 4;

    // ==================================================================================================
    //  LOG MESSAGE TEMPLATES
    //  ------------------------------------------------------------------------------------------------
    //  Declared as constants so that every logging call site passes a compile time constant template,
    //  which is what the structured logging analyzer requires and what keeps the emitted event shape
    //  stable for a log consumer. The values interpolated into them are passed as arguments and named,
    //  never concatenated into the template.
    //
    //  EVERY CALL SITE IS GUARDED BY ILogger.IsEnabled, WHICH IS THE ANALYZER'S OWN PRESCRIBED SHAPE.
    //  Each template takes at least one argument that the logging API receives as object, so a value type
    //  argument is converted and a rendered one is computed, whether or not the level is enabled. The SDK
    //  analyzer set reports exactly that as a diagnostic, and the guard is the remedy it prescribes; the
    //  alternative, a source generated logging partial, is deliberately not used because this refactor
    //  references no source generator anywhere. NOTHING OBSERVABLE CHANGES: a record is still written for
    //  every enabled level, and no performance property is claimed here or anywhere - the repository
    //  publishes none. The guard is uniform across all eleven call sites so that a reader never has to
    //  work out why one of them is shaped differently from its neighbour.
    // ==================================================================================================

    private const string InitializeAttemptMessage =
        "Initializing PowerFramework: capability mask {CapabilityMask} (0x{CapabilityMaskHex}), " +
        "enabled capabilities [{EnabledCapabilities}].";

    private const string UnrecognizedCapabilityBitsMessage =
        "Configured PowerFramework capability mask {CapabilityMask} (0x{CapabilityMaskHex}) carries bits " +
        "{UnrecognizedBits} (0x{UnrecognizedBitsHex}) that no INIT_FLAG_ENABLE_ constant declares. The " +
        "mask is passed through unchanged and unvalidated, which is the legacy behaviour; this entry is " +
        "diagnostic only and rejects nothing.";

    private const string InitializeSucceededMessage =
        "PowerFramework initialized: return code {ReturnCode} ({ReturnCodeName}), capability mask " +
        "{CapabilityMask} (0x{CapabilityMaskHex}), framework version {FrameworkVersion}.";

    private const string InitializeFailedMessage =
        "PowerFramework initialization FAILED: return code {ReturnCode} ({ReturnCodeName}), capability " +
        "mask {CapabilityMask} (0x{CapabilityMaskHex}). Initialization is mandatory, so the host will " +
        "not be started.";

    private const string InitializeIndeterminateMessage =
        "PowerFramework initialization DID NOT SUCCEED: return code {ReturnCode} ({ReturnCodeName}) is " +
        "neither a success nor a failure under the preserved return code algebra, for capability mask " +
        "{CapabilityMask} (0x{CapabilityMaskHex}). An unproven initialization is fatal, so the host will " +
        "not be started.";

    private const string InitializeThrewMessage =
        "PowerFramework initialization FAILED: the framework lifecycle boundary threw for capability " +
        "mask {CapabilityMask} (0x{CapabilityMaskHex}). Initialization is mandatory, so the host will " +
        "not be started.";

    private const string FinalizeSucceededMessage =
        "PowerFramework finalized: return code {ReturnCode} ({ReturnCodeName}).";

    private const string FinalizeFailedMessage =
        "PowerFramework finalization FAILED: return code {ReturnCode} ({ReturnCodeName}). Shutdown " +
        "continues regardless.";

    private const string FinalizeIndeterminateMessage =
        "PowerFramework finalization DID NOT SUCCEED: return code {ReturnCode} ({ReturnCodeName}) is " +
        "neither a success nor a failure under the preserved return code algebra. Shutdown continues " +
        "regardless.";

    private const string FinalizeThrewMessage =
        "PowerFramework finalization threw. Shutdown continues regardless.";

    private const string FinalizeSkippedMessage =
        "PowerFramework finalization skipped: lifecycle state is {LifecycleState}, so no successful " +
        "initialization is outstanding. Finalization must be paired with initialization and is " +
        "deliberately not performed here.";

    private readonly IFrameworkRuntime _runtime;
    private readonly IOptions<GatewayOptions> _options;
    private readonly ILogger<FrameworkInitializer> _logger;

    private int _lifecycleState = StateNotInitialized;
    private uint _effectiveMask;

    /// <summary>
    /// Initializes a new instance over the framework lifecycle boundary, the bound Gateway configuration
    /// and a logger.
    /// </summary>
    /// <param name="runtime">
    /// The framework lifecycle boundary. <see cref="ManagedFrameworkRuntime"/> is the implementation the
    /// composition root registers; a test supplies a substitute so that the success, failure and
    /// indeterminate paths are all reachable without a native library.
    /// </param>
    /// <param name="options">
    /// The bound Gateway configuration supplying the capability mask. Deliberately
    /// <see cref="IOptions{TOptions}"/> rather than <see cref="IOptionsMonitor{TOptions}"/>: the mask is
    /// consumed exactly once, at startup, and a mid process change has no meaning because the legacy has
    /// no re-initialize. <see cref="IOptions{TOptions}.Value"/> is read inside
    /// <see cref="StartingAsync(CancellationToken)"/> rather than in this constructor, so that a
    /// configuration validation failure surfaces during host startup rather than during container
    /// resolution.
    /// </param>
    /// <param name="logger">The logger the lifecycle diagnostics are written to.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public FrameworkInitializer(
        IFrameworkRuntime runtime,
        IOptions<GatewayOptions> options,
        ILogger<FrameworkInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _runtime = runtime;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Whether the framework is currently initialized, i.e. an initialization has succeeded and the
    /// matching finalization has not yet run.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> before initialization is attempted, <see langword="false"/> when it did not
    /// succeed, and <see langword="false"/> again after finalization - so it answers "is finalization
    /// owed", which is exactly the pairing obligation <c>docs/README.md:L15</c> describes. Use
    /// <see cref="IsFinalized"/> to distinguish the third case from the first two.
    /// </remarks>
    public bool IsInitialized => Volatile.Read(ref _lifecycleState) == StateInitialized;

    /// <summary>
    /// Whether the framework has been finalized, which happens at most once and only after an
    /// initialization that succeeded.
    /// </summary>
    public bool IsFinalized => Volatile.Read(ref _lifecycleState) == StateFinalized;

    /// <summary>
    /// The capability gate that was resolved from configuration and handed to the framework lifecycle
    /// boundary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recorded as soon as it is resolved and therefore readable whatever the outcome, which is what makes
    /// the mask available to a diagnostic after a failure as well as after a success. Before
    /// initialization has been attempted this is <see cref="CapabilityFlags.None"/>, which is
    /// indistinguishable from a configured mask of zero; <see cref="IsInitialized"/> and
    /// <see cref="IsFinalized"/> disambiguate. The value is <see cref="uint"/> wide throughout, matching
    /// the <c>readonly unsignedlong</c> parameter at
    /// <c>ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L8</c>.
    /// </para>
    /// <para>
    /// Unless configuration overrides <c>Gateway:CapabilityFlags</c>, this resolves to
    /// <see cref="Enums.INIT_FLAG_ENABLE_ALL"/> - the value <c>3847</c>, and NOT <c>3855</c>, because
    /// <see cref="Enums.INIT_FLAG_ENABLE_BLINKFAST"/> is deliberately excluded from that composite at
    /// <c>ws_objects/pfw.shared.pbl.src/enums.sru:L49</c>. That is the same mask
    /// <c>ws_objects/pfw.pbl.src/pfw.sra:L91</c> passes, so the default behaviour of this port is the
    /// legacy behaviour.
    /// </para>
    /// </remarks>
    public CapabilityFlags Capabilities => new(Volatile.Read(ref _effectiveMask));

    /// <summary>
    /// Returns the running framework version, as reported by the lifecycle boundary.
    /// </summary>
    /// <returns>The version string. Never <see langword="null"/>.</returns>
    /// <remarks>
    /// The managed counterpart of <c>global function string pfwversion()</c> at
    /// <c>ws_objects/pfw.base.pbl.src/pfwversion.srf:L7</c>, carried for API parity: its only legacy
    /// callers are windows in the permanently out of scope demos library, so there is no in scope call
    /// site to point at. It is a straight passthrough to <see cref="IFrameworkRuntime.Version()"/> and is
    /// safe to call at any point in the lifecycle, including before initialization, because the boundary's
    /// version does not depend on lifecycle state. The legacy version string from the repository's stale
    /// changelog is never returned; see <see cref="ManagedFrameworkRuntime.Version()"/>.
    /// </remarks>
    public string Version()
    {
        return _runtime.Version();
    }


    // ==================================================================================================
    //  STARTING - THE MANAGED pfwInitialize, AT THE VERY BEGINNING OF STARTUP
    // ==================================================================================================

    /// <summary>
    /// Initializes the framework before any hosted service is started, and prevents the host from
    /// starting if that does not succeed.
    /// </summary>
    /// <param name="cancellationToken">
    /// The host's startup cancellation token. Observed BEFORE anything is claimed, so a start that is
    /// already cancelled creates no pairing obligation.
    /// </param>
    /// <returns>A completed task when initialization succeeded.</returns>
    /// <exception cref="FrameworkInitializationException">
    /// Initialization did not succeed, whether by returning a failure code, by returning a code the
    /// preserved algebra classifies as neither success nor failure, or by throwing. THE HOST MUST NOT
    /// START in any of those cases.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Initialization has already been attempted on this instance, which means the host wiring registered
    /// it more than once. That is a structural fault, and <c>docs/README.md:L15</c> requires exactly one
    /// initialization per finalization.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was already cancelled, so startup is being abandoned.
    /// </exception>
    /// <remarks>
    /// This hook, rather than <see cref="StartAsync(CancellationToken)"/>, is what makes "prevent host
    /// start" literal: the host runs it for every hosted service BEFORE it starts any of them, so the web
    /// server has not bound a port and no endpoint exists to answer when it throws. It is the managed
    /// position of <c>pfwInitialize</c> at the very beginning of the application open event
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L91</c>, <c>docs/README.md:L11</c>].
    /// </remarks>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        // Observed FIRST, before the state is claimed. A host whose startup is already cancelled must not
        // acquire a finalization obligation on the way out; claiming the state and then abandoning it
        // would leave this instance in Initializing forever, from which there is deliberately no
        // transition.
        cancellationToken.ThrowIfCancellationRequested();

        int previousState = Interlocked.CompareExchange(
            ref _lifecycleState,
            StateInitializing,
            StateNotInitialized);

        if (previousState != StateNotInitialized)
        {
            // A structural fault in the host wiring rather than a runtime condition: the framework has no
            // re-initialize, and pairing is one to one [docs/README.md:L15]. Failing here is consistent
            // with the fail fast posture - a host wired to initialize twice is misconfigured, and starting
            // it anyway would leave the pairing ambiguous.
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PowerFramework initialization has already been attempted on this instance " +
                    $"(lifecycle state {DescribeState(previousState)}). Initialization must happen exactly " +
                    $"once and be paired with exactly one finalization, so register this type as a hosted " +
                    $"service exactly once."));
        }

        // The capability mask, resolved from configuration through the gate type. Reading Value here
        // rather than in the constructor is what puts an options validation failure on the startup path,
        // where it is also fatal, instead of on the container resolution path.
        //
        // WIDTH: the SIGNED TO UNSIGNED crossing happens exactly once, inside CapabilityFlags.FromOptions.
        // The configured property is declared C# long because the catalogue constants mirror PowerScript
        // Constant Long, a 32-bit SIGNED type [enums.sru:L41-L48]; the boundary parameter is
        // readonly unsignedlong, 32 bits UNSIGNED [pfwinitialize.srf:L8]. FromOptions is that type's
        // documented single adaptation point and performs the conversion explicitly and unchecked, which
        // reproduces the legacy handover - the legacy checks nothing either. From here on the mask is uint,
        // and nothing widens it again: no long and no ulong appears on the path to the boundary argument
        // below, and this file re-crosses nothing.
        GatewayOptions options = _options.Value;
        CapabilityFlags capabilities = CapabilityFlags.FromOptions(options);
        uint effectiveMask = capabilities.EffectiveMask;

        // Published before the outcome is known, so the mask is readable from a diagnostic whether
        // initialization goes on to succeed or to fail.
        Volatile.Write(ref _effectiveMask, effectiveMask);

        string effectiveMaskHex = FormatMaskAsHex(effectiveMask);

        if (capabilities.HasUnrecognizedBits)
        {
            // REPORTED, NEVER REJECTED. The native entry point validates nothing about the mask it
            // receives, so neither does this port: the value passes through unchanged. Bit positions 4
            // through 7 are unassigned in the legacy layout [enums.sru:L41-L48 jumps from 8 to 256] and
            // anything above 2048 is undeclared, so an operator who sets one is almost certainly looking
            // at a typo - but turning that into a rejection would be new behaviour.
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    UnrecognizedCapabilityBitsMessage,
                    effectiveMask,
                    effectiveMaskHex,
                    capabilities.UnrecognizedBits,
                    FormatMaskAsHex(capabilities.UnrecognizedBits));
            }
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                InitializeAttemptMessage,
                effectiveMask,
                effectiveMaskHex,
                string.Join(", ", capabilities.EnabledCapabilityNames));
        }

        long returnCode;

        try
        {
            // ==========================================================================================
            //  THE ONE DELIBERATE DEPARTURE FROM THE LEGACY, ANNOTATED WHERE IT HAPPENS
            //  ----------------------------------------------------------------------------------------
            //  ws_objects/pfw.pbl.src/pfw.sra:L91 reads exactly `pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)`
            //  and DISCARDS the return code: no assignment, no comparison, no guard. This port assigns it
            //  and checks it.
            //
            //  That is a reconciliation of two legacy sources that contradict each other, not an
            //  unrequested improvement. docs/README.md:L3 states initialization has been MANDATORY since
            //  pfw 2.0 and that the framework cannot be used without it, and docs/README.md:L30 warns that
            //  requesting a module whose native library is absent makes initialization FAIL. A framework
            //  that is unusable after a failed initialization cannot be run on regardless, so the
            //  documentation is the stronger authority on intent and the call site's laxity is the weaker
            //  signal.
            //
            //  THIS REASONING EXTENDS TO NOTHING ELSE. Every other legacy quirk in scope is reproduced
            //  verbatim, including the tri-state return code algebra applied to this very result below.
            // ==========================================================================================
            returnCode = _runtime.Initialize(effectiveMask);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The boundary faulted instead of returning a code. Still fatal, and recorded with the same
            // structured fields as a returned failure so a log consumer sees one shape for both. A
            // cancellation is excluded from the filter deliberately: that is the host abandoning startup,
            // not the framework failing, and it propagates unwrapped.
            Volatile.Write(ref _lifecycleState, StateInitializationFailed);

            if (_logger.IsEnabled(LogLevel.Critical))
            {
                _logger.LogCritical(exception, InitializeThrewMessage, effectiveMask, effectiveMaskHex);
            }

            throw new FrameworkInitializationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PowerFramework initialization FAILED: the framework lifecycle boundary threw " +
                    $"{exception.GetType().FullName} for capability mask {effectiveMask} " +
                    $"(0x{effectiveMaskHex}). Initialization is mandatory and a failed initialization " +
                    $"leaves the framework unusable, so the host is deliberately not started."),
                returnCode: null,
                requestedCapabilities: effectiveMask,
                innerException: exception);
        }

        string? returnCodeName = Formatting.FormatRetCode(returnCode);

        // READING 1 OF 3 - SUCCESS.
        // Predicates.IsSucceeded tests >= RetCode.OK, so RetCode.PREVENT (1) reads as a SUCCESS and
        // initialization proceeds. That is the preserved algebra and it is deliberately NOT special cased:
        // narrowing this test to == RetCode.OK would "correct" the legacy, which this refactor forbids.
        if (Predicates.IsSucceeded(returnCode))
        {
            // Published only here, which is what makes finalization owed only after a SUCCESSFUL
            // initialization.
            Volatile.Write(ref _lifecycleState, StateInitialized);

            // Read as its own statement rather than inline in the log call, so that it is visibly part of
            // the success path. This is the port's only consumption of the version accessor, and it is why
            // that accessor is on the seam at all - see IFrameworkRuntime.Version().
            string frameworkVersion = _runtime.Version();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    InitializeSucceededMessage,
                    returnCode,
                    returnCodeName,
                    effectiveMask,
                    effectiveMaskHex,
                    frameworkVersion);
            }

            return Task.CompletedTask;
        }

        // Both remaining readings are fatal, so the terminal state is set once, here, before they are
        // told apart. Nothing transitions out of it: a process that could not initialize is on its way
        // out, and finalization is now permanently not owed.
        Volatile.Write(ref _lifecycleState, StateInitializationFailed);

        // READING 2 OF 3 - DEFINITE FAILURE.
        if (Predicates.IsFailed(returnCode))
        {
            if (_logger.IsEnabled(LogLevel.Critical))
            {
                _logger.LogCritical(
                    InitializeFailedMessage,
                    returnCode,
                    returnCodeName,
                    effectiveMask,
                    effectiveMaskHex);
            }

            throw new FrameworkInitializationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PowerFramework initialization FAILED: the framework lifecycle boundary returned " +
                    $"{returnCode} ({returnCodeName}) for capability mask {effectiveMask} " +
                    $"(0x{effectiveMaskHex}). Initialization is mandatory and a requested module whose " +
                    $"native library is absent makes it fail, so the framework is unusable and the host " +
                    $"is deliberately not started."),
                returnCode,
                effectiveMask,
                innerException: null);
        }

        // READING 3 OF 3 - NEITHER SUCCEEDED NOR FAILED: THE TRI-STATE HOLE, HANDLED ON PURPOSE.
        // This branch is reached for RetCode.CANCELLED, for its alias RetCode.CANCELED which shares the
        // same value, and for a null code - Predicates.IsSucceeded answers false and Predicates.IsFailed
        // answers false for all of them, because IsFailed excludes CANCELLED explicitly and both
        // predicates answer false for null. It is a distinct branch rather than an else on the failure
        // test so that the third state cannot be reached by accident, and its diagnostic says something
        // different from a definite failure: the boundary reported something the algebra cannot classify,
        // as opposed to reporting a fault.
        //
        // THE DECISION: treated as FATAL. docs/README.md:L3 makes a usable framework conditional on a
        // completed initialization, so the absence of a positive success signal leaves that precondition
        // unproven, and continuing on an unproven precondition is exactly the graceful degradation the
        // fail fast posture exists to prevent. A cancelled initialization is not an initialization.
        if (_logger.IsEnabled(LogLevel.Critical))
        {
            _logger.LogCritical(
                InitializeIndeterminateMessage,
                returnCode,
                returnCodeName,
                effectiveMask,
                effectiveMaskHex);
        }

        throw new FrameworkInitializationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"PowerFramework initialization DID NOT SUCCEED: the framework lifecycle boundary " +
                $"returned {returnCode} ({returnCodeName}) for capability mask {effectiveMask} " +
                $"(0x{effectiveMaskHex}), which the preserved return code algebra classifies as NEITHER " +
                $"succeeded nor failed. An initialization whose success is not positively signalled " +
                $"leaves the framework's usability precondition unproven, which is fatal, so the host is " +
                $"deliberately not started."),
            returnCode,
            effectiveMask,
            innerException: null);
    }

    // ==================================================================================================
    //  STOPPED - THE MANAGED pfwFinalize, AT THE VERY END OF SHUTDOWN
    // ==================================================================================================

    /// <summary>
    /// Releases the framework after every hosted service has stopped, exactly once, and only when an
    /// initialization succeeded.
    /// </summary>
    /// <param name="cancellationToken">
    /// The host's shutdown cancellation token. Deliberately NOT observed - see the remarks.
    /// </param>
    /// <returns>A completed task. This method never throws and never faults its task.</returns>
    /// <remarks>
    /// <para>
    /// The managed position of <c>pfwFinalize</c> at the very end of the application close event
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L108</c>, <c>docs/README.md:L12</c>], and the inverse of
    /// <see cref="StartingAsync(CancellationToken)"/> in the same way that
    /// <c>ws_objects/pfw.base.pbl.src/n_initializer.sru:L28-L31</c> is the inverse of <c>:L23-L26</c>.
    /// </para>
    /// <para>
    /// THE PAIRING IS DECIDED BY THE SAME ATOMIC OPERATION THAT PERFORMS IT. The transition from
    /// initialized to finalized is a single compare and exchange, so finalization happens exactly once,
    /// only after an initialization that SUCCEEDED, and never when initialization failed or was never
    /// attempted. Both halves of the warning at <c>docs/README.md:L15</c> are therefore structural rather
    /// than conventional.
    /// </para>
    /// <para>
    /// IT NEVER THROWS, AND IT NEVER OBSERVES CANCELLATION. A finalization fault during shutdown must not
    /// mask an in flight shutdown, replace whatever else is unwinding with itself, or escape the shutdown
    /// path, so every outcome is logged and none is rethrown. The cancellation token is not observed for
    /// the same reason: a shutdown that is already cancelled still owes the framework its finalization,
    /// and abandoning it would break the pairing the legacy documentation requires.
    /// </para>
    /// </remarks>
    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        int previousState = Interlocked.CompareExchange(
            ref _lifecycleState,
            StateFinalized,
            StateInitialized);

        if (previousState != StateInitialized)
        {
            // Not owed: initialization never happened, did not succeed, or finalization already ran. This
            // is the half of the pairing requirement that says finalize must NOT run, and it is why the
            // pairing is not expressed as a finally block - a finally would fire on exactly this path.
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(FinalizeSkippedMessage, DescribeState(previousState));
            }

            return Task.CompletedTask;
        }

        long returnCode;

        try
        {
            returnCode = _runtime.Finalize();
        }
        catch (Exception exception)
        {
            // Every exception is caught here, including cancellation, because nothing may escape the
            // shutdown path. The state has already moved to Finalized, so the pairing is complete either
            // way and no retry is attempted - the legacy has no re-finalize.
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(exception, FinalizeThrewMessage);
            }

            return Task.CompletedTask;
        }

        string? returnCodeName = Formatting.FormatRetCode(returnCode);

        // The same three readings as initialization, and the same reason for keeping the third distinct -
        // but here every one of them merely selects a log level, because shutdown continues regardless.
        if (Predicates.IsSucceeded(returnCode))
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(FinalizeSucceededMessage, returnCode, returnCodeName);
            }
        }
        else if (Predicates.IsFailed(returnCode))
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(FinalizeFailedMessage, returnCode, returnCodeName);
            }
        }
        else
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(FinalizeIndeterminateMessage, returnCode, returnCodeName);
            }
        }

        return Task.CompletedTask;
    }

    // ==================================================================================================
    //  THE FOUR REMAINING LIFECYCLE HOOKS, DELIBERATELY EMPTY
    //  ------------------------------------------------------------------------------------------------
    //  IHostedLifecycleService declares six hooks and the legacy lifecycle occupies exactly two positions:
    //  the very beginning of startup and the very end of shutdown [docs/README.md:L11-L12]. The other four
    //  are implemented as completed tasks because there is nothing at those positions to reproduce, and
    //  each says why rather than being left to look unfinished. None of them is a placeholder for work to
    //  be added later: putting the framework lifecycle at any of these positions would be WRONG, not
    //  merely incomplete.
    // ==================================================================================================

    /// <summary>
    /// Does nothing. Required by <see cref="IHostedService"/>.
    /// </summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// Hosted services are started in registration order, and the web host builder registers the web
    /// server before any application service, so by the time this ran the server would already be
    /// listening. Initializing here would therefore admit requests to an un-initialized framework, which
    /// is the opposite of the requirement at <c>docs/README.md:L11</c>. The initialization lives in
    /// <see cref="StartingAsync(CancellationToken)"/>, which runs before ANY hosted service starts.
    /// </remarks>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Does nothing. Required by <see cref="IHostedLifecycleService"/>.
    /// </summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// This hook runs after every hosted service has started, which is too late for an initialization that
    /// belongs at the very beginning of startup, and a failure here could not prevent the host from
    /// starting because it already has.
    /// </remarks>
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Does nothing. Required by <see cref="IHostedLifecycleService"/>.
    /// </summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// This hook runs before any hosted service stops, so finalizing here would release the framework
    /// while services that may still be using it are running. <c>docs/README.md:L12</c> places
    /// finalization at the VERY END of shutdown, which is <see cref="StoppedAsync(CancellationToken)"/>.
    /// </remarks>
    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Does nothing. Required by <see cref="IHostedService"/>.
    /// </summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// Hosted services are stopped in reverse registration order, so other services may still be stopping
    /// when this runs. Finalizing here would be earlier than the very end of shutdown that
    /// <c>docs/README.md:L12</c> requires.
    /// </remarks>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    // ==================================================================================================
    //  DIAGNOSTIC FORMATTING HELPERS
    // ==================================================================================================

    /// <summary>
    /// Renders a lifecycle state constant as its name, for a diagnostic message.
    /// </summary>
    /// <param name="lifecycleState">One of the private lifecycle state constants.</param>
    /// <returns>
    /// The state's name, or its invariant decimal rendering for a value outside the declared set, so that
    /// the helper is total and reports whatever it was given rather than inventing a label for it.
    /// </returns>
    private static string DescribeState(int lifecycleState)
    {
        return lifecycleState switch
        {
            StateNotInitialized => "NotInitialized",
            StateInitializing => "Initializing",
            StateInitialized => "Initialized",
            StateInitializationFailed => "InitializationFailed",
            StateFinalized => "Finalized",
            _ => lifecycleState.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Renders a 32-bit capability mask as eight uppercase hexadecimal digits, without a prefix.
    /// </summary>
    /// <param name="mask">The mask to render.</param>
    /// <returns>Exactly eight hexadecimal digits, culture invariantly.</returns>
    /// <remarks>
    /// A mask is read bit by bit, so its hexadecimal form is the useful one, while configuration states it
    /// in decimal - both appear in every diagnostic for that reason. The width is fixed at eight digits
    /// because the boundary parameter is 32 bits wide
    /// [<c>ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L8</c>], and the invariant culture is passed
    /// explicitly so the rendering cannot shift with the ambient culture.
    /// </remarks>
    private static string FormatMaskAsHex(uint mask)
    {
        return mask.ToString("X8", CultureInfo.InvariantCulture);
    }
}

