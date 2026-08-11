// ==================================================================================================
//  ExpressionSessionHostFaultTests - THE HOST-FAULT PAYLOAD IS SAFE, AND THE DETAIL IS STILL KEPT
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   ExpressionSession.Faulted  -  the CrossDataWindowStatus.HostFaulted resolution
//
//  WHAT THESE TESTS PIN (CWE-209)
//  ------------------------------------------------------------------------------------------------
//  A cross-DataWindow reference resolves to a co-resident expression service, and that service's own
//  call throws. The host is arbitrary application code this service reached but does not own, so its
//  exception message can carry a file path, a connection string fragment, a SQL statement, a
//  configuration key, or a stack-shaped type name. That text used to travel to the caller in BOTH wire
//  fields - the rendered `text` and the structured `format_args` - which handed an authenticated caller
//  internal detail it never asked for and this service cannot vet.
//
//  The fix is not to swallow the fault. It is to NARROW the detail and move what survives: the fault's
//  TYPE CHAIN is recorded server-side against a correlation id, and the payload carries that id plus fixed
//  text. So there are three properties to pin, and each closes a different failure:
//
//    1. NOTHING FROM THE EXCEPTION IS ON THE WIRE - not the type name, not the message, in either field.
//    2. THE FAULT IS STILL IDENTIFIABLE, joined to the payload by the same id. A "fix" that merely deleted
//       everything would pass property 1 and leave an operator with an unactionable failure.
//    3. THE EXCEPTION OBJECT REACHES NEITHER CHANNEL. It used to be attached to the log record, which every
//       provider renders by calling ToString() - message chain and stack together - so the message the wire
//       was carefully denied was published in full one layer over. A log record is a different trust domain
//       from this process, and a host holds this expression's variable values, so its message is caller data
//       wherever it came from.
//
//  And the id has to be DETERMINISTIC, because it appears in a payload a paired characterization
//  recording compares byte for byte. A GUID or a timestamp would have to be masked on both sides;
//  session id plus a per-session ordinal does not.
// ==================================================================================================

using Microsoft.Extensions.Logging;

using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.Shared.Kernel;

using Xunit;

namespace PowerFramework.DataServices.Tests;

public sealed class ExpressionSessionHostFaultTests
{
    // The strings a leak would show up as. Each is shaped like real internal detail rather than a
    // placeholder, because the assertions are "this did not travel" and a vague marker would prove less.
    private const string SecretishMessage =
        "Data Source=/srv/secrets/company.db;Password=hunter2 while running "
        + "SELECT * FROM COMPANY WHERE salary > 1000";

    private static readonly string ThrownTypeName =
        typeof(InvalidOperationException).FullName!;

    [Fact]
    public void AHostFaultCarriesNoExceptionTypeOrMessageInTheRenderedText()
    {
        RecordingLogger logger = new();
        ExpressionSession session = new("session-1", logger: logger);
        DataWindowHandle handle = session.Register(new ThrowingHost(SecretishMessage));

        ForeignVariableResolution resolution = session.ResolveForeignVariable(handle, "gv_total");

        Assert.Equal(CrossDataWindowStatus.HostFaulted, resolution.Status);
        Assert.Equal(RetCode.E_INTERNAL_ERROR, resolution.ReturnCode);
        Assert.NotNull(resolution.Error);

        // THE TWO THINGS THAT MUST NOT BE THERE.
        Assert.DoesNotContain(SecretishMessage, resolution.Error!.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", resolution.Error.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", resolution.Error.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(ThrownTypeName, resolution.Error.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", resolution.Error.Text, StringComparison.Ordinal);

        // AND THE THINGS THAT MUST: the caller's own handle, its own session id, and an actionable id.
        Assert.Contains(handle.Value, resolution.Error.Text, StringComparison.Ordinal);
        Assert.Contains("session-1", resolution.Error.Text, StringComparison.Ordinal);
        Assert.Contains("session-1/fault/1", resolution.Error.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AHostFaultCarriesNoExceptionTypeOrMessageInTheStructuredArguments()
    {
        // `format_args` IS WHAT A CONSUMER RE-RENDERS FROM, so sanitising the rendered text alone would
        // have relocated the leak rather than closed it. The arguments are asserted as an exact list.
        ExpressionSession session = new("session-2");
        DataWindowHandle handle = session.Register(new ThrowingHost(SecretishMessage));

        ForeignVariableResolution resolution = session.ResolveForeignVariable(handle, "gv_total");

        Assert.NotNull(resolution.Error);
        Assert.Equal(
            [handle.Value, "session-2", "session-2/fault/1"],
            resolution.Error!.FormatArguments);

        foreach (string argument in resolution.Error.FormatArguments)
        {
            Assert.DoesNotContain("hunter2", argument, StringComparison.Ordinal);
            Assert.DoesNotContain("InvalidOperationException", argument, StringComparison.Ordinal);
        }

        // The one-legacy-site exception channel stays empty: a managed host fault has no legacy
        // counterpart, so it must not arrive through the field that exists because [:L739] published one.
        Assert.Null(resolution.Error.CaughtExceptionText);
    }

    [Fact]
    public void TheExceptionDetailIsRecordedServerSideUnderTheSameCorrelationId()
    {
        RecordingLogger logger = new();
        ExpressionSession session = new("session-3", logger: logger);
        DataWindowHandle handle = session.Register(new ThrowingHost(SecretishMessage));

        ForeignVariableResolution resolution = session.ResolveForeignVariable(handle, "gv_total");

        // PROPERTY 2: THE DIAGNOSTIC SURVIVES, IN ITS ALLOWLISTED FORM. One error record naming the
        // exception's TYPE CHAIN, and NO exception object.
        //
        // THIS ASSERTION USED TO REQUIRE THE OPPOSITE, AND THAT WAS THE DEFECT IT FROZE. It demanded the
        // exception object be attached "so a configured provider writes type, message and stack in full" -
        // which is precisely the leak: a host holds this expression's variable values, so its message is
        // caller data wherever it came from, and a log record is a different trust domain from this process.
        // Keeping the detail off the wire and putting it in the log solved half of one problem.
        RecordedLogEntry entry = Assert.Single(logger.Entries);

        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Null(entry.Exception);

        // The type is named, so the fault is still identifiable...
        Assert.Contains(
            typeof(InvalidOperationException).FullName!,
            entry.Message,
            StringComparison.Ordinal);

        // ...and the message it was carrying is not, on either channel.
        Assert.DoesNotContain(SecretishMessage, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", entry.Message, StringComparison.Ordinal);

        // AND THE JOIN IS THE ID THE CALLER WAS GIVEN. This is the whole design in one assertion: the
        // caller quotes the id, an operator finds this record.
        Assert.Contains("session-3/fault/1", entry.Message, StringComparison.Ordinal);
        Assert.Contains("session-3/fault/1", resolution.Error!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ANullLoggerLosesTheDetailRatherThanSubstitutingItOntoTheWire()
    {
        // THE TEMPTING WRONG BEHAVIOUR. "No logger configured, so put the detail in the payload instead"
        // would make the boundary depend on a deployment's logging configuration. It does not.
        ExpressionSession logged = new("session-4a", logger: new RecordingLogger());
        ExpressionSession unlogged = new("session-4b");

        DataWindowHandle loggedHandle = logged.Register(new ThrowingHost(SecretishMessage));
        DataWindowHandle unloggedHandle = unlogged.Register(new ThrowingHost(SecretishMessage));

        ForeignVariableResolution withLogger = logged.ResolveForeignVariable(loggedHandle, "gv_total");
        ForeignVariableResolution withoutLogger =
            unlogged.ResolveForeignVariable(unloggedHandle, "gv_total");

        // Same shape, same absence of detail; only the session and handle text differ.
        Assert.Equal(withLogger.Error!.FormatTemplate, withoutLogger.Error!.FormatTemplate);
        Assert.Equal(
            withLogger.Error.FormatArguments.Length,
            withoutLogger.Error.FormatArguments.Length);
        Assert.DoesNotContain("hunter2", withoutLogger.Error.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "InvalidOperationException",
            withoutLogger.Error.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheCorrelationIdIsDeterministicAndMonotonicWithinItsSession()
    {
        // DETERMINISM IS WHY IT MAY BE ON THE WIRE AT ALL. Two sessions driven through the same sequence
        // of calls produce the same identifiers, so a paired characterization recording compares them
        // directly instead of masking them.
        ExpressionSession first = new("session-5");
        ExpressionSession second = new("session-5");

        DataWindowHandle firstHandle = first.Register(new ThrowingHost(SecretishMessage));
        DataWindowHandle secondHandle = second.Register(new ThrowingHost(SecretishMessage));

        string[] fromFirst =
        [
            IdOf(first.ResolveForeignVariable(firstHandle, "a")),
            IdOf(first.ResolveForeignVariable(firstHandle, "b")),
            IdOf(first.ResolveForeignVariable(firstHandle, "c")),
        ];

        string[] fromSecond =
        [
            IdOf(second.ResolveForeignVariable(secondHandle, "a")),
            IdOf(second.ResolveForeignVariable(secondHandle, "b")),
            IdOf(second.ResolveForeignVariable(secondHandle, "c")),
        ];

        Assert.Equal(["session-5/fault/1", "session-5/fault/2", "session-5/fault/3"], fromFirst);
        Assert.Equal(fromFirst, fromSecond);

        // No clock and no randomness, stated as a property rather than as a comment: the ordinal is the
        // only varying part, and it varies by exactly one per fault.
        Assert.Distinct(fromFirst);
    }

    [Fact]
    public void TheCalculationPathIsSanitisedToo()
    {
        // THERE ARE THREE CATCH SITES, NOT ONE. Resolution, foreign-variable calculation and context-host
        // resolution all funnel through the same Faulted(), and the calculation path is the one whose
        // result reaches a caller as a VALUE - so it gets its own assertion rather than being assumed.
        ExpressionSession session = new("session-6");
        DataWindowHandle handle = session.Register(new ThrowingHost(SecretishMessage));

        // Bind first with a host that answers the lookup, then let the calculation throw.
        ExpressionSession bindable = new("session-7");
        DataWindowHandle bindableHandle = bindable.Register(new ThrowingOnCalcHost(SecretishMessage));

        ForeignVariableResolution bound = bindable.ResolveForeignVariable(bindableHandle, "gv_total");
        Assert.Equal(CrossDataWindowStatus.Resolved, bound.Status);

        ExpressionValueResult calculated = bindable.CalcForeignVariableValue(
            bound.ToReference(),
            callerRow: 1L,
            callerDwo: null,
            caller: null);

        Assert.Equal(CrossDataWindowStatus.HostFaulted, calculated.Status);

        // THE VALUE IS EMPTY AND THE ERROR IS CLEAN - a faulted calculation never carries a value, and it
        // never carries the host's exception text either.
        Assert.Equal(string.Empty, calculated.Value);
        Assert.NotNull(calculated.Error);
        Assert.DoesNotContain("hunter2", calculated.Error!.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "InvalidOperationException",
            calculated.Error.Text,
            StringComparison.Ordinal);
        Assert.Contains("session-7/fault/1", calculated.Error.Text, StringComparison.Ordinal);

        // And the resolution path on a different session is unaffected by the above - the ordinal is
        // per-session, so this is still fault 1.
        ForeignVariableResolution other = session.ResolveForeignVariable(handle, "gv_total");
        Assert.Contains("session-6/fault/1", other.Error!.Text, StringComparison.Ordinal);
    }

    private static string IdOf(ForeignVariableResolution resolution)
    {
        Assert.NotNull(resolution.Error);

        // The identifier is the third argument by construction; reading it from there rather than
        // parsing the rendered text keeps this helper independent of the message wording.
        return resolution.Error!.FormatArguments[2];
    }

    /// <summary>A host whose variable lookup throws with a message shaped like internal detail.</summary>
    private sealed class ThrowingHost(string message) : IExpressionServiceHost
    {
        public int FindVarIndex(string? name) => throw new InvalidOperationException(message);

        public string CalcVarExpValue(
            long row,
            IDataWindowObject? dwo,
            int index,
            long ctxRow,
            IDataWindowObject? ctxDwo,
            IExpressionServiceHost? ctxExpSvc) => throw new InvalidOperationException(message);

        public string CalcVarExpValue(
            int index,
            long ctxRow,
            IDataWindowObject? ctxDwo,
            IExpressionServiceHost? ctxExpSvc) => throw new InvalidOperationException(message);

        public string GetItemExpValue(long row, string? columnName) =>
            throw new InvalidOperationException(message);
    }

    /// <summary>A host that BINDS successfully and then throws when the value is calculated.</summary>
    private sealed class ThrowingOnCalcHost(string message) : IExpressionServiceHost
    {
        public int FindVarIndex(string? name) => 1;

        public string CalcVarExpValue(
            long row,
            IDataWindowObject? dwo,
            int index,
            long ctxRow,
            IDataWindowObject? ctxDwo,
            IExpressionServiceHost? ctxExpSvc) => throw new InvalidOperationException(message);

        public string CalcVarExpValue(
            int index,
            long ctxRow,
            IDataWindowObject? ctxDwo,
            IExpressionServiceHost? ctxExpSvc) => throw new InvalidOperationException(message);

        public string GetItemExpValue(long row, string? columnName) => string.Empty;
    }

    private sealed record RecordedLogEntry(LogLevel Level, string Message, Exception? Exception);

    /// <summary>
    /// The smallest logger that can prove the detail was recorded. Deliberately not a mocking framework:
    /// the assertion is about what reached the sink, and a hand-written sink states that directly.
    /// </summary>
    private sealed class RecordingLogger : ILogger
    {
        private readonly List<RecordedLogEntry> _entries = [];

        public IReadOnlyList<RecordedLogEntry> Entries => _entries;

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _entries.Add(new RecordedLogEntry(logLevel, formatter(state, exception), exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
