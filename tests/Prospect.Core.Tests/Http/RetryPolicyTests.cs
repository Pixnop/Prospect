using System.Net;

using Prospect.Core.Http;

using Shouldly;

namespace Prospect.Core.Tests.Http;

public sealed class RetryPolicyTests
{
    [Fact]
    public async Task ExecuteAsync_OperationSucceedsImmediately_RunsItOnce()
    {
        var attempts = 0;
        var policy = new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask);

        var result = await policy.ExecuteAsync(
            (_, _) =>
            {
                attempts++;

                return Task.FromResult("ok");
            },
            TransientHttpFailure.IsTransient,
            CancellationToken.None);

        result.ShouldBe("ok");
        attempts.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_TransientFailures_RetriesUpToTheConfiguredLimit()
    {
        var attempts = 0;
        var policy = new RetryPolicy(new RetryOptions(4, TimeSpan.Zero, 1d), (_, _) => Task.CompletedTask);

        var result = await policy.ExecuteAsync(
            (attempt, _) =>
            {
                attempts++;

                return attempt < 3 ? throw new HttpRequestException("panne") : Task.FromResult(attempt);
            },
            TransientHttpFailure.IsTransient,
            CancellationToken.None);

        result.ShouldBe(3);
        attempts.ShouldBe(4);
    }

    [Fact]
    public async Task ExecuteAsync_AttemptsExhausted_RethrowsTheLastFailure()
    {
        var policy = new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask);

        var exception = await Should.ThrowAsync<HttpRequestException>(() => policy.ExecuteAsync<int>(
            (_, _) => throw new HttpRequestException("toujours en panne"),
            TransientHttpFailure.IsTransient,
            CancellationToken.None));

        exception.Message.ShouldBe("toujours en panne");
    }

    [Fact]
    public async Task ExecuteAsync_NonTransientFailure_IsNotRetried()
    {
        var attempts = 0;
        var policy = new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask);

        await Should.ThrowAsync<InvalidOperationException>(() => policy.ExecuteAsync<int>(
            (_, _) =>
            {
                attempts++;

                throw new InvalidOperationException("bug applicatif");
            },
            TransientHttpFailure.IsTransient,
            CancellationToken.None));

        attempts.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_BackoffGrows_BetweenAttempts()
    {
        var waits = new List<TimeSpan>();
        var policy = new RetryPolicy(
            new RetryOptions(3, TimeSpan.FromSeconds(2), 3d),
            (delay, _) =>
            {
                waits.Add(delay);

                return Task.CompletedTask;
            });

        await Should.ThrowAsync<HttpRequestException>(() => policy.ExecuteAsync<int>(
            (_, _) => throw new HttpRequestException("panne"),
            TransientHttpFailure.IsTransient,
            CancellationToken.None));

        waits.ShouldBe([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6)]);
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyCanceled_DoesNotRunTheOperation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var policy = new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask);
        var attempts = 0;

        await Should.ThrowAsync<OperationCanceledException>(() => policy.ExecuteAsync(
            (_, _) =>
            {
                attempts++;

                return Task.FromResult(0);
            },
            TransientHttpFailure.IsTransient,
            cancellation.Token));

        attempts.ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutAnInjectedDelay_UsesTheRealOneWithoutHangingTheSuite()
    {
        var policy = new RetryPolicy(RetryOptions.NoDelay);
        var attempts = 0;

        var result = await policy.ExecuteAsync(
            (attempt, _) =>
            {
                attempts++;

                return attempt == 0 ? throw new HttpRequestException("panne") : Task.FromResult(attempt);
            },
            TransientHttpFailure.IsTransient,
            CancellationToken.None);

        result.ShouldBe(1);
        attempts.ShouldBe(2);
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() => new RetryPolicy(null!));

    [Fact]
    public async Task ExecuteAsync_NullArguments_ThrowArgumentNullException()
    {
        var policy = new RetryPolicy(RetryOptions.NoDelay);

        await Should.ThrowAsync<ArgumentNullException>(() => policy.ExecuteAsync<int>(null!, _ => true, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => policy.ExecuteAsync((_, _) => Task.FromResult(0), null!, CancellationToken.None));
    }

    [Fact]
    public void DelayBeforeAttempt_FirstAttempt_DoesNotWait()
        => RetryOptions.Default.DelayBeforeAttempt(0).ShouldBe(TimeSpan.Zero);

    [Fact]
    public void DelayBeforeAttempt_Default_DoublesEachTime()
    {
        RetryOptions.Default.DelayBeforeAttempt(1).ShouldBe(TimeSpan.FromSeconds(1));
        RetryOptions.Default.DelayBeforeAttempt(2).ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public void IsTransientStatus_ClassifiesServerSideHiccupsOnly(HttpStatusCode? statusCode, bool expected)
        => TransientHttpFailure.IsTransientStatus(statusCode).ShouldBe(expected);

    [Fact]
    public void IsTransient_ClassifiesExceptionsByKind()
    {
        TransientHttpFailure.IsTransient(new HttpRequestException("panne")).ShouldBeTrue();
        TransientHttpFailure.IsTransient(new IOException("coupure")).ShouldBeTrue();
        TransientHttpFailure.IsTransient(new TimeoutException("inactivité")).ShouldBeTrue();
        TransientHttpFailure.IsTransient(new OperationCanceledException()).ShouldBeFalse();
        TransientHttpFailure.IsTransient(new InvalidOperationException()).ShouldBeFalse();
    }

    [Fact]
    public void IsTransient_Null_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() => TransientHttpFailure.IsTransient(null!));
}