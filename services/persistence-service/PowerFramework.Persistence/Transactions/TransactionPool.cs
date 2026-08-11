// ==================================================================================================
//  TransactionPool.cs - the reference-counted transaction pool, with BOTH legacy clocks seamed
//  ------------------------------------------------------------------------------------------------
//  PORTED FROM   ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru  (240 lines, read in
//                    full) - the pool itself: its shared entry structure [:L10-L15], its three
//                    settings and the 30000 ms constant [:L53-L59], the idle hook [:L73], the
//                    initialisation order [:L76-L83], and all seven public members
//                    [:L86-L226] plus the destructor [:L238].
//                ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru  (545 lines) - the
//                    pooled transaction's connection lifecycle: the database-type discriminators
//                    [:L60-L61], the connection-OK hook [:L107-L109], connect [:L111-L143],
//                    disconnect [:L145-L161], the STATE-PRESERVING rollback [:L163-L183], rollback
//                    [:L185-L191], the liveness probe [:L193-L218], command execution
//                    [:L220-L238], commit [:L240-L257], the SQLCode predicates [:L337-L341], the
//                    descriptor transfer [:L343-L354], the dialect resolver [:L356-L361], state
//                    clearing [:L363-L368], the autocommit checkpoint [:L370-L381], the
//                    parameterless commit [:L383-L384], the STATE-PRESERVING clean disconnect
//                    [:L504-L524], and the broken flag with its side-effecting query
//                    [:L526-L534].
//                ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L113-L214 - the
//                    CONSUMER, which establishes the call convention: RemoveRef on a descriptor
//                    change [:L122], Release with a ref out-parameter [:L141], AddRef then Get
//                    [:L165, :L168], Exists [:L213], the pool's own initialisation being fired by
//                    the task layer [:L204], and - decisively - the TASK driving the liveness test
//                    and the connect [:L173-L179].
//                ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs - the descriptor, consumed
//                    from Transactions/TransactionData.cs and deliberately not duplicated here.
//                ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs - the structured error, consumed
//                    from Errors/DbErrorData.cs and deliberately not duplicated here.
//                ws_objects/pfw.shared.pbl.src/retcode.sru - the return codes, consumed from
//                    PowerFramework.Shared.Kernel and never redeclared. Values verified directly:
//                    CANCELED -2 [:L44], E_INVALID_ARGUMENT -3 [:L46], E_INVALID_OBJECT -5 [:L48],
//                    E_INVALID_TRANSACTION -7 [:L50], E_OUT_OF_BOUND -12 [:L55], E_DB_ERROR -28
//                    [:L71].
//                docs/PB多线程绕坑提示.md - the legacy threading-hazard note, both items.
//
//  ORACLE STATUS Every ws_objects/** path named above is READ ONLY (constraint C-C). Each was read
//                as specification and is cited by locator; nothing here copies, reformats, moves,
//                edits or deletes any of them, and nothing in this file depends on the PowerBuilder
//                toolchain, the PowerBuilder runtime or any shipped native binary. The legacy tree
//                is the only statement of intended behaviour that exists for this component, which
//                is why every behavioural claim below carries the :L line it came from.
//
//  ============================ THE FOUR THINGS A READER GETS WRONG ============================
//  Each of the four is a legacy behaviour that LOOKS like a defect, is not, and would be
//  "corrected" by anybody who did not read the oracle. Each is reproduced and annotated at the
//  point of reproduction as well as here, because a comment 900 lines away from the code does not
//  stop a well-meaning edit.
//
//  1. Release AND RemoveRef ARE NOT TWO SPELLINGS OF ONE OPERATION.
//     RemoveRef DECREMENTS and may destroy [:L91, :L94-L115]. Release does NOT decrement at all,
//     tests the count for EXACTLY 1 rather than at-most-1, disconnects the CALLER'S object on that
//     one boundary, clears its state and nulls the caller's handle [:L120-L132]. A count of 0 or 2
//     therefore disconnects NOTHING in Release, and the count is unchanged on return. The consumer
//     proves the two are distinct intents: it calls Release when handing an object back
//     [n_cst_thread_task_sqlbase.sru:L141] and RemoveRef when the descriptor itself changed
//     [:L122].
//
//  2. THE POOL NEVER OPENS A CONNECTION. All 240 lines were read and there is no of_Connect call
//     anywhere in the object. The pool creates, reuses, applies the descriptor, disconnects and
//     destroys; the TASK layer drives connection [n_cst_thread_task_sqlbase.sru:L173-L179]. This
//     single fact is what keeps constraint C-E cheap to honour here, and it is stated in the XML
//     documentation of Get as well, because a reader will expect a pool to connect.
//
//  3. IsBroken IS A QUERY WITH A SIDE EFFECT. `if Not _bBroken then Event OnCheck()` [:L530-L534]:
//     the check hook can flip the flag, so inspecting an entry can TRANSITION it to broken. The
//     pool calls it in two hot paths - RemoveRef [:L96] and Get [:L159] - as though it were free.
//     Both call sites carry the annotation.
//
//  4. THE DISCONNECT GUARD IS INCONSISTENT ACROSS THE THREE DESTROY PATHS, DELIBERATELY.
//     RemoveRef disconnects ONLY when the object is not broken [:L105-L107]. RemoveAll [:L197] and
//     Collect [:L217] disconnect UNCONDITIONALLY on any valid object. Harmonising the three would
//     change which objects see a disconnect attempt, so all three are reproduced as measured.
//  ============================================================================================
//
//  ================================ TWO CLOCKS, ONE INJECTED SEAM ==============================
//  There are TWO distinct legacy clock readers in this component and BOTH are observable, so both
//  must be substitutable. They are seamed through the SAME injected TimeProvider - the one
//  Program.cs registers as a singleton and documents as the determinism seam - and no bespoke clock
//  abstraction is invented alongside it.
//
//    CLOCK 1 - THE POOL'S IDLE EXPIRY.  `CPU()` at of_removeref:L97 stamps the idle start, and
//              of_collect:L215 compares `CPU() - idleStartTime >= _nKeepAliveExpireTime`. The
//              comparison is GREATER-OR-EQUAL, not strictly greater: a delta exactly equal to the
//              expiry collects. Zero is the "not idle" sentinel, written by of_addref:L149.
//
//    CLOCK 2 - THE TRANSACTION'S CONNECTION-LIVENESS CACHE.  n_cst_thread_trans.sru:L198 reads
//              `if CPU() - _nLastConnOK < 10000 then return true`. The threshold is 10,000
//              milliseconds and the operator is STRICTLY LESS THAN, so a delta of exactly 10,000
//              re-probes rather than short-circuiting. The stored tick is set by the connection-OK
//              hook [:L107], REFRESHED on a successful probe [:L212] and ZEROED on a failed one
//              [:L214], and zero is the "never connected OK" sentinel tested at [:L196].
//
//  WHY BOTH ARE SEAMED RATHER THAN JUST THE FIRST. AAP 0.6.7 requires that non-deterministic values
//  be masked from BOTH the golden master and the candidate recording - that is the Golden-Master
//  technique's own prerequisite, not a local convention - so a characterization comparison of any
//  workflow that touches the pool is only valid if EVERY clock read in the path can be substituted.
//  A seam on the pool alone would leave the liveness cache reading a real clock, and the same
//  workflow replayed ten seconds later would take a different arm at n_cst_thread_trans.sru:L198.
//
//  A WALL-CLOCK READ ANYWHERE IN THIS FILE IS A DEFECT. There is no DateTime.UtcNow, no
//  DateTime.Now, no Environment.TickCount and no Stopwatch here, and none may be added. Both clocks
//  read CurrentTicks()/ReadTicks(), which are the only two clock readers in the file and both call
//  the injected provider.
//
//  AND BOTH READ MONOTONIC ELAPSED TIME, NOT A WALL-CLOCK INSTANT. Both readers difference
//  TimeProvider.GetTimestamp() against an origin captured at construction, through
//  TimeProvider.GetElapsedTime. The distinction is load-bearing rather than stylistic: `CPU()` is a
//  process-relative counter that cannot move backwards, whereas a UTC instant CAN - an NTP
//  correction, a manual clock change or a daylight-saving transition on a badly configured host all
//  step it - and every comparison here is a DELTA against a stored stamp. Under a backward step an
//  elapsed idle interval reads as negative and an entry never expires; under a forward step a
//  transaction that has been idle for a second reads as expired and is collected under its caller,
//  and the liveness cache short-circuits past its window. Neither is a behaviour the oracle has,
//  because `CPU()` cannot produce either. A constant offset is added to each reading so a stamp can
//  never collide with the zero "not idle"/"never connected OK" sentinel; see MonotonicOffset.
//  ============================================================================================
//
//  ================================= THE TWO WIDTH DECISIONS ==================================
//  PowerBuilder's `unsignedlong` is a 32-BIT unsigned integer, so its faithful C# counterpart is
//  `uint` rather than `ulong`. shared/PowerFramework.Shared.Kernel/Bits.cs settled the identical
//  width question the identical way; the AAP 0.4.5.2 type table's `ulong` row is width-inaccurate on
//  this specific point and is not followed. Two fields of the legacy entry structure carry that
//  type [:L13-L14] and they are decided SEPARATELY, because the consequences differ.
//
//  DECISION A - THE REFERENCE COUNT IS `uint`, AND ITS DECREMENT SATURATES AT ZERO.
//    The hazard, stated plainly: decrementing an unsigned value that is already zero WRAPS to
//    4294967295 rather than going negative. The legacy's release test is `if refCount <= 0`
//    [:L94], and on an unsigned field that test can only ever be true at exactly zero - so under a
//    wrapping decrement the release arm becomes UNREACHABLE for an over-released entry and the
//    pooled connection is retained for the life of the process. In a PowerBuilder desktop session
//    an unbalanced release was a bounded programming error; in a long-lived container reached by
//    concurrent gRPC callers it is an unbounded retention.
//
//    WHAT THE ORACLE ACTUALLY SPECIFIES: nothing. PowerScript's behaviour when `--` takes an
//    unsigned field below zero is not documented anywhere in this repository and is not observable
//    through any framework API, so it cannot be settled from the oracle. That is the same class of
//    question the sibling TransactionPoolOptions.ResolveKeepAliveExpireMilliseconds faced over
//    double-to-unsigned conversion, and it is resolved the same way and for the same stated reason:
//    where the legacy is UNSPECIFIED rather than specified, this port picks the specified reading
//    and documents it as the authoritative contract.
//
//    THE CHOICE, MADE VISIBLY (the agent brief requires the choice be visible and tested, and
//    forbids a silent clamp): the decrement SATURATES. A count already at zero stays at zero, no
//    wrap occurs, and the entry takes exactly the arm the legacy's `<= 0` spelling was written to
//    take. That spelling is itself the strongest available evidence of intent - `<= 0` on an
//    unsigned field is only meaningful if the author was thinking "has reached zero" - and the
//    alternative would make an over-release leak silently. The rejected alternative is named here
//    rather than omitted, and DecrementRefCount carries the same note at the point of
//    reproduction. TransactionPoolTests pins the decrement-at-zero case explicitly.
//
//  DECISION B - THE IDLE START TICK IS `long`, NOT `uint`, AND THAT WIDENING IS UNOBSERVABLE.
//    The legacy field is `unsignedlong` [:L14] holding a `CPU()` reading, which is 32-bit and
//    therefore wraps after roughly 49.7 days of accumulated CPU TIME - years of wall-clock time for
//    any realistic process. Reproducing that wrap could only ever change a verdict on a delta the
//    legacy cannot actually reach, while introducing a spurious "not expired" answer on the one
//    that straddles the wrap. Widening cannot change any verdict the legacy genuinely produces, so
//    it is taken. The ZERO SENTINEL is preserved exactly: 0 means "not idle" [:L149].
//
//    ONE HONEST CONSEQUENCE OF THE SENTINEL, RECORDED RATHER THAN HIDDEN. The tick is epoch-
//    relative where CPU() is process-relative. Only DELTAS and the zero sentinel are ever read, so
//    the different origin is unobservable - but it does mean a clock positioned at exactly the Unix
//    epoch would read as "not idle". No deployment reaches that instant; a hand-driven fake clock
//    could, so the tests position their fake at a non-zero instant deliberately.
//  ============================================================================================
//
//  =================================== CONSTRAINT RULINGS =====================================
//  C-B  NO BEHAVIOUR IMPROVEMENTS. Every arm of every member below is contract. Reproduced AND
//       annotated, each citing its own locator: the exactly-1 Release test with no decrement
//       [:L123]; the non-positive expiry fallback CONSUMED from TransactionPoolOptions rather than
//       re-derived [:L78-L79]; the retain-not-destroy keep-alive arm [:L95-L99]; the
//       greater-or-equal expiry comparison [:L215]; the not-broken-guarded disconnect in RemoveRef
//       [:L105] against the UNGUARDED disconnect in RemoveAll [:L197] and Collect [:L217]; the
//       descriptor applied only to a NEWLY CREATED object [:L171]; AddRef returning an INDEX rather
//       than a return code [:L151]; the state-preserving rollback [n_cst_thread_trans.sru:L163-L183]
//       and clean disconnect [:L504-L524]; rollback under auto-commit returning FAILED [:L185];
//       SQLCode 100 reading as SUCCESS [:L233, :L340]; and IsBroken's side effect [:L530-L534].
//       Nothing is added that the legacy lacks: there is no eviction policy, no maximum pool size,
//       no connection-acquisition timeout, no wait queue, no health-check schedule and no metric,
//       however natural each would look on a type called a pool. Each would be a NEW capability
//       rather than a port.
//
//  C-C  THE LEGACY TREE IS READ ONLY AND IS THE ORACLE. Discharged above under ORACLE STATUS.
//
//  C-D  NOTHING FROM A DEFERRED SERVICE. The class-name activation below uses BCL type activation
//       ONLY. There is no script-bridge and no dynamic-invocation shim: AAP 0.2.1.4 establishes
//       that the legacy's n_scriptinvoker usage is a variadic-call escape hatch with no analogue to
//       port, and ScriptBridge is a deferred service that must not be touched even partially. No
//       XML, no JSON, no HTTP, no FTP, no WebSocket, no MQTT, no UI, no theming, no popup and no
//       dialog appears here or is reachable from here. Every failure surfaces as a return code
//       plus, where a caller needs detail, a DbErrorData - never as a message box.
//
//  C-E  SQLITE ONLY, NO FABRICATED DATABASE. NO SQL SERVER OR ORACLE CONNECTION IS EVER OPENED,
//       even though a descriptor can carry either engine's DBMS string and GetDbType can answer
//       DbtOracle. That answer only selects a paging STRING TRANSFORM owned by Sql/Paging/. This
//       file composes NO connection string for any engine and references no provider: the database
//       verbs arrive through ITransactionEngine, whose SQLite implementation belongs to
//       Data/SqliteConnectionFactory.cs, and the pool reaches a real connection only through the
//       injected IPooledTransaction abstraction.
//
//  C-F  THE PRIMARY SECRETS CONSTRAINT. No credential, key, token, password, connection string,
//       certificate or secret-shaped placeholder appears anywhere in this file, in any comment,
//       default, literal or example. NO DESCRIPTOR FIELD IS EVER LOGGED. The pool takes no logging
//       dependency at all, so there is no logging expression here to leak one from; where a
//       diagnostic is unavoidable it names the one-based INDEX and the REFERENCE COUNT and nothing
//       else - never the descriptor, never a field of it, and never ToString() of it. LogPass in
//       particular cannot appear even by accident: TransactionData deliberately publishes no getter
//       for it, so the only way to read it is the named RevealLogPassForConnect(), which this file
//       never calls. Two neighbours are covered by the same rule for the reason recorded on
//       TransactionData itself: DbParm and UserParm are credential-CAPABLE.
//
//       ISqlRedactor IS DELIBERATELY NOT A DEPENDENCY OF THIS FILE, and the reason is that the rule
//       it enforces has nothing to redact here. No statement text reaches any diagnostic path in
//       this component: the structured error this file produces is
//       DbErrorData.FromTransaction(sqlDbCode, sqlErrText), which sets SqlSyntax to the EMPTY STRING
//       by construction, mirroring the consumer that copies exactly those two values and no third
//       [n_cst_thread_task_sqlbase.sru:L175-L176]. Adding the failing statement to that payload would
//       be inventing a legacy behaviour that does not exist (C-B), so the redactor has no call site
//       to occupy. Any FUTURE path here that does carry statement text must take ISqlRedactor as a
//       constructor dependency and pass through it; the interface is named in this file's dependency
//       set precisely so that obligation is discoverable rather than rediscovered.
//
//  C-H  80% LINE COVERAGE PER SERVICE. Everything the pool decides is coverable with NO REAL
//       DATABASE: the pooled transaction sits behind IPooledTransaction so a fake substitutes for
//       it, the clock is the injected TimeProvider so a hand-rolled fake drives every expiry
//       boundary, and the pool's own body performs no I/O of any kind. The database verbs sit one
//       further seam down behind ITransactionEngine, so even the connection-semantics matrix runs
//       against a fake. PowerFramework.Persistence.csproj grants InternalsVisibleTo to
//       PowerFramework.Persistence.Tests, which is what makes these internal seams reachable.
//
//  C-K  DOCUMENT EVERY TECHNOLOGY- AND BOUNDARY-SPECIFIC DECISION. The two clocks and why both are
//       seamed, the exactly-1 Release asymmetry, the two width decisions, the fact that the pool
//       never connects, and the DisableBind relevance are all above or on the member concerned. The
//       DisableBind point, since it reaches this file through the descriptor: `DisableBind=1` in
//       DbParm means the runtime does NOT use bind variables and interpolates literals instead,
//       which AAP 0.6.4 identifies as the mechanical root of the SQL-injection exposure. This file
//       neither reads nor honours that flag - TransactionData.ResolveDbParmFlags derives it and the
//       task layer acts on it - but the flag TRAVELS through here inside the pool key, so a
//       descriptor difference in DbParm alone produces a DIFFERENT pool entry with a different
//       binding posture. That is why whole-descriptor value equality is the pool key and must not be
//       narrowed to a "connection target" subset: narrowing it would silently share one pooled
//       connection between a bound and an unbound configuration.
//
//  FAIL FAST, NEVER GRACEFUL DEGRADATION (AAP 0.1.4, 0.6.7). A structurally invalid configuration
//  fails at construction, which for a singleton is startup. What is NOT a structural fault is
//  stated with equal force on the member concerned: a non-positive configured expiry is LEGAL and
//  means "use 30000 ms", so validating it away would replace a preserved behaviour with a
//  validation failure the legacy never had.
//
//  .editorconfig SCOPE - NO SCREAMING_SNAKE CONSTANT IS DECLARED HERE. This folder sits OUTSIDE
//  every Band 3 naming-suppression section, none of which names this file. The repository-root
//  .editorconfig is the SOLE ROSTER of those sections and no count is restated here, because a number
//  copied into a source header is a second place for the roster to be wrong and it silently became
//  wrong as files were added. Warnings are errors, so a preserved-spelling constant here would break
//  the build. In
//  particular KEEPALIVE_EXPIRE is NOT declared: its value is consumed from
//  TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds. DbtMssql and DbtOracle come from the
//  generated PowerFramework.Contracts.Persistence.V1.DatabaseType, and every return code comes from
//  PowerFramework.Shared.Kernel.RetCode. Nothing in this file redeclares any of them.
//
//  SINGLETON AND THREAD SAFE. Program.cs registers the pool as a singleton - a scoped pool would
//  defeat its purpose - so concurrent gRPC calls reach AddRef, Get, RemoveRef, Release and Collect
//  simultaneously. The entry collection is guarded by ONE lock, documented on the field. THE LOCK IS
//  NEVER HELD ACROSS AN I/O CALL: every Disconnect and every Dispose is performed after the lock is
//  released, from a list of pending disposals collected while it was held. The pool does NOT
//  self-register in dependency injection and uses no service locator; the wiring obligation is
//  written out on TransactionPool itself. The caller-side/worker-side duality that
//  Tasks/TaskProxies/ preserves is NOT flattened here - AAP 0.4.5.4 states the thread-affinity
//  annotations are contract, not commentary - and this file makes no attempt to schedule, marshal or
//  thread-hop anything: it is a synchronous, affinity-neutral registry, and both legacy threading
//  hazards (docs/PB多线程绕坑提示.md items 1 and 2) are structurally unreachable because it returns
//  no string or blob across a thread boundary and holds no main-thread object.
//
//  RULES POSITION. review_rules returns exactly one line, "No user rules provided.", so NO
//  user-specified rule governs this file. That is a finding, not latitude: nothing is invented or
//  back-filled from convention in its place. The enterprise-standard baseline applies instead -
//  nullable reference types on, warnings as errors, no secret in source, deterministic and
//  trivially testable - and the binding constraints are the refactor plan's own non-rule inventory,
//  of which C-B, C-C, C-D, C-E, C-F, C-H and C-K bite on this file and are each discharged at the
//  point they are cited above.
//
//  NO PERFORMANCE PROPERTY IS ASSERTED and no decision here is justified by one: the repository
//  publishes no latency budget, no throughput target and no availability commitment, so there is no
//  baseline against which such a claim could be made. The collection is a plain list because the
//  legacy artifact is a plain array [:L55] and the legacy lookups are linear scans [:L137, :L183] -
//  not because a measurement said so. OrderedMap from PowerFramework.Shared.Containers is
//  deliberately NOT used: that type is the n_map substitute for the SQL task layer, and this
//  structure is an array with one-based indices handed back to callers.
// ==================================================================================================

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Microsoft.Extensions.Options;

using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Errors;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Persistence.Transactions;

// --------------------------------------------------------------------------------------------------
//  PART 1 OF 6 - THE FIVE-VALUE SQL STATE
// --------------------------------------------------------------------------------------------------

/// <summary>
/// The five PowerBuilder transaction-state values, captured as one immutable value.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS TYPE EXISTS SO THE FIVE VALUES CANNOT DRIFT APART, AND THAT IS ITS WHOLE JOB.</b> The
/// legacy declares the identical five-value save-and-restore sequence TWICE, in two private
/// subroutines that must stay byte-identical to each other: the state-preserving rollback
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L163-L183</c>] and the
/// state-preserving clean disconnect [<c>:L504-L524</c>]. A third copy of the same five names
/// appears in the clearing routine [<c>:L363-L368</c>]. Three hand-written copies of one tuple is
/// three chances for a future edit to add a value to one and not the others, so the tuple is
/// factored into this single type and every one of the three sites goes through it.
/// </para>
/// <para>
/// The five, in the oracle's own order [<c>:L166-L170</c>]: <c>SQLCode</c>, <c>SQLDBCode</c>,
/// <c>SQLNRows</c>, <c>SQLErrText</c>, <c>SQLReturnData</c>. The order is preserved for the same
/// reason <c>TransactionData</c> preserves its own field order - it is the order a reader will
/// compare against the oracle - even though nothing here is positional on a wire.
/// </para>
/// <para>
/// <b>The struct's all-bits-zero default IS the legacy cleared state</b>, which is why
/// <see cref="Cleared"/> is simply <see langword="default"/>. The legacy clearing routine assigns
/// <c>0</c>, <c>0</c>, <c>0</c>, <c>""</c> and <c>""</c> [<c>:L363-L367</c>]; the two string members
/// project <see langword="null"/> to <see cref="string.Empty"/> on read, so a defaulted instance
/// observes exactly those five values. That projection is not cosmetic: under the repository's
/// nullable context a plain non-nullable auto-property on a struct produces no warning yet still
/// hands a null reference out of <see langword="default"/>.
/// </para>
/// <para>
/// C-F: no member of this type is a credential and none is ever a descriptor field.
/// <see cref="SqlErrText"/> carries provider message text, and <see cref="SqlReturnData"/> carries
/// provider return text; NEITHER is redacted here, because neither is a statement and the redaction
/// obligation attaches to statement text (see the ISqlRedactor ruling in this file's header). A
/// caller that surfaces either across the network boundary owns that decision at the mapping layer,
/// which is <c>Errors/SqlRedactor.cs</c> together with <c>Grpc/</c>.
/// </para>
/// </remarks>
/// <summary>
/// One value bound into a statement, carried BESIDE the statement text rather than inside it.
/// </summary>
/// <param name="Name">
/// The provider-side placeholder name, including its prefix - <c>@p1</c>, <c>@p2</c> and so on, ONE
/// BASED because a legacy retrieval argument is positional and PowerBuilder counts positions from one. It is
/// generated by the binder and is never caller text, so it cannot itself carry an injection.
/// </param>
/// <param name="Value">
/// The value, TYPED AND UNFORMATTED. <see langword="null"/> travels as a database null. Nothing here is
/// ever rendered into statement text; rendering belongs to the parity projection alone.
/// </param>
/// <remarks>
/// <b>THIS TYPE IS THE FIX FOR THE PARAMETERIZATION GAP, AND IT EXISTS BECAUSE THE LEGACY HAS NO
/// EQUIVALENT.</b> The oracle interpolates every value into the statement whenever <c>DisableBind=1</c>
/// appears in <c>DBParm</c> [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128</c>],
/// which AAP §0.6.4 identifies as the mechanical root of the injection exposure. AAP §0.7.2 resolves it
/// as "parameterized SQL in the implementation even where the legacy interpolates, with the observable
/// generated statement preserved", and this parameter list is the "in the implementation" half: the
/// interpolated text still travels for parity and recording, and these values are what actually reach
/// the driver.
/// </remarks>
internal readonly record struct SqlBoundParameter(string Name, object? Value);

/// <summary>
/// A statement in BOTH of its forms: the text the legacy would have generated, and the parameterized
/// text plus values that are actually executed.
/// </summary>
/// <param name="ObservableText">
/// The statement exactly as the oracle renders it, values interpolated as literals. <b>This is the
/// parity artifact</b>: it is what a characterization recording compares, what
/// <c>dberrordata.sqlsyntax</c> would have carried, and what a caller reading a SQL preview observes.
/// It is NEVER what gets executed when <see cref="Parameters"/> is non-empty.
/// </param>
/// <param name="ParameterizedText">
/// The same statement with every bound value replaced by its generated placeholder. Equal to
/// <paramref name="ObservableText"/> when there was nothing to bind, which is why a caller may execute
/// this member unconditionally.
/// </param>
/// <param name="Parameters">The bound values, in placeholder order. Empty when nothing was bound.</param>
/// <param name="CacheStatement">
/// <para>
/// Whether the caller asked for this statement to be RETAINED IN PREPARED FORM for re-execution - the
/// port of the leading-<c>@</c> execution mode the SQLite binding's own <c>Exec</c> carries
/// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L397-L398</c>], whose comment records that the
/// prefix caches the statement to speed up re-parsing on a later execution.
/// </para>
/// <para>
/// <b>A REQUEST, NOT A GUARANTEE, AND NEVER A PERFORMANCE CLAIM.</b> An engine that keeps no prepared
/// form simply executes the statement immediately, which is the same OBSERVABLE outcome - the mode
/// changes how many times the provider parses the text and nothing else. The repository publishes no
/// latency budget anywhere (AAP §0.8.5), so no member of this type asserts one; what IS asserted is
/// that the mode is CARRIED rather than dropped, because a caller who sent the documented prefix must
/// not have their statement rejected.
/// </para>
/// <para>
/// <b>The prefix itself never reaches this member's siblings.</b> It is a mode selector rather than
/// SQL, so <c>Tasks/SqlCommandTask</c> removes it from the statement before either text is composed -
/// which is why an error payload, a SQL-preview hook and a log record all see the statement the
/// provider actually ran.
/// </para>
/// </param>
/// <remarks>
/// <para>
/// <b>TWO TEXTS, ONE MEANING, AND THE DISTINCTION IS A SECURITY BOUNDARY.</b> The two differ only in
/// how the values arrive at the engine, so the observable RESULT is identical - which is precisely the
/// condition AAP §0.1.5 places on an implementation being allowed to be safer than the legacy: "the
/// implementation may be safer than the legacy where the change is unobservable". Executing
/// <see cref="ObservableText"/> is therefore never necessary and never correct when parameters exist.
/// </para>
/// <para>
/// <b>C-F.</b> <see cref="ObservableText"/> may contain live row values, so it carries the same
/// disclosure obligation as <c>dberrordata.sqlsyntax</c>: it must reach a log record or a wire field
/// only through <c>Errors/ISqlRedactor</c>.
/// </para>
/// </remarks>
internal sealed record SqlBoundStatement(
    string ObservableText,
    string ParameterizedText,
    IReadOnlyList<SqlBoundParameter> Parameters,
    bool CacheStatement = false)
{
    /// <summary>
    /// A statement with nothing bound, whose two texts are therefore the same text.
    /// </summary>
    /// <param name="text">The statement.</param>
    /// <returns>A statement carrying no parameters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Defaults <see cref="CacheStatement"/> to <see langword="false"/>, which is the ordinary mode: a
    /// caller that wants the retained form selects it with <c>with { CacheStatement = true }</c> rather
    /// than through a second factory, so the two modes cannot drift apart in their construction.
    /// </remarks>
    internal static SqlBoundStatement Unbound(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SqlBoundStatement(text, text, []);
    }
}

internal readonly record struct SqlState
{
    private readonly string? _sqlErrText;
    private readonly string? _sqlReturnData;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlState"/> struct from the five values.
    /// </summary>
    /// <param name="sqlCode">The coarse outcome code. See <see cref="SqlCode"/>.</param>
    /// <param name="sqlDbCode">The provider's numeric code.</param>
    /// <param name="sqlNRows">The affected or returned row count.</param>
    /// <param name="sqlErrText">The provider's message text; <see langword="null"/> reads as empty.</param>
    /// <param name="sqlReturnData">The provider's return text; <see langword="null"/> reads as empty.</param>
    public SqlState(long sqlCode, long sqlDbCode, long sqlNRows, string? sqlErrText, string? sqlReturnData)
    {
        SqlCode = sqlCode;
        SqlDbCode = sqlDbCode;
        SqlNRows = sqlNRows;
        _sqlErrText = sqlErrText;
        _sqlReturnData = sqlReturnData;
    }

    /// <summary>
    /// The coarse outcome code, mirroring PowerBuilder's <c>SQLCode</c>.
    /// </summary>
    /// <value>
    /// <para>
    /// <b>ITS THREE-VALUE ALPHABET IS NOT THE RETURN-CODE ALGEBRA AND MUST NEVER BE CONFLATED WITH
    /// IT.</b> <c>0</c> is success, <c>-1</c> is an error, and <c>100</c> is "no data found" - and
    /// <c>100</c> READS AS A SUCCESS both in the command path, which tests
    /// <c>SQLCode &lt;&gt; 0 and SQLCode &lt;&gt; 100</c> [<c>:L233</c>], and in the transaction's own
    /// success predicate, which tests <c>SQLCode &gt;= 0</c> [<c>:L340</c>].
    /// </para>
    /// <para>
    /// Use <see cref="IPooledTransaction.IsSqlSucceeded"/> and
    /// <see cref="IPooledTransaction.IsSqlFailed"/> to ask about this value, NOT
    /// <c>Predicates.IsSucceeded</c> / <c>Predicates.IsFailed</c> from the shared kernel. The two
    /// families answer differently on the same input: the kernel's predicates implement the legacy
    /// RETURN-CODE algebra in which a prevention (1) reads as a success and cancelled (-2) is neither
    /// succeeded nor failed, whereas this family is a plain sign test over a provider code.
    /// </para>
    /// </value>
    public long SqlCode { get; init; }

    /// <summary>The provider's own numeric code, mirroring PowerBuilder's <c>SQLDBCode</c>.</summary>
    /// <value>
    /// Copied verbatim into <c>DbErrorData.SqlDbCode</c> by the consumer
    /// [<c>n_cst_thread_task_sqlbase.sru:L175</c>], which is why <see cref="IPooledTransaction.CaptureError"/>
    /// reads it rather than <see cref="SqlCode"/>.
    /// </value>
    public long SqlDbCode { get; init; }

    /// <summary>The affected or returned row count, mirroring PowerBuilder's <c>SQLNRows</c>.</summary>
    /// <value>
    /// <b>The liveness probe judges its verdict on THIS value and not on <see cref="SqlCode"/></b>:
    /// <c>bConnected = (SQLNRows &gt; 0)</c> [<c>:L207</c>]. That is a real distinction - a probe that
    /// returned no rows without raising an error would read as NOT connected.
    /// </value>
    public long SqlNRows { get; init; }

    /// <summary>The provider's message text, mirroring PowerBuilder's <c>SQLErrText</c>.</summary>
    /// <value>Never <see langword="null"/>; the cleared state is <see cref="string.Empty"/> [<c>:L366</c>].</value>
    public string SqlErrText
    {
        get => _sqlErrText ?? string.Empty;
        init => _sqlErrText = value;
    }

    /// <summary>The provider's return text, mirroring PowerBuilder's <c>SQLReturnData</c>.</summary>
    /// <value>Never <see langword="null"/>; the cleared state is <see cref="string.Empty"/> [<c>:L367</c>].</value>
    public string SqlReturnData
    {
        get => _sqlReturnData ?? string.Empty;
        init => _sqlReturnData = value;
    }

    /// <summary>
    /// The cleared state - the exact five values the legacy clearing routine assigns
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L363-L368</c>].
    /// </summary>
    /// <value>Zero, zero, zero, empty and empty.</value>
    public static SqlState Cleared => default;

    /// <summary>
    /// The successful state a database primitive reports when it completed without a provider error.
    /// </summary>
    /// <param name="rowCount">
    /// The affected or returned row count to publish as <see cref="SqlNRows"/>. Defaults to zero,
    /// which is what the legacy leaves behind for a verb that reports no row count.
    /// </param>
    /// <returns>A state whose <see cref="SqlCode"/> is <c>0</c> and whose text members are empty.</returns>
    /// <remarks>
    /// A convenience for <see cref="ITransactionEngine"/> implementations and for test doubles, so
    /// neither has to remember that success is spelled <c>SQLCode = 0</c> [<c>:L129</c>].
    /// </remarks>
    public static SqlState Succeeded(long rowCount = 0) =>
        new(0, 0, rowCount, string.Empty, string.Empty);

    /// <summary>
    /// The failed state a database primitive reports when the provider raised an error.
    /// </summary>
    /// <param name="sqlDbCode">The provider's numeric code.</param>
    /// <param name="sqlErrText">The provider's message text.</param>
    /// <returns>
    /// A state whose <see cref="SqlCode"/> is <c>-1</c>, carrying the two provider values.
    /// </returns>
    /// <remarks>
    /// <c>-1</c> rather than any other negative value, because that is PowerBuilder's own error code
    /// and because the transaction's failure predicate is the sign test <c>SQLCode &lt; 0</c>
    /// [<c>:L337</c>], which every negative value would satisfy - so the specific value is chosen to
    /// match the oracle rather than because the predicate needs it.
    /// </remarks>
    public static SqlState Failed(long sqlDbCode, string? sqlErrText) =>
        new(-1, sqlDbCode, 0, sqlErrText ?? string.Empty, string.Empty);
}

// --------------------------------------------------------------------------------------------------
//  PART 2 OF 6 - THE DATABASE-VERB SEAM AND THE HOOK SURFACE
// --------------------------------------------------------------------------------------------------

/// <summary>
/// A STABLE handle onto one pooled transaction entry, safe to hold across calls.
/// </summary>
/// <param name="Id">
/// The monotonic identity the pool issued. Never reused, so a handle that outlives its entry resolves
/// NOTHING rather than resolving a later entry that happened to take the same position.
/// </param>
/// <remarks>
/// <para>
/// <b>WHY THIS TYPE EXISTS, STATED ONCE.</b> The pool's own addressing is a ONE-BASED POSITION and its
/// removal REBUILDS the array, renumbering every later index
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L114</c>]. That is a preserved legacy
/// defect, not a bug: the legacy's consumer zeroes its stored index the instant it releases
/// [<c>n_cst_thread_task_sqlbase.sru:L123</c>], so a legacy holder never carried a stale index into a
/// renumbering. Decomposition changed exactly one thing - a session or a task now HOLDS its reference
/// across separate requests - and that is enough to turn an unreachable defect into a reachable
/// cross-request one, because another holder's release renumbers this holder's index.
/// </para>
/// <para>
/// <b>IT IS A TRANSLATION, NOT A REPLACEMENT.</b> The positional API and its renumbering are untouched and
/// remain the oracle's behaviour; this handle resolves to a position and then calls it. Nothing about the
/// legacy's observable pooling changes.
/// </para>
/// </remarks>
internal readonly record struct PoolLease(long Id)
{
    /// <summary>
    /// A handle that names nothing, which is what a holder starts with and returns to after releasing.
    /// </summary>
    /// <remarks>
    /// The default value is deliberately the invalid one, so a field that was never assigned cannot be
    /// mistaken for a live reference - the same reason the legacy's own index sentinel is zero.
    /// </remarks>
    internal static PoolLease None => default;

    /// <summary>Whether this handle names an entry at all.</summary>
    /// <remarks>
    /// The port of the legacy's <c>_nTransRefIndex &gt; 0</c> test, which appears at every one of its call
    /// sites [<c>n_cst_thread_task_sqlbase.sru:L122, :L153, :L164</c>].
    /// </remarks>
    internal bool IsValid => Id > 0L;
}

/// <summary>
/// One statement on its way to the engine, carrying the CANONICAL text with its placeholders intact,
/// the ordered parameter VALUES to bind, and the RENDERED text the legacy would have interpolated.
/// </summary>
/// <param name="CanonicalText">
/// The text the provider actually executes. Its placeholders are named <c>@p1</c>, <c>@p2</c> and so
/// on, in the legacy's own one-based positional order, so binding is positional exactly as
/// PowerBuilder's <c>?</c> markers are.
/// </param>
/// <param name="RenderedText">
/// <para>
/// THE PARITY ARTEFACT, AND IT IS NOT WHAT RUNS. The legacy interpolates literal values into the
/// statement whenever the connection disabled bind variables, and AAP 0.6.4 requires the OBSERVABLE
/// generated statement to match the oracle byte for byte while the implementation is free to be safer.
/// This member is that observable text: it is what the SQL-preview interception reports and what a
/// characterization recording compares, and it reaches a log only through
/// <c>Errors/ISqlRedactor</c>.
/// </para>
/// <para>
/// Equal to <see cref="CanonicalText"/> when there is nothing to bind, which is the ordinary case for
/// every statement the framework itself composes.
/// </para>
/// </param>
/// <param name="Parameters">
/// The values to bind, in one-based legacy order. Empty when the statement carries no placeholder.
/// </param>
/// <param name="CacheStatement">
/// <para>
/// Whether the engine is asked to RETAIN this statement in prepared form so a later execution of the
/// same text does not re-parse it - the port of the leading-<c>@</c> execution mode
/// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L397-L398</c>]. Carried on the command rather
/// than on the engine because the mode is a property of the REQUEST, exactly as it is in the legacy
/// where it is selected by the statement text a caller passes to one call.
/// </para>
/// <para>
/// <b>AN IMPLEMENTATION MAY IGNORE IT AND STILL BE CORRECT.</b> Honouring it changes how often the
/// provider parses the text and nothing a caller can observe in the RESULT, so an engine with no
/// prepared-form store executes immediately and reports the same outcome. That is why it defaults to
/// <see langword="false"/> and why no test double has to grow a cache to stay valid.
/// </para>
/// </param>
/// <remarks>
/// <b>WHY THE ENGINE SEAM CARRIES THIS RATHER THAN A BARE STRING.</b> A seam that accepts only rendered
/// text cannot represent a provider parameter at all, so every value would have to be spliced into the
/// statement before it crossed the boundary - which is the mechanical root of the injection exposure
/// AAP 0.6.4 analyses. Carrying the canonical text and the values side by side lets the implementation
/// bind while the rendered text still travels for parity, so neither obligation is traded for the
/// other.
/// </remarks>
internal readonly record struct SqlCommandText(
    string CanonicalText,
    string RenderedText,
    IReadOnlyList<object?> Parameters,
    bool CacheStatement = false)
{
    /// <summary>
    /// Wraps a statement that has nothing to bind, so the legacy single-string call shape keeps working
    /// unchanged.
    /// </summary>
    /// <param name="statement">The statement text.</param>
    /// <returns>A command whose canonical and rendered texts are the same and which binds nothing.</returns>
    internal static SqlCommandText FromRenderedStatement(string statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        return new SqlCommandText(statement, statement, []);
    }

    /// <summary>
    /// Creates a bound command from a canonical statement, its rendered parity text, and its values.
    /// </summary>
    /// <param name="canonicalText">The statement the provider executes, with <c>@pN</c> placeholders.</param>
    /// <param name="renderedText">The interpolated text the oracle would have produced.</param>
    /// <param name="parameters">The values to bind, in one-based legacy order.</param>
    /// <param name="cacheStatement">
    /// Whether the engine is asked to retain the prepared form. See <see cref="CacheStatement"/>;
    /// defaulted so every existing call site keeps the ordinary immediate mode.
    /// </param>
    /// <returns>The command.</returns>
    internal static SqlCommandText FromBoundStatement(
        string canonicalText,
        string renderedText,
        IReadOnlyList<object?> parameters,
        bool cacheStatement = false)
    {
        ArgumentNullException.ThrowIfNull(canonicalText);
        ArgumentNullException.ThrowIfNull(renderedText);
        ArgumentNullException.ThrowIfNull(parameters);

        return new SqlCommandText(canonicalText, renderedText, parameters, cacheStatement);
    }

    /// <summary>
    /// The placeholder spelling for a one-based parameter position.
    /// </summary>
    /// <param name="oneBasedPosition">The one-based position.</param>
    /// <returns>The placeholder name.</returns>
    internal static string PlaceholderFor(int oneBasedPosition) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"@p{oneBasedPosition}");

    /// <summary>
    /// Binds this command's values onto a provider command.
    /// </summary>
    /// <param name="command">The provider command whose text is already set.</param>
    /// <remarks>
    /// Typed rather than stringified: the value is handed to the provider as it stands, so the provider
    /// decides its wire representation. A null becomes <see cref="DBNull"/>, which is the only
    /// translation performed.
    /// </remarks>
    internal void BindTo(System.Data.Common.DbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        IReadOnlyList<object?> values = Parameters;

        for (int index = 0; index < values.Count; index++)
        {
            System.Data.Common.DbParameter parameter = command.CreateParameter();

            parameter.ParameterName = PlaceholderFor(index + 1);
            parameter.Value = values[index] ?? DBNull.Value;

            _ = command.Parameters.Add(parameter);
        }
    }
}

/// <summary>
/// The five database verbs a pooled transaction needs, plus the three connection properties it reads.
/// This is the port of the PowerBuilder runtime's embedded-SQL statements, not of any framework
/// object.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY THIS SEAM EXISTS AT ALL.</b> The legacy transaction object inherits from PowerBuilder's
/// built-in <c>transaction</c> type, so <c>CONNECT USING this</c>, <c>DISCONNECT USING this</c>,
/// <c>COMMIT USING this</c>, <c>ROLLBACK USING this</c> and <c>EXECUTE IMMEDIATE ... USING this</c>
/// are RUNTIME STATEMENTS with no method to call
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L116, :L127, :L151, :L174, :L229,
/// :L245</c>]. .NET has no equivalent language construct, so the five verbs have to arrive from
/// somewhere. They arrive here, behind one narrow interface, and everything ELSE the legacy object
/// does - hook ordering, the five-value snapshot and restore, the broken flag, the liveness cache,
/// the return-code mapping - stays in <see cref="PooledTransaction"/> where it can be verified
/// without a database.
/// </para>
/// <para>
/// <b>C-E RULING, WHICH IS THE REASON THE SEAM IS NARROW RATHER THAN CONVENIENT.</b> No
/// implementation of this interface is declared in this file and none may be. Composing a connection
/// string is <c>Data/SqliteConnectionFactory.cs</c>'s job - it owns the legacy URI grammar,
/// <c>mode=rwc</c> with the optional <c>check</c> and <c>journal</c> extensions - and SQLite is the
/// only engine this phase provisions. NO SQL SERVER OR ORACLE CONNECTION IS EVER OPENED, even though
/// <see cref="Dbms"/> can name either and <see cref="IPooledTransaction.GetDbType"/> can answer
/// <see cref="DatabaseType.DbtOracle"/>: that answer only selects a paging STRING TRANSFORM owned by
/// <c>Sql/Paging/</c>. This interface therefore takes the descriptor and hands back state; it exposes
/// no provider type, no connection object and no connection string.
/// </para>
/// <para>
/// <b>THE VERBS REPORT THROUGH THEIR RETURN VALUE, NOT THROUGH AN EXCEPTION.</b> PowerBuilder's
/// embedded SQL does not throw; it writes the five state values and lets the caller test
/// <c>SQLCode</c>. Reproducing that is what makes every arm of <see cref="PooledTransaction"/>
/// reachable, so an implementation MUST translate a provider exception into
/// <see cref="SqlState.Failed"/> rather than letting it escape. An implementation that throws is not
/// wrong so much as untestable against the oracle: the legacy has no arm for it.
/// </para>
/// <para>
/// <b>THREAD AFFINITY IS THE IMPLEMENTATION'S CONTRACT, NOT THIS INTERFACE'S.</b> AAP 0.4.5.4 states
/// the legacy thread-affinity annotations are contract rather than commentary, and the legacy encodes
/// them per SQL class rather than on the transaction. Nothing here schedules, marshals or thread-hops,
/// and the pool calls these verbs only outside its own lock, so an implementation is free to be
/// worker-thread-affine exactly as <c>Tasks/</c> requires.
/// </para>
/// </remarks>
internal interface ITransactionEngine : IDisposable
{
    /// <summary>
    /// The open connection handle, or <c>0</c> when nothing is open.
    /// </summary>
    /// <value>
    /// The port of <c>DBHandle()</c>, which the legacy tests in two places with two DIFFERENT
    /// operators: <c>DBHandle() &lt;&gt; 0</c> before a connect [<c>:L115</c>] and
    /// <c>DBHandle() &lt;= 0</c> at the head of a disconnect [<c>:L145</c>]. Both spellings are
    /// reproduced verbatim rather than normalised, because they only coincide while the handle is
    /// non-negative and normalising them would silently decide a case the oracle left open. An
    /// implementation should publish <c>0</c> for "closed" and any non-zero value for "open"; the
    /// value itself is never interpreted.
    /// </value>
    int DbHandle { get; }

    /// <summary>
    /// The DBMS identifier currently in force, as the descriptor supplied it.
    /// </summary>
    /// <value>
    /// Read for exactly two purposes, both substring tests on its upper-cased text: the dialect
    /// resolver [<c>:L356</c>] and the liveness probe's statement choice [<c>:L202</c>]. Never
    /// <see langword="null"/>; an implementation with no descriptor applied yet publishes
    /// <see cref="string.Empty"/>, which classifies as <see cref="DatabaseType.DbtMssql"/> exactly as
    /// the legacy's unmatched arm does [<c>:L359</c>].
    /// </value>
    string Dbms { get; }

    /// <summary>
    /// Whether the connection is in auto-commit mode.
    /// </summary>
    /// <value>
    /// <para>
    /// The port of the legacy transaction object's <c>AutoCommit</c> property, which gates three
    /// members: rollback returns <see cref="RetCode.FAILED"/> when it is set [<c>:L185</c>], commit
    /// does the same [<c>:L240</c>], and the autocommit checkpoint branches on it twice
    /// [<c>:L371, :L376</c>].
    /// </para>
    /// <para>
    /// <b>SETTABLE, AND DELIBERATELY NOT WRITTEN BY THE DESCRIPTOR TRANSFER.</b> The legacy
    /// descriptor carries an <c>autocommit</c> field [<c>transactiondata.srs:L11</c>] and
    /// <c>of_settransdata</c> pointedly does NOT assign it - it moves seven fields and that is not one
    /// of them [<c>:L345-L351</c>]. The setter exists because the command task toggles it around a
    /// statement to reproduce the <c>AC_NATIVE</c> arm, which belongs to <c>Tasks/</c>.
    /// </para>
    /// </value>
    bool AutoCommit { get; set; }

    /// <summary>
    /// Applies the descriptor's seven connection fields to the connection target.
    /// </summary>
    /// <param name="descriptor">
    /// The descriptor whose connection fields are to be adopted. Passed <see langword="in"/> because
    /// the legacy parameter is <c>readonly</c> [<c>:L343</c>].
    /// </param>
    /// <remarks>
    /// <para>
    /// SEVEN FIELDS, NOT NINE: <c>DBMS</c>, <c>ServerName</c>, <c>Database</c>, <c>LogID</c>,
    /// <c>LogPass</c>, <c>DBParm</c> and <c>Lock</c> [<c>:L345-L351</c>]. <c>AutoCommit</c> and
    /// <c>UserParm</c> are untouched by the legacy transfer and must stay untouched here. The
    /// canonical way to perform the fold is
    /// <c>TransactionData.WithConnectionFieldsFrom(in descriptor)</c>, which already encodes exactly
    /// those seven and is the reason this file does not restate them.
    /// </para>
    /// <para>
    /// <b>C-F:</b> the credential reaches an implementation through this call and through no other.
    /// It may be CONSUMED to open a connection and must be observable nowhere else - not in a log,
    /// not in an exception message, not in a diagnostic, not in a property. <c>LogPass</c> publishes
    /// no getter at all, so the only way to read it is the named
    /// <c>TransactionData.RevealLogPassForConnect()</c>, and an implementation should call that once,
    /// at the point of connection, and never store the result.
    /// </para>
    /// <para>
    /// <b>Applying a descriptor does NOT connect.</b> It configures the target. The legacy transfer
    /// contains no <c>CONNECT</c> [<c>:L343-L354</c>], and neither may an implementation of this
    /// method.
    /// </para>
    /// </remarks>
    void ApplyConnectionFields(in TransactionData descriptor);

    /// <summary>Opens the connection. The port of <c>CONNECT USING this</c> [<c>:L127</c>].</summary>
    /// <returns>The five state values the statement left behind.</returns>
    SqlState Connect(CancellationToken cancellationToken = default);

    /// <summary>Closes the connection. The port of <c>DISCONNECT USING this</c> [<c>:L151</c>].</summary>
    /// <returns>The five state values the statement left behind.</returns>
    SqlState Disconnect();

    /// <summary>Commits. The port of <c>COMMIT USING this</c> [<c>:L245</c>].</summary>
    /// <returns>The five state values the statement left behind.</returns>
    SqlState Commit();

    /// <summary>Rolls back. The port of <c>ROLLBACK USING this</c> [<c>:L174</c>].</summary>
    /// <returns>The five state values the statement left behind.</returns>
    SqlState Rollback();

    /// <summary>
    /// Executes a statement. The port of <c>EXECUTE IMMEDIATE :sqlCmd USING this</c> [<c>:L229</c>].
    /// </summary>
    /// <param name="sqlCommand">
    /// The statement text. Never <see langword="null"/> and never empty: the caller rejects both with
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> before reaching here [<c>:L220</c>].
    /// </param>
    /// <returns>The five state values the statement left behind.</returns>
    /// <remarks>
    /// <b>AAP 0.6.4 APPLIES TO EVERY IMPLEMENTATION OF THIS METHOD.</b> The legacy interpolates
    /// literals whenever <c>DisableBind=1</c> appears in <c>DBParm</c>, and that is the mechanical
    /// root of the SQL-injection exposure the plan analyses. An implementation must use a
    /// PARAMETERIZED command internally while preserving the observable generated statement, and must
    /// not echo the statement text into any diagnostic without passing it through
    /// <c>Errors/ISqlRedactor</c>. This method is the only place in the transaction surface that
    /// accepts caller-authored SQL, so it is the only place that obligation attaches.
    /// </remarks>
    SqlState Execute(string sqlCommand, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a statement with PROVIDER-BOUND parameters. The obligation AAP 0.6.4 places on
    /// <see cref="Execute(string)"/> is discharged structurally here rather than by review.
    /// </summary>
    /// <param name="command">
    /// The canonical statement, its ordered values, and the rendered parity text. Never carries a null
    /// or empty canonical text: the caller rejects both with
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> before reaching here [<c>:L220</c>].
    /// </param>
    /// <returns>The five state values the statement left behind.</returns>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE MEMBER AN IMPLEMENTATION SHOULD DO THE WORK IN.</b> A default implementation is
    /// deliberately NOT provided, because the one obvious default - render the parameters into the text
    /// and call the single-string overload - is exactly the splice this member exists to eliminate, and
    /// providing it would let an implementation opt back into the exposure by omission.
    /// </para>
    /// <para>
    /// <see cref="SqlCommandText.RenderedText"/> must never reach a diagnostic without passing through
    /// <c>Errors/ISqlRedactor</c>: it is the interpolated form and therefore the literal-dense one.
    /// </para>
    /// <para>
    /// <b><see cref="SqlCommandText.CacheStatement"/> IS A REQUEST AN IMPLEMENTATION MAY DECLINE, AND
    /// DECLINING IT IS NOT A DEFECT.</b> It is the port of the leading-<c>@</c> execution mode
    /// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L397-L398</c>], and honouring it changes how
    /// often the provider parses the text rather than anything a caller observes in the outcome - so an
    /// implementation with no prepared-form store executes immediately and reports the same
    /// <see cref="SqlState"/>. An implementation that DOES honour it owns two obligations: the retained
    /// form must be bounded, because the legacy's own comment describes the mode as trading space for
    /// time and an unbounded store trades away all of it; and the retained form must be released when
    /// the connection it was prepared against closes, because a prepared statement outliving its
    /// connection is a use-after-free.
    /// </para>
    /// </remarks>
    SqlState Execute(in SqlCommandText command, CancellationToken cancellationToken = default);
}

/// <summary>
/// The thirteen caller-supplied hooks a pooled transaction fires, the port of the PowerBuilder user
/// events declared on <c>n_cst_thread_trans</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L9-L29</c>].
/// </summary>
/// <remarks>
/// <para>
/// <b>EVERY MEMBER HAS A DEFAULT IMPLEMENTATION, AND THE DEFAULTS ARE THE POINT.</b> A PowerBuilder
/// <c>Event</c> call against an object with no script for that event returns <c>0</c> and does
/// nothing, so "not implemented" must behave as "allow, and no side effect" rather than as an error
/// or a veto. Default interface members reproduce that exactly, and they let a test double or a
/// production hook override only the one arm it cares about - which is what keeps the
/// connection-semantics matrix readable.
/// </para>
/// <para>
/// <b>THE VETO-RETURNING HOOKS ARE TESTED WITH <c>Predicates.IsPrevented</c>, WHICH IS AN EXACT
/// EQUALITY AGAINST <c>1</c>.</b> The legacy writes <c>IsPrevented(Event OnBeforeConnect())</c>
/// [<c>:L122</c>] and the same shape for the command hook [<c>:L224</c>], so a hook returning
/// <c>2</c> does NOT veto even though <c>2</c> is neither zero nor an allow code. That is the shared
/// kernel's preserved semantics and it is used rather than re-derived.
/// </para>
/// <para>
/// <b>THE CHECK HOOK IS REQUIRED TO BE NON-BLOCKING</b>, and that is a real constraint rather than
/// advice. <see cref="OnCheck"/> is reached from <see cref="IPooledTransaction.IsBroken"/>, which the
/// pool calls while holding its lock, so a hook that performed network or disk I/O there would hold
/// the pool's lock across it. The legacy hook is a flag-setting predicate and an implementation here
/// must be the same. Every hook that CAN block - connect, disconnect, commit, rollback, command - is
/// fired only outside the pool's lock, by construction.
/// </para>
/// <para>
/// C-F: NO IMPLEMENTATION MAY LOG A DESCRIPTOR OR ANY FIELD OF ONE. No hook on this interface
/// receives a descriptor, which is the structural half of that rule; the other half is that
/// <see cref="OnBeforeCommand"/> and <see cref="OnAfterCommand"/> receive STATEMENT text, so an
/// implementation that records either must pass it through <c>Errors/ISqlRedactor</c> first.
/// </para>
/// </remarks>
internal interface IPooledTransactionHooks
{
    /// <summary>
    /// Fired before the connection is opened, and able to veto it. [<c>:L122</c>]
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.PREVENT"/> to veto; any other value - including
    /// <see cref="RetCode.OK"/> - allows.
    /// </returns>
    /// <remarks>
    /// A veto is discriminated by the SQL state the hook leaves behind: a non-zero
    /// <see cref="SqlState.SqlCode"/> yields <see cref="RetCode.E_DB_ERROR"/> and a zero one yields
    /// <see cref="RetCode.CANCELLED"/> [<c>:L123-L124</c>]. A hook that wants a clean cancellation
    /// must therefore leave the state clean.
    /// </remarks>
    long OnBeforeConnect() => RetCode.OK;

    /// <summary>Fired after the connect statement, whatever its outcome. [<c>:L131</c>]</summary>
    /// <remarks>
    /// <b>IT RUNS ON FAILURE TOO, AND IT CAN CHANGE THE OUTCOME.</b> The legacy re-tests
    /// <c>SQLCode</c> AFTER this hook [<c>:L133</c>], so a hook that spoils a clean state turns a
    /// successful connect into <see cref="RetCode.E_DB_ERROR"/> and triggers the state-preserving
    /// clean disconnect. That is contract, not an accident.
    /// </remarks>
    void OnAfterConnect() { }

    /// <summary>
    /// Fired once a connection is confirmed good, after the liveness tick has been stamped and the
    /// broken flag cleared. [<c>:L107-L109</c>]
    /// </summary>
    /// <remarks>
    /// The legacy event carries the tick assignment and the flag clear in its own script, so a
    /// subclass overriding it would replace them. Here those two effects belong to
    /// <see cref="PooledTransaction"/> and are performed BEFORE this notification, so an override
    /// cannot accidentally discard them - which is a narrowing of the legacy extension point, made
    /// deliberately and recorded here rather than discovered.
    /// </remarks>
    void OnConnectionOk() { }

    /// <summary>Fired before a disconnect statement. [<c>:L150, :L513</c>]</summary>
    /// <remarks>
    /// Reached from BOTH disconnect paths - the ordinary one and the state-preserving clean
    /// disconnect - so an implementation must not assume which. It is NOT reached from the no-op fast
    /// path, where the legacy returns before firing any hook [<c>:L145-L148</c>].
    /// </remarks>
    void OnBeforeDisconnect() { }

    /// <summary>Fired after a disconnect statement. [<c>:L152, :L523</c>]</summary>
    /// <remarks>
    /// On the clean-disconnect path this hook observes the RESTORED state - the state as it was
    /// before the disconnect - because the restore happens first [<c>:L517-L521</c>]. On the ordinary
    /// path it observes the disconnect's own state. Two different views from one hook, both contract.
    /// </remarks>
    void OnAfterDisconnect() { }

    /// <summary>Fired before a rollback statement. [<c>:L172</c>]</summary>
    /// <remarks>
    /// It observes the state as the FAILING operation left it, because the snapshot is taken before
    /// this hook runs and the restore happens after the rollback [<c>:L166-L176</c>].
    /// </remarks>
    void OnBeforeRollback() { }

    /// <summary>Fired after a rollback statement, once the five state values have been restored. [<c>:L182</c>]</summary>
    /// <remarks>
    /// <b>THIS HOOK SEES THE ORIGINAL STATE, NOT THE ROLLBACK'S.</b> That ordering is the entire
    /// point of the state-preserving rollback and it is why the restore sits between the rollback and
    /// this hook [<c>:L176-L182</c>]. A "cleaner" implementation that fired this hook immediately
    /// after the rollback would silently show it the rollback's own state instead.
    /// </remarks>
    void OnAfterRollback() { }

    /// <summary>Fired before a commit statement. [<c>:L243</c>]</summary>
    void OnBeforeCommit() { }

    /// <summary>Fired after a commit statement, whatever its outcome. [<c>:L247</c>]</summary>
    /// <remarks>
    /// It runs before the <c>SQLCode</c> test [<c>:L249</c>], so a hook that spoils a clean state
    /// turns a successful commit into <see cref="RetCode.E_DB_ERROR"/> and, when auto-rollback was
    /// requested, into a rollback as well.
    /// </remarks>
    void OnAfterCommit() { }

    /// <summary>
    /// Fired before a statement executes, and able to veto it. [<c>:L224</c>]
    /// </summary>
    /// <param name="sqlCommand">The statement text, neither <see langword="null"/> nor empty.</param>
    /// <returns>
    /// <see cref="RetCode.PREVENT"/> to veto; any other value allows. The veto is discriminated by
    /// the SQL state exactly as the connect hook's is [<c>:L225-L226</c>].
    /// </returns>
    /// <remarks>C-F: an implementation that records the text must redact it first.</remarks>
    long OnBeforeCommand(string sqlCommand) => RetCode.OK;

    /// <summary>Fired after a statement executes, whatever its outcome. [<c>:L231</c>]</summary>
    /// <param name="sqlCommand">The statement text, neither <see langword="null"/> nor empty.</param>
    /// <remarks>C-F: an implementation that records the text must redact it first.</remarks>
    void OnAfterCommand(string sqlCommand) { }

    /// <summary>
    /// The health check consulted whenever the broken flag is not already set. [<c>:L197, :L531</c>]
    /// </summary>
    /// <returns>
    /// A return code. A FAILED code - by the shared kernel's
    /// <c>Predicates.IsFailed</c>, in which cancelled is NOT a failure - makes the liveness probe
    /// answer <see langword="false"/> immediately [<c>:L197</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE HOOK THAT MAKES <see cref="IPooledTransaction.IsBroken"/> IMPURE.</b> The
    /// legacy broken query fires it and then returns the flag [<c>:L530-L533</c>], so an
    /// implementation that calls <see cref="IPooledTransaction.SetBroken"/> from here causes the very
    /// act of inspecting a transaction to condemn it. That is the designed extension point, and the
    /// pool's two inspection sites are annotated accordingly.
    /// </para>
    /// <para>
    /// MUST NOT BLOCK - see the interface-level remark. This is the one hook the pool fires while
    /// holding its lock.
    /// </para>
    /// </remarks>
    long OnCheck() => RetCode.OK;

    /// <summary>
    /// The liveness test that REPLACES the built-in dialect probe when it answers non-null.
    /// [<c>:L200, :L209</c>]
    /// </summary>
    /// <returns>
    /// <see langword="null"/> to fall through to the built-in probe [<c>:L201</c>]; otherwise a return
    /// code judged by the shared kernel's <c>Predicates.IsSucceeded</c> [<c>:L209</c>] - under which a
    /// prevention still reads as connected, which is preserved rather than corrected.
    /// </returns>
    /// <remarks>
    /// The nullable return type is load-bearing and is exactly why this hook cannot be modelled as a
    /// plain <see cref="long"/>: <c>IsNull(rtCode)</c> is the legacy's own test for "no script here,
    /// use the built-in probe" [<c>:L201</c>], and collapsing null onto zero would turn every
    /// unimplemented hook into a successful test that never probed anything.
    /// </remarks>
    long? OnTest() => null;
}

// --------------------------------------------------------------------------------------------------
//  PART 3 OF 6 - THE POOLED-TRANSACTION ABSTRACTION
// --------------------------------------------------------------------------------------------------

/// <summary>
/// The pooled transaction the pool hands out - the port of <c>n_cst_thread_trans</c>'s
/// pool-facing lifecycle [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru</c>].
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS INTERFACE IS WHAT MAKES THE POOL TESTABLE WITHOUT A DATABASE (C-H).</b> Everything the
/// pool itself decides - reference counting, one-based index guards, the keep-alive retention arm, the
/// expiry comparison, the reuse-versus-recreate choice - is expressed in terms of these members and
/// nothing more, so a fake implementation plus a hand-driven <see cref="TimeProvider"/> covers every
/// arm the pool has. The database verbs sit one seam further down, behind
/// <see cref="ITransactionEngine"/>.
/// </para>
/// <para>
/// <b>THE POOL CALLS ONLY FIVE OF THESE MEMBERS</b>, and knowing which five is the fastest way to
/// understand the boundary: <see cref="IsBroken"/> [<c>n_cst_thread_trans_pool.sru:L96, :L105,
/// :L159</c>], <see cref="Disconnect"/> [<c>:L106, :L197, :L217</c>], <see cref="ClearState"/>
/// [<c>:L128, :L161</c>], <see cref="ApplyTransactionData"/> [<c>:L171</c>] and
/// <see cref="IDisposable.Dispose"/> for the legacy <c>Destroy</c> [<c>:L108, :L164, :L198,
/// :L218</c>]. <b>IT NEVER CALLS <see cref="Connect"/>.</b> The remaining members exist because the
/// TASK layer drives them [<c>n_cst_thread_task_sqlbase.sru:L173-L179, :L221, :L234</c>], and porting
/// the lifecycle without them would leave the pooled object unable to do the job it is pooled for.
/// </para>
/// <para>
/// <b>TWO PREDICATE FAMILIES LIVE ON THIS SURFACE AND THEY ARE NOT INTERCHANGEABLE.</b>
/// <see cref="IsSqlSucceeded"/> and <see cref="IsSqlFailed"/> are sign tests over
/// <see cref="SqlState.SqlCode"/>, a PROVIDER code [<c>:L337, :L340</c>]. <c>Predicates.IsSucceeded</c>
/// and <c>Predicates.IsFailed</c> from the shared kernel are the RETURN-CODE algebra, in which a
/// prevention reads as a success and cancelled is neither. The names are deliberately different so a
/// call site cannot silently pick the wrong one, and the legacy itself uses both families in one
/// method - the liveness probe tests HOOK RESULTS with the kernel predicates [<c>:L197, :L209</c>]
/// while testing PROVIDER state with the sign tests.
/// </para>
/// <para>
/// <b>Dispose is the port of <c>Destroy</c>, and it must be safe to call twice.</b> The pool disposes
/// an entry's transaction from four sites and disposes every remaining one again on its own disposal,
/// so an implementation that threw on a second call would turn an ordinary shutdown into a fault.
/// </para>
/// </remarks>
internal interface IPooledTransaction : IDisposable
{
    /// <summary>The coarse outcome code of the last operation. See <see cref="SqlState.SqlCode"/>.</summary>
    long SqlCode { get; }

    /// <summary>The provider's numeric code from the last operation.</summary>
    long SqlDbCode { get; }

    /// <summary>The affected or returned row count from the last operation.</summary>
    long SqlNRows { get; }

    /// <summary>The provider's message text from the last operation. Never <see langword="null"/>.</summary>
    string SqlErrText { get; }

    /// <summary>The provider's return text from the last operation. Never <see langword="null"/>.</summary>
    string SqlReturnData { get; }

    /// <summary>
    /// Overwrites all five SQL-state values at once.
    /// </summary>
    /// <param name="state">The state to impose.</param>
    /// <remarks>
    /// <para>
    /// <b>THE PORT OF THE LEGACY TRANSACTION OBJECT'S SQL-STATE PROPERTIES BEING PUBLICLY
    /// ASSIGNABLE.</b> PowerBuilder's <c>SQLCode</c>, <c>SQLDBCode</c>, <c>SQLNRows</c>,
    /// <c>SQLErrText</c> and <c>SQLReturnData</c> are read-WRITE properties of the transaction, and the
    /// framework itself assigns all five in two places - the state-preserving rollback's restore
    /// [<c>n_cst_thread_trans.sru:L176-L180</c>] and the clean disconnect's
    /// [<c>:L517-L521</c>]. Publishing the five as read-only alone would therefore be a NARROWING of
    /// the legacy surface.
    /// </para>
    /// <para>
    /// <b>IT IS NOT A CONVENIENCE - IT IS THE ONLY WAY ONE MEASURED ARM IS REACHABLE.</b> The legacy
    /// hooks are user events declared ON the transaction object, so a hook script can assign
    /// <c>SQLCode</c> directly. That is precisely how a clean connect comes to be reported as a
    /// database error: the after-connect hook spoils the state, and the framework re-tests it
    /// AFTERWARDS [<c>:L131-L137</c>]. In this port the hooks are a separate injected object, so
    /// without this member no hook could reach that arm and the branch would be dead code that the
    /// legacy exercises.
    /// </para>
    /// <para>
    /// It writes state ONLY. It does not touch the broken flag, does not touch the liveness tick, and
    /// performs no I/O - exactly like a PowerScript property assignment.
    /// </para>
    /// </remarks>
    void StampSqlState(in SqlState state);

    /// <summary>
    /// Whether the connection is in auto-commit mode.
    /// </summary>
    /// <value>
    /// Gates <see cref="Rollback"/> [<c>:L185</c>], <see cref="Commit(bool)"/> [<c>:L240</c>] and both
    /// branches of <see cref="AutoCommitCheckpoint"/> [<c>:L371, :L376</c>]. Settable because the
    /// command task toggles it around a statement to reproduce the <c>AC_NATIVE</c> arm.
    /// </value>
    bool AutoCommit { get; set; }

    /// <summary>
    /// Opens the connection, reproducing every arm of <c>of_connect</c> [<c>:L111-L143</c>].
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success; <see cref="RetCode.E_INVALID_TRANSACTION"/> when already
    /// broken; <see cref="RetCode.CANCELLED"/> on a clean veto;
    /// <see cref="RetCode.E_DB_ERROR"/> on a veto that left an error, on a failed connect, or on a
    /// successful connect whose state was spoiled afterwards.
    /// </returns>
    /// <remarks>
    /// <b>THE POOL NEVER CALLS THIS.</b> Connection is driven by the task layer
    /// [<c>n_cst_thread_task_sqlbase.sru:L173-L179</c>].
    /// </remarks>
    long Connect(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the connection, reproducing every arm of <c>of_disconnect</c> [<c>:L145-L161</c>].
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success or on the no-op fast path;
    /// <see cref="RetCode.E_DB_ERROR"/> when the statement reported an error.
    /// </returns>
    /// <remarks>
    /// The pool calls this from three sites with two DIFFERENT guards: guarded by not-broken in
    /// <c>RemoveRef</c> [<c>n_cst_thread_trans_pool.sru:L105-L107</c>], and UNGUARDED in
    /// <c>RemoveAll</c> [<c>:L197</c>] and <c>Collect</c> [<c>:L217</c>]. The asymmetry is the
    /// legacy's and is preserved; this method's own fast path makes the unguarded calls harmless in
    /// practice, which is very likely why the legacy author never harmonised them.
    /// </remarks>
    long Disconnect();

    /// <summary>
    /// Rolls back, preserving the five SQL-state values across the statement. [<c>:L185-L191</c>]
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.FAILED"/> when auto-commit is on - <b>not</b> a harmless no-op;
    /// <see cref="RetCode.E_INVALID_TRANSACTION"/> when broken; otherwise
    /// <see cref="RetCode.OK"/>.
    /// </returns>
    long Rollback();

    /// <summary>
    /// Commits, optionally rolling back on failure. [<c>:L240-L257</c>]
    /// </summary>
    /// <param name="autoRollback">
    /// When <see langword="true"/> and the commit reports an error, the state-preserving rollback runs
    /// before the error is returned [<c>:L250-L252</c>].
    /// </param>
    /// <returns>
    /// <see cref="RetCode.FAILED"/> when auto-commit is on; <see cref="RetCode.E_INVALID_TRANSACTION"/>
    /// when broken; <see cref="RetCode.E_DB_ERROR"/> when the statement reported an error; otherwise
    /// <see cref="RetCode.OK"/>.
    /// </returns>
    long Commit(bool autoRollback);

    /// <summary>
    /// Commits with auto-rollback enabled - the parameterless overload [<c>:L383-L384</c>].
    /// </summary>
    /// <returns>Whatever <see cref="Commit(bool)"/> returns for <see langword="true"/>.</returns>
    long Commit();

    /// <summary>
    /// The end-of-unit-of-work checkpoint. [<c>:L370-L381</c>]
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.E_DB_ERROR"/> when the current state already carries an error, in which case
    /// a non-auto-commit connection is rolled back first; otherwise the result of
    /// <see cref="Commit(bool)"/> for a non-auto-commit connection, or <see cref="RetCode.OK"/> for an
    /// auto-commit one.
    /// </returns>
    /// <remarks>
    /// Named <c>AutoCommitCheckpoint</c> rather than <c>AutoCommit</c> because the legacy function
    /// <c>of_autocommit</c> and the legacy PROPERTY <c>AutoCommit</c> are different things that C#
    /// cannot spell the same way - a method and a property cannot share a name on one type. The
    /// property keeps the legacy spelling because it is the one that appears in the descriptor and on
    /// the wire; the method is renamed, and this is the note that records it.
    /// </remarks>
    long AutoCommitCheckpoint();

    /// <summary>
    /// Executes a statement. [<c>:L220-L238</c>]
    /// </summary>
    /// <param name="sqlCommand">The statement text.</param>
    /// <returns>
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> when the text is <see langword="null"/> or empty;
    /// <see cref="RetCode.CANCELLED"/> on a clean veto; <see cref="RetCode.E_DB_ERROR"/> on a veto that
    /// left an error or on a statement whose code is neither <c>0</c> nor <c>100</c>; otherwise
    /// <see cref="RetCode.OK"/>.
    /// </returns>
    /// <remarks><b><c>SQLCode = 100</c> - no data found - IS A SUCCESS HERE</b> [<c>:L233</c>].</remarks>
    long Exec(string? sqlCommand, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a statement with PROVIDER-BOUND parameters, on the same arms as
    /// <see cref="Exec(string?)"/>. [<c>:L220-L238</c>]
    /// </summary>
    /// <param name="command">The canonical statement, its ordered values, and the rendered parity text.</param>
    /// <returns>Exactly the codes <see cref="Exec(string?)"/> returns, on exactly the same tests.</returns>
    /// <remarks>
    /// <para>
    /// EVERY ARM IS THE SAME AS THE SINGLE-STRING OVERLOAD'S. The emptiness guard tests the CANONICAL
    /// text, the veto discrimination is unchanged, and <c>SQLCode = 100</c> is still a success. Only the
    /// value that reaches the engine differs, and it differs by being bound rather than spliced.
    /// </para>
    /// <para>
    /// The two vetoable hooks receive the RENDERED text, because that is the statement the oracle's own
    /// handlers would have seen [<c>:L224, :L231</c>].
    /// </para>
    /// </remarks>
    long Exec(in SqlCommandText command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a statement whose values travel BESIDE it - the parameterized form of
    /// <see cref="Exec(string?)"/>. [<c>:L220-L238</c>]
    /// </summary>
    /// <param name="statement">The statement in both its parity and its executable forms.</param>
    /// <returns>Exactly the codes <see cref="Exec(string?)"/> returns.</returns>
    /// <remarks>
    /// <para>
    /// Every guard, hook and status test of <see cref="Exec(string?)"/> applies unchanged - the empty
    /// statement guard [<c>:L220</c>], the state clear [<c>:L222</c>], the vetoable before-command hook
    /// with its database-error discrimination [<c>:L224-L226</c>], the after-command notification that
    /// fires on failure too [<c>:L231</c>], and the arm in which <c>SQLCode = 100</c> reads as a SUCCESS
    /// [<c>:L233</c>]. Only the CARRIAGE of the values changes.
    /// </para>
    /// <para>
    /// <b>THE HOOKS SEE THE OBSERVABLE TEXT, NOT THE PARAMETERIZED TEXT.</b> A caller-supplied hook in
    /// the oracle receives the statement the oracle was about to run, so handing it the placeholder form
    /// would change what an existing hook observes - a behavioural change C-B forbids.
    /// </para>
    /// <para>
    /// <b>Defaulted to the observable form, exactly as <see cref="ITransactionEngine.Execute(SqlBoundStatement)"/>
    /// is.</b> An implementation that carries no parameters - a test double, or a transaction over an
    /// engine with no binding support - runs the interpolated text, which is precisely what the legacy
    /// ran, so the default is the legacy behaviour rather than a shortcut. The shipped implementation
    /// overrides it and carries the parameters through, which is where the CWE-89 exposure is actually
    /// closed.
    /// </para>
    /// </remarks>
    long Exec(SqlBoundStatement statement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statement);

        // BOUND, NOT SPLICED, AND FOR EVERY IMPLEMENTATION RATHER THAN ONLY THE SHIPPED ONE. This
        // used to hand the OBSERVABLE text to the single-string overload and leave the parameterized
        // text for an override to honour - which meant an implementation that forgot to override
        // silently re-opened the CWE-89 exposure the dual form exists to close. Converting here
        // instead makes the parameterized path the only path: the values travel beside the statement
        // to the in-parameter overload, whose shipped implementation hands the hooks the rendered
        // text and the provider the placeholder text with the values bound.
        //
        // The names recorded on the bound parameters are NOT re-read here, because binding is
        // POSITIONAL: SqlCommandText.BindTo re-derives @p1..@pN from the ordinal. The binder mints
        // exactly those names in exactly that order [Tasks/SqlTaskBase.cs, BindParams], so the two
        // agree by construction rather than by coincidence.
        //
        // THE EXECUTION MODE IS FORWARDED RATHER THAN DEFAULTED. It is the port of the leading-`@`
        // prefix, and this conversion is the only bridge between the statement type the task composes
        // and the command type the engine receives - so dropping it here would silently discard a mode
        // the caller selected and the published contract promises to honour (AAP §0.4.3 C-07).
        SqlCommandText command = SqlCommandText.FromBoundStatement(
            statement.ParameterizedText,
            statement.ObservableText,
            [.. statement.Parameters.Select(static parameter => parameter.Value)],
            statement.CacheStatement);

        return Exec(in command, cancellationToken);
    }

    /// <summary>
    /// The engine this transaction runs on, or <see langword="null"/> when it is not backed by one.
    /// </summary>
    /// <returns>The composed engine, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// <b>WHY THE READ SIDE NEEDS THIS, AND WHY IT IS NOT A WIDENING OF THE PORTED CONTRACT.</b> Every
    /// member above already delegates to the engine this transaction was composed with - connect,
    /// commit, rollback and exec are all forwarding calls. The read side of this service - the data-object
    /// runtime and the query surface - has to run its SELECTs through the SAME connection those writes
    /// went through, or an uncommitted write would be invisible to the retrieval that follows it inside
    /// one transaction. Publishing the seam the port already owns is what makes that possible; it does
    /// NOT publish a driver handle, because <c>ITransactionEngine</c> is itself the ported connection
    /// surface rather than a provider type.
    /// </para>
    /// <para>
    /// <b>Defaulted to <see langword="null"/> deliberately.</b> A test double, or a deployment that
    /// substitutes the activator, composes no engine - and every consumer of this member already
    /// publishes a defined negative for "no provider backs that transaction". Defaulting therefore keeps
    /// every existing implementation of this interface valid while making the negative reachable and
    /// honest, instead of forcing each one to invent an engine it does not have.
    /// </para>
    /// </remarks>
    ITransactionEngine? ResolveEngine() => null;

    /// <summary>
    /// Whether the connection is believed live, using the cached liveness tick. [<c>:L193-L218</c>]
    /// </summary>
    /// <returns><see langword="true"/> when the connection is believed live.</returns>
    /// <remarks>
    /// <b>NOT A PURE QUERY EITHER.</b> It refreshes or zeroes the liveness tick [<c>:L211-L215</c>] and
    /// it fires the check hook [<c>:L197</c>], which can condemn the transaction. The pool never calls
    /// it; the task layer does [<c>n_cst_thread_task_sqlbase.sru:L173</c>].
    /// </remarks>
    bool IsConnected();

    /// <summary>
    /// Whether the connection is believed live, ALSO REPORTING whether the answer came from an actual
    /// probe or from the liveness cache. [<c>:L193-L218</c>]
    /// </summary>
    /// <param name="probed">
    /// <see langword="true"/> when the connection was actually interrogated - by the test hook, or by
    /// the dialect probe when that hook yields nothing [<c>:L200-L210</c>]. <see langword="false"/> when
    /// the answer came from the cache short-circuit [<c>:L198</c>] or from one of the two
    /// no-probe-needed refusals [<c>:L196</c>, <c>:L197</c>].
    /// </param>
    /// <returns><see langword="true"/> when the connection is believed live.</returns>
    /// <remarks>
    /// <para>
    /// <b>THIS OVERLOAD REPORTS A DISTINCTION THE ORACLE ALREADY MAKES BUT NEVER RETURNS, AND IT ADDS NO
    /// BEHAVIOUR.</b> The cache short-circuit at [<c>:L198</c>] answers <see langword="true"/> WITHOUT
    /// TOUCHING THE CONNECTION, and nothing about the boolean result distinguishes that from a real
    /// probe - the difference is observable only through the ABSENCE of the probe. A characterization
    /// comparison has to be able to see it, which is why the published contract carries
    /// <c>persistence.v1.IsConnectedResponse.probed</c> and why the flag has to be reported rather than
    /// deduced: deducing it from a statement-status delta would have false negatives, because the test
    /// hook can answer without changing any state.
    /// </para>
    /// <para>
    /// The parameterless overload above remains the primary member and delegates here, so no existing
    /// caller changes and there is exactly one implementation of the sequence.
    /// </para>
    /// </remarks>
    bool IsConnected(out bool probed);

    /// <summary>
    /// Whether the transaction has been condemned. [<c>:L530-L534</c>]
    /// </summary>
    /// <returns><see langword="true"/> when the transaction is broken.</returns>
    /// <remarks>
    /// <b>THIS QUERY HAS A SIDE EFFECT.</b> When the flag is not already set it fires
    /// <see cref="IPooledTransactionHooks.OnCheck"/>, which may set it - so ASKING can change the
    /// answer for every subsequent asker. The pool inspects entries with it in two hot paths, so an
    /// implementation's check hook must be cheap and must not block.
    /// </remarks>
    bool IsBroken();

    /// <summary>
    /// Condemns the transaction. [<c>:L526-L528</c>]
    /// </summary>
    /// <returns><see cref="RetCode.OK"/>, always - the legacy has no failure arm here.</returns>
    long SetBroken();

    /// <summary>
    /// Clears the five SQL-state values. [<c>:L363-L368</c>]
    /// </summary>
    /// <remarks>
    /// The pool calls this on every object it hands out, whether reused [<c>:L161</c>] or freshly
    /// created by way of <c>Release</c>'s own clear [<c>:L128</c>], so a caller always starts from a
    /// clean state. It does NOT clear the broken flag and does NOT touch the liveness tick.
    /// </remarks>
    void ClearState();

    /// <summary>
    /// The dialect discriminator, resolved from the DBMS identifier. [<c>:L356-L361</c>]
    /// </summary>
    /// <returns>
    /// <see cref="DatabaseType.DbtOracle"/> when the upper-cased DBMS string CONTAINS <c>ORACLE</c>;
    /// <see cref="DatabaseType.DbtMssql"/> for <b>everything else, INCLUDING SQLITE and including an
    /// empty string</b>.
    /// </returns>
    /// <remarks>
    /// C-E: the answer selects a paging STRING TRANSFORM in <c>Sql/Paging/</c> and nothing else. No SQL
    /// Server or Oracle connection is ever opened on the strength of it.
    /// </remarks>
    DatabaseType GetDbType();

    /// <summary>
    /// Adopts the descriptor's seven connection fields, honouring the caller's veto hook.
    /// [<c>:L343-L354</c>]
    /// </summary>
    /// <param name="descriptor">The descriptor to adopt.</param>
    /// <returns><see cref="RetCode.OK"/>, always - including on the vetoed path [<c>:L343</c>].</returns>
    /// <remarks>
    /// The pool calls this on a NEWLY CREATED object only [<c>n_cst_thread_trans_pool.sru:L171</c>]; a
    /// reused object is never re-configured. That asymmetry is annotated at the pool's own call site.
    /// </remarks>
    long ApplyTransactionData(in TransactionData descriptor);

    /// <summary>Whether the last operation failed, by the provider code's sign. [<c>:L337</c>]</summary>
    /// <returns><see langword="true"/> when <see cref="SqlCode"/> is negative.</returns>
    /// <remarks>
    /// <b>NOT <c>Predicates.IsFailed</c>.</b> This is a sign test over a provider code; that is the
    /// return-code algebra. <c>100</c> answers <see langword="false"/> here, as it must.
    /// </remarks>
    bool IsSqlFailed();

    /// <summary>Whether the last operation succeeded, by the provider code's sign. [<c>:L340</c>]</summary>
    /// <returns><see langword="true"/> when <see cref="SqlCode"/> is zero or positive.</returns>
    /// <remarks>
    /// <b>NOT <c>Predicates.IsSucceeded</c>.</b> <c>100</c> answers <see langword="true"/> here, which
    /// is exactly the "no data found reads as success" behaviour the command path relies on.
    /// </remarks>
    bool IsSqlSucceeded();

    /// <summary>
    /// The structured error payload a caller needs when a database operation failed.
    /// </summary>
    /// <returns>
    /// <c>DbErrorData.FromTransaction(SqlDbCode, SqlErrText)</c> - the provider's numeric code and its
    /// message text, with an EMPTY statement, the primary buffer and row zero.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Exactly the two values the consumer copies and no third
    /// [<c>n_cst_thread_task_sqlbase.sru:L175-L176</c>]. <see cref="SqlCode"/> is deliberately NOT
    /// among them: the legacy copies <c>SQLDBCode</c>, which is the provider's code rather than the
    /// coarse outcome.
    /// </para>
    /// <para>
    /// C-F and C-B together: the payload carries no statement text, so there is nothing for
    /// <c>Errors/ISqlRedactor</c> to redact, and adding the statement would invent a legacy behaviour
    /// that does not exist. Mapping this onto the wire <c>DbError</c> is
    /// <c>Errors/SqlRedactor.ToDbError</c>'s job and belongs to <c>Grpc/</c>, not here.
    /// </para>
    /// </remarks>
    DbErrorData CaptureError();

    /// <summary>
    /// Requests an optional capability from the engine this transaction wraps.
    /// </summary>
    /// <typeparam name="TCapability">The capability interface being asked for.</typeparam>
    /// <param name="capability">
    /// Receives the engine cast to <typeparamref name="TCapability"/>, or <see langword="null"/> when the
    /// bound engine does not offer it.
    /// </param>
    /// <returns><see langword="true"/> when the capability was available.</returns>
    /// <remarks>
    /// <para>
    /// <b>WHY A PROBE RATHER THAN A MEMBER PER CAPABILITY, AND WHY IT KEEPS THIS FILE
    /// PROVIDER-NEUTRAL.</b> The legacy fuses connection and result carrier: a datastore holds its own
    /// transaction pointer and reaches the driver through it, so retrieval and update statements run on
    /// the same connection the transaction object opened. Something in this port has to reproduce that,
    /// and the alternatives were both worse. Declaring a provider-shaped member here - a command factory
    /// returning a SQLite command - would put the storage provider into the pooling abstraction, which is
    /// the one type in this service that must stay ignorant of it. Opening a SECOND connection for
    /// retrieval would silently take every read outside the caller's transaction, so an uncommitted write
    /// would be invisible to a read in the same session; that is a correctness defect, not a style one.
    /// </para>
    /// <para>
    /// So the capability is asked for by INTERFACE, and the interface is declared by the layer that owns
    /// the provider [<c>Data/DataWindowRuntime.cs</c>] rather than by this one. Nothing here names a
    /// provider type, and a transaction whose engine does not offer the requested capability answers
    /// <see langword="false"/> - which every caller turns into that caller's own documented negative
    /// rather than into an exception.
    /// </para>
    /// </remarks>
    bool TryGetEngineCapability<TCapability>(
        [NotNullWhen(true)] out TCapability? capability)
        where TCapability : class;
}

// --------------------------------------------------------------------------------------------------
//  PART 4 OF 6 - THE DEFAULT POOLED TRANSACTION
// --------------------------------------------------------------------------------------------------

/// <summary>
/// The default <see cref="IPooledTransaction"/>: the connection lifecycle of
/// <c>n_cst_thread_trans</c> reproduced arm for arm over an injected
/// <see cref="ITransactionEngine"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHAT LIVES HERE AND WHAT DOES NOT.</b> This class owns the ORDER of things - which hook fires
/// before which statement, when the five state values are snapshotted and when they are put back,
/// which <c>SQLCode</c> test decides which return code, when the liveness tick is stamped and when it
/// is zeroed. It owns no I/O: the five database verbs come from <see cref="ITransactionEngine"/>, so
/// every arm below is reachable from a unit test with a fake engine and a hand-driven clock.
/// </para>
/// <para>
/// <b>THE ONE CLOCK IT READS IS THE LIVENESS CACHE - CLOCK 2 OF THE TWO IN THIS FILE.</b>
/// <c>CPU() - _nLastConnOK &lt; 10000</c> [<c>:L198</c>], threshold ten thousand milliseconds,
/// operator STRICTLY LESS THAN, sentinel zero meaning "never connected OK". It reads the injected
/// <see cref="TimeProvider"/> and nothing else; there is no <c>DateTime.UtcNow</c> here.
/// </para>
/// <para>
/// <b>NOT THREAD SAFE, DELIBERATELY, AND THAT IS THE LEGACY'S POSTURE TOO.</b> The legacy transaction
/// object is single-owner: a SQL task holds one, works it on the worker thread, and hands it back
/// through the pool. AAP 0.4.5.4 states those thread-affinity annotations are contract, so this class
/// adds no lock and no synchronisation of its own - doing so would flatten the caller-side and
/// worker-side duality that <c>Tasks/TaskProxies/</c> exists to preserve. The POOL is the thread-safe
/// component; a transaction it has handed out belongs to one caller until that caller releases it.
/// </para>
/// <para>
/// C-F: this class holds no credential. It never reads <c>LogPass</c> - the descriptor goes straight
/// to <see cref="ITransactionEngine.ApplyConnectionFields"/>, which is the only member that can - and
/// it stores no descriptor field of any kind. It takes no logger, so it has no logging expression from
/// which one could leak.
/// </para>
/// </remarks>
internal sealed class PooledTransaction : IPooledTransaction
{
    /// <summary>
    /// The liveness-cache window in milliseconds - <c>10000</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From <c>if CPU() - _nLastConnOK &lt; 10000 then return true</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L198</c>], where the legacy writes
    /// the number as a bare literal with no name at all.
    /// </para>
    /// <para>
    /// <b>THE COMPARISON IS STRICTLY LESS THAN AND THAT BOUNDARY IS PINNED BY TEST.</b> A delta of
    /// 9,999 ms short-circuits and answers connected without probing; a delta of exactly 10,000 ms does
    /// NOT short-circuit and runs the probe. Turning the operator into <c>&lt;=</c> would suppress one
    /// probe per window boundary, which is unobservable in a happy path and wrong in exactly the case
    /// the probe exists for.
    /// </para>
    /// <para>
    /// Named in PascalCase rather than kept as a SCREAMING_SNAKE constant because this folder sits
    /// outside every <c>.editorconfig</c> naming-suppression section and warnings are errors. The
    /// legacy has no identifier here to preserve, so nothing observable is lost - unlike
    /// <c>RetCode.OK</c> or <c>DBT_ORACLE</c>, whose spellings travel in recordings and are consumed
    /// from their own files.
    /// </para>
    /// <para>
    /// <b><see langword="internal"/> rather than <see langword="private"/>, so the ONE value is stated
    /// once.</b> The published contract lets a caller ASK for a liveness window
    /// (<c>persistence.v1.PoolKeepAliveSettings.liveness_cache_window_ms</c>), and this build honours
    /// only the legacy one - so <c>Grpc/TransactionService.cs</c> has to compare a requested window
    /// against it in order to refuse a request it could not satisfy. Reading it from here is what keeps
    /// that comparison from becoming a second copy of the number that could drift from this one. It is
    /// still on no public surface: this assembly exposes its internals to
    /// <c>PowerFramework.Persistence.Tests</c> and to nothing else.
    /// </para>
    /// </remarks>
    internal const long LivenessCacheWindowMilliseconds = 10_000;

    /// <summary>
    /// The Oracle marker the dialect resolver and the liveness probe both search for.
    /// </summary>
    /// <remarks>
    /// <c>Pos(Upper(DBMS),"ORACLE") &gt; 0</c> appears twice, at [<c>:L202</c>] for the probe statement
    /// and at [<c>:L356</c>] for the dialect. It is a CONTAINS test on the UPPER-CASED string, not an
    /// equality and not a prefix, so <c>O90 ORACLE</c> and <c>ORACLE 12C</c> both match - which is
    /// exactly why PowerBuilder's own dotted DBMS strings work.
    /// </remarks>
    private const string OracleMarker = "ORACLE";

    /// <summary>
    /// The liveness probe for an Oracle connection [<c>:L203</c>].
    /// </summary>
    /// <remarks>
    /// C-E: this statement is only ever sent down an ALREADY OPEN connection that the injected engine
    /// supplied. Its presence opens nothing, provisions nothing and composes no connection string, and
    /// only SQLite is provisioned in this phase. The dual-table form is Oracle's requirement for a
    /// select with no table, which is the whole reason the legacy branches here at all.
    /// </remarks>
    private const string OracleLivenessProbe = "SELECT 1 FROM DUAL";

    /// <summary>
    /// The liveness probe for every non-Oracle connection [<c>:L205</c>], SQLite included.
    /// </summary>
    private const string DefaultLivenessProbe = "SELECT 1";

    private readonly ITransactionEngine _engine;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The MONOTONIC origin this instance measures from.
    /// </summary>
    /// <remarks>
    /// Captured once, from the injected provider, so every stamp is an elapsed measurement rather than a
    /// wall-clock reading. Wall time is adjustable - a clock step or a rollback would make a cached
    /// liveness answer outlive its window or expire early - while an elapsed measurement cannot move
    /// backwards. The provider is still the only source, so the characterization requirement that every
    /// clock read be maskable from both the master and the candidate (AAP 0.6.7) is unchanged.
    /// </remarks>
    private readonly long _monotonicOrigin;

    private readonly IPooledTransactionHooks _hooks;

    /// <summary>
    /// The five SQL-state values. The port of the legacy transaction object's own
    /// <c>SQLCode</c>/<c>SQLDBCode</c>/<c>SQLNRows</c>/<c>SQLErrText</c>/<c>SQLReturnData</c>
    /// properties, held as one value so the snapshot-and-restore pair cannot drift.
    /// </summary>
    private SqlState _state = SqlState.Cleared;

    /// <summary>
    /// The liveness tick in milliseconds - CLOCK 2. Zero means "never connected OK"
    /// [<c>:L196</c>], which is why it is not nullable: the legacy sentinel IS zero and reproducing it
    /// as a sentinel rather than as <see langword="null"/> keeps the arm ordering identical.
    /// </summary>
    private long _lastConnectionOkTicks;

    /// <summary>
    /// The condemned flag - the legacy <c>_bBroken</c> [<c>:L69</c>]. Set by
    /// <see cref="SetBroken"/> [<c>:L526</c>] and cleared only by a confirmed connection
    /// [<c>:L108</c>].
    /// </summary>
    private bool _broken;

    /// <summary>Guards <see cref="Dispose"/> against a second call.</summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledTransaction"/> class.
    /// </summary>
    /// <param name="engine">
    /// The database-verb seam. Its SQLite implementation belongs to
    /// <c>Data/SqliteConnectionFactory.cs</c> (C-E); this class composes no connection string.
    /// </param>
    /// <param name="timeProvider">
    /// The injected clock - the SAME seam the pool uses, so a characterization run can substitute both
    /// clocks at once. Registered by <c>Program.cs</c> as a singleton.
    /// </param>
    /// <param name="hooks">
    /// The caller's hooks, or <see langword="null"/> for the no-op set. <see langword="null"/> is the
    /// NORMAL case, not a degenerate one: it behaves exactly as a PowerBuilder object with no script
    /// for any of those events.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="engine"/> or <paramref name="timeProvider"/> is <see langword="null"/>.
    /// Fail-fast, per AAP 0.1.4: a transaction with no engine or no clock is structurally invalid and
    /// there is no degraded mode worth having.
    /// </exception>
    public PooledTransaction(
        ITransactionEngine engine,
        TimeProvider timeProvider,
        IPooledTransactionHooks? hooks = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _engine = engine;
        _timeProvider = timeProvider;
        _monotonicOrigin = timeProvider.GetTimestamp();
        _hooks = hooks ?? NoOpHooks.Instance;
    }

    /// <inheritdoc/>
    public long SqlCode => _state.SqlCode;

    /// <inheritdoc/>
    public long SqlDbCode => _state.SqlDbCode;

    /// <inheritdoc/>
    public long SqlNRows => _state.SqlNRows;

    /// <inheritdoc/>
    public string SqlErrText => _state.SqlErrText;

    /// <inheritdoc/>
    public string SqlReturnData => _state.SqlReturnData;

    /// <inheritdoc/>
    public bool AutoCommit
    {
        get => _engine.AutoCommit;
        set => _engine.AutoCommit = value;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A single field assignment, because the five values are held as ONE value - see
    /// <see cref="SqlState"/> for why. The legacy needs five statements to do this and has to keep them
    /// in step by hand [<c>:L176-L180, :L517-L521</c>].
    /// </remarks>
    public void StampSqlState(in SqlState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _state = state;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The oracle, verbatim [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L111-L143</c>]:
    /// <code>
    /// if _bBroken then return RetCode.E_INVALID_TRANSACTION                    [:L113]
    /// if DBHandle() &lt;&gt; 0 then DISCONNECT USING this;                     [:L115-L117]
    /// _nLastConnOK = 0                                                          [:L119]
    /// of_ClearState()                                                           [:L120]
    /// if IsPrevented(Event OnBeforeConnect()) then                              [:L122]
    ///     if SQLCode &lt;&gt; 0 then return RetCode.E_DB_ERROR                  [:L123]
    ///     return RetCode.CANCELLED                                              [:L124]
    /// end if
    /// CONNECT USING this;                                                       [:L127]
    /// bConnected = (SQLCode = 0)                                                [:L129]
    /// Event OnAfterConnect()                                                    [:L131]
    /// if SQLCode &lt;&gt; 0 then                                                [:L133]
    ///     if bConnected then _of_CleanDisconnect()                              [:L134-L136]
    ///     return RetCode.E_DB_ERROR                                             [:L137]
    /// end if
    /// Event OnConnOK()                                                          [:L140]
    /// return RetCode.OK                                                         [:L142]
    /// </code>
    /// <para>
    /// <b>THE LAST TWO ARMS ARE THE SUBTLE ONES.</b> <c>bConnected</c> is captured BEFORE the
    /// after-hook runs, and the <c>SQLCode</c> test happens AFTER it - so a hook that spoils a clean
    /// state causes a connection that genuinely opened to be cleanly disconnected again and reported as
    /// a database error. An implementation that tested <c>SQLCode</c> before the hook, or that reused
    /// the post-hook state as <c>bConnected</c>, would leak an open connection in exactly that case.
    /// </para>
    /// <para>
    /// The pre-emptive disconnect at [<c>:L115</c>] is NOT the state-preserving one - it is a bare
    /// statement with no hooks and no snapshot - so it is reproduced as a bare engine call.
    /// </para>
    /// </remarks>
    public long Connect(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // THE CANCELLATION ARM, AND IT SITS FIRST BECAUSE IT DECIDES WHETHER TO START AT ALL. See
        // ObserveCancellation for the policy that puts an arm here and deliberately puts none on commit,
        // rollback or the liveness probe.
        if (ObserveCancellation(cancellationToken))
        {
            return RetCode.CANCELLED;
        }

        // [:L113] A condemned transaction is never reconnected.
        if (_broken)
        {
            return RetCode.E_INVALID_TRANSACTION;
        }

        // [:L115-L117] `if DBHandle() <> 0 then DISCONNECT USING this;` - the NOT-EQUAL spelling,
        // preserved. No hook fires and no state is preserved around it: the legacy writes a bare
        // DISCONNECT here, not _of_CleanDisconnect().
        if (_engine.DbHandle != 0)
        {
            _state = _engine.Disconnect();
        }

        // [:L119] The liveness tick is invalidated before anything else can consult it.
        _lastConnectionOkTicks = 0;

        // [:L120]
        ClearState();

        // [:L122] Predicates.IsPrevented is an EXACT equality against RetCode.PREVENT, so a hook
        // returning 2 does not veto. That is the shared kernel's preserved semantics, consumed rather
        // than re-derived.
        if (Predicates.IsPrevented(_hooks.OnBeforeConnect()))
        {
            // [:L123-L124] THE VETO DISCRIMINATION. A veto that left an error is a database error; a
            // clean veto is a cancellation. Note this reads the state the HOOK left behind, which is
            // why ClearState() ran before the hook rather than after it.
            return _state.SqlCode != 0 ? RetCode.E_DB_ERROR : RetCode.CANCELLED;
        }

        // [:L127]
        _state = _engine.Connect(cancellationToken);

        // [:L129] Captured BEFORE the after-hook. This is the flag that decides whether a spoiled
        // state has an open connection to clean up.
        bool connected = _state.SqlCode == 0;

        // [:L131] Fires on failure as well as on success.
        _hooks.OnAfterConnect();

        // [:L133] Re-tested AFTER the hook, deliberately.
        if (_state.SqlCode != 0)
        {
            if (connected)
            {
                // [:L134-L136] Clean up a connection that DID open. State-preserving, so the caller
                // still reads the error that condemned it rather than the disconnect's own outcome.
                CleanDisconnect();
            }

            return RetCode.E_DB_ERROR;
        }

        // [:L140] The connection-OK hook. The tick stamp and the flag clear are performed here rather
        // than inside the notification, so an override cannot discard them - see
        // IPooledTransactionHooks.OnConnectionOk for why that narrowing was chosen.
        MarkConnectionOk();

        // [:L142]
        return RetCode.OK;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The oracle, verbatim [<c>:L145-L161</c>]:
    /// <code>
    /// if DBHandle() &lt;= 0 or _bBroken then                                    [:L145]
    ///     _nLastConnOK = 0                                                      [:L146]
    ///     return RetCode.OK                                                     [:L147]
    /// end if
    /// Event OnBeforeDisconnect()                                                [:L150]
    /// DISCONNECT USING this;                                                    [:L151]
    /// Event OnAfterDisconnect()                                                 [:L152]
    /// _nLastConnOK = 0                                                          [:L154]
    /// if SQLCode &lt;&gt; 0 then return RetCode.E_DB_ERROR                      [:L156-L158]
    /// return RetCode.OK                                                         [:L160]
    /// </code>
    /// <para>
    /// <b>THE FAST PATH FIRES NO HOOKS AND STILL REPORTS SUCCESS</b>, which is what makes the pool's
    /// unguarded disconnect calls in <c>RemoveAll</c> and <c>Collect</c> harmless on a broken or
    /// already-closed object. It also zeroes the liveness tick, so "nothing to disconnect" and "a real
    /// disconnect" leave the same tick behind.
    /// </para>
    /// <para>
    /// The handle test here is <c>&lt;= 0</c> where the connect path used <c>&lt;&gt; 0</c>
    /// [<c>:L115</c>]. Both are reproduced as written; they differ only for a negative handle, a case
    /// the oracle left open and this port does not close.
    /// </para>
    /// </remarks>
    public long Disconnect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // [:L145-L148] The no-op fast path. The LESS-OR-EQUAL spelling, preserved.
        if (_engine.DbHandle <= 0 || _broken)
        {
            _lastConnectionOkTicks = 0;
            return RetCode.OK;
        }

        // [:L150-L152]
        _hooks.OnBeforeDisconnect();
        _state = _engine.Disconnect();
        _hooks.OnAfterDisconnect();

        // [:L154] After the hooks, not before.
        _lastConnectionOkTicks = 0;

        // [:L156-L158]
        return _state.SqlCode != 0 ? RetCode.E_DB_ERROR : RetCode.OK;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The oracle [<c>:L185-L191</c>]: <c>if AutoCommit then return RetCode.FAILED</c>, then
    /// <c>if _bBroken then return RetCode.E_INVALID_TRANSACTION</c>, then the state-preserving
    /// rollback, then <see cref="RetCode.OK"/>.
    /// </para>
    /// <para>
    /// <b>ROLLBACK UNDER AUTO-COMMIT REPORTS FAILURE. IT IS NOT A HARMLESS NO-OP</b> [<c>:L185</c>].
    /// A caller may very reasonably expect "there is no transaction to roll back, so nothing happened,
    /// so this succeeded" - the legacy says <see cref="RetCode.FAILED"/> instead, and the order matters
    /// too: the auto-commit test comes BEFORE the broken test, so an auto-commit connection that is
    /// also broken reports <see cref="RetCode.FAILED"/> rather than
    /// <see cref="RetCode.E_INVALID_TRANSACTION"/>.
    /// </para>
    /// </remarks>
    public long Rollback()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // [:L185] FAILED, not OK. Tested before the broken flag, which is observable.
        if (_engine.AutoCommit)
        {
            return RetCode.FAILED;
        }

        // [:L186]
        if (_broken)
        {
            return RetCode.E_INVALID_TRANSACTION;
        }

        // [:L188]
        CleanRollback();

        // [:L190]
        return RetCode.OK;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The oracle [<c>:L240-L257</c>]: the auto-commit test, the broken test, the before-hook, the
    /// commit, the after-hook, and then <c>if SQLCode &lt;&gt; 0 then { if autoRollback then
    /// _of_CleanRollback() } return RetCode.E_DB_ERROR</c>. As with <see cref="Connect"/>, the state
    /// test comes AFTER the after-hook, so a hook that spoils a clean state turns a successful commit
    /// into a database error and - when auto-rollback was asked for - into a rollback as well.
    /// </remarks>
    public long Commit(bool autoRollback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // [:L240] Same order as Rollback: auto-commit before broken.
        if (_engine.AutoCommit)
        {
            return RetCode.FAILED;
        }

        // [:L241]
        if (_broken)
        {
            return RetCode.E_INVALID_TRANSACTION;
        }

        // [:L243-L247]
        _hooks.OnBeforeCommit();
        _state = _engine.Commit();
        _hooks.OnAfterCommit();

        // [:L249-L254]
        if (_state.SqlCode != 0)
        {
            if (autoRollback)
            {
                CleanRollback();
            }

            return RetCode.E_DB_ERROR;
        }

        // [:L256]
        return RetCode.OK;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The oracle is the one-line delegation <c>return of_Commit(true)</c> [<c>:L383-L384</c>], so
    /// auto-rollback is ON by default. A caller that wants a commit which does NOT roll back on failure
    /// has to say so explicitly through <see cref="Commit(bool)"/>.
    /// </remarks>
    public long Commit() => Commit(true);

    /// <inheritdoc/>
    /// <remarks>
    /// The oracle, verbatim [<c>:L370-L381</c>]:
    /// <code>
    /// if SQLCode &lt;&gt; 0 then                                                [:L370]
    ///     if Not AutoCommit then _of_CleanRollback()                            [:L371-L373]
    ///     return RetCode.E_DB_ERROR                                             [:L374]
    /// end if
    /// if Not AutoCommit then return of_Commit(true)                             [:L376-L378]
    /// return RetCode.OK                                                         [:L380]
    /// </code>
    /// <para>
    /// It inspects the state left behind by whatever ran before it rather than performing an operation
    /// of its own first, which is what makes it a CHECKPOINT: the caller runs statements, then calls
    /// this, and this decides between commit, rollback and nothing at all. The auto-commit test is
    /// INVERTED relative to <see cref="Commit(bool)"/>'s - here an auto-commit connection is the arm
    /// that succeeds trivially [<c>:L380</c>], because the provider already committed each statement.
    /// </para>
    /// </remarks>
    public long AutoCommitCheckpoint()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // [:L370] The state carries an error already.
        if (_state.SqlCode != 0)
        {
            // [:L371-L373] Roll back only when there is a transaction to roll back.
            if (!_engine.AutoCommit)
            {
                CleanRollback();
            }

            return RetCode.E_DB_ERROR;
        }

        // [:L376-L378] Note the delegation passes true, so a failed commit here rolls back.
        if (!_engine.AutoCommit)
        {
            return Commit(true);
        }

        // [:L380] Auto-commit: the provider already committed, so there is nothing to do.
        return RetCode.OK;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The oracle, verbatim [<c>:L220-L238</c>]:
    /// <code>
    /// if sqlCmd = "" or IsNull(sqlCmd) then return RetCode.E_INVALID_ARGUMENT   [:L220]
    /// of_ClearState()                                                           [:L222]
    /// if IsPrevented(Event OnBeforeCommand(sqlCmd)) then                        [:L224]
    ///     if SQLCode &lt;&gt; 0 then return RetCode.E_DB_ERROR                  [:L225]
    ///     return RetCode.CANCELLED                                              [:L226]
    /// end if
    /// EXECUTE IMMEDIATE :sqlCmd USING this;                                     [:L229]
    /// Event OnAfterCommand(sqlCmd)                                              [:L231]
    /// if SQLCode &lt;&gt; 0 and SQLCode &lt;&gt; 100 then                       [:L233]
    ///     return RetCode.E_DB_ERROR                                             [:L234]
    /// end if
    /// return RetCode.OK                                                         [:L237]
    /// </code>
    /// <para>
    /// <b><c>SQLCode = 100</c> - "no data found" - READS AS A SUCCESS</b> [<c>:L233</c>]. That is the
    /// single most easily lost line in this method: an implementation that tested only
    /// <c>SQLCode != 0</c> would report a statement that matched no rows as a database error.
    /// </para>
    /// <para>
    /// C-B: the legacy tests the empty string BEFORE the null [<c>:L220</c>], which in PowerScript is
    /// significant ordering and in C# is not, since both collapse into one
    /// <see cref="string.IsNullOrEmpty(string)"/> call with identical results. The collapse is
    /// recorded rather than silent. Whitespace is NOT rejected: the legacy tests emptiness, not
    /// blankness, so a statement of spaces reaches the engine exactly as it does in the oracle.
    /// </para>
    /// </remarks>
    public long Exec(string? sqlCommand, CancellationToken cancellationToken = default) =>
        string.IsNullOrEmpty(sqlCommand)
            // [:L220] Empty OR null, tested BEFORE the wrap so a null never reaches the command type's
            // own argument validation. IsNullOrEmpty and NOT IsNullOrWhiteSpace: the oracle tests
            // emptiness, so a statement of spaces still reaches the engine.
            ? RetCode.E_INVALID_ARGUMENT
            : Exec(SqlCommandText.FromRenderedStatement(sqlCommand), cancellationToken);

    /// <inheritdoc/>
    public long Exec(in SqlCommandText command, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // [:L220] The guard reads the CANONICAL text, because that is the statement the provider is
        // asked to run. The rendered text is a parity artefact and never decides an arm.
        if (string.IsNullOrEmpty(command.CanonicalText))
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // THE CANCELLATION ARM, placed AFTER the argument guard and BEFORE the state clear and the
        // before-command hook. After the argument guard because a malformed request is malformed whether
        // or not the caller is still there, and a caller that sent an empty statement should be told so.
        // Before the clear and the hook because both are observable side effects, and a statement that is
        // never issued must leave no trace that it was considered. See ObserveCancellation.
        if (ObserveCancellation(cancellationToken))
        {
            return RetCode.CANCELLED;
        }

        // The hooks see the statement the oracle's own handlers would have seen - the interpolated form.
        string observable = command.RenderedText;

        // [:L222] Before the hook, so the hook's own state is what the veto discrimination reads.
        ClearState();

        // [:L224]
        if (Predicates.IsPrevented(_hooks.OnBeforeCommand(observable)))
        {
            // [:L225-L226] The same veto discrimination as Connect's.
            return _state.SqlCode != 0 ? RetCode.E_DB_ERROR : RetCode.CANCELLED;
        }

        // [:L229] BOUND, NOT SPLICED (AAP 0.6.4).
        _state = _engine.Execute(in command, cancellationToken);

        // [:L231] Fires on failure as well as on success.
        _hooks.OnAfterCommand(observable);

        // [:L233-L235] 100 IS A SUCCESS. Do not simplify this to a single != 0 test.
        if (_state.SqlCode != 0 && _state.SqlCode != 100)
        {
            return RetCode.E_DB_ERROR;
        }

        // [:L237]
        return RetCode.OK;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The oracle, verbatim [<c>:L193-L218</c>]:
    /// <code>
    /// if _nLastConnOK = 0 or _bBroken then return false                         [:L196]
    /// if IsFailed(Event OnCheck()) then return false                            [:L197]
    /// if CPU() - _nLastConnOK &lt; 10000 then return true                       [:L198]
    /// rtCode = Event OnTest()                                                   [:L200]
    /// if IsNull(rtCode) then                                                    [:L201]
    ///     if Pos(Upper(DBMS),"ORACLE") &gt; 0 then                              [:L202]
    ///         EXECUTE IMMEDIATE "SELECT 1 FROM DUAL" USING this;                [:L203]
    ///     else
    ///         EXECUTE IMMEDIATE "SELECT 1" USING this;                          [:L205]
    ///     end if
    ///     bConnected = (SQLNRows &gt; 0)                                        [:L207]
    /// else
    ///     bConnected = IsSucceeded(rtCode)                                      [:L209]
    /// end if
    /// if bConnected then _nLastConnOK = CPU() else _nLastConnOK = 0             [:L211-L215]
    /// return bConnected                                                         [:L217]
    /// </code>
    /// <para>
    /// <b>THREE THINGS HERE ARE EASY TO GET WRONG.</b> First, the two hook results ARE judged by the
    /// shared kernel's return-code predicates [<c>:L197, :L209</c>] - unlike the provider-state tests
    /// elsewhere in this class - because they are return codes; that contrast is the clearest example
    /// in the file of why the two predicate families are named apart. Second, the built-in probe's
    /// verdict is <c>SQLNRows &gt; 0</c> and NOT a <c>SQLCode</c> test [<c>:L207</c>], so a probe that
    /// returned no rows without erroring reads as not connected. Third, the check hook runs BEFORE the
    /// cache short-circuit [<c>:L197</c> then <c>:L198</c>], so it is consulted on every call however
    /// warm the cache is - which is what lets a health check condemn a connection promptly.
    /// </para>
    /// </remarks>
    public bool IsConnected() => IsConnected(out _);

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The single implementation of the sequence; the parameterless overload delegates here and
    /// discards the flag. <b>The flag is set from the CONTROL FLOW that already exists rather than from
    /// any new decision</b>, so this overload adds no behaviour - it only reports which of the arms
    /// below was taken.
    /// </para>
    /// <para>
    /// The two early refusals report <see langword="false"/> for the flag as well as for the verdict,
    /// which is correct rather than merely convenient: neither of them touches the connection either.
    /// </para>
    /// </remarks>
    public bool IsConnected(out bool probed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // No arm below this point has interrogated the connection yet.
        probed = false;

        // [:L196] The zero sentinel means "never connected OK".
        if (_lastConnectionOkTicks == 0 || _broken)
        {
            return false;
        }

        // [:L197] A HOOK RESULT, so the shared kernel's return-code predicate is correct here - note
        // that under it CANCELLED is NOT a failure, so a hook returning -2 does not report a dead
        // connection. Preserved, not corrected.
        if (Predicates.IsFailed(_hooks.OnCheck()))
        {
            return false;
        }

        // [:L198] CLOCK 2. Strictly less than, so a delta of exactly the window re-probes. THE ANSWER
        // COMES FROM THE CACHE AND NOT FROM THE CONNECTION, which is what `probed` reports: this arm is
        // observable only by the absence of the probe below it.
        if (ReadTicks() - _lastConnectionOkTicks < LivenessCacheWindowMilliseconds)
        {
            return true;
        }

        // Past the short-circuit, one of the two probe paths below always runs.
        probed = true;

        // [:L200]
        long? hookResult = _hooks.OnTest();

        bool connected;
        if (hookResult is null)
        {
            // [:L201-L206] The dialect probe. C-E: this travels down an already-open connection the
            // injected engine supplied; it opens nothing and provisions nothing.
            _state = _engine.Execute(IsOracleDialect() ? OracleLivenessProbe : DefaultLivenessProbe);

            // [:L207] ROW COUNT, not SQLCode.
            connected = _state.SqlNRows > 0;
        }
        else
        {
            // [:L209] Another hook result, so the kernel predicate again - under which a prevention
            // (1) reads as connected.
            connected = Predicates.IsSucceeded(hookResult);
        }

        // [:L211-L215] Refresh on success, zero on failure.
        _lastConnectionOkTicks = connected ? ReadTicks() : 0;

        // [:L217]
        return connected;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The oracle [<c>:L530-L534</c>]: <c>if Not _bBroken then Event OnCheck()</c> then
    /// <c>return _bBroken</c>.
    /// <para>
    /// <b>THE HOOK CALL IS THE SIDE EFFECT AND IT IS NOT OPTIONAL.</b> The hook may call
    /// <see cref="SetBroken"/>, so the flag is re-read AFTER it rather than captured before - which is
    /// what makes "ask whether it is broken" able to answer <see langword="true"/> on a transaction
    /// that was healthy a statement earlier. The hook is skipped once the flag is already set, so a
    /// condemned transaction is never re-checked.
    /// </para>
    /// <para>
    /// The hook result is DISCARDED here, unlike in <see cref="IsConnected"/> where it is tested
    /// [<c>:L197</c>]. Same hook, two different treatments, both reproduced.
    /// </para>
    /// </remarks>
    public bool IsBroken()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // [:L530-L532] The result is deliberately ignored; only its effect on the flag matters.
        if (!_broken)
        {
            _ = _hooks.OnCheck();
        }

        // [:L533] Re-read, because the hook may have set it.
        return _broken;
    }

    /// <inheritdoc/>
    /// <remarks>The oracle [<c>:L526-L528</c>] sets the flag and returns OK unconditionally.</remarks>
    public long SetBroken()
    {
        // Deliberately NOT guarded by ObjectDisposedException: condemning an object is meaningful at
        // any point in its life, including from a hook that runs during teardown, and the legacy has
        // no failure arm here at all [:L527].
        _broken = true;
        return RetCode.OK;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The oracle [<c>:L363-L368</c>] assigns zero, zero, zero, empty and empty - exactly
    /// <see cref="SqlState.Cleared"/>. It does NOT touch the broken flag and does NOT touch the
    /// liveness tick, so clearing state cannot resurrect a condemned transaction.
    /// </remarks>
    public void ClearState() => _state = SqlState.Cleared;

    /// <inheritdoc/>
    /// <remarks>
    /// The oracle [<c>:L356-L361</c>]. <b>Everything that is not Oracle is MSSQL, SQLite included</b>,
    /// and an empty DBMS string classifies as MSSQL too because the substring test simply fails.
    /// </remarks>
    public DatabaseType GetDbType() =>
        IsOracleDialect() ? DatabaseType.DbtOracle : DatabaseType.DbtMssql;

    /// <inheritdoc/>
    /// <remarks>
    /// Delegates the SEVEN-field fold to <see cref="ITransactionEngine.ApplyConnectionFields"/> and
    /// then returns <see cref="RetCode.OK"/> [<c>:L353</c>].
    /// <para>
    /// <b>WHERE THE VETO HOOK WENT.</b> The legacy tests <c>Event OnSetTransData(data) = 1</c>
    /// [<c>:L343</c>] - a LITERAL <c>1</c>, not the prevention predicate, so a hook returning <c>2</c>
    /// does not veto. That hook belongs to <c>Transactions/TransactionData.cs</c>, which already
    /// implements it as <c>SetTransactionDataHook</c> together with the seven-field fold and the exact
    /// literal comparison. Re-implementing either here would be a second copy of one rule, so this
    /// method carries neither: a caller that needs the veto composes it where the descriptor lives.
    /// </para>
    /// <para>
    /// C-F: the credential passes through this call into the engine and is observable nowhere else.
    /// This class never reads it.
    /// </para>
    /// </remarks>
    public long ApplyTransactionData(in TransactionData descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // [:L345-L351] The seven fields, folded by the descriptor's own accessor.
        _engine.ApplyConnectionFields(in descriptor);

        // [:L353]
        return RetCode.OK;
    }

    /// <inheritdoc/>
    /// <remarks>The oracle [<c>:L337</c>] is the bare sign test <c>return (SQLCode &lt; 0)</c>.</remarks>
    public bool IsSqlFailed() => _state.SqlCode < 0;

    /// <inheritdoc/>
    /// <remarks>
    /// The oracle [<c>:L340</c>] is <c>return (SQLCode &gt;= 0)</c>, which is why <c>100</c> reads as a
    /// success. The two predicates are exact complements over every input, unlike the shared kernel's
    /// pair, whose tri-state hole leaves CANCELLED and null in neither.
    /// </remarks>
    public bool IsSqlSucceeded() => _state.SqlCode >= 0;

    /// <inheritdoc/>
    /// <remarks>
    /// The engine handed in at construction, never a copy and never null: this type refuses a null engine
    /// in its constructor, so a pooled transaction that exists has one.
    /// </remarks>
    public ITransactionEngine ResolveEngine() => _engine;

    /// <inheritdoc/>
    public DbErrorData CaptureError() =>
        // [n_cst_thread_task_sqlbase.sru:L175-L176] SQLDBCode and SQLErrText, and nothing else.
        DbErrorData.FromTransaction(_state.SqlDbCode, _state.SqlErrText);

    /// <inheritdoc/>
    /// <remarks>
    /// A plain cast, deliberately. The probe exists so that the layer owning the storage provider can ask
    /// for its own capability by interface without this type naming it; adding registration, caching or a
    /// capability table here would give the pooling layer knowledge of a set it has no business knowing.
    /// A disposed transaction refuses rather than handing out an engine it has already released.
    /// </remarks>
    public bool TryGetEngineCapability<TCapability>(
        [NotNullWhen(true)] out TCapability? capability)
        where TCapability : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        capability = _engine as TCapability;

        return capability is not null;
    }

    /// <summary>
    /// Releases the engine. The port of <c>Destroy</c> on the legacy transaction object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>IDEMPOTENT BY REQUIREMENT, NOT BY COURTESY.</b> The pool disposes an entry's transaction from
    /// four sites and then disposes every survivor again on its own disposal, so a second call must be
    /// a no-op. It is also deliberately SILENT about connection state: the legacy <c>Destroy</c> does
    /// not disconnect, and the pool decides whether a disconnect precedes a destroy - guarded in
    /// <c>RemoveRef</c> [<c>n_cst_thread_trans_pool.sru:L105-L108</c>], unguarded in <c>RemoveAll</c>
    /// [<c>:L197-L198</c>] and <c>Collect</c> [<c>:L217-L218</c>], and NOT AT ALL on <c>Get</c>'s
    /// broken-object path [<c>:L164</c>]. Disconnecting from here would silently add a fourth
    /// behaviour and destroy that third distinction.
    /// </para>
    /// <para>
    /// The engine is disposed because this object owns it: it was handed in at construction and is
    /// referenced by nothing else.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // No disconnect here - see the remark above. The engine's own disposal is what closes any
        // handle that is still open, which is the same thing PowerBuilder's Destroy leaves the runtime
        // to do.
        _engine.Dispose();
    }

    /// <summary>
    /// Whether the caller has gone, for the verbs that are allowed to abandon their work.
    /// </summary>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns><see langword="true"/> when the verb must not proceed.</returns>
    /// <remarks>
    /// <para>
    /// <b>=================== THE CANCELLATION POLICY FOR THIS WHOLE FILE ===================</b>
    /// Two of this type's verbs take a token and the rest deliberately do not. The dividing line is not
    /// convenience, it is whether abandoning the verb can leave work half-done:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///   <b><see cref="Connect"/> and <see cref="Exec(in SqlCommandText, CancellationToken)"/> DO take a
    ///   token.</b> Each ISSUES NEW WORK on the caller's behalf, and nothing has been started when the arm
    ///   is reached, so abandoning leaves the database exactly as it was. They answer
    ///   <see cref="RetCode.CANCELLED"/>, which is the oracle's own code for a task the caller stopped
    ///   [<c>of_IsCancelled</c>] and which the tri-state algebra classifies as NEITHER succeeded nor failed
    ///   - so a cancelled verb is not misread as a database error by any predicate in the kernel.
    ///   </description></item>
    ///   <item><description>
    ///   <b>Commit, rollback, the auto-commit checkpoint and disconnect do NOT take one.</b> Each COMPLETES
    ///   OR UNDOES work already begun. Abandoning a commit or a rollback leaves a unit of work neither
    ///   applied nor reverted and a connection neither returned nor closed - strictly worse for the caller
    ///   that cancelled than finishing the compensating step. The oracle reaches the same outcome by a
    ///   different route: its task polls <c>of_IsCancelled()</c> at documented points and its epilogue then
    ///   ROLLS BACK, so cancellation still discards the work - through the rollback, not by refusing it.
    ///   </description></item>
    ///   <item><description>
    ///   <b>The liveness probe does NOT take one either, and that is the least obvious of the three.</b>
    ///   A cancelled probe would have to report SOMETHING, and the only thing it could report is
    ///   "not connected" - which zeroes the liveness stamp and invites the pool to condemn, destroy and
    ///   recreate a perfectly healthy transaction [<c>n_cst_thread_trans_pool.sru:L158-L172</c>]. A
    ///   cancellation must not be able to reach through a read and damage shared pooled state.
    ///   </description></item>
    /// </list>
    /// <para>
    /// The token is observed and NOT thrown on: this type answers codes and never raises, which is what
    /// lets every caller in the SQL task layer keep the oracle's <c>if IsFailed(...)</c> shape.
    /// </para>
    /// </remarks>
    private static bool ObserveCancellation(CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested;

    /// <summary>
    /// Reads CLOCK 2 - the liveness cache's clock - as MONOTONIC elapsed milliseconds since this
    /// transaction was constructed, through the injected provider.
    /// </summary>
    /// <returns>Monotonic elapsed milliseconds, offset by <see cref="MonotonicOffset"/>.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE ONLY CLOCK READ IN THIS CLASS.</b> There is no <c>DateTime.UtcNow</c>, no
    /// <c>DateTime.Now</c>, no <c>Environment.TickCount</c> and no <c>Stopwatch</c> anywhere here, and
    /// adding one would break the characterization requirement that every clock read be maskable from
    /// both the master and the candidate (AAP 0.6.7).
    /// </para>
    /// <para>
    /// <b>MONOTONIC, NOT WALL-CLOCK, AND THAT IS THE POINT.</b> The legacy reads <c>CPU()</c>, a
    /// process-relative counter that cannot move backwards, and the only comparison performed on this
    /// value is a DELTA against a stored stamp [<c>:L198</c>]. A UTC instant can step in either
    /// direction under an NTP correction or a manual clock change; a backward step would make the cache
    /// window appear to have been re-entered and a large forward step would short-circuit past it. The
    /// timestamp is differenced against an origin captured at construction, so neither is reachable.
    /// </para>
    /// <para>
    /// Only DELTAS and the zero sentinel are ever compared, so the origin itself is unobservable and the
    /// constant offset below is invisible to every comparison; see the width discussion in this file's
    /// header.
    /// </para>
    /// </remarks>
    private long ReadTicks() =>
        (long)_timeProvider.GetElapsedTime(_monotonicOrigin, _timeProvider.GetTimestamp())
            .TotalMilliseconds
        + MonotonicOffset;

    /// <summary>
    /// The offset applied to every monotonic stamp so a stamp can never collide with the zero sentinel.
    /// </summary>
    /// <remarks>
    /// Zero is the "never connected OK" sentinel tested at [<c>:L196</c>] and written at [<c>:L214</c>].
    /// A monotonic reading taken in the first millisecond of this object's life would otherwise be zero
    /// and read as that sentinel. A constant offset is invisible to the only comparison performed - a
    /// delta - so it removes the collision without moving the cache window by a millisecond.
    /// </remarks>
    private const long MonotonicOffset = 1L;

    /// <summary>
    /// The <c>Pos(Upper(DBMS),"ORACLE") &gt; 0</c> test, shared by the dialect resolver [<c>:L356</c>]
    /// and the liveness probe [<c>:L202</c>].
    /// </summary>
    /// <returns><see langword="true"/> when the DBMS identifier names Oracle.</returns>
    /// <remarks>
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> reproduces <c>Upper()</c> followed by a
    /// substring search without allocating the upper-cased copy, and ORDINAL rather than culture-aware
    /// because these are ASCII provider tokens - a culture-aware comparison would make the dialect
    /// depend on the container's locale.
    /// </remarks>
    private bool IsOracleDialect() =>
        _engine.Dbms.Contains(OracleMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Stamps the liveness tick and clears the broken flag, then notifies the hook. The port of
    /// <c>Event OnConnOK</c>'s own script [<c>:L107-L109</c>].
    /// </summary>
    private void MarkConnectionOk()
    {
        // [:L107] CLOCK 2 again.
        _lastConnectionOkTicks = ReadTicks();

        // [:L108] A confirmed connection is the ONLY thing that clears this flag.
        _broken = false;

        // The notification runs last, so an override cannot discard either effect above.
        _hooks.OnConnectionOk();
    }

    /// <summary>
    /// The state-preserving rollback - <c>_of_cleanrollback</c> [<c>:L163-L183</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE RESTORE IS THE WHOLE POINT AND ITS POSITION IS CONTRACT.</b> The five values are
    /// snapshotted, the before-hook fires, the rollback runs, THE FIVE VALUES ARE PUT BACK, and only
    /// then does the after-hook fire [<c>:L166-L182</c>]. So the caller still reads the error that
    /// caused the rollback rather than the rollback's own outcome, and the after-hook sees the original
    /// state too. A "cleaner" rollback that let the statement's state stand would destroy the only
    /// diagnostic the caller has - it is the clearest example in this folder of a legacy behaviour a
    /// tidier implementation would silently lose.
    /// </para>
    /// <para>
    /// A consequence worth stating: A FAILING ROLLBACK IS INVISIBLE. Its own error code is overwritten
    /// by the restore, so neither this method nor its callers can report it, and the legacy declares it
    /// as a subroutine with no return value at all [<c>:L163</c>] - which is why this method is
    /// <see langword="void"/> rather than returning a code nobody could act on.
    /// </para>
    /// </remarks>
    private void CleanRollback()
    {
        // [:L166-L170] The snapshot, as ONE value rather than five locals - see SqlState.
        SqlState preserved = _state;

        // [:L172] Observes the state as the FAILING operation left it.
        _hooks.OnBeforeRollback();

        // [:L174]
        _state = _engine.Rollback();

        // [:L176-L180] THE RESTORE. Between the statement and the after-hook, exactly as measured.
        _state = preserved;

        // [:L182] Observes the RESTORED state.
        _hooks.OnAfterRollback();
    }

    /// <summary>
    /// The state-preserving disconnect - <c>_of_cleandisconnect</c> [<c>:L504-L524</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// Structurally identical to <see cref="CleanRollback"/> with the disconnect verb and the
    /// disconnect hooks substituted: snapshot [<c>:L507-L511</c>], before-hook [<c>:L513</c>],
    /// statement [<c>:L515</c>], restore [<c>:L517-L521</c>], after-hook [<c>:L523</c>]. Both share the
    /// one <see cref="SqlState"/> type so the two five-value tuples cannot drift apart, which is the
    /// reason that type exists.
    /// </para>
    /// <para>
    /// <b>IT IS UNCONDITIONAL, UNLIKE THE PUBLIC DISCONNECT.</b> There is no handle test and no broken
    /// test here [<c>:L504-L515</c>], because its one caller has already established that a connection
    /// genuinely opened [<c>:L134</c>]. Routing it through <see cref="Disconnect"/> instead would add
    /// that fast path, fire the wrong hooks and - worse - let the disconnect's state stand.
    /// </para>
    /// </remarks>
    private void CleanDisconnect()
    {
        // [:L507-L511]
        SqlState preserved = _state;

        // [:L513]
        _hooks.OnBeforeDisconnect();

        // [:L515] No handle test, no broken test - see the remark above.
        _state = _engine.Disconnect();

        // [:L517-L521] THE RESTORE, before the after-hook.
        _state = preserved;

        // [:L523]
        _hooks.OnAfterDisconnect();
    }

    /// <summary>
    /// The hook set used when a caller supplies none - the port of a PowerBuilder object with no script
    /// for any of the thirteen events.
    /// </summary>
    /// <remarks>
    /// A singleton, because it is stateless and every arm is the interface's own default. Declaring it
    /// at all - rather than making the field nullable and null-checking at thirteen call sites - is what
    /// keeps <see cref="PooledTransaction"/>'s bodies readable and, more importantly, what makes it
    /// impossible to forget a null check on the fourteenth.
    /// </remarks>
    private sealed class NoOpHooks : IPooledTransactionHooks
    {
        /// <summary>The single shared instance.</summary>
        internal static readonly NoOpHooks Instance = new();

        private NoOpHooks()
        {
        }
    }
}

// --------------------------------------------------------------------------------------------------
//  PART 5 OF 6 - CLASS-NAME RESOLUTION AND ACTIVATION
// --------------------------------------------------------------------------------------------------

/// <summary>
/// The caller-supplied resolver consulted FIRST for the pooled transaction's class name - the port of
/// <c>Event OnGetTransClsName()</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L24, :L82</c>].
/// </summary>
/// <returns>
/// A class name, or <see langword="null"/> or an empty string to fall through to the configured
/// <c>TransactionPoolOptions.TransactionClassName</c>.
/// </returns>
/// <remarks>
/// <para>
/// <b>THE ORDER IS CONTRACT AND IT IS EVENT-FIRST.</b> The legacy assigns the event's answer and only
/// then, when that answer is null or empty, reads the configuration key:
/// <c>_sTransCls = Event OnGetTransClsName(); if IsNull(_sTransCls) or _sTransCls = "" then _sTransCls
/// = ...of_GetDataString("$SQL.TransPool.TransClass")</c> [<c>:L82-L83</c>]. Configuration is the
/// FALLBACK, not the primary. An implementation that read configuration first and let a hook override
/// it would invert a documented precedence.
/// </para>
/// <para>
/// Modelled as a delegate for the same reason <c>TransactionData</c>'s hooks are: the legacy construct
/// is a PowerBuilder user event with at most one implementation, so a single optional callback is the
/// faithful shape. A multicast <see langword="event"/> would introduce a subscriber-ordering question
/// the legacy does not have. <see langword="null"/> - no resolver supplied - behaves exactly as an
/// unimplemented event, which returns an empty string.
/// </para>
/// <para>
/// It is consulted ONCE, at pool construction, because the legacy consults it once in <c>oninit</c>
/// [<c>:L82</c>] and caches the answer in a field [<c>:L57</c>]. A resolver that returned different
/// answers over time would therefore have no effect after startup, which is stated here so nobody
/// designs one that expects otherwise.
/// </para>
/// </remarks>
internal delegate string? TransactionClassNameResolver();

/// <summary>
/// Creates pooled transactions, either the default implementation or one named by class name.
/// </summary>
/// <remarks>
/// <para>
/// Separated from the pool so that the pool's own logic - reference counting, index guards, expiry -
/// is testable without any creation concern at all, and so that a caller can supply whatever creation
/// strategy its deployment needs. The pool holds one of these and calls exactly two methods on it.
/// </para>
/// <para>
/// <b>FAILURE IS REPORTED BY THROWING, DELIBERATELY.</b> The legacy creation site sits inside a
/// <c>try ... catch(throwable ex)</c> whose catch arm returns <see cref="RetCode.E_INVALID_OBJECT"/>
/// [<c>n_cst_thread_trans_pool.sru:L156, :L173-L175</c>], so an exception is exactly how a failed
/// <c>Create Using</c> reaches that arm. An activator that returned <see langword="null"/> instead
/// would need a second, parallel failure path in the pool that the oracle does not have.
/// </para>
/// </remarks>
internal interface IPooledTransactionActivator
{
    /// <summary>
    /// Creates the default pooled transaction - the port of <c>Create n_cst_thread_trans</c>
    /// [<c>n_cst_thread_trans_pool.sru:L169</c>].
    /// </summary>
    /// <returns>A new pooled transaction. Never <see langword="null"/>.</returns>
    IPooledTransaction CreateDefault();

    /// <summary>
    /// Creates a pooled transaction named by class name - the port of <c>Create Using _sTransCls</c>
    /// [<c>n_cst_thread_trans_pool.sru:L167</c>].
    /// </summary>
    /// <param name="className">The class name, neither <see langword="null"/> nor empty.</param>
    /// <returns>A new pooled transaction. Never <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The name cannot be resolved, does not implement <see cref="IPooledTransaction"/>, or cannot be
    /// constructed. The pool converts this to <see cref="RetCode.E_INVALID_OBJECT"/>.
    /// </exception>
    IPooledTransaction Create(string className);
}

/// <summary>
/// The default <see cref="IPooledTransactionActivator"/>: a registry of named factories with a
/// HARDENED type-name fallback.
/// </summary>
/// <remarks>
/// <para>
/// <b>TWO RESOLUTION MECHANISMS, IN THIS ORDER, AND THE FIRST ONE IS THE ONE A DEPLOYMENT SHOULD
/// USE.</b>
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// A REGISTRY of named factories supplied at construction by <c>Program.cs</c>. This is the
/// dependency-injection path: a deployment registers the implementations it is willing to use under
/// whatever names it likes, and a configured name that is not in the registry simply is not
/// available. Nothing is reflected over and nothing is loaded.
/// </description>
/// </item>
/// <item>
/// <description>
/// A TYPE-NAME fallback, resolved <b>only inside this service's own assembly</b>. This is what
/// reproduces <c>Create Using</c> for a type that ships with the service - a future engine-specific
/// transaction under <c>Data/</c>, for instance - without a registry entry.
/// </description>
/// </item>
/// </list>
/// <para>
/// <b>WHY THE FALLBACK IS CONFINED TO ONE ASSEMBLY, STATED PRECISELY BECAUSE IT IS A SECURITY
/// BOUNDARY.</b> Resolving a type from a name that arrived in configuration is, unconstrained, a route
/// from "a writable settings source" to "arbitrary code executing inside the only process that holds a
/// storage provider". Four constraints close that route, and all four are enforced rather than
/// documented:
/// </para>
/// <list type="bullet">
/// <item><description>
/// AN ASSEMBLY-QUALIFIED NAME IS REJECTED OUTRIGHT - any name containing a comma - so no name can ever
/// cause an assembly to be located or loaded.
/// </description></item>
/// <item><description>
/// RESOLUTION IS PERFORMED AGAINST THIS ASSEMBLY ONLY, through
/// <see cref="Assembly.GetType(string, bool, bool)"/> on the assembly that declares this activator.
/// A name that does not identify a type shipped in this service cannot be resolved at all.
/// </description></item>
/// <item><description>
/// THE TYPE MUST IMPLEMENT <see cref="IPooledTransaction"/> and must be a concrete, non-abstract,
/// non-generic class. That interface is <see langword="internal"/>, so the reachable candidate set is
/// bounded by this assembly's own internals.
/// </description></item>
/// <item><description>
/// ONLY THREE CONSTRUCTOR SHAPES ARE ACCEPTED, all of them taking dependencies this activator already
/// holds. No arbitrary constructor is invoked and no property is set.
/// </description></item>
/// </list>
/// <para>
/// <b>A NOTE FOR WHOEVER READS <c>TransactionPoolOptions.TransactionClassName</c> ALONGSIDE THIS.</b>
/// That property's documentation was written before this file existed and states that the name is
/// never resolved into a type anywhere in this service. It is resolved here, under the four
/// constraints above, because reproducing <c>Create Using _sTransCls</c> [<c>:L167</c>] is part of the
/// ported behaviour and because the empty-versus-non-empty branch at that line is observable. The
/// posture that documentation describes is still available and is still the recommended one: leave the
/// property empty and register an implementation, or register it under a name in the registry, and the
/// type-name fallback is never reached. The property is deliberately still NOT validated as a type name
/// at startup, for the reason given there - validating it would imply it must always be loadable, and a
/// registry name is not a type name.
/// </para>
/// <para>
/// C-D: this is BCL type activation and nothing else. There is no script bridge, no dynamic-invocation
/// shim and no expression compilation. AAP 0.2.1.4 establishes that the legacy's <c>n_scriptinvoker</c>
/// usage is a variadic-call escape hatch with no analogue to port, and ScriptBridge is a deferred
/// service that must not be touched even partially.
/// </para>
/// <para>
/// C-F: no exception message produced here contains a descriptor, a descriptor field or any credential.
/// The only caller-supplied value that appears in a message is the CLASS NAME itself, which is
/// configuration rather than data, and which a caller must be able to see in order to correct it.
/// </para>
/// </remarks>
internal sealed class PooledTransactionActivator : IPooledTransactionActivator
{
    /// <summary>
    /// The character whose presence marks a name as assembly-qualified, and therefore as rejected.
    /// </summary>
    private const char AssemblyQualifiedSeparator = ',';

    private readonly Func<ITransactionEngine> _engineFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IPooledTransactionHooks? _hooks;
    private readonly IReadOnlyDictionary<string, Func<IPooledTransaction>> _namedFactories;

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledTransactionActivator"/> class.
    /// </summary>
    /// <param name="engineFactory">
    /// Produces a fresh <see cref="ITransactionEngine"/> for each transaction created. Its SQLite
    /// implementation belongs to <c>Data/SqliteConnectionFactory.cs</c> (C-E); nothing here composes a
    /// connection string.
    /// </param>
    /// <param name="timeProvider">
    /// The injected clock handed to every transaction created - the same seam the pool uses, so one
    /// substitution masks both clocks.
    /// </param>
    /// <param name="hooks">
    /// The hooks handed to every transaction created, or <see langword="null"/> for the no-op set.
    /// </param>
    /// <param name="namedFactories">
    /// The registry consulted before the type-name fallback, or <see langword="null"/> for none.
    /// Compared case-insensitively with ordinal semantics, because a class name is an ASCII token and a
    /// culture-aware comparison would make resolution depend on the container's locale.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="engineFactory"/> or <paramref name="timeProvider"/> is <see langword="null"/>.
    /// </exception>
    public PooledTransactionActivator(
        Func<ITransactionEngine> engineFactory,
        TimeProvider timeProvider,
        IPooledTransactionHooks? hooks = null,
        IReadOnlyDictionary<string, Func<IPooledTransaction>>? namedFactories = null)
    {
        ArgumentNullException.ThrowIfNull(engineFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _engineFactory = engineFactory;
        _timeProvider = timeProvider;
        _hooks = hooks;
        _namedFactories = namedFactories
            ?? new Dictionary<string, Func<IPooledTransaction>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public IPooledTransaction CreateDefault() =>
        // [n_cst_thread_trans_pool.sru:L169] `Create n_cst_thread_trans`
        new PooledTransaction(CreateEngine(), _timeProvider, _hooks);

    /// <inheritdoc/>
    public IPooledTransaction Create(string className)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);

        // MECHANISM 1 - the registry. Checked first, so a deployment that registers its
        // implementations never reaches any reflection at all.
        if (_namedFactories.TryGetValue(className, out Func<IPooledTransaction>? factory))
        {
            return factory()
                ?? throw new InvalidOperationException(
                    "The registered pooled-transaction factory for the configured transaction class "
                    + "name returned no instance.");
        }

        // MECHANISM 2 - the hardened type-name fallback. CONSTRAINT 1: an assembly-qualified name can
        // never cause an assembly to be located or loaded, so it is refused before resolution is
        // attempted rather than after.
        if (className.Contains(AssemblyQualifiedSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "An assembly-qualified transaction class name is not accepted. Name a type that ships "
                + "in this service, or register a factory for it.");
        }

        // CONSTRAINT 2: this assembly only. Assembly.GetType never probes another assembly and never
        // triggers a load, so the reachable candidate set is exactly what this service shipped.
        //
        // NO EXCEPTION HANDLER HERE, AND THAT IS A MEASURED DECISION RATHER THAN AN OVERSIGHT. With
        // throwOnError false this call ANSWERS NULL for a malformed name rather than throwing -
        // verified directly on this toolchain against a bare space, an unmatched bracket, a broken
        // generic arity, a trailing separator and a bare wildcard, every one of which answered null. The
        // two exceptions the overload can still raise, FileLoadException and BadImageFormatException,
        // arise only when a type name embeds an ASSEMBLY-QUALIFIED generic argument - which requires a
        // comma, and CONSTRAINT 1 above has already refused every name containing one. So a handler here
        // would be unreachable code, and unreachable defensive code is worse than none: it reads as a
        // real path and can never be tested.
        Type? candidate = typeof(PooledTransactionActivator).Assembly
            .GetType(className, throwOnError: false, ignoreCase: false);

        if (candidate is null)
        {
            throw new InvalidOperationException(
                "The configured transaction class name does not identify a type that ships in this "
                + "service.");
        }

        // CONSTRAINT 3: the contract, and concreteness. An interface, an abstract class, a value type
        // or an open generic would all fail construction later with a far less useful message.
        if (!typeof(IPooledTransaction).IsAssignableFrom(candidate)
            || !candidate.IsClass
            || candidate.IsAbstract
            || candidate.ContainsGenericParameters)
        {
            throw new InvalidOperationException(
                "The configured transaction class name identifies a type that is not a concrete "
                + "pooled-transaction implementation.");
        }

        return Activate(candidate);
    }

    /// <summary>
    /// Constructs a validated candidate type through one of exactly three accepted constructor shapes.
    /// </summary>
    /// <param name="candidate">
    /// A concrete, non-generic class already known to implement <see cref="IPooledTransaction"/>.
    /// </param>
    /// <returns>The constructed instance.</returns>
    /// <exception cref="InvalidOperationException">
    /// No accepted constructor exists, or construction produced no instance.
    /// </exception>
    /// <remarks>
    /// <para>
    /// CONSTRAINT 4 of the four recorded on this class. <b>EXACTLY TWO shapes are accepted</b>, most
    /// specific first, and both are composed only of dependencies this activator already holds:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <c>(ITransactionEngine, TimeProvider, IPooledTransactionHooks?)</c> - the shape
    /// <see cref="PooledTransaction"/> itself declares, and the shape any in-service implementation
    /// written against these seams will declare. An OPTIONAL third parameter matches this probe, because
    /// the probe compares declared parameter TYPES, so an implementation that wants no hooks writes
    /// <c>hooks = null</c> rather than needing a shape of its own.
    /// </description></item>
    /// <item><description>
    /// <c>()</c> - the direct analogue of PowerBuilder's own <c>Create Using</c>, which passes NO
    /// arguments at all [<c>n_cst_thread_trans_pool.sru:L167</c>], for an implementation that sources its
    /// collaborators itself.
    /// </description></item>
    /// </list>
    /// <para>
    /// A THIRD, TWO-ARGUMENT SHAPE WAS CONSIDERED AND DROPPED. It would serve only a type that literally
    /// declares two parameters, which the optional-parameter note above already covers, so it was
    /// speculative generality: an arm no in-service type reaches and no test could exercise.
    /// </para>
    /// <para>
    /// A CONSTRUCTOR THAT THROWS IS LET THROUGH AS-IS rather than wrapped, because the pool's catch-all
    /// [<c>n_cst_thread_trans_pool.sru:L173-L175</c>] turns any throwable into
    /// <see cref="RetCode.E_INVALID_OBJECT"/>, so wrapping would add a layer no caller can see.
    /// <see cref="TargetInvocationException"/> unwrapping is likewise not attempted for the same
    /// reason.
    /// </para>
    /// <para>
    /// An engine is created ONLY on the shape that takes one, so naming a parameterless implementation
    /// does not leave an orphaned engine behind - which would be a resource leak invisible to every test
    /// that only asserted on the returned instance.
    /// </para>
    /// <para>
    /// WHY THIS IS <c>internal</c> RATHER THAN <c>private</c>. Construction and NAME RESOLUTION are two
    /// separate concerns, and only the latter is security-bearing. CONSTRAINT 2 exists so that a
    /// configuration STRING can never name a type outside this service; it governs
    /// <see cref="Create(string)"/> and is NOT relaxed by this modifier, because reaching this method
    /// requires an already-resolved <see cref="Type"/> object that no configuration value can produce.
    /// The widening exists so the constructor-shape matrix - notably the zero-argument shape, which is
    /// the LITERAL analogue of <c>Create Using</c> [<c>n_cst_thread_trans_pool.sru:L167</c>] and which no
    /// type shipped by this service happens to declare - is coverable against purpose-built doubles
    /// instead of sitting permanently unexercised. This is the <c>InternalsVisibleTo</c> seam the service
    /// grants for exactly this purpose, and it is preferred over the two alternatives: deleting the
    /// zero-argument arm would NARROW the port, since a faithful descendant of the legacy class takes no
    /// constructor arguments at all; and adding a parameterless type to the shipped surface purely to
    /// make a test pass would be production code with no production caller.
    /// </para>
    /// </remarks>
    internal IPooledTransaction Activate(Type candidate)
    {
        object? instance = null;

        if (FindConstructor(candidate, typeof(ITransactionEngine), typeof(TimeProvider), typeof(IPooledTransactionHooks))
            is { } seamed)
        {
            instance = seamed.Invoke([CreateEngine(), _timeProvider, _hooks]);
        }
        else if (FindConstructor(candidate) is { } parameterless)
        {
            instance = parameterless.Invoke([]);
        }

        return instance as IPooledTransaction
            ?? throw new InvalidOperationException(
                "The configured transaction class could not be constructed: it declares none of the "
                + "accepted constructor shapes, or construction produced no instance.");
    }

    /// <summary>
    /// The public instance constructor of <paramref name="candidate"/> taking exactly
    /// <paramref name="parameterTypes"/>, or <see langword="null"/> when there is none.
    /// </summary>
    /// <param name="candidate">The type to inspect.</param>
    /// <param name="parameterTypes">The parameter types, in order.</param>
    /// <returns>The constructor, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// PUBLIC ONLY. A non-public constructor is not probed, so a type cannot be constructed by name
    /// against its own author's intent - which matters because the candidate set is this assembly's
    /// internals, where a private constructor is a genuine statement that the type is not to be
    /// created this way.
    /// </para>
    /// <para>
    /// The resolved constructor is INVOKED DIRECTLY rather than through
    /// <see cref="Activator.CreateInstance(Type, object[])"/>, so the binder never has to choose
    /// between overloads on the strength of a <see langword="null"/> argument. Passing
    /// <see langword="null"/> for the hooks parameter is the ordinary case, and overload selection by
    /// a null argument is exactly where a reflective construction quietly picks the wrong member.
    /// </para>
    /// </remarks>
    private static ConstructorInfo? FindConstructor(Type candidate, params Type[] parameterTypes) =>
        candidate.GetConstructor(
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            parameterTypes,
            modifiers: null);

    /// <summary>
    /// Produces an engine, refusing a factory that hands back nothing.
    /// </summary>
    /// <returns>The new engine.</returns>
    /// <exception cref="InvalidOperationException">The factory returned <see langword="null"/>.</exception>
    /// <remarks>
    /// Fail-fast rather than deferring: a null engine would surface as a
    /// <see cref="ArgumentNullException"/> from deep inside a constructor, or worse, be accepted by an
    /// implementation that did not check and then fail on the first verb.
    /// </remarks>
    private ITransactionEngine CreateEngine() =>
        _engineFactory()
        ?? throw new InvalidOperationException(
            "The transaction engine factory returned no engine, so no pooled transaction can be "
            + "created.");
}

// --------------------------------------------------------------------------------------------------
//  PART 6 OF 6 - THE REFERENCE-COUNTED POOL
// --------------------------------------------------------------------------------------------------

/// <summary>
/// The reference-counted transaction pool - the port of <c>n_cst_thread_trans_pool</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru</c>], all 240 lines.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHAT IT IS.</b> A registry keyed on the WHOLE connection descriptor by value
/// [<c>:L138, :L184</c>], handing out one-based indices [<c>:L151</c>] and one shared transaction per
/// distinct descriptor. Callers take a reference, get the transaction, work it, and hand it back;
/// when the last reference goes and keep-alive is on the transaction is RETAINED against a future
/// caller [<c>:L95-L99</c>], and an idle sweep expires it later [<c>:L210-L226</c>].
/// </para>
/// <para>
/// <b>WHAT IT IS NOT - AND THE FIRST ITEM IS THE ONE THAT SURPRISES PEOPLE.</b> IT NEVER OPENS A
/// CONNECTION. There is no <c>of_Connect</c> call anywhere in the 240 lines of the oracle; the pool
/// creates, reuses, applies the descriptor, disconnects and destroys, while the TASK layer decides
/// when to connect [<c>n_cst_thread_task_sqlbase.sru:L173-L179</c>]. It also has NO maximum size, NO
/// wait queue, NO acquisition timeout, NO eviction policy beyond the idle sweep and NO metrics,
/// because the legacy has none of those and C-B forbids adding a capability the port does not carry.
/// </para>
/// <para>
/// <b>================ INDICES ARE RENUMBERED BY EVERY REMOVAL. THIS IS A PRESERVED DEFECT. =========
/// </b> All three destroy paths REBUILD the collection and assign it back - <c>_transactions =
/// NewTransactions</c> at [<c>:L114</c>], [<c>:L205</c>] and [<c>:L224</c>] - which COMPACTS it. Every
/// one-based index a caller is holding above a removed position therefore points at a DIFFERENT entry
/// afterwards, silently. The consumer partially mitigates it by zeroing its own stored index the
/// instant it calls <c>RemoveRef</c> [<c>n_cst_thread_task_sqlbase.sru:L122-L123</c>], but any OTHER
/// caller sharing the pool keeps a stale index and is not told. It is reproduced exactly, because the
/// alternative - stable handles, or tombstoned slots - would change the values <c>AddRef</c> hands
/// back and would change which entry every subsequent call reaches. A caller that must be safe
/// re-acquires with <c>AddRef</c>, which is what the consumer does [<c>:L164-L166</c>].
/// </para>
/// <para>
/// <b>THREAD SAFETY AND THE ONE RULE ABOUT THE LOCK.</b> Registered as a SINGLETON - a scoped pool
/// would defeat its purpose - so concurrent gRPC calls reach every member simultaneously. One lock
/// guards the entry collection. <b>IT IS NEVER HELD ACROSS AN I/O CALL:</b> every
/// <see cref="IPooledTransaction.Disconnect"/> and every <see cref="IDisposable.Dispose"/> happens
/// after the lock is released, from a list of pending disposals collected while it was held. Three
/// calls DO happen under the lock and each is required by contract to be non-blocking -
/// <see cref="IPooledTransaction.IsBroken"/> (a flag read plus the check hook),
/// <see cref="IPooledTransaction.ClearState"/> (five field assignments) and
/// <see cref="IPooledTransaction.ApplyTransactionData"/> (seven field assignments, which explicitly
/// does not connect). Creation happens under the lock too, which is why
/// <see cref="IPooledTransactionActivator"/> documents that construction must not perform I/O.
/// </para>
/// <para>
/// <b>WIRING - WHAT <c>Program.cs</c> OWNS, SO THAT THIS TYPE OWNS NONE OF IT.</b> The pool does NOT
/// self-register and uses no service locator. A host needs four registrations and one timer:
/// </para>
/// <list type="number">
/// <item><description>
/// <see cref="TimeProvider"/> as a singleton. <c>Program.cs</c> already does this and already
/// documents it as the seam for "the transaction pool's idle expiry".
/// </description></item>
/// <item><description>
/// An <see cref="ITransactionEngine"/> factory, whose SQLite implementation belongs to
/// <c>Data/SqliteConnectionFactory.cs</c> (C-E).
/// </description></item>
/// <item><description>
/// An <see cref="IPooledTransactionActivator"/> - normally
/// <see cref="PooledTransactionActivator"/> over that factory.
/// </description></item>
/// <item><description>
/// This type as a SINGLETON, plus an optional <see cref="TransactionClassNameResolver"/>.
/// </description></item>
/// <item><description>
/// A hosted background timer that calls <see cref="OnIdle"/>. <b>There is no Win32 message pump in a
/// Linux container</b> - AAP 0.6.5 lists message-pump processing as a deliberate non-port - so the
/// legacy's idle event [<c>:L73, :L80</c>] cannot arrive from a pump and nothing here waits for one.
/// <see cref="OnIdle"/> reproduces the SUBSCRIPTION GATING itself, so a host may register the timer
/// unconditionally and still get the legacy's behaviour.
/// </description></item>
/// </list>
/// <para>
/// C-F: this type takes no logger, holds no credential and never reads one. Its only descriptor
/// operations are value comparison and storage; it never renders, formats, logs or reveals a
/// descriptor or any field of one, and the exception messages it produces name no caller data at all.
/// </para>
/// </remarks>
internal sealed class TransactionPool : IDisposable
{
    /// <summary>
    /// One pooled entry - the port of the legacy <c>sharedtransactiondata</c> structure
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L10-L15</c>], four fields in
    /// the oracle's own order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A private mutable CLASS rather than a struct, deliberately: the legacy mutates array elements in
    /// place - <c>_transactions[refIndex].refCount --</c> [<c>:L91</c>] and
    /// <c>_transactions[nRefIdx].idleStartTime = 0</c> [<c>:L149</c>] - and a struct in a
    /// <see cref="List{T}"/> cannot be mutated in place at all, so a struct would silently require
    /// read-modify-write-back at every site and one forgotten write-back would be an invisible defect.
    /// </para>
    /// <para>
    /// It is PRIVATE and never appears on the public surface. Callers see one-based indices, never
    /// entries.
    /// </para>
    /// </remarks>
    private sealed class PooledEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PooledEntry"/> class for a descriptor.
        /// </summary>
        /// <param name="descriptor">The descriptor this entry is keyed on.</param>
        internal PooledEntry(in TransactionData descriptor, long leaseId)
        {
            Descriptor = descriptor;
            LeaseId = leaseId;
        }

        /// <summary>
        /// The STABLE identity of this entry, issued once and never reused.
        /// </summary>
        /// <value>
        /// <para>
        /// <b>WHY A SECOND IDENTITY EXISTS ALONGSIDE THE ONE-BASED POSITION.</b> The legacy pool REBUILDS
        /// its array on removal and thereby renumbers every later index [<c>:L114</c>]. That is a preserved
        /// defect and the positional API keeps it verbatim, because the legacy's own consumer zeroes its
        /// stored index immediately afterwards [<c>n_cst_thread_task_sqlbase.sru:L123</c>] and so never
        /// holds a stale one.
        /// </para>
        /// <para>
        /// DECOMPOSITION BREAKS THAT ASSUMPTION, AND ONLY THAT ASSUMPTION. A transaction session and a SQL
        /// task now hold their reference ACROSS calls, so a DIFFERENT holder's release renumbers theirs and
        /// silently repoints it at somebody else's entry - a cross-request data-integrity fault the legacy
        /// could not have, because one caller owned the pool. A lease is monotonic and never reused, so a
        /// holder that outlives a renumbering either resolves the entry it took or resolves nothing.
        /// </para>
        /// </value>
        internal long LeaseId { get; }

        /// <summary>
        /// The pool KEY - the whole descriptor, compared by value [<c>:L11, :L138</c>].
        /// </summary>
        /// <value>
        /// Immutable once the entry exists. The legacy assigns it exactly once, when the entry is
        /// appended [<c>:L145</c>], and never again - so a descriptor change produces a DIFFERENT
        /// entry rather than mutating this one, which is precisely why the consumer calls
        /// <c>RemoveRef</c> when its descriptor changes [<c>n_cst_thread_task_sqlbase.sru:L114-L125</c>].
        /// <para>
        /// C-K: the key is the WHOLE descriptor and must never be narrowed to a "connection target"
        /// subset. <c>DbParm</c> participates, and <c>DbParm</c> is where <c>DisableBind=1</c> lives -
        /// the flag that makes the runtime interpolate literals instead of binding parameters, which
        /// AAP 0.6.4 identifies as the mechanical root of the SQL-injection exposure. Narrowing the key
        /// would silently share one pooled connection between a bound and an unbound configuration.
        /// </para>
        /// </value>
        internal TransactionData Descriptor { get; }

        /// <summary>
        /// The pooled transaction, or <see langword="null"/> until the first <see cref="Get"/> creates
        /// one [<c>:L12</c>].
        /// </summary>
        /// <value>
        /// <see langword="null"/> is the ordinary initial state: <c>AddRef</c> creates the entry
        /// [<c>:L145</c>] and does NOT create a transaction, so an entry can carry references without
        /// ever having had one. Every legacy site tests it with <c>IsValidObject</c> before touching
        /// it [<c>:L95, :L104, :L158, :L196, :L216</c>], which a null test reproduces exactly - see
        /// <c>Predicates.IsValidObject</c>, whose whole body is a null test.
        /// </value>
        internal IPooledTransaction? Transaction { get; set; }

        /// <summary>
        /// The outstanding reference count [<c>:L13</c>], legacy type <c>unsignedlong</c>.
        /// </summary>
        /// <value>
        /// <see cref="uint"/> because PowerBuilder's <c>unsignedlong</c> is 32-BIT unsigned - the same
        /// width finding <c>shared/PowerFramework.Shared.Kernel/Bits.cs</c> made. Mutated only through
        /// <see cref="IncrementRefCount"/> and <see cref="DecrementRefCount"/>, both of which SATURATE;
        /// see those two members for the decision and the rejected alternative.
        /// </value>
        internal uint RefCount { get; private set; }

        /// <summary>
        /// The instant this entry went idle, in milliseconds, or <c>0</c> for "not idle"
        /// [<c>:L14, :L149</c>].
        /// </summary>
        /// <value>
        /// <see cref="long"/> rather than <see cref="uint"/>, unlike <see cref="RefCount"/> - the two
        /// legacy fields share a type and are decided separately, for the reasons in this file's
        /// header. In short: the legacy 32-bit wrap needs ~49.7 days of accumulated CPU TIME and is
        /// unreachable, so widening cannot change a verdict the legacy actually produces, whereas
        /// reproducing the wrap could invent one. THE ZERO SENTINEL IS PRESERVED EXACTLY.
        /// </value>
        internal long IdleStartTicks { get; set; }

        /// <summary>
        /// Adds a reference [<c>:L148</c>], saturating at <see cref="uint.MaxValue"/>.
        /// </summary>
        /// <remarks>
        /// SATURATES rather than wraps, for the mirror image of the reason
        /// <see cref="DecrementRefCount"/> does. An unsigned increment at the maximum wraps to ZERO,
        /// which would make a maximally-referenced entry instantly releasable and would destroy a
        /// transaction that four billion callers still hold. The legacy is unspecified here; this is
        /// the specified reading, and it is the safe one.
        /// </remarks>
        internal void IncrementRefCount()
        {
            if (RefCount < uint.MaxValue)
            {
                RefCount++;
            }
        }

        /// <summary>
        /// Removes a reference [<c>:L91</c>], saturating at zero.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>THIS IS THE WIDTH DECISION, MADE VISIBLE AT THE POINT OF REPRODUCTION.</b> Decrementing
        /// an unsigned value that is already zero WRAPS to <c>4294967295</c> rather than going
        /// negative. The legacy release test is <c>if refCount &lt;= 0</c> [<c>:L94</c>], and on an
        /// unsigned field that can only be true at exactly zero - so under a wrapping decrement an
        /// over-released entry would NEVER take the release arm and its pooled connection would be
        /// retained for the life of the process.
        /// </para>
        /// <para>
        /// WHAT THE ORACLE SPECIFIES: nothing. PowerScript's behaviour when <c>--</c> takes an unsigned
        /// field below zero is not documented in this repository and is not observable through any
        /// framework API, exactly like the double-to-unsigned conversion that
        /// <c>TransactionPoolOptions.ResolveKeepAliveExpireMilliseconds</c> faced - and it is resolved
        /// the same way, by taking the specified reading as the authoritative contract.
        /// </para>
        /// <para>
        /// THE CHOICE: SATURATE. A count already at zero stays at zero, no wrap occurs, and the entry
        /// takes exactly the arm the <c>&lt;= 0</c> spelling was written to take - that spelling being
        /// the strongest available evidence of intent, since <c>&lt;= 0</c> on an unsigned field is
        /// only meaningful to an author thinking "has reached zero". THE REJECTED ALTERNATIVE IS THE
        /// WRAP, and it is rejected because it converts an unbalanced release - a caller defect, and
        /// one a networked caller can now drive - into a silent unbounded connection leak. Neither
        /// option is left implicit: this is the documented decision and
        /// <c>TransactionPoolTests</c> pins the decrement-at-zero case.
        /// </para>
        /// </remarks>
        internal void DecrementRefCount()
        {
            if (RefCount > 0)
            {
                RefCount--;
            }
        }
    }

    /// <summary>
    /// A transaction removed from the pool and awaiting settlement OUTSIDE the lock.
    /// </summary>
    /// <param name="Transaction">The transaction to settle. Never <see langword="null"/>.</param>
    /// <param name="Disconnect">
    /// Whether to disconnect before disposing. <b>This flag is the legacy's own inconsistency, carried
    /// as data</b>: <c>RemoveRef</c> disconnects only when the object is NOT broken [<c>:L105</c>],
    /// while <c>RemoveAll</c> [<c>:L197</c>] and <c>Collect</c> [<c>:L217</c>] disconnect
    /// UNCONDITIONALLY. Modelling it as a flag is what lets one settlement routine serve all three
    /// call sites without any of them losing its own guard.
    /// </param>
    /// <remarks>
    /// This type exists for one reason: <b>to get the I/O out of the critical section.</b> Each destroy
    /// path decides everything under the lock, records what it decided here, and performs the
    /// disconnect and the dispose afterwards.
    /// </remarks>
    private readonly record struct PendingDisposal(IPooledTransaction Transaction, bool Disconnect);

    /// <summary>
    /// The one lock guarding <see cref="_entries"/> and every entry's mutable state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE lock and not one per entry, because every operation that matters is a whole-collection
    /// operation: the two lookups are linear scans over all entries [<c>:L137, :L183</c>] and all three
    /// destroy paths rebuild the entire collection [<c>:L114, :L205, :L224</c>]. Per-entry locking
    /// would have to be taken in a defined order to avoid deadlock and would still need a
    /// collection-wide lock for the rebuilds, so it would be strictly more machinery for the same
    /// guarantee.
    /// </para>
    /// <para>
    /// <see cref="System.Threading.Lock"/> rather than a plain <see cref="object"/> because that is the
    /// type the language and runtime now recognise for this purpose on this target framework, so
    /// <c>lock</c> binds to its typed API rather than to the monitor's object overload.
    /// </para>
    /// <para>
    /// <b>NEVER HELD ACROSS I/O.</b> See the class remarks for the three non-blocking calls that DO
    /// happen inside it and why each is safe.
    /// </para>
    /// </remarks>
    private readonly Lock _gate = new();

    /// <summary>
    /// The entries, in one-based index order once <see cref="TryPosition"/> has translated.
    /// </summary>
    /// <remarks>
    /// A plain <see cref="List{T}"/>, because the legacy artifact is a plain array [<c>:L55</c>] whose
    /// lookups are linear scans and whose one-based positions are handed to callers as handles.
    /// <c>OrderedMap</c> from <c>PowerFramework.Shared.Containers</c> is deliberately NOT used: that
    /// type is the <c>n_map</c> substitute for the SQL task layer, and this structure is an indexed
    /// array rather than a keyed map - the descriptor is matched by scanning, not by hashing, because
    /// the legacy scans and because index stability is what callers depend on between removals.
    /// </remarks>
    private readonly List<PooledEntry> _entries = [];

    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The MONOTONIC origin this pool measures idle time from. See <see cref="CurrentTicks"/>.
    /// </summary>
    private readonly long _monotonicOrigin;

    private readonly IPooledTransactionActivator _activator;

    /// <summary>
    /// The source of stable entry identities. Monotonic and NEVER REUSED, so a lease handed out once can
    /// never resolve a later entry that happened to take the same position - see
    /// <see cref="PooledEntry.LeaseId"/> for why that guarantee is the point.
    /// </summary>
    private long _nextLeaseId;

    /// <summary>
    /// Whether pooled transactions are retained past their last reference - the legacy
    /// <c>_bKeepAlive</c> [<c>:L58</c>], read from <c>$SQL.TransPool.KeepAlive</c> [<c>:L76</c>].
    /// </summary>
    private readonly bool _keepAlive;

    /// <summary>
    /// The idle lifetime in milliseconds - the legacy <c>_nKeepAliveExpireTime</c> [<c>:L59</c>].
    /// </summary>
    private readonly long _keepAliveExpireMilliseconds;

    /// <summary>
    /// The resolved transaction class name, or the empty string for "the default implementation" -
    /// the legacy <c>_sTransCls</c> [<c>:L57</c>].
    /// </summary>
    private readonly string _transactionClassName;

    /// <summary>
    /// Set once by <see cref="Dispose"/>. <see langword="volatile"/> so that the guard on every public
    /// member observes it without taking the lock first.
    /// </summary>
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionPool"/> class, reproducing the legacy
    /// <c>oninit</c> event [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L76-L83</c>].
    /// </summary>
    /// <param name="options">
    /// The service options, whose <c>TransactionPool</c> section supplies all three settings. The
    /// legacy key spellings are <c>$SQL.TransPool.KeepAlive</c> [<c>:L76</c>],
    /// <c>$SQL.TransPool.KeepAliveExpireTime</c> [<c>:L78</c>] and
    /// <c>$SQL.TransPool.TransClass</c> [<c>:L83</c>]; they are recorded here because they are what a
    /// reader will search the oracle for, and bound as the PascalCase members <c>KeepAlive</c>,
    /// <c>KeepAliveExpireSeconds</c> and <c>TransactionClassName</c>.
    /// </param>
    /// <param name="timeProvider">
    /// The injected clock - CLOCK 1, the idle expiry. The SAME instance the pooled transactions get,
    /// so one substitution masks both clocks in a characterization run.
    /// </param>
    /// <param name="activator">Creates pooled transactions on the <see cref="Get"/> create path.</param>
    /// <param name="transactionClassNameResolver">
    /// The event-first class-name resolver, or <see langword="null"/> for none.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/>, <paramref name="timeProvider"/> or <paramref name="activator"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The options instance or its transaction-pool section is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The oracle, verbatim, and the ORDER of these five steps is contract [<c>:L76-L83</c>]:
    /// </para>
    /// <code>
    /// if ...of_GetDataBoolean("$SQL.TransPool.KeepAlive") then                       [:L76]
    ///     _bKeepAlive = true                                                         [:L77]
    ///     _nKeepAliveExpireTime = ...of_GetDataDouble("...ExpireTime") * 1000        [:L78]
    ///     if _nKeepAliveExpireTime &lt;= 0 then _nKeepAliveExpireTime = 30000        [:L79]
    ///     parentThread.of_On(parentThread.EVT_IDLE,this,"onidle")                    [:L80]
    /// end if
    /// _sTransCls = Event OnGetTransClsName()                                         [:L82]
    /// if IsNull(_sTransCls) or _sTransCls = "" then _sTransCls = ...TransClass        [:L83]
    /// </code>
    /// <para>
    /// <b>KEEP-ALIVE DEFAULTS TO FALSE, AND WHEN IT IS FALSE THE IDLE COLLECTOR IS NEVER
    /// REGISTERED</b> - the subscription at [<c>:L80</c>] sits INSIDE the branch. So with keep-alive
    /// off nothing is ever retained and nothing ever sweeps. That gating is reproduced by
    /// <see cref="OnIdle"/> rather than by a conditional registration, so a host may wire its timer
    /// unconditionally; registering a sweep that ran anyway "for safety" would touch objects the
    /// legacy never revisits.
    /// </para>
    /// <para>
    /// <b>THE EXPIRY IS CALLED, NOT RE-DERIVED.</b>
    /// <c>TransactionPoolOptions.ResolveKeepAliveExpireMilliseconds</c> already performs the
    /// seconds-times-one-thousand conversion of [<c>:L78</c>] and the non-positive fallback of
    /// [<c>:L79</c>]. Re-implementing either here would double-apply the multiplication and let the
    /// two files drift. <b>A NON-POSITIVE CONFIGURED VALUE IS LEGAL and means "use 30000 ms"</b>
    /// (C-B): it is emphatically NOT validated away, because rejecting it would replace a preserved
    /// behaviour with a validation failure the legacy never had and would make the shipped default of
    /// zero unstartable.
    /// </para>
    /// <para>
    /// <b>WHY THE EXPIRY IS STILL POPULATED WHEN KEEP-ALIVE IS OFF.</b> The legacy field carries an
    /// initialiser of <c>KEEPALIVE_EXPIRE</c> [<c>:L59</c>] which the unexecuted branch leaves in
    /// place, and <c>of_collect</c> is a PUBLIC member that a caller may invoke directly whatever the
    /// setting [<c>:L210</c>]. So the thirty-second value is live in that path and is set here from
    /// <c>TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds</c> - never from a local constant,
    /// because this folder declares no SCREAMING_SNAKE constant and because one copy of that number is
    /// the whole point.
    /// </para>
    /// <para>
    /// <b>THE CLASS NAME IS EVENT-FIRST.</b> The resolver is consulted first and configuration is the
    /// FALLBACK, consulted only when the resolver yields null or empty [<c>:L82-L83</c>]. It is
    /// consulted ONCE, here, because the legacy caches the answer in a field.
    /// </para>
    /// <para>
    /// FAIL FAST (AAP 0.1.4): a missing options instance or a missing transaction-pool section is a
    /// structural fault and throws, which for a singleton means startup. There is no degraded mode
    /// worth having - a pool that silently invented its own settings would produce a retention policy
    /// nobody configured.
    /// </para>
    /// </remarks>
    public TransactionPool(
        IOptions<PersistenceOptions> options,
        TimeProvider timeProvider,
        IPooledTransactionActivator activator,
        TransactionClassNameResolver? transactionClassNameResolver = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(activator);

        PersistenceOptions resolvedOptions = options.Value
            ?? throw new InvalidOperationException(
                "The persistence options resolved to no value, so the transaction pool cannot be "
                + "configured.");

        TransactionPoolOptions poolOptions = resolvedOptions.TransactionPool
            ?? throw new InvalidOperationException(
                "The persistence options carry no transaction-pool section, so the transaction pool "
                + "cannot be configured.");

        _timeProvider = timeProvider;
        _monotonicOrigin = timeProvider.GetTimestamp();
        _activator = activator;

        // [:L76-L77] The keep-alive branch.
        _keepAlive = poolOptions.KeepAlive;

        // [:L78-L79] CALLED, not re-derived - the conversion and the non-positive fallback both live
        // in the options type. [:L59] supplies the value the unexecuted branch leaves behind, and
        // of_collect is public, so the field is populated on both arms.
        _keepAliveExpireMilliseconds = _keepAlive
            ? poolOptions.ResolveKeepAliveExpireMilliseconds()
            : TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds;

        // [:L80] The idle subscription lives inside the keep-alive branch. Reproduced by OnIdle's own
        // gating rather than by a conditional registration - see the remarks above.

        // [:L82] Event first.
        string? className = transactionClassNameResolver?.Invoke();

        // [:L83] Configuration second, and only on null-or-empty. IsNullOrEmpty and NOT
        // IsNullOrWhiteSpace: the oracle tests `IsNull(...) or ... = ""`, so a resolver answering a
        // single space WINS and reaches the activator, which will reject it. Preserved as measured.
        if (string.IsNullOrEmpty(className))
        {
            className = poolOptions.TransactionClassName;
        }

        _transactionClassName = className ?? string.Empty;
    }

    /// <summary>
    /// Whether pooled transactions are retained past their last reference.
    /// </summary>
    /// <value>
    /// The legacy <c>_bKeepAlive</c> [<c>:L58, :L77</c>]. Exposed because it is the single setting that
    /// changes what <see cref="RemoveRef"/> does with the last reference, so a diagnostic or a test
    /// that could not read it would be asserting against an invisible input.
    /// </value>
    internal bool IsKeepAliveEnabled => _keepAlive;

    /// <summary>
    /// Whether the idle sweep is driven at all.
    /// </summary>
    /// <value>
    /// Identical to <see cref="IsKeepAliveEnabled"/> and deliberately a SEPARATE member, because in
    /// the legacy they are separate facts that merely coincide: one is a field [<c>:L77</c>] and the
    /// other is whether a subscription was made [<c>:L80</c>]. Naming them apart is what documents
    /// that the sweep's existence is a consequence of the setting rather than the setting itself.
    /// </value>
    internal bool IsIdleCollectionEnabled => _keepAlive;

    /// <summary>
    /// The idle lifetime in milliseconds that <see cref="Collect"/> compares against.
    /// </summary>
    /// <value>
    /// The legacy <c>_nKeepAliveExpireTime</c> [<c>:L59, :L78-L79</c>], already through the
    /// seconds-to-milliseconds conversion and the non-positive fallback.
    /// </value>
    internal long KeepAliveExpireMilliseconds => _keepAliveExpireMilliseconds;

    /// <summary>
    /// The resolved transaction class name, or the empty string for "the default implementation".
    /// </summary>
    /// <value>
    /// The legacy <c>_sTransCls</c> [<c>:L57, :L82-L83</c>]. The empty-versus-non-empty distinction is
    /// what selects between the two create arms [<c>:L166-L170</c>], which is why it is observable
    /// rather than private.
    /// </value>
    internal string TransactionClassName => _transactionClassName;

    /// <summary>
    /// The highest valid one-based index - the port of <c>UpperBound(_transactions)</c>.
    /// </summary>
    /// <value>
    /// <b>THE COUNT, NOT THE COUNT MINUS ONE.</b> PowerBuilder's upper bound is the LAST VALID INDEX
    /// of a one-based array, so for three entries it is <c>3</c> and the valid indices are 1, 2 and 3.
    /// Every legacy guard is <c>refIndex &lt;= 0 or refIndex &gt; UpperBound(...)</c> [<c>:L89, :L120,
    /// :L154</c>], and reading it as a zero-based last index would make the highest entry unreachable.
    /// </value>
    internal int UpperBound
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    // MEMBER 1 OF 7 -------------------------------------------------------------------------------

    /// <summary>
    /// Takes a reference on the entry for <paramref name="descriptor"/>, creating the entry if this is
    /// the first reference to it. The port of <c>of_addref</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L134-L152</c>].
    /// </summary>
    /// <param name="descriptor">
    /// The connection descriptor. Passed <see langword="in"/> because the legacy parameter is
    /// <c>readonly</c> [<c>:L134</c>].
    /// </param>
    /// <returns>
    /// <b>A ONE-BASED INDEX, NOT A RETURN CODE</b> [<c>:L151</c>]. Every sibling member on this type
    /// answers with a <c>RetCode</c>; this one does not, and that is the legacy's own signature -
    /// <c>public function integer of_addref(...)</c>. A caller treats any NON-POSITIVE result as
    /// failure and must not compare it against <see cref="RetCode.OK"/>, which is zero and would read
    /// as success. The consumer stores the value and hands it straight back to <see cref="Get"/>
    /// [<c>n_cst_thread_task_sqlbase.sru:L165, :L168</c>].
    /// </returns>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    /// <remarks>
    /// The oracle, verbatim [<c>:L134-L152</c>]:
    /// <code>
    /// nCount = UpperBound(_transactions)                                             [:L136]
    /// for index = 1 to nCount                                                        [:L137]
    ///     if _transactions[index].TransData = TransData then                          [:L138]
    ///         nRefIdx = index                                                        [:L139]
    ///         exit                                                                   [:L140]
    ///     end if
    /// next
    /// if nRefIdx &lt;= 0 then                                                        [:L143]
    ///     nRefIdx = nCount + 1                                                       [:L144]
    ///     _transactions[nRefIdx].TransData = TransData                               [:L145]
    /// end if
    /// _transactions[nRefIdx].refCount ++                                             [:L148]
    /// _transactions[nRefIdx].idleStartTime = 0                                       [:L149]
    /// return nRefIdx                                                                 [:L151]
    /// </code>
    /// <para>
    /// <b>THE MATCH IS ON THE WHOLE DESCRIPTOR BY VALUE AND FIRST MATCH WINS</b> [<c>:L138, :L140</c>].
    /// The loop exits immediately, so duplicate entries for one descriptor - which cannot arise through
    /// this method but could through direct entry manipulation - would leave the later ones
    /// unreachable. <c>TransactionData</c> is a record struct, so <c>==</c> is exactly the
    /// whole-structure comparison PowerScript performs on a structure, over all NINE members including
    /// the credential.
    /// </para>
    /// <para>
    /// <b>THE IDLE STAMP IS CLEARED ON EVERY CALL, NOT ONLY ON THE FIRST</b> [<c>:L149</c>]. So taking
    /// a second reference on an entry that had gone idle un-idles it, which is what stops the sweep
    /// from collecting a transaction somebody has just asked for. Zeroing it only for a new entry would
    /// leave a stale stamp on a re-referenced one and expire it under its own caller.
    /// </para>
    /// <para>
    /// The append is at <c>nCount + 1</c> [<c>:L144</c>], i.e. one past the upper bound, which is
    /// PowerScript's array-growth idiom and is a plain <see cref="List{T}.Add"/> here. The one-based
    /// index of the appended entry is then the new count.
    /// </para>
    /// </remarks>
    public int AddRef(in TransactionData descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            return AddRefCore(in descriptor);
        }
    }

    /// <summary>
    /// The whole of <see cref="AddRef(in TransactionData)"/>, factored out so the lease-taking overload can
    /// read the selected entry's identity under the SAME lock acquisition.
    /// </summary>
    /// <param name="descriptor">The descriptor to match.</param>
    /// <returns>The one-based reference index.</returns>
    /// <remarks>
    /// ASSUMES <see cref="_gate"/> IS ALREADY HELD. Factoring rather than duplicating is what guarantees
    /// the positional and lease-taking entry points cannot select different entries for one descriptor.
    /// </remarks>
    private int AddRefCore(in TransactionData descriptor)
    {
        // [:L136-L142] The linear scan on whole-descriptor value equality; first match wins and the
        // loop exits. Written as an indexed loop rather than a LINQ query so the one-based index it
        // produces is visible next to the zero-based position it came from.
        int refIndex = 0;
        for (int position = 0; position < _entries.Count; position++)
        {
            if (_entries[position].Descriptor == descriptor)
            {
                refIndex = position + 1;
                break;
            }
        }

        // [:L143-L146] Absent: append at upperBound + 1 and store the descriptor as the key.
        if (refIndex <= 0)
        {
            _entries.Add(new PooledEntry(in descriptor, ++_nextLeaseId));
            refIndex = _entries.Count;
        }

        PooledEntry entry = _entries[refIndex - 1];

        // [:L148]
        entry.IncrementRefCount();

        // [:L149] Un-idle on EVERY call, not only on a fresh entry.
        entry.IdleStartTicks = 0;

        // [:L151] An index, not a code.
        return refIndex;
    }

    // MEMBER 2 OF 7 -------------------------------------------------------------------------------

    /// <summary>
    /// Drops a reference, retaining or destroying the entry according to the keep-alive setting. The
    /// port of <c>of_removeref</c> [<c>:L86-L118</c>].
    /// </summary>
    /// <param name="refIndex">The one-based index <see cref="AddRef"/> returned.</param>
    /// <returns>
    /// <see cref="RetCode.E_OUT_OF_BOUND"/> when the index is out of range; otherwise
    /// <see cref="RetCode.OK"/> - on BOTH the retention arm and the destruction arm, which the caller
    /// therefore cannot tell apart from the return value alone [<c>:L98, :L117</c>].
    /// </returns>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    /// <remarks>
    /// The oracle, verbatim [<c>:L86-L118</c>]:
    /// <code>
    /// if refIndex &lt;= 0 or refIndex &gt; UpperBound(_transactions) then
    ///     return RetCode.E_OUT_OF_BOUND                                              [:L89]
    /// _transactions[refIndex].refCount --                                            [:L91]
    /// if _transactions[refIndex].refCount &lt;= 0 then                               [:L94]
    ///     if _bKeepAlive and IsValidObject(...TransObject) then                      [:L95]
    ///         if Not ...TransObject.of_IsBroken() then                                [:L96]
    ///             _transactions[refIndex].idleStartTime = CPU()                       [:L97]
    ///             return RetCode.OK                                                   [:L98]
    ///         end if
    ///     end if
    ///     for index = 1 to UpperBound(_transactions)                                 [:L101-L102]
    ///         if index = refIndex then                                                [:L103]
    ///             if IsValidObject(...TransObject) then                               [:L104]
    ///                 if Not ...TransObject.of_IsBroken() then                        [:L105]
    ///                     ...TransObject.of_Disconnect()                              [:L106]
    ///                 end if
    ///                 Destroy ...TransObject                                          [:L108]
    ///             end if
    ///         else
    ///             NewTransactions[UpperBound(NewTransactions) + 1] = ...              [:L111]
    ///         end if
    ///     next
    ///     _transactions = NewTransactions                                             [:L114]
    /// end if
    /// return RetCode.OK                                                               [:L117]
    /// </code>
    /// <para>
    /// <b>THE RETENTION ARM RETURNS EARLY AND KEEPS THE ENTRY.</b> It does not destroy, does not
    /// disconnect and does not remove: it stamps the idle clock and leaves the transaction connected
    /// for the next caller [<c>:L97-L98</c>]. All three conditions must hold - keep-alive on, a
    /// transaction present, and NOT broken - and a broken transaction therefore falls through to
    /// destruction even with keep-alive on, which is the whole reason the sweep never has to reason
    /// about health.
    /// </para>
    /// <para>
    /// <b>THE DISCONNECT IS GUARDED BY NOT-BROKEN HERE</b> [<c>:L105</c>] <b>AND IS NOT GUARDED IN
    /// <see cref="RemoveAll"/> OR <see cref="Collect"/></b> [<c>:L197, :L217</c>]. Preserved as
    /// measured; see <see cref="PendingDisposal"/> for how one settlement routine keeps all three
    /// guards intact.
    /// </para>
    /// <para>
    /// <b><see cref="IPooledTransaction.IsBroken"/> IS NOT A FREE CALL - IT CAN CONDEMN THE ENTRY.</b>
    /// It fires the check hook whenever the flag is not already set [<c>n_cst_thread_trans.sru:L530-L532</c>],
    /// so merely inspecting an entry here may transition it to broken and thereby change which arm this
    /// method takes. That is the designed extension point, not an accident.
    /// </para>
    /// <para>
    /// <b>THE ORACLE ASKS IsBroken AT TWO LINES AND THIS PORT ASKS ONCE. The observable hook count is
    /// IDENTICAL, and here is the trace.</b> Keep-alive ON with a healthy transaction: the oracle asks
    /// at [<c>:L96</c>], the hook fires, the method returns - one hook call. Keep-alive ON with a
    /// transaction the hook condemns: [<c>:L96</c>] fires the hook and answers broken, then
    /// [<c>:L105</c>] asks again but the flag is now set so the hook is SKIPPED - still one hook call.
    /// Keep-alive OFF: [<c>:L95</c>] short-circuits so [<c>:L96</c>] is never reached, and
    /// [<c>:L105</c>] asks once - one hook call. No transaction at all: neither line is reached - zero
    /// hook calls. A single guarded call reproduces every row of that table, and it also keeps the
    /// lock's critical section to one hook invocation rather than two.
    /// </para>
    /// <para>
    /// <b>THE REBUILD RENUMBERS EVERY LATER INDEX</b> [<c>:L114</c>]. See the class remarks - it is a
    /// preserved defect and it is why the consumer zeroes its own stored index immediately
    /// [<c>n_cst_thread_task_sqlbase.sru:L123</c>].
    /// </para>
    /// </remarks>
    public long RemoveRef(int refIndex) => RemoveRefCore(EntrySelector.FromIndex(refIndex));

    /// <summary>
    /// The whole of <see cref="RemoveRef(int)"/>, addressed by an <see cref="EntrySelector"/> so the
    /// positional and lease-addressed entry points share one body and one lock acquisition.
    /// </summary>
    /// <param name="selector">How to locate the entry.</param>
    /// <returns>The code documented on <see cref="RemoveRef(int)"/>.</returns>
    private long RemoveRefCore(in EntrySelector selector)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PendingDisposal? pending = null;

        lock (_gate)
        {
            // [:L89]
            if (!TryLocate(in selector, out int position))
            {
                return RetCode.E_OUT_OF_BOUND;
            }

            PooledEntry entry = _entries[position];

            // [:L91] Saturating - see PooledEntry.DecrementRefCount for the width decision.
            entry.DecrementRefCount();

            // [:L94] `refCount <= 0` on an unsigned field is reachable only at exactly zero, so the
            // inverted early-out below is the same test written the way C# reads it.
            if (entry.RefCount > 0)
            {
                return RetCode.OK;
            }

            IPooledTransaction? transaction = entry.Transaction;

            // [:L96 / :L105] ASKED ONCE - see the hook-count trace in the remarks. Only asked at all
            // when there is a transaction, which is what reproduces the zero-hook row of that table.
            bool broken = transaction is not null && transaction.IsBroken();

            // [:L95-L100] THE RETENTION ARM. Keep-alive on, a transaction present, and not broken.
            if (_keepAlive && transaction is not null && !broken)
            {
                // [:L97] CLOCK 1.
                entry.IdleStartTicks = CurrentTicks();

                // [:L98] RETAINED - not destroyed, not disconnected, not removed.
                return RetCode.OK;
            }

            // [:L101-L114] The rebuild, which drops this entry and renumbers every later index.
            _entries.RemoveAt(position);

            if (transaction is not null)
            {
                // [:L105-L108] Disconnect ONLY when not broken, then destroy. Both happen after the
                // lock is released.
                entry.Transaction = null;
                pending = new PendingDisposal(transaction, Disconnect: !broken);
            }
        }

        // I/O strictly outside the lock.
        if (pending is { } disposal)
        {
            Settle(in disposal);
        }

        // [:L117]
        return RetCode.OK;
    }

    // MEMBER 3 OF 7 -------------------------------------------------------------------------------

    /// <summary>
    /// Hands a transaction back, disconnecting it only when exactly one reference is outstanding, and
    /// nulls the caller's handle. The port of <c>of_release</c> [<c>:L120-L132</c>].
    /// </summary>
    /// <param name="refIndex">The one-based index <see cref="AddRef"/> returned.</param>
    /// <param name="transaction">
    /// The caller's handle. Passed by <see langword="ref"/> - not <see langword="out"/> - because this
    /// method both READS it, to validate and to disconnect it, and WRITES it, to reproduce the legacy
    /// <c>SetNull(TransObject)</c> [<c>:L129</c>] which mutates the caller's own variable. Set to
    /// <see langword="null"/> on the success path only; the two guard arms leave it untouched, exactly
    /// as the legacy's early returns do.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.E_OUT_OF_BOUND"/> when the index is out of range;
    /// <see cref="RetCode.E_INVALID_OBJECT"/> when the caller's handle is <see langword="null"/>;
    /// otherwise <see cref="RetCode.OK"/>.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    /// <remarks>
    /// The oracle, verbatim and complete - it is only twelve lines and every one of them matters
    /// [<c>:L120-L132</c>]:
    /// <code>
    /// if refIndex &lt;= 0 or refIndex &gt; UpperBound(_transactions) then
    ///     return RetCode.E_OUT_OF_BOUND                                              [:L120]
    /// if Not IsValidObject(TransObject) then return RetCode.E_INVALID_OBJECT          [:L121]
    /// if _transactions[refIndex].refCount = 1 then                                    [:L123]
    ///     TransObject.of_Disconnect()                                                 [:L125]
    /// end if
    /// TransObject.of_ClearState()                                                     [:L128]
    /// SetNull(TransObject)                                                            [:L129]
    /// return RetCode.OK                                                               [:L131]
    /// </code>
    /// <para>
    /// <b>=================== THIS METHOD DOES NOT DECREMENT. THAT IS NOT A BUG. ===================
    /// </b> The reference count is UNCHANGED on return, and the test at [<c>:L123</c>] is
    /// <c>refCount = 1</c> - <b>EXACTLY one, not at-most-one</b>. Three consequences follow and all
    /// three are contract:
    /// </para>
    /// <list type="bullet">
    /// <item><description>A count of 2 does NOT disconnect. Another caller still holds a reference.</description></item>
    /// <item><description>
    /// A count of 0 does NOT disconnect either, even though nobody holds a reference - because the
    /// entry has already been through <see cref="RemoveRef"/>, which is what decrements, and either
    /// destroyed the transaction or stamped it idle.
    /// </description></item>
    /// <item><description>
    /// The count is the same afterwards as before, so calling this twice does the same thing twice.
    /// </description></item>
    /// </list>
    /// <para>
    /// A reader WILL assume the missing decrement is an oversight; this annotation is what stops the
    /// next maintainer from "fixing" it. <see cref="RemoveRef"/> is the member that decrements
    /// [<c>:L91</c>], and the two exist for different intents - the consumer calls THIS one when
    /// handing an object back [<c>n_cst_thread_task_sqlbase.sru:L141</c>] and THAT one when its
    /// descriptor changed [<c>:L122</c>].
    /// </para>
    /// <para>
    /// <b>IT VALIDATES THE CALLER'S OBJECT, NOT THE STORED ONE</b> [<c>:L121</c>]. The entry may hold a
    /// different transaction, or none, and this method neither notices nor cares: it disconnects and
    /// clears whatever the caller passed. That is why the count, which belongs to the ENTRY, and the
    /// object, which belongs to the CALLER, are read from two different places here.
    /// </para>
    /// <para>
    /// <b>THE GUARD ORDER IS INDEX FIRST, THEN OBJECT</b> [<c>:L120</c> then <c>:L121</c>], which is
    /// observable when both are bad: a null handle with an out-of-range index answers
    /// <see cref="RetCode.E_OUT_OF_BOUND"/> rather than <see cref="RetCode.E_INVALID_OBJECT"/>.
    /// </para>
    /// <para>
    /// <c>Predicates.IsValidObject</c> is used rather than a bare null test, even though its entire
    /// body IS a null test, because [<c>:L121</c>] is a call to that legacy function and consuming the
    /// ported one keeps the correspondence checkable.
    /// </para>
    /// </remarks>
    public long Release(int refIndex, ref IPooledTransaction? transaction) =>
        ReleaseCore(EntrySelector.FromIndex(refIndex), ref transaction);

    /// <summary>
    /// The whole of <see cref="Release(int, ref IPooledTransaction?)"/>, addressed by an
    /// <see cref="EntrySelector"/> so the positional and lease-addressed entry points share one body and
    /// one lock acquisition.
    /// </summary>
    /// <param name="selector">How to locate the entry.</param>
    /// <param name="transaction">The borrowed reference, nulled on success as the oracle nulls it.</param>
    /// <returns>The code documented on <see cref="Release(int, ref IPooledTransaction?)"/>.</returns>
    private long ReleaseCore(in EntrySelector selector, ref IPooledTransaction? transaction)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IPooledTransaction? handle = transaction;
        bool disconnect;

        lock (_gate)
        {
            // [:L120] Index first.
            if (!TryLocate(in selector, out int position))
            {
                return RetCode.E_OUT_OF_BOUND;
            }

            // [:L121] Then the CALLER'S object, not the stored one.
            if (!Predicates.IsValidObject(handle))
            {
                return RetCode.E_INVALID_OBJECT;
            }

            // [:L123] EXACTLY 1. Not <= 1. And NO DECREMENT ANYWHERE IN THIS METHOD.
            disconnect = _entries[position].RefCount == 1;
        }

        // Outside the lock: the disconnect is I/O. `handle` is non-null here - the guard above
        // established it and nothing since could have changed a local.
        if (disconnect)
        {
            // [:L125]
            _ = handle!.Disconnect();
        }

        // [:L128] Cleared whether or not it was disconnected.
        handle!.ClearState();

        // [:L129] SetNull on the CALLER'S variable. This is the reason the parameter is `ref`.
        transaction = null;

        // [:L131]
        return RetCode.OK;
    }

    // MEMBER 4 OF 7 -------------------------------------------------------------------------------

    /// <summary>
    /// Hands out the entry's transaction, reusing a healthy one or creating a replacement. The port of
    /// <c>of_get</c> [<c>:L154-L178</c>].
    /// </summary>
    /// <param name="refIndex">The one-based index <see cref="AddRef"/> returned.</param>
    /// <param name="transaction">
    /// Receives the transaction, or <see langword="null"/> on any failure arm.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.E_OUT_OF_BOUND"/> when the index is out of range;
    /// <see cref="RetCode.E_INVALID_OBJECT"/> when anything at all threw; otherwise
    /// <see cref="RetCode.OK"/>.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    /// <remarks>
    /// The oracle, verbatim [<c>:L154-L178</c>]:
    /// <code>
    /// if refIndex &lt;= 0 or refIndex &gt; UpperBound(_transactions) then
    ///     return RetCode.E_OUT_OF_BOUND                                              [:L154]
    /// try                                                                            [:L156]
    ///     if IsValidObject(_transactions[refIndex].TransObject) then                  [:L158]
    ///         if Not _transactions[refIndex].TransObject.of_IsBroken() then           [:L159]
    ///             TransObject = _transactions[refIndex].TransObject                   [:L160]
    ///             TransObject.of_ClearState()                                         [:L161]
    ///             return RetCode.OK                                                   [:L162]
    ///         end if
    ///         Destroy _transactions[refIndex].TransObject                             [:L164]
    ///     end if
    ///     if _sTransCls &lt;&gt; "" then                                             [:L166]
    ///         _transactions[refIndex].TransObject = Create Using _sTransCls           [:L167]
    ///     else
    ///         _transactions[refIndex].TransObject = Create n_cst_thread_trans         [:L169]
    ///     end if
    ///     _transactions[refIndex].TransObject.of_SetTransData(...TransData)           [:L171]
    ///     TransObject = _transactions[refIndex].TransObject                           [:L172]
    /// catch(throwable ex)                                                            [:L173]
    ///     return RetCode.E_INVALID_OBJECT                                             [:L174]
    /// end try
    /// return RetCode.OK                                                               [:L177]
    /// </code>
    /// <para>
    /// <b>=========================== THIS METHOD NEVER CONNECTS. ===========================</b>
    /// There is no <c>of_Connect</c> here and none anywhere in the pool's 240 lines. A reader expects a
    /// pool's acquire to hand back a LIVE connection; this one hands back a CONFIGURED transaction and
    /// the caller decides. The consumer does exactly that, testing
    /// <see cref="IPooledTransaction.IsConnected"/> and connecting itself
    /// [<c>n_cst_thread_task_sqlbase.sru:L173-L179</c>].
    /// </para>
    /// <para>
    /// <b>THE DESCRIPTOR IS APPLIED TO A NEWLY CREATED OBJECT ONLY</b> [<c>:L171</c>]. A REUSED object
    /// receives <see cref="IPooledTransaction.ClearState"/> and nothing else [<c>:L161</c>] - it is
    /// never re-configured, because the entry's descriptor is immutable and the object was configured
    /// when it was created, so re-applying it would be a no-op that merely re-fired the caller's
    /// descriptor hook. Moving the apply out of the create branch would do exactly that.
    /// </para>
    /// <para>
    /// <b>AND THE MIRROR IMAGE HOLDS: <see cref="IPooledTransaction.ClearState"/> RUNS ON THE REUSE ARM
    /// ONLY</b> [<c>:L161</c>], never on the create arm [<c>:L166-L172</c>]. The two asymmetries are
    /// exact opposites of one another, and BOTH are the oracle's. It is not an oversight that a freshly
    /// created object is handed out without its state being cleared: it has never run a statement, so
    /// its state is already blank, and the descriptor apply that follows is the only thing it needs.
    /// Adding a clear here "for symmetry" would be a behavioural change, because
    /// <see cref="IPooledTransaction.ApplyTransactionData"/> can legitimately leave SQL state behind -
    /// the consumer's descriptor hook is free to run a statement - and clearing after the apply would
    /// discard exactly that. Verified by test: the reuse arm records exactly one clear, the create arm
    /// records none.
    /// </para>
    /// <para>
    /// <b>THE CATCH IS DELIBERATELY BROAD, AND THAT IS THE LEGACY CONTRACT RATHER THAN DEFENSIVE
    /// SLOPPINESS.</b> <c>catch(throwable ex)</c> [<c>:L173</c>] is PowerScript's catch-everything, and
    /// the arm answers <see cref="RetCode.E_INVALID_OBJECT"/> for ANY failure - a class name that does
    /// not resolve, a constructor that threw, a descriptor hook that threw. So a type-activation failure
    /// must NOT escape, which is why <see cref="IPooledTransactionActivator"/> reports failure by
    /// throwing: throwing is how it reaches this arm. The one thing NOT swallowed is
    /// <see cref="ObjectDisposedException"/> for the pool itself, whose guard sits outside the
    /// <see langword="try"/> on purpose - a call on a disposed pool is a caller defect and turning it
    /// into a return code would hide it.
    /// </para>
    /// <para>
    /// <b><see cref="IPooledTransaction.IsBroken"/> CAN CONDEMN THE ENTRY HERE TOO</b> [<c>:L159</c>].
    /// Inspecting the stored transaction fires the check hook, so a hook that condemns it turns this
    /// call from a reuse into a destroy-and-recreate. Second of the two hot paths that inspect with it.
    /// </para>
    /// <para>
    /// <b>THE BROKEN OBJECT IS DESTROYED WITHOUT A DISCONNECT</b> [<c>:L164</c>] - the third distinct
    /// destroy behaviour in this file, alongside <see cref="RemoveRef"/>'s guarded disconnect and
    /// <see cref="RemoveAll"/>'s unguarded one. Disposal happens after the lock is released, from the
    /// <see langword="finally"/> block, so it also runs when a later step throws.
    /// </para>
    /// </remarks>
    public long Get(int refIndex, out IPooledTransaction? transaction) =>
        GetCore(EntrySelector.FromIndex(refIndex), out transaction);

    /// <summary>
    /// The whole of <see cref="Get(int, out IPooledTransaction?)"/>, addressed by an
    /// <see cref="EntrySelector"/> so the positional and lease-addressed entry points share one body and
    /// one lock acquisition.
    /// </summary>
    /// <param name="selector">How to locate the entry.</param>
    /// <param name="transaction">Receives the transaction, or <see langword="null"/> on any failure arm.</param>
    /// <returns>The code documented on <see cref="Get(int, out IPooledTransaction?)"/>.</returns>
    private long GetCore(in EntrySelector selector, out IPooledTransaction? transaction)
    {
        // Outside the try, deliberately: this must not be swallowed by the catch-all below.
        ObjectDisposedException.ThrowIf(_disposed, this);

        transaction = null;
        IPooledTransaction? doomed = null;

        try
        {
            lock (_gate)
            {
                // [:L154]
                if (!TryLocate(in selector, out int position))
                {
                    return RetCode.E_OUT_OF_BOUND;
                }

                PooledEntry entry = _entries[position];
                IPooledTransaction? stored = entry.Transaction;

                // [:L158]
                if (stored is not null)
                {
                    // [:L159] SIDE-EFFECTING - the check hook may condemn it here and now.
                    if (!stored.IsBroken())
                    {
                        // [:L160-L162] THE REUSE PATH. State cleared; descriptor NOT re-applied. The
                        // clear happens HERE AND ONLY HERE - the create path below deliberately has
                        // none. Both halves of that asymmetry are the oracle's.
                        transaction = stored;
                        stored.ClearState();
                        return RetCode.OK;
                    }

                    // [:L164] Broken: destroyed WITHOUT a disconnect, then recreated below. Disposal is
                    // deferred to the finally block so it happens outside the lock.
                    entry.Transaction = null;
                    doomed = stored;
                }

                // [:L166-L170] THE CREATE PATH. The empty-versus-non-empty test on the class name is
                // the observable branch, so it is written as the oracle writes it.
                IPooledTransaction created = _transactionClassName.Length > 0
                    ? _activator.Create(_transactionClassName)
                    : _activator.CreateDefault();

                entry.Transaction = created;

                // [:L171] Applied to a NEWLY CREATED object only. A local copy because the descriptor
                // is passed `in` and a property cannot be passed by reference. NOTE THE ABSENCE OF A
                // ClearState() CALL HERE: the oracle clears on the reuse arm only [:L161], and adding
                // one after this apply would discard any state the descriptor hook legitimately left.
                TransactionData descriptor = entry.Descriptor;
                _ = created.ApplyTransactionData(in descriptor);

                // [:L172]
                transaction = created;
            }
        }
        catch (Exception)
        {
            // [:L173-L175] `catch(throwable ex)` - ANY failure answers E_INVALID_OBJECT. The caller's
            // out-parameter is cleared so a failed call never hands back a half-built object.
            transaction = null;
            return RetCode.E_INVALID_OBJECT;
        }
        finally
        {
            // Outside the lock, because the lock block sits inside the try. Runs on the throwing path
            // too, so a broken object is never orphaned by a later failure.
            doomed?.Dispose();
        }

        // [:L177]
        return RetCode.OK;
    }

    // MEMBER 5 OF 7 -------------------------------------------------------------------------------

    /// <summary>
    /// Whether an entry exists for <paramref name="descriptor"/>. The port of <c>of_exists</c>
    /// [<c>:L180-L188</c>].
    /// </summary>
    /// <param name="descriptor">The descriptor to look for.</param>
    /// <returns><see langword="true"/> when an entry is keyed on an equal descriptor.</returns>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    /// <remarks>
    /// <para>
    /// A linear scan returning <see langword="true"/> on the first match [<c>:L183-L184</c>] - the same
    /// whole-descriptor value comparison <see cref="AddRef"/> uses, and the reason the descriptor's
    /// value equality is load-bearing rather than a convenience.
    /// </para>
    /// <para>
    /// <b>IT ANSWERS ABOUT THE ENTRY, NOT ABOUT A TRANSACTION.</b> An entry created by
    /// <see cref="AddRef"/> that has never been through <see cref="Get"/> holds no transaction at all,
    /// and this still answers <see langword="true"/>. The consumer relies on precisely that
    /// distinction, testing its own live object FIRST and falling back to this
    /// [<c>n_cst_thread_task_sqlbase.sru:L211-L213</c>].
    /// </para>
    /// <para>
    /// The seventh member of the legacy object's public surface, ported because it is part of that
    /// surface even though the consumer reaches it by a different route than the other six.
    /// </para>
    /// </remarks>
    public bool Exists(in TransactionData descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            // [:L182-L185]
            for (int position = 0; position < _entries.Count; position++)
            {
                if (_entries[position].Descriptor == descriptor)
                {
                    return true;
                }
            }

            // [:L187]
            return false;
        }
    }

    // MEMBER 6 OF 7 -------------------------------------------------------------------------------

    /// <summary>
    /// Destroys every unreferenced entry, or every entry at all when forced. The port of
    /// <c>of_removeall</c> [<c>:L190-L208</c>].
    /// </summary>
    /// <param name="force">
    /// When <see langword="true"/>, referenced entries are destroyed as well [<c>:L195</c>].
    /// </param>
    /// <returns><see cref="RetCode.OK"/>, always - the legacy has no failure arm [<c>:L207</c>].</returns>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    /// <remarks>
    /// The oracle, verbatim [<c>:L190-L208</c>]:
    /// <code>
    /// for index = 1 to UpperBound(_transactions)                                     [:L193-L194]
    ///     if _transactions[index].refCount &lt;= 0 or force then                     [:L195]
    ///         if IsValidObject(_transactions[index].TransObject) then                 [:L196]
    ///             _transactions[index].TransObject.of_Disconnect()                    [:L197]
    ///             Destroy _transactions[index].TransObject                            [:L198]
    ///         end if
    ///     else
    ///         NewTransactions[UpperBound(NewTransactions) + 1] = ...                  [:L201]
    ///     end if
    /// next
    /// _transactions = NewTransactions                                                 [:L205]
    /// return RetCode.OK                                                               [:L207</c>]
    /// </code>
    /// <para>
    /// <b>THE DISCONNECT HERE IS UNGUARDED</b> [<c>:L197</c>] - a valid transaction is disconnected
    /// whatever its broken state, unlike <see cref="RemoveRef"/>'s guarded call [<c>:L105</c>].
    /// Harmonising the two would change which objects see a disconnect attempt. It is harmless in
    /// practice because <see cref="IPooledTransaction.Disconnect"/> has its own broken fast path
    /// [<c>n_cst_thread_trans.sru:L145</c>], which is very likely why the legacy author never noticed
    /// the inconsistency - but "harmless" is not "identical", and the port reproduces the call as
    /// written.
    /// </para>
    /// <para>
    /// <b>UNFORCED, IT IGNORES THE IDLE CLOCK ENTIRELY.</b> That is the whole difference from
    /// <see cref="Collect"/>: this destroys an unreferenced entry immediately, that one waits for the
    /// expiry. Neither is a special case of the other.
    /// </para>
    /// <para>
    /// The traversal is FORWARD [<c>:L194</c>] and the survivors keep their relative order
    /// [<c>:L201</c>], so both the settlement order and the surviving indices match the oracle. A
    /// reverse in-place removal would be less code and would settle in the wrong order, firing the
    /// callers' disconnect hooks back to front.
    /// </para>
    /// </remarks>
    public long RemoveAll(bool force)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return RemoveAllCore(force);
    }

    // MEMBER 7 OF 7 -------------------------------------------------------------------------------

    /// <summary>
    /// Destroys every unreferenced entry whose idle lifetime has expired. The port of
    /// <c>of_collect</c> [<c>:L210-L226</c>].
    /// </summary>
    /// <param name="force">
    /// When <see langword="true"/>, the expiry comparison is bypassed - but the reference-count test is
    /// NOT [<c>:L215</c>], so a referenced entry survives even a forced collection. That is the
    /// difference from <see cref="RemoveAll"/> with <paramref name="force"/> set, which destroys
    /// referenced entries too.
    /// </param>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    /// <remarks>
    /// The oracle, verbatim [<c>:L210-L226</c>]:
    /// <code>
    /// for index = 1 to UpperBound(_transactions)                                     [:L213-L214]
    ///     if _transactions[index].refCount &lt;= 0
    ///        and (CPU() - _transactions[index].idleStartTime
    ///             &gt;= _nKeepAliveExpireTime or force) then                         [:L215]
    ///         if IsValidObject(_transactions[index].TransObject) then                 [:L216]
    ///             _transactions[index].TransObject.of_Disconnect()                    [:L217]
    ///             Destroy _transactions[index].TransObject                            [:L218]
    ///         end if
    ///     else
    ///         NewTransactions[UpperBound(NewTransactions) + 1] = ...                  [:L221]
    ///     end if
    /// next
    /// _transactions = NewTransactions                                                 [:L224]
    /// </code>
    /// <para>
    /// <b>THE COMPARISON IS GREATER-OR-EQUAL, NOT STRICTLY GREATER</b> [<c>:L215</c>]. A delta EXACTLY
    /// equal to the expiry collects; one tick under it does not. That boundary is pinned by test, and
    /// turning it into <c>&gt;</c> would hold every entry for one extra tick - unobservable in
    /// production and immediately visible against a hand-driven clock, which is exactly the kind of
    /// drift a characterization comparison exists to catch.
    /// </para>
    /// <para>
    /// <b>THE TWO CONDITIONS ARE AND-ED, AND <paramref name="force"/> IS INSIDE THE PARENTHESES</b>
    /// [<c>:L215</c>]. It overrides the EXPIRY only, never the reference count. A caller wanting to
    /// destroy referenced entries wants <see cref="RemoveAll"/> instead.
    /// </para>
    /// <para>
    /// <b>THE CLOCK IS READ ONCE FOR THE WHOLE SWEEP.</b> The legacy re-reads <c>CPU()</c> per entry
    /// inside the loop, which cannot change any verdict here - <c>CPU()</c> is monotonic and the
    /// comparison is one-sided, so a later reading can only ever make an entry MORE expired, and any
    /// entry that would flip is one whose delta crossed the boundary mid-sweep. One reading is used so
    /// the sweep is a consistent snapshot and so a test's clock advance cannot land mid-loop.
    /// </para>
    /// <para>
    /// <b>IT RETURNS <see langword="void"/></b>, because the legacy declares it as a <c>subroutine</c>
    /// [<c>:L210</c>] with no return value. No return code is invented for it: there is nothing a
    /// caller could do with one, and the two members that do answer with a code both answer
    /// <see cref="RetCode.OK"/> unconditionally anyway.
    /// </para>
    /// <para>
    /// <b>THE DISCONNECT IS UNGUARDED HERE TOO</b> [<c>:L217</c>], as in <see cref="RemoveAll"/>.
    /// </para>
    /// </remarks>
    public void Collect(bool force)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        CollectCore(force);
    }

    // THE IDLE ENTRY POINT ------------------------------------------------------------------------

    /// <summary>
    /// The idle sweep entry point - the port of the legacy <c>onidle</c> event [<c>:L73</c>].
    /// </summary>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    /// <remarks>
    /// <para>
    /// The legacy body is the single statement <c>of_Collect(false)</c> [<c>:L73</c>], and the handler
    /// is subscribed at [<c>:L80</c>] - <b>INSIDE the keep-alive branch</b>. So when keep-alive is off
    /// the legacy does not merely skip a sweep, it never registers the handler and the event never
    /// arrives at all. THAT GATING IS REPRODUCED HERE rather than in the host's registration, for two
    /// reasons: a host may then wire its timer unconditionally and still get legacy behaviour, and the
    /// gate is verifiable by a test that does not have to construct a host.
    /// </para>
    /// <para>
    /// <b>NOT DRIVEN BY A MESSAGE PUMP, BECAUSE THERE ISN'T ONE.</b> The legacy idle event comes from
    /// the framework's own thread loop; AAP 0.6.5 lists message-pump processing as a deliberate
    /// non-port, and a headless Linux container has no pump. A hosted background timer owned by
    /// <c>Program.cs</c> calls this instead - which is a change of DELIVERY MECHANISM only, since
    /// nothing about the sweep depends on where the tick came from.
    /// </para>
    /// <para>
    /// Distinct from <see cref="Collect"/> on purpose: <see cref="Collect"/> is the legacy's PUBLIC
    /// member and runs whatever the setting [<c>:L210</c>], while this is the SUBSCRIBED HANDLER and
    /// runs only when the subscription would have existed. Collapsing them would either make the
    /// public member refuse to work with keep-alive off, or make the sweep run when the legacy never
    /// registered it.
    /// </para>
    /// </remarks>
    public void OnIdle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // [:L80] The subscription lives inside the keep-alive branch, so with keep-alive off [:L73] is
        // unreachable in the oracle.
        if (!_keepAlive)
        {
            return;
        }

        // [:L73]
        CollectCore(force: false);
    }

    // DISPOSAL -----------------------------------------------------------------------------------

    /// <summary>
    /// Destroys every entry, referenced or not. The port of the legacy <c>destructor</c> event
    /// [<c>:L238</c>], whose whole body is <c>of_RemoveAll(true)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>FORCED, BECAUSE THE ORACLE FORCES.</b> <c>of_RemoveAll(true)</c> [<c>:L238</c>] destroys
    /// entries that still carry references - shutdown does not wait for callers to tidy up, and a
    /// disposal that spared referenced entries would leak every connection a caller had forgotten.
    /// </para>
    /// <para>
    /// <b>IDEMPOTENT.</b> The second call returns immediately. The disposed flag is set BEFORE the
    /// sweep so that a concurrent caller cannot re-add an entry into a pool that is going away, and the
    /// sweep goes through the private core rather than through <see cref="RemoveAll"/> precisely
    /// because that public member's guard would now reject it.
    /// </para>
    /// <para>
    /// <b>A FAILING DISCONNECT PROPAGATES FROM HERE (fail-fast, AAP 0.1.4), AND EVERY ENTRY IS STILL
    /// SETTLED FIRST.</b> Settlement disposes in a <see langword="finally"/> and the batch loop
    /// settles every item before rethrowing, so a throwing disconnect can neither skip a disposal nor
    /// hide itself. Swallowing it would be exactly the graceful degradation the plan forbids, and it
    /// would hide an engine that is violating its own no-throw contract.
    /// </para>
    /// <para>
    /// <see cref="IDisposable"/> and not <see cref="IAsyncDisposable"/>: nothing in the ported surface
    /// is asynchronous. The legacy destroy path is synchronous throughout, and
    /// <see cref="IPooledTransaction"/> exposes no asynchronous member for an async disposal to await,
    /// so adding one would be inventing a shape the port does not have.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Set first, so a concurrent AddRef cannot slip an entry past the sweep below.
        _disposed = true;

        // [:L238] `of_RemoveAll(true)` - through the core, because RemoveAll's own guard would now
        // reject the call.
        _ = RemoveAllCore(force: true);
    }

    // PRIVATE ------------------------------------------------------------------------------------

    /// <summary>
    /// <c>of_removeall</c>'s body, without the disposal guard so that <see cref="Dispose"/> can reach
    /// it [<c>:L190-L208</c>].
    /// </summary>
    /// <param name="force">Whether referenced entries are destroyed as well.</param>
    /// <returns><see cref="RetCode.OK"/>, always.</returns>
    private long RemoveAllCore(bool force)
    {
        List<PendingDisposal>? pending = null;

        lock (_gate)
        {
            // [:L191, :L201, :L205] The legacy builds a NEW array of survivors and assigns it back, so
            // the survivors keep their relative order and every later index is renumbered.
            List<PooledEntry> survivors = new(_entries.Count);

            // [:L193-L194] FORWARD, so settlement order matches the oracle.
            foreach (PooledEntry entry in _entries)
            {
                // [:L195] `refCount <= 0 or force`. On an unsigned count, `<= 0` is exactly zero.
                if (entry.RefCount == 0 || force)
                {
                    // [:L196-L199]
                    if (entry.Transaction is { } transaction)
                    {
                        // [:L197] UNGUARDED - no broken test here, unlike RemoveRef's [:L105].
                        entry.Transaction = null;
                        (pending ??= []).Add(new PendingDisposal(transaction, Disconnect: true));
                    }
                }
                else
                {
                    // [:L201]
                    survivors.Add(entry);
                }
            }

            // [:L205]
            _entries.Clear();
            _entries.AddRange(survivors);
        }

        // I/O strictly outside the lock.
        if (pending is not null)
        {
            SettleAll(pending);
        }

        // [:L207]
        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_collect</c>'s body, without the disposal guard so that <see cref="OnIdle"/> can share it
    /// [<c>:L210-L226</c>].
    /// </summary>
    /// <param name="force">Whether the expiry comparison is bypassed.</param>
    private void CollectCore(bool force)
    {
        List<PendingDisposal>? pending = null;

        lock (_gate)
        {
            // CLOCK 1, read ONCE for the whole sweep - see the remark on Collect for why that cannot
            // change a verdict.
            long now = CurrentTicks();

            // [:L211, :L221, :L224] Survivors in a new list, assigned back, preserving relative order.
            List<PooledEntry> survivors = new(_entries.Count);

            // [:L213-L214] FORWARD.
            foreach (PooledEntry entry in _entries)
            {
                // [:L215] `refCount <= 0 and (CPU() - idleStartTime >= expiry or force)`.
                // GREATER-OR-EQUAL, and `force` overrides ONLY the expiry - never the count.
                bool expired = now - entry.IdleStartTicks >= _keepAliveExpireMilliseconds || force;

                if (entry.RefCount == 0 && expired)
                {
                    // [:L216-L219]
                    if (entry.Transaction is { } transaction)
                    {
                        // [:L217] UNGUARDED, as in RemoveAll.
                        entry.Transaction = null;
                        (pending ??= []).Add(new PendingDisposal(transaction, Disconnect: true));
                    }
                }
                else
                {
                    // [:L221]
                    survivors.Add(entry);
                }
            }

            // [:L224]
            _entries.Clear();
            _entries.AddRange(survivors);
        }

        // I/O strictly outside the lock.
        if (pending is not null)
        {
            SettleAll(pending);
        }
    }

    /// <summary>
    /// THE ONE AND ONLY one-based to zero-based translation in this file.
    /// </summary>
    /// <param name="refIndex">The caller's one-based index.</param>
    /// <param name="position">The zero-based list position, or <c>-1</c> when out of range.</param>
    /// <returns><see langword="false"/> when the index is out of range.</returns>
    /// <remarks>
    /// <para>
    /// <b>CENTRALISED ON PURPOSE.</b> AAP 0.4.5.4 names one-based-to-zero-based translation the single
    /// most dangerous mechanical hazard in this refactor, because an off-by-one here is
    /// indistinguishable from a behavioural regression: the call succeeds, it just touches the wrong
    /// entry. Every entry point routes through this method, so the subtraction exists in exactly one
    /// place and the three guard sites [<c>:L89, :L120, :L154</c>] cannot drift apart.
    /// </para>
    /// <para>
    /// The guard is the oracle's, character for character:
    /// <c>refIndex &lt;= 0 or refIndex &gt; UpperBound(_transactions)</c>. <b>THE UPPER BOUND IS THE
    /// COUNT</b> - PowerBuilder's upper bound is the LAST VALID INDEX of a one-based array, so
    /// <c>&gt; Count</c> is correct and <c>&gt;= Count</c> would make the highest entry permanently
    /// unreachable. Zero and every negative index are rejected by the first half, which is why an index
    /// of <c>0</c> - the value the consumer stores to mean "no reference"
    /// [<c>n_cst_thread_task_sqlbase.sru:L123</c>] - can never accidentally address entry one.
    /// </para>
    /// <para>
    /// <b>MUST BE CALLED WHILE HOLDING <see cref="_gate"/></b>, because it reads the entry count. Every
    /// caller does; there is no path to it from outside the lock.
    /// </para>
    /// </remarks>
    // ==============================================================================================
    //  THE STABLE-LEASE API - THE SAME SEVEN OPERATIONS, ADDRESSED BY AN IDENTITY THAT CANNOT RENUMBER
    //
    //  READ THIS ONCE HERE RATHER THAN FIVE TIMES BELOW.
    //
    //  EVERY MEMBER IN THIS SECTION SHARES ONE BODY WITH ITS POSITIONAL TWIN. Each is a one-line call
    //  into the same private *Core the positional overload calls, differing only in the EntrySelector it
    //  passes. Not one of them reimplements a single arm, which is the whole design: the shared bodies
    //  keep the oracle's behaviour byte for byte - including the rebuild that renumbers every later index
    //  [:L114], which is a PRESERVED DEFECT and not a bug to be corrected - and the lease is an
    //  ADDRESSING MODE over them for the holders that decomposition gave a lifetime the legacy's holders
    //  never had.
    //
    //  THE LOOKUP HAPPENS INSIDE THE BODY'S OWN LOCK, NOT BEFORE IT. Resolving a lease to a position and
    //  then calling the positional member would release the gate between the two steps, and an unrelated
    //  holder's release in that window renumbers the position - so the call would land on a stranger's
    //  entry. See EntrySelector for why a narrower window is not a fix.
    //
    //  WHO SHOULD USE WHICH. A caller that takes a reference and releases it inside ONE operation may use
    //  either. A caller that HOLDS a reference across calls - a transaction session named by a later RPC,
    //  a SQL task retrieved and paged by later ones - must use the lease, because the positional index it
    //  stored can be renumbered by an unrelated holder's release in between and would then address
    //  somebody else's entry.
    //
    //  AN EXPIRED LEASE RESOLVES NOTHING RATHER THAN SOMETHING ELSE. Lease identities are monotonic and
    //  never reused, so a holder that outlives its entry receives E_OUT_OF_BOUND - the same code the
    //  positional API answers for an index past the end - instead of silently succeeding against a
    //  stranger. That distinction is the entire data-integrity value of this section.
    // ==============================================================================================

    /// <summary>
    /// Takes a reference on the entry matching the descriptor and answers its STABLE lease.
    /// </summary>
    /// <param name="descriptor">The descriptor to match, by whole-value equality.</param>
    /// <returns>The lease, which is always valid because the entry is created if it is absent.</returns>
    /// <remarks>
    /// The positional <see cref="AddRef(in TransactionData)"/> performs the whole operation; this member
    /// only reads the lease off the entry it selected, under the same lock, so the two cannot disagree
    /// about which entry was taken.
    /// </remarks>
    public PoolLease AddRefLease(in TransactionData descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            int refIndex = AddRefCore(in descriptor);

            return new PoolLease(_entries[refIndex - 1].LeaseId);
        }
    }

    /// <summary>Releases a reference held by lease. See <see cref="RemoveRef(int)"/>.</summary>
    /// <param name="lease">The lease.</param>
    /// <returns>
    /// The positional member's own code, or <see cref="RetCode.E_OUT_OF_BOUND"/> when the lease no longer
    /// names a live entry.
    /// </returns>
    public long RemoveRef(PoolLease lease) => RemoveRefCore(EntrySelector.FromLease(lease));

    /// <summary>Borrows the transaction held by lease. See <see cref="Get(int, out IPooledTransaction?)"/>.</summary>
    /// <param name="lease">The lease.</param>
    /// <param name="transaction">Receives the borrowed transaction, or <see langword="null"/>.</param>
    /// <returns>
    /// The positional member's own code, or <see cref="RetCode.E_OUT_OF_BOUND"/> when the lease no longer
    /// names a live entry.
    /// </returns>
    public long Get(PoolLease lease, out IPooledTransaction? transaction) =>
        GetCore(EntrySelector.FromLease(lease), out transaction);

    /// <summary>Hands back the transaction held by lease. See <see cref="Release(int, ref IPooledTransaction?)"/>.</summary>
    /// <param name="lease">The lease.</param>
    /// <param name="transaction">The borrowed reference, nulled on success exactly as the oracle nulls it.</param>
    /// <returns>
    /// The positional member's own code, or <see cref="RetCode.E_OUT_OF_BOUND"/> when the lease no longer
    /// names a live entry.
    /// </returns>
    public long Release(PoolLease lease, ref IPooledTransaction? transaction) =>
        ReleaseCore(EntrySelector.FromLease(lease), ref transaction);

    /// <summary>
    /// Whether a lease still names a live entry.
    /// </summary>
    /// <param name="lease">The lease.</param>
    /// <returns><see langword="true"/> when the entry is still pooled.</returns>
    public bool IsLeaseLive(PoolLease lease)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            return TryLocateLease(lease, out _);
        }
    }

    private bool TryPosition(int refIndex, out int position)
    {
        if (refIndex <= 0 || refIndex > _entries.Count)
        {
            position = -1;
            return false;
        }

        position = refIndex - 1;
        return true;
    }

    /// <summary>
    /// How an operation names the entry it acts on - either by the oracle's one-based position or by a
    /// stable lease.
    /// </summary>
    /// <param name="RefIndex">The one-based position, when <paramref name="Lease"/> is not valid.</param>
    /// <param name="Lease">The lease, when one was supplied.</param>
    /// <remarks>
    /// <b>THIS TYPE EXISTS TO CLOSE A RESOLVE-THEN-ACT WINDOW, AND THAT IS ITS ONLY PURPOSE.</b>
    /// Translating a lease to a position and then calling the positional member would release
    /// <see cref="_gate"/> between the two steps, and an unrelated holder's release in that window
    /// renumbers every later index [<c>:L114</c>] - so the second step would address a STRANGER'S entry.
    /// That is precisely the defect the lease exists to prevent, merely narrowed to a smaller window, and
    /// a narrower window is not a fix. Carrying the selector INTO the body instead means the lookup and
    /// the work happen under one acquisition, while every I/O the body defers still happens outside the
    /// lock exactly as before.
    /// </remarks>
    private readonly record struct EntrySelector(int RefIndex, PoolLease Lease)
    {
        /// <summary>Names an entry by the oracle's one-based position.</summary>
        /// <param name="refIndex">The one-based position.</param>
        /// <returns>The selector.</returns>
        internal static EntrySelector FromIndex(int refIndex) => new(refIndex, PoolLease.None);

        /// <summary>Names an entry by its stable lease.</summary>
        /// <param name="lease">The lease.</param>
        /// <returns>The selector.</returns>
        internal static EntrySelector FromLease(PoolLease lease) => new(0, lease);
    }

    /// <summary>
    /// Locates the entry a selector names.
    /// </summary>
    /// <param name="selector">How the entry is named.</param>
    /// <param name="position">Receives the zero-based position, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> when the entry was found.</returns>
    /// <remarks>
    /// ASSUMES <see cref="_gate"/> IS ALREADY HELD, because the whole point of the selector is that the
    /// lookup and the work it feeds share one acquisition.
    /// </remarks>
    private bool TryLocate(in EntrySelector selector, out int position) =>
        selector.Lease.IsValid
            ? TryLocateLease(selector.Lease, out position)
            : TryPosition(selector.RefIndex, out position);

    /// <summary>
    /// Scans for the entry carrying a lease identity.
    /// </summary>
    /// <param name="lease">The lease.</param>
    /// <param name="position">Receives the zero-based position, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> when the lease still names a pooled entry.</returns>
    /// <remarks>
    /// <para>
    /// ASSUMES <see cref="_gate"/> IS ALREADY HELD.
    /// </para>
    /// <para>
    /// A LINEAR SCAN, and deliberately so: the pool is an ARRAY matched by scanning rather than a keyed
    /// map, because the legacy scans and because the positional addressing its own callers use is defined
    /// in terms of that array. Adding a side index would introduce a second structure to keep in step
    /// with the first, and the entry count a transaction pool reaches makes the scan unmeasurable.
    /// </para>
    /// <para>
    /// Lease identities are monotonic and never reused, so a lease whose entry has been removed matches
    /// NOTHING rather than matching whatever now occupies its old position. That is the entire
    /// data-integrity value of the lease API.
    /// </para>
    /// </remarks>
    private bool TryLocateLease(PoolLease lease, out int position)
    {
        position = -1;

        if (!lease.IsValid)
        {
            return false;
        }

        for (int candidate = 0; candidate < _entries.Count; candidate++)
        {
            if (_entries[candidate].LeaseId == lease.Id)
            {
                position = candidate;

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads CLOCK 1 - the idle expiry's clock - as MONOTONIC elapsed milliseconds since the pool was
    /// constructed, through the injected provider.
    /// </summary>
    /// <returns>Monotonic elapsed milliseconds, offset by <see cref="MonotonicOffset"/>.</returns>
    /// <remarks>
    /// <para>
    /// The port of <c>CPU()</c> as this component uses it: stamped at [<c>:L97</c>] and differenced at
    /// [<c>:L215</c>]. <b>THE ONLY CLOCK READ IN THIS CLASS.</b> There is no
    /// <c>DateTime.UtcNow</c>, no <c>DateTime.Now</c>, no <c>Environment.TickCount</c> and no
    /// <c>Stopwatch</c> anywhere in this file, and adding one would break the characterization
    /// requirement that every clock read be maskable from BOTH the golden master and the candidate
    /// (AAP 0.6.7).
    /// </para>
    /// <para>
    /// <b>MONOTONIC, NOT WALL-CLOCK.</b> <c>CPU()</c> is a process-relative counter that cannot move
    /// backwards, and the only comparison performed on this value is the idle DELTA at [<c>:L215</c>].
    /// A UTC instant can step in either direction under an NTP correction or a manual clock change: a
    /// backward step makes an idle interval read as negative so an entry never expires, and a forward
    /// step expires a transaction a caller has just been handed. Differencing
    /// <c>GetTimestamp()</c> against an origin captured in the constructor reaches neither.
    /// </para>
    /// <para>
    /// Only DELTAS and the zero sentinel are ever compared, so the origin itself is unobservable and the
    /// constant offset below is invisible to every comparison - which is what lets it remove the one
    /// honest hazard the reading carried, that a stamp taken in the pool's first millisecond would be
    /// zero and read as the "not idle" sentinel [<c>:L149</c>].
    /// </para>
    /// </remarks>
    private long CurrentTicks() =>
        (long)_timeProvider.GetElapsedTime(_monotonicOrigin, _timeProvider.GetTimestamp())
            .TotalMilliseconds
        + MonotonicOffset;

    /// <summary>
    /// The offset applied to every monotonic stamp so a stamp can never collide with the zero sentinel.
    /// </summary>
    /// <remarks>
    /// A constant offset is invisible to every comparison this class performs, because only DELTAS and the
    /// zero sentinel are ever compared - so it preserves the exact expiry boundary while removing the one
    /// honest hazard the epoch-relative reading carried, that a clock positioned at the origin produces a
    /// stamp of zero and reads as "not idle" [<c>:L149</c>].
    /// </remarks>
    private const long MonotonicOffset = 1L;

    /// <summary>
    /// Performs one pending disposal - the disconnect, if the site asked for it, and then the destroy.
    /// </summary>
    /// <param name="pending">What to settle, and whether to disconnect first.</param>
    /// <remarks>
    /// <para>
    /// <b>ALWAYS CALLED OUTSIDE <see cref="_gate"/>.</b> That is the entire reason
    /// <see cref="PendingDisposal"/> exists: <see cref="IPooledTransaction.Disconnect"/> is I/O and the
    /// lock must never be held across it.
    /// </para>
    /// <para>
    /// The disconnect's own return code is DISCARDED, because no legacy call site inspects it
    /// [<c>:L106, :L197, :L217</c>] and there is nothing a destroy path could do with a failure it is
    /// about to make moot.
    /// </para>
    /// <para>
    /// <b>THE DISPOSE IS IN A <see langword="finally"/>, WHICH IS A .NET-SIDE ADDITION AND IS
    /// DELIBERATE.</b> PowerBuilder's embedded SQL cannot throw, so the oracle has no arm for a
    /// throwing disconnect and its <c>Destroy</c> is unconditionally reached [<c>:L106-L108</c>]. In
    /// .NET a call CAN throw, so reaching the destroy unconditionally requires saying so - handling a
    /// failure mode the technology transition itself creates, which AAP 0.5.3 establishes is required
    /// BY the transition rather than a behavioural improvement layered on top of it. The exception then
    /// propagates rather than being swallowed, because an engine that throws is violating its own
    /// contract and hiding that would be graceful degradation.
    /// </para>
    /// </remarks>
    private static void Settle(in PendingDisposal pending)
    {
        try
        {
            if (pending.Disconnect)
            {
                // [:L106, :L197, :L217] Return code discarded, as at every legacy call site.
                _ = pending.Transaction.Disconnect();
            }
        }
        finally
        {
            // [:L108, :L198, :L218] `Destroy` - reached whether or not the disconnect succeeded.
            pending.Transaction.Dispose();
        }
    }

    /// <summary>
    /// Settles a whole sweep's pending disposals, guaranteeing that every one is attempted.
    /// </summary>
    /// <param name="pending">The disposals collected under the lock, in the oracle's forward order.</param>
    /// <exception cref="AggregateException">
    /// One or more disconnects threw. Every item was still settled before this is raised.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The batch counterpart of <see cref="Settle"/>, and it exists for one reason: <b>a throwing
    /// disconnect part-way through a sweep must not strand the entries behind it.</b> The legacy loop
    /// cannot fail, so it has no such concern; here the loop continues, collects what went wrong, and
    /// reports it once at the end. Reporting rather than swallowing keeps the fail-fast posture, and
    /// continuing rather than aborting keeps the no-leak guarantee - both are needed, so neither a bare
    /// loop nor a bare try would do.
    /// </para>
    /// <para>
    /// Order is the oracle's forward order [<c>:L194, :L214</c>], which matters because settling fires
    /// the callers' disconnect hooks.
    /// </para>
    /// </remarks>
    private static void SettleAll(List<PendingDisposal> pending)
    {
        List<Exception>? failures = null;

        foreach (PendingDisposal item in pending)
        {
            try
            {
                Settle(in item);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more pooled transactions failed to disconnect while the transaction pool was "
                + "settling them. Every transaction was still disposed.",
                failures);
        }
    }
}
