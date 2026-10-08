using System.Net;
using System.Net.Http.Json;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Infrastructure.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed partial class CheckoutFlowTests
{
    private void ResetTransactionFaults()
    {
        factory.TransactionFaults.Reset();
        factory.Logs.Entries.Clear();
    }

    private void AssertOriginalFailure(string phase, string errorType, params string[] cleanupPhases)
    {
        var root = Assert.Single(factory.Logs.Entries, e => e.EventId == CheckoutTransactionExecutor.DatabaseFailureEvent);
        Assert.Equal(phase, root.Fields["Phase"]);
        Assert.Equal(errorType, root.Fields["ErrorType"]);
        foreach (var cleanup in cleanupPhases)
            Assert.Contains(factory.Logs.Entries, e => e.EventId == CheckoutTransactionExecutor.CleanupFailureEvent &&
                Equals(e.Fields["Phase"], cleanup));
        Assert.All(factory.Logs.Entries, e =>
        {
            Assert.Null(e.Exception);
            Assert.DoesNotContain(InjectedCheckoutDatabaseException.SensitiveMarker, e.Text);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransactionBeginFailure_ReturnsControlledHttpResult(bool webhook)
    {
        var (student, course) = await Setup(); var order = webhook ? await Create(student, course) : null;
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Checkout?courseId={course.Id}");
        ResetTransactionFaults();
        factory.TransactionFaults.BeginFailure = new InjectedCheckoutDatabaseException();
        try
        {
            using var response = webhook
                ? await client.PostAsJsonAsync("/payments/webhook", Event(order!))
                : await client.PostAsync("/Checkout/CreateOrder", new FormUrlEncodedContent(new Dictionary<string, string>
                  { ["CourseId"] = course.Id.ToString(), ["__RequestVerificationToken"] = token }));
            Assert.Equal(webhook ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Redirect, response.StatusCode);
            if (webhook) Assert.Contains("retry_later", await response.Content.ReadAsStringAsync());
            else
            {
                using var page = await client.GetAsync(response.Headers.Location);
                Assert.Contains("Chưa thể xử lý thanh toán thử nghiệm", WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync()));
            }
            Assert.Equal(1, factory.TransactionFaults.Begins);
            Assert.Equal(0, factory.TransactionFaults.Commits);
            Assert.Equal(0, factory.TransactionFaults.Rollbacks);
            AssertOriginalFailure("begin", nameof(InjectedCheckoutDatabaseException));
        }
        finally { factory.TransactionFaults.Reset(); }
        await AssertCounts(course.Id, webhook ? 1 : 0, 0, 0);
    }

    [Fact]
    public async Task WrappedProviderBeginFailure_IsClassifiedAsDatabaseFailure()
    {
        var (student, course) = await Setup(); ResetTransactionFaults();
        factory.TransactionFaults.BeginFailure = new InvalidOperationException("provider wrapper", new InjectedCheckoutDatabaseException());
        try
        {
            using var scope = factory.Services.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<ICheckoutService>().CreateOrderAsync(student.Id, course.Id);
            Assert.Equal(CheckoutCode.IntegrationError, result.Code);
            AssertOriginalFailure("begin", nameof(InvalidOperationException));
            var root = factory.Logs.Entries.Single(e => e.EventId == CheckoutTransactionExecutor.DatabaseFailureEvent);
            Assert.Equal(nameof(InjectedCheckoutDatabaseException), root.Fields["CauseType"]);
        }
        finally { factory.TransactionFaults.Reset(); }
        await AssertCounts(course.Id, 0, 0, 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseFailure_WithRollbackAndDisposeFailures_PreservesOriginalError(bool webhook)
    {
        var (student, course) = await Setup(); var order = await Create(student, course);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Checkout/Order/{order.Id}");
        ResetTransactionFaults();
        factory.Faults.FailEnrollment = true;
        factory.TransactionFaults.RollbackFailure = new InvalidOperationException("rollback has completed");
        factory.TransactionFaults.DisposeFailure = new InvalidOperationException("dispose failure");
        try
        {
            using var response = webhook
                ? await client.PostAsJsonAsync("/payments/webhook", Event(order))
                : await client.PostAsync($"/Checkout/Simulate/{order.Id}", new FormUrlEncodedContent(new Dictionary<string, string>
                  { ["scenario"] = "success", ["__RequestVerificationToken"] = token }));
            Assert.Equal(webhook ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Redirect, response.StatusCode);
            if (webhook) Assert.Contains("retry_later", await response.Content.ReadAsStringAsync());
            else
            {
                using var page = await client.GetAsync(response.Headers.Location);
                Assert.Contains("role=\"alert\"", await page.Content.ReadAsStringAsync());
            }
            Assert.True(factory.Faults.SawSavedPayment);
            AssertOriginalFailure("operation", nameof(DbUpdateException), "rollback", "dispose");
            Assert.Equal(1, factory.TransactionFaults.Rollbacks);
            Assert.Equal(1, factory.TransactionFaults.Disposes);
        }
        finally { factory.Faults.FailEnrollment = false; factory.TransactionFaults.Reset(); }
        await AssertCounts(course.Id, 1, 0, 0);
        Assert.Equal(CheckoutCode.Allowed, await Process(Event(order)));
        await AssertCounts(course.Id, 1, 1, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitFailure_AfterTransactionEnded_DoesNotEscapeRollbackError(bool webhook)
    {
        var (student, course) = await Setup(); var order = webhook ? await Create(student, course) : null;
        ResetTransactionFaults();
        factory.TransactionFaults.EndTransactionBeforeFailure = true;
        factory.TransactionFaults.CommitFailure = new InjectedCheckoutDatabaseException();
        try
        {
            if (webhook) Assert.Equal(CheckoutCode.IntegrationError, await Process(Event(order!)));
            else
            {
                using var scope = factory.Services.CreateScope();
                Assert.Equal(CheckoutCode.IntegrationError, (await scope.ServiceProvider.GetRequiredService<ICheckoutService>()
                    .CreateOrderAsync(student.Id, course.Id)).Code);
            }
            AssertOriginalFailure("commit", nameof(InjectedCheckoutDatabaseException), "rollback");
        }
        finally { factory.TransactionFaults.Reset(); }
        await AssertCounts(course.Id, webhook ? 1 : 0, 0, 0);
    }

    [Fact]
    public async Task LostCommitAcknowledgement_Returns503_NoAutomaticRetry_ThenReplayConfirmsPersistedPayment()
    {
        var (student, course) = await Setup(); var order = await Create(student, course); var callback = Event(order);
        using var client = IdentityTestHelpers.CreateClient(factory);
        ResetTransactionFaults();
        factory.TransactionFaults.CommitBeforeFailure = true;
        factory.TransactionFaults.CommitFailure = new InjectedCheckoutDatabaseException();
        try
        {
            using var response = await client.PostAsJsonAsync("/payments/webhook", callback);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("retry_later", await response.Content.ReadAsStringAsync());
            Assert.Equal(1, factory.TransactionFaults.Begins); Assert.Equal(1, factory.TransactionFaults.Commits);
            AssertOriginalFailure("commit", nameof(InjectedCheckoutDatabaseException), "rollback");
            var root = factory.Logs.Entries.Single(e => e.EventId == CheckoutTransactionExecutor.DatabaseFailureEvent);
            Assert.Equal(true, root.Fields["CommitAttempted"]); Assert.Equal(false, root.Fields["CommitConfirmed"]);
        }
        finally { factory.TransactionFaults.Reset(); }
        // The database DID commit, but that request must not claim it received an acknowledgement.
        await AssertCounts(course.Id, 1, 1, 1);
        using var replay = await client.PostAsJsonAsync("/payments/webhook", callback);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await AssertCounts(course.Id, 1, 1, 1);
    }

    [Fact]
    public async Task LostOrderCommitAcknowledgement_RepeatedPurchaseReusesPersistedOrder()
    {
        var (student, course) = await Setup(); ResetTransactionFaults();
        factory.TransactionFaults.CommitBeforeFailure = true;
        factory.TransactionFaults.CommitFailure = new InjectedCheckoutDatabaseException();
        try
        {
            using var scope = factory.Services.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<ICheckoutService>().CreateOrderAsync(student.Id, course.Id);
            Assert.Equal(CheckoutCode.IntegrationError, result.Code);
            Assert.Null(result.Value);
            Assert.Equal(1, factory.TransactionFaults.Begins); Assert.Equal(1, factory.TransactionFaults.Commits);
        }
        finally { factory.TransactionFaults.Reset(); }
        await AssertCounts(course.Id, 1, 0, 0);
        await Create(student, course);
        await AssertCounts(course.Id, 1, 0, 0);
    }

    [Fact]
    public async Task DisposeFailure_AfterConfirmedCommit_DoesNotUndoConfirmedSuccess()
    {
        var (student, course) = await Setup(); var order = await Create(student, course); ResetTransactionFaults();
        factory.TransactionFaults.DisposeFailure = new InvalidOperationException("dispose failure");
        try
        {
            Assert.Equal(CheckoutCode.Allowed, await Process(Event(order)));
            Assert.DoesNotContain(factory.Logs.Entries, e => e.EventId == CheckoutTransactionExecutor.DatabaseFailureEvent);
            var cleanup = Assert.Single(factory.Logs.Entries, e => e.EventId == CheckoutTransactionExecutor.CleanupFailureEvent);
            Assert.Equal("dispose", cleanup.Fields["Phase"]); Assert.Equal(true, cleanup.Fields["CommitConfirmed"]);
        }
        finally { factory.TransactionFaults.Reset(); }
        await AssertCounts(course.Id, 1, 1, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BeginCancellation_IsNotConvertedToIntegrationError(bool webhook)
    {
        var (student, course) = await Setup(); var order = webhook ? await Create(student, course) : null;
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var cancellation = new OperationCanceledException(cts.Token);
        ResetTransactionFaults(); factory.TransactionFaults.BeginFailure = cancellation;
        try
        {
            OperationCanceledException actual;
            if (webhook) actual = await Assert.ThrowsAsync<OperationCanceledException>(() => Process(Event(order!)));
            else
            {
                using var scope = factory.Services.CreateScope();
                actual = await Assert.ThrowsAsync<OperationCanceledException>(() => scope.ServiceProvider.GetRequiredService<ICheckoutService>()
                    .CreateOrderAsync(student.Id, course.Id));
            }
            Assert.Same(cancellation, actual);
            Assert.DoesNotContain(factory.Logs.Entries, e => e.EventId == CheckoutTransactionExecutor.DatabaseFailureEvent);
        }
        finally { factory.TransactionFaults.Reset(); }
        await AssertCounts(course.Id, webhook ? 1 : 0, 0, 0);
    }

    [Fact]
    public async Task Cancellation_DuringCommit_WithCleanupFailures_PropagatesOriginalCancellation()
    {
        var (student, course) = await Setup(); var order = await Create(student, course);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var cancellation = new OperationCanceledException(cts.Token);
        ResetTransactionFaults();
        factory.TransactionFaults.CommitFailure = cancellation;
        factory.TransactionFaults.RollbackFailure = new InvalidOperationException("rollback failure");
        factory.TransactionFaults.DisposeFailure = new InvalidOperationException("dispose failure");
        try
        {
            var actual = await Assert.ThrowsAsync<OperationCanceledException>(() => Process(Event(order)));
            Assert.Same(cancellation, actual);
            Assert.DoesNotContain(factory.Logs.Entries, e => e.EventId == CheckoutTransactionExecutor.DatabaseFailureEvent);
            Assert.Equal(2, factory.Logs.Entries.Count(e => e.EventId == CheckoutTransactionExecutor.CleanupFailureEvent));
        }
        finally { factory.TransactionFaults.Reset(); }
        await AssertCounts(course.Id, 1, 0, 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCanceledRequest_DoesNotStartTransaction(bool webhook)
    {
        var (student, course) = await Setup(); var order = webhook ? await Create(student, course) : null;
        using var cts = new CancellationTokenSource(); cts.Cancel(); ResetTransactionFaults();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
        try
        {
            if (webhook) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ProcessCallbackAsync(Event(order!), cts.Token));
            else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateOrderAsync(student.Id, course.Id, cts.Token));
            Assert.Equal(0, factory.TransactionFaults.Begins);
        }
        finally { factory.TransactionFaults.Reset(); }
        await AssertCounts(course.Id, webhook ? 1 : 0, 0, 0);
    }

    [Fact]
    public async Task CancellationAfterCommit_DoesNotClaimSuccess_AndReplayRemainsSafe()
    {
        var (student, course) = await Setup(); var order = await Create(student, course); var callback = Event(order);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var cancellation = new OperationCanceledException(cts.Token);
        ResetTransactionFaults();
        factory.TransactionFaults.CommitBeforeFailure = true;
        factory.TransactionFaults.CommitFailure = cancellation;
        try
        {
            Assert.Same(cancellation, await Assert.ThrowsAsync<OperationCanceledException>(() => Process(callback)));
            Assert.DoesNotContain(factory.Logs.Entries, e => e.EventId == CheckoutTransactionExecutor.DatabaseFailureEvent);
            Assert.Equal(1, factory.TransactionFaults.Commits);
        }
        finally { factory.TransactionFaults.Reset(); }
        await AssertCounts(course.Id, 1, 1, 1);
        Assert.Equal(CheckoutCode.Allowed, await Process(callback));
        await AssertCounts(course.Id, 1, 1, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BeginFailureAfterAllocation_CleansUpAllocatedEfTransaction(bool webhook)
    {
        var (student, course) = await Setup(); var order = webhook ? await Create(student, course) : null;
        ResetTransactionFaults();
        factory.TransactionFaults.BeginFailureAfterAllocation = true;
        factory.TransactionFaults.BeginFailure = new InjectedCheckoutDatabaseException();
        try
        {
            using var scope = factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
            if (webhook) Assert.Equal(CheckoutCode.IntegrationError, await service.ProcessCallbackAsync(Event(order!)));
            else Assert.Equal(CheckoutCode.IntegrationError, (await service.CreateOrderAsync(student.Id, course.Id)).Code);
            Assert.Null(scope.ServiceProvider.GetRequiredService<EnglishLearningPlatform.Infrastructure.Persistence.AppDbContext>().Database.CurrentTransaction);
            AssertOriginalFailure("begin", nameof(InjectedCheckoutDatabaseException));
        }
        finally { factory.TransactionFaults.Reset(); }
        await AssertCounts(course.Id, webhook ? 1 : 0, 0, 0);
    }

    [Fact]
    public async Task WrappedCancellation_IsPropagatedAsCancellation()
    {
        var (student, course) = await Setup(); var order = await Create(student, course);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var cancellation = new OperationCanceledException(cts.Token);
        ResetTransactionFaults();
        factory.TransactionFaults.BeginFailure = new DbUpdateException(InjectedCheckoutDatabaseException.SensitiveMarker, cancellation);
        try
        {
            Assert.Same(cancellation, await Assert.ThrowsAsync<OperationCanceledException>(() => Process(Event(order))));
            Assert.DoesNotContain(factory.Logs.Entries, e => e.EventId == CheckoutTransactionExecutor.DatabaseFailureEvent);
            Assert.All(factory.Logs.Entries, e => Assert.DoesNotContain(InjectedCheckoutDatabaseException.SensitiveMarker, e.Text));
        }
        finally { factory.TransactionFaults.Reset(); }
        await AssertCounts(course.Id, 1, 0, 0);
    }
}
