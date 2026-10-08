using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace EnglishLearningPlatform.Infrastructure.Commerce;

// A small infrastructure seam for fault injection; payment/gateway contracts do not change.
public interface ICheckoutTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
}

public interface ICheckoutTransactionFactory
{
    Task<ICheckoutTransaction> BeginAsync(CancellationToken ct);
}

public sealed class CheckoutTransactionFactory(AppDbContext db) : ICheckoutTransactionFactory
{
    public async Task<ICheckoutTransaction> BeginAsync(CancellationToken ct) =>
        new EfCheckoutTransaction(await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct));
}

internal sealed class EfCheckoutTransaction(IDbContextTransaction transaction) : ICheckoutTransaction
{
    public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);
    public Task RollbackAsync(CancellationToken ct) => transaction.RollbackAsync(ct);
    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}

public sealed class CheckoutTransactionExecutor(AppDbContext db, ICheckoutTransactionFactory factory,
    ILogger<CheckoutTransactionExecutor> logger)
{
    public static readonly EventId DatabaseFailureEvent = new(8100, "CheckoutDatabaseFailure");
    public static readonly EventId CleanupFailureEvent = new(8101, "CheckoutCleanupFailure");
    public static readonly EventId CancellationEvent = new(8102, "CheckoutCancellation");

    public async Task<T> ExecuteAsync<T>(string operation, Guid reference, Func<Task<T>> work,
        Func<T, bool> shouldCommit, T integrationError, CancellationToken ct)
    {
        ICheckoutTransaction? transaction = null;
        var previousTransaction = db.Database.CurrentTransaction;
        var phase = "begin";
        var commitAttempted = false;
        var commitConfirmed = false;
        var result = integrationError;
        ExceptionDispatchInfo? cancellation = null;
        var failed = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            transaction = await factory.BeginAsync(ct);
            phase = "operation";
            result = await work();
            if (shouldCommit(result))
            {
                phase = "commit";
                commitAttempted = true;
                await transaction.CommitAsync(ct);
                commitConfirmed = true;
            }
        }
        catch (Exception ex) when (FindCancellation(ex) is not null)
        {
            cancellation = ExceptionDispatchInfo.Capture(FindCancellation(ex)!);
            failed = true;
            logger.LogInformation(CancellationEvent,
                "Checkout canceled: Operation={Operation} Reference={Reference} Phase={Phase} CommitConfirmed={CommitConfirmed}",
                operation, reference, phase, commitConfirmed);
        }
        catch (Exception ex) when (IsDatabaseFailure(ex) ||
            (phase is "begin" or "commit" && ex is InvalidOperationException))
        {
            failed = true;
            result = integrationError;
            LogFailure(DatabaseFailureEvent, operation, reference, phase, ex, commitAttempted, commitConfirmed);
        }
        finally
        {
            // Begin may allocate an EF transaction before an interceptor/provider reports failure.
            try
            {
                if (transaction is null && db.Database.CurrentTransaction is { } allocated &&
                    !ReferenceEquals(previousTransaction, allocated))
                    transaction = new EfCheckoutTransaction(allocated);
            }
            catch (Exception ex)
            { LogFailure(CleanupFailureEvent, operation, reference, "recover-begin", ex, commitAttempted, commitConfirmed); }

            if (transaction is not null)
            {
                if (!commitConfirmed)
                {
                    try { await transaction.RollbackAsync(CancellationToken.None); }
                    catch (Exception ex)
                    { LogFailure(CleanupFailureEvent, operation, reference, "rollback", ex, commitAttempted, commitConfirmed); }
                }
                try { await transaction.DisposeAsync(); }
                catch (Exception ex)
                { LogFailure(CleanupFailureEvent, operation, reference, "dispose", ex, commitAttempted, commitConfirmed); }
            }
            if (failed)
            {
                try { db.ChangeTracker.Clear(); }
                catch (Exception ex)
                { LogFailure(CleanupFailureEvent, operation, reference, "clear-tracker", ex, commitAttempted, commitConfirmed); }
            }
        }

        // Cleanup never substitutes its own error for the primary cancellation/database failure.
        // A canceled request can have committed; a later callback must reconcile persisted state.
        cancellation?.Throw();
        ct.ThrowIfCancellationRequested();
        return result;
    }

    public async Task<T> ReadAsync<T>(string operation, Guid reference, Func<Task<T>> work,
        T integrationError, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = await work();
            ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception ex) when (FindCancellation(ex) is not null)
        {
            ExceptionDispatchInfo.Capture(FindCancellation(ex)!).Throw();
            throw;
        }
        catch (Exception ex) when (IsDatabaseFailure(ex))
        {
            LogFailure(DatabaseFailureEvent, operation, reference, "read", ex, false, false);
            ct.ThrowIfCancellationRequested();
            return integrationError;
        }
    }

    private static OperationCanceledException? FindCancellation(Exception ex)
    {
        for (Exception? cause = ex; cause is not null; cause = cause.InnerException)
            if (cause is OperationCanceledException cancellation) return cancellation;
        return null;
    }

    private static bool IsDatabaseFailure(Exception ex)
    {
        for (Exception? cause = ex; cause is not null; cause = cause.InnerException)
            if (cause is DbException or DbUpdateException) return true;
        return false;
    }

    private void LogFailure(EventId eventId, string operation, Guid reference, string phase, Exception ex,
        bool commitAttempted, bool commitConfirmed)
    {
        var cause = ex;
        SqlException? sql = null;
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            cause = current;
            if (current is SqlException sqlError) sql = sqlError;
        }
        // Do not log Exception/Message/ToString: provider errors can contain sensitive payloads.
        logger.LogWarning(eventId,
            "Checkout failure: Operation={Operation} Reference={Reference} Phase={Phase} " +
            "ErrorType={ErrorType} CauseType={CauseType} HResult={HResult} SqlNumber={SqlNumber} " +
            "CommitAttempted={CommitAttempted} CommitConfirmed={CommitConfirmed}",
            operation, reference, phase, ex.GetType().Name, cause.GetType().Name, cause.HResult,
            sql?.Number, commitAttempted, commitConfirmed);
    }
}
