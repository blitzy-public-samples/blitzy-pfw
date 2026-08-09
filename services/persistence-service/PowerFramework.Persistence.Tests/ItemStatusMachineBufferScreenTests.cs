// ==============================================================================================
//  ItemStatusMachineBufferScreenTests - the buffer screen, pinned against the corrected contract
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/persistence-service/PowerFramework.Persistence/Buffers/ItemStatus.cs
//                     specifically the private buffer screen reached through the two measured
//                     operations that validate their buffer argument
//  BEHAVIOURAL ORACLE ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L32,L56
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru
//                                                       :L138-L147 (the traversal, Primary! then Filter!)
//                     both READ ONLY per constraint C-C
//
//  WHY THIS NARROW SUITE EXISTS, AND WHY IT IS DELIBERATELY NOT A GENERAL ItemStatusMachine SUITE
//  --------------------------------------------------------------------------------------------
//  The buffer screen's OBSERVABLE BEHAVIOUR CHANGED when the published DwBuffer domain was corrected
//  to the three legacy buffers with Primary on zero, and it changed without a single character of its
//  own condition being edited. Under the earlier contract the zero value was a synthetic
//  DW_BUFFER_UNSPECIFIED sentinel and the screen REJECTED it; under the corrected contract zero is
//  Primary and the screen must ACCEPT it. A change of that shape - behaviour moving because a type
//  underneath moved - is exactly the kind nothing in a build notices, so it is pinned here.
//
//  The scope is held to the screen on purpose. A full suite for the classification predicates, the
//  delete walk and the modified-row traversal belongs to this project's own ItemStatus test file,
//  which is a later target and another agent's to author; duplicating it here would create two
//  places for the same case lists to drift. What is asserted below is only what the contract
//  correction touched.
//
//  C-F SELF-AUDIT: every value in this file is synthetic. No credential, key, token, password,
//  connection string or certificate appears.
// ==============================================================================================

using PowerFramework.Contracts.Common.V1;
using PowerFramework.Persistence.Buffers;

using Xunit;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Pins the legacy-buffer screen in <c>ItemStatusMachine</c> against the corrected three-member
/// <see cref="DwBuffer"/> domain.
/// </summary>
public sealed class ItemStatusMachineBufferScreenTests
{
    /// <summary>
    /// A row-status reader that reports every row unmodified. Sufficient for every case here, because
    /// these tests exercise the ARGUMENT SCREEN rather than the traversal, and a screen that rejects
    /// its buffer throws before reading any status at all.
    /// </summary>
    private static ItemStatus NoRowModified(long rowNumber) => ItemStatus.NotModified;

    /// <summary>
    /// All three legacy buffers must be accepted. <see cref="DwBuffer.Primary"/> matters most: it is
    /// the zero value of the corrected domain, so an implementation still screening "zero" as an
    /// absent-field marker would reject the single most common buffer in the legacy sources, where
    /// <c>Primary!</c> appears 36 times against <c>Filter!</c> 18 and <c>Delete!</c> 2.
    /// </summary>
    [Theory]
    [InlineData(DwBuffer.Primary)]
    [InlineData(DwBuffer.Delete)]
    [InlineData(DwBuffer.Filter)]
    public void EveryLegacyBuffer_IsAccepted(DwBuffer buffer)
    {
        long next = ItemStatusMachine.GetNextModified(
            ItemStatusMachine.BeforeFirstRow,
            buffer,
            rowCount: 3,
            NoRowModified);

        Assert.Equal(ItemStatusMachine.NoMoreModifiedRows, next);
        Assert.Empty(ItemStatusMachine.EnumerateModifiedRows(buffer, rowCount: 3, NoRowModified));
    }

    /// <summary>
    /// The default of the enum is a legal buffer, not an absent-field marker. Stated separately from
    /// the theory above so that the assertion survives even if someone renames the members: what is
    /// being pinned is that <c>default(DwBuffer)</c> passes the screen and denotes
    /// <see cref="DwBuffer.Primary"/>.
    /// </summary>
    [Fact]
    public void TheDefaultBuffer_IsPrimary_AndIsAccepted()
    {
        Assert.Equal(DwBuffer.Primary, default(DwBuffer));

        long next = ItemStatusMachine.GetNextModified(
            ItemStatusMachine.BeforeFirstRow,
            default,
            rowCount: 1,
            NoRowModified);

        Assert.Equal(ItemStatusMachine.NoMoreModifiedRows, next);
    }

    /// <summary>
    /// The screen must still reject a value outside the domain, which after the correction can only
    /// arise from an unchecked cast or from an enum number a newer contract introduced. Narrowing with
    /// a defined error is the refactor's standing rule; silently choosing a default buffer would
    /// attribute a row to <see cref="DwBuffer.Primary"/> on no evidence.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(99)]
    [InlineData(-1)]
    public void AnOutOfDomainCast_IsRejected(int rawValue)
    {
        DwBuffer outOfDomain = (DwBuffer)rawValue;

        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => ItemStatusMachine.GetNextModified(
                ItemStatusMachine.BeforeFirstRow,
                outOfDomain,
                rowCount: 1,
                NoRowModified));

        Assert.Equal("buffer", thrown.ParamName);
    }

    /// <summary>
    /// The traversal validates its buffer on the same terms. Asserted separately because
    /// <c>EnumerateModifiedRows</c> delegates its walk to a local iterator, and an argument check that
    /// sat inside the iterator body rather than ahead of it would not run until the first
    /// enumeration - a deferred-validation trap this assertion closes by never enumerating.
    /// </summary>
    [Fact]
    public void TheTraversalValidatesItsBufferEagerly()
    {
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => ItemStatusMachine.EnumerateModifiedRows((DwBuffer)42, rowCount: 1, NoRowModified));

        Assert.Equal("buffer", thrown.ParamName);
    }
}
