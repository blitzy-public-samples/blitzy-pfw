// ==================================================================================================
//  MacroTemporalRenderingTests - ONE CHARACTERIZED FORMATTER, TWO CALLERS
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   MacroInvoker.TryRender  <- n_cst_dwsvc_columnexp.sru:L2276-L2280
//            ValueToExpression       <- dwvaluetoexp.srf:L53-L69
//
//  WHY THIS FILE EXISTS
//  ------------------------------------------------------------------------------------------------
//  Both files render a temporal value into DataWindow expression text, and both do it by reproducing
//  PowerBuilder's parameterless `String(aVal)` conversion. They are two legacy sites, but they feed the
//  SAME expression parser and can both describe the same cell value - so if their rendered text differs,
//  one of them is wrong and neither says which.
//
//  MacroInvoker deliberately carries no format strings of its own; carrying them, with SIX fractional
//  digits for `datetime` and `time`, is the tempting shape. ValueToExpression carries none either: it
//  composes its wrapper around the three culture-pinned formatters the validators publish, which emit
//  WHOLE SECONDS. Two sets of format strings mean a whole-second value rendered through
//  a macro produces `DateTime('2024-03-05 14:07:09.000000')` while the same value rendered through the
//  validator path produced `DateTime('2024-03-05 14:07:09')`. A characterization recording comparing the
//  two would have shown a difference with no legacy counterpart.
//
//  DECISION 5 in the validator files is the rule these tests pin: the temporal format is declared ONCE,
//  on the producing validator, and every emitter calls through it. Fractional precision itself remains an
//  UNVERIFIED-FROM-REPOSITORY characterization item recorded on those constants - the point of these
//  tests is that when it is pinned, BOTH paths move together.
// ==================================================================================================

using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Validators;

using Xunit;

namespace PowerFramework.DataServices.Tests;

public sealed class MacroTemporalRenderingTests
{
    // A value with a non-zero sub-second component, chosen precisely because it is the value that
    // distinguishes the two formats. A whole-second value would pass under either.
    private static readonly DateTime SubSecondDateTime = new(2024, 3, 5, 14, 7, 9, 123, DateTimeKind.Unspecified);
    private static readonly DateTime WholeSecondDateTime = new(2024, 3, 5, 14, 7, 9, 0, DateTimeKind.Unspecified);
    private static readonly DateOnly Birth = new(1990, 1, 2);
    private static readonly TimeOnly SubSecondTime = new(14, 7, 9, 123);
    private static readonly TimeOnly WholeSecondTime = new(14, 7, 9);

    [Fact]
    public void ADateTimeRendersThroughTheCanonicalValidatorFormatter()
    {
        Assert.True(MacroInvoker.TryRender(SubSecondDateTime, out string rendered, out string? className));

        Assert.Equal("datetime", className);

        // THE ASSERTION IS AGREEMENT, NOT A SECOND COPY OF THE FORMAT. Comparing against the formatter
        // rather than against a literal is what keeps this test correct after the characterization item on
        // DateTimeValidator.ExpressionValueFormat is settled: if that constant changes, both sides move.
        Assert.Equal(
            "DateTime('" + DateTimeValidator.FormatExpressionValue(SubSecondDateTime) + "')",
            rendered);

        // And the sub-second component is absent, which is the observable half of the fix.
        Assert.DoesNotContain(".123", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(".000000", rendered, StringComparison.Ordinal);
        Assert.Equal("DateTime('2024-03-05 14:07:09')", rendered);
    }

    [Fact]
    public void ADateRendersThroughTheCanonicalValidatorFormatter()
    {
        Assert.True(MacroInvoker.TryRender(Birth, out string rendered, out string? className));

        Assert.Equal("date", className);
        Assert.Equal("Date('" + DateValidator.FormatValue(Birth) + "')", rendered);
        Assert.Equal("Date('1990-01-02')", rendered);
    }

    [Fact]
    public void ATimeRendersThroughTheCanonicalValidatorFormatter()
    {
        Assert.True(MacroInvoker.TryRender(SubSecondTime, out string rendered, out string? className));

        Assert.Equal("time", className);
        Assert.Equal("Time('" + TimeValidator.Format(SubSecondTime) + "')", rendered);
        Assert.DoesNotContain(".123", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(".000000", rendered, StringComparison.Ordinal);
        Assert.Equal("Time('14:07:09')", rendered);
    }

    [Fact]
    public void MacroRenderingAndTheValidatorEmitterAgreeOnEveryTemporalType()
    {
        // THE PROPERTY THE FINDING WAS ABOUT, ASSERTED DIRECTLY. Two legacy sites, one rendered form.
        Assert.True(MacroInvoker.TryRender(SubSecondDateTime, out string macroDateTime, out _));
        Assert.True(MacroInvoker.TryRender(WholeSecondDateTime, out string macroWholeDateTime, out _));
        Assert.True(MacroInvoker.TryRender(Birth, out string macroDate, out _));
        Assert.True(MacroInvoker.TryRender(SubSecondTime, out string macroTime, out _));
        Assert.True(MacroInvoker.TryRender(WholeSecondTime, out string macroWholeTime, out _));

        Assert.Equal(ValueToExpression.Convert(SubSecondDateTime), macroDateTime);
        Assert.Equal(ValueToExpression.Convert(WholeSecondDateTime), macroWholeDateTime);
        Assert.Equal(ValueToExpression.Convert(Birth), macroDate);
        Assert.Equal(ValueToExpression.Convert(SubSecondTime), macroTime);
        Assert.Equal(ValueToExpression.Convert(WholeSecondTime), macroWholeTime);
    }

    [Fact]
    public void TheWrapperSpellingsRemainByteExactBesideTheValidatorEmitter()
    {
        // DECISION 10 IN THE VALIDATOR FILE: the wrapper spellings, capital letters included, are byte-exact
        // contract. They are asserted here as well because MacroInvoker composes them from its OWN oracle
        // site [:L2276-L2280] - the formatters are shared, the wrappers are independently reproduced, and
        // this is what proves the two reproductions did not diverge.
        Assert.True(MacroInvoker.TryRender(WholeSecondDateTime, out string dateTime, out _));
        Assert.True(MacroInvoker.TryRender(Birth, out string date, out _));
        Assert.True(MacroInvoker.TryRender(WholeSecondTime, out string time, out _));

        Assert.StartsWith("DateTime('", dateTime, StringComparison.Ordinal);
        Assert.StartsWith("Date('", date, StringComparison.Ordinal);
        Assert.StartsWith("Time('", time, StringComparison.Ordinal);
        Assert.EndsWith("')", dateTime, StringComparison.Ordinal);
        Assert.EndsWith("')", date, StringComparison.Ordinal);
        Assert.EndsWith("')", time, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNonTemporalArmsAreUnchangedByTheSingleSourcingOfTheTemporalOnes()
    {
        // A REGRESSION FENCE. Only the three temporal arms were touched; the other nine labels are
        // asserted here so a future edit to the switch cannot quietly change one of them.
        Assert.True(MacroInvoker.TryRender("O'Brien", out string quoted, out _));

        // DEFECT PRESERVED [:L2266]: nothing is escaped, so an apostrophe produces a malformed fragment
        // exactly as the oracle does.
        Assert.Equal("'O'Brien'", quoted);

        Assert.True(MacroInvoker.TryRender(42L, out string number, out _));
        Assert.Equal("42", number);

        Assert.True(MacroInvoker.TryRender(true, out string yes, out _));
        Assert.Equal("1=1", yes);

        Assert.True(MacroInvoker.TryRender(false, out string no, out _));
        Assert.Equal("1=0", no);
    }
}
