using System.Collections.Concurrent;
using System.Data.Common;
using EnglishLearningPlatform.Infrastructure.Commerce;
using Microsoft.Extensions.Logging;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class InjectedCheckoutDatabaseException() : DbException(SensitiveMarker)
{
    public const string SensitiveMarker = "test-sensitive-payload-must-not-be-logged";
}

public sealed class CheckoutTransactionFaults
{
    public Exception? BeginFailure { get; set; }
    public Exception? CommitFailure { get; set; }
    public Exception? RollbackFailure { get; set; }
    public Exception? DisposeFailure { get; set; }
    public bool CommitBeforeFailure { get; set; }
    public bool EndTransactionBeforeFailure { get; set; }
    public bool BeginFailureAfterAllocation { get; set; }
    public int Begins;
    public int Commits;
    public int Rollbacks;
    public int Disposes;

    public void Reset()
    {
        BeginFailure = CommitFailure = RollbackFailure = DisposeFailure = null;
        CommitBeforeFailure = EndTransactionBeforeFailure = BeginFailureAfterAllocation = false;
        Begins = Commits = Rollbacks = Disposes = 0;
    }
}

public sealed class FaultingCheckoutTransactionFactory(ICheckoutTransactionFactory inner, CheckoutTransactionFaults faults)
    : ICheckoutTransactionFactory
{
    public async Task<ICheckoutTransaction> BeginAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref faults.Begins);
        if (faults.BeginFailure is { } before && !faults.BeginFailureAfterAllocation) throw before;
        var transaction = await inner.BeginAsync(ct);
        if (faults.BeginFailure is { } after) throw after;
        return new FaultingCheckoutTransaction(transaction, faults);
    }

    private sealed class FaultingCheckoutTransaction(ICheckoutTransaction inner, CheckoutTransactionFaults faults) : ICheckoutTransaction
    {
        public async Task CommitAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref faults.Commits);
            if (faults.EndTransactionBeforeFailure) await inner.RollbackAsync(CancellationToken.None);
            if (faults.CommitFailure is { } before && !faults.CommitBeforeFailure) throw before;
            await inner.CommitAsync(ct);
            if (faults.CommitFailure is { } after) throw after;
        }

        public async Task RollbackAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref faults.Rollbacks);
            // Roll back the isolated DB first; inject provider/transport failure without leaving locks behind.
            await inner.RollbackAsync(ct);
            if (faults.RollbackFailure is { } failure) throw failure;
        }

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref faults.Disposes);
            await inner.DisposeAsync();
            if (faults.DisposeFailure is { } failure) throw failure;
        }
    }
}

public sealed record CheckoutCapturedLog(EventId EventId, IReadOnlyDictionary<string, object?> Fields, string Text, Exception? Exception);

public sealed class CheckoutLogCapture : ILoggerProvider
{
    public ConcurrentQueue<CheckoutCapturedLog> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this,
        categoryName == typeof(CheckoutTransactionExecutor).FullName);
    public void Dispose() { }

    private sealed class CaptureLogger(CheckoutLogCapture capture, bool enabled) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => enabled;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!enabled) return;
            var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            capture.Entries.Enqueue(new(eventId, fields, formatter(state, exception), exception));
        }
    }
}
