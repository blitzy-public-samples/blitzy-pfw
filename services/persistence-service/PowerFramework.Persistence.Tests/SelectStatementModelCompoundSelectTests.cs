// ==============================================================================================
//  SelectStatementModelCompoundSelectTests - the compound-select characterization suite
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/persistence-service/PowerFramework.Persistence/Sql/
//                     SelectStatementModel.cs
//  BEHAVIOURAL ORACLE ws_objects/pfw.utility.parser.pbl.src/n_sql.sru        (the PBNI surface)
//                     ws_objects/pfw.utility.parser.pbl.src/parsesql.srf     (the factory)
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//                                                       (the only measured consumer, both dialects)
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru
//                                                       (DBT_MSSQL = 0, DBT_ORACLE = 1)
//                     all READ ONLY per constraint C-C - read as specification, never edited
//
//  WHAT THIS SUITE IS FOR, AND WHY IT IS SCOPED TO COMPOUND SELECTS
//  --------------------------------------------------------------------------------------------
//  SelectStatementModel is a managed reimplementation of a closed PBNI parser, so almost none of
//  its behaviour can be diffed against a running oracle from this repository. What CAN be pinned
//  without an oracle is the property the model states about itself and that its consumers depend on
//  absolutely: A STATEMENT THAT PARSES MUST RE-EMIT BYTE FOR BYTE, and a statement the two paging
//  rewriters can legitimately be handed MUST PARSE AT ALL.
//
//  This file covers the second of those for the set-operator surface, across BOTH target dialects.
//  The model is the single clause model behind both paging rewriters - the SQL Server strategies at
//  n_cst_thread_task_sqlquery.sru:L323-L381 and the Oracle triple-nested row-number strategy at
//  :L392-L395 - dispatched on the legacy's two-valued database type, DBT_MSSQL = 0 and
//  DBT_ORACLE = 1 [n_cst_thread_trans.sru:L60-L61, resolved at :L357-L359]. A dialect's set-
//  difference operator being unrecognised is therefore not a cosmetic gap: it makes every compound
//  query in that dialect unparseable.
//
//  THE FAILURE MODE THESE VECTORS EXIST TO CATCH IS SILENT, WHICH IS THE WHOLE POINT
//  --------------------------------------------------------------------------------------------
//  An unrecognised set operator does not raise anything. It simply never becomes a block boundary,
//  so both SELECT tokens fall into one segment; the strict increasing-clause-order check in Parse
//  then meets a second Column introducer after a Table introducer and rejects the entire statement.
//  Parse returns false - and ParseSql, reproducing the legacy factory's discarded parse result,
//  hands back a live but empty model with no error on any channel. A caller would see a statement
//  with zero blocks and no explanation. Nothing in the build catches that; these vectors do.
//
//  WHY THE OPERATOR IS ASSERTED THROUGH GetSql RATHER THAN BY INSPECTING A BOUNDARY
//  --------------------------------------------------------------------------------------------
//  The block splitter carries the operator's own source text in the FOLLOWING block's prefix and
//  re-emits it verbatim; it never reconstructs it. Asserting the round trip is therefore a stronger
//  statement than asserting the split position: it proves the operator survived with its exact
//  spelling, its exact casing and the whitespace on both sides of it, which is what byte-exact
//  parity of generated SQL requires. Every vector below asserts the round trip alongside the block
//  count for that reason.
//
//  C-F SELF-AUDIT: every statement in this file is synthetic. No credential, key, token, password,
//  connection string or certificate appears, and no statement text captured from any real log is
//  reproduced here. The table and column names are the fixture's own COMPANY shape or plain
//  single-letter stand-ins.
// ==============================================================================================

using PowerFramework.Persistence.Sql;

// The modification-style constants are CONSUMED from the shared kernel, never restated here: their
// SCREAMING_SNAKE spelling is preserved deliberately [AAP §0.4.5.3] and the root .editorconfig
// suppresses the naming analyzers only for the file that DECLARES them. The assembly is reachable
// transitively through the ProjectReference to PowerFramework.Persistence, which references it.
using PowerFramework.Shared.Kernel;

using Xunit;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Characterization vectors for <see cref="SelectStatementModel"/>'s compound-select handling,
/// covering the set operators of both target dialects. The general member, clause and
/// modification surface is covered by <see cref="SelectStatementModelTests"/>; this suite is
/// scoped to block splitting so the two can be read and extended independently.
/// </summary>
public sealed class SelectStatementModelCompoundSelectTests
{
    // ==========================================================================================
    //  THE SET-OPERATOR MATRIX - both dialects, one parser
    // ==========================================================================================

    /// <summary>
    /// Every recognised set operator must split the statement into two blocks and round-trip byte
    /// for byte. <c>MINUS</c> is Oracle's set difference and <c>EXCEPT</c> is the SQL Server and
    /// standard spelling of the same operation; both are present because one parser serves both
    /// dialects.
    /// </summary>
    /// <remarks>
    /// Stated as one theory over all five operators rather than as five separate tests, so that the
    /// dialects are visibly held to the SAME standard. A future reader adding a sixth operator has
    /// one row to add and one place to look.
    /// </remarks>
    [Theory]
    [InlineData("UNION")]
    [InlineData("UNION ALL")]
    [InlineData("INTERSECT")]
    [InlineData("EXCEPT")]
    [InlineData("MINUS")]
    public void SetOperator_SplitsIntoTwoBlocks_AndRoundTrips(string setOperator)
    {
        string sql = $"SELECT ID, NAME FROM COMPANY {setOperator} SELECT ID, NAME FROM COMPANY_ARCHIVE";
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql), $"A compound select using {setOperator} must parse.");
        Assert.Equal(2, model.GetSelectCount());
        Assert.Equal(sql, model.GetSql());
    }

    /// <summary>
    /// The operator's own casing and surrounding whitespace are part of the input and must survive
    /// the round trip untouched, because the block splitter carries the operator's source text into
    /// the following block's prefix rather than reconstructing it. Recognition itself is
    /// case-insensitive, so a lower-case or mixed-case operator must still split.
    /// </summary>
    [Theory]
    [InlineData("SELECT ID FROM A minus SELECT ID FROM B")]
    [InlineData("SELECT ID FROM A Minus SELECT ID FROM B")]
    [InlineData("SELECT ID FROM A\n  MINUS\n  SELECT ID FROM B")]
    [InlineData("SELECT ID FROM A except SELECT ID FROM B")]
    [InlineData("SELECT ID FROM A\tUNION\tALL\tSELECT ID FROM B")]
    public void SetOperator_CasingAndWhitespaceSurviveVerbatim(string sql)
    {
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql));
        Assert.Equal(2, model.GetSelectCount());
        Assert.Equal(sql, model.GetSql());
    }

    /// <summary>
    /// A three-block Oracle compound query mixing set-difference and intersection. Included because
    /// two blocks can be produced by a splitter that only ever splits once; three cannot.
    /// </summary>
    [Fact]
    public void OracleCompoundSelect_WithThreeBlocks_SplitsAndRoundTrips()
    {
        const string sql =
            "SELECT ID FROM COMPANY MINUS SELECT ID FROM RETIRED INTERSECT SELECT ID FROM ACTIVE";
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql));
        Assert.Equal(3, model.GetSelectCount());
        Assert.Equal(sql, model.GetSql());
    }

    // ==========================================================================================
    //  PER-BLOCK CLAUSE ADDRESSING ACROSS A MINUS
    // ==========================================================================================

    /// <summary>
    /// The clause accessors must address each block of a <c>MINUS</c> statement independently, which
    /// is the whole reason the one-based select index exists on every accessor
    /// [<c>n_sql.sru:L14-L49</c>]. Asserted on the Oracle operator specifically because that is the
    /// path this suite exists to pin.
    /// </summary>
    [Fact]
    public void OracleCompoundSelect_ClauseAccessorsAddressEachBlockIndependently()
    {
        const string sql =
            "SELECT ID FROM COMPANY WHERE AGE > 30 ORDER BY ID " +
            "MINUS " +
            "SELECT ID FROM RETIRED WHERE AGE > 65";
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql));
        Assert.Equal(2, model.GetSelectCount());

        Assert.Equal("ID", model.GetColumn(1));
        Assert.Equal("COMPANY", model.GetTable(1));
        Assert.Equal("AGE > 30", model.GetWhere(1));
        Assert.True(model.HasOrder(1));
        Assert.Equal("ID", model.GetOrder(1));

        Assert.Equal("ID", model.GetColumn(2));
        Assert.Equal("RETIRED", model.GetTable(2));
        Assert.Equal("AGE > 65", model.GetWhere(2));
        Assert.False(model.HasOrder(2));
        Assert.Equal(string.Empty, model.GetOrder(2));
    }

    /// <summary>
    /// The two-argument accessor overloads address block <c>1</c>, which is what the caller-side
    /// proxy does with its literal <c>1</c>
    /// [<c>n_cst_threading_task_sqlquery.sru:L380</c>, <c>:L383</c>]. Asserted across a
    /// <c>MINUS</c> so that the default cannot accidentally be "the last block" or "all blocks".
    /// </summary>
    [Fact]
    public void OracleCompoundSelect_ShortOverloadsAddressTheFirstBlock()
    {
        const string sql = "SELECT ID FROM COMPANY MINUS SELECT CODE FROM RETIRED";
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql));
        Assert.Equal(model.GetColumn(1), model.GetColumn());
        Assert.Equal(model.GetTable(1), model.GetTable());
        Assert.Equal("ID", model.GetColumn());
        Assert.Equal("COMPANY", model.GetTable());
    }

    /// <summary>
    /// Modifying one block of a <c>MINUS</c> statement must splice only that block and re-emit every
    /// other region of the statement verbatim - including the operator itself, which sits in the
    /// second block's prefix and is therefore adjacent to the splice.
    /// </summary>
    [Fact]
    public void OracleCompoundSelect_ModifyingOneBlockLeavesTheOperatorAndSiblingIntact()
    {
        const string sql = "SELECT ID FROM COMPANY MINUS SELECT ID FROM RETIRED";
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql));
        Assert.True(model.ModifyWhere(2, Enums.SQL_MS_REPLACE, "AGE > 65"));

        string emitted = model.GetSql();

        Assert.Contains("MINUS", emitted, StringComparison.Ordinal);
        Assert.Contains("WHERE AGE > 65", emitted, StringComparison.Ordinal);
        Assert.StartsWith("SELECT ID FROM COMPANY MINUS", emitted, StringComparison.Ordinal);
        Assert.Equal("AGE > 65", model.GetWhere(2));
        Assert.False(model.HasWhere(1));
    }

    // ==========================================================================================
    //  NON-REGRESSION - the word MINUS where it is NOT an operator
    //  ----------------------------------------------------------------------------------------
    //  Adding an operator to the recognised set widens what the scanner treats as a boundary, so
    //  these cases pin the protections that keep the widening safe. None of them is specific to
    //  MINUS - they are the same whole-word, depth and opaque-region rules EXCEPT and INTERSECT
    //  have always relied on - but they are asserted here because this is the change that made
    //  them matter for a new word.
    // ==========================================================================================

    /// <summary>
    /// The word must be matched as a WHOLE word. A longer identifier that merely begins with it, or
    /// ends with it, is not a boundary.
    /// </summary>
    [Theory]
    [InlineData("SELECT MINUSCULE FROM A")]
    [InlineData("SELECT SUBMINUS FROM A")]
    [InlineData("SELECT MINUS_TOTAL FROM A")]
    [InlineData("SELECT T.MINUS FROM A T")]
    public void MinusAsPartOfAnIdentifier_IsNotASetOperator(string sql)
    {
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql));
        Assert.Equal(1, model.GetSelectCount());
        Assert.Equal(sql, model.GetSql());
    }

    /// <summary>
    /// Inside a quoted region the scanner sees nothing at all, so the word cannot split there. All
    /// three quoting styles the scanner recognises are exercised: the single-quoted literal, the
    /// double-quoted identifier and SQL Server's bracketed identifier.
    /// </summary>
    [Theory]
    [InlineData("SELECT ID FROM A WHERE NAME = 'MINUS SELECT'")]
    [InlineData("SELECT \"MINUS\" FROM A")]
    [InlineData("SELECT [MINUS] FROM A")]
    [InlineData("SELECT ID FROM A WHERE NAME = 'a MINUS b' AND AGE > 1")]
    public void MinusInsideAQuotedRegion_IsNotASetOperator(string sql)
    {
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql));
        Assert.Equal(1, model.GetSelectCount());
        Assert.Equal(sql, model.GetSql());
    }

    /// <summary>
    /// Only a depth-zero operator is a boundary. A compound select nested inside parentheses - a
    /// derived table or an <c>IN</c> subquery - stays inside the enclosing block, exactly as the
    /// injected <c>INNER JOIN (&lt;a whole SELECT&gt;)</c> of the paging rewrites must.
    /// </summary>
    [Theory]
    [InlineData("SELECT ID FROM (SELECT ID FROM A MINUS SELECT ID FROM B) T")]
    [InlineData("SELECT ID FROM A WHERE ID IN (SELECT ID FROM B MINUS SELECT ID FROM C)")]
    public void MinusInsideParentheses_IsNotABlockBoundary(string sql)
    {
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql));
        Assert.Equal(1, model.GetSelectCount());
        Assert.Equal(sql, model.GetSql());
    }

    /// <summary>
    /// Inside a comment the scanner also sees nothing, for both comment forms.
    /// </summary>
    [Theory]
    [InlineData("SELECT ID FROM A -- MINUS SELECT ID FROM B")]
    [InlineData("SELECT ID /* MINUS SELECT */ FROM A")]
    public void MinusInsideAComment_IsNotASetOperator(string sql)
    {
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql));
        Assert.Equal(1, model.GetSelectCount());
        Assert.Equal(sql, model.GetSql());
    }

    /// <summary>
    /// Arithmetic subtraction uses the hyphen, never the word, so recognising the word costs
    /// arithmetic nothing. Stated explicitly because "MINUS is subtraction" is the intuition that
    /// would make a reader nervous about the rule above.
    /// </summary>
    [Fact]
    public void ArithmeticSubtraction_IsUnaffected()
    {
        const string sql = "SELECT SALARY - AGE FROM COMPANY WHERE SALARY - 100 > 0";
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql));
        Assert.Equal(1, model.GetSelectCount());
        Assert.Equal(sql, model.GetSql());
    }

    // ==========================================================================================
    //  THE ROUND-TRIP INVARIANT, ACROSS THE WHOLE OPERATOR SURFACE
    // ==========================================================================================

    /// <summary>
    /// Parse-then-emit with no intervening modification must return the input unchanged, for every
    /// combination of present and absent clauses on both sides of a set operator. This is the
    /// model's strongest available parity guarantee, since the native's own emission for a modified
    /// statement is not observable from this repository.
    /// </summary>
    [Theory]
    [InlineData("SELECT * FROM A MINUS SELECT * FROM B")]
    [InlineData("SELECT * FROM A WHERE X = 1 MINUS SELECT * FROM B WHERE Y = 2")]
    [InlineData("SELECT X, COUNT(*) FROM A GROUP BY X HAVING COUNT(*) > 1 MINUS SELECT X, 1 FROM B")]
    [InlineData("SELECT * FROM A ORDER BY X MINUS SELECT * FROM B")]
    [InlineData("  SELECT * FROM A   MINUS   SELECT * FROM B  ")]
    [InlineData("SELECT * FROM A EXCEPT SELECT * FROM B MINUS SELECT * FROM C")]
    public void ParseThenEmit_ReturnsTheInputUnchanged(string sql)
    {
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql));
        Assert.Equal(sql, model.GetSql());
    }

    /// <summary>
    /// The factory reproduces <c>parsesql.srf:L10-L14</c> including its discarded parse result, so a
    /// caller must interrogate the model. Asserted on a <c>MINUS</c> statement to confirm the
    /// factory path recognises the operator too, and not only the direct
    /// <see cref="SelectStatementModel.Parse"/> path.
    /// </summary>
    [Fact]
    public void ParseSqlFactory_HandlesAnOracleCompoundSelect()
    {
        const string sql = "SELECT ID FROM COMPANY MINUS SELECT ID FROM RETIRED";

        SelectStatementModel model = SelectStatementModel.ParseSql(sql);

        Assert.Equal(2, model.GetSelectCount());
        Assert.Equal(sql, model.GetSql());
    }
}
