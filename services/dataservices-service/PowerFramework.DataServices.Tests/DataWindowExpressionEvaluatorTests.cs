// -----------------------------------------------------------------------------------------------------
//  DataWindowExpressionEvaluatorTests.cs
//
//  Parity suite for Expressions/DataWindowExpressionEvaluator.cs, the in-repo reimplementation of the
//  PowerBuilder runtime facility reached as Describe("Evaluate(<expression>,<row>)").
//
//  WHY THESE TESTS EXIST IN THIS SHAPE
//  ----------------------------------
//  The evaluator is the largest single net-new obligation in the refactor (AAP §0.4.2.5, §0.6.5): there is
//  no PowerScript to port and no BCL or package equivalent to substitute, so the ONLY specification is the
//  set of legacy call sites under ws_objects/pfw.datawindow.services.pbl.src/**. Every case below therefore
//  cites the ws_objects locator it characterises, and every expected value is hand-computed from that
//  locator rather than captured from the implementation. Table-driven matrices are expressed as xunit
//  theories with member data, per AAP §0.4.2.8.
//
//  The DataWindow is reached exclusively through the abstract Domain/DataWindowServiceHost contract, so the
//  suite needs no live DataWindow and no PowerBuilder runtime — FakeDataWindowHost supplies the oracle
//  shape. The fixture reproduces ws_objects/pfw.tests.pbl.src/dw_sqlite.srd: six columns
//  (id, name, age, address, salary, birth) plus the footer compute "sum(salary for page)", carrying the
//  format-mask case difference exactly as found — lower-case "[general]" on the columns (dw_sqlite.srd
//  :L21-L26) and upper-case "[GENERAL]" on the compute (:L27). That difference is NOT normalised (C-B).
//
//  COVERAGE (C-H): CI enforces an 80% line-coverage gate per service from Cobertura. The evaluator
//  dominates the DataServices line count, so its function roster, all three quoting shapes, all three
//  sentinel outcomes, the tokenizer escape forms, the numeric promotion ladder and the parser error paths
//  are covered systematically rather than sampled.
// -----------------------------------------------------------------------------------------------------
using System.Globalization;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Validators;
using PowerFramework.Shared.Kernel;

using Xunit;

namespace PowerFramework.DataServices.Tests;

public static class EvaluatorFixture
{
    // The dw_sqlite.srd shape: six columns id/name/age/address/salary/birth plus the footer compute.
    public static FakeDataWindowHost BuildSqliteFixture()
    {
        FakeDataWindowHost host = new();

        host.AddTextObject("id_t", "header");
        host.AddTextObject("name_t", "header");

        host.AddColumn("id", FakeColumnType.Number).Format = "[general]";
        host.AddColumn("name", FakeColumnType.CharOf(100)).Format = "[general]";
        host.AddColumn("age", FakeColumnType.Number).Format = "[general]";
        host.AddColumn("address", FakeColumnType.CharOf(200)).Format = "[general]";
        host.AddColumn("salary", FakeColumnType.DecimalOf(2)).Format = "[general]";
        host.AddColumn("birth", FakeColumnType.Date).Format = "[general]";

        host.AddComputedField("compute_1", "footer", "sum(salary for page)").Format = "[GENERAL]";

        host.AddRow(1L, "Alice", 30L, "Shenzhen", 1000.50m, new DateOnly(1990, 1, 2));
        host.AddRow(2L, "国A", 40L, "Beijing", 2000.25m, new DateOnly(1980, 3, 4));
        host.AddRow(3L, "深圳市南山区", 50L, "深圳市南山区", 3000.00m, new DateOnly(1970, 5, 6));
        host.AddRow(4L, "Dave", 60L, "Xian", null, new DateOnly(1960, 7, 8));

        host.CurrentRow = 1L;
        return host;
    }

    public static DataWindowExpressionEvaluator BuildEvaluator(out FakeDataWindowHost host)
    {
        host = BuildSqliteFixture();
        return new DataWindowExpressionEvaluator(host);
    }
}

public class ExpressionQuotingConventionTests
{
    // One case per verified call-site form. All four must produce the same answer.
    public static TheoryData<string, string> Conventions() => new()
    {
        // Bare - n_cst_dwsvc_columnexp.sru:L750 hands _of_Evaluate a bare expression.
        { "Evaluate(name,1)", "Alice" },
        // Single-quoted - n_cst_dwsvc.sru:L289, contextmenu:L534/:L541/:L617/:L624.
        { "Evaluate('name',1)", "Alice" },
        // Escaped-double-quoted - n_cst_dwsvc.sru:L217.
        { "Evaluate(\"name\",1)", "Alice" },
        // LONE LEADING QUOTE - the runtime shape of n_cst_dwsvc.sru:L195/:L344/:L782.
        { "Evaluate(\"name,1)", "Alice" },
        // Whitespace between the name and the parenthesis, and around the row.
        { "Evaluate ( 'name' , 1 )", "Alice" },
        // Lower-case name - PowerBuilder is case-insensitive (DECISION D1).
        { "evaluate('name',1)", "Alice" },
    };

    [Theory]
    [MemberData(nameof(Conventions))]
    public void EveryQuotingConventionResolvesIdentically(string property, string expected)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(expected, evaluator.Describe(property));
    }

    [Fact]
    public void UnwrapReportsPayloadAndRowForEveryConvention()
    {
        Assert.True(DataWindowExpressionEvaluator.TryUnwrapEvaluateProperty(
            "Evaluate(\"name,7)", out string payload, out long row));
        Assert.Equal("name", payload);
        Assert.Equal(7L, row);

        Assert.True(DataWindowExpressionEvaluator.TryUnwrapEvaluateProperty(
            "Evaluate('LookUpDisplay(name)',3)", out payload, out row));
        Assert.Equal("LookUpDisplay(name)", payload);
        Assert.Equal(3L, row);

        // No row argument at all - the payload's own trailing text is not mistaken for one.
        Assert.True(DataWindowExpressionEvaluator.TryUnwrapEvaluateProperty(
            "Evaluate(Round(salary,2))", out payload, out row));
        Assert.Equal("Round(salary,2)", payload);
        Assert.Equal(DataWindowExpressionEvaluator.NoRowContext, row);

        // A payload containing a comma inside a literal, with a row.
        Assert.True(DataWindowExpressionEvaluator.TryUnwrapEvaluateProperty(
            "Evaluate('a,1',2)", out payload, out row));
        Assert.Equal("a,1", payload);
        Assert.Equal(2L, row);

        Assert.False(DataWindowExpressionEvaluator.TryUnwrapEvaluateProperty(
            "name.ColType", out payload, out row));
    }

    [Fact]
    public void NormaliseHandlesAllFourShapes()
    {
        Assert.Equal("name", DataWindowExpressionEvaluator.NormaliseExpressionPayload("name"));
        Assert.Equal("name", DataWindowExpressionEvaluator.NormaliseExpressionPayload("'name'"));
        Assert.Equal("name", DataWindowExpressionEvaluator.NormaliseExpressionPayload("\"name\""));
        Assert.Equal("name", DataWindowExpressionEvaluator.NormaliseExpressionPayload("\"name"));
        Assert.Equal(string.Empty, DataWindowExpressionEvaluator.NormaliseExpressionPayload(null));
        // A lone leading single quote is left alone, so the tokenizer reports it.
        Assert.Equal("'name", DataWindowExpressionEvaluator.NormaliseExpressionPayload("'name"));
        // A bare expression that merely BEGINS and ENDS with a literal keeps both quotes.
        Assert.Equal("'a' + 'b'", DataWindowExpressionEvaluator.NormaliseExpressionPayload("'a' + 'b'"));
        Assert.Equal("'x' <> 'y'", DataWindowExpressionEvaluator.NormaliseExpressionPayload("'x' <> 'y'"));
        // An escaped quote inside the literal does not close it.
        Assert.Equal("a~'b", DataWindowExpressionEvaluator.NormaliseExpressionPayload("'a~'b'"));
    }
}

public class ExpressionArityTests
{
    // n_cst_dwsvc.sru:L220 - the one-argument arity delegates with row 0.
    public static TheoryData<string> RowIndependentExpressions() => new()
    {
        "Max(Len(LookUpDisplay(name)))",
        "Max(LenA(LookUpDisplay(name)))",
        "RowCount()",
        "sum(salary for all)",
    };

    [Theory]
    [MemberData(nameof(RowIndependentExpressions))]
    public void OneArgumentArityEqualsRowZero(string expression)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(
            evaluator.Evaluate(expression, DataWindowExpressionEvaluator.NoRowContext),
            evaluator.Evaluate(expression));
    }

    [Fact]
    public void RowZeroColumnReferenceIsNullAndNotASentinel()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        DataWindowExpressionResult result = evaluator.TryEvaluate("name");

        Assert.True(result.IsSuccess);
        Assert.True(result.IsNullValue);
        Assert.Equal(string.Empty, result.Text);
    }
}

public class NestedDescribeTests
{
    // The exact construction of n_cst_dwsvc_contextmenu.sru:L1183 and :L1349, with {1} = compute_1.
    private const string LegacyFindExpression =
        "String(compute_1) <> if(GetRow() < RowCount(),Describe(\"Evaluate('compute_1',\""
            + " + String(GetRow() + 1) + \")\"),'')";

    [Fact]
    public void NestedDescribeEvaluatesTheInnerEvaluate()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out FakeDataWindowHost host);
        host.CurrentRow = 1L;

        // The subject here is the NESTED Describe/Evaluate, so the fixture is declared unpaginated rather
        // than left to the refusing default - `sum(salary for page)` needs a stated pagination before it
        // answers anything (DECISION D7a).
        evaluator.PageResolver = WholeBufferPageResolver.Instance;

        // The compute is `sum(salary for page)`; with the whole-buffer resolver it is the same
        // total on every row, so the comparison is false rather than a fault.
        DataWindowExpressionResult result = evaluator.TryEvaluate(LegacyFindExpression, 1L);

        Assert.True(result.IsSuccess);
        Assert.Equal("false", result.Text);
    }

    [Fact]
    public void NestedDescribeSeesADifferentPageTotalWhenPaginationIsSupplied()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out FakeDataWindowHost host)
            ;
        evaluator.PageResolver = new FixedRowsPerPageResolver(2L);
        host.CurrentRow = 2L;

        // Row 2 is on page 1 (rows 1-2, total 3000.75); row 3 is on page 2 (rows 3-4, total 3000.00).
        DataWindowExpressionResult result = evaluator.TryEvaluate(LegacyFindExpression, 2L);

        Assert.True(result.IsSuccess);
        Assert.Equal("true", result.Text);
    }

    [Fact]
    public void NestedDescribeOfAPlainPropertyReturnsTheHostAnswerAsAValue()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal("char(100)", evaluator.Evaluate("Describe('name.ColType')", 1L));
        // A sentinel from a nested property read is a STRING VALUE, not a failure.
        DataWindowExpressionResult result = evaluator.TryEvaluate("Describe('nope.ColType')", 1L);
        Assert.True(result.IsSuccess);
        Assert.Equal("!", result.Text);
    }
}

public class ExpressionFunctionRosterTests
{
    public static TheoryData<string, long, string> Roster() => new()
    {
        // LookUpDisplay - n_cst_dwsvc.sru:L289.
        { "LookUpDisplay(name)", 1L, "Alice" },
        { "LookUpDisplay(salary)", 1L, "1000.50" },
        // The two contextmenu width computations, verbatim.
        { "Max(Len(LookUpDisplay(name)))", 0L, "6" },
        { "Abs(Long(Max(LenA(LookUpDisplay(name)))))", 0L, "12" },
        // Len / LenA on the address column: '深圳市南山区' is six Han characters.
        { "Len(LookUpDisplay(address))", 3L, "6" },
        { "Len(LookUpDisplay(name))", 3L, "6" },
        { "LenA(LookUpDisplay(name))", 3L, "12" },
        { "LenA(LookUpDisplay(address))", 3L, "12" },
        // Numeric conversions.
        { "Abs(0 - age)", 1L, "30" },
        { "Long('17abc')", 0L, "0" },
        { "Long(salary)", 1L, "1000" },
        { "Double(age)", 1L, "30" },
        { "Round(1.005,2)", 0L, "1.01" },
        { "Round(0.5,0)", 0L, "1" },
        // String, if, GetRow, RowCount.
        { "String(age)", 2L, "40" },
        { "String(age,'[GENERAL]')", 2L, "40" },
        { "if(age > 35,'older','younger')", 1L, "younger" },
        { "if(age > 35,'older','younger')", 2L, "older" },
        { "GetRow()", 4L, "1" },
        { "RowCount()", 0L, "4" },
        // IsNull, and the relative row-offset form.
        { "IsNull(salary)", 4L, "true" },
        { "IsNull(salary)", 1L, "false" },
        { "age[1]", 1L, "40" },
        { "if(IsNull(salary),'-',String(salary))", 4L, "-" },
        // Temporal constructors.
        { "Date('1990-01-02')", 0L, "1990-01-02" },
        { "DateTime('1990-01-02 03:04:05')", 0L, "1990-01-02 03:04:05" },
        { "Time('03:04:05')", 0L, "03:04:05" },
        { "Date(birth)", 1L, "1990-01-02" },
        // Lower and LIKE - n_cst_dwsvc_dropdownsearch.sru:L319.
        { "Lower(name)", 1L, "alice" },
        { "(Lower(name) LIKE '%lic%')", 1L, "true" },
        { "(Lower(name) LIKE '%ZZZ%')", 1L, "false" },
        { "(name LIKE 'A_ice')", 1L, "true" },
        // Aggregates.
        { "sum(salary for all)", 0L, "6000.75" },
        { "Max(age)", 0L, "60" },
        // dwValueToExp and the five typed-null producers.
        { "dwValueToExp(name)", 1L, "'Alice'" },
        { "dwValueToExp(salary)", 4L, NumberValidator.NullLiteralExpression },
        { "dwValueToExp(birth)", 1L, "Date('1990-01-02')" },
        { "dwNvlNumber()", 1L, "" },
        { "dwNvlString()", 1L, "" },
        { "dwNvlDate()", 1L, "" },
        { "dwNvlDateTime()", 1L, "" },
        { "dwNvlTime()", 1L, "" },
        // Operators and precedence.
        { "1 + 2 * 3", 0L, "7" },
        { "(1 + 2) * 3", 0L, "9" },
        { "0 - 2 ^ 2", 0L, "-4" },
        { "5 / 2", 0L, "2.5" },
        { "'a' + 'b'", 0L, "ab" },
        { "not 1 = 2", 0L, "true" },
        { "1 = 1 and 2 = 3", 0L, "false" },
        { "1 = 1 or 2 = 3", 0L, "true" },
        { "age >= 30 and age <= 30", 1L, "true" },
        { "'x' <> 'y'", 0L, "true" },
        // PowerScript tilde escapes inside a literal.
        { "Len('a~tb')", 0L, "3" },
        { "Len('a~~b')", 0L, "3" },
        { "'a~~b' + 'c'", 0L, "a~bc" },
    };

    [Theory]
    [MemberData(nameof(Roster))]
    public void EveryRosterEntryProducesItsHandComputedValue(string expression, long row, string expected)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(expected, evaluator.Evaluate(expression, row));
    }

    [Fact]
    public void EveryRequiredNameIsRegistered()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        string[] required =
        [
            "LookUpDisplay", "PinyinFirstLetterLike", "Len", "LenA", "Max", "Sum", "Abs", "Long",
            "Double", "Round", "String", "if", "GetRow", "RowCount", "Describe", "IsNull", "Date",
            "DateTime", "Time", "Lower", ValueToExpression.FunctionName, NumberValidator.FunctionName,
            StringValidator.FunctionName, DateValidator.FunctionName, DateTimeValidator.FunctionName,
            TimeValidator.FunctionName,
        ];

        foreach (string name in required)
        {
            Assert.True(evaluator.IsFunctionRegistered(name), name);
            // Case-insensitive per DECISION D1.
            Assert.True(evaluator.IsFunctionRegistered(name.ToUpperInvariant()), name);
        }

        Assert.Equal(required.Length, evaluator.FunctionNames.Count);

        // C-B: the aggregates the legacy never writes are deliberately absent.
        Assert.False(evaluator.IsFunctionRegistered("Min"));
        Assert.False(evaluator.IsFunctionRegistered("Avg"));
        Assert.False(evaluator.IsFunctionRegistered("Count"));
    }

    [Fact]
    public void TheRosterIsExtensibleAndReplaceable()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        evaluator.RegisterFunction(
            "FormatPrice",
            invocation =>
            {
                if (invocation.ArgumentCount != 2)
                {
                    return invocation.WrongArity("2");
                }

                if (!invocation.TryEvaluateArguments(
                        out ExpressionValue[] values,
                        out DataWindowExpressionResult failure))
                {
                    return failure;
                }

                _ = values[0].TryGetDecimal(out decimal amount);
                _ = values[1].TryGetLong(out long digits);

                return invocation.Success(
                    ExpressionValue.FromDecimal(
                        decimal.Round(amount, (int)digits, MidpointRounding.AwayFromZero)));
            });

        Assert.Equal("1000.5", evaluator.Evaluate("FormatPrice(salary,1)", 1L));

        Assert.True(evaluator.RemoveFunction("FormatPrice"));
        Assert.Equal("!", evaluator.Evaluate("FormatPrice(salary,1)", 1L));
    }
}

public class LenVersusLenATests
{
    [Fact]
    public void LenAndLenADifferAndTheirDifferenceCountsWideCharacters()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // Row 2's name is "国A" - one Han character and one ASCII letter.
        long characters = long.Parse(
            evaluator.Evaluate("Len(LookUpDisplay(name))", 2L),
            CultureInfo.InvariantCulture);
        long bytes = long.Parse(
            evaluator.Evaluate("LenA(LookUpDisplay(name))", 2L),
            CultureInfo.InvariantCulture);

        Assert.Equal(2L, characters);
        Assert.Equal(3L, bytes);

        // The contextmenu computation at :L1132-L1134.
        long wide = Math.Abs(bytes - characters);
        long ascii = characters - wide;

        Assert.Equal(1L, wide);
        Assert.Equal(1L, ascii);
    }
}

public class PageAggregateTests
{
    [Fact]
    public void SumForPageIsRefusedUntilAPaginationIsSupplied()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // The primary fixture's own footer compute is `sum(salary for page)` [dw_sqlite.srd:L27], so this
        // is not a corner case - it is the expression the fixture exercises. Refusing it by default is
        // deliberate: see TheDefaultPageResolverRefusesRatherThanWideningToTheWholeBuffer.
        Assert.IsType<UnresolvedPageResolver>(evaluator.PageResolver);
        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate("sum(salary for page)", 1L));

        // Supplying the pagination is all it takes, and the same expression then answers.
        evaluator.PageResolver = WholeBufferPageResolver.Instance;

        Assert.Equal("6000.75", evaluator.Evaluate("sum(salary for page)", 1L));
    }

    [Fact]
    public void SumForPageHonoursASuppliedPagination()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);
        evaluator.PageResolver = new FixedRowsPerPageResolver(2L);

        Assert.Equal("3000.75", evaluator.Evaluate("sum(salary for page)", 1L));
        Assert.Equal("3000.75", evaluator.Evaluate("sum(salary for page)", 2L));
        Assert.Equal("3000.00", evaluator.Evaluate("sum(salary for page)", 3L));
        Assert.Equal("3000.00", evaluator.Evaluate("sum(salary for page)", 4L));
    }

    [Fact]
    public void TheFooterComputeEvaluatesThroughItsName()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out FakeDataWindowHost host);

        // The fixture's compute is `sum(salary for page)`, so the pagination has to be stated before the
        // compute can answer at all. This test is about resolving a compute BY NAME, not about the page
        // default, so it says the fixture is unpaginated and moves on.
        evaluator.PageResolver = WholeBufferPageResolver.Instance;

        // The compute's own [GENERAL] mask is preserved verbatim beside the columns' [general].
        Assert.Equal("[GENERAL]", host.Describe("compute_1.Format"));
        Assert.Equal("[general]", host.Describe("salary.Format"));
        Assert.True(DataWindowExpressionEvaluator.IsGeneralFormat("[GENERAL]"));
        Assert.True(DataWindowExpressionEvaluator.IsGeneralFormat("[general]"));
        Assert.False(DataWindowExpressionEvaluator.IsGeneralFormat("#,##0.00"));

        Assert.Equal("6000.75", evaluator.Evaluate("compute_1", 1L));
        Assert.Equal("6000.75", evaluator.Describe("Evaluate('compute_1',2)"));
    }

    [Fact]
    public void ForGroupIsARefusedNarrowingAndNotASilentWidening()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        DataWindowExpressionResult result = evaluator.TryEvaluate("sum(salary for group 1)", 1L);

        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, result.Outcome);
        Assert.Equal("!", result.Text);
        Assert.NotNull(result.Error);
        Assert.Contains("/v1/design/**", result.Error!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyBufferSumsToZeroAndMaximisesToNull()
    {
        FakeDataWindowHost host = new();
        host.AddColumn("salary", FakeColumnType.DecimalOf(2));
        DataWindowExpressionEvaluator evaluator = new(host);

        Assert.Equal("0", evaluator.Evaluate("sum(salary for all)"));

        DataWindowExpressionResult maximum = evaluator.TryEvaluate("Max(salary)");
        Assert.True(maximum.IsSuccess);
        Assert.True(maximum.IsNullValue);
    }
}

public class ValueToExpressionRoundTripTests
{
    [Fact]
    public void ANullRoundTripsThroughTheNvlLiteralBackToATypedNull()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // Row 4's salary is null. The oracle's shape: _of_Evaluate("dwValueToExp(" + sExp + ")", row).
        string literal = evaluator.Evaluate("dwValueToExp(salary)", 4L);

        Assert.Equal(NumberValidator.NullLiteralExpression, literal);
        Assert.Equal("dwNvlNumber()", literal);

        DataWindowExpressionResult reparsed = evaluator.TryEvaluate(literal, 4L);

        Assert.True(reparsed.IsSuccess);
        Assert.True(reparsed.IsNullValue);
        Assert.Equal(string.Empty, reparsed.Text);
    }

    [Fact]
    public void EveryTypedLiteralRoundTrips()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // n_cst_dwsvc_columnexp.sru:L2213 and :L2310 splice a substituted literal as "(" + sVal + ")".
        static string Splice(string literal) => "(" + literal + ")";

        Assert.Equal(
            "Alice",
            evaluator.Evaluate(Splice(evaluator.Evaluate("dwValueToExp(name)", 1L)), 1L));
        Assert.Equal(
            "1000.50",
            evaluator.Evaluate(Splice(evaluator.Evaluate("dwValueToExp(salary)", 1L)), 1L));
        Assert.Equal(
            "1990-01-02",
            evaluator.Evaluate(Splice(evaluator.Evaluate("dwValueToExp(birth)", 1L)), 1L));
        Assert.Equal(
            "30",
            evaluator.Evaluate(Splice(evaluator.Evaluate("dwValueToExp(age)", 1L)), 1L));
    }

    [Fact]
    public void TheNullConcatenationHazardIsGuarded()
    {
        // In C# "'" + null + "'" is "''", so a naive port would emit an empty quoted literal instead of
        // the dwNvl*() sentinel. ToDisplayText answers null, never "", which is what keeps the guard live.
        Assert.Null(ExpressionValue.Null.ToDisplayText());
        Assert.Equal(string.Empty, ExpressionValue.EmptyString.ToDisplayText());
        Assert.Equal(
            NumberValidator.NullLiteralExpression,
            ExpressionValue.Null.ToExpressionLiteral());
        Assert.Equal("''", ExpressionValue.EmptyString.ToExpressionLiteral());
    }
}

public class ExpressionSentinelTests
{
    [Fact]
    public void MalformedAnswersTheExclamationSentinel()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        foreach (string malformed in new[]
        {
            "no_such_column",
            "NoSuchFunction(1)",
            "1 +",
            "(1 + 2",
            "'unterminated",
            "name[",
            "age & 1",
            "sum(salary for banana)",
            "String(age,'#,##0.00')",
            "'abc' - 1",
        })
        {
            DataWindowExpressionResult result = evaluator.TryEvaluate(malformed, 1L);

            Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, result.Outcome);
            Assert.Equal(DataWindowExpressionEvaluator.InvalidExpressionSentinel, result.Text);
            Assert.True(result.IsSentinel);
            Assert.False(result.IsSuccess);
            Assert.False(result.IsAbort);
            Assert.NotNull(result.Error);
        }
    }

    [Fact]
    public void AnUndeterminedPropertyAnswersTheQuestionSentinel()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out FakeDataWindowHost host);
        host.SetDescribe("name.Protect", "?");

        DataWindowExpressionResult result = evaluator.TryDescribe("name.Protect");

        Assert.Equal(ExpressionEvaluationOutcome.Undetermined, result.Outcome);
        Assert.Equal(DataWindowExpressionEvaluator.UndeterminedValueSentinel, result.Text);
        Assert.True(result.IsSentinel);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsAbort);

        // And an invalid property read is classified apart from it, with the same observable text.
        DataWindowExpressionResult invalid = evaluator.TryDescribe("nope.Protect");
        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, invalid.Outcome);
        Assert.Equal("!", invalid.Text);
    }

    [Fact]
    public void AnEmptyExpressionAbortsAndIsNotAnEmptyValue()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        foreach (string empty in new[] { "", "   ", "''", "\"\"" })
        {
            DataWindowExpressionResult result = evaluator.TryEvaluate(empty, 1L);

            Assert.Equal(ExpressionEvaluationOutcome.Abort, result.Outcome);
            Assert.Equal(DataWindowExpressionEvaluator.AbortSentinel, result.Text);
            Assert.True(result.IsAbort);
            Assert.False(result.IsSuccess);
            Assert.False(result.IsSentinel);
            Assert.Null(result.Error);
        }

        Assert.True(evaluator.TryEvaluate(null, 1L).IsAbort);
        Assert.True(evaluator.TryDescribe("Evaluate(,1)").IsAbort);
    }

    [Fact]
    public void ASuccessfulEmptyValueIsDistinguishableFromAnAbort()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        DataWindowExpressionResult empty = evaluator.TryEvaluate("if(1=1,'','x')", 1L);
        DataWindowExpressionResult abort = evaluator.TryEvaluate(string.Empty, 1L);
        DataWindowExpressionResult nullValue = evaluator.TryEvaluate("salary", 4L);

        // All three render identically at the boundary, exactly as the oracle renders them.
        Assert.Equal(string.Empty, empty.Text);
        Assert.Equal(string.Empty, abort.Text);
        Assert.Equal(string.Empty, nullValue.Text);

        // And all three are distinguishable on the structured surface.
        Assert.True(empty.IsSuccess);
        Assert.False(empty.IsNullValue);
        Assert.Equal(ExpressionValueKind.String, empty.Value.Kind);

        Assert.True(abort.IsAbort);

        Assert.True(nullValue.IsSuccess);
        Assert.True(nullValue.IsNullValue);
    }

    [Fact]
    public void NullPropagatesAndIsNeverCollapsedToZero()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        foreach (string expression in new[]
        {
            "salary + 1", "salary * 2", "0 - salary", "salary > 0", "salary = salary",
            "Len(salary)", "String(salary)", "Lower(salary)", "not IsNull(1) and salary > 1",
        })
        {
            DataWindowExpressionResult result = evaluator.TryEvaluate(expression, 4L);

            Assert.True(result.IsSuccess, expression);
            Assert.True(result.IsNullValue, expression);
        }

        // Three-valued and/or: the operand that already decides the answer still decides it.
        Assert.Equal("false", evaluator.Evaluate("1 = 2 and salary > 1", 4L));
        Assert.Equal("true", evaluator.Evaluate("1 = 1 or salary > 1", 4L));
    }

    [Fact]
    public void RecursionIsCappedRatherThanExhaustingTheStack()
    {
        FakeDataWindowHost host = EvaluatorFixture.BuildSqliteFixture();
        host.AddComputedField("self", "footer", "self + 1");
        DataWindowExpressionEvaluator evaluator = new(host) { MaximumRecursionDepth = 8 };

        DataWindowExpressionResult result = evaluator.TryEvaluate("self", 1L);

        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, result.Outcome);
        Assert.Equal("!", result.Text);
    }
}

public class PinyinDispatchTests
{
    [Fact]
    public void PinyinReportsTheBlockedCapabilityRatherThanApproximating()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Same(PinyinFirstLetterMatcher.Blocked, evaluator.PinyinMatcher);

        // The exact filter clause of n_cst_dwsvc_dropdownsearch.sru:L323, flags 7.
        DataWindowExpressionResult result =
            evaluator.TryEvaluate("PinyinFirstLetterLike(name,'a',7)", 1L);

        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, result.Outcome);
        Assert.Equal("!", result.Text);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ShortCircuitingKeepsTheBlockedPinyinTermOutOfAnAlreadySatisfiedFilter()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // The whole drop-down filter shape: the display term matches, so the pinyin term is never
        // reached and its blocked capability is never surfaced.
        DataWindowExpressionResult result = evaluator.TryEvaluate(
            "((Lower(name) LIKE '%alic%') OR PinyinFirstLetterLike(name,'a',7))",
            1L);

        Assert.True(result.IsSuccess);
        Assert.Equal("true", result.Text);
    }
}

public class LookUpDisplayTests
{
    [Fact]
    public void ACodeTableSuppliesTheDisplayValue()
    {
        FakeDataWindowHost host = EvaluatorFixture.BuildSqliteFixture();
        host.Column("age").AddCodeTableEntry("Thirty", "30").AddCodeTableEntry("Forty", "40");
        DataWindowExpressionEvaluator evaluator = new(host);

        Assert.Equal("Thirty", evaluator.Evaluate("LookUpDisplay(age)", 1L));
        Assert.Equal("Forty", evaluator.Evaluate("LookUpDisplay(age)", 2L));
        // No entry maps row 3's value, so the display value IS the value.
        Assert.Equal("50", evaluator.Evaluate("LookUpDisplay(age)", 3L));
    }

    [Fact]
    public void ADropDownDataWindowSuppliesTheDisplayValue()
    {
        FakeDataWindowHost host = EvaluatorFixture.BuildSqliteFixture();

        FakeDataWindowChild child = new("dddw_age");
        child.AddColumn("code", FakeColumnType.Number);
        child.AddColumn("label", FakeColumnType.CharOf(20));
        child.AddRow(30L, "Thirty something");
        child.AddRow(40L, "Forty something");
        host.SetChild("age", child);

        host.SetDescribe("age.dddw.name", "dddw_age");
        host.SetDescribe("age.dddw.displaycolumn", "label");
        host.SetDescribe("age.dddw.datacolumn", "code");

        DataWindowExpressionEvaluator evaluator = new(host);

        Assert.Equal("Thirty something", evaluator.Evaluate("LookUpDisplay(age)", 1L));
        Assert.Equal("Forty something", evaluator.Evaluate("LookUpDisplay(age)", 2L));
        Assert.Equal("50", evaluator.Evaluate("LookUpDisplay(age)", 3L));
    }

    [Fact]
    public void LookUpDisplayRequiresAColumnReference()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal("!", evaluator.Evaluate("LookUpDisplay('name')", 1L));
        Assert.Equal("!", evaluator.Evaluate("LookUpDisplay(name,age)", 1L));
        Assert.Equal("!", evaluator.Evaluate("LookUpDisplay(nope)", 1L));

        // A null column value has no display value, and answers null rather than the empty string.
        DataWindowExpressionResult result = evaluator.TryEvaluate("LookUpDisplay(salary)", 4L);
        Assert.True(result.IsSuccess);
        Assert.True(result.IsNullValue);
    }

    [Fact]
    public void ThePositionalColumnFormResolves()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // n_cst_dwsvc.sru:L292 composes "#" + String(colNum); name is data column 2.
        Assert.Equal("Alice", evaluator.Evaluate("LookUpDisplay(#2)", 1L));
        Assert.Equal("Alice", evaluator.Evaluate("#2", 1L));
    }
}

public class ColumnTypeConversionTests
{
    // The six arms of _of_ConvertColType at n_cst_dwsvc.sru:L503-L518.
    public static TheoryData<string, long> ColumnTypes() => new()
    {
        { "char", DataWindowServiceBase.COL_TYPE_STRING },
        { "char(100)", DataWindowServiceBase.COL_TYPE_STRING },
        { "number", DataWindowServiceBase.COL_TYPE_INTEGER },
        { "long", DataWindowServiceBase.COL_TYPE_INTEGER },
        { "ulong", DataWindowServiceBase.COL_TYPE_INTEGER },
        { "decimal(2)", DataWindowServiceBase.COL_TYPE_DECIMAL },
        { "real", DataWindowServiceBase.COL_TYPE_DECIMAL },
        { "datetime", DataWindowServiceBase.COL_TYPE_DATETIME },
        { "date", DataWindowServiceBase.COL_TYPE_DATE },
        { "time", DataWindowServiceBase.COL_TYPE_TIME },
        { "blob", DataWindowServiceBase.COL_TYPE_UNKNOWN },
        { "", DataWindowServiceBase.COL_TYPE_UNKNOWN },
    };

    [Theory]
    [MemberData(nameof(ColumnTypes))]
    public void TheFirstFiveCharactersDecide(string colType, long expected) =>
        Assert.Equal(expected, DataWindowExpressionEvaluator.ConvertColumnType(colType));
}

public class ExpressionValueModelTests
{
    [Fact]
    public void TheVariableBridgeCoversAllSevenDeclaredTypes()
    {
        Assert.Equal(
            ExpressionValueKind.String,
            ExpressionValue.FromVariableValue(ExpressionVariableValue.FromString("x")).Kind);
        Assert.Equal(
            ExpressionValueKind.Long,
            ExpressionValue.FromVariableValue(ExpressionVariableValue.FromLong(1L)).Kind);
        Assert.Equal(
            ExpressionValueKind.Double,
            ExpressionValue.FromVariableValue(ExpressionVariableValue.FromDouble(1d)).Kind);
        Assert.Equal(
            ExpressionValueKind.Boolean,
            ExpressionValue.FromVariableValue(ExpressionVariableValue.FromBoolean(true)).Kind);
        Assert.Equal(
            ExpressionValueKind.Date,
            ExpressionValue.FromVariableValue(
                ExpressionVariableValue.FromDate(new DateOnly(2020, 1, 1))).Kind);
        Assert.Equal(
            ExpressionValueKind.Time,
            ExpressionValue.FromVariableValue(
                ExpressionVariableValue.FromTime(new TimeOnly(1, 2, 3))).Kind);
        Assert.Equal(
            ExpressionValueKind.DateTime,
            ExpressionValue.FromVariableValue(
                ExpressionVariableValue.FromDateTime(new DateTime(2020, 1, 1, 1, 2, 3))).Kind);

        // Unset, and a null-valued variable, both become the one null arm.
        Assert.True(ExpressionValue.FromVariableValue(ExpressionVariableValue.Unset).IsNull);
        Assert.True(ExpressionValue.FromVariableValue(ExpressionVariableValue.FromString(null)).IsNull);
    }

    [Fact]
    public void ComparisonReportsAllFourAnswers()
    {
        Assert.Equal(
            ExpressionComparison.Null,
            DataWindowExpressionEvaluator.CompareValues(
                ExpressionValue.Null,
                ExpressionValue.FromLong(1L)));
        Assert.Equal(
            ExpressionComparison.Incomparable,
            DataWindowExpressionEvaluator.CompareValues(
                ExpressionValue.FromString("1"),
                ExpressionValue.FromLong(1L)));
        Assert.Equal(
            ExpressionComparison.Less,
            DataWindowExpressionEvaluator.CompareValues(
                ExpressionValue.FromLong(1L),
                ExpressionValue.FromDecimal(1.5m)));
        Assert.Equal(
            ExpressionComparison.Equal,
            DataWindowExpressionEvaluator.CompareValues(
                ExpressionValue.FromLong(2L),
                ExpressionValue.FromDecimal(2.0m)));
        Assert.Equal(
            ExpressionComparison.Greater,
            DataWindowExpressionEvaluator.CompareValues(
                ExpressionValue.FromString("b"),
                ExpressionValue.FromString("a")));
        // A time is incomparable with a date; a date and a datetime do compare.
        Assert.Equal(
            ExpressionComparison.Incomparable,
            DataWindowExpressionEvaluator.CompareValues(
                ExpressionValue.FromTime(new TimeOnly(1, 0)),
                ExpressionValue.FromDate(new DateOnly(2020, 1, 1))));
        Assert.Equal(
            ExpressionComparison.Equal,
            DataWindowExpressionEvaluator.CompareValues(
                ExpressionValue.FromDate(new DateOnly(2020, 1, 1)),
                ExpressionValue.FromDateTime(new DateTime(2020, 1, 1, 0, 0, 0))));
    }

    [Fact]
    public void FromObjectTypesEveryShapeADataWindowItemCanCarry()
    {
        Assert.True(ExpressionValue.FromObject(null).IsNull);
        Assert.Equal(ExpressionValueKind.String, ExpressionValue.FromObject("x").Kind);
        Assert.Equal(ExpressionValueKind.Long, ExpressionValue.FromObject(1).Kind);
        Assert.Equal(ExpressionValueKind.Long, ExpressionValue.FromObject((short)1).Kind);
        Assert.Equal(ExpressionValueKind.Decimal, ExpressionValue.FromObject(1m).Kind);
        Assert.Equal(ExpressionValueKind.Double, ExpressionValue.FromObject(1d).Kind);
        Assert.Equal(ExpressionValueKind.Boolean, ExpressionValue.FromObject(true).Kind);
        Assert.Equal(ExpressionValueKind.Date, ExpressionValue.FromObject(DateOnly.MinValue).Kind);
        Assert.Equal(ExpressionValueKind.Time, ExpressionValue.FromObject(TimeOnly.MinValue).Kind);
        Assert.Equal(ExpressionValueKind.DateTime, ExpressionValue.FromObject(DateTime.MinValue).Kind);
        Assert.Equal(ExpressionValueKind.Decimal, ExpressionValue.FromObject(ulong.MaxValue).Kind);
        Assert.Equal(ExpressionValueKind.String, ExpressionValue.FromObject('c').Kind);
        Assert.Equal(
            ExpressionValueKind.Long,
            ExpressionValue.FromObject(ExpressionValue.FromLong(1L)).Kind);
    }
}

// -----------------------------------------------------------------------------------------------------
//  Systematic path coverage (C-H).
//
//  The matrices above characterise the six verified legacy call-site shapes. Those below cover the
//  machinery every one of them runs through - the tokenizer's escape and numeric forms, the numeric
//  promotion ladder, three-valued logic, the aggregate scopes, the column-reference forms and every
//  parser refusal - so the gate is met by systematic coverage rather than by sampling.
//
//  A payload that is EXACTLY ONE COMPLETE LITERAL is read as a delimited payload and its outer quotes are
//  stripped, so a case that needs a bare string literal EVALUATED splices it into a larger expression, in
//  parentheses, exactly as n_cst_dwsvc_columnexp.sru:L2213 and :L2310 do (sVal = "(" + sVal + ")").
// -----------------------------------------------------------------------------------------------------

/// <summary>
/// PowerScript tilde escapes inside a string literal.
/// </summary>
public class TildeEscapeTests
{
    public static TheoryData<string, string> Escapes() => new()
    {
        // The escape the oracle itself writes: n_cst_dwsvc.sru:L217 builds ~" to mean one double quote.
        { "('~~')", "~" },
        { "('~'')", "'" },
        { "('~\"')", "\"" },
        { "('~t')", "\t" },
        { "('~n')", "\n" },
        { "('~r')", "\r" },
        { "('~f')", "\f" },
        { "('~b')", "\b" },
        { "('~v')", "\v" },
        // Hex, octal and decimal numeric forms all decode to 'A' (65).
        { "('~h41')", "A" },
        { "('~o101')", "A" },
        { "('~065')", "A" },
        // An unrecognised escape yields the escaped character itself.
        { "('~q')", "q" },
        // SHORT AND MALFORMED FORMS ARE NOT ERRORS. PowerScript decodes what it can and takes the rest
        // literally, so a prefix with no digits emits the prefix character.
        { "('~h')", "h" },
        { "('~hZ')", "hZ" },
        { "('~o')", "o" },
        // A double-quoted literal decodes the same escapes as a single-quoted one.
        { "(\"~h42~h43\")", "BC" },
    };

    [Theory]
    [MemberData(nameof(Escapes))]
    public void EveryTildeEscapeDecodesAsPowerScriptDoes(string expression, string expected)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(expected, evaluator.Evaluate(expression, 1L));
    }

    [Fact]
    public void AnUnterminatedLiteralIsMalformed()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        DataWindowExpressionResult result = evaluator.TryEvaluate("('abc)", 1L);

        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, result.Outcome);
        Assert.Equal(DataWindowExpressionEvaluator.InvalidExpressionSentinel, result.Text);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ALoneTrailingTildeEscapesTheClosingDelimiter()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // `'a~'` does NOT hold the two characters a and tilde. The tilde escapes the quote that follows
        // it, so the literal is still open when the payload ends and the expression is malformed. Text
        // that really ends in a tilde has to double it.
        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate("('a~')", 1L));
        Assert.Equal("a~", evaluator.Evaluate("('a~~')", 1L));
    }
}

/// <summary>
/// Numeric literal forms.
/// </summary>
public class NumericLiteralTests
{
    public static TheoryData<string, string> Literals() => new()
    {
        { "(7)", "7" },
        { "(7.25)", "7.25" },
        { "(0.5 + 0.25)", "0.75" },
        // An exponent yields binary floating point, so it is funnelled through Long to render exactly.
        { "Long(1e3)", "1000" },
        { "Long(1E3)", "1000" },
        { "Long(1e+3)", "1000" },
        { "Long(1e-3 * 1000)", "1" },
        { "Long(2.5e2)", "250" },
    };

    [Theory]
    [MemberData(nameof(Literals))]
    public void EveryNumericFormParses(string expression, string expected)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(expected, evaluator.Evaluate(expression, 1L));
    }

    [Fact]
    public void AnUnfollowedExponentLetterIsNotConsumed()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // `1e` is the number 1 beside the identifier `e`, which is neither a function nor a column, so the
        // two primaries in a row are a malformed expression rather than a bad number.
        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate("(1e)", 1L));
    }

    [Fact]
    public void ALiteralPastDecimalRangeWidensRatherThanFailing()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // Past decimal's range but inside double's: widening keeps the literal usable rather than
        // rejecting text the oracle accepts. decimal tops out near 7.9e28.
        DataWindowExpressionResult result =
            evaluator.TryEvaluate("(1000000000000000000000000000000.5)", 1L);

        Assert.True(result.IsSuccess);
        Assert.Equal(ExpressionValueKind.Double, result.Value.Kind);

        // An exponent-bearing literal is binary floating point from the outset.
        DataWindowExpressionResult exponent = evaluator.TryEvaluate("(1.0e40)", 1L);

        Assert.True(exponent.IsSuccess);
        Assert.Equal(ExpressionValueKind.Double, exponent.Value.Kind);
    }
}

/// <summary>
/// The numeric promotion ladder (DECISION D3) and the arithmetic refusals.
/// </summary>
public class ArithmeticPromotionTests
{
    public static TheoryData<string, long, string> Cases() => new()
    {
        // Both operands integral and the operator is not division, so the Long arm is preserved and the
        // result renders without a decimal point - which is what keeps `Max(Len(...)) + 1` a Long.
        { "(3 + 4)", 1L, "7" },
        { "(3 - 4)", 1L, "-1" },
        { "(3 * 4)", 1L, "12" },
        // Division always prefers decimal, so an exact-halves quotient renders exactly.
        { "(7 / 2)", 1L, "3.5" },
        // A decimal operand promotes the whole expression to decimal.
        { "(salary + age)", 1L, "1030.50" },
        { "(salary - salary)", 1L, "0.00" },
        // The exponent is always binary floating point, funnelled through Long to render exactly.
        { "Long(2 ^ 10)", 1L, "1024" },
        // Concatenation is the one overload of `+`.
        { "('a' + 'b' + 'c')", 1L, "abc" },
        // NULL PROPAGATES - it is never collapsed to zero (AAP §0.4.5.4). Row 4 has a null salary.
        { "(salary + 1)", 4L, "" },
        { "(1 + salary)", 4L, "" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryPromotionArmHolds(string expression, long row, string expected)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(expected, evaluator.Evaluate(expression, row));
    }

    [Fact]
    public void NullArithmeticIsANullVALUEAndNotAnAbort()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        DataWindowExpressionResult result = evaluator.TryEvaluate("(salary + 1)", 4L);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsNullValue);
        Assert.False(result.IsAbort);
        Assert.Equal(string.Empty, result.Text);
    }

    public static TheoryData<string, long> Refusals() => new()
    {
        // Division by zero.
        { "(7 / 0)", 1L },
        { "(salary / 0)", 1L },
        // PowerBuilder does not coerce text into a number for arithmetic; an expression has to say
        // Long(...) or Double(...) to mean that.
        { "('a' - 1)", 1L },
        { "('a' * 2)", 1L },
        { "('a' / 2)", 1L },
        { "('a' ^ 2)", 1L },
        // A date or a time is not an arithmetic operand either.
        { "(birth + 1)", 1L },
        { "(birth - birth)", 1L },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void EveryArithmeticRefusalIsMalformed(string expression, long row)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        DataWindowExpressionResult result = evaluator.TryEvaluate(expression, row);

        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, result.Outcome);
        Assert.NotNull(result.Error);
    }
}

/// <summary>
/// Unary operators.
/// </summary>
public class UnaryOperatorTests
{
    public static TheoryData<string, long, string> Cases() => new()
    {
        { "(0 - 7)", 1L, "-7" },
        { "(-7)", 1L, "-7" },
        { "(- salary)", 1L, "-1000.50" },
        { "Long(-(2 ^ 3))", 1L, "-8" },
        { "(+7)", 1L, "7" },
        { "(+salary)", 1L, "1000.50" },
        // A boolean's numeric projection is 1 or 0, so its negation is -1 or 0.
        { "(-(1 = 1))", 1L, "-1" },
        { "(-(1 = 2))", 1L, "0" },
        { "(not 1 = 1)", 1L, "false" },
        { "(not 1 = 2)", 1L, "true" },
        // A NUMBER HAS A TRUTH VALUE here: its 1/0 projection is the same coercion that lets
        // `if(IsNull(x),1,0)` be compared as a number at n_cst_dwsvc_contextmenu.sru:L1188. So `not 7` is
        // FALSE rather than malformed, and `not 0` is TRUE.
        { "(not 7)", 1L, "false" },
        { "(not 0)", 1L, "true" },
        // Null propagates through a unary operator.
        { "(-salary)", 4L, "" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryUnaryArmHolds(string expression, long row, string expected)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(expected, evaluator.Evaluate(expression, row));
    }

    public static TheoryData<string> Refusals() => new()
    {
        "(-'a')",
        "(+'a')",
        "(-birth)",
        "(not 'a')",
        "(not birth)",
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void EveryUnaryRefusalIsMalformed(string expression)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate(expression, 1L));
    }
}

/// <summary>
/// Three-valued AND and OR (DECISION D8), and the observable short circuit.
/// </summary>
public class ThreeValuedLogicTests
{
    public static TheoryData<string, long, string> Cases() => new()
    {
        { "(1 = 1 and 2 = 2)", 1L, "true" },
        { "(1 = 1 and 2 = 3)", 1L, "false" },
        { "(1 = 2 and 2 = 2)", 1L, "false" },
        { "(1 = 1 or 2 = 3)", 1L, "true" },
        { "(1 = 2 or 2 = 3)", 1L, "false" },
        // FALSE short-circuits AND, so the null on the right is never reached and the answer is FALSE.
        { "(1 = 2 and salary > 0)", 4L, "false" },
        // TRUE short-circuits OR the same way.
        { "(1 = 1 or salary > 0)", 4L, "true" },
        // With no short circuit available the null propagates, so the answer is NULL, not FALSE.
        { "(1 = 1 and salary > 0)", 4L, "" },
        { "(1 = 2 or salary > 0)", 4L, "" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryLogicArmHolds(string expression, long row, string expected)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(expected, evaluator.Evaluate(expression, row));
    }

    [Fact]
    public void ShortCircuitingKeepsABlockedTermOutOfASatisfiedFilter()
    {
        // The pinyin matcher is BLOCKED without an oracle table, so a term naming it fails. Because the
        // left side of the OR is already TRUE, the legacy never evaluates the right side and the filter
        // still answers - the short circuit is OBSERVABLE, not an optimisation
        // [n_cst_dwsvc_dropdownsearch.sru:L323 builds exactly this OR].
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(
            "true",
            evaluator.Evaluate("(1 = 1 or PinyinFirstLetterLike(name,'a',7))", 1L));
    }

    [Fact]
    public void AnOperandWithNoTruthValueIsMalformed()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate("(name and 1 = 1)", 1L));
    }
}

/// <summary>
/// Comparison, ordering and LIKE.
/// </summary>
public class ComparisonAndLikeTests
{
    public static TheoryData<string, long, string> Cases() => new()
    {
        { "(age = 30)", 1L, "true" },
        { "(age <> 30)", 1L, "false" },
        { "(age < 40)", 1L, "true" },
        { "(age > 40)", 1L, "false" },
        { "(age <= 30)", 1L, "true" },
        { "(age >= 31)", 1L, "false" },
        { "('a' < 'b')", 1L, "true" },
        { "(birth < Date('2000-01-01'))", 1L, "true" },
        { "(salary > 1000)", 1L, "true" },
        // A comparison against NULL is NULL, in either position.
        { "(salary > 0)", 4L, "" },
        { "(0 < salary)", 4L, "" },
        { "(salary = salary)", 4L, "" },
        // LIKE IS CASE-SENSITIVE. The oracle lower-cases BOTH sides before comparing
        // [n_cst_dwsvc_dropdownsearch.sru:L319], which would be dead code if LIKE ignored case.
        { "(name like 'Ali%')", 1L, "true" },
        { "(name like 'ali%')", 1L, "false" },
        { "(Lower(name) like 'ali%')", 1L, "true" },
        { "(name like '%ice')", 1L, "true" },
        { "(name like 'A_ice')", 1L, "true" },
        { "(name like 'A%c_')", 1L, "true" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryComparisonArmHolds(string expression, long row, string expected)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(expected, evaluator.Evaluate(expression, row));
    }

    [Fact]
    public void IncomparableKindsAreMalformedRatherThanUnequal()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // A date against a number is not "not equal" - it is a malformed comparison, so the caller sees
        // the sentinel instead of a plausible FALSE.
        DataWindowExpressionResult result = evaluator.TryEvaluate("(birth = 30)", 1L);

        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, result.Outcome);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void CompareValuesReportsEveryOrdering()
    {
        Assert.Equal(
            ExpressionComparison.Equal,
            DataWindowExpressionEvaluator.CompareValues(
                ExpressionValue.FromLong(7L),
                ExpressionValue.FromDecimal(7m)));
        Assert.Equal(
            ExpressionComparison.Less,
            DataWindowExpressionEvaluator.CompareValues(
                ExpressionValue.FromLong(6L),
                ExpressionValue.FromDouble(6.5d)));
        Assert.Equal(
            ExpressionComparison.Greater,
            DataWindowExpressionEvaluator.CompareValues(
                ExpressionValue.FromString("b"),
                ExpressionValue.FromString("a")));
        Assert.Equal(
            ExpressionComparison.Null,
            DataWindowExpressionEvaluator.CompareValues(
                ExpressionValue.Null,
                ExpressionValue.FromLong(1L)));
        Assert.Equal(
            ExpressionComparison.Incomparable,
            DataWindowExpressionEvaluator.CompareValues(
                ExpressionValue.FromDate(new DateOnly(2000, 1, 1)),
                ExpressionValue.FromLong(1L)));
    }
}

/// <summary>
/// The numeric and text conversion functions.
/// </summary>
public class ConversionFunctionTests
{
    public static TheoryData<string, long, string> Cases() => new()
    {
        { "Long(7)", 1L, "7" },
        { "Long(3.7)", 1L, "3" },
        { "Long(0 - 3.7)", 1L, "-3" },
        // Text that is not a number converts to zero rather than failing, as PowerScript's Long does.
        { "Long('abc')", 1L, "0" },
        { "Long(salary)", 4L, "" },
        { "Long(1 = 1)", 1L, "1" },
        { "Long(Double(7.9))", 1L, "7" },
        { "Long(Double('abc'))", 1L, "0" },
        { "Long(Double(age))", 1L, "30" },
        { "Long(Abs(0 - 5))", 1L, "5" },
        { "Abs(0 - salary)", 1L, "1000.50" },
        { "Abs(salary)", 4L, "" },
        // ROUNDS HALF AWAY FROM ZERO, as PowerScript does - .NET's default is banker's rounding, which
        // would answer 2 and 4 here.
        { "Round(2.5,0)", 1L, "3" },
        { "Round(3.5,0)", 1L, "4" },
        { "Round(0 - 2.5,0)", 1L, "-3" },
        { "Round(salary,1)", 1L, "1000.5" },
        { "Round(salary,0)", 4L, "" },
        { "String(age)", 1L, "30" },
        { "String(name)", 1L, "Alice" },
        { "String(birth)", 1L, "1990-01-02" },
        { "String(salary)", 4L, "" },
        // The general mask is accepted in either case, matching dw_sqlite.srd's own inconsistency between
        // the columns' "[general]" (:L21-L26) and the compute's "[GENERAL]" (:L27).
        { "String(age,'[general]')", 1L, "30" },
        { "String(age,'[GENERAL]')", 1L, "30" },
        { "String(age,'')", 1L, "30" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryConversionArmHolds(string expression, long row, string expected)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(expected, evaluator.Evaluate(expression, row));
    }

    [Fact]
    public void ANonGeneralMaskIsRefusedRatherThanApproximated()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // DECISION D7(c): a DEFINED NARROWING. PowerBuilder's mask dialect is not reproduced, so a mask
        // other than the general one is refused rather than silently mis-formatted.
        DataWindowExpressionResult result = evaluator.TryEvaluate("String(birth,'yyyy/mm/dd')", 1L);

        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, result.Outcome);
        Assert.NotNull(result.Error);
        Assert.False(DataWindowExpressionEvaluator.IsGeneralFormat("yyyy/mm/dd"));
        Assert.True(DataWindowExpressionEvaluator.IsGeneralFormat("[general]"));
        Assert.True(DataWindowExpressionEvaluator.IsGeneralFormat("[GENERAL]"));
        Assert.True(DataWindowExpressionEvaluator.IsGeneralFormat(null));
    }

    public static TheoryData<string> Refusals() => new()
    {
        "Abs('a')",
        "Abs(birth)",
        "Round('a',1)",
        "Round(1.5,'a')",
        "Long(birth)",
        "Double(birth)",
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void EveryConversionRefusalIsMalformed(string expression)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate(expression, 1L));
    }
}

/// <summary>
/// The temporal constructors.
/// </summary>
public class TemporalFunctionTests
{
    [Fact]
    public void DateAcceptsTextADateAndADateTime()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal("1990-01-02", evaluator.Evaluate("Date('1990-01-02')", 1L));
        Assert.Equal("1990-01-02", evaluator.Evaluate("Date(birth)", 1L));
        Assert.Equal("1990-01-02", evaluator.Evaluate("Date(DateTime('1990-01-02 13:45:00'))", 1L));
        // Null in, null out - never a zero date.
        Assert.Equal(string.Empty, evaluator.Evaluate("Date(salary)", 4L));
    }

    [Fact]
    public void TimeAcceptsTextATimeAndADateTime()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal("true", evaluator.Evaluate("(Time('13:45:00') = Time('13:45:00'))", 1L));
        Assert.Equal("true", evaluator.Evaluate("(Time(DateTime('1990-01-02 13:45:00')) = Time('13:45:00'))", 1L));
        Assert.Equal("false", evaluator.Evaluate("(Time('13:45:00') = Time('13:46:00'))", 1L));
        Assert.Equal(string.Empty, evaluator.Evaluate("Time(salary)", 4L));
    }

    [Fact]
    public void DateTimeAcceptsTextADateAndADateTime()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(
            "true",
            evaluator.Evaluate("(DateTime('1990-01-02 00:00:00') = DateTime(birth))", 1L));
        Assert.Equal(
            "true",
            evaluator.Evaluate("(DateTime(DateTime('1990-01-02 13:45:00')) > DateTime(birth))", 1L));
        Assert.Equal(string.Empty, evaluator.Evaluate("DateTime(salary)", 4L));
    }

    public static TheoryData<string> Refusals() => new()
    {
        "Date(30)",
        "Time(30)",
        "DateTime(30)",
        "Date()",
        "Time(birth,1)",
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void EveryTemporalRefusalIsMalformed(string expression)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate(expression, 1L));
    }
}

/// <summary>
/// Aggregate scopes (DECISION D5 and D7).
/// </summary>
public class AggregateScopeTests
{
    [Fact]
    public void TheDefaultScopeIsEveryRow()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // 1000.50 + 2000.25 + 3000.00; row 4's null salary contributes nothing rather than zero.
        Assert.Equal("6000.75", evaluator.Evaluate("sum(salary)", 1L));
        Assert.Equal("6000.75", evaluator.Evaluate("sum(salary for all)", 1L));
        Assert.Equal("60", evaluator.Evaluate("Max(age)", 1L));
    }

    [Fact]
    public void APageResolverNarrowsTheRangeToItsPage()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // DECISION D7(a): `for page` needs a page model, which the headless service does not have, so it
        // is supplied by an injected resolver. Two rows to a page over four rows gives two pages.
        evaluator.PageResolver = new FixedRowsPerPageResolver(2L);

        Assert.Equal(2L, ((FixedRowsPerPageResolver)evaluator.PageResolver).RowsPerPage);
        Assert.Equal("3000.75", evaluator.Evaluate("sum(salary for page)", 1L));
        Assert.Equal("3000.75", evaluator.Evaluate("sum(salary for page)", 2L));
        // Page two is rows 3 and 4, and row 4's salary is null.
        Assert.Equal("3000.00", evaluator.Evaluate("sum(salary for page)", 3L));
        Assert.Equal("3000.00", evaluator.Evaluate("sum(salary for page)", 4L));
        Assert.Equal("40", evaluator.Evaluate("Max(age for page)", 1L));
    }

    [Fact]
    public void TheDefaultPageResolverRefusesRatherThanWideningToTheWholeBuffer()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // DECISION D7(a) AS THE DEFAULT, NOT MERELY AS AN OPTION. A page boundary is computed from band
        // heights and print margins - the deferred DesignSystem's half - so an uninstructed evaluator
        // knows no page range. Defaulting to "the buffer is one page" would answer `sum(salary for page)`
        // with the GRAND TOTAL: right on this four-row fixture, wrong on any paginated surface, and
        // indistinguishable from the page total in either case. AAP 0.1.5 requires the narrowing with a
        // defined error, which is what `for group` already receives.
        Assert.Same(UnresolvedPageResolver.Instance, evaluator.PageResolver);
        Assert.False(
            UnresolvedPageResolver.Instance.TryGetPageRange(1L, 4L, out long first, out long last));

        // The out-parameters carry the empty range, so a caller that ignored the return value still
        // cannot read a plausible span out of them.
        Assert.Equal(0L, first);
        Assert.Equal(0L, last);

        DataWindowExpressionResult refused = evaluator.TryEvaluate("sum(salary for page)", 1L);

        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, refused.Outcome);
        Assert.Equal(DataWindowExpressionEvaluator.InvalidExpressionSentinel, refused.Text);
        Assert.NotNull(refused.Error);

        // AND THE ERROR NAMES THE GAP AND THE WAY OUT, because a refusal a caller cannot act on is
        // only marginally better than a wrong number.
        Assert.Contains("/v1/design/**", refused.Error!.Text, StringComparison.Ordinal);
        Assert.Contains("FixedRowsPerPageResolver", refused.Error.Text, StringComparison.Ordinal);
        Assert.Contains("WholeBufferPageResolver", refused.Error.Text, StringComparison.Ordinal);

        // The grand total is what it would have answered had the scope been widened. It does not.
        Assert.NotEqual("6000.75", refused.Text);
        Assert.Equal("6000.75", evaluator.Evaluate("sum(salary for all)", 1L));
    }

    [Fact]
    public void TheWholeBufferResolverRemainsAvailableAsAnExplicitOptIn()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // IT IS THE RIGHT MODEL FOR AN UNPAGINATED SURFACE - a characterization run over a fixture that
        // fits on one page needs to be able to SAY the buffer is the page. What changed is that it has
        // to be said rather than assumed, so the resulting number is attributable to a stated pagination.
        evaluator.PageResolver = WholeBufferPageResolver.Instance;

        Assert.True(
            WholeBufferPageResolver.Instance.TryGetPageRange(1L, 4L, out long first, out long last));
        Assert.Equal(1L, first);
        Assert.Equal(4L, last);
        Assert.Equal(
            evaluator.Evaluate("sum(salary for all)", 1L),
            evaluator.Evaluate("sum(salary for page)", 1L));
    }

    [Fact]
    public void AGroupScopeIsRefusedRatherThanWidenedToEveryRow()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // DECISION D7(b): a group's row range comes from the DataWindow band model, which is the deferred
        // DesignSystem capability reserved at /v1/design/** (C-D). Answering a plausible total over EVERY
        // row would be wrong in a way no assertion would catch, so the scope is refused.
        DataWindowExpressionResult result = evaluator.TryEvaluate("sum(salary for group 1)", 1L);

        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, result.Outcome);
        Assert.Equal(DataWindowExpressionEvaluator.InvalidExpressionSentinel, result.Text);
        Assert.NotNull(result.Error);
        Assert.NotEqual("6000.75", result.Text);
    }

    [Fact]
    public void AnUnresolvablePageIsRefused()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);
        evaluator.PageResolver = new RefusingPageResolver();

        // A resolver that cannot place the row refuses the aggregate rather than falling back to every
        // row, which would answer a plausible total over the wrong set.
        DataWindowExpressionResult result = evaluator.TryEvaluate("sum(salary for page)", 1L);

        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, result.Outcome);
        Assert.NotNull(result.Error);
        Assert.NotEqual("6000.75", result.Text);
    }

    private sealed class RefusingPageResolver : IExpressionPageResolver
    {
        public bool TryGetPageRange(long row, long rowCount, out long firstRow, out long lastRow)
        {
            firstRow = 0L;
            lastRow = 0L;
            return false;
        }
    }

    [Fact]
    public void ARowsPerPageBelowOneIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedRowsPerPageResolver(0L));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedRowsPerPageResolver(-1L));
    }

    [Fact]
    public void SumRefusesANonNumericColumn()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate("sum(name)", 1L));
    }

    [Fact]
    public void SumOverAnEmptyBufferIsZeroAndMaxIsNull()
    {
        FakeDataWindowHost host = new();
        host.AddColumn("salary", FakeColumnType.DecimalOf(2)).Format = "[general]";
        DataWindowExpressionEvaluator evaluator = new(host);

        // A sum with nothing to add is 0, matching PowerScript's Sum. A MAXIMUM of nothing has no value
        // at all, so it is NULL rather than 0 - collapsing it to 0 would invent a data point.
        Assert.Equal("0", evaluator.Evaluate("sum(salary)", 0L));

        DataWindowExpressionResult maximum = evaluator.TryEvaluate("Max(salary)", 0L);

        Assert.True(maximum.IsSuccess);
        Assert.True(maximum.IsNullValue);
    }
}

/// <summary>
/// Column-reference forms.
/// </summary>
public class ColumnReferenceTests
{
    [Fact]
    public void EveryTypedGetterIsReached()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // The six arms of n_cst_dwsvc_columnexp.sru:L2346-L2356, read through the host's typed getters.
        Assert.Equal("1", evaluator.Evaluate("id", 1L));
        Assert.Equal("Alice", evaluator.Evaluate("name", 1L));
        Assert.Equal("30", evaluator.Evaluate("age", 1L));
        Assert.Equal("Shenzhen", evaluator.Evaluate("address", 1L));
        Assert.Equal("1000.50", evaluator.Evaluate("salary", 1L));
        Assert.Equal("1990-01-02", evaluator.Evaluate("birth", 1L));
    }

    [Fact]
    public void ARowOffsetIsRELATIVEToTheCurrentRow()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // Corroborated by the compute twin at n_cst_dwsvc_contextmenu.sru:L1183, which writes
        // String(GetRow() + 1) where its sibling writes col[1].
        Assert.Equal("国A", evaluator.Evaluate("name[1]", 1L));
        Assert.Equal("Alice", evaluator.Evaluate("name[0]", 1L));
        Assert.Equal("Alice", evaluator.Evaluate("name[-1]", 2L));
        // Off the end of the buffer is NULL, not a sentinel (DECISION D4).
        Assert.Equal(string.Empty, evaluator.Evaluate("name[99]", 1L));
    }

    [Fact]
    public void ARowOutsideTheBufferIsNullAndNotASentinel()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // DECISION D4. Row 0 is the no-row context used at n_cst_dwsvc.sru:L344 and :L782.
        foreach (long row in new[] { 0L, 5L, 99L, -1L })
        {
            DataWindowExpressionResult result = evaluator.TryEvaluate("name", row);

            Assert.True(result.IsSuccess);
            Assert.True(result.IsNullValue);
            Assert.False(result.IsSentinel);
        }
    }

    [Fact]
    public void AnUnknownIdentifierIsMalformed()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        DataWindowExpressionResult result = evaluator.TryEvaluate("no_such_column", 1L);

        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, result.Outcome);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ANonNumericOffsetIsMalformed()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate("name['a']", 1L));
    }

    [Fact]
    public void ANullOffsetYieldsNull()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Equal(string.Empty, evaluator.Evaluate("name[Long(salary)]", 4L));
    }

    [Fact]
    public void ThePositionalColumnFormResolves()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // #2 is composed by n_cst_dwsvc.sru:L244/:L268/:L292/:L348/:L351, so `#n` must tokenize as an
        // identifier rather than starting a comment.
        Assert.Equal("Alice", evaluator.Evaluate("#2", 1L));
    }
}

/// <summary>
/// Parser refusals.
/// </summary>
public class ParserRefusalTests
{
    public static TheoryData<string> Malformed() => new()
    {
        "(1 + )",
        "(1 +",
        "((1 + 2)",
        "(1 + 2))",
        "Len(",
        "Len(name",
        "Len(name,)",
        "(,)",
        "(1 2)",
        "()",
        "Len()",
        "Len(name,age)",
        "GetRow(1)",
        "RowCount(1)",
        "IsNull()",
        "if(1 = 1,'a')",
        "LookUpDisplay()",
        "dwNvlNumber(1)",
        "NoSuchFunction(1)",
        "(1 = )",
        "*",
        ")",
        "[1]",
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void EveryMalformedFormAnswersTheInvalidSentinel(string expression)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        DataWindowExpressionResult result = evaluator.TryEvaluate(expression, 1L);

        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, result.Outcome);
        Assert.Equal(DataWindowExpressionEvaluator.InvalidExpressionSentinel, result.Text);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void AParseErrorCarriesTheExpressionAndACaretPosition()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        DataWindowExpressionResult result = evaluator.TryEvaluate("(1 + )", 1L);

        Assert.NotNull(result.Error);
        // n_cst_dwsvc_columnexp.sru:L1308/:L1383/:L1390 report the expression, a caret position and a
        // message. No MessageBox anywhere (AAP Correction 5).
        Assert.Equal("(1 + )", result.Expression);
        Assert.Equal("(1 + )", result.Error!.Expression);
        Assert.Equal(ExpressionErrorCategory.Expression, result.Error.Category);
        Assert.Equal(ExpressionErrorSeverity.StopSign, result.Error.Severity);
        // These particular messages are NOT localized - the 28 column-expression sites are hardcoded and
        // bypass I18N, unlike the dwsvc/rowselect/contextmenu messages. That inconsistency is legacy
        // behaviour and is reproduced rather than harmonized (AAP §0.4.2.5).
        Assert.False(result.Error.Localized);
        Assert.Equal(0L, result.Error.LocalizationCategory);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result.Error.ReturnCode);
    }

    [Fact]
    public void CreateEvaluationErrorProducesAStructuredError()
    {
        ExpressionParseError error =
            DataWindowExpressionEvaluator.CreateEvaluationError("Len(name", 8L, "Missing ).");

        Assert.Equal("Len(name", error.Expression);
        Assert.Equal(8L, error.CaretPosition);
        Assert.Equal("Missing ).", error.FormatTemplate);
        Assert.Contains("Missing ).", error.Text, StringComparison.Ordinal);
        Assert.NotNull(error.RenderedMarker);
        Assert.Empty(error.FormatArguments);

        // A caret position of zero means there is no caret to render, so the message stands alone.
        ExpressionParseError plain =
            DataWindowExpressionEvaluator.CreateEvaluationError("Len(name", 0L, "Plain message.");

        Assert.Null(plain.CaretPosition);
        Assert.Null(plain.RenderedMarker);
        Assert.Equal("Plain message.", plain.Text);
    }

    [Fact]
    public void RecursionIsBoundedRatherThanOverflowingTheStack()
    {
        FakeDataWindowHost host = EvaluatorFixture.BuildSqliteFixture();
        // A compute whose expression names itself: evaluating it recurses until the depth guard trips.
        host.AddComputedField("loop_1", "footer", "loop_1").Format = "[general]";

        DataWindowExpressionEvaluator evaluator = new(host) { MaximumRecursionDepth = 4 };

        Assert.Equal(4, evaluator.MaximumRecursionDepth);
        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate("loop_1", 1L));
    }

    [Fact]
    public void ARecursionDepthBelowOneIsRejected()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.MaximumRecursionDepth = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.MaximumRecursionDepth = -1);
        Assert.Throws<ArgumentNullException>(() => evaluator.PageResolver = null!);
    }
}

/// <summary>
/// The function registry and its extension hook.
/// </summary>
public class FunctionRegistryTests
{
    [Fact]
    public void TheRosterIsRegisteredUnderItsLegacySpellings()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        foreach (string name in new[]
        {
            "LookUpDisplay", "PinyinFirstLetterLike", "Len", "LenA", "Max", "Sum", "Abs", "Long",
            "Double", "Round", "String", "if", "GetRow", "RowCount", "Describe", "IsNull", "Date",
            "DateTime", "Time", "Lower", "dwValueToExp", "dwNvlNumber", "dwNvlString", "dwNvlDate",
            "dwNvlDateTime", "dwNvlTime",
        })
        {
            Assert.True(evaluator.IsFunctionRegistered(name), name);
            Assert.Contains(name, evaluator.FunctionNames);
        }
    }

    [Fact]
    public void FunctionNamesAreMatchedWithoutRegardToCase()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // DECISION D1: PowerBuilder expression function names are case-insensitive, matched with
        // OrdinalIgnoreCase so the Turkish-I defect cannot reach a comparison.
        Assert.True(evaluator.IsFunctionRegistered("lookupdisplay"));
        Assert.True(evaluator.IsFunctionRegistered("LOOKUPDISPLAY"));
        Assert.True(evaluator.IsFunctionRegistered("dwvaluetoexp"));
        Assert.Equal("5", evaluator.Evaluate("LEN(name)", 1L));
        Assert.Equal("5", evaluator.Evaluate("len(name)", 1L));
        Assert.Equal("30", evaluator.Evaluate("STRING(AGE)", 1L));
    }

    [Fact]
    public void AnUnknownNameIsNotRegistered()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.False(evaluator.IsFunctionRegistered("NoSuchFunction"));
        Assert.False(evaluator.IsFunctionRegistered(null));
        Assert.False(evaluator.IsFunctionRegistered(string.Empty));
    }

    [Fact]
    public void AMacroCanBeRegisteredAndRemoved()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // The hook ColumnExpressionEngine uses to route a macro function through MacroInvoker
        // [docs/n_cst_dwsvc_columnexp.md:§macro functions; se_cst_dw.sru:L14].
        evaluator.RegisterFunction(
            "MyMacro",
            invocation =>
            {
                if (invocation.ArgumentCount != 2)
                {
                    return invocation.WrongArity("2");
                }

                if (!invocation.TryEvaluateArguments(
                        out ExpressionValue[] values,
                        out DataWindowExpressionResult failure))
                {
                    return failure;
                }

                _ = values[0].TryGetDouble(out double value);
                _ = values[1].TryGetLong(out long places);

                // The documented macro example: Round(Double(args[1]),Long(args[2]))
                // [docs/n_cst_dwsvc_columnexp.md:L126].
                return invocation.Success(
                    ExpressionValue.FromDecimal(
                        Math.Round((decimal)value, (int)places, MidpointRounding.AwayFromZero)));
            });

        Assert.True(evaluator.IsFunctionRegistered("MyMacro"));
        Assert.Equal("1000.5", evaluator.Evaluate("MyMacro(salary,1)", 1L));
        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate("MyMacro(salary)", 1L));

        Assert.True(evaluator.RemoveFunction("MyMacro"));
        Assert.False(evaluator.RemoveFunction("MyMacro"));
        Assert.False(evaluator.IsFunctionRegistered("MyMacro"));
        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate("MyMacro(salary,1)", 1L));
    }

    [Fact]
    public void AMacroSeesItsInvocationContext()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out FakeDataWindowHost host);
        ExpressionFunctionInvocation? seen = null;

        evaluator.RegisterFunction(
            "Probe",
            invocation =>
            {
                seen = invocation;
                _ = invocation.TryGetArgumentColumnName(0, out string columnName);
                return invocation.Success(ExpressionValue.FromString(columnName));
            });

        Assert.Equal("name", evaluator.Evaluate("Probe(name)", 2L));
        Assert.NotNull(seen);
        Assert.Equal("Probe", seen!.Name);
        Assert.Equal(1, seen.ArgumentCount);
        Assert.Equal(2L, seen.Row);
        Assert.Equal(ExpressionAggregateScope.All, seen.Scope);
        Assert.Same(host, seen.Host);
        Assert.Same(evaluator, seen.Evaluator);
        Assert.Equal("Probe(name)", seen.Expression);
    }

    [Fact]
    public void AMacroCanEvaluateAnArgumentAgainstAnotherRow()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        evaluator.RegisterFunction(
            "AtRowTwo",
            invocation => invocation.EvaluateArgument(0, 2L));

        Assert.Equal("国A", evaluator.Evaluate("AtRowTwo(name)", 1L));
    }

    [Fact]
    public void AMacroCanReportTheUndeterminedSentinel()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        evaluator.RegisterFunction("Unresolved", invocation => invocation.Undetermined("Cannot resolve."));

        DataWindowExpressionResult result = evaluator.TryEvaluate("Unresolved()", 1L);

        Assert.Equal(ExpressionEvaluationOutcome.Undetermined, result.Outcome);
        Assert.Equal(DataWindowExpressionEvaluator.UndeterminedValueSentinel, result.Text);
        Assert.True(result.IsSentinel);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void AMacroCanReportAStructuredErrorObject()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        ExpressionParseError prepared =
            DataWindowExpressionEvaluator.CreateEvaluationError("Bespoke()", 0L, "Bespoke failure.");
        evaluator.RegisterFunction("Bespoke", invocation => invocation.Invalid(prepared));

        DataWindowExpressionResult result = evaluator.TryEvaluate("Bespoke()", 1L);

        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, result.Outcome);
        Assert.Same(prepared, result.Error);
    }

    [Fact]
    public void RegistrationRejectsAnEmptyNameOrANullFunction()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        Assert.Throws<ArgumentException>(
            () => evaluator.RegisterFunction(" ", invocation => invocation.Success(ExpressionValue.Null)));
        Assert.Throws<ArgumentNullException>(() => evaluator.RegisterFunction("X", null!));
        Assert.Throws<ArgumentNullException>(() => new DataWindowExpressionEvaluator(null!));
    }
}

/// <summary>
/// The diagnostic renderings that keep the three outcomes legible apart.
/// </summary>
public class ResultRenderingTests
{
    [Fact]
    public void EveryOutcomeRendersDistinctly()
    {
        Assert.Equal(
            "Value:7",
            DataWindowExpressionResult.Success(ExpressionValue.FromLong(7L), "7", 1L).ToString());
        Assert.Equal(
            "Invalid:!",
            DataWindowExpressionResult.Invalid(
                DataWindowExpressionEvaluator.CreateEvaluationError("x", 0L, "m"), "x", 1L).ToString());
        Assert.Equal(
            "Undetermined:?",
            DataWindowExpressionResult.Undetermined(
                DataWindowExpressionEvaluator.CreateEvaluationError("x", 0L, "m"), "x", 1L).ToString());
        Assert.Equal("Abort:", DataWindowExpressionResult.Abort("", 1L).ToString());
    }

    [Fact]
    public void AnAbortAndANullValueAreLegibleApartDespiteSharingTheirText()
    {
        DataWindowExpressionResult abort = DataWindowExpressionResult.Abort(string.Empty, 1L);
        DataWindowExpressionResult nullValue =
            DataWindowExpressionResult.Success(ExpressionValue.Null, "salary", 4L);

        // Both render "" at the boundary - the legacy conflation this facility must reproduce (D9) - but
        // the internal distinction stays usable.
        Assert.Equal(string.Empty, abort.Text);
        Assert.Equal(string.Empty, nullValue.Text);
        Assert.NotEqual(abort.ToString(), nullValue.ToString());
        Assert.True(abort.IsAbort);
        Assert.False(nullValue.IsAbort);
        Assert.False(abort.IsSuccess);
        Assert.True(nullValue.IsSuccess);
        Assert.True(nullValue.IsNullValue);
    }

    [Fact]
    public void AValueRendersThroughItsOwnToString()
    {
        Assert.Equal("7", ExpressionValue.FromLong(7L).ToString());
        Assert.Equal("Alice", ExpressionValue.FromString("Alice").ToString());
        // A null has no display text, so the rendering names it rather than showing nothing.
        Assert.Equal("(null)", ExpressionValue.Null.ToString());
    }
}

/// <summary>
/// The value model's coercions and conversions.
/// </summary>
public class ExpressionValueCoercionTests
{
    [Fact]
    public void TextCoercionCoversEveryKind()
    {
        Assert.True(ExpressionValue.FromString("a").TryGetText(out string text));
        Assert.Equal("a", text);
        Assert.True(ExpressionValue.FromLong(7L).TryGetText(out text));
        Assert.Equal("7", text);
        Assert.True(ExpressionValue.FromDecimal(7.50m).TryGetText(out text));
        Assert.Equal("7.50", text);
        Assert.True(ExpressionValue.FromBoolean(true).TryGetText(out text));
        Assert.Equal("true", text);
        Assert.True(ExpressionValue.FromDate(new DateOnly(2000, 1, 2)).TryGetText(out text));
        Assert.Equal("2000-01-02", text);
        // A NULL HAS NO TEXT. The guard must fail rather than yielding "" - concatenating a null in
        // PowerScript propagates the null, and C# would silently produce "" instead.
        Assert.False(ExpressionValue.Null.TryGetText(out text));
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void NumericCoercionCoversEveryKind()
    {
        Assert.True(ExpressionValue.FromLong(7L).TryGetDecimal(out decimal number));
        Assert.Equal(7m, number);
        Assert.True(ExpressionValue.FromDouble(7.5d).TryGetDecimal(out number));
        Assert.Equal(7.5m, number);
        Assert.True(ExpressionValue.FromBoolean(true).TryGetDecimal(out number));
        Assert.Equal(1m, number);
        Assert.True(ExpressionValue.FromBoolean(false).TryGetDecimal(out number));
        Assert.Equal(0m, number);
        Assert.False(ExpressionValue.FromString("a").TryGetDecimal(out _));
        Assert.False(ExpressionValue.Null.TryGetDecimal(out _));
        Assert.False(ExpressionValue.FromDate(new DateOnly(2000, 1, 1)).TryGetDecimal(out _));

        Assert.True(ExpressionValue.FromDecimal(7.5m).TryGetDouble(out double wide));
        Assert.Equal(7.5d, wide);
        Assert.True(ExpressionValue.FromLong(7L).TryGetDouble(out wide));
        Assert.Equal(7d, wide);
        Assert.True(ExpressionValue.FromBoolean(true).TryGetDouble(out wide));
        Assert.Equal(1d, wide);
        Assert.False(ExpressionValue.Null.TryGetDouble(out _));

        Assert.True(ExpressionValue.FromDecimal(7m).TryGetLong(out long integral));
        Assert.Equal(7L, integral);
        // TRUNCATION, not refusal - this is the same coercion that makes Long(3.7) answer 3.
        Assert.True(ExpressionValue.FromDecimal(7.5m).TryGetLong(out integral));
        Assert.Equal(7L, integral);
        Assert.True(ExpressionValue.FromDecimal(-7.5m).TryGetLong(out integral));
        Assert.Equal(-7L, integral);
        // Past long's range there is no truncation that helps, so the coercion fails.
        Assert.False(ExpressionValue.FromDouble(1e30d).TryGetLong(out _));
        Assert.True(ExpressionValue.FromBoolean(true).TryGetLong(out integral));
        Assert.Equal(1L, integral);
        Assert.False(ExpressionValue.Null.TryGetLong(out _));

        // A DOUBLE THAT IS NOT FINITE HAS NO DECIMAL PROJECTION.
        Assert.False(ExpressionValue.FromDouble(double.NaN).TryGetDecimal(out _));
        Assert.False(ExpressionValue.FromDouble(double.PositiveInfinity).TryGetDecimal(out _));
        Assert.False(ExpressionValue.FromDouble(1e40d).TryGetDecimal(out _));
    }

    [Fact]
    public void TruthAndMomentCoercionCoverEveryKind()
    {
        Assert.True(ExpressionValue.FromBoolean(true).TryGetBoolean(out bool flag));
        Assert.True(flag);
        Assert.True(ExpressionValue.FromLong(1L).TryGetBoolean(out flag));
        Assert.True(flag);
        Assert.True(ExpressionValue.FromLong(0L).TryGetBoolean(out flag));
        Assert.False(flag);
        Assert.False(ExpressionValue.FromString("a").TryGetBoolean(out _));
        Assert.False(ExpressionValue.Null.TryGetBoolean(out _));

        Assert.True(ExpressionValue.FromDate(new DateOnly(2000, 1, 2)).TryGetMoment(out DateTime moment));
        Assert.Equal(new DateTime(2000, 1, 2, 0, 0, 0, DateTimeKind.Unspecified), moment);
        Assert.True(ExpressionValue.FromDateTime(new DateTime(2000, 1, 2, 3, 4, 5)).TryGetMoment(out moment));
        Assert.Equal(new DateTime(2000, 1, 2, 3, 4, 5), moment);
        Assert.True(ExpressionValue.FromTime(new TimeOnly(3, 4, 5)).TryGetMoment(out moment));
        Assert.Equal(3, moment.Hour);
        Assert.False(ExpressionValue.Null.TryGetMoment(out _));
        Assert.False(ExpressionValue.FromLong(1L).TryGetMoment(out _));
    }

    [Fact]
    public void ANullableFactoryYieldsNullRatherThanZero()
    {
        // AAP §0.4.5.4: never collapse null to zero.
        Assert.True(ExpressionValue.FromLong((long?)null).IsNull);
        Assert.True(ExpressionValue.FromDecimal((decimal?)null).IsNull);
        Assert.True(ExpressionValue.FromDouble((double?)null).IsNull);
        Assert.True(ExpressionValue.FromBoolean((bool?)null).IsNull);
        Assert.True(ExpressionValue.FromDate(null).IsNull);
        Assert.True(ExpressionValue.FromTime(null).IsNull);
        Assert.True(ExpressionValue.FromDateTime(null).IsNull);
        Assert.True(ExpressionValue.FromString(null).IsNull);
        // An EMPTY string is a VALUE, not a null - the distinction the abort sentinel depends on.
        Assert.False(ExpressionValue.FromString(string.Empty).IsNull);
        Assert.Equal(ExpressionValueKind.String, ExpressionValue.EmptyString.Kind);
    }

    [Fact]
    public void FromObjectMapsEveryRuntimeType()
    {
        Assert.Equal(ExpressionValueKind.Null, ExpressionValue.FromObject(null).Kind);
        Assert.Equal(ExpressionValueKind.String, ExpressionValue.FromObject("a").Kind);
        Assert.Equal(ExpressionValueKind.Boolean, ExpressionValue.FromObject(true).Kind);
        Assert.Equal(ExpressionValueKind.Long, ExpressionValue.FromObject(7L).Kind);
        Assert.Equal(ExpressionValueKind.Long, ExpressionValue.FromObject(7).Kind);
        Assert.Equal(ExpressionValueKind.Long, ExpressionValue.FromObject((short)7).Kind);
        Assert.Equal(ExpressionValueKind.Decimal, ExpressionValue.FromObject(7m).Kind);
        Assert.Equal(ExpressionValueKind.Double, ExpressionValue.FromObject(7d).Kind);
        Assert.Equal(ExpressionValueKind.Double, ExpressionValue.FromObject(7f).Kind);
        Assert.Equal(ExpressionValueKind.Date, ExpressionValue.FromObject(new DateOnly(2000, 1, 1)).Kind);
        Assert.Equal(ExpressionValueKind.Time, ExpressionValue.FromObject(new TimeOnly(1, 2, 3)).Kind);
        Assert.Equal(
            ExpressionValueKind.DateTime,
            ExpressionValue.FromObject(new DateTime(2000, 1, 1, 1, 2, 3)).Kind);
        // Anything else is carried as its text rather than refused, so no host value is lost.
        Assert.Equal(ExpressionValueKind.String, ExpressionValue.FromObject(new object()).Kind);
    }

    [Fact]
    public void ToRawObjectRoundTripsEveryKind()
    {
        Assert.Null(ExpressionValue.Null.ToRawObject());
        Assert.Equal("a", ExpressionValue.FromString("a").ToRawObject());
        Assert.Equal(true, ExpressionValue.FromBoolean(true).ToRawObject());
        Assert.Equal(7L, ExpressionValue.FromLong(7L).ToRawObject());
        Assert.Equal(7m, ExpressionValue.FromDecimal(7m).ToRawObject());
        Assert.Equal(7d, ExpressionValue.FromDouble(7d).ToRawObject());
        Assert.Equal(new DateOnly(2000, 1, 1), ExpressionValue.FromDate(new DateOnly(2000, 1, 1)).ToRawObject());
        Assert.Equal(new TimeOnly(1, 2, 3), ExpressionValue.FromTime(new TimeOnly(1, 2, 3)).ToRawObject());
        Assert.Equal(
            new DateTime(2000, 1, 1, 1, 2, 3),
            ExpressionValue.FromDateTime(new DateTime(2000, 1, 1, 1, 2, 3)).ToRawObject());
    }

    [Fact]
    public void ToExpressionLiteralQuotesTextAndTemporalsAndNeverEmitsEmptyForNull()
    {
        Assert.Equal("7", ExpressionValue.FromLong(7L).ToExpressionLiteral());
        Assert.Equal("7.50", ExpressionValue.FromDecimal(7.50m).ToExpressionLiteral());
        Assert.Equal("'a'", ExpressionValue.FromString("a").ToExpressionLiteral());
        // LEGACY DEFECT, REPRODUCED AND NOT REPAIRED (C-B). dwvaluetoexp.srf:L48 is literally
        // `sVal = "'" + val + "'"` - a raw wrap with NO escaping of any kind - and the sibling
        // Validators/ValueToExpression.cs carries that forward as its DEFECT 7. So a tilde or a quote
        // inside the text is emitted verbatim and the literal does NOT survive a re-parse.
        Assert.Equal("'a~b'", ExpressionValue.FromString("a~b").ToExpressionLiteral());
        Assert.Equal("'a~'b'", ExpressionValue.FromString("a~'b").ToExpressionLiteral());
        Assert.Equal("Date('2000-01-02')", ExpressionValue.FromDate(new DateOnly(2000, 1, 2)).ToExpressionLiteral());
        // 1=1 and 1=0 are how the macro marshalling at n_cst_dwsvc_columnexp.sru:L2264-L2308 spells a
        // boolean, because an expression has no boolean literal.
        Assert.Equal("1=1", ExpressionValue.FromBoolean(true).ToExpressionLiteral());
        Assert.Equal("1=0", ExpressionValue.FromBoolean(false).ToExpressionLiteral());
        // A NULL NEVER RENDERS AS "" - that would be the abort sentinel. It renders as the typed-null
        // producer's own call text instead.
        string nullLiteral = ExpressionValue.Null.ToExpressionLiteral();
        Assert.NotEqual(string.Empty, nullLiteral);
        Assert.Contains("dwNvl", nullLiteral, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryLiteralItProducesParsesBackToTheSameValue()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        foreach (ExpressionValue value in new[]
        {
            ExpressionValue.FromLong(7L),
            ExpressionValue.FromDecimal(7.50m),
            ExpressionValue.FromString("a"),
            ExpressionValue.FromString("plain text"),
            ExpressionValue.FromDate(new DateOnly(2000, 1, 2)),
            ExpressionValue.FromBoolean(true),
            ExpressionValue.FromBoolean(false),
            ExpressionValue.Null,
        })
        {
            // Spliced in parentheses, exactly as n_cst_dwsvc_columnexp.sru:L2213 and :L2310 splice it.
            DataWindowExpressionResult reparsed =
                evaluator.TryEvaluate("(" + value.ToExpressionLiteral() + ")", 1L);

            Assert.True(reparsed.IsSuccess, value.ToExpressionLiteral());
            Assert.Equal(value.IsNull, reparsed.Value.IsNull);

            if (!value.IsNull)
            {
                Assert.Equal(value.ToDisplayText(), reparsed.Value.ToDisplayText());
            }
        }
    }

    [Fact]
    public void KindPredicatesAgreeWithTheKind()
    {
        Assert.True(ExpressionValue.FromLong(1L).IsNumeric);
        Assert.True(ExpressionValue.FromDecimal(1m).IsNumeric);
        Assert.True(ExpressionValue.FromDouble(1d).IsNumeric);
        Assert.False(ExpressionValue.FromString("1").IsNumeric);
        Assert.False(ExpressionValue.Null.IsNumeric);
        Assert.True(ExpressionValue.FromDate(new DateOnly(2000, 1, 1)).IsTemporal);
        Assert.True(ExpressionValue.FromTime(new TimeOnly(1, 1, 1)).IsTemporal);
        Assert.True(ExpressionValue.FromDateTime(new DateTime(2000, 1, 1)).IsTemporal);
        Assert.False(ExpressionValue.FromLong(1L).IsTemporal);
        Assert.True(ExpressionValue.True.TryGetBoolean(out bool t) && t);
        Assert.True(ExpressionValue.False.TryGetBoolean(out bool f) && !f);
    }
}

// ==================================================================================================
//  THE DEPLOYED PAGE-RESOLUTION PATH
//  ------------------------------------------------------------------------------------------------
//  Every other test in this file that exercises `for page` INJECTS a resolver first, and each of them
//  is right to: the class default refuses `for page`, deliberately, so a test that wants a number has
//  to state a pagination. What none of them proved is that a RUNNING SERVICE ever states one - and it
//  did not. The evaluator's class default is UnresolvedPageResolver, so dw_sqlite.srd:L27's
//  `sum(salary for page)`, the only `for page` expression in the repository, answered the malformed
//  sentinel in the deployed path unless some wiring site remembered an assignment.
//
//  THE TESTS BELOW CONTAIN NO ASSIGNMENT TO PageResolver. That absence is the assertion: they walk the
//  same three steps Program.cs walks - read the bound ColumnExpressionOptions, hand its two values to
//  ExpressionPageResolverFactory, pass the result to the three-argument constructor - and then ask the
//  fixture's own footer compute for a number. If any link in that chain is missing, they fail.
// ==================================================================================================

public class DeployedPageResolutionTests
{
    /// <summary>
    /// The default configuration resolves <c>for page</c>: a deployment that states nothing still gets a
    /// number, not the sentinel.
    /// </summary>
    /// <remarks>
    /// THE FINDING, INVERTED INTO AN ASSERTION. A default-constructed ColumnExpressionOptions is exactly
    /// what binding an absent DataServices:ColumnExpression section produces, and appsettings.json states
    /// the same two values explicitly - so this covers both the stated and the unstated deployment.
    /// </remarks>
    [Fact]
    public void TheDefaultConfigurationResolvesForPageWithoutAnyInjection()
    {
        ColumnExpressionOptions configured = new();

        Assert.Equal(ExpressionPageResolution.WholeBuffer, configured.PageResolution);
        Assert.Equal(0, configured.PageRowsPerPage);

        DataWindowExpressionEvaluator evaluator = BuildAsProgramWould(configured, out _);

        // The fixture's OWN footer compute [dw_sqlite.srd:L27], evaluated at row 1 with no property
        // assignment anywhere above. 1000.50 + 2000.25 + 3000.00, with row 4's null salary contributing
        // nothing - the whole four-row buffer, because this deployment states that it is one page.
        Assert.Equal("6000.75", evaluator.Evaluate("sum(salary for page)", 1L));

        // And the resolver really is the one the configuration named, rather than something that happens
        // to answer.
        Assert.IsType<WholeBufferPageResolver>(evaluator.PageResolver);
    }

    /// <summary>
    /// A paginated deployment gets its own pages, again with no injection.
    /// </summary>
    /// <remarks>
    /// The two-row pages are the same partition the injected FixedRowsPerPageResolver test uses, so this
    /// proves the configured path reaches the identical behaviour by a different route: rows 1-2 total
    /// 3000.75 and rows 3-4 total 3000.00, the latter because row 4's salary is null.
    /// </remarks>
    [Fact]
    public void APaginatedConfigurationResolvesItsOwnPagesWithoutAnyInjection()
    {
        ColumnExpressionOptions configured = new()
        {
            PageResolution = ExpressionPageResolution.FixedRowsPerPage,
            PageRowsPerPage = 2,
        };

        DataWindowExpressionEvaluator evaluator = BuildAsProgramWould(configured, out _);

        Assert.Equal("3000.75", evaluator.Evaluate("sum(salary for page)", 1L));
        Assert.Equal("3000.75", evaluator.Evaluate("sum(salary for page)", 2L));
        Assert.Equal("3000.00", evaluator.Evaluate("sum(salary for page)", 3L));
        Assert.Equal("3000.00", evaluator.Evaluate("sum(salary for page)", 4L));

        FixedRowsPerPageResolver resolver =
            Assert.IsType<FixedRowsPerPageResolver>(evaluator.PageResolver);
        Assert.Equal(2L, resolver.RowsPerPage);
    }

    /// <summary>
    /// A deployment that states <c>Unresolved</c> still gets the refusal, because the narrowing remains
    /// available rather than being removed.
    /// </summary>
    /// <remarks>
    /// WIRING THE CAPABILITY MUST NOT DELETE THE DEFINED ERROR. AAP 0.1.5's narrowing is the right answer
    /// for a deployment that genuinely does not know its pagination, so it stays selectable - and a
    /// deployment that selects it has SAID so, which is the whole difference from silently getting it.
    /// </remarks>
    [Fact]
    public void AnUnresolvedConfigurationStillRefusesForPage()
    {
        ColumnExpressionOptions configured = new()
        {
            PageResolution = ExpressionPageResolution.Unresolved,
        };

        DataWindowExpressionEvaluator evaluator = BuildAsProgramWould(configured, out _);

        Assert.IsType<UnresolvedPageResolver>(evaluator.PageResolver);
        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate("sum(salary for page)", 1L));
    }

    /// <summary>
    /// The factory maps each declared mode to exactly one resolver, and refuses anything else.
    /// </summary>
    /// <remarks>
    /// THE UNRECOGNISED ARM THROWS RATHER THAN FALLING BACK, and that is why the factory exists at all: a
    /// <c>default:</c> arm returning the refusing resolver would turn a mistyped setting - or a fourth
    /// enum member somebody forgot to handle - back into the silent sentinel this change removes. The
    /// options validator rejects an undeclared value first, so in a configured service this is
    /// unreachable; it is the backstop for a caller that bypassed validation.
    /// </remarks>
    [Fact]
    public void TheFactoryMapsEveryDeclaredModeAndRefusesAnythingElse()
    {
        Assert.IsType<UnresolvedPageResolver>(
            ExpressionPageResolverFactory.Create(ExpressionPageResolution.Unresolved, 0));
        Assert.IsType<WholeBufferPageResolver>(
            ExpressionPageResolverFactory.Create(ExpressionPageResolution.WholeBuffer, 0));
        Assert.IsType<FixedRowsPerPageResolver>(
            ExpressionPageResolverFactory.Create(ExpressionPageResolution.FixedRowsPerPage, 25));

        // Every declared member is handled, so the switch cannot be partially implemented.
        foreach (ExpressionPageResolution declared in Enum.GetValues<ExpressionPageResolution>())
        {
            Assert.NotNull(ExpressionPageResolverFactory.Create(declared, 1));
        }

        ArgumentOutOfRangeException undeclared = Assert.Throws<ArgumentOutOfRangeException>(
            () => ExpressionPageResolverFactory.Create((ExpressionPageResolution)7, 0));
        Assert.Equal("resolution", undeclared.ParamName);

        // And a fixed page with no row count is refused against the SETTING's name, which is what an
        // operator reading the failure needs rather than a resolver constructor's parameter name.
        ArgumentOutOfRangeException missingCount = Assert.Throws<ArgumentOutOfRangeException>(
            () => ExpressionPageResolverFactory.Create(ExpressionPageResolution.FixedRowsPerPage, 0));
        Assert.Equal("rowsPerPage", missingCount.ParamName);
        Assert.Contains(
            "DataServices:ColumnExpression:PageRowsPerPage",
            missingCount.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The three-argument constructor installs the resolver, and refuses a null one.
    /// </summary>
    /// <remarks>
    /// A CONSTRUCTOR RATHER THAN AN ASSIGNMENT, because an assignment is what a wiring site can forget.
    /// The two shorter constructors are asserted here too, unchanged, so that widening the wiring did not
    /// quietly change the class default that every other `for page` test in this file depends on.
    /// </remarks>
    [Fact]
    public void TheThreeArgumentConstructorInstallsTheResolverAndTheShorterOnesKeepTheRefusingDefault()
    {
        FakeDataWindowHost host = EvaluatorFixture.BuildSqliteFixture();

        Assert.IsType<WholeBufferPageResolver>(
            new DataWindowExpressionEvaluator(
                host,
                PinyinFirstLetterMatcher.Blocked,
                WholeBufferPageResolver.Instance).PageResolver);

        Assert.IsType<UnresolvedPageResolver>(new DataWindowExpressionEvaluator(host).PageResolver);
        Assert.IsType<UnresolvedPageResolver>(
            new DataWindowExpressionEvaluator(host, PinyinFirstLetterMatcher.Blocked).PageResolver);

        Assert.Throws<ArgumentNullException>(
            () => new DataWindowExpressionEvaluator(host, PinyinFirstLetterMatcher.Blocked, null!));
    }

    /// <summary>
    /// Builds an evaluator through exactly the three steps <c>Program.cs</c> performs.
    /// </summary>
    /// <param name="configured">The bound column-expression options.</param>
    /// <param name="host">Receives the fixture host.</param>
    /// <returns>The evaluator.</returns>
    /// <remarks>
    /// DELIBERATELY MIRRORS THE COMPOSITION ROOT LINE FOR LINE, and contains no assignment to
    /// <c>PageResolver</c>. If the registration in <c>Program.cs</c> and this helper ever diverge, the
    /// helper is the one that is wrong - it exists to make the deployed path testable without a host, not
    /// to be a second way of doing it.
    /// </remarks>
    private static DataWindowExpressionEvaluator BuildAsProgramWould(
        ColumnExpressionOptions configured,
        out FakeDataWindowHost host)
    {
        host = EvaluatorFixture.BuildSqliteFixture();

        IExpressionPageResolver resolver = ExpressionPageResolverFactory.Create(
            configured.PageResolution,
            configured.PageRowsPerPage);

        return new DataWindowExpressionEvaluator(host, PinyinFirstLetterMatcher.Blocked, resolver);
    }
}


// ======================================================================================================
//  THE EXPRESSION-ENCODED PROPERTY PROTOCOL
//  -----------------------------------------------------------------------------------------------------
//  n_cst_dwsvc.sru:L186-L196 (_of_GetDWOProp) is the ONLY place in the oracle that decides whether a
//  property holds a literal or an expression, and the decision is made on a single TAB character:
//
//      sExp = #DataWindow.Describe(colName + "." + prop)      // :L188
//      nPos = Pos(sExp,"~t")                                  // :L189
//      if nPos = 0 then
//          return sExp                                        // :L191  the value IS the answer
//      else
//          sExp = "~"" + Mid(sExp,nPos + 1)                   // :L194  prefix ONE double quote
//          return #DataWindow.Describe("Evaluate(" + sExp + "," + String(row) + ")")   // :L195
//      end if
//
//  Two properties of that code are load bearing and are asserted below rather than assumed.
//
//  FIRST, THE NO-TAB ARM MUST NOT EVALUATE. A property whose literal value happens to BE a valid
//  expression - and `RowCount()` is - has to come back as that text, not as its value. Asserting this
//  with an inert literal would pass whether or not the arm evaluates, which is why the case below uses a
//  value that would visibly change if it were evaluated.
//
//  SECOND, THE PREFIXED QUOTE IS NOT A TYPO. The stored conditional form is `literal~texpression"` - it
//  carries a TRAILING quote and no leading one - so :L194 supplies the missing opening quote and the
//  result is a BALANCED `Evaluate("...",0)`. That is also why the sibling helper _of_GetPropExp does the
//  opposite arithmetic and DROPS the trailing character (its own comment reads 取表达式（排除尾部'"'）).
//  Both halves are exercised here, because a port that "cleaned up" either one would corrupt the other.
// ======================================================================================================

public class ExpressionEncodedPropertyTests
{
    /// <summary>
    /// Reaches the two protected halves of the <c>~t</c> protocol, which are protected because the
    /// oracle's own members are <c>protected</c> on <c>se_cst_datawindow</c>'s descendant.
    /// </summary>
    /// <remarks>
    /// A HAND-WRITTEN DOUBLE RATHER THAN REFLECTION, AND IT DERIVES FROM THE SERVICE BASE RATHER THAN
    /// FROM THE HOST. <c>_of_GetDWOProp</c> is a member of <c>n_cst_dwsvc</c>, the SERVICE that attaches
    /// to a DataWindow, not of the DataWindow itself, so the port puts it on
    /// <see cref="DataWindowServiceBase"/> and reaching it means being a service. Deriving here also
    /// exercises the same <c>OnInit</c> attachment the oracle performs on each attached service at
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L576-L580</c>; reflection would reach
    /// the same members while skipping the attachment, and the attachment is the part that can fail.
    /// </remarks>
    private sealed class PropertyReadingService : DataWindowServiceBase
    {
        /// <summary>Reaches <c>_of_GetDWOProp(dwoName, prop)</c>.</summary>
        /// <param name="dwoName">The object name.</param>
        /// <param name="prop">The property name.</param>
        /// <returns>The literal value, the evaluated value, or the empty string for a sentinel.</returns>
        public string ReadProperty(string dwoName, string prop) =>
            GetDataWindowObjectProperty(dwoName, prop);

        /// <summary>Reaches <c>_of_GetDWOText(dwoName)</c>.</summary>
        /// <param name="dwoName">The object name.</param>
        /// <returns>The object's text.</returns>
        public string ReadText(string dwoName) => GetDataWindowObjectText(dwoName);

        /// <summary>Reaches <c>_of_GetPropExp(prop)</c>.</summary>
        /// <param name="prop">A raw property value, possibly in the conditional form.</param>
        /// <returns>The expression half, with its trailing quote removed.</returns>
        public static string ExpressionHalf(string prop) => GetPropertyExpression(prop);
    }

    /// <summary>
    /// Builds the fixture with the DataWindow answering its own <c>Evaluate</c> properties, which is
    /// what the PowerBuilder runtime does and what <see cref="FakeDataWindowHost.DescribeOverride"/>
    /// exists for.
    /// </summary>
    /// <param name="host">Receives the host.</param>
    /// <param name="evaluator">Receives the evaluator wired into that host's property reads.</param>
    /// <returns>The service, already attached to <paramref name="host"/>.</returns>
    private static PropertyReadingService BuildAttachedService(
        out FakeDataWindowHost host,
        out DataWindowExpressionEvaluator evaluator)
    {
        host = EvaluatorFixture.BuildSqliteFixture();

        // The fixture's own compute is `sum(salary for page)` [dw_sqlite.srd:L27], so a pagination has
        // to be stated before any property read can reach it (DECISION D7a).
        DataWindowExpressionEvaluator built = new(host)
        {
            PageResolver = WholeBufferPageResolver.Instance,
        };

        // Only an Evaluate property is answered here; everything else falls through to the taught table
        // by answering null, so a plain property read still behaves exactly as it does elsewhere.
        host.DescribeOverride = property =>
            DataWindowExpressionEvaluator.TryUnwrapEvaluateProperty(property, out _, out _)
                ? built.Describe(property)
                : null;

        evaluator = built;

        PropertyReadingService service = new();
        service.OnInit(host);

        return service;
    }

    [Fact]
    public void ATabEncodedPropertyIsEvaluatedAndAPlainOneIsReturnedAsIs()
    {
        PropertyReadingService service = BuildAttachedService(out FakeDataWindowHost host, out _);

        // THE CONDITIONAL FORM, stored exactly as the DataWindow stores it: the unconditional value,
        // a TAB, the expression, and the trailing quote the oracle relies on [:L194].
        host.SetDescribe("name_t.text", "Name\tif(RowCount() > 2,'many','few')\"");

        // The fixture holds four rows, so the expression answers 'many'. Reading "Name" here instead
        // would mean the tab arm never fired.
        Assert.Equal("many", service.ReadProperty("name_t", "text"));

        // THE NO-TAB ARM. The value is itself a valid expression, chosen precisely so that evaluating
        // it would be observable - it would answer "4" - so this assertion can only pass if :L191
        // returns the text untouched.
        host.SetDescribe("id_t.text", "RowCount()");

        Assert.Equal("RowCount()", service.ReadProperty("id_t", "text"));
        Assert.NotEqual("4", service.ReadProperty("id_t", "text"));
    }

    [Fact]
    public void TheEvaluatedHalfSeesTheRowlessContextTheOracleGivesIt()
    {
        PropertyReadingService service = BuildAttachedService(out FakeDataWindowHost host, out _);

        // :L195 passes String(row) and :L344 / :L782 pass a literal 0. _of_GetDWOProp is the latter
        // shape, so a property expression is evaluated OUTSIDE any row and a COLUMN REFERENCE in it has
        // no value to read.
        //
        // THE PROBE IS A COLUMN AND DELIBERATELY NOT GetRow(). GetRow() answers the DataWindow's CURRENT
        // row and ignores the evaluation row entirely - `{ "GetRow()", 4L, "1" }` in the roster above is
        // that exact behaviour - so it cannot distinguish a rowless evaluation from any other. A column
        // reference can, because row 0 has no row to read it from.
        const string probe = "if(IsNull(name),'no row',name)";

        host.SetDescribe("name_t.text", "Value\t" + probe + "\"");

        Assert.Equal("no row", service.ReadProperty("name_t", "text"));

        // And the same expression at a real row does see one, which is what proves the answer above is
        // the row-zero context rather than a fault. Row 2's name is "国A" [the fixture].
        Assert.Equal("国A", host.Describe("Evaluate(\"" + probe + "\",2)"));
    }

    [Fact]
    public void EitherSentinelNormalisesToTheEmptyStringOnThePropertyReadPath()
    {
        PropertyReadingService service = BuildAttachedService(out FakeDataWindowHost host, out _);

        // _of_GetDWOProp: `if sExp = "!" or sExp = "?" then return ""`. Note this DIFFERS from
        // GetColumnProperty, which passes both sentinels through - the divergence is the oracle's and
        // both sides of it are preserved.
        Assert.Equal(string.Empty, service.ReadProperty("no_such_object", "text"));

        host.SetDescribe("id_t.text", DataWindowExpressionEvaluator.UndeterminedValueSentinel);
        Assert.Equal(string.Empty, service.ReadProperty("id_t", "text"));

        host.SetDescribe("id_t.text", DataWindowExpressionEvaluator.InvalidExpressionSentinel);
        Assert.Equal(string.Empty, service.ReadProperty("id_t", "text"));
    }

    [Fact]
    public void TheTextPropertyDelegationInheritsBothArms()
    {
        PropertyReadingService service = BuildAttachedService(out FakeDataWindowHost host, out _);

        // _of_GetDWOText is one line in the oracle - `return _of_GetDWOProp(dwoName,"text")` - so it
        // must inherit the tab arm, the plain arm and the sentinel normalisation rather than repeat
        // any of them.
        host.SetDescribe("name_t.text", "Name\tString(RowCount())\"");
        Assert.Equal("4", service.ReadText("name_t"));

        host.SetDescribe("id_t.text", "Identifier");
        Assert.Equal("Identifier", service.ReadText("id_t"));

        Assert.Equal(string.Empty, service.ReadText("no_such_object"));
    }

    [Fact]
    public void TheExpressionHalfDropsTheTrailingQuoteThatTheOtherHalfSupplies()
    {
        // _of_GetPropExp: `return Mid(prop,nPos + 1,Len(prop) - nPos - 1)`. The length is ONE SHORT of
        // the remainder, which is the whole of the "excluding the trailing quote" comment.
        Assert.Equal(
            "if(RowCount() > 2,'many','few')",
            PropertyReadingService.ExpressionHalf("Name\tif(RowCount() > 2,'many','few')\""));

        // No tab, so not conditional, so returned unchanged - INCLUDING a format mask, which is the
        // shape n_cst_dwsvc_contextmenu.sru:L1167-L1171 reads.
        Assert.Equal("[general]", PropertyReadingService.ExpressionHalf("[general]"));

        // A value that ends AT the tab has nothing after it to take, and PowerScript's Mid answers the
        // empty string for a non-positive length rather than faulting.
        Assert.Equal(string.Empty, PropertyReadingService.ExpressionHalf("Name\t"));
    }

    [Fact]
    public void APlainPropertyValueIsCarriedAsAStringValueAndNeverEvaluated()
    {
        DataWindowExpressionEvaluator evaluator =
            EvaluatorFixture.BuildEvaluator(out FakeDataWindowHost host);

        // A property whose VALUE is an expression is still just a property: TryDescribe hands back the
        // host's answer, unevaluated, because only an `Evaluate(...)` PROPERTY is an evaluation
        // request. dw_sqlite.srd:L27's compute expression is the realistic case.
        host.SetDescribe("compute_1.Expression", "sum(salary for page)");

        Assert.False(DataWindowExpressionEvaluator.TryUnwrapEvaluateProperty(
            "compute_1.Expression", out _, out _));

        DataWindowExpressionResult result = evaluator.TryDescribe("compute_1.Expression");

        Assert.True(result.IsSuccess);
        Assert.Equal("sum(salary for page)", result.Text);
        Assert.Equal(ExpressionValueKind.String, result.Value.Kind);
        Assert.Equal(DataWindowExpressionEvaluator.NoRowContext, result.Row);
        Assert.Null(result.Error);
    }
}


// ======================================================================================================
//  THE SENTINEL-TO-STRUCTURED-ERROR LINKAGE
//  -----------------------------------------------------------------------------------------------------
//  n_cst_dwsvc_columnexp.sru:L761-L763 is the engine's entire reaction to an evaluator failure:
//
//  if sVal = "?" or sVal = "!" then
//    MessageBox("错误","[" + String(ColExpDatas[index].name) + "]表达式错误:~nExpression:" + sExp,StopSign!)
//    return false
//  end if
//
//  So the contract has TWO halves and this suite asserts both and the joint between them. The evaluator
//  must SURFACE the sentinel as a failure while keeping the sentinel itself as the observable text - if
//  the text changed, the oracle's own equality test would stop firing. The engine must then convert that
//  into a structured error carrying the COLUMN NAME and the EXPRESSION TEXT, because those are the only
//  two values the dialog ever showed and a caller that lost either one could not reproduce the message.
//
//  The third sentinel is not in that test at all. `""` is the CALLER'S ABORT, returned by
//  _of_GetItemExpValue at :L2185-L2186 and by its `case else` arm at :L2358, and it is exactly why
//  dwvaluetoexp.srf may never answer the empty string: an empty answer there would be read as "abandon
//  this calculation" rather than as a value. That invariant is asserted here from the evaluator's side.
// ======================================================================================================

public class SentinelToStructuredErrorTests
{
    /// <summary>
    /// The column whose name the structured error carries, from dw_test_dwsvc_columnexp.srd.
    /// </summary>
    private const string ColumnName = "n1";

    /// <summary>The expression text it carries, from w_test_dwsvc_columnexp.srw:L151.</summary>
    private const string ExpressionText = "$FormatPrice($应收-$未收,$精度)";

    // :L761 tests for both sentinels in one condition, so both must reach the same error site.
    public static TheoryData<string, ExpressionEvaluationOutcome> Sentinels() => new()
    {
        {
            DataWindowExpressionEvaluator.UndeterminedValueSentinel,
            ExpressionEvaluationOutcome.Undetermined
        },
        {
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            ExpressionEvaluationOutcome.InvalidExpression
        },
    };

    [Theory]
    [MemberData(nameof(Sentinels))]
    public void EitherSentinelIsAFailureThatCarriesTheColumnNameAndTheExpression(
        string sentinel,
        ExpressionEvaluationOutcome expected)
    {
        DataWindowExpressionEvaluator evaluator =
            EvaluatorFixture.BuildEvaluator(out FakeDataWindowHost host);
        host.SetDescribe("salary.Protect", sentinel);

        // HALF ONE - the evaluator. Classified as a failure on the structured channel, while the
        // observable text stays the one character the oracle compares against at :L761.
        DataWindowExpressionResult result = evaluator.TryDescribe("salary.Protect");

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(sentinel, result.Text);
        Assert.True(result.IsSentinel);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsAbort);
        Assert.NotNull(result.Error);

        // HALF TWO - the engine's conversion of that failure [:L762], reached the same way
        // Expressions/ColumnExpressionEngine.cs reaches it.
        ExpressionParseError error = ParseErrorFormatter.CreatePlainError(
            ExpressionErrorSite.ExpressionError,
            ColumnName,
            ExpressionText);

        Assert.Equal(ExpressionErrorSite.ExpressionError, error.Site);
        Assert.Equal(762, error.LegacyLine);
        Assert.Equal(ExpressionErrorFamily.PlainMessage, error.Family);
        Assert.Equal(ExpressionErrorCategory.Expression, error.Category);
        Assert.Equal(ExpressionErrorSeverity.StopSign, error.Severity);
        Assert.Equal(ExpressionErrorCatalog.LegacyTitle, error.Title);

        // BOTH VALUES SURVIVE AS ARGUMENTS, not merely interpolated into prose, so a consumer can
        // render them itself instead of parsing them back out of the message.
        Assert.Equal(ColumnName, error.FormatArguments[0]);
        Assert.Equal(ExpressionText, error.FormatArguments[1]);

        // And the rendered message is the oracle's, hardcoded Chinese included - these particular
        // messages do NOT route through I18N, unlike the dwsvc and contextmenu ones, and that
        // inconsistency is reproduced rather than harmonised (AAP §0.2.1.3 Correction 5).
        Assert.Contains("[" + ColumnName + "]", error.Text, StringComparison.Ordinal);
        Assert.Contains(ExpressionText, error.Text, StringComparison.Ordinal);
        Assert.Contains("表达式错误", error.Text, StringComparison.Ordinal);
        Assert.Contains("Expression:", error.Text, StringComparison.Ordinal);

        // The caret members belong to the parse family and this site is not in it, so there is no
        // caret to report and none is invented.
        Assert.Null(error.CaretPosition);
    }

    [Fact]
    public void TheAbortSentinelIsNeitherFailureNorValueAndTheLiteralConverterNeverProducesIt()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        DataWindowExpressionResult abort = evaluator.TryEvaluate(string.Empty, 1L);

        Assert.True(abort.IsAbort);
        Assert.Equal(DataWindowExpressionEvaluator.AbortSentinel, abort.Text);
        Assert.False(abort.IsSentinel);
        Assert.False(abort.IsSuccess);

        // No error object, because an abort is not a failure - :L761 never sees it and no dialog was
        // ever shown for it.
        Assert.Null(abort.Error);

        // THE INVARIANT THAT FOLLOWS FROM :L2185-L2186 AND :L2358. Every dwvaluetoexp.srf overload,
        // null included, must answer something the caller cannot mistake for an abort.
        foreach (string literal in new[]
        {
            ValueToExpression.Convert((decimal?)null),
            ValueToExpression.Convert((short?)null),
            ValueToExpression.Convert((long?)null),
            ValueToExpression.Convert((double?)null),
            ValueToExpression.Convert((float?)null),
            ValueToExpression.Convert((string?)null),
            ValueToExpression.Convert((DateTime?)null),
            ValueToExpression.Convert((DateOnly?)null),
            ValueToExpression.Convert((TimeOnly?)null),

            // The genuinely EMPTY string is the case the guard exists to keep distinct: it converts
            // to the two-character literal '' and never to nothing at all.
            ValueToExpression.Convert(string.Empty),
        })
        {
            Assert.NotEqual(DataWindowExpressionEvaluator.AbortSentinel, literal);
        }

        Assert.Equal("''", ValueToExpression.Convert(string.Empty));
        Assert.Equal(StringValidator.NullLiteralExpression, ValueToExpression.Convert((string?)null));
    }

    [Fact]
    public void NoneOfTheThreeSentinelsIsEverThrown()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // The oracle `choose case`s on the returned text; it has no exception handler around
        // _of_Evaluate anywhere. A port that threw would convert a branch the caller takes into a
        // fault it does not catch, so every shape that produces a sentinel must RETURN it.
        foreach (string expression in new[]
        {
            // Abort.
            "", "   ", "''",

            // Invalid.
            "!", "1 +", "(1 + 2", "'unterminated", "NoSuchFunction(1)", "no_such_column",

            // The sentinel characters themselves as input, which must not be mistaken for output.
            "?", "'?'", "'!'",
        })
        {
            Assert.Null(Record.Exception(() => { _ = evaluator.Evaluate(expression, 1L); }));
            Assert.Null(Record.Exception(() => { _ = evaluator.TryEvaluate(expression, 1L); }));
            Assert.Null(Record.Exception(() => { _ = evaluator.Describe(expression); }));
            Assert.Null(Record.Exception(() => { _ = evaluator.TryDescribe(expression); }));
        }

        // A BARE QUOTED SENTINEL IS NOT A VALUE, AND THAT IS DELIBERATE. A payload that is exactly one
        // complete literal is ambiguous - it could be a quoted payload or a lone string literal - and
        // NormaliseExpressionPayload resolves it as the DELIMITER, because that is the shape all six
        // legacy call sites produce. So `'?'` normalises to the bare sentinel character and is malformed,
        // which is the same resolution that makes `''` the abort at n_cst_dwsvc_columnexp.sru:L745.
        Assert.Equal(
            ExpressionEvaluationOutcome.InvalidExpression,
            evaluator.TryEvaluate("'?'", 1L).Outcome);

        // The oracle reaches a lone literal the way it always does, by PARENTHESISING it before
        // evaluation - `sVal = "(" + sVal + ")"` at :L2213 and :L2310 - and then it is a value whose text
        // happens to be a sentinel character. Same text, different outcome: the distinction lives on the
        // structured channel, never in the text.
        DataWindowExpressionResult quoted = evaluator.TryEvaluate("('?')", 1L);

        Assert.Equal(ExpressionEvaluationOutcome.Value, quoted.Outcome);
        Assert.Equal(DataWindowExpressionEvaluator.UndeterminedValueSentinel, quoted.Text);
        Assert.False(quoted.IsSentinel);
        Assert.True(quoted.IsSuccess);

        Assert.Equal(ExpressionEvaluationOutcome.Value, evaluator.TryEvaluate("('!')", 1L).Outcome);
        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate("('!')", 1L));
    }
}



// ======================================================================================================
//  THE LEGACY EXPRESSION CORPUS, EVALUATED
//  -----------------------------------------------------------------------------------------------------
//  Everything above tests shapes this port CHOSE. This suite tests the shapes the legacy actually WROTE,
//  taken verbatim from ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw, which is the only place
//  in the repository where the expression corpus is exercised end to end. Its variable table is:
//
//      :L104  of_AddVarExp("损耗","1")            1        the surcharge
//      :L105  of_AddVarExp("倍率","2")            2        the multiplier
//      :L106  of_AddVarExp("回程","'3'")          '3'      A STRING, and this is the whole point
//      :L107  of_AddVarExp("上月读数","4")        4        last month's reading
//      :L108  of_AddVarExp("本月读数","5")        5        this month's reading
//      :L109  of_AddVar("精度",0)                 0        the precision, a plain long
//
//  and its four macro bodies plus the indirect selector are:
//
//      :L111  num1 = $FormatPrice($上月读数,$精度)
//      :L112  num2 = $FormatPrice($本月读数,$精度)
//      :L113  exp1 = $FormatPrice($本月读数*$倍率+$损耗,$精度)
//      :L114  exp2 = $FormatPrice(($本月读数+dec($回程))*$倍率+$损耗,$精度)
//      :L116  n1   = $$(if($num1-$num2>0 ,'exp1','exp2'))
//      :L140  数据汇总 = SUM(n1)                            (on the SECOND DataWindow)
//
//  This suite evaluates each body in its POST-EXPANSION form, because expansion is
//  Expressions/ColumnExpressionEngine.cs's job and this file is the substrate underneath it. FormatPrice
//  is registered exactly as docs/n_cst_dwsvc_columnexp.md:L126 defines the application's own macro body,
//  `Round(Double(args[1]),Long(args[2]))`, because the oracle expects the APPLICATION to implement the
//  macro switch and a test that invented a different body would be testing its own arithmetic.
//
//  ---------------------------------------------------------------------------------------------------
//  FINDING F1 - `dec(...)` IS NOT IN THE REGISTRY, AND :L114 IS THE SITE THAT NEEDS IT
//  ---------------------------------------------------------------------------------------------------
//  RegisterBuiltInFunctions registers twenty-six names and `dec` is not among them, nor is it reachable
//  as a cast or an alias. But :L114 writes `dec($回程)` for a concrete reason: 回程 is declared as the
//  STRING '3' at :L106, so without a numeric coercion the surrounding `$本月读数 + ...` would be adding a
//  string to a number. `dec` is the coercion, and it is the only variable in that table that needs one.
//
//  The consequence is that the legacy `exp2` body cannot be evaluated by this port as written: it answers
//  the malformed sentinel. That is REPORTED here rather than tested around, and rather than patched from
//  a test file - the roster is a documented decision surface in the unit under test, and widening it is
//  that file's change to make, not this one's. The row below pins the observable behaviour so the finding
//  is a failing capability with a name rather than a silent gap, and the row after it evaluates the same
//  body with the coercion already applied, which demonstrates that ONLY the coercion is missing and the
//  rest of the shape - grouping, multiplication, addition and the macro call - is sound.
// ======================================================================================================

public class LegacyMacroExpressionShapeTests
{
    // The variable table of w_test_dwsvc_columnexp.srw:L104-L109, as post-expansion expression text.
    private const string Surcharge = "1";
    private const string Multiplier = "2";
    private const string ReturnTrip = "'3'";
    private const string LastMonth = "4";
    private const string ThisMonth = "5";
    private const string Precision = "0";

    /// <summary>
    /// Builds the dw_test_dwsvc_columnexp.srd shape: three <c>decimal(2)</c> columns and two
    /// <c>char(100)</c> columns.
    /// </summary>
    /// <returns>The host.</returns>
    /// <remarks>
    /// The values are the ones the macro bodies above compute, so <c>SUM(n1)</c> at <c>:L140</c> has
    /// something to total: <c>exp1</c> answers 11 and <c>exp2</c> answers 17. The column ids in the
    /// oracle run 1, 2, 3 for the numbers and 4, 5 for the strings even though the source declares
    /// <c>s2</c> before <c>s1</c> [dw_test_dwsvc_columnexp.srd:L8-L12, :L20-L24]; that ordering is not
    /// reproduced here because nothing in this suite reads a column positionally.
    /// </remarks>
    private static FakeDataWindowHost BuildColumnExpFixture()
    {
        FakeDataWindowHost host = new();

        host.AddColumn("n1", FakeColumnType.DecimalOf(2)).Format = "[general]";
        host.AddColumn("n2", FakeColumnType.DecimalOf(2)).Format = "[general]";
        host.AddColumn("n3", FakeColumnType.DecimalOf(2)).Format = "[general]";
        host.AddColumn("s1", FakeColumnType.CharOf(100)).Format = "[general]";
        host.AddColumn("s2", FakeColumnType.CharOf(100)).Format = "[general]";

        host.AddRow(11m, 4m, 5m, "exp1", "num1");
        host.AddRow(17m, 6m, 7m, "exp2", "num2");

        host.CurrentRow = 1L;
        return host;
    }

    /// <summary>
    /// Registers the application's own <c>FormatPrice</c> macro, exactly as the legacy documentation
    /// implements it.
    /// </summary>
    /// <param name="evaluator">The evaluator to register into.</param>
    /// <remarks>
    /// docs/n_cst_dwsvc_columnexp.md:L124-L127 - the <c>OnColumnExpInvokeMethod</c> body is
    /// <c>choose case name / case "FormatPrice" / return Round(Double(args[1]),Long(args[2]))</c>. The
    /// macro switch belongs to the APPLICATION, not to the framework, which is why the roster does not
    /// carry this name and why the contract inverts the stream for macro invocation (AAP §0.4.3 C-04).
    /// </remarks>
    private static void RegisterFormatPrice(DataWindowExpressionEvaluator evaluator)
    {
        evaluator.RegisterFunction(
            "FormatPrice",
            invocation =>
            {
                if (invocation.ArgumentCount != 2)
                {
                    return invocation.WrongArity("2");
                }

                if (!invocation.TryEvaluateArguments(
                        out ExpressionValue[] values,
                        out DataWindowExpressionResult failure))
                {
                    return failure;
                }

                // Double(args[1]) then Long(args[2]), in the oracle's own order.
                if (!values[0].TryGetDecimal(out decimal amount)
                    || !values[1].TryGetLong(out long digits))
                {
                    return invocation.Invalid(
                        "FormatPrice expects an amount and a digit count, as "
                        + "docs/n_cst_dwsvc_columnexp.md:L126 defines it.");
                }

                return invocation.Success(
                    ExpressionValue.FromDecimal(
                        decimal.Round(amount, (int)digits, MidpointRounding.AwayFromZero)));
            });
    }

    // Every row is a legacy expression body in its post-expansion form, with its own locator.
    public static TheoryData<string, string, long, string> CorpusBodies() => new()
    {
        // :L111  num1 = FormatPrice(4,0) = 4
        {
            "w_test_dwsvc_columnexp.srw:L111",
            "FormatPrice(" + LastMonth + "," + Precision + ")",
            1L,
            "4"
        },

        // :L112  num2 = FormatPrice(5,0) = 5
        {
            "w_test_dwsvc_columnexp.srw:L112",
            "FormatPrice(" + ThisMonth + "," + Precision + ")",
            1L,
            "5"
        },

        // :L113  exp1 = FormatPrice(5*2+1,0) = FormatPrice(11,0) = 11. The MULTIPLICATION and ADDITION
        // forms, with the precedence that makes it 11 rather than 12.
        {
            "w_test_dwsvc_columnexp.srw:L113",
            "FormatPrice(" + ThisMonth + "*" + Multiplier + "+" + Surcharge + "," + Precision + ")",
            1L,
            "11"
        },

        // :L114  exp2, VERBATIM. FINDING F1: `dec` is unregistered, so the whole body is malformed.
        {
            "w_test_dwsvc_columnexp.srw:L114 (FINDING F1)",
            "FormatPrice((" + ThisMonth + "+dec(" + ReturnTrip + "))*" + Multiplier + "+" + Surcharge
                + "," + Precision + ")",
            1L,
            DataWindowExpressionEvaluator.InvalidExpressionSentinel
        },

        // :L114 with the coercion already applied - FormatPrice((5+3)*2+1,0) = FormatPrice(17,0) = 17.
        // This is the row that proves F1 is ONLY the missing coercion: the grouping, the multiplication,
        // the addition and the macro call all behave.
        {
            "w_test_dwsvc_columnexp.srw:L114 (coercion applied)",
            "FormatPrice((" + ThisMonth + "+3)*" + Multiplier + "+" + Surcharge + "," + Precision + ")",
            1L,
            "17"
        },

        // :L116  the SELECTOR, which is a COMPARISON INSIDE if(...): if(4-5>0,'exp1','exp2'). The
        // SUBTRACTION form, and the answer is the NAME of another variable because the outer $$( )
        // makes the whole thing an indirect dynamic reference.
        {
            "w_test_dwsvc_columnexp.srw:L116",
            "if(" + LastMonth + "-" + ThisMonth + ">0,'exp1','exp2')",
            1L,
            "exp2"
        },

        // :L116 with the operands swapped, so the other arm is taken too.
        {
            "w_test_dwsvc_columnexp.srw:L116 (other arm)",
            "if(" + ThisMonth + "-" + LastMonth + ">0,'exp1','exp2')",
            1L,
            "exp1"
        },

        // :L140  数据汇总 = SUM(n1) over the whole buffer: 11 + 17 = 28.
        { "w_test_dwsvc_columnexp.srw:L140", "SUM(n1)", 0L, "28" },

        // :L151  n2 = FormatPrice($应收-$未收,$精度) with the two operands read from COLUMNS rather than
        // from variables - n3 - n2 at row 1 is 5 - 4 = 1.
        { "w_test_dwsvc_columnexp.srw:L151", "FormatPrice(n3-n2," + Precision + ")", 1L, "1" },

        // :L154  n3 = FormatPrice($未收+$实收,$$精度) - the ADDITION form over two columns, 4 + 5 = 9.
        { "w_test_dwsvc_columnexp.srw:L154", "FormatPrice(n2+n3," + Precision + ")", 1L, "9" },

        // :L157  n1 = FormatPrice(n3-n2,$$('精度')) + $$数据汇总 - the two halves composed, with the
        // aggregate half already resolved to its value: 1 + 28 = 29.
        { "w_test_dwsvc_columnexp.srw:L157", "FormatPrice(n3-n2," + Precision + ")+SUM(n1)", 1L, "29" },

        // The MULTIPLICATION form over two columns, which no single legacy line writes on its own but
        // which :L113 depends on: 4 * 5 = 20.
        { "w_test_dwsvc_columnexp.srw:L113 (operator only)", "n2*n3", 1L, "20" },
    };

    [Theory]
    [MemberData(nameof(CorpusBodies))]
    public void EveryLegacyCorpusBodyEvaluatesToItsHandComputedValue(
        string locator,
        string expression,
        long row,
        string expected)
    {
        FakeDataWindowHost host = BuildColumnExpFixture();
        DataWindowExpressionEvaluator evaluator = new(host)
        {
            PageResolver = WholeBufferPageResolver.Instance,
        };
        RegisterFormatPrice(evaluator);

        // The locator travels with the assertion so a failure names the legacy line it broke.
        Assert.Equal(expected, evaluator.Evaluate(expression, row));
        Assert.False(string.IsNullOrEmpty(locator));
    }

    [Fact]
    public void FindingF1TheNumericCoercionTheLegacyCorpusUsesIsNotRegistered()
    {
        FakeDataWindowHost host = BuildColumnExpFixture();
        DataWindowExpressionEvaluator evaluator = new(host);
        RegisterFormatPrice(evaluator);

        // THE FINDING, stated as an executable fact rather than as a comment. `dec` is absent from the
        // roster in every casing, so w_test_dwsvc_columnexp.srw:L114 cannot be evaluated as written.
        Assert.False(evaluator.IsFunctionRegistered("dec"));
        Assert.False(evaluator.IsFunctionRegistered("Dec"));
        Assert.False(evaluator.IsFunctionRegistered("DEC"));
        Assert.False(evaluator.IsFunctionRegistered("decimal"));

        // An unregistered name is the MALFORMED sentinel and not an exception, which is the general
        // contract this particular gap happens to exercise.
        DataWindowExpressionResult result = evaluator.TryEvaluate("dec(" + ReturnTrip + ")", 1L);

        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, result.Outcome);
        Assert.Equal(DataWindowExpressionEvaluator.InvalidExpressionSentinel, result.Text);
        Assert.NotNull(result.Error);

        // AND THE COERCION IS GENUINELY NEEDED, WHICH IS WHAT MAKES THIS A FINDING RATHER THAN A PEDANTIC
        // ROSTER COMPLAINT - and the mechanism is worse than a plain refusal. 回程 is the STRING '3'
        // [:L106], and `+` with a string on either side is CONCATENATION, not addition. So without the
        // coercion the sub-expression :L114 writes silently answers the TEXT "53" instead of the number 8.
        DataWindowExpressionResult concatenated =
            evaluator.TryEvaluate(ThisMonth + "+" + ReturnTrip, 1L);

        Assert.Equal(ExpressionEvaluationOutcome.Value, concatenated.Outcome);
        Assert.Equal(ExpressionValueKind.String, concatenated.Value.Kind);
        Assert.Equal("53", concatenated.Text);

        // The same addition over two NUMBERS is arithmetic and stays integral, which isolates the
        // degradation to the operand's type rather than to the operator.
        DataWindowExpressionResult added = evaluator.TryEvaluate(ThisMonth + "+3", 1L);

        Assert.Equal(ExpressionValueKind.Long, added.Value.Kind);
        Assert.Equal("8", added.Text);

        // WHAT ACTUALLY CATCHES IT IS THE NEXT OPERATOR, NOT THE ADDITION. `*` has no string arm, so the
        // wrong intermediate is refused one step later - which is why the full :L114 body answers the
        // malformed sentinel rather than a wrong number. A shorter legacy expression that stopped at the
        // addition would have propagated "53" as a value.
        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate("(" + ThisMonth + "+" + ReturnTrip + ")*" + Multiplier, 1L));
    }

    [Fact]
    public void TheMacroIsInvokedThroughTheRegistryAndSeesItsOwnContext()
    {
        FakeDataWindowHost host = BuildColumnExpFixture();
        DataWindowExpressionEvaluator evaluator = new(host);

        string? observedName = null;
        int observedArity = -1;
        long observedRow = -1L;

        evaluator.RegisterFunction(
            "FormatPrice",
            invocation =>
            {
                observedName = invocation.Name;
                observedArity = invocation.ArgumentCount;
                observedRow = invocation.Row;

                return invocation.Success(ExpressionValue.FromLong(0L));
            });

        _ = evaluator.Evaluate("FormatPrice(n2," + Precision + ")", 2L);

        // The name is carried as WRITTEN, which matters because the oracle's macro switch is a
        // `choose case name` over exactly these strings [docs/n_cst_dwsvc_columnexp.md:L124-L127].
        Assert.Equal("FormatPrice", observedName);
        Assert.Equal(2, observedArity);

        // And the invocation carries the EVALUATION row, so a macro reading a column reads the right
        // one - this is the row the inverted macro stream has to transmit (AAP §0.4.3 C-04).
        Assert.Equal(2L, observedRow);
    }
}


// ======================================================================================================
//  THE CONTEXT-MENU EVALUATE SITES
//  -----------------------------------------------------------------------------------------------------
//  Services/ContextMenuModel.cs reaches this evaluator at TEN call sites. They collapse onto NINE
//  distinct expression shapes, because the first three appear twice - once in the whole-column pass and
//  once in the single-row pass - and every shape is asserted below with the oracle locator of both its
//  copies:
//
//    #  ContextMenuModel.cs      oracle                     shape
//    1  :L4959, :L5074           :L534, :L617               Evaluate('<column>',<row>)      raw value
//    2  :L4974, :L5089           :L541, :L624               Evaluate('<compute>',<row>)     raw value
//    3  :L4979, :L5094           :L543, :L626               Evaluate('LookUpDisplay(c)',r)  display
//    4  :L6360                   :L1130, :L1296             Evaluate(<compute>)             row zero
//    5  :L6466                   :L1132, :L1298             Max(Len(LookUpDisplay(o)))
//    6  :L6470                   :L1133, :L1299             Max(LenA(LookUpDisplay(o)))
//    7  :L6582                   :L1194, :L1360             Evaluate(<column>,<row>)        bare
//    8  :L6595                   :L1197, :L1363             Evaluate(<format>,<row>)        mask
//    9  :L6669                   :L1183, :L1349             the nested-Describe find expression
//
//  Shapes 1 to 3 arrive through EvaluateAtRow, which builds `Evaluate('<expression>',<row>)` and hands it
//  to Describe [ContextMenuModel.cs:L5737-L5741], so they are exercised HERE as properties rather than as
//  expressions - the property text is itself observable, since a recording of the ported call sequence
//  has to match the oracle's. Shape 9 is already covered by NestedDescribeTests above and is not
//  duplicated; the two are cross-referenced rather than repeated.
//
//  WHAT STOPS AT THIS BOUNDARY. Shape 1's `Y`/`N` mapping [:L535-L539] and the `Fill("A",n) + Fill("国",m)`
//  proxy string that shapes 5 and 6 feed [:L1134] are ContextMenuModelTests.cs's, not this file's. This
//  suite asserts that the evaluator hands that caller the RAW inputs those steps need, and stops there.
// ======================================================================================================

public class ContextMenuEvaluateSiteTests
{
    /// <summary>
    /// Builds a fixture with the three object kinds the context menu measures: a checkbox column, a
    /// compute, and plain columns whose widest display values differ in width class.
    /// </summary>
    /// <returns>The host.</returns>
    /// <remarks>
    /// The two names are chosen so shapes 5 and 6 DIVERGE: "Alice" is five ASCII characters and
    /// "深圳市南山区" is six Han characters, so the character maximum is 6 and the byte maximum is 12.
    /// A fixture whose names were all ASCII would make both shapes answer the same number and the width
    /// split would be untested.
    /// </remarks>
    private static FakeDataWindowHost BuildContextMenuFixture()
    {
        FakeDataWindowHost host = new();

        host.AddTextObject("name_t", "header");

        host.AddColumn("name", FakeColumnType.CharOf(100)).Format = "[general]";
        host.AddColumn("salary", FakeColumnType.DecimalOf(2)).Format = "[general]";

        FakeDataWindowObjectDefinition flag = host.AddColumn("flag", FakeColumnType.CharOf(1));
        flag.Format = "[general]";
        flag.EditStyle = "checkbox";
        flag.CheckBoxOn = "Y";
        flag.CheckBoxOff = "N";

        // dw_sqlite.srd:L27's footer compute, carrying its UPPER-CASE mask alongside the columns'
        // LOWER-CASE ones. Neither spelling is normalised.
        host.AddComputedField("compute_1", "footer", "sum(salary for page)").Format = "[GENERAL]";

        // A per-row compute, which is the kind :L541 and :L1194 read.
        host.AddComputedField("compute_2", "detail", "name + '/' + flag").Format = "[general]";

        host.AddRow("Alice", 1000.50m, "Y");
        host.AddRow("深圳市南山区", 2000.25m, "N");

        host.CurrentRow = 1L;
        return host;
    }

    private static DataWindowExpressionEvaluator BuildEvaluator(out FakeDataWindowHost host)
    {
        host = BuildContextMenuFixture();

        // compute_1 is `sum(salary for page)`, so the pagination is stated rather than left to the
        // refusing default (DECISION D7a). The whole buffer is one page here.
        return new DataWindowExpressionEvaluator(host)
        {
            PageResolver = WholeBufferPageResolver.Instance,
        };
    }

    // Shapes 1 to 3 and their second copies, as the PROPERTY text EvaluateAtRow builds.
    public static TheoryData<string, string, string> DescribeShapedSites() => new()
    {
        // #1 the CHECKBOX arm reads the column's RAW stored value, because the caller compares it
        // against the on and off values before mapping it [:L535-L539].
        { "n_cst_dwsvc_contextmenu.sru:L534", "Evaluate('flag',1)", "Y" },
        { "n_cst_dwsvc_contextmenu.sru:L617", "Evaluate('flag',2)", "N" },

        // #2 the COMPUTE arm evaluates the compute by name.
        { "n_cst_dwsvc_contextmenu.sru:L541", "Evaluate('compute_2',1)", "Alice/Y" },
        { "n_cst_dwsvc_contextmenu.sru:L624", "Evaluate('compute_2',2)", "深圳市南山区/N" },

        // #3 every other column goes through LookUpDisplay. Note the oracle spells the row conversion
        // String() at :L534 and :L541 and string() at :L543; PowerScript is case-insensitive so the two
        // are one function, and the port formats the row invariantly either way.
        { "n_cst_dwsvc_contextmenu.sru:L543", "Evaluate('LookUpDisplay(name)',1)", "Alice" },
        { "n_cst_dwsvc_contextmenu.sru:L626", "Evaluate('LookUpDisplay(name)',2)", "深圳市南山区" },
    };

    [Theory]
    [MemberData(nameof(DescribeShapedSites))]
    public void EveryDescribeShapedSiteResolves(string locator, string property, string expected)
    {
        DataWindowExpressionEvaluator evaluator = BuildEvaluator(out _);

        Assert.Equal(expected, evaluator.Describe(property));

        // The property text is an Evaluate property, which is what routes it to the evaluator at all.
        Assert.True(DataWindowExpressionEvaluator.TryUnwrapEvaluateProperty(property, out _, out _));
        Assert.False(string.IsNullOrEmpty(locator));
    }

    // Shapes 4 to 8, as the EXPRESSION text the model hands to Evaluate directly.
    public static TheoryData<string, string, long, string> ExpressionShapedSites() => new()
    {
        // #4 a compute measured at ROW ZERO, because a footer compute has no row of its own.
        // 1000.50 + 2000.25 over the one page the whole buffer forms.
        {
            "n_cst_dwsvc_contextmenu.sru:L1130",
            "compute_1",
            DataWindowExpressionEvaluator.NoRowContext,
            "3000.75"
        },

        // #5 the CHARACTER maximum: Len("Alice") = 5, Len("深圳市南山区") = 6.
        {
            "n_cst_dwsvc_contextmenu.sru:L1132",
            "Max(Len(LookUpDisplay(name)))",
            DataWindowExpressionEvaluator.NoRowContext,
            "6"
        },

        // #6 the DBCS BYTE maximum: LenA("Alice") = 5, LenA("深圳市南山区") = 12.
        {
            "n_cst_dwsvc_contextmenu.sru:L1133",
            "Max(LenA(LookUpDisplay(name)))",
            DataWindowExpressionEvaluator.NoRowContext,
            "12"
        },

        // #6 the composite the oracle actually writes, wrapper for wrapper:
        //     nChsCnt = Abs(Long(_of_Evaluate("Max(LenA(...))")) - nAsc2Cnt)
        {
            "n_cst_dwsvc_contextmenu.sru:L1133 (composite)",
            "Abs(Long(Max(LenA(LookUpDisplay(name)))) - Long(Max(Len(LookUpDisplay(name)))))",
            DataWindowExpressionEvaluator.NoRowContext,
            "6"
        },

        // #7 a compute evaluated AT A ROW, the bare two-argument arity.
        { "n_cst_dwsvc_contextmenu.sru:L1194", "compute_2", 2L, "深圳市南山区/N" },
        { "n_cst_dwsvc_contextmenu.sru:L1360", "compute_2", 1L, "Alice/Y" },

        // #8 a FORMAT that is itself a per-row expression. The oracle detects it with
        // `Pos(sFormat,"~t") > 0` at :L1169 and strips the tab form with _of_GetPropExp at :L1171 - both
        // asserted in ExpressionEncodedPropertyTests - and then evaluates the remainder PER ROW here.
        {
            "n_cst_dwsvc_contextmenu.sru:L1197",
            "if(salary > 1500,'#,##0.00','0.00')",
            1L,
            "0.00"
        },
        {
            "n_cst_dwsvc_contextmenu.sru:L1363",
            "if(salary > 1500,'#,##0.00','0.00')",
            2L,
            "#,##0.00"
        },
    };

    [Theory]
    [MemberData(nameof(ExpressionShapedSites))]
    public void EveryExpressionShapedSiteResolves(
        string locator,
        string expression,
        long row,
        string expected)
    {
        DataWindowExpressionEvaluator evaluator = BuildEvaluator(out _);

        Assert.Equal(expected, evaluator.Evaluate(expression, row));
        Assert.False(string.IsNullOrEmpty(locator));
    }

    [Fact]
    public void TheWidthPairFeedsTheOraclesOwnArithmeticWithoutSynthesisingTheProxyString()
    {
        DataWindowExpressionEvaluator evaluator = BuildEvaluator(out _);

        // :L1132-L1133 read the two maxima, :L1134 subtracts. Both reads go through Long()/Abs() in the
        // oracle, so both are taken here in the same wrappers.
        long characters = long.Parse(
            evaluator.Evaluate("Long(Max(Len(LookUpDisplay(name))))"),
            CultureInfo.InvariantCulture);
        long bytes = long.Parse(
            evaluator.Evaluate("Long(Max(LenA(LookUpDisplay(name))))"),
            CultureInfo.InvariantCulture);

        Assert.Equal(6L, characters);
        Assert.Equal(12L, bytes);

        // :L1133  nChsCnt = Abs(... - nAsc2Cnt)      then      :L1134  nAsc2Cnt -= nChsCnt
        long wide = Math.Abs(bytes - characters);
        long ascii = characters - wide;

        Assert.Equal(6L, wide);
        Assert.Equal(0L, ascii);

        // AND THAT IS WHERE THIS FILE STOPS. :L1134's `Fill("A",nAsc2Cnt) + Fill("国",nChsCnt)` builds a
        // proxy string whose rendered width stands in for the real one, and asserting that synthesis
        // belongs to ContextMenuModelTests.cs. The two counts above are the whole of this evaluator's
        // contribution to it.
    }

    [Fact]
    public void ATextObjectIsMeasuredThroughItsPropertyRatherThanThroughAnAggregate()
    {
        DataWindowExpressionEvaluator evaluator = BuildEvaluator(out FakeDataWindowHost host);

        // :L1135-L1136 - the `case "text"` arm reads the object's TEXT PROPERTY instead of evaluating an
        // aggregate over rows, because a heading has exactly one value and no rows to aggregate.
        host.SetDescribe("name_t.text", "Name");

        Assert.Equal("Name", evaluator.Describe("name_t.text"));

        // AND THE REASON THE ORACLE SPLITS THE ARMS IS WORTH PINNING, because the alternative does not
        // fail loudly. A text object is not a data column, so reading it at a row answers NULL rather
        // than refusing; Len of a null is null, and Max over nothing but nulls is null. So the width
        // expression the `case "column"` arm writes would answer the EMPTY STRING for a heading -
        // indistinguishable from a zero-width label - instead of reporting a problem.
        DataWindowExpressionResult aggregated =
            evaluator.TryEvaluate("Max(Len(LookUpDisplay(name_t)))", 1L);

        Assert.Equal(ExpressionEvaluationOutcome.Value, aggregated.Outcome);
        Assert.True(aggregated.IsNullValue);
        Assert.Equal(string.Empty, aggregated.Text);

        // The same reading one step at a time, so the null is located rather than inferred.
        Assert.True(evaluator.TryEvaluate("name_t", 1L).IsNullValue);
        Assert.True(evaluator.TryEvaluate("LookUpDisplay(name_t)", 1L).IsNullValue);

        // A genuinely UNKNOWN name is a different answer entirely - that one IS refused - so the null
        // above is the object resolving and carrying no data, not the name failing to resolve.
        Assert.Equal(
            DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            evaluator.Evaluate("Max(Len(LookUpDisplay(no_such_object)))"));
    }

    [Fact]
    public void TheFindExpressionSiteIsCoveredByTheNestedDescribeSuite()
    {
        DataWindowExpressionEvaluator evaluator = BuildEvaluator(out _);

        // Shape 9 - ContextMenuModel.cs:L6669, the port of :L1183 and :L1349. NestedDescribeTests above
        // asserts the full construction against the primary fixture; this row exists so the ten-site
        // inventory in this suite's header is complete rather than nine tenths complete, and it asserts
        // the one property that matters at this boundary: the INNER Describe("Evaluate('...',<row>)") is
        // recognised as an evaluation request from inside an outer expression.
        const string inner = "Describe(\"Evaluate('compute_2',2)\")";

        Assert.Equal("深圳市南山区/N", evaluator.Evaluate(inner, 1L));

        // And composed the way the oracle composes it, comparing a row against its successor.
        Assert.Equal(
            "true",
            evaluator.Evaluate("String(compute_2) <> " + inner, 1L));
    }
}



// ======================================================================================================
//  CULTURE INDEPENDENCE, PROVED RATHER THAN ASSERTED
//  -----------------------------------------------------------------------------------------------------
//  Every expectation in this file is written in the invariant form - "1000.50", "1990-01-02", "2.5" - and
//  every one of them would still pass on a machine whose culture happens to be invariant-like even if the
//  evaluator consulted the ambient culture somewhere. That makes the whole suite's numeric and temporal
//  expectations conditionally correct, which is not correct.
//
//  So this suite RE-RUNS the culture-sensitive expectations under cultures whose decimal separator is a
//  COMMA and whose date order is not ISO, and requires byte-identical output. A single missing
//  CultureInfo.InvariantCulture anywhere in the render path - ToDisplayText's eight arms, the tokenizer's
//  numeric literal parse, the aggregate accumulator, ValueToExpression's formatter, or the row conversion
//  inside an Evaluate property - turns "1000.50" into "1000,50" or "1990-01-02" into "02.01.1990" and
//  fails here.
//
//  WHY THIS MATTERS BEYOND TIDINESS. The rendered text is not a display detail: it is the value the
//  expansion engine splices back into another expression [n_cst_dwsvc_columnexp.sru:L2390 evaluates
//  `dwValueToExp(...)` and :L2213 / :L2310 splice the result], it is what a characterization recording
//  stores, and it is what crosses the C-04 contract. A comma-decimal render would produce an expression
//  in which the decimal separator is an ARGUMENT SEPARATOR, so the corruption would surface as a parse
//  failure somewhere else entirely, or worse, as a different number.
//
//  ICU stays live for this refactor - Directory.Build.props deliberately does NOT set
//  InvariantGlobalization, because the port carries an en / zh-Hans / zh-Hant localization surface - so
//  these cultures are genuinely available and the premise below is guarded rather than assumed.
// ======================================================================================================

public class InvariantCultureRenderingTests
{
    // Three cultures, each a comma-decimal culture with a different group separator and date order:
    // de-DE groups with ".", fr-FR with a narrow no-break space, pt-BR with "." and a d/M/y order.
    public static TheoryData<string> CommaDecimalCultures() => new()
    {
        "de-DE",
        "fr-FR",
        "pt-BR",
    };

    /// <summary>
    /// The rows whose rendered text a culture could corrupt, with the invariant expectation each one
    /// already carries elsewhere in this file.
    /// </summary>
    /// <remarks>
    /// Deliberately a superset of the numeric and temporal roster entries rather than a fresh set: the
    /// point is that THOSE expectations hold under a hostile culture, so restating them is the assertion.
    /// </remarks>
    private static (string Expression, long Row, string Expected)[] CultureSensitiveRows =>
    [
        // The decimal(2) column of dw_sqlite.srd:L12, read directly and through LookUpDisplay. A
        // comma-decimal culture would render "1000,50".
        ("salary", 1L, "1000.50"),
        ("LookUpDisplay(salary)", 1L, "1000.50"),

        // A bare numeric literal, which is the INBOUND direction - the tokenizer parsing "1000.50" out
        // of expression text. A culture-sensitive parse would reject it or read 100050.
        ("1000.50", 0L, "1000.50"),

        // The footer aggregate of dw_sqlite.srd:L27, whose accumulator sums three decimals.
        ("sum(salary for all)", 0L, "6000.75"),

        // Division and rounding, which MANUFACTURE a fraction rather than echoing a stored literal.
        ("5 / 2", 0L, "2.5"),
        ("Round(1.005,2)", 0L, "1.01"),

        // The three numeric conversion functions.
        ("Double(age)", 1L, "30"),
        ("Long(salary)", 1L, "1000"),
        ("Abs(0 - salary)", 1L, "1000.50"),

        // Text projection of a number, which is String() and therefore the likeliest place for an
        // ambient-culture call to hide.
        ("String(salary)", 1L, "1000.50"),

        // The three temporal renders. A comma-decimal culture is also a d.M.yyyy or d/M/yyyy culture,
        // so an ambient format would reorder every one of these.
        ("birth", 1L, "1990-01-02"),
        ("Date('1990-01-02')", 0L, "1990-01-02"),
        ("DateTime('1990-01-02 03:04:05')", 0L, "1990-01-02 03:04:05"),
        ("Time('03:04:05')", 0L, "03:04:05"),

        // ValueToExpression's output, which is spliced back INTO an expression - the case where a comma
        // would become an argument separator.
        ("dwValueToExp(salary)", 1L, "1000.50"),
        ("dwValueToExp(birth)", 1L, "Date('1990-01-02')"),
        ("dwValueToExp(name)", 1L, "'Alice'"),

        // A comparison over decimals, so the culture cannot leak in through the comparison path either.
        ("if(salary > 1000.49,'over','under')", 1L, "over"),

        // And the boolean spelling, which is PowerScript's and not the culture's.
        ("salary > 1000.49", 1L, "true"),
    ];

    [Theory]
    [MemberData(nameof(CommaDecimalCultures))]
    public void EveryRenderedValueIsByteIdenticalUnderACommaDecimalCulture(string cultureName)
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo comma = CultureInfo.GetCultureInfo(cultureName);

            // GUARD THE PREMISE RATHER THAN ASSUMING IT. If ICU were ever trimmed out, every culture
            // would resolve to an invariant-like fallback and every assertion below would pass for
            // entirely the wrong reason - a green test proving nothing.
            Assert.Equal(",", comma.NumberFormat.NumberDecimalSeparator);
            Assert.NotEqual(
                CultureInfo.InvariantCulture.DateTimeFormat.ShortDatePattern,
                comma.DateTimeFormat.ShortDatePattern);

            // The culture is set on THIS THREAD only, so a suite running in parallel is unaffected, and
            // the finally below returns the thread to where it started.
            CultureInfo.CurrentCulture = comma;
            CultureInfo.CurrentUICulture = comma;

            DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);
            evaluator.PageResolver = WholeBufferPageResolver.Instance;

            foreach ((string expression, long row, string expected) in CultureSensitiveRows)
            {
                Assert.Equal(expected, evaluator.Evaluate(expression, row));
            }

            // The same for the property-shaped entry point, whose ROW argument is itself formatted -
            // n_cst_dwsvc.sru:L195 writes String(row) and a culture-sensitive conversion there would
            // produce a row the unwrapper cannot read back.
            Assert.Equal("1000.50", evaluator.Describe("Evaluate('salary',1)"));
            Assert.Equal("6000.75", evaluator.Describe("Evaluate('sum(salary for all)',0)"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void TheInvariantRunAndTheCommaDecimalRunProduceTheSameText()
    {
        // The strongest form of the assertion, and the one that needs no hand-written expectation at
        // all: render every row twice, once under each culture, and require the two lists to be equal.
        // This catches a culture leak even in a row whose expected value this file got wrong.
        string[] invariantRun = Render(CultureInfo.InvariantCulture);
        string[] commaRun = Render(CultureInfo.GetCultureInfo("de-DE"));

        Assert.Equal(invariantRun, commaRun);

        // And a third culture, so the agreement is not an accident of one pairing.
        Assert.Equal(invariantRun, Render(CultureInfo.GetCultureInfo("fr-FR")));
    }

    /// <summary>
    /// Renders every culture-sensitive row under one culture.
    /// </summary>
    /// <param name="culture">The culture to render under.</param>
    /// <returns>The rendered text, one entry per row, in declaration order.</returns>
    private static string[] Render(CultureInfo culture)
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);
            evaluator.PageResolver = WholeBufferPageResolver.Instance;

            (string Expression, long Row, string Expected)[] rows = CultureSensitiveRows;
            string[] rendered = new string[rows.Length];

            for (int index = 0; index < rows.Length; index++)
            {
                rendered[index] = evaluator.Evaluate(rows[index].Expression, rows[index].Row);
            }

            return rendered;
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}



// ======================================================================================================
//  THE SIX PRODUCER NAMES ARE SINGLE-SOURCED, AND THAT IS ASSERTED RATHER THAN TRUSTED
//  -----------------------------------------------------------------------------------------------------
//  RegisterBuiltInFunctions registers the value converter and the five typed-null producers under their
//  OWNING VALIDATOR'S constant, not under a retyped literal:
//
//      RegisterFunction(ValueToExpression.FunctionName, ...)      not  RegisterFunction("dwValueToExp", ...)
//      RegisterFunction(NumberValidator.FunctionName,   ...)      not  RegisterFunction("dwNvlNumber",  ...)
//      ... and so on for String, Date, DateTime and Time
//
//  That single-sourcing is what closes the loop between the two halves of the round trip: dwvaluetoexp.srf
//  EMITS the literal text "dwNvlNumber()" for a null [:L19, :L25, :L31, :L37, :L43], "dwNvlString()"
//  [:L49], "dwNvlDateTime()" [:L55], "dwNvlDate()" [:L61] and "dwNvlTime()" [:L67], the preprocessor
//  splices that text into an expression, and THIS evaluator then has to evaluate it. If the emitting side
//  and the registering side ever named the function differently, a null would round-trip into an
//  unregistered call and answer the malformed sentinel instead of a null.
//
//  WHY A SEPARATE ASSERTION IS NEEDED. Two suites above already cover the registry: one looks the names up
//  through the CONSTANTS and one through the LEGACY LITERALS. Between them a drifted constant is caught,
//  but only as "name not registered" - the failure would point at the registry rather than at the rename
//  that caused it. Binding the constant to the legacy spelling here makes the diagnosis immediate, and it
//  is the assertion that literally states the requirement: the registry's names ARE the validators'
//  constants, and those constants ARE the oracle's spellings.
// ======================================================================================================

public class ProducerNameBindingTests
{
    // Each row pairs a validator's own constant with the literal dwvaluetoexp.srf emits, and with the
    // locator of the emitting line. The literals are deliberately written out here - this is the one
    // place in the file where a retyped literal is the POINT rather than a hazard.
    public static TheoryData<string, string, string> ProducerNames() => new()
    {
        {
            "n_cst_dwsvc_columnexp.sru:L2390",
            ValueToExpression.FunctionName,
            "dwValueToExp"
        },
        { "dwvaluetoexp.srf:L19", NumberValidator.FunctionName, "dwNvlNumber" },
        { "dwvaluetoexp.srf:L49", StringValidator.FunctionName, "dwNvlString" },
        { "dwvaluetoexp.srf:L61", DateValidator.FunctionName, "dwNvlDate" },
        { "dwvaluetoexp.srf:L55", DateTimeValidator.FunctionName, "dwNvlDateTime" },
        { "dwvaluetoexp.srf:L67", TimeValidator.FunctionName, "dwNvlTime" },
    };

    [Theory]
    [MemberData(nameof(ProducerNames))]
    public void EveryProducerConstantCarriesItsOracleSpellingAndIsTheRegisteredName(
        string locator,
        string constant,
        string oracleSpelling)
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // The constant IS the oracle's spelling, byte for byte and case for case.
        Assert.Equal(oracleSpelling, constant);

        // And the registry's entry is that same value, so the emitting side and the evaluating side
        // cannot drift apart.
        Assert.True(evaluator.IsFunctionRegistered(constant), locator);
        Assert.Contains(constant, evaluator.FunctionNames);
    }

    [Fact]
    public void TheNullLiteralsTheConverterEmitsAreExactlyTheCallsTheRegistryAnswers()
    {
        DataWindowExpressionEvaluator evaluator = EvaluatorFixture.BuildEvaluator(out _);

        // THE ROUND TRIP, CLOSED. Each producer's published null literal is the complete CALL TEXT -
        // "dwNvlNumber()" and friends - so evaluating it must answer a null rather than a refusal. This
        // is the exact path a null column value takes: dwValueToExp emits the text, the preprocessor
        // splices it in, and this evaluator reads it back.
        foreach (string nullLiteral in new[]
        {
            NumberValidator.NullLiteralExpression,
            StringValidator.NullLiteralExpression,
            DateValidator.NullLiteralExpression,
            DateTimeValidator.NullLiteralExpression,
            TimeValidator.NullLiteralExpression,
        })
        {
            DataWindowExpressionResult result = evaluator.TryEvaluate(nullLiteral, 1L);

            Assert.True(result.IsSuccess, nullLiteral);
            Assert.True(result.IsNullValue, nullLiteral);
            Assert.NotEqual(
                DataWindowExpressionEvaluator.InvalidExpressionSentinel,
                result.Text);
        }

        // And the loop actually closes: converting a null salary yields a literal that evaluates back to
        // a null, rather than to the empty string or to a sentinel.
        string produced = evaluator.Evaluate("dwValueToExp(salary)", 4L);

        Assert.Equal(NumberValidator.NullLiteralExpression, produced);
        Assert.True(evaluator.TryEvaluate(produced, 4L).IsNullValue);
    }
}
