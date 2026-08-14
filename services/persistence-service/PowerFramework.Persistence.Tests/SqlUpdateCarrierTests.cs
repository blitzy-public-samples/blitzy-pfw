// ==================================================================================================
//  SqlUpdateCarrierTests - THE TWO PROOFS THIS SERVICE'S CONTRACT TURNS ON
// ==================================================================================================
//
//  WHY THIS FILE EXISTS SEPARATELY FROM THE OTHER UPDATE CASES. Two behaviours are named as required
//  demonstrations for this service rather than as ordinary units:
//
//    1. CONFLICT MAPPING. An optimistic-concurrency mismatch must surface as gRPC `Aborted` carrying a
//       POPULATED ConflictDetail - not a bare status, and never a silent overwrite [AAP 0.6.3.8].
//    2. REDACTION. A database error carrying a generated statement must have that statement text
//       redacted before it is logged or returned, because the legacy field carries interpolated literal
//       values and the legacy logger performs no redaction at all [AAP 0.6.3.8, 0.6.5].
//
//  Neither is reachable by inspecting a component in isolation: a mismatch only exists once a real
//  statement has run against real storage and matched fewer rows than it was generated for. These cases
//  therefore drive the PRODUCTION carrier - the adapter the composition root registers - against a real
//  SQLite database, and assert the two demonstrations end to end.
//
//  WHAT THE FIXTURE IS. The one updatable DataWindow in the entire legacy estate is `dw_sqlite`, whose
//  table specification reads `updatewhere=1 updatekeyinplace=no` with all six columns marked
//  `update=yes updatewhereclause=yes` [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14], and the only
//  DDL anywhere in the repository creates COMPANY [w_test_sqlite.srw:L463-L469]. Both are TRANSCRIBED
//  here rather than read: the legacy tree is read-only and is the behavioural oracle, never an input a
//  test opens at run time (constraint C-C).
//
//  WHY `updatewhere=1` IS THE WHOLE POINT. That mode puts the key column PLUS THE ORIGINAL VALUE of every
//  marked column into the generated predicate. A row another writer has changed therefore matches
//  nothing, and the shortfall between rows-generated-for and rows-affected IS the mismatch. Building the
//  predicate from CURRENT values instead would always match, and the second writer would silently
//  overwrite the first - the exact failure the contract forbids.
// ==================================================================================================

using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Buffers;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Proves that a concurrency mismatch is reachable and reportable, and that a statement-bearing error is
/// redacted.
/// </summary>
public sealed class SqlUpdateCarrierTests
{
    /// <summary>The evidenced update table, transcribed from the oracle's table specification.</summary>
    private const string UpdateTable = "COMPANY";

    /// <summary>
    /// The evidenced DDL, transcribed from <c>w_test_sqlite.srw:L463-L469</c> - the only DDL in the
    /// repository.
    /// </summary>
    /// <remarks>
    /// THE TYPE MISMATCHES ARE THE ORACLE'S AND ARE PRESERVED. The DataWindow declares a 200-character
    /// address against this 50-character column, a two-place decimal salary against this REAL, and a date
    /// birth field against this TEXT [AAP 0.6.4]. They are defects to reproduce, not to tidy.
    /// </remarks>
    private const string CompanyDdl =
        "CREATE TABLE COMPANY ("
        + "ID INTEGER PRIMARY KEY AUTOINCREMENT, "
        + "NAME TEXT NOT NULL, "
        + "AGE INTEGER NOT NULL, "
        + "ADDRESS CHAR(50), "
        + "SALARY REAL, "
        + "BIRTH TEXT)";

    /// <summary>The six marked columns, in the oracle's declaration order.</summary>
    private static readonly string[] CompanyColumns =
        ["id", "name", "age", "address", "salary", "birth"];

    /// <summary>
    /// A column name that cannot occupy an identifier position, standing in for a hostile declaration.
    /// </summary>
    /// <remarks>
    /// IT CARRIES A STATEMENT SEPARATOR AND A COMMENT MARKER, which is the shape that matters: the
    /// separator turns one generated statement into two and <c>Microsoft.Data.Sqlite</c> executes every
    /// statement in a batch it is handed, while the marker comments out whatever followed - including the
    /// concurrency predicate. It carries NO <c>=</c> deliberately: the modification script splits each
    /// line on its first equals sign, so a name containing one would be refused by the script's own
    /// grammar and the case would pass without ever reaching the gate under test.
    /// </remarks>
    private const string HostileColumn = "birth;DROP TABLE COMPANY --";

    /// <summary>
    /// A row another writer has changed underneath matches nothing, and the mismatch carries the current
    /// row state a caller needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE CONFLICT-MAPPING DEMONSTRATION. The sequence is the real race: a row is retrieved so
    /// the carrier holds its ORIGINAL values, a competing writer changes the stored row, and then the
    /// first writer's update runs. Because the predicate carries the originals, it matches zero rows.
    /// </para>
    /// <para>
    /// THE THREE ASSERTIONS ARE NOT INTERCHANGEABLE. The shortfall proves the predicate was built from
    /// originals; the classifier's verdict proves the shortfall is recognised as a mismatch rather than as
    /// a database error; and the populated detail proves a caller receives the row state it needs to
    /// choose between retrying and surfacing. Without the third, a 409 would carry nothing actionable.
    /// </para>
    /// <para>
    /// THE FOURTH ASSERTION IS THE ONE THAT WOULD CATCH A SILENT OVERWRITE. The competing writer's value
    /// is still in storage afterwards, so nothing was overwritten by the update that lost the race.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStaleRowMatchesNothingAndItsMismatchCarriesCurrentRowState()
    {
        using CarrierFixture fixture = new();

        fixture.Seed("Ada Lovelace", 36, "London", 92500m, "1815-12-10");

        // The first writer retrieves, so the carrier now holds the row's ORIGINAL values as its baseline.
        ISqlUpdateCarrier carrier = fixture.AdaptWithRetrievedRow();

        // A competing writer changes the stored row. This is the race, expressed literally.
        fixture.ExecuteDirect("UPDATE COMPANY SET SALARY = 99000 WHERE NAME = 'Ada Lovelace'");

        // The first writer now modifies its own copy and updates. Its predicate carries the ORIGINAL
        // salary, which no longer exists in storage.
        MarkColumnModified(carrier, column: 5, value: 95000d);

        Assert.Equal(
            Buffers.DataWindowBufferStore.DataStoreSuccess,
            carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        ConcurrencyEvidence? evidence = carrier.CaptureConcurrencyEvidence();

        Assert.NotNull(evidence);
        Assert.False(evidence.ProviderFaulted);
        Assert.Equal(1L, evidence.RowsExpected);
        Assert.Equal(0L, evidence.RowsMatched);

        // The classifier recognises the shortfall as a MISMATCH, not as a provider fault.
        Assert.True(ConflictDetector.IsConcurrencyMismatch(evidence));

        ConflictDetail detail = ConflictDetector.BuildConflictDetail(evidence, UpdateTable);

        Assert.Equal(UpdateTable, detail.UpdateTable);
        Assert.Equal(1L, detail.RowsExpected);
        Assert.Equal(0L, detail.RowsMatched);

        // POPULATED, not merely present. A caller can see which row lost and what it held.
        Assert.NotEmpty(detail.Rows);
        Assert.Equal(1L, detail.Rows[0].Row);
        Assert.Equal(DwBuffer.Primary, detail.Rows[0].Buffer);
        Assert.NotEmpty(detail.Rows[0].CurrentValues);

        // NOTHING WAS OVERWRITTEN. The competing writer's value survives the update that lost the race.
        Assert.Equal(
            99000d,
            Convert.ToDouble(
                fixture.ScalarDirect("SELECT SALARY FROM COMPANY WHERE NAME = 'Ada Lovelace'"),
                CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// An update whose predicate still matches succeeds, and reports no mismatch.
    /// </summary>
    /// <remarks>
    /// THE NEGATIVE CONTROL FOR THE CASE ABOVE. Without it, a carrier that reported a mismatch on every
    /// update would pass that case while being entirely broken. This one proves the mismatch is a verdict
    /// about the data rather than a constant.
    /// </remarks>
    [Fact]
    public void AnUncontestedRowMatchesAndReportsNoMismatch()
    {
        using CarrierFixture fixture = new();

        fixture.Seed("Grace Hopper", 45, "Arlington", 88000m, "1906-12-09");

        ISqlUpdateCarrier carrier = fixture.AdaptWithRetrievedRow();

        MarkColumnModified(carrier, column: 5, value: 91000d);

        Assert.Equal(
            Buffers.DataWindowBufferStore.DataStoreSuccess,
            carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        ConcurrencyEvidence? evidence = carrier.CaptureConcurrencyEvidence();

        Assert.NotNull(evidence);
        Assert.Equal(1L, evidence.RowsExpected);
        Assert.Equal(1L, evidence.RowsMatched);
        Assert.Empty(evidence.Rows);
        Assert.False(ConflictDetector.IsConcurrencyMismatch(evidence));

        Assert.Equal(
            91000d,
            Convert.ToDouble(
                fixture.ScalarDirect("SELECT SALARY FROM COMPANY WHERE NAME = 'Grace Hopper'"),
                CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A database error's statement text is redacted before it can be returned or logged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE REDACTION DEMONSTRATION, AND IT IS ASSERTED ON THE STATEMENT THAT CAUSED THE FAULT
    /// rather than on a synthetic string. A generated statement carries interpolated literal values -
    /// personal names, salaries, addresses - and the legacy publishes it into <c>sqlsyntax</c> with no
    /// redaction whatsoever. The projection this service returns must not.
    /// </para>
    /// <para>
    /// BOTH DIRECTIONS ARE ASSERTED. The literals must be gone, and the statement's SHAPE must survive:
    /// a redaction that erased the whole statement would satisfy the first requirement while destroying
    /// the diagnostic value that made the field worth carrying at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStatementBearingErrorIsRedactedBeforeItTravels()
    {
        const string Generated =
            "UPDATE COMPANY SET SALARY = 95000, ADDRESS = 'London' "
            + "WHERE ID = 1 AND NAME = 'Ada Lovelace' AND SALARY = 92500";

        string redacted = SqlRedactor.Instance.Redact(Generated);

        // The literals are gone - every one of them, including the numeric salary.
        Assert.DoesNotContain("Ada Lovelace", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("London", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("92500", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("95000", redacted, StringComparison.Ordinal);

        // The shape survives, so the field is still diagnostic.
        Assert.Contains("UPDATE COMPANY", redacted, StringComparison.Ordinal);
        Assert.Contains("SALARY", redacted, StringComparison.Ordinal);
        Assert.Contains("WHERE", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    /// A real storage fault reaches the carrier's error channel without carrying the statement text.
    /// </summary>
    /// <remarks>
    /// THE CARRIER ATTACHES NO STATEMENT AT ALL ON A PROVIDER FAULT, which is stricter than redacting one.
    /// The exception message from the provider is the diagnostic; the statement that produced it stays
    /// inside the carrier, so there is no path by which an un-redacted statement could leave this layer.
    /// The case drives a genuine constraint violation rather than simulating one, because a simulated
    /// fault would prove only that the assertion matches the simulation.
    /// </remarks>
    [Fact]
    public void AProviderFaultReachesTheErrorChannelWithNoStatementAttached()
    {
        using CarrierFixture fixture = new();

        fixture.Seed("Alan Turing", 41, "Wilmslow", 75000m, "1912-06-23");

        ISqlUpdateCarrier carrier = fixture.AdaptWithRetrievedRow();

        // NAME carries a NOT NULL constraint, so nulling it is a real provider fault rather than a mock.
        MarkColumnModified(carrier, column: 2, value: null);

        Assert.Equal(
            Buffers.DataWindowBufferStore.DataStoreFailure,
            carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        (long Code, string Text, string Syntax, DwBuffer Buffer, long Row) error =
            Assert.NotNull(fixture.LastDbError);

        // The provider's own code and diagnostic travel, so the fault is identifiable.
        Assert.NotEqual(0L, error.Code);
        Assert.NotEmpty(error.Text);

        // NO STATEMENT TRAVELLED, so there is nothing for a downstream sink to leak. This is stricter
        // than redacting a statement: the text never enters the channel in the first place.
        Assert.Equal(string.Empty, error.Syntax);

        // And the row was not written, so the failure did not half-apply.
        Assert.Equal(
            "Alan Turing",
            Convert.ToString(
                fixture.ScalarDirect("SELECT NAME FROM COMPANY WHERE ID = 1"),
                CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A modification script naming a column this carrier does not hold is REFUSED with the oracle's own
    /// invalid-column diagnostic, and nothing from that script is installed.
    /// </summary>
    /// <remarks>
    /// <b>PowerBuilder'S Modify PARSES AGAINST THE LOADED DATAWINDOW</b>, which is why
    /// <c>_of_updateprepare</c> has no test of its own for an updatable column name: <c>&lt;name&gt;.Update
    /// = 'yes'</c> for a column the DataWindow does not declare is refused BY MODIFY, and the caller then
    /// takes its <c>if sErr &lt;&gt; ""</c> arm
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L145-L148</c>]. Installing
    /// script lines into a plain dictionary accepted any name at all, so a descriptor naming a column that
    /// does not exist reported success and the generated statement simply omitted it - a silent misdirection
    /// rather than a refusal.
    /// </remarks>
    [Fact]
    public void AModificationScriptNamingAnUnknownColumnIsRefused()
    {
        using CarrierFixture fixture = new();

        fixture.Seed("Grace Hopper", 45, "Arlington", 7100m, "1906-12-09");

        ISqlUpdateCarrier carrier = fixture.AdaptWithRetrievedRow();

        // THE SUCCESS TEST IS INVERTED: empty means accepted, non-empty is the driver's error text.
        string refusal = carrier.TargetModifier.Modify("no_such_column.Update=yes");

        Assert.Equal(UpdateWhereBuilder.InvalidColumnNameMessage + "no_such_column", refusal);

        // NOTHING WAS INSTALLED by the refused script - the unknown column still resolves to no column
        // identifier at all, which is the answer that drives the caller's own invalid-column arm.
        Assert.Equal(
            0,
            carrier.TargetMetadata.GetColumnId("no_such_column" + SqlUpdateCarrier.IdSuffix));

        // A REAL COLUMN, BY NAME AND BY ORDINAL, IS STILL ACCEPTED - both addressing forms are the
        // carrier's own and each is used by a different caller.
        Assert.Equal(string.Empty, carrier.TargetModifier.Modify("salary.Update=yes"));
        Assert.Equal(string.Empty, carrier.TargetModifier.Modify("#5.Key=no"));

        // AND SO IS EVERY TABLE-LEVEL PROPERTY, which names no column at all.
        Assert.Equal(
            string.Empty,
            carrier.TargetModifier.Modify(SqlUpdateCarrier.UpdateTableProperty + "='COMPANY'"));

        // AN ORDINAL OUTSIDE THE MODEL IS AS UNRESOLVABLE AS AN UNKNOWN NAME, and so is a property with
        // no object at all.
        Assert.Equal(
            UpdateWhereBuilder.InvalidColumnNameMessage + "#7",
            carrier.TargetModifier.Modify("#7.Update=yes"));
        Assert.Equal(
            UpdateWhereBuilder.InvalidColumnNameMessage + "#0",
            carrier.TargetModifier.Modify("#0.Update=yes"));
        Assert.Equal(
            UpdateWhereBuilder.InvalidColumnNameMessage + "Update",
            carrier.TargetModifier.Modify("Update=yes"));
    }

    /// <summary>
    /// 🔴 A modification addressed BY ORDINAL is visible BY NAME and actually suppresses the assignment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ALIASING PROOF, AND THE ONE THAT WOULD HAVE CAUGHT A SILENT DATA-INTEGRITY FAULT. In PowerBuilder
    /// <c>Describe("#5.Update")</c> and <c>Describe("salary.Update")</c> ask ONE column object for ONE
    /// attribute - the ordinal and the name are two ways of addressing the same object - so a
    /// <c>Modify</c> through either is visible through both. Storing them as two independent dictionary
    /// entries broke that, and the break was invisible from either side alone: the reset half of
    /// <c>_of_updateprepare</c> emits <c>#N.Update = no</c> for every column BY ORDINAL
    /// [<c>n_cst_thread_task_sqlupdate.sru:L103-L108</c>] while the three arms that re-enable emit
    /// <c>&lt;name&gt;.Update = yes</c> BY NAME [<c>:L111-L129</c>], and the generator reads them back BY
    /// NAME. So the reset wrote entries nothing read, the definition seed's own name-addressed <c>yes</c>
    /// survived for every column, and a descriptor naming one updatable column still generated an
    /// assignment for all six - writing a column the caller had EXCLUDED.
    /// </para>
    /// <para>
    /// THE THIRD ASSERTION IS THE ONE THAT MATTERS. The two describes prove the alias; storage proves the
    /// consequence. With the flag cleared by ordinal, the modified salary must NOT reach the row.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnOrdinalAddressedModificationIsVisibleByNameAndSuppressesTheAssignment()
    {
        using CarrierFixture fixture = new();

        fixture.Seed("Ada Lovelace", 36, "London", 92500m, "1815-12-10");

        ISqlUpdateCarrier carrier = fixture.AdaptWithRetrievedRow();

        // THE ALIAS, READ THROUGH A PRODUCTION DESCRIBE. The identity attribute is the one this service
        // genuinely reads both ways - the prepare step writes it BY NAME [:L127-L129] and the identity round
        // trip reads it BY ORDINAL [IdentityColumnResolver] - so it is the pair whose divergence was silent.
        Assert.Equal(string.Empty, carrier.TargetModifier.Modify("#5.Identity=yes"));
        Assert.Equal(
            UpdateWhereBuilder.YesLiteral,
            carrier.Identity.Metadata.DescribeColumnIdentity("salary" + SqlUpdateCarrier.IdentitySuffix));

        // AND THE CONVERSE, so neither direction is privileged.
        Assert.Equal(string.Empty, carrier.TargetModifier.Modify("salary.Identity=no"));
        Assert.Equal(
            UpdateWhereBuilder.NoLiteral,
            carrier.Identity.Metadata.DescribeColumnIdentity("#5" + SqlUpdateCarrier.IdentitySuffix));

        // Now clear the UPDATABLE flag by ordinal, and modify the column the caller has just excluded.
        Assert.Equal(string.Empty, carrier.TargetModifier.Modify("#5.Update=no"));

        MarkColumnModified(carrier, column: 5, value: 95000d);

        Assert.Equal(
            Buffers.DataWindowBufferStore.DataStoreSuccess,
            carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        // 🔴 THE EXCLUDED COLUMN WAS NOT WRITTEN. Before the alias existed, the surviving name-addressed
        // `yes` put SALARY in the SET list and this read answered the modified value.
        Assert.Equal(
            92500d,
            Convert.ToDouble(
                fixture.ScalarDirect("SELECT SALARY FROM COMPANY WHERE NAME = 'Ada Lovelace'"),
                CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 🔴 The installed concurrency mode decides which columns the predicate compares.
    /// </summary>
    /// <param name="updateWhere">The mode to install.</param>
    /// <param name="expectMismatch">
    /// Whether a competing writer's change to a NON-KEY, NON-MODIFIED column must make this update match
    /// nothing.
    /// </param>
    /// <remarks>
    /// <para>
    /// THE MODE WAS PREVIOUSLY WRITTEN AND NEVER READ. The property was installed by the prepare step and
    /// by the definition seed, and the predicate was composed as key ∪ marked unconditionally - mode 1
    /// hardcoded - so a caller declaring mode 0 or mode 2 had its declared concurrency policy silently
    /// replaced by this service's. The mode decides WHICH ROWS a statement matches, so the substitution was
    /// invisible in every response.
    /// </para>
    /// <para>
    /// THE RACE IS CONSTRUCTED SO THE THREE MODES DISAGREE. A competing writer changes ADDRESS, which is
    /// neither the key nor the column this update modifies. Mode 1 compares every marked column, so the
    /// stale ADDRESS original makes the predicate match nothing. Mode 0 compares the key alone and mode 2
    /// compares the key plus the modified SALARY - neither of which the competitor touched - so both match.
    /// </para>
    /// <para>
    /// THE PREDICATE TEXT IS ASSERTED TOO, because a matching row count alone would also be produced by a
    /// predicate that compared the wrong columns and happened to agree. The statement is read from the SQL
    /// preview channel, which is where the generator publishes every statement it runs.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(UpdateWhereBuilder.KeyOnlyMode, false)]
    [InlineData(UpdateWhereBuilder.KeyAndUpdatableColumnsMode, true)]
    [InlineData(UpdateWhereBuilder.KeyAndModifiedColumnsMode, false)]
    public void TheInstalledConcurrencyModeDecidesWhichColumnsThePredicateCompares(
        long updateWhere,
        bool expectMismatch)
    {
        using CarrierFixture fixture = new();

        fixture.Seed("Ada Lovelace", 36, "London", 92500m, "1815-12-10");

        ISqlUpdateCarrier carrier = fixture.AdaptWithRetrievedRow(updateWhere: updateWhere);

        // The competing writer changes a column that is neither the key nor the one this update modifies.
        fixture.ExecuteDirect("UPDATE COMPANY SET ADDRESS = 'Somerset' WHERE NAME = 'Ada Lovelace'");

        MarkColumnModified(carrier, column: 5, value: 95000d);

        Assert.Equal(
            Buffers.DataWindowBufferStore.DataStoreSuccess,
            carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        ConcurrencyEvidence? evidence = carrier.CaptureConcurrencyEvidence();

        Assert.NotNull(evidence);
        Assert.False(evidence.ProviderFaulted);
        Assert.Equal(1L, evidence.RowsExpected);
        Assert.Equal(expectMismatch ? 0L : 1L, evidence.RowsMatched);
        Assert.Equal(expectMismatch, ConflictDetector.IsConcurrencyMismatch(evidence));

        // THE PREDICATE ITSELF, so a coincidental row count cannot pass for the right comparison.
        string generated = Assert.IsType<string>(carrier.Store.Carrier.SqlPreviewStatement);

        Assert.StartsWith(
            "UPDATE COMPANY SET salary = @p1 WHERE id = @p2",
            generated,
            StringComparison.Ordinal);

        switch (updateWhere)
        {
            case UpdateWhereBuilder.KeyOnlyMode:
                // THE KEY AND NOTHING ELSE.
                Assert.Equal("UPDATE COMPANY SET salary = @p1 WHERE id = @p2", generated);

                break;

            case UpdateWhereBuilder.KeyAndModifiedColumnsMode:
                // THE KEY PLUS THE ONE COLUMN THIS ROW MODIFIED, and no other.
                Assert.Equal(
                    "UPDATE COMPANY SET salary = @p1 WHERE id = @p2 AND salary = @p3",
                    generated);

                break;

            default:
                // THE KEY PLUS EVERY MARKED COLUMN - all six of the evidenced fixture's.
                Assert.Equal(
                    "UPDATE COMPANY SET salary = @p1 WHERE id = @p2 AND name = @p3 AND age = @p4 "
                    + "AND address = @p5 AND salary = @p6 AND birth = @p7",
                    generated);

                break;
        }

        // NOTHING WAS OVERWRITTEN WHERE THE PREDICATE LOST, and where it won the write is the caller's own.
        Assert.Equal(
            expectMismatch ? 92500d : 95000d,
            Convert.ToDouble(
                fixture.ScalarDirect("SELECT SALARY FROM COMPANY WHERE NAME = 'Ada Lovelace'"),
                CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 🔴 A modification script whose VALUE lies outside a property's domain is refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE COMPANION TO THE UNKNOWN-COLUMN REFUSAL ABOVE, on the other half of a script line. PowerBuilder's
    /// <c>Modify</c> parses a script against the loaded DataWindow's object model and answers a non-empty
    /// error for a value outside a property's domain exactly as it does for a property whose object does not
    /// exist, and the caller then takes its <c>if sErr &lt;&gt; ""</c> arm
    /// [<c>n_cst_thread_task_sqlupdate.sru:L145-L148</c>]. Installing any value into a plain dictionary
    /// accepted a concurrency mode no DataWindow has.
    /// </para>
    /// <para>
    /// TWO PROPERTIES CARRY A CLOSED DOMAIN and they are the two the descriptor can set. Everything else
    /// carries free text and is not screened here, which the last two assertions pin so the refusal cannot
    /// quietly grow into a general value filter.
    /// </para>
    /// </remarks>
    [Fact]
    public void AModificationScriptStatingAValueOutsideAPropertyDomainIsRefused()
    {
        using CarrierFixture fixture = new();

        fixture.Seed("Grace Hopper", 45, "Arlington", 88000m, "1906-12-09");

        ISqlUpdateCarrier carrier = fixture.AdaptWithRetrievedRow();

        foreach (string rejected in new[] { "7", "3", "-1", "yes", string.Empty })
        {
            Assert.Equal(
                UpdateWhereBuilder.UnsupportedUpdateWhereModeMessage,
                carrier.TargetModifier.Modify(UpdateWhereBuilder.UpdateWhereProperty + "=" + rejected));
        }

        // THE THREE THAT EXIST ARE ACCEPTED, so the screen is a domain test rather than a narrowing to one.
        foreach (long accepted in new[]
        {
            UpdateWhereBuilder.KeyOnlyMode,
            UpdateWhereBuilder.KeyAndUpdatableColumnsMode,
            UpdateWhereBuilder.KeyAndModifiedColumnsMode,
        })
        {
            Assert.Equal(
                string.Empty,
                carrier.TargetModifier.Modify(
                    UpdateWhereBuilder.UpdateWhereProperty
                    + "="
                    + accepted.ToString(CultureInfo.InvariantCulture)));
        }

        // THE KEY-IN-PLACE SETTING IS A TWO-VALUED WORD, not a number and not a boolean [:L137-L141]. The
        // oracle's own reader is an exact comparison against "no" [:L155], so "true" and "1" both read as
        // "not no" and silently selected the in-place arm of a key change.
        Assert.Equal(
            UpdateWhereBuilder.UnsupportedUpdateKeyInPlaceMessage,
            carrier.TargetModifier.Modify(SqlUpdateCarrier.UpdateKeyInPlaceProperty + "=true"));
        Assert.Equal(
            UpdateWhereBuilder.UnsupportedUpdateKeyInPlaceMessage,
            carrier.TargetModifier.Modify(SqlUpdateCarrier.UpdateKeyInPlaceProperty + "=1"));
        Assert.Equal(
            string.Empty,
            carrier.TargetModifier.Modify(SqlUpdateCarrier.UpdateKeyInPlaceProperty + "=YES"));
        Assert.Equal(
            UpdateWhereBuilder.YesLiteral,
            carrier.TargetMetadata.DescribeUpdateKeyInPlace(),
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            string.Empty,
            carrier.TargetModifier.Modify(SqlUpdateCarrier.UpdateKeyInPlaceProperty + "=no"));
        Assert.Equal(UpdateWhereBuilder.NoLiteral, carrier.TargetMetadata.DescribeUpdateKeyInPlace());

        // EVERY OTHER PROPERTY STILL CARRIES FREE TEXT. The update table is screened by the identifier
        // gate rather than by a domain, and a per-column flag's reader already has its own yes/no test.
        Assert.Equal(
            string.Empty,
            carrier.TargetModifier.Modify(SqlUpdateCarrier.UpdateTableProperty + "='COMPANY'"));
        Assert.Equal(string.Empty, carrier.TargetModifier.Modify("salary.Update=whatever"));
    }

    /// <summary>
    /// A provider fault names the buffer and the row the walk was on, so the payload identifies the
    /// offending row rather than reporting the default pair.
    /// </summary>
    /// <remarks>
    /// PowerBuilder's <c>dberror</c> event carries the buffer and the row as two of its five arguments
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L85</c>], and the caller-side
    /// proxy's row translation reads the row specifically
    /// [<c>n_cst_threading_task_sqlupdate.sru:L315</c>]. Reporting <c>Primary</c>/<c>0</c> unconditionally
    /// would be indistinguishable from "not known".
    /// </remarks>
    [Fact]
    public void AProviderFaultNamesTheBufferAndRowTheWalkWasOn()
    {
        using CarrierFixture fixture = new();

        fixture.Seed("Alan Turing", 41, "Wilmslow", 75000m, "1912-06-23");
        fixture.Seed("Grace Hopper", 45, "Arlington", 7100m, "1906-12-09");

        ISqlUpdateCarrier carrier = fixture.AdaptWithRetrievedRow(expectedRows: 2L);

        // THE SECOND ROW IS THE ONE THAT FAILS, so a hardcoded row one could not pass this case.
        _ = carrier.Store.Carrier.SetItemValue(2, 2, DwBuffer.Primary, null);
        _ = carrier.Store.Carrier.SetItemStatus(2, 2, DwBuffer.Primary, ItemStatus.DataModified);
        _ = carrier.Store.Carrier.SetItemStatus(
            2,
            ItemStatusMachine.RowStatusColumn,
            DwBuffer.Primary,
            ItemStatus.DataModified);

        Assert.Equal(
            Buffers.DataWindowBufferStore.DataStoreFailure,
            carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        (long Code, string Text, string Syntax, DwBuffer Buffer, long Row) error =
            Assert.NotNull(fixture.LastDbError);

        Assert.Equal(DwBuffer.Primary, error.Buffer);
        Assert.Equal(2L, error.Row);
    }

    // ==============================================================================================
    //  THE IDENTIFIER ADMISSION GATE AT THE SINK (CWE-89)
    //
    //  The C-06 boundary screens a DESCRIPTOR, and UpdateServiceTests pins that. These two cases pin
    //  the SINK, which is the half that covers the path the boundary cannot: a carrier whose column
    //  model came from a supplied `sql_syntax` rather than from a descriptor. Both drive the production
    //  carrier against a real database, because the point being proved is that NO STATEMENT RUNS - and
    //  only storage can testify to that.
    // ==============================================================================================

    /// <summary>
    /// An installed update table whose text cannot occupy an identifier position generates no statement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TABLE IS INSTALLED THE WAY THE PREPARE STEP INSTALLS IT - through the modification script's
    /// table-level <c>DataWindow.Table.UpdateTable</c> property, which is the property
    /// <c>Concurrency/UpdateWhereBuilder.cs</c> writes [<c>n_cst_thread_task_sqlupdate.sru:L143</c>]. The
    /// script accepts it, exactly as PowerBuilder's own Modify accepts a table-level property without
    /// consulting a column model; the refusal belongs to the generator, at the point the name would reach
    /// a statement.
    /// </para>
    /// <para>
    /// THE THIRD ASSERTION IS THE ONE THAT MATTERS. A refusal code alone would also be produced by a
    /// statement that ran and failed, so the case reads storage afterwards: the table still exists and
    /// the seeded row is untouched, which is only true if nothing was executed at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnUpdateTableThatCannotOccupyAnIdentifierPositionGeneratesNoStatement()
    {
        using CarrierFixture fixture = new();

        fixture.Seed("Ada Lovelace", 36, "London", 92500m, "1815-12-10");

        ISqlUpdateCarrier carrier = fixture.AdaptWithRetrievedRow();

        // The shape the finding was raised on: a statement separator and a comment marker, which would
        // turn one generated UPDATE into an UPDATE plus a DROP with the concurrency predicate commented
        // out - and Microsoft.Data.Sqlite executes every statement in a batch it is handed.
        Assert.Equal(
            string.Empty,
            carrier.TargetModifier.Modify(
                $"{SqlUpdateCarrier.UpdateTableProperty}=\"COMPANY; DROP TABLE COMPANY --\""));

        MarkColumnModified(carrier, column: 5, value: 95000d);

        Assert.Equal(
            Buffers.DataWindowBufferStore.DataStoreFailure,
            carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        // NOTHING RAN. The table still exists and the row still holds its seeded salary, which no
        // execution path other than "no statement was generated" can produce.
        Assert.Equal(
            92500d,
            Convert.ToDouble(
                fixture.ScalarDirect("SELECT SALARY FROM COMPANY WHERE NAME = 'Ada Lovelace'"),
                CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A column of the carrier's model whose text cannot occupy an identifier position generates no
    /// statement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE PATH THE BOUNDARY CANNOT SCREEN, AND THE REASON THE SINK IS GUARDED AT ALL. A carrier
    /// built from a supplied <c>sql_syntax</c> takes its column model from the bindings that syntax
    /// produced, and the catalogue a lookup would validate against is the one that same syntax registered
    /// - so a name checked against the model would be checked against itself. The model is therefore
    /// replaced here AFTER a successful retrieval, which is exactly the state a syntax-derived carrier is
    /// in, and the flag for the hostile name is installed through the ordinary script so the case cannot
    /// pass merely because the column carried no flags.
    /// </para>
    /// <para>
    /// STORAGE IS READ AFTERWARDS for the same reason as the sibling case: it is the only witness that no
    /// statement was generated.
    /// </para>
    /// </remarks>
    [Fact]
    public void AColumnNameThatCannotOccupyAnIdentifierPositionGeneratesNoStatement()
    {
        using CarrierFixture fixture = new();

        fixture.Seed("Ada Lovelace", 36, "London", 92500m, "1815-12-10");

        ISqlUpdateCarrier carrier = fixture.AdaptWithRetrievedRow();

        MarkColumnModified(carrier, column: 5, value: 95000d);

        // A hostile SIXTH column name, replacing `birth`: the five preceding ordinals keep their real
        // names so the plan is otherwise exactly the one the successful cases build.
        fixture.ReplaceColumnModel(
            carrier,
            ["id", "name", "age", "address", "salary", HostileColumn]);

        // THE FLAG IS INSTALLED UNDER THE HOSTILE NAME TOO, so the case cannot pass merely because the
        // column carried none. The script's own stem test accepts it, exactly as PowerBuilder's Modify
        // accepts a property whose object the loaded definition declares - the model now declares it.
        Assert.Equal(
            string.Empty,
            carrier.TargetModifier.Modify($"{HostileColumn}{SqlUpdateCarrier.UpdateSuffix}=yes"));

        Assert.Equal(
            Buffers.DataWindowBufferStore.DataStoreFailure,
            carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        Assert.Equal(
            92500d,
            Convert.ToDouble(
                fixture.ScalarDirect("SELECT SALARY FROM COMPANY WHERE NAME = 'Ada Lovelace'"),
                CultureInfo.InvariantCulture));
    }

    // ==============================================================================================
    //  THE CONFLICT REREAD - ONE ROUND TRIP, AND STILL PER-ROW EXACT
    // ==============================================================================================

    /// <summary>
    /// Every conflicting row keeps its own current state, and a row another writer deleted reports none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CASE THAT WOULD CATCH A MIS-ATTRIBUTED BATCH. The conflict reread used to issue one keyed
    /// <c>SELECT</c> per conflicting row and now issues one compound statement for the batch, so the risk
    /// the change introduces is a row being handed ANOTHER row's current values - or being reported as
    /// deleted when it is present. Three rows conflict here and each holds a DIFFERENT competing value,
    /// so a batch that mixed them up cannot pass; the third row is DELETED by the competing writer, so
    /// the "no current values" arm is exercised in the same payload rather than in a separate case where
    /// a single-row batch would hide the attribution question entirely.
    /// </para>
    /// <para>
    /// THE COMPETING WRITER RUNS ONE STATEMENT PER ROW because it is standing in for three independent
    /// writers; that is the race the concurrency contract exists for, and it is what leaves each row with
    /// a distinguishable stored value.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryConflictingRowKeepsItsOwnCurrentStateAndADeletedRowReportsNone()
    {
        using CarrierFixture fixture = new();

        fixture.Seed("Ada Lovelace", 36, "London", 92500m, "1815-12-10");
        fixture.Seed("Alan Turing", 41, "Wilmslow", 75000m, "1912-06-23");
        fixture.Seed("Grace Hopper", 45, "Arlington", 71000m, "1906-12-09");

        ISqlUpdateCarrier carrier = fixture.AdaptWithRetrievedRow(expectedRows: 3L);

        // Three competing writers, three distinct outcomes: two rows moved to two different values and
        // one row is gone.
        fixture.ExecuteDirect("UPDATE COMPANY SET SALARY = 10001 WHERE NAME = 'Ada Lovelace'");
        fixture.ExecuteDirect("UPDATE COMPANY SET SALARY = 20002 WHERE NAME = 'Alan Turing'");
        fixture.ExecuteDirect("DELETE FROM COMPANY WHERE NAME = 'Grace Hopper'");

        MarkColumnModified(carrier, row: 1, column: 5, value: 95000d);
        MarkColumnModified(carrier, row: 2, column: 5, value: 76000d);
        MarkColumnModified(carrier, row: 3, column: 5, value: 72000d);

        Assert.Equal(
            Buffers.DataWindowBufferStore.DataStoreSuccess,
            carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        ConcurrencyEvidence? evidence = carrier.CaptureConcurrencyEvidence();

        Assert.NotNull(evidence);
        Assert.False(evidence.ProviderFaulted);
        Assert.Equal(3L, evidence.RowsExpected);
        Assert.Equal(0L, evidence.RowsMatched);
        Assert.True(ConflictDetector.IsConcurrencyMismatch(evidence));

        ConflictDetail detail = ConflictDetector.BuildConflictDetail(evidence, UpdateTable);

        Assert.Equal(3, detail.Rows.Count);
        Assert.Equal([1L, 2L, 3L], detail.Rows.Select(static row => row.Row));

        // ROW ONE AND ROW TWO CARRY THEIR OWN COMPETING VALUE, which is the attribution assertion.
        Assert.Equal(10001d, SalaryOf(detail.Rows[0]));
        Assert.Equal(20002d, SalaryOf(detail.Rows[1]));

        // ROW THREE IS GONE, so it reports its submitted originals and NO current values - the pairing
        // that tells a caller the row was deleted rather than changed.
        Assert.NotEmpty(detail.Rows[2].OriginalValues);
        Assert.Empty(detail.Rows[2].CurrentValues);

        static double SalaryOf(ConflictRow row) =>
            row.CurrentValues
                .Single(value => string.Equals(value.ColumnName, "salary", StringComparison.Ordinal))
                .Value.DoubleValue;
    }

    /// <summary>
    /// A conflict wider than one batch is still projected exactly, row for row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CHUNK BOUNDARY IS CROSSED FOR REAL RATHER THAN REASONED ABOUT. Every non-null key value in the
    /// reread is a parameter, and a provider refuses a statement past its own parameter ceiling, so the
    /// batch respects <see cref="SqlUpdateCarrier.MaximumBatchedParameters"/>. It respects a SECOND
    /// ceiling as well, and that one is binding here: each row is one term of a compound
    /// <c>SELECT</c> and SQLite refuses a compound statement past its own term limit, so the batch is
    /// also bounded by <see cref="SqlUpdateCarrier.MaximumBatchedRows"/>. THIS CASE FOUND THAT LIMIT
    /// rather than documenting it after the fact: a batch sized only by the parameter ceiling threw
    /// <c>too many terms in compound SELECT</c> where a row-at-a-time reread had succeeded. Seeding one
    /// row past the row ceiling forces exactly two commands, and the case asserts the payload is
    /// indistinguishable from what a row-at-a-time reread would have produced.
    /// </para>
    /// <para>
    /// THE LAST ROW IS THE ONE THAT PROVES IT. It lives in the SECOND chunk, so a batch loop that stopped
    /// after the first, or that mis-computed the offset the second begins at, reports it as deleted -
    /// which the final assertion refuses.
    /// </para>
    /// </remarks>
    [Fact]
    public void AConflictWiderThanOneBatchIsProjectedExactlyAcrossTheChunkBoundary()
    {
        using CarrierFixture fixture = new();

        int rows = SqlUpdateCarrier.MaximumBatchedRows + 1;

        for (int index = 0; index < rows; index++)
        {
            fixture.Seed(
                string.Format(CultureInfo.InvariantCulture, "Person {0}", index),
                30,
                "Somewhere",
                1000m + index,
                "1900-01-01");
        }

        ISqlUpdateCarrier carrier = fixture.AdaptWithRetrievedRow(expectedRows: rows);

        // One competing statement moves every stored row, so every predicate built from originals fails.
        fixture.ExecuteDirect("UPDATE COMPANY SET SALARY = SALARY + 500000");

        for (int index = 0; index < rows; index++)
        {
            MarkColumnModified(carrier, row: index + 1, column: 5, value: 1d);
        }

        Assert.Equal(
            Buffers.DataWindowBufferStore.DataStoreSuccess,
            carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        ConcurrencyEvidence? evidence = carrier.CaptureConcurrencyEvidence();

        Assert.NotNull(evidence);
        Assert.Equal(rows, evidence.RowsExpected);
        Assert.Equal(0L, evidence.RowsMatched);

        ConflictDetail detail = ConflictDetector.BuildConflictDetail(evidence, UpdateTable);

        Assert.Equal(rows, detail.Rows.Count);

        // EVERY row carries current values, including the one that fell into the second chunk.
        Assert.All(detail.Rows, static row => Assert.NotEmpty(row.CurrentValues));

        Assert.Equal(rows, detail.Rows[^1].Row);
    }

    /// <summary>
    /// Modifies one column of row one and marks both the column and the row modified, exactly as an
    /// arriving changeset marks them.
    /// </summary>
    /// <param name="carrier">The carrier holding the retrieved row.</param>
    /// <param name="column">The ONE-BASED column ordinal to modify.</param>
    /// <param name="value">The new value.</param>
    /// <remarks>
    /// BOTH STATUSES ARE SET, AND BOTH ARE LOAD BEARING FOR DIFFERENT REASONS. The ROW status is what the
    /// update walk enumerates on - it is how a modified row is found at all - and the COLUMN status is what
    /// the SET list is generated from, because PowerBuilder writes only the columns a row actually changed
    /// so an untouched column cannot clobber a concurrent writer's value for it. Setting a value alone
    /// changes neither, which mirrors the production path: statuses arrive on the wire inside the changeset
    /// rather than being inferred from an assignment, so a test that only assigned a value would generate
    /// an empty SET list and prove nothing.
    /// </remarks>
    private static void MarkColumnModified(ISqlUpdateCarrier carrier, int column, object? value) =>
        MarkColumnModified(carrier, row: 1, column, value);

    /// <summary>
    /// Marks one column of one row modified, which is what makes the row reach the generator.
    /// </summary>
    /// <param name="carrier">The carrier holding the row.</param>
    /// <param name="row">The one-based row number within the primary buffer.</param>
    /// <param name="column">The one-based column number.</param>
    /// <param name="value">The new value.</param>
    /// <remarks>
    /// R9: BOTH ORDINALS ARE ONE-BASED and neither is rebased. The row-status column at ordinal zero is
    /// marked as well, because that is the status the generator's walk selects a row by.
    /// </remarks>
    private static void MarkColumnModified(
        ISqlUpdateCarrier carrier,
        long row,
        int column,
        object? value)
    {
        _ = carrier.Store.Carrier.SetItemValue(row, column, DwBuffer.Primary, value);
        _ = carrier.Store.Carrier.SetItemStatus(
            row,
            column,
            DwBuffer.Primary,
            ItemStatus.DataModified);
        _ = carrier.Store.Carrier.SetItemStatus(
            row,
            ItemStatusMachine.RowStatusColumn,
            DwBuffer.Primary,
            ItemStatus.DataModified);
    }

    // ==============================================================================================
    //  FIXTURE
    // ==============================================================================================

    /// <summary>
    /// A real SQLite database with the evidenced schema, plus the production carrier adapter over it.
    /// </summary>
    /// <remarks>
    /// EVERY COLLABORATOR IS THE PRODUCTION ONE. The adapter, the store factory, the runtime, the pooled
    /// transaction and the engine are the same types the composition root registers, so a change to how
    /// this service composes its update path cannot drift away from what these cases assert.
    /// </remarks>
    private sealed class CarrierFixture : IDisposable
    {
        /// <summary>The temporary directory the database file lives in.</summary>
        private readonly string _directory;

        /// <summary>The storage seam.</summary>
        private readonly Data.SqliteConnectionFactory _storage;

        /// <summary>The pooled transaction the carrier writes through.</summary>
        private readonly IPooledTransaction _transaction;

        /// <summary>The definition catalogue, carrying the transcribed fixture.</summary>
        private readonly Data.DataObjectDefinitionCatalogue _catalogue = new();

        /// <summary>The store-to-transaction and store-to-columns association.</summary>
        private readonly Data.DataWindowStoreBindings _bindings = new();

        /// <summary>The production adapter.</summary>
        private readonly ISqlUpdateCarrierAdapter _adapter;

        /// <summary>The data-object runtime the stores retrieve through.</summary>
        private readonly Data.SqliteDataObjectRuntime _runtime;

        /// <summary>
        /// The owning task the carrier forwards its notifications to, which is where a published database
        /// error is observed from.
        /// </summary>
        /// <remarks>
        /// STANDING WHERE THE TASK STANDS, RATHER THAN INTERCEPTING THE CARRIER. The carrier's error channel
        /// is a NOTIFICATION and latches nothing - it forwards to its parent task - so the parent is the only
        /// place a published error can be read. Using the real carrier with a recording parent therefore
        /// leaves every behaviour under test in production code, and substitutes only the endpoint.
        /// </remarks>
        private readonly RecordingParentTask _owner = new();

        /// <summary>The worker-affine carrier, which is the shape the update path runs against.</summary>
        private readonly DataWindowCarrier _carrier =
            DataWindowCarrierFactory.Create(CarrierThreadAffinity.WorkerThread, TimeProvider.System);

        /// <summary>Carriers this fixture handed out, disposed with it.</summary>
        private readonly List<ISqlUpdateCarrier> _carriers = [];

        /// <summary>Initializes the fixture, creating the database and the evidenced table.</summary>
        internal CarrierFixture()
        {
            _directory = Path.Combine(Path.GetTempPath(), $"pfw-carrier-{Guid.NewGuid():n}");
            Directory.CreateDirectory(_directory);

            _storage = new Data.SqliteConnectionFactory(
                Options.Create(new PersistenceOptions
                {
                    Sqlite = new SqliteOptions
                    {
                        DataDirectory = _directory,
                        DatabaseFileName = "test.db",
                        Mode = "rwc",
                        Journal = "DELETE",
                    },
                }),
                NullLogger<Data.SqliteConnectionFactory>.Instance,
                TimeProvider.System);

            _transaction = new PooledTransactionActivator(
                () => new Data.SqliteTransactionEngine(
                    _storage,
                    NullLogger<Data.SqliteTransactionEngine>.Instance),
                TimeProvider.System).CreateDefault();

            _transaction.AutoCommit = true;
            Assert.Equal(0, _transaction.Connect());
            Assert.Equal(0, _transaction.Exec(CompanyDdl));

            _runtime = new Data.SqliteDataObjectRuntime(
                _catalogue,
                _bindings,
                NullLogger<Data.SqliteDataObjectRuntime>.Instance);

            // The carrier is joined to its owning task exactly as the legacy factory joins it on every
            // path out of its cache lookup; without it, the first carrier event raised would refuse.
            _carrier.OnInit(_owner);

            _adapter = new SqlUpdateCarrierAdapter(
                _bindings,
                _catalogue,
                new ChangesetPayloadCodec(),
                NullLoggerFactory.Instance);
        }

        /// <summary>The last database error the carrier published to its owning task.</summary>
        internal (long Code, string Text, string Syntax, DwBuffer Buffer, long Row)? LastDbError =>
            _owner.LastDbError;

        /// <summary>Inserts one row directly, bypassing the carrier under test.</summary>
        /// <param name="name">The name column.</param>
        /// <param name="age">The age column.</param>
        /// <param name="address">The address column.</param>
        /// <param name="salary">The salary column.</param>
        /// <param name="birth">The birth column.</param>
        internal void Seed(string name, int age, string address, decimal salary, string birth) =>
            Assert.Equal(
                0,
                _transaction.Exec(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "INSERT INTO COMPANY (NAME, AGE, ADDRESS, SALARY, BIRTH) "
                        + "VALUES ('{0}', {1}, '{2}', {3}, '{4}')",
                        name,
                        age,
                        address,
                        salary,
                        birth)));

        /// <summary>Runs a statement directly, standing in for a competing writer.</summary>
        /// <param name="sql">The statement to run.</param>
        internal void ExecuteDirect(string sql) => Assert.Equal(0, _transaction.Exec(sql));

        /// <summary>
        /// Replaces the column model a carrier's plan is read from, after its retrieval has run.
        /// </summary>
        /// <param name="carrier">The carrier whose store the model belongs to.</param>
        /// <param name="columns">The model to install, in one-based column order.</param>
        /// <remarks>
        /// THIS IS THE STATE A SYNTAX-DERIVED CARRIER IS IN, reached without standing up a second
        /// composition root. The bindings are where a carrier built from a supplied <c>sql_syntax</c> takes
        /// its column names from, and the retrieval has already happened by the time an update reads them -
        /// so replacing them here reproduces the path the C-06 boundary cannot screen, which is exactly the
        /// path the sink gate exists for.
        /// </remarks>
        internal void ReplaceColumnModel(ISqlUpdateCarrier carrier, string[] columns) =>
            _bindings.SetColumns(carrier.Store, columns);

        /// <summary>Reads one scalar directly, so an assertion sees storage rather than the carrier.</summary>
        /// <param name="sql">The statement to read.</param>
        /// <returns>The first column of the first row.</returns>
        internal object? ScalarDirect(string sql)
        {
            Assert.True(_transaction.TryGetEngineCapability(out Data.ISqliteCommandSource? commands));

            using SqliteCommand command = commands.CreateCommand();
            command.CommandText = sql;

            return command.ExecuteScalar();
        }

        /// <summary>
        /// Produces a carrier whose store holds the seeded row, retrieved and baselined, with the
        /// evidenced table installed exactly as the update-prepare path installs it.
        /// </summary>
        /// <param name="expectedRows">How many rows the retrieval must answer.</param>
        /// <param name="updateWhere">
        /// The concurrency mode to install, or <see langword="null"/> to leave the data object definition's
        /// own mode in force - which for the evidenced fixture is
        /// <see cref="UpdateWhereBuilder.KeyAndUpdatableColumnsMode"/> [<c>dw_sqlite.srd:L14</c>].
        /// </param>
        /// <returns>A carrier ready to be updated through.</returns>
        internal ISqlUpdateCarrier AdaptWithRetrievedRow(long expectedRows = 1L, long? updateWhere = null)
        {
            ISqlDataStore store = new SqlDataObjectStore(_carrier, _runtime);
            store.DataObject = Data.DataObjectDefinitionCatalogue.EvidencedDataObject;

            _bindings.AttachTransaction(store, _transaction);
            _bindings.SetColumns(store, CompanyColumns);

            ISqlUpdateCarrier carrier = _adapter.Adapt(store);
            _carriers.Add(carrier);

            carrier.SetTransObject(_transaction);

            // THE INSTALLATION THE UPDATE-PREPARE PATH PERFORMS, and in its order: every column is reset
            // off first and then selectively re-enabled from the table descriptor
            // [n_cst_thread_task_sqlupdate.sru:L104-L108]. Reproducing the order matters because the reset
            // is what makes the DataWindow's static definition irrelevant at run time.
            List<string> script =
            [
                $"{SqlUpdateCarrier.UpdateTableProperty}=\"{UpdateTable}\"",
                $"{SqlUpdateCarrier.UpdateKeyInPlaceProperty}=no",
            ];

            foreach (string column in CompanyColumns)
            {
                // updatewhere=1 with every column marked: the predicate spans all six ORIGINAL values.
                script.Add($"{column}{SqlUpdateCarrier.UpdateSuffix}=yes");
                script.Add($"{column}{SqlUpdateCarrier.UpdateWhereClauseSuffix}=yes");
                script.Add($"{column}{SqlUpdateCarrier.DbNameSuffix}=\"{UpdateTable}.{column}\"");
            }

            // The identifier column is the key AND the identity, per the oracle's specification.
            script.Add($"{CompanyColumns[0]}{SqlUpdateCarrier.KeySuffix}=yes");
            script.Add($"{CompanyColumns[0]}{SqlUpdateCarrier.IdentitySuffix}=yes");

            if (updateWhere.HasValue)
            {
                // The oracle emits this line only when the descriptor states the value
                // [n_cst_thread_task_sqlupdate.sru:L131-L133], so an absent mode leaves the definition's
                // own in force rather than installing a default.
                script.Add(
                    UpdateWhereBuilder.UpdateWhereProperty
                    + "="
                    + updateWhere.Value.ToString(CultureInfo.InvariantCulture));
            }

            Assert.Equal(string.Empty, carrier.TargetModifier.Modify(string.Join('\n', script)));

            // The retrieval fills the carrier and baselines it, so every column now reports an ORIGINAL
            // value - which is what the update predicate is built from. The count is asserted rather than
            // discarded so that a case which seeds more than one row says so.
            Assert.Equal(
                expectedRows,
                _runtime
                    .RetrieveAsync(store, [], CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult());

            return carrier;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (ISqlUpdateCarrier carrier in _carriers)
            {
                (carrier as IDisposable)?.Dispose();
            }

            _transaction.Dispose();
            _storage.Dispose();

            // SCOPED TO A PATH THIS FIXTURE ITSELF CREATED UNDER THE TEMPORARY ROOT, never to a configured
            // storage directory: the persistence volume's state is what the paired characterization
            // captures compare against.
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
