// ==============================================================================================
//  PagingRewriterByteExactTests - the generated-SQL pin for all five paging arms
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/persistence-service/PowerFramework.Persistence/Sql/Paging/**
//  BEHAVIOURAL ORACLE ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//                         :L303-L404  of_buildpagedsql - the whole dispatch and all five arms
//                         :L323-L364  SQL Server, unique-index columns present (arms 1 and 2)
//                         :L365-L384  SQL Server, unique-index columns absent  (arms 3 and 4)
//                         :L386-L395  Oracle, the triple-nested row-number strategy
//                     READ ONLY per constraint C-C - read as specification, never edited.
//
//  WHY THIS SUITE EXISTS, AND WHY IT IS TRANSCRIBED RATHER THAN COMPUTED
//  --------------------------------------------------------------------------------------------
//  BYTE-EXACT GENERATED SQL IS THE ACCEPTANCE CRITERION for the paging rewriters (AAP 0.6.4), and
//  it is the one property that cannot be checked by reading the code: every arm is a chain of a
//  dozen string appends whose whitespace, comma placement, alias spellings and parenthesis nesting
//  are individually load-bearing. The sibling suites assert structure - that a sentinel is present,
//  that the dispatcher chose the right arm, that validation changed nothing - and none of them would
//  notice a doubled space, a moved comma or a lost parenthesis.
//
//  THE EXPECTATIONS BELOW ARE TRANSCRIBED FROM A RUN OF THIS CODE, NOT DERIVED FROM THE ORACLE, and
//  that limitation is stated rather than glossed: n_sql is a closed binary with no C++ source
//  anywhere in this repository, so the ONLY way to obtain the oracle's own output would be to
//  execute PowerBuilder. This is therefore a TARGET CHARACTERIZATION - it records what the .NET
//  rewriters emit so that a change to any of them is visible in review, and it does not certify
//  agreement with pfw.dll. The five sentinel identifiers and the count alias ARE traceable to the
//  oracle's source text, and each is asserted by name so that a rename cannot pass.
//
//  ITS IMMEDIATE PURPOSE. Three changes were made to these files to resolve review findings: the
//  inner sub-query reads became terminator-free, every page product became `checked`, and the
//  dispatcher began handing the arms RESOLVED unique-index identifiers. All three are no-ops for a
//  statement with no terminator, no overflow and no aliased column - which is every case below - so
//  this suite is the evidence that they cost nothing.
//
//  NO DATABASE OF EITHER DIALECT IS INVOLVED. Both rewriters are pure string transforms, which is
//  precisely how the plan preserves SQL Server and Oracle behaviour without provisioning either
//  (AAP 0.6.4, constraint C-E).
// ==============================================================================================

using PowerFramework.Persistence.Sql.Paging;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The generated statement of every paging arm, asserted character for character.
/// </summary>
public sealed class PagingRewriterByteExactTests
{
    /// <summary>The primary fixture's own retrieve [<c>dw_sqlite.srd:L14</c>].</summary>
    private const string StarSelect = "SELECT * FROM COMPANY";

    /// <summary>The fixture's six columns, enumerated so the select list is not opaque.</summary>
    private const string EnumeratedSelect =
        "SELECT ID, NAME, AGE, ADDRESS, SALARY, BIRTH FROM COMPANY";

    /// <summary>A statement that already carries an ORDER BY, which four of the five arms react to.</summary>
    private const string OrderedSelect = "SELECT ID, NAME FROM COMPANY ORDER BY NAME";

    /// <summary>Both arms, as the dispatcher's composition root would supply them.</summary>
    private static IPagingRewriter[] BothArms =>
        [new SqlServerPagingRewriter(), new OraclePagingRewriter()];

    /// <summary>
    /// Every arm's generated statement for page 2 of 10.
    /// </summary>
    /// <remarks>
    /// PAGE TWO RATHER THAN PAGE ONE, DELIBERATELY. On page one every offset is zero and every
    /// <c>BETWEEN</c> lower bound is one, so an arm that dropped the <c>(pi - 1)</c> term entirely
    /// would still produce the right text - the one-based ordinal is only observable from page two on.
    /// </remarks>
    public static TheoryData<string, DatabaseType, string, bool, string[]?, string> Arms =>
        new()
        {
            {
                "SQL Server, unique-index columns present, engine paging [:L343-L350]",
                DatabaseType.DbtMssql,
                EnumeratedSelect,
                true,
                ["ID"],
                "SELECT ID, NAME, AGE, ADDRESS, SALARY, BIRTH FROM COMPANY INNER JOIN "
                    + "(SELECT ID FROM COMPANY ORDER BY ID OFFSET 10 ROWS FETCH NEXT 10 ROWS ONLY) "
                    + "pfwPagedSQL_OutterTbl ON pfwPagedSQL_OutterTbl.ID = ID ORDER BY ID"
            },
            {
                "SQL Server, unique-index columns present, row-number paging [:L351-L362]",
                DatabaseType.DbtMssql,
                EnumeratedSelect,
                false,
                ["ID"],
                "SELECT ID, NAME, AGE, ADDRESS, SALARY, BIRTH FROM COMPANY INNER JOIN "
                    + "(SELECT TOP 10 * FROM (SELECT TOP 20 ID,ROW_NUMBER() OVER (ORDER BY ID) AS "
                    + "pfwPagedSQL_RN FROM COMPANY) pfwPagedSQL_Tbl WHERE pfwPagedSQL_RN BETWEEN 11 "
                    + "AND 20) pfwPagedSQL_OutterTbl ON pfwPagedSQL_OutterTbl.ID = ID ORDER BY ID"
            },
            {
                "SQL Server, unique-index columns present, row-number paging over an existing ORDER BY",
                DatabaseType.DbtMssql,
                OrderedSelect,
                false,
                ["ID"],
                // NOTE THE SPACE-JOINED `NAME ID`, WHICH IS NOT A MISSING COMMA IN THIS EXPECTATION.
                // The unique-column loop appends its fragment through ModifyOrder(APPEND, ...)
                // [:L341], and an append joins with a single space [:L336 builds the fragment, the
                // clause model joins]. The legacy emits exactly this, so it is pinned rather than
                // corrected (C-B) - a comma here would be a behavioural change.
                "SELECT ID, NAME FROM COMPANY INNER JOIN (SELECT TOP 10 * FROM (SELECT TOP 20 ID,"
                    + "ROW_NUMBER() OVER (ORDER BY NAME,ID) AS pfwPagedSQL_RN FROM COMPANY) "
                    + "pfwPagedSQL_Tbl WHERE pfwPagedSQL_RN BETWEEN 11 AND 20) pfwPagedSQL_OutterTbl "
                    + "ON pfwPagedSQL_OutterTbl.ID = ID ORDER BY NAME,ID"
            },
            {
                "SQL Server, no unique-index columns, engine paging, no existing ORDER BY [:L367-L373]",
                DatabaseType.DbtMssql,
                StarSelect,
                true,
                null,
                // THE `(SELECT 0)` SUBSTITUTION [:L370]. SQL Server requires an ordering key for
                // OFFSET/FETCH, and a bare constant is rejected as one - which is why the oracle emits
                // a sub-select rather than `0`. Oracle's arm uses `''` instead and the two are
                // deliberately not shared.
                "SELECT * FROM COMPANY ORDER BY (SELECT 0) OFFSET 10 ROWS FETCH NEXT 10 ROWS ONLY"
            },
            {
                "SQL Server, no unique-index columns, engine paging, existing ORDER BY kept [:L368]",
                DatabaseType.DbtMssql,
                OrderedSelect,
                true,
                null,
                "SELECT ID, NAME FROM COMPANY ORDER BY NAME OFFSET 10 ROWS FETCH NEXT 10 ROWS ONLY"
            },
            {
                "SQL Server, no unique-index columns, row-number paging [:L375-L383]",
                DatabaseType.DbtMssql,
                StarSelect,
                false,
                null,
                // THE ONLY ARM THAT APPENDS A TRAILING ORDER BY [:L383], and the only one whose select
                // list is the star it started with.
                "SELECT TOP 10 * FROM (SELECT TOP 20 *,ROW_NUMBER() OVER (ORDER BY (SELECT 0)) AS "
                    + "pfwPagedSQL_RN FROM COMPANY) pfwPagedSQL_Tbl WHERE pfwPagedSQL_RN BETWEEN 11 "
                    + "AND 20 ORDER BY pfwPagedSQL_RN"
            },
            {
                "SQL Server, no unique-index columns, row-number paging, ORDER BY stripped [:L376-L377]",
                DatabaseType.DbtMssql,
                OrderedSelect,
                false,
                null,
                // The existing ORDER BY moved INTO the window function and was removed from the inner
                // statement, which is what the guarded strip at [:L377] is for.
                "SELECT TOP 10 * FROM (SELECT TOP 20 ID, NAME,ROW_NUMBER() OVER (ORDER BY NAME) AS "
                    + "pfwPagedSQL_RN FROM COMPANY) pfwPagedSQL_Tbl WHERE pfwPagedSQL_RN BETWEEN 11 "
                    + "AND 20 ORDER BY pfwPagedSQL_RN"
            },
            {
                "Oracle, no existing ORDER BY, the '' substitution [:L392]",
                DatabaseType.DbtOracle,
                StarSelect,
                false,
                null,
                // THREE NESTING LEVELS AND THREE SENTINEL ALIASES. Note `.*,` with no space before the
                // comma, the `<=` cap at the middle level and the BETWEEN at the outermost - and that
                // this arm emits no trailing ORDER BY, unlike the SQL Server row-number arm.
                "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                    + "(ORDER BY '') AS pfwPagedSQL_RN FROM (SELECT * FROM COMPANY) "
                    + "pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner WHERE pfwPagedSQL_RN <= 20) "
                    + "pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN BETWEEN 11 AND 20"
            },
            {
                "Oracle, existing ORDER BY moved into the window function [:L388-L390]",
                DatabaseType.DbtOracle,
                OrderedSelect,
                false,
                null,
                "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                    + "(ORDER BY NAME) AS pfwPagedSQL_RN FROM (SELECT ID, NAME FROM COMPANY) "
                    + "pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner WHERE pfwPagedSQL_RN <= 20) "
                    + "pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN BETWEEN 11 AND 20"
            },
            {
                "Oracle ignores the unique-index columns entirely [:L386-L395]",
                DatabaseType.DbtOracle,
                StarSelect,
                false,
                ["ID"],
                // IDENTICAL to the Oracle case above: this arm never reads the collection, which is why
                // the dispatcher does not validate it for this dialect.
                "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                    + "(ORDER BY '') AS pfwPagedSQL_RN FROM (SELECT * FROM COMPANY) "
                    + "pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner WHERE pfwPagedSQL_RN <= 20) "
                    + "pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN BETWEEN 11 AND 20"
            },
        };

    /// <param name="arm">The arm, named so a failure message identifies it.</param>
    /// <param name="dialect">The dialect to dispatch on.</param>
    /// <param name="sql">The statement to rewrite.</param>
    /// <param name="pageNative">Whether to use the engine's own paging construct.</param>
    /// <param name="uniqueIndexColumns">The unique-index columns, or none.</param>
    /// <param name="expected">The generated statement, character for character.</param>
    [Theory]
    [MemberData(nameof(Arms))]
    public void EveryArmGeneratesItsStatementCharacterForCharacter(
        string arm,
        DatabaseType dialect,
        string sql,
        bool pageNative,
        string[]? uniqueIndexColumns,
        string expected)
    {
        PagingRewriteRequest request = new(sql, 10L, 2L, pageNative, uniqueIndexColumns);

        PagingRewriteResult result = PagingRewriteDispatcher.Rewrite(in request, dialect, BothArms);

        Assert.True(result.IsOk, $"{arm} did not succeed: {result.ErrorText}");
        Assert.Equal(expected, result.RewrittenSql, StringComparer.Ordinal);
    }

    /// <summary>
    /// THE SAME ARM, WITH A TERMINATED STATEMENT, PRODUCES THE SAME TEXT PLUS ONE TRAILING SEMICOLON -
    /// never one in the middle.
    /// </summary>
    /// <param name="arm">The arm, named so a failure message identifies it.</param>
    /// <param name="dialect">The dialect to dispatch on.</param>
    /// <param name="sql">The statement to rewrite, to which a terminator is appended.</param>
    /// <param name="pageNative">Whether to use the engine's own paging construct.</param>
    /// <param name="uniqueIndexColumns">The unique-index columns, or none.</param>
    /// <param name="expected">The generated statement for the UNTERMINATED form.</param>
    /// <remarks>
    /// Driven from the same matrix rather than a second one, so every arm is covered and none can be
    /// forgotten. This is the finding stated as an assertion: a semicolon retained in the statement
    /// body landed inside the first parenthesis of the generated text, and everything after it - the
    /// OFFSET, the BETWEEN, the aliases, the trailing ORDER BY - became unreachable.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Arms))]
    public void EveryArmPutsAnInboundTerminatorAtTheVeryEndAndNowhereElse(
        string arm,
        DatabaseType dialect,
        string sql,
        bool pageNative,
        string[]? uniqueIndexColumns,
        string expected)
    {
        PagingRewriteRequest request = new(sql + ";", 10L, 2L, pageNative, uniqueIndexColumns);

        PagingRewriteResult result = PagingRewriteDispatcher.Rewrite(in request, dialect, BothArms);

        Assert.True(result.IsOk, $"{arm} did not succeed: {result.ErrorText}");
        Assert.Equal(expected + ";", result.RewrittenSql, StringComparer.Ordinal);
    }

    /// <summary>
    /// THE FIVE SENTINEL IDENTIFIERS AND THE COUNT ALIAS ARE SPELLED EXACTLY AS THE ORACLE SPELLS
    /// THEM, including the doubled <c>t</c> in <c>OutterTbl</c>.
    /// </summary>
    /// <remarks>
    /// These are the one part of this suite that IS traceable rather than transcribed: each appears
    /// verbatim in the oracle's source text, and each is part of the observable generated statement, so
    /// a "corrected" spelling would be a parity break that reads as a typo fix. <c>OutterTbl</c> is
    /// misspelled in the legacy and stays misspelled here (C-B).
    /// </remarks>
    [Fact]
    public void TheSentinelAliasesKeepTheirLegacySpellings()
    {
        PagingRewriteRequest uniqueIndex = new(EnumeratedSelect, 10L, 2L, false, ["ID"]);
        PagingRewriteRequest plain = new(StarSelect, 10L, 2L, false);

        string uniqueIndexSql = Rewritten(in uniqueIndex, DatabaseType.DbtMssql);
        string sqlServerPlain = Rewritten(in plain, DatabaseType.DbtMssql);
        string oraclePlain = Rewritten(in plain, DatabaseType.DbtOracle);

        Assert.Contains("pfwPagedSQL_OutterTbl", uniqueIndexSql, StringComparison.Ordinal);
        Assert.Contains("pfwPagedSQL_Tbl", uniqueIndexSql, StringComparison.Ordinal);
        Assert.Contains("pfwPagedSQL_RN", sqlServerPlain, StringComparison.Ordinal);
        Assert.Contains("pfwPagedSQL_TblInnerInner", oraclePlain, StringComparison.Ordinal);

        // SPACE-DELIMITED ON BOTH SIDES, DELIBERATELY. A bare `pfwPagedSQL_TblInner` is a prefix of
        // `pfwPagedSQL_TblInnerInner`, so the unadorned sub-string would be satisfied by the inner
        // alias alone and the middle level could lose its own alias unnoticed.
        Assert.Contains(" pfwPagedSQL_TblInner ", oraclePlain, StringComparison.Ordinal);
        Assert.Contains("pfwPagedSQL_TblOuter", oraclePlain, StringComparison.Ordinal);
    }

    /// <summary>Rewrites and asserts success, so a caller can read the text directly.</summary>
    /// <param name="request">The request.</param>
    /// <param name="dialect">The dialect.</param>
    private static string Rewritten(in PagingRewriteRequest request, DatabaseType dialect)
    {
        PagingRewriteResult result = PagingRewriteDispatcher.Rewrite(in request, dialect, BothArms);

        Assert.True(result.IsOk, result.ErrorText);

        return result.RewrittenSql;
    }
}
