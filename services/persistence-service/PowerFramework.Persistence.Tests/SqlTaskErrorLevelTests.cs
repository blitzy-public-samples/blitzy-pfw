// ==================================================================================================
//  SqlTaskErrorLevelTests - the level a framework error is recorded at answers "who has to act"
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS
//
//  The worker-side SQL task host recorded EVERY framework error at ERROR, unconditionally. Most of what
//  reaches that channel is not a fault of this deployment at all: a caller naming a DataWindow that does
//  not exist, a malformed SQL clause, a rejected filter expression and a stale-baseline update conflict
//  are all normal, contract-defined outcomes this service answers correctly - and every one produced a
//  `fail:` record naming a service that was working exactly as published. A measured probe issued two
//  ordinary requests, an unknown handle and a replayed stale update, and got two ERROR records.
//
//  WHY THAT IS A DEFECT AND NOT A COSMETIC PREFERENCE. ERROR is the level operational tooling alerts on.
//  A level that fires for caller-attributable outcomes trains its readers to ignore it, and the one
//  record here that really does mean this deployment is broken - an internal fault, an exhausted host -
//  becomes indistinguishable from the routine traffic around it. The sibling gRPC services report these
//  same conditions at Warning and contain no LogError at all, so this channel was additionally the only
//  place in the service disagreeing with the rest of it.
//
//  THE SUITE ASSERTS THREE SEPARABLE THINGS:
//    1. that a caller-attributable or contract-defined code is NOT recorded at Error - the finding
//    2. that a genuine internal or host fault IS still recorded at Error - the regression guard, and the
//       half a careless fix breaks by lowering everything
//    3. that the contract channel and the redaction posture are untouched - the level is the only thing
//       that changed, and a caller must be told exactly what it was told before
// ==================================================================================================

using Microsoft.Extensions.Logging;
using PowerFramework.Persistence.Errors;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The error-level classification suite.
/// </summary>
public sealed class SqlTaskErrorLevelTests
{
    /// <summary>
    /// A code that means the CALLER must act is not recorded at <see cref="LogLevel.Error"/>.
    /// </summary>
    /// <param name="errCode">The framework code the task reports.</param>
    /// <param name="what">What the code means, named so a failure message identifies the row.</param>
    /// <remarks>
    /// EVERY ROW IS A PUBLISHED OUTCOME OF THIS SERVICE, not a hypothetical. The unknown-handle and
    /// stale-conflict rows are the two the measured probe actually produced; the rest reach the same
    /// channel from the task layer's own rejection arms. A record is still written for each - the rate of
    /// caller refusals must stay visible - it simply is not an alert.
    /// </remarks>
    [Theory]
    [InlineData(RetCode.E_INVALID_HANDLE, "a DataWindow handle nothing is bound to")]
    [InlineData(RetCode.E_INVALID_ARGUMENT, "an argument the contract refuses")]
    [InlineData(RetCode.E_INVALID_DATAOBJECT, "a DataObject the carrier rejects")]
    [InlineData(RetCode.E_INVALID_SQL, "a SQL clause that will not parse")]
    [InlineData(RetCode.E_INVALID_DATA, "data the carrier refuses")]
    [InlineData(RetCode.E_INVALID_TRANSACTION, "a transaction handle that is not live")]
    [InlineData(RetCode.E_SQL_BIND_ARG_FAILED, "an argument that could not be bound")]
    [InlineData(RetCode.E_ACCESS_DENIED, "a refusal on entitlement")]
    [InlineData(RetCode.E_NO_SUPPORT, "an operation this provider does not offer")]
    [InlineData(RetCode.E_NO_IMPLEMENTATION, "an operation no provider implements")]
    [InlineData(RetCode.E_DB_ERROR, "the optimistic-concurrency conflict, answered as 409 at the ingress")]
    [InlineData(RetCode.E_BUSY, "a capacity ceiling that clears")]
    [InlineData(RetCode.E_RETRY, "an outcome whose own name says to retry")]
    public void ACallerAttributableOutcomeIsNotRecordedAsAFaultOfThisDeployment(long errCode, string what)
    {
        RecordingLogger logger = new();

        PersistenceSqlTaskHost host = new(logger, SqlRedactor.Instance);

        _ = host.OnError(errCode, "a detail the task raised");

        (LogLevel Level, string Message) record = Assert.Single(logger.Entries);

        Assert.NotEqual(LogLevel.Error, record.Level);
        Assert.Equal(LogLevel.Warning, record.Level);

        // THE RECORD STILL EXISTS AND STILL NAMES THE CODE. Lowering the level must not cost the reader
        // the ability to see the outcome at all - `what` is echoed into the failure message so a broken
        // row names itself.
        Assert.Contains(
            errCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            record.Message,
            StringComparison.Ordinal);

        Assert.False(string.IsNullOrWhiteSpace(what));
    }

    /// <summary>
    /// A cancellation or a prevention is recorded at <see cref="LogLevel.Information"/>, because the
    /// preserved algebra classifies neither as a failure.
    /// </summary>
    /// <param name="errCode">The code.</param>
    /// <remarks>
    /// THIS ARM FOLLOWS THE LEGACY ALGEBRA RATHER THAN INVENTING A CATEGORY. A cancellation is excluded
    /// from failure explicitly [<c>ws_objects/pfw.shared.pbl.src/isfailed.srf:L11-L13</c>] and a
    /// prevention reads as a SUCCESS [<c>issucceeded.srf:L11-L13</c>, which tests
    /// greater-than-or-equal-to zero]. Recording either as a failure of any severity would contradict the
    /// algebra this refactor is required to reproduce exactly, so the level follows the algebra.
    /// </remarks>
    [Theory]
    [InlineData(RetCode.CANCELLED)]
    [InlineData(RetCode.PREVENT)]
    public void AnOutcomeTheAlgebraCallsNeitherFailedNorFaultyIsRecordedAsInformation(long errCode)
    {
        RecordingLogger logger = new();

        PersistenceSqlTaskHost host = new(logger, SqlRedactor.Instance);

        _ = host.OnError(errCode, "a detail the task raised");

        Assert.Equal(LogLevel.Information, Assert.Single(logger.Entries).Level);
    }

    /// <summary>
    /// A genuine internal or host fault IS still recorded at <see cref="LogLevel.Error"/>.
    /// </summary>
    /// <param name="errCode">The code.</param>
    /// <remarks>
    /// <b>THE REGRESSION GUARD, AND THE HALF A CARELESS FIX BREAKS.</b> The finding is that ERROR fired
    /// too often; the wrong correction is to lower everything, which would silence the only records that
    /// genuinely mean an operator must act. Each row here says this deployment - not its caller - has a
    /// problem.
    /// </remarks>
    [Theory]
    [InlineData(RetCode.E_INTERNAL_ERROR)]
    [InlineData(RetCode.E_OUT_OF_MEMORY)]
    [InlineData(RetCode.E_WIN32_ERROR)]
    [InlineData(RetCode.E_IO_ERROR)]
    [InlineData(RetCode.UNKNOWN)]
    [InlineData(RetCode.FAILED)]
    public void AGenuineInternalFaultIsStillRecordedAsAnError(long errCode)
    {
        RecordingLogger logger = new();

        PersistenceSqlTaskHost host = new(logger, SqlRedactor.Instance);

        _ = host.OnError(errCode, "a detail the task raised");

        Assert.Equal(LogLevel.Error, Assert.Single(logger.Entries).Level);
    }

    /// <summary>
    /// An unrecognised code is recorded at <see cref="LogLevel.Error"/>.
    /// </summary>
    /// <remarks>
    /// THE DEFAULT DIRECTION IS PART OF THE FIX, NOT AN OVERSIGHT. An unrecognised code carries no
    /// evidence that the outcome is the caller's doing, so the conservative reading is that this
    /// deployment has a problem. A default of Warning would let a genuinely new internal fault arrive
    /// below the level anyone alerts on - the opposite defect to the one being fixed, and a worse one.
    /// </remarks>
    [Fact]
    public void AnUnrecognisedCodeIsRecordedAsAnError()
    {
        RecordingLogger logger = new();

        PersistenceSqlTaskHost host = new(logger, SqlRedactor.Instance);

        _ = host.OnError(-999_999, "a code from no published set");

        Assert.Equal(LogLevel.Error, Assert.Single(logger.Entries).Level);
    }

    /// <summary>
    /// A task that raised no detail text produces a record that says so, rather than an empty redaction.
    /// </summary>
    /// <remarks>
    /// 🔴 THE SHARED TEMPLATE DEGENERATED WHEN THERE WAS NOTHING TO SAY. Two arms raise no text
    /// deliberately - the stale-update conflict and the not-implemented arm - and both produced the
    /// literal record "Redacted detail (0 chars): " with nothing after the colon, so the field that
    /// exists to tell an operator whether a value was present instead read as a truncated or broken
    /// record. Stating that no text was raised is a fact about the task; printing an empty redaction is
    /// an artefact of the format.
    /// </remarks>
    [Fact]
    public void AnErrorWithNoDetailStatesThatNoTextWasRaised()
    {
        RecordingLogger logger = new();

        PersistenceSqlTaskHost host = new(logger, SqlRedactor.Instance);

        _ = host.OnError(RetCode.E_DB_ERROR, string.Empty);

        (LogLevel Level, string Message) record = Assert.Single(logger.Entries);

        Assert.Contains("raised no detail text", record.Message, StringComparison.Ordinal);

        // The degenerate rendering is gone.
        Assert.DoesNotContain("(0 chars)", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Redacted detail", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The contract channel still receives the raw text, and the log still receives only the redacted one.
    /// </summary>
    /// <remarks>
    /// <b>THE LEVEL IS THE ONLY THING THAT CHANGED, AND THIS IS WHAT PROVES IT.</b> The two channels
    /// carry deliberately different content: the caller-side sink gets the text exactly as the task
    /// raised it, because that is the published contract, while the record gets it only after redaction -
    /// the retrieval task composes some of these texts from an externally supplied SORT or FILTER
    /// expression, and a filter expression routinely carries literal comparison values. A change to the
    /// level had no business touching either, so both are asserted rather than assumed.
    /// </remarks>
    [Fact]
    public void TheContractChannelAndTheRedactionPostureAreUnchanged()
    {
        RecordingLogger logger = new();
        CollectingFaultSink sink = new();

        PersistenceSqlTaskHost host = new(logger, SqlRedactor.Instance, sink, parentTasking: null);

        const string Raw = "Filter rejected: salary > 12345 AND name = 'Alice'";

        _ = host.OnError(RetCode.E_INVALID_DATA, Raw);

        // The caller is told exactly what it was told before, unmasked.
        (long Code, string Text) forwarded = Assert.Single(sink.Faults);

        Assert.Equal(RetCode.E_INVALID_DATA, forwarded.Code);
        Assert.Equal(Raw, forwarded.Text);

        // The record carries the length as safe metadata and the text only after redaction, so the
        // literals in the raw text do not reach it.
        (LogLevel Level, string Message) record = Assert.Single(logger.Entries);

        Assert.Contains("Redacted detail", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("12345", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice", record.Message, StringComparison.Ordinal);
    }

    /// <summary>A logger that records the level and rendered message of every entry.</summary>
    private sealed class RecordingLogger : ILogger<PersistenceSqlTaskHost>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        /// <summary>The entries written, in order.</summary>
        internal IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        /// <remarks>
        /// EVERY LEVEL IS ENABLED, deliberately: the assertion is about which level the host CHOSE, so a
        /// logger that filtered would turn a wrong choice into an absent record and the tests would read
        /// as a missing-record defect instead of a classification one.
        /// </remarks>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _entries.Add((logLevel, formatter(state, exception)));
        }
    }

    /// <summary>A caller-side sink that keeps every fault forwarded to it.</summary>
    private sealed class CollectingFaultSink : IQueryFaultSink
    {
        private readonly List<(long Code, string Text)> _faults = [];

        /// <summary>The faults forwarded, in order.</summary>
        internal IReadOnlyList<(long Code, string Text)> Faults => _faults;

        /// <inheritdoc/>
        /// <remarks>
        /// Not exercised here: this suite drives the general-error channel, and the structured
        /// database-error channel is a different member with its own suite. Recording the call keeps the
        /// double honest about having been reached if a future case does reach it.
        /// </remarks>
        public void OnDbError(in DbErrorData error) => _faults.Add((error.SqlDbCode, nameof(OnDbError)));

        /// <inheritdoc/>
        public void OnError(long code, string errorText) => _faults.Add((code, errorText));
    }
}
