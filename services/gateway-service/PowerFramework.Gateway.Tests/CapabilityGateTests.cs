// ======================================================================================================
// CapabilityGateTests.cs
// The Gateway's eight-bit capability gate, and the single most important regression assertion in this
// service: INIT_FLAG_ENABLE_ALL is 3847 and NOT 3855.
// ======================================================================================================
//
// WHAT THIS FILE IS FOR
//
//   ws_objects/pfw.shared.pbl.src/enums.sru:L41-L49 declares eight capability bits and then declares
//   their aggregate as a SEVEN-term sum. That aggregate is the legacy framework telling us where its own
//   seams are - a module that is not explicitly initialized is unusable *and its native library need not
//   ship* [docs/README.md, the 高级初始化 section at :L26-L30] - so mapping the bitmask onto the service
//   roster is independent corroboration that the Phase-1 slice is drawn correctly. Gateway therefore
//   reproduces the gate as configuration, and this file is the standing proof that it reproduces it
//   FAITHFULLY rather than tidily.
//
//   Precision matters here more than breadth. The omission of INIT_FLAG_ENABLE_BLINKFAST from the
//   aggregate is the most easily "corrected" characteristic in the whole service, and correcting it would
//   be a silent behavioural change that no other test in this folder would catch.
//
// THE ASSERTION THIS FILE EXISTS FOR (constraint C-B, documented at the point of decision per C-K)
//
//   3847 is written as a BARE LITERAL, and 3855 is asserted to be WRONG, and the two assertions are
//   inseparable. The reason is mechanical rather than stylistic: a test that recomputed the aggregate by
//   summing or OR-ing the eight declared bits would produce 3855, agree with a "corrected" constant, and
//   validate nothing at all. The literal is the only form of the expectation that can fail when the
//   constant drifts, and an assertion that cannot fail is not a regression test.
//
//   Why the omission is deliberate and must never be "fixed": blink.dll and blinkfast.dll are
//   ALTERNATIVE BUILDS OF ONE ENGINE, so enabling both is meaningless. 3855 is exactly the value a
//   well-meaning correction produces, which is why it is named in the assertions rather than left
//   implicit - a future reader must not be able to mistake 3847 for an arithmetic mistake.
//
// HOW THIS FILE IS SCOPED AGAINST ITS SIBLINGS (no assertion is duplicated for its own sake)
//
//   CapabilityFlagsTests.cs owns the CapabilityFlags value type itself - its bit constants, its unchecked
//   projection across the whole unsigned domain, its equality. AuthorizationTests.cs owns the C-G posture
//   of /v1/capabilities (challenged anonymously, answered with a credential) and the return-code algebra
//   of the framework lifecycle. THIS file owns the gate as a GATE: the kernel constants measured against
//   their legacy literals, the aggregate's membership, the configuration key that drives it, the wire
//   projection the published contract describes, the Phase-1 destination of each bit, and the fact that
//   the configured mask is the one that actually reaches the framework boundary.
//
// EVERY TEST HERE RUNS WITH NO DATABASE, NO CONTAINER, NO SIBLING SERVICE AND NO NETWORK
//
//   Most of the file is pure: Composition/CapabilityFlags.cs is a total function of its input mask with
//   no I/O and no host dependency, so its matrix needs no host at all. The tests that do need a host take
//   the shared GatewayTestHostFixture declared in AuthorizationTests.cs through IClassFixture<> - a CLASS
//   fixture rather than a collection fixture, so tests in other classes stay parallel with these - and no
//   new fixture file is added, because this folder carries a fixed small set of files by design.
//
//   That fixture's substitution of the native lifecycle seam is MANDATORY rather than convenient.
//   pfwInitialize and pfwFinalize are both declared `from function_object native "pfw.dll"`
//   [ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L3, ws_objects/pfw.base.pbl.src/pfwfinalize.srf:L3] -
//   a closed-source Win32 binary with no source in this repository and no presence whatsoever in the
//   Linux container this service ships as. Without the substitution the host could not start and every
//   test in the folder would fail at once. Substituting it also makes the calls OBSERVABLE, which is what
//   lets the last section below assert that the configured gate is the mask the boundary receives.
//
//   The flags parameter is 32-bit `uint` because the legacy parameter is declared
//   `readonly unsignedlong` [pfwinitialize.srf:L8] and PowerBuilder's unsignedlong is 32 bits wide - not
//   because 32 bits happened to be convenient.
//
// DETERMINISM
//
//   Nothing here reads a clock, a GUID or a random source. The fixture freezes TimeProvider anyway, and
//   the two host-backed sections assert only values that are functions of configuration. Tests are
//   order-independent: the one section that needs a DIFFERENT capability mask builds its own short-lived
//   host rather than mutating the shared fixture's settings, because mutating shared settings after the
//   host has booted would make the outcome depend on which test ran first.
//
// CONSTRAINT C-D IS HONOURED BY CONSTRUCTION
//
//   Four of the eight bits name capability areas that belong to DEFERRED services. They appear here ONLY
//   as strings and as documentation. No type belonging to DesignSystem, Documents, Integration or
//   ScriptBridge is imported, instantiated or referenced anywhere in this file; none exists, and none may
//   be created in this phase. CapabilityDestination is Gateway's own published enum - naming a
//   destination is metadata about the eventual system, not an implementation of it.
//
// LEGACY SOURCES ARE REFERENCE ONLY (constraint C-C)
//
//   ws_objects/** is read-only and is the behavioural oracle. enums.sru, ws_objects/pfw.pbl.src/pfw.sra,
//   pfwinitialize.srf, pfwfinalize.srf, n_initializer.sru, the 47 w_test_*.srw windows and docs/README.md
//   were read for VALUES and SCENARIOS only. Not one line of any of them is translated into a test here,
//   and not one of them is edited, moved or reformatted. ws_objects/pfw.pbl.src/pfw.sra is always written
//   out in full because TWO distinct files in this repository are named pfw.sra and the other,
//   ws_objects/pfw.pack.pbl.src/pfw.sra, is the packager rather than the framework application.
//
// WHAT IS DELIBERATELY NOT ASSERTED
//
//   No latency, throughput, availability or timing budget: the repository publishes no service-level
//   agreement, no latency budget and no throughput target anywhere, so there is no baseline to compare
//   against and inventing one would be a fabricated requirement. No user interface, no component library
//   and no design system: the capability bits NAME user-interface concerns, but that is metadata about a
//   deferred service rather than a presentation surface, and none is created in this phase. No storage of
//   any kind, not even in memory - Gateway holds no storage provider (constraint C-E).
//
// USER RULES
//
//   review_rules returns exactly one line - "No user rules provided." That is the whole document, and it
//   is a finding rather than a loading failure. No rule is invented to fill the gap and its absence is not
//   read as licence to lower the bar: the binding constraints are the plan's own non-rule constraints
//   (C-A to C-L) and its enterprise-standard baseline, and this file names the ones it honours where it
//   honours them.
// ======================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Mime;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Gateway.Composition;
using PowerFramework.Gateway.Configuration;
using PowerFramework.Gateway.Endpoints;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// Verifies the framework's eight-bit capability gate: the legacy constant values, the deliberate
/// omission of <c>INIT_FLAG_ENABLE_BLINKFAST</c> from the aggregate, the gate expressed as
/// configuration, its projection over <c>GET /v1/capabilities</c>, the Phase-1 destination of each bit,
/// and the fact that the configured mask is the one handed to the framework's lifecycle boundary.
/// </summary>
/// <param name="host">
/// The shared in-process test host declared in <c>AuthorizationTests.cs</c> and consumed here as a CLASS
/// fixture. It substitutes the native <c>pfw.dll</c> lifecycle seam, which is what allows a Linux
/// container with no PowerBuilder runtime to start the composition root at all.
/// </param>
public sealed class CapabilityGateTests(GatewayTestHostFixture host) : IClassFixture<GatewayTestHostFixture>
{
    /// <summary>The published capability projection, contract C-09.</summary>
    private const string CapabilitiesRoute = "/v1/capabilities";

    /// <summary>
    /// The number of members <c>CapabilityReport</c> serializes. The published schema closes the object
    /// with <c>additionalProperties: false</c>, so the count is part of the contract rather than an
    /// implementation detail.
    /// </summary>
    private const int CapabilityReportMemberCount = 4;

    /// <summary>
    /// The number of members each <c>Capability</c> entry serializes, likewise closed by
    /// <c>additionalProperties: false</c> in <c>shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml</c>.
    /// </summary>
    private const int CapabilityMemberCount = 4;

    /// <summary>
    /// The size of the declared capability set. Closed at eight because the legacy declaration is closed:
    /// <c>ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48</c> declares exactly eight bits, and a ninth
    /// would be a change to the published contract rather than a new configuration value.
    /// </summary>
    private const int DeclaredCapabilityCount = 8;

    // --------------------------------------------------------------------------------------------------
    //  1. THE ASSERTION THIS FILE EXISTS FOR
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>INIT_FLAG_ENABLE_ALL</c> is 3847, and it is emphatically not 3855.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY CHARACTERISTIC - DO NOT "CORRECT" (constraint C-B).
    /// <c>ws_objects/pfw.shared.pbl.src/enums.sru:L49</c> declares the aggregate as a SEVEN-term sum:
    /// <c>UI</c> (1) + <c>SCITER</c> (2) + <c>BLINK</c> (4) + <c>ORCA</c> (256) + <c>SQLITE</c> (512) +
    /// <c>DPIAWARE</c> (1024) + <c>WEBVIEW</c> (2048) = <b>3847</b>.
    /// <c>INIT_FLAG_ENABLE_BLINKFAST</c> (8) is <b>not one of the terms</b>.
    /// </para>
    /// <para>
    /// The omission is deliberate, not a typo: <c>blink.dll</c> and <c>blinkfast.dll</c> are ALTERNATIVE
    /// BUILDS OF ONE ENGINE, so enabling both is meaningless. <b>3855 is precisely the value a
    /// well-meaning "correction" produces</b> - it is the bitwise union of all eight declared bits - which
    /// is why it is named here explicitly instead of being left implicit.
    /// </para>
    /// <para>
    /// THE EXPECTATION IS A BARE LITERAL ON PURPOSE, and that is the whole point of this test. Recomputing
    /// the aggregate from the eight bits would yield 3855, so such a test would agree with a corrected
    /// constant and fail to notice the drift it exists to catch. C-B forbids correcting legacy behaviour
    /// and C-K requires the decision documented where the decision is made, so both halves are recorded
    /// here rather than in a design note somewhere else.
    /// </para>
    /// <para>
    /// This is also the value the legacy composition root actually initializes with:
    /// <c>ws_objects/pfw.pbl.src/pfw.sra:L91</c> calls <c>pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)</c>,
    /// so the runtime-effective capability set genuinely is the <c>BLINKFAST</c>-omitting sum rather than
    /// an all-bits mask.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheAggregateCapabilityConstantIs3847AndDeliberatelyNot3855()
    {
        // The literal. Never a recomputed sum - see the remarks above for why that distinction is load
        // bearing rather than pedantic.
        Assert.Equal(3847L, Enums.INIT_FLAG_ENABLE_ALL);

        // Its inseparable other half: the value a "correction" would produce is asserted to be WRONG.
        Assert.NotEqual(3855L, Enums.INIT_FLAG_ENABLE_ALL);

        // The gate's own view of the same constant, so a cast or a re-declaration cannot drift away from
        // the kernel catalogue without failing here too.
        Assert.Equal(3847u, CapabilityFlags.AllCapabilitiesMask);
        Assert.NotEqual(3855u, CapabilityFlags.AllCapabilitiesMask);
    }

    /// <summary>
    /// The aggregate omits the fast-engine bit, while the union of all eight declared bits includes it -
    /// and those two facts are different facts.
    /// </summary>
    /// <remarks>
    /// The CONSTANT <c>INIT_FLAG_ENABLE_BLINKFAST</c> is correct and must exist; it is only its ABSENCE
    /// FROM THE AGGREGATE that is the preserved characteristic. Conflating the two would either delete a
    /// declared capability or "repair" the aggregate, and both are behavioural changes C-B forbids. The
    /// difference between the two masks is therefore asserted to be exactly the fast-engine bit and
    /// nothing else.
    /// </remarks>
    [Fact]
    public void TheFastEngineBitIsAbsentFromTheAggregateButPresentInTheUnionOfAllEight()
    {
        // Absent from the aggregate. Bits.BitTest is the kernel's ANY-bit test, ported from
        // ws_objects/pfw.common.pbl.src/bittest.srf.
        Assert.False(
            Bits.BitTest(CapabilityFlags.AllCapabilitiesMask, CapabilityFlags.BlinkFastBit),
            "INIT_FLAG_ENABLE_ALL must NOT carry INIT_FLAG_ENABLE_BLINKFAST: enums.sru:L49 declares a "
                + "seven-term sum, and blink.dll and blinkfast.dll are alternative builds of one engine.");

        // Present in the union of the eight, which is a different mask with a different value.
        Assert.True(Bits.BitTest(CapabilityFlags.KnownCapabilityMask, CapabilityFlags.BlinkFastBit));
        Assert.Equal(3855u, CapabilityFlags.KnownCapabilityMask);

        // The difference between the two is EXACTLY the fast-engine bit - no more, no less. This is the
        // arithmetic form of "3855 is what a correction would produce".
        Assert.Equal(
            CapabilityFlags.BlinkFastBit,
            Bits.BitAnd(CapabilityFlags.KnownCapabilityMask, Bits.BitNot(CapabilityFlags.AllCapabilitiesMask)));

        // And the bit itself is a declared capability with its legacy value, so nothing above deleted it.
        Assert.Equal(8L, Enums.INIT_FLAG_ENABLE_BLINKFAST);
        Assert.Equal(8u, CapabilityFlags.BlinkFastBit);
        Assert.Contains(nameof(Enums.INIT_FLAG_ENABLE_BLINKFAST), CapabilityFlags.KnownCapabilityNames);
    }

    // --------------------------------------------------------------------------------------------------
    //  2. THE EIGHT DECLARED BITS, VALUE BY VALUE
    //
    //  Table driven with member data, which is the plan's mandated test shape and the only form that makes
    //  a value-by-value matrix both exhaustive and readable. Each row carries the LITERAL the legacy
    //  declares alongside the ported constants, so the expectation never comes from the same expression as
    //  the actual.
    //
    //  The row identifiers are produced with nameof over the kernel catalogue rather than declared as
    //  local constants. That is not a stylistic preference: the root .editorconfig scopes its CA1707 and
    //  IDE1006 suppressions to seven named product files and no test file is among them, while
    //  Directory.Build.props sets TreatWarningsAsErrors - so a locally declared INIT_FLAG_ENABLE_UI would
    //  be a BUILD ERROR rather than a warning. nameof yields the preserved spelling with no declaration.
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The eight declared capability bits: declaration position, preserved identifier, the ported kernel
    /// constant, the gate's own bit constant, and the value
    /// <c>ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48</c> literally declares.
    /// </summary>
    /// <remarks>
    /// The set is sparse - 1, 2, 4, 8, 256, 512, 1024, 2048 - rather than the first eight powers of two.
    /// Bits 16, 32, 64 and 128 are simply not declared, and that gap is the legacy's own; it is reproduced
    /// rather than closed up, because the numeric values appear in serialized payloads and stored
    /// characterization comparisons.
    /// </remarks>
    public static TheoryData<int, string, long, uint, long> DeclaredCapabilityBits => new()
    {
        { 0, nameof(Enums.INIT_FLAG_ENABLE_UI), Enums.INIT_FLAG_ENABLE_UI, CapabilityFlags.UiBit, 1L },
        { 1, nameof(Enums.INIT_FLAG_ENABLE_SCITER), Enums.INIT_FLAG_ENABLE_SCITER, CapabilityFlags.SciterBit, 2L },
        { 2, nameof(Enums.INIT_FLAG_ENABLE_BLINK), Enums.INIT_FLAG_ENABLE_BLINK, CapabilityFlags.BlinkBit, 4L },
        { 3, nameof(Enums.INIT_FLAG_ENABLE_BLINKFAST), Enums.INIT_FLAG_ENABLE_BLINKFAST, CapabilityFlags.BlinkFastBit, 8L },
        { 4, nameof(Enums.INIT_FLAG_ENABLE_ORCA), Enums.INIT_FLAG_ENABLE_ORCA, CapabilityFlags.OrcaBit, 256L },
        { 5, nameof(Enums.INIT_FLAG_ENABLE_SQLITE), Enums.INIT_FLAG_ENABLE_SQLITE, CapabilityFlags.SqliteBit, 512L },
        { 6, nameof(Enums.INIT_FLAG_ENABLE_DPIAWARE), Enums.INIT_FLAG_ENABLE_DPIAWARE, CapabilityFlags.DpiAwareBit, 1024L },
        { 7, nameof(Enums.INIT_FLAG_ENABLE_WEBVIEW), Enums.INIT_FLAG_ENABLE_WEBVIEW, CapabilityFlags.WebViewBit, 2048L },
    };

    /// <summary>
    /// Every declared bit carries the exact value, identifier spelling and declaration position the legacy
    /// gives it.
    /// </summary>
    /// <param name="declarationIndex">The bit's zero-based position in the legacy declaration order.</param>
    /// <param name="capabilityIdentifier">The preserved <c>SCREAMING_SNAKE</c> identifier.</param>
    /// <param name="kernelValue">The value of the ported constant in the shared kernel catalogue.</param>
    /// <param name="gateBit">The value of the gate's own bit constant.</param>
    /// <param name="expectedValue">The literal declared at <c>enums.sru:L41-L48</c>.</param>
    /// <remarks>
    /// <c>BLINKFAST</c> is deliberately IN this matrix. The constant is correct and must exist; only its
    /// absence from the aggregate is the preserved characteristic, and section 1 owns that. Conflating the
    /// two would delete a declared capability.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeclaredCapabilityBits))]
    public void EveryDeclaredBitHasItsLegacyValueIdentifierAndPosition(
        int declarationIndex,
        string capabilityIdentifier,
        long kernelValue,
        uint gateBit,
        long expectedValue)
    {
        // The ported catalogue against the legacy literal.
        Assert.Equal(expectedValue, kernelValue);

        // The gate's 32-bit view of the same bit. The width is the legacy's: pfwinitialize.srf:L8 declares
        // the parameter `readonly unsignedlong`, and PowerBuilder's unsignedlong is 32 bits wide.
        Assert.Equal((uint)expectedValue, gateBit);

        // Exactly one bit is set. A capability bit that was not a single power of two could not be composed
        // with the others, and composition is how the mask is built.
        Assert.Equal(1, System.Numerics.BitOperations.PopCount(gateBit));

        // The identifier and its position in the published vocabulary. Order is part of the contract: the
        // /v1/capabilities response projects this list, and its schema fixes the member order.
        Assert.Equal(capabilityIdentifier, CapabilityFlags.KnownCapabilityNames[declarationIndex]);
    }

    /// <summary>
    /// A mask carrying one declared bit enables exactly that capability and no other.
    /// </summary>
    /// <param name="declarationIndex">
    /// The bit's declaration position, range-checked here so a malformed row is caught rather than silently
    /// carried; the rows are shared with the matrix above.
    /// </param>
    /// <param name="capabilityIdentifier">The preserved identifier expected in the projection.</param>
    /// <param name="kernelValue">The kernel constant used as the configured value.</param>
    /// <param name="gateBit">The gate bit expected to be reported as enabled.</param>
    /// <param name="expectedValue">The literal the legacy declares for this bit.</param>
    /// <remarks>
    /// This is the isolation property that makes the gate meaningful: enabling SQLite must not
    /// incidentally enable a deferred capability area. It is asserted from the CONFIGURED value, so the
    /// unchecked long-to-uint projection that <c>FromConfiguredValue</c> performs is on the path too.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeclaredCapabilityBits))]
    public void ASingleConfiguredBitEnablesExactlyItsOwnCapability(
        int declarationIndex,
        string capabilityIdentifier,
        long kernelValue,
        uint gateBit,
        long expectedValue)
    {
        Assert.InRange(declarationIndex, 0, DeclaredCapabilityCount - 1);

        CapabilityFlags gate = CapabilityFlags.FromConfiguredValue(kernelValue);

        Assert.Equal((uint)expectedValue, gate.EffectiveMask);
        Assert.True(gate.IsEnabled(gateBit));

        // Exactly one name is projected, and it is this bit's own.
        Assert.Equal([capabilityIdentifier], gate.EnabledCapabilityNames);

        // A declared bit is never reported as unrecognized, including the fast-engine bit that the
        // aggregate omits: omitted from the aggregate is not the same as undeclared.
        Assert.False(gate.HasUnrecognizedBits);
        Assert.Equal(0u, gate.UnrecognizedBits);
    }

    // --------------------------------------------------------------------------------------------------
    //  3. MEMBERSHIP OF THE AGGREGATE, AS DATA
    //
    //  Section 1 asserts the aggregate's value. This section asserts its COMPOSITION bit by bit, so the
    //  answer to "which of the eight is missing" is data rather than prose. Exactly one row expects false.
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether each declared bit is a term of the seven-term aggregate at
    /// <c>ws_objects/pfw.shared.pbl.src/enums.sru:L49</c>.
    /// </summary>
    /// <remarks>
    /// The single <see langword="false"/> row is <c>INIT_FLAG_ENABLE_BLINKFAST</c>. Every expectation is a
    /// literal read off the legacy declaration rather than computed from the aggregate.
    /// </remarks>
    public static TheoryData<string, uint, bool> AggregateMembership => new()
    {
        { nameof(Enums.INIT_FLAG_ENABLE_UI), CapabilityFlags.UiBit, true },
        { nameof(Enums.INIT_FLAG_ENABLE_SCITER), CapabilityFlags.SciterBit, true },
        { nameof(Enums.INIT_FLAG_ENABLE_BLINK), CapabilityFlags.BlinkBit, true },

        // THE ONE OMISSION, PRESERVED VERBATIM (C-B). blink.dll and blinkfast.dll are alternative builds
        // of one engine, so enabling both is meaningless. Flipping this row to true is exactly the
        // "correction" that would make the aggregate 3855.
        { nameof(Enums.INIT_FLAG_ENABLE_BLINKFAST), CapabilityFlags.BlinkFastBit, false },

        { nameof(Enums.INIT_FLAG_ENABLE_ORCA), CapabilityFlags.OrcaBit, true },
        { nameof(Enums.INIT_FLAG_ENABLE_SQLITE), CapabilityFlags.SqliteBit, true },
        { nameof(Enums.INIT_FLAG_ENABLE_DPIAWARE), CapabilityFlags.DpiAwareBit, true },
        { nameof(Enums.INIT_FLAG_ENABLE_WEBVIEW), CapabilityFlags.WebViewBit, true },
    };

    /// <summary>
    /// The aggregate carries seven of the eight declared bits, and the fast-engine bit is the one it omits.
    /// </summary>
    /// <param name="capabilityIdentifier">The preserved identifier, for a legible failure message.</param>
    /// <param name="capabilityBit">The bit under test.</param>
    /// <param name="isAggregateTerm">Whether the legacy declaration lists this bit as a term.</param>
    [Theory]
    [MemberData(nameof(AggregateMembership))]
    public void TheAggregateCarriesSevenOfTheEightDeclaredBits(
        string capabilityIdentifier,
        uint capabilityBit,
        bool isAggregateTerm)
    {
        Assert.Equal(
            isAggregateTerm,
            Bits.BitTest(CapabilityFlags.AllCapabilitiesMask, capabilityBit));

        // The same answer through the gate's own predicate surface, so the projection cannot disagree with
        // the arithmetic.
        Assert.Equal(isAggregateTerm, CapabilityFlags.All.IsEnabled(capabilityBit));
        Assert.Equal(
            isAggregateTerm,
            CapabilityFlags.All.EnabledCapabilityNames.Contains(capabilityIdentifier, StringComparer.Ordinal));
    }

    /// <summary>
    /// The aggregate enables seven capabilities, and the membership table above accounts for all eight.
    /// </summary>
    /// <remarks>
    /// The count is asserted against the literal seven rather than against the table's own row count, for
    /// the same reason the aggregate is asserted against 3847: an expectation derived from the thing under
    /// test cannot fail when that thing drifts.
    /// </remarks>
    [Fact]
    public void TheAggregateEnablesSevenCapabilitiesAndTheVocabularyDeclaresEight()
    {
        Assert.Equal(7, CapabilityFlags.All.EnabledCapabilityNames.Count);
        Assert.Equal(DeclaredCapabilityCount, CapabilityFlags.KnownCapabilityNames.Count);
        Assert.Equal(DeclaredCapabilityCount, CapabilityFlags.DeclaredCapabilityCount);

        // The omitted one is named, so a failure says WHICH capability moved rather than only that a count
        // changed.
        Assert.DoesNotContain(
            nameof(Enums.INIT_FLAG_ENABLE_BLINKFAST),
            CapabilityFlags.All.EnabledCapabilityNames);
    }

    // --------------------------------------------------------------------------------------------------
    //  4. THE GATE AS A PURE FUNCTION OF ITS MASK - NO HOST REQUIRED
    //
    //  Composition/CapabilityFlags.cs performs no I/O and depends on no host, so its whole behaviour can be
    //  pinned by a matrix of masks evaluated in memory. These are the fastest and most deterministic tests
    //  in the folder, and they are where the exhaustive per-capability coverage belongs.
    //
    //  The matrix deliberately includes the boundary cases rather than only the interesting ones: an empty
    //  mask, the aggregate, the union of all eight, a single undeclared high bit, a declared bit mixed with
    //  an undeclared one, the sign bit (which is where a 32-bit mask handled as a signed value would go
    //  wrong), and the all-ones mask.
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The eight declared capabilities paired with the single read-only predicate that reports each one.
    /// </summary>
    /// <remarks>
    /// Held as a table so that "one predicate per capability" is verified across every mask in the matrix
    /// rather than one predicate at a time. A predicate wired to the wrong bit would pass a hand-written
    /// single-bit test and fail here.
    /// </remarks>
    private static readonly (string Name, uint Bit, Func<CapabilityFlags, bool> Predicate)[] CapabilityPredicates =
    [
        (nameof(Enums.INIT_FLAG_ENABLE_UI), CapabilityFlags.UiBit, static gate => gate.Ui),
        (nameof(Enums.INIT_FLAG_ENABLE_SCITER), CapabilityFlags.SciterBit, static gate => gate.Sciter),
        (nameof(Enums.INIT_FLAG_ENABLE_BLINK), CapabilityFlags.BlinkBit, static gate => gate.Blink),
        (nameof(Enums.INIT_FLAG_ENABLE_BLINKFAST), CapabilityFlags.BlinkFastBit, static gate => gate.BlinkFast),
        (nameof(Enums.INIT_FLAG_ENABLE_ORCA), CapabilityFlags.OrcaBit, static gate => gate.Orca),
        (nameof(Enums.INIT_FLAG_ENABLE_SQLITE), CapabilityFlags.SqliteBit, static gate => gate.Sqlite),
        (nameof(Enums.INIT_FLAG_ENABLE_DPIAWARE), CapabilityFlags.DpiAwareBit, static gate => gate.DpiAware),
        (nameof(Enums.INIT_FLAG_ENABLE_WEBVIEW), CapabilityFlags.WebViewBit, static gate => gate.WebView),
    ];

    /// <summary>
    /// Masks paired with the capability identifiers each one must report as enabled, in declaration order.
    /// </summary>
    /// <remarks>
    /// The 3847 row lists seven names and the 3855 row lists eight, which is the same preserved
    /// characteristic as section 1 expressed as an observable projection rather than as arithmetic.
    /// </remarks>
    public static TheoryData<uint, string[]> CapabilityMaskMatrix => new()
    {
        // Nothing configured enables nothing. The legacy is explicit that a module which is not
        // initialized is unusable, so an empty gate is a meaningful state rather than a degenerate one
        // [docs/README.md, 高级初始化 at :L26].
        { 0u, [] },

        // Each declared bit on its own.
        { 1u, [nameof(Enums.INIT_FLAG_ENABLE_UI)] },
        { 2u, [nameof(Enums.INIT_FLAG_ENABLE_SCITER)] },
        { 4u, [nameof(Enums.INIT_FLAG_ENABLE_BLINK)] },
        { 8u, [nameof(Enums.INIT_FLAG_ENABLE_BLINKFAST)] },
        { 256u, [nameof(Enums.INIT_FLAG_ENABLE_ORCA)] },
        { 512u, [nameof(Enums.INIT_FLAG_ENABLE_SQLITE)] },
        { 1024u, [nameof(Enums.INIT_FLAG_ENABLE_DPIAWARE)] },
        { 2048u, [nameof(Enums.INIT_FLAG_ENABLE_WEBVIEW)] },

        // Two low bits composed, which is how a mask is built in the first place.
        {
            3u,
            [nameof(Enums.INIT_FLAG_ENABLE_UI), nameof(Enums.INIT_FLAG_ENABLE_SCITER)]
        },

        // THE AGGREGATE. Seven names, and INIT_FLAG_ENABLE_BLINKFAST is absent (C-B).
        {
            3847u,
            [
                nameof(Enums.INIT_FLAG_ENABLE_UI),
                nameof(Enums.INIT_FLAG_ENABLE_SCITER),
                nameof(Enums.INIT_FLAG_ENABLE_BLINK),
                nameof(Enums.INIT_FLAG_ENABLE_ORCA),
                nameof(Enums.INIT_FLAG_ENABLE_SQLITE),
                nameof(Enums.INIT_FLAG_ENABLE_DPIAWARE),
                nameof(Enums.INIT_FLAG_ENABLE_WEBVIEW),
            ]
        },

        // The union of all eight - the value a "correction" of the aggregate would produce. It is a
        // perfectly legal thing for an OPERATOR to configure; what C-B forbids is changing the constant.
        {
            3855u,
            [
                nameof(Enums.INIT_FLAG_ENABLE_UI),
                nameof(Enums.INIT_FLAG_ENABLE_SCITER),
                nameof(Enums.INIT_FLAG_ENABLE_BLINK),
                nameof(Enums.INIT_FLAG_ENABLE_BLINKFAST),
                nameof(Enums.INIT_FLAG_ENABLE_ORCA),
                nameof(Enums.INIT_FLAG_ENABLE_SQLITE),
                nameof(Enums.INIT_FLAG_ENABLE_DPIAWARE),
                nameof(Enums.INIT_FLAG_ENABLE_WEBVIEW),
            ]
        },

        // An undeclared bit alone enables no capability at all - it must not be mistaken for one of the
        // eight just because it is set.
        { 32768u, [] },

        // A declared bit mixed with two undeclared ones still reports exactly the declared capability.
        { 512u | 32768u | 65536u, [nameof(Enums.INIT_FLAG_ENABLE_SQLITE)] },

        // The sign bit. A mask handled as a signed value would misread this one, and the legacy parameter is
        // unsigned [pfwinitialize.srf:L8].
        { 0x8000_0000u, [] },

        // Every bit set: all eight declared capabilities are enabled and the rest of the word is
        // unrecognized.
        {
            uint.MaxValue,
            [
                nameof(Enums.INIT_FLAG_ENABLE_UI),
                nameof(Enums.INIT_FLAG_ENABLE_SCITER),
                nameof(Enums.INIT_FLAG_ENABLE_BLINK),
                nameof(Enums.INIT_FLAG_ENABLE_BLINKFAST),
                nameof(Enums.INIT_FLAG_ENABLE_ORCA),
                nameof(Enums.INIT_FLAG_ENABLE_SQLITE),
                nameof(Enums.INIT_FLAG_ENABLE_DPIAWARE),
                nameof(Enums.INIT_FLAG_ENABLE_WEBVIEW),
            ]
        },
    };

    /// <summary>
    /// For any mask, the gate reports the mask unchanged, projects exactly the expected capability names in
    /// declaration order, and reports everything outside the declared eight as unrecognized.
    /// </summary>
    /// <param name="mask">The 32-bit capability mask under test.</param>
    /// <param name="expectedEnabledNames">The identifiers expected to be reported, in declaration order.</param>
    /// <remarks>
    /// The unrecognized-bit expectation is computed against the LITERAL union 3855, not against
    /// <c>KnownCapabilityMask</c>, so the two cannot drift together silently.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CapabilityMaskMatrix))]
    public void TheGateReportsItsMaskUnchangedAndProjectsOnlyDeclaredCapabilities(
        uint mask,
        string[] expectedEnabledNames)
    {
        ArgumentNullException.ThrowIfNull(expectedEnabledNames);

        CapabilityFlags gate = new(mask);

        // The mask is carried, never normalised: rejecting or trimming a mask the legacy accepted would be
        // new behaviour (C-B).
        Assert.Equal(mask, gate.EffectiveMask);

        // The projection, in declaration order.
        Assert.Equal(expectedEnabledNames, gate.EnabledCapabilityNames);

        // Anything outside the eight declared bits is REPORTED rather than rejected, so a configuration
        // carrying a future or mistaken bit starts and says so.
        uint expectedUnrecognized = Bits.BitAnd(mask, Bits.BitNot(3855u));

        Assert.Equal(expectedUnrecognized, gate.UnrecognizedBits);
        Assert.Equal(expectedUnrecognized != 0u, gate.HasUnrecognizedBits);

        // No undeclared bit ever contributes a name, which is what keeps the published vocabulary closed.
        Assert.All(
            gate.EnabledCapabilityNames,
            name => Assert.Contains(name, CapabilityFlags.KnownCapabilityNames));
    }

    /// <summary>
    /// For any mask, each capability's single read-only predicate agrees with the kernel bit test and with
    /// the projected name list.
    /// </summary>
    /// <param name="mask">The 32-bit capability mask under test.</param>
    /// <param name="expectedEnabledNames">The identifiers expected to be reported, in declaration order.</param>
    /// <remarks>
    /// Three independent readings of the same fact are required to agree: the predicate on the gate, the
    /// kernel's <c>Bits.BitTest</c> (an ANY-bit test ported from
    /// <c>ws_objects/pfw.common.pbl.src/bittest.srf</c>) applied to the raw mask, and membership of the
    /// expected name list. A predicate wired to a neighbouring bit breaks the agreement.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CapabilityMaskMatrix))]
    public void EveryCapabilityPredicateAgreesWithTheKernelBitTestAndWithTheProjection(
        uint mask,
        string[] expectedEnabledNames)
    {
        ArgumentNullException.ThrowIfNull(expectedEnabledNames);

        CapabilityFlags gate = new(mask);

        Assert.Equal(DeclaredCapabilityCount, CapabilityPredicates.Length);

        foreach ((string name, uint bit, Func<CapabilityFlags, bool> predicate) in CapabilityPredicates)
        {
            bool expected = expectedEnabledNames.Contains(name, StringComparer.Ordinal);

            Assert.Equal(expected, predicate(gate));
            Assert.Equal(expected, gate.IsEnabled(bit));
            Assert.Equal(expected, Bits.BitTest(mask, bit));
        }
    }

    // --------------------------------------------------------------------------------------------------
    //  5. THE GATE EXPRESSED AS CONFIGURATION
    //
    //  This is Gateway's equivalent of the legacy module-gating bitmask. The legacy hardcodes its argument
    //  at ws_objects/pfw.pbl.src/pfw.sra:L91; the port preserves that value as the DEFAULT and makes it
    //  configurable, which reproduces the observable behaviour without re-creating the un-configurability.
    //
    //  The key is driven through test configuration here rather than by editing appsettings.json, which
    //  belongs to another project. In a deployed container the same key arrives through the ASP.NET Core
    //  double-underscore environment convention as Gateway__CapabilityFlags; the colon form asserted below
    //  is the same configuration key, and section 6 proves the SHIPPED default end to end by reading it
    //  back off the wire.
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// With nothing configured, the gate defaults to the seven-term aggregate.
    /// </summary>
    /// <remarks>
    /// Asserted against the literal 3847 as well as against the kernel constant, so a change to either the
    /// option default or the constant is caught. The default is the legacy's own hardcoded argument
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L91</c>].
    /// </remarks>
    [Fact]
    public void WithNothingConfiguredTheGateDefaultsToTheAggregate()
    {
        GatewayOptions options = new();

        Assert.Equal(3847L, options.CapabilityFlags);
        Assert.Equal(Enums.INIT_FLAG_ENABLE_ALL, options.CapabilityFlags);

        // Binding an EMPTY configuration section must not disturb the default. A binder that wrote a zero
        // over an absent value would silently disable every capability.
        IConfiguration empty = new ConfigurationBuilder().Build();

        empty.GetSection(GatewayOptions.SectionName).Bind(options);

        Assert.Equal(3847L, options.CapabilityFlags);
        Assert.Equal(3847u, CapabilityFlags.FromOptions(options).EffectiveMask);
        Assert.False(CapabilityFlags.FromOptions(options).BlinkFast);
        Assert.True(CapabilityFlags.FromOptions(options).Sqlite);
    }

    /// <summary>
    /// A value at <c>Gateway:CapabilityFlags</c> binds to the gate, across the whole configured domain.
    /// </summary>
    /// <param name="configuredValue">The value as an operator would write it in configuration.</param>
    /// <param name="expectedMask">The 32-bit mask the gate must resolve to.</param>
    /// <remarks>
    /// The negative and out-of-range rows are not curiosities. The configured value is projected with an
    /// UNCHECKED conversion because the legacy flag word is an unsigned long, so a wrapped value is
    /// accepted rather than rejected - refusing it would make a configuration the legacy tolerated fail to
    /// start, which would be new behaviour.
    /// </remarks>
    [Theory]
    [InlineData("3847", 3847u)]
    [InlineData("3855", 3855u)]
    [InlineData("512", 512u)]
    [InlineData("0", 0u)]
    [InlineData("2048", 2048u)]
    [InlineData("4294967295", uint.MaxValue)]
    [InlineData("-1", uint.MaxValue)]
    public void AValueAtTheDocumentedConfigurationKeyBindsToTheGate(string configuredValue, uint expectedMask)
    {
        GatewayOptions options = BindGatewaySection(
            $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.CapabilityFlags)}",
            configuredValue);

        Assert.Equal(expectedMask, CapabilityFlags.FromOptions(options).EffectiveMask);
    }

    /// <summary>
    /// The gate reads <c>Gateway:CapabilityFlags</c> and nothing else, so a value at a neighbouring key is
    /// ignored rather than silently honoured.
    /// </summary>
    /// <remarks>
    /// Worth pinning because a capability gate that quietly read an unintended key would be configurable by
    /// accident, and an operator would have no way to tell a typo from a working override.
    /// </remarks>
    [Fact]
    public void AValueAtAnyOtherKeyLeavesTheGateAtItsDefault()
    {
        Assert.Equal(3847u, CapabilityFlags.FromOptions(BindGatewaySection("CapabilityFlags", "512")).EffectiveMask);
        Assert.Equal(
            3847u,
            CapabilityFlags.FromOptions(BindGatewaySection("Gateway:Capabilities", "512")).EffectiveMask);
        Assert.Equal(
            3847u,
            CapabilityFlags.FromOptions(BindGatewaySection("Gateway:Upstreams:CapabilityFlags", "512"))
                .EffectiveMask);
    }

    // --------------------------------------------------------------------------------------------------
    //  6. THE PUBLISHED PROJECTION - GET /v1/capabilities
    //
    //  Asserted against the contract in shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml rather than
    //  against an invented shape: the report is closed with additionalProperties:false and requires
    //  effectiveMask, allMask and capabilities; allMask is declared `const: 3847`; and the capabilities
    //  array is fixed at minItems 8 / maxItems 8 because the legacy declaration is closed.
    //
    //  The host here is the shared fixture, which loads the Gateway project's own appsettings.json. That is
    //  what makes this section the end-to-end proof that the SHIPPED default of Gateway:CapabilityFlags is
    //  3847 rather than merely that the C# option default is.
    //
    //  The authorisation posture of this route (challenged anonymously, answered with a credential) is
    //  owned by AuthorizationTests.cs under C-G and is not restated here; these tests present a credential
    //  because they are about the BODY.
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The projection reports the deployment's effective mask and the aggregate that the legacy fixes.
    /// </summary>
    /// <remarks>
    /// <c>allMask</c> is 3847 and <b>not</b> 3855 on the wire as well as in the constant, because the
    /// published schema declares it <c>const: 3847</c>. A consumer that computed the union of the eight
    /// declared bits itself would disagree with the framework by exactly the fast-engine bit - which is why
    /// the value is published at all rather than left for callers to derive.
    /// </remarks>
    [Fact]
    public async Task TheProjectionReportsTheEffectiveMaskAndTheAggregateFixedByTheLegacy()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(CapabilitiesRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await ReadJsonBodyAsync(response);
        JsonElement report = body.RootElement;

        // The shipped default, read back off the wire.
        Assert.Equal(3847L, report.GetProperty("effectiveMask").GetInt64());

        // Fixed by the legacy declaration rather than by this deployment - hence a constant in the schema.
        Assert.Equal(3847L, report.GetProperty("allMask").GetInt64());
        Assert.NotEqual(3855L, report.GetProperty("allMask").GetInt64());

        // A default deployment carries no undeclared bit.
        Assert.Equal(0L, report.GetProperty("unrecognizedBits").GetInt64());

        // additionalProperties:false, so the member count is contract rather than incidental.
        int reportMembers = report.EnumerateObject().Count();

        Assert.Equal(CapabilityReportMemberCount, reportMembers);
    }

    /// <summary>
    /// The projection carries all eight declared capabilities, in declaration order, with their legacy
    /// values, and reports every one enabled except the fast-engine bit the aggregate omits.
    /// </summary>
    /// <remarks>
    /// The expected values are the literals the legacy declares and the published <c>value</c> enumeration
    /// repeats - 1, 2, 4, 8, 256, 512, 1024, 2048 - so neither the projection nor the ported catalogue can
    /// drift without failing here. The enabled flags are the preserved characteristic in its most visible
    /// form: seven true, and <c>INIT_FLAG_ENABLE_BLINKFAST</c> false in a default deployment.
    /// </remarks>
    [Fact]
    public async Task TheProjectionCarriesAllEightDeclaredCapabilitiesAndNothingElse()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using JsonDocument body = await ReadCapabilityReportAsync(client);
        JsonElement capabilities = body.RootElement.GetProperty("capabilities");

        Assert.Equal(JsonValueKind.Array, capabilities.ValueKind);
        Assert.Equal(DeclaredCapabilityCount, capabilities.GetArrayLength());

        string[] projectedNames = [.. capabilities.EnumerateArray()
            .Select(static capability => capability.GetProperty("name").GetString() ?? string.Empty)];

        long[] projectedValues = [.. capabilities.EnumerateArray()
            .Select(static capability => capability.GetProperty("value").GetInt64())];

        // Declaration order, spelled exactly as enums.sru:L41-L48 declares it. The identifiers are
        // preserved verbatim in deliberate departure from .NET naming convention because they appear in
        // serialized payloads and stored characterization comparisons.
        Assert.Equal(
            [
                nameof(Enums.INIT_FLAG_ENABLE_UI),
                nameof(Enums.INIT_FLAG_ENABLE_SCITER),
                nameof(Enums.INIT_FLAG_ENABLE_BLINK),
                nameof(Enums.INIT_FLAG_ENABLE_BLINKFAST),
                nameof(Enums.INIT_FLAG_ENABLE_ORCA),
                nameof(Enums.INIT_FLAG_ENABLE_SQLITE),
                nameof(Enums.INIT_FLAG_ENABLE_DPIAWARE),
                nameof(Enums.INIT_FLAG_ENABLE_WEBVIEW),
            ],
            projectedNames);

        // The sparse legacy value set, including the gap where bits 16, 32, 64 and 128 are simply not
        // declared.
        Assert.Equal([1L, 2L, 4L, 8L, 256L, 512L, 1024L, 2048L], projectedValues);

        // The projection agrees with the ported vocabulary as well as with the literals.
        Assert.Equal(CapabilityFlags.KnownCapabilityNames, projectedNames);

        foreach (JsonElement capability in capabilities.EnumerateArray())
        {
            string name = capability.GetProperty("name").GetString() ?? string.Empty;

            // Seven enabled, and the fast-engine bit not - the aggregate's omission as an operator sees it.
            bool expectedEnabled = !string.Equals(
                name,
                nameof(Enums.INIT_FLAG_ENABLE_BLINKFAST),
                StringComparison.Ordinal);

            Assert.Equal(expectedEnabled, capability.GetProperty("enabled").GetBoolean());

            // Every entry names its Phase-1 destination, and additionalProperties:false closes the object.
            Assert.Equal(JsonValueKind.String, capability.GetProperty("phaseOneDestination").ValueKind);

            int capabilityMembers = capability.EnumerateObject().Count();

            Assert.Equal(CapabilityMemberCount, capabilityMembers);
        }
    }

    // --------------------------------------------------------------------------------------------------
    //  7. ONLY ONE BIT HAS AN IN-SCOPE CONSUMER IN THIS PHASE
    //
    //  This mapping is the evidence that the Phase-1 service slice is drawn correctly, so it is pinned as
    //  DATA rather than left to a design document. INIT_FLAG_ENABLE_SQLITE is the only bit whose
    //  implementation exists in this phase; every other bit names a capability area outside it, so enabling
    //  one reports configuration and grants nothing.
    //
    //  CONSTRAINT C-D: the deferred areas appear as STRINGS ONLY. CapabilityDestination is Gateway's own
    //  published enumeration - nothing belonging to DesignSystem, Documents, Integration or ScriptBridge is
    //  imported, instantiated or referenced anywhere in this file, because none exists and none may be
    //  created in this phase.
    //
    //  Why the mapping is meaningful rather than decorative: docs/README.md:L26 states that a module which
    //  is not explicitly initialized is unusable AND that its native library need not ship, and :L30 warns
    //  that sciter.dll must accompany the application or initialization FAILS. A capability bit therefore
    //  carries a shipping obligation, and turning one on for a deferred area asks for a payload this phase
    //  does not deliver.
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Each declared capability with its legacy value, its Phase-1 destination as the contract publishes it,
    /// and whether that destination is a service implemented in this phase.
    /// </summary>
    /// <remarks>
    /// Exactly one row is in scope. <c>PackagingTooling</c> is <c>INIT_FLAG_ENABLE_ORCA</c>, which is
    /// PowerBuilder build tooling rather than a service at all, so it is out of scope without being
    /// deferred - a distinction the published enumeration keeps and this table therefore keeps too.
    /// </remarks>
    public static TheoryData<string, long, string, bool> PhaseOneDestinations => new()
    {
        { nameof(Enums.INIT_FLAG_ENABLE_UI), 1L, nameof(CapabilityDestination.DesignSystem), false },
        { nameof(Enums.INIT_FLAG_ENABLE_SCITER), 2L, nameof(CapabilityDestination.ScriptBridge), false },
        { nameof(Enums.INIT_FLAG_ENABLE_BLINK), 4L, nameof(CapabilityDestination.ScriptBridge), false },
        { nameof(Enums.INIT_FLAG_ENABLE_BLINKFAST), 8L, nameof(CapabilityDestination.ScriptBridge), false },
        { nameof(Enums.INIT_FLAG_ENABLE_ORCA), 256L, nameof(CapabilityDestination.PackagingTooling), false },

        // THE ONLY BIT WITH AN IN-SCOPE CONSUMER IN PHASE 1.
        { nameof(Enums.INIT_FLAG_ENABLE_SQLITE), 512L, nameof(CapabilityDestination.Persistence), true },

        { nameof(Enums.INIT_FLAG_ENABLE_DPIAWARE), 1024L, nameof(CapabilityDestination.DesignSystem), false },
        { nameof(Enums.INIT_FLAG_ENABLE_WEBVIEW), 2048L, nameof(CapabilityDestination.ScriptBridge), false },
    };

    /// <summary>
    /// Every capability names its Phase-1 destination on the wire, and only the SQLite bit's destination is
    /// a service this phase implements.
    /// </summary>
    /// <param name="capabilityIdentifier">The preserved identifier to look the entry up by.</param>
    /// <param name="expectedValue">The literal value the legacy declares for this bit.</param>
    /// <param name="expectedDestination">The destination the published contract assigns to it.</param>
    /// <param name="hasInScopeConsumer">Whether that destination is implemented in this phase.</param>
    [Theory]
    [MemberData(nameof(PhaseOneDestinations))]
    public async Task EachCapabilityNamesItsPhaseOneDestination(
        string capabilityIdentifier,
        long expectedValue,
        string expectedDestination,
        bool hasInScopeConsumer)
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using JsonDocument body = await ReadCapabilityReportAsync(client);

        JsonElement capability = FindCapability(body.RootElement, capabilityIdentifier);

        Assert.Equal(expectedValue, capability.GetProperty("value").GetInt64());
        Assert.Equal(expectedDestination, capability.GetProperty("phaseOneDestination").GetString());

        // The in-scope answer is pinned from BOTH directions, so a row cannot quietly claim that a deferred
        // capability area has an implementation in this phase: it must agree with the destination being
        // Persistence AND with the capability being the SQLite bit. Persistence is the one Phase-1 service
        // that consumes a capability bit at all.
        Assert.Equal(
            hasInScopeConsumer,
            string.Equals(expectedDestination, nameof(CapabilityDestination.Persistence), StringComparison.Ordinal));

        Assert.Equal(
            hasInScopeConsumer,
            string.Equals(
                capabilityIdentifier,
                nameof(Enums.INIT_FLAG_ENABLE_SQLITE),
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Across the whole projection, exactly one capability points at an in-scope Phase-1 service.
    /// </summary>
    /// <remarks>
    /// The per-row theory above proves each mapping; this proves the SET, which is the claim that actually
    /// corroborates the Phase-1 slice. Asserted from the wire so it holds for the deployed contract and not
    /// only for an in-memory table.
    /// </remarks>
    [Fact]
    public async Task SqliteIsTheOnlyCapabilityWhoseDestinationIsAnInScopeService()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using JsonDocument body = await ReadCapabilityReportAsync(client);

        string[] inScope = [.. body.RootElement.GetProperty("capabilities").EnumerateArray()
            .Where(static capability => string.Equals(
                capability.GetProperty("phaseOneDestination").GetString(),
                nameof(CapabilityDestination.Persistence),
                StringComparison.Ordinal))
            .Select(static capability => capability.GetProperty("name").GetString() ?? string.Empty)];

        Assert.Equal([nameof(Enums.INIT_FLAG_ENABLE_SQLITE)], inScope);

        // And no entry claims a destination outside the four the contract publishes.
        string[] publishedDestinations =
        [
            nameof(CapabilityDestination.Persistence),
            nameof(CapabilityDestination.DesignSystem),
            nameof(CapabilityDestination.ScriptBridge),
            nameof(CapabilityDestination.PackagingTooling),
        ];

        Assert.All(
            body.RootElement.GetProperty("capabilities").EnumerateArray(),
            capability => Assert.Contains(
                capability.GetProperty("phaseOneDestination").GetString() ?? string.Empty,
                publishedDestinations));
    }

    // --------------------------------------------------------------------------------------------------
    //  8. AN OVERRIDDEN GATE IS HONOURED END TO END
    //
    //  The gate is only a gate if configuring it changes something. Each row here builds its OWN short-lived
    //  host rather than mutating the shared fixture's settings, because settings are read once when the host
    //  boots and mutating them afterwards would make the result depend on test order.
    //
    //  Three things are asserted together, and together is the point: the configured value reaches the
    //  option, the projection reports it, and the framework's lifecycle boundary receives it. A gate that
    //  reported one mask while initializing another would satisfy any two of those separately.
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// An overridden capability mask is honoured by the option, by the published projection, and by the
    /// framework lifecycle boundary.
    /// </summary>
    /// <param name="configuredMask">The mask an operator configures at <c>Gateway:CapabilityFlags</c>.</param>
    /// <param name="expectedEnabledCount">How many of the eight declared capabilities that mask enables.</param>
    /// <remarks>
    /// <para>
    /// The 3855 row is deliberate: an operator MAY configure the union of all eight bits, and the port must
    /// honour it. What C-B forbids is changing the CONSTANT - the aggregate stays 3847, which is why
    /// <c>allMask</c> is asserted to remain 3847 even while <c>effectiveMask</c> is 3855.
    /// </para>
    /// <para>
    /// The 512 row is the only mask in this system with an in-scope consumer, and the 0 row is the state the
    /// legacy documentation describes as a module being unusable and its library not needing to ship
    /// [<c>docs/README.md:L26</c>]. It must START, not fail: nothing in the legacy rejects an empty mask.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(512L, 1)]
    [InlineData(3855L, 8)]
    [InlineData(2048L, 1)]
    [InlineData(0L, 0)]
    public async Task AnOverriddenGateIsHonouredByTheProjectionAndReachesTheLifecycleBoundary(
        long configuredMask,
        int expectedEnabledCount)
    {
        await using GatewayTestHostFixture overriddenHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        // In a container the same key arrives as Gateway__CapabilityFlags through the double-underscore
        // environment convention; the colon spelling below is that same configuration key. Driven through
        // test configuration rather than by editing another project's appsettings.json.
        overriddenHost.AdditionalSettings[
            $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.CapabilityFlags)}"] =
                configuredMask.ToString(CultureInfo.InvariantCulture);

        using HttpClient client = overriddenHost.CreateAuthenticatedClient();

        using JsonDocument body = await ReadCapabilityReportAsync(client);
        JsonElement report = body.RootElement;

        // The projection follows configuration ...
        Assert.Equal(configuredMask, report.GetProperty("effectiveMask").GetInt64());

        // ... while the aggregate stays exactly where the legacy declaration puts it.
        Assert.Equal(3847L, report.GetProperty("allMask").GetInt64());

        int enabledCount = report.GetProperty("capabilities").EnumerateArray()
            .Count(static capability => capability.GetProperty("enabled").GetBoolean());

        Assert.Equal(expectedEnabledCount, enabledCount);

        // The whole set is still projected, enabled or not: the published array is fixed at eight entries.
        Assert.Equal(DeclaredCapabilityCount, report.GetProperty("capabilities").GetArrayLength());

        // THE MASK THE FRAMEWORK BOUNDARY ACTUALLY RECEIVED. Recorded by the fixture's substitute for the
        // native pfw.dll seam, so this is the configured gate observed at the point of use rather than
        // inferred from the projection.
        Assert.Equal((uint)configuredMask, overriddenHost.RequestedCapabilityMask);
        Assert.Equal((uint)configuredMask, overriddenHost.Initializer.Capabilities.EffectiveMask);
        Assert.Contains("Initialize(uint)", overriddenHost.RuntimeCalls);
    }

    // --------------------------------------------------------------------------------------------------
    //  9. THE GATE AT THE FRAMEWORK LIFECYCLE BOUNDARY - NO HOST REQUIRED
    //
    //  ws_objects/pfw.pbl.src/pfw.sra:L91 calls pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL) and DISCARDS the
    //  return value - there is no assignment - and :L108 calls pfwFinalize() in the close event, likewise
    //  discarding it. FrameworkInitializer deliberately CHECKS both codes. That is a documented, annotated
    //  DEPARTURE rather than a C-B violation, and its justification is in the legacy documentation itself:
    //  docs/README.md:L15 marks the pairing mandatory, :L26-L30 make an uninitialized module unusable, and
    //  :L30 states that a missing native library makes initialization FAIL. Starting a service whose
    //  framework precondition is unproven would be graceful degradation dressed over a structural fault.
    //
    //  The tri-state hole matters at this boundary and is not assumed away: Predicates.IsFailed EXCLUDES
    //  RetCode.CANCELLED, so it is NOT the negation of Predicates.IsSucceeded and the two do not partition
    //  the space. The classification of each code is owned by AuthorizationTests.cs; what these tests own is
    //  that the CONFIGURED GATE is the mask that travels to the boundary and back out again in a failure.
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The configured gate is the mask handed to the framework boundary, and finalization is paired with it.
    /// </summary>
    /// <param name="configuredMask">The value configured at <c>Gateway:CapabilityFlags</c>.</param>
    /// <param name="expectedMask">The 32-bit mask the boundary must receive.</param>
    /// <remarks>
    /// The negative row is not a curiosity: the configured value is projected with an unchecked conversion
    /// because the legacy parameter is <c>readonly unsignedlong</c> [<c>pfwinitialize.srf:L8</c>], so a
    /// wrapped value must reach the boundary unchanged rather than be refused.
    /// </remarks>
    [Theory]
    [InlineData(3847L, 3847u)]
    [InlineData(3855L, 3855u)]
    [InlineData(512L, 512u)]
    [InlineData(0L, 0u)]
    [InlineData(-1L, uint.MaxValue)]
    public async Task TheConfiguredGateIsTheMaskHandedToTheFrameworkBoundary(long configuredMask, uint expectedMask)
    {
        RecordingFrameworkRuntime runtime = new();

        FrameworkInitializer initializer = new(
            runtime,
            Options.Create(new GatewayOptions { CapabilityFlags = configuredMask }),
            NullLogger<FrameworkInitializer>.Instance);

        await initializer.StartingAsync(TestContext.Current.CancellationToken);

        Assert.True(initializer.IsInitialized);

        // The gate reached the boundary unchanged, and the initializer's own view agrees with it.
        Assert.Equal(expectedMask, runtime.RequestedCapabilityMask);
        Assert.Equal(expectedMask, initializer.Capabilities.EffectiveMask);

        // The flags overload, never the parameterless one - which has no call site in the legacy estate
        // either [ws_objects/pfw.pbl.src/pfw.sra:L91 passes the aggregate].
        Assert.Contains("Initialize(uint)", runtime.Calls);
        Assert.DoesNotContain("Initialize()", runtime.Calls);

        await initializer.StoppedAsync(TestContext.Current.CancellationToken);

        // THE MANDATORY PAIRING: docs/README.md:L15 warns that pfwFinalize must be paired with
        // pfwInitialize, and ws_objects/pfw.pbl.src/pfw.sra pairs them across :L91 and :L108.
        Assert.True(initializer.IsFinalized);
        Assert.Contains("Finalize()", runtime.Calls);

        List<string> calls = [.. runtime.Calls];

        Assert.True(
            calls.IndexOf("Initialize(uint)") < calls.IndexOf("Finalize()"),
            "Finalization must follow initialization: the pairing is mandatory and it is ordered.");
    }

    /// <summary>
    /// A refused initialization is fatal, reports the gate that was requested, and leaves no finalization
    /// owed.
    /// </summary>
    /// <param name="refusal">The return code the framework boundary answers with.</param>
    /// <remarks>
    /// <para>
    /// Both rows are non-successes under the preserved algebra but they are non-successes of DIFFERENT
    /// kinds, and the distinction is the tri-state hole rather than a technicality:
    /// <see cref="RetCode.FAILED"/> is a definite failure, while <see cref="RetCode.CANCELLED"/> is neither
    /// succeeded nor failed because the failure predicate excludes it explicitly. Both are fatal here,
    /// because an initialization whose success is not positively signalled leaves the framework's usability
    /// precondition unproven.
    /// </para>
    /// <para>
    /// What this test adds over the algebra tests in <c>AuthorizationTests.cs</c> is the GATE: the failure
    /// carries the CONFIGURED mask, not the default one, so an operator reading the diagnostic learns which
    /// capability set was refused. Fail-fast is asserted AS fail-fast - the initializer refuses to complete
    /// startup - and nothing here terminates the test runner.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(RetCode.FAILED)]
    [InlineData(RetCode.CANCELLED)]
    public async Task ARefusedInitializationReportsTheConfiguredGateAndOwesNoFinalization(long refusal)
    {
        // The one bit with an in-scope consumer, chosen so the reported mask is unmistakably the CONFIGURED
        // one rather than the 3847 default.
        RecordingFrameworkRuntime runtime = new() { InitializeResult = refusal };

        FrameworkInitializer initializer = new(
            runtime,
            Options.Create(new GatewayOptions { CapabilityFlags = Enums.INIT_FLAG_ENABLE_SQLITE }),
            NullLogger<FrameworkInitializer>.Instance);

        FrameworkInitializationException failure =
            await Assert.ThrowsAsync<FrameworkInitializationException>(
                () => initializer.StartingAsync(TestContext.Current.CancellationToken));

        Assert.Equal(refusal, failure.ReturnCode);
        Assert.Equal(512u, failure.RequestedCapabilities);
        Assert.False(initializer.IsInitialized);

        // The boundary WAS called with the configured gate - the refusal came from it rather than from a
        // pre-flight check that never reached it.
        Assert.Equal(512u, runtime.RequestedCapabilityMask);

        // The tri-state hole, stated rather than assumed: neither code is a success, and only one of them is
        // a failure. An implementation that treated "not failed" as "succeeded" would start on the second.
        Assert.False(Predicates.IsSucceeded(refusal));
        Assert.Equal(refusal == RetCode.FAILED, Predicates.IsFailed(refusal));

        // Nothing was initialized, so nothing is owed: the pairing rule read in the other direction.
        await initializer.StoppedAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Finalize()", runtime.Calls);
        Assert.False(initializer.IsFinalized);
    }

    // --------------------------------------------------------------------------------------------------
    //  HELPERS
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="GatewayOptions"/> by binding the <c>Gateway</c> section of a configuration that
    /// carries exactly one key.
    /// </summary>
    /// <param name="configurationKey">The fully qualified configuration key to populate.</param>
    /// <param name="configuredValue">The value to place at that key, as an operator would write it.</param>
    /// <returns>The bound options.</returns>
    /// <remarks>
    /// A real configuration provider is used rather than a hand-set property, because the assertion is about
    /// the KEY PATH as much as the value: a gate that bound from an unintended key would be configurable by
    /// accident.
    /// </remarks>
    private static GatewayOptions BindGatewaySection(string configurationKey, string configuredValue)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [configurationKey] = configuredValue,
            })
            .Build();

        GatewayOptions options = new();

        configuration.GetSection(GatewayOptions.SectionName).Bind(options);

        return options;
    }

    /// <summary>
    /// Requests the capability projection and parses its body, asserting only that the request succeeded.
    /// </summary>
    /// <param name="client">A client carrying a credential; the route requires one.</param>
    /// <returns>The parsed capability report. The caller owns the returned document.</returns>
    private static async Task<JsonDocument> ReadCapabilityReportAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(
            new Uri(CapabilitiesRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadJsonBodyAsync(response);
    }

    /// <summary>
    /// Reads a response body as JSON.
    /// </summary>
    /// <param name="response">The response to read.</param>
    /// <returns>The parsed document. The caller owns it.</returns>
    private static async Task<JsonDocument> ReadJsonBodyAsync(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return JsonDocument.Parse(payload);
    }

    /// <summary>
    /// Finds one capability entry in a projected report by its preserved identifier.
    /// </summary>
    /// <param name="report">The parsed capability report.</param>
    /// <param name="capabilityIdentifier">The identifier to look for.</param>
    /// <returns>The matching entry.</returns>
    /// <remarks>
    /// Fails with the identifier named rather than with an index out of range, so a projection that dropped
    /// a capability says which one.
    /// </remarks>
    private static JsonElement FindCapability(JsonElement report, string capabilityIdentifier)
    {
        foreach (JsonElement capability in report.GetProperty("capabilities").EnumerateArray())
        {
            if (string.Equals(
                capability.GetProperty("name").GetString(),
                capabilityIdentifier,
                StringComparison.Ordinal))
            {
                return capability;
            }
        }

        Assert.Fail($"The capability projection carries no entry named '{capabilityIdentifier}'.");

        return default;
    }
}
