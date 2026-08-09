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
