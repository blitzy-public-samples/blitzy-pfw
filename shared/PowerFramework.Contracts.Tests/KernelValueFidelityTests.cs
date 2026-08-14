// ==================================================================================================
//  KernelValueFidelityTests - THE ONLY EDGE BETWEEN THE WIRE ALPHABETS AND THE MANAGED ONES
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   shared/PowerFramework.Contracts/Proto/common.v1.proto - every enum it declares
//            PowerFramework.Shared.Kernel.RetCode                  - the managed constant catalogue
//  ORACLE    ws_objects/pfw.shared.pbl.src/retcode.sru             - what BOTH are transcribed from
//            ws_objects/pfw.shared.pbl.src/issucceeded.srf, isfailed.srf - the classifying predicates
//            ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs           - `dwbuffer buffer` [:L7]
//            ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru,
//            n_cst_thread_task_sqlquery.sru, n_cst_thread_task_sqlupdate.sru,
//            n_cst_thread_task_sqlbase.sru - the buffer and item-status vocabulary in use
//            Read as SPECIFICATION ONLY. No test in this file opens a legacy file at runtime; every
//            legacy locator below is a citation in a comment (constraint C-C).
//
//  WHY THIS SUITE EXISTS AT ALL
//  ------------------------------------------------------------------------------------------------
//  shared/PowerFramework.Contracts declares ZERO project references, deliberately, so that the
//  contracts project stays a pure boundary definition and never becomes a shared-code back door
//  (AAP 0.4.2.3). The direct consequence is that the numeric agreement between the wire alphabets in
//  common.v1.proto and the managed catalogue in PowerFramework.Shared.Kernel.RetCode HAS NO
//  COMPILE-TIME EDGE ANYWHERE IN THE REPOSITORY. Two toolchains transcribe one read-only legacy object
//  into two languages and nothing makes them agree.
//
//  A divergence would therefore be SILENT and, worse, plausible: each side internally consistent, the
//  two disagreeing with each other, and the disagreement expressed as a number that every caller in the
//  system classifies BY ITS SIGN - `IsSucceeded` tests `>= RetCode.OK`
//  [ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L12] and `IsFailed` tests `< RetCode.OK` with an
//  EXPLICIT exclusion of `CANCELLED` [isfailed.srf:L11-L12]. One sign flip between the transcriptions
//  turns a failure into a success at a service boundary, and because a prevention ALREADY reads as a
//  success by design, nobody auditing the classification logic would find it surprising.
//
//  This file is the enforcement. It reads its expectations FROM THE KERNEL CONSTANTS BY REFLECTION
//  rather than from a hand-written list, so it fails on drift in EITHER direction: a constant renamed or
//  renumbered in the kernel breaks it, and a value renamed or renumbered in the .proto breaks it too. A
//  hand-maintained list would be a THIRD transcription of the same source needing the same guarantee -
//  it would move the problem, not solve it.
//
//  HOW THIS DIFFERS FROM RetCodeAgreementTests, ITS SIBLING IN THIS FOLDER
//  ------------------------------------------------------------------------------------------------
//  The two are complementary and neither subsumes the other. RetCodeAgreementTests asserts a handful of
//  whole-set facts about common.v1.RetCode.Value with [Fact]. This file is the TABLE-DRIVEN parity
//  matrix the repository prescribes (AAP 0.6.7): one theory ROW PER CONSTANT, across EVERY enum
//  common.v1 declares, in both directions, so that one drift cannot mask another in an aggregate. When
//  a single constant is wrong here, exactly one row goes red and it is named in the test id.
//
//  THE VACUOUS PASS IS THE WORST FAILURE MODE, AND IT IS GUARDED EXPLICITLY
//  ------------------------------------------------------------------------------------------------
//  Every assertion below is a comparison over a reflected set. A refactor that moved the kernel's
//  constants to another type, or renamed the class, would empty that set - and a comparison over an
//  empty set SUCCEEDS. The suite would then report green while checking nothing, which is strictly worse
//  than failing because it removes the only edge without removing the confidence. So the set is guarded
//  three ways before it is trusted: it must be non-empty, it must contain each of nine named anchor
//  constants, and it must hold the whole 156-constant catalogue. See the three tests under
//  "GUARDS AGAINST A VACUOUS PASS".
//
//  ALIASING IS THE TRAP, AND ALIASING IS CORRECT (constraint C-B)
//  ------------------------------------------------------------------------------------------------
//  In the legacy catalogue several numbers carry several names. 0 is spelled OK, SUCCESS and ALLOW
//  [retcode.sru:L39-L41] and, in the two other families, XML_OK [:L84] and SQLITE_OK [:L107]. 1 is
//  PREVENT [:L42], XML_E_FILE_NOT_FOUND [:L85] and SQLITE_ERROR [:L109]. -2 is spelled both CANCELED
//  and CANCELLED [:L44-L45].
//
//  None of that is a defect to normalise. Each spelling appears at legacy call sites, so each can appear
//  in a serialized payload, a log record or a characterization recording, where a rename silently
//  invalidates every stored comparison (AAP 0.4.5.3, 0.8.2). This file therefore asserts the
//  NAME-TO-NUMBER MAPPING and NEVER that a number appears once. A uniqueness-of-number assertion would
//  be a demand that the enum be TIDIER than the oracle, which is precisely the correction C-B forbids -
//  and it would fail against a correct contract, which makes it worse than useless.
//
//  DESCRIPTOR NAMES ON THE WIRE SIDE, FieldInfo.Name ON THE KERNEL SIDE - NEVER CLR ENUM MEMBERS
//  ------------------------------------------------------------------------------------------------
//  protoc PascalCases the C# projection of a proto enum value, so `E_INVALID_ARGUMENT` in the .proto
//  becomes the member `EInvalidArgument` in the generated code. Reflecting over the generated CLR enum
//  would therefore compare `EInvalidArgument` against the kernel's `E_INVALID_ARGUMENT` and fail FOR THE
//  WRONG REASON - reporting a spelling difference that protoc introduced as though it were a contract
//  breach. `EnumValueDescriptor.Name` returns the proto spelling AS AUTHORED and is the only thing in
//  the build that can see it. On the kernel side the constants are hand-written, so `FieldInfo.Name` is
//  already the authored spelling. The two substrates are not interchangeable and must not be mixed.
//
//  THE LAYOUT FOUND, RECORDED BECAUSE THE ASSERTIONS ARE WRITTEN TO SURVIVE EITHER ONE (C-K)
//  ------------------------------------------------------------------------------------------------
//  common.v1.proto splits the catalogue into THREE SEPARATE nested enums rather than fusing it into one:
//      common.v1.RetCode.Value           41 values, `option allow_alias = true`
//      common.v1.XmlParseStatus.Value    17 values, no allow_alias - all distinct
//      common.v1.SqliteResultCode.Value  98 values, no allow_alias - all distinct
//  41 + 17 + 98 = 156, which reconciles exactly to the oracle. Two further enums are declared at file
//  scope - common.v1.DwBuffer and common.v1.ItemStatus - and one more is nested, common.v1.RichError.Key.
//
//  The split layout is why each family's zero-valued member satisfies proto3's first-enumerator rule on
//  its own account (OK, XML_OK and SQLITE_OK are all 0 in three different scopes) and why allow_alias is
//  needed in one enum only. Every assertion below is nevertheless phrased over the NAME SET rather than
//  over an enum's membership count wherever the two layouts would differ, so fusing the three families
//  into one aliased enum later would not require rewriting this file. The one place the layout is read
//  directly - the indirect proof that aliasing is permitted - is documented at that test.
//
//  PURITY
//  ------------------------------------------------------------------------------------------------
//  No I/O, no clock, no randomness, no network, no environment variable, no host and no mutable static
//  state. Managed reflection over an immutable type and descriptor reflection over an immutable,
//  process-wide graph, and nothing else. Repeatability is the one hard prerequisite of the Golden-Master
//  approach this repository adopts (AAP 0.6.7), so every test here is order-independent and safe under
//  parallel collections. No secret, credential, key or token literal appears anywhere in this file, and
//  none is needed (constraint C-F's never-replicate posture).
//
//  A LOCATOR CORRECTION, MADE HERE SO IT IS NOT REDISCOVERED (C-K)
//  ------------------------------------------------------------------------------------------------
//  The buffer and item-status evidence is often attributed to n_cst_thread_task_sqlbase_ds.sru. It is
//  NOT there: that object (175 lines) contains no occurrence of Primary!, Delete!, Filter!, NotModified!,
//  DataModified!, NewModified! or the `dwbuffer` type at all. The vocabulary lives in its siblings, and
//  the citations in this file were each verified by inspection before being written down. The verified
//  set is listed at EveryEvidencedBufferMemberIsDeclaredOnTheWire and its item-status counterpart.
// ==================================================================================================

using System.Reflection;
using Google.Protobuf.Reflection;
using Xunit;
using KernelRetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Asserts that every alphabet published by <c>common.v1.proto</c> agrees, value for value and name for
/// name, with the managed catalogue in <see cref="KernelRetCode"/> - the only place in the repository
/// where that agreement is checked at all.
/// </summary>
/// <remarks>
/// <para>
/// Expectations are read from the kernel constants by reflection rather than listed, so a drift on
/// either side is a build failure. See the file header for why no compile-time edge exists, why aliasing
/// is correct rather than a defect, and why descriptor names rather than CLR member names are the
/// substrate on the wire side.
/// </para>
/// <para>
/// Oracle: <c>ws_objects/pfw.shared.pbl.src/retcode.sru</c>, read as specification only.
/// </para>
/// </remarks>
public sealed class KernelValueFidelityTests
{
    // ==============================================================================================
    //  THE ONLY LITERALS IN THIS FILE, AND EACH ONE IS STRUCTURAL RATHER THAN A VALUE
    //  --------------------------------------------------------------------------------------------
    //  A number that can be read from the kernel IS read from the kernel - that is the whole point of
    //  the suite, and hardcoding one would create a second expectation to keep in step. What remains
    //  below is the SHAPE of the catalogue: how many members a family has, where a run starts and
    //  stops, the radix the extended codes are built on, and how many spellings share a number. None of
    //  those can be derived from the kernel without assuming the very thing under test, so each is
    //  transcribed from the oracle and carries its locator.
    //
    //  These literals are also what makes the suite catch a CO-ORDINATED drift. The two directions
    //  below only prove the wire and the kernel agree WITH EACH OTHER; if both were edited together
    //  they would still agree while both having departed from the legacy. The structural literals and
    //  the focused anchor rows are transcribed from retcode.sru itself, which turns the two-way
    //  agreement into a three-way one against the read-only oracle.
    // ==============================================================================================

    /// <summary>
    /// Total constants in the legacy catalogue: 41 return codes + 17 <c>XML_*</c> + 98 <c>SQLITE_*</c>.
    /// </summary>
    /// <remarks>
    /// Counted from <c>ws_objects/pfw.shared.pbl.src/retcode.sru</c> - the return codes at
    /// <c>:L39-L79</c>, the XML parse statuses at <c>:L84-L100</c> and the SQLite result codes at
    /// <c>:L107-L206</c>. The file declares them with the keyword spelled <c>Constant Long</c>.
    /// </remarks>
    private const int LegacyCatalogueSize = 156;

    /// <summary>First value of the contiguous error block: <c>E_INVALID_ARGUMENT</c> [retcode.sru:L46].</summary>
    private const long DenseBlockFirstValue = -3;

    /// <summary>Last value of the contiguous error block: <c>E_RETRY</c> [retcode.sru:L76].</summary>
    private const long DenseBlockLastValue = -33;

    /// <summary>Members in the contiguous error block: every integer from -3 to -33 [retcode.sru:L46-L76].</summary>
    private const int DenseBlockSize = 31;

    /// <summary>First value of the XML parse-status family: <c>XML_OK</c> [retcode.sru:L84].</summary>
    private const long XmlStatusFirstValue = 0;

    /// <summary>Last value of the family: <c>XML_E_NO_DOCUMENT_ELEMENT</c> [retcode.sru:L100].</summary>
    private const long XmlStatusLastValue = 16;

    /// <summary>Members in the XML parse-status family [retcode.sru:L84-L100].</summary>
    private const int XmlStatusFamilySize = 17;

    /// <summary>
    /// The radix the SQLite extended result codes are built on: an extended code is its base code plus
    /// a multiple of 256, written as arithmetic in the oracle itself - for example
    /// <c>SQLITE_IOERR_READ = (SQLITE_IOERR + (1*256))</c> [retcode.sru:L143].
    /// </summary>
    private const long SqliteExtendedRadix = 256;

    /// <summary>Spellings of 0 in the return-code family: OK, SUCCESS, ALLOW [retcode.sru:L39-L41].</summary>
    private const int ZeroSpellingCount = 3;

    /// <summary>Spellings of -2 in the return-code family: CANCELED, CANCELLED [retcode.sru:L44-L45].</summary>
    private const int CancelledSpellingCount = 2;

    /// <summary>
    /// Smallest number of co-resident members at one number that proves an enum permits aliasing.
    /// </summary>
    /// <remarks>
    /// Two. Protobuf rejects a duplicate number in an enum unless <c>allow_alias</c> is set, so a
    /// descriptor that carries two members at the same number is proof the option is set - the file
    /// would not have compiled otherwise.
    /// </remarks>
    private const int AliasProofThreshold = 2;

    // ---- Proto names of the enums this suite addresses -------------------------------------------
    //  Dot-boundary suffixes rather than bare simple names: `Value` alone matches RetCode.Value,
    //  XmlParseStatus.Value and SqliteResultCode.Value, and ContractDescriptors reports that ambiguity
    //  as an error rather than resolving it to a first match. The qualified forms are unambiguous.

    /// <summary>The wire return-code alphabet, <c>common.v1.RetCode.Value</c>.</summary>
    private const string WireReturnCodeEnumName = "RetCode.Value";

    /// <summary>The wire XML parse-status alphabet, <c>common.v1.XmlParseStatus.Value</c>.</summary>
    private const string WireXmlStatusEnumName = "XmlParseStatus.Value";

    /// <summary>The wire SQLite result-code alphabet, <c>common.v1.SqliteResultCode.Value</c>.</summary>
    private const string WireSqliteEnumName = "SqliteResultCode.Value";

    /// <summary>The wire DataWindow buffer alphabet, <c>common.v1.DwBuffer</c>.</summary>
    private const string WireBufferEnumName = "DwBuffer";

    /// <summary>The wire DataWindow item-status alphabet, <c>common.v1.ItemStatus</c>.</summary>
    private const string WireItemStatusEnumName = "ItemStatus";

    // ---- Prefixes that partition the catalogue into its three legacy families --------------------
    //  retcode.sru sections itself with the banner comments `/*--- XML Parser ---*/` [:L81] and
    //  `/*--- SQLite ---*/` [:L104], and every member inside each section carries the matching name
    //  prefix. The prefixes are therefore the oracle's own partition rather than a convention invented
    //  here, which is what makes it legitimate to reason about "the return-code family" as the members
    //  carrying neither prefix.

    /// <summary>Name prefix of the XML parse-status family [retcode.sru:L81-L100].</summary>
    private const string XmlStatusPrefix = "XML_";

    /// <summary>Name prefix of the SQLite result-code family [retcode.sru:L104-L206].</summary>
    private const string SqlitePrefix = "SQLITE_";

    // ==============================================================================================
    //  THE KERNEL SIDE - read by reflection, never listed
    // ==============================================================================================

    /// <summary>
    /// Every public static literal constant declared on <see cref="KernelRetCode"/>, as authored name to
    /// value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>IsLiteral &amp;&amp; !IsInitOnly</c> selects <c>const</c> and excludes <c>static readonly</c>:
    /// a <c>static readonly</c> field has no compile-time constant value, so
    /// <see cref="FieldInfo.GetRawConstantValue"/> would throw on it rather than return anything useful.
    /// The catalogue is entirely <c>const long</c> today; the filter is what keeps this helper correct if
    /// a computed member is ever added beside it.
    /// </para>
    /// <para>
    /// <see cref="FieldInfo.GetRawConstantValue"/> is typed <c>object?</c>, and the widening below is
    /// exhaustive over the integral types a return code could plausibly be declared as. A field whose
    /// raw value is none of them is left out rather than coerced, because coercing an unexpected type
    /// would invent an expectation. Nothing is null-suppressed: the <c>null</c> arm falls through the
    /// pattern to <see langword="null"/> and the entry is skipped (constraint C-H).
    /// </para>
    /// <para>
    /// Ordinal, case-sensitive keys. A case-insensitive map would let <c>Cancelled</c> satisfy an
    /// assertion about <c>CANCELLED</c>, and the exact spelling is the thing under test.
    /// </para>
    /// </remarks>
    private static Dictionary<string, long> KernelConstants()
    {
        Dictionary<string, long> constants = new(StringComparer.Ordinal);

        foreach (FieldInfo field in typeof(KernelRetCode).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (!field.IsLiteral || field.IsInitOnly)
            {
                continue;
            }

            long? value = field.GetRawConstantValue() switch
            {
                long declaredAsLong => declaredAsLong,
                int declaredAsInt => declaredAsInt,
                short declaredAsShort => declaredAsShort,
                sbyte declaredAsSByte => declaredAsSByte,
                uint declaredAsUInt => declaredAsUInt,
                ushort declaredAsUShort => declaredAsUShort,
                byte declaredAsByte => declaredAsByte,
                _ => null,
            };

            if (value.HasValue)
            {
                constants[field.Name] = value.Value;
            }
        }

        return constants;
    }

    /// <summary>
    /// The kernel constant names in a stable order, so that theory rows and failure messages are
    /// reproducible run to run.
    /// </summary>
    /// <remarks>
    /// Ordinal ordering rather than reflection order: <see cref="Type.GetFields(BindingFlags)"/> makes no
    /// guarantee about the order it returns members in, and an unstable test id would make a single red
    /// row hard to correlate between two runs.
    /// </remarks>
    private static IReadOnlyList<string> KernelConstantNamesInOrder() =>
        [.. KernelConstants().Keys.OrderBy(static name => name, StringComparer.Ordinal)];

    // ==============================================================================================
    //  THE WIRE SIDE - descriptors, scoped to common.v1
    // ==============================================================================================

    /// <summary>
    /// Every enum declared in <c>common.v1.proto</c>, whether at file scope or nested inside a message.
    /// </summary>
    /// <remarks>
    /// Scoped to the shared vocabulary on purpose. <c>dataservices.v1</c> and <c>persistence.v1</c>
    /// declare their own alphabets - <c>SQL_MS_*</c> among them - which are not part of the return-code
    /// catalogue and have no counterpart on <see cref="KernelRetCode"/>. Reference equality is the right
    /// comparison because a <see cref="FileDescriptor"/> is a process-wide singleton reached through the
    /// generated reflection holder.
    /// </remarks>
    private static IReadOnlyList<EnumDescriptor> CommonEnums() =>
        [.. ContractDescriptors.AllEnums()
            .Where(static declared => ReferenceEquals(declared.File, ContractDescriptors.Common))];

    /// <summary>
    /// Every enum value declared anywhere in <c>common.v1.proto</c>, as a flat list of declaration sites.
    /// </summary>
    /// <remarks>
    /// A list of sites rather than a map, because two sites sharing a name is a FINDING this suite has to
    /// be able to report - see <see cref="NoValueNameIsDeclaredTwiceWithinTheSharedVocabulary"/>. Folding
    /// to a map first would silently discard one of the two.
    /// </remarks>
    private static IReadOnlyList<EnumValueDescriptor> CommonEnumValueSites() =>
        [.. CommonEnums().SelectMany(static declared => declared.Values)];

    /// <summary>
    /// Every value declared in <c>common.v1.proto</c>, as authored proto name to wire number.
    /// </summary>
    /// <remarks>
    /// Keyed on <see cref="EnumValueDescriptor.Name"/> - the SCREAMING_SNAKE spelling as authored, not
    /// the PascalCased CLR member protoc generates from it. Ordinal and case-sensitive, for the same
    /// reason the kernel map is. Duplicate names cannot corrupt this map unnoticed: every name in it is
    /// checked for exactly one declaration site by a dedicated test.
    /// </remarks>
    private static Dictionary<string, long> WireValues()
    {
        Dictionary<string, long> values = new(StringComparer.Ordinal);

        foreach (EnumValueDescriptor site in CommonEnumValueSites())
        {
            values[site.Name] = site.Number;
        }

        return values;
    }

    /// <summary>
    /// The names <c>common.v1.proto</c> declares that have no counterpart on <see cref="KernelRetCode"/>,
    /// which is a closed and audited set rather than an open one.
    /// </summary>
    /// <remarks>
    /// These eight are the alphabets that are NOT part of the 156-constant return-code catalogue, so
    /// their absence from the kernel is correct rather than a gap: the three DataWindow buffers, the four
    /// item statuses - all seven of which are PowerBuilder BUILT-IN enumerated types with no constant
    /// declaration in the legacy sources at all - and the single rich-error trailer key, which is a
    /// gRPC transport detail with no legacy analogue whatsoever.
    /// </remarks>
    private static IReadOnlySet<string> ExpectedWireNamesWithoutAKernelConstant { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "DW_BUFFER_PRIMARY",
            "DW_BUFFER_DELETE",
            "DW_BUFFER_FILTER",
            "ITEM_STATUS_NOT_MODIFIED",
            "ITEM_STATUS_DATA_MODIFIED",
            "ITEM_STATUS_NEW",
            "ITEM_STATUS_NEW_MODIFIED",
            "RICH_ERROR_TRAILER_KEY_V1",
        };

    // ==============================================================================================
    //  GUARDS AGAINST A VACUOUS PASS
    //  --------------------------------------------------------------------------------------------
    //  Everything else in this file compares one reflected set against another, and a comparison over
    //  an empty set SUCCEEDS. These three tests are what stop the suite reporting green while checking
    //  nothing - the single worst failure mode available to it, because it removes the only edge
    //  between the two transcriptions without removing anyone's confidence that the edge exists.
    //
    //  Three separate guards rather than one, because they fail on three different mistakes: a renamed
    //  or relocated class empties the map entirely, a partial refactor moves SOME constants out and
    //  trips the anchors, and a quiet deletion leaves the anchors intact but the count short.
    // ==============================================================================================

    /// <summary>
    /// The nine constants whose presence proves the kernel catalogue is being read at all.
    /// </summary>
    /// <remarks>
    /// Chosen to span the whole shape of the catalogue rather than to be representative of it, so that a
    /// partial refactor cannot leave every anchor standing: the zero, the prevention, the failure and the
    /// cancellation from the head [retcode.sru:L39-L45], both ends of the contiguous block
    /// [<c>:L46</c>, <c>:L76</c>], and the three distant outliers [<c>:L77-L79</c>].
    /// </remarks>
    public static TheoryData<string> AnchorConstantNames =>
    [
        "OK",                   // [retcode.sru:L39]
        "PREVENT",              // [retcode.sru:L42]
        "FAILED",               // [retcode.sru:L43]
        "CANCELLED",            // [retcode.sru:L45]
        "E_INVALID_ARGUMENT",   // [retcode.sru:L46] - first of the contiguous block
        "E_RETRY",              // [retcode.sru:L76] - last of the contiguous block
        "E_NO_SUPPORT",         // [retcode.sru:L77]
        "E_NO_IMPLEMENTATION",  // [retcode.sru:L78]
        "UNKNOWN",              // [retcode.sru:L79]
    ];

    /// <summary>
    /// The reflected kernel catalogue is not empty, so no comparison below is vacuous.
    /// </summary>
    [Fact]
    public void TheKernelExpectationSetIsNotEmpty()
    {
        Dictionary<string, long> kernel = KernelConstants();

        Assert.NotEmpty(kernel);
    }

    /// <summary>
    /// Every anchor constant is still declared on <see cref="KernelRetCode"/> under its authored name.
    /// </summary>
    /// <param name="constantName">The authored SCREAMING_SNAKE spelling, from the oracle.</param>
    [Theory]
    [MemberData(nameof(AnchorConstantNames))]
    public void TheKernelExpectationSetDeclaresEveryAnchorConstant(string constantName)
    {
        Dictionary<string, long> kernel = KernelConstants();

        Assert.True(
            kernel.ContainsKey(constantName),
            $"PowerFramework.Shared.Kernel.RetCode no longer declares '{constantName}'. Either the "
                + "constant was renamed - which AAP 0.4.5.3 forbids, because the spelling appears in "
                + "serialized payloads, log records and characterization recordings - or the catalogue "
                + "moved to another type, in which case every comparison in this file is now reading an "
                + "incomplete expectation set and reporting success for the wrong reason.");
    }

    /// <summary>
    /// The kernel declares the whole legacy catalogue, all <see cref="LegacyCatalogueSize"/> constants.
    /// </summary>
    /// <remarks>
    /// An exact count rather than a floor. A floor would tolerate a silent deletion, and the catalogue is
    /// closed: <c>ws_objects/pfw.shared.pbl.src/retcode.sru</c> is read-only (constraint C-C), so it
    /// cannot grow, and nothing may be added to the managed side that the oracle does not declare.
    /// </remarks>
    [Fact]
    public void TheKernelExpectationSetHoldsTheWholeLegacyCatalogue()
    {
        Dictionary<string, long> kernel = KernelConstants();

        Assert.Equal(LegacyCatalogueSize, kernel.Count);
    }

    // ==============================================================================================
    //  DIRECTION 1 - WIRE SUBSET OF KERNEL
    //  --------------------------------------------------------------------------------------------
    //  For every value common.v1 declares whose name is also a kernel constant, the two numbers must be
    //  equal. Rows are generated from the INTERSECTION rather than from the whole wire set, because
    //  common.v1 legitimately declares eight names that are not return codes and have no kernel
    //  counterpart - the buffers, the item statuses and the rich-error trailer key. Those eight are not
    //  skipped quietly: the set of names outside the intersection is asserted to be exactly them.
    // ==============================================================================================

    /// <summary>
    /// One row per <c>common.v1</c> value that names a kernel constant: the authored name and the number
    /// the WIRE declares for it.
    /// </summary>
    /// <remarks>
    /// The number here is read from the descriptor rather than from the kernel deliberately - this
    /// direction exists to carry the wire's own claim into the assertion, so that a failure message can
    /// print both sides. Ordered ordinally so a red row is correlatable between runs.
    /// </remarks>
    public static TheoryData<string, long> WireValuesNamingAKernelConstant
    {
        get
        {
            Dictionary<string, long> kernel = KernelConstants();
            TheoryData<string, long> rows = [];

            foreach (KeyValuePair<string, long> wireValue in WireValues()
                .OrderBy(static entry => entry.Key, StringComparer.Ordinal))
            {
                if (kernel.ContainsKey(wireValue.Key))
                {
                    rows.Add(wireValue.Key, wireValue.Value);
                }
            }

            return rows;
        }
    }

    /// <summary>
    /// A value published on the wire carries the same number as the kernel constant of the same name.
    /// </summary>
    /// <param name="valueName">The authored proto spelling, from <see cref="EnumValueDescriptor.Name"/>.</param>
    /// <param name="wireNumber">The number <c>common.v1.proto</c> declares for it.</param>
    /// <remarks>
    /// The direction that matters at a service boundary: a payload carries the wire number, and the
    /// receiving service classifies it with the kernel's predicates. A mismatch here means a code means
    /// one thing before it crosses the boundary and another after.
    /// </remarks>
    [Theory]
    [MemberData(nameof(WireValuesNamingAKernelConstant))]
    public void EveryWireValueNamingAKernelConstantCarriesTheSameNumber(string valueName, long wireNumber)
    {
        Dictionary<string, long> kernel = KernelConstants();

        Assert.True(
            kernel.TryGetValue(valueName, out long kernelValue),
            $"'{valueName}' was in the intersection when the rows were built but is absent from the "
                + "kernel catalogue now. The two reads disagree, which should be impossible for an "
                + "immutable type.");

        Assert.True(
            kernelValue == wireNumber,
            $"{valueName}: wire={wireNumber}, kernel={kernelValue}. The wire alphabet in "
                + "common.v1.proto and the managed catalogue in PowerFramework.Shared.Kernel.RetCode "
                + "are two transcriptions of ws_objects/pfw.shared.pbl.src/retcode.sru and must carry "
                + "identical numbers; nothing else in the build checks this.");
    }

    /// <summary>
    /// The compared intersection covers the whole catalogue, so Direction 1 checked every constant.
    /// </summary>
    /// <remarks>
    /// Without this, a value RENAMED on the wire would simply drop out of the intersection and its row
    /// would vanish - leaving a smaller theory that still passed. Direction 2 catches the same mistake
    /// from the other side; both are kept because a suite whose only guarantee is "the rows that ran
    /// passed" guarantees nothing about the rows that did not.
    /// </remarks>
    [Fact]
    public void TheComparedIntersectionCoversTheWholeLegacyCatalogue()
    {
        Dictionary<string, long> kernel = KernelConstants();
        Dictionary<string, long> wire = WireValues();
        int intersectionSize = kernel.Keys.Count(wire.ContainsKey);

        Assert.Equal(LegacyCatalogueSize, intersectionSize);
    }

    /// <summary>
    /// The wire names that have no kernel counterpart are exactly the eight non-return-code names.
    /// </summary>
    /// <remarks>
    /// Closes the loophole that Direction 1's intersection would otherwise open. A ninth unaudited name
    /// appearing in <c>common.v1</c> - or one of these eight disappearing - is reported here rather than
    /// silently excluded from the comparison. See
    /// <see cref="ExpectedWireNamesWithoutAKernelConstant"/> for why the eight are correct.
    /// </remarks>
    [Fact]
    public void TheOnlyWireNamesWithoutAKernelConstantAreTheNonReturnCodeAlphabets()
    {
        Dictionary<string, long> kernel = KernelConstants();

        IReadOnlyList<string> unmatched =
        [
            .. WireValues().Keys
                .Where(name => !kernel.ContainsKey(name))
                .OrderBy(static name => name, StringComparer.Ordinal),
        ];

        Assert.Equal(
            ExpectedWireNamesWithoutAKernelConstant.OrderBy(static name => name, StringComparer.Ordinal),
            unmatched);
    }

    // ==============================================================================================
    //  DIRECTION 2 - KERNEL SUBSET OF WIRE, PLUS COVERAGE
    //  --------------------------------------------------------------------------------------------
    //  Every kernel constant must be declared on the wire with the same number, and the set of kernel
    //  constants ABSENT from the wire must be EMPTY.
    //
    //  The empty absent set is a finding, not an assumption. common.v1.proto carries the return codes,
    //  the XML parse statuses AND the SQLite result codes - 41 + 17 + 98 = 156, reconciling exactly to
    //  the kernel catalogue - and the .proto states the reasoning for carrying the two deferred-service
    //  families explicitly: a VALUE IS NOT A CAPABILITY, so publishing the 17 XML numbers keeps the
    //  156-constant agreement whole while implying no XML support anywhere. Both sides were enumerated
    //  and compared before this assertion was written; the absent set was empty, so it is asserted
    //  empty. Had it not been, the intended absent set would be enumerated here with its reason instead
    //  of the test being weakened to pass.
    // ==============================================================================================

    /// <summary>
    /// One row per kernel constant: its authored name and the number the KERNEL declares for it.
    /// </summary>
    /// <remarks>
    /// All <see cref="LegacyCatalogueSize"/> of them, read by reflection. This is the direction the file
    /// requirement names as the point of the suite - the expectation is the kernel constant itself rather
    /// than a literal, so the row drifts automatically with the code it guards.
    /// </remarks>
    public static TheoryData<string, long> KernelConstantRows
    {
        get
        {
            Dictionary<string, long> kernel = KernelConstants();
            TheoryData<string, long> rows = [];

            foreach (string name in KernelConstantNamesInOrder())
            {
                rows.Add(name, kernel[name]);
            }

            return rows;
        }
    }

    /// <summary>Every kernel constant name, for the per-name declaration-site checks.</summary>
    public static TheoryData<string> KernelConstantNames
    {
        get
        {
            TheoryData<string> rows = [];

            foreach (string name in KernelConstantNamesInOrder())
            {
                rows.Add(name);
            }

            return rows;
        }
    }

    /// <summary>
    /// A kernel constant is declared on the wire under the same authored name and with the same number.
    /// </summary>
    /// <param name="constantName">The authored SCREAMING_SNAKE spelling from the kernel.</param>
    /// <param name="kernelValue">The number the kernel declares - the expectation, read not written.</param>
    [Theory]
    [MemberData(nameof(KernelConstantRows))]
    public void EveryKernelConstantIsDeclaredOnTheWireWithTheSameNumber(string constantName, long kernelValue)
    {
        Dictionary<string, long> wire = WireValues();

        Assert.True(
            wire.TryGetValue(constantName, out long wireNumber),
            $"'{constantName}' is declared on PowerFramework.Shared.Kernel.RetCode with the value "
                + $"{kernelValue} but no enum in common.v1.proto declares that name. A constant reachable "
                + "in process but absent from the boundary cannot be reported across it, so the code "
                + "would have to be substituted or dropped at the wire - which is a behavioural change "
                + "of exactly the kind constraint C-B forbids.");

        Assert.True(
            wireNumber == kernelValue,
            $"{constantName}: kernel={kernelValue}, wire={wireNumber}. Both are transcriptions of "
                + "ws_objects/pfw.shared.pbl.src/retcode.sru and must agree exactly.");
    }

    /// <summary>
    /// No kernel constant is missing from the wire - the absent set is empty, and named when it is not.
    /// </summary>
    /// <remarks>
    /// The aggregate companion to the per-name theory above. It exists for its failure message: when a
    /// whole family goes missing, one line listing the family beats 98 individually red rows.
    /// </remarks>
    [Fact]
    public void NoKernelConstantIsAbsentFromTheWire()
    {
        Dictionary<string, long> wire = WireValues();

        IReadOnlyList<string> absent =
        [
            .. KernelConstantNamesInOrder().Where(name => !wire.ContainsKey(name)),
        ];

        Assert.Empty(absent);
    }

    // ==============================================================================================
    //  EXACTLY ONE DECLARATION SITE PER NAME
    //  --------------------------------------------------------------------------------------------
    //  A legacy code declared in TWO enums with two different numbers would be a divergence of the most
    //  dangerous shape available: each enum internally consistent, the two disagreeing, and both
    //  agreeing with the kernel on whichever one a given test happened to read. Direction 1 and
    //  Direction 2 above fold the wire to a map and so cannot see it.
    //
    //  Protobuf does not prevent this. It rejects a duplicate value NAME only within one enum scope, so
    //  the same name in RetCode.Value and in a nested enum of another file compiles cleanly. The check
    //  therefore spans all three published files rather than common.v1 alone - a name that leaked into
    //  dataservices.v1 or persistence.v1 is exactly what it is looking for.
    // ==============================================================================================

    /// <summary>
    /// A kernel constant name is declared at exactly one site across the whole published boundary.
    /// </summary>
    /// <param name="constantName">The authored SCREAMING_SNAKE spelling.</param>
    /// <remarks>
    /// <see cref="ContractDescriptors.FindEnumValuesNamed"/> returns every declaration site across all
    /// three protocol definitions, which is why it returns a list rather than a single value.
    /// </remarks>
    [Theory]
    [MemberData(nameof(KernelConstantNames))]
    public void EveryKernelConstantNameIsDeclaredExactlyOnceAcrossTheWholeBoundary(string constantName)
    {
        IReadOnlyList<EnumValueDescriptor> sites = ContractDescriptors.FindEnumValuesNamed(constantName);

        EnumValueDescriptor site = Assert.Single(sites);

        Assert.True(
            ReferenceEquals(site.File, ContractDescriptors.Common),
            $"'{constantName}' is declared in {site.File.Name} rather than in common.v1.proto. The "
                + "return-code catalogue is shared vocabulary and belongs in the file both siblings "
                + "import; a copy in one sibling would drift from the other.");
    }

    /// <summary>
    /// No value name is declared twice inside <c>common.v1.proto</c>, including the eight that have no
    /// kernel counterpart.
    /// </summary>
    /// <remarks>
    /// The per-name theory above covers the 156 catalogue names. This covers the remaining eight as well,
    /// so that folding the wire to a map in <see cref="WireValues"/> cannot hide a collision anywhere.
    /// </remarks>
    [Fact]
    public void NoValueNameIsDeclaredTwiceWithinTheSharedVocabulary()
    {
        IReadOnlyList<string> duplicated =
        [
            .. CommonEnumValueSites()
                .GroupBy(static site => site.Name, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .Select(static group =>
                    $"{group.Key} declared in {string.Join(", ", group.Select(static site => site.EnumDescriptor.FullName))}")
                .OrderBy(static description => description, StringComparer.Ordinal),
        ];

        Assert.Empty(duplicated);
    }

    // ==============================================================================================
    //  ALIASING - ASSERTED POSITIVELY, BECAUSE THE ALIASES ARE THE CONTRACT (constraint C-B)
    //  --------------------------------------------------------------------------------------------
    //  Three names for 0 and two for -2 are preserved legacy spellings, not redundancy to be collapsed.
    //  Each appears at legacy call sites, so each can appear in a serialized payload, a log record or a
    //  characterization recording, and a rename silently invalidates every stored comparison
    //  (AAP 0.4.5.3, 0.8.2). These tests fail if an alias is DROPPED - the tidying a well-meaning editor
    //  is most likely to attempt.
    //
    //  Note what is deliberately NOT asserted anywhere in this file: that a number appears once. It does
    //  not, it must not, and a uniqueness-of-number assertion would fail against a correct contract.
    // ==============================================================================================

    /// <summary>
    /// The names belonging to the return-code family - those carrying neither of the two family prefixes.
    /// </summary>
    /// <remarks>
    /// <c>retcode.sru</c> partitions itself with the banner comments <c>/*--- XML Parser ---*/</c>
    /// [<c>:L81</c>] and <c>/*--- SQLite ---*/</c> [<c>:L104</c>], and every member inside each section
    /// carries the matching prefix, so the partition is the oracle's own rather than one invented here.
    /// Scoping the alias counts to this family is what makes them hold whether the wire fuses the three
    /// families into one aliased enum or splits them into three, since a fused enum would put five names
    /// on 0 rather than three.
    /// </remarks>
    private static IReadOnlyList<string> ReturnCodeFamilyNames() =>
    [
        .. KernelConstantNamesInOrder()
            .Where(static name =>
                !name.StartsWith(XmlStatusPrefix, StringComparison.Ordinal)
                && !name.StartsWith(SqlitePrefix, StringComparison.Ordinal)),
    ];

    /// <summary>The three spellings of zero, in oracle order [retcode.sru:L39-L41].</summary>
    public static TheoryData<string> ZeroSpellings => ["OK", "SUCCESS", "ALLOW"];

    /// <summary>Both spellings of the cancellation, in oracle order [retcode.sru:L44-L45].</summary>
    public static TheoryData<string> CancelledSpellings => ["CANCELED", "CANCELLED"];

    /// <summary>
    /// The two numbers the return-code family aliases, each paired with one of its spellings.
    /// </summary>
    /// <remarks>
    /// One spelling per number is enough: the test inspects the enum that CARRIES the spelling, and all
    /// spellings of one number must be co-resident there for the aliasing to be expressed at all.
    /// </remarks>
    public static TheoryData<string, long> AliasedNumbers
    {
        get
        {
            TheoryData<string, long> rows = [];

            rows.Add("OK", 0L);          // OK / SUCCESS / ALLOW [retcode.sru:L39-L41]
            rows.Add("CANCELLED", -2L);  // CANCELED / CANCELLED [retcode.sru:L44-L45]

            return rows;
        }
    }

    /// <summary>
    /// Each spelling of zero is declared on the wire, and carries zero.
    /// </summary>
    /// <param name="spelling"><c>OK</c>, <c>SUCCESS</c> or <c>ALLOW</c>.</param>
    /// <remarks>
    /// Zero is the pivot of the whole algebra: <c>IsSucceeded</c> tests <c>rtCode &gt;= RetCode.OK</c>
    /// [ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L12] and <c>IsFailed</c> tests
    /// <c>rtCode &lt; RetCode.OK</c> [isfailed.srf:L11-L12], so all three spellings are the boundary
    /// itself and any of them may appear in a payload.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ZeroSpellings))]
    public void EverySpellingOfZeroIsPreservedOnTheWire(string spelling)
    {
        Dictionary<string, long> wire = WireValues();

        Assert.True(
            wire.TryGetValue(spelling, out long wireNumber),
            $"The wire no longer declares '{spelling}'. All three spellings of 0 are preserved legacy "
                + "identifiers [retcode.sru:L39-L41]; dropping one is a rename in effect, which "
                + "AAP 0.4.5.3 forbids.");

        Assert.Equal(0L, wireNumber);
    }

    /// <summary>
    /// Each spelling of the cancellation is declared on the wire, and carries -2.
    /// </summary>
    /// <param name="spelling"><c>CANCELED</c> or <c>CANCELLED</c>.</param>
    /// <remarks>
    /// -2 is the value <c>IsFailed</c> EXPLICITLY EXCLUDES - its body reads
    /// <c>rtCode &lt; RetCode.OK and rtCode &lt;&gt; RetCode.CANCELLED</c>
    /// [ws_objects/pfw.shared.pbl.src/isfailed.srf:L11-L12] - while also failing
    /// <c>rtCode &gt;= RetCode.OK</c> [issucceeded.srf:L11-L12]. A cancellation is therefore NEITHER
    /// succeeded NOR failed: a tri-state hole in a nominally boolean algebra. That hole is preserved
    /// deliberately (constraint C-B), and it only works if this number is exactly -2 under both
    /// spellings, because the exclusion is written against the constant rather than against a range.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CancelledSpellings))]
    public void EverySpellingOfTheCancellationIsPreservedOnTheWire(string spelling)
    {
        Dictionary<string, long> wire = WireValues();

        Assert.True(
            wire.TryGetValue(spelling, out long wireNumber),
            $"The wire no longer declares '{spelling}'. Both the single-L and double-L spellings are "
                + "preserved legacy identifiers [retcode.sru:L44-L45].");

        Assert.Equal(-2L, wireNumber);
    }

    /// <summary>
    /// Exactly <see cref="ZeroSpellingCount"/> return-code names carry zero, and no more.
    /// </summary>
    /// <remarks>
    /// The count is asserted in both directions at once: fewer means an alias was dropped, more means one
    /// was invented. Scoped to the return-code family, because <c>XML_OK</c> [retcode.sru:L84] and
    /// <c>SQLITE_OK</c> [<c>:L107</c>] are also 0 in their own scopes and are not spellings of this one.
    /// </remarks>
    [Fact]
    public void ExactlyThreeReturnCodeNamesCarryZero()
    {
        Dictionary<string, long> wire = WireValues();

        IReadOnlyList<string> namesOnZero =
        [
            .. ReturnCodeFamilyNames()
                .Where(name => wire.TryGetValue(name, out long number) && number == 0L),
        ];

        Assert.Equal(ZeroSpellingCount, namesOnZero.Count);
    }

    /// <summary>Exactly <see cref="CancelledSpellingCount"/> return-code names carry -2, and no more.</summary>
    [Fact]
    public void ExactlyTwoReturnCodeNamesCarryTheCancellation()
    {
        Dictionary<string, long> wire = WireValues();

        IReadOnlyList<string> namesOnCancellation =
        [
            .. ReturnCodeFamilyNames()
                .Where(name => wire.TryGetValue(name, out long number) && number == -2L),
        ];

        Assert.Equal(CancelledSpellingCount, namesOnCancellation.Count);
    }

    /// <summary>
    /// The aliased numbers are carried by an enum that actually permits aliasing.
    /// </summary>
    /// <param name="spelling">Any spelling of the aliased number; its declaration site is inspected.</param>
    /// <param name="aliasedNumber">The number that number carries more than one name for.</param>
    /// <remarks>
    /// <para>
    /// Proven INDIRECTLY, and the indirection is the point. Protobuf rejects a duplicate number inside an
    /// enum unless <c>option allow_alias = true</c> is set, so an enum descriptor that carries two or
    /// more members at one number IS the proof the option is set - the file could not have compiled
    /// otherwise. Reading the option itself would add nothing and would tie the assertion to a descriptor
    /// surface that need not expose it.
    /// </para>
    /// <para>
    /// This is the one test that reads the LAYOUT rather than only the name set, and it survives either
    /// layout: the split layout found in <c>common.v1.proto</c> puts three members on 0 inside
    /// <c>RetCode.Value</c>, and a fused layout would put five there. Both clear the threshold. What it
    /// correctly rejects is a layout that scattered the spellings across separate enums, which would
    /// leave each one alone at its number and so express no aliasing at all.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AliasedNumbers))]
    public void TheEnumCarryingAnAliasedNumberPermitsAliasing(string spelling, long aliasedNumber)
    {
        EnumValueDescriptor site = Assert.Single(ContractDescriptors.FindEnumValuesNamed(spelling));
        EnumDescriptor carrier = site.EnumDescriptor;

        int membersOnThatNumber = carrier.Values.Count(value => value.Number == aliasedNumber);

        Assert.True(
            membersOnThatNumber >= AliasProofThreshold,
            $"{carrier.FullName} declares only {membersOnThatNumber} member(s) at {aliasedNumber}, so "
                + "the legacy aliasing is not expressed there. Protobuf permits two members to share a "
                + "number only when allow_alias is set, so a single member at an aliased number means "
                + "the other spellings were either dropped or moved to a different enum - and the "
                + "preserved spellings appear in stored payloads and recordings (AAP 0.4.5.3).");
    }

    // ==============================================================================================
    //  FOCUSED ANCHORS - THE THIRD SIDE OF THE TRIANGLE
    //  --------------------------------------------------------------------------------------------
    //  Everything above proves the wire and the kernel agree WITH EACH OTHER. That is not the same as
    //  either agreeing with the legacy: edited together, both could drift and every agreement test would
    //  stay green. The rows below carry the value TRANSCRIBED FROM THE ORACLE ITSELF and assert it
    //  against both transcriptions, which closes that gap.
    //
    //  This is the one place literals are not merely permitted but required - the whole purpose of the
    //  row is to be an independent third opinion. Each cites the exact oracle line it came from, and the
    //  set is restricted to the values whose absolute magnitude is load-bearing: the pivot of the
    //  predicate algebra, the tri-state hole, the two ends of the dense block, and the three outliers.
    // ==============================================================================================

    /// <summary>
    /// Constants whose exact value is transcribed from <c>retcode.sru</c> and checked against both sides.
    /// </summary>
    public static TheoryData<string, long> OracleTranscribedAnchors
    {
        get
        {
            TheoryData<string, long> rows = [];

            // The pivot of the algebra. IsSucceeded tests `>= RetCode.OK` [issucceeded.srf:L11-L12].
            rows.Add("OK", 0L);                        // [retcode.sru:L39]
            rows.Add("SUCCESS", 0L);                   // [retcode.sru:L40]
            rows.Add("ALLOW", 0L);                     // [retcode.sru:L41]

            // PREVENT = 1 is the reason a PREVENTION READS AS A SUCCESS: IsSucceeded tests
            // `rtCode >= RetCode.OK` [issucceeded.srf:L11-L12] and 1 satisfies it. Preserved as a
            // deliberate legacy behaviour, not a defect to correct (constraint C-B). Renumbering it
            // negative would silently reclassify every prevention in the system as a failure.
            rows.Add("PREVENT", 1L);                   // [retcode.sru:L42]

            rows.Add("FAILED", -1L);                   // [retcode.sru:L43]

            // -2 is the value IsFailed EXPLICITLY EXCLUDES - `rtCode < RetCode.OK and
            // rtCode <> RetCode.CANCELLED` [isfailed.srf:L11-L12] - while it also fails IsSucceeded.
            // A cancellation is therefore NEITHER succeeded NOR failed: the tri-state hole, preserved
            // deliberately. The exclusion is written against the constant, so the number must be exact.
            rows.Add("CANCELED", -2L);                 // [retcode.sru:L44]
            rows.Add("CANCELLED", -2L);                // [retcode.sru:L45]

            // Both ends of the contiguous block, so a shift of the whole run is caught even if the run
            // stays internally gap-free.
            rows.Add("E_INVALID_ARGUMENT", -3L);       // [retcode.sru:L46]
            rows.Add("E_RETRY", -33L);                 // [retcode.sru:L76]

            // The three outliers, far below the dense block. E_NO_IMPLEMENTATION is the value the paging
            // rewriter's `case else` arm returns for an unrecognised database type, so its magnitude is
            // observable in a returned status rather than internal.
            rows.Add("E_NO_SUPPORT", -2000L);          // [retcode.sru:L77]
            rows.Add("E_NO_IMPLEMENTATION", -2001L);   // [retcode.sru:L78]
            rows.Add("UNKNOWN", -4000L);               // [retcode.sru:L79]

            return rows;
        }
    }

    /// <summary>
    /// A value transcribed from the oracle matches BOTH the kernel constant and the wire value.
    /// </summary>
    /// <param name="constantName">The authored SCREAMING_SNAKE spelling.</param>
    /// <param name="oracleValue">The value as written in <c>ws_objects/pfw.shared.pbl.src/retcode.sru</c>.</param>
    [Theory]
    [MemberData(nameof(OracleTranscribedAnchors))]
    public void EveryOracleTranscribedAnchorMatchesBothTranscriptions(string constantName, long oracleValue)
    {
        Dictionary<string, long> kernel = KernelConstants();
        Dictionary<string, long> wire = WireValues();

        Assert.True(
            kernel.TryGetValue(constantName, out long kernelValue),
            $"The kernel no longer declares '{constantName}', which retcode.sru declares as {oracleValue}.");

        Assert.True(
            wire.TryGetValue(constantName, out long wireNumber),
            $"The wire no longer declares '{constantName}', which retcode.sru declares as {oracleValue}.");

        Assert.Equal(oracleValue, kernelValue);
        Assert.Equal(oracleValue, wireNumber);
    }

    // ==============================================================================================
    //  THE TWO DENSE RUNS - ASSERTED AS RUNS, NOT AS A BAG OF VALUES
    //  --------------------------------------------------------------------------------------------
    //  The contiguous error block and the XML parse-status family are each a GAP-FREE run of integers in
    //  the oracle. Checking the members individually cannot see a gap or a collision inside the run, so
    //  each integer in the span gets its own row and is required to be claimed by exactly one name on
    //  each side. A missing integer and a doubly-claimed one both surface as one red row naming the
    //  integer.
    // ==============================================================================================

    /// <summary>Every integer in the contiguous error block, from -3 down to -33 [retcode.sru:L46-L76].</summary>
    public static TheoryData<long> DenseBlockValues
    {
        get
        {
            TheoryData<long> rows = [];

            for (long value = DenseBlockFirstValue; value >= DenseBlockLastValue; value--)
            {
                rows.Add(value);
            }

            return rows;
        }
    }

    /// <summary>Every integer in the XML parse-status range, from 0 to 16 [retcode.sru:L84-L100].</summary>
    public static TheoryData<long> XmlStatusValues
    {
        get
        {
            TheoryData<long> rows = [];

            for (long value = XmlStatusFirstValue; value <= XmlStatusLastValue; value++)
            {
                rows.Add(value);
            }

            return rows;
        }
    }

    /// <summary>
    /// Each integer in the contiguous block is claimed by exactly one return code, on both sides.
    /// </summary>
    /// <param name="value">An integer between -3 and -33 inclusive.</param>
    /// <remarks>
    /// The density is the oracle's own: <c>retcode.sru:L46-L76</c> declares 31 constants over 31
    /// consecutive integers, so there is no gap to fill and no value to add. Scoped to the return-code
    /// family, because the SQLite family has no negative members and the XML family none either.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DenseBlockValues))]
    public void EveryIntegerInTheContiguousErrorBlockIsClaimedByExactlyOneReturnCode(long value)
    {
        Dictionary<string, long> kernel = KernelConstants();
        Dictionary<string, long> wire = WireValues();

        IReadOnlyList<string> kernelClaimants =
        [
            .. ReturnCodeFamilyNames().Where(name => kernel[name] == value),
        ];

        string claimant = Assert.Single(kernelClaimants);

        Assert.True(
            wire.TryGetValue(claimant, out long wireNumber),
            $"The kernel claims {value} with '{claimant}' but the wire does not declare that name.");

        Assert.Equal(value, wireNumber);
    }

    /// <summary>
    /// The contiguous block has exactly <see cref="DenseBlockSize"/> members, so nothing was added to it.
    /// </summary>
    /// <remarks>
    /// The per-integer theory proves every integer in the span is claimed once; this proves no name sits
    /// in the span that the span does not account for - together they pin the run exactly.
    /// </remarks>
    [Fact]
    public void TheContiguousErrorBlockHasExactlyThirtyOneMembers()
    {
        Dictionary<string, long> kernel = KernelConstants();

        IReadOnlyList<string> inBlock =
        [
            .. ReturnCodeFamilyNames()
                .Where(name => kernel[name] <= DenseBlockFirstValue && kernel[name] >= DenseBlockLastValue),
        ];

        Assert.Equal(DenseBlockSize, inBlock.Count);
    }

    /// <summary>
    /// Each integer in the XML parse-status range is claimed by exactly one <c>XML_*</c> name, both sides.
    /// </summary>
    /// <param name="value">An integer between 0 and 16 inclusive.</param>
    /// <remarks>
    /// Carried on the wire even though the XML object family belongs to Documents, one of the four
    /// deferred services (constraint C-D). That is not a scope breach and the distinction matters: A
    /// VALUE IS NOT A CAPABILITY. These 17 integers are part of the 156-constant algebra the kernel ports
    /// from a single in-scope oracle object, so omitting them would leave a 17-value hole in the very
    /// agreement this file exists to check, while carrying them grants no XML capability to any consumer.
    /// </remarks>
    [Theory]
    [MemberData(nameof(XmlStatusValues))]
    public void EveryIntegerInTheXmlStatusRangeIsClaimedByExactlyOneXmlStatus(long value)
    {
        Dictionary<string, long> kernel = KernelConstants();
        Dictionary<string, long> wire = WireValues();

        IReadOnlyList<string> claimants =
        [
            .. XmlStatusFamilyNames().Where(name => kernel[name] == value),
        ];

        string claimant = Assert.Single(claimants);

        Assert.True(
            wire.TryGetValue(claimant, out long wireNumber),
            $"The kernel claims XML status {value} with '{claimant}' but the wire does not declare it.");

        Assert.Equal(value, wireNumber);
    }

    /// <summary>
    /// The XML parse-status family has exactly <see cref="XmlStatusFamilySize"/> members, spanning 0 to 16.
    /// </summary>
    [Fact]
    public void TheXmlStatusFamilyHasExactlySeventeenMembers()
    {
        IReadOnlyList<string> family = XmlStatusFamilyNames();

        Assert.Equal(XmlStatusFamilySize, family.Count);
    }

    /// <summary>
    /// The XML parse-status family is carried by its own wire enum rather than folded into the return
    /// codes.
    /// </summary>
    /// <remarks>
    /// A scope check rather than a value check, and the reason it matters is a real collision:
    /// <c>XML_E_FILE_NOT_FOUND</c> is 1 [retcode.sru:L85] while <c>E_FILE_NOT_FOUND</c> is -15
    /// [<c>:L58</c>], and <c>XML_OK</c>, <c>OK</c> and <c>SQLITE_OK</c> are all 0. Three code spaces kept
    /// harmless by three scopes. Folding them into one enum would make 1 mean both "prevented" and "XML
    /// file not found" in the same alphabet, which no consumer could disambiguate.
    /// </remarks>
    [Fact]
    public void TheXmlAndSqliteFamiliesAreCarriedByTheirOwnWireEnums()
    {
        EnumDescriptor returnCodes = ContractDescriptors.RequireEnum(WireReturnCodeEnumName);
        EnumDescriptor xmlStatuses = ContractDescriptors.RequireEnum(WireXmlStatusEnumName);
        EnumDescriptor sqliteCodes = ContractDescriptors.RequireEnum(WireSqliteEnumName);

        Assert.NotSame(returnCodes, xmlStatuses);
        Assert.NotSame(returnCodes, sqliteCodes);
        Assert.NotSame(xmlStatuses, sqliteCodes);

        Assert.Equal(XmlStatusFamilySize, xmlStatuses.Values.Count);
    }

    /// <summary>The names of the XML parse-status family [retcode.sru:L84-L100].</summary>
    private static IReadOnlyList<string> XmlStatusFamilyNames() =>
    [
        .. KernelConstantNamesInOrder()
            .Where(static name => name.StartsWith(XmlStatusPrefix, StringComparison.Ordinal)),
    ];

    /// <summary>The names of the SQLite result-code family [retcode.sru:L107-L206].</summary>
    private static IReadOnlyList<string> SqliteFamilyNames() =>
    [
        .. KernelConstantNamesInOrder()
            .Where(static name => name.StartsWith(SqlitePrefix, StringComparison.Ordinal)),
    ];

    // ==============================================================================================
    //  THE SQLITE EXTENDED RESULT CODES - ARITHMETIC, NOT OPAQUE NUMBERS
    //  --------------------------------------------------------------------------------------------
    //  The oracle does not write these as literals. It writes them as arithmetic over their base code -
    //  `SQLITE_IOERR_READ = (SQLITE_IOERR + (1*256))` [retcode.sru:L143] - and the relationship is
    //  itself part of the contract: SQLite's own convention is that the low byte of an extended code IS
    //  the primary code, which is what lets a caller who understands only primary codes mask an extended
    //  one down and still classify it correctly. A transcription that flattened these to literals would
    //  hold the same numbers today and lose the invariant that keeps them correct tomorrow.
    //
    //  So each row carries the BASE NAME and the MULTIPLIER, never the product, and the expected value is
    //  computed from whatever the base resolves to. The multiplier is structural - transcribed from the
    //  oracle's own arithmetic - and the radix is a single named constant. Both sides are checked, which
    //  additionally proves the WIRE preserved the arithmetic even though it must spell the results out as
    //  literals, protobuf having no constant expressions.
    //
    //  All 67 extended codes appear, one row each, matching retcode.sru:L140-L206 line for line. The
    //  table is long on purpose: one row per fact means one drift cannot mask another, and a
    //  representative sample would leave 60 codes unchecked.
    // ==============================================================================================

    /// <summary>
    /// Every SQLite extended result code as (extended name, base name, multiplier) [retcode.sru:L140-L206].
    /// </summary>
    public static TheoryData<string, string, long> SqliteExtendedCodeRows
    {
        get
        {
            TheoryData<string, string, long> rows = [];

            // Hanging off SQLITE_ERROR [retcode.sru:L140-L142]
            rows.Add("SQLITE_ERROR_MISSING_COLLSEQ", "SQLITE_ERROR", 1L);
            rows.Add("SQLITE_ERROR_RETRY", "SQLITE_ERROR", 2L);
            rows.Add("SQLITE_ERROR_SNAPSHOT", "SQLITE_ERROR", 3L);

            // Hanging off SQLITE_IOERR - the densest family, 31 consecutive multipliers
            // [retcode.sru:L143-L173]
            rows.Add("SQLITE_IOERR_READ", "SQLITE_IOERR", 1L);
            rows.Add("SQLITE_IOERR_SHORT_READ", "SQLITE_IOERR", 2L);
            rows.Add("SQLITE_IOERR_WRITE", "SQLITE_IOERR", 3L);
            rows.Add("SQLITE_IOERR_FSYNC", "SQLITE_IOERR", 4L);
            rows.Add("SQLITE_IOERR_DIR_FSYNC", "SQLITE_IOERR", 5L);
            rows.Add("SQLITE_IOERR_TRUNCATE", "SQLITE_IOERR", 6L);
            rows.Add("SQLITE_IOERR_FSTAT", "SQLITE_IOERR", 7L);
            rows.Add("SQLITE_IOERR_UNLOCK", "SQLITE_IOERR", 8L);
            rows.Add("SQLITE_IOERR_RDLOCK", "SQLITE_IOERR", 9L);
            rows.Add("SQLITE_IOERR_DELETE", "SQLITE_IOERR", 10L);
            rows.Add("SQLITE_IOERR_BLOCKED", "SQLITE_IOERR", 11L);
            rows.Add("SQLITE_IOERR_NOMEM", "SQLITE_IOERR", 12L);
            rows.Add("SQLITE_IOERR_ACCESS", "SQLITE_IOERR", 13L);
            rows.Add("SQLITE_IOERR_CHECKRESERVEDLOCK", "SQLITE_IOERR", 14L);
            rows.Add("SQLITE_IOERR_LOCK", "SQLITE_IOERR", 15L);
            rows.Add("SQLITE_IOERR_CLOSE", "SQLITE_IOERR", 16L);
            rows.Add("SQLITE_IOERR_DIR_CLOSE", "SQLITE_IOERR", 17L);
            rows.Add("SQLITE_IOERR_SHMOPEN", "SQLITE_IOERR", 18L);
            rows.Add("SQLITE_IOERR_SHMSIZE", "SQLITE_IOERR", 19L);
            rows.Add("SQLITE_IOERR_SHMLOCK", "SQLITE_IOERR", 20L);
            rows.Add("SQLITE_IOERR_SHMMAP", "SQLITE_IOERR", 21L);
            rows.Add("SQLITE_IOERR_SEEK", "SQLITE_IOERR", 22L);
            rows.Add("SQLITE_IOERR_DELETE_NOENT", "SQLITE_IOERR", 23L);
            rows.Add("SQLITE_IOERR_MMAP", "SQLITE_IOERR", 24L);
            rows.Add("SQLITE_IOERR_GETTEMPPATH", "SQLITE_IOERR", 25L);
            rows.Add("SQLITE_IOERR_CONVPATH", "SQLITE_IOERR", 26L);
            rows.Add("SQLITE_IOERR_VNODE", "SQLITE_IOERR", 27L);
            rows.Add("SQLITE_IOERR_AUTH", "SQLITE_IOERR", 28L);
            rows.Add("SQLITE_IOERR_BEGIN_ATOMIC", "SQLITE_IOERR", 29L);
            rows.Add("SQLITE_IOERR_COMMIT_ATOMIC", "SQLITE_IOERR", 30L);
            rows.Add("SQLITE_IOERR_ROLLBACK_ATOMIC", "SQLITE_IOERR", 31L);

            // Hanging off SQLITE_LOCKED [retcode.sru:L174-L175]
            rows.Add("SQLITE_LOCKED_SHAREDCACHE", "SQLITE_LOCKED", 1L);
            rows.Add("SQLITE_LOCKED_VTAB", "SQLITE_LOCKED", 2L);

            // Hanging off SQLITE_BUSY [retcode.sru:L176-L177]
            rows.Add("SQLITE_BUSY_RECOVERY", "SQLITE_BUSY", 1L);
            rows.Add("SQLITE_BUSY_SNAPSHOT", "SQLITE_BUSY", 2L);

            // Hanging off SQLITE_CANTOPEN [retcode.sru:L178-L182]. The oracle annotates the fifth
            // "/* Not Used */" and it is carried anyway - an unused code is still a declared code, and
            // dropping it would renumber nothing but would break the value-for-value agreement.
            rows.Add("SQLITE_CANTOPEN_NOTEMPDIR", "SQLITE_CANTOPEN", 1L);
            rows.Add("SQLITE_CANTOPEN_ISDIR", "SQLITE_CANTOPEN", 2L);
            rows.Add("SQLITE_CANTOPEN_FULLPATH", "SQLITE_CANTOPEN", 3L);
            rows.Add("SQLITE_CANTOPEN_CONVPATH", "SQLITE_CANTOPEN", 4L);
            rows.Add("SQLITE_CANTOPEN_DIRTYWAL", "SQLITE_CANTOPEN", 5L);

            // Hanging off SQLITE_CORRUPT [retcode.sru:L183-L184]
            rows.Add("SQLITE_CORRUPT_VTAB", "SQLITE_CORRUPT", 1L);
            rows.Add("SQLITE_CORRUPT_SEQUENCE", "SQLITE_CORRUPT", 2L);

            // Hanging off SQLITE_READONLY [retcode.sru:L185-L190]
            rows.Add("SQLITE_READONLY_RECOVERY", "SQLITE_READONLY", 1L);
            rows.Add("SQLITE_READONLY_CANTLOCK", "SQLITE_READONLY", 2L);
            rows.Add("SQLITE_READONLY_ROLLBACK", "SQLITE_READONLY", 3L);
            rows.Add("SQLITE_READONLY_DBMOVED", "SQLITE_READONLY", 4L);
            rows.Add("SQLITE_READONLY_CANTINIT", "SQLITE_READONLY", 5L);
            rows.Add("SQLITE_READONLY_DIRECTORY", "SQLITE_READONLY", 6L);

            // THE ANOMALY. Multiplier 2 with no multiplier-1 sibling anywhere [retcode.sru:L191]. It is
            // asserted as it stands and NOT "completed" with a fabricated sibling - see
            // TheAbortFamilyHasOnlyTheMultiplierTwoExtendedCode below.
            rows.Add("SQLITE_ABORT_ROLLBACK", "SQLITE_ABORT", 2L);

            // Hanging off SQLITE_CONSTRAINT [retcode.sru:L192-L201]
            rows.Add("SQLITE_CONSTRAINT_CHECK", "SQLITE_CONSTRAINT", 1L);
            rows.Add("SQLITE_CONSTRAINT_COMMITHOOK", "SQLITE_CONSTRAINT", 2L);
            rows.Add("SQLITE_CONSTRAINT_FOREIGNKEY", "SQLITE_CONSTRAINT", 3L);
            rows.Add("SQLITE_CONSTRAINT_FUNCTION", "SQLITE_CONSTRAINT", 4L);
            rows.Add("SQLITE_CONSTRAINT_NOTNULL", "SQLITE_CONSTRAINT", 5L);
            rows.Add("SQLITE_CONSTRAINT_PRIMARYKEY", "SQLITE_CONSTRAINT", 6L);
            rows.Add("SQLITE_CONSTRAINT_TRIGGER", "SQLITE_CONSTRAINT", 7L);
            rows.Add("SQLITE_CONSTRAINT_UNIQUE", "SQLITE_CONSTRAINT", 8L);
            rows.Add("SQLITE_CONSTRAINT_VTAB", "SQLITE_CONSTRAINT", 9L);
            rows.Add("SQLITE_CONSTRAINT_ROWID", "SQLITE_CONSTRAINT", 10L);

            // Hanging off SQLITE_NOTICE, SQLITE_WARNING and SQLITE_AUTH [retcode.sru:L202-L205]
            rows.Add("SQLITE_NOTICE_RECOVER_WAL", "SQLITE_NOTICE", 1L);
            rows.Add("SQLITE_NOTICE_RECOVER_ROLLBACK", "SQLITE_NOTICE", 2L);
            rows.Add("SQLITE_WARNING_AUTOINDEX", "SQLITE_WARNING", 1L);
            rows.Add("SQLITE_AUTH_USER", "SQLITE_AUTH", 1L);

            // The second anomaly, and it is a different one: an EXTENDED code hanging off the SUCCESS
            // base, so a non-zero value whose primary code is 0 [retcode.sru:L206]. Preserved as such.
            rows.Add("SQLITE_OK_LOAD_PERMANENTLY", "SQLITE_OK", 1L);

            return rows;
        }
    }

    /// <summary>
    /// An extended SQLite code equals its base code plus its multiplier times 256, on both sides.
    /// </summary>
    /// <param name="extendedName">The extended code's authored name.</param>
    /// <param name="baseName">The primary code it hangs off.</param>
    /// <param name="multiplier">The multiplier the oracle writes in the arithmetic.</param>
    [Theory]
    [MemberData(nameof(SqliteExtendedCodeRows))]
    public void EverySqliteExtendedCodeIsItsBasePlusAMultipleOfTheRadix(
        string extendedName,
        string baseName,
        long multiplier)
    {
        Dictionary<string, long> kernel = KernelConstants();
        Dictionary<string, long> wire = WireValues();

        Assert.True(kernel.TryGetValue(baseName, out long kernelBase), $"The kernel lacks '{baseName}'.");
        Assert.True(
            kernel.TryGetValue(extendedName, out long kernelExtended),
            $"The kernel lacks '{extendedName}'.");
        Assert.True(wire.TryGetValue(baseName, out long wireBase), $"The wire lacks '{baseName}'.");
        Assert.True(
            wire.TryGetValue(extendedName, out long wireExtended),
            $"The wire lacks '{extendedName}'.");

        long expectedFromKernel = kernelBase + (multiplier * SqliteExtendedRadix);
        long expectedFromWire = wireBase + (multiplier * SqliteExtendedRadix);

        Assert.Equal(expectedFromKernel, kernelExtended);
        Assert.Equal(expectedFromWire, wireExtended);
    }

    /// <summary>
    /// The abort family carries only the multiplier-two extended code, with no multiplier-one sibling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SQLITE_ABORT_ROLLBACK = (SQLITE_ABORT + (2*256))</c> [retcode.sru:L191] is the one extended code
    /// in the catalogue whose multiplier sequence does not begin at 1. Every other base that has extended
    /// codes at all starts at 1 and runs consecutively.
    /// </para>
    /// <para>
    /// It is asserted AS AN ANOMALY rather than completed. Inventing a multiplier-one sibling to make the
    /// sequence tidy would add a constant the oracle does not declare and that nothing upstream emits -
    /// a behavioural addition, which constraint C-B forbids as squarely as it forbids a correction. The
    /// gap is upstream SQLite's, faithfully carried.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheAbortFamilyHasOnlyTheMultiplierTwoExtendedCode()
    {
        Dictionary<string, long> kernel = KernelConstants();

        IReadOnlyList<string> abortExtended =
        [
            .. SqliteFamilyNames().Where(static name =>
                name.StartsWith("SQLITE_ABORT_", StringComparison.Ordinal)),
        ];

        string only = Assert.Single(abortExtended);
        Assert.Equal("SQLITE_ABORT_ROLLBACK", only);

        long multiplierOneValue = kernel["SQLITE_ABORT"] + (1L * SqliteExtendedRadix);

        IReadOnlyList<string> claimingMultiplierOne =
        [
            .. SqliteFamilyNames().Where(name => kernel[name] == multiplierOneValue),
        ];

        Assert.Empty(claimingMultiplierOne);
    }

    // ==============================================================================================
    //  DwBuffer AND ItemStatus - WHERE THE KERNEL CANNOT BE THE ORACLE, AND WHY THAT IS SAID OUT LOUD
    //  --------------------------------------------------------------------------------------------
    //  Everything above reads its expectation from PowerFramework.Shared.Kernel.RetCode, which is the
    //  preferred shape because it makes the expectation drift automatically with the code it guards.
    //  These last two alphabets cannot be checked that way, and the honest limit is worth more here than
    //  a fabricated lookup would be.
    //
    //  WHY. `dwbuffer` and `dwItemStatus` are POWERBUILDER BUILT-IN ENUMERATED TYPES. The legacy code
    //  writes their members as bare literals with the language's trailing-bang syntax - Primary!,
    //  Filter!, DataModified! - and never declares them, so there is nothing in the legacy sources to
    //  transcribe into a constant. They are verified ABSENT from
    //  ws_objects/pfw.shared.pbl.src/enums.sru: a search of that object for buffer or item-status
    //  constants returns only the unrelated WS_E_NO_OUTGOING_BUFFERS and WS_E_NO_INCOMING_BUFFERS, which
    //  are WebSocket error codes. The kernel therefore correctly declares no counterpart, and asserting
    //  one into existence would be inventing an expectation rather than reading it.
    //
    //  WHY NOT REACH FOR THE MANAGED COUNTERPART INSTEAD. It exists - the Persistence service holds
    //  Buffers/DataWindowBuffers.cs and Buffers/ItemStatus.cs - and referencing it is exactly what
    //  constraint C-A forbids: PowerFramework.Contracts is the ONLY permitted cross-boundary coupling, so
    //  a shared test project that reached into a service to borrow an expectation would create the
    //  coupling the architecture is built to prevent, in the one project nobody audits for it. This file
    //  references the contracts project and the shared kernel, and nothing else.
    //
    //  WHAT IS ASSERTED INSTEAD. The membership the legacy actually EVIDENCES, with every locator below
    //  verified by inspection. Note that the frequently-cited attribution to
    //  n_cst_thread_task_sqlbase_ds.sru is wrong - that object contains none of this vocabulary - and the
    //  real sites are named per row.
    //
    //  WHAT IS DELIBERATELY NOT ASSERTED, AND THIS IS THE SUBTLE PART. Not the numbers, and not the
    //  member count. proto3 requires an enum's first value to be zero, and a contract author may
    //  legitimately satisfy that with a synthetic *_UNSPECIFIED = 0 sentinel where the legacy domain
    //  holds no natural zero. A sentinel shifts every real member up by one, so an absolute-value or
    //  exact-count assertion here would fail against a legitimate contract - it would be a demand about
    //  the contract's INTERNAL NUMBERING dressed up as a fidelity check. What is asserted is what
    //  survives either choice: the evidenced members are present, they are distinct-valued, and they are
    //  ordered as the legacy type declares them.
    //
    //  (For the record, the layout found makes no sentinel necessary in either enum: common.v1.proto
    //  documents that PowerBuilder initialises an unassigned `dwbuffer` to Primary! and an unassigned
    //  `dwItemStatus` to NotModified!, so each domain holds its own natural zero and expresses "not
    //  supplied" through proto3 field presence instead. The tests below pass under that layout and would
    //  keep passing if a sentinel were ever added.)
    // ==============================================================================================

    /// <summary>
    /// The three DataWindow buffers the legacy sources evidence, in the built-in type's declared order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The wire spellings carry a <c>DW_BUFFER_</c> prefix because these are FILE-LEVEL proto enums, whose
    /// members share the enclosing package scope - two file-level enums each declaring a bare
    /// <c>PRIMARY</c> would collide at the package level, which protoc rejects. The prefix is a
    /// namespacing requirement of the wire format rather than a rename of a legacy identifier, and it is
    /// why these names are not expected to appear in the kernel catalogue.
    /// </para>
    /// <para>
    /// Verified locators: <c>ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs:L7</c> declares the field
    /// <c>dwbuffer buffer</c>, and <c>n_cst_thread_task_sqlbase.sru:L34</c> and <c>:L85</c> declare the
    /// <c>ondberror</c> event with a <c>dwbuffer buffer</c> parameter - the type in type position.
    /// Members in use: <c>n_cst_thread_task_sqlbase_ds_mt.sru:L32</c> tests
    /// <c>buffer = Primary! or buffer = Filter!</c> and <c>:L56</c> reads
    /// <c>GetItemStatus(nRow,0,Delete!)</c>, which is the sole <c>Delete!</c> site in scope;
    /// <c>n_cst_thread_task_sqlquery.sru:L123</c> and <c>:L127</c> set item status on <c>Primary!</c> and
    /// <c>Filter!</c>; <c>n_cst_thread_task_sqlupdate.sru:L230</c> and <c>:L238</c> read the same two
    /// buffers, the second inside a loop that runs BACKWARDS [<c>:L237</c>] because the filter buffer's
    /// row order is inverted relative to the source.
    /// </para>
    /// </remarks>
    public static TheoryData<string> EvidencedBufferMembers =>
    [
        "DW_BUFFER_PRIMARY", // Primary! - the live rows, and the state of an unassigned dwbuffer
        "DW_BUFFER_DELETE",  // Delete!  - rows deleted but not yet flushed
        "DW_BUFFER_FILTER",  // Filter!  - filtered-out rows, row order inverted
    ];

    /// <summary>
    /// The four DataWindow item statuses, which is the built-in type's whole domain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FOUR rather than three, and the distinction is between a TYPE'S DOMAIN and the subset in-scope code
    /// happens to name. Verified locators for the three that are named:
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L57</c> reads
    /// <c>case NotModified!,DataModified!</c>; <c>n_cst_thread_task_sqlquery.sru:L123</c> and <c>:L127</c>
    /// set <c>DataModified!</c>; <c>n_cst_thread_task_sqlupdate.sru:L230</c> and <c>:L238</c> compare
    /// against <c>NewModified!</c>.
    /// </para>
    /// <para>
    /// Bare <c>New!</c> was searched for and NEVER appears literally anywhere in the in-scope sources. It
    /// is carried regardless, because it is a legal runtime status the DataWindow engine assigns to a row
    /// inserted but not yet edited, entirely without any legacy line naming it. A three-member alphabet
    /// would make some real rows UNREPRESENTABLE on the wire and force a service to invent a substitute
    /// status or drop the row. Carrying the fourth is faithfulness to the legacy type, not an added
    /// capability - no operation, no field and no behaviour accompanies it, only the ability to say it.
    /// </para>
    /// <para>
    /// Worth knowing when reading those locators: the legacy reads a ROW's status by asking for COLUMN
    /// INDEX ZERO - <c>GetItemStatus(nRow,0,Primary!)</c> - where 0 means "the row itself" rather than the
    /// first column, and <c>n_cst_thread_task_sqlupdate.sru:L162</c> reads a genuine per-column status
    /// with a real index two lines away. Both readings coexist and only the index distinguishes them.
    /// </para>
    /// </remarks>
    public static TheoryData<string> EvidencedItemStatusMembers =>
    [
        "ITEM_STATUS_NOT_MODIFIED",  // NotModified!  - existing row, unchanged
        "ITEM_STATUS_DATA_MODIFIED", // DataModified! - existing row, edited
        "ITEM_STATUS_NEW",           // New!          - inserted, not yet edited; never named in scope
        "ITEM_STATUS_NEW_MODIFIED",  // NewModified!  - inserted and edited
    ];

    /// <summary>
    /// An evidenced DataWindow buffer is declared by the wire's <c>DwBuffer</c> enum.
    /// </summary>
    /// <param name="memberName">The authored proto spelling of the buffer.</param>
    /// <remarks>
    /// Membership only - no number is asserted, because a legitimate proto3 zero sentinel would shift
    /// every member and an absolute-value assertion would then fail against a correct contract. The
    /// expectation cannot be read from the kernel here: <c>dwbuffer</c> is a PowerBuilder built-in
    /// enumerated type, verified absent from <c>ws_objects/pfw.shared.pbl.src/enums.sru</c>, and the
    /// managed counterpart lives in the Persistence service, which constraint C-A forbids this project
    /// from referencing. See the section comment above for the full reasoning.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EvidencedBufferMembers))]
    public void EveryEvidencedBufferMemberIsDeclaredOnTheWire(string memberName)
    {
        EnumDescriptor buffers = ContractDescriptors.RequireEnum(WireBufferEnumName);

        Assert.Contains(memberName, buffers.Values.Select(static value => value.Name), StringComparer.Ordinal);
    }

    /// <summary>
    /// An evidenced DataWindow item status is declared by the wire's <c>ItemStatus</c> enum.
    /// </summary>
    /// <param name="memberName">The authored proto spelling of the status.</param>
    /// <remarks>
    /// Membership only, for the same reason as the buffer test above, and with the same limit on the
    /// oracle: <c>dwItemStatus</c> is a PowerBuilder built-in enumerated type with no legacy constant
    /// declaration to transcribe, so the evidenced membership is asserted instead of a kernel lookup that
    /// would have to be fabricated to exist.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EvidencedItemStatusMembers))]
    public void EveryEvidencedItemStatusMemberIsDeclaredOnTheWire(string memberName)
    {
        EnumDescriptor statuses = ContractDescriptors.RequireEnum(WireItemStatusEnumName);

        Assert.Contains(memberName, statuses.Values.Select(static value => value.Name), StringComparer.Ordinal);
    }

    /// <summary>
    /// The buffer and item-status alphabets are distinct-valued - unlike the return codes, they alias
    /// nothing.
    /// </summary>
    /// <param name="enumName">The wire enum to inspect.</param>
    /// <remarks>
    /// The return-code family aliases three names onto 0 and two onto -2 and that is correct there,
    /// because the oracle declares those spellings. Nothing in the legacy evidences an alias in either of
    /// these two domains, so an alias appearing here would be a genuine defect: a payload could not say
    /// which member it meant, and a round trip through the generated CLR enum would not necessarily
    /// return the spelling it started from.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SingleValuedEnumNames))]
    public void TheBufferAndItemStatusAlphabetsAreDistinctValued(string enumName)
    {
        EnumDescriptor alphabet = ContractDescriptors.RequireEnum(enumName);

        IReadOnlyList<string> aliased =
        [
            .. alphabet.Values
                .GroupBy(static value => value.Number)
                .Where(static group => group.Count() > 1)
                .Select(static group =>
                    $"{group.Key} is carried by {string.Join(" and ", group.Select(static value => value.Name))}")
                .OrderBy(static description => description, StringComparer.Ordinal),
        ];

        Assert.Empty(aliased);
    }

    /// <summary>The two wire alphabets that must not alias.</summary>
    public static TheoryData<string> SingleValuedEnumNames =>
    [
        WireBufferEnumName,
        WireItemStatusEnumName,
    ];

    /// <summary>
    /// The evidenced members appear in the order the PowerBuilder built-in type declares them.
    /// </summary>
    /// <param name="enumName">The wire enum to inspect.</param>
    /// <param name="expectedOrder">The member names in the legacy declaration order.</param>
    /// <remarks>
    /// Relative order rather than absolute values, deliberately: ordering is the strongest property that
    /// survives the insertion of a proto3 zero sentinel, which would shift every number but reorder
    /// nothing. It is worth pinning because these alphabets are the ones most likely to be bridged to a
    /// legacy ordinal by a numeric cast, and a reordering would make such a cast silently read one state
    /// as another - the worst available failure shape, since nothing fails to compile.
    /// </remarks>
    [Theory]
    [MemberData(nameof(OrderedAlphabets))]
    public void TheEvidencedMembersKeepTheirLegacyDeclarationOrder(string enumName, string[] expectedOrder)
    {
        EnumDescriptor alphabet = ContractDescriptors.RequireEnum(enumName);

        IReadOnlyList<string> evidencedInNumericOrder =
        [
            .. alphabet.Values
                .Where(value => expectedOrder.Contains(value.Name, StringComparer.Ordinal))
                .OrderBy(static value => value.Number)
                .Select(static value => value.Name),
        ];

        Assert.Equal(expectedOrder, evidencedInNumericOrder);
    }

    /// <summary>
    /// Each single-valued alphabet with its members in the legacy built-in type's declaration order.
    /// </summary>
    public static TheoryData<string, string[]> OrderedAlphabets
    {
        get
        {
            TheoryData<string, string[]> rows = [];

            // PowerBuilder's dwbuffer declares Primary!, Delete!, Filter! in that order.
            rows.Add(
                WireBufferEnumName,
                ["DW_BUFFER_PRIMARY", "DW_BUFFER_DELETE", "DW_BUFFER_FILTER"]);

            // PowerBuilder's dwItemStatus declares NotModified!, DataModified!, New!, NewModified!.
            rows.Add(
                WireItemStatusEnumName,
                [
                    "ITEM_STATUS_NOT_MODIFIED",
                    "ITEM_STATUS_DATA_MODIFIED",
                    "ITEM_STATUS_NEW",
                    "ITEM_STATUS_NEW_MODIFIED",
                ]);

            return rows;
        }
    }

    /// <summary>
    /// The kernel really does declare no buffer or item-status constant - so the fallback above is still
    /// the correct reading, and this test says so if that ever changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The preference is unambiguous: expectations should be read from the kernel wherever the kernel can
    /// serve as oracle. The two tests above depart from that only because it cannot, and that premise is
    /// a fact about another file rather than something this one controls - so it is CHECKED rather than
    /// assumed. If a buffer or item-status constant is ever added to
    /// <see cref="KernelRetCode"/>, this test fails and the failure message says what to do about it,
    /// instead of the fallback quietly persisting after its justification has expired.
    /// </para>
    /// <para>
    /// Scoped to <see cref="KernelRetCode"/>, which is the only kernel type this file declares a
    /// dependency on. <c>PowerFramework.Shared.Kernel.Enums</c> was inspected during discovery and
    /// likewise declares no member of either domain - its only buffer-shaped names are the WebSocket codes
    /// <c>WS_E_NO_OUTGOING_BUFFERS</c> and <c>WS_E_NO_INCOMING_BUFFERS</c> - but it is deliberately not
    /// referenced here, because widening this file's dependency surface to reach a negative result would
    /// cost more than the result is worth.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheKernelStillDeclaresNoBufferOrItemStatusConstant()
    {
        Dictionary<string, long> kernel = KernelConstants();

        IReadOnlyList<string> unexpected =
        [
            .. ExpectedWireNamesWithoutAKernelConstant
                .Where(kernel.ContainsKey)
                .OrderBy(static name => name, StringComparer.Ordinal),
        ];

        Assert.True(
            unexpected.Count == 0,
            $"PowerFramework.Shared.Kernel.RetCode now declares {string.Join(", ", unexpected)}. That "
                + "invalidates the premise of EveryEvidencedBufferMemberIsDeclaredOnTheWire and "
                + "EveryEvidencedItemStatusMemberIsDeclaredOnTheWire, which assert evidenced MEMBERSHIP "
                + "precisely because no kernel constant existed to compare numbers against. Those two "
                + "tests should now read their expectations from the kernel, as every other test in this "
                + "file does, and this guard should be removed with them.");
    }
}
