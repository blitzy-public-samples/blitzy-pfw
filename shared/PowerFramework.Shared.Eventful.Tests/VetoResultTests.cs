// ==================================================================================================
//  VetoResultTests.cs - THE TRI-VALUED VETO, WHICH MUST NEVER BECOME A BOOLEAN
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Eventful.VetoResult
//  ORACLE            ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru   the broker's veto
//                    ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru    its consumers
//
//  WHY A THREE-VALUE ENUM NEEDS A TEST FILE AT ALL
//  ------------------------------------------------------------------------------------------------
//  Because the single most likely change to it is a correct-looking simplification. The broker's veto
//  protocol has THREE states - continue, prevent this dispatch only, and prevent this dispatch and every
//  nested one - and the inline handler comments in se_cst_dw.sru describe the values as though the
//  contract were binary, returning "1" for prevented and "0" for not. A reader who trusts those comments
//  would replace this enum with a bool and silently convert every DEEP prevention into a SHALLOW one:
//  the nested dispatch that should have been suppressed would run, and nothing would report an error.
//
//  The numeric values are equally load-bearing. They cross the wire in the DataServices event-chain
//  contract, appear in log records, and are compared against in characterization recordings, so
//  AAP 0.4.5.3 requires the values survive verbatim. Continue is 0, PreventOnce is 1, PreventDeep is 2 -
//  and 1 is deliberately NOT the RetCode algebra's PREVENT, which happens to share the value but means
//  something different and lives in a different type.
//
//  WHAT THIS FILE ASSERTS
//  ------------------------------------------------------------------------------------------------
//  The three names, the three values, that the set is exactly three wide, that the default is Continue,
//  and - the point of the file - that the type cannot be collapsed to two states without a test failing.
//
// ==================================================================================================

using System;
using System.Linq;
using Xunit;

namespace PowerFramework.Shared.Eventful.Tests;

/// <summary>
/// Characterization tests for <see cref="VetoResult"/>.
/// </summary>
public class VetoResultTests
{
    /// <summary>
    /// The three members carry exactly the legacy numeric values.
    /// </summary>
    /// <remarks>
    /// Asserted against the literals rather than against each other, because the values themselves are
    /// the contract: they are what the broker returns, what the event-chain contract carries, and what a
    /// stored characterization recording compares against. A renumbering that preserved the ORDER would
    /// pass any relative assertion and break every recording.
    /// </remarks>
    [Fact]
    public void TheThreeMembersCarryTheLegacyNumericValues()
    {
        Assert.Equal(0, (int)VetoResult.Continue);
        Assert.Equal(1, (int)VetoResult.PreventOnce);
        Assert.Equal(2, (int)VetoResult.PreventDeep);
    }

    /// <summary>
    /// The enumeration is EXACTLY three wide - no fourth state, and none of the three removed.
    /// </summary>
    /// <remarks>
    /// The cap is the machine-checkable form of "the veto is tri-valued". A fourth member would be a
    /// state the legacy broker cannot produce and no consumer knows how to interpret; a missing one would
    /// mean a legacy return value has no representation. Both are silent failures without this test,
    /// because C# happily converts any integer to an enum without validation.
    /// </remarks>
    [Fact]
    public void TheEnumerationIsExactlyThreeWide()
    {
        VetoResult[] declared = Enum.GetValues<VetoResult>();

        Assert.Equal(3, declared.Length);
        Assert.Equal(
            [VetoResult.Continue, VetoResult.PreventOnce, VetoResult.PreventDeep],
            declared);

        string[] names = Enum.GetNames<VetoResult>();

        Assert.Equal(
            [nameof(VetoResult.Continue), nameof(VetoResult.PreventOnce), nameof(VetoResult.PreventDeep)],
            names);
    }

    /// <summary>
    /// The default value is <see cref="VetoResult.Continue"/>, so an unset result never accidentally
    /// vetoes.
    /// </summary>
    /// <remarks>
    /// This is what makes zero the correct value for continue rather than an arbitrary choice. A field, a
    /// deserialized message with the value absent, or an array element that was never written all read as
    /// the default - and the safe reading of "no answer" is "do not suppress anything". Had continue been
    /// 1 and prevent 0, every uninitialised veto would have blocked its dispatch.
    /// </remarks>
    [Fact]
    public void TheDefaultValueIsContinue()
    {
        VetoResult unset = default;

        Assert.Equal(VetoResult.Continue, unset);
        Assert.Equal(0, (int)unset);
    }

    /// <summary>
    /// The two prevention states are DISTINCT, so the type cannot be collapsed into a boolean without
    /// losing information.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole reason this file exists, stated as directly as it can be. Both preventions are
    /// "prevented", so any boolean projection maps them to the same value - and the mapping is not
    /// reversible. The test demonstrates that: a round trip through a bool loses the distinction, while a
    /// round trip through the enum keeps it.
    /// </para>
    /// <para>
    /// The consequence in the DataWindow event chain is concrete. A shallow prevention suppresses the
    /// current dispatch; a deep one suppresses the nested dispatch that <c>ondwnitemchange</c> fires from
    /// inside itself [se_cst_dw.sru:L182-L253]. Flattening the two makes that nested event run when the
    /// handler asked for it not to.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTwoPreventionStatesAreDistinctSoABooleanCannotCarryThem()
    {
        Assert.NotEqual(VetoResult.PreventOnce, VetoResult.PreventDeep);

        // Any boolean projection loses the distinction: both preventions collapse to the same value.
        static bool IsPrevented(VetoResult result) => result != VetoResult.Continue;

        Assert.True(IsPrevented(VetoResult.PreventOnce));
        Assert.True(IsPrevented(VetoResult.PreventDeep));
        Assert.Equal(IsPrevented(VetoResult.PreventOnce), IsPrevented(VetoResult.PreventDeep));

        // ...and the projection is therefore NOT reversible, which is the statement that matters.
        VetoResult[] preventions = [VetoResult.PreventOnce, VetoResult.PreventDeep];
        Assert.Single(preventions.Select(IsPrevented).Distinct());
        Assert.Equal(2, preventions.Distinct().Count());
    }

    /// <summary>
    /// The prevention value 1 is NOT the return-code algebra's <c>PREVENT</c>, despite sharing the
    /// number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A genuine trap. <c>RetCode.PREVENT</c> is also 1, and both appear in the same call paths, so an
    /// implementation that returned one where the other was expected would compile if either were typed
    /// as a plain integer. They are different contracts: the return-code value participates in the
    /// tri-state success algebra - where <c>IsSucceeded(PREVENT)</c> is TRUE - while this value is a
    /// dispatch-suppression depth and participates in no algebra at all.
    /// </para>
    /// <para>
    /// Recorded as a test rather than a comment because the numeric coincidence is exactly what makes the
    /// confusion invisible: a fix that "unified" them would pass every value assertion in this file.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePreventionValueIsNotTheReturnCodeAlgebrasPrevent()
    {
        // The numbers coincide...
        Assert.Equal(1, (int)VetoResult.PreventOnce);
        Assert.Equal(1L, PowerFramework.Shared.Kernel.RetCode.PREVENT);

        // ...and the types do not, so the compiler keeps them apart.
        Assert.NotEqual(typeof(VetoResult), PowerFramework.Shared.Kernel.RetCode.PREVENT.GetType());
        Assert.Equal(typeof(long), PowerFramework.Shared.Kernel.RetCode.PREVENT.GetType());

        // And the veto has no equivalent of the algebra's third state: there is no veto value that is
        // neither continue nor a prevention, whereas CANCELLED is neither succeeded nor failed.
        Assert.DoesNotContain(
            Enum.GetValues<VetoResult>(),
            candidate => (int)candidate == (int)PowerFramework.Shared.Kernel.RetCode.CANCELLED);
    }

    /// <summary>
    /// The underlying type is <see cref="int"/>, so every legacy value is representable and the wire
    /// projection is unambiguous.
    /// </summary>
    /// <remarks>
    /// The default underlying type is asserted rather than assumed because a narrowing to
    /// <see cref="byte"/> - a plausible "optimization" for a three-value enum - would change how the
    /// value serializes in the event-chain contract and therefore what a stored recording contains, even
    /// though every behavioural test would still pass.
    /// </remarks>
    [Fact]
    public void TheUnderlyingTypeIsInt()
    {
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(VetoResult)));
    }

    /// <summary>
    /// The enumeration is not a flags set: the two preventions are alternatives, not combinable bits.
    /// </summary>
    /// <remarks>
    /// Worth pinning because 1 and 2 look like adjacent bits, and a reader might infer that a value of 3
    /// means "both". It does not - it is not a legal value at all. The broker returns exactly one of the
    /// three, and deep prevention already implies shallow, so there is nothing to combine.
    /// </remarks>
    [Fact]
    public void TheEnumerationIsNotAFlagsSet()
    {
        Assert.Null(typeof(VetoResult).GetCustomAttributes(typeof(FlagsAttribute), inherit: false)
            .FirstOrDefault());

        // The bitwise combination of the two preventions is not a declared value.
        Assert.False(Enum.IsDefined((VetoResult)((int)VetoResult.PreventOnce | (int)VetoResult.PreventDeep)));
    }

    /// <summary>
    /// Values outside the declared three are not defined, so a caller can detect a corrupt or
    /// out-of-contract veto.
    /// </summary>
    /// <param name="candidate">A numeric value that must not be a declared member.</param>
    /// <remarks>
    /// C# does not validate enum conversions, so a deserialized message carrying an unexpected number
    /// produces a <see cref="VetoResult"/> that matches none of the three. <see cref="Enum.IsDefined"/>
    /// is the check that catches it, and pinning that it answers false for these values is what makes a
    /// validating boundary possible in the contract layer.
    /// </remarks>
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void ValuesOutsideTheDeclaredThreeAreNotDefined(int candidate)
    {
        Assert.False(Enum.IsDefined((VetoResult)candidate));
    }
}
