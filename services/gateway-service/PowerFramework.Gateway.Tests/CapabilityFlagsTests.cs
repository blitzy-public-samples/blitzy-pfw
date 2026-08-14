// ==================================================================================================
//  CapabilityFlagsTests - THE EIGHT-BIT CAPABILITY GATE
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.Gateway.Composition.CapabilityFlags
//  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L41-L49
//            docs/README.md, initialization section - "a module not explicitly initialized is
//            unusable, and its native library need not ship"
//
//  WHY THIS TYPE MATTERS MORE THAN ITS SIZE SUGGESTS
//  ------------------------------------------------------------------------------------------------
//  The legacy framework's own module-gating bitmask is the legacy telling us where its seams are. It
//  is the closest thing the estate has to a decomposition intent authored by its own designers, and
//  mapping the eight bits onto the service roster is independent corroboration that the Phase-1 slice
//  is drawn correctly: SQLITE is the ONLY bit with an in-scope consumer, UI and DPIAWARE are
//  DesignSystem, the four engine bits are ScriptBridge, and ORCA is packaging tooling rather than a
//  service at all.
//
//  THE TWO PROPERTIES MOST WORTH SCRUTINY
//  ------------------------------------------------------------------------------------------------
//  1. `AllCapabilitiesMask` DELIBERATELY OMITS `BlinkFastBit`. It is not the bitwise union of the
//     eight declared bits, and that is not an oversight to be tidied: blink.dll and blinkfast.dll are
//     alternative builds of ONE engine, so enabling both is meaningless. Reproduced under C-B.
//  2. `FromConfiguredValue` converts `long` to `uint` UNCHECKED, so the accepted domain is the full
//     unsigned 32-bit range and a negative configured value WRAPS rather than being rejected. This is
//     the C# half of review finding DP-6, whose TypeScript fixture asserted a signed-32-bit ceiling
//     that the implementation does not have.
// ==================================================================================================

using PowerFramework.Gateway.Composition;
using PowerFramework.Gateway.Configuration;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Gateway.Tests;

public sealed class CapabilityFlagsTests
{
    // ==============================================================================================
    //  THE EIGHT BIT VALUES, AGAINST THE ORACLE
    // ==============================================================================================

    [Fact]
    public void TheEightBitsCarryTheirExactLegacyValues()
    {
        // ASSERTED AGAINST LITERALS, NOT AGAINST Enums.
        //
        // The constants are DEFINED as casts of the Enums members, so comparing them back to those
        // members would be a tautology that passes even if both were wrong. These literals come from
        // ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48.
        Assert.Equal(1u, CapabilityFlags.UiBit);
        Assert.Equal(2u, CapabilityFlags.SciterBit);
        Assert.Equal(4u, CapabilityFlags.BlinkBit);
        Assert.Equal(8u, CapabilityFlags.BlinkFastBit);
        Assert.Equal(256u, CapabilityFlags.OrcaBit);
        Assert.Equal(512u, CapabilityFlags.SqliteBit);
        Assert.Equal(1024u, CapabilityFlags.DpiAwareBit);
        Assert.Equal(2048u, CapabilityFlags.WebViewBit);
    }

    [Fact]
    public void TheBitSequenceIsSparseWithATwoHundredFiftySixGapAndThatIsTheLegacysOwn()
    {
        // THE GAP BETWEEN 8 AND 256 IS REAL AND IS PRESERVED.
        //
        // The eight bits are NOT the first eight powers of two: the sequence runs 1, 2, 4, 8 and then
        // jumps to 256, skipping 16, 32, 64 and 128. Renumbering them densely would look tidier and
        // would break every stored characterization comparison and every deployed configuration value.
        //
        // Asserting the gap explicitly documents that the sparseness was observed rather than
        // accidentally introduced.
        Assert.Equal(8u, CapabilityFlags.BlinkFastBit);
        Assert.Equal(256u, CapabilityFlags.OrcaBit);
        Assert.True(CapabilityFlags.OrcaBit / CapabilityFlags.BlinkFastBit == 32u);
    }

    [Fact]
    public void EachBitIsADistinctSinglePowerOfTwoSoTheyCompose()
    {
        uint[] bits =
        [
            CapabilityFlags.UiBit, CapabilityFlags.SciterBit, CapabilityFlags.BlinkBit,
            CapabilityFlags.BlinkFastBit, CapabilityFlags.OrcaBit, CapabilityFlags.SqliteBit,
            CapabilityFlags.DpiAwareBit, CapabilityFlags.WebViewBit,
        ];

        Assert.Equal(CapabilityFlags.DeclaredCapabilityCount, bits.Length);
        Assert.Equal(bits.Length, bits.Distinct().Count());

        foreach (uint bit in bits)
        {
            // EXACTLY ONE BIT SET. A capability whose value had two bits would be indistinguishable
            // from two capabilities being enabled, and the gate is composed with bitwise OR.
            Assert.Equal(1, System.Numerics.BitOperations.PopCount(bit));
        }
    }

    // ==============================================================================================
    //  THE DELIBERATE OMISSION - the single most easily "corrected" defect in this type
    // ==============================================================================================

    [Fact]
    public void AllCapabilitiesMaskDeliberatelyOmitsBlinkFast()
    {
        // 1 + 2 + 4 + 256 + 512 + 1024 + 2048 = 3847. NOTE THE ABSENT 8.
        //
        // enums.sru:L49 sums SEVEN of the eight bits into INIT_FLAG_ENABLE_ALL, omitting
        // INIT_FLAG_ENABLE_BLINKFAST. blink.dll and blinkfast.dll are alternative builds of one
        // engine, so "all" enabling both would be meaningless - and the omission is therefore
        // intentional legacy behaviour preserved under C-B, not a bug to be fixed.
        Assert.Equal(3847u, CapabilityFlags.AllCapabilitiesMask);

        // STATED THE OTHER WAY, WHICH IS THE ASSERTION THAT WOULD CATCH A "TIDY-UP":
        // `All` is NOT the union of every declared bit.
        Assert.NotEqual(CapabilityFlags.KnownCapabilityMask, CapabilityFlags.AllCapabilitiesMask);
        Assert.False(
            Bits.BitTest(CapabilityFlags.AllCapabilitiesMask, CapabilityFlags.BlinkFastBit),
            "INIT_FLAG_ENABLE_ALL must NOT include BLINKFAST - blink and blinkfast are alternative "
                + "builds of one engine. This omission is legacy behaviour reproduced verbatim (C-B).");
    }

    [Fact]
    public void TheAllFlagsInstanceEnablesSevenCapabilitiesAndNotBlinkFast()
    {
        CapabilityFlags all = CapabilityFlags.All;

        Assert.True(all.Ui);
        Assert.True(all.Sciter);
        Assert.True(all.Blink);
        Assert.True(all.Orca);
        Assert.True(all.Sqlite);
        Assert.True(all.DpiAware);
        Assert.True(all.WebView);

        // THE ONE THAT IS FALSE UNDER "ALL".
        Assert.False(all.BlinkFast);

        Assert.Equal(7, all.EnabledCapabilityNames.Count);
    }

    [Fact]
    public void KnownCapabilityMaskIsTheUnionOfAllEightIncludingBlinkFast()
    {
        // 3847 + 8 = 3855. `Known` and `All` are DIFFERENT masks and the difference is exactly
        // BlinkFast.
        //
        // The distinction is load-bearing: `Known` is what decides whether a bit is RECOGNISED, and
        // BlinkFast is a perfectly recognisable capability that simply is not part of "all". Using
        // `All` for recognition would report a BlinkFast-only configuration as carrying an
        // unrecognised bit.
        Assert.Equal(3855u, CapabilityFlags.KnownCapabilityMask);
        Assert.Equal(
            CapabilityFlags.KnownCapabilityMask,
            CapabilityFlags.AllCapabilitiesMask | CapabilityFlags.BlinkFastBit);

        Assert.Equal(8, System.Numerics.BitOperations.PopCount(CapabilityFlags.KnownCapabilityMask));
    }

    [Fact]
    public void ABlinkFastOnlyConfigurationIsRecognisedRatherThanReportedAsUnknown()
    {
        var flags = new CapabilityFlags(CapabilityFlags.BlinkFastBit);

        Assert.True(flags.BlinkFast);

        // THIS IS THE CASE THAT DISTINGUISHES `Known` FROM `All`.
        //
        // Had recognition been implemented against AllCapabilitiesMask, this configuration would report
        // an unrecognised bit - telling an operator their valid BlinkFast setting was a mistake.
        Assert.False(flags.HasUnrecognizedBits);
        Assert.Equal(0u, flags.UnrecognizedBits);
    }

    // ==============================================================================================
    //  DP-6 - THE UNCHECKED PROJECTION AND ITS FULL UNSIGNED DOMAIN
    // ==============================================================================================

    [Theory]
    [InlineData(0L, 0u)]
    [InlineData(1L, 1u)]
    [InlineData(3847L, 3847u)]
    [InlineData(3855L, 3855u)]

    // THE SIGNED-32-BIT CEILING THE REVIEW'S TYPESCRIPT FIXTURE ASSUMED. It is a legal value and is
    // accepted, but it is NOT the boundary of the domain.
    [InlineData(2147483647L, 2147483647u)]

    // ABOVE IT. A configured value that a signed-32-bit model would have rejected passes straight
    // through, because the projection is to `uint`.
    [InlineData(2147483648L, 2147483648u)]
    [InlineData(4294967295L, 4294967295u)]

    // NEGATIVE VALUES WRAP RATHER THAN THROWING - this is what `unchecked` means here.
    [InlineData(-1L, 4294967295u)]
    [InlineData(-2L, 4294967294u)]

    // AND VALUES BEYOND 32 BITS TRUNCATE to their low word rather than throwing.
    [InlineData(4294967296L, 0u)]
    [InlineData(4294967297L, 1u)]
    [InlineData(long.MaxValue, 4294967295u)]
    [InlineData(long.MinValue, 0u)]
    public void AConfiguredValueIsProjectedUncheckedAcrossTheWholeUnsignedDomain(
        long configured,
        uint expected)
    {
        // THIS IS THE C# HALF OF FINDING DP-6.
        //
        // `FromConfiguredValue` is `new CapabilityFlags(unchecked((uint)configuredValue))`. Every one of
        // the 2^32 masks is representable and NOTHING is rejected: a negative value wraps, an
        // over-wide value truncates, and no exception is ever thrown.
        //
        // That is deliberate rather than lax. The legacy flag word is an unsigned long, so rejecting a
        // wrapped value would make a configuration the LEGACY ACCEPTED fail to start - a behavioural
        // change introduced by the refactor rather than a preserved behaviour, which C-B forbids.
        //
        // The end-to-end fixture asserted a `LONG_MAX = 0x7fffffff` ceiling on masks, which contradicts
        // this. Phase 10 splits authored-flag validation from projected-mask validation on that side;
        // this theory is the authoritative statement of what the implementation actually accepts.
        CapabilityFlags flags = CapabilityFlags.FromConfiguredValue(configured);

        Assert.Equal(expected, flags.EffectiveMask);
    }

    [Fact]
    public void NoConfiguredValueWhatsoeverCausesAThrow()
    {
        // SWEPT ACROSS THE BOUNDARIES AND A SPREAD OF INTERIOR VALUES.
        //
        // Asserting "does not throw" over a handful of hand-picked values would leave the claim
        // anecdotal. These are the values where a bounds check, a cast overflow or a sign-handling
        // mistake would surface if one existed.
        long[] probes =
        [
            long.MinValue, long.MinValue + 1, int.MinValue - 1L, int.MinValue, -4294967297L,
            -4294967296L, -4294967295L, -65536L, -256L, -2L, -1L, 0L, 1L, 8L, 3847L, 3855L,
            65535L, 65536L, int.MaxValue - 1L, int.MaxValue, int.MaxValue + 1L,
            4294967294L, 4294967295L, 4294967296L, long.MaxValue - 1, long.MaxValue,
        ];

        foreach (long probe in probes)
        {
            CapabilityFlags flags = CapabilityFlags.FromConfiguredValue(probe);

            // AND THE RESULT IS ALWAYS THE LOW 32 BITS, reinterpreted unsigned - which is exactly what
            // `unchecked((uint)value)` is defined to produce.
            Assert.Equal(unchecked((uint)probe), flags.EffectiveMask);
        }
    }

    [Fact]
    public void AnUnrecognizedBitIsReportedRatherThanRejected()
    {
        // BIT 15 (32768) IS NOT ONE OF THE EIGHT.
        var flags = new CapabilityFlags(CapabilityFlags.SqliteBit | 32768u);

        // THE RECOGNISED BIT STILL WORKS.
        Assert.True(flags.Sqlite);

        // AND THE UNRECOGNISED ONE IS SURFACED, NOT SWALLOWED AND NOT FATAL.
        //
        // Reporting rather than rejecting means a configuration carrying a future bit, or a typo,
        // starts and SAYS SO - which matters because the alternative is a service that refuses to boot
        // on a value it could simply have ignored. The fail-fast posture applies to STRUCTURAL faults,
        // not to a flag word with a spare bit set.
        Assert.True(flags.HasUnrecognizedBits);
        Assert.Equal(32768u, flags.UnrecognizedBits);
    }

    [Fact]
    public void TheUnrecognizedBitsOfAFullMaskAreEverythingOutsideTheKnownEight()
    {
        var flags = new CapabilityFlags(uint.MaxValue);

        // ~3855 over 32 bits.
        Assert.Equal(unchecked((uint)~3855), flags.UnrecognizedBits);
        Assert.True(flags.HasUnrecognizedBits);

        // AND ALL EIGHT KNOWN CAPABILITIES READ AS ENABLED, including BlinkFast - because a full mask
        // sets every bit, and "all bits set" is not the same value as INIT_FLAG_ENABLE_ALL.
        Assert.True(flags.Ui);
        Assert.True(flags.BlinkFast);
        Assert.Equal(8, flags.EnabledCapabilityNames.Count);
    }

    // ==============================================================================================
    //  IsEnabled - AND THE ANY-BIT SEMANTICS INHERITED FROM THE KERNEL
    // ==============================================================================================

    [Theory]
    [InlineData(0u, 1u, false)]
    [InlineData(1u, 1u, true)]
    [InlineData(3847u, 512u, true)]
    [InlineData(3847u, 8u, false)]
    [InlineData(3855u, 8u, true)]
    public void IsEnabledTestsTheBitAgainstTheEffectiveMask(uint mask, uint bit, bool expected)
    {
        Assert.Equal(expected, new CapabilityFlags(mask).IsEnabled(bit));
    }

    [Fact]
    public void IsEnabledWithZeroIsAlwaysFalseBecauseTheKernelTestIsAnAnyBitTest()
    {
        // MEASURED FROM THE KERNEL, NOT ASSUMED.
        //
        // `Bits.BitTest(num, bits)` is `(num & bits) != 0u` - an ANY-bit test. Two consequences follow
        // and neither is obvious from the call site:
        //
        //   * `IsEnabled(0)` is ALWAYS false, even on a full mask, because `x & 0` is 0 for every x.
        //     A caller passing an uninitialised capability constant therefore gets "disabled" rather
        //     than an error, which is worth knowing.
        //
        //   * `IsEnabled(a | b)` is true when EITHER is set, not when both are. The name reads like
        //     "is this capability enabled" and the behaviour is "is any of these enabled".
        Assert.False(new CapabilityFlags(uint.MaxValue).IsEnabled(0u));
        Assert.False(CapabilityFlags.All.IsEnabled(0u));
        Assert.False(CapabilityFlags.None.IsEnabled(0u));
    }

    [Fact]
    public void IsEnabledWithACompositeBitIsAnyRatherThanAll()
    {
        // SQLITE SET, UI NOT SET.
        var flags = new CapabilityFlags(CapabilityFlags.SqliteBit);

        // AN "ALL" TEST WOULD RETURN false HERE. The any-bit semantics return true.
        Assert.True(flags.IsEnabled(CapabilityFlags.SqliteBit | CapabilityFlags.UiBit));

        // Pinned so a future change of `Bits.BitTest` to all-bits semantics - which would look like a
        // correctness fix - fails here instead of silently changing every capability gate in the system.
        Assert.False(flags.IsEnabled(CapabilityFlags.UiBit));
    }

    // ==============================================================================================
    //  THE EIGHT PREDICATES AND THE NAME PROJECTIONS
    // ==============================================================================================

    [Fact]
    public void EachPredicateReadsExactlyItsOwnBitAndNoOther()
    {
        (uint Bit, Func<CapabilityFlags, bool> Predicate, string Name)[] cases =
        [
            (CapabilityFlags.UiBit, static f => f.Ui, "INIT_FLAG_ENABLE_UI"),
            (CapabilityFlags.SciterBit, static f => f.Sciter, "INIT_FLAG_ENABLE_SCITER"),
            (CapabilityFlags.BlinkBit, static f => f.Blink, "INIT_FLAG_ENABLE_BLINK"),
            (CapabilityFlags.BlinkFastBit, static f => f.BlinkFast, "INIT_FLAG_ENABLE_BLINKFAST"),
            (CapabilityFlags.OrcaBit, static f => f.Orca, "INIT_FLAG_ENABLE_ORCA"),
            (CapabilityFlags.SqliteBit, static f => f.Sqlite, "INIT_FLAG_ENABLE_SQLITE"),
            (CapabilityFlags.DpiAwareBit, static f => f.DpiAware, "INIT_FLAG_ENABLE_DPIAWARE"),
            (CapabilityFlags.WebViewBit, static f => f.WebView, "INIT_FLAG_ENABLE_WEBVIEW"),
        ];

        foreach ((uint bit, Func<CapabilityFlags, bool> predicate, string name) in cases)
        {
            // ONE BIT SET IN ISOLATION: the matching predicate is true and every OTHER predicate is
            // false. Testing only the positive would pass even if a predicate read the wrong constant,
            // as long as it read a constant that happened to be set.
            var only = new CapabilityFlags(bit);

            Assert.True(predicate(only), $"{name} should be enabled when only its own bit is set.");

            foreach ((uint otherBit, Func<CapabilityFlags, bool> otherPredicate, string otherName)
                in cases)
            {
                if (otherBit == bit)
                {
                    continue;
                }

                Assert.False(
                    otherPredicate(only),
                    $"{otherName} must be disabled when only {name}'s bit is set.");
            }

            // AND THE NAME PROJECTION AGREES with the predicate.
            Assert.Equal([name], only.EnabledCapabilityNames);
        }
    }

    [Fact]
    public void NoneEnablesNothingAndProjectsAnEmptyNameList()
    {
        CapabilityFlags none = CapabilityFlags.None;

        Assert.Equal(0u, none.EffectiveMask);
        Assert.False(none.Ui);
        Assert.False(none.Sciter);
        Assert.False(none.Blink);
        Assert.False(none.BlinkFast);
        Assert.False(none.Orca);
        Assert.False(none.Sqlite);
        Assert.False(none.DpiAware);
        Assert.False(none.WebView);

        Assert.Empty(none.EnabledCapabilityNames);
        Assert.False(none.HasUnrecognizedBits);
    }

    [Fact]
    public void TheCapabilityNamesArePreservedLegacyIdentifiersInDeclarationOrder()
    {
        // THE SPELLINGS ARE THE LEGACY'S AND THE ORDER IS THE ORACLE'S.
        //
        // These are the SCREAMING_SNAKE identifiers from enums.sru:L41-L48, kept in deliberate
        // departure from .NET naming convention because they appear in serialized payloads, log records
        // and characterization recordings - where a rename would silently invalidate every stored
        // comparison rather than failing loudly.
        //
        // ORDER MATTERS TOO: this list is projected into the /v1/capabilities response, whose schema
        // publishes the same eight in the same order. A consumer indexing positionally would break on a
        // reorder.
        Assert.Equal(
            [
                "INIT_FLAG_ENABLE_UI",
                "INIT_FLAG_ENABLE_SCITER",
                "INIT_FLAG_ENABLE_BLINK",
                "INIT_FLAG_ENABLE_BLINKFAST",
                "INIT_FLAG_ENABLE_ORCA",
                "INIT_FLAG_ENABLE_SQLITE",
                "INIT_FLAG_ENABLE_DPIAWARE",
                "INIT_FLAG_ENABLE_WEBVIEW",
            ],
            CapabilityFlags.KnownCapabilityNames);

        Assert.Equal(CapabilityFlags.DeclaredCapabilityCount, CapabilityFlags.KnownCapabilityNames.Count);
    }

    [Fact]
    public void EnabledCapabilityNamesFollowTheSameOrderAsTheKnownList()
    {
        // A MASK WITH BITS SET OUT OF ORDER still projects in DECLARATION order rather than in
        // bit-value order or insertion order.
        var flags = new CapabilityFlags(
            CapabilityFlags.WebViewBit | CapabilityFlags.UiBit | CapabilityFlags.SqliteBit);

        Assert.Equal(
            ["INIT_FLAG_ENABLE_UI", "INIT_FLAG_ENABLE_SQLITE", "INIT_FLAG_ENABLE_WEBVIEW"],
            flags.EnabledCapabilityNames);

        // AND THE PROJECTION IS ALWAYS A SUBSEQUENCE OF THE KNOWN LIST.
        int lastIndex = -1;
        foreach (string name in flags.EnabledCapabilityNames)
        {
            int index = CapabilityFlags.KnownCapabilityNames
                .Select(static (value, position) => (value, position))
                .First(entry => entry.value == name).position;

            Assert.True(index > lastIndex, "EnabledCapabilityNames must preserve declaration order.");
            lastIndex = index;
        }
    }

    [Fact]
    public void AnUnrecognizedBitContributesNoNameToTheProjection()
    {
        var flags = new CapabilityFlags(CapabilityFlags.SqliteBit | 32768u | 65536u);

        // ONLY RECOGNISED CAPABILITIES ARE NAMED. An unrecognised bit is reported through
        // `UnrecognizedBits` and must not appear as a fabricated name in the capability projection,
        // because a consumer reading the /v1/capabilities response treats those names as a closed set.
        Assert.Equal(["INIT_FLAG_ENABLE_SQLITE"], flags.EnabledCapabilityNames);
        Assert.Equal(32768u | 65536u, flags.UnrecognizedBits);
    }

    // ==============================================================================================
    //  THE ONLY BIT WITH AN IN-SCOPE CONSUMER
    // ==============================================================================================

    [Fact]
    public void SqliteIsTheOnlyCapabilityWithAnInScopePhaseOneConsumer()
    {
        // THIS IS THE CORROBORATION, NOT JUST A FACT.
        //
        // Of the eight bits, exactly one maps to a service built in this phase. UI and DPIAWARE are
        // DesignSystem; SCITER, BLINK, BLINKFAST and WEBVIEW are ScriptBridge; ORCA is the PowerBuilder
        // packager and therefore not a service at all. If a SECOND bit ever acquired an in-scope
        // consumer, the Phase-1 slice would have grown - and that should be a deliberate decision
        // recorded in the AAP rather than something discovered later.
        Assert.Equal(512u, CapabilityFlags.SqliteBit);

        CapabilityFlags sqliteOnly = CapabilityFlags.FromConfiguredValue(Enums.INIT_FLAG_ENABLE_SQLITE);

        Assert.True(sqliteOnly.Sqlite);
        Assert.Single(sqliteOnly.EnabledCapabilityNames);
        Assert.False(sqliteOnly.HasUnrecognizedBits);
    }

    // ==============================================================================================
    //  FromOptions, AND VALUE SEMANTICS
    // ==============================================================================================

    [Fact]
    public void FromOptionsProjectsTheConfiguredFlagsAndRejectsANullOptions()
    {
        var options = new GatewayOptions { CapabilityFlags = Enums.INIT_FLAG_ENABLE_SQLITE };

        Assert.Equal(512u, CapabilityFlags.FromOptions(options).EffectiveMask);

        // A NULL OPTIONS IS A PROGRAMMING ERROR, not a configuration one, so it throws rather than
        // defaulting. Defaulting would silently disable every capability - including SQLITE, whose
        // absence would take Persistence out of the composition with no diagnostic.
        Assert.Throws<ArgumentNullException>(() => CapabilityFlags.FromOptions(null!));
    }

    [Fact]
    public void FromOptionsAgreesWithFromConfiguredValueAcrossTheDomain()
    {
        foreach (long configured in (long[])[long.MinValue, -1L, 0L, 8L, 3847L, 3855L, 4294967295L, long.MaxValue])
        {
            var options = new GatewayOptions { CapabilityFlags = configured };

            // ONE PROJECTION PATH, NOT TWO.
            //
            // `FromOptions` delegates to `FromConfiguredValue`, so the unchecked-domain behaviour is
            // identical however the value arrives. Two independent conversions would be the obvious
            // place for the two paths to diverge on a negative or over-wide value.
            Assert.Equal(
                CapabilityFlags.FromConfiguredValue(configured).EffectiveMask,
                CapabilityFlags.FromOptions(options).EffectiveMask);
        }
    }

    [Fact]
    public void TheDefaultGatewayConfigurationEnablesTheSevenAllCapabilities()
    {
        // THE DEFAULT IS `INIT_FLAG_ENABLE_ALL`, WHICH IS SEVEN RATHER THAN EIGHT.
        //
        // This is where the BlinkFast omission becomes observable in a running service: a Gateway
        // started with no capability configuration reports seven capabilities, and BlinkFast is the
        // absent one. An operator comparing that against the eight declared bits would reasonably
        // suspect a defect, which is why the omission is documented in the capability endpoint's own
        // description as well as here.
        var flags = CapabilityFlags.FromOptions(new GatewayOptions());

        Assert.Equal(CapabilityFlags.AllCapabilitiesMask, flags.EffectiveMask);
        Assert.Equal(7, flags.EnabledCapabilityNames.Count);
        Assert.False(flags.BlinkFast);
        Assert.True(flags.Sqlite);
    }

    [Fact]
    public void TwoFlagsWithTheSameMaskAreEqualAndHashAlike()
    {
        var first = new CapabilityFlags(3847u);
        var second = CapabilityFlags.FromConfiguredValue(3847L);

        // A `readonly record struct` GIVES VALUE EQUALITY over its single member. Asserted because the
        // capability set is compared and cached - a reference-equality slip would make two identical
        // configurations look different and defeat any caching built on it.
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.True(first == second);
        Assert.False(first != second);

        Assert.NotEqual(first, CapabilityFlags.None);
        Assert.NotEqual(first, new CapabilityFlags(3855u));
    }

    [Fact]
    public void TheConstructorPreservesTheMaskExactlyWithoutNormalising()
    {
        // NO MASKING TO THE KNOWN BITS ON THE WAY IN.
        //
        // A constructor that quietly ANDed its argument with KnownCapabilityMask would make
        // `UnrecognizedBits` always zero and destroy the reporting this type exists to provide. The
        // mask is stored verbatim and interpreted on read.
        foreach (uint mask in (uint[])[0u, 1u, 8u, 3847u, 3855u, 32768u, uint.MaxValue])
        {
            Assert.Equal(mask, new CapabilityFlags(mask).EffectiveMask);
        }
    }
}
