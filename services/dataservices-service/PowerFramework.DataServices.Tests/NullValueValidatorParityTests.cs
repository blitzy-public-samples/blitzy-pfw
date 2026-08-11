// ==================================================================================================
//  NullValueValidatorParityTests - PARITY MATRIX FAMILY 4 OF 4: THE VALIDATORS
// ==================================================================================================
//
//  UNITS UNDER TEST
//      Validators/DateValidator.cs        <- ws_objects/pfw.datawindow.services.pbl.src/dwnvldate.srf
//      Validators/DateTimeValidator.cs    <- .../dwnvldatetime.srf
//      Validators/NumberValidator.cs      <- .../dwnvlnumber.srf
//      Validators/StringValidator.cs      <- .../dwnvlstring.srf
//      Validators/TimeValidator.cs        <- .../dwnvltime.srf
//      plus the SIX-ARM COERCION TABLE those five files own between them, from
//      .../se_cst_dw.sru:L231-L244, and its single consumer Domain/ItemChangeProtocol.cs.
//
//  `Validators/ValueToExpression.cs` is covered by ValueToExpressionTests.cs and is touched here for
//  exactly one purpose: to prove that the five null sentinels and the three temporal format strings
//  are SINGLE SOURCED from the owning validator rather than duplicated (see REGION 6). A duplicated
//  format string is precisely how the emitted `Date('...')` text drifts away from what `Coerce`
//  accepts, and no test that looks only at one side of that pair can catch it.
//
//  USER RULES: `review_rules` reports "No user rules provided." - VERIFIED, and the document is that
//  one line in full. No user rule governs this file, none is invented, and the enterprise baseline of
//  AAP 0.7.2 applies in its place: nullable clean, warnings as errors, plain assertions, no secret,
//  and a hard per-service coverage gate.
//
//  GOVERNING AAP 0.7.3 CONSTRAINTS, AND HOW THIS FILE HONOURS EACH
//    C-B / G2  The emitted spellings, the declared return types and the token sets are LEGACY
//              BEHAVIOUR, not design choices. Every one of them is asserted LITERALLY against the
//              oracle text, never against a normalised or "tidied" form. Where the legacy is
//              inconsistent - and it is, in four separate places recorded in REGION 0 - the
//              inconsistency is pinned as behaviour rather than harmonised.
//    C-C       The five `.srf` files and `se_cst_dw.sru` are READ-ONLY. They are quoted here and
//              never touched. Nothing in this file writes to `ws_objects/**`.
//    0.5.3     `FluentValidation` is deliberately absent from the dependency inventory because these
//              five classes are PORTED LOGIC WITH EXACT LEGACY SEMANTICS, not a rule set. Every
//              assertion below therefore pins an EXACT OUTPUT - a literal string, a specific carrier
//              type, a specific return code - and never a notion of "validity". Assertions are plain
//              xunit `Assert`; no assertion library exists in the inventory and none is introduced.
//    C-K       Every behavioural claim carries its locator, in the form `file:Lnnn`.
//    0.4.5.3   SCREAMING_SNAKE constant identifiers are REFERENCED (`RetCode.OK`,
//              `RetCode.E_INVALID_DATA`) and NONE IS DECLARED here. That matters mechanically and not
//              only stylistically: `.editorconfig` suppresses CA1707 for the specific files on its
//              BAND 3 roster - the single source of truth for that list - that
//              declare preserved identifiers, this file is not one of them, and CA1707 reports
//              declarations only - so a declaration here would break the build under
//              TreatWarningsAsErrors while a reference cannot.
//    0.6.7     EVERY test below is a `[Theory]` driven by `[MemberData]`, one row per token and per
//              type. There is no `[Fact]` in this file, and that is a deliberate structural choice:
//              a parity matrix that is expressible as a table is what the characterization model
//              consumes, and a row that fails names the exact token or type that regressed.
//
// --------------------------------------------------------------------------------------------------
//  WHAT THE ORACLE ACTUALLY SAYS - READ IN FULL, QUOTED VERBATIM
// --------------------------------------------------------------------------------------------------
//
//  All five null producers have the same three-line body: declare a local of the declared return
//  type, null it, return it. The DECLARED RETURN TYPE is the only thing that differs, and it is the
//  thing that matters, because it is the type the DataWindow expression engine sees.
//
//      dwnvldate.srf:L6      global function date dwnvldate ()
//      dwnvldate.srf:L9-L11  date nvl / SetNull(nvl) / return nvl
//      dwnvldatetime.srf:L6  global function datetime dwnvldatetime ()
//      dwnvlnumber.srf:L6    global function integer dwnvlnumber ()      <-- integer. NOT decimal.
//      dwnvlnumber.srf:L9    int nvl                                          NOT long.
//      dwnvlstring.srf:L6    global function string dwnvlstring ()
//      dwnvltime.srf:L6      global function time dwnvltime ()
//
//  The EMITTED TEXT is not in those files at all. It is in the caller, and it is CAMEL CASED while
//  every one of the five PowerBuilder function objects is spelled entirely in lower case:
//
//      dwvaluetoexp.srf:L19  if IsNull(sVal) then sVal = "dwNvlNumber()"     decimal overload
//      dwvaluetoexp.srf:L25  if IsNull(sVal) then sVal = "dwNvlNumber()"     int     overload
//      dwvaluetoexp.srf:L31  if IsNull(sVal) then sVal = "dwNvlNumber()"     long    overload
//      dwvaluetoexp.srf:L37  if IsNull(sVal) then sVal = "dwNvlNumber()"     double  overload
//      dwvaluetoexp.srf:L43  if IsNull(sVal) then sVal = "dwNvlNumber()"     real    overload
//      dwvaluetoexp.srf:L49  if IsNull(sVal) then sVal = "dwNvlString()"
//      dwvaluetoexp.srf:L55  if IsNull(sVal) then sVal = "dwNvlDateTime()"
//      dwvaluetoexp.srf:L61  if IsNull(sVal) then sVal = "dwNvlDate()"
//      dwvaluetoexp.srf:L67  if IsNull(sVal) then sVal = "dwNvlTime()"
//
//  PowerBuilder resolves function names case-insensitively, so the two spellings name one function
//  and the legacy could not tell them apart. THE PORT CAN, and that is why the casing is asserted
//  rather than assumed: the emitted string is handed to a DataWindow expression parser as text, it is
//  what a characterization recording stores, and it is what a `Describe("Evaluate(...)")` substitute
//  has to register as callable. Normalising it to the `.srf` file spelling would be invisible in
//  every unit test that only round-trips the constant against itself, and would break the moment the
//  text met the evaluator or a stored recording. REGION 1 therefore asserts, per sentinel, that the
//  emitted text equals the camel-cased oracle literal byte for byte AND that it does NOT equal the
//  lower-cased `.srf` object spelling.
//
//  THE COERCION TABLE, VERBATIM FROM se_cst_dw.sru. Six arms, ten tokens, NO `case else`:
//
//      L231  choose case Left(dwo.ColType,5)
//      L232    case "char","char("
//      L233      SetItem(row,Long(dwo.ID),data)                <-- RAW. No conversion function.
//      L234    case "decim","real","numbe"
//      L235      SetItem(row,Long(dwo.ID),Dec(data))
//      L236    case "long","ulong"
//      L237      SetItem(row,Long(dwo.ID),Long(data))
//      L238    case "datet"
//      L239      SetItem(row,Long(dwo.ID),DateTime(data))
//      L240    case "date"
//      L241      SetItem(row,Long(dwo.ID),Date(data))
//      L242    case "time"
//      L243      SetItem(row,Long(dwo.ID),Time(data))
//      L244  end choose
//
//  Three properties of that construct are load bearing and each has its own region below.
//
//    (1) `Left(s,5)` IS NOT `Substring(0,5)`. PowerScript's `Left` of a string shorter than the
//        requested length returns the string UNCHANGED; `Substring(0,5)` THROWS. Five of the ten
//        tokens - "char", "date", "time", "real", "long" - are four characters long, so a naive
//        port throws on the majority of the table. REGION 4 asserts length safety per token.
//
//    (2) `choose case` COMPARES FOR EQUALITY, so the six arms are MUTUALLY EXCLUSIVE. The trap is
//        `"datetime"`, which truncates to `"datet"` and is therefore owned by the DateTime arm even
//        though the raw text begins with `"date"`. A port written with `StartsWith` sends every
//        datetime column down the date arm and SILENTLY DISCARDS THE TIME COMPONENT. REGION 4
//        asserts sole ownership for every token and for a set of near misses, and no expectation in
//        this file is expressed as a prefix test.
//
//    (3) THERE IS NO `case else`. A column type matching none of the ten tokens is NOT WRITTEN AT
//        ALL: no conversion runs, no value is stored, nothing is reported and nothing throws. That
//        is behaviour, not an omission, and REGION 5 asserts it end to end through
//        `ItemChangeProtocol` by showing that `"timestamp"` produces NO `SetItem` call whatsoever.
//
//  THE EMPTY STRING IS A RESERVED SENTINEL, WHICH IS WHY REGION 1 GUARDS AGAINST IT. In the column
//  expression engine an empty result means ABORT THE WHOLE CALCULATION:
//
//      n_cst_dwsvc_columnexp.sru:L2183   if sVal = "" then return ""
//      n_cst_dwsvc_columnexp.sru:L2186   if sVal = "" then return ""
//      n_cst_dwsvc_columnexp.sru:L2357   case else
//      n_cst_dwsvc_columnexp.sru:L2358     return ""
//
//  So a null producer that returned `""` would not merely emit wrong text - it would make every
//  expression containing a null value abort silently, with no error anywhere. `null` is equally
//  fatal for the opposite reason: it would concatenate into surrounding expression text as nothing
//  at all in PowerScript and as the empty string in C#.
//
// --------------------------------------------------------------------------------------------------
//  REGION 0 - THE FOUR LEGACY-FACING ASYMMETRIES THIS FILE PINS RATHER THAN HARMONISES
// --------------------------------------------------------------------------------------------------
//
//  The five ported validators do not present a uniform surface, and the differences are asserted
//  here as they stand. Anyone tempted to unify them should read this list first: three of the four
//  differences are REQUIRED by the oracle, and the fourth is a naming choice that is now observable
//  through this file.
//
//    A. `NumberValidator.NullNumber()` returns `short?`, not `int?`, `decimal?` or `long?`. PowerScript
//       `integer` is a 16-BIT SIGNED type, so `short` is the faithful carrier for the type
//       `dwnvlnumber.srf:L6` declares. This is the single most likely thing in the whole family to be
//       "helpfully" widened, because ALL FIVE numeric `dwvaluetoexp` overloads - decimal, int, long,
//       double and real - share this one 16-bit sentinel, so a reader who arrives from the decimal
//       overload sees a mismatch and reaches for `decimal?`. The mismatch is the legacy's, it is
//       recorded as DEFECT 2 in ValueToExpression.cs, and REGION 2 asserts the narrow type
//       explicitly, by reflection over the declared return type rather than over a value.
//
//    B. `DateTimeValidator.NullValue` is a PROPERTY; the other four expose METHODS
//       (`NullNumber()`, `StringValidator.NullValue()`, `DateValidator.NullValue()`,
//       `TimeValidator.NullValue()`). REGION 2 reads whichever member kind each validator publishes,
//       so the asymmetry is covered rather than tripped over.
//
//    C. `StringValidator.ColTypePrefix(null)` returns NULL, while `NumberValidator.ColumnTypePrefix`
//       and `DateValidator.TruncateColumnType` return `string.Empty`. Both reach the same observable
//       outcome - a null subject matches no arm - and the difference is asserted, not smoothed.
//
//    D. The five-character truncation length is published as `ColumnTypePrefixLength` by
//       NumberValidator and DateValidator, as `ColTypePrefixLength` by StringValidator, and NOT AT
//       ALL by DateTimeValidator and TimeValidator. REGION 4 asserts the value five wherever it is
//       declared, and asserts the BEHAVIOUR for all five regardless of whether a constant backs it.
//
// ==================================================================================================

using System.Globalization;
using System.Reflection;
using System.Text;

using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Validators;
using PowerFramework.Shared.Kernel;

using Xunit;

using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The validator parity matrix: the five typed-null producers of
/// <c>ws_objects/pfw.datawindow.services.pbl.src/dwnvl*.srf</c> and the six-arm column-type coercion
/// table of <c>se_cst_dw.sru:L231-L244</c> that those five files own between them.
/// </summary>
/// <remarks>
/// <para>
/// Every member is a <see cref="TheoryAttribute"/> fed by <see cref="MemberDataAttribute"/>, per
/// AAP 0.6.7. Rows carry the ORACLE side of each claim - the legacy locator, the legacy spelling, the
/// legacy declared type, the legacy token - and the implementation side is resolved inside the test
/// body from the validator under test. That direction is deliberate: a row that embedded the ported
/// constant would assert the constant against itself and pass no matter what the constant said.
/// </para>
/// <para>
/// Row payloads are restricted to strings, integers and booleans so that every row is serializable
/// and therefore individually addressable by the test runner. Where a row needs to select a CLR type
/// or a validator, it carries a short key string and the body resolves it through the switch helpers
/// at the foot of this class.
/// </para>
/// </remarks>
public sealed class NullValueValidatorParityTests
{
    // ----------------------------------------------------------------------------------------------
    //  VALIDATOR KEYS. Five validators, so five keys. Not an enum, because theory rows are more
    //  readable in a test report as text than as an enum member, and because an enum declared here
    //  would be a new public type in the test assembly for no gain.
    // ----------------------------------------------------------------------------------------------

    private const string DateKey = "Date";
    private const string DateTimeKey = "DateTime";
    private const string NumberKey = "Number";
    private const string StringKey = "String";
    private const string TimeKey = "Time";

    // ----------------------------------------------------------------------------------------------
    //  ARM KEYS. SIX arms across FIVE validators, because NumberValidator owns two of them: the
    //  `Dec()` arm at se_cst_dw.sru:L234-L235 and the `Long()` arm at :L236-L237. Any model with five
    //  arms has silently merged two conversions that the oracle keeps apart, and merging them would
    //  truncate the fractional part of every `number`, `decimal` and `real` column.
    // ----------------------------------------------------------------------------------------------

    private const string StringArm = "String";
    private const string NumberDecimalArm = "NumberDecimal";
    private const string NumberLongArm = "NumberLong";
    private const string DateTimeArm = "DateTime";
    private const string DateArm = "Date";
    private const string TimeArm = "Time";

    /// <summary>
    /// The sentinel used in a theory row to mean "no arm claims this column type", i.e. the absence
    /// of a <c>case else</c> at <c>se_cst_dw.sru:L244</c>.
    /// </summary>
    private const string NoArm = "(none)";

    /// <summary>
    /// The six arm keys in the oracle's declaration order, used by the sole-ownership matrices to
    /// prove that a column type claimed by one arm is rejected by the other five.
    /// </summary>
    private static readonly string[] AllArms =
    [
        StringArm,
        NumberDecimalArm,
        NumberLongArm,
        DateTimeArm,
        DateArm,
        TimeArm,
    ];

    // ==============================================================================================
    //  REGION 1 - THE EMITTED SPELLINGS, BYTE FOR BYTE
    //  Source: dwvaluetoexp.srf:L19, :L25, :L31, :L37, :L43, :L49, :L55, :L61, :L67
    // ==============================================================================================

    /// <summary>
    /// One row per null sentinel: the validator key, the legacy <c>.srf</c> file that declares the
    /// producer, the LOWER-CASE PowerBuilder object name in that file, the CAMEL-CASED function name
    /// the caller emits, and the complete emitted literal.
    /// </summary>
    /// <remarks>
    /// The third and fourth columns differ only in casing, and carrying both is the point: the test
    /// asserts equality against the emitted spelling and INEQUALITY against the declared spelling, so
    /// a port that normalised the text to the file name would fail rather than pass silently.
    /// </remarks>
    public static TheoryData<string, string, string, string, string> NullSentinelSpellings =>
        new()
        {
            { DateKey, "dwnvldate.srf:L6", "dwnvldate", "dwNvlDate", "dwNvlDate()" },
            { DateTimeKey, "dwnvldatetime.srf:L6", "dwnvldatetime", "dwNvlDateTime", "dwNvlDateTime()" },
            { NumberKey, "dwnvlnumber.srf:L6", "dwnvlnumber", "dwNvlNumber", "dwNvlNumber()" },
            { StringKey, "dwnvlstring.srf:L6", "dwnvlstring", "dwNvlString", "dwNvlString()" },
            { TimeKey, "dwnvltime.srf:L6", "dwnvltime", "dwNvlTime", "dwNvlTime()" },
        };

    /// <summary>
    /// The emitted null literal equals the oracle text byte for byte, including its camel casing.
    /// </summary>
    [Theory]
    [MemberData(nameof(NullSentinelSpellings))]
    public void TheEmittedNullLiteralIsTheOracleTextByteForByte(
        string validatorKey,
        string legacyDeclarationLocator,
        string legacyLowerCaseObjectName,
        string expectedFunctionName,
        string expectedNullLiteral)
    {
        string actual = ActualNullLiteralExpression(validatorKey);

        // Ordinal by construction: Assert.Equal(string, string) performs an ordinal comparison, so no
        // culture and no case folding participates. That is the whole assertion for a literal whose
        // casing is observable.
        Assert.Equal(expectedNullLiteral, actual);

        // And byte for byte, not merely char for char. Encoding the two sides and comparing the byte
        // sequences is what rules out an invisible difference that ordinal string equality would also
        // catch but that a future reader might assume was never checked - a BOM, a homoglyph, or a
        // non-ASCII look-alike introduced by a copy out of a document.
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedNullLiteral);
        byte[] actualBytes = Encoding.UTF8.GetBytes(actual);
        Assert.Equal(expectedBytes, actualBytes);

        // THE NORMALISATION GUARD. The PowerBuilder object at `legacyDeclarationLocator` is spelled
        // `legacyLowerCaseObjectName`, entirely in lower case, and PowerBuilder would accept either
        // spelling because its name resolution is case insensitive. The EMITTED text is not a name
        // being resolved, it is text a DataWindow expression parser reads and a characterization
        // recording stores, so the casing is observable and must be the caller's, not the
        // declaration's.
        Assert.NotEqual(legacyLowerCaseObjectName + "()", actual);
        Assert.NotEqual(expectedFunctionName.ToLowerInvariant(), ActualFunctionName(validatorKey));

        // The row's locator, asserted so that it stays a real `.srf` line reference and cannot decay
        // into a comment nobody maintains: the whole claim above rests on what that line declares.
        Assert.Contains(".srf:L6", legacyDeclarationLocator, StringComparison.Ordinal);
    }

    /// <summary>
    /// The published function name is the camel-cased spelling the emitted text uses, which is the
    /// name a <c>Describe("Evaluate(...)")</c> substitute has to register as expression-callable.
    /// </summary>
    [Theory]
    [MemberData(nameof(NullSentinelSpellings))]
    public void TheFunctionNameIsTheSpellingTheEmittedTextUses(
        string validatorKey,
        string legacyDeclarationLocator,
        string legacyLowerCaseObjectName,
        string expectedFunctionName,
        string expectedNullLiteral)
    {
        Assert.Equal(expectedFunctionName, ActualFunctionName(validatorKey));

        // The row's remaining oracle columns are asserted here too rather than left unused, so that
        // this theory is self-contained: the name is the literal with the parentheses removed, and the
        // declaration locator names the file the pair came from.
        Assert.Equal(expectedFunctionName + "()", expectedNullLiteral);
        Assert.NotEqual(legacyLowerCaseObjectName, expectedFunctionName);
        Assert.NotEqual(string.Empty, legacyDeclarationLocator);
    }

    /// <summary>
    /// The emitted literal is exactly the function name followed by an empty argument list: no
    /// whitespace anywhere, no argument, and nothing after the closing parenthesis.
    /// </summary>
    /// <remarks>
    /// Each of the five producers is declared with an EMPTY parameter list -
    /// <c>global function date dwnvldate ()</c> at <c>dwnvldate.srf:L6</c> and its four siblings - so
    /// a literal carrying an argument would not merely look wrong, it would fail to bind. The checks
    /// are written as index and length arithmetic rather than as prefix or suffix tests, because a
    /// prefix test would accept `dwNvlDate(1)` and a suffix test would accept `xdwNvlDate()`.
    /// </remarks>
    [Theory]
    [MemberData(nameof(NullSentinelSpellings))]
    public void TheEmittedLiteralIsTheFunctionNameWithAnEmptyArgumentList(
        string validatorKey,
        string legacyDeclarationLocator,
        string legacyLowerCaseObjectName,
        string expectedFunctionName,
        string expectedNullLiteral)
    {
        string name = ActualFunctionName(validatorKey);
        string literal = ActualNullLiteralExpression(validatorKey);

        // Lengths are read into locals rather than compared inline, because an `Assert.Equal` whose
        // operand is a `.Length` or `.Count` is what xUnit2013 reports, and warnings are errors here.
        int nameLength = name.Length;
        int literalLength = literal.Length;

        Assert.Equal(name + "()", literal);
        Assert.Equal(nameLength + 2, literalLength);
        Assert.Equal(nameLength, literal.IndexOf('(', StringComparison.Ordinal));
        Assert.Equal(nameLength + 1, literal.IndexOf(')', StringComparison.Ordinal));
        Assert.Equal(-1, literal.IndexOf(' ', StringComparison.Ordinal));
        Assert.Equal(-1, literal.IndexOf(',', StringComparison.Ordinal));

        // The oracle columns, restated as the invariant they encode.
        Assert.Equal(expectedNullLiteral, literal);
        Assert.Equal(expectedFunctionName, name);
        Assert.NotEqual(legacyLowerCaseObjectName + "()", literal);
        Assert.Contains(".srf:L", legacyDeclarationLocator, StringComparison.Ordinal);
    }

    /// <summary>
    /// No emitted literal is <see langword="null"/> or empty, because the empty string is the column
    /// expression engine's ABORT sentinel.
    /// </summary>
    /// <remarks>
    /// <c>n_cst_dwsvc_columnexp.sru:L2183</c> and <c>:L2186</c> both read
    /// <c>if sVal = "" then return ""</c>, and <c>:L2357-L2358</c> returns <c>""</c> from the
    /// <c>case else</c> of the column-type switch. An empty producer would therefore abort every
    /// calculation that touched a null value, silently and with nothing reported anywhere - which is
    /// strictly worse than emitting wrong text, because wrong text at least reaches the evaluator and
    /// comes back as a diagnostic.
    /// </remarks>
    [Theory]
    [MemberData(nameof(NullSentinelSpellings))]
    public void TheEmittedLiteralIsNeverNullOrEmptyBecauseEmptyIsTheAbortSentinel(
        string validatorKey,
        string legacyDeclarationLocator,
        string legacyLowerCaseObjectName,
        string expectedFunctionName,
        string expectedNullLiteral)
    {
        string literal = ActualNullLiteralExpression(validatorKey);
        string name = ActualFunctionName(validatorKey);

        Assert.NotNull(literal);
        Assert.NotEqual(string.Empty, literal);
        Assert.NotNull(name);
        Assert.NotEqual(string.Empty, name);

        // The oracle side of the row, asserted so no column is decorative: the emitted pair is the
        // camel-cased one, it is not the lower-cased declaration, and it came from a `.srf` locator.
        Assert.Equal(expectedNullLiteral, literal);
        Assert.Equal(expectedFunctionName, name);
        Assert.NotEqual(legacyLowerCaseObjectName, name);
        Assert.NotEqual(string.Empty, legacyDeclarationLocator);
    }

    // ==============================================================================================
    //  REGION 2 - THE TYPED NULLS, AGAINST THE LEGACY DECLARED RETURN TYPES
    //  Source: dwnvldate.srf:L6, dwnvldatetime.srf:L6, dwnvlnumber.srf:L6, dwnvlstring.srf:L6,
    //          dwnvltime.srf:L6
    // ==============================================================================================

    /// <summary>
    /// One row per producer: the validator key, the locator of the legacy declaration, the
    /// PowerScript type that declaration names, and the CLR carrier that type maps to under the
    /// transformation plan's type table (AAP 0.4.5.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The number row is the one to read twice. <c>dwnvlnumber.srf:L6</c> declares
    /// <c>global function integer dwnvlnumber ()</c> and <c>:L9</c> declares the local as <c>int</c>.
    /// PowerScript <c>integer</c> is SIXTEEN-BIT SIGNED, so the faithful carrier is
    /// <see cref="short"/> - not <see cref="int"/>, and emphatically not <see cref="decimal"/> or
    /// <see cref="long"/>.
    /// </para>
    /// <para>
    /// The pressure to widen it is real rather than hypothetical, and it comes from the caller: all
    /// FIVE numeric overloads of <c>dwvaluetoexp</c> - <c>decimal</c> at <c>:L17-L19</c>, <c>int</c> at
    /// <c>:L23-L25</c>, <c>long</c> at <c>:L29-L31</c>, <c>double</c> at <c>:L35-L37</c> and
    /// <c>real</c> at <c>:L41-L43</c> - all fall back to this ONE 16-bit sentinel. A reader who
    /// arrives from the decimal overload sees an obvious type mismatch and is tempted to fix it. The
    /// mismatch is the legacy's, it is recorded as a preserved defect in
    /// <c>Validators/ValueToExpression.cs</c>, and constraint C-B forbids correcting it.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string, string> TypedNullDeclaredTypes =>
        new()
        {
            { DateKey, "dwnvldate.srf:L6", "date", "System.Nullable`1[System.DateOnly]" },
            { DateTimeKey, "dwnvldatetime.srf:L6", "datetime", "System.Nullable`1[System.DateTime]" },
            { NumberKey, "dwnvlnumber.srf:L6", "integer", "System.Nullable`1[System.Int16]" },
            { StringKey, "dwnvlstring.srf:L6", "string", "System.String" },
            { TimeKey, "dwnvltime.srf:L6", "time", "System.Nullable`1[System.TimeOnly]" },
        };

    /// <summary>
    /// The DECLARED return type of each typed-null member is the CLR carrier of the PowerScript type
    /// the legacy declaration names - asserted by reflection over the member, not by inspecting a
    /// value.
    /// </summary>
    /// <remarks>
    /// Reflecting over the declaration rather than over the returned value is the point of this
    /// theory. Every one of the five members returns <see langword="null"/>, and a null carries no
    /// runtime type at all, so an assertion made against the value would pass identically for
    /// <c>short?</c>, <c>int?</c>, <c>decimal?</c> and <c>long?</c>. The declared type is the thing
    /// the expression engine and the wire contract see, so the declared type is what is pinned.
    /// </remarks>
    [Theory]
    [MemberData(nameof(TypedNullDeclaredTypes))]
    public void TheTypedNullCarrierMatchesTheLegacyDeclaredReturnType(
        string validatorKey,
        string legacyDeclarationLocator,
        string legacyDeclaredType,
        string expectedCarrierTypeName)
    {
        Type declared = TypedNullMemberType(validatorKey);

        Assert.Equal(expectedCarrierTypeName, TypeKey(declared));

        // The oracle columns, restated: the legacy type name is non-empty and the locator names the
        // file it was read from, so a future edit to either column cannot pass unnoticed.
        Assert.NotEqual(string.Empty, legacyDeclaredType);
        Assert.Contains(".srf:L6", legacyDeclarationLocator, StringComparison.Ordinal);
    }

    /// <summary>
    /// The numeric sentinel is SIXTEEN BIT and is neither <see cref="decimal"/> nor
    /// <see cref="long"/>.
    /// </summary>
    /// <remarks>
    /// Called out as its own theory rather than folded into the matrix above because it is the one
    /// carrier in the family that a well-intentioned change would widen. <c>dwnvlnumber.srf:L6</c>
    /// declares <c>integer</c>; PowerScript <c>integer</c> is 16-bit signed; the five numeric
    /// <c>dwvaluetoexp</c> overloads all share this sentinel regardless of their own width. Widening
    /// it would change the type the DataWindow expression engine is handed for an absent numeric
    /// value, which is observable behaviour and therefore forbidden by C-B.
    /// </remarks>
    [Theory]
    [MemberData(nameof(NumericSentinelWidthRows))]
    public void TheNumericSentinelIsSixteenBitAndIsNeitherDecimalNorLong(
        string legacyDeclarationLocator,
        string legacyDeclaredType,
        int expectedBitWidth)
    {
        Type declared = TypedNullMemberType(NumberKey);
        Type? underlying = Nullable.GetUnderlyingType(declared);

        Assert.NotNull(underlying);
        Assert.Equal(typeof(short), underlying);
        Assert.NotEqual(typeof(decimal), underlying);
        Assert.NotEqual(typeof(long), underlying);
        Assert.NotEqual(typeof(int), underlying);

        // 16 bits, stated numerically so the row itself records why `short` and not `int`. The width is
        // read into a local because xUnit2000 requires a literal or constant expression to occupy the
        // `expected` position, and the oracle's value is the one that belongs there.
        int carrierBitWidth = sizeof(short) * 8;
        Assert.Equal(expectedBitWidth, carrierBitWidth);
        Assert.Equal("integer", legacyDeclaredType);
        Assert.Equal("dwnvlnumber.srf:L6", legacyDeclarationLocator);
    }

    /// <summary>
    /// The single row backing <see cref="TheNumericSentinelIsSixteenBitAndIsNeitherDecimalNorLong"/>.
    /// </summary>
    /// <remarks>
    /// One row, and still a theory rather than a fact, because AAP 0.6.7 asks for the whole family to
    /// be table driven: the row is where the oracle locator and the declared width live, and keeping
    /// it in a row keeps the shape uniform with every other member of this class.
    /// </remarks>
    public static TheoryData<string, string, int> NumericSentinelWidthRows =>
        new()
        {
            { "dwnvlnumber.srf:L6", "integer", 16 },
        };

    /// <summary>
    /// Every typed null IS <see langword="null"/>, and is never the default value of its carrier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AAP 0.4.5.4 forbids collapsing a PowerScript null to a zero, because doing so converts
    /// "neither succeeded nor failed" into "succeeded" in the return-code algebra. The same collapse
    /// here has an equally silent consequence: <c>default(short)</c> is <c>0</c>,
    /// <c>default(DateTime)</c> is <c>0001-01-01 00:00:00</c>, <c>default(TimeOnly)</c> is midnight
    /// and <c>default(string)</c> would most likely be rendered as <c>""</c> - so a null column would
    /// become indistinguishable from a column legitimately holding that value.
    /// </para>
    /// <para>
    /// The row carries the default rendering so that the assertion names what it is ruling out. Note
    /// that midnight is the genuinely dangerous one, and it is dangerous in the OTHER direction too:
    /// <c>TimeValidator.UncoercibleFallback</c> deliberately IS midnight, reproducing the legacy's
    /// inability to tell an uncoercible time from <c>00:00:00</c> - see REGION 5. The typed NULL must
    /// still be null, and this theory is what keeps the two apart.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(TypedNullDefaultValueRows))]
    public void TheTypedNullIsNullAndNeverTheDefaultValueOfItsCarrier(
        string validatorKey,
        string legacyBodyLocator,
        string ruledOutDefaultRendering)
    {
        object? value = TypedNullMemberValue(validatorKey);

        // dwnvl*.srf:L9-L11 - `<type> nvl / SetNull(nvl) / return nvl`. Three statements whose whole
        // purpose is to produce a null of a specific type, so null is the entire contract.
        Assert.Null(value);

        // And it is not the carrier's default dressed up as absence. Boxing the default and rendering
        // it invariantly is what makes the comparison meaningful for a value type: an unboxed
        // `default(short)` compared against a null `object` would be trivially unequal and would prove
        // nothing about the member.
        object boxedDefault = CarrierDefaultValue(validatorKey);
        Assert.NotNull(boxedDefault);
        Assert.Equal(ruledOutDefaultRendering, RenderInvariant(boxedDefault));
        Assert.NotEqual(boxedDefault, value);

        Assert.Contains(".srf:L9", legacyBodyLocator, StringComparison.Ordinal);
    }

    /// <summary>
    /// One row per producer, carrying the invariant rendering of the carrier's default value - the
    /// value the typed null must NOT be.
    /// </summary>
    public static TheoryData<string, string, string> TypedNullDefaultValueRows =>
        new()
        {
            { DateKey, "dwnvldate.srf:L9-L11", "0001-01-01" },
            { DateTimeKey, "dwnvldatetime.srf:L9-L11", "0001-01-01 00:00:00" },
            { NumberKey, "dwnvlnumber.srf:L9-L11", "0" },
            { StringKey, "dwnvlstring.srf:L9-L11", string.Empty },
            { TimeKey, "dwnvltime.srf:L9-L11", "00:00:00" },
        };

    // ==============================================================================================
    //  REGION 3 - THE SIX-ARM TOKEN TABLE, SPLIT ACROSS FIVE FILES
    //  Source: se_cst_dw.sru:L231-L244
    // ==============================================================================================

    /// <summary>
    /// One row per ARM - six of them, across five validator files - carrying the arm's legacy locator,
    /// the conversion function the arm applies, and the arm's token set in the oracle's declaration
    /// order, pipe separated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SIX ROWS AND FIVE FILES IS THE POINT. <c>NumberValidator</c> owns two arms, because
    /// <c>se_cst_dw.sru:L234-L235</c> converts with <c>Dec()</c> while <c>:L236-L237</c> converts with
    /// <c>Long()</c>, and the oracle keeps them apart. Merging them would truncate the fractional part
    /// of every <c>number</c>, <c>decimal</c> and <c>real</c> column - and the primary fixture has two
    /// such columns, <c>salary decimal(2)</c> and <c>age number</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L10,L12</c>], so the merge would be exercised
    /// immediately rather than in some rare branch.
    /// </para>
    /// <para>
    /// The order within each arm is the oracle's declaration order, and it is asserted rather than
    /// sorted. It is not merely cosmetic: the published arrays are what a reader compares against the
    /// source, and a reordering would silently make that comparison harder while looking like a tidy
    /// up.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string, string> ArmTokenSets =>
        new()
        {
            { StringArm, "se_cst_dw.sru:L232-L233", "(none - raw pass-through)", "char|char(" },
            { NumberDecimalArm, "se_cst_dw.sru:L234-L235", "Dec", "decim|real|numbe" },
            { NumberLongArm, "se_cst_dw.sru:L236-L237", "Long", "long|ulong" },
            { DateTimeArm, "se_cst_dw.sru:L238-L239", "DateTime", "datet" },
            { DateArm, "se_cst_dw.sru:L240-L241", "Date", "date" },
            { TimeArm, "se_cst_dw.sru:L242-L243", "Time", "time" },
        };

    /// <summary>
    /// Each arm publishes exactly its legacy token set, in the oracle's declaration order.
    /// </summary>
    [Theory]
    [MemberData(nameof(ArmTokenSets))]
    public void EachArmPublishesExactlyItsLegacyTokenSetInDeclarationOrder(
        string armKey,
        string legacyLocator,
        string legacyConversionFunction,
        string expectedTokensPipeSeparated)
    {
        IReadOnlyList<string> published = PublishedArmTokens(armKey);

        Assert.Equal(expectedTokensPipeSeparated, string.Join('|', published));

        // Every token is at most five characters, because every one of them is compared against
        // `Left(dwo.ColType,5)` [se_cst_dw.sru:L231]. A six-character token could never match anything
        // and would be dead configuration masquerading as behaviour.
        foreach (string token in published)
        {
            Assert.NotEqual(string.Empty, token);
            Assert.True(token.Length <= 5, "Token '" + token + "' cannot match Left(ColType,5).");
            Assert.Equal(token, token.ToLowerInvariant());
        }

        Assert.Contains("se_cst_dw.sru:L", legacyLocator, StringComparison.Ordinal);
        Assert.NotEqual(string.Empty, legacyConversionFunction);
    }

    /// <summary>
    /// One row per NAMED TOKEN CONSTANT: the constant's fully qualified name, the arm that owns it, the
    /// legacy locator of the <c>case</c> label it reproduces, and the oracle text it must carry.
    /// </summary>
    /// <remarks>
    /// Four of the five validators name their token as a constant as well as publishing it in a set -
    /// <c>StringValidator</c> names both of its two - and the two representations can drift apart,
    /// because a set built from a literal rather than from the constant would still look right. These
    /// rows pin the constant's text against the oracle and then prove the published set is built from
    /// it. <c>NumberValidator</c> has no such constants, publishing only its two arrays, so it has no
    /// row here and its arrays are covered by <see cref="ArmTokenSets"/>.
    /// </remarks>
    public static TheoryData<string, string, string, string> PublishedTokenConstants =>
        new()
        {
            { "StringValidator.ColTypeTokenChar", StringArm, "se_cst_dw.sru:L232", "char" },
            { "StringValidator.ColTypeTokenParameterizedChar", StringArm, "se_cst_dw.sru:L232", "char(" },
            { "DateTimeValidator.ColumnTypePrefix", DateTimeArm, "se_cst_dw.sru:L238", "datet" },
            { "DateValidator.ColumnTypePrefix", DateArm, "se_cst_dw.sru:L240", "date" },
            { "TimeValidator.ColumnTypeToken", TimeArm, "se_cst_dw.sru:L242", "time" },
        };

    /// <summary>
    /// Every named token constant carries its oracle text, appears in its own arm's published set, and
    /// is claimed by that arm and by no other.
    /// </summary>
    [Theory]
    [MemberData(nameof(PublishedTokenConstants))]
    public void EveryNamedTokenConstantCarriesItsOracleTextAndBacksItsPublishedSet(
        string constantName,
        string ownerArm,
        string legacyLocator,
        string expectedToken)
    {
        string declared = PublishedTokenConstantValue(constantName);

        Assert.Equal(expectedToken, declared);

        // The published set is built FROM the constant, not from a second copy of its text.
        Assert.Contains(declared, PublishedArmTokens(ownerArm));

        // And the arm's own predicate claims a column type equal to the constant, while no other arm
        // does - so the constant, the set and the predicate are three views of one decision.
        int claims = 0;
        foreach (string armKey in AllArms)
        {
            if (ArmMatches(armKey, declared))
            {
                claims++;
            }
        }

        Assert.Equal(1, claims);
        Assert.True(ArmMatches(ownerArm, declared), "Arm '" + ownerArm + "' must claim '" + declared + "'.");
        Assert.Contains("se_cst_dw.sru:L", legacyLocator, StringComparison.Ordinal);
    }

    /// <summary>
    /// One row per format the <c>Time()</c> arm accepts when parsing, with a sample and its expected
    /// components.
    /// </summary>
    /// <remarks>
    /// <c>TimeValidator</c> publishes its accepted set so that a parity test can state the accepted
    /// surface EXACTLY rather than infer it from behaviour, and the set is deliberately WIDER than the
    /// single format used for emission: the single-letter specifiers <c>H</c>, <c>m</c> and <c>s</c>
    /// match one digit or two, so <c>"1:2:3"</c> is accepted as readily as <c>"01:02:03"</c>. That
    /// asymmetry between the accept surface and the emit surface is what
    /// <see cref="TheCanonicalEmissionFormatIsNotItselfAnAcceptedParseFormat"/> pins.
    /// </remarks>
    public static TheoryData<string, string, int, int, int> TimeAcceptedFormats =>
        new()
        {
            { "H:m:s", "10:20:30", 10, 20, 30 },
            { "H:m:s.FFFFFFF", "10:20:30.5", 10, 20, 30 },
            { "H:m", "10:20", 10, 20, 0 },
        };

    /// <summary>
    /// Every published accepted format is genuinely accepted, and the published set is exactly three
    /// formats wide.
    /// </summary>
    [Theory]
    [MemberData(nameof(TimeAcceptedFormats))]
    public void EveryPublishedAcceptedTimeFormatIsGenuinelyAccepted(
        string publishedFormat,
        string sampleText,
        int expectedHour,
        int expectedMinute,
        int expectedSecond)
    {
        Assert.Contains(publishedFormat, TimeValidator.AcceptedFormats);

        int publishedFormatCount = TimeValidator.AcceptedFormats.Count;
        Assert.Equal(3, publishedFormatCount);

        TimeOnly? coerced = TimeValidator.Coerce(sampleText);

        Assert.True(coerced.HasValue);
        Assert.Equal(expectedHour, coerced.Value.Hour);
        Assert.Equal(expectedMinute, coerced.Value.Minute);
        Assert.Equal(expectedSecond, coerced.Value.Second);

        // Not the uncoercible fallback: these rows must have parsed, not fallen through. The check
        // matters for the "10:20" row in particular, whose seconds are legitimately zero.
        Assert.NotEqual(RetCode.E_INVALID_DATA, TimeValidator.TryCoerce(sampleText, out TimeOnly? _));
    }

    /// <summary>
    /// The canonical EMISSION format is not itself one of the accepted PARSE formats, yet emitted text
    /// still round-trips - because the accepted specifiers match both widths.
    /// </summary>
    /// <remarks>
    /// Pinned as its own theory because the two facts look contradictory and a future reader who fixed
    /// the apparent inconsistency - by adding <c>"HH:mm:ss"</c> to the accepted set, or by narrowing the
    /// accepted set to it - would change the accept surface in one direction or the other without
    /// intending to. The round-trip assertion is the property that actually matters, and it holds
    /// without the literal strings matching.
    /// </remarks>
    [Theory]
    [MemberData(nameof(TimeRoundTripSamples))]
    public void TheCanonicalEmissionFormatIsNotItselfAnAcceptedParseFormat(
        int hour,
        int minute,
        int second)
    {
        Assert.DoesNotContain(TimeValidator.CanonicalFormat, TimeValidator.AcceptedFormats);

        TimeOnly value = new(hour, minute, second);
        string emitted = TimeValidator.Format(value);

        Assert.Equal(value, TimeValidator.Coerce(emitted));
    }

    /// <summary>Sample times spanning single- and double-digit components for the round trip.</summary>
    public static TheoryData<int, int, int> TimeRoundTripSamples =>
        new()
        {
            { 0, 0, 0 },
            { 1, 2, 3 },
            { 10, 20, 30 },
            { 23, 59, 59 },
        };

    /// <summary>
    /// One row per LEGACY TOKEN - ten of them, counted from <c>se_cst_dw.sru:L232</c> through
    /// <c>:L243</c> - naming the single arm that owns it.
    /// </summary>
    /// <remarks>
    /// This table is the union check. Ten rows, and the theory asserts sole ownership per row, so
    /// together they prove that the union of the five files' token sets is EXACTLY the oracle's set:
    /// nothing missing, because every legacy token has a row that must find its owner; nothing
    /// invented, because <see cref="TheArmsPublishExactlyTenTokensInTotal"/> pins the total count and
    /// <see cref="NoArmClaimsAColumnTypeOutsideTheLegacyTable"/> sweeps the near misses.
    /// </remarks>
    public static TheoryData<string, string> LegacyTokenOwners =>
        new()
        {
            { "char", StringArm },
            { "char(", StringArm },
            { "decim", NumberDecimalArm },
            { "real", NumberDecimalArm },
            { "numbe", NumberDecimalArm },
            { "long", NumberLongArm },
            { "ulong", NumberLongArm },
            { "datet", DateTimeArm },
            { "date", DateArm },
            { "time", TimeArm },
        };

    /// <summary>
    /// Every legacy token is published by exactly one arm, and it is the expected one.
    /// </summary>
    [Theory]
    [MemberData(nameof(LegacyTokenOwners))]
    public void EveryLegacyTokenIsPublishedByExactlyOneArm(string token, string expectedOwnerArm)
    {
        int owners = 0;
        string? found = null;

        foreach (string armKey in AllArms)
        {
            foreach (string published in PublishedArmTokens(armKey))
            {
                if (string.Equals(published, token, StringComparison.Ordinal))
                {
                    owners++;
                    found = armKey;
                }
            }
        }

        Assert.Equal(1, owners);
        Assert.Equal(expectedOwnerArm, found);
    }

    /// <summary>
    /// The six arms publish ten tokens in total - the exact count in the oracle switch - so no
    /// eleventh token has been invented and none has been dropped.
    /// </summary>
    /// <remarks>
    /// The count is asserted alongside the per-token ownership above because the two together are what
    /// close the set. Ownership alone would tolerate an extra token nobody asked for; a count alone
    /// would tolerate a substitution.
    /// </remarks>
    [Theory]
    [MemberData(nameof(LegacyTokenTotalRows))]
    public void TheArmsPublishExactlyTenTokensInTotal(string legacyLocator, int expectedTotal)
    {
        int total = 0;
        foreach (string armKey in AllArms)
        {
            total += PublishedArmTokens(armKey).Count;
        }

        Assert.Equal(expectedTotal, total);

        int armCount = AllArms.Length;
        Assert.Equal(6, armCount);
        Assert.Equal("se_cst_dw.sru:L231-L244", legacyLocator);
    }

    /// <summary>The single row backing <see cref="TheArmsPublishExactlyTenTokensInTotal"/>.</summary>
    public static TheoryData<string, int> LegacyTokenTotalRows =>
        new()
        {
            { "se_cst_dw.sru:L231-L244", 10 },
        };

    /// <summary>
    /// One row per input for the raw pass-through arm, each chosen to be destroyed by a specific
    /// "helpful" transformation.
    /// </summary>
    /// <remarks>
    /// <c>se_cst_dw.sru:L233</c> is <c>SetItem(row,Long(dwo.ID),data)</c>: the edit text, with NO
    /// conversion function around it. It is the only one of the six arms without one. The rows below
    /// name what must NOT happen - trimming, truncation to the declared column width, case folding,
    /// quote escaping and empty-to-null collapsing - and each is a transformation a port might
    /// plausibly add while believing it was cleaning up.
    /// </remarks>
    public static TheoryData<string, string> RawPassThroughInputs =>
        new()
        {
            { "  padded  ", "leading and trailing white space is NOT trimmed" },
            { "MiXeDcAsE", "case is NOT folded" },
            { "it's", "an embedded apostrophe is NOT escaped" },
            { "a\tb", "an embedded tab is preserved" },
            { "~t", "the PowerBuilder tilde escape is NOT interpreted" },
            { "", "the empty string stays the empty string and is NOT collapsed to null" },
            { "0123456789012345678901234567890123456789012345678901234567890", "text longer than a narrow column is NOT truncated to the declared width" },
        };

    /// <summary>
    /// The <c>char</c> / <c>char(</c> arm returns the edit text completely untouched - the same
    /// instance, not merely an equal one.
    /// </summary>
    /// <remarks>
    /// Reference identity is asserted deliberately. Equality alone would be satisfied by a port that
    /// normalised and happened to produce an equal string for these particular inputs; identity proves
    /// that no transformation ran at all, which is what <c>se_cst_dw.sru:L233</c> specifies. The width
    /// row matters in particular because the primary fixture declares a 200-character address against
    /// a 50-character database column [AAP 0.6.4], so a well-meant truncation here would silently
    /// change the data the update contract carries.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RawPassThroughInputs))]
    public void TheRawPassThroughArmReturnsTheEditTextUntouched(string data, string whatMustNotHappen)
    {
        string? coerced = StringValidator.Coerce(data);

        Assert.Same(data, coerced);

        int dataLength = data.Length;
        int coercedLength = coerced.Length;
        Assert.Equal(dataLength, coercedLength);
        Assert.NotEqual(string.Empty, whatMustNotHappen);
    }

    // ==============================================================================================
    //  REGION 4 - LENGTH-SAFE EXACT ORDINAL MATCHING
    //  Source: se_cst_dw.sru:L231  `choose case Left(dwo.ColType,5)`
    // ==============================================================================================

    /// <summary>
    /// One row per column type: the raw <c>ColType</c> as <c>Describe</c> reports it, the five
    /// character prefix <c>Left(ColType,5)</c> yields, and the single arm that owns it -
    /// <see cref="NoArm"/> when the oracle's <c>choose case</c> would fall through with no
    /// <c>case else</c> to catch it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FIRST TWO ROWS ARE THE WHOLE REASON THIS MATRIX EXISTS. <c>"datetime"</c> truncates to
    /// <c>"datet"</c>, so the DateTime arm owns it and the Date arm does NOT - even though the raw text
    /// begins with <c>"date"</c>. A port that used a prefix test would route every datetime column
    /// through <c>Date(data)</c> and discard the time component, in complete silence, and a row-count
    /// assertion would not notice. Nothing in this file expresses an expectation as a prefix test.
    /// </para>
    /// <para>
    /// THE FOUR-CHARACTER ROWS ARE THE SECOND REASON. <c>"char"</c>, <c>"date"</c>, <c>"time"</c>,
    /// <c>"real"</c> and <c>"long"</c> are all four characters, so <c>Substring(0, 5)</c> throws on
    /// every one of them where PowerScript's <c>Left</c> returns the token unchanged. Five of the ten
    /// legacy tokens are in that state, so the naive port fails on the majority of the table rather
    /// than on an edge case.
    /// </para>
    /// <para>
    /// The parameterised rows are the ones the primary fixture actually declares:
    /// <c>char(100)</c> for <c>name</c>, <c>char(200)</c> for <c>address</c>, <c>decimal(2)</c> for
    /// <c>salary</c>, <c>number</c> for <c>id</c> and <c>age</c>, and <c>date</c> for <c>birth</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L13</c>].
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string> ColumnTypeOwnership =>
        new()
        {
            // ---- the trap, both halves of it -----------------------------------------------------
            { "datetime", "datet", DateTimeArm },
            { "date", "date", DateArm },

            // ---- the raw pass-through arm, both tokens and both fixture widths --------------------
            { "char", "char", StringArm },
            { "char(", "char(", StringArm },
            { "char(100)", "char(", StringArm },
            { "char(200)", "char(", StringArm },

            // ---- the Dec() arm -------------------------------------------------------------------
            { "number", "numbe", NumberDecimalArm },
            { "numbe", "numbe", NumberDecimalArm },
            { "decimal(2)", "decim", NumberDecimalArm },
            { "decim", "decim", NumberDecimalArm },
            { "real", "real", NumberDecimalArm },

            // ---- the Long() arm ------------------------------------------------------------------
            { "long", "long", NumberLongArm },
            { "ulong", "ulong", NumberLongArm },

            // ---- the Time() arm ------------------------------------------------------------------
            { "time", "time", TimeArm },

            // ---- near misses: NOTHING claims these, because there is no `case else` at :L244 -----
            { "timestamp", "times", NoArm },
            { "blob", "blob", NoArm },
            { "boolean", "boole", NoArm },
            { "", "", NoArm },
            { "d", "d", NoArm },
            { "da", "da", NoArm },
            { "dat", "dat", NoArm },
            { "cha", "cha", NoArm },
            { "tim", "tim", NoArm },
            { "ulon", "ulon", NoArm },
            { "decimal", "decim", NumberDecimalArm },
            { "ulonglong", "ulong", NumberLongArm },

            // ---- case sensitivity: PowerScript `=` on strings is case sensitive, and every arm
            //      literal at :L232-L243 is lower case, so an upper-cased type matches nothing ------
            { "DATE", "DATE", NoArm },
            { "DateTime", "DateT", NoArm },
            { "CHAR(100)", "CHAR(", NoArm },
            { "Number", "Numbe", NoArm },
            { "TIME", "TIME", NoArm },
        };

    /// <summary>
    /// Truncation is <c>Left(ColType,5)</c> - length safe, never throwing on a shorter input - and
    /// every validator that publishes a truncation member agrees on the result.
    /// </summary>
    /// <remarks>
    /// Three of the five validators publish one: <c>NumberValidator.ColumnTypePrefix</c>,
    /// <c>StringValidator.ColTypePrefix</c> and <c>DateValidator.TruncateColumnType</c>. All three are
    /// exercised on every row, which makes this theory a drift guard as well as a correctness check:
    /// three implementations of one oracle line could diverge without any of them looking wrong on its
    /// own.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ColumnTypeOwnership))]
    public void TheColumnTypeIsTruncatedToFiveCharactersWithoutThrowingOnShorterInput(
        string colType,
        string expectedPrefix,
        string expectedOwnerArm)
    {
        Assert.Equal(expectedPrefix, NumberValidator.ColumnTypePrefix(colType));
        Assert.Equal(expectedPrefix, StringValidator.ColTypePrefix(colType));
        Assert.Equal(expectedPrefix, DateValidator.TruncateColumnType(colType));

        // Idempotent, which is what makes it safe for a caller to pre-truncate and for another not to.
        Assert.Equal(expectedPrefix, NumberValidator.ColumnTypePrefix(expectedPrefix));

        // And no longer than five characters, for any input, which is the property that makes exact
        // equality against the arm literals meaningful in the first place.
        Assert.True(expectedPrefix.Length <= 5, "Left(ColType,5) cannot exceed five characters.");
        Assert.NotEqual(string.Empty, expectedOwnerArm);
    }

    /// <summary>
    /// A column type is claimed by EXACTLY ONE arm, or by none at all, under length-safe exact ordinal
    /// equality against <c>Left(ColType,5)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the highest-value assertion in the file, and it carries four separate properties at
    /// once. Mutual exclusivity, because PowerScript <c>choose case</c> selects one arm. The absence of
    /// a <c>case else</c> [<c>se_cst_dw.sru:L244</c>], because a near-miss row expects
    /// <see cref="NoArm"/> and every one of the six predicates must say no. Case sensitivity, because
    /// the upper-cased rows expect <see cref="NoArm"/>. And length safety, because a four-character or
    /// single-character row would throw rather than fail if any predicate used
    /// <c>Substring(0, 5)</c>.
    /// </para>
    /// <para>
    /// The <c>"datetime"</c> row and the <c>"date"</c> row are the pair that a prefix-based port gets
    /// wrong, and they fail here in opposite directions: a prefix port makes the Date arm claim
    /// <c>"datetime"</c> (two owners, so the count assertion fails) and leaves the DateTime arm
    /// claiming nothing for <c>"date"</c>.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ColumnTypeOwnership))]
    public void AColumnTypeIsClaimedByExactlyOneArmUnderExactOrdinalEquality(
        string colType,
        string expectedPrefix,
        string expectedOwnerArm)
    {
        int claims = 0;
        string claimedBy = NoArm;

        foreach (string armKey in AllArms)
        {
            if (ArmMatches(armKey, colType))
            {
                claims++;
                claimedBy = armKey;
            }
        }

        if (string.Equals(expectedOwnerArm, NoArm, StringComparison.Ordinal))
        {
            Assert.Equal(0, claims);
        }
        else
        {
            Assert.Equal(1, claims);
        }

        Assert.Equal(expectedOwnerArm, claimedBy);

        // The prefix the ownership decision was made on, restated so the row documents WHY the arm was
        // or was not selected - "datetime" is owned by the DateTime arm BECAUSE it truncates to
        // "datet", and that reason is what a prefix-based port destroys.
        Assert.Equal(expectedPrefix, NumberValidator.ColumnTypePrefix(colType));
    }

    /// <summary>
    /// A <see langword="null"/> column type is claimed by no arm, and no predicate throws on it.
    /// </summary>
    /// <remarks>
    /// PowerScript's <c>Left(null,5)</c> is null, and a null subject matches no <c>case</c> arm at
    /// <c>se_cst_dw.sru:L232-L243</c>, so nothing is written and nothing is reported. The row also
    /// pins asymmetry C from REGION 0: the three published truncations disagree about what a null
    /// yields - <c>string.Empty</c> from two of them and <see langword="null"/> from
    /// <c>StringValidator</c> - and both reach the same observable outcome.
    /// </remarks>
    [Theory]
    [MemberData(nameof(NullColumnTypeRows))]
    public void ANullColumnTypeIsClaimedByNoArmAndNoPredicateThrows(
        string legacyLocator,
        string expectedOwnerArm)
    {
        foreach (string armKey in AllArms)
        {
            Assert.False(ArmMatches(armKey, null), "Arm '" + armKey + "' must not claim a null ColType.");
        }

        Assert.Equal(string.Empty, NumberValidator.ColumnTypePrefix(null));
        Assert.Equal(string.Empty, DateValidator.TruncateColumnType(null));
        Assert.Null(StringValidator.ColTypePrefix(null));

        Assert.Equal(NoArm, expectedOwnerArm);
        Assert.Contains("se_cst_dw.sru:L", legacyLocator, StringComparison.Ordinal);
    }

    /// <summary>The single row backing <see cref="ANullColumnTypeIsClaimedByNoArmAndNoPredicateThrows"/>.</summary>
    public static TheoryData<string, string> NullColumnTypeRows =>
        new()
        {
            { "se_cst_dw.sru:L231-L244", NoArm },
        };

    /// <summary>
    /// One row per column type that no arm may claim, expressed as its own table so the absence of a
    /// <c>case else</c> is a first-class assertion rather than a by-product.
    /// </summary>
    /// <remarks>
    /// Every entry is a plausible <c>Describe</c> answer that a prefix test, a case-insensitive
    /// comparison or an invented seventh arm would wrongly claim. <c>"timestamp"</c> is the sharpest:
    /// it truncates to <c>"times"</c>, which is not <c>"time"</c>, so the Time arm must reject it -
    /// but a prefix test would accept it and coerce a timestamp through <c>Time(data)</c>.
    /// </remarks>
    public static TheoryData<string, string> UnclaimedColumnTypes =>
        new()
        {
            { "timestamp", "truncates to 'times', which is not the token 'time'" },
            { "blob", "the legacy switch has no blob arm" },
            { "boolean", "truncates to 'boole'; a checkbox column reaches :L244 unwritten" },
            { "", "the empty string equals no arm literal" },
            { " ", "a single space equals no arm literal" },
            { "d", "one character, and length safety must not turn it into a throw" },
            { "dateti", "truncates to 'datet' - CLAIMED, so this row is not here" },
            { "DATE", "upper case; PowerScript string equality is case sensitive" },
            { "Char(100)", "mixed case; the arm literal at :L232 is lower case" },
            { "int", "PowerBuilder reports 'number', never 'int', for a DataWindow column" },
            { "money", "no arm; a currency column reaches :L244 unwritten" },
            { "varchar", "truncates to 'varch'; the arm literal is 'char'" },
        };

    /// <summary>
    /// No arm claims a column type outside the legacy ten-token table - with the one deliberate
    /// exception the table documents.
    /// </summary>
    /// <remarks>
    /// The <c>"dateti"</c> row is present on purpose and is asserted as CLAIMED: it truncates to
    /// <c>"datet"</c>, so the oracle's DateTime arm genuinely owns it, and a sweep that quietly
    /// expected every "odd looking" type to be unclaimed would be wrong. Keeping the counter-example
    /// inside the same table is what stops this theory from degenerating into a rule of thumb.
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnclaimedColumnTypes))]
    public void NoArmClaimsAColumnTypeOutsideTheLegacyTable(string colType, string reason)
    {
        bool isTheDocumentedException = string.Equals(colType, "dateti", StringComparison.Ordinal);

        int claims = 0;
        foreach (string armKey in AllArms)
        {
            if (ArmMatches(armKey, colType))
            {
                claims++;
            }
        }

        if (isTheDocumentedException)
        {
            Assert.Equal(1, claims);
            Assert.True(DateTimeValidator.MatchesColumnType(colType));
        }
        else
        {
            Assert.Equal(0, claims);
        }

        Assert.NotEqual(string.Empty, reason);
    }

    /// <summary>
    /// The truncation baked into the test double agrees with every validator's, so the suite cannot
    /// drift into a second, disagreeing copy of <c>Left(ColType,5)</c>.
    /// </summary>
    /// <remarks>
    /// <c>FakeColumnType.CoercionPrefix</c> in <c>FakeDataWindowHost.cs</c> is a fourth implementation
    /// of <c>se_cst_dw.sru:L231</c>, living in the test assembly. Two copies that disagree would make a
    /// fixture and a production predicate quietly answer differently about the same column, which is
    /// exactly the failure this theory closes. Note the one intended difference in shape rather than
    /// result: the double folds <see langword="null"/> to <see cref="string.Empty"/>, as two of the
    /// three production truncations do.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ColumnTypeOwnership))]
    public void TheTestDoubleTruncationAgreesWithEveryPublishedTruncation(
        string colType,
        string expectedPrefix,
        string expectedOwnerArm)
    {
        Assert.Equal(expectedPrefix, FakeColumnType.CoercionPrefix(colType));
        Assert.Equal(NumberValidator.ColumnTypePrefix(colType), FakeColumnType.CoercionPrefix(colType));
        Assert.Equal(DateValidator.TruncateColumnType(colType), FakeColumnType.CoercionPrefix(colType));
        Assert.Equal(string.Empty, FakeColumnType.CoercionPrefix(null));
        Assert.NotEqual(string.Empty, expectedOwnerArm);
    }

    /// <summary>
    /// One row per column-type constant the test double publishes, pinning it against the arm the
    /// production predicates assign it to.
    /// </summary>
    /// <remarks>
    /// The double's constants are what other suites in this assembly build fixtures from, so if one of
    /// them ever named a type the production table does not own, every fixture built on it would
    /// silently exercise the unwritten <c>:L244</c> path instead of the arm its author intended.
    /// </remarks>
    public static TheoryData<string, string> TestDoubleColumnTypeConstants =>
        new()
        {
            { FakeColumnType.Number, NumberDecimalArm },
            { FakeColumnType.Real, NumberDecimalArm },
            { FakeColumnType.Long, NumberLongArm },
            { FakeColumnType.ULong, NumberLongArm },
            { FakeColumnType.Char, StringArm },
            { FakeColumnType.CharOf(100), StringArm },
            { FakeColumnType.CharOf(200), StringArm },
            { FakeColumnType.DecimalOf(2), NumberDecimalArm },
            { FakeColumnType.Date, DateArm },
            { FakeColumnType.DateTime, DateTimeArm },
            { FakeColumnType.Time, TimeArm },
        };

    /// <summary>
    /// Every column-type constant the test double publishes is owned by exactly the arm the production
    /// predicates assign it to.
    /// </summary>
    [Theory]
    [MemberData(nameof(TestDoubleColumnTypeConstants))]
    public void EveryTestDoubleColumnTypeIsOwnedByTheExpectedArm(string colType, string expectedOwnerArm)
    {
        int claims = 0;
        string claimedBy = NoArm;

        foreach (string armKey in AllArms)
        {
            if (ArmMatches(armKey, colType))
            {
                claims++;
                claimedBy = armKey;
            }
        }

        Assert.Equal(1, claims);
        Assert.Equal(expectedOwnerArm, claimedBy);
    }

    /// <summary>
    /// One row per validator that declares the five-character truncation length, with the constant's
    /// name and its value.
    /// </summary>
    /// <remarks>
    /// Asymmetry D from REGION 0: three of the five declare it and two do not, and the two names that
    /// are used differ. All three are pinned to five here, because five is not an arbitrary choice -
    /// it is the literal second argument of <c>Left(dwo.ColType,5)</c> at <c>se_cst_dw.sru:L231</c>,
    /// and the longest arm literal, <c>"char("</c>, is exactly five characters.
    /// </remarks>
    public static TheoryData<string, string, int> PublishedPrefixLengths =>
        new()
        {
            { NumberKey, "NumberValidator.ColumnTypePrefixLength", NumberValidator.ColumnTypePrefixLength },
            { StringKey, "StringValidator.ColTypePrefixLength", StringValidator.ColTypePrefixLength },
            { DateKey, "DateValidator.ColumnTypePrefixLength", DateValidator.ColumnTypePrefixLength },
        };

    /// <summary>
    /// The five-character truncation length is five wherever it is declared.
    /// </summary>
    [Theory]
    [MemberData(nameof(PublishedPrefixLengths))]
    public void ThePublishedPrefixLengthIsFiveWhereverItIsDeclared(
        string validatorKey,
        string constantName,
        int declaredLength)
    {
        Assert.Equal(5, declaredLength);

        // Five is not arbitrary: it is the literal second argument of `Left(dwo.ColType,5)` and the
        // length of the longest arm literal, `"char("`.
        int longestArmLiteralLength = "char(".Length;
        Assert.Equal(longestArmLiteralLength, declaredLength);
        Assert.NotEqual(string.Empty, validatorKey);
        Assert.Contains("Length", constantName, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  REGION 5 - COERCION BEHAVIOUR: THE LEGACY-FAITHFUL FORM AND THE ADDITIVE REPORTING FORM
    //  Source: se_cst_dw.sru:L233, :L235, :L237, :L239, :L241, :L243
    // ==============================================================================================
    //
    //  THE LEGACY COERCION IS UNCHECKED, TOTAL AND SILENT. Every one of the six arms is a bare
    //  `SetItem(...)` whose return value is discarded, with no validity test anywhere and no
    //  `case else` to catch an unrecognised type. So the legacy-faithful members below NEVER THROW
    //  and NEVER REPORT, for any input, and these theories assert exactly that rather than assuming
    //  an exception: an `Assert.Throws` anywhere in this region would be asserting a behaviour the
    //  oracle does not have.
    //
    //  EACH ARM ALSO PUBLISHES A SEPARATE Try-SHAPED MEMBER, AND THOSE ARE ADDITIVE. They have no
    //  legacy analogue at all - they exist because a service boundary has to be able to turn an
    //  uncoercible payload into a structured error, which an in-process call could simply swallow.
    //  The item-change path must NOT use them, and REGION 5's cross-check proves it does not by
    //  observing that an uncoercible value still reaches `SetItem` rather than raising anything.
    //
    //  ASYMMETRY E, FOUND WHILE WRITING THIS FILE AND PINNED RATHER THAN "FIXED": the six reporting
    //  members do not agree about a null input. `DateValidator.TryCoerce(null)`,
    //  `TimeValidator.TryCoerce(null)`, `StringValidator.TryCoerce(null)`,
    //  `NumberValidator.TryCoerceToDecimal(null)` and `TryCoerceToLong(null)` all report
    //  `RetCode.OK`, treating absence as a legitimately absent value; `DateTimeValidator.TryCoerce
    //  (null)` reports `RetCode.E_INVALID_DATA`. Since these members are ADDITIVE the divergence is
    //  not a parity defect, but it IS an observable contract difference between five siblings and a
    //  sixth, so it is pinned here where a future change to either side must confront it.

    /// <summary>
    /// The <c>Dec()</c> arm: one row per input, with the invariant rendering of the coerced value or
    /// the empty string when the coercion yields nothing.
    /// </summary>
    /// <remarks>
    /// The carrier is <see cref="decimal"/> because the legacy conversion is <c>Dec()</c>, a
    /// fixed-point type. A binary floating-point carrier would change the value written for a
    /// <c>real</c> column, and two of the three tokens in this arm - <c>real</c> and <c>numbe</c> -
    /// name floating-point column types, so the temptation is right there in the token list.
    /// <c>"42.50"</c> is in the table for that reason: <see cref="decimal"/> preserves the declared
    /// scale, and a <c>double</c> carrier would render it as <c>"42.5"</c>.
    /// </remarks>
    public static TheoryData<string?, bool, string> DecimalArmCoercions =>
        new()
        {
            { null, false, string.Empty },
            { string.Empty, false, string.Empty },
            { "0", true, "0" },
            { "42", true, "42" },
            { "42.50", true, "42.50" },
            { "-42.5", true, "-42.5" },
            { " 42 ", true, "42" },
            { "1e3", true, "1000" },
            { "1,234", false, string.Empty },
            { "abc", false, string.Empty },
            { "1e400", false, string.Empty },
        };

    /// <summary>
    /// The <c>Dec()</c> arm coercion is total, invariant and silent.
    /// </summary>
    [Theory]
    [MemberData(nameof(DecimalArmCoercions))]
    public void TheDecimalArmCoercionIsTotalInvariantAndSilent(
        string? data,
        bool expectValue,
        string expectedInvariantRendering)
    {
        // No try/catch and no Assert.Throws: if this call raises for any row, the row fails, and that
        // IS the assertion that se_cst_dw.sru:L235 cannot throw.
        decimal? coerced = NumberValidator.CoerceToDecimal(data);

        if (expectValue)
        {
            Assert.True(coerced.HasValue);
            Assert.Equal(expectedInvariantRendering, coerced.Value.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            Assert.Null(coerced);
            Assert.Equal(string.Empty, expectedInvariantRendering);
        }
    }

    /// <summary>
    /// The <c>Long()</c> arm: one row per input, with the invariant rendering of the coerced value or
    /// the empty string when the coercion yields nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE ARM, TWO LEGACY RANGES. PowerScript <c>long</c> is 32-bit signed and <c>ulong</c> is 32-bit
    /// unsigned, and <c>se_cst_dw.sru:L236</c> puts both tokens in the same arm with the same
    /// conversion. The single 64-bit signed carrier spans both, which is why <c>"4294967295"</c> - the
    /// unsigned 32-bit maximum - is in the table and must succeed.
    /// </para>
    /// <para>
    /// <c>"12.34"</c> FAILS rather than truncating to 12. That is the narrow reading recorded as
    /// unverified-from-repository in <c>NumberValidator</c>, and it is the right default because a
    /// silent truncation would be data loss invented by the port. <c>"1e3"</c> fails for the same
    /// reason the decimal arm accepts it: the two arms pin different <c>NumberStyles</c>, and that
    /// difference is deliberate.
    /// </para>
    /// </remarks>
    public static TheoryData<string?, bool, string> LongArmCoercions =>
        new()
        {
            { null, false, string.Empty },
            { string.Empty, false, string.Empty },
            { "0", true, "0" },
            { "42", true, "42" },
            { "-42", true, "-42" },
            { " 42 ", true, "42" },
            { "2147483648", true, "2147483648" },
            { "4294967295", true, "4294967295" },
            { "12.34", false, string.Empty },
            { "1e3", false, string.Empty },
            { "1,234", false, string.Empty },
            { "9223372036854775808", false, string.Empty },
            { "abc", false, string.Empty },
        };

    /// <summary>
    /// The <c>Long()</c> arm coercion is total, invariant and silent, and spans both legacy integer
    /// ranges.
    /// </summary>
    [Theory]
    [MemberData(nameof(LongArmCoercions))]
    public void TheLongArmCoercionIsTotalInvariantAndSilent(
        string? data,
        bool expectValue,
        string expectedInvariantRendering)
    {
        long? coerced = NumberValidator.CoerceToLong(data);

        if (expectValue)
        {
            Assert.True(coerced.HasValue);
            Assert.Equal(expectedInvariantRendering, coerced.Value.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            Assert.Null(coerced);
            Assert.Equal(string.Empty, expectedInvariantRendering);
        }
    }

    /// <summary>
    /// The <c>Date()</c> arm: one row per input, with the expected date components, or
    /// <see langword="false"/> when the coercion yields the typed null.
    /// </summary>
    /// <remarks>
    /// The <c>"03/04/2020"</c> row is the culture row, and it is the reason every conversion in the
    /// family pins <see cref="CultureInfo.InvariantCulture"/> explicitly: under the invariant culture
    /// that text is unambiguously 4 March, and under a day-first culture the identical text is 3 April.
    /// The two time-component rows are the arm-separation rows: a midnight time component is accepted
    /// and a non-zero one is REFUSED, so a genuine <c>datetime</c> can never half-land in the
    /// <c>date</c> arm and lose its time silently.
    /// </remarks>
    public static TheoryData<string?, bool, int, int, int> DateArmCoercions =>
        new()
        {
            { null, false, 0, 0, 0 },
            { string.Empty, false, 0, 0, 0 },
            { "2020-02-29", true, 2020, 2, 29 },
            { "2020-1-2", true, 2020, 1, 2 },
            { "2020/02/29", true, 2020, 2, 29 },
            { "03/04/2020", true, 2020, 3, 4 },
            { " 2020-02-29 ", true, 2020, 2, 29 },
            { "2020-02-29T00:00:00", true, 2020, 2, 29 },
            { "2020-02-29 10:30", false, 0, 0, 0 },
            { "2020-02-30", false, 0, 0, 0 },
            { "2020-13-45", false, 0, 0, 0 },
            { "abc", false, 0, 0, 0 },
        };

    /// <summary>
    /// The <c>Date()</c> arm coercion is total, invariant and silent, and yields the typed null rather
    /// than a sentinel date when it yields nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(DateArmCoercions))]
    public void TheDateArmCoercionIsTotalInvariantAndSilent(
        string? data,
        bool expectValue,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        DateOnly? coerced = DateValidator.Coerce(data);

        if (expectValue)
        {
            Assert.True(coerced.HasValue);
            Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), coerced.Value);

            // Round trip through the arm's own emission format, which is what closes the loop between
            // what this arm accepts and what dwvaluetoexp.srf:L60 emits.
            Assert.Equal(coerced, DateValidator.Coerce(DateValidator.FormatValue(coerced.Value)));
        }
        else
        {
            Assert.Null(coerced);
            Assert.Equal(0, expectedYear + expectedMonth + expectedDay);
        }
    }

    /// <summary>
    /// The <c>DateTime()</c> arm: one row per input, with the expected components, or
    /// <see langword="false"/> when the coercion yields the typed null.
    /// </summary>
    /// <remarks>
    /// This arm parses against an EXACT format list and pins <c>DateTimeStyles.None</c>, so the rows
    /// that fail are as informative as the rows that succeed: surrounding white space is refused, the
    /// ISO <c>T</c> separator is refused, and a slash-separated date is refused. Refusing rather than
    /// guessing is the narrow choice, and a refusal is observable through the Try-shaped member while a
    /// guess would not be observable at all.
    /// </remarks>
    public static TheoryData<string?, bool, int, int, int, int, int, int> DateTimeArmCoercions =>
        new()
        {
            { null, false, 0, 0, 0, 0, 0, 0 },
            { string.Empty, false, 0, 0, 0, 0, 0, 0 },
            { "2020-02-29 10:30:00", true, 2020, 2, 29, 10, 30, 0 },
            { "2020-02-29 10:30", true, 2020, 2, 29, 10, 30, 0 },
            { "2020-02-29", true, 2020, 2, 29, 0, 0, 0 },
            { "2020-02-29 23:59:59", true, 2020, 2, 29, 23, 59, 59 },
            { " 2020-02-29 10:30:00 ", false, 0, 0, 0, 0, 0, 0 },
            { "2020-02-29T10:30:00", false, 0, 0, 0, 0, 0, 0 },
            { "2020/02/29 10:30:00", false, 0, 0, 0, 0, 0, 0 },
            { "2020-02-30 10:30:00", false, 0, 0, 0, 0, 0, 0 },
            { "abc", false, 0, 0, 0, 0, 0, 0 },
        };

    /// <summary>
    /// The <c>DateTime()</c> arm coercion is total, invariant and silent, and produces a zone-less
    /// value.
    /// </summary>
    [Theory]
    [MemberData(nameof(DateTimeArmCoercions))]
    public void TheDateTimeArmCoercionIsTotalInvariantAndSilent(
        string? data,
        bool expectValue,
        int expectedYear,
        int expectedMonth,
        int expectedDay,
        int expectedHour,
        int expectedMinute,
        int expectedSecond)
    {
        DateTime? coerced = DateTimeValidator.Coerce(data);

        if (expectValue)
        {
            Assert.True(coerced.HasValue);
            Assert.Equal(
                new DateTime(
                    expectedYear,
                    expectedMonth,
                    expectedDay,
                    expectedHour,
                    expectedMinute,
                    expectedSecond,
                    DateTimeKind.Unspecified),
                coerced.Value);

            // A PowerBuilder `datetime` carries no zone, so nothing may be inferred for it: no
            // AssumeLocal, no AssumeUniversal and no adjustment while reading.
            Assert.Equal(DateTimeKind.Unspecified, coerced.Value.Kind);

            // Round trip through the arm's own emission format [dwvaluetoexp.srf:L54].
            Assert.Equal(
                coerced,
                DateTimeValidator.Coerce(DateTimeValidator.FormatExpressionValue(coerced.Value)));
        }
        else
        {
            Assert.Null(coerced);
            Assert.Equal(0, expectedYear + expectedMonth + expectedDay);
            Assert.Equal(0, expectedHour + expectedMinute + expectedSecond);
        }
    }

    /// <summary>
    /// The <c>Time()</c> arm: one row per input, with the outcome encoded as a key because this arm has
    /// THREE outcomes rather than two.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A null input yields the TYPED NULL, because PowerScript propagates a null through a conversion -
    /// a null time is not midnight. Uncoercible non-null text yields
    /// <c>TimeValidator.UncoercibleFallback</c>, which IS midnight, reproducing the legacy's inability
    /// to distinguish uncoercible text from a genuine <c>00:00:00</c> at
    /// <c>se_cst_dw.sru:L243</c>. Those two outcomes are different values and must not be merged.
    /// </para>
    /// <para>
    /// The consequence is worth stating plainly, because it looks like a bug and is not: for the input
    /// <c>"abc"</c> this arm returns midnight, silently, and the item-change path writes it. That is
    /// the legacy behaviour. The reporting member is where the difference becomes visible, and REGION
    /// 5's reporting matrix asserts it there.
    /// </para>
    /// </remarks>
    public static TheoryData<string?, string, int, int, int> TimeArmCoercions =>
        new()
        {
            { null, TypedNullOutcome, 0, 0, 0 },
            { string.Empty, FallbackOutcome, 0, 0, 0 },
            { "10:20:30", ValueOutcome, 10, 20, 30 },
            { "1:2:3", ValueOutcome, 1, 2, 3 },
            { "10:20", ValueOutcome, 10, 20, 0 },
            { "00:00:00", ValueOutcome, 0, 0, 0 },
            { "23:59:59", ValueOutcome, 23, 59, 59 },
            { " 10:20:30 ", FallbackOutcome, 0, 0, 0 },
            { "10:20:30 PM", FallbackOutcome, 0, 0, 0 },
            { "24:00:00", FallbackOutcome, 0, 0, 0 },
            { "abc", FallbackOutcome, 0, 0, 0 },
        };

    /// <summary>The outcome key meaning "the typed null of the arm".</summary>
    private const string TypedNullOutcome = "TypedNull";

    /// <summary>The outcome key meaning "a parsed value".</summary>
    private const string ValueOutcome = "Value";

    /// <summary>The outcome key meaning "the arm's uncoercible fallback value".</summary>
    private const string FallbackOutcome = "Fallback";

    /// <summary>
    /// The <c>Time()</c> arm coercion is total and silent, and keeps its three outcomes distinct.
    /// </summary>
    [Theory]
    [MemberData(nameof(TimeArmCoercions))]
    public void TheTimeArmCoercionKeepsItsThreeOutcomesDistinct(
        string? data,
        string expectedOutcome,
        int expectedHour,
        int expectedMinute,
        int expectedSecond)
    {
        TimeOnly? coerced = TimeValidator.Coerce(data);

        switch (expectedOutcome)
        {
            case TypedNullOutcome:
                Assert.Null(coerced);
                break;

            case ValueOutcome:
                Assert.True(coerced.HasValue);
                Assert.Equal(new TimeOnly(expectedHour, expectedMinute, expectedSecond), coerced.Value);
                Assert.Equal(coerced, TimeValidator.Coerce(TimeValidator.Format(coerced.Value)));
                break;

            case FallbackOutcome:
                Assert.True(coerced.HasValue);
                Assert.Equal(TimeValidator.UncoercibleFallback, coerced.Value);
                Assert.Equal(TimeOnly.MinValue, coerced.Value);
                break;

            default:
                // A throw rather than Assert.Fail, so that no statement after it is unreachable: the
                // repository promotes warnings to errors, and CS0162 would fail the build.
                throw new InvalidOperationException("Unknown outcome key '" + expectedOutcome + "'.");
        }
    }

    /// <summary>
    /// One row per reporting-member call: the arm key, the input, and the return code the member must
    /// report.
    /// </summary>
    /// <remarks>
    /// The codes are REFERENCED from <c>PowerFramework.Shared.Kernel.RetCode</c> - <c>OK</c> is
    /// <c>0</c> [<c>retcode.sru:L39</c>] and <c>E_INVALID_DATA</c> is <c>-9</c>
    /// [<c>retcode.sru:L52</c>] - and no such identifier is DECLARED here, per AAP 0.4.5.3. The
    /// <c>StringArm</c> rows are the interesting ones: that member can never fail, because
    /// <c>se_cst_dw.sru:L233</c> contains no operation capable of failing, so <c>OK</c> is
    /// unconditional by construction rather than by luck. The <c>DateTimeArm</c> null row is
    /// asymmetry E.
    /// </remarks>
    public static TheoryData<string, string?, long> ReportingCoercions =>
        new()
        {
            { NumberDecimalArm, null, RetCode.OK },
            { NumberDecimalArm, "42.50", RetCode.OK },
            { NumberDecimalArm, string.Empty, RetCode.E_INVALID_DATA },
            { NumberDecimalArm, "abc", RetCode.E_INVALID_DATA },
            { NumberLongArm, null, RetCode.OK },
            { NumberLongArm, "42", RetCode.OK },
            { NumberLongArm, "12.34", RetCode.E_INVALID_DATA },
            { NumberLongArm, string.Empty, RetCode.E_INVALID_DATA },
            { StringArm, null, RetCode.OK },
            { StringArm, string.Empty, RetCode.OK },
            { StringArm, "anything at all", RetCode.OK },
            { DateArm, null, RetCode.OK },
            { DateArm, string.Empty, RetCode.OK },
            { DateArm, "   ", RetCode.OK },
            { DateArm, "2020-02-29", RetCode.OK },
            { DateArm, "abc", RetCode.E_INVALID_DATA },
            { DateTimeArm, null, RetCode.E_INVALID_DATA },
            { DateTimeArm, string.Empty, RetCode.E_INVALID_DATA },
            { DateTimeArm, "2020-02-29 10:30:00", RetCode.OK },
            { DateTimeArm, "abc", RetCode.E_INVALID_DATA },
            { TimeArm, null, RetCode.OK },
            { TimeArm, string.Empty, RetCode.E_INVALID_DATA },
            { TimeArm, "10:20:30", RetCode.OK },
            { TimeArm, "abc", RetCode.E_INVALID_DATA },
        };

    /// <summary>
    /// Every reporting member returns a <c>RetCode</c> and never throws.
    /// </summary>
    /// <remarks>
    /// The return type is <see cref="long"/> and not <see cref="bool"/> on every one of them, and that
    /// is asserted structurally by comparing against the <c>RetCode</c> constants: the ported codes are
    /// declared <c>Constant Long</c> in <c>retcode.sru</c>, and flattening them to a boolean would
    /// discard the tri-state algebra the rest of the framework depends on.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReportingCoercions))]
    public void EveryReportingCoercionReturnsARetCodeAndNeverThrows(
        string armKey,
        string? data,
        long expectedCode)
    {
        long actual = InvokeReportingCoercion(armKey, data, out bool hasValue);

        Assert.Equal(expectedCode, actual);

        // On a reported failure the out parameter is the typed null and never a fabricated value. The
        // one exception is the raw pass-through arm, which cannot fail at all.
        if (actual == RetCode.E_INVALID_DATA)
        {
            Assert.False(hasValue);
        }
    }

    // ==============================================================================================
    //  REGION 5b - THE CROSS-CHECK: THE ITEM-CHANGE DISPATCH AGREES WITH THE PUBLISHED TOKEN SETS
    //  Source: se_cst_dw.sru:L226-L251, driven through Domain/ItemChangeProtocol.cs
    // ==============================================================================================
    //
    //  The token sets asserted in REGIONS 3 and 4 are only worth anything if the ONE production
    //  caller of them dispatches on them. `Domain/ItemChangeProtocol.cs` holds a private
    //  `ApplyColumnTypeCoercion` that reproduces se_cst_dw.sru:L231-L244 as a six-way `if/else if`
    //  chain, and a chain is exactly the shape that can silently fall out of step with the sets it
    //  tests - by reordering `"datet"` after `"date"`, by adding a final `else`, or by calling a
    //  reporting coercion instead of a silent one.
    //
    //  The witness is the SetItem OVERLOAD the dispatch selects. `FakeDataWindowHost` records each
    //  overload under a distinct signature - "SetItem(string?)", "SetItem(decimal?)",
    //  "SetItem(long?)", "SetItem(DateTime?)", "SetItem(DateOnly?)", "SetItem(TimeOnly?)" - and the
    //  six arms bind six different overloads because their six coercions return six different
    //  carriers. So the recorded signature identifies the arm that ran, with no access to the private
    //  method and no reflection.
    //
    //  Reaching the coercion needs the oracle's `case else` arm [:L226], which needs two conditions:
    //  the item-change handler must return a code outside {1,2,3} - 0 is used - and the buffer value
    //  must be unchanged across the handler [:L229]. A fresh row with no value satisfies the second,
    //  because IsBufferValueEqual(null, null) is true [:L200].

    /// <summary>
    /// One row per arm plus one unclaimed type: the column type, the edit text, the
    /// <c>SetItem</c> overload signature the dispatch must select, and the invariant rendering of the
    /// value that lands in the buffer.
    /// </summary>
    /// <remarks>
    /// The last row is the <c>case else</c> row. <c>"timestamp"</c> is owned by no arm, so the oracle
    /// writes NOTHING - and the expectation is the absence of any <c>SetItem</c> call at all, which is
    /// how the missing <c>case else</c> at <c>se_cst_dw.sru:L244</c> is asserted end to end rather than
    /// inferred from a predicate. The <c>char</c> row deliberately carries padded text, so a trimming
    /// port fails here as well as in REGION 3.
    /// </remarks>
    public static TheoryData<string, string, string, string> DispatchWitnesses =>
        new()
        {
            { "char(100)", "  Alice  ", "SetItem(string?)", "  Alice  " },
            { "char", "MiXeD", "SetItem(string?)", "MiXeD" },
            { "number", "42.50", "SetItem(decimal?)", "42.50" },
            { "decimal(2)", "1234.75", "SetItem(decimal?)", "1234.75" },
            { "real", "-0.5", "SetItem(decimal?)", "-0.5" },
            { "long", "2147483648", "SetItem(long?)", "2147483648" },
            { "ulong", "4294967295", "SetItem(long?)", "4294967295" },
            { "datetime", "2020-02-29 10:30:00", "SetItem(DateTime?)", "2020-02-29 10:30:00" },
            { "date", "2020-02-29", "SetItem(DateOnly?)", "2020-02-29" },
            { "time", "10:20:30", "SetItem(TimeOnly?)", "10:20:30" },
            { "timestamp", "2020-02-29 10:30:00", NoSetItemCall, string.Empty },
        };

    /// <summary>
    /// The row sentinel meaning "no <c>SetItem</c> overload is called at all", i.e. the oracle's
    /// missing <c>case else</c> at <c>se_cst_dw.sru:L244</c>.
    /// </summary>
    private const string NoSetItemCall = "(no SetItem call)";

    /// <summary>
    /// The item-change dispatch selects exactly the arm the published token sets assign to the column
    /// type, and writes exactly that arm's coerced carrier.
    /// </summary>
    [Theory]
    [MemberData(nameof(DispatchWitnesses))]
    public void TheItemChangeDispatchAgreesWithThePublishedTokenSets(
        string colType,
        string editText,
        string expectedOverloadSignature,
        string expectedStoredRendering)
    {
        FakeDataWindowHost host = new();
        host.AddColumn("c", colType);
        host.AddRow();

        FakeDataWindowObject dwo = host.DwObject("c");
        long columnId = host.Column("c").Id;

        // DisabledEvent = 0, so the EID_ITEMCHANGE gate at :L187 does not short-circuit; the handler
        // returns 0, which is outside {1,2,3} and therefore reaches `case else` at :L226.
        ItemChangeSessionStateDouble session = new();
        ItemChangeEventSinkDouble sink = new();

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(host, session, sink, 1L, dwo, editText);

        // :L250 rewrites the result to 2 unconditionally on this arm, whatever the coercion did.
        Assert.Equal(ItemChangeResult.RestoreAndRejectText, result);

        // :L247 raises the changed event OUTSIDE the `if bEqual` guard, so it fires even on the
        // unclaimed row where nothing was written.
        Assert.Equal(1, sink.ChangedRaiseCount);

        int setItemCalls = 0;
        string? observedSignature = null;
        foreach (string signature in host.CallLog.Signatures)
        {
            if (signature.IndexOf("SetItem(", StringComparison.Ordinal) == 0)
            {
                setItemCalls++;
                observedSignature = signature;
            }
        }

        if (string.Equals(expectedOverloadSignature, NoSetItemCall, StringComparison.Ordinal))
        {
            // se_cst_dw.sru:L244 - `end choose` with no `case else`. Nothing written, nothing reported,
            // nothing thrown, and the cell keeps whatever it held.
            Assert.Equal(0, setItemCalls);
            Assert.Null(host.GetBufferValue(DwBuffer.Primary, 1L, columnId));
            Assert.Equal(string.Empty, expectedStoredRendering);
        }
        else
        {
            Assert.Equal(1, setItemCalls);
            Assert.Equal(expectedOverloadSignature, observedSignature);

            object? stored = host.GetBufferValue(DwBuffer.Primary, 1L, columnId);
            Assert.NotNull(stored);
            Assert.Equal(expectedStoredRendering, RenderInvariant(stored));
        }
    }

    /// <summary>
    /// One row per arm carrying UNCOERCIBLE text, to prove the dispatch stays on the silent coercion
    /// and never routes through the additive reporting one.
    /// </summary>
    /// <remarks>
    /// This is the assertion that keeps the two coercion shapes in their lanes. If the dispatch were
    /// re-pointed at a Try-shaped member and acted on its code, an uncoercible value would start
    /// producing an error where the legacy silently wrote something - a behaviour change on exactly the
    /// branch the port exists to preserve. What the legacy writes differs per arm, which is why the
    /// expected rendering is carried per row: the numeric and temporal arms write an absent value,
    /// while the time arm writes MIDNIGHT.
    /// </remarks>
    public static TheoryData<string, string, string, bool> UncoercibleDispatchWitnesses =>
        new()
        {
            { "number", "not a number", "SetItem(decimal?)", false },
            { "long", "12.34", "SetItem(long?)", false },
            { "datetime", "not a datetime", "SetItem(DateTime?)", false },
            { "date", "not a date", "SetItem(DateOnly?)", false },
            { "time", "not a time", "SetItem(TimeOnly?)", true },
        };

    /// <summary>
    /// Uncoercible edit text still reaches <c>SetItem</c> on the arm's own overload, silently.
    /// </summary>
    [Theory]
    [MemberData(nameof(UncoercibleDispatchWitnesses))]
    public void UncoercibleEditTextIsStillWrittenSilentlyByTheOwningArm(
        string colType,
        string editText,
        string expectedOverloadSignature,
        bool expectStoredValue)
    {
        FakeDataWindowHost host = new();
        host.AddColumn("c", colType);
        host.AddRow();

        FakeDataWindowObject dwo = host.DwObject("c");
        long columnId = host.Column("c").Id;

        ItemChangeSessionStateDouble session = new();
        ItemChangeEventSinkDouble sink = new();

        ItemChangeResult result = ItemChangeProtocol.OnDwnItemChange(host, session, sink, 1L, dwo, editText);

        Assert.Equal(ItemChangeResult.RestoreAndRejectText, result);
        Assert.Contains(expectedOverloadSignature, host.CallLog.Signatures);

        object? stored = host.GetBufferValue(DwBuffer.Primary, 1L, columnId);

        if (expectStoredValue)
        {
            // se_cst_dw.sru:L243 writes whatever `Time()` produced without checking it, and the ported
            // fallback is midnight - indistinguishable from a genuine "00:00:00", exactly as the legacy
            // is.
            Assert.Equal(TimeValidator.UncoercibleFallback, Assert.IsType<TimeOnly>(stored));
        }
        else
        {
            // The absent value the silent coercion produced, written as absence rather than as a
            // fabricated zero or sentinel date.
            Assert.Null(stored);
        }

        // And nothing was reported anywhere: the protocol still returns the forced 2 and still raises
        // the changed event exactly once. No error path exists on this branch.
        Assert.Equal(1, sink.ChangedRaiseCount);
    }

    // ==============================================================================================
    //  REGION 6 - THE SENTINELS AND THE FORMATS ARE SINGLE SOURCED
    //  Source: dwvaluetoexp.srf:L19, :L25, :L31, :L37, :L43, :L49, :L54-L55, :L60-L61, :L66-L67
    // ==============================================================================================
    //
    //  A duplicated literal is how the emitted text drifts away from what the arm accepts, and the
    //  drift is undetectable from either side alone: a test of `Coerce` never sees the emitted string,
    //  and a test of `Convert` that hard-codes the same literal passes whatever the validator says.
    //  These theories assert the CONNECTION - that the text `Convert` emits is composed from the
    //  owning validator's own constant and its own formatter - so that changing one side without the
    //  other cannot pass.

    /// <summary>
    /// One row per <c>dwvaluetoexp</c> overload: the CLR type key, the legacy locator of the fallback
    /// assignment, and the sentinel that assignment produces.
    /// </summary>
    /// <remarks>
    /// NINE ROWS, FIVE SENTINELS. The five numeric overloads all fall back to the ONE 16-bit
    /// <c>dwNvlNumber()</c>, which is asymmetry A of REGION 0 seen from the caller's side. Grouping
    /// them in one table is what makes the sharing visible: five rows with the same expected value and
    /// five different legacy locators.
    /// </remarks>
    public static TheoryData<string, string, string> NullConversionSentinels =>
        new()
        {
            { "decimal", "dwvaluetoexp.srf:L19", "dwNvlNumber()" },
            { "short", "dwvaluetoexp.srf:L25", "dwNvlNumber()" },
            { "long", "dwvaluetoexp.srf:L31", "dwNvlNumber()" },
            { "double", "dwvaluetoexp.srf:L37", "dwNvlNumber()" },
            { "float", "dwvaluetoexp.srf:L43", "dwNvlNumber()" },
            { "string", "dwvaluetoexp.srf:L49", "dwNvlString()" },
            { "DateTime", "dwvaluetoexp.srf:L55", "dwNvlDateTime()" },
            { "DateOnly", "dwvaluetoexp.srf:L61", "dwNvlDate()" },
            { "TimeOnly", "dwvaluetoexp.srf:L67", "dwNvlTime()" },
        };

    /// <summary>
    /// Converting an absent value emits the owning validator's own sentinel constant - not a copy of
    /// its text.
    /// </summary>
    /// <remarks>
    /// WHAT THIS ROW CAN PROVE, AND WHAT IT CANNOT - stated because an earlier form of it claimed the
    /// larger of the two. It asserted <c>Assert.Same(owned, emitted)</c> and described reference
    /// equality as the proof of single sourcing, reasoning that a <c>Convert</c> overload which had
    /// grown its own literal would satisfy value equality and fail the identity check. **That
    /// reasoning does not hold in C#.** <c>NullLiteralExpression</c> is a <c>const string</c>, so every
    /// reference to it is INLINED at compile time into the referencing assembly, and every equal string
    /// literal in a loaded assembly is INTERNED to one instance by the runtime. A duplicated literal
    /// therefore produces the very same reference, and the assertion could not fail for the reason it
    /// named - it could only fail if the two texts differed, which is value inequality wearing an
    /// identity check's clothing.
    /// <para>
    /// So this row now asserts exactly what it can: the text <c>Convert</c> emits equals the sentinel
    /// the oracle publishes at the cited locator. That is a real check - a validator whose constant
    /// changed fails it - and it is stated as value parity because value parity is what it is.
    /// Single sourcing is a property of the SOURCE rather than of the runtime, so it is asserted
    /// where it is observable, by
    /// <see cref="TheAbsentValueSentinelsAreSingleSourcedFromTheOwningValidators"/>.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(NullConversionSentinels))]
    public void ConvertingAnAbsentValueEmitsTheOwningValidatorSentinel(
        string typeKey,
        string legacyLocator,
        string expectedSentinel)
    {
        string emitted = ConvertAbsentValue(typeKey);

        Assert.Equal(expectedSentinel, emitted);
        Assert.Equal(OwningSentinelConstant(typeKey), emitted);
        Assert.Contains("dwvaluetoexp.srf:L", legacyLocator, StringComparison.Ordinal);
    }

    /// <summary>
    /// Binding an absent value carries the same sentinel on its parity text and reports no value.
    /// </summary>
    /// <remarks>
    /// The <c>Bind</c> surface is a second path to the same sentinel, so it is a second place the text
    /// could drift. Covering it here rather than leaving it to the <c>ValueToExpression</c> suite keeps
    /// the whole sentinel story - producer, converter and binder - in one failing-row-per-overload
    /// table.
    /// <para>
    /// Value parity rather than reference identity, for the reason given in full on
    /// <see cref="ConvertingAnAbsentValueEmitsTheOwningValidatorSentinel"/>: a <c>const string</c> is
    /// inlined at compile time and equal literals are interned, so an identity assertion here could
    /// never distinguish a referenced constant from a duplicated literal. Single sourcing is asserted
    /// against the source text by
    /// <see cref="TheAbsentValueSentinelsAreSingleSourcedFromTheOwningValidators"/>.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(NullConversionSentinels))]
    public void BindingAnAbsentValueCarriesTheSameSentinel(
        string typeKey,
        string legacyLocator,
        string expectedSentinel)
    {
        string parityText = BindAbsentValueParityText(typeKey, out bool hasValue);

        Assert.False(hasValue);
        Assert.Equal(expectedSentinel, parityText);
        Assert.Equal(OwningSentinelConstant(typeKey), parityText);
        Assert.NotEqual(string.Empty, legacyLocator);
    }

    /// <summary>
    /// The five absent-value sentinels are SINGLE SOURCED: <c>Validators/ValueToExpression.cs</c>
    /// returns each owning validator's published constant, and contains no copy of any sentinel's text.
    /// </summary>
    /// <remarks>
    /// THIS IS WHERE SINGLE SOURCING IS ACTUALLY OBSERVABLE, AND IT IS THE SOURCE TEXT. The two
    /// theories above can only compare values, and a duplicated literal is value-identical by
    /// construction - that is the whole reason a duplicate is dangerous rather than merely untidy. The
    /// only artifact in which "referenced" and "copied" differ is the source file, so the source file
    /// is what this reads.
    /// <para>
    /// Two claims, and they are complementary. The POSITIVE one is that each of the five owners appears
    /// as a returned constant, so the emitting overloads genuinely defer to the validator that owns the
    /// spelling. The NEGATIVE one is that no sentinel text appears as a string literal anywhere in the
    /// file, so a future edit cannot quietly inline one beside the reference and leave both present -
    /// which is the state in which the two sides drift apart while every value assertion still passes.
    /// </para>
    /// <para>
    /// The third assertion closes the loop back to the oracle: each owning constant's VALUE equals the
    /// sentinel the row table publishes at its <c>dwvaluetoexp.srf</c> locator. Together the three mean
    /// the emitted text is the owner's constant, the owner's constant is the oracle's spelling, and
    /// there is no second copy of either.
    /// </para>
    /// <para>
    /// Read-only, and nothing is created: the walk locates the repository by the same two root markers
    /// the sibling suites use and reads one file. A file that cannot be located FAILS rather than
    /// skips - standing down would turn a structural claim into a test that reports success without
    /// reading anything.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheAbsentValueSentinelsAreSingleSourcedFromTheOwningValidators()
    {
        // CODE ONLY, AND THE DISTINCTION IS ESSENTIAL RATHER THAN TIDY. The emitting file QUOTES the
        // legacy assignment `sVal = "dwNvlNumber()"` in a comment above each of the nine overloads, and
        // it should: that citation is how a reader ties the C# arm to dwvaluetoexp.srf. Searching the
        // raw text would therefore report the oracle citations as duplication - which is the opposite
        // of the truth, since those comments are what make the single sourcing legible.
        string source = CodeOnlyText(RequireDataServicesSourceText(ValueToExpressionSourcePath));

        foreach (string owner in SentinelOwningValidatorNames)
        {
            string reference = $"return {owner}.{NullLiteralConstantName};";

            Assert.True(
                source.Contains(reference, StringComparison.Ordinal),
                $"'{ValueToExpressionSourcePath}' does not return '{owner}.{NullLiteralConstantName}' " +
                "anywhere, so the sentinel that overload emits is not single sourced from the " +
                "validator that owns its spelling. The emitted text and the accepted text would then " +
                "be free to drift, and no value assertion could see it: a duplicated literal is " +
                "value-identical until the day one of the two copies is edited.");
        }

        foreach ((string typeKey, string sentinel) in DistinctSentinelTexts())
        {
            Assert.Equal(sentinel, OwningSentinelConstant(typeKey));

            Assert.False(
                source.Contains($"\"{sentinel}\"", StringComparison.Ordinal),
                $"'{ValueToExpressionSourcePath}' contains the literal \"{sentinel}\" rather than " +
                "referencing the owning validator's constant. That is precisely the duplication this " +
                "region exists to prevent: both copies satisfy every value assertion in this file " +
                "today, and the emitted text drifts away from what Coerce accepts the moment either " +
                "one is edited alone.");
        }
    }

    /// <summary>
    /// One row per temporal type: the legacy wrapper form, the invariant format constant, the emitted
    /// prefix, and a concrete value with its expected rendering.
    /// </summary>
    /// <remarks>
    /// The format constants are pinned literally because they are the contract between the two
    /// directions: <c>"yyyy-MM-dd"</c> at <c>dwvaluetoexp.srf:L60</c> is what <c>Coerce</c> tries first
    /// and what <c>Convert</c> emits, so a change to either alone breaks the round trip that
    /// <c>Describe("Evaluate(...)")</c> depends on. All three are fixed width and zero padded, which is
    /// why the round trip is exact for every value rather than for most of them.
    /// </remarks>
    public static TheoryData<string, string, string, string> TemporalEmissionFormats =>
        new()
        {
            { "DateOnly", "dwvaluetoexp.srf:L60", "yyyy-MM-dd", "Date('2020-02-29')" },
            { "DateTime", "dwvaluetoexp.srf:L54", "yyyy-MM-dd HH:mm:ss", "DateTime('2020-02-29 10:30:00')" },
            { "TimeOnly", "dwvaluetoexp.srf:L66", "HH:mm:ss", "Time('10:20:30')" },
        };

    /// <summary>
    /// The emitted temporal text is composed from the owning validator's own format constant and its
    /// own formatter.
    /// </summary>
    [Theory]
    [MemberData(nameof(TemporalEmissionFormats))]
    public void TheEmittedTemporalTextIsComposedFromTheOwningValidatorFormat(
        string typeKey,
        string legacyLocator,
        string expectedFormatConstant,
        string expectedEmittedText)
    {
        Assert.Equal(expectedFormatConstant, PublishedTemporalFormat(typeKey));

        string emitted = ConvertSampleTemporalValue(typeKey, out string formattedByOwner);

        Assert.Equal(expectedEmittedText, emitted);

        // The inner text of the emitted literal is EXACTLY the owning validator's formatter output, so
        // the wrapper is the only thing `Convert` contributes. A duplicated format string inside
        // `Convert` would still satisfy the equality above for this sample and would fail here.
        int innerStart = emitted.IndexOf('\'', StringComparison.Ordinal) + 1;
        int innerEnd = emitted.LastIndexOf('\'');
        Assert.Equal(formattedByOwner, emitted[innerStart..innerEnd]);

        Assert.Contains("dwvaluetoexp.srf:L", legacyLocator, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  RESOLUTION HELPERS
    //  Every theory row carries the ORACLE side of its claim and a short key; these helpers resolve the
    //  key to the IMPLEMENTATION side. Keeping the resolution here, in one switch per concern, is what
    //  lets the rows above stay readable and fully serializable.
    // ==============================================================================================

    /// <summary>Resolves a validator key to that validator's published function name.</summary>
    private static string ActualFunctionName(string validatorKey) => validatorKey switch
    {
        DateKey => DateValidator.FunctionName,
        DateTimeKey => DateTimeValidator.FunctionName,
        NumberKey => NumberValidator.FunctionName,
        StringKey => StringValidator.FunctionName,
        TimeKey => TimeValidator.FunctionName,
        _ => throw new ArgumentOutOfRangeException(nameof(validatorKey), validatorKey, "Unknown validator key."),
    };

    /// <summary>Resolves a validator key to that validator's published null literal expression.</summary>
    private static string ActualNullLiteralExpression(string validatorKey) => validatorKey switch
    {
        DateKey => DateValidator.NullLiteralExpression,
        DateTimeKey => DateTimeValidator.NullLiteralExpression,
        NumberKey => NumberValidator.NullLiteralExpression,
        StringKey => StringValidator.NullLiteralExpression,
        TimeKey => TimeValidator.NullLiteralExpression,
        _ => throw new ArgumentOutOfRangeException(nameof(validatorKey), validatorKey, "Unknown validator key."),
    };

    /// <summary>
    /// Resolves a validator key to the DECLARED type of its typed-null member, reading a property for
    /// <c>DateTimeValidator</c> and a method for the other four (asymmetry B of REGION 0).
    /// </summary>
    private static Type TypedNullMemberType(string validatorKey)
    {
        const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        switch (validatorKey)
        {
            case DateKey:
                return DeclaredMethodReturnType(typeof(DateValidator), "NullValue", PublicStatic);

            case DateTimeKey:
            {
                // A PROPERTY, not a method. The other four publish methods.
                PropertyInfo? property = typeof(DateTimeValidator).GetProperty("NullValue", PublicStatic);
                Assert.NotNull(property);
                return property.PropertyType;
            }

            case NumberKey:
                return DeclaredMethodReturnType(typeof(NumberValidator), "NullNumber", PublicStatic);

            case StringKey:
                return DeclaredMethodReturnType(typeof(StringValidator), "NullValue", PublicStatic);

            case TimeKey:
                return DeclaredMethodReturnType(typeof(TimeValidator), "NullValue", PublicStatic);

            default:
                throw new ArgumentOutOfRangeException(nameof(validatorKey), validatorKey, "Unknown validator key.");
        }
    }

    /// <summary>Reads the declared return type of a named public static method.</summary>
    private static Type DeclaredMethodReturnType(Type declaringType, string methodName, BindingFlags flags)
    {
        MethodInfo? method = declaringType.GetMethod(methodName, flags);
        Assert.NotNull(method);
        return method.ReturnType;
    }

    /// <summary>
    /// Invokes the typed-null member and returns whatever it produced, which must be
    /// <see langword="null"/> for all five.
    /// </summary>
    private static object? TypedNullMemberValue(string validatorKey) => validatorKey switch
    {
        DateKey => DateValidator.NullValue(),
        DateTimeKey => DateTimeValidator.NullValue,
        NumberKey => NumberValidator.NullNumber(),
        StringKey => StringValidator.NullValue(),
        TimeKey => TimeValidator.NullValue(),
        _ => throw new ArgumentOutOfRangeException(nameof(validatorKey), validatorKey, "Unknown validator key."),
    };

    /// <summary>
    /// The BOXED default value of the validator's carrier - the value the typed null must not be
    /// mistaken for.
    /// </summary>
    private static object CarrierDefaultValue(string validatorKey) => validatorKey switch
    {
        DateKey => default(DateOnly),
        DateTimeKey => default(DateTime),
        NumberKey => default(short),
        StringKey => string.Empty,
        TimeKey => default(TimeOnly),
        _ => throw new ArgumentOutOfRangeException(nameof(validatorKey), validatorKey, "Unknown validator key."),
    };

    /// <summary>
    /// A stable textual key for a CLR type, used so theory rows can name an expected type without
    /// carrying a <see cref="Type"/> instance.
    /// </summary>
    /// <remarks>
    /// <see cref="Type.ToString"/> rather than <see cref="Type.FullName"/>, because
    /// <c>FullName</c> of a constructed generic is assembly qualified and would embed a version number
    /// in every expected value.
    /// </remarks>
    private static string TypeKey(Type type) => type.ToString();

    /// <summary>Resolves an arm key to the token set that arm publishes.</summary>
    private static IReadOnlyList<string> PublishedArmTokens(string armKey)
    {
        switch (armKey)
        {
            case StringArm:
                return StringValidator.ColTypeTokens;

            case NumberDecimalArm:
                return NumberValidator.DecimalCoercionColumnTypePrefixes;

            case NumberLongArm:
                return NumberValidator.LongCoercionColumnTypePrefixes;

            case DateTimeArm:
                return DateTimeValidator.ColumnTypePrefixes;

            case DateArm:
                return DateValidator.ColumnTypePrefixes;

            case TimeArm:
            {
                // Published as a set rather than an array, so it is materialised in iteration order to
                // give every arm the same shape here. The set holds one token, so order is moot for
                // this arm today; materialising anyway keeps the helper honest if it ever holds two.
                List<string> tokens = [];
                foreach (string token in TimeValidator.ColumnTypeTokens)
                {
                    tokens.Add(token);
                }

                return tokens;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(armKey), armKey, "Unknown arm key.");
        }
    }

    /// <summary>
    /// Resolves a fully qualified constant name to the token text that constant declares.
    /// </summary>
    /// <remarks>
    /// Named rather than reflected, so that a rename of any of these constants breaks the build here
    /// instead of turning into a runtime lookup failure that reads like a missing member.
    /// </remarks>
    private static string PublishedTokenConstantValue(string constantName) => constantName switch
    {
        "StringValidator.ColTypeTokenChar" => StringValidator.ColTypeTokenChar,
        "StringValidator.ColTypeTokenParameterizedChar" => StringValidator.ColTypeTokenParameterizedChar,
        "DateTimeValidator.ColumnTypePrefix" => DateTimeValidator.ColumnTypePrefix,
        "DateValidator.ColumnTypePrefix" => DateValidator.ColumnTypePrefix,
        "TimeValidator.ColumnTypeToken" => TimeValidator.ColumnTypeToken,
        _ => throw new ArgumentOutOfRangeException(nameof(constantName), constantName, "Unknown constant name."),
    };

    /// <summary>
    /// Asks the arm's own predicate whether it claims a column type. Each predicate applies
    /// <c>Left(ColType,5)</c> itself and compares with ordinal equality; none is a prefix test.
    /// </summary>
    private static bool ArmMatches(string armKey, string? colType) => armKey switch
    {
        StringArm => StringValidator.OwnsColType(colType),
        NumberDecimalArm => NumberValidator.IsDecimalCoercionColumnType(colType),
        NumberLongArm => NumberValidator.IsLongCoercionColumnType(colType),
        DateTimeArm => DateTimeValidator.MatchesColumnType(colType),
        DateArm => DateValidator.MatchesColumnType(colType),
        TimeArm => TimeValidator.MatchesColumnType(colType),
        _ => throw new ArgumentOutOfRangeException(nameof(armKey), armKey, "Unknown arm key."),
    };

    /// <summary>
    /// Calls the arm's ADDITIVE reporting coercion and returns its return code, reporting through
    /// <paramref name="hasValue"/> whether the out parameter received a value.
    /// </summary>
    private static long InvokeReportingCoercion(string armKey, string? data, out bool hasValue)
    {
        switch (armKey)
        {
            case NumberDecimalArm:
            {
                long code = NumberValidator.TryCoerceToDecimal(data, out decimal? value);
                hasValue = value.HasValue;
                return code;
            }

            case NumberLongArm:
            {
                long code = NumberValidator.TryCoerceToLong(data, out long? value);
                hasValue = value.HasValue;
                return code;
            }

            case StringArm:
            {
                long code = StringValidator.TryCoerce(data, out string? value);
                hasValue = value is not null;
                return code;
            }

            case DateArm:
            {
                long code = DateValidator.TryCoerce(data, out DateOnly? value);
                hasValue = value.HasValue;
                return code;
            }

            case DateTimeArm:
            {
                long code = DateTimeValidator.TryCoerce(data, out DateTime? value);
                hasValue = value.HasValue;
                return code;
            }

            case TimeArm:
            {
                long code = TimeValidator.TryCoerce(data, out TimeOnly? value);
                hasValue = value.HasValue;
                return code;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(armKey), armKey, "Unknown arm key.");
        }
    }

    /// <summary>
    /// Renders a buffer value invariantly, routing each temporal type through its OWNING validator's
    /// formatter so that this helper cannot become a fourth copy of a format string.
    /// </summary>
    private static string RenderInvariant(object value) => value switch
    {
        string text => text,
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        long integral => integral.ToString(CultureInfo.InvariantCulture),
        short narrow => narrow.ToString(CultureInfo.InvariantCulture),
        DateTime timestamp => DateTimeValidator.FormatExpressionValue(timestamp),
        DateOnly date => DateValidator.FormatValue(date),
        TimeOnly time => TimeValidator.Format(time),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unrenderable buffer value."),
    };

    /// <summary>Converts an absent value of the keyed CLR type through the emitting overload.</summary>
    private static string ConvertAbsentValue(string typeKey) => typeKey switch
    {
        "decimal" => ValueToExpression.Convert(default(decimal?)),
        "short" => ValueToExpression.Convert(default(short?)),
        "long" => ValueToExpression.Convert(default(long?)),
        "double" => ValueToExpression.Convert(default(double?)),
        "float" => ValueToExpression.Convert(default(float?)),
        "string" => ValueToExpression.Convert(default(string?)),
        "DateTime" => ValueToExpression.Convert(default(DateTime?)),
        "DateOnly" => ValueToExpression.Convert(default(DateOnly?)),
        "TimeOnly" => ValueToExpression.Convert(default(TimeOnly?)),
        _ => throw new ArgumentOutOfRangeException(nameof(typeKey), typeKey, "Unknown type key."),
    };

    /// <summary>Binds an absent value of the keyed CLR type and returns its parity text.</summary>
    private static string BindAbsentValueParityText(string typeKey, out bool hasValue)
    {
        switch (typeKey)
        {
            case "decimal":
            {
                var bound = ValueToExpression.Bind(default(decimal?));
                hasValue = bound.HasValue;
                return bound.ParityText;
            }

            case "short":
            {
                var bound = ValueToExpression.Bind(default(short?));
                hasValue = bound.HasValue;
                return bound.ParityText;
            }

            case "long":
            {
                var bound = ValueToExpression.Bind(default(long?));
                hasValue = bound.HasValue;
                return bound.ParityText;
            }

            case "double":
            {
                var bound = ValueToExpression.Bind(default(double?));
                hasValue = bound.HasValue;
                return bound.ParityText;
            }

            case "float":
            {
                var bound = ValueToExpression.Bind(default(float?));
                hasValue = bound.HasValue;
                return bound.ParityText;
            }

            case "string":
            {
                var bound = ValueToExpression.Bind(default(string?));
                hasValue = bound.HasValue;
                return bound.ParityText;
            }

            case "DateTime":
            {
                var bound = ValueToExpression.Bind(default(DateTime?));
                hasValue = bound.HasValue;
                return bound.ParityText;
            }

            case "DateOnly":
            {
                var bound = ValueToExpression.Bind(default(DateOnly?));
                hasValue = bound.HasValue;
                return bound.ParityText;
            }

            case "TimeOnly":
            {
                var bound = ValueToExpression.Bind(default(TimeOnly?));
                hasValue = bound.HasValue;
                return bound.ParityText;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(typeKey), typeKey, "Unknown type key.");
        }
    }

    /// <summary>
    /// The sentinel constant OWNED by the validator responsible for the keyed CLR type. The five
    /// numeric keys all resolve to the one 16-bit sentinel, which is the sharing the legacy has.
    /// </summary>
    /// <summary>
    /// The project-relative path of the file whose single sourcing REGION 6 asserts.
    /// </summary>
    private const string ValueToExpressionSourcePath = "Validators/ValueToExpression.cs";

    /// <summary>The published constant name every sentinel is sourced from.</summary>
    private const string NullLiteralConstantName = "NullLiteralExpression";

    /// <summary>The repository-root markers the source walk anchors on.</summary>
    private const string SolutionMarkerFileName = "PowerFramework.slnx";

    private const string PackageVersionMarkerFileName = "Directory.Packages.props";

    /// <summary>The project directory the emitting file lives in, relative to the repository root.</summary>
    private const string DataServicesProjectRelativeDirectory =
        "services/dataservices-service/PowerFramework.DataServices";

    /// <summary>
    /// The five validators that own a sentinel spelling, by type name as the source spells it.
    /// </summary>
    /// <remarks>
    /// Five names for nine overloads, because the five numeric overloads share the one 16-bit
    /// <c>dwNvlNumber()</c> - asymmetry A of REGION 0, seen once more from the source's side.
    /// </remarks>
    private static readonly string[] SentinelOwningValidatorNames =
    [
        nameof(NumberValidator),
        nameof(StringValidator),
        nameof(DateTimeValidator),
        nameof(DateValidator),
        nameof(TimeValidator),
    ];

    /// <summary>
    /// One representative type key per DISTINCT sentinel text, with that text.
    /// </summary>
    /// <remarks>
    /// Distinct, so the five numeric rows do not assert the same claim five times, and keyed so the
    /// assertion can resolve the owning constant through the same switch every other row uses.
    /// </remarks>
    private static IEnumerable<(string TypeKey, string Sentinel)> DistinctSentinelTexts() =>
    [
        ("decimal", "dwNvlNumber()"),
        ("string", "dwNvlString()"),
        ("DateTime", "dwNvlDateTime()"),
        ("DateOnly", "dwNvlDate()"),
        ("TimeOnly", "dwNvlTime()"),
    ];

    /// <summary>
    /// Strips <c>//</c> line comments from C# source, leaving the code and every string literal intact.
    /// </summary>
    /// <remarks>
    /// A DELIBERATELY SMALL LEXICAL FILTER, not a parser, and its scope is stated so its limits are
    /// visible. It walks each line tracking whether it is inside a double-quoted string, and cuts the
    /// line at the first <c>//</c> found OUTSIDE one - so a comment quoting a sentinel is removed while
    /// a real literal containing <c>//</c> would survive. <c>///</c> is covered by the same rule.
    /// <para>
    /// It does NOT track block comments, because the file it reads contains none - verified: its only
    /// <c>/*</c> occurrences are the two <c>ws_objects/**</c> path fragments inside XML documentation,
    /// which the line rule removes anyway, and a block-comment tracker would mistake those for an
    /// unterminated block and swallow the rest of the file. Nor does it handle verbatim or raw string
    /// literals, of which that file has none either. Should a future edit introduce a block comment
    /// containing a sentinel spelling, this filter would report duplication that is not there - a LOUD
    /// and conservative failure, never a silent pass, which is the safe direction for a check whose
    /// whole purpose is to notice a copy.
    /// </para>
    /// </remarks>
    /// <param name="source">C# source text.</param>
    /// <returns>The same text with line comments removed.</returns>
    private static string CodeOnlyText(string source)
    {
        StringBuilder code = new(source.Length);

        foreach (string line in source.Split('\n'))
        {
            bool inString = false;
            int cut = line.Length;

            for (int index = 0; index < line.Length; index++)
            {
                char current = line[index];

                if (current == '\\' && inString)
                {
                    // Skip the escaped character so an escaped quote does not close the literal.
                    index++;
                    continue;
                }

                if (current == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (!inString && current == '/' && index + 1 < line.Length && line[index + 1] == '/')
                {
                    cut = index;
                    break;
                }
            }

            code.Append(line, 0, cut).Append('\n');
        }

        return code.ToString();
    }

    /// <summary>
    /// Reads one authored source file of the DataServices project, walking up for the repository root.
    /// </summary>
    /// <remarks>
    /// An upward SEARCH rather than a fixed number of parent hops, because the distance between a test
    /// assembly and the repository root is a function of the configuration and the target framework in
    /// the output path, so a fixed count breaks silently when either changes. The walk anchors on BOTH
    /// root markers together with the project directory itself, so it cannot latch onto a same-named
    /// directory elsewhere on the machine. Read-only: nothing is created, written or moved.
    /// </remarks>
    /// <param name="projectRelativePath">Forward-slashed path inside the DataServices project.</param>
    /// <returns>The file's text.</returns>
    private static string RequireDataServicesSourceText(string projectRelativePath)
    {
        string[] projectSegments = DataServicesProjectRelativeDirectory.Split('/');
        string[] fileSegments = projectRelativePath.Split('/');

        // STARTS AT THE EMBEDDED REPOSITORY ROOT WHEN THE BUILD SUPPLIED ONE, so this locator works when
        // the test output sits outside the checkout - `dotnet test --artifacts-path` - where no ancestor
        // of the output directory carries the marker below. The walk itself is unchanged and still
        // verifies that marker, so an absent or stale value simply falls back to the previous start.
        // See TestRepositoryRoot.
        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

        while (candidate is not null)
        {
            string projectDirectory = Path.Combine([candidate.FullName, .. projectSegments]);

            if (Directory.Exists(projectDirectory)
                && File.Exists(Path.Combine(candidate.FullName, SolutionMarkerFileName))
                && File.Exists(Path.Combine(candidate.FullName, PackageVersionMarkerFileName)))
            {
                string file = Path.Combine([projectDirectory, .. fileSegments]);

                Assert.True(
                    File.Exists(file),
                    $"'{projectRelativePath}' was not found at '{file}'. It is the subject of a " +
                    "source-level single-sourcing assertion, so its absence is the finding rather " +
                    "than a reason to stand down.");

                return File.ReadAllText(file);
            }

            // Parent is null at the filesystem root, which is what terminates the walk.
            candidate = candidate.Parent;
        }

        Assert.Fail(
            $"No directory from '{AppContext.BaseDirectory}' up to the filesystem root holds " +
            $"'{DataServicesProjectRelativeDirectory}' together with both repository markers " +
            $"'{SolutionMarkerFileName}' and '{PackageVersionMarkerFileName}', so the authored source " +
            "could not be located. Both probes are read-only; nothing was created.");

        return string.Empty;
    }

    private static string OwningSentinelConstant(string typeKey) => typeKey switch
    {
        "decimal" or "short" or "long" or "double" or "float" => NumberValidator.NullLiteralExpression,
        "string" => StringValidator.NullLiteralExpression,
        "DateTime" => DateTimeValidator.NullLiteralExpression,
        "DateOnly" => DateValidator.NullLiteralExpression,
        "TimeOnly" => TimeValidator.NullLiteralExpression,
        _ => throw new ArgumentOutOfRangeException(nameof(typeKey), typeKey, "Unknown type key."),
    };

    /// <summary>The invariant emission format constant published by the keyed temporal validator.</summary>
    private static string PublishedTemporalFormat(string typeKey) => typeKey switch
    {
        "DateOnly" => DateValidator.CanonicalValueFormat,
        "DateTime" => DateTimeValidator.ExpressionValueFormat,
        "TimeOnly" => TimeValidator.CanonicalFormat,
        _ => throw new ArgumentOutOfRangeException(nameof(typeKey), typeKey, "Unknown type key."),
    };

    /// <summary>
    /// Converts a fixed sample value of the keyed temporal type, also returning the owning validator's
    /// own rendering of that same value so the two can be compared.
    /// </summary>
    private static string ConvertSampleTemporalValue(string typeKey, out string formattedByOwner)
    {
        switch (typeKey)
        {
            case "DateOnly":
            {
                DateOnly value = new(2020, 2, 29);
                formattedByOwner = DateValidator.FormatValue(value);
                return ValueToExpression.Convert(value);
            }

            case "DateTime":
            {
                DateTime value = new(2020, 2, 29, 10, 30, 0, DateTimeKind.Unspecified);
                formattedByOwner = DateTimeValidator.FormatExpressionValue(value);
                return ValueToExpression.Convert(value);
            }

            case "TimeOnly":
            {
                TimeOnly value = new(10, 20, 30);
                formattedByOwner = TimeValidator.Format(value);
                return ValueToExpression.Convert(value);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(typeKey), typeKey, "Unknown type key.");
        }
    }

    // ==============================================================================================
    //  PROTOCOL DOUBLES
    //  The two collaborator interfaces of Domain/ItemChangeProtocol.cs are internal, reached here
    //  through the InternalsVisibleTo grant in PowerFramework.DataServices.csproj. These doubles are
    //  PRIVATE NESTED types deliberately: a public type cannot implement an internal interface, and a
    //  private nested one keeps them out of every other suite in this assembly, where a shared double
    //  would invite exactly the divergence this file is guarding against.
    // ==============================================================================================

    /// <summary>
    /// The session-state double: no event disabled, so the <c>EID_ITEMCHANGE</c> gate at
    /// <c>se_cst_dw.sru:L187</c> does not short-circuit and the coercion becomes reachable.
    /// </summary>
    /// <remarks>
    /// <c>DoItemChange</c> and <c>ItemChangeRetCode</c> are real read-write state, because the protocol
    /// performs the four-step re-entrancy dance of <c>:L192-L196</c> against them - save, set, raise,
    /// restore - and stashes the RAW handler code for <c>ondwnitemvalidationerror</c> to consume
    /// later. Modelling them as inert would silently disable behaviour these tests rely on reaching.
    /// </remarks>
    private sealed class ItemChangeSessionStateDouble : IItemChangeSessionState
    {
        /// <summary>Nothing suppressed: a zero mask disables no event [<c>se_cst_dw.sru:L88-L89</c>].</summary>
        public uint DisabledEvent => 0u;

        /// <inheritdoc />
        public bool DoItemChange { get; set; }

        /// <inheritdoc />
        public long ItemChangeRetCode { get; set; }
    }

    /// <summary>
    /// The event-sink double: the validation handler returns a code OUTSIDE <c>{1,2,3}</c> so that the
    /// protocol takes the <c>case else</c> arm at <c>se_cst_dw.sru:L226</c>, which is the only arm that
    /// reaches the column-type coercion.
    /// </summary>
    private sealed class ItemChangeEventSinkDouble : IItemChangeEventSink
    {
        /// <summary>
        /// How many times the changed event was raised. <c>se_cst_dw.sru:L247</c> raises it
        /// UNCONDITIONALLY, outside the <c>if bEqual</c> guard, so it fires even when no value was
        /// written - which is what the unclaimed-column-type row asserts.
        /// </summary>
        public int ChangedRaiseCount { get; private set; }

        /// <summary>
        /// Returns <c>0</c> - <c>RetCode.OK</c>, and outside the <c>{1,2,3}</c> item-change alphabet -
        /// so dispatch lands on <c>case else</c>. The handler writes nothing to the buffer, which keeps
        /// the <c>:L198-L202</c> equality test true and satisfies the <c>if bEqual</c> guard at
        /// <c>:L229</c>.
        /// </summary>
        public long OnDoItemChange(long row, IDataWindowObject dwo, string? data) => RetCode.OK;

        /// <summary>
        /// Unreached on these rows, because <c>:L204</c> only raises it when the buffer value CHANGED
        /// across the handler and this double changes nothing. Returns <c>RetCode.OK</c>; the oracle
        /// discards this value in any case, as a bare statement at <c>:L207</c>.
        /// </summary>
        public long OnDwnChanging(long row, IDataWindowObject dwo, string? data) => RetCode.OK;

        /// <inheritdoc />
        public void OnDoItemChanged(long row, IDataWindowObject dwo) => ChangedRaiseCount++;
    }
}
