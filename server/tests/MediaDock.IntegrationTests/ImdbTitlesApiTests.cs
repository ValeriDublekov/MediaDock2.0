using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Catalog;
using MediaDock.Api.Authentication;
using MediaDock.Api.Common;
using MediaDock.Api.ImdbTitles;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using static MediaDock.IntegrationTests.GoogleAuthenticationApiTestSupport;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class ImdbTitlesApiTests
{
    [Fact]
    public async Task ListIncludesAllCanonicalTitlesFiltersAndProjectsOnlyTheActiveUsersState()
    {
        const string ownerSubject = "imdb-titles-owner";
        const string ownerEmail = "imdb-titles-owner@example.com";
        const string otherSubject = "imdb-titles-other";
        const string otherEmail = "imdb-titles-other@example.com";
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_imdb_titles_api_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var owner = CreateUser(ownerEmail, ownerSubject, now);
            var other = CreateUser(otherEmail, otherSubject, now);
            db.Users.AddRange(owner, other);
            var usaTitle = CreateTitle("tt9300001", "Zeta Game", 2026, "game", 8.5m, 200, "enriched", now, "Action");
            usaTitle.Countries = ["USA"];
            db.ImdbTitles.AddRange(
                usaTitle,
                CreateTitle("tt9300002", "Alpha Episode", 2026, "episode", null, null, "pending", now, "Drama"),
                CreateTitle("tt9300003", "Beta Movie", 2025, "movie", 7.2m, 900, "enriched", now, "Drama"),
                CreateTitle("tt9300004", "Untyped Archive", null, null, 9.0m, 1000, "not_found", now, "Classics"),
                CreateTitle("tt9300005", "Retryable Feature", 2023, "movie", null, null, "temporary_error", now, "Comedy"));
            await db.SaveChangesAsync();

            var dateRated = DateOnly.FromDateTime(now.UtcDateTime);
            db.ImdbFavoriteMovies.AddRange(
                new ImdbFavoriteMovie
                {
                    UserId = owner.Id,
                    ImdbId = "tt9300001",
                    ToWatch = true,
                    AddedFromCatalog = true,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new ImdbFavoriteMovie
                {
                    UserId = other.Id,
                    ImdbId = "tt9300003",
                    ToWatch = true,
                    AddedFromCatalog = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            db.UserIgnoredMovies.AddRange(
                new UserIgnoredMovie { UserId = owner.Id, ImdbId = "tt9300004", CreatedAt = now },
                new UserIgnoredMovie { UserId = other.Id, ImdbId = "tt9300001", CreatedAt = now });
            db.PersonalRatings.AddRange(
                new PersonalRating
                {
                    UserId = owner.Id,
                    ImdbId = "tt9300003",
                    Rating = 8,
                    DateRated = dateRated,
                    UpdatedAt = now
                },
                new PersonalRating
                {
                    UserId = other.Id,
                    ImdbId = "tt9300002",
                    Rating = 3,
                    DateRated = dateRated,
                    UpdatedAt = now
                });
            await db.SaveChangesAsync();
        }

        using var ownerFactory = new ApiFactory(
            connectionString,
            CreatePrincipal(email: ownerEmail, subject: ownerSubject),
            accessControlEnforced: true);
        using var ownerClient = ownerFactory.CreateClient();
        using var otherFactory = new ApiFactory(
            connectionString,
            CreatePrincipal(email: otherEmail, subject: otherSubject),
            accessControlEnforced: true);
        using var otherClient = otherFactory.CreateClient();

        var page = await ownerClient.GetFromJsonAsync<ImdbTitlesPageResponse>("/api/imdb-titles?pageSize=100");
        Assert.NotNull(page);
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(1, page.TotalPages);
        Assert.Equal(
            ["tt9300002", "tt9300001", "tt9300003", "tt9300005", "tt9300004"],
            page.Items.Select(item => item.ImdbId));
        Assert.Equal(new ImdbTitleYearBounds(2023, 2026), page.YearBounds);
        Assert.Equal(new ImdbTitleRatingBounds(7.2m, 9.0m), page.ImdbRatingBounds);
        Assert.Equal(["episode", "game", "movie"], page.MediaTypes);
        Assert.True(page.HasUntypedTitles);
        Assert.Equal(["Action", "Classics", "Comedy", "Drama"], page.Genres);
        Assert.Contains(page.Items, item => item.ImdbId == "tt9300001" && item.IsFavorite && item.ToWatch);
        Assert.Contains(page.Items, item => item.ImdbId == "tt9300003" && item.PersonalRating == 8);
        Assert.Contains(page.Items, item => item.ImdbId == "tt9300004" && item.IsIgnored);
        Assert.Contains(page.Items, item => item.ImdbId == "tt9300002" && item.MetadataStatus == "pending");
        Assert.Contains(page.Items, item => item.ImdbId == "tt9300005" && item.MetadataStatus == "temporary_error");

        var otherPage = await otherClient.GetFromJsonAsync<ImdbTitlesPageResponse>("/api/imdb-titles?pageSize=100");
        Assert.NotNull(otherPage);
        Assert.Contains(otherPage.Items, item => item.ImdbId == "tt9300002" && item.PersonalRating == 3);
        Assert.Contains(otherPage.Items, item => item.ImdbId == "tt9300001" && item.IsIgnored && !item.IsFavorite);
        Assert.Contains(otherPage.Items, item => item.ImdbId == "tt9300003" && item.IsFavorite && item.PersonalRating is null);

        Assert.Single((await GetPageAsync(ownerClient, "search=tt9300004")).Items);
        Assert.Equal(2, (await GetPageAsync(ownerClient, "search=2026")).TotalCount);
        Assert.Single((await GetPageAsync(ownerClient, "mediaTypeIsNull=true")).Items);
        Assert.Single((await GetPageAsync(ownerClient, "mediaType=episode&metadataStatus=pending")).Items);
        Assert.Single((await GetPageAsync(ownerClient, "metadataStatus=temporary_error")).Items);
        Assert.Equal(2, (await GetPageAsync(ownerClient, "genres=Drama")).TotalCount);
        Assert.Equal(0, (await GetPageAsync(ownerClient, "genreFilter=true")).TotalCount);
        Assert.Single((await GetPageAsync(ownerClient, "countries=USA&countryFilter=true")).Items);
        Assert.Equal(0, (await GetPageAsync(ownerClient, "countryFilter=true")).TotalCount);
        Assert.Single((await GetPageAsync(ownerClient, "yearFrom=2025&imdbRatingFrom=8")).Items);

        var firstStablePage = await ownerClient.GetFromJsonAsync<ImdbTitlesPageResponse>("/api/imdb-titles?page=1&pageSize=2");
        var secondStablePage = await ownerClient.GetFromJsonAsync<ImdbTitlesPageResponse>("/api/imdb-titles?page=2&pageSize=2");
        Assert.Equal(["tt9300002", "tt9300001"], firstStablePage!.Items.Select(item => item.ImdbId));
        Assert.Equal(["tt9300003", "tt9300005"], secondStablePage!.Items.Select(item => item.ImdbId));

        var ratingSort = await GetPageAsync(ownerClient, "sort=rating");
        Assert.Equal(
            ["tt9300004", "tt9300001", "tt9300003", "tt9300002", "tt9300005"],
            ratingSort.Items.Select(item => item.ImdbId));
        var voteSort = await GetPageAsync(ownerClient, "sort=votes");
        Assert.Equal(
            ["tt9300004", "tt9300003", "tt9300001", "tt9300002", "tt9300005"],
            voteSort.Items.Select(item => item.ImdbId));

        Assert.Equal(4, (await GetPageAsync(ownerClient, "showIgnored=false")).TotalCount);
        Assert.Equal(4, (await GetPageAsync(ownerClient, "showWatched=false")).TotalCount);
        Assert.Equal(3, (await GetPageAsync(ownerClient, "showIgnored=false&showWatched=false")).TotalCount);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ownerClient.GetAsync("/api/imdb-titles?yearFrom=2026&yearTo=2025")).StatusCode);
    }

    [Fact]
    public async Task DeleteUnlinksTorrentTitleAndRejectsOtherReferences()
    {
        await using var host = await CatalogApiTestHost.CreateAsync("mediadock_imdb_titles_delete_api_test");
        var now = DateTimeOffset.UtcNow;
        var linkedTorrentTitle = new TorrentMovie
        {
            TitleText = "Linked film",
            NormalizedTitle = "linked film",
            MediaType = "movie",
            ImdbId = "tt9300011",
            UpdatedAt = now
        };
        host.Db.ImdbTitles.AddRange(
            CreateTitle("tt9300011", "Linked film", 2024, "movie", null, null, "enriched", now),
            CreateTitle("tt9300012", "Awarded film", 2023, "movie", null, null, "enriched", now));
        host.Db.TorrentMovies.Add(linkedTorrentTitle);
        host.Db.OscarMovies.Add(new OscarMovie
        {
            StableKey = "awarded-film",
            FilmTitle = "Awarded film",
            NormalizedTitle = "awarded film",
            FilmYear = 2023,
            ImdbId = "tt9300012",
            ImportedAt = now,
            UpdatedAt = now
        });
        await host.Db.SaveChangesAsync();

        using var deletedResponse = await host.Client.DeleteAsync("/api/imdb-titles/tt9300011");
        Assert.Equal(HttpStatusCode.OK, deletedResponse.StatusCode);
        var deletedResult = await deletedResponse.Content.ReadFromJsonAsync<DeleteImdbTitleResponse>();
        Assert.Equal(new DeleteImdbTitleResponse(1), deletedResult);
        Assert.False(await host.Db.ImdbTitles.AnyAsync(title => title.ImdbId == "tt9300011"));
        Assert.Null((await host.Db.TorrentMovies.AsNoTracking().SingleAsync(movie => movie.Id == linkedTorrentTitle.Id)).ImdbId);

        using var conflictResponse = await host.Client.DeleteAsync("/api/imdb-titles/tt9300012");
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        Assert.True(await host.Db.ImdbTitles.AnyAsync(title => title.ImdbId == "tt9300012"));
        Assert.True(await host.Db.OscarMovies.AnyAsync(movie => movie.ImdbId == "tt9300012"));
    }

    private static async Task<ImdbTitlesPageResponse> GetPageAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<ImdbTitlesPageResponse>($"/api/imdb-titles?pageSize=100&{query}"))!;

    private static User CreateUser(string email, string subject, DateTimeOffset now)
    {
        var user = new User
        {
            NormalizedEmail = email,
            GivenName = "Test",
            FamilyName = "User",
            Status = "active",
            Role = "user",
            CreatedAt = now,
            UpdatedAt = now
        };
        user.ExternalIdentities.Add(new ExternalIdentity
        {
            User = user,
            Issuer = GoogleSignInSettings.GoogleIssuer,
            Subject = subject,
            CreatedAt = now
        });
        return user;
    }

    private static ImdbTitle CreateTitle(
        string imdbId,
        string title,
        int? year,
        string? mediaType,
        decimal? rating,
        long? votes,
        string status,
        DateTimeOffset now,
        params string[] genres) => new()
    {
        ImdbId = imdbId,
        TitleText = title,
        Year = year,
        MediaType = mediaType,
        ImdbRating = rating,
        ImdbVotes = votes,
        Genres = genres,
        MetadataStatus = status,
        UpdatedAt = now
    };
}