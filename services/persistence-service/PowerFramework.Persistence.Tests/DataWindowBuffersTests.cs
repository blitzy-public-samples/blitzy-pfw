// ==============================================================================================
//  DataWindowBuffersTests - the anti-corruption layer, the carrier pair and the preserved defects
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST
//      services/persistence-service/PowerFramework.Persistence/Buffers/DataWindowBuffers.cs
//      services/persistence-service/PowerFramework.Persistence/Buffers/ItemStatus.cs
//
//  BEHAVIOURAL ORACLE (all READ ONLY per constraint C-C - never edited, moved or reformatted)
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru        the _ds carrier,
//                                                                               "[运行在主线程]",
//                                                                               175 lines
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru     the _ds_mt carrier,
//                                                                               "[运行在子线程]",
//                                                                               73 lines
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L553-L557 the affinity factory
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L84-L110 the handover, then
//                                                                               the codec selector
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L28-L33  6 notify codes
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L32     1 notify code
//      ws_objects/pfw.common.pbl.src/makelong.srf                               the word packer
//      ws_objects/pfw.tests.pbl.src/dw_sqlite.srd                               the golden master
//
//  WHY THIS SUITE EXISTS
//  --------------------------------------------------------------------------------------------
//  Buffers/ is the anti-corruption layer the refactor plan calls REQUIRED, NOT STYLISTIC. The legacy
//  result carrier is declared `global type n_cst_thread_task_sqlbase_ds from datastore`
//  [n_cst_thread_task_sqlbase_ds.sru:L8], so the legacy result IS a DataWindow - three buffers, a row
//  status and a per-column status, a current value and an original value per cell. A naive rowset
//  would silently discard exactly the state the update contract depends on, because `updatewhere=1`
//  on the golden master [dw_sqlite.srd:L14] combined with `updatewhereclause=yes` on all six columns
//  [:L8-L13] puts the ORIGINAL value of every updateable column into the generated where clause.
//  This file is what proves the layer carries that state.
//
//  SIX BEHAVIOURS HERE LOOK LIKE DEFECTS AND ARE NOT (C-B). A well-meaning future edit would "fix"
//  each of them and silently break parity, so every one is pinned below, by name, with its locator:
//
//      DEFECT 1  a delete preview against Primary!/Filter! does NOT advance the progress counter
//                [n_cst_thread_task_sqlbase_ds_mt.sru:L32-L35]
//      DEFECT 2  the deleted-row contribution walks the Delete! buffer from its count DOWN to 1, and
//                counts only NotModified! and DataModified! - so a NEW-THEN-DELETED row is excluded
//                [:L55-L60]
//      DEFECT 3  a row-cap handler answering 1 LIFTS the cap and lets the retrieve continue, and
//                leaves the rows-exceeded flag clear [:L65-L68]
//      DEFECT 4  the progress payload packs two 16-BIT words, so anything above 65535 truncates
//                [:L39 via makelong.srf, whose operands are PowerBuilder `uint`]
//      DEFECT 5  the N-char rewriter returns its ARGUMENT ITSELF, discarding everything accumulated,
//                the moment it meets an already-N-prefixed literal [_ds.sru:L125-L127]
//      DEFECT 6  the two per-task notify sets BOTH contain the value 1 and mean different things by
//                it [n_cst_threading_task_sqlquery.sru:L28 versus
//                n_cst_threading_task_sqlupdate.sru:L32]
//
//  ORDERING IS CONTRACT, NOT COMMENTARY. All four worker overrides open with `call super::<event>`
//  [:L27, :L30, :L47, :L63], and in one of them the ancestor call is load bearing - it is what
//  performs the N-char rewrite. AAP 0.4.5.4 states that the thread-affinity annotations are a
//  contract, so the proxy/worker duality is asserted here as TWO TYPES rather than one.
//
//  CONSTRAINTS DISCHARGED BY THIS FILE, EACH CITED AGAIN AT THE POINT IT IS DISCHARGED
//  --------------------------------------------------------------------------------------------
//  RULES POSITION FIRST. `review_rules` returns exactly one line - "No user rules provided." - so NO
//  user rule governs this file and none is invented. Enterprise-standard best practice applies in
//  their place per AAP 0.7.2, and the binding constraints come from AAP 0.7.3 instead:
//
//      C-B  Replicate defects and quirks verbatim. All six above are asserted as EXPECTED.
//      C-E  No fabricated database. Every case below runs against in-memory buffers. There is no
//           SQLite file, no connection, no dialect client and no schema anywhere in this file.
//      C-F  No secret. Every literal is synthetic, no statement asserted on carries a credential-
//           shaped value, and the SQL-preview interception surface is exercised with column edits
//           only - never with a password, key, token or connection string.
//      C-H  Buffers/ is large and central, so this file drives it broadly rather than narrowly.
//      C-K  Two decisions are documented rather than left to be rediscovered: why the database-error
//           delegate is five raw scalars instead of Errors/DbErrorData, and why the two notify sets
//           stay separate enumerations.
//      0.6.7 The ~100 ms progress throttle is a NAMED DETERMINISM SEAM. It is driven exclusively
//           through the injected FakeTimeProvider from TestDoubles.cs. NO test in this file sleeps,
//           delays, reads a real clock or constructs a Stopwatch - the notification COUNT depends on
//           the throttle, so a real clock would make that count vary run to run, which is precisely
//           what the Golden-Master technique forbids.
//      0.8.5 No performance assertion. The throttle is asserted as a BEHAVIOURAL rule about WHEN a
//           notification fires. Nothing here claims a throughput, a latency or a duration.
//      0.7.2 Nullable reference types and warnings-as-errors are inherited from the repository-root
//           Directory.Build.props. This file is deliberately NOT on the .editorconfig list that
//           relaxes CA1707 and IDE1006, so it declares no SCREAMING_SNAKE identifier at all.
// ==============================================================================================

using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;

// PowerFramework.Contracts.Common.V1 (ItemStatus, DwBuffer), PowerFramework.Persistence.Buffers,
// PowerFramework.Persistence.Errors (DbErrorData, referenced only to prove it is deliberately NOT
// used), PowerFramework.Shared.Kernel (Bits), Xunit and the RetCode alias all arrive from the sibling
// GlobalUsings.cs, which is this assembly's single owner of global using declarations. Restating any
// of them here would compile - Roslyn deduplicates - but it would split one concern across two
// mechanisms. System.Globalization, System.Reflection and System.Reflection.Emit are NOT global and
// NOT implicit, so they are imported file-locally, exactly as sibling test files do.
//
// RetCode IS DELIBERATELY NOT ALIASED HERE. GlobalUsings.cs binds `RetCode` to
// PowerFramework.Shared.Kernel.RetCode assembly-wide, which resolves the collision with the
// published contracts' message of the same name. A file-local alias repeating a global alias of the
// same name is error CS1537, so the one declaration has to stay in one place.
namespace PowerFramework.Persistence.Tests;

/// <summary>
/// One call site found in a compiled method body, with its position in instruction order.
/// </summary>
/// <param name="Ordinal">
/// The one-based position of this call among the call sites of the method it was read from. Ordinal 1
/// is the FIRST thing the method calls, which is the fact the ancestor-first assertions rest on.
/// </param>
/// <param name="Instruction">
/// The IL instruction name: <c>call</c> for a non-virtual call, <c>callvirt</c> for a virtual one,
/// <c>newobj</c> for a construction. THE DISTINCTION IS THE POINT for an ancestor call: C# compiles
/// <c>base.Member()</c> to a NON-VIRTUAL <c>call</c> precisely so that it reaches the ancestor's body
/// rather than re-entering the override, whereas <c>this.Member()</c> on a virtual member compiles to
/// <c>callvirt</c>. An assertion that accepted either would pass for an implementation that had
/// accidentally written recursion.
/// </param>
/// <param name="DeclaringTypeName">
/// The simple name of the type declaring the called member, or
/// <see cref="MethodBodyProbe.UnresolvedTypeName"/> when the metadata token could not be resolved.
/// </param>
/// <param name="MemberName">
/// The name of the called member, or <see cref="MethodBodyProbe.UnresolvedMemberName"/> when the
/// token could not be resolved.
/// </param>
internal readonly record struct RecordedIlCall(
    int Ordinal,
    string Instruction,
    string DeclaringTypeName,
    string MemberName);

/// <summary>
/// Reads the call sites out of a compiled method body, in instruction order, so that ORDERING and
/// ABSENCE can both be asserted.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY A COMPILED-BODY PROBE RATHER THAN A BEHAVIOURAL OBSERVATION.</b> Two facts this suite has
/// to pin are not observable from outside the objects that hold them.
/// </para>
/// <para>
/// THE FIRST IS ANCESTOR-FIRST ORDERING. Every one of the four worker overrides opens with
/// <c>call super::&lt;event&gt;</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L27, L30, L47, L63</c>],
/// and in three of the four the ancestor script is EMPTY - the PowerBuilder <c>datastore</c> does not
/// script <c>retrievestart</c>, <c>updatestart</c> or <c>retrieverow</c>, so the port's ancestor
/// bodies answer the continue value and do nothing else. A call to a method that does nothing leaves
/// no trace a test double could record, so the only honest ways to assert the ordering are to read the
/// compiled body or to assert nothing at all. The fourth, <c>sqlpreview</c>, IS observable because its
/// ancestor performs the N-char rewrite, and it is additionally asserted behaviourally so that the
/// two techniques corroborate each other rather than one standing alone.
/// </para>
/// <para>
/// THE SECOND IS THE ABSENCE OF A CODEC ON THE OWNERSHIP-HANDOVER PATH. "No codec ran" is a negative
/// over an entire code path, and a behavioural test can only ever show that no codec ran on the ONE
/// path it happened to drive. Reading the bodies shows that no codec is reachable at all.
/// </para>
/// <para>
/// <b>THE OPCODE TABLE IS BUILT FROM THE RUNTIME'S OWN DEFINITIONS</b> rather than hand-tabulated, so
/// there is no table to fall out of date, and every operand size comes from
/// <see cref="OpCode.OperandType"/>. Scanning raw bytes for a call opcode WITHOUT decoding operand
/// sizes would be wrong rather than merely approximate: an inline metadata token or a string literal
/// operand can contain the byte 0x28 and would be read as a call that is not there.
/// </para>
/// <para>
/// <b>AN UNRESOLVABLE TOKEN IS RECORDED, NOT SWALLOWED.</b> Resolution needs generic context for some
/// tokens, so failures are surfaced as <see cref="UnresolvedTypeName"/> and the suite asserts there
/// are none in the bodies it inspects. Silently dropping them would let a future edit weaken an
/// absence assertion without any test failing - which is the one failure mode a negative assertion
/// cannot tolerate.
/// </para>
/// <para>
/// <b>CALLS INSERTED BY COVERAGE INSTRUMENTATION ARE EXCLUDED, AND THAT IS NOT OPTIONAL.</b> The
/// documented test command collects coverage - <c>dotnet test</c> requesting the "XPlat Code Coverage"
/// data collector - and the collector REWRITES the compiled bodies before they run, inserting a hit
/// recorder at the head of every sequence point. So under the very command constraint C-H is measured
/// by, the first call instruction in each of these bodies is the recorder rather than the ancestor. The
/// insertion is confined to a tracker the collector injects, so it is identified by name and skipped
/// before ordinals are assigned; see <see cref="InstrumentationMemberNames"/>. Without this, every
/// ordering assertion in this suite would pass locally and fail in the coverage leg - the worst
/// possible split.
/// </para>
/// <para>
/// The ordering assertions do not rest on this exclusion ALONE, precisely because it is
/// vendor-shaped: they additionally assert that nothing the override itself does - no parent-task
/// call, no other member of the carrier hierarchy - precedes the ancestor call. That assertion needs no
/// filter at all and holds whether the body is instrumented or not.
/// </para>
/// <para>
/// This type reads metadata only. It loads nothing, emits nothing, and invokes nothing.
/// </para>
/// </remarks>
internal static class MethodBodyProbe
{
    /// <summary>
    /// The member names a coverage collector's injected hit recorder uses, excluded from every reported
    /// call list.
    /// </summary>
    /// <remarks>
    /// <c>RecordHit</c> is what the collector in this repository's test stack emits, observed directly:
    /// under the coverage command the first call in each worker override resolves to it. The
    /// single-hit variant is listed alongside it because the same collector emits that name when
    /// configured for single-hit mode, and a run that switched modes must not start failing ordering
    /// tests.
    /// </remarks>
    internal static readonly string[] InstrumentationMemberNames = ["RecordHit", "RecordSingleHit"];

    /// <summary>
    /// The namespace prefix of the tracker type a coverage collector injects into an instrumented
    /// module.
    /// </summary>
    internal const string InstrumentationTypeNamePrefix = "Coverlet.";
    /// <summary>The declaring-type name recorded for a call whose token could not be resolved.</summary>
    internal const string UnresolvedTypeName = "<unresolved-type>";

    /// <summary>The member name recorded for a call whose token could not be resolved.</summary>
    internal const string UnresolvedMemberName = "<unresolved-member>";

    /// <summary>The IL instruction name of a non-virtual call, which is what <c>base.M()</c> emits.</summary>
    internal const string NonVirtualCall = "call";

    private const int TwoByteOpCodePrefix = 0xFE;

    private static readonly Dictionary<short, OpCode> OpCodeTable = BuildOpCodeTable();

    private static readonly BindingFlags AllDeclared =
        BindingFlags.Static
        | BindingFlags.Instance
        | BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Finds the single method a type DECLARES ITSELF under a given name.
    /// </summary>
    /// <param name="type">The type to look in.</param>
    /// <param name="name">The method name.</param>
    /// <returns>The declared method.</returns>
    /// <remarks>
    /// <c>DeclaredOnly</c> is deliberate: an inherited member would answer the ancestor's own body and
    /// the ancestor-first assertion would then be inspecting the wrong method entirely and passing
    /// vacuously. A type that does not override the member at all therefore fails here, loudly.
    /// </remarks>
    internal static MethodInfo DeclaredMethod(Type type, string name)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type.GetMethods(AllDeclared).Single(candidate => candidate.Name == name);
    }

    /// <summary>
    /// Reads every call site of one method, in instruction order.
    /// </summary>
    /// <param name="method">The method whose body to read.</param>
    /// <returns>
    /// The call sites, ordinal 1 first, with coverage-instrumentation calls excluded and ordinals
    /// assigned after that exclusion. Empty when the method calls nothing or has no body.
    /// </returns>
    internal static IReadOnlyList<RecordedIlCall> CallsIn(MethodBase method)
    {
        ArgumentNullException.ThrowIfNull(method);

        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            return [];
        }

        List<RecordedIlCall> calls = [];

        foreach ((OpCode code, int operand) in Walk(il))
        {
            if (code != OpCodes.Call && code != OpCodes.Callvirt && code != OpCodes.Newobj)
            {
                continue;
            }

            string declaringTypeName = UnresolvedTypeName;
            string? declaringTypeFullName = null;
            string memberName = UnresolvedMemberName;

            try
            {
                MethodBase? target = method.Module.ResolveMethod(operand);

                if (target is not null)
                {
                    declaringTypeName = target.DeclaringType?.Name ?? UnresolvedTypeName;
                    declaringTypeFullName = target.DeclaringType?.FullName;
                    memberName = target.Name;
                }
            }
            catch (ArgumentException)
            {
                // A token that needs generic context. Recorded as unresolved rather than dropped -
                // see the remarks on this type for why an absence assertion cannot tolerate silence.
            }

            if (IsCoverageInstrumentation(declaringTypeFullName, memberName))
            {
                // Skipped BEFORE the ordinal is assigned, so ordinal 1 is the first call the SOURCE
                // makes whether or not the body has been rewritten for coverage collection.
                continue;
            }

            calls.Add(new RecordedIlCall(calls.Count + 1, code.Name ?? string.Empty, declaringTypeName, memberName));
        }

        return calls;
    }

    /// <summary>
    /// Whether a resolved call site was inserted by a coverage collector rather than written in source.
    /// </summary>
    /// <param name="declaringTypeFullName">
    /// The full name of the type declaring the called member, or <see langword="null"/> when the token
    /// did not resolve.
    /// </param>
    /// <param name="memberName">The called member's name.</param>
    /// <returns><see langword="true"/> when the call is instrumentation.</returns>
    /// <remarks>
    /// An UNRESOLVED token is never treated as instrumentation, so it is still reported and the
    /// "no unresolved call" assertions still bite. Treating unresolved tokens as instrumentation would
    /// turn a decoding failure into a silent pass.
    /// </remarks>
    private static bool IsCoverageInstrumentation(string? declaringTypeFullName, string memberName)
    {
        if (declaringTypeFullName is null)
        {
            return false;
        }

        return declaringTypeFullName.StartsWith(InstrumentationTypeNamePrefix, StringComparison.Ordinal)
            || InstrumentationMemberNames.Contains(memberName, StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads every call site of every method and constructor a type declares itself.
    /// </summary>
    /// <param name="type">The type to read.</param>
    /// <returns>
    /// The call sites. Ordinals restart per member, because the ordering that matters is within one
    /// method body; across a whole type, reflection's member order is not defined and no assertion in
    /// this suite depends on it.
    /// </returns>
    internal static IReadOnlyList<RecordedIlCall> CallsIn(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        List<MethodBase> members = [.. type.GetMethods(AllDeclared)];
        members.AddRange(type.GetConstructors(AllDeclared));

        return [.. members.SelectMany(CallsIn)];
    }

    private static Dictionary<short, OpCode> BuildOpCodeTable()
    {
        Dictionary<short, OpCode> table = [];

        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode code)
            {
                table[code.Value] = code;
            }
        }

        return table;
    }

    private static IEnumerable<(OpCode Code, int Operand)> Walk(byte[] il)
    {
        int offset = 0;

        while (offset < il.Length)
        {
            short value = il[offset];

            if (value == TwoByteOpCodePrefix)
            {
                value = (short)((TwoByteOpCodePrefix << 8) | il[offset + 1]);
                offset += 2;
            }
            else
            {
                offset += 1;
            }

            if (!OpCodeTable.TryGetValue(value, out OpCode code))
            {
                throw new InvalidOperationException(
                    $"Unknown IL opcode 0x{value:X4} at offset {offset}. The opcode table is built "
                        + "from the runtime's own OpCodes definitions, so this means the body used an "
                        + "instruction this runtime does not declare.");
            }

            int operandSize = OperandSizeOf(code, il, offset);
            int operand = operandSize == sizeof(int) ? BitConverter.ToInt32(il, offset) : 0;

            yield return (code, operand);

            offset += operandSize;
        }
    }

    private static int OperandSizeOf(OpCode code, byte[] il, int operandOffset)
    {
        return code.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget
                or OperandType.ShortInlineI
                or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget
                or OperandType.InlineField
                or OperandType.InlineI
                or OperandType.InlineMethod
                or OperandType.InlineSig
                or OperandType.InlineString
                or OperandType.InlineTok
                or OperandType.InlineType
                or OperandType.ShortInlineR => sizeof(int),
            OperandType.InlineI8 or OperandType.InlineR => sizeof(long),

            // A switch carries a case count followed by that many four-byte targets.
            OperandType.InlineSwitch =>
                sizeof(int) + (sizeof(int) * BitConverter.ToInt32(il, operandOffset)),
            _ => throw new InvalidOperationException(
                $"Unhandled IL operand type {code.OperandType} on opcode {code.Name}."),
        };
    }
}

/// <summary>
/// A minimal recording stand-in for the parent SQL task, narrowed to the database-error endpoint.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS TYPE IS SHARED, AND THAT IS WHY IT SURVIVES ALONGSIDE A RICHER DOUBLE.</b>
/// <c>SqlUpdateCarrierTests.cs</c> constructs it and reads <see cref="LastDbError"/> from it, standing
/// where the parent task stands so that a published database error can be observed while every
/// behaviour under test stays in production code. Its shape is therefore that file's contract as much
/// as this one's, and it is kept deliberately small so that it cannot drift into a second, competing
/// implementation of the same idea.
/// </para>
/// <para>
/// <b>THE TESTS IN THIS FILE USE <c>ScriptedCarrierParentTask</c> FROM TestDoubles.cs INSTEAD.</b> That
/// is the assembly's ORDERED CALL RECORDER: it stamps every notification with an ordinal, keeps the
/// five database-error scalars per forward, and can answer differently per notify code through its
/// notify script - which is what makes it possible to cover the row-cap arm, where a handler answering
/// 1 means CONTINUE, and the progress arm, where the same answer means STOP, in one suite. Two doubles
/// with one purpose would be redundant; two doubles with two purposes, each documented, is not.
/// </para>
/// <para>
/// The five members are the whole of <c>ICarrierParentTask</c>, which is itself the narrowing of the
/// two <c>privatewrite</c> references the legacy carrier holds
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L39-L40</c>, captured at
/// <c>:L62-L63</c>].
/// </para>
/// </remarks>
internal sealed class RecordingParentTask : ICarrierParentTask
{
    /// <inheritdoc/>
    /// <remarks>
    /// True by default, because the main-thread carrier is the ancestor and the unmarshalled path is
    /// the simpler one to reason about. A suite covering the worker path sets it false explicitly,
    /// which makes the marshalling boundary visible at the call site.
    /// </remarks>
    public bool IsMainThread { get; set; } = true;

    /// <inheritdoc/>
    public bool IsNCharBinding { get; set; }

    /// <inheritdoc/>
    public bool IsCancelled { get; set; }

    /// <summary>The value <see cref="OnNotify"/> answers. One means stop - except on the row cap.</summary>
    internal long NotifyAnswer { get; set; }

    /// <summary>The value <see cref="OnDbError"/> answers.</summary>
    internal long DbErrorAnswer { get; set; }

    /// <summary>How many notifications were raised.</summary>
    internal int NotifyCount { get; private set; }

    /// <summary>
    /// The five scalars of the most recent database-error forward, or <see langword="null"/> when
    /// none has happened.
    /// </summary>
    /// <remarks>
    /// FIVE RAW SCALARS AND NOT A STRUCTURED TYPE, reproducing
    /// <c>return #ParentTask.Event OnDBError(sqldbcode,sqlerrtext,sqlsyntax,buffer,row)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L159</c>], where it is the
    /// PARENT TASK that assembles the <c>dberrordata</c> structure and the carrier that forwards.
    /// </remarks>
    internal (long Code, string Text, string Syntax, DwBuffer Buffer, long Row)? LastDbError
    {
        get;
        private set;
    }

    /// <inheritdoc/>
    public long OnDbError(
        long sqlDbCode,
        string sqlErrText,
        string sqlSyntax,
        DwBuffer buffer,
        long row)
    {
        LastDbError = (sqlDbCode, sqlErrText, sqlSyntax, buffer, row);

        return DbErrorAnswer;
    }

    /// <inheritdoc/>
    public long OnNotify(long notifyCode, long payload, string text)
    {
        NotifyCount++;

        return NotifyAnswer;
    }
}

/// <summary>
/// Pins the item-status machine, the three-buffer model, the carrier pair and its affinity factory,
/// the ownership handover, the four worker event overrides and all six preserved defects.
/// </summary>
public sealed class DataWindowBuffersTests
{
    /// <summary>
    /// The number of statements a modest update produces, used wherever a total is needed and the
    /// exact figure is not the point.
    /// </summary>
    private const long StatementCount = 5L;

    /// <summary>
    /// A synthetic update statement. C-F: it edits a numeric column and carries no credential, key,
    /// token, password or connection string, and no assertion in this file reads its literal values.
    /// </summary>
    private const string UpdateStatement = "UPDATE COMPANY SET age=1";

    /// <summary>A synthetic delete statement, chosen for the same reason.</summary>
    private const string DeleteStatement = "DELETE FROM COMPANY WHERE id=1";

    /// <summary>A synthetic retrieval statement, chosen for the same reason.</summary>
    private const string SelectStatement = "SELECT * FROM COMPANY";

    /// <summary>
    /// The codec types that must be unreachable from the ownership-handover path.
    /// </summary>
    /// <remarks>
    /// The legacy names for what these do are <c>GetChanges</c>/<c>SetChanges</c> and
    /// <c>GetFullState</c>/<c>SetFullState</c>; the port's names are the ones below.
    /// </remarks>
    private static readonly string[] CodecTypeNames =
    [
        nameof(ChangesetCodec),
        nameof(ChangesetPayloadCodec),
        nameof(FullStateCodec),
    ];

    /// <summary>
    /// The codec entry-point member names that must be unreachable from the ownership-handover path,
    /// under both their port names and the legacy names the carrier double still uses.
    /// </summary>
    private static readonly string[] CodecMemberNames =
    [
        "TryEncode",
        "TryApply",
        "Capture",
        "Send",
        "Receive",
        "GetChanges",
        "SetChanges",
        "GetFullState",
        "SetFullState",
    ];

    #region Theory data - the status, processing and throttle matrices

    /// <summary>
    /// The whole item-status domain against the three classification predicates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every declared member of <c>ItemStatus</c> appears, plus one value outside the domain, so the
    /// matrix is exhaustive rather than illustrative and a fifth member added to the contract would
    /// make this theory fail rather than silently go uncovered.
    /// </para>
    /// <para>
    /// C-B - THE THREE PREDICATES DISAGREE, AND THE DISAGREEMENTS ARE THE LEGACY'S OWN. Each column
    /// reproduces one legacy test verbatim: delete-countable is
    /// <c>case NotModified!,DataModified!</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L57</c>], new-row is
    /// the equality <c>= NewModified!</c> that identity collection selects on
    /// [<c>n_cst_thread_task_sqlupdate.sru:L230</c>], and modified is the
    /// <c>DataModified!</c>-or-<c>NewModified!</c> pair the changeset codec stamps before
    /// <c>GetChanges</c>.
    /// </para>
    /// </remarks>
    public static TheoryData<ItemStatus, bool, bool, bool> ItemStatusPredicateMatrix =>
        new()
        {
            // status                    deleteCountable  newRow  modified
            { ItemStatus.NotModified, true, false, false },
            { ItemStatus.DataModified, true, false, true },

            // New! - inserted, not yet edited. Countable by NEITHER the delete walk nor the modified
            // pair, and it is not the status identity collection selects on either.
            { ItemStatus.New, false, false, false },

            // NewModified! - THE NEW-THEN-DELETED EXCLUSION. Absent from the legacy delete-walk case
            // list, so a row inserted, edited and then deleted before the update ran does NOT count
            // toward the progress total. It never reached the database and generates no statement, so
            // counting it would inflate the denominator of the notification the worker publishes at
            // [:L39]. It IS the new-row status and it IS modified - three different answers for one
            // status, all three preserved.
            { ItemStatus.NewModified, false, true, true },

            // A value outside the declared domain, reachable only by an out-of-range cast. All three
            // predicates answer false and NONE throws, which is exactly what a PowerScript
            // `choose case` with no `case else` arm does with an unmatched value.
            { (ItemStatus)9, false, false, false },
        };

    /// <summary>
    /// The codec discriminator: what each <c>DataWindow.Processing</c> describe answer selects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only kinds 4 and 5 - crosstab and composite, named by the legacy's own comment at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L94</c> - select the
    /// full-state path. The whole of 0 through 5 is enumerated so the boundary is pinned on both
    /// sides, and PowerBuilder's two describe markers plus the unparseable answers are included
    /// because <c>Describe</c> genuinely returns them.
    /// </para>
    /// <para>
    /// RECORDED SO THE COVERAGE IS NOT MISREAD: NO FIXTURE IN THE REPOSITORY IS CROSSTAB OR COMPOSITE.
    /// The golden master declares <c>processing=1</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L3</c>], and it is the only DataWindow in the
    /// tree carrying table-level update settings. The full-state arm is therefore exercised ONLY over a
    /// synthetic carrier - here and in the sibling full-state suite - and never against a transcribed
    /// fixture. That is a limit of the oracle, not of the implementation, and it is stated rather than
    /// papered over.
    /// </para>
    /// </remarks>
    public static TheoryData<string?, bool> ProcessingDescribeMatrix =>
        new()
        {
            // The two kinds that select full state.
            { "4", true },
            { "5", true },

            // Every other value in the contiguous run, including the golden master's own.
            { "0", false },
            { "1", false },
            { "2", false },
            { "3", false },
            { "6", false },

            // PowerBuilder's describe markers: "!" for an error and "?" for an unknown property.
            { "!", false },
            { "?", false },

            // No answer at all, and an answer that is not a number.
            { "", false },
            { null, false },
            { "not a number", false },
        };

    /// <summary>
    /// The progress throttle: three conditions, any of which releases a notification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From <c>if CPU() - _nUpdateNotifyTick &gt; 100 or _nUpdateCurrent = 1 or
    /// _nUpdateCurrent &gt;= _nUpdateTotal then</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L37</c>].
    /// </para>
    /// <para>
    /// EACH ROW IS A WHOLE UPDATE RUN, not a single statement, because the throttle's behaviour is a
    /// property of a SEQUENCE: the stamp is re-taken on every notification [<c>:L38</c>], so whether
    /// statement <i>n</i> notifies depends on when statement <i>n-1</i> notified. The expected figure
    /// is therefore the exact NOTIFICATION COUNT for the run - which is the observable the
    /// characterization comparison reads, and the reason a real clock is forbidden here (0.6.7).
    /// </para>
    /// <para>
    /// 0.8.5: every row asserts WHEN a notification fires, never how fast anything runs.
    /// </para>
    /// </remarks>
    public static TheoryData<string, long, long, long, int> ProgressThrottleMatrix =>
        new()
        {
            // because
            //   total, statementsIssued, millisecondsBetweenStatements, expectedNotifications

            // ESCAPE ONE - THE FIRST STATEMENT ALWAYS NOTIFIES. The clock has not moved at all, and
            // the total is nowhere near reached, so `_nUpdateCurrent = 1` is the only condition that
            // can be true.
            { "the first statement notifies with a frozen clock", 5L, 1L, 0L, 1 },

            // THE NEGATIVE THE THROTTLE EXISTS FOR - a mid-run statement inside the window is silent.
            { "a second statement inside the window is silent", 5L, 2L, 0L, 1 },

            // THE BOUNDARY IS STRICTLY GREATER THAN. Exactly the window does NOT release it.
            { "a gap of exactly the throttle window is still silent", 5L, 2L, 100L, 1 },

            // One millisecond past it does.
            { "a gap one millisecond past the window notifies", 5L, 2L, 101L, 2 },

            // ESCAPE TWO - THE LAST STATEMENT ALWAYS NOTIFIES. Five statements against a total of
            // five: the first and the fifth, and nothing in between.
            { "the last statement notifies with a frozen clock", 5L, 5L, 0L, 2 },

            // A long run with a frozen clock still emits exactly a start and a finish.
            { "a ten-statement run with a frozen clock notifies twice", 10L, 10L, 0L, 2 },

            // Every gap past the window means every statement notifies.
            { "every statement notifies when every gap passes the window", 5L, 5L, 101L, 5 },

            // The third condition is `>=` and not `=`, so a total the run overshoots keeps notifying
            // rather than falling silent. That matters because the total is seeded from the modified
            // count BEFORE the delete-then-insert pairs are known, so it can genuinely undercount.
            { "an overshot total keeps notifying rather than going silent", 1L, 3L, 0L, 3 },
        };

    /// <summary>
    /// The four worker event overrides, each with the ancestor type its <c>call super::</c> reaches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy scripts are <c>retrievestart</c> [<c>:L27</c>], <c>sqlpreview</c> [<c>:L30</c>],
    /// <c>updatestart</c> [<c>:L47</c>] and <c>retrieverow</c> [<c>:L63</c>], and every one of them
    /// opens with <c>call super::</c>.
    /// </para>
    /// <para>
    /// THE ANCESTOR IS NOT THE SAME TYPE FOR ALL FOUR, and that is faithful rather than untidy. The
    /// main-thread carrier scripts <c>sqlpreview</c> [<c>_ds.sru:L168-L174</c>] and does NOT script the
    /// other three, so the worker's SQL-preview ancestor is the main-thread carrier while the other
    /// three reach straight through to the buffer store's default scripts.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string> WorkerOverrides =>
        new()
        {
            { nameof(DataWindowBufferStore.OnRetrieveStart), nameof(DataWindowBufferStore) },
            { nameof(DataWindowBufferStore.OnRetrieveRow), nameof(DataWindowBufferStore) },
            { nameof(DataWindowBufferStore.OnUpdateStart), nameof(DataWindowBufferStore) },

            // The one whose ancestor is the intermediate carrier, because that is where the N-char
            // rewrite lives - which is why this particular ancestor call is load bearing.
            { nameof(DataWindowBufferStore.OnSqlPreview), nameof(DataWindowCarrier) },
        };

    #endregion

    #region Helpers

    /// <summary>
    /// A worker carrier on a stopped clock, with the assembly's ordered call recorder as its parent.
    /// </summary>
    /// <returns>The carrier, its clock and the recorder.</returns>
    /// <remarks>
    /// The clock is the <c>FakeTimeProvider</c> from <c>TestDoubles.cs</c>, which is the service's
    /// single clock seam in test (0.6.7). Its own documentation quotes this file's oracle line, and its
    /// <c>ProgressThrottleWindow</c> constant is the same 100 milliseconds the throttle compares
    /// against - so the matrix above and the production threshold cannot drift apart unnoticed.
    /// </remarks>
    private static (WorkerDataWindowCarrier Carrier, FakeTimeProvider Clock, ScriptedCarrierParentTask Task)
        NewWorker()
    {
        FakeTimeProvider clock = new();
        ScriptedCarrierParentTask task = new() { IsMainThread = false };
        WorkerDataWindowCarrier carrier = new(clock);
        carrier.OnInit(task);

        return (carrier, clock, task);
    }

    /// <summary>A main-thread carrier with the ordered call recorder as its parent.</summary>
    /// <returns>The carrier and the recorder.</returns>
    private static (DataWindowCarrier Carrier, ScriptedCarrierParentTask Task) NewMainThread()
    {
        ScriptedCarrierParentTask task = new();
        DataWindowCarrier carrier = new(new FakeTimeProvider());
        carrier.OnInit(task);

        return (carrier, task);
    }

    /// <summary>Appends <paramref name="count"/> data-modified rows to one buffer.</summary>
    /// <param name="store">The store to seed.</param>
    /// <param name="buffer">The buffer to seed.</param>
    /// <param name="count">How many rows to append.</param>
    private static void SeedModifiedRows(DataWindowBufferStore store, DwBuffer buffer, long count)
    {
        for (long index = 0L; index < count; index++)
        {
            store.AppendRow(buffer, ItemStatus.DataModified);
        }
    }

    /// <summary>Reads the low sixteen bits of a packed notification payload.</summary>
    /// <param name="payload">The payload as the carrier published it.</param>
    /// <returns>The low word.</returns>
    private static int LowWord(long payload) => Bits.LoWord((uint)payload);

    /// <summary>Reads the high sixteen bits of a packed notification payload.</summary>
    /// <param name="payload">The payload as the carrier published it.</param>
    /// <returns>The high word.</returns>
    private static int HighWord(long payload) => Bits.HiWord((uint)payload);

    /// <summary>The payloads of every notification the recorder holds, in call order.</summary>
    /// <param name="task">The recorder.</param>
    /// <returns>The payloads.</returns>
    private static IReadOnlyList<long> PayloadsOf(ScriptedCarrierParentTask task) =>
        [.. task.Notifications.Select(notification => notification.Payload)];

    /// <summary>
    /// Maps a statement-kind NAME onto the internal discriminator.
    /// </summary>
    /// <param name="name">The statement kind, spelled as the enumeration member is.</param>
    /// <returns>The discriminator.</returns>
    /// <remarks>
    /// Theory data carries the name rather than the value because <c>SqlPreviewType</c> is internal to
    /// the service assembly: naming it in the signature of a public test method would be inconsistent
    /// accessibility, and widening the type merely to be testable would be the wrong trade.
    /// </remarks>
    private static SqlPreviewType StatementKind(string name) => name switch
    {
        "Select" => SqlPreviewType.Select,
        "Insert" => SqlPreviewType.Insert,
        "Update" => SqlPreviewType.Update,
        "Delete" => SqlPreviewType.Delete,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown statement kind."),
    };

    /// <summary>The affinity a thread described the legacy way carries.</summary>
    /// <param name="isMainThread">What <c>#ParentThread.of_IsMainThread()</c> would answer.</param>
    /// <returns>The matching affinity.</returns>
    private static CarrierThreadAffinity AffinityOf(bool isMainThread) =>
        isMainThread ? CarrierThreadAffinity.MainThread : CarrierThreadAffinity.WorkerThread;

    /// <summary>Previews one update statement against the primary buffer.</summary>
    /// <param name="carrier">The carrier to raise the event on.</param>
    /// <returns>The event's answer.</returns>
    private static long PreviewUpdate(DataWindowBufferStore carrier) =>
        carrier.OnSqlPreview(SqlPreviewType.Update, UpdateStatement, DwBuffer.Primary);

    #endregion


    #region The item-status machine - classification, addressing and traversal

    /// <summary>
    /// The three classification predicates over the WHOLE item-status domain, including the value
    /// outside it.
    /// </summary>
    /// <param name="status">The status under test.</param>
    /// <param name="isDeleteCountable">
    /// Whether a row in the <c>Delete!</c> buffer with this status counts toward the update-progress
    /// total.
    /// </param>
    /// <param name="isNewRow">Whether identity collection selects this status.</param>
    /// <param name="isModified">Whether the changeset codec treats this status as modified.</param>
    /// <remarks>
    /// <para>
    /// C-B. Each column is one legacy test reproduced verbatim, and the three DISAGREE about
    /// <c>NewModified!</c> - excluded from the delete walk, selected by identity collection, and
    /// counted as modified. Collapsing them into one "is this row interesting" predicate would be the
    /// single most damaging simplification available here, because each of the three feeds a different
    /// observable: the progress denominator, the identity value arrays, and the changeset payload.
    /// </para>
    /// <para>
    /// The extension-method forms are asserted alongside the static ones, because they are a second
    /// public spelling of the same three predicates and a divergence between the two spellings would
    /// be invisible from either alone.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ItemStatusPredicateMatrix))]
    public void TheThreeStatusPredicatesReproduceTheirOwnLegacyTests(
        ItemStatus status,
        bool isDeleteCountable,
        bool isNewRow,
        bool isModified)
    {
        Assert.Equal(isDeleteCountable, ItemStatusMachine.IsDeleteCountable(status));
        Assert.Equal(isNewRow, ItemStatusMachine.IsNewRow(status));
        Assert.Equal(isModified, ItemStatusMachine.IsModified(status));

        Assert.Equal(isDeleteCountable, status.IsDeleteCountable());
        Assert.Equal(isNewRow, status.IsNewRow());
        Assert.Equal(isModified, status.IsModified());
    }

    /// <summary>
    /// C-B DEFECT 2. The delete walk asks for its rows from the buffer's count DOWN TO ONE, and the
    /// direction is directly observable because the status of each row is read through a delegate that
    /// records the order it was asked in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reproduces <c>for nRow = DeletedCount() to 1 step -1</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L55</c>].
    /// </para>
    /// <para>
    /// THE DIRECTION IS ASSERTED, NOT ASSUMED, AND THAT IS THE WHOLE POINT OF THIS TEST. A count
    /// assertion cannot see direction - reverse such a loop and the total stays right while the rows
    /// shift, which is the canonical way to produce wrong data that still passes. The sibling
    /// precedent is the <c>Filter!</c> buffer walk at
    /// <c>n_cst_thread_task_sqlupdate.sru:L237</c>, which runs backward because that buffer's row order
    /// is documented as inverted relative to the source [<c>:L235</c>] - so "correcting" a descent here
    /// would be exactly the wrong instinct.
    /// </para>
    /// <para>
    /// BOTH BOUNDS ARE ONE-BASED (R9). The walk starts at the count itself, not at the count minus one,
    /// and terminates at row 1, not at row 0.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDeleteCountableWalkRunsBackwardOverTheDeleteBuffer()
    {
        FakeDataWindowCarrier carrier = new();

        // Four rows in the delete buffer, one per declared status, so that the count and the
        // ORDER can both be read off the same run.
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.NotModified);
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.NewModified);
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.DataModified);
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.New);

        List<long> askedFor = [];

        long countable = ItemStatusMachine.CountDeleteCountable(
            carrier.DeletedCount(),
            row =>
            {
                askedFor.Add(row);

                return carrier.GetItemStatus(row, ItemStatusMachine.RowStatusColumn, DwBuffer.Delete);
            });

        // THE DIRECTION: the count first, one last, every row exactly once.
        Assert.Equal(new[] { 4L, 3L, 2L, 1L }, askedFor);

        // THE TOTAL: NotModified! and DataModified! only. The new-then-deleted row at position 2 and
        // the never-edited new row at position 4 are both excluded, deliberately.
        Assert.Equal(2L, countable);

        // An empty delete buffer asks for nothing and contributes nothing, so a walk that started at
        // the count minus one - or ran to zero - would fail here rather than pass silently.
        askedFor.Clear();
        Assert.Equal(
            0L,
            ItemStatusMachine.CountDeleteCountable(
                0L,
                row =>
                {
                    askedFor.Add(row);

                    return ItemStatus.DataModified;
                }));
        Assert.Empty(askedFor);
    }

    /// <summary>
    /// The delete walk refuses a negative count and a missing status reader, because both are
    /// structural faults rather than alternative inputs.
    /// </summary>
    [Fact]
    public void TheDeleteCountableWalkRefusesANegativeCountAndAMissingReader()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = ItemStatusMachine.CountDeleteCountable(-1L, _ => ItemStatus.NotModified);
            });
        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = ItemStatusMachine.CountDeleteCountable(1L, null!);
            });
    }

    /// <summary>
    /// ROW STATUS AND COLUMN STATUS ARE TWO DIFFERENT QUESTIONS, and the machine makes the caller say
    /// which one it means through two distinct entry points rather than by passing a bare zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy addresses a row's own status with column index zero -
    /// <c>GetItemStatus(nRow,0,Delete!)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L56</c>] - and a
    /// specific column's status with a one-based column number two lines apart from a row read:
    /// <c>:L160</c> reads the row, <c>:L162</c> reads column <c>nKeyColumns[nIndex]</c>. The ONLY thing
    /// distinguishing those two calls is whether the index is zero.
    /// </para>
    /// <para>
    /// C-K - THE CONVENTION IS SURFACED, NOT HIDDEN. The port keeps the legacy value zero, because it
    /// appears in stored recordings, but it names it a SENTINEL and gives callers
    /// <see cref="ItemStatusMachine.AddressesRowStatus"/> to ask the question and
    /// <see cref="ItemStatusMachine.TryGetColumnNumber"/> to resolve the other case. A caller that
    /// ignores the resolver's answer gets the sentinel echoed back rather than a plausible-looking
    /// column one, so the mistake cannot be silent.
    /// </para>
    /// <para>
    /// R9 - THERE IS NO REBASE. A legacy one-based column number passes through the resolver
    /// UNCHANGED, because it is already the number the published contracts carry.
    /// </para>
    /// </remarks>
    [Fact]
    public void RowStatusAndColumnStatusAreDistinctEntryPoints()
    {
        // The two accessors are separate members, so a caller must state which it means.
        Assert.True(ItemStatusMachine.AddressesRowStatus(ItemStatusMachine.RowStatusColumn));
        Assert.False(ItemStatusMachine.AddressesRowStatus(ItemStatusMachine.FirstColumnNumber));
        Assert.False(ItemStatusMachine.AddressesRowStatus(6));

        // The row sentinel is NOT a column number, and the resolver says so rather than answering one.
        Assert.False(
            ItemStatusMachine.TryGetColumnNumber(ItemStatusMachine.RowStatusColumn, out int notAColumn));
        Assert.Equal(ItemStatusMachine.RowStatusColumn, notAColumn);
        Assert.NotEqual(ItemStatusMachine.FirstColumnNumber, notAColumn);

        // A real column number resolves to ITSELF. No minus one.
        Assert.True(ItemStatusMachine.TryGetColumnNumber(1, out int first));
        Assert.Equal(1, first);
        Assert.True(ItemStatusMachine.TryGetColumnNumber(6, out int last));
        Assert.Equal(6, last);

        // The sentinels keep their legacy values, which is why they must not be renumbered.
        Assert.Equal(0, ItemStatusMachine.RowStatusColumn);
        Assert.Equal(1, ItemStatusMachine.FirstColumnNumber);
        Assert.Equal(1L, ItemStatusMachine.FirstRowNumber);
        Assert.Equal(0L, ItemStatusMachine.BeforeFirstRow);
        Assert.Equal(0L, ItemStatusMachine.NoMoreModifiedRows);

        // A negative index is outside the legacy domain entirely - and the commonest way to produce
        // one is to have rebased a one-based column number, so it fails fast.
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = ItemStatusMachine.AddressesRowStatus(-1);
            });
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = ItemStatusMachine.TryGetColumnNumber(-1, out _);
            });
    }

    /// <summary>
    /// The modified-row traversal uses the legacy seed and the legacy terminator, over BOTH buffers the
    /// update walk visits, and it visits every modified row exactly once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reproduces the legacy idiom <c>nRow = GetNextModified(0,Primary!)</c> followed by
    /// <c>do while(nRow &gt; 0)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L138-L139</c>], and the
    /// same pair over the <c>Filter!</c> buffer at <c>:L154-L155</c>. BOTH buffers are asserted because
    /// the legacy walks both - a traversal that silently only worked on the primary buffer would leave
    /// every filtered modification out of the update.
    /// </para>
    /// <para>
    /// R9 - THE SEED IS AN ARGUMENT AND THE TERMINATOR IS A RESULT, and they are separate contracts
    /// that happen to share the value zero. The seed's whole arithmetic is a single advance by one,
    /// which reaches row 1 - a move within a one-based domain, not a conversion out of one.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheModifiedRowTraversalUsesTheLegacySeedAndTerminatorOverBothBuffers()
    {
        FakeDataWindowCarrier carrier = new();

        // Primary: rows 2 and 4 modified, 1 and 3 not.
        carrier.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        carrier.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);
        carrier.AppendRow(DwBuffer.Primary, ItemStatus.New);
        carrier.AppendRow(DwBuffer.Primary, ItemStatus.NewModified);

        // Filter: row 1 modified only.
        carrier.AppendRow(DwBuffer.Filter, ItemStatus.NewModified);
        carrier.AppendRow(DwBuffer.Filter, ItemStatus.NotModified);

        // THE SEED. Passing BeforeFirstRow starts the walk; passing the previous answer continues it.
        long first = carrier.GetNextModified(ItemStatusMachine.BeforeFirstRow, DwBuffer.Primary);
        long second = carrier.GetNextModified(first, DwBuffer.Primary);

        // THE TERMINATOR. Nothing follows the last modified row.
        long exhausted = carrier.GetNextModified(second, DwBuffer.Primary);

        Assert.Equal(2L, first);
        Assert.Equal(4L, second);
        Assert.Equal(ItemStatusMachine.NoMoreModifiedRows, exhausted);

        // Each modified row exactly once, ascending, over each buffer independently.
        Assert.Equal(new[] { 2L, 4L }, carrier.EnumerateModifiedRows(DwBuffer.Primary));
        Assert.Equal(new[] { 1L }, carrier.EnumerateModifiedRows(DwBuffer.Filter));

        // AN EMPTY BUFFER TERMINATES IMMEDIATELY rather than probing row one and failing.
        Assert.Equal(
            ItemStatusMachine.NoMoreModifiedRows,
            carrier.GetNextModified(ItemStatusMachine.BeforeFirstRow, DwBuffer.Delete));
        Assert.Empty(carrier.EnumerateModifiedRows(DwBuffer.Delete));

        // A seed at or beyond the end also terminates, so a caller may pass an absurd value without
        // consequence and without overflowing the advance.
        Assert.Equal(
            ItemStatusMachine.NoMoreModifiedRows,
            carrier.GetNextModified(long.MaxValue, DwBuffer.Primary));
    }

    /// <summary>
    /// The traversal is a LIVE view: a status changed mid-walk is observed, exactly as the legacy loop
    /// observes it by re-reading the buffer on every iteration.
    /// </summary>
    [Fact]
    public void TheModifiedRowTraversalReReadsStatusOnEveryStep()
    {
        FakeDataWindowCarrier carrier = new();
        carrier.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);
        carrier.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);
        carrier.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        List<long> visited = [];

        foreach (long row in carrier.EnumerateModifiedRows(DwBuffer.Primary))
        {
            visited.Add(row);

            // Clear the NEXT row's status from inside the walk. A snapshot-based traversal would still
            // report it; the legacy would not, and neither does this.
            if (row == 1L)
            {
                carrier.SetRowStatus(2L, DwBuffer.Primary, ItemStatus.NotModified);
            }
        }

        Assert.Equal(new[] { 1L, 3L }, visited);
    }

    /// <summary>
    /// The traversal refuses an undeclared buffer, a negative seed, a negative row count and a missing
    /// status reader.
    /// </summary>
    [Fact]
    public void TheModifiedRowTraversalRefusesStructurallyInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = ItemStatusMachine.GetNextModified(
                    ItemStatusMachine.BeforeFirstRow,
                    (DwBuffer)9,
                    1L,
                    _ => ItemStatus.DataModified);
            });
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = ItemStatusMachine.GetNextModified(
                    -1L,
                    DwBuffer.Primary,
                    1L,
                    _ => ItemStatus.DataModified);
            });
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = ItemStatusMachine.GetNextModified(
                    ItemStatusMachine.BeforeFirstRow,
                    DwBuffer.Primary,
                    -1L,
                    _ => ItemStatus.DataModified);
            });
        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = ItemStatusMachine.GetNextModified(
                    ItemStatusMachine.BeforeFirstRow,
                    DwBuffer.Primary,
                    1L,
                    null!);
            });
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = ItemStatusMachine.EnumerateModifiedRows(
                    (DwBuffer)9,
                    1L,
                    _ => ItemStatus.DataModified);
            });
        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = ItemStatusMachine.EnumerateModifiedRows(DwBuffer.Primary, 1L, null!);
            });
    }

    /// <summary>
    /// The machine declares NO type of its own named <c>ItemStatus</c>: the name binds to the published
    /// contract's enumeration, and there is nothing in the service assembly to make it ambiguous.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-K, AND THE REASON THIS IS ASSERTED REFLECTIVELY RATHER THAN TRUSTED. The domain enumeration is
    /// declared once, by the published boundary in
    /// <c>shared/PowerFramework.Contracts/Proto/common.v1.proto</c>. A second type of the same name in
    /// <c>PowerFramework.Persistence.Buffers</c> would compile perfectly well here and then make every
    /// unqualified mention of <c>ItemStatus</c> ambiguous in <c>Grpc/</c>, <c>Tasks/</c> and
    /// <c>Concurrency/</c>, which all import both namespaces - a failure that would surface as
    /// CS0104 in three folders far away from the file that caused it.
    /// </para>
    /// <para>
    /// The file is named <c>Buffers/ItemStatus.cs</c> precisely because that is the concept it is about;
    /// the type it declares is named for the MACHINE instead. Asserting the absence keeps the two apart
    /// deliberately rather than by luck.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStatusMachineDeclaresNoTypeNamedItemStatus()
    {
        Assembly serviceAssembly = typeof(ItemStatusMachine).Assembly;

        // Nothing in the service assembly's Buffers namespace shadows the published enumeration.
        Assert.Null(serviceAssembly.GetType("PowerFramework.Persistence.Buffers.ItemStatus", false));

        // Nor is one nested inside the machine, which would be reachable as ItemStatusMachine.ItemStatus
        // and would read as the domain type at a glance.
        Assert.DoesNotContain(
            typeof(ItemStatusMachine).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic),
            nested => nested.Name == nameof(ItemStatus));

        // And the name genuinely binds to the published contract rather than to anything local.
        Assert.Equal("PowerFramework.Contracts.Common.V1.ItemStatus", typeof(ItemStatus).FullName);
        Assert.NotSame(serviceAssembly, typeof(ItemStatus).Assembly);

        // The published domain is the four legacy statuses with the legacy numbering. A fifth member,
        // or a renumbering to make room for a synthetic sentinel, would invalidate every stored
        // recording - AAP 0.4.5.3.
        Assert.Equal(4, Enum.GetValues<ItemStatus>().Length);
        Assert.Equal(0, (int)ItemStatus.NotModified);
        Assert.Equal(1, (int)ItemStatus.DataModified);
        Assert.Equal(2, (int)ItemStatus.New);
        Assert.Equal(3, (int)ItemStatus.NewModified);
    }

    #endregion

    #region The three-buffer model and its counts

    /// <summary>
    /// The four counts report their own buffers and nothing else.
    /// </summary>
    /// <remarks>
    /// <c>ModifiedCount</c> spans the primary AND filter buffers, which is derived from the update walk
    /// visiting both [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L172,
    /// L188</c>] while the deleted contribution is added separately
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L55-L60</c>].
    /// </remarks>
    [Fact]
    public void CountsReportTheirOwnBuffers()
    {
        DataWindowBufferStore store = new();

        SeedModifiedRows(store, DwBuffer.Primary, 3L);
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        SeedModifiedRows(store, DwBuffer.Filter, 2L);
        store.AppendRow(DwBuffer.Delete, ItemStatus.NotModified);

        Assert.Equal(4L, store.RowCount());
        Assert.Equal(2L, store.FilteredCount());
        Assert.Equal(1L, store.DeletedCount());

        // Three modified in Primary plus two in Filter. The unmodified primary row and the deleted
        // row are both excluded.
        Assert.Equal(5L, store.ModifiedCount());
    }

    /// <summary>
    /// <c>AppendRow</c> answers the new row's ONE-BASED number, which is the post-add count and never
    /// the zero-based index a list would report (R9).
    /// </summary>
    [Fact]
    public void AppendRowAnswersOneBasedRowNumbers()
    {
        DataWindowBufferStore store = new();

        Assert.Equal(1L, store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified));
        Assert.Equal(2L, store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified));
        Assert.Equal(1L, store.AppendRow(DwBuffer.Filter, ItemStatus.NotModified));
    }

    /// <summary>
    /// A row number outside 1 through the buffer's count is a structural fault and fails fast rather
    /// than answering a null status, because every measured legacy call site bounds itself by the
    /// buffer's own count.
    /// </summary>
    /// <param name="row">The unaddressable row number.</param>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(3L)]
    public void RowAccessRejectsRowNumbersOutsideTheOneBasedRange(long row)
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = store.RowAt(row, DwBuffer.Primary);
            });
    }

    /// <summary>
    /// A buffer value outside the three declared members can only arise from an out-of-range cast, so
    /// it fails fast rather than answering an empty buffer.
    /// </summary>
    [Fact]
    public void BufferResolutionRejectsAnUndeclaredBuffer()
    {
        DataWindowBufferStore store = new();
        const DwBuffer undeclared = (DwBuffer)9;

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = store.AppendRow(undeclared, ItemStatus.NotModified);
            });
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = store.GetNextModified(ItemStatusMachine.BeforeFirstRow, undeclared);
            });
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = store.RowsDiscard(1L, 1L, undeclared);
            });
    }

    /// <summary>
    /// Column index zero addresses THE ROW and any positive index addresses THAT COLUMN, which is the
    /// distinction two adjacent legacy lines rely on
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L160</c> versus
    /// <c>:L162</c>].
    /// </summary>
    [Fact]
    public void ItemStatusDistinguishesTheRowFromItsColumns()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            store.SetItemStatus(
                1L,
                ItemStatusMachine.RowStatusColumn,
                DwBuffer.Primary,
                ItemStatus.NewModified));
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            store.SetItemStatus(1L, 3, DwBuffer.Primary, ItemStatus.DataModified));

        Assert.Equal(
            ItemStatus.NewModified,
            store.GetItemStatus(1L, ItemStatusMachine.RowStatusColumn, DwBuffer.Primary));
        Assert.Equal(ItemStatus.DataModified, store.GetItemStatus(1L, 3, DwBuffer.Primary));

        // An untouched column reads NotModified, which is the status a freshly retrieved column
        // carries and the answer that makes the legacy's key-column comparison behave.
        Assert.Equal(ItemStatus.NotModified, store.GetItemStatus(1L, 4, DwBuffer.Primary));
    }

    #endregion

    #region Column values and the original values the concurrency check needs

    /// <summary>
    /// The first write per column captures the original; later writes do not overwrite it, so the
    /// concurrency check never compares against an intermediate value the database never saw.
    /// </summary>
    [Fact]
    public void OriginalValueIsCapturedOnceAndSurvivesLaterWrites()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        store.SetItemValue(1L, 2, DwBuffer.Primary, "retrieved");
        store.ResetUpdate();

        Assert.Equal("retrieved", store.GetItemValue(1L, 2, DwBuffer.Primary));
        Assert.Equal("retrieved", store.GetItemOriginalValue(1L, 2, DwBuffer.Primary));

        store.SetItemValue(1L, 2, DwBuffer.Primary, "edited once");
        store.SetItemValue(1L, 2, DwBuffer.Primary, "edited twice");

        Assert.Equal("edited twice", store.GetItemValue(1L, 2, DwBuffer.Primary));
        Assert.Equal("retrieved", store.GetItemOriginalValue(1L, 2, DwBuffer.Primary));
    }

    /// <summary>
    /// Writing a value does NOT change any item status. The legacy relies on PowerBuilder's implicit
    /// flip and exploits it with a self-assignment
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L155-L167</c>]; the port
    /// models value and status explicitly, which is what makes that workaround expressible at all.
    /// </summary>
    [Fact]
    public void WritingAValueDoesNotChangeAnyItemStatus()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        store.SetItemValue(1L, 1, DwBuffer.Primary, 42);

        Assert.Equal(
            ItemStatus.NotModified,
            store.GetItemStatus(1L, ItemStatusMachine.RowStatusColumn, DwBuffer.Primary));
        Assert.Equal(ItemStatus.NotModified, store.GetItemStatus(1L, 1, DwBuffer.Primary));
        Assert.Equal(0L, store.ModifiedCount());
    }

    /// <summary>
    /// A row reports its assigned columns in ASCENDING order however they were written. Deterministic
    /// order matters because a codec that serialized in enumeration order would otherwise produce a
    /// payload whose byte layout varied between runs, which would break the characterization
    /// comparison the parity model rests on.
    /// </summary>
    [Fact]
    public void ARowReportsItsAssignedColumnsInAscendingOrder()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        store.SetItemValue(1L, 6, DwBuffer.Primary, "birth");
        store.SetItemValue(1L, 1, DwBuffer.Primary, "id");
        store.SetItemValue(1L, 4, DwBuffer.Primary, "address");

        Assert.Equal(new[] { 1, 4, 6 }, store.RowAt(1L, DwBuffer.Primary).AssignedColumnNumbers);
    }

    /// <summary>
    /// A value is never addressed by the column-zero sentinel, so passing it is a caller fault rather
    /// than an alternative meaning (R9).
    /// </summary>
    [Fact]
    public void ValueAccessRejectsTheRowStatusSentinel()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = store.GetItemValue(1L, ItemStatusMachine.RowStatusColumn, DwBuffer.Primary);
            });
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = store.GetItemOriginalValue(1L, ItemStatusMachine.RowStatusColumn, DwBuffer.Primary);
            });
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = store.SetItemValue(1L, ItemStatusMachine.RowStatusColumn, DwBuffer.Primary, "x");
            });
    }

    // ==========================================================================================
    //  BLOB VALUES ARE ISOLATED ON EVERY CROSSING, AND THE REASON IS THE CONCURRENCY CHECK
    //  ----------------------------------------------------------------------------------------
    //  `byte[]` is the ONE value type in the published domain that is mutable, and the carrier's whole
    //  purpose is to hold a CURRENT value and its ORIGINAL side by side so that `updatewhere=1`
    //  [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14] can put the original into the generated where
    //  clause. Share one array between the two and the two stop being two: mutating the caller's array
    //  in place moves the original ALONG WITH the current, the comparison compares a value against
    //  itself, and the optimistic-concurrency check silently passes for every row - which is a lost
    //  update that no row-count assertion would notice.
    //
    //  Three crossings therefore copy: INGRESS (the write), the ORIGINAL SNAPSHOT taken from it, and
    //  EGRESS (both reads). Every other value in the domain is immutable, so it is handed over as-is.
    // ==========================================================================================

    /// <summary>
    /// A caller that mutates the array it wrote cannot reach the value the carrier holds.
    /// </summary>
    [Fact]
    public void MutatingTheArrayThatWasWrittenDoesNotReachTheStoredValue()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        byte[] written = [1, 2, 3];

        store.SetItemValue(1L, 3, DwBuffer.Primary, written);

        written[0] = 99;

        Assert.Equal(
            new byte[] { 1, 2, 3 },
            Assert.IsType<byte[]>(store.GetItemValue(1L, 3, DwBuffer.Primary)));
    }

    /// <summary>
    /// A caller that mutates the array it READ cannot reach the value the carrier holds either.
    /// </summary>
    [Fact]
    public void MutatingTheArrayThatWasReadDoesNotReachTheStoredValue()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        store.SetItemValue(1L, 3, DwBuffer.Primary, new byte[] { 1, 2, 3 });

        byte[] read = Assert.IsType<byte[]>(store.GetItemValue(1L, 3, DwBuffer.Primary));

        read[0] = 99;

        Assert.Equal(
            new byte[] { 1, 2, 3 },
            Assert.IsType<byte[]>(store.GetItemValue(1L, 3, DwBuffer.Primary)));

        // Two reads never hand back the same instance, which is what makes the guarantee hold for a
        // caller that keeps one of them.
        Assert.NotSame(read, store.GetItemValue(1L, 3, DwBuffer.Primary));
    }

    /// <summary>
    /// THE CASE THE ISOLATION EXISTS FOR: an in-place mutation must not move the ORIGINAL along with
    /// the current, because the two are what the concurrency check compares.
    /// </summary>
    [Fact]
    public void AnInPlaceMutationCannotMoveTheOriginalAlongWithTheCurrent()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        byte[] retrieved = [1, 2, 3];

        store.SetItemValue(1L, 3, DwBuffer.Primary, retrieved);
        store.ResetUpdate();

        // The edit, performed the way a careless caller would perform it - in place on the array it
        // still holds a reference to, with no second SetItemValue at all.
        retrieved[0] = 99;

        Assert.Equal(
            new byte[] { 1, 2, 3 },
            Assert.IsType<byte[]>(store.GetItemValue(1L, 3, DwBuffer.Primary)));
        Assert.Equal(
            new byte[] { 1, 2, 3 },
            Assert.IsType<byte[]>(store.GetItemOriginalValue(1L, 3, DwBuffer.Primary)));

        // And the honest edit still separates the two, which is the property the check depends on.
        store.SetItemValue(1L, 3, DwBuffer.Primary, new byte[] { 4, 5, 6 });

        Assert.Equal(
            new byte[] { 4, 5, 6 },
            Assert.IsType<byte[]>(store.GetItemValue(1L, 3, DwBuffer.Primary)));
        Assert.Equal(
            new byte[] { 1, 2, 3 },
            Assert.IsType<byte[]>(store.GetItemOriginalValue(1L, 3, DwBuffer.Primary)));
        Assert.NotSame(
            store.GetItemValue(1L, 3, DwBuffer.Primary),
            store.GetItemOriginalValue(1L, 3, DwBuffer.Primary));
    }

    /// <summary>
    /// An EMPTY blob survives as an empty blob rather than as a null or a one-element array, so the
    /// round trip has no length-dependent hole.
    /// </summary>
    /// <remarks>
    /// INSTANCE IDENTITY IS DELIBERATELY NOT ASSERTED HERE, unlike in the non-empty cases above. A
    /// zero-length array has no element to mutate, so it carries no aliasing hazard at all, and the
    /// runtime hands out a single interned instance for every zero-length array of a given element
    /// type - which means the copy taken on ingress legitimately IS the caller's instance. Asserting
    /// otherwise would be asserting a runtime implementation detail that buys no safety.
    /// </remarks>
    [Fact]
    public void AnEmptyBlobSurvivesAsAnEmptyBlob()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        store.SetItemValue(1L, 3, DwBuffer.Primary, Array.Empty<byte>());
        store.ResetUpdate();

        Assert.Empty(Assert.IsType<byte[]>(store.GetItemValue(1L, 3, DwBuffer.Primary)));
        Assert.Empty(Assert.IsType<byte[]>(store.GetItemOriginalValue(1L, 3, DwBuffer.Primary)));
    }

    /// <summary>
    /// Immutable values are handed over AS THEY ARE, because copying them would buy nothing and the
    /// isolation is deliberately narrow.
    /// </summary>
    [Fact]
    public void AnImmutableValueIsHandedOverWithoutBeingCopied()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        string written = "Contoso";

        store.SetItemValue(1L, 2, DwBuffer.Primary, written);

        Assert.Same(written, store.GetItemValue(1L, 2, DwBuffer.Primary));
    }

    #endregion

    #region The current-versus-original comparison that decides whether a value moved

    /// <summary>
    /// Pairs of values that ARE the same number however each half is spelled, and pairs that are not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One legacy <c>number</c> column has several faithful spellings on the wire, and a value that
    /// arrives back as a different CLR type is still the same number. The comparison therefore has to
    /// be numeric rather than <c>Equals</c>, whose answer depends on the boxed runtime type.
    /// </para>
    /// <para>
    /// C-B - WHY THIS IS THE CONCURRENCY CHECK'S OWN PRIMITIVE AND NOT A UTILITY. The update path asks
    /// whether a cell's CURRENT value differs from its ORIGINAL to decide whether the row changed, and
    /// on a key column that decision selects between an in-place update and the
    /// <c>updatekeyinplace=no</c> delete-plus-insert pair
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>]. A spelling difference read as a value
    /// difference therefore emits the WRONG STATEMENT PAIR for a row whose key never changed - and the
    /// storage engine disagrees with that reading anyway, because SQLite compares an integer against
    /// the same value written as a real as equal under numeric affinity.
    /// </para>
    /// <para>
    /// The narrower CLR integrals appear because the ingress widens them into the same arms, so every
    /// one of them is genuinely reachable from a caller.
    /// </para>
    /// </remarks>
    public static TheoryData<string, object?, object?, bool> ValueEquivalencePairs =>
        new()
        {
            // because, left, right, equivalent

            // NULL IS A VALUE IN THIS ALGEBRA. Two nulls are the same value; a null beside a value is
            // not; and null is NEVER folded to zero, because the framework's tri-state predicates
            // depend on the distinction.
            { "two nulls are the same value", null, null, true },
            { "a null is not zero", null, 0L, false },
            { "zero is not a null", 0m, null, false },

            // THE SAME NUMBER, SPELLED TWO WAYS. Each of these crosses the wire's four numeric arms or
            // the narrower integrals the ingress widens into them.
            { "a decimal beside an equal double", 28m, 28.0d, true },
            { "a decimal beside an equal long", 28m, 28L, true },
            { "a long beside an equal double", 28L, 28.0d, true },
            { "an unsigned long beside an equal long", 28UL, 28L, true },
            { "an int beside an equal long", 28, 28L, true },
            { "an unsigned int beside an equal decimal", 28U, 28m, true },
            { "a short beside an equal long", (short)28, 28L, true },
            { "an unsigned short beside an equal double", (ushort)28, 28.0d, true },
            { "a byte beside an equal decimal", (byte)28, 28m, true },
            { "a signed byte beside an equal long", (sbyte)28, 28L, true },
            { "a float beside an equal decimal", 28.0f, 28m, true },
            { "the evidenced salary scale, decimal beside double", 20000.50m, 20000.5d, true },

            // AND IT IS NOT A TOLERANCE. Two numbers that differ, however slightly, differ.
            { "two decimals one hundredth apart", 20000.50m, 20000.51m, false },
            { "a long beside the next long", 28L, 29L, false },
            { "a negative beside its magnitude", -28L, 28L, false },

            // A BLOB IS COMPARED BY CONTENT, because reference equality would report two arrays decoded
            // from identical bytes as different values.
            { "two blobs with the same bytes", new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }, true },
            { "two empty blobs", Array.Empty<byte>(), Array.Empty<byte>(), true },
            { "two blobs differing in one byte", new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 4 }, false },
            { "two blobs of different length", new byte[] { 1, 2 }, new byte[] { 1, 2, 3 }, false },

            // EVERYTHING ELSE FALLS THROUGH TO ORDINARY EQUALITY, and NO CROSS-FAMILY CONVERSION IS
            // INVENTED: the boundary narrows with a defined answer rather than widening with a guess.
            { "two equal strings", "Contoso", "Contoso", true },
            { "two strings differing in case", "Contoso", "contoso", false },
            { "a date beside the string that renders it", new DateOnly(2026, 1, 1), "2026-01-01", false },
            { "a boolean beside the number that encodes it", true, 1L, false },
            { "two equal booleans", true, true, true },
            { "two equal dates", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), true },
        };

    /// <summary>
    /// The same number is the same value however it is spelled, and nothing else is converted across
    /// families.
    /// </summary>
    /// <param name="because">The pair under test, named so a failure identifies the case.</param>
    /// <param name="left">One value.</param>
    /// <param name="right">The other value.</param>
    /// <param name="equivalent">Whether the two denote the same value.</param>
    /// <remarks>
    /// The comparison is asserted SYMMETRIC as well, because the update path compares current against
    /// original in one order and the changeset codec compares them in the other. An asymmetric answer
    /// would make the two disagree about whether the same row had changed.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ValueEquivalencePairs))]
    public void TheSameNumberIsTheSameValueHoweverItIsSpelled(
        string because,
        object? left,
        object? right,
        bool equivalent)
    {
        Assert.True(
            CarrierValue.AreEquivalent(left, right) == equivalent,
            $"The pair where {because} compared as {!equivalent}.");

        Assert.True(
            CarrierValue.AreEquivalent(right, left) == equivalent,
            $"The pair where {because} compared asymmetrically.");
    }

    /// <summary>
    /// Two values whose numeric magnitude is beyond <see cref="decimal"/>'s range are still compared as
    /// numbers, through the approximate arm.
    /// </summary>
    /// <remarks>
    /// Only the <see cref="double"/> spelling can produce such a value, and the exact arm cannot hold
    /// it, so the fallback is the only path on which these can be compared at all. It is a fallback and
    /// not a tolerance: two distinct magnitudes still differ.
    /// </remarks>
    [Fact]
    public void NumbersBeyondTheExactRangeAreStillComparedAsNumbers()
    {
        Assert.True(CarrierValue.AreEquivalent(double.MaxValue, double.MaxValue));
        Assert.False(CarrierValue.AreEquivalent(double.MaxValue, double.MinValue));
        Assert.True(CarrierValue.AreEquivalent(double.MinValue, double.MinValue));

        // A magnitude beyond decimal's range beside one inside it is not the same number.
        Assert.False(CarrierValue.AreEquivalent(double.MaxValue, 28m));
    }

    /// <summary>
    /// A MALFORMED wire value is REFUSED rather than coerced into a plausible one, which is the
    /// narrow-with-a-defined-answer rule applied at the decode boundary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The composite date-and-time spelling is the one with three ways to be malformed: no separator at
    /// all, an unparseable date half, and an unparseable time half. Each is refused, and the decoded
    /// value is left null rather than defaulted to an epoch - a fabricated instant would flow straight
    /// into a concurrency predicate and compare unequal to whatever the row actually holds.
    /// </para>
    /// <para>
    /// C-E. Nothing here touches a database; these are wire messages decoded in memory.
    /// </para>
    /// </remarks>
    [Fact]
    public void AMalformedWireValueIsRefusedRatherThanCoerced()
    {
        Assert.False(
            CarrierValue.TryFromWire(
                new AnyValue { DatetimeValue = new DateTimeValue { Value = "2026-01-01 12:00:00" } },
                out object? noSeparator));
        Assert.Null(noSeparator);

        Assert.False(
            CarrierValue.TryFromWire(
                new AnyValue { DatetimeValue = new DateTimeValue { Value = "not-a-dateT12:00:00" } },
                out object? badDate));
        Assert.Null(badDate);

        Assert.False(
            CarrierValue.TryFromWire(
                new AnyValue { DatetimeValue = new DateTimeValue { Value = "2026-01-01Tnot-a-time" } },
                out object? badTime));
        Assert.Null(badTime);

        // A wholly absent value is refused too, rather than decoding as null - "no value was supplied"
        // and "the value is null" are different statements and the contract keeps them apart.
        Assert.False(CarrierValue.TryFromWire(null, out object? absent));
        Assert.Null(absent);

        // And the well-formed spelling of the same shape round trips, so the refusals above are about
        // malformation rather than about the shape being unsupported.
        DateTime moment = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        Assert.True(CarrierValue.TryToWire(moment, out AnyValue? wire));
        Assert.True(CarrierValue.TryFromWire(wire, out object? decoded));
        Assert.Equal(moment, decoded);
    }

    /// <summary>
    /// The column-name table normalizes what it is given and answers the empty string outside the
    /// one-based column range, so a codec reading a name never has to guard the boundary itself.
    /// </summary>
    /// <remarks>
    /// R9. The names arrive as a list and are addressed by ONE-BASED column number, which is the only
    /// place in this file where a rebase is correct - and it is applied once, inside the accessor, after
    /// the range has been proven. Column 0 is the row-status sentinel and is outside the range by
    /// design.
    /// </remarks>
    [Fact]
    public void TheColumnNameTableNormalizesAndBoundsItself()
    {
        DataWindowBufferStore store = new();

        // No names at all is the default, and both ways of saying so are accepted.
        Assert.Empty(store.ColumnNames);

        store.SetColumnNames(null);
        Assert.Empty(store.ColumnNames);

        store.SetColumnNames([]);
        Assert.Empty(store.ColumnNames);

        // Whitespace-only entries are normalized to the empty string rather than kept, so a consumer
        // cannot accidentally emit a blank identifier that looks like a name.
        store.SetColumnNames(["id", "   ", "age"]);

        Assert.Equal(new[] { "id", string.Empty, "age" }, store.ColumnNames);

        // ONE-BASED addressing, with the sentinel and everything past the end answering empty.
        Assert.Equal("id", store.ColumnNameOf(1L));
        Assert.Equal(string.Empty, store.ColumnNameOf(2L));
        Assert.Equal("age", store.ColumnNameOf(3L));
        Assert.Equal(string.Empty, store.ColumnNameOf(ItemStatusMachine.RowStatusColumn));
        Assert.Equal(string.Empty, store.ColumnNameOf(4L));
        Assert.Equal(string.Empty, store.ColumnNameOf(-1L));

        // The table is copied on the way in, so a caller mutating its own list cannot reach it.
        List<string> caller = ["id", "name"];
        store.SetColumnNames(caller);
        caller[0] = "clobbered";

        Assert.Equal("id", store.ColumnNameOf(1L));
    }

    #endregion


    #region RowsMove, RowsDiscard and the two resets

    /// <summary>
    /// Reproduces the legacy's own most important movement:
    /// <c>RowsMove(1, FilteredCount(), Filter!, Data, RowCount() + 1, Primary!)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L109</c>], which folds the
    /// whole filter buffer onto the END of the primary buffer of the SAME carrier.
    /// </summary>
    [Fact]
    public void RowsMoveFoldsTheFilterBufferOntoTheEndOfPrimary()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        store.SetItemValue(1L, 1, DwBuffer.Primary, "primary-1");
        store.AppendRow(DwBuffer.Filter, ItemStatus.DataModified);
        store.SetItemValue(1L, 1, DwBuffer.Filter, "filtered-1");
        store.AppendRow(DwBuffer.Filter, ItemStatus.DataModified);
        store.SetItemValue(2L, 1, DwBuffer.Filter, "filtered-2");

        long answer = store.RowsMove(
            1L,
            store.FilteredCount(),
            DwBuffer.Filter,
            store,
            store.RowCount() + 1L,
            DwBuffer.Primary);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, answer);
        Assert.Equal(3L, store.RowCount());
        Assert.Equal(0L, store.FilteredCount());
        Assert.Equal("primary-1", store.GetItemValue(1L, 1, DwBuffer.Primary));
        Assert.Equal("filtered-1", store.GetItemValue(2L, 1, DwBuffer.Primary));
        Assert.Equal("filtered-2", store.GetItemValue(3L, 1, DwBuffer.Primary));
    }

    /// <summary>
    /// C-B. An empty range answers failure and moves nothing, which is what
    /// <c>RowsMove(1, FilteredCount(), ...)</c> does when nothing is filtered. The legacy ignores the
    /// answer, so no silent no-op success is invented.
    /// </summary>
    [Fact]
    public void RowsMoveAnswersFailureForAnEmptyRangeAndMovesNothing()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        long answer = store.RowsMove(
            1L,
            store.FilteredCount(),
            DwBuffer.Filter,
            store,
            store.RowCount() + 1L,
            DwBuffer.Primary);

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, answer);
        Assert.Equal(1L, store.RowCount());
        Assert.Equal(0L, store.FilteredCount());
    }

    /// <summary>
    /// A chunk moves into a separate carrier, as at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L160</c>, and a range past
    /// the end is refused.
    /// </summary>
    [Fact]
    public void RowsMoveTransfersBetweenCarriersAndRefusesAnUnaddressableRange()
    {
        DataWindowBufferStore source = new();
        DataWindowBufferStore temporary = new();
        SeedModifiedRows(source, DwBuffer.Primary, 3L);

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            source.RowsMove(1L, 2L, DwBuffer.Primary, temporary, 1L, DwBuffer.Primary));
        Assert.Equal(1L, source.RowCount());
        Assert.Equal(2L, temporary.RowCount());

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            source.RowsMove(1L, 9L, DwBuffer.Primary, temporary, 1L, DwBuffer.Primary));
        Assert.Equal(1L, source.RowCount());
        Assert.Equal(2L, temporary.RowCount());

        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = source.RowsMove(1L, 1L, DwBuffer.Primary, null!, 1L, DwBuffer.Primary);
            });
    }

    /// <summary>
    /// C-B. <c>RowsDiscard</c> is the workaround for the documented prohibition on using <c>Reset</c>
    /// to clear data between chunks
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L176-L182</c>], so it
    /// removes rows from ONE buffer and leaves the others alone - unlike <c>Reset</c>.
    /// </summary>
    [Fact]
    public void RowsDiscardRemovesFromOneBufferOnly()
    {
        DataWindowBufferStore store = new();
        SeedModifiedRows(store, DwBuffer.Primary, 3L);
        SeedModifiedRows(store, DwBuffer.Filter, 2L);

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            store.RowsDiscard(1L, 3L, DwBuffer.Primary));
        Assert.Equal(0L, store.RowCount());
        Assert.Equal(2L, store.FilteredCount());

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            store.RowsDiscard(1L, 5L, DwBuffer.Filter));
        Assert.Equal(2L, store.FilteredCount());
    }

    /// <summary>
    /// The two resets are DIFFERENT OPERATIONS and neither substitutes for the other: <c>Reset</c>
    /// removes every row from all three buffers, while <c>ResetUpdate</c> keeps every row, clears the
    /// update flags, re-baselines the original values and empties only the delete buffer.
    /// </summary>
    /// <remarks>
    /// C-B. The distinction is load bearing rather than cosmetic: the legacy documents outright that
    /// <c>Reset</c> must NOT be used to clear data between chunks because it breaks changeset
    /// application [<c>n_cst_thread_task_sqlquery.sru:L176</c>], which is why the chunked path discards
    /// rows instead.
    /// </remarks>
    [Fact]
    public void ResetAndResetUpdateAreNotInterchangeable()
    {
        DataWindowBufferStore resetTarget = new();
        SeedModifiedRows(resetTarget, DwBuffer.Primary, 2L);
        SeedModifiedRows(resetTarget, DwBuffer.Filter, 1L);
        resetTarget.AppendRow(DwBuffer.Delete, ItemStatus.NotModified);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, resetTarget.Reset());
        Assert.Equal(0L, resetTarget.RowCount());
        Assert.Equal(0L, resetTarget.FilteredCount());
        Assert.Equal(0L, resetTarget.DeletedCount());

        DataWindowBufferStore resetUpdateTarget = new();
        SeedModifiedRows(resetUpdateTarget, DwBuffer.Primary, 2L);
        resetUpdateTarget.SetItemValue(1L, 1, DwBuffer.Primary, "retrieved");
        resetUpdateTarget.SetItemStatus(1L, 5, DwBuffer.Primary, ItemStatus.DataModified);
        SeedModifiedRows(resetUpdateTarget, DwBuffer.Filter, 1L);
        resetUpdateTarget.AppendRow(DwBuffer.Delete, ItemStatus.NotModified);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, resetUpdateTarget.ResetUpdate());

        // Rows survive; only the flags and the delete buffer are cleared.
        Assert.Equal(2L, resetUpdateTarget.RowCount());
        Assert.Equal(1L, resetUpdateTarget.FilteredCount());
        Assert.Equal(0L, resetUpdateTarget.DeletedCount());
        Assert.Equal(0L, resetUpdateTarget.ModifiedCount());
        Assert.Equal(
            ItemStatus.NotModified,
            resetUpdateTarget.GetItemStatus(1L, 5, DwBuffer.Primary));

        // And the originals are re-baselined onto the current values, which is what makes freshly
        // delivered rows read as unmodified.
        Assert.Equal("retrieved", resetUpdateTarget.GetItemOriginalValue(1L, 1, DwBuffer.Primary));
    }

    #endregion

    #region The codec discriminator - DataWindow.Processing

    /// <summary>
    /// Only crosstab and composite select the full-state transfer path; every other processing kind
    /// selects the changeset path.
    /// </summary>
    /// <param name="describeResult">The answer <c>Describe("DataWindow.Processing")</c> gave.</param>
    /// <param name="expectsFullState">Whether that answer selects the full-state path.</param>
    /// <remarks>
    /// The discriminator is read from the legacy's own selector at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L93-L94</c>. See
    /// <see cref="ProcessingDescribeMatrix"/> for the note recording that no fixture in the repository
    /// is crosstab or composite - the golden master is <c>processing=1</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L3</c>].
    /// </remarks>
    [Theory]
    [MemberData(nameof(ProcessingDescribeMatrix))]
    public void OnlyCrosstabAndCompositeSelectFullStateTransfer(
        string? describeResult,
        bool expectsFullState)
    {
        DataWindowBufferStore store = new()
        {
            Processing = DataWindowProcessing.FromDescribe(describeResult),
        };

        Assert.Equal(expectsFullState, store.RequiresFullStateTransfer);
        Assert.Equal(expectsFullState, store.Processing.SelectsFullStateTransfer);
    }

    /// <summary>
    /// The two named kinds keep the numeric values the legacy dispatches on, and an unassigned carrier
    /// takes the changeset path.
    /// </summary>
    [Fact]
    public void ProcessingKindsKeepTheirLegacyNumericValues()
    {
        Assert.Equal(4L, DataWindowProcessing.Crosstab.Value);
        Assert.Equal(5L, DataWindowProcessing.Composite.Value);
        Assert.Equal(0L, DataWindowProcessing.Unassigned.Value);
        Assert.False(DataWindowProcessing.Unassigned.SelectsFullStateTransfer);
        Assert.False(new DataWindowBufferStore().RequiresFullStateTransfer);
    }

    /// <summary>
    /// The golden master takes the CHANGESET path, and the full-state arm is reachable only over a
    /// synthetic carrier - which is a limit of the oracle, recorded rather than glossed over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L3</c> declares <c>processing=1</c>, and it is the
    /// only DataWindow in the tree carrying table-level update settings [<c>:L14</c>]. So the carrier
    /// double's own full-state selector is the ONLY way either arm of the discriminator can be driven
    /// against something resembling a real carrier, and this test states that in code so a reader does
    /// not conclude the crosstab arm was verified against a fixture.
    /// </para>
    /// <para>
    /// The value is taken from the TRANSCRIBED FIXTURE rather than written as a literal here, so that
    /// this test is reading the same transcription every other fixture-driven suite reads. A literal
    /// would let the two disagree.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheGoldenMasterProcessingKindSelectsTheChangesetPath()
    {
        // The fixture's own value, read from the transcription. Not 4, not 5.
        Assert.NotEqual(DataWindowProcessing.CrosstabValue, DwSqliteFixture.ProcessingValue);
        Assert.NotEqual(DataWindowProcessing.CompositeValue, DwSqliteFixture.ProcessingValue);

        FakeDataWindowCarrier fixtureShaped = new()
        {
            Processing = new DataWindowProcessing(DwSqliteFixture.ProcessingValue),
        };

        Assert.False(fixtureShaped.RequiresFullStateTransfer);

        // And the describe-shaped route reaches the same answer for the same fixture value.
        Assert.False(
            DataWindowProcessing
                .FromDescribe(
                    DwSqliteFixture.ProcessingValue.ToString(CultureInfo.InvariantCulture))
                .SelectsFullStateTransfer);

        // The synthetic carrier is the only way to reach the other arm.
        FakeDataWindowCarrier synthetic = new();
        synthetic.SelectFullStateTransfer();

        Assert.True(synthetic.RequiresFullStateTransfer);
        Assert.Equal(DataWindowProcessing.CrosstabValue, synthetic.Processing.Value);

        synthetic.SelectFullStateTransfer(DataWindowProcessing.CompositeValue);

        Assert.True(synthetic.RequiresFullStateTransfer);
        Assert.Equal(DataWindowProcessing.CompositeValue, synthetic.Processing.Value);
    }

    #endregion


    #region The main-thread carrier - the six public members and the three replaced events

    /// <summary>
    /// The carrier publishes EXACTLY the six members the legacy declares public, with the legacy's own
    /// shapes, and keeps the statement rewriter private as the legacy prototype does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy prototype block is
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L53-L59</c>: six
    /// <c>public function</c>/<c>public subroutine</c> declarations followed by ONE
    /// <c>private function</c>. The shapes matter as much as the names -
    /// <c>of_isrowsexceeded</c> answers a boolean, <c>of_setmaxrows</c> answers a
    /// <c>long</c> rather than nothing, and <c>of_clearstate</c> is a SUBROUTINE and therefore answers
    /// nothing at all, so a port that made it return a code would be inventing a result the legacy has
    /// no equivalent of.
    /// </para>
    /// <para>
    /// Asserted reflectively as well as behaviourally because a shape is not observable from a call
    /// site that ignores the answer, and because the rewriter's PRIVACY is a fact about the surface
    /// rather than about any behaviour: it is reachable only through the SQL-preview event, which is
    /// also the only way the legacy reaches it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCarrierPublishesTheSixLegacyMembersWithTheirLegacyShapes()
    {
        Type carrier = typeof(DataWindowCarrier);

        MethodInfo rowsExceeded = MethodBodyProbe.DeclaredMethod(carrier, nameof(DataWindowCarrier.IsRowsExceeded));
        MethodInfo setMaxRows = MethodBodyProbe.DeclaredMethod(carrier, nameof(DataWindowCarrier.SetMaxRows));
        MethodInfo inserted = MethodBodyProbe.DeclaredMethod(carrier, nameof(DataWindowCarrier.GetInsertedCount));
        MethodInfo updated = MethodBodyProbe.DeclaredMethod(carrier, nameof(DataWindowCarrier.GetUpdatedCount));
        MethodInfo deleted = MethodBodyProbe.DeclaredMethod(carrier, nameof(DataWindowCarrier.GetDeletedCount));
        MethodInfo clearState = MethodBodyProbe.DeclaredMethod(carrier, nameof(DataWindowCarrier.ClearState));

        // of_isrowsexceeded() returns boolean [_ds.sru:L53, L66-L67].
        Assert.Equal(typeof(bool), rowsExceeded.ReturnType);
        Assert.Empty(rowsExceeded.GetParameters());

        // of_setmaxrows(readonly long maxrows) returns long [_ds.sru:L54, L69-L71].
        Assert.Equal(typeof(long), setMaxRows.ReturnType);
        Assert.Equal(typeof(long), Assert.Single(setMaxRows.GetParameters()).ParameterType);

        // The three counts answer long and take nothing [_ds.sru:L55-L57, L73-L80].
        foreach (MethodInfo count in new[] { inserted, updated, deleted })
        {
            Assert.Equal(typeof(long), count.ReturnType);
            Assert.Empty(count.GetParameters());
        }

        // of_clearstate is a SUBROUTINE, so it answers nothing [_ds.sru:L58, L82-L87].
        Assert.Equal(typeof(void), clearState.ReturnType);
        Assert.Empty(clearState.GetParameters());

        // And the rewriter stays private, exactly as its prototype is [_ds.sru:L59].
        MethodInfo rewriter = MethodBodyProbe.DeclaredMethod(carrier, "ReplaceNCharLiteral");

        Assert.True(rewriter.IsPrivate);
        Assert.True(rewriter.IsStatic);
        Assert.Equal(typeof(string), rewriter.ReturnType);
    }

    /// <summary>
    /// <c>SetMaxRows</c> answers the FRAMEWORK success value, which is zero, and not the DataStore
    /// success value, which is one
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L69-L71</c>]. C-B: no
    /// validation is added, so a negative cap is stored and simply behaves as no limit.
    /// </summary>
    [Fact]
    public void SetMaxRowsAnswersTheFrameworkSuccessCodeAndValidatesNothing()
    {
        (DataWindowCarrier carrier, _) = NewMainThread();

        Assert.Equal(RetCode.OK, carrier.SetMaxRows(10L));
        Assert.Equal(RetCode.OK, carrier.SetMaxRows(0L));
        Assert.Equal(RetCode.OK, carrier.SetMaxRows(-7L));

        // The two success values are different numbers, which is why the distinction is worth pinning.
        Assert.NotEqual(DataWindowBufferStore.DataStoreSuccess, RetCode.OK);
    }

    /// <summary>
    /// The update-completion event captures the three counts, and they are reported by the three
    /// getters [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L162-L166,
    /// L73-L80</c>].
    /// </summary>
    [Fact]
    public void UpdateEndCapturesTheThreeCounts()
    {
        (DataWindowCarrier carrier, _) = NewMainThread();

        carrier.OnUpdateEnd(7L, 11L, 13L);

        Assert.Equal(7L, carrier.GetInsertedCount());
        Assert.Equal(11L, carrier.GetUpdatedCount());
        Assert.Equal(13L, carrier.GetDeletedCount());
    }

    /// <summary>
    /// The update's deleted-row count and the delete BUFFER's row count are different facts that happen
    /// to have near-identical legacy names. Both are asserted together here precisely so that a future
    /// reader who conflates them sees this test fail.
    /// </summary>
    [Fact]
    public void UpdateDeletedCountIsNotTheDeleteBufferCount()
    {
        (DataWindowCarrier carrier, _) = NewMainThread();
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.NotModified);
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.NotModified);

        Assert.Equal(2L, carrier.DeletedCount());
        Assert.Equal(0L, carrier.GetDeletedCount());

        carrier.OnUpdateEnd(0L, 0L, 2L);
        carrier.ResetUpdate();

        Assert.Equal(0L, carrier.DeletedCount());
        Assert.Equal(2L, carrier.GetDeletedCount());
    }

    /// <summary>
    /// <c>ClearState</c> zeroes ALL FIVE pieces of carrier state
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L82-L87</c>]. The row cap
    /// is not directly readable, so it is observed the only way a caller can: a retrieve row past the
    /// old cap no longer notifies.
    /// </summary>
    [Fact]
    public void ClearStateZeroesAllFiveStateFields()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();

        carrier.SetMaxRows(2L);
        carrier.OnUpdateEnd(3L, 4L, 5L);
        task.NotifyResult = DataWindowBufferStore.EventContinue;

        Assert.Equal(DataWindowBufferStore.EventStop, carrier.OnRetrieveRow(3L));
        Assert.True(carrier.IsRowsExceeded());
        Assert.Equal(1, task.NotifyCount);

        carrier.ClearState();

        Assert.False(carrier.IsRowsExceeded());
        Assert.Equal(0L, carrier.GetInsertedCount());
        Assert.Equal(0L, carrier.GetUpdatedCount());
        Assert.Equal(0L, carrier.GetDeletedCount());

        // The cap is gone: a row far past the old cap neither notifies nor stops the retrieve.
        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(99L));
        Assert.Equal(1, task.NotifyCount);
    }

    /// <summary>
    /// The database-error hook carries FIVE RAW SCALARS and is deliberately NOT typed as
    /// <c>Errors/DbErrorData</c>, which is what keeps <c>Buffers/</c> dependency-free.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-K - WHY UNTYPED, STATED HERE SO IT IS NOT "TIDIED UP" LATER. In the legacy the carrier
    /// forwards five scalars and the PARENT TASK assembles the structure from them:
    /// <c>return #ParentTask.Event OnDBError(sqldbcode,sqlerrtext,sqlsyntax,buffer,row)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L159</c>] forwarding into
    /// the task's own handler. Two things follow. First, a callback over those same five scalars is the
    /// FAITHFUL shape, not a simplification of a structured one. Second, it is what keeps
    /// <c>Buffers/</c> free of a dependency on the sibling <c>Errors/</c> folder: <c>Buffers/</c> is
    /// foundational and consumes only <c>Buffers/ItemStatus.cs</c>, the published contracts and the
    /// shared kernel.
    /// </para>
    /// <para>
    /// THE ASSERTION IS THAT <c>DbErrorData</c> EXISTS AND IS STILL NOT USED HERE. Asserting only that
    /// the parameters are scalars would pass just as well if the structured type had never been
    /// written, and would therefore prove nothing about the choice. Because the type does exist, its
    /// absence from this signature is a decision.
    /// </para>
    /// <para>
    /// C-F. The statement argument carries interpolated literal values on the legacy's interpolating
    /// path. It is FORWARDED and never logged by the carrier, and the value asserted on below is
    /// synthetic and credential-free; redaction belongs to <c>Errors/SqlRedactor.cs</c>, on the way out
    /// to the wire.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDatabaseErrorHookCarriesFiveRawScalarsAndNotTheStructuredType()
    {
        MethodInfo hook = MethodBodyProbe.DeclaredMethod(
            typeof(ICarrierParentTask),
            nameof(ICarrierParentTask.OnDbError));

        ParameterInfo[] parameters = hook.GetParameters();

        Assert.Equal(5, parameters.Length);
        Assert.Equal(typeof(long), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
        Assert.Equal(typeof(string), parameters[2].ParameterType);
        Assert.Equal(typeof(DwBuffer), parameters[3].ParameterType);
        Assert.Equal(typeof(long), parameters[4].ParameterType);
        Assert.Equal(typeof(long), hook.ReturnType);

        // The structured type EXISTS, so its absence above is a decision rather than an accident.
        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(DbErrorData));
        Assert.NotEqual(typeof(DbErrorData), hook.ReturnType);
        Assert.Equal("PowerFramework.Persistence.Errors", typeof(DbErrorData).Namespace);

        // And the carrier's own event has the same five-scalar shape, because it forwards unchanged.
        Assert.Equal(
            5,
            MethodBodyProbe
                .DeclaredMethod(typeof(DataWindowBufferStore), nameof(DataWindowBufferStore.OnDbError))
                .GetParameters()
                .Length);
    }

    /// <summary>
    /// The database-error event forwards all five scalars to the parent task and PROPAGATES the hook's
    /// answer [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L159</c>]. The
    /// carrier assembles nothing and records nothing.
    /// </summary>
    [Fact]
    public void DbErrorForwardsFiveScalarsAndPropagatesTheAnswer()
    {
        (DataWindowCarrier carrier, ScriptedCarrierParentTask task) = NewMainThread();
        task.DbErrorResult = DataWindowBufferStore.EventStop;

        long answer = carrier.OnDbError(
            -1234L,
            "constraint violated",
            SelectStatement,
            DwBuffer.Filter,
            9L);

        Assert.Equal(DataWindowBufferStore.EventStop, answer);

        RecordedCarrierDbError forwarded = Assert.Single(task.DbErrors);

        Assert.Equal(1, forwarded.Ordinal);
        Assert.Equal(-1234L, forwarded.SqlDbCode);
        Assert.Equal("constraint violated", forwarded.SqlErrText);
        Assert.Equal(SelectStatement, forwarded.SqlSyntax);
        Assert.Equal(DwBuffer.Filter, forwarded.Buffer);
        Assert.Equal(9L, forwarded.Row);

        // Forwarding an error is not a notification, so the notification log stays empty.
        Assert.Empty(task.Notifications);
    }

    /// <summary>
    /// An uninitialized carrier fails fast rather than inventing a benign default: the legacy
    /// dereferences its parent reference unconditionally, and softening a structural fault into
    /// warn-and-continue would be a behavioural change dressed as robustness.
    /// </summary>
    [Fact]
    public void AnUninitializedCarrierFailsFast()
    {
        DataWindowCarrier carrier = new(new FakeTimeProvider());

        Assert.Null(carrier.ParentTask);
        Assert.Null(carrier.ParentThreadAffinity);
        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = carrier.OnDbError(0L, "text", SelectStatement, DwBuffer.Primary, 1L);
            });
        Assert.Throws<ArgumentNullException>(() => carrier.OnInit(null!));
        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = new DataWindowCarrier(null!);
            });
    }

    /// <summary>
    /// Initialization captures the parent task and derives the parent THREAD's affinity from it, which
    /// is a different fact from the carrier TYPE's own affinity
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L62-L63</c>].
    /// </summary>
    /// <param name="isMainThread">What the parent task reports about its thread.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InitializationDerivesTheParentThreadAffinity(bool isMainThread)
    {
        ScriptedCarrierParentTask task = new() { IsMainThread = isMainThread };
        DataWindowCarrier carrier = new(new FakeTimeProvider());

        carrier.OnInit(task);

        Assert.Same(task, carrier.ParentTask);
        Assert.Equal(AffinityOf(isMainThread), carrier.ParentThreadAffinity);

        // The TYPE's affinity is unaffected by which thread initialized it.
        Assert.Equal(CarrierThreadAffinity.MainThread, carrier.Affinity);
    }

    #endregion


    #region DEFECT 5 - the N-char literal rewriter

    /// <summary>
    /// The whole grammar of the N-char rewriter: every statement shape its single forward scan
    /// distinguishes.
    /// </summary>
    /// <remarks>
    /// From <c>_of_replacencharliteral</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L89-L147</c>].
    /// </remarks>
    public static TheoryData<string, string> NCharRewriteGrammar =>
        new()
        {
            // THE EMPTY INPUT ARM. `if nLen <= 0 then return ""` [:L110-L111] - the empty answer is the
            // legacy's own, reached before the scan starts at all.
            { string.Empty, string.Empty },

            // No literal to prefix, so the statement passes through the scan unchanged.
            { "SELECT 1", "SELECT 1" },

            // The ordinary case, and the same case with nothing before the literal - which is the path
            // on which the pending-segment start is still the zero sentinel when the opening quote is
            // met [:L129].
            { "SELECT 'a'", "SELECT N'a'" },
            { "'abc'", "N'abc'" },

            // TWO literals, both prefixed: the scan continues after the first rather than stopping.
            { "SELECT 'a','b'", "SELECT N'a',N'b'" },

            // An ESCAPED quote inside a literal. `nPos ++` then `continue` [:L119-L120] consumes both
            // halves, so the doubled quote does not close the literal and does not open a new one.
            { "SELECT 'a''b'", "SELECT N'a''b'" },

            // An EMPTY literal. The closing-quote arm re-opens the pending segment [:L133-L136] under
            // the legacy's own comment 覆盖空白字符串 - "covers the empty string" - and without it this
            // would come back as `SELECT N'`.
            { "SELECT ''", "SELECT N''" },

            // An UNTERMINATED literal. The scan does not require balance and the tail is appended by
            // [:L142-L144].
            { "SELECT 'a", "SELECT N'a" },

            // A literal that OPENS with an escaped quote. The escape branch itself has to open the
            // pending segment here, because the opening quote reset it one position earlier [:L118].
            { "SELECT '''a'", "SELECT N'''a'" },

            // The shape the rewriter actually meets in service: a generated update statement with
            // interpolated character literals. C-F: both values are synthetic and credential-free.
            {
                "UPDATE COMPANY SET address='x' WHERE name='y'",
                "UPDATE COMPANY SET address=N'x' WHERE name=N'y'"
            },
        };

    /// <summary>
    /// Statements that already carry at least one N-prefixed literal, and are therefore returned
    /// untouched.
    /// </summary>
    public static TheoryData<string> NCharAlreadyPrefixedStatements =>
        new()
        {
            // The plain case: the only literal is already prefixed.
            "SELECT N'a'",

            // THE ONE THAT PROVES THE TEST IS PER STATEMENT: an UNPREFIXED literal follows the
            // prefixed one and is still left alone.
            "SELECT N'a','b'",

            // And the reverse order, which proves the accumulator is DISCARDED rather than kept: the
            // scan had already rewritten the first literal into its buffer when it met the second.
            "SELECT 'a',N'b'",
        };

    /// <summary>
    /// The gate on the rewriter: which statement kinds, and which binding setting, reach it at all.
    /// </summary>
    public static TheoryData<string, bool, bool> NCharRewriteGate =>
        new()
        {
            // statementKind, isNCharBinding, expectsRewrite
            { "Update", true, true },
            { "Insert", true, true },
            { "Select", true, false },
            { "Delete", true, false },
            { "Update", false, false },
            { "Insert", false, false },
            { "Select", false, false },
            { "Delete", false, false },
        };

    /// <summary>
    /// The rewriter prefixes every string literal with <c>N</c>, and the grammar matrix is the whole of
    /// what its single forward scan distinguishes.
    /// </summary>
    /// <param name="statement">The statement as generated.</param>
    /// <param name="expected">The statement as installed.</param>
    /// <remarks>
    /// R9. The scan is <c>for nPos = 1 to nLen</c> [<c>:L113</c>] with a look-behind at
    /// <c>nPos - 1</c> [<c>:L125</c>] and a look-ahead at <c>nPos + 1</c> [<c>:L117</c>], so at the
    /// first and last positions it asks for characters outside the string. PowerBuilder's <c>Mid</c>
    /// answers the EMPTY STRING there rather than failing, which is why the first two cases below - a
    /// statement opening with a quote and one ending mid-literal - must answer rather than throw.
    /// </remarks>
    [Theory]
    [MemberData(nameof(NCharRewriteGrammar))]
    public void TheNCharRewriterPrefixesEveryLiteral(string statement, string expected)
    {
        (DataWindowCarrier carrier, ScriptedCarrierParentTask task) = NewMainThread();
        task.IsNCharBinding = true;

        long answer = carrier.OnSqlPreview(SqlPreviewType.Update, statement, DwBuffer.Primary);

        Assert.Equal(DataWindowBufferStore.EventContinue, answer);
        Assert.Equal(expected, carrier.SqlPreviewStatement);
    }

    /// <summary>
    /// C-B DEFECT 5. Once the scan meets a literal ALREADY prefixed with <c>N</c> it answers the
    /// argument ITSELF and abandons everything it had accumulated
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L125-L127</c>].
    /// </summary>
    /// <param name="statement">A statement carrying at least one already-prefixed literal.</param>
    /// <remarks>
    /// <para>
    /// TWO THINGS THIS PINS THAT AN "IDEMPOTENT REWRITE" WOULD BREAK. The answer is the SAME
    /// REFERENCE, asserted with <c>Assert.Same</c> rather than <c>Assert.Equal</c>, so an
    /// implementation that produced an equal-but-distinct string would fail here even though every text
    /// comparison passed. And the test is per STATEMENT rather than per literal: one prefixed literal
    /// leaves every OTHER literal in the same statement unprefixed, including any the scan had already
    /// rewritten, because the accumulator is discarded.
    /// </para>
    /// <para>
    /// It is a defect by any modern reading. It is also observable, so it is reproduced exactly.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(NCharAlreadyPrefixedStatements))]
    public void TheNCharRewriterAnswersItsArgumentItselfWhenAlreadyPrefixed(string statement)
    {
        (DataWindowCarrier carrier, ScriptedCarrierParentTask task) = NewMainThread();
        task.IsNCharBinding = true;

        carrier.OnSqlPreview(SqlPreviewType.Update, statement, DwBuffer.Primary);

        Assert.Same(statement, carrier.SqlPreviewStatement);
    }

    /// <summary>
    /// Both conditions must hold before anything is rewritten: the statement kind must be an update or
    /// an insert, AND the connection must use N-char binding
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L168-L169</c>]. The answer
    /// is the continue value either way, unconditionally [<c>:L173</c>].
    /// </summary>
    /// <param name="sqlTypeName">The statement kind.</param>
    /// <param name="isNCharBinding">Whether the connection uses N-char binding.</param>
    /// <param name="expectsRewrite">Whether the statement is rewritten.</param>
    [Theory]
    [MemberData(nameof(NCharRewriteGate))]
    public void TheNCharRewriteRequiresBothTheStatementKindAndTheBindingFlag(
        string sqlTypeName,
        bool isNCharBinding,
        bool expectsRewrite)
    {
        (DataWindowCarrier carrier, ScriptedCarrierParentTask task) = NewMainThread();
        task.IsNCharBinding = isNCharBinding;

        long answer = carrier.OnSqlPreview(
            StatementKind(sqlTypeName),
            "SELECT 'a'",
            DwBuffer.Primary);

        Assert.Equal(DataWindowBufferStore.EventContinue, answer);

        if (expectsRewrite)
        {
            Assert.Equal("SELECT N'a'", carrier.SqlPreviewStatement);
        }
        else
        {
            // Nothing was installed at all, which is a different fact from installing the original.
            Assert.Null(carrier.SqlPreviewStatement);
        }
    }

    /// <summary>
    /// The preview setter refuses a null statement, and the preview event refuses one too.
    /// </summary>
    /// <remarks>
    /// C-B. No SCOPE guard is reproduced alongside these. PowerBuilder documents
    /// <c>SetSqlPreview</c> as callable only from within the SQL-preview event, the legacy's one call
    /// site is inside that event [<c>_ds.sru:L170</c>], no legacy code checks the answer, and nothing
    /// in the repository states what happens elsewhere - so inventing a guard would add a failure mode
    /// the oracle does not have.
    /// </remarks>
    [Fact]
    public void SqlPreviewRefusesANullStatement()
    {
        (DataWindowCarrier carrier, _) = NewMainThread();

        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = carrier.SetSqlPreview(null!);
            });
        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = carrier.OnSqlPreview(SqlPreviewType.Update, null!, DwBuffer.Primary);
            });
    }

    /// <summary>
    /// The interception surface installs whatever it is given and answers the DataStore success value,
    /// and the installed statement is readable back - which is how the worker's accounting can run
    /// AFTER the rewrite without re-deriving it.
    /// </summary>
    /// <remarks>
    /// C-F. The statement used here is a synthetic column edit. The interception surface is never
    /// exercised in this file with a credential-shaped literal, because the one field in this whole
    /// subsystem known to carry interpolated values is this one.
    /// </remarks>
    [Fact]
    public void TheSqlPreviewSurfaceInstallsAndReportsTheStatement()
    {
        DataWindowBufferStore store = new();

        Assert.Null(store.SqlPreviewStatement);
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, store.SetSqlPreview(UpdateStatement));
        Assert.Equal(UpdateStatement, store.SqlPreviewStatement);

        // A second installation replaces the first rather than accumulating.
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, store.SetSqlPreview(DeleteStatement));
        Assert.Equal(DeleteStatement, store.SqlPreviewStatement);

        // The empty statement is a legal installation and is not treated as an absence.
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, store.SetSqlPreview(string.Empty));
        Assert.Equal(string.Empty, store.SqlPreviewStatement);
        Assert.NotNull(store.SqlPreviewStatement);
    }

    #endregion


    #region The four worker overrides - ancestor first, then cancellation

    /// <summary>
    /// ORDERING IS CONTRACT. Every one of the four worker overrides calls its ANCESTOR FIRST, before it
    /// does anything else at all - including before it asks whether the task was cancelled.
    /// </summary>
    /// <param name="eventName">The overridden event.</param>
    /// <param name="ancestorTypeName">The type whose body the <c>call super::</c> reaches.</param>
    /// <remarks>
    /// <para>
    /// The legacy writes <c>call super::&lt;event&gt;</c> as the FIRST STATEMENT of each script -
    /// <c>retrievestart</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L27</c>,
    /// <c>sqlpreview</c> at <c>:L30</c>, <c>updatestart</c> at <c>:L47</c> and <c>retrieverow</c> at
    /// <c>:L63</c> - and in one of the four it is load bearing: the SQL-preview ancestor is what
    /// performs the N-char rewrite [<c>_ds.sru:L168-L174</c>], so flattening the pair would move that
    /// rewrite AFTER the progress accounting instead of before it.
    /// </para>
    /// <para>
    /// HOW THE ORDER IS OBSERVED, AND WHY THAT TOOL. Three of the four ancestor bodies do nothing but
    /// answer the continue value - the PowerBuilder <c>datastore</c> scripts none of those three events
    /// - so a call to them leaves no trace any test double could record. Reading the compiled body is
    /// therefore the only honest way to assert the order for those three; see
    /// <see cref="MethodBodyProbe"/> for the full reasoning. The check is exact in three respects: the
    /// ancestor call is the FIRST call site in instruction order, the instruction is the NON-VIRTUAL
    /// <c>call</c> that <c>base.M()</c> emits rather than the <c>callvirt</c> that <c>this.M()</c> would
    /// emit, and the target is declared on a genuine base type. The load-bearing fourth is
    /// additionally asserted behaviourally, immediately below, so the two techniques corroborate each
    /// other.
    /// </para>
    /// <para>
    /// AAP 0.4.5.4 - the thread-affinity annotations are a contract, not commentary. This is that
    /// contract, at instruction level.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(WorkerOverrides))]
    public void EveryWorkerOverrideCallsItsAncestorFirst(string eventName, string ancestorTypeName)
    {
        MethodInfo overridden = MethodBodyProbe.DeclaredMethod(typeof(WorkerDataWindowCarrier), eventName);

        // It really is an override declared on the worker, not an inherited member read by accident.
        Assert.Equal(typeof(WorkerDataWindowCarrier), overridden.DeclaringType);
        Assert.True(overridden.IsVirtual);
        Assert.Equal(
            typeof(DataWindowBufferStore),
            overridden.GetBaseDefinition().DeclaringType);

        IReadOnlyList<RecordedIlCall> calls = MethodBodyProbe.CallsIn(overridden);

        Assert.NotEmpty(calls);

        // No call site in the body is unresolved, so everything below rests on a fully read body rather
        // than on a partially decoded one.
        Assert.DoesNotContain(
            calls,
            call => call.DeclaringTypeName == MethodBodyProbe.UnresolvedTypeName);

        // ASSERTION ONE - THE ANCESTOR CALL IS THE FIRST CALL THE SOURCE MAKES.
        RecordedIlCall ancestorCall = calls[0];

        Assert.Equal(1, ancestorCall.Ordinal);
        Assert.Equal(MethodBodyProbe.NonVirtualCall, ancestorCall.Instruction);
        Assert.Equal(eventName, ancestorCall.MemberName);
        Assert.Equal(ancestorTypeName, ancestorCall.DeclaringTypeName);

        // ASSERTION TWO - NOTHING THE OVERRIDE ITSELF DOES PRECEDES IT.
        //
        // This one needs no instrumentation filter and is therefore the assertion that cannot be
        // weakened by a change of coverage collector: whatever else a rewritten body contains, the
        // override's OWN work is its cancellation check and its accounting, and both reach the parent
        // task. If any parent-task call preceded the ancestor call, the ancestor would no longer be
        // first in any meaningful sense - which is exactly the divergence the legacy's
        // `call super::<event>` first-statement placement forbids.
        int ancestorIndex = -1;

        for (int index = 0; index < calls.Count; index++)
        {
            RecordedIlCall call = calls[index];

            if (call.Instruction == MethodBodyProbe.NonVirtualCall
                && call.MemberName == eventName
                && call.DeclaringTypeName == ancestorTypeName)
            {
                ancestorIndex = index;
                break;
            }

            // Reached only if some other call came first, which is the failure this loop detects.
            Assert.False(
                call.DeclaringTypeName == nameof(ICarrierParentTask)
                    || call.MemberName == "RequireParentTask"
                    || call.DeclaringTypeName == nameof(DataWindowBufferStore)
                    || call.DeclaringTypeName == nameof(DataWindowCarrier)
                    || call.DeclaringTypeName == nameof(WorkerDataWindowCarrier),
                $"{eventName} calls {call.DeclaringTypeName}.{call.MemberName} at position "
                    + $"{call.Ordinal}, BEFORE its ancestor. The legacy writes `call super::` as the "
                    + "first statement of the script, and the ordering is contract - the SQL-preview "
                    + "ancestor is what performs the N-char rewrite, so anything before it observes a "
                    + "statement that has not been rewritten yet.");
        }

        Assert.True(
            ancestorIndex == 0,
            $"{eventName} does not call its ancestor as its first call; the ancestor call was found at "
                + $"index {ancestorIndex}.");
    }

    /// <summary>
    /// THE BEHAVIOURAL COROLLARY, on the one override whose ancestor is observable: a CANCELLED task
    /// still gets the N-char rewrite, because the ancestor runs before the cancellation check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy script is one line, and the order within it is the whole point:
    /// <c>call super::sqlpreview;if #ParentTask.of_IsCancelled() then return 1</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L30</c>]. An
    /// implementation that checked cancellation first would answer the same stop value and would leave
    /// the statement UNREWRITTEN - so this is a directly observable difference, and it is the strongest
    /// evidence available that the ancestor really does run first.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSqlPreviewAncestorRunsEvenWhenTheTaskIsAlreadyCancelled()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();
        task.IsNCharBinding = true;
        task.IsCancelled = true;

        long answer = carrier.OnSqlPreview(
            SqlPreviewType.Update,
            "UPDATE COMPANY SET address='x'",
            DwBuffer.Primary);

        // The event stops, as cancellation requires...
        Assert.Equal(DataWindowBufferStore.EventStop, answer);

        // ...and the ancestor had ALREADY rewritten the statement by then.
        Assert.Equal("UPDATE COMPANY SET address=N'x'", carrier.SqlPreviewStatement);

        // Cancellation short-circuits the accounting, so nothing was notified.
        Assert.Empty(task.Notifications);
    }

    /// <summary>
    /// THE SECOND BEHAVIOURAL COROLLARY: on the uncancelled path the rewrite is in place BEFORE the
    /// progress notification is raised, observed from inside the notification handler itself.
    /// </summary>
    /// <remarks>
    /// This is the ordering that would break if the pair were flattened. The recorder's notify script
    /// runs at the exact moment the carrier raises the notification, so reading the installed statement
    /// from inside it reports the state as of that instant rather than afterwards.
    /// </remarks>
    [Fact]
    public void TheSqlPreviewRewriteIsInPlaceBeforeTheProgressNotificationIsRaised()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();
        task.IsNCharBinding = true;
        SeedModifiedRows(carrier, DwBuffer.Primary, StatementCount);
        carrier.OnUpdateStart();

        string? installedWhenNotified = null;

        task.NotifyScript = _ =>
        {
            installedWhenNotified = carrier.SqlPreviewStatement;

            return DataWindowBufferStore.EventContinue;
        };

        long answer = carrier.OnSqlPreview(
            SqlPreviewType.Update,
            "UPDATE COMPANY SET address='x'",
            DwBuffer.Primary);

        Assert.Equal(DataWindowBufferStore.EventContinue, answer);
        Assert.Single(task.Notifications);
        Assert.Equal("UPDATE COMPANY SET address=N'x'", installedWhenNotified);
    }

    /// <summary>
    /// All four worker overrides answer the stop value the moment the parent task reports cancellation
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L27, L30, L49,
    /// L63</c>].
    /// </summary>
    [Fact]
    public void EveryWorkerOverrideStopsOnCancellation()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();
        task.IsCancelled = true;

        Assert.Equal(DataWindowBufferStore.EventStop, carrier.OnRetrieveStart());
        Assert.Equal(DataWindowBufferStore.EventStop, carrier.OnRetrieveRow(1L));
        Assert.Equal(DataWindowBufferStore.EventStop, carrier.OnUpdateStart());
        Assert.Equal(DataWindowBufferStore.EventStop, PreviewUpdate(carrier));
        Assert.Empty(task.Notifications);
    }

    /// <summary>
    /// Uncancelled, the three overrides whose legacy scripts fall off their end answer zero - which is
    /// the PowerScript default for a <c>long</c> event and NOT the ancestor's value. The contrast is the
    /// SQL preview, which writes <c>return AncestorReturnValue</c> explicitly at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L44</c> precisely
    /// because falling off the end does NOT do it.
    /// </summary>
    [Fact]
    public void UncancelledOverridesAnswerTheContinueValue()
    {
        (WorkerDataWindowCarrier carrier, _, _) = NewWorker();

        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveStart());
        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnUpdateStart());
        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(1L));
    }

    /// <summary>
    /// The worker carrier reports the worker affinity, from its header annotation
    /// <c>[运行在子线程]</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L2</c>], and refuses
    /// to be built without a clock.
    /// </summary>
    [Fact]
    public void TheWorkerCarrierReportsTheWorkerAffinity()
    {
        (WorkerDataWindowCarrier carrier, _, _) = NewWorker();

        Assert.Equal(CarrierThreadAffinity.WorkerThread, carrier.Affinity);
        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = new WorkerDataWindowCarrier(null!);
            });
    }

    /// <summary>
    /// The worker carrier declares the legacy's own member set and NOTHING MORE: three private counters
    /// and four event overrides, with no function of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy type body is 73 lines and declares <c>private long _nUpdateCurrent</c>,
    /// <c>_nUpdateTotal</c> and <c>_nUpdateNotifyTick</c> [<c>:L13-L17</c>] plus four events. It has no
    /// <c>forward prototypes</c> block at all, so it declares no function.
    /// </para>
    /// <para>
    /// The port adds exactly two members the legacy expresses differently rather than not at all: the
    /// affinity property, which reproduces the header annotation that PowerBuilder carries as a comment
    /// [<c>:L2</c>], and the throttle predicate, which is the legacy's inline three-condition
    /// disjunction [<c>:L37</c>] given a name. Both are private or a property override, and neither
    /// widens the type's public surface. Pinning the count here means a future member cannot be added
    /// silently.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheWorkerCarrierDeclaresOnlyTheLegacyMemberSet()
    {
        Type worker = typeof(WorkerDataWindowCarrier);

        BindingFlags declared = BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        // THE THREE COUNTERS, and no fourth field. The throttle threshold is a const rather than a
        // field, so it does not appear here - a const is the port of a literal, not of state.
        string[] fields = [.. worker.GetFields(declared).Where(field => !field.IsLiteral).Select(field => field.Name).Order()];

        Assert.Equal(3, fields.Length);

        // THE FOUR OVERRIDES, and nothing else public or internal. The remaining declared methods are
        // the affinity property's getter and the private throttle predicate.
        string[] eventOverrides =
        [
            nameof(DataWindowBufferStore.OnRetrieveStart),
            nameof(DataWindowBufferStore.OnRetrieveRow),
            nameof(DataWindowBufferStore.OnUpdateStart),
            nameof(DataWindowBufferStore.OnSqlPreview),
        ];

        foreach (string eventName in eventOverrides)
        {
            Assert.Equal(
                worker,
                MethodBodyProbe.DeclaredMethod(worker, eventName).DeclaringType);
        }

        // NOT overridden: the update-completion and database-error events, which the MAIN-THREAD
        // carrier replaces [_ds.sru:L159, L162-L166] and the worker leaves alone. A worker override of
        // either would be inventing behaviour the oracle does not have.
        Assert.DoesNotContain(
            worker.GetMethods(declared),
            method => method.Name == nameof(DataWindowBufferStore.OnUpdateEnd)
                || method.Name == nameof(DataWindowBufferStore.OnDbError));

        // The type is sealed, because the legacy has no third carrier deriving from this one.
        Assert.True(worker.IsSealed);
    }

    #endregion

    #region DEFECT 2 - the deleted-row contribution to the progress total

    /// <summary>
    /// C-B DEFECT 2 and R9. The update-start walk seeds the total from the modified count and then adds
    /// the DELETE-COUNTABLE rows, walking the delete buffer from its count down to 1
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L52-L60</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// The total is observed the only way a caller can - through the HIGH word of the first progress
    /// payload - and it pins three things at once: the two-status case list, which excludes a
    /// new-then-deleted row because it never reached the database and generates no statement; and BOTH
    /// bounds of the walk, since a walk starting one row short would answer 2 and one running to zero
    /// would fail on an unaddressable row.
    /// </para>
    /// <para>
    /// THE DIRECTION of this particular loop cannot be observed from a count, which is why the
    /// observable direction assertion lives on
    /// <see cref="TheDeleteCountableWalkRunsBackwardOverTheDeleteBuffer"/> - the machine method that
    /// expresses the identical walk through a status reader whose call order IS recordable. Claiming a
    /// count test covered the direction would be worse than saying so plainly.
    /// </para>
    /// </remarks>
    [Fact]
    public void UpdateStartAddsOnlyDeleteCountableRowsToTheProgressTotal()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();

        SeedModifiedRows(carrier, DwBuffer.Primary, 1L);
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.NotModified);
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.NewModified);
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.DataModified);
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.New);

        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnUpdateStart());

        // One modified primary row, plus NotModified! and DataModified! from the delete buffer.
        // NewModified! and New! are absent from the legacy case list and are correctly excluded.
        PreviewUpdate(carrier);

        RecordedNotification progress = Assert.Single(task.Notifications);

        Assert.Equal(3, HighWord(progress.Payload));
        Assert.Equal(1, LowWord(progress.Payload));
    }

    /// <summary>
    /// Update-start RESETS the accounting, so a second update over the same carrier starts from
    /// statement one again rather than continuing the first update's count
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L51-L53</c>].
    /// </summary>
    /// <remarks>
    /// The notify tick is re-stamped from the clock at <c>:L53</c> as part of the same reset, which is
    /// why the second update's first statement notifies on the first-statement escape rather than on an
    /// elapsed interval inherited from the first update.
    /// </remarks>
    [Fact]
    public void UpdateStartResetsTheProgressAccounting()
    {
        (WorkerDataWindowCarrier carrier, FakeTimeProvider clock, ScriptedCarrierParentTask task) =
            NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, 4L);

        carrier.OnUpdateStart();
        PreviewUpdate(carrier);
        PreviewUpdate(carrier);

        Assert.Single(task.Notifications);
        Assert.Equal(1, LowWord(PayloadsOf(task)[0]));

        // A whole throttle window passes with no statement at all, then a second update begins.
        clock.AdvanceMilliseconds(1_000L);
        task.Clear();
        carrier.OnUpdateStart();
        PreviewUpdate(carrier);

        RecordedNotification restarted = Assert.Single(task.Notifications);

        Assert.Equal(1, LowWord(restarted.Payload));
        Assert.Equal(4, HighWord(restarted.Payload));
    }

    #endregion

    #region DEFECT 1 - the uncounted delete-then-insert arm

    /// <summary>
    /// C-B DEFECT 1. A DELETE preview against the <c>Primary!</c> or <c>Filter!</c> buffer answers the
    /// ancestor's value WITHOUT advancing the progress counter
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L32-L35</c>].
    /// </summary>
    /// <param name="buffer">The buffer the delete preview names.</param>
    /// <remarks>
    /// <para>
    /// THE MISSING INCREMENT IS THE BEHAVIOUR. A key change on a carrier whose definition says
    /// <c>updatekeyinplace=no</c> is performed as a DELETE PLUS AN INSERT, and the pair is one logical
    /// statement for progress purposes, so counting the delete half as well would double-count it. The
    /// delete half is identifiable exactly as this arm identifies it: a delete preview against a row
    /// still sitting in the <c>Primary!</c> or <c>Filter!</c> buffer rather than in <c>Delete!</c>,
    /// because the user never deleted it.
    /// </para>
    /// <para>
    /// THE ARM IS EXERCISED, NOT HYPOTHETICAL. The golden master declares
    /// <c>updatekeyinplace=no</c> [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>] and its
    /// <c>id</c> column is both key and identity [<c>:L8</c>], so any key edit takes this path.
    /// </para>
    /// <para>
    /// The arm is BUFFER-SENSITIVE, which is what makes it identifiable at all, so the contrast case
    /// against the <c>Delete!</c> buffer is asserted immediately below.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(DwBuffer.Primary)]
    [InlineData(DwBuffer.Filter)]
    public void ADeletePreviewAgainstPrimaryOrFilterIsNotCounted(DwBuffer buffer)
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, StatementCount);
        carrier.OnUpdateStart();

        long answer = carrier.OnSqlPreview(SqlPreviewType.Delete, DeleteStatement, buffer);

        // The ancestor's value, which is the continue value, and no notification at all: the counter is
        // still zero, so not even the first-statement condition fires. Note that the arm returns the
        // ANCESTOR's value rather than zero - it is a short circuit out of the accounting, not an abort.
        Assert.Equal(DataWindowBufferStore.EventContinue, answer);
        Assert.Empty(task.Notifications);

        // The very next preview is therefore statement number ONE, not two.
        PreviewUpdate(carrier);

        RecordedNotification progress = Assert.Single(task.Notifications);

        Assert.Equal(1, LowWord(progress.Payload));
    }

    /// <summary>
    /// The contrast case: a delete preview against the <c>Delete!</c> buffer is a real deletion and IS
    /// counted, so it becomes statement number one.
    /// </summary>
    [Fact]
    public void ADeletePreviewAgainstTheDeleteBufferIsCounted()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, StatementCount);
        carrier.OnUpdateStart();

        long answer = carrier.OnSqlPreview(SqlPreviewType.Delete, DeleteStatement, DwBuffer.Delete);

        Assert.Equal(DataWindowBufferStore.EventContinue, answer);

        RecordedNotification progress = Assert.Single(task.Notifications);

        Assert.Equal((long)SqlUpdateTaskNotifyCode.Progress, progress.NotifyCode);
        Assert.Equal(1, LowWord(progress.Payload));
    }

    /// <summary>
    /// An INSERT preview is counted from either buffer, which is the other half of the delete-then-insert
    /// pair and confirms the uncounted arm is specific to the DELETE kind rather than to the buffer
    /// alone [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L32</c>].
    /// </summary>
    [Fact]
    public void AnInsertPreviewIsCountedFromThePrimaryBuffer()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, StatementCount);
        carrier.OnUpdateStart();

        long answer = carrier.OnSqlPreview(
            SqlPreviewType.Insert,
            "INSERT INTO COMPANY (age) VALUES (1)",
            DwBuffer.Primary);

        Assert.Equal(DataWindowBufferStore.EventContinue, answer);
        Assert.Equal(1, LowWord(Assert.Single(task.Notifications).Payload));
    }

    /// <summary>
    /// A SELECT preview is never progress, so the whole accounting block is skipped
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L31</c>].
    /// </summary>
    [Fact]
    public void ASelectPreviewIsNeverProgress()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, StatementCount);
        carrier.OnUpdateStart();

        Assert.Equal(
            DataWindowBufferStore.EventContinue,
            carrier.OnSqlPreview(SqlPreviewType.Select, SelectStatement, DwBuffer.Primary));
        Assert.Empty(task.Notifications);
    }

    #endregion


    #region The progress throttle - a named determinism seam

    /// <summary>
    /// The throttle's three conditions, driven as whole update runs through the injected clock.
    /// </summary>
    /// <param name="because">The condition under test, named so a failure identifies the case.</param>
    /// <param name="total">The number of modified rows, which seeds the progress total.</param>
    /// <param name="statementsIssued">How many statements the run previews.</param>
    /// <param name="millisecondsBetweenStatements">
    /// How far the clock advances before each statement after the first.
    /// </param>
    /// <param name="expectedNotifications">The exact number of notifications the run must emit.</param>
    /// <remarks>
    /// <para>
    /// From <c>if CPU() - _nUpdateNotifyTick &gt; 100 or _nUpdateCurrent = 1 or
    /// _nUpdateCurrent &gt;= _nUpdateTotal then</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L37</c>]. Three
    /// disjuncts: the elapsed interval, the FIRST statement, and the LAST. The two escapes exist so
    /// that an update always emits at least a start and a finish however fast it runs.
    /// </para>
    /// <para>
    /// 0.6.7 - THIS IS THE DETERMINISM SEAM, AND IT IS DRIVEN ONLY THROUGH THE INJECTED CLOCK. No test
    /// in this file sleeps, delays, or reads a real clock. The reason is not tidiness: the assertion
    /// below is an exact notification COUNT, and with a real clock that count would vary run to run,
    /// which would make the recorded progress stream incomparable between the legacy capture and the
    /// target capture. <c>FakeTimeProvider.ProgressThrottleWindow</c> is the same 100 milliseconds the
    /// production threshold compares against, and the matrix states its gaps relative to it, so the two
    /// cannot drift apart silently.
    /// </para>
    /// <para>
    /// 0.8.5 - NO PERFORMANCE CLAIM IS MADE OR IMPLIED. Every row asserts WHEN a notification fires as a
    /// function of a clock the test controls. Nothing asserts how long anything takes, and nothing would
    /// change if the implementation were a thousand times slower.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ProgressThrottleMatrix))]
    public void TheProgressThrottleFiresOnItsThreeConditionsAndNoOthers(
        string because,
        long total,
        long statementsIssued,
        long millisecondsBetweenStatements,
        int expectedNotifications)
    {
        (WorkerDataWindowCarrier carrier, FakeTimeProvider clock, ScriptedCarrierParentTask task) =
            NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, total);
        carrier.OnUpdateStart();

        for (long statement = 1L; statement <= statementsIssued; statement++)
        {
            if (statement > 1L)
            {
                clock.AdvanceMilliseconds(millisecondsBetweenStatements);
            }

            Assert.Equal(DataWindowBufferStore.EventContinue, PreviewUpdate(carrier));
        }

        Assert.True(
            task.NotifyCount == expectedNotifications,
            $"The run in which {because} emitted {task.NotifyCount} progress notifications, "
                + $"but {expectedNotifications} were expected.");

        // Every notification carries the update task's progress code and the empty string argument,
        // which both carrier call sites pass.
        Assert.All(
            task.Notifications,
            notification =>
            {
                Assert.Equal((long)SqlUpdateTaskNotifyCode.Progress, notification.NotifyCode);
                Assert.Equal(string.Empty, notification.Text);
            });

        // The matrix's own gaps are stated relative to the shared window constant, so a change to
        // either side shows up here rather than as a mysterious count.
        Assert.Equal(100L, (long)FakeTimeProvider.ProgressThrottleWindow.TotalMilliseconds);
    }

    /// <summary>
    /// The boundary is STRICTLY greater than the window, and the interval restarts from each
    /// NOTIFICATION rather than from the start of the update.
    /// </summary>
    /// <remarks>
    /// The stamp is re-taken at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L38</c>, BEFORE the
    /// notification is raised, so the handler's own duration does not extend the next interval. A
    /// statement that does NOT notify leaves the stamp alone, which is why the second silent statement
    /// below does not reset the clock it is measured against.
    /// </remarks>
    [Fact]
    public void TheThrottleBoundaryIsStrictAndTheIntervalRestartsFromEachNotification()
    {
        (WorkerDataWindowCarrier carrier, FakeTimeProvider clock, ScriptedCarrierParentTask task) =
            NewWorker();

        // A total of one hundred keeps the reached-the-total condition out of the way entirely.
        SeedModifiedRows(carrier, DwBuffer.Primary, 100L);
        carrier.OnUpdateStart();

        long window = (long)FakeTimeProvider.ProgressThrottleWindow.TotalMilliseconds;

        // Statement 1 notifies on the first-statement condition and re-stamps the clock.
        PreviewUpdate(carrier);
        Assert.Equal(1, task.NotifyCount);

        // Exactly the window later: NOT greater than the window, so no notification - and, crucially,
        // no re-stamp either.
        clock.AdvanceMilliseconds(window);
        PreviewUpdate(carrier);
        Assert.Equal(1, task.NotifyCount);

        // One millisecond more, so one past the window measured from the last NOTIFICATION: it fires.
        clock.AdvanceMilliseconds(1L);
        PreviewUpdate(carrier);
        Assert.Equal(2, task.NotifyCount);

        // And the interval restarts from that notification rather than from the update's start.
        clock.AdvanceMilliseconds(window);
        PreviewUpdate(carrier);
        Assert.Equal(2, task.NotifyCount);

        clock.AdvanceMilliseconds(1L);
        PreviewUpdate(carrier);
        Assert.Equal(3, task.NotifyCount);
    }

    /// <summary>
    /// A progress handler answering one STOPS the update, which is the ORDINARY meaning of that answer
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L39-L41</c>] - and the
    /// exact opposite of what it means on the row-cap notification.
    /// </summary>
    [Fact]
    public void AProgressHandlerAnsweringOneStopsTheUpdate()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, StatementCount);
        carrier.OnUpdateStart();
        task.NotifyResult = DataWindowBufferStore.EventStop;

        Assert.Equal(DataWindowBufferStore.EventStop, PreviewUpdate(carrier));
        Assert.Single(task.Notifications);
    }

    /// <summary>
    /// A progress handler answering anything OTHER than one lets the update continue, and the answer the
    /// event gives is the ANCESTOR's rather than the handler's
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L44</c>].
    /// </summary>
    /// <remarks>
    /// The distinction is worth an assertion of its own: the legacy tests the handler's answer for
    /// EQUALITY WITH ONE [<c>:L39</c>] rather than for truthiness, so an answer of 2 - or of -1 -
    /// continues. A port that treated any non-zero answer as a stop would abort updates the legacy would
    /// have completed.
    /// </remarks>
    [Fact]
    public void AProgressHandlerAnsweringSomethingElseLetsTheUpdateContinue()
    {
        (WorkerDataWindowCarrier carrier, FakeTimeProvider clock, ScriptedCarrierParentTask task) =
            NewWorker();

        // A total of one hundred keeps the reached-the-total escape out of the way, so each statement
        // below notifies because the clock was advanced past the window and for no other reason.
        SeedModifiedRows(carrier, DwBuffer.Primary, 100L);
        carrier.OnUpdateStart();

        foreach (long handlerAnswer in new[] { 2L, -1L, 99L })
        {
            task.Clear();
            task.NotifyResult = handlerAnswer;
            clock.AdvanceMilliseconds(101L);

            Assert.Equal(DataWindowBufferStore.EventContinue, PreviewUpdate(carrier));
            Assert.Single(task.Notifications);
        }
    }

    #endregion

    #region DEFECT 4 - the sixteen-bit progress payload

    /// <summary>
    /// The payload packs the current statement in the LOW word and the total in the HIGH word, which is
    /// the order the legacy's own comment states
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L32</c>,
    /// <c>lparam:(low word:current,high word:total)</c>].
    /// </summary>
    [Fact]
    public void TheProgressPayloadPacksCurrentLowAndTotalHigh()
    {
        (WorkerDataWindowCarrier carrier, FakeTimeProvider clock, ScriptedCarrierParentTask task) =
            NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, 7L);
        carrier.OnUpdateStart();

        // Statement 1 notifies; 2 does not; 3 is forced by advancing past the throttle.
        PreviewUpdate(carrier);
        PreviewUpdate(carrier);
        clock.AdvanceMilliseconds(101L);
        PreviewUpdate(carrier);

        IReadOnlyList<long> payloads = PayloadsOf(task);

        Assert.Equal(2, payloads.Count);
        Assert.Equal(Bits.MakeLong(1, 7), (uint)payloads[0]);
        Assert.Equal(Bits.MakeLong(3, 7), (uint)payloads[1]);
        Assert.Equal(3, LowWord(payloads[1]));
        Assert.Equal(7, HighWord(payloads[1]));
    }

    /// <summary>
    /// The word packer's operands are the FIXED-WIDTH types the shared kernel declares, and that is
    /// what makes the sixteen-bit truncation faithful rather than accidental.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy primitive is
    /// <c>global function ulong makelong(readonly uint low,readonly uint high)</c>
    /// [<c>ws_objects/pfw.common.pbl.src/makelong.srf</c>], bound to the closed <c>pfw.dll</c>. In
    /// PowerBuilder <c>ulong</c> is 32 bits and <c>uint</c> is 16, so the faithful managed shape is two
    /// <see cref="ushort"/> operands answering a <see cref="uint"/> - which is exactly what the shared
    /// kernel declares.
    /// </para>
    /// <para>
    /// C-B DEFECT 4 IS A CONSEQUENCE OF THE SHAPE, NOT A SEPARATE CHOICE. Because the halves are 16
    /// bits, any value above 65535 wraps. Widening the operands to 32 bits would silently remove the
    /// wrap - and the wrap is observable in the notification stream, so it is preserved. Asserting the
    /// operand types here means the shape cannot be widened without this test failing.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheWordPackerUsesTheFixedWidthOperandsTheKernelDeclares()
    {
        MethodInfo makeLong = MethodBodyProbe.DeclaredMethod(typeof(Bits), nameof(Bits.MakeLong));
        ParameterInfo[] operands = makeLong.GetParameters();

        Assert.Equal(2, operands.Length);
        Assert.Equal(typeof(ushort), operands[0].ParameterType);
        Assert.Equal(typeof(ushort), operands[1].ParameterType);
        Assert.Equal(typeof(uint), makeLong.ReturnType);

        // The FIRST operand is the LOW half, matching `MakeLong(_nUpdateCurrent,_nUpdateTotal)` at
        // n_cst_thread_task_sqlbase_ds_mt.sru:L39 and the legacy's own lparam comment.
        Assert.Equal("low", operands[0].Name);
        Assert.Equal("high", operands[1].Name);

        // And the two readers answer fixed-width halves of a 32-bit whole, so a round trip is exact.
        MethodInfo lowWord = MethodBodyProbe.DeclaredMethod(typeof(Bits), nameof(Bits.LoWord));
        MethodInfo highWord = MethodBodyProbe.DeclaredMethod(typeof(Bits), nameof(Bits.HiWord));

        Assert.Equal(typeof(ushort), lowWord.ReturnType);
        Assert.Equal(typeof(ushort), highWord.ReturnType);
        Assert.Equal(typeof(uint), Assert.Single(lowWord.GetParameters()).ParameterType);
        Assert.Equal(typeof(uint), Assert.Single(highWord.GetParameters()).ParameterType);

        uint packed = Bits.MakeLong(3, 7);

        Assert.Equal(3, Bits.LoWord(packed));
        Assert.Equal(7, Bits.HiWord(packed));
    }

    /// <summary>
    /// C-B DEFECT 4. The two halves are SIXTEEN-BIT words, so a statement count above 65535 wraps.
    /// </summary>
    /// <remarks>
    /// It is reachable - the query task's chunk size defaults to ten thousand and a result may hold far
    /// more rows than 65535 - and it is observable in the notification stream, so it is preserved rather
    /// than widened. The wrap is written <c>unchecked</c> at its site, which states that it is intended
    /// rather than relying on the project's default arithmetic mode.
    /// </remarks>
    [Fact]
    public void TheProgressPayloadTruncatesAboveSixteenBits()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();

        // A total of one keeps the reached-the-total condition true from statement one onward, so every
        // preview notifies and the last payload is the one at statement 65536.
        SeedModifiedRows(carrier, DwBuffer.Primary, 1L);
        carrier.OnUpdateStart();

        const int wrapPoint = 65536;

        for (int statement = 1; statement <= wrapPoint; statement++)
        {
            PreviewUpdate(carrier);
        }

        IReadOnlyList<long> payloads = PayloadsOf(task);

        Assert.Equal(wrapPoint, payloads.Count);

        // Statement 65535 still fits; statement 65536 wraps to zero rather than widening the field.
        Assert.Equal(65535, LowWord(payloads[wrapPoint - 2]));
        Assert.Equal(0, LowWord(payloads[wrapPoint - 1]));
        Assert.Equal(1, HighWord(payloads[wrapPoint - 1]));

        // Pinned against the packing primitive directly, so that a change to either side shows up.
        Assert.Equal(Bits.MakeLong(0, 1), (uint)payloads[wrapPoint - 1]);
    }

    #endregion

    #region DEFECT 3 - row-cap lifting versus row-cap enforcement

    /// <summary>
    /// C-B DEFECT 3. A row-cap handler answering ONE LIFTS the cap and the retrieve CONTINUES
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L65-L68</c>] - the
    /// opposite of what that answer means everywhere else in this subsystem.
    /// </summary>
    /// <remarks>
    /// The cap is cleared to zero, which makes the guard at <c>:L64</c> permanently unsatisfiable for the
    /// whole remainder of the retrieve, and the rows-exceeded flag is deliberately NOT raised on this
    /// arm - so a caller cannot afterwards tell that the cap was ever reached. Both halves are asserted,
    /// because "fixing" either would look like an improvement.
    /// </remarks>
    [Fact]
    public void ARowCapHandlerAnsweringOneLiftsTheCapAndContinues()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();
        carrier.SetMaxRows(2L);
        task.NotifyResult = DataWindowBufferStore.EventStop;

        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(1L));
        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(2L));
        Assert.Empty(task.Notifications);

        // Row three passes the cap: the handler is asked, answers one, and the retrieve CONTINUES.
        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(3L));
        Assert.Single(task.Notifications);
        Assert.False(carrier.IsRowsExceeded());

        // The cap was cleared, so the remainder of the retrieve is never asked again.
        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(4L));
        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(4_000L));
        Assert.Single(task.Notifications);
        Assert.False(carrier.IsRowsExceeded());
    }

    /// <summary>
    /// The enforcement arm: a handler answering anything else raises the rows-exceeded flag and stops
    /// the retrieve [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L69-L70</c>].
    /// </summary>
    [Fact]
    public void ARowCapHandlerAnsweringAnythingElseEnforcesTheCap()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();
        carrier.SetMaxRows(2L);
        task.NotifyResult = DataWindowBufferStore.EventContinue;

        Assert.Equal(DataWindowBufferStore.EventStop, carrier.OnRetrieveRow(3L));
        Assert.True(carrier.IsRowsExceeded());
        Assert.Single(task.Notifications);

        // The cap is still in force, so a further row asks again.
        Assert.Equal(DataWindowBufferStore.EventStop, carrier.OnRetrieveRow(4L));
        Assert.Equal(2, task.NotifyCount);
    }

    /// <summary>
    /// BOTH ARMS IN ONE RETRIEVE, which is what proves the polarity is read per notification rather
    /// than configured once: the same carrier enforces the cap, then lifts it, because the handler
    /// answers differently the second time.
    /// </summary>
    /// <remarks>
    /// The recorder's per-notification script is what makes this expressible in one test. The ordinals
    /// it stamps are what make the sequence legible: notification 1 is the enforcement, notification 2
    /// is the lift.
    /// </remarks>
    [Fact]
    public void TheRowCapPolarityIsReadPerNotification()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();
        carrier.SetMaxRows(1L);

        task.NotifyScript = notification => notification.Ordinal == 1
            ? DataWindowBufferStore.EventContinue
            : DataWindowBufferStore.EventStop;

        // First crossing: the handler declines to lift, so the cap is enforced and the flag is raised.
        Assert.Equal(DataWindowBufferStore.EventStop, carrier.OnRetrieveRow(2L));
        Assert.True(carrier.IsRowsExceeded());

        // Second crossing: the handler answers one, so the cap is lifted and the retrieve continues.
        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(3L));

        // C-B: THE FLAG IS NOT CLEARED BY THE LIFT. It was raised by the first crossing and the lifting
        // arm neither sets nor clears it, so it stays raised - which is legacy behaviour and is
        // reproduced rather than normalised.
        Assert.True(carrier.IsRowsExceeded());

        Assert.Equal(2, task.NotifyCount);
        Assert.Equal(
            new[] { 1, 2 },
            task.Notifications.Select(notification => notification.Ordinal));
    }

    /// <summary>
    /// The row-cap payload is the ROW NUMBER passed straight through, with no word packing and therefore
    /// no truncation - unlike the progress payload.
    /// </summary>
    /// <remarks>
    /// The row number is passed as the second argument of
    /// <c>#ParentTask.Event OnNotify(n_cst_threading_task_sqlquery.NCD_MAXROWS,row,"")</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L65</c>], with no
    /// <c>MakeLong</c> anywhere near it, so a row number far beyond sixteen bits survives intact. The
    /// notify code belongs to the QUERY task's set, whose value one means the row cap while the UPDATE
    /// task's value one means progress - the collision the two separate enumerations exist to make
    /// unconfusable.
    /// </remarks>
    [Fact]
    public void TheRowCapPayloadIsTheRowNumberUnpacked()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();
        carrier.SetMaxRows(1L);
        task.NotifyResult = DataWindowBufferStore.EventContinue;

        carrier.OnRetrieveRow(70_000L);

        RecordedNotification notification = Assert.Single(task.Notifications);

        Assert.Equal(70_000L, notification.Payload);
        Assert.Equal((long)SqlQueryTaskNotifyCode.MaxRows, notification.NotifyCode);
        Assert.Equal(string.Empty, notification.Text);

        // The value is BEYOND sixteen bits, so a packed payload would have truncated it. That it did
        // not is the assertion.
        Assert.True(notification.Payload > ushort.MaxValue);
    }

    /// <summary>
    /// A cap of zero or below means NO LIMIT, so nothing is ever notified
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L64</c>].
    /// </summary>
    /// <param name="cap">The cap value, zero or negative.</param>
    /// <remarks>
    /// C-B. The negative case is reachable only because <c>of_setmaxrows</c> validates nothing
    /// [<c>_ds.sru:L69-L71</c>], and the guard is <c>_nMaxRows &gt; 0</c> rather than a non-zero test,
    /// so a negative cap behaves as no limit rather than as an immediate stop.
    /// </remarks>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void ACapOfZeroOrBelowMeansNoLimit(long cap)
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();
        carrier.SetMaxRows(cap);

        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(1_000_000L));
        Assert.Empty(task.Notifications);
        Assert.False(carrier.IsRowsExceeded());
    }

    /// <summary>
    /// The cap is exceeded only on the row AFTER the last permitted one, because the guard is
    /// <c>row &gt; _nMaxRows</c> rather than <c>row &gt;= _nMaxRows</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L64</c>].
    /// </summary>
    /// <remarks>
    /// R9. <c>row</c> is the ONE-BASED number of the row that has just arrived, so with a cap of two the
    /// second row is the last permitted one and the third is the first refusal. An off-by-one here would
    /// deliver one row too few or one too many, and a row-count assertion alone could not tell which.
    /// </remarks>
    [Fact]
    public void TheCapIsExceededOnlyByTheRowAfterTheLastPermittedOne()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();
        carrier.SetMaxRows(2L);
        task.NotifyResult = DataWindowBufferStore.EventContinue;

        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(2L));
        Assert.Empty(task.Notifications);
        Assert.False(carrier.IsRowsExceeded());

        Assert.Equal(DataWindowBufferStore.EventStop, carrier.OnRetrieveRow(3L));
        Assert.Equal(3L, Assert.Single(task.Notifications).Payload);
        Assert.True(carrier.IsRowsExceeded());
    }

    #endregion

    #region DEFECT 6 - the two per-task notify code sets and their shared value

    /// <summary>
    /// C-B DEFECT 6 and C-K. The two notification sets are SEPARATE ENUMERATIONS that both legitimately
    /// contain the value one, and every numeric value is the legacy's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The query task declares six codes
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L28-L33</c>] and the
    /// update task declares one [<c>n_cst_threading_task_sqlupdate.sru:L32</c>]. In PowerBuilder they do
    /// not collide because each is a constant on ITS OWN CLASS and every use is qualified - the carrier
    /// writes <c>n_cst_threading_task_sqlquery.NCD_MAXROWS</c> at
    /// <c>n_cst_thread_task_sqlbase_ds_mt.sru:L65</c> and
    /// <c>n_cst_threading_task_sqlupdate.NCD_PROGRESS</c> at <c>:L39</c>. Two enumerations reproduce
    /// exactly that per-class namespacing.
    /// </para>
    /// <para>
    /// C-K - WHY THEY MUST NOT BE MERGED, AND WHY NEITHER MAY BE RENUMBERED. Merging them would force a
    /// choice between two wrong outcomes: renumber one set, which invalidates every stored recording
    /// because the numeric code is what a recording carries; or keep both ones, which is not
    /// expressible in a single enumeration. The overlap is not a mistake to be resolved - it is
    /// information, namely that a notify code means nothing without knowing which task raised it.
    /// </para>
    /// <para>
    /// The identifiers are re-cased to PascalCase and the VALUES are not touched. Re-casing changes
    /// nothing observable; the values are observable, so they are preserved exactly. This file declares
    /// no SCREAMING_SNAKE identifier of its own, per 0.7.2.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTwoNotifyCodeSetsAreSeparateTypesThatShareTheValueOne()
    {
        // The query task's six, in the legacy's own order and with the legacy's own values.
        Assert.Equal(1L, (long)SqlQueryTaskNotifyCode.MaxRows);
        Assert.Equal(2L, (long)SqlQueryTaskNotifyCode.DataReceived);
        Assert.Equal(3L, (long)SqlQueryTaskNotifyCode.PageReceived);
        Assert.Equal(4L, (long)SqlQueryTaskNotifyCode.DataChunk);
        Assert.Equal(5L, (long)SqlQueryTaskNotifyCode.ChildReceived);
        Assert.Equal(6L, (long)SqlQueryTaskNotifyCode.ChildQuery);

        // The update task's one.
        Assert.Equal(1L, (long)SqlUpdateTaskNotifyCode.Progress);

        // TWO DISTINCT TYPES. Not an alias, not a shared type, not one enumeration with two names.
        Assert.NotEqual(typeof(SqlQueryTaskNotifyCode), typeof(SqlUpdateTaskNotifyCode));

        // THE COLLISION IS REAL, and it is the reason the two sets are distinct types.
        Assert.Equal((long)SqlQueryTaskNotifyCode.MaxRows, (long)SqlUpdateTaskNotifyCode.Progress);

        // Each set is exactly as large as its legacy constant block, so a code cannot be added to
        // either without this test failing.
        Assert.Equal(6, Enum.GetValues<SqlQueryTaskNotifyCode>().Length);
        Assert.Single(Enum.GetValues<SqlUpdateTaskNotifyCode>());

        // Both are backed by long, matching `Constant Long` in both legacy declarations.
        Assert.Equal(typeof(long), Enum.GetUnderlyingType(typeof(SqlQueryTaskNotifyCode)));
        Assert.Equal(typeof(long), Enum.GetUnderlyingType(typeof(SqlUpdateTaskNotifyCode)));

        // And no identifier in either set carries the legacy's underscored spelling, because this file
        // and the file under test are both outside the .editorconfig relaxation list.
        Assert.DoesNotContain(
            Enum.GetNames<SqlQueryTaskNotifyCode>(),
            name => name.Contains('_', StringComparison.Ordinal));
        Assert.DoesNotContain(
            Enum.GetNames<SqlUpdateTaskNotifyCode>(),
            name => name.Contains('_', StringComparison.Ordinal));
    }

    /// <summary>
    /// The two codes reach the recorder from two different carrier events, so the collision is
    /// observable end to end rather than only in the type system.
    /// </summary>
    [Fact]
    public void TheSharedValueOneArrivesFromTwoDifferentEvents()
    {
        (WorkerDataWindowCarrier carrier, _, ScriptedCarrierParentTask task) = NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, 1L);
        carrier.SetMaxRows(1L);
        task.NotifyResult = DataWindowBufferStore.EventContinue;

        // The row cap, from the retrieve-row event.
        carrier.OnRetrieveRow(2L);

        // Progress, from the SQL-preview event.
        carrier.OnUpdateStart();
        PreviewUpdate(carrier);

        Assert.Equal(2, task.NotifyCount);
        Assert.All(task.Notifications, notification => Assert.Equal(1L, notification.NotifyCode));

        // Identical codes, entirely different payload conventions: a raw row number, then a packed pair.
        Assert.Equal(2L, PayloadsOf(task)[0]);
        Assert.Equal(Bits.MakeLong(1, 1), (uint)PayloadsOf(task)[1]);

        // Which is exactly why a recording cannot be read without knowing which task raised it.
        Assert.Equal(2, task.CountOf((long)SqlQueryTaskNotifyCode.MaxRows));
        Assert.Equal(2, task.CountOf((long)SqlUpdateTaskNotifyCode.Progress));
    }

    #endregion


    #region The carrier pair and its affinity factory

    /// <summary>
    /// THE PROXY/WORKER DUALITY IS NOT FLATTENED. There are TWO carrier types, one per thread affinity,
    /// related by derivation, and neither is a configuration flag on the other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy declares two objects whose header comments state which thread each runs on:
    /// <c>n_cst_thread_task_sqlbase_ds</c> is annotated <c>[运行在主线程]</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L2</c>] and
    /// <c>n_cst_thread_task_sqlbase_ds_mt</c> is annotated <c>[运行在子线程]</c> and declared
    /// <c>from n_cst_thread_task_sqlbase_ds</c>
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L2, L8</c>].
    /// </para>
    /// <para>
    /// AAP 0.4.5.4 - THE THREAD-AFFINITY ANNOTATIONS ARE A CONTRACT, NOT COMMENTARY, and the plan is
    /// explicit that the pair must not be flattened into one type. It is not a stylistic point: the
    /// by-reference handover asserted at the end of this file exists ONLY because a main-thread carrier
    /// shares an address space with its consumer, which is precisely what the affinity records. One type
    /// with a boolean would erase a real execution path rather than merely a name.
    /// </para>
    /// <para>
    /// DERIVATION IS ASSERTED IN THE LEGACY'S DIRECTION: the worker derives from the main-thread
    /// carrier, not the reverse and not from a shared abstract base. That direction is what makes the
    /// worker's four <c>call super::</c> statements reach the main-thread bodies.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCarrierPairIsTwoTypesRelatedByDerivation()
    {
        // Two distinct types.
        Assert.NotEqual(typeof(DataWindowCarrier), typeof(WorkerDataWindowCarrier));

        // Derivation in the legacy's direction, and directly rather than through an intermediate.
        Assert.Equal(typeof(DataWindowCarrier), typeof(WorkerDataWindowCarrier).BaseType);

        // Both are carriers, and both are buffer stores - the legacy chain is
        // datastore -> _ds -> _ds_mt, and the port's is DataWindowBufferStore -> carrier -> worker.
        Assert.Equal(typeof(DataWindowBufferStore), typeof(DataWindowCarrier).BaseType);

        // The affinity is a property of the TYPE rather than of a constructor argument, so it cannot be
        // set to disagree with the type's behaviour.
        FakeTimeProvider clock = new();

        Assert.Equal(CarrierThreadAffinity.MainThread, new DataWindowCarrier(clock).Affinity);
        Assert.Equal(CarrierThreadAffinity.WorkerThread, new WorkerDataWindowCarrier(clock).Affinity);

        // Exactly two affinities, because the legacy has exactly two carriers.
        Assert.Equal(2, Enum.GetValues<CarrierThreadAffinity>().Length);
    }

    /// <summary>
    /// The factory reproduces the selection at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L553-L557</c>, answering the
    /// EXACT carrier type for each affinity.
    /// </summary>
    /// <remarks>
    /// EXACT RATHER THAN ASSIGNABLE MATTERS. The worker type derives from the main-thread type, so an
    /// "is a" assertion would pass for an implementation that answered a worker carrier on the main
    /// thread - which would silently add cancellation checks, progress accounting and row-cap
    /// enforcement to a path that has none of them.
    /// </remarks>
    [Fact]
    public void TheFactoryAnswersTheExactCarrierTypeForEachAffinity()
    {
        FakeTimeProvider clock = new();

        DataWindowCarrier mainThread =
            DataWindowCarrierFactory.Create(CarrierThreadAffinity.MainThread, clock);
        DataWindowCarrier worker =
            DataWindowCarrierFactory.Create(CarrierThreadAffinity.WorkerThread, clock);

        Assert.IsType<DataWindowCarrier>(mainThread);
        Assert.IsType<WorkerDataWindowCarrier>(worker);
        Assert.Equal(CarrierThreadAffinity.MainThread, mainThread.Affinity);
        Assert.Equal(CarrierThreadAffinity.WorkerThread, worker.Affinity);

        // The clock is handed through rather than replaced, so both carriers share the seam their
        // consumer injected. Observable through the worker, which is the one that reads a clock at all.
        Assert.Equal(0, clock.AdvanceCount);
    }

    /// <summary>
    /// The boolean-shaped overload reads the way the legacy branch does, asking
    /// <c>#ParentThread.of_IsMainThread()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L553</c>].
    /// </summary>
    /// <param name="isMainThread">The predicate's answer.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheFactoryKeysOnTheMainThreadPredicate(bool isMainThread)
    {
        DataWindowCarrier carrier =
            DataWindowCarrierFactory.CreateForThread(isMainThread, new FakeTimeProvider());

        Assert.Equal(AffinityOf(isMainThread), carrier.Affinity);
    }

    /// <summary>
    /// There are exactly two carrier types, so an undeclared affinity and a missing clock both fail
    /// fast.
    /// </summary>
    /// <remarks>
    /// The factory deliberately does NOT do what the legacy does immediately afterwards - assign the
    /// data object, register the carrier in the parent thread's cache [<c>:L558-L565</c>] and raise the
    /// initialization event [<c>:L566</c>] - because the cache belongs to the task layer. A caller must
    /// therefore initialize the carrier itself, and an uninitialized carrier fails fast rather than
    /// inventing a parent, which is asserted separately.
    /// </remarks>
    [Fact]
    public void TheFactoryRefusesAnUndeclaredAffinityAndAMissingClock()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = DataWindowCarrierFactory.Create(
                    (CarrierThreadAffinity)7,
                    new FakeTimeProvider());
            });
        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = DataWindowCarrierFactory.Create(CarrierThreadAffinity.MainThread, null!);
            });
        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = DataWindowCarrierFactory.CreateForThread(false, null!);
            });

        // The factory hands back an UNINITIALIZED carrier, exactly as the port documents, so the
        // fail-fast posture is reachable through the factory's own product.
        DataWindowCarrier fromFactory =
            DataWindowCarrierFactory.Create(CarrierThreadAffinity.WorkerThread, new FakeTimeProvider());

        Assert.Null(fromFactory.ParentTask);
        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = fromFactory.OnRetrieveStart();
            });
    }

    #endregion

    #region The main-thread handover that runs no codec at all

    /// <summary>
    /// The handover is available only on the main thread, with no receiver AND with caching off - the
    /// nested conjunction at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L84-L85</c>.
    /// </summary>
    /// <param name="isMainThread">Whether the publishing thread is the main thread.</param>
    /// <param name="hasReceiver">Whether a receiver is installed on the tasking object.</param>
    /// <param name="cacheEnabled">Whether the task caches its result.</param>
    /// <param name="expected">Whether the carrier may change hands by reference.</param>
    /// <remarks>
    /// All eight combinations are pinned, because any one of the three failing sends the result down a
    /// codec path instead - and the whole point of the predicate is that it is the gate in front of that
    /// choice.
    /// </remarks>
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, true, false)]
    public void TheHandoverRequiresAllThreeConditions(
        bool isMainThread,
        bool hasReceiver,
        bool cacheEnabled,
        bool expected)
    {
        Assert.Equal(
            expected,
            DataWindowCarrierOwnership.CanMoveWithoutSerialization(
                AffinityOf(isMainThread),
                hasReceiver,
                cacheEnabled));
    }

    /// <summary>
    /// THE HANDOVER RUNS NO CODEC AT ALL: the receiver adopts the carrier ITSELF, the sender's reference
    /// is then dropped, and neither transfer codec is touched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reproduces <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L84-L90</c>, which
    /// runs BEFORE the codec selector at <c>:L93</c> and short-circuits it entirely:
    /// <c>tasking.Event OnDataMove(data)</c> then <c>SetNull(data)</c> then <c>return RetCode.OK</c>.
    /// </para>
    /// <para>
    /// WHY THIS MATTERS ENOUGH TO ASSERT FROM FOUR ANGLES. On the main thread the legacy HANDS THE OBJECT
    /// OVER rather than serialising it, so a port that always ran a codec would change observable
    /// behaviour - the receiver would get a reconstruction rather than the original - as well as doing
    /// work the legacy does not do. The four angles are: the received instance is REFERENCE-IDENTICAL to
    /// the original, which no encode-decode round trip can produce; the carrier's state is readable
    /// afterwards without ever having been encoded; the ordered recorders show no codec call and no
    /// notification; and the ORDER is observed from inside the receiving action, where the sender's
    /// reference is still live because it is dropped only afterwards.
    /// </para>
    /// <para>
    /// The codec-call counters are shown to be a REAL detector rather than a vacuous zero: the same
    /// carrier double is asked for a changeset at the end, and the counter moves.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheHandoverTransfersOwnershipByReferenceAndRunsNoCodec()
    {
        FakeTimeProvider clock = new();
        ScriptedCarrierParentTask task = new() { IsMainThread = true };

        // The carrier double is the codec detector: it owns a changeset codec that counts its calls.
        FakeDataWindowCarrier detector = new(parentTask: task);

        DataWindowCarrier? sender =
            DataWindowCarrierFactory.Create(CarrierThreadAffinity.MainThread, clock);
        sender.OnInit(task);
        sender.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);
        sender.SetItemValue(1L, 1, DwBuffer.Primary, "row-1");

        // The gate says the handover is available, which is the precondition for taking it at all.
        Assert.True(
            DataWindowCarrierOwnership.CanMoveWithoutSerialization(
                CarrierThreadAffinity.MainThread,
                hasReceiver: false,
                cacheEnabled: false));

        DataWindowCarrier original = sender;
        DataWindowCarrier? received = null;
        bool senderStillHeldWhenReceiverRan = false;

        long answer = DataWindowCarrierOwnership.Move(
            ref sender,
            carrier =>
            {
                received = carrier;

                // THE ORDER, observed from inside the receiving action: the sender still holds its
                // reference at this instant, because SetNull(data) happens afterwards. Dropping first
                // would leave the carrier unreachable at the moment the receiver needed it.
                senderStillHeldWhenReceiverRan = sender is not null;
            });

        Assert.Equal(RetCode.OK, answer);
        Assert.True(senderStillHeldWhenReceiverRan);

        // BY REFERENCE. Not an equal copy - the same object.
        Assert.Same(original, received);
        Assert.Null(sender);

        // Its state is intact and was never encoded to get here.
        Assert.Equal(1L, received!.RowCount());
        Assert.Equal("row-1", received.GetItemValue(1L, 1, DwBuffer.Primary));

        // NO CODEC RAN, and no notification or database error was raised either.
        Assert.Equal(0, detector.Changeset.EncodeCalls);
        Assert.Equal(0, detector.Changeset.ApplyCalls);
        Assert.Empty(task.Notifications);
        Assert.Empty(task.DbErrors);

        // THE DETECTOR IS NOT VACUOUS: asking the double for a changeset moves the counter, so the zero
        // above is a fact about the handover rather than about the counter.
        _ = detector.GetChanges(out _);

        Assert.Equal(1, detector.Changeset.EncodeCalls);
    }

    /// <summary>
    /// No codec is REACHABLE from the ownership path at all, not merely unused on the one path a test
    /// happens to drive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "No codec ran" is a negative over a whole code path, and a behavioural test can only ever show it
    /// for the path it drove. Reading the compiled bodies shows that the ownership type calls nothing
    /// belonging to either transfer codec under any condition - and the same holds for both carrier
    /// types, which is what keeps <c>Buffers/</c>'s internal layering intact: the codecs consume the
    /// carriers, never the reverse.
    /// </para>
    /// <para>
    /// The scan is proven non-vacuous by running it over <c>ChangesetCodec</c>, which DOES reach a codec
    /// and is therefore detected. Without that half, a scan that had silently stopped resolving anything
    /// would report a clean absence.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoCodecIsReachableFromTheOwnershipOrCarrierPath()
    {
        Type[] mustBeCodecFree =
        [
            typeof(DataWindowCarrierOwnership),
            typeof(DataWindowCarrierFactory),
            typeof(DataWindowCarrier),
            typeof(WorkerDataWindowCarrier),
        ];

        foreach (Type type in mustBeCodecFree)
        {
            IReadOnlyList<RecordedIlCall> calls = MethodBodyProbe.CallsIn(type);

            Assert.NotEmpty(calls);

            // Every token resolved, so the absence below rests on a fully read body.
            Assert.DoesNotContain(
                calls,
                call => call.DeclaringTypeName == MethodBodyProbe.UnresolvedTypeName);

            // Neither transfer codec is reached, under its port name or its legacy one.
            Assert.DoesNotContain(
                calls,
                call => CodecTypeNames.Contains(call.DeclaringTypeName, StringComparer.Ordinal));
            Assert.DoesNotContain(
                calls,
                call => CodecMemberNames.Contains(call.MemberName, StringComparer.Ordinal));
        }

        // THE SCAN IS A REAL DETECTOR. The changeset codec reaches a codec collaborator, and the same
        // scan finds it.
        IReadOnlyList<RecordedIlCall> codecCalls = MethodBodyProbe.CallsIn(typeof(ChangesetCodec));

        Assert.Contains(
            codecCalls,
            call => CodecTypeNames.Contains(call.DeclaringTypeName, StringComparer.Ordinal));
    }

    /// <summary>
    /// A carrier may be handed over ONCE. A second handover means two parties believe they own the same
    /// result, which is exactly the fault the dropped reference exists to prevent, so it fails fast.
    /// </summary>
    /// <remarks>
    /// C-K. The dropped reference reproduces <c>SetNull(data)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L88</c>] and is NOT a
    /// garbage-collection nicety. It is the same discipline as hazard 2 of
    /// <c>docs/PB多线程绕坑提示.md</c>, and its purpose is to make ownership unambiguous: after the
    /// handover exactly one party holds the carrier, so a later write through a stale reference is
    /// impossible by construction rather than by convention.
    /// </remarks>
    [Fact]
    public void AHandoverCannotHappenTwice()
    {
        DataWindowCarrier? sender =
            DataWindowCarrierFactory.Create(CarrierThreadAffinity.MainThread, new FakeTimeProvider());

        DataWindowCarrierOwnership.Move(ref sender, _ => { });

        // A ref argument is passed to each failing call through a local function, so nothing has to be
        // captured merely to be able to assert on it.
        void MoveAgain() => DataWindowCarrierOwnership.Move(ref sender, _ => { });
        void MoveWithNoReceiver() => DataWindowCarrierOwnership.Move(ref sender, null!);

        Assert.Throws<InvalidOperationException>(MoveAgain);
        Assert.Throws<ArgumentNullException>(MoveWithNoReceiver);
    }

    /// <summary>
    /// If the receiver throws, the sender KEEPS its reference - which is the ordering of
    /// <c>OnDataMove</c> before <c>SetNull</c> observed from the other side.
    /// </summary>
    /// <remarks>
    /// The carrier is not lost by a failed handover: nothing has been serialized, nothing has been
    /// consumed, and the sender is still the sole owner. An implementation that dropped the reference
    /// first would leave the result unreachable from either party.
    /// </remarks>
    [Fact]
    public void AFailedHandoverLeavesTheCarrierWithItsSender()
    {
        DataWindowCarrier? sender =
            DataWindowCarrierFactory.Create(CarrierThreadAffinity.MainThread, new FakeTimeProvider());
        sender.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        void MoveIntoAFailingReceiver() => DataWindowCarrierOwnership.Move(
            ref sender,
            _ => throw new InvalidTimeZoneException("The receiver refused the carrier."));

        Assert.Throws<InvalidTimeZoneException>(MoveIntoAFailingReceiver);

        Assert.NotNull(sender);
        Assert.Equal(1L, sender.RowCount());

        // And the handover can still be taken afterwards, because nothing was consumed.
        DataWindowCarrier? received = null;

        Assert.Equal(RetCode.OK, DataWindowCarrierOwnership.Move(ref sender, carrier => received = carrier));
        Assert.NotNull(received);
        Assert.Null(sender);
    }

    #endregion
}

