using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Authentication;
using MediaDock.Api.Common;
using MediaDock.Api.Favorites;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using static MediaDock.IntegrationTests.GoogleAuthenticationApiTestSupport;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class FavoritesApiTests
{

    [Fact]
    public async Task FavoritesAreScopedToCurrentActiveUserEvenWhenAccessControlIsOpen()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_favorites_owner_api_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        long firstUserId;
        long secondUserId;
        const string firstImdbId = "tt12345678";
        const string secondImdbId = "tt87654321";
        const string seriesImdbId = "tt99887766";
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;

            User CreateUser(string email, string subject, string status = "active", string role = "user")
            {
                var user = new User
                {
                    NormalizedEmail = email,
                    GivenName = "Test",
                    FamilyName = "User",
                    Status = status,
                    Role = role,
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

            var firstUser = CreateUser("first@example.com", "favorites-first");
            var secondUser = CreateUser("second@example.com", "favorites-second");
            var deactivatedUser = CreateUser("disabled@example.com", "favorites-disabled", "deactivated");
            var adminUser = CreateUser("admin@example.com", "favorites-admin", role: "admin");
            db.Users.AddRange(firstUser, secondUser, deactivatedUser, adminUser);
            db.ImdbTitles.AddRange(
                new ImdbTitle
                {
                    ImdbId = firstImdbId,
                    TitleText = "Shared Film",
                    Year = 2025,
                    MediaType = "movie",
                    ImdbRating = 9.1m,
                    ImdbVotes = 1200,
                    Metascore = 88m,
                    Genres = ["Drama", "Mystery"],
                    Countries = ["USA"],
                    Director = "Mira Director",
                    Plot = "A story about a changing city.",
                    Runtime = "110 min",
                    Awards = "1 win and 3 nominations.",
                    BoxOffice = "$12 million",
                    PosterUrl = "https://example.test/canonical-shared.jpg",
                    MetadataStatus = "enriched",
                    UpdatedAt = now
                },
                new ImdbTitle
                {
                    ImdbId = secondImdbId,
                    TitleText = "Second User Film",
                    Year = 2024,
                    MediaType = "movie",
                    ImdbRating = 6.3m,
                    PosterUrl = "https://example.test/canonical-second.jpg",
                    MetadataStatus = "enriched",
                    UpdatedAt = now
                },
                new ImdbTitle
                {
                    ImdbId = seriesImdbId,
                    TitleText = "Shared Series",
                    Year = 2025,
                    MediaType = "series",
                    ImdbRating = 8.4m,
                    MetadataStatus = "enriched",
                    UpdatedAt = now
                },
                new ImdbTitle { ImdbId = "tt11223344", MediaType = "movie", MetadataStatus = "pending", UpdatedAt = now },
                new ImdbTitle { ImdbId = "tt11223345", MediaType = "movie", MetadataStatus = "not_found", UpdatedAt = now },
                new ImdbTitle { ImdbId = "tt11223346", MediaType = "movie", MetadataStatus = "temporary_error", UpdatedAt = now });
            db.OscarMovies.Add(new OscarMovie
            {
                StableKey = "favorites-shared-film",
                FilmTitle = "Shared Film",
                NormalizedTitle = "shared film",
                FilmYear = 2025,
                ImdbId = firstImdbId,
                ImportedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
            firstUserId = firstUser.Id;
            secondUserId = secondUser.Id;
        }

        using var anonymousFactory = new ApiFactory(connectionString);
        using var anonymousClient = anonymousFactory.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync("/api/favorites");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var unknownFactory = new ApiFactory(connectionString, CreatePrincipal(subject: "favorites-unknown"));
        using var unknownClient = unknownFactory.CreateClient();
        using var unknownResponse = await unknownClient.GetAsync("/api/favorites");
        Assert.Equal(HttpStatusCode.Forbidden, unknownResponse.StatusCode);

        using var deactivatedFactory = new ApiFactory(
            connectionString,
            CreatePrincipal(email: "disabled@example.com", subject: "favorites-disabled"));
        using var deactivatedClient = deactivatedFactory.CreateClient();
        using var deactivatedResponse = await deactivatedClient.GetAsync("/api/favorites");
        Assert.Equal(HttpStatusCode.Forbidden, deactivatedResponse.StatusCode);

        using var firstFactory = new ApiFactory(
            connectionString,
            CreatePrincipal(email: "first@example.com", subject: "favorites-first"));
        using var firstClient = firstFactory.CreateClient();
        using var secondFactory = new ApiFactory(
            connectionString,
            CreatePrincipal(email: "second@example.com", subject: "favorites-second"));
        using var secondClient = secondFactory.CreateClient();
        using var adminFactory = new ApiFactory(
            connectionString,
            CreatePrincipal(email: "admin@example.com", subject: "favorites-admin"));
        using var adminClient = adminFactory.CreateClient();

        using var missingImdbResponse = await firstClient.PostAsJsonAsync(
            "/api/favorites",
            new { imdbId = "tt99999999", from = "oscar" });
        Assert.Equal(HttpStatusCode.NotFound, missingImdbResponse.StatusCode);
        foreach (var blockedImdbId in new[] { "tt11223344", "tt11223345", "tt11223346" })
        {
            using var blockedResponse = await firstClient.PostAsJsonAsync(
                "/api/favorites",
                new { imdbId = blockedImdbId, from = "catalog" });
            Assert.Equal(HttpStatusCode.NotFound, blockedResponse.StatusCode);
        }
        using var seriesCatalogAddResponse = await firstClient.PostAsJsonAsync(
            "/api/favorites",
            new { imdbId = seriesImdbId, from = "catalog" });
        Assert.Equal(HttpStatusCode.OK, seriesCatalogAddResponse.StatusCode);
        var seriesCatalogFavorite = await seriesCatalogAddResponse.Content.ReadFromJsonAsync<FavoriteMovieResponse>();
        Assert.NotNull(seriesCatalogFavorite);
        Assert.Equal("series", seriesCatalogFavorite.MediaType);
        Assert.False(seriesCatalogFavorite.ToWatch);
        Assert.True(seriesCatalogFavorite.ToDownload);
        Assert.True(seriesCatalogFavorite.AddedFromCatalog);

        using var seriesOscarAddResponse = await firstClient.PostAsJsonAsync(
            "/api/favorites",
            new { imdbId = seriesImdbId, from = "oscar" });
        Assert.Equal(HttpStatusCode.NotFound, seriesOscarAddResponse.StatusCode);

        using var removeSeriesCatalogFavoriteResponse = await firstClient.DeleteAsync($"/api/favorites/{seriesImdbId}");
        Assert.Equal(HttpStatusCode.NoContent, removeSeriesCatalogFavoriteResponse.StatusCode);

        using var recommendationAddResponse = await firstClient.PostAsJsonAsync(
            "/api/favorites",
            new { imdbId = secondImdbId, from = "recommendation" });
        Assert.Equal(HttpStatusCode.OK, recommendationAddResponse.StatusCode);
        var recommendationFavorite = await recommendationAddResponse.Content.ReadFromJsonAsync<FavoriteMovieResponse>();
        Assert.NotNull(recommendationFavorite);
        Assert.True(recommendationFavorite.ToWatch);
        Assert.False(recommendationFavorite.ToDownload);
        Assert.False(recommendationFavorite.AddedFromOscar);
        Assert.False(recommendationFavorite.AddedFromCatalog);
        Assert.True(recommendationFavorite.AddedFromRecommendations);

        using var unwatchRecommendationResponse = await firstClient.PatchAsJsonAsync(
            $"/api/favorites/{secondImdbId}",
            new { toWatch = false });
        Assert.Equal(HttpStatusCode.OK, unwatchRecommendationResponse.StatusCode);
        using var readdRecommendationResponse = await firstClient.PostAsJsonAsync(
            "/api/favorites",
            new { imdbId = secondImdbId, from = "recommendation" });
        var readdedRecommendation = await readdRecommendationResponse.Content.ReadFromJsonAsync<FavoriteMovieResponse>();
        Assert.Equal(HttpStatusCode.OK, readdRecommendationResponse.StatusCode);
        Assert.NotNull(readdedRecommendation);
        Assert.True(readdedRecommendation.ToWatch);
        Assert.False(readdedRecommendation.ToDownload);

        using var removeRecommendationResponse = await firstClient.DeleteAsync($"/api/favorites/{secondImdbId}");
        Assert.Equal(HttpStatusCode.NoContent, removeRecommendationResponse.StatusCode);

        using var firstAddResponse = await firstClient.PostAsJsonAsync(
            $"/api/favorites?userId={secondUserId}",
            new { imdbId = firstImdbId, from = "catalog", userId = secondUserId });
        Assert.Equal(HttpStatusCode.OK, firstAddResponse.StatusCode);
        var firstAdded = await firstAddResponse.Content.ReadFromJsonAsync<FavoriteMovieResponse>();
        Assert.NotNull(firstAdded);
        Assert.Equal(firstImdbId, firstAdded.ImdbId);
        Assert.Null(firstAdded.TorrentMovieId);
        Assert.False(firstAdded.AddedFromOscar);
        Assert.True(firstAdded.AddedFromCatalog);
        Assert.Equal(9.1m, firstAdded.ImdbRating);
        Assert.Equal(1200, firstAdded.ImdbVotes);
        Assert.Equal(88m, firstAdded.Metascore);
        Assert.Equal(["Drama", "Mystery"], firstAdded.Genres);
        Assert.Equal(["USA"], firstAdded.Countries);
        Assert.Equal("Mira Director", firstAdded.Director);
        Assert.Equal("A story about a changing city.", firstAdded.Plot);
        Assert.Equal("110 min", firstAdded.Runtime);
        Assert.Equal("1 win and 3 nominations.", firstAdded.Awards);
        Assert.Equal("$12 million", firstAdded.BoxOffice);
        Assert.Equal("https://example.test/canonical-shared.jpg", firstAdded.PosterUrl);
        Assert.Equal(1, firstAdded.OscarFilmCount);
        Assert.Equal(0, firstAdded.OccurrenceCount);

        using var firstOscarAddResponse = await firstClient.PostAsJsonAsync(
            "/api/favorites",
            new { imdbId = firstImdbId, from = "oscar" });
        Assert.Equal(HttpStatusCode.OK, firstOscarAddResponse.StatusCode);
        var firstOscarAdded = await firstOscarAddResponse.Content.ReadFromJsonAsync<FavoriteMovieResponse>();
        Assert.NotNull(firstOscarAdded);
        Assert.True(firstOscarAdded.AddedFromOscar);
        Assert.True(firstOscarAdded.AddedFromCatalog);
        Assert.True(firstOscarAdded.ToWatch);
        Assert.True(firstOscarAdded.ToDownload);

        using var secondAddResponse = await secondClient.PostAsJsonAsync(
            "/api/favorites",
            new { imdbId = firstImdbId, from = "catalog" });
        Assert.Equal(HttpStatusCode.OK, secondAddResponse.StatusCode);
        using var secondOnlyAddResponse = await secondClient.PostAsJsonAsync(
            "/api/favorites",
            new { imdbId = secondImdbId, from = "catalog" });
        Assert.Equal(HttpStatusCode.OK, secondOnlyAddResponse.StatusCode);

        using var adminListResponse = await adminClient.GetAsync($"/api/favorites?userId={secondUserId}");
        Assert.Equal(HttpStatusCode.OK, adminListResponse.StatusCode);
        var adminList = await adminListResponse.Content.ReadFromJsonAsync<PageResponse<FavoriteMovieResponse>>();
        Assert.Equal(0, adminList?.TotalCount);

        using var updateResponse = await firstClient.PatchAsJsonAsync(
            $"/api/favorites/{firstImdbId}",
            new { toWatch = true, toDownload = false });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<FavoriteMovieResponse>();
        Assert.NotNull(updated);
        Assert.True(updated.ToWatch);
        Assert.False(updated.ToDownload);

        using var repeatAddResponse = await firstClient.PostAsJsonAsync(
            "/api/favorites",
            new { imdbId = firstImdbId, from = "catalog" });
        var repeatAdded = await repeatAddResponse.Content.ReadFromJsonAsync<FavoriteMovieResponse>();
        Assert.Equal(HttpStatusCode.OK, repeatAddResponse.StatusCode);
        Assert.NotNull(repeatAdded);
        Assert.True(repeatAdded.ToWatch);
        Assert.False(repeatAdded.ToDownload);
        Assert.True(repeatAdded.AddedFromOscar);
        Assert.True(repeatAdded.AddedFromCatalog);

        using var firstListResponse = await firstClient.GetAsync(
            $"/api/favorites?userId={secondUserId}&page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, firstListResponse.StatusCode);
        var firstPage = await firstListResponse.Content.ReadFromJsonAsync<PageResponse<FavoriteMovieResponse>>();
        Assert.NotNull(firstPage);
        Assert.Equal(1, firstPage.TotalCount);
        Assert.Equal(1, firstPage.TotalPages);
        Assert.Equal(firstImdbId, Assert.Single(firstPage.Items).ImdbId);
        Assert.True(firstPage.Items[0].ToWatch);
        Assert.False(firstPage.Items[0].ToDownload);

        using var secondPageOneResponse = await secondClient.GetAsync("/api/favorites?page=1&pageSize=1");
        using var secondPageTwoResponse = await secondClient.GetAsync("/api/favorites?page=2&pageSize=1");
        var secondPageOne = await secondPageOneResponse.Content.ReadFromJsonAsync<PageResponse<FavoriteMovieResponse>>();
        var secondPageTwo = await secondPageTwoResponse.Content.ReadFromJsonAsync<PageResponse<FavoriteMovieResponse>>();
        Assert.Equal(HttpStatusCode.OK, secondPageOneResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondPageTwoResponse.StatusCode);
        Assert.NotNull(secondPageOne);
        Assert.NotNull(secondPageTwo);
        Assert.Equal(2, secondPageOne.TotalCount);
        Assert.Equal(2, secondPageOne.TotalPages);
        Assert.Equal(2, new[] { Assert.Single(secondPageOne.Items).ImdbId, Assert.Single(secondPageTwo.Items).ImdbId }.Distinct().Count());
        var secondUserSharedFavorite = new[] { secondPageOne.Items[0], secondPageTwo.Items[0] }
            .Single(favorite => favorite.ImdbId == firstImdbId);
        Assert.False(secondUserSharedFavorite.ToWatch);
        Assert.True(secondUserSharedFavorite.ToDownload);
        var localMetadataFavorite = new[] { secondPageOne.Items[0], secondPageTwo.Items[0] }
            .Single(favorite => favorite.ImdbId == secondImdbId);
        Assert.Equal(6.3m, localMetadataFavorite.ImdbRating);
        Assert.Equal("https://example.test/canonical-second.jpg", localMetadataFavorite.PosterUrl);

        using var firstWatchFilterResponse = await firstClient.GetAsync("/api/favorites?status=to_watch");
        using var secondWatchFilterResponse = await secondClient.GetAsync("/api/favorites?status=to_watch");
        var firstWatchFilter = await firstWatchFilterResponse.Content.ReadFromJsonAsync<PageResponse<FavoriteMovieResponse>>();
        var secondWatchFilter = await secondWatchFilterResponse.Content.ReadFromJsonAsync<PageResponse<FavoriteMovieResponse>>();
        Assert.Equal(1, firstWatchFilter?.TotalCount);
        Assert.Equal(0, secondWatchFilter?.TotalCount);

        using var firstDownloadFilterResponse = await firstClient.GetAsync("/api/favorites?status=to_download");
        using var secondDownloadFilterResponse = await secondClient.GetAsync("/api/favorites?status=to_download");
        var firstDownloadFilter = await firstDownloadFilterResponse.Content.ReadFromJsonAsync<PageResponse<FavoriteMovieResponse>>();
        var secondDownloadFilter = await secondDownloadFilterResponse.Content.ReadFromJsonAsync<PageResponse<FavoriteMovieResponse>>();
        Assert.Equal(0, firstDownloadFilter?.TotalCount);
        Assert.Equal(2, secondDownloadFilter?.TotalCount);

        using var seriesRecommendationAddResponse = await firstClient.PostAsJsonAsync(
            "/api/favorites",
            new { imdbId = seriesImdbId, from = "recommendation" });
        Assert.Equal(HttpStatusCode.OK, seriesRecommendationAddResponse.StatusCode);
        var seriesFavorite = await seriesRecommendationAddResponse.Content.ReadFromJsonAsync<FavoriteMovieResponse>();
        Assert.NotNull(seriesFavorite);
        Assert.Equal("series", seriesFavorite.MediaType);
        Assert.True(seriesFavorite.ToWatch);
        Assert.False(seriesFavorite.ToDownload);

        using var seriesDownloadUpdateResponse = await firstClient.PatchAsJsonAsync(
            $"/api/favorites/{seriesImdbId}",
            new { toDownload = true });
        Assert.Equal(HttpStatusCode.OK, seriesDownloadUpdateResponse.StatusCode);
        var seriesDownloadFavorite = await seriesDownloadUpdateResponse.Content.ReadFromJsonAsync<FavoriteMovieResponse>();
        Assert.NotNull(seriesDownloadFavorite);
        Assert.True(seriesDownloadFavorite.ToDownload);

        using var seriesWatchFilterResponse = await firstClient.GetAsync("/api/favorites?status=to_watch");
        var seriesWatchFilter = await seriesWatchFilterResponse.Content.ReadFromJsonAsync<PageResponse<FavoriteMovieResponse>>();
        Assert.Equal(HttpStatusCode.OK, seriesWatchFilterResponse.StatusCode);
        Assert.Contains(seriesWatchFilter?.Items, favorite => favorite.ImdbId == seriesImdbId && favorite.MediaType == "series");
        using var seriesDownloadFilterResponse = await firstClient.GetAsync("/api/favorites?status=to_download");
        var seriesDownloadFilter = await seriesDownloadFilterResponse.Content.ReadFromJsonAsync<PageResponse<FavoriteMovieResponse>>();
        Assert.Equal(HttpStatusCode.OK, seriesDownloadFilterResponse.StatusCode);
        Assert.Contains(seriesDownloadFilter?.Items, favorite => favorite.ImdbId == seriesImdbId && favorite.MediaType == "series");
        using var seriesDeleteResponse = await firstClient.DeleteAsync($"/api/favorites/{seriesImdbId}");
        Assert.Equal(HttpStatusCode.NoContent, seriesDeleteResponse.StatusCode);

        using var crossUserUpdateResponse = await firstClient.PatchAsJsonAsync(
            $"/api/favorites/{secondImdbId}",
            new { toWatch = true });
        Assert.Equal(HttpStatusCode.NotFound, crossUserUpdateResponse.StatusCode);
        using var noOccurrenceDownloadResponse = await firstClient.PatchAsJsonAsync(
            $"/api/favorites/{firstImdbId}",
            new { toDownload = true });
        Assert.Equal(HttpStatusCode.OK, noOccurrenceDownloadResponse.StatusCode);
        var noOccurrenceDownload = await noOccurrenceDownloadResponse.Content.ReadFromJsonAsync<FavoriteMovieResponse>();
        Assert.NotNull(noOccurrenceDownload);
        Assert.True(noOccurrenceDownload.ToDownload);
        Assert.Equal(0, noOccurrenceDownload.OccurrenceCount);

        using var firstDeleteResponse = await firstClient.DeleteAsync($"/api/favorites/{firstImdbId}");
        Assert.Equal(HttpStatusCode.NoContent, firstDeleteResponse.StatusCode);

        await using var verifyDb = new MediaDockDbContext(options);
        var favorites = await verifyDb.ImdbFavoriteMovies.AsNoTracking()
            .OrderBy(favorite => favorite.UserId)
            .ThenBy(favorite => favorite.ImdbId)
            .ToListAsync();
        Assert.Equal(2, favorites.Count);
        Assert.All(favorites, favorite => Assert.Equal(secondUserId, favorite.UserId));
        Assert.Contains(favorites, favorite => favorite.ImdbId == firstImdbId);
        Assert.Contains(favorites, favorite => favorite.ImdbId == secondImdbId);
        Assert.DoesNotContain(favorites, favorite => favorite.UserId == firstUserId);
    }
}
