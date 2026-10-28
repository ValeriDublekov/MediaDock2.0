using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MediaDock.Api.Catalog;
using MediaDock.Api.BackgroundJobs;
using MediaDock.Api.Common;
using MediaDock.Api.GoldenGlobes;
using MediaDock.Application.Ingestion;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class BackgroundJobExecutionTests
{
    [Fact]
    public async Task SchedulerQueuesCatalogEnrichmentAlongsideTheDailyScanWhenTitlesRemain()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_imdb_catalog_schedule_test").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        var now = new DateTimeOffset(2026, 10, 23, 20, 0, 0, TimeSpan.Zero);
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();
        db.ImdbTitles.Add(new Infrastructure.Persistence.Entities.ImdbTitle
        {
            ImdbId = "tt1111111",
            MetadataStatus = "pending",
            UpdatedAt = now
        });
        db.BackgroundSchedulerStates.Add(new Infrastructure.Persistence.Entities.BackgroundSchedulerState
        {
            ScheduleName = "rss_ingestion",
            LastEvaluatedSlotUtc = now.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var scheduler = new BackgroundJobScheduler(db, new FixedTimeProvider(now));
        await scheduler.EnqueueDueSlotAsync(CancellationToken.None);

        var jobs = await db.BackgroundJobs.AsNoTracking().ToListAsync();
        Assert.Contains(jobs, job => job.JobType == "rss_scan" && job.Trigger == "schedule");
        Assert.Contains(jobs, job =>
            job.JobType == "imdb_catalog_enrichment"
            && job.Trigger == "schedule"
            && job.Status == "queued");
    }

    [Fact]
    public async Task ApiHostedDispatcherCompletesManualScanAndAssociatesScanRun()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_background_scan_execution_test").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            await ConfigureSettingsAsync(db, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        using var factory = new BackgroundJobsApiFactory(connectionString);
        using var client = factory.CreateClient();
        using var acceptedResponse = await client.PostAsync("/api/background-jobs/scans", content: null);
        Assert.Equal(HttpStatusCode.Accepted, acceptedResponse.StatusCode);
        var accepted = await acceptedResponse.Content.ReadFromJsonAsync<BackgroundJobAcceptedResponse>();
        Assert.NotNull(accepted);

        BackgroundJobResponse? completed = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            completed = await client.GetFromJsonAsync<BackgroundJobResponse>($"/api/background-jobs/{accepted.Id}");
            if (completed?.Status is "succeeded" or "partial" or "failed")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.NotNull(completed);
        Assert.Equal("succeeded", completed.Status);
        Assert.NotNull(completed.ScanRunId);
        await using var verificationDb = new MediaDockDbContext(options);
        var scanRun = await verificationDb.ScanRuns.SingleAsync(run => run.Id == completed.ScanRunId);
        Assert.Equal("succeeded", scanRun.Status);
        Assert.Equal("manual", scanRun.Trigger);
    }

    [Fact]
    public async Task ApiHostedDispatcherCachesScheduledRssFeedForCatalogPreview()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_scheduled_rss_preview_test").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var feedXml = "<rss version='2.0'><channel /></rss>";
        long jobId;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Sources.Add(new Source
            {
                StableKey = "scheduled-preview-movie",
                Name = "Movies",
                FeedType = "movie",
                Url = "https://feed.rutracker.cc/movies.atom",
                IsEnabled = true
            });
            await ConfigureSettingsAsync(db, DateTimeOffset.UtcNow);
            var job = new BackgroundJob
            {
                JobType = "rss_scan",
                Trigger = "schedule",
                Status = "queued",
                EnqueuedAt = DateTimeOffset.UtcNow,
                ScheduledSlotUtc = DateTimeOffset.UtcNow,
                CurrentStage = "queued"
            };
            db.BackgroundJobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        using var factory = new BackgroundJobsApiFactory(
            connectionString,
            rssFeedTransport: new StaticRssFeedTransport(Encoding.UTF8.GetBytes(feedXml)));
        using var client = factory.CreateClient();
        BackgroundJobResponse? completed = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            completed = await client.GetFromJsonAsync<BackgroundJobResponse>($"/api/background-jobs/{jobId}");
            if (completed?.Status is "succeeded" or "partial" or "failed")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.NotNull(completed);
        Assert.Equal("succeeded", completed.Status);
        using var previewResponse = await client.GetAsync("/api/catalog/rss/movie");
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<RssFeedPreviewResponse>();
        Assert.NotNull(preview);
        Assert.NotNull(preview.CachedAt);
        var feed = Assert.Single(preview.Feeds);
        Assert.Equal("https://feed.rutracker.cc/movies.atom", feed.SourceUrl);
        Assert.Equal(feedXml, feed.OriginalContent);
        Assert.Empty(feed.Items);
    }

    [Fact]
    public async Task FailedEntryRecheckJobReprocessesStoredParseLogPayload()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_failed_recheck_execution_test").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var processedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            var source = new Infrastructure.Persistence.Entities.Source
            {
                StableKey = "failed-recheck-source",
                Name = "Movies",
                FeedType = "movie",
                Url = "https://feed.rutracker.cc/success.atom"
            };
            db.Sources.Add(source);
            await ConfigureSettingsAsync(db, processedAt);
            await db.SaveChangesAsync();
            db.ParseLogs.Add(new Infrastructure.Persistence.Entities.ParseLog
            {
                SourceId = source.Id,
                SourceItemKey = "entry:stored-recheck-item",
                RawTitle = "The Matrix (1999) [1080p]",
                FeedName = source.Name,
                ParsedSuccessfully = false,
                OmdbStatus = "transport_error",
                Ignored = true,
                ErrorMessage = "transport_error",
                ProcessedAt = processedAt,
                RetryState = "retryable",
                AttemptCount = 1,
                FeedType = source.FeedType,
                SourcePublishedAt = processedAt,
                FeedEntryId = "stored-recheck-item",
                TorrentUrl = "https://rutracker.org/forum/viewtopic.php?t=191"
            });
            await db.SaveChangesAsync();
        }

        using var factory = new BackgroundJobsApiFactory(connectionString, new OmdbSuccessHandler());
        using var client = factory.CreateClient();
        using var acceptedResponse = await client.PostAsync("/api/background-jobs/recheck-failed", content: null);
        Assert.Equal(HttpStatusCode.Accepted, acceptedResponse.StatusCode);
        var accepted = await acceptedResponse.Content.ReadFromJsonAsync<BackgroundJobAcceptedResponse>();
        Assert.NotNull(accepted);

        BackgroundJobResponse? completed = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            completed = await client.GetFromJsonAsync<BackgroundJobResponse>($"/api/background-jobs/{accepted.Id}");
            if (completed?.Status is "succeeded" or "partial" or "failed")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.NotNull(completed);
    Assert.True(completed.Status == "succeeded", $"{completed.ErrorCode}: {completed.ResultSummary?.GetRawText()}");
        Assert.NotNull(completed.ScanRunId);
        await using var verificationDb = new MediaDockDbContext(options);
        var occurrence = await verificationDb.TorrentReleases.SingleAsync();
        Assert.Equal("entry:stored-recheck-item", occurrence.SourceItemKey);
        Assert.Equal("stored-recheck-item", occurrence.FeedEntryId);
        Assert.Equal("https://rutracker.org/forum/viewtopic.php?t=191", occurrence.TorrentUrl);
        Assert.Equal("resolved", (await verificationDb.ParseLogs.OrderByDescending(log => log.Id).FirstAsync()).RetryState);
        Assert.Equal("succeeded", (await verificationDb.ScanRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task ApiHostedDispatcherExecutesQueuedOscarImportAndCleansUploadBytes()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_background_job_execution_test").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        using var factory = new BackgroundJobsApiFactory(connectionString);
        using var client = factory.CreateClient();
        using var upload = new MultipartFormDataContent();
        var csv = new ByteArrayContent(Encoding.UTF8.GetBytes("""
            Ceremony,Year,Class,CanonicalCategory,Category,Film,FilmId,Name,Nominees,NomineeIds,Winner,Detail
            98,2025,Title,BEST PICTURE,BEST PICTURE,Queued Film,tt12345678,Producers,Producers,,True,
            """));
        csv.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        upload.Add(csv, "File", "queued-film.csv");
        upload.Add(new StringContent("1980"), "YearAfter");

        using var acceptedResponse = await client.PostAsync("/api/background-jobs/oscar-import", upload);
        Assert.Equal(HttpStatusCode.Accepted, acceptedResponse.StatusCode);
        var accepted = await acceptedResponse.Content.ReadFromJsonAsync<BackgroundJobAcceptedResponse>();
        Assert.NotNull(accepted);

        BackgroundJobResponse? completed = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            completed = await client.GetFromJsonAsync<BackgroundJobResponse>($"/api/background-jobs/{accepted.Id}");
            if (completed?.Status is "succeeded" or "partial" or "failed")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.NotNull(completed);
        Assert.Equal("succeeded", completed.Status);
        Assert.Equal("queued-film.csv", completed.InputFileName);

        await using var verificationDb = new MediaDockDbContext(options);
        Assert.Equal(1, await verificationDb.OscarMovies.CountAsync());
        var storedJob = await verificationDb.BackgroundJobs.SingleAsync(job => job.Id == accepted.Id);
        Assert.Null(storedJob.InputBytes);
    }

    [Fact]
    public async Task ApiHostedDispatcherExecutesQueuedGoldenGlobeImportAndCleansUploadBytes()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_job_execution_test").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        using var factory = new BackgroundJobsApiFactory(connectionString);
        using var client = factory.CreateClient();
        using var upload = new MultipartFormDataContent();
        var csv = new ByteArrayContent(Encoding.UTF8.GetBytes(
            "nominee_type,year,winner,award,title\nfilm,2025,true,Best Picture,Queued Golden Globe Film\n"));
        csv.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        upload.Add(csv, "File", "golden-globes.csv");
        upload.Add(new StringContent("1980"), "YearAfter");

        using var acceptedResponse = await client.PostAsync("/api/background-jobs/golden-globe-import", upload);
        Assert.Equal(HttpStatusCode.Accepted, acceptedResponse.StatusCode);
        var accepted = await acceptedResponse.Content.ReadFromJsonAsync<BackgroundJobAcceptedResponse>();
        Assert.NotNull(accepted);

        BackgroundJobResponse? completed = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            completed = await client.GetFromJsonAsync<BackgroundJobResponse>($"/api/background-jobs/{accepted.Id}");
            if (completed?.Status is "succeeded" or "partial" or "failed")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.NotNull(completed);
        Assert.Equal("succeeded", completed.Status);
        Assert.Equal("golden-globes.csv", completed.InputFileName);

        await using var verificationDb = new MediaDockDbContext(options);
        var nomination = await verificationDb.GoldenGlobeNominations
            .Include(row => row.Award)
            .Include(row => row.GoldenGlobeTitle)
            .SingleAsync();
        Assert.Equal("Queued Golden Globe Film", nomination.GoldenGlobeTitle!.Title);
        Assert.Equal("Best Picture", nomination.Award.Name);
        var storedJob = await verificationDb.BackgroundJobs.SingleAsync(job => job.Id == accepted.Id);
        Assert.Null(storedJob.InputBytes);
    }

    [Fact]
    public async Task ApiHostedDispatcherImportsAndEnrichesAnImdbTsvDataset()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_imdb_title_import_execution_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            await ConfigureSettingsAsync(db, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var handler = new PersonalRatingsOmdbHandler();
        using var factory = new BackgroundJobsApiFactory(connectionString, handler);
        using var client = factory.CreateClient();
        using var upload = new MultipartFormDataContent();
        var tsv = string.Join(
            '\n',
            "tconst\taverageRating\tnumVotes\ttitleType\tprimaryTitle\tstartYear\tgenres",
            "tt1111111\t7.1\t60001\tmovie\tImported Film\t1999\tDrama");
        var tsvContent = new ByteArrayContent(Encoding.UTF8.GetBytes(tsv));
        tsvContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/tab-separated-values");
        upload.Add(tsvContent, "File", "title-ratings.tsv");

        using var acceptedResponse = await client.PostAsync("/api/background-jobs/imdb-title-import", upload);
        Assert.Equal(HttpStatusCode.Accepted, acceptedResponse.StatusCode);
        var accepted = await acceptedResponse.Content.ReadFromJsonAsync<BackgroundJobAcceptedResponse>();
        Assert.NotNull(accepted);

        BackgroundJobResponse? completedImport = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            completedImport = await client.GetFromJsonAsync<BackgroundJobResponse>(
                $"/api/background-jobs/{accepted.Id}");
            if (completedImport?.Status is "succeeded" or "partial" or "failed")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.NotNull(completedImport);
        Assert.Equal("succeeded", completedImport.Status);
        Assert.Equal("title-ratings.tsv", completedImport.InputFileName);
        Assert.True(completedImport.ResultSummary.HasValue);
        var importSummary = completedImport.ResultSummary.Value.GetProperty("import");
        Assert.Equal(1, importSummary.GetProperty("RowsInserted").GetInt32());
        var enrichmentJobId = completedImport.ResultSummary.Value.GetProperty("enrichmentJobId").GetInt64();

        BackgroundJobResponse? completedEnrichment = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            completedEnrichment = await client.GetFromJsonAsync<BackgroundJobResponse>(
                $"/api/background-jobs/{enrichmentJobId}");
            if (completedEnrichment?.Status is "succeeded" or "partial" or "failed")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.NotNull(completedEnrichment);
        Assert.Equal("succeeded", completedEnrichment.Status);
        Assert.Single(handler.RequestUris);
        Assert.Contains("&i=tt1111111", handler.RequestUris[0].Query);

        await using var verificationDb = new MediaDockDbContext(options);
        var title = await verificationDb.ImdbTitles.AsNoTracking()
            .SingleAsync(item => item.ImdbId == "tt1111111");
        Assert.Equal("enriched", title.MetadataStatus);
        Assert.Equal("Dispatcher Enriched Film", title.TitleText);
        Assert.Equal(8.7m, title.ImdbRating);
        Assert.Empty(await verificationDb.PersonalRatings.ToListAsync());
        Assert.Equal(1, (await verificationDb.OmdbDailyUsage.AsNoTracking().SingleAsync()).TotalRequests);
        var storedImport = await verificationDb.BackgroundJobs.AsNoTracking()
            .SingleAsync(job => job.Id == accepted.Id);
        Assert.Null(storedImport.InputBytes);
    }

    [Fact]
    public async Task ApiHostedDispatcherEnrichesOnlyTheQueuedOwnersRatedImdbTitles()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_personal_ratings_enrichment_execution_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        long ownerUserId;
        long otherUserId;
        var enqueuedAt = DateTimeOffset.UtcNow;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var owner = new Infrastructure.Persistence.Entities.User
            {
                NormalizedEmail = "enrichment-owner@example.com",
                GivenName = "Ratings",
                FamilyName = "Owner",
                Status = "active",
                Role = "user",
                CreatedAt = enqueuedAt,
                UpdatedAt = enqueuedAt
            };
            var otherUser = new Infrastructure.Persistence.Entities.User
            {
                NormalizedEmail = "other-enrichment-owner@example.com",
                GivenName = "Other",
                FamilyName = "Owner",
                Status = "active",
                Role = "user",
                CreatedAt = enqueuedAt,
                UpdatedAt = enqueuedAt
            };
            db.Users.AddRange(owner, otherUser);
            await db.SaveChangesAsync();
            ownerUserId = owner.Id;
            otherUserId = otherUser.Id;

            await ConfigureSettingsAsync(db, enqueuedAt);
            db.ImdbTitles.AddRange(
                new Infrastructure.Persistence.Entities.ImdbTitle
                {
                    ImdbId = "tt1111111",
                    MetadataStatus = "pending",
                    UpdatedAt = enqueuedAt
                },
                new Infrastructure.Persistence.Entities.ImdbTitle
                {
                    ImdbId = "tt2222222",
                    MetadataStatus = "pending",
                    UpdatedAt = enqueuedAt
                });
            db.PersonalRatings.Add(new Infrastructure.Persistence.Entities.PersonalRating
            {
                UserId = ownerUserId,
                ImdbId = "tt1111111",
                Rating = 8,
                UpdatedAt = enqueuedAt
            });
            db.PersonalRatings.Add(new Infrastructure.Persistence.Entities.PersonalRating
            {
                UserId = otherUserId,
                ImdbId = "tt2222222",
                Rating = 6,
                UpdatedAt = enqueuedAt
            });
            var job = new Infrastructure.Persistence.Entities.BackgroundJob
            {
                JobType = "imdb_title_enrichment",
                Trigger = "manual",
                Status = "queued",
                EnqueuedAt = enqueuedAt,
                OwnerUserId = ownerUserId,
                CurrentStage = "queued"
            };
            job.Events.Add(new Infrastructure.Persistence.Entities.BackgroundJobEvent
            {
                OccurredAt = enqueuedAt,
                Level = "information",
                EventCode = "job_queued",
                Message = "IMDb metadata enrichment queued."
            });
            db.BackgroundJobs.Add(job);
            await db.SaveChangesAsync();
        }

        var handler = new PersonalRatingsOmdbHandler();
        using var factory = new BackgroundJobsApiFactory(connectionString, handler);
        using var client = factory.CreateClient();
        BackgroundJobResponse? completed = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            completed = await client.GetFromJsonAsync<BackgroundJobResponse>("/api/background-jobs/1");
            if (completed?.Status is "succeeded" or "partial" or "failed")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.NotNull(completed);
        Assert.Equal("succeeded", completed.Status);
        Assert.Null(completed.ResultSummary);
        Assert.Single(handler.RequestUris);
        Assert.Contains("&i=tt1111111", handler.RequestUris[0].Query);
        Assert.DoesNotContain("&t=", handler.RequestUris[0].Query);
        Assert.DoesNotContain("&y=", handler.RequestUris[0].Query);

        await using var verificationDb = new MediaDockDbContext(options);
        var title = await verificationDb.ImdbTitles.AsNoTracking()
            .SingleAsync(item => item.ImdbId == "tt1111111");
        Assert.Equal("enriched", title.MetadataStatus);
        Assert.Equal(1, title.EnrichmentAttemptCount);
        Assert.Equal("Dispatcher Enriched Film", title.TitleText);
        Assert.Equal(8.7m, title.ImdbRating);
        Assert.Null(title.NextEnrichmentAttemptAt);
        Assert.Equal(8, (await verificationDb.PersonalRatings.AsNoTracking()
            .SingleAsync(rating => rating.UserId == ownerUserId)).Rating);
        var usage = await verificationDb.OmdbDailyUsage.AsNoTracking().SingleAsync();
        Assert.Equal(1, usage.TotalRequests);
        Assert.Equal(0, usage.OscarRequests);
        var cached = await verificationDb.MetadataCache.AsNoTracking().SingleAsync();
        Assert.Equal("tt1111111", cached.LookupIdentity);
        Assert.Equal("found", cached.Status);
        var unrelatedTitle = await verificationDb.ImdbTitles.AsNoTracking()
            .SingleAsync(item => item.ImdbId == "tt2222222");
        Assert.Equal("pending", unrelatedTitle.MetadataStatus);
        Assert.Equal(0, unrelatedTitle.EnrichmentAttemptCount);
        var events = await verificationDb.BackgroundJobEvents.AsNoTracking()
            .Where(jobEvent => jobEvent.JobId == 1)
            .ToListAsync();
        Assert.All(events, jobEvent => Assert.DoesNotContain("tt1111111", jobEvent.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApiHostedDispatcherRefreshesManualGoldenGlobeLinkByExactImdbId()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_manual_refresh_execution_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>().UseNpgsql(connectionString).Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            await ConfigureSettingsAsync(db, DateTimeOffset.UtcNow);
            var goldenGlobeTitle = new Infrastructure.Persistence.Entities.GoldenGlobeTitle
            {
                Title = "Manual Refresh Film",
                Year = 2025,
                NomineeType = "movie"
            };
            db.GoldenGlobeNominations.Add(new Infrastructure.Persistence.Entities.GoldenGlobeNomination
            {
                ImportKey = "manual-refresh-film",
                GoldenGlobeTitle = goldenGlobeTitle,
                Award = new Infrastructure.Persistence.Entities.GoldenGlobeAward { Name = "Manual Refresh Award" }
            });
            await db.SaveChangesAsync();
        }

        var handler = new ManualGoldenGlobeOmdbHandler();
        using var factory = new BackgroundJobsApiFactory(connectionString, handler);
        using var client = factory.CreateClient();
        using var acceptedResponse = await client.PutAsJsonAsync("/api/golden-globes/imdb-link", new
        {
            filmId = "2025:movie:Manual Refresh Film",
            imdbId = "TT12345678"
        });
        Assert.Equal(HttpStatusCode.OK, acceptedResponse.StatusCode);
        var accepted = await acceptedResponse.Content.ReadFromJsonAsync<GoldenGlobeImdbLinkResponse>();
        Assert.NotNull(accepted?.RefreshJob);

        BackgroundJobResponse? completed = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            completed = await client.GetFromJsonAsync<BackgroundJobResponse>($"/api/background-jobs/{accepted.RefreshJob.Id}");
            if (completed?.Status is "succeeded" or "partial" or "failed") break;
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.NotNull(completed);
        Assert.Equal("succeeded", completed.Status);
        Assert.Contains("&i=tt12345678", Assert.Single(handler.RequestUris).Query);
        Assert.DoesNotContain("&t=", handler.RequestUris[0].Query);

        await using var verificationDb = new MediaDockDbContext(options);
        var title = await verificationDb.GoldenGlobeTitles.SingleAsync();
        Assert.Equal("tt12345678", title.ImdbId);
        Assert.True(title.IsImdbIdManual);
        Assert.Equal("enriched", title.EnrichmentStatus);
        var cached = await verificationDb.MetadataCache.SingleAsync();
        Assert.Equal("tt12345678", cached.LookupIdentity);
        Assert.Equal("movie", cached.SourceType);
        var usage = await verificationDb.OmdbDailyUsage.SingleAsync();
        Assert.Equal(1, usage.TotalRequests);
        Assert.Equal(0, usage.OscarRequests);

        var catalog = await client.GetFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>("/api/golden-globes?pageSize=100");
        var film = Assert.Single(catalog!.Items);
        Assert.True(film.IsImdbIdManual);
        Assert.Equal("tt12345678", film.ImdbId);
        Assert.Equal(8.7m, film.ImdbRating);
    }

    private static async Task ConfigureSettingsAsync(MediaDockDbContext db, DateTimeOffset updatedAt)
    {
        var settings = await db.Settings.SingleAsync();
        settings.OmdbApiKey = "synthetic-test-key";
        settings.OmdbDailyRequestLimit = 10;
        settings.UpdatedAt = updatedAt;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class BackgroundJobsApiFactory(
        string connectionString,
        HttpMessageHandler? messageHandler = null,
        IRssFeedTransport? rssFeedTransport = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MediaDock"] = connectionString
                }));
            builder.ConfigureTestServices(services =>
            {
                if (messageHandler is not null)
                {
                    services.RemoveAll<HttpClient>();
                    services.AddSingleton(new HttpClient(messageHandler) { Timeout = Timeout.InfiniteTimeSpan });
                }

                if (rssFeedTransport is not null)
                {
                    services.RemoveAll<IRssFeedTransport>();
                    services.AddScoped(_ => rssFeedTransport);
                }
            });
        }
    }

    private sealed class StaticRssFeedTransport(byte[] body) : IRssFeedTransport
    {
        public Task<byte[]> FetchAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult(body);
    }

    private sealed class OmdbSuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"Response":"True","Title":"The Matrix","Year":"1999","imdbID":"tt0133093","Type":"movie","imdbRating":"8.7","imdbVotes":"1000","Metascore":"73","Genre":"Action, Sci-Fi","Country":"USA","Director":"Example Director","Plot":"Example plot","Poster":"N/A","Runtime":"136 min","Awards":"None","BoxOffice":"$1"}
                    """, Encoding.UTF8, "application/json")
            });
    }

    private sealed class ManualGoldenGlobeOmdbHandler : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"Response":"True","Title":"Manual Refresh Film","Year":"2024","imdbID":"tt12345678","Type":"movie","imdbRating":"8.7","imdbVotes":"1000","Metascore":"73","Genre":"Drama","Country":"USA","Director":"Example Director","Plot":"Example plot","Poster":"https://example.test/manual.jpg","Runtime":"100 min","Awards":"None","BoxOffice":"$1"}
                    """, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class PersonalRatingsOmdbHandler : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"Response":"True","Title":"Dispatcher Enriched Film","Year":"2024","imdbID":"tt1111111","Type":"movie","imdbRating":"8.7","imdbVotes":"1000","Metascore":"73","Genre":"Drama","Country":"USA","Director":"Example Director","Plot":"Example plot","Poster":"https://example.test/enriched.jpg","Runtime":"100 min","Awards":"None","BoxOffice":"$1"}
                    """, Encoding.UTF8, "application/json")
            });
        }
    }
}