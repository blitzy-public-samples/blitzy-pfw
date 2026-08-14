// =====================================================================================================
//  ExpressionSessionRefusalLoggingTests - THE REFUSAL AN OPERATOR COULD NOT SEE
//  ---------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.DataServices.Grpc.ColumnExpressionService.OpenExpressionSession
//
//  THE FINDING. When the expression-session registry reached its ceiling, this arm returned the
//  registry's code and logged NOTHING. An operator watching a service that had reached its ceiling saw
//  client-side 429s with no server-side evidence at all, and no way to tell an exhausted ceiling from a
//  caller sending malformed requests. The sibling refusal on the VALIDATION-session registry had always
//  recorded both the code and the configured limits.
//
//  WHY THE LIMITS ARE PART OF THE ASSERTION AND NOT JUST THE CODE. "The registry refused" tells an
//  operator nothing they can act on. The ceiling and the idle timeout are the two settings that decide
//  whether it happens again, and both are configuration - so a record that omits them is not actionable
//  and this suite would accept it if it only asserted that SOMETHING was logged.
//
//  WHY A HAND-WRITTEN SINK. The assertion is about what reached the logger, and a hand-written sink
//  states that directly rather than through a mocking framework's matcher DSL.
// =====================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Grpc;
using PowerFramework.Shared.Kernel;
using Xunit;
using Svc = PowerFramework.DataServices.Grpc.ColumnExpressionService;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.DataServices.Tests;

public sealed class ExpressionSessionRefusalLoggingTests
{
    /// <summary>One entry that reached the sink.</summary>
    private sealed record Entry(LogLevel Level, string Message);

    /// <summary>The smallest logger that can prove the refusal was recorded.</summary>
    private sealed class Sink : ILogger<Svc>
    {
        private readonly List<Entry> _entries = [];

        public IReadOnlyList<Entry> Entries => _entries;

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _entries.Add(new Entry(logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    /// <summary>Builds the service over a registry whose ceiling is <paramref name="ceiling"/>.</summary>
    private static (Svc Service, Sink Log) NewService(int ceiling, TimeSpan idleTimeout)
    {
        DataServicesOptions options = new();
        options.Sessions.ExpressionSession.MaxConcurrentSessions = ceiling;
        options.Sessions.ExpressionSession.IdleTimeout = idleTimeout;

        ExpressionTraceBroker trace = new();
        Sink log = new();

        Svc service = new(
            Options.Create(options),
            new ExpressionSessionRegistry(Options.Create(options), timeProvider: null, traceSink: trace),
            new HeadlessDataWindowHostFactory(new DataWindowCatalogue()),
            new MacroInvocationRouter(),
            trace,
            new ColumnExpressionEventRelay(),
            UnresolvedPageResolver.Instance,
            PinyinFirstLetterMatcher.Blocked,
            logger: log);

        return (service, log);
    }

    private static async Task<OpenExpressionSessionResponse> OpenAsync(Svc service)
    {
        OpenExpressionSessionRequest request = new();
        request.DatawindowHandles.Add(DataWindowCatalogue.ServiceFixtureName);

        return await service.OpenExpressionSession(request, new C04CallContext());
    }

    /// <summary>
    /// 🔴 THE REFUSAL IS RECORDED, WITH THE CODE AND BOTH CONFIGURED LIMITS.
    /// </summary>
    /// <remarks>
    /// Ceiling of one, so the second open is refused deterministically without opening a hundred sessions.
    /// The idle timeout is a deliberately unusual value so the assertion cannot pass against a hard-coded
    /// default.
    /// </remarks>
    [Fact]
    public async Task ARefusedOpenIsRecordedWithItsCodeAndItsConfiguredLimits()
    {
        TimeSpan idleTimeout = TimeSpan.FromMinutes(37);
        (Svc service, Sink log) = NewService(ceiling: 1, idleTimeout);

        OpenExpressionSessionResponse first = await OpenAsync(service);
        Assert.Equal(WireRetCode.Ok, first.RetCode);

        // Nothing is logged for a SUCCESSFUL open: this arm is about the refusal.
        Assert.DoesNotContain(
            log.Entries,
            entry => entry.Message.Contains("could not be opened", StringComparison.Ordinal));

        OpenExpressionSessionResponse refused = await OpenAsync(service);
        Assert.Equal(WireRetCode.EBusy, refused.RetCode);

        Entry record = Assert.Single(
            log.Entries,
            entry => entry.Message.Contains("could not be opened", StringComparison.Ordinal));

        Assert.Equal(LogLevel.Warning, record.Level);

        // THE CODE, so an operator can tell an exhausted ceiling from a malformed request.
        Assert.Contains(
            ((long)RetCode.E_BUSY).ToString(CultureInfo.InvariantCulture),
            record.Message,
            StringComparison.Ordinal);

        // BOTH LIMITS, because the code alone is not actionable.
        Assert.Contains("1 concurrent sessions", record.Message, StringComparison.Ordinal);
        Assert.Contains(idleTimeout.ToString(), record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The record survives the ceiling being a realistic number rather than one.
    /// </summary>
    /// <remarks>
    /// A second shape of the same fact, so the assertion above cannot be satisfied by a service that only
    /// logs when the ceiling is one. Three is small enough to reach cheaply and larger than the boundary.
    /// </remarks>
    [Fact]
    public async Task TheRecordNamesWhicheverCeilingWasConfigured()
    {
        (Svc service, Sink log) = NewService(ceiling: 3, TimeSpan.FromMinutes(5));

        for (int opened = 0; opened < 3; opened++)
        {
            Assert.Equal(WireRetCode.Ok, (await OpenAsync(service)).RetCode);
        }

        Assert.Equal(WireRetCode.EBusy, (await OpenAsync(service)).RetCode);

        Entry record = Assert.Single(
            log.Entries,
            entry => entry.Message.Contains("could not be opened", StringComparison.Ordinal));

        Assert.Contains("3 concurrent sessions", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// NOTHING CALLER-SUPPLIED IS RECORDED, because the refusal happens before a name is read.
    /// </summary>
    /// <remarks>
    /// The data-object names a caller sends are the only caller content this operation carries, and an
    /// operator log is not the place for them. Asserted with an obviously identifiable name so a leak
    /// would be unambiguous.
    /// </remarks>
    [Fact]
    public async Task TheRecordCarriesNoCallerSuppliedContent()
    {
        const string Identifiable = "dw_test_dwsvc";
        (Svc service, Sink log) = NewService(ceiling: 1, TimeSpan.FromMinutes(5));

        _ = await OpenAsync(service);
        _ = await OpenAsync(service);

        Entry record = Assert.Single(
            log.Entries,
            entry => entry.Message.Contains("could not be opened", StringComparison.Ordinal));

        Assert.DoesNotContain(Identifiable, record.Message, StringComparison.Ordinal);
    }
}
