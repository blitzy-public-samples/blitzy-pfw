// ==================================================================================================
//  RetCodeAgreementTests - THE WIRE RETURN CODE AND THE IN-PROCESS ONE MUST BE THE SAME NUMBERS
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   common.v1.RetCode.Value      - the wire authority
//            PowerFramework.Shared.Kernel.RetCode - the in-process authority
//  ORACLE    ws_objects/pfw.shared.pbl.src/retcode.sru - what both are transcribed FROM
//
//  WHY THIS FILE EXISTS
//  ------------------------------------------------------------------------------------------------
//  The legacy return-code algebra is transcribed TWICE in this refactor, into two different languages,
//  from one read-only source. That duplication is not avoidable: the wire needs a protobuf enum
//  because a gRPC status detail cannot carry a C# constant, and the shared kernel needs C# constants
//  because in-process code cannot depend on a generated wire type without making every library
//  transitively depend on the contracts project - which is exactly the shared-behaviour back door the
//  architecture forbids.
//
//  So both must exist, and nothing in the build makes them agree. A protobuf enum and a C# constant
//  are compiled by different toolchains from different files, and the divergence would be SILENT: a
//  return code would mean one thing inside Persistence and a different thing after crossing the wire
//  into DataServices, and both sides would be internally consistent while disagreeing with each other.
//  This file is the only thing that makes that a build failure.
//
//  WHY DISAGREEMENT WOULD BE UNUSUALLY DANGEROUS HERE
//  ------------------------------------------------------------------------------------------------
//  Because of the tri-state algebra. `IsSucceeded` tests `>= 0` and `IsFailed` tests `< 0` WITH AN
//  EXPLICIT EXCLUSION OF CANCELLED, so the SIGN of a code decides how every caller in the system
//  classifies it, and two codes are classified as neither. A single sign flip between the two
//  transcriptions would turn a failure into a success at a service boundary - and because a prevention
//  ALREADY reads as a success by design, nobody reviewing the classification logic would find it
//  surprising.
// ==================================================================================================

using System.Reflection;
using Google.Protobuf.Reflection;
using PowerFramework.Contracts.Common.V1;
using Xunit;
using KernelRetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Contracts.Tests;

public sealed class RetCodeAgreementTests
{
    /// <summary>The wire enum: <c>common.v1.RetCode.Value</c>.</summary>
    private static EnumDescriptor WireEnum
    {
        get
        {
            MessageDescriptor message = CommonV1Reflection.Descriptor
                .FindTypeByName<MessageDescriptor>("RetCode")!;
            EnumDescriptor? value = message.EnumTypes.FirstOrDefault(static e => e.Name == "Value");
            Assert.True(value is not null, "common.v1.RetCode.Value was not found.");
            return value!;
        }
    }

    /// <summary>
    /// Every public integral constant on the shared kernel's <c>RetCode</c>, by name.
    /// </summary>
    /// <remarks>
    /// Read by reflection rather than listed, deliberately. A hand-maintained list would be a THIRD
    /// transcription of the same source and would need the same guarantee this file provides - so it
    /// would move the problem rather than solve it. Reflection means a constant added to the kernel is
    /// automatically subject to the agreement check.
    /// </remarks>
    private static Dictionary<string, long> KernelConstants()
    {
        Dictionary<string, long> constants = new(StringComparer.Ordinal);

        foreach (FieldInfo field in typeof(KernelRetCode)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
        {
            if (!field.IsLiteral || field.IsInitOnly)
            {
                continue;
            }

            object? raw = field.GetRawConstantValue();
            long? value = raw switch
            {
                long l => l,
                int i => i,
                short s => s,
                _ => null,
            };

            if (value.HasValue)
            {
                constants[field.Name] = value.Value;
            }
        }

        return constants;
    }

    // ==============================================================================================
    //  THE AGREEMENT ITSELF
    // ==============================================================================================

    [Fact]
    public void EveryWireReturnCodeAgreesNumericallyWithTheKernelConstantOfTheSameName()
    {
        Dictionary<string, long> kernel = KernelConstants();
        List<string> disagreements = [];
        int compared = 0;

        foreach (EnumValueDescriptor wireValue in WireEnum.Values)
        {
            if (!kernel.TryGetValue(wireValue.Name, out long kernelValue))
            {
                // Reported rather than skipped - see the dedicated coverage test below for why an
                // unmatched wire name is a finding rather than an acceptable gap.
                continue;
            }

            compared++;

            if (kernelValue != wireValue.Number)
            {
                disagreements.Add(
                    $"{wireValue.Name}: wire={wireValue.Number} kernel={kernelValue}");
            }
        }

        Assert.Empty(disagreements);

        // A FLOOR ON THE COMPARISON COUNT, so a refactor that accidentally emptied one side cannot make
        // this test pass by comparing nothing. The wire enum carries 41 values; the kernel carries the
        // wider set including the XML and SQLITE families.
        Assert.True(
            compared >= 40,
            $"Only {compared} return codes were compared, which is too few for the agreement to be "
                + "meaningful. One of the two transcriptions is probably not being read.");
    }

    [Fact]
    public void EveryWireReturnCodeHasAKernelConstantOfTheSameName()
    {
        Dictionary<string, long> kernel = KernelConstants();

        string[] wireOnly = WireEnum.Values
            .Select(static value => value.Name)
            .Where(name => !kernel.ContainsKey(name))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        // THE WIRE MAY NOT CARRY A CODE THE KERNEL CANNOT NAME.
        //
        // The direction matters and only one direction is required. A code that can arrive over the
        // wire must be nameable in process, or receiving it leaves a caller holding a number with no
        // constant to compare against - which in practice means a magic literal appears at the call
        // site, and the identifier-preservation discipline is lost at exactly the boundary it matters
        // most.
        //
        // The reverse is fine and expected: the kernel legitimately carries MORE, because retcode.sru
        // also declares the XML_* set and the whole SQLITE_* set including the extended codes computed
        // as base plus n x 256. Those have their own wire enums - XmlParseStatus and SqliteResultCode -
        // rather than being folded into this one, which is why they are absent here.
        Assert.Empty(wireOnly);
    }

    [Fact]
    public void TheTriStateBoundaryValuesAgreeExactlyOnBothSides()
    {
        Dictionary<string, long> kernel = KernelConstants();
        var wire = WireEnum.Values.ToDictionary(
            static value => value.Name, static value => (long)value.Number, StringComparer.Ordinal);

        // THESE FIVE ARE THE ONES A SIGN ERROR WOULD BE INVISIBLE IN, so they are asserted against
        // literals as well as against each other - a triple check rather than a mutual one.
        //
        // Asserting only that the two transcriptions AGREE would pass if both were wrong in the same
        // way, which is a realistic failure when one was copied from the other rather than from
        // retcode.sru. The literals below come from the oracle:
        //   OK/SUCCESS/ALLOW = 0   [retcode.sru - three aliases for one value]
        //   PREVENT          = 1   [:L42]
        //   FAILED           = -1
        //   CANCELED/CANCELLED = -2 [:L44-L45 - both spellings, one value]
        (string Name, long Expected)[] boundary =
        [
            ("OK", 0),
            ("SUCCESS", 0),
            ("ALLOW", 0),
            ("PREVENT", 1),
            ("FAILED", -1),
            ("CANCELED", -2),
            ("CANCELLED", -2),
        ];

        foreach ((string name, long expected) in boundary)
        {
            Assert.True(wire.ContainsKey(name), $"The wire enum does not declare {name}.");
            Assert.True(kernel.ContainsKey(name), $"The kernel does not declare {name}.");

            Assert.Equal(expected, wire[name]);
            Assert.Equal(expected, kernel[name]);
        }
    }

    [Fact]
    public void APreventionIsNonNegativeOnBothSidesSoItClassifiesAsASuccess()
    {
        Dictionary<string, long> kernel = KernelConstants();
        var wire = WireEnum.Values.ToDictionary(
            static value => value.Name, static value => (long)value.Number, StringComparer.Ordinal);

        // THE TRI-STATE HOLE, ASSERTED AS A PROPERTY RATHER THAN AS A VALUE.
        //
        // `IsSucceeded` tests `>= 0` [issucceeded.srf:L11-L13], and PREVENT is 1 - so A PREVENTION IS
        // CLASSIFIED AS A SUCCESS. That is a documented legacy defect preserved under C-B, not an
        // implementation mistake, and it is preserved on BOTH sides because a veto that read as a
        // failure after crossing the wire would change behaviour at the boundary.
        //
        // Asserting the SIGN as well as the value is the point: the value assertion catches a
        // transcription error, and the sign assertion states WHY the value matters, so a future reader
        // who is tempted to "fix" PREVENT to a negative number can see what would break.
        Assert.True(wire["PREVENT"] >= 0);
        Assert.True(kernel["PREVENT"] >= 0);

        // AND CANCELLED IS NEGATIVE YET EXCLUDED FROM FAILURE.
        //
        // `IsFailed` tests `< 0` with an EXPLICIT exclusion of CANCELLED [isfailed.srf:L11-L13], so
        // cancelled fails the success test AND is excluded from the failure test - it is NEITHER. That
        // tri-state hole in a nominally boolean algebra is why a consumer must never infer failure from
        // a non-zero code, which is stated in the ProblemDetails.retCode description in both OpenAPI
        // documents.
        Assert.True(wire["CANCELLED"] < 0);
        Assert.True(kernel["CANCELLED"] < 0);
    }

    [Fact]
    public void TheAliasedZeroAndTheTwoCancelledSpellingsAreAllPreservedOnTheWire()
    {
        var wire = WireEnum.Values.ToDictionary(
            static value => value.Name, static value => (long)value.Number, StringComparer.Ordinal);

        // THE ALIASES SURVIVE, WHICH REQUIRES `allow_alias` AND IS DELIBERATE.
        //
        // retcode.sru declares THREE identifiers for zero - OK, SUCCESS and ALLOW - and TWO spellings of
        // -2 - CANCELED and CANCELLED. Protobuf rejects duplicate values in an enum unless
        // `option allow_alias = true` is set, so carrying them costs an explicit option and it is worth
        // paying: these identifiers appear in log records and characterization recordings, so dropping
        // ALLOW in favour of OK would silently invalidate every stored comparison that used it.
        //
        // The one-spelling-per-value alternative would also force a choice about WHICH spelling is
        // canonical, and there is no evidence in the oracle for making that choice.
        Assert.Equal(wire["OK"], wire["SUCCESS"]);
        Assert.Equal(wire["OK"], wire["ALLOW"]);
        Assert.Equal(wire["CANCELED"], wire["CANCELLED"]);

        // AND THE ALIASES ARE DISTINCT NAMES rather than one name reported repeatedly.
        Assert.Equal(
            5,
            WireEnum.Values.Count(static value =>
                value.Name is "OK" or "SUCCESS" or "ALLOW" or "CANCELED" or "CANCELLED"));
    }

    [Fact]
    public void TheContiguousErrorBlockIsContiguousOnBothSides()
    {
        Dictionary<string, long> kernel = KernelConstants();
        var wire = WireEnum.Values.ToDictionary(
            static value => value.Name, static value => (long)value.Number, StringComparer.Ordinal);

        // THE E_* BLOCK RUNS CONTIGUOUSLY FROM -3 TO -33, AND THE CONTIGUITY IS ITSELF A FACT TO
        // PRESERVE.
        //
        // The 31 names below are the run from ws_objects/pfw.shared.pbl.src/retcode.sru:L46-L76, IN
        // SOURCE ORDER, transcribed from the oracle rather than reconstructed from either .NET
        // transcription - which is the whole point of listing them here. Asserting only that the two
        // transcriptions agree would pass if both were wrong in the same way, and copying one from the
        // other is exactly how that happens.
        //
        // Checking the RUN as well as each value catches a class of error that per-name checks miss: a
        // gap introduced by one transcription and not the other means two ADJACENT codes disagree while
        // every individually-named code still matches its neighbour's expectation.
        //
        // Note that the names are not a tidy family. `E_INVALID_*` runs -4..-11, `E_OUT_OF_*` runs
        // -12..-14, `*_NOT_FOUND` runs -15..-21, and then the block continues through unrelated
        // conditions to -33. That ordering is historical accretion in the oracle and it is preserved
        // exactly, because the VALUES are what appear in stored comparisons.
        string[] block =
        [
            "E_INVALID_ARGUMENT",       // -3
            "E_INVALID_IMAGE",          // -4
            "E_INVALID_OBJECT",         // -5
            "E_INVALID_TYPE",           // -6
            "E_INVALID_TRANSACTION",    // -7
            "E_INVALID_SQL",            // -8
            "E_INVALID_DATA",           // -9
            "E_INVALID_DATAOBJECT",     // -10
            "E_INVALID_HANDLE",         // -11
            "E_OUT_OF_BOUND",           // -12
            "E_OUT_OF_RANGE",           // -13
            "E_OUT_OF_MEMORY",          // -14
            "E_FILE_NOT_FOUND",         // -15
            "E_OBJECT_NOT_FOUND",       // -16
            "E_DATA_NOT_FOUND",         // -17
            "E_FUNCTION_NOT_FOUND",     // -18
            "E_EVENT_NOT_FOUND",        // -19
            "E_MEMBER_NOT_FOUND",       // -20
            "E_VAR_NOT_FOUND",          // -21
            "E_NOT_EXISTS",             // -22
            "E_BUSY",                   // -23
            "E_TIME_OUT",               // -24
            "E_ACCESS_DENIED",          // -25
            "E_WIN32_ERROR",            // -26
            "E_INTERNAL_ERROR",         // -27
            "E_DB_ERROR",               // -28
            "E_HTTP_ERROR",             // -29
            "E_WINHTTP_ERROR",          // -30
            "E_IO_ERROR",               // -31
            "E_SQL_BIND_ARG_FAILED",    // -32
            "E_RETRY",                  // -33
        ];

        List<string> problems = [];

        for (int index = 0; index < block.Length; index++)
        {
            string name = block[index];
            long expected = -3 - index;

            if (!wire.TryGetValue(name, out long wireValue))
            {
                problems.Add($"{name}: absent from the wire enum (oracle says {expected})");
                continue;
            }

            if (!kernel.TryGetValue(name, out long kernelValue))
            {
                problems.Add($"{name}: absent from the kernel (oracle says {expected})");
                continue;
            }

            if (wireValue != expected)
            {
                problems.Add($"{name}: wire={wireValue} but the oracle says {expected}");
            }

            if (kernelValue != expected)
            {
                problems.Add($"{name}: kernel={kernelValue} but the oracle says {expected}");
            }
        }

        Assert.Empty(problems);

        // AND THE RUN IS UNBROKEN AT BOTH ENDS.
        Assert.Equal(-3, wire["E_INVALID_ARGUMENT"]);
        Assert.Equal(-33, wire["E_RETRY"]);
        Assert.Equal(31, block.Length);
    }

    [Fact]
    public void TheThreeSentinelCodesKeepTheirDistantValues()
    {
        Dictionary<string, long> kernel = KernelConstants();
        var wire = WireEnum.Values.ToDictionary(
            static value => value.Name, static value => (long)value.Number, StringComparer.Ordinal);

        // THE GAPS ARE INTENTIONAL AND MUST NOT BE CLOSED.
        //
        // E_NO_SUPPORT is -2000, E_NO_IMPLEMENTATION is -2001 and UNKNOWN is -4000, far below the
        // contiguous block. The distance is what lets the block grow without colliding, so "tidying"
        // these into the run would eventually produce a collision - and a collision between two
        // return codes is indistinguishable from a correct result at every call site.
        //
        // E_NO_IMPLEMENTATION also carries real weight in this refactor: it is the `case else` arm of
        // the paging rewriter's dispatch on database type, so a caller asking for a dialect other than
        // SQL Server or Oracle receives exactly this code.
        (string Name, long Expected)[] sentinels =
        [
            ("E_NO_SUPPORT", -2000),
            ("E_NO_IMPLEMENTATION", -2001),
            ("UNKNOWN", -4000),
        ];

        foreach ((string name, long expected) in sentinels)
        {
            Assert.Equal(expected, wire[name]);
            Assert.Equal(expected, kernel[name]);
        }
    }

    [Fact]
    public void TheXmlAndSqliteFamiliesHaveTheirOwnWireEnumsRatherThanBeingFoldedIn()
    {
        FileDescriptor common = CommonV1Reflection.Descriptor;

        // THREE SEPARATE CODE SPACES, BECAUSE THEY OVERLAP NUMERICALLY.
        //
        // retcode.sru declares the framework codes, the XML_* set and the full SQLITE_* set. They CANNOT
        // share one enum: XML_OK and SQLITE_OK are both 0, XML_E_IO_ERROR is 2 while SQLITE_INTERNAL is
        // also 2, and the SQLITE extended codes are computed as base plus n x 256 so they collide with
        // nothing in particular but overlap the XML range freely.
        //
        // Folding them together would require renumbering, which would break every stored comparison.
        // Keeping them separate means a value's meaning is determined by WHICH enum carries it, which is
        // how the legacy treats them too - the caller always knows whether it called an XML parser or
        // SQLite.
        foreach (string messageName in (string[])["RetCode", "XmlParseStatus", "SqliteResultCode"])
        {
            MessageDescriptor? message = common.FindTypeByName<MessageDescriptor>(messageName);
            Assert.True(message is not null, $"common.v1.{messageName} is missing.");

            EnumDescriptor? values = message!.EnumTypes.FirstOrDefault(static e => e.Name == "Value");
            Assert.True(values is not null, $"common.v1.{messageName}.Value is missing.");
            Assert.NotEmpty(values!.Values);
        }
    }

    [Fact]
    public void TheSqliteExtendedCodesFollowTheBasePlusMultipleOfTwoFiftySixRule()
    {
        MessageDescriptor sqlite = CommonV1Reflection.Descriptor
            .FindTypeByName<MessageDescriptor>("SqliteResultCode")!;
        EnumDescriptor values = sqlite.EnumTypes.First(static e => e.Name == "Value");

        var byName = values.Values.ToDictionary(
            static value => value.Name, static value => value.Number, StringComparer.Ordinal);

        // THE EXTENDED CODES ARE COMPUTED, NOT ARBITRARY, AND THE ARITHMETIC IS CHECKABLE.
        //
        // SQLite composes an extended code as the primary code plus n x 256. So SQLITE_IOERR is 10 and
        // SQLITE_IOERR_READ is 266 = 10 + 1 x 256; SQLITE_IOERR_SHORT_READ is 522 = 10 + 2 x 256.
        // Verifying the arithmetic rather than the literals is what catches a transcription slip in a
        // set this large - there are dozens of these values and a single wrong digit in one of them
        // would otherwise be invisible.
        (string Extended, string Primary, int Multiple)[] cases =
        [
            ("SQLITE_IOERR_READ", "SQLITE_IOERR", 1),
            ("SQLITE_IOERR_SHORT_READ", "SQLITE_IOERR", 2),
            ("SQLITE_IOERR_WRITE", "SQLITE_IOERR", 3),
            ("SQLITE_READONLY_RECOVERY", "SQLITE_READONLY", 1),
            ("SQLITE_BUSY_RECOVERY", "SQLITE_BUSY", 1),
            ("SQLITE_CONSTRAINT_NOTNULL", "SQLITE_CONSTRAINT", 5),
            ("SQLITE_ABORT_ROLLBACK", "SQLITE_ABORT", 2),
        ];

        foreach ((string extended, string primary, int multiple) in cases)
        {
            Assert.True(byName.ContainsKey(extended), $"{extended} is missing from the wire enum.");
            Assert.True(byName.ContainsKey(primary), $"{primary} is missing from the wire enum.");

            Assert.Equal(byName[primary] + (multiple * 256), byName[extended]);
        }

        // SQLITE_ROW=100 AND SQLITE_DONE=101 ARE NOT ERRORS, and their values sit deliberately above the
        // primary error range so a caller stepping a statement can test for them without a lookup table.
        Assert.Equal(100, byName["SQLITE_ROW"]);
        Assert.Equal(101, byName["SQLITE_DONE"]);
    }
}
