using System.Net;
using MediaDock.Infrastructure.Metadata;
using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.UnitTests;

public sealed class ImdbRatingRefreshDownloadTests
{
    [Fact]
    public async Task RefreshAsyncReportsNonRetryableHttpStatusWithoutRepeatedRequests()
    {
        var handler = new NotFoundHandler();
        using var httpClient = new HttpClient(handler);
        var options = new DbContextOptionsBuilder<MediaDockDbContext>().Options;
        await using var dbContext = new MediaDockDbContext(options);
        var service = new ImdbRatingRefreshService(
            dbContext,
            httpClient,
            new ImdbRatingSnapshotParser(),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => service.RefreshAsync());

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("HTTP 404 (Not Found)", exception.Message);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("https://datasets.imdbws.com/title.ratings.tsv.gz", handler.RequestUri?.ToString());
    }

    [Fact]
    public async Task RefreshAsyncStopsAfterThreeTransientResponses()
    {
        var handler = new TransientHandler();
        using var httpClient = new HttpClient(handler);
        await using var dbContext = new MediaDockDbContext(new DbContextOptionsBuilder<MediaDockDbContext>().Options);
        var service = new ImdbRatingRefreshService(dbContext, httpClient, new ImdbRatingSnapshotParser(), TimeProvider.System);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => service.RefreshAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task RefreshAsyncRespectsCallerCancellationDuringRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new TransientHandler(cancellation);
        using var httpClient = new HttpClient(handler);
        await using var dbContext = new MediaDockDbContext(new DbContextOptionsBuilder<MediaDockDbContext>().Options);
        var service = new ImdbRatingRefreshService(dbContext, httpClient, new ImdbRatingSnapshotParser(), TimeProvider.System);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RefreshAsync(cancellation.Token));
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class TransientHandler(CancellationTokenSource? cancellation = null) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            cancellation?.Cancel();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}