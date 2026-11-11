using System.IO.Compression;
using System.Net;
using System.Text;
using MediaDock.Infrastructure.Metadata;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Persistence")]
public sealed class ImdbRatingRefreshTests
{
    [Fact]
    public async Task RefreshFromSnapshotAsyncUpdatesOnlyEligibleRatingsAndRejectsInvalidSnapshotAtomically()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_imdb_rating_refresh_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var dbContext = new MediaDockDbContext(options);
        await dbContext.Database.MigrateAsync();
        dbContext.ImdbTitles.AddRange(
            CreateTitle("tt0000001", 2026, 5.0m, 100, "Current year title"),
            CreateTitle("tt0000002", 2025, 6.0m, 200, "Previous year title"),
            CreateTitle("tt0000004", 2026, 7.0m, 300, "Unchanged title"),
            CreateTitle("tt0000005", 2025, 4.0m, 400, "Absent title"),
            CreateTitle("tt0000006", 2024, 3.0m, 500, "Out of window title"));
        var user = new User
        {
            NormalizedEmail = "refresh@example.test", GivenName = "Test", FamilyName = "User",
            Status = "active", Role = "user", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(user);
        dbContext.PersonalRatings.Add(new PersonalRating
        {
            User = user, ImdbId = "tt0000001", Rating = 9, DateRated = new DateOnly(2026, 1, 1),
            IsManuallyAdded = true, UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        await dbContext.GeneralRecommendationSettings.Where(settings => settings.Id == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.RequiresVoteRecalculation, false));

        var service = new ImdbRatingRefreshService(
            dbContext,
            new HttpClient(),
            new ImdbRatingSnapshotParser(),
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero)));
        using var snapshot = Compress(
            "tconst\taverageRating\tnumVotes\n"
            + "tt0000001\t8.1\t600\n"
            + "tt0000002\t7.2\t700\n"
            + "tt0000003\t6.0\t50\n"
            + "tt0000004\t7.0\t300\n");

        var summary = await service.RefreshFromSnapshotAsync(snapshot);

        Assert.Equal(2026, summary.TargetYear);
        Assert.Equal(4, summary.SelectedTitles);
        Assert.Equal(4, summary.RowsScanned);
        Assert.Equal(3, summary.MatchedRows);
        Assert.Equal(2, summary.UpdatedRows);
        Assert.Equal(1, summary.UnchangedRows);
        Assert.Equal(1, summary.AbsentFromSnapshot);
        Assert.Equal(0, summary.ConcurrentConflicts);
        Assert.Contains("2 titles updated", summary.Message);
        Assert.Contains("1 selected title absent", summary.Message);

        var savedTitles = await dbContext.ImdbTitles.AsNoTracking()
            .ToDictionaryAsync(title => title.ImdbId);
        Assert.Equal(8.1m, savedTitles["tt0000001"].ImdbRating);
        Assert.Equal(600, savedTitles["tt0000001"].ImdbVotes);
        Assert.Equal("Current year title", savedTitles["tt0000001"].TitleText);
        Assert.Equal("enriched", savedTitles["tt0000001"].MetadataStatus);
        Assert.Equal("movie", savedTitles["tt0000001"].MediaType);
        var personalRating = await dbContext.PersonalRatings.AsNoTracking().SingleAsync();
        Assert.Equal("tt0000001", personalRating.ImdbId);
        Assert.Equal(9, personalRating.Rating);
        Assert.True(personalRating.IsManuallyAdded);
        Assert.Equal(3.0m, savedTitles["tt0000006"].ImdbRating);
        Assert.Equal(500, savedTitles["tt0000006"].ImdbVotes);
        Assert.True(await dbContext.GeneralRecommendationSettings.AsNoTracking()
            .Where(settings => settings.Id == 1)
            .Select(settings => settings.RequiresVoteRecalculation)
            .SingleAsync());

        using var invalidSnapshot = Compress(
            "tconst\taverageRating\tnumVotes\n"
            + "tt0000001\t9.9\t999\n"
            + "tt0000002\tmalformed\t700\n");
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => service.RefreshFromSnapshotAsync(invalidSnapshot));

        Assert.Contains("row 3 has an invalid averageRating", exception.Message);
        var unchangedAfterFailure = await dbContext.ImdbTitles.AsNoTracking()
            .SingleAsync(title => title.ImdbId == "tt0000001");
        Assert.Equal(8.1m, unchangedAfterFailure.ImdbRating);
        Assert.Equal(600, unchangedAfterFailure.ImdbVotes);

        using var validSnapshot = Compress("tconst\taverageRating\tnumVotes\ntt0000001\t9.9\t999\n");
        var truncatedBytes = validSnapshot.ToArray();
        using var truncatedSnapshot = new MemoryStream(truncatedBytes, 0, truncatedBytes.Length - 8);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RefreshFromSnapshotAsync(truncatedSnapshot));
        var unchangedAfterTruncation = await dbContext.ImdbTitles.AsNoTracking()
            .SingleAsync(title => title.ImdbId == "tt0000001");
        Assert.Equal(8.1m, unchangedAfterTruncation.ImdbRating);
        Assert.Equal(600, unchangedAfterTruncation.ImdbVotes);
    }

    [Fact]
    public async Task RefreshFromSnapshotAsyncCountsConcurrentChangesAndExcludesNullYears()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_imdb_refresh_conflicts").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();
        db.ImdbTitles.AddRange(
            CreateTitle("tt0000001", 2026, 5m, 100, "Changed"),
            CreateTitle("tt0000002", 2026, 6m, 200, "Unchanged"),
            CreateTitle("tt0000003", 2025, 7m, 300, "Year changed"),
            new ImdbTitle { ImdbId = "tt0000004", Year = null, ImdbRating = 2m, ImdbVotes = 10, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        using var source = Compress("tconst\taverageRating\tnumVotes\n"
            + "tt0000001\t8.1\t600\n"
            + "tt0000002\t6.0\t200\n"
            + "tt0000003\t8.0\t400\n"
            + "tt0000004\t9.0\t500\n");
        using var snapshot = new BeforeReadStream(source.ToArray(), () =>
        {
            using var other = new MediaDockDbContext(options);
            other.Database.ExecuteSqlRaw("UPDATE imdb_titles SET imdb_votes = 999 WHERE imdb_id IN ('tt0000001', 'tt0000002')");
            other.Database.ExecuteSqlRaw("UPDATE imdb_titles SET year = 2024 WHERE imdb_id = 'tt0000003'");
        });
        var service = new ImdbRatingRefreshService(db, new HttpClient(), new ImdbRatingSnapshotParser(),
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.RefreshFromSnapshotAsync(snapshot);

        Assert.Equal(3, result.SelectedTitles);
        Assert.Equal(3, result.MatchedRows);
        Assert.Equal(3, result.ConcurrentConflicts);
        Assert.Equal(0, result.UnchangedRows);
        Assert.Equal(0, result.UpdatedRows);
        var titles = await db.ImdbTitles.AsNoTracking().ToDictionaryAsync(title => title.ImdbId);
        Assert.Equal(999, titles["tt0000001"].ImdbVotes);
        Assert.Equal(999, titles["tt0000002"].ImdbVotes);
        Assert.Equal(2024, titles["tt0000003"].Year);
        Assert.Equal(2m, titles["tt0000004"].ImdbRating);
    }

    [Fact]
    public async Task RefreshFromSnapshotAsyncRollsBackEarlierBatchesWhenLaterBatchFails()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_imdb_refresh_rollback").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();
        db.ImdbTitles.AddRange(Enumerable.Range(1, 501)
            .Select(number => CreateTitle($"tt{number:D7}", 2026, 5m, 100, "Title")));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_last_refresh() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.imdb_id = 'tt0000501' THEN RAISE EXCEPTION 'test batch failure'; END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER reject_last_refresh BEFORE UPDATE ON imdb_titles
                FOR EACH ROW EXECUTE FUNCTION reject_last_refresh();
            """);
        using var snapshot = Compress("tconst\taverageRating\tnumVotes\n"
            + string.Concat(Enumerable.Range(1, 501).Select(number => $"tt{number:D7}\t8.0\t200\n")));
        var service = new ImdbRatingRefreshService(db, new HttpClient(), new ImdbRatingSnapshotParser(),
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero)));

        var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => service.RefreshFromSnapshotAsync(snapshot));
        Assert.Contains("test batch failure", exception.MessageText);

        Assert.Equal(501, await db.ImdbTitles.AsNoTracking().CountAsync(title => title.ImdbRating == 5m && title.ImdbVotes == 100));
    }

    [Fact]
    public async Task RefreshAsyncRetriesInterruptedBodyAndKeepsYearFromRunStart()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_imdb_refresh_retry").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();
        db.ImdbTitles.Add(CreateTitle("tt0000001", 2025, 5m, 100, "Title"));
        await db.SaveChangesAsync();
        using var snapshot = Compress("tconst\taverageRating\tnumVotes\ntt0000001\t8.0\t200\n");
        var handler = new RetryBodyHandler(snapshot.ToArray());
        using var client = new HttpClient(handler);
        var service = new ImdbRatingRefreshService(db, client, new ImdbRatingSnapshotParser(), new AdvancingTimeProvider());

        var result = await service.RefreshAsync();

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(2026, result.TargetYear);
        Assert.Equal(1, result.UpdatedRows);
        Assert.True(result.ProcessingMilliseconds >= 1000);
        Assert.True(result.TotalMilliseconds >= result.ProcessingMilliseconds);
    }

    private sealed class RetryBodyHandler(byte[] bytes) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Stream body = RequestCount == 1 ? new FailingReadStream(bytes) : new MemoryStream(bytes);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
        }
    }

    private sealed class FailingReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("connection reset");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("connection reset"));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException<int>(new IOException("connection reset"));
    }

    private sealed class AdvancingTimeProvider : TimeProvider
    {
        private int _calls;
        public override DateTimeOffset GetUtcNow() => Interlocked.Increment(ref _calls) == 1
            ? new DateTimeOffset(2026, 12, 31, 21, 59, 0, TimeSpan.Zero)
            : new DateTimeOffset(2026, 12, 31, 22, 1, 0, TimeSpan.Zero);
    }

    private sealed class BeforeReadStream(byte[] bytes, Action beforeRead) : MemoryStream(bytes)
    {
        private bool _started;

        private void Start()
        {
            if (_started) return;
            _started = true;
            beforeRead();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Start();
            return base.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Start();
            return base.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Start();
            return base.ReadAsync(buffer, offset, count, cancellationToken);
        }
    }

    private static ImdbTitle CreateTitle(string imdbId, int year, decimal rating, long votes, string title) => new()
    {
        ImdbId = imdbId,
        TitleText = title,
        Year = year,
        MediaType = "movie",
        ImdbRating = rating,
        ImdbVotes = votes,
        MetadataStatus = "enriched",
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static MemoryStream Compress(string contents)
    {
        var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(false), 1024, leaveOpen: true))
        {
            writer.Write(contents);
        }

        stream.Position = 0;
        return stream;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}